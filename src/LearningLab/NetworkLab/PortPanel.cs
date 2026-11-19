// Network Lab - a drawn front panel of a device with all its ports (App Hub's PortPanel.cs for Avalonia, touch first):
// names, link lights, plugged cables, SFP slots and their modules. The port picker when cabling in the Physical view,
// the picture on a device's Physical tab and the devices in a rack. Where each port sits is PortLayout (the engine).
// Tap a port to pick it; tap (picture) or hold (picker) an SFP slot to change its module.
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using NetworkLab;
using LearningLab.Views;
using Rect = Avalonia.Rect;

namespace LearningLab.NetworkLab;

public sealed class PortPanel : Control
{
    public readonly Device D;
    /// <summary>Highlight the ports that suit this cable (picking).</summary>
    public string Cable = "auto";
    public bool Picking;
    /// <summary>The picture of a device page: a tap on an SFP slot changes its module.</summary>
    public bool ModuleOnTap;
    public readonly bool ShowBack;
    /// <summary>Drawn bigger than App Hub's, so a finger hits one port.</summary>
    public readonly double Zoom;
    public event Action<Port> PortClicked;
    /// <summary>Something changed (a module): redraw whatever shows it.</summary>
    public event Action Changed;
    Port pressed;
    readonly DispatcherTimer hold;
    bool held;

