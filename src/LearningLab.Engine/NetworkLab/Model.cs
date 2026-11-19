// Network Lab - the model: devices with ports, cables between ports, notes. The logical view (Topology) and the
// physical view (Physical) are two pictures of these same objects.
using System;
using System.Collections.Generic;
using System.Linq;

namespace NetworkLab
{
    public class Port
    {
        public Device Dev;
        public string Name = "", Short = "";
        /// <summary>copper, sfp, console (the device's console port), rs232 (a PC's serial port), wireless, passive</summary>
        public string Media = "copper";
        public int Speed = 100;
        /// <summary>For sfp slots: the module plugged in: "" (empty), mmf, smf, rj45.</summary>
        public string Sfp = "";
        public string Mac = "";
        public int Index;
        public readonly List<Link> Links = new List<Link>();
        // switch-port settings
        public bool Shutdown;
        public string Description = "";
        public bool Trunk;
        public int AccessVlan = 1;
        public int NativeVlan = 1;
        public string Allowed = "all";
        public bool Portfast;
        public int VoiceVlan;
        // runtime
        public readonly HashSet<int> StpBlocked = new HashSet<int>();
        public IpIf L3;            // a routed port's interface
        public long TxFrames, RxFrames;

        public Link Link { get { return Links.Count > 0 ? Links[0] : null; } }
        public bool IsWireless { get { return Media == "wireless"; } }
        public bool IsSerial { get { return Media == "console" || Media == "rs232"; } }
        public bool IsEthernet { get { return Media == "copper" || Media == "sfp" || Media == "passive"; } }
        public string FullName { get { return Dev.Name + " " + Short; } }
        public override string ToString() { return Short; }

        /// <summary>A switch port carries this VLAN (an access port of that VLAN, or a trunk that allows it).</summary>
        public bool CarriesVlan(int v)
        {
            if (!Trunk) return AccessVlan == v;
            return VlanList.Allows(Allowed, v);
        }

        /// <summary>The cable is plugged in and working, and this port isn't shut down.</summary>
        public bool IsUp
        {
            get
            {
                if (Shutdown) return false;
                if (IsWireless) return Links.Count > 0;
                var l = Link;
                return l != null && l.Status == "up";
            }
        }

        /// <summary>For a passive point (patch panel, wall jack): the port on the other side.</summary>
        public Port Pair
        {
            get
            {
                if (Media != "passive") return null;
                foreach (var p in Dev.Ports) if (p != this && p.Index == Index) return p;
                return null;
            }
        }
    }

    public static class VlanList
    {
        public static bool Allows(string list, int v)
        {
            if (string.IsNullOrEmpty(list) || list == "all") return true;
            if (list == "none") return false;
            foreach (var part in list.Split(','))
            {
                string s = part.Trim();
                int dash = s.IndexOf('-');
                int a, b;
                if (dash > 0) { if (int.TryParse(s.Substring(0, dash), out a) && int.TryParse(s.Substring(dash + 1), out b) && v >= a && v <= b) return true; }
                else if (int.TryParse(s, out a) && a == v) return true;
            }
            return false;
        }
        public static bool IsValid(string list)
        {
            if (list == "all" || list == "none") return true;
            foreach (var part in list.Split(','))
            {
                string s = part.Trim(); int a, b; int dash = s.IndexOf('-');
                if (dash > 0) { if (!int.TryParse(s.Substring(0, dash), out a) || !int.TryParse(s.Substring(dash + 1), out b) || a < 1 || b > 4094 || a > b) return false; }
                else if (!int.TryParse(s, out a) || a < 1 || a > 4094) return false;
            }
            return true;
        }
        /// <summary>Adds or removes VLANs from a list ("all" stays "all" on add).</summary>
        public static string Change(string list, string what, bool add)
        {
            var set = new SortedSet<int>();
            if (list == "all") { if (add) return "all"; for (int i = 1; i <= 4094; i++) set.Add(i); }
            else if (list != "none") for (int i = 1; i <= 4094; i++) if (Allows(list, i)) set.Add(i);
            for (int i = 1; i <= 4094; i++) if (Allows(what, i)) { if (add) set.Add(i); else set.Remove(i); }
            if (set.Count == 4094) return "all";
            if (set.Count == 0) return "none";
            return Compact(set);
        }
        public static string Compact(IEnumerable<int> vlans)
        {
            var l = vlans.OrderBy(x => x).ToList();
            var parts = new List<string>();
            int i = 0;
            while (i < l.Count)
            {
                int j = i;
                while (j + 1 < l.Count && l[j + 1] == l[j] + 1) j++;
                parts.Add(j > i ? l[i] + "-" + l[j] : l[i].ToString());
                i = j + 1;
            }
            return string.Join(",", parts.ToArray());
        }
    }

