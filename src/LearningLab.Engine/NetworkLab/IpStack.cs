// Network Lab - layer 3: IPv4 interfaces, ARP, the routing table, forwarding with TTL, ICMP (echo, time exceeded,
// unreachable), access lists and NAT on the way through, and handing packets to the services on the device.
using System;
using System.Collections.Generic;
using System.Linq;

namespace NetworkLab
{
    public class IpIf
    {
        public Device Dev;
        public string Name = "";
        public Port Port;           // routed port (or the parent port of a subinterface)
        public int Vlan;            // SVI or subinterface VLAN
        public bool IsSvi, IsSub, NativeSub, IsLoopback;
        public int SubNumber;
        public Bridge Bridge;
        public string Mac = "";
        public uint Ip, Mask;
        public bool Shutdown;
        public string Description = "";
        public bool Dhcp;
        public DhcpClient DhcpC;
        public uint Gateway;                         // end devices: the default gateway set on this adapter
        public List<uint> Dns = new List<uint>();    // end devices: DNS servers set on this adapter
        public string AclIn = "", AclOut = "";
        public int Nat;                              // 0 none, 1 inside, 2 outside
        public List<uint> Helpers = new List<uint>();
        public bool Apipa;
        public bool Configured { get { return Ip != 0; } }

        public string ShortName
        {
            get
            {
                if (IsSub) return Port.Short + "." + SubNumber;
                if (IsSvi) return "Vlan" + Vlan;
                if (IsLoopback) return "Lo" + SubNumber;
                return Port != null ? Port.Short : Name;
            }
        }

        /// <summary>Line protocol up: not shut down and the cable (or VLAN, or parent) is up.</summary>
        public bool Up
        {
            get
            {
                if (Shutdown || !Dev.PoweredOn) return false;
                if (IsLoopback) return true;
                if (IsSvi) return Bridge.VlanUp(Vlan);
                if (IsSub) return Port.L3 != null && !Port.L3.Shutdown && Port.IsUp;
                return Port != null && Port.IsUp;
            }
        }
        public string Status
        {
            get
            {
                if (Shutdown) return "administratively down";
                return Up ? "up" : "down";
            }
        }
    }

    public class ArpEntry { public uint Ip; public string Mac; public IpIf If; public long Time; public bool Static; }

    public class Route
    {
        public string Code = "C";    // C, L, S, R, S* (default), D (DHCP default)
        public uint Net, Mask, NextHop;
        public IpIf If;
        public int Ad, Metric;
        public long Age;
        public bool IsDefault { get { return Mask == 0 && Net == 0; } }
    }

    public class StaticRoute
    {
        public uint Net, Mask, NextHop;
        public string ExitIf = "";
        public int Ad = 1;
        public bool Permanent;
    }

    class PendingArp { public IpIf If; public List<IpPkt> Packets = new List<IpPkt>(); public int Tries; public SimTimer Timer; }

    public class IcmpWaiter { public int Ident; public Action<IpPkt> OnReply; }

    public class IpStack
    {
        public readonly Device D;
        public readonly List<IpIf> Ifs = new List<IpIf>();
        public bool Forwarding;
        public readonly Dictionary<uint, ArpEntry> Arp = new Dictionary<uint, ArpEntry>();
        readonly Dictionary<uint, PendingArp> pending = new Dictionary<uint, PendingArp>();
        public long ArpTimeoutMs = 4 * 3600 * 1000L;
        public readonly List<StaticRoute> Static = new List<StaticRoute>();
        public uint DefaultGateway;          // switches: ip default-gateway
        public Rip Rip;
        public readonly Dictionary<string, Acl> Acls = new Dictionary<string, Acl>(StringComparer.OrdinalIgnoreCase);
        public readonly Nat Nat;
        public DhcpServer Dhcp;
        public DnsServer DnsSrv;
        public readonly Dictionary<int, Action<IpIf, IpPkt>> Udp = new Dictionary<int, Action<IpIf, IpPkt>>();
        public readonly Dictionary<int, Func<IpPkt, TextMsg, TextMsg>> TcpListeners = new Dictionary<int, Func<IpPkt, TextMsg, TextMsg>>();
        public readonly Dictionary<string, TcpConn> Conns = new Dictionary<string, TcpConn>();
        public readonly List<IcmpWaiter> Waiters = new List<IcmpWaiter>();
        /// <summary>A firewall on an end device: returns why an incoming packet is blocked, or null.</summary>
        public Func<IpPkt, string> Firewall;
        /// <summary>Is a service listening on this TCP port right now (null: every registered listener is).</summary>
        public Func<int, bool> TcpOpen;
        public int DefaultTtl = 128;
        int ephemeral = 49152;
        public int NextIdent = 1;

