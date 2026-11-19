// Network Lab - the terminals of the Linux-like computers (Ubuntu-like with netplan and nmcli, Debian-like with
// /etc/network/interfaces) and of the macOS-like laptop. Settings typed here are what the simulated network card uses.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace NetworkLab
{
    public class LinuxShell : Shell
    {
        public const string NetplanFile = "/etc/netplan/01-netcfg.yaml";
        public const string InterfacesFile = "/etc/network/interfaces";
        public const string ResolvFile = "/etc/resolv.conf";
        protected long sudoUntil = -1;
        protected bool root;
        // an SSH session to another computer
        protected string sshHost, sshUser, sshPass;
        protected uint sshIp;
        protected string sshRemoteName;

        public LinuxShell(Device d) : base(d) { Title = "Terminal"; }

        protected LinuxPersonality Lp { get { return Pers as LinuxPersonality; } }
        protected bool UsesNetplan { get { return Lp != null && Lp.Netplan; } }

        public override string Banner
        {
            get
            {
                if (Pers.Family == "mac") return "Last login: today on ttys000 (simulated by Network Lab)\n";
                return (UsesNetplan ? "Welcome to an Ubuntu-like system (simulated by Network Lab)" : "Welcome to a Debian-like server (simulated by Network Lab)") + "\nType help to see the commands this terminal knows.\n";
            }
        }

        public override string Prompt
        {
            get
            {
                if (sshHost != null) return sshUser + "@" + sshRemoteName + ":~$ ";
                return "student@" + H.HostName + ":~$ ";
            }
        }

        protected virtual string[] Words { get { return new[] { "ip", "ping", "traceroute", "dig", "nslookup", "cat", "nano", "sudo", "netplan", "systemctl", "ss", "ufw", "hostname", "hostnamectl", "nmcli", "ssh", "curl", "resolvectl", "ls", "clear", "exit", "help", "whoami", "pwd", "echo", "ifup", "ifdown", "uname", "date", "history" }; } }

        public override string Complete(string line)
        {
            var w = line.Split(' ');
            if (w.Length == 1 && w[0].Length > 0)
            {
                var m = Words.Where(x => x.StartsWith(w[0])).ToList();
                return m.Count == 1 ? m[0] + " " : line;
            }
            // file names
            string last = w[w.Length - 1];
            if (last.StartsWith("/"))
            {
                var m = H.Files.Keys.Where(x => x.StartsWith(last)).ToList();
                if (m.Count == 1) { w[w.Length - 1] = m[0]; return string.Join(" ", w); }
            }
            return line;
        }

        static readonly Dictionary<string, string[]> SubWords = new Dictionary<string, string[]>
        {
            { "ip", new[] { "addr", "address", "link", "route", "neigh" } },
            { "netplan", new[] { "apply", "try", "get", "generate" } },
            { "systemctl", new[] { "start", "stop", "restart", "status" } },
            { "ufw", new[] { "enable", "disable", "status", "allow", "deny", "delete", "reset" } },
            { "nmcli", new[] { "device", "connection", "general", "radio" } },
            { "resolvectl", new[] { "status" } },
            { "sudo", null },
        };

        /// <summary>Tab like bash: command names first, then a few sub-commands, and file paths.</summary>
        public override Completion Candidates(string line)
        {
            int st;
            string word = LastWord(line, out st);
            var r = new Completion { Start = st };
            var before = line.Substring(0, st).Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (before.Count > 0 && before[0] == "sudo") before.RemoveAt(0);
            if (word.StartsWith("/") || word.StartsWith("~"))
            {
                string w = word.StartsWith("~") ? "/home/student" + word.Substring(1) : word;
                var paths = new HashSet<string>(H.Files.Keys);
                foreach (var dir in new[] { "/etc/", "/etc/netplan/", "/etc/network/", "/home/student/", "/var/log/" }) paths.Add(dir);
                foreach (var full in paths)
                {
                    if (!full.StartsWith(w, StringComparison.Ordinal) || (full == w && full.EndsWith("/"))) continue;
                    int slash = full.IndexOf('/', w.Length);
                    if (slash >= 0 && slash < full.Length - 1) { string d = full.Substring(0, slash + 1); if (!r.Words.Contains(d)) { r.Words.Add(d); r.NoSpace.Add(d); } }
                    else { if (!r.Words.Contains(full)) r.Words.Add(full); if (full.EndsWith("/")) r.NoSpace.Add(full); }
                }
                r.Words.Sort(StringComparer.Ordinal);
                return r;
            }
            if (before.Count == 0) { r.Words.AddRange(Words.Where(x => x.StartsWith(word, StringComparison.Ordinal)).OrderBy(x => x, StringComparer.Ordinal)); return r; }
            string[] subs;
            if (before.Count == 1 && SubWords.TryGetValue(before[0], out subs) && subs != null) r.Words.AddRange(subs.Where(x => x.StartsWith(word, StringComparison.Ordinal)));
            return r;
        }

        protected bool IsRoot { get { return root || (sudoUntil >= 0 && D.P.Sim.Now <= sudoUntil); } }

        protected override void Run(string line)
        {
            string t = line.Trim();
            if (t.Length == 0) return;
            if (sshHost != null) { RemoteCommand(t); return; }
            var a = Split(t);
            if (a[0] == "sudo")
            {
                if (a.Count == 1) { Print("usage: sudo command"); return; }
                string rest = t.Substring(4).Trim();
                if (a[1] == "-i" || a[1] == "su") { WithSudo(() => { root = true; Print("(you are now root: be careful. exit goes back)"); }); return; }
                WithSudo(() => { bool was = root; root = true; try { Command(Split(rest), rest); } finally { root = was; } });
                return;
            }
            Command(a, t);
        }

        protected void WithSudo(Action then)
        {
            if (IsRoot) { then(); return; }
            int tries = 0;
            Func<string, bool> ask = null;
            ask = typed =>
            {
                if (typed == H.Password) { sudoUntil = D.P.Sim.Now + 15 * 60000; then(); return true; }
                tries++;
                if (tries >= 3) { Print("sudo: 3 incorrect password attempts"); return true; }
                Print("Sorry, try again.");
                Ask("[sudo] password for student: ", true, ask);
                return true;
            };
            Ask(Pers.Family == "mac" ? "Password:" : "[sudo] password for student: ", true, ask);
        }

        protected virtual void Command(List<string> a, string line)
        {
            switch (a[0])
            {
                case "ip": IpCmd(a); return;
                case "ping": Ping(a, false); return;
                case "traceroute": case "tracepath": Traceroute(a); return;
                case "dig": Dig(a); return;
                case "nslookup": Nslookup(a); return;
                case "host": Nslookup(a); return;
                case "cat": Cat(a); return;
                case "ls": Ls(a); return;
                case "nano": case "vi": case "vim": Nano(a); return;
                case "netplan": NetplanCmd(a); return;
                case "systemctl": Systemctl(a); return;
                case "service": if (a.Count >= 3) Systemctl(new List<string> { "systemctl", a[2], a[1] }); return;
                case "ifup": case "ifdown": if (UsesNetplan) { Print(a[0] + ": command not found (this system uses netplan)"); return; } if (!IsRoot) { Print("ifup: failed to open lockfile /run/network/.ifstate.lock: Permission denied"); return; } ApplyInterfaces(a[0] == "ifup"); return;
                case "ss": case "netstat": Ss(); return;
                case "ufw": Ufw(a); return;
                case "hostname": HostnameCmd(a); return;
                case "hostnamectl": Hostnamectl(a); return;
                case "nmcli": Nmcli(a); return;
                case "ssh": Ssh(a); return;
                case "curl": case "wget": Curl(a); return;
                case "resolvectl": case "systemd-resolve": Resolvectl(); return;
                case "ifconfig": Print(UsesNetplan ? "Command 'ifconfig' not found, but can be installed with:\n\nsudo apt install net-tools\n\n(ip a shows the same information)" : "-bash: ifconfig: command not found  (use: ip a)"); return;
                case "apt": case "apt-get": Print("This simulated computer can't download packages: the tools you need are already installed."); return;
                case "clear": ClearRequested = true; Version++; return;
                case "exit": case "logout": if (root) { root = false; return; } Closed = true; Version++; return;
                case "whoami": Print(IsRoot && root ? "root" : "student"); return;
                case "pwd": Print("/home/student"); return;
                case "cd": return;
                case "echo": Print(line.Length > 5 ? line.Substring(5).Trim().Trim('"') : ""); return;
                case "date": Print(new DateTime(2026, 1, 1).AddMilliseconds(D.P.Sim.Now).ToString("ddd MMM d HH:mm:ss UTC yyyy", CultureInfo.InvariantCulture)); return;
                case "uname": Print(a.Contains("-a") ? "Linux " + H.HostName + " 6.8.0-netlab #1 SMP x86_64 GNU/Linux" : "Linux"); return;
                case "history": for (int i = 0; i < History.Count; i++) Print((i + 1).ToString().PadLeft(5) + "  " + History[i]); return;
                case "help": Help(); return;
                case "arp": Neigh(); return;
            }
            Print(a[0] + ": command not found");
        }

        protected virtual void Help()
        {
            Print("Commands this terminal knows:\n  ip a | ip addr add 192.168.1.20/24 dev " + EthName + " | ip link set " + EthName + " up | ip route | ip route add default via 192.168.1.1\n  ping [-c 4] <address>      (Ctrl+C stops it)\n  traceroute <address>       dig <name> [@server]      nslookup <name>\n  cat <file>   ls /etc   nano <file>   sudo <command>  (the password is the one in the lab, usually netlab)\n" +
                (UsesNetplan ? "  sudo nano " + NetplanFile + "  then  sudo netplan apply\n  resolvectl status          (DNS servers in use)\n" : "  sudo nano " + InterfacesFile + "  then  sudo systemctl restart networking\n  sudo nano " + ResolvFile + "  (nameserver lines: the DNS servers)\n") +
                (Lp != null && Lp.Desktop ? "  nmcli device status | nmcli device wifi list | nmcli device wifi connect <SSID> password <key>\n" : "") +
                "  sudo systemctl start apache2 | ssh     ss -tulpn     sudo ufw enable | allow 22 | status\n  hostname, hostnamectl, ssh user@host, curl http://host/, clear, exit");
        }

        protected string EthName { get { var e = H.Adapters.FirstOrDefault(f => !f.Port.IsWireless) ?? H.Adapters.FirstOrDefault(); return e != null ? Pers.IfName(e.Port) : "eth0"; } }
        protected IpIf Dev(string name) { return Pers.AdapterByName(D, name); }

        // ---------------------------------------------------------------- ip
        void IpCmd(List<string> a)
        {
            string obj = a.Count > 1 ? a[1] : "";
            string verb = a.Count > 2 ? a[2] : "show";
            if (obj == "a" || obj == "addr" || obj == "address")
            {
                if (verb == "show" || verb == "list" || verb == "s")
                {
                    string only = a.Count > 3 ? a[a.Count - 1] : null;
                    ShowAddr(only);
                    return;
                }
                if (!IsRoot) { Print("RTNETLINK answers: Operation not permitted  (use sudo)"); return; }
                string devName = Opt(a, "dev");
                var f = devName != null ? Dev(devName) : null;
                if (f == null) { Print(devName == null ? "Not enough information: \"dev\" argument is required." : "Cannot find device \"" + devName + "\""); return; }
                if (verb == "flush") { D.Ip.SetAddress(f, 0, 0); f.Gateway = 0; return; }
                if (a.Count < 4) { Print("Error: an address is required (like 192.168.1.20/24)."); return; }
                string cidr = a[3];
                int slash = cidr.IndexOf('/');
                uint ip, mask = 0xFFFFFFFF;
                if (!Ip.TryParse(slash > 0 ? cidr.Substring(0, slash) : cidr, out ip)) { Print("Error: any valid prefix is expected rather than \"" + cidr + "\"."); return; }
                if (slash > 0 && !Ip.TryParseMask(cidr.Substring(slash + 1), out mask)) { Print("Error: any valid prefix is expected rather than \"" + cidr + "\"."); return; }
                if (verb == "add")
                {
                    if (f.Ip != 0 && f.Ip == ip) { Print("RTNETLINK answers: File exists"); return; }
                    if (f.DhcpC != null) f.DhcpC.Stop();
                    f.Dhcp = false;
                    D.Ip.SetAddress(f, ip, mask);
                    if (slash < 0) Print("Warning: Executing wildcard deletion to stay compatible with old scripts.\n(no /prefix given, so the mask is /32: write it like 192.168.1.20/24)");
                    return;
                }
                if (verb == "del" || verb == "delete")
                {
                    if (f.Ip != ip) { Print("RTNETLINK answers: Cannot assign requested address"); return; }
                    D.Ip.SetAddress(f, 0, 0); f.Gateway = 0;
                    return;
                }
                Print("Command \"" + verb + "\" is unknown, try \"ip address help\".");
                return;
            }
            if (obj == "l" || obj == "link")
            {
                if (verb == "set")
                {
                    if (!IsRoot) { Print("RTNETLINK answers: Operation not permitted  (use sudo)"); return; }
                    string n = a.Count > 3 ? a[3] : ""; if (n == "dev" && a.Count > 4) n = a[4];
                    var f = Dev(n);
                    if (f == null) { Print("Cannot find device \"" + n + "\""); return; }
                    bool up = a.Contains("up");
                    bool down = a.Contains("down");
                    if (!up && !down) { Print("Error: say up or down."); return; }
                    f.Port.Shutdown = down;
                    D.P.Sim.TopologyChanged();
                    if (up && f.Dhcp) H.EnsureDhcp(f);
                    return;
                }
                var sb = new StringBuilder("1: lo: <LOOPBACK,UP,LOWER_UP> mtu 65536 qdisc noqueue state UNKNOWN mode DEFAULT group default qlen 1000\n    link/loopback 00:00:00:00:00:00 brd 00:00:00:00:00:00\n");
                int i = 2;
                foreach (var f in H.Adapters) sb.Append(i++ + ": " + Pers.IfName(f.Port) + ": " + Flags(f) + " mtu 1500 qdisc fq_codel state " + (f.Up ? "UP" : "DOWN") + " mode DEFAULT group default qlen 1000\n    link/ether " + Mac.Colon(f.Mac) + " brd ff:ff:ff:ff:ff:ff\n");
                Print(sb.ToString().TrimEnd('\n'));
                return;
            }
            if (obj == "r" || obj == "route" || obj == "ro")
            {
                if (verb == "show" || verb == "list" || a.Count == 2) { ShowRoutes(); return; }
                if (!IsRoot) { Print("RTNETLINK answers: Operation not permitted  (use sudo)"); return; }
                string target = a.Count > 3 ? a[3] : "";
                if (verb == "add" || verb == "replace")
                {
                    if (target != "default" && target != "0.0.0.0/0") { Print("Only the default route can be changed in this simulator: ip route add default via <gateway>"); return; }
                    string via = Opt(a, "via");
                    uint gw;
                    if (via == null || !Ip.TryParse(via, out gw)) { Print("Error: inet address is expected rather than \"" + (via ?? "") + "\"."); return; }
                    var f = H.Adapters.FirstOrDefault(x => x.Ip != 0 && x.Up && Ip.SameNet(x.Ip, gw, x.Mask));
                    if (f == null) { Print("Error: Nexthop has invalid gateway."); return; }
                    if (verb == "add" && H.Adapters.Any(x => x.Gateway != 0)) { Print("RTNETLINK answers: File exists  (there is a default route already: ip route del default first, or use replace)"); return; }
                    foreach (var o in H.Adapters) o.Gateway = 0;
                    f.Gateway = gw;
                    return;
                }
                if (verb == "del" || verb == "delete")
                {
                    if (!H.Adapters.Any(x => x.Gateway != 0)) { Print("RTNETLINK answers: No such process"); return; }
                    foreach (var o in H.Adapters) o.Gateway = 0;
                    return;
                }
                Print("Command \"" + verb + "\" is unknown, try \"ip route help\".");
                return;
            }
            if (obj == "n" || obj == "neigh" || obj == "neighbour") { Neigh(); return; }
            Print("Usage: ip [ OPTIONS ] OBJECT { COMMAND | help }\nwhere  OBJECT := { address | link | route | neigh }");
        }

        static string Opt(List<string> a, string key)
        {
            int i = a.IndexOf(key);
            return i >= 0 && i + 1 < a.Count ? a[i + 1] : null;
        }

        static string Flags(IpIf f)
        {
            if (f.Port.Shutdown) return "<BROADCAST,MULTICAST>";
            return f.Up ? "<BROADCAST,MULTICAST,UP,LOWER_UP>" : "<NO-CARRIER,BROADCAST,MULTICAST,UP>";
        }

        void ShowAddr(string only)
        {
            var sb = new StringBuilder();
            if (only == null || only == "lo") sb.Append("1: lo: <LOOPBACK,UP,LOWER_UP> mtu 65536 qdisc noqueue state UNKNOWN group default qlen 1000\n    link/loopback 00:00:00:00:00:00 brd 00:00:00:00:00:00\n    inet 127.0.0.1/8 scope host lo\n       valid_lft forever preferred_lft forever\n");
            int i = 2;
            foreach (var f in H.Adapters)
            {
                string n = Pers.IfName(f.Port);
                if (only != null && only != n) { i++; continue; }
                sb.Append(i++ + ": " + n + ": " + Flags(f) + " mtu 1500 qdisc fq_codel state " + (f.Up ? "UP" : "DOWN") + " group default qlen 1000\n    link/ether " + Mac.Colon(f.Mac) + " brd ff:ff:ff:ff:ff:ff\n");
                if (f.Ip != 0) sb.Append("    inet " + Ip.Str(f.Ip) + "/" + Ip.PrefixOf(f.Mask) + " brd " + Ip.Str(Ip.BroadcastOf(f.Ip, f.Mask)) + " scope global " + (f.Dhcp ? "dynamic " : "") + n + "\n       valid_lft " + (f.Dhcp ? "86213sec preferred_lft 86213sec" : "forever preferred_lft forever") + "\n");
            }
            Print(sb.ToString().TrimEnd('\n'));
        }

        void ShowRoutes()
        {
            var sb = new StringBuilder();
            foreach (var f in H.Adapters.Where(x => x.Up && x.Ip != 0 && x.Gateway != 0)) sb.Append("default via " + Ip.Str(f.Gateway) + " dev " + Pers.IfName(f.Port) + " proto " + (f.Dhcp ? "dhcp" : "static") + " metric 100\n");
            foreach (var f in H.Adapters.Where(x => x.Up && x.Ip != 0)) sb.Append(Ip.Cidr(f.Ip & f.Mask, f.Mask) + " dev " + Pers.IfName(f.Port) + " proto kernel scope link src " + Ip.Str(f.Ip) + " metric 100\n");
            Print(sb.Length == 0 ? "" : sb.ToString().TrimEnd('\n'));
        }

        protected void Neigh()
        {
            var sb = new StringBuilder();
            foreach (var e in D.Ip.ArpEntries()) sb.Append(Ip.Str(e.Ip) + " dev " + Pers.IfName(e.If.Port) + " lladdr " + Mac.Colon(e.Mac) + " REACHABLE\n");
            Print(sb.ToString().TrimEnd('\n'));
        }

        // ---------------------------------------------------------------- ping, traceroute
        protected void Ping(List<string> a, bool mac)
        {
            int count = 0; string target = null;
            for (int i = 1; i < a.Count; i++)
            {
                if (a[i] == "-c" && i + 1 < a.Count) int.TryParse(a[++i], out count);
                else if (a[i].StartsWith("-")) { if (a[i] == "-W" || a[i] == "-i" || a[i] == "-t") i++; }
                else target = a[i];
            }
            if (target == null) { Print("ping: usage error: Destination address required"); return; }
            PingJob.Start(D, target, count > 0 ? count : 1, j =>
            {
                Hold(j);
                j.Forever = count == 0;
                j.TimeoutMs = 1000;
                bool head = false;
                j.OnResolveFail = err => Print(mac ? "ping: cannot resolve " + target + ": Unknown host" : "ping: " + target + ": " + (err == "no-dns" || err == "timeout" ? "Temporary failure in name resolution" : "Name or service not known"));
                Action header = () => { if (head) return; head = true; Print(mac ? "PING " + target + " (" + Ip.Str(j.Target) + "): 56 data bytes" : "PING " + target + " (" + Ip.Str(j.Target) + ") 56(84) bytes of data."); };
                j.OnResult = r =>
                {
                    header();
                    int seq = mac ? r.Seq - 1 : r.Seq;
                    switch (r.Kind)
                    {
                        case "reply": Print("64 bytes from " + Ip.Str(r.From) + ": icmp_seq=" + seq + " ttl=" + r.Ttl + " " + Ms(r.Ms, false)); break;
                        case "timeout": if (mac) Print("Request timeout for icmp_seq " + seq); break;
                        case "ttl": Print("From " + Ip.Str(r.From) + " icmp_seq=" + seq + " Time to live exceeded"); break;
                        case "noroute": case "general": Print(mac ? "ping: sendto: No route to host" : "ping: connect: Network is unreachable"); j.Stop(); break;
                        case "prohibited": Print("From " + Ip.Str(r.From) + " icmp_seq=" + seq + " Packet filtered"); break;
                        default: Print("From " + Ip.Str(r.From) + " icmp_seq=" + seq + " Destination Host Unreachable"); break;
                    }
                };
                j.OnDone = jj =>
                {
                    Release();
                    if (jj.Target == 0) return;
                    header();
                    if (jj.Stopped && jj.Forever) Print("^C");
                    int loss = jj.Sent == 0 ? 0 : (jj.Sent - jj.Received) * 100 / jj.Sent;
                    Print("\n--- " + target + " ping statistics ---\n" + jj.Sent + " packets transmitted, " + jj.Received + (mac ? " packets received, " + loss + ".0% packet loss" : " received, " + loss + "% packet loss, time " + (jj.Sent * 1000 - 1000) + "ms") +
                        (jj.Received > 0 ? "\n" + (mac ? "round-trip" : "rtt") + " min/avg/max/" + (mac ? "stddev" : "mdev") + " = " + jj.MinMs + ".4/" + (jj.SumMs / jj.Received) + ".5/" + jj.MaxMs + ".6/0.1 ms" : ""));
                };
            });
        }

        protected void Traceroute(List<string> a)
        {
            string target = a.Count > 1 ? a[a.Count - 1] : null;
            if (target == null || target.StartsWith("-")) { Print("Usage: traceroute host"); return; }
            TraceJob.Start(D, target, true, j =>
            {
                Hold(j);
                j.TimeoutMs = 1000; j.MaxHops = 30;
                j.OnResolveFail = err => Print(target + ": Name or service not known\nCannot handle \"host\" cmdline arg `" + target + "' on position 1 (argc 1)");
                bool head = false;
                j.OnHop = h =>
                {
                    if (!head) { head = true; Print("traceroute to " + target + " (" + Ip.Str(j.Target) + "), 30 hops max, 60 byte packets"); }
                    var sb = new StringBuilder((h.Ttl < 10 ? " " : "") + h.Ttl + "  ");
                    if (h.From != 0) sb.Append(Ip.Str(h.From) + " (" + Ip.Str(h.From) + ")");
                    for (int i = 0; i < 3; i++) sb.Append(h.Ms[i] < 0 ? " *" : "  " + h.Ms[i] + ".512 ms");
                    if (h.Unreachable) sb.Append(" !H");
                    Print(sb.ToString());
                };
                j.OnDone = jj => Release();
            });
        }

        // ---------------------------------------------------------------- names
        protected void Dig(List<string> a)
        {
            string name = null; uint server = 0;
            foreach (var x in a.Skip(1)) { if (x.StartsWith("@")) Ip.TryParse(x.Substring(1), out server); else if (!x.StartsWith("+") && !x.StartsWith("-") && x != "A") name = x; }
            if (name == null) { Print("; <<>> DiG 9 (simulated) <<>>\n;; (give a name: dig www.example.com)"); return; }
            var servers = server != 0 ? new List<uint> { server } : H.DnsServers;
            string shown = server != 0 ? Ip.Str(server) : (UsesNetplan ? "127.0.0.53" : (servers.Count > 0 ? Ip.Str(servers[0]) : "127.0.0.1"));
            if (servers.Count == 0) { Print(";; communications error to " + shown + "#53: connection refused\n;; no servers could be reached"); return; }
            Hold("dig");
            long t0 = D.P.Sim.Now;
            DnsAsk(name, servers, 0, (ip, err) =>
            {
                Release();
                if (err == "timeout") { Print(";; communications error to " + shown + "#53: timed out\n;; communications error to " + shown + "#53: timed out\n\n; <<>> DiG 9 (simulated) <<>> " + name + "\n;; global options: +cmd\n;; no servers could be reached"); return; }
                var sb = new StringBuilder("\n; <<>> DiG 9 (simulated) <<>> " + name + "\n;; global options: +cmd\n;; Got answer:\n;; ->>HEADER<<- opcode: QUERY, status: " + (ip != 0 ? "NOERROR" : "NXDOMAIN") + ", id: " + (D.P.Rng.Next(1000, 65000)) + "\n;; flags: qr rd ra; QUERY: 1, ANSWER: " + (ip != 0 ? 1 : 0) + ", AUTHORITY: 0, ADDITIONAL: 1\n\n;; QUESTION SECTION:\n;" + name + ".\t\t\tIN\tA\n");
                if (ip != 0) sb.Append("\n;; ANSWER SECTION:\n" + name + ".\t\t300\tIN\tA\t" + Ip.Str(ip) + "\n");
                sb.Append("\n;; Query time: " + (D.P.Sim.Now - t0) + " msec\n;; SERVER: " + shown + "#53(" + shown + ") (UDP)\n;; MSG SIZE  rcvd: 60\n");
                Print(sb.ToString());
            });
        }

        protected void Nslookup(List<string> a)
        {
            string name = a.Count > 1 ? a[1] : null;
            var servers = H.DnsServers;
            uint sv;
            if (a.Count > 2 && Ip.TryParse(a[2], out sv)) servers = new List<uint> { sv };
            string shown = a.Count > 2 ? a[2] : (UsesNetplan ? "127.0.0.53" : (servers.Count > 0 ? Ip.Str(servers[0]) : "127.0.0.1"));
            if (name == null) { Print("usage: nslookup <name> [server]"); return; }
            if (servers.Count == 0) { Print(";; connection timed out; no servers could be reached"); return; }
            Hold("ns");
            DnsAsk(name, servers, 0, (ip, err) =>
            {
                Release();
                if (err == "timeout") { Print(";; connection timed out; no servers could be reached"); return; }
                if (a[0] == "host") { Print(ip != 0 ? name + " has address " + Ip.Str(ip) : "Host " + name + " not found: 3(NXDOMAIN)"); return; }
                Print("Server:\t\t" + shown + "\nAddress:\t" + shown + "#53\n\n" + (ip != 0 ? "Non-authoritative answer:\nName:\t" + name + "\nAddress: " + Ip.Str(ip) + "\n" : "** server can't find " + name + ": NXDOMAIN\n"));
            });
        }

        protected void DnsAsk(string name, List<uint> servers, int i, Action<uint, string> cb)
        {
            var s = D.Ip; var sim = D.P.Sim;
            int port = s.Ephemeral(); int id = D.P.Rng.Next(1, 65535);
            bool done = false; SimTimer t = null;
            s.Udp[port] = (ifc, p) =>
            {
                var r = p.Seg.App as DnsMsg;
                if (r == null || r.Id != id || done) return;
                done = true; s.Udp.Remove(port); if (t != null) t.Cancel();
                if (r.Answer != 0) H.DnsCache[name] = r.Answer;
                cb(r.Answer, r.Answer != 0 ? "" : "nxdomain");
            };
            var q = new IpPkt { Dst = servers[i], Proto = "UDP", Seg = new Seg { SrcPort = port, DstPort = 53, App = new DnsMsg { Id = id, QName = name } } };
            sim.NoteOrigin(D, new Frame { Ip = q }, D.Name + " asks the DNS server " + Ip.Str(servers[i]) + " for " + name + ".");
            string err = s.Send(q, null);
            if (err.Length > 0) { s.Udp.Remove(port); done = true; if (i + 1 < servers.Count) DnsAsk(name, servers, i + 1, cb); else cb(0, "timeout"); return; }
            t = sim.After(2000, () => { if (done) return; done = true; s.Udp.Remove(port); if (i + 1 < servers.Count) DnsAsk(name, servers, i + 1, cb); else cb(0, "timeout"); });
        }

        void Resolvectl()
        {
            var sb = new StringBuilder("Global\n       Protocols: +LLMNR -mDNS -DNSOverTLS DNSSEC=no/unsupported\nresolv.conf mode: stub\n");
            foreach (var f in H.Adapters)
            {
                sb.Append("\nLink " + (f.Port.Index + 2) + " (" + Pers.IfName(f.Port) + ")\n    Current Scopes: " + (f.Up && f.Dns.Count > 0 ? "DNS LLMNR/IPv4" : "none") + "\n");
                if (f.Dns.Count > 0) sb.Append("Current DNS Server: " + Ip.Str(f.Dns[0]) + "\n       DNS Servers: " + string.Join(" ", f.Dns.Select(x => Ip.Str(x)).ToArray()) + "\n");
            }
            Print(sb.ToString());
        }

        // ---------------------------------------------------------------- files
        protected string FileText(string path)
        {
            string txt;
            if (path == ResolvFile && UsesNetplan)
                return "# This is /run/systemd/resolve/stub-resolv.conf managed by man:systemd-resolved(8).\n# Do not edit.\n#\n# It points to the local resolver (127.0.0.53), which asks the DNS servers\n# set in netplan. See them with: resolvectl status\n\nnameserver 127.0.0.53\noptions edns0 trust-ad\nsearch .\n";
            if (H.Files.TryGetValue(path, out txt)) return txt;
            return null;
        }

        void Cat(List<string> a)
        {
            if (a.Count < 2) return;
            foreach (var p in a.Skip(1))
            {
                string t = FileText(p);
                if (t == null) { Print("cat: " + p + ": No such file or directory"); continue; }
                Write(t.EndsWith("\n") ? t : t + "\n");
            }
            Version++;
        }

        void Ls(List<string> a)
        {
            string dir = a.Count > 1 && !a[a.Count - 1].StartsWith("-") ? a[a.Count - 1].TrimEnd('/') : "/home/student";
            if (dir == "/home/student" || dir == "~" || dir == ".") { Print(""); return; }
            var names = new SortedSet<string>();
            foreach (var k in H.Files.Keys.Concat(UsesNetplan ? new[] { ResolvFile } : new string[0]))
            {
                if (!k.StartsWith(dir + "/")) continue;
                string rest = k.Substring(dir.Length + 1);
                int s = rest.IndexOf('/');
                names.Add(s >= 0 ? rest.Substring(0, s) : rest);
            }
            if (dir == "/etc") { names.Add("passwd"); names.Add("ssh"); }
            if (names.Count == 0) { Print("ls: cannot access '" + dir + "': No such file or directory"); return; }
            Print(string.Join("  ", names.ToArray()));
        }

        void Nano(List<string> a)
        {
            if (a.Count < 2) { Print("Give a file name: nano /etc/..."); return; }
            string path = a[1];
            if (!path.StartsWith("/")) path = "/home/student/" + path;
            string txt = FileText(path) ?? "";
            bool canWrite = IsRoot || path.StartsWith("/home/");
            if (path == ResolvFile && UsesNetplan) canWrite = false;
            OpenEditor(path, txt, false, (sh, text) =>
            {
                if (!canWrite) { Print("[ Error writing " + path + ": Permission denied ]  (open it with sudo nano)"); return; }
                if (path == "/var/www/html/index.html") H.Pages["index.html"] = text;
                H.Files[path] = text;
                if (path == "/etc/hostname") { H.ComputerName = text.Trim(); }
                Print("[ Wrote " + text.Split('\n').Length + " lines ]");
            });
        }

        // ---------------------------------------------------------------- netplan
        void NetplanCmd(List<string> a)
        {
            if (!UsesNetplan) { Print("netplan: command not found  (this server uses /etc/network/interfaces)"); return; }
            string verb = a.Count > 1 ? a[1] : "";
            if (verb != "apply" && verb != "try" && verb != "generate" && verb != "get") { Print("usage: netplan [-h] [--debug] apply | try | generate | get"); return; }
            if (verb == "get") { string t = FileText(NetplanFile) ?? ""; Print(t); return; }
            if (!IsRoot) { Print("Permission denied (you need sudo: sudo netplan " + verb + ")"); return; }
            string err = ApplyNetplan(verb != "generate");
            if (err.Length > 0) { Print(err); return; }
            if (verb == "try") Print("Do you want to keep these settings?\n\nPress ENTER before the timeout to accept the new configuration\n\nConfiguration accepted.");
        }

        /// <summary>Reads the netplan file and uses it. Returns "" or the error, worded like netplan's.</summary>
        public string ApplyNetplan(bool apply)
        {
            string text = FileText(NetplanFile);
            if (text == null) return "";
            YamlNode root; string err;
            if (!Yaml.Parse(text, out root, out err)) return NetplanFile + ":" + err;
            var net = root.Get("network");
            if (net == null) return NetplanFile + ":1:1: Error in network definition: expected a 'network:' section at the top";
            foreach (var key in net.Map.Keys)
                if (key != "version" && key != "ethernets" && key != "wifis" && key != "renderer") return NetplanFile + ":" + net.Map[key].Line + ":" + net.Map[key].Col + ": Error in network definition: unknown key '" + key + "'\n" + Yaml.Show(text, net.Map[key].Line, net.Map[key].Col);
            var plans = new List<Action>();
            foreach (var sect in new[] { "ethernets", "wifis" })
            {
                var e = net.Get(sect);
                if (e == null) continue;
                foreach (var kv in e.Map)
                {
                    var f = Dev(kv.Key);
                    if (f == null) return NetplanFile + ":" + kv.Value.Line + ":" + kv.Value.Col + ": Error in network definition: there is no interface called '" + kv.Key + "' (ip link shows the names)\n" + Yaml.Show(text, kv.Value.Line, kv.Value.Col);
                    var c = kv.Value;
                    bool dhcp = false; uint ip = 0, mask = 0, gw = 0; var dns = new List<uint>();
                    string ssid = null, key = "";
                    foreach (var p in c.Map)
                    {
                        switch (p.Key)
                        {
                            case "dhcp4":
                                if (p.Value.Text != "true" && p.Value.Text != "false" && p.Value.Text != "yes" && p.Value.Text != "no") return Err(text, p.Value, "invalid boolean value '" + p.Value.Text + "'");
                                dhcp = p.Value.Text == "true" || p.Value.Text == "yes"; break;
                            case "dhcp6": case "optional": case "renderer": break;
                            case "addresses":
                                {
                                    if (p.Value.List.Count == 0) return Err(text, p.Value, "addresses must be a list, like [192.168.1.20/24]");
                                    string cidr = p.Value.List[0].Text;
                                    int s = cidr.IndexOf('/');
                                    if (s < 0) return Err(text, p.Value.List[0], "address '" + cidr + "' is missing /prefixlength");
                                    if (!Ip.TryParse(cidr.Substring(0, s), out ip) || !Ip.TryParseMask(cidr.Substring(s + 1), out mask)) return Err(text, p.Value.List[0], "malformed address '" + cidr + "', must be X.X.X.X/NN");
                                    break;
                                }
                            case "gateway4":
                                if (!Ip.TryParse(p.Value.Text, out gw)) return Err(text, p.Value, "invalid IPv4 address '" + p.Value.Text + "'");
                                break;
                            case "routes":
                                foreach (var r in p.Value.List)
                                {
                                    var to = r.Get("to"); var via = r.Get("via");
                                    if (to == null || via == null) return Err(text, r, "a route needs 'to' and 'via'");
                                    if (to.Text != "default" && to.Text != "0.0.0.0/0") continue;
                                    if (!Ip.TryParse(via.Text, out gw)) return Err(text, via, "invalid IPv4 address '" + via.Text + "'");
                                }
                                break;
                            case "nameservers":
                                {
                                    var ad = p.Value.Get("addresses");
                                    if (ad != null) foreach (var x in ad.List) { uint v; if (!Ip.TryParse(x.Text, out v)) return Err(text, x, "malformed address '" + x.Text + "', must be X.X.X.X"); dns.Add(v); }
                                    foreach (var k2 in p.Value.Map.Keys) if (k2 != "addresses" && k2 != "search") return Err(text, p.Value.Map[k2], "unknown key '" + k2 + "'");
                                    break;
                                }
                            case "access-points":
                                foreach (var ap in p.Value.Map) { ssid = ap.Key; var pw = ap.Value.Get("password"); if (pw != null) key = pw.Text; }
                                break;
                            default:
                                return Err(text, p.Value, "unknown key '" + p.Key + "'");
                        }
                    }
                    if (!dhcp && ip == 0) return NetplanFile + ":" + c.Line + ":" + c.Col + ": Error in network definition: " + kv.Key + ": no address given (use dhcp4: true, or addresses:)\n" + Yaml.Show(text, c.Line, c.Col);
                    var fc = f; bool dc = dhcp; uint ic = ip, mc = mask, gc = gw; var dnsc = dns; string sc = ssid, kc = key;
                    plans.Add(() =>
                    {
                        if (sc != null) H.SetWifi(fc.Port, sc, kc);
                        if (dc) { H.SetDhcp(fc); fc.Dns.Clear(); if (dnsc.Count > 0) fc.Dns.AddRange(dnsc); }
                        else
                        {
                            H.SetStatic(fc, Ip.Str(ic), Ip.Str(mc), gc != 0 ? Ip.Str(gc) : "", dnsc.Count > 0 ? Ip.Str(dnsc[0]) : "", dnsc.Count > 1 ? Ip.Str(dnsc[1]) : "");
                            if (gc != 0 && !Ip.SameNet(gc, ic, mc)) fc.Gateway = gc;   // netplan allows it; it just won't work
                        }
                        H.DnsCache.Clear();
                    });
                }
            }
            if (apply) foreach (var p in plans) p();
            string warn = text.Contains("gateway4") ? "\n** (generate:1234): WARNING **: `gateway4` has been deprecated, use default routes instead.\nSee the 'Default routes' section of the documentation for more details." : "";
            return warn.Length > 0 ? warn.Trim() : "";
        }

        static string Err(string text, YamlNode n, string msg)
        {
            return NetplanFile + ":" + n.Line + ":" + n.Col + ": Error in network definition: " + msg + "\n" + Yaml.Show(text, n.Line, n.Col);
        }

        // ---------------------------------------------------------------- Debian: /etc/network/interfaces

        /// <summary>Reads /etc/network/interfaces and uses it. Returns "" or the error.</summary>
        public string ApplyInterfaces(bool up)
        {
            string text = FileText(InterfacesFile) ?? "";
            var lines = text.Replace("\r", "").Split('\n');
            IpIf cur = null; string method = null; uint ip = 0, mask = 0, gw = 0;
            var todo = new List<Action>();
            Action flush = null;
            flush = () =>
            {
                if (cur == null) return;
                var f = cur; string m = method; uint a = ip, k = mask, g = gw;
                todo.Add(() =>
                {
                    if (m == "dhcp") H.SetDhcp(f);
                    else if (m == "static") { H.SetStatic(f, Ip.Str(a), Ip.Str(k), g != 0 ? Ip.Str(g) : "", "", ""); f.Gateway = g; }
                    H.DnsCache.Clear();
                });
                cur = null;
            };
            for (int i = 0; i < lines.Length; i++)
            {
                string l = lines[i].Trim();
                if (l.Length == 0 || l.StartsWith("#")) continue;
                var w = l.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                string where = InterfacesFile + ":" + (i + 1) + ": ";
                switch (w[0])
                {
                    case "auto": case "allow-hotplug": case "source": break;
                    case "iface":
                        flush();
                        if (w.Length < 4 || w[2] != "inet") return where + "misplaced option or unknown address family (write: iface eth0 inet static)";
                        if (w[1] == "lo") { cur = null; method = "loopback"; break; }
                        cur = Dev(w[1]);
                        if (cur == null) return where + "unknown interface " + w[1] + " (ip link shows the names)";
                        method = w[3];
                        if (method != "static" && method != "dhcp" && method != "manual") return where + "unknown method " + method + " (use static or dhcp)";
                        ip = 0; mask = 0; gw = 0;
                        if (method == "manual") cur = null;
                        break;
                    case "address":
                        {
                            if (cur == null) break;
                            if (w.Length < 2) return where + "address needs a value";
                            string v = w[1]; int s = v.IndexOf('/');
                            if (!Ip.TryParse(s > 0 ? v.Substring(0, s) : v, out ip)) return where + "invalid address " + v;
                            if (s > 0 && !Ip.TryParseMask(v.Substring(s + 1), out mask)) return where + "invalid prefix in " + v;
                            break;
                        }
                    case "netmask": if (cur == null) break; if (w.Length < 2 || !Ip.TryParseMask(w[1], out mask)) return where + "invalid netmask"; break;
                    case "gateway": if (cur == null) break; if (w.Length < 2 || !Ip.TryParse(w[1], out gw)) return where + "invalid gateway"; break;
                    case "dns-nameservers": case "dns-search": case "broadcast": case "network": break;
                    default: return where + "unknown option '" + w[0] + "'";
                }
                if (w[0] == "address" || w[0] == "netmask") { }
            }
            // check before using
            flush();
            if (up) foreach (var t in todo) t();
            return "";
        }

        // ---------------------------------------------------------------- services
        void Systemctl(List<string> a)
        {
            string verb = a.Count > 1 ? a[1] : "";
            string unit = a.Count > 2 ? a[2].Replace(".service", "") : "";
            if (unit == "sshd") unit = "ssh";
            if (verb == "status")
            {
                bool on = unit == "apache2" ? H.HttpOn : unit == "ssh" ? H.SshOn : unit == "networking" ? H.State.Str("networkingError").Length == 0 : unit == "systemd-networkd" || unit == "systemd-resolved";
                string desc = unit == "apache2" ? "The Apache HTTP Server" : unit == "ssh" ? "OpenBSD Secure Shell server" : unit == "networking" ? "Raise network interfaces" : unit;
                if (unit != "apache2" && unit != "ssh" && unit != "networking" && unit != "systemd-networkd" && unit != "systemd-resolved") { Print("Unit " + unit + ".service could not be found."); return; }
                Print((on ? "* " : "x ") + unit + ".service - " + desc + "\n     Loaded: loaded (/lib/systemd/system/" + unit + ".service; enabled; preset: enabled)\n     Active: " + (on ? "active (running)" : (unit == "networking" ? "failed (Result: exit-code)" : "inactive (dead)")) + "\n" + (unit == "networking" && H.State.Str("networkingError").Length > 0 ? "\n" + H.State.Str("networkingError") : ""));
                return;
            }
            if (verb != "start" && verb != "stop" && verb != "restart" && verb != "enable" && verb != "disable" && verb != "reload") { Print("Unknown command verb " + verb + "."); return; }
            if (!IsRoot) { Print("Failed to " + verb + " " + unit + ".service: Access denied\nSee system logs and 'systemctl status " + unit + ".service' for details.  (use sudo)"); return; }
            switch (unit)
            {
                case "apache2": if (verb != "enable" && verb != "disable") H.HttpOn = verb != "stop"; return;
                case "ssh": if (verb != "enable" && verb != "disable") H.SshOn = verb != "stop"; return;
                case "networking":
                    if (UsesNetplan) { Print("Failed to " + verb + " networking.service: Unit networking.service not found.\n(This system uses netplan: sudo netplan apply)"); return; }
                    if (verb == "stop") { foreach (var f in H.Adapters) D.Ip.SetAddress(f, 0, 0); return; }
                    string err = ApplyInterfaces(true);
                    H.State["networkingError"] = err;

                    if (err.Length > 0) Print("Job for networking.service failed because the control process exited with error code.\nSee \"systemctl status networking.service\" and \"journalctl -xeu networking.service\" for details.");
                    return;
                case "systemd-networkd": case "systemd-resolved": if (UsesNetplan) { string e2 = ApplyNetplan(true); if (e2.Length > 0 && !e2.StartsWith("**")) Print(e2); } return;
            }
            Print("Failed to " + verb + " " + unit + ".service: Unit " + unit + ".service not found.");
        }

        void Ss()
        {
            var sb = new StringBuilder("Netid State  Recv-Q Send-Q Local Address:Port  Peer Address:Port Process\n");
            sb.Append("udp   UNCONN 0      0       127.0.0.53%lo:53         0.0.0.0:*\n");
            if (H.Adapters.Any(f => f.Dhcp)) sb.Append("udp   UNCONN 0      0       0.0.0.0%" + EthName + ":68  0.0.0.0:*\n");
            if (H.SshOn) sb.Append("tcp   LISTEN 0      128           0.0.0.0:22        0.0.0.0:*     users:((\"sshd\",pid=702,fd=3))\n");
            if (H.HttpOn) sb.Append("tcp   LISTEN 0      511                 *:80              *:*     users:((\"apache2\",pid=812,fd=4))\n");
            if (D.Ip.DnsSrv != null && D.Ip.DnsSrv.Enabled) sb.Append("udp   UNCONN 0      0             0.0.0.0:53        0.0.0.0:*     users:((\"named\",pid=640,fd=5))\n");
            Print(sb.ToString().TrimEnd('\n'));
        }

        void Ufw(List<string> a)
        {
            if (!IsRoot) { Print("ERROR: You need to be root to run this script  (use sudo)"); return; }
            string verb = a.Count > 1 ? a[1] : "";
            switch (verb)
            {
                case "enable": H.Ufw = true; Print("Firewall is active and enabled on system startup"); return;
                case "disable": H.Ufw = false; Print("Firewall stopped and disabled on system startup"); return;
                case "reset": H.Ufw = false; H.UfwRules.Clear(); Print("Resetting all rules to installed defaults.\nFirewall stopped and disabled on system startup"); return;
                case "status":
                    {
                        if (!H.Ufw) { Print("Status: inactive"); return; }
                        var sb = new StringBuilder("Status: active\n\nTo                         Action      From\n--                         ------      ----\n");
                        foreach (var r in H.UfwRules) sb.Append((r.Port + (r.Proto == "any" ? "" : "/" + r.Proto)).PadRight(27) + (r.Allow ? "ALLOW" : "DENY").PadRight(12) + "Anywhere\n");
                        Print(sb.ToString().TrimEnd('\n'));
                        return;
                    }
                case "allow": case "deny":
                    {
                        if (a.Count < 3) { Print("ERROR: Need a port: ufw " + verb + " 22"); return; }
                        string spec = a[2].ToLowerInvariant();
                        string proto = "any";
                        int slash = spec.IndexOf('/');
                        if (slash > 0) { proto = spec.Substring(slash + 1); spec = spec.Substring(0, slash); }
                        int port = spec == "ssh" || spec == "openssh" ? 22 : spec == "http" || spec == "apache" ? 80 : spec == "https" ? 443 : spec == "dns" ? 53 : -1;
                        if (port < 0 && !int.TryParse(spec, out port)) { Print("ERROR: Could not find a profile matching '" + a[2] + "'"); return; }
                        if (proto == "any" && (spec == "ssh" || spec == "http" || spec == "https")) proto = "tcp";
                        H.UfwRules.RemoveAll(r => r.Port == port && r.Proto == proto);
                        H.UfwRules.Add(new FwRule { Port = port, Proto = proto, Allow = verb == "allow" });
                        Print("Rule added");
                        return;
                    }
                case "delete":
                    {
                        if (a.Count < 4) { Print("ERROR: Usage: ufw delete allow 22"); return; }
                        int port; string spec = a[3].Split('/')[0];
                        if (!int.TryParse(spec, out port)) port = spec == "ssh" ? 22 : spec == "http" ? 80 : -1;
                        int n = H.UfwRules.RemoveAll(r => r.Port == port);
                        Print(n > 0 ? "Rule deleted" : "Could not delete non-existent rule");
                        return;
                    }
            }
            Print("ERROR: Invalid syntax\n\nUsage: ufw COMMAND\n  enable | disable | status | allow <port>[/tcp] | deny <port> | delete allow <port> | reset");
        }

        void HostnameCmd(List<string> a)
        {
            if (a.Count == 1) { Print(H.HostName); return; }
            if (a[1] == "-I") { Print(string.Join(" ", H.Adapters.Where(f => f.Ip != 0).Select(f => Ip.Str(f.Ip)).ToArray())); return; }
            if (!IsRoot) { Print("hostname: you must be root to change the host name"); return; }
            H.ComputerName = a[1];
        }

        void Hostnamectl(List<string> a)
        {
            if (a.Count >= 3 && a[1] == "set-hostname")
            {
                if (!IsRoot) { Print("Could not set static hostname: Access denied  (use sudo)"); return; }
                H.ComputerName = a[2]; H.Files["/etc/hostname"] = a[2] + "\n";
                return;
            }
            Print(" Static hostname: " + H.HostName + "\n       Icon name: computer-vm\n Operating System: " + Pers.Title + " (simulated by Network Lab)\n          Kernel: Linux 6.8.0-netlab\n    Architecture: x86-64");
        }

        // ---------------------------------------------------------------- NetworkManager (desktop)
        void Nmcli(List<string> a)
        {
            if (Lp == null || !Lp.Desktop) { Print("nmcli: command not found  (this server uses " + (UsesNetplan ? "netplan" : "/etc/network/interfaces") + ")"); return; }
            var w = a.Skip(1).ToList();
            string obj = w.Count > 0 ? w[0] : "";
            if (obj == "" || obj == "g" || obj == "general")
            {
                var sb = new StringBuilder();
                foreach (var f in H.Adapters) sb.Append(Pers.IfName(f.Port) + ": " + (f.Up ? "connected to " + ConnName(f) : (f.Port.IsWireless ? "disconnected" : "unavailable")) + "\n" + (f.Ip != 0 ? "        inet4 " + Ip.Str(f.Ip) + "/" + Ip.PrefixOf(f.Mask) + "\n" + (f.Gateway != 0 ? "        route4 default via " + Ip.Str(f.Gateway) + "\n" : "") : "") + "\n");
                sb.Append("DNS configuration:\n" + string.Join("\n", H.DnsServers.Select(x => "        servers: " + Ip.Str(x)).ToArray()));
                Print(sb.ToString().TrimEnd('\n'));
                return;
            }
            if ((obj == "d" || obj == "device") && (w.Count == 1 || w[1] == "status"))
            {
                var sb = new StringBuilder("DEVICE  TYPE      STATE         CONNECTION\n");
                foreach (var f in H.Adapters) sb.Append(Pers.IfName(f.Port).PadRight(8) + (f.Port.IsWireless ? "wifi" : "ethernet").PadRight(10) + (f.Up ? "connected" : (f.Port.IsWireless ? "disconnected" : "unavailable")).PadRight(14) + (f.Up ? ConnName(f) : "--") + "\n");
                sb.Append("lo      loopback  connected (externally)  lo");
                Print(sb.ToString());
                return;
            }
            if ((obj == "d" || obj == "device") && w.Count >= 3 && w[1] == "wifi" && w[2] == "list")
            {
                var radio = H.Adapters.FirstOrDefault(f => f.Port.IsWireless);
                if (radio == null) { Print("Error: no Wi-Fi device found."); return; }
                var sb = new StringBuilder("IN-USE  SSID            MODE   CHAN  SIGNAL  SECURITY\n");
                var wc = H.WifiFor(radio.Port);
                foreach (var ap in D.P.Devices.Where(x => x.Ap != null && x.Ap.Enabled && x.PoweredOn))
                {
                    double s = Physical.Signal(D.P, ap, D);
                    if (s <= 0) continue;
                    sb.Append((wc != null && wc.Status == "connected" && wc.Ssid == ap.Ap.Ssid ? "*" : " ").PadRight(8) + ap.Ap.Ssid.PadRight(16) + "Infra  " + ap.Ap.Channel.ToString().PadRight(6) + ((int)(s * 100)).ToString().PadRight(8) + (ap.Ap.Security == "open" ? "--" : "WPA2") + "\n");
                }
                Print(sb.ToString().TrimEnd('\n'));
                return;
            }
            if ((obj == "d" || obj == "device") && w.Count >= 4 && w[1] == "wifi" && w[2] == "connect")
            {
                var radio = H.Adapters.FirstOrDefault(f => f.Port.IsWireless);
                if (radio == null) { Print("Error: no Wi-Fi device found."); return; }
                string ssid = w[3];
                int pi = w.IndexOf("password");
                string key = pi >= 0 && pi + 1 < w.Count ? w[pi + 1] : "";
                H.SetWifi(radio.Port, ssid, key);
                var wc = H.WifiFor(radio.Port);
                if (radio.Ip == 0 && !radio.Dhcp) H.SetDhcp(radio);
                if (wc.Status == "connected") { if (radio.Dhcp) H.EnsureDhcp(radio); Print("Device '" + Pers.IfName(radio.Port) + "' successfully activated with '" + ssid + "'."); }
                else if (wc.Status == "wrong-key") Print("Error: Connection activation failed: Secrets were required, but not provided.");
                else Print("Error: No network with SSID '" + ssid + "' found.");
                return;
            }
            if ((obj == "c" || obj == "con" || obj == "connection") && (w.Count == 1 || w[1] == "show"))
            {
                var sb = new StringBuilder("NAME                UUID                                  TYPE      DEVICE\n");
                foreach (var f in H.Adapters) sb.Append(ConnName(f).PadRight(20) + "6f1c2a8e-0000-4e1b-9d2f-00000000000" + f.Port.Index + "  " + (f.Port.IsWireless ? "wifi" : "ethernet").PadRight(10) + (f.Up ? Pers.IfName(f.Port) : "--") + "\n");
                Print(sb.ToString().TrimEnd('\n'));
                return;
            }
            if ((obj == "c" || obj == "con" || obj == "connection") && w.Count >= 3 && (w[1] == "modify" || w[1] == "mod"))
            {
                var f = H.Adapters.FirstOrDefault(x => ConnName(x) == w[2] || Pers.IfName(x.Port) == w[2]);
                if (f == null) { Print("Error: unknown connection '" + w[2] + "'."); return; }
                string key = "nm_" + f.Port.Index + "_";
                for (int i = 3; i + 1 < w.Count; i += 2)
                {
                    string prop = w[i].TrimStart('+'), val = w[i + 1];
                    if (prop != "ipv4.method" && prop != "ipv4.addresses" && prop != "ipv4.gateway" && prop != "ipv4.dns") { Print("Error: invalid property '" + prop + "'."); return; }
                    H.State[key + prop] = val;
                }
                return;
            }
            if ((obj == "c" || obj == "con" || obj == "connection") && w.Count >= 3 && w[1] == "up")
            {
                var f = H.Adapters.FirstOrDefault(x => ConnName(x) == w[2] || Pers.IfName(x.Port) == w[2]);
                if (f == null) { Print("Error: unknown connection '" + w[2] + "'."); return; }
                string key = "nm_" + f.Port.Index + "_";
                string method = H.State.Str(key + "ipv4.method", f.Dhcp ? "auto" : "manual");
                if (method == "auto") { H.SetDhcp(f); string d0 = H.State.Str(key + "ipv4.dns"); uint dv; if (d0.Length > 0 && Ip.TryParse(d0.Split(',')[0], out dv)) { } }
                else
                {
                    string cidr = H.State.Str(key + "ipv4.addresses");
                    int s = cidr.IndexOf('/');
                    if (s < 0) { Print("Error: Connection activation failed: ipv4.addresses must look like 192.168.1.20/24."); return; }
                    var dns = H.State.Str(key + "ipv4.dns").Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    string err = H.SetStatic(f, cidr.Substring(0, s), cidr.Substring(s + 1), H.State.Str(key + "ipv4.gateway"), dns.Length > 0 ? dns[0] : "", dns.Length > 1 ? dns[1] : "");
                    if (err.Length > 0) { Print("Error: Connection activation failed: " + err); return; }
                }
                Print("Connection successfully activated (D-Bus active path: /org/freedesktop/NetworkManager/ActiveConnection/" + (f.Port.Index + 2) + ")");
                return;
            }
            Print("Error: argument '" + string.Join(" ", w.ToArray()) + "' not understood. Try: nmcli device status, nmcli device wifi list, nmcli device wifi connect <SSID> password <key>, nmcli connection modify ..., nmcli connection up ...");
        }

        protected string ConnName(IpIf f)
        {
            if (f.Port.IsWireless) { var w = H.WifiFor(f.Port); return w != null && w.Ssid.Length > 0 ? w.Ssid : "Wi-Fi"; }
            return "Wired connection 1";
        }

        // ---------------------------------------------------------------- web and ssh
        protected void Curl(List<string> a)
        {
            string url = a.Skip(1).FirstOrDefault(x => !x.StartsWith("-"));
            if (url == null) { Print("curl: try 'curl http://192.168.1.10/'"); return; }
            Hold("curl");
            Request.Http(D, url, "GET", null, (m, err) =>
            {
                Release();
                if (m == null)
                {
                    string host, path; int port; Request.ParseUrl(url, out host, out port, out path);
                    if (err.StartsWith("name")) Print("curl: (6) Could not resolve host: " + host);
                    else if (err == "refused") Print("curl: (7) Failed to connect to " + host + " port " + port + " after 1 ms: Connection refused");
                    else if (err == "https") Print("curl: (35) This simulator has no HTTPS: use http://");
                    else Print("curl: (28) Failed to connect to " + host + " port " + port + " after 4000 ms: Timeout was reached");
                    return;
                }
                Print(m.Body);
            });
        }

        void Ssh(List<string> a)
        {
            string target = a.Skip(1).FirstOrDefault(x => !x.StartsWith("-"));
            if (target == null) { Print("usage: ssh user@host"); return; }
            string user = "student", host = target;
            int at = target.IndexOf('@');
            if (at > 0) { user = target.Substring(0, at); host = target.Substring(at + 1); }
            Hold("ssh");
            Request.Send(D, host, 22, new TextMsg { Proto = "SSH", Line = "HELLO" }, (m, err, ip) =>
            {
                Release();
                if (m == null && ip == 0) { Print("ssh: Could not resolve hostname " + host + ": Name or service not known"); return; }
                if (m == null) { Print(err == "refused" ? "ssh: connect to host " + host + " port 22: Connection refused" : "ssh: connect to host " + host + " port 22: Connection timed out"); return; }
                Action login = () => Ask(user + "@" + host + "'s password: ", true, pw =>
                {
                    var req = new TextMsg { Proto = "SSH", Line = "LOGIN " + user + " " + pw };
                    Hold("ssh");
                    Request.Send(D, Ip.Str(ip), 22, req, (r, e2, ip2) =>
                    {
                        Release();
                        if (r == null || r.Line != "OK") { Print("Permission denied, please try again."); return; }
                        sshHost = host; sshUser = user; sshPass = pw; sshIp = ip;
                        var remote = D.P.Devices.FirstOrDefault(x => x.Ip != null && x.Ip.IsMine(ip));
                        sshRemoteName = remote != null ? remote.HostName : host;
                        Print("Welcome! (a simulated SSH session to " + sshRemoteName + ")\nLast login: just now\n");
                    });
                    return true;
                });
                string known = H.State.Str("known_" + Ip.Str(ip));
                if (known.Length == 0)
                {
                    Ask("The authenticity of host '" + host + " (" + Ip.Str(ip) + ")' can't be established.\nED25519 key fingerprint is SHA256:" + Convert.ToBase64String(BitConverter.GetBytes(ip)).TrimEnd('=') + "netlab.\nAre you sure you want to continue connecting (yes/no/[fingerprint])? ", false, ans =>
                    {
                        if (ans.Trim().ToLowerInvariant() != "yes") { Print("Host key verification failed."); return true; }
                        H.State["known_" + Ip.Str(ip)] = "yes";
                        Print("Warning: Permanently added '" + host + "' (ED25519) to the list of known hosts.");
                        login();
                        return true;
                    });
                }
                else login();
            });
        }

        void RemoteCommand(string t)
        {
            if (t == "exit" || t == "logout") { Print("logout\nConnection to " + sshHost + " closed."); sshHost = null; return; }
            Hold("ssh");
            var req = new TextMsg { Proto = "SSH", Line = "CMD " + t };
            Request.Send(D, Ip.Str(sshIp), 22, req, (m, err, ip) =>
            {
                Release();
                if (m == null) { Print("client_loop: send disconnect: Broken pipe"); sshHost = null; return; }
                Write(m.Body);
                Version++;
            });
        }
    }

    // ================================================================= macOS-like terminal
    public class MacShell : LinuxShell
    {
        public MacShell(Device d) : base(d) { Title = "Terminal"; }
        public override string Prompt { get { return sshHost != null ? sshUser + "@" + sshRemoteName + ":~$ " : "student@" + H.HostName + " ~ % "; } }
        protected override string[] Words { get { return new[] { "ifconfig", "networksetup", "ping", "traceroute", "nslookup", "dig", "arp", "netstat", "ipconfig", "scutil", "hostname", "curl", "ssh", "sudo", "clear", "exit", "help" }; } }

        protected override void Help()
        {
            Print("Commands this terminal knows:\n  ifconfig [en0]                   addresses of each adapter\n  ipconfig getifaddr en0           just the address\n  networksetup -listallnetworkservices\n  networksetup -getinfo Wi-Fi\n  networksetup -setmanual Wi-Fi <ip> <mask> <router>    (or -setdhcp Wi-Fi)\n  networksetup -setdnsservers Wi-Fi <dns> [dns2]        (Empty clears them)\n  networksetup -getdnsservers Wi-Fi\n  networksetup -setairportnetwork en0 <SSID> <password>\n  ping, traceroute, nslookup, dig, arp -a, netstat -rn, scutil --dns, curl, ssh, clear, exit");
        }

        protected override void Command(List<string> a, string line)
        {
            switch (a[0])
            {
                case "ifconfig": Ifconfig(a); return;
                case "networksetup": Networksetup(a); return;
                case "ping": Ping(a, true); return;
                case "ipconfig":
                    if (a.Count >= 3 && a[1] == "getifaddr") { var f = Dev(a[2]); if (f != null && f.Ip != 0) Print(Ip.Str(f.Ip)); return; }
                    Print("usage: ipconfig getifaddr en0"); return;
                case "arp":
                    {
                        var sb = new StringBuilder();
                        foreach (var e in D.Ip.ArpEntries()) sb.Append("? (" + Ip.Str(e.Ip) + ") at " + Mac.Colon(e.Mac) + " on " + Pers.IfName(e.If.Port) + " ifscope [ethernet]\n");
                        Print(sb.ToString().TrimEnd('\n'));
                        return;
                    }
                case "netstat":
                    {
                        var sb = new StringBuilder("Routing tables\n\nInternet:\nDestination        Gateway            Flags           Netif Expire\n");
                        foreach (var f in H.Adapters.Where(x => x.Up && x.Ip != 0 && x.Gateway != 0)) sb.Append("default            " + Ip.Str(f.Gateway).PadRight(19) + "UGScg".PadRight(16) + Pers.IfName(f.Port) + "\n");
                        foreach (var f in H.Adapters.Where(x => x.Up && x.Ip != 0)) sb.Append(Ip.Cidr(f.Ip & f.Mask, f.Mask).PadRight(19) + ("link#" + (f.Port.Index + 4)).PadRight(19) + "UCS".PadRight(16) + Pers.IfName(f.Port) + "\n");
                        Print(sb.ToString().TrimEnd('\n'));
                        return;
                    }
                case "scutil":
                    {
                        var dns = H.DnsServers;
                        var sb = new StringBuilder("DNS configuration\n\nresolver #1\n");
                        for (int i = 0; i < dns.Count; i++) sb.Append("  nameserver[" + i + "] : " + Ip.Str(dns[i]) + "\n");
                        if (dns.Count == 0) sb.Append("  (no DNS servers)\n");
                        Print(sb.ToString().TrimEnd('\n'));
                        return;
                    }
                case "ip": Print("zsh: command not found: ip  (on a Mac: ifconfig, networksetup)"); return;
                case "netplan": case "nmcli": case "systemctl": case "ufw": Print("zsh: command not found: " + a[0]); return;
            }
            base.Command(a, line);
        }

        void Ifconfig(List<string> a)
        {
            string only = a.Count > 1 ? a[1] : null;
            var sb = new StringBuilder();
            if (only == null || only == "lo0") sb.Append("lo0: flags=8049<UP,LOOPBACK,RUNNING,MULTICAST> mtu 16384\n\tinet 127.0.0.1 netmask 0xff000000\n");
            foreach (var f in H.Adapters)
            {
                string n = Pers.IfName(f.Port);
                if (only != null && only != n) continue;
                sb.Append(n + ": flags=8863<UP,BROADCAST,SMART,RUNNING,SIMPLEX,MULTICAST> mtu 1500\n\tether " + Mac.Colon(f.Mac) + "\n");
                if (f.Ip != 0) sb.Append("\tinet " + Ip.Str(f.Ip) + " netmask 0x" + f.Mask.ToString("x8") + " broadcast " + Ip.Str(Ip.BroadcastOf(f.Ip, f.Mask)) + "\n");
                sb.Append("\tmedia: autoselect\n\tstatus: " + (f.Up ? "active" : "inactive") + "\n");
            }
            if (only != null && sb.Length == 0) { Print("ifconfig: interface " + only + " does not exist"); return; }
            Print(sb.ToString().TrimEnd('\n'));
        }

        IpIf Service(string name)
        {
            if (name == null) return null;
            string n = name.ToLowerInvariant();
            foreach (var f in H.Adapters) if ((f.Port.IsWireless && (n == "wi-fi" || n == "wifi" || n == "airport")) || (!f.Port.IsWireless && (n == "ethernet" || n == "usb 10/100/1000 lan"))) return f;
            return Dev(name);
        }

        void Networksetup(List<string> a)
        {
            string op = a.Count > 1 ? a[1].ToLowerInvariant() : "";
            string svc = a.Count > 2 ? a[2] : null;
            switch (op)
            {
                case "-listallnetworkservices":
                    Print("An asterisk (*) denotes that a network service is disabled.\n" + string.Join("\n", H.Adapters.Select(f => f.Port.IsWireless ? "Wi-Fi" : "Ethernet").ToArray()));
                    return;
                case "-getinfo":
                    {
                        var f = Service(svc);
                        if (f == null) { Print(svc + " is not a recognized network service."); return; }
                        Print((f.Dhcp ? "DHCP Configuration" : "Manual Configuration") + "\nIP address: " + (f.Ip != 0 ? Ip.Str(f.Ip) : "none") + "\nSubnet mask: " + (f.Mask != 0 ? Ip.Str(f.Mask) : "none") + "\nRouter: " + (f.Gateway != 0 ? Ip.Str(f.Gateway) : "none") + "\nClient ID:\nIPv6: Automatic\n" + (f.Port.IsWireless ? "Wi-Fi ID: " : "Ethernet Address: ") + Mac.Colon(f.Mac));
                        return;
                    }
                case "-setmanual":
                    {
                        var f = Service(svc);
                        if (f == null) { Print(svc + " is not a recognized network service."); return; }
                        if (a.Count < 5) { Print("Usage: networksetup -setmanual <networkservice> <ip> <subnet> <router>"); return; }
                        var keep = f.Dns.Select(x => Ip.Str(x)).ToList();
                        string err = H.SetStatic(f, a[3], a[4], a.Count > 5 ? a[5] : "", keep.Count > 0 ? keep[0] : "", keep.Count > 1 ? keep[1] : "");
                        if (err.Length > 0) Print("Error: " + err);
                        return;
                    }
                case "-setdhcp":
                    {
                        var f = Service(svc);
                        if (f == null) { Print(svc + " is not a recognized network service."); return; }
                        H.SetDhcp(f);
                        return;
                    }
                case "-setdnsservers":
                    {
                        var f = Service(svc);
                        if (f == null) { Print(svc + " is not a recognized network service."); return; }
                        f.Dns.Clear();
                        foreach (var x in a.Skip(3)) { if (x.ToLowerInvariant() == "empty") break; uint ip; if (!Ip.TryParse(x, out ip)) { Print(x + " is not a valid IP address."); return; } f.Dns.Add(ip); }
                        H.DnsCache.Clear();
                        return;
                    }
                case "-getdnsservers":
                    {
                        var f = Service(svc);
                        if (f == null) { Print(svc + " is not a recognized network service."); return; }
                        Print(f.Dns.Count == 0 ? "There aren't any DNS Servers set on " + svc + "." : string.Join("\n", f.Dns.Select(x => Ip.Str(x)).ToArray()));
                        return;
                    }
                case "-setairportnetwork":
                    {
                        var radio = H.Adapters.FirstOrDefault(f => f.Port.IsWireless);
                        if (radio == null || a.Count < 4) { Print("Usage: networksetup -setairportnetwork en0 <SSID> [password]"); return; }
                        H.SetWifi(radio.Port, a[3], a.Count > 4 ? a[4] : "");
                        var wc = H.WifiFor(radio.Port);
                        if (wc.Status == "connected") { if (radio.Dhcp) H.EnsureDhcp(radio); return; }
                        Print(wc.Status == "wrong-key" ? "Failed to join network " + a[3] + ".\nError: -3900  The operation couldn't be completed." : "Could not find network " + a[3] + ".");
                        return;
                    }
                case "-getairportnetwork":
                    {
                        var radio = H.Adapters.FirstOrDefault(f => f.Port.IsWireless);
                        var wc = radio != null ? H.WifiFor(radio.Port) : null;
                        Print(wc != null && wc.Status == "connected" ? "Current Wi-Fi Network: " + wc.Ssid : "You are not associated with an AirPort network.");
                        return;
                    }
            }
            Print("networksetup: try -listallnetworkservices, -getinfo Wi-Fi, -setmanual, -setdhcp, -setdnsservers, -getdnsservers, -setairportnetwork");
        }
    }

    // ================================================================= a very small YAML reader (enough for netplan)
    public class YamlNode
    {
        public string Text;                                     // a scalar
        public readonly Dictionary<string, YamlNode> Map = new Dictionary<string, YamlNode>();
        public readonly List<YamlNode> List = new List<YamlNode>();
        public int Line, Col;
        public YamlNode Get(string k) { YamlNode n; return Map.TryGetValue(k, out n) ? n : null; }
    }

    public static class Yaml
    {
        class L { public int Indent, Line; public string Text; }

        public static bool Parse(string text, out YamlNode root, out string err)
        {
            root = new YamlNode { Line = 1, Col = 1 };
            err = "";
            var lines = new List<L>();
            var raw = text.Replace("\r", "").Split('\n');
            for (int i = 0; i < raw.Length; i++)
            {
                string s = raw[i];
                int hash = s.IndexOf(" #"); if (s.TrimStart().StartsWith("#")) continue; if (hash >= 0) s = s.Substring(0, hash);
                if (s.Trim().Length == 0) continue;
                if (s.Contains("\t")) { err = (i + 1) + ":" + (s.IndexOf('\t') + 1) + ": Invalid YAML: tabs are not allowed for indent:\n" + raw[i] + "\n" + new string(' ', s.IndexOf('\t')) + "^"; return false; }
                int ind = 0; while (ind < s.Length && s[ind] == ' ') ind++;
                lines.Add(new L { Indent = ind, Line = i + 1, Text = s.Trim() });
            }
            int k = 0;
            return Block(lines, ref k, 0, root, out err, text) && k >= lines.Count || Fail(lines, k, ref err);
        }

        static bool Fail(List<L> lines, int k, ref string err)
        {
            if (err.Length == 0 && k < lines.Count) err = lines[k].Line + ":" + (lines[k].Indent + 1) + ": Invalid YAML: inconsistent indentation:\n" + new string(' ', lines[k].Indent) + lines[k].Text + "\n" + new string(' ', lines[k].Indent) + "^";
            return false;
        }

        static bool Block(List<L> lines, ref int k, int indent, YamlNode node, out string err, string text)
        {
            err = "";
            while (k < lines.Count)
            {
                var l = lines[k];
                if (l.Indent < indent) return true;
                if (l.Indent > indent) { err = l.Line + ":" + (l.Indent + 1) + ": Invalid YAML: inconsistent indentation:\n" + new string(' ', l.Indent) + l.Text + "\n" + new string(' ', l.Indent) + "^"; return false; }
                if (l.Text.StartsWith("- ") || l.Text == "-")
                {
                    string item = l.Text.Length > 2 ? l.Text.Substring(2).Trim() : "";
                    var child = new YamlNode { Line = l.Line, Col = l.Indent + 3 };
                    node.List.Add(child);
                    k++;
                    if (item.Length == 0)
                    {
                        if (k < lines.Count && lines[k].Indent > indent) { if (!Block(lines, ref k, lines[k].Indent, child, out err, text)) return false; }
                        continue;
                    }
                    int c = KeyColon(item);
                    if (c > 0)
                    {
                        // "- to: default" followed by more keys indented to match
                        var fake = new List<L> { new L { Indent = l.Indent + 2, Line = l.Line, Text = item } };
                        while (k < lines.Count && lines[k].Indent == l.Indent + 2 && !lines[k].Text.StartsWith("- ")) { fake.Add(lines[k]); k++; }
                        int kk = 0;
                        if (!Block(fake, ref kk, l.Indent + 2, child, out err, text)) return false;
                        continue;
                    }
                    child.Text = Unquote(item);
                    continue;
                }
                int colon = KeyColon(l.Text);
                if (colon <= 0) { err = l.Line + ":" + (l.Indent + 1) + ": Invalid YAML: could not find expected ':':\n" + new string(' ', l.Indent) + l.Text + "\n" + new string(' ', l.Indent + l.Text.Length) + "^"; return false; }
                string key = Unquote(l.Text.Substring(0, colon).Trim());
                string val = l.Text.Substring(colon + 1).Trim();
                var n = new YamlNode { Line = l.Line, Col = l.Indent + 1 };
                if (node.Map.ContainsKey(key)) { err = l.Line + ":" + (l.Indent + 1) + ": Invalid YAML: duplicate key '" + key + "'"; return false; }
                node.Map[key] = n;
                k++;
                if (val.Length == 0)
                {
                    if (k < lines.Count && lines[k].Indent > indent) { if (!Block(lines, ref k, lines[k].Indent, n, out err, text)) return false; }
                    else if (k < lines.Count && lines[k].Indent == indent && lines[k].Text.StartsWith("- ")) { if (!Block(lines, ref k, indent, n, out err, text)) return false; }
                }
                else if (val.StartsWith("["))
                {
                    if (!val.EndsWith("]")) { err = l.Line + ":" + (l.Indent + colon + 3) + ": Invalid YAML: did not find expected ',' or ']'"; return false; }
                    foreach (var part in val.Substring(1, val.Length - 2).Split(','))
                        if (part.Trim().Length > 0) n.List.Add(new YamlNode { Text = Unquote(part.Trim()), Line = l.Line, Col = l.Indent + colon + 3 });
                }
                else n.Text = Unquote(val);
            }
            return true;
        }

        static int KeyColon(string s)
        {
            bool q = false;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '"' || s[i] == '\'') q = !q;
                if (!q && s[i] == ':' && (i == s.Length - 1 || s[i + 1] == ' ')) return i;
            }
            return -1;
        }
        static string Unquote(string s) { s = s.Trim(); if (s.Length >= 2 && (s[0] == '"' || s[0] == '\'') && s[s.Length - 1] == s[0]) return s.Substring(1, s.Length - 2); return s; }

        /// <summary>The line with a ^ under the column (how netplan shows errors).</summary>
        public static string Show(string text, int line, int col)
        {
            var raw = text.Replace("\r", "").Split('\n');
            if (line < 1 || line > raw.Length) return "";
            return raw[line - 1] + "\n" + new string(' ', Math.Max(0, col - 1)) + "^";
        }
    }
}
