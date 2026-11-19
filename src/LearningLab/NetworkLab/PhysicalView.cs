// Network Lab - the physical view (App Hub's PhysicalView.cs for Avalonia, touch first): the campus map with its
// buildings, each floor's plan (rooms, walls, doors, desks, labels, a picture of a real plan underneath), racks in the
// wiring closets and their fronts, the devices where they are, the cables along the walls in their colours and Wi-Fi
// range circles. The same network as the logical view: cabling or moving here shows there. One finger drags a device
// or pans, two pinch, the wheel zooms, a tap opens a device or goes into a building, a double tap zooms into a room,
// and holding (or right-clicking) gives the menu. Tools: select, connect (the port picker), delete, room, wall, door,
// label, rack, and the drawing tools.
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using NetworkLab;
using LearningLab.Views;
using Rect = Avalonia.Rect;
using Label = NetworkLab.Label;

namespace LearningLab.NetworkLab;

public sealed class PhysicalView : Control, IDrawHost
{
    public readonly DrawTool Draw;
    public Project P;
    public UndoStack Undo;
    /// <summary>campus, floor, rack</summary>
    public string Level = "floor";
    public string FloorKey = "";
    public string RackId = "";
    public double Zoom = 1;
    public Vector Pan = new(30, 50);
    /// <summary>select, connect, delete, room, wall, door, label, rack, or a drawing tool (draw:..., obj:...).</summary>
    public string Tool = "select";
    public string Cable = "auto";
    public bool ShowRange = true, ShowAddresses = true;
    public readonly HashSet<object> Selection = new();
    public string RoomName = "";
    /// <summary>The port picker shown last (the checks tap its ports).</summary>
    public PortPanel Picker { get; private set; }
    public Port ConnectFrom => connectFrom;

    public event Action<string> Status;
    public event Action<Device> DeviceOpened;
    public event Action Edited, LevelChanged, SelectionChanged;
    public event Action<string> ToolChanged;

    // ------------------------------------------------------------ what the drawing tools need
    Project IDrawHost.DrawProject => P;
    UndoStack IDrawHost.DrawUndo => Undo;
    string IDrawHost.DrawView => Level == "campus" ? "campus" : Level == "floor" && CurrentFloor != null ? FloorKey : "";
    string IDrawHost.CalloutView => Level == "floor" ? "physical" : "";
    double IDrawHost.DrawScale => Scale;
    double IDrawHost.DrawBase => Level == "campus" ? 3.2 : 22;
    Point IDrawHost.DrawToScreen(double x, double y) => S(x, y);
    Point IDrawHost.DrawToView(Point s) => M(s);
    HashSet<object> IDrawHost.DrawSelection => Selection;
    bool IDrawHost.CalloutAnchor(Callout c, out Point s)
    {
        s = default;
        var f = CurrentFloor;
        if (f == null || Level != "floor") return false;
        var d = P.Devices.FirstOrDefault(x => x.Id == c.Target);
        if (d != null) { if (!OnFloor(d, f)) return false; s = DevPoint(d); return true; }
        var l = P.Links.FirstOrDefault(x => x.Id == c.Target);
        if (l != null && OnFloor(l.A.Dev, f) && OnFloor(l.B.Dev, f)) { Point a = DevPoint(l.A.Dev), b = DevPoint(l.B.Dev); s = new Point((a.X + b.X) / 2, a.Y); return true; }
        return false;
    }
    object IDrawHost.DrawTargetAt(Point s) => Level == "floor" ? (object)HitDevice(s) ?? HitLink(s) : null;
    void IDrawHost.DrawRedraw() => InvalidateVisual();
    void IDrawHost.DrawSay(string m) => Say(m);
    void IDrawHost.DrawEdited() => Edited?.Invoke();
    void IDrawHost.DrawSelected() => SelectionChanged?.Invoke();
    void IDrawHost.DrawToolDone() { Tool = "select"; ToolChanged?.Invoke("select"); }
    Control IDrawHost.DrawControl => this;

    bool OnFloor(Device d, Floor f)
    {
        if (d.Rack.Length > 0) { var r = P.Racks.FirstOrDefault(k => k.Id == d.Rack); return r != null && r.Place == f.Key; }
        return d.Place == f.Key;
    }

    Point mouse, downAt;
    bool panning, dragging, drawing, moved, drawGesture;
    Vector panStart;
    Dictionary<Device, Point2> dragStart;
    Dictionary<object, Point2> drawDrag = new();
    Dictionary<Rack, Point2> rackStart;
    object downOn;
    int downClicks;
    Port connectFrom;
    readonly List<KeyValuePair<Rect, Action>> crumbs = new();
    readonly Dictionary<int, Point> touches = new();
    readonly DispatcherTimer longPress;
    double pinchDist, pinchZoom;
    Point pinchWorld;

