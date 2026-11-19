// Cert Lab - DNS Manager (practice; App Hub's UiWsDns): the DNS servers with their forward and reverse lookup zones and
// conditional forwarders in the tree; a server's forwarders, recursion and the New Zone form; a zone's settings
// (dynamic updates, zone transfers, DNSSEC) and records with the New Record form. Every tap is a "dns ..." console line.
using Avalonia;
using Avalonia.Controls;
using CertLab;

namespace LearningLab.CertLab.Portal;

public partial class WsWorkspace
{
    public WsForm DnsForm;

    Control BuildDns()
    {
        var servers = Lab.Servers.Values.Where(s => s.Dns != null).Select(s => s.Name).ToList();
        if (servers.Count == 0) return Pk.Small("No server runs the DNS Server role.");
        if (!servers.Contains(Machine, StringComparer.OrdinalIgnoreCase)) { Machine = servers[0]; Node = ""; }
        var tree = new StackPanel();
        foreach (var sn in servers)
        {
            string s0 = sn; var ds = Lab.DnsAt(sn);
            tree.Children.Add(TreeItem(sn + (ds.Up ? "" : "  (not running)"), 0, WsText.Eq(Machine, sn) && Node == "", IcServer, () => { Machine = s0; Node = ""; DnsForm = null; Show(); }));
            if (!WsText.Eq(Machine, sn)) continue;
            foreach (var grp in new[] { "Forward Lookup Zones", "Reverse Lookup Zones", "Conditional Forwarders" })
            {
                tree.Children.Add(TreeItem(grp, 1, false, IcFolder, null));
                foreach (var z in ds.Zones.Where(z => grp[0] == 'C' ? z.Type == "Forwarder" : z.Type != "Forwarder" && z.Reverse == (grp[0] == 'R')).OrderBy(z => z.Name))
                { string key = "zone:" + z.Name; tree.Children.Add(TreeItem(z.Name, 2, Node == key, z.Type == "Forwarder" ? IcLink : IcFolder, () => { Node = key; Sub = ""; DnsForm = null; Show(); })); }
            }
        }
        var page = new StackPanel();
        var srv = Lab.DnsAt(Machine);
        if (Node.StartsWith("zone:", StringComparison.Ordinal)) { var z = srv.Zone(Node.Substring(5)); page.Children.Add(z == null ? Pk.Small("The zone is gone.") : ZonePage(srv, z)); }
        else page.Children.Add(DnsServerPage(srv));
        return TwoPane(tree, page);
    }

    Control DnsServerPage(WsDnsServer ds)
    {
        var sp = new StackPanel();
        string s = ds.S.Name;
        sp.Children.Add(Title(s, "DNS server" + (ds.Up ? "" : " - the DNS Server service isn't running")));
        sp.Children.Add(Pk.H("Forwarders"));
        var fw = Pk.Box(string.Join(",", ds.Forwarders), 280);
        var rh = Pk.Check("Use root hints if no forwarders are available", ds.UseRootHints);
        sp.Children.Add(fw); sp.Children.Add(rh);
        sp.Children.Add(Act("Apply", () => Run("dns forwarders server=" + s + " set=" + Q(Pk.T(fw).Replace(" ", "")) + " roothints=" + (rh.IsChecked == true ? "yes" : "no"))));
        var rec = Pk.Check("Recursion (and forwarders) enabled", ds.Recursion);
        rec.Margin = new Thickness(0, 8, 0, 8);
        rec.Click += (o, e) => Pk.Safe(() => Run("dns recursion server=" + s + " " + (rec.IsChecked == true ? "on" : "off")));
        sp.Children.Add(rec);
        var f = F("New Zone");
        f.Choice("type", "Zone type", new[] { "Primary", "Secondary", "Stub", "Forwarder" }, "Primary");
        f.Text("name", "Zone name", ""); f.Choice("ad", "Store in Active Directory", new[] { "Domain", "Forest", "Legacy", "no" }, Lab.Dirs.ContainsKey(s) ? "Domain" : "no");
        f.Choice("dynamic", "Dynamic updates", new[] { "Secure", "NonsecureAndSecure", "None" }, "Secure"); f.Text("masters", "Master servers (IPs)", "");
        f.Submit("Create zone", () => { string t = f.V("type"); Run("dns zone add server=" + s + " name=" + Q(f.V("name")) + " type=" + t + " ad=" + (t == "Secondary" ? "no" : f.V("ad")) + (t == "Primary" ? " dynamic=" + (f.V("ad") == "no" && f.V("dynamic") == "Secure" ? "None" : f.V("dynamic")) : " masters=" + Q(f.V("masters").Replace(" ", "")))); });
        DnsForm = f;
        sp.Children.Add(Pk.Card(f.Panel));
        return sp;
    }