        public IpStack(Device d) { D = d; Nat = new Nat(this); DefaultTtl = d.Kind == "router" || d.Kind == "switch" || d.Kind == "cloud" ? 255 : 128; }

        Sim S { get { return D.P.Sim; } }

        public IpIf AddRoutedPort(Port p)
        {
            var f = new IpIf { Dev = D, Name = p.Name, Port = p, Mac = p.Mac };
            p.L3 = f;
            Ifs.Add(f);
            return f;
        }

        public IpIf AddSub(Port p, int n)
        {
            var f = Ifs.FirstOrDefault(x => x.IsSub && x.Port == p && x.SubNumber == n);
            if (f != null) return f;
            f = new IpIf { Dev = D, Name = p.Name + "." + n, Port = p, IsSub = true, SubNumber = n, Mac = p.Mac };
            Ifs.Add(f);
            return f;
        }

        public IpIf AddLoopback(int n)
        {
            var f = Ifs.FirstOrDefault(x => x.IsLoopback && x.SubNumber == n);
            if (f != null) return f;
            f = new IpIf { Dev = D, Name = "Loopback" + n, IsLoopback = true, SubNumber = n, Mac = "0000.0000.0000" };
            Ifs.Add(f);
            return f;
        }

        public IpIf IfByName(string name)
        {
            foreach (var f in Ifs) if (string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase) || string.Equals(f.ShortName, name, StringComparison.OrdinalIgnoreCase)) return f;
            return null;
        }

        public bool IsMine(uint ip)
        {
            if (ip == 0) return false;
            foreach (var f in Ifs) if (f.Ip == ip && f.Up) return true;
            return false;
        }
        public IpIf IfWithIp(uint ip) { foreach (var f in Ifs) if (f.Ip == ip) return f; return null; }

        public void Start() { }

        // ------------------------------------------------------------ routing table
        public List<Route> Routes()
        {
            var r = new List<Route>();
            foreach (var f in Ifs)
            {
                if (!f.Up || f.Ip == 0) continue;
                r.Add(new Route { Code = "C", Net = f.Ip & f.Mask, Mask = f.Mask, If = f, Ad = 0 });
                if (Forwarding && f.Mask != 0xFFFFFFFF) r.Add(new Route { Code = "L", Net = f.Ip, Mask = 0xFFFFFFFF, If = f, Ad = 0 });
            }
            if (!Forwarding)
            {
                // an end device (or a switch): the default gateway
                uint gw = DefaultGateway;
                IpIf gwIf = null;
                if (gw == 0) foreach (var f in Ifs) if (f.Up && f.Gateway != 0 && f.Ip != 0) { gw = f.Gateway; gwIf = f; break; }
                if (gw != 0)
                {
                    var c = r.FirstOrDefault(x => x.Code == "C" && Ip.SameNet(gw, x.Net, x.Mask) && (gwIf == null || x.If == gwIf));
                    if (c != null) r.Add(new Route { Code = "S*", Net = 0, Mask = 0, NextHop = gw, If = c.If, Ad = 1 });
                }
                return r;
            }
            foreach (var s in Static)
            {
                IpIf outIf = null;
                if (s.ExitIf.Length > 0) { outIf = IfByName(s.ExitIf); if (outIf == null || !outIf.Up) continue; }
                else
                {
                    var c = Lookup(r, s.NextHop, 0);
                    if (c == null) continue;
                    outIf = c.If;
                }
                if (r.Any(x => x.Net == s.Net && x.Mask == s.Mask && x.Ad < s.Ad)) continue;
                r.Add(new Route { Code = s.Mask == 0 ? "S*" : "S", Net = s.Net, Mask = s.Mask, NextHop = s.NextHop, If = outIf, Ad = s.Ad });
            }
            foreach (var f in Ifs)
                if (f.Dhcp && f.DhcpC != null && f.DhcpC.Router != 0 && f.Up && f.Ip != 0 && !r.Any(x => x.IsDefault && x.Ad <= 254))
                    r.Add(new Route { Code = "S*", Net = 0, Mask = 0, NextHop = f.DhcpC.Router, If = f, Ad = 254 });
            if (Rip != null)
                foreach (var rr in Rip.Learned.Values)
                {
                    if (rr.Metric >= 16 || !rr.If.Up) continue;
                    var same = r.FirstOrDefault(x => x.Net == rr.Net && x.Mask == rr.Mask);
                    if (same != null && same.Ad <= 120) continue;
                    if (same != null) r.Remove(same);
                    r.Add(new Route { Code = rr.Net == 0 && rr.Mask == 0 ? "R*" : "R", Net = rr.Net, Mask = rr.Mask, NextHop = rr.NextHop, If = rr.If, Ad = 120, Metric = rr.Metric, Age = rr.Heard });
                }
            return r;
        }

