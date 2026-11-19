// Cert Lab - the network settings of each pretend Windows machine: network adapters, IP addresses, routes, DNS client
// settings and the Windows Firewall (rules and profiles), plus the logic the networking cmdlets and programs share:
// can this machine reach that address, does the firewall let a ping or a TCP connection in, how a name resolves
// (hosts file, own name, then the DNS server DC01 answers for lab.example.com). Nothing here touches the real network.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public class NetAdapterInfo
    {
        public string Name = "Ethernet", Description = "Microsoft Hyper-V Network Adapter", Mac = "", Status = "Up", LinkSpeed = "10 Gbps", Guid = "";
        public int IfIndex = 6, IfMetric = 15, Mtu = 1500;
        public bool Dhcp, Loopback;
        public bool IsUp { get { return Status == "Up"; } }
    }

    public class NetIpAddr
    {
        public string Address = "", PrefixOrigin = "Manual", SuffixOrigin = "Manual";
        public int IfIndex = 6, Prefix = 24;
        public bool V6, SkipAsSource;
        public string Display { get { return V6 && Address.StartsWith("fe80:", StringComparison.OrdinalIgnoreCase) ? Address + "%" + IfIndex : Address; } }
    }

    public class NetRouteInfo
    {
        public string Dest = "0.0.0.0/0", NextHop = "0.0.0.0";
        public int IfIndex = 6, Metric = 256;
        public bool V6;
    }

    public class FwRule
    {
        public string Name = "", DisplayName = "", Description = "", DisplayGroup = "", Group = "";
        public bool Enabled = true;
        public string Direction = "Inbound", Action = "Allow", Profile = "Any", Protocol = "Any", LocalPort = "Any", RemotePort = "Any", RemoteAddress = "Any", LocalAddress = "Any", Program = "Any", IcmpType = "Any";
        public bool Builtin;
    }

    public class FwProfile
    {
        public string Name = "Domain", DefaultInboundAction = "NotConfigured", DefaultOutboundAction = "NotConfigured";
        public bool Enabled = true, LogAllowed, LogBlocked;
    }

    public static class Ip4
    {
        public static bool TryParse(string s, out uint v)
        {
            v = 0;
            if (string.IsNullOrEmpty(s)) return false;
            var p = s.Trim().Split('.');
            if (p.Length != 4) return false;
            foreach (var x in p)
            {
                int b;
                if (!int.TryParse(x, NumberStyles.None, CultureInfo.InvariantCulture, out b) || b < 0 || b > 255) return false;
                v = (v << 8) | (uint)b;
            }
            return true;
        }
        public static bool IsV4(string s) { uint v; return TryParse(s, out v); }
        public static string Str(uint v) { return ((v >> 24) & 255) + "." + ((v >> 16) & 255) + "." + ((v >> 8) & 255) + "." + (v & 255); }
        public static uint Mask(int prefix) { return prefix <= 0 ? 0u : prefix >= 32 ? 0xFFFFFFFFu : ~((1u << (32 - prefix)) - 1); }
        public static string MaskStr(int prefix) { return Str(Mask(prefix)); }
        public static bool SameNet(string a, string b, int prefix)
        {
            uint x, y;
            if (!TryParse(a, out x) || !TryParse(b, out y)) return false;
            return (x & Mask(prefix)) == (y & Mask(prefix));
        }
        public static string Network(string a, int prefix) { uint x; if (!TryParse(a, out x)) return a; return Str(x & Mask(prefix)); }
        public static string Broadcast(string a, int prefix) { uint x; if (!TryParse(a, out x)) return a; return Str((x & Mask(prefix)) | ~Mask(prefix)); }
        /// <summary>Does an address match a firewall or route scope ("Any", "LocalSubnet", "10.0.10.0/24", "10.0.10.5", "10.0.10.1-10.0.10.9")?</summary>
        public static bool InScope(string ip, string scope, PsMachine local)
        {
            if (string.IsNullOrEmpty(scope) || scope.Equals("Any", StringComparison.OrdinalIgnoreCase)) return true;
            foreach (var raw in scope.Split(','))
            {
                var s = raw.Trim();
                if (s.Equals("LocalSubnet", StringComparison.OrdinalIgnoreCase)) { if (local != null && local.LocalSubnetHas(ip)) return true; continue; }
                int slash = s.IndexOf('/');
                if (slash > 0)
                {
                    int pre;
                    string net = s.Substring(0, slash), m = s.Substring(slash + 1);
                    if (!int.TryParse(m, out pre)) { uint mv; pre = TryParse(m, out mv) ? CountBits(mv) : 32; }
                    if (SameNet(ip, net, pre)) return true;
                    continue;
                }
                int dash = s.IndexOf('-');
                if (dash > 0)
                {
                    uint a, b, x;
                    if (TryParse(s.Substring(0, dash), out a) && TryParse(s.Substring(dash + 1), out b) && TryParse(ip, out x) && x >= a && x <= b) return true;
                    continue;
                }
                if (s == ip) return true;
            }
            return false;
        }
        static int CountBits(uint v) { int n = 0; while (v != 0) { n += (int)(v & 1); v >>= 1; } return n; }
    }

    /// <summary>One answer of a name lookup.</summary>
    public class DnsAnswer
    {
        public string Name = "", Type = "A", Data = "", Section = "Answer";
        public int Ttl = 1200;
    }

    public partial class PsMachine
    {
        public readonly List<NetAdapterInfo> Adapters = new List<NetAdapterInfo>();
        public readonly List<NetIpAddr> IpAddrs = new List<NetIpAddr>();
        public readonly List<NetRouteInfo> Routes = new List<NetRouteInfo>();          // default gateways and routes added by hand
        public readonly Dictionary<int, List<string>> DnsServers = new Dictionary<int, List<string>>();
        public readonly List<FwRule> FwRules = new List<FwRule>();
        public readonly List<FwProfile> FwProfiles = new List<FwProfile>();
        public string ActiveProfile = "Domain";
        public string LinkLocal = "";
        public int DnsQueries;                                                          // DNS lookups since the cache was cleared

        partial void SeedNet()
        {
            int last = int.Parse(Ip.Substring(Ip.LastIndexOf('.') + 1), CultureInfo.InvariantCulture);
            var eth = new NetAdapterInfo { Name = "Ethernet", IfIndex = IsServer ? 6 : 4, Mac = "00-15-5D-0A-0A-" + last.ToString("X2", CultureInfo.InvariantCulture), Guid = "{" + (IsServer ? "8f6d4c2a-91b3-4f0e-a5c7-1d2e3f405162" : "2b7e9a14-6c3d-4e8f-b1a2-c3d4e5f60718").ToUpperInvariant() + "}" };
            Adapters.Add(eth);
            Adapters.Add(new NetAdapterInfo { Name = "Loopback Pseudo-Interface 1", Description = "Software Loopback Interface 1", IfIndex = 1, IfMetric = 75, Mtu = -1, Loopback = true, Mac = "", LinkSpeed = "1 Gbps" });
            LinkLocal = "fe80::" + (IsServer ? "8c41:5d2a:3b1f:a0b" : "c5e2:7a19:4d3b:9f21");
            IpAddrs.Add(new NetIpAddr { Address = LinkLocal, IfIndex = eth.IfIndex, Prefix = 64, V6 = true, PrefixOrigin = "WellKnown", SuffixOrigin = "Link" });
            IpAddrs.Add(new NetIpAddr { Address = "::1", IfIndex = 1, Prefix = 128, V6 = true, PrefixOrigin = "WellKnown", SuffixOrigin = "WellKnown" });
            IpAddrs.Add(new NetIpAddr { Address = Ip, IfIndex = eth.IfIndex, Prefix = 24 });
            IpAddrs.Add(new NetIpAddr { Address = "127.0.0.1", IfIndex = 1, Prefix = 8, PrefixOrigin = "WellKnown", SuffixOrigin = "WellKnown" });
            Routes.Add(new NetRouteInfo { Dest = "0.0.0.0/0", NextHop = "10.0.10.1", IfIndex = eth.IfIndex });
            DnsServers[eth.IfIndex] = new List<string> { "10.0.10.10" };
            foreach (var p in new[] { "Domain", "Private", "Public" }) FwProfiles.Add(new FwProfile { Name = p });
            SeedFirewall();
        }

        void SeedFirewall()
        {
            bool s = IsServer;
            Fw("FPS-ICMP4-ERQ-In", "File and Printer Sharing (Echo Request - ICMPv4-In)", "Echo Request messages are sent as ping requests to other nodes.", "File and Printer Sharing", "@FirewallAPI.dll,-28502", s, "Inbound", "ICMPv4", "Any", "Any").IcmpType = "8";
            Fw("FPS-ICMP4-ERQ-Out", "File and Printer Sharing (Echo Request - ICMPv4-Out)", "Echo Request messages are sent as ping requests to other nodes.", "File and Printer Sharing", "@FirewallAPI.dll,-28502", s, "Outbound", "ICMPv4", "Any", "Any").IcmpType = "8";
            Fw("FPS-SMB-In-TCP", "File and Printer Sharing (SMB-In)", "Inbound rule for File and Printer Sharing to allow Server Message Block transmission and reception via Named Pipes. [TCP 445]", "File and Printer Sharing", "@FirewallAPI.dll,-28502", s, "Inbound", "TCP", "445", "Any");
            Fw("FPS-NB_Session-In-TCP", "File and Printer Sharing (NB-Session-In)", "Inbound rule for File and Printer Sharing to allow NetBIOS Session Service connections. [TCP 139]", "File and Printer Sharing", "@FirewallAPI.dll,-28502", s, "Inbound", "TCP", "139", "Any");
            Fw("WINRM-HTTP-In-TCP", "Windows Remote Management (HTTP-In)", "Inbound rule for Windows Remote Management via WS-Management. [TCP 5985]", "Windows Remote Management", "@FirewallAPI.dll,-30267", s, "Inbound", "TCP", "5985", "Any").Profile = "Domain, Private";
            var pub = Fw("WINRM-HTTP-In-TCP-PUBLIC", "Windows Remote Management (HTTP-In)", "Inbound rule for Windows Remote Management via WS-Management. [TCP 5985]", "Windows Remote Management", "@FirewallAPI.dll,-30267", s, "Inbound", "TCP", "5985", "LocalSubnet");
            pub.Profile = "Public";
            Fw("RemoteDesktop-UserMode-In-TCP", "Remote Desktop - User Mode (TCP-In)", "Inbound rule for the Remote Desktop service to allow RDP traffic. [TCP 3389]", "Remote Desktop", "@FirewallAPI.dll,-28752", s, "Inbound", "TCP", "3389", "Any");
            Fw("RemoteDesktop-UserMode-In-UDP", "Remote Desktop - User Mode (UDP-In)", "Inbound rule for the Remote Desktop service to allow RDP traffic. [UDP 3389]", "Remote Desktop", "@FirewallAPI.dll,-28752", s, "Inbound", "UDP", "3389", "Any");
            Fw("WMI-RPCSS-In-TCP", "Windows Management Instrumentation (DCOM-In)", "Inbound rule to allow DCOM traffic for remote Windows Management Instrumentation. [TCP 135]", "Windows Management Instrumentation (WMI)", "@FirewallAPI.dll,-34251", s, "Inbound", "TCP", "135", "Any");
            Fw("WMI-WINMGMT-In-TCP", "Windows Management Instrumentation (WMI-In)", "Inbound rule to allow WMI traffic for remote Windows Management Instrumentation. [TCP]", "Windows Management Instrumentation (WMI)", "@FirewallAPI.dll,-34251", s, "Inbound", "TCP", "RPC", "Any");
            Fw("RemoteEventLogSvc-In-TCP", "Remote Event Log Management (RPC)", "Inbound rule for the local Event Log service to be remotely managed via RPC/TCP.", "Remote Event Log Management", "@FirewallAPI.dll,-29252", false, "Inbound", "TCP", "RPC", "Any");
            Fw("CoreNet-DHCP-In", "Core Networking - Dynamic Host Configuration Protocol (DHCP-In)", "Allows DHCP (Dynamic Host Configuration Protocol) messages for stateful auto-configuration.", "Core Networking", "@FirewallAPI.dll,-25000", true, "Inbound", "UDP", "68", "Any");
            Fw("CoreNet-DNS-Out-UDP", "Core Networking - DNS (UDP-Out)", "Outbound rule to allow DNS requests.", "Core Networking", "@FirewallAPI.dll,-25000", true, "Outbound", "UDP", "Any", "Any").RemotePort = "53";
            Fw("CoreNet-ICMP6-ERQ-In", "Core Networking Diagnostics - ICMP Echo Request (ICMPv6-In)", "ICMP Echo Request messages are sent as ping requests to other nodes.", "Core Networking Diagnostics", "@FirewallAPI.dll,-27000", false, "Inbound", "ICMPv6", "Any", "Any").IcmpType = "128";
        }
        FwRule Fw(string name, string display, string desc, string group, string groupRes, bool enabled, string dir, string proto, string port, string remote)
        {
            var r = new FwRule { Name = name, DisplayName = display, Description = desc, DisplayGroup = group, Group = groupRes, Enabled = enabled, Direction = dir, Protocol = proto, LocalPort = port, RemoteAddress = remote, Builtin = true };
            FwRules.Add(r);
            return r;
        }

        public NetAdapterInfo Eth { get { return Adapters.FirstOrDefault(a => !a.Loopback); } }
        public NetAdapterInfo AdapterByIndex(int i) { return Adapters.FirstOrDefault(a => a.IfIndex == i); }
        public NetAdapterInfo AdapterByAlias(string n) { return Adapters.FirstOrDefault(a => a.Name.Equals(n, StringComparison.OrdinalIgnoreCase)); }
        public List<string> DnsFor(int ifIndex) { List<string> l; if (!DnsServers.TryGetValue(ifIndex, out l)) { l = new List<string>(); DnsServers[ifIndex] = l; } return l; }

        /// <summary>The IPv4 addresses on adapters that are up (not loopback).</summary>
        public IEnumerable<NetIpAddr> LanV4 { get { return IpAddrs.Where(a => !a.V6 && a.IfIndex != 1 && AdapterByIndex(a.IfIndex) != null && AdapterByIndex(a.IfIndex).IsUp); } }
        public bool LocalSubnetHas(string ip) { return LanV4.Any(a => Ip4.SameNet(a.Address, ip, a.Prefix)); }
        public string DefaultGateway(int ifIndex)
        {
            var r = Routes.FirstOrDefault(x => !x.V6 && x.Dest == "0.0.0.0/0" && x.IfIndex == ifIndex);
            return r == null ? null : r.NextHop;
        }

        /// <summary>After an address change: M.Ip (the first LAN address), ExtraIps, the lab host entry and the DNS A record follow.</summary>
        public void SyncAddresses()
        {
            var v4 = IpAddrs.Where(a => !a.V6 && a.IfIndex != 1).Select(a => a.Address).ToList();
            string old = Ip;
            Ip = v4.Count > 0 ? v4[0] : "";
            ExtraIps.Clear();
            ExtraIps.AddRange(v4.Skip(1));
            var h = Net.Hosts.FirstOrDefault(x => x.Machine == this);
            if (h != null) h.Ip = Ip;
            for (int i = 0; i < Net.DnsA.Count; i++)
                if (Net.DnsA[i].Key.Equals(Fqdn, StringComparison.OrdinalIgnoreCase)) Net.DnsA[i] = new KeyValuePair<string, string>(Net.DnsA[i].Key, Ip);
        }

        /// <summary>How this machine sends to an address: the source address and interface, or null when there is no route.</summary>
        public NetIpAddr RouteTo(string ip, out string nextHop)
        {
            nextHop = null;
            if (ip.StartsWith("127.", StringComparison.Ordinal)) return IpAddrs.FirstOrDefault(a => a.Address == "127.0.0.1");
            var own = IpAddrs.FirstOrDefault(a => !a.V6 && a.Address == ip);
            if (own != null) return own;
            var direct = LanV4.FirstOrDefault(a => Ip4.SameNet(a.Address, ip, a.Prefix));
            if (direct != null) { nextHop = ip; return direct; }
            // the most specific route by hand, then the default gateway
            foreach (var r in Routes.Where(x => !x.V6).OrderByDescending(x => PrefixOf(x.Dest)))
            {
                var parts = r.Dest.Split('/');
                if (!Ip4.SameNet(ip, parts[0], PrefixOf(r.Dest))) continue;
                var src = LanV4.FirstOrDefault(a => a.IfIndex == r.IfIndex && Ip4.SameNet(a.Address, r.NextHop, a.Prefix));
                if (src != null) { nextHop = r.NextHop; return src; }
            }
            return null;
        }
        static int PrefixOf(string dest) { int i = dest.IndexOf('/'); int p; return i > 0 && int.TryParse(dest.Substring(i + 1), out p) ? p : 32; }

        /// <summary>Does a packet from this machine to the address arrive, and can the answer come back?</summary>
        public bool Delivers(string ip)
        {
            if (Ws != null) return Ws.Delivers(this, ip);
            string hop;
            var src = RouteTo(ip, out hop);
            if (src == null) return false;
            if (src.Address == ip || ip.StartsWith("127.", StringComparison.Ordinal)) return true;
            // the lab network is 10.0.10.0/24 behind the router 10.0.10.1; nothing else exists (no internet)
            if (hop != ip && hop != "10.0.10.1") return false;
            if (!Ip4.SameNet(ip, "10.0.10.0", 24) || !Ip4.SameNet(src.Address, "10.0.10.0", 24)) return false;
            return ip == "10.0.10.1" || Net.HostByIp(ip) != null;
        }

        /// <summary>Does this machine's firewall let traffic in from an address (protocol TCP, UDP or ICMPv4; port for TCP/UDP)?</summary>
        public bool FirewallAllows(string protocol, int port, string fromIp)
        {
            var prof = FwProfiles.FirstOrDefault(p => p.Name == ActiveProfile);
            if (prof == null || !prof.Enabled) return true;
            Func<FwRule, bool> match = r => r.Enabled && r.Direction == "Inbound" && ProfileMatches(r.Profile) && ProtoMatches(r, protocol) && PortMatches(r, protocol, port) && Ip4.InScope(fromIp, r.RemoteAddress, this);
            if (FwRules.Any(r => r.Action == "Block" && match(r))) return false;
            if (FwRules.Any(r => r.Action == "Allow" && match(r))) return true;
            return prof.DefaultInboundAction == "Allow";
        }
        bool ProfileMatches(string p) { return p == "Any" || p.Split(',').Any(x => x.Trim().Equals(ActiveProfile, StringComparison.OrdinalIgnoreCase)); }
        static bool ProtoMatches(FwRule r, string proto)
        {
            if (r.Protocol == "Any") return true;
            if (r.Protocol.Equals(proto, StringComparison.OrdinalIgnoreCase)) return true;
            return (r.Protocol == "6" && proto == "TCP") || (r.Protocol == "17" && proto == "UDP") || (r.Protocol == "1" && proto == "ICMPv4");
        }
        static bool PortMatches(FwRule r, string proto, int port)
        {
            if (proto == "ICMPv4") return r.IcmpType == "Any" || r.IcmpType.Split(',').Any(x => x.Trim() == "8" || x.Trim().StartsWith("8:", StringComparison.Ordinal));
            if (r.LocalPort == "Any") return true;
            foreach (var raw in r.LocalPort.Split(','))
            {
                var p = raw.Trim();
                int dash = p.IndexOf('-');
                int a, b;
                if (dash > 0 && int.TryParse(p.Substring(0, dash), out a) && int.TryParse(p.Substring(dash + 1), out b)) { if (port >= a && port <= b) return true; continue; }
                if (int.TryParse(p, out a) && a == port) return true;
            }
            return false;
        }

        /// <summary>The TCP ports something listens on here.</summary>
        public List<int> ListeningPorts()
        {
            var h = Net.Hosts.FirstOrDefault(x => x.Machine == this);
            var l = h == null ? new List<int>() : h.Ports.ToList();
            if (WinRmListening && !l.Contains(5985)) l.Add(5985);
            if (!WinRmListening) l.Remove(5985);
            if (WinRmListening && !l.Contains(47001)) l.Add(47001);
            l.Sort();
            return l;
        }

        /// <summary>The lines of the hosts file that map names: name -> address.</summary>
        public List<KeyValuePair<string, string>> HostsFile()
        {
            var l = new List<KeyValuePair<string, string>>();
            var f = FindFile("C:\\Windows\\System32\\drivers\\etc\\hosts");
            if (f == null) return l;
            foreach (var raw in f.Text.Replace("\r", "").Split('\n'))
            {
                var line = raw; int hash = line.IndexOf('#'); if (hash >= 0) line = line.Substring(0, hash);
                var w = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (w.Length < 2) continue;
                for (int i = 1; i < w.Length; i++) l.Add(new KeyValuePair<string, string>(w[i], w[0]));
            }
            return l;
        }

        /// <summary>The DNS server this machine asks (the first one set on a LAN adapter), or null.</summary>
        public string DnsServer()
        {
            foreach (var a in Adapters.Where(x => !x.Loopback && x.IsUp)) { var l = DnsFor(a.IfIndex); if (l.Count > 0) return l[0]; }
            return null;
        }

        /// <summary>
        /// Resolves a name the way the Windows DNS client does: the hosts file, localhost and the computer's own name,
        /// then the DNS server (short names get the suffix lab.example.com). error: null, "NXDOMAIN", "TIMEOUT", "NOSERVER".
        /// </summary>
        public List<DnsAnswer> Resolve(string name, string type, out string error, out string fqdn)
        {
            error = null;
            var res = new List<DnsAnswer>();
            string n = (name ?? "").Trim().TrimEnd('.');
            fqdn = n;
            type = (type ?? "A_AAAA").ToUpperInvariant();
            if (Ip4.IsV4(n))
            {
                if (type == "A" || type == "A_AAAA") { res.Add(new DnsAnswer { Name = n, Data = n }); return res; }
            }
            bool addrQuery = type == "A" || type == "A_AAAA" || type == "AAAA";
            if (addrQuery)
            {
                foreach (var h in HostsFile()) if (h.Key.Equals(n, StringComparison.OrdinalIgnoreCase)) { if (Ip4.IsV4(h.Value) && type != "AAAA") res.Add(new DnsAnswer { Name = n, Data = h.Value, Ttl = 86400 }); }
                if (res.Count > 0) return res;
                if (n.Equals("localhost", StringComparison.OrdinalIgnoreCase)) { if (type != "A") res.Add(new DnsAnswer { Name = "localhost", Type = "AAAA", Data = "::1", Ttl = 0 }); if (type != "AAAA") res.Add(new DnsAnswer { Name = "localhost", Data = "127.0.0.1", Ttl = 0 }); return res; }
                if (n.Equals(Name, StringComparison.OrdinalIgnoreCase))
                {
                    fqdn = Name;
                    if (type != "A") res.Add(new DnsAnswer { Name = Name, Type = "AAAA", Data = LinkLocal + "%" + (Eth == null ? 1 : Eth.IfIndex), Ttl = 1200 });
                    if (type != "AAAA") foreach (var a in LanV4) res.Add(new DnsAnswer { Name = Name, Data = a.Address, Ttl = 1200 });
                    return res;
                }
            }
            if (Ws != null) return Ws.ClientResolve(this, n, type, null, out error, out fqdn);   // the AZ-800 network has real DNS servers (WsDnsResolve.cs)
            string server = DnsServer();
            if (server == null) { error = "NOSERVER"; return res; }
            if (!Delivers(server) || Net.DnsServerAt(server) == null) { error = "TIMEOUT"; return res; }
            DnsQueries++;
            if (Ip4.IsV4(n) || n.EndsWith(".in-addr.arpa", StringComparison.OrdinalIgnoreCase))
            {
                string ip = n;
                if (!Ip4.IsV4(n)) { var p = n.Substring(0, n.Length - 13).Split('.'); Array.Reverse(p); ip = string.Join(".", p); }
                var parts = ip.Split('.');
                fqdn = parts[3] + "." + parts[2] + "." + parts[1] + "." + parts[0] + ".in-addr.arpa";
                foreach (var a in Net.DnsA) if (a.Value == ip && !a.Key.Equals(Domain, StringComparison.OrdinalIgnoreCase)) { res.Add(new DnsAnswer { Name = fqdn, Type = "PTR", Data = a.Key, Ttl = 1200 }); break; }
                if (res.Count == 0) error = "NXDOMAIN";
                return res;
            }
            string full = n.Contains(".") ? n : n + "." + Domain;
            if (!full.Equals(Domain, StringComparison.OrdinalIgnoreCase) && !full.EndsWith("." + Domain, StringComparison.OrdinalIgnoreCase)) { fqdn = full; error = "NXDOMAIN"; return res; }
            fqdn = full;
            string cur = full;
            for (int hop = 0; hop < 8; hop++)
            {
                var cn = Net.DnsCname.FirstOrDefault(c => c.Key.Equals(cur, StringComparison.OrdinalIgnoreCase));
                if (cn.Key == null) break;
                res.Add(new DnsAnswer { Name = cur, Type = "CNAME", Data = cn.Value, Ttl = 3600 });
                if (type == "CNAME") return res;
                cur = cn.Value;
            }
            var a4 = Net.DnsA.Where(a => a.Key.Equals(cur, StringComparison.OrdinalIgnoreCase)).ToList();
            bool zone = cur.Equals(Domain, StringComparison.OrdinalIgnoreCase);
            switch (type)
            {
                case "A": case "A_AAAA": case "ALL": case "ANY":
                    foreach (var a in a4) res.Add(new DnsAnswer { Name = a.Key.ToLowerInvariant(), Data = a.Value, Ttl = zone ? 600 : 1200 });
                    if (type == "ALL" || type == "ANY") AddZoneRecords(res, cur, null);
                    break;
                case "MX": case "NS": case "SOA": case "TXT": case "SRV":
                    AddZoneRecords(res, cur, type);
                    break;
                case "AAAA":
                    break;
                case "CNAME":
                    break;
                default:
                    break;
            }
            if (a4.Count == 0 && !zone && !res.Any(r => r.Type == "CNAME")) { error = "NXDOMAIN"; return res; }
            if (res.All(r => r.Type == "CNAME"))
            {
                // the name exists, but not with this type: the zone's SOA in the Authority section
                res.Add(new DnsAnswer { Name = Domain, Type = "SOA", Section = "Authority", Data = "dc01." + Domain + "|hostmaster." + Domain + "|42", Ttl = 3600 });
            }
            return res;
        }
        void AddZoneRecords(List<DnsAnswer> res, string name, string type)
        {
            if (!name.Equals(Domain, StringComparison.OrdinalIgnoreCase)) return;
            if (type == null || type == "NS") res.Add(new DnsAnswer { Name = Domain, Type = "NS", Data = "dc01." + Domain, Ttl = 3600 });
            if (type == null || type == "SOA") res.Add(new DnsAnswer { Name = Domain, Type = "SOA", Data = "dc01." + Domain + "|hostmaster." + Domain + "|42", Ttl = 3600 });
        }
    }

    public partial class PsRunspace
    {
        // the hooks the networking, module, security and DSC parts set (DNS, module import, execution policy,
        // Configuration calls) must be in place before the first line runs, not only after the first cmdlet lookup
        partial void InitNet() { PsCmdlets.Init(); }
    }

    public partial class PsNet
    {
        /// <summary>The lab host or console machine that has this address.</summary>
        public LabHost HostByIp(string ip)
        {
            foreach (var m in Machines) if (m.IpAddrs.Any(a => !a.V6 && a.Address == ip && a.IfIndex != 1)) return Hosts.FirstOrDefault(h => h.Machine == m);
            return Hosts.FirstOrDefault(h => h.Machine == null && h.Ip == ip);
        }
        /// <summary>A DNS server at this address (DC01), or null.</summary>
        public LabHost DnsServerAt(string ip)
        {
            var h = HostByIp(ip);
            return h != null && h.Machine == null && h.Ports.Contains(53) ? h : null;
        }
    }

    /// <summary>What a test from one machine to another finds: ping, TCP port, the addresses used.</summary>
    public class NetProbe
    {
        public string Target = "", Address, Source, Alias, Error, NextHop;
        public bool Resolved, Ping, Tcp;
        public int Rtt;

        public static NetProbe Run(PsMachine m, string target, int port)
        {
            var p = new NetProbe { Target = target };
            string err, fq;
            if (Ip4.IsV4(target)) p.Address = target;
            else
            {
                var ans = m.Resolve(target, "A", out err, out fq);
                var a = ans.FirstOrDefault(x => x.Type == "A");
                if (a == null) { p.Error = err ?? "NXDOMAIN"; return p; }
                p.Address = a.Data;
            }
            p.Resolved = true;
            string hop;
            var src = m.RouteTo(p.Address, out hop);
            p.NextHop = hop;
            if (src == null) { p.Error = "NOROUTE"; return p; }
            p.Source = src.Address;
            var ad = m.AdapterByIndex(src.IfIndex);
            p.Alias = ad == null ? "" : ad.Name;
            bool self = src.Address == p.Address || p.Address.StartsWith("127.", StringComparison.Ordinal);
            if (!m.Delivers(p.Address)) return p;
            var host = self ? m.Net.Hosts.FirstOrDefault(h => h.Machine == m) : m.Net.HostByIp(p.Address);
            if (self)
            {
                p.Ping = true;
                p.Tcp = port > 0 && m.ListeningPorts().Contains(port);
                return p;
            }
            p.Rtt = 0;
            if (host == null && m.Ws != null) { p.Ping = true; p.Tcp = port > 0 && m.Ws.OutsidePortOpen(p.Address, port); return p; }
            if (host == null) { p.Ping = p.Address == "10.0.10.1"; return p; }
            if (host.Machine != null)
            {
                var t = host.Machine;
                p.Ping = t.FirewallAllows("ICMPv4", 0, src.Address);
                p.Tcp = port > 0 && t.ListeningPorts().Contains(port) && t.FirewallAllows("TCP", port, src.Address);
            }
            else
            {
                p.Ping = host.Pingable;
                p.Tcp = port > 0 && host.Ports.Contains(port);
            }
            return p;
        }
    }
}
