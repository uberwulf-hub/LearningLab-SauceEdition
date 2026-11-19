// Cert Lab - the practice portal's device pages, reports and enrolment (App Hub's UiEpDevices / UiEpReports /
// UiEpEnroll): All devices (filter by OS) and a device's page (properties, compliance with the reasons, policies, apps,
// BitLocker keys, the local admin password behind Show, the device log, the remote actions with their questions);
// Reports (compliance, configuration status, app installs, feature updates, the audit log); Enrollment (automatic
// enrolment, restrictions, Autopilot devices with Import, profiles, the Enrollment Status Page).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using LearningLab.Views;
using CertLab;

namespace LearningLab.CertLab.Portal;

public partial class EpWorkspace
{
    string osFilter = "All";

    Control DevicesBlade()
    {
        var s = Stack(Title("All devices", "Every device Microsoft Entra ID or Intune knows in the pretend tenant. Open one for its details and remote actions."));
        var f = Pk.Combo(new[] { "All", "Windows", "iOS", "Android" }, osFilter, 160);
        f.SelectionChanged += (o, e) => Pk.Safe(() => { if (Pk.Val(f) == osFilter) return; osFilter = Pk.Val(f); Render(true); });
        s.Children.Add(Pk.Row(Pk.Txt("Operating system  ", 14), f));
        var list = T.Devices.Where(d => osFilter == "All" || d.Is("os", osFilter)).OrderBy(d => d.Name).ToList();
        var rows = list.Select(d => new[] { d.Name, d["os"] + " " + d["osVersion"], d.JoinType, d["ownership"], d.Enrolled ? "Intune" : "None", d.Enrolled ? d.Compliance : "", d.LastSync == DateTime.MinValue ? "" : EpText.Date(d.LastSync), d.PrimaryUser });
        s.Children.Add(Pk.Table(new[] { "Name", "OS", "Join type", "Ownership", "Managed by", "Compliance", "Last check-in", "Primary user" }, rows, i => Go("device|" + list[i].Name), Narrow, "No devices match."));
        return s;
    }

    Control DeviceBlade(EpDevice d)
    {
        if (d == null) return Pk.Small("That device isn't in the tenant any more (it may have been deleted).");
        var s = Stack(Title(d.Name, d["os"] + " " + d["osVersion"] + "  |  " + (d.JoinType.Length > 0 ? d.JoinType : "not in Microsoft Entra ID") + "  |  " + (d.Enrolled ? "managed by Intune" : "not enrolled")));
        s.Children.Add(RemoteBar(d));
        if (d["note"].Length > 0) s.Children.Add(Pk.Small(d["note"]));
        s.Children.Add(Pk.H("Properties"));
        foreach (var p in new[] { "Primary user|user", "Ownership|ownership", "Join type|join", "Managed by|managed", "Edition|edition", "Feature version|featureVersion", "Monthly update|patch", "Serial number|serial", "Manufacturer|manufacturer", "Model|model", "Enrollment profile|enrollmentProfile", "Category|category" })
        {
            var kv = p.Split('|');
            if (d[kv[1]].Length > 0 || kv[1] == "user") s.Children.Add(Pk.Pair(kv[0], d[kv[1]], Narrow));
        }
        s.Children.Add(Pk.Pair("Last check-in", d.LastSync == DateTime.MinValue ? "never" : EpText.Date(d.LastSync), Narrow));
        s.Children.Add(Pk.H("Device compliance"));
        s.Children.Add(Pk.Row(Pk.Badge(d.Compliance)));
        foreach (var r in d.ComplianceReasons) s.Children.Add(Pk.Txt("- " + r, 14));
        var comp = d.Policies.Where(p => p.Kind == "compliance").ToList();
        s.Children.Add(Pk.Table(new[] { "Policy", "Status", "Details" }, comp.Select(p => new[] { p.Policy, p.Status, p.Detail }), null, Narrow, d.Enrolled ? "No compliance policy is assigned to this device or its user (the tenant setting decides: " + T.Settings["noPolicyMark"] + ")." : "Not enrolled: Intune doesn't evaluate compliance."));
        s.Children.Add(Pk.H("Configuration and security policies"));
        var cfg = d.Policies.Where(p => p.Kind != "compliance").ToList();
        s.Children.Add(Pk.Table(new[] { "Policy", "Kind", "Status", "Details" }, cfg.Select(p => { var k = EpSchemas.Get(p.Kind); return new[] { p.Policy, k == null ? p.Kind : k.Title, p.Status, p.Detail }; }), null, Narrow, "Nothing has reached this device."));
        s.Children.Add(Pk.H("Apps"));
        s.Children.Add(Pk.Table(new[] { "App", "Intent", "Status", "Details" }, d.Apps.Values.Select(a => new[] { a.App, EpOps.IntentName(a.Intent), a.Status, a.Detail }), null, Narrow, "No apps assigned."));
        if (d.Windows) WindowsParts(s, d);
        s.Children.Add(Pk.H("Device log (Intune Management Extension)"));
        var log = d.Log.AsEnumerable().Reverse().Take(25).ToList();
        if (log.Count == 0) s.Children.Add(Pk.Small("Nothing logged since the lab started."));
        else s.Children.Add(Pk.MonoText(string.Join("\n", log)));
        return s;
    }

