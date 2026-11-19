// Cert Lab - the practice area's Devices tab (App Hub's UiEpDevTab / UiEpSettings / UiEpSettings2): every simulated
// device (phones too) with a summary of its state, and for Windows devices the PowerShell console, a pretend Settings
// app (Accounts > Access work or school, in a plain light look of Cert Lab's own), Company Portal, "Sign in to an app"
// and, at the out-of-box experience, Start setup. On a wide screen the devices are listed on the left; on a phone a
// picker at the top. Each action is an EpSim directive, as the labs' model solutions use.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LearningLab.NetworkLab;
using LearningLab.Views;
using CertLab;

namespace LearningLab.CertLab.Portal;

public partial class EpWorkspace
{
    public string DevSel = "RB-LT-001", DevPanel = "summary";
    StackPanel devList, devHead;
    ContentControl devBody;
    ColumnDefinition devListCol;
    readonly Dictionary<string, Control> consoles = new(StringComparer.OrdinalIgnoreCase);
    public string DeviceMessage = "";
    public bool DeviceMessageOk = true;
    string settingsMode = "";          // "", connect, info

    void BuildDevicesTab()
    {
        devListCol = new ColumnDefinition(new GridLength(210));
        devicesRoot.ColumnDefinitions.Add(devListCol);
        devicesRoot.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        devList = new StackPanel { Margin = new Thickness(6, 8, 6, 8) };
        devicesRoot.Children.Add(new Border { Child = new ScrollViewer { Content = devList }, Background = Pk.B(Ui.Surface2), BorderBrush = Pk.B(Ui.BorderC), BorderThickness = new Thickness(0, 0, 1, 0) });
        var right = new DockPanel { Margin = new Thickness(14, 8, 14, 10) };
        devHead = new StackPanel();
        DockPanel.SetDock(devHead, Dock.Top);
        right.Children.Add(devHead);
        devBody = new ContentControl();
        right.Children.Add(devBody);
        Grid.SetColumn(right, 1);
        devicesRoot.Children.Add(right);
    }

    /// <summary>Picks a device and a panel (summary, console, settings, portal, signin, oobe) on the Devices tab.</summary>
    public void ShowDevice(string name, string panel)
    {
        DevSel = name; DevPanel = panel ?? "summary";
        if (!DevicesShown) ShowTab(true); else RefreshDevicesTab(true);
    }

