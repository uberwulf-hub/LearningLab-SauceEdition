// Network Lab - the simulation engine: a deterministic discrete-event queue (time in milliseconds), cables and their
// lights, spanning tree, Wi-Fi association, and the event list that Simulation mode shows.
using System;
using System.Collections.Generic;
using System.Linq;

namespace NetworkLab
{
    /// <summary>One frame arriving at a device, as Simulation mode lists it.</summary>
    public class SimEvent
    {
        public int N { get; set; }
        public long Time { get; set; }
        public string TimeText { get { return (Time / 1000.0).ToString("0.000"); } }
        public string Last { get; set; }
        public string At { get; set; }
        public string Type { get; set; }
        public string Info { get; set; }
        public string Color { get { return Sim.ColorOf(Type); } }
        public Frame Frame;
        public Device FromDev, AtDev;
        public List<Port> Path = new List<Port>();   // the ports the frame crossed (from, through patch points, to)
        public List<string> Notes = new List<string>();
        public string Decision { get { return string.Join("\n", Notes.ToArray()); } }
        public bool Origin;
        public int Slot;
    }

    public class SimTimer { public bool Cancelled; public void Cancel() { Cancelled = true; } }

    public class Sim
    {
        public readonly Project P;
        public long Now;
        long seq;
        int frameIds;
        readonly List<Ev> heap = new List<Ev>();
        public readonly List<Link> Wireless = new List<Link>();
        /// <summary>Simulation mode: every frame is recorded with what each device decided.</summary>
        public bool Capture;
        public readonly List<SimEvent> Log = new List<SimEvent>();
        public int MaxLog = 5000;
        SimEvent current;
        public int SlotCounter;
        readonly Dictionary<Port, bool> wasUp = new Dictionary<Port, bool>();
        public long EventsRun;
        public bool StpEnabled = true;

        class Ev
        {
            public long Time, Seq;
            public Action Act;
            public Port To, From;
            public Frame F;
            public List<Port> Path;
            public SimTimer Timer;
        }

        public Sim(Project p) { P = p; }

        // ------------------------------------------------------------ the queue
        void Push(Ev e)
        {
            e.Seq = seq++;
            heap.Add(e);
            int i = heap.Count - 1;
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (Less(heap[i], heap[parent])) { var t = heap[i]; heap[i] = heap[parent]; heap[parent] = t; i = parent; } else break;
            }
        }
        static bool Less(Ev a, Ev b) { return a.Time < b.Time || (a.Time == b.Time && a.Seq < b.Seq); }
        Ev Pop()
        {
            var top = heap[0];
            var last = heap[heap.Count - 1];
            heap.RemoveAt(heap.Count - 1);
            if (heap.Count > 0)
            {
                heap[0] = last;
                int i = 0;
                while (true)
                {
                    int l = 2 * i + 1, r = l + 1, m = i;
                    if (l < heap.Count && Less(heap[l], heap[m])) m = l;
                    if (r < heap.Count && Less(heap[r], heap[m])) m = r;
                    if (m == i) break;
                    var t = heap[i]; heap[i] = heap[m]; heap[m] = t; i = m;
                }
            }
            return top;
        }
        public int Pending { get { return heap.Count; } }
        public long NextTime { get { return heap.Count == 0 ? long.MaxValue : heap[0].Time; } }

        /// <summary>Runs an action after a delay (milliseconds of simulated time).</summary>
        public SimTimer After(long ms, Action a)
        {
            var t = new SimTimer();
            Push(new Ev { Time = Now + Math.Max(0, ms), Act = a, Timer = t });
            return t;
        }

        /// <summary>Runs everything due up to the given time (and moves the clock there).</summary>
        public void RunUntil(long t)
        {
            int guard = 0;
            while (heap.Count > 0 && heap[0].Time <= t && guard++ < 400000) RunOne();
            if (t > Now) Now = t;
        }
        public void RunFor(long ms) { RunUntil(Now + ms); }