    Control ZonePage(WsDnsServer ds, DnsZone z)
    {
        var sp = new StackPanel(); string s = ds.S.Name, zn = Q(z.Name);
        sp.Children.Add(Title(z.Name, z.Type + (z.AdIntegrated ? ", Active Directory-integrated (" + z.ReplicationScope + ")" : z.Type == "Forwarder" ? "" : ", stored in a file on " + s) + (z.Signed ? ", signed (DNSSEC)" : "")));
        if (z.Type == "Forwarder" || z.Type == "Stub" || z.Type == "Secondary") sp.Children.Add(Pk.Pair("Master servers", string.Join(", ", z.Masters), Narrow));
        var bar = Bar();
        if (z.Type == "Secondary") bar.Children.Add(Act("Transfer from master", () => Run("dns zone transfer server=" + s + " " + zn)));
        if (z.Type == "Primary") bar.Children.Add(Act(z.Signed ? "Unsign zone" : "Sign the zone (DNSSEC)", () => Run("dns zone " + (z.Signed ? "unsign" : "sign") + " server=" + s + " name=" + zn)));
        bar.Children.Add(Act("Delete zone", () => { if (Run("dns zone remove server=" + s + " name=" + zn).Ok) { Node = ""; Show(); } }));
        sp.Children.Add(bar);
        if (z.Type == "Primary")
        {
            var dyn = Pk.Combo(new[] { "None", "Secure", "NonsecureAndSecure" }, z.DynamicUpdate, 190);
            var xfr = Pk.Combo(new[] { "NoTransfer", "TransferAnyServer", "TransferToZoneNameServer", "TransferToSecureServers" }, z.Transfers, 220);
            var sec = Pk.Box(string.Join(",", z.SecondaryServers), 160); sec.Width = 160;
            sp.Children.Add(Pk.Row(Pk.Txt("Dynamic updates", 14), dyn, Act("Apply", () => Run("dns zone set server=" + s + " name=" + zn + " dynamic=" + Pk.Val(dyn)))));
            sp.Children.Add(Pk.Row(Pk.Txt("Zone transfers", 14), xfr, sec, Act("Apply", () => Run("dns zone set server=" + s + " name=" + zn + " transfers=" + Pk.Val(xfr) + " secondaries=" + Q(Pk.T(sec).Replace(" ", ""))))));
        }
        if (z.Type == "Forwarder") return sp;
        sp.Children.Add(Pk.H("Records"));
        var recs = z.Records.Where(r => r.Scope.Length == 0).OrderBy(r => r.Name == "@" ? "" : r.Name).ThenBy(r => r.Type).ToList();
        sp.Children.Add(Pk.Table(new[] { "Name", "Type", "Data" }, recs.Select(r => new[] { r.Name == "@" ? "(same as parent folder)" : r.Name, r.Type, r.Data.Replace("|", " ") }), z.Type == "Primary" ? (i => { Sub = "rec:" + i; Show(); }) : null, Narrow, "No records."));
        if (z.Type == "Primary" && Sub.StartsWith("rec:", StringComparison.Ordinal) && int.TryParse(Sub.Substring(4), out int sel) && sel < recs.Count)
        {
            var rr = recs[sel];
            sp.Children.Add(Pk.Card(Pk.Col(Pk.Txt("Picked: " + rr.Name + " " + rr.Type + " " + rr.Data.Replace("|", " "), 14), Act("Delete this record", () => { Sub = ""; Run("dns record remove server=" + s + " zone=" + zn + " name=" + Q(rr.Name) + " type=" + rr.Type + " data=" + Q(rr.Data)); }))));
        }
        if (z.Type != "Primary") return sp;
        var f = F("New record");
        f.Choice("type", "Type", new[] { "A", "CNAME", "MX", "PTR", "TXT", "SRV" }, "A"); f.Text("name", "Name (blank: parent)", ""); f.Text("data", "Data (IP, target, 10|mail.x)", ""); f.Check("ptr", "Create associated pointer (PTR) record", false);
        f.Submit("Add record", () => Run("dns record add server=" + s + " zone=" + zn + " name=" + Q(f.V("name").Length == 0 ? "@" : f.V("name")) + " type=" + f.V("type") + " data=" + Q(f.V("data")) + (f.V("ptr") == "yes" ? " ptr=yes" : "")));
        DnsForm = f;
        sp.Children.Add(Pk.Card(f.Panel));
        return sp;
    }
}
