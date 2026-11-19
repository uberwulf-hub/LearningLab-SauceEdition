// Cyber Range (Learning Lab) - the look: App Hub's RangeStyle.cs drawing kit for Avalonia, touch first. Cards are
// buttons (a tap opens them, no hover-only actions), switches are 44 px high, rings, bars, chips, medals and rank
// insignia are drawn in code from RangeLook's data (the engine: colours, icons as 24 x 24 path data, plain words), so a
// category added later looks right without code here. Animations run only when RangeFx.On (never in the checks).
using System.Globalization;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Path = Avalonia.Controls.Shapes.Path;
using Avalonia.Layout;
using Avalonia.Media;
using CertLab;
using LearningLab.Views;

namespace LearningLab.CyberRange;

/// <summary>Animations only on a real screen: off in the checks and pictures, so they are exact and nothing keeps running.</summary>
public static class RangeFx
{
    public static bool On;
    /// <summary>Fades a control in (the glossary card, a capture's celebration).</summary>
    public static void FadeIn(Control c, int ms = 160)
    {
        if (!On) { c.Opacity = 1; return; }
        c.Opacity = 0;
        c.Transitions = new Avalonia.Animation.Transitions { new Avalonia.Animation.DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromMilliseconds(ms) } };
        Avalonia.Threading.Dispatcher.UIThread.Post(() => c.Opacity = 1, Avalonia.Threading.DispatcherPriority.Background);
    }
}

public static class Rg
{
    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Menlo, DejaVu Sans Mono, monospace");
    public const string Card = "#13161D", CardTop = "#171A21";

