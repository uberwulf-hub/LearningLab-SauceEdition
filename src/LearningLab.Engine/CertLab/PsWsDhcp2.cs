// Cert Lab - DhcpServer module: reservations, leases, scope statistics and failover relationships.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public partial class PsCmdlets
    {
        static PsObj FailoverObj(string me, DhcpFailover f)
        {
            var p = new PsObj("Microsoft.Management.Infrastructure.CimInstance#root/Microsoft/Windows/DHCP/DhcpServerv4Failover", "Microsoft.Management.Infrastructure.CimInstance");
            p.Val("Name", "string", f.Name); p.Val("PartnerServer", "string", f.Partner(me)); p.Val("Mode", "string", f.Mode); p.Val("ScopeId", "ipaddress[]", f.ScopeIds.Cast<object>().ToArray());
            p.Val("State", "string", f.State); p.Val("ServerRole", "string", f.Mode == "HotStandby" ? (WsText.Eq(me, f.PartnerA) ? "Active" : "Standby") : "Active");
            p.Val("LoadBalancePercent", "uint32", (uint)(f.Mode == "LoadBalance" ? f.LoadBalancePercent : 0)); p.Val("ReservePercent", "uint32", (uint)(f.Mode == "HotStandby" ? f.ReservePercent : 0));
            p.Val("MaxClientLeadTime", "timespan", f.Mclt); p.Val("StateSwitchInterval", "timespan", null); p.Val("EnableAuth", "bool", f.SharedSecret.Length > 0); p.Val("AutoStateTransition", "bool", false);
            return p;
        }

        static void RegWsDhcp2()
        {
            DhcpCmd("Add-DhcpServerv4Reservation", "ScopeId:string@0 !; IPAddress:string@1 !; ClientId:string@2 !; Name:string; Description:string; Type:string set=Dhcp|Bootp|Both", (c, o) =>
            {
                var s = DhcpSrv(c); var sc = DhcpScopeOf(c, s);
                var r = WsOps.Run(Lab(c), "dhcp scope reserve server=" + s.Name + " id=" + sc.ScopeId + " ip=" + c.Str("IPAddress") + " mac=" + c.Str("ClientId") + " name=\"" + (c.Str("Name") ?? "") + "\"");
                DhcpOk(c, r.Ok ? null : r.Text, c.Str("IPAddress"));
                if (c.Has("Description")) sc.Reservations.Last().Description = c.Str("Description");
            });
            DhcpCmd("Get-DhcpServerv4Reservation", "ScopeId:string@0; IPAddress:string[]; ClientId:string", (c, o) =>
            {
                var s = DhcpSrv(c);
                foreach (var sc in s.Dhcp.Scopes.Where(x => !c.Has("ScopeId") || x.ScopeId == c.Str("ScopeId")))
                    foreach (var r in sc.Reservations.Where(x => (!c.Has("IPAddress") || c.Strs("IPAddress").Contains(x.Ip)) && (!c.Has("ClientId") || WsText.Eq(x.Mac, c.Str("ClientId").Replace(":", "-")))))
                    {
                        var p = new PsObj("Microsoft.Management.Infrastructure.CimInstance#root/Microsoft/Windows/DHCP/DhcpServerv4Reservation", "Microsoft.Management.Infrastructure.CimInstance");
                        p.Val("IPAddress", "ipaddress", r.Ip); p.Val("ScopeId", "ipaddress", sc.ScopeId); p.Val("ClientId", "string", r.Mac.ToLowerInvariant()); p.Val("Name", "string", r.Name); p.Val("Type", "string", "Both"); p.Val("Description", "string", r.Description);
                        p.DefaultDisplay = new[] { "IPAddress", "ScopeId", "ClientId", "Name", "Type", "Description" };
                        c.Out(p);
                    }
            });
            DhcpCmd("Remove-DhcpServerv4Reservation", "ScopeId:string@0; IPAddress:string[]; ClientId:string", (c, o) =>
            {
                var s = DhcpSrv(c);
                foreach (var sc in s.Dhcp.Scopes) sc.Reservations.RemoveAll(x => c.Has("IPAddress") && c.Strs("IPAddress").Contains(x.Ip) || c.Has("ClientId") && WsText.Eq(x.Mac, c.Str("ClientId").Replace(":", "-")));
            });
            DhcpCmd("Get-DhcpServerv4Lease", "ScopeId:string[]@0; IPAddress:string[]; ClientId:string[]; AllLeases:switch", (c, o) =>
            {
                var s = DhcpSrv(c); var lab = Lab(c);
                foreach (var sc in s.Dhcp.Scopes.Where(x => !c.Has("ScopeId") || c.Strs("ScopeId").Contains(x.ScopeId)))
                    foreach (var l in sc.Leases.Where(x => !c.Has("IPAddress") || c.Strs("IPAddress").Contains(x.Ip)).OrderBy(x => x.Ip, StringComparer.Ordinal))
                    {
                        var p = new PsObj("Microsoft.Management.Infrastructure.CimInstance#root/Microsoft/Windows/DHCP/DhcpServerv4Lease", "Microsoft.Management.Infrastructure.CimInstance");
                        p.Val("IPAddress", "ipaddress", l.Ip); p.Val("ScopeId", "ipaddress", sc.ScopeId); p.Val("ClientId", "string", l.Mac.ToLowerInvariant()); p.Val("HostName", "string", l.Host);
                        p.Val("AddressState", "string", l.Kind == "Reservation" ? "ActiveReservation" : l.Expires > lab.Now ? "Active" : "Expired"); p.Val("LeaseExpiryTime", "System.Nullable[datetime]", l.Kind == "Reservation" ? null : (object)l.Expires); p.Val("ServerIP", "ipaddress", lab.Server(l.Server) == null ? "" : lab.Server(l.Server).M.Ip);
                        p.DefaultDisplay = new[] { "IPAddress", "ScopeId", "ClientId", "HostName", "AddressState", "LeaseExpiryTime" };
                        c.Out(p);
                    }
            });
            DhcpCmd("Get-DhcpServerv4ScopeStatistics", "ScopeId:string[]@0", (c, o) =>
            {
                var s = DhcpSrv(c); var lab = Lab(c);
                foreach (var sc in s.Dhcp.Scopes.Where(x => !c.Has("ScopeId") || c.Strs("ScopeId").Contains(x.ScopeId)))
                {
                    uint a, b; Ip4.TryParse(sc.Start, out a); Ip4.TryParse(sc.End, out b);
                    int total = (int)(b - a + 1), excl = 0; for (uint x = a; x <= b; x++) if (sc.Excluded(Ip4.Str(x))) excl++;
                    int used = sc.Leases.Count(l => l.Expires > lab.Now || l.Kind == "Reservation"), res = sc.Reservations.Count, free = Math.Max(0, total - excl - used - sc.Reservations.Count(r => !sc.Leases.Any(l => l.Ip == r.Ip)));
                    var p = new PsObj("Microsoft.Management.Infrastructure.CimInstance#root/Microsoft/Windows/DHCP/DhcpServerv4ScopeStatistics", "Microsoft.Management.Infrastructure.CimInstance");
                    p.Val("ScopeId", "ipaddress", sc.ScopeId); p.Val("Free", "uint32", (uint)free); p.Val("InUse", "uint32", (uint)used); p.Val("PercentageInUse", "single", (float)Math.Round(used * 100.0 / Math.Max(1, total - excl), 2)); p.Val("Reserved", "uint32", (uint)res); p.Val("Pending", "uint32", 0u);
                    c.Out(p);
                }
            });
            DhcpCmd("Add-DhcpServerv4Failover", "Name:string@0 !; ScopeId:string[]@1 !; PartnerServer:string@2 !; LoadBalancePercent:uint32; ServerRole:string set=Active|Standby; ReservePercent:uint32; MaxClientLeadTime:timespan; SharedSecret:string; AutoStateTransition:bool; StateSwitchInterval:timespan", (c, o) =>
            {
                var s = DhcpSrv(c);
                bool hot = c.Has("ServerRole") || c.Has("ReservePercent");
                var mclt = c.Has("MaxClientLeadTime") ? (TimeSpan)PsConv.Base(c.Get("MaxClientLeadTime")) : TimeSpan.FromHours(1);
                DhcpOk(c, Lab(c).AddFailover(s.Name, c.Str("PartnerServer"), c.Str("Name"), c.Strs("ScopeId").ToList(), hot ? "HotStandby" : "LoadBalance", c.Int("LoadBalancePercent", 50), c.Int("ReservePercent", 5), mclt, c.Str("SharedSecret")), c.Str("Name"));
                if (hot && WsText.Eq(c.Str("ServerRole"), "Standby")) { var f = s.Dhcp.Failovers.Last(); string x = f.PartnerA; f.PartnerA = f.PartnerB; f.PartnerB = x; }
                if (c.Sw("PassThru")) c.Out(FailoverObj(s.Name, s.Dhcp.Failovers.Last()));
            });
            DhcpCmd("Get-DhcpServerv4Failover", "Name:string[]@0; ScopeId:string[]", (c, o) =>
            {
                var s = DhcpSrv(c);
                foreach (var f in s.Dhcp.Failovers.Where(x => (!c.Has("Name") || c.Strs("Name").Contains(x.Name, StringComparer.OrdinalIgnoreCase)) && (!c.Has("ScopeId") || x.ScopeIds.Intersect(c.Strs("ScopeId")).Any()))) c.Out(FailoverObj(s.Name, f));
            });
            DhcpCmd("Remove-DhcpServerv4Failover", "Name:string[]@0 !", (c, o) => { var s = DhcpSrv(c); foreach (var n in c.Strs("Name")) DhcpOk(c, Lab(c).RemoveFailover(s.Name, n), n); });
            DhcpCmd("Invoke-DhcpServerv4FailoverReplication", "Name:string[]@0; ScopeId:string[]", (c, o) =>
            {
                var s = DhcpSrv(c);
                if (!c.Sw("Force") && !c.ShouldProcess(s.Name, "Replicate the failover scopes' settings to the partner server")) return;
                DhcpOk(c, Lab(c).ReplicateFailover(s.Name, c.Has("Name") ? c.Strs("Name")[0] : null, c.Has("ScopeId") ? c.Strs("ScopeId")[0] : null), s.Name);
            }).ShouldProcess = true;
        }
    }
}
