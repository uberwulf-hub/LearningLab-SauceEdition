// Learning Games - pictures for the math problems, drawn with shapes (no images): base-ten blocks and regrouping,
// bills and coins, graphs, shapes and equal parts, fraction bars and the "how many fit" model for dividing fractions,
// tape diagrams, double number lines, ratio tables, the coordinate plane, inequalities on a number line, composite
// shapes, nets, dot plots, box plots and element tiles. Problem.Visual names the picture ("blocks:347", ...);
// the older pictures (clock, coins, arrays, ...) are drawn by the pages.
// Ported from App Hub's Learning Games (Engine\Visuals.cs, WPF) to Avalonia with the same sizes, colours, font sizes
// and positions: every picture is a Canvas with its Width and Height set.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Path = Avalonia.Controls.Shapes.Path;

namespace LearningGames
{
    public static class Visuals
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        public const string TextC = "#E6E9F0", Muted = "#8C93A5", Blue = "#3B82F6", Blue2 = "#60A5FA", Yellow = "#FBBF24", Green = "#34D399", Red = "#F87171", Orange = "#F97316", Purple = "#A855F7";

        /// <summary>The digits' font: Consolas on Windows, the system's fixed-width font elsewhere (Android).</summary>
        public static readonly FontFamily Mono = new FontFamily("Consolas, Cascadia Mono, Courier New, monospace");
        static readonly ConcurrentDictionary<string, IBrush> brushes = new ConcurrentDictionary<string, IBrush>();

