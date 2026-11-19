// Network Lab - a pretend Windows-style desktop (App Hub's WinDesktop.cs; a 10-like or 11-like look): Start menu,
// taskbar, Settings > Network & internet (adapters, Edit IP settings, Wi-Fi, Printers & scanners, the firewall), the
// classic Control Panel (adapters, the IPv4 properties form, Windows Defender Firewall), Command Prompt, PowerShell,
// File Explorer (\\server\share) and a browser. Original drawings and wording. Windows are dragged on a wide screen
// and fill the desktop on a phone (DeskArea).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using NetworkLab;
using LearningLab.Views;

namespace LearningLab.NetworkLab;

public sealed class WinDesktop : Grid, IDesktop
{
    readonly DevicePage W;
    readonly Device D;
    readonly bool eleven;
    public readonly DeskArea Desk = new();
    readonly Border startMenu;
    readonly TextBlock clock, net;
    readonly Border search;
    string WinStyle => eleven ? "win11" : "win10";

    public WinDesktop(DevicePage w, Device d, bool win11)
    {
        W = w; D = d; eleven = win11;
        RowDefinitions = new RowDefinitions("*,48");
        // wallpaper: soft shapes of our own
        var wall = new Grid { ClipToBounds = true };
        if (eleven)
        {
            wall.Background = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative), GradientStops = { new GradientStop(Color.Parse("#9FB8E8"), 0), new GradientStop(Color.Parse("#5B6FB8"), 1) } };
            wall.Children.Add(new Ellipse { Width = 520, Height = 520, Opacity = 0.8, Fill = new RadialGradientBrush { GradientStops = { new GradientStop(Color.Parse("#C7D8F8"), 0), new GradientStop(Color.Parse("#00C7D8F8"), 1) } } });
            wall.Children.Add(new Ellipse { Width = 300, Height = 300, Opacity = 0.7, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 60, 30), Fill = new RadialGradientBrush { GradientStops = { new GradientStop(Color.Parse("#E9D5FF"), 0), new GradientStop(Color.Parse("#00E9D5FF"), 1) } } });
        }
        else
        {
            wall.Background = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0.4, 1, RelativeUnit.Relative), GradientStops = { new GradientStop(Color.Parse("#0B3A6E"), 0), new GradientStop(Color.Parse("#05162E"), 1) } };
            wall.Children.Add(new Polygon { Points = new List<Point> { new(0, 120), new(1400, 330), new(1400, 420), new(0, 200) }, Fill = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative), GradientStops = { new GradientStop(Color.Parse("#7860A5FA"), 0), new GradientStop(Color.Parse("#0060A5FA"), 1) } } });
        }
        // desktop shortcuts
        var shortcuts = new StackPanel { Margin = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        foreach (var sc in new[] { new[] { "browser", "Web browser" }, new[] { "explorer", "File Explorer" }, new[] { "control", "Control Panel" } })
        {
            var id = sc[0];
            var sp = new StackPanel { Width = 84 };
            var gl = Glyphs.App(id, 42); gl.HorizontalAlignment = HorizontalAlignment.Center;
            sp.Children.Add(gl);
            sp.Children.Add(new TextBlock { Text = sc[1], Foreground = Brushes.White, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Margin = new Thickness(0, 4, 0, 0) });
            var b = Lt.Plain(sp, () => OpenApp(id));
            b.Margin = new Thickness(0, 0, 0, 10); b.Padding = new Thickness(0, 6);
            shortcuts.Children.Add(b);
        }
        wall.Children.Add(shortcuts);
        Children.Add(wall);
        Children.Add(Desk);
        // taskbar
        var bar = new Grid { Background = Ui.Brush(eleven ? "#E6EAF2" : "#101318") };
        SetRow(bar, 1);
        Children.Add(bar);
        var apps = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = eleven ? HorizontalAlignment.Center : HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(eleven ? 0 : 4, 0, 0, 0) };
        var start = TaskButton(Glyphs.App("start", 28), "Start", () => startMenu.IsVisible = !startMenu.IsVisible);
        apps.Children.Add(start);
        search = new Border { Width = 200, Height = 34, Background = Ui.Brush("#2B2F36"), Margin = new Thickness(4, 0, 8, 0), Child = new TextBlock { Text = "  Type here to search", Foreground = Ui.Brush("#9CA3AF"), VerticalAlignment = VerticalAlignment.Center }, IsVisible = !eleven };
        apps.Children.Add(search);
        foreach (var a in new[] { new[] { "explorer", "File Explorer" }, new[] { "browser", "Web browser" }, new[] { "cmd", "Command Prompt" }, new[] { "settings", "Settings" } })
        {
            var id = a[0];
            apps.Children.Add(TaskButton(Glyphs.App(id, 28), a[1], () => OpenApp(id)));
        }
        bar.Children.Add(apps);
        var tray = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        string trayFg = eleven ? "#1F2937" : "#E5E7EB";
        net = new TextBlock { Foreground = Ui.Brush(trayFg), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 110 };
        var netBtn = Lt.Plain(net, () => OpenApp(D.Host.Wifi.Count > 0 ? "wifi" : "settings"));
        netBtn.Padding = new Thickness(6, 0);
        clock = new TextBlock { Foreground = Ui.Brush(trayFg), FontSize = 12, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        tray.Children.Add(netBtn); tray.Children.Add(clock);
        bar.Children.Add(tray);
        // start menu
        startMenu = BuildStart();
        Children.Add(startMenu);
        Desk.Changed += () => startMenu.IsVisible = false;
        SizeChanged += (s, e) => Fit(e.NewSize.Width);
        w.OnRefresh(RefreshTray);
        RefreshTray();
    }

    void Fit(double width)
    {
        bool narrow = width < DeskArea.CompactBelow;
        search.IsVisible = !eleven && width >= 900;
        startMenu.Width = Math.Min(eleven ? 520 : 340, width - 8);
        clock.IsVisible = !narrow || width > 470;
    }

    Button TaskButton(Control content, string tip, Action a)
    {
        var b = Lt.Plain(content, a, eleven ? "#000000" : "#FFFFFF");
        b.Width = 44; b.Height = 44; b.Margin = new Thickness(1, 0); b.HorizontalContentAlignment = HorizontalAlignment.Center;
        ToolTip.SetTip(b, tip);
        return b;
    }

    void RefreshTray()
    {
        var t = Lt.Clock(D);
        clock.Text = t.ToString("H:mm") + "\n" + t.ToString("d/M/yyyy");
        var m = D.Host.Main;
        bool up = m != null && m.Up;
        bool internet = up && m.Ip != 0 && !m.Apipa && m.Gateway != 0;
        net.Text = !up ? "Not connected" : (m.Port.IsWireless ? "Wi-Fi: " : "") + (internet ? "Connected" : m.Apipa ? "No network access" : m.Ip == 0 ? "Identifying..." : "No gateway");
    }

    Border BuildStart()
    {
        var fg = Ui.Brush(eleven ? "#111827" : "#E5E7EB");
        var b = new Border
        {
            Width = eleven ? 520 : 340, HorizontalAlignment = eleven ? HorizontalAlignment.Center : HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom,
            Background = Ui.Brush(eleven ? "#F3F4F6" : "#1F2328"), CornerRadius = new CornerRadius(eleven ? 8 : 0), Margin = new Thickness(0, 0, 0, 4), IsVisible = false, Padding = new Thickness(14),
            BoxShadow = BoxShadows.Parse("0 2 20 0 #80000000"), ZIndex = 100000,
        };
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = eleven ? "Pinned" : "Apps", Foreground = fg, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 10) });
        var grid = new WrapPanel();
        foreach (var a in new[] { new[] { "settings", "Settings" }, new[] { "control", "Control Panel" }, new[] { "cmd", "Command Prompt" }, new[] { "powershell", "Windows PowerShell" }, new[] { "explorer", "File Explorer" }, new[] { "browser", "Web browser" }, new[] { "printers", "Printers & scanners" } })
        {
            var id = a[0];
            var item = new StackPanel { Width = eleven ? 88 : 290, Orientation = eleven ? Orientation.Vertical : Orientation.Horizontal };
            var gl = Glyphs.App(id, 30); gl.Margin = new Thickness(eleven ? 0 : 6, 0, eleven ? 0 : 10, 4); gl.HorizontalAlignment = HorizontalAlignment.Center;
            item.Children.Add(gl);
            item.Children.Add(new TextBlock { Text = a[1], Foreground = fg, FontSize = 12.5, TextAlignment = eleven ? TextAlignment.Center : TextAlignment.Left, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
            var btn = Lt.Plain(item, () => { startMenu.IsVisible = false; OpenApp(id); }, eleven ? "#000000" : "#FFFFFF");
            btn.Margin = new Thickness(0, 0, 4, 6); btn.Padding = new Thickness(2, 6);
            grid.Children.Add(btn);
        }
        sp.Children.Add(grid);
        sp.Children.Add(new TextBlock { Text = "student", Foreground = fg, Margin = new Thickness(0, 16, 0, 0), FontWeight = FontWeight.SemiBold });
        b.Child = sp;
        return b;
    }

    public bool StartOpen => startMenu.IsVisible;
    public void ToggleStart() => startMenu.IsVisible = !startMenu.IsVisible;

    public Control OpenApp(string id)
    {
        startMenu.IsVisible = false;
        InnerWindow iw = id switch
        {
            "settings" => Desk.Open(id, "Settings", 780, 540, WinStyle, () => new WinSettings(W, D, eleven)),
            "wifi" => Desk.Open(id, "Settings", 780, 540, WinStyle, () => { var ws = new WinSettings(W, D, eleven); ws.Show("wifi"); return ws; }),
            "printers" => Desk.Open(id, "Settings", 780, 540, WinStyle, () => { var ws = new WinSettings(W, D, eleven); ws.Show("printers"); return ws; }),
            "control" => Desk.Open(id, "Control Panel", 720, 500, WinStyle, () => new WinControlPanel(D)),
            "cmd" => Desk.Open(id, "Command Prompt", 700, 420, WinStyle, () => DesktopApps.Prompt(D, false)),
            "powershell" => Desk.Open(id, "Windows PowerShell", 720, 430, WinStyle, () => DesktopApps.Prompt(D, true)),
            "explorer" => Desk.Open(id, "File Explorer", 680, 440, WinStyle, () => new WinExplorer(D)),
            "browser" => Desk.Open(id, "Web browser", 760, 500, WinStyle, () => new MiniBrowser(D, true, "")),
            _ => null,
        };
        return iw?.Body.Content as Control;
    }
}

