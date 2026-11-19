// Cert Lab - apps on a device of the pretend tenant: Required installs at check-in, Available shows in Company
// Portal, Uninstall removes. Win32 apps check requirements (architecture, minimum Windows), install dependencies first,
// replace what they supersede, need a silent install command, and are detected by their rule (MSI product code,
// file, registry) against the device's own files and registry - a wrong rule means "not detected after installation".
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public partial class EpTenant
    {
        void ApplyApps(EpDevice d)
        {
            var mine = new List<KeyValuePair<EpObj, string>>();
            foreach (var app in Of("app").ToList())
            {
                EpAssign hit; string why;
                if (!Targets(app, d, out hit, out why)) { if (d.Apps.ContainsKey(app.Name) && d.Apps[app.Name].Status == "Available") d.Apps.Remove(app.Name); continue; }
                // the strongest intent among the matching assignments: required, then uninstall, then available
                string intent = hit.Intent;
                foreach (var a in app.Assignments.Where(x => !x.Exclude && x != hit))
                {
                    var one = new EpObj { Kind = "app", Name = app.Name, T = this };
                    one["type"] = app["type"]; one.Assignments.Add(a);
                    foreach (var ex in app.Assignments.Where(x => x.Exclude)) one.Assignments.Add(ex);
                    if (!Targets(one, d)) continue;
                    if (a.Intent == "required" || a.Intent == "uninstall" && intent == "available") intent = a.Intent;
                }
                mine.Add(new KeyValuePair<EpObj, string>(app, intent));
            }
            foreach (var kv in mine.OrderBy(x => x.Value == "uninstall" ? 0 : 1))
            {
                var app = kv.Key;
                var newer = mine.FirstOrDefault(x => x.Value == "required" && EpText.Eq(x.Key["supersedes"], app.Name));
                if (kv.Value == "required" && newer.Key != null) { if (!Detected(d, app)) SetApp(d, app, "Not applicable", "Superseded by " + newer.Key.Name + ".", "required"); continue; }
                if (kv.Value == "required") Install(d, app, "required", 0);
                else if (kv.Value == "uninstall") Uninstall(d, app);
                else if (!Detected(d, app)) SetApp(d, app, "Available", "Shown in Company Portal for the user to install.", "available");
                else SetApp(d, app, "Installed", "", "available");
            }
        }

        void SetApp(EpDevice d, EpObj app, string status, string detail, string intent)
        {
            d.Apps[app.Name] = new EpAppState { App = app.Name, Status = status, Detail = detail ?? "", Intent = intent, When = Now };
        }

        public static string BuildOf(string minOs)
        {
            switch (minOs)
            {
                case "Windows 11 24H2": return "10.0.26100";
                case "Windows 11 23H2": return "10.0.22631";
                case "Windows 11 21H2": return "10.0.22000";
                case "Windows 10 22H2": return "10.0.19045";
                default: return "10.0.14393";
            }
        }
        public static int CompareVersion(string a, string b)
        {
            var x = (a ?? "").Split('.'); var y = (b ?? "").Split('.');
            for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
            {
                long p = 0, q = 0;
                if (i < x.Length) long.TryParse(x[i], out p);
                if (i < y.Length) long.TryParse(y[i], out q);
                if (p != q) return p < q ? -1 : 1;
            }
            return 0;
        }

        /// <summary>Installs an app on a device for a Required assignment, Company Portal or a dependency. Returns success.</summary>
        public bool Install(EpDevice d, EpObj app, string intent, int depth)
        {
            if (depth > 5) return false;
            string type = app["type"];
            if (Detected(d, app)) { SetApp(d, app, "Installed", "", intent); return true; }
            if (type == "Windows app (Win32)")
            {
                var pkg = EpCatalog.Package(app["package"]);
                if (pkg == null) { SetApp(d, app, "Failed", "The app package is missing.", intent); return false; }
                if (!app.List("arch").Any(a => EpText.Eq(a, d["arch"]))) { SetApp(d, app, "Not applicable", "Requirements not met: the device is " + d["arch"] + ".", intent); return false; }
                if (CompareVersion(d["osVersion"], BuildOf(app["minOs"])) < 0) { SetApp(d, app, "Not applicable", "Requirements not met: needs " + app["minOs"] + " or later.", intent); return false; }
                foreach (var depName in app.List("dependsOn"))
                {
                    var dep = Find("app", depName);
                    if (dep == null || !Install(d, dep, "dependency", depth + 1)) { SetApp(d, app, "Failed", "A dependency (" + depName + ") didn't install.", intent); return false; }
                }
                var old = Find("app", app["supersedes"]);
                if (old != null && !app.Is("uninstallPrevious", "No") && Detected(d, old)) { Remove(d, old); SetApp(d, old, "Not installed", "Replaced by " + app.Name + " (supersedence).", "superseded"); d.Note("Supersedence: uninstalled " + old.Name + " before installing " + app.Name + "."); }
                string cmd = app["installCmd"];
                bool msi = pkg.Setup.EndsWith(".msi", StringComparison.OrdinalIgnoreCase);
                bool names = cmd.IndexOf(pkg.Setup, StringComparison.OrdinalIgnoreCase) >= 0 && (!msi || cmd.IndexOf("msiexec", StringComparison.OrdinalIgnoreCase) >= 0);
                if (!names) { SetApp(d, app, "Failed", "The install command doesn't run the package's setup file (" + (msi ? "msiexec /i " : "") + pkg.Setup + "). Error 0x80070002: the system can't find the file.", intent); d.Note("Win32 app '" + app.Name + "': install command failed (file not found)."); return false; }
                if (!pkg.Silent.Any(s => cmd.Split(' ').Any(w => EpText.Eq(w, s)))) { SetApp(d, app, "Failed", "The installer waited for someone to click through it and timed out: the install command needs the silent switch (" + string.Join(" or ", pkg.Silent) + ").", intent); d.Note("Win32 app '" + app.Name + "': installer timed out (not silent)."); return false; }
                if (pkg.MinOs.Length > 0 && CompareVersion(d["osVersion"], pkg.MinOs) < 0) { SetApp(d, app, "Failed", "The installer exited with 1603 (0x80070643): it doesn't support this version of Windows. A requirement rule would have skipped this device.", intent); return false; }
                if (pkg.NeedsApp.Length > 0 && !FilesHave(d, EpCatalog.Packages.First(p => p.App == pkg.NeedsApp))) { SetApp(d, app, "Failed", "The installer exited with 1603 (0x80070643): fatal error during installation (it needs " + pkg.NeedsApp + " first: add it as a dependency).", intent); d.Note("Win32 app '" + app.Name + "': MSI error 1603."); return false; }
                if (app.Is("installBehavior", "User")) { SetApp(d, app, "Failed", "The installer writes to Program Files, which needs administrator rights: set Install behavior to System.", intent); return false; }
                PutFiles(d, pkg);
                bool det = Detected(d, app);
                if (!det) { SetApp(d, app, "Failed", "The application was not detected after installation completed successfully (0x87D1041C): check the detection rule.", intent); d.Note("Win32 app '" + app.Name + "': installed but the detection rule doesn't find it."); return false; }
                SetApp(d, app, "Installed", pkg.ExitCode == 3010 ? "Installed; the installer returned 3010 (a soft restart is pending)." : "", intent);
                d.Note("Win32 app '" + app.Name + "' installed (" + (intent == "dependency" ? "dependency" : intent) + ").");
                return true;
            }
            if (type.StartsWith("iOS", StringComparison.Ordinal) || type.StartsWith("Managed Google Play", StringComparison.Ordinal))
            {
                if (!d.Enrolled) { SetApp(d, app, "Not applicable", "The device isn't enrolled.", intent); return false; }
            }
            if (type.StartsWith("Microsoft Store", StringComparison.Ordinal) && EpCatalog.StoreApp(app["storeId"]) == null) { SetApp(d, app, "Failed", "The store couldn't find that package identifier.", intent); return false; }
            var installed = d.List("installed"); if (!installed.Contains(app.Name)) installed.Add(app.Name); d["installed"] = EpText.Join(installed);
            if (type.StartsWith("Microsoft 365", StringComparison.Ordinal))
            {
                var m = MachineFor(d);
                if (m != null) { m.MakeDir("C:\\Program Files\\Microsoft Office\\root\\Office16", Now); foreach (var a in app.List("suiteApps")) m.File("C:\\Program Files\\Microsoft Office\\root\\Office16\\" + OfficeExe(a), "", Now); }
            }
            SetApp(d, app, "Installed", "", intent);
            d.Note("App '" + app.Name + "' installed.");
            return true;
        }
        static string OfficeExe(string a)
        {
            switch (a) { case "Word": return "WINWORD.EXE"; case "Excel": return "EXCEL.EXE"; case "PowerPoint": return "POWERPNT.EXE"; case "Outlook": return "OUTLOOK.EXE"; case "OneNote": return "ONENOTE.EXE"; case "Access": return "MSACCESS.EXE"; case "Publisher": return "MSPUB.EXE"; default: return a.Replace(" ", "") + ".exe"; }
        }

        void Uninstall(EpDevice d, EpObj app)
        {
            if (!Detected(d, app)) { SetApp(d, app, "Not installed", "", "uninstall"); return; }
            Remove(d, app);
            SetApp(d, app, "Not installed", "Uninstalled (Uninstall assignment).", "uninstall");
            d.Note("App '" + app.Name + "' uninstalled.");
        }
        void Remove(EpDevice d, EpObj app)
        {
            var pkg = EpCatalog.Package(app["package"]);
            var m = MachineFor(d);
            if (pkg != null && m != null)
            {
                var dir = m.FindFile(pkg.InstallPath); if (dir != null && dir.Parent != null) dir.Parent.Kids.Remove(dir.Name);
                var un = m.HKLM.Find("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall"); if (un != null) un.Kids.Remove(pkg.UninstallKey);
            }
            var l = d.List("installed"); l.Remove(app.Name); d["installed"] = EpText.Join(l);
        }

        void PutFiles(EpDevice d, EpPackage pkg)
        {
            var m = MachineFor(d);
            if (m == null) { var l = d.List("installed"); l.Add(pkg.App); d["installed"] = EpText.Join(l); return; }
            m.File(pkg.InstallPath + "\\" + pkg.ExeName, "", Now).FakeSize = 1843200;
            var k = m.HKLM.Sub("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\" + pkg.UninstallKey);
            k.S("DisplayName", pkg.App + (pkg.MsiCode.Length > 0 ? "" : " " + pkg.Version)); k.S("DisplayVersion", pkg.Version); k.S("Publisher", pkg.Publisher); k.S("InstallLocation", pkg.InstallPath);
        }
        bool FilesHave(EpDevice d, EpPackage pkg)
        {
            var m = MachineFor(d);
            if (m == null) return d.List("installed").Contains(pkg.App);
            return m.FindFile(pkg.InstallPath + "\\" + pkg.ExeName) != null;
        }

        /// <summary>The app's detection rule, checked on the device (Win32), or "is it in the list" for other apps.</summary>
        public bool Detected(EpDevice d, EpObj app)
        {
            if (!app.Is("type", "Windows app (Win32)")) return d.List("installed").Contains(app.Name);
            var m = MachineFor(d);
            if (m == null) return false;
            switch (app["detect"])
            {
                case "MSI":
                    return m.HKLM.Find("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\" + app["msiCode"].Trim()) != null && app["msiCode"].Trim().StartsWith("{", StringComparison.Ordinal);
                case "File":
                    {
                        string p = app["detectPath"].TrimEnd('\\'), n = app["detectName"];
                        return m.FindFile(n.Length > 0 ? p + "\\" + n : p) != null;
                    }
                case "Registry":
                    {
                        string p = app["detectPath"];
                        foreach (var pre in new[] { "HKEY_LOCAL_MACHINE\\", "HKLM:\\", "HKLM\\" }) if (p.StartsWith(pre, StringComparison.OrdinalIgnoreCase)) p = p.Substring(pre.Length);
                        var k = m.HKLM.Find(p);
                        return k != null && (app["detectName"].Length == 0 || k.Value(app["detectName"]) != null);
                    }
            }
            return false;
        }
    }
}
