// Cert Lab - assignments, dynamic rules and What If in the practice portal (App Hub's UiEpAssign / UiEpRules /
// UiEpWhatIf): included and excluded groups (All users / All devices too), app intents, an optional filter, the
// warnings EpOps gives; the rule editor of dynamic groups and filters (checks itself as you type, "Validate rules"
// shows who matches now); and Conditional Access > What If (describe a sign-in, see the outcome and each policy).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LearningLab.Views;
using CertLab;

namespace LearningLab.CertLab.Portal;

public class AssignEditor : StackPanel
{
    readonly EpWorkspace W;
    readonly EpObj O;
    readonly EpKind K;
    readonly List<EpAssign> pending;      // the wizard's list, or null: change the object at once
    public ComboBox IntentBox, GroupBox, FilterBox, ModeBox;
    public readonly List<string> Warnings = new();

    public AssignEditor(EpWorkspace w, EpObj o, EpKind k, List<EpAssign> pending)
    {
        W = w; O = o; K = k; this.pending = pending;
        Render();
    }

    IEnumerable<EpAssign> Current => pending ?? (O == null ? new List<EpAssign>() : O.Assignments);

    void Render()
    {
        Children.Clear();
        if (K.Kind == "app") Children.Add(Pk.Small("Required installs on its own. Available for enrolled devices shows the app in Company Portal (user groups). Uninstall removes it. Excluded wins over the others."));
        else Children.Add(Pk.Small("Included groups get it; excluded groups never do, even if they are also included. Devices get changes at their next check-in."));
        foreach (var intent in K.Intents)
        {
            var list = Current.Where(a => a.Intent == intent).ToList();
            var h = Pk.Txt(intent == "include" ? "Included groups" : intent == "exclude" ? "Excluded groups" : EpOps.IntentName(intent), 14, true); h.Margin = new Thickness(0, 8, 0, 3);
            Children.Add(h);
            if (list.Count == 0) Children.Add(Pk.Small("None."));
            foreach (var a in list)
            {
                var a0 = a;
                var row = new DockPanel { Margin = new Thickness(0, 0, 0, 3) };
                var rm = Pk.Small("Remove", () => Remove(a0));
                DockPanel.SetDock(rm, Dock.Right); row.Children.Add(rm);
                string holds = W.T.GroupHolds(a.Group);
                var t = Pk.Txt(a.Group + (holds.Length > 0 ? "  (" + holds + "s)" : "") + (a.Filter.Length > 0 ? "  -  filter " + a.Filter + " (" + (a.FilterMode == "exclude" ? "exclude" : "include") + " filtered devices)" : ""), 14);
                t.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(t);
                Children.Add(Pk.Capped(row, 640));
            }
        }
        if (O != null && pending == null) foreach (var x in W.T.MixedWarnings(O)) Children.Add(WarnText(x));
        foreach (var x in Warnings) Children.Add(WarnText(x));
        var addHead = Pk.Txt("Add an assignment", 14, true, Ui.AccentText); addHead.Margin = new Thickness(0, 12, 0, 4);
        Children.Add(addHead);
        var wp = new WrapPanel();
        IntentBox = new ComboBox { MinWidth = 160, MinHeight = 44, Margin = new Thickness(0, 0, 6, 6), FontSize = 14 };
        foreach (var i in K.Intents) IntentBox.Items.Add(new ComboBoxItem { Content = i == "include" ? "Include" : i == "exclude" ? "Exclude" : EpOps.IntentName(i), Tag = i, MinHeight = 40 });
        IntentBox.SelectedIndex = 0;
        GroupBox = Pk.Combo(new[] { "", EpTenant.AllUsers, EpTenant.AllDevices }.Concat(W.T.GroupsList.Select(g => g.Name).OrderBy(x => x)), "", 200);
        Pk.FirstLabel(GroupBox, "Pick a group...");
        FilterBox = Pk.Combo(new[] { "" }.Concat(W.T.Of("filter").Select(f => f.Name)), "", 160);
        Pk.FirstLabel(FilterBox, "No filter");
        ModeBox = Pk.Combo(new[] { "include", "exclude" }, "include", 160);
        ((ComboBoxItem)ModeBox.Items[0]).Content = "Include filtered devices"; ((ComboBoxItem)ModeBox.Items[1]).Content = "Exclude filtered devices";
        wp.Children.Add(IntentBox); wp.Children.Add(GroupBox);
        if (K.Kind != "autopilotProfile") { wp.Children.Add(FilterBox); wp.Children.Add(ModeBox); }
        wp.Children.Add(Pk.Accent("Add", () => Add(Pk.Val(GroupBox), Pk.Val(IntentBox), Pk.Val(FilterBox), Pk.Val(ModeBox))));
        Children.Add(wp);
    }

