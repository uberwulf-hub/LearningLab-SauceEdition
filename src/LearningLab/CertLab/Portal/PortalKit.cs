// Cert Lab - building blocks of the practice portal (MD-102) and the practice consoles (AZ-800), App Hub's EpUi and
// WsKit for Avalonia and touch: every button and link is at least 44 px high, tables become cards on a narrow screen,
// a label sits above its value on a phone, and nothing is only on hover. Cert Lab's own look (App Hub's dark theme).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using LearningLab.Views;
using CertLab;

namespace LearningLab.CertLab.Portal;

public static class Pk
{
    public const string GoodC = "#22C55E", BadC = "#EF4444", WarnC = "#FBBF24", GoodSoftC = "#123222", BadSoftC = "#3A1717", WarnSoftC = "#3A2E10",
        LabelC = "#A3AABA";
    public static IBrush Good => Ui.Brush(GoodC);
    public static IBrush Bad => Ui.Brush(BadC);
    public static IBrush Warn => Ui.Brush(WarnC);
    public static IBrush B(string hex) => Ui.Brush(hex);
    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Menlo, DejaVu Sans Mono, monospace");

    /// <summary>Runs a tap's action; an error becomes a message instead of ending the app.</summary>
    public static void Safe(Action a)
    {
        try { a?.Invoke(); }
        catch (Exception e)
        {
            if (MainView.Current != null) MainView.Current.Message("Something went wrong", e.Message);
            else throw;
        }
    }

    // ------------------------------------------------------------ icons (24 x 24 strokes)
    static readonly Dictionary<string, string> paths = new()
    {
        { "menu", "M3,6 H21 M3,12 H21 M3,18 H21" },
        { "back", "M15,4.5 L7.5,12 L15,19.5" },
        { "home", "M3,11 L12,3.5 L21,11 M5.5,9 V20.5 H18.5 V9 M10,20.5 V14.5 H14 V20.5" },
        { "pc", "M3,4.5 H21 V15.5 H3 Z M8,20 H16 M12,15.5 V20" },
        { "phone", "M7,2.5 H17 V21.5 H7 Z M11,18.5 H13" },
        { "folder", "M3,6 H10 L12,8.5 H21 V19 H3 Z" },
        { "user", "M12,3.5 A4,4 0 1 1 11.99,3.5 Z M4.5,21 C4.5,16.5 8,14 12,14 C16,14 19.5,16.5 19.5,21" },
        { "group", "M9,4 A3.5,3.5 0 1 1 8.99,4 Z M2.5,20 C2.5,15.5 5.5,13.5 9,13.5 C12.5,13.5 15.5,15.5 15.5,20 M16,4.5 A3,3 0 1 1 15.99,4.5 Z M17.5,13.5 C20,14 21.5,16 21.5,19" },
        { "doc", "M6,2.5 H14 L19,7.5 V21.5 H6 Z M14,2.5 V7.5 H19 M9,12 H16 M9,16 H16" },
        { "filter", "M3,4.5 H21 L14,12.5 V19.5 L10,21 V12.5 Z" },
        { "link", "M10,14 L14,10 M8.5,11.5 L6,14 C4.5,15.5 4.5,18 6,19.5 C7.5,21 10,21 11.5,19.5 L14,17 M15.5,12.5 L18,10 C19.5,8.5 19.5,6 18,4.5 C16.5,3 14,3 12.5,4.5 L10,7" },
        { "domain", "M12,2.5 L21,7 V17 L12,21.5 L3,17 V7 Z M3,7 L12,11.5 L21,7 M12,11.5 V21.5" },
        { "chart", "M3,21 H21 M6,17 V11 M11,17 V6 M16,17 V13 M20,17 V8" },
        { "app", "M4,4 H10 V10 H4 Z M14,4 H20 V10 H14 Z M4,14 H10 V20 H4 Z M14,14 H20 V20 H14 Z" },
        { "update", "M20,12 A8,8 0 1 1 17.7,6.3 M20,3.5 V8.5 H15" },
        { "list", "M8,6 H21 M8,12 H21 M8,18 H21 M3.5,6 H4.5 M3.5,12 H4.5 M3.5,18 H4.5" },
        { "settings", "M12,8.5 A3.5,3.5 0 1 1 11.99,8.5 Z M12,2 V5 M12,19 V22 M2,12 H5 M19,12 H22 M4.9,4.9 L7,7 M17,17 L19.1,19.1 M4.9,19.1 L7,17 M17,7 L19.1,4.9" },
        { "close", "M5,5 L19,19 M19,5 L5,19" },
    };

