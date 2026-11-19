// Network Lab - layer 2 switching: MAC learning, flooding, VLANs (access and 802.1Q trunks), the switch's own
// address (an SVI such as interface vlan 1), Wi-Fi bridging on access points.
using System;
using System.Collections.Generic;
using System.Linq;

namespace NetworkLab
{
    public class MacEntry { public int Vlan; public string Mac; public Port Port; public long Time; public bool Static; }

    public class StpInfo { public string RootId = ""; public long Cost; public Port RootPort; }

    public class Bridge
    {
        public readonly Dictionary<int, StpInfo> Stp = new Dictionary<int, StpInfo>();
        public readonly Device D;
        public readonly List<Port> Members = new List<Port>();
        public readonly Dictionary<string, MacEntry> Table = new Dictionary<string, MacEntry>();
        public readonly SortedDictionary<int, string> Vlans = new SortedDictionary<int, string>();
        public readonly Dictionary<int, IpIf> Svis = new Dictionary<int, IpIf>();
        public readonly Dictionary<int, int> StpPriority = new Dictionary<int, int>();
        public long AgingMs = 300000;
        public string BaseMac;

        public Bridge(Device d)
        {
            D = d;
            Vlans[1] = "default";
            if (d.Kind == "switch") { Vlans[1002] = "fddi-default"; Vlans[1003] = "token-ring-default"; Vlans[1004] = "fddinet-default"; Vlans[1005] = "trnet-default"; }
        }

        public string Mac0 { get { if (BaseMac == null) BaseMac = Mac.Next(D.P.Rng); return BaseMac; } }

        public string BridgeId(int vlan)
        {
            int pr;
            if (!StpPriority.TryGetValue(vlan, out pr)) pr = 32768;
            return (pr + vlan).ToString("D5") + "." + Mac0;
        }
        public int Priority(int vlan) { int pr; if (!StpPriority.TryGetValue(vlan, out pr)) pr = 32768; return pr; }

        public IpIf AddSvi(int vlan, bool shutdown)
        {
            IpIf f;
            if (Svis.TryGetValue(vlan, out f)) return f;
            f = new IpIf { Dev = D, Name = "Vlan" + vlan, Vlan = vlan, IsSvi = true, Bridge = this, Mac = Mac.Next(D.P.Rng), Shutdown = shutdown };
            Svis[vlan] = f;
            D.Ip.Ifs.Add(f);
            return f;
        }

        Sim S { get { return D.P.Sim; } }

        public void Receive(Port inPort, Frame f)
        {
            int vlan;
            if (inPort.Trunk)
            {
                vlan = f.Vlan == 0 ? inPort.NativeVlan : f.Vlan;
                if (!VlanList.Allows(inPort.Allowed, vlan)) { S.Note("VLAN " + vlan + " isn't allowed on trunk " + inPort.Short + ", so the frame is dropped."); return; }
            }
            else
            {
                if (f.Vlan != 0) { S.Note(inPort.Short + " is an access port but the frame has a VLAN tag (" + f.Vlan + "): dropped. Is the other end a trunk?"); return; }
                vlan = inPort.AccessVlan;
            }
            if (inPort.StpBlocked.Contains(vlan)) { S.Note(inPort.Short + " is blocked by spanning tree (it would make a loop), so the frame is dropped."); return; }
            if (D.Kind == "switch" && !Vlans.ContainsKey(vlan)) { S.Note("VLAN " + vlan + " doesn't exist on " + D.Name + " (create it with  vlan " + vlan + "), so the frame is dropped."); return; }
            if (++f.L2Hops > 48) { S.Note("This frame has been passed around too many times (a loop?), so it is dropped."); return; }
            f.Vlan = 0;
            // learn
            string key = vlan + "|" + f.Src;
            MacEntry e;
            if (!Table.TryGetValue(key, out e) || e.Port != inPort)
            {
                if (e == null || !e.Static)
                {
                    Table[key] = new MacEntry { Vlan = vlan, Mac = f.Src, Port = inPort, Time = S.Now };
                    S.Note(D.Name + " learned that " + f.Src + " is on " + inPort.Short + (vlan != 1 || inPort.Trunk ? " (VLAN " + vlan + ")" : "") + ".");
                }
            }
            else e.Time = S.Now;
            // for the device itself (its SVI)?
            IpIf svi;
            Svis.TryGetValue(vlan, out svi);
            bool flood = Mac.IsMulticast(f.Dst);
            if (svi != null && !svi.Shutdown && (f.Dst == svi.Mac || flood))
            {
                if (svi.Ip != 0 || svi.Dhcp || D.Kind != "switch")
                {
                    if (!flood) { S.Note("The frame is for " + D.Name + " itself (" + svi.Name + ")."); D.Ip.ReceiveOnIf(svi, flood ? f.Clone() : f); return; }
                    D.Ip.ReceiveOnIf(svi, f.Clone());
                }
                else if (!flood) return;
            }
            // forward
            if (!flood)
            {
                MacEntry dst;
                if (Table.TryGetValue(vlan + "|" + f.Dst, out dst) && S.Now - dst.Time <= AgingMs)
                {
                    if (dst.Port == inPort && !inPort.IsWireless) { S.Note(f.Dst + " is on the port it came from, so " + D.Name + " doesn't send it anywhere (filtered)."); return; }
                    if (Forwarding(dst.Port, vlan))
                    {
                        S.Note(D.Name + " knows " + f.Dst + " is on " + dst.Port.Short + ": sent out " + dst.Port.Short + " only.");
                        SendOut(dst.Port, f, vlan, inPort);
                        return;
                    }
                }
            }
            var outs = Members.Where(p => (p != inPort || p.IsWireless) && Forwarding(p, vlan)).ToList();
            if (flood) S.Note((f.Dst == Mac.Broadcast ? "A broadcast" : "A multicast") + ": " + D.Name + " sends a copy out of every other port in VLAN " + vlan + (outs.Count > 0 ? " (" + string.Join(", ", outs.Select(p => p.Short).ToArray()) + ")." : " (there are none)."));
            else S.Note(D.Name + " doesn't know where " + f.Dst + " is yet, so it floods it out of every other port in VLAN " + vlan + (outs.Count > 0 ? " (" + string.Join(", ", outs.Select(p => p.Short).ToArray()) + ")." : "."));
            foreach (var p in outs) SendOut(p, f.Clone(), vlan, inPort);
        }

