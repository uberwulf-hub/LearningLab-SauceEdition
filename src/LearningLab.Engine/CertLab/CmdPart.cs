// Cert Lab - partitioning: fdisk (interactive, MBR and GPT, and -l), parted (commands and interactive) and gdisk.
// Changes are made on a copy of the partition table and only written with "w" (fdisk, gdisk) or at once (parted),
// like the real tools.
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
        static void RegPart() { }

        static readonly string[][] DosTypes =
        {
            new[] { "0", "Empty" }, new[] { "5", "Extended" }, new[] { "7", "HPFS/NTFS/exFAT" }, new[] { "b", "W95 FAT32" }, new[] { "c", "W95 FAT32 (LBA)" },
            new[] { "e", "W95 FAT16 (LBA)" }, new[] { "f", "W95 Ext'd (LBA)" }, new[] { "82", "Linux swap / Solaris" }, new[] { "83", "Linux" }, new[] { "8e", "Linux LVM" },
            new[] { "fd", "Linux raid autodetect" }, new[] { "ef", "EFI (FAT-12/16/32)" },
        };
        static readonly string[][] GptTypes =
        {
            new[] { "uefi", "EFI System", "C12A7328-F81F-11D2-BA4B-00A0C93EC93B", "ef00", "1" },
            new[] { "bios", "BIOS boot", "21686148-6449-6E6F-744E-656564454649", "ef02", "4" },
            new[] { "msdata", "Microsoft basic data", "EBD0A0A2-B9E5-4433-87C0-68B6B72699C7", "0700", "11" },
            new[] { "swap", "Linux swap", "0657FD6D-A4AB-43C4-84E5-0933C84B4F4F", "8200", "19" },
            new[] { "linux", "Linux filesystem", "0FC63DAF-8483-4772-8E79-3D69D8477DE4", "8300", "20" },
            new[] { "home", "Linux home", "933AC7E1-2EB4-4F13-B844-0E14E2AEF915", "8302", "" },
            new[] { "raid", "Linux RAID", "A19D880F-05FC-4D3B-A006-743F0F84911E", "fd00", "" },
            new[] { "lvm", "Linux LVM", "E6D6D379-F507-44C2-A23C-238F2A3DF928", "8e00", "" },
        };
        static string TypeName(Disk d, string t)
        {
            if (d.Label == "gpt") { var g = GptTypes.FirstOrDefault(x => x[0] == t); return g == null ? "Linux filesystem" : g[1]; }
            var e = DosTypes.FirstOrDefault(x => x[0] == t); return e == null ? "Unknown" : e[1];
        }
        static string DosAlias(string s)
        {
            switch (s.ToLowerInvariant()) { case "linux": return "83"; case "swap": return "82"; case "lvm": return "8e"; case "raid": return "fd"; case "extended": return "5"; case "uefi": return "ef"; }
            var h = s.ToLowerInvariant().TrimStart('0');
            if (h.StartsWith("x", StringComparison.Ordinal)) h = h.Substring(1);
            if (h.Length == 0) h = "0";
            return DosTypes.Any(x => x[0] == h) ? h : null;
        }
        static string GptAlias(string s)
        {
            var l = s.ToLowerInvariant();
            foreach (var g in GptTypes) if (g[0] == l || g[2].ToLowerInvariant() == l || (g[4].Length > 0 && g[4] == l) || g[3] == l) return g[0];
            return null;
        }

        static string FdiskSize(long sectors)
        {
            double b = sectors * 512.0; string[] u = { "B", "K", "M", "G", "T" }; int i = 0;
            while (b >= 1024 && i < u.Length - 1) { b /= 1024; i++; }
            return (Math.Round(b * 10) / 10).ToString("0.#", CultureInfo.InvariantCulture) + u[i];
        }
        static string FdiskSizeLong(long sectors) { var s = FdiskSize(sectors); return s.Substring(0, s.Length - 1) + " " + s.Substring(s.Length - 1) + "iB"; }

        static Disk CopyDisk(Disk d)
        {
            var c = new Disk { Name = d.Name, Bytes = d.Bytes, Label = d.Label, DiskId = d.DiskId, Index = d.Index };
            foreach (var p in d.Parts) c.Parts.Add(new Part { Num = p.Num, Start = p.Start, End = p.End, Type = p.Type, Name = p.Name, PartUuid = p.PartUuid, Boot = p.Boot });
            return c;
        }

        static string FdiskTable(Machine m, Disk d)
        {
            var sb = new StringBuilder();
            sb.Append("Disk /dev/" + d.Name + ": " + FdiskSizeLong(d.Sectors).Replace(".0 ", " ") + ", " + d.Bytes + " bytes, " + d.Sectors + " sectors\n");
            sb.Append("Units: sectors of 1 * 512 = 512 bytes\nSector size (logical/physical): 512 bytes / 512 bytes\nI/O size (minimum/optimal): 512 bytes / 512 bytes\n");
            if (d.Label.Length == 0) return sb.ToString();
            sb.Append("Disklabel type: " + d.Label + "\nDisk identifier: " + d.DiskId + "\n");
            if (d.Parts.Count == 0) return sb.ToString();
            sb.Append("\n");
            var rows = new List<string[]>();
            if (d.Label == "gpt")
            {
                rows.Add(new[] { "Device", "Start", "End", "Sectors", "Size", "Type" });
                foreach (var p in d.Parts.OrderBy(x => x.Num)) rows.Add(new[] { "/dev/" + d.Name + p.Num, p.Start.ToString(), p.End.ToString(), (p.End - p.Start + 1).ToString(), FdiskSize(p.End - p.Start + 1), TypeName(d, p.Type) });
            }
            else
            {
                rows.Add(new[] { "Device", "Boot", "Start", "End", "Sectors", "Size", "Id", "Type" });
                foreach (var p in d.Parts.OrderBy(x => x.Num)) rows.Add(new[] { "/dev/" + d.Name + p.Num, p.Boot ? "*" : "", p.Start.ToString(), p.End.ToString(), (p.End - p.Start + 1).ToString(), FdiskSize(p.End - p.Start + 1), p.Type, TypeName(d, p.Type) });
            }
            int cols = rows[0].Length;
            var w = new int[cols];
            foreach (var r in rows) for (int i = 0; i < cols; i++) w[i] = Math.Max(w[i], r[i].Length);
            foreach (var r in rows)
            {
                var line = new StringBuilder();
                for (int i = 0; i < cols; i++)
                {
                    bool right = i > 0 && i < cols - 1 && !(d.Label != "gpt" && i == 1) && !(d.Label != "gpt" && i == 6);
                    if (i == cols - 1) line.Append(r[i]);
                    else line.Append(right ? r[i].PadLeft(w[i]) : r[i].PadRight(w[i])).Append(' ');
                }
                sb.Append(line.ToString().TrimEnd()).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>Free ranges [start, end] for new partitions (primary area, or inside the extended one).</summary>
        static List<long[]> FreeRanges(Disk d, bool logical)
        {
            long first = 2048, last = d.Label == "gpt" ? d.Sectors - 34 : d.Sectors - 1;
            var used = new List<long[]>();
            var ext = d.Parts.FirstOrDefault(p => p.Type == "5" || p.Type == "f");
            if (logical && ext != null)
            {
                first = ext.Start + 2048; last = ext.End;
                foreach (var p in d.Parts.Where(p => p.Num >= 5)) used.Add(new[] { p.Start - 2048, p.End });
            }
            else foreach (var p in d.Parts.Where(p => p.Num <= 4 || d.Label == "gpt")) used.Add(new[] { p.Start, p.End });
            used = used.OrderBy(u => u[0]).ToList();
            var free = new List<long[]>();
            long pos = first;
            foreach (var u in used)
            {
                if (u[0] > pos && u[0] - pos >= 2048) free.Add(new[] { pos, u[0] - 1 });
                pos = Math.Max(pos, (u[1] + 2048) / 2048 * 2048);
            }
            if (last - pos >= 2047) free.Add(new[] { pos, last });
            return free;
        }

        static long ParseSector(string s, long start, long def, long min, long max, out string err)
        {
            err = null;
            s = s.Trim();
            if (s.Length == 0) return def;
            var mm = Regex.Match(s, "^([+-]?)(\\d+(\\.\\d+)?)([KMGTP]?)(i?B?)$", RegexOptions.IgnoreCase);
            if (!mm.Success) { err = "Value out of range."; return -1; }
            string sign = mm.Groups[1].Value, unit = mm.Groups[4].Value.ToUpperInvariant();
            double n = double.Parse(mm.Groups[2].Value, CultureInfo.InvariantCulture);
            long sectors;
            if (unit.Length == 0) sectors = (long)n;
            else
            {
                double mul = unit == "K" ? 1024 : unit == "M" ? 1024 * 1024 : unit == "G" ? 1024.0 * 1024 * 1024 : unit == "T" ? 1024.0 * 1024 * 1024 * 1024 : 1024.0 * 1024 * 1024 * 1024 * 1024;
                sectors = (long)(n * mul / 512);
            }
            long v;
            if (sign == "+") v = start + sectors - 1;
            else if (sign == "-") v = max - sectors;
            else v = sectors;
            if (v < min || v > max) { err = "Value out of range."; return -1; }
            return v;
        }

        // ------------------------------------------------------------ fdisk
        static int Fdisk(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "lt:wbu:cs:", "list|l,type=|t,wipe=,sector-size=,units=?|u,compatibility=?|c", "fdisk");
            if (o.Error != null) return Fail(io, o);
            var m = sh.M;
            if (o.Has("l"))
            {
                if (sh.P.Cr.Uid != 0) { foreach (var d in o.Args) io.E("fdisk: cannot open " + d + ": Permission denied"); return 1; }
                var disks = o.Args.Count > 0 ? o.Args.Select(x => m.FindDev(x)).Where(x => x != null && x.Type == "disk").Select(x => x.Disk).ToList() : m.Disks.Where(x => !x.Name.StartsWith("sr", StringComparison.Ordinal)).ToList();
                bool first = true;
                foreach (var d in disks) { if (!first) io.L(""); first = false; io.P(FdiskTable(m, d)); }
                if (o.Args.Count == 0)
                    foreach (var lv in m.Devices().Where(x => x.Type == "lvm"))
                        io.L("\n\nDisk " + lv.Path + ": " + FdiskSizeLong(lv.Bytes / 512) + ", " + lv.Bytes + " bytes, " + lv.Bytes / 512 + " sectors\nUnits: sectors of 1 * 512 = 512 bytes\nSector size (logical/physical): 512 bytes / 512 bytes\nI/O size (minimum/optimal): 512 bytes / 512 bytes");
                return 0;
            }
            if (o.Args.Count == 0) { io.E("Usage:\n fdisk [options] <disk>         change partition table\n fdisk [options] -l [<disk>...] list partition table(s)\n\nDisplay or manipulate a disk partition table."); return 1; }
            if (sh.P.Cr.Uid != 0) { io.E("fdisk: cannot open " + o.Args[0] + ": Permission denied"); return 1; }
            var dev = m.FindDev(o.Args[0]);
            if (dev == null) { io.E("fdisk: cannot open " + o.Args[0] + ": No such file or directory"); return 1; }
            if (dev.Type != "disk") { io.E(dev.Type == "rom" ? "fdisk: cannot open " + dev.Path + ": Read-only file system" : "Welcome to fdisk (util-linux 2.37.4).\n\nfdisk: " + dev.Path + " is a partition or volume, not a disk; use fdisk on the whole disk (for example /dev/" + Regex.Replace(dev.Name, "\\d+$", "") + ")"); return 1; }
            var disk = CopyDisk(dev.Disk);
            io.L("\nWelcome to fdisk (util-linux 2.37.4).\nChanges will remain in memory only, until you decide to write them.\nBe careful before using the write command.\n");
            if (disk.Label.Length == 0)
            {
                var c = m.ContentOf(dev);
                if (c != null) io.L("The device contains '" + c.Kind + "' signature and it will be removed by a write command. See fdisk(8) man page and --wipe option for more details.\n");
                disk.Label = "dos"; disk.DiskId = "0x" + m.NewHex(8);
                io.L("Device does not contain a recognized partition table.\nCreated a new DOS disklabel with disk identifier " + disk.DiskId + ".\n");
            }
            var removeSig = new HashSet<long>();
            bool wipeWhole = disk.Label.Length > 0 && m.ContentOf(dev) != null;
            if (sh.S == null) return 1;
            for (;;)
            {
                string cmd = sh.S.ReadLine("Command (m for help): ", false);
                if (cmd == null) { io.L("\n"); return 0; }
                cmd = cmd.Trim();
                if (cmd.Length == 0) continue;
                switch (cmd)
                {
                    case "m":
                        io.L("\nHelp:\n\n  DOS (MBR)\n   a   toggle a bootable flag\n   b   edit nested BSD disklabel\n   c   toggle the dos compatibility flag\n\n  Generic\n   d   delete a partition\n   F   list free unpartitioned space\n   l   list known partition types\n   n   add a new partition\n   p   print the partition table\n   t   change a partition type\n   v   verify the partition table\n   i   print information about a partition\n\n  Misc\n   m   print this menu\n   u   change display/entry units\n   x   extra functionality (experts only)\n\n  Script\n   I   load disk layout from sfdisk script file\n   O   dump disk layout to sfdisk script file\n\n  Save & Exit\n   w   write table to disk and exit\n   q   quit without saving changes\n\n  Create a new label\n   g   create a new empty GPT partition table\n   G   create a new empty SGI (IRIX) partition table\n   o   create a new empty DOS partition table\n   s   create a new empty Sun partition table\n");
                        break;
                    case "p": io.L(""); io.P(FdiskTable(m, disk)); io.L(""); break;
                    case "l":
                        if (disk.Label == "gpt") { foreach (var g in GptTypes) io.L((g[4].Length > 0 ? g[4].PadLeft(3) + " " : "    ") + g[1].PadRight(28) + g[2]); io.L("\nAliases:\n" + string.Join("\n", GptTypes.Select(g => "   " + g[0].PadRight(15) + "- " + g[2]).ToArray())); }
                        else { foreach (var t in DosTypes) io.L(t[0].PadLeft(2) + "  " + t[1]); io.L("\nAliases:\n   linux          - 83\n   swap           - 82\n   extended       - 05\n   uefi           - EF\n   raid           - FD\n   lvm            - 8E\n   linuxex        - 85"); }
                        break;
                    case "o": disk.Label = "dos"; disk.Parts.Clear(); disk.DiskId = "0x" + m.NewHex(8); io.L("Created a new DOS disklabel with disk identifier " + disk.DiskId + ".\n"); break;
                    case "g": disk.Label = "gpt"; disk.Parts.Clear(); disk.DiskId = m.NewUuid().ToUpperInvariant(); io.L("Created a new GPT disklabel (GUID: " + disk.DiskId + ").\n"); break;
                    case "q": io.L(""); return 0;
                    case "w":
                        {
                            var real = dev.Disk;
                            bool busy = real.Parts.Any(p => { var bd = m.Devices().FirstOrDefault(x => x.Part == p); return bd != null && (m.InUse(bd) != null || (m.ContentOf(bd) != null && m.ContentOf(bd).Kind == "LVM2_member" && m.VgOfPv(bd.Key) != null)); });
                            // the old partitions that went away lose what was on them only if the new table does not cover them again
                            if (wipeWhole) m.SetContent(m.Devices().First(x => x.Key == dev.Key), null);
                            foreach (var s in removeSig) m.Content.Remove(real.Name + "@" + s);
                            if (busy)
                            {
                                // the kernel keeps the old table for partitions in use; Cert Lab applies the new one but warns
                                io.L("The partition table has been altered.\nCalling ioctl() to re-read partition table.\nRe-reading the partition table failed.: Device or resource busy\n\nThe kernel still uses the old table. The new table will be used at the next reboot or after you run partprobe(8) or partx(8).\n");
                            }
                            else io.L("The partition table has been altered.\nCalling ioctl() to re-read partition table.\nSyncing disks.\n");
                            real.Label = disk.Label; real.DiskId = disk.DiskId; real.Parts = disk.Parts;
                            foreach (var p in real.Parts) if (p.PartUuid.Length == 0) p.PartUuid = real.Label == "gpt" ? m.NewUuid() : real.DiskId.Substring(2) + "-" + p.Num.ToString("00");
                            m.SyncDev();
                            return 0;
                        }
                    case "n": FdiskNew(sh, io, m, dev, disk, removeSig); break;
                    case "d":
                        {
                            if (disk.Parts.Count == 0) { io.L("The partition table is empty."); break; }
                            int n;
                            if (disk.Parts.Count == 1) { n = disk.Parts[0].Num; io.L("Selected partition " + n); }
                            else
                            {
                                var nums = disk.Parts.Select(x => x.Num).OrderBy(x => x).ToList();
                                string ans = sh.S.ReadLine("Partition number (" + string.Join(",", nums.Select(x => x.ToString()).ToArray()) + ", default " + nums.Last() + "): ", false);
                                if (ans == null) break;
                                if (ans.Trim().Length == 0) n = nums.Last(); else if (!int.TryParse(ans.Trim(), out n) || !nums.Contains(n)) { io.L("Value out of range."); break; }
                            }
                            var p = disk.Parts.First(x => x.Num == n);
                            disk.Parts.Remove(p);
                            if (p.Type == "5" || p.Type == "f") disk.Parts.RemoveAll(x => x.Num >= 5);
                            else if (n >= 5) foreach (var x in disk.Parts.Where(x => x.Num > n)) x.Num--;
                            io.L("Partition " + n + " has been deleted.\n");
                            break;
                        }
                    case "t":
                        {
                            if (disk.Parts.Count == 0) { io.L("No partition is defined yet!"); break; }
                            int n;
                            if (disk.Parts.Count == 1) { n = disk.Parts[0].Num; io.L("Selected partition " + n); }
                            else
                            {
                                var nums = disk.Parts.Select(x => x.Num).OrderBy(x => x).ToList();
                                string ans = sh.S.ReadLine("Partition number (" + string.Join(",", nums.Select(x => x.ToString()).ToArray()) + ", default " + nums.Last() + "): ", false);
                                if (ans == null) break;
                                if (ans.Trim().Length == 0) n = nums.Last(); else if (!int.TryParse(ans.Trim(), out n) || !nums.Contains(n)) { io.L("Value out of range."); break; }
                            }
                            var p = disk.Parts.First(x => x.Num == n);
                            string old = TypeName(disk, p.Type);
                            string code;
                            for (;;)
                            {
                                string ans = sh.S.ReadLine(disk.Label == "gpt" ? "Partition type or alias (type L to list all): " : "Hex code or alias (type L to list all): ", false);
                                if (ans == null) { code = null; break; }
                                ans = ans.Trim();
                                if (ans == "L" || ans == "l") { foreach (var g in disk.Label == "gpt" ? GptTypes.Select(x => (x[4].Length > 0 ? x[4].PadLeft(3) + " " : "    ") + x[1]) : DosTypes.Select(x => x[0].PadLeft(2) + "  " + x[1])) io.L(g); continue; }
                                code = disk.Label == "gpt" ? GptAlias(ans) : DosAlias(ans);
                                if (code == null) { io.L("Failed to parse '" + ans + "' partition type."); continue; }
                                break;
                            }
                            if (code == null) break;
                            p.Type = code;
                            io.L("Changed type of partition '" + old + "' to '" + TypeName(disk, code) + "'.\n");
                            break;
                        }
                    case "a":
                        {
                            if (disk.Label == "gpt") { io.L("a: unknown command"); break; }
                            if (disk.Parts.Count == 0) { io.L("No partition is defined yet!"); break; }
                            var p = disk.Parts.Count == 1 ? disk.Parts[0] : null;
                            if (p == null) { string ans = sh.S.ReadLine("Partition number (" + string.Join(",", disk.Parts.Select(x => x.Num.ToString()).ToArray()) + ", default " + disk.Parts.Last().Num + "): ", false); int n; p = int.TryParse((ans ?? "").Trim(), out n) ? disk.Parts.FirstOrDefault(x => x.Num == n) : disk.Parts.Last(); }
                            else io.L("Selected partition " + p.Num);
                            if (p == null) break;
                            p.Boot = !p.Boot;
                            io.L("The bootable flag on partition " + p.Num + " is " + (p.Boot ? "enabled" : "disabled") + " now.\n");
                            break;
                        }
                    case "F":
                        {
                            var free = FreeRanges(disk, false);
                            long tot = free.Sum(f => f[1] - f[0] + 1);
                            io.L("Unpartitioned space /dev/" + disk.Name + ": " + FdiskSizeLong(tot) + ", " + tot * 512 + " bytes, " + tot + " sectors\nUnits: sectors of 1 * 512 = 512 bytes\nSector size (logical/physical): 512 bytes / 512 bytes\n");
                            if (free.Count > 0) { io.L("  Start      End  Sectors  Size"); foreach (var f in free) io.L(f[0].ToString().PadLeft(7) + " " + f[1].ToString().PadLeft(8) + " " + (f[1] - f[0] + 1).ToString().PadLeft(8) + " " + FdiskSize(f[1] - f[0] + 1).PadLeft(5)); }
                            break;
                        }
                    case "v": io.L((disk.Parts.Count == 0 ? "No partitions found.\n" : "") + "Remaining " + FreeRanges(disk, false).Sum(f => f[1] - f[0] + 1) + " unallocated 512-byte sectors.\n"); break;
                    case "i":
                        {
                            if (disk.Parts.Count == 0) { io.L("No partition is defined yet!"); break; }
                            var p = disk.Parts.Last();
                            io.L("         Device: /dev/" + disk.Name + p.Num + "\n          Start: " + p.Start + "\n            End: " + p.End + "\n        Sectors: " + (p.End - p.Start + 1) + "\n           Size: " + FdiskSize(p.End - p.Start + 1) + "\n           Type: " + TypeName(disk, p.Type) + "\n");
                            break;
                        }
                    case "u": io.L("Changing display/entry units to cylinders (DEPRECATED!)."); break;
                    default: io.L(cmd + ": unknown command"); break;
                }
            }
        }

        static void FdiskNew(Sh sh, Io io, Machine m, BDev dev, Disk disk, HashSet<long> removeSig)
        {
            bool logical = false;
            int num;
            if (disk.Label == "gpt")
            {
                var used = disk.Parts.Select(x => x.Num).ToList();
                int def = Enumerable.Range(1, 128).First(x => !used.Contains(x));
                var freeG = FreeRanges(disk, false);
                if (freeG.Count == 0) { io.L("No free sectors available."); return; }
                string ans = sh.S.ReadLine("Partition number (" + def + "-128, default " + def + "): ", false);
                if (ans == null) return;
                if (ans.Trim().Length == 0) num = def;
                else if (!int.TryParse(ans.Trim(), out num) || num < 1 || num > 128) { io.L("Value out of range."); return; }
                else if (used.Contains(num)) { io.L("Partition " + num + " is already defined.  Delete it before re-adding it."); return; }
            }
            else
            {
                int primaries = disk.Parts.Count(x => x.Num <= 4);
                bool hasExt = disk.Parts.Any(x => x.Type == "5" || x.Type == "f");
                string kind;
                if (primaries >= 4 && !hasExt) { io.L("To create more partitions, first replace a primary with an extended partition."); return; }
                if (primaries >= 4 && hasExt) { kind = "l"; io.L("All primary partitions are in use.\nAdding logical partition " + (disk.Parts.Where(x => x.Num >= 5).Select(x => x.Num).DefaultIfEmpty(4).Max() + 1)); }
                else
                {
                    io.L("Partition type\n   p   primary (" + primaries + " primary, " + (hasExt ? 1 : 0) + " extended, " + (4 - primaries) + " free)\n   " + (hasExt ? "l   logical (numbered from 5)" : "e   extended (container for logical partitions)"));
                    string ans = sh.S.ReadLine("Select (default p): ", false);
                    if (ans == null) return;
                    kind = ans.Trim().Length == 0 ? "p" : ans.Trim();
                    if (kind == "p") io.L("Using default response p.");
                }
                if (kind == "l")
                {
                    if (!hasExt) { io.L("Invalid partition type `l'."); return; }
                    logical = true;
                    num = disk.Parts.Where(x => x.Num >= 5).Select(x => x.Num).DefaultIfEmpty(4).Max() + 1;
                    if (primaries < 4) io.L("Adding logical partition " + num);
                }
                else if (kind == "p" || kind == "e")
                {
                    var used = disk.Parts.Where(x => x.Num <= 4).Select(x => x.Num).ToList();
                    var avail = Enumerable.Range(1, 4).Where(x => !used.Contains(x)).ToList();
                    string ans = sh.S.ReadLine("Partition number (" + (avail.Count == 1 ? avail[0].ToString() : avail.First() + "-" + avail.Last()) + ", default " + avail.First() + "): ", false);
                    if (ans == null) return;
                    if (ans.Trim().Length == 0) num = avail.First();
                    else if (!int.TryParse(ans.Trim(), out num) || !avail.Contains(num)) { io.L(num >= 1 && num <= 4 ? "Partition " + num + " is already defined.  Delete it before re-adding it." : "Value out of range."); return; }
                    if (kind == "e" && hasExt) { io.L("Extended partition already exists."); return; }
                }
                else { io.L("Invalid partition type `" + kind + "'."); return; }
                if (kind == "e") logical = false;
                var free0 = FreeRanges(disk, logical);
                if (free0.Count == 0) { io.L("No free sectors available."); return; }
                if (kind == "e")
                {
                    string err0;
                    long st0 = AskFirst(sh, io, free0, out err0); if (st0 < 0) return;
                    var g0 = free0.First(f => st0 >= f[0] && st0 <= f[1]);
                    long en0 = AskLast(sh, io, st0, g0[1]); if (en0 < 0) return;
                    disk.Parts.Add(new Part { Num = num, Start = st0, End = en0, Type = "5" });
                    io.L("\nCreated a new partition " + num + " of type 'Extended' and of size " + FdiskSizeLong(en0 - st0 + 1).Replace(".0 ", " ") + ".\n");
                    return;
                }
            }
            var free = FreeRanges(disk, logical);
            if (free.Count == 0) { io.L("No free sectors available."); return; }
            string err;
            long start = AskFirst(sh, io, free, out err);
            if (start < 0) return;
            var gap = free.First(f => start >= f[0] && start <= f[1]);
            long end = AskLast(sh, io, start, gap[1]);
            if (end < 0) return;
            string type = disk.Label == "gpt" ? "linux" : "83";
            disk.Parts.Add(new Part { Num = num, Start = start, End = end, Type = type });
            io.L("\nCreated a new partition " + num + " of type '" + TypeName(disk, type) + "' and of size " + FdiskSizeLong(end - start + 1).Replace(".0 ", " ") + ".");
            BlockContent old;
            if (m.Content.TryGetValue(dev.Disk.Name + "@" + start, out old) && old != null && old.Kind != null)
            {
                io.L("Partition #" + num + " contains a " + old.Kind + " signature.\n");
                string ans = sh.S.ReadLine("Do you want to remove the signature? [Y]es/[N]o: ", false);
                if (ans != null && (ans.Trim().Length == 0 || ans.Trim().StartsWith("y", StringComparison.OrdinalIgnoreCase))) { removeSig.Add(start); io.L("\nThe signature will be removed by a write command.\n"); }
                else io.L("");
            }
            else io.L("");
        }
        static long AskFirst(Sh sh, Io io, List<long[]> free, out string err)
        {
            err = null;
            long def = free[0][0];
            for (;;)
            {
                string ans = sh.S.ReadLine("First sector (" + free[0][0] + "-" + free[free.Count - 1][1] + ", default " + def + "): ", false);
                if (ans == null) return -1;
                long v = ParseSector(ans, 0, def, free[0][0], free[free.Count - 1][1], out err);
                if (v < 0 || !free.Any(f => v >= f[0] && v <= f[1])) { io.L("Value out of range."); continue; }
                return v;
            }
        }
        static long AskLast(Sh sh, Io io, long start, long max)
        {
            for (;;)
            {
                string ans = sh.S.ReadLine("Last sector, +/-sectors or +/-size{K,M,G,T,P} (" + start + "-" + max + ", default " + max + "): ", false);
                if (ans == null) return -1;
                string err;
                long v = ParseSector(ans, start, max, start, max, out err);
                if (v < 0) { io.L("Value out of range."); continue; }
                return v;
            }
        }

        // ------------------------------------------------------------ parted
        static long PartedPos(string s, Disk d, string defUnit, out string err)
        {
            err = null;
            s = s.Trim();
            if (s.EndsWith("%", StringComparison.Ordinal)) { double p; if (!double.TryParse(s.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out p)) { err = "Invalid number."; return -1; } return (long)(d.Sectors * p / 100.0); }
            var mm = Regex.Match(s, "^(-?\\d+(\\.\\d+)?)\\s*(s|B|kB|KB|MB|GB|TB|KiB|MiB|GiB|TiB|K|M|G|T)?$", RegexOptions.IgnoreCase);
            if (!mm.Success) { err = "Invalid number."; return -1; }
            double n = double.Parse(mm.Groups[1].Value, CultureInfo.InvariantCulture);
            string u = mm.Groups[3].Success && mm.Groups[3].Value.Length > 0 ? mm.Groups[3].Value : defUnit;
            double bytes;
            switch (u.ToLowerInvariant())
            {
                case "s": return n < 0 ? d.Sectors + (long)n : (long)n;
                case "b": bytes = n; break;
                case "kb": case "k": bytes = n * 1000; break;
                case "mb": case "m": bytes = n * 1e6; break;
                case "gb": case "g": bytes = n * 1e9; break;
                case "tb": case "t": bytes = n * 1e12; break;
                case "kib": bytes = n * 1024; break;
                case "mib": bytes = n * 1048576; break;
                case "gib": bytes = n * 1073741824; break;
                case "tib": bytes = n * 1099511627776; break;
                default: bytes = n * 1e6; break;
            }
            long sec = (long)Math.Round(bytes / 512);
            if (n < 0) sec = d.Sectors + sec;
            return sec;
        }
        static string PartedUnit(long sectors, string unit)
        {
            double b = sectors * 512.0;
            switch (unit)
            {
                case "s": return sectors + "s";
                case "b": return (long)b + "B";
                case "mib": return (b / 1048576).ToString("0.##", CultureInfo.InvariantCulture) + "MiB";
                case "gib": return (b / 1073741824).ToString("0.##", CultureInfo.InvariantCulture) + "GiB";
                case "gb": return (b / 1e9).ToString("0.##", CultureInfo.InvariantCulture) + "GB";
            }
            // compact: like parted's default (MB etc.)
            if (b < 1e6) return Math.Round(b / 1000).ToString(CultureInfo.InvariantCulture) + "kB";
            if (b < 1e9) return Math.Round(b / 1e6).ToString(CultureInfo.InvariantCulture) + "MB";
            if (b < 1e12) return (Math.Round(b / 1e7) / 100).ToString("0.##", CultureInfo.InvariantCulture) + "GB";
            return (Math.Round(b / 1e10) / 100).ToString("0.##", CultureInfo.InvariantCulture) + "TB";
        }

        static int Parted(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "slma:", "script|s,list|l,machine|m,align=|a", "parted", true);
            if (o.Error != null) return Fail(io, o);
            var m = sh.M;
            if (o.Has("l")) { foreach (var d in m.Disks.Where(x => !x.Name.StartsWith("sr", StringComparison.Ordinal))) { PartedPrint(m, io, d, "compact", false); io.L(""); } return 0; }
            if (o.Args.Count == 0) { io.E("Usage: parted [OPTION]... [DEVICE [COMMAND [PARAMETERS]...]...]"); return 1; }
            if (sh.P.Cr.Uid != 0) { io.E("Error: Error opening " + o.Args[0] + ": Permission denied"); return 1; }
            var dev = m.FindDev(o.Args[0]);
            if (dev == null || dev.Type != "disk") { io.E("Error: Could not stat device " + o.Args[0] + " - No such file or directory."); return 1; }
            var disk = dev.Disk;
            bool script = o.Has("s");
            var cmds = o.Args.Skip(1).ToList();
            string unit = "compact";
            Func<List<string>, int> run = w => PartedOne(sh, io, m, dev, disk, w, script, ref unit);
            if (cmds.Count > 0)
            {
                // several commands may follow one another on the command line
                int st = 0;
                while (cmds.Count > 0)
                {
                    int take = PartedArity(cmds, disk);
                    var one = cmds.Take(take).ToList();
                    cmds = cmds.Skip(take).ToList();
                    st = run(one);
                    if (st != 0) return st;
                }
                return st;
            }
            if (sh.S == null) return 0;
            io.L("GNU Parted 3.5\nUsing " + dev.Path + "\nWelcome to GNU Parted! Type 'help' to view a list of commands.");
            for (;;)
            {
                string line = sh.S.ReadLine("(parted) ", false);
                if (line == null) { io.L(""); return 0; }
                var w = Regex.Matches(line, "\"[^\"]*\"|\\S+").Cast<Match>().Select(x => x.Value.Trim('"')).ToList();
                if (w.Count == 0) continue;
                if (w[0] == "quit" || w[0] == "q") { io.L(""); return 0; }
                run(w);
            }
        }
        static int PartedArity(List<string> c, Disk d)
        {
            switch (c[0])
            {
                case "mklabel": case "mktable": return 2;
                case "rm": return 2;
                case "unit": return 2;
                case "set": return 4;
                case "name": return 3;
                case "toggle": return 3;
                case "resizepart": return 3;
                case "mkpart":
                    {
                        // mkpart [part-type|name] [fs-type] start end
                        int n = 1;
                        var rest = c.Skip(1).ToList();
                        int numeric = 0; int i = 0;
                        for (; i < rest.Count && numeric < 2; i++) { if (Regex.IsMatch(rest[i], "^-?\\d") || rest[i].EndsWith("%", StringComparison.Ordinal)) numeric++; }
                        return n + i;
                    }
            }
            return 1;
        }
        static int PartedOne(Sh sh, Io io, Machine m, BDev dev, Disk disk, List<string> w, bool script, ref string unit)
        {
            string cmd = w[0];
            Func<string, string> ask = q => script ? null : sh.S == null ? null : sh.S.ReadLine(q, false);
            switch (cmd)
            {
                case "help": io.L("  align-check TYPE N                       check partition N for TYPE(min|opt) alignment\n  help [COMMAND]                           print general help, or help on COMMAND\n  mklabel,mktable LABEL-TYPE               create a new disklabel (partition table)\n  mkpart PART-TYPE [FS-TYPE] START END     make a partition\n  name NUMBER NAME                         name partition NUMBER as NAME\n  print [devices|free|list,all]            display the partition table, or available devices, or free space, or all found partitions\n  quit                                     exit program\n  rescue START END                         rescue a lost partition near START and END\n  resizepart NUMBER END                    resize partition NUMBER\n  rm NUMBER                                delete partition NUMBER\n  select DEVICE                            choose the device to edit\n  disk_set FLAG STATE                      change the FLAG on selected device\n  disk_toggle [FLAG]                       toggle the state of FLAG on selected device\n  set NUMBER FLAG STATE                    change the FLAG on partition NUMBER\n  toggle [NUMBER [FLAG]]                   toggle the state of FLAG on partition NUMBER\n  unit UNIT                                set the default unit to UNIT\n  version                                  display the version number and copyright information of GNU Parted"); return 0;
                case "print": case "p": PartedPrint(m, io, disk, unit, w.Count > 1 && w[1] == "free"); return 0;
                case "unit": { if (w.Count < 2) { string u = ask("Unit?  [compact]? "); if (u == null) return 1; w.Add(u); } unit = w[1].ToLowerInvariant(); return 0; }
                case "mklabel": case "mktable":
                    {
                        string lb = w.Count > 1 ? w[1] : ask("New disk label type? ");
                        if (lb == null) { io.E("Error: Expecting a disk label type."); return 1; }
                        if (lb != "gpt" && lb != "msdos") { io.E("parted: invalid token: " + lb); return 1; }
                        if (disk.Label.Length > 0 && !script)
                        {
                            string ans = ask("Warning: The existing disk label on " + dev.Path + " will be destroyed and all data on this disk will be lost. Do you want to continue?\nYes/No? ");
                            if (ans == null || !ans.Trim().StartsWith("y", StringComparison.OrdinalIgnoreCase)) return 1;
                        }
                        if (disk.Parts.Any(p => { var bd = m.Devices().FirstOrDefault(x => x.Part == p); return bd != null && m.InUse(bd) != null; })) { io.E("Error: Partition(s) on " + dev.Path + " are being used."); return 1; }
                        disk.Label = lb == "msdos" ? "dos" : "gpt"; disk.Parts.Clear(); disk.DiskId = lb == "gpt" ? m.NewUuid().ToUpperInvariant() : "0x" + m.NewHex(8);
                        m.SetContent(dev, null);
                        m.SyncDev();
                        return 0;
                    }
                case "mkpart":
                    {
                        if (disk.Label.Length == 0) { io.E("Error: " + dev.Path + ": unrecognised disk label"); return 1; }
                        var args = w.Skip(1).ToList();
                        string ptype = null, name = "", fstype = null;
                        if (disk.Label == "dos")
                        {
                            if (args.Count > 0 && (args[0] == "primary" || args[0] == "extended" || args[0] == "logical")) { ptype = args[0]; args.RemoveAt(0); }
                            else { ptype = ask("Partition type?  primary/extended? "); if (ptype == null) { io.E("Error: Expecting a partition type."); return 1; } }
                        }
                        else
                        {
                            if (args.Count > 0 && !Regex.IsMatch(args[0], "^-?\\d") && !IsFsType(args[0])) { name = args[0]; args.RemoveAt(0); }
                            else if (args.Count < 2 && !script) { name = ask("Partition name?  []? ") ?? ""; }
                        }
                        if (args.Count > 0 && !Regex.IsMatch(args[0], "^-?\\d")) { fstype = args[0]; args.RemoveAt(0); }
                        else if (args.Count < 2 && !script && fstype == null) { fstype = ask("File system type?  [ext2]? "); }
                        if (args.Count < 1 && !script) { var s = ask("Start? "); if (s == null) return 1; args.Add(s); }
                        if (args.Count < 2 && !script) { var e = ask("End? "); if (e == null) return 1; args.Add(e); }
                        if (args.Count < 2) { io.E("Error: Expecting " + (args.Count == 0 ? "a start" : "an end") + " position."); return 1; }
                        string err;
                        long start = PartedPos(args[0], disk, "MB", out err), end = PartedPos(args[1], disk, "MB", out err);
                        if (err != null || start < 0 || end < 0) { io.E("Error: Invalid number."); return 1; }
                        // parted aligns to 1 MiB and counts the end as inclusive
                        if (start < 2048) start = 2048;
                        start = (start + 2047) / 2048 * 2048;
                        end = end - 1;
                        long maxEnd = disk.Label == "gpt" ? disk.Sectors - 34 : disk.Sectors - 1;
                        if (end > maxEnd) end = maxEnd;
                        if (end <= start) { io.E("Error: Can't have the end before the start! (start sector=" + start + " length=" + (end - start) + ")"); return 1; }
                        foreach (var p in disk.Parts.Where(x => x.Num <= 4 || disk.Label == "gpt")) if (start <= p.End && end >= p.Start && !(ptype == "logical"))
                            { io.E("Error: You requested a partition from " + PartedUnit(start, "compact") + " to " + PartedUnit(end + 1, "compact") + " (sectors " + start + ".." + end + ").\nThe closest location we can manage is " + PartedUnit(p.End + 2048, "compact") + " to " + PartedUnit(maxEnd, "compact") + "."); return 1; }
                        int num;
                        if (ptype == "logical") num = disk.Parts.Where(x => x.Num >= 5).Select(x => x.Num).DefaultIfEmpty(4).Max() + 1;
                        else { var used = disk.Parts.Select(x => x.Num).ToList(); num = Enumerable.Range(1, disk.Label == "gpt" ? 128 : 4).FirstOrDefault(x => !used.Contains(x)); if (num == 0) { io.E("Error: Too many primary partitions."); return 1; } }
                        string type = disk.Label == "gpt" ? (fstype == "linux-swap" ? "swap" : "linux") : (ptype == "extended" ? "5" : fstype == "linux-swap" ? "82" : fstype != null && fstype.StartsWith("fat", StringComparison.Ordinal) ? "c" : "83");
                        var np = new Part { Num = num, Start = start, End = end, Type = type, Name = disk.Label == "gpt" ? name : "" };
                        np.PartUuid = disk.Label == "gpt" ? m.NewUuid() : disk.DiskId.Substring(2) + "-" + num.ToString("00");
                        disk.Parts.Add(np);
                        m.SyncDev();
                        io.L("Information: You may need to update /etc/fstab.\n");
                        return 0;
                    }
                case "rm":
                    {
                        string ns = w.Count > 1 ? w[1] : ask("Partition number? ");
                        int n;
                        if (ns == null || !int.TryParse(ns, out n)) { io.E("Error: Expecting a partition number."); return 1; }
                        var p = disk.Parts.FirstOrDefault(x => x.Num == n);
                        if (p == null) { io.E("Error: Partition doesn't exist."); return 1; }
                        var bd = m.Devices().FirstOrDefault(x => x.Part == p);
                        if (bd != null && m.InUse(bd) != null) { io.E("Error: Partition " + bd.Path + " is being used. You must unmount it before you modify it with Parted."); return 1; }
                        disk.Parts.Remove(p);
                        m.SyncDev();
                        io.L("Information: You may need to update /etc/fstab.\n");
                        return 0;
                    }
                case "set": case "toggle":
                    {
                        if (w.Count < 3) { io.E("Error: Expecting a partition number."); return 1; }
                        int n; if (!int.TryParse(w[1], out n)) { io.E("Error: Expecting a partition number."); return 1; }
                        var p = disk.Parts.FirstOrDefault(x => x.Num == n);
                        if (p == null) { io.E("Error: Partition doesn't exist."); return 1; }
                        string flag = w[2]; bool on = cmd == "toggle" ? true : (w.Count > 3 ? w[3] == "on" : true);
                        switch (flag)
                        {
                            case "lvm": p.Type = on ? (disk.Label == "gpt" ? "lvm" : "8e") : (disk.Label == "gpt" ? "linux" : "83"); break;
                            case "swap": p.Type = on ? (disk.Label == "gpt" ? "swap" : "82") : (disk.Label == "gpt" ? "linux" : "83"); break;
                            case "raid": p.Type = on ? (disk.Label == "gpt" ? "raid" : "fd") : (disk.Label == "gpt" ? "linux" : "83"); break;
                            case "boot": if (disk.Label == "gpt") p.Type = on ? "uefi" : "linux"; else p.Boot = on; break;
                            case "esp": p.Type = on ? "uefi" : "linux"; break;
                            case "bios_grub": p.Type = on ? "bios" : "linux"; break;
                            default: io.E("parted: invalid token: " + flag); return 1;
                        }
                        io.L("Information: You may need to update /etc/fstab.\n");
                        return 0;
                    }
                case "name": { if (disk.Label != "gpt") { io.E("Error: msdos disk labels do not support partition names."); return 1; } int n; if (w.Count < 3 || !int.TryParse(w[1], out n)) { io.E("Error: Expecting a partition number."); return 1; } var p = disk.Parts.FirstOrDefault(x => x.Num == n); if (p == null) { io.E("Error: Partition doesn't exist."); return 1; } p.Name = w[2]; return 0; }
                case "resizepart":
                    {
                        int n; if (w.Count < 3 || !int.TryParse(w[1], out n)) { io.E("Error: Expecting a partition number."); return 1; }
                        var p = disk.Parts.FirstOrDefault(x => x.Num == n); if (p == null) { io.E("Error: Partition doesn't exist."); return 1; }
                        string err; long end = PartedPos(w[2], disk, "MB", out err) - 1;
                        if (end <= p.Start) { io.E("Error: Can't have the end before the start!"); return 1; }
                        if (disk.Parts.Any(x => x != p && x.Start > p.Start && x.Start <= end && (x.Num <= 4 || disk.Label == "gpt"))) { io.E("Error: Can't have overlapping partitions."); return 1; }
                        p.End = end;
                        m.SyncDev();
                        return 0;
                    }
                case "version": io.L("parted (GNU parted) 3.5\nCopyright (C) 2022 Free Software Foundation, Inc."); return 0;
                case "select": io.L("Using " + (w.Count > 1 ? w[1] : dev.Path)); return 0;
            }
            io.E("Error: Unknown command: " + cmd);
            return 1;
        }
        static bool IsFsType(string s) { return new[] { "xfs", "ext2", "ext3", "ext4", "linux-swap", "fat16", "fat32", "ntfs", "btrfs", "hfs", "lvm" }.Contains(s.ToLowerInvariant()); }

        static void PartedPrint(Machine m, Io io, Disk d, string unit, bool free)
        {
            io.L("Model: Virtio Block Device (virtblk)\nDisk /dev/" + d.Name + ": " + PartedUnit(d.Sectors, unit) + "\nSector size (logical/physical): 512B/512B\nPartition Table: " + (d.Label == "" ? "unknown" : d.Label == "dos" ? "msdos" : "gpt") + "\nDisk Flags: \n");
            if (d.Label == "") return;
            var rows = new List<string[]>();
            if (d.Label == "gpt") rows.Add(new[] { "Number", "Start", "End", "Size", "File system", "Name", "Flags" });
            else rows.Add(new[] { "Number", "Start", "End", "Size", "Type", "File system", "Flags" });
            foreach (var p in d.Parts.OrderBy(x => x.Start))
            {
                var bd = m.Devices().FirstOrDefault(x => x.Part == p);
                var c = m.ContentOf(bd);
                string fs = c == null ? "" : c.Kind == "swap" ? "linux-swap(v1)" : c.Kind == "LVM2_member" ? "" : c.Kind == "vfat" ? "fat32" : c.Kind;
                string flags = d.Label == "gpt" ? (p.Type == "lvm" ? "lvm" : p.Type == "swap" ? "swap" : p.Type == "uefi" ? "boot, esp" : p.Type == "raid" ? "raid" : p.Type == "bios" ? "bios_grub" : "") : ((p.Boot ? "boot" : "") + (p.Type == "8e" ? "lvm" : p.Type == "fd" ? "raid" : p.Type == "82" ? "swap" : ""));
                string start = PartedUnit(p.Start, unit), end = PartedUnit(p.End + 1, unit), size = PartedUnit(p.End - p.Start + 1, unit);
                if (d.Label == "gpt") rows.Add(new[] { " " + p.Num, start, end, size, fs, p.Name, flags });
                else rows.Add(new[] { " " + p.Num, start, end, size, p.Num >= 5 ? "logical" : (p.Type == "5" || p.Type == "f") ? "extended" : "primary", fs, flags });
            }
            var w = new int[7];
            foreach (var r in rows) for (int i = 0; i < 7; i++) w[i] = Math.Max(w[i], r[i].Length);
            foreach (var r in rows) io.L(string.Join("  ", r.Select((x, i) => i == 6 ? x : x.PadRight(w[i])).ToArray()).TrimEnd());
            io.L("");
        }

        // ------------------------------------------------------------ gdisk
        static int Gdisk(Sh sh, List<string> a, Io io)
        {
            var m = sh.M;
            if (a.Count < 2) { io.E("Usage: gdisk [-l] device_file"); return 1; }
            if (a[1] == "-l" && a.Count > 2) { var d0 = m.FindDev(a[2]); if (d0 == null) { io.E("Problem opening " + a[2] + " for reading! Error is 2."); return 1; } io.P(FdiskTable(m, d0.Disk)); return 0; }
            if (sh.P.Cr.Uid != 0) { io.E("Problem opening " + a[1] + " for reading! Error is 13.\nYou must run this program as root or use sudo!"); return 1; }
            var dev = m.FindDev(a[1]);
            if (dev == null || dev.Type != "disk") { io.E("Problem opening " + a[1] + " for reading! Error is 2.\nThe specified file does not exist!"); return 1; }
            var disk = CopyDisk(dev.Disk);
            io.L("GPT fdisk (gdisk) version 1.0.7\n\nPartition table scan:\n  MBR: " + (disk.Label == "dos" ? "MBR only" : disk.Label == "gpt" ? "protective" : "not present") + "\n  BSD: not present\n  APM: not present\n  GPT: " + (disk.Label == "gpt" ? "present" : "not present") + "\n");
            if (disk.Label == "gpt") io.L("Found valid GPT with protective MBR; using GPT.\n");
            else if (disk.Label == "dos")
            {
                io.L("\n***************************************************************\nFound invalid GPT and valid MBR; will convert MBR to GPT format\nin memory. THIS OPERATION IS POTENTIALLY DESTRUCTIVE! Exit by\ntyping 'q' if you don't want to convert your MBR partitions\nto GPT format!\n***************************************************************\n");
                disk.Label = "gpt"; foreach (var p in disk.Parts) p.Type = p.Type == "8e" ? "lvm" : p.Type == "82" ? "swap" : "linux";
            }
            else { io.L("Creating new GPT entries in memory.\n"); disk.Label = "gpt"; disk.DiskId = m.NewUuid().ToUpperInvariant(); }
            if (sh.S == null) return 1;
            for (;;)
            {
                string cmd = sh.S.ReadLine("Command (? for help): ", false);
                if (cmd == null) return 0;
                cmd = cmd.Trim();
                switch (cmd)
                {
                    case "?": io.L("b\tback up GPT data to a file\nc\tchange a partition's name\nd\tdelete a partition\ni\tshow detailed information on a partition\nl\tlist known partition types\nn\tadd a new partition\no\tcreate a new empty GUID partition table (GPT)\np\tprint the partition table\nq\tquit without saving changes\nr\trecovery and transformation options (experts only)\ns\tsort partitions\nt\tchange a partition's type code\nv\tverify disk\nw\twrite table to disk and exit\nx\textra functionality (experts only)\n?\tprint this menu\n"); break;
                    case "p":
                        {
                            io.L("Disk /dev/" + disk.Name + ": " + disk.Sectors + " sectors, " + FdiskSizeLong(disk.Sectors).Replace(" ", " ") + "\nSector size (logical/physical): 512/512 bytes\nDisk identifier (GUID): " + disk.DiskId + "\nPartition table holds up to 128 entries\nMain partition table begins at sector 2 and ends at sector 33\nFirst usable sector is 34, last usable sector is " + (disk.Sectors - 34) + "\nPartitions will be aligned on 2048-sector boundaries\nTotal free space is " + FreeRanges(disk, false).Sum(f => f[1] - f[0] + 1) + " sectors\n\nNumber  Start (sector)    End (sector)  Size       Code  Name");
                            foreach (var p in disk.Parts.OrderBy(x => x.Num)) io.L(p.Num.ToString().PadLeft(4) + p.Start.ToString().PadLeft(16) + p.End.ToString().PadLeft(16) + "   " + FdiskSizeLong(p.End - p.Start + 1).PadRight(10) + " " + GptTypes.First(g => g[0] == p.Type)[3].ToUpperInvariant() + "  " + (p.Name.Length > 0 ? p.Name : GptTypes.First(g => g[0] == p.Type)[1]));
                            io.L("");
                            break;
                        }
                    case "n":
                        {
                            var used = disk.Parts.Select(x => x.Num).ToList();
                            int def = Enumerable.Range(1, 128).First(x => !used.Contains(x));
                            var free = FreeRanges(disk, false);
                            if (free.Count == 0) { io.L("No free sectors available! Cannot create partition."); break; }
                            string ans = sh.S.ReadLine("Partition number (" + def + "-128, default " + def + "): ", false); if (ans == null) break;
                            int num; if (ans.Trim().Length == 0) num = def; else if (!int.TryParse(ans.Trim(), out num) || used.Contains(num)) break;
                            long start;
                            for (;;) { string s = sh.S.ReadLine("First sector (34-" + (disk.Sectors - 34) + ", default = " + free[0][0] + ") or {+-}size{KMGTP}: ", false); if (s == null) return 0; string e; start = ParseSector(s, free[0][0], free[0][0], 34, disk.Sectors - 34, out e); if (start >= 0 && free.Any(f => start >= f[0] && start <= f[1])) break; }
                            var gap = free.First(f => start >= f[0] && start <= f[1]);
                            long end;
                            for (;;) { string s = sh.S.ReadLine("Last sector (" + start + "-" + gap[1] + ", default = " + gap[1] + ") or {+-}size{KMGTP}: ", false); if (s == null) return 0; string e; end = ParseSector(s, start, gap[1], start, gap[1], out e); if (end >= 0) break; }
                            io.L("Current type is 8300 (Linux filesystem)");
                            string tc = sh.S.ReadLine("Hex code or GUID (L to show codes, Enter = 8300): ", false);
                            string type = string.IsNullOrEmpty(tc) || tc.Trim().Length == 0 ? "linux" : (GptAlias(tc.Trim()) ?? "linux");
                            disk.Parts.Add(new Part { Num = num, Start = start, End = end, Type = type });
                            io.L("Changed type of partition to '" + TypeName(disk, type) + "'\n");
                            break;
                        }
                    case "d":
                        {
                            if (disk.Parts.Count == 0) { io.L("No partitions"); break; }
                            int n = disk.Parts[0].Num;
                            if (disk.Parts.Count > 1) { string ans = sh.S.ReadLine("Partition number (" + string.Join("-", new[] { disk.Parts.Min(x => x.Num).ToString(), disk.Parts.Max(x => x.Num).ToString() }) + "): ", false); if (ans == null || !int.TryParse(ans.Trim(), out n)) break; }
                            else io.L("Using " + n);
                            disk.Parts.RemoveAll(x => x.Num == n);
                            break;
                        }
                    case "t":
                        {
                            if (disk.Parts.Count == 0) break;
                            int n = disk.Parts[0].Num;
                            if (disk.Parts.Count > 1) { string ans = sh.S.ReadLine("Partition number (" + disk.Parts.Min(x => x.Num) + "-" + disk.Parts.Max(x => x.Num) + "): ", false); if (ans == null || !int.TryParse(ans.Trim(), out n)) break; }
                            else io.L("Using " + n);
                            var p = disk.Parts.FirstOrDefault(x => x.Num == n); if (p == null) break;
                            io.L("Current type is " + GptTypes.First(g => g[0] == p.Type)[3].ToUpperInvariant() + " (" + TypeName(disk, p.Type) + ")");
                            string tc = sh.S.ReadLine("Hex code or GUID (L to show codes, Enter = 8300): ", false);
                            string type = string.IsNullOrEmpty(tc) || tc.Trim().Length == 0 ? "linux" : GptAlias(tc.Trim());
                            if (type == null) { io.L("Invalid type code"); break; }
                            p.Type = type;
                            io.L("Changed type of partition to '" + TypeName(disk, type) + "'\n");
                            break;
                        }
                    case "l": foreach (var g in GptTypes) io.L(g[3].ToUpperInvariant() + " " + g[1]); break;
                    case "o": { string ans = sh.S.ReadLine("This option deletes all partitions and creates a new protective MBR.\nProceed? (Y/N): ", false); if (ans != null && ans.Trim().StartsWith("Y", StringComparison.OrdinalIgnoreCase)) { disk.Parts.Clear(); disk.DiskId = m.NewUuid().ToUpperInvariant(); } break; }
                    case "q": return 0;
                    case "w":
                        {
                            io.L("\nFinal checks complete. About to write GPT data. THIS WILL OVERWRITE EXISTING\nPARTITIONS!!\n");
                            string ans = sh.S.ReadLine("Do you want to proceed? (Y/N): ", false);
                            if (ans == null || !ans.Trim().StartsWith("Y", StringComparison.OrdinalIgnoreCase)) break;
                            io.L("OK; writing new GUID partition table (GPT) to " + dev.Path + ".\nThe operation has completed successfully.");
                            var real = dev.Disk;
                            real.Label = "gpt"; real.DiskId = disk.DiskId; real.Parts = disk.Parts;
                            foreach (var p in real.Parts) if (p.PartUuid.Length == 0) p.PartUuid = m.NewUuid();
                            m.SetContent(m.Devices().First(x => x.Key == dev.Key), null);
                            m.SyncDev();
                            return 0;
                        }
                    case "v": io.L("\nNo problems found. " + FreeRanges(disk, false).Sum(f => f[1] - f[0] + 1) + " free sectors in " + FreeRanges(disk, false).Count + " segments.\n"); break;
                    default: if (cmd.Length > 0) io.L("b\tback up GPT data to a file\n...\n?\tprint this menu"); break;
                }
            }
        }
    }
}