        static Route Lookup(List<Route> table, uint dst, int depth)
        {
            Route best = null;
            foreach (var x in table)
            {
                if ((dst & x.Mask) != x.Net) continue;
                if (best == null || x.Mask > best.Mask || (x.Mask == best.Mask && x.Ad < best.Ad)) best = x;
            }
            return best;
        }

        /// <summary>Where a packet to this address goes: the route, the interface and the next hop (or null).</summary>
        public Route Lookup(uint dst, out uint nextHop)
        {
            var t = Routes();
            var r = Lookup(t, dst, 0);
            nextHop = 0;
            if (r == null) return null;
            nextHop = r.NextHop != 0 ? r.NextHop : dst;
            if (r.Code == "L") nextHop = dst;
            return r;
        }

        // ------------------------------------------------------------ sending
        /// <summary>Sends a packet this device makes itself. Returns "" or why it can't be sent.</summary>
        public string Send(IpPkt p, IpIf via)
        {
            if (p.Ttl == 0) p.Ttl = DefaultTtl;
            if (p.Dst == Ip.Broadcast || Ip.IsMulticast(p.Dst))
            {
                if (via == null) return "no interface";
                if (p.Src == 0 && via.Ip != 0 && !(p.Seg != null && p.Seg.App is DhcpMsg)) p.Src = via.Ip;
                var f = new Frame { Dst = p.Dst == Ip.Broadcast ? Mac.Broadcast : Mac.ForIp(p.Dst), Ip = p };
                SendFrame(via, f);
                return "";
            }
            if (IsMine(p.Dst))
            {
                var me = IfWithIp(p.Dst);
                if (p.Src == 0) p.Src = p.Dst;
                S.After(0, () => LocalDeliver(me, p));
                return "";
            }
            uint nh;
            var r = via != null && !Forwarding ? null : Lookup(p.Dst, out nh);
            if (via != null && r == null) { nh = p.Dst; r = new Route { If = via }; }
            else if (r == null)
            {
                if (!Forwarding && !Ifs.Any(f => f.Up && f.Ip != 0)) return "no address";
                return "no route";
            }
            else nh = r.NextHop != 0 && r.Code != "L" && r.Code != "C" ? r.NextHop : p.Dst;
            if (p.Src == 0) p.Src = r.If.Ip;
            Deliver(r.If, nh, p, false);
            return "";
        }

