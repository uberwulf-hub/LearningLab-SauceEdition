// Network Lab - services: DHCP (server, client, relay), DNS (server), RIP v2, and a small TCP used by HTTP, file
// sharing, printing and SSH.
using System;
using System.Collections.Generic;
using System.Linq;

namespace NetworkLab
{
    // ================================================================= DHCP
    public class DhcpPool
    {
        public string Name = "";
        public uint Network, Mask, Router;
        public List<uint> Dns = new List<uint>();
        public string Domain = "";
        public uint Start;      // server style (first address); 0 = the whole network
        public int Max;         // server style (how many); 0 = all
        public bool Enabled = true;
        public bool Contains(uint ip) { return Mask != 0 && (ip & Mask) == Network; }
    }

    public class DhcpLease { public uint Ip; public string Mac = ""; public string Pool = ""; public long Time; public string Client = ""; }

    public class DhcpServer
    {
        readonly IpStack S;
        public readonly List<DhcpPool> Pools = new List<DhcpPool>();
        public readonly List<uint[]> Excluded = new List<uint[]>();
        public readonly Dictionary<string, DhcpLease> Leases = new Dictionary<string, DhcpLease>();
        readonly Dictionary<string, uint> offers = new Dictionary<string, uint>();
        public bool Enabled = true;

        public DhcpServer(IpStack s)
        {
            S = s;
            s.Udp[67] = Handle;
        }
        Sim Sim { get { return S.D.P.Sim; } }

        public DhcpPool Pool(string name, bool create)
        {
            var p = Pools.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
            if (p == null && create) { p = new DhcpPool { Name = name }; Pools.Add(p); }
            return p;
        }

        bool IsExcluded(uint ip)
        {
            foreach (var r in Excluded) if (ip >= r[0] && ip <= r[1]) return true;
            if (S.IfWithIp(ip) != null) return true;
            return false;
        }

        uint Allocate(DhcpPool pool, string mac)
        {
            DhcpLease l;
            if (Leases.TryGetValue(mac, out l) && pool.Contains(l.Ip)) return l.Ip;
            uint first = pool.Start != 0 ? pool.Start : pool.Network + 1;
            uint last = Ip.BroadcastOf(pool.Network, pool.Mask) - 1;
            if (pool.Max > 0) last = Math.Min(last, first + (uint)pool.Max - 1);
            var used = new HashSet<uint>(Leases.Values.Select(x => x.Ip));
            foreach (var o in offers) if (o.Key != mac) used.Add(o.Value);
            for (uint ip = first; ip <= last && ip > pool.Network; ip++)
            {
                if (used.Contains(ip) || IsExcluded(ip) || ip == pool.Router) continue;
                return ip;
            }
            return 0;
        }

        void Handle(IpIf ifc, IpPkt p)
        {
            var m = p.Seg.App as DhcpMsg;
            if (m == null) return;
            if (!Enabled) { if (ifc.Helpers.Count > 0) DhcpRelay.Relay(S, ifc, p); return; }
            if (ifc.Helpers.Count > 0 && m.Giaddr == 0) { DhcpRelay.Relay(S, ifc, p); return; }
            uint giaddr = m.Giaddr != 0 ? m.Giaddr : ifc.Ip;
            var pool = Pools.FirstOrDefault(x => x.Enabled && x.Contains(giaddr));
            if (pool == null)
            {
                Sim.Note("DHCP: " + S.D.Name + " has no address pool for the network of " + Ip.Str(giaddr) + ", so it doesn't answer.");
                return;
            }
            if (m.Type == "DISCOVER")
            {
                uint ip = Allocate(pool, m.ClientMac);
                if (ip == 0) { Sim.Note("DHCP: pool " + pool.Name + " has no free addresses left."); return; }
                offers[m.ClientMac] = ip;
                Sim.Note("DHCP: " + S.D.Name + " offers " + Ip.Str(ip) + " from pool " + pool.Name + ".");
                Reply(ifc, m, pool, ip, "OFFER");
            }
            else if (m.Type == "REQUEST")
            {
                uint ip = m.Requested;
                if (ip == 0 || !pool.Contains(ip) || (Leases.Values.Any(l => l.Ip == ip && l.Mac != m.ClientMac)))
                {
                    Sim.Note("DHCP: that address can't be given out, so the answer is NAK.");
                    Reply(ifc, m, pool, 0, "NAK");
                    return;
                }
                if (m.ServerId != 0 && m.ServerId != ServerId(ifc, m)) { offers.Remove(m.ClientMac); return; }   // the client chose another server
                offers.Remove(m.ClientMac);
                Leases[m.ClientMac] = new DhcpLease { Ip = ip, Mac = m.ClientMac, Pool = pool.Name, Time = Sim.Now };
                Sim.Note("DHCP: " + Ip.Str(ip) + " is now leased to " + m.ClientMac + ".");
                Reply(ifc, m, pool, ip, "ACK");
            }
            else if (m.Type == "RELEASE")
            {
                Leases.Remove(m.ClientMac);
                Sim.Note("DHCP: " + m.ClientMac + " gave its address back.");
            }
        }

