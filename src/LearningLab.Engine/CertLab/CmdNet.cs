// Cert Lab - network programs: ip, nmcli, nmtui, ping, ss, host / dig / nslookup, tracepath and firewall-cmd.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CertLab
{
    public static partial class Cmd
    {
        static void RegNet()
        {
            P("ip", Ip); P("nmcli", Nmcli); P("nmtui", (sh, a, io) => { io.E("nmtui draws a full-screen menu that Cert Lab's terminal does not show. Use nmcli: the same settings, one command each (see man nmcli, or the Networking cheat sheet)."); return 1; });
            P("nmtui-edit", (sh, a, io) => { io.E("Use nmcli connection modify in Cert Lab."); return 1; }); P("nmtui-hostname", (sh, a, io) => { io.E("Use hostnamectl set-hostname in Cert Lab."); return 1; });
            P("ping", Ping); P("ss", Ss); P("netstat", Ss); P("host", Host); P("dig", Dig); P("nslookup", Nslookup); P("tracepath", Tracepath); P("traceroute", Tracepath); P("arping", (sh, a, io) => { io.L("ARPING " + a.Last() + " from " + (sh.M.FirstV4() ?? "0.0.0.0") + " enp1s0\nUnicast reply from " + a.Last() + " [52:54:00:12:34:56]  0.612ms\nSent 1 probes (1 broadcast(s))\nReceived 1 response(s)"); return 0; });
            P("firewall-cmd", FirewallCmd); P("firewall-offline-cmd", FirewallCmd);
            RegSsh(); RegHttp();
        }

        // ------------------------------------------------------------ ip
        static int Ip(Sh sh, List<string> a, Io io)
        {
            var m = sh.M;
            var args = a.Skip(1).ToList();
            bool brief = false; int fam = 0;
            while (args.Count > 0 && args[0].StartsWith("-", StringComparison.Ordinal))
            {
                var o = args[0]; args.RemoveAt(0);
                if (o == "-br" || o == "-brief") brief = true; else if (o == "-4") fam = 4; else if (o == "-6") fam = 6; else if (o == "-c" || o == "-color" || o == "-s" || o == "-d") { } else { io.E("Option \"" + o + "\" is unknown, try \"ip -help\"."); return 255; }
            }
            if (args.Count == 0) { io.E("Usage: ip [ OPTIONS ] OBJECT { COMMAND | help }\nwhere  OBJECT := { address | link | route | neighbor | ... }"); return 255; }
            string obj = args[0]; args.RemoveAt(0);
            Func<string, string, bool> ab = (x, full) => x.Length > 0 && full.StartsWith(x, StringComparison.Ordinal);
            if (ab(obj, "address") || obj == "a" || obj == "addr")
            {
                string verb = args.Count > 0 ? args[0] : "show";
                if (ab(verb, "add") || ab(verb, "delete") || verb == "del")
                {
                    if (sh.P.Cr.Uid != 0) { io.E("RTNETLINK answers: Operation not permitted"); return 2; }
                    if (args.Count < 4 || args[2] != "dev") { io.E("Not enough information: \"dev\" argument is required."); return 1; }
                    var nif = m.Iface(args[3]);
                    if (nif == null) { io.E("Cannot find device \"" + args[3] + "\""); return 1; }
                    string addr = args[1].Contains("/") ? args[1] : args[1] + "/32";
                    var list = addr.Contains(":") ? nif.V6 : nif.V4;
                    if (ab(verb, "add")) { if (list.Contains(addr)) { io.E("RTNETLINK answers: File exists"); return 2; } list.Add(addr); }
                    else if (!list.Remove(addr)) { io.E("RTNETLINK answers: Cannot assign requested address"); return 2; }
                    return 0;
                }
                string dev = args.Count > 2 && args[1] == "dev" ? args[2] : (args.Count > 1 && args[0] == "show" ? args[1] : null);
                if (dev != null && dev != "lo" && m.Iface(dev) == null) { io.E("Device \"" + dev + "\" does not exist."); return 1; }
                if (brief)
                {
                    if (dev == null || dev == "lo") io.L("lo               UNKNOWN        " + (fam == 6 ? "::1/128" : fam == 4 ? "127.0.0.1/8" : "127.0.0.1/8 ::1/128") + " ");
                    foreach (var i in m.Ifs.Where(x => dev == null || x.Name == dev))
                    {
                        var addrs = (fam == 6 ? new List<string>() : i.V4).Concat(fam == 4 ? new List<string>() : i.V6.OrderBy(x => x.StartsWith("fe80", StringComparison.Ordinal) ? 1 : 0).ToList());
                        io.L(i.Name.PadRight(16) + " " + (i.Up && i.Carrier ? "UP" : "DOWN").PadRight(14) + " " + string.Join(" ", addrs.ToArray()) + " ");
                    }
                    return 0;
                }
                int n = 1;
                if (dev == null || dev == "lo")
                {
                    io.L("1: lo: <LOOPBACK,UP,LOWER_UP> mtu 65536 qdisc noqueue state UNKNOWN group default qlen 1000\n    link/loopback 00:00:00:00:00:00 brd 00:00:00:00:00:00" + (fam != 6 ? "\n    inet 127.0.0.1/8 scope host lo\n       valid_lft forever preferred_lft forever" : "") + (fam != 4 ? "\n    inet6 ::1/128 scope host noprefixroute \n       valid_lft forever preferred_lft forever" : ""));
                }
                foreach (var i in m.Ifs)
                {
                    n++;
                    if (dev != null && i.Name != dev) continue;
                    bool up = i.Up && i.Carrier;
                    var sb = new StringBuilder();
                    sb.Append(n + ": " + i.Name + ": <BROADCAST,MULTICAST" + (i.Up ? ",UP" : "") + (up ? ",LOWER_UP" : "") + "> mtu 1500 qdisc fq_codel state " + (up ? "UP" : "DOWN") + " group default qlen 1000\n    link/ether " + i.Mac + " brd ff:ff:ff:ff:ff:ff");
                    if (fam != 6) foreach (var v4 in i.V4) sb.Append("\n    inet " + v4 + " brd " + Broadcast(v4) + " scope global noprefixroute " + i.Name + "\n       valid_lft forever preferred_lft forever");
                    if (fam != 4) foreach (var v6 in i.V6.OrderBy(x => x.StartsWith("fe80", StringComparison.Ordinal) ? 1 : 0)) sb.Append("\n    inet6 " + v6 + " scope " + (v6.StartsWith("fe80", StringComparison.Ordinal) ? "link" : "global") + " noprefixroute \n       valid_lft forever preferred_lft forever");
                    io.L(sb.ToString());
                }
                return 0;
            }
            if (ab(obj, "link") || obj == "l")
            {
                string verb = args.Count > 0 ? args[0] : "show";
                if (verb == "set")
                {
                    if (sh.P.Cr.Uid != 0) { io.E("RTNETLINK answers: Operation not permitted"); return 2; }
                    string dev = args.Count > 2 && args[1] == "dev" ? args[2] : (args.Count > 1 ? args[1] : null);
                    var nif = dev == null ? null : m.Iface(dev);
                    if (nif == null) { io.E("Cannot find device \"" + dev + "\""); return 1; }
                    if (args.Contains("up")) nif.Up = true; if (args.Contains("down")) nif.Up = false;
                    return 0;
                }
                if (brief) { io.L("lo               UNKNOWN        00:00:00:00:00:00 <LOOPBACK,UP,LOWER_UP> "); foreach (var i in m.Ifs) io.L(i.Name.PadRight(16) + " " + (i.Up ? "UP" : "DOWN").PadRight(14) + " " + i.Mac + " <BROADCAST,MULTICAST" + (i.Up ? ",UP,LOWER_UP" : "") + "> "); return 0; }
                io.L("1: lo: <LOOPBACK,UP,LOWER_UP> mtu 65536 qdisc noqueue state UNKNOWN mode DEFAULT group default qlen 1000\n    link/loopback 00:00:00:00:00:00 brd 00:00:00:00:00:00");
                int n = 1;
                foreach (var i in m.Ifs) { n++; io.L(n + ": " + i.Name + ": <BROADCAST,MULTICAST" + (i.Up ? ",UP,LOWER_UP" : "") + "> mtu 1500 qdisc fq_codel state " + (i.Up ? "UP" : "DOWN") + " mode DEFAULT group default qlen 1000\n    link/ether " + i.Mac + " brd ff:ff:ff:ff:ff:ff"); }
                return 0;
            }
            if (ab(obj, "route") || obj == "r" || obj == "ro")
            {
                if (fam == 6)
                {
                    foreach (var i in m.Ifs) foreach (var v6 in i.V6) io.L(Net6(v6) + " dev " + i.Name + " proto kernel metric 100 pref medium");
                    foreach (var i in m.Ifs.Where(x => x.Up && x.Gw6.Length > 0)) io.L("default via " + i.Gw6 + " dev " + i.Name + " proto static metric 100 pref medium");
                    return 0;
                }
                foreach (var i in m.Ifs.Where(x => x.Up && x.Gw4.Length > 0)) io.L("default via " + i.Gw4 + " dev " + i.Name + " proto static metric 100 ");
                foreach (var i in m.Ifs.Where(x => x.Up)) foreach (var v4 in i.V4) io.L(NetOf(v4) + " dev " + i.Name + " proto kernel scope link src " + v4.Split('/')[0] + " metric 100 ");
                return 0;
            }
            if (ab(obj, "neighbour") || obj == "neigh" || obj == "n")
            {
                if (m.FirstV4() != null) io.L(m.Net.UtilityIp + " dev enp1s0 lladdr 52:54:00:aa:bb:fe REACHABLE");
                return 0;
            }
            io.E("Object \"" + obj + "\" is unknown, try \"ip help\".");
            return 255;
        }
        static string Broadcast(string cidr)
        {
            var p = cidr.Split('/'); int bits = p.Length > 1 ? int.Parse(p[1]) : 32;
            uint ip = LabNet.IpNum(p[0]); uint mask = bits == 0 ? 0 : 0xFFFFFFFFu << (32 - bits);
            uint b = (ip & mask) | ~mask;
            return string.Join(".", new[] { b >> 24, (b >> 16) & 255, (b >> 8) & 255, b & 255 }.Select(x => x.ToString()).ToArray());
        }
        static string NetOf(string cidr)
        {
            var p = cidr.Split('/'); int bits = p.Length > 1 ? int.Parse(p[1]) : 32;
            uint ip = LabNet.IpNum(p[0]); uint mask = bits == 0 ? 0 : 0xFFFFFFFFu << (32 - bits);
            uint n = ip & mask;
            return string.Join(".", new[] { n >> 24, (n >> 16) & 255, (n >> 8) & 255, n & 255 }.Select(x => x.ToString()).ToArray()) + "/" + bits;
        }
        static string Net6(string cidr)
        {
            if (cidr.StartsWith("fe80", StringComparison.Ordinal)) return "fe80::/64";
            string addr = cidr.Split('/')[0], bits = cidr.Contains("/") ? cidr.Split('/')[1] : "128";
            string pre = addr.Substring(0, addr.LastIndexOf(':') + 1);
            if (!pre.EndsWith("::", StringComparison.Ordinal)) pre += ":";
            return pre + "/" + bits;
        }

        // ------------------------------------------------------------ nmcli
        static readonly Dictionary<string, string> NmAlias = new Dictionary<string, string>
        {
            { "con-name", "connection.id" }, { "ifname", "connection.interface-name" }, { "type", "connection.type" }, { "autoconnect", "connection.autoconnect" },
            { "ip4", "ipv4.addresses" }, { "gw4", "ipv4.gateway" }, { "ip6", "ipv6.addresses" }, { "gw6", "ipv6.gateway" },
        };

        static int Nmcli(Sh sh, List<string> a, Io io)
        {
            var m = sh.M;
            var args = a.Skip(1).ToList();
            string fields = null; bool terse = false, getValues = false, pretty = false;
            while (args.Count > 0 && args[0].StartsWith("-", StringComparison.Ordinal))
            {
                var o = args[0]; args.RemoveAt(0);
                if ((o == "-f" || o == "--fields") && args.Count > 0) { fields = args[0]; args.RemoveAt(0); }
                else if ((o == "-g" || o == "--get-values") && args.Count > 0) { fields = args[0]; args.RemoveAt(0); getValues = true; terse = true; }
                else if (o == "-t" || o == "--terse") terse = true;
                else if (o == "-p" || o == "--pretty") pretty = true;
                else if (o == "-a" || o == "--ask" || o == "-w" || o == "--wait" || o == "-c" || o == "--colors") { if ((o == "-w" || o == "-c") && args.Count > 0) args.RemoveAt(0); }
                else { io.E("Error: Option '" + o + "' is unknown, try 'nmcli -help'."); return 2; }
            }
            bool nmUp = m.ServiceActive("NetworkManager.service");
            if (!nmUp && !(args.Count > 0 && args[0].StartsWith("g", StringComparison.Ordinal))) { io.E("Error: NetworkManager is not running."); return 8; }
            Func<string, string, bool> ab = (x, full) => x.Length > 0 && full.StartsWith(x, StringComparison.Ordinal);
            if (args.Count == 0)
            {
                foreach (var i in m.Ifs) io.L(i.Name + ": " + (i.V4.Count > 0 || i.Conn.Length > 0 ? "connected to " + i.Conn : "disconnected") + "\n        \"Red Hat Virtio 1.0\"\n        ethernet (virtio_net), " + i.Mac.ToUpperInvariant() + ", hw, mtu 1500\n        ip4 default\n" + string.Join("", i.V4.Select(v => "        inet4 " + v + "\n").ToArray()) + (i.Gw4.Length > 0 ? "        route4 default via " + i.Gw4 + " metric 100\n" : "") + string.Join("", i.V6.Select(v => "        inet6 " + v + "\n").ToArray()));
                io.L("lo: connected (externally) to lo\n        \"lo\"\n        loopback (unknown), 00:00:00:00:00:00, sw, mtu 65536\n");
                io.L("DNS configuration:\n" + string.Join("", m.Ifs.Where(x => x.Dns.Count > 0).Select(x => "        servers: " + string.Join(" ", x.Dns.ToArray()) + "\n" + (x.Search.Count > 0 ? "        domains: " + string.Join(" ", x.Search.ToArray()) + "\n" : "") + "        interface: " + x.Name + "\n").ToArray()));
                io.L("Use \"nmcli device show\" to get complete information about known devices and\n\"nmcli connection show\" to get an overview on active connection profiles.\n\nConsult nmcli(1) and nmcli-examples(7) manual pages for complete usage details.");
                return 0;
            }
            string obj = args[0]; args.RemoveAt(0);
            if (ab(obj, "general"))
            {
                string verb = args.Count > 0 ? args[0] : "status";
                if (ab(verb, "hostname"))
                {
                    if (args.Count < 2) { io.L(m.HostnameNow); return 0; }
                    if (sh.P.Cr.Uid != 0) { io.E("Error: Insufficient privileges."); return 1; }
                    m.SysWrite("/etc/hostname", args[1] + "\n"); m.Hostname = args[1];
                    return 0;
                }
                if (!nmUp) { io.E("Error: NetworkManager is not running."); return 8; }
                io.L("STATE      CONNECTIVITY  WIFI-HW  WIFI     WWAN-HW  WWAN    \nconnected  full          missing  enabled  missing  enabled ");
                return 0;
            }
            if (ab(obj, "networking")) { io.L("enabled"); return 0; }
            if (ab(obj, "device"))
            {
                string verb = args.Count > 0 ? args[0] : "status";
                if (ab(verb, "status"))
                {
                    var rows = new List<string[]> { new[] { "DEVICE", "TYPE", "STATE", "CONNECTION" } };
                    foreach (var i in m.Ifs) rows.Add(new[] { i.Name, "ethernet", i.Conn.Length > 0 ? "connected" : (i.Up ? "disconnected" : "unavailable"), i.Conn.Length > 0 ? i.Conn : "--" });
                    rows.Add(new[] { "lo", "loopback", "connected (externally)", "lo" });
                    if (fields != null) { var fs = fields.Split(',').Select(f => f.ToUpperInvariant()).ToList(); rows = rows.Select(r => fs.Select(f => r[Math.Max(0, Array.IndexOf(new[] { "DEVICE", "TYPE", "STATE", "CONNECTION" }, f))]).ToArray()).ToList(); }
                    NmTable(io, rows, terse);
                    return 0;
                }
                if (ab(verb, "show"))
                {
                    foreach (var i in m.Ifs.Where(x => args.Count < 2 || x.Name == args[1]))
                    {
                        var kv = new List<string[]> { new[] { "GENERAL.DEVICE", i.Name }, new[] { "GENERAL.TYPE", "ethernet" }, new[] { "GENERAL.HWADDR", i.Mac.ToUpperInvariant() }, new[] { "GENERAL.MTU", "1500" }, new[] { "GENERAL.STATE", i.Conn.Length > 0 ? "100 (connected)" : "30 (disconnected)" }, new[] { "GENERAL.CONNECTION", i.Conn.Length > 0 ? i.Conn : "--" } };
                        for (int k = 0; k < i.V4.Count; k++) kv.Add(new[] { "IP4.ADDRESS[" + (k + 1) + "]", i.V4[k] });
                        kv.Add(new[] { "IP4.GATEWAY", i.Gw4.Length > 0 ? i.Gw4 : "--" });
                        for (int k = 0; k < i.Dns.Count; k++) kv.Add(new[] { "IP4.DNS[" + (k + 1) + "]", i.Dns[k] });
                        for (int k = 0; k < i.V6.Count; k++) kv.Add(new[] { "IP6.ADDRESS[" + (k + 1) + "]", i.V6[k] });
                        kv.Add(new[] { "IP6.GATEWAY", i.Gw6.Length > 0 ? i.Gw6 : "--" });
                        foreach (var x in kv) if (fields == null || fields.Split(',').Any(f => x[0].StartsWith(f, StringComparison.Ordinal))) io.L(getValues ? x[1] : terse ? x[0] + ":" + x[1] : (x[0] + ":").PadRight(40) + x[1]);
                        if (!terse) io.L("");
                    }
                    return 0;
                }
                if (ab(verb, "connect") || ab(verb, "reapply"))
                {
                    if (args.Count < 2) { io.E("Error: No interface specified."); return 2; }
                    var c = m.NmConns().FirstOrDefault(x => x.Iface == args[1] && x.Autoconnect) ?? m.NmConns().FirstOrDefault(x => x.Iface == args[1]);
                    if (c == null) { io.E("Error: Connection activation failed: No suitable connection found."); return 4; }
                    string err = m.ConnUp(c); if (err != null) { io.E(err); return 4; }
                    io.L("Device '" + args[1] + "' successfully activated with '" + c.Uuid + "'.");
                    return 0;
                }
                if (ab(verb, "disconnect")) { if (args.Count < 2) return 2; m.ConnDown(args[1]); io.L("Device '" + args[1] + "' successfully disconnected."); return 0; }
                io.E("Error: argument '" + verb + "' not understood. Try passing --help instead.");
                return 2;
            }
            if (ab(obj, "connection"))
            {
                string verb = args.Count > 0 ? args[0] : "show";
                var rest = args.Skip(1).ToList();
                var conns = m.NmConns();
                Func<string, NmConn> find = id => conns.FirstOrDefault(x => x.Id == id || x.Uuid == id || x.File == id);
                bool root = sh.P.Cr.Uid == 0 || sh.P.Cr.InGroup(10);
                if (ab(verb, "show"))
                {
                    if (rest.Count > 0 && rest[0] != "--active")
                    {
                        string id = rest[0] == "id" || rest[0] == "uuid" ? (rest.Count > 1 ? rest[1] : "") : rest[0];
                        var c = find(id);
                        if (c == null) { io.E("Error: " + id + " - no such connection profile."); return 10; }
                        var props = NmProps(m, c);
                        // the fields in the order asked for (-g ipv4.addresses,ipv4.gateway)
                        var shown = fields == null ? props : fields.Split(',').SelectMany(f => props.Where(p => p[0] == f || p[0].StartsWith(f + ".", StringComparison.Ordinal) || (f.EndsWith(".", StringComparison.Ordinal) && p[0].StartsWith(f, StringComparison.Ordinal)))).Distinct().ToList();
                        foreach (var p in shown)
                            io.L(getValues ? p[1] : terse ? p[0] + ":" + p[1] : (p[0] + ":").PadRight(40) + p[1]);
                        return 0;
                    }
                    var rows = new List<string[]> { new[] { "NAME", "UUID", "TYPE", "DEVICE" } };
                    bool active = rest.Contains("--active");
                    foreach (var c in conns.OrderBy(x => m.Ifs.Any(i => i.Conn == x.Id) ? 0 : 1))
                    {
                        var dev = m.Ifs.FirstOrDefault(i => i.Conn == c.Id);
                        if (active && dev == null) continue;
                        rows.Add(new[] { c.Id, c.Uuid, c.Type, dev == null ? "--" : dev.Name });
                    }
                    rows.Insert(rows.Count(r => r[3] != "--" && r[0] != "NAME") + 1, new[] { "lo", "8b4c0b9a-2b6a-44d2-9b1c-5b1b2d1e0f1a", "loopback", "lo" });
                    if (fields != null) { var fs = fields.Split(',').Select(f => f.ToUpperInvariant()).ToList(); rows = rows.Select(r => fs.Select(f => r[Math.Max(0, Array.IndexOf(new[] { "NAME", "UUID", "TYPE", "DEVICE" }, f))]).ToArray()).ToList(); }
                    NmTable(io, rows, terse);
                    return 0;
                }
                if (!root && (ab(verb, "add") || ab(verb, "modify") || verb == "mod" || ab(verb, "delete") || ab(verb, "up") || ab(verb, "down"))) { io.E("Error: Failed to " + (ab(verb, "add") ? "add new" : "modify") + " connection: Insufficient privileges."); return 1; }
                if (ab(verb, "add"))
                {
                    var c = new NmConn { Uuid = m.NewUuid(), V6Method = "auto" };
                    string err = NmApply(m, c, rest, true);
                    if (err != null) { io.E(err); return 2; }
                    if (c.Id.Length == 0) c.Id = c.Type + "-" + (c.Iface.Length > 0 ? c.Iface : "1");
                    if (c.V4Addrs.Count > 0 && !rest.Any(x => x == "ipv4.method")) c.V4Method = "manual";
                    if (c.V4Method == "manual" && c.V4Addrs.Count == 0) { io.E("Error: Failed to add '" + c.Id + "' connection: ipv4.addresses: this property cannot be empty for 'method=manual'"); return 2; }
                    m.SaveNm(c);
                    io.L("Connection '" + c.Id + "' (" + c.Uuid + ") successfully added.");
                    var dev = m.Iface(c.Iface);
                    if (c.Autoconnect && dev != null && dev.Conn.Length == 0) m.ConnUp(c);
                    return 0;
                }
                if (ab(verb, "modify") || verb == "mod")
                {
                    if (rest.Count == 0) { io.E("Error: No connection specified."); return 2; }
                    var c = find(rest[0]);
                    if (c == null) { io.E("Error: unknown connection '" + rest[0] + "'."); return 10; }
                    string oldFile = c.File, oldId = c.Id;
                    string err = NmApply(m, c, rest.Skip(1).ToList(), false);
                    if (err != null) { io.E(err.Replace("Error: ", "Error: Failed to modify connection '" + oldId + "': ")); return 2; }
                    if (c.V4Method == "manual" && c.V4Addrs.Count == 0) { io.E("Error: Failed to modify connection '" + oldId + "': ipv4.addresses: this property cannot be empty for 'method=manual'"); return 2; }
                    m.SaveNm(c);
                    if (c.Id != oldId) foreach (var i in m.Ifs) if (i.Conn == oldId) i.Conn = c.Id;
                    return 0;
                }
                if (ab(verb, "up"))
                {
                    if (rest.Count == 0) { io.E("Error: No connection specified."); return 2; }
                    var c = find(rest[0] == "id" && rest.Count > 1 ? rest[1] : rest[0]);
                    if (c == null) { io.E("Error: unknown connection '" + rest[0] + "'."); return 10; }
                    string err = m.ConnUp(c);
                    if (err != null) { io.E(err); return 4; }
                    io.L("Connection successfully activated (D-Bus active path: /org/freedesktop/NetworkManager/ActiveConnection/" + (m.Rand(20) + 2) + ")");
                    return 0;
                }
                if (ab(verb, "down"))
                {
                    var c = rest.Count > 0 ? find(rest[0]) : null;
                    var dev = c == null ? null : m.Ifs.FirstOrDefault(i => i.Conn == c.Id);
                    if (c == null || dev == null) { io.E("Error: '" + (rest.Count > 0 ? rest[0] : "") + "' is not an active connection.\nError: no active connection provided."); return 10; }
                    m.ConnDown(dev.Name);
                    io.L("Connection '" + c.Id + "' successfully deactivated (D-Bus active path: /org/freedesktop/NetworkManager/ActiveConnection/2)");
                    return 0;
                }
                if (ab(verb, "delete"))
                {
                    int st = 0;
                    foreach (var id in rest)
                    {
                        var c = find(id);
                        if (c == null) { io.E("Error: unknown connection '" + id + "'."); st = 10; continue; }
                        var dev = m.Ifs.FirstOrDefault(i => i.Conn == c.Id);
                        if (dev != null) m.ConnDown(dev.Name);
                        m.V.RemoveG(c.File);
                        io.L("Connection '" + c.Id + "' (" + c.Uuid + ") successfully deleted.");
                    }
                    return st;
                }
                if (ab(verb, "reload") || ab(verb, "load")) return 0;
                if (ab(verb, "edit")) { io.E("Error: 'nmcli connection edit' has its own interactive shell that Cert Lab does not include; use nmcli connection modify."); return 2; }
                io.E("Error: argument '" + verb + "' not understood. Try passing --help instead.");
                return 2;
            }
            if (ab(obj, "radio")) { io.L("WIFI-HW  WIFI     WWAN-HW  WWAN    \nmissing  enabled  missing  enabled "); return 0; }
            io.E("Error: argument '" + obj + "' not understood. Try passing --help instead.");
            return 2;
        }

        static void NmTable(Io io, List<string[]> rows, bool terse)
        {
            if (terse) { foreach (var r in rows.Skip(1)) io.L(string.Join(":", r)); return; }
            int n = rows[0].Length; var w = new int[n];
            foreach (var r in rows) for (int i = 0; i < n; i++) w[i] = Math.Max(w[i], r[i].Length);
            foreach (var r in rows) io.L(string.Join("  ", r.Select((x, i) => x.PadRight(w[i])).ToArray()) + " ");
        }

        static List<string[]> NmProps(Machine m, NmConn c)
        {
            var dev = m.Ifs.FirstOrDefault(i => i.Conn == c.Id);
            var l = new List<string[]>
            {
                new[] { "connection.id", c.Id }, new[] { "connection.uuid", c.Uuid }, new[] { "connection.stable-id", "--" }, new[] { "connection.type", c.Type == "ethernet" ? "802-3-ethernet" : c.Type },
                new[] { "connection.interface-name", c.Iface.Length > 0 ? c.Iface : "--" }, new[] { "connection.autoconnect", c.Autoconnect ? "yes" : "no" }, new[] { "connection.autoconnect-priority", c.AutoPrio.ToString() },
                new[] { "connection.zone", "--" },
                new[] { "ipv4.method", c.V4Method }, new[] { "ipv4.dns", string.Join(",", c.V4Dns.ToArray()) }, new[] { "ipv4.dns-search", string.Join(",", c.V4Search.ToArray()) },
                new[] { "ipv4.addresses", c.V4Addrs.Count > 0 ? string.Join(", ", c.V4Addrs.ToArray()) : "--" }, new[] { "ipv4.gateway", c.V4Gw.Length > 0 ? c.V4Gw : "--" },
                new[] { "ipv4.ignore-auto-dns", c.V4IgnoreAutoDns ? "yes" : "no" }, new[] { "ipv4.never-default", c.V4NeverDefault ? "yes" : "no" },
                new[] { "ipv6.method", c.V6Method }, new[] { "ipv6.dns", string.Join(",", c.V6Dns.ToArray()) }, new[] { "ipv6.addresses", c.V6Addrs.Count > 0 ? string.Join(", ", c.V6Addrs.ToArray()) : "--" }, new[] { "ipv6.gateway", c.V6Gw.Length > 0 ? c.V6Gw : "--" },
            };
            if (dev != null)
            {
                l.Add(new[] { "GENERAL.NAME", c.Id }); l.Add(new[] { "GENERAL.DEVICES", dev.Name }); l.Add(new[] { "GENERAL.STATE", "activated" });
                for (int k = 0; k < dev.V4.Count; k++) l.Add(new[] { "IP4.ADDRESS[" + (k + 1) + "]", dev.V4[k] });
                l.Add(new[] { "IP4.GATEWAY", dev.Gw4.Length > 0 ? dev.Gw4 : "--" });
                for (int k = 0; k < dev.Dns.Count; k++) l.Add(new[] { "IP4.DNS[" + (k + 1) + "]", dev.Dns[k] });
            }
            return l;
        }

        static bool ValidCidr(string s, bool six)
        {
            var p = s.Split('/');
            if (six) return Regex.IsMatch(p[0], "^[0-9a-fA-F:]+$") && p[0].Contains(":") && (p.Length == 1 || Regex.IsMatch(p[1], "^\\d{1,3}$") && int.Parse(p[1]) <= 128);
            return LabNet.IsIp(p[0]) && (p.Length == 1 || Regex.IsMatch(p[1], "^\\d{1,2}$") && int.Parse(p[1]) <= 32);
        }

        /// <summary>nmcli property settings: name value, +name value (add), -name value (remove).</summary>
        static string NmApply(Machine m, NmConn c, List<string> a, bool adding)
        {
            for (int i = 0; i < a.Count; i++)
            {
                string k = a[i];
                if (i + 1 >= a.Count) return "Error: value for '" + k + "' is missing.";
                string v = a[++i];
                string op = "=";
                if (k.StartsWith("+", StringComparison.Ordinal) || k.StartsWith("-", StringComparison.Ordinal)) { op = k.Substring(0, 1); k = k.Substring(1); }
                string key;
                if (!NmAlias.TryGetValue(k, out key)) key = k;
                if (key == "ipv4.address") key = "ipv4.addresses";
                if (key == "ipv6.address") key = "ipv6.addresses";
                var list = v.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).ToList();
                Func<List<string>, List<string>, List<string>> merge = (cur, nv) => op == "+" ? cur.Concat(nv.Where(x => !cur.Contains(x))).ToList() : op == "-" ? cur.Where(x => !nv.Contains(x)).ToList() : nv;
                switch (key)
                {
                    case "connection.id": c.Id = v; break;
                    case "connection.interface-name": c.Iface = v; break;
                    case "connection.type": c.Type = v == "802-3-ethernet" ? "ethernet" : v; if (c.Type != "ethernet") return "Error: invalid connection type; Cert Lab's machines have Ethernet only."; break;
                    case "connection.autoconnect": c.Autoconnect = v == "yes" || v == "true" || v == "on"; break;
                    case "connection.autoconnect-priority": int.TryParse(v, out c.AutoPrio); break;
                    case "ipv4.method": if (!new[] { "auto", "manual", "disabled", "link-local", "shared" }.Contains(v)) return "Error: invalid property value 'ipv4.method': '" + v + "' not among [auto, link-local, manual, shared, disabled]"; c.V4Method = v; break;
                    case "ipv4.addresses":
                        foreach (var x in list) if (!ValidCidr(x, false)) return "Error: invalid IP address: Invalid IPv4 address '" + x + "'";
                        c.V4Addrs = merge(c.V4Addrs, list.Select(x => x.Contains("/") ? x : x + "/32").ToList());
                        if (op == "=" && list.Count > 0 && !a.Contains("ipv4.method") && adding) c.V4Method = "manual";
                        break;
                    case "ipv4.gateway": if (v.Length > 0 && !LabNet.IsIp(v)) return "Error: invalid IP address: Invalid IPv4 address '" + v + "'"; c.V4Gw = v; break;
                    case "ipv4.dns": foreach (var x in list) if (!LabNet.IsIp(x)) return "Error: invalid IP address: Invalid IPv4 address '" + x + "'"; c.V4Dns = merge(c.V4Dns, list); break;
                    case "ipv4.dns-search": c.V4Search = merge(c.V4Search, list); break;
                    case "ipv4.ignore-auto-dns": c.V4IgnoreAutoDns = v == "yes" || v == "true"; break;
                    case "ipv4.never-default": c.V4NeverDefault = v == "yes" || v == "true"; break;
                    case "ipv6.method": if (!new[] { "auto", "manual", "disabled", "ignore", "link-local", "dhcp", "shared" }.Contains(v)) return "Error: invalid property value 'ipv6.method': '" + v + "' not among [ignore, auto, dhcp, link-local, manual, shared, disabled]"; c.V6Method = v; break;
                    case "ipv6.addresses": foreach (var x in list) if (!ValidCidr(x, true)) return "Error: invalid IP address: Invalid IPv6 address '" + x + "'"; c.V6Addrs = merge(c.V6Addrs, list.Select(x => x.Contains("/") ? x : x + "/128").ToList()); if (op == "=" && adding && !a.Contains("ipv6.method")) c.V6Method = "manual"; break;
                    case "ipv6.gateway": c.V6Gw = v; break;
                    case "ipv6.dns": c.V6Dns = merge(c.V6Dns, list); break;
                    case "802-3-ethernet.mtu": case "ethernet.mtu": case "connection.zone": case "ipv6.addr-gen-mode": case "connection.master": break;
                    default: return "Error: invalid <setting>.<property> '" + key + "'.";
                }
            }
            return null;
        }

        // ------------------------------------------------------------ ping, ss, DNS tools
        static int Ping(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "c:W:i:qnw:46I:s:", "", "ping");
            if (o.Error != null) return Fail(io, o, 2);
            if (o.Args.Count == 0) { io.E("ping: usage error: Destination address required"); return 1; }
            var m = sh.M;
            string host = o.Args[0];
            if (host.Contains(":")) return Ping6(sh, o, io, host);
            string ip = m.Net.Resolve(m, host);
            if (ip == null) { io.E("ping: " + host + ": Name or service not known"); return 2; }
            string shown = LabNet.IsIp(host) ? host : (m.Net.Dns.FirstOrDefault(kv => kv.Value == ip && kv.Key.StartsWith(host, StringComparison.Ordinal)).Key ?? host);
            int count = o.Has("c") ? Math.Max(1, int.Parse(o.Get("c"))) : 4;
            bool reach = m.Net.Reachable(m, ip) && (ip.StartsWith("127.", StringComparison.Ordinal) || m.Net.Owner(ip) != null || ip == m.FirstV4());
            io.L("PING " + host + " (" + ip + ") 56(84) bytes of data.");
            int got = 0;
            for (int i = 1; i <= count; i++)
            {
                if (reach) { got++; if (!o.Has("q")) io.L("64 bytes from " + (shown != ip ? shown + " (" + ip + ")" : ip) + ": icmp_seq=" + i + " ttl=64 time=" + (0.3 + (i * 37 % 10) / 20.0).ToString("0.###", CultureInfo.InvariantCulture) + " ms"); }
                else if (m.FirstV4() != null && !o.Has("q")) io.L("From " + m.FirstV4() + " icmp_seq=" + i + " Destination Host Unreachable");
            }
            if (!o.Has("c") && sh.S != null && io.Out.IsTty) { try { sh.S.WaitForBreak(); } catch (BreakSignal) { io.P("^C\n"); } }
            io.L("\n--- " + host + " ping statistics ---\n" + count + " packets transmitted, " + got + " received, " + (got == count ? "0" : got == 0 ? "100" : ((count - got) * 100 / count).ToString()) + "% packet loss, time " + ((count - 1) * 1001 + 2) + "ms" + (got > 0 ? "\nrtt min/avg/max/mdev = 0.312/0.405/0.512/0.071 ms" : ""));
            return got > 0 ? 0 : 1;
        }

        /// <summary>ping to an IPv6 address: answered by a running machine that has it, on an address of the same /64.</summary>
        static int Ping6(Sh sh, Opts o, Io io, string addr)
        {
            var m = sh.M;
            string a = addr.ToLowerInvariant();
            Func<string, string> pfx = c => { var s = c.Split('/')[0].ToLowerInvariant(); int i = s.LastIndexOf(':'); return s.Substring(0, i); };
            bool mine = m.Ifs.Any(i => i.Up && i.V6.Any(v => v.Split('/')[0].ToLowerInvariant() == a)) || a == "::1";
            var owner = m.Net.Machines.FirstOrDefault(x => x.State == "running" && x.Ifs.Any(i => i.Up && i.V6.Any(v => v.Split('/')[0].ToLowerInvariant() == a)));
            bool sameNet = m.Ifs.Any(i => i.Up && i.Carrier && i.V6.Any(v => !v.StartsWith("fe80", StringComparison.Ordinal) && pfx(v) == pfx(a + "/64")));
            bool reach = mine || (owner != null && (sameNet || a.StartsWith("fe80", StringComparison.Ordinal)) && owner.FwAllowsIcmp());
            int count = o.Has("c") ? Math.Max(1, int.Parse(o.Get("c"))) : 4;
            io.L("PING " + addr + "(" + addr + ") 56 data bytes");
            for (int i = 1; i <= count; i++)
                if (reach && !o.Has("q")) io.L("64 bytes from " + addr + ": icmp_seq=" + i + " ttl=64 time=0." + (400 + i * 37 % 300) + " ms");
            if (!o.Has("c") && sh.S != null && io.Out.IsTty) { try { sh.S.WaitForBreak(); } catch (BreakSignal) { io.P("^C\n"); } }
            io.L("\n--- " + addr + " ping statistics ---\n" + count + " packets transmitted, " + (reach ? count : 0) + " received, " + (reach ? "0" : "100") + "% packet loss, time " + ((count - 1) * 1001 + 2) + "ms" + (reach ? "\nrtt min/avg/max/mdev = 0.437/0.512/0.589/0.051 ms" : ""));
            return reach ? 0 : 1;
        }

        static int Ss(Sh sh, List<string> a, Io io)
        {
            var m = sh.M;
            string fl = string.Join("", a.Skip(1).Where(x => x.StartsWith("-", StringComparison.Ordinal) && !x.StartsWith("--", StringComparison.Ordinal)).Select(x => x.Substring(1)).ToArray());
            bool tcp = fl.Contains("t") || (!fl.Contains("u")), udp = fl.Contains("u"), listen = fl.Contains("l") || a[0] == "netstat", proc = fl.Contains("p");
            if (fl.Contains("s")) { io.L("Total: 180\nTCP:   " + (m.Services.Values.Sum(s => s.Ports.Count) + 2) + " (estab 1, closed 0, orphaned 0, timewait 0)"); return 0; }
            io.L("Netid State  Recv-Q Send-Q Local Address:Port  Peer Address:Port Process");
            foreach (var s in m.Services.Values.Where(x => x.Active == "active"))
                foreach (var port in s.Ports)
                {
                    bool isUdp = port == 123 || port == 323;
                    if (isUdp ? !udp : !tcp) continue;
                    string comm = m.ProcByPid(s.Pid) == null ? s.Unit.Replace(".service", "").Replace(".socket", "") : m.ProcByPid(s.Pid).Comm;
                    io.L((isUdp ? "udp   UNCONN" : "tcp   LISTEN") + " 0      " + (isUdp ? "0     " : "128   ") + ("0.0.0.0:" + port).PadLeft(18) + "      0.0.0.0:*    " + (proc && sh.P.Cr.Uid == 0 ? " users:((\"" + comm + "\",pid=" + s.Pid + ",fd=3))" : ""));
                    if (!isUdp) io.L("tcp   LISTEN 0      128   " + ("[::]:" + port).PadLeft(18) + "         [::]:*    " + (proc && sh.P.Cr.Uid == 0 ? " users:((\"" + comm + "\",pid=" + s.Pid + ",fd=4))" : ""));
                }
            if (udp && m.ServiceActive("chronyd.service")) io.L("udp   UNCONN 0      0            127.0.0.1:323        0.0.0.0:*    " + (proc && sh.P.Cr.Uid == 0 ? " users:((\"chronyd\",pid=" + m.ServicePid("chronyd.service") + ",fd=5))" : ""));
            foreach (var c in m.Containers.Where(x => x.Running)) foreach (var p in c.Ports) if (tcp) io.L("tcp   LISTEN 0      4096  " + ("0.0.0.0:" + p.Host).PadLeft(18) + "      0.0.0.0:*    " + (proc ? " users:((\"" + (c.Owner.Length > 0 ? "rootlessport" : "conmon") + "\",pid=" + (m.NextPid - 5) + ",fd=10))" : ""));
            return 0;
        }

        static bool NeedBindUtils(Sh sh, Io io, string prog)
        {
            if (sh.M.PackageInstalled("bind-utils")) return false;
            io.E("bash: " + prog + ": command not found...");
            return true;
        }
        static int Host(Sh sh, List<string> a, Io io)
        {
            var m = sh.M;
            var name = a.Skip(1).LastOrDefault(x => !x.StartsWith("-", StringComparison.Ordinal));
            if (name == null) { io.E("Usage: host [-aCdilrTvVw] [-c class] [-N ndots] [-t type] [-W time]\n            [-R number] [-m flag] [-p port] hostname [server]"); return 1; }
            if (!m.HasDnsServer()) { io.L(";; connection timed out; no servers could be reached"); return 1; }
            if (LabNet.IsIp(name)) { var n = m.Net.Dns.FirstOrDefault(kv => kv.Value == name && !kv.Key.StartsWith("repo", StringComparison.Ordinal) && !kv.Key.StartsWith("registry", StringComparison.Ordinal) && !kv.Key.StartsWith("nfs", StringComparison.Ordinal) && !kv.Key.StartsWith("ntp", StringComparison.Ordinal)); if (n.Key == null) { io.L("Host " + string.Join(".", name.Split('.').Reverse().ToArray()) + ".in-addr.arpa. not found: 3(NXDOMAIN)"); return 1; } io.L(string.Join(".", name.Split('.').Reverse().ToArray()) + ".in-addr.arpa domain name pointer " + n.Key + "."); return 0; }
            string full = name.Contains(".") ? name : name + ".lab.example.com";
            string ip;
            if (!m.Net.Dns.TryGetValue(full.ToLowerInvariant().TrimEnd('.'), out ip)) { io.L("Host " + name + " not found: 3(NXDOMAIN)"); return 1; }
            io.L(full + " has address " + ip);
            return 0;
        }
        static int Dig(Sh sh, List<string> a, Io io)
        {
            var m = sh.M;
            var name = a.Skip(1).FirstOrDefault(x => !x.StartsWith("-", StringComparison.Ordinal) && !x.StartsWith("+", StringComparison.Ordinal) && !x.StartsWith("@", StringComparison.Ordinal) && x != "A" && x != "MX" && x != "PTR");
            bool shortOut = a.Contains("+short");
            if (!m.HasDnsServer()) { io.L(";; communications error to 192.168.50.254#53: timed out\n;; no servers could be reached"); return 9; }
            string full = (name ?? ".").Contains(".") ? name : name + ".lab.example.com";
            string ip; bool ok = name != null && m.Net.Dns.TryGetValue(full.ToLowerInvariant().TrimEnd('.'), out ip);
            ip = ok ? m.Net.Dns[full.ToLowerInvariant().TrimEnd('.')] : null;
            if (shortOut) { if (ok) io.L(ip); return 0; }
            io.L("\n; <<>> DiG 9.16.23-RH <<>> " + string.Join(" ", a.Skip(1).ToArray()) + "\n;; global options: +cmd\n;; Got answer:\n;; ->>HEADER<<- opcode: QUERY, status: " + (ok ? "NOERROR" : "NXDOMAIN") + ", id: 41537\n;; flags: qr aa rd ra; QUERY: 1, ANSWER: " + (ok ? 1 : 0) + ", AUTHORITY: 0, ADDITIONAL: 1\n\n;; QUESTION SECTION:\n;" + full + ".\t\tIN\tA\n");
            if (ok) io.L(";; ANSWER SECTION:\n" + full + ".\t3600\tIN\tA\t" + ip + "\n");
            io.L(";; Query time: 1 msec\n;; SERVER: 192.168.50.254#53(192.168.50.254)\n;; WHEN: " + DateText(m, m.Now) + "\n;; MSG SIZE  rcvd: 72\n");
            return 0;
        }
        static int Nslookup(Sh sh, List<string> a, Io io)
        {
            var m = sh.M;
            var name = a.Skip(1).FirstOrDefault(x => !x.StartsWith("-", StringComparison.Ordinal));
            if (!m.HasDnsServer()) { io.L(";; connection timed out; no servers could be reached"); return 1; }
            string full = (name ?? "").Contains(".") ? name : name + ".lab.example.com";
            string ip;
            io.L("Server:\t\t192.168.50.254\nAddress:\t192.168.50.254#53\n");
            if (name == null || !m.Net.Dns.TryGetValue(full.ToLowerInvariant().TrimEnd('.'), out ip)) { io.L("** server can't find " + name + ": NXDOMAIN\n"); return 1; }
            io.L("Name:\t" + full + "\nAddress: " + ip + "\n");
            return 0;
        }
        static int Tracepath(Sh sh, List<string> a, Io io)
        {
            var m = sh.M;
            string host = a.Skip(1).LastOrDefault(x => !x.StartsWith("-", StringComparison.Ordinal)) ?? "";
            string ip = m.Net.Resolve(m, host);
            if (ip == null) { io.E(a[0] + ": " + host + ": Name or service not known"); return 2; }
            bool reach = m.Net.Reachable(m, ip);
            io.L(" 1?: [LOCALHOST]                      pmtu 1500");
            io.L(reach ? " 1:  " + host + "                                   0.512ms reached\n     Resume: pmtu 1500 hops 1 back 1 " : " 1:  " + (m.FirstV4() ?? "localhost") + "                                   3071.415ms !H\n     Resume: pmtu 1500 ");
            return reach ? 0 : 1;
        }

        // ------------------------------------------------------------ firewall-cmd
        static int FirewallCmd(Sh sh, List<string> a, Io io)
        {
            var m = sh.M;
            // "--add-service http" works like "--add-service=http"
            var withValue = new HashSet<string> { "--zone", "--add-service", "--remove-service", "--add-port", "--remove-port", "--add-source", "--remove-source", "--add-interface", "--change-interface", "--remove-interface", "--add-rich-rule", "--remove-rich-rule", "--set-default-zone", "--query-service", "--query-port", "--info-service", "--get-zone-of-interface", "--new-service" };
            var joined = new List<string> { a[0] };
            for (int i = 1; i < a.Count; i++)
            {
                if (withValue.Contains(a[i]) && i + 1 < a.Count && !a[i + 1].StartsWith("--", StringComparison.Ordinal)) { joined.Add(a[i] + "=" + a[i + 1]); i++; }
                else joined.Add(a[i]);
            }
            a = joined;
            bool offline = a[0] == "firewall-offline-cmd";
            bool perm = offline || a.Contains("--permanent");
            string zoneName = null;
            var acts = new List<string[]>();
            foreach (var x in a.Skip(1))
            {
                if (x == "--permanent" || x == "-q" || x == "--quiet") continue;
                if (x.StartsWith("--zone=", StringComparison.Ordinal)) { zoneName = x.Substring(7); continue; }
                if (!x.StartsWith("--", StringComparison.Ordinal)) { io.E("usage: see firewall-cmd man page\nfirewall-cmd: error: unrecognized arguments: " + x); return 2; }
                int eq = x.IndexOf('=');
                acts.Add(eq > 0 ? new[] { x.Substring(2, eq - 2), x.Substring(eq + 1) } : new[] { x.Substring(2), null });
            }
            if (acts.Count == 0) { io.E("usage: see firewall-cmd man page\nNo option specified."); return 2; }
            string first = acts[0][0];
            if (first == "state") { if (m.FwRunning) { io.L("running"); return 0; } io.L("not running"); return 252; }
            if (!m.FwRunning && !offline) { io.L("FirewallD is not running"); return 252; }
            if (first == "version") { io.L("1.3.4"); return 0; }
            bool readOnly = first.StartsWith("get-", StringComparison.Ordinal) || first.StartsWith("list-", StringComparison.Ordinal) || first.StartsWith("query-", StringComparison.Ordinal) || first.StartsWith("info-", StringComparison.Ordinal);
            if (!readOnly && sh.P.Cr.Uid != 0) { io.E("Authorization failed.\n    Make sure polkit agent is running or run the application as superuser."); return 252; }
            var permZones = Firewall.LoadPermanent(m);
            var zones = perm ? permZones : m.FwRuntime;
            string defZone = Firewall.DefaultZone(m);
            if (!perm && m.FwDefaultZone.Length > 0) defZone = m.FwDefaultZone;
            string zn = zoneName ?? defZone;
            FwZone zone;
            if (!zones.TryGetValue(zn, out zone)) { io.E("Error: INVALID_ZONE: " + zn); return 112; }
            int st = 0;
            bool changed = false;
            var services = Firewall.ServiceNames(m);
            foreach (var act in acts)
            {
                string k = act[0], v = act[1];
                switch (k)
                {
                    case "reload": case "complete-reload":
                        if (perm) { io.E("Error: Can't use stand-alone options with other options."); return 2; }
                        m.FwRuntime = Firewall.LoadPermanent(m); m.FwDefaultZone = Firewall.DefaultZone(m);
                        foreach (var z in m.FwRuntime.Values) foreach (var s in z.Services) if (!Firewall.ServiceNames(m).Contains(s)) { io.E("Error: INVALID_SERVICE: " + s); }
                        io.L("success"); break;
                    case "runtime-to-permanent":
                        foreach (var z in m.FwRuntime.Values) { var pz = permZones.ContainsKey(z.Name) ? permZones[z.Name] : null; if (pz == null || Firewall.ZoneXml(pz) != Firewall.ZoneXml(z)) Firewall.SavePermanent(m, z.Copy()); }
                        io.L("success"); break;
                    case "get-default-zone": io.L(defZone); break;
                    case "set-default-zone":
                        if (!zones.ContainsKey(v ?? "")) { io.E("Error: INVALID_ZONE: " + v); return 112; }
                        string conf = m.V.Cat("/etc/firewalld/firewalld.conf") ?? "DefaultZone=public\n";
                        m.SysWrite("/etc/firewalld/firewalld.conf", Regex.Replace(conf, "(?m)^DefaultZone=.*$", "DefaultZone=" + v));
                        m.FwDefaultZone = v;
                        io.L(defZone == v ? "Warning: ZONE_ALREADY_SET: " + v + "\nsuccess" : "success"); break;
                    case "get-zones": io.L(string.Join(" ", zones.Keys.OrderBy(x => x, StringComparer.Ordinal).ToArray())); break;
                    case "get-services": io.L(string.Join(" ", services.ToArray())); break;
                    case "get-active-zones":
                        foreach (var z in m.FwRuntime.Values.Where(z => z.Sources.Count > 0 || z.Interfaces.Count > 0 || z.Name == m.FwDefaultZone).OrderBy(z => z.Name == m.FwDefaultZone ? 1 : 0))
                            io.L((z.Name + "\n" + (z.Name == m.FwDefaultZone || z.Interfaces.Count > 0 ? "  interfaces: " + string.Join(" ", (z.Name == m.FwDefaultZone ? m.NicNames.Where(n => !m.FwRuntime.Values.Any(o => o.Interfaces.Contains(n))).Concat(z.Interfaces) : z.Interfaces).ToArray()) + "\n" : "") + (z.Sources.Count > 0 ? "  sources: " + string.Join(" ", z.Sources.ToArray()) : "")).TrimEnd('\n'));
                        break;
                    case "get-zone-of-interface": { var z = zones.Values.FirstOrDefault(x => x.Interfaces.Contains(v)); io.L(z != null ? z.Name : (m.NicNames.Contains(v) && !perm ? defZone : "no zone")); if (z == null && (perm || !m.NicNames.Contains(v))) st = 2; break; }
                    case "list-all": io.P(FwListAll(m, zone, zone.Name == defZone && !perm, perm)); break;
                    case "list-all-zones": foreach (var z in zones.Values.OrderBy(x => x.Name, StringComparer.Ordinal)) io.L(FwListAll(m, z, z.Name == defZone && !perm, perm)); break;
                    case "list-services": io.L(string.Join(" ", zone.Services.OrderBy(x => x, StringComparer.Ordinal).ToArray())); break;
                    case "list-ports": io.L(string.Join(" ", zone.Ports.ToArray())); break;
                    case "list-sources": io.L(string.Join(" ", zone.Sources.ToArray())); break;
                    case "list-interfaces": io.L(string.Join(" ", zone.Interfaces.ToArray())); break;
                    case "list-rich-rules": foreach (var r in zone.Rich) io.L(r); break;
                    case "query-service": { bool q = zone.Services.Contains(v); io.L(q ? "yes" : "no"); if (!q) st = 1; break; }
                    case "query-port": { bool q = zone.Ports.Contains(v); io.L(q ? "yes" : "no"); if (!q) st = 1; break; }
                    case "query-masquerade": io.L(zone.Masquerade ? "yes" : "no"); if (!zone.Masquerade) st = 1; break;
                    case "info-service":
                        {
                            if (!services.Contains(v ?? "")) { io.E("Error: INVALID_SERVICE: " + v); return 101; }
                            var ports = Firewall.ServicePorts(m, v);
                            io.L(v + "\n  ports: " + string.Join(" ", ports.ToArray()) + "\n  protocols: \n  source-ports: \n  modules: \n  destination: \n  includes: \n  helpers: ");
                            break;
                        }
                    case "add-service": case "remove-service":
                        {
                            if (string.IsNullOrEmpty(v)) { io.E("usage: see firewall-cmd man page\nfirewall-cmd: error: argument --" + k + ": expected one argument"); return 2; }
                            foreach (var s in v.Split(','))
                            {
                                if (!services.Contains(s)) { io.E("Error: INVALID_SERVICE: '" + s + "' not among existing services"); return 101; }
                                if (k == "add-service") { if (zone.Services.Contains(s)) io.E("Warning: ALREADY_ENABLED: " + s); else { zone.Services.Add(s); changed = true; } }
                                else { if (!zone.Services.Remove(s)) io.E("Warning: NOT_ENABLED: " + s); else changed = true; }
                            }
                            io.L("success"); break;
                        }
                    case "add-port": case "remove-port":
                        {
                            if (string.IsNullOrEmpty(v) || !Regex.IsMatch(v, "^\\d{1,5}(-\\d{1,5})?/(tcp|udp|sctp|dccp)$")) { io.E("Error: INVALID_PORT: " + v); return 102; }
                            if (k == "add-port") { if (zone.Ports.Contains(v)) io.E("Warning: ALREADY_ENABLED: " + v); else { zone.Ports.Add(v); changed = true; } }
                            else { if (!zone.Ports.Remove(v)) io.E("Warning: NOT_ENABLED: " + v); else changed = true; }
                            io.L("success"); break;
                        }
                    case "add-source": case "remove-source":
                        {
                            if (string.IsNullOrEmpty(v) || !ValidCidr(v, false)) { io.E("Error: INVALID_ADDR: " + v); return 110; }
                            if (k == "add-source") { var other = zones.Values.FirstOrDefault(z => z != zone && z.Sources.Contains(v)); if (other != null) { io.E("Error: ZONE_CONFLICT: '" + v + "' already bound to '" + other.Name + "'"); return 114; } if (!zone.Sources.Contains(v)) { zone.Sources.Add(v); changed = true; } }
                            else if (zone.Sources.Remove(v)) changed = true;
                            io.L("success"); break;
                        }
                    case "add-interface": case "change-interface": case "remove-interface":
                        foreach (var z in zones.Values) if (z.Interfaces.Remove(v ?? "")) changed = true;
                        if (k != "remove-interface") { zone.Interfaces.Add(v); changed = true; }
                        io.L("success"); break;
                    case "add-rich-rule": case "remove-rich-rule":
                        {
                            if (string.IsNullOrEmpty(v) || !Regex.IsMatch(v, "^rule ") || !Regex.IsMatch(v, "(accept|reject|drop|mark)")) { io.E("Error: INVALID_RULE: " + v); return 106; }
                            if (k == "add-rich-rule") { if (zone.Rich.Contains(v)) io.E("Warning: ALREADY_ENABLED: " + v); else { zone.Rich.Add(v); changed = true; } }
                            else if (zone.Rich.Remove(v)) changed = true;
                            io.L("success"); break;
                        }
                    case "add-masquerade": zone.Masquerade = true; changed = true; io.L("success"); break;
                    case "remove-masquerade": zone.Masquerade = false; changed = true; io.L("success"); break;
                    case "new-service":
                        if (!perm) { io.E("Error: Option can be used only with --permanent."); return 2; }
                        m.V.EnsureDir("/etc/firewalld/services", 0x1E8, null);
                        m.SysWrite("/etc/firewalld/services/" + v + ".xml", "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<service>\n</service>\n");
                        io.L("success"); break;
                    case "get-default-zone ": break;
                    case "panic-on": case "panic-off": io.L("success"); break;
                    case "query-panic": io.L("no"); st = 1; break;
                    default: io.E("usage: see firewall-cmd man page\nfirewall-cmd: error: unrecognized arguments: --" + k); return 2;
                }
            }
            if (changed && perm) Firewall.SavePermanent(m, zone);
            return st;
        }

        static string FwListAll(Machine m, FwZone z, bool isDefaultRuntime, bool perm)
        {
            var ifs = new List<string>(z.Interfaces);
            if (isDefaultRuntime) foreach (var n in m.NicNames) if (!ifs.Contains(n) && !m.FwRuntime.Values.Any(o => o != z && o.Interfaces.Contains(n))) ifs.Add(n);
            bool active = !perm && (ifs.Count > 0 || z.Sources.Count > 0);
            var sb = new StringBuilder();
            sb.Append(z.Name + (active ? " (active)" : "") + "\n");
            sb.Append("  target: " + z.Target + "\n");
            sb.Append("  icmp-block-inversion: no\n");
            sb.Append("  interfaces: " + string.Join(" ", ifs.ToArray()) + "\n");
            sb.Append("  sources: " + string.Join(" ", z.Sources.ToArray()) + "\n");
            sb.Append("  services: " + string.Join(" ", z.Services.OrderBy(x => x, StringComparer.Ordinal).ToArray()) + "\n");
            sb.Append("  ports: " + string.Join(" ", z.Ports.ToArray()) + "\n");
            sb.Append("  protocols: \n");
            sb.Append("  forward: yes\n");
            sb.Append("  masquerade: " + (z.Masquerade ? "yes" : "no") + "\n");
            sb.Append("  forward-ports: \n  source-ports: \n  icmp-blocks: \n");
            sb.Append("  rich rules: \n");
            foreach (var r in z.Rich) sb.Append("\t" + r + "\n");
            return sb.ToString();
        }
    }
}