/// <summary>Settings: Network & internet (adapters, IP settings, Wi-Fi), Bluetooth & devices (printers), the firewall.
/// The menu sits on the left on a wide screen and on top on a phone.</summary>
public sealed class WinSettings : Grid
{
    readonly DevicePage W;
    readonly Device D;
    readonly bool eleven;
    readonly ContentControl page = new();
    readonly StackPanel nav = new() { Margin = new Thickness(10) };
    public string Shown { get; private set; } = "";

    public WinSettings(DevicePage w, Device d, bool win11)
    {
        W = w; D = d; eleven = win11;
        Background = Ui.Brush(eleven ? "#F3F3F3" : "#FFFFFF");
        nav.Children.Add(Lt.T("student", 14, true));
        nav.Children.Add(Lt.Small("Local account"));
        var links = new WrapPanel();
        foreach (var n in new[] { new[] { "net", "Network & internet" }, new[] { "printers", "Bluetooth & devices" }, new[] { "security", "Privacy & security" } })
        {
            var id = n[0];
            links.Children.Add(Lt.Link(n[1], () => Show(id)));
        }
        nav.Children.Add(links);
        var scroll = Lt.Scroll(page);
        Children.Add(nav); Children.Add(scroll);
        void Lay(double width)
        {
            bool narrow = width < 600;
            ColumnDefinitions = narrow ? new ColumnDefinitions("*") : new ColumnDefinitions("210,*");
            RowDefinitions = narrow ? new RowDefinitions("Auto,*") : new RowDefinitions("*");
            SetColumn(scroll, narrow ? 0 : 1); SetRow(scroll, narrow ? 1 : 0);
            links.Orientation = narrow ? Orientation.Horizontal : Orientation.Vertical;
            nav.Margin = narrow ? new Thickness(10, 4, 10, 0) : new Thickness(10);
        }
        Lay(800);
        SizeChanged += (s, e) => Lay(e.NewSize.Width);
        Show("net");
    }