        uint ServerId(IpIf ifc, DhcpMsg m)
        {
            if (m.Giaddr != 0)
            {
                uint nh;
                var r = S.Lookup(m.Giaddr, out nh);
                if (r != null) return r.If.Ip;
            }
            return ifc.Ip;
        }

        void Reply(IpIf ifc, DhcpMsg m, DhcpPool pool, uint ip, string type)
        {
            var r = new DhcpMsg { Type = type, Xid = m.Xid, ClientMac = m.ClientMac, Yiaddr = ip, Giaddr = m.Giaddr, ServerId = ServerId(ifc, m), Mask = pool.Mask, Router = pool.Router, Domain = pool.Domain };
            r.Dns.AddRange(pool.Dns);
            if (m.Giaddr != 0)
            {
                var pk = new IpPkt { Dst = m.Giaddr, Proto = "UDP", Seg = new Seg { SrcPort = 67, DstPort = 67, App = r } };
                S.Send(pk, null);
            }
            else
            {
                var pk = new IpPkt { Src = ifc.Ip, Dst = Ip.Broadcast, Proto = "UDP", Seg = new Seg { SrcPort = 67, DstPort = 68, App = r } };
                S.Send(pk, ifc);
            }
        }

        public void ClearBindings() { Leases.Clear(); offers.Clear(); }
    }

    /// <summary>ip helper-address: a router passes DHCP broadcasts on to a server in another network.</summary>
    public static class DhcpRelay
    {
        public static void Relay(IpStack s, IpIf ifc, IpPkt p)
        {
            var m = p.Seg.App as DhcpMsg;
            if (m == null || ifc.Ip == 0) return;
            foreach (var h in ifc.Helpers)
            {
                var copy = m.CloneMsg();
                if (copy.Giaddr == 0) copy.Giaddr = ifc.Ip;
                s.D.P.Sim.Note("DHCP relay: " + s.D.Name + " has  ip helper-address " + Ip.Str(h) + "  on " + ifc.ShortName + ", so it passes the " + m.Type + " on to that server (as a normal packet, not a broadcast).");
                var pk = new IpPkt { Dst = h, Src = ifc.Ip, Proto = "UDP", Seg = new Seg { SrcPort = 67, DstPort = 67, App = copy } };
                s.Send(pk, null);
            }
        }

        /// <summary>The server's answer comes back to the router (port 67 to giaddr): send it on to the client.</summary>
        public static void Back(IpStack s, IpIf ifc, IpPkt p)
        {
            var m = p.Seg.App as DhcpMsg;
            if (m == null) return;
            var outIf = s.IfWithIp(m.Giaddr);
            if (outIf == null) return;
            s.D.P.Sim.Note("DHCP relay: " + s.D.Name + " passes the server's " + m.Type + " back to the client on " + outIf.ShortName + ".");
            var pk = new IpPkt { Src = outIf.Ip, Dst = Ip.Broadcast, Proto = "UDP", Seg = new Seg { SrcPort = 67, DstPort = 68, App = m.CloneMsg() } };
            s.Send(pk, outIf);
        }