    static TextBlock WarnText(string x) { var t = Pk.Txt(x, 13, false, Pk.WarnC); t.Margin = new Thickness(0, 6, 0, 0); return t; }

    /// <summary>Adds an assignment (the Add button); in the wizard it waits for Create.</summary>
    public EpResult Add(string group, string intent, string filter, string mode)
    {
        if (string.IsNullOrEmpty(group)) { W.Say("Pick a group first.", false); return EpResult.Fail("Pick a group first."); }
        var a = new EpAssign { Group = group, Intent = intent, Filter = filter ?? "", FilterMode = string.IsNullOrEmpty(filter) ? "" : (mode ?? "include") };
        if (pending != null)
        {
            bool virt = group == EpTenant.AllUsers || group == EpTenant.AllDevices;
            if (virt && a.Exclude) { W.Say(group + " can't be excluded.", false); return EpResult.Fail(group + " can't be excluded."); }
            if (a.Exclude && a.Filter.Length > 0) { W.Say("Filters can't be used on excluded groups.", false); return EpResult.Fail("Filters can't be used on excluded groups."); }
            pending.RemoveAll(x => EpText.Eq(x.Group, group));
            pending.Add(a);
            Warnings.Clear();
            bool exU = pending.Any(x => x.Exclude && W.T.GroupHolds(x.Group) == "user"), exD = pending.Any(x => x.Exclude && W.T.GroupHolds(x.Group) == "device");
            bool inU = pending.Any(x => !x.Exclude && W.T.GroupHolds(x.Group) == "user"), inD = pending.Any(x => !x.Exclude && W.T.GroupHolds(x.Group) == "device");
            if (exU && inD || exD && inU) Warnings.Add("Mixing user and device groups across include and exclude doesn't work as people expect: exclude users from user groups and devices from device groups.");
            Render();
            return new EpResult { Message = "Will assign to " + group + "." };
        }
        var r = EpOps.Assign(W.T, O, a);
        if (r.Ok) W.T.Tick();
        W.Say(r.Text, r.Ok);
        Render();
        return r;
    }

    void Remove(EpAssign a)
    {
        if (pending != null) { pending.Remove(a); Render(); return; }
        var r = EpOps.Unassign(W.T, O, a.Group);
        if (r.Ok) W.T.Tick();
        W.Say(r.Text, r.Ok);
        Render();
    }
}

/// <summary>The rule editor of dynamic groups and assignment filters.</summary>
public class RuleBox : StackPanel
{
    public readonly TextBox Box;
    public readonly TextBlock Status;
    public readonly StackPanel PreviewPanel = new();
    public string Message;                 // null while the rule is valid (or empty)
    readonly EpTenant t;
    readonly string kind;                  // group or filter
    readonly Func<string> scope;           // the group's membership type, or the filter's platform

    public RuleBox(EpTenant tenant, string kind, string text, Action<string> changed, Func<string> scope)
    {
        t = tenant; this.kind = kind; this.scope = scope;
        Box = Pk.Multi(text, 90, true);
        Box.TextWrapping = TextWrapping.Wrap;
        Status = Pk.Txt("", 13); Status.Margin = new Thickness(0, 0, 0, 4);
        Children.Add(Box); Children.Add(Status);
        var props = kind == "filter" ? EpRules.FilterProps.Select(p => "device." + p) : (scope().Contains("device") ? EpRules.DeviceProps.Select(p => "device." + p) : EpRules.UserProps.Select(p => "user." + p));
        var add = Pk.Combo(new[] { "" }.Concat(props), "", 240);
        Pk.FirstLabel(add, "Insert a property...");
        add.SelectedIndex = 0;
        add.SelectionChanged += (s, e) => Pk.Safe(() =>
        {
            string p = Pk.Val(add);
            if (p.Length == 0) return;
            string clause = "(" + p + " -eq \"\")";
            string cur = Box.Text ?? "";
            Box.Text = cur.Trim().Length == 0 ? clause : cur.TrimEnd() + " -and " + clause;
            add.SelectedIndex = 0;
        });
        Children.Add(Pk.Row(add, Pk.Small("Validate rules", () => Preview())));
        Children.Add(PreviewPanel);
        Pk.OnText(Box, () => { changed(Box.Text ?? ""); Check(); });
        Check();
    }

