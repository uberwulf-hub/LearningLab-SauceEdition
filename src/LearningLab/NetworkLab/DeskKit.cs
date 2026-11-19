// Network Lab - pieces shared by the pretend desktops (App Hub's DesktopFrame.cs and Desktops.cs' DesktopApps, for
// Avalonia and touch): the pretend windows (DeskArea: on a wide screen windows you drag by their title bar, on a phone
// each window fills the desktop with a big close button), the light-look form pieces of the pretend systems (Lt), small
// app pictures drawn in code (Glyphs), and the apps every desktop can open (a terminal, Printers, the Wi-Fi list).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using NetworkLab;
using LearningLab.Views;

namespace LearningLab.NetworkLab;

/// <summary>A window inside a pretend desktop: a title bar (drag it on a wide screen), a close button and its content.</summary>
public sealed class InnerWindow : Border
{
    public string Id { get; }
    public string Title => TitleText.Text;
    public readonly ContentControl Body = new();
    public readonly TextBlock TitleText;
    public event Action Closing;
    public double WantW, WantH;
    readonly DeskArea host;
    readonly Border bar;
    Point grab;
    bool dragging;

    public InnerWindow(DeskArea host, string id, string title, double w, double h, string style, Control content)
    {
        this.host = host;
        Id = id; WantW = w; WantH = h;
        bool mac = style == "mac", rounded = style == "win11" || mac || style == "linux";
        CornerRadius = new CornerRadius(rounded ? 8 : 0);
        BorderThickness = new Thickness(1);
        BorderBrush = Ui.Brush(mac ? "#9CA3AF" : "#6B7280");
        Background = Ui.Brush("#FFFFFF");
        ClipToBounds = true;
        BoxShadow = BoxShadows.Parse("0 3 18 0 #73000000");
        var dp = new DockPanel { LastChildFill = true };
        string barBg = style == "linux" ? "#303030" : mac ? "#ECECEC" : style == "win11" ? "#F3F3F3" : "#FFFFFF";
        string barFg = style == "linux" ? "#E5E7EB" : "#111827";
        var row = new DockPanel { Height = 44 };
        bar = new Border { Background = Ui.Brush(barBg), Child = row };
        DockPanel.SetDock(bar, Dock.Top);
        if (mac)
        {
            var dots = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var red = Lt.Plain(new Ellipse { Width = 14, Height = 14, Fill = Ui.Brush("#F87171") }, Close);
            red.Width = 44; red.Height = 44;
            ToolTip.SetTip(red, "Close");
            dots.Children.Add(red);
            dots.Children.Add(new Ellipse { Width = 14, Height = 14, Fill = Ui.Brush("#FBBF24"), Margin = new Thickness(0, 0, 8, 0) });
            dots.Children.Add(new Ellipse { Width = 14, Height = 14, Fill = Ui.Brush("#4ADE80") });
            DockPanel.SetDock(dots, Dock.Left);
            row.Children.Add(dots);
        }
        else
        {
            var close = Lt.Plain(new TextBlock { Text = "X", FontSize = 15, Foreground = Ui.Brush(barFg), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, Close);
            close.Width = 48; close.Height = 44;
            ToolTip.SetTip(close, "Close");
            DockPanel.SetDock(close, Dock.Right);
            row.Children.Add(close);
        }
        TitleText = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 8, 0), Foreground = Ui.Brush(barFg), FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap };
        row.Children.Add(TitleText);
        bar.PointerPressed += (s, e) =>
        {
            Front();
            if (host.Compact) return;
            dragging = true; grab = e.GetPosition(this); e.Pointer.Capture(bar); e.Handled = true;
        };
        bar.PointerMoved += (s, e) =>
        {
            if (!dragging) return;
            var p = e.GetPosition(host);
            Canvas.SetLeft(this, Math.Max(-Width + 80, Math.Min(host.Bounds.Width - 60, p.X - grab.X)));
            Canvas.SetTop(this, Math.Max(0, Math.Min(host.Bounds.Height - 44, p.Y - grab.Y)));
        };
        bar.PointerReleased += (s, e) => { dragging = false; e.Pointer.Capture(null); };
        bar.PointerCaptureLost += (s, e) => dragging = false;
        Body.Content = content;
        dp.Children.Add(bar);
        dp.Children.Add(Lt.Scope(Body));
        Child = dp;
        AddHandler(PointerPressedEvent, (s, e) => Front(), Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    public void Close() { Closing?.Invoke(); host.Remove(this); }
    public void Front() => host.Front(this);
}

/// <summary>The desktop's area for pretend windows. Wide: windows you can drag, each at its own size. Narrow (a phone):
/// every window fills the area (the newest on top), with a 44 px title bar and close button.</summary>
public sealed class DeskArea : Canvas
{
    public const double CompactBelow = 700;
    readonly Dictionary<string, InnerWindow> open = new();
    int z = 10;
    public bool Compact => Bounds.Width > 0 && Bounds.Width < CompactBelow || Bounds.Height > 0 && Bounds.Height < 380;
    public IEnumerable<InnerWindow> Windows => Children.OfType<InnerWindow>();
    public InnerWindow Top => Windows.OrderByDescending(w => w.ZIndex).FirstOrDefault();
    public event Action Changed;

