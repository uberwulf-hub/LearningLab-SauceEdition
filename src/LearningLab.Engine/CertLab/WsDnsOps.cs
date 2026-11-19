// Cert Lab - managing the pretend DNS servers: zones (file-backed or stored in Active Directory), records with the
// usual rules (a CNAME stands alone at its name), zone transfers to secondaries, and the starting zones of corp.example.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public partial class WsServer
    {
        WsDnsServer dns;
        /// <summary>The DNS server on this machine (null unless the DNS role is installed).</summary>
        public WsDnsServer Dns { get { if (!Has("DNS")) return null; if (dns == null) dns = new WsDnsServer { S = this }; return dns; } }
    }

    public partial class WsLab
    {
        public string DnsPartition(string scope) { return scope == "Forest" ? "CN=MicrosoftDNS,DC=ForestDnsZones," + DomainDn : scope == "Legacy" ? "CN=MicrosoftDNS,CN=System," + DomainDn : "CN=MicrosoftDNS,DC=DomainDnsZones," + DomainDn; }
        public IEnumerable<WsAdObj> AdZones(WsDir d) { return d.Live.Where(o => o.Is("dnsZone")); }
        public WsDnsServer DnsAt(string server) { var s = Server(server); return s == null ? null : s.Dns; }

        /// <summary>Changes a zone on a server and saves it where it lives (the server's file zones, or the directory).</summary>
        public string EditZone(string server, string zone, Func<DnsZone, string> change)
        {
            var ds = DnsAt(server);
            if (ds == null) return "The DNS Server role isn't installed on " + server + ".";
            var fz = ds.FileZones.FirstOrDefault(z => WsText.Eq(z.Name, zone));
            if (fz != null) return change(fz);
            var d = Dir(server);
            var o = d == null ? null : AdZones(d).FirstOrDefault(x => WsText.Eq(x.Name, zone));
            if (o == null) return "The zone " + zone + " was not found on server " + server + ".";
            var z0 = DnsZone.FromAd(o);
            string err = change(z0);
            if (err != null) return err;
            o.Set("zoneData", z0.Serialize()); o.Set("dnsScope", z0.ReplicationScope);
            Touch(server, o);
            return null;
        }

        public string AddZone(string server, DnsZone z)
        {
            var ds = DnsAt(server);
            if (ds == null) return "The DNS Server role isn't installed on " + server + ".";
            if (ds.Zone(z.Name) != null) return "The zone " + z.Name + " already exists on " + server + ".";
            if (z.AdIntegrated)
            {
                var d = Dir(server);
                if (d == null) return "Only a domain controller can store a zone in Active Directory (" + server + " isn't one).";
                var o = new WsAdObj { Guid = NewGuid(), Dn = "DC=" + z.Name + "," + DnsPartition(z.ReplicationScope), Class = "dnsZone" };
                o.Set("name", z.Name).Set("dnsScope", z.ReplicationScope).Set("zoneData", z.Serialize());
                Touch(server, o);
                return null;
            }
            z.Loaded = Now;
            ds.FileZones.Add(z);
            if (z.Type == "Secondary") { Transfer(server, z); ScheduleTransfer(server, z.Name); }
            return null;
        }
        public string RemoveZone(string server, string zone)
        {
            var ds = DnsAt(server);
            if (ds == null) return "The DNS Server role isn't installed on " + server + ".";
            if (ds.FileZones.RemoveAll(z => WsText.Eq(z.Name, zone)) > 0) return null;
            var d = Dir(server);
            var o = d == null ? null : AdZones(d).FirstOrDefault(x => WsText.Eq(x.Name, zone));
            if (o == null) return "The zone " + zone + " was not found on server " + server + ".";
            o.Deleted = true; Touch(server, o);
            return null;
        }

        public string AddRecord(string server, string zone, DnsRec r)
        {
            return EditZone(server, zone, z =>
            {
                if (z.Type != "Primary") return "Records can only be added to a primary zone (" + z.Name + " is " + z.Type.ToLowerInvariant() + ").";
                if (r.Scope.Length > 0 && !z.Scopes.Contains(r.Scope, StringComparer.OrdinalIgnoreCase)) return "The zone scope " + r.Scope + " doesn't exist in " + z.Name + ".";
                var same = z.Records.Where(x => WsText.Eq(x.Name, r.Name) && WsText.Eq(x.Scope, r.Scope)).ToList();
                if (r.Type == "CNAME" && same.Count > 0 || same.Any(x => x.Type == "CNAME")) return "A CNAME record can't share its name with other records (" + r.Name + "." + z.Name + ").";
                if (same.Any(x => x.Type == r.Type && WsText.Eq(x.Data, r.Data))) return "The resource record already exists.";
                z.Records.Add(r);
                return null;
            });
        }
        public string RemoveRecord(string server, string zone, string name, string type, string data)
        {
            return EditZone(server, zone, z =>
            {
                int n = z.Records.RemoveAll(x => WsText.Eq(x.Name, name) && (type == null || WsText.Eq(x.Type, type)) && (string.IsNullOrEmpty(data) || WsText.Eq(x.Data, data)));
                return n == 0 ? "Failed to find the resource record " + name + " in zone " + zone + "." : null;
            });
        }

        /// <summary>Copies a secondary zone from its first master that allows the transfer.</summary>
        public void Transfer(string server, DnsZone sec)
        {
            var me = Server(server);
            foreach (var mip in sec.Masters)
            {
                var ms = Server(mip); var md = ms == null ? null : ms.Dns;
                if (md == null || !md.Up) continue;
                var src = md.Zone(sec.Name);
                if (src == null || src.Type == "Secondary" && src.Records.Count == 0) continue;
                bool ok = src.Transfers == "TransferAnyServer" || src.Transfers == "TransferToSecureServers" && src.SecondaryServers.Contains(me.M.Ip) || src.Transfers == "TransferToZoneNameServer" && src.Records.Any(r => r.Type == "NS" && (WsText.Eq(r.Data, me.M.Fqdn) || WsText.Eq(r.Data, me.M.Fqdn + ".")));
                if (!ok) { sec.Expired = true; Note("Zone transfer of " + sec.Name + " to " + server + " was refused by " + ms.Name + "."); continue; }
                sec.Records.Clear();
                foreach (var r in src.Records.Where(x => x.Scope.Length == 0)) sec.Records.Add(r.Clone());
                sec.Expired = false; sec.Loaded = Now;
                return;
            }
        }
        void ScheduleTransfer(string server, string zone)
        {
            At("dnsxfr:" + server + ":" + zone, Now.AddMinutes(15), () =>
            {
                var ds = DnsAt(server); var z = ds == null ? null : ds.FileZones.FirstOrDefault(x => WsText.Eq(x.Name, zone) && x.Type == "Secondary");
                if (z == null) return;
                Transfer(server, z); ScheduleTransfer(server, zone);
            }, false);
        }

        partial void SeedDns()
        {
            var z = new DnsZone { Name = DomainDns, ReplicationScope = "Domain", DynamicUpdate = "Secure" };
            Action<string, string, string> rr = (n, t, v) => z.Records.Add(new DnsRec { Name = n, Type = t, Data = v, Ttl = t == "A" && n != "@" ? 1200 : 3600 });
            rr("@", "NS", "dc1." + DomainDns); rr("@", "NS", "dc2." + DomainDns);
            rr("@", "A", "10.10.0.10"); rr("@", "A", "10.20.0.11");
            foreach (var row in MachineList) rr(row[0].ToLowerInvariant(), "A", row[1]);
            foreach (var s in new[] { "_ldap._tcp", "_kerberos._tcp" }) { rr(s, "SRV", "0|100|" + (s.StartsWith("_ldap", StringComparison.Ordinal) ? "389" : "88") + "|dc1." + DomainDns); rr(s, "SRV", "0|100|" + (s.StartsWith("_ldap", StringComparison.Ordinal) ? "389" : "88") + "|dc2." + DomainDns); }
            rr("intranet", "CNAME", "srv1." + DomainDns);
            rr("@", "MX", "10|mail.harborline.example");
            var rev = new DnsZone { Name = "0.10.10.in-addr.arpa", ReplicationScope = "Domain", DynamicUpdate = "Secure" };
            foreach (var row in MachineList.Where(r => r[1].StartsWith("10.10.0.", StringComparison.Ordinal))) rev.Records.Add(new DnsRec { Name = row[1].Substring(8), Type = "PTR", Data = row[0].ToLowerInvariant() + "." + DomainDns, Ttl = 1200 });
            rev.Records.Add(new DnsRec { Name = "@", Type = "NS", Data = "dc1." + DomainDns });
            var msdcs = new DnsZone { Name = "_msdcs." + DomainDns, ReplicationScope = "Forest", DynamicUpdate = "Secure" };
            msdcs.Records.Add(new DnsRec { Name = "@", Type = "NS", Data = "dc1." + DomainDns });
            msdcs.Records.Add(new DnsRec { Name = "_ldap._tcp.dc", Type = "SRV", Data = "0|100|389|dc1." + DomainDns });
            msdcs.Records.Add(new DnsRec { Name = "_ldap._tcp.dc", Type = "SRV", Data = "0|100|389|dc2." + DomainDns });
            msdcs.Records.Add(new DnsRec { Name = "gc", Type = "A", Data = "10.10.0.10" });
            foreach (var zz in new[] { z, rev, msdcs }) AddZone("DC1", zz);
            foreach (var n in new[] { "DC1", "DC2" }) DnsAt(n).Forwarders.Add(IspDns);
            // every machine asks DC1 first, then DC2 (the DCs ask themselves first)
            foreach (var s in Servers.Values)
            {
                var l = s.M.DnsFor(s.M.Eth.IfIndex); l.Clear();
                if (s.Name == "DC2") { l.Add("10.20.0.11"); l.Add("10.10.0.10"); } else { l.Add("10.10.0.10"); l.Add("10.20.0.11"); }
                s.M.Routes.Clear();
                s.M.Routes.Add(new NetRouteInfo { Dest = "0.0.0.0/0", NextHop = s.M.Ip.StartsWith("10.20.", StringComparison.Ordinal) ? "10.20.0.1" : "10.10.0.1", IfIndex = s.M.Eth.IfIndex });
            }
        }
        public const string IspDns = "203.0.113.53", PartnerDns = "192.168.50.10", AzureDns = "168.63.129.16";
    }
}
