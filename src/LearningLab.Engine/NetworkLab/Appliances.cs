// Network Lab - the boxes that are set up with forms instead of a command line: the wireless home router (switch +
// Wi-Fi + DHCP + NAT router in one) and the Internet / ISP cloud with its pretend web sites.
using System;
using System.Collections.Generic;
using System.Linq;

namespace NetworkLab
{
    public class HomeCfg
    {
        public string WanMode = "dhcp";     // dhcp or static
        public string WanIp = "", WanMask = "", WanGw = "", WanDns = "";
        public string LanIp = "192.168.0.1", LanMask = "255.255.255.0";
        public bool DhcpOn = true;
        public string DhcpStart = "192.168.0.100";
        public int DhcpMax = 50;
        public string AdminPassword = "admin";
    }

    public static class HomeRouter
    {
        public static void Init(Device d)
        {
            d.Home = new HomeCfg();
            var lan = d.Bridge.AddSvi(1, false);
            lan.Name = "LAN";
            lan.Nat = 1;
            var wan = d.Ip.AddRoutedPort(d.Ports.First(p => p.Name == "Internet"));
            wan.Nat = 2;
            d.Ip.Dhcp = new DhcpServer(d.Ip);
            d.Ip.Udp[53] = (ifc, p) => DnsProxy(d, ifc, p);
            Apply(d);
        }

        /// <summary>Like a real home router, it answers DNS for the home network by asking the internet provider's
        /// DNS server (so the computers can use the router's own address as their DNS server).</summary>
        static void DnsProxy(Device d, IpIf ifc, IpPkt p)
        {
            var q = p.Seg.App as DnsMsg;
            if (q == null || q.Response) return;
            var s = d.Ip;
            var sim = d.P.Sim;
            var up = Upstream(d);
            Action<DnsMsg> answer = r => s.Send(new IpPkt { Src = p.Dst, Dst = p.Src, Proto = "UDP", Seg = new Seg { SrcPort = 53, DstPort = p.Seg.SrcPort, App = r } }, null);
            if (up == 0) { sim.Note(d.Name + " has no internet DNS server yet (is the Internet port connected?), so it can't answer."); answer(new DnsMsg { Response = true, Id = q.Id, QName = q.QName, Error = "REFUSED" }); return; }
            int port = s.Ephemeral();
            bool done = false;
            sim.Note(d.Name + " passes the DNS question on to the internet provider's DNS server " + Ip.Str(up) + ".");
            s.Udp[port] = (i2, r) =>
            {
                var a = r.Seg.App as DnsMsg;
                if (a == null || done) return;
                done = true; s.Udp.Remove(port);
                sim.Note(d.Name + " hands the DNS answer back to " + Ip.Str(p.Src) + ".");
                var copy = (DnsMsg)a.Copy();
                answer(copy);
            };
            s.Send(new IpPkt { Dst = up, Proto = "UDP", Seg = new Seg { SrcPort = port, DstPort = 53, App = new DnsMsg { Id = q.Id, QName = q.QName } } }, null);
            sim.After(3000, () => { if (!done) { done = true; s.Udp.Remove(port); } });
        }

        static uint Upstream(Device d)
        {
            var c = d.Home;
            uint ip;
            if (c.WanMode == "static" && Ip.TryParse(c.WanDns, out ip)) return ip;
            var w = Wan(d);
            if (w.DhcpC != null && w.DhcpC.Dns.Count > 0) return w.DhcpC.Dns[0];
            return 0;
        }

        public static IpIf Lan(Device d) { return d.Bridge.Svis[1]; }
        public static IpIf Wan(Device d) { return d.Ports.First(p => p.Name == "Internet").L3; }