    public class Link
    {
        public string Id = "";
        public Port A, B;
        /// <summary>auto (chosen for you), straight, cross, fiber-mm, fiber-sm, console, wireless</summary>
        public string Cable = "straight";
        /// <summary>Copper category for straight / cross: cat5e or cat6.</summary>
        public string Category = "cat6";
        /// <summary>Metres; computed from the physical view unless Manual.</summary>
        public double Length = 2;
        public bool ManualLength;
        // computed by Sim.RefreshLinks
        public string Status = "down";   // up, down, fault
        public string Problem = "";      // plain-language reason when not up
        public double Signal = 1;        // wireless: 0..1
        public Port Other(Port p) { return p == A ? B : A; }
        public bool IsWireless { get { return Cable == "wireless"; } }
        public bool IsFiber { get { return Cable == "fiber-mm" || Cable == "fiber-sm"; } }
        public bool IsConsole { get { return Cable == "console"; } }
        public string CableName
        {
            get
            {
                switch (Cable)
                {
                    case "straight": return "Copper straight-through (" + (Category == "cat5e" ? "Cat5e" : "Cat6") + ")";
                    case "cross": return "Copper crossover (" + (Category == "cat5e" ? "Cat5e" : "Cat6") + ")";
                    case "fiber-mm": return "Fibre, multimode";
                    case "fiber-sm": return "Fibre, single-mode";
                    case "console": return "Console (rollover)";
                    case "wireless": return "Wi-Fi";
                }
                return Cable;
            }
        }
    }

    public class Note
    {
        public string Id = "";
        public double X, Y;
        public string Text = "Note";
        public string View = "logical";   // logical or a floor key
    }

    public class Rack
    {
        public string Id = "", Name = "Rack", Place = "";
        public double PX, PY;
        public int Units = 24;
    }

    public class Device
    {
        public Project P;
        public string Id = "", Kind = "pc", Name = "", Os = "";
        public double X, Y;
        // physical view: the floor ("building/floor"), metres on it; or a rack and its U position
        public string Place = "";
        public double PX = -1, PY = -1;
        public string Rack = "";
        public int RackU;
        public readonly List<Port> Ports = new List<Port>();
        public Bridge Bridge;
        public IpStack Ip;
        public Ios Ios;
        public Host Host;
        public WifiAp Ap;
        public HomeCfg Home;
        public bool PoweredOn = true;

        public bool IsIos { get { return Kind == "router" || Kind == "switch"; } }
        public bool IsPassive { get { return Kind == "patch" || Kind == "jack"; } }
        public bool IsEnd { get { return Host != null; } }
        public bool IsRackable { get { return Kind == "router" || Kind == "switch" || Kind == "patch" || Kind == "server" || Kind == "nas"; } }

        public Port Port(string name)
        {
            if (name == null) return null;
            foreach (var p in Ports) if (string.Equals(p.Short, name, StringComparison.OrdinalIgnoreCase) || string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) return p;
            string n = Ios.NormalizeIfName(name);
            if (n != null) foreach (var p in Ports) if (string.Equals(p.Name, n, StringComparison.OrdinalIgnoreCase)) return p;
            return null;
        }

        public string KindName { get { return Catalog.KindName(Kind); } }
        public string Model { get { return Catalog.ModelName(this); } }
        public override string ToString() { return Name; }

        /// <summary>The hostname a device answers to (DNS-less names, prompts).</summary>
        public string HostName
        {
            get
            {
                if (Ios != null) return Ios.Hostname;
                if (Host != null && Host.ComputerName.Length > 0) return Host.ComputerName;
                return Name;
            }
        }
    }

    /// <summary>Kinds of device, their ports and their names.</summary>
    public static class Catalog
    {
        public static readonly string[] Kinds = { "pc", "laptop", "phone", "tablet", "server", "printer", "switch", "router", "homerouter", "ap", "cloud", "nas", "camera", "tv", "console", "patch", "jack" };