        void Deliver(IpIf outIf, uint nextHop, IpPkt p, bool forwarded)
        {
            if (outIf.IsLoopback) { LocalDeliver(outIf, p); return; }
            if (p.Dst == Ip.BroadcastOf(outIf.Ip, outIf.Mask) && outIf.Mask != 0xFFFFFFFF)
            {
                SendFrame(outIf, new Frame { Dst = Mac.Broadcast, Ip = p });
                return;
            }
            ArpEntry e;
            if (Arp.TryGetValue(nextHop, out e) && e.If == outIf && (e.Static || S.Now - e.Time < ArpTimeoutMs))
            {
                SendFrame(outIf, new Frame { Dst = e.Mac, Ip = p });
                return;
            }
            // ask with ARP first
            PendingArp pa;
            if (!pending.TryGetValue(nextHop, out pa))
            {
                pa = new PendingArp { If = outIf };
                pending[nextHop] = pa;
                SendArpRequest(outIf, nextHop, pa);
            }
            if (forwarded && D.Kind != "homerouter" && D.Kind != "cloud")
            {
                S.Note(D.Name + " doesn't know the MAC address of " + Ip.Str(nextHop) + " yet: it sends an ARP request and drops this packet (routers don't wait; the next one will get through).");
                return;
            }
            if (pa.Packets.Count < 8) pa.Packets.Add(p);
            S.Note(D.Name + " needs the MAC address of " + Ip.Str(nextHop) + " first, so the packet waits while it asks with ARP.");
        }

        void SendArpRequest(IpIf outIf, uint target, PendingArp pa)
        {
            pa.Tries++;
            var f = new Frame { Dst = Mac.Broadcast, Src = outIf.Mac, EtherType = "ARP", Arp = new ArpPkt { Op = 1, SenderMac = outIf.Mac, SenderIp = outIf.Ip, TargetIp = target } };
            S.NoteOrigin(D, f, D.Name + " asks everyone on " + outIf.ShortName + ": who has " + Ip.Str(target) + "?");
            SendFrame(outIf, f);
            pa.Timer = S.After(1000, () =>
            {
                PendingArp cur;
                if (!pending.TryGetValue(target, out cur) || cur != pa) return;
                if (pa.Tries < 2) { SendArpRequest(outIf, target, pa); return; }
                pending.Remove(target);   // nobody answered: the waiting packets are lost
            });
        }

        public void SendFrame(IpIf outIf, Frame f)
        {
            f.Src = outIf.Mac;
            if (f.Id == 0) f.Id = S.NewFrameId();
            if (outIf.IsSvi) { outIf.Bridge.SendFromSvi(outIf, f); return; }
            if (outIf.IsSub) { f.Vlan = outIf.NativeSub ? 0 : outIf.Vlan; S.Transmit(outIf.Port, f, null); return; }
            if (outIf.Port != null) S.Transmit(outIf.Port, f, null);
        }

        // ------------------------------------------------------------ receiving
        public void ReceiveFrame(Port port, Frame f)
        {
            var main = port.L3;
            IpIf ifc = main;
            var subs = Ifs.Where(x => x.IsSub && x.Port == port).ToList();
            if (f.Vlan != 0)
            {
                ifc = subs.FirstOrDefault(x => x.Vlan == f.Vlan && !x.NativeSub);
                if (ifc == null) { S.Note("The frame is tagged for VLAN " + f.Vlan + ", but " + D.Name + " has no subinterface with  encapsulation dot1Q " + f.Vlan + "  on " + port.Short + ": dropped."); return; }
            }
            else
            {
                var nat = subs.FirstOrDefault(x => x.NativeSub);
                if (nat != null) ifc = nat;
            }
            f.Vlan = 0;
            ReceiveOnIf(ifc, f);
        }

        public void ReceiveOnIf(IpIf ifc, Frame f)
        {
            if (!ifc.Up) { S.Note(ifc.ShortName + " is down, so the frame is ignored."); return; }
            if (f.Dst != ifc.Mac && !Mac.IsMulticast(f.Dst)) { S.Note("The frame is for " + f.Dst + ", not for " + D.Name + " (" + ifc.Mac + "), so it is ignored."); return; }
            if (f.Arp != null) { HandleArp(ifc, f.Arp); return; }
            if (f.Ip != null) ReceiveIp(ifc, f.Ip, f);
        }

