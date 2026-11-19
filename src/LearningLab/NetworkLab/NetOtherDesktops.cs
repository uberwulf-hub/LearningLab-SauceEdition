// Network Lab - the other pretend systems (App Hub's OtherDesktops.cs): an Ubuntu-like desktop, a macOS-like desktop,
// phones and tablets, a printer's front panel, and the TV / game console screens. Original drawings and wording.
// Touch first: every button is at least 44 px high, and windows fill the desktop on a phone (DeskArea).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using NetworkLab;
using LearningLab.Views;

namespace LearningLab.NetworkLab;

static class Grad
{
    public static IBrush Of(double x2, double y2, params (string C, double At)[] stops)
    {
        var b = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(x2, y2, RelativeUnit.Relative) };
        foreach (var s in stops) b.GradientStops.Add(new GradientStop(Color.Parse(s.C), s.At));
        return b;
    }
}

// ================================================================= Ubuntu-like desktop
public sealed class LinuxDesktop : Grid, IDesktop
{
    readonly DevicePage W;
    readonly Device D;
    public readonly DeskArea Desk = new();

    public LinuxDesktop(DevicePage w, Device d)
    {
        W = w; D = d;
        RowDefinitions = new RowDefinitions("32,*");
        ColumnDefinitions = new ColumnDefinitions("60,*");
        var wall = new Border { Background = Grad.Of(1, 0.7, ("#3B0A45", 0), ("#8E2A55", 0.6), ("#E2663A", 1)) };
        SetRow(wall, 1); SetColumnSpan(wall, 2);
        Children.Add(wall);
        var top = new DockPanel { Background = Ui.Brush("#0F0F0F") };
        SetColumnSpan(top, 2);
        var act = new TextBlock { Text = "  Activities", Foreground = Ui.Brush("#E5E7EB"), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(act, Dock.Left);
        var status = new TextBlock { Foreground = Ui.Brush("#E5E7EB"), VerticalAlignment = VerticalAlignment.Center, FontSize = 12.5 };
        var statusBtn = Lt.Plain(status, () => OpenApp("settings"));
        statusBtn.MinHeight = 32; statusBtn.Padding = new Thickness(8, 0);
        DockPanel.SetDock(statusBtn, Dock.Right);
        var clock = new TextBlock { Foreground = Ui.Brush("#E5E7EB"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeight.SemiBold };
        top.Children.Add(act); top.Children.Add(statusBtn); top.Children.Add(clock);
        Children.Add(top);
        var dock = new StackPanel { Background = Ui.Brush("#1A1A1A") };
        SetRow(dock, 1);
        foreach (var a in new[] { new[] { "browser", "Web browser" }, new[] { "terminal", "Terminal" }, new[] { "settings", "Settings" } })
        {
            var id = a[0];
            var b = Lt.Plain(Glyphs.App(id, 38), () => OpenApp(id));
            b.Width = 48; b.Height = 48; b.Margin = new Thickness(6, 8, 6, 0); b.HorizontalContentAlignment = HorizontalAlignment.Center;
            ToolTip.SetTip(b, a[1]);
            dock.Children.Add(b);
        }
        Children.Add(dock);
        SetRow(Desk, 1); SetColumn(Desk, 1);
        Children.Add(Desk);
        void Upd()
        {
            clock.Text = Lt.Clock(D).ToString("ddd H:mm", System.Globalization.CultureInfo.InvariantCulture);
            var m = D.Host.Main;
            status.Text = m != null && m.Up ? (m.Port.IsWireless ? "wifi " : "wired ") + (m.Ip != 0 ? Ip.Str(m.Ip) : "connecting...") : "offline";
        }
        Upd(); w.OnRefresh(Upd);
    }

    public Control OpenApp(string id)
    {
        var iw = id switch
        {
            "terminal" or "cmd" => Desk.Open("terminal", "student@" + D.Host.HostName + ": ~", 720, 440, "linux", () => DesktopApps.Prompt(D, false), 30),
            "settings" => Desk.Open(id, "Settings", 760, 500, "linux", () => new LinuxSettings(W, D), 30),
            "browser" => Desk.Open(id, "Web browser", 760, 480, "linux", () => new MiniBrowser(D, true, ""), 30),
            _ => null,
        };
        return iw?.Body.Content as Control;
    }
}

/// <summary>GNOME-style Settings: Wi-Fi and Network (wired), with the IPv4 page.</summary>
public sealed class LinuxSettings : DockPanel
{
    readonly DevicePage W; readonly Device D;
    readonly ContentControl page = new();
    public LinuxSettings(DevicePage w, Device d)
    {
        W = w; D = d;
        Background = Ui.Brush("#FAFAFA");
        var nav = new WrapPanel { Background = Ui.Brush("#EBEBEB") };
        SetDock(nav, Dock.Top);
        nav.Children.Add(new TextBlock { Text = "Settings:", FontWeight = FontWeight.SemiBold, Foreground = Lt.Fg, Margin = new Thickness(12, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
        if (d.Host.Wifi.Count > 0) nav.Children.Add(Lt.Link("Wi-Fi", () => page.Content = Wifi(), Lt.FgC));
        nav.Children.Add(Lt.Link("Network", () => page.Content = Net(), Lt.FgC));
        Children.Add(nav);
        Children.Add(Lt.Scroll(page));
        page.Content = Net();
    }

    public Control Net()
    {
        var sp = new StackPanel { Margin = new Thickness(18) };
        foreach (var f in D.Host.Adapters.Where(x => !x.Port.IsWireless))
        {
            var ff = f;
            sp.Children.Add(Lt.T("Wired", 15, true));
            var st = Lt.T("", 14, false); st.Margin = new Thickness(0);
            void Upd() => st.Text = ff.Up ? "Connected - " + ff.Port.Speed + " Mb/s" + (ff.Ip != 0 ? "   (" + Ip.Str(ff.Ip) + ")" : "") : "Cable unplugged";
            Upd(); W.OnRefresh(Upd);
            var row = new DockPanel();
            var gear = new TextBlock { Text = "Settings >", Foreground = Ui.Brush("#E95420"), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(gear, Dock.Right);
            row.Children.Add(gear); row.Children.Add(st);
            sp.Children.Add(Lt.Row(row, () => page.Content = Details(ff)));
        }
        sp.Children.Add(Lt.Small("Settings here change the connection right away (like NetworkManager). On servers, the netplan file is used instead."));
        return sp;
    }

    public Control Details(IpIf f)
    {
        var sp = new StackPanel { Margin = new Thickness(18), MaxWidth = 480, HorizontalAlignment = HorizontalAlignment.Left };
        sp.Children.Add(Lt.Link("< Network", () => page.Content = Net()));
        sp.Children.Add(Lt.T("Wired: IPv4", 17, true));
        string g = "m" + Guid.NewGuid().ToString("n");
        var auto = Lt.Radio("Automatic (DHCP)", f.Dhcp, g);
        var man = Lt.Radio("Manual", !f.Dhcp, g);
        sp.Children.Add(Lt.Small("IPv4 Method")); sp.Children.Add(auto); sp.Children.Add(man);
        var addr = Lt.Box(f.Dhcp ? "" : Lt.IpOrEmpty(f.Ip), 170);
        var mask = Lt.Box(f.Dhcp ? "" : Lt.IpOrEmpty(f.Mask), 170);
        var gw = Lt.Box(f.Dhcp ? "" : Lt.IpOrEmpty(f.Gateway), 170);
        var grid = new StackPanel { Margin = new Thickness(0, 6) };
        grid.Children.Add(Lt.FieldRow("Address", addr, 120)); grid.Children.Add(Lt.FieldRow("Netmask", mask, 120)); grid.Children.Add(Lt.FieldRow("Gateway", gw, 120));
        sp.Children.Add(grid);
        var dnsAuto = Lt.Check("DNS automatic", f.Dhcp);
        var dns = Lt.Box(string.Join(", ", f.Dns.Select(Ip.Str)), 280);
        sp.Children.Add(dnsAuto); sp.Children.Add(Lt.Small("DNS servers, separated by commas")); sp.Children.Add(dns);
        void En() { grid.IsEnabled = man.IsChecked == true; dns.IsEnabled = dnsAuto.IsChecked != true; }
        auto.IsCheckedChanged += (s, e) => En(); man.IsCheckedChanged += (s, e) => En(); dnsAuto.IsCheckedChanged += (s, e) => En();
        En();
        var err = Lt.Err();
        sp.Children.Add(Lt.Buttons(Lt.Primary("Apply", () =>
        {
            var dl = (dns.Text ?? "").Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (auto.IsChecked == true)
            {
                D.Host.SetDhcp(f);
                if (dnsAuto.IsChecked != true) { f.Dns.Clear(); foreach (var x in dl) if (Ip.TryParse(x, out uint a)) f.Dns.Add(a); }
            }
            else
            {
                string e2 = Lt.ApplyManual(D, f, addr.Text, mask.Text, gw.Text, dl.Length > 0 ? dl[0] : "", dl.Length > 1 ? dl[1] : "");
                if (e2.Length > 0) { err.Text = e2; return; }
            }
            DevicePage.Changed();
            page.Content = Net();
        }, "#E95420"), Lt.Btn("Cancel", () => page.Content = Net())));
        sp.Children.Add(err);
        return sp;
    }

    Control Wifi()
    {
        var sp = new StackPanel { Margin = new Thickness(18) };
        sp.Children.Add(Lt.T("Wi-Fi: visible networks", 15, true));
        var ask = new StackPanel { IsVisible = false };
        var askT = Lt.T("", 14, false);
        var key = Lt.Secret(240);
        var res = Lt.T("", 13, false);
        string wanted = null;
        ask.Children.Add(askT); ask.Children.Add(key);
        ask.Children.Add(Lt.Buttons(Lt.Primary("Connect", () =>
        {
            var st = DesktopApps.Join(D, wanted, key.Text);
            res.Text = st == "connected" ? "Connected to " + wanted + "." : st == "wrong-key" ? "Authentication required: the password was wrong." : "Couldn't connect.";
            if (st == "connected") ask.IsVisible = false;
        }, "#E95420")));
        sp.Children.Add(DesktopApps.WifiList(W, D, true, s => { wanted = s; askT.Text = "Authentication required by Wi-Fi network: a password is needed to use " + s + "."; key.Text = ""; ask.IsVisible = true; key.Focus(); }));
        sp.Children.Add(ask); sp.Children.Add(res);
        return sp;
    }
}

// ================================================================= macOS-like desktop
public sealed class MacDesktop : Grid, IDesktop
{
    readonly DevicePage W;
    readonly Device D;
    public readonly DeskArea Desk = new();
    readonly Border wifiMenu;
    readonly StackPanel menus;

    public MacDesktop(DevicePage w, Device d)
    {
        W = w; D = d;
        RowDefinitions = new RowDefinitions("32,*");
        var wall = new Border { Background = Grad.Of(0.6, 1, ("#1E3A8A", 0), ("#6D28D9", 0.55), ("#F472B6", 1)) };
        SetRow(wall, 1);
        Children.Add(wall);
        var bar = new DockPanel { Background = Ui.Brush("#E9E9EB") };
        menus = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        menus.Children.Add(new Ellipse { Width = 12, Height = 12, Fill = Ui.Brush("#4B5563"), Margin = new Thickness(0, 0, 14, 0) });
        foreach (var t in new[] { "Finder", "File", "Edit", "View", "Go", "Window", "Help" }) menus.Children.Add(new TextBlock { Text = t, Foreground = Lt.Fg, Margin = new Thickness(0, 0, 14, 0), FontWeight = t == "Finder" ? FontWeight.SemiBold : FontWeight.Normal });
        DockPanel.SetDock(menus, Dock.Left);
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        var wifi = new TextBlock { Foreground = Lt.Fg, FontSize = 12.5 };
        var wifiBtn = Lt.Plain(wifi, ToggleWifiMenu, "#000000");
        wifiBtn.MinHeight = 32; wifiBtn.Padding = new Thickness(8, 0);
        var clock = new TextBlock { Foreground = Lt.Fg, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(wifiBtn); right.Children.Add(clock);
        bar.Children.Add(menus); bar.Children.Add(right);
        Children.Add(bar);
        SetRow(Desk, 1);
        Children.Add(Desk);
        // the dock
        var dock = new Border { Background = Ui.Brush("#F5F5F7", 150), CornerRadius = new CornerRadius(14), Padding = new Thickness(8, 4), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 8) };
        var icons = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var a in new[] { new[] { "browser", "Web browser" }, new[] { "terminal", "Terminal" }, new[] { "settings", "System Settings" } })
        {
            var id = a[0];
            var b = Lt.Plain(Glyphs.App(id, 42), () => OpenApp(id), "#000000");
            b.Width = 52; b.Height = 52; b.Margin = new Thickness(3, 0); b.HorizontalContentAlignment = HorizontalAlignment.Center;
            ToolTip.SetTip(b, a[1]);
            icons.Children.Add(b);
        }
        dock.Child = icons;
        SetRow(dock, 1);
        Children.Add(dock);
        // the Wi-Fi menu (drops down from the menu bar)
        wifiMenu = new Border { Width = 300, Background = Ui.Brush("#F5F5F7"), CornerRadius = new CornerRadius(8), Padding = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 8, 0), IsVisible = false, BoxShadow = BoxShadows.Parse("0 2 16 0 #66000000"), ZIndex = 100000 };
        SetRow(wifiMenu, 1);
        Children.Add(wifiMenu);
        Desk.Changed += () => wifiMenu.IsVisible = false;
        SizeChanged += (s, e) =>
        {
            for (int i = 2; i < menus.Children.Count; i++) menus.Children[i].IsVisible = e.NewSize.Width >= 640;
            wifiMenu.Width = Math.Min(300, e.NewSize.Width - 16);
        };
        void Upd()
        {
            clock.Text = Lt.Clock(D).ToString("ddd d MMM  H:mm", System.Globalization.CultureInfo.InvariantCulture);
            var radio = D.Host.Adapters.FirstOrDefault(f => f.Port.IsWireless);
            var wc = radio != null ? D.Host.WifiFor(radio.Port) : null;
            wifi.Text = wc == null ? "" : wc.Status == "connected" ? "Wi-Fi: " + wc.Ssid : "Wi-Fi: off";
            wifiBtn.IsVisible = wc != null;
        }
        Upd(); w.OnRefresh(Upd);
    }

    public bool WifiMenuOpen => wifiMenu.IsVisible;

    public void ToggleWifiMenu()
    {
        if (wifiMenu.IsVisible) { wifiMenu.IsVisible = false; return; }
        var sp = new StackPanel();
        sp.Children.Add(Lt.T("Wi-Fi", 14, true));
        var ask = new StackPanel { IsVisible = false };
        var askT = Lt.Small("");
        var key = Lt.Secret(220);
        var res = Lt.Small("");
        string wanted = null;
        ask.Children.Add(askT); ask.Children.Add(key);
        ask.Children.Add(Lt.Buttons(Lt.Primary("Join", () =>
        {
            var st = DesktopApps.Join(D, wanted, key.Text);
            res.Text = st == "connected" ? "Joined " + wanted + "." : st == "wrong-key" ? "The password is incorrect." : "Couldn't join.";
            if (st == "connected") ask.IsVisible = false;
        }, "#0A84FF")));
        sp.Children.Add(DesktopApps.WifiList(W, D, true, s => { wanted = s; askT.Text = "The Wi-Fi network \"" + s + "\" needs a password."; key.Text = ""; ask.IsVisible = true; key.Focus(); }));
        sp.Children.Add(ask); sp.Children.Add(res);
        sp.Children.Add(Lt.Link("Wi-Fi Settings...", () => { wifiMenu.IsVisible = false; OpenApp("settings"); }));
        wifiMenu.Child = Lt.Scope(sp);
        wifiMenu.IsVisible = true;
    }

    public Control OpenApp(string id)
    {
        var iw = id switch
        {
            "terminal" or "cmd" => Desk.Open("terminal", "student - zsh", 700, 430, "mac", () => DesktopApps.Prompt(D, false), 60),
            "settings" => Desk.Open(id, "Network", 720, 480, "mac", () => new MacSettings(W, D), 60),
            "browser" => Desk.Open(id, "Web browser", 760, 480, "mac", () => new MiniBrowser(D, true, ""), 60),
            _ => null,
        };
        return iw?.Body.Content as Control;
    }
}

/// <summary>System Settings > Network: services, and Details with TCP/IP and DNS panes.</summary>
public sealed class MacSettings : DockPanel
{
    readonly DevicePage W; readonly Device D;
    readonly ContentControl page = new();
    public MacSettings(DevicePage w, Device d)
    {
        W = w; D = d;
        Background = Ui.Brush("#F5F5F7");
        Children.Add(Lt.Scroll(page));
        page.Content = List();
    }

    public Control List()
    {
        var sp = new StackPanel { Margin = new Thickness(18) };
        sp.Children.Add(Lt.T("Network", 18, true));
        foreach (var f in D.Host.Adapters)
        {
            var ff = f;
            var t = Lt.T("", 14, false); t.Margin = new Thickness(0);
            void Upd() => t.Text = (ff.Port.IsWireless ? "Wi-Fi" : "Ethernet") + "   " + (ff.Up ? (ff.Ip != 0 ? "Connected  (" + Ip.Str(ff.Ip) + ")" : "Connecting...") : "Not connected");
            Upd(); W.OnRefresh(Upd);
            var row = new DockPanel();
            var det = new TextBlock { Text = "Details...", Foreground = Ui.Brush("#0A84FF"), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(det, Dock.Right);
            row.Children.Add(det); row.Children.Add(t);
            sp.Children.Add(Lt.Row(row, () => page.Content = Details(ff, "tcp"), 8));
        }
        return sp;
    }

    public Control Details(IpIf f, string tab)
    {
        var sp = new StackPanel { Margin = new Thickness(18), MaxWidth = 460, HorizontalAlignment = HorizontalAlignment.Left };
        sp.Children.Add(Lt.T((f.Port.IsWireless ? "Wi-Fi" : "Ethernet") + " details", 16, true));
        sp.Children.Add(Lt.Buttons(Lt.Btn("TCP/IP", () => page.Content = Details(f, "tcp")), Lt.Btn("DNS", () => page.Content = Details(f, "dns"))));
        var err = Lt.Err();
        if (tab == "tcp")
        {
            var how = Lt.Combo(200, "Using DHCP", "Manually");
            how.SelectedIndex = f.Dhcp ? 0 : 1;
            sp.Children.Add(Lt.FieldRow("Configure IPv4", how, 130));
            var ip = Lt.Box(Lt.IpOrEmpty(f.Ip), 170); var mask = Lt.Box(Lt.IpOrEmpty(f.Mask), 170); var gw = Lt.Box(Lt.IpOrEmpty(f.Gateway), 170);
            var fields = new StackPanel();
            fields.Children.Add(Lt.FieldRow("IP address", ip, 130)); fields.Children.Add(Lt.FieldRow("Subnet mask", mask, 130)); fields.Children.Add(Lt.FieldRow("Router", gw, 130));
            sp.Children.Add(fields);
            void En() => fields.IsEnabled = how.SelectedIndex == 1;
            how.SelectionChanged += (s, e) => En();
            En();
            sp.Children.Add(Lt.Buttons(Lt.Primary("OK", () =>
            {
                if (how.SelectedIndex == 0) { D.Host.SetDhcp(f); DevicePage.Changed(); page.Content = List(); return; }
                var keep = f.Dns.Select(Ip.Str).ToList();
                string e2 = Lt.ApplyManual(D, f, ip.Text, mask.Text, gw.Text, keep.Count > 0 ? keep[0] : "", keep.Count > 1 ? keep[1] : "");
                if (e2.Length > 0) { err.Text = e2; return; }
                page.Content = List();
            }, "#0A84FF"), Lt.Btn("Cancel", () => page.Content = List())));
        }
        else
        {
            sp.Children.Add(Lt.Small("DNS Servers"));
            var servers = f.Dns.Select(Ip.Str).ToList();
            var list = new StackPanel();
            void Fill()
            {
                list.Children.Clear();
                foreach (var s in servers.ToList())
                {
                    var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
                    var rm = Lt.Btn("-", () => { servers.Remove(s); Fill(); });
                    rm.MinWidth = 44; rm.Margin = new Thickness(8, 0, 0, 0);
                    DockPanel.SetDock(rm, Dock.Right);
                    row.Children.Add(rm);
                    row.Children.Add(new TextBlock { Text = s, Foreground = Lt.Fg, VerticalAlignment = VerticalAlignment.Center });
                    list.Children.Add(row);
                }
                if (servers.Count == 0) list.Children.Add(Lt.Small("None."));
            }
            Fill();
            sp.Children.Add(new Border { Child = list, Background = Brushes.White, Padding = new Thickness(10, 6), BorderBrush = Ui.Brush("#D1D5DB"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 0, 8) });
            var nw = Lt.Box("", 170);
            var plus = Lt.Btn("+", () => { if (Ip.TryParse(nw.Text, out uint a)) { servers.Add(Ip.Str(a)); nw.Text = ""; err.Text = ""; Fill(); } else err.Text = "That isn't a valid IP address."; });
            plus.MinWidth = 44; plus.Margin = new Thickness(8, 0, 0, 0);
            sp.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { nw, plus } });
            sp.Children.Add(Lt.Buttons(Lt.Primary("OK", () =>
            {
                f.Dns.Clear();
                foreach (var s in servers) f.Dns.Add(Ip.Parse(s));
                D.Host.DnsCache.Clear();
                DevicePage.Changed();
                page.Content = List();
            }, "#0A84FF")));
        }
        sp.Children.Add(err);
        return sp;
    }
}

// ================================================================= phones and tablets
public sealed class PhoneScreen : Grid, IDesktop
{
    readonly DevicePage W;
    readonly Device D;
    readonly bool ios;
    readonly ContentControl screen = new();
    readonly Stack<Func<Control>> back = new();
    readonly TextBlock statusTime, statusNet;
    public string Shown { get; private set; } = "home";

    public PhoneScreen(DevicePage w, Device d, bool iphone)
    {
        W = w; D = d; ios = iphone;
        Background = Ui.Brush("#0F172A");
        bool tablet = d.Kind == "tablet";
        var frame = new Border { MaxWidth = tablet ? 600 : 360, MaxHeight = tablet ? 460 : 680, CornerRadius = new CornerRadius(tablet ? 24 : 32), Background = Ui.Brush("#111111"), Padding = new Thickness(10), Margin = new Thickness(8), BorderBrush = Ui.Brush("#4B5563"), BorderThickness = new Thickness(2) };
        var inner = new DockPanel { Background = Ui.Brush(ios ? "#F2F2F7" : "#FFFFFF"), ClipToBounds = true };
        var status = new DockPanel { Height = 26, Background = Ui.Brush(ios ? "#F2F2F7" : "#FFFFFF") };
        DockPanel.SetDock(status, Dock.Top);
        statusTime = new TextBlock { Foreground = Lt.Fg, FontWeight = FontWeight.SemiBold, Margin = new Thickness(14, 4, 0, 0) };
        statusNet = new TextBlock { Foreground = Lt.Fg, Margin = new Thickness(0, 4, 14, 0), HorizontalAlignment = HorizontalAlignment.Right, FontSize = 12.5 };
        DockPanel.SetDock(statusTime, Dock.Left);
        status.Children.Add(statusTime); status.Children.Add(statusNet);
        inner.Children.Add(status);
        // the home bar (iPhone-like) or the navigation row (Android-like): both are 44 px to tap
        Control homeMark = ios ? new Border { Width = 120, Height = 5, CornerRadius = new CornerRadius(3), Background = Ui.Brush("#111111") } : new Ellipse { Width = 16, Height = 16, Stroke = Ui.Brush("#374151"), StrokeThickness = 2 };
        var nav = Lt.Plain(homeMark, Home, "#000000");
        nav.Height = 44; nav.HorizontalContentAlignment = HorizontalAlignment.Center; nav.HorizontalAlignment = HorizontalAlignment.Stretch;
        if (!ios) nav.Background = Ui.Brush("#F3F4F6");
        ToolTip.SetTip(nav, "Home");
        DockPanel.SetDock(nav, Dock.Bottom);
        inner.Children.Add(nav);
        inner.Children.Add(Lt.Scope(screen));
        frame.Child = inner;
        Children.Add(frame);
        Home();
        void Upd()
        {
            statusTime.Text = Lt.Clock(D).ToString("H:mm");
            var wc = D.Host.Wifi.FirstOrDefault();
            statusNet.Text = wc != null && wc.Status == "connected" ? "Wi-Fi " + wc.Ssid : "No Wi-Fi";
        }
        Upd(); w.OnRefresh(Upd);
    }

    void Go(string name, Func<Control> page) { back.Push(page); Shown = name; screen.Content = page(); }
    void Back() { if (back.Count > 1) { back.Pop(); screen.Content = back.Peek()(); Shown = "back"; } else Home(); }
    public void Home() { back.Clear(); Go("home", HomePage); }

    Control Header(string title, bool showBack)
    {
        var dp = new DockPanel { Margin = new Thickness(6, 4, 8, 8) };
        if (showBack) { var b = Lt.Link(ios ? "< Back" : "<-", Back, ios ? "#0A84FF" : "#1A73E8"); DockPanel.SetDock(b, Dock.Left); b.Margin = new Thickness(0, 0, 8, 0); b.MinWidth = 44; dp.Children.Add(b); }
        var t = Lt.T(title, ios ? 20 : 18, true); t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(showBack ? 0 : 8, 0, 0, 0);
        dp.Children.Add(t);
        return dp;
    }

    Control HomePage()
    {
        var g = new WrapPanel { Margin = new Thickness(16) };
        var sp = new Grid { Background = Grad.Of(0.5, 1, (ios ? "#60A5FA" : "#34D399", 0), (ios ? "#A78BFA" : "#0EA5E9", 1)) };
        foreach (var a in new[] { new[] { "Settings", "settings" }, new[] { "Browser", "browser" } })
        {
            var name = a[0]; var glyph = a[1];
            var b = new StackPanel { Width = 72 };
            var icon = new Border { Width = 56, Height = 56, CornerRadius = new CornerRadius(ios ? 13 : 28), Background = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, Child = Glyphs.App(glyph, 40) };
            b.Children.Add(icon);
            b.Children.Add(new TextBlock { Text = name, Foreground = Brushes.White, TextAlignment = TextAlignment.Center, FontSize = 12.5, Margin = new Thickness(0, 4, 0, 0) });
            var btn = Lt.Plain(b, () => OpenApp(glyph));
            btn.Margin = new Thickness(0, 0, 12, 12); btn.Padding = new Thickness(0, 4);
            g.Children.Add(btn);
        }
        sp.Children.Add(g);
        return sp;
    }

    public Control OpenApp(string id)
    {
        switch (id)
        {
            case "settings": Home(); Go("settings", SettingsPage); break;
            case "wifi": ShowWifi(); break;
            case "browser": Home(); Go("browser", BrowserPage); break;
            case "ip": ShowWifi(); Go("ip", IpPage); break;
            default: return null;
        }
        return screen.Content as Control;
    }

    Control SettingsPage()
    {
        var sp = new StackPanel();
        sp.Children.Add(Header("Settings", false));
        sp.Children.Add(Item(ios ? "Wi-Fi" : "Network & internet  >  Wi-Fi", () => Go("wifi", WifiPage)));
        sp.Children.Add(Item(D.Kind == "tablet" ? "About tablet" : "About phone", () => Go("about", About)));
        return sp;
    }

    Control Item(string text, Action a)
    {
        var b = Lt.Row(new TextBlock { Text = text, Foreground = Lt.Fg, Margin = new Thickness(4, 0, 0, 0) }, a, ios ? 8 : 0);
        b.Margin = new Thickness(ios ? 12 : 0, 0, ios ? 12 : 0, 2);
        return b;
    }

    Control About()
    {
        var sp = new StackPanel { Margin = new Thickness(10) };
        sp.Children.Add(Header("About", true));
        var f = D.Host.Adapters.FirstOrDefault();
        sp.Children.Add(Lt.T("Model: " + Personalities.Get(D.Os).Title, 14, false));
        if (f != null) { sp.Children.Add(Lt.T("Wi-Fi MAC address: " + Mac.Colon(f.Mac), 14, false)); sp.Children.Add(Lt.T("IP address: " + (f.Ip != 0 ? Ip.Str(f.Ip) : "unavailable"), 14, false)); }
        return sp;
    }

    Control WifiPage()
    {
        var sp = new StackPanel();
        sp.Children.Add(Header("Wi-Fi", true));
        var wc = D.Host.Wifi.FirstOrDefault();
        if (wc == null) { sp.Children.Add(Lt.Small("No Wi-Fi on this device.")); return sp; }
        sp.Children.Add(new Border { Margin = new Thickness(ios ? 12 : 4, 0, ios ? 12 : 4, 8), Child = DesktopApps.WifiList(W, D, true, ssid => Go("password", () => Password(ssid))) });
        if (wc.Status == "connected") sp.Children.Add(Item((ios ? "(i)  " : "Settings: ") + wc.Ssid + " - IP settings", () => Go("ip", IpPage)));
        return new ScrollViewer { Content = sp };
    }

    Control Password(string ssid)
    {
        var sp = new StackPanel { Margin = new Thickness(10) };
        sp.Children.Add(Header(ios ? "Enter Password" : ssid, true));
        sp.Children.Add(Lt.Small(ios ? "Enter the password for \"" + ssid + "\"" : "Password"));
        var key = Lt.Secret(240);
        key.Margin = new Thickness(0, 4, 0, 8);
        sp.Children.Add(key);
        var res = Lt.T("", 13, false);
        sp.Children.Add(Lt.Primary(ios ? "Join" : "Connect", () =>
        {
            var st = DesktopApps.Join(D, ssid, key.Text);
            if (st == "connected") { Back(); return; }
            res.Text = st == "wrong-key" ? (ios ? "Incorrect password for \"" + ssid + "\"" : "Check password and try again") : "Couldn't connect.";
        }, ios ? "#0A84FF" : "#1A73E8"));
        sp.Children.Add(res);
        return sp;
    }

    Control IpPage()
    {
        var f = D.Host.Adapters.First();
        var sp = new StackPanel { Margin = new Thickness(10) };
        sp.Children.Add(Header(ios ? "Configure IP" : "IP settings", true));
        var how = Lt.Combo(200, ios ? "Automatic" : "DHCP", ios ? "Manual" : "Static");
        how.SelectedIndex = f.Dhcp ? 0 : 1;
        sp.Children.Add(how);
        var ip = Lt.Box(Lt.IpOrEmpty(f.Ip), 200); var gw = Lt.Box(Lt.IpOrEmpty(f.Gateway), 200);
        var mask = Lt.Box(f.Mask != 0 ? (ios ? Ip.Str(f.Mask) : Ip.PrefixOf(f.Mask).ToString()) : "", 200);
        var dns = Lt.Box(f.Dns.Count > 0 ? Ip.Str(f.Dns[0]) : "", 200);
        var fields = new StackPanel { Margin = new Thickness(0, 8) };
        void Add(string l, TextBox b) { fields.Children.Add(Lt.Small(l)); b.Margin = new Thickness(0, 0, 0, 6); fields.Children.Add(b); }
        Add("IP address", ip);
        if (ios) { Add("Subnet Mask", mask); Add("Router", gw); }
        else { Add("Gateway", gw); Add("Network prefix length", mask); }
        Add(ios ? "DNS server" : "DNS 1", dns);
        sp.Children.Add(fields);
        void En() => fields.IsEnabled = how.SelectedIndex == 1;
        how.SelectionChanged += (s, e) => En(); En();
        var err = Lt.Err();
        sp.Children.Add(Lt.Primary("Save", () =>
        {
            if (how.SelectedIndex == 0) { D.Host.SetDhcp(f); DevicePage.Changed(); Back(); return; }
            string e2 = Lt.ApplyManual(D, f, ip.Text, mask.Text, gw.Text, dns.Text, "");
            if (e2.Length > 0) { err.Text = e2; return; }
            Back();
        }, ios ? "#0A84FF" : "#1A73E8"));
        sp.Children.Add(err);
        return new ScrollViewer { Content = sp };
    }

    Control BrowserPage() => new MiniBrowser(D, true, "www.example.com");

    public void ShowWifi() { Home(); Go("settings", SettingsPage); Go("wifi", WifiPage); }
}

// ================================================================= a printer's front panel
public sealed class PrinterPanel : StackPanel, IDesktop
{
    readonly Device D;
    readonly TextBlock lcd1, lcd2, paper;
    string[] menu;
    int sel;
    string path = "";
    string editing, buffer = "";
    string flash1, flash2; DateTime flashUntil;

    public PrinterPanel(DevicePage w, Device d)
    {
        D = d;
        Margin = new Thickness(14);
        Children.Add(Ui.Text(d.Model + " front panel", 17, Ui.AccentText, true));
        var note = Ui.Text("Use Menu, the arrows and OK like on a real printer. Network > TCP/IP sets its address. Its own web page is at http://<its address>/ from any computer's browser.", 14, Ui.Muted);
        note.Margin = new Thickness(0, 4, 0, 6);
        Children.Add(note);
        var body = new Border { MaxWidth = 640, Background = Grad.Of(0, 1, ("#E5E7EB", 0), ("#9CA3AF", 1)), CornerRadius = new CornerRadius(14), Padding = new Thickness(14), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8) };
        var wrap = new WrapPanel();
        var lcd = new Border { Width = 280, Background = Ui.Brush("#A7C080"), BorderBrush = Ui.Brush("#374151"), BorderThickness = new Thickness(3), CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 8), Height = 72, Margin = new Thickness(0, 0, 14, 10) };
        var mono = new FontFamily("Cascadia Mono, Consolas, Menlo, DejaVu Sans Mono, monospace");
        lcd1 = new TextBlock { FontFamily = mono, FontSize = 17, Foreground = Ui.Brush("#1F2A14") };
        lcd2 = new TextBlock { FontFamily = mono, FontSize = 17, Foreground = Ui.Brush("#1F2A14") };
        lcd.Child = new StackPanel { Children = { lcd1, lcd2 } };
        wrap.Children.Add(lcd);
        var keys = new WrapPanel { Width = 300 };
        foreach (var k in new[] { "Menu", "Back", "Up", "Down", "OK" }) keys.Children.Add(Key(k, 54));
        foreach (var k in new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", ".", "0", "Del" }) keys.Children.Add(Key(k, k == "Del" ? 54 : 44));
        wrap.Children.Add(keys);
        body.Child = wrap;
        Children.Add(body);
        var tray = Ui.Text("Output tray", 16, Ui.AccentText, true); tray.Margin = new Thickness(0, 8, 0, 4);
        Children.Add(tray);
        paper = new TextBlock { FontFamily = mono, FontSize = 13, Foreground = Ui.Brush(Ui.TextC), TextWrapping = TextWrapping.Wrap };
        Children.Add(paper);
        w.OnRefresh(Show);
        Show();
    }

    public Control OpenApp(string id) => null;
    public string Lcd => lcd1.Text + "\n" + lcd2.Text;
    public string Tray => paper.Text;

    Button Key(string k, double w)
    {
        var b = new Button
        {
            Content = new TextBlock { Text = k, Foreground = Brushes.White, FontSize = 14 }, Width = w, Height = 44, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(2),
            Background = Ui.Brush("#374151"), BorderBrush = Ui.Brush("#111827"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
        };
        b.Resources["ButtonBackgroundPointerOver"] = Ui.Brush("#4B5563");
        b.Resources["ButtonBackgroundPressed"] = Ui.Brush("#1F2937");
        b.Click += (s, e) => Lt.Safe(() => Press(k));
        return b;
    }

    IpIf F => D.Host.Adapters.First();

    static string[] MenuFor(string p) => p switch
    {
        "" => new[] { "Network", "Print reports", "Device info" },
        "Network" => new[] { "TCP/IP", "Host name" },
        "Network > TCP/IP" => new[] { "Boot method", "IP address", "Subnet mask", "Gateway", "DNS server" },
        "Network > TCP/IP > Boot method" => new[] { "DHCP", "Static" },
        "Print reports" => new[] { "Network config", "Test page" },
        _ => null,
    };

    public void Press(string k)
    {
        if (editing != null)
        {
            if (k == "Back") { editing = null; Show(); return; }
            if (k == "Del") { if (buffer.Length > 0) buffer = buffer.Substring(0, buffer.Length - 1); Show(); return; }
            if (k == "OK") { Save(); return; }
            if (k.Length == 1) { if (buffer.Length < 15) buffer += k; Show(); }
            return;
        }
        if (menu == null) { if (k == "Menu" || k == "OK") { path = ""; menu = MenuFor(""); sel = 0; } Show(); return; }
        switch (k)
        {
            case "Up": sel = (sel + menu.Length - 1) % menu.Length; break;
            case "Down": sel = (sel + 1) % menu.Length; break;
            case "Back": case "Menu":
                if (path == "" || k == "Menu") { menu = null; break; }
                int i = path.LastIndexOf(" > ", StringComparison.Ordinal);
                path = i < 0 ? "" : path.Substring(0, i);
                menu = MenuFor(path); sel = 0;
                break;
            case "OK":
                {
                    string item = menu[sel];
                    string next = path.Length == 0 ? item : path + " > " + item;
                    if (path == "Network > TCP/IP > Boot method")
                    {
                        if (item == "DHCP") { D.Host.SetDhcp(F); Flash("Boot method", "DHCP: saved"); }
                        else { F.Dhcp = false; F.DhcpC?.Stop(); Flash("Boot method", "Static: saved"); }
                        DevicePage.Changed();
                        return;
                    }
                    if (path == "Network > TCP/IP" && item != "Boot method") { editing = item; buffer = ""; Show(); return; }
                    if (path == "Network" && item == "Host name") { Flash("Host name", D.Host.HostName); return; }
                    if (path == "Print reports")
                    {
                        D.Host.Printed.Add(D.Name + ": " + (item == "Network config" ? "network configuration page (" + (F.Ip != 0 ? Ip.Str(F.Ip) : "no address") + ")" : "test page"));
                        Flash("Printing...", item);
                        DevicePage.Changed();
                        return;
                    }
                    if (path == "" && item == "Device info") { Flash(D.Model, "MAC " + Mac.Colon(F.Mac).Substring(6)); return; }
                    var m = MenuFor(next);
                    if (m != null) { path = next; menu = m; sel = 0; }
                    break;
                }
        }
        Show();
    }

    void Save()
    {
        var f = F;
        string ip = Lt.IpOrEmpty(f.Ip), mask = Lt.IpOrEmpty(f.Mask), gw = Lt.IpOrEmpty(f.Gateway), dns = f.Dns.Count > 0 ? Ip.Str(f.Dns[0]) : "";
        switch (editing)
        {
            case "IP address": ip = buffer; if (mask.Length == 0 && Ip.TryParse(ip, out uint a)) mask = Ip.Str(Ip.MaskOf(Ip.ClassfulPrefix(a))); break;
            case "Subnet mask": mask = buffer; break;
            case "Gateway": gw = buffer; break;
            case "DNS server": dns = buffer; break;
        }
        string what = editing;
        editing = null;
        if (ip.Length == 0) { Flash("Set IP address", "first"); return; }
        string err = D.Host.SetStatic(f, ip, mask, gw, dns, "");
        Flash(what, err.Length > 0 ? "Invalid value" : "Saved (static)");
        if (err.Length == 0) { D.P.Sim.TopologyChanged(); DevicePage.Changed(); }
    }

    void Flash(string a, string b) { flash1 = a; flash2 = b; flashUntil = DateTime.Now.AddSeconds(2); menu = MenuFor(path); Show(); }

    void Show()
    {
        var f = F;
        if (DateTime.Now < flashUntil) { lcd1.Text = Fit(flash1); lcd2.Text = Fit(flash2); }
        else if (editing != null) { lcd1.Text = Fit(editing + ":"); lcd2.Text = Fit(buffer + "_"); }
        else if (menu != null) { int i = path.LastIndexOf(" > ", StringComparison.Ordinal); lcd1.Text = Fit((path.Length > 0 ? (i >= 0 ? path.Substring(i + 3) : path) : "Menu") + ":"); lcd2.Text = Fit("> " + menu[sel]); }
        else
        {
            lcd1.Text = Fit(D.PoweredOn ? "Ready" : "");
            lcd2.Text = Fit(!D.PoweredOn ? "" : !f.Up ? "No network cable" : f.Ip != 0 ? Ip.Str(f.Ip) + (f.Dhcp ? " (DHCP)" : "") : f.Dhcp ? "Getting address..." : "No IP address");
        }
        paper.Text = D.Host.Printed.Count == 0 ? "(empty)" : string.Join("\n", D.Host.Printed.Skip(Math.Max(0, D.Host.Printed.Count - 8)).Select(x => "[page] " + x));
    }
    static string Fit(string s) { s ??= ""; return s.Length > 22 ? s.Substring(0, 22) : s; }
}

// ================================================================= TV and game console
public sealed class TvScreen : Grid, IDesktop
{
    readonly Device D;
    readonly ContentControl screen = new();
    public string Shown { get; private set; } = "";
    public string ScreenText => string.Join("\n", Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(screen).OfType<TextBlock>().Select(t => t.Text));

    public TvScreen(DevicePage w, Device d)
    {
        D = d;
        Background = Ui.Brush("#0F172A");
        bool console = d.Kind == "console";
        var frame = new Border { MaxWidth = 760, Margin = new Thickness(10), Background = Ui.Brush("#000000"), BorderBrush = Ui.Brush("#374151"), BorderThickness = new Thickness(8), CornerRadius = new CornerRadius(6), VerticalAlignment = VerticalAlignment.Stretch };
        frame.Child = new Border { Background = Grad.Of(0, 1, (console ? "#064E3B" : "#1E1B4B", 0), ("#020617", 1)), Child = new ScrollViewer { Content = screen } };
        Children.Add(frame);
        Main();
    }

    public Control OpenApp(string id)
    {
        switch (id) { case "wifi": WifiMenu(); break; case "status": Status(); break; case "test": Test(); break; default: Main(); break; }
        return screen.Content as Control;
    }

    static TextBlock W2(string s, double size) => new() { Text = s, Foreground = Brushes.White, FontSize = size, Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap };
    static Button Big(string s, Action a)
    {
        var b = new Button
        {
            Content = new TextBlock { Text = s, Foreground = Brushes.White, FontSize = 16, TextWrapping = TextWrapping.Wrap }, MinHeight = 48, Width = 300, HorizontalAlignment = HorizontalAlignment.Left,
            HorizontalContentAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(14, 6),
            Background = Ui.Brush("#334155"), BorderBrush = Ui.Brush("#64748B"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
        };
        b.Resources["ButtonBackgroundPointerOver"] = Ui.Brush("#475569");
        b.Resources["ButtonBackgroundPressed"] = Ui.Brush("#1E293B");
        b.Click += (x, y) => Lt.Safe(a);
        return b;
    }

    void Main()
    {
        Shown = "main";
        var sp = new StackPanel { Margin = new Thickness(24) };
        sp.Children.Add(W2(D.Kind == "console" ? "Game console: Settings" : "Smart TV: Settings", 24));
        sp.Children.Add(Big("Network: Wi-Fi", WifiMenu));
        sp.Children.Add(Big("Network status", Status));
        if (D.Kind == "console") sp.Children.Add(Big("Test internet connection", Test));
        screen.Content = sp;
    }

    void WifiMenu()
    {
        Shown = "wifi";
        var sp = new StackPanel { Margin = new Thickness(24) };
        sp.Children.Add(W2("Choose your Wi-Fi network", 22));
        var radio = D.Host.Adapters.FirstOrDefault(f => f.Port.IsWireless);
        if (radio != null)
            foreach (var ap in D.P.Devices.Where(x => x.Ap != null && x.Ap.Enabled && x.PoweredOn && Physical.Signal(D.P, x, D) > 0).GroupBy(x => x.Ap.Ssid).Select(g => g.First()))
            {
                var ssid = ap.Ap.Ssid;
                bool open = ap.Ap.Security == "open";
                sp.Children.Add(Big(ssid + (open ? "" : "   (password)"), () => { if (open) { DesktopApps.Join(D, ssid, ""); Status(); } else Key(ssid); }));
            }
        sp.Children.Add(Big("< Back", Main));
        screen.Content = sp;
    }

    void Key(string ssid)
    {
        Shown = "key";
        var sp = new StackPanel { Margin = new Thickness(24) };
        sp.Children.Add(W2("Password for " + ssid, 22));
        var pw = new TextBox { Width = 300, MinHeight = 44, FontSize = 18, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 10), PasswordChar = '*' };
        sp.Children.Add(pw);
        var res = W2("", 15);
        sp.Children.Add(Big("Connect", () => { var st = DesktopApps.Join(D, ssid, pw.Text); if (st == "connected") Status(); else res.Text = st == "wrong-key" ? "The password is incorrect." : "Couldn't connect."; }));
        sp.Children.Add(Big("< Back", WifiMenu));
        sp.Children.Add(res);
        screen.Content = sp;
        pw.Focus();
    }

    void Status()
    {
        Shown = "status";
        var f = D.Host.Main;
        var wc = D.Host.Wifi.FirstOrDefault();
        var sp = new StackPanel { Margin = new Thickness(24) };
        sp.Children.Add(W2("Network status", 22));
        sp.Children.Add(W2("Wi-Fi: " + (wc != null ? wc.StatusText : "none"), 16));
        sp.Children.Add(W2("Wired: " + (D.Ports.Any(p => p.Media == "copper" && p.IsUp) ? "connected" : "not connected"), 16));
        sp.Children.Add(W2("IP address: " + (f != null && f.Ip != 0 ? Ip.Str(f.Ip) + (f.Dhcp ? " (automatic)" : "") : "none yet"), 16));
        sp.Children.Add(Big("< Back", Main));
        screen.Content = sp;
    }

    void Test()
    {
        Shown = "test";
        var sp = new StackPanel { Margin = new Thickness(24) };
        sp.Children.Add(W2("Testing the connection to the game server...", 20));
        var res = W2("", 16);
        sp.Children.Add(res);
        sp.Children.Add(Big("< Back", Main));
        screen.Content = sp;
        Request.Http(D, "http://play.example.net/", "GET", null, (m, err) => res.Text = m != null ? "Internet connection: OK. Game server: reachable." : "Can't reach the game server (" + (err.StartsWith("name") ? "name not found: check DNS" : "no answer: check the Wi-Fi, address, gateway and the router's NAT") + ").");
    }
}
