// Network Lab - the drawing tools (App Hub's DrawTool.cs for Avalonia, touch first), shared by the Logical view
// (NetCanvas) and the Physical view (PhysicalView) through IDrawHost: drawing shapes, lines, text, notes, areas,
// pictures and objects, the handles that resize and turn a drawing (big enough for a finger), hit testing, the map
// scale line, copy and paste, and the commands of the properties panel. Drawings are decoration only: nothing here
// touches the simulation or a lab's goals.
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using NetworkLab;
using LearningLab.Views;
using Rect = Avalonia.Rect;

namespace LearningLab.NetworkLab;

/// <summary>What a view tells the drawing tools (NetCanvas and PhysicalView).</summary>
public interface IDrawHost
{
    Project DrawProject { get; }
    UndoStack DrawUndo { get; }
    /// <summary>The shapes' view key here (logical, campus, a floor key), or "" where nothing can be drawn.</summary>
    string DrawView { get; }
    /// <summary>logical / physical: where labels on devices show here ("" = none).</summary>
    string CalloutView { get; }
    double DrawScale { get; }
    double DrawBase { get; }
    Point DrawToScreen(double x, double y);
    Point DrawToView(Point s);
    HashSet<object> DrawSelection { get; }
    bool CalloutAnchor(Callout c, out Point screen);
    object DrawTargetAt(Point s);
    void DrawRedraw();
    void DrawSay(string message);
    void DrawEdited();
    void DrawSelected();
    void DrawToolDone();
    /// <summary>The control questions are shown over.</summary>
    Control DrawControl { get; }
}

/// <summary>A short question with a text box (a flyout over the view). The checks answer through Auto.</summary>
public static class NetAsk
{
    /// <summary>For the checks: (title, suggested text) -> the answer, or null to cancel. Never a flyout then.</summary>
    public static Func<string, string, string> Auto;

