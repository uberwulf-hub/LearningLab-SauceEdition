// Network Lab - drawing the drawings (App Hub's DrawArt.cs for Avalonia): shapes, lines with arrowheads, text, sticky
// notes, areas, pictures, the clip-art objects of the library (simple flat pictures drawn here, all original) and the
// map under a view. Shared by the Logical view (NetCanvas) and the Physical view (PhysicalView).
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using NetworkLab;
using Rect = Avalonia.Rect;

namespace LearningLab.NetworkLab;

public static class DrawArt
{
    public static IBrush Br(string c)
    {
        if (string.IsNullOrEmpty(c) || c == "none") return null;
        try { return NetIcons.B(c); } catch (Exception) { return null; }
    }
    public static IBrush Alpha(string c, byte a)
    {
        if (!DrawColor.TryParse(c, out _, out byte r, out byte g, out byte b)) { r = 128; g = 128; b = 128; }
        return new SolidColorBrush(Color.FromArgb(a, r, g, b)).ToImmutable();
    }
    static readonly Typeface UiFace = new(FontFamily.Default), UiBold = new(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold);
    public static FormattedText Fmt(string t, double size, IBrush b, bool bold) => new(t ?? "", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, bold ? UiBold : UiFace, Math.Max(1, size), b);

    public static IPen PenOf(Shape s, double fk)
    {
        var b = Br(s.Stroke);
        if (b == null || s.StrokeWidth <= 0) return null;
        IDashStyle dash = s.Dash == "dash" ? new DashStyle(new double[] { 4, 3 }, 0) : s.Dash == "dot" ? new DashStyle(new double[] { 0.1, 2.2 }, 0) : null;
        return new Pen(b, Math.Max(0.5, s.StrokeWidth * fk), dash, PenLineCap.Round, PenLineJoin.Round);
    }
    public static double PenWidth(Shape s, double fk) => Br(s.Stroke) == null || s.StrokeWidth <= 0 ? 0 : Math.Max(0.5, s.StrokeWidth * fk);

    static readonly Dictionary<string, FontFamily> fonts = new();
    static FontFamily FontOf(string name)
    {
        name = string.IsNullOrEmpty(name) ? "Segoe UI" : name;
        if (!fonts.TryGetValue(name, out var f))
        {
            try { f = new FontFamily(name + ", " + FontFamily.DefaultFontFamilyName); } catch (Exception) { f = FontFamily.Default; }
            fonts[name] = f;
        }
        return f;
    }

    public static FormattedText TextOf(Shape s, double fk, double maxW)
    {
        var tf = new Typeface(FontOf(s.Font), s.Italic ? FontStyle.Italic : FontStyle.Normal, s.Bold ? FontWeight.Bold : FontWeight.Normal);
        var ft = new FormattedText(s.Text ?? "", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, Math.Max(1, s.FontSize * fk), Br(s.TextColor) ?? Brushes.White);
        if (maxW > 4) ft.MaxTextWidth = maxW;
        ft.TextAlignment = s.Align == "left" ? TextAlignment.Left : s.Align == "right" ? TextAlignment.Right : TextAlignment.Center;
        return ft;
    }

    // ---------------------------------------------------------------- pictures (cached by the string itself)
    static readonly List<KeyValuePair<string, Bitmap>> pics = new();
    public static Bitmap Picture(string b64)
    {
        if (string.IsNullOrEmpty(b64)) return null;
        foreach (var kv in pics) if (ReferenceEquals(kv.Key, b64)) return kv.Value;
        foreach (var kv in pics) if (kv.Key.Length == b64.Length && kv.Key == b64) return kv.Value;
        Bitmap img = null;
        try { img = new Bitmap(new MemoryStream(Convert.FromBase64String(b64))); } catch (Exception) { img = null; }
        pics.Add(new KeyValuePair<string, Bitmap>(b64, img));
        if (pics.Count > 24) pics.RemoveAt(0);
        return img;
    }

    /// <summary>The size of a picture in pixels; throws a plain sentence when it isn't a picture we can read.</summary>
    public static void PixelSize(byte[] bytes, out double w, out double h)
    {
        try
        {
            using var bmp = new Bitmap(new MemoryStream(bytes));
            w = bmp.PixelSize.Width; h = bmp.PixelSize.Height;
        }
        catch (Exception) { throw new InvalidOperationException("That file isn't a picture Network Lab can read (use PNG or JPEG)."); }
    }

    static Matrix RotateAt(double degrees, double cx, double cy) => Matrix.CreateTranslation(-cx, -cy) * Matrix.CreateRotation(degrees * Math.PI / 180) * Matrix.CreateTranslation(cx, cy);
    public static Point Turn(Point p, double degrees, Point c) => RotateAt(degrees, c.X, c.Y).Transform(p);