    public void Show(string id)
    {
        Shown = id;
        switch (id)
        {
            case "net": page.Content = NetPage(); break;
            case "wifi": page.Content = WifiPage(); break;
            case "printers": page.Content = new DesktopApps.PrintersApp(D, true); break;
            case "security": page.Content = FirewallPage(); break;
            default:
                if (id.StartsWith("adapter:")) page.Content = AdapterPage(D.Host.Adapters[int.Parse(id.Substring(8))]);
                break;
        }
    }

    public Control Page => page.Content as Control;

    Control NetPage()
    {
        var sp = new StackPanel { Margin = new Thickness(10) };
        sp.Children.Add(Lt.T("Network & internet", 22, true));
        int i = 0;
        foreach (var f in D.Host.Adapters)
        {
            int idx = i++;
            var ff = f;
            var c = new StackPanel();
            c.Children.Add(Lt.T(Personalities.Get(D.Os).IfName(f.Port), 15, true));
            var st = Lt.Small("");
            void Upd() => st.Text = !ff.Up ? (ff.Port.IsWireless ? "Not connected" : "Network cable unplugged") : ff.Apipa ? "No internet: no DHCP server answered" : ff.Ip == 0 ? "Identifying..." : "Connected" + (ff.Gateway == 0 ? " (no default gateway)" : "");
            Upd(); W.OnRefresh(Upd);
            c.Children.Add(st);
            sp.Children.Add(Lt.Row(c, () => Show(ff.Port.IsWireless ? "wifi" : "adapter:" + idx), eleven ? 6 : 0));
            if (f.Port.IsWireless) sp.Children.Add(Lt.Link("Wi-Fi properties (IP settings)", () => Show("adapter:" + idx)));
        }
        sp.Children.Add(Lt.Small("Tap an adapter to see its properties and change its IP settings."));
        return sp;
    }

