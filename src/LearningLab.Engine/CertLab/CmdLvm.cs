// Cert Lab - LVM: pvcreate / pvs / pvdisplay / pvremove, vgcreate / vgs / vgdisplay / vgextend / vgreduce /
// vgremove / vgrename / vgchange, lvcreate / lvs / lvdisplay / lvextend / lvresize / lvreduce / lvremove /
// lvrename / lvchange, with extent rounding and the size notation of LVM (<19.00g, 1020.00m).
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
        static void RegLvm()
        {
            P("pvcreate", Pvcreate); P("pvs", Pvs); P("pvdisplay", Pvdisplay); P("pvremove", Pvremove); P("pvscan", (sh, a, io) => { foreach (var vg in sh.M.Vgs) foreach (var pv in vg.Pvs) io.L("  PV " + sh.M.DevPathOfKey(pv).PadRight(10) + " VG " + vg.Name.PadRight(10) + " lvm2 [" + LvmSize(sh.M.PvExtents(pv, vg) * vg.PeSize, "h", true) + " / " + LvmSize((sh.M.PvExtents(pv, vg) - sh.M.PvUsed(pv, vg)) * vg.PeSize, "h", true) + " free]"); io.L("  Total: " + sh.M.Vgs.Sum(v => v.Pvs.Count) + " [] / in use: " + sh.M.Vgs.Sum(v => v.Pvs.Count) + " [] / in no VG: 0 [0   ]"); return 0; });
            P("pvmove", (sh, a, io) => { io.L("  " + (a.Count > 1 ? a[1] : "") + ": Moved: 100.00%"); return 0; }); P("pvresize", (sh, a, io) => { io.L("  Physical volume \"" + (a.Count > 1 ? a[a.Count - 1] : "") + "\" changed\n  1 physical volume(s) resized or updated / 0 physical volume(s) not resized"); return 0; }); P("pvchange", (sh, a, io) => 0);
            P("vgcreate", Vgcreate); P("vgs", Vgs); P("vgdisplay", Vgdisplay); P("vgextend", Vgextend); P("vgreduce", Vgreduce); P("vgremove", Vgremove); P("vgrename", Vgrename); P("vgchange", Vgchange); P("vgscan", (sh, a, io) => { foreach (var vg in sh.M.Vgs) io.L("  Found volume group \"" + vg.Name + "\" using metadata type lvm2"); return 0; });
            P("lvcreate", Lvcreate); P("lvs", Lvs); P("lvdisplay", Lvdisplay); P("lvextend", (sh, a, io) => Lvresize(sh, a, io, "extend")); P("lvresize", (sh, a, io) => Lvresize(sh, a, io, "resize")); P("lvreduce", (sh, a, io) => Lvresize(sh, a, io, "reduce"));
            P("lvremove", Lvremove); P("lvrename", Lvrename); P("lvchange", Lvchange); P("lvscan", (sh, a, io) => { foreach (var vg in sh.M.Vgs) foreach (var lv in vg.Lvs) io.L("  " + (lv.Active ? "ACTIVE  " : "inactive") + "          '/dev/" + vg.Name + "/" + lv.Name + "' [" + LvmSize(lv.Extents * vg.PeSize, "h", true) + "] inherit"); return 0; });
            P("lvm", (sh, a, io) => { if (a.Count < 2) { io.L("lvm> (Cert Lab: run the LVM commands directly, e.g. lvs)"); return 0; } var b = a.Skip(1).ToList(); CmdFn f; if (Sh.Programs.TryGetValue(b[0], out f)) return f(sh, b, io); io.E("  No such command '" + b[0] + "'.  Try 'help'."); return 3; });
            P("fsadm", (sh, a, io) => { var d = sh.M.FindDev(a[a.Count - 1]); if (d != null) sh.M.GrowFs(d); return 0; });
        }

        /// <summary>LVM's size notation: h = the biggest unit that keeps the number at least 1, two decimals, "&lt;" when rounded up.</summary>
        public static string LvmSize(long bytes, string unit, bool display)
        {
            string[] us = { "b", "k", "m", "g", "t", "p" };
            double v = bytes; int i = 0;
            unit = (unit ?? "h").ToLowerInvariant();
            if (unit == "h" || unit == "") { while (v >= 1024 && i < us.Length - 1) { v /= 1024; i++; } }
            else { i = Array.IndexOf(us, unit.Substring(0, 1)); if (i < 0) i = 3; v = bytes / Math.Pow(1024, i); }
            if (bytes == 0) return display ? "0   " : "0 ";
            double r = Math.Round(v, 2);
            string lt = r > v + 1e-9 ? "<" : "";
            string num = r.ToString("0.00", CultureInfo.InvariantCulture);
            if (display) return lt + num + " " + (i == 0 ? "B" : us[i].ToUpperInvariant() + "iB");
            return lt + num + us[i];
        }

        static bool LvmRoot(Sh sh, Io io)
        {
            if (sh.P.Cr.Uid == 0) return true;
            io.E("  WARNING: Running as a non-root user. Functionality may be unavailable.\n  /run/lock/lvm/P_global:aux: open failed: Permission denied");
            return false;
        }

        static VolGroup FindVg(Machine m, string name) { return m.Vgs.FirstOrDefault(v => v.Name == name.Replace("/dev/", "").TrimEnd('/')); }
        static Lv FindLv(Machine m, string spec, out VolGroup vg)
        {
            vg = null;
            string s = spec;
            if (s.StartsWith("/dev/mapper/", StringComparison.Ordinal)) { var d = m.FindDev(s); if (d != null && d.Lv != null) { vg = d.Vg; return d.Lv; } return null; }
            if (s.StartsWith("/dev/", StringComparison.Ordinal)) s = s.Substring(5);
            var p = s.Split('/');
            if (p.Length != 2) return null;
            vg = FindVg(m, p[0]);
            if (vg == null) return null;
            return vg.Lvs.FirstOrDefault(l => l.Name == p[1]);
        }
        static bool LvOpen(Machine m, VolGroup vg, Lv lv)
        {
            var d = m.Devices().FirstOrDefault(x => x.Lv == lv);
            return d != null && m.InUse(d) != null;
        }

        // ------------------------------------------------------------ PVs
        static int Pvcreate(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "fyvu:", "force|f,yes|y,verbose|v,uuid=|u,norestorefile", "pvcreate");
            if (o.Error != null) return Fail(io, o);
            if (!LvmRoot(sh, io)) return 5;
            var m = sh.M;
            if (o.Args.Count == 0) { io.E("  Please enter a physical volume path."); return 3; }
            int st = 0;
            foreach (var x in o.Args)
            {
                var d = m.FindDev(x);
                if (d == null) { io.E("  No device found for " + x + "."); st = 5; continue; }
                if (d.Type == "disk" && d.Disk.Parts.Count > 0) { io.E("  Cannot use " + d.Path + ": device is partitioned"); st = 5; continue; }
                if (d.Type == "part" && (d.Part.Type == "5" || d.Part.Type == "f")) { io.E("  Cannot use " + d.Path + ": device is too small (pv_min_size)"); st = 5; continue; }
                if (m.InUse(d) != null) { io.E("  Can't open " + d.Path + " exclusively.  Mounted filesystem?"); st = 5; continue; }
                var c = m.ContentOf(d);
                if (c != null && c.Kind == "LVM2_member")
                {
                    var vg = m.VgOfPv(d.Key);
                    if (vg != null) { io.E("  Can't initialize physical volume \"" + d.Path + "\" of volume group \"" + vg.Name + "\" without -ff\n  " + d.Path + ": physical volume not initialized."); st = 5; continue; }
                }
                else if (c != null && !o.Has("f") && !o.Has("y"))
                {
                    if (!Confirm(sh, io, "WARNING: " + c.Kind + " signature detected on " + d.Path + " at offset 0. Wipe it? [y/n]: ")) { io.E("  Aborted wiping of " + c.Kind + ".\n  1 existing signature left on the device."); st = 5; continue; }
                    io.L("  Wiping " + c.Kind + " signature on " + d.Path + ".");
                }
                else if (c != null) io.L("  Wiping " + c.Kind + " signature on " + d.Path + ".");
                m.SetContent(d, new BlockContent { Kind = "LVM2_member", Uuid = m.PvUuid() });
                m.SyncDev();
                LvmDevicesFile(m, d.Path, true);
                io.L("  Physical volume \"" + d.Path + "\" successfully created.");
            }
            return st;
        }
        static void LvmDevicesFile(Machine m, string dev, bool add)
        {
            const string f = "/etc/lvm/devices/system.devices";
            string t = m.V.Cat(f) ?? "# LVM uses devices listed in this file.\n# Created by LVM command pvcreate pid 1234 at Mon Mar  2 09:00:00 2026\nVERSION=1.1.1\n";
            var lines = t.Split('\n').Where(x => x.Length > 0 && !x.Contains("DEVNAME=" + dev + " ")).ToList();
            if (add) lines.Add("IDTYPE=devname IDNAME=" + dev + " DEVNAME=" + dev + " PVID=" + m.NewHex(32) + " PART=0");
            try { m.V.EnsureDir("/etc/lvm/devices", 0x1ED, null); m.SysWrite(f, string.Join("\n", lines.ToArray()) + "\n"); } catch (VfsEx) { }
        }

        static int Pvremove(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "fyv", "force|f,yes|y", "pvremove");
            if (!LvmRoot(sh, io)) return 5;
            var m = sh.M; int st = 0;
            foreach (var x in o.Args)
            {
                var d = m.FindDev(x); var c = m.ContentOf(d);
                if (c == null || c.Kind != "LVM2_member") { io.E("  No PV found on device " + x + "."); st = 5; continue; }
                var vg = m.VgOfPv(d.Key);
                if (vg != null) { io.E("  PV " + d.Path + " is used by VG " + vg.Name + " so please use vgreduce first.\n  (If you are certain you need pvremove, then confirm by using --force twice.)\n  " + d.Path + ": physical volume label not removed."); st = 5; continue; }
                m.SetContent(d, null);
                LvmDevicesFile(m, d.Path, false);
                io.L("  Labels on physical volume \"" + d.Path + "\" successfully wiped.");
            }
            return st;
        }

        static string Units(Opts o) { return o.Get("units") ?? "h"; }

        static void LvmTable(Io io, Opts o, List<string> head, List<string[]> rows, HashSet<int> right)
        {
            bool noHead = o.Has("noheadings");
            string sep = o.Get("separator");
            var all = new List<string[]>();
            if (!noHead) all.Add(head.ToArray());
            all.AddRange(rows);
            if (sep != null) { foreach (var r in all) io.L("  " + string.Join(sep, r)); return; }
            var w = new int[head.Count];
            foreach (var r in all) for (int i = 0; i < head.Count; i++) w[i] = Math.Max(w[i], r[i].Length);
            foreach (var r in all)
            {
                var sb = new StringBuilder("  ");
                for (int i = 0; i < r.Length; i++) { sb.Append(right.Contains(i) ? r[i].PadLeft(w[i]) : r[i].PadRight(w[i])); if (i < r.Length - 1) sb.Append(' '); }
                io.L(sb.ToString().TrimEnd());
            }
        }

        static int Pvs(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "ao:vO:S:", "all|a,options=|o,units=,noheadings,nosuffix,separator=,sort=|O,select=|S,segments", "pvs");
            if (o.Error != null) return Fail(io, o);
            if (!LvmRoot(sh, io)) return 5;
            var m = sh.M;
            string u = Units(o);
            var cols = new List<string> { "pv_name", "vg_name", "pv_fmt", "pv_attr", "pv_size", "pv_free" };
            var opt = o.Get("o");
            if (opt != null) { if (opt.StartsWith("+", StringComparison.Ordinal)) cols.AddRange(opt.Substring(1).Split(',')); else cols = opt.Split(',').ToList(); }
            var heads = cols.Select(c => PvHead(c)).ToList();
            var rows = new List<string[]>();
            foreach (var d in m.Devices())
            {
                var c = m.ContentOf(d);
                if (c == null || c.Kind != "LVM2_member") continue;
                if (o.Args.Count > 0 && !o.Args.Any(x => m.FindDev(x) != null && m.FindDev(x).Key == d.Key)) continue;
                var vg = m.VgOfPv(d.Key);
                long size = vg == null ? d.Bytes : m.PvExtents(d.Key, vg) * vg.PeSize;
                long free = vg == null ? d.Bytes : (m.PvExtents(d.Key, vg) - m.PvUsed(d.Key, vg)) * vg.PeSize;
                rows.Add(cols.Select(col =>
                {
                    switch (col)
                    {
                        case "pv_name": return d.Path;
                        case "vg_name": return vg == null ? "" : vg.Name;
                        case "pv_fmt": return "lvm2";
                        case "pv_attr": return vg == null ? "---" : "a--";
                        case "pv_size": return o.Has("nosuffix") ? LvmSize(size, u, false).TrimEnd('b', 'k', 'm', 'g', 't').TrimStart('<') : LvmSize(size, u, false);
                        case "pv_free": return o.Has("nosuffix") ? LvmSize(free, u, false).TrimEnd('b', 'k', 'm', 'g', 't').TrimStart('<') : LvmSize(free, u, false);
                        case "pv_uuid": return c.Uuid;
                        case "dev_size": return LvmSize(d.Bytes, u, false);
                        case "pv_pe_count": return vg == null ? "0" : m.PvExtents(d.Key, vg).ToString();
                        case "pv_pe_alloc_count": return vg == null ? "0" : m.PvUsed(d.Key, vg).ToString();
                        case "pv_used": return vg == null ? "0 " : LvmSize(m.PvUsed(d.Key, vg) * vg.PeSize, u, false);
                    }
                    return "";
                }).ToArray());
            }
            if (rows.Count == 0 && o.Args.Count > 0) { foreach (var x in o.Args) io.E("  Failed to find physical volume \"" + x + "\"."); return 5; }
            LvmTable(io, o, heads, rows, new HashSet<int>(cols.Select((c, i) => c.EndsWith("size", StringComparison.Ordinal) || c.EndsWith("free", StringComparison.Ordinal) || c.Contains("count") || c == "pv_used" ? i : -1).Where(i => i >= 0)));
            return 0;
        }
        static string PvHead(string c)
        {
            switch (c) { case "pv_name": return "PV"; case "vg_name": return "VG"; case "pv_fmt": return "Fmt"; case "pv_attr": return "Attr"; case "pv_size": return "PSize"; case "pv_free": return "PFree"; case "pv_uuid": return "PV UUID"; case "dev_size": return "DevSize"; case "pv_pe_count": return "PE"; case "pv_pe_alloc_count": return "Alloc"; case "pv_used": return "Used"; }
            return c;
        }

        static int Pvdisplay(Sh sh, List<string> a, Io io)
        {
            if (!LvmRoot(sh, io)) return 5;
            var m = sh.M;
            var o = Opts.Parse(a, "smvC", "short|s,maps|m,columns|C", "pvdisplay");
            if (o.Has("C")) return Pvs(sh, a.Where(x => x != "-C").ToList(), io);
            bool any = false;
            foreach (var d in m.Devices())
            {
                var c = m.ContentOf(d);
                if (c == null || c.Kind != "LVM2_member") continue;
                if (o.Args.Count > 0 && !o.Args.Any(x => m.FindDev(x) != null && m.FindDev(x).Key == d.Key)) continue;
                any = true;
                var vg = m.VgOfPv(d.Key);
                if (vg == null)
                {
                    io.L("  \"" + d.Path + "\" is a new physical volume of \"" + LvmSize(d.Bytes, "h", true) + "\"\n  --- NEW Physical volume ---\n  PV Name               " + d.Path + "\n  VG Name               \n  PV Size               " + LvmSize(d.Bytes, "h", true) + "\n  Allocatable           NO\n  PE Size               0   \n  Total PE              0\n  Free PE               0\n  Allocated PE          0\n  PV UUID               " + c.Uuid + "\n   ");
                    continue;
                }
                long tot = m.PvExtents(d.Key, vg), used = m.PvUsed(d.Key, vg);
                long notUsable = d.Bytes - tot * vg.PeSize;
                io.L("  --- Physical volume ---\n  PV Name               " + d.Path + "\n  VG Name               " + vg.Name + "\n  PV Size               " + LvmSize(d.Bytes, "h", true) + " / not usable " + LvmSize(notUsable, "h", true) + "\n  Allocatable           yes" + (used == tot ? " (but full)" : "") + "\n  PE Size               " + LvmSize(vg.PeSize, "h", true) + "\n  Total PE              " + tot + "\n  Free PE               " + (tot - used) + "\n  Allocated PE          " + used + "\n  PV UUID               " + c.Uuid + "\n   ");
            }
            if (!any && o.Args.Count > 0) { io.E("  Failed to find physical volume \"" + o.Args[0] + "\"."); return 5; }
            return 0;
        }

        // ------------------------------------------------------------ VGs
        static bool ValidLvmName(string n) { return Regex.IsMatch(n, "^[A-Za-z0-9+_.][A-Za-z0-9+_.-]*$") && n != "." && n != ".." && !n.StartsWith("-", StringComparison.Ordinal) && n.Length <= 127; }

        static List<string> PrepPvs(Sh sh, Io io, List<string> devs, bool autoCreate, out bool failed)
        {
            var m = sh.M;
            var keys = new List<string>();
            failed = false;
            foreach (var x in devs)
            {
                var d = m.FindDev(x);
                if (d == null) { io.E("  No device found for " + x + "."); failed = true; return keys; }
                var c = m.ContentOf(d);
                if (c == null || c.Kind != "LVM2_member")
                {
                    if (!autoCreate) { failed = true; return keys; }
                    if (Pvcreate(sh, new List<string> { "pvcreate", x }, io) != 0) { failed = true; return keys; }
                }
                var vg = m.VgOfPv(d.Key);
                if (vg != null) { io.E("  Physical volume '" + d.Path + "' is already in volume group '" + vg.Name + "'\n  Unable to add physical volume '" + d.Path + "' to volume group '" + vg.Name + "'"); failed = true; return keys; }
                keys.Add(d.Key);
            }
            return keys;
        }

        static int Vgcreate(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "s:fyv", "physicalextentsize=|s,force|f,yes|y,verbose|v", "vgcreate");
            if (o.Error != null) return Fail(io, o);
            if (!LvmRoot(sh, io)) return 5;
            var m = sh.M;
            if (o.Args.Count < 2) { io.E(o.Args.Count == 0 ? "  Please provide volume group name and physical volumes" : "  Please enter physical volume name(s)"); return 3; }
            string name = o.Args[0];
            if (!ValidLvmName(name)) { io.E("  Name contains invalid character, valid set includes: [a-zA-Z0-9.-_+].\n  New volume group name \"" + name + "\" is invalid."); return 3; }
            if (FindVg(m, name) != null) { io.E("  A volume group called " + name + " already exists."); return 5; }
            long pe = 4L * 1024 * 1024;
            if (o.Has("s"))
            {
                pe = ParseSize(o.Get("s"), 1024L * 1024);
                if (pe <= 0 || (pe & (pe - 1)) != 0 || pe < 1024) { io.E("  Invalid physical extent size " + o.Get("s") + "."); return 3; }
            }
            bool failed;
            var keys = PrepPvs(sh, io, o.Args.Skip(1).ToList(), true, out failed);
            if (failed) return 5;
            var vg = new VolGroup { Name = name, Uuid = m.PvUuid(), PeSize = pe, Pvs = keys };
            m.Vgs.Add(vg);
            io.L("  Volume group \"" + name + "\" successfully created");
            return 0;
        }

        static int Vgs(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "ao:vO:S:", "all|a,options=|o,units=,noheadings,nosuffix,separator=,sort=|O,select=|S", "vgs");
            if (o.Error != null) return Fail(io, o);
            if (!LvmRoot(sh, io)) return 5;
            var m = sh.M;
            string u = Units(o);
            var cols = new List<string> { "vg_name", "pv_count", "lv_count", "snap_count", "vg_attr", "vg_size", "vg_free" };
            var opt = o.Get("o");
            if (opt != null) { if (opt.StartsWith("+", StringComparison.Ordinal)) cols.AddRange(opt.Substring(1).Split(',')); else cols = opt.Split(',').ToList(); }
            var heads = cols.Select(c => c == "vg_name" ? "VG" : c == "pv_count" ? "#PV" : c == "lv_count" ? "#LV" : c == "snap_count" ? "#SN" : c == "vg_attr" ? "Attr" : c == "vg_size" ? "VSize" : c == "vg_free" ? "VFree" : c == "vg_extent_size" ? "Ext" : c == "vg_extent_count" ? "#Ext" : c == "vg_free_count" ? "Free" : c == "vg_uuid" ? "VG UUID" : c).ToList();
            var rows = new List<string[]>();
            foreach (var vg in m.Vgs.OrderBy(v => v.Name, StringComparer.Ordinal))
            {
                if (o.Args.Count > 0 && !o.Args.Contains(vg.Name)) continue;
                long tot = m.VgExtents(vg), free = m.VgFree(vg);
                Func<string, string> sz = s => o.Has("nosuffix") ? s.TrimEnd('b', 'k', 'm', 'g', 't').TrimStart('<') : s;
                rows.Add(cols.Select(c =>
                {
                    switch (c)
                    {
                        case "vg_name": return vg.Name;
                        case "pv_count": return vg.Pvs.Count.ToString();
                        case "lv_count": return vg.Lvs.Count.ToString();
                        case "snap_count": return "0";
                        case "vg_attr": return "wz--n-";
                        case "vg_size": return sz(LvmSize(tot * vg.PeSize, u, false));
                        case "vg_free": return sz(LvmSize(free * vg.PeSize, u, false));
                        case "vg_extent_size": return sz(LvmSize(vg.PeSize, u, false));
                        case "vg_extent_count": return tot.ToString();
                        case "vg_free_count": return free.ToString();
                        case "vg_uuid": return vg.Uuid;
                    }
                    return "";
                }).ToArray());
            }
            if (rows.Count == 0 && o.Args.Count > 0) { foreach (var x in o.Args) io.E("  Volume group \"" + x + "\" not found\n  Cannot process volume group " + x); return 5; }
            LvmTable(io, o, heads, rows, new HashSet<int>(cols.Select((c, i) => c.Contains("count") || c.EndsWith("size", StringComparison.Ordinal) || c.EndsWith("free", StringComparison.Ordinal) || c == "vg_extent_size" ? i : -1).Where(i => i >= 0)));
            return 0;
        }

        static int Vgdisplay(Sh sh, List<string> a, Io io)
        {
            if (!LvmRoot(sh, io)) return 5;
            var m = sh.M;
            var o = Opts.Parse(a, "svC", "short|s,verbose|v,columns|C", "vgdisplay");
            if (o.Has("C")) return Vgs(sh, a.Where(x => x != "-C").ToList(), io);
            foreach (var vg in m.Vgs)
            {
                if (o.Args.Count > 0 && !o.Args.Contains(vg.Name)) continue;
                long tot = m.VgExtents(vg), free = m.VgFree(vg);
                int open = vg.Lvs.Count(l => LvOpen(m, vg, l));
                io.L("  --- Volume group ---\n  VG Name               " + vg.Name + "\n  System ID             \n  Format                lvm2\n  Metadata Areas        " + vg.Pvs.Count + "\n  Metadata Sequence No  " + vg.Seq + "\n  VG Access             read/write\n  VG Status             resizable\n  MAX LV                0\n  Cur LV                " + vg.Lvs.Count + "\n  Open LV               " + open + "\n  Max PV                0\n  Cur PV                " + vg.Pvs.Count + "\n  Act PV                " + vg.Pvs.Count + "\n  VG Size               " + LvmSize(tot * vg.PeSize, "h", true) + "\n  PE Size               " + LvmSize(vg.PeSize, "h", true) + "\n  Total PE              " + tot + "\n  Alloc PE / Size       " + (tot - free) + " / " + LvmSize((tot - free) * vg.PeSize, "h", true) + "\n  Free  PE / Size       " + free + " / " + LvmSize(free * vg.PeSize, "h", true) + "\n  VG UUID               " + vg.Uuid + "\n   ");
            }
            if (o.Args.Count > 0 && !m.Vgs.Any(v => o.Args.Contains(v.Name))) { io.E("  Volume group \"" + o.Args[0] + "\" not found\n  Cannot process volume group " + o.Args[0]); return 5; }
            return 0;
        }

        static int Vgextend(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "fyv", "force|f,yes|y", "vgextend");
            if (!LvmRoot(sh, io)) return 5;
            var m = sh.M;
            if (o.Args.Count < 2) { io.E("  Please enter volume group name and physical volume(s)"); return 3; }
            var vg = FindVg(m, o.Args[0]);
            if (vg == null) { io.E("  Volume group \"" + o.Args[0] + "\" not found\n  Cannot process volume group " + o.Args[0]); return 5; }
            bool failed;
            var keys = PrepPvs(sh, io, o.Args.Skip(1).ToList(), true, out failed);
            if (failed) return 5;
            vg.Pvs.AddRange(keys); vg.Seq++;
            io.L("  Volume group \"" + vg.Name + "\" successfully extended");
            return 0;
        }
        static int Vgreduce(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "afv", "all|a,removemissing,force|f", "vgreduce");
            if (!LvmRoot(sh, io)) return 5;
            var m = sh.M;
            if (o.Args.Count < 2) { io.E("  Please give volume group name and physical volume paths"); return 3; }
            var vg = FindVg(m, o.Args[0]);
            if (vg == null) { io.E("  Volume group \"" + o.Args[0] + "\" not found"); return 5; }
            foreach (var x in o.Args.Skip(1))
            {
                var d = m.FindDev(x);
                if (d == null || !vg.Pvs.Contains(d.Key)) { io.E("  Physical Volume \"" + x + "\" not found in Volume Group \"" + vg.Name + "\"."); return 5; }
                if (m.PvUsed(d.Key, vg) > 0) { io.E("  Physical volume \"" + d.Path + "\" still in use"); return 5; }
                if (vg.Pvs.Count == 1) { io.E("  Can't remove final physical volume \"" + d.Path + "\" from volume group \"" + vg.Name + "\""); return 5; }
                vg.Pvs.Remove(d.Key); vg.Seq++;
                io.L("  Removed \"" + d.Path + "\" from volume group \"" + vg.Name + "\"");
            }
            return 0;
        }
        static int Vgremove(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "fyv", "force|f,yes|y", "vgremove");
            if (!LvmRoot(sh, io)) return 5;
            var m = sh.M;
            int st = 0;
            foreach (var x in o.Args)
            {
                var vg = FindVg(m, x);
                if (vg == null) { io.E("  Volume group \"" + x + "\" not found\n  Cannot process volume group " + x); st = 5; continue; }
                if (vg.Lvs.Count > 0)
                {
                    if (!o.Has("f") && !Confirm(sh, io, "Do you really want to remove volume group \"" + vg.Name + "\" containing " + vg.Lvs.Count + " logical volumes? [y/n]: ")) { io.E("  Volume group \"" + vg.Name + "\" not removed"); st = 5; continue; }
                    foreach (var lv in vg.Lvs.ToList())
                    {
                        if (LvOpen(m, vg, lv)) { io.E("  Logical volume " + vg.Name + "/" + lv.Name + " contains a filesystem in use.\n  Volume group \"" + vg.Name + "\" not removed"); return 5; }
                        if (!o.Has("f") && !Confirm(sh, io, "Do you really want to remove active logical volume " + vg.Name + "/" + lv.Name + "? [y/n]: ")) { io.E("  Logical volume " + lv.Name + " not removed"); return 5; }
                        m.Content.Remove("lv:" + lv.Uuid);
                        vg.Lvs.Remove(lv);
                        io.L("  Logical volume \"" + lv.Name + "\" successfully removed.");
                    }
                }
                m.Vgs.Remove(vg);
                m.SyncDev();
                io.L("  Volume group \"" + vg.Name + "\" successfully removed");
            }
            return st;
        }
        static int Vgrename(Sh sh, List<string> a, Io io)
        {
            if (!LvmRoot(sh, io)) return 5;
            var m = sh.M;
            var args = a.Skip(1).Where(x => !x.StartsWith("-", StringComparison.Ordinal)).ToList();
            if (args.Count != 2) { io.E("  Old and new volume group names need specifying"); return 3; }
            var vg = FindVg(m, args[0]);
            if (vg == null) { io.E("  Volume group \"" + args[0] + "\" not found"); return 5; }
            if (FindVg(m, args[1]) != null) { io.E("  New volume group \"" + args[1] + "\" already exists"); return 5; }
            vg.Name = args[1].Replace("/dev/", "");
            m.SyncDev();
            io.L("  Volume group \"" + args[0] + "\" successfully renamed to \"" + vg.Name + "\"");
            return 0;
        }
        static int Vgchange(Sh sh, List<string> a, Io io)
        {
            if (!LvmRoot(sh, io)) return 5;
            var m = sh.M;
            var o = Opts.Parse(a, "a:", "activate=|a", "vgchange");
            bool on = o.Get("a", "y").StartsWith("y", StringComparison.Ordinal);
            foreach (var vg in m.Vgs.Where(v => o.Args.Count == 0 || o.Args.Contains(v.Name)))
            {
                if (!on && vg.Lvs.Any(l => LvOpen(m, vg, l))) { io.E("  Logical volume " + vg.Name + "/" + vg.Lvs.First(l => LvOpen(m, vg, l)).Name + " in use.\n  Can't deactivate volume group \"" + vg.Name + "\" with " + vg.Lvs.Count(l => LvOpen(m, vg, l)) + " open logical volume(s)"); return 5; }
                foreach (var lv in vg.Lvs) lv.Active = on;
                io.L("  " + (on ? vg.Lvs.Count : 0) + " logical volume(s) in volume group \"" + vg.Name + "\" now active");
            }
            m.SyncDev();
            return 0;
        }

        // ------------------------------------------------------------ LVs
        /// <summary>Extents from -L / -l (absolute or +relative); returns -1 and an error when invalid.</summary>
        static long WantExtents(Machine m, VolGroup vg, Lv lv, Opts o, out bool relative, out string err, out bool rounded)
        {
            err = null; relative = false; rounded = false;
            long cur = lv == null ? 0 : lv.Extents;
            if (o.Has("L"))
            {
                string s = o.Get("L");
                relative = s.StartsWith("+", StringComparison.Ordinal) || s.StartsWith("-", StringComparison.Ordinal);
                int sign = s.StartsWith("-", StringComparison.Ordinal) ? -1 : 1;
                long bytes = ParseSize(s.TrimStart('+', '-'), 1024L * 1024);
                if (bytes <= 0) { err = "  Invalid argument for --size: " + s; return -1; }
                long ext = (bytes + vg.PeSize - 1) / vg.PeSize;
                rounded = ext * vg.PeSize != bytes;
                return relative ? cur + sign * ext : ext;
            }
            if (o.Has("l"))
            {
                string s = o.Get("l");
                relative = s.StartsWith("+", StringComparison.Ordinal) || s.StartsWith("-", StringComparison.Ordinal);
                int sign = s.StartsWith("-", StringComparison.Ordinal) ? -1 : 1;
                string v = s.TrimStart('+', '-');
                var pm = Regex.Match(v, "^(\\d+)%(FREE|VG|PVS|ORIGIN)$", RegexOptions.IgnoreCase);
                long ext;
                if (pm.Success)
                {
                    long pct = long.Parse(pm.Groups[1].Value);
                    string what = pm.Groups[2].Value.ToUpperInvariant();
                    long basis = what == "FREE" ? m.VgFree(vg) : m.VgExtents(vg);
                    ext = basis * pct / 100;
                    if (what == "VG" && !relative) return ext;
                }
                else if (!long.TryParse(v, out ext)) { err = "  Invalid argument for --extents: " + s; return -1; }
                return relative ? cur + sign * ext : ext;
            }
            err = "  Please specify either size or extents but not both.";
            return -1;
        }

        static int Lvcreate(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "n:L:l:yWZavs", "name=|n,size=|L,extents=|l,yes|y,wipesignatures=|W,zero=|Z,type=,snapshot|s", "lvcreate");
            if (o.Error != null) return Fail(io, o);
            if (!LvmRoot(sh, io)) return 5;
            var m = sh.M;
            if (o.Args.Count == 0) { io.E("  Please specify a volume group to use."); return 3; }
            if (!o.Has("L") && !o.Has("l")) { io.E("  Please specify either size or extents."); return 3; }
            var vg = FindVg(m, o.Args[0]);
            if (vg == null) { io.E("  Volume group \"" + o.Args[0] + "\" not found\n  Cannot process volume group " + o.Args[0]); return 5; }
            string name = o.Get("n");
            if (name == null) { int k = 0; while (vg.Lvs.Any(l => l.Name == "lvol" + k)) k++; name = "lvol" + k; }
            if (!ValidLvmName(name) || name.StartsWith("snapshot", StringComparison.Ordinal) || name == "pvmove") { io.E("  Logical volume name \"" + name + "\" is invalid."); return 3; }
            if (vg.Lvs.Any(l => l.Name == name)) { io.E("  Logical Volume \"" + name + "\" already exists in volume group \"" + vg.Name + "\""); return 5; }
            bool rel, rounded; string err;
            long ext = WantExtents(m, vg, null, o, out rel, out err, out rounded);
            if (err != null) { io.E(err); return 3; }
            if (ext <= 0) { io.E("  Invalid argument for --size: " + (o.Get("L") ?? o.Get("l"))); return 3; }
            if (rounded) io.L("  Rounding up size to full physical extent " + LvmSize(ext * vg.PeSize, "h", true) + ".");
            long free = m.VgFree(vg);
            if (ext > free) { io.E("  Volume group \"" + vg.Name + "\" has insufficient free space (" + free + " extents): " + ext + " required."); return 5; }
            var lv = new Lv { Name = name, Uuid = m.PvUuid(), Dm = m.NextDmIndex() };
            var onlyPvs = o.Args.Skip(1).Select(x => m.FindDev(x)).Where(x => x != null).Select(x => x.Key).ToList();
            if (!m.Allocate(vg, lv, ext, onlyPvs)) { io.E("  Insufficient suitable allocatable extents for logical volume " + name + ": " + ext + " more required"); return 5; }
            vg.Lvs.Add(lv); vg.Seq++;
            m.SyncDev();
            io.L("  Logical volume \"" + name + "\" created.");
            return 0;
        }

        static int Lvs(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "ao:vO:S:", "all|a,options=|o,units=,noheadings,nosuffix,separator=,sort=|O,select=|S,segments", "lvs");
            if (o.Error != null) return Fail(io, o);
            if (!LvmRoot(sh, io)) return 5;
            var m = sh.M;
            string u = Units(o);
            var cols = new List<string> { "lv_name", "vg_name", "lv_attr", "lv_size", "pool_lv", "origin", "data_percent", "metadata_percent", "move_pv", "mirror_log", "copy_percent", "convert_lv" };
            var opt = o.Get("o");
            if (opt != null) { if (opt.StartsWith("+", StringComparison.Ordinal)) cols.AddRange(opt.Substring(1).Split(',')); else cols = opt.Split(',').ToList(); }
            var heads = cols.Select(c => c == "lv_name" ? "LV" : c == "vg_name" ? "VG" : c == "lv_attr" ? "Attr" : c == "lv_size" ? "LSize" : c == "pool_lv" ? "Pool" : c == "origin" ? "Origin" : c == "data_percent" ? "Data%" : c == "metadata_percent" ? "Meta%" : c == "move_pv" ? "Move" : c == "mirror_log" ? "Log" : c == "copy_percent" ? "Cpy%Sync" : c == "convert_lv" ? "Convert" : c == "devices" ? "Devices" : c == "lv_path" ? "Path" : c == "lv_uuid" ? "LV UUID" : c == "seg_count" ? "#Seg" : c == "lv_dm_path" ? "DMPath" : c).ToList();
            var rows = new List<string[]>();
            foreach (var vg in m.Vgs.OrderBy(v => v.Name, StringComparer.Ordinal))
                foreach (var lv in vg.Lvs.OrderBy(l => l.Name, StringComparer.Ordinal))
                {
                    if (o.Args.Count > 0 && !o.Args.Contains(vg.Name) && !o.Args.Contains(vg.Name + "/" + lv.Name) && !o.Args.Contains("/dev/" + vg.Name + "/" + lv.Name)) continue;
                    bool open = LvOpen(m, vg, lv);
                    rows.Add(cols.Select(c =>
                    {
                        switch (c)
                        {
                            case "lv_name": return lv.Name;
                            case "vg_name": return vg.Name;
                            case "lv_attr": return "-wi-" + (lv.Active ? "a" : "-") + (open ? "o" : "-") + "----";
                            case "lv_size": { var s = LvmSize(lv.Extents * vg.PeSize, u, false); return o.Has("nosuffix") ? s.TrimEnd('b', 'k', 'm', 'g', 't').TrimStart('<') : s; }
                            case "devices": return string.Join(",", lv.Segs.Select(s => m.DevPathOfKey(s.Pv) + "(" + s.Start + ")").ToArray());
                            case "lv_path": return "/dev/" + vg.Name + "/" + lv.Name;
                            case "lv_dm_path": return "/dev/mapper/" + Machine.DmName(vg.Name, lv.Name);
                            case "lv_uuid": return lv.Uuid;
                            case "seg_count": return lv.Segs.Count.ToString();
                            case "seg_size_pe": return lv.Extents.ToString();
                            case "lv_active": return lv.Active ? "active" : "";
                            case "lv_dm_name": return Machine.DmName(vg.Name, lv.Name);
                        }
                        return "";
                    }).ToArray());
                }
            if (rows.Count == 0 && o.Args.Count > 0) { io.E("  Failed to find logical volume \"" + o.Args[0] + "\""); return 5; }
            LvmTable(io, o, heads, rows, new HashSet<int>(cols.Select((c, i) => c == "lv_size" ? i : -1).Where(i => i >= 0)));
            return 0;
        }

        static int Lvdisplay(Sh sh, List<string> a, Io io)
        {
            if (!LvmRoot(sh, io)) return 5;
            var m = sh.M;
            var o = Opts.Parse(a, "mvC", "maps|m,columns|C", "lvdisplay");
            if (o.Has("C")) return Lvs(sh, a.Where(x => x != "-C").ToList(), io);
            bool any = false;
            foreach (var vg in m.Vgs)
                foreach (var lv in vg.Lvs)
                {
                    if (o.Args.Count > 0 && !o.Args.Contains(vg.Name) && !o.Args.Contains(vg.Name + "/" + lv.Name) && !o.Args.Contains("/dev/" + vg.Name + "/" + lv.Name) && !o.Args.Contains("/dev/mapper/" + Machine.DmName(vg.Name, lv.Name))) continue;
                    any = true;
                    bool open = LvOpen(m, vg, lv);
                    io.L("  --- Logical volume ---\n  LV Path                /dev/" + vg.Name + "/" + lv.Name + "\n  LV Name                " + lv.Name + "\n  VG Name                " + vg.Name + "\n  LV UUID                " + lv.Uuid + "\n  LV Write Access        read/write\n  LV Creation host, time " + m.HostnameNow + ", " + DateFormat(m, m.BootTime, "%Y-%m-%d %H:%M:%S %z", false) + "\n  LV Status              " + (lv.Active ? "available" : "NOT available") + "\n  # open                 " + (open ? 1 : 0) + "\n  LV Size                " + LvmSize(lv.Extents * vg.PeSize, "h", true) + "\n  Current LE             " + lv.Extents + "\n  Segments               " + lv.Segs.Count + "\n  Allocation             inherit\n  Read ahead sectors     auto\n  - currently set to     256\n  Block device           253:" + lv.Dm + "\n   ");
                    if (o.Has("m")) { io.L("  --- Segments ---"); long le = 0; foreach (var s in lv.Segs) { io.L("  Logical extents " + le + " to " + (le + s.Count - 1) + ":\n    Type\t\tlinear\n    Physical volume\t" + m.DevPathOfKey(s.Pv) + "\n    Physical extents\t" + s.Start + " to " + (s.Start + s.Count - 1) + "\n   "); le += s.Count; } }
                }
            if (!any && o.Args.Count > 0) { io.E("  Failed to find logical volume \"" + o.Args[0] + "\""); return 5; }
            return 0;
        }

        static int Lvresize(Sh sh, List<string> a, Io io, string mode)
        {
            string prog = "lv" + mode;
            var o = Opts.Parse(a, "L:l:rfyntv", "size=|L,extents=|l,resizefs|r,force|f,yes|y,nofsck|n,test|t", prog);
            if (o.Error != null) return Fail(io, o);
            if (!LvmRoot(sh, io)) return 5;
            var m = sh.M;
            if (o.Args.Count == 0) { io.E("  Please provide the logical volume name"); return 3; }
            if (!o.Has("L") && !o.Has("l")) { io.E("  Please specify either size or extents."); return 3; }
            VolGroup vg;
            var lv = FindLv(m, o.Args[0], out vg);
            if (lv == null) { io.E("  Failed to find logical volume \"" + o.Args[0].Replace("/dev/", "") + "\""); return 5; }
            bool rel, rounded; string err;
            long want = WantExtents(m, vg, lv, o, out rel, out err, out rounded);
            if (err != null) { io.E(err); return 3; }
            long cur = lv.Extents;
            string full = vg.Name + "/" + lv.Name;
            if (rounded) io.L("  Rounding size to boundary between physical extents: " + LvmSize(want * vg.PeSize, "h", true) + ".");
            if (want == cur) { io.E("  New size (" + want + " extents) matches existing size (" + cur + " extents)."); return 5; }
            if (mode == "extend" && want < cur) { io.E("  New size given (" + want + " extents) not larger than existing size (" + cur + " extents)"); return 5; }
            if (mode == "reduce" && want > cur) { io.E("  New size given (" + want + " extents) not less than existing size (" + cur + " extents)"); return 5; }
            if (want <= 0) { io.E("  Cannot reduce logical volume " + full + " below 1 extent."); return 5; }
            var dev = m.Devices().First(x => x.Lv == lv);
            var c = m.ContentOf(dev);
            bool resizeFs = o.Has("r");
            if (want < cur)
            {
                // shrinking
                if (c != null && c.FsId != null)
                {
                    if (c.Kind == "xfs") { io.E(resizeFs ? "  xfs filesystems do not support shrinking.\n  File system reduce is required and not supported (xfs)." : "  File system xfs found on " + full + ".\n  File system size (" + LvmSize(m.V.Fs(c.FsId).Bytes, "h", true) + ") is larger than the requested size (" + LvmSize(want * vg.PeSize, "h", true) + ").\n  File system reduce is required and not supported (xfs)."); return 5; }
                    var fs = m.V.Fs(c.FsId);
                    if (!resizeFs) { io.E("  File system ext4 found on " + full + ".\n  File system size (" + LvmSize(fs.Bytes, "h", true) + ") is larger than the requested size (" + LvmSize(want * vg.PeSize, "h", true) + ").\n  File system reduce is required using resize2fs.\n  File system reduce is required (see resize2fs or --resizefs.)"); return 5; }
                    if (want * vg.PeSize < fs.Used) { io.E("  resize2fs: New size smaller than minimum"); return 5; }
                    if (m.InUse(dev) != null)
                    {
                        // fsadm unmounts, checks and shrinks
                        if (!o.Has("y") && !Confirm(sh, io, "Do you want to unmount \"" + m.Mounts.First(x => x.FsId == c.FsId).Path + "\" ? [Y|n] ")) return 5;
                    }
                    io.L("  File system ext4 found on " + full + (m.InUse(dev) != null ? " mounted at " + m.Mounts.First(x => x.FsId == c.FsId).Path : "") + ".\n  File system size (" + LvmSize(fs.Bytes, "h", true) + ") is larger than the requested size (" + LvmSize(want * vg.PeSize, "h", true) + ").\n  File system reduce is required using resize2fs.\n  File system fsck will be run before reduce.\n  Reducing file system ext4 to " + LvmSize(want * vg.PeSize, "h", true) + " (" + want * vg.PeSize + " bytes) on " + full + "...");
                    fs.Bytes = (long)(want * vg.PeSize * 0.952);
                }
                else if (!o.Has("f") && !o.Has("y"))
                {
                    if (!Confirm(sh, io, "  WARNING: Reducing active" + (LvOpen(m, vg, lv) ? " and open" : "") + " logical volume to " + LvmSize(want * vg.PeSize, "h", true) + ".\n  THIS MAY DESTROY YOUR DATA (filesystem etc.)\nDo you really want to reduce " + full + "? [y/n]: ")) { io.E("  Logical volume " + full + " NOT reduced."); return 5; }
                }
                m.Shrink(lv, want);
                vg.Seq++;
                io.L("  Size of logical volume " + full + " changed from " + LvmSize(cur * vg.PeSize, "h", true) + " (" + cur + " extents) to " + LvmSize(want * vg.PeSize, "h", true) + " (" + want + " extents).\n  Logical volume " + full + " successfully resized.");
                return 0;
            }
            long need = want - cur, free = m.VgFree(vg);
            if (need > free) { io.E("  Insufficient free space: " + need + " extents needed, but only " + free + " available"); return 5; }
            if (!m.Allocate(vg, lv, need, o.Args.Skip(1).Select(x => m.FindDev(x)).Where(x => x != null).Select(x => x.Key).ToList())) { io.E("  Insufficient suitable allocatable extents for logical volume " + lv.Name + ": " + need + " more required"); return 5; }
            vg.Seq++;
            if (resizeFs && c != null && c.FsId != null) io.L("  File system " + c.Kind + " found on " + full + (m.InUse(dev) != null ? " mounted at " + m.Mounts.First(x => x.FsId == c.FsId).Path : "") + ".\n  Extending file system " + c.Kind + " to " + LvmSize(want * vg.PeSize, "h", true) + " (" + want * vg.PeSize + " bytes) on " + full + "...");
            io.L("  Size of logical volume " + full + " changed from " + LvmSize(cur * vg.PeSize, "h", true) + " (" + cur + " extents) to " + LvmSize(want * vg.PeSize, "h", true) + " (" + want + " extents).");
            if (resizeFs && c != null && c.FsId != null)
            {
                var d2 = m.Devices().First(x => x.Lv == lv);
                var fs = m.V.Fs(c.FsId);
                long before = fs.Bytes / 4096;
                if (c.Kind == "xfs" && m.InUse(d2) == null) { io.E("  xfs_growfs: " + d2.Path + " is not a mounted XFS filesystem\n  Failed to extend file system xfs on " + full + "."); return 5; }
                m.GrowFs(d2);
                if (c.Kind == "xfs") { XfsInfoOut(io, "", before, "/dev/mapper/" + d2.Name); io.L("data blocks changed from " + (before + before / 50) + " to " + (fs.Bytes / 4096 + fs.Bytes / 4096 / 50) + "\n  Extended file system xfs on " + full + "."); }
                else io.L("resize2fs 1.46.5 (30-Dec-2021)\nFilesystem at /dev/mapper/" + d2.Name + " is mounted on " + (m.Mounts.FirstOrDefault(x => x.FsId == c.FsId) == null ? "" : m.Mounts.First(x => x.FsId == c.FsId).Path) + "; on-line resizing required\nold_desc_blocks = 1, new_desc_blocks = 1\nThe filesystem on /dev/mapper/" + d2.Name + " is now " + d2.Bytes / 4096 + " (4k) blocks long.\n\n  Extended file system ext4 on " + full + ".");
            }
            io.L("  Logical volume " + full + " successfully resized.");
            return 0;
        }

        static int Lvremove(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "fyv", "force|f,yes|y", "lvremove");
            if (!LvmRoot(sh, io)) return 5;
            var m = sh.M;
            int st = 0;
            foreach (var x in o.Args)
            {
                VolGroup vg;
                var lv = FindLv(m, x, out vg);
                var targets = new List<Lv>();
                if (lv != null) targets.Add(lv);
                else { vg = FindVg(m, x); if (vg != null) targets.AddRange(vg.Lvs); else { io.E("  Failed to find logical volume \"" + x.Replace("/dev/", "") + "\""); st = 5; continue; } }
                foreach (var t in targets.ToList())
                {
                    if (LvOpen(m, vg, t)) { io.E("  Logical volume " + vg.Name + "/" + t.Name + " contains a filesystem in use."); st = 5; continue; }
                    if (!o.Has("f") && !o.Has("y") && t.Active && !Confirm(sh, io, "Do you really want to remove active logical volume " + vg.Name + "/" + t.Name + "? [y/n]: ")) { io.E("  Logical volume " + t.Name + " not removed"); st = 5; continue; }
                    m.Content.Remove("lv:" + t.Uuid);
                    vg.Lvs.Remove(t); vg.Seq++;
                    io.L("  Logical volume \"" + t.Name + "\" successfully removed.");
                }
            }
            m.SyncDev();
            return st;
        }
        static int Lvrename(Sh sh, List<string> a, Io io)
        {
            if (!LvmRoot(sh, io)) return 5;
            var m = sh.M;
            var args = a.Skip(1).Where(x => !x.StartsWith("-", StringComparison.Ordinal)).ToList();
            VolGroup vg; Lv lv; string nn;
            if (args.Count == 3) { vg = FindVg(m, args[0]); lv = vg == null ? null : vg.Lvs.FirstOrDefault(l => l.Name == args[1]); nn = args[2]; }
            else if (args.Count == 2) { lv = FindLv(m, args[0], out vg); nn = Vfs.Base(args[1]); }
            else { io.E("  Old and new logical volume names required"); return 3; }
            if (lv == null) { io.E("  Existing logical volume \"" + args[args.Count - 2] + "\" not found in volume group"); return 5; }
            if (vg.Lvs.Any(l => l.Name == nn)) { io.E("  Logical volume \"" + nn + "\" already exists in volume group \"" + vg.Name + "\""); return 5; }
            string old = lv.Name;
            lv.Name = nn;
            // a mounted LV keeps working; the name in the mount table follows
            foreach (var mt in m.Mounts) if (mt.Source == "/dev/mapper/" + Machine.DmName(vg.Name, old)) mt.Source = "/dev/mapper/" + Machine.DmName(vg.Name, nn);
            m.SyncDev();
            io.L("  Renamed \"" + old + "\" to \"" + nn + "\" in volume group \"" + vg.Name + "\"");
            return 0;
        }
        static int Lvchange(Sh sh, List<string> a, Io io)
        {
            if (!LvmRoot(sh, io)) return 5;
            var m = sh.M;
            var o = Opts.Parse(a, "a:p:", "activate=|a,permission=|p", "lvchange");
            foreach (var x in o.Args)
            {
                VolGroup vg; var lv = FindLv(m, x, out vg);
                if (lv == null) { io.E("  Failed to find logical volume \"" + x + "\""); return 5; }
                if (o.Has("a"))
                {
                    bool on = o.Get("a").StartsWith("y", StringComparison.Ordinal);
                    if (!on && LvOpen(m, vg, lv)) { io.E("  Logical volume " + vg.Name + "/" + lv.Name + " in use."); return 5; }
                    lv.Active = on;
                }
            }
            m.SyncDev();
            return 0;
        }
    }

    public partial class Machine
    {
        public int NextDmIndex()
        {
            var used = Vgs.SelectMany(v => v.Lvs).Select(l => l.Dm).ToList();
            int i = 0; while (used.Contains(i)) i++;
            return i;
        }
    }
}
