// Cert Lab - Windows Autopilot and provisioning packages in the pretend tenant, and the clock (devices check in
// about every 8 hours; Sync from the portal or from Settings checks in at once).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public partial class EpTenant
    {
        /// <summary>Imports a hardware hash (from a CSV made by Get-WindowsAutopilotInfo) as an Autopilot device.</summary>
        public string ImportAutopilot(string serial, string groupTag, string user)
        {
            if (Find("autopilot", serial) != null) return "A device with serial number " + serial + " is already registered.";
            var r = Add(new EpObj { Kind = "autopilot", Name = serial });
            r["serial"] = serial; r["groupTag"] = groupTag ?? ""; r["user"] = user ?? ""; r["profileStatus"] = "Not assigned";
            var d = Device(serial);
            if (d != null) { r["model"] = d["model"]; r["manufacturer"] = d["manufacturer"]; LinkAutopilot(r, d); }
            Log("Autopilot device " + serial + " imported.");
            return null;
        }
        public void LinkAutopilot(EpObj r, EpDevice d)
        {
            var ids = d.List("physicalIds").Where(x => !x.StartsWith("[ZTDid]", StringComparison.Ordinal) && !x.StartsWith("[OrderID]", StringComparison.Ordinal)).ToList();
            ids.Add("[ZTDid]:" + Math.Abs(FwHash.Of((r["serial"] + "ztd"))).ToString("x8", CultureInfo.InvariantCulture) + "-ztd");
            if (r["groupTag"].Length > 0) ids.Add("[OrderID]:" + r["groupTag"]);
            d["physicalIds"] = EpText.Join(ids);
        }

        void AutopilotTick()
        {
            // "Convert all targeted devices to Autopilot": existing Entra joined devices get registered
            foreach (var p in Of("autopilotProfile").Where(x => x.Is("convertAll", "Yes")).ToList())
                foreach (var d in Devices.Where(x => x.Windows && x.EntraJoined && x.Enrolled && Find("autopilot", x["serial"]) == null && Targets(p, x)).ToList())
                    ImportAutopilot(d["serial"], "", "");
            foreach (var r in Of("autopilot").ToList())
            {
                var d = Device(r["serial"]);
                if (d != null) LinkAutopilot(r, d);
                if (r["profileStatus"].Length == 0) r["profileStatus"] = "Not assigned";
                EpObj prof = null;
                if (d != null) prof = Of("autopilotProfile").Where(p => Targets(p, d)).OrderBy(p => p.Created).ThenBy(p => p.Id, StringComparer.Ordinal).FirstOrDefault();
                string want = prof == null ? "" : prof.Name;
                if (!EpText.Eq(want, r["profile"]))
                {
                    r["profile"] = want;
                    r["profileStatus"] = want.Length == 0 ? "Not assigned" : "Assigning";
                    r["assignAt"] = EpText.Date(Now.AddMinutes(15));
                }
                DateTime at;
                if (r.Is("profileStatus", "Assigning") && DateTime.TryParse(r["assignAt"], CultureInfo.InvariantCulture, DateTimeStyles.None, out at) && Now >= at) { r["profileStatus"] = "Assigned"; Log("Autopilot profile " + want + " assigned to " + r["serial"] + "."); }
            }
        }

        string MakeName(string template, EpDevice d)
        {
            if (template.Length == 0) return "DESKTOP-" + RandomChars(7);
            var s = template.Replace("%SERIAL%", d["serial"]);
            var m = System.Text.RegularExpressions.Regex.Match(s, "%RAND:(\\d+)%");
            if (m.Success) s = s.Replace(m.Value, RandomDigits(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)));
            return s.Length > 15 ? s.Substring(0, 15) : s;
        }
        string RandomChars(int n) { const string a = "ABCDEFGHJKLMNPQRSTUVWXYZ0123456789"; var sb = new StringBuilder(); for (int i = 0; i < n; i++) sb.Append(a[Rng.Next(a.Length)]); return sb.ToString(); }
        string RandomDigits(int n) { var sb = new StringBuilder(); for (int i = 0; i < n; i++) sb.Append((char)('0' + Rng.Next(10))); return sb.ToString(); }

        void Rename(EpDevice d, string name)
        {
            if (d.Get("originalName").Length == 0) d["originalName"] = d.Name;
            foreach (var g in Of("group")) { var l = g.List("members"); int i = l.FindIndex(x => EpText.Eq(x, d.Name)); if (i >= 0) { l[i] = name; g["members"] = EpText.Join(l); } }
            d.Name = name;
            var m = MachineFor(d); if (m != null) { var h = Net.Hosts.FirstOrDefault(x => x.Machine == m); if (h != null) h.Name = name; m.Name = name; if (m.Domain == "WORKGROUP") m.DomainShort = name; }
        }

        /// <summary>The out-of-box experience: Autopilot when a profile is assigned, else ordinary setup for work or school.</summary>
        public string RunOobe(EpDevice d, string userName)
        {
            if (!d.Is("state", "oobe")) return "The device isn't in the out-of-box experience.";
            var r = Find("autopilot", d["serial"]);
            var prof = r != null && r.Is("profileStatus", "Assigned") ? Find("autopilotProfile", r["profile"]) : null;
            EpObj u = User(userName);
            if (prof == null)
            {
                if (u == null) return "Without an Autopilot profile, setup asks for a work or school account.";
                d["state"] = "running";
                Rename(d, MakeName("", d));
                d.Note("Ordinary setup (no Autopilot profile was assigned" + (r != null ? ": profile status " + r["profileStatus"] : ": the device isn't registered for Autopilot") + ").");
                var err = JoinFromSettings(d, userName);
                if (err != null) { d["state"] = "oobe"; return err; }
                return null;
            }
            bool self = prof.Is("mode", "Self-deploying");
            bool hybrid = prof["joinType"].Contains("hybrid");
            if (self)
            {
                if (!d.Is("tpm", "2.0")) return "Self-deploying mode needs TPM 2.0 (device attestation) and this device doesn't have it.";
                u = null;
            }
            else
            {
                if (u == null && r["user"].Length > 0) u = User(r["user"]);
                if (u == null) return "User-driven mode: sign in with a work account.";
                if (!MdmScopeHas(u)) return "Something went wrong: the MDM user scope doesn't include this user, so Windows can't enrol the device.";
            }
            if (u != null) { var why = EnrollProblem(d, u, true); if (why != null) return "Something went wrong: " + why; }
            string name;
            if (hybrid)
            {
                if (!Settings.Is("adConnector", "Installed")) return "Hybrid join failed: the Intune Connector for Active Directory isn't installed.";
                var dj = Of("config").FirstOrDefault(c => c.Is("type", "Domain Join") && Targets(c, d));
                if (dj == null) return "Hybrid join failed: no Domain Join profile is assigned to this device's group.";
                string pre = dj["prefix"];
                name = pre + RandomDigits(Math.Max(1, 15 - pre.Length));
                d["join"] = "Hybrid joined"; d["domain"] = dj["domain"]; d["ou"] = dj["ou"];
            }
            else { name = MakeName(prof["nameTemplate"], d); d["join"] = "Entra joined"; }
            Rename(d, name);
            d["state"] = "running"; d["ownership"] = "Corporate"; d["enrollmentProfile"] = prof.Name;
            d["userIsAdmin"] = prof.Is("userType", "Administrator") ? "Yes" : "No";
            d["user"] = u == null ? "" : u["alias"];
            d.Note("Windows Autopilot (" + prof["mode"] + ", " + prof["joinType"] + ") with profile " + prof.Name + "; device name " + name + ".");
            Enroll(d, u, "Windows Autopilot");
            var esp = Of("esp").FirstOrDefault(e => Targets(e, d)) ;
            if (esp != null && esp.Is("showProgress", "Yes")) d.Note("Enrollment Status Page " + esp.Name + ": device preparation, device setup and account setup finished.");
            return null;
        }
    }
}