        /// <summary>A relayed answer arriving at the router on UDP 67 (from the server) instead of 68.</summary>
        public static bool IsServerReply(DhcpMsg m) { return m.Type == "OFFER" || m.Type == "ACK" || m.Type == "NAK"; }
    }

    public class DhcpClient
    {
        readonly IpStack S;
        readonly IpIf F;
        public string State = "idle";   // idle, discovering, requesting, bound, apipa
        public uint Server, Router, Offered;
        public List<uint> Dns = new List<uint>();
        public string Domain = "";
        int xid;
        int tries;
        SimTimer timer;
        public long LeaseTime;
        public event Action<DhcpClient> Done;
        public bool InstallDefaultRoute;

        public DhcpClient(IpStack s, IpIf f)
        {
            S = s; F = f;
            if (!s.Udp.ContainsKey(68)) s.Udp[68] = Received;
            // a router relaying replies uses 67; a client listens on 68 (one handler serves every client interface)
        }
        Sim Sim { get { return S.D.P.Sim; } }

        public void LinkUp() { if (State != "discovering" && State != "requesting") Start(); }

        public void Start()
        {
            if (!F.Dhcp) return;
            tries = 0;
            State = "discovering";
            if (F.Ip == 0 || F.Apipa) { }
            SendDiscover();
        }

        public void Stop() { if (timer != null) timer.Cancel(); State = "idle"; }

        void SendDiscover()
        {
            if (!F.Up || !F.Dhcp) { State = "idle"; return; }
            tries++;
            xid = S.D.P.Rng.Next(1, int.MaxValue);
            State = "discovering";
            var m = new DhcpMsg { Type = "DISCOVER", Xid = xid, ClientMac = F.Mac };
            var pk = new IpPkt { Src = 0, Dst = Ip.Broadcast, Proto = "UDP", Seg = new Seg { SrcPort = 68, DstPort = 67, App = m } };
            var fr = new Frame { Dst = Mac.Broadcast, Ip = pk };
            Sim.NoteOrigin(S.D, fr, S.D.Name + " has no address yet on " + F.ShortName + ", so it shouts to everyone: is there a DHCP server?");
            S.SendFrame(F, fr);
            if (timer != null) timer.Cancel();
            timer = Sim.After(3000, () =>
            {
                if (State == "bound") return;
                if (tries < 3) { SendDiscover(); return; }
                // nobody answered: an automatic private address (APIPA), like a real PC
                State = "apipa";
                if (S.D.Host != null)
                {
                    uint a = 0xA9FE0000u | (uint)(S.D.P.Rng.Next(1, 254) << 8) | (uint)S.D.P.Rng.Next(1, 254);
                    S.SetAddress(F, a, 0xFFFF0000);
                    F.Apipa = true;
                    F.Gateway = 0; F.Dns.Clear();
                }
                var h = Done; if (h != null) h(this);
                timer = Sim.After(60000, () => { if (State == "apipa" && F.Dhcp) { tries = 0; SendDiscover(); } });
            });
        }

        void Received(IpIf ifc, IpPkt p)
        {
            var m = p.Seg.App as DhcpMsg;
            if (m == null) return;
            // this handler is shared: find the client interface the answer is for
            foreach (var f in S.Ifs)
            {
                if (f.DhcpC != null && f.Mac == m.ClientMac) { f.DhcpC.Handle(m); return; }
            }
        }

