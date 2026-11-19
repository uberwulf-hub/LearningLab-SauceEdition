// Cert Lab - console actions of DNS Manager (practice) and the DHCP tool (in Windows Admin Center, practice).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public static partial class WsOps
    {
        static readonly string[] ZoneTypes = { "Primary", "Secondary", "Stub", "Forwarder" };
        static readonly string[] Dynamics = { "None", "Secure", "NonsecureAndSecure" };
        static readonly string[] Transfers = { "NoTransfer", "TransferAnyServer", "TransferToZoneNameServer", "TransferToSecureServers" };

        static partial void RunNet(WsLab lab, string v, string noun, List<string> tok, ref WsResult r)
        {
            if (v != "dns" && v != "dhcp") return;
            var a = Parse(tok, 2);
            string server = a["server"] ?? "DC1";
            var srv = lab.Server(server);
            if (srv == null) { r = WsResult.Fail("There is no server " + server + "."); return; }
            if (v == "dns")
            {
                switch (noun)
                {
                    case "zone":
                        {
                            string act = a.P(0) ?? "";
                            if (act == "add")
                            {
                                string type = ZoneTypes.FirstOrDefault(x => WsText.Eq(x, a["type"] ?? "Primary"));
                                if (type == null) { r = WsResult.Fail("Zone type: Primary, Secondary, Stub or Forwarder."); return; }
                                string scope = a["ad"] == null || WsText.Eq(a["ad"], "no") ? "" : new[] { "Forest", "Domain", "Legacy" }.FirstOrDefault(x => WsText.Eq(x, a["ad"]));
                                if (scope == null) { r = WsResult.Fail("ad= Forest, Domain, Legacy or no."); return; }
                                if (type == "Secondary" && scope.Length > 0) { r = WsResult.Fail("A secondary zone can't be stored in Active Directory."); return; }
                                var z = new DnsZone { Name = (a["name"] ?? a.P(1) ?? "").TrimEnd('.').ToLowerInvariant(), Type = type, ReplicationScope = scope, DynamicUpdate = scope.Length > 0 ? "Secure" : "None", Transfers = scope.Length > 0 ? "NoTransfer" : "TransferToZoneNameServer" };
                                if (z.Name.Length == 0) { r = WsResult.Fail("A zone needs a name."); return; }
                                if (a["dynamic"] != null) { z.DynamicUpdate = Dynamics.FirstOrDefault(x => WsText.Eq(x, a["dynamic"])); if (z.DynamicUpdate == null) { r = WsResult.Fail("dynamic= None, Secure or NonsecureAndSecure."); return; } if (z.DynamicUpdate == "Secure" && scope.Length == 0) { r = WsResult.Fail("Secure dynamic updates need a zone stored in Active Directory."); return; } }
                                if (type != "Primary") { z.Masters.AddRange((a["masters"] ?? "").Split(',').Select(x => x.Trim()).Where(x => x.Length > 0)); if (z.Masters.Count == 0) { r = WsResult.Fail("A " + type.ToLowerInvariant() + " zone needs master servers (masters=IP)."); return; } z.DynamicUpdate = "None"; }
                                if (type == "Primary") z.Records.Add(new DnsRec { Name = "@", Type = "NS", Data = srv.M.Fqdn });
                                r = R(lab.AddZone(srv.Name, z), "Created the " + type.ToLowerInvariant() + " zone " + z.Name + " on " + srv.Name + (scope.Length > 0 ? " (stored in Active Directory, replicated to the " + scope.ToLowerInvariant() + ")" : "") + "."); return;
                            }
                            string zn = a["name"] ?? a.P(1);
                            if (act == "remove") { r = R(lab.RemoveZone(srv.Name, zn), "Deleted the zone " + zn + "."); return; }
                            if (act == "set")
                            {
                                r = R(lab.EditZone(srv.Name, zn, z =>
                                {
                                    if (a["dynamic"] != null) { var x = Dynamics.FirstOrDefault(y => WsText.Eq(y, a["dynamic"])); if (x == null) return "dynamic= None, Secure or NonsecureAndSecure."; if (x == "Secure" && !z.AdIntegrated) return "Secure dynamic updates need a zone stored in Active Directory."; z.DynamicUpdate = x; }
                                    if (a["transfers"] != null) { var x = Transfers.FirstOrDefault(y => WsText.Eq(y, a["transfers"])); if (x == null) return "transfers= " + string.Join(", ", Transfers) + "."; z.Transfers = x; }
                                    if (a["secondaries"] != null) { z.SecondaryServers.Clear(); z.SecondaryServers.AddRange(a["secondaries"].Split(',').Select(s => s.Trim()).Where(s => s.Length > 0)); }
                                    if (a["ad"] != null) { if (z.Type != "Primary" && z.Type != "Stub" && z.Type != "Forwarder") return "Only primary, stub and forwarder zones can be stored in Active Directory."; }
                                    if (a["aging"] != null) z.Aging = WsText.True(a["aging"]);
                                    return null;
                                }), "Saved the zone " + zn + "."); return;
                            }
                            if (act == "sign") { r = R(lab.EditZone(srv.Name, zn, z => { if (z.Type != "Primary") return "Only a primary zone can be signed."; z.Signed = true; if (!z.Records.Any(x => x.Type == "DNSKEY")) { z.Records.Add(new DnsRec { Name = "@", Type = "DNSKEY", Data = "257 3 8 (KSK)" }); z.Records.Add(new DnsRec { Name = "@", Type = "DNSKEY", Data = "256 3 8 (ZSK)" }); } return null; }), "Signed the zone " + zn + " with DNSSEC (default settings)."); return; }
                            if (act == "unsign") { r = R(lab.EditZone(srv.Name, zn, z => { z.Signed = false; z.Records.RemoveAll(x => x.Type == "DNSKEY" || x.Type == "RRSIG"); return null; }), "Unsigned the zone " + zn + "."); return; }
                            if (act == "transfer") { var ds = lab.DnsAt(srv.Name); var z = ds == null ? null : ds.FileZones.FirstOrDefault(x => WsText.Eq(x.Name, zn) && x.Type == "Secondary"); if (z == null) { r = WsResult.Fail(zn + " isn't a secondary zone on " + srv.Name + "."); return; } lab.Transfer(srv.Name, z); r = z.Expired ? WsResult.Fail("The zone transfer was refused by the master.") : WsResult.Done("Transferred " + zn + " from its master."); return; }
                            r = WsResult.Fail("dns zone add|remove|set|sign|unsign|transfer"); return;
                        }
                    case "record":
                        {
                            string act = a.P(0) ?? "", zone = a["zone"], name = a["name"] ?? "@", type = (a["type"] ?? "A").ToUpperInvariant(), data = a["data"];
                            if (act == "add")
                            {
                                if (string.IsNullOrEmpty(data)) { r = WsResult.Fail("A record needs its data (data=10.10.0.80, data=srv1.corp.example, data=10|mail.x for MX)."); return; }
                                if (type == "A" && !Ip4.IsV4(data)) { r = WsResult.Fail("An A record holds an IPv4 address."); return; }
                                var rec = new DnsRec { Name = name.ToLowerInvariant(), Type = type, Data = data, Ttl = a.Int("ttl", 3600), Scope = a["scope"] ?? "" };
                                var e = lab.AddRecord(srv.Name, zone, rec);
                                if (e == null && type == "A" && a.Yes("ptr", false))
                                {
                                    var p = data.Split('.'); string rz = p[2] + "." + p[1] + "." + p[0] + ".in-addr.arpa";
                                    var ds = lab.DnsAt(srv.Name);
                                    if (ds == null || ds.Zone(rz) == null) e = "The A record was added, but the reverse zone " + rz + " doesn't exist for the PTR record.";
                                    else e = lab.AddRecord(srv.Name, rz, new DnsRec { Name = p[3], Type = "PTR", Data = (name == "@" ? zone : name + "." + zone).ToLowerInvariant() });
                                }
                                r = R(e, "Added " + type + " " + name + " -> " + data + " in " + zone + "."); return;
                            }
                            if (act == "remove") { r = R(lab.RemoveRecord(srv.Name, zone, name, type, data), "Deleted " + type + " " + name + " from " + zone + "."); return; }
                            r = WsResult.Fail("dns record add|remove"); return;
                        }
                    case "forwarders":
                        {
                            var ds = lab.DnsAt(srv.Name);
                            if (ds == null) { r = WsResult.Fail("The DNS Server role isn't installed on " + srv.Name + "."); return; }
                            var l = (a["set"] ?? a.P(0) ?? "").Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                            if (l.Any(x => !Ip4.IsV4(x))) { r = WsResult.Fail("Forwarders are IP addresses."); return; }
                            ds.Forwarders.Clear(); ds.Forwarders.AddRange(l);
                            if (a["roothints"] != null) ds.UseRootHints = WsText.True(a["roothints"]);
                            r = WsResult.Done(srv.Name + " forwards to " + (l.Count == 0 ? "nobody (root hints)" : WsText.Join(l)) + "."); return;
                        }
                    case "recursion":
                        {
                            var ds = lab.DnsAt(srv.Name);
                            if (ds == null) { r = WsResult.Fail("The DNS Server role isn't installed on " + srv.Name + "."); return; }
                            ds.Recursion = WsText.True(a.P(0) ?? "on");
                            r = WsResult.Done("Recursion is " + (ds.Recursion ? "on" : "off") + " on " + srv.Name + "."); return;
                        }
                }
                return;
            }
            RunDhcp(lab, srv, noun, a, ref r);
        }
    }
}
