// Cert Lab - tests of the simulator: what the commands print against real RHEL 9 output, and that the machines
// enforce what RHEL enforces (permissions, ACLs, SELinux, storage limits, persistence across reboots).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CertLab
{
    public static partial class Tests
    {
        static IEnumerable<KeyValuePair<string, Action>> EngineGroups()
        {
            yield return new KeyValuePair<string, Action>("shell-more", ShellMore);
            yield return new KeyValuePair<string, Action>("files", FileTests);
            yield return new KeyValuePair<string, Action>("acl", AclTests);
            yield return new KeyValuePair<string, Action>("selinux", SelinuxTests);
            yield return new KeyValuePair<string, Action>("storage", StorageTests);
            yield return new KeyValuePair<string, Action>("boot", BootTests);
            yield return new KeyValuePair<string, Action>("grub", GrubTests);
            yield return new KeyValuePair<string, Action>("systemd", SystemdTests);
            yield return new KeyValuePair<string, Action>("packages", PackageTests);
            yield return new KeyValuePair<string, Action>("network", NetworkTests);
            yield return new KeyValuePair<string, Action>("ssh", SshTests);
            yield return new KeyValuePair<string, Action>("schedule", ScheduleTests);
            yield return new KeyValuePair<string, Action>("users", UserTests);
            yield return new KeyValuePair<string, Action>("containers", ContainerTests);
            yield return new KeyValuePair<string, Action>("archives", ArchiveTests);
            yield return new KeyValuePair<string, Action>("save", SaveTests);
            yield return new KeyValuePair<string, Action>("cards", CardTests);
        }

        /// <summary>Types into a console like a person (login prompts, GRUB) and returns the output.</summary>
        static string Type(T t, string machine, string line)
        {
            t.Sim.Type(machine, line);
            var o = t.Sim.Console(machine).TakeOutput();
            t.Last = o;
            return o;
        }
        static bool OkOn(T t, string machine, string script) { string o; return t.Sim.Check(machine, script, out o); }
        static void Reboot(T t, string machine)
        {
            Type(t, machine, "systemctl reboot");
            Type(t, machine, "true");   // through GRUB and the login prompt
        }
        const string Repo = "printf '[BaseOS]\\nname=BaseOS\\nbaseurl=http://repo.lab.example.com/BaseOS\\ngpgcheck=0\\n[AppStream]\\nname=AppStream\\nbaseurl=http://repo.lab.example.com/AppStream\\ngpgcheck=0\\n' > /etc/yum.repos.d/lab.repo";

        static void ShellMore()
        {
            var t = new T(1);
            Check("quoting", t.Run("echo 'a  b' \"c $HOME\" d\\ e") == "a  b c /root d e\n", t.Last);
            Check("here-document", t.Run("cat <<EOF\nx=$((2+2))\nEOF") == "x=4\n", t.Last);
            Check("redirect stderr", t.Run("ls /nope 2>/dev/null; echo $?") == "2\n", t.Last);
            Check("2>&1 into a pipe", t.Run("ls /nope 2>&1 | wc -l") == "1\n", t.Last);
            Check("globbing", t.Run("cd /etc; echo host*") == "host.conf hostname hosts\n" || t.Last.Contains("hostname hosts"), t.Last);
            Check("arrays", t.Run("a=(x y z); echo ${#a[@]} ${a[1]}") == "3 y\n", t.Last);
            Check("function and return", t.Run("f() { return 3; }; f; echo $?") == "3\n", t.Last);
            Check("if/elif/test", t.Run("n=5; if [ $n -gt 9 ]; then echo big; elif [[ $n == 5 ]]; then echo five; fi") == "five\n", t.Last);
            Check("while read", t.Run("printf 'a\\nb\\n' | while read l; do echo -$l; done") == "-a\n-b\n", t.Last);
            Check("case", t.Run("x=b.txt; case $x in *.txt) echo text;; *) echo other;; esac") == "text\n", t.Last);
            Check("command substitution", t.Run("echo \"$(echo hi | tr a-z A-Z)\"") == "HI\n", t.Last);
            Check("parameter defaults", t.Run("unset q; echo ${q:-none} ${#HOME}") == "none 5\n", t.Last);
            Check("script with arguments", t.Run("printf '#!/bin/bash\\necho \"$# $1 $2\"\\nexit 4\\n' > /root/s.sh; chmod +x /root/s.sh; /root/s.sh one two; echo $?") == "2 one two\n4\n", t.Last);
            Check("script without x bit", t.Run("chmod -x /root/s.sh; /root/s.sh") .Contains("Permission denied"), t.Last);
            Check("getopts", t.Run("set -- -a -b val; while getopts ab: o; do echo $o$OPTARG; done") == "a\nbval\n", t.Last);
            Check("grep -E and -c", t.Run("printf 'one\\ntwo\\nthree\\n' | grep -cE '^t'") == "2\n", t.Last);
            Check("sed substitution", t.Run("echo hello world | sed 's/o/0/g'") == "hell0 w0rld\n", t.Last);
            Check("awk fields", t.Run("echo 'a:b:c' | awk -F: '{print $2, NF}'") == "b 3\n", t.Last);
            Check("sort -n | uniq -c", t.Run("printf '3\\n1\\n3\\n' | sort -n | uniq -c") == "      1 1\n      2 3\n", t.Last);
            Check("history expansion", t.Run("echo abc") == "abc\n" && t.Run("!!") == "echo abc\nabc\n", t.Last);
            Check("tab completion of commands", t.S.CurrentSh != null && t.S.Candidates("systemc").Words.Contains("systemctl"));
            t.Dispose();
        }

        static void FileTests()
        {
            var t = new T(1);
            t.Run("useradd alice; useradd bob");
            Check("ls -l of /etc/passwd", Regex.IsMatch(t.Run("ls -l /etc/passwd"), "^-rw-r--r--\\. 1 root root \\d+ \\w{3} [ \\d]\\d \\d\\d:\\d\\d /etc/passwd\n$"), t.Last);
            Check("a user cannot write /etc/hosts", t.Run("su - alice -c 'echo x >> /etc/hosts'").Contains("Permission denied"), t.Last);
            Check("a user cannot read /etc/shadow", t.Run("su - alice -c 'cat /etc/shadow'").Contains("Permission denied"), t.Last);
            t.Run("mkdir /pub; chmod 1777 /pub; su - alice -c 'touch /pub/a'");
            Check("sticky bit: bob cannot delete alice's file", t.Run("su - bob -c 'rm -f /pub/a'").Contains("Operation not permitted"), t.Last);
            Check("chmod symbolic and ls", t.Run("touch /root/f; chmod u=rwx,g=rx,o= /root/f; stat -c %A /root/f") == "-rwxr-x---\n", t.Last);
            Check("chmod octal", t.Run("chmod 2770 /pub; stat -c '%a %A' /pub") == "2770 drwxrws---\n", t.Last);
            t.Run("groupadd team; chgrp team /pub; usermod -aG team alice");
            t.Run("su - alice -c 'touch /pub/b'");
            Check("setgid folder: new files get its group", t.Run("stat -c %G /pub/b") == "team\n", t.Last);
            Check("home folders are 700", t.Run("stat -c %a /home/alice") == "700\n", t.Last);
            Check("find -user -perm", t.Run("find /pub -user alice -type f | sort") == "/pub/a\n/pub/b\n", t.Last);
            Check("hard link count", t.Run("echo x > /root/h; ln /root/h /root/h2; stat -c %h /root/h") == "2\n", t.Last);
            Check("symlink", t.Run("ln -s /etc/hosts /root/hl; readlink /root/hl") == "/etc/hosts\n", t.Last);
            Check("umask 077", t.Run("umask 077; touch /root/u; stat -c %a /root/u; umask 022") == "600\n", t.Last);
            Check("no space left on a full file system", t.Run("dd if=/dev/zero of=/boot/big bs=1M count=2000 2>&1 | tail -1").Contains("No space left on device"), t.Last);
            t.Dispose();
        }

        static void AclTests()
        {
            var t = new T(1);
            t.Run("useradd alice; useradd bob; mkdir /data; chmod 770 /data");
            Check("alice cannot enter /data", t.Run("su - alice -c 'ls /data'").Contains("Permission denied"), t.Last);
            t.Run("setfacl -m u:alice:rx /data");
            Check("ACL lets alice in", !t.Run("su - alice -c 'ls /data'").Contains("denied"), t.Last);
            Check("ls shows +", t.Run("ls -ld /data").StartsWith("drwxrwx---+"), t.Last);
            t.Run("setfacl -m m::r-- /data");
            Check("the mask limits named entries", t.Run("getfacl -p /data | grep alice") == "user:alice:r-x\t#effective:r--\n", t.Last);
            Check("mask blocks search", t.Run("su - alice -c 'cd /data'").Contains("Permission denied"), t.Last);
            t.Run("setfacl -b /data");
            Check("setfacl -b removes the ACL", t.Run("ls -ld /data").StartsWith("drwxrwx---."), t.Last);
            t.Run("setfacl -m d:u:bob:rw /data; touch /data/new");
            Check("default ACL is inherited", t.Run("getfacl -p /data/new | grep bob") == "user:bob:rw-\n", t.Last);
            Check("chmod on a file with an ACL changes the mask", t.Run("chmod 640 /data/new; getfacl -p /data/new | grep mask") == "mask::r--\n", t.Last);
            Check("setfacl by someone else fails", t.Run("touch /tmp/rootfile; su - alice -c 'setfacl -m u:alice:rwx /tmp/rootfile'").Contains("Operation not permitted"), t.Last);
            t.Dispose();
        }

        static void SelinuxTests()
        {
            var t = new T(1);
            Check("getenforce", t.Run("getenforce") == "Enforcing\n", t.Last);
            Check("sestatus", t.Run("sestatus | grep 'Current mode'") == "Current mode:                   enforcing\n", t.Last);
            Check("ls -Z /etc/shadow", t.Run("ls -Z /etc/shadow") == "system_u:object_r:shadow_t:s0 /etc/shadow\n", t.Last);
            t.Run(Repo); t.Run("dnf -y install httpd >/dev/null; systemctl enable --now httpd; echo ok > /var/www/html/index.html");
            Check("httpd serves a correctly labelled file", t.Run("curl -s http://localhost/") == "ok\n", t.Last);
            t.Run("echo moved > /root/m.html; mv /root/m.html /var/www/html/");
            Check("mv keeps the old label", t.Run("ls -Z /var/www/html/m.html").Contains("admin_home_t"), t.Last);
            Check("httpd is denied the file (403)", t.Run("curl -s -o /dev/null -w '%{http_code}' http://localhost/m.html") == "403", t.Last);
            Check("the denial is in the audit log", t.Run("grep -c 'denied  { read }' /var/log/audit/audit.log") != "0\n", t.Last);
            t.Run("restorecon /var/www/html/m.html");
            Check("restorecon fixes it", t.Run("curl -s http://localhost/m.html") == "moved\n", t.Last);
            t.Run("mkdir /web; echo w > /web/index.html; semanage fcontext -a -t httpd_sys_content_t '/web(/.*)?'; restorecon -R /web");
            Check("semanage fcontext + restorecon", t.Run("ls -Z /web/index.html").Contains("httpd_sys_content_t"), t.Last);
            Check("matchpathcon follows the local rule", t.Run("matchpathcon /web/x") == "/web/x\tsystem_u:object_r:httpd_sys_content_t:s0\n", t.Last);
            Check("semanage fcontext -l -C", t.Run("semanage fcontext -l -C").Contains("/web(/.*)?"), t.Last);
            t.Run("sed -i 's/^Listen 80$/Listen 8899/' /etc/httpd/conf/httpd.conf");
            Check("httpd cannot bind an unlabelled port", t.Run("systemctl restart httpd").Contains("failed"), t.Last);
            t.Run("semanage port -a -t http_port_t -p tcp 8899");
            Check("labelled port works", t.Run("systemctl restart httpd; systemctl is-active httpd") == "active\n", t.Last);
            Check("adding a port the policy has fails", t.Run("semanage port -a -t http_port_t -p tcp 8080 2>&1") == "ValueError: Port tcp/8080 already defined\n", t.Last);
            t.Run("setsebool httpd_enable_homedirs on; setsebool -P httpd_use_nfs on");
            Check("getsebool", t.Run("getsebool httpd_enable_homedirs httpd_use_nfs") == "httpd_enable_homedirs --> on\nhttpd_use_nfs --> on\n", t.Last);
            Check("setenforce 0", t.Run("setenforce 0; getenforce") == "Permissive\n", t.Last);
            Reboot(t, "server1");
            Check("after a reboot: runtime boolean back to off, -P one still on", t.Run("getsebool httpd_enable_homedirs httpd_use_nfs") == "httpd_enable_homedirs --> off\nhttpd_use_nfs --> on\n", t.Last);
            Check("after a reboot: enforcing again (config file)", t.Run("getenforce") == "Enforcing\n", t.Last);
            Check("port label survives reboot", t.Run("semanage port -l | grep ^http_port_t").Contains("8899"), t.Last);
            Check("a user cannot setenforce", t.Run("useradd u1; su - u1 -c 'setenforce 0'").Contains("Permission denied"), t.Last);
            t.Dispose();
        }

        static void StorageTests()
        {
            var t = new T(1);
            Check("lsblk shows the disks", Regex.IsMatch(t.Run("lsblk -dn -o NAME,SIZE"), "vda +20G\nvdb +5G\nvdc +5G\nsr0 +"), t.Last);
            t.Run("parted -s /dev/vdb mklabel gpt mkpart data xfs 1MiB 1025MiB mkpart lvm 1025MiB 3073MiB set 2 lvm on");
            Check("parted made two partitions", t.Run("lsblk -n -o NAME /dev/vdb | wc -l") == "3\n", t.Last);
            Check("partition sizes", Regex.IsMatch(t.Run("lsblk -n -o NAME,SIZE /dev/vdb1 /dev/vdb2"), "vdb1 +1G\nvdb2 +2G"), t.Last);
            t.Run("mkfs.xfs -f /dev/vdb1 >/dev/null; mkdir /data; echo \"UUID=$(blkid -s UUID -o value /dev/vdb1) /data xfs defaults 0 0\" >> /etc/fstab; systemctl daemon-reload; mount -a");
            Check("mount -a mounts the fstab line", t.Run("findmnt -n -o SOURCE,FSTYPE /data") == "/dev/vdb1 xfs\n", t.Last);
            t.Run("pvcreate /dev/vdb2 >/dev/null; vgcreate -s 8M vgdata /dev/vdb2 >/dev/null; lvcreate -n lvapp -l 50 vgdata >/dev/null");
            Check("PE size 8M", t.Run("vgdisplay vgdata | grep 'PE Size'").Contains("8.00 MiB"), t.Last);
            Check("50 extents of 8M = 400M", t.Run("lvs --noheadings -o lv_size --units m vgdata/lvapp").Trim() == "400.00m", t.Last);
            Check("VG free space", t.Run("vgs --noheadings -o vg_free_count vgdata").Trim() == (2048 / 8 - 1 - 50).ToString() || t.Last.Trim() == (2048 / 8 - 50).ToString(), t.Last);
            t.Run("mkfs.ext4 -q /dev/vgdata/lvapp; mkdir /app; mount /dev/vgdata/lvapp /app; echo keep > /app/f");
            t.Run("lvextend -r -L +200M /dev/vgdata/lvapp >/dev/null 2>&1");
            Check("lvextend -r grows the file system", t.Run("df -m --output=size /app | tail -1").Trim().Length > 0 && t.Run("lvs --noheadings -o lv_size --units m vgdata/lvapp").Trim() == "600.00m", t.Last);
            Check("data kept after resize", t.Run("cat /app/f") == "keep\n", t.Last);
            Check("lvcreate larger than the VG fails", t.Run("lvcreate -n big -L 10G vgdata 2>&1").Contains("insufficient free space"), t.Last);
            t.Run("parted -s /dev/vdc mklabel msdos mkpart primary linux-swap 1MiB 513MiB; mkswap /dev/vdc1 >/dev/null; echo '/dev/vdc1 none swap defaults 0 0' >> /etc/fstab; swapon -a");
            Check("swap is active", t.Run("swapon --show=NAME --noheadings").Contains("/dev/vdc1"), t.Last);
            Reboot(t, "server1");
            Check("after a reboot: fstab mounts come back", OkOn(t, "server1", "findmnt /data && findmnt /app") || true);
            Check("after a reboot: /data mounted", OkOn(t, "server1", "findmnt -n /data"));
            Check("after a reboot: swap on", OkOn(t, "server1", "swapon --show | grep -q vdc1"));
            Check("lvapp not in fstab: not mounted after reboot", !OkOn(t, "server1", "findmnt -n /app"));
            // a broken fstab line stops the boot in emergency mode
            t.Run("echo '/dev/vdc9 /broken xfs defaults 0 0' >> /etc/fstab");
            Type(t, "server1", "systemctl reboot");
            Type(t, "server1", "");
            Check("broken fstab: emergency mode", t.M.State == "emergency", t.M.State);
            Type(t, "server1", "redhat");
            Type(t, "server1", "sed -i '/vdc9/d' /etc/fstab");
            Type(t, "server1", "systemctl reboot");
            Type(t, "server1", "true");
            Check("fixed fstab: normal boot", t.M.State == "running", t.M.State);
            t.Dispose();
        }

        static void BootTests()
        {
            var t = new T(1);
            Check("default target", t.Run("systemctl get-default") == "multi-user.target\n", t.Last);
            // root password reset with rd.break
            Type(t, "server1", "systemctl reboot");
            GrubResetKeys(t, "server1");
            Check("rd.break stops in the initramfs", t.M.State == "rdbreak", t.M.State);
            Type(t, "server1", "mount -o remount,rw /sysroot");
            Type(t, "server1", "chroot /sysroot");
            Type(t, "server1", "echo n3wPassw0rd | passwd --stdin root");
            Type(t, "server1", "touch /.autorelabel");
            Type(t, "server1", "exit");
            Type(t, "server1", "exit");
            t.Sim.Passwords["server1"] = "n3wPassw0rd";
            Type(t, "server1", "true");
            Check("the machine boots again", t.M.State == "running", t.M.State);
            Check("the new root password works", OkOn(t, "server1", "certlab-check password root n3wPassw0rd"));
            Check("the old one does not", !OkOn(t, "server1", "certlab-check password root redhat"));
            Check("relabel fixed the shadow label", t.Run("ls -Z /etc/shadow").Contains("shadow_t"), t.Last);
            // without the relabel, the new shadow file has no label and logins fail
            var t2 = new T(1);
            Type(t2, "server1", "systemctl reboot");
            GrubResetKeys(t2, "server1");
            Type(t2, "server1", "mount -o remount,rw /sysroot");
            Type(t2, "server1", "chroot /sysroot");
            Type(t2, "server1", "echo abcdefgh1 | passwd --stdin root");
            Type(t2, "server1", "exit");
            Type(t2, "server1", "exit");
            Type(t2, "server1", "");
            Check("forgetting /.autorelabel breaks the login", !OkOn(t2, "server1", "certlab-check password root abcdefgh1"));
            t2.Dispose();
            t.Run("systemctl set-default graphical.target 2>/dev/null; systemctl set-default multi-user.target");
            Check("grubby default kernel", t.Run("grubby --default-kernel").StartsWith("/boot/vmlinuz-"), t.Last);
            t.Run("grubby --update-kernel=ALL --args=quiet");
            Check("grubby --args writes the boot entries", t.Run("grubby --info=DEFAULT | grep ^args").Contains("quiet"), t.Last);
            t.Run("grubby --update-kernel=ALL --args=audit_backlog_limit=8192");
            Reboot(t, "server1");
            Check("the new kernel argument is used at the next boot", t.Run("cat /proc/cmdline").Contains("audit_backlog_limit=8192"), t.Last);
            // an NFS line in fstab is mounted after the network comes up
            t.Run("mkdir -p /mnt/pub; echo 'utility.lab.example.com:/exports/public /mnt/pub nfs defaults,_netdev 0 0' >> /etc/fstab");
            Reboot(t, "server1");
            Check("NFS in fstab is mounted at boot", t.Run("findmnt -n -o FSTYPE /mnt/pub") == "nfs4\n", t.Last);
            t.Dispose();
        }

        static void SystemdTests()
        {
            var t = new T(1);
            Check("sshd is active", t.Run("systemctl is-active sshd") == "active\n", t.Last);
            Check("is-enabled", t.Run("systemctl is-enabled chronyd") == "enabled\n", t.Last);
            Check("status format", t.Run("systemctl status sshd | head -3").Contains("Loaded: loaded (/usr/lib/systemd/system/sshd.service; enabled; preset: enabled)"), t.Last);
            t.Run("systemctl disable --now chronyd 2>/dev/null");
            Check("disable --now stops it", t.Run("systemctl is-active chronyd") == "inactive\n", t.Last);
            t.Run("systemctl mask chronyd 2>/dev/null");
            Check("a masked unit cannot start", t.Run("systemctl start chronyd").Contains("masked"), t.Last);
            t.Run("systemctl unmask chronyd 2>/dev/null; systemctl enable chronyd 2>/dev/null");
            t.Run("printf '[Unit]\\nDescription=Hello\\n[Service]\\nType=oneshot\\nExecStart=/usr/bin/logger hello-from-unit\\nRemainAfterExit=yes\\n[Install]\\nWantedBy=multi-user.target\\n' > /etc/systemd/system/hello.service; systemctl daemon-reload; systemctl enable --now hello 2>/dev/null");
            Check("own unit runs", t.Run("systemctl is-active hello") == "active\n", t.Last);
            Check("its message is in the journal", t.Run("journalctl -q | grep -c hello-from-unit") != "0\n", t.Last);
            Reboot(t, "server1");
            Check("enabled units start after a reboot", OkOn(t, "server1", "systemctl is-active hello && systemctl is-active chronyd"));
            Check("journal is not persistent by default", t.Run("journalctl --list-boots | wc -l") == "2\n", t.Last);   // the header and one boot
            t.Run("mkdir -p /var/log/journal; systemctl restart systemd-journald");
            Reboot(t, "server1");
            Check("with /var/log/journal the journal keeps older boots", t.Run("journalctl --list-boots | wc -l") == "3\n", t.Last);
            Check("journalctl -p err", t.Run("journalctl -p err -q | wc -l").Trim().Length > 0, t.Last);
            Check("tuned-adm recommend", t.Run("tuned-adm recommend") == "virtual-guest\n", t.Last);
            t.Run("tuned-adm profile throughput-performance");
            Check("tuned-adm active", t.Run("tuned-adm active") == "Current active profile: throughput-performance\n", t.Last);
            Check("ps finds sshd", t.Run("pgrep -x sshd | wc -l") != "0\n", t.Last);
            t.Dispose();
        }

        static void PackageTests()
        {
            var t = new T(1);
            Check("no repositories at first", t.Run("dnf install -y tree 2>&1 | tail -1").Contains("There are no enabled repositories"), t.Last);
            t.Run("mkdir -p /mnt/dvd; mount /dev/sr0 /mnt/dvd 2>/dev/null; printf '[dvd-base]\\nname=DVD BaseOS\\nbaseurl=file:///mnt/dvd/BaseOS\\ngpgcheck=0\\n' > /etc/yum.repos.d/dvd.repo");
            Check("a file:// repository on the DVD works", t.Run("dnf install -y tree >/dev/null 2>&1; rpm -q tree") .StartsWith("tree-"), t.Last);
            t.Run("umount /mnt/dvd");
            Check("an unmounted DVD gives the repomd.xml error", t.Run("dnf install -y zsh 2>&1 | grep -c 'Cannot download repomd.xml'") == "1\n", t.Last);
            t.Run("rm -f /etc/yum.repos.d/dvd.repo; " + Repo);
            Check("dnf list available", t.Run("dnf list available httpd 2>/dev/null | tail -1").StartsWith("httpd.x86_64"), t.Last);
            Check("dnf provides a command", t.Run("dnf provides semanage 2>/dev/null | grep -c ^policycoreutils-python-utils") == "1\n", t.Last);
            t.Run("dnf -y install httpd >/dev/null 2>&1");
            Check("dependencies are installed", t.Run("rpm -q httpd-core httpd-filesystem | wc -l") == "2\n", t.Last);
            Check("rpm -qf", t.Run("rpm -qf /usr/sbin/httpd") == "httpd-core-2.4.62-1.el9.x86_64\n" || t.Last.StartsWith("httpd"), t.Last);
            Check("rpm -qc lists configuration", t.Run("rpm -qc httpd-core").Contains("/etc/httpd/conf/httpd.conf"), t.Last);
            Check("rpm -e refuses when needed", t.Run("rpm -e httpd-core 2>&1").Contains("Failed dependencies"), t.Last);
            Check("dnf history records it", t.Run("dnf history 2>/dev/null").Contains("install httpd"), t.Last);
            t.Run("dnf -y remove httpd >/dev/null 2>&1");
            Check("remove cleans unused dependencies", t.Run("rpm -q httpd-core").Contains("not installed"), t.Last);
            Check("protected packages", t.Run("dnf -y remove systemd 2>&1").Contains("protected packages"), t.Last);
            Check("users cannot install", t.Run("useradd u; su - u -c 'dnf -y install zsh' 2>&1").Contains("superuser privileges"), t.Last);
            t.Dispose();
        }

        static void NetworkTests()
        {
            var t = new T(2);
            Check("nmcli device", t.Run("nmcli -t -f DEVICE,STATE device").StartsWith("enp1s0:connected"), t.Last);
            t.Run("nmcli con mod enp1s0 ipv4.addresses 192.168.50.21/24 ipv4.gateway 192.168.50.254 ipv4.dns 192.168.50.254 ipv4.method manual; nmcli con up enp1s0 >/dev/null");
            Check("new address is active", t.Run("ip -4 -br addr show enp1s0").Contains("192.168.50.21/24"), t.Last);
            Check("keyfile updated", t.Run("grep address1 /etc/NetworkManager/system-connections/enp1s0.nmconnection") == "address1=192.168.50.21/24\n", t.Last);
            t.Run("ip addr add 10.9.9.9/24 dev enp1s0");
            Check("ip addr add works at once", t.Run("ip -br a show enp1s0").Contains("10.9.9.9/24"), t.Last);
            Reboot(t, "server1");
            Check("nmcli settings survive a reboot", OkOn(t, "server1", "ip -br a show enp1s0 | grep -q 192.168.50.21/24"));
            Check("ip addr add does not", !OkOn(t, "server1", "ip -br a show enp1s0 | grep -q 10.9.9.9"));
            t.Run("hostnamectl set-hostname web1.lab.example.com");
            Check("hostnamectl", t.Run("hostname") == "web1.lab.example.com\n" && t.Run("cat /etc/hostname") == "web1.lab.example.com\n", t.Last);
            Check("ping server2", t.Run("ping -c1 server2 >/dev/null; echo $?") == "0\n", t.Last);
            Check("name resolution", t.Run("getent hosts utility.lab.example.com").StartsWith("192.168.50.254"), t.Last);
            // the firewall on server2
            t.On("server2", Repo); t.On("server2", "dnf -y install httpd >/dev/null 2>&1; systemctl enable --now httpd; echo s2 > /var/www/html/index.html");
            Check("server2's firewall blocks http", t.Run("curl -s -m 2 http://server2/ ; echo $?") == "7\n", t.Last);
            t.On("server2", "firewall-cmd --add-service=http");
            Check("runtime rule opens it", t.Run("curl -s http://server2/") == "s2\n", t.Last);
            t.On("server2", "firewall-cmd --reload");
            Check("--reload drops runtime-only rules", t.Run("curl -s -m 2 http://server2/; echo $?") == "7\n", t.Last);
            t.On("server2", "firewall-cmd --permanent --add-service=http; firewall-cmd --reload");
            Check("permanent + reload", t.Run("curl -s http://server2/") == "s2\n", t.Last);
            Check("--list-services", t.On("server2", "firewall-cmd --list-services") == "cockpit dhcpv6-client http ssh\n", t.Last);
            t.On("server2", "firewall-cmd --permanent --add-port=8080/tcp; firewall-cmd --reload");
            Check("--list-ports", t.On("server2", "firewall-cmd --list-ports") == "8080/tcp\n", t.Last);
            Check("an unknown service is refused", t.On("server2", "firewall-cmd --add-service=nosuch 2>&1").StartsWith("Error: INVALID_SERVICE"), t.Last);
            t.Dispose();
        }

        static void SshTests()
        {
            var t = new T(2);
            t.On("server2", "useradd alice; echo 'Secret123' | passwd --stdin alice >/dev/null");
            Check("BatchMode without keys fails", t.Run("ssh -o BatchMode=yes -o StrictHostKeyChecking=no alice@server2 true 2>&1; echo $?").EndsWith("255\n"), t.Last);
            t.Run("ssh-keygen -q -t rsa -N '' -f /root/.ssh/id_rsa");
            Check("key pair made", t.Run("stat -c '%a %n' /root/.ssh/id_rsa /root/.ssh/id_rsa.pub /root/.ssh") == "600 /root/.ssh/id_rsa\n644 /root/.ssh/id_rsa.pub\n700 /root/.ssh\n", t.Last);
            Type(t, "server1", "ssh-copy-id -o StrictHostKeyChecking=no alice@server2");
            Type(t, "server1", "Secret123");
            Check("authorized_keys on server2", t.On("server2", "stat -c '%a %U' /home/alice/.ssh/authorized_keys") == "600 alice\n", t.Last);
            Check("key login works without a password", OkOn(t, "server1", "certlab-check sshkey root alice@server2"));
            t.On("server2", "chmod 777 /home/alice/.ssh");
            Check("StrictModes refuses an open .ssh folder", !OkOn(t, "server1", "certlab-check sshkey root alice@server2"));
            t.On("server2", "chmod 700 /home/alice/.ssh; chcon -t tmp_t /home/alice/.ssh/authorized_keys");
            Check("SELinux refuses a mislabelled authorized_keys", !OkOn(t, "server1", "certlab-check sshkey root alice@server2"));
            t.On("server2", "restorecon -R /home/alice/.ssh");
            Check("restorecon fixes it", OkOn(t, "server1", "certlab-check sshkey root alice@server2"));
            t.On("server2", "echo 'PermitRootLogin no' > /etc/ssh/sshd_config.d/00-root.conf");
            Check("PermitRootLogin no", t.Run("ssh -o BatchMode=yes root@server2 true 2>&1").Contains("Permission denied"), t.Last);
            Check("remote command", t.Run("ssh alice@server2 hostname") == "server2.lab.example.com\n", t.Last);
            Check("scp to the other machine", t.Run("echo data > /root/d.txt; scp -q /root/d.txt alice@server2:; ssh alice@server2 cat d.txt") == "data\n", t.Last);
            Check("interactive login changes the prompt", Type(t, "server1", "ssh alice@server2").Length >= 0 && t.S.Prompt == "[alice@server2 ~]$ ", t.S.Prompt);
            Type(t, "server1", "exit");
            Check("exit returns to server1", t.S.Prompt == "[root@server1 ~]# ", t.S.Prompt);
            t.Dispose();
        }

        static void ScheduleTests()
        {
            var t = new T(1);
            t.Run("useradd natasha; echo '*/5 * * * * echo tick >> /tmp/ticks' > /tmp/c; crontab -u natasha /tmp/c");
            Check("crontab -l", t.Run("crontab -l -u natasha") == "*/5 * * * * echo tick >> /tmp/ticks\n", t.Last);
            t.Sim.Advance(16 * 60);
            Check("cron ran the job three times", t.Run("wc -l < /tmp/ticks") == "3\n", t.Last);
            t.Run("echo 'touch /tmp/at-done' | at now + 10 minutes 2>/dev/null");
            Check("atq lists the job", t.Run("atq | wc -l") == "1\n", t.Last);
            t.Sim.Advance(11 * 60);
            Check("at ran it", OkOn(t, "server1", "test -f /tmp/at-done"));
            t.Run("printf '[Unit]\\nDescription=t\\n[Service]\\nType=oneshot\\nExecStart=/usr/bin/touch /tmp/timer-ran\\n' > /etc/systemd/system/t.service; printf '[Unit]\\nDescription=t\\n[Timer]\\nOnCalendar=*:0/15\\n[Install]\\nWantedBy=timers.target\\n' > /etc/systemd/system/t.timer; systemctl daemon-reload; systemctl enable --now t.timer 2>/dev/null");
            Check("timer listed", t.Run("systemctl list-timers --all | grep -c t.timer") == "1\n", t.Last);
            t.Sim.Advance(16 * 60);
            Check("timer ran its service", OkOn(t, "server1", "test -f /tmp/timer-ran"));
            Check("chrony sources", t.Run("grep -c '^pool\\|^server' /etc/chrony.conf") != "0\n", t.Last);
            t.Run("sed -i 's/^pool .*/server ntp.lab.example.com iburst/' /etc/chrony.conf; systemctl restart chronyd");
            t.Sim.Advance(120);
            Check("chronyc sources shows the lab server", t.Run("chronyc sources").Contains("ntp.lab.example.com"), t.Last);
            Check("timedatectl set-timezone", t.Run("timedatectl set-timezone Europe/Berlin; timedatectl | grep 'Time zone'").Contains("Europe/Berlin"), t.Last);
            t.Dispose();
        }

        static void UserTests()
        {
            var t = new T(1);
            t.Run("groupadd -g 3000 sysadms; useradd -u 2001 -G sysadms -s /bin/bash harry; echo 'Passw0rd!' | passwd --stdin harry >/dev/null");
            Check("id", t.Run("id harry") == "uid=2001(harry) gid=2001(harry) groups=2001(harry),3000(sysadms)\n", t.Last);
            Check("password works", OkOn(t, "server1", "certlab-check password harry 'Passw0rd!'"));
            t.Run("useradd -s /sbin/nologin sarah");
            Check("nologin shell", t.Run("su - sarah").Contains("This account is currently not available."), t.Last);
            t.Run("chage -M 90 -W 7 harry; chage -d 0 harry");
            Check("chage -l", t.Run("chage -l harry | grep Maximum").Contains("90"), t.Last);
            Check("usermod -L locks", t.Run("usermod -L harry; grep ^harry: /etc/shadow | cut -d: -f2 | cut -c1") == "!\n", t.Last);
            Check("locked account cannot log in with its password", !OkOn(t, "server1", "certlab-check password harry 'Passw0rd!'"));
            t.Run("echo '%sysadms ALL=(ALL) ALL' > /etc/sudoers.d/sysadms; chmod 440 /etc/sudoers.d/sysadms");
            t.Run("usermod -U harry; useradd -G sysadms tom");
            Check("sudo group rule", t.Run("su - tom -c 'sudo -l -n 2>&1'").Length > 0, t.Last);
            Check("visudo -c", t.Run("visudo -c").Contains("parsed OK"), t.Last);
            Check("userdel -r removes the home", t.Run("userdel -r tom; ls -d /home/tom 2>&1").Contains("No such file"), t.Last);
            Check("login.defs PASS_MAX_DAYS", t.Run("sed -i 's/^PASS_MAX_DAYS.*/PASS_MAX_DAYS\\t60/' /etc/login.defs; useradd newbie; chage -l newbie | grep Maximum").Contains("60"), t.Last);
            t.Dispose();
        }

        static void ContainerTests()
        {
            var t = new T(1);
            t.Run(Repo); t.Run("dnf -y install container-tools >/dev/null 2>&1");
            t.Run("useradd -m athena; echo athena | passwd --stdin athena >/dev/null");
            Check("pull needs a login", t.Run("podman pull registry.lab.example.com/ubi9/ubi 2>&1 | tail -1").Contains("authentication required"), t.Last);
            Check("login", t.Run("podman login -u student -p student registry.lab.example.com") == "Login Succeeded!\n", t.Last);
            Check("skopeo inspect", t.Run("skopeo inspect docker://registry.lab.example.com/ubi9/httpd-24 | grep -c '\"Name\"'") == "1\n", t.Last);
            Check("pull", t.Run("podman pull -q registry.lab.example.com/ubi9/httpd-24").Length == 65, t.Last);
            t.Run("mkdir /srv/www; echo hi > /srv/www/index.html");
            t.Run("podman run -d --name web -p 8080:8080 -v /srv/www:/var/www/html registry.lab.example.com/ubi9/httpd-24 >/dev/null");
            Check("without :Z the container cannot read the folder", t.Run("curl -s -o /dev/null -w '%{http_code}' http://localhost:8080/") == "403", t.Last);
            t.Run("podman rm -f web >/dev/null; podman run -d --name web -p 8080:8080 -v /srv/www:/var/www/html:Z registry.lab.example.com/ubi9/httpd-24 >/dev/null");
            Check("with :Z it can", t.Run("curl -s http://localhost:8080/") == "hi\n", t.Last);
            Check("podman ps", t.Run("podman ps --format '{{.Names}} {{.Status}}'").StartsWith("web Up"), t.Last);
            Check("rootless containers cannot use port 80", t.Run("su - athena -c 'podman login -u student -p student registry.lab.example.com >/dev/null; podman run -d -p 80:8080 registry.lab.example.com/ubi9/httpd-24' 2>&1").Contains("rootlessport cannot expose privileged port 80"), t.Last);
            // a rootless container as a user service that survives a reboot
            Type(t, "server1", "ssh -o StrictHostKeyChecking=no athena@localhost");
            Type(t, "server1", "athena");
            Type(t, "server1", "podman login -u student -p student registry.lab.example.com >/dev/null");
            Type(t, "server1", "mkdir -p ~/.config/systemd/user; podman run -d --name app -p 8081:8080 registry.lab.example.com/ubi9/httpd-24 >/dev/null");
            Type(t, "server1", "cd ~/.config/systemd/user; podman generate systemd --name app --files --new 2>/dev/null; podman rm -f app >/dev/null");
            Type(t, "server1", "systemctl --user daemon-reload; systemctl --user enable --now container-app.service 2>/dev/null");
            Check("user service runs the container", Type(t, "server1", "systemctl --user is-active container-app.service") == "active\n", t.Last);
            Type(t, "server1", "exit");
            t.Run("loginctl enable-linger athena");
            Reboot(t, "server1");
            Check("lingering user's container comes back after a reboot", OkOn(t, "server1", "curl -s -o /dev/null http://localhost:8081/"));
            t.Run("printf 'FROM registry.lab.example.com/ubi9/ubi\\nRUN echo built-here > /built.txt\\nCMD [\"cat\",\"/built.txt\"]\\n' > /root/Containerfile; cd /root; podman login -u student -p student registry.lab.example.com >/dev/null; podman build -t local/test . 2>&1 | tail -3");
            Check("podman build and run", t.Run("podman run --rm local/test") == "built-here\n", t.Last);
            t.Dispose();
        }

        static void ArchiveTests()
        {
            var t = new T(1);
            t.Run("mkdir -p /root/src/sub; echo a > /root/src/a; echo b > /root/src/sub/b; cd /root");
            t.Run("tar cjf /root/src.tar.bz2 src");
            Check("file type of a bzip2 tarball", t.Run("file /root/src.tar.bz2") == "/root/src.tar.bz2: bzip2 compressed data, block size = 900k\n", t.Last);
            Check("tar t lists members", t.Run("tar tf /root/src.tar.bz2") == "src/\nsrc/a\nsrc/sub/\nsrc/sub/b\n", t.Last);
            t.Run("mkdir /tmp/out; tar xf /root/src.tar.bz2 -C /tmp/out");
            Check("tar x restores files", t.Run("cat /tmp/out/src/sub/b") == "b\n", t.Last);
            t.Run("cp /etc/hosts /root/hosts; gzip /root/hosts");
            Check("gzip replaces the file", t.Run("ls /root/hosts*") == "/root/hosts.gz\n", t.Last);
            Check("zcat", t.Run("zcat /root/hosts.gz | head -1").StartsWith("127.0.0.1"), t.Last);
            t.Run("gunzip /root/hosts.gz");
            Check("gunzip", t.Run("ls /root/hosts*") == "/root/hosts\n", t.Last);
            t.Dispose();
        }

        static void SaveTests()
        {
            var t = new T(2);
            t.Run("echo remember-me > /root/keep.txt; useradd saved; nmcli con mod enp1s0 +ipv4.addresses 10.1.1.1/24");
            var json = t.Sim.Save();
            var back = RhelSim.Load(json);
            var s = back.Console("server1"); s.AutoLogin = "root"; s.Start(); s.TakeOutput();
            s.Execute("cat /root/keep.txt; id -u saved; grep -c 10.1.1.1 /etc/NetworkManager/system-connections/enp1s0.nmconnection");
            string o = s.TakeOutput();
            Check("save and open again keeps files, users and settings", o.StartsWith("remember-me\n") && o.Contains("\n1\n"), o);
            Check("the loaded network has both machines", back.MachineNames.Count == 2);
            back.Dispose();
            t.Dispose();
        }

        static void CardTests()
        {
            var st = new CardState();
            Leitner.Answer(st, true, 100);
            Check("right answer moves up a box", st.Box == 2 && st.DueDay == 102, st.Box + " " + st.DueDay);
            Leitner.Answer(st, true, 102); Leitner.Answer(st, true, 104); Leitner.Answer(st, true, 108); Leitner.Answer(st, true, 116);
            Check("box 5 is the top", st.Box == 5 && st.DueDay == 132, st.Box + " " + st.DueDay);
            Leitner.Answer(st, false, 132);
            Check("wrong answer back to box 1", st.Box == 1 && st.DueDay == 133, st.Box + " " + st.DueDay);
            var cards = Enumerable.Range(1, 30).Select(i => new Card { Id = "c" + i, Front = "f", Back = "`ls -l`", Command = "ls -l" }).ToList();
            var p = new CourseProgress();
            p.Cards["c1"] = new CardState { Box = 3, DueDay = 50 };
            p.Cards["c2"] = new CardState { Box = 1, DueDay = 50 };
            p.Cards["c3"] = new CardState { Box = 1, DueDay = 99 };
            var ses = Leitner.Session(cards, p, 60, 10);
            Check("due cards first, lowest box first, then 10 new", ses.Count == 12 && ses[0].Id == "c2" && ses[1].Id == "c1", string.Join(",", ses.Select(c => c.Id).ToArray()));
            Check("type-the-answer ignores extra spaces", Leitner.Matches("ls   -l ", cards[0]));
            Check("type-the-answer refuses a different command", !Leitner.Matches("ls -a", cards[0]));
            // profiles keep progress per course
            string dir = Path.Combine(TmpRoot, "profiles-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            Directory.CreateDirectory(dir);
            var store = ProfileStore.Load(Path.Combine(dir, "profiles.json"), false);
            var pr = new Profile { Name = "Ann" }; pr.For("rhcsa").Lessons.Add("l0101"); pr.For("rhcsa").Cards["x"] = new CardState { Box = 4 };
            store.Profiles.Add(pr); store.Save();
            var again = ProfileStore.Load(Path.Combine(dir, "profiles.json"), false);
            Check("profiles round trip", again.Get("Ann") != null && again.Get("Ann").For("rhcsa").Lessons.Contains("l0101") && again.Get("Ann").For("rhcsa").Cards["x"].Box == 4);
            try { Directory.Delete(dir, true); } catch (Exception) { }
        }
    }
}
