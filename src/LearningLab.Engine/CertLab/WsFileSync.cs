// Cert Lab - Azure File Sync in the pretend subscription: a Storage Sync Service, servers registered to it (the agent's
// FileSyncSvc runs on them), sync groups with one cloud endpoint (an Azure file share) and server endpoints (a folder on
// a registered server, not on the system volume). Every 15 minutes (or on "sync now") files go to the cloud share and
// from it to every server endpoint. Cloud tiering keeps only a pointer on the server for files not opened for the
// number of days the date policy says (the free-space policy is shown, not enforced, in Cert Lab).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public class SyncServerEndpoint { public string Server = "", Path = ""; public bool Tiering; public int FreeSpacePercent = 20, TierDays; }
    public class SyncGroup
    {
        public string Name = "", StorageAccount = "", FileShare = "";
        public readonly List<SyncServerEndpoint> Servers = new List<SyncServerEndpoint>();
        public readonly WinFile Cloud = new WinFile { Name = "share", IsDir = true };
    }

    public partial class WsAzure
    {
        public string SyncService = "";
        public readonly List<string> SyncRegistered = new List<string>();
        public readonly List<SyncGroup> SyncGroups = new List<SyncGroup>();
        public readonly Dictionary<string, string> StorageAccounts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { { "stharborfiles", "salesarchive,hrarchive" } };
        public SyncGroup SyncGroupNamed(string n) { return SyncGroups.FirstOrDefault(g => WsText.Eq(g.Name, n)); }

        public string CreateSyncService(string name)
        {
            if (SyncService.Length > 0) return "This subscription already has the Storage Sync Service " + SyncService + " (Cert Lab keeps one).";
            if (string.IsNullOrEmpty(name)) return "Give the Storage Sync Service a name.";
            SyncService = name; return null;
        }
        public string RegisterServer(string server)
        {
            var s = Lab.Server(server);
            if (SyncService.Length == 0) return "Create a Storage Sync Service first.";
            if (s == null || s.IsClient) return "Azure File Sync registers Windows Server machines.";
            if (s.M.Svc("FileSyncSvc") == null) { var sv = new WinService { Name = "FileSyncSvc", DisplayName = "Storage Sync Agent", StartType = "Automatic", Exe = "filesyncsvc", BinaryPath = "\"C:\\Program Files\\Azure\\StorageSyncAgent\\FileSyncSvc.exe\"", Feature = true }; s.M.Services.Add(sv); s.M.StartService(sv, true); }
            if (!SyncRegistered.Contains(s.Name)) SyncRegistered.Add(s.Name);
            return null;
        }
        public string CreateSyncGroup(string name, string account, string share)
        {
            if (SyncService.Length == 0) return "Create a Storage Sync Service first.";
            if (SyncGroupNamed(name) != null) return "A sync group named " + name + " already exists.";
            string shares; if (!StorageAccounts.TryGetValue(account ?? "", out shares)) return "There is no storage account " + account + " in the subscription.";
            if (!shares.Split(',').Contains(share, StringComparer.OrdinalIgnoreCase)) return "The storage account " + account + " has no file share " + share + ".";
            if (SyncGroups.Any(g => WsText.Eq(g.StorageAccount, account) && WsText.Eq(g.FileShare, share))) return "That Azure file share is already the cloud endpoint of another sync group.";
            SyncGroups.Add(new SyncGroup { Name = name, StorageAccount = account, FileShare = share });
            return null;
        }
        public string AddServerEndpoint(string group, string server, string path, bool tiering, int freePct, int days)
        {
            var g = SyncGroupNamed(group);
            if (g == null) return "There is no sync group " + group + ".";
            var s = Lab.Server(server);
            if (s == null || !SyncRegistered.Contains(s.Name)) return server + " isn't registered with the Storage Sync Service (install the agent and register the server).";
            if (string.IsNullOrEmpty(path) || path.StartsWith("C:", StringComparison.OrdinalIgnoreCase)) return "A server endpoint can't be on the system volume (C:).";
            if (s.M.FindFile(path) == null) return "The path " + path + " doesn't exist on " + s.Name + ".";
            if (g.Servers.Any(x => WsText.Eq(x.Server, s.Name))) return s.Name + " already has a server endpoint in " + group + " (one per server in a sync group).";
            g.Servers.Add(new SyncServerEndpoint { Server = s.Name, Path = path, Tiering = tiering, FreeSpacePercent = freePct > 0 ? freePct : 20, TierDays = days });
            SyncNow(g);
            ScheduleFileSync();
            return null;
        }
        public void SyncNow(SyncGroup g)
        {
            foreach (var ep in g.Servers) { var s = Lab.Server(ep.Server); var f = s == null ? null : s.M.FindFile(ep.Path); if (f != null && s.Running && s.SvcRunning("FileSyncSvc")) WsLab.CopyTree(f, g.Cloud, false); }
            foreach (var ep in g.Servers)
            {
                var s = Lab.Server(ep.Server); if (s == null || !s.Running || !s.SvcRunning("FileSyncSvc")) continue;
                var f = s.M.FindFile(ep.Path); if (f == null) continue;
                WsLab.CopyTree(g.Cloud, f, false);
                if (ep.Tiering && ep.TierDays > 0) Tier(f, Lab.Now.AddDays(-ep.TierDays));
            }
        }
        static void Tier(WinFile dir, DateTime before)
        {
            foreach (var k in dir.Kids.Values)
            {
                if (k.IsDir) { Tier(k, before); continue; }
                if (k.Accessed < before) k.Attr |= 0x1000 | 0x400000;   // Offline + RecallOnDataAccess: only a pointer stays on the server
            }
        }
        public static bool Tiered(WinFile f) { return (f.Attr & 0x1000) != 0; }
        public void ScheduleFileSync() { Lab.At("afs:sync", Lab.Now.AddMinutes(15), () => { foreach (var g in SyncGroups) SyncNow(g); ScheduleFileSync(); }, false); }
    }
}
