// Network Lab - what travels on the simulated cables: Ethernet frames carrying ARP or IPv4, and inside IPv4: ICMP,
// UDP (DHCP, DNS, RIP, discovery) and TCP (HTTP, file sharing, printing, SSH). Nothing here ever reaches a real network.
using System;
using System.Collections.Generic;
using System.Text;

namespace NetworkLab
{
    public class Frame
    {
        public string Src = "", Dst = "";
        public int Vlan;              // 0 = no 802.1Q tag
        public string EtherType = "IPv4";
        public ArpPkt Arp;
        public IpPkt Ip;
        public int L2Hops;            // safety net against loops (spanning tree should stop them first)
        public int Id;

        public Frame Clone()
        {
            var f = (Frame)MemberwiseClone();
            if (Arp != null) f.Arp = Arp.Clone();
            if (Ip != null) f.Ip = Ip.Clone();
            return f;
        }

        /// <summary>The name learners know it by: ARP, ICMP, DHCP, DNS, HTTP, RIP, TCP, UDP, SMB, PRINT, SSH, LLMNR...</summary>
        public string Protocol
        {
            get
            {
                if (Arp != null) return "ARP";
                if (Ip == null) return EtherType;
                return Ip.Protocol;
            }
        }

        public string Summary()
        {
            if (Arp != null) return Arp.Op == 1 ? "ARP: who has " + NetworkLab.Ip.Str(Arp.TargetIp) + "? Tell " + NetworkLab.Ip.Str(Arp.SenderIp) : "ARP: " + NetworkLab.Ip.Str(Arp.SenderIp) + " is at " + Arp.SenderMac;
            if (Ip != null) return Ip.Summary();
            return EtherType;
        }

        /// <summary>The layers, outside in, for the "look inside the envelope" panel: title then lines.</summary>
        public List<string[]> Layers()
        {
            var L = new List<string[]>();
            var eth = new List<string>();
            eth.Add("Destination MAC: " + Dst + (Dst == Mac.Broadcast ? "  (broadcast: everyone on this network)" : ""));
            eth.Add("Source MAC: " + Src);
            if (Vlan != 0) eth.Add("802.1Q tag: VLAN " + Vlan + "  (added on a trunk so the other switch knows the VLAN)");
            eth.Add("Type: " + (Arp != null ? "ARP (0x0806)" : "IPv4 (0x0800)"));
            L.Add(new[] { "Layer 2: Ethernet" + (Vlan != 0 ? " + 802.1Q" : "") }.Concat(eth));
            if (Arp != null)
            {
                L.Add(new[] { "ARP " + (Arp.Op == 1 ? "request" : "reply") }.Concat(new List<string> {
                    "Sender: " + Arp.SenderMac + "  " + NetworkLab.Ip.Str(Arp.SenderIp),
                    "Target: " + (Arp.Op == 1 ? "(unknown)" : Arp.TargetMac) + "  " + NetworkLab.Ip.Str(Arp.TargetIp),
                    Arp.Op == 1 ? "Asks: which device has this IP address? Please send me your MAC address." : "Answers: this IP address belongs to my MAC address." }));
                return L;
            }
            if (Ip == null) return L;
            L.Add(new[] { "Layer 3: IPv4" }.Concat(new List<string> {
                "Source IP: " + NetworkLab.Ip.Str(Ip.Src), "Destination IP: " + NetworkLab.Ip.Str(Ip.Dst),
                "TTL: " + Ip.Ttl + "  (each router takes one off; at 0 the packet is thrown away)",
                "Protocol: " + Ip.Proto }));
            if (Ip.Icmp != null)
            {
                var ic = Ip.Icmp;
                var lines = new List<string> { "Type " + ic.Type + " code " + ic.Code + ": " + ic.Describe() };
                if (ic.Type == 0 || ic.Type == 8) lines.Add("Identifier " + ic.Ident + ", sequence " + ic.Seq);
                if (ic.Original != null) lines.Add("About the packet from " + NetworkLab.Ip.Str(ic.Original.Src) + " to " + NetworkLab.Ip.Str(ic.Original.Dst));
                L.Add(new[] { "Layer 3: ICMP" }.Concat(lines));
            }
            if (Ip.Seg != null)
            {
                var s = Ip.Seg;
                var lines = new List<string> { "Source port: " + s.SrcPort, "Destination port: " + s.DstPort + Ports.Describe(s.DstPort, s.SrcPort) };
                if (Ip.Proto == "TCP") lines.Add("Flags: " + s.Flags + "  " + Tcp.Explain(s.Flags));
                L.Add(new[] { "Layer 4: " + Ip.Proto }.Concat(lines));
                if (s.App != null) L.Add(new[] { "Layer 7: " + s.App.Name }.Concat(s.App.Describe()));
            }
            return L;
        }
    }

    static class ArrExt
    {
        public static string[] Concat(this string[] head, List<string> rest)
        {
            var r = new string[head.Length + rest.Count];
            head.CopyTo(r, 0);
            rest.CopyTo(r, head.Length);
            return r;
        }
    }