        void Handle(DhcpMsg m)
        {
            if (m.Xid != xid) return;
            if (m.Type == "OFFER" && State == "discovering")
            {
                Offered = m.Yiaddr;
                Server = m.ServerId;
                State = "requesting";
                Sim.Note(S.D.Name + " got an offer of " + Ip.Str(m.Yiaddr) + " and asks for it (REQUEST).");
                var r = new DhcpMsg { Type = "REQUEST", Xid = xid, ClientMac = F.Mac, Requested = m.Yiaddr, ServerId = m.ServerId };
                var pk = new IpPkt { Src = 0, Dst = Ip.Broadcast, Proto = "UDP", Seg = new Seg { SrcPort = 68, DstPort = 67, App = r } };
                S.SendFrame(F, new Frame { Dst = Mac.Broadcast, Ip = pk });
                return;
            }
            if (m.Type == "ACK" && State == "requesting")
            {
                if (timer != null) timer.Cancel();
                State = "bound";
                Router = m.Router;
                Dns = new List<uint>(m.Dns);
                Domain = m.Domain;
                Server = m.ServerId;
                LeaseTime = Sim.Now;
                S.SetAddress(F, m.Yiaddr, m.Mask);
                F.Apipa = false;
                if (S.D.Host != null) { F.Gateway = m.Router; F.Dns = new List<uint>(m.Dns); }
                Sim.Note(S.D.Name + " now uses " + Ip.Str(m.Yiaddr) + " / " + Ip.Str(m.Mask) + (m.Router != 0 ? ", gateway " + Ip.Str(m.Router) : "") + (m.Dns.Count > 0 ? ", DNS " + Ip.Str(m.Dns[0]) : "") + ".");
                var h = Done; if (h != null) h(this);
                return;
            }
            if (m.Type == "NAK" && State == "requesting") { State = "idle"; Sim.After(1000, () => { tries = 0; SendDiscover(); }); }
        }

        public void Release()
        {
            if (State == "bound" && F.Ip != 0)
            {
                var r = new DhcpMsg { Type = "RELEASE", Xid = xid, ClientMac = F.Mac, ServerId = Server };
                var pk = new IpPkt { Src = F.Ip, Dst = Server, Proto = "UDP", Seg = new Seg { SrcPort = 68, DstPort = 67, App = r } };
                S.Send(pk, null);
            }
            if (timer != null) timer.Cancel();
            State = "idle";
            S.SetAddress(F, 0, 0);
            F.Gateway = 0; F.Dns.Clear();
            Router = 0;
        }
    }

    // ================================================================= DNS
    public class DnsServer
    {
        readonly IpStack S;
        public bool Enabled = true;
        public readonly SortedDictionary<string, uint> Records = new SortedDictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        public readonly SortedDictionary<string, string> Cnames = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public DnsServer(IpStack s) { S = s; s.Udp[53] = Handle; }

        public uint Resolve(string name)
        {
            uint ip;
            name = name.TrimEnd('.');
            for (int g = 0; g < 5; g++)
            {
                if (Records.TryGetValue(name, out ip)) return ip;
                string c;
                if (!Cnames.TryGetValue(name, out c)) return 0;
                name = c;
            }
            return 0;
        }

        void Handle(IpIf ifc, IpPkt p)
        {
            var q = p.Seg.App as DnsMsg;
            if (q == null || q.Response) return;
            var sim = S.D.P.Sim;
            var r = new DnsMsg { Response = true, Id = q.Id, QName = q.QName };
            if (!Enabled) { sim.Note("The DNS service on " + S.D.Name + " is switched off."); r.Error = "REFUSED"; }
            else
            {
                r.Answer = Resolve(q.QName);
                if (r.Answer == 0) r.Error = "NXDOMAIN";
                sim.Note("DNS: " + S.D.Name + " looks up " + q.QName + (r.Answer != 0 ? ": it is " + Ip.Str(r.Answer) + "." : ": no such name in its records."));
            }
            S.Send(new IpPkt { Src = p.Dst, Dst = p.Src, Proto = "UDP", Seg = new Seg { SrcPort = 53, DstPort = p.Seg.SrcPort, App = r } }, null);
        }
    }

    // ================================================================= RIP v2
    public class RipRoute { public uint Net, Mask, NextHop; public int Metric; public IpIf If; public long Heard; public bool Garbage; }

    public class Rip
    {
        readonly IpStack S;
        public int Version = 1;
        public bool VersionSet;
        public readonly List<uint> Networks = new List<uint>();
        public readonly List<string> Passive = new List<string>();
        public bool AutoSummary = true;
        public bool DefaultOriginate;
        public readonly Dictionary<string, RipRoute> Learned = new Dictionary<string, RipRoute>();
        SimTimer periodic, triggered;
        public long UpdateMs = 30000, InvalidMs = 180000, FlushMs = 240000;
        public long LastUpdate;

