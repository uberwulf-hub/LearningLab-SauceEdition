// Network Lab - device pictures, drawn in code (original simple shapes, no vendor artwork). App Hub's Icons.cs for
// Avalonia: the same shapes and colours.
using Avalonia;
using Avalonia.Media;
using NetworkLab;
using Rect = Avalonia.Rect;

namespace LearningLab.NetworkLab;

public static class NetIcons
{
    static readonly Dictionary<string, IBrush> brushes = new();
    public static IBrush B(string hex)
    {
        if (!brushes.TryGetValue(hex, out var b)) brushes[hex] = b = new SolidColorBrush(Color.Parse(hex)).ToImmutable();
        return b;
    }
    static IBrush Grad(string a, string b)
    {
        var g = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative) };
        g.GradientStops.Add(new GradientStop(Color.Parse(a), 0)); g.GradientStops.Add(new GradientStop(Color.Parse(b), 1));
        return g;
    }
    public static IPen P(string hex, double w) => new Pen(B(hex), w) { LineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
    static Geometry Ell(Point c, double rx, double ry) => new EllipseGeometry(new Rect(c.X - rx, c.Y - ry, rx * 2, ry * 2));
    static Geometry Union(params Geometry[] gs)
    {
        Geometry g = gs[0];
        for (int i = 1; i < gs.Length; i++) g = new CombinedGeometry(GeometryCombineMode.Union, g, gs[i]);
        return g;
    }
    static void DrawRoundRect(this DrawingContext dc, IBrush b, IPen p, Rect r, double rx, double ry) => dc.DrawRectangle(b, p, r, rx, ry);

    static string Screen(string os)
    {
        switch (Personalities.Get(os).Family)
        {
            case "windows": return "#3B82F6";
            case "linux": return "#F59E0B";
            case "mac": return "#A78BFA";
            case "phone": return "#34D399";
        }
        return "#38BDF8";
    }

    /// <summary>Draws a device into a rectangle (about 4:3 works best).</summary>
    public static void Draw(DrawingContext dc, string kind, string os, Rect r, bool on)
    {
        double x = r.X, y = r.Y, w = r.Width, h = r.Height;
        IPen edge = P("#0B1220", Math.Max(1, w / 40));
        string scr = on ? Screen(os) : "#334155";
        switch (kind)
        {
            case "pc":
                {
                    var mon = new Rect(x + w * 0.12, y + h * 0.06, w * 0.76, h * 0.62);
                    dc.DrawRoundRect(Grad("#CBD5E1", "#64748B"), edge, mon, w * 0.05, w * 0.05);
                    dc.DrawRoundRect(Grad(scr, "#0F172A"), null, new Rect(mon.X + w * 0.05, mon.Y + h * 0.06, mon.Width - w * 0.1, mon.Height - h * 0.12), w * 0.02, w * 0.02);
                    var st = new StreamGeometry();
                    using (var c = st.Open())
                    {
                        c.BeginFigure(new Point(x + w * 0.44, y + h * 0.68), true);
                        c.LineTo(new Point(x + w * 0.56, y + h * 0.68));
                        c.LineTo(new Point(x + w * 0.62, y + h * 0.86));
                        c.LineTo(new Point(x + w * 0.38, y + h * 0.86));
                        c.EndFigure(true);
                    }
                    dc.DrawGeometry(B("#94A3B8"), edge, st);
                    dc.DrawRoundRect(B("#64748B"), edge, new Rect(x + w * 0.28, y + h * 0.86, w * 0.44, h * 0.08), 2, 2);
                    break;
                }
            case "laptop":
                {
                    var lid = new Rect(x + w * 0.18, y + h * 0.1, w * 0.64, h * 0.56);
                    dc.DrawRoundRect(Grad("#CBD5E1", "#64748B"), edge, lid, w * 0.04, w * 0.04);
                    dc.DrawRectangle(Grad(scr, "#0F172A"), null, new Rect(lid.X + w * 0.04, lid.Y + h * 0.05, lid.Width - w * 0.08, lid.Height - h * 0.1));
                    var b = new StreamGeometry();
                    using (var c = b.Open())
                    {
                        c.BeginFigure(new Point(x + w * 0.14, y + h * 0.68), true);
                        c.LineTo(new Point(x + w * 0.86, y + h * 0.68));
                        c.LineTo(new Point(x + w * 0.96, y + h * 0.84));
                        c.LineTo(new Point(x + w * 0.04, y + h * 0.84));
                        c.EndFigure(true);
                    }
                    dc.DrawGeometry(Grad("#E2E8F0", "#94A3B8"), edge, b);
                    break;
                }
            case "phone": case "tablet":
                {
                    double pw = kind == "phone" ? w * 0.38 : w * 0.62, ph = h * 0.86;
                    var body = new Rect(x + (w - pw) / 2, y + h * 0.06, pw, ph);
                    dc.DrawRoundRect(Grad("#334155", "#0F172A"), P("#94A3B8", Math.Max(1, w / 30)), body, w * 0.07, w * 0.07);
                    dc.DrawRoundRect(Grad(scr, "#1E293B"), null, new Rect(body.X + w * 0.04, body.Y + h * 0.08, body.Width - w * 0.08, body.Height - h * 0.16), w * 0.03, w * 0.03);
                    dc.DrawEllipse(B("#94A3B8"), null, new Point(body.X + body.Width / 2, body.Bottom - h * 0.04), w * 0.025, w * 0.025);
                    break;
                }
            case "server":
                {
                    var body = new Rect(x + w * 0.3, y + h * 0.04, w * 0.4, h * 0.9);
                    dc.DrawRoundRect(Grad("#475569", "#1E293B"), edge, body, w * 0.03, w * 0.03);
                    for (int i = 0; i < 4; i++)
                    {
                        double yy = body.Y + h * 0.1 + i * h * 0.18;
                        dc.DrawRoundRect(B("#0F172A"), null, new Rect(body.X + w * 0.04, yy, body.Width - w * 0.08, h * 0.11), 2, 2);
                        dc.DrawEllipse(B(on ? (i == 0 ? "#4ADE80" : "#60A5FA") : "#334155"), null, new Point(body.Right - w * 0.08, yy + h * 0.055), w * 0.018, w * 0.018);
                    }
                    break;
                }
            case "printer":
                {
                    dc.DrawRectangle(B("#F8FAFC"), P("#94A3B8", 1), new Rect(x + w * 0.3, y + h * 0.08, w * 0.4, h * 0.3));
                    var body = new Rect(x + w * 0.12, y + h * 0.3, w * 0.76, h * 0.42);
                    dc.DrawRoundRect(Grad("#E2E8F0", "#94A3B8"), edge, body, w * 0.04, w * 0.04);
                    dc.DrawRectangle(B("#1E293B"), null, new Rect(x + w * 0.22, y + h * 0.62, w * 0.56, h * 0.05));
                    dc.DrawRectangle(B("#F8FAFC"), P("#94A3B8", 1), new Rect(x + w * 0.26, y + h * 0.66, w * 0.48, h * 0.2));
                    dc.DrawEllipse(B(on ? "#4ADE80" : "#334155"), null, new Point(x + w * 0.8, y + h * 0.4), w * 0.025, w * 0.025);
                    break;
                }
            case "switch":
                {
                    var body = new Rect(x + w * 0.04, y + h * 0.3, w * 0.92, h * 0.4);
                    dc.DrawRoundRect(Grad("#3B82F6", "#1E3A8A"), edge, body, w * 0.04, w * 0.04);
                    IPen ar = P("#E0F2FE", Math.Max(1.2, w / 28));
                    double cy = body.Y + body.Height * 0.32, cy2 = body.Y + body.Height * 0.68;
                    Arrow(dc, ar, new Point(x + w * 0.22, cy), new Point(x + w * 0.78, cy), w * 0.06);
                    Arrow(dc, ar, new Point(x + w * 0.78, cy2), new Point(x + w * 0.22, cy2), w * 0.06);
                    break;
                }
            case "router":
                {
                    var body = new Rect(x + w * 0.1, y + h * 0.22, w * 0.8, h * 0.56);
                    dc.DrawRoundRect(Grad("#22D3EE", "#0E7490"), edge, body, h * 0.28, h * 0.28);
                    IPen ar = P("#ECFEFF", Math.Max(1.2, w / 28));
                    var c0 = new Point(x + w / 2, y + h / 2);
                    double d = h * 0.17, l = h * 0.06;
                    Arrow(dc, ar, new Point(c0.X - w * 0.24, c0.Y - d), new Point(c0.X - w * 0.06, c0.Y - l * 0.3), w * 0.05);
                    Arrow(dc, ar, new Point(c0.X + w * 0.24, c0.Y + d), new Point(c0.X + w * 0.06, c0.Y + l * 0.3), w * 0.05);
                    Arrow(dc, ar, new Point(c0.X + w * 0.06, c0.Y - l * 0.3), new Point(c0.X + w * 0.24, c0.Y - d), w * 0.05);
                    Arrow(dc, ar, new Point(c0.X - w * 0.06, c0.Y + l * 0.3), new Point(c0.X - w * 0.24, c0.Y + d), w * 0.05);
                    break;
                }
            case "homerouter":
                {
                    IPen ant = P("#475569", Math.Max(1.5, w / 22));
                    dc.DrawLine(ant, new Point(x + w * 0.28, y + h * 0.5), new Point(x + w * 0.2, y + h * 0.08));
                    dc.DrawLine(ant, new Point(x + w * 0.72, y + h * 0.5), new Point(x + w * 0.8, y + h * 0.08));
                    var body = new Rect(x + w * 0.1, y + h * 0.46, w * 0.8, h * 0.34);
                    dc.DrawRoundRect(Grad("#F1F5F9", "#94A3B8"), edge, body, w * 0.05, w * 0.05);
                    for (int i = 0; i < 4; i++) dc.DrawEllipse(B(on ? (i == 0 ? "#60A5FA" : "#4ADE80") : "#475569"), null, new Point(x + w * (0.28 + i * 0.14), y + h * 0.63), w * 0.025, w * 0.025);
                    break;
                }
            case "ap":
                {
                    var g = new StreamGeometry();
                    using (var c = g.Open())
                    {
                        c.BeginFigure(new Point(x + w * 0.2, y + h * 0.78), true);
                        c.ArcTo(new Point(x + w * 0.8, y + h * 0.78), new Size(w * 0.3, h * 0.22), 0, false, SweepDirection.Clockwise);
                        c.EndFigure(true);
                    }
                    dc.DrawGeometry(Grad("#F1F5F9", "#94A3B8"), edge, g);
                    IPen wv = P("#38BDF8", Math.Max(1.2, w / 30));
                    for (int i = 0; i < 3; i++)
                    {
                        double rr = w * (0.12 + i * 0.1);
                        var arc = new StreamGeometry();
                        using (var c = arc.Open())
                        {
                            var ctr = new Point(x + w / 2, y + h * 0.5);
                            c.BeginFigure(new Point(ctr.X - rr * 0.7, ctr.Y - rr * 0.7), false);
                            c.ArcTo(new Point(ctr.X + rr * 0.7, ctr.Y - rr * 0.7), new Size(rr, rr), 0, false, SweepDirection.Clockwise);
                            c.EndFigure(false);
                        }
                        dc.DrawGeometry(null, wv, arc);
                    }
                    break;
                }
            case "cloud":
                {
                    var outline = Union(Ell(new Point(x + w * 0.3, y + h * 0.58), w * 0.2, h * 0.2), Ell(new Point(x + w * 0.5, y + h * 0.42), w * 0.24, h * 0.26),
                                        Ell(new Point(x + w * 0.7, y + h * 0.56), w * 0.2, h * 0.22), new RectangleGeometry(new Rect(x + w * 0.3, y + h * 0.56, w * 0.4, h * 0.22)));
                    dc.DrawGeometry(Grad("#F8FAFC", "#93C5FD"), P("#1E3A8A", Math.Max(1, w / 40)), outline);
                    break;
                }
            case "nas":
                {
                    var body = new Rect(x + w * 0.22, y + h * 0.14, w * 0.56, h * 0.74);
                    dc.DrawRoundRect(Grad("#334155", "#0F172A"), edge, body, w * 0.04, w * 0.04);
                    for (int i = 0; i < 2; i++) dc.DrawRoundRect(B("#475569"), null, new Rect(body.X + w * 0.06 + i * w * 0.23, body.Y + h * 0.1, w * 0.19, h * 0.52), 2, 2);
                    dc.DrawEllipse(B(on ? "#4ADE80" : "#334155"), null, new Point(body.X + body.Width / 2, body.Bottom - h * 0.08), w * 0.025, w * 0.025);
                    break;
                }
            case "camera":
                {
                    dc.DrawRectangle(B("#64748B"), null, new Rect(x + w * 0.46, y + h * 0.5, w * 0.08, h * 0.3));
                    dc.DrawRoundRect(B("#475569"), edge, new Rect(x + w * 0.3, y + h * 0.78, w * 0.4, h * 0.08), 2, 2);
                    dc.DrawRoundRect(Grad("#F1F5F9", "#94A3B8"), edge, new Rect(x + w * 0.16, y + h * 0.22, w * 0.62, h * 0.32), w * 0.06, w * 0.06);
                    dc.DrawEllipse(B("#0F172A"), P("#64748B", 1), new Point(x + w * 0.74, y + h * 0.38), w * 0.1, w * 0.1);
                    dc.DrawEllipse(B(on ? "#F87171" : "#334155"), null, new Point(x + w * 0.26, y + h * 0.3), w * 0.02, w * 0.02);
                    break;
                }
            case "tv":
                {
                    var s = new Rect(x + w * 0.06, y + h * 0.12, w * 0.88, h * 0.6);
                    dc.DrawRoundRect(B("#0F172A"), P("#64748B", Math.Max(1, w / 30)), s, w * 0.02, w * 0.02);
                    dc.DrawRectangle(Grad(on ? "#818CF8" : "#1E293B", "#1E1B4B"), null, new Rect(s.X + w * 0.03, s.Y + h * 0.04, s.Width - w * 0.06, s.Height - h * 0.08));
                    dc.DrawLine(P("#64748B", Math.Max(1.5, w / 25)), new Point(x + w * 0.3, y + h * 0.84), new Point(x + w * 0.36, y + h * 0.72));
                    dc.DrawLine(P("#64748B", Math.Max(1.5, w / 25)), new Point(x + w * 0.7, y + h * 0.84), new Point(x + w * 0.64, y + h * 0.72));
                    break;
                }
            case "console":
                {
                    var pad0 = Union(Ell(new Point(x + w * 0.3, y + h * 0.55), w * 0.18, h * 0.24), Ell(new Point(x + w * 0.7, y + h * 0.55), w * 0.18, h * 0.24),
                                     new RectangleGeometry(new Rect(x + w * 0.3, y + h * 0.33, w * 0.4, h * 0.3)) { RadiusX = w * 0.06, RadiusY = w * 0.06 });
                    dc.DrawGeometry(Grad("#475569", "#1E293B"), edge, pad0);
                    IPen pad = P("#E2E8F0", Math.Max(1.2, w / 30));
                    dc.DrawLine(pad, new Point(x + w * 0.22, y + h * 0.52), new Point(x + w * 0.36, y + h * 0.52));
                    dc.DrawLine(pad, new Point(x + w * 0.29, y + h * 0.45), new Point(x + w * 0.29, y + h * 0.59));
                    dc.DrawEllipse(B("#F87171"), null, new Point(x + w * 0.72, y + h * 0.47), w * 0.03, w * 0.03);
                    dc.DrawEllipse(B("#60A5FA"), null, new Point(x + w * 0.78, y + h * 0.55), w * 0.03, w * 0.03);
                    break;
                }
            case "patch":
                {
                    var body = new Rect(x + w * 0.02, y + h * 0.38, w * 0.96, h * 0.24);
                    dc.DrawRoundRect(Grad("#64748B", "#334155"), edge, body, 2, 2);
                    for (int i = 0; i < 8; i++) dc.DrawRectangle(B("#0F172A"), null, new Rect(body.X + w * 0.06 + i * w * 0.11, body.Y + h * 0.06, w * 0.07, h * 0.1));
                    break;
                }
            case "jack":
                {
                    var body = new Rect(x + w * 0.3, y + h * 0.2, w * 0.4, h * 0.6);
                    dc.DrawRoundRect(Grad("#F8FAFC", "#CBD5E1"), edge, body, w * 0.03, w * 0.03);
                    dc.DrawRectangle(B("#0F172A"), null, new Rect(x + w * 0.42, y + h * 0.42, w * 0.16, h * 0.14));
                    break;
                }
            default:
                dc.DrawRoundRect(B("#64748B"), edge, new Rect(x + w * 0.2, y + h * 0.2, w * 0.6, h * 0.6), 4, 4);
                break;
        }
    }

    public static void Arrow(DrawingContext dc, IPen pen, Point a, Point b, double head)
    {
        dc.DrawLine(pen, a, b);
        var v = new Vector(b.X - a.X, b.Y - a.Y);
        double len = Math.Sqrt(v.X * v.X + v.Y * v.Y);
        if (len < 0.01) return;
        v = new Vector(v.X / len, v.Y / len);
        var n = new Vector(-v.Y, v.X);
        dc.DrawLine(pen, b, b - v * head + n * head * 0.6);
        dc.DrawLine(pen, b, b - v * head - n * head * 0.6);
    }
}

/// <summary>A device picture as a control (palette buttons, lists).</summary>
public sealed class DeviceIcon : Avalonia.Controls.Control
{
    readonly string kind, os;
    public DeviceIcon(string kind, string os, double w = 44) { this.kind = kind; this.os = os; Width = w; Height = w * 0.8; }
    public override void Render(DrawingContext dc) => NetIcons.Draw(dc, kind, os, new Rect(Bounds.Size), true);
}
