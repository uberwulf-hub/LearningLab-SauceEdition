// Cert Lab - console actions for Active Directory Sites and Services, Active Directory Domains and Trusts and the
// forest (FSMO roles, the Recycle Bin, UPN suffixes, trusts, replicate now).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public class WsTrust { public string Name = "", Type = "Forest", Direction = "Bidirectional"; public bool Selective, Transitive = true; public DateTime Created; }

    public partial class WsLab
    {
        public readonly List<WsTrust> Trusts = new List<WsTrust>();
        public List<string> UpnSuffixes { get { var d = Main.ByDn(DomainDn); return d == null ? new List<string>() : d.List("upnSuffixes"); } }

        public string NewSite(string dc, string name, string link)
        {
            var d = Dir(dc);
            if (Sites(d).Any(s => WsText.Eq(s.Name, name))) return "A site named " + name + " already exists.";
            Create(dc, "site", "CN=" + name + "," + SitesDn);
            Create(dc, "container", "CN=Servers,CN=" + name + "," + SitesDn);
            if (!string.IsNullOrEmpty(link))
            {
                var l = SiteLinks(d).FirstOrDefault(x => WsText.Eq(x.Name, link));
                if (l == null) return "There is no site link " + link + ".";
                l.AddTo("siteList", name); Touch(dc, l);
            }
            return null;
        }
        public string NewSubnet(string dc, string cidr, string site, string location)
        {
            var d = Dir(dc);
            var p = (cidr ?? "").Split('/'); int len;
            if (p.Length != 2 || !Ip4.IsV4(p[0]) || !int.TryParse(p[1], out len) || len < 1 || len > 32 || Ip4.Network(p[0], len) != p[0]) return "A subnet is written as network/prefix, for example 10.20.0.0/24.";
            if (Subnets(d).Any(s => WsText.Eq(s.Name, cidr))) return "The subnet " + cidr + " already exists.";
            if (!string.IsNullOrEmpty(site) && !Sites(d).Any(s => WsText.Eq(s.Name, site))) return "There is no site " + site + ".";
            Create(dc, "subnet", "CN=" + cidr + ",CN=Subnets," + SitesDn, "siteObject", Sites(d).Where(s => WsText.Eq(s.Name, site)).Select(s => s.Name).FirstOrDefault() ?? "", "location", location ?? "");
            return null;
        }
        public string NewSiteLink(string dc, string name, List<string> sites, int cost, int interval)
        {
            var d = Dir(dc);
            if (SiteLinks(d).Any(s => WsText.Eq(s.Name, name))) return "A site link named " + name + " already exists.";
            if (sites.Count < 2) return "A site link joins at least two sites.";
            foreach (var s in sites) if (!Sites(d).Any(x => WsText.Eq(x.Name, s))) return "There is no site " + s + ".";
            if (interval < 15) return "The replication interval can't be shorter than 15 minutes.";
            var l = Create(dc, "siteLink", "CN=" + name + ",CN=IP,CN=Inter-Site Transports," + SitesDn, "cost", cost.ToString(CultureInfo.InvariantCulture), "replInterval", interval.ToString(CultureInfo.InvariantCulture));
            l.SetList("siteList", sites.Select(s => Sites(d).First(x => WsText.Eq(x.Name, s)).Name)); Touch(dc, l);
            return null;
        }
        public string MoveDcToSite(string dc, string server, string site)
        {
            var d = Dir(dc);
            var so = ServerObj(d, server);
            if (so == null) return server + " isn't a domain controller.";
            var st = Sites(d).FirstOrDefault(s => WsText.Eq(s.Name, site));
            if (st == null) return "There is no site " + site + ".";
            return Move(dc, so, "CN=Servers," + st.Dn);
        }
        public string MoveFsmo(string role, string target, bool seize)
        {
            string r = FsmoRoles.FirstOrDefault(x => WsText.Eq(x, role) || WsText.Eq(x.Replace("Master", ""), role) || role == ((Array.IndexOf(FsmoRoles, x)).ToString(CultureInfo.InvariantCulture)));
            if (r == null) return "Unknown operations master role " + role + ": SchemaMaster, DomainNamingMaster, PDCEmulator, RIDMaster or InfrastructureMaster (or 0-4).";
            var t = Server(target);
            if (t == null || !Dirs.ContainsKey(t.Name)) return target + " isn't a domain controller.";
            if (!DcUp(t.Name)) return "The target " + t.Name + " isn't running.";
            string holder = Fsmo[r];
            if (WsText.Eq(holder, t.Name)) return null;
            if (!DcUp(holder) && !seize) return "The role " + r + " can't be transferred because its current holder " + holder + " can't be contacted. If it is gone for good, seize the role (-Force).";
            if (DcUp(holder)) ReplicatePair(holder, t.Name, false);
            Fsmo[r] = t.Name;
            Note((DcUp(holder) ? "Transferred " : "Seized ") + r + " from " + holder + " to " + t.Name + ".");
            return null;
        }
        public string CreateTrust(string dc, string partner, string type, string direction, bool selective)
        {
            if (Trusts.Any(x => WsText.Eq(x.Name, partner))) return "A trust with " + partner + " already exists.";
            var probe = Query(M(dc).Ip, "_ldap._tcp." + partner, "SRV", M(dc).Ip, 0);
            if (probe.Rcode != "OK" || probe.Answers.Count == 0) return "The specified domain either does not exist or could not be contacted: " + dc + " can't find a domain controller for " + partner + " in DNS. Add a conditional forwarder (or a stub zone) for " + partner + " first.";
            string ty = WsText.Eq(type, "External") ? "External" : WsText.Eq(type, "Realm") ? "Realm" : WsText.Eq(type, "Shortcut") ? "Shortcut" : "Forest";
            string dir = WsText.Eq(direction, "Inbound") ? "Inbound" : WsText.Eq(direction, "Outbound") ? "Outbound" : "Bidirectional";
            Trusts.Add(new WsTrust { Name = partner.ToLowerInvariant(), Type = ty, Direction = dir, Selective = selective, Transitive = ty == "Forest" || ty == "Shortcut", Created = Now });
            return null;
        }
    }

    public static partial class WsOps
    {
        static partial void RunAd2(WsLab lab, string v, string noun, List<string> tok, ref WsResult r)
        {
            var a = Parse(tok, 2);
            string dc = Dc(lab, a, false);
            switch (v + " " + noun)
            {
                case "new site": r = R(lab.NewSite(dc, a["name"] ?? a.P(0), a["link"]), "Created the site " + (a["name"] ?? a.P(0)) + "."); return;
                case "new subnet": r = R(lab.NewSubnet(dc, a["name"] ?? a.P(0), a["site"], a["location"]), "Created the subnet " + (a["name"] ?? a.P(0)) + "."); return;
                case "new sitelink": r = R(lab.NewSiteLink(dc, a["name"] ?? a.P(0), (a["sites"] ?? "").Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList(), a.Int("cost", 100), a.Int("interval", 180)), "Created the site link."); return;
                case "set sitelink":
                    {
                        var l = lab.SiteLinks(lab.Dir(dc)).FirstOrDefault(x => WsText.Eq(x.Name, a.P(0)));
                        if (l == null) { r = WsResult.Fail("There is no site link " + a.P(0) + "."); return; }
                        if (a.Has("interval")) { if (a.Int("interval", 0) < 15) { r = WsResult.Fail("The replication interval can't be shorter than 15 minutes."); return; } l.Set("replInterval", a["interval"]); }
                        if (a.Has("cost")) l.Set("cost", a["cost"]);
                        if (a.Has("+sites")) foreach (var s in a["+sites"].Split(',')) { if (!lab.Sites(lab.Dir(dc)).Any(x => WsText.Eq(x.Name, s.Trim()))) { r = WsResult.Fail("There is no site " + s + "."); return; } l.AddTo("siteList", s.Trim()); }
                        if (a.Has("-sites")) foreach (var s in a["-sites"].Split(',')) l.RemoveFrom("siteList", s.Trim());
                        lab.Touch(dc, l); r = WsResult.Done("Saved the site link " + l.Name + "."); return;
                    }
                case "move dc": r = R(lab.MoveDcToSite(dc, a.P(0), a.P(1)), "Moved " + a.P(0) + " to the site " + a.P(1) + "."); return;
                case "replicate all": { int n = lab.SyncAll(); r = WsResult.Done("Replicated every DC with every other (" + n + " changes)."); return; }
            }
            var b = Parse(tok, 1);
            switch (v)
            {
                case "replicate":
                    {
                        var to = lab.Server(b.P(0)); var from = lab.Server(b["from"]);
                        if (to == null || from == null || !lab.Dirs.ContainsKey(to.Name) || !lab.Dirs.ContainsKey(from.Name)) { r = WsResult.Fail("replicate DC from=OTHERDC: both must be domain controllers."); return; }
                        if (!lab.DcUp(to.Name) || !lab.DcUp(from.Name)) { r = WsResult.Fail("The replication didn't happen: one of the DCs isn't running."); return; }
                        int n = lab.ReplicatePair(from.Name, to.Name, true);
                        r = WsResult.Done(to.Name + " replicated from " + from.Name + " (" + n + " changes)."); return;
                    }
                case "fsmo": r = R(lab.MoveFsmo(b.P(1), b.P(2), WsText.Eq(b.P(0), "seize")), "The role " + b.P(1) + " is now on " + b.P(2) + "."); return;
                case "enable":
                    if (noun != "recyclebin") return;
                    {
                        var f = lab.Dir(dc).ByDn("CN=Recycle Bin Feature,CN=Optional Features,CN=Directory Service,CN=Windows NT,CN=Services," + lab.ConfigDn);
                        f.Set("enabled", "TRUE"); lab.Touch(dc, f);
                        r = WsResult.Done("The Active Directory Recycle Bin is enabled (this can't be undone)."); return;
                    }
                case "upnsuffix":
                    {
                        var dom = lab.Dir(dc).ByDn(lab.DomainDn);
                        if (WsText.Eq(b.P(0), "add")) dom.AddTo("upnSuffixes", b.P(1)); else dom.RemoveFrom("upnSuffixes", b.P(1));
                        lab.Touch(dc, dom); r = WsResult.Done("UPN suffixes: " + WsText.Join(dom.List("upnSuffixes"))); return;
                    }
                case "trust": r = R(lab.CreateTrust(dc, b.P(1), b["type"], b["direction"], WsText.Eq(b["auth"], "Selective")), "Created the trust with " + b.P(1) + "."); return;
            }
        }
    }
}
