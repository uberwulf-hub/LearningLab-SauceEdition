// Cert Lab - name resolution in the AZ-800 network: the DNS client (its DNS servers in order, the next one only when a
// server doesn't answer; short names get the primary DNS suffix and the suffix search list), and each DNS server
// (query resolution policies, authoritative zones and zone scopes, conditional forwarders and stub zones, then its
// forwarders and root hints when recursion is on). Outside the lab: the ISP's resolver (203.0.113.53, the Internet's
// names), the partner Tidewater Freight's DNS server (192.168.50.10, over the site-to-site VPN) and Azure's resolver.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public class DnsResp { public string Rcode = "OK"; public readonly List<DnsAnswer> Answers = new List<DnsAnswer>(); public string By = ""; }

    public partial class WsLab
    {
        // the Internet as the ISP's resolver sees it, and the partner's internal zone: name|type|data
        static readonly string[] InternetRecords = {
            "www.harborline.example|A|198.51.100.20", "harborline.example|A|198.51.100.20", "harborline.example|MX|10|mail.harborline.example", "mail.harborline.example|A|198.51.100.25",
            "www.tidewater.example|A|198.51.100.80", "tidewater.example|A|198.51.100.80", "updates.example.net|A|203.0.113.80", "www.example.org|A|203.0.113.10",
            "portal.azure.example|A|203.0.113.120" };
        static readonly string[] PartnerRecords = {
            "tidewater.example|A|192.168.50.10", "dc01.tidewater.example|A|192.168.50.10", "files.tidewater.example|A|192.168.50.20", "www.tidewater.example|A|198.51.100.80",
            "_ldap._tcp.tidewater.example|SRV|0|100|389|dc01.tidewater.example" };

        /// <summary>Can a machine reach an address? The two office subnets are routed to each other; the ISP's resolver and the
        /// partner (over the VPN) are reachable from every lab machine; Azure's network only through an Azure Network Adapter or VPN.</summary>
        public bool Delivers(PsMachine from, string ip)
        {
            if (ip.StartsWith("127.", StringComparison.Ordinal) || from.Addresses().Contains(ip)) return true;
            if (from.State != "running") return false;
            if (Ip4.SameNet(ip, "10.10.0.0", 24) || Ip4.SameNet(ip, "10.20.0.0", 24)) return ip.EndsWith(".1", StringComparison.Ordinal) || Net.Machines.Any(m => m.Ip == ip || m.ExtraIps.Contains(ip)) || DhcpHolds(ip);
            if (ip == IspDns || ip == PartnerDns || Ip4.SameNet(ip, "198.51.100.0", 24) || Ip4.SameNet(ip, "203.0.113.0", 24)) return true;
            if (Ip4.SameNet(ip, "10.50.0.0", 16) || ip == AzureDns) return Azure != null && Azure.Reaches(from.Name);
            return false;
        }
        partial void DhcpHoldsPart(string ip, ref bool held);
        bool DhcpHolds(string ip) { bool h = false; DhcpHoldsPart(ip, ref h); return h; }

        /// <summary>What a machine's DNS client gets for a name (server: ask only that server, as Resolve-DnsName -Server).</summary>
        public List<DnsAnswer> ClientResolve(PsMachine m, string name, string type, string server, out string error, out string fqdn)
        {
            error = null;
            string n = (name ?? "").Trim().TrimEnd('.');
            type = (type ?? "A").ToUpperInvariant();
            if (type == "A_AAAA" || type == "AAAA") type = "A";
            if (Ip4.IsV4(n)) { var p = n.Split('.'); n = p[3] + "." + p[2] + "." + p[1] + "." + p[0] + ".in-addr.arpa"; type = "PTR"; }
            fqdn = n;
            var servers = new List<string>();
            if (server != null) { var s = Server(server); servers.Add(s != null ? s.M.Ip : server); }
            else servers.AddRange(m.DnsFor(m.Eth.IfIndex));
            if (servers.Count == 0) { error = "NOSERVER"; return new List<DnsAnswer>(); }
            var names = new List<string>();
            if (n.Contains(".")) names.Add(n);
            else { names.Add(n + "." + m.Domain); foreach (var sfx in SearchList(m)) names.Add(n + "." + sfx); }
            DnsResp last = null;
            foreach (var cand in names.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                last = null;
                foreach (var ip in servers)
                {
                    if (!Delivers(m, ip)) continue;
                    var r = Query(ip, cand, type, m.Ip, 0);
                    if (r.Rcode == "TIMEOUT") continue;
                    last = r;
                    if (r.Rcode == "SERVFAIL" || r.Rcode == "REFUSED") continue;   // a server failure: the client tries the next server
                    break;
                }
                if (last == null) { error = "TIMEOUT"; return new List<DnsAnswer>(); }
                fqdn = cand;
                if (last.Rcode == "OK") return last.Answers;
            }
            error = last.Rcode == "NXDOMAIN" ? "NXDOMAIN" : last.Rcode == "REFUSED" ? "REFUSED" : "SERVFAIL";
            if (error == "SERVFAIL" || error == "REFUSED") error = "TIMEOUT";
            return new List<DnsAnswer>();
        }
        IEnumerable<string> SearchList(PsMachine m)
        {
            RsopResult r;
            if (LastComputer.TryGetValue(m.Name, out r)) { RsopValue v; if (r.C.TryGetValue("DnsSuffixList", out v) && v.State == "Enabled") return v.Value.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0); }
            return new string[0];
        }

        /// <summary>One query to the DNS server at an address.</summary>
        public DnsResp Query(string serverIp, string fqdn, string type, string clientIp, int depth)
        {
            var resp = new DnsResp { By = serverIp };
            if (depth > 6) { resp.Rcode = "SERVFAIL"; return resp; }
            if (serverIp == IspDns || serverIp == AzureDns) return Table(InternetRecords, fqdn, type, resp);
            if (serverIp == PartnerDns) { if (fqdn.EndsWith("tidewater.example", StringComparison.OrdinalIgnoreCase)) return Table(PartnerRecords, fqdn, type, resp); return Query(IspDns, fqdn, type, PartnerDns, depth + 1); }
            var s = Server(serverIp);
            var ds = s == null ? null : s.Dns;
            if (ds == null || !ds.Up) { resp.Rcode = "TIMEOUT"; return resp; }
            string scope = "";
            var pol = PolicyFor(ds, null, fqdn, clientIp);
            if (pol != null && pol.Action == "DENY") { resp.Rcode = "REFUSED"; return resp; }
            if (pol != null && pol.Action == "IGNORE") { resp.Rcode = "TIMEOUT"; return resp; }
            var zone = ds.ZoneFor(fqdn);
            if (zone != null && (zone.Type == "Forwarder" || zone.Type == "Stub"))
            {
                if (zone.Type == "Forwarder" && !ds.Recursion) { resp.Rcode = "SERVFAIL"; return resp; }
                foreach (var ip in zone.Masters) { if (!Delivers(s.M, ip)) continue; var r = Query(ip, fqdn, type, s.M.Ip, depth + 1); if (r.Rcode != "TIMEOUT") return r; }
                resp.Rcode = "SERVFAIL"; return resp;
            }
            if (zone != null)
            {
                if (zone.Type == "Secondary" && zone.Expired) { resp.Rcode = "SERVFAIL"; return resp; }
                var zp = PolicyFor(ds, zone, fqdn, clientIp);
                if (zp != null && zp.Action == "DENY") { resp.Rcode = "REFUSED"; return resp; }
                if (zp != null && zp.Action == "IGNORE") { resp.Rcode = "TIMEOUT"; return resp; }
                if (zp != null && zp.ZoneScope.Length > 0) scope = zp.ZoneScope.Split(';')[0].Split(',')[0].Trim();
                return Authoritative(ds, zone, fqdn, type, scope, clientIp, depth, resp);
            }
            if (!ds.Recursion) { resp.Rcode = "REFUSED"; return resp; }
            foreach (var f in ds.Forwarders) { if (!Delivers(s.M, f)) continue; var r = Query(f, fqdn, type, s.M.Ip, depth + 1); if (r.Rcode != "TIMEOUT") return r; }
            if (ds.UseRootHints) return Table(InternetRecords, fqdn, type, resp);
            resp.Rcode = "SERVFAIL"; return resp;
        }

        DnsResp Authoritative(WsDnsServer ds, DnsZone zone, string fqdn, string type, string scope, string clientIp, int depth, DnsResp resp)
        {
            string rel = zone.Rel(fqdn);
            var at = zone.Records.Where(r => WsText.Eq(r.Name, rel) && WsText.Eq(r.Scope, scope)).ToList();
            if (at.Count == 0 && scope.Length > 0) at = zone.Records.Where(r => WsText.Eq(r.Name, rel) && r.Scope.Length == 0).ToList();
            var hit = at.Where(r => WsText.Eq(r.Type, type) || type == "ANY" || type == "ALL").ToList();
            foreach (var r in hit) resp.Answers.Add(new DnsAnswer { Name = fqdn.ToLowerInvariant(), Type = r.Type, Data = r.Data, Ttl = r.Ttl });
            if (hit.Count > 0) return resp;
            var cn = at.FirstOrDefault(r => r.Type == "CNAME");
            if (cn != null)
            {
                resp.Answers.Add(new DnsAnswer { Name = fqdn.ToLowerInvariant(), Type = "CNAME", Data = cn.Data.TrimEnd('.'), Ttl = cn.Ttl });
                var next = Query(ds.S.M.Ip, cn.Data.TrimEnd('.'), type, clientIp, depth + 1);
                resp.Answers.AddRange(next.Answers);
                return resp;
            }
            if (at.Count > 0) { resp.Answers.Add(new DnsAnswer { Name = zone.Name, Type = "SOA", Section = "Authority", Data = ds.S.M.Fqdn + "|hostmaster." + zone.Name + "|" + (zone.Records.Count + 30), Ttl = 3600 }); return resp; }
            resp.Rcode = "NXDOMAIN"; return resp;
        }

        static DnsResp Table(string[] rows, string fqdn, string type, DnsResp resp)
        {
            var at = rows.Select(r => r.Split(new[] { '|' }, 3)).Where(p => WsText.Eq(p[0], fqdn)).ToList();
            foreach (var p in at.Where(p => WsText.Eq(p[1], type) || type == "ANY")) resp.Answers.Add(new DnsAnswer { Name = fqdn.ToLowerInvariant(), Type = p[1], Data = p[2], Ttl = 300 });
            if (at.Count == 0) resp.Rcode = "NXDOMAIN";
            return resp;
        }

        /// <summary>The first enabled policy (server level when zone is null) whose conditions match the query.</summary>
        DnsPolicy PolicyFor(WsDnsServer ds, DnsZone zone, string fqdn, string clientIp)
        {
            foreach (var p in ds.Policies.Where(x => x.Enabled && (zone == null ? x.Zone.Length == 0 : WsText.Eq(x.Zone, zone.Name))).OrderBy(x => x.Order))
            {
                if (p.ClientSubnet.Length > 0 && !Criterion(p.ClientSubnet, v => ds.ClientSubnets.ContainsKey(v) && ds.ClientSubnets[v].Any(c => InCidr(clientIp, c)))) continue;
                if (p.Fqdn.Length > 0 && !Criterion(p.Fqdn, v => WsText.Like(fqdn, v.TrimEnd('.')))) continue;
                if (p.TimeOfDay.Length > 0 && !Criterion(p.TimeOfDay, v => InTime(v))) continue;
                return p;
            }
            return null;
        }
        static bool Criterion(string spec, Func<string, bool> test)
        {
            var parts = spec.Split(',').Select(x => x.Trim()).ToList();
            bool eq = !WsText.Eq(parts[0], "NE");
            bool any = parts.Skip(1).Any(test);
            return eq ? any : !any;
        }
        public static bool InCidr(string ip, string cidr) { var p = cidr.Split('/'); int len; return p.Length == 2 && int.TryParse(p[1], out len) && Ip4.SameNet(ip, p[0], len); }
        bool InTime(string range)
        {
            var p = range.Split('-'); if (p.Length != 2) return false;
            TimeSpan a, b; if (!TimeSpan.TryParse(p[0], CultureInfo.InvariantCulture, out a) || !TimeSpan.TryParse(p[1], CultureInfo.InvariantCulture, out b)) return false;
            var t = Now.TimeOfDay; return a <= b ? t >= a && t <= b : t >= a || t <= b;
        }
    }
}