        public static IBrush B(string hex) { return B(hex, 255); }
        /// <summary>A brush for "#RRGGBB" with this alpha (immutable, shared). A bad colour throws, as in App Hub.</summary>
        public static IBrush B(string hex, byte alpha)
        {
            string key = hex + "/" + alpha;
            IBrush b;
            if (brushes.TryGetValue(key, out b)) return b;
            Color c = Color.Parse(hex);
            b = new ImmutableSolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
            brushes[key] = b;
            return b;
        }
        public static TextBlock T(string text, double size, string color, bool bold)
        {
            TextBlock t = new TextBlock(); t.Text = text; t.FontSize = size; t.Foreground = B(color);
            if (bold) t.FontWeight = FontWeight.SemiBold;
            return t;
        }
        public static Size Measure(TextBlock t) { t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); return t.DesiredSize; }
        public static void At(Canvas c, Control e, double x, double y) { Canvas.SetLeft(e, x); Canvas.SetTop(e, y); c.Children.Add(e); }
        /// <summary>Text centred on (x, y).</summary>
        public static void Label(Canvas c, string text, double x, double y, double size, string color, bool bold)
        {
            TextBlock t = T(text, size, color, bold); Size s = Measure(t); At(c, t, x - s.Width / 2, y - s.Height / 2);
        }
        public static Rectangle Box(Canvas c, double x, double y, double w, double h, string fill, byte alpha, string stroke, double thick)
        {
            Rectangle r = new Rectangle(); r.Width = Math.Max(0, w); r.Height = Math.Max(0, h);
            if (fill != null) r.Fill = B(fill, alpha);
            if (stroke != null) { r.Stroke = B(stroke); r.StrokeThickness = thick; }
            At(c, r, x, y); return r;
        }
        public static Line Ln(Canvas c, double x1, double y1, double x2, double y2, string color, double thick)
        {
            Line l = new Line(); l.StartPoint = new Point(x1, y1); l.EndPoint = new Point(x2, y2); l.Stroke = B(color); l.StrokeThickness = thick;
            l.StrokeLineCap = PenLineCap.Round; c.Children.Add(l); return l;
        }
        static Line Dash(Canvas c, double x1, double y1, double x2, double y2, string color)
        {
            Line l = Ln(c, x1, y1, x2, y2, color, 2); l.StrokeDashArray = new AvaloniaList<double> { 4, 3 }; return l;
        }
        public static Polygon Poly(Canvas c, string fill, byte alpha, string stroke, params Point[] pts)
        {
            Polygon p = new Polygon(); p.Points = new List<Point>(pts);
            if (fill != null) p.Fill = B(fill, alpha);
            if (stroke != null) { p.Stroke = B(stroke); p.StrokeThickness = 2.5; p.StrokeJoin = PenLineJoin.Round; }
            c.Children.Add(p); return p;
        }
        static Ellipse Dot(Canvas c, double x, double y, double r, string fill, string stroke)
        {
            Ellipse e = new Ellipse(); e.Width = e.Height = 2 * r; e.Fill = B(fill);
            if (stroke != null) { e.Stroke = B(stroke); e.StrokeThickness = 2; }
            At(c, e, x - r, y - r); return e;
        }
        public static Ellipse Dot2(Canvas c, double x, double y, double r, string fill) { return Dot(c, x, y, r, fill, null); }
        static Canvas New(double w, double h) { Canvas c = new Canvas(); c.Width = w; c.Height = h; return c; }
        static double D(string s) { double v; double.TryParse(s.Replace("$", ""), NumberStyles.Float, Inv, out v); return v; }
        static bool IsNum(string s) { double v; return double.TryParse(s.Replace("$", ""), NumberStyles.Float, Inv, out v); }

        /// <summary>The picture for a problem, or null when this file doesn't draw that kind.</summary>
        public static Control Draw(string visual)
        {
            if (string.IsNullOrEmpty(visual)) return null;
            int colon = visual.IndexOf(':');
            string kind = colon < 0 ? visual : visual.Substring(0, colon), arg = colon < 0 ? "" : visual.Substring(colon + 1);
            try
            {
                switch (kind)
                {
                    case "blocks": return Blocks(int.Parse(arg, Inv));
                    case "regroup": { string[] a = arg.Split(','); return Regroup(int.Parse(a[0], Inv), int.Parse(a[1], Inv), a[2] == "-"); }
                    case "cash": return Cash(arg.Split(',').Select(s => int.Parse(s, Inv)).ToArray());
                    case "graph": return Graph(arg.Split(';'));
                    case "shape": return Shape(arg);
                    case "parts": { int[] a = arg.Split(',').Select(s => int.Parse(s, Inv)).ToArray(); return Parts(a[0], a[1], a[2]); }
                    case "fracbars": return FracBars(arg.Split(','));
                    case "fracdiv": { string[] a = arg.Split(','); return FracDiv(a[0], a[1]); }
                    case "tape": { string[] a = arg.Split(','); return Tape(int.Parse(a[0], Inv), a[1]); }
                    case "dnl": return DoubleLine(arg.Split(';'));
                    case "rtable": return RatioTable(arg.Split(';'));
                    case "grid": return Grid(arg);
                    case "ineq": { string[] a = arg.Split(','); return Ineq(int.Parse(a[0], Inv), a[1] == "closed", a[2] == "right"); }
                    case "lshape": { int[] a = arg.Split(',').Select(s => int.Parse(s, Inv)).ToArray(); return LShape(a[0], a[1], a[2], a[3]); }
                    case "house": { int[] a = arg.Split(',').Select(s => int.Parse(s, Inv)).ToArray(); return House(a[0], a[1], a[2]); }
                    case "trap": { int[] a = arg.Split(',').Select(s => int.Parse(s, Inv)).ToArray(); return Trap(a[0], a[1], a[2]); }
                    case "hole": { int[] a = arg.Split(',').Select(s => int.Parse(s, Inv)).ToArray(); return Hole(a[0], a[1], a[2]); }
                    case "net": return Net(arg.Split(','));
                    case "dots": { string[] a = arg.Split(';'); return DotPlot(a[0], a[1].Split(',').Select(s => int.Parse(s, Inv)).ToArray()); }
                    case "boxplot": { string[] a = arg.Split(';'); return BoxPlot(a[0], a[1].Split(',').Select(D).ToArray()); }
                    case "scene": return Scene(arg);
                    case "bracket": { string[] a = arg.Split(','); return Bracket(a[0], a[1]); }
                    case "element": { string[] a = arg.Split(','); return Element(int.Parse(a[0], Inv), a[1], a[2], a.Length > 3 ? a[3] : ""); }
                }
            }
            catch (Exception) { return null; }
            return null;
        }

        // ---------------------------------------------------------------- base-ten blocks
        /// <summary>Hundreds flats, tens rods and ones cubes for n, with a label under each group.</summary>
        public static Canvas Blocks(int n) { return BlocksScaled(n, n >= 100 ? 10 : 13, true); }
        static Canvas BlocksScaled(int n, double u, bool labels)
        {
            int h = n / 100, t = n / 10 % 10, o = n % 10;
            double flat = 10 * u, gap = Math.Max(4, u * 0.7);
            int flatCols = Math.Min(h, 5), flatRows = h > 5 ? 2 : (h > 0 ? 1 : 0);
            double fw = flatCols * (flat + gap);
            double tw = t * (u + gap * 0.7);
            double ow = 3 * (u + 3);
            double groupGap = 26;
            double W = (h > 0 ? fw + groupGap : 0) + (t > 0 ? tw + groupGap : 0) + ow + 10;
            double H = Math.Max(flatRows * (flat + gap), flat) + (labels ? 30 : 4);
            Canvas c = New(Math.Max(W, 120), H);
            double x = 0;
            if (h > 0)
            {
                for (int i = 0; i < h; i++) DrawFlat(c, x + (i % 5) * (flat + gap), (i / 5) * (flat + gap), u);
                if (labels) Label(c, h + (h == 1 ? " hundred" : " hundreds"), x + fw / 2, H - 12, 14, "#93C5FD", true);
                x += fw + groupGap;
            }
            if (t > 0)
            {
                for (int i = 0; i < t; i++) DrawRod(c, x + i * (u + gap * 0.7), 0, u, "#22C55E", "#14532D");
                if (labels) Label(c, t + (t == 1 ? " ten" : " tens"), x + tw / 2, H - 12, 14, "#86EFAC", true);
                x += tw + groupGap;
            }
            for (int i = 0; i < o; i++) DrawCube(c, x + (i % 3) * (u + 3), flat - u - (i / 3) * (u + 3), u, "#F97316", "#7C2D12");
            if (labels) Label(c, o + (o == 1 ? " one" : " ones"), x + ow / 2, H - 12, 14, "#FDBA74", true);
            return c;
        }
        static void DrawFlat(Canvas c, double x, double y, double u)
        {
            Box(c, x, y, 10 * u, 10 * u, Blue, 255, "#1E3A8A", 1.5);
            for (int k = 1; k < 10; k++) { Ln(c, x + k * u, y, x + k * u, y + 10 * u, "#93C5FD", 0.6); Ln(c, x, y + k * u, x + 10 * u, y + k * u, "#93C5FD", 0.6); }
        }
        static void DrawRod(Canvas c, double x, double y, double u, string fill, string edge)
        {
            Box(c, x, y, u, 10 * u, fill, 255, edge, 1.2);
            for (int k = 1; k < 10; k++) Ln(c, x, y + k * u, x + u, y + k * u, edge, 0.6);
        }
        static void DrawCube(Canvas c, double x, double y, double u, string fill, string edge) { Box(c, x, y, u, u, fill, 255, edge, 1.2); }

        /// <summary>Adding or subtracting with blocks, showing the trade: 10 ones for a ten (or a ten broken into 10 ones).</summary>
        public static Canvas Regroup(int a, int b, bool sub)
        {
            double u = Math.Max(a, b) >= 600 ? 6.5 : Math.Max(a, b) >= 100 ? 7.5 : 10;
            Canvas top = BlocksScaled(a, u, true), bot = BlocksScaled(b, u, true);
            double rowH = Math.Max(top.Height, bot.Height);
            double W = Math.Max(Math.Max(top.Width, bot.Width) + 60, 560);
            Canvas c = New(W, 2 * rowH + 60);
            Label(c, a.ToString(Inv), 22, rowH / 2 - 8, 22, TextC, true);
            At(c, top, 56, 0);
            Label(c, (sub ? "\u2212 " : "+ ") + b, 22, rowH + 26 + rowH / 2 - 8, 22, TextC, true);
            At(c, bot, 56, rowH + 26);
            Ln(c, 0, rowH + 13, W - 10, rowH + 13, "#353C4B", 2);
            // the trade
            string note;
            if (sub)
            {
                if (a % 10 < b % 10 && a / 10 % 10 == 0) note = "No tens to trade! First 1 hundred becomes 10 tens, then 1 ten becomes 10 ones";
                else if (a % 10 < b % 10) note = "Not enough ones: trade 1 ten for 10 ones (" + (a % 10) + " ones become " + (a % 10 + 10) + ")";
                else if (a / 10 % 10 < b / 10 % 10) note = "Not enough tens: trade 1 hundred for 10 tens";
                else note = "Take away: ones, then tens, then hundreds";
            }
            else
            {
                int os = a % 10 + b % 10, ts = a / 10 % 10 + b / 10 % 10 + (os >= 10 ? 1 : 0);
                if (os >= 10) note = "Ones: " + (a % 10) + " + " + (b % 10) + " = " + os + ". Trade 10 ones for 1 ten!";
                else if (ts >= 10) note = "Tens: " + ts + " tens. Trade 10 tens for 1 hundred!";
                else note = "Put the ones together, then the tens, then the hundreds";
            }
            Border badge = new Border(); badge.CornerRadius = new CornerRadius(10); badge.Background = B("#33280E"); badge.BorderBrush = B(Yellow); badge.BorderThickness = new Thickness(1.5); badge.Padding = new Thickness(12, 6, 12, 6);
            TextBlock bt = T(note, 15, "#FDE68A", true); badge.Child = bt;
            At(c, badge, 10, 2 * rowH + 30);
            return c;
        }

        // ---------------------------------------------------------------- money
        /// <summary>Bills ($1, $5, $10, $20) and coins, given in cents.</summary>
        public static Canvas Cash(int[] items)
        {
            Canvas c = New(600, 10);
            double x = 0, y = 0, rowH = 0;
            foreach (int v in items)
            {
                double w, h;
                if (v >= 100)
                {
                    w = 132; h = 62;
                    if (x + w > 600) { x = 0; y += rowH + 12; rowH = 0; }
                    Box(c, x, y, w, h, "#86EFAC", 255, "#166534", 2.5).RadiusX = 6;
                    Box(c, x + 8, y + 8, w - 16, h - 16, null, 0, "#15803D", 1.2);
                    Ellipse e = new Ellipse(); e.Width = 34; e.Height = 40; e.Stroke = B("#15803D"); e.StrokeThickness = 1.5; e.Fill = B("#BBF7D0"); At(c, e, x + w / 2 - 17, y + h / 2 - 20);
                    Label(c, "$" + (v / 100), x + w / 2, y + h / 2, 18, "#14532D", true);
                    Label(c, (v / 100).ToString(Inv), x + 18, y + 16, 11, "#14532D", true);
                    Label(c, (v / 100).ToString(Inv), x + w - 18, y + h - 16, 11, "#14532D", true);
                }
                else
                {
                    double d = v == 1 ? 40 : v == 5 ? 46 : v == 10 ? 36 : 54;
                    w = d; h = d;
                    if (x + w > 600) { x = 0; y += rowH + 12; rowH = 0; }
                    Ellipse e = new Ellipse(); e.Width = e.Height = d; e.Fill = B(v == 1 ? "#D97706" : "#CBD5E1"); e.Stroke = B("#0B0D12"); e.StrokeThickness = 2; At(c, e, x, y + (54 - d) / 2);
                    Label(c, v + "\u00A2", x + d / 2, y + 27, 13, "#0B0D12", true);
                    h = 54;
                }
                x += w + 12; rowH = Math.Max(rowH, h);
            }
            c.Height = y + rowH + 4;
            return c;
        }

        // ---------------------------------------------------------------- graphs
        static readonly string[] RowColors = { "#22C55E", "#FBBF24", "#60A5FA", "#F97316", "#A855F7", "#F472B6" };
        /// <summary>"pic" or "bar"; title; labels; values.</summary>
        public static Canvas Graph(string[] a)
        {
            bool bar = a[0] == "bar"; string title = a[1];
            string[] labels = a[2].Split(','); int[] vals = a[3].Split(',').Select(s => int.Parse(s, Inv)).ToArray();
            int n = labels.Length;
            if (!bar)
            {
                double rowH = 40, labelW = 120, cell = 30;
                Canvas c = New(labelW + 12 * cell + 20, 40 + n * rowH + 30);
                Label(c, title, c.Width / 2, 14, 16, TextC, true);
                for (int i = 0; i < n; i++)
                {
                    double y = 34 + i * rowH;
                    Box(c, 0, y, c.Width - 10, rowH - 4, "#13161D", 255, "#262B36", 1);
                    TextBlock t = T(labels[i], 14, TextC, true); At(c, t, 8, y + 9);
                    for (int k = 0; k < vals[i]; k++) Block3(c, labelW + k * cell, y + 5, 24, RowColors[i % RowColors.Length]);
                }
                TextBlock key = T("Key: each block = 1", 13, Muted, false); At(c, key, 8, 40 + n * rowH);
                return c;
            }
            int max = Math.Max(2, (vals.Max() + 1) / 2 * 2);
            double gw = 440, gh = 220, x0 = 46, y0 = 36;
            Canvas g = New(x0 + gw + 20, y0 + gh + 50);
            Label(g, title, g.Width / 2, 14, 16, TextC, true);
            for (int v = 0; v <= max; v++)
            {
                double y = y0 + gh - gh * v / max;
                Ln(g, x0, y, x0 + gw, y, v == 0 ? "#8C93A5" : "#262B36", v == 0 ? 2 : 1);
                if (v % (max > 12 ? 2 : 1) == 0) { TextBlock t = T(v.ToString(Inv), 12, Muted, false); Size s = Measure(t); At(g, t, x0 - 8 - s.Width, y - s.Height / 2); }
            }
            Ln(g, x0, y0, x0, y0 + gh, "#8C93A5", 2);
            double slot = gw / n;
            for (int i = 0; i < n; i++)
            {
                double h = gh * vals[i] / max, x = x0 + i * slot + slot * 0.2;
                Box(g, x, y0 + gh - h, slot * 0.6, h, RowColors[i % RowColors.Length], 230, "#0B0D12", 1.5);
                Label(g, labels[i], x + slot * 0.3, y0 + gh + 16, 13, TextC, true);
            }
            return g;
        }
        static void Block3(Canvas c, double x, double y, double s, string color)
        {
            Box(c, x, y, s, s, color, 255, "#0B0D12", 1.5);
            Box(c, x + 3, y + 3, s * 0.35, s * 0.2, "#FFFFFF", 70, null, 0);
        }

        // ---------------------------------------------------------------- shapes and equal parts
        public static Canvas Shape(string name)
        {
            Canvas c = New(240, 230);
            double cx = 120, cy = 112, r = 96;
            if (name == "cube")
            {
                double s = 110, d = 45, x = 40, y = 70;
                Poly(c, Blue, 120, Blue2, new Point(x, y), new Point(x + s, y), new Point(x + s, y + s), new Point(x, y + s));
                Poly(c, Blue2, 140, Blue2, new Point(x, y), new Point(x + d, y - d), new Point(x + s + d, y - d), new Point(x + s, y));
                Poly(c, "#1D4ED8", 160, Blue2, new Point(x + s, y), new Point(x + s + d, y - d), new Point(x + s + d, y + s - d), new Point(x + s, y + s));
                return c;
            }
            int sides = name == "triangle" ? 3 : name == "quadrilateral" || name == "square" ? 4 : name == "pentagon" ? 5 : 6;
            var pts = new List<Point>();
            if (name == "quadrilateral") { pts.Add(new Point(40, 60)); pts.Add(new Point(190, 40)); pts.Add(new Point(210, 190)); pts.Add(new Point(60, 170)); }
            else
            {
                double start = sides == 4 ? -Math.PI / 4 : -Math.PI / 2;
                for (int k = 0; k < sides; k++) { double ang = start + 2 * Math.PI * k / sides; pts.Add(new Point(cx + r * Math.Cos(ang), cy + r * Math.Sin(ang))); }
            }
            Poly(c, Orange, 110, "#FDBA74", pts.ToArray());
            foreach (Point p in pts) Dot(c, p.X, p.Y, 5, Yellow, null);
            return c;
        }
        /// <summary>A rectangle in rows x cols equal parts with the first `shaded` parts coloured.</summary>
        public static Canvas Parts(int rows, int cols, int shaded)
        {
            double cell = rows == 1 ? Math.Min(110, 360.0 / cols) : Math.Min(60, 320.0 / cols);
            double ch = rows == 1 ? 140 : cell;
            Canvas c = New(cols * cell + 10, rows * ch + 10);
            int k = 0;
            for (int r = 0; r < rows; r++)
                for (int q = 0; q < cols; q++, k++)
                    Box(c, 4 + q * cell, 4 + r * ch, cell, ch, k < shaded ? Yellow : "#1B2D50", k < shaded ? (byte)220 : (byte)255, "#93C5FD", 2.5);
            return c;
        }

        // ---------------------------------------------------------------- fractions
        static void ParseFrac(string s, out int n, out int d)
        {
            string[] f = s.Split('/'); n = int.Parse(f[0], Inv); d = f.Length > 1 ? int.Parse(f[1], Inv) : 1;
        }
        /// <summary>Bars for fractions, one under the other, the same length for one whole.</summary>
        public static Canvas FracBars(string[] fr)
        {
            double W = 480, h = 44, labelW = 70;
            Canvas c = New(labelW + W + 10, fr.Length * (h + 16) + 6);
            for (int i = 0; i < fr.Length; i++)
            {
                int n, d; ParseFrac(fr[i], out n, out d);
                double y = i * (h + 16);
                Label(c, fr[i], labelW / 2, y + h / 2, 20, TextC, true);
                for (int k = 0; k < d; k++) Box(c, labelW + W * k / d, y, W / d, h, k < n ? (i == 0 ? Blue : Green) : "#13161D", k < n ? (byte)220 : (byte)255, "#E6E9F0", 1.5);
            }
            return c;
        }
        /// <summary>How many c/d fit in a/b: the amount a/b shaded on whole bars cut into d pieces, with a bracket for every c/d.</summary>
        public static Canvas FracDiv(string what, string by)
        {
            int a, b, cc, d; ParseFrac(what, out a, out b); ParseFrac(by, out cc, out d);
            int wholes = Math.Max(1, (int)Math.Ceiling((double)a / b - 1e-9));
            double unit = Math.Min(170, 600.0 / wholes), h = 46, x0 = 10, y0 = 40;
            Canvas c = New(x0 + wholes * unit + 20, y0 + h + 70);
            double amount = (double)a / b, piece = (double)cc / d;
            // the amount, shaded
            Box(c, x0, y0, unit * amount, h, Blue, 200, null, 0);
            for (int w = 0; w < wholes; w++)
            {
                Box(c, x0 + w * unit, y0, unit, h, null, 0, "#E6E9F0", 2.5);
                for (int k = 1; k < d; k++) Ln(c, x0 + w * unit + unit * k / d, y0, x0 + w * unit + unit * k / d, y0 + h, "#93C5FD", 1);
                Label(c, w == 0 ? "1 whole" : (w + 1) + " wholes", x0 + w * unit + unit / 2, y0 - 14, 12, Muted, false);
            }
            // a bracket for each group of c/d
            int groups = (int)Math.Floor(amount / piece + 1e-9);
            for (int g = 0; g < groups; g++)
            {
                double xa = x0 + unit * g * piece, xb = x0 + unit * (g + 1) * piece;
                Ln(c, xa + 2, y0 + h + 10, xb - 2, y0 + h + 10, Yellow, 3);
                Ln(c, xa + 2, y0 + h + 4, xa + 2, y0 + h + 12, Yellow, 2); Ln(c, xb - 2, y0 + h + 4, xb - 2, y0 + h + 12, Yellow, 2);
                Label(c, (g + 1).ToString(Inv), (xa + xb) / 2, y0 + h + 26, 14, Yellow, true);
            }
            TextBlock t = T("Shaded: " + what.Replace("/1", "") + ".  Each bracket is " + by + ".", 13, Muted, false); At(c, t, x0, y0 + h + 42);
            return c;
        }

        // ---------------------------------------------------------------- percents and ratios
        /// <summary>A tape for 100% cut into equal parts, with pct% shaded and the part's value written over it.</summary>
        public static Canvas Tape(int pct, string part)
        {
            int parts = 100 % pct == 0 && 100 / pct <= 20 ? 100 / pct : 10;
            double W = 560, h = 50, x0 = 20, y0 = 44;
            Canvas c = New(W + 60, 160);
            double shadedW = W * pct / 100.0;
            Box(c, x0, y0, shadedW, h, Blue, 200, null, 0);
            for (int k = 0; k < parts; k++) Box(c, x0 + W * k / parts, y0, W / parts, h, null, 0, "#E6E9F0", 1.5);
            Ln(c, x0, y0 - 10, x0 + shadedW, y0 - 10, Yellow, 2.5);
            Label(c, pct + "% is " + part, x0 + shadedW / 2, y0 - 26, 15, Yellow, true);
            Label(c, "100% = ?", x0 + W - 34, y0 - 26, 15, Green, true);
            for (int k = 0; k <= parts; k++)
            {
                double x = x0 + W * k / parts;
                Ln(c, x, y0 + h, x, y0 + h + 8, Muted, 1.5);
                if (parts <= 10 || k % 2 == 0) Label(c, Answers.Num(100.0 * k / parts) + "%", x, y0 + h + 20, 12, Muted, false);
            }
            return c;
        }
        /// <summary>Two number lines that share their ticks: top label; bottom label; top values; bottom values ("?" for the unknown).</summary>
        public static Canvas DoubleLine(string[] a)
        {
            string topName = a[0], botName = a[1];
            string[] tv = a[2].Split(','), bv = a[3].Split(',');
            int n = Math.Min(tv.Length, bv.Length);
            // positions: the bottom numbers, or the top numbers times the ratio between the rows
            double ratio = 0;
            for (int i = 0; i < n; i++) if (IsNum(tv[i]) && IsNum(bv[i]) && D(tv[i]) != 0) { ratio = D(bv[i]) / D(tv[i]); break; }
            double[] pos = new double[n];
            for (int i = 0; i < n; i++) pos[i] = IsNum(bv[i]) ? D(bv[i]) : IsNum(tv[i]) ? D(tv[i]) * ratio : i;
            double maxP = Math.Max(1e-9, pos.Max());
            double x0 = 130, W = 460, yT = 40, yB = 120;
            Canvas c = New(x0 + W + 40, 160);
            TextBlock t1 = T(topName, 14, Blue2, true); At(c, t1, 0, yT - 10);
            TextBlock t2 = T(botName, 14, Green, true); At(c, t2, 0, yB - 10);
            Ln(c, x0 - 10, yT, x0 + W + 20, yT, TextC, 2.5); Ln(c, x0 - 10, yB, x0 + W + 20, yB, TextC, 2.5);
            for (int i = 0; i < n; i++)
            {
                double x = x0 + W * pos[i] / maxP;
                Ln(c, x, yT - 8, x, yT + 8, TextC, 2); Ln(c, x, yB - 8, x, yB + 8, TextC, 2);
                Dash(c, x, yT + 10, x, yB - 10, "#353C4B");
                Label(c, tv[i], x, yT - 20, 15, tv[i] == "?" ? Yellow : Blue2, true);
                Label(c, bv[i], x, yB + 20, 15, bv[i] == "?" ? Yellow : Green, true);
            }
            return c;
        }
        /// <summary>A ratio table: two rows with their names; "?" cells in yellow.</summary>
        public static Canvas RatioTable(string[] a)
        {
            string[] tv = a[2].Split(','), bv = a[3].Split(',');
            double labelW = 170, cw = 76, rh = 46;
            Canvas c = New(labelW + tv.Length * cw + 6, 2 * rh + 6);
            for (int r = 0; r < 2; r++)
            {
                string[] row = r == 0 ? tv : bv;
                Box(c, 2, 2 + r * rh, labelW, rh, "#1B2D50", 255, "#93C5FD", 1.5);
                TextBlock t = T(a[r], 15, TextC, true); At(c, t, 12, 2 + r * rh + 12);
                for (int i = 0; i < row.Length; i++)
                {
                    bool q = row[i] == "?";
                    Box(c, 2 + labelW + i * cw, 2 + r * rh, cw, rh, q ? "#33280E" : "#13161D", 255, "#93C5FD", 1.5);
                    Label(c, row[i], 2 + labelW + i * cw + cw / 2, 2 + r * rh + rh / 2, 19, q ? Yellow : TextC, true);
                }
            }
            return c;
        }

        // ---------------------------------------------------------------- number system
        /// <summary>The coordinate plane from -9 to 9 with points A (and B): "x,y[;x2,y2]".</summary>
        public static Canvas Grid(string arg)
        {
            const int M = 9; double cell = 20, size = 2 * M * cell;
            Canvas c = New(size + 40, size + 30);
            double ox = 20 + M * cell, oy = 10 + M * cell;
            for (int k = -M; k <= M; k++)
            {
                Ln(c, ox + k * cell, oy - M * cell, ox + k * cell, oy + M * cell, k == 0 ? TextC : "#262B36", k == 0 ? 2.5 : 1);
                Ln(c, ox - M * cell, oy + k * cell, ox + M * cell, oy + k * cell, k == 0 ? TextC : "#262B36", k == 0 ? 2.5 : 1);
                if (k != 0 && k % 2 == 0)
                {
                    Label(c, k.ToString(Inv), ox + k * cell, oy + 11, 10, Muted, false);
                    Label(c, k.ToString(Inv), ox - 11, oy - k * cell, 10, Muted, false);
                }
            }
            Label(c, "x", ox + M * cell + 10, oy, 13, Muted, true); Label(c, "y", ox, oy - M * cell - 2, 13, Muted, true);
            Label(c, "I", ox + M * cell * 0.62, oy - M * cell * 0.8, 15, "#353C4B", true); Label(c, "II", ox - M * cell * 0.62, oy - M * cell * 0.8, 15, "#353C4B", true);
            Label(c, "III", ox - M * cell * 0.62, oy + M * cell * 0.8, 15, "#353C4B", true); Label(c, "IV", ox + M * cell * 0.62, oy + M * cell * 0.8, 15, "#353C4B", true);
            string[] pts = arg.Split(';');
            for (int i = 0; i < pts.Length; i++)
            {
                string[] xy = pts[i].Split(','); int x = int.Parse(xy[0], Inv), y = int.Parse(xy[1], Inv);
                Dot(c, ox + x * cell, oy - y * cell, 7, i == 0 ? Yellow : Green, "#0B0D12");
                Label(c, i == 0 ? "A" : "B", ox + x * cell + 13, oy - y * cell - 12, 15, i == 0 ? Yellow : Green, true);
            }
            return c;
        }
        /// <summary>An inequality on a number line: open or filled circle at k, arrow left or right.</summary>
        public static Canvas Ineq(int k, bool closed, bool right)
        {
            double x0 = 20, W = 520, y = 60; int lo = k - 6, hi = k + 6;
            Canvas c = New(W + 40, 110);
            Ln(c, x0, y, x0 + W, y, TextC, 2.5);
            for (int v = lo; v <= hi; v++) { double x = x0 + W * (v - lo) / (hi - lo); Ln(c, x, y - 7, x, y + 7, TextC, 1.5); Label(c, v.ToString(Inv), x, y + 20, 12, Muted, false); }
            double xk = x0 + W * (k - lo) / (hi - lo), xe = right ? x0 + W + 8 : x0 - 8;
            Ln(c, xk, y, xe, y, Yellow, 6);
            Poly(c, Yellow, 255, null, right ? new[] { new Point(xe + 10, y), new Point(xe - 4, y - 9), new Point(xe - 4, y + 9) } : new[] { new Point(xe - 10, y), new Point(xe + 4, y - 9), new Point(xe + 4, y + 9) });
            Dot(c, xk, y, 9, closed ? Yellow : "#0B0D12", Yellow);
            return c;
        }

        // ---------------------------------------------------------------- geometry
        static double Scale(double w, double h, double maxW, double maxH) { return Math.Min(maxW / w, maxH / h); }
        public static Canvas LShape(int w1, int h1, int w2, int h2)
        {
            double s = Scale(w1 + w2, h1, 360, 220), x0 = 50, y0 = 20;
            Canvas c = New(x0 + (w1 + w2) * s + 60, y0 + h1 * s + 50);
            double bx = y0 + h1 * s;
            Poly(c, Blue, 70, Blue2, new Point(x0, y0), new Point(x0 + w1 * s, y0), new Point(x0 + w1 * s, bx - h2 * s), new Point(x0 + (w1 + w2) * s, bx - h2 * s), new Point(x0 + (w1 + w2) * s, bx), new Point(x0, bx));
            Dash(c, x0 + w1 * s, bx - h2 * s, x0 + w1 * s, bx, Yellow);
            Label(c, w1.ToString(Inv), x0 + w1 * s / 2, bx + 14, 15, Blue2, true);
            Label(c, w2.ToString(Inv), x0 + w1 * s + w2 * s / 2, bx + 14, 15, Blue2, true);
            Label(c, h1.ToString(Inv), x0 - 16, y0 + h1 * s / 2, 15, Yellow, true);
            Label(c, h2.ToString(Inv), x0 + (w1 + w2) * s + 16, bx - h2 * s / 2, 15, Yellow, true);
            Label(c, w1.ToString(Inv), x0 + w1 * s / 2, y0 - 2 + 12, 13, Muted, false);
            return c;
        }
        public static Canvas House(int w, int h, int rh)
        {
            double s = Scale(w, h + rh, 300, 230), x0 = 60, y0 = 16;
            Canvas c = New(x0 + w * s + 90, y0 + (h + rh) * s + 40);
            double top = y0 + rh * s, bot = top + h * s;
            Poly(c, Orange, 80, "#FDBA74", new Point(x0, top), new Point(x0 + w * s / 2, y0), new Point(x0 + w * s, top));
            Poly(c, Blue, 70, Blue2, new Point(x0, top), new Point(x0 + w * s, top), new Point(x0 + w * s, bot), new Point(x0, bot));
            Dash(c, x0 + w * s / 2, y0, x0 + w * s / 2, top, Yellow);
            Label(c, w.ToString(Inv), x0 + w * s / 2, bot + 14, 15, Blue2, true);
            Label(c, h.ToString(Inv), x0 + w * s + 18, top + h * s / 2, 15, Blue2, true);
            Label(c, rh.ToString(Inv), x0 + w * s / 2 + 16, y0 + rh * s / 2 + 4, 15, Yellow, true);
            return c;
        }
        public static Canvas Trap(int b1, int b2, int h)
        {
            double s = Scale(b2, h, 380, 200), x0 = 30, y0 = 30;
            Canvas c = New(x0 + b2 * s + 40, y0 + h * s + 40);
            double off = (b2 - b1) * s / 2;
            Poly(c, Green, 70, Green, new Point(x0 + off, y0), new Point(x0 + off + b1 * s, y0), new Point(x0 + b2 * s, y0 + h * s), new Point(x0, y0 + h * s));
            Dash(c, x0 + off, y0, x0 + off, y0 + h * s, Yellow); Dash(c, x0 + off + b1 * s, y0, x0 + off + b1 * s, y0 + h * s, Yellow);
            Label(c, b1.ToString(Inv), x0 + b2 * s / 2, y0 - 14, 15, Green, true);
            Label(c, b2.ToString(Inv), x0 + b2 * s / 2, y0 + h * s + 14, 15, Green, true);
            Label(c, h.ToString(Inv), x0 + off - 14, y0 + h * s / 2, 15, Yellow, true);
            return c;
        }
        public static Canvas Hole(int w, int h, int sq)
        {
            double s = Scale(w, h, 360, 220), x0 = 30, y0 = 10;
            Canvas c = New(x0 + w * s + 40, y0 + h * s + 36);
            Box(c, x0, y0, w * s, h * s, "#A16207", 160, "#FDBA74", 2.5);
            Box(c, x0 + (w - sq) * s / 2, y0 + (h - sq) * s / 2, sq * s, sq * s, Blue2, 230, "#BFDBFE", 2);
            Label(c, w.ToString(Inv), x0 + w * s / 2, y0 + h * s + 14, 15, "#FDBA74", true);
            Label(c, h.ToString(Inv), x0 + w * s + 16, y0 + h * s / 2, 15, "#FDBA74", true);
            Label(c, sq.ToString(Inv), x0 + w * s / 2, y0 + (h + sq) * s / 2 + 12, 14, "#BFDBFE", true);
            return c;
        }
        /// <summary>Nets: "l,w,h[,open]" (a box), "prism", "pyramid,b,slant".</summary>
        public static Canvas Net(string[] a)
        {
            if (a[0] == "prism")
            {
                Canvas p = New(400, 260); double s = 70, x0 = 60, y0 = 70;
                for (int k = 0; k < 3; k++) Box(p, x0 + k * s * 1.3, y0, s * 1.3, s * 1.6, Blue, 80, Blue2, 2);
                double mx = x0 + 1.3 * s, mw = 1.3 * s;
                Poly(p, Green, 90, Green, new Point(mx, y0), new Point(mx + mw, y0), new Point(mx + mw / 2, y0 - mw * 0.85));
                Poly(p, Green, 90, Green, new Point(mx, y0 + 1.6 * s), new Point(mx + mw, y0 + 1.6 * s), new Point(mx + mw / 2, y0 + 1.6 * s + mw * 0.85));
                return p;
            }
            if (a[0] == "pyramid")
            {
                int b = a.Length > 1 ? int.Parse(a[1], Inv) : 4, sl = a.Length > 2 ? int.Parse(a[2], Inv) : 5;
                double u = Math.Min(260.0 / (b + 2.0 * sl), 30);
                double bs = b * u, ts = sl * u, cx = 20 + ts + bs / 2, cy = 10 + ts + bs / 2;
                Canvas p = New(2 * ts + bs + 60, 2 * ts + bs + 30);
                Box(p, cx - bs / 2, cy - bs / 2, bs, bs, Blue, 90, Blue2, 2);
                Poly(p, Orange, 80, "#FDBA74", new Point(cx - bs / 2, cy - bs / 2), new Point(cx + bs / 2, cy - bs / 2), new Point(cx, cy - bs / 2 - ts));
                Poly(p, Orange, 80, "#FDBA74", new Point(cx - bs / 2, cy + bs / 2), new Point(cx + bs / 2, cy + bs / 2), new Point(cx, cy + bs / 2 + ts));
                Poly(p, Orange, 80, "#FDBA74", new Point(cx - bs / 2, cy - bs / 2), new Point(cx - bs / 2, cy + bs / 2), new Point(cx - bs / 2 - ts, cy));
                Poly(p, Orange, 80, "#FDBA74", new Point(cx + bs / 2, cy - bs / 2), new Point(cx + bs / 2, cy + bs / 2), new Point(cx + bs / 2 + ts, cy));
                Label(p, b + " by " + b, cx, cy, 14, TextC, true);
                Dash(p, cx, cy - bs / 2, cx, cy - bs / 2 - ts, Yellow); Label(p, sl.ToString(Inv), cx + 12, cy - bs / 2 - ts / 2, 14, Yellow, true);
                return p;
            }
            int l = int.Parse(a[0], Inv), w = int.Parse(a[1], Inv), h = int.Parse(a[2], Inv);
            bool open = a.Length > 3 && a[3] == "open";
            double sc = Scale(w + l + w, w + h + w + h, 420, 330);
            double X = 10 + w * sc, Y = 4;
            Canvas c = New((w + l + w) * sc + 30, (w + h + w + h) * sc + 10);
            double yTop = Y, yFront = Y + w * sc, yBottom = yFront + h * sc, yBack = yBottom + w * sc;
            if (!open) NetFace(c, X, yTop, l * sc, w * sc, l + " \u00D7 " + w, "#1D4ED8");
            else { Rectangle r = Box(c, X, yTop, l * sc, w * sc, null, 0, Muted, 1.5); r.StrokeDashArray = new AvaloniaList<double> { 4, 3 }; Label(c, "no lid", X + l * sc / 2, yTop + w * sc / 2, 13, Muted, false); }
            NetFace(c, X, yFront, l * sc, h * sc, l + " \u00D7 " + h, Blue);
            NetFace(c, X - w * sc, yFront, w * sc, h * sc, w + " \u00D7 " + h, Green);
            NetFace(c, X + l * sc, yFront, w * sc, h * sc, w + " \u00D7 " + h, Green);
            NetFace(c, X, yBottom, l * sc, w * sc, l + " \u00D7 " + w, "#1D4ED8");
            NetFace(c, X, yBack, l * sc, h * sc, l + " \u00D7 " + h, Blue);
            return c;
        }
        static void NetFace(Canvas c, double x, double y, double w, double h, string label, string color)
        {
            Box(c, x, y, w, h, color, 110, "#BFDBFE", 2);
            if (w > 34 && h > 18) Label(c, label, x + w / 2, y + h / 2, Math.Min(14, h * 0.6), TextC, true);
        }

        // ---------------------------------------------------------------- statistics
        public static Canvas DotPlot(string title, int[] data)
        {
            int lo = data.Min() - 1, hi = data.Max() + 1;
            double W = 520, x0 = 30, r = 9;
            int tallest = data.GroupBy(v => v).Max(g => g.Count());
            double y = 40 + tallest * (2 * r + 3) + 6;
            Canvas c = New(W + 70, y + 40);
            Label(c, title, (W + 60) / 2, 12, 15, TextC, true);
            Ln(c, x0, y, x0 + W, y, TextC, 2.5);
            for (int v = lo; v <= hi; v++)
            {
                double x = x0 + W * (v - lo) / (hi - lo);
                Ln(c, x, y - 6, x, y + 6, TextC, 1.5); Label(c, v.ToString(Inv), x, y + 18, 13, Muted, false);
                int n = data.Count(d => d == v);
                for (int k = 0; k < n; k++) Dot(c, x, y - 10 - r - k * (2 * r + 3), r, Blue2, "#0B0D12");
            }
            return c;
        }
        public static Canvas BoxPlot(string title, double[] f)
        {
            int lo = (int)Math.Floor(f[0]) - 1, hi = (int)Math.Ceiling(f[4]) + 1;
            double W = 520, x0 = 30, y = 110;
            Canvas c = New(W + 70, 160);
            Label(c, title, (W + 60) / 2, 12, 15, TextC, true);
            Func<double, double> X = v => x0 + W * (v - lo) / (hi - lo);
            Ln(c, x0, y, x0 + W, y, TextC, 2.5);
            for (int v = lo; v <= hi; v++) { Ln(c, X(v), y - 6, X(v), y + 6, TextC, 1.5); Label(c, v.ToString(Inv), X(v), y + 18, 13, Muted, false); }
            double by = 46, bh = 40;
            Ln(c, X(f[0]), by + bh / 2, X(f[1]), by + bh / 2, Blue2, 2.5); Ln(c, X(f[3]), by + bh / 2, X(f[4]), by + bh / 2, Blue2, 2.5);
            Ln(c, X(f[0]), by + 8, X(f[0]), by + bh - 8, Blue2, 2.5); Ln(c, X(f[4]), by + 8, X(f[4]), by + bh - 8, Blue2, 2.5);
            Box(c, X(f[1]), by, X(f[3]) - X(f[1]), bh, Blue, 90, Blue2, 2.5);
            Ln(c, X(f[2]), by, X(f[2]), by + bh, Yellow, 3.5);
            return c;
        }

        // ---------------------------------------------------------------- story pictures (our own pixel sprites)
        static readonly Dictionary<string, string[]> Sprites = new Dictionary<string, string[]> {
            { "pickaxe", new[] { ".SSSSSS.", "S.....SS", ".....NS.", "....N...", "...N....", "..N.....", ".N......", "N......." } },
            { "gem", new[] { "..CCCC..", ".CDCCDC.", "CDCCCCDC", "CCCCCCCC", ".CCCCCC.", "..CCCC..", "...CC...", "........" } },
            { "gloomling", new[] { "..PPPP..", ".PPPPPP.", "PPYPPYPP", "PPYPPYPP", "PPPPPPPP", "PP.PP.PP", "P..PP..P", "........" } },
            { "blob", new[] { "........", "..GGGG..", ".GGGGGG.", "GGKGGKGG", "GGGGGGGG", "GGGKKGGG", ".GGGGGG.", "........" } },
            { "clatterbones", new[] { "..WWWW..", ".WKWWKW.", ".WWWWWW.", "..WKKW..", "...WW...", ".WWWWWW.", "...WW...", "..W..W.." } },
            { "torch", new[] { "...OO...", "..OYYO..", "..OYYO..", "...OO...", "...NN...", "...NN...", "...NN...", "...NN..." } },
            { "carrot", new[] { "...GG...", "..GGG...", "...OO...", "..OOO...", "..OOO...", "...OO...", "...O....", "........" } },
            { "lantern", new[] { "...KK...", "..K..K..", ".KKKKKK.", ".KYYYYK.", ".KYOOYK.", ".KYYYYK.", ".KKKKKK.", "........" } },
            { "beaker", new[] { ".W....W.", ".W....W.", ".W....W.", ".W....W.", "W.BBBB.W", "WBBBBBBW", "WBBBBBBW", ".WWWWWW." } },
            { "jar", new[] { ".KKKKKK.", "W......W", "W..G...W", "W.GGG..W", "W..N.G.W", "WNNNNNNW", "WSSSSSSW", ".WWWWWW." } },
            { "rover", new[] { "....K...", "...KK...", ".YYYYYY.", "YYYYYYYY", "YKYYYYKY", "Y.KKKK.Y", ".KK..KK.", ".KK..KK." } },
        };
        static readonly Dictionary<char, string> SpriteColors = new Dictionary<char, string> {
            { 'S', "#9CA3AF" }, { 'W', "#E5E7EB" }, { 'C', "#34D399" }, { 'D', "#A7F3D0" }, { 'P', "#7C3AED" }, { 'Y', "#FDE047" }, { 'G', "#22C55E" },
            { 'K', "#1F2937" }, { 'O', "#F97316" }, { 'N', "#8B5A2B" }, { 'B', "#60A5FA" } };
        static readonly Dictionary<string, string> BlockColor = new Dictionary<string, string> {
            { "stone", "#8A8F98" }, { "dirt", "#7B4B25" }, { "sand", "#E3C77A" }, { "wood", "#C8955A" }, { "glass", "#A5D8FF" }, { "brick", "#B5452F" }, { "snow", "#F1F5F9" }, { "clay", "#B97A57" } };
        /// <summary>A row of story pictures: "pickaxe,block-stone,gloomling".</summary>
        public static Canvas Scene(string arg)
        {
            string[] items = arg.Split(',');
            double px = 11, size = 8 * px, gap = 26;
            Canvas c = New(items.Length * (size + gap), size + 26);
            double x = 0;
            foreach (string it in items)
            {
                if (it.StartsWith("block-"))
                {
                    string kind = it.Substring(6); string col; if (!BlockColor.TryGetValue(kind, out col)) col = "#8A8F98";
                    Box(c, x, 0, size, size, col, 255, "#0B0D12", 2);
                    for (int k = 0; k < 6; k++) Box(c, x + 8 + (k * 37 % 64), 8 + (k * 23 % 60), 10, 8, "#000000", 40, null, 0);
                    Box(c, x + 6, 6, size * 0.4, size * 0.12, "#FFFFFF", 60, null, 0);
                    Label(c, kind, x + size / 2, size + 12, 13, Muted, false);
                }
                else
                {
                    string[] rows; if (!Sprites.TryGetValue(it, out rows)) continue;
                    for (int y = 0; y < 8; y++)
                        for (int k = 0; k < 8; k++)
                        {
                            char ch = rows[y][k]; if (ch == '.') continue;
                            Box(c, x + k * px, y * px, px, px, SpriteColors[ch], 255, null, 0);
                        }
                    Label(c, it == "jar" ? "terrarium" : it, x + size / 2, size + 12, 13, Muted, false);
                }
                x += size + gap;
            }
            return c;
        }

        /// <summary>A division written the long way, ready to work on paper: divisor, bracket, dividend.</summary>
        public static Canvas Bracket(string dividend, string divisor)
        {
            Canvas c = New(70 + 34 * (dividend.Length + divisor.Length) + 40, 130);
            TextBlock dv = T(divisor, 40, Blue2, true); dv.FontFamily = Mono; Size ds = Measure(dv); At(c, dv, 20, 50);
            double x0 = 20 + ds.Width + 14;
            TextBlock dd = T(dividend, 40, TextC, true); dd.FontFamily = Mono; Size s = Measure(dd); At(c, dd, x0 + 6, 50);
            Path p = new Path(); p.Stroke = B(TextC); p.StrokeThickness = 3;
            p.Data = Geometry.Parse(string.Format(Inv, "M {0},{1} Q {2},{3} {0},{4} M {0},{1} L {5},{1}", x0, 46, x0 + 12, 46 + s.Height / 2, 50 + s.Height, x0 + s.Width + 20));
            c.Children.Add(p);
            Label(c, "?", x0 + 6 + s.Width / 2, 24, 26, Yellow, true);
            return c;
        }

        // ---------------------------------------------------------------- science
        /// <summary>A periodic-table style tile: atomic number, symbol, name, and the isotope's mass number if given.</summary>
        public static Canvas Element(int z, string sym, string name, string mass)
        {
            Canvas c = New(190, 190);
            Border b = new Border(); b.Width = 170; b.Height = 170; b.CornerRadius = new CornerRadius(12);
            b.Background = B(z > 88 ? "#3B2667" : z > 20 ? "#1E3A5F" : "#14432F"); b.BorderBrush = B(z > 88 ? "#C4B5FD" : z > 20 ? "#93C5FD" : "#86EFAC"); b.BorderThickness = new Thickness(3);
            At(c, b, 10, 10);
            TextBlock zt = T(z.ToString(Inv), 20, TextC, true); At(c, zt, 26, 22);
            if (mass.Length > 0) { TextBlock mt = T(mass, 16, Yellow, true); Size ms = Measure(mt); At(c, mt, 164 - ms.Width, 24); }
            Label(c, sym, 95, 92, 58, "#FFFFFF", true);
            Label(c, name.Substring(0, 1).ToUpperInvariant() + name.Substring(1), 95, 148, 17, TextC, false);
            return c;
        }
    }
}
