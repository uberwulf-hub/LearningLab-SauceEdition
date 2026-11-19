// Cert Lab - console actions of Group Policy Management (practice): GPOs, links, inheritance, settings, security
// filtering and delegation, WMI filters, backups, the central store, and refreshing policy on a computer.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public class GpoBackup { public string Id = "", Gpo = "", Guid = "", Path = "", Comment = ""; public DateTime When; public WsAdObj Copy; }

    public partial class WsLab
    {
        public readonly List<GpoBackup> GpoBackups = new List<GpoBackup>();

        public string CopyCentralStore(string dc)
        {
            var m = M(dc);
            var src = m == null ? null : m.FindFile("C:\\Windows\\PolicyDefinitions");
            if (src == null) return dc + " has no C:\\Windows\\PolicyDefinitions folder.";
            var dst = m.MakeDir("C:\\Windows\\SYSVOL\\sysvol\\" + DomainDns + "\\Policies\\PolicyDefinitions", Now);
            CopyTree(src, dst, false);
            return null;
        }
        public GpoBackup Backup(string dc, WsAdObj g, string path, string comment)
        {
            var b = new GpoBackup { Id = NewGuid(), Gpo = g.Get("displayName"), Guid = g.Guid, Path = string.IsNullOrEmpty(path) ? "C:\\GpoBackups" : path, Comment = comment ?? "", When = Now, Copy = g.Clone() };
            GpoBackups.Add(b);
            var m = M(dc); if (m != null) m.MakeDir(b.Path + "\\{" + b.Id.ToUpperInvariant() + "}", Now);
            return b;
        }
        /// <summary>Restores a GPO's settings, status, permissions and WMI filter from a backup (links are not part of a backup).</summary>
        public string RestoreGpo(string dc, GpoBackup b)
        {
            var d = Dir(dc);
            var g = d.ById(b.Guid);
            if (g == null)
            {
                var gone = d.Objs.Values.FirstOrDefault(x => WsText.Eq(x.Guid, b.Guid));
                g = b.Copy.Clone(); g.Deleted = false; g.Version = gone == null ? 0 : gone.Version;
                d.Objs[g.Guid] = g;
            }
            foreach (var k in new[] { "gpSetting", "gpPerm", "flags", "wmiFilter", "description" }) { if (b.Copy.A.ContainsKey(k)) g.A[k] = new List<string>(b.Copy.List(k)); else g.A.Remove(k); }
            g.Set("versionComputer", (int.Parse("0" + g.Get("versionComputer"), CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture));
            Touch(dc, g);
            return null;
        }
    }

    public static partial class WsOps
    {
        static partial void RunGp(WsLab lab, string v, string noun, List<string> tok, ref WsResult r)
        {
            if (v != "gpo" && v != "wmi" && v != "inheritance" && v != "gpupdate" && v != "centralstore") return;
            var a = Parse(tok, 2);
            string dc = Dc(lab, a, true);
            if (dc == null) { r = WsResult.Fail("No domain controller is running."); return; }
            var d = lab.Dir(dc);
            if (v == "gpupdate") { var e = lab.Refresh(tok.Count > 1 ? tok[1] : "", a["target"]); r = R(e, "Group Policy was refreshed on " + (tok.Count > 1 ? tok[1] : "") + "."); return; }
            if (v == "centralstore") { r = R(lab.CopyCentralStore(dc), "Copied the ADMX files to the central store in SYSVOL on " + dc + "."); return; }
            if (v == "inheritance")
            {
                var t = lab.LinkTarget(d, ResolvePath(lab, a.P(0)) ?? a.P(0));
                if (t == null || !t.Is("organizationalUnit") && !t.Is("domainDNS")) { r = WsResult.Fail("Block Inheritance is set on a domain or an OU; " + a.P(0) + " isn't one."); return; }
                bool block = !WsText.Eq(noun, "unblock") && a.Yes("blocked", true);
                lab.BlockInheritance(dc, t, block);
                r = WsResult.Done((block ? "Inheritance is blocked at " : "Inheritance is no longer blocked at ") + Dn.Canonical(t.Dn) + "."); return;
            }
            if (v == "wmi")
            {
                if (noun == "new")
                {
                    string q = a["query"], n = a["name"] ?? a.P(0);
                    var err = WsWmi.Check(q);
                    if (err != null) { r = WsResult.Fail(err); return; }
                    if (lab.FindWmi(d, n) != null) { r = WsResult.Fail("A WMI filter named " + n + " already exists."); return; }
                    string g = lab.NewGuid();
                    var o = lab.Create(dc, "msWMI-Som", "CN={" + g.ToUpperInvariant() + "}," + lab.WmiDn, "msWMI-Name", n, "query", q, "description", a["description"] ?? "");
                    r = WsResult.Done("Created the WMI filter " + n + "."); return;
                }
                var w = lab.FindWmi(d, a.P(0));
                if (w == null) { r = WsResult.Fail("There is no WMI filter " + a.P(0) + "."); return; }
                if (noun == "set") { var err = a.Has("query") ? WsWmi.Check(a["query"]) : null; if (err != null) { r = WsResult.Fail(err); return; } if (a.Has("query")) w.Set("query", a["query"]); if (a.Has("description")) w.Set("description", a["description"]); lab.Touch(dc, w); r = WsResult.Done("Saved the WMI filter."); return; }
                if (noun == "delete") { if (lab.Gpos(d).Any(x => x.Get("wmiFilter") == w.Guid)) { r = WsResult.Fail("The WMI filter is in use by a GPO."); return; } w.Deleted = true; lab.Touch(dc, w); r = WsResult.Done("Deleted the WMI filter."); return; }
                return;
            }
            // gpo ...
            if (noun == "new") { string e; var g = lab.NewGpo(dc, a["name"] ?? a.P(0), a["comment"], out e); r = R(e, "Created the GPO " + (a["name"] ?? a.P(0)) + " on " + dc + "."); return; }
            if (noun == "restore")
            {
                var b = lab.GpoBackups.Where(x => WsText.Eq(x.Gpo, a.P(0)) || WsText.Eq(x.Id, a.P(0))).OrderByDescending(x => x.When).FirstOrDefault();
                if (b == null) { r = WsResult.Fail("There is no backup of " + a.P(0) + "."); return; }
                r = R(lab.RestoreGpo(dc, b), "Restored " + b.Gpo + " from the backup of " + WsText.Date(b.When) + "."); return;
            }
            var gpo = lab.FindGpo(d, a.P(0));
            if (gpo == null) { r = WsResult.Fail("There is no GPO named " + a.P(0) + " on " + dc + "."); return; }
            string name = gpo.Get("displayName");
            switch (noun)
            {
                case "delete":
                    foreach (var kv in lab.WhereLinked(d, gpo).ToList()) lab.Unlink(dc, gpo, kv.Key);
                    gpo.Deleted = true; lab.Touch(dc, gpo);
                    r = WsResult.Done("Deleted the GPO " + name + " and its links."); return;
                case "rename":
                    if (lab.FindGpo(d, a.P(1)) != null) { r = WsResult.Fail("A GPO named " + a.P(1) + " already exists."); return; }
                    gpo.Set("displayName", a.P(1)); lab.Touch(dc, gpo); r = WsResult.Done("Renamed the GPO to " + a.P(1) + "."); return;
                case "link": case "setlink": case "unlink":
                    {
                        var t = lab.LinkTarget(d, ResolvePath(lab, a.P(1)) ?? a.P(1));
                        if (t == null && a.P(1) != null) t = lab.LinkTarget(d, a.P(1));
                        if (t == null) { r = WsResult.Fail("A GPO links to a site, the domain or an OU; " + a.P(1) + " isn't one."); return; }
                        string e = noun == "link" ? lab.Link(dc, gpo, t, a.Yes("enabled", true), a.Yes("enforced", false), a.Int("order", 0))
                            : noun == "unlink" ? lab.Unlink(dc, gpo, t)
                            : lab.SetLink(dc, gpo, t, a.Has("enabled") ? (bool?)a.Yes("enabled", true) : null, a.Has("enforced") ? (bool?)a.Yes("enforced", false) : null, a.Int("order", 0));
                        r = R(e, (noun == "unlink" ? "Removed the link of " : "Linked ") + name + (noun == "unlink" ? " from " : " to ") + (t.Is("site") ? t.Name : Dn.Canonical(t.Dn)) + "."); return;
                    }
                case "set":
                    {
                        var def = GpCatalog.Get(a["setting"] ?? "");
                        if (def == null) { r = WsResult.Fail("Unknown setting " + a["setting"] + " (use the id, e.g. NoControlPanel, or the setting's name)."); return; }
                        string state = a["state"] ?? "Enabled";
                        if (def.Kind == "choice" && WsText.Eq(state, "Enabled") && !def.Options.Any(o => WsText.Eq(o.Split('=')[0], a["value"] ?? ""))) { r = WsResult.Fail("Pick one of: " + string.Join(", ", def.Options.Select(o => o.Split('=')[0]).ToArray()) + "."); return; }
                        if (def.Kind == "number" && WsText.Eq(state, "Enabled")) { int n; if (!int.TryParse(a["value"], out n) || n < 0) { r = WsResult.Fail(def.Name + " needs a number."); return; } }
                        if (def.Kind == "text" && WsText.Eq(state, "Enabled") && string.IsNullOrEmpty(a["value"])) { r = WsResult.Fail(def.Name + " needs a value."); return; }
                        lab.SetSetting(dc, gpo, def.Scope, def.Id, state, a["value"]);
                        r = WsResult.Done(name + ": " + def.Name + " is " + state + (string.IsNullOrEmpty(a["value"]) ? "" : " (" + a["value"] + ")") + "."); return;
                    }
                case "status":
                    {
                        string[] st = { "AllSettingsEnabled", "UserSettingsDisabled", "ComputerSettingsDisabled", "AllSettingsDisabled" };
                        int i = Array.FindIndex(st, x => WsText.Eq(x, a.P(1)));
                        if (i < 0) { r = WsResult.Fail("GPO status: " + string.Join(", ", st) + "."); return; }
                        gpo.Set("flags", i.ToString(CultureInfo.InvariantCulture)); lab.Touch(dc, gpo); r = WsResult.Done(name + ": " + st[i] + "."); return;
                    }
                case "perm": case "filter":
                    {
                        string trustee = noun == "filter" ? a.P(2) : a.P(1);
                        string level = noun == "filter" ? (WsText.Eq(a.P(1), "add") ? "GpoApply" : "None") : a.P(2);
                        if (!WsLab.PermLevels.Contains(level, StringComparer.OrdinalIgnoreCase)) { r = WsResult.Fail("Permission level: " + string.Join(", ", WsLab.PermLevels) + "."); return; }
                        bool wellKnown = new[] { "Authenticated Users", "Everyone", "SYSTEM", "ENTERPRISE DOMAIN CONTROLLERS" }.Contains(trustee, StringComparer.OrdinalIgnoreCase);
                        if (!wellKnown && d.Find(trustee, "principal") == null) { r = WsResult.Fail("There is no user, group or computer " + trustee + "."); return; }
                        lab.SetPerm(dc, gpo, trustee, WsLab.PermLevels.First(x => WsText.Eq(x, level)));
                        r = WsResult.Done(name + ": " + trustee + " now has " + level + "."); return;
                    }
                case "wmi":
                    {
                        if (WsText.Eq(a.P(1), "none") || a.P(1) == null) { gpo.Set("wmiFilter", null); lab.Touch(dc, gpo); r = WsResult.Done(name + " has no WMI filter."); return; }
                        var w = lab.FindWmi(d, a.P(1));
                        if (w == null) { r = WsResult.Fail("There is no WMI filter " + a.P(1) + "."); return; }
                        gpo.Set("wmiFilter", w.Guid); lab.Touch(dc, gpo); r = WsResult.Done(name + " is filtered by " + w.Get("msWMI-Name") + "."); return;
                    }
                case "backup": { var b = lab.Backup(dc, gpo, a["path"], a["comment"]); r = WsResult.Done("Backed up " + name + " to " + b.Path + "."); return; }
                case "copy":
                    {
                        string e; var n = lab.NewGpo(dc, a.P(1), gpo.Get("description"), out e);
                        if (n == null) { r = WsResult.Fail(e); return; }
                        n.SetList("gpSetting", gpo.List("gpSetting")); n.SetList("gpPerm", gpo.List("gpPerm")); lab.Touch(dc, n);
                        r = WsResult.Done("Copied " + name + " to " + a.P(1) + " (links aren't copied)."); return;
                    }
            }
            r = WsResult.Fail("Unknown Group Policy action: gpo " + noun);
        }
    }
}
