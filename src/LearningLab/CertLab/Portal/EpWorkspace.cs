// Cert Lab - the MD-102 course's practice area (App Hub's UiEpWorkspace / UiEpPortal / UiEpNav for Avalonia and touch):
// the pretend admin portal (its menu of blades; lists, forms, device pages) and the simulated devices (state, PowerShell
// console, the pretend Settings app). Lab and exam pages host it in place of terminals when the course's simulator is
// "endpoint". On a wide screen the menu sits on the left; under 760 px it is a drawer behind the menu button, tables
// become cards and labels sit above their values. Nothing here changes the tenant except through EpOps and EpSim's
// directives, so a lab's model solution and a person's taps go through the same rules. Nothing signs in anywhere.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using LearningLab.Views;
using CertLab;

namespace LearningLab.CertLab.Portal;

public partial class EpWorkspace : Grid
{
    public readonly EpSim Sim;
    public readonly bool Exam;
    readonly Action<string> status;
    /// <summary>The consoles open in the workspace (terminal controls; the page polls them through Tick).</summary>
    public readonly List<Control> Terms = new();
    public const double NarrowBelow = 760;

    Grid portalRoot, devicesRoot;
    Button tabPortal, tabDevices;
    ColumnDefinition navCol, contentCol;
    ScrollViewer navScroll, contentScroll;
    StackPanel navPanel;
    TextBlock crumb, clockText, msgText;
    Border msgBar;
    DockPanel head;
    WrapPanel headTabs;
    bool narrow, navOpen;
    public string Blade = "home";
    public string LastError;            // why the last page could not be drawn (checks)
    readonly List<string> history = new();
    readonly List<Action> tickers = new();
    string stateSig = "";
    public EpForm Form;                 // the form on the page, if any (the refresh leaves it alone)
    public EpTenant T => Sim.T;
    /// <summary>True on a narrow screen (a phone, an exam's column): cards instead of tables, the menu in a drawer.</summary>
    public bool Narrow => narrow;
    public bool NavOpen => navOpen;
    public bool DevicesShown => devicesRoot != null && devicesRoot.IsVisible;

    public EpWorkspace(EpSim sim, bool exam, Action<string> status = null)
    {
        Sim = sim; Exam = exam; this.status = status;
        narrow = exam;
        Build();
        SizeChanged += (s, e) =>
        {
            bool n = e.NewSize.Width < NarrowBelow;
            if (n == narrow) return;
            narrow = n;
            Dispatcher.UIThread.Post(Relayout);
        };
    }

    /// <summary>Called every second while the page shows (the clock may have moved): refresh what shows time or state.</summary>
    public void Tick()
    {
        foreach (var t in Terms) PortalWorkspaces.Poll(t);
        UpdateClock();
        foreach (var t in tickers.ToList()) Pk.Safe(t);
        string sig = Signature();
        if (sig != stateSig)
        {
            if (Form == null) Render(true); else stateSig = sig;
            RefreshDevicesTab(false);
        }
    }

