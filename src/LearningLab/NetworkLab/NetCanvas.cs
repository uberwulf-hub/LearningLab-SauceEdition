// Network Lab - the logical view (App Hub's TopologyView for Avalonia, touch first): the devices, the cables and their
// link lights (green working, amber blocked by spanning tree, red down), IP addresses under the devices, notes, and the
// envelopes of Simulation mode. Drag a device to move it; drag empty space to pan; pinch or the mouse wheel to zoom;
// tap a device to open it; hold (or right-click) for its menu. Tools: select, connect (a cable: tap one device, then
// the other; the ports are picked for you, or pick them from the menu), delete.
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using NetworkLab;
using LearningLab.Views;
using Rect = Avalonia.Rect;

namespace LearningLab.NetworkLab;

public sealed class NetCanvas : Control, IDrawHost
{
    /// <summary>The drawing tools (shapes, labels, the map) on the Logical view.</summary>
    public readonly DrawTool Draw;
    public bool ShowVlans;
    public event Action SelectionChanged;
    public event Action<string> ToolChanged;
    public Project P;
    public UndoStack Undo;
    public double Zoom = 1;
    public Vector Pan = new(40, 30);
    /// <summary>select, connect or delete.</summary>
    public string Tool = "select";
    public bool ShowAddresses = true, ShowPorts;
    public readonly HashSet<object> Selection = new();
    public List<SimEvent> Envelopes = new();
    public double EnvProgress = 1;
    public SimEvent Picked;

    public event Action<Device> DeviceOpened;
    public event Action<string> Status;
    public event Action Edited;
    public event Action<SimEvent> EnvelopeClicked;
    /// <summary>The device and the place a menu was asked for (hold or right-click).</summary>
    public event Action<object, Point> MenuAsked;

    const double IconW = 58, IconH = 46;
    Device connectFrom;
    Point mouse;
    /// <summary>A cable started from a port picked in a device's menu: the next device tapped asks which of its ports.</summary>
    public Port PortFrom;
    public event Action<Device, Point> PortAsked;
    /// <summary>The envelope tool: a ping from the first device tapped to the second.</summary>
    public event Action<Device, Device> PduRequested;
    Device pduFrom;

