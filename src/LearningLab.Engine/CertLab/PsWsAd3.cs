// Cert Lab - ActiveDirectory module: the domain and forest, domain controllers, operations masters, sites, subnets,
// site links and replication (Get-ADReplicationPartnerMetadata, Sync-ADObject).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public partial class PsCmdlets
    {
        static PsObj Ado(string type, params object[] kv)
        {
            var p = new PsObj("Microsoft.ActiveDirectory.Management." + type, "Microsoft.ActiveDirectory.Management.ADEntity", "System.Object");
            for (int i = 0; i + 2 < kv.Length + 1 && i + 1 < kv.Length; i += 2) p.Val((string)kv[i], kv[i + 1] is bool ? "bool" : kv[i + 1] is object[] ? "Microsoft.ActiveDirectory.Management.ADPropertyValueCollection" : kv[i + 1] is int ? "int" : "string", kv[i + 1]);
            return p;
        }
        static object[] Arr(IEnumerable<string> l) { return l.Select(x => (object)x).ToArray(); }

        static PsObj DcObj(WsLab lab, string dc)
        {
            var s = lab.Server(dc);
            var roles = WsLab.FsmoRoles.Where(r => WsText.Eq(lab.Fsmo[r], dc)).ToList();
            var so = lab.ServerObj(lab.Main, dc);
            var comp = lab.Main.Find(dc, "computer");
            var p = Ado("ADDomainController", "ComputerObjectDN", comp == null ? "" : comp.Dn, "DefaultPartition", lab.DomainDn, "Domain", lab.DomainDns, "Enabled", true, "Forest", lab.DomainDns,
                "HostName", s.M.Fqdn, "IPv4Address", s.M.Ip, "IsGlobalCatalog", so != null && so.Get("options") == "1", "IsReadOnly", s.M.FindFile("C:\\Windows\\NTDS\\rodc.flag") != null, "Name", s.Name,
                "OperatingSystem", "Windows Server 2022 Datacenter", "OperatingSystemVersion", "10.0 (20348)", "OperationMasterRoles", Arr(roles), "ServerObjectDN", so == null ? "" : so.Dn, "Site", lab.SiteOfDc(dc) ?? "");
            p.ToStr = x => s.M.Fqdn;
            return p;
        }

        static partial void RegWsAd3()
        {
            AdCmd("Get-ADDomain", "Identity:object@0 pipe; Current:string set=LocalComputer|LoggedOnUser; Server:string; Credential:pscredential", (c, o) =>
            {
                var lab = Lab(c); string dc; var dir = AdDir(c, out dc); var dom = dir.ByDn(lab.DomainDn);
                var p = Ado("ADDomain", "AllowedDNSSuffixes", new object[0], "ChildDomains", new object[0], "ComputersContainer", lab.ComputersDn, "DistinguishedName", lab.DomainDn, "DNSRoot", lab.DomainDns,
                    "DomainControllersContainer", "OU=Domain Controllers," + lab.DomainDn, "DomainMode", dom.Get("domainMode"), "DomainSID", "S-1-5-21-3623811015-3361044348-30300820", "Forest", lab.DomainDns,
                    "InfrastructureMaster", lab.Server(lab.Fsmo["InfrastructureMaster"]).M.Fqdn, "LinkedGroupPolicyObjects", Arr(WsLab.Links(dom).Select(l => "cn={" + l[0].ToUpperInvariant() + "},cn=policies,cn=system," + lab.DomainDn)),
                    "Name", "corp", "NetBIOSName", lab.NetBios, "ObjectClass", "domainDNS", "ParentDomain", null, "PDCEmulator", lab.Server(lab.Fsmo["PDCEmulator"]).M.Fqdn,
                    "ReplicaDirectoryServers", Arr(lab.DcNames.Select(n => lab.Server(n).M.Fqdn)), "RIDMaster", lab.Server(lab.Fsmo["RIDMaster"]).M.Fqdn, "UsersContainer", lab.UsersDn);
                p.ToStr = x => lab.DomainDn; c.Out(p);
            });
            AdCmd("Get-ADForest", "Identity:object@0 pipe; Current:string set=LocalComputer|LoggedOnUser; Server:string; Credential:pscredential", (c, o) =>
            {
                var lab = Lab(c); string dc; var dir = AdDir(c, out dc); var dom = dir.ByDn(lab.DomainDn);
                var p = Ado("ADForest", "ApplicationPartitions", Arr(new[] { "DC=ForestDnsZones," + lab.DomainDn, "DC=DomainDnsZones," + lab.DomainDn }), "CrossForestReferences", new object[0], "DomainNamingMaster", lab.Server(lab.Fsmo["DomainNamingMaster"]).M.Fqdn,
                    "Domains", Arr(new[] { lab.DomainDns }), "ForestMode", dom.Get("forestMode"), "GlobalCatalogs", Arr(lab.DcNames.Select(n => lab.Server(n).M.Fqdn)), "Name", lab.DomainDns, "RootDomain", lab.DomainDns,
                    "SchemaMaster", lab.Server(lab.Fsmo["SchemaMaster"]).M.Fqdn, "Sites", Arr(lab.Sites(dir).Select(s => s.Name)), "SPNSuffixes", new object[0], "UPNSuffixes", Arr(dom.List("upnSuffixes")));
                p.ToStr = x => lab.DomainDns; c.Out(p);
            });
            AdCmd("Set-ADForest", "Identity:object@0 pipe; UPNSuffixes:hashtable; Server:string; Credential:pscredential; PassThru:switch", (c, o) =>
            {
                var lab = Lab(c); string dc; var dir = AdDir(c, out dc); var dom = dir.ByDn(lab.DomainDn);
                var h = c.Hash("UPNSuffixes");
                if (h != null) foreach (DictionaryEntry e in h) foreach (var v in PsConv.Items(e.Value)) { var k = PsConv.ToStr(e.Key).ToLowerInvariant(); if (k == "add") dom.AddTo("upnSuffixes", PsConv.ToStr(v)); else if (k == "remove") dom.RemoveFrom("upnSuffixes", PsConv.ToStr(v)); else if (k == "replace") dom.SetList("upnSuffixes", PsConv.Items(e.Value).Select(x => PsConv.ToStr(x))); }
                lab.Touch(dc, dom);
            });
            var gdc = AdCmd("Get-ADDomainController", "Identity:object@0 pipe; Filter:string ps=Filter; Discover:switch ps=Discover; Service:string[] ps=Discover; SiteName:string ps=Discover; DomainName:string ps=Discover; Server:string; Credential:pscredential", (c, o) =>
            {
                var lab = Lab(c);
                if (c.Has("Filter")) { foreach (var n in lab.DcNames) c.Out(DcObj(lab, n)); return; }
                if (c.Has("Identity")) { var s = lab.Server(PsConv.ToStr(PsConv.Base(c.Get("Identity")))); if (s == null || !lab.Dirs.ContainsKey(s.Name)) throw NotFound(c, PsConv.ToStr(c.Get("Identity")), lab); c.Out(DcObj(lab, s.Name)); return; }
                string err; var dc = c.Has("SiteName") ? lab.DcNames.FirstOrDefault(n => lab.DcUp(n) && WsText.Eq(lab.SiteOfDc(n), c.Str("SiteName"))) : lab.LocateDc(c.M, null, out err);
                if (dc == null) throw c.Fail("Directory object not found: no domain controller could be found" + (c.Has("SiteName") ? " in the site " + c.Str("SiteName") : "") + ".", "ActiveDirectoryCmdlet:Microsoft.ActiveDirectory.Management.ADServerDownException", "ResourceUnavailable", null, "Microsoft.ActiveDirectory.Management.ADServerDownException");
                c.Out(DcObj(lab, dc));
            });
            gdc.DefaultSet = "Discover";
            var mv = AdCmd("Move-ADDirectoryServerOperationMasterRole", "Identity:object@0 ! pipe; OperationMasterRole:string[]@1 !; Force:switch; Server:string; Credential:pscredential; PassThru:switch", (c, o) =>
            {
                var lab = Lab(c); string target = PsConv.ToStr(PsConv.Base(c.Get("Identity")));
                foreach (var r in c.Strs("OperationMasterRole"))
                {
                    if (!c.ShouldProcess(target, "Move operation master role " + r)) continue;
                    AdFail(c, lab.MoveFsmo(r, target, c.Sw("Force")), target);
                }
            });
            mv.ShouldProcess = true; mv.ConfirmImpact = "High";
            AdCmd("Move-ADDirectoryServer", "Identity:object@0 ! pipe; Site:string@1 !; Server:string; Credential:pscredential; PassThru:switch", (c, o) =>
            {
                var lab = Lab(c); string dc = AdDc(c);
                AdFail(c, lab.MoveDcToSite(dc, PsConv.ToStr(PsConv.Base(c.Get("Identity"))), c.Str("Site")), c.Str("Site"));
            });
            foreach (var kind in new[] { "Site", "Subnet", "SiteLink" })
            {
                string k = kind;
                var g = AdCmd("Get-ADReplication" + k, "Identity:object@0 pipe ps=Identity; Filter:string ps=Filter; Properties:string[]; Server:string; Credential:pscredential", (c, o) =>
                {
                    var lab = Lab(c); string dc; var dir = AdDir(c, out dc);
                    var all = k == "Site" ? lab.Sites(dir) : k == "Subnet" ? lab.Subnets(dir) : lab.SiteLinks(dir);
                    var list = c.Has("Identity") ? all.Where(x => WsText.Eq(x.Name, PsConv.ToStr(PsConv.Base(c.Get("Identity"))))).ToList() : all.ToList();
                    if (c.Has("Identity") && list.Count == 0) throw NotFound(c, PsConv.ToStr(c.Get("Identity")), lab);
                    foreach (var x in list) c.Out(SiteThing(lab, k, x));
                });
                g.DefaultSet = "Filter";
            }
            AdCmd("New-ADReplicationSite", "Name:string@0 !; Description:string" + AdCommon, (c, o) => { var lab = Lab(c); AdFail(c, lab.NewSite(AdDc(c), c.Str("Name"), null), c.Str("Name")); });
            AdCmd("New-ADReplicationSubnet", "Name:string@0 !; Site:string@1; Location:string; Description:string" + AdCommon, (c, o) => { var lab = Lab(c); AdFail(c, lab.NewSubnet(AdDc(c), c.Str("Name"), c.Str("Site"), c.Str("Location")), c.Str("Name")); });
            AdCmd("New-ADReplicationSiteLink", "Name:string@0 !; SitesIncluded:string[]@1; Cost:int; ReplicationFrequencyInMinutes:int; InterSiteTransportProtocol:string set=IP|SMTP; Description:string" + AdCommon, (c, o) =>
            {
                var lab = Lab(c); AdFail(c, lab.NewSiteLink(AdDc(c), c.Str("Name"), (c.Strs("SitesIncluded") ?? new string[0]).ToList(), c.Int("Cost", 100), c.Int("ReplicationFrequencyInMinutes", 180)), c.Str("Name"));
            });
            AdCmd("Set-ADReplicationSiteLink", "Identity:object@0 ! pipe; Cost:int; ReplicationFrequencyInMinutes:int; SitesIncluded:hashtable; Description:string" + AdCommon, (c, o) =>
            {
                var lab = Lab(c); string dc; var dir = AdDir(c, out dc);
                var l = lab.SiteLinks(dir).FirstOrDefault(x => WsText.Eq(x.Name, PsConv.ToStr(PsConv.Base(c.Get("Identity")))));
                if (l == null) throw NotFound(c, PsConv.ToStr(c.Get("Identity")), lab);
                if (c.Has("ReplicationFrequencyInMinutes")) { if (c.Int("ReplicationFrequencyInMinutes", 0) < 15) AdFail(c, "The replication interval can't be shorter than 15 minutes.", l.Name); l.Set("replInterval", c.Str("ReplicationFrequencyInMinutes")); }
                if (c.Has("Cost")) l.Set("cost", c.Str("Cost"));
                var h = c.Hash("SitesIncluded");
                if (h != null) foreach (DictionaryEntry e in h) foreach (var v in PsConv.Items(e.Value)) { if (WsText.Eq(PsConv.ToStr(e.Key), "Add")) { if (!lab.Sites(dir).Any(s => WsText.Eq(s.Name, PsConv.ToStr(v)))) AdFail(c, "There is no site " + v + ".", v); l.AddTo("siteList", PsConv.ToStr(v)); } else l.RemoveFrom("siteList", PsConv.ToStr(v)); }
                lab.Touch(dc, l);
            });
            AdCmd("Get-ADReplicationPartnerMetadata", "Target:object[]@0 !; Partition:string; PartnerType:string; Scope:string; Server:string; Credential:pscredential", (c, o) =>
            {
                var lab = Lab(c);
                foreach (var t in c.Strs("Target"))
                {
                    var s = lab.Server(t); if (s == null || !lab.Dirs.ContainsKey(s.Name)) throw NotFound(c, t, lab);
                    foreach (var p in lab.DcNames.Where(x => !WsText.Eq(x, s.Name)))
                    {
                        DateTime last; bool ok = lab.LastRepl.TryGetValue(p + ">" + s.Name, out last);
                        var po = Ado("ADReplicationPartnerMetadata", "ConsecutiveReplicationFailures", lab.DcUp(p) ? 0 : 1, "LastReplicationAttempt", WsText.Date(ok ? last : lab.Now.AddHours(-1)), "LastReplicationResult", lab.DcUp(p) ? 0 : 1722,
                            "LastReplicationSuccess", WsText.Date(ok ? last : lab.Now.AddHours(-1)), "Partition", lab.DomainDn, "Partner", "CN=NTDS Settings,CN=" + p + ",CN=Servers,CN=" + lab.SiteOfDc(p) + "," + lab.SitesDn, "PartnerAddress", lab.Server(p).M.Fqdn, "PartnerType", "Inbound", "Server", s.M.Fqdn, "Backlog", lab.Backlog(p, s.Name));
                        c.Out(po);
                    }
                }
            });
            AdCmd("Sync-ADObject", "Object:object@0 ! pipe; Source:string; Destination:string !; PasswordOnly:switch; PassThru:switch; Credential:pscredential", (c, o) =>
            {
                var lab = Lab(c);
                var src = lab.Server(c.Str("Source") ?? lab.Fsmo["PDCEmulator"]); var dst = lab.Server(c.Str("Destination"));
                if (src == null || dst == null || !lab.DcUp(src.Name) || !lab.DcUp(dst.Name)) AdFail(c, "The source and destination must be running domain controllers.", c.Str("Destination"));
                var x = AdFind(c, lab.Dir(src.Name), c.Get("Object"), null);
                lab.Dir(dst.Name).Objs[x.Guid] = x.Clone();
            });
        }

        static PsObj SiteThing(WsLab lab, string kind, WsAdObj x)
        {
            PsObj p;
            if (kind == "Site") p = Ado("ADReplicationSite", "Description", x.Get("description"), "DistinguishedName", x.Dn, "Name", x.Name, "ObjectClass", "site");
            else if (kind == "Subnet") p = Ado("ADReplicationSubnet", "DistinguishedName", x.Dn, "Location", x.Get("location"), "Name", x.Name, "ObjectClass", "subnet", "Site", x.Get("siteObject").Length == 0 ? null : "CN=" + x.Get("siteObject") + "," + lab.SitesDn);
            else p = Ado("ADReplicationSiteLink", "Cost", int.Parse(x.Get("cost"), CultureInfo.InvariantCulture), "DistinguishedName", x.Dn, "Name", x.Name, "ObjectClass", "siteLink", "ReplicationFrequencyInMinutes", int.Parse(x.Get("replInterval"), CultureInfo.InvariantCulture), "SitesIncluded", Arr(x.List("siteList").Select(s => "CN=" + s + "," + lab.SitesDn)));
            p.Base = x; p.ToStr = y => x.Dn;
            return p;
        }
    }
}