        public Rip(IpStack s)
        {
            S = s;
            s.Udp[520] = Receive;
            Schedule();
        }
        Sim Sim { get { return S.D.P.Sim; } }

        public bool Enabled(IpIf f)
        {
            if (f.Ip == 0) return false;
            foreach (var n in Networks)
            {
                int pre = Ip.ClassfulPrefix(n);
                if ((f.Ip & Ip.MaskOf(pre)) == (n & Ip.MaskOf(pre))) return true;
            }
            return false;
        }

        void Schedule()
        {
            if (periodic != null) periodic.Cancel();
            periodic = Sim.After(UpdateMs, () => { SendAll(); Age(); Schedule(); });
        }

        /// <summary>Something changed (an interface, an address, a network statement): tell the neighbours soon.</summary>
        public void Changed()
        {
            if (triggered != null && !triggered.Cancelled) return;
            triggered = Sim.After(5, () => { triggered = null; DropDown(); SendAll(); });
        }

        void DropDown()
        {
            foreach (var r in Learned.Values) if (!r.If.Up && r.Metric < 16) { r.Metric = 16; r.Heard = Sim.Now; }
        }

        void Age()
        {
            long now = Sim.Now;
            foreach (var k in Learned.Keys.ToList())
            {
                var r = Learned[k];
                if (now - r.Heard > FlushMs) Learned.Remove(k);
                else if (now - r.Heard > InvalidMs && r.Metric < 16) r.Metric = 16;
            }
        }

        readonly Dictionary<string, RipEntry> lastConnected = new Dictionary<string, RipEntry>();
        readonly Dictionary<string, long> withdrawnAt = new Dictionary<string, long>();
        readonly Dictionary<string, RipEntry> withdrawn = new Dictionary<string, RipEntry>();

        public void SendAll()
        {
            LastUpdate = Sim.Now;
            // networks that were connected and aren't any more: tell the neighbours they are gone (metric 16)
            var now = new Dictionary<string, RipEntry>();
            foreach (var o in S.Ifs) if (o.Up && Enabled(o)) now[(o.Ip & o.Mask) + "/" + o.Mask] = new RipEntry { Net = o.Ip & o.Mask, Mask = o.Mask, Metric = 16 };
            foreach (var kv in lastConnected) if (!now.ContainsKey(kv.Key)) { withdrawn[kv.Key] = kv.Value; withdrawnAt[kv.Key] = Sim.Now; }
            foreach (var k in now.Keys) { withdrawn.Remove(k); withdrawnAt.Remove(k); }
            foreach (var k in withdrawnAt.Where(x => Sim.Now - x.Value > 120000).Select(x => x.Key).ToList()) { withdrawn.Remove(k); withdrawnAt.Remove(k); }
            lastConnected.Clear();
            foreach (var kv in now) lastConnected[kv.Key] = kv.Value;
            foreach (var f in S.Ifs)
            {
                if (!f.Up || !Enabled(f) || Passive.Any(x => string.Equals(x, f.Name, StringComparison.OrdinalIgnoreCase))) continue;
                var m = new RipMsg { Command = 2, Version = Version == 1 ? 1 : 2 };
                foreach (var o in S.Ifs)
                    if (o.Up && Enabled(o) && o != f) m.Entries.Add(new RipEntry { Net = o.Ip & o.Mask, Mask = o.Mask, Metric = 1 });
                    else if (o == f && o.Up && Enabled(o)) { }
                foreach (var r in Learned.Values)
                {
                    if (r.If == f) continue;   // split horizon: never tell a neighbour what it told us
                    m.Entries.Add(new RipEntry { Net = r.Net, Mask = r.Mask, Metric = Math.Min(16, r.Metric + 1) });
                }
                if (DefaultOriginate && S.Routes().Any(x => x.IsDefault && x.If != f)) m.Entries.Add(new RipEntry { Net = 0, Mask = 0, Metric = 1 });
                foreach (var w in withdrawn.Values) m.Entries.Add(new RipEntry { Net = w.Net, Mask = w.Mask, Metric = 16 });
                uint dst = m.Version == 2 ? 0xE0000009u : Ip.Broadcast;
                var pk = new IpPkt { Src = f.Ip, Dst = dst, Ttl = 2, Proto = "UDP", Seg = new Seg { SrcPort = 520, DstPort = 520, App = m } };
                var fr = new Frame { Dst = dst == Ip.Broadcast ? Mac.Broadcast : Mac.ForIp(dst), Ip = pk };
                Sim.NoteOrigin(S.D, fr, S.D.Name + " tells its RIP neighbours on " + f.ShortName + " which networks it can reach (" + m.Entries.Count + ").");
                S.SendFrame(f, fr);
            }
        }

