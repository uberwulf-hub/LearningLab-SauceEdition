// Cert Lab - the DnsServer module on the AZ-800 machines (DNS servers and machines with the DNS tools): zones of every
// kind, records, forwarders, recursion, DNSSEC signing, aging, and DNS policies (client subnets, zone scopes, query
// resolution policies). -ComputerName picks the DNS server (default: this machine). Everything goes through the lab.
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
        const string DnsMod = "DnsServer";
        static Func<PsMachine, bool> HasDnsTools = m => m.Ws != null && (m.FeatureInstalled("DNS") || m.FeatureInstalled("RSAT-DNS-Server"));
        static CmdletDef DnsCmd(string name, string spec, Action<CmdCtx, object> run) { var d = Cdxml(Add(name, DnsMod, spec + "; ComputerName:string alias=Cn; PassThru:switch; CimSession:object; Force:switch", run)); d.Available = HasDnsTools; return d; }
        static WsServer DnsSrv(CmdCtx c)
        {
            var lab = Lab(c); var s = lab.Server(c.Str("ComputerName") ?? c.M.Name);
            if (s == null || s.Dns == null) throw c.Fail("Failed to get the zone information for the DNS server " + (c.Str("ComputerName") ?? c.M.Name) + ": the DNS Server role isn't installed there.", "WIN32 1722", "ConnectionError", c.Str("ComputerName"), "Microsoft.Management.Infrastructure.CimException");
            if (!s.Dns.Up) throw c.Fail("The RPC server is unavailable: the DNS Server service on " + s.Name + " isn't running.", "WIN32 1722", "ConnectionError", s.Name, "Microsoft.Management.Infrastructure.CimException");
            return s;
        }
        static void DnsOk(CmdCtx c, string err, string target) { if (err != null) throw c.Fail(err, "WIN32 9611", "InvalidOperation", target, "Microsoft.Management.Infrastructure.CimException"); }
        static PsObj ZoneObj(DnsZone z)
        {
            var p = new PsObj("Microsoft.Management.Infrastructure.CimInstance#root/Microsoft/Windows/DNS/DnsServer" + (z.Type == "Forwarder" ? "ConditionalForwarder" : z.Type) + "Zone", "Microsoft.Management.Infrastructure.CimInstance#DnsServerZone", "Microsoft.Management.Infrastructure.CimInstance");
            p.Val("ZoneName", "string", z.Name); p.Val("ZoneType", "string", z.Type); p.Val("IsAutoCreated", "bool", false); p.Val("IsDsIntegrated", "bool", z.AdIntegrated);
            p.Val("IsReverseLookupZone", "bool", z.Reverse); p.Val("IsSigned", "bool", z.Signed); p.Val("DynamicUpdate", "string", z.DynamicUpdate); p.Val("ReplicationScope", "string", z.AdIntegrated ? z.ReplicationScope : "None");
            p.Val("SecureSecondaries", "string", z.Transfers); p.Val("SecondaryServers", "string[]", z.SecondaryServers.Cast<object>().ToArray()); p.Val("MasterServers", "string[]", z.Masters.Cast<object>().ToArray()); p.Val("Aging", "bool", z.Aging);
            p.DefaultDisplay = new[] { "ZoneName", "ZoneType", "IsAutoCreated", "IsDsIntegrated", "IsReverseLookupZone", "IsSigned" };
            return p;
        }
        static PsObj RecObj(DnsRec r)
        {
            var p = new PsObj("Microsoft.Management.Infrastructure.CimInstance#root/Microsoft/Windows/DNS/DnsServerResourceRecord", "Microsoft.Management.Infrastructure.CimInstance");
            string data = r.Type == "MX" ? "[" + r.Data.Split('|')[0] + "][" + r.Data.Split('|').Last() + ".]" : r.Type == "SRV" ? "[" + r.Data.Replace("|", "][") + ".]" : r.Type == "A" || r.Type == "TXT" || r.Type == "DNSKEY" ? r.Data : r.Data.TrimEnd('.') + ".";
            p.Val("HostName", "string", r.Name); p.Val("RecordType", "string", r.Type); p.Val("Type", "uint16", r.Type == "A" ? 1 : r.Type == "NS" ? 2 : r.Type == "CNAME" ? 5 : r.Type == "SOA" ? 6 : r.Type == "PTR" ? 12 : r.Type == "MX" ? 15 : r.Type == "TXT" ? 16 : r.Type == "SRV" ? 33 : 48);
            p.Val("Timestamp", "System.Nullable[datetime]", null); p.Val("TimeToLive", "timespan", TimeSpan.FromSeconds(r.Ttl)); p.Val("RecordData", "string", data);
            p.DefaultDisplay = new[] { "HostName", "RecordType", "Type", "Timestamp", "TimeToLive", "RecordData" };
            return p;
        }
        static string Scope(CmdCtx c) { var s = c.Str("ReplicationScope"); return s == null ? "" : new[] { "Forest", "Domain", "Legacy", "Custom" }.FirstOrDefault(x => WsText.Eq(x, s)) ?? ""; }
        static string NetIdZone(string netId)
        {
            var p = netId.Split('/'); var o = p[0].Split('.'); int len = p.Length > 1 ? int.Parse(p[1], CultureInfo.InvariantCulture) : 24;
            int keep = len / 8;
            return string.Join(".", o.Take(keep).Reverse().ToArray()) + ".in-addr.arpa";
        }

        static partial void RegWsDns()
        {
            PsFormat.Table("Microsoft.Management.Infrastructure.CimInstance#DnsServerZone", PsFormat.P("ZoneName", 32), PsFormat.P("ZoneType", 16), PsFormat.P("IsAutoCreated", 13), PsFormat.P("IsDsIntegrated", 14), PsFormat.P("IsReverseLookupZone", 19), PsFormat.P("IsSigned", 8));
            PsFormat.Table("Microsoft.Management.Infrastructure.CimInstance#root/Microsoft/Windows/DNS/DnsServerResourceRecord", PsFormat.P("HostName", 24), PsFormat.P("RecordType", 10), PsFormat.PR("Type", 6), PsFormat.P("Timestamp", 22), PsFormat.P("TimeToLive", 15), PsFormat.P("RecordData", 30));
            DnsCmd("Get-DnsServerZone", "Name:string[]@0 alias=ZoneName", (c, o) =>
            {
                var s = DnsSrv(c);
                if (c.Has("Name")) foreach (var n in c.Strs("Name")) { var z = s.Dns.Zone(n); if (z == null) throw c.Fail("The zone " + n + " was not found on server " + s.Name + ".", "WIN32 9601", "ObjectNotFound", n, "Microsoft.Management.Infrastructure.CimException"); c.Out(ZoneObj(z)); }
                else foreach (var z in s.Dns.Zones.OrderBy(x => x.Name)) c.Out(ZoneObj(z));
            });
            DnsCmd("Add-DnsServerPrimaryZone", "Name:string@0 alias=ZoneName; NetworkId:string; ReplicationScope:string set=Forest|Domain|Legacy|Custom; ZoneFile:string; DynamicUpdate:string set=None|Secure|NonsecureAndSecure; DirectoryPartitionName:string", (c, o) =>
            {
                var s = DnsSrv(c); string name = c.Has("NetworkId") ? NetIdZone(c.Str("NetworkId")) : c.Str("Name");
                if (string.IsNullOrEmpty(name)) throw c.Fail("Give the zone a -Name (or -NetworkId for a reverse lookup zone).", "Missing", "InvalidArgument", null, "System.ArgumentException");
                string scope = Scope(c);
                if (scope.Length == 0 && !c.Has("ZoneFile")) throw c.Fail("A primary zone needs -ReplicationScope (stored in Active Directory) or -ZoneFile (a file on this server).", "WIN32 87", "InvalidArgument", name, "Microsoft.Management.Infrastructure.CimException");
                string dyn = c.Str("DynamicUpdate") ?? (scope.Length > 0 ? "Secure" : "None");
                if (dyn == "Secure" && scope.Length == 0) throw c.Fail("Secure dynamic updates are possible only for zones stored in Active Directory.", "WIN32 9611", "InvalidArgument", name, "Microsoft.Management.Infrastructure.CimException");
                var z = new DnsZone { Name = name.ToLowerInvariant(), ReplicationScope = scope, DynamicUpdate = dyn, Transfers = scope.Length > 0 ? "NoTransfer" : "TransferToZoneNameServer" };
                z.Records.Add(new DnsRec { Name = "@", Type = "NS", Data = s.M.Fqdn });
                DnsOk(c, Lab(c).AddZone(s.Name, z), name);
                if (c.Sw("PassThru")) c.Out(ZoneObj(z));
            });
            DnsCmd("Add-DnsServerSecondaryZone", "Name:string@0 ! alias=ZoneName; NetworkId:string; ZoneFile:string !; MasterServers:string[] !", (c, o) =>
            {
                var s = DnsSrv(c); string name = c.Has("NetworkId") ? NetIdZone(c.Str("NetworkId")) : c.Str("Name");
                var z = new DnsZone { Name = name.ToLowerInvariant(), Type = "Secondary" }; z.Masters.AddRange(c.Strs("MasterServers"));
                DnsOk(c, Lab(c).AddZone(s.Name, z), name);
                if (c.Sw("PassThru")) c.Out(ZoneObj(z));
            });
            foreach (var kind in new[] { "Stub", "ConditionalForwarder" })
            {
                string k = kind;
                DnsCmd("Add-DnsServer" + k + "Zone", "Name:string@0 ! alias=ZoneName; MasterServers:string[]@1 !; ReplicationScope:string set=Forest|Domain|Legacy|Custom; ZoneFile:string; ForwarderTimeout:int", (c, o) =>
                {
                    var s = DnsSrv(c);
                    var z = new DnsZone { Name = c.Str("Name").ToLowerInvariant(), Type = k == "Stub" ? "Stub" : "Forwarder", ReplicationScope = Scope(c) }; z.Masters.AddRange(c.Strs("MasterServers"));
                    DnsOk(c, Lab(c).AddZone(s.Name, z), z.Name);
                    if (c.Sw("PassThru")) c.Out(ZoneObj(z));
                });
            }
            var rz = DnsCmd("Remove-DnsServerZone", "Name:string@0 ! alias=ZoneName", (c, o) =>
            {
                var s = DnsSrv(c);
                if (!c.Sw("Force") && !c.ShouldProcess(c.Str("Name"), "Remove-DnsServerZone")) return;
                DnsOk(c, Lab(c).RemoveZone(s.Name, c.Str("Name")), c.Str("Name"));
            });
            rz.ShouldProcess = true; rz.ConfirmImpact = "High";
            DnsCmd("Set-DnsServerPrimaryZone", "Name:string@0 ! alias=ZoneName; DynamicUpdate:string set=None|Secure|NonsecureAndSecure; SecureSecondaries:string set=NoTransfer|TransferAnyServer|TransferToZoneNameServer|TransferToSecureServers; SecondaryServers:string[]; ReplicationScope:string set=Forest|Domain|Legacy|Custom; Notify:string", (c, o) =>
            {
                var s = DnsSrv(c);
                DnsOk(c, Lab(c).EditZone(s.Name, c.Str("Name"), z =>
                {
                    if (z.Type != "Primary") return "The zone " + z.Name + " isn't a primary zone.";
                    if (c.Has("DynamicUpdate")) { if (c.Str("DynamicUpdate") == "Secure" && !z.AdIntegrated) return "Secure dynamic updates are possible only for zones stored in Active Directory."; z.DynamicUpdate = c.Str("DynamicUpdate"); }
                    if (c.Has("SecureSecondaries")) z.Transfers = c.Str("SecureSecondaries");
                    if (c.Has("SecondaryServers")) { z.SecondaryServers.Clear(); z.SecondaryServers.AddRange(c.Strs("SecondaryServers")); }
                    if (c.Has("ReplicationScope") && z.AdIntegrated) z.ReplicationScope = Scope(c);
                    return null;
                }), c.Str("Name"));
            });
            DnsCmd("ConvertTo-DnsServerPrimaryZone", "Name:string@0 ! alias=ZoneName; ReplicationScope:string ! set=Forest|Domain|Legacy|Custom; ZoneFile:string", (c, o) =>
            {
                var s = DnsSrv(c); var lab = Lab(c);
                var fz = s.Dns.FileZones.FirstOrDefault(x => WsText.Eq(x.Name, c.Str("Name")));
                if (fz == null) DnsOk(c, "The zone " + c.Str("Name") + " isn't a file-backed zone on " + s.Name + ".", c.Str("Name"));
                if (!lab.Dirs.ContainsKey(s.Name)) DnsOk(c, "Only a DNS server that is also a domain controller can store a zone in Active Directory.", s.Name);
                if (fz.Type == "Secondary") { fz.Type = "Primary"; }
                s.Dns.FileZones.Remove(fz);
                fz.ReplicationScope = Scope(c); if (fz.DynamicUpdate == "None") fz.DynamicUpdate = "Secure";
                DnsOk(c, lab.AddZone(s.Name, fz), fz.Name);
            });
            DnsCmd("Start-DnsServerZoneTransfer", "Name:string@0 ! alias=ZoneName; FullTransfer:switch", (c, o) =>
            {
                var s = DnsSrv(c); var z = s.Dns.FileZones.FirstOrDefault(x => WsText.Eq(x.Name, c.Str("Name")) && x.Type == "Secondary");
                if (z == null) DnsOk(c, "The zone " + c.Str("Name") + " isn't a secondary zone on " + s.Name + ".", c.Str("Name"));
                Lab(c).Transfer(s.Name, z);
                if (z.Expired) DnsOk(c, "The zone transfer of " + z.Name + " was refused by its master server.", z.Name);
            });
            RegWsDns2();
        }
    }
}
