// Cert Lab - the MD-102 practice portal and the AZ-800 practice consoles, drawn off screen with Avalonia's headless
// platform (group "certlab-portal"): App Hub's ep-ui and ws-ui checks (every blade, form, tab, node and tool draws;
// forms refuse what's wrong and create nothing; Create / Save / taps change the simulator through EpOps / WsOps), the
// main flows driven through the controls at 1180 x 800 and at 400 x 840 (a phone), buttons at least 44 px there, and
// App Hub's scenes as pictures (Portal, PortalForm, PortalDevices, Device, DeviceSettings, WhatIf; Network, Aduc, Gpmc,
// GpoEditor, GpModeling, DnsManager, Wac, AzurePortal), wide and phone-sized.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CertLab;
using LearningLab.CertLab.Portal;
using LearningLab.Views;

namespace LearningLab.Tests;

static class PortalScreens
{
    static void Check(string what, bool ok, string detail = null) => Program.Check("certlab-portal: " + what, ok, detail);
    static Window win;

    sealed class HostPage : IPage
    {
        public string Title { get; init; }
        public Control View { get; init; }
    }

    static void Pump() { Dispatcher.UIThread.RunJobs(); win.UpdateLayout(); Dispatcher.UIThread.RunJobs(); win.UpdateLayout(); }
    static void Snap(string name) { Pump(); LearningLab.Tests.Snap.Save(win, name); }
    static void Size(double w, double h) { win.Width = w; win.Height = h; Pump(); }

    public static void Run()
    {
        win = new Window { Width = 1180, Height = 800, Content = new MainView() };
        win.Show();
        try
        {
            Factory();
            Portal();
            Consoles();
        }
        finally { win.Close(); }
    }

    // ------------------------------------------------------------ helpers
    static CourseInfo Course(string id) => CourseLoader.Load(Path.Combine(CertLabContent.CoursesDir, id));

    /// <summary>The simulator a lab page makes for a course's first lab: RhelSim.Create for its simulator, then the lab's setup.</summary>
    static ISim LabSim(CourseInfo c, out TaskSet lab)
    {
        lab = c.Labs.First().Instance(1);
        var sim = RhelSim.Create(c.Simulator, 2, 1);
        TaskRunner.Prepare(sim, lab);
        return sim;
    }

    static void Host(string title, Control w)
    {
        var mv = MainView.Current;
        mv.Push(new HostPage { Title = title, View = w });
        Pump();
    }

