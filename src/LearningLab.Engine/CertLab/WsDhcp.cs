// Cert Lab - DHCP in the AZ-800 network: servers (the role, authorized in Active Directory, the DHCP Server service
// running), IPv4 scopes with exclusions, reservations, options (003 router, 006 DNS servers, 015 DNS domain name) at
// server or scope level (the scope wins), leases on the simulated clock, and failover relationships (load balance or
// hot standby; when one partner is down the other serves every client - Cert Lab skips the MCLT wait and the states in
// between). Clients: CL1 (a real pretend PC) and a few pretend devices that only hold leases.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public class DhcpLease { public string Ip = "", Mac = "", Host = "", Server = "", Kind = "Lease"; public DateTime Expires; }
    public class DhcpRes { public string Ip = "", Mac = "", Name = "", Description = ""; }
    public class DhcpScope
    {
        public string ScopeId = "", Name = "", Start = "", End = "", Mask = "255.255.255.0", State = "Active", Description = "", Failover = "";
        public TimeSpan Lease = TimeSpan.FromDays(8);
        public readonly List<string[]> Exclusions = new List<string[]>();
        public readonly Dictionary<int, string> Options = new Dictionary<int, string>();
        public readonly List<DhcpRes> Reservations = new List<DhcpRes>();
        public readonly List<DhcpLease> Leases = new List<DhcpLease>();
        public int Prefix { get { uint m; Ip4.TryParse(Mask, out m); int n = 0; while (m != 0) { n += (int)(m & 1); m >>= 1; } return n; } }
        public bool InRange(string ip) { uint a, s, e; return Ip4.TryParse(ip, out a) && Ip4.TryParse(Start, out s) && Ip4.TryParse(End, out e) && a >= s && a <= e; }
        public bool Excluded(string ip) { uint a; Ip4.TryParse(ip, out a); foreach (var x in Exclusions) { uint s, e; if (Ip4.TryParse(x[0], out s) && Ip4.TryParse(x[1], out e) && a >= s && a <= e) return true; } return false; }
        public DhcpScope CopyConfig()
        {
            var c = new DhcpScope { ScopeId = ScopeId, Name = Name, Start = Start, End = End, Mask = Mask, State = State, Description = Description, Lease = Lease, Failover = Failover };
            c.Exclusions.AddRange(Exclusions.Select(x => new[] { x[0], x[1] }));
            foreach (var kv in Options) c.Options[kv.Key] = kv.Value;
            foreach (var r in Reservations) c.Reservations.Add(new DhcpRes { Ip = r.Ip, Mac = r.Mac, Name = r.Name, Description = r.Description });
            foreach (var l in Leases) c.Leases.Add(new DhcpLease { Ip = l.Ip, Mac = l.Mac, Host = l.Host, Server = l.Server, Expires = l.Expires, Kind = l.Kind });
            return c;
        }
    }
    public class DhcpFailover
    {
        public string Name = "", PartnerA = "", PartnerB = "", Mode = "LoadBalance", SharedSecret = "", State = "Normal";
        public int LoadBalancePercent = 50, ReservePercent = 5;
        public TimeSpan Mclt = TimeSpan.FromHours(1);
        public readonly List<string> ScopeIds = new List<string>();
        public string Partner(string me) { return WsText.Eq(me, PartnerA) ? PartnerB : PartnerA; }
    }
    public class WsDhcpServer
    {
        public WsServer S;
        public readonly List<DhcpScope> Scopes = new List<DhcpScope>();
        public readonly Dictionary<int, string> ServerOptions = new Dictionary<int, string>();
        public readonly List<DhcpFailover> Failovers = new List<DhcpFailover>();
        public bool Authorized { get { return S.Lab.DhcpAuthorized(S.Name); } }
        public bool Up { get { return S.Running && S.Has("DHCP") && S.SvcRunning("DHCPServer") && Authorized; } }
        public DhcpScope Scope(string id) { return Scopes.FirstOrDefault(x => WsText.Eq(x.ScopeId, id) || WsText.Eq(x.Name, id)); }
        public string Option(DhcpScope sc, int id) { string v; if (sc != null && sc.Options.TryGetValue(id, out v)) return v; return ServerOptions.TryGetValue(id, out v) ? v : ""; }
    }
    /// <summary>A device that gets its address by DHCP (CL1, or a pretend device).</summary>
    public class DhcpClient { public string Name = "", Mac = "", Subnet = "10.10.0.0", Ip = "", Server = "", Note = ""; public DateTime Expires, Obtained; public bool Pc; }

    public partial class WsServer
    {
        WsDhcpServer dhcp;
        public WsDhcpServer Dhcp { get { if (!Has("DHCP")) return null; if (dhcp == null) dhcp = new WsDhcpServer { S = this }; return dhcp; } }
    }

    public partial class WsLab
    {
        public readonly List<DhcpClient> DhcpClients = new List<DhcpClient>();
        public string NetServicesDn { get { return "CN=NetServices,CN=Services," + ConfigDn; } }
        public bool DhcpAuthorized(string server) { var s = Server(server); return s != null && Main.Live.Any(o => o.Is("dHCPClass") && (WsText.Eq(o.Name, s.M.Fqdn) || WsText.Eq(o.Get("dhcpIp"), s.M.Ip))); }
        public WsDhcpServer DhcpAt(string server) { var s = Server(server); return s == null ? null : s.Dhcp; }
        partial void DhcpHoldsPart(string ip, ref bool held) { held = DhcpClients.Any(c => c.Ip == ip); }

        /// <summary>A client asks for an address (DISCOVER / REQUEST). Returns null on success, else why it got none.</summary>
        public string Lease(DhcpClient c)
        {
            var offers = new List<KeyValuePair<WsDhcpServer, DhcpScope>>();
            foreach (var s in Servers.Values.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                var d = s.Dhcp;
                if (d == null || !d.Up) continue;
                var sc = d.Scopes.FirstOrDefault(x => x.ScopeId == c.Subnet && x.State == "Active");
                if (sc != null) offers.Add(new KeyValuePair<WsDhcpServer, DhcpScope>(d, sc));
            }
            // a client whose lease is still valid keeps its address when no server answers; only an expired one falls back to APIPA
            if (offers.Count == 0)
            {
                if (c.Expires > Now) return "No DHCP server answered on " + c.Subnet + "/24; the client keeps " + c.Ip + " until its lease runs out.";
                Fallback(c); return "No DHCP server answered on " + c.Subnet + "/24, so the client gave itself an APIPA address (169.254.x.x).";
            }
            var pick = offers[0];
            if (!string.IsNullOrEmpty(pick.Value.Failover) && offers.Count > 1)
            {
                var fo = pick.Key.Failovers.FirstOrDefault(f => f.Name == pick.Value.Failover);
                if (fo != null)
                {
                    string first = fo.Mode == "HotStandby" ? fo.PartnerA : (Math.Abs(FwHash.Of(c.Mac)) % 100 < fo.LoadBalancePercent ? fo.PartnerA : fo.PartnerB);
                    var o = offers.FirstOrDefault(x => WsText.Eq(x.Key.S.Name, first));
                    if (o.Key != null) pick = o;
                }
            }
            string ip = PickAddress(pick.Value, c);
            if (ip == null) { if (c.Expires <= Now) Fallback(c); return "The scope " + pick.Value.ScopeId + " on " + pick.Key.S.Name + " has no free addresses."; }
            var dsrv = pick.Key; var scope = pick.Value;
            foreach (var o in offers.Where(x => x.Value.Failover == scope.Failover && scope.Failover.Length > 0 || x.Key == dsrv)) { o.Value.Leases.RemoveAll(l => l.Mac == c.Mac || l.Ip == ip); }
            var lease = new DhcpLease { Ip = ip, Mac = c.Mac, Host = c.Name.ToLowerInvariant() + "." + DomainDns, Server = dsrv.S.Name, Expires = Now + scope.Lease, Kind = scope.Reservations.Any(r => r.Mac == c.Mac) ? "Reservation" : "Lease" };
            foreach (var o in offers.Where(x => x.Value.Failover == scope.Failover && scope.Failover.Length > 0 || x.Key == dsrv)) o.Value.Leases.Add(new DhcpLease { Ip = lease.Ip, Mac = lease.Mac, Host = lease.Host, Server = lease.Server, Expires = lease.Expires, Kind = lease.Kind });
            c.Ip = ip; c.Server = dsrv.S.Name; c.Expires = lease.Expires; c.Obtained = Now; c.Note = "";
            if (c.Pc) ApplyLease(c, dsrv, scope);
            var half = TimeSpan.FromTicks(scope.Lease.Ticks / 2);
            var cc = c;
            At("dhcp:" + c.Mac, Now + half, () => Renewal(cc), false);
            return null;
        }
        /// <summary>Renewal at half the lease; if that fails, retry when 7/8 of it has passed, and so on until it expires.</summary>
        void Renewal(DhcpClient c)
        {
            if (Lease(c) == null || c.Expires <= Now) return;
            var left = c.Expires - Now;
            var next = left.TotalMinutes > 2 ? Now + TimeSpan.FromTicks(left.Ticks * 3 / 4) : c.Expires;
            At("dhcp:" + c.Mac, next, () => Renewal(c), false);
        }
        string PickAddress(DhcpScope sc, DhcpClient c)
        {
            var res = sc.Reservations.FirstOrDefault(r => r.Mac == c.Mac);
            if (res != null) return res.Ip;
            var had = sc.Leases.FirstOrDefault(l => l.Mac == c.Mac && !sc.Reservations.Any(r => r.Ip == l.Ip));
            if (had != null) return had.Ip;
            uint s, e; Ip4.TryParse(sc.Start, out s); Ip4.TryParse(sc.End, out e);
            for (uint a = s; a <= e; a++)
            {
                string ip = Ip4.Str(a);
                if (sc.Excluded(ip) || sc.Reservations.Any(r => r.Ip == ip) || sc.Leases.Any(l => l.Ip == ip && l.Expires > Now) || Net.Machines.Any(m => m.Ip == ip && (m.Eth == null || !m.Eth.Dhcp))) continue;
                return ip;
            }
            return null;
        }
        void Fallback(DhcpClient c)
        {
            c.Ip = "169.254." + (Math.Abs(FwHash.Of(c.Mac)) % 200 + 20) + "." + (Math.Abs(FwHash.Of(c.Name)) % 200 + 20);
            c.Server = ""; c.Note = "APIPA";
            if (c.Pc) { var m = M(c.Name); SetAddress(m, c.Ip, 16, null, new List<string>()); }
        }
        void ApplyLease(DhcpClient c, WsDhcpServer d, DhcpScope sc)
        {
            var m = M(c.Name);
            var dns = d.Option(sc, 6).Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            SetAddress(m, c.Ip, sc.Prefix, d.Option(sc, 3), dns);
            // the client registers its name (dynamic update; Secure is fine for a domain member)
            foreach (var z in new[] { DomainDns })
                EditZone("DC1", z, zz => { if (zz.DynamicUpdate == "None") return null; zz.Records.RemoveAll(r => r.Type == "A" && WsText.Eq(r.Name, c.Name)); zz.Records.Add(new DnsRec { Name = c.Name.ToLowerInvariant(), Type = "A", Data = c.Ip, Ttl = 1200 }); return null; });
        }
        public static void SetAddress(PsMachine m, string ip, int prefix, string gateway, List<string> dns)
        {
            var eth = m.Eth;
            m.IpAddrs.RemoveAll(a => !a.V6 && a.IfIndex == eth.IfIndex);
            m.IpAddrs.Add(new NetIpAddr { Address = ip, IfIndex = eth.IfIndex, Prefix = prefix, PrefixOrigin = "Dhcp", SuffixOrigin = "Dhcp" });
            m.Routes.RemoveAll(r => r.Dest == "0.0.0.0/0" && r.IfIndex == eth.IfIndex);
            if (!string.IsNullOrEmpty(gateway)) m.Routes.Add(new NetRouteInfo { Dest = "0.0.0.0/0", NextHop = gateway.Split(',')[0].Trim(), IfIndex = eth.IfIndex });
            var l = m.DnsFor(eth.IfIndex); l.Clear(); l.AddRange(dns);
            m.SyncAddresses();
        }
    }
}
