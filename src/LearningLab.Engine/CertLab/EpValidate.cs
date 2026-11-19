// Cert Lab - validation of the practice portal's forms: required fields, choices, number ranges, and the rules of
// each kind (dynamic rules, filters, Autopilot name templates, LAPS ages, app detection, Conditional Access
// controls, settings catalog values...). The messages are the ones the form shows under its fields.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace CertLab
{
    public static class EpValidate
    {
        public static List<string> Check(EpTenant t, EpObj o, EpObj existing)
        {
            var e = new List<string>();
            var k = EpSchemas.Get(o.Kind);
            if (k == null) return e;
            if (o.Kind != "tenant")
            {
                if (o.Name.Trim().Length == 0) e.Add("Name is required.");
                else if (t.Of(o.Kind).Any(x => x != existing && EpText.Eq(x.Name, o.Name))) e.Add("Another " + k.Title.ToLowerInvariant() + " is already called '" + o.Name + "'.");
            }
            foreach (var f in k.Fields)
            {
                if (f.Key == "name" || !f.Shows(x => o.Get(x))) continue;
                string v = o.Get(f.Key);
                if (f.Required && v.Trim().Length == 0) { e.Add(f.Label + " is required."); continue; }
                if (v.Length == 0) continue;
                if (f.Type == "choice" && f.Options.Length > 0 && !f.Options.Any(x => EpText.Eq(x, v))) e.Add(f.Label + ": pick one of " + string.Join(", ", f.Options.Where(x => x.Length > 0).ToArray()) + ".");
                if (f.Type == "multi") foreach (var p in EpText.Lines(v)) if (!f.Options.Any(x => EpText.Eq(x, p))) e.Add(f.Label + ": '" + p + "' isn't one of the choices.");
                if (f.Type == "number")
                {
                    int n;
                    if (!int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) e.Add(f.Label + " must be a whole number.");
                    else if (n < f.Min || n > f.Max) e.Add(f.Label + " must be between " + f.Min + " and " + f.Max + ".");
                }
            }
            foreach (var kv in o.P.Where(x => x.Key.StartsWith("s:", StringComparison.OrdinalIgnoreCase)))
            {
                var s = EpCatalog.Find(kv.Key.Substring(2));
                if (s == null) { e.Add("The settings catalog has no setting '" + kv.Key.Substring(2) + "'."); continue; }
                if (s.Type == "choice" && s.Data(kv.Value) == null) e.Add(s.Name + ": pick one of " + string.Join(", ", s.Choices.ToArray()) + ".");
                int n;
                if (s.Type == "number" && (!int.TryParse(kv.Value, out n) || n < s.Min || n > s.Max)) e.Add(s.Name + " must be a number from " + s.Min + " to " + s.Max + ".");
            }
            Specific(t, o, e);
            return e;
        }

        static bool Exists(EpTenant t, string group) { return EpText.Eq(group, EpTenant.AllUsers) || EpText.Eq(group, EpTenant.AllDevices) || t.Group(group) != null; }

        static void Specific(EpTenant t, EpObj o, List<string> e)
        {
            switch (o.Kind)
            {
                case "user":
                    if (!Regex.IsMatch(o["upn"], "^[A-Za-z0-9._-]+@" + Regex.Escape(t.Domain) + "$")) e.Add("User principal name must be name@" + t.Domain + ".");
                    else if (t.Users.Any(u => u.Id != o.Id && EpText.Eq(u["upn"], o["upn"]))) e.Add("Another user already signs in as " + o["upn"] + ".");
                    if (o["licenses"].Length > 0 && o["usageLocation"].Length == 0) e.Add("Set a usage location before assigning licences.");
                    break;
                case "group":
                    if (o["membership"].StartsWith("Dynamic", StringComparison.Ordinal))
                    {
                        string why = EpRules.Check(o["rule"], "group");
                        if (o["rule"].Length > 0 && why != null) e.Add("Dynamic membership rule: " + why);
                        else if (why == null)
                        {
                            string subj = EpRules.Subject(EpRules.Parse(o["rule"], "group"));
                            string want = o["membership"] == "Dynamic user" ? "user" : "device";
                            if (subj != want) e.Add("A " + o["membership"].ToLowerInvariant() + " group needs a rule on " + want + ". properties.");
                        }
                        if (o["type"] == "Microsoft 365" && o["membership"] == "Dynamic device") e.Add("Microsoft 365 groups can't have dynamic device membership.");
                    }
                    else foreach (var m in o.List("members")) if (t.User(m) == null && t.Device(m) == null && t.Group(m) == null) e.Add("Members: there is no user, device or group called '" + m + "'.");
                    break;
                case "filter":
                    { string why = EpRules.Check(o["rule"], "filter"); if (o["rule"].Length > 0 && why != null) e.Add("Rule: " + why); }
                    break;
                case "location":
                    foreach (var r in o.List("ranges")) if (!Regex.IsMatch(r, "^\\d{1,3}(\\.\\d{1,3}){3}/\\d{1,2}$")) e.Add("IP ranges: '" + r + "' isn't in CIDR form (for example 203.0.113.0/24).");
                    break;
                case "tenant":
                    if (o.Is("mdmScope", "Some") && !Exists(t, o["mdmGroup"])) e.Add("Pick the group for the MDM user scope.");
                    if (o.Is("joinScope", "Selected") && !Exists(t, o["joinGroup"])) e.Add("Pick the users who may join devices.");
                    break;
                case "roleAssignment":
                    foreach (var g in o.List("admins").Concat(o.List("scopeGroups"))) if (!Exists(t, g)) e.Add("There is no group called '" + g + "'.");
                    foreach (var s in o.List("scopeTags")) if (s != "Default" && t.Find("scopeTag", s) == null) e.Add("There is no scope tag called '" + s + "'.");
                    break;
                case "autopilotProfile":
                    {
                        bool hybrid = o["joinType"].Contains("hybrid");
                        if (o.Is("mode", "Self-deploying") && hybrid) e.Add("Self-deploying mode supports Microsoft Entra join only.");
                        if (o.Is("mode", "Self-deploying") && o.Is("preprovision", "Yes")) e.Add("Pre-provisioned deployment is for user-driven mode.");
                        string tpl = o["nameTemplate"];
                        if (tpl.Length > 0)
                        {
                            if (hybrid) e.Add("Device name templates are for Microsoft Entra joined devices; hybrid joined devices get their names from the Domain Join profile.");
                            string bare = Regex.Replace(tpl, "%SERIAL%|%RAND:\\d+%", "");
                            if (!Regex.IsMatch(bare, "^[A-Za-z0-9-]*$")) e.Add("Device name template: use letters, digits, hyphens, %SERIAL% or %RAND:x%.");
                            var rnd = Regex.Match(tpl, "%RAND:(\\d+)%");
                            int len = bare.Length + (rnd.Success ? int.Parse(rnd.Groups[1].Value) : 0);
                            if (len > 15) e.Add("Device name template: names can be at most 15 characters.");
                            if (Regex.Matches(tpl, "%SERIAL%|%RAND:\\d+%").Count > 1) e.Add("Device name template: use only one of %SERIAL% or %RAND:x%.");
                            if (Regex.IsMatch(tpl, "^%RAND:\\d+%$") || Regex.IsMatch(bare, "^\\d+$") && !tpl.Contains("%")) e.Add("Device name template: a name can't be only digits.");
                        }
                    }
                    break;
                case "esp":
                    foreach (var a in o.List("blockApps")) if (t.Find("app", a) == null) e.Add("Apps to wait for: there is no app called '" + a + "'.");
                    break;
                case "config": Config(t, o, e); break;
                case "compliance":
                    foreach (var key in new[] { "minOs", "maxOs" }) if (o[key].Length > 0 && !Regex.IsMatch(o[key], "^\\d+(\\.\\d+){0,3}$")) e.Add(EpSchemas.Label("compliance", key) + " must look like 10.0.22631 or 17.4.");
                    if (o["minOs"].Length > 0 && o["maxOs"].Length > 0 && EpTenant.CompareVersion(o["minOs"], o["maxOs"]) > 0) e.Add("Minimum OS version can't be higher than the maximum.");
                    break;
                case "ring":
                    { int s = o.Int("activeStart", 8), en = o.Int("activeEnd", 17); int span = (en - s + 24) % 24; if (span == 0 || span > 18) e.Add("Active hours can be at most 18 hours long, and the end must differ from the start."); }
                    break;
                case "laps":
                    if (o["backup"].Contains("Entra") && o.Int("ageDays", 30) < 7) e.Add("With backup to Microsoft Entra ID, Password Age Days must be at least 7.");
                    break;
                case "script":
                    PsSyntax(o["content"], "Script", e); break;
                case "remediation":
                    PsSyntax(o["detect"], "Detection script", e); PsSyntax(o["remediate"], "Remediation script", e); break;
                case "firewallRules":
                    foreach (var r in o.List("rules"))
                    {
                        var p = r.Split(',').Select(x => x.Trim()).ToArray();
                        if (p.Length != 5 || !new[] { "In", "Out" }.Contains(p[1]) || !new[] { "Allow", "Block" }.Contains(p[2]) || !new[] { "TCP", "UDP", "Any" }.Contains(p[3]) || !Regex.IsMatch(p[4], "^(Any|\\d+(-\\d+)?(;\\d+(-\\d+)?)*)$"))
                            e.Add("Rule '" + r + "': write name, In or Out, Allow or Block, TCP / UDP / Any, port (for example Expenses app, In, Allow, TCP, 8443).");
                    }
                    break;
                case "app": App(t, o, e); break;
                case "ca":
                    if (o.Is("grant", "Grant access") && o.List("controls").Count == 0) e.Add("Grant access needs at least one control (for example Require multifactor authentication).");
                    foreach (var p in o.List("usersInclude").Concat(o.List("usersExclude"))) if (!EpText.Eq(p, "All users") && t.User(p) == null && t.Group(p) == null) e.Add("Users: there is no user or group called '" + p + "'.");
                    foreach (var a in o.List("apps")) if (!EpText.Eq(a, "All cloud apps") && !EpText.Eq(a, "All resources") && !EpSchemas.CloudApps.Any(x => EpText.Eq(x, a))) e.Add("Target resources: '" + a + "' isn't a cloud app in this tenant.");
                    break;
            }
        }

        static void PsSyntax(string text, string label, List<string> e)
        {
            if (string.IsNullOrEmpty(text)) return;
            try { PsParser.Parse(text, ""); } catch (PsParseError x) { e.Add(label + " has a PowerShell syntax error: " + x.Message); }
        }

        static void Config(EpTenant t, EpObj o, List<string> e)
        {
            string plat = o["platform"], type = o["type"];
            bool win = plat == EpSchemas.Win;
            var winOnly = new[] { "Custom", "Kiosk", "Domain Join", "Edition upgrade and mode switch" };
            if (!win && winOnly.Contains(type)) e.Add("The " + type + " profile type is for Windows 10 and later.");
            if (win && type == "Device restrictions") e.Add("For Windows, use the settings catalog instead of the Device restrictions template in Cert Lab.");
            switch (type)
            {
                case "Settings catalog":
                    if (!o.P.Keys.Any(x => x.StartsWith("s:", StringComparison.OrdinalIgnoreCase))) e.Add("Add at least one setting from the settings catalog.");
                    if (!win) e.Add("Cert Lab's settings catalog has Windows settings only.");
                    break;
                case "Custom":
                    if (o["omaName"].Length == 0 || o["omaUri"].Length == 0 || o["omaValue"].Length == 0) e.Add("A custom profile needs a setting name, an OMA-URI and a value.");
                    else if (!o["omaUri"].StartsWith("./", StringComparison.Ordinal)) e.Add("An OMA-URI starts with ./Device/ or ./User/.");
                    if (o.Is("omaType", "Integer") && o["omaValue"].Length > 0 && !Regex.IsMatch(o["omaValue"], "^-?\\d+$")) e.Add("The value must be a whole number for the Integer data type.");
                    break;
                case "Kiosk":
                    if (o.Is("kioskApp", "Microsoft Edge browser") && o["startUrl"].Length == 0) e.Add("Start URL is required for a Microsoft Edge kiosk.");
                    break;
                case "Domain Join":
                    if (o["prefix"].Length == 0 || o["domain"].Length == 0) e.Add("Computer name prefix and Domain name are required.");
                    else if (o["prefix"].Length > 15 || !Regex.IsMatch(o["prefix"], "^[A-Za-z0-9-]+$")) e.Add("Computer name prefix: letters, digits and hyphens, shorter than 15 characters.");
                    break;
                case "Wi-Fi":
                    if (o["ssid"].Length == 0) e.Add("Network name (SSID) is required.");
                    if (o["wifiSecurity"].Contains("Personal") && (o["wifiKey"].Length < 8 || o["wifiKey"].Length > 63)) e.Add("The pre-shared key must be 8 to 63 characters.");
                    break;
            }
        }

        static void App(EpTenant t, EpObj o, List<string> e)
        {
            if (o.Is("type", "Microsoft Store app (new)") && o["storeId"].Length > 0 && EpCatalog.StoreApp(o["storeId"]) == null) e.Add("Package identifier: search the store and pick an app (for example Company Portal).");
            if (o.Is("type", "Microsoft Store app (new)") && EpCatalog.StoreApp(o["storeId"]) != null) o["storeId"] = EpCatalog.StoreApp(o["storeId"]).Id;
            if (!o.Is("type", "Windows app (Win32)")) return;
            if (o["package"].Length > 0 && EpCatalog.Package(o["package"]) == null) e.Add("App package file: pick one of the .intunewin files.");
            switch (o["detect"])
            {
                case "MSI": if (!Regex.IsMatch(o["msiCode"], "^\\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\\}$")) e.Add("MSI product code must look like {6B1A3C55-2F0E-4C7A-9D11-0A7E5B3C2D41}."); break;
                case "File": if (o["detectPath"].Length == 0 || o["detectName"].Length == 0) e.Add("A file detection rule needs a path and a file or folder."); break;
                case "Registry": if (o["detectPath"].Length == 0) e.Add("A registry detection rule needs a key path."); break;
            }
            foreach (var d in o.List("dependsOn"))
            {
                var a = t.Find("app", d);
                if (a == null) e.Add("Dependencies: there is no app called '" + d + "'.");
                else if (!a.Is("type", "Windows app (Win32)")) e.Add("Dependencies must be Win32 apps.");
                else if (EpText.Eq(a.Name, o.Name)) e.Add("An app can't depend on itself.");
            }
            if (o["supersedes"].Length > 0)
            {
                var a = t.Find("app", o["supersedes"]);
                if (a == null) e.Add("Supersedence: there is no app called '" + o["supersedes"] + "'.");
                else if (EpText.Eq(a.Name, o.Name)) e.Add("An app can't supersede itself.");
            }
        }
    }
}
