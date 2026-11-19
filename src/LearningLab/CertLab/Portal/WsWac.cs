// Cert Lab - Windows Admin Center (practice; App Hub's UiWsWac / UiWsWac2), the gateway on SRV1: its connections in the
// tree, and a server's tools on the page (overview, roles and features, services, files and sharing with NTFS and
// effective access, storage with pools and deduplication, virtual machines and switches, DHCP, Azure hybrid services).
// Cert Lab's own drawing; every tap is a console line (feature install, service start, share new, ntfs grant, vm set...).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using LearningLab.Views;
using CertLab;

namespace LearningLab.CertLab.Portal;

public partial class WsWorkspace
{
    public WsForm WacForm;
    static readonly string[][] WacTools = { new[] { "overview", "Overview" }, new[] { "roles", "Roles & features" }, new[] { "services", "Services" }, new[] { "files", "Files & file sharing" }, new[] { "storage", "Storage" }, new[] { "vms", "Virtual machines" }, new[] { "switches", "Virtual switches" }, new[] { "dhcp", "DHCP" }, new[] { "azure", "Azure hybrid services" } };

    Control BuildWac()
    {
        var z = Lab.Azure;
        if (!z.WacConnections.Contains(Machine, StringComparer.OrdinalIgnoreCase)) Machine = z.WacConnections.FirstOrDefault() ?? "SRV1";
        var tree = new StackPanel();
        tree.Children.Add(Pk.Small("Windows Admin Center on srv1.corp.example"));
        foreach (var n in z.WacConnections) { string m = n; tree.Children.Add(TreeItem(n + (Lab.Server(n).Running ? "" : "  (off)"), 0, WsText.Eq(Machine, n), IcServer, () => { Machine = m; Sub = ""; WacForm = null; Show(); })); }
        var add = Pk.Box("", 130); add.Width = 130;
        tree.Children.Add(Pk.Row(add, Act("Add", () => Run("wac add " + Q(Pk.T(add).Trim())))));
        if (!z.WacRegistered) tree.Children.Add(Act("Register with Azure", () => Run("wac register")));
        var page = new StackPanel();
        var s = Lab.Server(Machine);
        if (Sub.Length == 0 || Sub == "dhcp" && s.Dhcp == null || (Sub == "vms" || Sub == "switches") && s.Hv == null) Sub = "overview";
        page.Children.Add(Title(s.M.Fqdn, Sim.Status(s.Name)));
        var tabs = new StackPanel();
        foreach (var t in WacTools)
        {
            string id = t[0];
            if ((id == "vms" || id == "switches") && s.Hv == null) continue;
            if (id == "dhcp" && s.Dhcp == null) continue;
            var b = Act(t[1], () => { Sub = id; WacForm = null; Show(); });
            Pk.Picked(b, Sub == id);
            tabs.Children.Add(b);
        }
        var strip = Pk.Strip(tabs); strip.Margin = new Thickness(0, 0, 0, 8);
        page.Children.Add(strip);
        if (!s.Running) { page.Children.Add(Pk.Error("The server isn't running.")); return TwoPane(tree, page); }
        switch (Sub)
        {
            case "roles": WacRoles(page, s); break;
            case "services": WacServices(page, s); break;
            case "files": WacFiles(page, s); break;
            case "storage": WacStorage(page, s); break;
            case "vms": WacVms(page, s); break;
            case "switches": WacSwitches(page, s); break;
            case "dhcp": WacDhcp(page, s); break;
            case "azure": WacAzure(page, s); break;
            default:
                foreach (var p in new[] { new[] { "Operating system", s.M.OsCaption }, new[] { "Installation type", s.Kind == "core" ? "Server Core" : s.IsClient ? "Client" : "Server with Desktop Experience" }, new[] { "Domain", s.M.Domain }, new[] { "IP address", s.M.Ip }, new[] { "Processors", s.M.Cpus.ToString() }, new[] { "Memory", (s.M.MemoryBytes >> 30) + " GB" }, new[] { "Azure Arc", Lab.Azure.ArcStatus(s.Name) } }) page.Children.Add(Pk.Pair(p[0], p[1], Narrow));
                page.Children.Add(Bar(Act("Restart", () => { Sim.Reboot(s.Name); Say(s.Name + " restarted.", false); Show(); })));
                break;
        }
        return TwoPane(tree, page);
    }