        void HandleArp(IpIf ifc, ArpPkt a)
        {
            bool forMe = ifc.Ip != 0 && a.TargetIp == ifc.Ip;
            ArpEntry e;
            if (a.SenderIp != 0 && (forMe || Arp.TryGetValue(a.SenderIp, out e)))
            {
                if (!Arp.TryGetValue(a.SenderIp, out e) || !e.Static)
                {
                    Arp[a.SenderIp] = new ArpEntry { Ip = a.SenderIp, Mac = a.SenderMac, If = ifc, Time = S.Now };
                    S.Note(D.Name + " notes in its ARP table: " + Ip.Str(a.SenderIp) + " is " + a.SenderMac + ".");
                }
                PendingArp pa;
                if (pending.TryGetValue(a.SenderIp, out pa))
                {
                    pending.Remove(a.SenderIp);
                    if (pa.Timer != null) pa.Timer.Cancel();
                    foreach (var p in pa.Packets) SendFrame(pa.If, new Frame { Dst = a.SenderMac, Ip = p });
                    if (pa.Packets.Count > 0) S.Note("Now it can send the " + pa.Packets.Count + " waiting packet" + (pa.Packets.Count == 1 ? "" : "s") + ".");
                }
            }
            if (a.Op == 1)
            {
                if (forMe)
                {
                    S.Note("That's " + D.Name + "'s address, so it answers with its MAC address " + ifc.Mac + ".");
                    SendFrame(ifc, new Frame { Dst = a.SenderMac, EtherType = "ARP", Arp = new ArpPkt { Op = 2, SenderMac = ifc.Mac, SenderIp = ifc.Ip, TargetMac = a.SenderMac, TargetIp = a.SenderIp } });
                }
                else S.Note(D.Name + " isn't " + Ip.Str(a.TargetIp) + ", so it ignores the question.");
            }
        }

        public void ReceiveIp(IpIf ifc, IpPkt p, Frame f)
        {
            // access list on the way in
            if (ifc.AclIn.Length > 0)
            {
                Acl acl;
                if (Acls.TryGetValue(ifc.AclIn, out acl))
                {
                    Ace hit;
                    if (!acl.Permits(p, out hit))
                    {
                        S.Note("Access list " + acl.Name + " on " + ifc.ShortName + " (in) " + (hit != null ? "says: " + hit.Text() : "has no line that permits it (every list ends with an invisible 'deny any'") + ": dropped.");
                        SendIcmpError(p, 3, 13, ifc);
                        return;
                    }
                    S.Note("Access list " + acl.Name + " on " + ifc.ShortName + " (in) permits it" + (hit != null ? ": " + hit.Text() : "") + ".");
                }
            }
            if (ifc.Nat == 2) Nat.OutsideToInside(p);
            bool bcast = p.Dst == Ip.Broadcast || (ifc.Mask != 0 && ifc.Mask != 0xFFFFFFFF && p.Dst == Ip.BroadcastOf(ifc.Ip, ifc.Mask));
            if (bcast || Ip.IsMulticast(p.Dst) || IsMine(p.Dst) || (p.Dst == ifc.Ip && ifc.Ip != 0))
            {
                LocalDeliver(ifc, p);
                return;
            }
            if (!Forwarding)
            {
                S.Note("This packet is for " + Ip.Str(p.Dst) + ", not for " + D.Name + ". " + D.KindName + "s don't pass packets on, so it is dropped.");
                return;
            }
            Forward(ifc, p);
        }

