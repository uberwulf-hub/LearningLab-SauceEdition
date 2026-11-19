// Cert Lab - what reaches a device in the pretend tenant: assignment targeting (include and exclude groups, user
// and device groups, filters in include or exclude mode), platforms, and the device check-in (EpTenant.Sync) that
// applies configuration (with conflicts), endpoint security, updates, scripts, apps, LAPS and BitLocker, then
// works out compliance. The rules follow Intune's documented behaviour, simplified where the course says so.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public partial class EpTenant
    {
        /// <summary>The platform an object is for: Windows, iOS, Android or "" (any).</summary>
        public static string PlatformOf(EpObj o)
        {
            string p = o.Get("platform");
            if (o.Kind == "app")
            {
                string t = o["type"];
                if (t.StartsWith("iOS", StringComparison.Ordinal)) return "iOS";
                if (t.StartsWith("Managed Google Play", StringComparison.Ordinal)) return "Android";
                if (t == "Web link") return "";
                return "Windows";
            }
            if (o.Kind == "appConfig") return o.Is("enrollment", "Managed apps") ? "" : (p.StartsWith("iOS", StringComparison.Ordinal) ? "iOS" : "Android");
            if (p.Length == 0)
                return o.Kind == "appProtection" || o.Kind == "filter" || o.Kind == "ca" || o.Kind == "group" || o.Kind == "user" || o.Kind == "location" || o.Kind == "restriction" ? "" : "Windows";
            if (p.StartsWith("Windows", StringComparison.Ordinal)) return "Windows";
            if (p.StartsWith("iOS", StringComparison.Ordinal)) return "iOS";
            if (p.StartsWith("Android", StringComparison.Ordinal)) return "Android";
            if (p.StartsWith("macOS", StringComparison.Ordinal)) return "macOS";
            return "";
        }

        /// <summary>Does an assignment's group hold this device (device groups) or the device's primary user (user groups)?
        /// Returns "device", "user" or null.</summary>
        string Via(EpAssign a, EpDevice d)
        {
            if (d.InDirectory && InGroup(a.Group, d)) return "device";
            var u = User(d.PrimaryUser);
            if (u != null && InGroup(a.Group, u)) return "user";
            return null;
        }

        /// <summary>Is the object targeted at the device? hit is the include assignment that matched; why explains.</summary>
        public bool Targets(EpObj o, EpDevice d, out EpAssign hit, out string why)
        {
            hit = null; why = "";
            string plat = PlatformOf(o);
            if (plat.Length > 0 && !EpText.Eq(plat, d["os"])) { why = "for " + plat + " devices"; return false; }
            foreach (var a in o.Assignments.Where(x => !x.Exclude))
            {
                string via = Via(a, d);
                if (via == null) continue;
                // exclusions count only when they are the same kind of group (users with users, devices with devices)
                var ex = o.Assignments.Where(x => x.Exclude).FirstOrDefault(x => Via(x, d) == via);
                if (ex != null) { why = "excluded by " + ex.Group; return false; }
                if (a.Filter.Length > 0)
                {
                    var f = Find("filter", a.Filter);
                    bool m = f != null && EpRules.Matches(f["rule"], "filter", p => FilterProp(d, p));
                    bool want = !EpText.Eq(a.FilterMode, "exclude");
                    if (m != want) { why = "filter " + a.Filter + (want ? " doesn't match" : " matches (exclude mode)"); continue; }
                }
                hit = a; why = "through " + a.Group + (a.Filter.Length > 0 ? " with filter " + a.Filter : "");
                return true;
            }
            if (why.Length == 0) why = "not assigned to a group this device or its user is in";
            return false;
        }
        public bool Targets(EpObj o, EpDevice d) { EpAssign a; string w; return Targets(o, d, out a, out w); }

        /// <summary>Is the object (an app protection policy, a restriction) targeted at the user?</summary>
        public bool TargetsUser(EpObj o, EpObj user)
        {
            if (user == null) return false;
            bool inc = o.Assignments.Any(a => !a.Exclude && InGroup(a.Group, user));
            return inc && !o.Assignments.Any(a => a.Exclude && InGroup(a.Group, user));
        }

        /// <summary>Exclusions that Intune would ignore because they mix user and device groups (the portal warns).</summary>
        public List<string> MixedWarnings(EpObj o)
        {
            var l = new List<string>();
            var inc = o.Assignments.Where(a => !a.Exclude).Select(a => GroupHolds(a.Group)).Where(x => x.Length > 0).Distinct().ToList();
            foreach (var ex in o.Assignments.Where(a => a.Exclude))
            {
                string h = GroupHolds(ex.Group);
                if (h.Length > 0 && inc.Count > 0 && !inc.Contains(h)) l.Add("Excluding the " + h + " group " + ex.Group + " from " + (inc[0] == "user" ? "user" : "device") + " groups isn't supported: the exclusion is ignored.");
            }
            return l;
        }

        // ------------------------------------------------------------ check-in
        /// <summary>A device checks in: Intune sends it everything assigned, it reports back.</summary>
        public void Sync(EpDevice d)
        {
            if (!d.Enrolled || d.Is("state", "off") || d.Is("state", "oobe")) return;
            d.LastSync = Now; d.NextSync = Now.AddHours(8);
            d.Note("Check-in with Intune.");
            d.Policies.Clear();
            // subscription activation: Windows Pro steps up to Enterprise for a user with a Windows Enterprise E3 / E5 licence
            if (d.Windows && d.Is("edition", "Pro") && d.EntraJoined && Licensed(User(d.PrimaryUser), "winent"))
            {
                d["edition"] = "Enterprise"; d["activation"] = "Subscription activation";
                d.Note("Subscription activation: Windows Pro stepped up to Enterprise (the user has a Windows Enterprise licence).");
                var sm = MachineFor(d); if (sm != null) { sm.OsCaption = sm.OsCaption.Replace(" Pro", " Enterprise"); sm.OsEdition = "Enterprise"; }
            }
            ApplyConfiguration(d);
            if (d.Windows) { ApplySecurity(d); ApplyUpdates(d); RunScripts(d); }
            ApplyApps(d);
            if (d.Windows) ApplyLaps(d);
            Evaluate(d);
        }

        void State(EpDevice d, EpObj o, string status, string detail)
        {
            d.Policies.RemoveAll(p => p.Policy == o.Name && p.Kind == o.Kind);
            d.Policies.Add(new EpPolicyState { Policy = o.Name, Kind = o.Kind, Status = status, Detail = detail ?? "" });
        }

        /// <summary>The settings an object sends: key -> value (settings catalog keys, and the keys endpoint
        /// security and custom profiles map onto).</summary>
        public static Dictionary<string, string> SettingsOf(EpObj o)
        {
            var s = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (o.Kind == "config")
            {
                foreach (var kv in o.P) if (kv.Key.StartsWith("s:", StringComparison.OrdinalIgnoreCase) && kv.Value.Length > 0) s[kv.Key.Substring(2)] = kv.Value;
                if (o.Is("type", "Custom"))
                {
                    const string pre = "./Device/Vendor/MSFT/Policy/Config/";
                    string uri = o["omaUri"];
                    if (uri.StartsWith(pre, StringComparison.OrdinalIgnoreCase))
                    {
                        var key = uri.Substring(pre.Length);
                        var cat = EpCatalog.Find(key);
                        if (cat != null)
                        {
                            string disp = cat.Type == "choice" ? cat.Choices.FirstOrDefault(c => cat.Data(c) == o["omaValue"]) : o["omaValue"];
                            s[cat.Key] = disp ?? o["omaValue"];
                        }
                        else s[key] = o["omaValue"];
                    }
                }
            }
            if (o.Kind == "av" && o["rtp"].Length > 0 && !o.Is("rtp", "Not configured")) s["Defender/AllowRealtimeMonitoring"] = o["rtp"];
            return s;
        }

        void ApplyConfiguration(EpDevice d)
        {
            var targeted = All.Where(o => (o.Kind == "config" || o.Kind == "av") && Targets(o, d)).ToList();
            var from = new Dictionary<string, List<KeyValuePair<EpObj, string>>>(StringComparer.OrdinalIgnoreCase);
            foreach (var o in targeted)
                foreach (var kv in SettingsOf(o))
                {
                    List<KeyValuePair<EpObj, string>> l;
                    if (!from.TryGetValue(kv.Key, out l)) { l = new List<KeyValuePair<EpObj, string>>(); from[kv.Key] = l; }
                    l.Add(new KeyValuePair<EpObj, string>(o, kv.Value));
                }
            var conflicted = new HashSet<EpObj>();
            var keep = new Dictionary<string, EpApplied>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in from)
            {
                var vals = kv.Value.Select(x => x.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (vals.Count > 1)
                {
                    foreach (var x in kv.Value) conflicted.Add(x.Key);
                    d.Note("Conflict: " + kv.Key + " is set to different values by " + string.Join(" and ", kv.Value.Select(x => x.Key.Name).ToArray()) + "; it isn't applied.");
                    continue;
                }
                keep[kv.Key] = new EpApplied { Key = kv.Key, Value = vals[0], From = kv.Value[0].Key.Name };
            }
            // settings no longer sent stay as they are on Windows (tattooing) for most CSPs; Cert Lab removes them
            foreach (var old in d.Applied.Keys.ToList()) if (!keep.ContainsKey(old)) { d.Applied.Remove(old); WriteSetting(d, old, null); }
            foreach (var a in keep.Values) { d.Applied[a.Key] = a; WriteSetting(d, a.Key, a.Value); }
            foreach (var o in targeted.Where(x => x.Kind == "config"))
            {
                string why = ConfigEffect(d, o);
                State(d, o, conflicted.Contains(o) ? "Conflict" : why == null ? "Succeeded" : "Error", conflicted.Contains(o) ? "Another profile sets the same setting to a different value." : why);
            }
            // effects of the applied settings
            string rtp = d.Setting("Defender/AllowRealtimeMonitoring");
            if (rtp.Length > 0) d["rtp"] = EpText.Eq(rtp, "Not allowed") ? "Off" : "On";
            string tel = d.Setting("System/AllowTelemetry");
            if (tel.Length > 0) d["telemetry"] = tel;
        }

        /// <summary>Writes (or removes) a setting under PolicyManager on the device, where Windows keeps MDM policies.</summary>
        void WriteSetting(EpDevice d, string key, string display)
        {
            var m = MachineFor(d);
            if (m == null || key.IndexOf('/') < 0) return;
            var cat = EpCatalog.Find(key);
            string area = key.Substring(0, key.IndexOf('/')), name = key.Substring(key.IndexOf('/') + 1);
            string path = cat != null && cat.RegPath.Length > 0 ? cat.RegPath : "SOFTWARE\\Microsoft\\PolicyManager\\current\\device\\" + area;
            var k = m.HKLM.Sub(path);
            if (display == null) { k.Values.RemoveAll(v => EpText.Eq(v.Name, name)); return; }
            string data = cat == null ? display : (cat.Data(display) ?? display);
            int n;
            if (int.TryParse(data, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) k.D(name, n); else k.S(name, data);
        }

        /// <summary>A profile's effect beyond plain settings (kiosk, Wi-Fi, edition upgrade, domain join...): null if fine,
        /// else the error the device reports.</summary>
        string ConfigEffect(EpDevice d, EpObj o)
        {
            switch (o["type"])
            {
                case "Kiosk":
                    if (!d.Is("edition", "Enterprise") && !d.Is("edition", "Education") && !d.Is("edition", "Pro")) return "Kiosk mode needs Windows Pro, Enterprise or Education.";
                    d["kiosk"] = o["kioskMode"] + ": " + o["kioskApp"] + (o["startUrl"].Length > 0 ? " (" + o["startUrl"] + ")" : "");
                    return null;
                case "Wi-Fi":
                    var l = d.List("wifi"); if (!l.Contains(o["ssid"])) l.Add(o["ssid"]); d["wifi"] = EpText.Join(l);
                    return null;
                case "Edition upgrade and mode switch":
                    if (d.Is("edition", "Home")) return "Home can't be upgraded by this profile.";
                    if (o["edition"].Contains("Enterprise")) d["edition"] = "Enterprise";
                    else if (o["edition"].Contains("Education")) d["edition"] = "Education";
                    return null;
                case "Domain Join":
                    return null;   // used during hybrid Autopilot only
            }
            return null;
        }
    }
}