    // ---------------------------------------------------------------- shapes
    /// <summary>Draws a shape. S turns view units into screen points, k is screen pixels per unit and fk the size of a
    /// "pixel at 100%" (line widths and fonts).</summary>
    public static void DrawShape(DrawingContext dc, Shape s, Func<double, double, Point> S, double k, double fk)
    {
        using var op = dc.PushOpacity(Math.Max(0.05, Math.Min(1, s.Opacity)));
        if (s.IsLine) { DrawLines(dc, s, S, fk); return; }
        var tl = S(s.X, s.Y);
        var r = new Rect(tl.X, tl.Y, Math.Max(1, s.W * k), Math.Max(1, s.H * k));
        bool rot = Math.Abs(s.Rotation) > 0.01;
        using var tr = rot ? dc.PushTransform(RotateAt(s.Rotation, r.X + r.Width / 2, r.Y + r.Height / 2)) : default(DrawingContext.PushedState?);
        var fill = Br(s.Fill);
        var pen = PenOf(s, fk);
        double rad = Math.Min(Math.Min(r.Width, r.Height) / 2, Math.Max(0, s.Corner * fk));
        switch (s.Kind)
        {
            case "ellipse":
                dc.DrawEllipse(fill, pen, r.Center, r.Width / 2, r.Height / 2);
                InnerText(dc, s, r, fk, true);
                break;
            case "text":
                if (fill != null || pen != null) dc.DrawRectangle(fill, pen, r, rad, rad);
                InnerText(dc, s, r, fk, false);
                break;
            case "note":
                {
                    double f = Math.Min(r.Width, r.Height) * 0.16;
                    dc.DrawRectangle(Alpha("#000000", 70), null, new Rect(r.X + 3 * fk, r.Y + 4 * fk, r.Width, r.Height));
                    var g = Geo(true, new Point(r.X, r.Y), new Point(r.Right, r.Y), new Point(r.Right, r.Bottom - f), new Point(r.Right - f, r.Bottom), new Point(r.X, r.Bottom));
                    dc.DrawGeometry(fill ?? NetIcons.B("#FDE68A"), pen, g);
                    dc.DrawGeometry(Alpha("#000000", 60), pen, Geo(true, new Point(r.Right, r.Bottom - f), new Point(r.Right - f, r.Bottom - f), new Point(r.Right - f, r.Bottom)));
                    dc.DrawRectangle(Alpha("#FFFFFF", 50), null, new Rect(r.X + r.Width * 0.3, r.Y - 4 * fk, r.Width * 0.4, 9 * fk));
                    InnerText(dc, s, new Rect(r.X, r.Y, r.Width, Math.Max(1, r.Height - f * 0.5)), fk, false);
                    break;
                }
            case "area":
                {
                    string c = DrawColor.IsColor(s.Fill) && s.Fill != "none" ? s.Fill : "#22C55E";
                    dc.DrawRectangle(Alpha(c, 40), pen, r, rad, rad);
                    if ((s.Text ?? "").Length > 0)
                    {
                        var t = TextOf(s, fk, Math.Max(10, r.Width - 16 * fk));
                        double pad = 8 * fk;
                        var band = new Rect(r.X, r.Y, Math.Min(r.Width, t.WidthIncludingTrailingWhitespace + pad * 2), Math.Min(r.Height, t.Height + pad));
                        if (s.Align != "left") band = band.WithWidth(r.Width);
                        using (dc.PushClip(new RoundedRect(r, rad)))
                            dc.DrawRectangle(Alpha(c, 80), null, band);
                        dc.DrawText(t, new Point(r.X + pad, r.Y + pad / 2));
                    }
                    break;
                }
            case "image":
                {
                    var img = Picture(s.Picture);
                    if (img != null) dc.DrawImage(img, r);
                    else
                    {
                        dc.DrawRectangle(NetIcons.B("#334155"), null, r);
                        var t = Fmt("Picture", 11 * fk, NetIcons.B("#94A3B8"), false);
                        dc.DrawText(t, new Point(r.X + (r.Width - t.Width) / 2, r.Y + (r.Height - t.Height) / 2));
                    }
                    if (pen != null) dc.DrawRectangle(null, pen, r);
                    break;
                }
            case "object":
                {
                    Object(dc, s.Art, r);
                    if (pen != null) dc.DrawRectangle(null, pen, r, rad, rad);
                    if ((s.Text ?? "").Length > 0)
                    {
                        double mw = Math.Max(r.Width * 1.6, 80 * fk);
                        var t = TextOf(s, fk, mw);
                        double tw = t.Width;
                        dc.DrawRectangle(Alpha("#0D131D", 170), null, new Rect(r.X + r.Width / 2 - tw / 2 - 3, r.Bottom + 1, tw + 6, t.Height + 2), 3, 3);
                        dc.DrawText(t, new Point(r.X + r.Width / 2 - mw / 2, r.Bottom + 2));
                    }
                    break;
                }
            default:   // rect, roundrect
                dc.DrawRectangle(fill, pen, r, rad, rad);
                InnerText(dc, s, r, fk, true);
                break;
        }
    }

    static void InnerText(DrawingContext dc, Shape s, Rect r, double fk, bool middle)
    {
        if ((s.Text ?? "").Length == 0) return;
        double pad = (s.Kind == "note" ? 9 : 5) * fk;
        var t = TextOf(s, fk, Math.Max(10, r.Width - pad * 2));
        double y = middle ? r.Y + (r.Height - t.Height) / 2 : r.Y + pad * (s.Kind == "text" ? 0.6 : 1);
        using (dc.PushClip(new Rect(r.X - 2, r.Y - 2, r.Width + 4, Math.Max(r.Height, t.Height) + 4)))
            dc.DrawText(t, new Point(r.X + pad, y));
    }