    public static Color C(string hex) => Color.TryParse(hex, out var c) ? c : Colors.SteelBlue;
    public static IBrush B(string hex) => Ui.Brush(hex);
    /// <summary>A colour at an alpha (0..1): the soft backgrounds of chips and tiles.</summary>
    public static IBrush Soft(string hex, double alpha) => Ui.Brush(hex, (byte)Math.Round(Math.Max(0, Math.Min(1, alpha)) * 255));
    public static Color Mix(Color a, Color b, double t) => Color.FromArgb((byte)(a.A + (b.A - a.A) * t), (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    public static string Hex(Color c) => "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");

    /// <summary>A two-colour gradient at an angle in degrees (0 = left to right, 90 = top to bottom, as WPF's).</summary>
    public static IBrush Gradient(string a, string b, double angle) => Gradient(C(a), C(b), angle);
    public static IBrush Gradient(Color a, Color b, double angle)
    {
        double r = angle * Math.PI / 180, dx = Math.Cos(r) / 2, dy = Math.Sin(r) / 2;
        return new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.5 - dx, 0.5 - dy, RelativeUnit.Relative), EndPoint = new RelativePoint(0.5 + dx, 0.5 + dy, RelativeUnit.Relative),
            GradientStops = { new GradientStop(a, 0), new GradientStop(b, 1) },
        };
    }
    /// <summary>A soft round glow of a colour from a point of the area (relative 0..1), fading out.</summary>
    public static IBrush Glow(string hex, byte alpha, double cx, double cy, double radius)
    {
        var c = C(hex);
        return new RadialGradientBrush
        {
            Center = new RelativePoint(cx, cy, RelativeUnit.Relative), GradientOrigin = new RelativePoint(cx, cy, RelativeUnit.Relative), RadiusX = new RelativeScalar(radius, RelativeUnit.Relative), RadiusY = new RelativeScalar(radius, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromArgb(alpha, c.R, c.G, c.B), 0), new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1) },
        };
    }
    /// <summary>The fill of a card: the surface with a faint lift towards the top.</summary>
    public static IBrush CardFill() => Gradient(CardTop, Card, 90);

    // ------------------------------------------------------------ icons (RangeLook.IconPaths, 24 x 24 strokes)
    public static Control Icon(string key, double size, string colour, double weight = 2)
    {
        string data = RangeLook.HasIcon(key) ? RangeLook.IconPaths[key] : RangeLook.IconPaths["hex"];
        var p = new Path { Data = Geometry.Parse(data), Stroke = B(colour), StrokeThickness = weight, StrokeJoin = PenLineJoin.Round, StrokeLineCap = PenLineCap.Round };
        var cv = new Canvas { Width = 24, Height = 24, Children = { p } };
        return new Viewbox { Child = cv, Width = size, Height = size, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center };
    }

    /// <summary>A rounded tile with a soft accent glow and the icon in the middle (category tiles).</summary>
    public static Border IconTile(string key, string accent, string accent2, double size)
    {
        var a = C(accent); var b = C(accent2);
        var ic = Icon(key, size * 0.5, accent, 1.9);
        ic.HorizontalAlignment = HorizontalAlignment.Center;
        return new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.28),
            Background = Gradient(Color.FromArgb(70, a.R, a.G, a.B), Color.FromArgb(28, b.R, b.G, b.B), 45),
            BorderBrush = Soft(accent, 0.45), BorderThickness = new Thickness(1), Child = ic, VerticalAlignment = VerticalAlignment.Top,
        };
    }

    // ------------------------------------------------------------ text
    public static TextBlock Eyebrow(string t, string colour = Ui.Muted) => new() { Text = (t ?? "").ToUpperInvariant(), FontSize = 11.5, FontWeight = FontWeight.SemiBold, Foreground = B(colour), Margin = new Thickness(0, 0, 0, 4), TextWrapping = TextWrapping.Wrap, LetterSpacing = 0.6 };
    public static TextBlock Title(string t, double size) => new() { Text = t, FontSize = size, FontWeight = FontWeight.SemiBold, Foreground = B(Ui.TextC), TextWrapping = TextWrapping.Wrap };
    public static TextBlock Body(string t, double size = 14, string colour = Ui.Muted) => new() { Text = t, FontSize = size, Foreground = B(colour), TextWrapping = TextWrapping.Wrap, LineHeight = Math.Round(size * 1.5) };
    public static SelectableTextBlock Sel(string t, double size = 14, string colour = Ui.TextC) => new() { Text = t, FontSize = size, Foreground = B(colour), TextWrapping = TextWrapping.Wrap, LineHeight = Math.Round(size * 1.5), SelectionBrush = B("#2F5A8A") };

    /// <summary>A small rounded label.</summary>
    public static Border Chip(string text, string fg, double alphaBg = 0.14)
    {
        return new Border
        {
            Background = Soft(fg, alphaBg), CornerRadius = new CornerRadius(20), Padding = new Thickness(9, 3, 9, 4), Margin = new Thickness(0, 0, 6, 5), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = text, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = B(fg) },
        };
    }
    /// <summary>A neutral label with an optional icon ("~20 min", "250 pts").</summary>
    public static Border Tag(string icon, string text)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        if (icon != null) { var i = Icon(icon, 12, Ui.Muted, 2.2); i.Margin = new Thickness(0, 0, 5, 0); sp.Children.Add(i); }
        sp.Children.Add(new TextBlock { Text = text, FontSize = 12, Foreground = B(Ui.Muted), VerticalAlignment = VerticalAlignment.Center });
        return new Border { Background = Soft("#FFFFFF", 0.045), CornerRadius = new CornerRadius(20), Padding = new Thickness(8, 3, 9, 4), Margin = new Thickness(0, 0, 6, 5), Child = sp, VerticalAlignment = VerticalAlignment.Center };
    }
    public static Border LevelChip(string level) => Chip(level, RangeLook.Level(level).Color, 0.14);

    // ------------------------------------------------------------ bars and rings
    /// <summary>A thin progress bar (0..1).</summary>
    public static Control Bar(double value, IBrush fill, double height)
    {
        value = Math.Max(0, Math.Min(1, double.IsNaN(value) ? 0 : value));
        var g = new Grid { Height = height, ColumnDefinitions = new ColumnDefinitions(Ui.F(Math.Max(value, 0.0001), "0.0000") + "*," + Ui.F(Math.Max(1 - value, 0.0001), "0.0000") + "*") };
        var track = new Border { Background = Soft("#FFFFFF", 0.07), CornerRadius = new CornerRadius(height / 2) };
        Grid.SetColumnSpan(track, 2);
        g.Children.Add(track);
        if (value > 0) g.Children.Add(new Border { Background = fill, CornerRadius = new CornerRadius(height / 2) });
        return g;
    }
    public static Control Bar(double value, string colour, double height) => Bar(value, B(colour), height);

    /// <summary>One segment per objective: the filled ones are captured.</summary>
    public static Control Segments(int done, int total, string colour, double height)
    {
        total = Math.Max(1, total);
        var g = new Grid { Height = height };
        for (int i = 0; i < total; i++)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            var b = new Border { CornerRadius = new CornerRadius(height / 2), Margin = new Thickness(i == 0 ? 0 : 2, 0, i == total - 1 ? 0 : 2, 0), Background = i < done ? B(colour) : Soft("#FFFFFF", 0.08) };
            Grid.SetColumn(b, i);
            g.Children.Add(b);
        }
        return g;
    }

    public static RangeRing Ring(double value, string colour, double size, string center, string under)
        => new() { Value = value, Fill = colour, Width = size, Height = size, Stroke = Math.Max(4, size / 11), Center = center, Under = under, CenterSize = size / 4.2, VerticalAlignment = VerticalAlignment.Top };

    // ------------------------------------------------------------ panels
    /// <summary>A plain (not tappable) panel.</summary>
    public static Border Panel(Control child, double pad = 16)
        => new() { Child = child, Background = CardFill(), BorderBrush = B(Ui.BorderC), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(pad), Margin = new Thickness(0, 0, 0, 14) };
    /// <summary>A panel with an accent stripe down its left edge.</summary>
    public static Border Callout(Control child, string accent)
        => new() { Child = new Border { Child = child, Padding = new Thickness(14, 11, 14, 12) }, Background = Soft(accent, 0.07), BorderBrush = B(accent), BorderThickness = new Thickness(3, 0, 0, 0), CornerRadius = new CornerRadius(4, 10, 10, 4), Margin = new Thickness(0, 0, 0, 14) };

    /// <summary>A heading inside a panel: an icon and a title.</summary>
    public static Control PaneHead(string icon, string title)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8), Spacing = 8 };
        sp.Children.Add(Icon(icon, 16, Ui.AccentText, 2.1));
        sp.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeight.SemiBold, Foreground = B(Ui.TextC), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        return sp;
    }
    /// <summary>A section heading: a small eyebrow, a title and a note at the right (under it on a phone).</summary>
    public static Control SectionHead(string eyebrow, string title, string right)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 10, 0, 10) };
        if (eyebrow != null) sp.Children.Add(Eyebrow(eyebrow));
        var row = new WrapPanel();
        var t = Title(title, 19); t.Margin = new Thickness(0, 0, 16, 0);
        row.Children.Add(t);
        if (right != null) { var r = Body(right, 13); r.VerticalAlignment = VerticalAlignment.Bottom; r.Margin = new Thickness(0, 4, 0, 2); row.Children.Add(r); }
        sp.Children.Add(row);
        return sp;
    }
    /// <summary>A banner with an icon, a title and a sentence (the briefing's "A defender is watching").</summary>
    public static Border Banner(string icon, string title, string text, string colour)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        var ic = Icon(icon, 20, colour); ic.VerticalAlignment = VerticalAlignment.Top; ic.Margin = new Thickness(0, 1, 12, 0);
        g.Children.Add(ic);
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = title, FontSize = 14.5, FontWeight = FontWeight.SemiBold, Foreground = B(colour), Margin = new Thickness(0, 0, 0, 2), TextWrapping = TextWrapping.Wrap });
        sp.Children.Add(Body(text, 13.5, Ui.TextC));
        Grid.SetColumn(sp, 1); g.Children.Add(sp);
        return Callout(g, colour);
    }

    // ------------------------------------------------------------ medals and insignia
    public static Control Medal(string icon, string colour, bool earned, double size)
    {
        var g = new Grid { Width = size, Height = size };
        IBrush fill;
        if (earned)
        {
            var c = C(colour);
            fill = new RadialGradientBrush
            {
                GradientOrigin = new RelativePoint(0.35, 0.3, RelativeUnit.Relative), Center = new RelativePoint(0.45, 0.4, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.75, RelativeUnit.Relative), RadiusY = new RelativeScalar(0.75, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Mix(c, Colors.White, 0.25), 0), new GradientStop(Mix(c, Colors.Black, 0.35), 1) },
            };
        }
        else fill = Soft("#FFFFFF", 0.04);
        g.Children.Add(new Ellipse { Fill = fill, Stroke = earned ? Soft("#FFFFFF", 0.35) : B(Ui.BorderStrong), StrokeThickness = earned ? 1.5 : 1.2, StrokeDashArray = earned ? null : new AvaloniaList<double> { 3, 2.5 } });
        if (earned) g.Children.Add(new Ellipse { Margin = new Thickness(size * 0.09), Stroke = Soft("#FFFFFF", 0.22), StrokeThickness = 1 });
        var ic = Icon(icon, size * 0.46, earned ? "#FFFFFF" : Ui.Dim, 2.1);
        ic.HorizontalAlignment = HorizontalAlignment.Center;
        g.Children.Add(ic);
        return g;
    }

    /// <summary>A rank's insignia: one chevron per rank; the top rank adds a star.</summary>
    public static Control Insignia(int rank, double size, string colour)
    {
        var cv = new Canvas { Width = 24, Height = 24 };
        int chevrons = Math.Min(4, rank + 1);
        double top = rank >= 4 ? 9.6 : 12 - chevrons * 2.2 + 2.2, step = rank >= 4 ? 3.4 : 4.2;
        if (rank >= 4) cv.Children.Add(new Path { Data = Geometry.Parse("M12,1.2 L13.4,4.2 L16.6,4.5 L14.2,6.6 L14.9,9.8 L12,8.1 L9.1,9.8 L9.8,6.6 L7.4,4.5 L10.6,4.2 Z"), Fill = B(colour) });
        for (int i = 0; i < chevrons; i++)
        {
            double y = top + i * step;
            cv.Children.Add(new Path { Data = Geometry.Parse(string.Format(CultureInfo.InvariantCulture, "M4,{0} L12,{1} L20,{0}", y + 4, y)), Stroke = B(colour), StrokeThickness = 2.6, StrokeJoin = PenLineJoin.Round, StrokeLineCap = PenLineCap.Round });
        }
        return new Viewbox { Child = cv, Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center };
    }

    public static string N(int n) => RangeLook.N(n);
    public static string Pts(int n) => N(n) + (n == 1 ? " point" : " points");
}

