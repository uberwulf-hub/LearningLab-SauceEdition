// Cert Lab - the practice portal's pages (App Hub's UiEpLists / UiEpObject): the home page, the lists of each kind
// (with Create), "pick a platform and profile type", the apps by type, the tenant settings parts some pages show, and
// an object's page: its properties, Edit and Delete, its assignments, which devices report it, and what only some kinds
// have (a group's members, a filter's matching devices, a ring's Pause / Resume, an Autopilot device's user).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using LearningLab.Views;
using CertLab;

namespace LearningLab.CertLab.Portal;

public partial class EpWorkspace
{
    Control HomeBlade()
    {
        var s = Stack(Title("Home", "A summary of the pretend tenant " + T.TenantName + ". Everything here is a simulation in Cert Lab: nothing reaches a real service."));
        var devs = T.Devices.ToList();
        var tiles = new WrapPanel();
        tiles.Children.Add(Pk.Tile("Devices", devs.Count.ToString(), Ui.TextC));
        tiles.Children.Add(Pk.Tile("Enrolled in Intune", devs.Count(d => d.Enrolled).ToString(), Ui.AccentText));
        tiles.Children.Add(Pk.Tile("Compliant", devs.Count(d => d.Compliance == "Compliant").ToString(), Pk.GoodC));
        tiles.Children.Add(Pk.Tile("Not compliant", devs.Count(d => d.Compliance == "Not compliant").ToString(), Pk.BadC));
        tiles.Children.Add(Pk.Tile("In grace period", devs.Count(d => d.Compliance == "In grace period").ToString(), Pk.WarnC));
        tiles.Children.Add(Pk.Tile("Users", T.Users.Count().ToString(), Ui.TextC));
        s.Children.Add(tiles);
        var bad = devs.Where(d => d.Compliance == "Not compliant").ToList();
        if (bad.Count > 0)
        {
            s.Children.Add(Pk.H("Devices that aren't compliant"));
            foreach (var d in bad) { var d0 = d; s.Children.Add(Pk.Link(d.Name + "  -  " + (d.ComplianceReasons.Count > 0 ? d.ComplianceReasons[0] : "see the device"), () => Go("device|" + d0.Name))); }
        }
        s.Children.Add(Pk.H("Recent activity (audit log)"));
        var audit = T.Audit.AsEnumerable().Reverse().Take(12).ToList();
        if (audit.Count == 0) s.Children.Add(Pk.Small("No changes yet. What you create, assign or send to devices shows here."));
        foreach (var l in audit) { var t = Pk.Txt(l, 13, false, Pk.LabelC); t.Margin = new Thickness(0, 0, 0, 4); s.Children.Add(t); }
        if (T.Audit.Count > 12) s.Children.Add(Pk.Link("Open the whole audit log", () => Go("reports|audit")));
        return s;
    }

    Control ListBlade(string[] kinds)
    {
        var k0 = EpSchemas.Get(kinds[0]);
        var m = Menu.FirstOrDefault(x => x[2] == "list|" + string.Join(",", kinds));
        var s = Stack(Title(m != null ? m[1] : k0 == null ? kinds[0] : k0.Plural, null));
        foreach (var k in kinds) { var p = ListPart(k); if (p != null) s.Children.Add(p); }
        if (kinds.Contains("ring")) s.Children.Add(Pk.Small("Pause or resume a ring on its page (open it from the list)."));
        return s;
    }

