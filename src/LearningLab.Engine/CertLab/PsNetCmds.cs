// Cert Lab - the networking cmdlets of the simulated Windows PowerShell 5.1: NetAdapter, NetTCPIP (addresses,
// interfaces, routes, Test-NetConnection, TCP connections), DnsClient (Resolve-DnsName, DNS server settings),
// NetSecurity (firewall rules and profiles), NetConnection and Test-Connection, plus [System.Net.Dns]. Their output
// and errors follow Windows PowerShell 5.1 (most of these are CIM-based commands: "No MSFT_... objects found").
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
        public const string NetTcpip = "NetTCPIP", NetAdapterMod = "NetAdapter", DnsClientMod = "DnsClient", NetSec = "NetSecurity", NetConn = "NetConnection";
        const string CimInst = "Microsoft.Management.Infrastructure.CimInstance";

        static PsObj CimObj(string cls, string ns)
        {
            return new PsObj(CimInst + "#" + ns + "/" + cls, CimInst + "#" + cls, CimInst, "System.Object");
        }
        static PsObj Variant(PsObj o, string tag) { o.TypeNames.Insert(1, o.TypeNames[0] + "#" + tag); return o; }
        /// <summary>Windows keeps a firewall address prefix as a mask: 10.0.10.0/24 is shown as 10.0.10.0/255.255.255.0.</summary>
        static string FwAddr(string a)
        {
            a = (a ?? "").Trim(); int s = a.IndexOf('/'); int bits;
            if (s < 0 || !int.TryParse(a.Substring(s + 1), out bits) || bits < 0 || bits > 32 || a.Contains(":")) return a;
            uint m = bits == 0 ? 0u : 0xFFFFFFFFu << (32 - bits);
            return a.Substring(0, s) + "/" + (m >> 24) + "." + ((m >> 16) & 255) + "." + ((m >> 8) & 255) + "." + (m & 255);
        }
        static string Std(string cls) { return CimInst + "#ROOT/StandardCimv2/" + cls; }
        static void ListView(string type, params FmtCol[] cols) { PsFormat.View(type, new FmtView { Kind = "list", Cols = cols.ToList() }); }
        static object Gv(object o, string p) { var po = PsConv.Base(o) as PsObj; return po == null ? null : po.Get(p); }

        static bool MatchAny(string[] pats, string v)
        {
            if (pats == null) return true;
            return pats.Any(p => PsConv.Like(v ?? "", p, false));
        }

        /// <summary>The error CIM-based commands write when a filter by one property finds nothing.</summary>
        static PsErrorRecord CimNotFound(CmdCtx c, string cls, string prop, string val)
        {
            var r = c.MakeError("No " + cls + " objects found with property '" + prop + "' equal to '" + val + "'.  Verify the value of the property and retry.", "CmdletizationQuery_NotFound_" + prop, "ObjectNotFound", val, "Microsoft.PowerShell.Cmdletization.Cim.CimJobException");
            r.Reason = "CimJobException";
            return r;
        }
        static PsErrorRecord CimQueryNotFound(CmdCtx c, string cls, string where)
        {
            var r = c.MakeError("No matching " + cls + " objects found by CIM query for instances of the ROOT/StandardCimv2/" + cls + " class on the  CIM server: SELECT * FROM " + cls + "  WHERE (" + where + "). Verify query parameters and retry.", "CmdletizationQuery_NotFound", "ObjectNotFound", cls, "Microsoft.PowerShell.Cmdletization.Cim.CimJobException");
            r.Reason = "CimJobException";
            return r;
        }
        static PsErrorRecord CimError(CmdCtx c, string cls, string ns, string msg, string id, string cat)
        {
            var r = c.MakeError(msg, id, cat, null, "Microsoft.Management.Infrastructure.CimException");
            r.TargetName = cls; r.TargetType = ns + "/" + cls; r.Reason = "CimException";
            return r;
        }
        static CmdletDef Cdxml(CmdletDef d) { d.FqClass = d.Name; return d; }

        // ------------------------------------------------------------ objects
        static PsObj AdapterObj(PsMachine m, NetAdapterInfo a)
        {
            var o = CimObj("MSFT_NetAdapter", "ROOT/StandardCimv2");
            o.Val("Name", "string", a.Name);
            o.Val("InterfaceDescription", "string", a.Description);
            o.Val("ifIndex", "uint32", a.IfIndex);
            o.Val("Status", "string", a.Status);
            o.Val("MacAddress", "string", a.Mac);
            o.Val("LinkSpeed", "string", a.LinkSpeed);
            o.Val("MediaConnectionState", "string", a.IsUp ? "Connected" : "Disconnected");
            o.Val("ifAlias", "string", a.Name);
            o.Val("InterfaceAlias", "string", a.Name);
            o.Val("InterfaceIndex", "uint32", a.IfIndex);
            o.Val("InterfaceGuid", "string", a.Guid);
            o.Val("ifDesc", "string", a.Description);
            o.Val("AdminStatus", "string", a.Status == "Disabled" ? "Down" : "Up");
            o.Val("MediaType", "string", "802.3");
            o.Val("PhysicalMediaType", "string", "Unspecified");
            o.Val("MtuSize", "uint32", a.Mtu);
            o.Val("Virtual", "bool", true);
            o.Val("DriverDescription", "string", a.Description);
            o.Val("FullDuplex", "bool", true);
            o.Val("PSComputerName", "string", null);
            o.ToStr = x => a.Name;
            o.Base = a;
            return o;
        }

        static PsObj IpObj(PsMachine m, NetIpAddr a, string store)
        {
            var ad = m.AdapterByIndex(a.IfIndex);
            var o = CimObj("MSFT_NetIPAddress", "ROOT/StandardCimv2");
            o.Val("IPAddress", "string", a.Display);
            o.Val("InterfaceIndex", "uint32", a.IfIndex);
            o.Val("InterfaceAlias", "string", ad == null ? "" : ad.Name);
            o.Val("AddressFamily", "AddressFamily", PsEnums.V("AddressFamily", a.V6 ? "IPv6" : "IPv4"));
            o.Val("Type", "string", "Unicast");
            o.Val("PrefixLength", "byte", (byte)a.Prefix);
            o.Val("PrefixOrigin", "PrefixOrigin", PsEnums.V("PrefixOrigin", a.PrefixOrigin));
            o.Val("SuffixOrigin", "SuffixOrigin", PsEnums.V("SuffixOrigin", a.SuffixOrigin));
            o.Val("AddressState", "AddressState", PsEnums.V("AddressState", "Preferred"));
            o.Val("ValidLifetime", "timespan", TimeSpan.MaxValue);
            o.Val("PreferredLifetime", "timespan", TimeSpan.MaxValue);
            o.Val("SkipAsSource", "bool", a.SkipAsSource);
            o.Val("PolicyStore", "string", store);
            o.Val("IPv4Address", "string", a.V6 ? null : a.Address);
            o.Val("IPv6Address", "string", a.V6 ? a.Display : null);
            o.Val("ifIndex", "uint32", a.IfIndex);
            o.Val("Store", "Store", PsEnums.V("Store", store));
            o.Val("PSComputerName", "string", null);
            o.ToStr = x => a.Display;
            o.Base = a;
            return o;
        }
        static string Lifetime(object v) { return v is TimeSpan && (TimeSpan)v == TimeSpan.MaxValue ? "Infinite ([TimeSpan]::MaxValue)" : PsConv.ToStr(v); }

        static PsObj RouteObj(PsMachine m, NetRouteInfo r, string store)
        {
            var ad = m.AdapterByIndex(r.IfIndex);
            var o = CimObj("MSFT_NetRoute", "ROOT/StandardCimv2");
            o.Val("ifIndex", "uint32", r.IfIndex);
            o.Val("DestinationPrefix", "string", r.Dest);
            o.Val("NextHop", "string", r.NextHop);
            o.Val("RouteMetric", "uint16", r.Metric);
            o.Val("ifMetric", "uint32", ad == null ? 0 : ad.IfMetric);
            o.Val("InterfaceMetric", "uint32", ad == null ? 0 : ad.IfMetric);
            o.Val("PolicyStore", "string", store);
            o.Val("InterfaceAlias", "string", ad == null ? "" : ad.Name);
            o.Val("InterfaceIndex", "uint32", r.IfIndex);
            o.Val("AddressFamily", "AddressFamily", PsEnums.V("AddressFamily", r.V6 ? "IPv6" : "IPv4"));
            o.Val("Protocol", "string", "NetMgmt");
            o.Val("State", "string", "Alive");
            o.Val("TypeOfRoute", "string", r.NextHop == "0.0.0.0" || r.NextHop == "::" ? "Local" : "Remote");
            o.Val("Publish", "string", "No");
            o.Val("PSComputerName", "string", null);
            o.ToStr = x => r.Dest;
            o.Base = r;
            return o;
        }

        /// <summary>Every route of the machine: the ones Windows makes for each address, and the gateways and routes added by hand.</summary>
        static List<NetRouteInfo> AllRoutes(PsMachine m)
        {
            var l = new List<NetRouteInfo>();
            foreach (var ad in m.Adapters.Where(a => !a.Loopback && a.IsUp))
            {
                l.Add(new NetRouteInfo { Dest = "255.255.255.255/32", IfIndex = ad.IfIndex });
                l.Add(new NetRouteInfo { Dest = "224.0.0.0/4", IfIndex = ad.IfIndex });
                foreach (var a in m.IpAddrs.Where(x => !x.V6 && x.IfIndex == ad.IfIndex))
                {
                    l.Add(new NetRouteInfo { Dest = Ip4.Broadcast(a.Address, a.Prefix) + "/32", IfIndex = ad.IfIndex });
                    l.Add(new NetRouteInfo { Dest = a.Address + "/32", IfIndex = ad.IfIndex });
                    l.Add(new NetRouteInfo { Dest = Ip4.Network(a.Address, a.Prefix) + "/" + a.Prefix, IfIndex = ad.IfIndex });
                }
                foreach (var r in m.Routes.Where(x => !x.V6 && x.IfIndex == ad.IfIndex)) l.Add(r);
            }
            l.Add(new NetRouteInfo { Dest = "255.255.255.255/32", IfIndex = 1 });
            l.Add(new NetRouteInfo { Dest = "224.0.0.0/4", IfIndex = 1 });
            l.Add(new NetRouteInfo { Dest = "127.255.255.255/32", IfIndex = 1 });
            l.Add(new NetRouteInfo { Dest = "127.0.0.1/32", IfIndex = 1 });
            l.Add(new NetRouteInfo { Dest = "127.0.0.0/8", IfIndex = 1 });
            foreach (var ad in m.Adapters.Where(a => !a.Loopback && a.IsUp))
            {
                l.Add(new NetRouteInfo { Dest = "ff00::/8", NextHop = "::", IfIndex = ad.IfIndex, V6 = true });
                l.Add(new NetRouteInfo { Dest = m.LinkLocal + "/128", NextHop = "::", IfIndex = ad.IfIndex, V6 = true });
                l.Add(new NetRouteInfo { Dest = "fe80::/64", NextHop = "::", IfIndex = ad.IfIndex, V6 = true });
            }
            l.Add(new NetRouteInfo { Dest = "ff00::/8", NextHop = "::", IfIndex = 1, V6 = true });
            l.Add(new NetRouteInfo { Dest = "::1/128", NextHop = "::", IfIndex = 1, V6 = true });
            return l;
        }

        static PsObj FwRuleObj(FwRule r)
        {
            var o = new PsObj(CimInst + "#root/standardcimv2/MSFT_NetFirewallRule", CimInst + "#MSFT_NetFirewallRule", CimInst + "#MSFT_NetSARule", CimInst, "System.Object");
            o.Val("Name", "string", r.Name);
            o.Val("ID", "string", r.Name);
            o.Val("DisplayName", "string", r.DisplayName);
            o.Val("Description", "string", r.Description);
            o.Val("DisplayGroup", "string", r.DisplayGroup);
            o.Val("Group", "string", r.Group);
            o.Val("Enabled", "string", r.Enabled ? "True" : "False");
            o.Val("Profile", "string", r.Profile);
            o.Val("Platform", "string[]", new object[0]);
            o.Val("Direction", "string", r.Direction);
            o.Val("Action", "string", r.Action);
            o.Val("EdgeTraversalPolicy", "string", "Block");
            o.Val("LooseSourceMapping", "bool", false);
            o.Val("LocalOnlyMapping", "bool", false);
            o.Val("Owner", "string", null);
            o.Val("PrimaryStatus", "string", "OK");
            o.Val("Status", "string", "The rule was parsed successfully from the store. (65536)");
            o.Val("EnforcementStatus", "string", "NotApplicable");
            o.Val("PolicyStoreSource", "string", "PersistentStore");
            o.Val("PolicyStoreSourceType", "string", "Local");
            o.Val("PSComputerName", "string", null);
            o.ToStr = x => r.Name;
            o.Base = r;
            return o;
        }
        static readonly string[] FwRuleList = { "Name", "DisplayName", "Description", "DisplayGroup", "Group", "Enabled", "Profile", "Platform", "Direction", "Action", "EdgeTraversalPolicy", "LooseSourceMapping", "LocalOnlyMapping", "Owner", "PrimaryStatus", "Status", "EnforcementStatus", "PolicyStoreSource", "PolicyStoreSourceType" };

        static PsObj FwProfileObj(FwProfile p)
        {
            var o = new PsObj(CimInst + "#root/standardcimv2/MSFT_NetFirewallProfile", CimInst + "#MSFT_NetFirewallProfile", CimInst, "System.Object");
            o.Val("Name", "string", p.Name);
            o.Val("Enabled", "string", p.Enabled ? "True" : "False");
            o.Val("DefaultInboundAction", "string", p.DefaultInboundAction);
            o.Val("DefaultOutboundAction", "string", p.DefaultOutboundAction);
            o.Val("AllowInboundRules", "string", "NotConfigured");
            o.Val("AllowLocalFirewallRules", "string", "NotConfigured");
            o.Val("AllowLocalIPsecRules", "string", "NotConfigured");
            o.Val("AllowUserApps", "string", "NotConfigured");
            o.Val("AllowUserPorts", "string", "NotConfigured");
            o.Val("AllowUnicastResponseToMulticast", "string", "NotConfigured");
            o.Val("NotifyOnListen", "string", "False");
            o.Val("EnableStealthModeForIPsec", "string", "NotConfigured");
            o.Val("LogFileName", "string", "%systemroot%\\system32\\LogFiles\\Firewall\\pfirewall.log");
            o.Val("LogMaxSizeKilobytes", "uint32", 4096);
            o.Val("LogAllowed", "string", p.LogAllowed ? "True" : "False");
            o.Val("LogBlocked", "string", p.LogBlocked ? "True" : "False");
            o.Val("LogIgnored", "string", "NotConfigured");
            o.Val("DisabledInterfaceAliases", "string[]", new object[] { "NotConfigured" });
            o.Val("PSComputerName", "string", null);
            o.ToStr = x => p.Name;
            o.Base = p;
            return o;
        }

        static string NormProfile(string[] v)
        {
            if (v == null || v.Length == 0) return "Any";
            var parts = v.SelectMany(x => x.Split(',')).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            if (parts.Any(x => x.Equals("Any", StringComparison.OrdinalIgnoreCase))) return "Any";
            var order = new[] { "Domain", "Private", "Public" };
            var got = order.Where(o => parts.Any(x => x.Equals(o, StringComparison.OrdinalIgnoreCase))).ToList();
            return got.Count == 3 ? "Any" : string.Join(", ", got.ToArray());
        }
        static string NormProtocol(string p)
        {
            if (string.IsNullOrEmpty(p)) return "Any";
            switch (p.ToUpperInvariant())
            {
                case "TCP": case "6": return "TCP";
                case "UDP": case "17": return "UDP";
                case "ICMPV4": case "1": return "ICMPv4";
                case "ICMPV6": case "58": return "ICMPv6";
                case "ANY": return "Any";
            }
            return p;
        }

        /// <summary>The rules a Set-/Enable-/Disable-/Remove-NetFirewallRule call means (by -Name, -DisplayName, -DisplayGroup or piped objects).</summary>
        static List<FwRule> PickRules(CmdCtx c, object o)
        {
            var m = c.M;
            var res = new List<FwRule>();
            if (c.HasInput && o != null)
            {
                var po = PsConv.Base(o) as PsObj;
                var fr = po == null ? null : po.Base as FwRule;
                if (fr != null) { res.Add(fr); return res; }
            }
            if (c.Has("InputObject"))
            {
                foreach (var x in c.Objs("InputObject")) { var po = PsConv.Base(x) as PsObj; if (po != null && po.Base is FwRule) res.Add((FwRule)po.Base); }
                return res;
            }
            foreach (var key in new[] { "Name", "DisplayName", "DisplayGroup", "Group" })
            {
                if (!c.Has(key)) continue;
                foreach (var v in c.Strs(key))
                {
                    var hit = m.FwRules.Where(r => PsConv.Like(key == "Name" ? r.Name : key == "DisplayName" ? r.DisplayName : key == "DisplayGroup" ? r.DisplayGroup : r.Group, v, false)).ToList();
                    if (hit.Count == 0 && !PsConv.HasWildcard(v)) c.WriteError(CimNotFound(c, "MSFT_NetFirewallRule", key, v));
                    foreach (var h in hit) if (!res.Contains(h)) res.Add(h);
                }
                return res;
            }
            return res;
        }

        static void ApplyRuleSettings(CmdCtx c, FwRule r)
        {
            if (c.Has("Enabled")) r.Enabled = c.Str("Enabled") != "False";
            if (c.Has("Action")) r.Action = c.Str("Action");
            if (c.Has("Direction")) r.Direction = c.Str("Direction");
            if (c.Has("Profile")) r.Profile = NormProfile(c.Strs("Profile"));
            if (c.Has("Protocol")) r.Protocol = NormProtocol(c.Str("Protocol"));
            if (c.Has("LocalPort")) r.LocalPort = string.Join(",", c.Strs("LocalPort"));
            if (c.Has("RemotePort")) r.RemotePort = string.Join(",", c.Strs("RemotePort"));
            if (c.Has("RemoteAddress")) r.RemoteAddress = string.Join(",", c.Strs("RemoteAddress").Select(FwAddr).ToArray());
            if (c.Has("LocalAddress")) r.LocalAddress = string.Join(",", c.Strs("LocalAddress"));
            if (c.Has("Program")) r.Program = c.Str("Program");
            if (c.Has("Description")) r.Description = c.Str("Description");
            if (c.Has("IcmpType")) r.IcmpType = string.Join(",", c.Strs("IcmpType"));
            if (c.Has("NewDisplayName")) r.DisplayName = c.Str("NewDisplayName");
        }

        static PsObj DnsServerObj(PsMachine m, NetAdapterInfo ad, bool v6)
        {
            var o = CimObj("MSFT_DNSClientServerAddress", "ROOT/StandardCimv2");
            o.Val("InterfaceAlias", "string", ad.Name);
            o.Val("InterfaceIndex", "uint32", ad.IfIndex);
            o.Val("AddressFamily", "uint16", v6 ? 23 : 2);
            string[] servers = v6 ? (ad.Loopback ? new[] { "fec0:0:0:ffff::1", "fec0:0:0:ffff::2", "fec0:0:0:ffff::3" } : new string[0]) : (ad.Loopback ? new string[0] : m.DnsFor(ad.IfIndex).ToArray());
            o.Val("ServerAddresses", "string[]", servers.Cast<object>().ToArray());
            o.Val("ElementName", "string", ad.Name);
            o.Val("PSComputerName", "string", null);
            o.ToStr = x => ad.Name;
            o.Base = ad;
            return o;
        }

        static PsObj DnsRecordObj(DnsAnswer a, bool asList)
        {
            var o = new PsObj("Microsoft.DnsClient.Commands.DnsRecord_" + a.Type, "Microsoft.DnsClient.Commands.DnsRecord", "System.Object");
            o.TypeNames.Insert(1, "Microsoft.DnsClient.Commands.DnsRecord_" + a.Type + (asList ? "#List" : "#Table"));
            o.Val("Name", "string", a.Name);
            o.Val("Type", "Microsoft.DnsClient.Commands.RecordType", a.Type);
            o.Val("QueryType", "Microsoft.DnsClient.Commands.RecordType", a.Type);
            o.Val("CharacterSet", "string", "Unicode");
            o.Val("Section", "Microsoft.DnsClient.Commands.Section", a.Section);
            o.Val("DataLength", "uint16", a.Type == "A" ? 4 : a.Type == "AAAA" ? 16 : 8);
            o.Val("TTL", "uint32", a.Ttl);
            switch (a.Type)
            {
                case "A": o.Val("IP4Address", "string", a.Data); o.Val("IPAddress", "string", a.Data); o.Val("Address", "string", a.Data); break;
                case "AAAA": o.Val("IP6Address", "string", a.Data); o.Val("IPAddress", "string", a.Data); o.Val("Address", "string", a.Data); break;
                case "CNAME": case "PTR": case "NS": o.Val("NameHost", "string", a.Data); o.Val("Server", "string", a.Data); break;
                case "MX": { var q = a.Data.Split(new[] { (char)124 }, 2); o.Val("NameExchange", "string", q.Length > 1 ? q[1] : a.Data); o.Val("Preference", "uint16", q.Length > 1 ? (object)ushort.Parse(q[0], CultureInfo.InvariantCulture) : (object)(ushort)10); break; }
                case "SRV": { var q = a.Data.Split((char)124); if (q.Length == 4) { o.Val("NameTarget", "string", q[3]); o.Val("Priority", "uint16", ushort.Parse(q[0], CultureInfo.InvariantCulture)); o.Val("Weight", "uint16", ushort.Parse(q[1], CultureInfo.InvariantCulture)); o.Val("Port", "uint16", ushort.Parse(q[2], CultureInfo.InvariantCulture)); } break; }
                case "TXT": o.Val("Strings", "string[]", new object[] { a.Data }); break;
                case "SOA":
                    var p = a.Data.Split('|');
                    o.Val("PrimaryServer", "string", p[0]); o.Val("NameAdministrator", "string", p[1]); o.Val("SerialNumber", "uint32", long.Parse(p[2], CultureInfo.InvariantCulture));
                    o.Val("TimeToZoneRefresh", "uint32", 900); o.Val("TimeToZoneFailureRetry", "uint32", 600); o.Val("TimeToExpiration", "uint32", 86400); o.Val("DefaultTTL", "uint32", 3600);
                    break;
            }
            o.ToStr = x => a.Name;
            return o;
        }

        static PsObj PingStatusObj(PsMachine m, string target, NetProbe p, int bytes)
        {
            var o = new PsObj("System.Management.ManagementObject#root\\cimv2\\Win32_PingStatus", "System.Management.ManagementObject", "System.Management.ManagementBaseObject", "System.ComponentModel.Component", "System.MarshalByRefObject", "System.Object");
            o.Val("__SERVER", "string", m.Name);
            o.Val("PSComputerName", "string", m.Name);
            o.Val("Address", "string", target);
            o.Val("IPV4Address", "ipaddress", p.Address);
            o.Val("IPV6Address", "ipaddress", null);
            o.Val("BufferSize", "uint32", bytes);
            o.Val("ReplySize", "uint32", bytes);
            o.Val("ResponseTime", "uint32", p.Rtt);
            o.Val("ResponseTimeToLive", "uint32", 128);
            o.Val("StatusCode", "uint32", 0);
            o.Val("ProtocolAddress", "string", p.Address);
            o.Val("Timeout", "uint32", 4000);
            o.Val("TimeToLive", "uint32", 80);
            o.ToStr = x => "\\\\" + m.Name + "\\root\\cimv2:Win32_PingStatus.Address=\"" + target + "\"";
            return o;
        }

        static int CommonPort(string s)
        {
            switch ((s ?? "").ToUpperInvariant()) { case "HTTP": return 80; case "RDP": return 3389; case "SMB": return 445; case "WINRM": return 5985; }
            return 0;
        }

        static List<PsObj> TcpConnections(PsMachine m)
        {
            var l = new List<PsObj>();
            Func<string, int, string, int, string, int, PsObj> mk = (la, lp, ra, rp, st, pid) =>
            {
                var o = CimObj("MSFT_NetTCPConnection", "ROOT/StandardCimv2");
                o.Val("LocalAddress", "string", la); o.Val("LocalPort", "uint16", lp); o.Val("RemoteAddress", "string", ra); o.Val("RemotePort", "uint16", rp);
                o.Val("State", "string", st); o.Val("AppliedSetting", "string", st == "Listen" ? null : "Datacenter"); o.Val("OwningProcess", "uint32", pid);
                o.Val("CreationTime", "datetime", m.BootTime.AddSeconds(20)); o.Val("OffloadState", "string", "InHost"); o.Val("PSComputerName", "string", null);
                o.ToStr = x => la + ":" + lp;
                return o;
            };
            var ports = m.ListeningPorts();
            foreach (var p in ports) l.Add(mk("::", p, "::", 0, "Listen", PidFor(p)));
            if (m.DnsServer() != null && m.Delivers("10.0.10.10")) l.Add(mk(m.Ip, 49712, "10.0.10.10", 389, "Established", 712));
            foreach (var p in ports) l.Add(mk("0.0.0.0", p, "0.0.0.0", 0, "Listen", PidFor(p)));
            return l;
        }
        static int PidFor(int port) { switch (port) { case 135: return 968; case 3389: return 1236; case 445: case 139: case 5985: case 47001: return 4; } return 1424; }

        // ------------------------------------------------------------ registration
        static partial void RegisterNet()
        {
            PsEnums.Define("Microsoft.DnsClient.Commands.RecordType", "A=1,NS=2,CNAME=5,SOA=6,PTR=12,MX=15,TXT=16,AAAA=28,SRV=33,ALL=255");
            PsEnums.Define("Microsoft.DnsClient.Commands.Section", "Question=0,Answer=1,Authority=2,Additional=3");
            RegisterNetViews();
            PsNetHook.Dns = DnsStatic;

            Cdxml(Add("Get-NetAdapter", NetAdapterMod, "Name:string[]@0 alias=ifAlias,InterfaceAlias; InterfaceDescription:string[] alias=ifDesc; InterfaceIndex:int[] alias=ifIndex; Physical:switch; IncludeHidden:switch; CimSession:object; ThrottleLimit:int; AsJob:switch", (c, o) =>
            {
                var m = c.M;
                var list = m.Adapters.Where(a => !a.Loopback || c.Sw("IncludeHidden")).ToList();
                if (c.Has("Name"))
                {
                    foreach (var n in c.Strs("Name"))
                    {
                        var hit = list.Where(a => PsConv.Like(a.Name, n, false)).ToList();
                        if (hit.Count == 0 && !PsConv.HasWildcard(n)) c.WriteError(CimNotFound(c, "MSFT_NetAdapter", "Name", n));
                        foreach (var a in hit) c.Out(AdapterObj(m, a));
                    }
                    return;
                }
                if (c.Has("InterfaceIndex"))
                {
                    foreach (var i in c.Objs("InterfaceIndex").Select(x => PsConv.ToInt(x)))
                    {
                        var a = list.FirstOrDefault(x => x.IfIndex == i);
                        if (a == null) c.WriteError(CimNotFound(c, "MSFT_NetAdapter", "InterfaceIndex", i.ToString(CultureInfo.InvariantCulture))); else c.Out(AdapterObj(m, a));
                    }
                    return;
                }
                foreach (var a in list.Where(a => MatchAny(c.Strs("InterfaceDescription"), a.Description))) c.Out(AdapterObj(m, a));
            }));
            Cdxml(Add("Disable-NetAdapter", NetAdapterMod, "Name:string[]@0 alias=ifAlias; InputObject:object pipe; PassThru:switch", (c, o) =>
            {
                foreach (var a in PickAdapters(c, o))
                    if (c.ShouldProcess(a.Name, "Disable")) { a.Status = "Disabled"; if (c.Sw("PassThru")) c.Out(AdapterObj(c.M, a)); }
            })).ShouldProcess = true;
            Cdxml(Add("Enable-NetAdapter", NetAdapterMod, "Name:string[]@0 alias=ifAlias; InputObject:object pipe; PassThru:switch", (c, o) =>
            {
                foreach (var a in PickAdapters(c, o)) { a.Status = "Up"; if (c.Sw("PassThru")) c.Out(AdapterObj(c.M, a)); }
            }));
            Cdxml(Add("Restart-NetAdapter", NetAdapterMod, "Name:string[]@0 alias=ifAlias; InputObject:object pipe; PassThru:switch", (c, o) =>
            {
                foreach (var a in PickAdapters(c, o)) { a.Status = "Up"; if (c.Sw("PassThru")) c.Out(AdapterObj(c.M, a)); }
            }));
            Cdxml(Add("Rename-NetAdapter", NetAdapterMod, "Name:string[]@0 alias=ifAlias; NewName:string@1 !; InputObject:object pipe; PassThru:switch", (c, o) =>
            {
                foreach (var a in PickAdapters(c, o)) { a.Name = c.Str("NewName"); if (c.Sw("PassThru")) c.Out(AdapterObj(c.M, a)); }
            }));

            Cdxml(Add("Get-NetIPAddress", NetTcpip, "IPAddress:string[]@0; InterfaceIndex:int[] alias=ifIndex; InterfaceAlias:string[] alias=ifAlias; AddressFamily:string[] set=IPv4|IPv6; Type:string[]; PrefixLength:int[]; PrefixOrigin:string[]; SuffixOrigin:string[]; AddressState:string[]; SkipAsSource:bool; PolicyStore:string; IncludeAllCompartments:switch; CimSession:object", (c, o) =>
            {
                var m = c.M;
                string store = c.Str("PolicyStore", "ActiveStore");
                var l = m.IpAddrs.OrderBy(a => a.V6 ? 0 : 1).ThenBy(a => a.IfIndex == 1 ? 1 : 0).ToList();
                var where = new List<string>();
                if (c.Has("IPAddress")) { l = l.Where(a => MatchAny(c.Strs("IPAddress"), a.Display) || MatchAny(c.Strs("IPAddress"), a.Address)).ToList(); where.Add("(IPAddress LIKE '" + c.Strs("IPAddress")[0] + "')"); }
                if (c.Has("InterfaceIndex")) { var ix = c.Objs("InterfaceIndex").Select(x => PsConv.ToInt(x)).ToList(); l = l.Where(a => ix.Contains(a.IfIndex)).ToList(); where.Add("(InterfaceIndex = " + ix[0] + ")"); }
                if (c.Has("InterfaceAlias")) { l = l.Where(a => { var ad = m.AdapterByIndex(a.IfIndex); return ad != null && MatchAny(c.Strs("InterfaceAlias"), ad.Name); }).ToList(); where.Add("(InterfaceAlias LIKE '" + c.Strs("InterfaceAlias")[0] + "')"); }
                if (c.Has("AddressFamily")) { var f = c.Strs("AddressFamily"); l = l.Where(a => f.Any(x => x.Equals(a.V6 ? "IPv6" : "IPv4", StringComparison.OrdinalIgnoreCase))).ToList(); where.Add("(AddressFamily = " + (f[0].Equals("IPv6", StringComparison.OrdinalIgnoreCase) ? 23 : 2) + ")"); }
                if (c.Has("PrefixLength")) { var pl = c.Objs("PrefixLength").Select(x => PsConv.ToInt(x)).ToList(); l = l.Where(a => pl.Contains(a.Prefix)).ToList(); where.Add("(PrefixLength = " + pl[0] + ")"); }
                if (c.Has("PrefixOrigin")) l = l.Where(a => MatchAny(c.Strs("PrefixOrigin"), a.PrefixOrigin)).ToList();
                if (c.Has("SuffixOrigin")) l = l.Where(a => MatchAny(c.Strs("SuffixOrigin"), a.SuffixOrigin)).ToList();
                if (l.Count == 0 && where.Count > 0 && !(c.Has("IPAddress") && PsConv.HasWildcard(c.Strs("IPAddress")[0])))
                {
                    if (c.Has("IPAddress") && where.Count == 1) { c.WriteError(CimNotFound(c, "MSFT_NetIPAddress", "IPAddress", c.Strs("IPAddress")[0])); return; }
                    c.WriteError(CimQueryNotFound(c, "MSFT_NetIPAddress", string.Join(" AND ", where.ToArray())));
                    return;
                }
                foreach (var a in l) c.Out(IpObj(m, a, store));
            }));
            var nip = Cdxml(Add("New-NetIPAddress", NetTcpip, "IPAddress:string@0 !; InterfaceAlias:string alias=ifAlias; InterfaceIndex:int alias=ifIndex; PrefixLength:int alias=Length; DefaultGateway:string; AddressFamily:string set=IPv4|IPv6; Type:string; PolicyStore:string; SkipAsSource:bool; ValidLifetime:timespan; PreferredLifetime:timespan; CimSession:object", (c, o) =>
            {
                var m = c.M;
                string ip = c.Str("IPAddress").Trim();
                NetAdapterInfo ad = c.Has("InterfaceAlias") ? m.AdapterByAlias(c.Str("InterfaceAlias")) : c.Has("InterfaceIndex") ? m.AdapterByIndex(c.Int("InterfaceIndex", 0)) : null;
                if (ad == null && c.Has("InterfaceAlias")) { c.WriteError(CimNotFound(c, "MSFT_NetIPInterface", "InterfaceAlias", c.Str("InterfaceAlias"))); return; }
                if (ad == null && c.Has("InterfaceIndex")) { c.WriteError(CimNotFound(c, "MSFT_NetIPInterface", "InterfaceIndex", c.Str("InterfaceIndex"))); return; }
                bool v6 = ip.Contains(":");
                if (ad == null || (!v6 && !Ip4.IsV4(ip))) { c.WriteError(CimError(c, "MSFT_NetIPAddress", "ROOT/StandardCimv2", "The parameter is incorrect.", "Windows System Error 87", "InvalidArgument")); return; }
                int prefix = c.Int("PrefixLength", v6 ? 64 : 8);
                if (m.IpAddrs.Any(a => a.Address.Equals(ip, StringComparison.OrdinalIgnoreCase)))
                {
                    c.WriteError(CimError(c, "MSFT_NetIPAddress", "ROOT/StandardCimv2", "The object already exists.", "Windows System Error 5010", "ResourceExists"));
                    return;
                }
                if (!c.ShouldProcess("IPAddress: " + ip + " InterfaceIndex: " + ad.IfIndex, "Create")) return;
                var na = new NetIpAddr { Address = ip, IfIndex = ad.IfIndex, Prefix = prefix, V6 = v6 };
                if (ad.Dhcp) { ad.Dhcp = false; m.IpAddrs.RemoveAll(a => a.IfIndex == ad.IfIndex && a.PrefixOrigin == "Dhcp"); }
                int at = m.IpAddrs.FindLastIndex(a => a.IfIndex == ad.IfIndex && a.V6 == v6);
                m.IpAddrs.Insert(at < 0 ? m.IpAddrs.Count : at + 1, na);
                m.SyncAddresses();
                c.Out(IpObj(m, na, "ActiveStore"));
                c.Out(IpObj(m, na, "PersistentStore"));
                if (c.Has("DefaultGateway"))
                {
                    if (m.DefaultGateway(ad.IfIndex) != null) { c.WriteError(CimError(c, "MSFT_NetIPAddress", "ROOT/StandardCimv2", "Instance DefaultGateway already exists", "Windows System Error 87", "InvalidArgument")); return; }
                    m.Routes.Add(new NetRouteInfo { Dest = "0.0.0.0/0", NextHop = c.Str("DefaultGateway"), IfIndex = ad.IfIndex });
                }
            }));
            nip.ShouldProcess = true;
            var rip = Cdxml(Add("Remove-NetIPAddress", NetTcpip, "IPAddress:string[]@0; InterfaceAlias:string[] alias=ifAlias; InterfaceIndex:int[] alias=ifIndex; AddressFamily:string[]; PrefixLength:int[]; InputObject:object pipe; DefaultGateway:string; IncludeAllCompartments:switch; PolicyStore:string; PassThru:switch; CimSession:object", (c, o) =>
            {
                var m = c.M;
                var hits = new List<NetIpAddr>();
                if (c.HasInput && PsConv.Base(o) is PsObj && ((PsObj)PsConv.Base(o)).Base is NetIpAddr) hits.Add((NetIpAddr)((PsObj)PsConv.Base(o)).Base);
                else
                {
                    var l = m.IpAddrs.ToList();
                    if (c.Has("IPAddress")) l = l.Where(a => MatchAny(c.Strs("IPAddress"), a.Address) || MatchAny(c.Strs("IPAddress"), a.Display)).ToList();
                    if (c.Has("InterfaceIndex")) { var ix = c.Objs("InterfaceIndex").Select(x => PsConv.ToInt(x)).ToList(); l = l.Where(a => ix.Contains(a.IfIndex)).ToList(); }
                    if (c.Has("InterfaceAlias")) l = l.Where(a => { var ad = m.AdapterByIndex(a.IfIndex); return ad != null && MatchAny(c.Strs("InterfaceAlias"), ad.Name); }).ToList();
                    if (c.Has("AddressFamily")) l = l.Where(a => c.Strs("AddressFamily").Any(x => x.Equals(a.V6 ? "IPv6" : "IPv4", StringComparison.OrdinalIgnoreCase))).ToList();
                    if (!c.Has("IPAddress") && !c.Has("InterfaceIndex") && !c.Has("InterfaceAlias")) l.Clear();
                    if (l.Count == 0 && c.Has("IPAddress")) { c.WriteError(CimNotFound(c, "MSFT_NetIPAddress", "IPAddress", c.Strs("IPAddress")[0])); return; }
                    hits = l;
                }
                foreach (var a in hits)
                {
                    if (!c.ShouldProcess("IPAddress: " + a.Display + " InterfaceIndex: " + a.IfIndex, "Delete")) continue;
                    m.IpAddrs.Remove(a);
                    if (c.Has("DefaultGateway")) m.Routes.RemoveAll(r => r.Dest == "0.0.0.0/0" && r.NextHop == c.Str("DefaultGateway"));
                    m.SyncAddresses();
                    if (c.Sw("PassThru")) c.Out(IpObj(m, a, "ActiveStore"));
                }
            }));
            rip.ShouldProcess = true; rip.ConfirmImpact = "High";
            Cdxml(Add("Get-NetIPInterface", NetTcpip, "InterfaceAlias:string[]@0 alias=ifAlias; InterfaceIndex:int[] alias=ifIndex; AddressFamily:string[] set=IPv4|IPv6; Dhcp:string set=Enabled|Disabled; ConnectionState:string; PolicyStore:string; CimSession:object", (c, o) =>
            {
                var m = c.M;
                foreach (var v6 in new[] { true, false })
                    foreach (var ad in m.Adapters.OrderByDescending(a => a.IfIndex))
                    {
                        if (c.Has("AddressFamily") && !c.Strs("AddressFamily").Any(x => x.Equals(v6 ? "IPv6" : "IPv4", StringComparison.OrdinalIgnoreCase))) continue;
                        if (!MatchAny(c.Strs("InterfaceAlias"), ad.Name)) continue;
                        if (c.Has("InterfaceIndex") && !c.Objs("InterfaceIndex").Select(x => PsConv.ToInt(x)).Contains(ad.IfIndex)) continue;
                        string dhcp = ad.Loopback ? "Disabled" : v6 ? "Enabled" : ad.Dhcp ? "Enabled" : "Disabled";
                        if (c.Has("Dhcp") && !c.Str("Dhcp").Equals(dhcp, StringComparison.OrdinalIgnoreCase)) continue;
                        c.Out(IpInterfaceObj(ad, v6, dhcp));
                    }
            }));
            Cdxml(Add("Set-NetIPInterface", NetTcpip, "InterfaceAlias:string[]@0 alias=ifAlias; InterfaceIndex:int[] alias=ifIndex; AddressFamily:string[]; Dhcp:string set=Enabled|Disabled; InterfaceMetric:int; AutomaticMetric:string; NlMtuBytes:int; InputObject:object pipe; PassThru:switch; CimSession:object", (c, o) =>
            {
                var m = c.M;
                var ads = new List<NetAdapterInfo>();
                if (c.HasInput && PsConv.Base(o) is PsObj && ((PsObj)PsConv.Base(o)).Base is NetAdapterInfo) ads.Add((NetAdapterInfo)((PsObj)PsConv.Base(o)).Base);
                ads.AddRange(m.Adapters.Where(a => (c.Has("InterfaceAlias") && MatchAny(c.Strs("InterfaceAlias"), a.Name)) || (c.Has("InterfaceIndex") && c.Objs("InterfaceIndex").Select(x => PsConv.ToInt(x)).Contains(a.IfIndex))));
                if (ads.Count == 0 && c.Has("InterfaceAlias")) { c.WriteError(CimNotFound(c, "MSFT_NetIPInterface", "InterfaceAlias", c.Strs("InterfaceAlias")[0])); return; }
                foreach (var ad in ads.Distinct())
                {
                    if (c.Has("InterfaceMetric")) ad.IfMetric = c.Int("InterfaceMetric", ad.IfMetric);
                    if (c.Has("NlMtuBytes")) ad.Mtu = c.Int("NlMtuBytes", ad.Mtu);
                    if (c.Has("Dhcp") && !ad.Loopback)
                    {
                        bool on = c.Str("Dhcp").Equals("Enabled", StringComparison.OrdinalIgnoreCase);
                        if (on && !ad.Dhcp)
                        {
                            ad.Dhcp = true;
                            m.IpAddrs.RemoveAll(a => !a.V6 && a.IfIndex == ad.IfIndex);
                            int last = m.IsServer ? 111 : 121;
                            m.IpAddrs.Add(new NetIpAddr { Address = "10.0.10." + last, IfIndex = ad.IfIndex, Prefix = 24, PrefixOrigin = "Dhcp", SuffixOrigin = "Dhcp" });
                            m.Routes.RemoveAll(r => r.IfIndex == ad.IfIndex && r.Dest == "0.0.0.0/0");
                            m.Routes.Add(new NetRouteInfo { Dest = "0.0.0.0/0", NextHop = "10.0.10.1", IfIndex = ad.IfIndex });
                            m.SyncAddresses();
                        }
                        else if (!on && ad.Dhcp)
                        {
                            ad.Dhcp = false;
                            m.IpAddrs.RemoveAll(a => a.IfIndex == ad.IfIndex && a.PrefixOrigin == "Dhcp");
                            m.SyncAddresses();
                        }
                    }
                    if (c.Sw("PassThru")) c.Out(IpInterfaceObj(ad, false, ad.Dhcp ? "Enabled" : "Disabled"));
                }
            }));
            Cdxml(Add("Get-NetIPConfiguration", NetTcpip, "InterfaceAlias:string@0 alias=ifAlias; InterfaceIndex:int alias=ifIndex; All:switch; Detailed:switch; CimSession:object", (c, o) =>
            {
                var m = c.M;
                var ads = m.Adapters.Where(a => (!a.Loopback || c.Sw("All")) && (a.IsUp || c.Sw("All"))).ToList();
                if (c.Has("InterfaceAlias")) { ads = m.Adapters.Where(a => PsConv.Like(a.Name, c.Str("InterfaceAlias"), false)).ToList(); if (ads.Count == 0) { c.WriteError(CimNotFound(c, "MSFT_NetIPInterface", "InterfaceAlias", c.Str("InterfaceAlias"))); return; } }
                if (c.Has("InterfaceIndex")) { ads = m.Adapters.Where(a => a.IfIndex == c.Int("InterfaceIndex", -1)).ToList(); if (ads.Count == 0) { c.WriteError(CimNotFound(c, "MSFT_NetIPInterface", "InterfaceIndex", c.Str("InterfaceIndex"))); return; } }
                foreach (var ad in ads) c.Out(IpConfigObj(m, ad, c.Sw("Detailed")));
            }));
            Cdxml(Add("Get-NetRoute", NetTcpip, "DestinationPrefix:string[]@0; InterfaceAlias:string[] alias=ifAlias; InterfaceIndex:int[] alias=ifIndex; AddressFamily:string[] set=IPv4|IPv6; NextHop:string[]; RouteMetric:int[]; PolicyStore:string; CimSession:object", (c, o) =>
            {
                var m = c.M;
                var l = AllRoutes(m);
                if (c.Has("DestinationPrefix")) l = l.Where(r => MatchAny(c.Strs("DestinationPrefix"), r.Dest)).ToList();
                if (c.Has("InterfaceAlias")) l = l.Where(r => { var ad = m.AdapterByIndex(r.IfIndex); return ad != null && MatchAny(c.Strs("InterfaceAlias"), ad.Name); }).ToList();
                if (c.Has("InterfaceIndex")) { var ix = c.Objs("InterfaceIndex").Select(x => PsConv.ToInt(x)).ToList(); l = l.Where(r => ix.Contains(r.IfIndex)).ToList(); }
                if (c.Has("AddressFamily")) l = l.Where(r => c.Strs("AddressFamily").Any(x => x.Equals(r.V6 ? "IPv6" : "IPv4", StringComparison.OrdinalIgnoreCase))).ToList();
                if (c.Has("NextHop")) l = l.Where(r => MatchAny(c.Strs("NextHop"), r.NextHop)).ToList();
                if (l.Count == 0 && c.Has("DestinationPrefix") && !PsConv.HasWildcard(c.Strs("DestinationPrefix")[0])) { c.WriteError(CimNotFound(c, "MSFT_NetRoute", "DestinationPrefix", c.Strs("DestinationPrefix")[0])); return; }
                foreach (var r in l) c.Out(RouteObj(m, r, c.Str("PolicyStore", "ActiveStore")));
            }));
            var nr = Cdxml(Add("New-NetRoute", NetTcpip, "DestinationPrefix:string@0 !; InterfaceAlias:string alias=ifAlias; InterfaceIndex:int alias=ifIndex; NextHop:string; RouteMetric:int; PolicyStore:string; AddressFamily:string; CimSession:object", (c, o) =>
            {
                var m = c.M;
                NetAdapterInfo ad = c.Has("InterfaceAlias") ? m.AdapterByAlias(c.Str("InterfaceAlias")) : c.Has("InterfaceIndex") ? m.AdapterByIndex(c.Int("InterfaceIndex", 0)) : null;
                string dest = c.Str("DestinationPrefix");
                if (ad == null || !dest.Contains("/")) { c.WriteError(CimError(c, "MSFT_NetRoute", "ROOT/StandardCimv2", "The parameter is incorrect.", "Windows System Error 87", "InvalidArgument")); return; }
                string hop = c.Str("NextHop", "0.0.0.0");
                if (m.Routes.Any(r => r.Dest == dest && r.NextHop == hop && r.IfIndex == ad.IfIndex)) { c.WriteError(CimError(c, "MSFT_NetRoute", "ROOT/StandardCimv2", "The object already exists.", "Windows System Error 5010", "ResourceExists")); return; }
                if (!c.ShouldProcess("DestinationPrefix: " + dest + " NextHop: " + hop, "Create")) return;
                var r2 = new NetRouteInfo { Dest = dest, NextHop = hop, IfIndex = ad.IfIndex, Metric = c.Int("RouteMetric", 256), V6 = dest.Contains(":") };
                m.Routes.Add(r2);
                c.Out(RouteObj(m, r2, "ActiveStore"));
                c.Out(RouteObj(m, r2, "PersistentStore"));
            }));
            nr.ShouldProcess = true;
            var rr = Cdxml(Add("Remove-NetRoute", NetTcpip, "DestinationPrefix:string[]@0; InterfaceAlias:string[] alias=ifAlias; InterfaceIndex:int[] alias=ifIndex; NextHop:string[]; InputObject:object pipe; PolicyStore:string; PassThru:switch; CimSession:object", (c, o) =>
            {
                var m = c.M;
                var hits = new List<NetRouteInfo>();
                if (c.HasInput && PsConv.Base(o) is PsObj && ((PsObj)PsConv.Base(o)).Base is NetRouteInfo) hits.Add((NetRouteInfo)((PsObj)PsConv.Base(o)).Base);
                else hits = m.Routes.Where(r => (!c.Has("DestinationPrefix") || MatchAny(c.Strs("DestinationPrefix"), r.Dest)) && (!c.Has("NextHop") || MatchAny(c.Strs("NextHop"), r.NextHop)) && (!c.Has("InterfaceIndex") || c.Objs("InterfaceIndex").Select(x => PsConv.ToInt(x)).Contains(r.IfIndex)) && (!c.Has("InterfaceAlias") || MatchAny(c.Strs("InterfaceAlias"), m.AdapterByIndex(r.IfIndex) == null ? "" : m.AdapterByIndex(r.IfIndex).Name))).ToList();
                if (hits.Count == 0 && c.Has("DestinationPrefix")) { c.WriteError(CimNotFound(c, "MSFT_NetRoute", "DestinationPrefix", c.Strs("DestinationPrefix")[0])); return; }
                foreach (var r in hits)
                {
                    if (!c.ShouldProcess("DestinationPrefix: " + r.Dest + " NextHop: " + r.NextHop, "Delete")) continue;
                    m.Routes.Remove(r);
                    if (c.Sw("PassThru")) c.Out(RouteObj(m, r, "ActiveStore"));
                }
            }));
            rr.ShouldProcess = true; rr.ConfirmImpact = "High";

            Cdxml(Add("Test-NetConnection", NetTcpip, "ComputerName:string@0 alias=RemoteAddress,cn; Port:int alias=RemotePort; CommonTCPPort:string set=HTTP|RDP|SMB|WINRM; InformationLevel:string set=Quiet|Detailed; TraceRoute:switch; Hops:int; DiagnoseRouting:switch", (c, o) => TestNetConnection(c)));
            Cdxml(Add("Get-NetTCPConnection", NetTcpip, "LocalAddress:string[]@0; LocalPort:int[]@1; RemoteAddress:string[]; RemotePort:int[]; State:string[]; OwningProcess:int[]; AppliedSetting:string[]; CimSession:object", (c, o) =>
            {
                var l = TcpConnections(c.M);
                var where = new List<string>();
                if (c.Has("LocalAddress")) { l = l.Where(x => MatchAny(c.Strs("LocalAddress"), x.S("LocalAddress"))).ToList(); where.Add("(LocalAddress LIKE '" + c.Strs("LocalAddress")[0] + "')"); }
                if (c.Has("LocalPort")) { var p = c.Objs("LocalPort").Select(x => PsConv.ToInt(x)).ToList(); l = l.Where(x => p.Contains(PsConv.ToInt(x.Get("LocalPort")))).ToList(); where.Add("(LocalPort = " + p[0] + ")"); }
                if (c.Has("RemoteAddress")) { l = l.Where(x => MatchAny(c.Strs("RemoteAddress"), x.S("RemoteAddress"))).ToList(); where.Add("(RemoteAddress LIKE '" + c.Strs("RemoteAddress")[0] + "')"); }
                if (c.Has("RemotePort")) { var p = c.Objs("RemotePort").Select(x => PsConv.ToInt(x)).ToList(); l = l.Where(x => p.Contains(PsConv.ToInt(x.Get("RemotePort")))).ToList(); where.Add("(RemotePort = " + p[0] + ")"); }
                if (c.Has("State")) { l = l.Where(x => MatchAny(c.Strs("State"), x.S("State"))).ToList(); where.Add("(State = " + (c.Strs("State")[0].Equals("Listen", StringComparison.OrdinalIgnoreCase) ? 2 : 5) + ")"); }
                if (c.Has("OwningProcess")) { var p = c.Objs("OwningProcess").Select(x => PsConv.ToInt(x)).ToList(); l = l.Where(x => p.Contains(PsConv.ToInt(x.Get("OwningProcess")))).ToList(); where.Add("(OwningProcess = " + p[0] + ")"); }
                if (l.Count == 0 && where.Count > 0) { c.WriteError(CimQueryNotFound(c, "MSFT_NetTCPConnection", string.Join(" AND ", where.ToArray()))); return; }
                foreach (var x in l) c.Out(x);
            }));

            Add("Test-Connection", Mgmt, "ComputerName:string[]@0 ! alias=CN,IPAddress,__SERVER,Server,Destination; Count:int; Quiet:switch; BufferSize:int alias=Size,Bytes,BS; Source:string[] alias=FCN,SRC; Delay:int; TimeToLive:int alias=TTL; AsJob:switch; Credential:pscredential", (c, o) =>
            {
                var m = c.M;
                int count = c.Int("Count", 4), bytes = c.Int("BufferSize", 32);
                foreach (var t in c.Strs("ComputerName"))
                {
                    var p = NetProbe.Run(m, t, 0);
                    if (c.Sw("Quiet")) { c.Out(p.Ping); continue; }
                    if (!p.Ping)
                    {
                        string why = p.Resolved ? "Error due to lack of resources" : "No such host is known";
                        var r = c.MakeError("Testing connection to computer '" + t + "' failed: " + why, "TestConnectionException", "ResourceUnavailable", t, "System.Net.NetworkInformation.PingException");
                        r.Reason = "PingException";
                        c.WriteError(r);
                        continue;
                    }
                    for (int i = 0; i < count; i++) c.Out(PingStatusObj(m, t, p, bytes));
                }
            });

            Cdxml(Add("Resolve-DnsName", DnsClientMod, "Name:string@0 ! pipe prop; Type:string@1 set=A|AAAA|A_AAAA|CNAME|MX|NS|PTR|SOA|SRV|TXT|ALL|ANY; Server:string[]; DnsOnly:switch; NoHostsFile:switch; CacheOnly:switch; QuickTimeout:switch; TcpOnly:switch; NoRecursion:switch; NoIdn:switch", (c, o) =>
            {
                var m = c.M;
                string name = c.Str("Name");
                string type = c.Str("Type", Ip4.IsV4(name) ? "PTR" : "A_AAAA");
                string err, fq;
                List<DnsAnswer> ans;
                if (m.Ws != null) ans = m.Ws.ClientResolve(m, name, type, c.Has("Server") ? c.Strs("Server")[0] : null, out err, out fq);
                else if (c.Has("Server"))
                {
                    var srv = c.Strs("Server")[0];
                    var h = m.Net.FindHost(srv);
                    string sip = h != null ? h.Ip : srv;
                    if (m.Net.DnsServerAt(sip) == null || !m.Delivers(sip)) { ans = new List<DnsAnswer>(); err = "TIMEOUT"; fq = name; }
                    else
                    {
                        var keep = m.DnsFor(m.Eth.IfIndex).ToList();
                        var l = m.DnsFor(m.Eth.IfIndex); l.Clear(); l.Add(sip);
                        try { ans = m.Resolve(name, type, out err, out fq); } finally { l.Clear(); l.AddRange(keep); }
                    }
                }
                else ans = m.Resolve(name, type, out err, out fq);
                if (err != null)
                {
                    string msg = err == "NXDOMAIN" ? "DNS name does not exist" : err == "NOSERVER" ? "No DNS servers configured for local system" : "This operation returned because the timeout period expired";
                    string id = err == "NXDOMAIN" ? "DNS_ERROR_RCODE_NAME_ERROR" : err == "NOSERVER" ? "DNS_ERROR_NO_DNS_SERVERS" : "ERROR_TIMEOUT";
                    var r = c.MakeError(name + " : " + msg, id, err == "TIMEOUT" ? "OperationTimeout" : "ResourceUnavailable", name, "System.ComponentModel.Win32Exception");
                    r.Reason = "Win32Exception"; r.FullCommandName = "Microsoft.DnsClient.Commands.ResolveDnsName";
                    c.WriteError(r);
                    return;
                }
                bool chain = ans.Any(a => a.Type == "CNAME");
                foreach (var a in ans) c.Out(DnsRecordObj(a, chain && a.Type != "CNAME"));
            })).FqClass = "Microsoft.DnsClient.Commands.ResolveDnsName";
            Cdxml(Add("Clear-DnsClientCache", DnsClientMod, "CimSession:object; AsJob:switch", (c, o) => { c.M.DnsQueries = 0; }));
            Cdxml(Add("Get-DnsClientServerAddress", DnsClientMod, "InterfaceAlias:string[]@0 alias=ifAlias; InterfaceIndex:int[] alias=ifIndex; AddressFamily:string[] set=IPv4|IPv6; CimSession:object", (c, o) =>
            {
                var m = c.M;
                var ads = m.Adapters.Where(a => a.IsUp || !a.Loopback).ToList();
                if (c.Has("InterfaceAlias"))
                {
                    var hit = ads.Where(a => MatchAny(c.Strs("InterfaceAlias"), a.Name)).ToList();
                    if (hit.Count == 0 && !PsConv.HasWildcard(c.Strs("InterfaceAlias")[0])) { c.WriteError(CimNotFound(c, "MSFT_DNSClientServerAddress", "InterfaceAlias", c.Strs("InterfaceAlias")[0])); return; }
                    ads = hit;
                }
                if (c.Has("InterfaceIndex")) { var ix = c.Objs("InterfaceIndex").Select(x => PsConv.ToInt(x)).ToList(); ads = ads.Where(a => ix.Contains(a.IfIndex)).ToList(); }
                foreach (var ad in ads)
                    foreach (var v6 in new[] { false, true })
                    {
                        if (c.Has("AddressFamily") && !c.Strs("AddressFamily").Any(x => x.Equals(v6 ? "IPv6" : "IPv4", StringComparison.OrdinalIgnoreCase))) continue;
                        c.Out(DnsServerObj(m, ad, v6));
                    }
            }));
            Cdxml(Add("Set-DnsClientServerAddress", DnsClientMod, "InterfaceAlias:string[]@0 alias=ifAlias; InterfaceIndex:int[] alias=ifIndex; ServerAddresses:string[] alias=Addresses; ResetServerAddresses:switch; Validate:switch; InputObject:object pipe; PassThru:switch; CimSession:object", (c, o) =>
            {
                var m = c.M;
                var ads = new List<NetAdapterInfo>();
                if (c.HasInput && PsConv.Base(o) is PsObj && ((PsObj)PsConv.Base(o)).Base is NetAdapterInfo) ads.Add((NetAdapterInfo)((PsObj)PsConv.Base(o)).Base);
                if (c.Has("InterfaceAlias"))
                {
                    var hit = m.Adapters.Where(a => MatchAny(c.Strs("InterfaceAlias"), a.Name)).ToList();
                    if (hit.Count == 0) { c.WriteError(CimNotFound(c, "MSFT_DNSClientServerAddress", "InterfaceAlias", c.Strs("InterfaceAlias")[0])); return; }
                    ads.AddRange(hit);
                }
                if (c.Has("InterfaceIndex")) { var ix = c.Objs("InterfaceIndex").Select(x => PsConv.ToInt(x)).ToList(); ads.AddRange(m.Adapters.Where(a => ix.Contains(a.IfIndex))); }
                if (c.Has("ServerAddresses") && c.Strs("ServerAddresses").Any(s => !Ip4.IsV4(s) && !s.Contains(":")))
                {
                    c.WriteError(CimError(c, "MSFT_DNSClientServerAddress", "ROOT/StandardCimv2", "The parameter is incorrect.", "Windows System Error 87", "InvalidArgument"));
                    return;
                }
                foreach (var ad in ads.Distinct())
                {
                    var l = m.DnsFor(ad.IfIndex);
                    if (c.Sw("ResetServerAddresses")) { l.Clear(); if (ad.Dhcp) l.Add("10.0.10.10"); }
                    else if (c.Has("ServerAddresses")) { l.Clear(); l.AddRange(c.Strs("ServerAddresses").Where(Ip4.IsV4)); }
                    if (c.Sw("PassThru")) c.Out(DnsServerObj(m, ad, false));
                }
            }));
            Cdxml(Add("Get-DnsClient", DnsClientMod, "InterfaceAlias:string[]@0 alias=ifAlias; InterfaceIndex:int[] alias=ifIndex; ConnectionSpecificSuffix:string[]; CimSession:object", (c, o) =>
            {
                foreach (var ad in c.M.Adapters.Where(a => MatchAny(c.Strs("InterfaceAlias"), a.Name)))
                {
                    var x = CimObj("MSFT_DNSClient", "ROOT/StandardCimv2");
                    x.Val("InterfaceAlias", "string", ad.Name); x.Val("InterfaceIndex", "uint32", ad.IfIndex);
                    x.Val("ConnectionSpecificSuffix", "string", ""); x.Val("ConnectionSpecificSuffixSearchList", "string[]", new object[0]);
                    x.Val("RegisterThisConnectionsAddress", "bool", true); x.Val("UseSuffixWhenRegistering", "bool", false);
                    x.Val("PSComputerName", "string", null);
                    x.ToStr = y => ad.Name;
                    c.Out(x);
                }
            }));
            Cdxml(Add("Get-DnsClientGlobalSetting", DnsClientMod, "CimSession:object", (c, o) =>
            {
                var x = CimObj("MSFT_DnsClientGlobalSetting", "ROOT/StandardCimv2");
                x.Val("UseSuffixSearchList", "bool", true); x.Val("SuffixSearchList", "string[]", new object[] { c.M.Domain });
                x.Val("UseDevolution", "bool", true); x.Val("DevolutionLevel", "uint32", 0);
                x.DefaultDisplay = new[] { "UseSuffixSearchList", "SuffixSearchList", "UseDevolution", "DevolutionLevel" };
                c.Out(x);
            }));

            Cdxml(Add("Get-NetConnectionProfile", NetConn, "Name:string[]@0; InterfaceAlias:string[] alias=ifAlias; InterfaceIndex:int[] alias=ifIndex; NetworkCategory:string[]; CimSession:object", (c, o) =>
            {
                var m = c.M;
                foreach (var ad in m.Adapters.Where(a => !a.Loopback && a.IsUp && MatchAny(c.Strs("InterfaceAlias"), a.Name)))
                    c.Out(ConnProfileObj(m, ad));
            }));

            // ---------------------------------------------------- the firewall
            Cdxml(Add("Get-NetFirewallRule", NetSec, "Name:string[]@0 alias=ID; DisplayName:string[]; Description:string[]; DisplayGroup:string[]; Group:string[]; Enabled:string[] set=True|False; Direction:string[] set=Inbound|Outbound; Action:string[] set=Allow|Block|NotConfigured; PolicyStore:string; All:switch; AssociatedNetFirewallPortFilter:object pipe; CimSession:object", (c, o) =>
            {
                var m = c.M;
                if (c.HasInput && PsConv.Base(o) is PsObj && ((PsObj)PsConv.Base(o)).Base is FwRule) { c.Out(FwRuleObj((FwRule)((PsObj)PsConv.Base(o)).Base)); return; }
                IEnumerable<FwRule> l = m.FwRules;
                if (c.Has("Name") || c.Has("DisplayName") || c.Has("DisplayGroup") || c.Has("Group")) l = PickRules(c, null);
                if (c.Has("Enabled")) l = l.Where(r => c.Strs("Enabled").Any(x => x.Equals(r.Enabled ? "True" : "False", StringComparison.OrdinalIgnoreCase)));
                if (c.Has("Direction")) l = l.Where(r => MatchAny(c.Strs("Direction"), r.Direction));
                if (c.Has("Action")) l = l.Where(r => MatchAny(c.Strs("Action"), r.Action));
                if (c.Has("Description")) l = l.Where(r => MatchAny(c.Strs("Description"), r.Description));
                foreach (var r in l.ToList()) c.Out(FwRuleObj(r));
            }));
            var nfr = Cdxml(Add("New-NetFirewallRule", NetSec, "DisplayName:string@0 !; Name:string alias=ID; Description:string; Group:string; Enabled:string set=True|False; Profile:string[]; Direction:string set=Inbound|Outbound; Action:string set=Allow|Block|NotConfigured; Protocol:string; LocalPort:string[]; RemotePort:string[]; LocalAddress:string[]; RemoteAddress:string[]; Program:string; IcmpType:string[]; Service:string; EdgeTraversalPolicy:string; PolicyStore:string; CimSession:object", (c, o) =>
            {
                var m = c.M;
                var r = new FwRule { Name = c.Str("Name") ?? "{" + PsConv.ToStr(m.NewGuid()) + "}", DisplayName = c.Str("DisplayName"), Builtin = false };
                if (m.FwRules.Any(x => x.Name.Equals(r.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    c.WriteError(CimError(c, "MSFT_NetFirewallRule", "root/standardcimv2", "Cannot create a file when that file already exists.", "Windows System Error 183", "ResourceExists"));
                    return;
                }
                ApplyRuleSettings(c, r);
                if (c.Has("Group")) { r.Group = c.Str("Group"); r.DisplayGroup = c.Str("Group"); }
                if ((r.LocalPort != "Any" || r.RemotePort != "Any") && r.Protocol != "TCP" && r.Protocol != "UDP")
                {
                    c.WriteError(CimError(c, "MSFT_NetFirewallRule", "root/standardcimv2", "The port filter can only be set when the protocol is TCP or UDP: add -Protocol TCP or -Protocol UDP.", "HRESULT 0x80070057", "InvalidArgument"));
                    return;
                }
                if (!c.ShouldProcess(r.DisplayName, "New")) return;
                m.FwRules.Add(r);
                c.Out(FwRuleObj(r));
            }));
            nfr.ShouldProcess = true;
            var sfr = Cdxml(Add("Set-NetFirewallRule", NetSec, "Name:string[]@0 alias=ID; DisplayName:string[]; DisplayGroup:string[]; Group:string[]; InputObject:object pipe; NewDisplayName:string; Description:string; Enabled:string set=True|False; Profile:string[]; Direction:string set=Inbound|Outbound; Action:string set=Allow|Block|NotConfigured; Protocol:string; LocalPort:string[]; RemotePort:string[]; LocalAddress:string[]; RemoteAddress:string[]; Program:string; IcmpType:string[]; PolicyStore:string; PassThru:switch; CimSession:object", (c, o) =>
            {
                foreach (var r in PickRules(c, o))
                {
                    if (!c.ShouldProcess(r.Name, "Set")) continue;
                    ApplyRuleSettings(c, r);
                    if (c.Sw("PassThru")) c.Out(FwRuleObj(r));
                }
            }));
            sfr.ShouldProcess = true;
            foreach (var verb in new[] { "Enable", "Disable", "Remove" })
            {
                string v = verb;
                var d = Cdxml(Add(v + "-NetFirewallRule", NetSec, "Name:string[]@0 alias=ID; DisplayName:string[]; DisplayGroup:string[]; Group:string[]; Direction:string[]; InputObject:object pipe; PolicyStore:string; PassThru:switch; CimSession:object", (c, o) =>
                {
                    foreach (var r in PickRules(c, o))
                    {
                        if (!c.ShouldProcess(r.Name, v)) continue;
                        if (c.Has("Direction") && !MatchAny(c.Strs("Direction"), r.Direction)) continue;
                        if (v == "Remove") c.M.FwRules.Remove(r); else r.Enabled = v == "Enable";
                        if (c.Sw("PassThru")) c.Out(FwRuleObj(r));
                    }
                }));
                d.ShouldProcess = true;
            }
            Cdxml(Add("Get-NetFirewallPortFilter", NetSec, "AssociatedNetFirewallRule:object@0 pipe; Protocol:string[]; LocalPort:string[]; All:switch; PolicyStore:string; CimSession:object", (c, o) =>
            {
                var rules = new List<FwRule>();
                var src = c.HasInput ? o : c.Get("AssociatedNetFirewallRule");
                foreach (var x in PsConv.Items(src)) { var po = PsConv.Base(x) as PsObj; if (po != null && po.Base is FwRule) rules.Add((FwRule)po.Base); }
                if (!c.HasInput && src == null) rules.AddRange(c.M.FwRules);
                foreach (var r in rules)
                {
                    var p = new PsObj(CimInst + "#root/standardcimv2/MSFT_NetProtocolPortFilter", CimInst + "#MSFT_NetProtocolPortFilter", CimInst, "System.Object");
                    p.Val("Protocol", "string", r.Protocol); p.Val("LocalPort", "string", r.LocalPort); p.Val("RemotePort", "string", r.RemotePort);
                    p.Val("IcmpType", "string", r.IcmpType); p.Val("DynamicTarget", "string", "Any"); p.Val("PSComputerName", "string", null);
                    p.Base = r;
                    c.Out(p);
                }
            }));
            Cdxml(Add("Get-NetFirewallAddressFilter", NetSec, "AssociatedNetFirewallRule:object@0 pipe; All:switch; PolicyStore:string; CimSession:object", (c, o) =>
            {
                var src = c.HasInput ? o : c.Get("AssociatedNetFirewallRule");
                foreach (var x in PsConv.Items(src))
                {
                    var po = PsConv.Base(x) as PsObj; var r = po == null ? null : po.Base as FwRule;
                    if (r == null) continue;
                    var p = new PsObj(CimInst + "#root/standardcimv2/MSFT_NetAddressFilter", CimInst + "#MSFT_NetAddressFilter", CimInst, "System.Object");
                    p.Val("LocalAddress", "string", r.LocalAddress); p.Val("RemoteAddress", "string", r.RemoteAddress); p.Val("PSComputerName", "string", null);
                    p.Base = r;
                    c.Out(p);
                }
            }));
            Cdxml(Add("Get-NetFirewallProfile", NetSec, "Name:string[]@0 alias=Profile; All:switch; PolicyStore:string; CimSession:object", (c, o) =>
            {
                var m = c.M;
                if (c.Has("Name"))
                {
                    foreach (var n in c.Strs("Name"))
                    {
                        var hit = m.FwProfiles.Where(p => PsConv.Like(p.Name, n, false)).ToList();
                        if (hit.Count == 0) c.WriteError(CimNotFound(c, "MSFT_NetFirewallProfile", "Name", n));
                        foreach (var p in hit) c.Out(FwProfileObj(p));
                    }
                    return;
                }
                foreach (var p in m.FwProfiles) c.Out(FwProfileObj(p));
            }));
            var sfp = Cdxml(Add("Set-NetFirewallProfile", NetSec, "Name:string[]@0 alias=Profile; All:switch; Enabled:string set=True|False|NotConfigured; DefaultInboundAction:string set=Allow|Block|NotConfigured; DefaultOutboundAction:string set=Allow|Block|NotConfigured; LogAllowed:string set=True|False|NotConfigured; LogBlocked:string set=True|False|NotConfigured; LogFileName:string; LogMaxSizeKilobytes:int; NotifyOnListen:string; InputObject:object pipe; PolicyStore:string; PassThru:switch; CimSession:object", (c, o) =>
            {
                var m = c.M;
                var ps = new List<FwProfile>();
                if (c.HasInput && PsConv.Base(o) is PsObj && ((PsObj)PsConv.Base(o)).Base is FwProfile) ps.Add((FwProfile)((PsObj)PsConv.Base(o)).Base);
                else if (c.Sw("All")) ps.AddRange(m.FwProfiles);
                else if (c.Has("Name"))
                    foreach (var n in c.Strs("Name").SelectMany(x => x.Split(',')).Select(x => x.Trim()))
                    {
                        var p = m.FwProfiles.FirstOrDefault(x => x.Name.Equals(n, StringComparison.OrdinalIgnoreCase));
                        if (p == null) c.WriteError(CimNotFound(c, "MSFT_NetFirewallProfile", "Name", n)); else ps.Add(p);
                    }
                foreach (var p in ps)
                {
                    if (!c.ShouldProcess(p.Name, "Set")) continue;
                    if (c.Has("Enabled")) p.Enabled = c.Str("Enabled") != "False";
                    if (c.Has("DefaultInboundAction")) p.DefaultInboundAction = c.Str("DefaultInboundAction");
                    if (c.Has("DefaultOutboundAction")) p.DefaultOutboundAction = c.Str("DefaultOutboundAction");
                    if (c.Has("LogAllowed")) p.LogAllowed = c.Str("LogAllowed") == "True";
                    if (c.Has("LogBlocked")) p.LogBlocked = c.Str("LogBlocked") == "True";
                    if (c.Sw("PassThru")) c.Out(FwProfileObj(p));
                }
            }));
            sfp.ShouldProcess = true;
        }

        static List<NetAdapterInfo> PickAdapters(CmdCtx c, object o)
        {
            var l = new List<NetAdapterInfo>();
            if (c.HasInput && PsConv.Base(o) is PsObj && ((PsObj)PsConv.Base(o)).Base is NetAdapterInfo) { l.Add((NetAdapterInfo)((PsObj)PsConv.Base(o)).Base); return l; }
            foreach (var n in c.Strs("Name") ?? new string[0])
            {
                var hit = c.M.Adapters.Where(a => !a.Loopback && PsConv.Like(a.Name, n, false)).ToList();
                if (hit.Count == 0 && !PsConv.HasWildcard(n)) c.WriteError(CimNotFound(c, "MSFT_NetAdapter", "Name", n));
                l.AddRange(hit);
            }
            return l;
        }

        static PsObj IpInterfaceObj(NetAdapterInfo ad, bool v6, string dhcp)
        {
            var o = CimObj("MSFT_NetIPInterface", "ROOT/StandardCimv2");
            o.Val("ifIndex", "uint32", ad.IfIndex);
            o.Val("InterfaceAlias", "string", ad.Name);
            o.Val("AddressFamily", "AddressFamily", PsEnums.V("AddressFamily", v6 ? "IPv6" : "IPv4"));
            o.Val("NlMtu", "uint32", ad.Mtu < 0 ? 4294967295L : ad.Mtu);
            o.Val("InterfaceMetric", "uint32", ad.IfMetric);
            o.Val("Dhcp", "string", dhcp);
            o.Val("ConnectionState", "string", ad.IsUp ? "Connected" : "Disconnected");
            o.Val("PolicyStore", "string", "ActiveStore");
            o.Val("InterfaceIndex", "uint32", ad.IfIndex);
            o.Val("Forwarding", "string", "Disabled");
            o.Val("AutomaticMetric", "string", "Enabled");
            o.Val("PSComputerName", "string", null);
            o.ToStr = x => ad.Name;
            o.Base = ad;
            return o;
        }

        static PsObj ConnProfileObj(PsMachine m, NetAdapterInfo ad)
        {
            var o = CimObj("MSFT_NetConnectionProfile", "ROOT/StandardCimv2");
            o.Val("Name", "string", m.Domain);
            o.Val("InterfaceAlias", "string", ad.Name);
            o.Val("InterfaceIndex", "uint32", ad.IfIndex);
            o.Val("NetworkCategory", "string", "DomainAuthenticated");
            o.Val("IPv4Connectivity", "string", "LocalNetwork");
            o.Val("IPv6Connectivity", "string", "NoTraffic");
            o.Val("PSComputerName", "string", null);
            o.DefaultDisplay = new[] { "Name", "InterfaceAlias", "InterfaceIndex", "NetworkCategory", "IPv4Connectivity", "IPv6Connectivity" };
            o.ToStr = x => m.Domain;
            return o;
        }

        static PsObj IpConfigObj(PsMachine m, NetAdapterInfo ad, bool detailed)
        {
            var o = new PsObj("NetIPConfiguration", "System.Object");
            if (detailed) o.TypeNames.Insert(1, "NetIPConfiguration#Detailed");
            var v4 = m.IpAddrs.Where(a => !a.V6 && a.IfIndex == ad.IfIndex).Select(a => (object)IpObj(m, a, "ActiveStore")).ToArray();
            var ll = m.IpAddrs.Where(a => a.V6 && a.IfIndex == ad.IfIndex).Select(a => (object)IpObj(m, a, "ActiveStore")).ToArray();
            var gws = m.Routes.Where(r => !r.V6 && r.IfIndex == ad.IfIndex && r.Dest == "0.0.0.0/0").Select(r => (object)RouteObj(m, r, "ActiveStore")).ToArray();
            var dns = ad.Loopback ? new object[0] : new object[] { DnsServerObj(m, ad, false) };
            o.Val("ComputerName", "string", m.Name);
            o.Val("InterfaceAlias", "string", ad.Name);
            o.Val("InterfaceIndex", "int", ad.IfIndex);
            o.Val("InterfaceDescription", "string", ad.Description);
            o.Val("NetAdapter", "object", AdapterObj(m, ad));
            o.Val("NetProfile", "object", ad.Loopback ? null : ConnProfileObj(m, ad));
            o.Val("IPv6Address", "object", null);
            o.Val("IPv6TemporaryAddress", "object", null);
            o.Val("IPv6LinkLocalAddress", "object", ll.Length == 1 ? ll[0] : (object)ll);
            o.Val("IPv4Address", "object", v4.Length == 1 ? v4[0] : (object)v4);
            o.Val("IPv6DefaultGateway", "object", null);
            o.Val("IPv4DefaultGateway", "object", gws.Length == 1 ? gws[0] : gws.Length == 0 ? null : (object)gws);
            o.Val("DNSServer", "object", dns.Length == 1 ? dns[0] : (object)dns);
            o.ToStr = x => ad.Name;
            return o;
        }
        static string Lines(IEnumerable<string> l) { return string.Join("\n", l.Where(x => !string.IsNullOrEmpty(x)).ToArray()); }

        static void RegisterNetViews()
        {
            PsFormat.Table(Std("MSFT_NetAdapter"), PsFormat.P("Name", 25), PsFormat.P("InterfaceDescription", 39), PsFormat.PR("ifIndex", 7), PsFormat.P("Status", 12), PsFormat.P("MacAddress", 22), PsFormat.PR("LinkSpeed", 9));
            var ipList = new[] { "IPAddress", "InterfaceIndex", "InterfaceAlias", "AddressFamily", "Type", "PrefixLength", "PrefixOrigin", "SuffixOrigin", "AddressState", "ValidLifetime", "PreferredLifetime", "SkipAsSource", "PolicyStore" };
            ListView(Std("MSFT_NetIPAddress"), ipList.Select(p => p.EndsWith("Lifetime", StringComparison.Ordinal) ? PsFormat.Col(p, 0, false, o => Lifetime(Gv(o, p))) : PsFormat.P(p, 0)).ToArray());
            PsFormat.ListProps(Std("MSFT_NetIPAddress"), ipList);
            PsFormat.Table(Std("MSFT_NetIPInterface"), PsFormat.P("ifIndex", 7), PsFormat.P("InterfaceAlias", 31), PsFormat.P("AddressFamily", 13), PsFormat.Col("NlMtu(Bytes)", 12, true, o => Gv(o, "NlMtu")), PsFormat.PR("InterfaceMetric", 15), PsFormat.P("Dhcp", 8), PsFormat.P("ConnectionState", 15), PsFormat.P("PolicyStore", 0));
            PsFormat.Table(Std("MSFT_NetRoute"), PsFormat.P("ifIndex", 7), PsFormat.P("DestinationPrefix", 46), PsFormat.P("NextHop", 40), PsFormat.PR("RouteMetric", 11), PsFormat.P("ifMetric", 8), PsFormat.P("PolicyStore", 0));
            PsFormat.Table(Std("MSFT_NetTCPConnection"), PsFormat.P("LocalAddress", 35), PsFormat.P("LocalPort", 9), PsFormat.P("RemoteAddress", 35), PsFormat.P("RemotePort", 10), PsFormat.P("State", 11), PsFormat.P("AppliedSetting", 14), PsFormat.P("OwningProcess", 0));
            PsFormat.Table(Std("MSFT_DNSClientServerAddress"), PsFormat.P("InterfaceAlias", 28), PsFormat.Col("Interface\nIndex", 9, true, o => Gv(o, "InterfaceIndex")), PsFormat.Col("Address\nFamily", 7, false, o => PsConv.ToInt(Gv(o, "AddressFamily")) == 23 ? "IPv6" : "IPv4"), PsFormat.P("ServerAddresses", 0));
            PsFormat.Table(Std("MSFT_DNSClient"), PsFormat.P("InterfaceAlias", 28), PsFormat.Col("Interface\nIndex", 9, true, o => Gv(o, "InterfaceIndex")), PsFormat.Col("ConnectionSpecificSuffix", 28, false, o => Gv(o, "ConnectionSpecificSuffix")), PsFormat.Col("ConnectionSpecificSuffix\nSearchList", 24, false, o => Gv(o, "ConnectionSpecificSuffixSearchList")), PsFormat.Col("RegisterThisConn\nectionsAddress", 16, false, o => Gv(o, "RegisterThisConnectionsAddress")), PsFormat.Col("UseSuffixWhen\nRegistering", 0, false, o => Gv(o, "UseSuffixWhenRegistering")));
            ListView(Std("MSFT_NetConnectionProfile"), new[] { "Name", "InterfaceAlias", "InterfaceIndex", "NetworkCategory", "IPv4Connectivity", "IPv6Connectivity" }.Select(p => PsFormat.P(p, 0)).ToArray());
            ListView(CimInst + "#root/standardcimv2/MSFT_NetFirewallRule", FwRuleList.Select(p => PsFormat.P(p, 0)).ToArray());
            PsFormat.ListProps(CimInst + "#root/standardcimv2/MSFT_NetFirewallRule", FwRuleList);
            var prof = new[] { "Name", "Enabled", "DefaultInboundAction", "DefaultOutboundAction", "AllowInboundRules", "AllowLocalFirewallRules", "AllowLocalIPsecRules", "AllowUserApps", "AllowUserPorts", "AllowUnicastResponseToMulticast", "NotifyOnListen", "EnableStealthModeForIPsec", "LogFileName", "LogMaxSizeKilobytes", "LogAllowed", "LogBlocked", "LogIgnored", "DisabledInterfaceAliases" };
            ListView(CimInst + "#root/standardcimv2/MSFT_NetFirewallProfile", prof.Select(p => PsFormat.P(p, 0)).ToArray());
            PsFormat.ListProps(CimInst + "#root/standardcimv2/MSFT_NetFirewallProfile", prof);
            ListView(CimInst + "#root/standardcimv2/MSFT_NetProtocolPortFilter", new[] { "Protocol", "LocalPort", "RemotePort", "IcmpType", "DynamicTarget" }.Select(p => PsFormat.P(p, 0)).ToArray());
            ListView(CimInst + "#root/standardcimv2/MSFT_NetAddressFilter", new[] { "LocalAddress", "RemoteAddress" }.Select(p => PsFormat.P(p, 0)).ToArray());
            // Resolve-DnsName
            PsFormat.Table("Microsoft.DnsClient.Commands.DnsRecord_A#Table", PsFormat.P("Name", 46), PsFormat.P("Type", 6), PsFormat.P("TTL", 5), PsFormat.P("Section", 10), PsFormat.P("IPAddress", 0));
            PsFormat.Table("Microsoft.DnsClient.Commands.DnsRecord_AAAA#Table", PsFormat.P("Name", 46), PsFormat.P("Type", 6), PsFormat.P("TTL", 5), PsFormat.P("Section", 10), PsFormat.P("IPAddress", 0));
            foreach (var t in new[] { "CNAME", "PTR", "NS" }) PsFormat.Table("Microsoft.DnsClient.Commands.DnsRecord_" + t + "#Table", PsFormat.P("Name", 30), PsFormat.P("Type", 6), PsFormat.P("TTL", 5), PsFormat.P("Section", 10), PsFormat.P("NameHost", 0));
            PsFormat.Table("Microsoft.DnsClient.Commands.DnsRecord_SOA#Table",PsFormat.P("Name", 27), PsFormat.P("Type", 4), PsFormat.P("TTL", 5), PsFormat.P("Section", 10), PsFormat.P("PrimaryServer", 27), PsFormat.P("NameAdministrator", 27), PsFormat.P("SerialNumber", 0));
            ListView("Microsoft.DnsClient.Commands.DnsRecord_A#List", PsFormat.P("Name", 0), PsFormat.P("QueryType", 0), PsFormat.P("TTL", 0), PsFormat.P("Section", 0), PsFormat.P("IP4Address", 0));
            ListView("Microsoft.DnsClient.Commands.DnsRecord_AAAA#List", PsFormat.P("Name", 0), PsFormat.P("QueryType", 0), PsFormat.P("TTL", 0), PsFormat.P("Section", 0), PsFormat.P("IP6Address", 0));
            ListView("Microsoft.DnsClient.Commands.DnsRecord_SOA#List", PsFormat.P("Name", 0), PsFormat.P("QueryType", 0), PsFormat.P("TTL", 0), PsFormat.P("Section", 0), PsFormat.P("PrimaryServer", 0), PsFormat.P("NameAdministrator", 0), PsFormat.P("SerialNumber", 0));
            // Get-NetIPConfiguration
            Func<object, string, object> sub = (o, p) => Gv(Gv(o, p.Split('.')[0]), p.Split('.')[1]);
            Func<object, string, string> lines = (o, p) => Lines(PsConv.Items(Gv(o, p)).Select(x => PsConv.ToStr(x)));
            Func<object, string> gw = o => Lines(PsConv.Items(Gv(o, "IPv4DefaultGateway")).Select(x => PsConv.ToStr(Gv(x, "NextHop"))));
            Func<object, string> dns = o => Lines(PsConv.Items(Gv(o, "DNSServer")).SelectMany(x => PsConv.Items(Gv(x, "ServerAddresses"))).Select(x => PsConv.ToStr(x)));
            ListView("NetIPConfiguration", PsFormat.P("InterfaceAlias", 0), PsFormat.P("InterfaceIndex", 0), PsFormat.P("InterfaceDescription", 0), PsFormat.Col("NetProfile.Name", 0, false, o => sub(o, "NetProfile.Name")),
                PsFormat.Col("IPv4Address", 0, false, o => lines(o, "IPv4Address")), PsFormat.Col("IPv6DefaultGateway", 0, false, o => ""), PsFormat.Col("IPv4DefaultGateway", 0, false, o => gw(o)), PsFormat.Col("DNSServer", 0, false, o => dns(o)));
            ListView("NetIPConfiguration#Detailed", PsFormat.P("ComputerName", 0), PsFormat.P("InterfaceAlias", 0), PsFormat.P("InterfaceIndex", 0), PsFormat.P("InterfaceDescription", 0),
                PsFormat.Col("NetAdapter.LinkLayerAddress", 0, false, o => sub(o, "NetAdapter.MacAddress")), PsFormat.Col("NetAdapter.Status", 0, false, o => sub(o, "NetAdapter.Status")),
                PsFormat.Col("NetProfile.Name", 0, false, o => sub(o, "NetProfile.Name")), PsFormat.Col("NetProfile.NetworkCategory", 0, false, o => sub(o, "NetProfile.NetworkCategory")),
                PsFormat.Col("NetProfile.IPv6Connectivity", 0, false, o => sub(o, "NetProfile.IPv6Connectivity")), PsFormat.Col("NetProfile.IPv4Connectivity", 0, false, o => sub(o, "NetProfile.IPv4Connectivity")),
                PsFormat.Col("IPv6LinkLocalAddress", 0, false, o => lines(o, "IPv6LinkLocalAddress")), PsFormat.Col("IPv4Address", 0, false, o => lines(o, "IPv4Address")),
                PsFormat.Col("IPv6DefaultGateway", 0, false, o => ""), PsFormat.Col("IPv4DefaultGateway", 0, false, o => gw(o)),
                PsFormat.Col("NetIPv6Interface.NlMTU", 0, false, o => sub(o, "NetAdapter.MtuSize")), PsFormat.Col("NetIPv4Interface.NlMTU", 0, false, o => sub(o, "NetAdapter.MtuSize")),
                PsFormat.Col("NetIPv6Interface.DHCP", 0, false, o => "Enabled"), PsFormat.Col("NetIPv4Interface.DHCP", 0, false, o => { var ad = (PsConv.Base(Gv(o, "NetAdapter")) as PsObj); var b = ad == null ? null : ad.Base as NetAdapterInfo; return b != null && b.Dhcp ? "Enabled" : "Disabled"; }),
                PsFormat.Col("DNSServer", 0, false, o => dns(o)));
            // Test-NetConnection
            var cn = PsFormat.P("ComputerName", 0); var ra = PsFormat.P("RemoteAddress", 0); var rp = PsFormat.P("RemotePort", 0); var ia = PsFormat.P("InterfaceAlias", 0); var sa = PsFormat.P("SourceAddress", 0);
            var ps = PsFormat.P("PingSucceeded", 0); var rtt = PsFormat.Col("PingReplyDetails (RTT)", 0, false, o => Gv(o, "PingReplyDetails") == null ? "" : PsConv.ToStr(Gv(Gv(o, "PingReplyDetails"), "RoundtripTime")) + " ms"); var tcp = PsFormat.P("TcpTestSucceeded", 0);
            ListView("TestNetConnectionResult#Ping", cn, ra, ia, sa, ps, rtt);
            ListView("TestNetConnectionResult#NoName", cn, ra, ia, sa, ps);
            ListView("TestNetConnectionResult#Tcp", cn, ra, rp, ia, sa, tcp);
            ListView("TestNetConnectionResult#TcpFail", cn, ra, rp, ia, sa, ps, rtt, tcp);
            var nrr = PsFormat.Col("NameResolutionResults", 0, false, o => Lines(PsConv.Items(Gv(o, "NameResolutionResults")).Select(x => PsConv.ToStr(x))));
            var hop = PsFormat.Col("NetRoute (NextHop)", 0, false, o => Gv(o, "NextHop"));
            var mir = PsFormat.Col("MatchingIPsecRules", 0, false, o => ""); var nic = PsFormat.Col("NetworkIsolationContext", 0, false, o => "Private Network"); var adm = PsFormat.Col("IsAdmin", 0, false, o => "True");
            ListView("TestNetConnectionResult#DetailedPing", cn, ra, nrr, mir, nic, adm, ia, sa, hop, ps, rtt);
            ListView("TestNetConnectionResult#DetailedTcp", cn, ra, rp, nrr, mir, nic, adm, ia, sa, hop, tcp);
            ListView("TestNetConnectionResult#Trace", cn, ra, ia, sa, ps, rtt, PsFormat.Col("TraceRoute", 0, false, o => Lines(PsConv.Items(Gv(o, "TraceRoute")).Select(x => PsConv.ToStr(x)))));
            PsFormat.Table("System.Management.ManagementObject#root\\cimv2\\Win32_PingStatus", PsFormat.Col("Source", 13, false, o => Gv(o, "__SERVER")), PsFormat.Col("Destination", 15, false, o => Gv(o, "Address")), PsFormat.P("IPV4Address", 16), PsFormat.P("IPV6Address", 40), PsFormat.Col("Bytes", 8, false, o => Gv(o, "ReplySize")), PsFormat.Col("Time(ms)", 8, false, o => Gv(o, "ResponseTime")));
        }

        static void TestNetConnection(CmdCtx c)
        {
            var m = c.M;
            string target = c.Str("ComputerName") ?? "internetbeacon.msedge.net";
            int port = c.Has("Port") ? c.Int("Port", 0) : CommonPort(c.Str("CommonTCPPort"));
            string level = c.Str("InformationLevel", "");
            var p = NetProbe.Run(m, target, port);
            var o = new PsObj("TestNetConnectionResult", "System.Object");
            o.Val("ComputerName", "string", target);
            o.Val("RemoteAddress", "ipaddress", p.Address);
            if (port > 0) o.Val("RemotePort", "uint32", port);
            o.Val("NameResolutionResults", "object[]", p.Address == null ? new object[0] : new object[] { p.Address });
            o.Val("InterfaceAlias", "string", p.Alias);
            o.Val("SourceAddress", "object", p.Source == null ? null : IpObj(m, m.IpAddrs.First(a => a.Address == p.Source), "ActiveStore"));
            o.Val("NextHop", "string", p.NextHop == null ? null : p.NextHop == p.Address ? "0.0.0.0" : p.NextHop);
            o.Val("PingSucceeded", "bool", p.Ping);
            var reply = new PsObj("System.Net.NetworkInformation.PingReply", "System.Object");
            reply.Val("Status", "string", p.Ping ? "Success" : "TimedOut"); reply.Val("Address", "ipaddress", p.Address); reply.Val("RoundtripTime", "long", (long)(p.Ping ? p.Rtt : 0));
            o.Val("PingReplyDetails", "System.Net.NetworkInformation.PingReply", p.Resolved ? reply : null);
            o.Val("TcpTestSucceeded", "bool", p.Tcp);
            o.Val("BasicNameResolution", "object[]", new object[0]);
            o.Val("LLMNRNetbiosRecords", "object[]", new object[0]);
            o.Val("DNSOnlyRecords", "object[]", new object[0]);
            o.Val("AllNameResolutionResults", "object", p.Address);
            o.Val("IsAdmin", "bool", true);
            o.Val("NetworkIsolationContext", "string", "Private Network");
            o.Val("MatchingIPsecRules", "object", null);
            o.ToStr = x => "TestNetConnectionResult";
            if (!p.Resolved)
            {
                c.Warn("Name resolution of " + target + " failed");
                if (level.Equals("Quiet", StringComparison.OrdinalIgnoreCase)) { c.Out(false); return; }
                c.Out(Variant(o, "NoName"));
                return;
            }
            if (port > 0)
            {
                if (!p.Tcp)
                {
                    c.Warn("TCP connect to (" + p.Address + " : " + port + ") failed");
                    if (!p.Ping) c.Warn("Ping to " + p.Address + " failed with status: TimedOut");
                }
                if (level.Equals("Quiet", StringComparison.OrdinalIgnoreCase)) { c.Out(p.Tcp); return; }
                c.Out(Variant(o, level.Equals("Detailed", StringComparison.OrdinalIgnoreCase) ? "DetailedTcp" : p.Tcp ? "Tcp" : "TcpFail"));
                return;
            }
            if (!p.Ping) c.Warn("Ping to " + p.Address + " failed with status: TimedOut");
            if (level.Equals("Quiet", StringComparison.OrdinalIgnoreCase)) { c.Out(p.Ping); return; }
            if (c.Sw("TraceRoute"))
            {
                var hops = new List<object>();
                if (p.Ping || p.Source != null) { if (p.NextHop != null && p.NextHop != p.Address) hops.Add(p.NextHop); hops.Add(p.Ping ? p.Address : "0.0.0.0"); }
                o.Val("TraceRoute", "string[]", hops.ToArray());
                c.Out(Variant(o, "Trace"));
                return;
            }
            c.Out(Variant(o, level.Equals("Detailed", StringComparison.OrdinalIgnoreCase) ? "DetailedPing" : "Ping"));
        }

        /// <summary>A System.Net.IPAddress as PowerShell shows it (Address, AddressFamily, IPAddressToString...).</summary>
        public static PsObj IpAddressObj(string ip)
        {
            var o = new PsObj("System.Net.IPAddress", "System.Object");
            uint v; Ip4.TryParse(ip, out v);
            long le = ((v >> 24) & 255) | (((v >> 16) & 255) << 8) | (((v >> 8) & 255) << 16) | ((long)(v & 255) << 24);
            o.Val("Address", "long", le);
            o.Val("AddressFamily", "System.Net.Sockets.AddressFamily", PsEnums.V("System.Net.Sockets.AddressFamily", "InterNetwork"));
            o.Val("ScopeId", "long", null);
            foreach (var n in new[] { "IsIPv6Multicast", "IsIPv6LinkLocal", "IsIPv6SiteLocal", "IsIPv6Teredo", "IsIPv4MappedToIPv6" }) o.Val(n, "bool", false);
            o.Val("IPAddressToString", "string", ip);
            o.ToStr = x => ip;
            return o;
        }

        /// <summary>[System.Net.Dns]::GetHostAddresses / GetHostEntry / GetHostName / Resolve on the pretend network.</summary>
        static object DnsStatic(PsRunspace R, string n, object[] args, PsAst at)
        {
            var m = R.M;
            string name = args.Length > 0 ? PsConv.ToStr(args[0]) : "";
            string proper = n == "gethostaddresses" ? "GetHostAddresses" : n == "gethostentry" ? "GetHostEntry" : n == "resolve" ? "Resolve" : n == "gethostbyname" ? "GetHostByName" : n;
            switch (n)
            {
                case "gethostname": return m.Name;
                case "gethostaddresses": case "gethostentry": case "resolve": case "gethostbyname":
                    {
                        string err, fq;
                        var ans = m.Resolve(name, "A", out err, out fq);
                        var ips = ans.Where(a => a.Type == "A").Select(a => a.Data).ToList();
                        if (ips.Count == 0) throw PsMembers.MethodError(R, at, proper, args.Length, err == "TIMEOUT" ? "This is usually a temporary error during hostname resolution and means that the local server did not receive a response from an authoritative server" : "No such host is known", "System.Net.Sockets.SocketException");
                        var addrs = ips.Select(i => (object)IpAddressObj(i)).ToArray();
                        if (n == "gethostaddresses") return addrs;
                        var e = new PsObj("System.Net.IPHostEntry", "System.Object");
                        var cn = ans.Where(a => a.Type == "CNAME").Select(a => (object)a.Name).ToArray();
                        string host = ans.Where(a => a.Type == "A").Select(a => a.Name).First();
                        e.Val("HostName", "string", Ip4.IsV4(name) ? name : host);
                        e.Val("Aliases", "string[]", cn);
                        e.Val("AddressList", "System.Net.IPAddress[]", addrs);
                        return e;
                    }
            }
            return null;
        }
    }

    public static partial class PsNatives
    {
        static partial void RegisterNetNatives()
        {
            Add("ipconfig", null, Ipconfig);
            Add("ping", "C:\\Windows\\system32\\PING.EXE", Ping);
            Add("nslookup", null, Nslookup);
            Add("netstat", null, Netstat);
            Add("tracert", null, Tracert);
            Add("route", null, Route);
            Add("arp", null, call =>
            {
                var m = call.M;
                foreach (var ad in m.Adapters.Where(a => !a.Loopback && a.IsUp))
                {
                    var a4 = m.IpAddrs.FirstOrDefault(a => !a.V6 && a.IfIndex == ad.IfIndex);
                    if (a4 == null) continue;
                    call.L("");
                    call.L("Interface: " + a4.Address + " --- 0x" + ad.IfIndex.ToString("x", CultureInfo.InvariantCulture));
                    call.L("  Internet Address      Physical Address      Type");
                    call.L(string.Format(CultureInfo.InvariantCulture, "  {0,-22}{1,-22}{2}", "10.0.10.1", "00-15-5d-0a-0a-01", "dynamic"));
                    call.L(string.Format(CultureInfo.InvariantCulture, "  {0,-22}{1,-22}{2}", "10.0.10.10", "00-15-5d-0a-0a-0a", "dynamic"));
                    call.L(string.Format(CultureInfo.InvariantCulture, "  {0,-22}{1,-22}{2}", Ip4.Broadcast(a4.Address, a4.Prefix), "ff-ff-ff-ff-ff-ff", "static"));
                    call.L(string.Format(CultureInfo.InvariantCulture, "  {0,-22}{1,-22}{2}", "224.0.0.22", "01-00-5e-00-00-16", "static"));
                }
                return 0;
            });
        }

        /// <summary>An ipconfig label with its dot leader ("   IPv4 Address. . . . . . . . . . . : ").</summary>
        static string Dl(string label)
        {
            var sb = new StringBuilder(label);
            bool dot = label.Length % 2 == 0;
            while (sb.Length < 33) { sb.Append(dot ? '.' : ' '); dot = !dot; }
            return "   " + sb + " : ";
        }

        static int Ipconfig(NativeCall call)
        {
            var m = call.M;
            string opt = call.Args.Count > 0 ? call.Args[0].ToLowerInvariant().Replace('-', '/') : "";
            if (opt == "/flushdns") { m.DnsQueries = 0; call.L(""); call.L("Windows IP Configuration"); call.L(""); call.L("Successfully flushed the DNS Resolver Cache."); return 0; }
            if (opt == "/registerdns") { call.L(""); call.L("Windows IP Configuration"); call.L(""); call.L("Registration of the DNS resource records for all adapters of this computer has been initiated. Any errors will be reported in the Event Viewer in 15 minutes."); return 0; }
            if (opt == "/release" || opt == "/renew")
            {
                call.L(""); call.L("Windows IP Configuration"); call.L("");
                if (!m.Adapters.Any(a => a.Dhcp)) { call.L("The operation failed as no adapter is in the state permissible for"); call.L("this operation."); return 1; }
                if (opt == "/renew" && m.Ws != null)
                {
                    var msg = m.Ws.Renew(m.Name);
                    if (msg != null) { call.L("An error occurred while renewing interface Ethernet : " + msg); return 1; }
                }
            }
            if (opt == "/?" ) { call.L(""); call.L("USAGE:"); call.L("    ipconfig [/allcompartments] [/? | /all | /renew [adapter] | /release [adapter] | /flushdns | /displaydns | /registerdns ]"); return 0; }
            if (opt.Length > 0 && opt != "/all" && opt != "/release" && opt != "/renew") { call.L("Error: unrecognized or incomplete command line."); call.L(""); call.L("USAGE:"); call.L("    ipconfig [/allcompartments] [/? | /all | /renew [adapter] | /release [adapter] | /flushdns | /displaydns | /registerdns ]"); return 1; }
            bool all = opt == "/all";
            if (opt != "/release" && opt != "/renew") { call.L(""); call.L("Windows IP Configuration"); call.L(""); }
            var wsc = m.Ws == null ? null : m.Ws.DhcpClient(m.Name);
            if (all)
            {
                call.L(Dl("Host Name") + m.Name);
                call.L(Dl("Primary Dns Suffix ") + m.Domain);
                call.L(Dl("Node Type") + "Hybrid");
                call.L(Dl("IP Routing Enabled") + "No");
                call.L(Dl("WINS Proxy Enabled") + "No");
                call.L(Dl("DNS Suffix Search List") + m.Domain);
            }
            foreach (var ad in m.Adapters.Where(a => !a.Loopback))
            {
                call.L("");
                call.L("Ethernet adapter " + ad.Name + ":");
                call.L("");
                if (!ad.IsUp)
                {
                    call.L(Dl("Media State") + "Media disconnected");
                    call.L(Dl("Connection-specific DNS Suffix ") + "");
                    continue;
                }
                var v4 = m.IpAddrs.Where(a => !a.V6 && a.IfIndex == ad.IfIndex).ToList();
                var ll = m.IpAddrs.Where(a => a.V6 && a.IfIndex == ad.IfIndex).ToList();
                call.L(Dl("Connection-specific DNS Suffix ") + (ad.Dhcp ? m.Domain : ""));
                if (all)
                {
                    call.L(Dl("Description") + ad.Description);
                    call.L(Dl("Physical Address") + ad.Mac);
                    call.L(Dl("DHCP Enabled") + (ad.Dhcp ? "Yes" : "No"));
                    call.L(Dl("Autoconfiguration Enabled ") + "Yes");
                }
                foreach (var a in ll) call.L(Dl("Link-local IPv6 Address ") + a.Display + (all ? "(Preferred) " : ""));
                bool first = true;
                foreach (var a in v4)
                {
                    call.L(Dl("IPv4 Address") + a.Address + (all ? "(Preferred) " : ""));
                    call.L(Dl("Subnet Mask ") + Ip4.MaskStr(a.Prefix));
                    if (all && ad.Dhcp && first)
                    {
                        call.L(Dl("Lease Obtained") + (wsc != null ? wsc.Obtained : m.Now.AddHours(-2)).ToString("dddd, MMMM d, yyyy h:mm:ss tt", PsConv.Us));
                        call.L(Dl("Lease Expires ") + (wsc != null ? wsc.Expires : m.Now.AddDays(8)).ToString("dddd, MMMM d, yyyy h:mm:ss tt", PsConv.Us));
                    }
                    first = false;
                }
                var gws = m.Routes.Where(r => !r.V6 && r.IfIndex == ad.IfIndex && r.Dest == "0.0.0.0/0").Select(r => r.NextHop).ToList();
                call.L(Dl("Default Gateway ") + (gws.Count > 0 ? gws[0] : ""));
                for (int i = 1; i < gws.Count; i++) call.L(new string(' ', 39) + gws[i]);
                if (all)
                {
                    if (ad.Dhcp) call.L(Dl("DHCP Server ") + (wsc != null ? m.Ws.DhcpServerIp(wsc) : "10.0.10.10"));
                    call.L(Dl("DHCPv6 IAID") + (100663296 + ad.IfIndex * 4096 + 349).ToString(CultureInfo.InvariantCulture));
                    call.L(Dl("DHCPv6 Client DUID") + "00-01-00-01-2D-3A-6B-1C-" + ad.Mac);
                    var dns = m.DnsFor(ad.IfIndex);
                    call.L(Dl("DNS Servers ") + (dns.Count > 0 ? dns[0] : ""));
                    for (int i = 1; i < dns.Count; i++) call.L(new string(' ', 39) + dns[i]);
                    call.L(Dl("NetBIOS over Tcpip") + "Enabled");
                }
            }
            for (int i = 0; i < call.Out.Count; i++) call.Out[i] = call.Out[i].TrimEnd();
            return 0;
        }

        static int Ping(NativeCall call)
        {
            var m = call.M;
            int count = 4; string target = null; bool forever = false;
            for (int i = 0; i < call.Args.Count; i++)
            {
                var a = call.Args[i];
                if ((a == "-n" || a == "/n") && i + 1 < call.Args.Count) { int.TryParse(call.Args[++i], out count); continue; }
                if (a == "-t" || a == "/t") { forever = true; continue; }
                if ((a == "-l" || a == "/l" || a == "-w" || a == "/w" || a == "-i" || a == "/i") && i + 1 < call.Args.Count) { i++; continue; }
                if (a == "-4" || a == "-a" || a == "/4" || a == "/a") continue;
                if (a == "/?" || a == "-?") { call.L(""); call.L("Usage: ping [-t] [-a] [-n count] [-l size] [-w timeout] target_name"); return 1; }
                target = a;
            }
            if (forever) count = 4;
            if (target == null) { call.L(""); call.L("Usage: ping [-t] [-a] [-n count] [-l size] [-w timeout] target_name"); return 1; }
            var p = NetProbe.Run(m, target, 0);
            if (!p.Resolved) { call.L("Ping request could not find host " + target + ". Please check the name and try again."); return 1; }
            string fq = target;
            if (!Ip4.IsV4(target)) { string err, f; m.Resolve(target, "A", out err, out f); fq = f; }
            call.L("");
            call.L("Pinging " + (Ip4.IsV4(target) ? p.Address : fq + " [" + p.Address + "]") + " with 32 bytes of data:");
            if (p.Source == null)
            {
                for (int i = 0; i < count; i++) call.L("PING: transmit failed. General failure. ");
                call.L(""); call.L("Ping statistics for " + p.Address + ":"); call.L("    Packets: Sent = " + count + ", Received = 0, Lost = " + count + " (100% loss),");
                return 1;
            }
            for (int i = 0; i < count; i++) call.L(p.Ping ? "Reply from " + p.Address + ": bytes=32 time" + (p.Rtt > 1 ? "=" + p.Rtt + "ms" : "<1ms") + " TTL=128" : "Request timed out.");
            call.L("");
            call.L("Ping statistics for " + p.Address + ":");
            call.L("    Packets: Sent = " + count + ", Received = " + (p.Ping ? count : 0) + ", Lost = " + (p.Ping ? 0 : count) + " (" + (p.Ping ? 0 : 100) + "% loss),");
            if (p.Ping) { call.L("Approximate round trip times in milli-seconds:"); call.L("    Minimum = 0ms, Maximum = 0ms, Average = 0ms"); }
            return p.Ping ? 0 : 1;
        }

        static int Nslookup(NativeCall call)
        {
            var m = call.M;
            if (m.Ws != null) return m.Ws.Nslookup(call);
            string name = null, type = "A";
            foreach (var a in call.Args)
            {
                if (a.StartsWith("-type=", StringComparison.OrdinalIgnoreCase) || a.StartsWith("-q=", StringComparison.OrdinalIgnoreCase)) { type = a.Substring(a.IndexOf('=') + 1).ToUpperInvariant(); continue; }
                if (name == null) name = a;
            }
            string server = m.DnsServer();
            var sh = server == null ? null : m.Net.DnsServerAt(server);
            bool ok = sh != null && m.Delivers(server);
            if (server == null)
            {
                call.L("Default Server:  UnKnown"); call.L("Address:  127.0.0.1"); call.L("");
                if (name != null) call.L("*** UnKnown can't find " + name + ": No response from server");
                return 1;
            }
            if (!ok) { call.L("DNS request timed out."); call.L("    timeout was 2 seconds."); }
            call.L((name == null ? "Default Server:  " : "Server:  ") + (ok ? sh.Fqdn : "UnKnown"));
            call.L("Address:  " + server);
            call.L("");
            if (name == null) { call.L("> "); return 0; }
            if (!ok) { call.L("DNS request timed out."); call.L("    timeout was 2 seconds."); call.L("*** Request to UnKnown timed-out"); return 1; }
            string err, fq;
            var ans = m.Resolve(name, Ip4.IsV4(name) ? "PTR" : type, out err, out fq);
            if (err != null || ans.Count == 0 || ans.All(x => x.Section == "Authority"))
            {
                if (err == null && ans.Any(x => x.Section == "Authority")) { call.L(fq); call.L("\tprimary name server = dc01." + m.Domain); call.L("\tresponsible mail addr = hostmaster." + m.Domain); return 0; }
                call.L("*** " + sh.Fqdn + " can't find " + name + ": Non-existent domain");
                return 1;
            }
            var ptr = ans.FirstOrDefault(x => x.Type == "PTR");
            if (ptr != null) { call.L("Name:    " + ptr.Data); call.L("Address:  " + name); call.L(""); return 0; }
            var cn = ans.Where(x => x.Type == "CNAME").ToList();
            var a4 = ans.Where(x => x.Type == "A").ToList();
            if (type == "NS" || type == "SOA")
            {
                foreach (var x in ans.Where(x => x.Type == "NS")) call.L(x.Name + "\tnameserver = " + x.Data);
                foreach (var x in ans.Where(x => x.Type == "SOA")) { var p = x.Data.Split('|'); call.L(x.Name); call.L("\tprimary name server = " + p[0]); call.L("\tresponsible mail addr = " + p[1]); call.L("\tserial  = " + p[2]); }
                return 0;
            }
            if (a4.Count == 0) { call.L("*** " + sh.Fqdn + " can't find " + name + ": Non-existent domain"); return 1; }
            call.L("Name:    " + a4[0].Name);
            call.L((a4.Count > 1 ? "Addresses:  " : "Address:  ") + string.Join("\n\t  ", a4.Select(x => x.Data).ToArray()));
            if (cn.Count > 0) call.L("Aliases:  " + string.Join("\n\t  ", cn.Select(x => x.Name).ToArray()));
            call.L("");
            return 0;
        }

        static int Netstat(NativeCall call)
        {
            var m = call.M;
            bool all = call.Args.Any(a => a.Contains("a")), num = call.Args.Any(a => a.Contains("n")), pid = call.Args.Any(a => a.Contains("o"));
            call.L("");
            call.L("Active Connections");
            call.L("");
            call.L("  Proto  Local Address          Foreign Address        State" + (pid ? "           PID" : ""));
            Func<string, string, string, string, int, string> row = (pr, l, f, s, p) => string.Format(CultureInfo.InvariantCulture, "  {0,-7}{1,-23}{2,-23}{3}", pr, l, f, s) + (pid ? new string(' ', Math.Max(1, 16 - s.Length)) + p : "");
            var ports = m.ListeningPorts();
            if (all)
            {
                foreach (var p in ports) call.L(row("TCP", "0.0.0.0:" + p, "0.0.0.0:0", "LISTENING", PidForPort(p)));
            }
            if (m.DnsServer() != null && m.Delivers("10.0.10.10")) call.L(row("TCP", m.Ip + ":49712", num ? "10.0.10.10:389" : "dc01:ldap", "ESTABLISHED", 712));
            if (all)
            {
                foreach (var p in ports) call.L(row("TCP", "[::]:" + p, "[::]:0", "LISTENING", PidForPort(p)));
                foreach (var p in new[] { 123, 500, 4500, 5353, 5355 }) call.L(row("UDP", "0.0.0.0:" + p, "*:*", "", 1324));
                call.L(row("UDP", m.Ip + ":137", "*:*", "", 4));
                call.L(row("UDP", m.Ip + ":138", "*:*", "", 4));
            }
            return 0;
        }
        static int PidForPort(int port) { switch (port) { case 135: return 968; case 3389: return 1236; case 445: case 139: case 5985: case 47001: return 4; } return 1424; }

        static int Tracert(NativeCall call)
        {
            var m = call.M;
            string target = call.Args.LastOrDefault(a => !a.StartsWith("-", StringComparison.Ordinal) && !a.StartsWith("/", StringComparison.Ordinal));
            if (target == null) { call.L(""); call.L("Usage: tracert [-d] [-h maximum_hops] [-w timeout] target_name"); return 1; }
            bool noNames = call.Args.Contains("-d") || call.Args.Contains("/d");
            var p = NetProbe.Run(m, target, 0);
            if (!p.Resolved) { call.L("Unable to resolve target system name " + target + "."); return 1; }
            string err, fq = target;
            if (!Ip4.IsV4(target)) m.Resolve(target, "A", out err, out fq);
            call.L("");
            call.L("Tracing route to " + (Ip4.IsV4(target) ? p.Address : fq + " [" + p.Address + "]"));
            call.L("over a maximum of 30 hops:");
            call.L("");
            if (p.Source == null) { call.L("Unable to contact IP driver. General failure."); return 1; }
            int n = 1;
            if (p.NextHop != null && p.NextHop != p.Address) call.L(string.Format(CultureInfo.InvariantCulture, "{0,3}    <1 ms    <1 ms    <1 ms  {1}", n++, p.NextHop));
            if (p.Ping)
            {
                var h = m.Net.HostByIp(p.Address);
                string nm = noNames || h == null ? p.Address : h.Fqdn + " [" + p.Address + "]";
                call.L(string.Format(CultureInfo.InvariantCulture, "{0,3}    <1 ms    <1 ms    <1 ms  {1}", n, nm));
            }
            else
                for (int i = n; i <= Math.Min(30, n + 3); i++) call.L(string.Format(CultureInfo.InvariantCulture, "{0,3}     *        *        *     Request timed out.", i));
            call.L("");
            call.L("Trace complete.");
            return 0;
        }

        static int Route(NativeCall call)
        {
            var m = call.M;
            string verb = call.Args.Count > 0 ? call.Args[0].ToLowerInvariant() : "";
            if (verb == "add" || verb == "delete" || verb == "change")
            {
                call.L(verb == "add" ? " OK!" : " OK!");
                if (verb == "add" && call.Args.Count >= 5 && call.Args[2].Equals("mask", StringComparison.OrdinalIgnoreCase))
                {
                    uint mk; int pre = 32;
                    if (Ip4.TryParse(call.Args[3], out mk)) { pre = 0; while (mk != 0) { pre += (int)(mk >> 31); mk <<= 1; } }
                    var eth = m.Eth;
                    if (eth != null) m.Routes.Add(new NetRouteInfo { Dest = Ip4.Network(call.Args[1], pre) + "/" + pre, NextHop = call.Args[4], IfIndex = eth.IfIndex });
                }
                if (verb == "delete" && call.Args.Count >= 2) m.Routes.RemoveAll(r => r.Dest.StartsWith(call.Args[1] + "/", StringComparison.Ordinal));
                return 0;
            }
            if (verb != "print" && verb != "" ) { call.L("Bad command: " + verb); return 1; }
            const string bar = "===========================================================================";
            call.L(bar);
            call.L("Interface List");
            foreach (var ad in m.Adapters.OrderByDescending(a => a.IfIndex))
            {
                string mac = ad.Loopback ? "" : ad.Mac.Replace('-', ' ').ToLowerInvariant() + " ";
                string left = string.Format(CultureInfo.InvariantCulture, "{0,3}...", ad.IfIndex) + mac;
                call.L(left.PadRight(29, '.') + ad.Description);
            }
            call.L(bar);
            call.L("");
            call.L("IPv4 Route Table");
            call.L(bar);
            call.L("Active Routes:");
            call.L("Network Destination        Netmask          Gateway       Interface  Metric");
            var rows = new List<string[]>();
            foreach (var r in m.Routes.Where(x => !x.V6 && x.Dest == "0.0.0.0/0"))
            {
                var src = m.LanV4.FirstOrDefault(a => a.IfIndex == r.IfIndex);
                var ad = m.AdapterByIndex(r.IfIndex);
                rows.Add(new[] { "0.0.0.0", "0.0.0.0", r.NextHop, src == null ? "" : src.Address, (r.Metric + (ad == null ? 0 : ad.IfMetric)).ToString(CultureInfo.InvariantCulture) });
            }
            foreach (var a in m.LanV4)
            {
                var ad = m.AdapterByIndex(a.IfIndex);
                string metric = (256 + ad.IfMetric).ToString(CultureInfo.InvariantCulture);
                rows.Add(new[] { Ip4.Network(a.Address, a.Prefix), Ip4.MaskStr(a.Prefix), "On-link", a.Address, metric });
                rows.Add(new[] { a.Address, "255.255.255.255", "On-link", a.Address, metric });
                rows.Add(new[] { Ip4.Broadcast(a.Address, a.Prefix), "255.255.255.255", "On-link", a.Address, metric });
            }
            foreach (var r in m.Routes.Where(x => !x.V6 && x.Dest != "0.0.0.0/0"))
            {
                var src = m.LanV4.FirstOrDefault(a => a.IfIndex == r.IfIndex);
                var parts = r.Dest.Split('/');
                rows.Add(new[] { parts[0], Ip4.MaskStr(int.Parse(parts[1], CultureInfo.InvariantCulture)), r.NextHop, src == null ? "" : src.Address, (r.Metric + 15).ToString(CultureInfo.InvariantCulture) });
            }
            rows.Add(new[] { "127.0.0.0", "255.0.0.0", "On-link", "127.0.0.1", "331" });
            rows.Add(new[] { "127.0.0.1", "255.255.255.255", "On-link", "127.0.0.1", "331" });
            rows.Add(new[] { "127.255.255.255", "255.255.255.255", "On-link", "127.0.0.1", "331" });
            rows.Add(new[] { "224.0.0.0", "240.0.0.0", "On-link", "127.0.0.1", "331" });
            foreach (var a in m.LanV4.Take(1)) rows.Add(new[] { "224.0.0.0", "240.0.0.0", "On-link", a.Address, (256 + m.AdapterByIndex(a.IfIndex).IfMetric).ToString(CultureInfo.InvariantCulture) });
            rows.Add(new[] { "255.255.255.255", "255.255.255.255", "On-link", "127.0.0.1", "331" });
            foreach (var a in m.LanV4.Take(1)) rows.Add(new[] { "255.255.255.255", "255.255.255.255", "On-link", a.Address, (256 + m.AdapterByIndex(a.IfIndex).IfMetric).ToString(CultureInfo.InvariantCulture) });
            foreach (var r in rows) call.L(string.Format(CultureInfo.InvariantCulture, "{0,17}{1,17}{2,17}{3,16}{4,7}", r[0], r[1], r[2], r[3], r[4]));
            call.L(bar);
            call.L("Persistent Routes:");
            var pers = m.Routes.Where(x => !x.V6).ToList();
            if (pers.Count == 0) call.L("  None");
            else
            {
                call.L("  Network Address          Netmask  Gateway Address  Metric");
                foreach (var r in pers) { var parts = r.Dest.Split('/'); call.L(string.Format(CultureInfo.InvariantCulture, "{0,17}{1,17}{2,17}{3,8}", parts[0], Ip4.MaskStr(int.Parse(parts[1], CultureInfo.InvariantCulture)), r.NextHop, "Default")); }
            }
            call.L(bar);
            call.L("");
            call.L("IPv6 Route Table");
            call.L(bar);
            call.L("Active Routes:");
            call.L(" If Metric Network Destination      Gateway");
            var eth2 = m.Eth;
            call.L(string.Format(CultureInfo.InvariantCulture, "{0,3}{1,7} {2,-25}On-link", 1, 331, "::1/128"));
            if (eth2 != null)
            {
                call.L(string.Format(CultureInfo.InvariantCulture, "{0,3}{1,7} {2,-25}On-link", eth2.IfIndex, 271, "fe80::/64"));
                call.L(string.Format(CultureInfo.InvariantCulture, "{0,3}{1,7} {2}", eth2.IfIndex, 271, m.LinkLocal + "/128"));
                call.L("                                    On-link");
            }
            call.L(string.Format(CultureInfo.InvariantCulture, "{0,3}{1,7} {2,-25}On-link", 1, 331, "ff00::/8"));
            if (eth2 != null) call.L(string.Format(CultureInfo.InvariantCulture, "{0,3}{1,7} {2,-25}On-link", eth2.IfIndex, 271, "ff00::/8"));
            call.L(bar);
            call.L("Persistent Routes:");
            call.L("  None");
            return 0;
        }
    }
}
