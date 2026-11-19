// Cert Lab - DnsServer module: resource records, forwarders, recursion, the cache, DNSSEC signing, aging, and DNS
// policies (client subnets, zone scopes, query resolution policies).
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
        static void AddRec(CmdCtx c, WsServer s, DnsRec r)
        {
            r.Scope = c.Str("ZoneScope") ?? "";
            if (c.Has("TimeToLive")) { var v = PsConv.Base(c.Get("TimeToLive")); r.Ttl = v is TimeSpan ? (int)((TimeSpan)v).TotalSeconds : (int)TimeSpan.Parse(PsConv.ToStr(v), CultureInfo.InvariantCulture).TotalSeconds; }
            DnsOk(c, Lab(c).AddRecord(s.Name, c.Str("ZoneName"), r), c.Str("ZoneName"));
            if (c.Sw("CreatePtr") && r.Type == "A")
            {
                var p = r.Data.Split('.'); string rz = p[2] + "." + p[1] + "." + p[0] + ".in-addr.arpa";
                if (s.Dns.Zone(rz) == null) c.Warn("The reverse lookup zone " + rz + " doesn't exist, so no PTR record was created.");
                else DnsOk(c, Lab(c).AddRecord(s.Name, rz, new DnsRec { Name = p[3], Type = "PTR", Data = (r.Name == "@" ? c.Str("ZoneName") : r.Name + "." + c.Str("ZoneName")).ToLowerInvariant() }), rz);
            }
            if (c.Sw("PassThru")) c.Out(RecObj(r));
        }
        const string RecBase = "ZoneName:string ! ; Name:string ! ; TimeToLive:object; ZoneScope:string; AllowUpdateAny:switch; AgeRecord:switch";

        static void RegWsDns2()
        {
            DnsCmd("Get-DnsServerResourceRecord", "ZoneName:string@0 !; Name:string@1; RRType:string set=A|AAAA|CName|MX|NS|Ptr|Soa|Srv|Txt|DNSKEY; ZoneScope:string", (c, o) =>
            {
                var s = DnsSrv(c); var z = s.Dns.Zone(c.Str("ZoneName"));
                if (z == null) throw c.Fail("Failed to get the zone information for " + c.Str("ZoneName") + " on server " + s.Name + ".", "WIN32 9601", "ObjectNotFound", c.Str("ZoneName"), "Microsoft.Management.Infrastructure.CimException");
                var list = z.Records.Where(r => WsText.Eq(r.Scope, c.Str("ZoneScope") ?? "") && (!c.Has("Name") || WsText.Eq(r.Name, c.Str("Name"))) && (!c.Has("RRType") || WsText.Eq(r.Type, c.Str("RRType")))).ToList();
                if (c.Has("Name") && list.Count == 0) throw c.Fail("Failed to get " + c.Str("Name") + " record in " + z.Name + " zone on " + s.Name + " server.", "WIN32 9714", "ObjectNotFound", c.Str("Name"), "Microsoft.Management.Infrastructure.CimException");
                foreach (var r in list.OrderBy(r => r.Name == "@" ? "" : r.Name)) c.Out(RecObj(r));
            });
            DnsCmd("Add-DnsServerResourceRecordA", RecBase.Replace("Name:string ! ", "Name:string@0 !") + "; IPv4Address:string[]@1 !; CreatePtr:switch", (c, o) =>
            {
                var s = DnsSrv(c);
                foreach (var ip in c.Strs("IPv4Address")) { if (!Ip4.IsV4(ip)) throw c.Fail("\"" + ip + "\" isn't an IPv4 address.", "InvalidArgument", "InvalidArgument", ip, "System.ArgumentException"); AddRec(c, s, new DnsRec { Name = c.Str("Name").ToLowerInvariant(), Type = "A", Data = ip, Ttl = 3600 }); }
            });
            DnsCmd("Add-DnsServerResourceRecordCName", RecBase + "; HostNameAlias:string !", (c, o) => { var s = DnsSrv(c); AddRec(c, s, new DnsRec { Name = c.Str("Name").ToLowerInvariant(), Type = "CNAME", Data = c.Str("HostNameAlias").TrimEnd('.') }); });
            DnsCmd("Add-DnsServerResourceRecordMX", RecBase + "; MailExchange:string !; Preference:int !", (c, o) => { var s = DnsSrv(c); AddRec(c, s, new DnsRec { Name = c.Str("Name"), Type = "MX", Data = c.Int("Preference", 10) + "|" + c.Str("MailExchange").TrimEnd('.') }); });
            DnsCmd("Add-DnsServerResourceRecordPtr", RecBase + "; PtrDomainName:string !", (c, o) => { var s = DnsSrv(c); AddRec(c, s, new DnsRec { Name = c.Str("Name"), Type = "PTR", Data = c.Str("PtrDomainName").TrimEnd('.') }); });
            DnsCmd("Add-DnsServerResourceRecord", RecBase + "; A:switch; CName:switch; Ptr:switch; Srv:switch; Txt:switch; MX:switch; IPv4Address:string; HostNameAlias:string; PtrDomainName:string; DomainName:string; Priority:int; Weight:int; Port:int; DescriptiveText:string; MailExchange:string; Preference:int", (c, o) =>
            {
                var s = DnsSrv(c); string n = c.Str("Name").ToLowerInvariant();
                if (c.Sw("A")) AddRec(c, s, new DnsRec { Name = n, Type = "A", Data = c.Str("IPv4Address") });
                else if (c.Sw("CName")) AddRec(c, s, new DnsRec { Name = n, Type = "CNAME", Data = c.Str("HostNameAlias").TrimEnd('.') });
                else if (c.Sw("Ptr")) AddRec(c, s, new DnsRec { Name = n, Type = "PTR", Data = c.Str("PtrDomainName").TrimEnd('.') });
                else if (c.Sw("Srv")) AddRec(c, s, new DnsRec { Name = n, Type = "SRV", Data = c.Int("Priority", 0) + "|" + c.Int("Weight", 0) + "|" + c.Int("Port", 0) + "|" + c.Str("DomainName").TrimEnd('.') });
                else if (c.Sw("Txt")) AddRec(c, s, new DnsRec { Name = n, Type = "TXT", Data = c.Str("DescriptiveText") });
                else if (c.Sw("MX")) AddRec(c, s, new DnsRec { Name = n, Type = "MX", Data = c.Int("Preference", 10) + "|" + c.Str("MailExchange").TrimEnd('.') });
                else throw c.Fail("Say which kind of record: -A, -CName, -Ptr, -Srv, -Txt or -MX.", "InvalidArgument", "InvalidArgument", n, "System.ArgumentException");
            });
            var rr = DnsCmd("Remove-DnsServerResourceRecord", "ZoneName:string@0 !; Name:string@1; RRType:string@2 set=A|AAAA|CName|MX|NS|Ptr|Srv|Txt; RecordData:string[]; InputObject:object pipe; ZoneScope:string", (c, o) =>
            {
                var s = DnsSrv(c); string name = c.Str("Name"), type = c.Str("RRType");
                if (c.HasInput && PsConv.Base(o) is PsObj) { var po = (PsObj)PsConv.Base(o); name = PsConv.ToStr(po.Get("HostName")); type = PsConv.ToStr(po.Get("RecordType")); }
                if (!c.Sw("Force") && !c.ShouldProcess(name + " " + type, "Remove-DnsServerResourceRecord")) return;
                DnsOk(c, Lab(c).RemoveRecord(s.Name, c.Str("ZoneName"), name, type, c.Has("RecordData") ? c.Strs("RecordData")[0] : null), name);
            });
            rr.ShouldProcess = true; rr.ConfirmImpact = "High";
            DnsCmd("Get-DnsServerForwarder", "", (c, o) =>
            {
                var s = DnsSrv(c); var p = new PsObj("Microsoft.Management.Infrastructure.CimInstance#root/Microsoft/Windows/DNS/DnsServerForwarder", "Microsoft.Management.Infrastructure.CimInstance");
                p.Val("UseRootHint", "bool", s.Dns.UseRootHints); p.Val("Timeout(s)", "uint32", 3u); p.Val("EnableReordering", "bool", true); p.Val("IPAddress", "string[]", s.Dns.Forwarders.Cast<object>().ToArray()); p.Val("ReorderedIPAddress", "string[]", s.Dns.Forwarders.Cast<object>().ToArray());
                c.Out(p);
            });
            foreach (var verb in new[] { "Set", "Add", "Remove" })
            {
                string v = verb;
                DnsCmd(verb + "-DnsServerForwarder", "IPAddress:string[]@0" + (v == "Remove" ? " !" : "") + "; UseRootHint:bool; Timeout:int", (c, o) =>
                {
                    var s = DnsSrv(c); var l = (c.Strs("IPAddress") ?? new string[0]).ToList();
                    if (l.Any(x => !Ip4.IsV4(x))) throw c.Fail("Forwarders are IP addresses.", "InvalidArgument", "InvalidArgument", null, "System.ArgumentException");
                    if (v == "Set" && c.Has("IPAddress")) { s.Dns.Forwarders.Clear(); s.Dns.Forwarders.AddRange(l); }
                    if (v == "Add") foreach (var x in l) if (!s.Dns.Forwarders.Contains(x)) s.Dns.Forwarders.Add(x);
                    if (v == "Remove") s.Dns.Forwarders.RemoveAll(l.Contains);
                    if (c.Has("UseRootHint")) s.Dns.UseRootHints = PsConv.ToBool(c.Get("UseRootHint"));
                });
            }
            DnsCmd("Get-DnsServerRecursion", "", (c, o) => { var s = DnsSrv(c); var p = new PsObj("Microsoft.Management.Infrastructure.CimInstance#root/Microsoft/Windows/DNS/DnsServerRecursion", "Microsoft.Management.Infrastructure.CimInstance"); p.Val("Enable", "bool", s.Dns.Recursion); p.Val("AdditionalTimeout(s)", "uint32", 4u); p.Val("RetryInterval(s)", "uint32", 3u); p.Val("Timeout(s)", "uint32", 8u); p.Val("SecureResponse", "bool", true); c.Out(p); });
            DnsCmd("Set-DnsServerRecursion", "Enable:bool", (c, o) => { var s = DnsSrv(c); if (c.Has("Enable")) s.Dns.Recursion = PsConv.ToBool(c.Get("Enable")); });
            DnsCmd("Clear-DnsServerCache", "", (c, o) => { DnsSrv(c); });
            DnsCmd("Invoke-DnsServerZoneSign", "ZoneName:string@0 !; SignWithDefault:switch; DoResign:switch", (c, o) =>
            {
                var s = DnsSrv(c);
                if (!c.Sw("SignWithDefault") && !c.Sw("DoResign")) throw c.Fail("Cert Lab signs with the default settings: add -SignWithDefault.", "NotSimulated", "NotImplemented", null, "System.NotSupportedException");
                if (!c.Sw("Force") && !c.ShouldProcess(c.Str("ZoneName"), "Sign the zone with DNSSEC")) return;
                var r = WsOps.Run(Lab(c), "dns zone sign server=" + s.Name + " name=" + c.Str("ZoneName"));
                DnsOk(c, r.Ok ? null : r.Text, c.Str("ZoneName"));
            }).ShouldProcess = true;
            DnsCmd("Set-DnsServerZoneAging", "Name:string@0 ! alias=ZoneName; Aging:bool; NoRefreshInterval:object; RefreshInterval:object", (c, o) => { var s = DnsSrv(c); DnsOk(c, Lab(c).EditZone(s.Name, c.Str("Name"), z => { if (c.Has("Aging")) z.Aging = PsConv.ToBool(c.Get("Aging")); return null; }), c.Str("Name")); });
            DnsCmd("Set-DnsServerScavenging", "ScavengingState:bool; ScavengingInterval:object; ApplyOnAllZones:switch", (c, o) => { var s = DnsSrv(c); if (c.Has("ScavengingState")) s.Dns.Scavenging = PsConv.ToBool(c.Get("ScavengingState")); });
            DnsCmd("Add-DnsServerClientSubnet", "Name:string@0 !; IPv4Subnet:string[]@1 !", (c, o) =>
            {
                var s = DnsSrv(c);
                if (s.Dns.ClientSubnets.ContainsKey(c.Str("Name"))) throw c.Fail("The client subnet " + c.Str("Name") + " already exists.", "WIN32 9907", "ResourceExists", c.Str("Name"), "Microsoft.Management.Infrastructure.CimException");
                s.Dns.ClientSubnets[c.Str("Name")] = c.Strs("IPv4Subnet").ToList();
            });
            DnsCmd("Get-DnsServerClientSubnet", "Name:string@0", (c, o) => { var s = DnsSrv(c); foreach (var kv in s.Dns.ClientSubnets) { if (c.Has("Name") && !WsText.Eq(kv.Key, c.Str("Name"))) continue; var p = new PsObj("Microsoft.Management.Infrastructure.CimInstance#DnsServerClientSubnet", "Microsoft.Management.Infrastructure.CimInstance"); p.Val("Name", "string", kv.Key); p.Val("IPv4Subnet", "string[]", kv.Value.Cast<object>().ToArray()); p.Val("IPv6Subnet", "string[]", null); c.Out(p); } });
            DnsCmd("Add-DnsServerZoneScope", "ZoneName:string@0 !; Name:string@1 !", (c, o) => { var s = DnsSrv(c); DnsOk(c, Lab(c).EditZone(s.Name, c.Str("ZoneName"), z => { if (z.Scopes.Contains(c.Str("Name"), StringComparer.OrdinalIgnoreCase)) return "The zone scope already exists."; z.Scopes.Add(c.Str("Name")); return null; }), c.Str("Name")); });
            DnsCmd("Get-DnsServerZoneScope", "ZoneName:string@0 !", (c, o) => { var s = DnsSrv(c); var z = s.Dns.Zone(c.Str("ZoneName")); if (z == null) return; foreach (var n in new[] { z.Name }.Concat(z.Scopes)) { var p = new PsObj("Microsoft.Management.Infrastructure.CimInstance#DnsServerZoneScope", "Microsoft.Management.Infrastructure.CimInstance"); p.Val("ZoneScope", "string", n); p.Val("FileName", "string", n + ".dns"); c.Out(p); } });
            DnsCmd("Add-DnsServerQueryResolutionPolicy", "Name:string@0 !; Action:string set=ALLOW|DENY|IGNORE; ClientSubnet:string; Fqdn:string; TimeOfDay:string; ZoneScope:string; ZoneName:string; ProcessingOrder:int; Disable:switch", (c, o) =>
            {
                var s = DnsSrv(c);
                if (s.Dns.Policies.Any(p => WsText.Eq(p.Name, c.Str("Name")))) throw c.Fail("A policy named " + c.Str("Name") + " already exists.", "WIN32 9971", "ResourceExists", c.Str("Name"), "Microsoft.Management.Infrastructure.CimException");
                if (c.Has("ClientSubnet")) foreach (var n in c.Str("ClientSubnet").Split(',').Skip(1)) if (!s.Dns.ClientSubnets.ContainsKey(n.Trim())) throw c.Fail("The client subnet " + n.Trim() + " doesn't exist (Add-DnsServerClientSubnet).", "WIN32 9907", "ObjectNotFound", n, "Microsoft.Management.Infrastructure.CimException");
                if (c.Has("ZoneScope") && !c.Has("ZoneName")) throw c.Fail("A policy with -ZoneScope needs -ZoneName.", "InvalidArgument", "InvalidArgument", null, "System.ArgumentException");
                if (c.Has("ZoneScope")) { var z = s.Dns.Zone(c.Str("ZoneName")); var sc = c.Str("ZoneScope").Split(';')[0].Split(',')[0].Trim(); if (z == null || !WsText.Eq(sc, z.Name) && !z.Scopes.Contains(sc, StringComparer.OrdinalIgnoreCase)) throw c.Fail("The zone scope " + sc + " doesn't exist in " + c.Str("ZoneName") + ".", "WIN32 9958", "ObjectNotFound", sc, "Microsoft.Management.Infrastructure.CimException"); }
                s.Dns.Policies.Add(new DnsPolicy { Name = c.Str("Name"), Action = (c.Str("Action") ?? "ALLOW").ToUpperInvariant(), ClientSubnet = c.Str("ClientSubnet") ?? "", Fqdn = c.Str("Fqdn") ?? "", TimeOfDay = c.Str("TimeOfDay") ?? "", ZoneScope = c.Str("ZoneScope") ?? "", Zone = c.Str("ZoneName") ?? "", Order = c.Int("ProcessingOrder", s.Dns.Policies.Count + 1), Enabled = !c.Sw("Disable") });
            });
            DnsCmd("Get-DnsServerQueryResolutionPolicy", "Name:string@0; ZoneName:string", (c, o) =>
            {
                var s = DnsSrv(c);
                foreach (var p in s.Dns.Policies.Where(x => (!c.Has("Name") || WsText.Eq(x.Name, c.Str("Name"))) && (!c.Has("ZoneName") || WsText.Eq(x.Zone, c.Str("ZoneName")))).OrderBy(x => x.Order))
                { var r = new PsObj("Microsoft.Management.Infrastructure.CimInstance#DnsServerPolicy", "Microsoft.Management.Infrastructure.CimInstance"); r.Val("Name", "string", p.Name); r.Val("ProcessingOrder", "uint32", (uint)p.Order); r.Val("IsEnabled", "bool", p.Enabled); r.Val("Action", "string", p.Action); r.Val("Criteria", "string", (p.ClientSubnet.Length > 0 ? "ClientSubnet " + p.ClientSubnet : "") + (p.Fqdn.Length > 0 ? " Fqdn " + p.Fqdn : "") + (p.TimeOfDay.Length > 0 ? " TimeOfDay " + p.TimeOfDay : "")); r.Val("Content", "string", p.ZoneScope); c.Out(r); }
            });
            DnsCmd("Remove-DnsServerQueryResolutionPolicy", "Name:string@0 !; ZoneName:string", (c, o) => { var s = DnsSrv(c); s.Dns.Policies.RemoveAll(p => WsText.Eq(p.Name, c.Str("Name"))); });
        }
    }
}
