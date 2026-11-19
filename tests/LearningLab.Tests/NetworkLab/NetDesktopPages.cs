// Network Lab - the device screens, drawn off screen with Avalonia's headless platform (group "network-desktops"): the
// pretend desktops (simple, Windows-like 10 and 11, Ubuntu-like, macOS-like), phones, the printer's front panel, the TV,
// the NAS and a Linux server's console; the browser opening a server's page (by address, by name, a link), Printers
// (add one, print a test page), the Windows-like Settings (a static address), the Control Panel's IPv4 form, File
// Explorer on a shared folder, Services (a DNS record, a web page), the router and switch Config forms (and the commands
// they typed, seen in the CLI); wide and phone-sized. Also App Hub's IP window check (Static doesn't snap back to DHCP)
// and the Tab checks of the terminals the desktops open.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using LearningLab.NetworkLab;
using LearningLab.Views;
using NetworkLab;

namespace LearningLab.Tests;

static class NetDesktopPages
{
    static void Check(string what, bool ok, string detail = null) => Program.Check("network desktops: " + what, ok, detail);
    static Window win;
    static MainView mv;
    static Project P;
    static void Pump() => Dispatcher.UIThread.RunJobs();
    static void Snap(string name) => LearningLab.Tests.Snap.Save(win, name);
    static void Run(long ms) { P.Sim.RunFor(ms); Pump(); }

    public static void Run()
    {
        Brand.LoadBuiltIn(null);
        _ = Data.For("NetworkLab");
        win = new Window { Width = 1180, Height = 800, Content = new MainView() };
        win.Show();
        mv = MainView.Current;
        try { Wide(); Phone(); IpWindowChecks(); TerminalChecks(); }
        finally { while (mv.Back()) { } win.Close(); DevicePage.Edited = null; }
    }

    static Project Build(string script)
    {
        var p = new Project(11);
        var errs = Script.Run(p, script);
        if (errs.Count > 0) throw new Exception(string.Join("; ", errs));
        p.Sim.RunFor(500);
        return p;
    }

    const string Lab =
        "add switch SW 0 0\nadd router R 0 0\nadd pc W 0 0 win11\nadd pc W10 0 0 win10\nadd pc G 0 0\nadd server S 0 0\nadd printer P 0 0\n" +
        "add laptop U 0 0 ubuntu\nadd laptop M 0 0 macos\nadd server L 0 0 ubuntu-server\nadd ap AP 0 0\nadd phone A 0 0 android\nadd tablet T 0 0 iphone\nadd tv TV 0 0\nadd nas NAS 0 0\n" +
        "cable W Fa0 SW Fa0/1\ncable W10 Fa0 SW Fa0/2\ncable G Fa0 SW Fa0/3\ncable S Fa0 SW Fa0/4\ncable P Fa0 SW Fa0/5\ncable U Fa0 SW Fa0/6\ncable M Fa0 SW Fa0/7\n" +
        "cable L Fa0 SW Fa0/8\ncable AP Port0 SW Fa0/9\ncable NAS Fa0 SW Fa0/10\ncable R Gi0/0 SW Fa0/11\n" +
        "ap AP ssid=Lab security=wpa2 key=secret99\n" +
        "ip W10 10.5.0.3/24\nip G 10.5.0.4/24\nip S 10.5.0.9/24\nip P 10.5.0.60/24\nip U 10.5.0.6/24\nip M 10.5.0.7/24\nip L 10.5.0.8/24\nip NAS 10.5.0.10/24\n" +
        "web S on\ndns S on\nsmb S Public readme.txt";