        /// <summary>Runs until nothing is left before the given time or a condition holds (checked after each event).</summary>
        public bool RunUntil(long t, Func<bool> done)
        {
            int guard = 0;
            if (done()) return true;
            while (heap.Count > 0 && heap[0].Time <= t && guard++ < 400000)
            {
                RunOne();
                if (done()) return true;
            }
            if (t > Now) Now = t;
            return done();
        }

        void RunOne()
        {
            var e = Pop();
            if (e.Time > Now) Now = e.Time;
            EventsRun++;
            if (e.Timer != null && e.Timer.Cancelled) return;
            if (e.F != null) { Arrive(e); return; }
            try { e.Act(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        }

        /// <summary>Simulation mode: runs the next moment in which a visible frame arrives. Returns the new events.</summary>
        public List<SimEvent> StepSlot(Func<SimEvent, bool> visible, long maxAhead)
        {
            var result = new List<SimEvent>();
            long limit = Now + maxAhead;
            int guard = 0;
            while (heap.Count > 0 && heap[0].Time <= limit && guard++ < 100000)
            {
                long t = heap[0].Time;
                int before = Log.Count;
                SlotCounter++;
                while (heap.Count > 0 && heap[0].Time == t) RunOne();
                for (int i = before; i < Log.Count; i++) if (visible(Log[i])) result.Add(Log[i]);
                if (result.Count > 0) break;
            }
            return result;
        }

        // ------------------------------------------------------------ frames on cables
        public int NewFrameId() { return ++frameIds; }

        /// <summary>Sends a frame out of a port. On a Wi-Fi radio it goes to the right station (or all of them).</summary>
        public void Transmit(Port outPort, Frame f, Port arrivedFrom)
        {
            if (!outPort.Dev.PoweredOn || outPort.Shutdown) return;
            if (f.Id == 0) f.Id = NewFrameId();
            outPort.TxFrames++;
            if (outPort.IsWireless)
            {
                if (outPort.Links.Count == 0) return;
                var targets = new List<Link>();
                if (!Mac.IsMulticast(f.Dst))
                    foreach (var l in outPort.Links) { var o = l.Other(outPort); if (o.Mac == f.Dst) targets.Add(l); }
                if (targets.Count == 0)
                    foreach (var l in outPort.Links) if (arrivedFrom == null || l.Other(outPort) != arrivedFrom) targets.Add(l);
                // a station (not the AP) sends only to its AP
                foreach (var l in targets)
                {
                    if (l.Status != "up") continue;
                    var to = l.Other(outPort);
                    Push(new Ev { Time = Now + 1, F = targets.Count > 1 ? f.Clone() : f, To = to, From = outPort, Path = new List<Port> { outPort, to } });
                }
                return;
            }
            var link = outPort.Link;
            if (link == null || link.Status != "up") return;
            var path = new List<Port> { outPort };
            Port cur = link.Other(outPort);
            for (int guard = 0; guard < 16 && cur != null && cur.Media == "passive"; guard++)
            {
                path.Add(cur);
                var pair = cur.Pair;
                if (pair == null || pair.Link == null || pair.Link.Status != "up") return;
                path.Add(pair);
                cur = pair.Link.Other(pair);
            }
            if (cur == null || cur.Media == "passive") return;
            path.Add(cur);
            Push(new Ev { Time = Now + 1, F = f, To = cur, From = outPort, Path = path });
        }

        void Arrive(Ev e)
        {
            var port = e.To;
            var d = port.Dev;
            if (Capture)
            {
                current = new SimEvent
                {
                    Time = Now, Last = e.From.Dev.Name, At = d.Name, Type = e.F.Protocol, Info = e.F.Summary(),
                    Frame = e.F.Clone(), FromDev = e.From.Dev, AtDev = d, Path = e.Path, Slot = SlotCounter
                };
                AddLog(current);
            }
            try
            {
                if (!d.PoweredOn) { Note("The device is switched off."); return; }
                if (port.Shutdown) { Note(port.Short + " is shut down, so the frame is ignored."); return; }
                port.RxFrames++;
                Note("Arrived on " + port.Short + ".");
                if (d.Bridge != null && d.Bridge.Members.Contains(port)) d.Bridge.Receive(port, e.F);
                else if (d.Ip != null && port.L3 != null) d.Ip.ReceiveFrame(port, e.F);
                else Note("Nothing on this port handles it, so it is dropped.");
            }
            finally { current = null; }
        }

        void AddLog(SimEvent ev)
        {
            ev.N = Log.Count + 1;
            Log.Add(ev);
            if (Log.Count > MaxLog) { Log.RemoveRange(0, Log.Count - MaxLog); }
        }

        /// <summary>Records what a device decided (shown under the event in Simulation mode).</summary>
        public void Note(string text) { if (current != null) current.Notes.Add(text); }

        /// <summary>A device starts something by itself (a ping, a DHCP request...): an event with no previous device.</summary>
        public void NoteOrigin(Device at, Frame f, string text)
        {
            if (!Capture) return;
            if (current != null && current.AtDev == at) { current.Notes.Add(text); return; }
            var ev = new SimEvent { Time = Now, Last = "--", At = at.Name, Type = f != null ? f.Protocol : "", Info = f != null ? f.Summary() : text, Frame = f != null ? f.Clone() : null, AtDev = at, Origin = true, Slot = SlotCounter };
            ev.Notes.Add(text);
            AddLog(ev);
        }
        public bool Capturing { get { return Capture && current != null; } }

        public static string ColorOf(string type)
        {
            switch (type)
            {
                case "ICMP": return "#F472B6";
                case "ARP": return "#FBBF24";
                case "DHCP": return "#34D399";
                case "DNS": return "#60A5FA";
                case "HTTP": return "#A78BFA";
                case "RIP": return "#F97316";
                case "TCP": return "#C4B5FD";
                case "SMB": return "#2DD4BF";
                case "PRINT": return "#FB7185";
                case "SSH": return "#94A3B8";
                case "LLMNR": case "MDNS": return "#FDE68A";
            }
            return "#E5E7EB";
        }
        public static readonly string[] Protocols = { "ICMP", "ARP", "DHCP", "DNS", "HTTP", "TCP", "RIP", "SMB", "PRINT", "SSH", "LLMNR", "MDNS", "UDP" };

        // ------------------------------------------------------------ topology: cables, Wi-Fi, spanning tree
        public void DeviceAdded(Device d) { if (d.Ip != null) d.Ip.Start(); if (d.Host != null) d.Host.Start(); }

        bool refreshing;
        /// <summary>After any change to cables, ports or settings that affect links: lights, Wi-Fi, spanning tree, and
        /// telling devices which ports went up or down.</summary>
        public void TopologyChanged()
        {
            if (refreshing) return;
            refreshing = true;
            try
            {
                foreach (var l in P.Links) Physical.UpdateLength(P, l);
                RefreshLinks();
                RefreshWireless();
                ComputeStp();
                var changed = new List<Port>();
                foreach (var d in P.Devices)
                    foreach (var p in d.Ports)
                    {
                        bool up = p.IsUp && d.PoweredOn;
                        bool before;
                        if (!wasUp.TryGetValue(p, out before)) before = false;
                        if (up != before) { wasUp[p] = up; changed.Add(p); }
                    }
                foreach (var p in changed)
                {
                    bool up = wasUp[p];
                    if (p.Dev.Bridge != null) p.Dev.Bridge.PortChanged(p, up);
                    if (p.Dev.Ip != null) p.Dev.Ip.PortChanged(p, up);
                    if (p.Dev.Host != null) p.Dev.Host.PortChanged(p, up);
                }
            }
            finally { refreshing = false; }
            P.Touch();
        }

        void RefreshLinks()
        {
            foreach (var l in P.Links)
            {
                l.Status = "up"; l.Problem = "";
                if (l.IsConsole) continue;
                // the whole channel through patch panels and wall jacks
                var chain = Chain(l);
                Port ea = chain.Item1, eb = chain.Item2;
                double total = chain.Item3;
                if (ea == null || eb == null)
                {
                    l.Status = "down";
                    l.Problem = "This cable goes into a patch panel or wall jack, but nothing is connected on the other side yet.";
                    continue;
                }
                // modules and cable kinds along the chain
                string why = MediaProblem(l);
                if (why.Length > 0) { l.Status = "fault"; l.Problem = why; continue; }
                if (!l.IsFiber && !chain.Item5)
                {
                    bool cross = chain.Item4;
                    bool need = Wiring.IsMdix(ea) == Wiring.IsMdix(eb);
                    if (cross != need)
                    {
                        l.Status = "fault";
                        l.Problem = need ? "Wrong cable type: " + ea.Dev.KindName + " to " + eb.Dev.KindName + " needs a crossover cable (both ends send on the same wires)."
                                         : "Wrong cable type: " + ea.Dev.KindName + " to " + eb.Dev.KindName + " needs a straight-through cable.";
                        continue;
                    }
                }
                double max = l.IsFiber ? (l.Cable == "fiber-mm" ? 550 : 10000) : 100;
                if (total > max + 0.01)
                {
                    l.Status = "fault";
                    l.Problem = string.Format("The cable run is {0:0} m long. {1} works up to {2:0} m, so the signal is too weak at the far end.", total, l.IsFiber ? (l.Cable == "fiber-mm" ? "Multimode fibre" : "Single-mode fibre") : "Copper network cable", max);
                    continue;
                }
                if (!ea.Dev.PoweredOn || !eb.Dev.PoweredOn) { l.Status = "down"; l.Problem = "A device at one end is switched off."; continue; }
                if (ea.Shutdown || eb.Shutdown)
                {
                    var s = ea.Shutdown ? ea : eb;
                    l.Status = "down";
                    l.Problem = s.FullName + " is shut down (administratively down). On a router, ports start shut down: use  no shutdown  on the interface.";
                }
            }
        }

        /// <summary>The far ends of a cable's channel, its total length, whether it crosses over, and whether it is fibre.</summary>
        public Tuple<Port, Port, double, bool, bool> Chain(Link l)
        {
            double total = 0; bool cross = false; bool fiber = false;
            Func<Port, Port> walk = start =>
            {
                Port cur = start;
                for (int g = 0; g < 16; g++)
                {
                    if (cur.Media != "passive") return cur;
                    var pair = cur.Pair;
                    if (pair == null || pair.Link == null) return null;
                    total += pair.Link.Length;
                    if (pair.Link.Cable == "cross") cross = !cross;
                    if (pair.Link.IsFiber) fiber = true;
                    cur = pair.Link.Other(pair);
                }
                return null;
            };
            total += l.Length;
            if (l.Cable == "cross") cross = !cross;
            if (l.IsFiber) fiber = true;
            var a = walk(l.A);
            var b = walk(l.B);
            return Tuple.Create(a, b, total, cross, fiber);
        }

        static string MediaProblem(Link l)
        {
            foreach (var p in new[] { l.A, l.B })
            {
                if (p.Media != "sfp") continue;
                if (l.IsFiber)
                {
                    if (p.Sfp == "") return p.FullName + " is an empty SFP slot: it needs a fibre module.";
                    if (p.Sfp == "rj45") return p.FullName + " has a copper module, but a fibre cable is plugged in.";
                    if ((l.Cable == "fiber-mm") != (p.Sfp == "mmf")) return "The module in " + p.FullName + " doesn't match the fibre type (multimode and single-mode don't mix).";
                }
                else if (p.Sfp != "rj45") return p.Sfp == "" ? p.FullName + " is an empty SFP slot: copper needs an RJ-45 module." : p.FullName + " has a fibre module: a copper cable doesn't work in it.";
            }
            return "";
        }

        void RefreshWireless()
        {
            var keep = new List<Link>();
            foreach (var d in P.Devices)
            {
                if (d.Host == null || !d.PoweredOn) continue;
                foreach (var port in d.Ports)
                {
                    if (!port.IsWireless) continue;
                    var w = d.Host.WifiFor(port);
                    Link existing = port.Links.Count > 0 ? port.Links[0] : null;
                    WifiAp best = null; double bestSignal = 0; bool wrongKey = false; bool tooFar = false;
                    if (w != null && w.Ssid.Length > 0 && !port.Shutdown)
                    {
                        foreach (var ap in P.Devices)
                        {
                            if (ap.Ap == null || !ap.PoweredOn || !ap.Ap.Enabled || ap.Ap.Ssid != w.Ssid) continue;
                            double s = Physical.Signal(P, ap, d);
                            if (s <= 0) { tooFar = true; continue; }
                            if (ap.Ap.Security != "open" && ap.Ap.Key != w.Key) { wrongKey = true; continue; }
                            if (s > bestSignal) { bestSignal = s; best = ap.Ap; }
                        }
                    }
                    if (best != null)
                    {
                        var radio = best.Radio;
                        if (existing != null && existing.Other(port) == radio) { existing.Signal = bestSignal; keep.Add(existing); w.Status = "connected"; continue; }
                        if (existing != null) Unlink(existing);
                        var l = new Link { A = radio, B = port, Cable = "wireless", Status = "up", Signal = bestSignal, Id = "w" + d.Id + port.Index };
                        radio.Links.Add(l); port.Links.Add(l);
                        keep.Add(l);
                        w.Status = "connected";
                    }
                    else
                    {
                        if (existing != null) Unlink(existing);
                        if (w != null) w.Status = w.Ssid.Length == 0 ? "off" : wrongKey ? "wrong-key" : tooFar ? "no-signal" : "not-found";
                    }
                }
            }
            foreach (var l in Wireless.ToList()) if (!keep.Contains(l)) Unlink(l);
            Wireless.Clear();
            Wireless.AddRange(keep);
        }
        void Unlink(Link l) { l.A.Links.Remove(l); l.B.Links.Remove(l); Wireless.Remove(l); }

        /// <summary>Per-VLAN spanning tree (like PVST+), computed for the whole network at once: lowest bridge ID is the
        /// root, each other switch keeps its cheapest way to the root, and on each loop one port is blocked.</summary>
        public void ComputeStp()
        {
            var bridges = P.Devices.Where(d => d.Bridge != null && d.PoweredOn).ToList();
            foreach (var b in bridges) { foreach (var p in b.Ports) p.StpBlocked.Clear(); b.Bridge.Stp.Clear(); }
            if (!StpEnabled || bridges.Count < 2) return;
            var vlans = new SortedSet<int> { 1 };
            foreach (var b in bridges) foreach (var v in b.Bridge.Vlans.Keys) if (v < 1002) vlans.Add(v);
            // bridge-to-bridge segments
            var segs = new List<Tuple<Port, Port>>();
            foreach (var l in P.Links)
            {
                if (l.Status != "up" || l.IsConsole) continue;
                var ch = Chain(l);
                if (ch.Item1 == null || ch.Item2 == null) continue;
                if (l.A.Media == "passive" && l.B.Media == "passive") continue;
                Port a = l.A.Media == "passive" ? null : l.A, b = l.B.Media == "passive" ? null : l.B;
                if (a == null || b == null) { a = ch.Item1; b = ch.Item2; if (segs.Any(s => (s.Item1 == a && s.Item2 == b) || (s.Item1 == b && s.Item2 == a))) continue; }
                if (a.Dev.Bridge == null || b.Dev.Bridge == null) continue;
                if (!a.Dev.Bridge.Members.Contains(a) || !b.Dev.Bridge.Members.Contains(b)) continue;
                if (a.Shutdown || b.Shutdown) continue;
                segs.Add(Tuple.Create(a, b));
            }
            foreach (int v in vlans)
            {
                var vs = segs.Where(s => s.Item1.CarriesVlan(v) && s.Item2.CarriesVlan(v)).ToList();
                if (vs.Count == 0) continue;
                var nodes = new HashSet<Device>();
                foreach (var s in vs) { nodes.Add(s.Item1.Dev); nodes.Add(s.Item2.Dev); }
                // components, each with its own root
                var unvisited = new HashSet<Device>(nodes);
                while (unvisited.Count > 0)
                {
                    var comp = new HashSet<Device>();
                    var stack = new Stack<Device>();
                    var first = unvisited.First();
                    stack.Push(first); unvisited.Remove(first);
                    while (stack.Count > 0)
                    {
                        var n = stack.Pop(); comp.Add(n);
                        foreach (var s in vs)
                        {
                            Device o = s.Item1.Dev == n ? s.Item2.Dev : s.Item2.Dev == n ? s.Item1.Dev : null;
                            if (o != null && unvisited.Remove(o)) stack.Push(o);
                        }
                    }
                    var cs = vs.Where(s => comp.Contains(s.Item1.Dev)).ToList();
                    Device root = comp.OrderBy(x => x.Bridge.BridgeId(v), StringComparer.Ordinal).First();
                    var cost = new Dictionary<Device, long>();
                    var rootPort = new Dictionary<Device, Port>();
                    foreach (var n in comp) cost[n] = long.MaxValue;
                    cost[root] = 0;
                    // Dijkstra (small graphs): ties broken by the neighbour's bridge ID, then port numbers
                    var done = new HashSet<Device>();
                    while (done.Count < comp.Count)
                    {
                        Device u = null;
                        foreach (var n in comp) if (!done.Contains(n) && cost[n] != long.MaxValue && (u == null || cost[n] < cost[u] || (cost[n] == cost[u] && string.CompareOrdinal(n.Bridge.BridgeId(v), u.Bridge.BridgeId(v)) < 0))) u = n;
                        if (u == null) break;
                        done.Add(u);
                        foreach (var s in cs)
                        {
                            Port mine, theirs;
                            if (s.Item1.Dev == u) { mine = s.Item1; theirs = s.Item2; }
                            else if (s.Item2.Dev == u) { mine = s.Item2; theirs = s.Item1; }
                            else continue;
                            var w = theirs.Dev;
                            if (done.Contains(w)) continue;
                            long c = cost[u] + PortCost(theirs);
                            bool better = c < cost[w];
                            if (!better && c == cost[w] && rootPort.ContainsKey(w))
                            {
                                var cur = rootPort[w];
                                var curUp = cur.Link != null ? Wiring.FarEnd(cur.Link.Other(cur)) : null;
                                string a1 = u.Bridge.BridgeId(v), a2 = curUp != null ? curUp.Dev.Bridge.BridgeId(v) : "~";
                                int cmp = string.CompareOrdinal(a1, a2);
                                if (cmp < 0 || (cmp == 0 && mine.Index < (curUp != null ? curUp.Index : 999)) || (cmp == 0 && curUp != null && mine.Index == curUp.Index && theirs.Index < cur.Index)) better = true;
                            }
                            if (better) { cost[w] = c; rootPort[w] = theirs; }
                        }
                    }
                    foreach (var n in comp)
                    {
                        Port rp; rootPort.TryGetValue(n, out rp);
                        n.Bridge.Stp[v] = new StpInfo { RootId = root.Bridge.BridgeId(v), Cost = cost[n] == long.MaxValue ? 0 : cost[n], RootPort = rp };
                    }
                    // each segment: the side closer to the root (then lower bridge ID, then port) is designated
                    foreach (var s in cs)
                    {
                        Port a = s.Item1, b = s.Item2;
                        bool aRoot = rootPort.ContainsKey(a.Dev) && rootPort[a.Dev] == a;
                        bool bRoot = rootPort.ContainsKey(b.Dev) && rootPort[b.Dev] == b;
                        if (aRoot || bRoot) continue;
                        // neither is a root port: block the side with the worse claim
                        int cmp = cost[a.Dev].CompareTo(cost[b.Dev]);
                        if (cmp == 0) cmp = string.CompareOrdinal(a.Dev.Bridge.BridgeId(v), b.Dev.Bridge.BridgeId(v));
                        if (cmp == 0) cmp = a.Index.CompareTo(b.Index);
                        (cmp < 0 ? b : a).StpBlocked.Add(v);
                    }
                }
            }
        }
        static long PortCost(Port p) { return p.Speed >= 1000 ? 4 : 19; }

        /// <summary>Stops everything in flight (used when a project is replaced).</summary>
        public void Clear() { heap.Clear(); Log.Clear(); }

        public string Clock
        {
            get
            {
                long s = Now / 1000;
                return string.Format("{0:00}:{1:00}:{2:00}", s / 3600, (s / 60) % 60, s % 60);
            }
        }
    }
}