    void RefreshDevicesTab(bool full)
    {
        if (!DevicesShown) return;
        var d = T.Device(DevSel) ?? T.Devices.FirstOrDefault();
        if (d == null) return;
        DevSel = d.Name;
        devList.Children.Clear();
        devList.Children.Add(new TextBlock { Text = "DEVICES", FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = Pk.B(Ui.Muted), Margin = new Thickness(6, 4, 0, 4) });
        foreach (var x in T.Devices.OrderBy(x => x.Windows ? 0 : 1).ThenBy(x => x.Name))
        {
            var x0 = x;
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = x.Name, Foreground = Pk.B(Ui.TextC), FontSize = 14, FontWeight = x == d ? FontWeight.SemiBold : FontWeight.Normal });
            sp.Children.Add(new TextBlock { Text = DeviceLine(x), Foreground = x.Enrolled ? Pk.StatusBrush(x.Compliance) : Pk.B(Ui.Muted), FontSize = 12 });
            var b = new Button { Content = sp, MinHeight = 48, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 4, 6, 4), Margin = new Thickness(0, 0, 0, 2), Background = x == d ? Pk.B(Ui.AccentSoft) : Brushes.Transparent, BorderThickness = new Thickness(0) };
            b.Click += (s, e) => Pk.Safe(() => ShowDevice(x0.Name, x0.Windows || DevPanel != "console" && DevPanel != "settings" ? DevPanel : "summary"));
            devList.Children.Add(b);
        }
        devHead.Children.Clear();
        if (Narrow)
        {
            var pick = new ComboBox { MinHeight = 44, MinWidth = 240, Margin = new Thickness(0, 0, 0, 6), FontSize = 14 };
            foreach (var x in T.Devices) pick.Items.Add(new ComboBoxItem { Content = x.Name + "  -  " + DeviceLine(x), Tag = x.Name, MinHeight = 40 });
            Pk.Select(pick, d.Name);
            pick.SelectionChanged += (s, e) => Pk.Safe(() => { if (Pk.Val(pick) != DevSel) ShowDevice(Pk.Val(pick), DevPanel); });
            devHead.Children.Add(pick);
        }
        devHead.Children.Add(Pk.Txt(d.Name, 19, true));
        if (d["note"].Length > 0) devHead.Children.Add(Pk.Small(d["note"]));
        var tabs = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
        var panels = new List<string[]> { new[] { "summary", "Summary" } };
        if (d.Is("state", "oobe")) panels.Add(new[] { "oobe", "Start setup" });
        if (d.Windows) { panels.Add(new[] { "console", "PowerShell" }); panels.Add(new[] { "settings", "Settings" }); }
        panels.Add(new[] { "portal", "Company Portal" });
        panels.Add(new[] { "signin", "Sign in to an app" });
        if (!panels.Any(p => p[0] == DevPanel)) DevPanel = "summary";
        foreach (var p in panels)
        {
            var p0 = p[0];
            var b = Pk.Small(p[1], () => ShowDevice(DevSel, p0));
            Pk.Picked(b, p0 == DevPanel);
            tabs.Children.Add(b);
        }
        devHead.Children.Add(Pk.Strip(tabs));
        if (full || DevPanel != "console") devBody.Content = DevicePanel(d);
    }

    static string DeviceLine(EpDevice x) => x.Is("state", "oobe") ? "out-of-box setup" : x.Enrolled ? x.Compliance : x.JoinType.Length > 0 && x.JoinType != "None" ? x.JoinType : "not connected";

    Control Summary(EpDevice d)
    {
        var s = new StackPanel();
        void P(string l, string v) => s.Children.Add(Pk.Pair(l, v, Narrow));
        P("Operating system", d["os"] + " " + d["osVersion"] + (d["edition"].Length > 0 ? " " + d["edition"] : "") + (d["featureVersion"].Length > 0 ? ", version " + d["featureVersion"] : ""));
        P("State", d.Is("state", "oobe") ? "At the out-of-box experience (setup)" : d.Is("state", "off") ? "Switched off" : "Running");
        P("Microsoft Entra ID", d.JoinType.Length > 0 ? d.JoinType : "None");
        P("Intune", d.Enrolled ? "Enrolled" + (d.LastSync == DateTime.MinValue ? "" : ", last check-in " + EpText.Date(d.LastSync)) : "Not enrolled");
        P("Compliance", d.Enrolled ? d.Compliance + (d.ComplianceReasons.Count > 0 ? ": " + string.Join("; ", d.ComplianceReasons) : "") : "-");
        P("Primary user", d.PrimaryUser);
        if (d.Windows)
        {
            P("BitLocker", d["bitlocker"] + (d["recoveryKeyIn"].Length > 0 ? ", key in " + d["recoveryKeyIn"] : ""));
            P("Firewall", d["firewall"]);
            P("Antivirus", d["av"] + ", real-time protection " + d["rtp"] + (d["sigAgeDays"].Length > 0 ? ", signatures " + d["sigAgeDays"] + " days old" : ""));
            P("Monthly update", d["patch"] + (d["ring"].Length > 0 ? " (ring " + d["ring"] + ")" : ""));
            P("Local admin password (LAPS)", d["laps"].Length > 0 ? d["laps"] + (d["laps.account"].Length > 0 ? " for " + d["laps.account"] : "") : "Not managed");
            if (d["kiosk"].Length > 0) P("Kiosk", d["kiosk"]);
            P("TPM / Secure Boot", d["tpm"] + " / " + d["secureBoot"]);
        }
        else P("Passcode", d["passcode"]);
        P("Apps installed", string.Join(", ", d.Apps.Values.Where(a => a.Status == "Installed").Select(a => a.App)));
        return s;
    }

    static ScrollViewer Scroll(Control c) => new() { Content = c, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };

    Control DevicePanel(EpDevice d)
    {
        switch (DevPanel)
        {
            case "console": return ConsoleFor(d);
            case "settings": return Scroll(SettingsApp(d));
            case "portal": return Scroll(CompanyPortal(d));
            case "signin":
                {
                    var w = new WhatIfView(T, Narrow, d.Name);
                    if (d.PrimaryUser.Length > 0) Pk.Select(w.UserBox, d.PrimaryUser);
                    return Scroll(Stack(Pk.Small("Someone signs in to a cloud app on " + d.Name + ". Conditional Access decides whether they get in."), w));
                }
            case "oobe": return Scroll(Oobe(d));
        }
        return Scroll(Summary(d));
    }

    /// <summary>Runs something a person does on the device and shows what happened.</summary>
    public string Act(EpDevice d, string directive, string done)
    {
        string why = Sim.Directive(d, directive);
        T.Tick();
        DeviceMessage = why ?? done; DeviceMessageOk = why == null;
        Say(DeviceMessage, DeviceMessageOk);
        settingsMode = "";
        RefreshDevicesTab(true);
        return why;
    }
    Control MessageLine(IBrush ok, IBrush bad)
    {
        if (DeviceMessage.Length == 0) return new TextBlock();
        return new TextBlock { Text = DeviceMessage, Foreground = DeviceMessageOk ? ok : bad, TextWrapping = TextWrapping.Wrap, FontSize = 14, Margin = new Thickness(0, 8, 0, 8) };
    }

    Control ConsoleFor(EpDevice d)
    {
        if (!consoles.TryGetValue(d.Name, out var tv))
        {
            var c = Sim.Console(d.Name);
            if (c == null) return Pk.Small("This device has no console.");
            tv = PortalWorkspaces.Terminal(c);
            consoles[d.Name] = tv;
            Terms.Add(tv);
        }
        if (tv.Parent is Border host) host.Child = null;
        return new Border { Child = tv, BorderBrush = Pk.B(Ui.BorderC), BorderThickness = new Thickness(1), MinHeight = 300 };
    }

    /// <summary>The console control of a device (opens the PowerShell panel).</summary>
    public Control ConsoleView(string device) { ShowDevice(device, "console"); return consoles.TryGetValue(device, out var c) ? c : null; }

    // ------------------------------------------------------------ the pretend Settings app (light, plain)
    const string LAccent = "#2F5D7C", LGood = "#1E7A3C", LBad = "#B42318", LLine = "#DADDE2";

    static Border LRow(Control c) => new() { Child = c, Background = Brushes.White, BorderBrush = Pk.B(LLine), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(14, 10), Margin = new Thickness(0, 0, 0, 8) };

    bool HasWorkAccount(EpDevice d) => d.PrimaryUser.Length > 0 && (d.EntraJoined || d.Is("join", "Entra registered") || d.Enrolled);

    Control SettingsApp(EpDevice d)
    {
        var win = new DockPanel { Background = Pk.B("#F6F6F4"), MinHeight = 380 };
        var bar = new Border { Background = Pk.B("#ECEDEA"), Child = Lt.Small("Settings  -  " + d.Name + "  (pretend, on the device)") };
        bar.Padding = new Thickness(12, 8, 12, 2);
        DockPanel.SetDock(bar, Dock.Top); win.Children.Add(bar);
        if (!Narrow)
        {
            var side = new StackPanel { Width = 180, Margin = new Thickness(10, 12, 6, 10) };
            foreach (var x in new[] { "System", "Bluetooth & devices", "Network & internet", "Apps", "Accounts", "Windows Update" })
                side.Children.Add(new Border { Background = x == "Accounts" ? Pk.B("#E3E6EA") : Brushes.Transparent, CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 8, 6, 8), Child = new TextBlock { Text = x, Foreground = Pk.B(x == "Accounts" ? Lt.FgC : "#5D6470"), FontSize = 14, FontWeight = x == "Accounts" ? FontWeight.SemiBold : FontWeight.Normal } });
            DockPanel.SetDock(side, Dock.Left); win.Children.Add(side);
        }
        var main = new StackPanel { Margin = new Thickness(14, 12, 16, 16) };
        main.Children.Add(Lt.T("Accounts  >  Access work or school", 19, true));
        main.Children.Add(Lt.Small("Get access to resources like email, apps and the network. Connecting means your work or school might control some things on this device."));
        main.Children.Add(MessageLine(Pk.B(LGood), Pk.B(LBad)));
        SettingsAccounts(main, d);
        win.Children.Add(main);
        return new Border { Child = Lt.Scope(win), BorderBrush = Pk.B(LLine), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), ClipToBounds = true };
    }

    /// <summary>Opens the pretend Settings app on a device ("" / connect / info).</summary>
    public void OpenSettings(string device, string mode) { settingsMode = mode ?? ""; DeviceMessage = ""; ShowDevice(device, "settings"); }

    string Who(TextBox box) { var t = Pk.T(box).Trim(); int at = t.IndexOf('@'); return at > 0 ? t.Substring(0, at) : t; }
    void NeedAccount() { DeviceMessage = "Type your work account first."; DeviceMessageOk = false; RefreshDevicesTab(true); }

    void SettingsAccounts(StackPanel main, EpDevice d)
    {
        if (HasWorkAccount(d))
        {
            string upn = d.PrimaryUser + "@" + T.Domain;
            var acc = new StackPanel();
            acc.Children.Add(Lt.T(d.EntraJoined ? "Connected to " + T.TenantName + "'s Microsoft Entra ID" : d.Enrolled && !d.Is("join", "Entra registered") ? "Connected to " + T.TenantName + "'s device management" : "Work or school account", 15, true));
            acc.Children.Add(Lt.Small("Connected by " + upn + (d.Enrolled ? "  |  Managed by " + T.TenantName : "")));
            var btns = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            btns.Children.Add(Lt.Btn("Info", () => { settingsMode = settingsMode == "info" ? "" : "info"; RefreshDevicesTab(true); }));
            btns.Children.Add(Lt.Btn("Disconnect", () => Act(d, "#settings disconnect", "Disconnected. The work account was removed from this device.")));
            acc.Children.Add(btns);
            if (settingsMode == "info")
            {
                var info = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
                if (d.Enrolled)
                {
                    info.Children.Add(Lt.T("Managed by " + T.TenantName, 14, true));
                    info.Children.Add(Lt.Small("Connection info: MDM server manage.pretend." + T.Domain + "  |  Last sync: " + (d.LastSync == DateTime.MinValue ? "never" : EpText.Date(d.LastSync))));
                    info.Children.Add(Lt.Small("Policies on this device: " + d.Policies.Count + ".  Syncing asks Intune for anything new now instead of waiting for the next check-in."));
                    info.Children.Add(Lt.Primary("Sync", () => Act(d, "#settings sync", "Sync finished. The device checked in with Intune."), LAccent));
                }
                else info.Children.Add(Lt.Small("This device isn't managed by your organisation (no device management), so there is nothing to sync."));
                acc.Children.Add(info);
            }
            main.Children.Add(LRow(acc));
        }
        var add = new StackPanel();
        var head = new DockPanel();
        var connect = Lt.Primary("Connect", () => { settingsMode = settingsMode == "connect" ? "" : "connect"; RefreshDevicesTab(true); }, LAccent);
        DockPanel.SetDock(connect, Dock.Right); head.Children.Add(connect);
        var ht = Lt.T("Add a work or school account", 15, false); ht.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(ht);
        add.Children.Add(head);
        if (settingsMode == "connect")
        {
            var card = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            card.Children.Add(Lt.T("Set up a work or school account", 16, true));
            card.Children.Add(Lt.Small("Type your work account. You'll get access to resources like email, apps and the network."));
            var box = Lt.Box(d.PrimaryUser.Length > 0 ? d.PrimaryUser + "@" + T.Domain : "", 280);
            box.Margin = new Thickness(0, 4, 0, 8);
            card.Children.Add(box);
            card.Children.Add(Lt.Primary("Next", () => { if (Who(box).Length == 0) { NeedAccount(); return; } Act(d, "#settings register " + Who(box), "The work account was added (the device is now Microsoft Entra registered)."); }, LAccent));
            card.Children.Add(Lt.Small("Alternate actions:"));
            card.Children.Add(Lt.Btn("Join this device to Microsoft Entra ID", () => { if (Who(box).Length == 0) { NeedAccount(); return; } Act(d, "#settings join " + Who(box), "This device is now joined to " + T.TenantName + "'s Microsoft Entra ID. Sign in with your work account next time."); }));
            add.Children.Add(card);
        }
        main.Children.Add(LRow(add));
        var enr = new StackPanel();
        enr.Children.Add(Lt.T("Enroll only in device management", 15, false));
        enr.Children.Add(Lt.Small("Lets the organisation manage the device without joining it or adding the account for apps."));
        var ebox = Lt.Box(d.PrimaryUser.Length > 0 ? d.PrimaryUser + "@" + T.Domain : "", 280);
        ebox.Margin = new Thickness(0, 4, 0, 8);
        enr.Children.Add(ebox);
        enr.Children.Add(Lt.Btn("Enroll", () => Act(d, "#settings enroll " + Who(ebox), "The device is enrolled in device management.")));
        main.Children.Add(LRow(enr));
    }

    Control CompanyPortal(EpDevice d)
    {
        var s = Stack(Title("Company Portal on " + d.Name, d.Enrolled ? "Apps " + T.TenantName + " makes available to " + (d.PrimaryUser.Length > 0 ? d.PrimaryUser : "this device") + "." : "This device isn't enrolled, so Company Portal has nothing to show. Enrol it first."));
        s.Children.Add(MessageLine(Pk.Good, Pk.Bad));
        var avail = d.Apps.Values.Where(a => a.Status == "Available" || a.Status == "Failed" && a.Intent == "available").ToList();
        s.Children.Add(Pk.H("Available apps"));
        if (avail.Count == 0) s.Children.Add(Pk.Small("No apps are available to install. Assign an app as Available for enrolled devices to a group the user is in, then sync."));
        foreach (var a in avail)
        {
            var a0 = a;
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 4) };
            var b = Pk.Accent("Install", () => Act(d, "#portal install \"" + a0.App + "\"", a0.App + ": installation requested."));
            DockPanel.SetDock(b, Dock.Right); row.Children.Add(b);
            var t = Pk.Txt(a.App + (a.Status == "Failed" ? "  (failed: " + a.Detail + ")" : ""), 14.5); t.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(t);
            s.Children.Add(Pk.Capped(row, 600));
        }
        s.Children.Add(Pk.H("Installed"));
        var inst = d.Apps.Values.Where(a => a.Status == "Installed").Select(a => a.App).ToList();
        s.Children.Add(Pk.Txt(inst.Count == 0 ? "Nothing yet." : string.Join(", ", inst), 14));
        return s;
    }

    Control Oobe(EpDevice d)
    {
        var s = Stack(Title("Setting up " + d.Name, d.Is("state", "oobe") ? "The device is at the out-of-box experience (the first screens of Windows setup). If an Autopilot profile is assigned to it, setup follows that profile." : "This device has finished setup."));
        s.Children.Add(MessageLine(Pk.Good, Pk.Bad));
        if (!d.Is("state", "oobe")) return s;
        s.Children.Add(Pk.Txt("Work account (empty for self-deploying mode)", 14));
        var box = Pk.Box("", 280); box.HorizontalAlignment = HorizontalAlignment.Left; box.Width = 280;
        s.Children.Add(box);
        var go = Pk.Accent("Start setup", () =>
        {
            var w = Who(box);
            Act(d, "#oobe" + (w.Length > 0 ? " " + w : ""), "Setup finished. See the summary for what happened.");
        });
        go.HorizontalAlignment = HorizontalAlignment.Left;
        s.Children.Add(go);
        return s;
    }
}