/// <summary>A tappable card: a button that keeps its card look, lights its edge in the accent on hover or focus and
/// darkens a little when pressed. Enter and Space work as on any button.</summary>
public sealed class RangeCard : Button
{
    public RangeCard(string accent, Control content, Action click, double pad = 16)
    {
        var rest = Rg.C(Ui.BorderC);
        var hot = Rg.Hex(Rg.Mix(rest, Rg.C(accent), 0.75));
        Content = content;
        Padding = new Thickness(pad);
        CornerRadius = new CornerRadius(12);
        Background = Rg.CardFill();
        BorderBrush = Rg.B(Ui.BorderC);
        BorderThickness = new Thickness(1);
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        MinHeight = 44;
        ClipToBounds = true;
        Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand);
        Resources["ButtonBackgroundPointerOver"] = Rg.Gradient("#1B1F28", "#161A21", 90);
        Resources["ButtonBackgroundPressed"] = Rg.B("#11141A");
        Resources["ButtonBorderBrushPointerOver"] = Rg.B(hot);
        Resources["ButtonBorderBrushPressed"] = Rg.B(hot);
        Resources["ButtonForegroundPointerOver"] = Rg.B(Ui.TextC);
        Resources["ButtonForegroundPressed"] = Rg.B(Ui.TextC);
        Foreground = Rg.B(Ui.TextC);
        if (click != null) Click += (s, e) => RangeUi.Safe(click);
    }
}

