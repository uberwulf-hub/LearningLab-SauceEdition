// Cert Lab - the pretend tenant itself: every object, lookups by name, group membership (assigned, nested and
// dynamic), licences, the tenant-wide settings (automatic enrolment, who may join devices, Entra LAPS, the
// compliance defaults, connectors) and the clock. Devices check in on the clock (EpEval.Sync).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public partial class EpTenant
    {
        public readonly List<EpObj> All = new List<EpObj>();
        public EpObj Settings;
        public PsNet Net;                         // the pretend PCs behind the Windows devices (one clock for all)
        DateTime ownClock = new DateTime(2026, 3, 2, 9, 0, 0);
        public DateTime Now { get { return Net != null ? Net.Now : ownClock; } }
        int ids;
        public string Domain = "riverbend.example";
        public string TenantName = "Riverbend Outfitters";
        public string TenantId = "6f1c2a9e-4b7d-4e21-9c3a-5d8e0f7a1b24";   // made up
        public readonly List<string> Audit = new List<string>();

        public IEnumerable<EpObj> Of(string kind) { return All.Where(o => o.Kind == kind); }
        public IEnumerable<EpObj> Users { get { return Of("user"); } }
        public IEnumerable<EpObj> GroupsList { get { return Of("group"); } }
        public IEnumerable<EpDevice> Devices { get { return All.OfType<EpDevice>(); } }

        public T Add<T>(T o) where T : EpObj
        {
            o.T = this;
            if (o.Id.Length == 0) o.Id = o.Kind + "-" + (++ids).ToString("000", CultureInfo.InvariantCulture);
            o.Created = o.Modified = Now;
            All.Add(o);
            return o;
        }
        public EpObj New(string kind, string name) { return Add(new EpObj { Kind = kind, Name = name }); }
        public void Remove(EpObj o)
        {
            All.Remove(o);
            foreach (var g in Of("group")) { var m = g.List("members"); if (m.RemoveAll(x => EpText.Eq(x, o.Name) || EpText.Eq(x, o.Id)) > 0) g["members"] = EpText.Join(m); }
            foreach (var x in All) x.Assignments.RemoveAll(a => o.Kind == "group" && EpText.Eq(a.Group, o.Name) || o.Kind == "filter" && EpText.Eq(a.Filter, o.Name));
        }

        public EpObj Find(string kind, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (kind == "user") return User(name);
            if (kind == "device") return Device(name);
            return Of(kind).FirstOrDefault(o => EpText.Eq(o.Name, name)) ?? Of(kind).FirstOrDefault(o => EpText.Eq(o.Id, name));
        }
        /// <summary>A user by alias (jordan), sign-in name (jordan@riverbend.example), display name or id.</summary>
        public EpObj User(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            return Users.FirstOrDefault(u => EpText.Eq(u["alias"], s) || EpText.Eq(u["upn"], s) || EpText.Eq(u.Name, s) || EpText.Eq(u.Id, s));
        }
        public EpDevice Device(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            return Devices.FirstOrDefault(d => EpText.Eq(d.Name, s) || EpText.Eq(d.Id, s)) ?? Devices.FirstOrDefault(d => EpText.Eq(d["serial"], s)) ?? Devices.FirstOrDefault(d => EpText.Eq(d["originalName"], s));
        }
        public EpObj Group(string s) { return Find("group", s); }

        public void Log(string what) { Audit.Add(EpText.Date(Now) + "  " + what); }

        // ------------------------------------------------------------ licences
        /// <summary>What a user's licences give: intune, p1, p2, winent (Windows Enterprise E3/E5), mde, office.</summary>
        public static HashSet<string> Grants(string licences)
        {
            var g = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var l in EpText.Lines(licences))
            {
                var s = l.ToLowerInvariant();
                if (s.Contains("microsoft 365 e3") || s.Contains("microsoft 365 e5")) { g.Add("intune"); g.Add("p1"); g.Add("winent"); g.Add("office"); }
                if (s.Contains("microsoft 365 e5")) { g.Add("p2"); g.Add("mde"); }
                if (s.Contains("business premium")) { g.Add("intune"); g.Add("p1"); g.Add("office"); }
                if (s.Contains("enterprise mobility") || s.Contains("ems e3")) { g.Add("intune"); g.Add("p1"); }
                if (s.Contains("intune plan 1") || s == "intune") g.Add("intune");
                if (s.Contains("office 365 e3")) g.Add("office");
                if (s.Contains("windows enterprise e3") || s.Contains("windows 10/11 enterprise e3")) g.Add("winent");
            }
            return g;
        }
        public bool Licensed(EpObj user, string what) { return user != null && Grants(user["licenses"]).Contains(what); }

        // ------------------------------------------------------------ groups
        public const string AllUsers = "All users", AllDevices = "All devices";

        /// <summary>Is this user or device a member of the group (directly, through a nested group, or by its rule)?</summary>
        public bool InGroup(string group, EpObj who) { return InGroup(Group(group), who, 0) || IsVirtual(group, who); }
        static bool IsVirtual(string group, EpObj who)
        {
            if (EpText.Eq(group, AllUsers)) return who != null && who.Kind == "user";
            if (EpText.Eq(group, AllDevices)) return who is EpDevice;
            return false;
        }
        bool InGroup(EpObj g, EpObj who, int depth)
        {
            if (g == null || who == null || depth > 8) return false;
            if (who is EpDevice && !((EpDevice)who).InDirectory) return false;   // not in Microsoft Entra ID: in no group
            string mt = g["membership"];
            if (mt.StartsWith("Dynamic", StringComparison.OrdinalIgnoreCase))
            {
                bool forDevices = mt.IndexOf("device", StringComparison.OrdinalIgnoreCase) >= 0;
                if (forDevices != (who is EpDevice)) return false;
                if (who.Kind != "user" && !(who is EpDevice)) return false;
                return EpRules.Matches(g["rule"], "group", p => RuleProp(who, p));
            }
            foreach (var m in g.List("members"))
            {
                if (EpText.Eq(m, who.Name) || EpText.Eq(m, who.Id) || who.Kind == "user" && (EpText.Eq(m, who["alias"]) || EpText.Eq(m, who["upn"]))) return true;
                var sub = Group(m);
                if (sub != null && sub != g && InGroup(sub, who, depth + 1)) return true;
            }
            return false;
        }
        /// <summary>The users and devices in a group (for the portal's Members page and the checks).</summary>
        public List<EpObj> Members(string group)
        {
            if (EpText.Eq(group, AllUsers)) return Users.ToList();
            if (EpText.Eq(group, AllDevices)) return Devices.Cast<EpObj>().ToList();
            var g = Group(group);
            if (g == null) return new List<EpObj>();
            return Users.Concat(Devices.Cast<EpObj>()).Where(o => InGroup(g, o, 0)).ToList();
        }
        /// <summary>"user", "device", "mixed" or "" (empty) for what a group holds; dynamic groups say by their type.</summary>
        public string GroupHolds(string group)
        {
            if (EpText.Eq(group, AllUsers)) return "user";
            if (EpText.Eq(group, AllDevices)) return "device";
            var g = Group(group);
            if (g == null) return "";
            string mt = g["membership"];
            if (mt.StartsWith("Dynamic", StringComparison.OrdinalIgnoreCase)) return mt.IndexOf("device", StringComparison.OrdinalIgnoreCase) >= 0 ? "device" : "user";
            var m = Members(group);
            bool u = m.Any(x => x.Kind == "user"), d = m.Any(x => x is EpDevice);
            return u && d ? "mixed" : u ? "user" : d ? "device" : "";
        }

        /// <summary>A property for membership rules: user.department, device.deviceOSType, device.devicePhysicalIds...</summary>
        public List<string> RuleProp(EpObj who, string prop)
        {
            string p = prop.Substring(prop.IndexOf('.') + 1).ToLowerInvariant();
            var l = new List<string>();
            if (who.Kind == "user")
            {
                switch (p)
                {
                    case "userprincipalname": l.Add(who["upn"]); break;
                    case "displayname": l.Add(who.Name); break;
                    case "accountenabled": l.Add(who.Is("accountEnabled", "false") ? "false" : "true"); break;
                    case "usertype": l.Add(who.Get("userType").Length > 0 ? who["userType"] : "Member"); break;
                    case "mail": l.Add(who["upn"]); break;
                    default: l.Add(who[p]); break;
                }
                return l;
            }
            var d = who as EpDevice;
            if (d == null) return l;
            switch (p)
            {
                case "displayname": l.Add(d.Name); break;
                case "deviceostype": l.Add(d.Get("osType").Length > 0 ? d["osType"] : d["os"]); break;
                case "deviceosversion": l.Add(d["osVersion"]); break;
                case "deviceownership": l.Add(d.Is("ownership", "Corporate") ? "Company" : d["ownership"]); break;
                case "devicemanufacturer": l.Add(d["manufacturer"]); break;
                case "devicemodel": l.Add(d["model"]); break;
                case "devicecategory": l.Add(d["category"]); break;
                case "enrollmentprofilename": l.Add(d["enrollmentProfile"]); break;
                case "managementtype": l.Add(d.Enrolled ? "MDM" : ""); break;
                case "isrooted": l.Add(d.Yes("rooted") ? "true" : "false"); break;
                case "accountenabled": l.Add("true"); break;
                case "deviceid": l.Add(d.Id); break;
                case "profiletype": l.Add("RegisteredDevice"); break;
                case "devicephysicalids": l.AddRange(d.List("physicalIds")); break;
            }
            return l;
        }
        /// <summary>A property for assignment filters: device.osVersion, device.model, device.deviceTrustType...</summary>
        public List<string> FilterProp(EpDevice d, string prop)
        {
            string p = prop.Substring(prop.IndexOf('.') + 1).ToLowerInvariant();
            var l = new List<string>();
            switch (p)
            {
                case "devicename": l.Add(d.Name); break;
                case "manufacturer": l.Add(d["manufacturer"]); break;
                case "model": l.Add(d["model"]); break;
                case "osversion": l.Add(d["osVersion"]); break;
                case "deviceownership": l.Add(d["ownership"]); break;
                case "enrollmentprofilename": l.Add(d["enrollmentProfile"]); break;
                case "operatingsystemsku": l.Add(d.Is("edition", "Pro") ? "Professional" : d["edition"]); break;
                case "devicecategory": l.Add(d["category"]); break;
                case "devicetrusttype": l.Add(d.Is("join", "Entra joined") ? "Azure AD joined" : d.Is("join", "Hybrid joined") ? "Hybrid Azure AD joined" : d.Is("join", "Entra registered") ? "Azure AD registered" : "Unknown"); break;
                case "isrooted": l.Add(d.Yes("rooted") ? "true" : "false"); break;
            }
            return l;
        }
    }
}
