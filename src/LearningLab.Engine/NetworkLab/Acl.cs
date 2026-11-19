// Network Lab - access lists (standard 1-99 and named, extended 100-199 and named) and NAT / PAT.
using System;
using System.Collections.Generic;
using System.Linq;

namespace NetworkLab
{
    public class Ace
    {
        public int Seq;
        public bool Permit;
        public string Remark;           // a remark line (no match)
        public string Proto = "ip";     // ip, tcp, udp, icmp
        public uint Src, SrcWild, Dst, DstWild;
        public bool SrcAny, DstAny;
        public string SrcOp = "", DstOp = "";   // eq, neq, gt, lt, range
        public int SrcPort1, SrcPort2, DstPort1, DstPort2;
        public string IcmpType = "";    // echo, echo-reply, ...
        public long Matches;
        public bool Extended;

        public bool Match(IpPkt p)
        {
            if (Remark != null) return false;
            if (!SrcAny && (p.Src & ~SrcWild) != (Src & ~SrcWild)) return false;
            if (!Extended) return true;
            if (!DstAny && (p.Dst & ~DstWild) != (Dst & ~DstWild)) return false;
            switch (Proto)
            {
                case "ip": return true;
                case "icmp":
                    if (p.Icmp == null) return false;
                    if (IcmpType == "echo") return p.Icmp.Type == 8;
                    if (IcmpType == "echo-reply") return p.Icmp.Type == 0;
                    if (IcmpType == "unreachable") return p.Icmp.Type == 3;
                    if (IcmpType == "time-exceeded") return p.Icmp.Type == 11;
                    return true;
                case "tcp": case "udp":
                    if (p.Proto != Proto.ToUpperInvariant() || p.Seg == null) return false;
                    return PortOk(SrcOp, SrcPort1, SrcPort2, p.Seg.SrcPort) && PortOk(DstOp, DstPort1, DstPort2, p.Seg.DstPort);
            }
            return false;
        }

        static bool PortOk(string op, int a, int b, int v)
        {
            switch (op)
            {
                case "": return true;
                case "eq": return v == a;
                case "neq": return v != a;
                case "gt": return v > a;
                case "lt": return v < a;
                case "range": return v >= a && v <= b;
            }
            return true;
        }

        static string Addr(bool any, uint ip, uint wild)
        {
            if (any) return "any";
            if (wild == 0) return "host " + Ip.Str(ip);
            return Ip.Str(ip) + " " + Ip.Str(wild);
        }
        static string PortText(string op, int a, int b)
        {
            if (op == "") return "";
            if (op == "range") return " range " + Ports.IosName(a) + " " + Ports.IosName(b);
            return " " + op + " " + Ports.IosName(a);
        }

        /// <summary>The line as it is typed (without the list name).</summary>
        public string Text()
        {
            if (Remark != null) return "remark " + Remark;
            string s = (Permit ? "permit " : "deny ");
            if (!Extended) return s + (SrcAny ? "any" : SrcWild == 0 ? Ip.Str(Src) : Ip.Str(Src) + " " + Ip.Str(SrcWild));
            s += Proto + " " + Addr(SrcAny, Src, SrcWild) + PortText(SrcOp, SrcPort1, SrcPort2) + " " + Addr(DstAny, Dst, DstWild) + PortText(DstOp, DstPort1, DstPort2);
            if (IcmpType.Length > 0) s += " " + IcmpType;
            return s;
        }
        /// <summary>How show access-lists prints it (standard host lines say "host").</summary>
        public string ShowText()
        {
            if (Remark != null) return "remark " + Remark;
            if (!Extended) return (Permit ? "permit " : "deny ") + (SrcAny ? "any" : SrcWild == 0 ? "host " + Ip.Str(Src) : Ip.Str(Src) + ", wildcard bits " + Ip.Str(SrcWild));
            return Text();
        }
    }

    public class Acl
    {
        public string Name = "";
        public bool Extended;
        public bool Named;
        public readonly List<Ace> Lines = new List<Ace>();

        public bool IsNumbered { get { int n; return int.TryParse(Name, out n); } }