        void Receive(IpIf ifc, IpPkt p)
        {
            var m = p.Seg.App as RipMsg;
            if (m == null) return;
            if (!Enabled(ifc)) { Sim.Note("RIP isn't on for " + ifc.ShortName + " (no matching  network  statement), so the update is ignored."); return; }
            if (S.IsMine(p.Src)) return;
            if (Ip.SameNet(p.Src, ifc.Ip, ifc.Mask) == false) { Sim.Note("The RIP update comes from another network, so it is ignored."); return; }
            if (m.Command == 1) { Changed(); return; }
            bool changed = false;
            var notes = new List<string>();
            foreach (var e in m.Entries)
            {
                int metric = Math.Min(16, e.Metric);   // the sender already counted its own hop (connected = 1)
                if (S.Ifs.Any(f => f.Up && f.Ip != 0 && (f.Ip & f.Mask) == e.Net && f.Mask == e.Mask)) continue;   // connected beats RIP
                string key = e.Net + "/" + e.Mask;
                RipRoute cur;
                if (!Learned.TryGetValue(key, out cur))
                {
                    if (metric >= 16) continue;
                    Learned[key] = new RipRoute { Net = e.Net, Mask = e.Mask, NextHop = p.Src, Metric = metric, If = ifc, Heard = Sim.Now };
                    notes.Add("new route to " + Ip.Cidr(e.Net, e.Mask) + " (" + metric + " hop" + (metric == 1 ? "" : "s") + ")");
                    changed = true;
                }
                else if (cur.NextHop == p.Src)
                {
                    if (cur.Metric != metric) { changed = true; notes.Add(Ip.Cidr(e.Net, e.Mask) + " now " + (metric >= 16 ? "unreachable" : metric + " hops")); }
                    cur.Metric = metric; cur.Heard = Sim.Now; cur.If = ifc;
                }
                else if (metric < cur.Metric)
                {
                    cur.NextHop = p.Src; cur.Metric = metric; cur.If = ifc; cur.Heard = Sim.Now;
                    changed = true;
                    notes.Add("a shorter way to " + Ip.Cidr(e.Net, e.Mask));
                }
            }
            Sim.Note("RIP: " + S.D.Name + " reads the update from " + Ip.Str(p.Src) + (notes.Count > 0 ? ": " + string.Join("; ", notes.ToArray()) + "." : ": nothing new."));
            if (changed) Changed();
        }
    }

    // ================================================================= TCP (just enough for the apps)
    public class TcpConn
    {
        public IpStack S;
        public uint Remote, Local;
        public int RemotePort, LocalPort;
        public string State = "syn-sent";
        public Action<TcpConn> OnConnected;
        public Action<TcpConn, TextMsg> OnData;
        public Action<TcpConn, string> OnFail;
        SimTimer timer;
        TextMsg queued;

        string Key { get { return LocalPort + "|" + Ip.Str(Remote) + "|" + RemotePort; } }