    public static void Text(Control anchor, string title, string prompt, string value, bool multiline, Action<string> ok)
    {
        if (Auto != null) { var a = Auto(title, value); if (a != null) ok(a); return; }
        if (anchor == null) return;
        var fl = new Flyout();
        var box = new TextBox { Text = value ?? "", MinWidth = 240, MinHeight = multiline ? 90 : 44, AcceptsReturn = multiline, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap };
        var go = Ui.Button("OK", true, 14);
        var cancel = Ui.Button("Cancel", false, 14);
        go.Click += (s, e) => { fl.Hide(); ok(box.Text ?? ""); };
        cancel.Click += (s, e) => fl.Hide();
        box.KeyDown += (s, e) => { if (e.Key == Key.Enter && (!multiline || (e.KeyModifiers & KeyModifiers.Control) != 0)) { fl.Hide(); ok(box.Text ?? ""); e.Handled = true; } };
        var sp = new StackPanel { Spacing = 8, MaxWidth = 320, Children = { Ui.Text(title, 16, Ui.TextC, true) } };
        if (!string.IsNullOrEmpty(prompt)) sp.Children.Add(new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap, FontSize = 13, Foreground = Ui.Brush(Ui.Muted) });
        sp.Children.Add(box);
        sp.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancel, go } });
        fl.Content = sp;
        fl.ShowAt(anchor, true);
        Dispatcher.UIThread.Post(() => { box.Focus(); box.SelectAll(); }, DispatcherPriority.Input);
    }

    public static double Number(string s) { double.TryParse((s ?? "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v); return v; }
}

public sealed class DrawTool
{
    public readonly IDrawHost Host;
    /// <summary>The picture tool was used at (x, y) in view units: the page asks for a file and calls AddPicture.</summary>
    public event Action<double, double> PictureWanted;
    /// <summary>What was copied last (the drawings' own text format; also into another network).</summary>
    public static string LastClip = "";
    /// <summary>Shift held (keeps squares and straight lines; set from the pointer's key modifiers).</summary>
    public static bool Shift;
    /// <summary>How near a finger must be to a handle (screen pixels).</summary>
    public const double HandleReach = 18;

    public DrawTool(IDrawHost host) { Host = host; }

    Project P => Host.DrawProject;
    string View => Host.DrawView;
    double K => Host.DrawScale;
    double FK => Host.DrawScale / Host.DrawBase;
    Point S(double x, double y) => Host.DrawToScreen(x, y);
    Point2 Wv(Point s) { var p = Host.DrawToView(s); return new Point2(p.X, p.Y); }
    Point2 Snapped(Point s) { var w = Wv(s); return new Point2(Drawings.SnapV(View, w.X), Drawings.SnapV(View, w.Y)); }

    public bool CanDraw => P != null && View.Length > 0;
    public LayerSet Layers => Drawings.LayersOf(P, Drawings.LayerKeyOf(View));
    public bool LayerShows(string id) => P == null || View.Length == 0 || Layers.Shows(id);
    public bool LayerLocked(string id) => P != null && View.Length > 0 && Layers.IsLocked(id);
    static string LayerOf(Shape s) => s.Layer == "map" ? "map" : s.Layer == "labels" ? "labels" : "drawings";

    IEnumerable<Shape> Here() => P == null ? Enumerable.Empty<Shape>() : P.Shapes.Where(s => s.View == View);

    public List<Shape> SelectedShapes() => P == null ? new List<Shape>() : Host.DrawSelection.OfType<Shape>().Where(s => s.View == View && P.Shapes.Contains(s)).ToList();
    public List<Callout> SelectedCallouts() => P == null ? new List<Callout>() : Host.DrawSelection.OfType<Callout>().Where(c => P.Callouts.Contains(c)).ToList();

    static double Dist(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    static Point Mid(Point a, Point b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);
    static double Len(Point2 a, Point2 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    // ================================================================= drawing
    public void RenderMap(DrawingContext dc, Rect area)
    {
        if (!CanDraw || !LayerShows("map")) return;
        if (P.Maps.TryGetValue(View, out var m)) DrawArt.MapUnder(dc, m, area, S, s => Host.DrawToView(s), K, View);
        foreach (var s in Here().Where(x => x.Layer == "map")) DrawArt.DrawShape(dc, s, S, K, FK);
    }
    /// <summary>True when the view has a map of its own (then it doesn't draw its usual dotted background).</summary>
    public bool HasMap
    {
        get
        {
            if (!CanDraw || !LayerShows("map")) return false;
            return (P.Maps.TryGetValue(View, out var m) && (m.Style != "none" || m.Picture.Length > 0)) || Here().Any(x => x.Layer == "map");
        }
    }
    public void RenderBelow(DrawingContext dc)
    {
        if (!CanDraw) return;
        foreach (var s in Here().Where(x => x.Layer != "map" && !x.Above && LayerShows(LayerOf(x))).ToList()) DrawArt.DrawShape(dc, s, S, K, FK);
    }
    public void RenderAbove(DrawingContext dc)
    {
        if (!CanDraw) return;
        foreach (var s in Here().Where(x => x.Layer != "map" && x.Above && LayerShows(LayerOf(x))).ToList()) DrawArt.DrawShape(dc, s, S, K, FK);
    }

    public Rect CalloutRect(Callout c, out Point anchor, out FormattedText text)
    {
        text = null;
        if (!Host.CalloutAnchor(c, out anchor)) return default;
        double f = Math.Max(0.7, Math.Min(1.6, FK));
        text = DrawArt.Fmt(c.Text, Math.Max(7, c.FontSize * f), DrawArt.Br(c.Color) ?? Brushes.White, true);
        var ctr = new Point(anchor.X + c.DX * K, anchor.Y + c.DY * K);
        return new Rect(ctr.X - text.Width / 2 - 6, ctr.Y - text.Height / 2 - 3, text.Width + 12, text.Height + 6);
    }

    public void RenderLabels(DrawingContext dc)
    {
        if (P == null || Host.CalloutView.Length == 0 || !LayerShows("labels")) return;
        foreach (var c in P.Callouts.Where(x => x.View == Host.CalloutView))
        {
            var r = CalloutRect(c, out var a, out var t);
            if (r.Width <= 0 || t == null) continue;
            bool sel = Host.DrawSelection.Contains(c);
            var ctr = r.Center;
            if (Dist(ctr, a) > r.Height * 1.2)
            {
                var edge = Clip(r, a, ctr);
                dc.DrawLine(new Pen(NetIcons.B("#94A3B8"), 1.2, new DashStyle(new double[] { 1, 2 }, 0)), a, edge);
                dc.DrawEllipse(NetIcons.B("#94A3B8"), null, a, 2.2, 2.2);
            }
            dc.DrawRectangle(DrawArt.Br(c.Fill), new Pen(NetIcons.B(sel ? "#3B82F6" : "#64748B"), sel ? 2 : 1), r, 5, 5);
            dc.DrawText(t, new Point(r.X + 6, r.Y + 3));
        }
    }

    static Point Clip(Rect r, Point outside, Point centre)
    {
        double vx = outside.X - centre.X, vy = outside.Y - centre.Y;
        double tx = Math.Abs(vx) < 1e-6 ? double.MaxValue : (r.Width / 2) / Math.Abs(vx);
        double ty = Math.Abs(vy) < 1e-6 ? double.MaxValue : (r.Height / 2) / Math.Abs(vy);
        double t = Math.Min(1, Math.Min(tx, ty));
        return new Point(centre.X + vx * t, centre.Y + vy * t);
    }

    public void RenderOverlay(DrawingContext dc)
    {
        if (!CanDraw) return;
        var blue = NetIcons.B("#3B82F6");
        var sel = SelectedShapes();
        var dash = new Pen(blue, 1.2, new DashStyle(new double[] { 4, 3 }, 0));
        foreach (var s in sel)
        {
            if (s.IsLine && s.Points.Count >= 2 && s.Kind != "freehand" && s.Kind != "polyline") continue;
            dc.DrawGeometry(null, dash, DrawArt.Geo(true, Corners(s)));
        }
        if (sel.Count == 1 && !sel[0].Locked && !LayerLocked(LayerOf(sel[0])))
            foreach (var h in Handles(sel[0]))
            {
                if (h.Key == "rot")
                {
                    var c = Corners(sel[0]);
                    dc.DrawLine(new Pen(blue, 1), Mid(c[0], c[1]), h.Value);
                    dc.DrawEllipse(NetIcons.B("#F8FAFC"), new Pen(blue, 2), h.Value, 8, 8);
                    dc.DrawEllipse(null, new Pen(blue, 1.5), h.Value, 3.5, 3.5);
                }
                else dc.DrawRectangle(NetIcons.B("#F8FAFC"), new Pen(blue, 2), new Rect(h.Value.X - 6, h.Value.Y - 6, 12, 12), 2, 2);
            }
        if (sel.Count == 1 && (sel[0].Locked || LayerLocked(LayerOf(sel[0]))))
        {
            var c = Corners(sel[0]);
            var t = DrawArt.Fmt("locked", 10.5, NetIcons.B("#FBBF24"), true);
            dc.DrawText(t, new Point(c[0].X, c[0].Y - t.Height - 2));
        }
        if (preview != null)
        {
            using (dc.PushOpacity(0.75)) DrawArt.DrawShape(dc, preview, S, K, FK);
            if (Tool == "draw:calibrate" && preview.Points.Count == 2)
            {
                double len = Len(preview.Points[0], preview.Points[1]);
                var t = DrawArt.Fmt(View == "logical" ? len.ToString("0", CultureInfo.InvariantCulture) + " px" : len.ToString("0.#", CultureInfo.InvariantCulture) + " m on the map now", 12, NetIcons.B("#FDE68A"), true);
                var e = S(preview.Points[1].X, preview.Points[1].Y);
                dc.DrawText(t, new Point(e.X + 10, e.Y - 8));
            }
        }
    }

    /// <summary>The four corners of a shape's box on screen (top-left, top-right, bottom-right, bottom-left).</summary>
    public Point[] Corners(Shape s)
    {
        Point a = S(s.X, s.Y), b = S(s.X + s.W, s.Y + s.H);
        double pad = s.IsLine ? 4 : 0;
        var pts = new[] { new Point(a.X - pad, a.Y - pad), new Point(b.X + pad, a.Y - pad), new Point(b.X + pad, b.Y + pad), new Point(a.X - pad, b.Y + pad) };
        if (s.IsLine || Math.Abs(s.Rotation) < 0.01) return pts;
        var c = Mid(a, b);
        for (int i = 0; i < 4; i++) pts[i] = DrawArt.Turn(pts[i], s.Rotation, c);
        return pts;
    }

    /// <summary>Handles of one selected shape: n, ne, e, se, s, sw, w, nw, rot (box shapes) or p0, p1 (lines).</summary>
    public List<KeyValuePair<string, Point>> Handles(Shape s)
    {
        var list = new List<KeyValuePair<string, Point>>();
        if ((s.Kind == "line" || s.Kind == "arrow") && s.Points.Count == 2)
        {
            list.Add(new("p0", S(s.Points[0].X, s.Points[0].Y)));
            list.Add(new("p1", S(s.Points[1].X, s.Points[1].Y)));
            return list;
        }
        var c = Corners(s);
        list.Add(new("nw", c[0])); list.Add(new("n", Mid(c[0], c[1])));
        list.Add(new("ne", c[1])); list.Add(new("e", Mid(c[1], c[2])));
        list.Add(new("se", c[2])); list.Add(new("s", Mid(c[2], c[3])));
        list.Add(new("sw", c[3])); list.Add(new("w", Mid(c[3], c[0])));
        if (!s.IsLine)
        {
            var top = Mid(c[0], c[1]); var ctr = Mid(c[0], c[2]);
            double vx = top.X - ctr.X, vy = top.Y - ctr.Y, l = Math.Sqrt(vx * vx + vy * vy);
            if (l < 1) { vx = 0; vy = -1; l = 1; }
            list.Add(new("rot", new Point(top.X + vx / l * 30, top.Y + vy / l * 30)));
        }
        return list;
    }

    // ================================================================= hit testing
    bool Hittable(Shape s) => LayerShows(LayerOf(s)) && !LayerLocked(LayerOf(s));

    public bool HitShape(Shape s, Point p)
    {
        double tol = 6;
        if (s.IsLine)
        {
            var pts = s.Points.Select(x => S(x.X, x.Y)).ToList();
            double half = DrawArt.PenWidth(s, FK) / 2;
            for (int i = 1; i < pts.Count; i++) if (DistSeg(p, pts[i - 1], pts[i]) < tol + half) return true;
            if (s.Closed && DrawArt.Br(s.Fill) != null && pts.Count > 2 && DrawArt.Geo(true, pts.ToArray()).FillContains(p)) return true;
            return false;
        }
        // into the shape's own (unturned) frame
        Point a = S(s.X, s.Y), b = S(s.X + s.W, s.Y + s.H);
        var ctr = Mid(a, b);
        if (Math.Abs(s.Rotation) > 0.01) p = DrawArt.Turn(p, -s.Rotation, ctr);
        var r = new Rect(a, b);
        if (s.Kind == "object" && (s.Text ?? "").Length > 0) r = r.WithHeight(r.Height + 16);
        bool filled = DrawArt.Br(s.Fill) != null || s.Kind == "text" || s.Kind == "note" || s.Kind == "image" || s.Kind == "object" || s.Kind == "area";
        if (s.Kind == "ellipse")
        {
            double rx = r.Width / 2, ry = r.Height / 2;
            if (rx < 1 || ry < 1) return false;
            double d = Math.Pow((p.X - ctr.X) / rx, 2) + Math.Pow((p.Y - ctr.Y) / ry, 2);
            if (filled) return d <= 1.05;
            double edge = tol / Math.Min(rx, ry);
            return d <= Math.Pow(1 + edge, 2) && d >= Math.Pow(Math.Max(0, 1 - edge), 2);
        }
        var outer = r.Inflate(tol);
        if (!outer.Contains(p)) return false;
        if (filled) return true;
        if (r.Width <= tol * 2 || r.Height <= tol * 2) return true;
        return !r.Deflate(tol).Contains(p);
    }

    static double DistSeg(Point p, Point a, Point b)
    {
        double abx = b.X - a.X, aby = b.Y - a.Y, len2 = abx * abx + aby * aby;
        if (len2 < 1e-9) return Dist(p, a);
        double t = Math.Max(0, Math.Min(1, ((p.X - a.X) * abx + (p.Y - a.Y) * aby) / len2));
        return Dist(new Point(a.X + abx * t, a.Y + aby * t), p);
    }

    /// <summary>Drawings above the devices and the labels on devices (checked before the devices).</summary>
    public object HitTop(Point p)
    {
        if (P == null) return null;
        if (Host.CalloutView.Length > 0 && LayerShows("labels") && !LayerLocked("labels"))
            for (int i = P.Callouts.Count - 1; i >= 0; i--)
            {
                var c = P.Callouts[i];
                if (c.View != Host.CalloutView) continue;
                var r = CalloutRect(c, out _, out _);
                if (r.Width > 0 && r.Inflate(4).Contains(p)) return c;
            }
        if (!CanDraw) return null;
        var here = Here().ToList();
        for (int i = here.Count - 1; i >= 0; i--) if (here[i].Above && here[i].Layer != "map" && Hittable(here[i]) && HitShape(here[i], p)) return here[i];
        return null;
    }

    /// <summary>Drawings under the devices, then the map's pieces (checked after the devices and cables).</summary>
    public object HitBelow(Point p)
    {
        if (!CanDraw) return null;
        var here = Here().ToList();
        for (int i = here.Count - 1; i >= 0; i--) if (!here[i].Above && here[i].Layer != "map" && Hittable(here[i]) && HitShape(here[i], p)) return here[i];
        for (int i = here.Count - 1; i >= 0; i--) if (here[i].Layer == "map" && Hittable(here[i]) && HitShape(here[i], p)) return here[i];
        return null;
    }

    /// <summary>A tap on a grouped drawing picks the whole group.</summary>
    public List<object> Expand(object hit)
    {
        if (hit is not Shape s || s.Group.Length == 0) return new List<object> { hit };
        return P.Shapes.Where(x => x.Group == s.Group).Cast<object>().ToList();
    }

    /// <summary>Things that move when the selection is dragged (locked drawings and layers stay put; labels of
    /// selected devices follow their device by themselves).</summary>
    public List<object> Movable()
    {
        var list = new List<object>();
        foreach (var s in SelectedShapes()) if (!s.Locked && !LayerLocked(LayerOf(s))) list.Add(s);
        var ids = new HashSet<string>(Host.DrawSelection.OfType<Device>().Select(d => d.Id).Concat(Host.DrawSelection.OfType<Link>().Select(l => l.Id)));
        foreach (var c in SelectedCallouts()) if (!ids.Contains(c.Target) && !LayerLocked("labels")) list.Add(c);
        return list;
    }

    // ================================================================= the tools
    public string Tool = "";
    Shape preview;
    bool creating;
    Point2 start;
    string handle = "";
    Shape handleShape;
    DrawState handleBefore;
    Shape handleOrig;
    double rotStart;
    int pasteCount;

    public static bool IsDrawTool(string t) => t != null && (t.StartsWith("draw:") || t.StartsWith("obj:"));
    public bool Busy => creating || handle.Length > 0;
    /// <summary>The shape being drawn (for the checks).</summary>
    public Shape Preview => preview;

    /// <summary>A finger (or the mouse) went down with a drawing tool. True = the view should follow the pointer.</summary>
    public bool Down(string tool, Point s, int clicks)
    {
        Tool = tool;
        if (!CanDraw) { Host.DrawSay("Drawings go on the Logical view, the Campus map and the floor plans."); return false; }
        var w = Snapped(s);
        if (tool.StartsWith("obj:")) { PlaceObject(tool.Substring(4), w.X, w.Y); return false; }
        string kind = tool.Substring(5);
        switch (kind)
        {
            case "text":
            case "note":
                {
                    var sh = Drawings.Make(P, kind, View, w.X, w.Y, 160 / Host.DrawBase, kind == "note" ? 110 / Host.DrawBase : 34 / Host.DrawBase);
                    if (kind == "note") sh.Text = "Note";
                    NetAsk.Text(Host.DrawControl, kind == "note" ? "Sticky note" : "Text", kind == "note" ? "What the note says:" : "The text:", sh.Text, true, t =>
                    {
                        if (t.Trim().Length > 0) sh.Text = t;
                        if (kind == "text") FitText(sh);
                        Drawings.Add(P, Host.DrawUndo, sh, null);
                        Done(sh);
                    });
                    return false;
                }
            case "image":
                PictureWanted?.Invoke(w.X, w.Y);
                Host.DrawToolDone();
                return false;
            case "label":
                {
                    var target = Host.DrawTargetAt(s);
                    if (target == null) { Host.DrawSay("Tap a device or a cable to give it a label."); return false; }
                    AskLabel(target);
                    Host.DrawToolDone();
                    return false;
                }
            case "polyline":
                {
                    if (!creating)
                    {
                        creating = true;
                        preview = Drawings.Make(P, "polyline", View, 0, 0, 0, 0);
                        preview.Points.Add(w); preview.Points.Add(w);
                        Host.DrawSay("Tap corner after corner; tap the last corner again (or press Finish) to finish.");
                        return true;
                    }
                    var last = preview.Points[^2];
                    if (clicks >= 2 || (preview.Points.Count > 2 && Len(last, w) * K < 12)) { FinishPolyline(); return false; }
                    preview.Points[^1] = w;
                    preview.Points.Add(w);
                    Host.DrawRedraw();
                    return true;
                }
        }
        creating = true;
        start = w;
        if (kind == "line" || kind == "arrow" || kind == "calibrate" || kind == "freehand")
        {
            preview = Drawings.Make(P, kind == "calibrate" ? "line" : kind, View, 0, 0, 0, 0);
            if (kind == "calibrate") { preview.Stroke = "#FDE68A"; preview.Dash = "dash"; preview.ArrowStart = true; preview.ArrowEnd = true; }
            var first = kind == "freehand" ? Wv(s) : w;
            preview.Points.Add(first); preview.Points.Add(first);
            preview.FitPoints();
        }
        else preview = Drawings.Make(P, kind, View, w.X, w.Y, 0, 0);
        return true;
    }

    /// <summary>True while drawing a polyline (the page shows a Finish button).</summary>
    public bool DrawingLines => creating && preview != null && preview.Kind == "polyline";

    void FitText(Shape sh)
    {
        var t = DrawArt.TextOf(sh, 1, 0);
        sh.W = Math.Max(30, t.WidthIncludingTrailingWhitespace + 12) / Host.DrawBase;
        sh.H = Math.Max(20, t.Height + 8) / Host.DrawBase;
    }

    public bool Move(Point s, bool pressed)
    {
        if (handle.Length > 0) { DragHandle(s); Host.DrawRedraw(); return true; }
        if (!creating || preview == null) return false;
        if (preview.Kind == "polyline") { preview.Points[^1] = Snapped(s); preview.FitPoints(); Host.DrawRedraw(); return true; }
        if (!pressed) return true;
        var w = Snapped(s);
        if (preview.Kind == "freehand")
        {
            var raw = Wv(s);
            if (Len(preview.Points[^1], raw) * K >= 3) preview.Points.Add(raw);
            preview.FitPoints();
        }
        else if (preview.IsLine)
        {
            if (Shift) w = Snap45(start, w);
            preview.Points[1] = w;
            preview.FitPoints();
        }
        else
        {
            double x0 = Math.Min(start.X, w.X), y0 = Math.Min(start.Y, w.Y), ww = Math.Abs(w.X - start.X), hh = Math.Abs(w.Y - start.Y);
            if (Shift) { double m = Math.Max(ww, hh); ww = m; hh = m; x0 = w.X < start.X ? start.X - m : start.X; y0 = w.Y < start.Y ? start.Y - m : start.Y; }
            preview.X = x0; preview.Y = y0; preview.W = ww; preview.H = hh;
        }
        Host.DrawRedraw();
        return true;
    }

    static Point2 Snap45(Point2 a, Point2 b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, len = Math.Sqrt(dx * dx + dy * dy);
        double ang = Math.Round(Math.Atan2(dy, dx) / (Math.PI / 4)) * (Math.PI / 4);
        return new Point2(a.X + Math.Cos(ang) * len, a.Y + Math.Sin(ang) * len);
    }

    /// <summary>The finger came up. True = it was ours.</summary>
    public bool Up(Point s)
    {
        if (handle.Length > 0)
        {
            handle = "";
            Drawings.Commit(P, Host.DrawUndo, "change " + Drawings.KindName(handleShape).ToLowerInvariant(), handleBefore);
            handleShape = null;
            Host.DrawEdited();
            Host.DrawSelected();
            return true;
        }
        if (!creating || preview == null) return false;
        if (preview.Kind == "polyline") return true;
        creating = false;
        var sh = preview; preview = null;
        double minPx = 4;
        if (Tool == "draw:calibrate")
        {
            Host.DrawRedraw();
            double units = Len(sh.Points[0], sh.Points[1]);
            if (units * K < 10) { Host.DrawSay("Drag a longer line along something whose length you know (a road, a building, a wall)."); return true; }
            NetAsk.Text(Host.DrawControl, "Map scale", "How many metres long is the line you drew?", "100", false, v =>
            {
                double m = NetAsk.Number(v);
                if (m > 0) Calibrate(units, m);
            });
            Host.DrawToolDone();
            return true;
        }
        if (sh.IsLine)
        {
            if (sh.Kind != "freehand" && Len(sh.Points[0], sh.Points[1]) * K < minPx)
                sh.Points[1] = new Point2(sh.Points[0].X + 120 / Host.DrawBase, sh.Points[0].Y);   // a tap: a short line to the right
            if (sh.Kind == "freehand" && sh.Points.Count < 3) { Host.DrawRedraw(); return true; }
            if (sh.Kind == "freehand") Simplify(sh);
            sh.FitPoints();
        }
        else if (sh.W * K < minPx || sh.H * K < minPx)
        {
            // a tap: the usual size
            double w = (sh.Kind == "area" ? 260 : 120) / Host.DrawBase, h = (sh.Kind == "area" ? 170 : 80) / Host.DrawBase;
            if (Shift) h = w;
            sh.W = w; sh.H = h;
        }
        if (sh.Kind == "area")
        {
            Drawings.Add(P, Host.DrawUndo, sh, null);
            Done(sh);
            NetAsk.Text(Host.DrawControl, "Area", "A title for the area (for example Sales VLAN 20, DMZ, Building A):", "Area", false, t => SetText(sh, t.Trim()));
            return true;
        }
        Drawings.Add(P, Host.DrawUndo, sh, null);
        Done(sh);
        return true;
    }

    static void Simplify(Shape sh)
    {
        // drop points that sit on a nearly straight line
        if (sh.Points.Count < 4) return;
        var keep = new List<Point2> { sh.Points[0] };
        for (int i = 1; i < sh.Points.Count - 1; i++)
        {
            var a = keep[^1]; var b = sh.Points[i]; var c = sh.Points[i + 1];
            double cross = Math.Abs((b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X));
            if (cross > 0.0001 * Math.Max(1, Len(a, c) * Len(a, c)) * 40) keep.Add(b);
        }
        keep.Add(sh.Points[^1]);
        sh.Points = keep;
    }

    public void FinishPolyline()
    {
        if (preview == null) { creating = false; return; }
        var sh = preview; preview = null; creating = false;
        // the last point follows the finger: drop it (and duplicates)
        if (sh.Points.Count > 2) sh.Points.RemoveAt(sh.Points.Count - 1);
        var pts = new List<Point2>();
        foreach (var q in sh.Points) if (pts.Count == 0 || Len(pts[^1], q) * K > 2) pts.Add(q);
        sh.Points = pts;
        if (sh.Points.Count < 2) { Host.DrawRedraw(); Host.DrawToolDone(); return; }
        sh.FitPoints();
        Drawings.Add(P, Host.DrawUndo, sh, null);
        Done(sh);
    }

    void Done(Shape sh)
    {
        Host.DrawSelection.Clear();
        Host.DrawSelection.Add(sh);
        Host.DrawSay(Drawings.KindName(sh) + " added. Drag its handles to resize or turn it; Properties changes its colours and text.");
        Host.DrawEdited();
        Host.DrawSelected();
        if (!Shift) Host.DrawToolDone();
        Host.DrawRedraw();
    }

    public void Cancel()
    {
        if (creating && preview != null && preview.Kind == "polyline" && preview.Points.Count > 2) { FinishPolyline(); return; }
        creating = false; preview = null; handle = "";
        Host.DrawRedraw();
    }

    public Shape PlaceObject(string art, double x, double y)
    {
        if (!CanDraw) return null;
        var sh = Drawings.MakeObject(P, art, View, x, y);
        Drawings.Add(P, Host.DrawUndo, sh, null);
        Done(sh);
        return sh;
    }

    /// <summary>A picture as a drawing, centred on (x, y).</summary>
    public Shape AddPicture(byte[] bytes, double x, double y)
    {
        if (!CanDraw) return null;
        if (bytes.Length > 6 * 1024 * 1024) throw new InvalidOperationException("That picture is bigger than 6 MB: please use a smaller one.");
        DrawArt.PixelSize(bytes, out double pw, out double ph);
        double w = View == "logical" ? Math.Min(320, pw) : View == "campus" ? 60 : 6;
        double h = pw > 0 ? w * ph / pw : w * 0.75;
        var sh = Drawings.Make(P, "image", View, x - w / 2, y - h / 2, w, h);
        sh.Picture = Convert.ToBase64String(bytes);
        Drawings.Add(P, Host.DrawUndo, sh, "add picture");
        Done(sh);
        return sh;
    }

    // ================================================================= handles: resize, turn, move line ends
    public bool HandleDown(Point s)
    {
        if (!CanDraw) return false;
        var sel = SelectedShapes();
        if (sel.Count != 1 || sel[0].Locked || LayerLocked(LayerOf(sel[0]))) return false;
        KeyValuePair<string, Point>? best = null; double bd = HandleReach;
        foreach (var h in Handles(sel[0])) { double d = Dist(h.Value, s); if (d <= bd) { bd = d; best = h; } }
        if (best == null) return false;
        handle = best.Value.Key;
        handleShape = sel[0];
        handleOrig = sel[0].Clone();
        handleBefore = Drawings.Capture(P);
        if (handle == "rot") { var c = S(handleShape.CX, handleShape.CY); rotStart = Math.Atan2(s.Y - c.Y, s.X - c.X) * 180 / Math.PI - handleShape.Rotation; }
        return true;
    }

    /// <summary>Where a handle of the selected shape is on screen (for the checks: they drag it like a finger).</summary>
    public Point HandleAt(string key) { var sel = SelectedShapes(); return sel.Count == 1 ? Handles(sel[0]).First(h => h.Key == key).Value : default; }

    void DragHandle(Point s)
    {
        var sh = handleShape; var o = handleOrig;
        if (sh == null) return;
        if (handle == "p0" || handle == "p1")
        {
            int i = handle == "p0" ? 0 : 1;
            var w = Snapped(s);
            if (Shift) w = Snap45(o.Points[1 - i], w);
            sh.Points[i] = w;
            sh.FitPoints();
            return;
        }
        if (handle == "rot")
        {
            var c = S(o.CX, o.CY);
            double a = Math.Atan2(s.Y - c.Y, s.X - c.X) * 180 / Math.PI - rotStart;
            if (Shift) a = Math.Round(a / 15) * 15;
            a = ((a % 360) + 360) % 360;
            if (a > 180) a -= 360;
            sh.Rotation = Math.Round(a, 1);
            return;
        }
        int hx = handle.Contains('e') ? 1 : handle.Contains('w') ? -1 : 0;
        int hy = handle.Contains('s') ? 1 : handle.StartsWith('n') ? -1 : 0;
        var m = Wv(s);
        double rad = (o.IsLine ? 0 : o.Rotation) * Math.PI / 180;
        double ux = Math.Cos(rad), uy = Math.Sin(rad), vx = -Math.Sin(rad), vy = Math.Cos(rad);
        double cx = o.CX, cy = o.CY;
        double qx = (m.X - cx) * ux + (m.Y - cy) * uy, qy = (m.X - cx) * vx + (m.Y - cy) * vy;
        double ax = -hx * o.W / 2, ay = -hy * o.H / 2;
        double min = 4 / K;
        double nw = hx != 0 ? Math.Max(min, (qx - ax) * hx) : o.W;
        double nh = hy != 0 ? Math.Max(min, (qy - ay) * hy) : o.H;
        if (Drawings.SnapOn && Math.Abs(o.Rotation) < 0.01 && !o.IsLine) { nw = Math.Max(min, Drawings.SnapV(View, nw)); nh = Math.Max(min, Drawings.SnapV(View, nh)); }
        if (Shift && hx != 0 && hy != 0 && o.W > 0 && o.H > 0) { double f = Math.Max(nw / o.W, nh / o.H); nw = o.W * f; nh = o.H * f; }
        double ncx = hx != 0 ? ax + hx * nw / 2 : 0, ncy = hy != 0 ? ay + hy * nh / 2 : 0;
        double wx = cx + ux * ncx + vx * ncy, wy = cy + uy * ncx + vy * ncy;
        if (o.IsLine)
        {
            double sx = o.W > 1e-9 ? nw / o.W : 1, sy = o.H > 1e-9 ? nh / o.H : 1;
            double nx0 = wx - nw / 2, ny0 = wy - nh / 2;
            for (int i = 0; i < o.Points.Count; i++) sh.Points[i] = new Point2(nx0 + (o.Points[i].X - o.X) * sx, ny0 + (o.Points[i].Y - o.Y) * sy);
            sh.FitPoints();
            return;
        }
        sh.W = nw; sh.H = nh; sh.X = wx - nw / 2; sh.Y = wy - nh / 2;
    }

    // ================================================================= commands (keys, menus and the properties panel)
    public void Apply(string name, Action<Shape> change)
    {
        var sel = SelectedShapes();
        if (sel.Count == 0) return;
        Drawings.Edit(P, Host.DrawUndo, name, () => { foreach (var s in sel) change(s); });
        Host.DrawEdited();
        Host.DrawRedraw();
    }
    public void ApplyCallouts(string name, Action<Callout> change)
    {
        var sel = SelectedCallouts();
        if (sel.Count == 0) return;
        Drawings.Edit(P, Host.DrawUndo, name, () => { foreach (var c in sel) change(c); });
        Host.DrawEdited();
        Host.DrawRedraw();
    }

    public void Order(string how) { Drawings.Order(P, Host.DrawUndo, SelectedShapes(), how); Host.DrawEdited(); Host.DrawRedraw(); }
    public void Align(string how) { Drawings.Align(P, Host.DrawUndo, SelectedShapes(), how); Host.DrawEdited(); Host.DrawRedraw(); }
    public void Distribute(bool across) { Drawings.Distribute(P, Host.DrawUndo, SelectedShapes(), across); Host.DrawEdited(); Host.DrawRedraw(); }
    public void GroupSelection()
    {
        var g = Drawings.GroupShapes(P, Host.DrawUndo, SelectedShapes());
        Host.DrawSay(g.Length > 0 ? "Grouped: they move and arrange together now." : "Select two or more drawings to group them.");
        Host.DrawEdited(); Host.DrawSelected(); Host.DrawRedraw();
    }
    public void UngroupSelection() { Drawings.Ungroup(P, Host.DrawUndo, SelectedShapes()); Host.DrawEdited(); Host.DrawSelected(); Host.DrawRedraw(); }

    double Step => View == "logical" ? 20 : View == "campus" ? 5 : 0.5;

    public void Duplicate()
    {
        var copies = Drawings.Duplicate(P, Host.DrawUndo, SelectedShapes(), Step, Step);
        if (copies.Count == 0) return;
        Host.DrawSelection.Clear(); foreach (var c in copies) Host.DrawSelection.Add(c);
        Host.DrawEdited(); Host.DrawSelected(); Host.DrawRedraw();
    }

    public void Copy()
    {
        var sel = Drawings.WithGroups(P, SelectedShapes());
        if (sel.Count == 0) return;
        LastClip = Drawings.ToClipText(sel);
        pasteCount = 0;
        Host.DrawSay("Copied " + sel.Count + " drawing" + (sel.Count == 1 ? "" : "s") + ". Paste them here, on another view or in another network.");
    }

    public void Cut() { Copy(); DeleteSelection(); }

    public List<Shape> Paste()
    {
        if (!CanDraw) return new List<Shape>();
        pasteCount++;
        var list = Drawings.FromClipText(P, LastClip, View, Step * pasteCount, Step * pasteCount);
        if (list == null || list.Count == 0) { Host.DrawSay("Nothing to paste: copy some drawings first."); return new List<Shape>(); }
        Drawings.Edit(P, Host.DrawUndo, "paste", () => P.Shapes.AddRange(list));
        Host.DrawSelection.Clear(); foreach (var c in list) Host.DrawSelection.Add(c);
        Host.DrawEdited(); Host.DrawSelected(); Host.DrawRedraw();
        return list;
    }

    public void DeleteSelection()
    {
        var items = SelectedShapes().Cast<object>().Concat(SelectedCallouts()).ToList();
        if (items.Count == 0) return;
        Edits.Delete(P, Host.DrawUndo, items);
        foreach (var o in items) Host.DrawSelection.Remove(o);
        Host.DrawEdited(); Host.DrawSelected(); Host.DrawRedraw();
    }

    public void Nudge(double dx, double dy)
    {
        var list = Movable();
        if (list.Count == 0) return;
        Drawings.Edit(P, Host.DrawUndo, "nudge", () => { foreach (var o in list) { var p = Edits.Pos(o); Edits.Set(o, new Point2(p.X + dx, p.Y + dy)); } });
        Host.DrawEdited(); Host.DrawRedraw();
    }

    /// <summary>Keys for drawings (the page calls this first). True = handled.</summary>
    public bool OnKey(Key k, KeyModifiers mods)
    {
        bool ctrl = (mods & KeyModifiers.Control) != 0, shift = (mods & KeyModifiers.Shift) != 0;
        if (DrawingLines && k == Key.Enter) { FinishPolyline(); return true; }
        if (k == Key.Escape && Busy) { Cancel(); return true; }
        bool hasShapes = SelectedShapes().Count > 0;
        if (ctrl && k == Key.V && LastClip.Length > 0 && CanDraw) { Paste(); return true; }
        if (!hasShapes) return false;
        if (ctrl && k == Key.C) { Copy(); return true; }
        if (ctrl && k == Key.X) { Cut(); return true; }
        if (ctrl && k == Key.D) { Duplicate(); return true; }
        if (ctrl && k == Key.G) { if (shift) UngroupSelection(); else GroupSelection(); return true; }
        if (k == Key.OemCloseBrackets) { Order(ctrl || shift ? "front" : "forward"); return true; }
        if (k == Key.OemOpenBrackets) { Order(ctrl || shift ? "back" : "backward"); return true; }
        if (k == Key.Delete) { DeleteSelection(); return true; }
        if (k == Key.Left || k == Key.Right || k == Key.Up || k == Key.Down)
        {
            double step = Drawings.GridStep(View) * (shift ? 10 : 1);
            Nudge(k == Key.Left ? -step : k == Key.Right ? step : 0, k == Key.Up ? -step : k == Key.Down ? step : 0);
            return true;
        }
        return false;
    }

    // ================================================================= text and labels
    public void EditText(Shape s)
    {
        if (s == null || s.Kind == "image") return;
        NetAsk.Text(Host.DrawControl, Drawings.KindName(s), s.Kind == "area" ? "The area's title:" : "The text:", s.Text, s.Kind != "area", t => SetText(s, t));
    }

    /// <summary>Changes a drawing's text (a text box grows to fit).</summary>
    public void SetText(Shape s, string t)
    {
        if (t == s.Text) return;
        Drawings.Edit(P, Host.DrawUndo, "edit text", () => { s.Text = t; if (s.Kind == "text") FitText(s); });
        Host.DrawEdited(); Host.DrawSelected(); Host.DrawRedraw();
    }

    public void EditCallout(Callout c)
    {
        NetAsk.Text(Host.DrawControl, "Label", "The label:", c.Text, false, t =>
        {
            if (t.Trim().Length == 0) { Edits.Delete(P, Host.DrawUndo, new object[] { c }); Host.DrawSelection.Remove(c); }
            else Drawings.Edit(P, Host.DrawUndo, "edit label", () => c.Text = t.Trim());
            Host.DrawEdited(); Host.DrawSelected(); Host.DrawRedraw();
        });
    }

    /// <summary>Asks for a label for a device or a cable and sticks it on.</summary>
    public void AskLabel(object target)
    {
        string id = target is Device dv ? dv.Id : target is Link lk ? lk.Id : null;
        if (id == null || Host.CalloutView.Length == 0) return;
        string what = target is Device d ? d.Name : "this cable";
        NetAsk.Text(Host.DrawControl, "Label", "A label for " + what + " (for example Reception PC, Uplink to the internet):", target is Device d2 ? d2.Name : "Uplink", false, t =>
        {
            if (t.Trim().Length == 0) return;
            var c = Drawings.AddCallout(P, Host.DrawUndo, id, Host.CalloutView, t.Trim());
            Host.DrawSelection.Clear(); Host.DrawSelection.Add(c);
            Host.DrawSay("Label added. It moves with " + (target is Device d3 ? d3.Name : "the cable") + "; drag it to place it.");
            Host.DrawEdited(); Host.DrawSelected(); Host.DrawRedraw();
        });
    }

    /// <summary>Menu entries for a drawing or a label (hold or right-click), or "Add a label" for a device or cable.</summary>
    public List<(string Label, Action Act)> MenuItems(object hit)
    {
        var list = new List<(string, Action)>();
        if (hit is Shape s)
        {
            if (s.Kind != "image") list.Add((s.Kind == "area" ? "Edit the title..." : "Edit the text...", () => EditText(s)));
            list.Add(("Bring to front", () => Order("front")));
            list.Add(("Send to back", () => Order("back")));
            list.Add((s.Above ? "Put under the devices" : "Put above the devices", () => { bool v = !s.Above; Apply(v ? "drawing above devices" : "drawing under devices", x => x.Above = v); }));
            list.Add((s.Locked ? "Unlock its position" : "Lock its position", () => { bool v = !s.Locked; Apply(v ? "lock" : "unlock", x => x.Locked = v); }));
            list.Add(("Duplicate", Duplicate));
            list.Add(("Copy", Copy));
            if (SelectedShapes().Count > 1) list.Add(("Group", GroupSelection));
            if (s.Group.Length > 0) list.Add(("Ungroup", UngroupSelection));
            list.Add(("Delete", DeleteSelection));
        }
        if (hit is Callout c)
        {
            list.Add(("Edit the label...", () => EditCallout(c)));
            list.Add(("Delete the label", () => { Edits.Delete(P, Host.DrawUndo, new object[] { c }); Host.DrawSelection.Remove(c); Host.DrawEdited(); Host.DrawRedraw(); }));
        }
        if ((hit is Device || hit is Link) && Host.CalloutView.Length > 0) list.Add(("Add a label...", () => AskLabel(hit)));
        return list;
    }

    // ================================================================= maps
    /// <summary>The area a map covers: the campus, the floor, or the devices on the Logical view (with room around).</summary>
    public global::NetworkLab.Rect MapExtent()
    {
        if (View == "campus") return new global::NetworkLab.Rect(0, 0, P.Layout.CampusW, P.Layout.CampusH);
        if (View != "logical") { var f = P.Layout.Floor(View); return f == null ? new global::NetworkLab.Rect(0, 0, 30, 20) : new global::NetworkLab.Rect(0, 0, f.W, f.H); }
        var r = new global::NetworkLab.Rect(0, 0, 900, 600);
        foreach (var d in P.Devices) r.Union(new global::NetworkLab.Rect(d.X, d.Y, 0, 0));
        foreach (var s in P.Shapes.Where(x => x.View == "logical" && !x.FromStyle)) r.Union(s.Bounds);
        r = new global::NetworkLab.Rect(r.X - 160, r.Y - 140, r.Width + 320, r.Height + 280);
        return new global::NetworkLab.Rect(Math.Floor(r.X / 40) * 40, Math.Floor(r.Y / 40) * 40, Math.Ceiling(r.Width / 40) * 40, Math.Ceiling(r.Height / 40) * 40);
    }

    public void ApplyMapStyle(string style)
    {
        if (!CanDraw) return;
        if (View != "logical" && View != "campus") { Host.DrawSay("Drawn maps go on the Logical view and the Campus map. On a floor plan, use a picture of the plan."); return; }
        Drawings.ApplyMapStyle(P, Host.DrawUndo, View, style, MapExtent());
        Host.DrawSay(style == "none" ? "The map is gone." : "Map added. It is on the Map layer, which is locked so taps reach your devices: unlock it under Layers to change the map's pieces, or add roads, parks and buildings from the Objects library on top.");
        Host.DrawEdited(); Host.DrawRedraw();
    }

    public void SetMapPicture(byte[] bytes)
    {
        if (!CanDraw) return;
        DrawArt.PixelSize(bytes, out double pw, out double ph);
        if (View == "logical" || View == "campus")
        {
            Drawings.SetMapPicture(P, Host.DrawUndo, View, bytes, MapExtent(), pw, ph);
            Host.DrawSay("The picture is the map now. To make distances right, use Set the scale and draw a line along something whose length you know.");
        }
        else
        {
            var f = P.Layout.Floor(View);
            if (f == null) return;
            if (bytes.Length > 4 * 1024 * 1024) throw new InvalidOperationException("That picture is bigger than 4 MB: please use a smaller one.");
            string old = f.Background; double ow = f.BgW, oh = f.BgH;
            string b64 = Convert.ToBase64String(bytes);
            double nw = f.W, nh = pw > 0 ? f.W * ph / pw : f.H;
            Host.DrawUndo.Do("plan picture", () => { f.Background = b64; f.BgW = nw; f.BgH = nh; P.Touch(); }, () => { f.Background = old; f.BgW = ow; f.BgH = oh; P.Touch(); });
            Host.DrawSay("The picture is under this floor now. Set the scale by drawing a line along a wall whose length you know.");
        }
        Host.DrawEdited(); Host.DrawRedraw();
    }

    public void RemoveMapPicture()
    {
        if (!CanDraw) return;
        if (View == "logical" || View == "campus") Drawings.RemoveMapPicture(P, Host.DrawUndo, View);
        else
        {
            var f = P.Layout.Floor(View);
            if (f == null || f.Background.Length == 0) return;
            string old = f.Background;
            Host.DrawUndo.Do("remove plan picture", () => { f.Background = ""; P.Touch(); }, () => { f.Background = old; P.Touch(); });
        }
        Host.DrawEdited(); Host.DrawRedraw();
    }

    public void Calibrate(double units, double metres)
    {
        Drawings.Calibrate(P, Host.DrawUndo, View, units, metres);
        Host.DrawSay(View == "campus" ? "Scale set: the map, the buildings and the cable lengths between buildings follow it now." : View == "logical" ? "Scale set: the scale bar shows it." : "Scale set: the picture under the floor is stretched to match.");
        Host.DrawEdited(); Host.DrawRedraw();
    }

    /// <summary>Shows or hides / locks or unlocks a layer of this view (the Layers panel).</summary>
    public void SetLayer(string id, bool visibility, bool on)
    {
        if (!CanDraw) return;
        string key = Drawings.LayerKeyOf(View);
        Drawings.Edit(P, Host.DrawUndo, (visibility ? (on ? "show " : "hide ") : (on ? "lock " : "unlock ")) + id, () =>
        {
            var l = Drawings.LayersOf(P, key);
            if (visibility) { if (on) l.Hidden.Remove(id); else l.Hidden.Add(id); }
            else { if (on) l.Locked.Add(id); else l.Locked.Remove(id); }
        });
        if (visibility && !on || !visibility && on)
            foreach (var o in Host.DrawSelection.ToList())
                if ((o is Shape sh && LayerOf(sh) == id) || (o is Device && id == "devices") || (o is Link && id == "cables") || (o is Callout && id == "labels")) Host.DrawSelection.Remove(o);
        Host.DrawEdited();
        Host.DrawSelected();
        Host.DrawRedraw();
    }
}