/// <summary>An on / off switch with a label: a 44 px button (tap, Enter or Space toggles it).</summary>
public sealed class RangeSwitch : Button
{
    public event Action<bool> Changed;
    public readonly string Label;
    readonly Border track;
    readonly Ellipse knob;
    readonly string colour;
    bool on;
    public bool IsOn { get => on; set { on = value; Paint(); } }

    public RangeSwitch(string label, string icon, string colour, bool isOn)
    {
        Label = label; this.colour = colour; on = isOn;
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9 };
        knob = new Ellipse { Width = 16, Height = 16, Margin = new Thickness(3, 0), VerticalAlignment = VerticalAlignment.Center };
        track = new Border { Width = 42, Height = 24, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center, Child = knob };
        sp.Children.Add(track);
        if (icon != null) sp.Children.Add(Rg.Icon(icon, 16, colour, 2.1));
        sp.Children.Add(new TextBlock { Text = label, FontSize = 14, Foreground = Rg.B(Ui.TextC), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        Content = sp;
        MinHeight = 44;
        Padding = new Thickness(6, 4, 12, 4);
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        HorizontalAlignment = HorizontalAlignment.Left;
        Resources["ButtonBackgroundPointerOver"] = Rg.Soft("#FFFFFF", 0.04);
        Resources["ButtonBackgroundPressed"] = Rg.Soft("#FFFFFF", 0.07);
        Avalonia.Automation.AutomationProperties.SetName(this, label);
        Click += (s, e) => Toggle();
        Paint();
    }
    public void Toggle() { on = !on; Paint(); var c = Changed; if (c != null) RangeUi.Safe(() => c(on)); }
    void Paint()
    {
        track.Background = on ? Rg.B(colour) : Rg.Soft("#FFFFFF", 0.06);
        track.BorderBrush = on ? Rg.B(colour) : Rg.B(Ui.BorderStrong);
        knob.Fill = on ? Rg.B("#0B0D12") : Rg.B(Ui.Muted);
        knob.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left;
    }
}

/// <summary>A round progress ring with a short text (or the percentage) in the middle.</summary>
public sealed class RangeRing : Control
{
    public double Value;
    public string Fill = "#34D399";
    public double Stroke = 6, CenterSize = 15;
    public string Center, Under;
    protected override Size MeasureOverride(Size s) => new(double.IsNaN(Width) ? 56 : Width, double.IsNaN(Height) ? 56 : Height);
    public override void Render(DrawingContext dc)
    {
        double w = Bounds.Width, h = Bounds.Height, r = Math.Min(w, h) / 2 - Stroke / 2;
        var c = new Point(w / 2, h / 2);
        dc.DrawEllipse(null, new Pen(Rg.Soft("#FFFFFF", 0.08), Stroke), c, r, r);
        double v = Math.Max(0, Math.Min(1, Value));
        var fill = Rg.B(Fill);
        if (v > 0.999) dc.DrawEllipse(null, new Pen(fill, Stroke), c, r, r);
        else if (v > 0)
        {
            double a0 = -Math.PI / 2, a1 = a0 + v * 2 * Math.PI;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(new Point(c.X + r * Math.Cos(a0), c.Y + r * Math.Sin(a0)), false);
                ctx.ArcTo(new Point(c.X + r * Math.Cos(a1), c.Y + r * Math.Sin(a1)), new Size(r, r), 0, v > 0.5, SweepDirection.Clockwise);
                ctx.EndFigure(false);
            }
            dc.DrawGeometry(null, new Pen(fill, Stroke) { LineCap = PenLineCap.Round }, g);
        }
        string t = Center ?? ((int)Math.Round(v * 100)).ToString(CultureInfo.InvariantCulture) + "%";
        var ft = new FormattedText(t, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold), CenterSize, Rg.B(Ui.TextC));
        double y = c.Y - ft.Height / 2;
        if (!string.IsNullOrEmpty(Under))
        {
            var fu = new FormattedText(Under, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(FontFamily.Default), Math.Max(9, CenterSize * 0.55), Rg.B(Ui.Muted));
            y = c.Y - (ft.Height + fu.Height - 2) / 2;
            dc.DrawText(fu, new Point(c.X - fu.Width / 2, y + ft.Height - 2));
        }
        dc.DrawText(ft, new Point(c.X - ft.Width / 2, y));
    }
}