    public class ArpPkt
    {
        public int Op;   // 1 request, 2 reply
        public string SenderMac = "", TargetMac = "0000.0000.0000";
        public uint SenderIp, TargetIp;
        public ArpPkt Clone() { return (ArpPkt)MemberwiseClone(); }
    }

    public class IpPkt
    {
        public uint Src, Dst;
        public int Ttl = 128;
        public string Proto = "ICMP";   // ICMP, UDP, TCP
        public IcmpPkt Icmp;
        public Seg Seg;
        public IpPkt Clone()
        {
            var p = (IpPkt)MemberwiseClone();
            if (Icmp != null) p.Icmp = Icmp.Clone();
            if (Seg != null) p.Seg = Seg.Clone();
            return p;
        }
        public string Protocol
        {
            get
            {
                if (Icmp != null) return "ICMP";
                if (Seg != null)
                {
                    if (Seg.App != null) return Seg.App.Name;
                    string n = Ports.Name(Seg.DstPort, Seg.SrcPort);
                    if (n.Length > 0) return n;
                }
                return Proto;
            }
        }
        public string Summary()
        {
            string a = NetworkLab.Ip.Str(Src) + " -> " + NetworkLab.Ip.Str(Dst);
            if (Icmp != null) return "ICMP " + Icmp.Describe() + ", " + a;
            if (Seg != null)
            {
                string app = Seg.App != null ? Seg.App.Brief() : Protocol;
                return app + ", " + a + (Proto == "TCP" && Seg.App == null ? " [" + Seg.Flags + "]" : "");
            }
            return Proto + ", " + a;
        }
    }

    public class IcmpPkt
    {
        public int Type, Code, Ident, Seq;
        public IpPkt Original;   // for errors: the packet it is about
        public IcmpPkt Clone() { var c = (IcmpPkt)MemberwiseClone(); if (Original != null) c.Original = Original.Clone(); return c; }
        public string Describe()
        {
            switch (Type)
            {
                case 8: return "echo request (ping)";
                case 0: return "echo reply (ping answer)";
                case 11: return "time exceeded (TTL ran out)";
                case 3:
                    switch (Code)
                    {
                        case 0: return "destination network unreachable";
                        case 1: return "destination host unreachable";
                        case 3: return "port unreachable";
                        case 13: return "administratively prohibited (blocked by an access list)";
                        default: return "destination unreachable";
                    }
                default: return "type " + Type;
            }
        }
    }

    public class Seg
    {
        public int SrcPort, DstPort;
        public string Flags = "";   // TCP: SYN, SYN+ACK, ACK, PSH+ACK, RST, FIN
        public AppMsg App;
        public Seg Clone() { var s = (Seg)MemberwiseClone(); if (App != null) s.App = App.Copy(); return s; }
    }

    public static class Tcp
    {
        public static string Explain(string flags)
        {
            switch (flags)
            {
                case "SYN": return "(may I connect?)";
                case "SYN+ACK": return "(yes, you may)";
                case "ACK": return "(got it)";
                case "PSH+ACK": return "(here is some data)";
                case "RST": return "(no: nothing is listening on that port, or it was refused)";
                case "FIN": return "(I am done)";
            }
            return "";
        }
    }

    public static class Ports
    {
        public static string Name(int dst, int src)
        {
            string n = One(dst);
            if (n.Length == 0) n = One(src);
            return n;
        }
        static string One(int p)
        {
            switch (p)
            {
                case 67: case 68: return "DHCP";
                case 53: return "DNS";
                case 80: return "HTTP";
                case 520: return "RIP";
                case 445: return "SMB";
                case 9100: return "PRINT";
                case 22: return "SSH";
                case 5355: return "LLMNR";
                case 5353: return "MDNS";
            }
            return "";
        }
        public static string Describe(int dst, int src)
        {
            string n = Name(dst, src);
            switch (n)
            {
                case "DHCP": return "  (DHCP: handing out addresses)";
                case "DNS": return "  (DNS: names to addresses)";
                case "HTTP": return "  (HTTP: web pages)";
                case "RIP": return "  (RIP: routers sharing routes)";
                case "SMB": return "  (SMB: shared folders)";
                case "PRINT": return "  (printing)";
                case "SSH": return "  (SSH: a remote command line)";
                case "LLMNR": return "  (asking the local network for a name)";
                case "MDNS": return "  (discovering printers and devices nearby)";
            }
            return "";
        }
        /// <summary>A well-known port by number or the IOS name (www, domain, ...); -1 if unknown.</summary>
        public static int Parse(string s)
        {
            int n;
            if (int.TryParse(s, out n) && n >= 0 && n <= 65535) return n;
            switch (s.ToLowerInvariant())
            {
                case "www": case "http": return 80;
                case "domain": return 53;
                case "bootps": return 67;
                case "bootpc": return 68;
                case "rip": return 520;
                case "telnet": return 23;
                case "ftp": return 21;
                case "smtp": return 25;
                case "pop3": return 110;
                case "ssh": case "22": return 22;
                case "https": case "443": return 443;
            }
            return -1;
        }
        public static string IosName(int p)
        {
            switch (p) { case 80: return "www"; case 53: return "domain"; case 67: return "bootps"; case 68: return "bootpc"; case 23: return "telnet"; case 21: return "ftp"; case 25: return "smtp"; case 110: return "pop3"; case 22: return "22"; }
            return p.ToString();
        }
    }

