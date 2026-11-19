// Cert Lab - repadmin, dcdiag, nltest, netdom and icacls on the AZ-800 machines.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public static partial class PsNatives
    {
        static partial void RegisterWsNatives2()
        {
            Add("repadmin", null, c =>
            {
                var lab = c.M.Ws;
                if (lab == null || !c.M.FeatureInstalled("RSAT-AD-PowerShell") && !c.M.FeatureInstalled("AD-Domain-Services")) { c.Err("repadmin : The term 'repadmin' is not recognized (install the AD DS tools)."); return 1; }
                var a = c.Args.Select(x => x.ToLowerInvariant()).ToList();
                if (a.Contains("/syncall"))
                {
                    int n = lab.SyncAll();
                    c.L("Syncing all NC's held on " + c.M.Name + ".\nSyncing partition: " + lab.DomainDn + "\nCALLBACK MESSAGE: SyncAll Finished.\nSyncAll terminated with no errors." + (n > 0 ? "" : "") + "\n");
                    return 0;
                }
                if (a.Contains("/replsummary"))
                {
                    var sb = new StringBuilder("Replication Summary Start Time: " + lab.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "\n\nBeginning data collection for replication summary, this may take awhile:\n  ....\n\nSource DSA          largest delta    fails/total %%   error\n");
                    foreach (var s in lab.DcNames) sb.Append(" " + s.PadRight(19) + Delta(lab, s, true).PadRight(17) + (lab.DcUp(s) ? "0 /   1" : "1 /   1").PadLeft(7) + (lab.DcUp(s) ? "    0" : "  100  (1722) The RPC server is unavailable.") + "\n");
                    sb.Append("\nDestination DSA     largest delta    fails/total %%   error\n");
                    foreach (var s in lab.DcNames) sb.Append(" " + s.PadRight(19) + Delta(lab, s, false).PadRight(17) + "0 /   1".PadLeft(7) + "    0\n");
                    c.L(sb.ToString()); return 0;
                }
                if (a.Contains("/showrepl") || a.Count == 0)
                {
                    string dc = c.Args.FirstOrDefault(x => !x.StartsWith("/", StringComparison.Ordinal)) ?? c.M.Name;
                    var s = lab.Server(dc);
                    if (s == null || !lab.Dirs.ContainsKey(s.Name)) { c.L("Repadmin: error binding to " + dc + ": the server isn't a domain controller."); return 1; }
                    var sb = new StringBuilder("\nRepadmin: running command /showrepl against full DC localhost\n" + (lab.SiteOfDc(s.Name) ?? "") + "\\" + s.Name + "\nDSA Options: IS_GC\nSite Options: (none)\n\n==== INBOUND NEIGHBORS ======================================\n\n");
                    foreach (var p in lab.DcNames.Where(x => !WsText.Eq(x, s.Name)))
                    {
                        DateTime last; bool ok = lab.LastRepl.TryGetValue(p + ">" + s.Name, out last);
                        sb.Append(lab.DomainDn + "\n    " + (lab.SiteOfDc(p) ?? "") + "\\" + p + " via RPC\n        Last attempt @ " + (ok ? last : lab.Now).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + (lab.DcUp(p) ? " was successful." : " failed, result 1722 (0x6ba):\n            The RPC server is unavailable.") + "\n");
                        int back = lab.Backlog(p, s.Name);
                        if (back > 0) sb.Append("        " + back + " change(s) not replicated yet (next: " + (lab.NextReplTime(p, s.Name).HasValue ? lab.NextReplTime(p, s.Name).Value.ToString("HH:mm", CultureInfo.InvariantCulture) : "no site link") + ").\n");
                        sb.Append("\n");
                    }
                    c.L(sb.ToString()); return 0;
                }
                if (a.Contains("/replicate"))
                {
                    var rest = c.Args.Where(x => !x.StartsWith("/", StringComparison.Ordinal)).ToList();
                    if (rest.Count < 3) { c.L("Usage: repadmin /replicate <Dest_DSA> <Source_DSA> <Naming Context>"); return 1; }
                    var r = WsOps.Run(lab, "replicate " + rest[0] + " from=" + rest[1]);
                    if (!r.Ok) { c.L("DsReplicaSync() failed with status 1722 (0x6ba):\n    The RPC server is unavailable."); return 1; }
                    c.L("Sync from " + rest[1] + " to " + rest[0] + " completed successfully."); return 0;
                }
                c.L("Cert Lab simulates repadmin /replsummary, /showrepl [DC], /replicate DEST SOURCE NC and /syncall [/AdeP]."); return 1;
            });
            Add("dcdiag", null, c =>
            {
                var lab = c.M.Ws;
                if (lab == null || !c.M.FeatureInstalled("RSAT-AD-PowerShell")) { c.Err("dcdiag : The term 'dcdiag' is not recognized."); return 1; }
                string dc = Opt(c, "/s") ?? (lab.Dirs.ContainsKey(c.M.Name) ? c.M.Name : lab.Fsmo["PDCEmulator"]);
                var s = lab.Server(dc);
                var sb = new StringBuilder("\nDirectory Server Diagnosis\n\nPerforming initial setup:\n   Trying to find home server...\n   Home Server = " + s.Name + "\n   * Identified AD Forest.\n   Done gathering initial info.\n\nDoing primary tests\n\n   Testing server: " + (lab.SiteOfDc(s.Name) ?? "") + "\\" + s.Name + "\n");
                bool up = lab.DcUp(s.Name);
                foreach (var t in new[] { "Connectivity", "Advertising", "FrsEvent", "DFSREvent", "SysVolCheck", "KccEvent", "MachineAccount", "NetLogons", "ObjectsReplicated", "Replications", "RidManager", "Services", "SystemLog", "VerifyReferences" })
                {
                    bool pass = up;
                    if (t == "Replications") pass = up && lab.DcNames.All(p => WsText.Eq(p, s.Name) || lab.DcUp(p));
                    if (t == "RidManager") pass = up && lab.DcUp(lab.Fsmo["RIDMaster"]);
                    if (t == "Services") pass = up && s.SvcRunning("NTDS");
                    if (Opt(c, "/test") != null && !WsText.Eq(Opt(c, "/test"), t)) continue;
                    sb.Append("      Starting test: " + t + "\n" + (pass ? "" : "         " + (t == "RidManager" ? "Warning: the RID master " + lab.Fsmo["RIDMaster"] + " is not responding." : t == "Replications" ? "[Replications Check," + s.Name + "] A recent replication attempt failed: the RPC server is unavailable." : "The server isn't responding.") + "\n") + "         ......................... " + s.Name + (pass ? " passed" : " failed") + " test " + t + "\n");
                }
                c.L(sb.ToString()); return 0;
            });
            Add("nltest", null, c =>
            {
                var lab = c.M.Ws;
                if (lab == null) { c.L("Cert Lab simulates nltest on the AZ-800 machines."); return 1; }
                string get = c.Args.FirstOrDefault(x => x.StartsWith("/dsgetdc:", StringComparison.OrdinalIgnoreCase));
                if (get != null)
                {
                    string site = Opt(c, "/site"), err;
                    string dc = site != null ? lab.DcNames.FirstOrDefault(n => lab.DcUp(n) && WsText.Eq(lab.SiteOfDc(n), site)) : lab.LocateDc(c.M, null, out err);
                    if (dc == null) { c.L("Getting DC name failed: Status = 1355 0x54b ERROR_NO_SUCH_DOMAIN"); return 1; }
                    var s = lab.Server(dc);
                    c.L("           DC: \\\\" + s.M.Fqdn + "\n      Address: \\\\" + s.M.Ip + "\n     Dom Guid: 6f0e3c1a-2b7d-4e58-9a61-c3d2e1f0a9b8\n     Dom Name: " + lab.DomainDns + "\n  Forest Name: " + lab.DomainDns + "\n Dc Site Name: " + (lab.SiteOfDc(dc) ?? "") + "\nOur Site Name: " + (lab.SiteOfIp(c.M.Ip) ?? "(none)") + "\n        Flags: PDC GC DS LDAP KDC TIMESERV WRITABLE DNS_DC DNS_DOMAIN DNS_FOREST CLOSE_SITE FULL_SECRET WS DS_8 DS_9 DS_10\nThe command completed successfully");
                    return 0;
                }
                if (c.Args.Any(x => x.StartsWith("/dclist:", StringComparison.OrdinalIgnoreCase)))
                {
                    var sb = new StringBuilder("Get list of DCs in domain '" + lab.DomainDns + "' from '\\\\" + lab.Server(lab.Fsmo["PDCEmulator"]).M.Fqdn + "'.\n");
                    foreach (var n in lab.DcNames) sb.Append("    " + lab.Server(n).M.Fqdn + (WsText.Eq(n, lab.Fsmo["PDCEmulator"]) ? " [PDC] " : "       ") + " [DS] Site: " + (lab.SiteOfDc(n) ?? "") + "\n");
                    c.L(sb + "The command completed successfully"); return 0;
                }
                if (c.Args.Any(x => WsText.Eq(x, "/dsgetsite")))
                {
                    var site = lab.Dirs.ContainsKey(c.M.Name) ? lab.SiteOfDc(c.M.Name) : lab.SiteOfIp(c.M.Ip);
                    if (site == null) { c.L("Getting DC name failed: Status = 1919 0x77f ERROR_NO_SITENAME"); return 1; }
                    c.L(site + "\nThe command completed successfully"); return 0;
                }
                c.L("Cert Lab simulates nltest /dsgetdc:DOMAIN [/site:NAME], /dsgetsite and /dclist:DOMAIN."); return 1;
            });
            Add("netdom", null, c =>
            {
                var lab = c.M.Ws;
                var a = c.Args.Select(x => x.ToLowerInvariant()).ToList();
                if (lab != null && a.Count >= 2 && a[0] == "query" && a[1] == "fsmo")
                {
                    var names = new[] { "Schema master", "Domain naming master", "PDC", "RID pool manager", "Infrastructure master" };
                    var sb = new StringBuilder();
                    for (int i = 0; i < 5; i++) sb.Append(names[i].PadRight(28) + lab.Server(lab.Fsmo[WsLab.FsmoRoles[i]]).M.Fqdn + "\n");
                    c.L(sb + "The command completed successfully.\n"); return 0;
                }
                if (lab != null && a.Count >= 1 && a[0] == "trust")
                {
                    if (a.Contains("/verify")) { string dom = c.Args.Count > 1 ? c.Args[1] : ""; var t = lab.Trusts.FirstOrDefault(x => WsText.Eq(x.Name, dom) || WsText.Eq(x.Name, Opt(c, "/d"))); c.L(t == null ? "The trust between " + lab.DomainDns + " and " + (Opt(c, "/d") ?? dom) + " could not be verified: it doesn't exist." : "The trust between " + lab.DomainDns + " and " + t.Name + " has been successfully verified.\nThe command completed successfully."); return t == null ? 1 : 0; }
                }
                c.L("Cert Lab simulates netdom query fsmo and netdom trust ... /verify."); return 1;
            });
            RegisterWsNatives3();
        }
        static partial void RegisterWsNatives3();
        static string Delta(WsLab lab, string dc, bool source)
        {
            var times = lab.LastRepl.Where(kv => source ? kv.Key.StartsWith(dc + ">", StringComparison.OrdinalIgnoreCase) : kv.Key.EndsWith(">" + dc, StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Value).ToList();
            if (times.Count == 0) return "   01h:00m:00s";
            var d = lab.Now - times.Min();
            return "   " + ((int)d.TotalHours).ToString("00", CultureInfo.InvariantCulture) + "h:" + d.Minutes.ToString("00", CultureInfo.InvariantCulture) + "m:" + d.Seconds.ToString("00", CultureInfo.InvariantCulture) + "s";
        }
    }
}