/// <summary>Children in as many equal columns as fit (each at least MinCell wide, at most MaxCols); each row is as tall as
/// its tallest child. The home's cards (two columns on a wide screen, one on a phone), the hero's stats, the tour, medals.</summary>
public sealed class FlowGrid : Panel
{
    public double MinCell = 460, Gap = 14, RowGap = 0;
    public int MaxCols = 2;
    double[] rows = Array.Empty<double>();
    int Cols(double w)
    {
        if (double.IsInfinity(w)) return MaxCols;
        int n = (int)((w + Gap) / (MinCell + Gap));
        return Math.Max(1, Math.Min(MaxCols, Math.Min(n, Math.Max(1, Children.Count))));
    }
    protected override Size MeasureOverride(Size avail)
    {
        int cols = Cols(avail.Width);
        double cw = double.IsInfinity(avail.Width) ? MinCell : Math.Max(0, (avail.Width - Gap * (cols - 1)) / cols);
        int n = Children.Count, nr = (n + cols - 1) / cols;
        rows = new double[nr];
        for (int i = 0; i < n; i++)
        {
            var c = Children[i];
            c.Measure(new Size(cw, double.PositiveInfinity));
            rows[i / cols] = Math.Max(rows[i / cols], c.DesiredSize.Height);
        }
        double w = double.IsInfinity(avail.Width) ? cw * cols + Gap * (cols - 1) : avail.Width;
        return new Size(w, rows.Sum() + RowGap * Math.Max(0, nr - 1));
    }
    protected override Size ArrangeOverride(Size final)
    {
        int cols = Cols(final.Width);
        double cw = Math.Max(0, (final.Width - Gap * (cols - 1)) / cols);
        int n = Children.Count, nr = (n + cols - 1) / cols;
        if (rows.Length != nr) { rows = new double[nr]; for (int i = 0; i < n; i++) rows[i / cols] = Math.Max(rows[i / cols], Children[i].DesiredSize.Height); }
        double y = 0;
        for (int r = 0; r < nr; r++)
        {
            for (int k = 0; k < cols; k++)
            {
                int i = r * cols + k;
                if (i >= n) break;
                Children[i].Arrange(new Rect(k * (cw + Gap), y, cw, rows[r]));
            }
            y += rows[r] + RowGap;
        }
        return final;
    }
}

