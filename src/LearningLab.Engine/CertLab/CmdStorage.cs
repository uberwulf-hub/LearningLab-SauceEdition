// Cert Lab - storage programs: mount, umount, findmnt, lsblk, blkid, mkfs (xfs, ext4, vfat), mkswap, swapon,
// swapoff, wipefs, xfs_growfs, xfs_info, xfs_admin, resize2fs, e2label, tune2fs, fsck, showmount, partprobe.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CertLab
{
    public static partial class Cmd
    {
        static void RegStorage()
        {
            P("mount", MountCmd); P("umount", UmountCmd); P("findmnt", Findmnt); P("lsblk", Lsblk); P("blkid", Blkid); P("findfs", (sh, a, io) => { var d = a.Count > 1 ? sh.M.FindDev(a[1]) : null; if (d == null) { io.E("findfs: unable to resolve '" + (a.Count > 1 ? a[1] : "") + "'"); return 1; } io.L(d.Path); return 0; });
            P("mkfs.xfs", (sh, a, io) => Mkfs(sh, a, io, "xfs")); P("mkfs.ext4", (sh, a, io) => Mkfs(sh, a, io, "ext4")); P("mkfs.ext3", (sh, a, io) => Mkfs(sh, a, io, "ext4")); P("mkfs.ext2", (sh, a, io) => Mkfs(sh, a, io, "ext4"));
            P("mke2fs", (sh, a, io) => Mkfs(sh, a, io, "ext4")); P("mkfs.vfat", (sh, a, io) => Mkfs(sh, a, io, "vfat")); P("mkfs.fat", (sh, a, io) => Mkfs(sh, a, io, "vfat")); P("mkdosfs", (sh, a, io) => Mkfs(sh, a, io, "vfat"));
            P("mkfs", (sh, a, io) => { var o = Opts.Parse(a, "t:V", "type=|t", "mkfs", true); string t = o.Get("t", "ext2"); var b = new List<string> { "mkfs." + t }; b.AddRange(a.Skip(1).Where(x => x != "-t" && x != t && !x.StartsWith("--type", StringComparison.Ordinal))); if (t == "ext2" || t == "ext3" || t == "ext4") return Mkfs(sh, b, io, "ext4"); if (t == "xfs") return Mkfs(sh, b, io, "xfs"); if (t == "vfat" || t == "fat" || t == "msdos") return Mkfs(sh, b, io, "vfat"); io.E("mkfs: failed to execute mkfs." + t + ": No such file or directory"); return 1; });
            RegLvm();
            P("fdisk", Fdisk); P("parted", Parted); P("gdisk", Gdisk); P("sgdisk", (sh, a, io) => { io.E("Cert Lab: use gdisk, fdisk or parted"); return 1; });
            P("mkswap", Mkswap); P("swapon", Swapon); P("swapoff", Swapoff); P("wipefs", Wipefs);
            P("xfs_growfs", XfsGrowfs); P("xfs_info", XfsInfo); P("xfs_admin", XfsAdmin); P("xfs_repair", (sh, a, io) => { io.L("Phase 1 - find and verify superblock...\nPhase 2 - using internal log\n        - zero log...\nPhase 3 - for each AG...\nPhase 7 - verify and correct link counts...\ndone"); return 0; }); P("fsck.xfs", (sh, a, io) => 0); P("xfs_db", (sh, a, io) => 0);
            P("resize2fs", Resize2fs); P("e2label", E2label); P("tune2fs", Tune2fs); P("e2fsck", Fsck); P("fsck", Fsck); P("fsck.ext4", Fsck); P("fsck.vfat", Fsck); P("dumpe2fs", (sh, a, io) => { io.L("dumpe2fs 1.46.5 (30-Dec-2021)\nFilesystem volume name:   <none>\nFilesystem state:         clean"); return 0; });
            P("fatlabel", (sh, a, io) => { var d = a.Count > 1 ? sh.M.FindDev(a[1]) : null; var c = sh.M.ContentOf(d); if (c == null) { io.E("fatlabel: unable to open " + (a.Count > 1 ? a[1] : "")); return 1; } if (a.Count > 2) c.Label = a[2].ToUpperInvariant(); else io.L(c.Label); return 0; });
            P("partprobe", (sh, a, io) => { sh.M.SyncDev(); return 0; }); P("partx", (sh, a, io) => { sh.M.SyncDev(); return 0; }); P("udevadm", (sh, a, io) => { sh.M.SyncDev(); return 0; });
            P("blockdev", (sh, a, io) => { if (a.Contains("--getsize64") && a.Count > 2) { var d = sh.M.FindDev(a[a.Count - 1]); if (d == null) { io.E("blockdev: cannot open " + a[a.Count - 1] + ": No such file or directory"); return 1; } io.L(d.Bytes.ToString()); } if (a.Contains("--rereadpt")) sh.M.SyncDev(); return 0; });
            P("showmount", Showmount); P("exportfs", (sh, a, io) => { if (a.Contains("-v")) foreach (var l in Lines(sh.M.V.Cat("/etc/exports") ?? "")) if (l.Trim().Length > 0 && !l.StartsWith("#", StringComparison.Ordinal)) io.L(l); return 0; });
            P("mount.nfs", (sh, a, io) => { var b = new List<string> { "mount", "-t", "nfs" }; b.AddRange(a.Skip(1)); return MountCmd(sh, b, io); }); P("mount.nfs4", (sh, a, io) => { var b = new List<string> { "mount", "-t", "nfs4" }; b.AddRange(a.Skip(1)); return MountCmd(sh, b, io); });
            P("nfsstat", (sh, a, io) => { io.L("Client rpc stats:\ncalls      retrans    authrefrsh\n" + (sh.M.Mounts.Count(x => x.FsType.StartsWith("nfs", StringComparison.Ordinal)) * 120) + "        0          0"); return 0; });
            P("automount", (sh, a, io) => { io.L("automount: the autofs service runs this; use systemctl start autofs"); return 0; });
            P("losetup", (sh, a, io) => { if (a.Count == 1 || a.Contains("-a")) return 0; io.E("losetup: Cert Lab does not simulate loop devices"); return 1; });
            P("fstrim", (sh, a, io) => 0); P("fsfreeze", (sh, a, io) => 0); P("sync", (sh, a, io) => 0);
        }

        static bool NeedRootStorage(Sh sh, Io io, string prog, string what)
        {
            if (sh.P.Cr.Uid == 0) return false;
            io.E(prog + ": " + what + ": Permission denied");
            return true;
        }

        // ------------------------------------------------------------ mount
        static int MountCmd(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "t:o:avfrwnlLUBRM", "types=|t,options=|o,all|a,verbose|v,fake|f,read-only|r,rw|w,read-write|w,no-mtab|n,show-labels|l,bind|B,rbind|R,move|M,source=,target=,label=|L,uuid=|U", "mount");
            if (o.Error != null) return Fail(io, o);
            var m = sh.M;
            var args = o.Args;
            if (!o.Has("a") && args.Count == 0 && !o.Has("L") && !o.Has("U"))
            {
                foreach (var mt in m.Mounts)
                {
                    if (o.Has("t") && mt.FsType != o.Get("t")) continue;
                    io.L(mt.Source + " on " + mt.Path + " type " + mt.FsType + " (" + mt.Opts + ")");
                }
                return 0;
            }
            if (sh.P.Cr.Uid != 0) { io.E("mount: " + (args.Count > 0 ? args[args.Count - 1] : "/") + ": must be superuser to use mount."); return 32; }
            string opts = string.Join(",", o.All("o").ToArray());
            if (o.Has("r")) opts = (opts.Length > 0 ? opts + "," : "") + "ro";
            if (o.Has("B")) opts = (opts.Length > 0 ? opts + "," : "") + "bind";
            if (o.Has("a"))
            {
                FstabHint(sh, io);
                var errs = m.MountAll(false, null);
                foreach (var e in errs) io.E(e);
                if (o.Has("v")) foreach (var e in m.Fstab()) io.L(e.File.PadRight(24) + ": " + (errs.Any(x => x.Contains(e.File)) ? "failed" : "already mounted"));
                return errs.Count > 0 ? 32 : 0;
            }
            if (opts.Split(',').Contains("remount"))
            {
                string target = args.Count > 0 ? args[args.Count - 1] : "/";
                Loc l;
                try { l = Find(sh, target, true); } catch (VfsEx) { io.E("mount: " + target + ": mount point does not exist."); return 32; }
                var mt = m.Mounts.LastOrDefault(x => x.Path == l.GPath);
                if (mt == null) { io.E("mount: " + target + ": mount point not mounted or bad option."); return 32; }
                var ol = opts.Split(',');
                if (ol.Contains("rw")) { mt.Ro = false; mt.Opts = Regex.Replace(mt.Opts, "^ro", "rw"); }
                if (ol.Contains("ro")) { mt.Ro = true; mt.Opts = Regex.Replace(mt.Opts, "^rw", "ro"); }
                return 0;
            }
            string src, dst, type = o.Get("t");
            if (o.Has("L")) { src = "LABEL=" + o.Get("L"); dst = args.Count > 0 ? args[0] : null; }
            else if (o.Has("U")) { src = "UUID=" + o.Get("U"); dst = args.Count > 0 ? args[0] : null; }
            else if (args.Count == 1)
            {
                // one argument: the line in /etc/fstab
                FstabHint(sh, io);
                string x = args[0], gx = Abs(sh, x);
                var e = m.Fstab().FirstOrDefault(f => Vfs.Norm(f.File) == gx || f.Spec == x || (m.FindDev(f.Spec) != null && m.FindDev(f.Spec) == m.FindDev(x) && m.FindDev(x) != null));
                if (e == null) { io.E("mount: " + x + ": can't find in /etc/fstab."); return 1; }
                if (e.Type == "swap") { io.E("mount: " + x + ": can't find in /etc/fstab."); return 1; }
                src = e.Spec; dst = e.File; if (type == null) type = e.Type; if (opts.Length == 0) opts = e.Opts;
            }
            else { src = args[0]; dst = args[1]; }
            if (dst == null) { io.E("mount: bad usage\nTry 'mount --help' for more information."); return 1; }
            // already mounted there?
            try
            {
                var dl = Find(sh, dst, true);
                var d = m.FindDev(src);
                var c = m.ContentOf(d);
                if (dl.Exists && c != null && m.Mounts.Any(x => x.Path == dl.GPath && x.FsId == c.FsId)) { io.E("mount: " + dst + ": " + d.Path + " already mounted on " + dl.GPath + "."); return 32; }
            }
            catch (VfsEx) { }
            m.LastMountWarning = null;
            string err = m.MountFs(src, dst, type, opts, sh.P);
            if (m.LastMountWarning != null) { io.E(m.LastMountWarning); m.LastMountWarning = null; }
            if (err != null)
            {
                io.E(err);
                if (err.Contains("wrong fs type")) io.E("       dmesg(1) may have more information after failed mount system call.");
                return 32;
            }
            if (o.Has("v")) io.L("mount: " + src + " mounted on " + Abs(sh, dst) + ".");
            return 0;
        }

        static void FstabHint(Sh sh, Io io)
        {
            var m = sh.M;
            string f = m.V.Cat("/etc/fstab") ?? "";
            if (m.FstabSeen != null && m.FstabSeen != f) io.E("mount: (hint) your fstab has been modified, but systemd still uses\n       the old version; use 'systemctl daemon-reload' to reload.");
        }

        static int UmountCmd(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "lfvaRrt:", "lazy|l,force|f,verbose|v,all|a,recursive|R,read-only|r", "umount");
            if (o.Error != null) return Fail(io, o);
            if (o.Args.Count == 0) { io.E("umount: bad usage\nTry 'umount --help' for more information."); return 1; }
            if (sh.P.Cr.Uid != 0) { io.E("umount: " + o.Args[0] + ": must be superuser to unmount."); return 32; }
            int st = 0;
            foreach (var x in o.Args)
            {
                string err = sh.M.Umount(x, sh.P, o.Has("l"));
                if (err != null) { io.E(err); st = 32; }
                else if (o.Has("v")) io.L("umount: " + x + " unmounted");
            }
            return st;
        }

        static int Findmnt(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "lnso:t:SrDfAmkv", "list|l,noheadings|n,fstab|s,output=|o,types=|t,source=|S,raw|r,df|D,first-only|f,all|A,mtab|m,kernel|k,verify,verbose|v,target=|T,mountpoint=|M,real,pseudo", "findmnt");
            if (o.Error != null) return Fail(io, o);
            var m = sh.M;
            if (o.Has("verify")) return FindmntVerify(sh, io, o.Has("v"));
            var rows = new List<string[]>();   // target, source, fstype, options
            if (o.Has("s"))
            {
                foreach (var e in m.Fstab()) rows.Add(new[] { e.File, e.Spec, e.Type, e.Opts });
            }
            else
            {
                foreach (var mt in m.Mounts) rows.Add(new[] { mt.Path, mt.Bind ? mt.Source.Length > 0 ? m.V.MountOf(mt.Source) != null && m.V.MountOf(mt.Source).Path != "/" ? m.V.MountOf(mt.Source).Source + "[" + mt.Source.Substring(m.V.MountOf(mt.Source).Path.Length) + "]" : RootSrc(m) + "[" + mt.Source + "]" : mt.Source : mt.Source, mt.FsType, mt.Opts });
            }
            if (o.Has("t")) { var ts = o.Get("t").Split(','); rows = rows.Where(r => ts.Contains(r[2]) || ts.Contains("nfs") && r[2].StartsWith("nfs", StringComparison.Ordinal)).ToList(); }
            if (o.Has("S")) { var d = m.FindDev(o.Get("S")); rows = rows.Where(r => r[1] == o.Get("S") || (d != null && m.FindDev(r[1]) == d)).ToList(); }
            var targets = o.Args.ToList();
            if (o.Has("T")) targets.Add(o.Get("T"));
            if (o.Has("M")) targets.Add(o.Get("M"));
            if (targets.Count > 0)
            {
                string t = targets[0];
                var d = m.FindDev(t);
                string g = Abs(sh, t);
                var hit = rows.Where(r => r[0] == g || r[1] == t || (d != null && m.FindDev(r[1]) != null && m.FindDev(r[1]).Key == d.Key) || (targets.Count > 1 && r[1] == targets[0] && r[0] == Abs(sh, targets[1]))).ToList();
                if (hit.Count == 0) return 1;
                rows = hit;
            }
            var cols = (o.Get("o") ?? "TARGET,SOURCE,FSTYPE,OPTIONS").Split(',').Select(x => x.Trim().ToUpperInvariant()).ToList();
            Func<string[], string, string> col = (r, c) =>
            {
                switch (c)
                {
                    case "TARGET": return r[0];
                    case "SOURCE": return r[1];
                    case "FSTYPE": return r[2];
                    case "OPTIONS": return r[3];
                    case "UUID": { var d = m.FindDev(r[1]); var c2 = m.ContentOf(d); return c2 == null ? "" : c2.Uuid; }
                    case "LABEL": { var d = m.FindDev(r[1]); var c2 = m.ContentOf(d); return c2 == null ? "" : c2.Label; }
                    case "SIZE": case "USED": case "AVAIL": case "USE%":
                        {
                            var mt = m.Mounts.FirstOrDefault(x => x.Path == r[0]); var fs = mt == null ? null : m.V.Fs(mt.FsId);
                            if (fs == null) return "";
                            if (c == "SIZE") return LsblkSize(fs.Bytes); if (c == "USED") return LsblkSize(fs.Used); if (c == "AVAIL") return LsblkSize(fs.Bytes - fs.Used);
                            return (fs.Bytes == 0 ? 0 : (int)(fs.Used * 100 / fs.Bytes)) + "%";
                        }
                }
                return "";
            };
            var table = new List<string[]> { cols.ToArray() };
            foreach (var r in rows) table.Add(cols.Select(c => col(r, c)).ToArray());
            if (o.Has("n")) table.RemoveAt(0);
            var w = new int[cols.Count];
            foreach (var r in table) for (int i = 0; i < cols.Count; i++) w[i] = Math.Max(w[i], r[i].Length);
            bool tree = !o.Has("l") && !o.Has("s") && targets.Count == 0 && !o.Has("t") && !o.Has("r") && cols[0] == "TARGET";
            for (int k = 0; k < table.Count; k++)
            {
                var r = table[k];
                string first = r[0];
                if (tree && (k > 0 || o.Has("n")) && first != "/")
                {
                    int depth = Math.Max(1, m.Mounts.Count(mm => mm.Path != "/" && first.StartsWith(mm.Path + "/", StringComparison.Ordinal)) + 1);
                    first = new string(' ', (depth - 1) * 2) + Tree1 + first;
                }
                var sb = new StringBuilder();
                for (int i = 0; i < r.Length; i++) { string v = i == 0 ? first : r[i]; sb.Append(i == r.Length - 1 ? v : v.PadRight(Math.Max(w[i], i == 0 && tree ? w[0] + 6 : w[i]) + 1)); }
                io.L(sb.ToString().TrimEnd());
            }
            return 0;
        }
        static string RootSrc(Machine m) { return Machine.RootDev; }

        static int FindmntVerify(Sh sh, Io io, bool verbose)
        {
            var m = sh.M;
            int errors = 0, warnings = 0;
            foreach (var e in m.Fstab())
            {
                var msgs = new List<string>();
                if (e.Type == "swap") { var d0 = m.FindDev(e.Spec); if (d0 == null && !e.Spec.StartsWith("/", StringComparison.Ordinal)) { msgs.Add("   [E] unreachable source: " + e.Spec); errors++; } else if (verbose) msgs.Add("   [ ] " + e.Spec); }
                else
                {
                    if (!e.File.StartsWith("/", StringComparison.Ordinal)) { msgs.Add("   [E] non-absolute target path: " + e.File); errors++; }
                    else if (!m.V.Exists(e.File)) { msgs.Add("   [W] non-existing target: " + e.File); warnings++; }
                    bool net = e.Type.StartsWith("nfs", StringComparison.Ordinal) || e.Spec.Contains(":");
                    var d = m.FindDev(e.Spec);
                    if (!net && d == null) { msgs.Add("   [E] unreachable source: " + e.Spec + ": No such file or directory"); errors++; }
                    else if (d != null)
                    {
                        var c = m.ContentOf(d);
                        if (c == null || c.FsId == null) { msgs.Add("   [W] cannot detect on-disk filesystem type"); warnings++; }
                        else if (e.Type != "auto" && e.Type != c.Kind && !(e.Type == "ext3" && c.Kind == "ext4")) { msgs.Add("   [E] " + e.Type + " does not match with on-disk " + c.Kind); errors++; }
                    }
                    string bad = Machine.BadOption(e.Opts);
                    if (bad != null) { msgs.Add("   [W] unrecognized option '" + bad + "'"); warnings++; }
                    if (!Machine.KnownFsTypes.Contains(e.Type)) { msgs.Add("   [W] " + e.Type + " seems unsupported by the current kernel"); warnings++; }
                }
                if (msgs.Count > 0 || verbose) { io.L(e.File); foreach (var x in msgs) io.L(x); }
            }
            io.L("\n0 parse errors, " + errors + " error" + (errors == 1 ? "" : "s") + ", " + warnings + " warning" + (warnings == 1 ? "" : "s"));
            return errors > 0 ? 1 : 0;
        }

        // ------------------------------------------------------------ lsblk / blkid
        public static string LsblkSize(long bytes)
        {
            if (bytes < 1024) return bytes + "B";
            string[] u = { "K", "M", "G", "T" };
            double v = bytes; int i = -1;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            string s = (Math.Round(v * 10) / 10).ToString("0.#", CultureInfo.InvariantCulture);
            return s + u[i];
        }

        class BRow { public BDev D; public string Prefix; public int Depth; }

        static int Lsblk(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "fpnlo:bdamSrJ", "fs|f,paths|p,noheadings|n,list|l,output=|o,bytes|b,nodeps|d,all|a,perms|m,scsi|S,raw|r,json|J", "lsblk");
            if (o.Error != null) return Fail(io, o);
            var m = sh.M;
            var devs = m.Devices();
            List<string> cols;
            if (o.Has("o")) cols = o.Get("o").ToUpperInvariant().Split(',').Select(x => x.Trim()).ToList();
            else if (o.Has("f")) cols = new List<string> { "NAME", "FSTYPE", "FSVER", "LABEL", "UUID", "FSAVAIL", "FSUSE%", "MOUNTPOINTS" };
            else if (o.Has("m")) cols = new List<string> { "NAME", "SIZE", "OWNER", "GROUP", "MODE" };
            else cols = new List<string> { "NAME", "MAJ:MIN", "RM", "SIZE", "RO", "TYPE", "MOUNTPOINTS" };
            // the tree: disks, their partitions, the LVs on PVs
            var rows = new List<BRow>();
            var only = o.Args.Select(x => m.FindDev(x)).ToList();
            if (o.Args.Count > 0 && only.Any(x => x == null)) { io.E("lsblk: " + o.Args[only.IndexOf(null)] + ": not a block device"); return 32; }
            // a partition or LV named on the command line is shown on its own (with what sits on it)
            foreach (var d in only.Where(x => x.Type != "disk" && x.Type != "rom"))
            {
                rows.Add(new BRow { D = d, Prefix = "", Depth = 0 });
                if (!o.Has("d") && d.Type == "part") AddLvRows(m, devs, d, rows, "", 1);
            }
            foreach (var d in devs.Where(x => x.Type == "disk" || x.Type == "rom"))
            {
                if (only.Count > 0 && !only.Any(x => x.Key == d.Key)) continue;
                rows.Add(new BRow { D = d, Prefix = "", Depth = 0 });
                if (o.Has("d")) continue;
                var parts = devs.Where(x => x.Type == "part" && x.Disk == d.Disk).ToList();
                if (parts.Count == 0) AddLvRows(m, devs, d, rows, "", 1);
                for (int i = 0; i < parts.Count; i++)
                {
                    bool last = i == parts.Count - 1;
                    rows.Add(new BRow { D = parts[i], Prefix = (last ? Tree2 : Tree1), Depth = 1 });
                    AddLvRows(m, devs, parts[i], rows, last ? "  " : "\u2502 ", 2);
                }
            }
            var table = new List<string[]>();
            table.Add(cols.ToArray());
            foreach (var r in rows)
            {
                var d = r.D; var c = d.Type == "rom" ? new BlockContent { Kind = "iso9660", Label = "RHEL-9-5-0-BaseOS-x86_64", Uuid = "2024-11-06-04-30-19-00" } : m.ContentOf(d);
                var mts = d.Type == "rom" ? m.Mounts.Where(x => x.Source == "/dev/sr0").Select(x => x.Path).ToList() : MountsOf(m, d);
                var vals = new List<string>();
                foreach (var col in cols)
                {
                    switch (col)
                    {
                        case "NAME": vals.Add(r.Prefix + (o.Has("p") ? d.Path : d.Name)); break;
                        case "KNAME": vals.Add(d.Type == "lvm" ? "dm-" + d.Minor : d.Name); break;
                        case "PATH": vals.Add(d.Path); break;
                        case "MAJ:MIN": vals.Add((d.Major + ":" + d.Minor).PadLeft(7)); break;
                        case "RM": vals.Add(d.Type == "rom" ? " 1" : " 0"); break;
                        case "SIZE": vals.Add(o.Has("b") ? d.Bytes.ToString() : LsblkSize(d.Bytes).PadLeft(5)); break;
                        case "RO": vals.Add(" 0"); break;
                        case "TYPE": vals.Add(d.Type); break;
                        case "MOUNTPOINT": vals.Add(mts.FirstOrDefault() ?? ""); break;
                        case "MOUNTPOINTS": vals.Add(string.Join(" ", mts.ToArray())); break;
                        case "FSTYPE": vals.Add(c == null ? "" : c.Kind); break;
                        case "FSVER": vals.Add(c == null ? "" : c.Kind == "LVM2_member" ? "LVM2 001" : c.Kind == "swap" ? "1" : c.Kind == "ext4" ? "1.0" : c.Kind == "vfat" ? "FAT32" : c.Kind == "iso9660" ? "Joliet Extension" : ""); break;
                        case "LABEL": vals.Add(c == null ? "" : c.Label); break;
                        case "UUID": vals.Add(c == null ? "" : c.Uuid); break;
                        case "PARTUUID": vals.Add(d.Part == null ? "" : d.Part.PartUuid); break;
                        case "FSAVAIL": case "FSUSE%": case "FSSIZE": case "FSUSED":
                            {
                                var fs = c == null || c.FsId == null || mts.Count == 0 ? null : m.V.Fs(c.FsId);
                                if (fs == null) { vals.Add(""); break; }
                                long avail = fs.Bytes - fs.Used;
                                vals.Add(col == "FSAVAIL" ? LsblkSize(avail).PadLeft(7) : col == "FSSIZE" ? LsblkSize(fs.Bytes) : col == "FSUSED" ? LsblkSize(fs.Used) : ((int)Math.Round(fs.Used * 100.0 / Math.Max(1, fs.Bytes))) + "%");
                                break;
                            }
                        case "OWNER": vals.Add("root"); break;
                        case "GROUP": vals.Add("disk"); break;
                        case "MODE": vals.Add("brw-rw----"); break;
                        case "MODEL": vals.Add(""); break;
                        case "SERIAL": vals.Add(""); break;
                        case "VENDOR": vals.Add("0x1af4"); break;
                        case "PKNAME": vals.Add(d.Parent); break;
                        default: vals.Add(""); break;
                    }
                }
                table.Add(vals.ToArray());
            }
            if (o.Has("n")) table.RemoveAt(0);
            if (o.Has("l") || o.Has("r")) foreach (var r in table) r[0] = r[0].Replace(Tree1, "").Replace(Tree2, "").Replace("\u2502 ", "").TrimStart();
            var w = new int[cols.Count];
            foreach (var r in table) for (int i = 0; i < cols.Count; i++) w[i] = Math.Max(w[i], r[i].Length);
            foreach (var r in table)
            {
                var sb = new StringBuilder();
                for (int i = 0; i < r.Length; i++)
                {
                    if (o.Has("r")) { sb.Append(i > 0 ? " " : "").Append(r[i]); continue; }
                    bool right = cols[i] == "SIZE" || cols[i] == "FSAVAIL" || cols[i] == "FSUSE%";
                    string v = r[i];
                    if (i == r.Length - 1) sb.Append(right ? v.PadLeft(w[i]) : v);
                    else sb.Append(right ? v.PadLeft(w[i]) : v.PadRight(w[i])).Append(' ');
                }
                io.L(sb.ToString().TrimEnd());
            }
            return 0;
        }

        static void AddLvRows(Machine m, List<BDev> devs, BDev pv, List<BRow> rows, string indent, int depth)
        {
            var c = m.ContentOf(pv);
            if (c == null || c.Kind != "LVM2_member") return;
            var vg = m.VgOfPv(pv.Key);
            if (vg == null) return;
            var lvs = devs.Where(x => x.Type == "lvm" && x.Vg == vg && x.Lv.Segs.Any(s => s.Pv == pv.Key)).OrderBy(x => x.Name, StringComparer.Ordinal).ToList();
            for (int i = 0; i < lvs.Count; i++) rows.Add(new BRow { D = lvs[i], Prefix = indent + (i == lvs.Count - 1 ? Tree2 : Tree1), Depth = depth });
        }

        static List<string> MountsOf(Machine m, BDev d)
        {
            var l = new List<string>();
            var c = m.ContentOf(d);
            if (c != null && c.Kind == "swap" && m.Swaps.Any(s => s.Dev == d.Path || m.FindDev(s.Dev) != null && m.FindDev(s.Dev).Key == d.Key)) l.Add("[SWAP]");
            if (c != null && c.FsId != null) foreach (var mt in m.Mounts) if (mt.FsId == c.FsId && !mt.Bind && mt.Path != "/sysroot" || mt.FsId == c.FsId && mt.Path == "/sysroot") l.Add(mt.Path);
            return l;
        }

        static int Blkid(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "o:s:pUL:cg", "output=|o,match-tag=|s,probe|p,label=|L,uuid=|U", "blkid");
            if (o.Error != null) return Fail(io, o);
            var m = sh.M;
            if (sh.P.Cr.Uid != 0 && o.Args.Count == 0) { return 2; }
            if (o.Has("L")) { var d = m.FindDev("LABEL=" + o.Get("L")); if (d == null) return 2; io.L(d.Path); return 0; }
            if (o.Has("U")) { var d = m.FindDev("UUID=" + o.Get("U")); if (d == null) return 2; io.L(d.Path); return 0; }
            var list = o.Args.Count > 0 ? o.Args.Select(x => m.FindDev(x)).ToList() : m.Devices().Where(x => m.ContentOf(x) != null || x.Part != null).OrderBy(x => x.Type == "lvm" ? 0 : 1).ToList();
            int st = 2;
            var tags = o.All("s");
            foreach (var d in list)
            {
                if (d == null) continue;
                var c = m.ContentOf(d);
                var kv = new List<string[]>();
                if (c != null && c.Label.Length > 0) kv.Add(new[] { "LABEL", c.Label });
                if (c != null && c.Uuid.Length > 0) kv.Add(new[] { "UUID", c.Uuid });
                if (c != null && (c.Kind == "xfs" || c.Kind == "vfat")) kv.Add(new[] { "BLOCK_SIZE", "512" });
                if (c != null && c.Kind == "ext4") kv.Add(new[] { "BLOCK_SIZE", "4096" });
                if (c != null) kv.Add(new[] { "TYPE", c.Kind });
                if (d.Part != null) kv.Add(new[] { "PARTUUID", d.Part.PartUuid });
                if (d.Part != null && d.Disk.Label == "gpt" && d.Part.Name.Length > 0) kv.Insert(kv.Count - 1, new[] { "PARTLABEL", d.Part.Name });
                if (tags.Count > 0) kv = kv.Where(x => tags.Contains(x[0])).ToList();
                if (kv.Count == 0) continue;
                st = 0;
                string fmt = o.Get("o", "full");
                if (fmt == "value") foreach (var x in kv) io.L(x[1]);
                else if (fmt == "export") { io.L("DEVNAME=" + d.Path); foreach (var x in kv) io.L(x[0] + "=" + x[1]); io.L(""); }
                else if (fmt == "device") io.L(d.Path);
                else io.L(d.Path + ": " + string.Join(" ", kv.Select(x => x[0] + "=\"" + x[1] + "\"").ToArray()));
            }
            return st;
        }

        // ------------------------------------------------------------ mkfs / mkswap / wipefs
        static BDev DevArg(Sh sh, Io io, string prog, string spec)
        {
            var d = sh.M.FindDev(spec);
            if (d == null)
            {
                bool exists = false;
                try { exists = Find(sh, spec, true).Exists; } catch (VfsEx) { }
                io.E(prog + ": " + (exists ? "cannot open " + spec + ": not a block device" : spec + ": No such file or directory"));
            }
            return d;
        }

        static int Mkfs(Sh sh, List<string> a, Io io, string kind)
        {
            string prog = a[0];
            var o = kind == "xfs" ? Opts.Parse(a, "fL:b:d:i:l:n:m:qKs:N", "", prog) : kind == "ext4" ? Opts.Parse(a, "FL:t:b:m:qN:I:E:O:jcnU:T:", "", prog) : Opts.Parse(a, "n:F:vIi:cS:s:", "", prog);
            if (o.Error != null) return Fail(io, o);
            var m = sh.M;
            if (o.Args.Count == 0) { io.E(kind == "xfs" ? "no device name given in argument list\nUsage: mkfs.xfs ..." : kind == "ext4" ? "Usage: mke2fs [-c|-l filename] [-b block-size] [-C cluster-size] device [blocks-count]" : "mkfs.fat: No device specified."); return 1; }
            if (sh.P.Cr.Uid != 0) { io.E(prog + ": cannot open " + o.Args[0] + ": Permission denied"); return 1; }
            string spec = o.Args[0];
            var d = m.FindDev(spec);
            // a file can hold a file system too (rare in the exam); only devices here
            if (d == null) { io.E(kind == "xfs" ? "mkfs.xfs: cannot open " + spec + ": No such file or directory" : kind == "ext4" ? "mke2fs 1.46.5 (30-Dec-2021)\nThe file " + spec + " does not exist and no size was specified." : "mkfs.fat 4.2 (2021-01-31)\nmkfs.fat: unable to open " + spec + ": No such file or directory"); return 1; }
            if (d.Type == "part" && (d.Part.Type == "5" || d.Part.Type == "f")) { io.E(prog + ": " + d.Path + " is an extended partition (it only holds logical partitions)"); return 1; }
            if (m.InUse(d) != null) { io.E(kind == "xfs" ? "mkfs.xfs: cannot open " + d.Path + ": Device or resource busy" : kind == "ext4" ? "mke2fs 1.46.5 (30-Dec-2021)\n" + d.Path + " contains a " + (m.ContentOf(d) == null ? "" : m.ContentOf(d).Kind) + " file system\n\t/dev/" + d.Name + " is mounted; will not make a filesystem here!" : "mkfs.fat 4.2 (2021-01-31)\nmkfs.fat: unable to open " + d.Path + ": Device or resource busy"); return 1; }
            var old = m.ContentOf(d);
            if (d.Type == "disk" && d.Disk.Parts.Count > 0 && !(o.Has("f") || o.Has("F")))
            {
                if (kind == "xfs") { io.E("mkfs.xfs: " + d.Path + " appears to contain a partition table (" + d.Disk.Label + ").\nmkfs.xfs: Use the -f option to force overwrite."); return 1; }
            }
            if (old != null && old.Kind == "LVM2_member" && m.VgOfPv(d.Key) != null) { io.E(prog + ": cannot open " + d.Path + ": Device or resource busy"); return 1; }
            if (old != null)
            {
                if (kind == "xfs" && !o.Has("f")) { io.E("mkfs.xfs: " + d.Path + " appears to contain an existing filesystem (" + old.Kind + ").\nmkfs.xfs: Use the -f option to force overwrite."); return 1; }
                if (kind == "ext4" && !o.Has("F"))
                {
                    io.L("mke2fs 1.46.5 (30-Dec-2021)");
                    if (!Confirm(sh, io, d.Path + " contains a " + old.Kind + " file system" + (old.Label.Length > 0 ? " labelled '" + old.Label + "'" : "") + "\nProceed anyway? (y,N) ")) return 1;
                }
            }
            else if (kind == "ext4" && !o.Has("q")) io.L("mke2fs 1.46.5 (30-Dec-2021)");
            string label = kind == "xfs" ? o.Get("L") : kind == "ext4" ? o.Get("L") : o.Get("n");
            if (kind == "xfs" && label != null && label.Length > 12) { io.E("mkfs.xfs: label " + label + " is too long (max 12 characters)"); return 1; }
            if (kind == "ext4" && label != null && label.Length > 16) { io.E("Warning: label too long; will be truncated to '" + label.Substring(0, 16) + "'"); label = label.Substring(0, 16); }
            var fs = m.NewFs(kind, d.Bytes, d.Path);
            var c = new BlockContent { Kind = kind, FsId = fs.Id, Uuid = m.FsUuid(kind), Label = label == null ? "" : (kind == "vfat" ? label.ToUpperInvariant() : label) };
            m.SetContent(d, c);
            m.SyncDev();
            long blocks = d.Bytes / 4096;
            if (o.Has("q")) { }
            else if (kind == "xfs")
            {
                long ag = 4; long agsize = blocks / ag;
                io.L("meta-data=" + d.Path.PadRight(22) + " isize=512    agcount=" + ag + ", agsize=" + agsize + " blks");
                io.L("         =                       sectsz=512   attr=2, projid32bit=1");
                io.L("         =                       crc=1        finobt=1, sparse=1, rmapbt=0");
                io.L("         =                       reflink=1    bigtime=1 inobtcount=1 nrext64=0");
                io.L("data     =                       bsize=4096   blocks=" + blocks + ", imaxpct=25");
                io.L("         =                       sunit=0      swidth=0 blks");
                io.L("naming   =version 2              bsize=4096   ascii-ci=0, ftype=1");
                io.L("log      =internal log           bsize=4096   blocks=" + Math.Max(4096, blocks / 64) + ", version=2");
                io.L("         =                       sectsz=512   sunit=0 blks, lazy-count=1");
                io.L("realtime =none                   extsz=4096   blocks=0, rtextents=0");
                io.L("Discarding blocks...Done.");
            }
            else if (kind == "ext4")
            {
                long inodes = Math.Max(16384, blocks / 4);
                io.L("Discarding device blocks: done                            ");
                io.L("Creating filesystem with " + blocks + " 4k blocks and " + inodes + " inodes");
                io.L("Filesystem UUID: " + c.Uuid);
                if (blocks > 32768) { var bk = new List<long>(); foreach (var b in new long[] { 32768, 98304, 163840, 229376, 294912, 819200, 884736, 1605632 }) if (b < blocks) bk.Add(b); io.L("Superblock backups stored on blocks: \n\t" + string.Join(", ", bk.Select(x => x.ToString()).ToArray())); }
                io.L("\nAllocating group tables: done                            ");
                io.L("Writing inode tables: done                            ");
                io.L("Creating journal (" + (blocks < 262144 ? 4096 : 16384) + " blocks): done");
                io.L("Writing superblocks and filesystem accounting information: done\n");
            }
            else io.L("mkfs.fat 4.2 (2021-01-31)");
            return 0;
        }

        static int Mkswap(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "L:U:fcp:", "label=|L,uuid=|U,force|f,check|c,pagesize=|p", "mkswap");
            if (o.Error != null) return Fail(io, o);
            var m = sh.M;
            if (o.Args.Count == 0) { io.E("mkswap: error: Nowhere to set up swap on?\nTry 'mkswap --help' for more information."); return 1; }
            if (sh.P.Cr.Uid != 0) { io.E("mkswap: cannot open " + o.Args[0] + ": Permission denied"); return 1; }
            string spec = o.Args[0];
            var d = m.FindDev(spec);
            string uuid = o.Get("U") ?? m.NewUuid();
            string label = o.Get("L") ?? "";
            if (d == null)
            {
                Loc l;
                try { l = Find(sh, spec, true); } catch (VfsEx e) { io.E("mkswap: cannot open " + spec + ": " + e.Message); return 1; }
                if (!l.Exists) { io.E("mkswap: cannot open " + spec + ": No such file or directory"); return 1; }
                long size = l.Node.Size;
                if (size < 40960) { io.E("mkswap: error: swap area needs to be at least 40 KiB"); return 1; }
                if ((l.Node.Mode & 0x3F) != 0) io.E("mkswap: " + spec + ": insecure permissions " + Convert.ToString(l.Node.Mode & 0xFFF, 8).PadLeft(4, '0') + ", fix with: chmod 0600 " + spec);
                l.Node.Text = "\u0000SWAPSPACE2"; l.Node.Blob = "swap"; l.Node.BinSize = size;
                l.Node.Ctx = Selinux.WithType(l.Node.Ctx, "swapfile_t");
                io.L("Setting up swapspace version 1, size = " + MkswapSize(size - 4096) + " (" + (size - 4096) + " bytes)\n" + (label.Length > 0 ? "LABEL=" + label + ", " : "no label, ") + "UUID=" + uuid);
                return 0;
            }
            if (m.InUse(d) != null) { io.E("mkswap: error: " + d.Path + " is mounted; will not make swapspace"); return 1; }
            var old = m.ContentOf(d);
            if (old != null && old.Kind == "LVM2_member" && m.VgOfPv(d.Key) != null) { io.E("mkswap: cannot open " + d.Path + ": Device or resource busy"); return 1; }
            if (d.Type == "disk" && d.Disk.Parts.Count > 0 && !o.Has("f")) { io.E("mkswap: " + d.Path + ": warning: wiping old dos signature.\nmkswap: error: " + d.Path + " contains partitions; use -f to force"); return 1; }
            if (old != null && old.Kind != "swap") io.L("mkswap: " + d.Path + ": warning: wiping old " + old.Kind + " signature.");
            m.SetContent(d, new BlockContent { Kind = "swap", Uuid = uuid, Label = label });
            m.SyncDev();
            io.L("Setting up swapspace version 1, size = " + MkswapSize(d.Bytes - 4096) + " (" + (d.Bytes - 4096) + " bytes)\n" + (label.Length > 0 ? "LABEL=" + label + ", " : "no label, ") + "UUID=" + uuid);
            return 0;
        }
        static string MkswapSize(long b)
        {
            double v = b; string[] u = { "B", "KiB", "MiB", "GiB", "TiB" }; int i = 0;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            return (Math.Floor(v * 10) / 10).ToString("0.#", CultureInfo.InvariantCulture) + " " + u[i];
        }

        static int Swapon(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "asp:veL:U:", "all|a,summary|s,priority=|p,verbose|v,show=?,discard=?,noheadings,bytes,ifexists|e,fixpgsz|f", "swapon");
            if (o.Error != null) return Fail(io, o);
            var m = sh.M;
            if (o.Has("s")) { io.P(m.ProcSwaps()); return 0; }
            if (o.Has("show") || (o.Args.Count == 0 && !o.Has("a")))
            {
                if (!o.Has("show") && o.Args.Count == 0) { io.E("Usage:\n swapon [options] [<spec>]\n\nEnable devices and files for paging and swapping.\n\nOptions:\n -a, --all                enable all swaps from /etc/fstab\n -p, --priority <prio>    specify the priority of the swap device\n -s, --summary            display summary about used swap devices (DEPRECATED)\n     --show[=<columns>]   display summary in definable table"); return 1; }
                if (m.Swaps.Count == 0) return 0;
                if (!o.Has("noheadings")) io.L("NAME      TYPE      SIZE USED PRIO");
                foreach (var s in m.Swaps) io.L(s.Dev.PadRight(9) + " " + (s.Dev.StartsWith("/dev/", StringComparison.Ordinal) ? "partition" : "file").PadRight(9) + " " + LsblkSize(s.Bytes).PadLeft(4) + "   " + (s.UsedKb == 0 ? "0B" : LsblkSize(s.UsedKb * 1024)) + "   " + s.Prio);
                return 0;
            }
            if (sh.P.Cr.Uid != 0) { io.E("swapon: " + (o.Args.Count > 0 ? o.Args[0] : "") + ": swapon failed: Operation not permitted"); return 1; }
            int prio = o.Has("p") ? int.Parse(o.Get("p")) : -2;
            int st = 0;
            if (o.Has("a"))
            {
                foreach (var e in m.Fstab().Where(x => x.Type == "swap"))
                {
                    if (e.Opts.Split(',').Contains("noauto")) continue;
                    int pr = prio;
                    var pm = Regex.Match(e.Opts, "pri=(-?\\d+)"); if (pm.Success) pr = int.Parse(pm.Groups[1].Value);
                    string err = m.SwapOn(e.Spec, pr);
                    if (err != null && !err.Contains("busy")) { io.E(err); st = 4; }
                }
                return st;
            }
            foreach (var x in o.Args)
            {
                string spec = x;
                if (x.StartsWith("UUID=", StringComparison.Ordinal) || x.StartsWith("LABEL=", StringComparison.Ordinal)) spec = x;
                else { try { var l = Find(sh, x, true); if (l.Exists && l.Node.T == 'f') spec = l.GPath; } catch (VfsEx) { } }
                if (spec.StartsWith("/", StringComparison.Ordinal) && !spec.StartsWith("/dev/", StringComparison.Ordinal))
                {
                    try { var l = m.V.G(spec); if (l.Exists && (l.Node.Mode & 0x3F) != 0) io.E("swapon: " + x + ": insecure permissions " + Convert.ToString(l.Node.Mode & 0xFFF, 8).PadLeft(4, '0') + ", 0600 suggested."); } catch (VfsEx) { }
                }
                string err = m.SwapOn(spec, prio);
                if (err != null) { io.E(err); st = 4; }
            }
            return st;
        }
        static int Swapoff(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "av", "all|a,verbose|v", "swapoff");
            if (o.Error != null) return Fail(io, o);
            var m = sh.M;
            if (sh.P.Cr.Uid != 0) { io.E("swapoff: Not superuser."); return 1; }
            if (o.Has("a")) { m.Swaps.Clear(); return 0; }
            int st = 0;
            foreach (var x in o.Args)
            {
                var d = m.FindDev(x);
                string key = d == null ? null : d.Key;
                int n = m.Swaps.RemoveAll(s => s.Dev == x || s.Dev == Abs(sh, x) || (key != null && m.FindDev(s.Dev) != null && m.FindDev(s.Dev).Key == key));
                if (n == 0) { io.E("swapoff: " + x + ": swapoff failed: Invalid argument"); st = 4; }
            }
            return st;
        }

        static int Wipefs(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "afnqt:o:", "all|a,force|f,no-act|n,quiet|q,types=|t,offset=|o", "wipefs");
            if (o.Error != null) return Fail(io, o);
            var m = sh.M;
            if (sh.P.Cr.Uid != 0) { io.E("wipefs: error: " + (o.Args.Count > 0 ? o.Args[0] : "") + ": probing initialization failed: Permission denied"); return 1; }
            int st = 0;
            foreach (var x in o.Args)
            {
                var d = DevArg(sh, io, "wipefs", x);
                if (d == null) { st = 1; continue; }
                var c = m.ContentOf(d);
                bool table = d.Type == "disk" && d.Disk.Label.Length > 0;
                if (!o.Has("a"))
                {
                    if (c == null && !table) continue;
                    io.L("DEVICE OFFSET TYPE UUID LABEL");
                    if (c != null) io.L(d.Name + " 0x" + (c.Kind == "swap" ? "ff6" : c.Kind == "LVM2_member" ? "218" : "0") + " " + c.Kind + " " + c.Uuid + " " + c.Label);
                    if (table) io.L(d.Name + " 0x1fe " + d.Disk.Label);
                    continue;
                }
                if (m.InUse(d) != null || (c != null && c.Kind == "LVM2_member" && m.VgOfPv(d.Key) != null)) { io.E("wipefs: error: " + d.Path + ": probing initialization failed: Device or resource busy"); st = 1; continue; }
                if (c != null) { io.L(d.Path + ": " + (c.Kind == "xfs" ? "4 bytes were erased at offset 0x00000000 (xfs): 58 46 53 42" : c.Kind == "ext4" ? "2 bytes were erased at offset 0x00000438 (ext4): 53 ef" : c.Kind == "swap" ? "10 bytes were erased at offset 0x00000ff6 (swap): 53 57 41 50 53 50 41 43 45 32" : c.Kind == "LVM2_member" ? "8 bytes were erased at offset 0x00000218 (LVM2_member): 4c 56 4d 32 20 30 30 31" : "8 bytes were erased at offset 0x00000052 (vfat): 46 41 54 33 32 20 20 20")); if (!o.Has("n")) m.SetContent(d, null); }
                if (table) { io.L(d.Path + ": 2 bytes were erased at offset 0x000001fe (" + d.Disk.Label + "): 55 aa"); if (!o.Has("n")) { d.Disk.Label = ""; d.Disk.Parts.Clear(); } io.L(d.Path + ": calling ioctl to re-read partition table: Success"); }
                m.SyncDev();
            }
            return st;
        }

        // ------------------------------------------------------------ growing and labels
        static int XfsGrowfs(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "dD:nmL:r", "", "xfs_growfs");
            if (o.Error != null) return Fail(io, o);
            var m = sh.M;
            if (o.Args.Count == 0) { io.E("Usage: xfs_growfs [options] mountpoint"); return 1; }
            if (sh.P.Cr.Uid != 0) { io.E("xfs_growfs: cannot open " + o.Args[0] + ": Permission denied"); return 1; }
            string x = o.Args[0];
            Mount mt = null;
            var d = m.FindDev(x);
            if (d != null) { var c = m.ContentOf(d); if (c != null) mt = m.Mounts.FirstOrDefault(mm => mm.FsId == c.FsId && !mm.Bind); }
            else { try { var l = Find(sh, x, true); mt = m.Mounts.LastOrDefault(mm => mm.Path == l.GPath); } catch (VfsEx) { } }
            if (mt == null || mt.FsType != "xfs") { io.E("xfs_growfs: " + x + " is not a mounted XFS filesystem"); return 1; }
            var fs = m.V.Fs(mt.FsId);
            var dev = m.Devices().FirstOrDefault(dd => { var c = m.ContentOf(dd); return c != null && c.FsId == fs.Id; });
            long before = fs.Bytes / 4096;
            if (dev != null) m.GrowFs(dev);
            long after = fs.Bytes / 4096;
            XfsInfoOut(io, mt.Path, before, dev == null ? mt.Source : dev.Path);
            if (after > before) io.L("data blocks changed from " + (before + before / 50) + " to " + (after + after / 50));
            return 0;
        }
        static void XfsInfoOut(Io io, string mp, long blocks, string dev)
        {
            io.L("meta-data=" + dev.PadRight(22) + " isize=512    agcount=4, agsize=" + (blocks / 4) + " blks\n         =                       sectsz=512   attr=2, projid32bit=1\n         =                       crc=1        finobt=1, sparse=1, rmapbt=0\n         =                       reflink=1    bigtime=1 inobtcount=1 nrext64=0\ndata     =                       bsize=4096   blocks=" + (blocks + blocks / 50) + ", imaxpct=25\n         =                       sunit=0      swidth=0 blks\nnaming   =version 2              bsize=4096   ascii-ci=0, ftype=1\nlog      =internal log           bsize=4096   blocks=16384, version=2\n         =                       sectsz=512   sunit=0 blks, lazy-count=1\nrealtime =none                   extsz=4096   blocks=0, rtextents=0");
        }
        static int XfsInfo(Sh sh, List<string> a, Io io)
        {
            var m = sh.M;
            if (a.Count < 2) { io.E("Usage: xfs_info [-V] [-t mtab] [mountpoint|device|file]"); return 1; }
            var d = m.FindDev(a[1]);
            FileSys fs = null; string dev = a[1];
            if (d != null) { var c = m.ContentOf(d); if (c != null && c.Kind == "xfs") fs = m.V.Fs(c.FsId); dev = d.Path; }
            else { try { var l = Find(sh, a[1], true); if (l.Exists && l.Fs.Type == "xfs") { fs = l.Fs; dev = l.Mnt.Source; } } catch (VfsEx) { } }
            if (fs == null) { io.E("xfs_info: " + a[1] + " is not a valid XFS filesystem (unexpected SB magic number 0x00000000)"); return 1; }
            XfsInfoOut(io, a[1], fs.Bytes / 4096, dev);
            return 0;
        }
        static int XfsAdmin(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "L:U:luf", "", "xfs_admin");
            var m = sh.M;
            if (o.Args.Count == 0) { io.E("Usage: xfs_admin [-efjlpuV] [-c 0|1] [-L label] [-U uuid] device [logdev]"); return 1; }
            var d = m.FindDev(o.Args[0]); var c = m.ContentOf(d);
            if (c == null || c.Kind != "xfs") { io.E("xfs_admin: " + o.Args[0] + " is not a valid XFS filesystem"); return 1; }
            if (o.Has("l")) { io.L("label = \"" + c.Label + "\""); return 0; }
            if (o.Has("u")) { io.L("UUID = " + c.Uuid); return 0; }
            if ((o.Has("L") || o.Has("U")) && m.InUse(d) != null) { io.E("xfs_admin: " + d.Path + " contains a mounted filesystem\n\nfatal error -- couldn't initialize XFS library"); return 1; }
            if (o.Has("L")) { c.Label = o.Get("L") == "--" ? "" : o.Get("L"); io.L("writing all SBs\nnew label = \"" + c.Label + "\""); }
            if (o.Has("U")) { c.Uuid = o.Get("U") == "generate" ? m.NewUuid() : o.Get("U"); io.L("Clearing log and setting UUID\nwriting all SBs\nnew UUID = " + c.Uuid); }
            m.SyncDev();
            return 0;
        }
        static int Resize2fs(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "fFpPM", "", "resize2fs");
            var m = sh.M;
            io.L("resize2fs 1.46.5 (30-Dec-2021)");
            if (o.Args.Count == 0) { io.E("Usage: resize2fs [-d debug_flags] [-f] [-F] [-M] [-P] [-p] device [-b|-s|new_size] [-S RAID-stride] [-z undo_file]"); return 1; }
            var d = m.FindDev(o.Args[0]); var c = m.ContentOf(d);
            if (c == null || c.Kind != "ext4") { io.E("resize2fs: Bad magic number in super-block while trying to open " + o.Args[0] + "\nCouldn't find valid filesystem superblock."); return 1; }
            var fs = m.V.Fs(c.FsId);
            long before = fs.Bytes / 4096;
            var mt = m.Mounts.FirstOrDefault(x => x.FsId == c.FsId && !x.Bind);
            if (o.Args.Count > 1)
            {
                long want = ParseSize(o.Args[1], 4096);
                if (want < fs.Used) { io.E("resize2fs: New size smaller than minimum (" + (fs.Used / 4096) + ")"); return 1; }
                if (want < fs.Bytes && mt != null) { io.E("Filesystem at " + d.Path + " is mounted on " + mt.Path + "; on-line resizing required\nresize2fs: On-line shrinking not supported"); return 1; }
                fs.Bytes = (long)(Math.Min(want, d.Bytes) * 0.952 / 0.952);
            }
            else m.GrowFs(d);
            long after = fs.Bytes / 4096;
            if (after == before) { io.L("The filesystem is already " + (d.Bytes / 4096) + " (4k) blocks long.  Nothing to do!"); return 0; }
            if (mt != null) io.L("Filesystem at " + d.Path + " is mounted on " + mt.Path + "; on-line resizing required\nold_desc_blocks = 1, new_desc_blocks = 1");
            io.L("The filesystem on " + d.Path + " is now " + (d.Bytes / 4096) + " (4k) blocks long.\n");
            return 0;
        }
        static int E2label(Sh sh, List<string> a, Io io)
        {
            var m = sh.M;
            if (a.Count < 2) { io.E("Usage: e2label device [newlabel]"); return 1; }
            var d = m.FindDev(a[1]); var c = m.ContentOf(d);
            if (c == null || c.Kind != "ext4") { io.E("e2label: Bad magic number in super-block while trying to open " + a[1] + "\nCouldn't find valid filesystem superblock."); return 1; }
            if (a.Count > 2) { c.Label = a[2].Length > 16 ? a[2].Substring(0, 16) : a[2]; m.SyncDev(); } else io.L(c.Label);
            return 0;
        }
        static int Tune2fs(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "L:lc:i:m:o:O:U:", "", "tune2fs");
            var m = sh.M;
            io.L("tune2fs 1.46.5 (30-Dec-2021)");
            if (o.Args.Count == 0) { io.E("Usage: tune2fs [-c max_mounts_count] [-e errors_behavior] [-f] [-g group]\n\t[-i interval[d|m|w]] [-j] [-J journal_options] [-l]\n\t[-m reserved_blocks_percent] [-o [^]mount_options[,...]]\n\t[-r reserved_blocks_count] [-u user] [-C mount_count]\n\t[-L volume_label] [-M last_mounted_dir]\n\t[-O [^]feature[,...]] [-Q quota_options]\n\t[-E extended-option[,...]] [-T last_check_time] [-U UUID]\n\t[-I new_inode_size] [-z undo_file] device"); return 1; }
            var d = m.FindDev(o.Args[0]); var c = m.ContentOf(d);
            if (c == null || c.Kind != "ext4") { io.E("tune2fs: Bad magic number in super-block while trying to open " + o.Args[0]); return 1; }
            if (o.Has("L")) { c.Label = o.Get("L"); m.SyncDev(); }
            if (o.Has("U")) c.Uuid = o.Get("U") == "random" || o.Get("U") == "time" ? m.NewUuid() : o.Get("U");
            if (o.Has("l")) io.L("Filesystem volume name:   " + (c.Label.Length > 0 ? c.Label : "<none>") + "\nLast mounted on:          <not available>\nFilesystem UUID:          " + c.Uuid + "\nFilesystem magic number:  0xEF53\nFilesystem revision #:    1 (dynamic)\nFilesystem features:      has_journal ext_attr resize_inode dir_index filetype extent 64bit flex_bg sparse_super large_file huge_file dir_nlink extra_isize metadata_csum\nDefault mount options:    user_xattr acl\nFilesystem state:         clean\nBlock size:               4096");
            return 0;
        }
        static int Fsck(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "fnpyyANRVT", "", a[0]);
            var m = sh.M;
            if (o.Args.Count == 0) return 0;
            var d = m.FindDev(o.Args[0]); var c = m.ContentOf(d);
            if (c == null) { io.E(a[0] + ": Bad magic number in super-block while trying to open " + o.Args[0]); return 8; }
            if (m.InUse(d) != null) { io.E(o.Args[0] + " is mounted.\ne2fsck: Cannot continue, aborting.\n"); return 8; }
            if (c.Kind == "xfs") { io.L("If you wish to check the consistency of an XFS filesystem or\nrepair a damaged filesystem, see xfs_repair(8)."); return 0; }
            io.L("e2fsck 1.46.5 (30-Dec-2021)\nPass 1: Checking inodes, blocks, and sizes\nPass 2: Checking directory structure\nPass 3: Checking directory connectivity\nPass 4: Checking reference counts\nPass 5: Checking group summary information\n" + d.Path + ": 11/" + (d.Bytes / 16384) + " files (0.0% non-contiguous), " + (d.Bytes / 4096 / 30) + "/" + (d.Bytes / 4096) + " blocks");
            var fs = m.V.Fs(c.FsId); if (fs != null) fs.Dirty = false;
            return 0;
        }

        static int Showmount(Sh sh, List<string> a, Io io)
        {
            var m = sh.M;
            var o = Opts.Parse(a, "ead", "exports|e,all|a,directories|d", "showmount");
            string host = o.Args.Count > 0 ? o.Args[0] : m.HostnameNow;
            string ip = m.Net.Resolve(m, host);
            if (ip == null) { io.E("clnt_create: RPC: Unknown host"); return 1; }
            if (!m.Net.Reachable(m, ip)) { io.E("clnt_create: RPC: Port mapper failure - Unable to receive: errno 113 (No route to host)"); return 1; }
            if (ip != m.Net.UtilityIp) { io.E("clnt_create: RPC: Program not registered"); return 1; }
            io.L("Export list for " + host + ":");
            foreach (var e in m.Net.Exports) io.L(e.Path.PadRight(26) + " " + e.Clients);
            return 0;
        }
    }

    public partial class Machine
    {
        public string FstabSeen;
        public bool DvdInserted = true;
        public static readonly HashSet<string> KnownFsTypes = new HashSet<string> { "xfs", "ext4", "ext3", "ext2", "vfat", "msdos", "swap", "nfs", "nfs4", "tmpfs", "iso9660", "auto", "none", "proc", "sysfs", "devtmpfs", "cifs", "fuse", "overlay", "bind" };
    }
}
