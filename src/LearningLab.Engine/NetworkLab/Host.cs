// Network Lab - an end device (PC, laptop, phone, server, printer...): its address settings, Wi-Fi, firewall, the
// services it offers (web, DNS, DHCP, shared folders, printing, SSH) and the name resolver its apps use.
using System;
using System.Collections.Generic;
using System.Linq;

namespace NetworkLab
{
    public class WifiClient
    {
        public Port Port;
        public string Ssid = "", Key = "";
        /// <summary>off, connected, wrong-key, not-found, no-signal</summary>
        public string Status = "off";
        public string StatusText
        {
            get
            {
                switch (Status)
                {
                    case "connected": return "Connected to " + Ssid;
                    case "wrong-key": return "Can't connect to " + Ssid + ": the password (key) is wrong";
                    case "not-found": return "No network called " + Ssid + " was found";
                    case "no-signal": return Ssid + " is too far away (no signal here)";
                }
                return "Not connected";
            }
        }
    }

    public class SharedFolder { public string Name = ""; public List<string> Files = new List<string>(); public bool Everyone = true; }

    public class FwRule { public string Name = ""; public int Port; public string Proto = "tcp"; public bool Allow = true; }

    public class Host
    {
        public readonly Device D;
        public string ComputerName = "";
        public readonly List<WifiClient> Wifi = new List<WifiClient>();
        // services
        public bool HttpOn;
        public readonly SortedDictionary<string, string> Pages = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public bool SmbOn;
        public readonly List<SharedFolder> Shares = new List<SharedFolder>();
        public bool PrintOn;
        public readonly List<string> Printed = new List<string>();
        public bool SshOn;
        public string User = "student";
        public string Password = "netlab";
        // firewalls
        public bool WinFirewall;
        public bool FwSharing;        // Windows: "File and printer sharing" allowed (ping and shared folders)
        public bool FwWeb;            // Windows: web server allowed
        public bool Ufw;
        public readonly List<FwRule> UfwRules = new List<FwRule>();
        // a little file system (Linux and friends: /etc/...)
        public readonly SortedDictionary<string, string> Files = new SortedDictionary<string, string>(StringComparer.Ordinal);
        // printers this computer knows ("Printers & scanners")
        public readonly List<string[]> Printers = new List<string[]>();   // name, address
        public readonly Dictionary<string, uint> DnsCache = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        public string Extra = "";      // personality-specific settings (JSON object text)
        public JObj State = new JObj();

        public Host(Device d)
        {
            D = d;
            foreach (var p in d.Ports) if (p.IsWireless) Wifi.Add(new WifiClient { Port = p });
            var s = d.Ip;
            s.Firewall = Inbound;
            s.TcpListeners[80] = Http;
            s.TcpListeners[445] = Smb;
            s.TcpListeners[9100] = Print;
            s.TcpListeners[22] = Ssh;
            s.TcpOpen = port => (port == 80 && HttpOn) || (port == 445 && SmbOn) || (port == 9100 && PrintOn) || (port == 22 && SshOn);
            s.Udp[5355] = Llmnr;
            s.Udp[5353] = Mdns;
        }

        Sim Sim { get { return D.P.Sim; } }
        IpStack S { get { return D.Ip; } }

        public WifiClient WifiFor(Port p) { return Wifi.FirstOrDefault(w => w.Port == p); }

        public void Start()
        {
            foreach (var f in S.Ifs) if (f.Dhcp) EnsureDhcp(f);
        }

        public void EnsureDhcp(IpIf f)
        {
            if (f.DhcpC == null) f.DhcpC = new DhcpClient(S, f);
            if (f.Up) f.DhcpC.Start();
        }

        public void PortChanged(Port p, bool up) { }

        /// <summary>The adapters people see: wired first, then Wi-Fi.</summary>
        public List<IpIf> Adapters { get { return S.Ifs.Where(f => f.Port != null && !f.IsSub).ToList(); } }
        public IpIf Main
        {
            get
            {
                var a = Adapters;
                var up = a.FirstOrDefault(f => f.Up && f.Ip != 0);
                return up ?? a.FirstOrDefault(f => f.Up) ?? a.FirstOrDefault();
            }
        }

