// Network Lab - the guided labs: a story, a starting network, goals the app checks by asking the simulation,
// hints, and an answer (a script, shown in plain words by "Show me the answer").
using System;
using System.Collections.Generic;
using System.Linq;

namespace NetworkLab
{
    public class Goal
    {
        public string Text = "";
        public Func<Project, bool> Test;
        public string Hint = "";
    }

    public class Lab
    {
        public string Id = "", Title = "", Level = "Beginner", Topic = "", Story = "";
        public string Start = "";        // script that builds the starting network
        public string Answer = "";       // script that solves it
        public string Layout = "office";
        public readonly List<Goal> Goals = new List<Goal>();
        public readonly List<string> Hints = new List<string>();
        public long SettleMs = 15000;

        public Lab G(string text, Func<Project, bool> test, string hint) { Goals.Add(new Goal { Text = text, Test = test, Hint = hint ?? "" }); return this; }
        public Lab H(string hint) { Hints.Add(hint); return this; }

        /// <summary>A new project with this lab's starting network.</summary>
        public Project Build()
        {
            var p = new Project(Id.GetHashCode() & 0xFFFF);
            p.Layout = Layouts.Create(Layout);
            var errs = Script.Run(p, Start);
            if (errs.Count > 0) throw new Exception("Lab " + Id + " start: " + string.Join("; ", errs.ToArray()));
            p.LabId = Id;
            p.Sim.RunFor(200);
            return p;
        }

        /// <summary>Checks every goal on a settled copy of the network (the learner's own network isn't touched).</summary>
        public bool[] Check(Project p)
        {
            var c = Check2(p);
            return c;
        }

        bool[] Check2(Project p)
        {
            var r = new bool[Goals.Count];
            Project c;
            try { c = NetworkLab.Check.Settled(p, SettleMs); }
            catch (Exception) { return r; }
            for (int i = 0; i < Goals.Count; i++)
            {
                try { r[i] = Goals[i].Test(c); }
                catch (Exception) { r[i] = false; }
            }
            return r;
        }

        /// <summary>The answer in plain words, one step per line.</summary>
        public List<string> AnswerSteps()
        {
            var l = new List<string>();
            foreach (var line in Answer.Replace("\r", "").Split('\n'))
            {
                string s = Script.Describe(line);
                if (s.Length > 0) l.Add(s);
            }
            return l;
        }
    }

    public static class Labs
    {
        static List<Lab> all;
        public static List<Lab> All { get { if (all == null) all = Make(); return all; } }
        public static Lab Get(string id) { return All.FirstOrDefault(l => l.Id == id); }

        static Device D(Project p, string n) { return p.Find(n); }
        static bool PingOk(Project p, string a, string b) { return NetworkLab.Check.CanPing(p, a, b); }
        static bool InNet(Project p, string dev, string cidr)
        {
            string a = NetworkLab.Check.Address(p, dev);
            uint ip; if (!Ip.TryParse(a, out ip)) return false;
            int s = cidr.IndexOf('/');
            uint net = Ip.Parse(cidr.Substring(0, s)), mask = Ip.MaskOf(int.Parse(cidr.Substring(s + 1)));
            return (ip & mask) == net && ip != net && ip != Ip.BroadcastOf(net, mask);
        }
        static bool Linked(Project p, string a, string b) { return NetworkLab.Check.LinkUp(p, a, b); }
        static bool Vlan(Project p, string sw, int v) { var d = D(p, sw); return d != null && d.Bridge.Vlans.ContainsKey(v); }
        static bool PortVlan(Project p, string sw, string port, int v) { var d = D(p, sw); var pt = d.Port(port); return pt != null && !pt.Trunk && pt.AccessVlan == v; }
        static bool Trunk(Project p, string sw, string port) { var pt = D(p, sw).Port(port); return pt != null && pt.Trunk; }
        static bool RipRoute(Project p, string r, string cidr)
        {
            var d = D(p, r);
            int s = cidr.IndexOf('/');
            uint net = Ip.Parse(cidr.Substring(0, s)), mask = Ip.MaskOf(int.Parse(cidr.Substring(s + 1)));
            return d.Ip.Routes().Any(x => x.Net == net && x.Mask == mask && x.Code == "R");
        }
        static bool Printed(Project p, string printer, string from) { var d = D(p, printer); return d != null && d.Host.Printed.Any(x => x.StartsWith(from + ":")); }