    /// <summary>An application message (the top layer).</summary>
    public abstract class AppMsg
    {
        public abstract string Name { get; }
        public abstract List<string> Describe();
        public virtual string Brief() { return Name; }
        public AppMsg Copy() { return (AppMsg)MemberwiseClone(); }
    }

    public class DhcpMsg : AppMsg
    {
        public string Type = "DISCOVER";   // DISCOVER, OFFER, REQUEST, ACK, NAK, RELEASE
        public int Xid;
        public string ClientMac = "";
        public uint Yiaddr, Giaddr, ServerId, Requested, Mask, Router;
        public List<uint> Dns = new List<uint>();
        public string Domain = "";
        public override string Name { get { return "DHCP"; } }
        public override string Brief() { return "DHCP " + Type; }
        public override List<string> Describe()
        {
            var l = new List<string> { "Message: " + Type + "  " + Explain(), "Client hardware address: " + ClientMac, "Transaction ID: 0x" + Xid.ToString("X8") };
            if (Yiaddr != 0) l.Add("Your address: " + Ip.Str(Yiaddr) + (Mask != 0 ? " / " + Ip.Str(Mask) : ""));
            if (Router != 0) l.Add("Default gateway: " + Ip.Str(Router));
            if (Dns.Count > 0) { var s = new List<string>(); foreach (var d in Dns) s.Add(Ip.Str(d)); l.Add("DNS server: " + string.Join(", ", s.ToArray())); }
            if (Giaddr != 0) l.Add("Relay agent (giaddr): " + Ip.Str(Giaddr) + "  (a router passed this on with ip helper-address)");
            if (ServerId != 0) l.Add("DHCP server: " + Ip.Str(ServerId));
            return l;
        }
        string Explain()
        {
            switch (Type)
            {
                case "DISCOVER": return "(is there a DHCP server? I need an address)";
                case "OFFER": return "(you can have this address)";
                case "REQUEST": return "(I'd like that address, please)";
                case "ACK": return "(it's yours)";
                case "NAK": return "(no, start again)";
                case "RELEASE": return "(I don't need my address any more)";
            }
            return "";
        }
        public DhcpMsg CloneMsg() { var m = (DhcpMsg)MemberwiseClone(); m.Dns = new List<uint>(Dns); return m; }
    }

    public class DnsMsg : AppMsg
    {
        public bool Response;
        public int Id;
        public string QName = "";
        public uint Answer;
        public string Error = "";   // "", NXDOMAIN, REFUSED
        public override string Name { get { return "DNS"; } }
        public override string Brief() { return Response ? "DNS answer " + QName + (Answer != 0 ? " = " + Ip.Str(Answer) : " (" + Error + ")") : "DNS query " + QName; }
        public override List<string> Describe()
        {
            var l = new List<string> { (Response ? "Answer" : "Question") + " (id " + Id + ")", "Name: " + QName + ", type A (IPv4 address)" };
            if (Response) l.Add(Answer != 0 ? "Address: " + Ip.Str(Answer) : "No such name (" + Error + ")");
            return l;
        }
    }

    public class RipEntry { public uint Net, Mask, NextHop; public int Metric; }
    public class RipMsg : AppMsg
    {
        public int Command = 2;   // 1 request, 2 response
        public int Version = 2;
        public List<RipEntry> Entries = new List<RipEntry>();
        public override string Name { get { return "RIP"; } }
        public override string Brief() { return "RIPv" + Version + " " + (Command == 1 ? "request" : "update, " + Entries.Count + " route" + (Entries.Count == 1 ? "" : "s")); }
        public override List<string> Describe()
        {
            var l = new List<string> { "RIP version " + Version + (Command == 1 ? " request (please send me your routes)" : " update (these are the networks I can reach)") };
            foreach (var e in Entries) l.Add(Ip.Cidr(e.Net, e.Mask) + "  metric " + e.Metric + (e.Metric >= 16 ? " (unreachable)" : " hop" + (e.Metric == 1 ? "" : "s")));
            return l;
        }
    }

    /// <summary>Text-based messages: HTTP, SMB, printing, SSH, name and printer discovery.</summary>
    public class TextMsg : AppMsg
    {
        public string Proto = "HTTP";
        public string Line = "";       // first line: "GET /index.html", "200 OK", "LIST share"...
        public string Body = "";
        public Dictionary<string, string> Fields = new Dictionary<string, string>();
        public override string Name { get { return Proto; } }
        public override string Brief() { return Proto + " " + Line; }
        public override List<string> Describe()
        {
            var l = new List<string> { Line };
            foreach (var kv in Fields) l.Add(kv.Key + ": " + kv.Value);
            if (Body.Length > 0) l.Add(Body.Length > 200 ? Body.Substring(0, 200) + "..." : Body);
            return l;
        }
        public TextMsg CloneMsg() { var m = (TextMsg)MemberwiseClone(); m.Fields = new Dictionary<string, string>(Fields); return m; }
    }
}