        /// <summary>Static address settings for an adapter ("" values leave a field empty).</summary>
        public string SetStatic(IpIf f, string ip, string mask, string gw, string dns1, string dns2)
        {
            uint a, m, g = 0, d1 = 0, d2 = 0;
            if (!Ip.TryParse(ip, out a)) return "The IP address isn't valid (four numbers 0-255 with dots, like 192.168.1.10).";
            if (!Ip.TryParseMask(mask, out m)) return "The subnet mask isn't valid (like 255.255.255.0, or 24).";
            if (!string.IsNullOrEmpty(gw) && !Ip.TryParse(gw, out g)) return "The default gateway isn't a valid IP address.";
            if (!string.IsNullOrEmpty(dns1) && !Ip.TryParse(dns1, out d1)) return "The DNS server isn't a valid IP address.";
            if (!string.IsNullOrEmpty(dns2) && !Ip.TryParse(dns2, out d2)) return "The second DNS server isn't a valid IP address.";
            if (m != 0 && (a & ~m) == 0) return Ip.Str(a) + " is the network's own number (all host bits 0), so a device can't use it.";
            if (m != 0 && m != 0xFFFFFFFF && (a | m) == 0xFFFFFFFF) return Ip.Str(a) + " is the broadcast address of its network, so a device can't use it.";
            if (f.DhcpC != null) f.DhcpC.Stop();
            f.Dhcp = false;
            f.Gateway = g;
            f.Dns.Clear();
            if (d1 != 0) f.Dns.Add(d1);
            if (d2 != 0) f.Dns.Add(d2);
            S.SetAddress(f, a, m);
            f.Apipa = false;
            return "";
        }

        public void SetDhcp(IpIf f)
        {
            f.Dhcp = true;
            S.SetAddress(f, 0, 0);
            f.Gateway = 0; f.Dns.Clear();
            EnsureDhcp(f);
        }

        public void SetWifi(Port p, string ssid, string key)
        {
            var w = WifiFor(p);
            if (w == null) return;
            w.Ssid = ssid ?? ""; w.Key = key ?? "";
            Sim.TopologyChanged();
        }

        public List<uint> DnsServers
        {
            get
            {
                var special = Personalities.Get(D.Os).DnsServers(D);
                if (special != null) return special;
                var l = new List<uint>();
                foreach (var f in Adapters) if (f.Up && f.Ip != 0) foreach (var d in f.Dns) if (!l.Contains(d)) l.Add(d);
                return l;
            }
        }
        public uint Gateway
        {
            get { foreach (var f in Adapters) if (f.Up && f.Ip != 0 && f.Gateway != 0) return f.Gateway; return 0; }
        }

        // ------------------------------------------------------------ firewall
        string Inbound(IpPkt p)
        {
            if (Personalities.Get(D.Os).Family == "windows" && WinFirewall)
            {
                if (p.Icmp != null && p.Icmp.Type == 8 && !FwSharing) return "Windows Defender Firewall on " + D.Name + " blocks incoming pings (echo requests), so there is no answer. (Allow 'File and printer sharing', or turn the firewall off.)";
                if (p.Seg != null && p.Proto == "TCP" && p.Seg.Flags == "SYN")
                {
                    int port = p.Seg.DstPort;
                    if (port == 445 && !FwSharing) return "Windows Defender Firewall on " + D.Name + " blocks file sharing (port 445).";
                    if (port == 80 && !FwWeb) return "Windows Defender Firewall on " + D.Name + " blocks the web server (port 80).";
                    if (port != 445 && port != 80 && port != 9100) return "Windows Defender Firewall on " + D.Name + " blocks TCP port " + port + ".";
                }
            }
            if (Ufw && p.Seg != null && (p.Proto == "TCP" ? p.Seg.Flags == "SYN" : p.Seg.DstPort < 49152 && p.Seg.DstPort != 68 && p.Seg.DstPort != 5353 && p.Seg.DstPort != 5355))
            {
                string proto = p.Proto.ToLowerInvariant();
                var rule = UfwRules.LastOrDefault(r => r.Port == p.Seg.DstPort && (r.Proto == "any" || r.Proto == proto));
                if (rule == null || !rule.Allow) return "The ufw firewall on " + D.Name + " blocks incoming " + p.Proto + " port " + p.Seg.DstPort + (rule == null ? " (only allowed ports get in)." : " (a deny rule).");
            }
            return null;
        }

        // ------------------------------------------------------------ servers
        TextMsg Http(IpPkt p, TextMsg req)
        {
            if (!HttpOn) return null;
            string path = req.Line.Length > 4 ? req.Line.Substring(req.Line.IndexOf(' ') + 1).Trim() : "/";
            string method = req.Line.Split(' ')[0];
            var custom = Personalities.Get(D.Os).HttpRequest(D, method, path, req);
            if (custom != null) { Sim.Note(D.Name + " answers the web request for " + path + "."); return custom; }
            string file = path.TrimStart('/');
            if (file.Length == 0) file = "index.html";
            string page;
            if (Pages.TryGetValue(file, out page))
            {
                Sim.Note(D.Name + "'s web server sends the page " + file + ".");
                return new TextMsg { Proto = "HTTP", Line = "200 OK", Body = page };
            }
            Sim.Note(D.Name + "'s web server has no page called " + file + " (404 Not Found).");
            return new TextMsg { Proto = "HTTP", Line = "404 Not Found", Body = "<h1>Not Found</h1><p>The page " + file + " isn't on this server.</p>" };
        }

