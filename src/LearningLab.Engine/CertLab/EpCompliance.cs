// Cert Lab - compliance and Conditional Access in the pretend tenant. A device's compliance comes from every
// compliance policy assigned to it (all must pass), the built-in checks (it has a policy, it checked in within the
// validity period) and the tenant's "no policy" setting; "Mark device noncompliant" after N days gives a grace
// period. Conditional Access evaluates a sign-in against every policy that is on (report-only ones are only
// reported): the user, the cloud app, the platform, the location, the client app, then block or the grant controls.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public partial class EpTenant
    {
        /// <summary>Works out the device's compliance from the policies assigned to it.</summary>
        public void Evaluate(EpDevice d)
        {
            d.ComplianceReasons.Clear();
            if (!d.Enrolled) { d.Compliance = "Not evaluated"; return; }
            var pols = Of("compliance").Where(p => Targets(p, d)).ToList();
            var fails = new List<string>();
            int graceDays = int.MaxValue;
            foreach (var p in pols)
            {
                var r = Reasons(p, d);
                State(d, p, r.Count == 0 ? "Compliant" : "Not compliant", string.Join(" ", r.ToArray()));
                if (r.Count > 0) { fails.AddRange(r.Select(x => p.Name + ": " + x)); graceDays = Math.Min(graceDays, p.Int("noncompliantDays", 0)); }
            }
            if (pols.Count == 0 && Settings.Is("noPolicyMark", "Not compliant")) fails.Add("Default Device Compliance Policy: no compliance policy is assigned, and the tenant marks such devices as not compliant.");
            if (d.LastSync != DateTime.MinValue && (Now - d.LastSync).TotalDays > Settings.Int("validityDays", 30)) fails.Add("Default Device Compliance Policy: the device hasn't checked in within the compliance status validity period.");
            d.ComplianceReasons.AddRange(fails);
            if (fails.Count == 0) { d.Compliance = "Compliant"; d.NoncompliantSince = DateTime.MinValue; return; }
            if (d.NoncompliantSince == DateTime.MinValue) d.NoncompliantSince = Now;
            if (graceDays != int.MaxValue && graceDays > 0 && Now < d.NoncompliantSince.AddDays(graceDays)) d.Compliance = "In grace period";
            else d.Compliance = "Not compliant";
        }

        /// <summary>Why a device fails one compliance policy (empty: it passes).</summary>
        public List<string> Reasons(EpObj p, EpDevice d)
        {
            var r = new List<string>();
            Func<string, bool> req = k => p.Is(k, "Require");
            if (d.Windows)
            {
                if (req("bitlocker") && !d.Is("bitlocker", "On")) r.Add("BitLocker isn't on" + (d.Is("bitlocker", "Encrypting") ? " yet (encrypting)." : "."));
                if (req("secureBoot") && !d.Yes("secureBoot")) r.Add("Secure Boot isn't enabled.");
                if (req("codeIntegrity") && !d.Yes("codeIntegrity")) r.Add("Code integrity isn't on.");
                if (req("tpm") && (d.Is("tpm", "None") || d["tpm"].Length == 0)) r.Add("No TPM.");
                if (req("firewall") && !d.Is("firewall", "On")) r.Add("The firewall is off.");
                if ((req("antivirus") || req("antispyware") || req("defender")) && !d.Is("av", "On")) r.Add("No active antivirus.");
                if (req("rtp") && !d.Is("rtp", "On")) r.Add("Real-time protection is off.");
                if (req("sigUpToDate") && d.Int("sigAgeDays", 0) > 3) r.Add("Microsoft Defender security intelligence is out of date.");
                if (req("password") && !d.Yes("localPassword")) r.Add("No password is set to unlock the device.");
                string risk = p["mdeRisk"];
                if (risk.Length > 0 && risk != "Not configured")
                {
                    if (!d.Is("mde", "Yes")) r.Add("The device isn't onboarded to Microsoft Defender for Endpoint, so it has no risk score.");
                    else if (RiskRank(d["risk"]) > RiskRank(risk)) r.Add("Machine risk score is " + d["risk"] + " (allowed: " + risk + " or lower).");
                }
            }
            if (d.Is("os", "iOS") && p.Is("jailbreak", "Block") && d.Yes("jailbroken")) r.Add("The device is jailbroken.");
            if (d.Is("os", "Android") && p.Is("rooted", "Block") && d.Yes("rooted")) r.Add("The device is rooted.");
            if (!d.Windows && req("password") && !d.Yes("passcode")) r.Add("No passcode.");
            if (d.Is("os", "Android") && p["playIntegrity"].Contains("device integrity") && !d["integrity"].Contains("device")) r.Add("The Play Integrity verdict fails device integrity.");
            if (p["minOs"].Length > 0 && CompareVersion(d["osVersion"], p["minOs"]) < 0) r.Add("OS version " + d["osVersion"] + " is older than the minimum " + p["minOs"] + ".");
            if (p["maxOs"].Length > 0 && CompareVersion(d["osVersion"], p["maxOs"]) > 0) r.Add("OS version " + d["osVersion"] + " is newer than the maximum " + p["maxOs"] + ".");
            return r;
        }
        static int RiskRank(string r) { return r == "High" ? 3 : r == "Medium" ? 2 : r == "Low" ? 1 : 0; }

        // ------------------------------------------------------------ Conditional Access
        static string PlatformName(EpDevice d) { return d == null ? "" : d.Windows ? "Windows" : d["os"]; }

        bool InPeople(string list, EpObj user)
        {
            foreach (var l in EpText.Lines(list))
            {
                if (EpText.Eq(l, "All users")) return true;
                if (User(l) == user) return true;
                if (Group(l) != null && InGroup(l, user)) return true;
            }
            return false;
        }
        bool AppIn(string list, string app)
        {
            foreach (var l in EpText.Lines(list))
            {
                if (EpText.Eq(l, "All cloud apps") || EpText.Eq(l, "All resources") || EpText.Eq(l, app)) return true;
                if (EpText.Eq(l, "Office 365") && app.StartsWith("Office 365", StringComparison.OrdinalIgnoreCase) || EpText.Eq(l, "Office 365") && app == "Microsoft Teams Services") return true;
            }
            return false;
        }
        bool Trusted(string location)
        {
            var loc = Find("location", location);
            return loc != null && loc.Is("trusted", "Yes");
        }

        public EpCaResult EvaluateSignIn(EpSignIn s)
        {
            var res = new EpCaResult();
            var user = User(s.User);
            var dev = Device(s.Device);
            string plat = s.Platform.Length > 0 ? s.Platform : PlatformName(dev);
            if (plat.Length == 0) plat = "Windows";
            string client = (s.Client ?? "browser").ToLowerInvariant();
            string clientName = client.StartsWith("mobile", StringComparison.Ordinal) || client.StartsWith("desktop", StringComparison.Ordinal) ? "Mobile apps and desktop clients" : client.StartsWith("eas", StringComparison.Ordinal) || client.Contains("activesync") ? "Exchange ActiveSync clients" : client.StartsWith("other", StringComparison.Ordinal) || client.StartsWith("legacy", StringComparison.Ordinal) ? "Other clients" : "Browser";
            bool blocked = false, mfaNeeded = false;
            foreach (var p in Of("ca").Where(x => !x.Is("state", "Off")))
            {
                var line = new EpCaLine { Policy = p.Name, ReportOnly = p.Is("state", "Report-only") };
                string why = null;
                if (user == null || !InPeople(p["usersInclude"], user)) why = "the user isn't included";
                else if (InPeople(p["usersExclude"], user)) why = "the user is excluded";
                else if (!AppIn(p["apps"], s.App)) why = "the app isn't targeted";
                else if (p.List("platforms").Count > 0 && !p.List("platforms").Any(x => x == "Any device" || EpText.Eq(x, plat))) why = "the platform isn't included";
                else if (p.List("platformsExclude").Any(x => EpText.Eq(x, plat))) why = "the platform is excluded";
                else if (p.Is("locations", "All trusted locations") && !Trusted(s.Location)) why = "the sign-in isn't from a trusted location";
                else if (p.Is("locationsExclude", "All trusted locations") && Trusted(s.Location)) why = "trusted locations are excluded";
                else if (p.List("clientApps").Count > 0 && !p.List("clientApps").Any(x => EpText.Eq(x, clientName))) why = "the client app isn't included";
                if (why != null) { line.Applies = "Not applied"; line.Why = why; res.Lines.Add(line); continue; }
                line.Applies = "Applies";
                string outcome; var missing = new List<string>();
                if (p.Is("grant", "Block access")) outcome = "Block";
                else
                {
                    var ctl = p.List("controls");
                    var ok = new List<bool>();
                    foreach (var c in ctl)
                    {
                        bool sat;
                        if (c.Contains("multifactor")) { sat = s.Mfa; if (!sat) missing.Add("MFA"); }
                        else if (c.Contains("compliant")) { sat = dev != null && dev.Enrolled && (dev.Compliance == "Compliant" || dev.Compliance == "In grace period"); if (!sat) missing.Add(dev == null ? "a known device" : !dev.Enrolled ? "a device enrolled in Intune" : "a compliant device"); }
                        else if (c.Contains("hybrid")) { sat = dev != null && dev.Is("join", "Hybrid joined"); if (!sat) missing.Add("a hybrid joined device"); }
                        else if (c.Contains("app protection")) { sat = (plat == "iOS" || plat == "Android") && Of("appProtection").Any(a => TargetsUser(a, user) && EpText.Eq(PlatformOf(a).Length > 0 ? PlatformOf(a) : (a["platform"].StartsWith("iOS", StringComparison.Ordinal) ? "iOS" : "Android"), plat) && a.List("targetApps").Any(x => EpText.Eq(x, s.ClientApp))); if (!sat) missing.Add("an app protected by an app protection policy"); }
                        else sat = true;
                        ok.Add(sat);
                    }
                    bool all = p.Is("controlsOp", "Require one of the selected controls") ? ok.Any(x => x) : ok.All(x => x);
                    if (ctl.Count == 0) all = true;
                    bool onlyMfa = !all && missing.Count > 0 && missing.All(x => x == "MFA") || !all && p.Is("controlsOp", "Require one of the selected controls") && missing.Contains("MFA");
                    outcome = all ? "Grant" : onlyMfa ? "MFA" : "Block";
                }
                line.Result = outcome == "Block" ? "Block" : outcome == "MFA" ? "Grant after MFA" : "Grant";
                line.Why = missing.Count > 0 ? "needs " + string.Join(", ", missing.ToArray()) : "";
                res.Lines.Add(line);
                if (line.ReportOnly) continue;
                if (outcome == "Block") { blocked = true; foreach (var x in missing) if (!res.Missing.Contains(x)) res.Missing.Add(x); }
                if (outcome == "MFA") mfaNeeded = true;
            }
            res.Outcome = blocked ? "Blocked" : mfaNeeded ? "Allowed after MFA" : "Allowed";
            return res;
        }
    }
}