    public void Check()
    {
        string r = (Box.Text ?? "").Trim();
        Message = r.Length == 0 ? null : EpRules.Check(r, kind);
        if (Message == null && r.Length > 0 && kind == "group")
        {
            string subj = EpRules.Subject(EpRules.Parse(r, "group"));
            string want = scope().Contains("device") ? "device" : "user";
            if (subj != want) Message = "A " + scope().ToLowerInvariant() + " group needs a rule on " + want + ". properties.";
        }
        Status.Text = r.Length == 0 ? "Type a rule, or insert a property and fill in the value." : Message ?? "The rule is valid.";
        Status.Foreground = Pk.B(r.Length == 0 ? Ui.Muted : Message == null ? Pk.GoodC : Pk.BadC);
    }

    /// <summary>Who or what the rule matches now (users or devices); shown under the editor.</summary>
    public List<string> Preview()
    {
        PreviewPanel.Children.Clear();
        var names = new List<string>();
        string r = (Box.Text ?? "").Trim();
        Check();
        if (r.Length == 0 || Message != null) { PreviewPanel.Children.Add(Pk.Error(r.Length == 0 ? "There is no rule to check yet." : "Fix the rule first: " + Message)); return names; }
        if (kind == "filter")
        {
            string pl = scope();
            foreach (var d in t.Devices.Where(x => OnPlatform(x, pl)))
                if (EpRules.Matches(r, "filter", p => t.FilterProp(d, p))) names.Add(d.Name);
        }
        else if (EpRules.Subject(EpRules.Parse(r, "group")) == "device")
        {
            foreach (var d in t.Devices.Where(x => x.InDirectory)) if (EpRules.Matches(r, "group", p => t.RuleProp(d, p))) names.Add(d.Name);
        }
        else foreach (var u in t.Users) if (EpRules.Matches(r, "group", p => t.RuleProp(u, p))) names.Add(u["alias"] + " (" + u.Name + ")");
        PreviewPanel.Children.Add(Pk.Txt(names.Count == 0 ? "Nothing matches this rule now." : names.Count + " match" + (names.Count == 1 ? "es" : "") + " now: " + string.Join(", ", names), 13));
        return names;
    }

    static bool OnPlatform(EpDevice d, string platform)
    {
        if (platform.StartsWith("Windows", StringComparison.Ordinal)) return d.Windows;
        if (platform.StartsWith("iOS", StringComparison.Ordinal)) return d.Is("os", "iOS");
        if (platform.StartsWith("Android", StringComparison.Ordinal)) return d.Is("os", "Android");
        return true;
    }
}

/// <summary>Conditional Access > What If (and the Devices tab's "Sign in to an app").</summary>
public class WhatIfView : StackPanel
{
    readonly EpTenant T;
    readonly bool narrow;
    public ComboBox UserBox, DeviceBox, AppBox, PlatformBox, LocationBox, ClientBox, ClientAppBox;
    public CheckBox MfaBox;
    public TextBlock Outcome;
    public readonly StackPanel ResultPanel = new();
    public EpCaResult Last;