        void Forward(IpIf inIf, IpPkt p)
        {
            if (p.Ttl <= 1)
            {
                S.Note("The TTL ran out (it was " + p.Ttl + "), so " + D.Name + " drops the packet and tells the sender (ICMP time exceeded).");
                SendIcmpError(p, 11, 0, inIf);
                return;
            }
            uint nh;
            var r = Lookup(p.Dst, out nh);
            if (r == null)
            {
                S.Note(D.Name + " has no route to " + Ip.Str(p.Dst) + " in its routing table, so it drops the packet and tells the sender (destination unreachable).");
                SendIcmpError(p, 3, 0, inIf);
                return;
            }
            p = p.Clone();
            p.Ttl--;
            var outIf = r.If;
            S.Note(D.Name + " looks up " + Ip.Str(p.Dst) + ": " + RouteText(r) + ". TTL goes down to " + p.Ttl + ".");
            if (outIf.AclOut.Length > 0)
            {
                Acl acl;
                if (Acls.TryGetValue(outIf.AclOut, out acl))
                {
                    Ace hit;
                    if (!acl.Permits(p, out hit))
                    {
                        S.Note("Access list " + acl.Name + " on " + outIf.ShortName + " (out) " + (hit != null ? "says: " + hit.Text() : "has no line that permits it (the invisible 'deny any' at the end)") + ": dropped.");
                        SendIcmpError(p, 3, 13, inIf);
                        return;
                    }
                    S.Note("Access list " + acl.Name + " on " + outIf.ShortName + " (out) permits it.");
                }
            }
            if (inIf.Nat == 1 && outIf.Nat == 2)
            {
                if (!Nat.InsideToOutside(p, outIf)) { }
            }
            Deliver(outIf, r.Code == "C" || r.Code == "L" ? p.Dst : nh, p, true);
        }

        public static string RouteText(Route r)
        {
            string net = Ip.Cidr(r.Net, r.Mask);
            switch (r.Code)
            {
                case "C": return net + " is directly connected on " + r.If.ShortName;
                case "L": return "it is this router's own address";
                case "S": return "static route to " + net + " via " + Ip.Str(r.NextHop) + " (" + r.If.ShortName + ")";
                case "S*": return "no closer match, so the default route via " + Ip.Str(r.NextHop) + " (" + r.If.ShortName + ")";
                case "R": case "R*": return "RIP route to " + net + " via " + Ip.Str(r.NextHop) + ", " + r.Metric + " hop" + (r.Metric == 1 ? "" : "s");
            }
            return net;
        }

        void LocalDeliver(IpIf ifc, IpPkt p)
        {
            if (Firewall != null && !Ip.IsMulticast(p.Dst) && p.Dst != Ip.Broadcast)
            {
                string why = Firewall(p);
                if (why != null) { S.Note(why); return; }
            }
            if (p.Icmp != null) { HandleIcmp(ifc, p); return; }
            if (p.Seg == null) return;
            if (p.Proto == "UDP")
            {
                var dm = p.Seg.App as DhcpMsg;
                if (p.Seg.DstPort == 67 && dm != null && DhcpRelay.IsServerReply(dm)) { if (Forwarding) DhcpRelay.Back(this, ifc, p); return; }
                Action<IpIf, IpPkt> h;
                if (Udp.TryGetValue(p.Seg.DstPort, out h)) { h(ifc, p); return; }
                if (p.Seg.DstPort == 67 && ifc.Helpers.Count > 0) { DhcpRelay.Relay(this, ifc, p); return; }
                if (p.Seg.DstPort == 68 && Forwarding) { DhcpRelay.Back(this, ifc, p); return; }
                if (p.Dst != Ip.Broadcast && !Ip.IsMulticast(p.Dst) && !(ifc.Mask != 0 && p.Dst == Ip.BroadcastOf(ifc.Ip, ifc.Mask)))
                {
                    S.Note("Nothing on " + D.Name + " listens on UDP port " + p.Seg.DstPort + ", so it answers 'port unreachable'.");
                    SendIcmpError(p, 3, 3, ifc);
                }
                else S.Note("Nothing on " + D.Name + " listens on UDP port " + p.Seg.DstPort + ", so it ignores it.");
                return;
            }
            if (p.Proto == "TCP") TcpConn.Receive(this, ifc, p);
        }

