// Cert Lab - Group Policy Management (practice; App Hub's UiWsGpmc / UiWsGpmc2): the forest's domain, OUs and their
// linked GPOs, the GPO container, WMI filters, sites, Group Policy Modeling and Results in the tree; a container's
// links and inheritance, or a GPO's Scope (links, security filtering, WMI filter), Details, Settings and Delegation on
// the page; the Group Policy Management Editor (practice) with the settings Cert Lab knows; Modeling and Results.
// It talks to the PDC emulator, and every tap is a console line (gpo new / link / set ..., inheritance ..., wmi ...).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LearningLab.Views;
using CertLab;

namespace LearningLab.CertLab.Portal;

public partial class WsWorkspace
{
    public WsForm GpForm;
    public WsForm SettingForm;
    public string ModelUser = "bcole", ModelComputer = "CL1";

    Control BuildGpmc()
    {
        var d = ConsoleDir;
        if (Node.Length == 0) Node = "som:" + Lab.DomainDn;
        var tree = new StackPanel();
        tree.Children.Add(TreeItem("Forest: " + Lab.DomainDns, 0, false, IcGlobe, null));
        void Som(WsAdObj c, int depth)
        {
            string key = "som:" + c.Dn;
            tree.Children.Add(TreeItem(c.Is("domainDNS") ? Lab.DomainDns : c.Name + (c.Get("gPOptions") == "1" ? "  (blocked)" : ""), depth, Node == key, c.Is("domainDNS") ? IcDomain : IcFolder, () => { Node = key; Sub = ""; GpForm = null; Show(); }));
            foreach (var l in WsLab.Links(c))
            {
                var g = d.ById(l[0]); if (g == null) continue;
                string gk = "gpo:" + g.Get("displayName");
                tree.Children.Add(TreeItem(g.Get("displayName") + (l[2] == "1" ? "  (enforced)" : "") + (l[1] != "1" ? "  (link off)" : ""), depth + 1, Node == gk, IcScroll, () => { Node = gk; Sub = ""; GpForm = null; Show(); }));
            }
            foreach (var k in d.Children(c.Dn).Where(x => x.Is("organizationalUnit")).OrderBy(x => x.Name)) Som(k, depth + 1);
        }
        Som(d.ByDn(Lab.DomainDn), 1);
        tree.Children.Add(TreeItem("Group Policy Objects", 2, Node == "gpos", IcFolder, () => { Node = "gpos"; Sub = ""; GpForm = null; Show(); }));
        foreach (var g in Lab.Gpos(d).OrderBy(x => x.Get("displayName"))) { string gk = "gpo:" + g.Get("displayName"); tree.Children.Add(TreeItem(g.Get("displayName"), 3, Node == gk && Sub != "", IcScroll, () => { Node = gk; Sub = "scope"; GpForm = null; Show(); })); }
        tree.Children.Add(TreeItem("WMI Filters", 2, Node == "wmi", IcFilter, () => { Node = "wmi"; Sub = ""; GpForm = null; Show(); }));
        tree.Children.Add(TreeItem("Sites", 1, false, IcFolder, null));
        foreach (var s in Lab.Sites(d).OrderBy(x => x.Name)) { string sk = "som:" + s.Dn; tree.Children.Add(TreeItem(s.Name, 2, Node == sk, IcGlobe, () => { Node = sk; Sub = ""; Show(); })); }
        tree.Children.Add(TreeItem("Group Policy Modeling", 0, Node == "modeling", IcScroll, () => { Node = "modeling"; Sub = ""; Show(); }));
        tree.Children.Add(TreeItem("Group Policy Results", 0, Node == "results", IcScroll, () => { Node = "results"; Sub = ""; Show(); }));
        Control page;
        if (Node.StartsWith("som:", StringComparison.Ordinal)) page = SomPage(d, d.ByDn(Node.Substring(4)) ?? d.ByDn(Lab.DomainDn));
        else if (Node.StartsWith("gpo:", StringComparison.Ordinal)) { var g = Lab.FindGpo(d, Node.Substring(4)); page = g == null ? Pk.Small("That GPO doesn't exist any more.") : Sub.StartsWith("edit", StringComparison.Ordinal) ? GpoEditor(d, g) : GpoPage(d, g); }
        else if (Node == "gpos") page = GposPage(d);
        else if (Node == "wmi") page = WmiPage(d);
        else page = RsopPage(d, Node == "results");
        return TwoPane(tree, page);
    }