    Control AdapterPage(IpIf f)
    {
        var sp = new StackPanel { Margin = new Thickness(10) };
        string name = Personalities.Get(D.Os).IfName(f.Port);
        sp.Children.Add(Lt.Link("< Network & internet", () => Show("net")));
        sp.Children.Add(Lt.T("Network & internet  >  " + name, 20, true));
        var editArea = new ContentControl();
        var props = new StackPanel();
        void Fill()
        {
            props.Children.Clear();
            props.Children.Add(Row("IP assignment:", f.Dhcp ? "Automatic (DHCP)" : "Manual", () => editArea.Content = IpEditor(f, editArea, false)));
            props.Children.Add(Row("DNS server assignment:", f.Dhcp && (f.DhcpC == null || f.Dns.SequenceEqual(f.DhcpC.Dns)) ? "Automatic (DHCP)" : "Manual", () => editArea.Content = IpEditor(f, editArea, true)));
            props.Children.Add(Kv("Link speed (Receive/Transmit):", f.Up ? f.Port.Speed + "/" + f.Port.Speed + " (Mbps)" : ""));
            props.Children.Add(Kv("IPv4 address:", Lt.IpOrEmpty(f.Ip)));
            props.Children.Add(Kv("IPv4 mask:", Lt.IpOrEmpty(f.Mask)));
            props.Children.Add(Kv("IPv4 default gateway:", Lt.IpOrEmpty(f.Gateway)));
            props.Children.Add(Kv("IPv4 DNS servers:", string.Join(", ", f.Dns.Select(Ip.Str))));
            props.Children.Add(Kv("Physical address (MAC):", Mac.Dashed(f.Mac)));
        }
        Fill();
        string lastSig = "";
        W.OnRefresh(() => { string sig = f.Dhcp + Ip.Str(f.Ip) + f.Mask + f.Gateway + string.Join(",", f.Dns) + f.Up; if (editArea.Content == null && sig != lastSig) { lastSig = sig; Fill(); } });
        sp.Children.Add(editArea);
        sp.Children.Add(new Border { Child = props, Background = Brushes.White, Padding = new Thickness(14), CornerRadius = new CornerRadius(eleven ? 6 : 0), BorderBrush = Ui.Brush("#E5E7EB"), BorderThickness = new Thickness(1) });
        return sp;
    }

