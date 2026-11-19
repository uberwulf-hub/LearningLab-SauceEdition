// Cert Lab - storage: virtual disks with MBR (dos) or GPT partition tables, what is written on each device
// (a file system, swap or an LVM physical volume), LVM volume groups and logical volumes with real extent maths,
// mounting (devices, UUID= / LABEL=, NFS, bind, tmpfs), /etc/fstab at boot, swap, and autofs.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CertLab
{
    public class Disk
    {
        public string Name;            // vda
        public long Bytes;
        public string Label = "";      // "" none, dos, gpt
        public string DiskId = "";     // dos: 0x1234abcd, gpt: a GUID
        public List<Part> Parts = new List<Part>();
        public int Index;
        public long Sectors { get { return Bytes / 512; } }
    }
    public class Part
    {
        public int Num; public long Start, End;   // sectors, inclusive
        public string Type = "83";                // dos: hex id; gpt: linux, lvm, swap, efi, bios, msdata, raid
        public string Name = "";                  // gpt partition name
        public string PartUuid = "";
        public bool Boot;
        public long Bytes { get { return (End - Start + 1) * 512; } }
    }
    public class BlockContent
    {
        public string Kind;            // xfs ext4 vfat swap LVM2_member
        public string FsId; public string Uuid = ""; public string Label = "";
        public bool Corrupt;
    }
    public class VolGroup
    {
        public string Name; public string Uuid; public long PeSize = 4L * 1024 * 1024;
        public List<string> Pvs = new List<string>();
        public List<Lv> Lvs = new List<Lv>();
        public long Seq = 1;
    }
    public class Lv
    {
        public string Name; public string Uuid; public bool Active = true; public int Dm;
        public List<LvSeg> Segs = new List<LvSeg>();
        public long Extents { get { return Segs.Sum(s => s.Count); } }
    }
    public class LvSeg { public string Pv; public long Start, Count; }

    public class BDev
    {
        public string Path, Name, Type, Key, Parent = "";
        public int Major, Minor; public long Bytes;
        public Disk Disk; public Part Part; public VolGroup Vg; public Lv Lv;
    }

    public partial class Machine
    {
        public Dictionary<string, FileSys> RunFs = new Dictionary<string, FileSys>();
        [NonSerialized] public string LastMountWarning;
        public int NextDm;

        // ------------------------------------------------------------ devices
        public List<BDev> Devices()
        {
            var l = new List<BDev>();
            foreach (var d in Disks)
            {
                if (d.Name.StartsWith("sr", StringComparison.Ordinal)) { l.Add(new BDev { Path = "/dev/" + d.Name, Name = d.Name, Type = "rom", Key = d.Name + "@0", Major = 11, Minor = 0, Bytes = d.Bytes, Disk = d }); continue; }
                l.Add(new BDev { Path = "/dev/" + d.Name, Name = d.Name, Type = "disk", Key = d.Name + "@0", Major = 252, Minor = d.Index * 16, Bytes = d.Bytes, Disk = d });
                foreach (var p in d.Parts.OrderBy(x => x.Num))
                    l.Add(new BDev { Path = "/dev/" + d.Name + p.Num, Name = d.Name + p.Num, Type = "part", Key = d.Name + "@" + p.Start, Major = 252, Minor = d.Index * 16 + p.Num, Bytes = (p.Type == "5" || p.Type == "f") ? 1024 : p.Bytes, Disk = d, Part = p, Parent = d.Name });
            }
            foreach (var vg in Vgs)
                foreach (var lv in vg.Lvs)
                    if (lv.Active)
                        l.Add(new BDev { Path = "/dev/mapper/" + DmName(vg.Name, lv.Name), Name = DmName(vg.Name, lv.Name), Type = "lvm", Key = "lv:" + lv.Uuid, Major = 253, Minor = lv.Dm, Bytes = lv.Extents * vg.PeSize, Vg = vg, Lv = lv });
            return l;
        }
        public static string DmName(string vg, string lv) { return vg.Replace("-", "--") + "-" + lv.Replace("-", "--"); }

        /// <summary>A device from what people type: /dev/vdb1, /dev/vg/lv, /dev/mapper/vg-lv, /dev/dm-0, UUID=, LABEL=, PARTUUID=.</summary>
        public BDev FindDev(string spec)
        {
            if (string.IsNullOrEmpty(spec)) return null;
            spec = spec.Trim().Trim('"');
            var all = Devices();
            if (spec.StartsWith("UUID=", StringComparison.OrdinalIgnoreCase))
            {
                string u = spec.Substring(5).Trim('"').ToLowerInvariant();
                return all.FirstOrDefault(d => { var c = ContentOf(d); return c != null && c.Uuid.ToLowerInvariant() == u; });
            }
            if (spec.StartsWith("LABEL=", StringComparison.OrdinalIgnoreCase))
            {
                string lb = spec.Substring(6).Trim('"');
                return all.FirstOrDefault(d => { var c = ContentOf(d); return c != null && c.Label == lb && lb.Length > 0; });
            }
            if (spec.StartsWith("PARTUUID=", StringComparison.OrdinalIgnoreCase))
            {
                string u = spec.Substring(9).Trim('"').ToLowerInvariant();
                return all.FirstOrDefault(d => d.Part != null && d.Part.PartUuid.ToLowerInvariant() == u);
            }
            string p = spec;
            if (p.StartsWith("/dev/disk/by-uuid/", StringComparison.Ordinal)) return FindDev("UUID=" + p.Substring(18));
            if (p.StartsWith("/dev/disk/by-label/", StringComparison.Ordinal)) return FindDev("LABEL=" + p.Substring(19));
            var m = Regex.Match(p, "^/dev/dm-(\\d+)$");
            if (m.Success) { int n = int.Parse(m.Groups[1].Value); return all.FirstOrDefault(d => d.Type == "lvm" && d.Minor == n); }
            m = Regex.Match(p, "^/dev/([^/]+)/([^/]+)$");
            if (m.Success && m.Groups[1].Value != "mapper" && m.Groups[1].Value != "disk")
            {
                var vg = Vgs.FirstOrDefault(v => v.Name == m.Groups[1].Value);
                if (vg != null) return all.FirstOrDefault(d => d.Vg == vg && d.Lv.Name == m.Groups[2].Value);
            }
            return all.FirstOrDefault(d => d.Path == p);
        }

        public BlockContent ContentOf(BDev d) { if (d == null) return null; BlockContent c; Content.TryGetValue(d.Key, out c); return c; }

        public string DevPathOfKey(string key)
        {
            var d = Devices().FirstOrDefault(x => x.Key == key);
            return d == null ? "[unknown]" : d.Path;
        }
        public BDev DevOfKey(string key) { return Devices().FirstOrDefault(x => x.Key == key); }

        /// <summary>Keeps /dev in step with the devices (nodes, /dev/mapper, /dev/VG/LV links, /dev/disk/by-uuid).</summary>
        public void SyncDev()
        {
            FileSys fs; Inode node; Mount mnt;
            V.LocateGlobal("/dev", out fs, out node, out mnt);
            if (fs == null || fs.Type != "devtmpfs") return;
            var root = fs.Root;
            // remove old block nodes and LVM folders
            foreach (var k in root.Kids.Keys.ToList()) { var n = fs.Node(root.Kids[k]); if (n != null && (n.T == 'b' || (n.T == 'l' && k.StartsWith("dm", StringComparison.Ordinal)))) { root.Kids.Remove(k); fs.Nodes.Remove(n.Ino); } }
            foreach (var vg in Vgs) RemoveDevDir(fs, root, vg.Name);
            RemoveDevDir(fs, root, "mapper");
            RemoveDevDir(fs, root, "disk");
            Func<Inode, string, Inode> dir = (parent, name) =>
            {
                int ino;
                if (parent.Kids.TryGetValue(name, out ino)) return fs.Node(ino);
                var d = fs.NewNode('d', 0x1ED, 0, 0, Now); d.Ctx = "system_u:object_r:device_t:s0"; parent.Kids[name] = d.Ino; parent.Nlink++; return d;
            };
            Action<Inode, string, string> link = (parent, name, target) =>
            {
                var n = fs.NewNode('l', 0x1FF, 0, 0, Now); n.Target = target; n.Ctx = "system_u:object_r:device_t:s0"; parent.Kids[name] = n.Ino;
            };
            var mapper = dir(root, "mapper");
            var byUuid = dir(dir(root, "disk"), "by-uuid");
            foreach (var d in Devices())
            {
                if (d.Type == "lvm")
                {
                    string dm = "dm-" + d.Minor;
                    var n = fs.NewNode('b', 0x1B0, 0, 6, Now); n.Dev = dm; n.Ctx = "system_u:object_r:fixed_disk_device_t:s0"; root.Kids[dm] = n.Ino;
                    link(mapper, d.Name, "../" + dm);
                    link(dir(root, d.Vg.Name), d.Lv.Name, "../" + dm);
                }
                else
                {
                    var n = fs.NewNode('b', 0x1B0, 0, 6, Now); n.Dev = d.Name; n.Ctx = "system_u:object_r:fixed_disk_device_t:s0"; root.Kids[d.Name] = n.Ino;
                }
                var c = ContentOf(d);
                if (c != null && c.Uuid.Length > 0 && c.Kind != "LVM2_member") link(byUuid, c.Uuid, "../../" + (d.Type == "lvm" ? "dm-" + d.Minor : d.Name));
            }
            if (!mapper.Kids.ContainsKey("control")) { var n = fs.NewNode('c', 0x180, 0, 0, Now); n.Dev = "control"; mapper.Kids["control"] = n.Ino; }
        }
        void RemoveDevDir(FileSys fs, Inode root, string name)
        {
            int ino;
            if (!root.Kids.TryGetValue(name, out ino)) return;
            var d = fs.Node(ino);
            if (d != null && d.T == 'd') { foreach (var k in d.Kids.Values) { var c = fs.Node(k); if (c != null && c.T == 'd') foreach (var kk in c.Kids.Values) fs.Nodes.Remove(kk); fs.Nodes.Remove(k); } fs.Nodes.Remove(ino); }
            root.Kids.Remove(name);
        }

        public string ProcSwaps()
        {
            var sb = new StringBuilder("Filename\t\t\t\tType\t\tSize\t\tUsed\t\tPriority\n");
            foreach (var s in Swaps)
            {
                bool file = !s.Dev.StartsWith("/dev/", StringComparison.Ordinal);
                sb.Append(s.Dev.PadRight(40) + (file ? "file" : "partition").PadRight(16) + (s.Bytes / 1024 - 4).ToString().PadRight(16) + s.UsedKb.ToString().PadRight(16) + s.Prio + "\n");
            }
            return sb.ToString();
        }
        public string ProcMounts()
        {
            var sb = new StringBuilder();
            foreach (var m in Mounts) sb.Append(m.Source + " " + m.Path + " " + m.FsType + " " + m.Opts + " 0 0\n");
            return sb.ToString();
        }
        public string ProcPartitions()
        {
            var sb = new StringBuilder("major minor  #blocks  name\n\n");
            foreach (var d in Devices()) sb.Append(d.Major.ToString().PadLeft(4) + d.Minor.ToString().PadLeft(8) + (d.Bytes / 1024).ToString().PadLeft(11) + " " + (d.Type == "lvm" ? "dm-" + d.Minor : d.Name) + "\n");
            return sb.ToString();
        }

        /// <summary>Is a device in use (mounted, swap, a PV in a VG, or a disk with partitions in use)?</summary>
        public string InUse(BDev d)
        {
            if (Mounts.Any(m => m.Source == d.Path || (FindDev(m.Source) != null && FindDev(m.Source).Key == d.Key))) return "mounted";
            if (Swaps.Any(s => s.Dev == d.Path || (FindDev(s.Dev) != null && FindDev(s.Dev).Key == d.Key))) return "swap";
            return null;
        }

        // ------------------------------------------------------------ file systems on devices
        public FileSys NewFs(string type, long bytes, string dev)
        {
            var fs = new FileSys { Id = "fs" + NewHex(10), Type = type, Device = dev };
            long mib = 1024 * 1024;
            if (type == "xfs") { fs.Bytes = Math.Max(16 * mib, bytes - Math.Min(bytes / 50, 64 * mib) - 2 * mib); fs.BaseUsed = Math.Min(fs.Bytes / 4, 6 * mib + bytes / 160); }
            else if (type == "ext4") { fs.Bytes = (long)(bytes * 0.952); fs.BaseUsed = 24 * 1024; }
            else if (type == "vfat") { fs.Bytes = bytes - Math.Max(512 * 1024, bytes / 256); fs.BaseUsed = 0; }
            else fs.Bytes = bytes;
            var r = new Inode { Ino = 1, T = 'd', Mode = 0x1ED, Kids = new Dictionary<string, int>(StringComparer.Ordinal), Nlink = 2, Mtime = Now, Ctime = Now, Atime = Now, Ctx = "system_u:object_r:unlabeled_t:s0" };
            fs.Nodes[1] = r;
            if (type == "ext4")
            {
                var lf = fs.NewNode('d', 0x1C0, 0, 0, Now); lf.Ctx = "system_u:object_r:unlabeled_t:s0";
                r.Kids["lost+found"] = lf.Ino; r.Nlink++;
            }
            Fss[fs.Id] = fs;
            return fs;
        }

        public void SetContent(BDev d, BlockContent c)
        {
            BlockContent old;
            if (Content.TryGetValue(d.Key, out old) && old.FsId != null && old.FsId != (c == null ? null : c.FsId)) Fss.Remove(old.FsId);
            if (c == null) Content.Remove(d.Key); else Content[d.Key] = c;
        }

        public string FsUuid(string kind)
        {
            if (kind == "vfat") { var h = NewHex(8).ToUpperInvariant(); return h.Substring(0, 4) + "-" + h.Substring(4); }
            return NewUuid();
        }

        // ------------------------------------------------------------ mounting
        public static readonly HashSet<string> KnownMountOpts = new HashSet<string> { "defaults", "rw", "ro", "auto", "noauto", "user", "nouser", "users", "owner", "exec", "noexec", "suid", "nosuid", "dev", "nodev", "sync", "async", "atime", "noatime", "relatime", "norelatime", "strictatime", "diratime", "nodiratime", "lazytime", "nofail", "_netdev", "remount", "bind", "rbind", "discard", "nodiscard", "acl", "noacl", "user_xattr", "nouser_xattr", "errors", "data", "barrier", "nobarrier", "uquota", "gquota", "pquota", "usrquota", "grpquota", "prjquota", "quota", "noquota", "inode64", "attr2", "noattr2", "logbufs", "logbsize", "allocsize", "largeio", "nolargeio", "uid", "gid", "umask", "dmask", "fmask", "context", "fscontext", "defcontext", "rootcontext", "vers", "nfsvers", "proto", "hard", "soft", "timeo", "retrans", "rsize", "wsize", "sec", "intr", "nointr", "nolock", "lock", "actimeo", "port", "mode", "size", "nr_inodes", "commit", "journal_checksum", "iocharset", "shortname", "codepage", "utf8", "fsck", "x-systemd", "comment", "pri", "nfs4", "bg", "fg", "retry", "ac", "noac", "namlen", "addr", "clientaddr", "minorversion", "local_lock", "lookupcache", "fsc", "nofsc", "sloppy", "max_batch_time", "min_batch_time", "stripe", "nombcache", "inlinecrypt", "dax", "noinit_itable", "init_itable", "journal_dev", "noload", "norecovery", "nouuid", "wsync", "swalloc", "noswalloc", "filestreams", "ikeep", "noikeep" };

        public static string BadOption(string opts)
        {
            foreach (var o0 in (opts ?? "").Split(','))
            {
                var o = o0.Trim();
                if (o.Length == 0) continue;
                if (o.StartsWith("x-", StringComparison.Ordinal) || o.StartsWith("comment=", StringComparison.Ordinal)) continue;
                var k = o.Split('=')[0];
                if (!KnownMountOpts.Contains(k)) return o;
            }
            return null;
        }

        /// <summary>Mounts something; returns null or the error message (mount's wording).</summary>
        public string MountFs(string source, string target, string type, string opts, PCtx who)
        {
            opts = string.IsNullOrEmpty(opts) ? "defaults" : opts;
            var optList = opts.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            bool ro = optList.Contains("ro");
            string gtarget = Vfs.Norm(target);
            // the mount point
            Loc tl;
            try { tl = V.Resolve(who, target, true); }
            catch (VfsEx) { return "mount: " + target + ": mount point does not exist."; }
            if (!tl.Exists) return "mount: " + target + ": mount point does not exist.";
            if (tl.Node.T != 'd' && !optList.Contains("bind")) return "mount: " + target + ": mount point is not a directory.";
            gtarget = tl.GPath;
            string bad = BadOption(opts);
            if (optList.Contains("bind") || optList.Contains("rbind"))
            {
                Loc sl;
                try { sl = V.Resolve(who, source, true); } catch (VfsEx) { return "mount: " + target + ": special device " + source + " does not exist."; }
                if (!sl.Exists) return "mount: " + target + ": special device " + source + " does not exist.";
                Mounts.Add(new Mount { Path = gtarget, FsId = sl.Fs.Id, RootIno = sl.Node.Ino, Source = sl.GPath, FsType = sl.Fs.Type, Opts = ro ? "ro" : "rw", Ro = ro, Bind = true });
                return null;
            }
            if (type == "tmpfs" || source == "tmpfs")
            {
                var fs = new FileSys { Id = "tmp" + NewHex(8), Type = "tmpfs", Bytes = 1900L * 1024 * 1024 };
                fs.Nodes[1] = new Inode { Ino = 1, T = 'd', Mode = 0x3FF, Kids = new Dictionary<string, int>(StringComparer.Ordinal), Nlink = 2, Ctx = "system_u:object_r:tmpfs_t:s0", Mtime = Now, Ctime = Now, Atime = Now };
                RunFs[fs.Id] = fs;
                Mounts.Add(new Mount { Path = gtarget, FsId = fs.Id, RootIno = 1, Source = "tmpfs", FsType = "tmpfs", Opts = MountOptsText(optList, "tmpfs"), Ro = ro });
                return null;
            }
            // NFS: server:/export
            int colon = source.IndexOf(':');
            if ((type == "nfs" || type == "nfs4" || (colon > 0 && !source.StartsWith("/", StringComparison.Ordinal) && !source.Contains("="))) && colon > 0)
            {
                if (!PackageInstalled("nfs-utils")) return "mount: " + target + ": bad option; for several filesystems (e.g. nfs, cifs) you might need a /sbin/mount.<type> helper program.";
                string host = source.Substring(0, colon), path = source.Substring(colon + 1);
                string ip = Net.Resolve(this, host);
                if (ip == null) return "mount.nfs: Failed to resolve server " + host + ": Name or service not known";
                if (!Net.Reachable(this, ip)) return "mount.nfs: No route to host";
                if (ip != Net.UtilityIp) return "mount.nfs: Connection refused";
                var ex = Net.FindExport(path);
                string fsid; int rootIno = 1;
                if (ex == null)
                {
                    // a folder inside an export (e.g. /exports/home)
                    var parent = Net.Exports.Where(e => e.Path.StartsWith(path.TrimEnd('/') + "/", StringComparison.Ordinal)).ToList();
                    if (parent.Count == 0) return "mount.nfs: mounting " + source + " failed, reason given by server: No such file or directory";
                    var vfs = Net.VirtualNfsDir(path.TrimEnd('/'));
                    fsid = vfs.Id;
                }
                else fsid = ex.FsId;
                if (bad != null && bad.Split('=')[0] != "vers") return "mount.nfs: an incorrect mount option was specified";
                var nfsOpts = (ro || (ex != null && ex.Opts.StartsWith("ro", StringComparison.Ordinal)) ? "ro" : "rw") + ",relatime,vers=4.2,rsize=262144,wsize=262144,namlen=255,hard,proto=tcp,timeo=600,retrans=2,sec=sys,clientaddr=" + (FirstV4() ?? "0.0.0.0") + ",local_lock=none,addr=" + ip;
                Mounts.Add(new Mount { Path = gtarget, FsId = fsid, RootIno = rootIno, Source = host + ":" + path, FsType = "nfs4", Opts = nfsOpts, Ro = ro || (ex != null && ex.Opts.StartsWith("ro", StringComparison.Ordinal)) });
                return null;
            }
            var d = FindDev(source);
            if (d == null)
            {
                if (source.StartsWith("UUID=", StringComparison.Ordinal) || source.StartsWith("LABEL=", StringComparison.Ordinal)) return "mount: " + target + ": can't find " + source + ".";
                return "mount: " + target + ": special device " + source + " does not exist.";
            }
            var c = ContentOf(d);
            if (c == null || c.Kind == "LVM2_member" || c.Kind == "swap")
            {
                if (c != null && c.Kind == "swap") return "mount: " + target + ": unknown filesystem type 'swap'.";
                if (c != null && c.Kind == "LVM2_member") return "mount: " + target + ": unknown filesystem type 'LVM2_member'.";
                return "mount: " + target + ": wrong fs type, bad option, bad superblock on " + d.Path + ", missing codepage or helper program, or other error.";
            }
            if (!string.IsNullOrEmpty(type) && type != "auto" && type != c.Kind && !(type == "ext3" && c.Kind == "ext4") && !(type == "msdos" && c.Kind == "vfat"))
                return "mount: " + target + ": wrong fs type, bad option, bad superblock on " + d.Path + ", missing codepage or helper program, or other error.";
            if (bad != null) return "mount: " + target + ": wrong fs type, bad option, bad superblock on " + d.Path + ", missing codepage or helper program, or other error.";
            if (c.Corrupt) return "mount: " + target + ": mount(2) system call failed: Structure needs cleaning.";
            if (c.Kind == "iso9660")
            {
                if (Mounts.Any(x => x.Path == gtarget && x.FsId == c.FsId)) return "mount: " + target + ": " + d.Path + " already mounted on " + gtarget + ".";
                if (!ro) LastMountWarning = "mount: " + target + ": WARNING: source write-protected, mounted read-only.";
                Mounts.Add(new Mount { Path = gtarget, FsId = c.FsId, RootIno = 1, Source = d.Path, FsType = "iso9660", Opts = "ro,relatime,nojoliet,check=s,map=n,blocksize=2048,iocharset=utf8", Ro = true });
                return null;
            }
            var already = Mounts.FirstOrDefault(m => m.FsId == c.FsId && !m.Bind);
            if (already != null && already.Path == gtarget) return "mount: " + target + ": " + d.Path + " already mounted on " + target + ".";
            if (already != null && c.Kind == "xfs") { }
            var fsys = V.Fs(c.FsId);
            if (fsys == null) return "mount: " + target + ": wrong fs type, bad option, bad superblock on " + d.Path + ", missing codepage or helper program, or other error.";
            // a freshly made file system gets its root labelled like the mount point the first time
            var r = fsys.Root;
            if (Selinux.TypeOf(r.Ctx) == "unlabeled_t") r.Ctx = tl.Node.Ctx;
            if (c.Kind == "vfat") ApplyVfatOwners(fsys, optList);
            Mounts.Add(new Mount { Path = gtarget, FsId = c.FsId, RootIno = fsys.RootIno, Source = d.Type == "lvm" ? "/dev/mapper/" + d.Name : d.Path, FsType = c.Kind, Opts = MountOptsText(optList, c.Kind), Ro = ro });
            if (c.Kind == "ext4" && fsys.Dirty) fsys.Dirty = false;
            return null;
        }

        void ApplyVfatOwners(FileSys fs, List<string> opts)
        {
            int uid = 0, gid = 0;
            foreach (var o in opts)
            {
                if (o.StartsWith("uid=", StringComparison.Ordinal)) int.TryParse(o.Substring(4), out uid);
                if (o.StartsWith("gid=", StringComparison.Ordinal)) int.TryParse(o.Substring(4), out gid);
            }
            foreach (var n in fs.Nodes.Values) { n.Uid = uid; n.Gid = gid; n.Mode = n.T == 'd' ? 0x1ED : 0x1ED; n.Ctx = "system_u:object_r:dosfs_t:s0"; }
        }

        static string MountOptsText(List<string> opts, string kind)
        {
            var l = new List<string> { opts.Contains("ro") ? "ro" : "rw" };
            foreach (var o in new[] { "nosuid", "nodev", "noexec", "sync" }) if (opts.Contains(o)) l.Add(o);
            l.Add(opts.Contains("noatime") ? "noatime" : "relatime");
            if (kind == "xfs") { l.Add("seclabel"); l.Add("attr2"); l.Add("inode64"); l.Add("logbufs=8"); l.Add("logbsize=32k"); l.Add("noquota"); }
            else if (kind == "ext4") { l.Add("seclabel"); }
            else if (kind == "vfat") { l.Add("fmask=0022"); l.Add("dmask=0022"); l.Add("codepage=437"); l.Add("iocharset=ascii"); l.Add("shortname=mixed"); l.Add("errors=remount-ro"); }
            else if (kind == "tmpfs") { l.Add("seclabel"); }
            return string.Join(",", l.ToArray());
        }

        public string Umount(string spec, PCtx who, bool lazy)
        {
            Mount m = null;
            string g = spec;
            try { var l = V.Resolve(who, spec, true); if (l.Exists) g = l.GPath; } catch (VfsEx) { }
            m = Mounts.LastOrDefault(x => x.Path == g && x.Path != "/");
            if (m == null)
            {
                var d = FindDev(spec);
                if (d != null) { var c = ContentOf(d); if (c != null) m = Mounts.LastOrDefault(x => x.FsId == c.FsId && x.Path != "/"); }
                if (m == null) m = Mounts.LastOrDefault(x => x.Source == spec && x.Path != "/");
            }
            if (m == null)
            {
                if (spec == "/") return "umount: /: target is busy.";
                bool exists = false; try { exists = V.Resolve(who, spec, true).Exists; } catch (VfsEx) { }
                return exists ? "umount: " + spec + ": not mounted." : "umount: " + spec + ": no mount point specified.";
            }
            // busy: a shell standing in it, or another mount below it
            if (!lazy)
            {
                foreach (var s in Sessions) foreach (var sh in s.ShStack) if (sh.M == this && (sh.P.Cwd == m.Path || sh.P.Cwd.StartsWith(m.Path + "/", StringComparison.Ordinal))) return "umount: " + m.Path + ": target is busy.";
                foreach (var other in Net.Machines) if (other != this) foreach (var s in other.Sessions) foreach (var sh in s.ShStack) if (sh.M == this && (sh.P.Cwd == m.Path || sh.P.Cwd.StartsWith(m.Path + "/", StringComparison.Ordinal))) return "umount: " + m.Path + ": target is busy.";
                if (Mounts.Any(x => x != m && x.Path.StartsWith(m.Path + "/", StringComparison.Ordinal))) return "umount: " + m.Path + ": target is busy.";
                if (Containers.Any(c => c.Running && c.Volumes.Any(v => v.Host == m.Path || v.Host.StartsWith(m.Path + "/", StringComparison.Ordinal)))) return "umount: " + m.Path + ": target is busy.";
            }
            Mounts.Remove(m);
            if (m.FsType == "tmpfs") RunFs.Remove(m.FsId);
            return null;
        }

        // ------------------------------------------------------------ swap
        public string SwapOn(string spec, int prio)
        {
            var d = FindDev(spec);
            if (d == null)
            {
                // a swap file
                try
                {
                    var l = V.G(spec);
                    if (l.Exists && l.Node.T == 'f')
                    {
                        if (l.Node.Text != "\u0000SWAPSPACE2") return "swapon: " + spec + ": read swap header failed";
                        if ((l.Node.Mode & 0x3F) != 0) { }
                        if (Swaps.Any(s => s.Dev == l.GPath)) return "swapon: " + spec + ": swapon failed: Device or resource busy";
                        Swaps.Add(new SwapArea { Dev = l.GPath, Bytes = l.Node.BinSize > 0 ? l.Node.BinSize : 1024 * 1024, Prio = prio });
                        return null;
                    }
                }
                catch (VfsEx) { }
                return "swapon: cannot open " + spec + ": No such file or directory";
            }
            var c = ContentOf(d);
            if (c == null || c.Kind != "swap") return "swapon: " + d.Path + ": read swap header failed";
            if (Swaps.Any(s => s.Dev == d.Path || FindDev(s.Dev) != null && FindDev(s.Dev).Key == d.Key)) return "swapon: " + d.Path + ": swapon failed: Device or resource busy";
            Swaps.Add(new SwapArea { Dev = d.Type == "lvm" ? "/dev/dm-" + d.Minor : d.Path, Bytes = d.Bytes, Prio = prio < -1 ? -2 - Swaps.Count : prio });
            return null;
        }

        // ------------------------------------------------------------ /etc/fstab
        public class FstabEntry { public string Spec, File, Type, Opts; public int Dump, Pass; public int Line; public string Raw; }

        public List<FstabEntry> Fstab()
        {
            var l = new List<FstabEntry>();
            string t = V.Cat("/etc/fstab") ?? "";
            int n = 0;
            foreach (var raw in t.Split('\n'))
            {
                n++;
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                var p = Regex.Split(line, "\\s+");
                var e = new FstabEntry { Line = n, Raw = raw, Spec = p[0], File = p.Length > 1 ? p[1] : "", Type = p.Length > 2 ? p[2] : "auto", Opts = p.Length > 3 ? p[3] : "defaults" };
                int x;
                if (p.Length > 4 && int.TryParse(p[4], out x)) e.Dump = x;
                if (p.Length > 5 && int.TryParse(p[5], out x)) e.Pass = x;
                l.Add(e);
            }
            return l;
        }

        /// <summary>mount -a (and the boot): mounts what fstab lists and is not mounted yet. Returns the errors.</summary>
        public List<string> MountAll(bool boot, List<string> failedCritical) { return MountAll(boot, failedCritical, 0); }
        /// <summary>pass 0: everything (mount -a); 1: local file systems (early boot); 2: network file systems (after the network is up).</summary>
        public List<string> MountAll(bool boot, List<string> failedCritical, int pass)
        {
            var errors = new List<string>();
            var root = new PCtx();
            foreach (var e in Fstab())
            {
                var opts = e.Opts.Split(',');
                if (e.Type == "swap" || e.File == "swap" || e.File == "none" && e.Type == "swap")
                {
                    if (!boot) continue;
                    string err = SwapOn(e.Spec, -2);
                    if (err != null && !err.Contains("busy")) { errors.Add(err); Log("systemd", 1, 3, "Timed out waiting for device " + e.Spec + ".", null); }
                    continue;
                }
                if (opts.Contains("noauto")) continue;
                if (e.File == "/" ) continue;
                string target = e.File;
                if (!target.StartsWith("/", StringComparison.Ordinal)) { errors.Add("mount: " + target + ": mount point is not a directory."); continue; }
                // already mounted there?
                string gt = Vfs.Norm(target);
                var d = FindDev(e.Spec);
                var c = ContentOf(d);
                if (Mounts.Any(m => m.Path == gt && (c == null || m.FsId == c.FsId || m.Source == e.Spec))) continue;
                if (Mounts.Any(m => m.Path == gt) && !boot) continue;
                // systemd makes missing mount points at boot; mount -a does not
                if (boot) { try { if (!V.G(gt).Exists) V.EnsureDir(gt, 0x1ED, null); } catch (VfsEx) { } }
                bool netdev = opts.Contains("_netdev") || e.Type.StartsWith("nfs", StringComparison.Ordinal) || e.Type == "cifs";
                if ((pass == 1 && netdev) || (pass == 2 && !netdev)) continue;
                string err2 = MountFs(e.Spec, target, e.Type, e.Opts, root);
                if (err2 != null)
                {
                    errors.Add(err2);
                    bool nofail = opts.Contains("nofail");
                    if (boot)
                    {
                        string unit = UnitNameForPath(gt) + ".mount";
                        if (d == null && !netdev) Log("systemd", 1, 3, "Timed out waiting for device " + DevUnitName(e.Spec) + ".", null);
                        Log("systemd", 1, 3, "Failed to mount " + target + ".", unit);
                        Log("mount", 0, 3, err2, unit);
                        if (!nofail && !netdev && failedCritical != null) failedCritical.Add(target);
                    }
                }
            }
            return errors;
        }

        public static string UnitNameForPath(string p)
        {
            if (p == "/") return "-";
            var parts = p.Trim('/').Split('/');
            return string.Join("-", parts.Select(x => x.Replace("-", "\\x2d")).ToArray());
        }
        public static string DevUnitName(string spec)
        {
            if (spec.StartsWith("UUID=", StringComparison.Ordinal)) return "dev-disk-by\\x2duuid-" + spec.Substring(5).Replace("-", "\\x2d") + ".device";
            if (spec.StartsWith("LABEL=", StringComparison.Ordinal)) return "dev-disk-by\\x2dlabel-" + spec.Substring(6) + ".device";
            return UnitNameForPath(spec) + ".device";
        }

        // ------------------------------------------------------------ autofs
        public bool AutoMount(string gpath)
        {
            if (!ServiceActive("autofs.service")) return false;
            string parent = Vfs.Parent(gpath), key = Vfs.Base(gpath);
            var am = Mounts.FirstOrDefault(m => m.FsType == "autofs" && m.Path == parent && m.AutoMap != null);
            if (am == null) return false;
            string line = AutoMapLookup(am.AutoMap, key);
            if (line == null) return false;
            // "-rw,sync  server:/path/&"
            var p = Regex.Split(line.Trim(), "\\s+");
            string opts = "", loc;
            if (p.Length >= 2 && p[0].StartsWith("-", StringComparison.Ordinal)) { opts = p[0].Substring(1); loc = p[1]; } else loc = p[0];
            if (loc.StartsWith(":", StringComparison.Ordinal)) loc = loc.Substring(1);
            loc = loc.Replace("&", key);
            string fstype = "nfs";
            foreach (var o in opts.Split(',')) if (o.StartsWith("fstype=", StringComparison.Ordinal)) fstype = o.Substring(7);
            opts = string.Join(",", opts.Split(',').Where(o => o.Length > 0 && !o.StartsWith("fstype=", StringComparison.Ordinal)).ToArray());
            var fs = V.Fs(am.FsId);
            if (fs == null) return false;
            var dir = fs.Root;
            if (!dir.Kids.ContainsKey(key)) { var n = fs.NewNode('d', 0x16D, 0, 0, Now); n.Ctx = "system_u:object_r:autofs_t:s0"; dir.Kids[key] = n.Ino; }
            string err = MountFs(loc, gpath, fstype, opts.Length == 0 ? "defaults" : opts, null);
            if (err != null) { dir.Kids.Remove(key); Log("automount", ServicePid("autofs.service"), 3, "mount(nfs): nfs: mount failure " + loc + " on " + gpath, "autofs.service"); return false; }
            return true;
        }

        string AutoMapLookup(string mapFile, string key)
        {
            string t = V.Cat(mapFile);
            if (t == null) return null;
            string wild = null;
            foreach (var raw in t.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                var p = Regex.Split(line, "\\s+");
                if (p[0] == key) return line.Substring(p[0].Length);
                if (p[0] == "*") wild = line.Substring(1);
            }
            return wild;
        }

        /// <summary>autofs started: reads auto.master (+ auto.master.d) and puts its mount points in place.</summary>
        public List<string> StartAutofs()
        {
            var problems = new List<string>();
            Mounts.RemoveAll(m => m.FsType == "autofs");
            var masters = new List<string>();
            string mt = V.Cat("/etc/auto.master");
            if (mt != null) masters.Add(mt);
            try { var d = V.G("/etc/auto.master.d"); if (d.Exists && d.IsDir) foreach (var k in Vfs.Sorted(d.Node.Kids.Keys)) if (k.EndsWith(".autofs", StringComparison.Ordinal)) masters.Add(V.Cat("/etc/auto.master.d/" + k) ?? ""); } catch (VfsEx) { }
            foreach (var m in masters)
                foreach (var raw in m.Split('\n'))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith("+", StringComparison.Ordinal)) continue;
                    var p = Regex.Split(line, "\\s+");
                    if (p.Length < 2) continue;
                    string mp = p[0], map = p[1];
                    if (map == "-hosts") continue;
                    if (!map.StartsWith("/", StringComparison.Ordinal)) map = "/etc/" + map;
                    if (mp == "/-")
                    {
                        // direct map: each key is a full path
                        string dt = V.Cat(map);
                        if (dt == null) { problems.Add("lookup(file): file map " + map + " not found"); continue; }
                        foreach (var dl in dt.Split('\n'))
                        {
                            var dline = dl.Trim();
                            if (dline.Length == 0 || dline.StartsWith("#", StringComparison.Ordinal)) continue;
                            var dp = Regex.Split(dline, "\\s+");
                            string path = dp[0];
                            string opts = dp.Length >= 3 && dp[1].StartsWith("-", StringComparison.Ordinal) ? dp[1].Substring(1) : "";
                            string loc = dp.Length >= 3 ? dp[2] : (dp.Length >= 2 ? dp[1] : "");
                            try { V.EnsureDir(path, 0x1ED, null); } catch (VfsEx) { continue; }
                            string fstype = "nfs"; foreach (var o in opts.Split(',')) if (o.StartsWith("fstype=", StringComparison.Ordinal)) fstype = o.Substring(7);
                            string err = MountFs(loc.TrimStart(':'), path, fstype, string.Join(",", opts.Split(',').Where(o => o.Length > 0 && !o.StartsWith("fstype=", StringComparison.Ordinal)).ToArray()), null);
                            if (err != null) problems.Add(err);
                        }
                        continue;
                    }
                    if (V.Cat(map) == null) { problems.Add("lookup(file): file map " + map + " not found"); continue; }
                    try { V.EnsureDir(mp, 0x1ED, null); } catch (VfsEx) { continue; }
                    var fs = new FileSys { Id = "auto" + NewHex(8), Type = "autofs", Bytes = 0 };
                    fs.Nodes[1] = new Inode { Ino = 1, T = 'd', Mode = 0x1ED, Kids = new Dictionary<string, int>(StringComparer.Ordinal), Nlink = 2, Ctx = "system_u:object_r:autofs_t:s0", Mtime = Now, Ctime = Now, Atime = Now };
                    RunFs[fs.Id] = fs;
                    Mounts.Add(new Mount { Path = Vfs.Norm(mp), FsId = fs.Id, RootIno = 1, Source = map, FsType = "autofs", Opts = "rw,relatime,fd=5,pgrp=" + ServicePid("autofs.service") + ",timeout=300,minproto=5,maxproto=5,indirect", AutoMap = map });
                }
            return problems;
        }
        public void StopAutofs()
        {
            var autos = Mounts.Where(m => m.FsType == "autofs").ToList();
            foreach (var a in autos)
            {
                Mounts.RemoveAll(m => m.Path.StartsWith(a.Path + "/", StringComparison.Ordinal));
                Mounts.Remove(a);
                RunFs.Remove(a.FsId);
            }
        }

        // ------------------------------------------------------------ LVM
        public long PvExtents(string key, VolGroup vg)
        {
            var d = DevOfKey(key);
            if (d == null) return 0;
            return Math.Max(0, (d.Bytes - 1024 * 1024) / vg.PeSize);
        }
        public long VgExtents(VolGroup vg) { return vg.Pvs.Sum(p => PvExtents(p, vg)); }
        public long VgFree(VolGroup vg) { return VgExtents(vg) - vg.Lvs.Sum(l => l.Extents); }
        public long PvUsed(string key, VolGroup vg) { return vg.Lvs.Sum(l => l.Segs.Where(s => s.Pv == key).Sum(s => s.Count)); }
        public VolGroup VgOfPv(string key) { return Vgs.FirstOrDefault(v => v.Pvs.Contains(key)); }

        /// <summary>Allocates extents for an LV (normal policy: free ranges in PV order).</summary>
        public bool Allocate(VolGroup vg, Lv lv, long count, List<string> onlyPvs)
        {
            long need = count;
            var segs = new List<LvSeg>();
            foreach (var pv in vg.Pvs)
            {
                if (onlyPvs != null && onlyPvs.Count > 0 && !onlyPvs.Contains(pv)) continue;
                long total = PvExtents(pv, vg);
                var used = vg.Lvs.SelectMany(l => l.Segs).Concat(segs).Where(s => s.Pv == pv).OrderBy(s => s.Start).ToList();
                long pos = 0;
                foreach (var u in used)
                {
                    if (u.Start > pos && need > 0) { long take = Math.Min(need, u.Start - pos); segs.Add(new LvSeg { Pv = pv, Start = pos, Count = take }); need -= take; }
                    pos = Math.Max(pos, u.Start + u.Count);
                }
                if (need > 0 && pos < total) { long take = Math.Min(need, total - pos); segs.Add(new LvSeg { Pv = pv, Start = pos, Count = take }); need -= take; }
                if (need == 0) break;
            }
            if (need > 0) return false;
            // merge with the last segment when it continues it
            foreach (var s in segs)
            {
                var last = lv.Segs.LastOrDefault();
                if (last != null && last.Pv == s.Pv && last.Start + last.Count == s.Start) last.Count += s.Count;
                else lv.Segs.Add(s);
            }
            return true;
        }
        public void Shrink(Lv lv, long newExtents)
        {
            long cut = lv.Extents - newExtents;
            while (cut > 0 && lv.Segs.Count > 0)
            {
                var last = lv.Segs[lv.Segs.Count - 1];
                long take = Math.Min(cut, last.Count);
                last.Count -= take; cut -= take;
                if (last.Count == 0) lv.Segs.RemoveAt(lv.Segs.Count - 1);
            }
        }

        public string PvUuid()
        {
            var r = new Random(Seed * 17 + (Counter++) * 3);
            const string a = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
            var sb = new StringBuilder();
            int[] groups = { 6, 4, 4, 4, 4, 4, 6 };
            for (int g = 0; g < groups.Length; g++) { if (g > 0) sb.Append('-'); for (int i = 0; i < groups[g]; i++) sb.Append(a[r.Next(a.Length)]); }
            return sb.ToString();
        }

        /// <summary>Grows a file system to fill its device (xfs_growfs / resize2fs / lvextend -r).</summary>
        public void GrowFs(BDev d)
        {
            var c = ContentOf(d);
            if (c == null || c.FsId == null) return;
            var fs = V.Fs(c.FsId);
            if (fs == null) return;
            var fresh = new FileSys { Type = fs.Type };
            long mib = 1024 * 1024;
            if (fs.Type == "xfs") fs.Bytes = Math.Max(fs.Bytes, d.Bytes - Math.Min(d.Bytes / 50, 64 * mib) - 2 * mib);
            else if (fs.Type == "ext4") fs.Bytes = (long)(d.Bytes * 0.952);
            else fs.Bytes = d.Bytes;
        }
    }
}