    /// <summary>The "Create a GPO and link it here" form on a container's page.</summary>
    public WsForm NewLinkedGpo()
    {
        var d = ConsoleDir;
        var c = Node.StartsWith("som:", StringComparison.Ordinal) ? d.ByDn(Node.Substring(4)) ?? d.ByDn(Lab.DomainDn) : d.ByDn(Lab.DomainDn);
        string target = c.Is("site") ? c.Name : c.Dn;
        var f = F("New GPO linked to " + (c.Is("domainDNS") ? Lab.DomainDns : c.Name));
        f.Text("name", "Name", "");
        f.Submit("OK", () => { if (Run("gpo new " + Q(f.V("name"))).Ok) Run("gpo link " + Q(f.V("name")) + " " + Q(target)); });
        GpForm = f; Show();
        return f;
    }

    Control SomPage(WsDir d, WsAdObj c)
    {
        var sp = new StackPanel();
        string target = c.Is("site") ? c.Name : c.Dn;
        sp.Children.Add(Title(c.Is("domainDNS") ? Lab.DomainDns : c.Name, c.Is("site") ? "site" : Dn.Canonical(c.Dn)));
        var bar = Bar(Act("Create a GPO and link it here", () => NewLinkedGpo()));
        var others = Lab.Gpos(d).Select(g => g.Get("displayName")).OrderBy(x => x).ToList();
        var pick = Pk.First(Pk.Combo(others, "", 200));
        bar.Children.Add(pick); bar.Children.Add(Act("Link an existing GPO", () => Run("gpo link " + Q(Pk.Val(pick)) + " " + Q(target))));
        if (c.Is("organizationalUnit") || c.Is("domainDNS"))
        {
            var bi = Pk.Check("Block Inheritance", c.Get("gPOptions") == "1");
            bi.Margin = new Thickness(8, 0, 0, 4);
            bi.Click += (s, e) => Pk.Safe(() => Run("inheritance " + (bi.IsChecked == true ? "block " : "unblock ") + Q(target)));
            bar.Children.Add(bi);
        }
        sp.Children.Add(bar);
        if (GpForm != null) sp.Children.Add(Pk.Card(GpForm.Panel));
        sp.Children.Add(Pk.H("Linked Group Policy Objects"));
        var links = WsLab.Links(c);
        if (links.Count == 0) sp.Children.Add(Pk.Small("No GPOs are linked here."));
        for (int i = 0; i < links.Count; i++)
        {
            var g = d.ById(links[i][0]); if (g == null) continue;
            string n = g.Get("displayName"); bool enf = links[i][2] == "1", on = links[i][1] == "1"; int order = i + 1;
            var card = new StackPanel();
            card.Children.Add(Pk.Row(Pk.Txt(order + ".", 14, false, Ui.Muted), Pk.Link(n, () => { Node = "gpo:" + n; Sub = "scope"; Show(); })));
            card.Children.Add(Pk.Txt("Enforced: " + (enf ? "Yes" : "No") + "   Link enabled: " + (on ? "Yes" : "No"), 13, false, Pk.LabelC));
            var acts = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            acts.Children.Add(Act(enf ? "Not enforced" : "Enforce", () => Run("gpo setlink " + Q(n) + " " + Q(target) + " enforced=" + (enf ? "no" : "yes"))));
            acts.Children.Add(Act(on ? "Disable link" : "Enable link", () => Run("gpo setlink " + Q(n) + " " + Q(target) + " enabled=" + (on ? "no" : "yes"))));
            if (order > 1) acts.Children.Add(Act("Up", () => Run("gpo setlink " + Q(n) + " " + Q(target) + " order=" + (order - 1))));
            acts.Children.Add(Act("Delete link", () => Run("gpo unlink " + Q(n) + " " + Q(target))));
            card.Children.Add(acts);
            sp.Children.Add(new Border { Child = card, BorderBrush = Pk.B(Ui.BorderC), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 2, 0, 6) });
        }
        if (!c.Is("site"))
        {
            sp.Children.Add(Pk.H("Group Policy Inheritance (precedence: 1 wins)"));
            var order2 = Lab.GpoOrder(d, "CN=probe," + c.Dn, null, null); order2.Reverse();
            sp.Children.Add(Pk.Table(new[] { "Precedence", "GPO", "Location", "Enforced" }, order2.Select((r, i) => new[] { (i + 1).ToString(), r.Name, r.LinkedTo, r.Enforced ? "Yes" : "No" }), null, Narrow, "Nothing reaches this container."));
        }
        return sp;
    }

    Control GposPage(WsDir d)
    {
        var sp = new StackPanel();
        sp.Children.Add(Title("Group Policy Objects", Lab.DomainDns));
        var f = F(null); f.Text("name", "New GPO", ""); f.Text("comment", "Comment", ""); f.Submit("Create", () => Run("gpo new " + Q(f.V("name")) + (f.V("comment").Length > 0 ? " comment=" + Q(f.V("comment")) : "")));
        GpForm = f;
        sp.Children.Add(f.Panel);
        var gpos = Lab.Gpos(d).OrderBy(x => x.Get("displayName")).ToList();
        sp.Children.Add(Pk.Table(new[] { "Name", "GPO status", "WMI filter", "Modified" }, gpos.Select(g => { var w = d.ById(g.Get("wmiFilter")); return new[] { g.Get("displayName"), WsLab.Status(g), w == null ? "None" : w.Get("msWMI-Name"), g.Get("whenChanged").Length > 0 ? g.Get("whenChanged") : g.Get("whenCreated") }; }), i => { Node = "gpo:" + gpos[i].Get("displayName"); Sub = "scope"; Show(); }, Narrow, ""));
        return sp;
    }

    Control WmiPage(WsDir d)
    {
        var sp = new StackPanel();
        sp.Children.Add(Title("WMI Filters", Lab.DomainDns));
        foreach (var w in Lab.WmiFilters(d).OrderBy(x => x.Get("msWMI-Name")))
        {
            string wn = w.Get("msWMI-Name");
            sp.Children.Add(Pk.Card(Pk.Col(Pk.Txt(wn, 14.5, true), Pk.Small(w.Get("description")), Pk.MonoText(w.Get("query")), Act("Delete", () => Run("wmi delete " + Q(wn))))));
        }
        var f = F("New WMI filter"); f.Text("name", "Name", ""); f.Text("description", "Description", ""); f.Text("query", "Query", "SELECT * FROM Win32_OperatingSystem WHERE ProductType = '1'");
        f.Submit("Save", () => Run("wmi new " + f.Pairs("name", "description", "query")));
        sp.Children.Add(Pk.Card(f.Panel));
        return sp;
    }

    Control GpoPage(WsDir d, WsAdObj g)
    {
        string n = g.Get("displayName"), qn = Q(n);
        var sp = new StackPanel();
        sp.Children.Add(Title(n, "GPO"));
        if (Sub.Length == 0) Sub = "scope";
        var tabs = Bar();
        foreach (var t in new[] { "scope|Scope", "details|Details", "settings|Settings", "delegation|Delegation" }) { var p = t.Split('|'); string id = p[0]; var b = Act(p[1], () => { Sub = id; Show(); }); Pk.Picked(b, Sub == id); tabs.Children.Add(b); }
        tabs.Children.Add(Pk.Accent("Edit...", () => { Sub = "edit"; Show(); }));
        sp.Children.Add(tabs);
        if (Sub == "scope")
        {
            sp.Children.Add(Pk.H("Links"));
            var links = Lab.WhereLinked(d, g).ToList();
            sp.Children.Add(Pk.Table(new[] { "Location", "Enforced", "Link enabled" }, links.Select(kv => new[] { kv.Key.Is("site") ? kv.Key.Name : Dn.Canonical(kv.Key.Dn), kv.Value[2] == "1" ? "Yes" : "No", kv.Value[1] == "1" ? "Yes" : "No" }), null, Narrow, "This GPO isn't linked anywhere."));
            sp.Children.Add(Pk.H("Security Filtering"));
            sp.Children.Add(Pk.Small("The settings in this GPO apply only to these groups, users and computers:"));
            foreach (var e in g.List("gpPerm").Where(x => x.EndsWith("|GpoApply", StringComparison.Ordinal))) { string who = e.Split('|')[0]; sp.Children.Add(Pk.Row(Pk.Txt(who, 14), Pk.Link("remove", () => Run("gpo filter " + qn + " remove " + Q(who))))); }
            var add = Pk.Box("", 200); add.Width = 200;
            sp.Children.Add(Pk.Row(add, Act("Add", () => Run("gpo filter " + qn + " add " + Q(Pk.T(add).Trim())))));
            sp.Children.Add(Pk.H("WMI Filtering"));
            var w = d.ById(g.Get("wmiFilter"));
            var cb = Pk.Combo(new[] { "<none>" }.Concat(Lab.WmiFilters(d).Select(x => x.Get("msWMI-Name"))), w == null ? "<none>" : w.Get("msWMI-Name"), 220);
            sp.Children.Add(Pk.Row(cb, Act("Apply", () => Run("gpo wmi " + qn + " " + (Pk.Val(cb) == "<none>" ? "none" : Q(Pk.Val(cb)))))));
        }
        else if (Sub == "details")
        {
            sp.Children.Add(Pk.Pair("Domain", Lab.DomainDns, Narrow)); sp.Children.Add(Pk.Pair("Owner", g.Get("owner").Length > 0 ? g.Get("owner") : Lab.NetBios + "\\Domain Admins", Narrow));
            sp.Children.Add(Pk.Pair("Created", g.Get("whenCreated"), Narrow)); sp.Children.Add(Pk.Pair("Modified", g.Get("whenChanged"), Narrow));
            sp.Children.Add(Pk.Pair("User version", g.Get("versionUser") + " (AD), " + g.Get("versionUser") + " (SYSVOL)", Narrow)); sp.Children.Add(Pk.Pair("Computer version", g.Get("versionComputer") + " (AD), " + g.Get("versionComputer") + " (SYSVOL)", Narrow));
            sp.Children.Add(Pk.Pair("Unique ID", "{" + g.Guid.ToUpperInvariant() + "}", Narrow)); sp.Children.Add(Pk.Pair("Comment", g.Get("description"), Narrow));
            var st = Pk.Combo(new[] { "AllSettingsEnabled", "UserSettingsDisabled", "ComputerSettingsDisabled", "AllSettingsDisabled" }, WsLab.Status(g), 240);
            sp.Children.Add(Pk.Row(Pk.Txt("GPO status  ", 14), st, Act("Apply", () => Run("gpo status " + qn + " " + Pk.Val(st)))));
            sp.Children.Add(Bar(Act("Back up...", () => Run("gpo backup " + qn)), Act("Restore from the latest backup", () => Run("gpo restore " + qn)), Act("Delete GPO", () => { if (Run("gpo delete " + qn).Ok) { Node = "gpos"; Sub = ""; Show(); } })));
        }
        else if (Sub == "settings")
        {
            foreach (var sc in new[] { "C", "U" })
            {
                sp.Children.Add(Pk.H(sc == "C" ? "Computer Configuration" + (WsLab.ScopeEnabled(g, "C") ? "" : " (disabled)") : "User Configuration" + (WsLab.ScopeEnabled(g, "U") ? "" : " (disabled)")));
                var set = WsLab.Settings(g, sc);
                if (set.Count == 0) sp.Children.Add(Pk.Small("No settings defined."));
                foreach (var kv in set) { var def = GpCatalog.Get(kv.Key); var p = kv.Value.Split(new[] { '|' }, 2); sp.Children.Add(Pk.Pair(def != null ? def.Name : kv.Key, p[0] + (p.Length > 1 && p[1].Length > 0 ? " (" + p[1] + ")" : ""), Narrow)); }
            }
        }
        else
        {
            sp.Children.Add(Pk.Table(new[] { "Group or user", "Permission" }, g.List("gpPerm").Select(e => e.Split('|')), null, Narrow, ""));
            var who = Pk.Box("", 180); who.Width = 180; var lvl = Pk.Combo(WsLab.PermLevels, "GpoRead", 200);
            sp.Children.Add(Pk.Row(who, lvl, Act("Set", () => Run("gpo perm " + qn + " " + Q(Pk.T(who).Trim()) + " " + Pk.Val(lvl)))));
        }
        return sp;
    }

    /// <summary>The Group Policy Management Editor (practice): the settings by folder; Sub "edit:ID" opens one.</summary>
    Control GpoEditor(WsDir d, WsAdObj g)
    {
        string n = g.Get("displayName");
        var sp = new StackPanel();
        sp.Children.Add(Title("Group Policy Management Editor (practice)", n));
        sp.Children.Add(Bar(Act("Close the editor", () => { Sub = "settings"; Show(); })));
        sp.Children.Add(Pk.Small(Lab.CentralStore(d.Dc) ? "Administrative Templates: policy definitions (ADMX files) retrieved from the central store." : "Administrative Templates: policy definitions (ADMX files) retrieved from the local computer."));
        string open = Sub.StartsWith("edit:", StringComparison.Ordinal) ? Sub.Substring(5) : "";
        foreach (var grp in GpCatalog.All.GroupBy(s => s.Folder))
        {
            var fh = Pk.Txt(grp.Key.Replace("/", "  >  "), 13.5, true, Ui.AccentText); fh.Margin = new Thickness(0, 12, 0, 2);
            sp.Children.Add(fh);
            foreach (var s in grp)
            {
                var cur = WsLab.Settings(g, s.Scope); cur.TryGetValue(s.Id, out var v);
                string state = v == null ? "Not configured" : v.Split('|')[0];
                var vp = v == null ? null : v.Split(new[] { '|' }, 2);
                string val = vp != null && vp.Length > 1 ? vp[1] : "";
                string id = s.Id;
                var line = Pk.Row(Pk.Link(s.Name, () => { Sub = "edit:" + id; Show(); }), Pk.Txt(state + (val.Length > 0 ? " (" + val + ")" : ""), 13, false, state == "Not configured" ? Ui.Muted : Pk.GoodC));
                sp.Children.Add(line);
                if (open == s.Id) sp.Children.Add(Pk.Card(SettingPanel(n, s, state, val)));
            }
        }
        return sp;
    }

    Control SettingPanel(string gpo, GpSetting s, string state, string val)
    {
        var f = F(s.Name);
        f.Panel.Children.Add(Pk.Small((s.IsRegistry ? s.Key + "\\" + s.ValueName : s.Type == "sec" ? "Security setting" : "Preference") + "   (" + (s.Scope == "C" ? "computer" : "user") + " setting)"));
        f.Choice("state", "State", s.Type == "sec" || s.Type == "pref" ? new[] { "Not configured", "Enabled" } : new[] { "Not configured", "Enabled", "Disabled" }, state);
        if (s.Kind == "choice") f.Choice("value", "Option", s.Options.Select(o => o.Split('=')[0]), val);
        else if (s.Kind != "toggle") f.Text("value", s.Kind == "number" ? "Value (number)" : "Value", val);
        f.Submit("Apply", () => Run("gpo set " + Q(gpo) + " setting=" + s.Id + " state=" + Q(f.V("state")) + (f.V("value").Length > 0 ? " value=" + Q(f.V("value")) : "")));
        SettingForm = f;
        return f.Panel;
    }

    Control RsopPage(WsDir d, bool results)
    {
        var sp = new StackPanel();
        sp.Children.Add(Title(results ? "Group Policy Results" : "Group Policy Modeling", results ? "what a computer last applied" : "what would apply, computed on " + d.Dc));
        var users = new[] { "(none)" }.Concat(d.OfClass("user").Where(u => !u.Yes("isCriticalSystemObject") || u.Sam == "Administrator").Select(u => u.Sam).OrderBy(x => x)).ToList();
        var comps = d.OfClass("computer").Select(c => c.Name).OrderBy(x => x).ToList();
        var cu = Pk.Combo(users, ModelUser, 170); var cc = Pk.Combo(comps, ModelComputer, 150);
        sp.Children.Add(Pk.Row(Pk.Txt("User", 14), cu, Pk.Txt("Computer", 14), cc, Act("Show", () => { ModelUser = Pk.Val(cu); ModelComputer = Pk.Val(cc); Show(); })));
        RsopResult r = null;
        var comp = d.Find(ModelComputer, "computer"); var user = ModelUser == "(none)" ? null : d.Find(ModelUser, "user");
        if (results) { if (user != null ? Lab.LastUser.TryGetValue(ModelComputer + "|" + ModelUser, out var x) : Lab.LastComputer.TryGetValue(ModelComputer, out x)) r = x; }
        else if (comp != null) r = Lab.Rsop(d.Dc, comp, user, null);
        LastRsop = r;
        if (r == null) { sp.Children.Add(Pk.Small(results ? "No results: that user hasn't signed in to " + ModelComputer + "." : "Pick a computer.")); return sp; }
        sp.Children.Add(Pk.Small("Site: " + (r.Site ?? "(none)") + "   Loopback: " + (r.Loopback.Length == 0 ? "not configured" : r.Loopback) + (results ? "   Applied: " + WsText.Date(r.When) + " from " + r.Dc : "")));
        foreach (var part in new[] { "C", "U" })
        {
            if (part == "U" && user == null) continue;
            var ap = part == "C" ? r.AppliedC : r.AppliedU; var den = part == "C" ? r.DeniedC : r.DeniedU; var set = part == "C" ? r.C : r.U;
            sp.Children.Add(Pk.H(part == "C" ? "Computer: applied GPOs (last one wins)" : "User: applied GPOs (last one wins)"));
            sp.Children.Add(Pk.Table(new[] { "GPO", "Linked to" }, ap.Select(x => new[] { x.Name, x.LinkedTo + (x.Enforced ? " (enforced)" : "") }), null, Narrow, "None."));
            if (den.Count > 0) { sp.Children.Add(Pk.Small("Denied or not applied:")); sp.Children.Add(Pk.Table(new[] { "GPO", "Reason" }, den.Select(x => new[] { x.Name, x.Reason }), null, Narrow, "")); }
            sp.Children.Add(Pk.Table(new[] { "Setting", "State", "Winning GPO" }, set.Values.Select(v => new[] { v.Def != null ? v.Def.Name : v.Id, v.State + (v.Value.Length > 0 ? " (" + v.Value + ")" : ""), v.Gpo }), null, Narrow, "No settings."));
        }
        return sp;
    }
    /// <summary>What Modeling / Results showed last (the checks).</summary>
    public RsopResult LastRsop;
}