    static string AllText(Control root)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var o in new[] { root }.Concat(root.GetLogicalDescendants().OfType<Control>()))
        {
            if (o is TextBlock tb) sb.Append(tb.Text).Append('\n');
            else if (o is TextBox tx) sb.Append(tx.Text).Append('\n');
            else if (o is ContentControl cc && cc.Content is string s) sb.Append(s).Append('\n');
        }
        return sb.ToString();
    }

    static Button FindButton(Control root, string text)
        => root.GetLogicalDescendants().OfType<Button>().FirstOrDefault(b => b.Content as string == text || b.Tag as string == text || b.Content is TextBlock tb && tb.Text == text);

    static bool Tap(Control root, string text)
    {
        var b = FindButton(root, text);
        if (b == null) return false;
        b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Pump();
        return true;
    }

    /// <summary>Buttons on screen less than 44 px high (touch first).</summary>
    static List<string> SmallButtons(Control root)
    {
        var l = new List<string>();
        foreach (var b in root.GetVisualDescendants().OfType<Button>())
        {
            if (!b.IsEffectivelyVisible || b.Bounds.Width <= 0 || b is RepeatButton) continue;   // scroll bar arrows aren't ours
            if (b.Bounds.Height < 43.5) l.Add((b.Content as string ?? b.Tag as string ?? b.GetType().Name) + " " + b.Bounds.Height.ToString("0"));
        }
        return l;
    }

    /// <summary>Nothing wider than the window (no sideways page scroll on a phone) apart from scrolling strips.</summary>
    static bool FitsWidth(Control root, out string wide)
    {
        wide = null;
        double w = win.Bounds.Width;
        var r = root.TransformToVisual(win);
        if (r == null) return true;
        var b = root.Bounds;
        var p = new Rect(b.Size).TransformToAABB(r.Value);
        if (p.Right > w + 1) { wide = "the workspace reaches " + p.Right.ToString("0") + " px of " + w.ToString("0"); return false; }
        return true;
    }

    // ------------------------------------------------------------ the factory
    static void Factory()
    {
        var ep = EpSim.Create(2);
        var ws = WsSim.Create(2);
        var rh = RhelSim.Create("rhel", 1, 1);
        Check("the factory makes the portal for an EpSim", PortalWorkspaces.Make(ep) is EpWorkspace);
        Check("the factory makes the consoles for a WsSim", PortalWorkspaces.Make(ws, true) is WsWorkspace w && w.Exam);
        Check("the factory gives null for other simulators", PortalWorkspaces.Make(rh) == null && PortalWorkspaces.Make(null) == null);
        ep.Dispose(); ws.Dispose(); rh.Dispose();
    }

    // ------------------------------------------------------------ MD-102
    static void Portal()
    {
        var course = Course("md102");
        Check("the MD-102 course has labs on the endpoint simulator", course.Simulator == "endpoint" && course.Labs.Count > 0, course.Simulator);
        var sim = (EpSim)LabSim(course, out var lab);
        string said = null;
        var w = (EpWorkspace)PortalWorkspaces.Make(sim, false, s => said = s);
        var t = sim.T;
        Host("Practice portal", w);
        try
        {
            Check("the workspace opens on the portal's home page", w.Blade == "home" && w.LastError == null && !w.Narrow, w.LastError);
            Snap("portal-home");
            // every blade in the menu, through the menu's own buttons
            foreach (var m in EpWorkspace.Menu)
            {
                bool tapped = Tap(w, m[1]);
                Check("menu button opens: " + m[1], tapped && w.Blade == m[2] && w.LastError == null, w.Blade + " " + w.LastError);
            }
            foreach (var d in t.Devices) { w.Go("device|" + d.Name); Check("device page draws: " + d.Name, w.LastError == null, w.LastError); }
            foreach (var k in EpSchemas.AllKinds.Where(x => x.Kind != "tenant"))
            {
                w.Go(k.Sub.Length > 0 ? "pick|" + k.Kind : "new|" + k.Kind);
                Check("create form draws: " + k.Kind, w.LastError == null, w.LastError);
            }
            foreach (var o in t.All.Where(x => x.Kind != "tenant" && x is not EpDevice).ToList()) { w.Go("obj|" + o.Id); Check("object page draws: " + o.Name, w.LastError == null, w.LastError); }
            foreach (var r in new[] { "compliance", "config", "apps", "feature", "audit" }) { w.Go("reports|" + r); Check("report draws: " + r, w.LastError == null, w.LastError); }
            w.Go("reports|feature");
            Check("the feature update report says how to turn on diagnostic data", AllText(w.Page).Contains("Tenant administration > Connectors"));

            // Pick blade: Create without a platform says so; with one it opens the wizard
            w.Go("list|compliance");
            w.Go("compliance");
            Check("Compliance > Create opens the platform step", Tap(w, "Create") && w.Blade == "pick|compliance", w.Blade);
            Tap(w.Page, "Create");
            Check("Create with no platform asks for one", AllText(w.Page).Contains("Pick "), AllText(w.Page));

            // a compliance policy with no name: the message stands under Name and nothing is created
            w.Go("new|compliance|platform=" + EpSchemas.Win);
            var f = w.Form;
            Check("the compliance form opens as a wizard", f != null && f.Wizard && f.Steps().Contains("Assignments") && f.Steps().Last() == "Review + create", f == null ? "no form" : string.Join(", ", f.Steps()));
            f.Set("bitlocker", "Require");
            Tap(f, "Next");
            Check("Next without a name shows Name is required. under Name", f.Step == 0 && f.FieldErrors.TryGetValue("name", out var err) && err == "Name is required.", string.Join(" | ", f.FieldErrors.Values));
            Check("the message is drawn on the form", AllText(f).Contains("Name is required."));
            Snap("portal-form");
            var r0 = f.Submit();
            Check("Create without a name is refused and creates nothing", !r0.Ok && !t.Of("compliance").Any() && f.FieldErrors.ContainsKey("name"), r0.Text);
            // filled in by typing into the Name box, assigned through the form's Assignments step
            var nameBox = f.GetLogicalDescendants().OfType<TextBox>().First();
            nameBox.Text = "UI BitLocker check";
            Check("typing in Name fills the form", f.Get("name") == "UI BitLocker check");
            f.Step = f.Steps().IndexOf("Assignments"); f.Render(); Pump();
            var ae = f.GetLogicalDescendants().OfType<AssignEditor>().FirstOrDefault();
            Check("the Assignments step has the assignment editor", ae != null);
            if (ae != null)
            {
                Pk.Select(ae.GroupBox, "Windows devices");
                Tap(ae, "Add");
                Check("an assignment waits in the wizard", f.Pending.Count == 1 && f.Pending[0].Group == "Windows devices");
            }
            f.Step = f.Steps().Count - 1; f.Render(); Pump();
            Check("Review lists the assignment", AllText(f).Contains("Windows devices"));
            Tap(f, "Create");
            var pol = t.Find("compliance", "UI BitLocker check");
            Check("Create makes the policy with its settings", pol != null && pol.Is("bitlocker", "Require") && pol.Is("platform", EpSchemas.Win), w.Message);
            Check("the policy is assigned to Windows devices", pol != null && pol.Assignments.Any(a => a.Group == "Windows devices" && a.Intent == "include"));
            Check("after Create the portal shows the policy's page", pol != null && w.Blade == "obj|" + pol.Id && w.LastError == null, w.Blade + " " + w.LastError);
            Check("the message line and the page's status say what happened", w.Message.Length > 0 && said == w.Message, w.Message);
            var dev = t.Device("RB-LT-001");
            Check("before a sync the device doesn't have it", dev.PolicyStatus("UI BitLocker check") == "Not assigned");
            // the pretend Settings app: Accounts > Access work or school > Info > Sync
            w.ShowDevice("RB-LT-001", "settings"); Pump();
            Check("the Devices tab shows the pretend Settings app", w.DevicesShown && AllText(w).Contains("Access work or school"));
            Tap(w, "Info");
            Check("Info shows the Sync button", FindButton(w, "Sync") != null || w.GetLogicalDescendants().OfType<Button>().Any(b => b.Content is TextBlock tb && tb.Text == "Sync"));
            var sync = w.GetLogicalDescendants().OfType<Button>().FirstOrDefault(b => b.Content is TextBlock tb && tb.Text == "Sync");
            sync?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Check("Sync from the pretend Settings app works", w.DeviceMessageOk && w.DeviceMessage.Contains("Sync finished"), w.DeviceMessage);
            Check("after Sync the policy reached RB-LT-001 and it isn't compliant", dev.PolicyStatus("UI BitLocker check") != "Not assigned" && dev.Compliance == "Not compliant" && dev.ComplianceReasons.Any(x => x.Contains("BitLocker")), dev.Compliance + ": " + string.Join("; ", dev.ComplianceReasons));
            // editing goes through EpOps.Update
            w.Go("edit|" + pol.Id);
            w.Form.Set("passwordMin", "99");
            var r2 = w.Form.Submit();
            Check("an edit out of range is refused with the message under its field", !r2.Ok && w.Form.FieldErrors.ContainsKey("passwordMin") && !pol.Is("passwordMin", "99"), r2.Text);

            // a dynamic group, typed into the rule editor, created by Create
            w.Go("list|group");
            Tap(w, "New group");
            var g = w.Form;
            Check("Groups > New group opens the group form", g != null && g.K.Kind == "group", w.Blade);
            g.Set("name", "UI dynamic sales");
            g.Set("membership", "Dynamic user");
            Pump();
            Check("a dynamic group shows the rule editor", g.Rules.TryGetValue("rule", out var rb));
            if (rb != null)
            {
                rb.Box.Text = "(user.departmnet -eq \"Sales\")";
                Check("the rule editor reports a bad rule", rb.Message != null && rb.Status.Text == rb.Message, rb.Status.Text);
                Check("Validate rules refuses to preview a bad rule", rb.Preview().Count == 0);
                rb.Box.Text = "(device.deviceOSType -eq \"Windows\")";
                Check("a device rule in a user group is reported", rb.Message != null && rb.Message.Contains("user."), rb.Message);
                rb.Box.Text = "(user.department -eq \"Sales\")";
                Check("a good rule is valid", rb.Message == null, rb.Message);
                Tap(rb, "Validate rules");
                var who = rb.Preview();
                Check("Validate rules shows who matches", who.Any(x => x.StartsWith("jordan")) && who.Any(x => x.StartsWith("priya")) && !who.Any(x => x.StartsWith("sam")), string.Join(", ", who));
                for (int i = 0; i < 6 && w.Form == g && g.Step < g.Steps().Count - 1; i++) Tap(g, "Next");
                Tap(g, "Create");
                var grp = t.Find("group", "UI dynamic sales");
                Check("Create makes the dynamic group with its rule", grp != null && grp.Is("membership", "Dynamic user") && grp["rule"] == "(user.department -eq \"Sales\")", w.Message);
                Check("its members come from the rule", grp != null && t.Members(grp.Name).Any(m => m["alias"] == "priya"), grp == null ? "" : string.Join(", ", t.Members(grp.Name).Select(m => m.Name)));
            }

            // What If: the policy made through a portal command line, the sign-in through the What If form
            var ca = w.RunLine("create ca name=\"Require a compliant device\" usersInclude=\"All users\" usersExclude=breakglass apps=\"All cloud apps\" grant=\"Grant access\" controls=\"Require device to be marked as compliant\" state=On");
            Check("portal: a Require compliant device policy", ca.Ok, ca.Text);
            Tap(w, "What If");
            Check("Conditional Access > What If opens", w.Blade == "whatif" && w.WhatIf != null);
            Pk.Select(w.WhatIf.UserBox, "priya");
            Pk.Select(w.WhatIf.DeviceBox, "SAM-HOME-PC");
            Tap(w.WhatIf, "What If");
            var res = w.WhatIf.Last;
            Check("What If: priya on SAM-HOME-PC is blocked", res != null && res.Outcome == "Blocked" && w.WhatIf.Outcome.Text.Contains("Blocked"), res?.Outcome);
            Check("What If lists the policy's line", AllText(w.WhatIf.ResultPanel).Contains("Require a compliant device"));
            Snap("portal-whatif");

            // the Devices tab: a console, a remote action, the pretend Settings app
            w.ShowDevice("SAM-HOME-PC", "console"); Pump();
            Check("a Windows device's console opens in the workspace", w.Terms.Count == 1 && w.DevicesShown);
            if (w.Terms[0] is PortalConsole pc)
            {
                pc.Submit("hostname");
                Check("the device's console runs a command", pc.Text.Contains("SAM-HOME-PC"), pc.Text);
            }
            w.Go("device|RB-LT-001");
            Check("Go leaves the Devices tab for the portal", !w.DevicesShown);
            var remoteBefore = dev.Log.Count;
            Tap(w.Page, EpTenant.RemoteLabel("sync"));
            Check("a remote action (Sync) from the device page reaches the device", w.Message.Contains("sent to RB-LT-001") && dev.Log.Count > remoteBefore, w.Message);
            w.Tick();

            // the scenes, wide
            Scenes(w, t, "wide");
            // ---- phone-sized
            Size(400, 840);
            Check("under 760 px the portal is narrow", w.Narrow, w.Bounds.Width.ToString("0"));
            w.Go("devices"); Pump();
            Check("the menu folds into a drawer on a phone", !w.NavOpen);
            var menu = w.GetLogicalDescendants().OfType<Button>().First(b => ToolTip.GetTip(b) as string == "Show or hide the menu");
            menu.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Check("the menu button opens the drawer", w.NavOpen);
            Snap("portal-phone-menu");
            Check("Back closes the drawer first", PortalWorkspaces.OnBack(w) && !w.NavOpen);
            menu.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Check("the drawer has a Close the menu button", Tap(w, "Close the menu") && !w.NavOpen);
            menu.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Tap(w, "Compliance");
            Check("picking from the drawer opens the page and closes the drawer", w.Blade == "compliance" && !w.NavOpen, w.Blade);
            w.Go("devices"); Pump();
            Check("lists are cards on a phone (no table grid)", !w.Page.GetLogicalDescendants().OfType<Grid>().Any(gr => gr.ColumnDefinitions.Count >= 5));
            Scenes(w, t, "phone");
            w.Go("new|compliance|platform=" + EpSchemas.Win); Pump();
            var small = SmallButtons(w);
            Check("every button on the phone-sized form is at least 44 px high", small.Count == 0, string.Join(", ", small.Take(8)));
            Check("the phone-sized portal fits the width", FitsWidth(w, out var wide), wide);
            w.ShowDevice("RB-LT-001", "summary"); Pump();
            small = SmallButtons(w);
            Check("every button on the phone-sized Devices tab is at least 44 px high", small.Count == 0, string.Join(", ", small.Take(8)));
            Size(1180, 800);
        }
        finally { MainView.Current.Back(); Pump(); sim.Dispose(); }
    }

    static void Scenes(EpWorkspace w, EpTenant t, string size)
    {
        void Run(string line) { var r = EpOps.Run(t, line); if (!r.Ok && size == "wide") Check("scene line: " + line, false, r.Text); }
        w.Go("home"); Snap("portal-" + size);
        w.Go("new|compliance|platform=" + EpSchemas.Win);
        w.Form.Set("description", "BitLocker and a firewall on every Windows laptop");
        w.Form.Next(); Snap("portal-form-" + size);
        if (size == "wide")
        {
            Run("create compliance name=\"Windows baseline compliance\" platform=\"Windows 10 and later\" bitlocker=Require firewall=Require");
            Run("assign compliance \"Windows baseline compliance\" \"Windows devices\"");
            Run("remote sync \"RB-LT-001\"");
            Run("remote sync \"RB-DT-010\"");
        }
        w.Go("devices"); Snap("portal-devices-" + size);
        Check("PortalDevices (" + size + ") shows a device that isn't compliant", AllText(w.Page).Contains("Not compliant"));
        w.Go("device|RB-LT-001"); Snap("portal-device-" + size);
        w.OpenSettings("SAM-HOME-PC", "connect"); Snap("portal-device-settings-" + size);
        Check("DeviceSettings (" + size + ") draws the connect card", AllText(w).Contains("Set up a work or school account"));
        w.Go("new|config|platform=" + EpSchemas.Win + ";type=Settings catalog");
        w.Form.Set("name", "Block the camera");
        w.Form.Next(); w.Form.AddSetting("Camera/AllowCamera");
        w.Form.CatalogSearch.Text = "bluetooth";
        Snap("portal-catalog-" + size);
        Check("the settings catalog (" + size + ") lists matches", AllText(w.Form).Contains("Bluetooth"));
        w.Go("whatif");
        Pk.Select(w.WhatIf.UserBox, "priya"); Pk.Select(w.WhatIf.DeviceBox, "SAM-HOME-PC");
        w.WhatIf.Run(); Snap("portal-whatif-" + size);
    }

    // ------------------------------------------------------------ AZ-800
    static void Consoles()
    {
        var course = Course("az800");
        Check("the AZ-800 course has labs on the winserver simulator", course.Simulator == "winserver" && course.Labs.Count > 0, course.Simulator);
        var sim = (WsSim)LabSim(course, out var lab);
        var w = (WsWorkspace)PortalWorkspaces.Make(sim);
        var labN = sim.Lab;
        Host("Practice consoles", w);
        string ov;
        try
        {
            Check("the workspace opens on DC1's console", w.Tab == "servers" && w.LastError == null && w.Terms.Count == 1, w.LastError);
            foreach (var m in sim.MachineNames) { w.Go("servers|" + m); Check("console draws: " + m, w.LastError == null, w.LastError); }
            foreach (var ou in labN.Main.Live.Where(o => o.Is("organizationalUnit")).ToList()) { w.Go("aduc|" + ou.Dn); Check("Users and Computers draws " + ou.Name, w.LastError == null, w.LastError); }
            foreach (var u in labN.Main.OfClass("user").Take(5).ToList()) { w.Go("aduc|" + Dn.Parent(u.Dn) + "|" + u.Dn); Check("a user's page draws: " + u.Sam, w.LastError == null, w.LastError); }
            foreach (var n in new[] { "gpos", "wmi", "modeling", "results", "som:" + labN.DomainDn, "som:OU=Staff," + labN.DomainDn, "gpo:Staff desktop", "gpo:Staff desktop|details", "gpo:Staff desktop|settings", "gpo:Staff desktop|delegation", "gpo:Staff desktop|edit:Wallpaper" })
            { var p = n.Split('|'); w.Go("gpmc|" + p[0] + (p.Length > 1 ? "|" + p[1] : "")); Check("Group Policy draws " + n, w.LastError == null, w.LastError); }
            foreach (var n in new[] { "dns|DC1|", "dns|DC1|zone:corp.example", "dns|DC2|zone:0.10.10.in-addr.arpa" }) { w.Go(n); Check("DNS draws " + n, w.LastError == null, w.LastError); }
            foreach (var s in new[] { "SRV1", "DC1", "FS1" }) foreach (var tl in new[] { "overview", "roles", "services", "files", "storage", "dhcp", "azure" }) { w.Go("wac|" + s + "||" + tl); Check("Admin Center draws " + s + " " + tl, w.LastError == null, w.LastError); }
            if (!labN.Azure.WacConnections.Contains("HV1")) labN.Azure.WacConnections.Add("HV1");
            foreach (var tl in new[] { "vms", "switches" }) { w.Go("wac|HV1||" + tl); Check("Admin Center draws HV1 " + tl, w.LastError == null, w.LastError); }
            foreach (var b in new[] { "arc", "updates", "vms", "vnet", "connect", "users", "filesync" }) { w.Go("azure|" + b); Check("Azure portal draws " + b, w.LastError == null, w.LastError); }
            // the tabs along the top, by tapping them
            foreach (var tb in WsWorkspace.Tabs) { Check("the tab button opens " + tb[1], Tap(w, tb[1]) && w.Tab == tb[0] && w.LastError == null, w.LastError); }

            // the server console
            w.Go("servers|DC1");
            if (w.Terms[0] is PortalConsole pc)
            {
                pc.Submit("Get-ADUser -Identity bcole | Select-Object Name, SamAccountName");
                Check("DC1's console runs a cmdlet", pc.Text.Contains("bcole"), pc.Text);
            }

            // Users and Computers: a bad password is refused and nothing is created; a good one creates the user
            w.Go("aduc|OU=Sales,OU=Staff," + labN.DomainDn);
            Check("New user opens the form", Tap(w, "New user") && w.AducForm != null);
            var f = w.AducForm;
            f.Set("given", "Lena"); f.Set("surname", "Ortiz"); f.Set("name", "Lena Ortiz"); f.Set("sam", "lortiz"); f.Set("password", "short");
            f.SubmitButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Check("a weak password is refused with the domain's message", w.LastMessage.Contains("length") && labN.Main.Find("lortiz", "user") == null, w.LastMessage);
            Check("the form's tap built a 'new user' console line", w.LastLine != null && w.LastLine.StartsWith("new user ") && w.LastLine.Contains("sam=lortiz"), w.LastLine);
            f = w.AducForm; f.Set("password", "Harbor#2031"); f.Press(); Pump();
            Check("the form created the user in Sales", WsCheck.Run(labN, "user[\"lortiz\"].ou == \"OU=Sales,OU=Staff,DC=corp,DC=example\"\nuser[\"lortiz\"].mustchange == Yes", out ov), ov + w.LastMessage);
            // ... and an OU
            w.Go("aduc|OU=Staff," + labN.DomainDn);
            Tap(w, "New OU");
            w.AducForm.Set("name", "Interns"); w.AducForm.Press(); Pump();
            Check("New OU created OU=Interns under Staff", labN.Main.ByDn("OU=Interns,OU=Staff," + labN.DomainDn) != null, w.LastMessage);
            // Group Policy: create and link a GPO here, then set a setting in the editor
            w.Go("gpmc|som:OU=Sales,OU=Staff," + labN.DomainDn);
            Check("Create a GPO and link it here opens its form", Tap(w, "Create a GPO and link it here") && w.GpForm != null);
            w.GpForm.Set("name", "Sales kiosk"); w.GpForm.Press(); Pump();
            Check("the GPO was created and linked", WsCheck.Run(labN, "gpo[\"Sales kiosk\"].links has \"OU=Sales,OU=Staff,DC=corp,DC=example\"", out ov), ov + w.LastMessage);
            w.Go("gpmc|gpo:Sales kiosk|scope");
            Check("Edit... opens the editor", Tap(w, "Edit...") && w.Sub == "edit");
            w.Go("gpmc|gpo:Sales kiosk|edit:NoCmd");
            w.SettingForm.Set("state", "Enabled"); w.SettingForm.Press(); Pump();
            Check("the editor set the setting", WsCheck.Run(labN, "rsop(computer=CL1, user=bcole).setting[\"NoCmd\"] == Enabled", out ov), ov + w.LastMessage);
            // Group Policy Modeling shows it
            w.Go("gpmc|modeling");
            Check("Modeling shows the setting from Sales kiosk", w.LastRsop != null && w.LastRsop.U.Values.Any(v => v.Gpo == "Sales kiosk") && AllText(w).Contains("Sales kiosk"), w.LastRsop == null ? "no result" : string.Join(", ", w.LastRsop.U.Values.Select(v => v.Gpo)));
            // DNS: a record through the form
            w.Go("dns|DC1|zone:corp.example");
            w.DnsForm.Set("name", "app"); w.DnsForm.Set("data", "10.10.0.77"); w.DnsForm.Press(); Pump();
            Check("the DNS form added a record", WsCheck.Run(labN, "resolve(client=CL1, name=app.corp.example) == 10.10.0.77", out ov), ov + w.LastMessage);
            // Windows Admin Center: a share on FS1, and a service stopped
            w.Go("wac|FS1||files");
            w.WacForm.Set("name", "SalesTeam"); w.WacForm.Set("path", "D:\\Shares\\Sales"); w.WacForm.Press(); Pump();
            Check("Windows Admin Center shared a folder on FS1", labN.Server("FS1").Shares.Any(x => x.Name == "SalesTeam"), w.LastMessage);
            w.Go("wac|FS1||services");
            // Azure: Microsoft Entra Connect from the portal
            w.Go("azure|connect");
            w.AzForm.Press(); Pump();
            Check("the portal installed Microsoft Entra Connect", WsCheck.Run(labN, "connect.server == SRV1\ncloud.user[\"bcole\"].upn == bcole@harborline.onmicrosoft.example", out ov), ov + w.LastMessage);
            w.Go("azure|arc");
            Tap(w, "Generate script and onboard");
            Check("the portal onboarded a server to Azure Arc", labN.Azure.Arc.Count > 0 && w.LastLine.StartsWith("arc onboard "), w.LastMessage);
            w.Tick();

            WsScenes(w, sim, "wide");
            // ---- phone-sized
            Size(400, 840);
            Check("under 760 px the consoles are narrow", w.Narrow);
            w.Go("aduc|OU=Sales,OU=Staff," + labN.DomainDn); Pump();
            Check("the tree folds behind a button on a phone", w.TreeToggle != null && !w.TreeOpen);
            w.TreeToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Check("Show the tree opens it", w.TreeOpen);
            Check("Back folds the tree", PortalWorkspaces.OnBack(w) && !w.TreeOpen);
            w.TreeToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Snap("ws-phone-tree");
            var staff = w.GetLogicalDescendants().OfType<Button>().FirstOrDefault(b => b.Tag as string == "Staff");
            staff?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
            Check("tapping a tree item opens it and folds the tree", w.Node == "OU=Staff," + labN.DomainDn && !w.TreeOpen, w.Node);
            Tap(w, "New user");
            var small = SmallButtons(w);
            Check("every button on the phone-sized Users and Computers is at least 44 px high", small.Count == 0, string.Join(", ", small.Take(8)));
            Check("the phone-sized consoles fit the width", FitsWidth(w, out var wide), wide);
            w.AducForm.Set("name", "Phone User"); w.AducForm.Set("sam", "puser"); w.AducForm.Set("password", "Harbor#2031"); w.AducForm.Press(); Pump();
            Check("a user made on the phone-sized form exists", labN.Main.Find("puser", "user") != null, w.LastMessage);
            WsScenes(w, sim, "phone");
            w.Go("wac|FS1||files"); Pump();
            small = SmallButtons(w);
            Check("every button on the phone-sized Admin Center is at least 44 px high", small.Count == 0, string.Join(", ", small.Take(8)));
            Size(1180, 800);
        }
        finally { MainView.Current.Back(); Pump(); sim.Dispose(); }
    }

    static void WsScenes(WsWorkspace w, WsSim sim, string size)
    {
        void Run(string line) { var r = WsOps.Run(sim.Lab, line); if (!r.Ok && size == "wide") Check("scene line: " + line, false, r.Text); }
        var tv = w.ConsoleView("DC1");
        if (tv is PortalConsole pc && size == "wide")
            foreach (var l in new[] { "Get-ADDomainController -Filter * | Format-Table Name, Site, IsGlobalCatalog -AutoSize", "Get-ADUser -Filter \"Department -eq 'Sales'\" | Select-Object Name, SamAccountName" }) pc.Submit(l);
        Snap("ws-network-" + size);
        w.Go("aduc|OU=Sales,OU=Staff,DC=corp,DC=example|CN=Bianca Cole,OU=Sales,OU=Staff,DC=corp,DC=example"); Snap("ws-aduc-" + size);
        Check("Aduc (" + size + ") shows Bianca Cole's page", AllText(w).Contains("Reset password"));
        if (size == "wide")
        {
            Run("gpo new \"Sales lockdown\"");
            Run("gpo set \"Sales lockdown\" setting=NoControlPanel state=Enabled");
            Run("gpo link \"Sales lockdown\" Staff/Sales enforced=yes");
            Run("gpo new \"Kiosk users\""); Run("gpo set \"Kiosk users\" setting=Loopback state=Enabled value=Replace"); Run("gpo set \"Kiosk users\" setting=NoCmd state=Enabled"); Run("gpo link \"Kiosk users\" Workstations");
            Run("dns record add server=DC1 zone=corp.example name=web type=A data=10.10.0.80 ptr=yes");
        }
        w.Go("gpmc|som:OU=Sales,OU=Staff,DC=corp,DC=example"); Snap("ws-gpmc-" + size);
        Check("Gpmc (" + size + ") lists the linked GPO", AllText(w).Contains("Sales lockdown"));
        w.Go("gpmc|gpo:Workstation baseline|edit:Loopback"); Snap("ws-gpo-editor-" + size);
        Check("GpoEditor (" + size + ") opens a setting", w.SettingForm != null && w.LastError == null, w.LastError);
        w.Go("gpmc|modeling"); Snap("ws-gp-modeling-" + size);
        w.Go("dns|DC1|zone:corp.example"); Snap("ws-dns-" + size);
        Check("DnsManager (" + size + ") lists the web record", AllText(w).Contains("10.10.0.80"));
        w.Go("wac|FS1||files"); Snap("ws-wac-" + size);
        w.Go("azure|users"); Snap("ws-azure-" + size);
        Check("AzurePortal (" + size + ") lists synced users", AllText(w).Contains("bcole"));
    }
}