        public static string KindName(string k)
        {
            switch (k)
            {
                case "pc": return "PC";
                case "laptop": return "Laptop";
                case "phone": return "Phone";
                case "tablet": return "Tablet";
                case "server": return "Server";
                case "printer": return "Printer";
                case "switch": return "Switch";
                case "router": return "Router";
                case "homerouter": return "Wireless router";
                case "ap": return "Access point";
                case "cloud": return "Internet (ISP)";
                case "nas": return "File server (NAS)";
                case "camera": return "IP camera";
                case "tv": return "Smart TV";
                case "console": return "Game console";
                case "patch": return "Patch panel";
                case "jack": return "Wall jack";
            }
            return k;
        }

        public static string BaseName(string k)
        {
            switch (k)
            {
                case "homerouter": return "HomeRouter";
                case "ap": return "AP";
                case "cloud": return "Internet";
                case "nas": return "NAS";
                case "camera": return "Camera";
                case "tv": return "TV";
                case "console": return "Console";
                case "patch": return "Patch";
                case "jack": return "Jack";
                case "pc": return "PC";
            }
            return KindName(k);
        }

        /// <summary>A plain-language explanation for tooltips.</summary>
        public static string Explain(string k)
        {
            string brand = Brand.FullModel(k);
            string by = brand.Length > 0 ? " (" + brand + ")" : "";
            switch (k)
            {
                case "pc": return "A desktop computer. Give it an IP address (or let DHCP do it), then use its command prompt and web browser.";
                case "laptop": return "A laptop: a computer with a network socket and Wi-Fi.";
                case "phone": return "A smartphone. It joins a Wi-Fi network with the network name (SSID) and password.";
                case "tablet": return "A tablet. Like a phone, it connects over Wi-Fi.";
                case "server": return "A server: a computer that offers services to others, such as web pages (HTTP), names (DNS) or addresses (DHCP).";
                case "printer": return "A network printer. It needs an IP address too; computers send it pages over the network.";
                case "switch": return "A switch" + by + " connects devices in one network and learns where each device is, so it can send each message only where it needs to go. Its ports can be split into VLANs.";
                case "router": return "A router" + by + " connects different networks together and chooses the way to each one using its routing table.";
                case "homerouter": return "A wireless router" + by + ", like the box at home: a small switch, Wi-Fi, a DHCP server and a router to the internet with NAT, all in one.";
                case "ap": return "A wireless access point" + by + ": it lets Wi-Fi devices join the wired network.";
                case "cloud": return "A stand-in for the internet and your internet provider (ISP). It hands out a public address and has a few web sites. Nothing here reaches the real internet.";
                case "nas": return "A file server (network attached storage) with shared folders.";
                case "camera": return "An IP camera with its own small web page.";
                case "tv": return "A smart TV that joins the Wi-Fi.";
                case "console": return "A game console that needs the internet.";
                case "patch": return "A patch panel" + by + ": the tidy end of the cables that run through the walls. Short patch cables link it to the switch.";
                case "jack": return "A wall jack: a network socket in the wall. Inside the wall, a cable runs to the patch panel.";
            }
            return "";
        }

        public static string ModelName(Device d)
        {
            string m = Brand.FullModel(d.Kind);
            if (m.Length > 0) return m;
            switch (d.Kind)
            {
                case "printer": return d.Os == "printer-b" ? "PageJet 400" : "LaserLeaf 200";
                case "pc": case "laptop": case "server": case "phone": case "tablet": return Personalities.Title(d.Os);
            }
            return KindName(d.Kind);
        }

        public static string DefaultOs(string k)
        {
            switch (k)
            {
                case "pc": case "laptop": case "server": return "generic";
                case "phone": return "android";
                case "tablet": return "iphone";
                case "printer": return "printer-a";
            }
            return "";
        }

