// Cert Lab - "installing" a machine: the disks (vda with /boot and an LVM volume group "rhel", two empty disks
// vdb and vdc), the root file system with the usual RHEL 9 layout and files, the base packages, two accounts
// (root / redhat and student / student, student in wheel) and the services a minimal install enables.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public partial class Machine
    {
        public const string RootPw = "redhat";
        public const string StudentPw = "student";

        public void Install(string hostname, string ip)
        {
            MachineId = NewHex(32);
            Macs.Clear(); Macs.Add("52:54:00:" + NewHex(2) + ":" + NewHex(2) + ":" + NewHex(2));
            NicNames.Clear(); NicNames.Add("enp1s0");
            // disks
            long gib = 1024L * 1024 * 1024;
            var vda = new Disk { Name = "vda", Bytes = 20 * gib, Label = "dos", DiskId = "0x" + NewHex(8), Index = 0 };
            vda.Parts.Add(new Part { Num = 1, Start = 2048, End = 2099199, Type = "83", Boot = true, PartUuid = vda.DiskId.Substring(2) + "-01" });
            vda.Parts.Add(new Part { Num = 2, Start = 2099200, End = 41943039, Type = "8e", PartUuid = vda.DiskId.Substring(2) + "-02" });
            Disks.Add(vda);
            Disks.Add(new Disk { Name = "vdb", Bytes = 5 * gib, Index = 1 });
            Disks.Add(new Disk { Name = "vdc", Bytes = 5 * gib, Index = 2 });
            Disks.Add(new Disk { Name = "sr0", Bytes = 10900000000L, Index = 9 });
            Content["sr0@0"] = new BlockContent { Kind = "iso9660", FsId = "dvd", Label = "RHEL-9-5-0-BaseOS-x86_64", Uuid = "2024-11-06-04-30-19-00" };
            var vg = new VolGroup { Name = "rhel", Uuid = PvUuid() };
            Content["vda@2099200"] = new BlockContent { Kind = "LVM2_member", Uuid = PvUuid() };
            vg.Pvs.Add("vda@2099200");
            Vgs.Add(vg);
            var swapLv = new Lv { Name = "swap", Uuid = PvUuid(), Dm = 1 };
            var rootLv = new Lv { Name = "root", Uuid = PvUuid(), Dm = 0 };
            vg.Lvs.Add(rootLv); vg.Lvs.Add(swapLv);
            long total = VgExtents(vg);
            Allocate(vg, swapLv, 512, null);
            Allocate(vg, rootLv, total - 512, null);
            // root: segments in the order the installer made them (swap first on disk, root after)
            NextDm = 2;
            var rootFs = NewFs("xfs", rootLv.Extents * vg.PeSize, "/dev/mapper/rhel-root");
            rootFs.BaseUsed = (long)(2.3 * gib);
            Content["lv:" + rootLv.Uuid] = new BlockContent { Kind = "xfs", FsId = rootFs.Id, Uuid = NewUuid() };
            var bootFs = NewFs("xfs", vda.Parts[0].Bytes, "/dev/vda1");
            bootFs.BaseUsed = 180L * 1024 * 1024;
            Content["vda@2048"] = new BlockContent { Kind = "xfs", FsId = bootFs.Id, Uuid = NewUuid() };
            Content["lv:" + swapLv.Uuid] = new BlockContent { Kind = "swap", Uuid = NewUuid() };
            Mounts.Clear();
            Mounts.Add(new Mount { Path = "/", FsId = rootFs.Id, RootIno = 1, Source = RootDev, FsType = "xfs", Opts = "rw" });
            rootFs.Root.Mode = 0x16D;   // / is dr-xr-xr-x on RHEL 9
            rootFs.Root.Ctx = "system_u:object_r:root_t:s0";
            Layout();
            Mounts.Add(new Mount { Path = "/boot", FsId = bootFs.Id, RootIno = 1, Source = "/dev/vda1", FsType = "xfs", Opts = "rw" });
            bootFs.Root.Ctx = "system_u:object_r:boot_t:s0"; bootFs.Root.Mode = 0x16D;
            BootFiles();
            BaseFiles(hostname, ip, Content["lv:" + rootLv.Uuid].Uuid, Content["vda@2048"].Uuid, Content["lv:" + swapLv.Uuid].Uuid);
            foreach (var p in Packages.All.Where(x => x.Base)) InstallPkg(p, "anaconda");
            // the graders' helper (checks of labs and exams use it; see certlab-check --help)
            var helper = V.Put("/usr/libexec/certlab/certlab-check", null, 0x1C0, 0, 0, null);
            helper.Text = null; helper.BinSize = 18000; helper.Prog = "certlab-check";
            var mk = V.Put("/usr/libexec/certlab/certlab-mkcert", null, 0x1C0, 0, 0, null);   // pretend certificates for the Security+ labs
            mk.Text = null; mk.BinSize = 9000; mk.Prog = "certlab-mkcert";
            Accounts();
            EnableDefaults();
            RelabelAll();
            Mounts.Clear();
        }

        void Layout()
        {
            foreach (var d in new[] { "/boot", "/dev", "/etc", "/home", "/media", "/mnt", "/opt", "/proc", "/run", "/srv", "/sys", "/usr", "/usr/bin", "/usr/sbin", "/usr/lib", "/usr/lib64", "/usr/libexec", "/usr/include", "/usr/local", "/usr/local/bin", "/usr/local/sbin", "/usr/local/lib", "/usr/local/share", "/usr/local/etc", "/usr/share", "/usr/share/doc", "/usr/share/man", "/usr/share/info", "/usr/share/zoneinfo", "/usr/share/dict", "/usr/lib/systemd/system", "/usr/lib/systemd/user", "/usr/lib/firewalld/zones", "/usr/lib/firewalld/services", "/var", "/var/lib", "/var/lib/rpm", "/var/lib/systemd/linger", "/var/log", "/var/log/audit", "/var/spool", "/var/spool/mail", "/var/cache", "/var/opt", "/var/local", "/etc/skel", "/etc/profile.d", "/etc/systemd/system", "/etc/systemd/user", "/etc/yum.repos.d", "/etc/pki/rpm-gpg", "/etc/sysctl.d", "/etc/security", "/etc/pam.d", "/etc/sysconfig", "/etc/modprobe.d", "/etc/X11", "/usr/lib/systemd/system-preset", "/etc/systemd/system/multi-user.target.wants", "/etc/systemd/system/timers.target.wants", "/etc/systemd/system/sockets.target.wants", "/etc/systemd/system/sysinit.target.wants", "/etc/systemd/system/getty.target.wants", "/etc/cron.d" })
                V.EnsureDir(d, 0x1ED, null);
            V.EnsureDir("/root", 0x168, null);        // dr-xr-x---
            V.G("/root").Node.Mode = 0x168;
            V.EnsureDir("/tmp", 0x3FF, null); V.G("/tmp").Node.Mode = 0x3FF;           // drwxrwxrwt
            V.EnsureDir("/var/tmp", 0x3FF, null); V.G("/var/tmp").Node.Mode = 0x3FF;
            V.G("/var/spool/mail").Node.Mode = 0x3FD; V.G("/var/spool/mail").Node.Gid = 12;
            V.G("/boot").Node.Mode = 0x16D;
            V.G("/proc").Node.Mode = 0x16D; V.G("/sys").Node.Mode = 0x16D;
            V.PutLink("/bin", "usr/bin"); V.PutLink("/sbin", "usr/sbin"); V.PutLink("/lib", "usr/lib"); V.PutLink("/lib64", "usr/lib64");
            V.PutLink("/var/run", "../run"); V.PutLink("/var/lock", "../run/lock"); V.PutLink("/var/mail", "spool/mail");
        }

        void BootFiles()
        {
            foreach (var k in Kernels)
            {
                V.Put("/boot/vmlinuz-" + k, null, 0x1ED, 0, 0, null).BinSize = 12800000;
                V.Put("/boot/initramfs-" + k + ".img", null, 0x180, 0, 0, null).BinSize = 34000000;
                V.Put("/boot/System.map-" + k, null, 0x180, 0, 0, null).BinSize = 8000000;
                V.Put("/boot/config-" + k, "# Linux/x86_64 " + k + " Kernel Configuration\nCONFIG_XFS_FS=m\nCONFIG_EXT4_FS=m\nCONFIG_SECURITY_SELINUX=y\n");
                V.Put("/boot/loader/entries/" + MachineId + "-" + k + ".conf", "title Red Hat Enterprise Linux (" + k + ") 9." + (k.Contains("el9_5") ? "5" : "4") + " (Plow)\nversion " + k + "\nlinux /vmlinuz-" + k + "\ninitrd /initramfs-" + k + ".img $tuned_initrd\noptions root=" + RootDev + " ro crashkernel=1G-4G:192M,4G-64G:256M,64G-:512M resume=/dev/mapper/rhel-swap rd.lvm.lv=rhel/root rd.lvm.lv=rhel/swap rhgb quiet $tuned_params\ngrub_users $grub_users\ngrub_arg --unrestricted\ngrub_class rhel\n");
            }
            string rk = "0-rescue-" + MachineId;
            V.Put("/boot/vmlinuz-" + rk, null, 0x1ED, 0, 0, null).BinSize = 12800000;
            V.Put("/boot/initramfs-" + rk + ".img", null, 0x180, 0, 0, null).BinSize = 90000000;
            V.Put("/boot/loader/entries/" + MachineId + "-0-rescue.conf", "title Red Hat Enterprise Linux (0-rescue-" + MachineId + ") 9.5 (Plow)\nversion 0-rescue-" + MachineId + "\nlinux /vmlinuz-" + rk + "\ninitrd /initramfs-" + rk + ".img\noptions root=" + RootDev + " ro crashkernel=1G-4G:192M,4G-64G:256M,64G-:512M resume=/dev/mapper/rhel-swap rd.lvm.lv=rhel/root rd.lvm.lv=rhel/swap rhgb quiet\ngrub_users $grub_users\ngrub_arg --unrestricted\ngrub_class rhel\n");
            V.Put("/boot/grub2/grubenv", "# GRUB Environment Block\n# WARNING: Do not edit this file by tools other than grub-editenv!!!\nsaved_entry=" + MachineId + "-" + Kernels[0] + "\nmenu_auto_hide=1\nboot_success=1\n" + new string('#', 512) + "\n", 0x180, 0, 0, null);
            V.Put("/boot/grub2/grub.cfg", GrubCfg(), 0x180, 0, 0, null);
        }

        public string GrubCfg()
        {
            string def = V == null ? "" : (V.Cat("/etc/default/grub") ?? "");
            return "#\n# DO NOT EDIT THIS FILE\n#\n# It is automatically generated by grub2-mkconfig using templates\n# from /etc/grub.d and settings from /etc/default/grub\n#\n\n### BEGIN /etc/grub.d/00_header ###\nset pager=1\n\nif [ -f ${config_directory}/grubenv ]; then\n  load_env -f ${config_directory}/grubenv\nfi\nset default=\"${saved_entry}\"\n\nterminal_output console\nset timeout_style=menu\nset timeout=" + GrubTimeout() + "\n### END /etc/grub.d/00_header ###\n\n### BEGIN /etc/grub.d/10_linux ###\ninsmod part_msdos\ninsmod xfs\nset root='hd0,msdos1'\nset kernelopts=\"root=" + RootDev + " ro " + GrubCmdline() + " \"\ninsmod blscfg\nblscfg\n### END /etc/grub.d/10_linux ###\n";
        }
        public string GrubTimeout()
        {
            var t = V.Cat("/etc/default/grub") ?? "";
            var m = System.Text.RegularExpressions.Regex.Match(t, "(?m)^GRUB_TIMEOUT=\"?(\\d+)");
            return m.Success ? m.Groups[1].Value : "5";
        }
        public string GrubCmdline()
        {
            var t = V.Cat("/etc/default/grub") ?? "";
            var m = System.Text.RegularExpressions.Regex.Match(t, "(?m)^GRUB_CMDLINE_LINUX=\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : "";
        }

        void BaseFiles(string hostname, string ip, string rootUuid, string bootUuid, string swapUuid)
        {
            V.Put("/etc/hostname", hostname + "\n");
            V.Put("/etc/hosts", "127.0.0.1   localhost localhost.localdomain localhost4 localhost4.localdomain4\n::1         localhost localhost.localdomain localhost6 localhost6.localdomain6\n");
            V.Put("/etc/fstab", "\n#\n# /etc/fstab\n# Created by anaconda on Mon Mar  2 08:41:17 2026\n#\n# Accessible filesystems, by reference, are maintained under '/dev/disk/'.\n# See man pages fstab(5), findfs(8), mount(8) and/or blkid(8) for more info.\n#\n# After editing this file, run 'systemctl daemon-reload' to update systemd\n# units generated from this file.\n#\n/dev/mapper/rhel-root   /                       xfs     defaults        0 0\nUUID=" + bootUuid + " /boot                   xfs     defaults        0 0\n/dev/mapper/rhel-swap   none                    swap    defaults        0 0\n");
            V.Put("/etc/resolv.conf", "# Generated by NetworkManager\nsearch lab.example.com\nnameserver 192.168.50.254\n");
            V.Put("/etc/nsswitch.conf", "# Generated by authselect\n# Do not modify this file manually, use authselect instead. Any user changes will be overwritten.\n# You can stop authselect from managing your configuration by calling 'authselect opt-out'.\n# See authselect(8) for more details.\n\npasswd:     files systemd\ngroup:      files systemd\nnetgroup:   files\nautomount:  files\nservices:   files\nsudoers:    files\nshadow:     files\nhosts:      files dns myhostname\n");
            V.Put("/etc/shells", "/bin/sh\n/bin/bash\n/usr/bin/sh\n/usr/bin/bash\n");
            V.Put("/etc/motd", "");
            V.Put("/etc/issue", "\\S\nKernel \\r on an \\m\n\n");
            V.Put("/etc/issue.net", "\\S\nKernel \\r on an \\m\n");
            V.Put("/etc/machine-id", MachineId + "\n", 0x124, 0, 0, null);
            V.Put("/etc/locale.conf", "LANG=\"en_US.UTF-8\"\n");
            V.Put("/etc/vconsole.conf", "KEYMAP=\"us\"\n");
            V.Put("/etc/adjtime", "0.0 0 0.0\n0\nUTC\n");
            V.Put("/etc/environment", "");
            V.Put("/etc/sysctl.conf", "# sysctl settings are defined through files in\n# /usr/lib/sysctl.d/, /run/sysctl.d/, and /etc/sysctl.d/.\n#\n# Vendors settings live in /usr/lib/sysctl.d/.\n# To override a whole file, create a new file with the same in\n# /etc/sysctl.d/ and put new settings there. To override\n# only specific settings, add a file with a lexically later\n# name in /etc/sysctl.d/ and put new settings there.\n#\n# For more information, see sysctl.conf(5) and sysctl.d(5).\n");
            V.Put("/etc/profile", "# /etc/profile\n\n# System wide environment and startup programs, for login setup\n# Functions and aliases go in /etc/bashrc\n\n# It's NOT a good idea to change this file unless you know what you\n# are doing. It's much better to create a custom.sh shell script in\n# /etc/profile.d/ to make custom changes to your environment, as this\n# will prevent the need for merging in future updates.\n\nHISTSIZE=1000\nexport PATH USER LOGNAME MAIL HOSTNAME HISTSIZE HISTCONTROL\n");
            V.Put("/etc/bashrc", "# /etc/bashrc\n\n# System wide functions and aliases\n# Environment stuff goes in /etc/profile\n\n# Prompt: [user@host dir]$\n");
            V.PutLink("/etc/system-release", "redhat-release");
            V.PutLink("/etc/localtime", "../usr/share/zoneinfo/America/New_York");
            V.PutLink("/etc/grub2.cfg", "../boot/grub2/grub.cfg");
            V.Put("/etc/subuid", "student:100000:65536\n");
            V.Put("/etc/subgid", "student:100000:65536\n");
            V.Put("/etc/security/limits.conf", "# /etc/security/limits.conf\n#\n#This file sets the resource limits for the users logged in via PAM.\n#<domain>        <type>  <item>  <value>\n#\n#*               soft    core            0\n#@student        hard    nproc           20\n\n# End of file\n");
            V.Put("/etc/pam.d/system-auth", "# Generated by authselect\nauth        required      pam_env.so\nauth        sufficient    pam_unix.so try_first_pass nullok\nauth        required      pam_deny.so\npassword    requisite     pam_pwquality.so local_users_only\npassword    sufficient    pam_unix.so sha512 shadow nullok use_authtok\n");
            V.Put("/etc/security/pwquality.conf", "# Configuration for systemwide password quality limits\n# Defaults:\n#\n# Minimum acceptable size for the new password (plus one if\n# credits are not disabled which is the default). (See pam_cracklib manual.)\n# Cannot be set to lower value than 6.\n# minlen = 8\n#\n# The maximum credit for having digits in the new password. If less than 0\n# it is the minimum number of digits in the new password.\n# dcredit = 0\n");
            V.Put("/etc/skel/.bash_logout", "# ~/.bash_logout\n\n");
            V.Put("/etc/skel/.bash_profile", "# .bash_profile\n\n# Get the aliases and functions\nif [ -f ~/.bashrc ]; then\n\t. ~/.bashrc\nfi\n\n# User specific environment and startup programs\n");
            V.Put("/etc/skel/.bashrc", "# .bashrc\n\n# Source global definitions\nif [ -f /etc/bashrc ]; then\n\t. /etc/bashrc\nfi\n\n# User specific environment\nif ! [[ \"$PATH\" =~ \"$HOME/.local/bin:$HOME/bin:\" ]]; then\n    PATH=\"$HOME/.local/bin:$HOME/bin:$PATH\"\nfi\nexport PATH\n\n# Uncomment the following line if you don't like systemctl's auto-paging feature:\n# export SYSTEMD_PAGER=\n\n# User specific aliases and functions\n");
            foreach (var f in new[] { "/etc/skel/.bash_logout", "/etc/skel/.bash_profile", "/etc/skel/.bashrc" }) V.G(f).Node.Mode = 0x1A4;
            V.Put("/etc/yum.repos.d/redhat.repo", "#\n# Certificate-Based Repositories\n# Managed by (rhsm) subscription-manager\n#\n# *** This file is auto-generated.  Changes made here will be over-written. ***\n# *** Use \"subscription-manager repo-override --help\" if you wish to make changes. ***\n#\n# If this file is empty and this system is subscribed consider\n# a \"yum repolist\" to refresh available repos\n#\n");
            // network: the profile the installer wrote
            var nm = new StringBuilder();
            nm.Append("[connection]\nid=enp1s0\nuuid=" + NewUuid() + "\ntype=ethernet\nautoconnect-priority=-999\ninterface-name=enp1s0\ntimestamp=1772440800\n\n[ethernet]\n\n[ipv4]\naddress1=" + ip + "/24\ndns=192.168.50.254;\ndns-search=lab.example.com;\ngateway=192.168.50.254\nmethod=manual\n\n[ipv6]\naddr-gen-mode=eui64\nmethod=auto\n\n[proxy]\n");
            V.Put("/etc/NetworkManager/system-connections/enp1s0.nmconnection", nm.ToString(), 0x180, 0, 0, null);
            // time zones
            foreach (var z in Tz.Zones) V.Put("/usr/share/zoneinfo/" + z[0], null, 0x1A4, 0, 0, null).BinSize = 2200;
            V.Put("/usr/share/dict/README", "Install the words package for /usr/share/dict/words.\n");
            // firewalld's built-in zones and services
            foreach (var z in Firewall.Zones) V.Put("/usr/lib/firewalld/zones/" + z.Name + ".xml", Firewall.ZoneXml(z));
            foreach (var s in Firewall.ServiceDefs) V.Put("/usr/lib/firewalld/services/" + s[0] + ".xml", Firewall.ServiceXml(s));
            // the root user's files
            V.Put("/root/anaconda-ks.cfg", "# Generated by Anaconda 34.25.5.9\n# Generated by pykickstart v3.32\n#version=RHEL9\n# Use graphical install\ngraphical\n\n%packages\n@^minimal-environment\n\n%end\n\n# Keyboard layouts\nkeyboard --xlayouts='us'\n# System language\nlang en_US.UTF-8\n\n# Network information\nnetwork  --bootproto=static --device=enp1s0 --ip=" + ip + " --netmask=255.255.255.0 --gateway=192.168.50.254 --nameserver=192.168.50.254 --activate\nnetwork  --hostname=" + hostname + "\n\n# Use hard drive installation media\nharddrive --dir= --partition=LABEL=RHEL-9-5-0-BaseOS-x86_64\n\n# Run the Setup Agent on first boot\nfirstboot --enable\n\n# Generated using Blivet version 3.6.0\nignoredisk --only-use=vda\nautopart\n# Partition clearing information\nclearpart --none --initlabel\n\n# System timezone\ntimezone America/New_York --utc\n\n# Root password\nrootpw --iscrypted --allow-ssh (hash not shown)\nuser --groups=wheel --name=student --password=(hash not shown) --iscrypted --gecos=\"Student\"\n", 0x180, 0, 0, null);
            foreach (var f in new[] { ".bash_logout", ".bash_profile", ".bashrc" }) V.Put("/root/" + f, V.Cat("/etc/skel/" + f).Replace("# User specific aliases and functions\n", "# User specific aliases and functions\n\nalias rm='rm -i'\nalias cp='cp -i'\nalias mv='mv -i'\n"), 0x1A4, 0, 0, null);
            V.Put("/root/.cshrc", "# .cshrc\n");
            V.Put("/root/.tcshrc", "# .tcshrc\n");
            // logs
            V.Put("/var/log/messages", "", 0x180, 0, 0, null);
            V.Put("/var/log/secure", "", 0x180, 0, 0, null);
            V.Put("/var/log/cron", "", 0x180, 0, 0, null);
            V.Put("/var/log/maillog", "", 0x180, 0, 0, null);
            V.Put("/var/log/boot.log", "", 0x180, 0, 0, null);
            V.Put("/var/log/spooler", "", 0x180, 0, 0, null);
            V.Put("/var/log/dnf.log", "", 0x1A4, 0, 0, null);
            V.Put("/var/log/audit/audit.log", "", 0x180, 0, 0, null);
            V.Put("/var/log/lastlog", null, 0x1B4, 0, 22, null).BinSize = 292292;
            V.Put("/var/log/wtmp", null, 0x1B4, 0, 22, null).BinSize = 5760;
            V.Put("/var/log/btmp", null, 0x1B0, 0, 22, null).BinSize = 0;
            V.EnsureDir("/var/log/tuned", 0x1ED, null);
            V.EnsureDir("/var/log/chrony", 0x1ED, null);
            V.Put("/var/lib/systemd/linger/.keep", "");
            V.RemoveG("/var/lib/systemd/linger/.keep");
            // documentation for the "files in /usr/share/doc" objective
            V.Put("/usr/share/doc/README.certlab", "Packages put their documentation in /usr/share/doc/<package>/.\nTry: ls /usr/share/doc   and   less /usr/share/doc/chrony/FAQ\n");
            V.Put("/usr/share/info/dir", "This is the file .../info/dir, which contains the topmost node of the Info hierarchy.\n");
        }

        void Accounts()
        {
            var pw = new List<PwEnt>
            {
                new PwEnt { Name = "root", Uid = 0, Gid = 0, Gecos = "root", Home = "/root", Shell = "/bin/bash" },
                new PwEnt { Name = "bin", Uid = 1, Gid = 1, Gecos = "bin", Home = "/bin", Shell = "/sbin/nologin" },
                new PwEnt { Name = "daemon", Uid = 2, Gid = 2, Gecos = "daemon", Home = "/sbin", Shell = "/sbin/nologin" },
                new PwEnt { Name = "adm", Uid = 3, Gid = 4, Gecos = "adm", Home = "/var/adm", Shell = "/sbin/nologin" },
                new PwEnt { Name = "lp", Uid = 4, Gid = 7, Gecos = "lp", Home = "/var/spool/lpd", Shell = "/sbin/nologin" },
                new PwEnt { Name = "sync", Uid = 5, Gid = 0, Gecos = "sync", Home = "/sbin", Shell = "/bin/sync" },
                new PwEnt { Name = "shutdown", Uid = 6, Gid = 0, Gecos = "shutdown", Home = "/sbin", Shell = "/sbin/shutdown" },
                new PwEnt { Name = "halt", Uid = 7, Gid = 0, Gecos = "halt", Home = "/sbin", Shell = "/sbin/halt" },
                new PwEnt { Name = "mail", Uid = 8, Gid = 12, Gecos = "mail", Home = "/var/spool/mail", Shell = "/sbin/nologin" },
                new PwEnt { Name = "operator", Uid = 11, Gid = 0, Gecos = "operator", Home = "/root", Shell = "/sbin/nologin" },
                new PwEnt { Name = "games", Uid = 12, Gid = 100, Gecos = "games", Home = "/usr/games", Shell = "/sbin/nologin" },
                new PwEnt { Name = "ftp", Uid = 14, Gid = 50, Gecos = "FTP User", Home = "/var/ftp", Shell = "/sbin/nologin" },
                new PwEnt { Name = "nobody", Uid = 65534, Gid = 65534, Gecos = "Kernel Overflow User", Home = "/", Shell = "/sbin/nologin" },
                new PwEnt { Name = "systemd-coredump", Uid = 999, Gid = 997, Gecos = "systemd Core Dumper", Home = "/", Shell = "/sbin/nologin" },
                new PwEnt { Name = "dbus", Uid = 81, Gid = 81, Gecos = "System message bus", Home = "/", Shell = "/sbin/nologin" },
                new PwEnt { Name = "polkitd", Uid = 998, Gid = 996, Gecos = "User for polkitd", Home = "/", Shell = "/sbin/nologin" },
                new PwEnt { Name = "tss", Uid = 59, Gid = 59, Gecos = "Account used for TPM access", Home = "/", Shell = "/usr/sbin/nologin" },
                new PwEnt { Name = "sssd", Uid = 997, Gid = 995, Gecos = "User for sssd", Home = "/", Shell = "/sbin/nologin" },
                new PwEnt { Name = "chrony", Uid = 996, Gid = 994, Gecos = "chrony system user", Home = "/var/lib/chrony", Shell = "/sbin/nologin" },
                new PwEnt { Name = "sshd", Uid = 74, Gid = 74, Gecos = "Privilege-separated SSH", Home = "/usr/share/empty.sshd", Shell = "/sbin/nologin" },
                new PwEnt { Name = "rpc", Uid = 32, Gid = 32, Gecos = "Rpcbind Daemon", Home = "/var/lib/rpcbind", Shell = "/sbin/nologin" },
                new PwEnt { Name = "rpcuser", Uid = 29, Gid = 29, Gecos = "RPC Service User", Home = "/var/lib/nfs", Shell = "/sbin/nologin" },
                new PwEnt { Name = "tcpdump", Uid = 72, Gid = 72, Gecos = "", Home = "/", Shell = "/sbin/nologin" },
                new PwEnt { Name = "student", Uid = 1000, Gid = 1000, Gecos = "Student", Home = "/home/student", Shell = "/bin/bash" },
            };
            WritePasswd(pw);
            var gr = new List<GrEnt>();
            foreach (var g in new[] { "root:0", "bin:1", "daemon:2", "sys:3", "adm:4", "tty:5", "disk:6", "lp:7", "mem:8", "kmem:9", "wheel:10", "cdrom:11", "mail:12", "man:15", "dialout:18", "floppy:19", "games:20", "tape:33", "video:39", "ftp:50", "lock:54", "audio:63", "users:100", "nobody:65534", "utmp:22", "utempter:35", "input:999", "kvm:36", "render:998", "systemd-journal:190", "systemd-coredump:997", "dbus:81", "polkitd:996", "printadmin:995", "tss:59", "ssh_keys:101", "sssd:993", "chrony:994", "sshd:74", "rpc:32", "rpcuser:29", "tcpdump:72", "student:1000" })
            {
                var p = g.Split(':');
                var e = new GrEnt { Name = p[0], Gid = int.Parse(p[1], CultureInfo.InvariantCulture) };
                if (e.Name == "wheel") e.Members.Add("student");
                gr.Add(e);
            }
            WriteGroups(gr);
            var sh = new List<ShEnt>();
            long days = DaysNow - 1;
            foreach (var p in pw)
            {
                var s = new ShEnt { Name = p.Name, Hash = p.Uid == 0 || p.Uid >= 1000 && p.Uid < 65534 ? "" : (p.Uid < 200 ? "*" : "!!"), LastChg = days.ToString(CultureInfo.InvariantCulture), Min = p.Uid >= 1000 && p.Uid < 65534 ? "0" : "", Max = p.Uid >= 1000 && p.Uid < 65534 ? "99999" : "", Warn = p.Uid >= 1000 && p.Uid < 65534 ? "7" : "" };
                if (p.Uid == 0) { s.Min = "0"; s.Max = "99999"; s.Warn = "7"; }
                sh.Add(s);
            }
            WriteShadow(sh);
            V.G("/etc/shadow").Node.Mode = 0; V.G("/etc/gshadow").Node.Mode = 0;
            SetPassword("root", RootPw);
            SetPassword("student", StudentPw);
            foreach (var s in Shadow()) { }
            var l2 = Shadow(); foreach (var s in l2) if (s.Name == "root" || s.Name == "student") s.LastChg = days.ToString(CultureInfo.InvariantCulture); WriteShadow(l2);
            MakeHome("student", 1000, 1000);
        }

        public void MakeHome(string user, int uid, int gid)
        {
            string h = "/home/" + user;
            var n = V.EnsureDir(h, 0x1C0, null);
            n.Uid = uid; n.Gid = gid; n.Mode = 0x1C0;
            foreach (var f in new[] { ".bash_logout", ".bash_profile", ".bashrc" })
            {
                var c = V.Put(h + "/" + f, V.Cat("/etc/skel/" + f) ?? "", 0x1A4, uid, gid, null);
            }
            V.Put("/var/spool/mail/" + user, "", 0x1B0, uid, 12, null);
        }

        public void AddSystemUser(string name, int uid, int gid, string gecos, string home, string shell)
        {
            var pw = Passwd();
            if (pw.Any(p => p.Name == name)) return;
            var gr = Groups();
            if (!gr.Any(g => g.Name == name)) { gr.Add(new GrEnt { Name = name, Gid = gid }); WriteGroups(gr); }
            pw.Add(new PwEnt { Name = name, Uid = uid, Gid = gid, Gecos = gecos, Home = home, Shell = shell });
            WritePasswd(pw);
            var sh = Shadow(); sh.Add(new ShEnt { Name = name, Hash = "!!", LastChg = DaysNow.ToString(CultureInfo.InvariantCulture) }); WriteShadow(sh);
        }

        void EnableDefaults()
        {
            foreach (var u in new[] { "sshd.service", "crond.service", "atd.service", "chronyd.service", "firewalld.service", "NetworkManager.service", "rsyslog.service", "tuned.service", "auditd.service" })
                V.PutLink("/etc/systemd/system/multi-user.target.wants/" + u, "/usr/lib/systemd/system/" + u);
            V.PutLink("/etc/systemd/system/timers.target.wants/logrotate.timer", "/usr/lib/systemd/system/logrotate.timer");
            V.PutLink("/etc/systemd/system/dbus-org.fedoraproject.FirewallD1.service", "/usr/lib/systemd/system/firewalld.service");
            V.PutLink("/etc/systemd/system/default.target", "/usr/lib/systemd/system/multi-user.target");
            foreach (var t in new[] { "basic.target", "sysinit.target", "network.target", "network-online.target", "local-fs.target", "remote-fs.target", "timers.target", "sockets.target", "getty.target", "swap.target", "reboot.target", "poweroff.target", "shutdown.target", "nss-lookup.target", "time-sync.target" })
                V.Put("/usr/lib/systemd/system/" + t, "[Unit]\nDescription=" + TargetDescription(t) + "\nDocumentation=man:systemd.special(7)\n");
            V.Put("/usr/lib/systemd/system/emergency.service", "[Unit]\nDescription=Emergency Shell\nDocumentation=man:sulogin(8)\nDefaultDependencies=no\n\n[Service]\nExecStart=-/usr/lib/systemd/systemd-sulogin-shell emergency\nType=idle\n");
            V.Put("/usr/lib/systemd/system/rescue.service", "[Unit]\nDescription=Rescue Shell\nDocumentation=man:sulogin(8)\nDefaultDependencies=no\n\n[Service]\nExecStart=-/usr/lib/systemd/systemd-sulogin-shell rescue\nType=idle\n");
            V.Put("/usr/lib/systemd/user/default.target", "[Unit]\nDescription=Main User Target\nDocumentation=man:systemd.special(7)\nRequires=basic.target\nAllowIsolate=yes\n");
            V.Put("/usr/lib/systemd/system/user@.service", "[Unit]\nDescription=User Manager for UID %i\nDocumentation=man:user@.service(5)\nAfter=user-runtime-dir@%i.service dbus.service systemd-oomd.service\n\n[Service]\nUser=%i\nPAMName=systemd-user\nType=notify-reload\nExecStart=/usr/lib/systemd/systemd --user\n");
        }
    }
}
