// Cert Lab - creating, editing, assigning and deleting objects in the pretend tenant (the portal's Save, Assign
// and Delete buttons, and the course's setup and solution lines).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public static partial class EpOps
    {
        /// <summary>Puts values on an object: choices in their canonical spelling, lists one per line, +key adds to a list, -key removes.</summary>
        static void Apply(EpObj o, IDictionary<string, string> values)
        {
            var k = EpSchemas.Get(o.Kind);
            foreach (var kv in values)
            {
                string key = kv.Key, val = kv.Value ?? "";
                bool add = key.StartsWith("+", StringComparison.Ordinal), rem = key.StartsWith("-", StringComparison.Ordinal) && key.Length > 1;
                if (add || rem) key = key.Substring(1);
                if (EpText.Eq(key, "name")) { o.Name = val.Trim(); continue; }
                var f = k == null ? null : k.Field(key);
                if (key.StartsWith("s:", StringComparison.OrdinalIgnoreCase))
                {
                    var s = EpCatalog.Find(key.Substring(2));
                    if (s != null) { key = "s:" + s.Key; if (s.Type == "choice") { var c = s.Choices.FirstOrDefault(x => EpText.Eq(x, val)); if (c != null) val = c; } }
                }
                else if (f != null && f.Key != key) key = f.Key;
                if (f != null && (f.Type == "choice"))
                {
                    var c = f.Options.FirstOrDefault(x => EpText.Eq(x, val.Trim()));
                    if (c != null) val = c;
                }
                if (f != null && (f.Type == "multi" || f.Type == "groups" || f.Type == "apps" || f.Type == "members" || f.Type == "people" || f.Type == "cloudapps") || add || rem)
                {
                    var parts = val.Split(new[] { '\n', ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                    if (f != null && f.Type == "multi") parts = parts.Select(x => f.Options.FirstOrDefault(y => EpText.Eq(y, x)) ?? x).ToList();
                    if (add || rem)
                    {
                        var cur = o.List(key);
                        foreach (var p in parts) { if (add && !cur.Any(x => EpText.Eq(x, p))) cur.Add(p); if (rem) cur.RemoveAll(x => EpText.Eq(x, p)); }
                        parts = cur;
                    }
                    val = EpText.Join(parts);
                }
                if (val.Length == 0) o.P.Remove(key); else o.P[key] = val;
            }
        }

        static EpObj Blank(EpTenant t, string kind)
        {
            EpObj o = kind == "device" ? new EpDevice() : new EpObj();
            o.Kind = kind; o.T = t;
            return o;
        }

        public static EpResult Create(EpTenant t, string kind, IDictionary<string, string> values)
        {
            var k = EpSchemas.Get(kind);
            if (k == null && kind != "device") return EpResult.Fail("Unknown kind of object: " + kind + ".");
            var o = Blank(t, kind);
            Apply(o, values);
            if (k != null)
                foreach (var f in k.Fields)
                    if (f.Default.Length > 0 && o.Get(f.Key).Length == 0 && f.Shows(x => o.Get(x))) o.P[f.Key] = f.Default;
            if (kind == "user" && o.Get("alias").Length == 0 && o["upn"].Contains("@")) o["alias"] = o["upn"].Substring(0, o["upn"].IndexOf('@'));
            var errs = EpValidate.Check(t, o, null);
            if (errs.Count > 0) { var r = new EpResult { Ok = false }; r.Errors.AddRange(errs); return r; }
            t.Add(o);
            t.Log("Created " + (k == null ? kind : k.Title.ToLowerInvariant()) + " '" + o.Name + "'.");
            return new EpResult { Obj = o, Message = "Created " + (k == null ? kind : k.Title.ToLowerInvariant()) + " '" + o.Name + "'." };
        }

        public static EpResult Update(EpTenant t, EpObj o, IDictionary<string, string> values)
        {
            var copy = Blank(t, o.Kind);
            copy.Name = o.Name; copy.Id = o.Id;
            foreach (var kv in o.P) copy.P[kv.Key] = kv.Value;
            Apply(copy, values);
            var k = EpSchemas.Get(o.Kind);
            if (k != null) foreach (var f in k.Fields) if (f.Default.Length > 0 && copy.Get(f.Key).Length == 0 && f.Shows(x => copy.Get(x))) copy.P[f.Key] = f.Default;
            var errs = EpValidate.Check(t, copy, o);
            if (errs.Count > 0) { var r = new EpResult { Ok = false }; r.Errors.AddRange(errs); return r; }
            string oldName = o.Name;
            o.P.Clear(); foreach (var kv in copy.P) o.P[kv.Key] = kv.Value;
            if (oldName != copy.Name)
            {
                o.Name = copy.Name;
                if (o.Kind == "group") foreach (var x in t.All) foreach (var a in x.Assignments) if (EpText.Eq(a.Group, oldName)) a.Group = copy.Name;
                if (o.Kind == "filter") foreach (var x in t.All) foreach (var a in x.Assignments) if (EpText.Eq(a.Filter, oldName)) a.Filter = copy.Name;
            }
            o.Modified = t.Now;
            t.Log("Changed " + (k == null ? o.Kind : k.Title.ToLowerInvariant()) + " '" + o.Name + "'.");
            return new EpResult { Obj = o, Message = "Saved " + (k == null ? o.Kind : k.Title.ToLowerInvariant()) + " '" + o.Name + "'." };
        }

        static string PlatformOfFilter(EpObj f) { var p = f["platform"]; return p.StartsWith("Windows", StringComparison.Ordinal) ? "Windows" : p.StartsWith("iOS", StringComparison.Ordinal) ? "iOS" : p.StartsWith("Android", StringComparison.Ordinal) ? "Android" : ""; }

        public static EpResult Assign(EpTenant t, EpObj o, EpAssign a)
        {
            var k = EpSchemas.Get(o.Kind);
            if (k == null || !k.Assignable) return EpResult.Fail((k == null ? o.Kind : k.Plural) + " aren't assigned to groups" + (o.Kind == "ca" ? ": a Conditional Access policy names its users and groups in Users: include / exclude." : "."));
            bool virt = EpText.Eq(a.Group, EpTenant.AllUsers) || EpText.Eq(a.Group, EpTenant.AllDevices);
            var g = t.Group(a.Group);
            if (!virt && g == null) return EpResult.Fail("There is no group called '" + a.Group + "'.");
            if (g != null) a.Group = g.Name; else a.Group = EpText.Eq(a.Group, EpTenant.AllUsers) ? EpTenant.AllUsers : EpTenant.AllDevices;
            if (!k.Intents.Contains(a.Intent)) return EpResult.Fail("For " + k.Plural.ToLowerInvariant() + " an assignment is " + string.Join(", ", k.Intents) + ", not " + a.Intent + ".");
            if (virt && a.Exclude) return EpResult.Fail(a.Group + " can't be excluded.");
            string holds = t.GroupHolds(a.Group);
            if ((o.Kind == "autopilotProfile" || o.Kind == "featureUpdate" || o.Kind == "qualityUpdate" || o.Kind == "driverUpdate") && holds == "user")
                return EpResult.Fail(k.Plural + " are assigned to device groups; " + a.Group + " holds users.");
            if ((o.Kind == "appProtection" || o.Kind == "restriction") && holds == "device")
                return EpResult.Fail(k.Plural + " are assigned to user groups; " + a.Group + " holds devices.");
            if (o.Kind == "app" && a.Intent == "available" && holds == "device")
                return EpResult.Fail("Available for enrolled devices is assigned to user groups (the user picks the app in Company Portal); " + a.Group + " holds devices.");
            if (a.Filter.Length > 0)
            {
                if (a.Exclude) return EpResult.Fail("Filters can't be used on excluded groups.");
                var f = t.Find("filter", a.Filter);
                if (f == null) return EpResult.Fail("There is no filter called '" + a.Filter + "'.");
                string pl = EpTenant.PlatformOf(o);
                if (pl.Length > 0 && !EpText.Eq(PlatformOfFilter(f), pl)) return EpResult.Fail("The filter " + f.Name + " is for " + f["platform"] + ", and this is for " + pl + ".");
                a.Filter = f.Name;
                if (a.FilterMode != "include" && a.FilterMode != "exclude") return EpResult.Fail("A filter is used in include or exclude mode.");
            }
            else a.FilterMode = "";
            o.Assignments.RemoveAll(x => EpText.Eq(x.Group, a.Group));
            o.Assignments.Add(a);
            o.Modified = t.Now;
            var res = new EpResult { Obj = o, Message = "Assigned " + k.Title.ToLowerInvariant() + " '" + o.Name + "' to " + a.Group + " (" + a.Intent + (a.Filter.Length > 0 ? ", filter " + a.Filter + " in " + a.FilterMode + " mode" : "") + "). Devices get it at their next check-in." };
            res.Warnings.AddRange(t.MixedWarnings(o));
            t.Log(res.Message);
            return res;
        }

        public static EpResult Unassign(EpTenant t, EpObj o, string group)
        {
            int n = o.Assignments.RemoveAll(x => EpText.Eq(x.Group, group));
            if (n == 0) return EpResult.Fail("'" + o.Name + "' isn't assigned to " + group + ".");
            t.Log("Removed the assignment of '" + o.Name + "' to " + group + ".");
            return new EpResult { Obj = o, Message = "Removed the assignment to " + group + "." };
        }

        public static EpResult Delete(EpTenant t, EpObj o)
        {
            if (o.Kind == "tenant" || o is EpDevice) return EpResult.Fail("Devices are removed with the Delete remote action.");
            if (o.Kind == "group")
            {
                var users = t.All.Where(x => x.Assignments.Any(a => EpText.Eq(a.Group, o.Name))).Select(x => x.Name).ToList();
                if (users.Count > 0) return EpResult.Fail("The group is still assigned to: " + string.Join(", ", users.ToArray()) + ". Remove those assignments first.");
            }
            t.Remove(o);
            t.Log("Deleted '" + o.Name + "'.");
            return new EpResult { Message = "Deleted '" + o.Name + "'." };
        }

        public static EpResult Members(EpTenant t, EpObj g, bool add, List<string> names)
        {
            if (!g.Is("membership", "Assigned")) return EpResult.Fail("Members of a dynamic group come from its rule; you can't add or remove them by hand.");
            var cur = g.List("members");
            foreach (var n in names)
            {
                EpObj who = t.User(n) ?? (EpObj)t.Device(n) ?? t.Group(n);
                if (who == null) return EpResult.Fail("There is no user, device or group called '" + n + "'.");
                if (who == g) return EpResult.Fail("A group can't be a member of itself.");
                if (who is EpDevice && !((EpDevice)who).InDirectory) return EpResult.Fail(who.Name + " isn't in Microsoft Entra ID yet, so it can't be added to a group.");
                string id = who.Kind == "user" ? who["alias"] : who.Name;
                if (add) { if (!cur.Any(x => EpText.Eq(x, id))) cur.Add(id); }
                else cur.RemoveAll(x => EpText.Eq(x, id) || EpText.Eq(x, who.Name));
            }
            g["members"] = EpText.Join(cur);
            t.Log((add ? "Added " : "Removed ") + string.Join(", ", names.ToArray()) + (add ? " to " : " from ") + g.Name + ".");
            return new EpResult { Obj = g, Message = (add ? "Added to " : "Removed from ") + g.Name + "." };
        }

        /// <summary>Devices > Enrollment > Windows Autopilot > Devices > Import: the CSV from Get-WindowsAutopilotInfo.</summary>
        public static EpResult ImportCsv(EpTenant t, string device, string path, IDictionary<string, string> extra)
        {
            var d = t.Device(device);
            var m = t.MachineFor(d);
            var f = m == null ? null : m.FindFile(path);
            if (f == null) return EpResult.Fail("Cert Lab can't find " + path + " on " + device + ". Make it there with Get-WindowsAutopilotInfo -OutputFile.");
            var lines = (f.Text ?? "").Replace("\r", "").Split('\n').Where(x => x.Trim().Length > 0).ToList();
            if (lines.Count < 2 || !lines[0].StartsWith("Device Serial Number,Windows Product ID,Hardware Hash", StringComparison.Ordinal)) return EpResult.Fail("The file isn't a hardware hash CSV: its first line must be the header Device Serial Number,Windows Product ID,Hardware Hash.");
            var head = lines[0].Split(',').ToList();
            var res = new EpResult();
            foreach (var row in lines.Skip(1))
            {
                var c = row.Split(',');
                string serial = c[0].Trim('"');
                string v;
                string tag = extra.TryGetValue("groupTag", out v) ? v : (head.IndexOf("Group Tag") >= 0 && head.IndexOf("Group Tag") < c.Length ? c[head.IndexOf("Group Tag")].Trim('"') : "");
                string user = extra.TryGetValue("user", out v) ? v : "";
                var why = t.ImportAutopilot(serial, tag, user);
                if (why != null) return EpResult.Fail(why);
                res.Message = "Imported " + serial + ". Profile assignment follows the device's groups and takes a few minutes.";
            }
            return res;
        }
    }
}