    public DeskArea()
    {
        ClipToBounds = true;
        SizeChanged += (s, e) => Place();
    }

    public InnerWindow Find(string id) => open.TryGetValue(id, out var w) && Children.Contains(w) ? w : null;

    public InnerWindow Open(string id, string title, double w, double h, string style, Func<Control> make, double left = 110, double top = 20)
    {
        var iw = Find(id);
        if (iw != null) { iw.Front(); return iw; }
        var content = make();
        iw = new InnerWindow(this, id, title, w, h, style, content);
        int n = Windows.Count();
        Canvas.SetLeft(iw, left + n * 26); Canvas.SetTop(iw, top + n * 22);
        Children.Add(iw);
        open[id] = iw;
        if (content is NetTerminal nt)
        {
            nt.ClosedByUser += () => iw.Close();
            Avalonia.Threading.Dispatcher.UIThread.Post(nt.FocusInput, Avalonia.Threading.DispatcherPriority.Input);
        }
        Front(iw);
        Place();
        Changed?.Invoke();
        return iw;
    }

    public void Remove(InnerWindow w) { Children.Remove(w); Changed?.Invoke(); }
    public void Front(InnerWindow w) { w.ZIndex = ++z; }
    public void CloseAll() { foreach (var w in Windows.ToList()) w.Close(); }