        public bool Permits(IpPkt p, out Ace hit)
        {
            foreach (var a in Lines)
            {
                if (a.Match(p)) { a.Matches++; hit = a; return a.Permit; }
            }
            hit = null;
            return false;
        }

        public void Add(Ace a)
        {
            if (a.Seq == 0) a.Seq = (Lines.Count == 0 ? 0 : Lines.Max(x => x.Seq)) + 10;
            Lines.Add(a);
            Lines.Sort((x, y) => x.Seq.CompareTo(y.Seq));
        }
    }

    public class NatEntry
    {
        public string Proto = "icmp";   // icmp, tcp, udp, or "---" for a static entry
        public uint InLocal, InGlobal, OutIp;
        public int InLocalPort, InGlobalPort, OutPort;
        public long Time;
        public bool Static;
    }

    public class NatRule
    {
        public string Acl = "";        // dynamic: the access list that picks inside addresses
        public string Interface = "";  // ... translated to this interface's address
        public bool Overload;
        public string Pool = "";
        public uint StaticLocal, StaticGlobal;
        public bool IsStatic { get { return StaticLocal != 0; } }
    }

    public class NatPool { public string Name = ""; public uint Start, End, Mask; }

    public class Nat
    {
        readonly IpStack S;
        public readonly List<NatRule> Rules = new List<NatRule>();
        public readonly List<NatEntry> Table = new List<NatEntry>();
        public readonly Dictionary<string, NatPool> Pools = new Dictionary<string, NatPool>(StringComparer.OrdinalIgnoreCase);
        public long Translations, Misses;

        public Nat(IpStack s) { S = s; }
        Sim Sim { get { return S.D.P.Sim; } }

        static void Key(IpPkt p, bool src, out string proto, out int port)
        {
            if (p.Icmp != null) { proto = "icmp"; port = p.Icmp.Type == 8 || p.Icmp.Type == 0 ? p.Icmp.Ident : 0; return; }
            if (p.Seg != null) { proto = p.Proto.ToLowerInvariant(); port = src ? p.Seg.SrcPort : p.Seg.DstPort; return; }
            proto = "ip"; port = 0;
        }

        /// <summary>A packet leaving through an outside interface: swap the private source for the public one.</summary>
        public bool InsideToOutside(IpPkt p, IpIf outIf)
        {
            Expire();
            string proto; int port;
            Key(p, true, out proto, out port);
            // static first
            foreach (var r in Rules.Where(r => r.IsStatic))
            {
                if (r.StaticLocal != p.Src) continue;
                Sim.Note("NAT (static): the source " + Ip.Str(p.Src) + " becomes " + Ip.Str(r.StaticGlobal) + ".");
                Table.Add(new NatEntry { Proto = proto, InLocal = p.Src, InLocalPort = port, InGlobal = r.StaticGlobal, InGlobalPort = port, OutIp = p.Dst, OutPort = OutPortOf(p), Time = Sim.Now });
                p.Src = r.StaticGlobal;
                Translations++;
                return true;
            }
            foreach (var r in Rules.Where(r => !r.IsStatic))
            {
                Acl acl;
                if (!S.Acls.TryGetValue(r.Acl, out acl)) continue;
                Ace hit;
                if (!acl.Permits(p, out hit)) continue;
                uint global;
                if (r.Interface.Length > 0)
                {
                    var gi = S.IfByName(r.Interface);
                    if (gi == null || gi.Ip == 0) { Misses++; Sim.Note("NAT: the outside interface " + r.Interface + " has no address yet, so nothing can be translated."); return false; }
                    global = gi.Ip;
                }
                else
                {
                    NatPool pool;
                    if (!Pools.TryGetValue(r.Pool, out pool)) { Misses++; return false; }
                    global = pool.Start;
                }
                var existing = Table.FirstOrDefault(e => !e.Static && e.Proto == proto && e.InLocal == p.Src && e.InLocalPort == port && e.InGlobal == global);
                int gport = port;
                if (existing != null) { gport = existing.InGlobalPort; existing.Time = Sim.Now; }
                else
                {
                    if (r.Overload)
                    {
                        while (Table.Any(e => e.Proto == proto && e.InGlobal == global && e.InGlobalPort == gport)) gport = gport < 1024 ? 1024 : gport + 1;
                    }
                    Table.Add(new NatEntry { Proto = proto, InLocal = p.Src, InLocalPort = port, InGlobal = global, InGlobalPort = gport, OutIp = p.Dst, OutPort = OutPortOf(p), Time = Sim.Now });
                }
                Sim.Note("NAT" + (r.Overload ? " overload (PAT)" : "") + ": the private source " + Ip.Str(p.Src) + (port != 0 ? ":" + port : "") + " becomes the public " + Ip.Str(global) + (gport != 0 ? ":" + gport : "") + ". The router remembers this in its translation table.");
                p.Src = global;
                SetPort(p, true, gport);
                Translations++;
                return true;
            }
            Misses++;
            Sim.Note("NAT: no rule matches " + Ip.Str(p.Src) + ", so it goes out with its private address (and the answer won't find its way back).");
            return false;
        }

