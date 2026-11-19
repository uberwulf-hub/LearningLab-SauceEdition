// Cert Lab - icacls (NTFS permissions on the AZ-800 servers), docker (a pretend Docker engine on HV1), sconfig (Server
// Core's menu, shown as text) and azcmagent (the Azure Connected Machine agent; it never reaches Azure).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public static partial class PsNatives
    {
        static partial void RegisterWsNatives3()
        {
            Add("icacls", null, c =>
            {
                var s = c.M.WsSrv;
                if (s == null) { c.L("Cert Lab simulates icacls on the AZ-800 servers."); return 1; }
                if (c.Args.Count == 0) { c.L("ICACLS name [/grant[:r] Sid:perm[...]] [/deny Sid:perm [...]] [/remove[:g|:d]] Sid[...]] [/inheritance:e|d|r] [/T] [/C] [/Q]\n    perm: F full, M modify, RX read and execute, R read, W write, D delete\n    inheritance: (OI) object inherit, (CI) container inherit, (IO) inherit only, (NP) don't propagate"); return 0; }
                string path = c.R.FullFsPath(c.Args[0], c.R.CurrentFsDir);
                var f = c.M.FindFile(path);
                if (f == null) { c.L(c.Args[0] + ": The system cannot find the file specified.\nSuccessfully processed 0 files; Failed processing 1 files"); return 2; }
                var acl = s.AclOf(f, true);
                bool changed = false;
                for (int i = 1; i < c.Args.Count; i++)
                {
                    string sw = c.Args[i].ToLowerInvariant();
                    if (sw == "/t" || sw == "/c" || sw == "/q") continue;
                    if (sw.StartsWith("/inheritance:", StringComparison.Ordinal))
                    {
                        char m = sw[13];
                        if (m == 'e') acl.Protected = false;
                        else { if (m == 'd') foreach (var e in s.EffectiveAces(f).Where(x => x.Inherited)) { var cp = e.Copy(); cp.Inherited = false; acl.Explicit.Add(cp); } acl.Protected = true; }
                        changed = true; continue;
                    }
                    if (sw.StartsWith("/grant", StringComparison.Ordinal) || sw == "/deny" || sw.StartsWith("/remove", StringComparison.Ordinal))
                    {
                        if (i + 1 >= c.Args.Count) { c.L("Invalid parameter \"" + c.Args[i] + "\""); return 87; }
                        string spec = c.Args[++i];
                        if (sw.StartsWith("/remove", StringComparison.Ordinal)) { string who = spec; acl.Explicit.RemoveAll(x => WsText.Eq(x.Trustee, who) || WsText.Eq(WsLab.StripDomain(x.Trustee), WsLab.StripDomain(who)) && (sw == "/remove" || sw == "/remove:g" && x.Allow || sw == "/remove:d" && !x.Allow)); changed = true; continue; }
                        int colon = spec.LastIndexOf(':');
                        if (colon <= 0) { c.L("Invalid parameter \"" + spec + "\""); return 87; }
                        string trustee = spec.Substring(0, colon), perm = spec.Substring(colon + 1);
                        var ace = new WsAce { Trustee = trustee, Allow = sw != "/deny" };
                        foreach (var fl in new[] { "(OI)", "(CI)", "(IO)", "(NP)" }) if (perm.ToUpperInvariant().Contains(fl)) { if (fl == "(OI)") ace.OI = true; if (fl == "(CI)") ace.CI = true; if (fl == "(IO)") ace.IO = true; if (fl == "(NP)") ace.NP = true; perm = perm.Replace(fl, "").Replace(fl.ToLowerInvariant(), ""); }
                        perm = perm.Trim('(', ')').Replace(",", " ");
                        int mask = NtfsRights.Parse(perm);
                        if (mask <= 0) { c.L("Invalid parameter \"" + spec + "\""); return 87; }
                        ace.Rights = mask;
                        if (sw == "/grant:r") acl.Explicit.RemoveAll(x => x.Allow && WsText.Eq(x.Trustee, trustee));
                        acl.Explicit.Add(ace); changed = true; continue;
                    }
                    c.L("Invalid parameter \"" + c.Args[i] + "\""); return 87;
                }
                if (changed) { c.L("processed file: " + f.FullPath + "\nSuccessfully processed 1 files; Failed processing 0 files"); return 0; }
                var sb = new StringBuilder();
                bool first = true;
                foreach (var e in s.EffectiveAces(f).Concat(acl.Explicit.Where(x => x.IO)).Distinct())
                {
                    sb.Append((first ? f.FullPath + " " : new string(' ', f.FullPath.Length + 1)) + e.Trustee + ":" + (e.Allow ? "" : "(DENY)") + (e.Inherited ? "(I)" : "") + e.Flags + (e.Rights == NtfsRights.Full || e.Rights == NtfsRights.Modify || e.Rights == NtfsRights.ReadExecute || e.Rights == NtfsRights.Read || e.Rights == NtfsRights.Write ? "(" + NtfsRights.Short(e.Rights) + ")" : NtfsRights.Short(e.Rights)) + "\n");
                    first = false;
                }
                c.L(sb + "\nSuccessfully processed 1 files; Failed processing 0 files");
                return 0;
            });
            Add("docker", "C:\\Program Files\\Docker\\docker.exe", Docker);
            Add("sconfig", "C:\\Windows\\System32\\sconfig.cmd", c =>
            {
                var m = c.M;
                c.L("================================================================================\n                       Welcome to Windows Server 2022 Datacenter\n================================================================================\n\n" +
                    "  1)  Domain/workgroup:                    Domain: " + m.Domain + "\n  2)  Computer name:                       " + m.Name + "\n  3)  Add local administrator\n  4)  Remote management:                   " + (m.WinRmListening ? "Enabled" : "Disabled") + "\n\n" +
                    "  5)  Update setting:                      Download only\n  6)  Install updates\n  7)  Remote desktop:                      Disabled\n\n  8)  Network settings\n  9)  Date and time\n  10) Telemetry setting:                   Required\n  11) Windows activation\n\n  12) Log off user\n  13) Restart server\n  14) Shut down server\n  15) Exit to command line (PowerShell)\n\n" +
                    "Cert Lab shows sconfig's menu only: each item is a PowerShell command you can type instead (Rename-Computer, Add-Computer, Enable-PSRemoting, Set-NetIPAddress, Restart-Computer...).");
                return 0;
            });
            Add("azcmagent", "C:\\Program Files\\AzureConnectedMachineAgent\\azcmagent.exe", c =>
            {
                var lab = c.M.Ws;
                if (lab == null || c.M.Svc("himds") == null) { c.Err("azcmagent : The term 'azcmagent' is not recognized: the Azure Connected Machine agent isn't installed (onboard the server from the Azure portal or Windows Admin Center)."); return 1; }
                string verb = c.Args.Count > 0 ? c.Args[0].ToLowerInvariant() : "";
                var z = lab.Azure;
                if (verb == "show")
                {
                    ArcServer a; bool on = z.Arc.TryGetValue(c.M.Name, out a);
                    c.L("Resource Name                           : " + (on ? a.Name : "") + "\nResource Group Name                     : " + (on ? a.ResourceGroup : "") + "\nSubscription ID                         : " + (on ? z.SubscriptionId : "") + "\nLocation                                : " + (on ? a.Region : "") + "\nAgent Version                           : 1.47\nAgent Status                            : " + (on ? z.ArcStatus(c.M.Name) : "Disconnected") + "\nAgent Last Heartbeat                    : " + lab.Now.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) + "\n\nAgent Service (himds)                   : " + (c.M.Svc("himds").Running ? "running" : "stopped") + "\nGC Service (gcarcservice)               : " + (c.M.Svc("GCArcService") != null && c.M.Svc("GCArcService").Running ? "running" : "stopped"));
                    return 0;
                }
                if (verb == "connect") { string rg = Opt(c, "--resource-group") ?? Opt(c, "-g"); var err = z.Onboard(c.M.Name, rg, Opt(c, "--location") ?? Opt(c, "-l")); c.L(err ?? "INFO    Connecting machine to Azure... This might take a few minutes.\nINFO    Connected machine to Azure (Cert Lab: a pretend subscription; nothing left this PC).\nINFO    Machine overview page: (the practice Azure portal > Azure Arc > " + c.M.Name + ")"); return err == null ? 0 : 1; }
                if (verb == "disconnect") { var err = z.Disconnect(c.M.Name); c.L(err ?? "INFO    Successfully disconnected."); return err == null ? 0 : 1; }
                if (verb == "version") { c.L("azcmagent version 1.47"); return 0; }
                c.L("Cert Lab simulates azcmagent show, connect (--resource-group, --location), disconnect and version.");
                return 1;
            });
        }
    }
}
