// Cert Lab - DFSR module (replication groups, folders, members, connections, memberships, polling Active Directory,
// the backlog) and ADSync module (Get / Set-ADSyncScheduler, Start-ADSyncSyncCycle) on the AZ-800 machines.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public partial class PsCmdlets
    {
        static DfsrGroup Rg(CmdCtx c)
        {
            var g = Lab(c).DfsrGroup(c.Str("GroupName"));
            if (g == null) throw c.Fail("The replication group " + c.Str("GroupName") + " was not found in Active Directory.", "Get-DfsReplicationGroup", "ObjectNotFound", c.Str("GroupName"), "System.ArgumentException");
            return g;
        }
        static PsObj RgObj(DfsrGroup g) { var p = new PsObj("Microsoft.DistributedFileSystemReplication.DfsReplicationGroup", "System.Object"); p.Val("GroupName", "string", g.Name); p.Val("DomainName", "string", "corp.example"); p.Val("Description", "string", g.Description); p.Val("Identifier", "guid", Guid.Empty); p.Val("State", "string", "Normal"); return p; }

        static void RegWsDfsr()
        {
            DfsCmd("New-DfsReplicationGroup", "DFSR", HasDfsr, "GroupName:string@0 !; Description:string; DomainName:string", (c, o) =>
            {
                var lab = Lab(c);
                if (lab.DfsrGroup(c.Str("GroupName")) != null) throw c.Fail("A replication group named " + c.Str("GroupName") + " already exists.", "New-DfsReplicationGroup", "ResourceExists", c.Str("GroupName"), "System.ArgumentException");
                var g = new DfsrGroup { Name = c.Str("GroupName"), Description = c.Str("Description") ?? "", Changed = lab.Now };
                lab.DfsrGroups.Add(g); c.Out(RgObj(g));
            });
            DfsCmd("Get-DfsReplicationGroup", "DFSR", HasDfsr, "GroupName:string[]@0", (c, o) => { foreach (var g in Lab(c).DfsrGroups.Where(x => !c.Has("GroupName") || c.Strs("GroupName").Any(n => WsText.Like(x.Name, n)))) c.Out(RgObj(g)); });
            DfsCmd("New-DfsReplicatedFolder", "DFSR", HasDfsr, "GroupName:string@0 !; FolderName:string@1 !; Description:string; DfsnPath:string", (c, o) =>
            {
                var g = Rg(c); string f = c.Str("FolderName");
                if (g.Folders.Contains(f, StringComparer.OrdinalIgnoreCase)) throw c.Fail("The replicated folder " + f + " already exists.", "New-DfsReplicatedFolder", "ResourceExists", f, "System.ArgumentException");
                g.Folders.Add(f); foreach (var m in g.Members) g.Memberships.Add(new DfsrMember { Server = m, Folder = f }); g.Changed = Lab(c).Now;
                var p = new PsObj("Microsoft.DistributedFileSystemReplication.DfsReplicatedFolder", "System.Object"); p.Val("GroupName", "string", g.Name); p.Val("FolderName", "string", f); p.Val("DfsnPath", "string", c.Str("DfsnPath") ?? ""); c.Out(p);
            });
            DfsCmd("Add-DfsrMember", "DFSR", HasDfsr, "GroupName:string@0 !; ComputerName:string[]@1 !; Description:string", (c, o) =>
            {
                var g = Rg(c); var lab = Lab(c);
                foreach (var n in c.Strs("ComputerName"))
                {
                    var s = lab.Server(n);
                    if (s == null || !s.Has("FS-DFS-Replication")) throw c.Fail("The DFS Replication service isn't installed on " + n + " (Install-WindowsFeature FS-DFS-Replication).", "Add-DfsrMember", "InvalidOperation", n, "System.ArgumentException");
                    if (g.Members.Contains(s.Name, StringComparer.OrdinalIgnoreCase)) continue;
                    g.Members.Add(s.Name); foreach (var f in g.Folders) g.Memberships.Add(new DfsrMember { Server = s.Name, Folder = f });
                    var p = new PsObj("Microsoft.DistributedFileSystemReplication.DfsrMember", "System.Object"); p.Val("GroupName", "string", g.Name); p.Val("ComputerName", "string", s.Name); p.Val("DnsName", "string", s.M.Fqdn); c.Out(p);
                }
                g.Changed = lab.Now;
            });
            DfsCmd("Get-DfsrMember", "DFSR", HasDfsr, "GroupName:string[]@0; ComputerName:string[]", (c, o) => { foreach (var g in Lab(c).DfsrGroups.Where(x => !c.Has("GroupName") || c.Strs("GroupName").Contains(x.Name, StringComparer.OrdinalIgnoreCase))) foreach (var m in g.Members) { var p = new PsObj("Microsoft.DistributedFileSystemReplication.DfsrMember", "System.Object"); p.Val("GroupName", "string", g.Name); p.Val("ComputerName", "string", m); c.Out(p); } });
            DfsCmd("Add-DfsrConnection", "DFSR", HasDfsr, "GroupName:string@0 !; SourceComputerName:string@1 !; DestinationComputerName:string@2 !; CreateOneWay:switch; DisableConnection:switch", (c, o) =>
            {
                var g = Rg(c); var lab = Lab(c);
                var a = lab.Server(c.Str("SourceComputerName")); var b = lab.Server(c.Str("DestinationComputerName"));
                if (a == null || b == null || !g.Members.Contains(a.Name, StringComparer.OrdinalIgnoreCase) || !g.Members.Contains(b.Name, StringComparer.OrdinalIgnoreCase)) throw c.Fail("Both computers must be members of the replication group (Add-DfsrMember).", "Add-DfsrConnection", "InvalidOperation", null, "System.ArgumentException");
                if (!g.Connections.Any(x => WsText.Eq(x[0], a.Name) && WsText.Eq(x[1], b.Name))) g.Connections.Add(new[] { a.Name, b.Name });
                if (!c.Sw("CreateOneWay") && !g.Connections.Any(x => WsText.Eq(x[0], b.Name) && WsText.Eq(x[1], a.Name))) g.Connections.Add(new[] { b.Name, a.Name });
                g.Changed = lab.Now;
            });
            DfsCmd("Set-DfsrMembership", "DFSR", HasDfsr, "GroupName:string@0 !; FolderName:string@1 !; ComputerName:string[]@2 !; ContentPath:string; PrimaryMember:bool; ReadOnly:bool; StagingPathQuotaInMB:uint32; Force:switch", (c, o) =>
            {
                var g = Rg(c); var lab = Lab(c);
                foreach (var n in c.Strs("ComputerName"))
                {
                    var s = lab.Server(n);
                    var m = s == null ? null : g.Memberships.FirstOrDefault(x => WsText.Eq(x.Server, s.Name) && WsText.Eq(x.Folder, c.Str("FolderName")));
                    if (m == null) throw c.Fail(n + " isn't a member of " + g.Name + " for the folder " + c.Str("FolderName") + ".", "Set-DfsrMembership", "ObjectNotFound", n, "System.ArgumentException");
                    if (c.Has("ContentPath")) { if (s.M.FindFile(c.Str("ContentPath")) == null) s.M.MakeDir(c.Str("ContentPath"), lab.Now); m.ContentPath = c.Str("ContentPath"); }
                    if (c.Has("PrimaryMember") && PsConv.ToBool(c.Get("PrimaryMember"))) { foreach (var x in g.Memberships.Where(y => WsText.Eq(y.Folder, m.Folder))) x.Primary = false; m.Primary = true; }
                    if (c.Has("ReadOnly")) m.ReadOnly = PsConv.ToBool(c.Get("ReadOnly"));
                }
                g.Changed = lab.Now;
            });
            DfsCmd("Get-DfsrMembership", "DFSR", HasDfsr, "GroupName:string[]@0; ComputerName:string[]", (c, o) =>
            {
                foreach (var g in Lab(c).DfsrGroups.Where(x => !c.Has("GroupName") || c.Strs("GroupName").Contains(x.Name, StringComparer.OrdinalIgnoreCase)))
                    foreach (var m in g.Memberships.Where(x => !c.Has("ComputerName") || c.Strs("ComputerName").Any(n => WsText.Eq(n, x.Server))))
                    { var p = new PsObj("Microsoft.DistributedFileSystemReplication.DfsrMembership", "System.Object"); p.Val("GroupName", "string", g.Name); p.Val("ComputerName", "string", m.Server); p.Val("FolderName", "string", m.Folder); p.Val("ContentPath", "string", m.ContentPath); p.Val("PrimaryMember", "bool", m.Primary); p.Val("ReadOnly", "bool", m.ReadOnly); p.Val("Enabled", "bool", m.Enabled); c.Out(p); }
            });
            DfsCmd("Update-DfsrConfigurationFromAD", "DFSR", HasDfsr, "ComputerName:string[]@0", (c, o) => { var lab = Lab(c); foreach (var n in c.Strs("ComputerName") ?? new[] { c.M.Name }) { var s = lab.Server(n); if (s != null) lab.DfsrPoll(s.Name); } });
            DfsCmd("Get-DfsrBacklog", "DFSR", HasDfsr, "GroupName:string !; FolderName:string !; SourceComputerName:string !; DestinationComputerName:string !", (c, o) =>
            {
                var g = Rg(c); var lab = Lab(c);
                var a = g.Memberships.FirstOrDefault(x => WsText.Eq(x.Server, c.Str("SourceComputerName")) && WsText.Eq(x.Folder, c.Str("FolderName"))); var b = g.Memberships.FirstOrDefault(x => WsText.Eq(x.Server, c.Str("DestinationComputerName")) && WsText.Eq(x.Folder, c.Str("FolderName")));
                if (a == null || b == null) throw c.Fail("Both computers must be members for the folder.", "Get-DfsrBacklog", "ObjectNotFound", null, "System.ArgumentException");
                var sa = lab.Server(a.Server).M.FindFile(a.ContentPath); var sb = lab.Server(b.Server).M.FindFile(b.ContentPath);
                var have = new HashSet<string>(StringComparer.OrdinalIgnoreCase); Action<WinFile, string, HashSet<string>> walk = null; walk = (f, p, set) => { if (f == null) return; foreach (var k in f.Kids.Values) { if (k.IsDir) walk(k, p + k.Name + "\\", set); else set.Add(p + k.Name); } };
                walk(sb, "", have); var want = new HashSet<string>(StringComparer.OrdinalIgnoreCase); walk(sa, "", want);
                foreach (var f in want.Where(x => !have.Contains(x))) { var p = new PsObj("Microsoft.DistributedFileSystemReplication.DfsrIdRecord", "System.Object"); p.Val("FileName", "string", f.Substring(f.LastIndexOf('\\') + 1)); p.Val("FullPathName", "string", a.ContentPath + "\\" + f); c.Out(p); }
            });
            // ------------------------------------------------------------ ADSync (Microsoft Entra Connect Sync)
            Func<PsMachine, bool> hasSync = m => m.Ws != null && m.Svc("ADSync") != null;
            DfsCmd("Get-ADSyncScheduler", "ADSync", hasSync, "", (c, o) =>
            {
                var z = Lab(c).Azure; var p = new PsObj("Microsoft.IdentityManagement.PowerShell.ObjectModel.SchedulerSettings", "System.Object");
                p.Val("AllowedSyncCycleInterval", "timespan", TimeSpan.FromMinutes(30)); p.Val("CurrentlyEffectiveSyncCycleInterval", "timespan", TimeSpan.FromMinutes(30)); p.Val("CustomizedSyncCycleInterval", "timespan", null);
                var due = Lab(c).Due("aadsync:delta"); p.Val("NextSyncCyclePolicyType", "string", "Delta"); p.Val("NextSyncCycleStartTimeInUTC", "datetime", due.HasValue ? due.Value : Lab(c).Now);
                p.Val("PurgeRunHistoryInterval", "timespan", TimeSpan.FromDays(7)); p.Val("SyncCycleEnabled", "bool", z.SyncEnabled); p.Val("MaintenanceEnabled", "bool", true); p.Val("StagingModeEnabled", "bool", z.Staging); p.Val("SchedulerSuspended", "bool", false); p.Val("SyncCycleInProgress", "bool", false);
                c.Out(p);
            });
            DfsCmd("Set-ADSyncScheduler", "ADSync", hasSync, "SyncCycleEnabled:bool; CustomizedSyncCycleInterval:timespan; NextSyncCyclePolicyType:string set=Delta|Initial", (c, o) => { var z = Lab(c).Azure; if (c.Has("SyncCycleEnabled")) z.SyncEnabled = PsConv.ToBool(c.Get("SyncCycleEnabled")); });
            DfsCmd("Start-ADSyncSyncCycle", "ADSync", hasSync, "PolicyType:string@0 set=Delta|Initial; InteractiveMode:bool", (c, o) =>
            {
                var z = Lab(c).Azure;
                if (!z.SyncEnabled) throw c.Fail("The sync scheduler is disabled (Set-ADSyncScheduler -SyncCycleEnabled $true).", "System.InvalidOperationException", "InvalidOperation", null, "System.InvalidOperationException");
                z.Sync(true);
                var p = new PsObj("System.Management.Automation.PSCustomObject", "System.Object"); p.Val("Result", "string", "Success"); c.Out(p);
            });
        }
    }
}
