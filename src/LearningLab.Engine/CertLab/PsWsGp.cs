// Cert Lab - the GroupPolicy module on the AZ-800 machines with Group Policy Management (DCs, SRV1): GPOs, links,
// inheritance, registry-based settings, permissions (security filtering), reports and remote refresh. Commands talk to
// the PDC emulator unless -Server says otherwise, as GPMC does.
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
        const string GpMod = "GroupPolicy";
        static Func<PsMachine, bool> HasGp = m => m.Ws != null && m.FeatureInstalled("GPMC");
        static CmdletDef GpCmd(string name, string spec, Action<CmdCtx, object> run) { var d = Add(name, GpMod, spec, run); d.Available = HasGp; d.FqClass = "Microsoft.GroupPolicy.Commands." + name.Replace("-", "") + "Command"; return d; }
        static string GpDc(CmdCtx c)
        {
            var lab = Lab(c); string want = c.Str("Server") ?? lab.Fsmo["PDCEmulator"];
            if (!lab.DcUp(lab.Server(want) == null ? want : lab.Server(want).Name)) throw c.Fail("The specified domain controller " + want + " could not be contacted (the PDC emulator is the default; use -Server for another DC).", "GpoDcUnavailable", "ResourceUnavailable", want, "System.ArgumentException");
            return lab.Server(want).Name;
        }
        static WsAdObj GpFind(CmdCtx c, WsDir d)
        {
            var lab = Lab(c);
            object key = c.Has("Guid") ? c.Get("Guid") : c.Has("Name") ? c.Get("Name") : c.Get("DisplayName");
            var po = PsConv.Base(key) as PsObj;
            string k = po != null && po.Base is WsAdObj ? ((WsAdObj)po.Base).Guid : PsConv.ToStr(PsConv.Base(key));
            var g = lab.FindGpo(d, k);
            if (g == null) throw c.Fail("GPO with " + (c.Has("Guid") ? "ID" : "name") + " " + k + " was not found in the " + lab.DomainDns + " domain.", "GpoWithNameNotFound", "ObjectNotFound", k, "System.ArgumentException");
            return g;
        }
        static PsObj GpoObj(WsLab lab, WsDir d, WsAdObj g)
        {
            var p = new PsObj("Microsoft.GroupPolicy.Gpo", "System.Object") { Base = g };
            var w = d.ById(g.Get("wmiFilter"));
            p.Val("DisplayName", "string", g.Get("displayName")); p.Val("DomainName", "string", lab.DomainDns); p.Val("Owner", "string", g.Get("owner").Length > 0 ? g.Get("owner") : lab.NetBios + "\\Domain Admins");
            p.Val("Id", "guid", new Guid(g.Guid)); p.Val("GpoStatus", "Microsoft.GroupPolicy.GpoStatus", WsLab.Status(g)); p.Val("Description", "string", g.Get("description"));
            p.Val("CreationTime", "datetime", Dt(g.Get("whenCreated"), lab)); p.Val("ModificationTime", "datetime", Dt(g.Get("whenChanged").Length > 0 ? g.Get("whenChanged") : g.Get("whenCreated"), lab));
            p.Val("UserVersion", "string", "AD Version: " + g.Get("versionUser") + ", SysVol Version: " + g.Get("versionUser"));
            p.Val("ComputerVersion", "string", "AD Version: " + g.Get("versionComputer") + ", SysVol Version: " + g.Get("versionComputer"));
            p.Val("WmiFilter", "Microsoft.GroupPolicy.WmiFilter", w == null ? null : w.Get("msWMI-Name"));
            p.ToStr = x => "Microsoft.GroupPolicy.Gpo";
            return p;
        }
        static DateTime Dt(string s, WsLab lab) { DateTime t; return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out t) ? t : lab.Now; }
        static PsObj LinkObj(WsLab lab, WsDir d, WsAdObj g, WsAdObj target)
        {
            var l = WsLab.Links(target); int i = l.FindIndex(x => x[0] == g.Guid);
            var p = new PsObj("Microsoft.GroupPolicy.GpoLink", "System.Object");
            p.Val("GpoId", "guid", new Guid(g.Guid)); p.Val("DisplayName", "string", g.Get("displayName")); p.Val("Enabled", "bool", i >= 0 && l[i][1] == "1"); p.Val("Enforced", "bool", i >= 0 && l[i][2] == "1");
            p.Val("Target", "string", target.Dn); p.Val("Order", "int", i + 1);
            return p;
        }
        static WsAdObj GpTarget(CmdCtx c, WsDir d)
        {
            var lab = Lab(c); var t = lab.LinkTarget(d, c.Str("Target"));
            if (t == null) throw c.Fail("The \"" + c.Str("Target") + "\" path is not valid: it must name a site, the domain or an OU by its distinguished name.", "InvalidTarget", "InvalidArgument", c.Str("Target"), "System.ArgumentException");
            return t;
        }
        static bool YesNo(CmdCtx c, string n, bool def) { if (!c.Has(n)) return def; var s = c.Str(n); return WsText.Eq(s, "Yes") || WsText.True(s); }
        const string GpId = "Name:string@0 prop ps=ByName alias=DisplayName; Guid:object prop ps=ById alias=Id; Domain:string alias=DomainName; Server:string alias=DC";

        static partial void RegWsGp()
        {
            GpCmd("Get-GPO", GpId + "; All:switch ps=All", (c, o) =>
            {
                var lab = Lab(c); var d = lab.Dir(GpDc(c));
                if (c.Sw("All") || !c.Has("Name") && !c.Has("Guid")) { foreach (var g in lab.Gpos(d).OrderBy(x => x.Get("displayName"))) c.Out(GpoObj(lab, d, g)); return; }
                c.Out(GpoObj(lab, d, GpFind(c, d)));
            }).DefaultSet = "ByName";
            GpCmd("New-GPO", "Name:string@0 ! alias=DisplayName; Comment:string; StarterGpoName:string; Domain:string; Server:string", (c, o) =>
            {
                var lab = Lab(c); string dc = GpDc(c), err;
                var g = lab.NewGpo(dc, c.Str("Name"), c.Str("Comment"), out err);
                if (g == null) throw c.Fail(err, "GpoWithNameAlreadyExists", "ResourceExists", c.Str("Name"), "System.ArgumentException");
                c.Out(GpoObj(lab, lab.Dir(dc), g));
            });
            var rm = GpCmd("Remove-GPO", GpId + "; KeepLinks:switch", (c, o) =>
            {
                var lab = Lab(c); string dc = GpDc(c); var d = lab.Dir(dc); var g = GpFind(c, d);
                if (!c.ShouldProcess(g.Get("displayName"), "Remove-GPO")) return;
                WsOps.Run(lab, "gpo delete \"" + g.Guid + "\" server=" + dc);
            });
            rm.ShouldProcess = true;
            GpCmd("Rename-GPO", GpId + "; TargetName:string !", (c, o) => { var lab = Lab(c); string dc = GpDc(c); var g = GpFind(c, lab.Dir(dc)); var r = WsOps.Run(lab, "gpo rename \"" + g.Guid + "\" \"" + c.Str("TargetName") + "\" server=" + dc); if (!r.Ok) throw c.Fail(r.Text, "RenameFailed", "InvalidOperation", null, "System.ArgumentException"); c.Out(GpoObj(lab, lab.Dir(dc), g)); });
            GpCmd("Copy-GPO", "SourceName:string@0 alias=Name; SourceGuid:object; TargetName:string !; SourceDomain:string; TargetDomain:string; Server:string", (c, o) =>
            {
                var lab = Lab(c); string dc = GpDc(c);
                var r = WsOps.Run(lab, "gpo copy \"" + (c.Str("SourceName") ?? c.Str("SourceGuid")) + "\" \"" + c.Str("TargetName") + "\" server=" + dc);
                if (!r.Ok) throw c.Fail(r.Text, "CopyFailed", "InvalidOperation", null, "System.ArgumentException");
                c.Out(GpoObj(lab, lab.Dir(dc), lab.FindGpo(lab.Dir(dc), c.Str("TargetName"))));
            });
            foreach (var verb in new[] { "New", "Set", "Remove" })
            {
                string v = verb;
                GpCmd(verb + "-GPLink", GpId + "; Target:string !; LinkEnabled:string set=Yes|No|Unspecified; Enforced:string set=Yes|No|Unspecified; Order:int", (c, o) =>
                {
                    var lab = Lab(c); string dc = GpDc(c); var d = lab.Dir(dc); var g = GpFind(c, d); var t = GpTarget(c, d);
                    string err = v == "New" ? lab.Link(dc, g, t, YesNo(c, "LinkEnabled", true), YesNo(c, "Enforced", false), c.Int("Order", 0))
                        : v == "Remove" ? lab.Unlink(dc, g, t)
                        : lab.SetLink(dc, g, t, c.Has("LinkEnabled") ? (bool?)YesNo(c, "LinkEnabled", true) : null, c.Has("Enforced") ? (bool?)YesNo(c, "Enforced", false) : null, c.Int("Order", 0));
                    if (err != null) throw c.Fail(err, "GpoLinkFailed", "InvalidOperation", c.Str("Target"), "System.ArgumentException");
                    if (v != "Remove") c.Out(LinkObj(lab, d, g, t));
                });
            }
            GpCmd("Get-GPInheritance", "Target:string@0 !; Domain:string; Server:string", (c, o) =>
            {
                var lab = Lab(c); string dc = GpDc(c); var d = lab.Dir(dc); var t = GpTarget(c, d);
                c.Out(GpTargetInfo(lab, d, t));
            });
            GpCmd("Set-GPInheritance", "Target:string@0 !; IsBlocked:string@1 ! set=Yes|No; Domain:string; Server:string", (c, o) =>
            {
                var lab = Lab(c); string dc = GpDc(c); var d = lab.Dir(dc); var t = GpTarget(c, d);
                if (!t.Is("organizationalUnit") && !t.Is("domainDNS")) throw c.Fail("Inheritance can be blocked on a domain or an OU only.", "InvalidTarget", "InvalidArgument", t.Dn, "System.ArgumentException");
                lab.BlockInheritance(dc, t, YesNo(c, "IsBlocked", false));
                c.Out(GpTargetInfo(lab, d, t));
            });
            RegWsGp2();
        }

        /// <summary>A Som: the container, whether it blocks inheritance, its own links and every GPO that reaches it (precedence order).</summary>
        static PsObj GpTargetInfo(WsLab lab, WsDir d, WsAdObj t)
        {
            var p = new PsObj("Microsoft.GroupPolicy.Som", "System.Object");
            p.Val("Name", "string", t.Is("domainDNS") ? lab.DomainDns : t.Name); p.Val("ContainerType", "Microsoft.GroupPolicy.SomType", t.Is("domainDNS") ? "Domain" : t.Is("site") ? "Site" : "OU"); p.Val("Path", "string", t.Dn.ToLowerInvariant());
            p.Val("GpoInheritanceBlocked", "bool", t.Get("gPOptions") == "1");
            p.Val("GpoLinks", "Microsoft.GroupPolicy.GpoLink[]", WsLab.Links(t).Select(l => { var g = d.ById(l[0]); return g == null ? null : (object)g.Get("displayName"); }).Where(x => x != null).ToArray());
            var inh = lab.GpoOrder(d, "CN=probe," + t.Dn, null, null); inh.Reverse();
            p.Val("InheritedGpoLinks", "Microsoft.GroupPolicy.GpoLink[]", inh.Select(x => (object)x.Name).ToArray());
            return p;
        }
    }
}