    public PortPanel(Device d, bool showBack = false, double zoom = 1.3)
    {
        D = d;
        ShowBack = showBack && d.Kind == "patch";
        Zoom = zoom;
        var size = PortLayout.PanelSize(d, ShowBack);
        Width = size.Width * zoom; Height = (size.Height + 14) * zoom;   // room for the names under the second row
        hold = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(550) };
        hold.Tick += (s, e) => { hold.Stop(); if (pressed != null && pressed.Media == "sfp") { held = true; ModuleMenu(pressed, this, () => { InvalidateVisual(); Changed?.Invoke(); }); } };
    }

    static IBrush B(string hex) => NetIcons.B(hex);

    public static string Led(Port p)
    {
        if (!p.Dev.PoweredOn || p.Links.Count == 0) return "#1F2937";
        var l = p.Link;
        if (l == null) return "#1F2937";
        if (l.Status != "up" || p.Shutdown) return l.Status == "fault" ? "#F87171" : "#374151";
        if (p.StpBlocked.Count > 0) return "#FBBF24";
        return "#4ADE80";
    }

    /// <summary>Does a free port take this kind of cable?</summary>
    public static bool Suits(Port p, string cable)
    {
        if (p.IsWireless || p.Links.Count > 0) return false;
        if (cable == "console") return p.IsSerial;
        if (p.IsSerial) return false;
        if (cable == "fiber-mm") return p.Media == "passive" || (p.Media == "sfp" && p.Sfp == "mmf");
        if (cable == "fiber-sm") return p.Media == "passive" || (p.Media == "sfp" && p.Sfp == "smf");
        if (cable == "auto") return true;
        return p.Media != "sfp" || p.Sfp == "rj45";
    }

    /// <summary>Where a port is on this control (its middle), for taps and the checks.</summary>
    public Point PortCenter(Port p)
    {
        foreach (var kv in PortLayout.Layout(D, ShowBack))
            if (kv.Key == p) return new Point((kv.Value.X + kv.Value.Width / 2) * Zoom, (kv.Value.Y + kv.Value.Height / 2) * Zoom);
        return new Point(-100, -100);
    }

    public Port Hit(Point s)
    {
        double x = s.X / Zoom, y = s.Y / Zoom;
        Port best = null; double bd = double.MaxValue;
        foreach (var kv in PortLayout.Layout(D, ShowBack))
        {
            var r = kv.Value;
            if (x < r.X - 8 || x > r.Right + 8 || y < r.Y - 10 || y > r.Bottom + 14) continue;
            double cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2, d = (x - cx) * (x - cx) + (y - cy) * (y - cy);
            if (d < bd) { bd = d; best = kv.Key; }
        }
        return best;
    }

    public override void Render(DrawingContext dc)
    {
        using (dc.PushTransform(Matrix.CreateScale(Zoom, Zoom)))
            Draw(dc, D, ShowBack, new Size(Width / Zoom, Height / Zoom), Picking, Cable, pressed);
    }

    /// <summary>Draws a device's front panel at 0, 0 in its PanelSize (also used for racks).</summary>
    public static void Draw(DrawingContext dc, Device d, bool showBack, Size size, bool picking, string cable, Port highlight)
    {
        double w = size.Width, h = size.Height;
        var body = new Rect(4, 4, w - 8, h - 8);
        string face = d.Kind == "switch" ? "#1E3A8A" : d.Kind == "router" ? "#155E75" : d.Kind == "patch" ? "#374151" : d.Kind == "homerouter" ? "#E2E8F0" : d.Kind == "cloud" ? "#1E293B" : "#334155";
        var grad = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative) };
        grad.GradientStops.Add(new GradientStop(Color.Parse(face), 0)); grad.GradientStops.Add(new GradientStop(Color.Parse("#0F172A"), 1));
        dc.DrawRectangle(grad, new Pen(B("#64748B"), 1.5), body, 6, 6);
        bool lightFace = d.Kind == "homerouter";
        var label = DrawArt.Fmt(d.Model + "  -  " + d.Name, 12, B(lightFace ? "#0F172A" : "#E2E8F0"), true);
        dc.DrawText(label, new Point(14, 10));
        dc.DrawEllipse(B(d.PoweredOn ? "#4ADE80" : "#374151"), null, new Point(w - 22, 18), 5, 5);
        if (showBack)
        {
            double my = 36 + PortLayout.BackOffset - 20;
            dc.DrawLine(new Pen(B("#64748B"), 1), new Point(10, my - 3), new Point(w - 10, my - 3));
            var fl = DrawArt.Fmt("Front (P): short cables to the switch", 9.5, B("#94A3B8"), false);
            dc.DrawText(fl, new Point(w - 36 - fl.Width, 24));
            var bl = DrawArt.Fmt("Back (B): the cables from the wall jacks. P5 and B5 are the same line.", 9.5, B("#94A3B8"), false);
            dc.DrawText(bl, new Point(14, my));
        }
        foreach (var kv in PortLayout.Layout(d, showBack))
        {
            var p = kv.Key;
            var r = new Rect(kv.Value.X, kv.Value.Y, kv.Value.Width, kv.Value.Height);
            bool sfp = p.Media == "sfp";
            string fill = p.IsSerial ? "#0EA5E9" : sfp ? (p.Sfp == "" ? "#0B1220" : p.Sfp == "rj45" ? "#111827" : p.Sfp == "mmf" ? "#C2410C" : "#A16207") : "#0B1220";
            bool can = picking && Suits(p, cable);
            using (picking && !can ? dc.PushOpacity(0.45) : default(DrawingContext.PushedState?))
            {
                var pen = new Pen(B(p == highlight ? "#FFFFFF" : can ? "#60A5FA" : "#94A3B8"), p == highlight || can ? 2 : 1);
                dc.DrawRectangle(B(fill), pen, r, 3, 3);
                if (sfp && p.Sfp == "") dc.DrawText(DrawArt.Fmt("SFP", 8.5, B("#64748B"), true), new Point(r.X + 5, r.Y + 6));
                if (p.Links.Count > 0)
                {
                    var l = p.Link;
                    string plug = l.IsFiber ? (l.Cable == "fiber-mm" ? "#FB923C" : "#FACC15") : l.IsConsole ? "#7DD3FC" : "#60A5FA";
                    dc.DrawRectangle(B(plug), null, new Rect(r.X + 5, r.Y + 5, r.Width - 10, r.Height - 10), 2, 2);
                }
                if (!p.IsSerial) dc.DrawRectangle(B(Led(p)), null, new Rect(r.X + r.Width / 2 - 4, r.Y - 5, 8, 3));
                var t = DrawArt.Fmt(p.Short.Replace("FastEthernet", "Fa"), 9.5, B(lightFace ? "#0F172A" : "#CBD5E1"), false);
                dc.DrawText(t, new Point(r.X + r.Width / 2 - t.Width / 2, r.Bottom + 2));
            }
        }
    }

    public static string PortTip(Port p)
    {
        string s = p.Name;
        if (p.Media == "sfp") s += "  (SFP slot: " + (p.Sfp == "" ? "empty" : p.Sfp == "rj45" ? "copper module" : p.Sfp == "mmf" ? "multimode fibre module" : "single-mode fibre module") + ")";
        else if (p.IsSerial) s += "  (console: needs a console cable)";
        else if (p.Media == "passive") s += "  (patch point)";
        else s += "  (copper, " + (p.Speed >= 1000 ? "1 Gb/s" : "100 Mb/s") + ")";
        if (p.Link != null) { var l = p.Link; var o = l.Other(p); s += "\nCable to " + o.FullName + ": " + l.CableName + ", " + l.Length.ToString("0.#", CultureInfo.InvariantCulture) + " m, " + (l.Status == "up" ? "working" : l.Problem); }
        else s += "\nFree";
        return s;
    }

    // ------------------------------------------------------------ touch and mouse
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var s = e.GetPosition(this);
        pressed = Hit(s);
        held = false;
        if (pressed == null) return;
        e.Handled = true;
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed) { var p = pressed; pressed = null; if (p.Media == "sfp") ModuleMenu(p, this, () => { InvalidateVisual(); Changed?.Invoke(); }); return; }
        hold.Start();
        ToolTip.SetTip(this, PortTip(pressed));
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        hold.Stop();
        var p = pressed; pressed = null;
        InvalidateVisual();
        if (p == null || held) return;
        if (Hit(e.GetPosition(this)) != p) return;
        e.Handled = true;
        TapAt(PortCenter(p));
    }

    /// <summary>A tap on the panel (the same path as a finger): picks the port, or changes an SFP slot's module.</summary>
    public Port TapAt(Point s)
    {
        var p = Hit(s);
        if (p == null) return null;
        if (ModuleOnTap && p.Media == "sfp") { ModuleMenu(p, this, () => { InvalidateVisual(); Changed?.Invoke(); }); return p; }
        if (Picking && !Suits(p, Cable)) return null;
        PortClicked?.Invoke(p);
        return p;
    }

    /// <summary>For the checks: the module picked from the menu (no menu is shown then).</summary>
    public static Func<Port, string> AutoModule;

    public static readonly string[][] Modules =
    {
        new[] { "", "Empty (no module)" }, new[] { "rj45", "Copper (RJ-45) module" },
        new[] { "mmf", "Multimode fibre module (up to 550 m)" }, new[] { "smf", "Single-mode fibre module (up to 10 km)" },
    };

    /// <summary>Puts a module into an SFP slot ("" = take it out).</summary>
    public static void SetModule(Port p, string id)
    {
        if (p.Media != "sfp" || p.Sfp == id) return;
        p.Sfp = id;
        p.Dev.P.Sim.TopologyChanged();
        p.Dev.P.Touch();
    }

    /// <summary>A menu to put a module into an SFP slot.</summary>
    public static void ModuleMenu(Port p, Control target, Action changed)
    {
        if (p.Media != "sfp") return;
        if (AutoModule != null) { SetModule(p, AutoModule(p)); changed?.Invoke(); return; }
        if (Data.Testing) return;
        var menu = new MenuFlyout();
        menu.Items.Add(new MenuItem { Header = p.Name + ": which module?", IsEnabled = false });
        foreach (var m in Modules)
        {
            var mm = m;
            var mi = new MenuItem { Header = (p.Sfp == m[0] ? "✓ " : "") + m[1], MinHeight = 44 };
            mi.Click += (s, e) => { SetModule(p, mm[0]); changed?.Invoke(); };
            menu.Items.Add(mi);
        }
        menu.ShowAt(target, true);
    }

    /// <summary>The port picker in a flyout by the view; calls back with the chosen port. Returns the panel (the checks
    /// tap it with TapAt; the flyout isn't shown in the checks).</summary>
    public static PortPanel Pick(Device d, string cable, Control anchor, string title, Action<Port> chosen)
    {
        var panel = new PortPanel(d, true) { Cable = cable, Picking = true };
        var fl = new Flyout { Placement = PlacementMode.Pointer };
        var head = Ui.Text(title, 15, Ui.TextC, true);
        var hint = Ui.Text(d.Kind == "patch"
            ? "Bright ports suit this cable. Cables from wall jacks go in the back (B); the short cable to the switch goes in the front port with the same number (P)."
            : "Bright ports suit this cable." + (d.Ports.Any(x => x.Media == "sfp") ? " Hold an SFP slot to plug in a module." : ""), 12.5, Ui.Muted);
        var close = Ui.Button("Cancel", false, 14);
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.Click += (s, e) => fl.Hide();
        var scroll = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var col = new StackPanel { Spacing = 8, Children = { head, scroll, hint, close } };
        double avail = TopLevel.GetTopLevel(anchor)?.Bounds.Width ?? 800;
        col.MaxWidth = Math.Max(280, Math.Min(panel.Width + 8, avail - 48));
        hint.MaxWidth = col.MaxWidth;
        fl.Content = col;
        panel.PortClicked += p => { fl.Hide(); chosen(p); };
        if (!Data.Testing && anchor != null) fl.ShowAt(anchor, true);
        return panel;
    }
}