    void Place()
    {
        double aw = Bounds.Width, ah = Bounds.Height;
        if (aw <= 0 || ah <= 0) return;
        foreach (var w in Windows)
        {
            if (Compact) { Canvas.SetLeft(w, 0); Canvas.SetTop(w, 0); w.Width = aw; w.Height = ah; w.CornerRadius = new CornerRadius(0); continue; }
            w.Width = Math.Min(w.WantW, aw - 16); w.Height = Math.Min(w.WantH, ah - 8);
            double l = Canvas.GetLeft(w), t = Canvas.GetTop(w);
            if (double.IsNaN(l) || l + w.Width > aw) Canvas.SetLeft(w, Math.Max(0, aw - w.Width - 8));
            if (double.IsNaN(t) || t + w.Height > ah) Canvas.SetTop(w, Math.Max(0, ah - w.Height - 4));
        }
    }
}

/// <summary>Small original app pictures for the pretend desktops (folder, globe, terminal, gear, printer...).</summary>
public static class Glyphs
{
    static IBrush Lin(string a, string b, bool vertical = true) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = vertical ? new RelativePoint(0, 1, RelativeUnit.Relative) : new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.Parse(a), 0), new GradientStop(Color.Parse(b), 1) },
    };

    public static Control App(string id, double size)
    {
        var g = new Grid { Width = size, Height = size };
        double s = size;
        switch (id)
        {
            case "explorer":
                g.Children.Add(new Border { Width = s * 0.42, Height = s * 0.2, CornerRadius = new CornerRadius(s * 0.05), Background = Ui.Brush("#F59E0B"), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(s * 0.08, s * 0.16, 0, 0) });
                g.Children.Add(new Border { Width = s * 0.84, Height = s * 0.56, CornerRadius = new CornerRadius(s * 0.07), Background = Lin("#FDE68A", "#F59E0B"), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, s * 0.14) });
                break;
            case "browser":
                g.Children.Add(new Ellipse { Width = s * 0.8, Height = s * 0.8, Fill = Lin("#60A5FA", "#1D4ED8") });
                g.Children.Add(new Ellipse { Width = s * 0.36, Height = s * 0.8, Stroke = Brushes.White, StrokeThickness = Math.Max(1, s * 0.05) });
                g.Children.Add(new Rectangle { Width = s * 0.8, Height = Math.Max(1, s * 0.05), Fill = Brushes.White });
                break;
            case "cmd": case "terminal": case "powershell":
                {
                    var bg = id == "powershell" ? "#1E3A8A" : "#111827";
                    g.Children.Add(new Border { Width = s * 0.84, Height = s * 0.7, CornerRadius = new CornerRadius(s * 0.08), Background = Ui.Brush(bg), BorderBrush = Ui.Brush("#6B7280"), BorderThickness = new Thickness(1) });
                    g.Children.Add(new TextBlock { Text = ">_", Foreground = Ui.Brush(id == "terminal" ? "#4ADE80" : "#E5E7EB"), FontFamily = new FontFamily("Cascadia Mono, Consolas, Menlo, DejaVu Sans Mono, monospace"), FontSize = s * 0.3, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                    break;
                }
            case "settings": case "control":
                {
                    var c = id == "control" ? "#64748B" : "#6B7280";
                    g.Children.Add(new Ellipse { Width = s * 0.78, Height = s * 0.78, Fill = Ui.Brush(c) });
                    for (int i = 0; i < 6; i++)
                        g.Children.Add(new Rectangle { Width = s * 0.16, Height = s * 0.9, Fill = Ui.Brush(c), RenderTransformOrigin = RelativePoint.Center, RenderTransform = new RotateTransform(i * 30) });
                    g.Children.Add(new Ellipse { Width = s * 0.3, Height = s * 0.3, Fill = Ui.Brush("#E5E7EB") });
                    break;
                }
            case "printers":
                g.Children.Add(new Border { Width = s * 0.46, Height = s * 0.3, Background = Brushes.White, BorderBrush = Ui.Brush("#9CA3AF"), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, s * 0.1, 0, 0) });
                g.Children.Add(new Border { Width = s * 0.84, Height = s * 0.38, CornerRadius = new CornerRadius(s * 0.06), Background = Ui.Brush("#FB7185"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, s * 0.1, 0, 0) });
                break;
            case "wifi":
                for (int i = 3; i >= 1; i--)
                    g.Children.Add(new Arc { Width = s * 0.28 * i, Height = s * 0.28 * i, StartAngle = 225, SweepAngle = 90, Stroke = Ui.Brush("#10B981"), StrokeThickness = Math.Max(1.5, s * 0.07), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, s * 0.28 * i * 0.5, 0, 0) });
                g.Children.Add(new Ellipse { Width = s * 0.12, Height = s * 0.12, Fill = Ui.Brush("#10B981"), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, s * 0.18) });
                break;
            case "ip":
                g.Children.Add(new Border { Width = s * 0.84, Height = s * 0.6, CornerRadius = new CornerRadius(s * 0.1), Background = Ui.Brush("#38BDF8") });
                g.Children.Add(new TextBlock { Text = "IP", FontSize = s * 0.32, FontWeight = FontWeight.Bold, Foreground = Ui.Brush("#0B1220"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                break;
            case "start":
                {
                    g.Children.Add(new Border { Width = s * 0.84, Height = s * 0.84, CornerRadius = new CornerRadius(s * 0.24), Background = Lin("#38BDF8", "#2563EB") });
                    var bars = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                    for (int i = 0; i < 3; i++) bars.Children.Add(new Border { Width = s * 0.46, Height = Math.Max(1.5, s * 0.08), Background = Brushes.White, Margin = new Thickness(0, s * 0.04), CornerRadius = new CornerRadius(1) });
                    g.Children.Add(bars);
                    break;
                }
            default:
                g.Children.Add(new Border { Width = s * 0.8, Height = s * 0.8, CornerRadius = new CornerRadius(s * 0.16), Background = Ui.Brush("#64748B") });
                break;
        }
        return g;
    }
}