        /// <summary>Builds the ports (and the parts that make the device work) of a new device.</summary>
        public static void Build(Device d, Random rng)
        {
            Func<string, string, string, int, Port> add = (name, shortName, media, speed) =>
            {
                var p = new Port { Dev = d, Name = name, Short = shortName, Media = media, Speed = speed, Index = d.Ports.Count, Mac = Mac.Next(rng) };
                d.Ports.Add(p);
                return p;
            };
            switch (d.Kind)
            {
                case "pc": case "server": case "printer": case "nas": case "camera":
                    add("FastEthernet0", "Fa0", "copper", 100);
                    if (d.Kind == "pc" || d.Kind == "server") add("RS-232", "RS-232", "rs232", 0);
                    break;
                case "laptop":
                    add("FastEthernet0", "Fa0", "copper", 100);
                    add("Wireless0", "Wi-Fi", "wireless", 300);
                    add("RS-232", "RS-232", "rs232", 0);
                    break;
                case "phone": case "tablet":
                    add("Wireless0", "Wi-Fi", "wireless", 300);
                    break;
                case "tv": case "console":
                    add("Ethernet0", "Eth0", "copper", 100);
                    add("Wireless0", "Wi-Fi", "wireless", 300);
                    break;
                case "switch":
                    for (int i = 1; i <= 24; i++) add("FastEthernet0/" + i, "Fa0/" + i, "copper", 100);
                    add("GigabitEthernet0/1", "Gi0/1", "sfp", 1000);
                    add("GigabitEthernet0/2", "Gi0/2", "sfp", 1000);
                    add("Console", "Console", "console", 0);
                    break;
                case "router":
                    for (int i = 0; i <= 2; i++) add("GigabitEthernet0/" + i, "Gi0/" + i, "copper", 1000);
                    add("GigabitEthernet0/3", "Gi0/3", "sfp", 1000);
                    add("Console", "Console", "console", 0);
                    break;
                case "homerouter":
                    add("Internet", "Internet", "copper", 1000);
                    for (int i = 1; i <= 4; i++) add("Ethernet" + i, "Ethernet" + i, "copper", 1000);
                    add("Wireless", "Wi-Fi", "wireless", 300);
                    break;
                case "ap":
                    add("Port0", "Port0", "copper", 1000);
                    add("Wireless", "Wi-Fi", "wireless", 300);
                    break;
                case "cloud":
                    for (int i = 0; i <= 3; i++) add("Ethernet" + i, "Eth" + i, "copper", 1000);
                    add("Fiber0", "Fiber0", "sfp", 1000);
                    d.Ports[4].Sfp = "smf";
                    break;
                case "patch":
                    for (int i = 1; i <= 24; i++) { var p = add("Port" + i, "P" + i, "passive", 1000); p.Index = i; }
                    for (int i = 1; i <= 24; i++) { var p = add("Back" + i, "B" + i, "passive", 1000); p.Index = i; }
                    break;
                case "jack":
                    { var a = add("Front", "Front", "passive", 1000); a.Index = 1; var b = add("Back", "Back", "passive", 1000); b.Index = 1; }
                    break;
            }
            // the parts that make it work
            switch (d.Kind)
            {
                case "switch":
                    d.Bridge = new Bridge(d);
                    foreach (var p in d.Ports) if (p.IsEthernet) d.Bridge.Members.Add(p);
                    d.Ip = new IpStack(d);
                    d.Ios = new Ios(d);
                    d.Bridge.AddSvi(1, true);
                    break;
                case "router":
                    d.Ip = new IpStack(d) { Forwarding = true };
                    foreach (var p in d.Ports) if (p.IsEthernet) { var f = d.Ip.AddRoutedPort(p); f.Shutdown = true; p.Shutdown = true; }
                    d.Ios = new Ios(d);
                    break;
                case "homerouter":
                    d.Bridge = new Bridge(d);
                    foreach (var p in d.Ports) if (p.Name != "Internet") d.Bridge.Members.Add(p);
                    d.Ip = new IpStack(d) { Forwarding = true };
                    d.Ap = new WifiAp(d) { Ssid = "HomeNet", Security = "wpa2", Key = "letmein123" };
                    HomeRouter.Init(d);
                    break;
                case "ap":
                    d.Bridge = new Bridge(d);
                    foreach (var p in d.Ports) d.Bridge.Members.Add(p);
                    d.Ap = new WifiAp(d) { Ssid = "NetLab", Security = "open" };
                    break;
                case "cloud":
                    d.Ip = new IpStack(d) { Forwarding = true };
                    Cloud.Init(d);
                    break;
                case "patch": case "jack":
                    break;
                default:
                    d.Ip = new IpStack(d);
                    foreach (var p in d.Ports) if (p.IsEthernet || p.IsWireless) d.Ip.AddRoutedPort(p);
                    d.Host = new Host(d);
                    if (string.IsNullOrEmpty(d.Os)) d.Os = DefaultOs(d.Kind);
                    Personalities.Apply(d);
                    break;
            }
        }
    }

