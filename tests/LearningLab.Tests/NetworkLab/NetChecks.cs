// Network Lab - the engine checks App Hub ran with NetworkLab.ps1 -Test (no window, no real network), as group
// "network" of Learning Lab's checks. The checks of the screens (drawings, terminals, the IP settings window) come with
// the screens.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace NetworkLab
{
    public static partial class NetChecks
    {
        static List<string> output;
        static int passed, failed;

        static void Ok(bool cond, string name, string detail)
        {
            if (cond) passed++; else failed++;
            LearningLab.Tests.Program.Check("network: " + name, cond, detail);
        }
        static void Ok(bool cond, string name) { Ok(cond, name, null); }

        static Project Build(string script)
        {
            var p = new Project(7);
            var errs = Script.Run(p, script);
            if (errs.Count > 0) throw new Exception(string.Join("; ", errs.ToArray()));
            p.Sim.RunFor(500);
            return p;
        }

        static string Cli(Project p, string dev, string commands)
        {
            var d = p.Find(dev);
            var s = new CliSession(d) { Started = true };
            var sb = new StringBuilder();
            foreach (var c in commands.Split('\n'))
            {
                s.Execute(c);
                for (int g = 0; g < 3 && s.Pending != null; g++) s.Execute("");
                p.Sim.RunUntil(p.Sim.Now + 30000, () => s.Busy == null);
                sb.Append(s.TakeOutput());
            }
            d.Ios.Sessions.Remove(s);
            return sb.ToString();
        }

        static string Sh(Project p, string dev, string cmd) { Script.Sh(p, p.Find(dev), cmd); return Script.Last; }

        public static void Run()
        {
            output = new List<string>();
            passed = 0; failed = 0;
            Brand.LoadBuiltIn(null);
            var sections = new List<KeyValuePair<string, Action>>
            {
                new KeyValuePair<string, Action>("switching", Switching),
                new KeyValuePair<string, Action>("vlans", Vlans),
                new KeyValuePair<string, Action>("routing", Routing),
                new KeyValuePair<string, Action>("services", Services),
                new KeyValuePair<string, Action>("nat and acl", NatAcl),
                new KeyValuePair<string, Action>("wifi", Wifi),
                new KeyValuePair<string, Action>("spanning tree", Stp),
                new KeyValuePair<string, Action>("cli", CliParser),
                new KeyValuePair<string, Action>("save and load", SaveLoad),
                new KeyValuePair<string, Action>("simulation mode", Determinism),
                new KeyValuePair<string, Action>("windows-like", WindowsLike),
                new KeyValuePair<string, Action>("linux-like", LinuxLike),
                new KeyValuePair<string, Action>("macos, phones, printers, gadgets", OtherPersonalities),
                new KeyValuePair<string, Action>("physical", PhysicalView),
                new KeyValuePair<string, Action>("labs", LabsSolvable),
                new KeyValuePair<string, Action>("drawings (the model: undo, order, align, groups, copy and paste)", DrawingModel),
                new KeyValuePair<string, Action>("brand", BrandCheck),
                new KeyValuePair<string, Action>("the screens", LearningLab.Tests.NetPages.Run),
            };
            foreach (var s in sections)
            {
                try { s.Value(); }
                catch (Exception ex) { Ok(false, s.Key + " ran without an error", ex.ToString()); }
            }
        }

        // ------------------------------------------------------------ layer 2
        static void Switching()
        {
            var p = Build("add switch SW1 0 0\nadd pc PC1 0 0\nadd pc PC2 0 0\nadd pc PC3 0 0\ncable PC1 Fa0 SW1 Fa0/1\ncable PC2 Fa0 SW1 Fa0/2\ncable PC3 Fa0 SW1 Fa0/3\nip PC1 192.168.1.1/24\nip PC2 192.168.1.2/24\nip PC3 192.168.1.3/24");
            var pc3 = p.Find("PC3").Port("Fa0");
            p.Sim.RunFor(2000);
            long before = pc3.RxFrames;
            Ok(Check.CanPing(p, "PC1", "PC2"), "PC1 pings PC2 through a switch");
            long afterFirst = pc3.RxFrames;
            Ok(afterFirst > before, "the first ARP broadcast is flooded to PC3 too", before + " -> " + afterFirst);
            var sw = p.Find("SW1");
            var tbl = sw.Bridge.Entries();
            Ok(tbl.Any(e => e.Mac == p.Find("PC1").Port("Fa0").Mac && e.Port.Short == "Fa0/1") && tbl.Any(e => e.Mac == p.Find("PC2").Port("Fa0").Mac && e.Port.Short == "Fa0/2"), "the switch learned PC1 on Fa0/1 and PC2 on Fa0/2");
            Check.CanPing(p, "PC1", "PC2");
            Ok(pc3.RxFrames == afterFirst, "once learned, pings between PC1 and PC2 don't reach PC3", afterFirst + " -> " + pc3.RxFrames);
            var arp = p.Find("PC1").Ip.ArpEntries();
            Ok(arp.Any(a => a.Ip == Ip.Parse("192.168.1.2") && a.Mac == p.Find("PC2").Port("Fa0").Mac), "PC1's ARP table has PC2's MAC");
            string mt = Cli(p, "SW1", "show mac address-table");
            Ok(mt.Contains("DYNAMIC") && mt.Contains("Fa0/1"), "show mac address-table lists the learned addresses", mt);
            // wrong cable and shut ports
            var q = Build("add pc A 0 0\nadd pc B 0 0\ncable A Fa0 B Fa0 straight");
            Ok(q.Links[0].Status == "fault" && q.Links[0].Problem.Contains("crossover"), "PC to PC with a straight cable is a wrong-cable fault", q.Links[0].Problem);
            var r = Build("add router R 0 0\nadd switch S 0 0\ncable R Gi0/0 S Fa0/1");
            Ok(r.Links[0].Status == "down" && r.Links[0].Problem.Contains("no shutdown"), "a router port starts shut down (red light)", r.Links[0].Problem);
        }

        static void Vlans()
        {
            string s = "add switch SW1 0 0\nadd switch SW2 0 0\nadd pc A1 0 0\nadd pc A2 0 0\nadd pc B1 0 0\n" +
                       "cable SW1 Fa0/24 SW2 Fa0/24 cross\ncable A1 Fa0 SW1 Fa0/1\ncable B1 Fa0 SW1 Fa0/2\ncable A2 Fa0 SW2 Fa0/1\n" +
                       "ip A1 10.0.0.1/24\nip A2 10.0.0.2/24\nip B1 10.0.0.3/24\n" +
                       "SW1: enable\nSW1: configure terminal\nSW1: vlan 10\nSW1: vlan 20\nSW1: interface fa0/1\nSW1: switchport access vlan 10\nSW1: interface fa0/2\nSW1: switchport access vlan 20\nSW1: end\n" +
                       "SW2: enable\nSW2: configure terminal\nSW2: vlan 10\nSW2: interface fa0/1\nSW2: switchport access vlan 10\nSW2: end";
            var p = Build(s);
            Ok(!Check.CanPing(p, "A1", "A2"), "VLAN 10 doesn't cross an access link in VLAN 1");
            Ok(!Check.CanPing(p, "A1", "B1"), "VLAN 10 and VLAN 20 are isolated on one switch, even in the same subnet");
            Cli(p, "SW1", "enable\nconfigure terminal\ninterface fa0/24\nswitchport mode trunk\nend");
            Cli(p, "SW2", "enable\nconfigure terminal\ninterface fa0/24\nswitchport mode trunk\nend");
            Ok(Check.CanPing(p, "A1", "A2"), "VLAN 10 crosses the 802.1Q trunk");
            Ok(!Check.CanPing(p, "A1", "B1"), "still isolated from VLAN 20");
            string vb = Cli(p, "SW1", "show vlan brief");
            Ok(vb.Contains("10   VLAN0010") && vb.Contains("Fa0/1"), "show vlan brief", vb);
            string tr = Cli(p, "SW1", "show interfaces trunk");
            Ok(tr.Contains("802.1q") && tr.Contains("trunking"), "show interfaces trunk", tr);
            Cli(p, "SW1", "enable\nconfigure terminal\ninterface fa0/24\nswitchport trunk allowed vlan remove 10\nend");
            Ok(!Check.CanPing(p, "A1", "A2"), "removing VLAN 10 from the trunk's allowed list stops it");
        }

        // ------------------------------------------------------------ layer 3
        static void Routing()
        {
            var lab = Labs.Get("two-subnets");
            var p = lab.Build();
            Ok(!Check.CanPing(p, "PC1", "PC3"), "no ping across a router that isn't set up");
            Script.Run(p, "R1: enable\nR1: configure terminal\nR1: interface g0/0\nR1: ip address 192.168.1.1 255.255.255.0\nR1: no shutdown\nR1: interface g0/1\nR1: ip address 192.168.2.1 255.255.255.0\nR1: no shutdown\nR1: end");
            Ok(Check.CanPing(p, "PC1", "192.168.1.1"), "PC1 pings its router port");
            Ok(!Check.CanPing(p, "PC1", "PC3"), "without default gateways, no ping across the router");
            Script.Run(p, "ip PC1 192.168.1.10/24 192.168.1.1\nip PC3 192.168.2.10/24 192.168.2.1");
            Ok(Check.CanPing(p, "PC1", "PC3"), "with default gateways, PC1 pings PC3 across the router");
            string winOut = Sh(p, "PC1", "ping 192.168.2.10");
            Ok(winOut.Contains("Reply from 192.168.2.10") && winOut.Contains("TTL=127"), "the reply's TTL went down by one at the router", winOut);
            // three routers: static routes, traceroute hops, show ip route
            var st = Labs.Get("static-routes").Build();
            Script.Run(st, Labs.Get("static-routes").Answer);
            TraceHop last = null; int hops = 0; bool done = false;
            TraceJob.Start(st.Find("PC1"), "192.168.3.10", false, j => { j.OnHop = h => { hops++; last = h; }; j.OnDone = jj => done = true; });
            st.Sim.RunUntil(st.Sim.Now + 60000, () => done);
            Ok(hops == 4 && last != null && last.Reached, "tracert from PC1 to PC2 shows 3 routers and then PC2", "hops " + hops);
            string rt = Cli(st, "R1", "show ip route");
            Ok(rt.Contains("S       192.168.3.0/24 [1/0] via 10.0.12.2") || rt.Contains("S    192.168.3.0/24 [1/0] via 10.0.12.2"), "show ip route shows the static route", rt);
            Ok(rt.Contains("C ") && rt.Contains("L "), "show ip route shows connected and local routes");
            string tro = Cli(st, "R1", "traceroute 192.168.3.10");
            Ok(tro.Contains("10.0.12.2") && tro.Contains("10.0.23.2") && tro.Contains("192.168.3.10"), "router traceroute lists the hops", tro);
            string pg = Cli(st, "R1", "ping 192.168.3.10");
            Ok(pg.Contains("Success rate is") && (pg.Contains("!!!!!") || pg.Contains(".!!!!")), "router ping prints ! for replies", pg);
            // TTL runs out: ping with TTL 2 from PC1 to PC2
            string ttl = Sh(st, "PC1", "ping -i 2 192.168.3.10");
            Ok(ttl.Contains("TTL expired in transit"), "a ping with too small a TTL gets 'TTL expired in transit'", ttl);
            // RIP converges
            var rip = Labs.Get("rip").Build();
            Script.Run(rip, Labs.Get("rip").Answer);
            rip.Sim.RunFor(5000);
            Ok(Labs.Get("rip").Goals.Take(3).All(g => g.Test(rip)), "RIP: every router learned the far networks within seconds");
            string rr = Cli(rip, "R1", "show ip route");
            Ok(rr.Contains("R    192.168.3.0/24 [120/2] via 10.0.12.2") && rr.Contains("R       10.0.23.0/30 [120/1] via 10.0.12.2"), "RIP hop counts: one router away = 1, two = 2", rr);
            string proto = Cli(rip, "R2", "show ip protocols");
            Ok(proto.Contains("Routing Protocol is \"rip\"") && proto.Contains("10.0.0.0"), "show ip protocols", proto);
            // link down: RIP routes go
            Script.Run(rip, "R3: enable\nR3: configure terminal\nR3: interface g0/1\nR3: shutdown\nR3: end");
            rip.Sim.RunFor(5000);
            Ok(!Check.HasRoute(rip, "R1", "192.168.3.0/24") || rip.Find("R1").Ip.Routes().First(r => r.Net == Ip.Parse("192.168.3.0")).Metric >= 16, "RIP: a network that goes down is withdrawn");
        }

        static void Services()
        {
            var lab = Labs.Get("dhcp-dns");
            var p = lab.Build();
            p.Sim.RunFor(12000);
            Ok(Check.Address(p, "PC1").StartsWith("169.254."), "with no DHCP server answering, a PC gives itself a 169.254 address", Check.Address(p, "PC1"));
            Script.Run(p, lab.Answer);
            p.Sim.RunFor(15000);
            uint a;
            Ok(Ip.TryParse(Check.Address(p, "PC1"), out a) && a >= Ip.Parse("192.168.2.100"), "DHCP through a relay (ip helper-address) gives a lease", Check.Address(p, "PC1"));
            Ok(p.Find("Server1").Ip.Dhcp.Leases.Count == 2, "the server lists two leases");
            Ok(Check.Resolve(p, "PC1", "www.lab.local") == "192.168.1.10", "DNS lookup through the DHCP-given DNS server");
            Ok(Check.Resolve(p, "PC1", "nothing.lab.local") == "", "a name that isn't there fails");
            var m = Check.Web(p, "PC2", "http://www.lab.local/");
            Ok(m != null && m.Line.StartsWith("200") && m.Body.Contains("Server1"), "HTTP fetch of the server's page by name");
            var nf = Check.Web(p, "PC2", "http://www.lab.local/missing.html");
            Ok(nf != null && nf.Line.StartsWith("404"), "a missing page is 404");
            string ns = Sh(p, "PC1", "nslookup www.lab.local");
            Ok(ns.Contains("Address:  192.168.1.10") && ns.Contains("Name:    www.lab.local"), "nslookup output", ns);
            string ipc = Sh(p, "PC1", "ipconfig /all");
            Ok(ipc.Contains("192.168.2.1") && ipc.Contains("DHCP"), "ipconfig /all shows the DHCP lease", ipc);
            // router as DHCP server, show ip dhcp binding
            var q = Labs.Get("office-printer").Build();
            q.Sim.RunFor(8000);
            Ok(Check.Address(q, "PC1") == "192.168.1.100", "a router DHCP pool skips excluded addresses", Check.Address(q, "PC1"));
            string bind = Cli(q, "R1", "show ip dhcp binding");
            Ok(bind.Contains("192.168.1.100"), "show ip dhcp binding", bind);
        }

        static void NatAcl()
        {
            var lab = Labs.Get("nat");
            var p = lab.Build();
            Ok(!Check.CanPing(p, "PC1", "198.51.100.20"), "without NAT, private addresses don't come back from the internet");
            Script.Run(p, lab.Answer);
            Ok(Check.CanPing(p, "PC1", "198.51.100.20"), "with PAT, PC1 reaches the internet");
            var t = p.Find("R1").Ip.Nat.Entries();
            Ok(t.Any(e => e.InLocal == Ip.Parse("192.168.1.10") && e.InGlobal == Ip.Parse("203.0.113.2")), "the translation table maps 192.168.1.10 to 203.0.113.2");
            Ok(Check.WebOk(p, "PC2", "http://www.example.com/"), "web through NAT, by name");
            string tr = Cli(p, "R1", "show ip nat translations");
            Ok(tr.Contains("203.0.113.2:") && tr.Contains("192.168.1.1"), "show ip nat translations", tr);
            bool done = false; int hops = 0; bool reached = false;
            TraceJob.Start(p.Find("PC1"), "198.51.100.20", false, j => { j.OnHop = h => { hops++; reached = h.Reached; }; j.OnDone = jj => done = true; });
            p.Sim.RunUntil(p.Sim.Now + 60000, () => done);
            Ok(reached && hops == 2, "tracert through NAT reaches the site (time-exceeded messages translated back)", "hops " + hops);
            // a router that gets its internet address from the provider by DHCP
            var dq = Build("add cloud I 0 0\nadd router R 0 0\ncable R Gi0/1 I Eth1\nR: enable\nR: configure terminal\nR: interface g0/1\nR: ip address dhcp\nR: no shutdown\nR: end");
            dq.Sim.RunFor(5000);
            var wanIf = dq.Find("R").Port("Gi0/1").L3;
            Ok(wanIf.Ip != 0 && (wanIf.Ip & 0xFFFFFFE0) == Ip.Parse("203.0.113.32") && dq.Find("R").Ip.Routes().Any(r => r.IsDefault && r.Ad == 254), "ip address dhcp on a router: an address from the provider and a default route", Ip.Str(wanIf.Ip));
            Ok(Check.CanPing(dq, "R", "198.51.100.20"), "...so the router itself reaches the internet");
            // ACL
            var al = Labs.Get("acl");
            var q = al.Build();
            Ok(Check.WebOk(q, "PC1", "http://10.0.0.10/"), "before the ACL PC1 opens the web server");
            Script.Run(q, al.Answer);
            Ok(!Check.WebOk(q, "PC1", "http://10.0.0.10/"), "ACL denies PC1's web traffic");
            Ok(Check.WebOk(q, "PC2", "http://10.0.0.10/"), "ACL permits PC2");
            Ok(Check.CanPing(q, "PC1", "Web1"), "ACL still permits PC1's ping");
            string acl = Cli(q, "R1", "show access-lists");
            Ok(acl.Contains("Extended IP access list 101") && acl.Contains("deny tcp host 192.168.1.10 host 10.0.0.10 eq www") && acl.Contains("match"), "show access-lists with hit counts", acl);
            string bad = Cli(q, "R1", "enable\nconfigure terminal\naccess-list 102 permit tcp any hots 1.2.3.4");
            Ok(bad.Contains("^") && bad.Contains("Invalid input"), "a typo in an access list is marked with ^", bad);
            // standard ACL
            Script.Run(q, "R1: enable\nR1: configure terminal\nR1: no access-list 101\nR1: access-list 5 deny host 192.168.1.11\nR1: access-list 5 permit any\nR1: interface g0/1\nR1: ip access-group 5 out\nR1: interface g0/0\nR1: no ip access-group 101 in\nR1: end");
            Ok(!Check.CanPing(q, "PC2", "Web1") && Check.CanPing(q, "PC1", "Web1"), "standard ACL (out) blocks one host");
        }

        static void Wifi()
        {
            var lab = Labs.Get("home");
            var p = lab.Build();
            Script.Run(p, "cable HomeRouter0 Internet Internet0 Eth0\nwifi Laptop1 HomeNet wrongpass");
            Ok(p.Find("Laptop1").Host.Wifi[0].Status == "wrong-key", "a wrong Wi-Fi key doesn't connect");
            Ok(!Check.CanPing(p, "Laptop1", "192.168.0.1"), "...and the laptop can't reach the router");
            Script.Run(p, "wifi Laptop1 HomeNet letmein123");
            p.Sim.RunFor(3000);
            Ok(p.Find("Laptop1").Host.Wifi[0].Status == "connected", "the right key connects");
            Ok(Check.Address(p, "Laptop1").StartsWith("192.168.0."), "and DHCP from the wireless router works over Wi-Fi", Check.Address(p, "Laptop1"));
            Ok(Check.WebOk(p, "Laptop1", "http://www.example.com/"), "the laptop reaches the internet through the home router's NAT");
            Script.Run(p, "wifi Phone1 OtherNet x");
            Ok(p.Find("Phone1").Host.Wifi[0].Status == "not-found", "an unknown network name isn't found");
            // an access point bridging to a wired switch
            var q = Build("add switch SW 0 0\nadd ap AP1 0 0\nadd pc PC 0 0\nadd laptop L 0 0\ncable AP1 Port0 SW Fa0/1\ncable PC Fa0 SW Fa0/2\nap AP1 ssid=Lab security=wpa2 key=secret99\nip PC 10.1.1.1/24\nwifi L Lab secret99\nip L 10.1.1.2/24 - - Wi-Fi");
            Ok(Check.CanPing(q, "L", "PC"), "a Wi-Fi laptop pings a wired PC through an access point");
        }

        static void Stp()
        {
            var p = Build("add switch S1 0 0\nadd switch S2 0 0\nadd switch S3 0 0\nadd pc A 0 0\nadd pc B 0 0\n" +
                          "cable S1 Fa0/23 S2 Fa0/23 cross\ncable S2 Fa0/24 S3 Fa0/24 cross\ncable S3 Fa0/22 S1 Fa0/22 cross\ncable A Fa0 S1 Fa0/1\ncable B Fa0 S3 Fa0/1\nip A 10.9.0.1/24\nip B 10.9.0.2/24");
            int blocked = p.Devices.Sum(d => d.Ports.Count(x => x.StpBlocked.Contains(1)));
            Ok(blocked == 1, "spanning tree blocks exactly one port in a three-switch loop", "blocked " + blocked);
            long ev0 = p.Sim.EventsRun;
            Ok(Check.CanPing(p, "A", "B"), "ping works over the loop-free tree");
            long used = p.Sim.EventsRun - ev0;
            Ok(used < 300, "no broadcast storm (" + used + " events for the ping)");
            string stp = "";
            foreach (var n in new[] { "S1", "S2", "S3" }) stp += Cli(p, n, "show spanning-tree");
            Ok(stp.Contains("Altn BLK") && stp.Contains("This bridge is the root") && stp.Contains("Root FWD"), "show spanning-tree shows the root, root ports and the blocked port", stp);
            // break a link: the blocked port takes over
            var root = p.Devices.Where(d => d.Bridge != null).OrderBy(d => d.Bridge.BridgeId(1), StringComparer.Ordinal).First();
            var link = p.Links.First(l => l.A.Dev.Kind == "switch" && l.B.Dev.Kind == "switch" && !l.A.StpBlocked.Contains(1) && !l.B.StpBlocked.Contains(1));
            p.Disconnect(link);
            Ok(p.Devices.Sum(d => d.Ports.Count(x => x.StpBlocked.Contains(1))) == 0, "with one link gone, nothing is blocked any more");
            Ok(Check.CanPing(p, "A", "B"), "and the ping still works over the other way");
            // priority makes a root
            var q = Build("add switch S1 0 0\nadd switch S2 0 0\ncable S1 Fa0/1 S2 Fa0/1 cross\ncable S1 Fa0/2 S2 Fa0/2 cross\nS2: enable\nS2: configure terminal\nS2: spanning-tree vlan 1 priority 4096\nS2: end");
            Ok(q.Find("S1").Bridge.Stp[1].RootId == q.Find("S2").Bridge.BridgeId(1), "spanning-tree vlan 1 priority 4096 makes S2 the root");
            Ok(q.Find("S1").Ports.Count(x => x.StpBlocked.Contains(1)) == 1 && q.Find("S2").Ports.Count(x => x.StpBlocked.Contains(1)) == 0, "the root's ports all forward; the other switch blocks one of two parallel links");
        }

        // ------------------------------------------------------------ the command line
        static void CliParser()
        {
            var p = Build("add router R1 0 0\nadd switch SW1 0 0\nadd pc PC1 0 0\ncable R1 Gi0/0 SW1 Fa0/1\ncable PC1 Fa0 SW1 Fa0/2\nip PC1 192.168.1.10/24 192.168.1.1");
            var d = p.Find("R1");
            var s = new CliSession(d) { Started = true };
            Func<string, string> X = c => { s.Execute(c); for (int g = 0; g < 3 && s.Pending != null; g++) s.Execute(""); p.Sim.RunUntil(p.Sim.Now + 30000, () => s.Busy == null); return s.TakeOutput(); };
            Ok(s.Prompt == "Router>", "starts in user mode with Router>", s.Prompt);
            string o = X("conf t");
            Ok(o.Contains("Invalid input") || o.Contains("Unknown"), "configure isn't available in user mode", o);
            X("en");
            Ok(s.Prompt == "Router#", "en = enable", s.Prompt);
            o = X("c");
            Ok(o.Contains("Ambiguous command"), "c is ambiguous (clear, configure, copy)", o);
            X("conf t");
            Ok(s.Prompt == "Router(config)#", "conf t", s.Prompt);
            X("host R1");
            Ok(s.Prompt == "R1(config)#", "host R1 renames the prompt", s.Prompt);
            X("int g0/0");
            Ok(s.Prompt == "R1(config-if)#", "int g0/0", s.Prompt);
            X("ip add 192.168.1.1 255.255.255.0");
            X("no shut");
            Ok(d.Port("Gi0/0").L3.Up && d.Port("Gi0/0").L3.Ip == Ip.Parse("192.168.1.1"), "ip add / no shut abbreviations work");
            o = X("ip adress 1.1.1.1 255.0.0.0");
            Ok(o.Contains("^") && o.Contains("% Invalid input detected at '^' marker."), "a typo gets the ^ marker", o);
            int caretCol = o.Split('\n')[0].IndexOf('^');
            Ok(caretCol == "R1(config-if)#".Length + 3, "the ^ is under the bad word", "column " + caretCol);
            o = X("ip address");
            Ok(o.Contains("% Incomplete command."), "incomplete command", o);
            o = X("?");
            Ok(o.Contains("ip") && o.Contains("shutdown") && o.Contains("description"), "? lists the interface commands", o);
            o = X("ip ?");
            Ok(o.Contains("address") && o.Contains("access-group"), "ip ? lists what can follow", o);
            o = X("sh?");
            Ok(o.Contains("shutdown"), "sh? lists the words starting with sh", o);
            Ok(s.Complete("descr") == "description ", "Tab completes a word", s.Complete("descr"));
            Ok(s.Complete("ip add") == "ip address ", "Tab completes the second word");
            o = X("do show ip interface brief");
            Ok(o.Contains("GigabitEthernet0/0") && o.Contains("192.168.1.1") && o.Contains("up"), "do show ip interface brief from interface mode", o);
            X("interface g0/1");
            Ok(s.If != null && s.If.Name == "GigabitEthernet0/1", "a global command (interface) works from interface mode");
            X("exit");
            Ok(s.Prompt == "R1(config)#", "exit goes back one mode");
            X("router rip");
            Ok(s.Prompt == "R1(config-router)#", "router rip");
            X("ver 2");
            X("net 192.168.1.0");
            X("end");
            Ok(s.Prompt == "R1#" && d.Ip.Rip != null && d.Ip.Rip.Version == 2 && d.Ip.Rip.Networks.Contains(Ip.Parse("192.168.1.0")), "RIP configured with abbreviations");
            o = X("show run");
            Ok(o.Contains("hostname R1") && o.Contains("interface GigabitEthernet0/0") && o.Contains(" ip address 192.168.1.1 255.255.255.0") && o.Contains("router rip") && o.Contains(" version 2"), "show running-config", o);
            o = X("sh ip int br");
            Ok(o.Contains("GigabitEthernet0/1") && o.Contains("administratively down"), "sh ip int br", o);
            o = X("foo");
            Ok(o.Contains("Translating \"foo\"...domain server (255.255.255.255)"), "an unknown word is treated as a host name (domain lookup)", o);
            X("conf t"); X("no ip domain-lookup"); X("end");
            o = X("foo");
            Ok(!o.Contains("Translating") && o.Contains("Unknown command"), "no ip domain-lookup stops the lookup", o);
            o = X("ping 192.168.1.10");
            Ok(o.Contains("Success rate is") && o.Contains("!"), "ping from the router", o);
            o = X("show arp");
            Ok(o.Contains("192.168.1.10") && o.Contains("ARPA"), "show arp", o);
            o = X("show version");
            Ok(o.Contains(Brand.Name) && o.Contains(Brand.Os), "show version names the brand and its OS", o);
            o = X("show cdp neighbors");
            Ok(o.Contains("Switch") && o.Contains(Brand.ModelOf("switch")), "show cdp neighbors", o);
            // access-list help
            X("conf t");
            o = X("access-list 101 permit ?");
            Ok(o.Contains("tcp") && o.Contains("icmp"), "? inside an access list line", o);
            o = X("access-list 101 permit tcp any ?");
            Ok(o.Contains("host") && o.Contains("A.B.C.D"), "? asks for the destination", o);
            X("end");
            // passwords
            X("conf t"); X("enable secret class"); X("line con 0"); X("password cisco"); X("login"); X("exit"); X("service password-encryption"); X("end");
            o = X("show run");
            Ok(o.Contains("enable secret 5 $1$") && o.Contains(" password 7 ") && !o.Contains("password cisco"), "secrets are hashed and passwords encrypted in show run", o);
            X("disable");
            s.Execute("enable");
            Ok(s.Pending != null && s.PendingSecret, "enable asks for the password");
            s.Execute("wrong");
            s.Execute("class");
            Ok(s.Prompt == "R1#", "the enable secret works", s.Prompt);
            X("copy run start");
            Ok(d.Ios.Startup != null && d.Ios.Startup.Contains("hostname R1"), "copy running-config startup-config");
            X("conf t"); X("hostname Changed"); X("end");
            s.Execute("reload"); s.Execute("");
            Ok(d.Ios.Hostname == "R1", "reload restores the saved configuration", d.Ios.Hostname);
            // switch specifics
            var sw = p.Find("SW1");
            var t = new CliSession(sw) { Started = true };
            Func<string, string> Y = c => { t.Execute(c); for (int g = 0; g < 3 && t.Pending != null; g++) t.Execute(""); return t.TakeOutput(); };
            Y("en"); Y("conf t");
            o = Y("int range fa0/3 - 6");
            Ok(t.Prompt == "Switch(config-if-range)#", "interface range", t.Prompt);
            o = Y("sw acc vlan 30");
            Ok(o.Contains("% Access VLAN does not exist. Creating vlan 30") && sw.Port("Fa0/5").AccessVlan == 30, "switchport access vlan creates a missing VLAN", o);
            Y("exit"); Y("vlan 30"); Y("name Guests"); Y("end");
            o = Y("show vlan brief");
            Ok(o.Contains("30   Guests") && o.Contains("Fa0/3, Fa0/4, Fa0/5, Fa0/6"), "show vlan brief after a range", o);
            o = Y("conf t\nint g0/0");
            Y("conf t");
            o = Y("int g0/0");
            Ok(o.Contains("Invalid interface"), "a switch has no g0/0", o);
            o = Y("interface vlan 1");
            Y("ip address 192.168.1.2 255.255.255.0"); Y("no shut"); Y("exit"); Y("ip default-gateway 192.168.1.1"); Y("end");
            Ok(Check.CanPing(p, "PC1", "192.168.1.2"), "a switch answers pings on interface vlan 1");
            o = Y("show interfaces fa0/2");
            Ok(o.Contains("FastEthernet0/2 is up, line protocol is up"), "show interfaces fa0/2", o);
        }

        // ------------------------------------------------------------ files
        static void SaveLoad()
        {
            var lab = Labs.Get("vlans");
            var p = lab.Build();
            Script.Run(p, lab.Answer);
            Script.Run(p, "note 100 100 A note\nos PC2 win10");
            string a = Persist.Save(p);
            var q = Persist.Load(a);
            string b = Persist.Save(q);
            Ok(a == b, "save -> open -> save gives the same file", Diff(a, b));
            q.Sim.RunFor(3000);
            Ok(Check.CanPing(q, "PC1", "PC4"), "the opened network works (inter-VLAN ping)");
            Ok(q.Notes.Count == 1 && q.Find("PC2").Os == "win10", "notes and personalities survive");
            Ok(!a.Contains(Brand.Name), "a saved project doesn't contain the brand name");
            // every lab's answer round-trips
            foreach (var l in Labs.All)
            {
                var x = l.Build();
                Script.Run(x, l.Answer);
                string s1 = Persist.Save(x);
                string s2 = Persist.Save(Persist.Load(s1));
                if (s1 != s2) { Ok(false, "round trip of lab " + l.Id, Diff(s1, s2)); }
            }
            Ok(true, "every lab's solved network round-trips");
        }

        static string Diff(string a, string b)
        {
            if (a == b) return "";
            var la = a.Split('\n'); var lb = b.Split('\n');
            for (int i = 0; i < Math.Min(la.Length, lb.Length); i++) if (la[i] != lb[i]) return "line " + (i + 1) + ": '" + la[i].Trim() + "' vs '" + lb[i].Trim() + "'";
            return "lengths " + la.Length + " vs " + lb.Length;
        }

        static void Determinism()
        {
            var lab = Labs.Get("two-subnets");
            var p0 = lab.Build();
            Script.Run(p0, lab.Answer);
            string json = Persist.Save(p0);
            Func<List<string>> run = () =>
            {
                var p = Persist.Load(json);
                p.Sim.RunFor(2000);
                p.Sim.Capture = true;
                p.Sim.Log.Clear();
                PingJob.Start(p.Find("PC1"), "192.168.2.10", 2, null);
                var steps = new List<string>();
                for (int i = 0; i < 200 && p.Sim.Pending > 0; i++)
                {
                    var evs = p.Sim.StepSlot(e => e.Type == "ICMP" || e.Type == "ARP", 5000);
                    if (evs.Count == 0) break;
                    foreach (var e in evs) steps.Add(e.TimeText + " " + e.Last + ">" + e.At + " " + e.Type + " " + e.Info + " | " + e.Decision);
                }
                return steps;
            };
            var r1 = run(); var r2 = run();
            Ok(r1.Count > 6 && r1.SequenceEqual(r2), "Simulation mode gives the same event list every time (" + r1.Count + " events)");
            Ok(r1.Any(x => x.Contains("ARP") && x.Contains("PC1")), "the event list includes the ARP for the gateway");
            Ok(r1.Any(x => x.Contains("learned")), "switch decisions are explained ('learned ... on Fa0/x')");
            Ok(r1.Any(x => x.Contains("looks up 192.168.2.10")), "router decisions are explained (routing table lookup)");
            var any = Persist.Load(json);
            any.Sim.RunFor(2000);
            any.Sim.Capture = true;
            PingJob.Start(any.Find("PC1"), "192.168.2.10", 1, null);
            var ev = any.Sim.StepSlot(e => e.Type == "ICMP", 5000);
            Ok(ev.Count > 0 && ev[0].Frame != null && ev[0].Frame.Layers().Count >= 3, "an envelope's layers can be opened (Ethernet, IP, ICMP)");
        }

        // ------------------------------------------------------------ personalities
        static void WindowsLike()
        {
            var p = Build("add switch SW 0 0\nadd pc W 0 0 win11\nadd pc G 0 0\nadd server S 0 0\ncable W Fa0 SW Fa0/1\ncable G Fa0 SW Fa0/2\ncable S Fa0 SW Fa0/3\nip G 10.5.0.2/24\nip S 10.5.0.9/24\nweb S on\ndns S on\ndns S files.lan 10.5.0.9\nsmb S Public readme.txt");
            p.Sim.RunFor(12000);
            string o = Sh(p, "W", "ipconfig");
            Ok(o.Contains("Windows IP Configuration") && o.Contains("Ethernet adapter Ethernet:") && o.Contains("Autoconfiguration IPv4 Address"), "Windows-like ipconfig with no DHCP server shows an autoconfiguration (169.254) address", o);
            o = Sh(p, "W", "netsh interface ip set address \"Ethernet\" static 10.5.0.3 255.255.255.0 10.5.0.1");
            Ok(Check.Address(p, "W") == "10.5.0.3", "netsh interface ip set address", o);
            o = Sh(p, "W", "netsh interface ip set dns \"Ethernet\" static 10.5.0.9");
            Ok(p.Find("W").Host.DnsServers.Contains(Ip.Parse("10.5.0.9")), "netsh interface ip set dns");
            Ok(!Check.CanPing(p, "G", "W"), "the Windows firewall blocks incoming pings");
            Ok(Check.CanPing(p, "W", "G"), "...but the Windows PC can ping out");
            Sh(p, "W", "netsh advfirewall firewall add rule name=\"Ping\" protocol=icmpv4:8,any dir=in action=allow");
            Ok(Check.CanPing(p, "G", "W"), "a firewall rule for ICMPv4 lets pings in");
            Sh(p, "W", "ps Set-NetFirewallProfile -Profile Domain,Public,Private -Enabled False");
            Ok(!p.Find("W").Host.WinFirewall, "Set-NetFirewallProfile -Enabled False");
            o = Sh(p, "W", "ps Get-NetIPConfiguration");
            Ok(o.Contains("IPv4Address          : 10.5.0.3") && o.Contains("DNSServer            : 10.5.0.9"), "Get-NetIPConfiguration", o);
            Sh(p, "W", "ps Remove-NetIPAddress -InterfaceAlias Ethernet");
            o = Sh(p, "W", "ps New-NetIPAddress -InterfaceAlias \"Ethernet\" -IPAddress 10.5.0.4 -PrefixLength 24 -DefaultGateway 10.5.0.1");
            Ok(Check.Address(p, "W") == "10.5.0.4" && p.Find("W").Host.Gateway == Ip.Parse("10.5.0.1"), "New-NetIPAddress", o);
            o = Sh(p, "W", "ps New-NetIPAddress -InterfaceAlias Wrong -IPAddress 10.5.0.5 -PrefixLength 24");
            Ok(o.Contains("No MSFT_NetIPInterface objects found"), "New-NetIPAddress on a missing adapter fails like the real one", o);
            Sh(p, "W", "ps Set-DnsClientServerAddress -InterfaceAlias Ethernet -ServerAddresses 10.5.0.9");
            o = Sh(p, "W", "ps Resolve-DnsName files.lan");
            Ok(o.Contains("files.lan") && o.Contains("10.5.0.9"), "Resolve-DnsName", o);
            o = Sh(p, "W", "ps Resolve-DnsName nope.lan");
            Ok(o.Contains("DNS name does not exist"), "Resolve-DnsName of a missing name", o);
            o = Sh(p, "W", "ps Test-NetConnection 10.5.0.9 -Port 80");
            Ok(o.Contains("TcpTestSucceeded : True"), "Test-NetConnection -Port 80 to a web server", o);
            o = Sh(p, "W", "ps Test-NetConnection 10.5.0.2 -Port 80");
            Ok(o.Contains("TcpTestSucceeded : False") && o.Contains("WARNING: TCP connect"), "Test-NetConnection -Port to a closed port", o);
            o = Sh(p, "W", "ps Test-NetConnection 10.5.0.2");
            Ok(o.Contains("PingSucceeded    : True"), "Test-NetConnection (ping)", o);
            o = Sh(p, "W", "tracert 10.5.0.2");
            Ok(o.Contains("Tracing route to 10.5.0.2") && o.Contains("Trace complete."), "tracert", o);
            o = Sh(p, "W", "arp -a");
            Ok(o.Contains("10.5.0.2") && o.Contains("dynamic"), "arp -a", o);
            o = Sh(p, "W", "route print");
            Ok(o.Contains("0.0.0.0") && o.Contains("10.5.0.1"), "route print", o);
            o = Sh(p, "W", "net view \\\\files.lan");
            Ok(o.Contains("Public") && o.Contains("completed successfully"), "net view \\\\server lists the shared folders", o);
            o = Sh(p, "W", "ping nosuchname");
            Ok(o.Contains("could not find host nosuchname"), "ping of an unknown name", o);
            o = Sh(p, "W", "ipconfgi");
            Ok(o.Contains("is not recognized as an internal or external command"), "an unknown command", o);
            // the server's own firewall (a Windows-like server) blocks SMB
            Script.Run(p, "os S win10\nip S 10.5.0.9/24\nfirewall S on");
            o = Sh(p, "W", "net view \\\\10.5.0.9");
            Ok(o.Contains("System error 53"), "a Windows firewall blocks file sharing until allowed", o);
            Script.Run(p, "firewall S allow-ping");
            o = Sh(p, "W", "net view \\\\10.5.0.9");
            Ok(o.Contains("Public"), "...and allowing File and printer sharing opens it", o);
            // LLMNR: names on the local network without DNS
            Script.Run(p, "hostname G OFFICE-PC");
            Ok(Check.Resolve(p, "W", "OFFICE-PC") == "10.5.0.2", "a Windows-like PC finds a neighbour's name without DNS (LLMNR)");
        }

        static void LinuxLike()
        {
            var p = Build("add router R 0 0\nadd switch SW 0 0\nadd server U 0 0 ubuntu-server\nadd server D 0 0 debian\nadd pc C 0 0\nadd server N 0 0\n" +
                          "cable R Gi0/0 SW Fa0/24\ncable U Fa0 SW Fa0/1\ncable D Fa0 SW Fa0/2\ncable C Fa0 SW Fa0/3\ncable N Fa0 SW Fa0/4\n" +
                          "R: enable\nR: configure terminal\nR: interface g0/0\nR: ip address 172.16.0.1 255.255.255.0\nR: no shutdown\nR: end\nip C 172.16.0.50/24 172.16.0.1\nip N 172.16.0.53/24\ndns N on\ndns N web.lan 172.16.0.20");
            p.Sim.RunFor(12000);
            string o = Sh(p, "U", "ip a");
            Ok(o.Contains("enp0s3") && o.Contains("link/ether"), "ip a", o);
            o = Sh(p, "U", "ip addr add 172.16.0.20/24 dev enp0s3");
            Ok(o.Contains("Operation not permitted"), "ip addr add without sudo is refused", o);
            Sh(p, "U", "sudo ip addr add 172.16.0.20/24 dev enp0s3");
            Ok(Check.Address(p, "U") == "172.16.0.20", "sudo ip addr add (with the sudo password)");
            Sh(p, "U", "sudo ip route add default via 172.16.0.1");
            o = Sh(p, "U", "ip route");
            Ok(o.Contains("default via 172.16.0.1 dev enp0s3"), "ip route add default / ip route", o);
            // netplan: a typo fails, the fixed file works
            p.Find("U").Host.Files[LinuxShell.NetplanFile] = "network:\n  version: 2\n  ethernets:\n    enp0s3:\n      dhcp4: false\n      adresses: [172.16.0.21/24]\n      routes:\n        - to: default\n          via: 172.16.0.1\n      nameservers:\n        addresses: [172.16.0.53]\n";
            o = Sh(p, "U", "sudo netplan apply");
            Ok(o.Contains("unknown key 'adresses'") && o.Contains(":6:7:") && o.Contains("^"), "netplan reports the typo with line, column and ^", o);
            Ok(Check.Address(p, "U") == "172.16.0.20", "...and changes nothing");
            p.Find("U").Host.Files[LinuxShell.NetplanFile] = "network:\n  version: 2\n  ethernets:\n    enp0s3:\n\tdhcp4: true\n";
            o = Sh(p, "U", "sudo netplan apply");
            Ok(o.Contains("tabs"), "netplan refuses tabs", o);
            p.Find("U").Host.Files[LinuxShell.NetplanFile] = "network:\n  version: 2\n  ethernets:\n    enp0s3:\n      dhcp4: false\n      addresses: [172.16.0.21/24]\n      routes:\n        - to: default\n          via: 172.16.0.1\n      nameservers:\n        addresses: [172.16.0.53]\n";
            o = Sh(p, "U", "sudo netplan apply");
            Ok(Check.Address(p, "U") == "172.16.0.21" && p.Find("U").Host.DnsServers.Contains(Ip.Parse("172.16.0.53")), "netplan apply sets the address, gateway and DNS", o);
            o = Sh(p, "U", "cat /etc/resolv.conf");
            Ok(o.Contains("127.0.0.53"), "Ubuntu-like resolv.conf points at the local resolver", o);
            o = Sh(p, "U", "resolvectl status");
            Ok(o.Contains("172.16.0.53"), "resolvectl status shows the real DNS server", o);
            o = Sh(p, "U", "dig web.lan");
            Ok(o.Contains("status: NOERROR") && o.Contains("172.16.0.20"), "dig", o);
            o = Sh(p, "U", "ping -c 3 172.16.0.50");
            Ok(o.Contains("64 bytes from 172.16.0.50: icmp_seq=1 ttl=128") && o.Contains("3 packets transmitted, 3 received, 0% packet loss"), "Linux ping -c 3", o);
            o = Sh(p, "U", "traceroute 172.16.0.50");
            Ok(o.Contains("traceroute to 172.16.0.50"), "traceroute", o);
            // Debian: interfaces file and resolv.conf
            p.Find("D").Host.Files[LinuxShell.InterfacesFile] = "auto lo\niface lo inet loopback\n\nauto eth0\niface eth0 inet static\n    adress 172.16.0.30/24\n";
            o = Sh(p, "D", "sudo systemctl restart networking");
            Ok(o.Contains("Job for networking.service failed"), "a bad /etc/network/interfaces fails the restart", o);
            o = Sh(p, "D", "systemctl status networking");
            Ok(o.Contains("unknown option 'adress'"), "systemctl status networking shows why", o);
            p.Find("D").Host.Files[LinuxShell.InterfacesFile] = "auto lo\niface lo inet loopback\n\nauto eth0\niface eth0 inet static\n    address 172.16.0.30\n    netmask 255.255.255.0\n    gateway 172.16.0.1\n";
            Sh(p, "D", "sudo systemctl restart networking");
            Ok(Check.Address(p, "D") == "172.16.0.30", "Debian-like: /etc/network/interfaces + systemctl restart networking");
            Ok(Check.Resolve(p, "D", "web.lan") == "", "with no nameserver line, names fail");
            p.Find("D").Host.Files[LinuxShell.ResolvFile] = "nameserver 172.16.0.53\n";
            Ok(Check.Resolve(p, "D", "web.lan") == "172.16.0.20", "a nameserver line in /etc/resolv.conf fixes it");
            o = Sh(p, "D", "nslookup web.lan");
            Ok(o.Contains("Address: 172.16.0.20"), "Linux nslookup", o);
            o = Sh(p, "D", "ifconfig");
            Ok(o.Contains("command not found"), "ifconfig isn't there (use ip a)", o);
            // services, ss, ufw, curl, ssh
            Ok(!Check.WebOk(p, "C", "http://172.16.0.30/"), "no web server until apache2 starts");
            o = Sh(p, "D", "systemctl start apache2");
            Ok(o.Contains("Access denied"), "systemctl start without sudo is refused", o);
            Sh(p, "D", "sudo systemctl start apache2");
            Ok(Check.WebOk(p, "C", "http://172.16.0.30/"), "sudo systemctl start apache2: the page is visible to others");
            o = Sh(p, "D", "ss -tulpn");
            Ok(o.Contains(":80") && o.Contains("apache2"), "ss -tulpn lists the web server", o);
            Sh(p, "D", "sudo ufw enable");
            Ok(!Check.WebOk(p, "C", "http://172.16.0.30/"), "ufw enable blocks the web server");
            Ok(Check.CanPing(p, "C", "D"), "...but still answers pings (like the real ufw)");
            Sh(p, "D", "sudo ufw allow 80/tcp");
            Ok(Check.WebOk(p, "C", "http://172.16.0.30/"), "ufw allow 80/tcp lets it in");
            o = Sh(p, "D", "sudo ufw status");
            Ok(o.Contains("Status: active") && o.Contains("80/tcp") && o.Contains("ALLOW"), "ufw status", o);
            Sh(p, "D", "sudo systemctl start ssh");
            Sh(p, "D", "sudo ufw allow ssh");
            o = Sh(p, "U", "curl http://172.16.0.30/");
            Ok(o.Contains("apache2"), "curl gets the page", o);
            var ush = new LinuxShell(p.Find("U"));
            ush.Execute("ssh student@172.16.0.30");
            for (int g = 0; g < 10; g++)
            {
                p.Sim.RunUntil(p.Sim.Now + 10000, () => ush.Busy == null);
                if (ush.Pending != null) ush.Execute(ush.PendingSecret ? "netlab" : "yes"); else break;
            }
            p.Sim.RunUntil(p.Sim.Now + 10000, () => ush.Busy == null);
            Ok(ush.Prompt.StartsWith("student@d:"), "ssh logs in to the other computer", ush.TakeOutput() + " prompt " + ush.Prompt);
            ush.Execute("hostname");
            p.Sim.RunUntil(p.Sim.Now + 10000, () => ush.Busy == null);
            o = ush.TakeOutput();
            Ok(o.Trim().EndsWith("d"), "a command over ssh runs on the remote computer", o);
            ush.Execute("exit");
            Ok(ush.Prompt.StartsWith("student@u:"), "exit leaves the ssh session", ush.Prompt);
            o = Sh(p, "U", "sudo ufw status");
            Ok(o.Contains("inactive"), "ufw status inactive", o);
            o = Sh(p, "U", "hostnamectl set-hostname web01");
            Ok(o.Contains("Access denied"), "hostnamectl without sudo", o);
            Sh(p, "U", "sudo hostnamectl set-hostname web01");
            Ok(p.Find("U").Host.HostName == "web01", "sudo hostnamectl set-hostname");
            // wrong sudo password
            var s2 = new LinuxShell(p.Find("D"));
            s2.Execute("sudo ufw disable");
            s2.Execute("nope"); s2.Execute("nope2"); s2.Execute("nope3");
            o = s2.TakeOutput();
            Ok(o.Contains("3 incorrect password attempts"), "a wrong sudo password three times", o);
            // nano: writing needs sudo
            var s3 = new LinuxShell(p.Find("D"));
            s3.Execute("nano /etc/resolv.conf");
            Ok(s3.EditorFile == "/etc/resolv.conf" && s3.EditorText.Contains("nameserver"), "nano opens the file");
            s3.CloseEditor("nameserver 1.1.1.1\n", true);
            Ok(s3.TakeOutput().Contains("Permission denied") && p.Find("D").Host.Files[LinuxShell.ResolvFile].Contains("172.16.0.53"), "saving /etc without sudo is refused");
            // nmcli on the desktop
            var q = Build("add homerouter H 0 0\nadd laptop L 0 0 ubuntu\nhome H ssid=Den security=wpa2 key=pass1234");
            o = Sh(q, "L", "nmcli device wifi list");
            Ok(o.Contains("Den") && o.Contains("WPA2"), "nmcli device wifi list", o);
            o = Sh(q, "L", "nmcli device wifi connect Den password wrong");
            Ok(o.Contains("Secrets were required"), "nmcli with a wrong key", o);
            o = Sh(q, "L", "nmcli device wifi connect Den password pass1234");
            q.Sim.RunFor(3000);
            Ok(o.Contains("successfully activated") && Check.Address(q, "L").StartsWith("192.168.0."), "nmcli device wifi connect", o);
        }

        static void OtherPersonalities()
        {
            var p = Build("add homerouter H 0 0\nadd cloud I 0 0\nadd laptop M 0 0 macos\nadd phone A 0 0 android\nadd tablet T 0 0 iphone\nadd printer P 0 0\nadd pc W 0 0 win10\nadd tv TV 0 0\nadd nas NAS 0 0\nadd camera Cam 0 0\n" +
                          "cable H Internet I Eth0\ncable P Fa0 H Ethernet1\ncable W Fa0 H Ethernet2\ncable NAS Fa0 H Ethernet3\ncable Cam Fa0 H Ethernet4\nhome H ssid=Home security=wpa2 key=abcd1234");
            p.Sim.RunFor(5000);
            string o = Sh(p, "M", "networksetup -setairportnetwork en0 Home wrong");
            Ok(o.Contains("Failed to join network Home"), "macOS-like: a wrong Wi-Fi password", o);
            Sh(p, "M", "networksetup -setairportnetwork en0 Home abcd1234");
            p.Sim.RunFor(3000);
            o = Sh(p, "M", "ifconfig en0");
            Ok(o.Contains("inet 192.168.0.") && o.Contains("status: active"), "macOS-like: joins Wi-Fi, ifconfig en0", o);
            o = Sh(p, "M", "networksetup -getinfo Wi-Fi");
            Ok(o.Contains("DHCP Configuration") && o.Contains("Router: 192.168.0.1"), "networksetup -getinfo", o);
            Sh(p, "M", "networksetup -setmanual Wi-Fi 192.168.0.77 255.255.255.0 192.168.0.1");
            Ok(Check.Address(p, "M") == "192.168.0.77", "networksetup -setmanual");
            Sh(p, "M", "networksetup -setdnsservers Wi-Fi 198.51.100.53");
            Ok(Check.WebOk(p, "M", "http://www.example.com/"), "the Mac browses the internet");
            o = Sh(p, "M", "ping -c 2 192.168.0.1");
            Ok(o.Contains("icmp_seq=0") && o.Contains("2 packets received"), "macOS-like ping output", o);
            // phones and the TV
            Script.Run(p, "wifi A Home abcd1234\nwifi T Home abcd1234\nwifi TV Home abcd1234");
            p.Sim.RunFor(4000);
            Ok(Check.WebOk(p, "A", "http://www.example.com/") && Check.WebOk(p, "T", "http://news.example.org/"), "both phones browse over Wi-Fi");
            Ok(Check.Address(p, "TV").StartsWith("192.168.0."), "the TV joins the Wi-Fi");
            // printer: DHCP address, its web page, discovery, printing
            string pa = Check.Address(p, "P");
            Ok(pa.StartsWith("192.168.0."), "the printer gets an address by DHCP", pa);
            var page = Check.Web(p, "W", "http://" + pa + "/");
            Ok(page != null && page.Body.Contains("Network settings") && page.Body.Contains("<form"), "the printer's own web page");
            List<string[]> found = null;
            Request.DiscoverPrinters(p.Find("W"), l => found = l);
            p.Sim.RunFor(3000);
            Ok(found != null && found.Count == 1 && found[0][2] == pa, "printer discovery finds it on the same network");
            Script.Run(p, "printer W Laser " + pa);
            Ok(Script.Run(p, "print W Laser").Count == 0 && p.Find("P").Host.Printed.Count == 1, "a test page prints");
            var form = new Dictionary<string, string> { { "mode", "manual" }, { "ip", "192.168.0.60" }, { "mask", "255.255.255.0" }, { "gateway", "192.168.0.1" }, { "dns", "" } };
            TextMsg resp = null;
            Request.Http(p.Find("W"), "http://" + pa + "/network", "POST", form, (m, e) => resp = m);
            p.Sim.RunFor(5000);
            Ok(resp != null && resp.Body.Contains("Saved") && Check.Address(p, "P") == "192.168.0.60", "the printer's web page changes its address");
            Ok(Script.Run(p, "print W Laser").Count > 0, "printing to the old address now fails");
            Script.Run(p, "printer W Laser 192.168.0.60");
            Ok(Script.Run(p, "print W Laser").Count == 0, "...and works at the new one");
            // NAS and camera
            o = Sh(p, "W", "net view \\\\" + Check.Address(p, "NAS"));
            Ok(o.Contains("Family"), "the NAS shares its folders", o);
            var cam = Check.Web(p, "W", "http://" + Check.Address(p, "Cam") + "/");
            Ok(cam != null && cam.Body.Contains("Live view"), "the camera's web page");
        }

        // ------------------------------------------------------------ physical view
        static void PhysicalView()
        {
            var p = new Project(3);
            p.Layout = Layouts.Create("school");
            Script.Run(p, "add switch A 0 0\nadd switch B 0 0\nadd pc PC 0 0\nadd router R 0 0");
            var a = p.Find("A"); var b = p.Find("B"); var pc = p.Find("PC");
            Ok(a.Place.Length > 0 && a.PX >= 0, "a device added in the logical view gets a place in the physical view");
            Ok(p.CanConnect(pc.Port("Fa0"), a.Port("Gi0/1"), "straight").Contains("SFP slot"), "copper into an empty SFP slot is refused");
            Ok(p.CanConnect(a.Port("Gi0/1"), b.Port("Gi0/1"), "fiber-mm").Contains("empty SFP slot"), "fibre into an empty slot is refused");
            Script.Run(p, "sfp A Gi0/1 mmf\nsfp B Gi0/1 smf");
            Ok(p.CanConnect(a.Port("Gi0/1"), b.Port("Gi0/1"), "fiber-mm").Contains("multimode and single-mode") || p.CanConnect(a.Port("Gi0/1"), b.Port("Gi0/1"), "fiber-mm").Contains("single-mode"), "mixing multimode and single-mode is refused", p.CanConnect(a.Port("Gi0/1"), b.Port("Gi0/1"), "fiber-mm"));
            Ok(p.CanConnect(pc.Port("Fa0"), a.Port("Gi0/1"), "straight").Contains("fibre module"), "copper into a fibre module is refused");
            Script.Run(p, "sfp B Gi0/1 mmf");
            Ok(p.CanConnect(a.Port("Gi0/1"), b.Port("Gi0/1"), "fiber-mm") == "", "matching modules take a fibre cable");
            Ok(Project.AutoCable(a.Port("Gi0/1"), b.Port("Gi0/1")) == "fiber-mm", "the automatic cable picks fibre for fibre modules");
            Ok(p.CanConnect(pc.Port("Fa0"), a.Port("Console"), "straight") != "" && p.CanConnect(pc.Port("RS-232"), a.Port("Console"), "console") == "", "a console cable goes RS-232 to Console only");
            // distances
            Script.Run(p, "place A main/f1 10 10\nplace B main/f1 20 15\nplace PC main/f2 10 10\nplace R gym/f1 5 5");
            double same = Physical.RunLength(p, a, b);
            Ok(Math.Abs(same - 16) < 0.01, "same floor: walls-following (Manhattan) distance + slack", same.ToString());
            double up = Physical.RunLength(p, a, pc);
            Ok(Math.Abs(up - (Physical.FloorHeight + 3 + 1)) < 0.01, "one floor up: the rise between floors", up.ToString());
            double far = Physical.RunLength(p, a, p.Find("R"));
            Ok(far > 200, "another building: the distance across the campus", far.ToString());
            // copper too long, fibre fine
            var l1 = p.Connect(a.Port("Fa0/1"), p.Find("R").Port("Gi0/0"), "straight");
            Ok(l1.Status == "fault" && l1.Problem.Contains("100 m"), "copper over 100 m is a link fault", l1.Problem);
            var l2 = p.Connect(a.Port("Gi0/1"), b.Port("Gi0/1"), "fiber-mm");
            Ok(l2.Status == "up", "a short fibre link is up");
            Script.Run(p, "place B gym/f1 30 10");
            Ok(l2.Status == "up" && l2.Length > 200, "multimode fibre works over 200 m", l2.Length.ToString());
            Script.Run(p, "sfp A Gi0/1 smf\nsfp B Gi0/1 smf");
            Ok(l2.Status == "fault", "changing one end's module type breaks the link until the cable matches", l2.Problem);
            // wireless range and walls
            var w = new Project(4);
            w.Layout = Layouts.Create("house");
            Script.Run(w, "add homerouter H 0 0\nadd laptop L 0 0\nplace H home/f1 12 8\nplace L home/f1 12 9");
            double near = Physical.Signal(w, w.Find("H"), w.Find("L"));
            Script.Run(w, "place L home/f1 2 9");
            double walls = Physical.Signal(w, w.Find("H"), w.Find("L"));
            int nw = Physical.WallsBetween(w.Layout.Floor("home/f1"), 12, 8, 2, 9);
            Ok(near > walls && nw >= 2, "walls weaken the Wi-Fi signal (" + nw + " walls: " + near + " -> " + walls + ")");
            Script.Run(w, "range H 12");
            Ok(Physical.Signal(w, w.Find("H"), w.Find("L")) == 0, "out of range through walls: no signal");
            Script.Run(w, "wifi L HomeNet letmein123");
            Ok(w.Find("L").Host.Wifi[0].Status == "no-signal", "too far away: the laptop can't join", w.Find("L").Host.Wifi[0].Status);
            Script.Run(w, "place L home/f1 12 9");
            Ok(w.Find("L").Host.Wifi[0].Status == "connected", "moving closer connects it");
            // patch panels and wall jacks pass through
            var j = new Project(5);
            j.Layout = Layouts.Create("office");
            Script.Run(j, "add switch S 0 0\nadd patch PP 0 0\nadd jack J 0 0\nadd pc PC 0 0\nip PC 10.0.0.2/24\nadd pc PC2 0 0\nip PC2 10.0.0.3/24\ncable PC2 Fa0 S Fa0/2\n" +
                          "place S main/f1 30 12\nplace PP main/f1 30 13\nplace J main/f1 5 5\nplace PC main/f1 5 6\ncable PC Fa0 J Front\ncable J Back PP B1\ncable PP P1 S Fa0/1");
            Ok(j.Links.All(x => x.Status == "up") && Check.LinkUp(j, "PC", "S"), "PC -> wall jack -> patch panel -> switch makes one working channel");
            Ok(Check.CanPing(j, "PC", "PC2"), "...and traffic passes through it");
            Script.Run(j, "uncable PP P1");
            Ok(j.Links.Where(x => x.A.Dev.Name == "PC" || x.B.Dev.Name == "PC").All(x => x.Status != "up"), "unplugging the patch cable takes the channel down");
            // the same with the mouse: the automatic cable and the port picker (Andrew: "how do I connect patch panels to
            // a switch"; the picker used to show only the front ports, so a jack's line could never reach the switch)
            var m = new Project(5);
            m.Layout = Layouts.Create("office");
            Script.Run(m, "add switch S 0 0\nadd patch PP 0 0\nadd jack J 0 0\nadd pc PC 0 0\nip PC 10.0.0.2/24\nadd pc PC2 0 0\nip PC2 10.0.0.3/24\ncable PC2 Fa0 S Fa0/2\n" +
                          "place S main/f1 30 12\nplace PP main/f1 30 13\nplace J main/f1 5 5\nplace PC main/f1 5 6");
            Port pa, pb;
            foreach (var pair in new[] { new[] { "PC", "J" }, new[] { "J", "PP" }, new[] { "PP", "S" } })
            {
                bool okAuto = PortLayout.AutoPorts(m.Find(pair[0]), m.Find(pair[1]), out pa, out pb);
                if (okAuto) Script.Run(m, "cable " + pair[0] + " " + pa.Name + " " + pair[1] + " " + pb.Name);
            }
            Ok(m.Find("J").Ports.First(q => q.Name == "Front").Links.Count == 1 && m.Find("J").Ports.First(q => q.Name == "Back").Links.Count == 1,
               "automatic cables: the computer goes in the jack's front, the jack's back goes to the patch panel");
            Ok(m.Find("PP").Ports.First(q => q.Name == "Back1").Links.Count == 1 && m.Find("PP").Ports.First(q => q.Name == "Port1").Links.Count == 1,
               "automatic cables: the jack lands on the panel's back B1, the switch on the front P1 (the same line)");
            Ok(Check.CanPing(m, "PC", "PC2"), "...and the computer reaches the switch through jack and panel");
            var picker = PortLayout.Layout(m.Find("PP"), true);
            Ok(picker.Count(kv => kv.Key.Name.StartsWith("Back")) == 24 && picker.Count(kv => kv.Key.Name.StartsWith("Port")) == 24 &&
               picker.Where(kv => kv.Key.Name.StartsWith("Back")).All(kv => kv.Value.Y > picker.Where(f => f.Key.Name.StartsWith("Port")).Max(f => f.Value.Bottom)),
               "the port picker shows a patch panel's 24 back ports below its 24 front ports", picker.Count.ToString());
            Ok(PortLayout.Layout(m.Find("PP")).All(kv => kv.Key.Name.StartsWith("Port")), "racks still show only the front");
            var sz = PortLayout.PanelSize(m.Find("PP"), true);
            Ok(picker.All(kv => kv.Value.Bottom + 14 <= sz.Height && kv.Value.Right <= sz.Width), "every port fits on the picker", sz.ToString());
            // racks
            Script.Run(j, "newrack R1 main/f1 31 12 Rack A\nrack S R1 20");
            Ok(j.Find("S").Rack == "R1" && Physical.RunLength(j, j.Find("S"), j.Find("PC2")) > 0, "a switch mounted in a rack takes the rack's place");
            // views agree
            Ok(p.Links.Contains(l1) && l1.A.Links.Contains(l1), "the physical and logical views share one model (same cables)");
        }

        // ------------------------------------------------------------ labs
        static void LabsSolvable()
        {
            foreach (var l in Labs.All)
            {
                var p = l.Build();
                var before = l.Check(p);
                Ok(before.Any(x => !x), "lab " + l.Id + " isn't solved at the start", string.Join(",", before.Select(x => x ? "1" : "0").ToArray()));
                var errs = Script.Run(p, l.Answer);
                Ok(errs.Count == 0, "lab " + l.Id + " answer runs", string.Join("; ", errs.ToArray()));
                var after = l.Check(p);
                var missing = new List<string>();
                for (int i = 0; i < after.Length; i++) if (!after[i]) missing.Add(l.Goals[i].Text);
                Ok(missing.Count == 0, "lab " + l.Id + " is solved by its answer", string.Join(" | ", missing.ToArray()));
                Ok(l.AnswerSteps().Count > 0 && l.Hints.Count > 0 && l.Story.Length > 40, "lab " + l.Id + " has a story, hints and answer steps");
            }
        }

        // ------------------------------------------------------------ the brand lives only in brand.json
        static void BrandCheck()
        {
            string shipped = Brand.ShippedName;
            Ok(shipped.Length > 0 && Brand.Name == shipped, "brand.json gives the brand", shipped);
            // Learning Lab: the engine's own sources (found from the checks' folder upwards) and the app's Network Lab pages
            var hits = new List<string>();
            string root = AppContext.BaseDirectory;
            while (root != null && !File.Exists(Path.Combine(root, "LearningLab.slnx"))) root = Path.GetDirectoryName(root);
            Ok(root != null, "the repository is found from the checks' folder");
            if (root != null)
                foreach (var dir in new[] { Path.Combine(root, "src", "LearningLab.Engine", "NetworkLab"), Path.Combine(root, "src", "LearningLab", "NetworkLab") })
                {
                    if (!Directory.Exists(dir)) continue;
                    foreach (var f in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
                        if (File.ReadAllText(f).IndexOf(shipped, StringComparison.OrdinalIgnoreCase) >= 0) hits.Add(Path.GetFileName(f));
                }
            Ok(hits.Count == 0, "the brand name appears only in brand.json (not in the code)", string.Join(", ", hits.ToArray()));
            Brand.SetName("Acme");
            var p = Build("add router R 0 0");
            var s = new CliSession(p.Find("R")) { Started = true };
            s.Execute("show version");
            string o = s.TakeOutput();
            Ok(o.Contains("Acme Internetwork Operating System") && !o.Contains(shipped), "a different brand in Settings relabels show version", o);
            Ok(Catalog.Explain("router").Contains("Acme"), "...and the tooltips");
            Brand.SetName(null);
            Ok(Brand.Name == shipped && Brand.About.StartsWith(shipped + " is a made-up brand"), "back to the shipped brand; the About note names it");
        }

        // ------------------------------------------------------------ drawings: the model part of App Hub's DrawingChecks (TestsDrawing.cs)
        static void DrawingModel()
        {
            var p = new Project(11);
            var u = new UndoStack();
            // creating shapes, defaults, undo / redo
            var r = Drawings.Make(p, "rect", "logical", 100, 100, 120, 80);
            Drawings.Add(p, u, r, null);
            var arrow = Drawings.Make(p, "arrow", "logical", 0, 0, 0, 0);
            arrow.Points.Add(new Point2(0, 0)); arrow.Points.Add(new Point2(200, 0)); arrow.FitPoints();
            Drawings.Add(p, u, arrow, null);
            var area = Drawings.Make(p, "area", "logical", 0, 300, 300, 150);
            area.Text = "Sales VLAN 20";
            Drawings.Add(p, u, area, null);
            Ok(p.Shapes.Count == 3 && arrow.ArrowEnd && !arrow.ArrowStart && arrow.W == 200 && area.Dash == "dash" && area.Layer == "drawings", "new shapes get their kind's look (arrowhead, dashed area)");
            Ok(Drawings.KindName(r) == "Rectangle" && Drawings.KindName(Drawings.Make(p, "ellipse", "logical", 0, 0, 50, 50)) == "Circle", "shape names (a round ellipse is a circle)");
            u.Undo();
            Ok(p.Shapes.Count == 2 && !p.Shapes.Contains(area), "undo takes the last drawing away");
            u.Redo();
            Ok(p.Shapes.Count == 3 && p.Shapes[2] == area, "redo puts it back (the same object, same place in the order)");
            // properties through the same path as the panel
            Drawings.Edit(p, u, "fill colour", () => { r.Fill = "#FF0000"; r.Corner = 12; r.Text = "Server room"; r.Bold = true; r.Rotation = 30; });
            Ok(r.Fill == "#FF0000" && r.Corner == 12 && r.Text == "Server room", "changing a shape's properties");
            u.Undo();
            Ok(r.Fill == "#1E3A5F" && r.Corner == 0 && r.Text == "" && r.Rotation == 0, "undo puts every property back");
            u.Redo();
            // z-order
            Drawings.Order(p, u, new[] { r }, "front");
            Ok(p.Shapes.Last() == r, "bring to front");
            Drawings.Order(p, u, new[] { area }, "back");
            Ok(p.Shapes.First() == area, "send to back");
            Drawings.Order(p, u, new[] { area }, "forward");
            Ok(p.Shapes.IndexOf(area) == 1, "bring forward one step");
            Drawings.Order(p, u, new[] { area }, "backward");
            Ok(p.Shapes.IndexOf(area) == 0, "send backward one step");
            u.Undo(); u.Undo(); u.Undo();
            Ok(p.Shapes.Last() == r && p.Shapes.IndexOf(area) == 1, "undo the order changes");
            Ok(!area.Above && !r.Above, "drawings are under the devices unless asked");
            // align, distribute, group
            var q = new Project(12);
            var a1 = Drawings.Make(q, "rect", "logical", 10, 0, 20, 20); var a2 = Drawings.Make(q, "rect", "logical", 60, 40, 40, 20); var a3 = Drawings.Make(q, "rect", "logical", 300, 90, 20, 20);
            q.Shapes.AddRange(new[] { a1, a2, a3 });
            var qu = new UndoStack();
            Drawings.Align(q, qu, new[] { a1, a2, a3 }, "left");
            Ok(a1.X == 10 && a2.X == 10 && a3.X == 10, "align left");
            Drawings.Align(q, qu, new[] { a1, a2, a3 }, "bottom");
            Ok(a1.Y + a1.H == 110 && a2.Y + a2.H == 110 && a3.Y + a3.H == 110, "align bottom");
            qu.Undo(); qu.Undo();
            Ok(a1.X == 10 && a2.X == 60 && a3.X == 300 && a2.Y == 40, "undo the aligning");
            Drawings.Distribute(q, qu, new[] { a3, a1, a2 }, true);
            double g1 = a2.X - (a1.X + a1.W), g2 = a3.X - (a2.X + a2.W);
            Ok(Math.Abs(g1 - g2) < 1e-6 && a1.X == 10 && a3.X == 300, "distribute across: equal gaps, the ends stay", g1 + " / " + g2);
            string gid = Drawings.GroupShapes(q, qu, new[] { a1, a2 });
            Ok(gid.Length > 0 && a1.Group == gid && a2.Group == gid && Drawings.WithGroups(q, new[] { a1 }).Count == 2, "group: picking one picks the group");
            Drawings.Align(q, qu, new[] { a1, a3 }, "top");
            Ok(Math.Min(a1.Y, a2.Y) == a3.Y && a2.Y - a1.Y == 40, "a group lines up as one thing (keeps its shape)");
            var dup = Drawings.Duplicate(q, qu, new[] { a1 }, 20, 20);
            Ok(dup.Count == 2 && dup[0].Group == dup[1].Group && dup[0].Group != gid && dup[0].X == a1.X + 20, "duplicate copies the whole group as a new group");
            Drawings.Ungroup(q, qu, new[] { a1 });
            Ok(a1.Group == "" && a2.Group == "", "ungroup");
            // copy and paste, also into another project and another view
            string clip = Drawings.ToClipText(new[] { a1, a2 });
            var other = new Project(13);
            var pasted = Drawings.FromClipText(other, clip, "logical", 20, 20);
            Ok(pasted != null && pasted.Count == 2 && pasted[0].Id != pasted[1].Id && pasted[0].X == a1.X + 20 && pasted[0].Fill == a1.Fill, "copy and paste into another network", pasted == null ? "null" : pasted.Count + " " + pasted[0].X + " vs " + a1.X + " " + pasted[0].Id + " " + a1.Id);
            var onFloor = Drawings.FromClipText(other, clip, "main/f1", 0, 0);
            Ok(onFloor.All(s => s.View == "main/f1") && onFloor[0].W < a1.W, "pasting on a floor plan turns pixels into metres");
            Ok(Drawings.FromClipText(other, "hello", "logical", 0, 0) == null, "other text on the clipboard is ignored");
        }
    }
}