    public PhysicalView()
    {
        Focusable = true; ClipToBounds = true;
        Draw = new DrawTool(this);
        longPress = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(550) };
        longPress.Tick += (s, e) => { longPress.Stop(); if (!moved && touches.Count == 1) { dragging = false; panning = false; Draw.Cancel(); ShowMenuAt(downAt); } };
    }

    void Say(string s) => Status?.Invoke(s);
    static IBrush B(string hex) => NetIcons.B(hex);
    static FormattedText Fmt(string t, double size, IBrush b, bool bold) => DrawArt.Fmt(t, size, b, bold);
    static double Dist(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    double W => Bounds.Width;
    double H => Bounds.Height;

    public Floor CurrentFloor => P?.Layout.Floor(FloorKey);
    public double Scale => (Level == "campus" ? 3.2 : 22) * Zoom;
    public Point S(double mx, double my) => new(mx * Scale + Pan.X, my * Scale + Pan.Y);
    public Point M(Point s) => new((s.X - Pan.X) / Scale, (s.Y - Pan.Y) / Scale);

    public void ShowFloor(string key)
    {
        var f = P.Layout.Floor(key);
        if (f == null) return;
        Level = "floor"; FloorKey = key; P.Layout.ActiveFloor = key;
        Selection.Clear();
        Fit();
        LevelChanged?.Invoke();
    }
    public void ShowCampus() { Level = "campus"; Selection.Clear(); Fit(); LevelChanged?.Invoke(); }
    public void ShowRack(string id) { Level = "rack"; RackId = id; Selection.Clear(); Fit(); LevelChanged?.Invoke(); }

    public void ZoomToRoom(Room r)
    {
        RoomName = r.Name;
        Zoom = Math.Max(0.2, Math.Min(6, Math.Min((W - 40) / (r.W * 22), (H - 90) / (r.H * 22))));
        Pan = new Vector((W - r.W * Scale) / 2 - r.X * Scale, 44 + (H - 44 - r.H * Scale) / 2 - r.Y * Scale);
        Say(r.Name + (r.Kind == "closet" ? ": the wiring closet, where switches, patch panels and racks go." : r.Kind == "server" ? ": the server room." : ". Tap the floor's name at the top to see the whole floor."));
        InvalidateVisual();
    }

    public void Fit()
    {
        RoomName = "";
        if (P == null || W < 10) { InvalidateVisual(); return; }
        if (FloorKey.Length == 0 || P.Layout.Floor(FloorKey) == null) { var df = P.Layout.DefaultFloor; if (df != null) FloorKey = df.Key; }
        if (Level == "rack")
        {
            var rack = P.Racks.FirstOrDefault(r => r.Id == RackId);
            int units = rack?.Units ?? 24;
            Zoom = Math.Max(0.3, Math.Min(1, Math.Min((W - 50) / (RackW + 60), (H - 110) / (units * UnitPx + 40))));
            Pan = new Vector(Math.Max(36 * Zoom, (W - RackW * Zoom) / 2), 56);
            InvalidateVisual();
            return;
        }
        double w, h;
        if (Level == "campus") { w = P.Layout.CampusW; h = P.Layout.CampusH; }
        else { var f = CurrentFloor; if (f == null) { InvalidateVisual(); return; } w = f.W; h = f.H; }
        double baseScale = Level == "campus" ? 3.2 : 22;
        Zoom = Math.Max(0.1, Math.Min(4, Math.Min((W - 30) / (w * baseScale), (H - 80) / (h * baseScale))));
        Pan = new Vector((W - w * Scale) / 2, 44 + (H - 44 - 30 - h * Scale) / 2);
        InvalidateVisual();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (e.PreviousSize.Width < 10) Fit();
    }

    // ------------------------------------------------------------ drawing
    public override void Render(DrawingContext dc)
    {
        dc.FillRectangle(B("#0D131D"), new Rect(0, 0, W, H));
        if (P == null) return;
        if (FloorKey.Length == 0 || P.Layout.Floor(FloorKey) == null) { var df = P.Layout.DefaultFloor; if (df != null) FloorKey = df.Key; }
        crumbs.Clear();
        if (Level == "campus") DrawCampus(dc);
        else if (Level == "rack") DrawRack(dc);
        else DrawFloor(dc);
        if (Level != "rack") Draw.RenderOverlay(dc);
        DrawCrumbs(dc);
        if (connectFrom != null) dc.DrawLine(new Pen(B("#93C5FD"), 2, new DashStyle(new double[] { 4, 3 }, 0)), DevPoint(connectFrom.Dev), mouse);
        if (drawing && (Tool == "room" || Tool == "wall"))
        {
            if (Tool == "room") dc.DrawRectangle(DrawArt.Alpha("#3B82F6", 50), new Pen(B("#3B82F6"), 2), new Rect(downAt, mouse).Normalize());
            else dc.DrawLine(new Pen(B("#E2E8F0"), 4), downAt, AxisEnd(downAt, mouse));
        }
    }

    static Point AxisEnd(Point a, Point b) => Math.Abs(a.X - b.X) > Math.Abs(a.Y - b.Y) ? new Point(b.X, a.Y) : new Point(a.X, b.Y);

    void DrawCrumbs(DrawingContext dc)
    {
        double x = 8;
        void Add(string t, Action a)
        {
            var ft = Fmt(t, 13, B(a != null ? "#93C5FD" : "#E2E8F0"), a == null);
            var r = new Rect(x, 6, ft.Width + 20, Math.Max(34, ft.Height + 12));
            dc.DrawRectangle(B("#1C2330"), a != null ? new Pen(B("#334155"), 1) : null, r, 6, 6);
            dc.DrawText(ft, new Point(x + 10, r.Y + (r.Height - ft.Height) / 2));
            if (a != null) crumbs.Add(new KeyValuePair<Rect, Action>(r, a));
            x += r.Width + 4;
            var sep = Fmt("›", 15, B("#64748B"), false);
            dc.DrawText(sep, new Point(x, r.Y + (r.Height - sep.Height) / 2));
            x += sep.Width + 4;
        }
        var f = CurrentFloor;
        bool narrow = W < 520;
        Add(narrow ? "Campus" : P.Layout.Name + " (campus)", Level == "campus" ? null : ShowCampus);
        if (Level != "campus" && f != null) Add(narrow ? f.Name : f.B.Name + ", " + f.Name, Level == "floor" && RoomName.Length == 0 ? null : () => ShowFloor(f.Key));
        if (Level == "floor" && RoomName.Length > 0) Add(RoomName, null);
        if (Level == "rack") { var r = P.Racks.FirstOrDefault(k => k.Id == RackId); Add(r != null ? r.Name : "Rack", null); }
    }

    void DrawCampus(DrawingContext dc)
    {
        var L = P.Layout;
        var campus = new Rect(S(0, 0), S(L.CampusW, L.CampusH));
        dc.DrawRectangle(B("#13261B"), new Pen(B("#1F3B2A"), 1), campus);
        using (dc.PushClip(campus)) Draw.RenderMap(dc, campus);
        foreach (var w in L.Paths) dc.DrawLine(new Pen(B("#475569"), 6 * Zoom, null, PenLineCap.Round), S(w.X1, w.Y1), S(w.X2, w.Y2));
        foreach (var lb in L.CampusLabels) dc.DrawText(Fmt(lb.Text, 12, B("#86EFAC"), false), S(lb.X, lb.Y));
        Draw.RenderBelow(dc);
        foreach (var b in L.Buildings)
        {
            var r = new Rect(S(b.X, b.Y), S(b.X + b.W, b.Y + b.H));
            dc.DrawRectangle(B("#334155"), new Pen(B("#94A3B8"), 2), r);
            int n = P.Devices.Count(d => { var f = P.Layout.Floor(d.Place); return f != null && f.B == b; });
            var t = Fmt(b.Name + "\n" + b.Floors.Count + " floor" + (b.Floors.Count == 1 ? "" : "s") + ", " + n + " device" + (n == 1 ? "" : "s"), 12, B("#F1F5F9"), true);
            t.MaxTextWidth = Math.Max(40, r.Width - 8);
            using (dc.PushClip(r)) dc.DrawText(t, new Point(r.X + 4, r.Y + 4));
        }
        if (Draw.LayerShows("cables"))
            foreach (var l in P.Links)
            {
                var fa = P.Layout.Floor(l.A.Dev.Place); var fb = P.Layout.Floor(l.B.Dev.Place);
                if (fa == null || fb == null || fa.B == fb.B) continue;
                Point a = S(fa.B.X + l.A.Dev.PX, fa.B.Y + l.A.Dev.PY), b = S(fb.B.X + l.B.Dev.PX, fb.B.Y + l.B.Dev.PY);
                dc.DrawLine(new Pen(B(CableColor(l)), 3), a, b);
                var mid = new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);
                var t = Fmt(l.CableName + ", " + l.Length.ToString("0", CultureInfo.InvariantCulture) + " m" + (l.Status == "up" ? "" : "  (not working)"), 11.5, B(l.Status == "up" ? "#E2E8F0" : "#FCA5A5"), false);
                dc.DrawRectangle(DrawArt.Alpha("#0D131D", 170), null, new Rect(mid.X - t.Width / 2 - 3, mid.Y - t.Height - 5, t.Width + 6, t.Height + 2), 3, 3);
                dc.DrawText(t, new Point(mid.X - t.Width / 2, mid.Y - t.Height - 4));
            }
        Draw.RenderAbove(dc);
        dc.DrawText(Fmt("Tap a building to go inside.", 12, B("#94A3B8"), false), new Point(12, H - 24));
    }

    static string RoomFill(string kind) => kind switch { "closet" => "#3B2F1E", "server" => "#1D3346", "hall" => "#18202D", _ => "#1E293B" };

    public static string CableColor(Link l) => l.Cable switch
    {
        "fiber-mm" => "#FB923C",
        "fiber-sm" => "#FACC15",
        "console" => "#7DD3FC",
        "wireless" => "#38BDF8",
        "cross" => "#A78BFA",
        _ => "#3B82F6",
    };

    void DrawFloor(DrawingContext dc)
    {
        var f = CurrentFloor;
        if (f == null) return;
        var outline = new Rect(S(0, 0), S(f.W, f.H));
        dc.DrawRectangle(B("#111827"), new Pen(B("#CBD5E1"), 4), outline);
        var bg = DrawArt.Picture(f.Background);
        if (bg != null) using (dc.PushOpacity(0.55)) dc.DrawImage(bg, new Rect(S(0, 0), S(f.BgW > 0 ? f.BgW : f.W, f.BgH > 0 ? f.BgH : f.H)));
        foreach (var r in f.Rooms) dc.DrawRectangle(DrawArt.Alpha(RoomFill(r.Kind), (byte)(bg != null ? 150 : 255)), null, new Rect(S(r.X, r.Y), S(r.X + r.W, r.Y + r.H)));
        double wallW = Math.Max(2, 0.18 * Scale);
        var wallPen = new Pen(B("#94A3B8"), wallW, null, PenLineCap.Square);
        foreach (var w in f.AllWalls()) dc.DrawLine(wallPen, S(w.X1, w.Y1), S(w.X2, w.Y2));
        foreach (var d in f.Doors)
        {
            Point a = S(d.X, d.Y), b = d.Vertical ? S(d.X, d.Y + d.W) : S(d.X + d.W, d.Y);
            dc.DrawLine(new Pen(B("#111827"), wallW + 2), a, b);
            dc.DrawLine(new Pen(B("#64748B"), 1), a, d.Vertical ? S(d.X + d.W, d.Y) : S(d.X, d.Y + d.W));
        }
        foreach (var r in f.Rooms)
        {
            var t = Fmt(r.Name, Math.Max(9, Math.Min(14, 0.5 * Scale)), B("#94A3B8"), true);
            var p0 = S(r.X, r.Y);
            dc.DrawText(t, new Point(p0.X + 6, p0.Y + 4));
        }
        foreach (var lb in f.Labels)
        {
            var p = S(lb.X, lb.Y);
            var t = Fmt(lb.Text, Math.Max(8, 0.38 * Scale), B("#CBD5E1"), false);
            dc.DrawRectangle(B("#2A3344"), new Pen(B("#475569"), 1), new Rect(p.X - 2, p.Y - 2, Math.Max(t.Width + 4, 1.4 * Scale), Math.Max(t.Height + 4, 0.8 * Scale)), 2, 2);
            dc.DrawText(t, p);
        }
        // drawings: the map layer, then furniture and shapes under the devices
        Draw.RenderMap(dc, outline);
        Draw.RenderBelow(dc);
        // Wi-Fi range
        if (ShowRange)
            foreach (var d in P.Devices.Where(x => x.Ap != null && x.Place == f.Key && x.PoweredOn && x.Ap.Enabled))
            {
                var c = DevPoint(d);
                double rr = d.Ap.Range * Scale;
                var g = new RadialGradientBrush();
                g.GradientStops.Add(new GradientStop(Color.FromArgb(60, 56, 189, 248), 0));
                g.GradientStops.Add(new GradientStop(Color.FromArgb(0, 56, 189, 248), 1));
                dc.DrawEllipse(g, new Pen(DrawArt.Alpha("#38BDF8", 90), 1, new DashStyle(new double[] { 4, 3 }, 0)), c, rr, rr);
            }
        // racks
        foreach (var r in P.Racks.Where(k => k.Place == f.Key))
        {
            var rr = RackRect(r);
            dc.DrawRectangle(B("#0B0F14"), new Pen(B(Selection.Contains(r) ? "#3B82F6" : "#64748B"), 2), rr);
            for (int i = 1; i < 5; i++) dc.DrawLine(new Pen(B("#1F2937"), 1), new Point(rr.X + 2, rr.Y + rr.Height * i / 5), new Point(rr.Right - 2, rr.Y + rr.Height * i / 5));
            var t = Fmt(r.Name + " (" + P.Devices.Count(d => d.Rack == r.Id) + ")", Math.Max(9, 0.4 * Scale), B("#E2E8F0"), true);
            dc.DrawText(t, new Point(rr.Center.X - t.Width / 2, rr.Bottom + 2));
        }
        // cables along the walls
        int lane = 0;
        if (Draw.LayerShows("cables"))
            foreach (var l in P.Links)
            {
                bool ha = OnFloor(l.A.Dev, f), hb = OnFloor(l.B.Dev, f);
                if (!ha && !hb) continue;
                var dash = l.Status != "up" ? new DashStyle(new double[] { 3, 2 }, 0) : null;
                var pen = new Pen(B(CableColor(l)), Selection.Contains(l) ? 4 : 2.2, dash, PenLineCap.Flat, PenLineJoin.Round);
                if (ha && hb)
                {
                    Point a = DevPoint(l.A.Dev), b = DevPoint(l.B.Dev);
                    double o = ((lane++ % 5) - 2) * 3;   // cables side by side, like in a cable tray
                    var corner = new Point(b.X + o, a.Y + o);
                    dc.DrawGeometry(null, pen, DrawArt.Geo(false, new Point(a.X, a.Y + o), corner, new Point(b.X + o, b.Y)));
                    if (l.Status != "up") dc.DrawEllipse(B("#F87171"), null, corner, 4, 4);
                }
                else
                {
                    var here = ha ? l.A : l.B; var there = ha ? l.B : l.A;
                    var a = DevPoint(here.Dev);
                    var riser = Riser(f);
                    dc.DrawGeometry(null, pen, DrawArt.Geo(false, a, new Point(riser.X, a.Y), riser));
                    dc.DrawEllipse(B("#0B0F14"), new Pen(pen.Brush, 2), riser, 6, 6);
                    var tf = P.Layout.Floor(there.Dev.Rack.Length > 0 ? (P.Racks.FirstOrDefault(k => k.Id == there.Dev.Rack)?.Place ?? there.Dev.Place) : there.Dev.Place);
                    var t = Fmt("to " + there.Dev.Name + (tf != null ? " (" + (tf.B == f.B ? tf.Name : tf.B.Name) + ")" : ""), 10.5, B("#CBD5E1"), false);
                    dc.DrawText(t, new Point(riser.X + 8, riser.Y - 6));
                }
            }
        // devices
        if (Draw.LayerShows("devices"))
            foreach (var d in P.Devices.Where(x => x.Place == f.Key && x.Rack.Length == 0))
            {
                var c = DevPoint(d);
                double w = DevW, h = w * 0.8;
                var r = new Rect(c.X - w / 2, c.Y - h / 2, w, h);
                if (Selection.Contains(d)) dc.DrawRectangle(DrawArt.Alpha("#3B82F6", 70), new Pen(B("#3B82F6"), 1.5), r.Inflate(3), 5, 5);
                using (d.PoweredOn ? default(DrawingContext.PushedState?) : dc.PushOpacity(0.45))
                    NetIcons.Draw(dc, d.Kind, d.Os, r, d.PoweredOn);
                var t = Fmt(d.Name, Math.Max(9, Math.Min(12.5, 0.45 * Scale)), B("#E2E8F0"), true);
                dc.DrawRectangle(DrawArt.Alpha("#0D131D", 170), null, new Rect(c.X - t.Width / 2 - 2, r.Bottom, t.Width + 4, t.Height), 2, 2);
                dc.DrawText(t, new Point(c.X - t.Width / 2, r.Bottom));
                if (ShowAddresses && Draw.LayerShows("labels"))
                {
                    var ips = NetCanvas.Addresses(d);
                    if (ips.Count > 0) { var it = Fmt(string.Join("  ", ips), Math.Max(8, Math.Min(11, 0.4 * Scale)), B("#7DD3FC"), false); dc.DrawText(it, new Point(c.X - it.Width / 2, r.Bottom + t.Height)); }
                }
            }
        Draw.RenderAbove(dc);
        Draw.RenderLabels(dc);
        // scale bar
        var s0 = new Point(14, H - 22);
        dc.DrawLine(new Pen(B("#94A3B8"), 2), s0, new Point(s0.X + 10 * Scale, s0.Y));
        dc.DrawText(Fmt("10 m", 11, B("#94A3B8"), false), new Point(s0.X + 10 * Scale + 6, s0.Y - 8));
    }

    double DevW => Math.Max(26, 1.6 * Scale);
    Rect RackRect(Rack r) { var c = S(r.PX, r.PY); double s = Math.Max(Scale, 24); return new Rect(c.X - 0.4 * s, c.Y - 0.5 * s, 0.8 * s, 1.0 * s); }

    /// <summary>Where cables leave a floor: the corner of the wiring closet (or the floor's corner).</summary>
    Point Riser(Floor f)
    {
        var c = f.Rooms.FirstOrDefault(r => r.Kind == "closet") ?? f.Rooms.FirstOrDefault(r => r.Kind == "server");
        return c != null ? S(c.X + c.W - 0.6, c.Y + 0.6) : S(f.W - 1, 1);
    }

    public Point DevPoint(Device d)
    {
        if (d.Rack.Length > 0) { var r = P.Racks.FirstOrDefault(k => k.Id == d.Rack); if (r != null) return S(r.PX, r.PY); }
        return S(d.PX, d.PY);
    }

    // ------------------------------------------------------------ rack front
    public const double UnitPx = 44, RackW = 700;
    /// <summary>How many rack units (U) a device takes.</summary>
    public static int UnitsOf(Device d) => 2;

    void DrawRack(DrawingContext dc)
    {
        var rack = P.Racks.FirstOrDefault(r => r.Id == RackId);
        if (rack == null) return;
        using (dc.PushTransform(Matrix.CreateScale(Zoom, Zoom) * Matrix.CreateTranslation(Pan.X, Pan.Y)))
        {
            double uh = UnitPx, w = RackW;
            dc.DrawRectangle(B("#0B0F14"), new Pen(B("#64748B"), 3), new Rect(-30, -10, w + 60, rack.Units * uh + 20));
            for (int u = 1; u <= rack.Units; u++)
            {
                double y = (rack.Units - u) * uh;
                dc.DrawText(Fmt(u.ToString(CultureInfo.InvariantCulture), 10, B("#64748B"), false), new Point(-24, y + 8));
                dc.DrawLine(new Pen(B("#1F2937"), 1), new Point(0, y), new Point(w, y));
            }
            foreach (var d in P.Devices.Where(k => k.Rack == rack.Id))
            {
                int u = Math.Max(1, Math.Min(rack.Units, d.RackU));
                int units = UnitsOf(d);
                double y = (rack.Units - (u + units - 1)) * uh;
                var size = PortLayout.PanelSize(d);
                double sc = Math.Min(w / size.Width, (uh * units - 4) / size.Height);
                dc.DrawRectangle(B("#1F2937"), new Pen(B("#475569"), 1), new Rect(-2, y + 1, w + 4, uh * units - 2), 3, 3);
                using (dc.PushTransform(Matrix.CreateScale(sc, sc) * Matrix.CreateTranslation(0, y + 2)))
                    PortPanel.Draw(dc, d, false, new Size(size.Width, size.Height), false, "auto", null);
            }
            var note = Fmt("Mount devices from their menu in the floor plan (hold a device, Mount in rack). Tap a device here to open it.", 12, B("#94A3B8"), false);
            note.MaxTextWidth = w;
            dc.DrawText(note, new Point(0, rack.Units * uh + 18));
        }
    }

    public Device RackDeviceAt(Point s)
    {
        var rack = P.Racks.FirstOrDefault(r => r.Id == RackId);
        if (rack == null) return null;
        double x = (s.X - Pan.X) / Zoom, y0 = (s.Y - Pan.Y) / Zoom;
        foreach (var d in P.Devices.Where(k => k.Rack == rack.Id))
        {
            int u = Math.Max(1, Math.Min(rack.Units, d.RackU));
            double y = (rack.Units - (u + UnitsOf(d) - 1)) * UnitPx;
            if (new Rect(0, y, RackW, UnitPx * UnitsOf(d)).Contains(new Point(x, y0))) return d;
        }
        return null;
    }

    /// <summary>Where a device mounted in the rack shown is drawn (its middle; for the checks).</summary>
    public Point RackDevicePoint(Device d)
    {
        var rack = P.Racks.FirstOrDefault(r => r.Id == RackId);
        if (rack == null) return default;
        int u = Math.Max(1, Math.Min(rack.Units, d.RackU));
        double y = (rack.Units - (u + UnitsOf(d) - 1)) * UnitPx + UnitPx * UnitsOf(d) / 2;
        return new Point(Pan.X + RackW / 2 * Zoom, Pan.Y + y * Zoom);
    }

    // ------------------------------------------------------------ hit testing
    public Device HitDevice(Point s)
    {
        var f = CurrentFloor;
        if (f == null || Level != "floor" || !Draw.LayerShows("devices")) return null;
        foreach (var d in P.Devices.Where(x => x.Place == f.Key && x.Rack.Length == 0).Reverse())
        {
            var c = DevPoint(d);
            double w = Math.Max(40, DevW), h = w * 0.8 + 14;
            if (new Rect(c.X - w / 2, c.Y - w * 0.4, w, h).Contains(s)) return d;
        }
        return null;
    }
    public Rack HitRack(Point s)
    {
        var f = CurrentFloor;
        if (f == null || Level != "floor") return null;
        foreach (var r in P.Racks.Where(k => k.Place == f.Key)) if (RackRect(r).Inflate(new Thickness(8, 8, 8, 20)).Contains(s)) return r;
        return null;
    }
    public Link HitLink(Point s)
    {
        var f = CurrentFloor;
        if (f == null || Level != "floor" || !Draw.LayerShows("cables")) return null;
        foreach (var l in P.Links)
        {
            if (!OnFloor(l.A.Dev, f) || !OnFloor(l.B.Dev, f)) continue;
            Point a = DevPoint(l.A.Dev), b = DevPoint(l.B.Dev), c = new(b.X, a.Y);
            if (Near(s, a, c) || Near(s, c, b)) return l;
        }
        return null;
    }
    static bool Near(Point s, Point a, Point b)
    {
        double abx = b.X - a.X, aby = b.Y - a.Y, len2 = abx * abx + aby * aby;
        if (len2 < 1) return Dist(s, a) < 9;
        double t = Math.Max(0, Math.Min(1, ((s.X - a.X) * abx + (s.Y - a.Y) * aby) / len2));
        return Dist(new Point(a.X + abx * t, a.Y + aby * t), s) < 9;
    }
    public Building HitBuilding(Point s)
    {
        if (Level != "campus") return null;
        foreach (var b in P.Layout.Buildings) if (new Rect(S(b.X, b.Y), S(b.X + b.W, b.Y + b.H)).Contains(s)) return b;
        return null;
    }
    /// <summary>The middle of a building on the campus map (for the checks).</summary>
    public Point BuildingPoint(Building b) => S(b.X + b.W / 2, b.Y + b.H / 2);

    // ------------------------------------------------------------ touch, pen and mouse
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
            longPress.Stop();
            CancelGesture();
            var ps = touches.Values.ToList();
            pinchDist = Math.Max(10, Dist(ps[0], ps[1])); pinchZoom = Zoom;
            pinchWorld = new Point(((ps[0].X + ps[1].X) / 2 - Pan.X) / Zoom, ((ps[0].Y + ps[1].Y) / 2 - Pan.Y) / Zoom);
            return;
        }
        if (touches.Count > 2) return;
        if (pt.Properties.IsRightButtonPressed) { touches.Remove(e.Pointer.Id); if (connectFrom != null) { connectFrom = null; Say("Cancelled."); InvalidateVisual(); return; } if (Draw.Busy) { Draw.Cancel(); return; } ShowMenuAt(s); return; }
        if (pt.Properties.IsMiddleButtonPressed) { panning = true; panStart = Pan; downAt = s; moved = true; return; }
        PressAt(s, e.ClickCount);
        longPress.Start();
    }

    void CancelGesture()
    {
        if (dragging && dragStart != null) { foreach (var kv in dragStart) { kv.Key.PX = kv.Value.X; kv.Key.PY = kv.Value.Y; } foreach (var kv in drawDrag) Edits.Set(kv.Key, kv.Value); }
        if (drawGesture) Draw.Cancel();
        dragging = false; panning = false; drawing = false; drawGesture = false; downOn = null;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var s = e.GetPosition(this);
        if (touches.ContainsKey(e.Pointer.Id)) touches[e.Pointer.Id] = s;
        if (touches.Count == 2)
        {
            var ps = touches.Values.ToList();
            double z = Math.Clamp(pinchZoom * Dist(ps[0], ps[1]) / pinchDist, 0.1, 8);
            var m = new Point((ps[0].X + ps[1].X) / 2, (ps[0].Y + ps[1].Y) / 2);
            Zoom = z;
            Pan = new Vector(m.X - pinchWorld.X * Zoom, m.Y - pinchWorld.Y * Zoom);
            InvalidateVisual();
            return;
        }
        if (touches.Count == 0 && !DrawTool.IsDrawTool(Tool) && connectFrom == null) return;
        if (Dist(s, downAt) > 6) { moved = true; longPress.Stop(); }
        MoveTo(s, touches.Count > 0);
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

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { base.OnPointerCaptureLost(e); touches.Remove(e.Pointer.Id); }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        ZoomAt(e.GetPosition(this), e.Delta.Y > 0 ? 1.15 : 1 / 1.15);
        e.Handled = true;
    }

    public void ZoomAt(Point s, double f)
    {
        if (Level == "rack") { Zoom = Math.Clamp(Zoom * f, 0.2, 3); InvalidateVisual(); return; }
        var m = M(s);
        Zoom = Math.Clamp(Zoom * f, 0.1, 8);
        Pan = new Vector(s.X - m.X * Scale, s.Y - m.Y * Scale);
        InvalidateVisual();
    }

    static double Clamp(double v, double max) => Math.Max(0.3, Math.Min(max - 0.3, v));

    /// <summary>A finger (or the mouse button) went down: what the gesture is about.</summary>
    public void PressAt(Point s, int clicks = 1)
    {
        mouse = downAt = s; moved = false; downOn = null; downClicks = clicks; dragging = false; panning = false; drawing = false; drawGesture = false;
        if (P == null) return;
        foreach (var c in crumbs) if (c.Key.Contains(s)) { c.Value(); return; }
        if (Level != "rack" && DrawTool.IsDrawTool(Tool))
        {
            drawGesture = Draw.Down(Tool, s, clicks);
            InvalidateVisual();
            return;
        }
        if (Level == "rack") { downOn = RackDeviceAt(s); if (downOn == null) StartPan(); return; }
        if (Level == "campus")
        {
            if (Tool == "select" && Draw.HandleDown(s)) { drawGesture = true; return; }
            var top = Draw.HitTop(s);
            var b = top == null ? HitBuilding(s) : null;
            object hit = top ?? (b == null ? Draw.HitBelow(s) : null);
            if (hit is Shape sh && clicks >= 2) { Draw.EditText(sh); return; }
            if (Tool == "delete") { if (hit != null) { Edits.Delete(P, Undo, Draw.Expand(hit)); Edited?.Invoke(); } InvalidateVisual(); return; }
            if (b != null) { downOn = b; StartPan(); return; }
            SelectAndDrag(hit);
            return;
        }
        var f = CurrentFloor;
        if (f == null) return;
        var m = M(s);
        switch (Tool)
        {
            case "room": case "wall": drawing = true; return;
            case "door": AddDoor(f, m); return;
            case "label":
                NetAsk.Text(this, "Label", "Text for the label:", "Desk", false, t =>
                {
                    if (t.Trim().Length == 0) return;
                    var lb = new Label { X = m.X, Y = m.Y, Text = t.Trim() };
                    Undo.Do("label", () => { f.Labels.Add(lb); P.Touch(); }, () => { f.Labels.Remove(lb); P.Touch(); });
                    Edited?.Invoke(); InvalidateVisual();
                });
                ToolDone();
                return;
            case "rack":
                {
                    var r = new Rack { Id = "rack" + (P.Racks.Count + 1), Name = "Rack " + (char)('A' + P.Racks.Count % 26), Place = f.Key, PX = Clamp(m.X, f.W), PY = Clamp(m.Y, f.H) };
                    while (P.Racks.Any(k => k.Id == r.Id)) r.Id += "x";
                    Undo.Do("add rack", () => { P.Racks.Add(r); P.Touch(); }, () => { P.Racks.Remove(r); P.Touch(); });
                    Say(r.Name + " added. Hold a switch, router, patch panel or server and pick Mount in " + r.Name + ".");
                    Edited?.Invoke();
                    ToolDone(); InvalidateVisual();
                    return;
                }
        }
        var dev = HitDevice(s);
        if (Tool == "connect") { ConnectTap(s); return; }
        if (Tool == "delete")
        {
            object o = Draw.HitTop(s) ?? (object)dev ?? (object)HitLink(s) ?? (object)HitRack(s) ?? Draw.HitBelow(s);
            if (o is Shape || o is Callout) { Edits.Delete(P, Undo, Draw.Expand(o)); Edited?.Invoke(); }
            else if ((o is Device && Draw.LayerLocked("devices")) || (o is Link && Draw.LayerLocked("cables"))) Say("That layer is locked: unlock it under Layers to delete it.");
            else if (o is Rack rk) DeleteRack(rk);
            else if (o != null) { Edits.Delete(P, Undo, new[] { o }); Edited?.Invoke(); }
            InvalidateVisual();
            return;
        }
        if (Tool == "select" && Draw.HandleDown(s)) { drawGesture = true; return; }
        var topHit = Draw.HitTop(s);
        if (topHit != null) dev = null;
        if (clicks >= 2 && topHit is Callout co) { Draw.EditCallout(co); return; }
        var rackHit = dev == null && topHit == null ? HitRack(s) : null;
        if (rackHit != null && clicks >= 2) { ShowRack(rackHit.Id); return; }
        object hitF = topHit ?? (object)dev ?? (object)HitLink(s) ?? (object)rackHit ?? Draw.HitBelow(s);
        if (hitF is Shape shp && clicks >= 2) { Draw.EditText(shp); return; }
        if (hitF == null && clicks >= 2)
        {
            var room = f.RoomAt(m.X, m.Y);
            if (room != null) { ZoomToRoom(room); return; }
        }
        SelectAndDrag(hitF);
    }

    void StartPan() { panning = true; panStart = Pan; }

    void SelectAndDrag(object hit)
    {
        downOn = hit;
        if (hit != null)
        {
            if (!Selection.Contains(hit)) { Selection.Clear(); foreach (var g in Draw.Expand(hit)) Selection.Add(g); }
            dragStart = new Dictionary<Device, Point2>();
            rackStart = new Dictionary<Rack, Point2>();
            if (Level == "floor" && !Draw.LayerLocked("devices")) foreach (var o in Selection.OfType<Device>()) dragStart[o] = new Point2(o.PX, o.PY);
            if (Level == "floor") foreach (var r in Selection.OfType<Rack>()) rackStart[r] = new Point2(r.PX, r.PY);
            drawDrag = new Dictionary<object, Point2>();
            foreach (var o in Draw.Movable()) drawDrag[o] = Edits.Pos(o);
            dragging = true;
        }
        else { Selection.Clear(); StartPan(); }
        SelectionChanged?.Invoke();
        InvalidateVisual();
    }

    /// <summary>The finger moved (pressed = still down).</summary>
    public void MoveTo(Point s, bool pressed = true)
    {
        mouse = s;
        if (Dist(s, downAt) > 6) moved = true;
        if (panning && pressed) { Pan = panStart + (s - downAt); InvalidateVisual(); return; }
        if (drawing) { InvalidateVisual(); return; }
        if (drawGesture || Draw.Busy) { if (Draw.Move(s, pressed)) return; }
        if (dragging && pressed && moved && CurrentFloor != null)
        {
            var d0 = M(downAt); var d1 = M(s);
            var f = CurrentFloor;
            if (Level == "floor")
            {
                foreach (var kv in dragStart) { kv.Key.PX = Clamp(kv.Value.X + d1.X - d0.X, f.W); kv.Key.PY = Clamp(kv.Value.Y + d1.Y - d0.Y, f.H); }
                foreach (var kv in rackStart) { kv.Key.PX = Clamp(kv.Value.X + d1.X - d0.X, f.W); kv.Key.PY = Clamp(kv.Value.Y + d1.Y - d0.Y, f.H); }
            }
            string view = Level == "campus" ? "campus" : f.Key;
            double dx = Drawings.SnapV(view, d1.X - d0.X), dy = Drawings.SnapV(view, d1.Y - d0.Y);
            foreach (var kv in drawDrag) Edits.Set(kv.Key, new Point2(kv.Value.X + dx, kv.Value.Y + dy));
            InvalidateVisual();
            return;
        }
        if (connectFrom != null) InvalidateVisual();
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
        var f = CurrentFloor;
        if (drawing && f != null)
        {
            drawing = false;
            Point a = M(downAt), b = M(Tool == "wall" ? AxisEnd(downAt, s) : s);
            if (Tool == "room" && Math.Abs(a.X - b.X) > 0.5 && Math.Abs(a.Y - b.Y) > 0.5) AddRoom(f, a, b);
            else if (Tool == "wall" && Dist(a, b) > 0.3) AddWall(f, a, b);
            InvalidateVisual();
            return;
        }
        if (panning)
        {
            panning = false;
            if (!moved && downOn is Building bd && bd.Floors.Count > 0) { ShowFloor(bd.Floors[0].Key); Say(bd.Name + ": " + bd.Floors[0].Name + ". Pick another floor at the top."); }
            else if (!moved && downOn is Device rd && Level == "rack") DeviceOpened?.Invoke(rd);
            downOn = null;
            return;
        }
        if (Level == "rack" && downOn is Device rdev && !moved) { DeviceOpened?.Invoke(rdev); downOn = null; return; }
        if (dragging)
        {
            dragging = false;
            if (!moved)
            {
                if (downOn is Device d) DeviceOpened?.Invoke(d);
                else if (downOn is Link l) Say(NetCanvas.LinkTip(l));
                else if (downOn is Rack r) Say(r.Name + ": " + (P.Devices.Any(x => x.Rack == r.Id) ? string.Join(", ", P.Devices.Where(x => x.Rack == r.Id).OrderByDescending(x => x.RackU).Select(x => "U" + x.RackU + " " + x.Name)) : "empty") + ". Double tap it to see its front.");
            }
            else
            {
                int depth = Undo.Depth;
                if (dragStart != null && dragStart.Count > 0) Edits.MovedPhysical(P, Undo, dragStart);
                if (rackStart != null && rackStart.Count > 0)
                {
                    var before = rackStart.ToDictionary(k => k.Key, k => k.Value); var after = rackStart.Keys.ToDictionary(k => k, k => new Point2(k.PX, k.PY));
                    Undo.Push("move rack", () => { foreach (var kv in after) { kv.Key.PX = kv.Value.X; kv.Key.PY = kv.Value.Y; } P.Sim.TopologyChanged(); }, () => { foreach (var kv in before) { kv.Key.PX = kv.Value.X; kv.Key.PY = kv.Value.Y; } P.Sim.TopologyChanged(); });
                }
                if (drawDrag.Count > 0) Edits.Moved(P, Undo, drawDrag);
                while (Undo.Depth - depth >= 2) Undo.JoinLast("move");
                P.Sim.TopologyChanged();
                P.Touch();
                Edited?.Invoke();
            }
        }
        downOn = null;
        InvalidateVisual();
    }

    /// <summary>A tap: down and up at one place (the checks use it as a finger would).</summary>
    public void Tap(Point s, int clicks = 1) { PressAt(s, clicks); ReleaseAt(s); }
    /// <summary>A drag from a to b (the checks use it as a finger would).</summary>
    public void DragFinger(Point a, Point b) { PressAt(a); for (int i = 1; i <= 6; i++) MoveTo(new Point(a.X + (b.X - a.X) * i / 6, a.Y + (b.Y - a.Y) * i / 6)); ReleaseAt(b); }

    void ToolDone() { Tool = "select"; ToolChanged?.Invoke("select"); }

    void AddRoom(Floor f, Point a, Point b)
    {
        string suggested = "Room " + (f.Rooms.Count + 1);
        var r = new Room { Name = suggested, X = Math.Round(Math.Min(a.X, b.X), 1), Y = Math.Round(Math.Min(a.Y, b.Y), 1), W = Math.Round(Math.Abs(a.X - b.X), 1), H = Math.Round(Math.Abs(a.Y - b.Y), 1) };
        Undo.Do("room", () => { f.Rooms.Add(r); P.Sim.TopologyChanged(); P.Touch(); }, () => { f.Rooms.Remove(r); P.Sim.TopologyChanged(); P.Touch(); });
        Edited?.Invoke();
        NetAsk.Text(this, "Room", "Name of the room (put 'closet' in the name for a wiring closet, 'server' for a server room):", suggested, false, name =>
        {
            name = name.Trim();
            if (name.Length == 0) return;
            string lower = name.ToLowerInvariant();
            r.Name = name; r.Kind = lower.Contains("closet") ? "closet" : lower.Contains("server") ? "server" : "room";
            P.Touch(); Edited?.Invoke(); InvalidateVisual();
        });
    }

    void AddWall(Floor f, Point a, Point b)
    {
        var w = new Wall { X1 = Math.Round(a.X, 1), Y1 = Math.Round(a.Y, 1), X2 = Math.Round(b.X, 1), Y2 = Math.Round(b.Y, 1) };
        Undo.Do("wall", () => { f.Walls.Add(w); P.Sim.TopologyChanged(); P.Touch(); }, () => { f.Walls.Remove(w); P.Sim.TopologyChanged(); P.Touch(); });
        Edited?.Invoke();
    }

    void AddDoor(Floor f, Point m)
    {
        Wall best = null; double bd = Math.Max(1.2, 22 / Scale);
        foreach (var w in f.AllWalls())
        {
            double d = DistToSeg(m, new Point(w.X1, w.Y1), new Point(w.X2, w.Y2));
            if (d < bd) { bd = d; best = w; }
        }
        if (best == null) { Say("Tap on a wall to put a door in it."); return; }
        bool vert = Math.Abs(best.X1 - best.X2) < 0.01;
        var door = new Door { Vertical = vert, X = vert ? best.X1 : m.X - 0.45, Y = vert ? m.Y - 0.45 : best.Y1 };
        Undo.Do("door", () => { f.Doors.Add(door); P.Touch(); }, () => { f.Doors.Remove(door); P.Touch(); });
        Say("Door added.");
        Edited?.Invoke();
        InvalidateVisual();
    }
    static double DistToSeg(Point p, Point a, Point b)
    {
        double abx = b.X - a.X, aby = b.Y - a.Y, len2 = abx * abx + aby * aby;
        if (len2 < 1e-9) return Dist(p, a);
        double t = Math.Max(0, Math.Min(1, ((p.X - a.X) * abx + (p.Y - a.Y) * aby) / len2));
        return Dist(new Point(a.X + abx * t, a.Y + aby * t), p);
    }

    void DeleteRack(Rack rk)
    {
        var mounted = P.Devices.Where(d => d.Rack == rk.Id).ToList();
        Undo.Do("delete rack", () => { P.Racks.Remove(rk); foreach (var d in mounted) { d.Rack = ""; d.PX = rk.PX; d.PY = rk.PY; d.Place = rk.Place; } P.Sim.TopologyChanged(); P.Touch(); },
                               () => { P.Racks.Add(rk); foreach (var d in mounted) d.Rack = rk.Id; P.Sim.TopologyChanged(); P.Touch(); });
        Edited?.Invoke();
    }

    // ------------------------------------------------------------ cables with the port picker
    /// <summary>The Cable tool: tap a device, pick its port, then the same on the other device (it can be on another
    /// floor or in another building: switch floors and tap it there).</summary>
    public void ConnectTap(Point s)
    {
        var dev = HitDevice(s);
        if (dev == null) { if (connectFrom != null) { connectFrom = null; Say("Cancelled."); InvalidateVisual(); } else Say("Tap a device to start a cable."); return; }
        if (connectFrom != null && dev == connectFrom.Dev) { Say("Now tap the other device."); return; }
        Picker = PortPanel.Pick(dev, Cable, this, (connectFrom == null ? "Cable from " : "Cable to ") + dev.Name + ": pick a port", PortChosen);
        Say((connectFrom == null ? "Cable from " : "Cable to ") + dev.Name + ": pick a port.");
    }

    void PortChosen(Port p)
    {
        if (connectFrom == null)
        {
            connectFrom = p;
            mouse = DevPoint(p.Dev);
            Say("Cable from " + p.FullName + ": now tap the other device (it can be on another floor or building: switch floors and tap it there).");
            InvalidateVisual();
            return;
        }
        var a = connectFrom;
        connectFrom = null;
        string c = Cable == "auto" ? Project.AutoCable(a, p) : Cable;
        string why = P.CanConnect(a, p, c);
        if (why.Length > 0) { Say(why); InvalidateVisual(); return; }
        var l = Edits.Connect(P, Undo, a, p, c);
        Say("Connected " + a.FullName + " to " + p.FullName + ": " + l.CableName.ToLowerInvariant() + ", " + l.Length.ToString("0.#", CultureInfo.InvariantCulture) + " m." + (l.Status == "up" ? "" : " " + l.Problem));
        Edited?.Invoke();
        InvalidateVisual();
    }

    public void CancelTool() { connectFrom = null; Draw.Cancel(); drawing = false; InvalidateVisual(); }

    // ------------------------------------------------------------ the menu (hold or right-click)
    public void ShowMenuAt(Point s)
    {
        var items = MenuItems(s);
        if (items.Count == 0 || Data.Testing) return;
        var m = new MenuFlyout();
        foreach (var (label, act) in items)
        {
            var a = act;
            var mi = new MenuItem { Header = label, MinHeight = 40 };
            mi.Click += (x, y) => { try { a(); } catch (Exception ex) { Say(ex.Message); } InvalidateVisual(); };
            m.Items.Add(mi);
        }
        m.ShowAt(this, true);
    }

    /// <summary>What the menu at a point offers (the checks run its actions).</summary>
    public List<(string Label, Action Act)> MenuItems(Point s)
    {
        var list = new List<(string, Action)>();
        if (P == null || (Level != "floor" && Level != "campus")) return list;
        var f = CurrentFloor;
        var drawn = Draw.HitTop(s) ?? (Level == "campus" || (HitDevice(s) == null && HitLink(s) == null && HitRack(s) == null) ? Draw.HitBelow(s) : null);
        if (drawn != null)
        {
            if (!Selection.Contains(drawn)) { Selection.Clear(); foreach (var g in Draw.Expand(drawn)) Selection.Add(g); SelectionChanged?.Invoke(); InvalidateVisual(); }
            list.AddRange(Draw.MenuItems(drawn));
            return list;
        }
        if (Level == "campus")
        {
            var b = HitBuilding(s);
            if (b != null) foreach (var fl in b.Floors) { var ff = fl; list.Add(("Go to " + fl.Name, () => ShowFloor(ff.Key))); }
            return list;
        }
        var d = HitDevice(s);
        var link = d == null ? HitLink(s) : null;
        if (link != null)
        {
            list.Add(("About this cable", () => MainView.Current?.Message("Cable", NetCanvas.LinkTip(link))));
            if (!Draw.LayerLocked("cables")) list.Add(("Delete the cable", () => { Edits.Delete(P, Undo, new object[] { link }); Selection.Clear(); Edited?.Invoke(); }));
            list.AddRange(Draw.MenuItems(link));
        }
        if (d != null)
        {
            if (!Selection.Contains(d)) { Selection.Clear(); Selection.Add(d); SelectionChanged?.Invoke(); }
            list.Add(("Open " + d.Name, () => DeviceOpened?.Invoke(d)));
            list.Add(("Cable from a port...", () => { Tool = "connect"; ToolChanged?.Invoke("connect"); ConnectTap(DevPoint(d)); }));
            if (d.IsRackable)
                foreach (var r in P.Racks.Where(k => k.Place == f.Key)) { var rr = r; list.Add(("Mount in " + r.Name, () => Mount(d, rr))); }
            foreach (var fl in P.Layout.AllFloors().Where(x => x != f)) { var ff = fl; list.Add(("Move to " + fl.B.Name + ", " + fl.Name, () => MoveToFloor(d, ff))); }
            list.AddRange(Draw.MenuItems(d));
            if (!Draw.LayerLocked("devices")) list.Add(("Delete", () => { Edits.Delete(P, Undo, new object[] { d }); Selection.Clear(); Edited?.Invoke(); }));
            return list;
        }
        if (link != null) return list;
        var rack = HitRack(s);
        if (rack != null)
        {
            list.Add(("Show the front of " + rack.Name, () => ShowRack(rack.Id)));
            foreach (var md in P.Devices.Where(x => x.Rack == rack.Id).ToList()) { var mdd = md; list.Add(("Take " + md.Name + " out of the rack", () => Unmount(mdd))); }
            list.Add(("Delete " + rack.Name, () => DeleteRack(rack)));
            return list;
        }
        var m = M(s);
        var room = f?.RoomAt(m.X, m.Y);
        if (room != null)
        {
            list.Add(("Zoom into " + room.Name, () => ZoomToRoom(room)));
            list.Add(("Rename " + room.Name + "...", () => NetAsk.Text(this, "Room", "New name:", room.Name, false, n => { n = n.Trim(); if (n.Length == 0) return; string old = room.Name, ok = room.Kind; string nk = n.ToLowerInvariant().Contains("closet") ? "closet" : n.ToLowerInvariant().Contains("server") ? "server" : ok == "hall" ? "hall" : "room"; Undo.Do("rename room", () => { room.Name = n; room.Kind = nk; P.Touch(); }, () => { room.Name = old; room.Kind = ok; P.Touch(); }); Edited?.Invoke(); InvalidateVisual(); })));
            list.Add(("Remove room " + room.Name, () => { Undo.Do("remove room", () => { f.Rooms.Remove(room); P.Sim.TopologyChanged(); P.Touch(); }, () => { f.Rooms.Add(room); P.Sim.TopologyChanged(); P.Touch(); }); Edited?.Invoke(); }));
        }
        var lb = f?.Labels.FirstOrDefault(x => Math.Abs(x.X - m.X) < 1.5 && Math.Abs(x.Y - m.Y) < 0.8);
        if (lb != null) list.Add(("Remove the label " + lb.Text, () => { Undo.Do("remove label", () => { f.Labels.Remove(lb); P.Touch(); }, () => { f.Labels.Add(lb); P.Touch(); }); Edited?.Invoke(); }));
        if (f != null && f.Walls.Count > 0) list.Add(("Remove the last wall drawn", () => { var w = f.Walls[^1]; Undo.Do("remove wall", () => { f.Walls.Remove(w); P.Sim.TopologyChanged(); P.Touch(); }, () => { f.Walls.Add(w); P.Sim.TopologyChanged(); P.Touch(); }); Edited?.Invoke(); }));
        if (f != null && f.Doors.Count > 0) list.Add(("Remove the last door", () => { var w = f.Doors[^1]; Undo.Do("remove door", () => { f.Doors.Remove(w); P.Touch(); }, () => { f.Doors.Add(w); P.Touch(); }); Edited?.Invoke(); }));
        return list;
    }

    /// <summary>Mounts a device in a rack at the highest free place (2 U each).</summary>
    public void Mount(Device d, Rack r)
    {
        int top = P.Devices.Where(x => x.Rack == r.Id && x != d).Select(x => x.RackU).DefaultIfEmpty(r.Units + 1).Min() - UnitsOf(d);
        int u = Math.Max(1, Math.Min(r.Units, top));
        string oldRack = d.Rack, oldPlace = d.Place; int oldU = d.RackU;
        Undo.Do("mount " + d.Name, () => { d.Rack = r.Id; d.RackU = u; d.Place = r.Place; P.Sim.TopologyChanged(); P.Touch(); }, () => { d.Rack = oldRack; d.RackU = oldU; d.Place = oldPlace; P.Sim.TopologyChanged(); P.Touch(); });
        Say(d.Name + " is in " + r.Name + " at U" + u + ". Double tap the rack to see its front.");
        Edited?.Invoke();
    }

    public void Unmount(Device d)
    {
        var r = P.Racks.FirstOrDefault(k => k.Id == d.Rack);
        string oldRack = d.Rack; double ox = d.PX, oy = d.PY;
        Undo.Do("take out of the rack", () => { if (r != null) { d.PX = r.PX + 1; d.PY = r.PY; d.Place = r.Place; } d.Rack = ""; P.Sim.TopologyChanged(); P.Touch(); }, () => { d.Rack = oldRack; d.PX = ox; d.PY = oy; P.Sim.TopologyChanged(); P.Touch(); });
        Edited?.Invoke();
    }

    public void MoveToFloor(Device d, Floor ff)
    {
        string op = d.Place, orack = d.Rack; double ox = d.PX, oy = d.PY;
        Undo.Do("move to " + ff.Name, () => { d.Place = ff.Key; d.Rack = ""; d.PX = ff.W / 2; d.PY = ff.H / 2; P.Sim.TopologyChanged(); P.Touch(); }, () => { d.Place = op; d.Rack = orack; d.PX = ox; d.PY = oy; P.Sim.TopologyChanged(); P.Touch(); });
        Say(d.Name + " moved to " + ff.B.Name + ", " + ff.Name + ".");
        Edited?.Invoke();
    }

    /// <summary>Puts a new device on the floor shown, in the middle of the view.</summary>
    public void PlaceHere(Device d)
    {
        var f = CurrentFloor;
        if (f == null || Level != "floor") return;
        var c = M(new Point(W / 2, H / 2));
        double x = Clamp(c.X, f.W), y = Clamp(c.Y, f.H);
        for (int k = 0; k < 30 && P.Devices.Any(o => o != d && o.Place == f.Key && o.Rack.Length == 0 && Math.Abs(o.PX - x) < 1.5 && Math.Abs(o.PY - y) < 1.5); k++) { x = Clamp(x + 2, f.W); if (k % 5 == 4) { x = Clamp(x - 10, f.W); y = Clamp(y + 2, f.H); } }
        d.Place = f.Key; d.Rack = ""; d.PX = Math.Round(x, 1); d.PY = Math.Round(y, 1);
        P.Sim.TopologyChanged();
    }

    /// <summary>Changes the site to one of the layouts: devices go to its first floor (cables and settings stay).</summary>
    public void UseLayout(string id)
    {
        string before = Json.Write(Physical.SaveLayout(P.Layout));
        var places = P.Devices.ToDictionary(d => d, d => (d.Place, d.Rack, d.PX, d.PY, d.RackU));
        var racks = P.Racks.ToList();
        void DoIt()
        {
            P.Layout = Layouts.Create(id);
            foreach (var d in P.Devices) { d.Place = ""; d.Rack = ""; }
            P.Racks.Clear();
            foreach (var d in P.Devices) Physical.PlaceNew(P, d);
            P.Sim.TopologyChanged(); P.Touch();
        }
        DoIt();
        string after = Json.Write(Physical.SaveLayout(P.Layout));
        var placed = P.Devices.ToDictionary(d => d, d => (d.Place, d.Rack, d.PX, d.PY, d.RackU));
        Undo.Push("site: " + P.Layout.Name, () => { P.Layout = Physical.LoadLayout(Json.ParseObj(after)); P.Racks.Clear(); foreach (var kv in placed) { kv.Key.Place = kv.Value.Place; kv.Key.Rack = kv.Value.Rack; kv.Key.PX = kv.Value.PX; kv.Key.PY = kv.Value.PY; } P.Sim.TopologyChanged(); FloorKey = ""; },
                                             () => { P.Layout = Physical.LoadLayout(Json.ParseObj(before)); P.Racks.Clear(); P.Racks.AddRange(racks); foreach (var kv in places) { kv.Key.Place = kv.Value.Place; kv.Key.Rack = kv.Value.Rack; kv.Key.PX = kv.Value.PX; kv.Key.PY = kv.Value.PY; kv.Key.RackU = kv.Value.RackU; } P.Sim.TopologyChanged(); FloorKey = ""; });
        FloorKey = P.Layout.DefaultFloor?.Key ?? "";
        Level = "floor";
        Selection.Clear();
        Fit();
        LevelChanged?.Invoke();
        Edited?.Invoke();
    }
}