    public class Project
    {
        public readonly List<Device> Devices = new List<Device>();
        public readonly List<Link> Links = new List<Link>();
        public readonly List<Note> Notes = new List<Note>();
        public readonly List<Rack> Racks = new List<Rack>();
        /// <summary>Drawings (decoration only, see Drawing.cs): shapes in z-order, labels on devices and cables, the
        /// map of each view and its layers.</summary>
        public readonly List<Shape> Shapes = new List<Shape>();
        public readonly List<Callout> Callouts = new List<Callout>();
        public readonly Dictionary<string, MapInfo> Maps = new Dictionary<string, MapInfo>();
        public readonly Dictionary<string, LayerSet> Layers = new Dictionary<string, LayerSet>();
        public Layout Layout = Layouts.Create("office");
        public Sim Sim;
        public Random Rng;
        public int NextId = 1;
        public string LabId = "";
        public Dictionary<string, bool> LabDone = new Dictionary<string, bool>();
        public string FilePath = "";
        /// <summary>Raised after anything in the model changes (the views redraw).</summary>
        public event EventHandler Changed;
        public int Version;

        public Project() : this(1) { }
        public Project(int seed)
        {
            Rng = new Random(seed);
            Sim = new Sim(this);
        }

        public void Touch() { Version++; var h = Changed; if (h != null) h(this, EventArgs.Empty); }

        public string NewId(string prefix) { return prefix + (NextId++); }

        public Device Find(string nameOrId)
        {
            foreach (var d in Devices) if (string.Equals(d.Name, nameOrId, StringComparison.OrdinalIgnoreCase)) return d;
            foreach (var d in Devices) if (d.Id == nameOrId) return d;
            return null;
        }

        public string UniqueName(string kind)
        {
            string b = Catalog.BaseName(kind);
            for (int i = 0; ; i++) { string n = b + i; if (Find(n) == null) return n; }
        }

        public Device Add(string kind, double x, double y, string name)
        {
            var d = new Device { P = this, Kind = kind, X = x, Y = y, Id = NewId("d") };
            d.Name = string.IsNullOrEmpty(name) ? UniqueName(kind) : name;
            Catalog.Build(d, Rng);
            Devices.Add(d);
            Physical.PlaceNew(this, d);
            Sim.DeviceAdded(d);
            Sim.TopologyChanged();
            return d;
        }

        public void Remove(Device d)
        {
            foreach (var l in Links.Where(l => l.A.Dev == d || l.B.Dev == d).ToList()) Disconnect(l, false);
            Devices.Remove(d);
            Sim.TopologyChanged();
        }

        /// <summary>Plain-language reason a cable can't go between these ports, or "" if it can.</summary>
        public string CanConnect(Port a, Port b, string cable)
        {
            if (a == null || b == null) return "Pick a port at each end.";
            if (a.Dev == b.Dev) return "A cable needs two different devices.";
            if (cable == "wireless") return "Wi-Fi devices join a network from their Wi-Fi settings (network name and password), not with a cable.";
            if (a.IsWireless || b.IsWireless) return "That is a Wi-Fi radio, not a socket. Wi-Fi devices join from their Wi-Fi settings.";
            if (a.Links.Count > 0) return a.FullName + " already has a cable in it.";
            if (b.Links.Count > 0) return b.FullName + " already has a cable in it.";
            if (cable == "console")
            {
                bool ok = (a.Media == "console" && b.Media == "rs232") || (a.Media == "rs232" && b.Media == "console");
                return ok ? "" : "A console cable goes from a computer's RS-232 port to the Console port of a router or switch.";
            }
            if (a.IsSerial || b.IsSerial) return "Console and RS-232 ports only take a console cable.";
            if (cable == "fiber-mm" || cable == "fiber-sm")
            {
                foreach (var p in new[] { a, b })
                {
                    if (p.Media == "passive") continue;
                    if (p.Media != "sfp") return p.FullName + " is a copper socket: fibre needs an SFP slot with a fibre module.";
                    if (p.Sfp == "") return p.FullName + " is an empty SFP slot: plug in a fibre module first (right-click the port).";
                    if (p.Sfp == "rj45") return p.FullName + " has a copper (RJ-45) module: use a copper cable, or swap the module for a fibre one.";
                    string want = cable == "fiber-mm" ? "mmf" : "smf";
                    if (p.Sfp != want) return "The module in " + p.FullName + " is for " + (p.Sfp == "mmf" ? "multimode" : "single-mode") + " fibre, but this cable is " + (want == "mmf" ? "multimode" : "single-mode") + ". Both must match.";
                }
                return "";
            }
            // copper
            foreach (var p in new[] { a, b })
            {
                if (p.Media == "sfp" && p.Sfp != "rj45")
                    return p.Sfp == "" ? p.FullName + " is an SFP slot: a copper cable only fits with a copper (RJ-45) module in it."
                                       : p.FullName + " has a fibre module: copper doesn't fit. Use a fibre cable, or swap in a copper (RJ-45) module.";
            }
            return "";
        }

