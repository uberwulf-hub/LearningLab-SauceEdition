// Cert Lab - booting and shutting down. A boot rebuilds everything that runs from what is on disk, in the real
// order: GRUB's boot entries (BLS files in /boot/loader/entries) and kernel arguments, the initramfs (rd.break
// stops here), the root file system, SELinux (and a relabel if /.autorelabel exists), /etc/fstab (a bad line drops
// to emergency mode), the hostname, NetworkManager's profiles, firewalld, and the services enabled for the target.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CertLab
{
    public class BootEntry { public string Title, Kernel, Args, File; public bool Rescue; }

    public partial class Machine
    {
        public const string RootDev = "/dev/mapper/rhel-root";
        public bool RelabelPending;
        public string BootTarget = "";

        // ------------------------------------------------------------ GRUB's view
        public string DefaultArgs()
        {
            return "root=" + RootDev + " ro crashkernel=1G-4G:192M,4G-64G:256M,64G-:512M resume=/dev/mapper/rhel-swap rd.lvm.lv=rhel/root rd.lvm.lv=rhel/swap rhgb quiet";
        }

        /// <summary>GRUB reads the boot partition itself (vda1), mounted or not: a file there by its path
        /// inside the partition ("loader/entries"), or null.</summary>
        Inode BootNode(string rel)
        {
            var c = ContentOf(FindDev("/dev/vda1"));
            var fs = c == null ? null : V.Fs(c.FsId);
            if (fs == null) return null;
            var n = fs.Root;
            foreach (var p in rel.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int ino;
                if (n == null || n.T != 'd' || !n.Kids.TryGetValue(p, out ino)) return null;
                n = fs.Node(ino);
            }
            return n;
        }
        string BootCat(string rel) { var n = BootNode(rel); return n == null || n.T != 'f' ? null : n.Text ?? ""; }

        /// <summary>The boot entries from /boot/loader/entries (newest kernel first), plus the rescue entry.</summary>
        public List<BootEntry> BootEntries()
        {
            var l = new List<BootEntry>();
            try
            {
                var d = BootNode("loader/entries");
                if (d != null && d.T == 'd')
                    foreach (var k in d.Kids.Keys.OrderByDescending(x => x, StringComparer.Ordinal))
                    {
                        if (!k.EndsWith(".conf", StringComparison.Ordinal)) continue;
                        string t = BootCat("loader/entries/" + k) ?? "";
                        var e = new BootEntry { File = k };
                        foreach (var line in t.Split('\n'))
                        {
                            var ln = line.Trim();
                            if (ln.StartsWith("title ", StringComparison.Ordinal)) e.Title = ln.Substring(6).Trim();
                            else if (ln.StartsWith("linux ", StringComparison.Ordinal)) e.Kernel = ln.Substring(6).Trim().Replace("/vmlinuz-", "");
                            else if (ln.StartsWith("options ", StringComparison.Ordinal)) e.Args = ln.Substring(8).Trim();
                        }
                        if (e.Kernel == null) continue;
                        e.Rescue = e.Kernel.Contains("rescue");
                        if (e.Args == null) e.Args = DefaultArgs();
                        e.Args = e.Args.Replace("$kernelopts", DefaultArgs()).Replace("$tuned_params", "").Trim();
                        l.Add(e);
                    }
            }
            catch (VfsEx) { }
            // rescue last, otherwise newest first
            l = l.OrderBy(e => e.Rescue ? 1 : 0).ThenByDescending(e => e.Kernel, StringComparer.Ordinal).ToList();
            if (l.Count == 0) l.Add(new BootEntry { Title = "Red Hat Enterprise Linux (" + Kernels[0] + ") 9.5 (Plow)", Kernel = Kernels[0], Args = DefaultArgs() });
            return l;
        }

        /// <summary>The entry grubby / grub2-set-default chose (saved_entry in /boot/grub2/grubenv).</summary>
        public int DefaultEntryIndex()
        {
            var env = BootCat("grub2/grubenv") ?? "";
            var m = Regex.Match(env, "saved_entry=(\\S+)");
            var es = BootEntries();
            if (m.Success)
            {
                string want = m.Groups[1].Value;
                for (int i = 0; i < es.Count; i++) if (es[i].File == want + ".conf" || es[i].File.Replace(".conf", "") == want) return i;
                int n; if (int.TryParse(want, out n) && n >= 0 && n < es.Count) return n;
            }
            return 0;
        }
        /// <summary>The menu's countdown in seconds, from grub.cfg's "set timeout=" (GRUB never reads /etc/default/grub:
        /// a change there counts only after grub2-mkconfig). At least 1 second, so the menu can always be reached.</summary>
        public int MenuTimeout()
        {
            var m = Regex.Match(BootCat("grub2/grub.cfg") ?? "", "(?m)^\\s*set timeout=(\\d+)");
            int t;
            if (!m.Success || !int.TryParse(m.Groups[1].Value, out t)) t = 5;
            return Math.Max(1, t);
        }
        public string DefaultKernel() { var es = BootEntries(); int i = DefaultEntryIndex(); return es.Count > 0 ? es[Math.Min(i, es.Count - 1)].Kernel : null; }

        public string IssueText()
        {
            if (Banner.Length > 0) return Banner.TrimEnd('\n') + "\n";
            return "Red Hat Enterprise Linux 9.5 (Plow)   (simulated by Cert Lab, not a Red Hat product)\nKernel " + KernelRelease + " on an x86_64\n\nActivate the web console with: systemctl enable --now cockpit.socket\n";
        }

        // ------------------------------------------------------------ power
        public void PowerOn()
        {
            if (State != "off") return;
            State = "grub";
            if (!Sessions.Any(s => s.IsConsole)) Boot(Kernels[0], DefaultArgsFromEntry(), false);
        }
        string DefaultArgsFromEntry() { var es = BootEntries(); var e = es[Math.Min(DefaultEntryIndex(), es.Count - 1)]; return e.Args; }

        /// <summary>Stops everything; reboot: GRUB next (or boots at once when nobody watches the console / autoBoot).</summary>
        public void Shutdown(bool reboot) { Shutdown(reboot, false); }
        public void Shutdown(bool reboot, bool autoBoot)
        {
            if (State == "running" || State == "emergency" || State == "rescue")
            {
                foreach (var s in Services.Values.Where(x => x.Active == "active").ToList()) Log("systemd", 1, 6, "Stopping " + UnitDescription(s.Unit) + "...", s.Unit);
            }
            Log("systemd", 1, 6, reboot ? "Reached target System Reboot." : "Reached target System Power Off.", null);
            KeepJournal();
            Services.Clear(); Procs.Clear(); Mounts.Clear(); Swaps.Clear(); RunFs.Clear(); Ifs.Clear(); FwRuntime.Clear(); FwRunning = false;
            foreach (var c in Containers) { c.Running = false; c.Status = "Exited (0)"; }
            State = reboot ? "grub" : "off";
            string msg = reboot ? "\n[  OK  ] Stopped target Multi-User System.\n[  OK  ] Unmounted file systems.\n[  OK  ] Reached target System Reboot.\n" : "\n[  OK  ] Reached target System Power Off.\n";
            // ssh sessions into this machine from the other one
            foreach (var other in Net.Machines)
                foreach (var s in other.Sessions.ToList())
                    if (s.Remote.Contains(this)) s.Kick(this, "Connection to " + HostnameNow + " closed by remote host.\nConnection to " + HostnameNow + " closed.\n");
            bool console = Sessions.Any(s => s.IsConsole);
            if (reboot && (!console || autoBoot)) Boot(Kernels[0], DefaultArgsFromEntry(), false);
            foreach (var s in Sessions.ToList()) s.Kick(this, msg);
        }

        void KeepJournal()
        {
            // the journal on disk keeps what was written while it was persistent
        }

        public bool JournalPersistent()
        {
            string conf = V.Cat("/etc/systemd/journald.conf") ?? "";
            string storage = "auto";
            try { var d = V.G("/etc/systemd/journald.conf.d"); if (d.Exists && d.IsDir) foreach (var k in Vfs.Sorted(d.Node.Kids.Keys)) conf += "\n" + (V.Cat("/etc/systemd/journald.conf.d/" + k) ?? ""); } catch (VfsEx) { }
            foreach (var raw in conf.Split('\n')) { var l = raw.Trim(); if (l.StartsWith("Storage=", StringComparison.Ordinal)) storage = l.Substring(8).Trim(); }
            if (storage == "persistent") return true;
            if (storage == "volatile" || storage == "none") return false;
            return V.IsDirG("/var/log/journal");
        }

        // ------------------------------------------------------------ the boot
        public void Boot(string kernel, string args, bool rescueEntry)
        {
            BootLog.Clear();
            Services.Clear(); Procs.Clear(); Mounts.Clear(); Swaps.Clear(); RunFs.Clear(); Ifs.Clear(); FwRuntime.Clear(); FwRunning = false;
            SeBools.Clear(); Sysctl.Clear(); FailedMounts.Clear(); LoadedUnits.Clear();
            BootId++; BootTime = Now; Cmdline = "BOOT_IMAGE=(hd0,msdos1)/vmlinuz-" + kernel + " " + args;
            NextPid = 1;
            var a = Regex.Split(args.Trim(), "\\s+").ToList();
            // the journal: entries from volatile boots are gone
            if (!JournalPersistentThisBoot) Journal.Clear();
            Log("kernel", 0, 5, "Linux version " + kernel + " (mockbuild@cert-lab)", null);
            Log("kernel", 0, 6, "Command line: " + Cmdline, null);
            Spawn("root", "/usr/lib/systemd/systemd rhgb --switched-root --system --deserialize 31", "", "system_r:init_t").PPid = 0;
            NextPid = 2; Spawn("root", "[kthreadd]", "", "system_r:kernel_t").PPid = 0;   // PID 2, child of the kernel (0)
            NextPid = 600;

            // the root file system
            var rootLv = FindDev(RootDev);
            var rootC = ContentOf(rootLv);
            if (rootC == null) { State = "emergency"; BootLog.Add("[FAILED] Cannot find the root file system " + RootDev + "."); return; }
            Mounts.Add(new Mount { Path = "/", FsId = rootC.FsId, RootIno = 1, Source = RootDev, FsType = "xfs", Opts = "ro,relatime,seclabel,attr2,inode64,logbufs=8,logbsize=32k,noquota", Ro = true });
            MountPseudo();

            // SELinux mode
            string seConf = V.Cat("/etc/selinux/config") ?? "SELINUX=enforcing\n";
            var sm = Regex.Match(seConf, "(?m)^\\s*SELINUX\\s*=\\s*(\\w+)");
            SeMode = sm.Success ? sm.Groups[1].Value.ToLowerInvariant() : "enforcing";
            if (SeMode != "enforcing" && SeMode != "permissive" && SeMode != "disabled") SeMode = "enforcing";
            if (a.Contains("selinux=0")) SeMode = "disabled";
            else if (a.Contains("enforcing=0") && SeMode != "disabled") SeMode = "permissive";
            else if (a.Contains("enforcing=1") && SeMode != "disabled") SeMode = "enforcing";

            // breaking into the initramfs
            if (a.Contains("rd.break"))
            {
                Mounts.Clear();
                var initfs = new FileSys { Id = "initramfs", Type = "rootfs", Bytes = 300 * 1024 * 1024 };
                initfs.Nodes[1] = new Inode { Ino = 1, T = 'd', Mode = 0x1ED, Kids = new Dictionary<string, int>(StringComparer.Ordinal), Nlink = 2, Ctx = "system_u:object_r:root_t:s0" };
                RunFs[initfs.Id] = initfs;
                Mounts.Add(new Mount { Path = "/", FsId = initfs.Id, RootIno = 1, Source = "rootfs", FsType = "rootfs", Opts = "rw" });
                foreach (var d in new[] { "sysroot", "proc", "sys", "dev", "run", "etc", "usr", "bin", "sbin", "tmp" }) { var n = initfs.NewNode('d', 0x1ED, 0, 0, Now); n.Ctx = "system_u:object_r:root_t:s0"; initfs.Root.Kids[d] = n.Ino; }
                InitramfsTools(initfs);
                Mounts.Add(new Mount { Path = "/sysroot", FsId = rootC.FsId, RootIno = 1, Source = RootDev, FsType = "xfs", Opts = "ro,relatime,seclabel,attr2,inode64,logbufs=8,logbsize=32k,noquota", Ro = true });
                MountPseudo();
                State = "rdbreak";
                Log("dracut", 0, 5, "Breaking to emergency shell (rd.break)", null);
                return;
            }
            string init = a.FirstOrDefault(x => x.StartsWith("init=", StringComparison.Ordinal));
            if (init != null && (init.EndsWith("/bash", StringComparison.Ordinal) || init.EndsWith("/sh", StringComparison.Ordinal)))
            {
                State = "initbash";
                return;
            }
            ContinueAfterRoot(a, rescueEntry);
        }

        void InitramfsTools(FileSys fs)
        {
            // the initramfs has a small set of tools; /usr/bin there points at the same programs
            int ino;
            var usr = fs.Node(fs.Root.Kids["usr"]);
            var bin = fs.NewNode('d', 0x1ED, 0, 0, Now); usr.Kids["bin"] = bin.Ino;
            var sbin = fs.NewNode('d', 0x1ED, 0, 0, Now); usr.Kids["sbin"] = sbin.Ino;
            foreach (var p in new[] { "bash", "sh", "mount", "umount", "chroot", "ls", "cat", "echo", "touch", "vi", "journalctl", "reboot", "systemctl", "lvm", "lvs", "vgs", "pvs", "mkdir", "ln", "cp", "mv", "rm", "grep", "less", "load_policy", "passwd", "exit", "sed", "head", "tail" })
            {
                var n = fs.NewNode('f', 0x1ED, 0, 0, Now); n.Prog = p; n.Ctx = "system_u:object_r:bin_t:s0";
                bin.Kids[p] = n.Ino;
            }
            if (fs.Root.Kids.TryGetValue("bin", out ino)) { fs.Nodes.Remove(ino); fs.Root.Kids.Remove("bin"); }
            var l = fs.NewNode('l', 0x1FF, 0, 0, Now); l.Target = "usr/bin"; fs.Root.Kids["bin"] = l.Ino;
            if (fs.Root.Kids.TryGetValue("sbin", out ino)) { fs.Nodes.Remove(ino); fs.Root.Kids.Remove("sbin"); }
            var l2 = fs.NewNode('l', 0x1FF, 0, 0, Now); l2.Target = "usr/bin"; fs.Root.Kids["sbin"] = l2.Ino;
            var etc = fs.Node(fs.Root.Kids["etc"]);
            var f = fs.NewNode('f', 0x1A4, 0, 0, Now); f.Text = "root:x:0:0:root:/root:/bin/bash\n"; etc.Kids["passwd"] = f.Ino;
        }

        void MountPseudo()
        {
            Func<string, string, FileSys> pseudo = (type, ctx) =>
            {
                var fs = new FileSys { Id = type + NewHex(6), Type = type, Bytes = type == "tmpfs" ? 1900L * 1024 * 1024 : 0 };
                fs.Nodes[1] = new Inode { Ino = 1, T = 'd', Mode = type == "tmpfs" ? 0x1ED : 0x16D, Kids = new Dictionary<string, int>(StringComparer.Ordinal), Nlink = 2, Ctx = ctx, Mtime = Now, Ctime = Now, Atime = Now };
                RunFs[fs.Id] = fs;
                return fs;
            };
            string pre = Mounts.Any(m => m.Path == "/sysroot") ? "" : "";
            var proc = pseudo("proc", "system_u:object_r:proc_t:s0");
            Mounts.Add(new Mount { Path = "/proc", FsId = proc.Id, RootIno = 1, Source = "proc", FsType = "proc", Opts = "rw,nosuid,nodev,noexec,relatime" });
            var sys = pseudo("sysfs", "system_u:object_r:sysfs_t:s0");
            Mounts.Add(new Mount { Path = "/sys", FsId = sys.Id, RootIno = 1, Source = "sysfs", FsType = "sysfs", Opts = "rw,nosuid,nodev,noexec,relatime,seclabel" });
            var dev = pseudo("devtmpfs", "system_u:object_r:device_t:s0");
            Mounts.Add(new Mount { Path = "/dev", FsId = dev.Id, RootIno = 1, Source = "devtmpfs", FsType = "devtmpfs", Opts = "rw,nosuid,seclabel,size=4096k,nr_inodes=474658,mode=755,inode64" });
            foreach (var cd in new[] { "null", "zero", "random", "urandom", "tty", "console", "full" })
            {
                var n = dev.NewNode('c', cd == "console" ? 0x180 : 0x1B6, 0, cd == "tty" ? 5 : 0, Now); n.Dev = cd; n.Ctx = "system_u:object_r:" + (cd == "null" || cd == "zero" || cd == "full" ? "null_device_t" : cd.Contains("random") ? "random_device_t" : "tty_device_t") + ":s0";
                dev.Root.Kids[cd] = n.Ino;
            }
            foreach (var dd in new[] { "pts", "shm" }) { var n = dev.NewNode('d', 0x1ED, 0, 0, Now); n.Ctx = "system_u:object_r:device_t:s0"; dev.Root.Kids[dd] = n.Ino; }
            var run = pseudo("tmpfs", "system_u:object_r:var_run_t:s0");
            Mounts.Add(new Mount { Path = "/run", FsId = run.Id, RootIno = 1, Source = "tmpfs", FsType = "tmpfs", Opts = "rw,nosuid,nodev,seclabel,size=764600k,nr_inodes=819200,mode=755,inode64" });
            foreach (var dd in new[] { "user", "lock", "systemd", "media" }) { var n = run.NewNode('d', 0x1ED, 0, 0, Now); n.Ctx = "system_u:object_r:var_run_t:s0"; run.Root.Kids[dd] = n.Ino; }
            SyncDev();
        }

        /// <summary>After the root file system: relabel, fstab, network, services (also "exit" from rd.break / emergency).</summary>
        void ContinueAfterRoot(List<string> a, bool rescueEntry)
        {
            LoadSeBools();
            // relabel
            if (V.Exists("/.autorelabel") && SeMode != "disabled")
            {
                var root = Mounts.First(m => m.Path == "/");
                root.Ro = false;
                BootLog.Add("[    9.456789] selinux-autorelabel[612]: *** Warning -- SELinux targeted policy relabel is required.");
                BootLog.Add("[    9.456812] selinux-autorelabel[612]: *** Relabeling could take a very long time, depending on file");
                BootLog.Add("[    9.456830] selinux-autorelabel[612]: *** system size and speed of hard drives.");
                RelabelAll();
                V.RemoveG("/.autorelabel");
                BootLog.Add("[   21.873210] selinux-autorelabel[612]: Relabeling / /boot /dev /dev/hugepages /dev/mqueue /dev/pts /dev/shm /run ...");
                BootLog.Add("[   34.120053] systemd[1]: Rebooting after the relabel.");
                Log("selinux-autorelabel", 612, 5, "Relabeled the file systems (/.autorelabel).", "selinux-autorelabel.service");
                State = "grub";
                bool console = Sessions.Any(s => s.IsConsole);
                if (!console) Boot(Kernels[0], DefaultArgsFromEntry(), false);
                return;
            }
            string unitArg = a.FirstOrDefault(x => x.StartsWith("systemd.unit=", StringComparison.Ordinal));
            string target = unitArg != null ? unitArg.Substring(13) : DefaultTarget();
            if (a.Contains("emergency") || a.Contains("-b")) target = "emergency.target";
            if (a.Contains("rescue") || a.Contains("single") || a.Contains("s") || a.Contains("1") || rescueEntry && false) target = "rescue.target";
            BootTarget = target;
            var rootm = Mounts.First(m => m.Path == "/");
            if (target == "emergency.target")
            {
                // only the root file system, read-only
                State = "emergency"; Target = target;
                Log("systemd", 1, 6, "Started Emergency Shell.", "emergency.service");
                FinishJournal();
                return;
            }
            rootm.Ro = false; rootm.Opts = "rw" + rootm.Opts.Substring(2);
            // LVM: every volume group is activated
            foreach (var vg in Vgs) foreach (var lv in vg.Lvs) lv.Active = true;
            SyncDev();
            var critical = new List<string>();
            MountAll(true, critical, 1);
            FailedMounts = critical;
            var bootMount = Mounts.FirstOrDefault(m => m.Path == "/boot");
            Hostname = (V.Cat("/etc/hostname") ?? "").Trim();
            if (Hostname.Length == 0) Hostname = "localhost.localdomain";
            TransientHostname = "";
            NextPid = 640;
            Spawn("root", "/usr/lib/systemd/systemd-journald", "systemd-journald.service", "system_r:syslogd_t");
            Spawn("root", "/usr/lib/systemd/systemd-udevd", "systemd-udevd.service", "system_r:udev_t");
            Services["systemd-journald.service"] = new ServiceState { Unit = "systemd-journald.service", Active = "active", Sub = "running", Pid = Procs.Last().Pid - 1 >= 0 ? Procs[Procs.Count - 2].Pid : 0, Since = Now };
            Services["systemd-udevd.service"] = new ServiceState { Unit = "systemd-udevd.service", Active = "active", Sub = "running", Pid = Procs.Last().Pid, Since = Now };
            JournalPersistentThisBoot = JournalPersistent();
            if (critical.Count > 0)
            {
                foreach (var f in critical) { BootLog.Add("[FAILED] Failed to mount " + f + "."); }
                BootLog.Add("[DEPEND] Dependency failed for Local File Systems.");
                BootLog.Add("[  OK  ] Started Emergency Shell.");
                BootLog.Add("[  OK  ] Reached target Emergency Mode.");
                Log("systemd", 1, 3, "Dependency failed for Local File Systems.", "local-fs.target");
                Log("systemd", 1, 6, "Started Emergency Shell.", "emergency.service");
                State = "emergency"; Target = "emergency.target";
                FinishJournal();
                return;
            }
            if (target == "rescue.target")
            {
                State = "rescue"; Target = target;
                StartService("rsyslog.service", null);
                Log("systemd", 1, 6, "Started Rescue Shell.", "rescue.service");
                FinishJournal();
                return;
            }
            State = "running";
            Target = target;
            ApplySysctlFiles();
            // the services of the target (and what it pulls in)
            var order = new List<string> { "auditd.service", "firewalld.service", "NetworkManager.service", "rsyslog.service", "chronyd.service" };
            var wanted = WantedUnits(target);
            foreach (var u in order) if (wanted.Contains(u)) StartService(u, null);
            foreach (var u in wanted) if (!order.Contains(u)) StartService(u, null);
            if (!Services.ContainsKey("NetworkManager.service") || Services["NetworkManager.service"].Active != "active") ApplyNetworkFallback();
            MountAll(true, null, 2);   // remote-fs.target: NFS and _netdev mounts once the network is up
            StartLingerUsers();
            BootLog.Add("[  OK  ] Reached target " + TargetDescription(target) + ".");
            Log("systemd", 1, 6, "Reached target " + TargetDescription(target) + ".", target);
            Log("systemd", 1, 6, "Startup finished in 1.402s (kernel) + 3.218s (initrd) + 9.874s (userspace) = 14.495s.", null);
            FinishJournal();
            ChronyBoot();
        }

        public void FinishJournal()
        {
            JournalPersistentThisBoot = JournalPersistent();
            foreach (var e in Journal) if (e.Boot == BootId) e.Boot = BootId;
            if (JournalPersistentThisBoot) { try { V.EnsureDir("/var/log/journal/" + MachineId, 0x9ED, null); } catch (VfsEx) { } }
        }

        /// <summary>exit from rd.break / init=/bin/bash, or Ctrl+D / "systemctl default" in emergency mode.</summary>
        public void ContinueBoot()
        {
            if (State == "rdbreak" || State == "initbash")
            {
                // switch_root: the real root file system becomes / again
                var rootLv = FindDev(RootDev);
                var rootC = ContentOf(rootLv);
                Mounts.Clear(); RunFs.Clear();
                Mounts.Add(new Mount { Path = "/", FsId = rootC.FsId, RootIno = 1, Source = RootDev, FsType = "xfs", Opts = "ro,relatime,seclabel,attr2,inode64,logbufs=8,logbsize=32k,noquota", Ro = true });
                MountPseudo();
                BootLog.Clear();
                var a = Regex.Split(Cmdline.Trim(), "\\s+").Where(x => x != "rd.break" && !x.StartsWith("init=", StringComparison.Ordinal)).ToList();
                ContinueAfterRoot(a, false);
                return;
            }
            if (State == "emergency" || State == "rescue")
            {
                BootLog.Clear();
                var a = Regex.Split(Cmdline.Trim(), "\\s+").Where(x => !x.StartsWith("systemd.unit=", StringComparison.Ordinal) && x != "emergency" && x != "rescue" && x != "single").ToList();
                // keep what is mounted; try the rest again
                Mounts.RemoveAll(m => m.Path != "/" && m.FsType != "proc" && m.FsType != "sysfs" && m.FsType != "devtmpfs" && !(m.FsType == "tmpfs" && m.Path == "/run"));
                Swaps.Clear();
                ContinueAfterRoot(a, false);
            }
        }

        public void RelabelAll()
        {
            foreach (var m in Mounts.Where(x => x.FsType == "xfs" || x.FsType == "ext4").ToList())
            {
                var fs = V.Fs(m.FsId);
                if (fs == null) continue;
                RelabelTree(fs, fs.Node(m.RootIno), m.Path == "/" ? "" : m.Path);
            }
        }
        void RelabelTree(FileSys fs, Inode dir, string path)
        {
            if (dir == null) return;
            dir.Ctx = Selinux.WithType(dir.Ctx, Selinux.DefaultType(this, path.Length == 0 ? "/" : path));
            if (dir.T != 'd') return;
            foreach (var kv in dir.Kids)
            {
                var n = fs.Node(kv.Value);
                if (n == null) continue;
                string p = path + "/" + kv.Key;
                if (n.T == 'd')
                {
                    if (Mounts.Any(m => m.Path == p)) continue;
                    RelabelTree(fs, n, p);
                }
                else n.Ctx = Selinux.WithType(n.Ctx, Selinux.DefaultType(this, p));
            }
        }

        void ApplySysctlFiles()
        {
            var files = new List<string> { "/etc/sysctl.conf" };
            try { var d = V.G("/etc/sysctl.d"); if (d.Exists && d.IsDir) foreach (var k in Vfs.Sorted(d.Node.Kids.Keys)) if (k.EndsWith(".conf", StringComparison.Ordinal)) files.Add("/etc/sysctl.d/" + k); } catch (VfsEx) { }
            foreach (var f in files)
            {
                var t = V.Cat(f);
                if (t == null) continue;
                foreach (var raw in t.Split('\n'))
                {
                    var l = raw.Trim();
                    if (l.Length == 0 || l.StartsWith("#", StringComparison.Ordinal) || l.StartsWith(";", StringComparison.Ordinal)) continue;
                    int eq = l.IndexOf('=');
                    if (eq > 0) Sysctl[l.Substring(0, eq).Trim().Replace('/', '.')] = l.Substring(eq + 1).Trim();
                }
            }
        }

        public string DefaultTarget()
        {
            try
            {
                var l = V.GNoFollow("/etc/systemd/system/default.target");
                if (l.Exists && l.Node.T == 'l') return Vfs.Base(l.Node.Target);
            }
            catch (VfsEx) { }
            return "multi-user.target";
        }

        public static string TargetDescription(string t)
        {
            switch (t)
            {
                case "multi-user.target": return "Multi-User System";
                case "graphical.target": return "Graphical Interface";
                case "rescue.target": return "Rescue Mode";
                case "emergency.target": return "Emergency Mode";
                case "basic.target": return "Basic System";
                case "network-online.target": return "Network is Online";
                case "network.target": return "Network";
                case "local-fs.target": return "Local File Systems";
                case "remote-fs.target": return "Remote File Systems";
                case "timers.target": return "Timer Units";
                case "sockets.target": return "Socket Units";
                case "sysinit.target": return "System Initialization";
                case "default.target": return "Default";
                case "reboot.target": return "System Reboot";
                case "poweroff.target": return "System Power Off";
                case "shutdown.target": return "System Shutdown";
                case "getty.target": return "Login Prompts";
                case "swap.target": return "Swaps";
            }
            return t;
        }

        // ------------------------------------------------------------ logging in
        public bool Authenticate(string user, string pw)
        {
            var p = User(user);
            if (p == null) return false;
            // with SELinux enforcing, a shadow file that lost its label stops every login
            if (SeMode == "enforcing")
            {
                try { var l = V.G("/etc/shadow"); if (l.Exists && Selinux.TypeOf(l.Node.Ctx) != "shadow_t") { Log("audit", 0, 4, "avc:  denied  { read } for comm=\"unix_chkpwd\" name=\"shadow\" scontext=system_u:system_r:chkpwd_t:s0 tcontext=" + l.Node.Ctx + " tclass=file", null); AppendLog("/var/log/audit/audit.log", "type=AVC msg=audit(" + Now + ".001:" + (AuditSerial++) + "): avc:  denied  { read } for  pid=1501 comm=\"unix_chkpwd\" name=\"shadow\" dev=\"dm-0\" ino=" + l.Node.Ino + " scontext=system_u:system_r:chkpwd_t:s0-s0:c0.c1023 tcontext=" + l.Node.Ctx + " tclass=file permissive=0\n"); return false; } }
                catch (VfsEx) { }
            }
            var s = Shadow().FirstOrDefault(x => x.Name == user);
            if (s == null) return false;
            if (s.Hash.StartsWith("!", StringComparison.Ordinal) || s.Hash.StartsWith("*", StringComparison.Ordinal)) return false;
            long exp;
            if (long.TryParse(s.Expire, out exp) && exp >= 0 && DaysNow >= exp) { return false; }
            return CheckPassword(user, pw);
        }

        public string LastLoginText(string user, string tty)
        {
            var d = LocalTime(Now - 3600);
            return "Last login: " + d.ToString("ddd MMM", CultureInfo.InvariantCulture) + " " + d.Day.ToString().PadLeft(2) + " " + d.ToString("HH:mm:ss yyyy", CultureInfo.InvariantCulture) + " on " + tty;
        }

        public void AfterLoad()
        {
            if (Sessions == null) Sessions = new List<Session>();
        }
    }
}