/// <summary>Light-look form pieces for the pretend operating systems' windows (their own drawing, never a real system's).</summary>
public static class Lt
{
    public const string FgC = "#1F2937", DimC = "#6B7280";
    public static readonly IBrush Fg = Ui.Brush(FgC), Dim = Ui.Brush(DimC);

    /// <summary>A light area: Avalonia's light controls inside, and dark text unless a piece says otherwise.</summary>
    public static ThemeVariantScope Scope(Control child)
    {
        var sc = new ThemeVariantScope { RequestedThemeVariant = ThemeVariant.Light, Child = child };
        sc.Styles.Add(new Style(x => x.OfType<TextBlock>()) { Setters = { new Setter(TextBlock.ForegroundProperty, Fg) } });
        return sc;
    }

    public static TextBlock T(string s, double size, bool bold) => new() { Text = s, FontSize = size, FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal, Foreground = Fg, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
    public static TextBlock Small(string s) { var t = T(s, 12.5, false); t.Foreground = Dim; return t; }
    public static TextBlock Err() { var t = T("", 13, false); t.Foreground = Ui.Brush("#B91C1C"); return t; }

    public static TextBox Box(string value, double w = 240) => new()
    {
        Text = value ?? "", Width = w, MaxWidth = 9999, HorizontalAlignment = HorizontalAlignment.Left, MinHeight = 40, Padding = new Thickness(6, 8),
        Background = Brushes.White, Foreground = Fg, BorderBrush = Ui.Brush("#9CA3AF"), CaretBrush = Fg, SelectionBrush = Ui.Brush("#93C5FD"),
    };
    public static TextBox Secret(double w = 240) { var b = Box("", w); b.PasswordChar = '*'; return b; }

    static void Hover(Button b, string bg, string fg)
    {
        b.Resources["ButtonBackgroundPointerOver"] = Ui.Brush(bg, 220);
        b.Resources["ButtonBackgroundPressed"] = Ui.Brush(bg, 180);
        b.Resources["ButtonForegroundPointerOver"] = Ui.Brush(fg);
        b.Resources["ButtonForegroundPressed"] = Ui.Brush(fg);
        b.Resources["ButtonBorderBrushPointerOver"] = Ui.Brush(bg);
        b.Resources["ButtonBorderBrushPressed"] = Ui.Brush(bg);
    }

    static Button Make(string text, Action a, string bg, string border, string fg)
    {
        var b = new Button
        {
            Content = new TextBlock { Text = text, Foreground = Ui.Brush(fg), FontSize = 14 },
            MinHeight = 44, MinWidth = 72, Padding = new Thickness(16, 6), Margin = new Thickness(0, 0, 8, 8),
            Background = Ui.Brush(bg), BorderBrush = Ui.Brush(border), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
        };
        Hover(b, bg, fg);
        b.Click += (s, e) => Safe(a);
        return b;
    }

    public static Button Btn(string text, Action a) => Make(text, a, "#E5E7EB", "#9CA3AF", FgC);
    public static Button Primary(string text, Action a, string color) => Make(text, a, color, color, "#FFFFFF");

    /// <summary>A text link: 44 px high to tap.</summary>
    public static Button Link(string text, Action a, string color = "#1D4ED8")
    {
        var b = new Button
        {
            Content = new TextBlock { Text = text, Foreground = Ui.Brush(color), FontSize = 14, TextWrapping = TextWrapping.Wrap },
            MinHeight = 44, Padding = new Thickness(2, 4), Margin = new Thickness(0, 0, 14, 0), Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left, HorizontalContentAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Center,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        Hover(b, "#DBEAFE", color);
        b.Click += (s, e) => Safe(a);
        return b;
    }

    /// <summary>A plain tappable piece (an icon, a list row): no look of its own.</summary>
    public static Button Plain(Control content, Action a, string hover = "#FFFFFF")
    {
        var b = new Button
        {
            Content = content, Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), MinHeight = 44,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Center, Cursor = new Cursor(StandardCursorType.Hand),
        };
        b.Resources["ButtonBackgroundPointerOver"] = Ui.Brush(hover, 40);
        b.Resources["ButtonBackgroundPressed"] = Ui.Brush(hover, 70);
        b.Resources["ButtonBorderBrushPointerOver"] = Brushes.Transparent;
        b.Resources["ButtonBorderBrushPressed"] = Brushes.Transparent;
        b.Click += (s, e) => Safe(a);
        return b;
    }

    /// <summary>A row of the light settings lists: a white card you tap.</summary>
    public static Button Row(Control content, Action a, double radius = 6)
    {
        var b = Plain(content, a, "#93C5FD");
        b.Background = Brushes.White; b.BorderBrush = Ui.Brush("#E5E7EB"); b.BorderThickness = new Thickness(1); b.CornerRadius = new CornerRadius(radius);
        b.Padding = new Thickness(14, 10); b.Margin = new Thickness(0, 0, 0, 8); b.HorizontalAlignment = HorizontalAlignment.Stretch;
        return b;
    }

    /// <summary>A label beside its box on a wide screen, above it on a narrow one (a WrapPanel does both).</summary>
    public static Control FieldRow(string label, Control field, double labelW)
    {
        var l = T(label, 13.5, false); l.Width = labelW; l.VerticalAlignment = VerticalAlignment.Center; l.Margin = new Thickness(0, 0, 8, 2);
        field.Margin = new Thickness(0);
        return new WrapPanel { Margin = new Thickness(0, 0, 0, 8), Children = { l, field } };
    }

    public static CheckBox Check(string text, bool on) => new() { Content = new TextBlock { Text = text, Foreground = Fg, TextWrapping = TextWrapping.Wrap }, IsChecked = on, MinHeight = 44, Margin = new Thickness(0, 0, 0, 2) };
    public static RadioButton Radio(string text, bool on, string group) => new() { Content = new TextBlock { Text = text, Foreground = Fg, TextWrapping = TextWrapping.Wrap }, IsChecked = on, GroupName = group, MinHeight = 44, Margin = new Thickness(0, 0, 0, 2) };

    public static ComboBox Combo(double w, params string[] items)
    {
        var c = new ComboBox { Width = w, MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left, ItemsSource = items, SelectedIndex = 0, Margin = new Thickness(0, 0, 0, 8) };
        return c;
    }

    public static StackPanel Buttons(params Control[] items) { var w = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) }; foreach (var i in items) w.Children.Add(i); return new StackPanel { Children = { w } }; }

    public static string IpOrEmpty(uint v) => v == 0 ? "" : Ip.Str(v);

    /// <summary>Applies manual IP settings typed in a pretend system's window. mask can be 255.255.255.0 or 24.</summary>
    public static string ApplyManual(Device d, IpIf f, string ip, string mask, string gw, string dns1, string dns2)
    {
        ip ??= ""; mask ??= ""; gw ??= ""; dns1 ??= ""; dns2 ??= "";
        if (ip.Trim().Length == 0) return "Enter a valid IP address.";
        if (mask.Trim().Length == 0) return "Enter a subnet mask (or prefix length).";
        string err = d.Host.SetStatic(f, ip.Trim(), mask.Trim(), gw.Trim(), dns1.Trim(), dns2.Trim());
        if (err.Length == 0) { d.P.Sim.TopologyChanged(); DevicePage.Changed(); }
        return err;
    }

    public static void Safe(Action a)
    {
        try { a(); }
        catch (Exception ex) { MainView.Current?.Message("That didn't work", ex.Message); }
    }

    public static ScrollViewer Scroll(Control c, string bg = null) => new() { Content = c, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, Background = bg == null ? null : Ui.Brush(bg) };

    /// <summary>The pretend clock: 9:00 on the first of January, plus the simulation's time.</summary>
    public static DateTime Clock(Device d) => new DateTime(2026, 1, 1, 9, 0, 0).AddMilliseconds(d.P.Sim.Now);
}