        public Link Connect(Port a, Port b, string cable)
        {
            if (cable == "auto") cable = AutoCable(a, b);
            string why = CanConnect(a, b, cable);
            if (why.Length > 0) throw new InvalidOperationException(why);
            var l = new Link { A = a, B = b, Cable = cable, Id = NewId("l") };
            a.Links.Add(l); b.Links.Add(l);
            Links.Add(l);
            Physical.UpdateLength(this, l);
            Sim.TopologyChanged();
            return l;
        }

        public void Disconnect(Link l, bool notify)
        {
            l.A.Links.Remove(l); l.B.Links.Remove(l);
            Links.Remove(l);
            if (notify) Sim.TopologyChanged();
        }
        public void Disconnect(Link l) { Disconnect(l, true); }

        /// <summary>The cable that works between two ports (the "automatic" cable).</summary>
        public static string AutoCable(Port a, Port b)
        {
            if (a.IsSerial || b.IsSerial) return "console";
            foreach (var p in new[] { a, b }) if (p.Media == "sfp" && (p.Sfp == "mmf" || p.Sfp == "smf")) return p.Sfp == "mmf" ? "fiber-mm" : "fiber-sm";
            return Wiring.NeedsCrossover(a, b) ? "cross" : "straight";
        }

        /// <summary>Picks the first free port that suits the cable (for the automatic cable and labs).</summary>
        public static Port FreePort(Device d, Device other)
        {
            // A patch panel takes cables from wall jacks (and other panels) at the back, everything else at the front. A
            // wall jack takes the cable to a patch panel at the back, a device at the front.
            bool wantBack = (d.Kind == "patch" && other.IsPassive && other != d) || (d.Kind == "jack" && other.Kind == "patch");
            foreach (var p in d.Ports)
            {
                if (p.Links.Count > 0 || p.IsWireless || p.IsSerial) continue;
                if (p.Media == "sfp" && p.Sfp != "rj45") continue;
                if (p.Media == "passive" && p.Name.StartsWith("Back") != wantBack) continue;
                if (d.Kind == "homerouter" && p.Name == "Internet" && other.Kind != "cloud" && other.Kind != "router" && other.Kind != "switch") continue;
                if (d.Kind == "homerouter" && p.Name != "Internet" && other.Kind == "cloud") continue;
                return p;
            }
            // a wall jack has one line: if the side it should use is taken, the other side will do
            if (d.Kind == "jack")
                foreach (var p in d.Ports) if (p.Links.Count == 0 && p.Name.StartsWith("Back") != wantBack) return p;
            return null;
        }
    }

    /// <summary>Which copper cable two ports need.</summary>
    public static class Wiring
    {
        /// <summary>MDI-X ports (switches, the LAN side of a home router, the ISP box) take a straight cable from a computer or router.</summary>
        public static bool IsMdix(Port p)
        {
            var d = p.Dev;
            if (d.Kind == "switch") return true;
            if (d.Kind == "homerouter") return p.Name != "Internet";
            if (d.Kind == "cloud") return true;
            return false;
        }
        /// <summary>Follows patch panels and wall jacks to the real device at the other end (or null).</summary>
        public static Port FarEnd(Port p)
        {
            Port cur = p;
            for (int guard = 0; guard < 16; guard++)
            {
                if (cur.Media != "passive") return cur;
                var pair = cur.Pair;
                if (pair == null || pair.Link == null) return null;
                cur = pair.Link.Other(pair);
            }
            return null;
        }
        public static bool NeedsCrossover(Port a, Port b)
        {
            var ea = a.Media == "passive" ? FarEndBehind(a) : a;
            var eb = b.Media == "passive" ? FarEndBehind(b) : b;
            if (ea == null || eb == null) return false;
            return IsMdix(ea) == IsMdix(eb);
        }
        static Port FarEndBehind(Port p)
        {
            var pair = p.Pair;
            if (pair == null || pair.Link == null) return null;
            return FarEnd(pair.Link.Other(pair));
        }
    }
}