    void Build()
    {
        RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));
        // header: title, the pretend tenant, the two tabs
        head = new DockPanel { Background = Pk.B(Ui.Surface2), LastChildFill = true };
        headTabs = new WrapPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 6, 6, 0) };
        tabPortal = Pk.Btn("Admin portal", () => ShowTab(false)); tabDevices = Pk.Btn("Devices", () => ShowTab(true));
        headTabs.Children.Add(tabPortal); headTabs.Children.Add(tabDevices);
        head.Children.Add(headTabs);
        var title = new StackPanel { Margin = new Thickness(12, 8, 8, 6) };
        var tl = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        tl.Children.Add(Pk.Icon("shield", 18, Ui.AccentText));
        tl.Children.Add(Pk.Txt("Endpoint admin center (practice)", 16, true));
        title.Children.Add(tl);
        title.Children.Add(Pk.Txt(T.TenantName + "   |   Signed in as alex@" + T.Domain + " (pretend)", 12.5, false, Ui.Muted));
        head.Children.Add(title);
        Children.Add(new Border { Child = head, BorderBrush = Pk.B(Ui.BorderC), BorderThickness = new Thickness(1, 1, 1, 0) });
        // the message line
        msgText = Pk.Txt("", 14);
        msgText.VerticalAlignment = VerticalAlignment.Center;
        var msgClose = Pk.Small("Close", () => Say(null, true)); msgClose.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(msgClose, Dock.Right);
        var md = new DockPanel(); md.Children.Add(msgClose); md.Children.Add(msgText);
        msgBar = new Border { Child = md, Padding = new Thickness(12, 4, 8, 4), IsVisible = false, BorderBrush = Pk.B(Ui.BorderC), BorderThickness = new Thickness(1, 1, 1, 0) };
        Grid.SetRow(msgBar, 1); Children.Add(msgBar);
        // the portal: menu | content
        portalRoot = new Grid { Background = Pk.B(Ui.Surface) };
        navCol = new ColumnDefinition(new GridLength(230));
        contentCol = new ColumnDefinition(new GridLength(1, GridUnitType.Star));
        portalRoot.ColumnDefinitions.Add(navCol);
        portalRoot.ColumnDefinitions.Add(contentCol);
        navPanel = new StackPanel { Margin = new Thickness(6, 8, 6, 12) };
        navScroll = new ScrollViewer { Content = navPanel, Background = Pk.B(Ui.Surface2) };
        portalRoot.Children.Add(new Border { Child = navScroll, BorderBrush = Pk.B(Ui.BorderC), BorderThickness = new Thickness(0, 0, 1, 0) });
        BuildNav();
        var right = new DockPanel();
        var bar = new DockPanel { Margin = new Thickness(10, 8, 10, 4) };
        var menuBtn = Pk.IconBtn("menu", "Show or hide the menu", ToggleNav); menuBtn.Margin = new Thickness(0, 0, 6, 0);
        var back = Pk.IconBtn("back", "Back", GoBack); back.Margin = new Thickness(0, 0, 8, 0);
        DockPanel.SetDock(menuBtn, Dock.Left); DockPanel.SetDock(back, Dock.Left);
        clockText = Pk.Txt("", 12.5, false, Ui.Muted);
        clockText.VerticalAlignment = VerticalAlignment.Center; clockText.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(clockText, Dock.Right);
        crumb = new TextBlock { Foreground = Pk.B(Pk.LabelC), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        bar.Children.Add(menuBtn); bar.Children.Add(back); bar.Children.Add(clockText); bar.Children.Add(crumb);
        DockPanel.SetDock(bar, Dock.Top);
        right.Children.Add(bar);
        contentScroll = new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, Padding = new Thickness(16, 4, 16, 24) };
        right.Children.Add(contentScroll);
        Grid.SetColumn(right, 1);
        portalRoot.Children.Add(right);
        var pf = new Border { Child = portalRoot, BorderBrush = Pk.B(Ui.BorderC), BorderThickness = new Thickness(1) };
        Grid.SetRow(pf, 2); Children.Add(pf);
        // the Devices tab
        devicesRoot = new Grid { Background = Pk.B(Ui.Surface), IsVisible = false };
        var df = new Border { Child = devicesRoot, BorderBrush = Pk.B(Ui.BorderC), BorderThickness = new Thickness(1) };
        Grid.SetRow(df, 2); Children.Add(df);
        BuildDevicesTab();
        ApplyLayout();
        ShowTab(false);
        Go("home");
    }

    /// <summary>Puts the menu, the header and the device list where the width wants them.</summary>
    void ApplyLayout()
    {
        DockPanel.SetDock(headTabs, narrow ? Dock.Bottom : Dock.Right);
        headTabs.Margin = narrow ? new Thickness(10, 0, 6, 4) : new Thickness(10, 6, 6, 0);
        if (!narrow) { navOpen = false; navCol.Width = new GridLength(230); contentCol.Width = new GridLength(1, GridUnitType.Star); }
        else
        {
            navCol.Width = navOpen ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
            contentCol.Width = navOpen ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        }
        devListCol.Width = new GridLength(narrow ? 0 : 210);
        navClose.IsVisible = narrow;
    }

    void Relayout()
    {
        ApplyLayout();
        if (Form != null) Form.Render(); else Render(true);
        if (DevicesShown) RefreshDevicesTab(true);
    }

    public void ShowTab(bool devices)
    {
        devicesRoot.IsVisible = devices;
        portalRoot.IsVisible = !devices;
        Pk.Picked(tabPortal, !devices); Pk.Picked(tabDevices, devices);
        tabPortal.FontWeight = devices ? FontWeight.Normal : FontWeight.SemiBold;
        tabDevices.FontWeight = devices ? FontWeight.SemiBold : FontWeight.Normal;
        if (devices) RefreshDevicesTab(true);
    }

    /// <summary>The menu button: on a wide screen it folds the menu away; on a phone it opens and closes the drawer.</summary>
    public void ToggleNav()
    {
        if (narrow) { navOpen = !navOpen; ApplyLayout(); return; }
        navCol.Width = new GridLength(navCol.Width.Value > 0 ? 0 : 230);
    }

    /// <summary>A message under the header: what an action did (green) or why it didn't (red). Null hides it.</summary>
    public void Say(string text, bool ok)
    {
        if (string.IsNullOrEmpty(text)) { msgBar.IsVisible = false; return; }
        msgText.Text = text;
        msgBar.Background = Pk.B(ok ? Pk.GoodSoftC : Pk.BadSoftC);
        msgBar.IsVisible = true;
        status?.Invoke(text);
    }
    public string Message => msgBar.IsVisible ? msgText.Text : "";

    /// <summary>Opens a blade (home, devices, device|NAME, list|kind,kind, obj|ID, new|KIND|presets, ...).</summary>
    public void Go(string blade)
    {
        if (history.Count == 0 || history[^1] != blade) history.Add(blade);
        if (history.Count > 60) history.RemoveAt(0);
        Blade = blade;
        if (narrow && navOpen) { navOpen = false; ApplyLayout(); }
        Render(false);
        if (DevicesShown) ShowTab(false);
    }
    /// <summary>Opens a blade in place of the current one (after Create: the new object's page instead of the form).</summary>
    public void ReplaceBlade(string blade)
    {
        if (history.Count > 0) history.RemoveAt(history.Count - 1);
        Go(blade);
    }
    /// <summary>The phone's Back button (the host page's OnBack): closes the drawer, leaves the Devices tab, or goes
    /// back a portal page; false when there is nothing to go back to, so the host leaves the page.</summary>
    public bool OnBack()
    {
        if (narrow && navOpen) { navOpen = false; ApplyLayout(); return true; }
        if (DevicesShown) { ShowTab(false); return true; }
        if (history.Count > 1) { GoBack(); return true; }
        return false;
    }

    public void GoBack()
    {
        if (narrow && navOpen) { navOpen = false; ApplyLayout(); return; }
        if (history.Count > 1) history.RemoveAt(history.Count - 1);
        Blade = history.Count > 0 ? history[^1] : "home";
        Render(false);
    }

    /// <summary>Draws the current blade again (keeps the scroll position when asked).</summary>
    public void Render(bool keepScroll)
    {
        var off = contentScroll.Offset;
        Form = null; tickers.Clear();
        Control page;
        LastError = null;
        try { page = BuildBlade(Blade); }
        catch (Exception e) { LastError = e.ToString(); page = Pk.Error("Cert Lab couldn't draw this page: " + e.Message); }
        contentScroll.Content = page;
        crumb.Text = CrumbFor(Blade);
        stateSig = Signature();
        if (keepScroll) { contentScroll.UpdateLayout(); contentScroll.Offset = off; } else contentScroll.Offset = new Vector(0, 0);
        UpdateClock();
    }
    public Control Page => contentScroll.Content as Control;

    void UpdateClock() { if (clockText != null) clockText.Text = narrow ? "" : "Tenant time " + EpText.Date(T.Now); }

    /// <summary>A short fingerprint of what the portal shows; when it changes, the page is drawn again.</summary>
    string Signature()
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(T.Audit.Count).Append('|').Append(T.All.Count);
        foreach (var d in T.Devices) sb.Append('|').Append(d.Compliance).Append(d.Log.Count).Append(d["state"]).Append(d["join"]).Append(d["managed"]).Append(d.Apps.Count).Append(d.Name);
        foreach (var r in T.Of("autopilot")) sb.Append('|').Append(r["profileStatus"]);
        return sb.ToString();
    }

    // ------------------------------------------------------------ the menu and the blades it opens
    internal static readonly string[][] Menu =
    {
        new[] { "", "Home", "home", "home" },
        new[] { "Devices", "All devices", "devices", "pc" },
        new[] { "Devices", "Enrollment", "enroll", "door" },
        new[] { "Devices", "Configuration", "list|config", "settings" },
        new[] { "Devices", "Compliance", "compliance", "check" },
        new[] { "Devices", "Windows updates", "updates", "update" },
        new[] { "Devices", "Scripts and remediations", "list|script,remediation", "terminal" },
        new[] { "Apps", "All apps", "apps", "app" },
        new[] { "Apps", "App protection policies", "list|appProtection", "lock" },
        new[] { "Apps", "App configuration policies", "list|appConfig", "layers" },
        new[] { "Endpoint security", "Antivirus", "list|av,wsx", "shield" },
        new[] { "Endpoint security", "Disk encryption", "list|diskEncryption", "lock" },
        new[] { "Endpoint security", "Firewall", "list|firewall,firewallRules", "flame" },
        new[] { "Endpoint security", "Endpoint detection and response", "edr", "eye" },
        new[] { "Endpoint security", "Attack surface reduction", "list|asr", "target" },
        new[] { "Endpoint security", "Account protection", "laps", "key" },
        new[] { "Endpoint security", "Security baselines", "list|baseline", "list" },
        new[] { "Users and groups", "Users", "list|user", "user" },
        new[] { "Users and groups", "Groups", "list|group", "group" },
        new[] { "Users and groups", "Device settings", "entra", "settings" },
        new[] { "Conditional Access", "Policies", "list|ca", "lock" },
        new[] { "Conditional Access", "Named locations", "list|location", "map" },
        new[] { "Conditional Access", "What If", "whatif", "bulb" },
        new[] { "", "Reports", "reports", "chart" },
        new[] { "Tenant administration", "Roles", "list|roleAssignment", "crown" },
        new[] { "Tenant administration", "Scope tags", "list|scopeTag", "flag" },
        new[] { "Tenant administration", "Filters", "list|filter", "filter" },
        new[] { "Tenant administration", "Connectors", "connectors", "link" },
        new[] { "Tenant administration", "Tenant settings", "tenant", "domain" },
    };

    Button navClose;
    void BuildNav()
    {
        navClose = Pk.Btn("Close the menu", ToggleNav);
        navClose.HorizontalAlignment = HorizontalAlignment.Stretch;
        navPanel.Children.Add(navClose);
        string last = null;
        foreach (var m in Menu)
        {
            if (m[0] != last && m[0].Length > 0)
                navPanel.Children.Add(new TextBlock { Text = m[0].ToUpperInvariant(), FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Pk.B(Ui.Muted), Margin = new Thickness(8, 14, 0, 2) });
            last = m[0];
            string id = m[2];
            var b = Pk.TreeItem(m[1], 0, false, m[3], () => Go(id));
            navPanel.Children.Add(b);
        }
    }

    string CrumbFor(string blade)
    {
        var parts = blade.Split('|');
        var m = Menu.FirstOrDefault(x => x[2] == blade);
        if (m != null) return (m[0].Length > 0 ? m[0] + "  >  " : "") + m[1];
        switch (parts[0])
        {
            case "device": return "Devices  >  All devices  >  " + parts[1];
            case "obj": case "edit": { var o = ById(parts[1]); var k = o == null ? null : EpSchemas.Get(o.Kind); return (k == null ? "" : k.Blade.Replace(" > ", "  >  ") + "  >  ") + (o == null ? "" : o.Name) + (parts[0] == "edit" ? "  >  Edit" : ""); }
            case "new": case "pick": { var k = EpSchemas.Get(parts[1]); return (k == null ? parts[1] : k.Blade.Replace(" > ", "  >  ")) + "  >  " + (k != null && (k.Kind == "user" || k.Kind == "group") ? "New" : "Create"); }
            case "reports": return "Reports";
        }
        return blade;
    }

    public EpObj ById(string id) => T.All.FirstOrDefault(o => o.Id == id);

    Control BuildBlade(string blade)
    {
        var parts = blade.Split('|');
        string a = parts.Length > 1 ? parts[1] : "", b = parts.Length > 2 ? parts[2] : "";
        switch (parts[0])
        {
            case "home": return HomeBlade();
            case "devices": return DevicesBlade();
            case "device": return DeviceBlade(T.Device(a));
            case "list": return ListBlade(a.Split(','));
            case "obj": { var o = ById(a); return o == null ? Pk.Small("That object no longer exists.") : ObjectBlade(o); }
            case "edit": { var o = ById(a); return o == null ? Pk.Small("That object no longer exists.") : FormBlade(o.Kind, o, null); }
            case "pick": return PickBlade(a);
            case "new": return FormBlade(a, null, b);
            case "enroll": return EnrollBlade();
            case "compliance": return Stack(ListPart("compliance"), TenantPart("Compliance policy settings", null));
            case "updates": return ListBlade(new[] { "ring", "featureUpdate", "qualityUpdate", "driverUpdate" });
            case "apps": return AppsBlade();
            case "edr": return Stack(TenantPart("Connectors", "mdeConnector"), ListPart("edr"));
            case "laps": return Stack(TenantPart("Microsoft Entra device settings", "lapsEnabled"), ListPart("laps"));
            case "entra": return TenantPart("Microsoft Entra device settings", null);
            case "connectors": return TenantPart("Connectors", null);
            case "tenant": return TenantPart(null, null);
            case "whatif": return WhatIfBlade();
            case "reports": return ReportsBlade(a);
        }
        return Pk.Small("Unknown page " + blade);
    }

    internal static StackPanel Stack(params Control[] items) { var s = new StackPanel(); foreach (var i in items) if (i != null) s.Children.Add(i); return s; }
    static StackPanel Title(string title, string help) => Pk.Title(title, help);

    /// <summary>Shows an operation's result, lets the tenant tick, and draws the page again.</summary>
    public void RunOp(EpResult r)
    {
        if (r.Ok) T.Tick();
        Say(r.Text, r.Ok);
        Render(true);
    }

    /// <summary>Runs a portal command line (the same language as the labs' setups and model solutions) and shows what happened.</summary>
    public EpResult RunLine(string line)
    {
        var r = EpOps.Run(T, line);
        Say(r.Text, r.Ok);
        if (Form == null) Render(true);
        return r;
    }
}
