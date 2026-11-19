// Network Lab - what the router and switch "show" commands print, and their ping and traceroute.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NetworkLab
{
    public static class CliShow
    {
        // ---------------------------------------------------------------- ping and traceroute
        public static void Ping(CliCtx c, string target)
        {
            var s = c.S;
            if (s.Loading) return;
            uint ip;
            bool isIp = Ip.TryParse(target, out ip);
            if (!isIp && c.Ios.HostTable.ContainsKey(target) == false && (!c.Ios.DomainLookup || c.Ios.NameServers.Count == 0))
            {
                s.Print("% Unrecognized host or address, or protocol not running.\n");
                return;
            }
            if (!isIp) s.Print("Translating \"" + target + "\"...domain server (" + (c.Ios.NameServers.Count > 0 ? Ip.Str(c.Ios.NameServers[0]) : "255.255.255.255") + ")");
            PingJob job = null;
            job = PingJob.Start(c.D, target, 5, j =>
            {
                j.TimeoutMs = 2000; j.IntervalMs = 0;
                s.Busy = j;
                j.OnResolveFail = err => s.Print("% Unrecognized host or address, or protocol not running.\n");
                bool headerDone = false;
                Action header = () =>
                {
                    if (headerDone) return; headerDone = true;
                    s.Print("Type escape sequence to abort.\nSending 5, 100-byte ICMP Echos to " + Ip.Str(j.Target) + ", timeout is 2 seconds:");
                };
                j.OnResult = r =>
                {
                    header();
                    switch (r.Kind)
                    {
                        case "reply": s.Write("!"); break;
                        case "timeout": s.Write("."); break;
                        case "prohibited": s.Write("A"); break;
                        case "ttl": s.Write("."); break;
                        default: s.Write("U"); break;
                    }
                };
                j.OnDone = jj =>
                {
                    s.Busy = null;
                    if (jj.Target == 0) return;
                    header();
                    int pct = jj.Sent == 0 ? 0 : jj.Received * 100 / jj.Sent;
                    string rtt = jj.Received > 0 ? string.Format(", round-trip min/avg/max = {0}/{1}/{2} ms", jj.MinMs, jj.SumMs / jj.Received, jj.MaxMs) : "";
                    s.Write("\nSuccess rate is " + pct + " percent (" + jj.Received + "/" + jj.Sent + ")" + rtt + "\n");
                };
            });
        }

        public static void Trace(CliCtx c, string target)
        {
            var s = c.S;
            if (s.Loading) return;
            uint ip;
            if (!Ip.TryParse(target, out ip) && !c.Ios.HostTable.ContainsKey(target) && (!c.Ios.DomainLookup || c.Ios.NameServers.Count == 0))
            {
                s.Print("% Unrecognized host or address, or protocol not running.\n");
                return;
            }
            TraceJob.Start(c.D, target, true, j =>
            {
                s.Busy = j;
                j.MaxHops = 30;
                j.TimeoutMs = 2000;
                bool head = false;
                j.OnResolveFail = err => s.Print("% Unrecognized host or address, or protocol not running.\n");
                j.OnHop = h =>
                {
                    if (!head) { head = true; s.Print("Type escape sequence to abort.\nTracing the route to " + Ip.Str(j.Target) + "\n"); }
                    var sb = new StringBuilder(h.Ttl.ToString().PadLeft(3) + " ");
                    bool any = false;
                    for (int i = 0; i < 3; i++)
                    {
                        if (h.Ms[i] < 0) { sb.Append(i == 0 && h.From == 0 ? "  *    " : "*  "); continue; }
                        if (!any) { sb.Append(Ip.Str(h.From) + " "); any = true; }
                        sb.Append(h.Ms[i] + " msec ");
                    }
                    if (h.Unreachable) sb.Append("!H");
                    s.Print(sb.ToString().TrimEnd());
                };
                j.OnDone = jj => { s.Busy = null; };
            });
        }

        // ---------------------------------------------------------------- show version, clock, flash
        static string ImageName(Device d)
        {
            return Brand.Name.ToLowerInvariant().Replace(" ", "") + "-" + Brand.ModelOf(d.Kind).ToLowerInvariant().Replace(" ", "") + "-universal.bin";
        }

        public static string Version(Device d)
        {
            var sb = new StringBuilder();
            sb.AppendLine(Brand.Os + " Software (Network Lab simulation), Version 1.0");
            sb.AppendLine("Technical support: none needed, this is a practice simulator.");
            sb.AppendLine(Brand.About);
            sb.AppendLine("");
            sb.AppendLine("ROM: " + Brand.Name + " bootstrap program, Version 1.0");
            sb.AppendLine("");
            sb.AppendLine(d.Ios.Hostname + " uptime is " + d.Ios.Uptime);
            sb.AppendLine("System returned to ROM by power-on");
            sb.AppendLine("System image file is \"flash:" + ImageName(d) + "\"");
            sb.AppendLine("");
            sb.AppendLine(Brand.FullModel(d.Kind) + " (simulated processor) with 524288K bytes of memory.");
            int fa = d.Ports.Count(p => p.Name.StartsWith("Fast")), gi = d.Ports.Count(p => p.Name.StartsWith("Gigabit"));
            if (fa > 0) sb.AppendLine(fa + " FastEthernet interfaces");
            if (gi > 0) sb.AppendLine(gi + " Gigabit Ethernet interfaces");
            sb.AppendLine("256K bytes of non-volatile configuration memory.");
            sb.AppendLine("");
            sb.AppendLine("Configuration register is 0x2102");
            return sb.ToString();
        }

        public static string Clock(Device d)
        {
            var t = new DateTime(2026, 1, 1).AddMilliseconds(d.P.Sim.Now);
            return "*" + t.ToString("HH:mm:ss.fff") + " UTC " + t.ToString("ddd MMM d yyyy", System.Globalization.CultureInfo.InvariantCulture);
        }

        public static string Flash(Device d)
        {
            return "Directory of flash:/\n\n    1  -rw-     33591768          <no date>  " + ImageName(d) + "\n\n64016384 bytes total (30424616 bytes free)";
        }

        // ---------------------------------------------------------------- interfaces
        static string PortStatus(Port p)
        {
            if (p.Shutdown) return "administratively down";
            return p.IsUp ? "up" : "down";
        }

        public static string IpIntBrief(Device d)
        {
            var sb = new StringBuilder("Interface              IP-Address      OK? Method Status                Protocol \n");
            Action<string, uint, string, string, string> row = (n, ip, method, status, proto) =>
                sb.Append(n.PadRight(23) + (ip == 0 ? "unassigned" : Ip.Str(ip)).PadRight(16) + "YES " + method.PadRight(7) + status.PadRight(22) + proto + " \n");
            foreach (var p in d.Ports.Where(x => !x.IsSerial))
            {
                var f = p.L3;
                if (f != null && d.Kind != "switch")
                {
                    row(p.Name, f.Ip, f.Dhcp ? "DHCP" : f.Ip != 0 ? "manual" : "unset", PortStatus(p), f.Up ? "up" : "down");
                    foreach (var s in d.Ip.Ifs.Where(x => x.IsSub && x.Port == p).OrderBy(x => x.SubNumber))
                        row(s.Name, s.Ip, s.Ip != 0 ? "manual" : "unset", s.Shutdown ? "administratively down" : (p.Shutdown ? "administratively down" : p.IsUp ? "up" : "down"), s.Up ? "up" : "down");
                }
                else row(p.Name, 0, "unset", PortStatus(p), p.IsUp ? "up" : "down");
            }
            foreach (var f in d.Ip.Ifs.Where(x => x.IsSvi || x.IsLoopback).OrderBy(x => x.Name))
                row(f.Name, f.Ip, f.Dhcp ? "DHCP" : f.Ip != 0 ? "manual" : "unset", f.Shutdown ? "administratively down" : f.Up ? "up" : "down", f.Up ? "up" : "down");
            return sb.ToString().TrimEnd('\n');
        }

        public static string Interfaces(Device d, string which)
        {
            var sb = new StringBuilder();
            string want = which == null ? null : Ios.NormalizeIfName(which);
            if (which != null && want == null) return "% Invalid interface " + which;
            bool found = false;
            foreach (var p in d.Ports.Where(x => !x.IsSerial))
            {
                if (want != null && !string.Equals(p.Name, want, StringComparison.OrdinalIgnoreCase)) continue;
                found = true;
                var f = d.Kind == "switch" ? null : p.L3;
                string st = PortStatus(p), proto = p.IsUp ? "up" : "down";
                sb.AppendLine(p.Name + " is " + st + ", line protocol is " + proto + (d.Kind == "switch" ? (p.IsUp ? " (connected)" : " (notconnect)") : ""));
                sb.AppendLine("  Hardware is " + Brand.Name + " " + (p.Speed >= 1000 ? "Gigabit" : "Fast") + " Ethernet, address is " + p.Mac + " (bia " + p.Mac + ")");
                string desc = f != null ? f.Description : p.Description;
                if (desc.Length > 0) sb.AppendLine("  Description: " + desc);
                if (f != null && f.Ip != 0) sb.AppendLine("  Internet address is " + Ip.Str(f.Ip) + "/" + Ip.PrefixOf(f.Mask));
                sb.AppendLine("  MTU 1500 bytes, BW " + (p.Speed * 1000) + " Kbit, DLY 100 usec,");
                sb.AppendLine("     reliability 255/255, txload 1/255, rxload 1/255");
                sb.AppendLine("  Encapsulation ARPA, loopback not set");
                sb.AppendLine("  " + (p.IsUp ? "Full-duplex, " + (p.Speed >= 1000 ? "1000Mb/s" : "100Mb/s") : "Auto-duplex, Auto-speed") + ", media type is " + (p.Media == "sfp" ? (p.Sfp == "" ? "empty SFP slot" : p.Sfp == "rj45" ? "RJ45 (SFP)" : p.Sfp == "mmf" ? "SFP multimode fibre" : "SFP single-mode fibre") : "RJ45"));
                if (p.Link != null && p.Link.Status == "fault") sb.AppendLine("  Problem: " + p.Link.Problem);
                sb.AppendLine("  5 minute input rate 0 bits/sec, 0 packets/sec");
                sb.AppendLine("  5 minute output rate 0 bits/sec, 0 packets/sec");
                sb.AppendLine("     " + p.RxFrames + " packets input, " + (p.RxFrames * 74) + " bytes, 0 no buffer");
                sb.AppendLine("     " + p.TxFrames + " packets output, " + (p.TxFrames * 74) + " bytes, 0 underruns");
            }
            foreach (var f in d.Ip.Ifs.Where(x => x.IsSvi || x.IsSub || x.IsLoopback))
            {
                if (want != null && !string.Equals(f.Name, want, StringComparison.OrdinalIgnoreCase)) continue;
                found = true;
                sb.AppendLine(f.Name + " is " + (f.Shutdown ? "administratively down" : f.Up ? "up" : "down") + ", line protocol is " + (f.Up ? "up" : "down"));
                sb.AppendLine("  Hardware is " + (f.IsSvi ? "CPU Interface" : f.IsLoopback ? "Loopback" : "802.1Q Virtual LAN") + ", address is " + f.Mac);
                if (f.Description.Length > 0) sb.AppendLine("  Description: " + f.Description);
                if (f.Ip != 0) sb.AppendLine("  Internet address is " + Ip.Str(f.Ip) + "/" + Ip.PrefixOf(f.Mask));
                if (f.IsSub) sb.AppendLine("  Encapsulation 802.1Q Virtual LAN, Vlan ID  " + f.Vlan + (f.NativeSub ? " (native)" : ""));
            }
            if (!found) return "% Invalid input: no interface " + which;
            return sb.ToString().TrimEnd('\n', '\r');
        }

        public static string IfStatus(Device d)
        {
            var sb = new StringBuilder("Port      Name               Status       Vlan       Duplex  Speed Type\n");
            foreach (var p in d.Ports.Where(x => x.IsEthernet))
            {
                string st = p.Shutdown ? "disabled" : p.IsUp ? "connected" : (p.Link != null && p.Link.Status == "fault" ? "err-disabled" : "notconnect");
                string vl = p.Trunk ? "trunk" : p.AccessVlan.ToString();
                sb.Append(p.Short.PadRight(10) + (p.Description.Length > 18 ? p.Description.Substring(0, 18) : p.Description).PadRight(19) + st.PadRight(13) + vl.PadRight(11) + (p.IsUp ? "a-full" : "auto").PadRight(8) + (p.IsUp ? (p.Speed >= 1000 ? "a-1000" : "a-100") : "auto").PadRight(6) + (p.Media == "sfp" ? "SFP" : "10/100BaseTX") + "\n");
            }
            return sb.ToString().TrimEnd('\n');
        }

        // ---------------------------------------------------------------- tables
        public static string Arp(Device d)
        {
            var sb = new StringBuilder("Protocol  Address          Age (min)  Hardware Addr   Type   Interface\n");
            long now = d.P.Sim.Now;
            foreach (var f in d.Ip.Ifs.Where(x => x.Ip != 0 && x.Up && !x.IsLoopback))
                sb.Append("Internet  " + Ip.Str(f.Ip).PadRight(17) + "-".PadLeft(9).PadRight(11) + f.Mac.PadRight(16) + "ARPA   " + f.Name + "\n");
            foreach (var e in d.Ip.ArpEntries())
                sb.Append("Internet  " + Ip.Str(e.Ip).PadRight(17) + ((now - e.Time) / 60000).ToString().PadLeft(9).PadRight(11) + e.Mac.PadRight(16) + "ARPA   " + e.If.Name + "\n");
            return sb.ToString().TrimEnd('\n');
        }

        public static string MacTable(Device d)
        {
            var sb = new StringBuilder("          Mac Address Table\n-------------------------------------------\n\nVlan    Mac Address       Type        Ports\n----    -----------       --------    -----\n\n");
            foreach (var e in d.Bridge.Entries())
                sb.Append(e.Vlan.ToString().PadLeft(4) + "    " + e.Mac.ToLowerInvariant().PadRight(18) + (e.Static ? "STATIC" : "DYNAMIC").PadRight(12) + e.Port.Short + "\n");
            return sb.ToString().TrimEnd('\n');
        }

        public static string Vlans(Device d, bool brief)
        {
            var b = d.Bridge;
            var sb = new StringBuilder("\nVLAN Name                             Status    Ports\n---- -------------------------------- --------- -------------------------------\n");
            foreach (var kv in b.Vlans)
            {
                var ports = d.Ports.Where(p => p.IsEthernet && !p.Trunk && p.AccessVlan == kv.Key).Select(p => p.Short).ToList();
                string status = kv.Key >= 1002 ? "act/unsup" : "active";
                var lines = new List<string>();
                var cur = "";
                foreach (var p in ports)
                {
                    string add = (cur.Length > 0 ? ", " : "") + p;
                    if (cur.Length + add.Length > 31) { lines.Add(cur + ","); cur = p; } else cur += add;
                }
                lines.Add(cur);
                sb.Append(kv.Key.ToString().PadRight(5) + kv.Value.PadRight(33) + status.PadRight(10) + lines[0] + "\n");
                for (int i = 1; i < lines.Count; i++) sb.Append(new string(' ', 48) + lines[i] + "\n");
            }
            if (!brief)
            {
                sb.Append("\nVLAN Type  SAID       MTU   Parent RingNo BridgeNo Stp  BrdgMode Trans1 Trans2\n---- ----- ---------- ----- ------ ------ -------- ---- -------- ------ ------\n");
                foreach (var kv in b.Vlans) sb.Append(kv.Key.ToString().PadRight(5) + "enet  " + (100000 + kv.Key).ToString().PadRight(11) + "1500  -      -      -        -    -        0      0\n");
            }
            return sb.ToString().TrimEnd('\n');
        }

        public static string Trunks(Device d)
        {
            var tr = d.Ports.Where(p => p.Trunk && p.IsUp).ToList();
            if (tr.Count == 0) return "";
            var sb = new StringBuilder("Port        Mode         Encapsulation  Status        Native vlan\n");
            foreach (var p in tr) sb.Append(Ios.ShortIf(p.Name).PadRight(12) + "on           802.1q         trunking      " + p.NativeVlan + "\n");
            sb.Append("\nPort        Vlans allowed on trunk\n");
            foreach (var p in tr) sb.Append(Ios.ShortIf(p.Name).PadRight(12) + (p.Allowed == "all" ? "1-4094" : p.Allowed) + "\n");
            sb.Append("\nPort        Vlans allowed and active in management domain\n");
            foreach (var p in tr) sb.Append(Ios.ShortIf(p.Name).PadRight(12) + VlanList.Compact(d.Bridge.Vlans.Keys.Where(v => v < 1002 && VlanList.Allows(p.Allowed, v))) + "\n");
            sb.Append("\nPort        Vlans in spanning tree forwarding state and not pruned\n");
            foreach (var p in tr) sb.Append(Ios.ShortIf(p.Name).PadRight(12) + VlanList.Compact(d.Bridge.Vlans.Keys.Where(v => v < 1002 && VlanList.Allows(p.Allowed, v) && !p.StpBlocked.Contains(v))) + "\n");
            return sb.ToString().TrimEnd('\n');
        }

        public static string Stp(Device d)
        {
            var b = d.Bridge;
            var sb = new StringBuilder();
            foreach (var v in b.Vlans.Keys.Where(x => x < 1002))
            {
                var ports = d.Ports.Where(p => p.IsEthernet && p.IsUp && p.CarriesVlan(v)).ToList();
                if (ports.Count == 0) continue;
                StpInfo info;
                if (!b.Stp.TryGetValue(v, out info)) info = new StpInfo { RootId = b.BridgeId(v) };
                bool isRoot = info.RootId == b.BridgeId(v);
                string[] rid = info.RootId.Split('.');
                string rootMac = info.RootId.Substring(info.RootId.IndexOf('.') + 1);
                sb.AppendLine("VLAN" + v.ToString("D4"));
                sb.AppendLine("  Spanning tree enabled protocol " + (d.Ios.StpMode == "rapid-pvst" ? "rstp" : "ieee"));
                sb.AppendLine("  Root ID    Priority    " + int.Parse(rid[0]));
                sb.AppendLine("             Address     " + rootMac);
                if (isRoot) sb.AppendLine("             This bridge is the root");
                else sb.AppendLine("             Cost        " + info.Cost + "\n             Port        " + (info.RootPort != null ? (info.RootPort.Index + 1) + "(" + info.RootPort.Name + ")" : "-"));
                sb.AppendLine("             Hello Time  2 sec  Max Age 20 sec  Forward Delay 15 sec");
                sb.AppendLine("");
                sb.AppendLine("  Bridge ID  Priority    " + (b.Priority(v) + v) + "  (priority " + b.Priority(v) + " sys-id-ext " + v + ")");
                sb.AppendLine("             Address     " + b.Mac0);
                sb.AppendLine("             Hello Time  2 sec  Max Age 20 sec  Forward Delay 15 sec");
                sb.AppendLine("             Aging Time  20");
                sb.AppendLine("");
                sb.AppendLine("Interface        Role Sts Cost      Prio.Nbr Type");
                sb.AppendLine("---------------- ---- --- --------- -------- --------------------------------");
                foreach (var p in ports)
                {
                    string role = p.StpBlocked.Contains(v) ? "Altn BLK" : info.RootPort == p ? "Root FWD" : "Desg FWD";
                    sb.AppendLine(p.Short.PadRight(17) + role + " " + (p.Speed >= 1000 ? "4" : "19").PadRight(10) + ("128." + (p.Index + 1)).PadRight(9) + "P2p" + (p.Portfast ? " Edge" : ""));
                }
                sb.AppendLine("");
            }
            return sb.ToString().TrimEnd('\n', '\r');
        }

        public static string Cdp(Device d, bool detail)
        {
            var sb = new StringBuilder();
            if (!d.Ios.Cdp) return "% CDP is not enabled";
            if (!detail) sb.Append("Capability Codes: R - Router, T - Trans Bridge, B - Source Route Bridge\n                  S - Switch, H - Host, I - IGMP, r - Repeater, P - Phone\nDevice ID    Local Intrfce   Holdtme    Capability   Platform    Port ID\n");
            foreach (var p in d.Ports)
            {
                if (p.Link == null || p.Link.Status != "up" || p.IsSerial || p.IsWireless) continue;
                var far = Wiring.FarEnd(p.Link.Other(p));
                if (far == null || far.Dev.Ios == null || !far.Dev.Ios.Cdp) continue;
                var o = far.Dev;
                string cap = o.Kind == "router" ? "R" : "S";
                if (detail)
                {
                    sb.Append("\nDevice ID: " + o.Ios.Hostname + "\nEntry address(es):\n");
                    var ip = o.Ip.Ifs.FirstOrDefault(f => f.Ip != 0 && (f.Port == far || f.IsSvi));
                    if (ip != null) sb.Append("  IP address : " + Ip.Str(ip.Ip) + "\n");
                    sb.Append("Platform: " + Brand.FullModel(o.Kind) + ",  Capabilities: " + (o.Kind == "router" ? "Router" : "Switch") + "\nInterface: " + p.Name + ",  Port ID (outgoing port): " + far.Name + "\nHoldtime: 150\n\nVersion :\n" + Brand.Os + " Software, Version 1.0\n");
                }
                else sb.Append(o.Ios.Hostname.PadRight(13) + Ios.ShortIf(p.Name).PadRight(16) + "150".PadRight(11) + cap.PadLeft(10).PadRight(13) + Brand.ModelOf(o.Kind).PadRight(12) + Ios.ShortIf(far.Name) + "\n");
            }
            return sb.ToString().TrimEnd('\n');
        }

        // ---------------------------------------------------------------- routing
        public static string Routes(Device d)
        {
            var s = d.Ip;
            var sb = new StringBuilder();
            if (!s.Forwarding)
            {
                return s.DefaultGateway != 0 ? "Default gateway is " + Ip.Str(s.DefaultGateway) + "\n\nHost               Gateway           Last Use    Total Uses  Interface\nICMP redirect cache is empty" : "Default gateway is not set\n\nHost               Gateway           Last Use    Total Uses  Interface\nICMP redirect cache is empty";
            }
            sb.Append("Codes: L - local, C - connected, S - static, R - RIP, M - mobile, B - BGP\n       D - EIGRP, EX - EIGRP external, O - OSPF, IA - OSPF inter area\n       N1 - OSPF NSSA external type 1, N2 - OSPF NSSA external type 2\n       E1 - OSPF external type 1, E2 - OSPF external type 2, E - EGP\n       i - IS-IS, L1 - IS-IS level-1, L2 - IS-IS level-2, ia - IS-IS inter area\n       * - candidate default, U - per-user static route, o - ODR\n       P - periodic downloaded static route\n\n");
            var routes = s.Routes();
            var def = routes.Where(r => r.IsDefault).OrderBy(r => r.Ad).FirstOrDefault();
            sb.Append(def != null ? "Gateway of last resort is " + Ip.Str(def.NextHop) + " to network 0.0.0.0\n\n" : "Gateway of last resort is not set\n\n");
            long now = d.P.Sim.Now;
            foreach (var grp in routes.Where(r => !r.IsDefault).GroupBy(r => r.Net & Ip.MaskOf(Ip.ClassfulPrefix(r.Net))).OrderBy(g => g.Key))
            {
                var list = grp.OrderBy(r => r.Net).ThenBy(r => r.Mask).ThenBy(r => r.Code == "L" ? 1 : 0).ToList();
                int classful = Ip.ClassfulPrefix(grp.Key);
                bool subnetted = list.Count > 1 || Ip.PrefixOf(list[0].Mask) != classful;
                if (subnetted)
                {
                    int masks = list.Select(r => r.Mask).Distinct().Count();
                    sb.Append("     " + Ip.Str(grp.Key) + "/" + (masks == 1 ? Ip.PrefixOf(list[0].Mask) : classful) + " is " + (masks == 1 ? "subnetted, " + list.Count + " subnet" + (list.Count == 1 ? "" : "s") : "variably subnetted, " + list.Count + " subnets, " + masks + " masks") + "\n");
                }
                foreach (var r in list) sb.Append(RouteLine(r, now, subnetted) + "\n");
            }
            foreach (var r in routes.Where(r => r.IsDefault)) sb.Append(RouteLine(r, now, false) + "\n");
            return sb.ToString().TrimEnd('\n');
        }

        static string RouteLine(Route r, long now, bool sub)
        {
            string code = r.Code.PadRight(sub ? 8 : 5);
            string net = sub ? Ip.Str(r.Net) + "/" + Ip.PrefixOf(r.Mask) : Ip.Cidr(r.Net, r.Mask);
            switch (r.Code)
            {
                case "C": case "L": return code + net + " is directly connected, " + r.If.Name;
                case "S": case "S*":
                    if (r.NextHop == 0) return code + net + " is directly connected, " + r.If.Name;
                    return code + net + " [" + r.Ad + "/0] via " + Ip.Str(r.NextHop);
                case "R": case "R*":
                    long age = (now - r.Age) / 1000;
                    return code + net + " [120/" + r.Metric + "] via " + Ip.Str(r.NextHop) + ", " + string.Format("{0:00}:{1:00}:{2:00}", age / 3600, (age / 60) % 60, age % 60) + ", " + r.If.Name;
            }
            return code + net;
        }

        public static string Protocols(Device d)
        {
            var r = d.Ip.Rip;
            if (r == null) return "";
            long next = Math.Max(0, (r.LastUpdate + r.UpdateMs - d.P.Sim.Now) / 1000);
            var sb = new StringBuilder();
            sb.AppendLine("Routing Protocol is \"rip\"");
            sb.AppendLine("Sending updates every 30 seconds, next due in " + next + " seconds");
            sb.AppendLine("Invalid after 180 seconds, hold down 180, flushed after 240");
            sb.AppendLine("Outgoing update filter list for all interfaces is not set");
            sb.AppendLine("Incoming update filter list for all interfaces is not set");
            sb.AppendLine("Redistributing: rip");
            sb.AppendLine("Default version control: send version " + (r.Version == 1 ? "1, receive any version" : "2, receive 2"));
            sb.AppendLine("  Interface             Send  Recv  Triggered RIP  Key-chain");
            foreach (var f in d.Ip.Ifs.Where(x => x.Up && r.Enabled(x) && !r.Passive.Contains(x.Name)))
                sb.AppendLine("  " + f.Name.PadRight(22) + (r.Version == 1 ? "1" : "2").PadRight(6) + (r.Version == 1 ? "2 1" : "2").PadRight(6));
            sb.AppendLine("Automatic network summarization is " + (r.AutoSummary && r.Version == 1 ? "" : "not ") + "in effect");
            sb.AppendLine("Maximum path: 4");
            sb.AppendLine("Routing for Networks:");
            foreach (var n in r.Networks) sb.AppendLine("\t" + Ip.Str(n));
            sb.AppendLine("Passive Interface(s):");
            foreach (var p in r.Passive) sb.AppendLine("\t" + p);
            sb.AppendLine("Routing Information Sources:");
            sb.AppendLine("\tGateway         Distance      Last Update");
            foreach (var g in r.Learned.Values.GroupBy(x => x.NextHop))
            {
                long age = (d.P.Sim.Now - g.Max(x => x.Heard)) / 1000;
                sb.AppendLine("\t" + Ip.Str(g.Key).PadRight(16) + "120".PadRight(14) + string.Format("{0:00}:{1:00}:{2:00}", age / 3600, (age / 60) % 60, age % 60));
            }
            sb.Append("Distance: (default is 120)");
            return sb.ToString();
        }

        public static string RipDb(Device d)
        {
            var r = d.Ip.Rip;
            if (r == null) return "";
            var sb = new StringBuilder();
            foreach (var f in d.Ip.Ifs.Where(x => x.Up && r.Enabled(x))) sb.AppendLine(Ip.Cidr(f.Ip & f.Mask, f.Mask) + "    directly connected, " + f.Name);
            foreach (var x in r.Learned.Values) sb.AppendLine(Ip.Cidr(x.Net, x.Mask) + "\n    [" + x.Metric + "] via " + Ip.Str(x.NextHop) + ", " + x.If.Name);
            return sb.ToString().TrimEnd('\n', '\r');
        }

        public static string DhcpBindings(Device d)
        {
            var sb = new StringBuilder("IP address       Client-ID/              Lease expiration        Type\n                 Hardware address\n");
            if (d.Ip.Dhcp != null) foreach (var l in d.Ip.Dhcp.Leases.Values.OrderBy(x => x.Ip)) sb.Append(Ip.Str(l.Ip).PadRight(17) + l.Mac.PadRight(24) + "--".PadRight(24) + "Automatic\n");
            return sb.ToString().TrimEnd('\n');
        }

        public static string DhcpPools(Device d)
        {
            var sb = new StringBuilder();
            if (d.Ip.Dhcp == null) return "";
            foreach (var p in d.Ip.Dhcp.Pools)
            {
                int leased = d.Ip.Dhcp.Leases.Values.Count(l => l.Pool == p.Name);
                uint size = p.Mask == 0 ? 0 : (~p.Mask) - 1;
                sb.AppendLine("\nPool " + p.Name + " :");
                sb.AppendLine(" Utilization mark (high/low)    : 100 / 0");
                sb.AppendLine(" Subnet size (first/next)       : 0 / 0");
                sb.AppendLine(" Total addresses                : " + size);
                sb.AppendLine(" Leased addresses               : " + leased);
                sb.AppendLine(" Excluded addresses             : " + d.Ip.Dhcp.Excluded.Sum(r => (long)(r[1] - r[0] + 1)));
                sb.AppendLine(" Pending event                  : none");
                sb.AppendLine(" 1 subnet is currently in the pool :");
                sb.AppendLine(" Current index        IP address range                    Leased/Excluded/Total");
                if (p.Mask != 0) sb.AppendLine(" " + Ip.Str(p.Network + 1).PadRight(21) + (Ip.Str(p.Network + 1) + "      - " + Ip.Str(Ip.BroadcastOf(p.Network, p.Mask) - 1)).PadRight(36) + leased + " / 0 / " + size);
            }
            return sb.ToString().TrimEnd('\n', '\r');
        }

        public static string NatTable(Device d)
        {
            var sb = new StringBuilder("Pro  Inside global      Inside local       Outside local      Outside global\n");
            foreach (var e in d.Ip.Nat.Entries())
            {
                Func<uint, int, string> ap = (ip, port) => (ip == 0 ? "---" : Ip.Str(ip) + (port != 0 || e.Proto != "---" ? ":" + port : "")).PadRight(19);
                if (e.Static) sb.Append("---  " + Ip.Str(e.InGlobal).PadRight(19) + Ip.Str(e.InLocal).PadRight(19) + "---".PadRight(19) + "---\n");
                else sb.Append(e.Proto.PadRight(5) + ap(e.InGlobal, e.InGlobalPort) + ap(e.InLocal, e.InLocalPort) + ap(e.OutIp, e.OutPort) + ap(e.OutIp, e.OutPort).TrimEnd() + "\n");
            }
            return sb.ToString().TrimEnd('\n');
        }

        public static string NatStats(Device d)
        {
            var n = d.Ip.Nat;
            var sb = new StringBuilder();
            sb.AppendLine("Total translations: " + n.Entries().Count + " (" + n.Rules.Count(r => r.IsStatic) + " static, " + n.Table.Count + " dynamic, " + n.Table.Count + " extended)");
            sb.AppendLine("Outside Interfaces: " + string.Join(", ", d.Ip.Ifs.Where(f => f.Nat == 2).Select(f => f.Name).ToArray()));
            sb.AppendLine("Inside Interfaces: " + string.Join(", ", d.Ip.Ifs.Where(f => f.Nat == 1).Select(f => f.Name).ToArray()));
            sb.AppendLine("Hits: " + n.Translations + "  Misses: " + n.Misses);
            sb.AppendLine("Expired translations: 0");
            sb.AppendLine("Dynamic mappings:");
            foreach (var r in n.Rules.Where(x => !x.IsStatic)) sb.AppendLine("-- Inside Source\naccess-list " + r.Acl + (r.Interface.Length > 0 ? " interface " + r.Interface : " pool " + r.Pool) + " refcount " + n.Table.Count);
            return sb.ToString().TrimEnd('\n', '\r');
        }

        public static string AccessLists(Device d, string name)
        {
            var sb = new StringBuilder();
            foreach (var a in d.Ip.Acls.Values.OrderBy(x => x.IsNumbered ? int.Parse(x.Name) : 100000).ThenBy(x => x.Name))
            {
                if (name != null && !string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                if (a.Name == "HOME" && d.Kind == "homerouter") continue;
                sb.AppendLine((a.Extended ? "Extended" : "Standard") + " IP access list " + a.Name);
                foreach (var l in a.Lines)
                {
                    if (l.Remark != null) continue;
                    sb.AppendLine("    " + l.Seq + " " + l.ShowText() + (l.Matches > 0 ? " (" + l.Matches + " match(es))" : ""));
                }
            }
            return sb.ToString().TrimEnd('\n', '\r');
        }

        public static string RunInterface(CliCtx c)
        {
            string n = Ios.NormalizeIfName(c.Arg(0));
            string block = n == null ? null : Persist.InterfaceBlock(c.D, n);
            if (block == null) return "% Invalid input: no interface " + c.Arg(0);
            return "Building configuration...\n\nCurrent configuration : " + block.Length + " bytes\n!\n" + block + "end";
        }
    }
}