        /// <summary>Opens a connection; answers arrive through the callbacks.</summary>
        public static TcpConn Open(IpStack s, uint dst, int port, Action<TcpConn> onConnected, Action<TcpConn, TextMsg> onData, Action<TcpConn, string> onFail)
        {
            var c = new TcpConn { S = s, Remote = dst, RemotePort = port, LocalPort = s.Ephemeral(), OnConnected = onConnected, OnData = onData, OnFail = onFail };
            s.Conns[c.Key] = c;
            var pk = new IpPkt { Dst = dst, Proto = "TCP", Seg = new Seg { SrcPort = c.LocalPort, DstPort = port, Flags = "SYN" } };
            var sim = s.D.P.Sim;
            string err = s.Send(pk, null);
            if (err.Length > 0) { s.Conns.Remove(c.Key); sim.After(0, () => { if (onFail != null) onFail(c, err); }); return c; }
            c.Local = pk.Src;
            sim.NoteOrigin(s.D, new Frame { Ip = pk }, s.D.Name + " asks " + Ip.Str(dst) + " to open a connection on TCP port " + port + " (SYN).");
            c.timer = sim.After(4000, () => c.Fail("timeout"));
            return c;
        }

        public void Send(TextMsg m)
        {
            if (State != "open") { queued = m; return; }
            var pk = new IpPkt { Src = Local, Dst = Remote, Proto = "TCP", Seg = new Seg { SrcPort = LocalPort, DstPort = RemotePort, Flags = "PSH+ACK", App = m } };
            S.Send(pk, null);
            if (timer != null) timer.Cancel();
            timer = S.D.P.Sim.After(4000, () => Fail("timeout"));
        }

        public void Close() { if (timer != null) timer.Cancel(); State = "closed"; S.Conns.Remove(Key); }

        void Fail(string why)
        {
            if (State == "closed") return;
            State = "closed";
            S.Conns.Remove(Key);
            if (OnFail != null) OnFail(this, why);
        }

        public static void Receive(IpStack s, IpIf ifc, IpPkt p)
        {
            var sim = s.D.P.Sim;
            var seg = p.Seg;
            TcpConn c;
            string key = seg.DstPort + "|" + Ip.Str(p.Src) + "|" + seg.SrcPort;
            if (s.Conns.TryGetValue(key, out c))
            {
                if (seg.Flags == "SYN+ACK" && c.State == "syn-sent")
                {
                    c.State = "open";
                    if (c.timer != null) c.timer.Cancel();
                    sim.Note("The connection to " + Ip.Str(p.Src) + " port " + seg.SrcPort + " is open.");
                    if (c.OnConnected != null) c.OnConnected(c);
                    if (c.queued != null) { var q = c.queued; c.queued = null; c.Send(q); }
                    return;
                }
                if (seg.Flags == "RST") { if (c.timer != null) c.timer.Cancel(); sim.Note("The other side refused the connection."); c.Fail("refused"); return; }
                if (seg.App is TextMsg)
                {
                    if (c.timer != null) c.timer.Cancel();
                    if (c.OnData != null) c.OnData(c, (TextMsg)seg.App);
                    return;
                }
                return;
            }
            // a server
            Func<IpPkt, TextMsg, TextMsg> handler;
            if (!s.TcpListeners.TryGetValue(seg.DstPort, out handler) || (s.TcpOpen != null && !s.TcpOpen(seg.DstPort)))
            {
                if (seg.Flags == "SYN")
                {
                    sim.Note("Nothing on " + s.D.Name + " listens on TCP port " + seg.DstPort + ", so it refuses (RST).");
                    s.Send(new IpPkt { Src = p.Dst, Dst = p.Src, Proto = "TCP", Seg = new Seg { SrcPort = seg.DstPort, DstPort = seg.SrcPort, Flags = "RST" } }, null);
                }
                return;
            }
            if (seg.Flags == "SYN")
            {
                sim.Note(s.D.Name + " accepts the connection on port " + seg.DstPort + " (SYN+ACK).");
                s.Send(new IpPkt { Src = p.Dst, Dst = p.Src, Proto = "TCP", Seg = new Seg { SrcPort = seg.DstPort, DstPort = seg.SrcPort, Flags = "SYN+ACK" } }, null);
                return;
            }
            var req = seg.App as TextMsg;
            if (req == null) return;
            var resp = handler(p, req);
            if (resp == null) return;
            s.Send(new IpPkt { Src = p.Dst, Dst = p.Src, Proto = "TCP", Seg = new Seg { SrcPort = seg.DstPort, DstPort = seg.SrcPort, Flags = "PSH+ACK", App = resp } }, null);
        }
    }
}
