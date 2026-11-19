// Cert Lab - remote actions from the practice portal (sync, restart, retire, wipe, Autopilot Reset, Fresh Start,
// rename, BitLocker key rotation, local admin password rotation, Defender scans, collect diagnostics, delete), a
// provisioning package run on the device, and the clock.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public partial class EpTenant
    {
        public static readonly string[] RemoteActions = { "sync", "restart", "retire", "wipe", "autopilot-reset", "fresh-start", "rename", "rotate-bitlocker", "rotate-laps", "quick-scan", "full-scan", "update-defender", "collect-diagnostics", "remote-lock", "delete" };
        public static string RemoteLabel(string a)
        {
            switch (a)
            {
                case "sync": return "Sync"; case "restart": return "Restart"; case "retire": return "Retire"; case "wipe": return "Wipe";
                case "autopilot-reset": return "Autopilot Reset"; case "fresh-start": return "Fresh Start"; case "rename": return "Rename device";
                case "rotate-bitlocker": return "BitLocker key rotation"; case "rotate-laps": return "Rotate local admin password";
                case "quick-scan": return "Quick scan"; case "full-scan": return "Full scan"; case "update-defender": return "Update Windows Defender security intelligence";
                case "collect-diagnostics": return "Collect diagnostics"; case "remote-lock": return "Remote lock"; case "delete": return "Delete";
            }
            return a;
        }
        /// <summary>Which remote actions the portal offers for a device.</summary>
        public static IEnumerable<string> ActionsFor(EpDevice d)
        {
            foreach (var a in RemoteActions)
            {
                bool win = d.Windows;
                if (!win && (a == "autopilot-reset" || a == "fresh-start" || a == "rotate-bitlocker" || a == "rotate-laps" || a.Contains("scan") || a == "update-defender" || a == "rename" || a == "collect-diagnostics")) continue;
                if (win && a == "remote-lock") continue;
                yield return a;
            }
        }

        /// <summary>Runs a remote action; null when it was sent, else why not.</summary>
        public string Remote(EpDevice d, string action, string arg, bool keepEnrollment, bool keepUserData)
        {
            if (d == null) return "No such device.";
            if (!d.Enrolled && action != "delete") return d.Name + " isn't enrolled in Intune, so it can't receive remote actions.";
            var m = MachineFor(d);
            Log(RemoteLabel(action) + " sent to " + d.Name + ".");
            switch (action)
            {
                case "sync": Sync(d); return null;
                case "restart": if (m != null) m.Reboot(); d.Note("Restarted from the portal."); return null;
                case "retire":
                    Unmanage(d, true); d.Note("Retired: company data, apps and settings removed; the device left Intune."); return null;
                case "wipe":
                    if (keepEnrollment) { foreach (var a in d.Apps.Values.ToList()) { var app = Find("app", a.App); if (app != null) Remove(d, app); } d.Apps.Clear(); d.Note("Wiped (enrolment state and user account kept)."); Sync(d); return null; }
                    Unmanage(d, true); d["join"] = "None"; d["user"] = ""; d["state"] = "oobe"; d["bitlocker"] = "Off"; d["laps.password"] = "";
                    d.Note("Wiped: reset to factory settings; it starts at the out-of-box experience.");
                    return null;
                case "autopilot-reset":
                    if (!d.Is("join", "Entra joined")) return "Autopilot Reset needs a Microsoft Entra joined device.";
                    foreach (var a in d.Apps.Values.ToList()) { var app = Find("app", a.App); if (app != null) Remove(d, app); }
                    d.Apps.Clear(); d["user"] = ""; d.Note("Autopilot Reset: personal files, apps and settings removed; still Microsoft Entra joined and enrolled. Policies and apps come back at check-in.");
                    Sync(d); return null;
                case "fresh-start":
                    d.Note("Fresh Start: Windows reinstalled without the manufacturer's extra apps" + (keepUserData ? "; user data kept." : "."));
                    Sync(d); return null;
                case "rename":
                    {
                        string n = (arg ?? "").Trim();
                        if (n.Length == 0 || n.Length > 15 || n.Any(ch => !(char.IsLetterOrDigit(ch) || ch == '-')) || n.All(char.IsDigit)) return "Device names are 1-15 letters, digits or hyphens, and not only digits.";
                        if (d.Is("join", "Hybrid joined")) return "Renaming hybrid joined devices from the portal needs extra setup; rename them in Active Directory.";
                        Rename(d, n); d.Note("Renamed to " + n + " (after a restart)."); return null;
                    }
                case "rotate-bitlocker":
                    {
                        var pol = Of("diskEncryption").FirstOrDefault(p => Targets(p, d));
                        string rot = pol == null ? "" : pol["rotation"];
                        if (!d.Is("bitlocker", "On")) return "BitLocker isn't on for this device.";
                        if (!rot.StartsWith("Refresh on", StringComparison.Ordinal) || d.Is("join", "Hybrid joined") && !rot.Contains("hybrid")) return "Key rotation needs Configure Recovery Password Rotation turned on for this kind of device in a disk encryption policy.";
                        d["recoveryKey"] = NewRecoveryKey(); d["recoveryKeyIn"] = "Microsoft Entra ID"; d.Note("BitLocker recovery password rotated and backed up."); return null;
                    }
                case "rotate-laps":
                    if (d["laps"] != "On") return "This device has no working LAPS policy.";
                    d["laps.rotate"] = "Yes"; Sync(d); return null;
                case "quick-scan": case "full-scan": d.Note("Microsoft Defender Antivirus " + (action == "quick-scan" ? "quick" : "full") + " scan: no threats found."); d["lastScan"] = EpText.Date(Now); return null;
                case "update-defender": d["sigAgeDays"] = "0"; d.Note("Security intelligence updated."); return null;
                case "collect-diagnostics":
                    d["diagnostics"] = "DiagLogs-" + d.Name + "-" + Now.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture) + ".zip"; d.Note("Diagnostics collected (" + d["diagnostics"] + ")."); return null;
                case "remote-lock": d.Note("Locked from the portal."); return null;
                case "delete":
                    Unmanage(d, false); d.Note("Deleted from Intune. (It is still in Microsoft Entra ID; Windows enrols again only if someone reconnects it.)"); return null;
            }
            return "Unknown action " + action + ".";
        }

        // ------------------------------------------------------------ provisioning packages
        /// <summary>A provisioning package made with Windows Configuration Designer: bulk Entra join, enrolment, a name.</summary>
        public string InstallPackage(EpDevice d, string file)
        {
            var m = MachineFor(d);
            var f = m == null ? null : m.FindFile(file);
            if (f == null) return "Cannot find the provisioning package " + file + ".";
            var cfg = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in (f.Text ?? "").Replace("\r", "").Split('\n')) { int eq = line.IndexOf('='); if (eq > 0) cfg[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim(); }
            string v; DateTime exp;
            if (cfg.TryGetValue("TokenExpires", out v) && DateTime.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.None, out exp) && exp < Now) return "The package's bulk token expired on " + v + ": make a new package with a new token.";
            if (cfg.TryGetValue("DeviceName", out v)) Rename(d, MakeName(v, d));
            if (d.Is("edition", "Home")) return "Windows Home can't be bulk joined to Microsoft Entra ID.";
            d["join"] = "Entra joined"; d["ownership"] = "Corporate"; d["state"] = "running"; d["user"] = "";
            d.Note("Provisioning package " + f.Name + ": bulk joined to Microsoft Entra ID.");
            if (Settings.Is("mdmScope", "All")) Enroll(d, null, "bulk enrolment (provisioning package)");
            else d.Note("Not enrolled: bulk enrolment needs the MDM user scope set to All.");
            return null;
        }

        // ------------------------------------------------------------ the clock
        public void Advance(long seconds)
        {
            var target = Now.AddSeconds(Math.Max(0, seconds));
            Tick();
            int guard = 0;
            while (Now < target && guard++ < 5000)
            {
                var next = target;
                foreach (var d in Devices)
                {
                    if (d.Enrolled && d.NextSync > Now && d.NextSync < next) next = d.NextSync;
                    DateTime t;
                    if (d.Is("bitlocker", "Encrypting") && DateTime.TryParse(d["bitlockerDone"], CultureInfo.InvariantCulture, DateTimeStyles.None, out t) && t > Now && t < next) next = t;
                }
                foreach (var r in Of("autopilot")) { DateTime t; if (r.Is("profileStatus", "Assigning") && DateTime.TryParse(r["assignAt"], CultureInfo.InvariantCulture, DateTimeStyles.None, out t) && t > Now && t < next) next = t; }
                long step = (long)Math.Ceiling((next - Now).TotalSeconds);
                if (step <= 0) step = 1;
                if (Net != null) Net.Advance(step); else ownClock = ownClock.AddSeconds(step);
                Tick();
            }
        }

        public void Tick()
        {
            AutopilotTick();
            foreach (var d in Devices.ToList())
            {
                DateTime t;
                if (d.Is("bitlocker", "Encrypting") && DateTime.TryParse(d["bitlockerDone"], CultureInfo.InvariantCulture, DateTimeStyles.None, out t) && Now >= t) { d["bitlocker"] = "On"; d.Note("BitLocker: encryption finished; protection is on."); }
                if (d["laps.usedAt"].Length > 0 && DateTime.TryParse(d["laps.usedAt"], CultureInfo.InvariantCulture, DateTimeStyles.None, out t))
                {
                    var lp = Of("laps").FirstOrDefault(p => Targets(p, d));
                    if (lp != null && !lp.Is("postAuth", "Disabled") && Now >= t.AddHours(lp.Int("postDelay", 24))) { NewLapsPassword(d, lp, d["laps.account"]); d.Note("LAPS: the password was used, so it was reset after the post-authentication delay."); }
                }
                if (d.Enrolled && Now >= d.NextSync) Sync(d);
            }
        }
    }
}