/// <summary>Apps every desktop can open.</summary>
public static class DesktopApps
{
    public static NetTerminal Prompt(Device d, bool powershell)
    {
        var sh = Personalities.Get(d.Os).NewShell(d) ?? new WinShell(d, false);
        if (sh is WinShell ws && powershell) { ws.PowerShell = true; ws.Title = "Windows PowerShell"; }
        string bg = "#0C0C0C", fg = "#CCCCCC";
        if (powershell) { bg = "#012456"; fg = "#EEEDF0"; }
        var fam = Personalities.Get(d.Os).Family;
        if (fam == "linux") { bg = "#1D0F1F"; fg = "#E8E6E3"; }
        if (fam == "mac") { bg = "#1E1E1E"; fg = "#E6E6E6"; }
        if (fam == "generic") { bg = "#0B0F14"; fg = "#E5E7EB"; }
        return new NetTerminal(new ShellBackend(sh), bg, fg);
    }

    /// <summary>The terminal program a PC uses over a console cable to a router or switch.</summary>
    public static Control Terminal(Device d)
    {
        var port = d.Ports.FirstOrDefault(p => p.Media == "rs232");
        var l = port?.Link;
        if (l == null)
        {
            var t = Ui.Text("Connect a console cable from this computer's RS-232 port to the Console port of a router or switch, then open Terminal to type commands on it, as if you were standing next to it.", 14, Ui.Muted);
            return new Border { Padding = new Thickness(16), Child = new StackPanel { Children = { Ui.Text("Terminal", 17, Ui.AccentText, true), t } } };
        }
        var dev = l.Other(port).Dev;
        var s = new CliSession(dev);
        s.Print("Terminal: 9600 bits per second, 8 data bits, no parity, 1 stop bit (console cable to " + dev.Name + ")\n\nPress RETURN to get started!\n");
        s.Exited = true;
        return new NetTerminal(new CliBackend(s), "#0B0F14", "#E5E7EB");
    }