    static Control Row(string k, string v, Action edit)
    {
        var e = Lt.Btn("Edit", edit);
        e.Margin = new Thickness(0);
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { Lt.Small(k), Lt.T(v, 14, true) } };
        var dp = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(e, Dock.Right);
        dp.Children.Add(e); dp.Children.Add(words);
        return dp;
    }
    static Control Kv(string k, string v) => new StackPanel { Margin = new Thickness(0, 0, 0, 4), Children = { Lt.Small(k), Lt.T(v.Length == 0 ? "-" : v, 14, false) } };

    /// <summary>"Edit IP settings": Automatic (DHCP) or Manual with IPv4 fields. Fields are public for the checks.</summary>
    public sealed class IpEdit
    {
        public ComboBox Mode;
        public TextBox Ip, Mask, Gateway, Dns1, Dns2;
        public TextBlock Error;
        public Action Save;
    }
    public IpEdit LastEditor { get; private set; }

    public Control IpEditor(IpIf f, ContentControl area, bool dnsOnly)
    {
        var b = new Border { Background = Brushes.White, BorderBrush = Ui.Brush("#9CA3AF"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(eleven ? 8 : 0), Padding = new Thickness(16), Margin = new Thickness(0, 4, 0, 12), MaxWidth = 440, HorizontalAlignment = HorizontalAlignment.Left };
        var sp = new StackPanel();
        sp.Children.Add(Lt.T(dnsOnly ? "Edit DNS settings" : "Edit IP settings", 18, true));
        var mode = Lt.Combo(220, "Automatic (DHCP)", "Manual");
        mode.SelectedIndex = f.Dhcp ? 0 : 1;
        sp.Children.Add(mode);
        var fields = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        var ip = Lt.Box(f.Dhcp ? "" : Lt.IpOrEmpty(f.Ip), 220);
        // Windows 10 asks for the prefix length, Windows 11 for the mask; both accept either here
        var mask = Lt.Box(f.Dhcp ? "" : (f.Mask != 0 ? (eleven ? global::NetworkLab.Ip.Str(f.Mask) : global::NetworkLab.Ip.PrefixOf(f.Mask).ToString()) : ""), 220);
        var gw = Lt.Box(f.Dhcp ? "" : Lt.IpOrEmpty(f.Gateway), 220);
        var d1 = Lt.Box(f.Dns.Count > 0 ? global::NetworkLab.Ip.Str(f.Dns[0]) : "", 220);
        var d2 = Lt.Box(f.Dns.Count > 1 ? global::NetworkLab.Ip.Str(f.Dns[1]) : "", 220);
        void Add(string label, TextBox box) { fields.Children.Add(Lt.Small(label)); box.Margin = new Thickness(0, 0, 0, 6); fields.Children.Add(box); }
        if (!dnsOnly)
        {
            fields.Children.Add(Lt.T("IPv4", 14, true));
            Add("IP address", ip); Add(eleven ? "Subnet mask" : "Subnet prefix length", mask); Add("Gateway", gw);
        }
        Add("Preferred DNS", d1); Add("Alternate DNS", d2);
        sp.Children.Add(fields);
        void Vis() => fields.IsVisible = mode.SelectedIndex == 1;
        mode.SelectionChanged += (s, e) => Vis();
        Vis();
        var err = Lt.Err();
        sp.Children.Add(err);
        void Save()
        {
            if (mode.SelectedIndex == 0)
            {
                if (dnsOnly) { f.Dns.Clear(); if (f.DhcpC != null) f.Dns.AddRange(f.DhcpC.Dns); D.Host.DnsCache.Clear(); }
                else D.Host.SetDhcp(f);
                area.Content = null; DevicePage.Changed(); Show("adapter:" + D.Host.Adapters.IndexOf(f)); return;
            }
            if (dnsOnly)
            {
                uint a1 = 0, a2 = 0;
                if ((d1.Text ?? "").Trim().Length > 0 && !global::NetworkLab.Ip.TryParse(d1.Text, out a1)) { err.Text = "Enter a valid preferred DNS address."; return; }
                if ((d2.Text ?? "").Trim().Length > 0 && !global::NetworkLab.Ip.TryParse(d2.Text, out a2)) { err.Text = "Enter a valid alternate DNS address."; return; }
                f.Dns.Clear(); if (a1 != 0) f.Dns.Add(a1); if (a2 != 0) f.Dns.Add(a2);
                D.Host.DnsCache.Clear();
                area.Content = null; DevicePage.Changed(); Show("adapter:" + D.Host.Adapters.IndexOf(f)); return;
            }
            string e2 = Lt.ApplyManual(D, f, ip.Text, mask.Text, gw.Text, d1.Text, d2.Text);
            if (e2.Length > 0) { err.Text = e2; return; }
            area.Content = null;
            Show("adapter:" + D.Host.Adapters.IndexOf(f));
        }
        sp.Children.Add(Lt.Buttons(Lt.Primary("Save", Save, "#2563EB"), Lt.Btn("Cancel", () => area.Content = null)));
        LastEditor = new IpEdit { Mode = mode, Ip = ip, Mask = mask, Gateway = gw, Dns1 = d1, Dns2 = d2, Error = err, Save = Save };
        b.Child = sp;
        return b;
    }

    Control WifiPage()
    {
        var sp = new StackPanel { Margin = new Thickness(10) };
        sp.Children.Add(Lt.Link("< Network & internet", () => Show("net")));
        sp.Children.Add(Lt.T("Wi-Fi", 22, true));
        var ask = new StackPanel { IsVisible = false, Margin = new Thickness(0, 8, 0, 0) };
        var askText = Lt.T("", 14, false);
        var key = Lt.Secret(240);
        var result = Lt.T("", 13, false);
        string wanted = null;
        ask.Children.Add(askText);
        ask.Children.Add(key);
        ask.Children.Add(Lt.Buttons(Lt.Primary("Next", () =>
        {
            var st = DesktopApps.Join(D, wanted, key.Text);
            result.Text = st == "connected" ? "Connected, secured." : st == "wrong-key" ? "The network security key isn't correct. Please try again." : "Can't connect to this network.";
            if (st == "connected") ask.IsVisible = false;
        }, "#2563EB"), Lt.Btn("Cancel", () => ask.IsVisible = false)));
        var list = DesktopApps.WifiList(W, D, true, ssid => { wanted = ssid; askText.Text = "Enter the network security key for " + ssid; key.Text = ""; ask.IsVisible = true; key.Focus(); });
        sp.Children.Add(list);
        sp.Children.Add(ask);
        sp.Children.Add(result);
        return sp;
    }

    Control FirewallPage()
    {
        var sp = new StackPanel { Margin = new Thickness(10) };
        sp.Children.Add(Lt.T("Windows Security  >  Firewall & network protection", 19, true));
        sp.Children.Add(Lt.Small("A firewall decides which network traffic may come into this PC. When it's on, other computers can't ping this one unless File and Printer Sharing is allowed."));
        var on = Lt.Check("Firewall on (all network types)", D.Host.WinFirewall);
        on.IsCheckedChanged += (s, e) => { D.Host.WinFirewall = on.IsChecked == true; DevicePage.Changed(); };
        sp.Children.Add(on);
        sp.Children.Add(Lt.T("Allowed apps", 15, true));
        var share = Lt.Check("File and Printer Sharing (also answers pings)", D.Host.FwSharing);
        share.IsCheckedChanged += (s, e) => { D.Host.FwSharing = share.IsChecked == true; DevicePage.Changed(); };
        var web = Lt.Check("World Wide Web Services (HTTP)", D.Host.FwWeb);
        web.IsCheckedChanged += (s, e) => { D.Host.FwWeb = web.IsChecked == true; DevicePage.Changed(); };
        sp.Children.Add(share); sp.Children.Add(web);
        return sp;
    }
}