    /// <summary>One kind's list: a heading, Create, and a row per object.</summary>
    Control ListPart(string kind)
    {
        var k = EpSchemas.Get(kind);
        if (k == null) return null;
        var s = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        var head = new DockPanel { Margin = new Thickness(0, 10, 0, 4) };
        if (kind != "autopilot")
        {
            var create = Pk.Accent(kind == "user" || kind == "group" ? "New " + k.Title.ToLowerInvariant() : "Create", () => Go(k.Sub.Length > 0 ? "pick|" + kind : "new|" + kind));
            create.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(create, Dock.Right); head.Children.Add(create);
        }
        var ht = Pk.Txt(k.Plural, 16, true, Ui.AccentText); ht.VerticalAlignment = VerticalAlignment.Center;
        head.Children.Add(ht);
        s.Children.Add(head);
        var items = T.Of(kind).OrderBy(o => o.Name).ToList();
        int cols = Narrow ? 3 : 4;
        var heads = new List<string> { "Name" };
        heads.AddRange(k.Columns.Take(cols).Select(c => ColumnTitle(kind, c)));
        if (k.Assignable) heads.Add("Assigned to");
        var rows = items.Select(o =>
        {
            var r = new List<string> { o.Name };
            r.AddRange(k.Columns.Take(cols).Select(c => Short(EpSchemas.Value(o, c).Replace("\n", ", "))));
            if (k.Assignable) r.Add(o.Assignments.Count == 0 ? "Not assigned" : string.Join(", ", o.Assignments.Select(a => (a.Exclude ? "not " : "") + a.Group)));
            return r.ToArray();
        });
        s.Children.Add(Pk.Table(heads.ToArray(), rows, i => Go("obj|" + items[i].Id), Narrow, "None yet."));
        return s;
    }
    static string ColumnTitle(string kind, string key) { var l = EpSchemas.Label(kind, key); int p = l.IndexOf(" (", StringComparison.Ordinal); if (p > 0) l = l.Substring(0, p); return l.Length > 28 ? l.Substring(0, 26) + "..." : l; }
    static string Short(string s) => s.Length > 60 ? s.Substring(0, 57) + "..." : s;

    /// <summary>Create, step 0: the platform (and for configuration profiles the profile type).</summary>
    Control PickBlade(string kind)
    {
        var k = EpSchemas.Get(kind);
        var s = Stack(Title("Create " + k.Title.ToLowerInvariant(), "First pick what it is for. The form that follows shows the settings for that choice."));
        var sub = k.Field(k.Sub);
        var pick = Pk.Combo(new[] { "" }.Concat(sub.Options.Where(x => x.Length > 0)), "", 260);
        s.Children.Add(Pk.Txt(sub.Label + " *", 14)); s.Children.Add(pick);
        ComboBox type = null;
        if (kind == "config")
        {
            var tf = k.Field("type");
            type = Pk.Combo(new[] { "" }.Concat(tf.Options), "", 260);
            s.Children.Add(Pk.Txt(tf.Label + " *", 14)); s.Children.Add(type);
            s.Children.Add(Pk.Small("Settings catalog: pick any of the settings Windows offers. Templates (Custom, Kiosk, Domain Join, Wi-Fi, Edition upgrade, Device restrictions) have fixed settings."));
        }
        var err = Pk.Error(""); err.IsVisible = false;
        s.Children.Add(err);
        var next = Pk.Accent("Create", () =>
        {
            string p = Pk.Val(pick), t = type == null ? "" : Pk.Val(type);
            if (p.Length == 0 || type != null && t.Length == 0) { err.Text = "Pick " + (p.Length == 0 ? sub.Label.ToLowerInvariant() : "a profile type") + " first."; err.IsVisible = true; return; }
            Go("new|" + kind + "|" + k.Sub + "=" + p + (t.Length > 0 ? ";type=" + t : ""));
        });
        next.Margin = new Thickness(0, 10, 6, 0);
        next.HorizontalAlignment = HorizontalAlignment.Left;
        s.Children.Add(next);
        return s;
    }

    Control AppsBlade()
    {
        var s = Stack(Title("All apps", "Apps by type. Create picks the type first: Windows app (Win32) from an .intunewin package, Microsoft Store app, Microsoft 365 Apps, phone apps, web links."));
        s.Children.Add(ListPart("app"));
        var apps = T.Of("app").ToList();
        if (apps.Count > 0)
        {
            s.Children.Add(Pk.H("By type"));
            foreach (var g in apps.GroupBy(a => a["type"]).OrderBy(g => g.Key))
                s.Children.Add(Pk.Pair(g.Key, string.Join(", ", g.Select(a => a.Name)), Narrow));
        }
        return s;
    }

    Control FormBlade(string kind, EpObj existing, string presets)
    {
        var f = new EpForm(this, kind, existing, presets, null, null);
        Form = f;
        return f;
    }

    /// <summary>Tenant settings: one section (or all), or one setting, with Save.</summary>
    Control TenantPart(string section, string key)
    {
        var f = new EpForm(this, "tenant", T.Settings, null, section, key);
        return Pk.Card(f);
    }

