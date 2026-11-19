// Cert Lab - Active Directory Users and Computers (practice; App Hub's UiWsAduc): the domain's containers and OUs in
// the tree, their objects on the page, forms for new users, groups, OUs and computers, and an object's page (account,
// group membership, password, move, delete). It reads and writes the PDC emulator's copy of the directory, as the real
// console does by default. Every button builds a console line (new user ..., members add ..., move ...) and runs it.
using Avalonia;
using Avalonia.Controls;
using CertLab;

namespace LearningLab.CertLab.Portal;

public partial class WsWorkspace
{
    public WsDir ConsoleDir => Lab.Dir(WsOps.Dc(Lab, new WsOps.Args(), false)) ?? Lab.Main;
    public WsForm AducForm;     // the open "new ..." form (the checks fill it)

    static bool ShowInAduc(WsAdObj o) => (o.Is("organizationalUnit") || o.Is("container") || o.Is("builtinDomain")) && !o.Name.StartsWith("Password Settings", StringComparison.Ordinal) && o.Name != "System" && o.Name != "Configuration" && o.Name != "Policies" && o.Name != "WMIPolicy";

    Control BuildAduc()
    {
        var d = ConsoleDir;
        if (Node.Length == 0 || d.ByDn(Node) == null) Node = Lab.DomainDn;
        var tree = new StackPanel();
        tree.Children.Add(TreeItem(Lab.DomainDns, 0, Dn.Eq(Node, Lab.DomainDn), IcDomain, () => { Node = Lab.DomainDn; Sub = ""; Show(); }));
        void Walk(string dn, int depth)
        {
            foreach (var k in d.Children(dn).Where(ShowInAduc).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                string kdn = k.Dn;
                tree.Children.Add(TreeItem(k.Name, depth, Dn.Eq(Node, kdn), IcFolder, () => { Node = kdn; Sub = ""; AducForm = null; Show(); }));
                if (Dn.Under(Node, kdn)) Walk(kdn, depth + 1);
            }
        }
        Walk(Lab.DomainDn, 1);
        var page = new StackPanel();
        var here = d.ByDn(Node);
        page.Children.Add(Title(here.Is("domainDNS") ? Lab.DomainDns : here.Name, Dn.Canonical(here.Dn) + "   (" + d.Dc + ")"));
        var bar = Bar(Act("New user", () => NewForm("user")), Act("New group", () => NewForm("group")), Act("New OU", () => NewForm("ou")), Act("New computer", () => NewForm("computer")));
        if (here.Is("organizationalUnit"))
        {
            bar.Children.Add(Act(here.Yes("protectedFromAccidentalDeletion") ? "Unprotect" : "Protect from deletion", () => Run("set ou " + Q(here.Dn) + " protect=" + (here.Yes("protectedFromAccidentalDeletion") ? "no" : "yes"))));
            bar.Children.Add(Act("Delete OU", () => { var p = Dn.Parent(here.Dn); if (Run("delete ou " + Q(here.Dn) + " recursive=yes").Ok) { Node = p; Show(); } }));
        }
        page.Children.Add(bar);
        if (AducForm != null) page.Children.Add(Pk.Card(AducForm.Panel));
        var objs = d.Children(Node).Where(o => !ShowInAduc(o)).OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase).ToList();
        var rows = objs.Select(o => new[] { o.Name, Kind(o), o.Get("description").Length > 0 ? o.Get("description") : o.Get("title") }).ToList();
        page.Children.Add(Pk.Table(new[] { "Name", "Type", "Description" }, rows, i => { Sub = objs[i].Dn; AducForm = null; Show(); }, Narrow, "This container is empty."));
        var sel = Sub.Length > 0 ? d.ByDn(Sub) : null;
        if (sel != null) page.Children.Add(Pk.Card(ObjectPage(d, sel)));
        return TwoPane(tree, page);
    }
    static string Kind(WsAdObj o)
    {
        if (o.Is("group")) return "Security Group - " + o.Get("groupScope").Replace("DomainLocal", "Domain Local");
        if (o.Is("user")) return o.Get("enabled") == "FALSE" ? "User (disabled)" : "User";
        if (o.Is("computer")) return "Computer";
        if (o.Is("msDS-GroupManagedServiceAccount")) return "Group Managed Service Account";
        return o.Class;
    }

    /// <summary>Opens the New user / group / OU / computer form in the container shown.</summary>
    public WsForm NewForm(string kind)
    {
        var f = F(kind == "ou" ? "New organizational unit" : "New " + kind + " in " + Dn.Canonical(Node));
        if (kind == "user")
        {
            f.Text("given", "First name", ""); f.Text("surname", "Last name", ""); f.Text("name", "Full name", "");
            f.Text("sam", "User logon name", ""); f.Choice("suffix", "UPN suffix", new[] { Lab.DomainDns }.Concat(Lab.UpnSuffixes), Lab.DomainDns);
            f.Text("password", "Password", ""); f.Check("mustchange", "User must change password at next logon", true); f.Check("enabled", "Account is enabled", true);
            f.Text("department", "Department", "");
            f.Submit("Create", () => Run("new user " + f.Pairs("name", "sam", "given", "surname", "department", "password") + " upn=" + Q(f.V("sam") + "@" + f.V("suffix")) + " enabled=" + f.V("enabled") + " mustchange=" + f.V("mustchange") + " path=" + Q(Node)));
        }
        if (kind == "group")
        {
            f.Text("name", "Group name", ""); f.Choice("scope", "Group scope", new[] { "Global", "DomainLocal", "Universal" }, "Global"); f.Choice("category", "Group type", new[] { "Security", "Distribution" }, "Security"); f.Text("description", "Description", "");
            f.Submit("Create", () => Run("new group " + f.Pairs("name", "scope", "category", "description") + " path=" + Q(Node)));
        }
        if (kind == "ou")
        {
            f.Text("name", "Name", ""); f.Check("protect", "Protect container from accidental deletion", true);
            f.Submit("Create", () => Run("new ou " + f.Pairs("name") + " protect=" + f.V("protect") + " path=" + Q(Node)));
        }
        if (kind == "computer")
        {
            f.Text("name", "Computer name", ""); f.Text("description", "Description", "");
            f.Submit("Create", () => Run("new computer " + f.Pairs("name", "description") + " path=" + Q(Node)));
        }
        AducForm = f; Sub = ""; Show();
        return f;
    }

    Control ObjectPage(WsDir d, WsAdObj o)
    {
        var sp = new StackPanel();
        sp.Children.Add(Pk.Txt(o.Name, 17, true));
        sp.Children.Add(Pk.Small(Kind(o) + "  -  " + Dn.Canonical(o.Dn)));
        string key = o.Sam.Length > 0 ? o.Sam : o.Dn;
        string noun = o.Is("user") ? "user" : o.Is("group") ? "group" : o.Is("computer") ? "computer" : "object";
        if (o.Is("user")) foreach (var a in new[] { "userPrincipalName|User logon name", "displayName|Display name", "department|Department", "title|Job title", "l|City", "description|Description" }) { var p = a.Split('|'); sp.Children.Add(Pk.Pair(p[1], o.Get(p[0]), Narrow)); }
        if (o.Is("computer")) foreach (var a in new[] { "dNSHostName|DNS name", "operatingSystem|Operating system", "description|Description" }) { var p = a.Split('|'); sp.Children.Add(Pk.Pair(p[1], o.Get(p[0]), Narrow)); }
        if (o.Is("group")) { sp.Children.Add(Pk.Pair("Scope", o.Get("groupScope"), Narrow)); sp.Children.Add(Pk.Pair("Type", o.Get("groupCategory"), Narrow)); sp.Children.Add(Pk.Pair("Description", o.Get("description"), Narrow)); }
        var acts = new WrapPanel { Margin = new Thickness(0, 6, 0, 6) };
        if (o.Is("user") || o.Is("computer")) acts.Children.Add(Act(o.Get("enabled") == "FALSE" ? "Enable account" : "Disable account", () => Run("set " + noun + " " + Q(key) + " enabled=" + (o.Get("enabled") == "FALSE" ? "yes" : "no"))));
        if (o.Is("user")) acts.Children.Add(Act("Unlock", () => Run("unlock " + Q(key))));
        acts.Children.Add(Act("Delete", () => { if (Run("delete " + noun + " " + Q(key)).Ok) Sub = ""; Show(); }));
        sp.Children.Add(acts);
        if (o.Is("group"))
        {
            sp.Children.Add(Pk.H("Members"));
            var ms = Lab.Members(d, o, false).OrderBy(x => x.Name).ToList();
            foreach (var m in ms) { string mk = m.Sam.Length > 0 ? m.Sam : m.Name; sp.Children.Add(Pk.Row(Pk.Txt(m.Name + "  (" + Kind(m) + ")", 14), Pk.Link("remove", () => Run("members remove " + Q(key) + " " + Q(mk))))); }
            if (ms.Count == 0) sp.Children.Add(Pk.Small("No members."));
            var add = Pk.Box("", 200); add.Width = 200;
            sp.Children.Add(Pk.Row(add, Act("Add member", () => Run("members add " + Q(key) + " " + Q(Pk.T(add).Trim())))));
        }
        if (o.IsPrincipal)
        {
            sp.Children.Add(Pk.H("Member of"));
            foreach (var g in Lab.GroupsOf(d, o, false).OrderBy(x => x.Name))
            {
                string gk = g.Sam; bool prim = WsText.Eq(g.Name, o.Get("primaryGroup"));
                sp.Children.Add(Pk.Row(Pk.Txt(g.Name + (prim ? "  (primary group)" : ""), 14), prim ? null : Pk.Link("remove", () => Run("members remove " + Q(gk) + " " + Q(key)))));
            }
            var groups = d.OfClass("group").Select(x => x.Name).OrderBy(x => x).ToList();
            var cb = Pk.First(Pk.Combo(groups, "", 220));
            sp.Children.Add(Pk.Row(cb, Act("Add to group", () => Run("members add " + Q(Pk.Val(cb)) + " " + Q(key)))));
        }
        if (o.Is("user"))
        {
            sp.Children.Add(Pk.H("Reset password"));
            var pw = Pk.Box("", 200); pw.Width = 200;
            var must = Pk.Check("must change at next logon", true);
            sp.Children.Add(Pk.Row(pw, must, Act("Reset", () => Run("password " + Q(key) + " " + Q(Pk.T(pw)) + " mustchange=" + (must.IsChecked == true ? "yes" : "no")))));
        }
        sp.Children.Add(Pk.H("Move to"));
        var ous = d.Live.Where(x => x.Is("organizationalUnit") || x.Is("container") && (x.Name == "Users" || x.Name == "Computers")).Select(x => Dn.Canonical(x.Dn)).OrderBy(x => x).ToList();
        var to = Pk.First(Pk.Combo(ous, "", 260));
        sp.Children.Add(Pk.Row(to, Act("Move", () => { var target = d.Live.FirstOrDefault(x => Dn.Canonical(x.Dn) == Pk.Val(to)); if (target != null && Run("move " + noun + " " + Q(key) + " " + Q(target.Dn)).Ok) { Node = target.Dn; Sub = Dn.Rdn(o.Dn) + "," + target.Dn; Show(); } })));
        return sp;
    }
}
