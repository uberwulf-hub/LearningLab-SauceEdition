// Cert Lab - Group Policy processing (the resultant set of policy) for a computer and a user, as Windows does it:
// LSDOU order (the local policy isn't simulated, then site, domain and OUs from the top down), link order within a
// container (link order 1 is applied last and wins), Block Inheritance (drops what is linked above, except enforced
// links), Enforced links (applied after everything else, the highest container last so it wins), disabled links,
// GPO status, security filtering (Read + Apply; since MS16-072 the computer must also be able to read a GPO that
// holds user settings), WMI filters (evaluated on the computer), and loopback processing (Merge or Replace).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public class RsopGpo { public WsAdObj Gpo; public string Name = "", LinkedTo = "", Reason = ""; public bool Enforced; public string Scope = "C"; }
    public class RsopValue { public string State = "", Value = "", Gpo = ""; public GpSetting Def; public string Id = ""; }

    public class RsopResult
    {
        public string Computer = "", User = "", Dc = "", Site = "", Loopback = "";
        public DateTime When;
        public readonly List<RsopGpo> AppliedC = new List<RsopGpo>(), AppliedU = new List<RsopGpo>(), DeniedC = new List<RsopGpo>(), DeniedU = new List<RsopGpo>();
        public readonly Dictionary<string, RsopValue> C = new Dictionary<string, RsopValue>(StringComparer.OrdinalIgnoreCase), U = new Dictionary<string, RsopValue>(StringComparer.OrdinalIgnoreCase);
        public RsopValue Get(string id)
        {
            var def = GpCatalog.Get(id);
            string key = def != null ? def.Id : id;
            RsopValue v;
            if ((def == null || def.Scope == "C") && C.TryGetValue(key, out v)) return v;
            if ((def == null || def.Scope == "U") && U.TryGetValue(key, out v)) return v;
            return null;
        }
    }

    public partial class WsLab
    {
        /// <summary>The containers a DN inherits from, top first: the domain, then each OU down to the object's parent.</summary>
        List<WsAdObj> Chain(WsDir d, string dn)
        {
            var res = new List<WsAdObj>();
            for (string p = Dn.Parent(dn); p.Length > 0; p = Dn.Parent(p))
            {
                var o = d.ByDn(p);
                if (o != null && (o.Is("organizationalUnit") || o.Is("domainDNS"))) res.Insert(0, o);
                if (o != null && o.Is("domainDNS")) break;
            }
            return res;
        }

        /// <summary>The GPOs that reach an object, in the order they are applied, with the ones left out and why.</summary>
        public List<RsopGpo> GpoOrder(WsDir d, string dn, string site, List<RsopGpo> skipped)
        {
            var chain = Chain(d, dn);
            var containers = new List<WsAdObj>();
            var siteObj = site == null ? null : Sites(d).FirstOrDefault(s => WsText.Eq(s.Name, site));
            if (siteObj != null) containers.Add(siteObj);
            containers.AddRange(chain);
            int blockAt = -1;   // the deepest container that blocks inheritance
            for (int i = 0; i < containers.Count; i++) if (containers[i].Get("gPOptions") == "1" && containers[i].Is("organizationalUnit")) blockAt = i;
            var normal = new List<RsopGpo>(); var enforced = new List<RsopGpo>();
            for (int i = 0; i < containers.Count; i++)
            {
                var c = containers[i];
                var links = Links(c);
                var ce = new List<RsopGpo>();
                for (int k = links.Count - 1; k >= 0; k--)   // link order n first, link order 1 last
                {
                    var g = d.ById(links[k][0]);
                    if (g == null) continue;
                    var r = new RsopGpo { Gpo = g, Name = g.Get("displayName"), LinkedTo = c.Is("site") ? c.Name : Dn.Canonical(c.Dn), Enforced = links[k][2] == "1" };
                    if (links[k][1] != "1") { r.Reason = "Disabled (Link)"; if (skipped != null) skipped.Add(r); continue; }
                    if (r.Enforced) { ce.Add(r); continue; }
                    if (i < blockAt) { r.Reason = "Blocked (Inheritance)"; if (skipped != null) skipped.Add(r); continue; }
                    normal.Add(r);
                }
                enforced.InsertRange(0, ce);
            }
            normal.AddRange(enforced);
            // the same GPO linked twice applies once, at its last (winning) position
            var res = new List<RsopGpo>();
            for (int i = 0; i < normal.Count; i++) if (!normal.Skip(i + 1).Any(x => x.Gpo == normal[i].Gpo)) res.Add(normal[i]);
            return res;
        }

        /// <summary>Group Policy for a computer and (optionally) a user, as the DC dc holds it.</summary>
        public RsopResult Rsop(string dc, WsAdObj computer, WsAdObj user, string siteOverride)
        {
            var d = Dir(dc) ?? Main;
            var r = new RsopResult { Dc = dc, When = Now, Computer = computer == null ? "" : computer.Name, User = user == null ? "" : user.Sam };
            string ip = computer == null ? null : computer.Get("ipv4");
            var cm = computer == null ? null : M(computer.Name);
            if (cm != null) ip = cm.Ip;
            r.Site = siteOverride ?? (ip == null ? null : SiteOfIp(ip));
            var ctoken = computer == null ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Authenticated Users", "Domain Computers", "Everyone" } : TokenNames(d, computer);
            if (computer != null && computer.Yes("isDc")) ctoken.Add("ENTERPRISE DOMAIN CONTROLLERS");
            var compPath = computer == null ? new List<RsopGpo>() : GpoOrder(d, computer.Dn, r.Site, r.DeniedC);
            foreach (var g in compPath)
            {
                string why = Filter(g.Gpo, "C", ctoken, null, cm);
                if (why != null) { g.Reason = why; r.DeniedC.Add(g); continue; }
                r.AppliedC.Add(g);
                Merge(r.C, g, "C");
            }
            RsopValue lb;
            if (r.C.TryGetValue("Loopback", out lb) && lb.State == "Enabled") r.Loopback = lb.Value.Length > 0 ? lb.Value : "Merge";
            if (user == null) return r;
            var utoken = TokenNames(d, user);
            var list = new List<RsopGpo>();
            if (r.Loopback != "Replace") list.AddRange(GpoOrder(d, user.Dn, r.Site, r.DeniedU));
            if (r.Loopback.Length > 0) foreach (var g in GpoOrder(d, computer.Dn, r.Site, null)) list.Add(new RsopGpo { Gpo = g.Gpo, Name = g.Name, LinkedTo = g.LinkedTo + " (loopback)", Enforced = g.Enforced, Scope = "U" });
            foreach (var g in list)
            {
                g.Scope = "U";
                string why = Filter(g.Gpo, "U", utoken, ctoken, cm);
                if (why != null) { g.Reason = why; r.DeniedU.Add(g); continue; }
                r.AppliedU.Add(g);
                Merge(r.U, g, "U");
            }
            return r;
        }

        /// <summary>Why a GPO doesn't apply (null: it applies).</summary>
        string Filter(WsAdObj g, string scope, HashSet<string> token, HashSet<string> computerToken, PsMachine cm)
        {
            if (!ScopeEnabled(g, scope)) return "Disabled (GPO)";
            if (!CanRead(g, token) || !Applies(g, token)) return "Denied (Security)";
            if (computerToken != null && !CanRead(g, computerToken)) return "Denied (Security: the computer can't read it)";
            var wid = g.Get("wmiFilter");
            if (wid.Length > 0)
            {
                var w = Main.ById(wid);
                if (w != null && cm != null && !WsWmi.Matches(cm, this, w.Get("query"))) return "Denied (WMI Filter)";
            }
            if (Settings(g, scope).Count == 0) return "Not Applied (Empty)";
            return null;
        }
        static void Merge(Dictionary<string, RsopValue> into, RsopGpo g, string scope)
        {
            foreach (var kv in Settings(g.Gpo, scope))
            {
                var p = kv.Value.Split(new[] { '|' }, 2);
                into[kv.Key] = new RsopValue { Id = kv.Key, State = p[0], Value = p.Length > 1 ? p[1] : "", Gpo = g.Name, Def = GpCatalog.Get(kv.Key) };
            }
        }
    }
}
