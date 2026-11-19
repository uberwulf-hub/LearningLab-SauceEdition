// Cert Lab - a device's life in the pretend tenant: joining Microsoft Entra ID or adding a work account from
// Settings > Accounts > Access work or school, automatic enrolment (MDM user scope, licence, enrolment
// restrictions, device limit), Windows Autopilot (imported hardware hashes, profile assignment through device
// groups, user-driven and self-deploying setup, hybrid join through the Intune Connector and a Domain Join
// profile, the device name template), provisioning packages, and the clock that makes devices check in.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public partial class EpTenant
    {
        public Func<EpDevice, PsMachine> MachineFactory;
        public PsMachine MachineFor(EpDevice d)
        {
            if (d == null || !d.Windows) return null;
            if (d.Machine == null && MachineFactory != null) d.Machine = MachineFactory(d);
            return d.Machine;
        }

        /// <summary>Null when the user may enrol this device into Intune, else what the user sees.</summary>
        public string EnrollProblem(EpDevice d, EpObj user, bool corporate)
        {
            if (user == null) return "Unknown user.";
            if (!Licensed(user, "intune")) return "This user isn't licensed for Intune (error 0x80180018): assign a licence that includes Intune.";
            string ruleWhy = null;
            var plat = Of("restriction").Where(r => r["platform"].StartsWith("Windows", StringComparison.Ordinal) && TargetsUser(r, user)).OrderBy(r => r.Int("priority", 99)).FirstOrDefault();
            if (d.Windows)
            {
                bool allow = plat == null || !plat.Is("allow", "Block");
                bool personalOk = plat != null ? !plat.Is("personal", "Block") : !Settings.Is("winPersonal", "Block");
                if (!allow) ruleWhy = "Enrolment restrictions block Windows devices for this user (" + plat.Name + ") (error 0x80180014).";
                else if (!corporate && !personalOk) ruleWhy = "Personally owned Windows devices are blocked by the enrolment restrictions (error 0x80180014). Corporate methods (Autopilot, a provisioning package, Group Policy, co-management) still work.";
            }
            if (ruleWhy != null) return ruleWhy;
            int limit = Settings.Int("enrollLimit", 5);
            int have = Devices.Count(x => x != d && x.Enrolled && EpText.Eq(x.PrimaryUser, user["alias"]));
            if (!corporate && have >= limit) return "The user has reached the device limit (" + limit + " enrolled devices).";
            return null;
        }
        bool MdmScopeHas(EpObj user)
        {
            string sc = Settings["mdmScope"];
            if (sc == "All") return true;
            if (sc == "Some") return Settings["mdmGroup"].Length > 0 && InGroup(Settings["mdmGroup"], user);
            return false;
        }

        void Enroll(EpDevice d, EpObj user, string how)
        {
            d["managed"] = "Intune"; d["enrolledBy"] = how; if (user != null) d["user"] = user["alias"];
            d.LastSync = DateTime.MinValue;
            d.Note("Enrolled in Intune (" + how + ").");
            var m = MachineFor(d);
            if (m != null)
            {
                m.HKLM.Sub("SOFTWARE\\Microsoft\\Enrollments\\" + d.Id).S("ProviderID", "MS DM Server").S("UPN", user == null ? "" : user["upn"]);
                var s = m.Svc("IntuneManagementExtension"); if (s != null) { s.StartType = "Automatic"; s.Status = "Running"; }
            }
            Sync(d);
        }

        /// <summary>Settings > Accounts > Access work or school > Connect > "Join this device to Microsoft Entra ID".</summary>
        public string JoinFromSettings(EpDevice d, string userName)
        {
            var u = User(userName);
            if (u == null) return "We couldn't find an account with that name.";
            if (u.Is("accountEnabled", "No")) return "Your account has been locked. Contact your support person.";
            if (d.Is("edition", "Home")) return "Windows Home can't join Microsoft Entra ID: add the work account instead (registers the device), or upgrade to Pro.";
            if (d.Is("join", "Hybrid joined") || d.Get("domain").Length > 0) return "This device is joined to an Active Directory domain; it can't also be Microsoft Entra joined from Settings.";
            if (d.Is("join", "Entra joined")) return "This device is already connected to " + TenantName + ".";
            string js = Settings["joinScope"];
            if (js == "None" || js == "Selected" && !(Settings["joinGroup"].Length > 0 && InGroup(Settings["joinGroup"], u))) return "This user isn't allowed to join devices to Microsoft Entra ID (Devices > Device settings) (error 0x801c0003).";
            bool enroll = MdmScopeHas(u);
            if (enroll) { var why = EnrollProblem(d, u, false); if (why != null) return "Something went wrong: " + why; }
            d["join"] = "Entra joined"; d["ownership"] = "Personal"; d["user"] = u["alias"];
            Log("Device " + d.Name + " joined to Microsoft Entra ID by " + u["upn"] + ".");
            d.Note("Joined to Microsoft Entra ID by " + u["upn"] + ".");
            if (enroll) Enroll(d, u, "Microsoft Entra join and automatic enrolment");
            else d.Note("Not enrolled: the MDM user scope doesn't include " + u["upn"] + ".");
            return null;
        }
        /// <summary>Settings > Access work or school > Connect with a work account: Microsoft Entra registered.</summary>
        public string RegisterFromSettings(EpDevice d, string userName)
        {
            var u = User(userName);
            if (u == null) return "We couldn't find an account with that name.";
            if (d.EntraJoined) return "This device is already connected to " + TenantName + ".";
            d["join"] = "Entra registered"; d["user"] = u["alias"];
            if (d.Get("ownership").Length == 0) d["ownership"] = "Personal";
            d.Note("Work account added: Microsoft Entra registered (" + u["upn"] + ").");
            return null;
        }
        /// <summary>Settings > Access work or school > "Enroll only in device management".</summary>
        public string EnrollOnly(EpDevice d, string userName)
        {
            var u = User(userName);
            if (u == null) return "We couldn't find an account with that name.";
            if (d.Enrolled) return "This device is already managed by " + TenantName + ".";
            if (d.Is("edition", "Home")) return "Windows Home can't be enrolled in device management: it needs Windows Pro, Enterprise or Education.";
            var why = EnrollProblem(d, u, false);
            if (why != null) return "Something went wrong: " + why;
            if (d.Get("ownership").Length == 0) d["ownership"] = "Personal";
            Enroll(d, u, "Enroll only in device management");
            return null;
        }
        /// <summary>Settings > Access work or school > Disconnect.</summary>
        public string Disconnect(EpDevice d)
        {
            if (d.Enrolled && d.Is("join", "Entra joined") && EpText.Eq(d.Setting("Experience/AllowManualMDMUnenrollment"), "Block")) return "Your organisation doesn't allow you to disconnect this device (Allow Manual MDM Unenrollment is blocked).";
            if (d.Is("join", "Hybrid joined")) return "Hybrid joined devices are disconnected by leaving the domain, not here.";
            Unmanage(d, true);
            d["join"] = "None"; d["user"] = "";
            d.Note("Disconnected from " + TenantName + ".");
            return null;
        }

        void Unmanage(EpDevice d, bool removeCompanyApps)
        {
            d["managed"] = "None"; d.Compliance = "Not evaluated"; d.ComplianceReasons.Clear(); d.Policies.Clear();
            foreach (var key in d.Applied.Keys.ToList()) WriteSetting(d, key, null);
            d.Applied.Clear();
            d.NextSync = DateTime.MaxValue;
            if (removeCompanyApps) foreach (var a in d.Apps.Values.Where(x => x.Status == "Installed").ToList()) { var app = Find("app", a.App); if (app != null) Remove(d, app); }
            d.Apps.Clear();
            d["laps.password"] = ""; d["laps"] = "";
        }
    }
}
