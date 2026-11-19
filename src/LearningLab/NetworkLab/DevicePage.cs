// Network Lab - one device as a page (App Hub's DeviceWindow; on a phone a window is a page): the tabs it has.
// Routers and switches: CLI (the command line) and Physical. Computers, phones, servers: Command line (the device's own
// Command Prompt, PowerShell, bash or zsh), Config (name, operating system, each adapter's IP settings, Wi-Fi) and
// Physical. The wireless router and access point: their setup page. The internet box: what it offers. Physical: power,
// SFP modules, the cables and where they go.
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using NetworkLab;
using LearningLab.Views;

namespace LearningLab.NetworkLab;

public sealed class DevicePage : IPage
{
    public Device D { get; }
    public string Title => D.Name;
    public Control View => root;
    /// <summary>Something in the network changed (the main page redraws and checks the lab).</summary>
    public static Action Edited;
    public static UndoStack Undo;

    static readonly Dictionary<Device, CliSession> cliSessions = new();
    readonly Grid root = new() { RowDefinitions = new RowDefinitions("Auto,Auto,*") };
    readonly StackPanel tabBar = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 12, 8) };
    readonly ContentControl content = new();
    readonly List<(string Name, Func<Control> Make)> tabs = new();
    readonly Dictionary<string, Control> made = new();
    readonly List<Action> refreshers = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    readonly TextBlock title = Ui.Text("", 20, Ui.TextC, true), sub = Ui.Text("", 13, Ui.Muted);
    public string Tab { get; private set; }
    public NetTerminal Cli { get; private set; }
    public NetTerminal Shell { get; private set; }
    /// <summary>The pretend desktop, phone or front panel (once its tab was opened).</summary>
    public Control Desktop { get; private set; }
    public IosConfigTab IosConfig { get; private set; }
    public ServicesTab Services { get; private set; }
    /// <summary>The front panel on the Physical tab (once it has been shown).</summary>
    public PortPanel Panel { get; private set; }

    public DevicePage(Device d, string tab = null)
    {
        D = d;
        var head = new DockPanel { Margin = new Thickness(12, 10, 12, 8) };
        var icon = new DeviceIcon(d.Kind, d.Os, 52) { Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(icon, Dock.Left);
        head.Children.Add(icon);
        head.Children.Add(new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { title, sub } });
        root.Children.Add(head);
        // the tabs: one row that scrolls sideways on a phone (the desktops need the height)
        var tabScroll = new ScrollViewer { Content = tabBar, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetRow(tabScroll, 1);
        root.Children.Add(tabScroll);
        Grid.SetRow(content, 2);
        root.Children.Add(content);

        var fam = d.Host != null ? Personalities.Get(d.Os).Family : "";
        if (d.Host != null && Desktops.Create(this, d) is Control desk) tabs.Add((Desktops.TabName(d), () => { Desktop = desk; return desk; }));
        if (d.Ios != null) { tabs.Add(("CLI", BuildCli)); tabs.Add(("Config", () => IosConfig = new IosConfigTab(this, D))); }
        if (d.Host != null && Personalities.Get(d.Os).NewShell(d) != null) tabs.Add((fam == "windows" ? "Command Prompt" : fam == "generic" ? "Command line" : "Terminal", () => BuildShell(false)));
        if (fam == "windows") tabs.Add(("PowerShell", () => BuildShell(true)));
        if (d.Host != null && d.Ports.Any(p => p.Media == "rs232")) tabs.Add(("Console", BuildConsole));
        if (d.Host != null) tabs.Add(("Config", () => Scroll(HostConfig())));
        if (d.Kind == "server" || d.Kind == "nas") tabs.Add(("Services", () => Services = new ServicesTab(D)));
        if (d.Kind == "homerouter") tabs.Add(("Setup", () => Scroll(HomeConfig())));
        if (d.Kind == "ap") tabs.Add(("Setup", () => Scroll(ApConfig())));
        if (d.Kind == "cloud") tabs.Add(("About", () => Scroll(CloudInfo())));
        tabs.Add(("Physical", () => Scroll(PhysicalTab())));
        ShowTab(tab != null && tabs.Any(t => t.Name == tab) ? tab : tabs[0].Name);
        timer.Tick += (s, e) => Refresh();
        root.AttachedToVisualTree += (s, e) => { timer.Start(); Refresh(); };
        root.DetachedFromVisualTree += (s, e) => timer.Stop();
        Refresh();
    }

    public IReadOnlyList<string> Tabs => tabs.Select(t => t.Name).ToList();

    public void ShowTab(string name)
    {
        Tab = name;
        if (!made.TryGetValue(name, out var c)) made[name] = c = tabs.First(t => t.Name == name).Make();
        content.Content = c;
        tabBar.Children.Clear();
        foreach (var t in tabs)
        {
            var b = Ui.Button(t.Name, t.Name == name, 14);
            b.MinHeight = 44; b.Margin = new Thickness(0, 0, 6, 6);
            var n = t.Name;
            b.Click += (s, e) => ShowTab(n);
            tabBar.Children.Add(b);
            if (n == name) b.AttachedToVisualTree += (s, e) => Dispatcher.UIThread.Post(() => b.BringIntoView(), DispatcherPriority.Background);
        }
        if (c is NetTerminal nt) Dispatcher.UIThread.Post(nt.FocusInput, DispatcherPriority.Input);
        else if (c is ServerScreen ss) Dispatcher.UIThread.Post(ss.Console.FocusInput, DispatcherPriority.Input);
        Refresh();
    }

    public void Refresh()
    {
        title.Text = D.Name;
        sub.Text = D.Model + (D.PoweredOn ? "" : "  ·  switched off");
        foreach (var r in refreshers) r();
    }

    public void OnRefresh(Action a) => refreshers.Add(a);
    public static void Changed() => Edited?.Invoke();

    static Control Scroll(Control c) => new ScrollViewer { Content = new Border { Child = c, Padding = new Thickness(14, 4, 14, 24), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left } };
    static TextBlock Head(string t) { var h = Ui.Text(t, 16, Ui.AccentText, true); h.Margin = new Thickness(0, 14, 0, 6); return h; }
    static TextBlock Note(string t) { var n = Ui.Text(t, 13, Ui.Muted); n.TextWrapping = TextWrapping.Wrap; n.Margin = new Thickness(0, 2, 0, 6); return n; }
    static TextBlock MonoText(string t) => new() { Text = t, FontFamily = new FontFamily("Cascadia Mono, Consolas, Menlo, DejaVu Sans Mono, monospace"), FontSize = 13, Foreground = Ui.Brush(Ui.TextC), TextWrapping = TextWrapping.Wrap };

    /// <summary>A label over a text box (fits a phone).</summary>
    static TextBox Field(Panel parent, string label, string value, string tip = null, double width = 260)
    {
        var l = Ui.Text(label, 13, Ui.Muted);
        var t = new TextBox { Text = value ?? "", Width = width, HorizontalAlignment = HorizontalAlignment.Left, MinHeight = 40 };
        if (!string.IsNullOrEmpty(tip)) ToolTip.SetTip(t, tip);
        parent.Children.Add(new StackPanel { Spacing = 3, Margin = new Thickness(0, 0, 0, 8), Children = { l, t } });
        return t;
    }

    static Button Btn(string text, Action a, bool primary = false)
    {
        var b = Ui.Button(text, primary, 14);
        b.MinHeight = 44; b.Margin = new Thickness(0, 0, 8, 8); b.HorizontalAlignment = HorizontalAlignment.Left;
        b.Click += (s, e) => { try { a(); } catch (Exception ex) { MainView.Current?.Message("That didn't work", ex.Message); } };
        return b;
    }

    // ------------------------------------------------------------ the command lines
    public static CliSession SessionFor(Device d)
    {
        if (!cliSessions.TryGetValue(d, out var s) || !d.Ios.Sessions.Contains(s))
        {
            s = new CliSession(d);
            s.Print(Brand.Os + " Software (Network Lab simulation), Version 1.0\n" + Brand.About + "\n\n" + Brand.FullModel(d.Kind) + " with simulated memory.\n" + d.Ports.Count(p => !p.IsSerial && !p.IsWireless) + " network interfaces.\n\nPress RETURN to get started!\n");
            s.Exited = true;
            cliSessions[d] = s;
        }
        return s;
    }

    /// <summary>A new network: the old devices' sessions go.</summary>
    public static void ForgetSessions() => cliSessions.Clear();

    Control BuildCli()
    {
        Cli = new NetTerminal(new CliBackend(SessionFor(D)), "#0B0F14", "#E5E7EB");
        return Cli;
    }

    Control BuildShell(bool powershell)
    {
        var sh = Personalities.Get(D.Os).NewShell(D) ?? new WinShell(D, false);
        if (sh is WinShell ws && powershell) { ws.PowerShell = true; ws.Title = "Windows PowerShell"; }
        string bg = "#0C0C0C", fg = "#CCCCCC";
        var fam = Personalities.Get(D.Os).Family;
        if (powershell) { bg = "#012456"; fg = "#EEEDF0"; }
        else if (fam == "linux") { bg = "#1D0F1F"; fg = "#E8E6E3"; }
        else if (fam == "mac") { bg = "#1E1E1E"; fg = "#E6E6E6"; }
        else if (fam == "generic") { bg = "#0B0F14"; fg = "#E5E7EB"; }
        if (fam == "linux" && !powershell && Personalities.Get(D.Os) is LinuxPersonality lp && !lp.Desktop)
        {
            var screen = new ServerScreen(D);
            screen.Console.ClosedByUser += () => { made.Remove(Tab); ShowTab(Tab); };
            Shell = screen.Console;
            return screen;
        }
        var t = new NetTerminal(new ShellBackend(sh), bg, fg);
        t.ClosedByUser += () => { made.Remove(Tab); ShowTab(Tab); };   // "exit": a fresh one
        Shell = t;
        return t;
    }

    /// <summary>The terminal program over a console cable to a router or switch.</summary>
    Control BuildConsole()
    {
        var port = D.Ports.FirstOrDefault(p => p.Media == "rs232");
        var l = port?.Link;
        if (l == null) return Scroll(new StackPanel { Children = { Head("Terminal"), Note("Connect a console cable from this computer's RS-232 port to the Console port of a router or switch, then open this tab again to type commands on it, as if you were standing next to it.") } });
        var dev = l.Other(port).Dev;
        var s = new CliSession(dev);
        s.Print("Terminal: 9600 bits per second, 8 data bits, no parity, 1 stop bit (console cable to " + dev.Name + ")\n\nPress RETURN to get started!\n");
        s.Exited = true;
        return new NetTerminal(new CliBackend(s), "#0B0F14", "#E5E7EB");
    }

    // ------------------------------------------------------------ Physical
    Control PhysicalTab()
    {
        var sp = new StackPanel();
        sp.Children.Add(Head(D.Model));
        sp.Children.Add(Note(Catalog.Explain(D.Kind)));
        var power = Btn("", () =>
        {
            D.PoweredOn = !D.PoweredOn; D.P.Sim.TopologyChanged();
            if (D.PoweredOn && D.Host != null) D.Host.Start();
            if (D.PoweredOn && D.Ios != null) Persist.Reload(D);
            Changed(); Refresh();
        });
        OnRefresh(() => power.Content = D.PoweredOn ? "Switch off" : "Switch on");
        sp.Children.Add(power);
        // the front panel: every port with its light and cable; tap an SFP slot to change its module
        Panel = new PortPanel(D, true) { ModuleOnTap = true };
        Panel.Changed += () => { Changed(); Refresh(); };
        OnRefresh(() => Panel.InvalidateVisual());
        sp.Children.Add(new ScrollViewer { Content = Panel, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, Margin = new Thickness(0, 4, 0, 4) });
        if (D.Ports.Any(p => p.Media == "sfp")) sp.Children.Add(Note("Tap an SFP slot on the picture to plug in a module, or use the lists below."));
        var slots = D.Ports.Where(p => p.Media == "sfp").ToList();
        if (slots.Count > 0)
        {
            sp.Children.Add(Head("SFP slots"));
            sp.Children.Add(Note("Plug a module into a slot: copper (RJ-45), multimode fibre (up to 550 m) or single-mode fibre (up to 10 km). Both ends of a fibre cable need the same kind."));
            foreach (var p in slots)
            {
                var pp = p;
                var cb = new ComboBox { MinWidth = 260, MinHeight = 40, ItemsSource = new[] { "Empty", "Copper (RJ-45) module", "Multimode fibre module", "Single-mode fibre module" } };
                string[] ids = { "", "rj45", "mmf", "smf" };
                cb.SelectedIndex = Math.Max(0, Array.IndexOf(ids, p.Sfp));
                cb.SelectionChanged += (s, e) => { if (cb.SelectedIndex >= 0 && ids[cb.SelectedIndex] != pp.Sfp) { pp.Sfp = ids[cb.SelectedIndex]; D.P.Sim.TopologyChanged(); Changed(); } };
                OnRefresh(() => { int i = Math.Max(0, Array.IndexOf(ids, pp.Sfp)); if (cb.SelectedIndex != i) cb.SelectedIndex = i; });
                sp.Children.Add(new StackPanel { Spacing = 3, Margin = new Thickness(0, 0, 0, 8), Children = { Ui.Text(p.Name, 13, Ui.Muted), cb } });
            }
        }
        sp.Children.Add(Head("Cables"));
        var cables = MonoText("");
        OnRefresh(() =>
        {
            var lines = new List<string>();
            foreach (var p in D.Ports) foreach (var l in p.Links)
                {
                    var o = l.Other(p);
                    lines.Add(p.Short + "  ->  " + o.FullName + "\n    " + l.CableName + (l.IsWireless ? "" : ", " + l.Length.ToString("0.#", CultureInfo.InvariantCulture) + " m") + ", " + (l.Status == "up" ? "working" : l.Problem));
                }
            cables.Text = lines.Count == 0 ? "No cables yet." : string.Join("\n", lines);
        });
        sp.Children.Add(cables);
        sp.Children.Add(Head("Ports"));
        sp.Children.Add(MonoText(string.Join("\n", D.Ports.Where(p => !p.IsWireless).Select(p => p.Name + (p.Media == "sfp" ? " (SFP slot)" : p.IsSerial ? " (console)" : "") + (p.Links.Count > 0 ? "  - in use" : "")))));
        return sp;
    }

    // ------------------------------------------------------------ end devices: name, system, adapters, Wi-Fi
    Control HostConfig()
    {
        var sp = new StackPanel();
        sp.Children.Add(Head("Device"));
        var name = Field(sp, "Display name", D.Name, "The name shown under the picture.");
        name.LostFocus += (s, e) => { if (name.Text != D.Name && (name.Text ?? "").Trim().Length > 0) { Edits.Rename(D.P, Undo, D, name.Text.Trim()); Changed(); Refresh(); } };
        var hn = Field(sp, "Computer name", D.Host.HostName, "The name the computer calls itself (hostname); other computers can find it by this name on the same network.");
        hn.LostFocus += (s, e) => { var v = (hn.Text ?? "").Trim(); if (v.Length > 0 && v != D.Host.HostName) { D.Host.ComputerName = v; Changed(); } };
        var oss = Personalities.For(D.Kind).ToList();
        if (oss.Count > 1)
        {
            var cb = new ComboBox { MinWidth = 260, MinHeight = 40, ItemsSource = oss.Select(p => p.Title).ToList(), SelectedIndex = oss.FindIndex(p => p.Id == D.Os) };
            var blurb = Note(Personalities.Get(D.Os).Blurb);
            cb.SelectionChanged += (s, e) =>
            {
                if (cb.SelectedIndex < 0 || oss[cb.SelectedIndex].Id == D.Os) return;
                Personalities.Change(D, oss[cb.SelectedIndex].Id);
                Changed();
                MainView.Current?.Replace(new DevicePage(D, "Config"));   // its terminals and tabs change with the system
            };
            sp.Children.Add(new StackPanel { Spacing = 3, Margin = new Thickness(0, 0, 0, 4), Children = { Ui.Text("Operating system", 13, Ui.Muted), cb } });
            sp.Children.Add(blurb);
        }
        foreach (var f in D.Host.Adapters)
        {
            sp.Children.Add(Head(Personalities.Get(D.Os).IfName(f.Port) + (f.Port.IsWireless ? " (Wi-Fi)" : " (network socket)")));
            if (f.Port.IsWireless) sp.Children.Add(WifiPanel(f));
            sp.Children.Add(IpPanel(f));
        }
        if (D.Host.Adapters.Count == 0) sp.Children.Add(Note("This device has no network adapters."));
        return sp;
    }

    Control WifiPanel(IpIf f)
    {
        var sp = new StackPanel();
        var w = D.Host.WifiFor(f.Port);
        var ssid = Field(sp, "Network name (SSID)", w.Ssid, "The name of the Wi-Fi network to join.");
        var key = Field(sp, "Password (key)", w.Key, "The Wi-Fi password. Leave it empty for an open network.");
        var status = Note("");
        OnRefresh(() => status.Text = w.StatusText);
        var row = new WrapPanel();
        row.Children.Add(Btn("Connect", () => { D.Host.SetWifi(f.Port, (ssid.Text ?? "").Trim(), key.Text ?? ""); if (!f.Dhcp && f.Ip == 0) D.Host.SetDhcp(f); else if (f.Dhcp) D.Host.EnsureDhcp(f); Changed(); Refresh(); }, true));
        row.Children.Add(Btn("Disconnect", () => { D.Host.SetWifi(f.Port, "", ""); Changed(); Refresh(); }));
        sp.Children.Add(row);
        sp.Children.Add(status);
        return sp;
    }

    /// <summary>IP settings of one adapter: DHCP or static, with the DHCP result shown.</summary>
    public Control IpPanel(IpIf f)
    {
        var h = D.Host;
        var sp = new StackPanel();
        var rbD = new RadioButton { Content = "DHCP (automatic)", IsChecked = f.Dhcp, GroupName = "m" + f.Port.Index + D.Id, Margin = new Thickness(0, 0, 16, 0), MinHeight = 40 };
        var rbS = new RadioButton { Content = "Static", IsChecked = !f.Dhcp, GroupName = "m" + f.Port.Index + D.Id, MinHeight = 40 };
        sp.Children.Add(new WrapPanel { Margin = new Thickness(0, 0, 0, 6), Children = { rbD, rbS } });
        var ip = Field(sp, "IP address", f.Ip != 0 ? Ip.Str(f.Ip) : "", "This device's own address, for example 192.168.1.10.", 200);
        var mask = Field(sp, "Subnet mask", f.Mask != 0 ? Ip.Str(f.Mask) : "", "Which part of the address is the network: 255.255.255.0 means the first three numbers.", 200);
        var gw = Field(sp, "Default gateway", f.Gateway != 0 ? Ip.Str(f.Gateway) : "", "The router to send everything for other networks to.", 200);
        var dns = Field(sp, "DNS server", f.Dns.Count > 0 ? Ip.Str(f.Dns[0]) : "", "The server that turns names (www.example.com) into addresses.", 200);
        var msg = Note("");
        bool pendingStatic = false;
        void Apply()
        {
            if (rbD.IsChecked == true) { pendingStatic = false; if (!f.Dhcp) { h.SetDhcp(f); msg.Text = "Asking for an address..."; } }
            else
            {
                if ((ip.Text ?? "").Trim().Length == 0) { msg.Text = "Type an IP address and a subnet mask."; return; }
                if ((mask.Text ?? "").Trim().Length == 0 && Ip.TryParse(ip.Text, out uint tmp)) mask.Text = Ip.Str(Ip.MaskOf(Ip.ClassfulPrefix(tmp)));
                string err = h.SetStatic(f, ip.Text, mask.Text, (gw.Text ?? "").Trim(), (dns.Text ?? "").Trim(), f.Dns.Count > 1 ? Ip.Str(f.Dns[1]) : "");
                msg.Text = err.Length > 0 ? err : "Saved.";
                if (err.Length == 0) pendingStatic = false;
            }
            D.P.Sim.TopologyChanged();
            Changed();
        }
        rbD.IsCheckedChanged += (s, e) => { if (rbD.IsChecked == true) Apply(); };
        rbS.IsCheckedChanged += (s, e) =>
        {
            if (rbS.IsChecked != true) return;
            foreach (var t in new[] { ip, mask, gw, dns }) t.IsReadOnly = false;
            if (f.Dhcp) { pendingStatic = true; msg.Text = "Type the address, subnet mask, gateway and DNS server, then press Apply."; }
        };
        foreach (var t in new[] { ip, mask, gw, dns }) t.KeyDown += (s, e) => { if (e.Key == Avalonia.Input.Key.Enter) Apply(); };
        sp.Children.Add(Btn("Apply", Apply, true));
        sp.Children.Add(msg);
        OnRefresh(() =>
        {
            if (pendingStatic && f.Dhcp) return;   // the person's choice wins until Apply
            pendingStatic = false;
            bool dyn = f.Dhcp;
            foreach (var t in new[] { ip, mask, gw, dns }) t.IsReadOnly = dyn;
            if (dyn)
            {
                ip.Text = f.Ip != 0 ? Ip.Str(f.Ip) : ""; mask.Text = f.Mask != 0 ? Ip.Str(f.Mask) : "";
                gw.Text = f.Gateway != 0 ? Ip.Str(f.Gateway) : ""; dns.Text = f.Dns.Count > 0 ? Ip.Str(f.Dns[0]) : "";
                var c = f.DhcpC;
                msg.Text = !f.Up ? "Not connected (no cable or no Wi-Fi), so no DHCP yet." : c == null || c.State == "discovering" || c.State == "requesting" ? "Asking for an address (DHCP)..." : c.State == "bound" ? "DHCP request successful: the address came from " + Ip.Str(c.Server) + "." : c.State == "apipa" ? "DHCP failed: no DHCP server answered, so it uses an automatic 169.254 address (APIPA). It keeps trying." : "";
            }
            if (rbD.IsChecked != dyn && !rbS.IsFocused && !rbD.IsFocused) { rbD.IsChecked = dyn; rbS.IsChecked = !dyn; }
        });
        /// <summary>The checks: set a static address the way the form does.</summary>
        IpForms[f] = (a, m, g, d) => { rbS.IsChecked = true; ip.Text = a; mask.Text = m; gw.Text = g; dns.Text = d; Apply(); return msg.Text; };
        return sp;
    }

    /// <summary>The checks: each adapter's form, filled and applied (address, mask, gateway, DNS) -> the message.</summary>
    public readonly Dictionary<IpIf, Func<string, string, string, string, string>> IpForms = new();

    // ------------------------------------------------------------ the wireless router's setup page
    Control HomeConfig()
    {
        var c = D.Home;
        var sp = new StackPanel();
        sp.Children.Add(Note("This is the router's setup page, like the one you open in a browser at home. Change something, then Save."));
        sp.Children.Add(Head("Internet connection"));
        var mode = new ComboBox { MinWidth = 260, MinHeight = 40, ItemsSource = new[] { "Automatic (DHCP from the provider)", "Static address" }, SelectedIndex = c.WanMode == "static" ? 1 : 0, Margin = new Thickness(0, 0, 0, 8) };
        sp.Children.Add(mode);
        var wip = Field(sp, "Internet IP address", c.WanIp, "Only for a static address: the address your provider gave you.", 200);
        var wm = Field(sp, "Subnet mask", c.WanMask, null, 200);
        var wg = Field(sp, "Gateway", c.WanGw, "The provider's router.", 200);
        var wd = Field(sp, "DNS server", c.WanDns, null, 200);
        var wanStatus = Note("");
        sp.Children.Add(wanStatus);
        sp.Children.Add(Head("Home network (LAN) and DHCP"));
        var lip = Field(sp, "Router address", c.LanIp, "The router's own address in your home network: the default gateway for every device at home.", 200);
        var lm = Field(sp, "Subnet mask", c.LanMask, null, 200);
        var dh = new CheckBox { Content = "Hand out addresses (DHCP server)", IsChecked = c.DhcpOn, MinHeight = 40 };
        sp.Children.Add(dh);
        var st = Field(sp, "First address", c.DhcpStart, "The first address it hands out.", 200);
        var mx = Field(sp, "How many", c.DhcpMax.ToString(CultureInfo.InvariantCulture), "The most devices it hands addresses to.", 100);
        sp.Children.Add(Head("Wi-Fi"));
        var ssid = Field(sp, "Network name (SSID)", D.Ap.Ssid, "The name people see in their Wi-Fi list.");
        var sec = new ComboBox { MinWidth = 260, MinHeight = 40, ItemsSource = new[] { "Open (no password)", "WPA2 Personal (password)" }, SelectedIndex = D.Ap.Security == "open" ? 0 : 1, Margin = new Thickness(0, 0, 0, 8) };
        sp.Children.Add(sec);
        var key = Field(sp, "Wi-Fi password", D.Ap.Key, "At least 8 characters.");
        var msg = Note("");
        sp.Children.Add(Btn("Save", () =>
        {
            c.WanMode = mode.SelectedIndex == 1 ? "static" : "dhcp"; c.WanIp = (wip.Text ?? "").Trim(); c.WanMask = (wm.Text ?? "").Trim(); c.WanGw = (wg.Text ?? "").Trim(); c.WanDns = (wd.Text ?? "").Trim();
            c.LanIp = (lip.Text ?? "").Trim(); c.LanMask = (lm.Text ?? "").Trim(); c.DhcpOn = dh.IsChecked == true; c.DhcpStart = (st.Text ?? "").Trim();
            if (int.TryParse(mx.Text, out int n)) c.DhcpMax = n;
            if (sec.SelectedIndex == 1 && (key.Text ?? "").Length < 8) { msg.Text = "A WPA2 password needs at least 8 characters."; return; }
            D.Ap.Ssid = (ssid.Text ?? "").Trim(); D.Ap.Security = sec.SelectedIndex == 0 ? "open" : "wpa2"; D.Ap.Key = key.Text ?? "";
            string err = HomeRouter.Apply(D);
            D.P.Sim.TopologyChanged();
            msg.Text = err.Length > 0 ? err : "Saved.";
            Changed();
        }, true));
        sp.Children.Add(msg);
        sp.Children.Add(Head("Devices at home (DHCP)"));
        var clients = MonoText("");
        sp.Children.Add(clients);
        OnRefresh(() =>
        {
            var w = HomeRouter.Wan(D);
            wanStatus.Text = w.Up ? (w.Ip != 0 ? "Connected: internet address " + Ip.Cidr(w.Ip, w.Mask) + (w.Dhcp ? " (from the provider)" : "") : "The cable is in, waiting for an address...") : "Not connected: plug the Internet port into the internet box (or the provider's equipment).";
            var leases = D.Ip.Dhcp.Leases.Values.OrderBy(l => l.Ip).ToList();
            clients.Text = leases.Count == 0 ? "None yet." : string.Join("\n", leases.Select(l => Ip.Str(l.Ip).PadRight(16) + (D.P.Devices.FirstOrDefault(x => x.Ports.Any(p => p.Mac == l.Mac)) ?? D).Name));
        });
        return sp;
    }

    Control ApConfig()
    {
        var sp = new StackPanel();
        sp.Children.Add(Note("An access point adds Wi-Fi to a wired network. Wi-Fi devices join it with the network name and the password; it passes their traffic to Port0."));
        var on = new CheckBox { Content = "Wi-Fi on", IsChecked = D.Ap.Enabled, MinHeight = 40 };
        sp.Children.Add(on);
        var ssid = Field(sp, "Network name (SSID)", D.Ap.Ssid);
        var sec = new ComboBox { MinWidth = 260, MinHeight = 40, ItemsSource = new[] { "Open (no password)", "WPA2 Personal (password)" }, SelectedIndex = D.Ap.Security == "open" ? 0 : 1, Margin = new Thickness(0, 0, 0, 8) };
        sp.Children.Add(sec);
        var key = Field(sp, "Password", D.Ap.Key);
        var range = Field(sp, "Range (metres, open space)", D.Ap.Range.ToString("0", CultureInfo.InvariantCulture), "How far the signal reaches with no walls in between; walls and floors cut it down.", 100);
        var msg = Note("");
        sp.Children.Add(Btn("Save", () =>
        {
            if (sec.SelectedIndex == 1 && (key.Text ?? "").Length < 8) { msg.Text = "A WPA2 password needs at least 8 characters."; return; }
            D.Ap.Enabled = on.IsChecked == true; D.Ap.Ssid = (ssid.Text ?? "").Trim(); D.Ap.Security = sec.SelectedIndex == 0 ? "open" : "wpa2"; D.Ap.Key = key.Text ?? "";
            if (double.TryParse(range.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double r) && r > 0) D.Ap.Range = r;
            D.P.Sim.TopologyChanged(); msg.Text = "Saved."; Changed();
        }, true));
        sp.Children.Add(msg);
        var who = Note("");
        OnRefresh(() => { var st = D.Ap.Radio.Links.Select(l => l.Other(D.Ap.Radio).Dev.Name).ToList(); who.Text = st.Count == 0 ? "Nobody is connected." : "Connected: " + string.Join(", ", st); });
        sp.Children.Add(who);
        return sp;
    }

    Control CloudInfo()
    {
        var sp = new StackPanel();
        sp.Children.Add(Note(Catalog.Explain("cloud")));
        sp.Children.Add(Head("Its sockets (each hands out public addresses with DHCP)"));
        foreach (var f in D.Ip.Ifs.Where(x => x.Port != null))
            sp.Children.Add(MonoText(f.Port.Short + ": network " + Ip.Cidr(f.Ip & f.Mask, f.Mask) + ", its address (your gateway) " + Ip.Str(f.Ip)));
        sp.Children.Add(Head("Web sites on the pretend internet"));
        foreach (var s in Cloud.Sites) sp.Children.Add(MonoText(s[0] + "  =  " + s[1]));
        sp.Children.Add(Note("Its DNS server is 198.51.100.53. Private addresses (10.x, 172.16-31.x, 192.168.x) can't travel here: a router with NAT has to swap them for a public one."));
        return sp;
    }
}