    /// <summary>Printers: the list, Print a test page, add one by its address or name, find printers on this network.</summary>
    public sealed class PrintersApp : StackPanel
    {
        readonly Device D;
        readonly bool light;
        readonly StackPanel list = new();
        readonly TextBlock status;
        readonly StackPanel found = new();
        public string Selected { get; private set; }
        public readonly TextBox Address, NameBox;

        TextBlock T(string s, double size, bool bold) { var t = Lt.T(s, size, bold); if (!light) t.Foreground = Ui.Brush(Ui.TextC); return t; }
        Button B(string s, Action a, bool primary = false) { if (light) return primary ? Lt.Primary(s, a, "#2563EB") : Lt.Btn(s, a); var b = Ui.Button(s, primary, 14); b.Margin = new Thickness(0, 0, 8, 8); b.HorizontalAlignment = HorizontalAlignment.Left; b.Click += (x, y) => Lt.Safe(a); return b; }

        public PrintersApp(Device d, bool light)
        {
            D = d; this.light = light;
            Margin = new Thickness(14);
            status = T("", 13.5, false);
            Children.Add(T("Printers", 20, true));
            Children.Add(list);
            Children.Add(B("Print a test page", PrintTest, true));
            Children.Add(status);
            Children.Add(T("Add a printer using an IP address or name", 15, true));
            Address = light ? Lt.Box("", 220) : new TextBox { Width = 220, MinHeight = 40, HorizontalAlignment = HorizontalAlignment.Left };
            NameBox = light ? Lt.Box("Printer", 220) : new TextBox { Width = 220, MinHeight = 40, Text = "Printer", HorizontalAlignment = HorizontalAlignment.Left };
            ToolTip.SetTip(Address, "The printer's IP address (for example 192.168.1.60) or its name");
            Children.Add(Pair("Address", Address));
            Children.Add(Pair("Name on this computer", NameBox));
            Children.Add(B("Add", () => Add(Address.Text, NameBox.Text)));
            Children.Add(B("Find printers on my network", Find));
            Children.Add(found);
            Fill();
        }