/// <summary>The classic Control Panel: Network and Sharing Center, adapter settings, the IPv4 properties form and
/// Windows Defender Firewall.</summary>
public sealed class WinControlPanel : DockPanel
{
    readonly Device D;
    readonly ContentControl page = new();
    readonly TextBlock crumbs = Lt.T("", 13, false);
    public string Where => crumbs.Text;

    public WinControlPanel(Device d)
    {
        D = d;
        Background = Brushes.White;
        crumbs.Margin = new Thickness(10, 8, 10, 8);
        crumbs.Foreground = Lt.Dim;
        SetDock(crumbs, Dock.Top);
        Children.Add(crumbs);
        Children.Add(Lt.Scroll(page));
        Home();
    }

    public void Home()
    {
        crumbs.Text = "Control Panel";
        var sp = new StackPanel { Margin = new Thickness(16) };
        sp.Children.Add(Lt.T("Adjust your computer's settings", 18, true));
        sp.Children.Add(Lt.Link("Network and Internet  >  Network and Sharing Center", Sharing));
        sp.Children.Add(Lt.Link("System and Security  >  Windows Defender Firewall", Firewall));
        page.Content = sp;
    }

    public void Sharing()
    {
        crumbs.Text = "Control Panel  >  Network and Internet  >  Network and Sharing Center";
        var sp = new StackPanel { Margin = new Thickness(16) };
        sp.Children.Add(Lt.Link("< Control Panel", Home));
        sp.Children.Add(Lt.T("View your basic network information and set up connections", 16, true));
        foreach (var f in D.Host.Adapters)
            sp.Children.Add(Lt.T(Personalities.Get(D.Os).IfName(f.Port) + ":  " + (f.Up ? (f.Ip != 0 && !f.Apipa ? "connected" : "Unidentified network") : "not connected"), 14, false));
        sp.Children.Add(Lt.Link("Change adapter settings", Adapters));
        page.Content = sp;
    }