    // ------------------------------------------------------------ what the drawing tools need
    Project IDrawHost.DrawProject => P;
    UndoStack IDrawHost.DrawUndo => Undo;
    string IDrawHost.DrawView => "logical";
    string IDrawHost.CalloutView => "logical";
    double IDrawHost.DrawScale => Zoom;
    double IDrawHost.DrawBase => 1;
    Point IDrawHost.DrawToScreen(double x, double y) => ToScreen(x, y);
    Point IDrawHost.DrawToView(Point s) => ToWorld(s);
    HashSet<object> IDrawHost.DrawSelection => Selection;
    bool IDrawHost.CalloutAnchor(Callout c, out Point s)
    {
        s = default;
        var d = P.Devices.FirstOrDefault(x => x.Id == c.Target);
        if (d != null) { s = Center(d); return true; }
        var l = P.Links.FirstOrDefault(x => x.Id == c.Target);
        if (l != null) { Point a = Center(l.A.Dev), b = Center(l.B.Dev); s = new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2); return true; }
        return false;
    }
    object IDrawHost.DrawTargetAt(Point s) => (object)HitDevice(s) ?? HitLink(s);
    void IDrawHost.DrawRedraw() => InvalidateVisual();
    void IDrawHost.DrawSay(string m) => Say(m);
    void IDrawHost.DrawEdited() => Edited?.Invoke();
    void IDrawHost.DrawSelected() => SelectionChanged?.Invoke();
    void IDrawHost.DrawToolDone() { Tool = "select"; ToolChanged?.Invoke("select"); }
    Control IDrawHost.DrawControl => this;

    public NetCanvas()
    {
        Draw = new DrawTool(this);
        ClipToBounds = true;
        Focusable = true;
        longPress = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(550) };
        longPress.Tick += (s, e) => { longPress.Stop(); if (downOn != null && !moved) { var o = downOn; downOn = null; dragging = false; MenuAsked?.Invoke(o, downAt); } };
    }

    void Say(string m) => Status?.Invoke(m);

    public Point ToScreen(double x, double y) => new(x * Zoom + Pan.X, y * Zoom + Pan.Y);
    public Point ToWorld(Point s) => new((s.X - Pan.X) / Zoom, (s.Y - Pan.Y) / Zoom);
    public Point Center(Device d) => ToScreen(d.X, d.Y);

    static IBrush B(string hex) => NetIcons.B(hex);
    static readonly Typeface Face = new(FontFamily.Default), Bold = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);
    static FormattedText Fmt(string t, double size, IBrush b, bool bold) => new(t ?? "", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, bold ? Bold : Face, size, b);

    // ------------------------------------------------------------ drawing
    public override void Render(DrawingContext dc)
    {
        double W = Bounds.Width, H = Bounds.Height;
        dc.FillRectangle(B("#0D131D"), new Rect(0, 0, W, H));
        if (P == null) return;
        if (Draw.HasMap) Draw.RenderMap(dc, new Rect(0, 0, W, H));
        else if (Zoom > 0.35)
        {
            var dot = B("#1E293B");
            double step = 40 * Zoom;
            double ox = ((Pan.X % step) + step) % step, oy = ((Pan.Y % step) + step) % step;
            for (double x = ox; x < W; x += step)
                for (double y = oy; y < H; y += step)
                    dc.FillRectangle(dot, new Rect(x, y, 2, 2));
        }
        Draw.RenderBelow(dc);
        if (Draw.LayerShows("drawings")) foreach (var n in P.Notes.Where(n => n.View == "logical")) DrawNote(dc, n);
        if (Draw.LayerShows("cables"))
        {
            foreach (var l in P.Links) DrawLink(dc, l);
            foreach (var l in P.Sim.Wireless) DrawLink(dc, l);
        }
        if (Draw.LayerShows("devices")) foreach (var d in P.Devices) DrawDevice(dc, d);
        Draw.RenderAbove(dc);
        Draw.RenderLabels(dc);
        DrawScaleBar(dc);
        DrawEnvelopes(dc);
        Draw.RenderOverlay(dc);
        if (pduFrom != null) dc.DrawLine(new Pen(B("#F472B6"), 2) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) }, Center(pduFrom), mouse);
        var from = connectFrom ?? PortFrom?.Dev;
        if (from != null)
            dc.DrawLine(new Pen(B("#93C5FD"), 2) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) }, Center(from), mouse);
        if (P.Devices.Count == 0)
        {
            var t = Fmt("An empty network. Add devices with + Add, then connect them with the Cable tool.", 15, B("#64748B"), false);
            t.MaxTextWidth = Math.Max(200, W - 60);
            dc.DrawText(t, new Point((W - t.Width) / 2, H / 2 - t.Height / 2));
        }
    }

    static string CableColor(Link l) => l.Cable switch
    {
        "fiber-mm" => "#FB923C",
        "fiber-sm" => "#FACC15",
        "console" => "#7DD3FC",
        "wireless" => "#38BDF8",
        _ => "#CBD5E1",
    };

    public static string LightColor(Link l, Port p)
    {
        if (l.Status != "up") return "#F87171";
        if (p.StpBlocked.Count > 0 && !p.IsWireless && p.Dev.Bridge != null && p.Ports().All(v => p.StpBlocked.Contains(v))) return "#FBBF24";
        if (p.Shutdown) return "#F87171";
        return "#4ADE80";
    }

    void DrawLink(DrawingContext dc, Link l)
    {
        Point a = Center(l.A.Dev), b = Center(l.B.Dev);
        double th = (Selection.Contains(l) ? 3.5 : 2) * Math.Max(0.6, Zoom);
        DashStyle dash = l.Cable == "cross" ? new DashStyle(new double[] { 4, 2 }, 0) : l.IsWireless ? new DashStyle(new double[] { 2, 3 }, 0) : l.IsConsole ? new DashStyle(new double[] { 1, 2 }, 0) : null;
        if (l.IsWireless) th = 1.5;
        if (Selection.Contains(l)) dc.DrawLine(new Pen(B("#3B82F6"), th + 4), a, b);
        dc.DrawLine(new Pen(B(CableColor(l)), th) { DashStyle = dash }, a, b);
        double vx = b.X - a.X, vy = b.Y - a.Y, len = Math.Sqrt(vx * vx + vy * vy);
        if (len < 1) return;
        vx /= len; vy /= len;
        double off = Math.Min(len / 2 - 4, 36 * Zoom);
        foreach (var (port, at, sign) in new[] { (l.A, a, 1.0), (l.B, b, -1.0) })
        {
            var pt = new Point(at.X + vx * off * sign, at.Y + vy * off * sign);
            double r = 4.5 * Math.Max(0.7, Zoom);
            dc.DrawEllipse(B(LightColor(l, port)), new Pen(B("#0B1220"), 1), pt, r, r);
            if (ShowPorts && Zoom >= 0.8 && !l.IsWireless && Draw.LayerShows("labels"))
            {
                var t = Fmt(port.Short, 10.5 * Zoom, B("#94A3B8"), false);
                var tp = new Point(at.X + vx * (off + 12 * Zoom) * sign, at.Y + vy * (off + 12 * Zoom) * sign);
                dc.DrawText(t, new Point(tp.X - t.Width / 2, tp.Y - t.Height - 2));
            }
        }
        if (ShowVlans && Zoom >= 0.5 && Draw.LayerShows("labels"))
        {
            string vt = Drawings.VlanText(l);
            if (vt.Length > 0)
            {
                var mid = new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);
                var t = Fmt(vt, Math.Max(8, 10.5 * Zoom), B("#C4B5FD"), true);
                var r = new Rect(mid.X - t.Width / 2 - 4, mid.Y + 6 * Zoom, t.Width + 8, t.Height + 2);
                dc.DrawRectangle(DrawArt.Alpha("#1E1B4B", 200), new Pen(B("#6D28D9"), 1), r, 3, 3);
                dc.DrawText(t, new Point(r.X + 4, r.Y + 1));
            }
        }
        if (l.Status == "fault")
        {
            var mid = new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);
            var t = Fmt("!", 13 * Zoom, B("#F87171"), true);
            dc.DrawEllipse(B("#3F1D1D"), new Pen(B("#F87171"), 1), mid, 9 * Zoom, 9 * Zoom);
            dc.DrawText(t, new Point(mid.X - t.Width / 2, mid.Y - t.Height / 2));
        }
    }

    public Rect IconRect(Device d)
    {
        var c = Center(d);
        double w = IconW * Zoom, h = IconH * Zoom;
        if (d.IsPassive) { w *= 0.8; h *= 0.6; }
        return new Rect(c.X - w / 2, c.Y - h / 2, w, h);
    }

    void DrawDevice(DrawingContext dc, Device d)
    {
        var r = IconRect(d);
        if (Selection.Contains(d)) dc.DrawRectangle(B("#3C3B82F6"), new Pen(B("#3B82F6"), 1.5), r.Inflate(4), 8, 8);
        using (d.PoweredOn ? default(DrawingContext.PushedState?) : dc.PushOpacity(0.45))
            NetIcons.Draw(dc, d.Kind, d.Os, r, d.PoweredOn);
        double fs = Math.Max(8, 12 * Zoom);
        var name = Fmt(d.Name, fs, B("#E2E8F0"), true);
        var c = Center(d);
        double ty = r.Bottom + 2;
        dc.DrawRectangle(B("#A00D131D"), null, new Rect(c.X - name.Width / 2 - 3, ty - 1, name.Width + 6, name.Height + 2), 3, 3);
        dc.DrawText(name, new Point(c.X - name.Width / 2, ty));
        ty += name.Height;
        if (ShowAddresses && Zoom >= 0.6 && d.Ip != null && Draw.LayerShows("labels"))
        {
            var ips = Addresses(d);
            if (ips.Count > 0)
            {
                var t = Fmt(string.Join("  ", ips), Math.Max(7.5, 10.5 * Zoom), B("#7DD3FC"), false);
                dc.DrawText(t, new Point(c.X - t.Width / 2, ty));
            }
        }
    }

    public static List<string> Addresses(Device d)
    {
        var ips = new List<string>();
        if (d.Host != null) { foreach (var f in d.Host.Adapters) if (f.Ip != 0) ips.Add(Ip.Str(f.Ip)); }
        else if (d.Kind == "homerouter") { var f = HomeRouter.Lan(d); if (f.Ip != 0) ips.Add(Ip.Str(f.Ip)); }
        else if (d.Ip != null) foreach (var f in d.Ip.Ifs) if (f.Ip != 0 && !f.IsLoopback && d.Kind != "cloud") ips.Add(Ip.Str(f.Ip));
        if (ips.Count > 2) ips = new List<string> { ips[0], ips[1], "+" + (ips.Count - 2) };
        return ips;
    }

    /// <summary>Once the map's scale is set: a bar of a round number of metres.</summary>
    void DrawScaleBar(DrawingContext dc)
    {
        if (!P.Maps.TryGetValue("logical", out var m) || m.MetresPerUnit <= 0) return;
        double pxPerM = Zoom / m.MetresPerUnit;
        double want = 120 / pxPerM, nice = 1;
        foreach (var v in new double[] { 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 2000, 5000, 10000 }) if (v <= want) nice = v;
        double len = nice * pxPerM;
        var s0 = new Point(14, Bounds.Height - 22);
        dc.DrawLine(new Pen(B("#94A3B8"), 2), s0, new Point(s0.X + len, s0.Y));
        dc.DrawLine(new Pen(B("#94A3B8"), 2), s0, new Point(s0.X, s0.Y - 6));
        dc.DrawLine(new Pen(B("#94A3B8"), 2), new Point(s0.X + len, s0.Y), new Point(s0.X + len, s0.Y - 6));
        var t = Fmt(nice >= 1000 ? (nice / 1000).ToString("0.#", CultureInfo.InvariantCulture) + " km" : nice.ToString("0", CultureInfo.InvariantCulture) + " m", 11, B("#94A3B8"), false);
        dc.DrawText(t, new Point(s0.X + len + 6, s0.Y - 9));
    }

    void DrawNote(DrawingContext dc, Note n)
    {
        var p0 = ToScreen(n.X, n.Y);
        var t = Fmt(n.Text, 12.5 * Zoom, B("#FDE68A"), false);
        t.MaxTextWidth = Math.Max(60, 240 * Zoom);
        var r = new Rect(p0.X, p0.Y, t.Width + 16 * Zoom, t.Height + 12 * Zoom);
        dc.DrawRectangle(B("#2A2416"), new Pen(Selection.Contains(n) ? B("#3B82F6") : B("#7C6A2E"), Selection.Contains(n) ? 2 : 1), r, 6, 6);
        dc.DrawText(t, new Point(p0.X + 8 * Zoom, p0.Y + 6 * Zoom));
    }

    // ------------------------------------------------------------ envelopes (Simulation mode)
    List<Point> EnvPath(SimEvent e)
    {
        var pts = new List<Point>();
        if (e.Path != null && e.Path.Count >= 2)
        {
            Device last = null;
            foreach (var pt in e.Path) { if (pt.Dev != last) { pts.Add(Center(pt.Dev)); last = pt.Dev; } }
        }
        else if (e.AtDev != null) pts.Add(Center(e.AtDev));
        return pts;
    }

    public Point EnvPoint(SimEvent e, double t)
    {
        var pts = EnvPath(e);
        if (pts.Count == 0) return new Point(-100, -100);
        if (pts.Count == 1 || e.Origin) return new Point(pts[0].X + 14 * Zoom, pts[0].Y - 26 * Zoom);
        double Dist(Point a, Point b) => Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
        double total = 0;
        for (int i = 1; i < pts.Count; i++) total += Dist(pts[i - 1], pts[i]);
        double want = total * Math.Clamp(t, 0, 1);
        for (int i = 1; i < pts.Count; i++)
        {
            double seg = Dist(pts[i - 1], pts[i]);
            if (want <= seg || i == pts.Count - 1)
            {
                double k = seg < 0.01 ? 1 : Math.Min(1, want / seg);
                return new Point(pts[i - 1].X + (pts[i].X - pts[i - 1].X) * k, pts[i - 1].Y + (pts[i].Y - pts[i - 1].Y) * k);
            }
            want -= seg;
        }
        return pts[^1];
    }

    Point EnvAt(SimEvent e, int k)
    {
        var c = EnvPoint(e, EnvProgress);
        if (EnvProgress >= 0.999) c = new Point(c.X + (k % 3 - 1) * 6 * Zoom, c.Y + (k / 3 % 3) * -6 * Zoom);
        return c;
    }

    void DrawEnvelopes(DrawingContext dc)
    {
        if (Envelopes == null) return;
        int k = 0;
        foreach (var e in Envelopes)
        {
            var c = EnvAt(e, k++);
            double w = 22 * Math.Max(0.7, Zoom), h = 15 * Math.Max(0.7, Zoom);
            var r = new Rect(c.X - w / 2, c.Y - h / 2, w, h);
            dc.DrawRectangle(B(e.Color), new Pen(e == Picked ? B("#FFFFFF") : B("#0B1220"), e == Picked ? 2.5 : 1.2), r, 2, 2);
            var flap = new Pen(B("#0B1220"), 1.2);
            dc.DrawLine(flap, r.TopLeft, new Point(c.X, c.Y + h * 0.1));
            dc.DrawLine(flap, r.TopRight, new Point(c.X, c.Y + h * 0.1));
        }
    }

    SimEvent HitEnvelope(Point s)
    {
        if (Envelopes == null) return null;
        SimEvent hit = null;
        int k = 0;
        foreach (var e in Envelopes)
        {
            var c = EnvAt(e, k++);
            if (Math.Abs(s.X - c.X) < 16 * Math.Max(0.7, Zoom) && Math.Abs(s.Y - c.Y) < 13 * Math.Max(0.7, Zoom)) hit = e;
        }
        return hit;
    }

    // ------------------------------------------------------------ hit testing
    public Device HitDevice(Point s)
    {
        if (P == null || !Draw.LayerShows("devices")) return null;
        for (int i = P.Devices.Count - 1; i >= 0; i--)
        {
            var r = IconRect(P.Devices[i]).Inflate(6);
            r = new Rect(r.X, r.Y, r.Width, r.Height + 14 * Zoom);
            if (r.Contains(s)) return P.Devices[i];
        }
        return null;
    }

    public Link HitLink(Point s)
    {
        if (P == null || !Draw.LayerShows("cables")) return null;
        foreach (var l in P.Links.Concat(P.Sim.Wireless))
        {
            Point a = Center(l.A.Dev), b = Center(l.B.Dev);
            double abx = b.X - a.X, aby = b.Y - a.Y, len2 = abx * abx + aby * aby;
            if (len2 < 1) continue;
            double t = Math.Clamp(((s.X - a.X) * abx + (s.Y - a.Y) * aby) / len2, 0, 1);
            double dx = a.X + abx * t - s.X, dy = a.Y + aby * t - s.Y;
            if (Math.Sqrt(dx * dx + dy * dy) < 9) return l;
        }
        return null;
    }

    Note HitNote(Point s)
    {
        if (!Draw.LayerShows("drawings") || Draw.LayerLocked("drawings")) return null;
        for (int i = P.Notes.Count - 1; i >= 0; i--)
        {
            var n = P.Notes[i];
            if (n.View != "logical") continue;
            var p0 = ToScreen(n.X, n.Y);
            var t = Fmt(n.Text, 12.5 * Zoom, Brushes.White, false);
            t.MaxTextWidth = Math.Max(60, 240 * Zoom);
            if (new Rect(p0.X, p0.Y, t.Width + 16 * Zoom, t.Height + 12 * Zoom).Contains(s)) return n;
        }
        return null;
    }

    public object HitAny(Point s) => Draw.HitTop(s) ?? (object)HitDevice(s) ?? (object)HitNote(s) ?? (object)HitLink(s) ?? Draw.HitBelow(s);

    /// <summary>Can this be moved or deleted (its layer isn't locked)?</summary>
    public bool CanChange(object o) => o switch
    {
        Device => !Draw.LayerLocked("devices"),
        Link => !Draw.LayerLocked("cables"),
        Note => !Draw.LayerLocked("drawings"),
        _ => true,
    };

    // ------------------------------------------------------------ touch, pen and mouse
    readonly Dictionary<int, Point> touches = new();
    readonly DispatcherTimer longPress;
    object downOn;
    Point downAt;
    bool dragging, panning, moved, drawGesture;
    Vector panStart;
    Dictionary<object, Point2> dragStart;
    double pinchDist, pinchZoom;
    Point pinchWorld;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (P == null) return;
        Focus();
        var s = e.GetPosition(this);
        var pt = e.GetCurrentPoint(this);
        DrawTool.Shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
        touches[e.Pointer.Id] = s;
        e.Pointer.Capture(this);
        e.Handled = true;
        if (touches.Count == 2)
        {
            // two fingers: pinch to zoom (whatever the first finger was doing stops)
            longPress.Stop(); dragging = false; panning = false; downOn = null;
            if (drawGesture) { Draw.Cancel(); drawGesture = false; }
            if (dragStart != null) { foreach (var kv in dragStart) Edits.Set(kv.Key, kv.Value); dragStart = null; }
            var ps = touches.Values.ToList();
            pinchDist = Math.Max(10, Dist(ps[0], ps[1]));
            pinchZoom = Zoom;
            pinchWorld = ToWorld(Mid(ps[0], ps[1]));
            return;
        }
        if (touches.Count > 2) return;
        if (pt.Properties.IsRightButtonPressed)
        {
            touches.Remove(e.Pointer.Id);
            if (Draw.Busy) { Draw.Cancel(); return; }
            var o = HitAny(s);
            if (o != null) { Select(o); MenuAsked?.Invoke(o, s); }
            return;
        }
        if (pt.Properties.IsMiddleButtonPressed) { mouse = downAt = s; moved = true; panning = true; panStart = Pan; return; }
        PressAt(s, e.ClickCount);
        if (downOn != null) longPress.Start();
    }

    /// <summary>A finger (or the mouse button) went down: what the gesture is about.</summary>
    public void PressAt(Point s, int clicks = 1)
    {
        mouse = downAt = s; moved = false; downOn = null; dragging = false; panning = false; drawGesture = false;
        if (P == null) return;
        if (DrawTool.IsDrawTool(Tool)) { drawGesture = Draw.Down(Tool, s, clicks); InvalidateVisual(); return; }
        var env = HitEnvelope(s);
        if (env != null && Tool == "select") { Picked = env; EnvelopeClicked?.Invoke(env); InvalidateVisual(); return; }
        if (Tool == "connect") { ConnectTap(s); return; }
        if (Tool == "pdu")
        {
            var d = HitDevice(s);
            if (d == null || d.Ip == null) { if (pduFrom != null) { pduFrom = null; Say("Cancelled."); } else Say("Tap a computer to send a ping from, then the one to ping."); InvalidateVisual(); return; }
            if (pduFrom == null) { pduFrom = d; Say("Now tap the device to ping."); }
            else { var from = pduFrom; pduFrom = null; if (from != d) PduRequested?.Invoke(from, d); }
            InvalidateVisual();
            return;
        }
        if (Tool == "delete")
        {
            var o = HitAny(s);
            if (o != null && !CanChange(o)) { Say("That layer is locked: unlock it under Layers to delete it."); return; }
            if (o != null) { var all = Draw.Expand(o); Edits.Delete(P, Undo, all); foreach (var x in all) Selection.Remove(x); Edited?.Invoke(); Say("Deleted."); InvalidateVisual(); }
            return;
        }
        if (Draw.HandleDown(s)) { drawGesture = true; return; }
        var hit = HitAny(s);
        if (clicks >= 2 && hit is Shape sh) { Draw.EditText(sh); return; }
        if (clicks >= 2 && hit is Callout co) { Draw.EditCallout(co); return; }
        downOn = hit;
        if (hit != null)
        {
            Select(hit);
            dragStart = new Dictionary<object, Point2>();
            foreach (var o in Selection) if ((o is Device || o is Note) && CanChange(o)) dragStart[o] = Edits.Pos(o);
            foreach (var o in Draw.Movable()) dragStart[o] = Edits.Pos(o);
            dragging = true;
        }
        else
        {
            Selection.Clear();
            panning = true; panStart = Pan;
        }
        SelectionChanged?.Invoke();
        InvalidateVisual();
    }

    void Select(object o)
    {
        if (Selection.Contains(o)) return;
        Selection.Clear();
        foreach (var x in Draw.Expand(o)) Selection.Add(x);
        SelectionChanged?.Invoke();
    }

    static double Dist(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    static Point Mid(Point a, Point b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var s = e.GetPosition(this);
        if (touches.ContainsKey(e.Pointer.Id)) touches[e.Pointer.Id] = s;
        if (touches.Count == 2)
        {
            var ps = touches.Values.ToList();
            double z = Math.Clamp(pinchZoom * Dist(ps[0], ps[1]) / pinchDist, 0.25, 3);
            var m = Mid(ps[0], ps[1]);
            Zoom = z;
            Pan = new Vector(m.X - pinchWorld.X * Zoom, m.Y - pinchWorld.Y * Zoom);
            InvalidateVisual();
            return;
        }
        if (Dist(s, downAt) > 6) longPress.Stop();
        MoveTo(s, touches.Count == 1);
    }

    /// <summary>The finger moved (pressed = still down).</summary>
    public void MoveTo(Point s, bool pressed = true)
    {
        mouse = s;
        if (Dist(s, downAt) > 6) moved = true;
        if (drawGesture || Draw.Busy) { if (Draw.Move(s, pressed)) return; }
        if (panning && pressed) { Pan = panStart + (s - downAt); InvalidateVisual(); return; }
        if (dragging && moved && pressed && dragStart != null)
        {
            var w0 = ToWorld(downAt); var w1 = ToWorld(s);
            double dx = Math.Round((w1.X - w0.X) / 10) * 10, dy = Math.Round((w1.Y - w0.Y) / 10) * 10;
            foreach (var kv in dragStart) Edits.Set(kv.Key, kv.Key is Callout ? new Point2(kv.Value.X + w1.X - w0.X, kv.Value.Y + w1.Y - w0.Y) : new Point2(kv.Value.X + dx, kv.Value.Y + dy));
            InvalidateVisual();
            return;
        }
        if (connectFrom != null || PortFrom != null || pduFrom != null) InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        bool wasPinch = touches.Count >= 2;
        touches.Remove(e.Pointer.Id);
        longPress.Stop();
        if (wasPinch) { if (touches.Count == 1) { downAt = touches.Values.First(); panStart = Pan; panning = true; moved = true; } return; }
        if (touches.Count > 0) return;
        e.Pointer.Capture(null);
        ReleaseAt(e.GetPosition(this));
    }

    /// <summary>The finger came up: a tap or the end of a drag.</summary>
    public void ReleaseAt(Point s)
    {
        mouse = s;
        if (drawGesture || Draw.Busy)
        {
            drawGesture = false;
            if (Draw.Up(s)) { InvalidateVisual(); return; }
        }
        if (panning) { panning = false; return; }
        if (dragging)
        {
            dragging = false;
            if (!moved) { if (downOn is Device d) DeviceOpened?.Invoke(d); }
            else if (dragStart != null) { Edits.Moved(P, Undo, dragStart); Edited?.Invoke(); }
            dragStart = null;
        }
        if (!moved && downOn is Link l) Say(LinkTip(l));
        downOn = null;
        InvalidateVisual();
    }

    /// <summary>A tap: down and up at one place (the checks use it as a finger would).</summary>
    public void Tap(Point s, int clicks = 1) { PressAt(s, clicks); ReleaseAt(s); }
    /// <summary>A drag from a to b (the checks use it as a finger would).</summary>
    public void DragFinger(Point a, Point b) { PressAt(a); for (int i = 1; i <= 6; i++) MoveTo(new Point(a.X + (b.X - a.X) * i / 6, a.Y + (b.Y - a.Y) * i / 6)); ReleaseAt(b); }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        touches.Remove(e.Pointer.Id);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        ZoomAt(e.GetPosition(this), e.Delta.Y > 0 ? 1.15 : 1 / 1.15);
        e.Handled = true;
    }

    public void ZoomAt(Point s, double f)
    {
        var w = ToWorld(s);
        Zoom = Math.Clamp(Zoom * f, 0.25, 3);
        Pan = new Vector(s.X - w.X * Zoom, s.Y - w.Y * Zoom);
        InvalidateVisual();
    }

    /// <summary>Zooms and pans so every device is in view.</summary>
    public void Fit()
    {
        double W = Bounds.Width, H = Bounds.Height;
        if (P == null || P.Devices.Count == 0 || W < 10) return;
        double minX = P.Devices.Min(d => d.X) - 70, maxX = P.Devices.Max(d => d.X) + 70, minY = P.Devices.Min(d => d.Y) - 60, maxY = P.Devices.Max(d => d.Y) + 80;
        foreach (var n in P.Notes.Where(n => n.View == "logical")) { minX = Math.Min(minX, n.X - 10); minY = Math.Min(minY, n.Y - 10); maxX = Math.Max(maxX, n.X + 200); maxY = Math.Max(maxY, n.Y + 40); }
        Zoom = Math.Clamp(Math.Min(W / (maxX - minX), H / (maxY - minY)), 0.3, 1.4);
        Pan = new Vector((W - (maxX - minX) * Zoom) / 2 - minX * Zoom, (H - (maxY - minY) * Zoom) / 2 - minY * Zoom);
        InvalidateVisual();
    }

    /// <summary>Where a new device goes: the middle of the view, a little to the side of the last one added.</summary>
    public Point2 NewSpot()
    {
        var c = ToWorld(new Point(Bounds.Width / 2, Bounds.Height / 2));
        double x = Math.Round(c.X / 10) * 10, y = Math.Round(c.Y / 10) * 10;
        for (int k = 0; k < 40 && P.Devices.Any(d => Math.Abs(d.X - x) < 50 && Math.Abs(d.Y - y) < 50); k++) { x += 80; if (k % 5 == 4) { x -= 400; y += 90; } }
        return new Point2(x, y);
    }

    public void CancelTool() { connectFrom = null; PortFrom = null; pduFrom = null; Draw.Cancel(); InvalidateVisual(); }

    // ------------------------------------------------------------ cables
    /// <summary>The cable tool: the first device, then the second (automatic ports and cable).</summary>
    public void ConnectTap(Point s)
    {
        var d = HitDevice(s);
        if (PortFrom != null)
        {
            if (d == null) { PortFrom = null; Say("Cancelled."); InvalidateVisual(); return; }
            if (d == PortFrom.Dev) { Say("Now tap the other device."); return; }
            PortAsked?.Invoke(d, s);
            return;
        }
        if (d == null) { if (connectFrom != null) { connectFrom = null; Say("Cancelled."); InvalidateVisual(); } else Say("Tap a device to start a cable."); return; }
        if (connectFrom == null)
        {
            if (Project.FreePort(d, d) == null && !d.Ports.Any(x => x.Media == "sfp")) { Say(d.Name + " has no free port."); return; }
            connectFrom = d; mouse = s;
            Say("Cable from " + d.Name + ": now tap the other device.");
            InvalidateVisual();
            return;
        }
        var a0 = connectFrom;
        connectFrom = null;
        if (a0 == d) { Say("Pick two different devices."); InvalidateVisual(); return; }
        if (!PortLayout.AutoPorts(a0, d, out var pa, out var pb)) { Say("No suitable free ports between " + a0.Name + " and " + d.Name + ". Hold a device to pick its ports yourself."); InvalidateVisual(); return; }
        MakeCable(pa, pb, "auto");
    }

    /// <summary>A cable between two ports ("auto" picks straight-through or crossover).</summary>
    public Link MakeCable(Port a, Port b, string cable)
    {
        string c = cable == "auto" ? Project.AutoCable(a, b) : cable;
        string why = P.CanConnect(a, b, c);
        if (why.Length > 0) { Say(why); InvalidateVisual(); return null; }
        var l = Edits.Connect(P, Undo, a, b, c);
        Say("Connected " + a.FullName + " to " + b.FullName + " (" + l.CableName.ToLowerInvariant() + ")." + (l.Status == "up" ? "" : " " + l.Problem));
        Edited?.Invoke();
        InvalidateVisual();
        return l;
    }

    public static string DeviceTip(Device d)
    {
        var sb = new System.Text.StringBuilder(d.Name + "  -  " + d.Model);
        if (!d.PoweredOn) sb.Append("\nSwitched off");
        if (d.Host != null)
            foreach (var f in d.Host.Adapters)
                sb.Append("\n" + Personalities.Get(d.Os).IfName(f.Port) + ": " + (f.Ip != 0 ? Ip.Cidr(f.Ip, f.Mask) + (f.Dhcp ? " (DHCP)" : "") : f.Dhcp ? "waiting for DHCP" : "no address") + (f.Up ? "" : ", not connected"));
        else if (d.Ip != null)
            foreach (var f in d.Ip.Ifs.Where(x => x.Ip != 0 && !x.IsLoopback).Take(6)) sb.Append("\n" + f.ShortName + ": " + Ip.Cidr(f.Ip, f.Mask) + (f.Up ? "" : " (down)"));
        if (d.Ap != null) sb.Append("\nWi-Fi: " + d.Ap.Ssid + (d.Ap.Security == "open" ? " (open)" : " (WPA2)"));
        sb.Append("\n\n" + Catalog.Explain(d.Kind));
        return sb.ToString();
    }

    public static string LinkTip(Link l)
    {
        var sb = new System.Text.StringBuilder(l.A.FullName + "  -  " + l.B.FullName + ": " + l.CableName);
        if (!l.IsWireless) sb.Append(", " + l.Length.ToString("0.#", CultureInfo.InvariantCulture) + " m");
        else sb.Append(", signal " + (int)(l.Signal * 100) + "%");
        sb.Append(". " + (l.Status == "up" ? "Working (green lights)." : l.Problem));
        foreach (var p in new[] { l.A, l.B }) if (p.StpBlocked.Count > 0) sb.Append(" " + p.FullName + " is blocked by spanning tree (amber): it would make a loop.");
        return sb.ToString();
    }
}

static class PortExt
{
    /// <summary>The VLANs a switch port carries (for "blocked in every VLAN" = amber light).</summary>
    public static IEnumerable<int> Ports(this Port p)
    {
        if (p.Dev.Bridge == null) return new[] { 1 };
        return p.Dev.Bridge.Vlans.Keys.Where(v => v < 1002 && p.CarriesVlan(v));
    }
}