    bool showLaps;
    void WindowsParts(StackPanel s, EpDevice d)
    {
        s.Children.Add(Pk.H("BitLocker"));
        s.Children.Add(Pk.Pair("Encryption", d["bitlocker"] + (d["bitlockerMethod"].Length > 0 ? " (" + d["bitlockerMethod"] + ")" : "") + (d["bitlockerWhy"].Length > 0 ? " - " + d["bitlockerWhy"] : ""), Narrow));
        if (d["recoveryKeyIn"].Length > 0) s.Children.Add(Pk.Pair("Recovery key (stored in " + d["recoveryKeyIn"] + ")", d["recoveryKey"], Narrow));
        else s.Children.Add(Pk.Small(d["recoveryKey"].Length > 0 ? "The device has a recovery key, but it wasn't backed up to Microsoft Entra ID, so the portal can't show it." : "No BitLocker recovery key is stored for this device."));
        s.Children.Add(Pk.H("Local admin password"));
        if (d["laps.password"].Length == 0) s.Children.Add(Pk.Small(d["laps"] == "Error" ? "LAPS reports an error: " + d["lapsWhy"] : "No local admin password is backed up for this device (it needs a LAPS policy, and Microsoft Entra LAPS turned on in device settings)."));
        else
        {
            s.Children.Add(Pk.Pair("Account", d["laps.account"], Narrow));
            s.Children.Add(Pk.Pair("Password", showLaps ? d["laps.password"] : new string('*', 12), Narrow));
            s.Children.Add(Pk.Pair("Last rotated", d["laps.updated"], Narrow));
            s.Children.Add(Pk.Pair("Next rotation", d["laps.expires"], Narrow));
            s.Children.Add(Pk.Small(showLaps ? "Hide" : "Show", () => { showLaps = !showLaps; Render(true); }));
        }
        s.Children.Add(Pk.H("Protection"));
        s.Children.Add(Pk.Pair("Firewall", d["firewall"], Narrow));
        s.Children.Add(Pk.Pair("Antivirus / real-time protection", d["av"] + " / " + d["rtp"], Narrow));
        s.Children.Add(Pk.Pair("Defender for Endpoint", d["mde"] + (d["risk"].Length > 0 ? ", risk " + d["risk"] : ""), Narrow));
        s.Children.Add(Pk.Pair("TPM / Secure Boot", d["tpm"] + " / " + d["secureBoot"], Narrow));
        if (d["kiosk"].Length > 0) s.Children.Add(Pk.Pair("Kiosk", d["kiosk"], Narrow));
    }

    /// <summary>The remote action buttons, and the small question panel some of them need.</summary>
    Control RemoteBar(EpDevice d)
    {
        var sp = new StackPanel();
        var wp = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
        var ask = new StackPanel();
        foreach (var a in EpTenant.ActionsFor(d))
        {
            string a0 = a;
            wp.Children.Add(Pk.Small(EpTenant.RemoteLabel(a), () => RemoteClicked(d, a0, ask)));
        }
        sp.Children.Add(wp); sp.Children.Add(ask);
        return sp;
    }