        Control Pair(string label, Control box) { var l = T(label, 13, false); l.Margin = new Thickness(0, 4, 0, 2); return new StackPanel { Children = { l, box } }; }

        void Fill()
        {
            list.Children.Clear();
            foreach (var p in D.Host.Printers)
            {
                var pp = p;
                bool sel = Selected == p[0];
                var row = new TextBlock { Text = (sel ? "> " : "") + p[0] + "   (" + p[1] + ")", Foreground = light ? Lt.Fg : Ui.Brush(Ui.TextC), FontWeight = sel ? FontWeight.SemiBold : FontWeight.Normal, Margin = new Thickness(10, 0) };
                var b = light ? Lt.Row(row, () => { Selected = pp[0]; Fill(); }) : Lt.Plain(row, () => { Selected = pp[0]; Fill(); });
                if (sel) b.Background = Ui.Brush(light ? "#DBEAFE" : Ui.AccentSoft);
                b.Margin = new Thickness(0, 0, 0, 4);
                list.Children.Add(b);
            }
            if (D.Host.Printers.Count == 0) list.Children.Add(T("No printers yet.", 13.5, false));
        }

        public void Select(string name) { Selected = name; Fill(); }

        public void PrintTest()
        {
            var p = D.Host.Printers.FirstOrDefault(x => x[0] == Selected) ?? (D.Host.Printers.Count == 1 ? D.Host.Printers[0] : null);
            if (p == null) { status.Text = "Pick a printer first."; return; }
            status.Text = "Sending the test page to " + p[0] + "...";
            Request.Print(D, p[1], "Test page from " + D.Name, (ok, err) =>
            {
                status.Text = ok ? "The test page was sent to " + p[0] + "." : "The printer didn't answer (" + (err.StartsWith("name") ? "its name couldn't be found" : err == "refused" ? "it refused" : "no answer") + "). Check its address, the cable, and this computer's IP settings.";
                DevicePage.Changed();
            });
        }

        public string Status => status.Text;

        public void Add(string address, string name)
        {
            address = (address ?? "").Trim(); name = (name ?? "").Trim();
            if (address.Length == 0) { status.Text = "Type the printer's address or name first."; return; }
            if (name.Length == 0) name = address;
            D.Host.Printers.RemoveAll(x => x[0] == name);
            D.Host.Printers.Add(new[] { name, address });
            Selected = name;
            Fill();
            status.Text = "Added. Now print a test page.";
            DevicePage.Changed();
        }

        void Find()
        {
            found.Children.Clear();
            found.Children.Add(T("Looking...", 13.5, false));
            Request.DiscoverPrinters(D, l =>
            {
                found.Children.Clear();
                if (l.Count == 0) found.Children.Add(T("No printers answered. Looking only reaches printers in this computer's own network.", 13.5, false));
                foreach (var p in l)
                {
                    var pp = p;
                    found.Children.Add(B("Add " + p[0] + " (" + p[1] + ", " + p[2] + ")", () => { D.Host.Printers.RemoveAll(x => x[0] == pp[0]); D.Host.Printers.Add(new[] { pp[0], pp[2] }); Selected = pp[0]; Fill(); DevicePage.Changed(); }));
                }
            });
        }
    }