        /// <summary>Turns the form settings into the router's working parts. Returns "" or a problem in plain words.</summary>
        public static string Apply(Device d)
        {
            var c = d.Home;
            var s = d.Ip;
            uint lip, lmask;
            if (!Ip.TryParse(c.LanIp, out lip) || !Ip.TryParseMask(c.LanMask, out lmask)) return "The router's own (LAN) address or mask isn't valid.";
            var lan = Lan(d);
            if (lan.Ip != lip || lan.Mask != lmask) s.SetAddress(lan, lip, lmask);
            // DHCP for the home network
            var dh = s.Dhcp;
            dh.Enabled = c.DhcpOn;
            var pool = dh.Pool("LAN", true);
            pool.Network = lip & lmask; pool.Mask = lmask; pool.Router = lip;
            uint st;
            pool.Start = Ip.TryParse(c.DhcpStart, out st) && (st & lmask) == pool.Network ? st : pool.Network + 100;
            pool.Max = Math.Max(1, c.DhcpMax);
            pool.Dns.Clear();
            pool.Dns.Add(lip);   // the router answers DNS itself (it asks the provider)
            // NAT: everything from the home network goes out as the router's internet address
            Acl acl;
            if (!s.Acls.TryGetValue("HOME", out acl)) { acl = new Acl { Name = "HOME", Named = true }; s.Acls["HOME"] = acl; }
            acl.Lines.Clear();
            acl.Add(new Ace { Permit = true, Src = pool.Network, SrcWild = ~lmask });
            s.Nat.Rules.Clear();
            s.Nat.Rules.Add(new NatRule { Acl = "HOME", Interface = "Internet", Overload = true });
            // the internet side
            var wan = Wan(d);
            if (c.WanMode == "static")
            {
                if (wan.DhcpC != null) wan.DhcpC.Stop();
                wan.Dhcp = false;
                uint wip, wm, wg = 0;
                if (!Ip.TryParse(c.WanIp, out wip) || !Ip.TryParseMask(c.WanMask, out wm)) return "The internet address or mask isn't valid.";
                Ip.TryParse(c.WanGw, out wg);
                s.SetAddress(wan, wip, wm);
                s.Static.Clear();
                if (wg != 0) s.Static.Add(new StaticRoute { Net = 0, Mask = 0, NextHop = wg });


            }
            else
            {
                s.Static.Clear();
                if (!wan.Dhcp)
                {
                    wan.Dhcp = true;
                    s.SetAddress(wan, 0, 0);
                }
                if (wan.DhcpC == null)
                {
                    wan.DhcpC = new DhcpClient(s, wan);

                }
                if (wan.Up && wan.Ip == 0) wan.DhcpC.Start();


            }
            return "";
        }
    }

    public static class Cloud
    {
        public const uint Www = 0xC6336414;      // 198.51.100.20
        public const uint Dns = 0xC6336435;      // 198.51.100.53
        public const uint News = 0xC633641E;     // 198.51.100.30
        public const uint Play = 0xC6336428;     // 198.51.100.40

        public static readonly string[][] Sites =
        {
            new[] { "www.example.com", "198.51.100.20" },
            new[] { "news.example.org", "198.51.100.30" },
            new[] { "play.example.net", "198.51.100.40" },
            new[] { "dns.example.net", "198.51.100.53" },
        };

        public static void Init(Device d)
        {
            var s = d.Ip;
            s.Dhcp = new DhcpServer(s);
            int k = 0;
            foreach (var p in d.Ports)
            {
                var f = s.AddRoutedPort(p);
                uint net = 0xCB007100u + (uint)(k * 32);     // 203.0.113.0/27, .32/27, ...
                f.Ip = net + 1; f.Mask = 0xFFFFFFE0;
                var pool = s.Dhcp.Pool("ISP" + k, true);
                pool.Network = net; pool.Mask = f.Mask; pool.Router = net + 1; pool.Start = net + 2; pool.Max = 20;
                pool.Dns.Add(Dns);
                k++;
            }
            int n = 0;
            foreach (var site in Sites)
            {
                var lo = s.AddLoopback(n++);
                lo.Ip = Ip.Parse(site[1]); lo.Mask = 0xFFFFFFFF;
            }
            s.DnsSrv = new DnsServer(s);
            foreach (var site in Sites) s.DnsSrv.Records[site[0]] = Ip.Parse(site[1]);
            s.TcpListeners[80] = (p, req) =>
            {
                string host = Ip.Str(p.Dst);
                foreach (var site in Sites) if (Ip.Parse(site[1]) == p.Dst) host = site[0];
                d.P.Sim.Note("The web site " + host + " (somewhere on the internet) sends its page.");
                return new TextMsg { Proto = "HTTP", Line = "200 OK", Body = Page(host) };
            };
            s.TcpOpen = port => port == 80;
        }

        static string Page(string host)
        {
            switch (host)
            {
                case "www.example.com":
                    return "<h1>Welcome to the internet!</h1><p>If you can read this, your network reaches the (pretend) internet: your computer found this site's address with DNS, and your router sent the request out and the answer back.</p><ul><li><a href=\"http://news.example.org/\">news.example.org</a></li><li><a href=\"http://play.example.net/\">play.example.net</a></li></ul>";
                case "news.example.org":
                    return "<h1>Network News</h1><h2>Packets delivered on time, again</h2><p>Routers everywhere report another busy day of looking things up in their routing tables.</p><p><a href=\"http://www.example.com/\">Back to the welcome page</a></p>";
                case "play.example.net":
                    return "<h1>Game Server</h1><p>Your game console or computer can reach the game server. Have fun!</p>";
            }
            return "<h1>" + host + "</h1><p>A site on the pretend internet.</p>";
        }
    }
}
