// Learning Lab - small helpers for building pages in code: colours, text, buttons and cards in App Hub's dark look.
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace LearningLab.Views;

public static class Ui
{
    public const string Bg = "#0B0D12", Surface = "#13161D", Surface2 = "#1A1E27", Surface3 = "#242A36", BorderC = "#252A35",
        BorderStrong = "#353C4B", TextC = "#E6E9F0", Muted = "#8C93A5", Dim = "#5C6375", Accent = "#3B82F6", AccentHover = "#60A5FA",
        AccentSoft = "#1B2D50", AccentText = "#93C5FD", Green = "#34D399", Red = "#F87171", Yellow = "#FBBF24";

    static readonly Dictionary<string, IBrush> brushes = new();

    public static IBrush Brush(string hex) => Brush(hex, 255);
    public static IBrush Brush(string hex, byte alpha)
    {
        string key = hex + "/" + alpha;
        lock (brushes)
        {
            if (brushes.TryGetValue(key, out var b)) return b;
            Color c = Color.TryParse(hex, out var parsed) ? parsed : Color.Parse("#3B82F6");
            b = new ImmutableSolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
            brushes[key] = b;
            return b;
        }
    }

    public static TextBlock Text(string text, double size = 15, string color = TextC, bool bold = false)
    {
        var t = new TextBlock { Text = text, FontSize = size, Foreground = Brush(color), TextWrapping = TextWrapping.Wrap };
        if (bold) t.FontWeight = FontWeight.SemiBold;
        return t;
    }

    public static Button Button(string text, bool primary = false, double size = 15)
    {
        var b = new Button
        {
            Content = text,
            FontSize = size,
            Padding = new Thickness(18, 10),
            MinHeight = 44,
            CornerRadius = new CornerRadius(8),
            Foreground = Brush(primary ? "#FFFFFF" : TextC),
            Background = Brush(primary ? Accent : Surface3),
            BorderBrush = Brush(primary ? Accent : BorderStrong),
            BorderThickness = new Thickness(1),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        return b;
    }

    public static Button IconButton(string glyph, string tip)
    {
        var b = new Button
        {
            Content = glyph,
            FontSize = 20,
            Width = 44, Height = 44,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(22),
            Background = Brushes.Transparent,
            Foreground = Brush(TextC),
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        ToolTip.SetTip(b, tip);
        return b;
    }

    public static Border Card(Control child, string bg = Surface, double pad = 16)
    {
        return new Border
        {
            Child = child,
            Background = Brush(bg),
            BorderBrush = Brush(BorderC),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(pad),
        };
    }

    /// <summary>A card that acts as a button (tiles on the home pages).</summary>
    public static Button Tile(Control content, string accent, Action click)
    {
        var b = new Button
        {
            Content = content,
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(14),
            Background = Brush(Surface),
            BorderBrush = Brush(accent, 110),
            BorderThickness = new Thickness(1.5),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        b.Click += (s, e) => click();
        return b;
    }

    /// <summary>A scrolling page body with a readable maximum width.</summary>
    public static ScrollViewer Page(Control body, double maxWidth = 1100)
    {
        body.Margin = new Thickness(16);
        body.MaxWidth = maxWidth;
        body.HorizontalAlignment = HorizontalAlignment.Stretch;
        body.VerticalAlignment = VerticalAlignment.Top;
        return new ScrollViewer { Content = body, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    /// <summary>A grid that puts as many columns of tiles as fit (one on a phone held upright).</summary>
    public static Control Tiles(IEnumerable<Control> items, double minTile = 260, double gap = 12)
    {
        var list = items.ToList();
        var grid = new UniformGrid();
        var outer = new Border { Child = grid };
        void Fit(double w)
        {
            int cols = Math.Max(1, (int)((w + gap) / (minTile + gap)));
            cols = Math.Min(cols, Math.Max(1, list.Count));
            if (grid.Columns != cols) grid.Columns = cols;
        }
        foreach (var c in list) { c.Margin = new Thickness(gap / 2); grid.Children.Add(c); }
        outer.SizeChanged += (s, e) => Fit(e.NewSize.Width);
        Fit(1000);
        return outer;
    }

    public static string F(double v, string fmt = "0") => v.ToString(fmt, CultureInfo.InvariantCulture);
}