/// <summary>Two parts side by side (the main part, then a fixed-width side) on a wide page, stacked when narrow: the side
/// first (a briefing's Start button) or after (the hero's rank).</summary>
public sealed class RangeSplit : Panel
{
    public double Side = 320, Gap = 22, StackBelow = 880;
    public bool SideFirst = true;
    bool Stacked(double w) => double.IsInfinity(w) || w < StackBelow;
    protected override Size MeasureOverride(Size a)
    {
        if (Children.Count < 2) { if (Children.Count == 1) { Children[0].Measure(a); return Children[0].DesiredSize; } return new Size(); }
        var m = Children[0]; var s = Children[1];
        if (Stacked(a.Width))
        {
            m.Measure(new Size(a.Width, double.PositiveInfinity)); s.Measure(new Size(a.Width, double.PositiveInfinity));
            return new Size(double.IsInfinity(a.Width) ? Math.Max(m.DesiredSize.Width, s.DesiredSize.Width) : a.Width, m.DesiredSize.Height + s.DesiredSize.Height + Gap * 0.6);
        }
        m.Measure(new Size(Math.Max(0, a.Width - Side - Gap), double.PositiveInfinity)); s.Measure(new Size(Side, double.PositiveInfinity));
        return new Size(a.Width, Math.Max(m.DesiredSize.Height, s.DesiredSize.Height));
    }
    protected override Size ArrangeOverride(Size f)
    {
        if (Children.Count < 2) { if (Children.Count == 1) Children[0].Arrange(new Rect(f)); return f; }
        var m = Children[0]; var s = Children[1];
        double gap = Gap * 0.6;
        if (Stacked(f.Width))
        {
            if (SideFirst) { s.Arrange(new Rect(0, 0, f.Width, s.DesiredSize.Height)); m.Arrange(new Rect(0, s.DesiredSize.Height + gap, f.Width, m.DesiredSize.Height)); }
            else { m.Arrange(new Rect(0, 0, f.Width, m.DesiredSize.Height)); s.Arrange(new Rect(0, m.DesiredSize.Height + gap, f.Width, s.DesiredSize.Height)); }
        }
        else { m.Arrange(new Rect(0, 0, f.Width - Side - Gap, f.Height)); s.Arrange(new Rect(f.Width - Side, 0, Side, s.DesiredSize.Height)); }
        return f;
    }
}