    /// <summary>The Wi-Fi networks in range as rows to tap (with signal bars). askKey(ssid) asks for a password.</summary>
    public static Control WifiList(DevicePage w, Device d, bool light, Action<string> askKey)
    {
        var sp = new StackPanel { Margin = new Thickness(4) };
        var radio = d.Host.Adapters.FirstOrDefault(f => f.Port.IsWireless);
        IBrush fg = light ? Lt.Fg : Ui.Brush(Ui.TextC), dim = light ? Lt.Dim : Ui.Brush(Ui.Muted);
        if (radio == null) { sp.Children.Add(new TextBlock { Text = "No Wi-Fi on this device.", Foreground = fg }); return sp; }
        var wc = d.Host.WifiFor(radio.Port);
        var status = new TextBlock { Foreground = fg, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        var list = new StackPanel();
        sp.Children.Add(status);
        sp.Children.Add(list);
        string last = null;
        void Update()
        {
            status.Text = wc.StatusText;
            var nets = d.P.Devices.Where(x => x.Ap != null && x.Ap.Enabled && x.PoweredOn).Select(x => new { ap = x, s = Physical.Signal(d.P, x, d) }).Where(x => x.s > 0).GroupBy(x => x.ap.Ap.Ssid).Select(g => g.OrderByDescending(x => x.s).First()).ToList();
            string sig = string.Join("|", nets.Select(n => n.ap.Ap.Ssid + (int)(n.s * 4))) + wc.Status + wc.Ssid;
            if (sig == last) return;
            last = sig;
            list.Children.Clear();
            foreach (var n in nets)
            {
                var ssid = n.ap.Ap.Ssid;
                bool secured = n.ap.Ap.Security != "open";
                bool connected = wc.Status == "connected" && wc.Ssid == ssid;
                var row = new DockPanel { Margin = new Thickness(10, 0) };
                var bars = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
                int level = 1 + (int)(n.s * 3.99);
                for (int i = 0; i < 4; i++) bars.Children.Add(new Border { Width = 4, Height = 4 + i * 3, Margin = new Thickness(0, 0, 2, 0), VerticalAlignment = VerticalAlignment.Bottom, Background = i < level ? (light ? Ui.Brush("#1F2937") : Ui.Brush(Ui.TextC)) : Ui.Brush("#9CA3AF") });
                DockPanel.SetDock(bars, Dock.Left);
                row.Children.Add(bars);
                var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                words.Children.Add(new TextBlock { Text = ssid, Foreground = fg, FontWeight = connected ? FontWeight.SemiBold : FontWeight.Normal });
                words.Children.Add(new TextBlock { Text = (secured ? "Secured" : "Open") + (connected ? ", connected (tap to disconnect)" : ""), Foreground = dim, FontSize = 12.5 });
                row.Children.Add(words);
                var b = light ? Lt.Row(row, () => Tap(ssid, secured, connected)) : Lt.Plain(row, () => Tap(ssid, secured, connected));
                if (light && connected) b.Background = Ui.Brush("#EFF6FF");
                b.Margin = new Thickness(0, 0, 0, 4);
                b.Padding = new Thickness(0, 8);
                b.Tag = "wifi:" + ssid;
                list.Children.Add(b);
            }
            if (nets.Count == 0) list.Children.Add(new TextBlock { Text = "No Wi-Fi networks in range.", Foreground = dim });
        }
        void Tap(string ssid, bool secured, bool connected)
        {
            if (connected) { d.Host.SetWifi(radio.Port, "", ""); DevicePage.Changed(); last = null; Update(); return; }
            if (!secured) { Join(d, ssid, ""); last = null; Update(); return; }
            askKey(ssid);
        }
        w.OnRefresh(Update);
        Update();
        return sp;
    }

    /// <summary>Joins a Wi-Fi network with a key (DHCP unless the adapter is set to a static address).</summary>
    public static string Join(Device d, string ssid, string key)
    {
        var radio = d.Host.Adapters.FirstOrDefault(f => f.Port.IsWireless);
        if (radio == null) return "No Wi-Fi";
        d.Host.SetWifi(radio.Port, ssid, key ?? "");
        if (radio.Ip == 0 && !radio.Dhcp) d.Host.SetDhcp(radio); else if (radio.Dhcp) d.Host.EnsureDhcp(radio);
        DevicePage.Changed();
        return d.Host.WifiFor(radio.Port).Status;
    }
}