        void HandleIcmp(IpIf ifc, IpPkt p)
        {
            var ic = p.Icmp;
            if (ic.Type == 8)
            {
                if (p.Dst == Ip.Broadcast || Ip.IsMulticast(p.Dst)) return;
                S.Note(D.Name + " got a ping (echo request) from " + Ip.Str(p.Src) + " and answers it (echo reply).");
                var r = new IpPkt { Src = p.Dst, Dst = p.Src, Proto = "ICMP", Icmp = new IcmpPkt { Type = 0, Ident = ic.Ident, Seq = ic.Seq } };
                string err = Send(r, null);
                if (err.Length > 0) S.Note("...but it can't send the answer: " + (err == "no route" ? "it has no route back to " + Ip.Str(p.Src) + " (a missing default gateway?)" : err) + ".");
                return;
            }
            int ident = ic.Ident;
            if (ic.Type == 0) S.Note(D.Name + " got the answer to its ping from " + Ip.Str(p.Src) + ".");
            else if (ic.Original != null)
            {
                if (ic.Original.Icmp != null) ident = ic.Original.Icmp.Ident;
                else if (ic.Original.Seg != null) ident = -ic.Original.Seg.SrcPort;
                S.Note(D.Name + " got an ICMP message from " + Ip.Str(p.Src) + ": " + ic.Describe() + ".");
            }
            foreach (var w in Waiters.ToList()) if (w.Ident == ident) w.OnReply(p);
        }

        /// <summary>Sends an ICMP error about a packet back to its sender (never about another error or a broadcast).</summary>
        public void SendIcmpError(IpPkt about, int type, int code, IpIf via)
        {
            if (about.Icmp != null && about.Icmp.Type != 8 && about.Icmp.Type != 0) return;
            if (about.Dst == Ip.Broadcast || Ip.IsMulticast(about.Dst) || about.Src == 0) return;
            uint src = via != null && via.Ip != 0 ? via.Ip : 0;
            var e = new IpPkt { Src = src, Dst = about.Src, Proto = "ICMP", Icmp = new IcmpPkt { Type = type, Code = code, Original = about.Clone() } };
            Send(e, null);
        }

        public int Ephemeral() { ephemeral++; if (ephemeral > 65000) ephemeral = 49152; return ephemeral; }

        public void PortChanged(Port p, bool up)
        {
            foreach (var f in Ifs.Where(x => x.Port == p || (x.IsSvi && p.Dev.Bridge != null)).ToList())
            {
                if (!up && !f.IsSvi)
                    foreach (var k in Arp.Where(kv => kv.Value.If == f && !kv.Value.Static).Select(kv => kv.Key).ToList()) Arp.Remove(k);
                if (f.Dhcp && f.DhcpC != null && f.Up && !f.IsSvi) f.DhcpC.LinkUp();
                if (f.Dhcp && f.DhcpC != null && f.IsSvi && f.Up && f.Ip == 0) f.DhcpC.LinkUp();
            }
            if (Rip != null) Rip.Changed();
        }

        public void ClearArp() { foreach (var k in Arp.Where(kv => !kv.Value.Static).Select(kv => kv.Key).ToList()) Arp.Remove(k); }

        public List<ArpEntry> ArpEntries()
        {
            long now = S.Now;
            foreach (var k in Arp.Where(kv => !kv.Value.Static && now - kv.Value.Time > ArpTimeoutMs).Select(kv => kv.Key).ToList()) Arp.Remove(k);
            return Arp.Values.OrderBy(a => a.Ip).ToList();
        }

        /// <summary>Changes an interface's address (and tells RIP, DHCP and the ARP table).</summary>
        public void SetAddress(IpIf f, uint ip, uint mask)
        {
            f.Ip = ip; f.Mask = mask;
            if (ip != 0) f.Apipa = (ip >> 16) == 0xA9FE;
            if (Rip != null) Rip.Changed();
            // tell the neighbours (gratuitous ARP) so stale entries go away
            if (ip != 0 && f.Up && !f.IsLoopback)
                SendFrame(f, new Frame { Dst = Mac.Broadcast, EtherType = "ARP", Arp = new ArpPkt { Op = 2, SenderMac = f.Mac, SenderIp = ip, TargetIp = ip, TargetMac = Mac.Broadcast } });
        }
    }
}
