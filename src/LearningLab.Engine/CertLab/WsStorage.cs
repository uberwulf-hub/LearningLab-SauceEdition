// Cert Lab - disks and Storage Spaces on the AZ-800 servers: physical disks (FS1 has four empty 100 GB disks that can
// be pooled), storage pools, virtual disks with their resiliency (simple, two- or three-way mirror, parity) and
// provisioning (fixed or thin), partitions and volumes (NTFS or ReFS), and Data Deduplication per volume (files older
// than the minimum age, 3 days by default, are optimized by the hourly background job or Start-DedupJob).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public class WsPart { public int Number; public string Letter = "", FileSystem = "", Label = ""; public long Size; }
    public class WsDisk
    {
        public int Number; public string FriendlyName = "", Bus = "SAS", Media = "HDD", Serial = "", Style = "RAW", Pool = "", VirtualDisk = "", Health = "Healthy";
        public long Size; public bool Offline;
        public readonly List<WsPart> Parts = new List<WsPart>();
        public bool CanPool { get { return VirtualDisk.Length == 0 && Pool.Length == 0 && Parts.Count == 0 && Number > 0 && Size >= 4L << 30; } }
        public long Free { get { return Size - Parts.Sum(p => p.Size) - (Style == "GPT" ? 128L << 20 : 0); } }
    }
    public class WsPool { public string Name = "", Health = "Healthy"; public readonly List<int> Disks = new List<int>(); }
    public class WsVDisk { public string Name = "", Pool = "", Resiliency = "Simple", Provisioning = "Fixed"; public int Copies = 1, Columns = 1; public long Size, Footprint; public int DiskNumber; }
    public class WsDedup { public string Volume = "", UsageType = "Default"; public bool Enabled = true; public int MinAgeDays = 3; public long Saved; public int Optimized, InPolicy; public DateTime LastRun; }

    public partial class WsServer
    {
        public readonly List<WsDisk> Disks = new List<WsDisk>();
        public readonly List<WsPool> Pools = new List<WsPool>();
        public readonly List<WsVDisk> VDisks = new List<WsVDisk>();
        public readonly Dictionary<string, WsDedup> Dedup = new Dictionary<string, WsDedup>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, string> VolumeFs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);   // letter -> NTFS / ReFS
        public WsDisk Disk(int n) { return Disks.FirstOrDefault(d => d.Number == n); }
        public WsPool Pool(string name) { return Pools.FirstOrDefault(p => WsText.Eq(p.Name, name)); }
        public WsVDisk VDisk(string name) { return VDisks.FirstOrDefault(v => WsText.Eq(v.Name, name)); }
        public long PoolSize(WsPool p) { return p.Disks.Sum(n => Disk(n).Size - (256L << 20)); }
        public long PoolUsed(WsPool p) { return VDisks.Where(v => WsText.Eq(v.Pool, p.Name)).Sum(v => v.Footprint); }
        public WsPart PartOf(string letter) { foreach (var d in Disks) foreach (var p in d.Parts) if (WsText.Eq(p.Letter, letter)) return p; return null; }
        public string NextLetter() { for (char c = 'D'; c <= 'Z'; c++) if (PartOf(c.ToString()) == null && !M.ExtraDrives.ContainsKey(c.ToString())) return c.ToString(); return null; }

        /// <summary>How much more a virtual disk takes from the pool than its size: mirror copies, parity's extra column.</summary>
        public static double Factor(WsVDisk v) { if (v.Resiliency == "Mirror") return v.Copies; if (v.Resiliency == "Parity") return v.Columns / (double)Math.Max(1, v.Columns - 1); return 1; }

        public string NewPool(string name, List<int> disks)
        {
            if (Pool(name) != null) return "A storage pool named " + name + " already exists.";
            if (disks.Count == 0) return "Select at least one physical disk that can be pooled.";
            foreach (var n in disks) { var d = Disk(n); if (d == null || !d.CanPool) return "The physical disk " + n + " can't be pooled (it is in use, has partitions, or is the system disk)."; }
            var p = new WsPool { Name = name };
            foreach (var n in disks) { p.Disks.Add(n); Disk(n).Pool = name; }
            Pools.Add(p);
            return null;
        }

        public string NewVDisk(string pool, string name, string resiliency, int copies, string prov, long size, bool max, out WsVDisk vd)
        {
            vd = null;
            var p = Pool(pool);
            if (p == null) return "There is no storage pool named " + pool + ".";
            if (VDisk(name) != null) return "A virtual disk named " + name + " already exists.";
            string res = WsText.Eq(resiliency, "Mirror") ? "Mirror" : WsText.Eq(resiliency, "Parity") ? "Parity" : WsText.Eq(resiliency, "Simple") || string.IsNullOrEmpty(resiliency) ? "Simple" : null;
            if (res == null) return "The resiliency setting must be Simple, Mirror or Parity.";
            int n = p.Disks.Count;
            if (res == "Mirror" && copies < 2) copies = 2;
            if (res == "Mirror" && copies == 2 && n < 2) return "A two-way mirror needs at least 2 physical disks in the pool.";
            if (res == "Mirror" && copies >= 3 && n < 5) return "A three-way mirror needs at least 5 physical disks in the pool.";
            if (res == "Parity" && n < 3) return "Parity needs at least 3 physical disks in the pool.";
            vd = new WsVDisk { Name = name, Pool = p.Name, Resiliency = res, Copies = res == "Mirror" ? copies : 1, Columns = res == "Parity" ? Math.Min(n, 8) : 1, Provisioning = WsText.Eq(prov, "Thin") ? "Thin" : "Fixed" };
            long free = PoolSize(p) - PoolUsed(p);
            if (max) size = (long)(free / Factor(vd)) / (1L << 30) * (1L << 30);
            if (size <= 0) return "Specify a size (-Size 50GB) or -UseMaximumSize.";
            vd.Size = size;
            vd.Footprint = vd.Provisioning == "Thin" ? 0 : (long)(size * Factor(vd));
            if (vd.Footprint > free) return "Not enough available capacity: the pool has " + Gb(free) + " free and this disk needs " + Gb(vd.Footprint) + " (" + res + (res == "Mirror" ? ", " + copies + " copies" : "") + ").";
            vd.DiskNumber = Disks.Max(d => d.Number) + 1;
            Disks.Add(new WsDisk { Number = vd.DiskNumber, FriendlyName = name, Size = size, Bus = "Spaces", VirtualDisk = name, Serial = "" });
            VDisks.Add(vd);
            return null;
        }
        public static string Gb(long b) { return (b / (double)(1L << 30)).ToString("0.##", CultureInfo.InvariantCulture) + " GB"; }

        public string NewPartition(int disk, long size, bool max, string letter, out WsPart part)
        {
            part = null;
            var d = Disk(disk);
            if (d == null) return "No MSFT_Disk objects found with property 'Number' equal to '" + disk + "'.";
            if (d.Style == "RAW") return "The disk has not been initialized. Run Initialize-Disk first.";
            long free = d.Free;
            if (max) size = free;
            if (size <= 0 || size > free) return "Not enough available capacity on disk " + disk + ".";
            if (!string.IsNullOrEmpty(letter) && (PartOf(letter) != null || M.ExtraDrives.ContainsKey(letter) || WsText.Eq(letter, "C"))) return "The drive letter " + letter + " is already in use.";
            part = new WsPart { Number = d.Parts.Count + 2, Size = size, Letter = letter ?? "" };
            d.Parts.Add(part);
            return null;
        }
        public string Format(string letter, string fs, string label)
        {
            var p = PartOf(letter);
            if (p == null) return "No volume with drive letter " + letter + ".";
            fs = WsText.Eq(fs, "ReFS") ? "ReFS" : "NTFS";
            p.FileSystem = fs; p.Label = label ?? "";
            var root = new WinFile { Name = letter + ":", IsDir = true, Attr = 22, Created = Lab.Now, Modified = Lab.Now, Accessed = Lab.Now };
            M.ExtraDrives[letter] = root; VolumeFs[letter] = fs;
            Acls.Remove(root); Lab.DefaultRootAcl(this, root);
            return null;
        }

        // ------------------------------------------------------------ data deduplication
        public string EnableDedup(string letter, string usage)
        {
            if (!Has("FS-Data-Deduplication")) return "The Data Deduplication feature isn't installed (Install-WindowsFeature FS-Data-Deduplication).";
            letter = letter.TrimEnd(':', '\\');
            if (WsText.Eq(letter, "C")) return "Data Deduplication can't be enabled on the system or boot volume.";
            if (!M.ExtraDrives.ContainsKey(letter)) return "The volume " + letter + ": doesn't exist.";
            WsDedup d; if (!Dedup.TryGetValue(letter, out d)) { d = new WsDedup { Volume = letter + ":" }; Dedup[letter] = d; }
            d.Enabled = true; d.UsageType = string.IsNullOrEmpty(usage) ? "Default" : usage;
            if (d.UsageType == "HyperV" || d.UsageType == "Backup") d.MinAgeDays = d.UsageType == "Backup" ? 0 : 3;
            Lab.ScheduleDedup(Name);
            return null;
        }
        /// <summary>An optimization job: every file older than the minimum age and bigger than 32 KB is optimized; copies of
        /// the same content (files with the same text tag) are stored once, unique content shrinks by about a quarter.</summary>
        public void Optimize(string letter)
        {
            WsDedup d; if (!Dedup.TryGetValue(letter, out d) || !d.Enabled) return;
            WinFile root; if (!M.ExtraDrives.TryGetValue(letter, out root)) return;
            var files = new List<WinFile>();
            Action<WinFile> walk = null; walk = f => { foreach (var k in f.Kids.Values) { if (k.IsDir) walk(k); else files.Add(k); } };
            walk(root);
            var eligible = files.Where(f => f.FakeSize > 32768 && f.Modified <= Lab.Now.AddDays(-d.MinAgeDays)).ToList();
            long saved = 0;
            foreach (var g in eligible.GroupBy(f => f.Text.Length > 0 ? f.Text : f.Name)) { long sz = g.First().FakeSize; saved += (g.Count() - 1) * sz + sz / 4; }
            d.Saved = saved; d.Optimized = eligible.Count; d.InPolicy = eligible.Count; d.LastRun = Lab.Now;
        }
    }

    public partial class WsLab
    {
        public void ScheduleDedup(string server)
        {
            At("dedup:" + server, Now.AddHours(1), () => { var s = Server(server); if (s != null && s.Running) foreach (var v in s.Dedup.Keys.ToList()) s.Optimize(v); ScheduleDedup(server); }, true);
        }
    }
}
