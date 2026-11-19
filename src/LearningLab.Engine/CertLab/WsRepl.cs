// Cert Lab - sites and replication of the pretend Active Directory. A change on a DC reaches the DCs in its site about
// 15 seconds later (change notification); between sites it waits for the site link's schedule (every replInterval
// minutes, 180 by default, counted from Monday 00:00). A site that is in no site link gets nothing from other sites.
// The newer copy of an object wins (higher version, then the later change). repadmin /syncall and "Replicate Now"
// replicate at once. Group Policy objects live in the directory here, so SYSVOL travels with them.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public partial class WsLab
    {
        public const int IntraSiteSeconds = 15;
        static readonly DateTime LinkEpoch = new DateTime(2026, 1, 5, 0, 0, 0);
        public readonly Dictionary<string, DateTime> LastRepl = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);   // "A>B" -> last success

        public IEnumerable<WsAdObj> Sites(WsDir d) { return d.Children(SitesDn).Where(o => o.Is("site")); }
        public IEnumerable<WsAdObj> Subnets(WsDir d) { return d.Children("CN=Subnets," + SitesDn).Where(o => o.Is("subnet")); }
        public IEnumerable<WsAdObj> SiteLinks(WsDir d) { return d.Children("CN=IP,CN=Inter-Site Transports," + SitesDn).Where(o => o.Is("siteLink")); }
        public WsAdObj ServerObj(WsDir d, string dc) { return d.Live.FirstOrDefault(o => o.Is("server") && WsText.Eq(o.Name, dc)); }

        /// <summary>The site a DC's server object sits in (as DC1's copy of the configuration says).</summary>
        public string SiteOfDc(string dc)
        {
            var so = ServerObj(Main, dc);
            if (so == null) return null;
            return Dn.RdnValue(Dn.Parent(Dn.Parent(so.Dn)));
        }
        /// <summary>The site of an address: the most specific subnet object that holds it (null: no subnet matches).</summary>
        public string SiteOfIp(string ip)
        {
            string best = null; int bestLen = -1;
            foreach (var s in Subnets(Main))
            {
                var parts = s.Name.Split('/');
                int len; if (parts.Length != 2 || !int.TryParse(parts[1], out len)) continue;
                if (Ip4.SameNet(ip, parts[0], len) && len > bestLen) { bestLen = len; best = s.Get("siteObject"); }
            }
            return best;
        }

        /// <summary>The replication interval in minutes between two sites, or -1 when no site link (or chain of links) joins them.</summary>
        public int LinkInterval(string siteA, string siteB, out string linkName)
        {
            linkName = null;
            if (WsText.Eq(siteA, siteB)) return 0;
            var links = SiteLinks(Main).ToList();
            // breadth first over links (all links are bridged, as by default); the interval is the slowest on the path
            var best = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { { siteA, 0 } };
            var via = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var q = new Queue<string>(); q.Enqueue(siteA);
            while (q.Count > 0)
            {
                var s = q.Dequeue();
                foreach (var l in links.Where(l => l.List("siteList").Contains(s, StringComparer.OrdinalIgnoreCase)))
                    foreach (var t in l.List("siteList"))
                    {
                        if (best.ContainsKey(t)) continue;
                        int iv; if (!int.TryParse(l.Get("replInterval"), out iv)) iv = 180;
                        best[t] = Math.Max(best[s], iv); via[t] = l.Name;
                        q.Enqueue(t);
                    }
            }
            int r; if (!best.TryGetValue(siteB, out r)) return -1;
            via.TryGetValue(siteB, out linkName);
            return r;
        }

        /// <summary>When a change on DC a is due at DC b (null: never, no site link).</summary>
        public DateTime? NextReplTime(string a, string b)
        {
            string sa = SiteOfDc(a), sb = SiteOfDc(b), ln;
            if (sa == null || sb == null || WsText.Eq(sa, sb)) return Now.AddSeconds(IntraSiteSeconds);
            int iv = LinkInterval(sa, sb, out ln);
            if (iv < 0) return null;
            if (iv < 15) iv = 15;
            double mins = (Now - LinkEpoch).TotalMinutes;
            long k = (long)Math.Floor(mins / iv) + 1;
            return LinkEpoch.AddMinutes(k * iv);
        }

        /// <summary>A DC's copy changed: schedule replication to the others.</summary>
        public void Changed(string dc)
        {
            foreach (var other in DcNames.Where(x => !WsText.Eq(x, dc)))
            {
                var due = NextReplTime(dc, other);
                if (!due.HasValue) continue;
                string a = dc, b = other;
                At("repl:" + a + ">" + b, due.Value, () => { if (NextReplTime(a, b).HasValue) ReplicatePair(a, b, true); }, true);
            }
        }

        /// <summary>Copies what is newer on a to b. Returns the number of objects that changed on b.</summary>
        public int ReplicatePair(string a, string b, bool onward)
        {
            var src = Dir(a); var dst = Dir(b);
            if (src == null || dst == null) return 0;
            if (!DcUp(a) || !DcUp(b)) { At("repl:" + a + ">" + b, Now.AddMinutes(15), () => ReplicatePair(a, b, onward), true); return 0; }
            int n = 0;
            foreach (var o in src.Objs.Values)
            {
                WsAdObj have;
                if (dst.Objs.TryGetValue(o.Guid, out have) && (have.Version > o.Version || have.Version == o.Version && have.Stamp >= o.Stamp)) continue;
                dst.Objs[o.Guid] = o.Clone(); n++;
            }
            LastRepl[a + ">" + b] = Now;
            if (n > 0)
            {
                Note("Replication: " + n + " change" + (n == 1 ? "" : "s") + " from " + a + " reached " + b + ".");
                if (onward) foreach (var c in DcNames.Where(x => !WsText.Eq(x, a) && !WsText.Eq(x, b))) { var due = NextReplTime(b, c); string bb = b, cc = c; if (due.HasValue) At("repl:" + bb + ">" + cc, due.Value, () => ReplicatePair(bb, cc, true), true); }
                OnDirChanged(b);
            }
            return n;
        }
        /// <summary>repadmin /syncall /AdeP: every DC pulls from every other, until nothing changes (sites and links ignored, as with /e).</summary>
        public int SyncAll()
        {
            int total = 0;
            for (int round = 0; round < 4; round++)
            {
                int n = 0;
                foreach (var a in DcNames.ToList()) foreach (var b in DcNames.ToList()) if (!WsText.Eq(a, b)) n += ReplicatePair(a, b, false);
                total += n;
                if (n == 0) break;
            }
            foreach (var a in DcNames) foreach (var b in DcNames) if (!WsText.Eq(a, b)) Cancel("repl:" + a + ">" + b);
            return total;
        }
        /// <summary>Is b missing anything that a has (a backlog)?</summary>
        public int Backlog(string a, string b)
        {
            var src = Dir(a); var dst = Dir(b);
            if (src == null || dst == null) return 0;
            int n = 0;
            foreach (var o in src.Objs.Values) { WsAdObj h; if (!dst.Objs.TryGetValue(o.Guid, out h) || h.Version < o.Version || h.Version == o.Version && h.Stamp < o.Stamp) n++; }
            return n;
        }
        partial void DirChanged(string dc);
        void OnDirChanged(string dc) { DirChanged(dc); }
    }
}