    static Button Find(Control root, string text) =>
        root.GetLogicalDescendants().OfType<Button>().FirstOrDefault(b => Label(b) == text) ??
        root.GetLogicalDescendants().OfType<Button>().FirstOrDefault(b => Label(b).StartsWith(text)) ??
        root.GetLogicalDescendants().OfType<Button>().FirstOrDefault(b => b.GetLogicalDescendants().OfType<TextBlock>().Any(t => (t.Text ?? "").StartsWith(text)));
    static string Label(Button b) => b.Content as string ?? (b.Content as TextBlock)?.Text ?? "";
    static bool Click(Control root, string text)
    {
        var b = Find(root, text);
        if (b == null) return false;
        b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump();
        return true;
    }
    static string Texts(Control root) => string.Join("\n", root.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text));
    /// <summary>Every button on screen is at least 44 px high (touch first); returns the ones that aren't.</summary>
    static List<string> Small(Control root) => root.GetVisualDescendantsSafe().OfType<Button>()
        .Where(b => b.GetType() == typeof(Button) && b.IsEffectivelyVisible && b.Bounds.Height > 0 && b.Bounds.Height < 43.5).Select(b => Label(b) + " " + b.Bounds.Height.ToString("0")).ToList();

    static DevicePage Open(Device d, string tab = null)
    {
        var dp = new DevicePage(d, tab);
        mv.Push(dp); Pump();
        return dp;
    }

    static void Wide()
    {
        P = Build(Lab);
        Run(3000);
        var w = P.Find("W"); var s = P.Find("S"); var pr = P.Find("P");
        var f = w.Host.Adapters.First(a => !a.Port.IsWireless);

        // ---- Windows 11-like: the desktop, Start, Settings > a static address
        var dp = Open(w);
        Check("an end device's page opens on its Desktop", dp.Tab == "Desktop" && dp.Desktop is WinDesktop, string.Join(",", dp.Tabs));
        var desk = (WinDesktop)dp.Desktop;
        desk.ToggleStart(); Pump(); Snap("desk-win11-start");
        Check("the Start menu opens", desk.StartOpen);
        var ws = (WinSettings)desk.OpenApp("settings"); Pump();
        Check("Start's app opens Settings in a window (and Start closes)", ws != null && !desk.StartOpen && desk.Desk.Windows.Count() == 1);
        ws.Show("adapter:0"); Pump();
        Check("the adapter page has Edit for the IP assignment", Click(ws, "Edit"));
        var ed = ws.LastEditor;
        ed.Mode.SelectedIndex = 1; ed.Ip.Text = "10.5.0.2"; ed.Mask.Text = "255.255.255.0"; ed.Gateway.Text = "10.5.0.1"; ed.Dns1.Text = "10.5.0.9";
        Pump(); Snap("desk-win11-ip");
        ed.Save(); Pump();
        Check("Settings > Edit IP settings > Manual > Save gives a static address", !f.Dhcp && Ip.Str(f.Ip) == "10.5.0.2" && f.Gateway == Ip.Parse("10.5.0.1") && f.Dns.Count == 1, Ip.Str(f.Ip) + " " + ed.Error.Text);
        Check("the adapter page shows the new address", Texts(ws).Contains("10.5.0.2"));
        ed = null;
        Click(ws, "Edit"); ws.LastEditor.Mode.SelectedIndex = 1; ws.LastEditor.Ip.Text = "10.5.0.300"; ws.LastEditor.Save(); Pump();
        Check("a wrong address is refused with a message", ws.LastEditor.Error.Text.Length > 0 && Ip.Str(f.Ip) == "10.5.0.2", ws.LastEditor.Error.Text);
        Run(2000);

        // ---- the browser: by address, a link, by name (DNS), an error page
        s.Host.Pages["index.html"] = "<h1>Lab server</h1><p>Hello from <b>S</b>. See <a href=\"/about.html\">about us</a>.</p><ul><li>one</li><li>two</li></ul><form action=\"/search\" method=\"get\"><input type=\"text\" name=\"q\"><input type=\"submit\" value=\"Search\"></form>";
        s.Host.Pages["about.html"] = "<h2>About</h2><p>We are a pretend server.</p>";
        var br = (MiniBrowser)desk.OpenApp("browser"); Pump();
        br.Go("10.5.0.9", true); Run(3000);
        Check("the browser opens a server's page by its address", br.PageText.Contains("Lab server") && br.PageText.Contains("Hello from S"), br.PageText);
        Check("the page's link, list and form are drawn", br.Shown.Links.Count == 1 && br.PageText.Contains("two") && br.Shown.Fields.ContainsKey("q") && br.Shown.Buttons.Count == 1);
        Snap("desk-win11-browser");
        br.Shown.Links[0].Open(); Run(3000);
        Check("tapping a link opens that page", br.PageText.Contains("pretend server") && br.Current.EndsWith("/about.html"), br.PageText);
        br.Go("http://10.5.0.99/", true); Run(8000);
        Check("an address nobody has: a page that says why", br.PageText.Contains("can't be reached"), br.PageText);
        Check("the desktop's windows: Settings and the browser, the browser in front", desk.Desk.Windows.Count() == 2 && desk.Desk.Top.Id == "browser");

        // ---- Printers & scanners: add the printer, print a test page
        var ps = (WinSettings)desk.OpenApp("printers"); Pump();
        var pa = ps.Page as DesktopApps.PrintersApp;
        Check("Printers & scanners opens in Settings", pa != null);
        pa.Address.Text = "10.5.0.60"; pa.NameBox.Text = "Laser";
        Click(pa, "Add");
        Check("a printer is added by its address", w.Host.Printers.Any(x => x[0] == "Laser" && x[1] == "10.5.0.60"));
        Click(pa, "Print a test page"); Run(4000);
        Check("the test page prints on the printer", pr.Host.Printed.Count == 1 && pa.Status.Contains("was sent"), pa.Status);
        Pump(); Snap("desk-win11-printers");

        // ---- File Explorer on the server's shared folder; Command Prompt
        var ex = (WinExplorer)desk.OpenApp("explorer"); Pump();
        ex.Go("\\\\10.5.0.9\\Public"); Run(4000);
        Check("File Explorer opens \\\\server\\share", ex.ItemsText.Contains("readme.txt"), ex.ItemsText);
        var cmd = (NetTerminal)desk.OpenApp("cmd"); Pump();
        cmd.TypeLine("ipconfig"); cmd.Poll(); Pump();
        Check("the Command Prompt window runs ipconfig", cmd.Text.Contains("10.5.0.2"));
        Snap("desk-win11-windows");
        Check("buttons on the Windows-like desktop are 44 px or more", Small(dp.View) is var sm && sm.Count == 0, string.Join(", ", sm.Take(5)));
        mv.Back(); Pump();

        // ---- Services on the server: a DNS record, then the PC browses by name; editing the page
        var sp = Open(s, "Services");
        var svc = sp.Services;
        Check("a server's page has Services", svc != null && sp.Tabs.Contains("Services") && sp.Tabs[0] == "Desktop", string.Join(",", sp.Tabs));
        svc.Show("DNS"); Pump();
        svc.Boxes["name"].Text = "www.lab.local"; svc.Boxes["address"].Text = "10.5.0.9";
        svc.Actions["add record"](); Pump();
        Check("Services > DNS adds a record", s.Ip.DnsSrv.Records.TryGetValue("www.lab.local", out uint rec) && rec == Ip.Parse("10.5.0.9"));
        svc.Boxes["name"].Text = "bad"; svc.Boxes["address"].Text = "nope"; svc.Actions["add record"]();
        Check("a record without a valid address is refused", svc.Message.Text.Length > 0 && !s.Ip.DnsSrv.Records.ContainsKey("bad"));
        Snap("desk-services-dns");
        svc.Show("Web (HTTP)"); Pump();
        svc.Actions["edit index"](); svc.Boxes["page"].Text = "<h1>Edited page</h1><p>New words.</p>"; svc.Actions["save page"](); Pump();
        Check("Services > Web edits a page", s.Host.Pages["index.html"].Contains("Edited page"));
        Snap("desk-services-web");
        svc.Show("DHCP"); Pump(); Snap("desk-services-dhcp");
        svc.Show("Shared folders"); Pump(); Snap("desk-services-smb");
        mv.Back(); Pump();
        dp = Open(w); desk = (WinDesktop)dp.Desktop;
        br = (MiniBrowser)desk.OpenApp("browser");
        br.Go("www.lab.local", true); Run(4000);
        Check("the PC finds the page by its name (the DNS record)", br.PageText.Contains("Edited page"), br.PageText);
        mv.Back(); Pump();

        // ---- Windows 10-like: the Control Panel's IPv4 form, the firewall
        var w10 = P.Find("W10");
        dp = Open(w10);
        var d10 = (WinDesktop)dp.Desktop;
        var cp = (WinControlPanel)d10.OpenApp("control"); Pump();
        cp.Sharing(); Pump();
        Check("the Control Panel: Network and Sharing Center", cp.Where.Contains("Sharing Center") && Click(cp, "Change adapter settings") && cp.Where.Contains("Network Connections"));
        cp.Ipv4(w10.Host.Adapters[0]); Pump(); Snap("desk-win10-ipv4");
        Check("the IPv4 properties form shows the static address", cp.GetLogicalDescendants().OfType<TextBox>().Any(t => t.Text == "10.5.0.3"));
        cp.Firewall(); Pump();
        var fwOff = cp.GetLogicalDescendants().OfType<RadioButton>().First(r => ((r.Content as TextBlock)?.Text ?? "").StartsWith("Turn off"));
        fwOff.IsChecked = true; Pump();
        Check("the firewall can be turned off", !w10.Host.WinFirewall);
        Snap("desk-win10-firewall");
        mv.Back(); Pump();

        // ---- the simple desktop: tiles, IP Configuration, the browser
        var g = P.Find("G");
        dp = Open(g);
        var gd = (GenericDesktop)dp.Desktop;
        Snap("desk-generic");
        gd.OpenApp("ip"); Pump(); Snap("desk-generic-ip");
        Check("the simple desktop's IP Configuration shows the address", dp.View.GetLogicalDescendants().OfType<TextBox>().Any(t => t.Text == "10.5.0.4"));
        var gb = gd.OpenApp("browser").GetLogicalDescendants().OfType<MiniBrowser>().First();
        gb.Go("10.5.0.9", true); Run(3000);
        Check("the simple desktop's Web Browser", gb.PageText.Contains("Edited page"), gb.PageText);
        Snap("desk-generic-browser");
        mv.Back(); Pump();

        // ---- Ubuntu-like and macOS-like
        var u = P.Find("U");
        dp = Open(u);
        var ld = (LinuxDesktop)dp.Desktop;
        var lset = (LinuxSettings)ld.OpenApp("settings"); Pump();
        Click(lset, "Connected"); Pump();
        Check("Ubuntu-like Settings > Wired > IPv4", Texts(lset).Contains("Wired: IPv4"));
        Snap("desk-ubuntu-settings");
        var lt = (NetTerminal)ld.OpenApp("terminal"); Pump();
        lt.TypeLine("ip a"); lt.Poll(); Pump();
        Check("the Ubuntu-like terminal", lt.Text.Contains("10.5.0.6"));
        Snap("desk-ubuntu-terminal");
        mv.Back(); Pump();
        var m = P.Find("M");
        dp = Open(m);
        var md = (MacDesktop)dp.Desktop;
        var mset = (MacSettings)md.OpenApp("settings"); Pump();
        mset.Details(m.Host.Adapters.First(a => !a.Port.IsWireless), "tcp");
        Pump(); Snap("desk-mac-settings");
        md.ToggleWifiMenu(); Pump(); Snap("desk-mac-wifi");
        Check("the macOS-like Wi-Fi menu lists the network", md.WifiMenuOpen && Texts(md).Contains("Lab"));
        mv.Back(); Pump();

        // ---- a Linux server: its Terminal is the console screen
        dp = Open(P.Find("L"));
        Check("a Linux server has no desktop: it opens on its console", dp.Desktop == null && dp.Tabs[0] == "Terminal" && dp.Shell != null, string.Join(",", dp.Tabs));
        Snap("desk-linux-server");
        mv.Back(); Pump();

        // ---- the phone: Wi-Fi with a password, then the browser
        var ph = P.Find("A");
        dp = Open(ph);
        var scr = (PhoneScreen)dp.Desktop;
        Check("a phone opens on its Screen", dp.Tab == "Screen");
        Snap("desk-android-home");
        scr.OpenApp("wifi"); Run(500); dp.Refresh(); Pump();
        var net = dp.View.GetLogicalDescendants().OfType<Button>().FirstOrDefault(b => (b.Tag as string) == "wifi:Lab");
        Check("the phone's Wi-Fi list shows the network", net != null);
        Snap("desk-android-wifi");
        net?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
        var key = dp.View.GetLogicalDescendants().OfType<TextBox>().FirstOrDefault(t => t.PasswordChar == '*');
        Check("tapping a secured network asks for its password", key != null);
        if (key != null) { key.Text = "secret99"; Click(dp.View, "Connect"); }
        Run(4000);
        Check("the phone joins the Wi-Fi", ph.Host.Wifi[0].Status == "connected", ph.Host.Wifi[0].StatusText);
        scr.OpenApp("ip"); Pump();
        var ipb = dp.View.GetLogicalDescendants().OfType<ComboBox>().First();
        ipb.SelectedIndex = 1; Pump();
        var pboxes = dp.View.GetLogicalDescendants().OfType<TextBox>().ToList();
        pboxes[0].Text = "10.5.0.30"; pboxes[1].Text = "10.5.0.1"; pboxes[2].Text = "24"; Pump();
        Snap("desk-android-ip");
        Click(dp.View, "Save"); Pump();
        Check("the phone's IP settings: a static address", ph.Host.Main.Ip == Ip.Parse("10.5.0.30") && !ph.Host.Main.Dhcp, Ip.Str(ph.Host.Main.Ip));
        mv.Back(); Pump();
        dp = Open(P.Find("T")); Snap("desk-iphone-tablet"); mv.Back(); Pump();

        // ---- the printer's front panel: print a test page from its menu
        dp = Open(pr);
        var pp = (PrinterPanel)dp.Desktop;
        Check("a printer opens on its Front panel", dp.Tab == "Front panel" && pp.Lcd.Contains("10.5.0.60"), pp.Lcd);
        int before = pr.Host.Printed.Count;
        foreach (var k in new[] { "Menu", "Down", "OK", "Down", "OK" }) pp.Press(k);
        Pump();
        Check("Menu > Print reports > Test page prints", pr.Host.Printed.Count == before + 1, pp.Lcd);
        Snap("desk-printer");
        mv.Back(); Pump();

        // ---- the TV and the NAS
        dp = Open(P.Find("TV"));
        var tv = (TvScreen)dp.Desktop;
        tv.OpenApp("wifi"); Pump();
        Check("the TV lists the Wi-Fi network", tv.ScreenText.Contains("Lab"));
        Snap("desk-tv");
        mv.Back(); Pump();
        dp = Open(P.Find("NAS"));
        Check("the NAS: a status screen and Services", dp.Desktop is ApplianceScreen && dp.Tabs.Contains("Services"));
        dp.Refresh(); Pump(); Snap("desk-nas");
        mv.Back(); Pump();

        // ---- the router: Config forms type the commands; the CLI shows them
        var r = P.Find("R");
        var rp = Open(r, "Config");
        var cfg = rp.IosConfig;
        Check("a router has CLI then Config", rp.Tabs[0] == "CLI" && rp.Tabs[1] == "Config" && cfg != null, string.Join(",", rp.Tabs));
        cfg.Show("Interfaces"); Pump();
        int gi = r.Ports.Where(x => !x.IsSerial).ToList().FindIndex(x => x.Name.EndsWith("0/0"));
        cfg.PortPicker.SelectedIndex = gi; Pump();
        cfg.Boxes["ip"].Text = "10.5.0.1"; cfg.Boxes["mask"].Text = "255.255.255.0";
        cfg.Actions["set address"](); cfg.Actions["port on"](); Pump();
        var gp = r.Ports[gi];
        Check("the interface form sets the address and turns the port on", gp.L3.Ip == Ip.Parse("10.5.0.1") && !gp.Shutdown, cfg.Message.Text);
        Check("Equivalent commands show what the form typed", cfg.Log.Contains("interface " + gp.Name) && cfg.Log.Contains("ip address 10.5.0.1 255.255.255.0") && cfg.Log.Contains("no shutdown"), cfg.Log);
        Snap("desk-router-config");
        cfg.Show("Settings"); Pump();
        cfg.Boxes["hostname"].Text = "EDGE"; cfg.Actions["hostname"](); Pump();
        Check("Settings > Set hostname", r.Ios.Hostname == "EDGE" && cfg.Log.Contains("hostname EDGE"));
        cfg.Show("Static routes"); Pump();
        cfg.Boxes["network"].Text = "192.168.3.0"; cfg.Boxes["next hop"].Text = "10.5.0.254"; cfg.Actions["add"](); Pump();
        Check("Static routes > Add", r.Ip.Static.Any(x => x.Net == Ip.Parse("192.168.3.0")) && cfg.Log.Contains("ip route 192.168.3.0 255.255.255.0 10.5.0.254"), cfg.Message.Text);
        cfg.Boxes["network"].Text = "nonsense"; cfg.Actions["add"](); Pump();
        Check("a wrong route shows the router's error", cfg.Message.Text.StartsWith("%"), cfg.Message.Text);
        Snap("desk-router-routes");
        rp.ShowTab("CLI"); Pump();
        rp.Cli.TypeLine(""); rp.Cli.TypeLine("enable"); rp.Cli.TypeLine("show running-config"); rp.Cli.Poll(); Pump();
        Check("the CLI sees what the forms typed", rp.Cli.Text.Contains("hostname EDGE") && rp.Cli.Text.Contains("ip address 10.5.0.1 255.255.255.0") && rp.Cli.Text.Contains("ip route 192.168.3.0"), rp.Cli.Text.Substring(Math.Max(0, rp.Cli.Text.Length - 600)));
        Run(3000);
        Check("and the PC can ping the router it set up", global::NetworkLab.Check.CanPing(P, "W", "10.5.0.1"));
        mv.Back(); Pump();

        // ---- the switch: a VLAN and an access port
        var swp = Open(P.Find("SW"), "Config");
        var sc = swp.IosConfig;
        sc.Show("VLANs"); Pump();
        sc.Boxes["number"].Text = "10"; sc.Boxes["name"].Text = "Office"; sc.Actions["add"](); Pump();
        Check("the switch's VLAN form adds VLAN 10", P.Find("SW").Bridge.Vlans.ContainsKey(10) && sc.Log.Contains("vlan 10") && sc.Log.Contains("name Office"), sc.Log);
        Snap("desk-switch-vlans");
        sc.Show("Vlan1 (address)"); Pump();
        sc.Boxes["ip"].Text = "10.5.0.250"; sc.Boxes["mask"].Text = "255.255.255.0"; sc.Actions["apply"](); Pump();
        Check("the switch's Vlan1 form gives it an address", P.Find("SW").Bridge.Svis[1].Ip == Ip.Parse("10.5.0.250"), sc.Message.Text);
        mv.Back(); Pump();
    }

    static void Phone()
    {
        win.Width = 400; win.Height = 840; Pump();
        try
        {
            var dp = Open(P.Find("W"));
            var desk = (WinDesktop)dp.Desktop;
            var ws = (WinSettings)desk.OpenApp("settings"); Pump();
            Snap("desk-phone-win11-settings");
            var iw = desk.Desk.Top;
            Check("on a phone a window fills the desktop", desk.Desk.Compact && Math.Abs(iw.Bounds.Width - desk.Desk.Bounds.Width) < 1 && Math.Abs(iw.Bounds.Height - desk.Desk.Bounds.Height) < 1, iw.Bounds + " in " + desk.Desk.Bounds);
            ws.Show("adapter:0"); Click(ws, "Edit"); Pump(); Snap("desk-phone-win11-ip");
            Check("buttons on the phone-sized Settings are 44 px or more", Small(dp.View) is var sm && sm.Count == 0, string.Join(", ", sm.Take(5)));
            desk.OpenApp("browser"); var br = (MiniBrowser)desk.Desk.Top.Body.Content; br.Go("10.5.0.9", true); Run(3000); Snap("desk-phone-win11-browser");
            iw = desk.Desk.Top; iw.Close(); Pump();
            Check("the window's close button goes back to the one under it", desk.Desk.Top?.Id == "settings");
            desk.Desk.CloseAll(); Pump(); desk.ToggleStart(); Pump(); Snap("desk-phone-win11-start");
            mv.Back(); Pump();

            dp = Open(P.Find("W10")); ((WinDesktop)dp.Desktop).OpenApp("control"); Pump(); Snap("desk-phone-win10-control"); mv.Back(); Pump();
            dp = Open(P.Find("G")); Snap("desk-phone-generic"); ((GenericDesktop)dp.Desktop).OpenApp("printers"); Pump(); Snap("desk-phone-generic-printers"); mv.Back(); Pump();
            dp = Open(P.Find("U")); ((LinuxDesktop)dp.Desktop).OpenApp("settings"); Pump(); Snap("desk-phone-ubuntu"); mv.Back(); Pump();
            dp = Open(P.Find("M")); ((MacDesktop)dp.Desktop).ToggleWifiMenu(); Pump(); Snap("desk-phone-mac-wifi"); mv.Back(); Pump();
            dp = Open(P.Find("A")); Snap("desk-phone-android"); ((PhoneScreen)dp.Desktop).OpenApp("browser"); Run(3000); Snap("desk-phone-android-browser"); mv.Back(); Pump();
            dp = Open(P.Find("P")); Snap("desk-phone-printer");
            Check("the printer's keys are 44 px or more on a phone", Small(dp.View) is var ps && ps.Count == 0, string.Join(", ", ps.Take(5)));
            mv.Back(); Pump();
            dp = Open(P.Find("TV")); Snap("desk-phone-tv"); mv.Back(); Pump();
            dp = Open(P.Find("S"), "Services"); dp.Services.Show("DHCP"); Pump(); Snap("desk-phone-services"); mv.Back(); Pump();
            dp = Open(P.Find("R"), "Config"); dp.IosConfig.Show("Interfaces"); Pump(); Snap("desk-phone-router-config");
            Check("the Config tab's buttons are 44 px or more on a phone", Small(dp.View) is var cs && cs.Count == 0, string.Join(", ", cs.Take(5)));
            mv.Back(); Pump();
        }
        finally { win.Width = 1180; win.Height = 800; Pump(); }
    }

    // App Hub's IpWindowChecks (Andrew: a PC set to DHCP snapped back to DHCP when he picked Static, because the window
    // refreshed every 400 ms and put the buttons back as soon as he tapped into a box to type the address).
    static void IpWindowChecks()
    {
        var p = Build("add pc PC1 0 0\ndhcp PC1");
        var pc = p.Find("PC1");
        var dp = Open(pc, "Config");
        var f = pc.Host.Adapters.First(x => !x.Port.IsWireless);
        var radios = dp.View.GetLogicalDescendants().OfType<RadioButton>().ToList();
        var rbD = radios.First(r => ("" + r.Content).StartsWith("DHCP")); var rbS = radios.First(r => "" + r.Content == "Static");
        var boxes = dp.View.GetLogicalDescendants().OfType<TextBox>().ToList();
        var ipBox = boxes.First(b => ToolTip.GetTip(b) is string t && t.StartsWith("This device's own address"));
        int i0 = boxes.IndexOf(ipBox);
        Check("starts on DHCP", f.Dhcp && rbD.IsChecked == true);
        rbS.IsChecked = true;
        ipBox.Focus();
        dp.Refresh(); dp.Refresh(); Pump();
        Check("picking Static stays on Static while typing (it used to snap back to DHCP)", rbS.IsChecked == true && rbD.IsChecked != true);
        Check("the address boxes stay editable after Static is picked", !ipBox.IsReadOnly);
        boxes[i0].Text = "192.168.5.20"; boxes[i0 + 1].Text = "255.255.255.0"; boxes[i0 + 2].Text = "192.168.5.1";
        Click(dp.View, "Apply");
        dp.Refresh(); Pump();
        Check("Apply saves the static address and the page keeps showing Static", !f.Dhcp && Ip.Str(f.Ip) == "192.168.5.20" && rbS.IsChecked == true, f.Dhcp + " " + Ip.Str(f.Ip));
        rbD.IsChecked = true; dp.Refresh(); Pump();
        Check("switching back to DHCP works", f.Dhcp && rbD.IsChecked == true);
        mv.Back(); Pump();
    }

    // The Tab parts of App Hub's TerminalChecks, on terminals the desktops open.
    static void TerminalChecks()
    {
        var p = Build("add pc W 0 0 win11\nadd laptop U 0 0 ubuntu");
        var dp = Open(p.Find("W"));
        var desk = (WinDesktop)dp.Desktop;
        var cmd = (NetTerminal)desk.OpenApp("cmd"); Pump();
        cmd.Typed = "ipc"; cmd.Tab(false);
        Check("Command Prompt: Tab completes a command", cmd.Typed == "ipconfig", cmd.Typed);
        cmd.Typed = "ipconfig /r"; cmd.Tab(false); string r1 = cmd.Typed; cmd.Tab(false); string r2 = cmd.Typed; cmd.Tab(true);
        Check("Command Prompt: Tab cycles through the matches, Shift+Tab goes back", r1 == "ipconfig /release" && r2 == "ipconfig /renew" && cmd.Typed == "ipconfig /release", r1 + " | " + r2 + " | " + cmd.Typed);
        var ps = (NetTerminal)desk.OpenApp("powershell"); Pump();
        ps.Typed = "Get-NetI"; ps.Tab(false); string g1 = ps.Typed; ps.Tab(false);
        Check("PowerShell: Get-NetI then Tab, Tab", g1 == "Get-NetIPAddress" && ps.Typed == "Get-NetIPConfiguration", g1 + " | " + ps.Typed);
        int pf = ps.Flashes; ps.Typed = "Zork-"; ps.Tab(false);
        Check("PowerShell: no match: a flash", ps.Typed == "Zork-" && ps.Flashes == pf + 1);
        ps.TypeLine("exit"); ps.Poll(); Pump();
        Check("exit closes the PowerShell window", desk.Desk.Find("powershell") == null);
        mv.Back(); Pump();
        dp = Open(p.Find("U"));
        var lin = (NetTerminal)((LinuxDesktop)dp.Desktop).OpenApp("terminal"); Pump();
        lin.Typed = "netp"; lin.Tab(false);
        Check("Linux: Tab completes a command", lin.Typed == "netplan ", lin.Typed);
        lin.Typed = "cat /etc/netp"; lin.Tab(false);
        Check("Linux: Tab completes a folder (no space after it)", lin.Typed == "cat /etc/netplan/", lin.Typed);
        mv.Back(); Pump();
    }
}

static class VisualExt
{
    public static IEnumerable<Avalonia.Visual> GetVisualDescendantsSafe(this Avalonia.Visual v) => Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(v);
}