    void RemoteClicked(EpDevice d, string a, StackPanel ask)
    {
        ask.Children.Clear();
        void Send(string arg, bool keepEnr, bool keepData)
        {
            string why = T.Remote(d, a, arg, keepEnr, keepData);
            T.Tick();
            Say(why == null ? EpTenant.RemoteLabel(a) + " sent to " + d.Name + "." + (a == "delete" ? "" : " Open the Devices tab to see the device.") : why, why == null);
            if (why == null && a == "delete") Go("devices"); else Render(true);
        }
        if (a == "rename")
        {
            var box = Pk.Box(d.Name, 220); box.HorizontalAlignment = HorizontalAlignment.Left; box.Width = 220;
            ask.Children.Add(Pk.Txt("New name (up to 15 characters: letters, digits, hyphens). The device restarts to use it.", 14));
            ask.Children.Add(Pk.Row(box, Pk.Accent("Rename", () => Send(Pk.T(box).Trim(), false, false)), Pk.Btn("Cancel", () => ask.Children.Clear())));
            return;
        }
        if (a == "wipe" || a == "fresh-start" || a == "retire" || a == "delete" || a == "autopilot-reset")
        {
            var keep = Pk.Check(a == "wipe" ? "Keep enrollment state and user account" : "Keep user data on this device", false);
            ask.Children.Add(Pk.Txt(a == "delete" ? "Delete " + d.Name + " from Intune? It stops being managed; it can enrol again." : EpTenant.RemoteLabel(a) + " " + d.Name + "? This can't be undone.", 14));
            if (a == "wipe" || a == "fresh-start") ask.Children.Add(keep);
            ask.Children.Add(Pk.Row(Pk.Accent("Yes, " + EpTenant.RemoteLabel(a).ToLowerInvariant(), () => Send(null, a == "wipe" && keep.IsChecked == true, a == "fresh-start" && keep.IsChecked == true)), Pk.Btn("Cancel", () => ask.Children.Clear())));
            return;
        }
        Send(null, false, false);
    }