    public void Adapters()
    {
        crumbs.Text = "Control Panel  >  Network and Internet  >  Network Connections";
        var sp = new StackPanel { Margin = new Thickness(16) };
        sp.Children.Add(Lt.Link("< Network and Sharing Center", Sharing));
        var wrap = new WrapPanel();
        foreach (var f in D.Host.Adapters)
        {
            var ff = f;
            var item = new StackPanel { Orientation = Orientation.Horizontal, Width = 240 };
            item.Children.Add(new DeviceIcon(f.Port.IsWireless ? "laptop" : "pc", D.Os, 40) { Margin = new Thickness(6, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
            var t = new StackPanel();
            t.Children.Add(Lt.T(Personalities.Get(D.Os).IfName(f.Port), 13.5, true));
            t.Children.Add(Lt.Small(f.Up ? (f.Ip != 0 && !f.Apipa ? "Network" : "Unidentified network") : (f.Port.IsWireless ? "Not connected" : "Network cable unplugged")));
            t.Children.Add(Lt.Small(f.Port.IsWireless ? "Simulated Wi-Fi Adapter" : "Simulated Ethernet Adapter"));
            item.Children.Add(t);
            var b = Lt.Plain(item, () => Properties(ff), "#93C5FD");
            b.Margin = new Thickness(0, 0, 12, 12); b.Padding = new Thickness(4, 6);
            wrap.Children.Add(b);
        }
        sp.Children.Add(wrap);
        sp.Children.Add(Lt.Small("Tap an adapter to open its Properties."));
        page.Content = sp;
    }

    public void Properties(IpIf f)
    {
        string name = Personalities.Get(D.Os).IfName(f.Port);
        crumbs.Text = name + " Properties";
        var sp = new StackPanel { Margin = new Thickness(16), MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Left };
        sp.Children.Add(Lt.T("Networking", 14, true));
        sp.Children.Add(Lt.Small("Connect using:  Simulated " + (f.Port.IsWireless ? "Wi-Fi" : "Ethernet") + " Adapter"));
        sp.Children.Add(Lt.Small("This connection uses the following items:"));
        var items = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
        foreach (var it in new[] { "Client for Networks", "File and Printer Sharing", "QoS Packet Scheduler", "Internet Protocol Version 4 (TCP/IPv4)", "Internet Protocol Version 6 (TCP/IPv6)", "Link-Layer Topology Discovery Responder" })
        {
            var row = new DockPanel();
            var cb = new CheckBox { IsChecked = true, MinHeight = 40, Margin = new Thickness(0, 0, 4, 0) };
            DockPanel.SetDock(cb, Dock.Left);
            row.Children.Add(cb);
            if (it.Contains("IPv4")) row.Children.Add(Lt.Link(it + "  (tap for Properties)", () => Ipv4(f)));
            else row.Children.Add(new TextBlock { Text = it, Foreground = Lt.Fg, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
            items.Children.Add(row);
        }
        sp.Children.Add(new Border { Child = items, BorderBrush = Ui.Brush("#9CA3AF"), BorderThickness = new Thickness(1), Padding = new Thickness(6, 2) });
        sp.Children.Add(Lt.Buttons(Lt.Btn("Properties", () => Ipv4(f)), Lt.Btn("Close", Adapters)));
        page.Content = sp;
    }

    /// <summary>The classic "Internet Protocol Version 4 (TCP/IPv4) Properties" form.</summary>
    public void Ipv4(IpIf f)
    {
        crumbs.Text = "Internet Protocol Version 4 (TCP/IPv4) Properties";
        var sp = new StackPanel { Margin = new Thickness(16), MaxWidth = 460, HorizontalAlignment = HorizontalAlignment.Left };
        sp.Children.Add(Lt.Small("You can get IP settings assigned automatically if your network supports this capability. Otherwise, you need to ask your network administrator for the appropriate IP settings."));
        string g1 = "ip" + Guid.NewGuid().ToString("n"), g2 = "dns" + Guid.NewGuid().ToString("n");
        var auto = Lt.Radio("Obtain an IP address automatically", f.Dhcp, g1);
        var man = Lt.Radio("Use the following IP address:", !f.Dhcp, g1);
        var ip = Lt.Box(f.Dhcp ? "" : Lt.IpOrEmpty(f.Ip), 180);
        var mask = Lt.Box(f.Dhcp ? "" : Lt.IpOrEmpty(f.Mask), 180);
        var gw = Lt.Box(f.Dhcp ? "" : Lt.IpOrEmpty(f.Gateway), 180);
        bool manualDns = !f.Dhcp || (f.DhcpC != null && !f.Dns.SequenceEqual(f.DhcpC.Dns));
        var dauto = Lt.Radio("Obtain DNS server address automatically", !manualDns, g2);
        var dman = Lt.Radio("Use the following DNS server addresses:", manualDns, g2);
        var d1 = Lt.Box(f.Dns.Count > 0 && manualDns ? Ip.Str(f.Dns[0]) : "", 180);
        var d2 = Lt.Box(f.Dns.Count > 1 && manualDns ? Ip.Str(f.Dns[1]) : "", 180);
        sp.Children.Add(auto); sp.Children.Add(man);
        var ipBox = new StackPanel { Margin = new Thickness(16, 4, 0, 8) };
        ipBox.Children.Add(Lt.FieldRow("IP address:", ip, 150));
        ipBox.Children.Add(Lt.FieldRow("Subnet mask:", mask, 150));
        ipBox.Children.Add(Lt.FieldRow("Default gateway:", gw, 150));
        sp.Children.Add(ipBox);
        sp.Children.Add(dauto); sp.Children.Add(dman);
        var dBox = new StackPanel { Margin = new Thickness(16, 4, 0, 8) };
        dBox.Children.Add(Lt.FieldRow("Preferred DNS server:", d1, 150));
        dBox.Children.Add(Lt.FieldRow("Alternate DNS server:", d2, 150));
        sp.Children.Add(dBox);
        void En()
        {
            ipBox.IsEnabled = man.IsChecked == true;
            if (man.IsChecked == true) { dman.IsChecked = true; dauto.IsEnabled = false; } else dauto.IsEnabled = true;
            dBox.IsEnabled = dman.IsChecked == true;
        }
        foreach (var r in new[] { auto, man, dauto, dman }) r.IsCheckedChanged += (s, e) => En();
        En();
        var err = Lt.Err();
        // like the real form: the mask fills in when you leave the IP box
        ip.LostFocus += (s, e) => { if ((mask.Text ?? "").Trim().Length == 0 && Ip.TryParse(ip.Text, out uint a)) mask.Text = Ip.Str(Ip.MaskOf(Ip.ClassfulPrefix(a))); };
        void Ok()
        {
            if (auto.IsChecked == true)
            {
                D.Host.SetDhcp(f);
                if (dman.IsChecked == true && Ip.TryParse(d1.Text, out uint a1)) { f.Dns.Clear(); f.Dns.Add(a1); if (Ip.TryParse(d2.Text, out uint a2)) f.Dns.Add(a2); }
                DevicePage.Changed();
                Properties(f);
                return;
            }
            string e2 = Lt.ApplyManual(D, f, ip.Text, mask.Text, gw.Text, d1.Text, d2.Text);
            if (e2.Length > 0) { err.Text = e2; return; }
            Properties(f);
        }
        sp.Children.Add(Lt.Buttons(Lt.Primary("OK", Ok, "#2563EB"), Lt.Btn("Cancel", () => Properties(f))));
        sp.Children.Add(err);
        page.Content = sp;
    }

    public void Firewall()
    {
        crumbs.Text = "Control Panel  >  System and Security  >  Windows Defender Firewall";
        var sp = new StackPanel { Margin = new Thickness(16) };
        sp.Children.Add(Lt.Link("< Control Panel", Home));
        sp.Children.Add(Lt.T("Help protect your PC with Windows Defender Firewall", 16, true));
        sp.Children.Add(Lt.Small("It stops other computers from reaching this one unless you allow it. A ping that gets no answer may simply be the firewall."));
        string g = "fw" + Guid.NewGuid().ToString("n");
        var on = Lt.Radio("Turn on Windows Defender Firewall", D.Host.WinFirewall, g);
        var off = Lt.Radio("Turn off Windows Defender Firewall (not recommended)", !D.Host.WinFirewall, g);
        on.IsCheckedChanged += (s, e) => { if (on.IsChecked == true) { D.Host.WinFirewall = true; DevicePage.Changed(); } };
        off.IsCheckedChanged += (s, e) => { if (off.IsChecked == true) { D.Host.WinFirewall = false; DevicePage.Changed(); } };
        sp.Children.Add(on); sp.Children.Add(off);
        sp.Children.Add(Lt.T("Allow an app or feature through Windows Defender Firewall", 15, true));
        var share = Lt.Check("File and Printer Sharing (includes Echo Request - ICMPv4-In, so pings get answers)", D.Host.FwSharing);
        share.IsCheckedChanged += (s, e) => { D.Host.FwSharing = share.IsChecked == true; DevicePage.Changed(); };
        var web = Lt.Check("World Wide Web Services (HTTP)", D.Host.FwWeb);
        web.IsCheckedChanged += (s, e) => { D.Host.FwWeb = web.IsChecked == true; DevicePage.Changed(); };
        sp.Children.Add(share); sp.Children.Add(web);
        page.Content = sp;
    }
}

/// <summary>File Explorer: type \\server or \\server\share to see shared folders.</summary>
public sealed class WinExplorer : DockPanel
{
    readonly Device D;
    readonly TextBox address;
    readonly StackPanel items = new() { Margin = new Thickness(14) };
    public string ItemsText => string.Join("\n", items.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text));

    public WinExplorer(Device d)
    {
        D = d;
        Background = Brushes.White;
        var bar = new DockPanel { Margin = new Thickness(8) };
        SetDock(bar, Dock.Top);
        var go = Lt.Btn("Go", () => Go(address.Text));
        go.Margin = new Thickness(6, 0, 0, 0); go.MinWidth = 52;
        SetDock(go, Dock.Right);
        address = Lt.Box("This PC", 100);
        address.Width = double.NaN; address.HorizontalAlignment = HorizontalAlignment.Stretch;
        address.KeyDown += (s, e) => { if (e.Key == Key.Enter) { e.Handled = true; Lt.Safe(() => Go(address.Text)); } };
        bar.Children.Add(go); bar.Children.Add(address);
        Children.Add(bar);
        Children.Add(Lt.Scroll(items));
        ShowThisPc();
    }

    void ShowThisPc()
    {
        items.Children.Clear();
        items.Children.Add(Lt.T("This PC", 16, true));
        items.Children.Add(Lt.T("Local Disk (C:)", 14, false));
        items.Children.Add(Lt.Small("To open a shared folder on another computer, type its address above, for example \\\\SERVER1\\Public or \\\\192.168.1.10\\Public, and press Go."));
    }

    public void Go(string path)
    {
        path = (path ?? "").Trim();
        address.Text = path;
        if (!path.StartsWith("\\\\")) { ShowThisPc(); return; }
        var parts = path.Substring(2).Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;
        string host = parts[0];
        items.Children.Clear();
        items.Children.Add(Lt.Small("Working on it..."));
        var req = parts.Length > 1 ? new TextMsg { Proto = "SMB", Line = "LIST " + parts[1] } : new TextMsg { Proto = "SMB", Line = "SHARES" };
        Request.Send(D, host, 445, req, (m, err, ip) =>
        {
            items.Children.Clear();
            if (m == null || m.Line == "NOT FOUND")
            {
                items.Children.Add(Lt.T("Network Error", 16, true));
                items.Children.Add(Lt.T("Windows cannot access " + path, 14, false));
                items.Children.Add(Lt.Small(m != null ? "The shared folder doesn't exist on " + host + "." : err.StartsWith("name") ? "Check the spelling of the name. The name " + host + " couldn't be found (DNS, or a computer on this network with that name)." : "Error code: 0x80070035  The network path was not found. (Is file sharing on there, and does its firewall allow it?)"));
                return;
            }
            items.Children.Add(Lt.T(path, 16, true));
            foreach (var f in m.Body.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var name = f;
                if (parts.Length == 1) items.Children.Add(Lt.Link("[folder]  " + name, () => Go("\\\\" + host + "\\" + name)));
                else items.Children.Add(Lt.T("[file]  " + name, 14, false));
            }
        });
    }
}
