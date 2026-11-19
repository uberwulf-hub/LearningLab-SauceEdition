// Cert Lab - Windows updates in the pretend tenant: a monthly cumulative update every second Tuesday (Windows 10
// 22H2 gets none after October 2025, its end of support), update rings (deferral, pause), feature updates
// policies (the version to move to, a Windows 10 fallback for PCs that can't run Windows 11), expedited quality
// updates; and platform scripts and remediations, which really run in the device's simulated PowerShell.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public partial class EpTenant
    {
        /// <summary>The second Tuesday of a month: when the monthly cumulative update comes out.</summary>
        public static DateTime PatchTuesday(int year, int month)
        {
            var d = new DateTime(year, month, 1, 10, 0, 0);
            while (d.DayOfWeek != DayOfWeek.Tuesday) d = d.AddDays(1);
            return d.AddDays(7);
        }
        /// <summary>The newest monthly update ("yyyy-MM") released at least `deferDays` days before now.</summary>
        public string LatestPatch(int deferDays)
        {
            var t = Now.AddDays(-deferDays);
            var d = new DateTime(t.Year, t.Month, 1);
            if (t < PatchTuesday(t.Year, t.Month)) d = d.AddMonths(-1);
            return d.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        }
        static int MonthIndex(string patch) { DateTime d; return DateTime.TryParseExact(patch + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d) ? (d.Year - 2025) * 12 + d.Month - 10 : 0; }
        /// <summary>The build number shown for a feature version and monthly update (made up, steady steps).</summary>
        public static string BuildFor(string feature, string patch)
        {
            int idx = Math.Max(0, MonthIndex(patch));
            switch (feature)
            {
                case "25H2": return "10.0.26200." + (7520 + 80 * idx);
                case "24H2": return "10.0.26100." + (7520 + 80 * idx);
                case "23H2": return "10.0.22631." + (6060 + 80 * idx);
                default: return "10.0.19045." + (6456 + 80 * Math.Min(idx, 0));
            }
        }
        static int FeatureRank(string v) { return v == "25H2" ? 4 : v == "24H2" ? 3 : v == "23H2" ? 2 : 1; }
        static string FeatureOf(string policyVersion) { return policyVersion.Contains("25H2") ? "25H2" : policyVersion.Contains("24H2") ? "24H2" : policyVersion.Contains("23H2") ? "23H2" : "22H2"; }
        public static bool IsWin11(string feature) { return feature != "22H2"; }

        void ApplyUpdates(EpDevice d)
        {
            var rings = TargetedOf(d, "ring").ToList();
            EpObj ring = null;
            if (rings.Count > 1) foreach (var r in rings) State(d, r, "Conflict", "More than one update ring targets this device.");
            else if (rings.Count == 1) { ring = rings[0]; State(d, ring, "Succeeded", null); }
            d["ring"] = ring == null ? "" : ring.Name;
            var m = MachineFor(d);
            if (ring != null && m != null)
            {
                var k = m.HKLM.Sub("SOFTWARE\\Microsoft\\PolicyManager\\current\\device\\Update");
                k.D("DeferQualityUpdatesPeriodInDays", ring.Int("qualityDeferral", 0));
                k.D("DeferFeatureUpdatesPeriodInDays", ring.Int("featureDeferral", 0));
                k.D("PauseQualityUpdates", ring.Is("pausedQuality", "Yes") ? 1 : 0);
                k.D("PauseFeatureUpdates", ring.Is("pausedFeature", "Yes") ? 1 : 0);
            }
            // feature version first
            var fus = TargetedOf(d, "featureUpdate").ToList();
            string feature = d["featureVersion"];
            if (fus.Count > 0)
            {
                var fu = fus.OrderByDescending(x => FeatureRank(FeatureOf(x["version"]))).First();
                foreach (var x in fus) State(d, x, x == fu ? "Succeeded" : "Not applicable", x == fu ? null : "Another feature updates policy wins.");
                string want = FeatureOf(fu["version"]);
                bool paused = ring != null && ring.Is("pausedFeature", "Yes");
                if (IsWin11(want) && !d.Yes("win11Capable"))
                {
                    State(d, fu, "Not applicable", "This device doesn't meet the Windows 11 hardware requirements (TPM 2.0, Secure Boot, a supported processor), so Windows Update doesn't offer it Windows 11.");
                }
                else if (FeatureRank(want) > FeatureRank(feature) && !paused) Upgrade(d, want);
            }
            else if (ring != null && ring.Is("upgradeWin11", "Yes") && feature == "22H2" && d.Yes("win11Capable") && !ring.Is("pausedFeature", "Yes")) Upgrade(d, "25H2");
            feature = d["featureVersion"];
            // the monthly update
            string patch = d["patch"];
            if (feature == "22H2") { d["osVersion"] = BuildFor(feature, patch); d["patchNote"] = "Windows 10 reached its end of support in October 2025: no more monthly updates."; return; }
            d["patchNote"] = "";
            int defer = ring == null ? 0 : ring.Int("qualityDeferral", 0);
            string target = ring != null && ring.Is("pausedQuality", "Yes") ? patch : LatestPatch(defer);
            foreach (var q in TargetedOf(d, "qualityUpdate"))
            {
                string exp = q["expediteBelow"].Substring(0, 7);
                if (string.CompareOrdinal(patch, exp) < 0 && string.CompareOrdinal(LatestPatch(0), exp) >= 0) { if (string.CompareOrdinal(target, exp) < 0) target = exp; d.Note("Expedited quality update " + exp + "."); }
                State(d, q, "Succeeded", null);
            }
            if (string.CompareOrdinal(target, patch) > 0) { d["patch"] = target; d.Note("Installed the " + target + " cumulative update."); AddHotfix(d, target); }
            d["osVersion"] = BuildFor(feature, d["patch"]);
            if (m != null) { m.OsVersion = d["osVersion"].Substring(0, d["osVersion"].LastIndexOf('.')); }
        }

        void Upgrade(EpDevice d, string to)
        {
            d.Note("Feature update: upgraded from " + d["featureVersion"] + " to Windows 11, version " + to + ".");
            d["featureVersion"] = to;
            if (d.Is("patch", "2025-10") || string.CompareOrdinal(d["patch"], LatestPatch(0)) < 0) d["patch"] = LatestPatch(0);
            d["osVersion"] = BuildFor(to, d["patch"]);
            var m = MachineFor(d);
            if (m != null) { m.OsCaption = "Microsoft Windows 11 " + d["edition"]; m.OsBuild = to == "25H2" ? "26200" : to == "24H2" ? "26100" : "22631"; m.OsDisplayVersion = to; m.OsVersion = "10.0." + m.OsBuild; }
        }

        void AddHotfix(EpDevice d, string patch)
        {
            var m = MachineFor(d);
            if (m == null) return;
            m.Hotfixes.Add(new WinHotfix { Id = "KB50" + (40000 + MonthIndex(patch) * 37 + (d["featureVersion"] == "23H2" ? 3 : 0)).ToString(CultureInfo.InvariantCulture), Description = "Security Update", On = Now });
        }

        // ------------------------------------------------------------ scripts
        void RunScripts(EpDevice d)
        {
            var m = MachineFor(d);
            foreach (var s in TargetedOf(d, "script"))
            {
                string id = s.Name + "#" + FwHash.Of(s["content"]);
                if (d.ScriptsRun.Contains(id)) { State(d, s, d.Get("script:" + s.Name), null); continue; }
                d.ScriptsRun.Add(id);
                string err = s.Is("enforceSig", "Yes") ? "The script isn't signed by a trusted publisher, and the signature check is on." : null;
                int code = 0;
                if (err == null && m != null) err = RunPs(m, s["content"], out code);
                string st = err == null && code == 0 ? "Succeeded" : "Error";
                d["script:" + s.Name] = st;
                State(d, s, st, err ?? (code != 0 ? "The script exited with code " + code + "." : null));
                d.Note("Platform script '" + s.Name + "': " + (st == "Succeeded" ? "ran successfully" : "failed") + ".");
            }
            foreach (var r in TargetedOf(d, "remediation"))
            {
                if (!Licensed(User(d.PrimaryUser), "winent") && d.PrimaryUser.Length > 0) { State(d, r, "Error", "Remediations need Windows Enterprise E3 or E5 (or Education A3 / A5) licences for the users."); continue; }
                if (r.Is("schedule", "Once") && d.ScriptsRun.Contains("rem:" + r.Name)) { State(d, r, d.Get("rem:" + r.Name), null); continue; }
                d.ScriptsRun.Add("rem:" + r.Name);
                int code = 0;
                string err = m == null ? null : RunPs(m, r["detect"], out code);
                string st;
                if (err != null) st = "Error";
                else if (code == 0) st = "Without issues";
                else if (r["remediate"].Length == 0) st = "With issues";
                else
                {
                    int c2; string e2 = RunPs(m, r["remediate"], out c2);
                    int c3; RunPs(m, r["detect"], out c3);
                    st = e2 == null && c3 == 0 ? "Issue fixed" : "Recurred";
                }
                d["rem:" + r.Name] = st;
                State(d, r, st, err);
                d.Note("Remediation '" + r.Name + "': " + st + ".");
            }
        }

        /// <summary>Runs PowerShell on the device as SYSTEM would; null when it ran, else the error. code: the exit code.</summary>
        public static string RunPs(PsMachine m, string script, out int code)
        {
            code = 0;
            var r = new PsRunspace(m, new NullHost(), "Administrator");
            var errs = new List<string>();
            var st = new Streams { Err = e => errs.Add(PsRunspace.ErrorText(e)) };
            r.RunScript(script ?? "", "", st);
            m.Sessions.Remove(r);
            code = r.ExitRequested ? r.LastExit : r.Terminated ? 1 : 0;
            if (r.Terminated) return errs.Count > 0 ? errs[errs.Count - 1].Trim() : "The script stopped with an error.";
            return null;
        }
    }
}
