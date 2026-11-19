// Network Lab - device "personalities": the pretend operating systems of end devices (a Windows-like PC, an
// Ubuntu-like desktop, a Debian-like server, a macOS-like laptop, phones, printers and gadgets). Each one only
// changes how the device looks and which commands it understands; the settings the learner enters are what its
// simulated network card uses. New ones: subclass Personality and add it to Personalities.All.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NetworkLab
{
    public class Personality
    {
        public string Id = "generic", Title = "Simple desktop", Family = "generic";
        public string[] Kinds = { "pc", "laptop", "server" };
        public string Blurb = "";
        public virtual bool Llmnr { get { return Family == "windows" || Family == "generic" || Family == "linux"; } }
        public virtual int Ttl { get { return Family == "windows" || Family == "generic" ? 128 : 64; } }

        /// <summary>New device (or a new personality for it): sensible defaults.</summary>
        public virtual void Apply(Device d) { }
        /// <summary>After a project is opened.</summary>
        public virtual void Loaded(Device d) { }
        /// <summary>The adapter name people see (Ethernet, Wi-Fi, enp0s3, en0...).</summary>
        public virtual string IfName(Port p) { return p.IsWireless ? "Wireless0" : p.Name; }
        public virtual Shell NewShell(Device d) { return new WinShell(d, false); }
        /// <summary>Pages of the device's own little web site (printers, cameras); null = the normal web server.</summary>
        public virtual TextMsg HttpRequest(Device d, string method, string path, TextMsg req) { return null; }
        /// <summary>DNS servers when the personality keeps them somewhere special (Debian's /etc/resolv.conf); null = the adapters'.</summary>
        public virtual List<uint> DnsServers(Device d) { return null; }

        public IpIf AdapterByName(Device d, string name)
        {
            foreach (var f in d.Host.Adapters) if (string.Equals(IfName(f.Port), name, StringComparison.OrdinalIgnoreCase) || string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)) return f;
            return null;
        }
    }

    public class WindowsPersonality : Personality
    {
        public WindowsPersonality(string id, string title) { Id = id; Title = title; Family = "windows"; Kinds = new[] { "pc", "laptop", "server" }; Blurb = "A pretend Windows-style desktop: Start menu, Settings, Control Panel, Command Prompt and PowerShell, and a firewall that blocks pings."; }
        public override string IfName(Port p) { return p.IsWireless ? "Wi-Fi" : "Ethernet"; }
        public override Shell NewShell(Device d) { return new WinShell(d, false); }
        public override void Apply(Device d)
        {
            var h = d.Host;
            h.WinFirewall = true;
            if (h.ComputerName.Length == 0) h.ComputerName = "DESKTOP-" + RandomName(d, 7);
            foreach (var f in h.Adapters) if (!f.Dhcp && f.Ip == 0) f.Dhcp = true;
        }
        public static string RandomName(Device d, int n)
        {
            const string abc = "ABCDEFGHJKLMNPQRSTUVWXYZ0123456789";
            var r = new Random(d.Id.GetHashCode() ^ d.Name.GetHashCode());
            var sb = new StringBuilder();
            for (int i = 0; i < n; i++) sb.Append(abc[r.Next(abc.Length)]);
            return sb.ToString();
        }
    }

    public class LinuxPersonality : Personality
    {
        public bool Desktop;
        public bool Netplan;
        public LinuxPersonality(string id, string title, bool desktop, bool netplan)
        {
            Id = id; Title = title; Family = "linux"; Desktop = desktop; Netplan = netplan;
            Kinds = desktop ? new[] { "pc", "laptop" } : new[] { "server", "pc" };
            Blurb = desktop ? "A pretend Ubuntu-style desktop with a terminal: ip, ping, dig, nano, netplan, nmcli, ufw, ssh." : "A pretend Debian-style server with only a terminal: /etc/network/interfaces, /etc/resolv.conf, systemctl, apache2, ssh, ufw.";
        }
        public override string IfName(Port p)
        {
            if (Netplan) return p.IsWireless ? "wlp2s0" : "enp0s3";
            return p.IsWireless ? "wlan0" : "eth0";
        }
        public override Shell NewShell(Device d) { return new LinuxShell(d); }
        public override void Apply(Device d)
        {
            var h = d.Host;
            if (h.ComputerName.Length == 0) h.ComputerName = d.Name.ToLowerInvariant().Replace(" ", "-");
            var eth = h.Adapters.FirstOrDefault(f => !f.Port.IsWireless);
            string ifn = eth != null ? IfName(eth.Port) : "eth0";
            if (Netplan && !h.Files.ContainsKey(LinuxShell.NetplanFile))
            {
                h.Files[LinuxShell.NetplanFile] = "# Network settings for this computer, read by: sudo netplan apply\nnetwork:\n  version: 2\n  ethernets:\n    " + ifn + ":\n      dhcp4: true\n";
                foreach (var f in h.Adapters) if (!f.Port.IsWireless && f.Ip == 0) f.Dhcp = true;
            }
            if (!Netplan && !h.Files.ContainsKey(LinuxShell.InterfacesFile))
            {
                h.Files[LinuxShell.InterfacesFile] = "# The network interfaces of this computer. After a change: sudo systemctl restart networking\n\nauto lo\niface lo inet loopback\n\nauto " + ifn + "\niface " + ifn + " inet dhcp\n";
                foreach (var f in h.Adapters) if (!f.Port.IsWireless && f.Ip == 0) f.Dhcp = true;
            }
            if (!Netplan && !h.Files.ContainsKey(LinuxShell.ResolvFile)) h.Files[LinuxShell.ResolvFile] = "# The DNS servers this computer asks for names\n";
            if (!h.Files.ContainsKey("/etc/hostname")) h.Files["/etc/hostname"] = h.ComputerName + "\n";
            if (!h.Files.ContainsKey("/etc/hosts")) h.Files["/etc/hosts"] = "127.0.0.1\tlocalhost\n127.0.1.1\t" + h.ComputerName + "\n";
            if (!h.Pages.ContainsKey("index.html")) h.Pages["index.html"] = "<h1>It's alive!</h1><p>The apache2 web server on " + h.ComputerName + " is running. Edit /var/www/html/index.html to change this page.</p>";
        }
        public override List<uint> DnsServers(Device d)
        {
            if (Netplan) return null;
            string txt;
            var l = new List<uint>();
            if (!d.Host.Files.TryGetValue(LinuxShell.ResolvFile, out txt)) return l;
            foreach (var line in txt.Split('\n'))
            {
                var w = line.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                uint ip;
                if (w.Length >= 2 && w[0] == "nameserver" && Ip.TryParse(w[1], out ip)) l.Add(ip);
            }
            return l;
        }
    }

    public class MacPersonality : Personality
    {
        public MacPersonality() { Id = "macos"; Title = "macOS-like"; Family = "mac"; Kinds = new[] { "laptop", "pc" }; Blurb = "A pretend macOS-style laptop: System Settings > Network, the Wi-Fi menu, and Terminal with ifconfig and networksetup."; }
        public override string IfName(Port p) { return p.IsWireless ? "en0" : "en1"; }
        public override Shell NewShell(Device d) { return new MacShell(d); }
        public override bool Llmnr { get { return false; } }
        public override void Apply(Device d)
        {
            var h = d.Host;
            if (h.ComputerName.Length == 0) h.ComputerName = d.Name.Replace(" ", "-") + "s-MacBook";
            foreach (var f in h.Adapters) if (!f.Dhcp && f.Ip == 0) f.Dhcp = true;
        }
    }

    public class PhonePersonality : Personality
    {
        public PhonePersonality(string id, string title) { Id = id; Title = title; Family = "phone"; Kinds = new[] { "phone", "tablet" }; Blurb = "A pretend " + (id == "android" ? "Android" : "iPhone") + "-style phone: Wi-Fi settings with a list of networks, a static IP option, and a browser."; }
        public override string IfName(Port p) { return Id == "android" ? "wlan0" : "en0"; }
        public override Shell NewShell(Device d) { return null; }
        public override bool Llmnr { get { return false; } }
        public override void Apply(Device d) { foreach (var f in d.Host.Adapters) if (!f.Dhcp && f.Ip == 0) f.Dhcp = true; if (d.Host.ComputerName.Length == 0) d.Host.ComputerName = d.Name; }
    }

    public class PrinterPersonality : Personality
    {
        public PrinterPersonality(string id, string title) { Id = id; Title = title; Family = "printer"; Kinds = new[] { "printer" }; Blurb = "A made-up network printer with a front-panel menu and its own web page."; }
        public override string IfName(Port p) { return "Ethernet"; }
        public override Shell NewShell(Device d) { return null; }
        public override bool Llmnr { get { return true; } }
        public override void Apply(Device d)
        {
            var h = d.Host;
            h.PrintOn = true; h.HttpOn = true;
            if (h.ComputerName.Length == 0) h.ComputerName = (Id == "printer-b" ? "PJ400-" : "LL200-") + WindowsPersonality.RandomName(d, 4);
            foreach (var f in h.Adapters) if (!f.Dhcp && f.Ip == 0) f.Dhcp = true;
            if (!h.State.ContainsKey("toner")) h.State["toner"] = 80.0;
        }
        public override TextMsg HttpRequest(Device d, string method, string path, TextMsg req)
        {
            var h = d.Host;
            var f = h.Main;
            string msg = "";
            if (method == "POST" && path.StartsWith("/network"))
            {
                string mode, ip, mask, gw, dns;
                req.Fields.TryGetValue("mode", out mode); req.Fields.TryGetValue("ip", out ip); req.Fields.TryGetValue("mask", out mask); req.Fields.TryGetValue("gateway", out gw); req.Fields.TryGetValue("dns", out dns);
                if (mode == "dhcp") { h.SetDhcp(f); msg = "<p><b>Saved.</b> The printer now asks DHCP for its address.</p>"; }
                else
                {
                    string err = h.SetStatic(f, ip ?? "", mask ?? "", gw ?? "", dns ?? "", "");
                    msg = err.Length > 0 ? "<p><b>Not saved:</b> " + err + "</p>" : "<p><b>Saved.</b> The printer's address is now " + ip + ". (If you changed it, open the new address in the browser.)</p>";
                }
            }
            var sb = new StringBuilder();
            sb.Append("<h1>" + d.Model + " - " + h.HostName + "</h1>");
            sb.Append(msg);
            sb.Append("<h2>Status</h2><p>Ready. Toner " + h.State.Num("toner", 80) + "%. Pages printed: " + h.Printed.Count + ".</p>");
            sb.Append("<h2>Network settings</h2><form action=\"/network\" method=\"post\">");
            sb.Append("<p>How it gets an address: <input name=\"mode\" value=\"" + (f.Dhcp ? "dhcp" : "manual") + "\"> (dhcp or manual)</p>");
            sb.Append("<p>IP address: <input name=\"ip\" value=\"" + (f.Ip != 0 ? Ip.Str(f.Ip) : "") + "\"></p>");
            sb.Append("<p>Subnet mask: <input name=\"mask\" value=\"" + (f.Mask != 0 ? Ip.Str(f.Mask) : "") + "\"></p>");
            sb.Append("<p>Gateway: <input name=\"gateway\" value=\"" + (f.Gateway != 0 ? Ip.Str(f.Gateway) : "") + "\"></p>");
            sb.Append("<p>DNS server: <input name=\"dns\" value=\"" + (f.Dns.Count > 0 ? Ip.Str(f.Dns[0]) : "") + "\"></p>");
            sb.Append("<p><input type=\"submit\" value=\"Save\"></p></form>");
            return new TextMsg { Proto = "HTTP", Line = "200 OK", Body = sb.ToString() };
        }
    }

    public class AppliancePersonality : Personality
    {
        public AppliancePersonality(string id, string title, string kind) { Id = id; Title = title; Family = "appliance"; Kinds = new[] { kind }; }
        public override Shell NewShell(Device d) { return null; }
        public override string IfName(Port p) { return p.IsWireless ? "Wi-Fi" : "Ethernet"; }
        public override void Apply(Device d)
        {
            var h = d.Host;
            foreach (var f in h.Adapters) if (!f.Dhcp && f.Ip == 0) f.Dhcp = true;
            if (h.ComputerName.Length == 0) h.ComputerName = d.Name;
            if (Id == "nas")
            {
                h.SmbOn = true; h.HttpOn = true;
                if (h.Shares.Count == 0)
                {
                    h.Shares.Add(new SharedFolder { Name = "Family", Files = new List<string> { "Holiday photos", "Recipes.docx", "Homework" } });
                    h.Shares.Add(new SharedFolder { Name = "Backups", Files = new List<string> { "Laptop backup (Sunday)" } });
                }
            }
            if (Id == "camera") h.HttpOn = true;
        }
        public override TextMsg HttpRequest(Device d, string method, string path, TextMsg req)
        {
            if (Id == "camera") return new TextMsg { Proto = "HTTP", Line = "200 OK", Body = "<h1>Camera " + d.Name + "</h1><p>Live view: the front door. Nobody there right now.</p><p>Recording: on. Motion alerts: 2 today (a cat, twice).</p>" };
            if (Id == "nas") return new TextMsg { Proto = "HTTP", Line = "200 OK", Body = "<h1>" + d.Name + " storage</h1><p>Shared folders: " + string.Join(", ", d.Host.Shares.Select(s => s.Name).ToArray()) + ".</p><p>Open them from a PC as \\\\" + d.Host.HostName + "\\" + (d.Host.Shares.Count > 0 ? d.Host.Shares[0].Name : "share") + "</p>" };
            return null;
        }
    }

    public static class Personalities
    {
        public static readonly List<Personality> All = new List<Personality>
        {
            new Personality { Id = "generic", Title = "Simple desktop", Family = "generic", Kinds = new[] { "pc", "laptop", "server" }, Blurb = "A plain desktop like the practice software uses: IP configuration, a command prompt and a web browser." },
            new WindowsPersonality("win10", "Windows 10-like"),
            new WindowsPersonality("win11", "Windows 11-like"),
            new LinuxPersonality("ubuntu", "Ubuntu-like desktop", true, true),
            new LinuxPersonality("ubuntu-server", "Ubuntu-like server", false, true),
            new LinuxPersonality("debian", "Debian-like server", false, false),
            new MacPersonality(),
            new PhonePersonality("android", "Android-like"),
            new PhonePersonality("iphone", "iPhone-like"),
            new PrinterPersonality("printer-a", "LaserLeaf 200"),
            new PrinterPersonality("printer-b", "PageJet 400"),
            new AppliancePersonality("nas", "File server (NAS)", "nas"),
            new AppliancePersonality("camera", "IP camera", "camera"),
            new AppliancePersonality("tv", "Smart TV", "tv"),
            new AppliancePersonality("console", "Game console", "console"),
        };

        public static Personality Get(string id)
        {
            foreach (var p in All) if (p.Id == id) return p;
            return All[0];
        }

        public static string Title(string id) { return Get(id).Title; }

        public static IEnumerable<Personality> For(string kind) { return All.Where(p => p.Kinds.Contains(kind)); }

        public static void Apply(Device d)
        {
            if (d.Kind == "nas" || d.Kind == "camera" || d.Kind == "tv" || d.Kind == "console") d.Os = d.Kind;
            var p = Get(d.Os);
            d.Ip.DefaultTtl = p.Ttl;
            p.Apply(d);
        }

        /// <summary>Gives a device another operating system (its address settings stay).</summary>
        public static void Change(Device d, string os)
        {
            d.Os = os;
            d.Host.WinFirewall = false;
            Apply(d);
            foreach (var f in d.Host.Adapters) if (f.Dhcp) d.Host.EnsureDhcp(f);
            d.P.Touch();
        }
    }
}