    // ------------------------------------------------------------ reports
    Control ReportsBlade(string which)
    {
        if (which.Length == 0) which = "compliance";
        var s = Stack(Title("Reports", null));
        var tabs = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var t in new[] { "compliance|Device compliance", "config|Configuration policy status", "apps|App install status", "feature|Windows feature updates", "audit|Audit log" })
        {
            var p = t.Split('|');
            var b = Pk.Small(p[1], () => ReplaceBlade("reports|" + p[0]));
            Pk.Picked(b, p[0] == which);
            tabs.Children.Add(b);
        }
        s.Children.Add(tabs);
        var devs = T.Devices.OrderBy(d => d.Name).ToList();
        switch (which)
        {
            case "compliance":
                {
                    var en = devs.Where(d => d.Enrolled).ToList();
                    s.Children.Add(Pk.Small(en.Count(d => d.Compliance == "Compliant") + " compliant, " + en.Count(d => d.Compliance == "Not compliant") + " not compliant, " + en.Count(d => d.Compliance == "In grace period") + " in grace period, of " + en.Count + " enrolled devices."));
                    var rows = en.Select(d => new[] { d.Name, d.PrimaryUser, d["os"], d.Compliance, string.Join("; ", d.ComplianceReasons) }).ToList();
                    s.Children.Add(Pk.Table(new[] { "Device", "User", "OS", "Compliance", "Reasons" }, rows, i => Go("device|" + rows[i][0]), Narrow, "No enrolled devices."));
                    break;
                }
            case "config":
                {
                    var kinds = new[] { "config", "av", "wsx", "diskEncryption", "firewall", "firewallRules", "asr", "laps", "edr", "baseline", "ring", "script", "remediation" };
                    var pols = T.All.Where(o => kinds.Contains(o.Kind)).OrderBy(o => o.Name).ToList();
                    var rows = pols.Select(o =>
                    {
                        var st = devs.Select(d => d.Policies.FirstOrDefault(p => p.Kind == o.Kind && p.Policy == o.Name)).Where(p => p != null).ToList();
                        return new[] { o.Name, EpSchemas.Get(o.Kind).Title, st.Count(p => p.Status == "Succeeded").ToString(), st.Count(p => p.Status == "Error").ToString(), st.Count(p => p.Status == "Conflict").ToString(), st.Count(p => p.Status == "Not applicable").ToString() };
                    }).ToList();
                    s.Children.Add(Pk.Table(new[] { "Profile", "Kind", "Succeeded", "Error", "Conflict", "Not applicable" }, rows, i => Go("obj|" + pols[i].Id), Narrow, "No configuration profiles or security policies yet."));
                    break;
                }
            case "apps":
                {
                    var apps = T.Of("app").OrderBy(a => a.Name).ToList();
                    var rows = apps.Select(a =>
                    {
                        var st = devs.Where(d => d.Apps.ContainsKey(a.Name)).Select(d => d.Apps[a.Name].Status).ToList();
                        return new[] { a.Name, a["type"], st.Count(x => x == "Installed").ToString(), st.Count(x => x == "Failed").ToString(), st.Count(x => x == "Pending").ToString(), st.Count(x => x == "Not installed" || x == "Available").ToString() };
                    }).ToList();
                    s.Children.Add(Pk.Table(new[] { "App", "Type", "Installed", "Failed", "Pending", "Not installed" }, rows, i => Go("obj|" + apps[i].Id), Narrow, "No apps yet."));
                    break;
                }
            case "feature":
                {
                    if (!T.Settings.Is("windowsData", "On"))
                    {
                        s.Children.Add(Pk.Txt("This report needs Windows diagnostic data, and that is turned off for the tenant.", 14, true));
                        s.Children.Add(Pk.Txt("Turn it on in Tenant administration > Connectors: set \"Enable features that need Windows diagnostic data\" to On and save. Devices then report at their next check-in.", 14));
                        s.Children.Add(Pk.Link("Open Connectors", () => Go("connectors")));
                        break;
                    }
                    var win = devs.Where(d => d.Windows && d.Enrolled).ToList();
                    var rows = win.Select(d => new[] { d.Name, "Windows " + (EpTenant.IsWin11(d["featureVersion"]) ? "11" : "10") + ", " + d["featureVersion"], d["patch"], d["ring"], d["win11Capable"] == "No" ? "Can't run Windows 11" : "Ready", d["patchNote"] }).ToList();
                    s.Children.Add(Pk.Table(new[] { "Device", "Version", "Monthly update", "Ring", "Windows 11", "Notes" }, rows, i => Go("device|" + rows[i][0]), Narrow, "No enrolled Windows devices."));
                    break;
                }
            case "audit":
                {
                    s.Children.Add(Pk.Small("Every change in the tenant, newest first."));
                    foreach (var l in T.Audit.AsEnumerable().Reverse().Take(200)) { var t = Pk.Txt(l, 13, false, Pk.LabelC); t.Margin = new Thickness(0, 0, 0, 3); s.Children.Add(t); }
                    if (T.Audit.Count == 0) s.Children.Add(Pk.Small("Nothing yet."));
                    break;
                }
        }
        return s;
    }

    // ------------------------------------------------------------ enrolment
    Control EnrollBlade()
    {
        var s = Stack(Title("Enrollment", "How devices join Intune: automatic enrolment when Windows joins Microsoft Entra ID, restrictions, and Windows Autopilot for new PCs."));
        s.Children.Add(Pk.H("Automatic enrolment"));
        s.Children.Add(TenantPart("Automatic enrolment", null));
        s.Children.Add(Pk.H("Enrolment restrictions"));
        s.Children.Add(TenantPart("Enrolment restrictions", null));
        s.Children.Add(ListPart("restriction"));
        s.Children.Add(Pk.H("Windows Autopilot devices"));
        var recs = T.Of("autopilot").OrderBy(r => r["serial"]).ToList();
        var rows = recs.Select(r => new[] { r["serial"], r["model"], r["groupTag"], r["profileStatus"], r["profile"], r["user"] });
        s.Children.Add(Pk.Table(new[] { "Serial number", "Model", "Group tag", "Profile status", "Profile", "Assigned user" }, rows, i => Go("obj|" + recs[i].Id), Narrow, "No devices are registered for Autopilot yet. Import a hardware hash file."));
        s.Children.Add(ImportPanel());
        s.Children.Add(ListPart("autopilotProfile"));
        s.Children.Add(ListPart("esp"));
        return s;
    }

    Control ImportPanel()
    {
        var sp = new StackPanel();
        sp.Children.Add(Pk.Txt("Import", 14, true, Ui.AccentText));
        sp.Children.Add(Pk.Small("The file is the CSV that Get-WindowsAutopilotInfo -OutputFile writes on a device (its first line is Device Serial Number,Windows Product ID,Hardware Hash). Pick the device the file is on and type its path."));
        var dev = Pk.Combo(new[] { "" }.Concat(T.Devices.Where(d => d.Windows).Select(d => d.Name)), "", 200);
        Pk.FirstLabel(dev, "The file is on...");
        dev.SelectedIndex = 0;
        var path = Pk.Box("C:\\HWID\\AutopilotHWID.csv", 280);
        var tag = Pk.Box("", 140); tag.HorizontalAlignment = HorizontalAlignment.Left; tag.Width = 160;
        sp.Children.Add(dev);
        sp.Children.Add(path);
        sp.Children.Add(Pk.Row(Pk.Txt("Group tag (optional)  ", 14), tag));
        sp.Children.Add(Pk.Accent("Import", () =>
        {
            if (Pk.Val(dev).Length == 0) { Say("Pick the device the file is on.", false); return; }
            var extra = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (Pk.T(tag).Trim().Length > 0) extra["groupTag"] = Pk.T(tag).Trim();
            RunOp(EpOps.ImportCsv(T, Pk.Val(dev), Pk.T(path).Trim(), extra));
        }));
        return Pk.Card(sp);
    }
}
