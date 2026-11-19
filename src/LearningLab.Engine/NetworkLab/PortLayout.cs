// Network Lab in Learning Lab: where each port sits on a device's drawn front panel, and which ports a cable takes when
// none is picked. Moved out of App Hub's PortPanel.cs and TopologyView.cs (WPF) so the engine, the checks and both
// platforms' screens share them. PanelSize returns a Rect at 0, 0 (Width, Height).
using System;
using System.Collections.Generic;
using System.Linq;

namespace NetworkLab
{
    public static class PortLayout
    {
        public const double BackOffset = 108;

        public static List<KeyValuePair<Port, Rect>> Layout(Device d) { return Layout(d, false); }

        /// <summary>Where each port sits on the panel: copper ports in one or two rows, then the SFP slots and console.
        /// A patch panel with <paramref name="back"/> gets a second block below for its back ports (B1-B24).</summary>
        public static List<KeyValuePair<Port, Rect>> Layout(Device d, bool back)
        {
            var list = new List<KeyValuePair<Port, Rect>>();
            if (d.Kind == "patch" && back)
            {
                list.AddRange(Layout(d, false));
                foreach (var kv in Layout(d, false))
                {
                    var bp = kv.Key.Pair;
                    if (bp != null) list.Add(new KeyValuePair<Port, Rect>(bp, new Rect(kv.Value.X, kv.Value.Y + BackOffset, kv.Value.Width, kv.Value.Height)));
                }
                return list;
            }
            var ports = d.Ports.Where(p => !p.IsWireless).ToList();
            if (d.Kind == "patch") ports = ports.Where(p => p.Name.StartsWith("Port")).ToList();
            var copper = ports.Where(p => p.Media == "copper" || p.Media == "passive").ToList();
            var other = ports.Where(p => !(p.Media == "copper" || p.Media == "passive")).ToList();
            double pw = 30, ph = 24, x = 14, y = 36;
            bool two = copper.Count > 12;
            int cols = two ? (copper.Count + 1) / 2 : copper.Count;
            for (int i = 0; i < copper.Count; i++)
            {
                int col = two ? i / 2 : i, row = two ? i % 2 : 0;
                double px = x + col * (pw + (two ? 6 : 22)) + (two ? (col / 6) * 10 : 0);
                list.Add(new KeyValuePair<Port, Rect>(copper[i], new Rect(px, y + row * (ph + 20) + (two ? 0 : 6), pw, ph)));
            }
            double ox = x + cols * (pw + (two ? 6 : 22)) + (two ? (cols / 6) * 10 : 0) + 24;
            foreach (var p in other)
            {
                double ww = p.IsSerial ? pw + 16 : pw + 4;
                list.Add(new KeyValuePair<Port, Rect>(p, new Rect(ox, y + (two ? 10 : 6), ww, ph)));
                ox += ww + 22;
            }
            return list;
        }

        public static Rect PanelSize(Device d) { return PanelSize(d, false); }
        public static Rect PanelSize(Device d, bool back)
        {
            back = back && d.Kind == "patch";
            var l = Layout(d, false);
            double right = l.Count == 0 ? 200 : l.Max(kv => kv.Value.Right);
            double h = l.Any(kv => kv.Value.Y > 50) ? 110 : 96;
            return new Rect(0, 0, Math.Max(back ? 330 : 260, right + 24), back ? h + BackOffset + 12 : h);
        }

        /// <summary>The ports a cable between two devices takes when none is picked: matching fibre modules first, then copper.</summary>
        public static bool AutoPorts(Device a, Device b, out Port pa, out Port pb)
        {
            pa = null; pb = null;
            // matching fibre modules first, then copper
            foreach (var x in a.Ports.Where(p => p.Media == "sfp" && (p.Sfp == "mmf" || p.Sfp == "smf") && p.Links.Count == 0))
            {
                var y = b.Ports.FirstOrDefault(p => p.Media == "sfp" && p.Sfp == x.Sfp && p.Links.Count == 0);
                if (y != null) { pa = x; pb = y; return true; }
            }
            pa = Project.FreePort(a, b); pb = Project.FreePort(b, a);
            if (a.Kind == "patch" && pa != null) { }
            return pa != null && pb != null;
        }
    }
}