    // ------------------------------------------------------------ an object's page
    Control ObjectBlade(EpObj o)
    {
        var k = EpSchemas.Get(o.Kind);
        var s = Stack(Title(o.Name, k.Title + (o.Kind == "app" ? "  -  " + o["type"] : "") + "  |  created " + EpText.Date(o.Created) + ", changed " + EpText.Date(o.Modified)));
        var acts = new WrapPanel();
        Border confirm = null;
        acts.Children.Add(Pk.Accent("Edit", () => Go("edit|" + o.Id)));
        acts.Children.Add(Pk.Btn("Delete", () => confirm.IsVisible = true));
        s.Children.Add(acts);
        var yes = Pk.Btn("Yes, delete it", () => { var r = EpOps.Delete(T, o); if (r.Ok) T.Tick(); Say(r.Text, r.Ok); if (r.Ok) GoBack(); });
        confirm = new Border { Child = Stack(Pk.Txt("Delete " + o.Name + "? Devices lose what it set at their next check-in.", 14), Pk.Row(yes, Pk.Btn("No", () => confirm.IsVisible = false))), Background = Pk.B(Pk.BadSoftC), Padding = new Thickness(12), CornerRadius = new CornerRadius(8), IsVisible = false, Margin = new Thickness(0, 0, 0, 8) };
        s.Children.Add(confirm);
        Extras(s, o);
        if (k.Assignable) { s.Children.Add(Pk.H("Assignments")); s.Children.Add(new AssignEditor(this, o, k, null)); }
        Status(s, o);
        foreach (var sec in k.Fields.Select(f => f.Section).Distinct())
        {
            var fs = k.Fields.Where(f => f.Section == sec && f.Key != "name" && f.Shows(x => o.Get(x))).ToList();
            if (fs.Count == 0) continue;
            s.Children.Add(Pk.H(sec == "Basics" ? "Properties" : sec));
            foreach (var f in fs) s.Children.Add(Pk.Pair(f.Label, ShowValue(o, f), Narrow));
        }
        var cat = o.P.Where(x => x.Key.StartsWith("s:", StringComparison.OrdinalIgnoreCase)).ToList();
        if (cat.Count > 0)
        {
            s.Children.Add(Pk.H("Configuration settings"));
            foreach (var kv in cat) s.Children.Add(Pk.Pair(EpSchemas.Label("config", kv.Key), kv.Value, Narrow));
        }
        return s;
    }

    static string ShowValue(EpObj o, EpField f)
    {
        string v = EpSchemas.Value(o, f.Key);
        if (f.Type == "script" || f.Type == "longtext") return v;
        if (v.Length == 0) return "Not configured";
        return v.Replace("\n", ", ");
    }

    /// <summary>Device (and app) status: what each device reports for this object.</summary>
    void Status(StackPanel s, EpObj o)
    {
        if (o.Kind == "app")
        {
            s.Children.Add(Pk.H("Device install status"));
            var rows = T.Devices.Where(d => d.Apps.ContainsKey(o.Name)).Select(d => { var a = d.Apps[o.Name]; return new[] { d.Name, d.PrimaryUser, a.Status, a.Detail, a.When == DateTime.MinValue ? "" : EpText.Date(a.When) }; }).ToList();
            s.Children.Add(Pk.Table(new[] { "Device", "User", "Status", "Details", "When" }, rows, i => Go("device|" + rows[i][0]), Narrow, "No device has reported this app yet. Assign it and sync a device."));
            return;
        }
        if (!EpSchemas.Get(o.Kind).Assignable) return;
        var st = T.Devices.Select(d => new { d, p = d.Policies.FirstOrDefault(x => x.Kind == o.Kind && EpText.Eq(x.Policy, o.Name)) }).Where(x => x.p != null).ToList();
        s.Children.Add(Pk.H("Device status"));
        var r2 = st.Select(x => new[] { x.d.Name, x.d.PrimaryUser, x.p.Status, x.p.Detail, x.d.LastSync == DateTime.MinValue ? "" : EpText.Date(x.d.LastSync) }).ToList();
        s.Children.Add(Pk.Table(new[] { "Device", "User", "Status", "Details", "Last check-in" }, r2, i => Go("device|" + r2[i][0]), Narrow, "No device has reported this yet: devices see changes at their next check-in (Sync on the device page, or skip time)."));
    }