        static List<Lab> Make()
        {
            var L = new List<Lab>();
            Lab l;

            // ------------------------------------------------------------ 1
            l = new Lab
            {
                Id = "two-pcs", Title = "1. Two PCs talking", Level = "Beginner", Topic = "Cables and IP addresses", Layout = "house",
                Story = "Sam and Alex want their two computers to talk to each other, with no switch or router: just one cable. Connect PC1 to PC2, give them addresses in the same network (for example 192.168.1.10 and 192.168.1.11, mask 255.255.255.0), and check with ping.",
                Start = "add pc PC1 220 260\nadd pc PC2 560 260\nnote 330 160 Two computers, one cable",
                Answer = "cable PC1 Fa0 PC2 Fa0 cross\nip PC1 192.168.1.10/24\nip PC2 192.168.1.11/24\nsh PC1: ping 192.168.1.11"
            };
            l.G("PC1 and PC2 are connected and the link light is green", p => Linked(p, "PC1", "PC2"), "Two computers of the same kind need a crossover cable (or pick the automatic cable).")
             .G("Both PCs have addresses in the same network", p => { var a = NetworkLab.Check.Address(p, "PC1"); var b = NetworkLab.Check.Address(p, "PC2"); uint x, y; return Ip.TryParse(a, out x) && Ip.TryParse(b, out y) && x != y && (x & 0xFFFFFF00) == (y & 0xFFFFFF00); }, "Click each PC > Desktop > IP Configuration, choose Static and type the address and mask.")
             .G("PC1 can ping PC2", p => PingOk(p, "PC1", "PC2"), "On PC1: Desktop > Command Prompt, type  ping 192.168.1.11");
            l.H("A cable between two PCs needs to be a crossover: each computer sends on the wires the other listens on.").H("Same network means the first three numbers match when the mask is 255.255.255.0.");
            L.Add(l);

            // ------------------------------------------------------------ 2
            l = new Lab
            {
                Id = "switch-four", Title = "2. A switch and four PCs", Level = "Beginner", Topic = "Switches", Layout = "office",
                Story = "The new office has four computers and one switch. Cable each PC to the switch and give them addresses 192.168.10.1 to 192.168.10.4 (mask 255.255.255.0). A switch learns where each computer is; watch it happen in Simulation mode.",
                Start = "add switch SW1 400 160\nadd pc PC1 160 360\nadd pc PC2 320 380\nadd pc PC3 480 380\nadd pc PC4 640 360",
                Answer = "cable PC1 Fa0 SW1 Fa0/1\ncable PC2 Fa0 SW1 Fa0/2\ncable PC3 Fa0 SW1 Fa0/3\ncable PC4 Fa0 SW1 Fa0/4\nip PC1 192.168.10.1/24\nip PC2 192.168.10.2/24\nip PC3 192.168.10.3/24\nip PC4 192.168.10.4/24"
            };
            l.G("All four PCs are cabled to SW1", p => Linked(p, "PC1", "SW1") && Linked(p, "PC2", "SW1") && Linked(p, "PC3", "SW1") && Linked(p, "PC4", "SW1"), "A PC to a switch takes a straight-through cable.")
             .G("Every PC has an address in 192.168.10.0/24", p => InNet(p, "PC1", "192.168.10.0/24") && InNet(p, "PC2", "192.168.10.0/24") && InNet(p, "PC3", "192.168.10.0/24") && InNet(p, "PC4", "192.168.10.0/24"), "Desktop > IP Configuration on each PC.")
             .G("PC1 can ping PC4", p => PingOk(p, "PC1", "PC4"), "")
             .G("PC2 can ping PC3", p => PingOk(p, "PC2", "PC3"), "");
            l.H("Switch ports come up by themselves; the lights turn green once a PC is plugged in.").H("After a ping, type  show mac address-table  on SW1's CLI to see what it learned.");
            L.Add(l);

            // ------------------------------------------------------------ 3
            l = new Lab
            {
                Id = "home", Title = "3. Home network with Wi-Fi and the internet", Level = "Beginner", Topic = "Wireless, DHCP, the internet", Layout = "house",
                Story = "Set up a family home: connect the wireless router to the internet box, plug the desktop PC into the router, and join the laptop and the phone to the Wi-Fi called HomeNet (the password is letmein123). The router hands out addresses by itself (DHCP) and shares the internet (NAT).",
                Start = "add cloud Internet0 120 120\nadd homerouter HomeRouter0 380 220\nadd pc PC1 220 420\nadd laptop Laptop1 520 420\nadd phone Phone1 680 360\nplace HomeRouter0 home/f1 13 9\nplace PC1 home/f1 14 10\nplace Laptop1 home/f1 4 3\nplace Phone1 home/f1 12 2\n" +
                        "area logical 160 170 610 330 #F59E0B The family home\nobject house logical 735 205 64\nlabel Internet0 The internet box\n" +
                        "object sofa home/f1 4.5 1.2\nobject plant home/f1 0.8 0.8\nobject desk home/f1 14 10.9\nobject chair home/f1 14 11.6",
                Answer = "cable HomeRouter0 Internet Internet0 Eth0\ncable PC1 Fa0 HomeRouter0 Ethernet1\ndhcp PC1\nwifi Laptop1 HomeNet letmein123\nwifi Phone1 HomeNet letmein123"
            };
            l.G("The wireless router is connected to the internet and has an internet address", p => { var d = D(p, "HomeRouter0"); var w = HomeRouter.Wan(d); return w.Up && w.Ip != 0; }, "Use the router's Internet port to the internet box.")
             .G("PC1 got its address from the router (DHCP)", p => InNet(p, "PC1", "192.168.0.0/24") && D(p, "PC1").Host.Main.Dhcp, "PC1 > Desktop > IP Configuration > DHCP.")
             .G("The laptop is on the Wi-Fi", p => D(p, "Laptop1").Host.Wifi[0].Status == "connected", "Laptop1 > Desktop > Wi-Fi: network HomeNet, password letmein123.")
             .G("The phone is on the Wi-Fi", p => D(p, "Phone1").Host.Wifi[0].Status == "connected", "Phone1 > Settings > Wi-Fi.")
             .G("The laptop can open www.example.com", p => NetworkLab.Check.WebOk(p, "Laptop1", "http://www.example.com/"), "Laptop1 > Desktop > Web Browser.");
            l.H("The Internet port of a home router goes to the internet box; the numbered ports are for your own computers.").H("A wrong Wi-Fi password just means 'can't connect'. Check the letters carefully.");
            L.Add(l);

            // ------------------------------------------------------------ 4
            l = new Lab
            {
                Id = "two-subnets", Title = "4. Two networks and a router", Level = "Beginner", Topic = "Routers and default gateways",
                Story = "The office now has two networks: 192.168.1.0/24 on the left and 192.168.2.0/24 on the right. The router R1 sits between them but isn't set up, and the PCs don't know where the router is. Give R1's ports their addresses (.1 on each side), turn them on, and give every PC its default gateway.",
                Start = "add router R1 400 120\nadd switch SW1 220 260\nadd switch SW2 580 260\nadd pc PC1 140 420\nadd pc PC2 300 420\nadd pc PC3 580 420\n" +
                        "cable R1 Gi0/0 SW1 Fa0/24\ncable R1 Gi0/1 SW2 Fa0/24\ncable PC1 Fa0 SW1 Fa0/1\ncable PC2 Fa0 SW1 Fa0/2\ncable PC3 Fa0 SW2 Fa0/1\n" +
                        "ip PC1 192.168.1.10/24\nip PC2 192.168.1.11/24\nip PC3 192.168.2.10/24",
                Answer = "R1: enable\nR1: configure terminal\nR1: interface g0/0\nR1: ip address 192.168.1.1 255.255.255.0\nR1: no shutdown\nR1: interface g0/1\nR1: ip address 192.168.2.1 255.255.255.0\nR1: no shutdown\nR1: end\nip PC1 192.168.1.10/24 192.168.1.1\nip PC2 192.168.1.11/24 192.168.1.1\nip PC3 192.168.2.10/24 192.168.2.1"
            };
            l.G("R1's G0/0 is 192.168.1.1/24 and up", p => { var f = D(p, "R1").Port("Gi0/0").L3; return f.Up && f.Ip == Ip.Parse("192.168.1.1") && f.Mask == 0xFFFFFF00; }, "interface g0/0, ip address 192.168.1.1 255.255.255.0, no shutdown")
             .G("R1's G0/1 is 192.168.2.1/24 and up", p => { var f = D(p, "R1").Port("Gi0/1").L3; return f.Up && f.Ip == Ip.Parse("192.168.2.1") && f.Mask == 0xFFFFFF00; }, "Router ports start shut down: no shutdown turns them on.")
             .G("Every PC has its default gateway", p => D(p, "PC1").Host.Gateway == Ip.Parse("192.168.1.1") && D(p, "PC2").Host.Gateway == Ip.Parse("192.168.1.1") && D(p, "PC3").Host.Gateway == Ip.Parse("192.168.2.1"), "The default gateway is the router's address on the PC's own network.")
             .G("PC1 can ping PC3 (across the router)", p => PingOk(p, "PC1", "PC3"), "")
             .G("PC3 can ping PC2", p => PingOk(p, "PC3", "PC2"), "");
            l.H("On R1's CLI: enable, configure terminal, interface g0/0, ip address ..., no shutdown.").H("Without a default gateway, a PC can't send anything outside its own network.");
            L.Add(l);

            // ------------------------------------------------------------ 5
            l = new Lab
            {
                Id = "vlans", Title = "5. VLANs and inter-VLAN routing", Level = "Intermediate", Topic = "VLANs, trunks, router-on-a-stick",
                Story = "Sales (PC1, PC2) and the Office (PC3, PC4) share one switch but must be separate networks: VLAN 10 called Sales (192.168.10.0/24) and VLAN 20 called Office (192.168.20.0/24). Then let them talk through R1 with one cable: a trunk to the router and a subinterface per VLAN (router-on-a-stick).",
                Start = "add router R1 400 100\nadd switch SW1 400 260\nadd pc PC1 160 420\nadd pc PC2 320 440\nadd pc PC3 480 440\nadd pc PC4 640 420\n" +
                        "cable R1 Gi0/0 SW1 Fa0/24\ncable PC1 Fa0 SW1 Fa0/1\ncable PC2 Fa0 SW1 Fa0/2\ncable PC3 Fa0 SW1 Fa0/3\ncable PC4 Fa0 SW1 Fa0/4\n" +
                        "ip PC1 192.168.10.10/24 192.168.10.1\nip PC2 192.168.10.11/24 192.168.10.1\nip PC3 192.168.20.10/24 192.168.20.1\nip PC4 192.168.20.11/24 192.168.20.1\n" +
                        "area logical 90 360 300 150 #22C55E Sales - VLAN 10\narea logical 410 360 300 150 #3B82F6 Office - VLAN 20\nlabel R1 Router-on-a-stick\ncablelabel R1 SW1 One cable for both VLANs",
                Answer = "SW1: enable\nSW1: configure terminal\nSW1: vlan 10\nSW1: name Sales\nSW1: vlan 20\nSW1: name Office\nSW1: interface range fa0/1 - 2\nSW1: switchport mode access\nSW1: switchport access vlan 10\nSW1: interface range fa0/3 - 4\nSW1: switchport mode access\nSW1: switchport access vlan 20\nSW1: interface fa0/24\nSW1: switchport mode trunk\nSW1: end\n" +
                         "R1: enable\nR1: configure terminal\nR1: interface g0/0\nR1: no shutdown\nR1: interface g0/0.10\nR1: encapsulation dot1Q 10\nR1: ip address 192.168.10.1 255.255.255.0\nR1: interface g0/0.20\nR1: encapsulation dot1Q 20\nR1: ip address 192.168.20.1 255.255.255.0\nR1: end"
            };
            l.G("VLAN 10 (Sales) and VLAN 20 (Office) exist on SW1", p => Vlan(p, "SW1", 10) && Vlan(p, "SW1", 20) && D(p, "SW1").Bridge.Vlans[10].ToLowerInvariant() == "sales" && D(p, "SW1").Bridge.Vlans[20].ToLowerInvariant() == "office", "vlan 10, then name Sales")
             .G("Fa0/1 and Fa0/2 are in VLAN 10, Fa0/3 and Fa0/4 in VLAN 20", p => PortVlan(p, "SW1", "Fa0/1", 10) && PortVlan(p, "SW1", "Fa0/2", 10) && PortVlan(p, "SW1", "Fa0/3", 20) && PortVlan(p, "SW1", "Fa0/4", 20), "interface range fa0/1 - 2, switchport mode access, switchport access vlan 10")
             .G("Fa0/24 (to the router) is a trunk", p => Trunk(p, "SW1", "Fa0/24"), "interface fa0/24, switchport mode trunk")
             .G("PC1 can ping PC2 (same VLAN)", p => PingOk(p, "PC1", "PC2"), "")
             .G("PC1 can ping PC4 (through the router)", p => PingOk(p, "PC1", "PC4"), "Subinterfaces g0/0.10 and g0/0.20 with encapsulation dot1Q and the gateway addresses; and no shutdown on g0/0.");
            l.H("A trunk carries every VLAN on one cable, each frame tagged with its VLAN number (802.1Q).").H("The router needs one subinterface per VLAN: interface g0/0.10, encapsulation dot1Q 10, ip address 192.168.10.1 255.255.255.0.");
            L.Add(l);

            // ------------------------------------------------------------ 6 and 7: three routers
            string three = "add router R1 200 140\nadd router R2 400 100\nadd router R3 600 140\nadd switch SW1 160 300\nadd switch SW3 640 300\nadd pc PC1 120 440\nadd pc PC2 680 440\n" +
                           "cable R1 Gi0/1 R2 Gi0/0 cross\ncable R2 Gi0/1 R3 Gi0/0 cross\ncable R1 Gi0/0 SW1 Fa0/24\ncable R3 Gi0/1 SW3 Fa0/24\ncable PC1 Fa0 SW1 Fa0/1\ncable PC2 Fa0 SW3 Fa0/1\n" +
                           "R1: enable\nR1: configure terminal\nR1: hostname R1\nR1: interface g0/0\nR1: ip address 192.168.1.1 255.255.255.0\nR1: no shutdown\nR1: interface g0/1\nR1: ip address 10.0.12.1 255.255.255.252\nR1: no shutdown\nR1: end\n" +
                           "R2: enable\nR2: configure terminal\nR2: hostname R2\nR2: interface g0/0\nR2: ip address 10.0.12.2 255.255.255.252\nR2: no shutdown\nR2: interface g0/1\nR2: ip address 10.0.23.1 255.255.255.252\nR2: no shutdown\nR2: end\n" +
                           "R3: enable\nR3: configure terminal\nR3: hostname R3\nR3: interface g0/0\nR3: ip address 10.0.23.2 255.255.255.252\nR3: no shutdown\nR3: interface g0/1\nR3: ip address 192.168.3.1 255.255.255.0\nR3: no shutdown\nR3: end\n" +
                           "ip PC1 192.168.1.10/24 192.168.1.1\nip PC2 192.168.3.10/24 192.168.3.1";
            l = new Lab
            {
                Id = "static-routes", Title = "6. Static routes between three routers", Level = "Intermediate", Topic = "Static routing",
                Story = "Three routers in a row link two offices: 192.168.1.0/24 behind R1 and 192.168.3.0/24 behind R3. All the ports are set up, but no router knows the way to networks it isn't connected to. Add static routes (ip route) so PC1 and PC2 can reach each other.",
                Start = three,
                Answer = "R1: enable\nR1: configure terminal\nR1: ip route 192.168.3.0 255.255.255.0 10.0.12.2\nR1: ip route 10.0.23.0 255.255.255.252 10.0.12.2\nR1: end\n" +
                         "R2: enable\nR2: configure terminal\nR2: ip route 192.168.1.0 255.255.255.0 10.0.12.1\nR2: ip route 192.168.3.0 255.255.255.0 10.0.23.2\nR2: end\n" +
                         "R3: enable\nR3: configure terminal\nR3: ip route 192.168.1.0 255.255.255.0 10.0.23.1\nR3: ip route 10.0.12.0 255.255.255.252 10.0.23.1\nR3: end"
            };
            l.G("R1 has a route to 192.168.3.0/24", p => NetworkLab.Check.HasRoute(p, "R1", "192.168.3.0/24"), "On R1: ip route 192.168.3.0 255.255.255.0 10.0.12.2")
             .G("R3 has a route to 192.168.1.0/24", p => NetworkLab.Check.HasRoute(p, "R3", "192.168.1.0/24"), "")
             .G("R2 knows both offices", p => NetworkLab.Check.HasRoute(p, "R2", "192.168.1.0/24") && NetworkLab.Check.HasRoute(p, "R2", "192.168.3.0/24"), "R2 is in the middle: it needs a route each way.")
             .G("PC1 can ping PC2", p => PingOk(p, "PC1", "PC2"), "The answers need routes back too.");
            l.H("ip route <network> <mask> <next hop>: the next hop is the neighbour router's address.").H("Use  show ip route  to see what each router knows, and  tracert  from PC1 to see where packets stop.");
            L.Add(l);

            l = new Lab
            {
                Id = "rip", Title = "7. Let the routers share routes (RIP)", Level = "Intermediate", Topic = "Dynamic routing: RIP v2",
                Story = "Same three routers, but this time the routers should work out the routes themselves. Turn on RIP version 2 on each router and tell it which of its networks to share (network ...). Within seconds they'll tell each other everything they know.",
                Start = three,
                Answer = "R1: enable\nR1: configure terminal\nR1: router rip\nR1: version 2\nR1: network 192.168.1.0\nR1: network 10.0.0.0\nR1: no auto-summary\nR1: end\n" +
                         "R2: enable\nR2: configure terminal\nR2: router rip\nR2: version 2\nR2: network 10.0.0.0\nR2: no auto-summary\nR2: end\n" +
                         "R3: enable\nR3: configure terminal\nR3: router rip\nR3: version 2\nR3: network 192.168.3.0\nR3: network 10.0.0.0\nR3: no auto-summary\nR3: end"
            };
            l.G("RIP version 2 runs on R1, R2 and R3", p => new[] { "R1", "R2", "R3" }.All(n => D(p, n).Ip.Rip != null && D(p, n).Ip.Rip.Version == 2), "router rip, then version 2")
             .G("R1 learned 192.168.3.0/24 from RIP", p => RipRoute(p, "R1", "192.168.3.0/24"), "network 192.168.3.0 on R3 shares that network.")
             .G("R3 learned 192.168.1.0/24 from RIP", p => RipRoute(p, "R3", "192.168.1.0/24"), "")
             .G("PC1 can ping PC2", p => PingOk(p, "PC1", "PC2"), "");
            l.H("network takes the classful network: 10.0.0.0 covers both 10.0.12.x and 10.0.23.x links.").H("show ip route marks RIP routes with R; show ip protocols shows what RIP is doing.");
            L.Add(l);

            // ------------------------------------------------------------ 8
            l = new Lab
            {
                Id = "dhcp-dns", Title = "8. A DHCP and DNS server", Level = "Intermediate", Topic = "DHCP, relay, DNS, web",
                Story = "Server1 (192.168.1.10) should do three jobs: hand out addresses to the PCs on the other network (192.168.2.0/24, gateway 192.168.2.1, from .100), answer DNS for www.lab.local (= 192.168.1.10), and serve a web page. DHCP requests are broadcasts that stop at the router, so R1 must pass them on with ip helper-address.",
                Start = "add router R1 400 120\nadd switch SW1 200 260\nadd switch SW2 600 260\nadd server Server1 120 420\nadd pc PC1 520 420\nadd pc PC2 680 420\n" +
                        "cable R1 Gi0/0 SW1 Fa0/24\ncable R1 Gi0/1 SW2 Fa0/24\ncable Server1 Fa0 SW1 Fa0/1\ncable PC1 Fa0 SW2 Fa0/1\ncable PC2 Fa0 SW2 Fa0/2\n" +
                        "R1: enable\nR1: configure terminal\nR1: interface g0/0\nR1: ip address 192.168.1.1 255.255.255.0\nR1: no shutdown\nR1: interface g0/1\nR1: ip address 192.168.2.1 255.255.255.0\nR1: no shutdown\nR1: end\n" +
                        "ip Server1 192.168.1.10/24 192.168.1.1 192.168.1.10\ndhcp PC1\ndhcp PC2",
                Answer = "dhcpd Server1 Clients 192.168.2.0/24 192.168.2.1 192.168.1.10 192.168.2.100 50\ndhcpd Server1 on\ndns Server1 www.lab.local 192.168.1.10\ndns Server1 on\nweb Server1 on\n" +
                         "R1: enable\nR1: configure terminal\nR1: interface g0/1\nR1: ip helper-address 192.168.1.10\nR1: end\nsh PC1: ipconfig /renew\nsh PC2: ipconfig /renew"
            };
            l.G("PC1 and PC2 got addresses from Server1 (192.168.2.100 and up)", p => { uint a, b; return Ip.TryParse(NetworkLab.Check.Address(p, "PC1"), out a) && Ip.TryParse(NetworkLab.Check.Address(p, "PC2"), out b) && a >= Ip.Parse("192.168.2.100") && b >= Ip.Parse("192.168.2.100") && a < Ip.Parse("192.168.2.255") && b < Ip.Parse("192.168.2.255"); }, "Server1 > Services > DHCP, and ip helper-address on R1's g0/1.")
             .G("PC1 finds www.lab.local with DNS", p => NetworkLab.Check.Resolve(p, "PC1", "www.lab.local") == "192.168.1.10", "Server1 > Services > DNS: add www.lab.local = 192.168.1.10. The DHCP pool must give 192.168.1.10 as the DNS server.")
             .G("PC2 opens http://www.lab.local", p => NetworkLab.Check.WebOk(p, "PC2", "http://www.lab.local/"), "Server1 > Services > HTTP: on.");
            l.H("A DHCP pool for a far network: network 192.168.2.0/24, gateway 192.168.2.1, DNS 192.168.1.10.").H("ip helper-address 192.168.1.10 goes on the router port facing the PCs.");
            L.Add(l);

            // ------------------------------------------------------------ 9
            l = new Lab
            {
                Id = "nat", Title = "9. NAT/PAT to the internet", Level = "Intermediate", Topic = "NAT overload",
                Story = "R1 connects the office (192.168.1.0/24) to the internet provider with the public address 203.0.113.2. Private addresses can't travel on the internet, so R1 must swap them for its own public address on the way out (NAT overload, also called PAT) and back on the way in.",
                Start = "add cloud Internet0 640 100\nadd router R1 400 160\nadd switch SW1 400 300\nadd pc PC1 260 440\nadd pc PC2 540 440\n" +
                        "cable R1 Gi0/1 Internet0 Eth0\ncable R1 Gi0/0 SW1 Fa0/24\ncable PC1 Fa0 SW1 Fa0/1\ncable PC2 Fa0 SW1 Fa0/2\n" +
                        "R1: enable\nR1: configure terminal\nR1: interface g0/0\nR1: ip address 192.168.1.1 255.255.255.0\nR1: no shutdown\nR1: interface g0/1\nR1: ip address 203.0.113.2 255.255.255.224\nR1: no shutdown\nR1: exit\nR1: ip route 0.0.0.0 0.0.0.0 203.0.113.1\nR1: end\n" +
                        "ip PC1 192.168.1.10/24 192.168.1.1 198.51.100.53\nip PC2 192.168.1.11/24 192.168.1.1 198.51.100.53",
                Answer = "R1: enable\nR1: configure terminal\nR1: access-list 1 permit 192.168.1.0 0.0.0.255\nR1: ip nat inside source list 1 interface g0/1 overload\nR1: interface g0/0\nR1: ip nat inside\nR1: interface g0/1\nR1: ip nat outside\nR1: end"
            };
            l.G("R1 has a NAT overload rule for the office", p => D(p, "R1").Ip.Nat.Rules.Any(r => r.Overload && !r.IsStatic), "ip nat inside source list 1 interface g0/1 overload")
             .G("G0/0 is the inside and G0/1 the outside", p => D(p, "R1").Port("Gi0/0").L3.Nat == 1 && D(p, "R1").Port("Gi0/1").L3.Nat == 2, "ip nat inside / ip nat outside on the interfaces")
             .G("PC1 can ping 198.51.100.20 on the internet", p => PingOk(p, "PC1", "198.51.100.20"), "")
             .G("PC2 can open www.example.com", p => NetworkLab.Check.WebOk(p, "PC2", "http://www.example.com/"), "");
            l.H("access-list 1 permit 192.168.1.0 0.0.0.255 picks the addresses to translate (0.0.0.255 is a wildcard: the last number can be anything).").H("After a ping, show ip nat translations lists who is using which port.");
            L.Add(l);

            // ------------------------------------------------------------ 10
            l = new Lab
            {
                Id = "acl", Title = "10. Block one PC from the web server", Level = "Intermediate", Topic = "Extended access lists",
                Story = "PC1 (192.168.1.10) must not open the web server at 10.0.0.10, but PC2 still can, and PC1 can still ping the server. Write an extended access list on R1 and put it on the right interface in the right direction.",
                Start = "add router R1 400 160\nadd switch SW1 220 300\nadd server Web1 620 300\nadd pc PC1 140 440\nadd pc PC2 300 440\n" +
                        "cable R1 Gi0/0 SW1 Fa0/24\ncable R1 Gi0/1 Web1 Fa0 cross\ncable PC1 Fa0 SW1 Fa0/1\ncable PC2 Fa0 SW1 Fa0/2\n" +
                        "R1: enable\nR1: configure terminal\nR1: interface g0/0\nR1: ip address 192.168.1.1 255.255.255.0\nR1: no shutdown\nR1: interface g0/1\nR1: ip address 10.0.0.1 255.255.255.0\nR1: no shutdown\nR1: end\n" +
                        "ip PC1 192.168.1.10/24 192.168.1.1\nip PC2 192.168.1.11/24 192.168.1.1\nip Web1 10.0.0.10/24 10.0.0.1\nweb Web1 on",
                Answer = "R1: enable\nR1: configure terminal\nR1: access-list 101 deny tcp host 192.168.1.10 host 10.0.0.10 eq www\nR1: access-list 101 permit ip any any\nR1: interface g0/0\nR1: ip access-group 101 in\nR1: end"
            };
            l.G("PC1 can't open http://10.0.0.10", p => !NetworkLab.Check.WebOk(p, "PC1", "http://10.0.0.10/"), "access-list 101 deny tcp host 192.168.1.10 host 10.0.0.10 eq www")
             .G("PC2 still opens http://10.0.0.10", p => NetworkLab.Check.WebOk(p, "PC2", "http://10.0.0.10/"), "Every access list ends with an invisible deny: add permit ip any any.")
             .G("PC1 can still ping the server", p => PingOk(p, "PC1", "Web1"), "Only block web traffic (tcp ... eq www), not everything.");
            l.H("Extended lists go close to the source: on g0/0, direction in.").H("show access-lists shows how many packets each line matched.");
            L.Add(l);

            // ============================================================ simulated operating systems
            l = new Lab
            {
                Id = "win-printer", Title = "11. A static address so the PC can reach the printer", Level = "Beginner", Topic = "Windows-like settings", Layout = "office",
                Story = "There's no DHCP server in this small office, so the Windows PC gave itself a 169.254 address and can't see the printer at 192.168.5.50. Open the PC's Settings > Network & Internet > Ethernet > IP settings > Edit, choose Manual and give it 192.168.5.20 / 255.255.255.0. Then add the printer by its address and print a test page.",
                Start = "add switch SW1 400 180\nadd pc PC1 220 360 win11\nadd printer Printer1 600 360\ncable PC1 Fa0 SW1 Fa0/1\ncable Printer1 Fa0 SW1 Fa0/2\nip Printer1 192.168.5.50/24",
                Answer = "ip PC1 192.168.5.20/24\nprinter PC1 Office 192.168.5.50\nprint PC1 Office"
            };
            l.G("PC1 has a manual address in 192.168.5.0/24", p => InNet(p, "PC1", "192.168.5.0/24") && !D(p, "PC1").Host.Main.Dhcp, "Settings > Network & Internet > Ethernet > Edit > Manual, IPv4 on.")
             .G("PC1 can ping the printer", p => PingOk(p, "PC1", "192.168.5.50"), "")
             .G("PC1 printed a test page on Printer1", p => Printed(p, "Printer1", "PC1"), "Settings > Bluetooth & devices > Printers & scanners > Add device > add by IP address, then Print test page.");
            l.H("A 169.254.x.x address means 'I asked for DHCP and nobody answered'.").H("The prefix length 24 is the same as the mask 255.255.255.0.");
            L.Add(l);

            l = new Lab
            {
                Id = "linux-dns", Title = "12. The Linux server's DNS is wrong", Level = "Intermediate", Topic = "Linux: /etc/resolv.conf",
                Story = "Srv1, a Debian-like server, can ping everything by address, but names don't work: 'Temporary failure in name resolution'. The DNS server is DNS1 at 192.168.1.53. Open Srv1's terminal, look at /etc/resolv.conf (cat), fix it with sudo nano (the password is netlab) and test with dig www.example.lan.",
                Start = "add switch SW1 400 180\nadd server Srv1 220 360 debian\nadd server DNS1 600 360\ncable Srv1 Fa0 SW1 Fa0/1\ncable DNS1 Fa0 SW1 Fa0/2\nip DNS1 192.168.1.53/24\ndns DNS1 www.example.lan 192.168.1.80\ndns DNS1 on\n" +
                        "file Srv1 /etc/network/interfaces\n| # The network interfaces of this computer. After a change: sudo systemctl restart networking\n|\n| auto lo\n| iface lo inet loopback\n|\n| auto eth0\n| iface eth0 inet static\n|     address 192.168.1.30/24\n|     gateway 192.168.1.1\n" +
                        "file Srv1 /etc/resolv.conf\n| # The DNS servers this computer asks for names\n| nameserver 192.168.1.99\nsh Srv1: sudo systemctl restart networking",
                Answer = "file Srv1 /etc/resolv.conf\n| nameserver 192.168.1.53\nsh Srv1: dig www.example.lan"
            };
            l.G("Srv1 has 192.168.1.30", p => NetworkLab.Check.Address(p, "Srv1") == "192.168.1.30", "")
             .G("Srv1 can look up www.example.lan", p => NetworkLab.Check.Resolve(p, "Srv1", "www.example.lan") == "192.168.1.80", "sudo nano /etc/resolv.conf: the line should say  nameserver 192.168.1.53");
            l.H("cat /etc/resolv.conf shows which DNS server the computer asks.").H("In nano, Ctrl+O saves and Ctrl+X closes.");
            L.Add(l);

            l = new Lab
            {
                Id = "win-firewall", Title = "13. Ping fails: the Windows firewall", Level = "Beginner", Topic = "Firewalls",
                Story = "PC1 can't ping PC2, even though the cable and the addresses are right. PC2 is a Windows-like PC, and its firewall blocks incoming pings. Keep the firewall on, but allow pings: Control Panel > Windows Defender Firewall > Allow an app > File and Printer Sharing (or netsh advfirewall firewall add rule ... protocol=icmpv4 ... action=allow).",
                Start = "add switch SW1 400 180\nadd pc PC1 220 360\nadd pc PC2 600 360 win11\ncable PC1 Fa0 SW1 Fa0/1\ncable PC2 Fa0 SW1 Fa0/2\nip PC1 192.168.1.10/24\nip PC2 192.168.1.20/24\nfirewall PC2 on",
                Answer = "firewall PC2 allow-ping"
            };
            l.G("PC2's firewall is still on", p => D(p, "PC2").Host.WinFirewall, "Don't just switch it off: allow pings instead.")
             .G("PC1 can ping PC2", p => PingOk(p, "PC1", "PC2"), "Allow File and Printer Sharing (it includes pings).");
            l.H("A firewall that drops pings looks exactly like a missing computer: 'Request timed out'.");
            L.Add(l);

            l = new Lab
            {
                Id = "phone-tv", Title = "14. The phone and the TV on the home Wi-Fi", Level = "Beginner", Topic = "Wi-Fi", Layout = "house",
                Story = "The family Wi-Fi is called Family and its password is sunflower7. Connect the phone (Settings > Wi-Fi) and the smart TV (its Network menu) and check the phone can open www.example.com.",
                Start = "add cloud Internet0 120 120\nadd homerouter HomeRouter0 380 200\nadd phone Phone1 220 420\nadd tv TV1 560 420\ncable HomeRouter0 Internet Internet0 Eth0\nhome HomeRouter0 ssid=Family security=wpa2 key=sunflower7\nplace HomeRouter0 home/f1 12 8\nplace Phone1 home/f1 3 3\nplace TV1 home/f1 5 2",
                Answer = "wifi Phone1 Family sunflower7\nwifi TV1 Family sunflower7"
            };
            l.G("The phone is on the Wi-Fi", p => D(p, "Phone1").Host.Wifi[0].Status == "connected", "")
             .G("The TV is on the Wi-Fi and has an address", p => D(p, "TV1").Host.Wifi[0].Status == "connected" && NetworkLab.Check.Address(p, "TV1").StartsWith("192.168.0."), "")
             .G("The phone can open www.example.com", p => NetworkLab.Check.WebOk(p, "Phone1", "http://www.example.com/"), "");
            l.H("Wi-Fi passwords are case-sensitive.");
            L.Add(l);

            l = new Lab
            {
                Id = "netplan", Title = "15. A netplan typo keeps the server offline", Level = "Intermediate", Topic = "Linux: netplan",
                Story = "Web1, an Ubuntu-like server, should be 192.168.1.20/24 with gateway 192.168.1.1, but someone edited /etc/netplan/01-netcfg.yaml and sudo netplan apply now fails. Read the error, fix the file with sudo nano (password netlab) and apply it again.",
                Start = "add router R1 400 120\nadd switch SW1 400 260\nadd server Web1 600 400 ubuntu-server\ncable R1 Gi0/0 SW1 Fa0/24\ncable Web1 Fa0 SW1 Fa0/1\n" +
                        "R1: enable\nR1: configure terminal\nR1: interface g0/0\nR1: ip address 192.168.1.1 255.255.255.0\nR1: no shutdown\nR1: end\n" +
                        "file Web1 /etc/netplan/01-netcfg.yaml\n| # Network settings for this computer, read by: sudo netplan apply\n| network:\n|   version: 2\n|   ethernets:\n|     enp0s3:\n|       dhcp4: false\n|       adresses:\n|         - 192.168.1.20/24\n|       routes:\n|         - to: default\n|           via: 192.168.1.1\n" +
                        "sh Web1: sudo ip addr flush dev enp0s3",
                Answer = "file Web1 /etc/netplan/01-netcfg.yaml\n| network:\n|   version: 2\n|   ethernets:\n|     enp0s3:\n|       dhcp4: false\n|       addresses:\n|         - 192.168.1.20/24\n|       routes:\n|         - to: default\n|           via: 192.168.1.1\nsh Web1: sudo netplan apply"
            };
            l.G("Web1 has 192.168.1.20", p => NetworkLab.Check.Address(p, "Web1") == "192.168.1.20", "The error points at the line with the typo.")
             .G("Web1 can ping its gateway 192.168.1.1", p => PingOk(p, "Web1", "192.168.1.1"), "");
            l.H("YAML is fussy: spaces (never tabs) and exact key names. addresses has two d's.");
            L.Add(l);

            l = new Lab
            {
                Id = "office-printer", Title = "16. Add the office printer by IP", Level = "Beginner", Topic = "Printers",
                Story = "The office printer has a fixed address, 192.168.1.60. On the Windows-like PC, open Settings > Bluetooth & devices > Printers & scanners > Add device > Add a printer using an IP address, then print a test page.",
                Start = "add router R1 400 100\nadd switch SW1 400 240\nadd pc PC1 220 380 win10\nadd printer Printer1 600 380\ncable R1 Gi0/0 SW1 Fa0/24\ncable PC1 Fa0 SW1 Fa0/1\ncable Printer1 Fa0 SW1 Fa0/2\n" +
                        "R1: enable\nR1: configure terminal\nR1: ip dhcp excluded-address 192.168.1.1 192.168.1.99\nR1: ip dhcp pool OFFICE\nR1: network 192.168.1.0 255.255.255.0\nR1: default-router 192.168.1.1\nR1: interface g0/0\nR1: ip address 192.168.1.1 255.255.255.0\nR1: no shutdown\nR1: end\nip Printer1 192.168.1.60/24 192.168.1.1",
                Answer = "printer PC1 Office 192.168.1.60\nprint PC1 Office"
            };
            l.G("PC1 knows the printer at 192.168.1.60", p => D(p, "PC1").Host.Printers.Any(x => x[1] == "192.168.1.60"), "Add a printer using an IP address: 192.168.1.60")
             .G("A test page came out of Printer1", p => Printed(p, "Printer1", "PC1"), "Printers & scanners > the printer > Print a test page");
            l.H("Printers usually get fixed addresses so computers can always find them.");
            L.Add(l);

            l = new Lab
            {
                Id = "mac-dns", Title = "17. The Mac can't find web sites", Level = "Beginner", Topic = "macOS-like settings", Layout = "house",
                Story = "The Mac laptop is on the Wi-Fi and can ping 198.51.100.20, but web sites by name don't open: someone typed a wrong DNS server. In System Settings > Network > Wi-Fi > Details > DNS, replace it with 198.51.100.53 (or in Terminal: networksetup -setdnsservers Wi-Fi 198.51.100.53).",
                Start = "add cloud Internet0 120 120\nadd homerouter HomeRouter0 380 200\nadd laptop Mac1 560 420 macos\ncable HomeRouter0 Internet Internet0 Eth0\nplace HomeRouter0 home/f1 12 8\nplace Mac1 home/f1 4 3\nwifi Mac1 HomeNet letmein123\nrun 3000\nsh Mac1: networksetup -setmanual Wi-Fi 192.168.0.42 255.255.255.0 192.168.0.1\nsh Mac1: networksetup -setdnsservers Wi-Fi 192.168.0.250",
                Answer = "sh Mac1: networksetup -setdnsservers Wi-Fi 198.51.100.53"
            };
            l.G("The Mac uses 198.51.100.53 for DNS", p => D(p, "Mac1").Host.DnsServers.Contains(Ip.Parse("198.51.100.53")), "")
             .G("The Mac can open www.example.com", p => NetworkLab.Check.WebOk(p, "Mac1", "http://www.example.com/"), "");
            l.H("If pinging an address works but names don't, look at DNS.");
            L.Add(l);

            // ============================================================ physical view
            l = new Lab
            {
                Id = "classroom", Title = "18. Wire the new classroom", Level = "Beginner", Topic = "Physical view: cabling", Layout = "school",
                Story = "The new classroom on the first floor has four desks with PCs. Run a cable from each PC to the switch SW-Up in the first-floor wiring closet (Physical view > School > Main building > First floor). Every PC already has an address.",
                Start = "layout school main/f2\nadd switch SW-Up 400 120\nadd pc Desk1 160 360\nadd pc Desk2 320 380\nadd pc Desk3 480 380\nadd pc Desk4 640 360\n" +
                        "place SW-Up main/f2 15 19\nplace Desk1 main/f2 33 3\nplace Desk2 main/f2 37.5 3\nplace Desk3 main/f2 42 3\nplace Desk4 main/f2 46.5 3\n" +
                        "ip Desk1 10.10.1.11/24\nip Desk2 10.10.1.12/24\nip Desk3 10.10.1.13/24\nip Desk4 10.10.1.14/24",
                Answer = "cable Desk1 Fa0 SW-Up Fa0/1\ncable Desk2 Fa0 SW-Up Fa0/2\ncable Desk3 Fa0 SW-Up Fa0/3\ncable Desk4 Fa0 SW-Up Fa0/4"
            };
            l.G("All four desks are cabled to SW-Up", p => new[] { "Desk1", "Desk2", "Desk3", "Desk4" }.All(n => Linked(p, n, "SW-Up")), "Click a PC, pick its port, then click the switch and pick a free port.")
             .G("Desk1 can ping Desk4", p => PingOk(p, "Desk1", "Desk4"), "");
            l.H("Copper network cable works up to 100 m. Each run here is about 30 m.");
            L.Add(l);

            l = new Lab
            {
                Id = "fibre", Title = "19. Link the two buildings with fibre", Level = "Intermediate", Topic = "Physical view: fibre", Layout = "school",
                Story = "The sports hall is 200 m from the main building: too far for copper (100 m at most). Put a fibre module (SFP) in Gi0/1 of each closet switch and join them with a fibre cable. Multimode reaches 550 m, single-mode 10 km; both ends must match.",
                Start = "layout school\nadd switch SW-Main 220 160\nadd switch SW-Gym 600 160\nadd pc PC-Office 180 380\nadd pc PC-Gym 640 380\n" +
                        "place SW-Main main/f1 15 19\nplace SW-Gym gym/f1 27 25\nplace PC-Office main/f1 5 20\nplace PC-Gym gym/f1 34 26\n" +
                        "cable PC-Office Fa0 SW-Main Fa0/1\ncable PC-Gym Fa0 SW-Gym Fa0/1\nip PC-Office 10.20.0.10/24\nip PC-Gym 10.20.0.20/24\n" +
                        "area logical 120 90 210 360 #3B82F6 Main building\narea logical 520 90 210 360 #F59E0B Sports hall\narrow logical 340 150 510 150 200 m across the campus\n" +
                        "object tree campus 90 20 8\nobject tree campus 110 26 7\nobject tree campus 160 22 9\nobject tree campus 200 70 8\nobject carpark campus 200 85 22\nobject bus campus 115 37 14",
                Answer = "sfp SW-Main Gi0/1 mmf\nsfp SW-Gym Gi0/1 mmf\ncable SW-Main Gi0/1 SW-Gym Gi0/1 fiber-mm"
            };
            l.G("SW-Main and SW-Gym are linked and the link is up", p => Linked(p, "SW-Main", "SW-Gym"), "Right-click the SFP slot (or use the port picker) to insert a module first.")
             .G("The link is fibre", p => p.Links.Any(k => k.IsFiber && ((k.A.Dev.Name == "SW-Main" && k.B.Dev.Name == "SW-Gym") || (k.B.Dev.Name == "SW-Main" && k.A.Dev.Name == "SW-Gym"))), "Copper can't go 200 m.")
             .G("PC-Gym can ping PC-Office", p => PingOk(p, "PC-Gym", "PC-Office"), "");
            l.H("Look at the cable's length in its tooltip: anything over 100 m needs fibre.");
            L.Add(l);

            l = new Lab
            {
                Id = "too-long", Title = "20. Why is the warehouse offline?", Level = "Intermediate", Topic = "Physical view: cable length", Layout = "office",
                Story = "Someone ran one long copper cable from the office switch to the PC in the warehouse office. Its light is red and the PC is offline. Find out why (hover the cable), then fix it: put a switch in the warehouse closet, link the two closets with fibre, and plug the PC into the new switch.",
                Start = "layout office\nadd switch SW-Office 300 160\nadd server Files 140 360\nadd pc PC-W 620 380\n" +
                        "place SW-Office main/f1 31 12\nplace Files main/f1 33 4\nplace PC-W warehouse/f1 6 26\n" +
                        "cable Files Fa0 SW-Office Fa0/1\ncable PC-W Fa0 SW-Office Fa0/2\nip Files 10.30.0.5/24\nip PC-W 10.30.0.50/24",
                Answer = "uncable PC-W Fa0\nadd switch SW-W 620 200\nplace SW-W warehouse/f1 14 26\nsfp SW-Office Gi0/1 smf\nsfp SW-W Gi0/1 smf\ncable SW-Office Gi0/1 SW-W Gi0/1 fiber-sm\ncable PC-W Fa0 SW-W Fa0/1"
            };
            l.G("PC-W's cable works (no red light)", p => { var pt = D(p, "PC-W").Port("Fa0"); return pt.Link != null && pt.Link.Status == "up"; }, "Copper over 100 m fails: the signal fades.")
             .G("PC-W can ping the file server", p => PingOk(p, "PC-W", "Files"), "");
            l.H("The office and the warehouse are about 150 m apart.").H("A switch in the warehouse closet, fibre between the closets, and a short copper cable to the PC.");
            L.Add(l);

            return L;
        }
    }
}
