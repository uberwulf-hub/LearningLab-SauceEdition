// Cert Lab - the DhcpServer module on the AZ-800 machines (DHCP servers and machines with the DHCP tools): authorizing
// servers, IPv4 scopes, exclusions, options, reservations, leases, statistics and failover. -ComputerName picks the
// DHCP server (default: this machine).
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
        const string DhcpMod = "DhcpServer";
        static Func<PsMachine, bool> HasDhcpTools = m => m.Ws != null && (m.FeatureInstalled("DHCP") || m.FeatureInstalled("RSAT-DHCP"));
        static CmdletDef DhcpCmd(string name, string spec, Action<CmdCtx, object> run) { var d = Cdxml(Add(name, DhcpMod, spec + (spec.Length > 0 ? "; " : "") + "ComputerName:string alias=Cn; PassThru:switch; CimSession:object; Force:switch", run)); d.Available = HasDhcpTools; return d; }
        static WsServer DhcpSrv(CmdCtx c)
        {
            var s = Lab(c).Server(c.Str("ComputerName") ?? c.M.Name);
            if (s == null || s.Dhcp == null) throw c.Fail("Failed to enumerate scopes on DHCP server " + (c.Str("ComputerName") ?? c.M.Name) + ": the DHCP Server role isn't installed there.", "WIN32 1753", "NotSpecified", c.Str("ComputerName"), "Microsoft.Management.Infrastructure.CimException");
            if (!s.Running || !s.SvcRunning("DHCPServer")) throw c.Fail("The RPC server is unavailable: the DHCP Server service on " + s.Name + " isn't running.", "WIN32 1722", "ConnectionError", s.Name, "Microsoft.Management.Infrastructure.CimException");
            return s;
        }
        static void DhcpOk(CmdCtx c, string err, object target) { if (err != null) throw c.Fail(err, "WIN32 20000", "InvalidOperation", target, "Microsoft.Management.Infrastructure.CimException"); }
        static DhcpScope DhcpScopeOf(CmdCtx c, WsServer s)
        {
            var sc = s.Dhcp.Scope(c.Str("ScopeId"));
            if (sc == null) throw c.Fail("Failed to get information for scope " + c.Str("ScopeId") + " on DHCP server " + s.Name + ": the specified DHCP scope doesn't exist.", "WIN32 20022", "ObjectNotFound", c.Str("ScopeId"), "Microsoft.Management.Infrastructure.CimException");
            return sc;
        }
        static PsObj ScopeObj(DhcpScope sc)
        {
            var p = new PsObj("Microsoft.Management.Infrastructure.CimInstance#root/Microsoft/Windows/DHCP/DhcpServerv4Scope", "Microsoft.Management.Infrastructure.CimInstance");
            p.Val("ScopeId", "ipaddress", sc.ScopeId); p.Val("SubnetMask", "ipaddress", sc.Mask); p.Val("Name", "string", sc.Name); p.Val("State", "string", sc.State);
            p.Val("StartRange", "ipaddress", sc.Start); p.Val("EndRange", "ipaddress", sc.End); p.Val("LeaseDuration", "timespan", sc.Lease); p.Val("Description", "string", sc.Description);
            p.DefaultDisplay = new[] { "ScopeId", "SubnetMask", "Name", "State", "StartRange", "EndRange", "LeaseDuration" };
            return p;
        }
        static readonly Dictionary<int, string> OptNames = new Dictionary<int, string> { { 3, "Router" }, { 6, "DNS Servers" }, { 15, "DNS Domain Name" }, { 44, "WINS/NBNS Servers" }, { 46, "WINS/NBT Node Type" }, { 51, "Lease" }, { 66, "Boot Server Host Name" }, { 67, "Bootfile Name" } };

        static partial void RegWsDhcp()
        {
            PsFormat.Table("Microsoft.Management.Infrastructure.CimInstance#root/Microsoft/Windows/DHCP/DhcpServerv4Scope", PsFormat.P("ScopeId", 15), PsFormat.P("SubnetMask", 15), PsFormat.P("Name", 18), PsFormat.P("State", 8), PsFormat.P("StartRange", 15), PsFormat.P("EndRange", 15), PsFormat.P("LeaseDuration", 14));
            DhcpCmd("Add-DhcpServerInDC", "DnsName:string@0; IPAddress:string@1", (c, o) =>
            {
                var lab = Lab(c); var s = lab.Server(c.Str("DnsName") ?? c.Str("IPAddress") ?? c.M.Name);
                if (s == null) throw c.Fail("The DHCP server " + c.Str("DnsName") + " was not found.", "WIN32 20070", "ObjectNotFound", c.Str("DnsName"), "Microsoft.Management.Infrastructure.CimException");
                DhcpOk(c, lab.Authorize(s.Name, true), s.Name);
            });
            DhcpCmd("Remove-DhcpServerInDC", "DnsName:string@0; IPAddress:string@1", (c, o) => { var lab = Lab(c); var s = lab.Server(c.Str("DnsName") ?? c.M.Name); if (s != null) DhcpOk(c, lab.Authorize(s.Name, false), s.Name); });
            DhcpCmd("Get-DhcpServerInDC", "", (c, o) =>
            {
                var lab = Lab(c);
                foreach (var x in lab.Main.Live.Where(x => x.Is("dHCPClass"))) { var p = new PsObj("Microsoft.Management.Infrastructure.CimInstance#DhcpServerInDC", "Microsoft.Management.Infrastructure.CimInstance"); p.Val("IPAddress", "ipaddress", x.Get("dhcpIp")); p.Val("DnsName", "string", x.Name); c.Out(p); }
            });
            DhcpCmd("Get-DhcpServerv4Scope", "ScopeId:string[]@0", (c, o) =>
            {
                var s = DhcpSrv(c);
                if (c.Has("ScopeId")) foreach (var id in c.Strs("ScopeId")) { var sc = s.Dhcp.Scope(id); if (sc == null) DhcpOk(c, "The specified DHCP scope " + id + " doesn't exist.", id); c.Out(ScopeObj(sc)); }
                else foreach (var sc in s.Dhcp.Scopes.OrderBy(x => x.ScopeId)) c.Out(ScopeObj(sc));
            });
            DhcpCmd("Add-DhcpServerv4Scope", "Name:string !; StartRange:string !; EndRange:string !; SubnetMask:string !; LeaseDuration:timespan; State:string set=Active|InActive; Description:string; Type:string", (c, o) =>
            {
                var s = DhcpSrv(c);
                var sc = new DhcpScope { Name = c.Str("Name"), Start = c.Str("StartRange"), End = c.Str("EndRange"), Mask = c.Str("SubnetMask"), State = c.Str("State") ?? "Active", Description = c.Str("Description") ?? "" };
                if (c.Has("LeaseDuration")) sc.Lease = (TimeSpan)PsConv.Base(c.Get("LeaseDuration"));
                DhcpOk(c, Lab(c).AddScope(s.Name, sc), sc.Name);
                if (c.Sw("PassThru")) c.Out(ScopeObj(sc));
            });
            DhcpCmd("Set-DhcpServerv4Scope", "ScopeId:string@0 !; Name:string; State:string set=Active|InActive; LeaseDuration:timespan; Description:string; StartRange:string; EndRange:string", (c, o) =>
            {
                var s = DhcpSrv(c); var sc = DhcpScopeOf(c, s);
                if (c.Has("Name")) sc.Name = c.Str("Name"); if (c.Has("State")) sc.State = c.Str("State"); if (c.Has("Description")) sc.Description = c.Str("Description");
                if (c.Has("LeaseDuration")) sc.Lease = (TimeSpan)PsConv.Base(c.Get("LeaseDuration"));
                if (c.Has("StartRange")) sc.Start = c.Str("StartRange"); if (c.Has("EndRange")) sc.End = c.Str("EndRange");
                if (c.Sw("PassThru")) c.Out(ScopeObj(sc));
            });
            var rs = DhcpCmd("Remove-DhcpServerv4Scope", "ScopeId:string[]@0 !", (c, o) =>
            {
                var s = DhcpSrv(c);
                foreach (var id in c.Strs("ScopeId")) { var sc = s.Dhcp.Scope(id); if (sc == null) DhcpOk(c, "The specified DHCP scope " + id + " doesn't exist.", id); if (sc.Failover.Length > 0) DhcpOk(c, "The scope " + id + " is part of a failover relationship: remove it from the failover first.", id); if (!c.Sw("Force") && !c.ShouldProcess(id, "Remove-DhcpServerv4Scope")) continue; s.Dhcp.Scopes.Remove(sc); }
            });
            rs.ShouldProcess = true; rs.ConfirmImpact = "High";
            DhcpCmd("Add-DhcpServerv4ExclusionRange", "ScopeId:string@0 !; StartRange:string@1 !; EndRange:string@2 !", (c, o) =>
            {
                var s = DhcpSrv(c); var sc = DhcpScopeOf(c, s);
                if (!sc.InRange(c.Str("StartRange")) || !sc.InRange(c.Str("EndRange"))) DhcpOk(c, "The exclusion range must be within the scope's address range " + sc.Start + "-" + sc.End + ".", c.Str("StartRange"));
                sc.Exclusions.Add(new[] { c.Str("StartRange"), c.Str("EndRange") });
            });
            DhcpCmd("Get-DhcpServerv4ExclusionRange", "ScopeId:string@0", (c, o) =>
            {
                var s = DhcpSrv(c);
                foreach (var sc in s.Dhcp.Scopes.Where(x => !c.Has("ScopeId") || x.ScopeId == c.Str("ScopeId")))
                    foreach (var x in sc.Exclusions) { var p = new PsObj("Microsoft.Management.Infrastructure.CimInstance#DhcpServerv4ExclusionRange", "Microsoft.Management.Infrastructure.CimInstance"); p.Val("ScopeId", "ipaddress", sc.ScopeId); p.Val("StartRange", "ipaddress", x[0]); p.Val("EndRange", "ipaddress", x[1]); c.Out(p); }
            });
            DhcpCmd("Set-DhcpServerv4OptionValue", "ScopeId:string@0; ReservedIP:string; Router:string[]; DnsServer:string[]; DnsDomain:string; WinsServer:string[]; OptionId:int; Value:string[]", (c, o) =>
            {
                var s = DhcpSrv(c); var lab = Lab(c);
                var target = c.Has("ScopeId") ? DhcpScopeOf(c, s).Options : s.Dhcp.ServerOptions;
                if (c.Has("DnsServer") && !c.Sw("Force"))
                    foreach (var ip in c.Strs("DnsServer")) { var ds = lab.Server(ip); if (ds == null || ds.Dns == null || !ds.Dns.Up) DhcpOk(c, "The DNS server " + ip + " is not a valid DNS server (it didn't answer). Use -Force to set it anyway.", ip); }
                if (c.Has("Router")) target[3] = string.Join(",", c.Strs("Router"));
                if (c.Has("DnsServer")) target[6] = string.Join(",", c.Strs("DnsServer"));
                if (c.Has("DnsDomain")) target[15] = c.Str("DnsDomain");
                if (c.Has("WinsServer")) target[44] = string.Join(",", c.Strs("WinsServer"));
                if (c.Has("OptionId")) target[c.Int("OptionId", 0)] = string.Join(",", c.Strs("Value") ?? new string[0]);
            });
            DhcpCmd("Get-DhcpServerv4OptionValue", "ScopeId:string@0; OptionId:int[]; All:switch", (c, o) =>
            {
                var s = DhcpSrv(c);
                var opts = c.Has("ScopeId") ? DhcpScopeOf(c, s).Options : s.Dhcp.ServerOptions;
                foreach (var kv in opts.OrderBy(k => k.Key))
                {
                    if (c.Has("OptionId") && !c.Objs("OptionId").Select(PsConv.ToInt).Contains(kv.Key)) continue;
                    var p = new PsObj("Microsoft.Management.Infrastructure.CimInstance#root/Microsoft/Windows/DHCP/DhcpServerv4OptionValue", "Microsoft.Management.Infrastructure.CimInstance");
                    string nm; OptNames.TryGetValue(kv.Key, out nm);
                    p.Val("Name", "string", nm ?? ""); p.Val("OptionId", "int", kv.Key); p.Val("Type", "string", kv.Key == 15 ? "String" : "IPv4Address"); p.Val("Value", "string[]", kv.Value.Split(',').Cast<object>().ToArray()); p.Val("VendorClass", "string", ""); p.Val("UserClass", "string", ""); p.Val("PolicyName", "string", null);
                    p.DefaultDisplay = new[] { "Name", "OptionId", "Type", "Value", "VendorClass", "UserClass", "PolicyName" };
                    c.Out(p);
                }
            });
            RegWsDhcp2();
        }
    }
}
