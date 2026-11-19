// Cert Lab - the Azure portal (practice) for the AZ-800 course (App Hub's UiWsAzure): Azure Arc servers, Update Manager,
// the Windows Server VMs, the virtual network, Microsoft Entra Connect / Cloud Sync and the synced users, Storage Sync
// Services. A pretend subscription; nothing signs in anywhere. Every tap is a console line (arc ..., connect ...).
using Avalonia.Controls;
using CertLab;

namespace LearningLab.CertLab.Portal;

public partial class WsWorkspace
{
    static readonly string[][] Blades = { new[] { "arc", "Azure Arc: servers" }, new[] { "updates", "Azure Update Manager" }, new[] { "vms", "Virtual machines" }, new[] { "vnet", "Virtual network" }, new[] { "connect", "Microsoft Entra Connect" }, new[] { "users", "Microsoft Entra ID: users" }, new[] { "filesync", "Storage Sync Services" } };
    public WsForm AzForm;

    Control BuildAzure()
    {
        var z = Lab.Azure;
        if (Node.Length == 0 || !Blades.Any(b => b[0] == Node)) Node = "arc";
        var tree = new StackPanel();
        tree.Children.Add(Pk.Small("Subscription: " + z.Subscription));
        foreach (var b in Blades) { string id = b[0]; tree.Children.Add(TreeItem(b[1], 0, Node == id, IcCloud, () => { Node = id; Sub = ""; AzForm = null; Show(); })); }
        var page = new StackPanel();
        page.Children.Add(Title(Blades.First(x => x[0] == Node)[1], "Azure portal (practice)"));
        switch (Node)
        {
            case "arc":
                {
                    page.Children.Add(Pk.Table(new[] { "Server", "Status", "Resource group", "Region", "Extensions" }, z.Arc.Values.Select(a => new[] { a.Name, z.ArcStatus(a.Name), a.ResourceGroup, a.Region, string.Join(", ", a.Extensions) }), null, Narrow, "No Azure Arc-enabled servers yet."));
                    var srv = Pk.Combo(Lab.Servers.Values.Where(x => !x.IsClient && !z.Arc.ContainsKey(x.Name)).Select(x => x.Name), "", 140);
                    if (srv.SelectedIndex < 0 && srv.Items.Count > 0) srv.SelectedIndex = 0;
                    page.Children.Add(Pk.Row(Pk.Txt("Add a server", 14), srv, Act("Generate script and onboard", () => Run("arc onboard " + Pk.Val(srv) + " rg=" + z.ResourceGroup))));
                    var es = Pk.Combo(z.Arc.Keys, "", 140); var en = Pk.Combo(new[] { "AzureMonitorWindowsAgent", "WindowsAdminCenter", "MicrosoftDefenderForServers", "CustomScriptExtension" }, "AzureMonitorWindowsAgent", 240);
                    if (es.SelectedIndex < 0 && es.Items.Count > 0) es.SelectedIndex = 0;
                    page.Children.Add(Pk.Row(Pk.Txt("Extension", 14), es, en, Act("Install", () => Run("arc extension " + Pk.Val(es) + " " + Pk.Val(en)))));
                    break;
                }
            case "updates":
                {
                    var machines = z.Arc.Keys.Concat(z.Vms.Select(v => v.Name)).ToList();
                    page.Children.Add(Pk.Table(new[] { "Machine", "Assessed", "Missing updates" }, machines.Select(m => { z.Missing.TryGetValue(m, out var l); return new[] { m, z.Assessed.ContainsKey(m) ? WsText.Date(z.Assessed[m]) : "never", z.Assessed.ContainsKey(m) && l != null ? string.Join(", ", l.Select(u => u.Kb + " (" + u.Classification + ")")) : "" }; }), null, Narrow, "Only Azure VMs and Azure Arc-enabled servers appear here."));
                    var um = Pk.Combo(machines, "", 140); var cls = Pk.Combo(new[] { "", "Security", "Critical", "Update rollups" }, "", 150);
                    if (um.SelectedIndex < 0 && um.Items.Count > 0) um.SelectedIndex = 0;
                    Pk.FirstLabel(cls, "All classifications");
                    page.Children.Add(Pk.Row(um, Act("Check for updates", () => Run("update assess " + Pk.Val(um))), cls, Act("One-time update", () => Run("update install " + Pk.Val(um) + (Pk.Val(cls).Length > 0 ? " classes=" + Q(Pk.Val(cls)) : "")))));
                    break;
                }
            case "vms":
                foreach (var vm in z.Vms)
                {
                    var sp = new StackPanel(); string vn = vm.Name;
                    sp.Children.Add(Pk.Txt(vm.Name + "  -  " + vm.State, 15, true));
                    sp.Children.Add(Pk.Small(vm.Os + ", " + vm.Size + ", " + z.Vnet + "/" + vm.Subnet));
                    sp.Children.Add(Pk.Pair("Private IP", vm.PrivateIp + " (" + vm.IpAllocation + ")", Narrow));
                    sp.Children.Add(Pk.Pair("Data disks", string.Join(", ", vm.DataDisks.Select(dd => dd.Name + " " + dd.SizeGb + " GB, caching " + dd.Caching)), Narrow));
                    sp.Children.Add(Bar(Act(vm.IpAllocation == "Static" ? "Make the IP dynamic" : "Make the IP static", () => Run("azure vm " + vn + " ipallocation=" + (vm.IpAllocation == "Static" ? "Dynamic" : "Static")))));
                    var dn = Pk.Box(vn.ToLowerInvariant() + "-data", 170); dn.Width = 170; var dc = Pk.Combo(new[] { "None", "ReadOnly", "ReadWrite" }, "None", 130);
                    sp.Children.Add(Pk.Row(Pk.Txt("Attach a data disk", 14), dn, dc, Act("Attach", () => Run("azure vm " + vn + " disk name=" + Q(Pk.T(dn).Trim()) + " size=32 caching=" + Pk.Val(dc)))));
                    page.Children.Add(Pk.Card(sp));
                }
                break;
            case "vnet":
                {
                    page.Children.Add(Pk.Pair("Address space", z.VnetSpace, Narrow));
                    page.Children.Add(Pk.Pair("Site-to-site VPN to the offices", z.SiteVpn ? "Connected" : "Not connected", Narrow));
                    page.Children.Add(Pk.Pair("VPN gateway (point-to-site)", z.GatewayReady.HasValue ? (z.GatewayReady.Value <= Lab.Now ? "Ready" : "Being created, ready at " + WsText.Date(z.GatewayReady.Value)) : "None", Narrow));
                    page.Children.Add(Pk.Pair("DNS servers", z.VnetDns.Count == 0 ? "Default (Azure-provided)" : string.Join(", ", z.VnetDns), Narrow));
                    var dnsBox = Pk.Box(string.Join(",", z.VnetDns), 220); dnsBox.Width = 220;
                    page.Children.Add(Pk.Row(dnsBox, Act("Save DNS servers", () => Run("azure vnetdns " + (Pk.T(dnsBox).Trim().Length == 0 ? "default" : Q(Pk.T(dnsBox).Replace(" ", "")))))));
                    page.Children.Add(Bar(Act(z.SiteVpn ? "Disconnect the site-to-site VPN" : "Connect the site-to-site VPN", () => Run("azure vpn " + (z.SiteVpn ? "off" : "on")))));
                    break;
                }
            case "connect":
                page.Children.Add(Pk.Pair("Sync engine", z.CloudSync ? "Microsoft Entra Cloud Sync (agents: " + string.Join(", ", z.CloudAgents) + ")" : z.ConnectServer.Length > 0 ? "Microsoft Entra Connect Sync on " + z.ConnectServer : "Not installed", Narrow));
                page.Children.Add(Pk.Pair("User sign-in", z.ConnectServer.Length == 0 && !z.CloudSync ? "" : z.SignIn + (z.Sso ? ", seamless single sign-on" : ""), Narrow));
                page.Children.Add(Pk.Pair("OU filtering", z.OuFilter.Count == 0 ? "All OUs" : string.Join("; ", z.OuFilter), Narrow));
                page.Children.Add(Pk.Pair("Last sync", z.LastSync.HasValue ? WsText.Date(z.LastSync.Value) : "never", Narrow));
                if (z.ConnectServer.Length == 0 && !z.CloudSync)
                {
                    var f = F("Install Microsoft Entra Connect Sync (practice)"); f.Choice("server", "Server", Lab.Servers.Values.Where(x => !x.IsClient).Select(x => x.Name), "SRV1"); f.Choice("signin", "User sign-in", new[] { "PHS", "PTA", "Federation" }, "PHS"); f.Check("sso", "Enable single sign-on", true); f.Text("ous", "Sync these OUs (blank: all)", "OU=Staff," + Lab.DomainDn);
                    f.Submit("Install", () => Run("connect install " + f.Pairs("server", "signin", "ous") + " sso=" + f.V("sso")));
                    AzForm = f; page.Children.Add(Pk.Card(f.Panel));
                }
                else page.Children.Add(Bar(Act("Run a sync cycle now", () => Run("connect sync"))));
                break;
            case "users":
                page.Children.Add(Pk.Small("Verified domains: " + string.Join(", ", z.VerifiedDomains) + " (and the initial domain " + z.TenantDomain + ")"));
                page.Children.Add(Pk.Table(new[] { "User", "User principal name", "Department", "Source" }, z.CloudUsers.Values.OrderBy(u => u.Sam).Select(u => new[] { u.Sam, u.Upn, u.Department, u.Source }), null, Narrow, "No synced users yet."));
                break;
            case "filesync":
                page.Children.Add(Pk.Pair("Storage Sync Service", z.SyncService.Length == 0 ? "None" : z.SyncService, Narrow));
                page.Children.Add(Pk.Pair("Registered servers", string.Join(", ", z.SyncRegistered), Narrow));
                foreach (var g in z.SyncGroups) page.Children.Add(Pk.Card(Pk.Col(Pk.Txt(g.Name + "  (cloud endpoint " + g.StorageAccount + "/" + g.FileShare + ")", 15, true), Pk.Table(new[] { "Server", "Path", "Cloud tiering" }, g.Servers.Select(x => new[] { x.Server, x.Path, x.Tiering ? "On, files not used for " + x.TierDays + " days, keep " + x.FreeSpacePercent + "% free" : "Off" }), null, Narrow, "No server endpoints."))));
                if (z.SyncService.Length == 0) { var nm = Pk.Box("sss-harbor", 170); nm.Width = 170; page.Children.Add(Pk.Row(nm, Act("Create Storage Sync Service", () => Run("filesync service name=" + Q(Pk.T(nm).Trim()))))); }
                else
                {
                    var gf = F("New sync group"); gf.Text("name", "Name", ""); gf.Choice("account", "Storage account", z.StorageAccounts.Keys, "stharborfiles"); gf.Choice("share", "Azure file share", z.StorageAccounts.Values.First().Split(','), "salesarchive");
                    gf.Submit("Create", () => Run("filesync group " + gf.Pairs("name", "account", "share")));
                    AzForm = gf;
                    page.Children.Add(Pk.Card(gf.Panel));
                    if (z.SyncGroups.Count > 0)
                    {
                        var ef = F("Add server endpoint"); ef.Choice("group", "Sync group", z.SyncGroups.Select(x => x.Name), z.SyncGroups[0].Name); ef.Choice("server", "Registered server", z.SyncRegistered, ""); ef.Text("path", "Path", "E:\\Archive"); ef.Check("tiering", "Cloud tiering", true); ef.Text("days", "Tier files not used for (days)", "60"); ef.Text("free", "Volume free space %", "20");
                        ef.Submit("Create", () => Run("filesync endpoint " + ef.Pairs("group", "server", "path", "days", "free") + " tiering=" + ef.V("tiering")));
                        page.Children.Add(Pk.Card(ef.Panel));
                    }
                }
                break;
        }
        return TwoPane(tree, page);
    }
}
