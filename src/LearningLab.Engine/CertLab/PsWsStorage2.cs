// Cert Lab - Storage module: virtual disks, disks, partitions and volumes; the Deduplication module (Data
// Deduplication per volume, jobs and savings).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public partial class PsCmdlets
    {
        static IEnumerable<KeyValuePair<WsDisk, WsPart>> Volumes(WsServer s) { foreach (var d in s.Disks) foreach (var p in d.Parts.Where(x => x.FileSystem.Length > 0 && x.FileSystem != "FAT32" || x.Letter.Length > 0)) yield return new KeyValuePair<WsDisk, WsPart>(d, p); }

        static void RegWsStorage2()
        {
            StCmd("New-VirtualDisk", "StoragePoolFriendlyName:string !; FriendlyName:string !; ResiliencySettingName:string set=Simple|Mirror|Parity; NumberOfDataCopies:int; Size:long; UseMaximumSize:switch; ProvisioningType:string set=Thin|Fixed; NumberOfColumns:int", (c, o) =>
            {
                var s = c.M.WsSrv; WsVDisk v;
                StOk(c, s.NewVDisk(c.Str("StoragePoolFriendlyName"), c.Str("FriendlyName"), c.Str("ResiliencySettingName"), c.Int("NumberOfDataCopies", 2), c.Str("ProvisioningType"), c.Long("Size", 0), c.Sw("UseMaximumSize"), out v), c.Str("FriendlyName"));
                c.Out(VDiskObj(s, v));
            });
            StCmd("Get-VirtualDisk", "FriendlyName:string[]@0", (c, o) => { var s = c.M.WsSrv; foreach (var v in s.VDisks.Where(x => !c.Has("FriendlyName") || c.Strs("FriendlyName").Any(n => WsText.Like(x.Name, n)))) c.Out(VDiskObj(s, v)); });
            var rv = StCmd("Remove-VirtualDisk", "FriendlyName:string[]@0 !", (c, o) =>
            {
                var s = c.M.WsSrv;
                foreach (var n in c.Strs("FriendlyName")) { var v = s.VDisk(n); if (v == null) StOk(c, "No virtual disk named " + n + ".", n); if (!c.ShouldProcess(n, "Delete virtual disk")) continue; var d = s.Disk(v.DiskNumber); foreach (var pt in d.Parts) s.M.ExtraDrives.Remove(pt.Letter); s.Disks.Remove(d); s.VDisks.Remove(v); }
            });
            rv.ShouldProcess = true; rv.ConfirmImpact = "High";
            StCmd("Get-Disk", "Number:uint32[]@0; VirtualDisk:object pipe; FriendlyName:string[]", (c, o) =>
            {
                var s = c.M.WsSrv;
                if (c.Has("VirtualDisk")) { c.Out(DiskObj(s, DiskFrom(c, s, c.Get("VirtualDisk")))); return; }
                foreach (var d in s.Disks.Where(x => (!c.Has("Number") || c.Objs("Number").Any(n => PsConv.ToInt(n) == x.Number)) && (!c.Has("FriendlyName") || c.Strs("FriendlyName").Any(n => WsText.Like(x.FriendlyName, n)))).OrderBy(x => x.Number)) c.Out(DiskObj(s, d));
            });
            StCmd("Initialize-Disk", "Number:uint32@0; InputObject:object pipe; PartitionStyle:string set=GPT|MBR", (c, o) =>
            {
                var s = c.M.WsSrv; var d = DiskFrom(c, s, c.Get("InputObject"));
                if (d.Style != "RAW") StOk(c, "The disk has already been initialized.", d.Number);
                d.Style = c.Str("PartitionStyle") ?? "GPT";
                if (c.Sw("PassThru")) c.Out(DiskObj(s, d));
            });
            StCmd("New-Partition", "DiskNumber:uint32@0 prop; InputObject:object pipe; Size:long; UseMaximumSize:switch; AssignDriveLetter:switch; DriveLetter:char", (c, o) =>
            {
                var s = c.M.WsSrv; var d = c.Has("InputObject") ? DiskFrom(c, s, c.Get("InputObject")) : DiskFrom(c, s, null);
                string letter = c.Has("DriveLetter") ? c.Str("DriveLetter").ToUpperInvariant() : c.Sw("AssignDriveLetter") ? s.NextLetter() : "";
                WsPart pt; StOk(c, s.NewPartition(d.Number, c.Long("Size", 0), c.Sw("UseMaximumSize"), letter, out pt), d.Number);
                c.Out(PartObj(d, pt));
            });
            StCmd("Get-Partition", "DiskNumber:uint32[]@0; DriveLetter:char[]", (c, o) =>
            {
                var s = c.M.WsSrv;
                foreach (var d in s.Disks.Where(x => !c.Has("DiskNumber") || c.Objs("DiskNumber").Any(n => PsConv.ToInt(n) == x.Number))) foreach (var pt in d.Parts.Where(x => !c.Has("DriveLetter") || c.Strs("DriveLetter").Any(l => WsText.Eq(l, x.Letter)))) c.Out(PartObj(d, pt));
            });
            var fv = StCmd("Format-Volume", "DriveLetter:char[]@0; Partition:object pipe; FileSystem:string set=NTFS|ReFS|FAT32|exFAT; NewFileSystemLabel:string; AllocationUnitSize:uint32; Full:switch; Force:switch", (c, o) =>
            {
                var s = c.M.WsSrv; string letter = null;
                var po = PsConv.Base(c.Get("Partition")) as PsObj; if (po != null && po.Base is WsPart) letter = ((WsPart)po.Base).Letter;
                if (letter == null && c.Has("DriveLetter")) letter = c.Strs("DriveLetter")[0].ToUpperInvariant();
                if (string.IsNullOrEmpty(letter)) StOk(c, "Cert Lab formats volumes that have a drive letter (New-Partition -AssignDriveLetter).", null);
                if (WsText.Eq(letter, "C")) StOk(c, "Formatting the system volume isn't allowed.", letter);
                if (!c.Sw("Force") && s.M.ExtraDrives.ContainsKey(letter) && s.M.ExtraDrives[letter].Kids.Count > 0 && !c.ShouldProcess(letter + ":", "Format volume (all data on it will be lost)")) return;
                StOk(c, s.Format(letter, c.Str("FileSystem") ?? "NTFS", c.Str("NewFileSystemLabel")), letter);
                c.Out(VolumeObj(s, s.PartOf(letter)));
            });
            fv.ShouldProcess = true; fv.ConfirmImpact = "High";
            StCmd("Get-Volume", "DriveLetter:char[]@0; FileSystemLabel:string[]", (c, o) =>
            {
                var s = c.M.WsSrv;
                foreach (var kv in Volumes(s).Where(x => (!c.Has("DriveLetter") || c.Strs("DriveLetter").Any(l => WsText.Eq(l, x.Value.Letter))) && (!c.Has("FileSystemLabel") || c.Strs("FileSystemLabel").Any(l => WsText.Like(x.Value.Label, l)))).OrderBy(x => x.Value.Letter.Length == 0 ? "~" : x.Value.Letter)) c.Out(VolumeObj(s, kv.Value));
            });
            StCmd("New-Volume", "StoragePoolFriendlyName:string !; FriendlyName:string !; FileSystem:string set=NTFS|ReFS; Size:long; UseMaximumSize:switch; ResiliencySettingName:string set=Simple|Mirror|Parity; NumberOfDataCopies:int; ProvisioningType:string set=Thin|Fixed; DriveLetter:char", (c, o) =>
            {
                var s = c.M.WsSrv; WsVDisk v;
                StOk(c, s.NewVDisk(c.Str("StoragePoolFriendlyName"), c.Str("FriendlyName"), c.Str("ResiliencySettingName"), c.Int("NumberOfDataCopies", 2), c.Str("ProvisioningType"), c.Long("Size", 0), c.Sw("UseMaximumSize") || !c.Has("Size"), out v), c.Str("FriendlyName"));
                s.Disk(v.DiskNumber).Style = "GPT";
                string letter = c.Has("DriveLetter") ? c.Str("DriveLetter").ToUpperInvariant() : s.NextLetter();
                WsPart pt; StOk(c, s.NewPartition(v.DiskNumber, 0, true, letter, out pt), letter);
                StOk(c, s.Format(letter, c.Str("FileSystem") ?? "ReFS", c.Str("FriendlyName")), letter);
                c.Out(VolumeObj(s, pt));
            });
            // ------------------------------------------------------------ Data Deduplication
            Func<PsMachine, bool> hasDedup = m => m.Ws != null && m.FeatureInstalled("FS-Data-Deduplication");
            Func<string, CmdletDef, CmdletDef> dd = (n, d) => { d.Available = hasDedup; return d; };
            Func<object, string> vol = v => PsConv.ToStr(PsConv.Base(v)).TrimEnd('\\', ':');
            dd("Enable-DedupVolume", Cdxml(Add("Enable-DedupVolume", "Deduplication", "Volume:string[]@0 !; UsageType:string set=Default|HyperV|Backup; DataAccess:switch; CimSession:object", (c, o) =>
            {
                var s = c.M.WsSrv;
                foreach (var v in c.Strs("Volume")) { StOk(c, s.EnableDedup(vol(v), c.Str("UsageType")), v); c.Out(DedupObj(s, s.Dedup[vol(v)])); }
            })));
            dd("Disable-DedupVolume", Cdxml(Add("Disable-DedupVolume", "Deduplication", "Volume:string[]@0 !; CimSession:object", (c, o) => { var s = c.M.WsSrv; foreach (var v in c.Strs("Volume")) { WsDedup d; if (s.Dedup.TryGetValue(vol(v), out d)) d.Enabled = false; } })));
            dd("Get-DedupVolume", Cdxml(Add("Get-DedupVolume", "Deduplication", "Volume:string[]@0; CimSession:object", (c, o) => { var s = c.M.WsSrv; foreach (var d in s.Dedup.Values.Where(x => !c.Has("Volume") || c.Strs("Volume").Any(v => WsText.Eq(vol(v), x.Volume.TrimEnd(':'))))) c.Out(DedupObj(s, d)); })));
            dd("Set-DedupVolume", Cdxml(Add("Set-DedupVolume", "Deduplication", "Volume:string[]@0 !; MinimumFileAgeDays:uint32; UsageType:string set=Default|HyperV|Backup; CimSession:object", (c, o) =>
            {
                var s = c.M.WsSrv;
                foreach (var v in c.Strs("Volume")) { WsDedup d; if (!s.Dedup.TryGetValue(vol(v), out d)) StOk(c, "Data Deduplication isn't enabled on " + v + ".", v); if (c.Has("MinimumFileAgeDays")) d.MinAgeDays = c.Int("MinimumFileAgeDays", 3); if (c.Has("UsageType")) d.UsageType = c.Str("UsageType"); }
            })));
            dd("Start-DedupJob", Cdxml(Add("Start-DedupJob", "Deduplication", "Volume:string[]@0 !; Type:string ! set=Optimization|GarbageCollection|Scrubbing|Unoptimization; Wait:switch; Full:switch; CimSession:object", (c, o) =>
            {
                var s = c.M.WsSrv;
                foreach (var v in c.Strs("Volume")) { WsDedup d; if (!s.Dedup.TryGetValue(vol(v), out d) || !d.Enabled) StOk(c, "Data Deduplication isn't enabled on " + v + ".", v); if (c.Str("Type") == "Optimization") s.Optimize(vol(v)); var j = new PsObj("Microsoft.Management.Infrastructure.CimInstance#ROOT/Microsoft/Windows/Deduplication/MSFT_DedupJob", "Microsoft.Management.Infrastructure.CimInstance"); j.Val("Type", "string", c.Str("Type")); j.Val("ScheduleType", "string", "Manual"); j.Val("StartTime", "datetime", s.Lab.Now); j.Val("Progress", "string", c.Sw("Wait") ? "100 %" : "0 %"); j.Val("State", "string", c.Sw("Wait") ? "Completed" : "Queued"); j.Val("Volume", "string", d.Volume); if (!c.Sw("Wait")) c.Out(j); }
            })));
            dd("Get-DedupStatus", Cdxml(Add("Get-DedupStatus", "Deduplication", "Volume:string[]@0; CimSession:object", (c, o) =>
            {
                var s = c.M.WsSrv;
                foreach (var d in s.Dedup.Values.Where(x => !c.Has("Volume") || c.Strs("Volume").Any(v => WsText.Eq(vol(v), x.Volume.TrimEnd(':')))))
                {
                    var p = new PsObj("Microsoft.Management.Infrastructure.CimInstance#ROOT/Microsoft/Windows/Deduplication/MSFT_DedupVolumeStatus", "Microsoft.Management.Infrastructure.CimInstance");
                    var pt = s.PartOf(d.Volume.TrimEnd(':')); long size = pt == null ? 0 : pt.Size;
                    var vo = pt == null ? null : VolumeObj(s, pt);
                    p.Val("FreeSpace", "uint64", vo == null ? 0UL : (ulong)vo.Get("SizeRemaining")); p.Val("SavedSpace", "uint64", (ulong)d.Saved); p.Val("OptimizedFilesCount", "uint64", (ulong)d.Optimized); p.Val("InPolicyFilesCount", "uint64", (ulong)d.InPolicy); p.Val("LastOptimizationTime", "System.Nullable[datetime]", d.LastRun == DateTime.MinValue ? null : (object)d.LastRun); p.Val("Volume", "string", d.Volume);
                    p.DefaultDisplay = new[] { "FreeSpace", "SavedSpace", "OptimizedFilesCount", "InPolicyFilesCount", "Volume" };
                    c.Out(p);
                }
            })));
        }
        static PsObj DedupObj(WsServer s, WsDedup d)
        {
            var p = new PsObj("Microsoft.Management.Infrastructure.CimInstance#ROOT/Microsoft/Windows/Deduplication/MSFT_DedupVolume", "Microsoft.Management.Infrastructure.CimInstance");
            p.Val("Enabled", "bool", d.Enabled); p.Val("UsageType", "string", d.UsageType); p.Val("SavedSpace", "uint64", (ulong)d.Saved); var spt = s.PartOf(d.Volume.TrimEnd(':')); var svo = spt == null ? null : VolumeObj(s, spt); long used = svo == null ? 0 : spt.Size - Convert.ToInt64(svo.Get("SizeRemaining")); p.Val("SavingsRate", "uint32", used + d.Saved <= 0 ? 0u : (uint)(d.Saved * 100 / (used + d.Saved))); p.Val("Volume", "string", d.Volume); p.Val("MinimumFileAgeDays", "uint32", (uint)d.MinAgeDays);
            p.DefaultDisplay = new[] { "Enabled", "UsageType", "SavedSpace", "SavingsRate", "Volume" };
            return p;
        }
    }
}