    public static string IconPath(string key)
    {
        if (paths.TryGetValue(key ?? "", out var p)) return p;
        return RangeLook.HasIcon(key) ? RangeLook.IconPaths[key] : paths["doc"];
    }

    public static Control Icon(string key, double size, string color)
    {
        return new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse(IconPath(key)), Stroke = B(color), StrokeThickness = 1.8, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round,
            Stretch = Stretch.Uniform, Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center,
        };
    }

    /// <summary>A round 44 x 44 button with an icon (the menu, Back).</summary>
    public static Button IconBtn(string icon, string tip, Action click)
    {
        var b = new Button
        {
            Content = Icon(icon, 20, Ui.TextC), Width = 44, Height = 44, Padding = new Thickness(0), CornerRadius = new CornerRadius(8),
            Background = B(Ui.Surface3), BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(b, tip);
        AutomationTip(b, tip);
        b.Click += (s, e) => Safe(click);
        return b;
    }
    static void AutomationTip(Control c, string name) => Avalonia.Automation.AutomationProperties.SetName(c, name);

    // ------------------------------------------------------------ status colours
    /// <summary>The colour a status word gets: green for good, red for bad, yellow for waiting.</summary>
    public static string StatusColor(string s)
    {
        var v = (s ?? "").ToLowerInvariant();
        if (v == "compliant" || v == "succeeded" || v == "installed" || v == "on" || v == "assigned" || v == "allowed" || v == "yes" || v == "grant" || v == "applies") return GoodC;
        if (v.StartsWith("not compliant", StringComparison.Ordinal) || v == "error" || v == "failed" || v == "conflict" || v == "blocked" || v == "block" || v == "off") return BadC;
        if (v.Contains("pending") || v.Contains("grace") || v.Contains("assigning") || v.Contains("encrypting") || v.Contains("mfa") || v == "available") return WarnC;
        return Ui.Muted;
    }
    public static IBrush StatusBrush(string s) => B(StatusColor(s));

    public static Border Badge(string s)
    {
        string fg = StatusColor(s);
        string bg = fg == GoodC ? GoodSoftC : fg == BadC ? BadSoftC : fg == WarnC ? WarnSoftC : Ui.Surface3;
        return new Border
        {
            Background = B(bg), CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 2, 9, 3), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = s, Foreground = B(fg), FontSize = 13 },
        };
    }

    // ------------------------------------------------------------ text
    public static TextBlock Txt(string text, double size = 14, bool bold = false, string color = Ui.TextC)
    {
        var t = new TextBlock { Text = text ?? "", FontSize = size, Foreground = B(color), TextWrapping = TextWrapping.Wrap };
        if (bold) t.FontWeight = FontWeight.SemiBold;
        return t;
    }
    public static TextBlock H(string text) { var t = Txt(text, 16, true); t.Margin = new Thickness(0, 16, 0, 6); return t; }
    public static TextBlock Small(string text) { var t = Txt(text, 13, false, LabelC); t.Margin = new Thickness(0, 0, 0, 4); return t; }
    public static TextBlock Error(string text) { var t = Txt(text, 13, false, BadC); t.Margin = new Thickness(0, 2, 0, 4); return t; }
    public static TextBlock MonoText(string text) => new() { Text = text ?? "", FontFamily = Mono, FontSize = 12.5, Foreground = B(LabelC), TextWrapping = TextWrapping.Wrap };

    /// <summary>A blade's heading: a big title and a line of plain words.</summary>
    public static StackPanel Title(string title, string help)
    {
        var s = new StackPanel { Margin = new Thickness(0, 4, 0, 8) };
        s.Children.Add(Txt(title, 20, true));
        if (!string.IsNullOrEmpty(help)) { var h = Txt(help, 13.5, false, LabelC); h.Margin = new Thickness(0, 2, 0, 0); s.Children.Add(h); }
        return s;
    }

    /// <summary>A label and a value: on one line when wide, the label above the value on a phone.</summary>
    public static Control Pair(string label, string value, bool narrow)
    {
        var l = Txt(label, 13.5, false, LabelC);
        var v = Txt(string.IsNullOrEmpty(value) ? "-" : value, 14);
        if (narrow)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
            sp.Children.Add(l); sp.Children.Add(v);
            return sp;
        }
        var g = new Grid { Margin = new Thickness(0, 0, 0, 5) };
        g.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(240)));
        g.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        l.Margin = new Thickness(0, 0, 10, 0);
        Grid.SetColumn(v, 1);
        g.Children.Add(l); g.Children.Add(v);
        return g;
    }

    // ------------------------------------------------------------ buttons
    /// <summary>A button (44 px high); primary = the accent colour.</summary>
    public static Button Btn(string text, Action click, bool primary = false)
    {
        var b = Ui.Button(text, primary, 14);
        b.Padding = new Thickness(14, 8);
        b.Margin = new Thickness(0, 0, 6, 6);
        b.Click += (s, e) => Safe(click);
        return b;
    }
    public static Button Accent(string text, Action click) => Btn(text, click, true);

    /// <summary>A smaller-looking button that is still 44 px high to tap.</summary>
    public static Button Small(string text, Action click)
    {
        var b = Ui.Button(text, false, 13);
        b.Padding = new Thickness(12, 6);
        b.Margin = new Thickness(0, 0, 6, 6);
        b.Click += (s, e) => Safe(click);
        return b;
    }

    /// <summary>A button marked as the one picked (tabs, steps).</summary>
    public static void Picked(Button b, bool on)
    {
        b.Background = B(on ? Ui.AccentSoft : Ui.Surface3);
        b.BorderBrush = B(on ? Ui.Accent : Ui.BorderStrong);
        if (on) b.FontWeight = FontWeight.SemiBold;
    }

    /// <summary>A text link, 44 px high to tap.</summary>
    public static Button Link(string text, Action click)
    {
        var b = new Button
        {
            Content = text, Foreground = B(Ui.AccentText), FontSize = 14, MinHeight = 44, Padding = new Thickness(4, 4),
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left,
            HorizontalContentAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Center, Cursor = new Cursor(StandardCursorType.Hand),
        };
        b.Resources["ButtonForegroundPointerOver"] = B(Ui.AccentHover);
        b.Resources["ButtonForegroundPressed"] = B(Ui.AccentHover);
        if (click != null) b.Click += (s, e) => Safe(click);
        return b;
    }

    /// <summary>A row of controls that wraps on a narrow screen.</summary>
    public static WrapPanel Row(params Control[] items)
    {
        var w = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var i in items) if (i != null) { i.VerticalAlignment = VerticalAlignment.Center; w.Children.Add(i); }
        return w;
    }
    /// <summary>A control that takes the width it is given up to max, from the left (MaxWidth alone would centre it).</summary>
    public static Grid Capped(Control c, double max)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)) { MaxWidth = max });
        g.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(0)));
        c.HorizontalAlignment = HorizontalAlignment.Stretch;
        g.Children.Add(c);
        return g;
    }
    public static StackPanel Col(params Control[] items) { var s = new StackPanel(); foreach (var i in items) if (i != null) s.Children.Add(i); return s; }

    /// <summary>A row of tab buttons that scrolls sideways with a finger when it doesn't fit.</summary>
    public static ScrollViewer Strip(StackPanel row)
    {
        row.Orientation = Orientation.Horizontal;
        return new ScrollViewer { Content = row, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    }

    /// <summary>Takes a control out of its old parent (a form kept between redraws is put in a new card).</summary>
    public static T Detach<T>(T c) where T : Control
    {
        if (c?.Parent is Decorator d) d.Child = null;
        else if (c?.Parent is Panel p) p.Children.Remove(c);
        else if (c?.Parent is ContentControl cc) cc.Content = null;
        return c;
    }

    public static Border Card(Control child, double top = 8)
        => new() { Child = Detach(child), Background = B(Ui.Surface2), BorderBrush = B(Ui.BorderC), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 12), Margin = new Thickness(0, top, 0, 8) };

    /// <summary>A number in a tile (the portal's home page).</summary>
    public static Border Tile(string label, string value, string accent)
    {
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = value, FontSize = 26, FontWeight = FontWeight.SemiBold, Foreground = B(accent) });
        sp.Children.Add(Txt(label, 13, false, LabelC));
        return new Border { Child = sp, Background = B(Ui.Surface2), BorderBrush = B(Ui.BorderC), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 10), Margin = new Thickness(0, 0, 10, 10), MinWidth = 140 };
    }

    // ------------------------------------------------------------ fields
    public static ComboBox Combo(IEnumerable<string> items, string selected, double width)
    {
        var c = new ComboBox { MinWidth = width, MaxWidth = 460, MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 6, 6), FontSize = 14 };
        foreach (var i in items) c.Items.Add(new ComboBoxItem { Content = i.Length == 0 ? "(not set)" : i, Tag = i, MinHeight = 40 });
        for (int i = 0; i < c.Items.Count; i++) if (EpText.Eq((string)((ComboBoxItem)c.Items[i]).Tag, selected)) { c.SelectedIndex = i; break; }
        return c;
    }
    /// <summary>The words shown for the first item (a "Pick..." prompt in place of an empty value).</summary>
    public static void FirstLabel(ComboBox c, string label) { if (c.Items.Count > 0) ((ComboBoxItem)c.Items[0]).Content = label; }
    /// <summary>Picks the first item when nothing is picked (a list to act on, not a setting that may be empty).</summary>
    public static ComboBox First(ComboBox c) { if (c.SelectedIndex < 0 && c.Items.Count > 0) c.SelectedIndex = 0; return c; }
    public static string Val(ComboBox c) => c.SelectedItem is ComboBoxItem i ? (string)i.Tag ?? "" : "";
    public static void Select(ComboBox c, string v) { for (int i = 0; i < c.Items.Count; i++) if (EpText.Eq((string)((ComboBoxItem)c.Items[i]).Tag, v)) { c.SelectedIndex = i; return; } }

    public static TextBox Box(string text, double width)
        => new() { Text = text ?? "", Width = Math.Max(width, 60), MaxWidth = 520, MinHeight = 44, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 10), Margin = new Thickness(0, 0, 6, 6), VerticalContentAlignment = VerticalAlignment.Center };
    public static TextBox Multi(string text, double height, bool mono)
    {
        var t = new TextBox { Text = text ?? "", AcceptsReturn = true, TextWrapping = mono ? TextWrapping.NoWrap : TextWrapping.Wrap, Height = height, Padding = new Thickness(8, 6), Margin = new Thickness(0, 0, 0, 6), FontSize = 14, HorizontalAlignment = HorizontalAlignment.Stretch };
        if (mono) { t.FontFamily = Mono; t.FontSize = 13; t.AcceptsTab = true; }
        return t;
    }
    public static string T(TextBox b) => b.Text ?? "";
    /// <summary>Runs when the box's text changes, at once (TextBox.TextChanged comes later, from the dispatcher).</summary>
    public static void OnText(TextBox b, Action changed)
    {
        b.PropertyChanged += (s, e) => { if (e.Property == TextBox.TextProperty) Safe(changed); };
    }
    public static CheckBox Check(string text, bool on)
        => new() { Content = text, IsChecked = on, MinHeight = 44, Margin = new Thickness(0, 0, 14, 4), Foreground = B(Ui.TextC), FontSize = 14 };

    // ------------------------------------------------------------ tables
    /// <summary>A table: header + rows; cell 0 may be a link. Narrow: each row becomes a card (name, then "Head: value" lines).</summary>
    public static Control Table(string[] heads, IEnumerable<string[]> rows, Action<int> open, bool narrow, string empty)
    {
        var list = rows.ToList();
        if (list.Count == 0) return Small(empty ?? "Nothing here yet.");
        if (narrow)
        {
            var sp = new StackPanel();
            for (int r = 0; r < list.Count; r++)
            {
                int ri = r;
                var row = list[r];
                var card = new StackPanel();
                if (open != null) card.Children.Add(Link(row[0], () => open(ri))); else card.Children.Add(Txt(row[0], 14, true));
                for (int c = 1; c < row.Length && c < heads.Length; c++)
                {
                    if (string.IsNullOrEmpty(row[c])) continue;
                    if (IsStatusHead(heads[c])) { var w = Row(Txt(heads[c] + ":  ", 13, false, LabelC), Badge(row[c])); w.Margin = new Thickness(0, 2, 0, 2); card.Children.Add(w); }
                    else card.Children.Add(Txt(heads[c] + ": " + row[c], 13, false, LabelC));
                }
                sp.Children.Add(new Border { Child = card, BorderBrush = B(Ui.BorderC), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 4, 0, 8) });
            }
            return sp;
        }
        var g = new Grid();
        for (int c = 0; c < heads.Length; c++) g.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(c == 0 ? 1.6 : 1, GridUnitType.Star)));
        g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (int c = 0; c < heads.Length; c++)
        {
            var h = Txt(heads[c], 13, true, LabelC); h.Margin = new Thickness(0, 0, 8, 4);
            Grid.SetColumn(h, c); g.Children.Add(h);
        }
        for (int r = 0; r < list.Count; r++)
        {
            g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var line = new Border { BorderBrush = B(Ui.BorderC), BorderThickness = new Thickness(0, 1, 0, 0) };
            Grid.SetRow(line, r + 1); Grid.SetColumnSpan(line, heads.Length); g.Children.Add(line);
            for (int c = 0; c < heads.Length; c++)
            {
                string v = c < list[r].Length ? list[r][c] : "";
                int ri = r;
                Control cell;
                if (c == 0 && open != null) cell = Link(v, () => open(ri));
                else if (IsStatusHead(heads[c])) cell = string.IsNullOrEmpty(v) ? Txt("", 14) : Badge(v);
                else cell = Txt(v, 14);
                cell.Margin = new Thickness(0, 5, 8, 5);
                cell.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetRow(cell, r + 1); Grid.SetColumn(cell, c);
                g.Children.Add(cell);
            }
        }
        return g;
    }
    static bool IsStatusHead(string h) => h == "Compliance" || h == "Status" || h == "Profile status" || h == "Result";

    /// <summary>One line of a console's tree: indented, an icon, the text; the picked one is highlighted. A button to tap.</summary>
    public static Button TreeItem(string text, int depth, bool selected, string icon, Action click)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (icon != null) sp.Children.Add(Icon(icon, 16, selected ? Ui.AccentText : LabelC));
        sp.Children.Add(new TextBlock { Text = text, Foreground = B(selected ? Ui.AccentText : Ui.TextC), FontSize = 14, FontWeight = selected ? FontWeight.SemiBold : FontWeight.Normal, VerticalAlignment = VerticalAlignment.Center });
        var b = new Button
        {
            Content = sp, MinHeight = 44, Padding = new Thickness(6 + depth * 14, 4, 8, 4), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Center, Background = selected ? B(Ui.Surface3) : Brushes.Transparent, BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(6),
            IsEnabled = true,
        };
        b.Tag = text;
        if (click != null) b.Click += (s, e) => Safe(click);
        return b;
    }
}