        static int OutPortOf(IpPkt p) { if (p.Icmp != null) return p.Icmp.Ident; return p.Seg != null ? p.Seg.DstPort : 0; }

        static void SetPort(IpPkt p, bool src, int port)
        {
            if (p.Icmp != null && (p.Icmp.Type == 8 || p.Icmp.Type == 0)) { p.Icmp.Ident = port; return; }
            if (p.Seg != null) { if (src) p.Seg.SrcPort = port; else p.Seg.DstPort = port; }
        }

        /// <summary>A packet arriving on an outside interface for a public address: back to the private one.</summary>
        public void OutsideToInside(IpPkt p)
        {
            Expire();
            // ICMP errors (traceroute through NAT) carry the original packet: translate both
            if (p.Icmp != null && p.Icmp.Original != null && p.Icmp.Type != 0 && p.Icmp.Type != 8)
            {
                var o = p.Icmp.Original;
                string oproto; int oport;
                Key(o, true, out oproto, out oport);
                var e = Table.FirstOrDefault(x => x.Proto == oproto && x.InGlobal == o.Src && x.InGlobalPort == oport);
                if (e != null && p.Dst == e.InGlobal)
                {
                    p.Dst = e.InLocal; o.Src = e.InLocal; SetPort(o, true, e.InLocalPort);
                    Sim.Note("NAT: this ICMP message is about a translated packet, so it goes back to " + Ip.Str(e.InLocal) + ".");
                }
                return;
            }
            string proto; int port;
            Key(p, false, out proto, out port);
            var hit = Table.FirstOrDefault(x => x.Proto == proto && x.InGlobal == p.Dst && x.InGlobalPort == port);
            if (hit != null)
            {
                Sim.Note("NAT: " + Ip.Str(p.Dst) + (port != 0 ? ":" + port : "") + " is in the translation table: the destination becomes " + Ip.Str(hit.InLocal) + (hit.InLocalPort != 0 ? ":" + hit.InLocalPort : "") + ".");
                p.Dst = hit.InLocal;
                SetPort(p, false, hit.InLocalPort);
                hit.Time = Sim.Now;
                return;
            }
            foreach (var r in Rules.Where(r => r.IsStatic))
                if (r.StaticGlobal == p.Dst) { Sim.Note("NAT (static): " + Ip.Str(p.Dst) + " becomes " + Ip.Str(r.StaticLocal) + "."); p.Dst = r.StaticLocal; return; }
        }

        void Expire()
        {
            long now = Sim.Now;
            Table.RemoveAll(e => !e.Static && now - e.Time > (e.Proto == "icmp" ? 60000 : e.Proto == "udp" ? 300000 : 86400000));
        }

        public List<NatEntry> Entries()
        {
            Expire();
            var l = new List<NatEntry>();
            foreach (var r in Rules.Where(r => r.IsStatic)) l.Add(new NatEntry { Proto = "---", InLocal = r.StaticLocal, InGlobal = r.StaticGlobal, Static = true });
            l.AddRange(Table);
            return l;
        }
    }
}