    public WhatIfView(EpTenant t, bool narrow, string device)
    {
        T = t; this.narrow = narrow;
        UserBox = Pk.Combo(T.Users.Select(u => u["alias"]), "jordan", 200);
        DeviceBox = Pk.Combo(new[] { "" }.Concat(T.Devices.Select(d => d.Name)), device ?? "", 200);
        AppBox = Pk.Combo(EpSchemas.CloudApps, "Office 365", 220);
        PlatformBox = Pk.Combo(new[] { "", "Windows", "iOS", "Android", "macOS", "Linux" }, "", 180);
        Pk.FirstLabel(PlatformBox, "From the device");
        LocationBox = Pk.Combo(new[] { "" }.Concat(T.Of("location").Select(l => l.Name)), "", 200);
        Pk.FirstLabel(LocationBox, "Anywhere else");
        ClientBox = new ComboBox { MinWidth = 220, MinHeight = 44, Margin = new Thickness(0, 0, 6, 6), HorizontalAlignment = HorizontalAlignment.Left, FontSize = 14 };
        foreach (var c in new[] { "browser|Browser", "mobile|Mobile apps and desktop clients", "eas|Exchange ActiveSync clients", "other|Other clients" })
            ClientBox.Items.Add(new ComboBoxItem { Content = c.Split('|')[1], Tag = c.Split('|')[0], MinHeight = 40 });
        ClientBox.SelectedIndex = 0;
        ClientAppBox = Pk.Combo(new[] { "", "Microsoft Outlook", "Microsoft Teams", "Microsoft Edge", "Microsoft Word", "Microsoft OneDrive" }, "", 200);
        Pk.FirstLabel(ClientAppBox, "(none)");
        MfaBox = Pk.Check("The person completed multifactor authentication", false);
        MfaBox.Margin = new Thickness(0, 4, 0, 8);
        Line("User", UserBox); Line("Device", DeviceBox); Line("Cloud app", AppBox); Line("Device platform", PlatformBox);
        Line("Location", LocationBox); Line("Client app", ClientBox); Line("App on the phone", ClientAppBox);
        Children.Add(MfaBox);
        var go = Pk.Accent("What If", () => Run()); go.HorizontalAlignment = HorizontalAlignment.Left;
        Children.Add(go);
        Outcome = Pk.Txt("", 18, true); Outcome.Margin = new Thickness(0, 12, 0, 4);
        Children.Add(Outcome);
        Children.Add(ResultPanel);
    }

    void Line(string label, Control e)
    {
        var l = Pk.Txt(label, 14, false, Pk.LabelC); l.VerticalAlignment = VerticalAlignment.Center;
        if (narrow) { l.Margin = new Thickness(0, 4, 0, 2); Children.Add(l); Children.Add(e); return; }
        var g = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        g.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(160)));
        g.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1, GridUnitType.Star)));
        Grid.SetColumn(e, 1);
        g.Children.Add(l); g.Children.Add(e);
        Children.Add(g);
    }

    /// <summary>Evaluates the sign-in and shows the outcome and each policy's line.</summary>
    public EpCaResult Run()
    {
        var s = new EpSignIn { User = Pk.Val(UserBox), Device = Pk.Val(DeviceBox), App = Pk.Val(AppBox), Platform = Pk.Val(PlatformBox), Location = Pk.Val(LocationBox), Client = Pk.Val(ClientBox), ClientApp = Pk.Val(ClientAppBox), Mfa = MfaBox.IsChecked == true };
        var r = T.EvaluateSignIn(s);
        Last = r;
        Outcome.Text = "Result: " + r.Outcome;
        Outcome.Foreground = Pk.StatusBrush(r.Outcome == "Allowed" ? "allowed" : r.Outcome == "Blocked" ? "blocked" : "mfa");
        ResultPanel.Children.Clear();
        if (r.Missing.Count > 0) ResultPanel.Children.Add(Pk.Txt("To get in, the sign-in needs " + string.Join(", ", r.Missing) + ".", 14));
        if (r.Lines.Count == 0) ResultPanel.Children.Add(Pk.Small("No Conditional Access policy is on (or in report-only), so nothing applies."));
        var rows = r.Lines.Select(l => new[] { l.Policy + (l.ReportOnly ? " (report-only)" : ""), l.Applies, l.Applies == "Applies" ? l.Result : "", l.Why });
        ResultPanel.Children.Add(Pk.Table(new[] { "Policy", "Applies", "Result", "Why" }, rows, null, narrow, null));
        ResultPanel.Children.Add(Pk.Small("Report-only policies are evaluated and shown but don't change the result."));
        return r;
    }
}

public partial class EpWorkspace
{
    public WhatIfView WhatIf;

    Control WhatIfBlade()
    {
        var s = Stack(Title("What If", "Try a sign-in against the Conditional Access policies: which ones apply, and would the person get in?"));
        WhatIf = new WhatIfView(T, Narrow, "");
        s.Children.Add(WhatIf);
        return s;
    }
}