    /// <summary>A line of a list: the name, a status badge and a button; wraps on a phone.</summary>
    Control StateLine(string name, string badge, Control action)
    {
        var t = Pk.Txt(name, 14);
        if (!Narrow) t.Width = 280;
        var row = Pk.Row(t, Pk.Badge(badge), action);
        row.Margin = new Thickness(0, 0, 0, 2);
        return new Border { Child = row, BorderBrush = Pk.B(Ui.BorderC), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 2) };
    }

    void WacRoles(StackPanel page, WsServer s)
    {
        if (s.IsClient) { page.Children.Add(Pk.Small("Roles and features are on servers.")); return; }
        var interesting = new[] { "AD-Domain-Services", "DNS", "DHCP", "Hyper-V", "FS-FileServer", "FS-DFS-Namespace", "FS-DFS-Replication", "FS-Data-Deduplication", "Containers", "GPMC", "RSAT-AD-PowerShell", "RSAT-DNS-Server", "RSAT-DHCP", "Web-Server", "Windows-Server-Backup" };
        foreach (var n in interesting)
        {
            var f = s.M.FindFeature(n); if (f == null) continue; string fn = f.Name;
            page.Children.Add(StateLine(f.Display, f.State == "Installed" ? "Installed" : f.State == "InstallPending" ? "Pending restart" : "Available",
                f.State == "Available" ? Act("Install", () => Run("feature install " + s.Name + " " + fn)) : null));
        }
    }

    void WacServices(StackPanel page, WsServer s)
    {
        var names = new[] { "NTDS", "DNS", "DHCPServer", "vmms", "DFSR", "Dfs", "ddpsvc", "himds", "ADSync", "AzureADConnectAuthenticationAgent", "FileSyncSvc", "ServerManagementGateway", "WinRM", "LanmanServer", "W32Time" };
        foreach (var n in names)
        {
            var sv = s.M.Svc(n); if (sv == null) continue; string sn = sv.Name;
            page.Children.Add(StateLine(sv.DisplayName, sv.Status == "Running" ? "On" : "Off", Act(sv.Running ? "Stop" : "Start", () => Run("service " + (sv.Running ? "stop " : "start ") + s.Name + " " + sn))));
        }
    }

    void WacFiles(StackPanel page, WsServer s)
    {
        page.Children.Add(Pk.H("Shares"));
        var shares = s.Shares.Where(x => !x.Special).OrderBy(x => x.Name).ToList();
        page.Children.Add(Pk.Table(new[] { "Share", "Path", "Share permissions" }, shares.Select(x => new[] { x.Name, x.Path, string.Join("; ", x.Access.Select(a => a.Trustee + ": " + (a.Allow ? "" : "Deny ") + a.Right)) + (x.FolderEnumerationMode == "AccessBased" ? "   (ABE)" : "") }), null, Narrow, "No shares."));
        var f = F("New share"); f.Text("name", "Share name", ""); f.Text("path", "Folder", "D:\\Shares\\"); f.Text("access", "Permissions (who:right;...)", "Everyone:Read"); f.Check("abe", "Access-based enumeration", false); f.Check("encrypt", "Encrypt data (SMB 3)", false);
        f.Submit("Share", () => Run("share new server=" + s.Name + " " + f.Pairs("name", "path", "access") + " abe=" + f.V("abe") + " encrypt=" + f.V("encrypt")));
        WacForm = f;
        page.Children.Add(Pk.Card(f.Panel));
        page.Children.Add(Pk.H("Permissions and effective access"));
        var path = Pk.Box(Node.Length > 0 && Node.Contains(":\\") ? Node : "D:\\Shares\\Sales", 260); path.Width = 260;
        page.Children.Add(Pk.Row(path, Act("Show", () => { Node = Pk.T(path).Trim(); Show(); })));
        var file = s.M.FindFile(Node.Contains(":\\") ? Node : "D:\\Shares\\Sales");
        if (file == null) return;
        string fp = file.FullPath;
        var acl = s.AclOf(file, false);
        page.Children.Add(Pk.Small(fp + (acl != null && acl.Protected ? "   (inheritance disabled)" : "   (inherits from the parent)")));
        page.Children.Add(Pk.Table(new[] { "Principal", "Type", "Access", "Inherited" }, s.EffectiveAces(file).Select(a => new[] { a.Trustee, a.Allow ? "Allow" : "Deny", NtfsRights.Words(a.Rights), a.Inherited ? "Yes" : "No" }), null, Narrow, "No entries."));
        var who = Pk.Box("CORP\\", 170); who.Width = 170; var rights = Pk.Combo(new[] { "Modify", "ReadAndExecute", "Read", "Write", "FullControl" }, "Modify", 150);
        page.Children.Add(Pk.Row(who, rights, Act("Allow", () => Run("ntfs grant server=" + s.Name + " path=" + Q(fp) + " account=" + Q(Pk.T(who).Trim()) + " rights=" + Pk.Val(rights))), Act("Deny", () => Run("ntfs deny server=" + s.Name + " path=" + Q(fp) + " account=" + Q(Pk.T(who).Trim()) + " rights=" + Pk.Val(rights))), Act("Remove", () => Run("ntfs remove server=" + s.Name + " path=" + Q(fp) + " account=" + Q(Pk.T(who).Trim())))));
        page.Children.Add(Bar(Act("Disable inheritance (convert)", () => Run("ntfs inheritance server=" + s.Name + " path=" + Q(fp) + " convert")), Act("Disable inheritance (remove)", () => Run("ntfs inheritance server=" + s.Name + " path=" + Q(fp) + " remove")), Act("Enable inheritance", () => Run("ntfs inheritance server=" + s.Name + " path=" + Q(fp) + " enable"))));
        var user = Pk.Box("bcole", 130); user.Width = 130;
        page.Children.Add(Pk.Row(Pk.Txt("Effective access for", 14), user, Act("Check", () =>
        {
            var share = s.Shares.FirstOrDefault(x => !x.Special && fp.StartsWith(x.Path, StringComparison.OrdinalIgnoreCase));
            string u = Pk.T(user).Trim();
            var r = Lab.Access(u, share != null ? "\\\\" + s.Name + "\\" + share.Name + fp.Substring(share.Path.Length) : s.Name + ":" + fp);
            Say(r.Error ?? (u + ": " + NtfsRights.Words(r.Effective) + (r.Share >= 0 ? " over \\\\" + s.Name + "\\" + share.Name + " (NTFS " + NtfsRights.Words(r.Ntfs) + ", share " + NtfsRights.Words(r.Share) + ")" : " at the console")), r.Error != null);
        })));
    }

    void WacStorage(StackPanel page, WsServer s)
    {
        page.Children.Add(Pk.H("Disks"));
        page.Children.Add(Pk.Table(new[] { "Disk", "Name", "Size", "Pool / use" }, s.Disks.OrderBy(d => d.Number).Select(d => new[] { d.Number.ToString(), d.FriendlyName, WsServer.Gb(d.Size), d.Pool.Length > 0 ? "in pool " + d.Pool : d.CanPool ? "can be pooled" : string.Join(" ", d.Parts.Where(p => p.Letter.Length > 0).Select(p => p.Letter + ":")) }), null, Narrow, ""));
        page.Children.Add(Pk.H("Volumes"));
        page.Children.Add(Pk.Table(new[] { "Volume", "Label", "File system", "Size", "Deduplication" }, s.Disks.SelectMany(d => d.Parts).Where(p => p.Letter.Length > 0).OrderBy(p => p.Letter).Select(p => { s.Dedup.TryGetValue(p.Letter, out var dd); return new[] { p.Letter + ":", p.Label, p.FileSystem, WsServer.Gb(p.Size), dd == null ? "Off" : dd.Enabled ? "On (saved " + WsServer.Gb(dd.Saved) + ")" : "Off" }; }), null, Narrow, ""));
        if (s.Pools.Count > 0) page.Children.Add(Pk.Table(new[] { "Storage pool", "Disks", "Size", "Used" }, s.Pools.Select(p => new[] { p.Name, p.Disks.Count.ToString(), WsServer.Gb(s.PoolSize(p)), WsServer.Gb(s.PoolUsed(p)) }), null, Narrow, ""));
        var f = F("New storage pool"); f.Text("name", "Name", "Pool1"); f.Text("disks", "Disks (numbers)", string.Join(",", s.Disks.Where(d => d.CanPool).Select(d => d.Number.ToString())));
        f.Submit("Create pool", () => Run("storage pool server=" + s.Name + " " + f.Pairs("name", "disks")));
        page.Children.Add(Pk.Card(f.Panel));
        if (s.Pools.Count > 0)
        {
            var v = F("New volume on a pool"); v.Choice("pool", "Pool", s.Pools.Select(p => p.Name), s.Pools[0].Name); v.Text("name", "Name", ""); v.Choice("resiliency", "Resiliency", new[] { "Simple", "Mirror", "Parity" }, "Mirror"); v.Choice("copies", "Copies (mirror)", new[] { "2", "3" }, "2");
            v.Choice("provisioning", "Provisioning", new[] { "Fixed", "Thin" }, "Fixed"); v.Text("sizegb", "Size (GB)", "50"); v.Text("letter", "Drive letter", s.NextLetter() ?? ""); v.Choice("fs", "File system", new[] { "ReFS", "NTFS" }, "ReFS");
            v.Submit("Create volume", () => Run("storage vdisk server=" + s.Name + " " + v.Pairs("pool", "name", "resiliency", "copies", "provisioning", "sizegb", "letter", "fs")));
            page.Children.Add(Pk.Card(v.Panel));
        }
        var dvol = Pk.Box("E:", 80); dvol.Width = 80; var usage = Pk.Combo(new[] { "Default", "HyperV", "Backup" }, "Default", 130);
        page.Children.Add(Pk.Row(Pk.Txt("Data Deduplication on", 14), dvol, usage, Act("Enable", () => Run("storage dedup server=" + s.Name + " " + Pk.T(dvol).Trim() + " usage=" + Pk.Val(usage))), Act("Optimize now", () => Run("storage optimize server=" + s.Name + " " + Pk.T(dvol).Trim()))));
    }

    void WacVms(StackPanel page, WsServer s)
    {
        var h = s.Hv;
        var vms = h.Vms.OrderBy(v => v.Name).ToList();
        page.Children.Add(Pk.Table(new[] { "Name", "State", "Generation", "Memory", "Processors", "Switch" }, vms.Select(v => new[] { v.Name, v.State, v.Generation.ToString(), (v.Memory >> 20) + " MB" + (v.Dynamic ? " (dynamic)" : ""), v.Cpus.ToString(), v.Nics.Count > 0 ? v.Nics[0].Switch : "" }), i => { Node = vms[i].Name; Show(); }, Narrow, "No virtual machines."));
        var vm = h.Vm(Node);
        if (vm != null)
        {
            string n = Q(vm.Name);
            var sp = new StackPanel();
            sp.Children.Add(Pk.Txt(vm.Name + "  -  " + vm.State, 16, true));
            sp.Children.Add(Bar(Act("Start", () => Run("vm start " + n)), Act("Shut down", () => Run("vm stop " + n)), Act("Turn off", () => Run("vm stop " + n + " turnoff=yes")), Act("Save", () => Run("vm stop " + n + " save=yes")), Act("Delete", () => Run("vm delete " + n))));
            sp.Children.Add(Pk.Pair("Nested virtualization", vm.Nested ? "On" : "Off", Narrow)); sp.Children.Add(Pk.Pair("MAC address spoofing", vm.Nics.Count > 0 ? vm.Nics[0].Spoofing : "", Narrow)); sp.Children.Add(Pk.Pair("Checkpoint type", vm.CheckpointType, Narrow));
            var f = F("Settings"); f.Text("cpus", "Processors", vm.Cpus.ToString()); f.Text("memorygb", "Startup memory (GB)", (vm.Memory >> 30).ToString()); f.Check("dynamic", "Dynamic memory", vm.Dynamic); f.Check("nested", "Enable nested virtualization", vm.Nested); f.Check("spoofing", "MAC address spoofing", vm.Nics.Count > 0 && vm.Nics[0].Spoofing == "On");
            f.Choice("switch", "Virtual switch", h.Switches.Select(x => x.Name), vm.Nics.Count > 0 ? vm.Nics[0].Switch : "");
            f.Submit("Save", () => Run("vm set " + n + " " + f.Pairs("cpus", "memorygb") + " dynamic=" + f.V("dynamic") + " nested=" + f.V("nested") + " spoofing=" + f.V("spoofing") + (f.V("switch").Length > 0 ? " switch=" + Q(f.V("switch")) : "")));
            WacForm = f;
            sp.Children.Add(f.Panel);
            sp.Children.Add(Pk.H("Checkpoints"));
            foreach (var c in vm.Checkpoints) { string cn = Q(c.Name); sp.Children.Add(Pk.Row(Pk.Txt(c.Name + "  (" + c.Type + ", " + WsText.Date(c.Created) + ")" + (c.Name == vm.Current ? "  <- now" : ""), 14), Pk.Link("apply", () => Run("vm restore " + n + " " + cn)), Pk.Link("delete", () => Run("vm deletecheckpoint " + n + " " + cn)))); }
            var cname = Pk.Box("", 180); cname.Width = 180;
            sp.Children.Add(Pk.Row(cname, Act("Checkpoint", () => Run("vm checkpoint " + n + (Pk.T(cname).Trim().Length > 0 ? " " + Q(Pk.T(cname).Trim()) : "")))));
            page.Children.Add(Pk.Card(sp));
        }
        var nf = F("New virtual machine"); nf.Text("name", "Name", ""); nf.Choice("generation", "Generation", new[] { "2", "1" }, "2"); nf.Text("memorygb", "Memory (GB)", "2"); nf.Text("disksizegb", "New disk (GB)", "60"); nf.Choice("switch", "Virtual switch", h.Switches.Select(x => x.Name), "LAN");
        nf.Submit("Create", () => Run("vm new " + nf.Pairs("name", "generation", "memorygb", "disksizegb", "switch")));
        if (vm == null) WacForm = nf;
        page.Children.Add(Pk.Card(nf.Panel));
    }

    void WacSwitches(StackPanel page, WsServer s)
    {
        var h = s.Hv;
        page.Children.Add(Pk.Table(new[] { "Name", "Type", "Network adapter", "Shared with host" }, h.Switches.Select(x => new[] { x.Name, x.Type, x.Adapter, x.ManagementOs ? "Yes" : "No" }), null, Narrow, "No virtual switches."));
        var f = F("New virtual switch"); f.Text("name", "Name", ""); f.Choice("type", "Type", new[] { "External", "Internal", "Private" }, "Internal"); f.Choice("adapter", "Network adapter (external)", s.M.Adapters.Where(a => !a.Loopback).Select(a => a.Name), "Ethernet 2");
        f.Submit("Create", () => Run("vswitch new name=" + Q(f.V("name")) + " type=" + f.V("type") + (f.V("type") == "External" ? " adapter=" + Q(f.V("adapter")) : "")));
        WacForm = f;
        page.Children.Add(Pk.Card(f.Panel));
    }

    void WacDhcp(StackPanel page, WsServer s)
    {
        var d = s.Dhcp;
        page.Children.Add(Pk.Pair("Authorized in Active Directory", d.Authorized ? "Yes" : "No", Narrow));
        if (!d.Authorized) page.Children.Add(Act("Authorize", () => Run("dhcp authorize server=" + s.Name)));
        foreach (var sc in d.Scopes.OrderBy(x => x.ScopeId))
        {
            var sp = new StackPanel(); string id = sc.ScopeId;
            sp.Children.Add(Pk.Txt("Scope " + sc.ScopeId + "  " + sc.Name + "  (" + sc.State + ")" + (sc.Failover.Length > 0 ? "  failover: " + sc.Failover : ""), 15, true));
            sp.Children.Add(Pk.Small(sc.Start + " - " + sc.End + "   lease " + sc.Lease.TotalDays + " days   router " + d.Option(sc, 3) + "   DNS " + d.Option(sc, 6) + "   domain " + d.Option(sc, 15)));
            sp.Children.Add(Pk.Table(new[] { "Address", "Client", "Kind" }, sc.Leases.OrderBy(l => l.Ip).Select(l => new[] { l.Ip, l.Host + " (" + l.Mac + ")", l.Kind }), null, Narrow, "No leases."));
            var rip = Pk.Box("", 130); rip.Width = 130; var rmac = Pk.Box("", 170); rmac.Width = 170;
            sp.Children.Add(Pk.Row(Pk.Txt("Reserve", 14), rip, rmac, Act("Add reservation", () => Run("dhcp scope reserve server=" + s.Name + " id=" + id + " ip=" + Pk.T(rip).Trim() + " mac=" + Pk.T(rmac).Trim()))));
            sp.Children.Add(Bar(Act(sc.State == "Active" ? "Deactivate" : "Activate", () => Run("dhcp scope set server=" + s.Name + " id=" + id + " active=" + (sc.State == "Active" ? "no" : "yes")))));
            page.Children.Add(Pk.Card(sp));
        }
        var f = F("New scope"); f.Text("name", "Name", ""); f.Text("start", "Start address", ""); f.Text("end", "End address", ""); f.Text("mask", "Subnet mask", "255.255.255.0"); f.Text("days", "Lease (days)", "8");
        f.Submit("Create", () => Run("dhcp scope add server=" + s.Name + " " + f.Pairs("name", "start", "end", "mask", "days")));
        WacForm = f;
        page.Children.Add(Pk.Card(f.Panel));
        var fo = F("Configure failover"); fo.Choice("partner", "Partner server", Lab.Servers.Values.Where(x => x.Dhcp != null && x != s).Select(x => x.Name), ""); fo.Text("scopes", "Scopes", string.Join(",", d.Scopes.Where(x => x.Failover.Length == 0).Select(x => x.ScopeId)));
        fo.Choice("mode", "Mode", new[] { "LoadBalance", "HotStandby" }, "LoadBalance"); fo.Text("percent", "Load balance %", "50"); fo.Text("secret", "Shared secret", "");
        fo.Submit("Create", () => Run("dhcp failover add server=" + s.Name + " name=" + Q(s.Name + "-" + fo.V("partner")) + " " + fo.Pairs("partner", "scopes", "mode", "percent", "secret")));
        page.Children.Add(Pk.Card(fo.Panel));
    }

    void WacAzure(StackPanel page, WsServer s)
    {
        var z = Lab.Azure;
        page.Children.Add(Pk.Pair("Azure Arc", z.ArcStatus(s.Name), Narrow));
        if (!z.Arc.ContainsKey(s.Name)) page.Children.Add(Act("Onboard to Azure Arc", () => Run("arc onboard " + s.Name)));
        page.Children.Add(Pk.Pair("Azure Network Adapter", z.VnetReach.Contains(s.Name) ? (z.Reaches(s.Name) ? "Connected to " + z.Vnet : "Waiting for the VPN gateway") : "Not set up", Narrow));
        if (!z.VnetReach.Contains(s.Name)) page.Children.Add(Act("Add Azure Network Adapter", () => Run("azure netadapter " + s.Name)));
        page.Children.Add(Pk.Pair("Azure File Sync", z.SyncRegistered.Contains(s.Name) ? "Registered with " + z.SyncService : "Not registered", Narrow));
        if (!z.SyncRegistered.Contains(s.Name)) page.Children.Add(Act("Install the agent and register", () => Run("filesync register " + s.Name)));
        page.Children.Add(Pk.Small(z.WacRegistered ? "This gateway is registered with Azure (" + z.Subscription + ")." : "Register Windows Admin Center with Azure first (in the list of connections)."));
    }
}