    public static StreamGeometry Geo(bool closed, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], closed);
            for (int i = 1; i < pts.Length; i++) c.LineTo(pts[i]);
            c.EndFigure(closed);
        }
        return g;
    }

    static void DrawLines(DrawingContext dc, Shape s, Func<double, double, Point> S, double fk)
    {
        if (s.Points.Count < 2) return;
        var pts = s.Points.Select(p => S(p.X, p.Y)).ToArray();
        var pen = PenOf(s, fk);
        var fill = s.Closed ? Br(s.Fill) : null;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], fill != null);
            if (s.Kind == "freehand" && pts.Length > 2)
            {
                // smooth the hand-drawn line through the mid points
                for (int i = 1; i < pts.Length - 1; i++) c.QuadraticBezierTo(pts[i], new Point((pts[i].X + pts[i + 1].X) / 2, (pts[i].Y + pts[i + 1].Y) / 2));
                c.LineTo(pts[^1]);
            }
            else for (int i = 1; i < pts.Length; i++) c.LineTo(pts[i]);
            c.EndFigure(s.Closed);
        }
        dc.DrawGeometry(fill, pen, g);
        if (pen != null)
        {
            double th = PenWidth(s, fk);
            double head = Math.Max(9, th * 3.2);
            var solid = new Pen(pen.Brush, Math.Max(1, th * 0.6), null, PenLineCap.Round, PenLineJoin.Round);
            if (s.ArrowEnd) Head(dc, pen.Brush, solid, pts[^2], pts[^1], head);
            if (s.ArrowStart) Head(dc, pen.Brush, solid, pts[1], pts[0], head);
        }
        if ((s.Text ?? "").Length > 0)
        {
            int mid = (pts.Length - 1) / 2;
            var m = new Point((pts[mid].X + pts[mid + 1].X) / 2, (pts[mid].Y + pts[mid + 1].Y) / 2);
            var t = TextOf(s, fk, 0);
            dc.DrawRectangle(Alpha("#0D131D", 190), null, new Rect(m.X - t.Width / 2 - 4, m.Y - t.Height - 6, t.Width + 8, t.Height + 2), 3, 3);
            dc.DrawText(t, new Point(m.X - t.Width / 2, m.Y - t.Height - 5));
        }
    }

    static void Head(DrawingContext dc, IBrush b, IPen pen, Point from, Point to, double size)
    {
        double vx = to.X - from.X, vy = to.Y - from.Y, len = Math.Sqrt(vx * vx + vy * vy);
        if (len < 0.01) return;
        vx /= len; vy /= len;
        double nx = -vy, ny = vx;
        Point P(double a, double n) => new(to.X + vx * a * size + nx * n * size, to.Y + vy * a * size + ny * n * size);
        dc.DrawGeometry(b, pen, Geo(true, P(0.15, 0), P(-1, 0.55), P(-0.7, 0), P(-1, -0.55)));
    }

    // ---------------------------------------------------------------- the map under a view
    /// <summary>The map's colour, grid and picture. area = the part of the screen to cover (Logical: all of it;
    /// Campus: the campus).</summary>
    public static void MapUnder(DrawingContext dc, MapInfo m, Rect area, Func<double, double, Point> S, Func<Point, Point> W, double k, string view)
    {
        if (m == null) return;
        var bg = Br(m.Color);
        if (bg != null) dc.DrawRectangle(bg, null, area);
        if (m.Style == "grid")
        {
            double minor = view == "logical" ? 40 : view == "campus" ? 10 : 1, major = minor * 5;
            if (minor * k >= 6)
            {
                var p0 = W(area.TopLeft); var p1 = W(area.BottomRight);
                var thin = new Pen(Alpha("#94A3B8", 40), 1);
                var thick = new Pen(Alpha("#94A3B8", 80), 1);
                for (double x = Math.Floor(p0.X / minor) * minor; x <= p1.X; x += minor)
                {
                    bool big = Math.Abs(Math.IEEERemainder(x, major)) < minor / 10;
                    var a = S(x, p0.Y); dc.DrawLine(big ? thick : thin, new Point(a.X, area.Top), new Point(a.X, area.Bottom));
                }
                for (double y = Math.Floor(p0.Y / minor) * minor; y <= p1.Y; y += minor)
                {
                    bool big = Math.Abs(Math.IEEERemainder(y, major)) < minor / 10;
                    var a = S(p0.X, y); dc.DrawLine(big ? thick : thin, new Point(area.Left, a.Y), new Point(area.Right, a.Y));
                }
            }
        }
        var img = Picture(m.Picture);
        if (img != null && m.PW > 0 && m.PH > 0)
        {
            using (dc.PushOpacity(Math.Max(0.1, Math.Min(1, m.Opacity))))
                dc.DrawImage(img, new Rect(S(m.PX, m.PY), S(m.PX + m.PW, m.PY + m.PH)));
        }
    }

    // ---------------------------------------------------------------- clip-art
    static IPen Ln(string c, double w) => new Pen(NetIcons.B(c), Math.Max(0.4, w), null, PenLineCap.Round, PenLineJoin.Round);

    /// <summary>Draws a library object into a rectangle.</summary>
    public static void Object(DrawingContext dc, string art, Rect r)
    {
        Point P(double x, double y) => new(r.X + x * r.Width, r.Y + y * r.Height);
        Rect R(double x, double y, double w, double h) => new(P(x, y), P(x + w, y + h));
        double u = Math.Min(r.Width, r.Height);
        var edge = Ln("#0F172A", u * 0.025);
        void box(string c, IPen pen, Rect rr, double rad) => dc.DrawRectangle(NetIcons.B(c), pen, rr, rad * u, rad * u);
        void poly(string c, IPen pen, Point[] pts) => dc.DrawGeometry(c == null ? null : NetIcons.B(c), pen, Geo(true, pts));
        void oval(string c, IPen pen, Point ctr, double rx, double ry) => dc.DrawEllipse(c == null ? null : NetIcons.B(c), pen, ctr, rx * r.Width, ry * r.Height);
        void line(string c, double w, Point a, Point b) => dc.DrawLine(Ln(c, w * u), a, b);
        StreamGeometry Curve(Action<StreamGeometryContext> draw) { var g = new StreamGeometry(); using (var c = g.Open()) draw(c); return g; }
        switch (art)
        {
            // ---------------------------------------------------- buildings (from the front)
            case "house":
                box("#9A3412", edge, R(0.66, 0.12, 0.09, 0.2), 0);
                box("#F5E6C8", edge, R(0.13, 0.45, 0.74, 0.52), 0);
                poly("#C2410C", edge, new[] { P(0.03, 0.5), P(0.5, 0.05), P(0.97, 0.5) });
                box("#7C2D12", edge, R(0.43, 0.66, 0.15, 0.31), 0.02);
                box("#7DD3FC", edge, R(0.2, 0.58, 0.16, 0.15), 0);
                box("#7DD3FC", edge, R(0.65, 0.58, 0.16, 0.15), 0);
                oval("#FDE68A", null, P(0.55, 0.82), 0.012, 0.012);
                break;
            case "office":
                box("#64748B", edge, R(0.14, 0.05, 0.72, 0.93), 0.02);
                box("#475569", null, R(0.14, 0.05, 0.72, 0.05), 0);
                for (int i = 0; i < 4; i++) for (int j = 0; j < 7; j++) box(j % 3 == 1 && i == 2 ? "#FDE68A" : "#BAE6FD", null, R(0.2 + i * 0.165, 0.14 + j * 0.105, 0.11, 0.07), 0);
                box("#1E293B", edge, R(0.42, 0.86, 0.16, 0.12), 0);
                break;
            case "school":
                box("#B45309", edge, R(0.04, 0.4, 0.92, 0.57), 0);
                poly("#7C2D12", edge, new[] { P(0.3, 0.42), P(0.5, 0.16), P(0.7, 0.42) });
                box("#B45309", edge, R(0.44, 0.06, 0.12, 0.16), 0);
                oval("#F8FAFC", edge, P(0.5, 0.32), 0.045, 0.06);
                line("#0F172A", 0.02, P(0.5, 0.32), P(0.5, 0.28)); line("#0F172A", 0.02, P(0.5, 0.32), P(0.53, 0.32));
                for (int i = 0; i < 6; i++) if (i != 2 && i != 3) box("#BFDBFE", edge, R(0.08 + i * 0.145, 0.52, 0.1, 0.14), 0);
                box("#451A03", edge, R(0.44, 0.72, 0.12, 0.25), 0);
                line("#CBD5E1", 0.02, P(0.9, 0.4), P(0.9, 0.06)); poly("#EF4444", null, new[] { P(0.9, 0.06), P(0.99, 0.1), P(0.9, 0.14) });
                break;
            case "warehouse":
                box("#94A3B8", edge, R(0.04, 0.36, 0.92, 0.61), 0);
                poly("#475569", edge, new[] { P(0.01, 0.4), P(0.5, 0.1), P(0.99, 0.4) });
                foreach (var dx in new[] { 0.12, 0.56 })
                {
                    box("#CBD5E1", edge, R(dx, 0.55, 0.32, 0.42), 0);
                    for (int i = 1; i < 7; i++) line("#94A3B8", 0.012, P(dx, 0.55 + i * 0.06), P(dx + 0.32, 0.55 + i * 0.06));
                }
                break;
            case "datacentre":
                box("#334155", edge, R(0.05, 0.22, 0.9, 0.75), 0.02);
                box("#64748B", edge, R(0.12, 0.1, 0.2, 0.12), 0); box("#64748B", edge, R(0.4, 0.1, 0.2, 0.12), 0);
                for (int j = 0; j < 4; j++)
                {
                    box("#0F172A", null, R(0.1, 0.3 + j * 0.16, 0.8, 0.11), 0.01);
                    for (int i = 0; i < 9; i++) oval(i % 3 == 0 ? "#38BDF8" : "#4ADE80", null, P(0.16 + i * 0.085, 0.355 + j * 0.16), 0.012, 0.016);
                }
                break;
            case "tower":
                box("#0369A1", edge, R(0.24, 0.12, 0.52, 0.86), 0.02);
                for (int j = 0; j < 12; j++) box(j % 4 == 2 ? "#FDE68A" : "#7DD3FC", null, R(0.3, 0.16 + j * 0.068, 0.4, 0.035), 0);
                line("#CBD5E1", 0.03, P(0.5, 0.12), P(0.5, 0.0));
                oval("#EF4444", null, P(0.5, 0.01), 0.04, 0.012);
                break;
            // ---------------------------------------------------- map pieces (from above)
            case "road":
                box("#4B5563", null, R(0, 0, 1, 1), 0);
                line("#9CA3AF", 0.05, P(0, 0.06), P(1, 0.06)); line("#9CA3AF", 0.05, P(0, 0.94), P(1, 0.94));
                for (int i = 0; i < 8; i++) line("#F8FAFC", 0.06, P(0.02 + i * 0.125, 0.5), P(0.09 + i * 0.125, 0.5));
                break;
            case "roadcurve":
                {
                    var g = Curve(c => { c.BeginFigure(P(0, 0.35), false); c.ArcTo(P(0.65, 1), new Size(0.65 * r.Width, 0.65 * r.Height), 0, false, SweepDirection.Clockwise); c.EndFigure(false); });
                    dc.DrawGeometry(null, new Pen(NetIcons.B("#4B5563"), 0.5 * u), g);
                    dc.DrawGeometry(null, new Pen(NetIcons.B("#F8FAFC"), 0.025 * u, new DashStyle(new double[] { 2, 2 }, 0)), g);
                    break;
                }
            case "junction":
                box("#4B5563", null, R(0, 0.3, 1, 0.4), 0); box("#4B5563", null, R(0.3, 0, 0.4, 1), 0);
                for (int i = 0; i < 3; i++) { line("#F8FAFC", 0.03, P(0.03 + i * 0.09, 0.5), P(0.08 + i * 0.09, 0.5)); line("#F8FAFC", 0.03, P(0.75 + i * 0.09, 0.5), P(0.8 + i * 0.09, 0.5)); line("#F8FAFC", 0.03, P(0.5, 0.03 + i * 0.09), P(0.5, 0.08 + i * 0.09)); line("#F8FAFC", 0.03, P(0.5, 0.75 + i * 0.09), P(0.5, 0.8 + i * 0.09)); }
                for (int i = 0; i < 4; i++) line("#E5E7EB", 0.025, P(0.32 + i * 0.1, 0.27), P(0.32 + i * 0.1, 0.2));
                break;
            case "roundabout":
                box("#4B5563", null, R(0.4, 0, 0.2, 1), 0); box("#4B5563", null, R(0, 0.4, 1, 0.2), 0);
                oval(null, new Pen(NetIcons.B("#4B5563"), 0.2 * u), P(0.5, 0.5), 0.3, 0.3);
                oval("#166534", Ln("#E5E7EB", 0.02 * u), P(0.5, 0.5), 0.19, 0.19);
                oval("#22C55E", null, P(0.5, 0.5), 0.08, 0.08);
                break;
            case "river":
                {
                    var g = Curve(c => { c.BeginFigure(P(0, 0.5), false); c.CubicBezierTo(P(0.25, 0.05), P(0.5, 0.95), P(0.75, 0.5)); c.CubicBezierTo(P(0.85, 0.3), P(0.93, 0.35), P(1, 0.5)); c.EndFigure(false); });
                    dc.DrawGeometry(null, new Pen(NetIcons.B("#1D4ED8"), 0.62 * r.Height), g);
                    dc.DrawGeometry(null, new Pen(NetIcons.B("#3B82F6"), 0.4 * r.Height), g);
                    dc.DrawGeometry(null, new Pen(NetIcons.B("#93C5FD"), 0.04 * r.Height, new DashStyle(new double[] { 3, 5 }, 0)), g);
                    break;
                }
            case "park":
                box("#166534", Ln("#14532D", u * 0.03), R(0, 0, 1, 1), 0.06);
                dc.DrawGeometry(null, new Pen(NetIcons.B("#C8B88A"), 0.07 * u), Curve(c => { c.BeginFigure(P(0, 0.8), false); c.CubicBezierTo(P(0.3, 0.5), P(0.6, 0.95), P(1, 0.3)); c.EndFigure(false); }));
                oval("#2563EB", Ln("#93C5FD", u * 0.02), P(0.72, 0.72), 0.12, 0.1);
                foreach (var t in new[] { new[] { 0.18, 0.25 }, new[] { 0.4, 0.2 }, new[] { 0.62, 0.3 }, new[] { 0.25, 0.55 }, new[] { 0.85, 0.2 }, new[] { 0.45, 0.6 } })
                { oval("#14532D", null, P(t[0], t[1]), 0.08, 0.11); oval("#22C55E", null, P(t[0] - 0.015, t[1] - 0.02), 0.045, 0.06); }
                break;
            case "carpark":
                box("#6B7280", Ln("#374151", u * 0.03), R(0, 0, 1, 1), 0.03);
                for (int i = 0; i < 7; i++) { line("#F8FAFC", 0.02, P(0.1 + i * 0.12, 0.05), P(0.1 + i * 0.12, 0.35)); line("#F8FAFC", 0.02, P(0.1 + i * 0.12, 0.65), P(0.1 + i * 0.12, 0.95)); }
                box("#2563EB", Ln("#F8FAFC", u * 0.03), R(0.4, 0.38, 0.2, 0.24), 0.03);
                {
                    var t = Fmt("P", Math.Max(4, u * 0.2), Brushes.White, true);
                    var c = P(0.5, 0.5); dc.DrawText(t, new Point(c.X - t.Width / 2, c.Y - t.Height / 2));
                }
                oval("#DC2626", null, P(0.22, 0.2), 0.05, 0.11); oval("#FACC15", null, P(0.7, 0.8), 0.05, 0.11);
                break;
            case "bridge":
                box("#2563EB", null, R(0.3, 0, 0.4, 1), 0);
                line("#93C5FD", 0.02, P(0.4, 0.05), P(0.4, 0.2)); line("#93C5FD", 0.02, P(0.6, 0.8), P(0.6, 0.95));
                box("#9CA3AF", edge, R(0, 0.3, 1, 0.4), 0);
                line("#E5E7EB", 0.04, P(0.05, 0.32), P(0.95, 0.32)); line("#E5E7EB", 0.04, P(0.05, 0.68), P(0.95, 0.68));
                for (int i = 0; i < 6; i++) line("#F8FAFC", 0.03, P(0.08 + i * 0.16, 0.5), P(0.15 + i * 0.16, 0.5));
                break;
            // ---------------------------------------------------- furniture (from above)
            case "desk":
                box("#A16207", edge, R(0, 0, 1, 1), 0.04);
                box("#1F2937", edge, R(0.35, 0.08, 0.3, 0.14), 0.02);
                box("#E5E7EB", edge, R(0.33, 0.5, 0.34, 0.16), 0.02);
                oval("#E5E7EB", edge, P(0.76, 0.58), 0.03, 0.06);
                break;
            case "chair":
                box("#475569", edge, R(0.12, 0.2, 0.76, 0.72), 0.12);
                box("#334155", edge, R(0.05, 0.02, 0.9, 0.24), 0.08);
                break;
            case "table":
                for (int i = 0; i < 3; i++) { box("#475569", edge, R(0.2 + i * 0.25, 0, 0.12, 0.14), 0.03); box("#475569", edge, R(0.2 + i * 0.25, 0.86, 0.12, 0.14), 0.03); }
                box("#475569", edge, R(0, 0.42, 0.08, 0.16), 0.03); box("#475569", edge, R(0.92, 0.42, 0.08, 0.16), 0.03);
                box("#92400E", edge, R(0.07, 0.14, 0.86, 0.72), 0.25);
                break;
            case "sofa":
                box("#5B21B6", edge, R(0, 0, 1, 0.38), 0.08);
                box("#5B21B6", edge, R(0, 0.1, 0.12, 0.9), 0.06); box("#5B21B6", edge, R(0.88, 0.1, 0.12, 0.9), 0.06);
                box("#7C3AED", edge, R(0.12, 0.34, 0.38, 0.62), 0.05); box("#7C3AED", edge, R(0.5, 0.34, 0.38, 0.62), 0.05);
                break;
            case "bookshelf":
                box("#78350F", edge, R(0, 0, 1, 1), 0.02);
                {
                    string[] cs = { "#DC2626", "#2563EB", "#16A34A", "#CA8A04", "#7C3AED", "#0891B2", "#DB2777", "#EA580C" };
                    double x = 0.04;
                    for (int i = 0; x < 0.94; i++) { double w = 0.045 + (i * 37 % 5) * 0.01; box(cs[i % cs.Length], null, R(x, 0.12, Math.Min(w, 0.96 - x), 0.76), 0); x += w + 0.008; }
                }
                break;
            case "rackcab":
                box("#111827", Ln("#64748B", u * 0.05), R(0, 0, 1, 1), 0.04);
                for (int j = 0; j < 8; j++)
                {
                    box("#1F2937", null, R(0.12, 0.06 + j * 0.115, 0.76, 0.09), 0);
                    oval(j % 3 == 0 ? "#38BDF8" : "#4ADE80", null, P(0.78, 0.105 + j * 0.115), 0.04, 0.02);
                }
                break;
            case "cabinet":
                box("#9CA3AF", edge, R(0, 0, 1, 1), 0.04);
                for (int j = 0; j < 3; j++) { box("#D1D5DB", edge, R(0.08, 0.06 + j * 0.31, 0.84, 0.27), 0.02); box("#374151", null, R(0.35, 0.16 + j * 0.31, 0.3, 0.06), 0.02); }
                break;
            // ---------------------------------------------------- outdoor (from the side)
            case "tree":
                box("#7C4A1E", edge, R(0.43, 0.6, 0.14, 0.4), 0.02);
                oval("#15803D", edge, P(0.5, 0.42), 0.42, 0.36);
                oval("#22C55E", null, P(0.38, 0.32), 0.2, 0.16);
                oval("#16A34A", null, P(0.64, 0.5), 0.18, 0.15);
                break;
            case "bush":
                oval("#166534", edge, P(0.28, 0.62), 0.27, 0.36); oval("#15803D", edge, P(0.72, 0.62), 0.27, 0.36); oval("#22C55E", edge, P(0.5, 0.45), 0.3, 0.42);
                break;
            case "car":
                poly("#93C5FD", edge, new[] { P(0.22, 0.45), P(0.34, 0.12), P(0.68, 0.12), P(0.8, 0.45) });
                box("#DC2626", edge, R(0.02, 0.42, 0.96, 0.38), 0.1);
                line("#0F172A", 0.02, P(0.51, 0.12), P(0.51, 0.45));
                oval("#FDE68A", null, P(0.95, 0.55), 0.025, 0.05);
                oval("#111827", edge, P(0.24, 0.8), 0.1, 0.19); oval("#111827", edge, P(0.76, 0.8), 0.1, 0.19);
                oval("#9CA3AF", null, P(0.24, 0.8), 0.04, 0.08); oval("#9CA3AF", null, P(0.76, 0.8), 0.04, 0.08);
                break;
            case "bus":
                box("#EAB308", edge, R(0.01, 0.08, 0.98, 0.72), 0.06);
                for (int i = 0; i < 6; i++) box("#BAE6FD", edge, R(0.05 + i * 0.13, 0.18, 0.1, 0.28), 0.02);
                box("#1E293B", edge, R(0.84, 0.18, 0.1, 0.52), 0.02);
                line("#0F172A", 0.02, P(0.02, 0.56), P(0.83, 0.56));
                oval("#111827", edge, P(0.2, 0.82), 0.06, 0.17); oval("#111827", edge, P(0.75, 0.82), 0.06, 0.17);
                break;
            case "bicycle":
                {
                    var wp = Ln("#0F172A", u * 0.09);
                    oval(null, wp, P(0.2, 0.68), 0.17, 0.29); oval(null, wp, P(0.8, 0.68), 0.17, 0.29);
                    var fp = "#DC2626";
                    line(fp, 0.08, P(0.2, 0.68), P(0.45, 0.68)); line(fp, 0.08, P(0.45, 0.68), P(0.68, 0.3)); line(fp, 0.08, P(0.2, 0.68), P(0.38, 0.3));
                    line(fp, 0.08, P(0.38, 0.3), P(0.68, 0.3)); line(fp, 0.08, P(0.68, 0.3), P(0.8, 0.68)); line("#0F172A", 0.08, P(0.66, 0.2), P(0.76, 0.16));
                    line("#0F172A", 0.1, P(0.33, 0.18), P(0.44, 0.18)); line(fp, 0.07, P(0.38, 0.3), P(0.38, 0.18)); line(fp, 0.07, P(0.68, 0.3), P(0.66, 0.2));
                    break;
                }
            case "person":
                oval("#FBBF24", edge, P(0.5, 0.12), 0.26, 0.11);
                box("#2563EB", edge, R(0.14, 0.25, 0.72, 0.4), 0.15);
                box("#1E293B", edge, R(0.2, 0.62, 0.26, 0.37), 0.05); box("#1E293B", edge, R(0.54, 0.62, 0.26, 0.37), 0.05);
                break;
            case "streetlight":
                line("#64748B", 0.14, P(0.4, 1), P(0.4, 0.1)); line("#64748B", 0.12, P(0.4, 0.1), P(0.78, 0.1));
                oval("#FDE68A", null, P(0.78, 0.16), 0.3, 0.07);
                box("#475569", edge, R(0.62, 0.1, 0.32, 0.06), 0.02);
                box("#475569", edge, R(0.2, 0.92, 0.4, 0.08), 0.02);
                break;
            // ---------------------------------------------------- telecom
            case "celltower":
                {
                    var st = Ln("#94A3B8", u * 0.06);
                    dc.DrawLine(st, P(0.2, 1), P(0.45, 0.12)); dc.DrawLine(st, P(0.8, 1), P(0.55, 0.12));
                    for (int i = 0; i < 6; i++)
                    {
                        double y1 = 1 - i * 0.15, y2 = 1 - (i + 1) * 0.15;
                        double a = 0.2 + (1 - y1) * 0.28, b = 0.8 - (1 - y1) * 0.28, a2 = 0.2 + (1 - y2) * 0.28, b2 = 0.8 - (1 - y2) * 0.28;
                        dc.DrawLine(Ln("#64748B", u * 0.03), P(a, y1), P(b2, y2)); dc.DrawLine(Ln("#64748B", u * 0.03), P(b, y1), P(a2, y2));
                    }
                    box("#E5E7EB", edge, R(0.28, 0.1, 0.1, 0.16), 0.02); box("#E5E7EB", edge, R(0.62, 0.1, 0.1, 0.16), 0.02); box("#E5E7EB", edge, R(0.45, 0.04, 0.1, 0.16), 0.02);
                    for (int i = 1; i <= 2; i++)
                    {
                        var arc = new Pen(NetIcons.B("#38BDF8"), u * 0.04);
                        int ii = i;
                        dc.DrawGeometry(null, arc, Curve(c => { c.BeginFigure(P(0.5 - 0.14 * ii - 0.08, 0.02 + 0.03 * ii), false); c.ArcTo(P(0.5 - 0.14 * ii - 0.08, 0.26 - 0.03 * ii), new Size(0.12 * ii * r.Width, 0.12 * ii * r.Height), 0, false, SweepDirection.CounterClockwise); c.EndFigure(false); }));
                        dc.DrawGeometry(null, arc, Curve(c => { c.BeginFigure(P(0.5 + 0.14 * ii + 0.08, 0.02 + 0.03 * ii), false); c.ArcTo(P(0.5 + 0.14 * ii + 0.08, 0.26 - 0.03 * ii), new Size(0.12 * ii * r.Width, 0.12 * ii * r.Height), 0, false, SweepDirection.Clockwise); c.EndFigure(false); }));
                    }
                    break;
                }
            case "dish":
                line("#64748B", 0.08, P(0.5, 0.62), P(0.5, 0.95)); box("#475569", edge, R(0.32, 0.9, 0.36, 0.1), 0.02);
                using (dc.PushTransform(RotateAt(-35, P(0.45, 0.42).X, P(0.45, 0.42).Y)))
                {
                    oval("#E5E7EB", edge, P(0.45, 0.42), 0.38, 0.17);
                    oval("#CBD5E1", null, P(0.45, 0.45), 0.28, 0.1);
                }
                line("#64748B", 0.04, P(0.45, 0.45), P(0.72, 0.2)); oval("#475569", null, P(0.72, 0.2), 0.05, 0.05);
                break;
            case "antenna":
                line("#94A3B8", 0.08, P(0.5, 0.05), P(0.5, 0.92));
                for (int i = 0; i < 4; i++) line("#CBD5E1", 0.05, P(0.2 + i * 0.03, 0.15 + i * 0.12), P(0.8 - i * 0.03, 0.15 + i * 0.12));
                oval("#EF4444", null, P(0.5, 0.05), 0.07, 0.04);
                box("#475569", edge, R(0.3, 0.9, 0.4, 0.1), 0.02);
                break;
            case "pole":
                box("#7C4A1E", edge, R(0.45, 0.06, 0.1, 0.94), 0.02);
                box("#7C4A1E", edge, R(0.08, 0.14, 0.84, 0.05), 0.01);
                foreach (var x in new[] { 0.14, 0.34, 0.66, 0.86 }) box("#E5E7EB", edge, R(x - 0.025, 0.08, 0.05, 0.06), 0.01);
                line("#1F2937", 0.015, P(0, 0.09), P(1, 0.11)); line("#1F2937", 0.015, P(0, 0.12), P(1, 0.1));
                break;
            case "manhole":
                oval("#4B5563", Ln("#1F2937", u * 0.06), P(0.5, 0.5), 0.46, 0.46);
                oval("#374151", Ln("#6B7280", u * 0.03), P(0.5, 0.5), 0.36, 0.36);
                for (int i = 0; i < 5; i++) { line("#6B7280", 0.025, P(0.24 + i * 0.13, 0.22), P(0.24 + i * 0.13, 0.78)); line("#6B7280", 0.025, P(0.22, 0.24 + i * 0.13), P(0.78, 0.24 + i * 0.13)); }
                break;
            // ---------------------------------------------------- fun
            case "coffee":
                box("#1F2937", edge, R(0.08, 0.02, 0.84, 0.96), 0.08);
                box("#475569", null, R(0.16, 0.08, 0.68, 0.18), 0.04);
                oval("#4ADE80", null, P(0.72, 0.17), 0.05, 0.04); oval("#F87171", null, P(0.56, 0.17), 0.05, 0.04);
                box("#0F172A", null, R(0.2, 0.34, 0.6, 0.5), 0.02);
                box("#94A3B8", edge, R(0.42, 0.34, 0.16, 0.08), 0.01);
                box("#F8FAFC", edge, R(0.36, 0.58, 0.28, 0.24), 0.03);
                line("#CBD5E1", 0.02, P(0.45, 0.52), P(0.47, 0.46)); line("#CBD5E1", 0.02, P(0.55, 0.52), P(0.53, 0.46));
                box("#334155", null, R(0.16, 0.84, 0.68, 0.06), 0.01);
                break;
            case "plant":
                oval("#15803D", edge, P(0.3, 0.32), 0.18, 0.12); oval("#16A34A", edge, P(0.7, 0.3), 0.18, 0.12); oval("#22C55E", edge, P(0.5, 0.2), 0.14, 0.18);
                oval("#4ADE80", edge, P(0.5, 0.45), 0.2, 0.1);
                poly("#C2410C", edge, new[] { P(0.2, 0.55), P(0.8, 0.55), P(0.7, 1), P(0.3, 1) });
                box("#9A3412", edge, R(0.16, 0.52, 0.68, 0.09), 0.02);
                break;
            default:
                box("#64748B", edge, R(0.1, 0.1, 0.8, 0.8), 0.1);
                break;
        }
    }
}

/// <summary>A library object as a small picture (the objects library's buttons).</summary>
public sealed class ObjectIcon : Control
{
    public readonly string Art;
    public ObjectIcon(string art, double size) { Art = art; Width = size; Height = size; }
    public override void Render(DrawingContext dc)
    {
        double size = Math.Min(Bounds.Width, Bounds.Height);
        var a = ObjectCatalog.Get(Art);
        double w = size, h = size;
        if (a != null) { double ratio = a.PxW / a.PxH; if (ratio > 1) h = size / ratio; else w = size * ratio; }
        DrawArt.Object(dc, Art, new Rect((Bounds.Width - w) / 2, (Bounds.Height - h) / 2, w, h));
    }
}