        bool Forwarding(Port p, int vlan)
        {
            if (!p.IsUp || p.Shutdown) return false;
            if (!p.CarriesVlan(vlan)) return false;
            if (p.StpBlocked.Contains(vlan)) return false;
            return true;
        }

        void SendOut(Port p, Frame f, int vlan, Port inPort)
        {
            f.Vlan = p.Trunk && vlan != p.NativeVlan ? vlan : 0;
            S.Transmit(p, f, inPort != null && inPort.IsWireless && p == inPort ? FromStation(inPort, f) : null);
        }
        Port FromStation(Port radio, Frame f)
        {
            foreach (var l in radio.Links) { var o = l.Other(radio); if (o.Mac == f.Src) return o; }
            return null;
        }

        /// <summary>A frame the device itself sends from one of its SVIs.</summary>
        public void SendFromSvi(IpIf svi, Frame f)
        {
            int vlan = svi.Vlan;
            f.Src = svi.Mac;
            if (!Mac.IsMulticast(f.Dst))
            {
                MacEntry dst;
                if (Table.TryGetValue(vlan + "|" + f.Dst, out dst) && Forwarding(dst.Port, vlan)) { SendOut(dst.Port, f, vlan, null); return; }
            }
            foreach (var p in Members.Where(p => Forwarding(p, vlan)).ToList()) SendOut(p, f.Clone(), vlan, null);
        }

        public void PortChanged(Port p, bool up)
        {
            if (up) return;
            foreach (var k in Table.Where(kv => kv.Value.Port == p && !kv.Value.Static).Select(kv => kv.Key).ToList()) Table.Remove(k);
        }

        public List<MacEntry> Entries()
        {
            long now = S.Now;
            foreach (var k in Table.Where(kv => !kv.Value.Static && now - kv.Value.Time > AgingMs).Select(kv => kv.Key).ToList()) Table.Remove(k);
            return Table.Values.OrderBy(x => x.Vlan).ThenBy(x => x.Mac).ToList();
        }

        public void ClearDynamic() { foreach (var k in Table.Where(kv => !kv.Value.Static).Select(kv => kv.Key).ToList()) Table.Remove(k); }

        /// <summary>Is any member port of this VLAN up (an SVI is up only then)?</summary>
        public bool VlanUp(int vlan)
        {
            if (D.Kind == "switch" && !Vlans.ContainsKey(vlan)) return false;
            foreach (var p in Members) if (p.IsUp && p.CarriesVlan(vlan)) return true;
            return D.Kind != "switch";   // a home router's LAN or an AP stays up
        }
    }
}