        TextMsg Smb(IpPkt p, TextMsg req)
        {
            if (!SmbOn) return null;
            string[] w = req.Line.Split(new[] { ' ' }, 2);
            if (w[0] == "SHARES")
            {
                Sim.Note(D.Name + " lists its shared folders.");
                return new TextMsg { Proto = "SMB", Line = "OK", Body = string.Join("\n", Shares.Select(s => s.Name).ToArray()) };
            }
            if (w[0] == "LIST" && w.Length > 1)
            {
                var sh = Shares.FirstOrDefault(s => string.Equals(s.Name, w[1], StringComparison.OrdinalIgnoreCase));
                if (sh == null) { Sim.Note(D.Name + " has no shared folder called " + w[1] + "."); return new TextMsg { Proto = "SMB", Line = "NOT FOUND" }; }
                Sim.Note(D.Name + " sends the list of files in " + sh.Name + ".");
                return new TextMsg { Proto = "SMB", Line = "OK", Body = string.Join("\n", sh.Files.ToArray()) };
            }
            return new TextMsg { Proto = "SMB", Line = "BAD REQUEST" };
        }

        TextMsg Print(IpPkt p, TextMsg req)
        {
            if (!PrintOn) return null;
            string from;
            if (!req.Fields.TryGetValue("From", out from)) from = Ip.Str(p.Src);
            Printed.Add(from + ": " + req.Body);
            Sim.Note(D.Name + " prints the page from " + from + ".");
            return new TextMsg { Proto = "PRINT", Line = "PRINTED" };
        }

        TextMsg Ssh(IpPkt p, TextMsg req)
        {
            if (!SshOn) return null;
            string[] w = req.Line.Split(new[] { ' ' }, 3);
            if (w[0] == "LOGIN")
            {
                bool ok = w.Length > 2 && w[1] == User && w[2] == Password;
                Sim.Note(ok ? D.Name + " accepts the SSH login of " + w[1] + "." : D.Name + " refuses the SSH login (wrong name or password).");
                return new TextMsg { Proto = "SSH", Line = ok ? "OK" : "DENIED" };
            }
            if (w[0] == "CMD")
            {
                string cmd = req.Line.Length > 4 ? req.Line.Substring(4) : "";
                var sh = Personalities.Get(D.Os).NewShell(D);
                string output = sh != null ? sh.RunRemote(cmd) : "";
                return new TextMsg { Proto = "SSH", Line = "OUT", Body = output };
            }
            return new TextMsg { Proto = "SSH", Line = "BAD" };
        }

        void Llmnr(IpIf ifc, IpPkt p)
        {
            var q = p.Seg.App as DnsMsg;
            if (q == null || q.Response) return;
            if (!string.Equals(q.QName, HostName, StringComparison.OrdinalIgnoreCase) && !string.Equals(q.QName, D.Name, StringComparison.OrdinalIgnoreCase)) return;
            if (ifc.Ip == 0) return;
            Sim.Note(D.Name + " hears someone asking for the name " + q.QName + " and answers: that's me, " + Ip.Str(ifc.Ip) + ".");
            S.Send(new IpPkt { Src = ifc.Ip, Dst = p.Src, Proto = "UDP", Seg = new Seg { SrcPort = 5355, DstPort = p.Seg.SrcPort, App = new DnsMsg { Response = true, Id = q.Id, QName = q.QName, Answer = ifc.Ip } } }, null);
        }

        void Mdns(IpIf ifc, IpPkt p)
        {
            var q = p.Seg.App as TextMsg;
            if (q == null || q.Line != "WHO PRINTS?" || !PrintOn || ifc.Ip == 0) return;
            Sim.Note(D.Name + " hears 'who prints?' and answers with its name and address.");
            var a = new TextMsg { Proto = "MDNS", Line = "I PRINT" };
            a.Fields["Name"] = D.Name; a.Fields["Model"] = D.Model; a.Fields["Address"] = Ip.Str(ifc.Ip);
            S.Send(new IpPkt { Src = ifc.Ip, Dst = p.Src, Proto = "UDP", Seg = new Seg { SrcPort = 5353, DstPort = p.Seg.SrcPort, App = a } }, null);
        }

        public string HostName { get { return ComputerName.Length > 0 ? ComputerName : D.Name; } }
    }

    /// <summary>A Wi-Fi network offered by an access point or a wireless router.</summary>
    public class WifiAp
    {
        public readonly Device D;
        public string Ssid = "NetLab";
        public string Security = "open";    // open, wpa2
        public string Key = "";
        public int Channel = 6;
        public bool Enabled = true;
        public double Range = 35;           // metres in open space
        public WifiAp(Device d) { D = d; }
        public Port Radio { get { return D.Ports.First(p => p.IsWireless); } }
    }
}
