// Network Lab - the Command Prompt and PowerShell of the Windows-like PCs (and the simple desktop's prompt).
// Wording follows what learners see on real PCs, written for this simulator.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NetworkLab
{
    public class WinShell : Shell
    {
        public bool PowerShell;
        bool generic;

        public WinShell(Device d, bool powershell) : base(d)
        {
            PowerShell = powershell;
            generic = Personalities.Get(d.Os).Family == "generic";
            Title = powershell ? "Windows PowerShell" : "Command Prompt";
        }

        public override string Banner
        {
            get
            {
                if (generic) return "Network Lab command prompt (type help for the commands)\n";
                if (PowerShell) return "Windows PowerShell (simulated)\nThis PowerShell only knows network commands: type help to see them.\n";
                return "Microsoft Windows (simulated in Network Lab)\nType help to see the commands.\n";
            }
        }

        public override string Prompt
        {
            get
            {
                if (generic) return "C:\\>";
                return PowerShell ? "PS C:\\Users\\student> " : "C:\\Users\\student>";
            }
        }

        static readonly string[] CmdWords = { "ipconfig", "ping", "tracert", "nslookup", "arp", "route", "netsh", "hostname", "getmac", "whoami", "ver", "cls", "help", "exit", "net", "powershell", "echo" };
        static readonly string[] PsWords = { "Get-NetIPConfiguration", "Get-NetIPAddress", "Get-NetAdapter", "New-NetIPAddress", "Remove-NetIPAddress", "Set-NetIPInterface", "Set-DnsClientServerAddress", "Get-DnsClientServerAddress", "Test-NetConnection", "Test-Connection", "Resolve-DnsName", "Clear-DnsClientCache", "Get-NetFirewallProfile", "Set-NetFirewallProfile", "Get-NetRoute", "Restart-NetAdapter", "ipconfig", "ping", "tracert", "nslookup", "arp", "cls", "clear", "help", "exit", "hostname" };

        public override string Complete(string line)
        {
            var w = line.Split(' ');
            if (w.Length != 1 || w[0].Length == 0) return line;
            var all = PowerShell ? PsWords : CmdWords;
            var m = all.Where(x => x.StartsWith(w[0], StringComparison.OrdinalIgnoreCase)).ToList();
            return m.Count == 1 ? m[0] + " " : line;
        }

        static readonly Dictionary<string, string[]> PsParams = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "Get-NetIPConfiguration", new[] { "-InterfaceAlias", "-InterfaceIndex", "-All", "-Detailed" } },
            { "Get-NetIPAddress", new[] { "-InterfaceAlias", "-AddressFamily", "-IPAddress" } },
            { "Get-NetAdapter", new[] { "-Name", "-InterfaceIndex" } },
            { "New-NetIPAddress", new[] { "-InterfaceAlias", "-IPAddress", "-PrefixLength", "-DefaultGateway" } },
            { "Remove-NetIPAddress", new[] { "-InterfaceAlias", "-IPAddress", "-Confirm" } },
            { "Set-NetIPInterface", new[] { "-InterfaceAlias", "-Dhcp" } },
            { "Set-DnsClientServerAddress", new[] { "-InterfaceAlias", "-ServerAddresses", "-ResetServerAddresses" } },
            { "Get-DnsClientServerAddress", new[] { "-InterfaceAlias", "-AddressFamily" } },
            { "Test-NetConnection", new[] { "-ComputerName", "-Port", "-TraceRoute", "-InformationLevel" } },
            { "Test-Connection", new[] { "-ComputerName", "-Count" } },
            { "Resolve-DnsName", new[] { "-Name", "-Server", "-Type" } },
            { "Get-NetFirewallProfile", new[] { "-Name" } },
            { "Set-NetFirewallProfile", new[] { "-Profile", "-Enabled" } },
            { "Get-NetRoute", new[] { "-AddressFamily", "-InterfaceAlias" } },
            { "Restart-NetAdapter", new[] { "-Name" } },
        };
        static readonly Dictionary<string, string[]> CmdArgs = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "ipconfig", new[] { "/all", "/release", "/renew", "/flushdns", "/displaydns" } },
            { "netsh", new[] { "interface", "advfirewall", "wlan" } },
            { "arp", new[] { "-a", "-d" } },
            { "route", new[] { "print" } },
            { "net", new[] { "view" } },
        };

        /// <summary>Tab like Windows: each Tab shows the next match (Shift+Tab goes back): commands, PowerShell
        /// parameters, a few arguments and file paths.</summary>
        public override string TabStyle { get { return "cycle"; } }
        public override Completion Candidates(string line)
        {
            int st;
            string word = LastWord(line, out st);
            var r = new Completion { Start = st };
            var before = line.Substring(0, st).Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (before.Length == 0)
            {
                r.Words.AddRange((PowerShell ? PsWords : CmdWords).Where(x => x.StartsWith(word, StringComparison.OrdinalIgnoreCase)).OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
                return r;
            }
            string[] list;
            if (PowerShell && word.StartsWith("-") && PsParams.TryGetValue(before[0], out list)) r.Words.AddRange(list.Where(x => x.StartsWith(word, StringComparison.OrdinalIgnoreCase)));
            else if (!PowerShell && before.Length == 1 && CmdArgs.TryGetValue(before[0], out list)) r.Words.AddRange(list.Where(x => x.StartsWith(word, StringComparison.OrdinalIgnoreCase)));
            else if (word.Contains("\\") || word.Contains(":")) r.Words.AddRange(H.Files.Keys.Where(x => x.StartsWith(word, StringComparison.OrdinalIgnoreCase)));
            return r;
        }

        protected override void Run(string line)
        {
            var a = Split(line.Trim());
            if (a.Count == 0) return;
            string cmd = a[0].ToLowerInvariant();
            if (cmd.EndsWith(".exe")) cmd = cmd.Substring(0, cmd.Length - 4);
            switch (cmd)
            {
                case "ipconfig": Ipconfig(a); return;
                case "ping": Ping(a); return;
                case "tracert": Tracert(a); return;
                case "nslookup": Nslookup(a); return;
                case "arp": Arp(a); return;
                case "route": RoutePrint(); return;
                case "netsh": Netsh(a); return;
                case "hostname": Print(H.HostName); return;
                case "getmac": Getmac(); return;
                case "whoami": Print(H.HostName.ToLowerInvariant() + "\\student"); return;
                case "ver": Print("\nMicrosoft Windows [Version 10.0 - simulated by Network Lab]"); return;
                case "cls": case "clear": ClearRequested = true; Version++; return;
                case "exit": if (PowerShell && !generic && Title == "Command Prompt") { PowerShell = false; return; } Closed = true; Version++; return;
                case "echo": Print(line.Trim().Length > 5 ? line.Trim().Substring(5) : "ECHO is on."); return;
                case "help": Help(); return;
                case "net": Net(a); return;
                case "powershell": if (generic) break; PowerShell = true; Print("Windows PowerShell (simulated)\n"); return;
                case "cmd": if (PowerShell) { PowerShell = false; Print("Microsoft Windows (simulated)\n"); return; } return;
            }
            if (PowerShell && Ps(a, line)) return;
            if (PowerShell)
            {
                Print(a[0] + " : The term '" + a[0] + "' is not recognized as the name of a cmdlet, function, script file, or operable program.\nCheck the spelling of the name, or if a path was included, verify that the path is correct and try again.");
                return;
            }
            Print("'" + a[0] + "' is not recognized as an internal or external command,\noperable program or batch file.");
        }

        void Help()
        {
            if (PowerShell)
            {
                Print("Network commands in this PowerShell:\n  Get-NetIPConfiguration           addresses, gateway and DNS of each adapter\n  Get-NetIPAddress / Get-NetAdapter\n  New-NetIPAddress -InterfaceAlias Ethernet -IPAddress 192.168.1.10 -PrefixLength 24 -DefaultGateway 192.168.1.1\n  Remove-NetIPAddress -InterfaceAlias Ethernet\n  Set-NetIPInterface -InterfaceAlias Ethernet -Dhcp Enabled\n  Set-DnsClientServerAddress -InterfaceAlias Ethernet -ServerAddresses 192.168.1.53\n  Get-DnsClientServerAddress, Clear-DnsClientCache, Resolve-DnsName <name>\n  Test-Connection <address>, Test-NetConnection <address> [-Port 80] [-TraceRoute]\n  Get-NetFirewallProfile, Set-NetFirewallProfile -Enabled False\n  Get-NetRoute, Restart-NetAdapter -Name Ethernet\n  ipconfig, ping, tracert, nslookup, arp -a also work here. cmd goes back to the Command Prompt.");
                return;
            }
            Print("Commands:\n  ipconfig [/all | /release | /renew | /flushdns]   your addresses\n  ping [-n count] [-t] <address or name>           is it reachable?\n  tracert <address or name>                        the routers on the way\n  nslookup <name> [server]                         ask DNS for an address\n  arp -a                                           addresses this PC has met\n  route print                                      this PC's routing table" +
                (generic ? "" : "\n  netsh interface ip set address \"Ethernet\" static <ip> <mask> <gateway>  (or dhcp)\n  netsh interface ip set dns \"Ethernet\" static <ip>  (or dhcp)\n  netsh advfirewall set allprofiles state on|off\n  net view \\\\<computer>                            shared folders\n  hostname, getmac, whoami, ver, powershell") + "\n  cls, exit");
        }

        // ---------------------------------------------------------------- ipconfig
        string AdapterTitle(IpIf f)
        {
            string n = Pers.IfName(f.Port);
            if (generic) return (f.Port.IsWireless ? "Wireless0" : f.Port.Name) + " Connection:(default port)";
            return (f.Port.IsWireless ? "Wireless LAN adapter " : "Ethernet adapter ") + n + ":";
        }
        static string Dots(string label) { return "   " + label.PadRight(36, '.').Substring(0, 36).Replace("..", ". ") + ": "; }
        static string Row(string label, string v)
        {
            var sb = new StringBuilder("   " + label);
            while (sb.Length < 38) sb.Append(sb.Length % 2 == 0 ? ' ' : '.');
            return sb.ToString().TrimEnd() + " : " + v;
        }

        void Ipconfig(List<string> a)
        {
            string opt = a.Count > 1 ? a[1].ToLowerInvariant() : "";
            if (opt == "/release")
            {
                foreach (var f in H.Adapters.Where(x => x.Dhcp)) if (f.DhcpC != null) f.DhcpC.Release();
                if (!H.Adapters.Any(x => x.Dhcp)) { Print("The operation failed as no adapter is in the state permissible for\nthis operation."); return; }
                Ipconfig(new List<string> { "ipconfig" });
                return;
            }
            if (opt == "/renew")
            {
                var dh = H.Adapters.Where(x => x.Dhcp && x.Up).ToList();
                if (dh.Count == 0) { Print(H.Adapters.Any(x => x.Dhcp) ? "An error occurred while renewing interface " + Pers.IfName(H.Adapters.First(x => x.Dhcp).Port) + " : The media is disconnected." : "The operation failed as no adapter is in the state permissible for\nthis operation."); return; }
                foreach (var f in dh) H.EnsureDhcp(f);
                var sim = D.P.Sim;
                Hold("renew");
                long start = sim.Now;
                Action wait = null;
                wait = () =>
                {
                    bool done = dh.All(f => f.DhcpC.State == "bound" || f.DhcpC.State == "apipa");
                    if (!done && sim.Now - start < 12000) { sim.After(200, wait); return; }
                    Release();
                    if (dh.Any(f => f.DhcpC.State != "bound")) Print("An error occurred while renewing interface " + Pers.IfName(dh[0].Port) + " : unable to contact your DHCP server. Request has timed out.");
                    Ipconfig(new List<string> { "ipconfig" });
                };
                sim.After(200, wait);
                return;
            }
            if (opt == "/flushdns") { H.DnsCache.Clear(); Print("\nWindows IP Configuration\n\nSuccessfully flushed the DNS Resolver Cache."); return; }
            if (opt == "/displaydns")
            {
                var sb0 = new StringBuilder("\nWindows IP Configuration\n");
                foreach (var kv in H.DnsCache) sb0.Append("\n    " + kv.Key + "\n    ----------------------------------------\n    Record Name . . . . . : " + kv.Key + "\n    A (Host) Record . . . : " + Ip.Str(kv.Value) + "\n");
                Print(sb0.ToString());
                return;
            }
            bool all = opt == "/all";
            if (opt.Length > 0 && !all && opt != "/?") { Print("Error: unrecognized or incomplete command line.\n\nUSAGE:\n    ipconfig [/all | /release | /renew | /flushdns | /displaydns]"); return; }
            var sb = new StringBuilder("\n" + (generic ? "" : "Windows IP Configuration\n\n"));
            if (all && !generic)
            {
                sb.Append(Row("Host Name", H.HostName) + "\n" + Row("Primary Dns Suffix", "") + "\n" + Row("Node Type", "Hybrid") + "\n" + Row("IP Routing Enabled", "No") + "\n\n");
            }
            foreach (var f in H.Adapters)
            {
                sb.Append("\n" + AdapterTitle(f) + "\n\n");
                if (!f.Up)
                {
                    sb.Append(Row("Media State", "Media disconnected") + "\n" + Row("Connection-specific DNS Suffix", "") + "\n");
                    if (all) sb.Append(Row("Physical Address", Mac.Dashed(f.Mac)) + "\n" + Row("DHCP Enabled", f.Dhcp ? "Yes" : "No") + "\n");
                    continue;
                }
                sb.Append(Row("Connection-specific DNS Suffix", f.DhcpC != null && f.Dhcp ? f.DhcpC.Domain : "") + "\n");
                if (all)
                {
                    sb.Append(Row("Description", f.Port.IsWireless ? "Simulated Wi-Fi Adapter" : "Simulated Ethernet Adapter") + "\n");
                    sb.Append(Row("Physical Address", Mac.Dashed(f.Mac)) + "\n");
                    sb.Append(Row("DHCP Enabled", f.Dhcp ? "Yes" : "No") + "\n");
                    sb.Append(Row("Autoconfiguration Enabled", "Yes") + "\n");
                }
                if (f.Ip == 0)
                {
                    if (f.Dhcp) sb.Append(Row("IPv4 Address", "(waiting for DHCP...)") + "\n");
                    else sb.Append(Row("IPv4 Address", "0.0.0.0") + "\n" + Row("Subnet Mask", "0.0.0.0") + "\n");
                }
                else if (f.Apipa) sb.Append(Row("Autoconfiguration IPv4 Address", Ip.Str(f.Ip) + (all ? "(Preferred)" : "")) + "\n");
                else sb.Append(Row("IPv4 Address", Ip.Str(f.Ip) + (all ? "(Preferred)" : "")) + "\n");
                if (f.Ip != 0) sb.Append(Row("Subnet Mask", Ip.Str(f.Mask)) + "\n");
                sb.Append(Row("Default Gateway", f.Gateway != 0 ? Ip.Str(f.Gateway) : "") + "\n");
                if (all)
                {
                    if (f.Dhcp && f.DhcpC != null && f.DhcpC.State == "bound") sb.Append(Row("DHCP Server", Ip.Str(f.DhcpC.Server)) + "\n");
                    var dns = H.DnsServers;
                    sb.Append(Row("DNS Servers", dns.Count > 0 ? Ip.Str(dns[0]) : "") + "\n");
                    for (int i = 1; i < dns.Count; i++) sb.Append(new string(' ', 41) + Ip.Str(dns[i]) + "\n");
                }
            }
            Print(sb.ToString().TrimEnd('\n'));
        }

        // ---------------------------------------------------------------- ping and tracert
        void Ping(List<string> a)
        {
            int count = 4, ttl = 0, size = 32; bool forever = false; string target = null;
            for (int i = 1; i < a.Count; i++)
            {
                string o = a[i].ToLowerInvariant();
                if ((o == "-n" || o == "/n") && i + 1 < a.Count) { int.TryParse(a[++i], out count); }
                else if ((o == "-i" || o == "/i") && i + 1 < a.Count) { int.TryParse(a[++i], out ttl); }
                else if ((o == "-l" || o == "/l") && i + 1 < a.Count) { int.TryParse(a[++i], out size); }
                else if (o == "-t" || o == "/t") forever = true;
                else if (o == "-4" || o == "-a") { }
                else if (o.StartsWith("-")) { Print("Bad option " + a[i] + "."); return; }
                else target = a[i];
            }
            if (target == null) { Print("\nUsage: ping [-t] [-n count] [-i TTL] [-l size] target_name\n\nExamples: ping 192.168.1.1   ping -n 10 www.example.com   ping -t 10.0.0.1 (Ctrl+C stops it)"); return; }
            if (count < 1) count = 1;
            PingJob.Start(D, target, count, j =>
            {
                Hold(j);
                j.Forever = forever; j.Ttl = ttl; j.Size = size;
                j.TimeoutMs = 1000;
                bool head = false;
                j.OnResolveFail = err => Print(generic ? "Ping request could not find host " + target + ". Please check the name and try again." : "Ping request could not find host " + target + ". Please check the name and try again.");
                Action header = () =>
                {
                    if (head) return; head = true;
                    uint t0; bool isIp = Ip.TryParse(target, out t0);
                    Print("\nPinging " + (isIp ? Ip.Str(j.Target) : target + " [" + Ip.Str(j.Target) + "]") + " with " + size + " bytes of data:");
                };
                j.OnResult = r =>
                {
                    header();
                    switch (r.Kind)
                    {
                        case "reply": Print("Reply from " + Ip.Str(r.From) + ": bytes=" + r.Bytes + " " + Ms(r.Ms, true) + " TTL=" + r.Ttl); break;
                        case "timeout": Print("Request timed out."); break;
                        case "ttl": Print("Reply from " + Ip.Str(r.From) + ": TTL expired in transit."); break;
                        case "unreachable": Print("Reply from " + Ip.Str(r.From) + ": Destination host unreachable."); break;
                        case "prohibited": Print("Reply from " + Ip.Str(r.From) + ": Destination net unreachable."); break;
                        case "port": Print("Reply from " + Ip.Str(r.From) + ": Destination port unreachable."); break;
                        case "noroute":
                            {
                                var me = H.Main;
                                if (me != null && me.Ip != 0) Print("Reply from " + Ip.Str(me.Ip) + ": Destination host unreachable.");
                                else Print("PING: transmit failed. General failure.");
                                break;
                            }
                        default: Print("PING: transmit failed. General failure."); break;
                    }
                };
                j.OnDone = jj =>
                {
                    Release();
                    if (jj.Target == 0) return;
                    header();
                    int lost = jj.Sent - jj.Received;
                    Print("\nPing statistics for " + Ip.Str(jj.Target) + ":\n    Packets: Sent = " + jj.Sent + ", Received = " + jj.Received + ", Lost = " + lost + " (" + (jj.Sent == 0 ? 0 : lost * 100 / jj.Sent) + "% loss)," +
                        (jj.Received > 0 ? "\nApproximate round trip times in milli-seconds:\n    Minimum = " + jj.MinMs + "ms, Maximum = " + jj.MaxMs + "ms, Average = " + (jj.SumMs / jj.Received) + "ms" : ""));
                    if (jj.Stopped) Print("Control-C\n^C");
                };
            });
        }

        void Tracert(List<string> a)
        {
            string target = null; int max = 30;
            for (int i = 1; i < a.Count; i++)
            {
                string o = a[i].ToLowerInvariant();
                if (o == "-d") { }
                else if (o == "-h" && i + 1 < a.Count) int.TryParse(a[++i], out max);
                else target = a[i];
            }
            if (target == null) { Print("\nUsage: tracert [-d] [-h maximum_hops] target_name"); return; }
            TraceJob.Start(D, target, false, j =>
            {
                Hold(j);
                j.MaxHops = max;
                j.TimeoutMs = 1000;
                j.OnResolveFail = err => Print("Unable to resolve target system name " + target + ".");
                bool head = false;
                j.OnHop = h =>
                {
                    if (!head)
                    {
                        head = true;
                        uint t0;
                        Print("\nTracing route to " + (Ip.TryParse(target, out t0) ? Ip.Str(j.Target) : target + " [" + Ip.Str(j.Target) + "]") + "\nover a maximum of " + j.MaxHops + " hops:\n");
                    }
                    var sb = new StringBuilder(h.Ttl.ToString().PadLeft(3));
                    for (int i = 0; i < 3; i++) sb.Append(h.Ms[i] < 0 ? "     *   " : (h.Ms[i] < 1 ? "    <1 ms" : (h.Ms[i] + " ms").PadLeft(9)));
                    sb.Append("  " + (h.From != 0 ? Ip.Str(h.From) + (h.Unreachable ? " reports: Destination host unreachable." : "") : "Request timed out."));
                    Print(sb.ToString());
                };
                j.OnDone = jj => { Release(); if (jj.Target != 0) Print("\nTrace complete."); };
            });
        }

        // ---------------------------------------------------------------- names
        void Nslookup(List<string> a)
        {
            var servers = H.DnsServers;
            string name = a.Count > 1 ? a[1] : null;
            if (a.Count > 2)
            {
                uint s;
                if (!Ip.TryParse(a[2], out s)) { Print("*** Can't find address for server " + a[2] + ": Non-existent domain"); return; }
                servers = new List<uint> { s };
            }
            if (servers.Count == 0)
            {
                Print("*** Default servers are not available\nServer:  UnKnown\nAddress:  127.0.0.1\n");
                if (name != null) Print("*** UnKnown can't find " + name + ": No response from server");
                return;
            }
            Print("Server:  UnKnown\nAddress:  " + Ip.Str(servers[0]) + "\n");
            if (name == null) return;
            uint dummy;
            if (Ip.TryParse(name, out dummy)) { Print("*** UnKnown can't find " + name + ": Non-existent domain"); return; }
            Hold("nslookup");
            // ask exactly these servers, without the cache (like the real nslookup)
            var saved = new Dictionary<string, uint>(H.DnsCache);
            H.DnsCache.Clear();
            AskServers(name, servers, 0, (ip, err) =>
            {
                foreach (var kv in saved) if (!H.DnsCache.ContainsKey(kv.Key)) H.DnsCache[kv.Key] = kv.Value;
                Release();
                if (ip != 0) Print((PowerShell ? "" : "Non-authoritative answer:\n") + "Name:    " + name + "\nAddress:  " + Ip.Str(ip) + "\n");
                else if (err == "timeout") Print("DNS request timed out.\n    timeout was 2 seconds.\n*** Request to UnKnown timed-out");
                else Print("*** UnKnown can't find " + name + ": Non-existent domain");
            });
        }

        void AskServers(string name, List<uint> servers, int i, Action<uint, string> cb)
        {
            var copy = D.Host;
            // resolve through the normal resolver, but with only the chosen servers
            var fake = new List<uint> { servers[i] };
            var s = D.Ip; var sim = D.P.Sim;
            int port = s.Ephemeral(); int id = D.P.Rng.Next(1, 65535);
            bool done = false; SimTimer t = null;
            s.Udp[port] = (ifc, p) =>
            {
                var r = p.Seg.App as DnsMsg;
                if (r == null || r.Id != id || done) return;
                done = true; s.Udp.Remove(port); if (t != null) t.Cancel();
                cb(r.Answer, r.Answer != 0 ? "" : "nxdomain");
            };
            string err = s.Send(new IpPkt { Dst = servers[i], Proto = "UDP", Seg = new Seg { SrcPort = port, DstPort = 53, App = new DnsMsg { Id = id, QName = name } } }, null);
            if (err.Length > 0) { s.Udp.Remove(port); cb(0, "timeout"); return; }
            t = sim.After(2000, () => { if (done) return; done = true; s.Udp.Remove(port); if (i + 1 < servers.Count) AskServers(name, servers, i + 1, cb); else cb(0, "timeout"); });
        }

        void Arp(List<string> a)
        {
            string o = a.Count > 1 ? a[1].ToLowerInvariant() : "";
            if (o == "-d") { D.Ip.ClearArp(); if (!generic) Print(""); return; }
            if (o != "-a" && o != "-g")
            {
                Print("\nDisplays and modifies the IP-to-Physical address translation tables.\n\nARP -a        shows the table\nARP -d *      empties it");
                return;
            }
            var entries = D.Ip.ArpEntries();
            if (entries.Count == 0 && generic) { Print("No ARP Entries Found"); return; }
            var sb = new StringBuilder();
            foreach (var f in H.Adapters.Where(x => x.Ip != 0 && x.Up))
            {
                sb.Append("\nInterface: " + Ip.Str(f.Ip) + " --- 0x" + (f.Port.Index + 3).ToString("x") + "\n  Internet Address      Physical Address      Type\n");
                foreach (var e in entries.Where(x => x.If == f)) sb.Append("  " + Ip.Str(e.Ip).PadRight(22) + Mac.Dashed(e.Mac).ToLowerInvariant().PadRight(22) + "dynamic\n");
                if (!generic) sb.Append("  " + Ip.Str(Ip.BroadcastOf(f.Ip, f.Mask)).PadRight(22) + "ff-ff-ff-ff-ff-ff".PadRight(22) + "static\n");
            }
            if (sb.Length == 0) { Print("No ARP Entries Found."); return; }
            Print(sb.ToString().TrimEnd('\n'));
        }

        void RoutePrint()
        {
            var sb = new StringBuilder("===========================================================================\nIPv4 Route Table\n===========================================================================\nActive Routes:\nNetwork Destination        Netmask          Gateway       Interface  Metric\n");
            foreach (var r in D.Ip.Routes())
            {
                string gw = r.NextHop != 0 ? Ip.Str(r.NextHop) : "On-link";
                sb.Append(Ip.Str(r.Net).PadLeft(18) + Ip.Str(r.Mask).PadLeft(17) + gw.PadLeft(17) + Ip.Str(r.If.Ip).PadLeft(17) + (r.IsDefault ? "  25" : "  281") + "\n");
            }
            sb.Append("===========================================================================");
            Print(sb.ToString());
        }

        void Getmac()
        {
            var sb = new StringBuilder("\nPhysical Address    Transport Name\n=================== ==========================================================\n");
            foreach (var f in H.Adapters) sb.Append(Mac.Dashed(f.Mac).PadRight(20) + (f.Up ? "\\Device\\Tcpip_{" + f.Port.Index + "}" : "Media disconnected") + "\n");
            Print(sb.ToString());
        }

        void Net(List<string> a)
        {
            if (a.Count >= 3 && a[1].ToLowerInvariant() == "view" && a[2].StartsWith("\\\\"))
            {
                string host = a[2].Substring(2).TrimEnd('\\');
                Hold("net");
                Request.Send(D, host, 445, new TextMsg { Proto = "SMB", Line = "SHARES" }, (m, err, ip) =>
                {
                    Release();
                    if (m == null)
                    {
                        if (err.StartsWith("name")) Print("System error 53 has occurred.\n\nThe network path was not found.");
                        else Print("System error 53 has occurred.\n\nThe network path was not found.\n(Is file sharing turned on there, and does the firewall allow it?)");
                        return;
                    }
                    var sb = new StringBuilder("Shared resources at \\\\" + host + "\n\n\n\nShare name  Type  Used as  Comment\n\n-------------------------------------------------------------------------------\n");
                    foreach (var s in m.Body.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)) sb.Append(s.PadRight(12) + "Disk\n");
                    sb.Append("The command completed successfully.");
                    Print(sb.ToString());
                });
                return;
            }
            Print("The syntax of this command is:\n\nNET VIEW \\\\computername");
        }

        // ---------------------------------------------------------------- netsh
        void Netsh(List<string> a)
        {
            var w = a.Skip(1).Select(x => x.ToLowerInvariant()).ToList();
            if (w.Count >= 3 && w[0] == "advfirewall" && w[1] == "set" && w.Contains("state"))
            {
                int i = w.IndexOf("state");
                if (i + 1 < w.Count) { H.WinFirewall = w[i + 1] == "on"; Print("Ok.\n"); return; }
            }
            if (w.Count >= 3 && w[0] == "advfirewall" && w[1] == "firewall" && w[2] == "add")
            {
                string all = string.Join(" ", w.ToArray());
                if (all.Contains("icmpv4") && all.Contains("allow")) { H.FwSharing = true; Print("Ok.\n"); return; }
                if (all.Contains("localport=80") && all.Contains("allow")) { H.FwWeb = true; Print("Ok.\n"); return; }
                if (all.Contains("localport=445") && all.Contains("allow")) { H.FwSharing = true; Print("Ok.\n"); return; }
                Print("Ok.\n");
                return;
            }
            if (w.Count >= 2 && w[0] == "advfirewall" && w[1] == "show") { Print("\nAll Profiles:\n----------------------------------------------------------------------\nState                                 " + (H.WinFirewall ? "ON" : "OFF") + "\nFile and printer sharing allowed      " + (H.FwSharing ? "Yes" : "No") + "\nOk.\n"); return; }
            if (w.Count >= 4 && w[0] == "interface" && (w[1] == "ip" || w[1] == "ipv4") && (w[2] == "set" || w[2] == "add"))
            {
                // the adapter name is the next word, or name="..."
                var rest = a.Skip(5).ToList();
                string name = rest.Count > 0 ? rest[0] : "";
                if (name.StartsWith("name=", StringComparison.OrdinalIgnoreCase)) name = name.Substring(5);
                var f = Pers.AdapterByName(D, name);
                if (f == null) { Print("The filename, directory name, or volume label syntax is incorrect.\n(There is no adapter called \"" + name + "\": try \"Ethernet\" or \"Wi-Fi\".)\n"); return; }
                var args = rest.Skip(1).Select(x => { int eq = x.IndexOf('='); return eq > 0 ? x.Substring(eq + 1) : x; }).ToList();
                if (w[3] == "address")
                {
                    if (args.Count >= 1 && args[0].ToLowerInvariant() == "dhcp") { H.SetDhcp(f); Print(""); return; }
                    if (args.Count >= 1 && args[0].ToLowerInvariant() == "static") args.RemoveAt(0);
                    if (args.Count < 2) { Print("The syntax supplied for this command is not valid. Check help for the correct syntax.\n(netsh interface ip set address \"Ethernet\" static 192.168.1.10 255.255.255.0 192.168.1.1)\n"); return; }
                    var keep = f.Dns.Select(x => Ip.Str(x)).ToList();
                    string err = H.SetStatic(f, args[0], args[1], args.Count > 2 ? args[2] : "", keep.Count > 0 ? keep[0] : "", keep.Count > 1 ? keep[1] : "");
                    Print(err.Length > 0 ? "The parameter is incorrect.\n(" + err + ")\n" : "");
                    return;
                }
                if (w[3] == "dns" || w[3] == "dnsservers" || w[3] == "dnsserver")
                {
                    if (args.Count >= 1 && args[0].ToLowerInvariant() == "dhcp") { f.Dns.Clear(); if (f.Dhcp && f.DhcpC != null) f.Dns.AddRange(f.DhcpC.Dns); Print(""); return; }
                    if (args.Count >= 1 && args[0].ToLowerInvariant() == "static") args.RemoveAt(0);
                    uint ip;
                    if (args.Count < 1 || !Ip.TryParse(args[0], out ip)) { Print("The syntax supplied for this command is not valid. Check help for the correct syntax.\n"); return; }
                    if (w[2] == "set") f.Dns.Clear();
                    if (!f.Dns.Contains(ip)) f.Dns.Add(ip);
                    H.DnsCache.Clear();
                    Print("");
                    return;
                }
            }
            if (w.Count >= 4 && w[0] == "interface" && (w[1] == "ip" || w[1] == "ipv4") && w[2] == "show" && (w[3] == "config" || w[3] == "address" || w[3] == "addresses"))
            {
                var sb = new StringBuilder();
                foreach (var f in H.Adapters)
                {
                    sb.Append("\nConfiguration for interface \"" + Pers.IfName(f.Port) + "\"\n");
                    sb.Append("    DHCP enabled:                         " + (f.Dhcp ? "Yes" : "No") + "\n");
                    if (f.Ip != 0) { sb.Append("    IP Address:                           " + Ip.Str(f.Ip) + "\n    Subnet Prefix:                        " + Ip.Cidr(f.Ip & f.Mask, f.Mask) + " (mask " + Ip.Str(f.Mask) + ")\n"); }
                    if (f.Gateway != 0) sb.Append("    Default Gateway:                      " + Ip.Str(f.Gateway) + "\n");
                    sb.Append("    DNS servers:                          " + (f.Dns.Count > 0 ? string.Join(", ", f.Dns.Select(x => Ip.Str(x)).ToArray()) : "None") + "\n");
                }
                Print(sb.ToString());
                return;
            }
            if (w.Count >= 3 && w[0] == "wlan" && w[1] == "show" && w[2] == "networks")
            {
                var sb = new StringBuilder("\nInterface name : Wi-Fi\n");
                int n = 0;
                foreach (var ap in D.P.Devices.Where(x => x.Ap != null && x.Ap.Enabled && x.PoweredOn))
                {
                    if (Physical.Signal(D.P, ap, D) <= 0) continue;
                    n++;
                    sb.Append("\nSSID " + n + " : " + ap.Ap.Ssid + "\n    Network type            : Infrastructure\n    Authentication          : " + (ap.Ap.Security == "open" ? "Open" : "WPA2-Personal") + "\n");
                }
                sb.Insert(0, "\nThere are " + n + " networks currently visible.");
                Print(sb.ToString());
                return;
            }
            Print("The following command was not found: " + string.Join(" ", a.Skip(1).ToArray()) + ".\n(Try: netsh interface ip set address \"Ethernet\" static <ip> <mask> <gateway>, netsh interface ip set dns \"Ethernet\" static <ip>, netsh advfirewall set allprofiles state off)");
        }

        // ---------------------------------------------------------------- PowerShell
        static string Opt(List<string> a, string name)
        {
            for (int i = 1; i < a.Count; i++)
                if (string.Equals(a[i], "-" + name, StringComparison.OrdinalIgnoreCase) && i + 1 < a.Count) return a[i + 1].Trim('\'', '"');
            return null;
        }
        static bool Flag(List<string> a, string name) { return a.Any(x => string.Equals(x, "-" + name, StringComparison.OrdinalIgnoreCase)); }

        IpIf Alias(List<string> a, string opt)
        {
            string n = Opt(a, opt) ?? Opt(a, "InterfaceAlias") ?? Opt(a, "Name");
            if (n == null) { var e = H.Adapters.FirstOrDefault(x => !x.Port.IsWireless) ?? H.Adapters.FirstOrDefault(); return e; }
            return Pers.AdapterByName(D, n);
        }

        void PsError(string cmd, string msg) { Print(cmd + " : " + msg + "\n    + CategoryInfo          : InvalidArgument\n    + FullyQualifiedErrorId : " + cmd.Replace("-", "")); }

        bool Ps(List<string> a, string line)
        {
            string c = a[0].ToLowerInvariant();
            switch (c)
            {
                case "get-netipconfiguration":
                    {
                        var sb = new StringBuilder();
                        foreach (var f in H.Adapters)
                        {
                            sb.Append("\nInterfaceAlias       : " + Pers.IfName(f.Port) + "\nInterfaceIndex       : " + (f.Port.Index + 3) + "\nInterfaceDescription : " + (f.Port.IsWireless ? "Simulated Wi-Fi Adapter" : "Simulated Ethernet Adapter") + "\n");
                            if (!f.Up) { sb.Append("NetAdapter.Status    : Disconnected\n"); continue; }
                            sb.Append("IPv4Address          : " + (f.Ip != 0 ? Ip.Str(f.Ip) : "") + "\nIPv4DefaultGateway   : " + (f.Gateway != 0 ? Ip.Str(f.Gateway) : "") + "\nDNSServer            : " + string.Join("\n                       ", f.Dns.Select(x => Ip.Str(x)).ToArray()) + "\n");
                        }
                        Print(sb.ToString());
                        return true;
                    }
                case "get-netipaddress":
                    {
                        var sb = new StringBuilder();
                        foreach (var f in H.Adapters.Where(x => x.Ip != 0))
                            sb.Append("\nIPAddress         : " + Ip.Str(f.Ip) + "\nInterfaceAlias    : " + Pers.IfName(f.Port) + "\nAddressFamily     : IPv4\nPrefixLength      : " + Ip.PrefixOf(f.Mask) + "\nPrefixOrigin      : " + (f.Dhcp ? "Dhcp" : "Manual") + "\nAddressState      : Preferred\n");
                        Print(sb.ToString());
                        return true;
                    }
                case "get-netadapter":
                    {
                        var sb = new StringBuilder("\nName                      InterfaceDescription                    ifIndex Status       MacAddress             LinkSpeed\n----                      --------------------                    ------- ------       ----------             ---------\n");
                        foreach (var f in H.Adapters) sb.Append(Pers.IfName(f.Port).PadRight(26) + (f.Port.IsWireless ? "Simulated Wi-Fi Adapter" : "Simulated Ethernet Adapter").PadRight(40) + (f.Port.Index + 3).ToString().PadLeft(7) + " " + (f.Up ? "Up" : "Disconnected").PadRight(13) + Mac.Dashed(f.Mac).PadRight(23) + (f.Up ? f.Port.Speed + " Mbps" : "0 bps") + "\n");
                        Print(sb.ToString());
                        return true;
                    }
                case "new-netipaddress":
                    {
                        var f = Alias(a, "InterfaceAlias");
                        if (f == null) { PsError("New-NetIPAddress", "No MSFT_NetIPInterface objects found with property 'InterfaceAlias' equal to '" + Opt(a, "InterfaceAlias") + "'."); return true; }
                        string ip = Opt(a, "IPAddress"), pre = Opt(a, "PrefixLength"), gw = Opt(a, "DefaultGateway");
                        if (ip == null || pre == null) { PsError("New-NetIPAddress", "Give -IPAddress and -PrefixLength (for example 24)."); return true; }
                        if (f.Ip != 0 && !f.Dhcp) { PsError("New-NetIPAddress", "The object already exists. (Remove-NetIPAddress first, or this adapter keeps " + Ip.Str(f.Ip) + ".)"); return true; }
                        var keep = f.Dns.Select(x => Ip.Str(x)).ToList();
                        string err = H.SetStatic(f, ip, pre, gw ?? "", keep.Count > 0 ? keep[0] : "", keep.Count > 1 ? keep[1] : "");
                        if (err.Length > 0) { PsError("New-NetIPAddress", err); return true; }
                        Print("\nIPAddress         : " + ip + "\nInterfaceAlias    : " + Pers.IfName(f.Port) + "\nAddressFamily     : IPv4\nPrefixLength      : " + Ip.PrefixOf(f.Mask) + "\nPrefixOrigin      : Manual\n");
                        return true;
                    }
                case "remove-netipaddress":
                    {
                        var f = Alias(a, "InterfaceAlias");
                        if (f == null || f.Ip == 0) { PsError("Remove-NetIPAddress", "No matching MSFT_NetIPAddress objects found."); return true; }
                        D.Ip.SetAddress(f, 0, 0); f.Gateway = 0;
                        return true;
                    }
                case "set-netipinterface":
                    {
                        var f = Alias(a, "InterfaceAlias");
                        if (f == null) { PsError("Set-NetIPInterface", "No matching MSFT_NetIPInterface objects found."); return true; }
                        string dh = Opt(a, "Dhcp");
                        if (dh != null && dh.ToLowerInvariant() == "enabled") H.SetDhcp(f);
                        else if (dh != null) { f.Dhcp = false; if (f.DhcpC != null) f.DhcpC.Stop(); }
                        return true;
                    }
                case "set-dnsclientserveraddress":
                    {
                        var f = Alias(a, "InterfaceAlias");
                        if (f == null) { PsError("Set-DnsClientServerAddress", "No matching MSFT_DNSClientServerAddress objects found."); return true; }
                        if (Flag(a, "ResetServerAddresses")) { f.Dns.Clear(); if (f.Dhcp && f.DhcpC != null) f.Dns.AddRange(f.DhcpC.Dns); H.DnsCache.Clear(); return true; }
                        int i = a.FindIndex(x => string.Equals(x, "-ServerAddresses", StringComparison.OrdinalIgnoreCase));
                        if (i < 0 || i + 1 >= a.Count) { PsError("Set-DnsClientServerAddress", "Give -ServerAddresses (for example 192.168.1.53)."); return true; }
                        var list = new List<uint>();
                        foreach (var part in string.Join(",", a.Skip(i + 1).TakeWhile(x => !x.StartsWith("-")).ToArray()).Split(','))
                        {
                            string t = part.Trim().Trim('"', '\'', '(', ')');
                            if (t.Length == 0) continue;
                            uint ip;
                            if (!Ip.TryParse(t, out ip)) { PsError("Set-DnsClientServerAddress", "'" + t + "' isn't a valid IP address."); return true; }
                            list.Add(ip);
                        }
                        f.Dns.Clear(); f.Dns.AddRange(list);
                        H.DnsCache.Clear();
                        return true;
                    }
                case "get-dnsclientserveraddress":
                    {
                        var sb = new StringBuilder("\nInterfaceAlias               Interface Address ServerAddresses\n                             Index     Family\n--------------               --------- ------- ---------------\n");
                        foreach (var f in H.Adapters) sb.Append(Pers.IfName(f.Port).PadRight(29) + (f.Port.Index + 3).ToString().PadRight(10) + "IPv4    {" + string.Join(", ", f.Dns.Select(x => Ip.Str(x)).ToArray()) + "}\n");
                        Print(sb.ToString());
                        return true;
                    }
                case "clear-dnsclientcache": H.DnsCache.Clear(); return true;
                case "get-netfirewallprofile":
                    {
                        var sb = new StringBuilder();
                        foreach (var n in new[] { "Domain", "Private", "Public" }) sb.Append("\nName    : " + n + "\nEnabled : " + (H.WinFirewall ? "True" : "False") + "\n");
                        Print(sb.ToString());
                        return true;
                    }
                case "set-netfirewallprofile":
                    {
                        string e = Opt(a, "Enabled");
                        if (e == null) { PsError("Set-NetFirewallProfile", "Give -Enabled True or False."); return true; }
                        H.WinFirewall = e.Trim('$').ToLowerInvariant() == "true";
                        return true;
                    }
                case "get-netroute":
                    {
                        var sb = new StringBuilder("\nifIndex DestinationPrefix  NextHop         RouteMetric\n------- -----------------  -------         -----------\n");
                        foreach (var r in D.Ip.Routes()) sb.Append((r.If.Port != null ? (r.If.Port.Index + 3).ToString() : "1").PadLeft(7) + " " + Ip.Cidr(r.Net, r.Mask).PadRight(19) + (r.NextHop != 0 ? Ip.Str(r.NextHop) : "0.0.0.0").PadRight(16) + (r.IsDefault ? "0" : "256") + "\n");
                        Print(sb.ToString());
                        return true;
                    }
                case "restart-netadapter":
                    {
                        var f = Alias(a, "Name");
                        if (f == null) { PsError("Restart-NetAdapter", "No MSFT_NetAdapter objects found."); return true; }
                        if (f.Dhcp) H.EnsureDhcp(f);
                        H.DnsCache.Clear();
                        return true;
                    }
                case "resolve-dnsname":
                    {
                        string name = a.Count > 1 && !a[1].StartsWith("-") ? a[1] : Opt(a, "Name");
                        if (name == null) { PsError("Resolve-DnsName", "Give a name, for example Resolve-DnsName www.example.com"); return true; }
                        string server = Opt(a, "Server");
                        var servers = H.DnsServers;
                        uint sv;
                        if (server != null && Ip.TryParse(server, out sv)) servers = new List<uint> { sv };
                        Hold("resolve");
                        Action<uint, string> done = (ip, err) =>
                        {
                            Release();
                            if (ip != 0) Print("\nName                                           Type   TTL   Section    IPAddress\n----                                           ----   ---   -------    ---------\n" + name.PadRight(47) + "A      300   Answer     " + Ip.Str(ip) + "\n");
                            else Print("Resolve-DnsName : " + name + " : " + (err == "timeout" || err == "no-dns" ? "This operation returned because the timeout period expired" : "DNS name does not exist") + "\n    + CategoryInfo          : ResourceUnavailable: (" + name + ":String) [Resolve-DnsName], Win32Exception\n    + FullyQualifiedErrorId : " + (err == "timeout" ? "ERROR_TIMEOUT" : "DNS_ERROR_RCODE_NAME_ERROR") + ",Microsoft.DnsClient.Commands.ResolveDnsName");
                        };
                        if (servers.Count == 0) { done(0, "no-dns"); return true; }
                        AskServers(name, servers, 0, done);
                        return true;
                    }
                case "test-connection":
                    {
                        string target = a.Count > 1 && !a[1].StartsWith("-") ? a[1] : Opt(a, "ComputerName") ?? Opt(a, "TargetName");
                        if (target == null) { PsError("Test-Connection", "Give an address or name."); return true; }
                        int count = 4; string cs = Opt(a, "Count"); if (cs != null) int.TryParse(cs, out count);
                        PingJob.Start(D, target, count, j =>
                        {
                            Hold(j);
                            bool head = false;
                            j.OnResolveFail = err => Print("Test-Connection : Testing connection to computer '" + target + "' failed: No such host is known");
                            j.OnResult = r =>
                            {
                                if (!head) { head = true; Print("\nSource        Destination     IPV4Address      Bytes    Time(ms)\n------        -----------     -----------      -----    --------"); }
                                if (r.Kind == "reply") Print(H.HostName.PadRight(14) + target.PadRight(16) + Ip.Str(j.Target).PadRight(17) + "32".PadRight(9) + r.Ms);
                                else Print("Test-Connection : Testing connection to computer '" + target + "' failed: " + (r.Kind == "timeout" ? "Error due to lack of resources" : "Destination host unreachable"));
                            };
                            j.OnDone = jj => Release();
                        });
                        return true;
                    }
                case "test-netconnection": case "tnc":
                    {
                        string target = a.Count > 1 && !a[1].StartsWith("-") ? a[1] : Opt(a, "ComputerName") ?? "internetbeacon.msedge.net";
                        string ps = Opt(a, "Port");
                        bool trace = Flag(a, "TraceRoute");
                        int port = 0; if (ps != null) int.TryParse(ps, out port);
                        TestNet(target, port, trace);
                        return true;
                    }
            }
            return false;
        }

        void TestNet(string target, int port, bool trace)
        {
            Hold("tnc");
            var f = H.Main;
            Resolver.Resolve(D, target, (ip, err, srv) =>
            {
                if (ip == 0) { Release(); Print("WARNING: Name resolution of " + target + " failed\n\nComputerName   : " + target + "\nRemoteAddress  :\nInterfaceAlias :\nSourceAddress  :\nPingSucceeded  : False\n"); return; }
                Action<bool, long> after = (pingOk, ms) =>
                {
                    Action<bool> tcpDone = tcpOk =>
                    {
                        Release();
                        var sb = new StringBuilder();
                        if (port != 0 && !tcpOk) sb.Append("WARNING: TCP connect to (" + Ip.Str(ip) + " : " + port + ") failed\n");
                        if (!pingOk) sb.Append("WARNING: Ping to " + Ip.Str(ip) + " failed with status: TimedOut\n");
                        sb.Append("\nComputerName     : " + target + "\nRemoteAddress    : " + Ip.Str(ip) + "\n");
                        if (port != 0) sb.Append("RemotePort       : " + port + "\n");
                        sb.Append("InterfaceAlias   : " + (f != null ? Pers.IfName(f.Port) : "") + "\nSourceAddress    : " + (f != null && f.Ip != 0 ? Ip.Str(f.Ip) : "") + "\n");
                        if (port == 0) { sb.Append("PingSucceeded    : " + (pingOk ? "True" : "False") + "\n"); if (pingOk) sb.Append("PingReplyDetails (RTT) : " + ms + " ms\n"); }
                        else sb.Append("TcpTestSucceeded : " + (tcpOk ? "True" : "False") + "\n");
                        Print(sb.ToString());
                    };
                    if (port == 0) { tcpDone(false); return; }
                    TcpConn.Open(D.Ip, ip, port, c => { c.Close(); tcpDone(true); }, null, (c, why) => tcpDone(false));
                };
                if (trace)
                {
                    var hops = new List<string>();
                    TraceJob.Start(D, Ip.Str(ip), false, j =>
                    {
                        j.MaxHops = 15;
                        j.OnHop = h => hops.Add(h.From != 0 ? Ip.Str(h.From) : "0.0.0.0");
                        j.OnDone = jj => { Release(); Print("\nComputerName   : " + target + "\nRemoteAddress  : " + Ip.Str(ip) + "\nPingSucceeded  : " + (hops.Count > 0 && hops[hops.Count - 1] == Ip.Str(ip) ? "True" : "False") + "\nTraceRoute     : " + string.Join("\n                 ", hops.ToArray()) + "\n"); };
                    });
                    return;
                }
                var job = PingJob.Start(D, Ip.Str(ip), 1, j => { j.TimeoutMs = 1000; long ms = 0; bool ok = false; j.OnResult = r => { ok = r.Kind == "reply"; ms = r.Ms; }; j.OnDone = jj => after(ok, ms); });
            });
        }
    }
}
