// Cert Lab - the packages of the simulated RHEL-like system: which repository each one is in (BaseOS or
// AppStream on the utility server, or the "DVD"), what it needs, and what it puts on disk (programs, unit files,
// configuration). A program exists on a machine only while its package is installed, so "command not found"
// and "dnf install" behave like the real thing. Version strings are made up but follow RHEL 9's scheme.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public class Pkg
    {
        public string Name, Ver, Arch = "x86_64", Repo = "BaseOS", Summary = "";
        public List<string> Requires = new List<string>();
        public List<string> Bins = new List<string>();     // "useradd" -> /usr/bin; "s:useradd" -> /usr/sbin; "p:/path" -> a full path
        public List<string> Units = new List<string>();
        public List<string> Docs = new List<string>();
        public bool Base;                                  // installed with the system
        public string Group = "";
        public long SizeKb = 300;
        public string Nevra { get { return Name + "-" + Ver + "." + Arch; } }
    }

    public static partial class Packages
    {
        public static readonly List<Pkg> All = new List<Pkg>();
        static readonly Dictionary<string, Pkg> byName = new Dictionary<string, Pkg>(StringComparer.Ordinal);

        // name | version-release | arch | repo | base(1/0) | requires | programs | units | summary
        static readonly string[] Table =
        {
            "bash|5.1.8-9.el9|x86_64|BaseOS|1||bash,sh||The GNU Bourne Again shell",
            "coreutils|8.32-36.el9|x86_64|BaseOS|1||ls,cp,mv,rm,mkdir,rmdir,touch,cat,echo,chmod,chown,chgrp,ln,head,tail,wc,sort,uniq,cut,tr,date,id,whoami,pwd,basename,dirname,stat,du,df,dd,tee,sleep,true,false,seq,mktemp,printf,env,printenv,yes,nl,paste,tac,od,md5sum,sha1sum,sha256sum,base64,base32,realpath,readlink,uname,hostid,nproc,shuf,split,expand,fold,comm,join,test,[,install,chcon,runcon,truncate,sync,shred,timeout,nice,nohup,groups,users,who,tty,stty,dircolors,vdir,dir,fmt,csplit,expr,factor,numfmt,pr,ptx,sum,tsort,unexpand,unlink,link,mkfifo,mknod,pathchk,pinky,logname,arch,b2sum,cksum,sha512sum,chroot,s:chroot||A set of basic GNU tools commonly used in shell scripts",
            "util-linux|2.37.4-20.el9|x86_64|BaseOS|1||dmesg,mount,umount,findmnt,lsblk,kill,more,su,mesg,script,whereis,wall,logger,look,cal,col,column,flock,getopt,hexdump,ionice,ipcs,last,lastb,lscpu,lsmem,lsns,namei,rename,renice,rev,setsid,taskset,uuidgen,wdctl,s:blkid,s:fdisk,s:sfdisk,s:cfdisk,s:mkswap,s:swapon,s:swapoff,s:wipefs,s:partx,s:blockdev,s:fsck,s:mkfs,s:losetup,s:findfs,s:fstrim,s:agetty,s:sulogin,s:switch_root,s:runuser,s:zramctl,s:chcpu,s:hwclock,s:ldattach,s:pivot_root,s:rtcwake,s:fsfreeze||A collection of basic system utilities",
            "procps-ng|3.3.17-14.el9|x86_64|BaseOS|1||ps,top,free,uptime,w,pkill,pgrep,pidof,pmap,pwdx,vmstat,watch,tload,slabtop,s:sysctl||System and process monitoring utilities",
            "psmisc|23.4-3.el9|x86_64|BaseOS|1||killall,pstree,fuser,prtstat||Utilities for managing processes on your system",
            "findutils|4.8.0-7.el9|x86_64|BaseOS|1||find,xargs||The GNU versions of find utilities (find and xargs)",
            "grep|3.6-5.el9|x86_64|BaseOS|1||grep,egrep,fgrep||Pattern matching utilities",
            "sed|4.8-9.el9|x86_64|BaseOS|1||sed||A GNU stream text editor",
            "gawk|5.1.0-6.el9|x86_64|BaseOS|1||awk,gawk||The GNU version of the AWK text processing utility",
            "diffutils|3.7-12.el9|x86_64|BaseOS|1||diff,cmp,diff3,sdiff||A GNU collection of diff utilities",
            "less|590-5.el9|x86_64|BaseOS|1||less||A text file browser similar to more, but better",
            "which|2.21-29.el9|x86_64|BaseOS|1||which||Displays where a particular program in your path is located",
            "file|5.39-16.el9|x86_64|BaseOS|1||file||Utility for determining file types",
            "tar|1.34-7.el9|x86_64|BaseOS|1||tar||GNU file archiving program",
            "gzip|1.12-1.el9|x86_64|BaseOS|1||gzip,gunzip,zcat,zgrep,zless,zdiff||The GNU data compression program",
            "bzip2|1.0.8-8.el9|x86_64|BaseOS|1||bzip2,bunzip2,bzcat,bzgrep||A file compression utility",
            "xz|5.2.5-8.el9_0|x86_64|BaseOS|1||xz,unxz,xzcat,xzgrep||LZMA compression utilities",
            "zip|3.0-35.el9|x86_64|BaseOS|0||zip||A file compression and packaging utility compatible with PKZIP",
            "unzip|6.0-57.el9|x86_64|BaseOS|0||unzip||A utility for unpacking zip files",
            "rsync|3.2.3-20.el9|x86_64|BaseOS|1||rsync||A program for synchronizing files over a network",
            "shadow-utils|4.9-9.el9|x86_64|BaseOS|1||s:adduser,chage,gpasswd,newgrp,lastlog,s:useradd,s:usermod,s:userdel,s:groupadd,s:groupmod,s:groupdel,s:chpasswd,s:pwck,s:grpck,s:vipw,s:vigr,s:newusers,s:pwconv,s:grpconv,s:chgpasswd||Utilities for managing accounts and shadow password files",
            "passwd|0.80-12.el9|x86_64|BaseOS|1||passwd||An utility for setting or changing passwords using PAM",
            "sudo|1.9.5p2-10.el9_3|x86_64|BaseOS|1||sudo,sudoedit,s:visudo||Allows restricted root access for specified users",
            "systemd|252-46.el9_5.2|x86_64|BaseOS|1||udevadm,p:/usr/lib/systemd/systemd-journald,p:/usr/lib/systemd/systemd-logind,systemctl,journalctl,hostnamectl,timedatectl,localectl,loginctl,systemd-analyze,systemd-run,systemd-cat,busctl,s:reboot,s:poweroff,s:halt,s:shutdown,s:init,s:runlevel,s:telinit|systemd-journald.service,systemd-logind.service,multi-user.target,graphical.target,rescue.target,emergency.target,default.target|System and Service Manager",
            "rsyslog|8.2310.0-4.el9|x86_64|AppStream|1||s:rsyslogd|rsyslog.service|Enhanced system logging and kernel message trapping daemon",
            "logrotate|3.18.0-8.el9|x86_64|BaseOS|1||s:logrotate|logrotate.timer|Rotates, compresses, removes and mails system log files",
            "cronie|1.5.7-12.el9|x86_64|BaseOS|1||run-parts,crontab,s:crond,s:anacron|crond.service|Cron daemon for executing programs at set times",
            "at|3.1.23-11.el9|x86_64|BaseOS|1||at,atq,atrm,batch,s:atd|atd.service|Job spooling tools",
            "openssh-server|8.7p1-43.el9|x86_64|BaseOS|1|openssh|s:sshd|sshd.service|An open source SSH server daemon",
            "openssh-clients|8.7p1-43.el9|x86_64|BaseOS|1|openssh|ssh,scp,sftp,ssh-keygen,ssh-copy-id,ssh-agent,ssh-add,ssh-keyscan||An open source SSH client applications",
            "openssh|8.7p1-43.el9|x86_64|BaseOS|1||||An open source implementation of SSH protocol version 2",
            "NetworkManager|1.48.10-2.el9_5|x86_64|BaseOS|1||nmcli,s:NetworkManager|NetworkManager.service|Network connection manager and user applications",
            "NetworkManager-tui|1.48.10-2.el9_5|x86_64|BaseOS|1|NetworkManager|nmtui,nmtui-edit,nmtui-connect,nmtui-hostname||NetworkManager curses-based UI",
            "iproute|6.2.0-6.el9_4|x86_64|BaseOS|1||ss,s:ip,s:bridge,s:tc||Advanced IP routing and network device configuration tools",
            "iputils|20210202-10.el9_5|x86_64|BaseOS|1||ping,tracepath,arping||Network monitoring tools including ping",
            "hostname|3.23-6.el9|x86_64|BaseOS|1||hostname,domainname,dnsdomainname||Utility to set/show the host name or domain name",
            "bind-utils|9.16.23-24.el9_5|x86_64|AppStream|0||dig,host,nslookup,nsupdate||Utilities for querying DNS name servers",
            "curl|7.76.1-31.el9|x86_64|BaseOS|1||curl||A utility for getting files from remote servers (FTP, HTTP, and others)",
            "wget|1.21.1-8.el9_4|x86_64|AppStream|0||wget||A utility for retrieving files using the HTTP or FTP protocols",
            "firewalld|1.3.4-7.el9|noarch|BaseOS|1||firewall-cmd,firewall-offline-cmd,s:firewalld|firewalld.service|A firewall daemon with D-Bus interface providing a dynamic firewall",
            "chrony|4.5-3.el9|x86_64|BaseOS|1||chronyc,s:chronyd|chronyd.service|An NTP client/server",
            "tuned|2.24.0-1.el9|noarch|BaseOS|1||s:tuned,s:tuned-adm|tuned.service|A dynamic adaptive system tuning daemon",
            "audit|3.1.5-1.el9|x86_64|BaseOS|1||s:auditd,s:ausearch,s:aureport,s:auditctl|auditd.service|User space tools for kernel auditing",
            "libselinux-utils|3.6-1.el9|x86_64|BaseOS|1||getsebool,s:getenforce,s:setenforce,s:selinuxenabled,s:matchpathcon||SELinux libselinux utilities",
            "policycoreutils|3.6-2.1.el9|x86_64|BaseOS|1||s:restorecon,s:setfiles,s:load_policy,s:sestatus,s:setsebool,s:fixfiles,s:semodule,s:restorecond||SELinux policy core utilities",
            "policycoreutils-python-utils|3.6-2.1.el9|noarch|BaseOS|1||audit2allow,audit2why,chcat,s:semanage||SELinux policy core python utilities",
            "setroubleshoot-server|3.3.32-1.el9|x86_64|AppStream|0||s:sealert,s:setroubleshootd||SELinux troubleshoot server",
            "acl|2.3.1-4.el9|x86_64|BaseOS|1||getfacl,setfacl,chacl||Access control list utilities",
            "attr|2.5.1-3.el9|x86_64|BaseOS|1||getfattr,setfattr,attr||Utilities for managing filesystem extended attributes",
            "lvm2|2.03.24-2.el9|x86_64|BaseOS|1||s:lvm,s:pvcreate,s:pvremove,s:pvs,s:pvdisplay,s:pvscan,s:pvmove,s:pvresize,s:pvchange,s:vgcreate,s:vgremove,s:vgs,s:vgdisplay,s:vgextend,s:vgreduce,s:vgrename,s:vgchange,s:vgscan,s:lvcreate,s:lvremove,s:lvs,s:lvdisplay,s:lvextend,s:lvreduce,s:lvresize,s:lvrename,s:lvchange,s:lvscan,s:fsadm||Userland logical volume management tools",
            "xfsprogs|6.4.0-4.el9|x86_64|BaseOS|1||s:mkfs.xfs,s:xfs_growfs,s:xfs_admin,s:xfs_info,s:xfs_repair,s:xfs_db,s:fsck.xfs||Utilities for managing the XFS filesystem",
            "e2fsprogs|1.46.5-5.el9|x86_64|BaseOS|1||chattr,lsattr,s:mkfs.ext4,s:mkfs.ext3,s:mkfs.ext2,s:mke2fs,s:resize2fs,s:e2fsck,s:fsck.ext4,s:tune2fs,s:dumpe2fs,s:e2label||Utilities for managing ext2, ext3, and ext4 file systems",
            "dosfstools|4.2-3.el9|x86_64|BaseOS|1||s:mkfs.vfat,s:mkfs.fat,s:mkdosfs,s:fsck.vfat,s:fatlabel||Utilities for making and checking MS-DOS FAT filesystems on Linux",
            "parted|3.5-2.el9|x86_64|BaseOS|1||s:parted,s:partprobe||The GNU disk partition manipulation program",
            "gdisk|1.0.7-5.el9|x86_64|BaseOS|1||s:gdisk,s:sgdisk,s:cgdisk,s:fixparts||An fdisk-like partitioning tool for GPT disks",
            "kmod|28-10.el9|x86_64|BaseOS|1||lsmod,s:modprobe,s:modinfo,s:insmod,s:rmmod,s:depmod||Linux kernel module management utilities",
            "pciutils|3.7.0-5.el9|x86_64|BaseOS|1||lspci||PCI bus related utilities",
            "nfs-utils|2.5.4-27.el9|x86_64|BaseOS|1||s:showmount,s:exportfs,s:mount.nfs,s:mount.nfs4,s:nfsstat|nfs-server.service|NFS utilities and supporting clients and daemons for the kernel NFS server",
            "autofs|5.1.7-58.el9|x86_64|BaseOS|0|nfs-utils|s:automount|autofs.service|A tool for automatically mounting and unmounting filesystems",
            "rpm|4.16.1.3-34.el9|x86_64|BaseOS|1||rpm,rpm2cpio,rpmquery,rpmverify||The RPM package management system",
            "dnf|4.14.0-17.el9|noarch|BaseOS|1||dnf,yum,dnf-3||Package manager",
            "dnf-plugins-core|4.3.0-16.el9|noarch|BaseOS|1|dnf|||Core Plugins for DNF",
            "subscription-manager|1.29.42-1.el9|x86_64|BaseOS|1||subscription-manager||Tools and libraries for subscription and repository management",
            "kernel|5.14.0-503.14.1.el9_5|x86_64|BaseOS|1||||The Linux kernel",
            "grub2-tools|2.06-92.el9|x86_64|BaseOS|1||s:grub2-mkconfig,s:grub2-set-default,s:grub2-editenv,s:grub2-install,s:grub2-probe||Support tools for GRUB.",
            "grubby|8.40-61.el9|x86_64|BaseOS|1||s:grubby||Command line tool for updating bootloader configs",
            "man-db|2.9.3-7.el9|x86_64|BaseOS|1||man,apropos,whatis,mandb,info||Tools for searching and reading man pages",
            "man-pages|6.04-5.el9|noarch|BaseOS|1||||Linux kernel and C library user-space interface documentation",
            "vim-minimal|8.2.2637-21.el9|x86_64|BaseOS|1||vi,rvi,view||A minimal version of the VIM editor",
            "vim-enhanced|8.2.2637-21.el9|x86_64|AppStream|0|vim-common|vim,vimdiff,rvim,vimtutor||A version of the VIM editor which includes recent enhancements",
            "vim-common|8.2.2637-21.el9|x86_64|AppStream|0||xxd||The common files needed by any version of the VIM editor",
            "nano|5.6.1-6.el9|x86_64|BaseOS|1||nano||A small text editor",
            "bash-completion|2.11-5.el9|noarch|BaseOS|0||||Programmable completion for Bash",
            "tree|1.8.0-10.el9|x86_64|BaseOS|0||tree||File system tree viewer",
            "lsof|4.94.0-3.el9|x86_64|BaseOS|0||lsof||A utility which lists open files on a Linux/UNIX system",
            "tmux|3.2a-5.el9|x86_64|BaseOS|0||tmux||A terminal multiplexer",
            "zsh|5.8-9.el9|x86_64|BaseOS|0||zsh||Powerful interactive shell",
            "git|2.43.5-1.el9_4|x86_64|AppStream|0|git-core|git||Fast Version Control System",
            "git-core|2.43.5-1.el9_4|x86_64|AppStream|0||||Core package of git with minimal functionality",
            "openssl|3.0.7-27.el9|x86_64|BaseOS|1||openssl||Utilities from the general purpose cryptography library with TLS implementation",
            "nmap|7.92-1.el9|x86_64|AppStream|0||nmap||Network exploration tool and security scanner",
            "cyber-range-tools|1.0-1.range|noarch|Range|0||smbclient,winrm-shell,ftp,hashcrack,pwguess,webscan,vulnlookup,ticket-request,xorcat,vigenere,morse,catcher,browse,siem,cloudctl,toyvm||Cyber Range practice tools (simulated; only on the range's own boxes, in no repository)",
            "httpd|2.4.62-1.el9|x86_64|AppStream|0|httpd-core,httpd-filesystem,httpd-tools,mod_lua||httpd.service|Apache HTTP Server",
            "httpd-core|2.4.62-1.el9|x86_64|AppStream|0|httpd-filesystem|s:httpd,s:apachectl||httpd minimal core",
            "httpd-filesystem|2.4.62-1.el9|noarch|AppStream|0||||The basic directory layout for the Apache HTTP Server",
            "httpd-tools|2.4.62-1.el9|x86_64|AppStream|0||htpasswd,ab||Tools for use with the Apache HTTP Server",
            "mod_lua|2.4.62-1.el9|x86_64|AppStream|0||||Lua language module for the Apache HTTP Server",
            "httpd-manual|2.4.62-1.el9|noarch|AppStream|0|httpd|||Documentation for the Apache HTTP Server",
            "mod_ssl|2.4.62-1.el9|x86_64|AppStream|0|httpd|||SSL/TLS module for the Apache HTTP Server",
            "nginx|1.20.1-20.el9|x86_64|AppStream|0||s:nginx|nginx.service|A high performance web server and reverse proxy server",
            "vsftpd|3.0.5-6.el9|x86_64|AppStream|0||s:vsftpd|vsftpd.service|Very Secure Ftp Daemon",
            "mariadb-server|10.5.22-1.el9_2|x86_64|AppStream|0|mariadb|p:/usr/libexec/mariadbd,mysqld_safe|mariadb.service|The MariaDB server and related files",
            "mariadb|10.5.22-1.el9_2|x86_64|AppStream|0||mysql,mariadb||A very fast and robust SQL database server",
            "php|8.0.30-1.el9_2|x86_64|AppStream|0||php||PHP scripting language for creating dynamic web sites",
            "cockpit|311.2-1.el9|x86_64|BaseOS|0|cockpit-ws,cockpit-system|||Web Console for Linux servers",
            "cockpit-ws|311.2-1.el9|x86_64|BaseOS|0||s:cockpit-ws|cockpit.socket|Cockpit Web Service",
            "cockpit-system|311.2-1.el9|noarch|BaseOS|0||||Cockpit admin interface package for configuring and troubleshooting a system",
            "podman|5.2.2-9.el9_5|x86_64|AppStream|0|containers-common,conmon,crun|podman||Manage Pods, Containers and Container Images",
            "containers-common|1-90.el9_5|x86_64|AppStream|0||||Common configuration and documentation for containers",
            "conmon|2.1.12-1.el9|x86_64|AppStream|0||||OCI container runtime monitor",
            "crun|1.16.1-1.el9|x86_64|AppStream|0||||OCI runtime written in C",
            "skopeo|1.16.1-2.el9_5|x86_64|AppStream|0|containers-common|skopeo||Inspect container images and repositories on registries",
            "buildah|1.37.5-1.el9_5|x86_64|AppStream|0|containers-common|buildah||A command line tool used for creating OCI Images",
            "container-tools|1-14.el9|noarch|AppStream|0|podman,skopeo,buildah|||A meta-package with all the container tools",
            "stress-ng|0.17.08-1.el9|x86_64|AppStream|0||stress-ng||Stress test a computer system in various ways",
            "sysstat|12.5.4-9.el9|x86_64|AppStream|0||sar,iostat,mpstat,pidstat||Collection of performance monitoring tools for Linux",
            "nmap-ncat|7.92-3.el9|x86_64|AppStream|0||nc,ncat||Nmap's Netcat replacement",
            "tcpdump|4.99.0-9.el9|x86_64|AppStream|0||s:tcpdump||A network traffic monitoring tool",
            "words|3.0-36.el9|noarch|BaseOS|0||||A dictionary of English words for the /usr/share/dict directory",
            "redhat-release|9.5-0.6.el9|x86_64|BaseOS|1||||Red Hat Enterprise Linux release file",
            "ncurses|6.2-10.20210508.el9|x86_64|BaseOS|1||clear,tput,reset,infocmp||Ncurses support utilities",
            "selinux-policy-targeted|38.1.45-3.el9_5|noarch|BaseOS|1||||SELinux targeted policy configuration",
            "chkconfig|1.24-1.el9|x86_64|BaseOS|1||s:alternatives,s:chkconfig||A system tool for maintaining the /etc/rc*.d hierarchy",
            "bc|1.07.1-14.el9|x86_64|BaseOS|1||bc||GNU's bc (a numeric processing language) and dc (a calculator)",
            "glibc-common|2.34-125.el9_5.1|x86_64|BaseOS|1||getent||Common binaries and locale data for glibc",
            "net-tools|2.0-0.64.20160912git.el9|x86_64|BaseOS|0||netstat||Basic networking tools (old; RHEL 9 uses ip and ss)",
            "binutils|2.35.2-54.el9|x86_64|BaseOS|0||strings||A GNU collection of binary utilities",
            "traceroute|2.1.1-1.el9|x86_64|BaseOS|0||traceroute||Traces the route taken by packets over an IPv4/IPv6 network",
            "cracklib-dicts|2.9.6-27.el9|x86_64|BaseOS|1||||The standard CrackLib dictionaries",
        };

        static Packages()
        {
            foreach (var row in Table)
            {
                var p = row.Split('|');
                var k = new Pkg { Name = p[0], Ver = p[1], Arch = p[2], Repo = p[3], Base = p[4] == "1", Summary = p[8] };
                if (p[5].Length > 0) k.Requires.AddRange(p[5].Split(','));
                if (p[6].Length > 0) k.Bins.AddRange(p[6].Split(','));
                if (p[7].Length > 0) k.Units.AddRange(p[7].Split(','));
                k.SizeKb = 120 + (Math.Abs(FwHash.Of(k.Name)) % 4000);
                All.Add(k);
                byName[k.Name] = k;
            }
            InitConfigs();
        }

        public static Pkg Get(string name) { Pkg p; byName.TryGetValue(name, out p); return p; }

        /// <summary>Which package has a program (for "command not found" hints and dnf provides).</summary>
        public static string WhoProvides(string prog)
        {
            foreach (var p in All)
                foreach (var b in p.Bins)
                {
                    string n = b.StartsWith("s:", StringComparison.Ordinal) ? b.Substring(2) : b;
                    if (n == prog) return p.Name;
                }
            return null;
        }
        public static string BinPath(string b) { return b.StartsWith("s:", StringComparison.Ordinal) ? "/usr/sbin/" + b.Substring(2) : b.StartsWith("p:", StringComparison.Ordinal) ? b.Substring(2) : "/usr/bin/" + b; }

        /// <summary>The files a package owns (for rpm -ql / -qf), incl. its configuration.</summary>
        public static List<string> Files(Pkg p)
        {
            var l = new List<string>();
            foreach (var b in p.Bins) l.Add(BinPath(b));
            foreach (var u in p.Units) l.Add("/usr/lib/systemd/system/" + u);
            foreach (var kv in Configs) if (kv.Value.Pkg == p.Name) l.Add(kv.Key);
            foreach (var d in DocFiles(p)) l.Add(d);
            l.Sort(StringComparer.Ordinal);
            return l;
        }
        public static IEnumerable<string> DocFiles(Pkg p)
        {
            string dir = "/usr/share/doc/" + p.Name;
            switch (p.Name)
            {
                case "chrony": yield return dir + "/README"; yield return dir + "/FAQ"; break;
                case "openssh": yield return dir + "/README"; break;
                case "bash": yield return dir + "/FAQ"; break;
                case "autofs": yield return dir + "/README"; break;
                case "sudo": yield return dir + "/README.md"; break;
                case "firewalld": yield return dir + "/README.md"; break;
                case "lvm2": yield return dir + "/README"; break;
                case "tuned": yield return dir + "/README.md"; break;
                case "podman": yield return dir + "/README.md"; break;
                case "nfs-utils": yield return dir + "/README"; break;
                case "httpd-core": yield return dir + "/README"; break;
                case "cronie": yield return dir + "/README"; break;
            }
        }

        public class ConfigFile { public string Pkg; public string Text; public int Mode = 0x1A4; public bool Config = true; }
        public static readonly Dictionary<string, ConfigFile> Configs = new Dictionary<string, ConfigFile>(StringComparer.Ordinal);
        static void C(string pkg, string path, string text) { Configs[path] = new ConfigFile { Pkg = pkg, Text = text }; }
        static void C(string pkg, string path, string text, int mode) { Configs[path] = new ConfigFile { Pkg = pkg, Text = text, Mode = mode }; }
    }

    public partial class Machine
    {
        public const string RpmDb = "/var/lib/rpm/certlab-installed";

        public Dictionary<string, string[]> InstalledPackages()
        {
            var d = new Dictionary<string, string[]>(StringComparer.Ordinal);
            string t = V == null ? null : V.Cat(RpmDb);
            if (t == null) return d;
            foreach (var line in t.Split('\n'))
            {
                var p = line.Split('|');
                if (p.Length >= 4) d[p[0]] = p;
            }
            return d;
        }
        void WriteRpmDb(Dictionary<string, string[]> d)
        {
            var sb = new StringBuilder();
            foreach (var kv in d.OrderBy(x => x.Key, StringComparer.Ordinal)) sb.Append(string.Join("|", kv.Value)).Append('\n');
            SysWrite(RpmDb, sb.ToString(), 0x1A4, "system_u:object_r:rpm_var_lib_t:s0");
        }

        /// <summary>Puts a package's files on disk and records it (no dependency handling here: dnf does that).</summary>
        public void InstallPkg(Pkg p, string repo)
        {
            foreach (var b in p.Bins)
            {
                string path = Packages.BinPath(b);
                string name = Vfs.Base(path);
                var n = V.Put(path, null, 0x1ED, 0, 0, null);
                n.Text = null; n.BinSize = 40000 + name.Length * 3313; n.Prog = name; n.Pkg = p.Name;
                if (name == "sudo" || name == "passwd" || name == "su" || name == "mount" || name == "umount" || name == "crontab" || name == "at" || name == "chage" || name == "gpasswd" || name == "newgrp") n.Mode = 0x9ED;   // setuid root
                if (name == "crontab" || name == "at") n.Mode = 0x9ED;
            }
            foreach (var u in p.Units)
            {
                string text = Packages.UnitText(u);
                if (text == null) continue;
                var n = V.Put("/usr/lib/systemd/system/" + u, text);
                n.Pkg = p.Name;
            }
            foreach (var kv in Packages.Configs)
            {
                if (kv.Value.Pkg != p.Name) continue;
                if (kv.Key.EndsWith("/", StringComparison.Ordinal)) { V.EnsureDir(kv.Key.TrimEnd('/'), kv.Value.Mode, null); continue; }
                if (V.Exists(kv.Key) && kv.Value.Config) continue;   // %config(noreplace)
                var n = V.Put(kv.Key, kv.Value.Text, kv.Value.Mode, 0, 0, null);
                n.Pkg = p.Name;
            }
            foreach (var d in Packages.DocFiles(p)) { var n = V.Put(d, Packages.DocText(p, d)); n.Pkg = p.Name; }
            Packages.AfterInstall(this, p);
            var db = InstalledPackages();
            db[p.Name] = new[] { p.Name, p.Ver, p.Arch, repo ?? "@System", Now.ToString(CultureInfo.InvariantCulture) };
            WriteRpmDb(db);
        }

        public void RemovePkg(Pkg p)
        {
            foreach (var b in p.Bins) V.RemoveG(Packages.BinPath(b));
            foreach (var u in p.Units)
            {
                StopService(u, null);
                V.RemoveG("/usr/lib/systemd/system/" + u);
                foreach (var w in new[] { "multi-user.target.wants", "sockets.target.wants", "timers.target.wants", "graphical.target.wants" }) V.RemoveG("/etc/systemd/system/" + w + "/" + u);
            }
            foreach (var kv in Packages.Configs)
            {
                if (kv.Value.Pkg != p.Name || kv.Key.EndsWith("/", StringComparison.Ordinal)) continue;
                // changed configuration files are kept as .rpmsave
                var cur = V.Cat(kv.Key);
                if (cur != null && cur != kv.Value.Text && kv.Value.Config) { V.Put(kv.Key + ".rpmsave", cur); }
                V.RemoveG(kv.Key);
            }
            foreach (var d in Packages.DocFiles(p)) V.RemoveG(d);
            var db = InstalledPackages();
            db.Remove(p.Name);
            WriteRpmDb(db);
        }
    }
}