    void Extras(StackPanel s, EpObj o)
    {
        switch (o.Kind)
        {
            case "group":
                {
                    s.Children.Add(Pk.H("Members"));
                    var mem = T.Members(o.Name);
                    bool assigned = o.Is("membership", "Assigned");
                    s.Children.Add(Pk.Small(assigned ? "Assigned membership: add and remove members here." : "Dynamic membership: members come from the rule " + o["rule"]));
                    foreach (var m in mem)
                    {
                        var m0 = m;
                        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
                        if (assigned && o.List("members").Any(x => EpText.Eq(x, m0.Name) || EpText.Eq(x, m0["alias"])))
                        { var rm = Pk.Small("Remove", () => RunOp(EpOps.Members(T, o, false, new List<string> { m0.Kind == "user" ? m0["alias"] : m0.Name }))); DockPanel.SetDock(rm, Dock.Right); row.Children.Add(rm); }
                        var t = Pk.Txt((m is EpDevice ? "Device  " : "User  ") + m.Name + (m.Kind == "user" ? " (" + m["upn"] + ")" : ""), 14); t.VerticalAlignment = VerticalAlignment.Center;
                        row.Children.Add(t);
                        s.Children.Add(Pk.Capped(row, 600));
                    }
                    if (mem.Count == 0) s.Children.Add(Pk.Small("No members."));
                    foreach (var sub in o.List("members").Where(x => T.Group(x) != null)) s.Children.Add(Pk.Small("Nested group: " + sub));
                    if (assigned)
                    {
                        var pick = Pk.Combo(new[] { "" }.Concat(T.Users.Select(u => u["alias"])).Concat(T.Devices.Where(d => d.InDirectory).Select(d => d.Name)).Concat(T.GroupsList.Where(g => g != o).Select(g => g.Name)), "", 200);
                        Pk.FirstLabel(pick, "Pick a member...");
                        s.Children.Add(Pk.Row(pick, Pk.Btn("Add member", () => { if (Pk.Val(pick).Length > 0) RunOp(EpOps.Members(T, o, true, new List<string> { Pk.Val(pick) })); })));
                    }
                    break;
                }
            case "filter":
                {
                    s.Children.Add(Pk.H("Devices that match now"));
                    var rb = new RuleBox(T, "filter", o["rule"], x => { }, () => o["platform"]);
                    var names = rb.Preview();
                    s.Children.Add(Pk.Txt(names.Count == 0 ? "None." : string.Join(", ", names), 14));
                    break;
                }
            case "ring":
                {
                    s.Children.Add(Pk.H("Pause and resume"));
                    s.Children.Add(Pk.Small("Quality updates: " + (o.Yes("pausedQuality") ? "paused" : "running") + ".  Feature updates: " + (o.Yes("pausedFeature") ? "paused" : "running") + ". Pausing lasts up to 35 days in the real service."));
                    s.Children.Add(Pk.Row(
                        Pk.Btn(o.Yes("pausedQuality") ? "Resume quality updates" : "Pause quality updates", () => RunOp(EpOps.Update(T, o, Pair("pausedQuality", o.Yes("pausedQuality") ? "No" : "Yes")))),
                        Pk.Btn(o.Yes("pausedFeature") ? "Resume feature updates" : "Pause feature updates", () => RunOp(EpOps.Update(T, o, Pair("pausedFeature", o.Yes("pausedFeature") ? "No" : "Yes"))))));
                    break;
                }
            case "autopilot":
                {
                    s.Children.Add(Pk.H("Assign user"));
                    var pick = Pk.Combo(new[] { "" }.Concat(T.Users.Select(u => u["alias"])), o["user"], 200);
                    s.Children.Add(Pk.Row(pick, Pk.Btn("Save", () => RunOp(EpOps.Update(T, o, Pair("user", Pk.Val(pick)))))));
                    s.Children.Add(Pk.Pair("Profile status", o["profileStatus"], Narrow));
                    s.Children.Add(Pk.Pair("Profile", o["profile"], Narrow));
                    break;
                }
            case "user":
                {
                    s.Children.Add(Pk.H("Devices and groups"));
                    s.Children.Add(Pk.Pair("Devices", string.Join(", ", T.Devices.Where(d => EpText.Eq(d.PrimaryUser, o["alias"])).Select(d => d.Name)), Narrow));
                    s.Children.Add(Pk.Pair("Groups", string.Join(", ", T.GroupsList.Where(g => T.InGroup(g.Name, o)).Select(g => g.Name)), Narrow));
                    break;
                }
        }
    }

    static Dictionary<string, string> Pair(string k, string v) => new(StringComparer.OrdinalIgnoreCase) { { k, v } };
}
