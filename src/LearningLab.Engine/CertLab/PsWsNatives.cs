// Cert Lab - programs on the AZ-800 machines: gpupdate and gpresult (Group Policy on the pretend domain), repadmin,
// dcdiag, nltest and netdom (domain controllers and replication), icacls (NTFS permissions). Their output follows the
// real tools' layout, shortened; what they report comes from the lab.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public static partial class PsNatives
    {
        static bool Has(NativeCall c, string sw) { return c.Args.Any(a => a.Equals(sw, StringComparison.OrdinalIgnoreCase) || a.StartsWith(sw + ":", StringComparison.OrdinalIgnoreCase)); }
        static string Opt(NativeCall c, string sw) { var a = c.Args.FirstOrDefault(x => x.StartsWith(sw + ":", StringComparison.OrdinalIgnoreCase)); if (a != null) return a.Substring(sw.Length + 1); int i = c.Args.FindIndex(x => x.Equals(sw, StringComparison.OrdinalIgnoreCase)); return i >= 0 && i + 1 < c.Args.Count ? c.Args[i + 1] : null; }

        static partial void RegisterWsNatives()
        {
            var oldGpresult = Get("gpresult");
            Add("gpupdate", null, c =>
            {
                if (c.M.Ws == null) { c.L("Updating policy...\n\nComputer Policy update has completed successfully.\nUser Policy update has completed successfully.\n"); return 0; }
                string t = Opt(c, "/target"); t = t == null ? null : t.ToLowerInvariant();
                string err = c.M.Ws.Refresh(c.M.Name, t);
                if (err != null) { c.L("Updating policy...\n\n" + err); return 1; }
                c.L("Updating policy...\n\n" + (t != "user" ? "Computer Policy update has completed successfully.\n" : "") + (t != "computer" ? "User Policy update has completed successfully.\n" : ""));
                return 0;
            });
            Add("gpresult", null, c =>
            {
                if (c.M.Ws == null) return oldGpresult.Run(c);
                var lab = c.M.Ws;
                string scope = (Opt(c, "/scope") ?? "").ToLowerInvariant(), user = Opt(c, "/user"), file = Opt(c, "/h") ?? Opt(c, "/x");
                if (!Has(c, "/r") && !Has(c, "/v") && !Has(c, "/z") && file == null) { c.L("GPRESULT [/S system] [/USER username] [/SCOPE { USER | COMPUTER }] { /R | /V | /Z | /H file | /X file }\nCert Lab simulates /R (summary), /V, /SCOPE, /USER and /H."); return 1; }
                if (user != null && user.Contains("\\")) user = user.Substring(user.IndexOf('\\') + 1);
                string who = user ?? (c.R.User ?? "Administrator");
                RsopResult rc, ru;
                lab.LastComputer.TryGetValue(c.M.Name, out rc);
                lab.LastUser.TryGetValue(c.M.Name + "|" + who, out ru);
                if (ru == null && scope != "computer" && user != null) { c.L("\nINFO: The user \"" + lab.NetBios + "\\" + who + "\" does not have RSOP data."); return 1; }
                if (file != null)
                {
                    string path = c.R.FullFsPath(file, c.R.CurrentFsDir);
                    var why = c.R.WriteFile(path, "<html><body><h1>Group Policy Results</h1><pre>\r\n" + PsCmdlets.RsopText(ru ?? rc) + "</pre></body></html>\r\n", "unicode", false);
                    if (why != null) { c.Err(why); return 1; }
                    return 0;
                }
                var sb = new StringBuilder("\nMicrosoft (R) Windows (R) Operating System Group Policy Result tool v2.0\n\nCreated on " + lab.Now.ToString("M/d/yyyy 'at' h:mm:ss tt", CultureInfo.InvariantCulture) + "\n\n");
                sb.Append("RSOP data for " + lab.NetBios + "\\" + who + " on " + c.M.Name + " : Logging Mode\n" + new string('-', 40) + "\n\n");
                sb.Append("OS Configuration:            " + (c.M.IsServer ? (lab.Dirs.ContainsKey(c.M.Name) ? "Primary Domain Controller" : "Member Server") : "Member Workstation") + "\nOS Version:                  " + c.M.OsVersion + "\nSite Name:                   " + (rc != null && rc.Site != null ? rc.Site : "N/A") + "\nDomain Name:                 " + lab.NetBios + "\nDomain Type:                 Windows 2008 or later\n\n");
                Action<string, RsopResult, string, bool> part = (title, r, dn, isUser) =>
                {
                    sb.Append(title + "\n" + new string('-', title.Length) + "\n    " + dn + "\n");
                    if (r == null) { sb.Append("    (no Group Policy applied yet)\n\n"); return; }
                    sb.Append("    Last time Group Policy was applied: " + r.When.ToString("M/d/yyyy 'at' h:mm:ss tt", CultureInfo.InvariantCulture) + "\n    Group Policy was applied from:      " + lab.Server(r.Dc).M.Fqdn + "\n    Group Policy slow link threshold:   500 kbps\n");
                    if (!isUser && r.Loopback.Length > 0) sb.Append("    Loopback processing mode:           " + r.Loopback + "\n");
                    sb.Append("\n    Applied Group Policy Objects\n    -----------------------------\n");
                    var ap = isUser ? r.AppliedU : r.AppliedC; var den = isUser ? r.DeniedU : r.DeniedC;
                    foreach (var g in Enumerable.Reverse(ap)) sb.Append("        " + g.Name + "\n");
                    if (ap.Count == 0) sb.Append("        N/A\n");
                    sb.Append("\n    The following GPOs were not applied because they were filtered out\n    -------------------------------------------------------------------\n");
                    foreach (var g in den) sb.Append("        " + g.Name + "\n            Filtering:  " + g.Reason + "\n\n");
                    if (den.Count == 0) sb.Append("        N/A\n\n");
                    if (Has(c, "/v") || Has(c, "/z"))
                    {
                        sb.Append("    Resultant Set Of Policies for " + (isUser ? "User" : "Computer") + "\n    ----------------------------------\n");
                        foreach (var v in (isUser ? r.U : r.C).Values) sb.Append("        GPO: " + v.Gpo + "\n            Policy:  " + (v.Def != null ? v.Def.Name : v.Id) + "\n            Setting: " + v.State + (v.Value.Length > 0 ? " (" + v.Value + ")" : "") + "\n\n");
                    }
                };
                var comp = lab.Main.Find(c.M.Name, "computer"); var u = lab.Main.Find(who, "user");
                if (scope != "user") part("COMPUTER SETTINGS", rc, comp == null ? "" : comp.Dn, false);
                if (scope != "computer") part("USER SETTINGS", ru, u == null ? "" : u.Dn, true);
                c.L(sb.ToString());
                return 0;
            });
            RegisterWsNatives2();
        }
        static partial void RegisterWsNatives2();
    }
}