/// <summary>Little helpers every Range page uses.</summary>
public static class RangeUi
{
    /// <summary>Errors in handlers become a sentence on the page instead of a crash.</summary>
    public static Action<string> OnError;
    public static void Safe(Action a)
    {
        try { a(); }
        catch (Exception ex) { if (OnError != null) OnError("Something went wrong: " + ex.Message); else throw; }
    }

    public static Button Primary(string text, Action a)
    {
        var b = Ui.Button(text, true, 15);
        b.Margin = new Thickness(0, 0, 8, 8);
        b.Click += (s, e) => Safe(a);
        return b;
    }
    public static Button Secondary(string text, Action a)
    {
        var b = Ui.Button(text, false, 15);
        b.Margin = new Thickness(0, 0, 8, 8);
        b.Click += (s, e) => Safe(a);
        return b;
    }
    /// <summary>A small button (still 44 px high: touch first).</summary>
    public static Button Small(string text, Action a)
    {
        var b = Ui.Button(text, false, 14);
        b.Padding = new Thickness(14, 8);
        b.Click += (s, e) => Safe(a);
        return b;
    }

    /// <summary>A scrolling page: a column up to maxWidth, with room at the sides (16 px on a phone).</summary>
    public static ScrollViewer Page(Control root, double maxWidth)
    {
        root.MaxWidth = maxWidth;
        root.HorizontalAlignment = HorizontalAlignment.Stretch;
        root.Margin = new Thickness(16, 12, 16, 28);
        return new ScrollViewer { Content = root, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
    }

    /// <summary>A challenge's status pill: Done (best score), Tried, or New.</summary>
    public static Border Status(RangeApp v, RangeItem it)
    {
        var r = v.Store.For(it.Puzzle, it.Id);
        if (r.Completed)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
            sp.Children.Add(Rg.Icon("check", 12, "#34D399", 3));
            sp.Children.Add(new TextBlock { Text = "Done  " + r.BestScore + "/" + it.Points, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = Rg.B("#6EE7B7"), VerticalAlignment = VerticalAlignment.Center });
            return new Border { Child = sp, Background = Rg.Soft("#34D399", 0.12), CornerRadius = new CornerRadius(20), Padding = new Thickness(8, 3, 10, 4), VerticalAlignment = VerticalAlignment.Top };
        }
        if (r.Attempts > 0) return Pill("Tried  best " + r.BestScore, "#FBBF24");
        return Pill("New", "#93C5FD");
    }
    static Border Pill(string t, string c)
        => new() { Child = new TextBlock { Text = t, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = Rg.B(c) }, BorderBrush = Rg.Soft(c, 0.45), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(20), Padding = new Thickness(9, 2, 9, 3), VerticalAlignment = VerticalAlignment.Top };

    /// <summary>A small tile with a number and a label (the hero's stats).</summary>
    public static Control Stat(string icon, string colour, string value, string label)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(0, 0, 0, 10) };
        var ic = Rg.Icon(icon, 17, colour, 2.1); ic.HorizontalAlignment = HorizontalAlignment.Center;
        g.Children.Add(new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(10), Background = Rg.Soft(colour, 0.13), Child = ic, Margin = new Thickness(0, 0, 11, 0), VerticalAlignment = VerticalAlignment.Center });
        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(new TextBlock { Text = value, FontSize = 19, FontWeight = FontWeight.SemiBold, Foreground = Rg.B(Ui.TextC) });
        sp.Children.Add(new TextBlock { Text = label, FontSize = 12.5, Foreground = Rg.B(Ui.Muted), TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(sp, 1); g.Children.Add(sp);
        return g;
    }
}
