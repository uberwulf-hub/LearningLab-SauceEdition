// Cert Lab - endpoint security on a device of the pretend tenant: Defender Antivirus and tamper protection,
// BitLocker (silent encryption needs: required, warnings for other encryption disabled, standard users allowed,
// a TPM, Entra join; the recovery key goes to Microsoft Entra ID when backup is on), the firewall profiles, attack
// surface reduction rules, onboarding to Defender for Endpoint, and Windows LAPS (backup to Microsoft Entra ID needs
// the tenant's LAPS switch; age, rotation and the post-authentication reset).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public partial class EpTenant
    {
        IEnumerable<EpObj> TargetedOf(EpDevice d, string kind) { return Of(kind).Where(o => Targets(o, d)).ToList(); }

        /// <summary>Two policies of one kind on one device that set the same field differently: both report a conflict.</summary>
        string Merge(EpDevice d, List<EpObj> pols, string key)
        {
            var vals = pols.Select(p => p[key]).Where(v => v.Length > 0 && v != "Not configured").Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (vals.Count > 1) { foreach (var p in pols) State(d, p, "Conflict", "Another policy sets " + EpSchemas.Label(p.Kind, key) + " differently."); return null; }
            return vals.Count == 1 ? vals[0] : null;
        }

        void ApplySecurity(EpDevice d)
        {
            var m = MachineFor(d);
            // antivirus
            var av = TargetedOf(d, "av").ToList();
            foreach (var p in av) State(d, p, "Succeeded", null);
            string v;
            if ((v = Merge(d, av, "cloud")) != null) d["cloud"] = v == "Allowed" ? "On" : "Off";
            if ((v = Merge(d, av, "pua")) != null) d["pua"] = v.Contains("on") ? "On" : v.Contains("Audit") ? "Audit" : "Off";
            if ((v = Merge(d, av, "exclusions")) != null) d["avExclusions"] = v;
            var wsx = TargetedOf(d, "wsx").ToList();
            foreach (var p in wsx) State(d, p, "Succeeded", null);
            if ((v = Merge(d, wsx, "tamper")) != null) d["tamper"] = v;
            // firewall
            var fw = TargetedOf(d, "firewall").ToList();
            foreach (var p in fw) State(d, p, "Succeeded", null);
            foreach (var prof in new[] { "domain", "private", "public" })
            {
                var on = Merge(d, fw, prof + "On");
                if (on != null) d["fw." + prof] = on == "True" ? "On" : "Off";
                var inb = Merge(d, fw, prof + "In");
                if (inb != null) d["fw." + prof + "In"] = inb;
                if (m != null)
                {
                    var fp = m.FwProfiles.FirstOrDefault(x => x.Name.Equals(prof, StringComparison.OrdinalIgnoreCase));
                    if (fp != null) { if (on != null) fp.Enabled = on == "True"; if (inb != null) fp.DefaultInboundAction = inb; }
                }
            }
            d["firewall"] = new[] { "domain", "private", "public" }.All(x => !d.Is("fw." + x, "Off")) ? "On" : "Off";
            foreach (var p in TargetedOf(d, "firewallRules"))
            {
                State(d, p, "Succeeded", null);
                if (m == null) continue;
                foreach (var line in p.List("rules"))
                {
                    var r = line.Split(',').Select(x => x.Trim()).ToArray();
                    if (r.Length != 5 || m.FwRules.Any(x => x.DisplayName == r[0])) continue;
                    m.FwRules.Add(new FwRule { Name = "MDM-" + Math.Abs(FwHash.Of(r[0])).ToString(CultureInfo.InvariantCulture), DisplayName = r[0], Direction = r[1] == "Out" ? "Outbound" : "Inbound", Action = r[2], Protocol = r[3], LocalPort = r[4], Group = "MDM" });
                }
            }
            // attack surface reduction
            var asr = TargetedOf(d, "asr").ToList();
            foreach (var p in asr) State(d, p, "Succeeded", null);
            foreach (var r in EpCatalog.AsrRules) { var mode = Merge(d, asr, "asr:" + r.Key); if (mode != null) d["asr:" + r.Key] = mode; }
            if ((v = Merge(d, asr, "cfa")) != null) d["cfa"] = v;
            // Defender for Endpoint
            foreach (var p in TargetedOf(d, "edr"))
            {
                if (p.Is("package", "Offboard")) { d["mde"] = "No"; State(d, p, "Succeeded", null); continue; }
                if (p.Is("package", "Auto from connector") && !Settings.Is("mdeConnector", "On")) { State(d, p, "Error", "No onboarding package: the connection to Microsoft Defender for Endpoint is off (Endpoint security > Microsoft Defender for Endpoint)."); continue; }
                if (!d.Is("mde", "Yes")) d.Note("Onboarded to Microsoft Defender for Endpoint.");
                d["mde"] = "Yes";
                if (d.Get("riskAfterOnboard").Length > 0) d["risk"] = d["riskAfterOnboard"]; else if (d["risk"].Length == 0) d["risk"] = "Clear";
                State(d, p, "Succeeded", null);
                if (m != null) { var s = m.Svc("Sense"); if (s != null) { s.StartType = "Automatic"; s.Status = "Running"; } }
            }
            foreach (var p in TargetedOf(d, "baseline")) State(d, p, "Succeeded", null);
            ApplyBitLocker(d);
        }

        void ApplyBitLocker(EpDevice d)
        {
            var pols = TargetedOf(d, "diskEncryption").ToList();
            if (pols.Count == 0) return;
            if (pols.Count > 1 && Merge(d, pols, "requireEncryption") == null && pols.Select(p => p["requireEncryption"]).Distinct().Count() > 1) return;
            var p0 = pols[0];
            foreach (var p in pols) State(d, p, "Succeeded", null);
            if (!p0.Is("requireEncryption", "Enabled")) return;
            d["bitlockerMethod"] = p0.Get("osMethod").Length > 0 ? p0["osMethod"] : "XTS-AES 128-bit";
            if (d.Is("bitlocker", "On") || d.Is("bitlocker", "Encrypting"))
            {
                if (p0.Is("backup", "Enabled") && !d.Is("recoveryKeyIn", "Microsoft Entra ID") && d.EntraJoined) { d["recoveryKeyIn"] = "Microsoft Entra ID"; if (d["recoveryKey"].Length == 0) d["recoveryKey"] = NewRecoveryKey(); d.Note("BitLocker recovery key backed up to Microsoft Entra ID."); }
                return;
            }
            string why = null;
            if (d.Is("tpm", "None") || d.Get("tpm").Length == 0) why = "This device has no TPM, so BitLocker can't turn on silently.";
            else if (!p0.Is("allowWarning", "Disabled")) why = "Waiting for the user: Windows asks the user to start encryption (Allow Warning For Other Disk Encryption isn't Disabled, so it isn't silent).";
            else if (!d.Yes("userIsAdmin") && d.PrimaryUser.Length > 0 && !p0.Is("standardUser", "Enabled")) why = "The signed-in user is a standard user and Allow Standard User Encryption isn't Enabled.";
            else if (p0.Is("startupAuth", "Enabled") && p0.Is("tpmPin", "Require startup PIN with TPM")) why = "Waiting for the user to choose a startup PIN (a required PIN can't be set silently).";
            else if (!d.EntraJoined) why = "Silent encryption needs a Microsoft Entra joined or hybrid joined device.";
            else if (p0.Is("backupFirst", "Enabled") && !p0.Is("backup", "Enabled")) why = "The policy waits for the recovery key to be stored, but backing it up to Microsoft Entra ID isn't turned on.";
            if (why != null) { State(d, p0, "Error", why); d["bitlockerWhy"] = why; d.Note("BitLocker: " + why); return; }
            d["bitlocker"] = "Encrypting"; d["bitlockerDone"] = EpText.Date(Now.AddHours(2)); d["bitlockerWhy"] = "";
            d["recoveryKey"] = NewRecoveryKey();
            d["recoveryKeyIn"] = p0.Is("backup", "Enabled") ? "Microsoft Entra ID" : "";
            d.Note("BitLocker: encrypting the operating system drive silently (" + d["bitlockerMethod"] + ")" + (p0.Is("backup", "Enabled") ? "; recovery key backed up to Microsoft Entra ID." : "."));
        }
        string NewRecoveryKey()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 8; i++) { if (i > 0) sb.Append('-'); sb.Append((Rng.Next(0, 65536) * 11).ToString("000000", CultureInfo.InvariantCulture)); }
            return sb.ToString();
        }
        Random rng;
        public Random Rng { get { if (rng == null) rng = new Random(Net != null ? Net.Seed * 131 + 7 : 7); return rng; } }

        public void ApplyLaps(EpDevice d)
        {
            var pols = TargetedOf(d, "laps").ToList();
            if (pols.Count == 0) return;
            if (pols.Count > 1) { foreach (var p in pols) State(d, p, "Conflict", "More than one LAPS policy targets this device."); return; }
            var lp = pols[0];
            string backup = lp["backup"];
            if (backup.StartsWith("Disabled", StringComparison.Ordinal)) { State(d, lp, "Succeeded", null); d["laps"] = "Off"; return; }
            string err = null;
            if (backup.Contains("Entra"))
            {
                if (!Settings.Is("lapsEnabled", "Yes")) err = "LAPS couldn't back up the password: Microsoft Entra LAPS isn't enabled in the tenant's device settings.";
                else if (!d.EntraJoined) err = "Backing up to Microsoft Entra ID needs a Microsoft Entra joined or hybrid joined device.";
            }
            else if (!d.Is("join", "Hybrid joined")) err = "Backing up to Active Directory needs a device joined to an Active Directory domain.";
            string account = lp.Get("adminName").Length > 0 ? lp["adminName"] : "Administrator";
            var m = MachineFor(d);
            if (err == null && m != null && m.FindUser(account) == null) err = "The managed account '" + account + "' doesn't exist on the device (create it first).";
            if (err != null) { State(d, lp, "Error", err); d["laps"] = "Error"; d["lapsWhy"] = err; d.Note("LAPS: " + err); return; }
            State(d, lp, "Succeeded", null);
            DateTime exp;
            bool due = d.Get("laps.password").Length == 0 || d.Yes("laps.rotate") || !EpText.Eq(d["laps.account"], account)
                || DateTime.TryParse(d["laps.expires"], CultureInfo.InvariantCulture, DateTimeStyles.None, out exp) && exp <= Now;
            if (due) NewLapsPassword(d, lp, account);
            d["laps"] = "On"; d["lapsWhy"] = "";
        }

        public void NewLapsPassword(EpDevice d, EpObj lp, string account)
        {
            int len = lp.Int("length", 14);
            const string all = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!#%+=@";
            var sb = new StringBuilder();
            for (int i = 0; i < len; i++) sb.Append(all[Rng.Next(all.Length)]);
            d["laps.password"] = sb.ToString(); d["laps.account"] = account; d["laps.updated"] = EpText.Date(Now);
            d["laps.expires"] = EpText.Date(Now.AddDays(lp.Int("ageDays", 30))); d["laps.rotate"] = ""; d["laps.usedAt"] = "";
            d["laps.backup"] = lp["backup"].Contains("Entra") ? "Microsoft Entra ID" : "Active Directory";
            var m = MachineFor(d);
            if (m != null) { var u = m.FindUser(account); if (u != null) { u.Password = d["laps.password"]; u.PasswordLastSet = Now; } }
            d.Note("LAPS: new password for " + account + ", backed up to " + d["laps.backup"] + ".");
        }

        /// <summary>Someone signed in with the LAPS password: the post-authentication action runs after the delay.</summary>
        public void LapsUsed(EpDevice d)
        {
            d["laps.usedAt"] = EpText.Date(Now);
            d.Note("Someone signed in with the managed local administrator account.");
        }
    }
}
