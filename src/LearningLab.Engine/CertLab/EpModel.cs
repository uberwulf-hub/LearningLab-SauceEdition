// Cert Lab - the MD-102 course's pretend tenant: users, groups, devices and every kind of policy the practice
// admin portal manages, as plain objects (EpObj: a kind, a name, properties as text, assignments to groups). Nothing
// here talks to Microsoft or any real service: it is a simulation in memory. Schemas (EpSchemas.cs) say which
// properties a kind has and how the portal's forms validate them; EpEval.cs decides what reaches each device.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    /// <summary>One assignment of a policy or app: a group (or "All users" / "All devices"), the intent
    /// (include / exclude for policies; required / available / uninstall / exclude for apps) and an optional filter.</summary>
    public class EpAssign
    {
        public string Group = "", Intent = "include", Filter = "", FilterMode = "";
        public bool Exclude { get { return Intent == "exclude"; } }
        public EpAssign Copy() { return new EpAssign { Group = Group, Intent = Intent, Filter = Filter, FilterMode = FilterMode }; }
    }

    /// <summary>Anything in the tenant: a user, group, device, policy, profile, app, filter, role...</summary>
    public class EpObj
    {
        public string Kind = "", Id = "", Name = "";
        public readonly Dictionary<string, string> P = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public readonly List<EpAssign> Assignments = new List<EpAssign>();
        public DateTime Created, Modified;
        public EpTenant T;

        public string this[string key] { get { return Get(key); } set { P[key] = value ?? ""; } }
        public virtual string Get(string key) { string v; return P.TryGetValue(key, out v) ? v : ""; }
        public bool Is(string key, string value) { return string.Equals(Get(key), value, StringComparison.OrdinalIgnoreCase); }
        public bool Yes(string key) { var v = Get(key).ToLowerInvariant(); return v == "yes" || v == "true" || v == "require" || v == "required" || v == "on" || v == "enabled" || v == "block"; }
        public int Int(string key, int def) { int n; return int.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : def; }
        public List<string> List(string key) { return EpText.Lines(Get(key)); }
        public IEnumerable<string> Groups(string intent) { return Assignments.Where(a => a.Intent == intent).Select(a => a.Group); }
        public override string ToString() { return Kind + " " + Name; }
    }

    public static class EpText
    {
        public static List<string> Lines(string s)
        {
            if (string.IsNullOrEmpty(s)) return new List<string>();
            return s.Replace("\r", "").Split(new[] { '\n', ';' }).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        }
        public static string Join(IEnumerable<string> l) { return string.Join("\n", l.ToArray()); }
        public static bool Eq(string a, string b) { return string.Equals(a ?? "", b ?? "", StringComparison.OrdinalIgnoreCase); }
        public static string Date(DateTime d) { return d.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture); }
        public static string Day(DateTime d) { return d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }
    }

    /// <summary>What a device has of one app: Installed, Not installed, Failed, Pending, Not applicable, Available.</summary>
    public class EpAppState { public string App = "", Status = "", Detail = "", Intent = ""; public DateTime When; }

    /// <summary>A policy's result on one device: Succeeded, Error, Conflict, Not applicable, Pending.</summary>
    public class EpPolicyState { public string Policy = "", Kind = "", Status = "", Detail = ""; }

    /// <summary>A setting that reached a device: the value and the policy it came from.</summary>
    public class EpApplied { public string Key = "", Value = "", From = ""; }

    /// <summary>A device: what Microsoft Entra ID knows (join type, owner), what Intune knows (enrolled, compliance,
    /// policies, apps) and the device's own state (BitLocker, Defender, firewall, updates, LAPS). Windows devices also
    /// have a pretend PowerShell console (EpSim gives each one a PsMachine).</summary>
    public class EpDevice : EpObj
    {
        public readonly List<EpPolicyState> Policies = new List<EpPolicyState>();
        public readonly Dictionary<string, EpApplied> Applied = new Dictionary<string, EpApplied>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, EpAppState> Apps = new Dictionary<string, EpAppState>(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> ComplianceReasons = new List<string>();
        public readonly HashSet<string> ScriptsRun = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly List<string> Log = new List<string>();          // the Intune Management Extension's log, in plain words
        public PsMachine Machine;                                       // Windows devices: the pretend PC behind the console
        public DateTime LastSync = DateTime.MinValue, NextSync = DateTime.MaxValue, NoncompliantSince = DateTime.MinValue;
        public string Compliance = "Not evaluated";

        public bool Windows { get { return Is("os", "Windows"); } }
        public bool Enrolled { get { return Is("managed", "Intune"); } }
        public string JoinType { get { return Get("join"); } }
        public bool EntraJoined { get { return Is("join", "Entra joined") || Is("join", "Hybrid joined"); } }
        public string PrimaryUser { get { return Get("user"); } }
        /// <summary>Is there a device object in Microsoft Entra ID (joined, hybrid joined, registered, or imported to Autopilot)?</summary>
        public bool InDirectory { get { string j = Get("join"); return j.Length > 0 && j != "None" || Get("physicalIds").IndexOf("[ZTDid]", StringComparison.OrdinalIgnoreCase) >= 0; } }

        public override string Get(string key)
        {
            switch ((key ?? "").ToLowerInvariant())
            {
                case "compliance": return Compliance;
                case "compliancereasons": case "reasons": return EpText.Join(ComplianceReasons);
                case "lastsync": return LastSync == DateTime.MinValue ? "" : EpText.Date(LastSync);
                case "apps": return EpText.Join(Apps.Values.Where(a => a.Status == "Installed").Select(a => a.App));
                case "policies": return EpText.Join(Policies.Select(p => p.Policy + ": " + p.Status));
                case "log": return EpText.Join(Log);
            }
            return base.Get(key);
        }
        public string AppStatus(string app) { EpAppState s; return Apps.TryGetValue(app, out s) ? s.Status : "Not installed"; }
        public string PolicyStatus(string policy) { var p = Policies.FirstOrDefault(x => EpText.Eq(x.Policy, policy)); return p == null ? "Not assigned" : p.Status; }
        public string Setting(string key) { EpApplied a; return Applied.TryGetValue(key, out a) ? a.Value : ""; }
        public void Note(string text)
        {
            string line = EpText.Date(T.Now) + "  " + text;
            Log.Add(line); if (Log.Count > 400) Log.RemoveAt(0);
            if (Machine != null) EpDeviceSeed.AppendLog(Machine, line);
        }
    }

    /// <summary>A sign-in to test against Conditional Access (the portal's What If, the checks, the device panel).</summary>
    public class EpSignIn
    {
        public string User = "", Device = "", App = "Office 365", Platform = "", Location = "", Client = "browser";
        public bool Mfa;
        public string ClientApp = "";   // the app used on a phone (Microsoft Outlook...), for "Require app protection policy"
    }
    public class EpCaLine { public string Policy = "", Applies = "", Result = "", Why = ""; public bool ReportOnly; }
    public class EpCaResult
    {
        public string Outcome = "Allowed";      // Allowed, Blocked, Allowed after MFA
        public List<EpCaLine> Lines = new List<EpCaLine>();
        public List<string> Missing = new List<string>();   // what the person would need (MFA, a compliant device...)
    }
}
