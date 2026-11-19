// Cert Lab - a small model of SELinux's targeted policy: default file labels (like file_contexts), port types,
// booleans, and the few access rules the exam cares about (web servers, containers, the shadow file). Denials are
// written to /var/log/audit/audit.log as AVC lines, the way the real audit daemon records them.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace CertLab
{
    public static class Selinux
    {
        /// <summary>Built-in file context rules: regex (whole path), type. More specific rules come first.</summary>
        public static readonly string[][] Rules = new[]
        {
            new[] { "/etc/shadow.*", "shadow_t" }, new[] { "/etc/gshadow.*", "shadow_t" },
            new[] { "/etc/passwd", "passwd_file_t" }, new[] { "/etc/passwd-", "passwd_file_t" }, new[] { "/etc/group", "passwd_file_t" }, new[] { "/etc/group-", "passwd_file_t" },
            new[] { "/etc/hosts", "net_conf_t" }, new[] { "/etc/resolv\\.conf.*", "net_conf_t" }, new[] { "/etc/hostname", "hostname_etc_t" },
            new[] { "/etc/selinux(/.*)?", "selinux_config_t" },
            new[] { "/etc/ssh/ssh_host_.*_key", "sshd_key_t" }, new[] { "/etc/ssh/ssh_host_.*_key\\.pub", "sshd_key_t" },
            new[] { "/etc/httpd(/.*)?", "httpd_config_t" },
            new[] { "/etc/NetworkManager/system-connections(/.*)?", "NetworkManager_etc_rw_t" }, new[] { "/etc/NetworkManager(/.*)?", "NetworkManager_etc_t" },
            new[] { "/etc/systemd/system(/.*)?", "systemd_unit_file_t" },
            new[] { "/etc/crontab", "system_cron_spool_t" }, new[] { "/etc/cron\\.d(/.*)?", "system_cron_spool_t" },
            new[] { "/etc/chrony\\.conf", "chronyd_conf_t" }, new[] { "/etc/chrony\\.keys", "chronyd_keys_t" },
            new[] { "/etc/firewalld(/.*)?", "firewalld_etc_rw_t" }, new[] { "/etc/sudoers", "etc_t" },
            new[] { "/etc/containers(/.*)?", "etc_t" },
            new[] { "/etc(/.*)?", "etc_t" },
            new[] { "/root/\\.ssh(/.*)?", "ssh_home_t" }, new[] { "/root(/.*)?", "admin_home_t" },
            new[] { "/home/[^/]+/\\.ssh(/.*)?", "ssh_home_t" }, new[] { "/home/[^/]+/public_html(/.*)?", "httpd_user_content_t" },
            new[] { "/home/[^/]+/\\.local/share/containers(/.*)?", "container_ro_file_t" },
            new[] { "/home/[^/]+/.+", "user_home_t" }, new[] { "/home/[^/]+", "user_home_dir_t" }, new[] { "/home", "home_root_t" },
            new[] { "/tmp(/.*)?", "tmp_t" }, new[] { "/var/tmp(/.*)?", "tmp_t" },
            new[] { "/var/www/cgi-bin(/.*)?", "httpd_sys_script_exec_t" }, new[] { "/var/www(/.*)?", "httpd_sys_content_t" },
            new[] { "/var/log/audit(/.*)?", "auditd_log_t" }, new[] { "/var/log/httpd(/.*)?", "httpd_log_t" }, new[] { "/var/log/journal(/.*)?", "var_log_t" }, new[] { "/var/log(/.*)?", "var_log_t" },
            new[] { "/var/spool/cron(/.*)?", "user_cron_spool_t" }, new[] { "/var/spool/at(/.*)?", "user_cron_spool_t" }, new[] { "/var/spool/mail(/.*)?", "mail_spool_t" },
            new[] { "/var/ftp(/.*)?", "public_content_t" },
            new[] { "/var/lib/containers(/.*)?", "container_var_lib_t" }, new[] { "/var/lib/nfs(/.*)?", "var_lib_nfs_t" }, new[] { "/var/lib(/.*)?", "var_lib_t" },
            new[] { "/var/run(/.*)?", "var_run_t" }, new[] { "/run(/.*)?", "var_run_t" },
            new[] { "/var(/.*)?", "var_t" }, new[] { "/srv(/.*)?", "var_t" },
            new[] { "/usr/lib/systemd/system(/.*)?", "systemd_unit_file_t" }, new[] { "/usr/lib/systemd/user(/.*)?", "systemd_unit_file_t" },
            new[] { "/usr/s?bin(/.*)?", "bin_t" }, new[] { "/usr/local/s?bin(/.*)?", "bin_t" }, new[] { "/usr/libexec(/.*)?", "bin_t" },
            new[] { "/usr/share/doc(/.*)?", "usr_t" }, new[] { "/usr/share/man(/.*)?", "man_t" }, new[] { "/usr(/.*)?", "usr_t" }, new[] { "/opt(/.*)?", "usr_t" },
            new[] { "/bin", "bin_t" }, new[] { "/sbin", "bin_t" }, new[] { "/lib", "lib_t" }, new[] { "/lib64", "lib_t" },
            new[] { "/mnt(/.*)?", "mnt_t" }, new[] { "/media(/.*)?", "mnt_t" },
            new[] { "/boot(/.*)?", "boot_t" }, new[] { "/dev(/.*)?", "device_t" }, new[] { "/proc(/.*)?", "proc_t" }, new[] { "/sys(/.*)?", "sysfs_t" },
            new[] { "/\\.autorelabel", "etc_runtime_t" },
            new[] { "/", "root_t" },
        };

        static readonly Dictionary<string, Regex> rxCache = new Dictionary<string, Regex>();
        public static Regex Rx(string pattern)
        {
            Regex r;
            lock (rxCache)
            {
                if (!rxCache.TryGetValue(pattern, out r))
                {
                    try { r = new Regex("^(?:" + pattern + ")$", RegexOptions.CultureInvariant); } catch (ArgumentException) { r = new Regex("^$"); }
                    rxCache[pattern] = r;
                }
            }
            return r;
        }

        public const string LocalFile = "/etc/selinux/targeted/contexts/files/file_contexts.local";

        /// <summary>The local rules added with semanage fcontext -a: (regex, full context).</summary>
        public static List<string[]> LocalRules(Machine m)
        {
            var l = new List<string[]>();
            string t = m.V.Cat(LocalFile);
            if (t == null) return l;
            foreach (var raw in t.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                var p = Regex.Split(line, "\\s+");
                if (p.Length >= 2) l.Add(new[] { p[0], p[p.Length - 1] });
            }
            return l;
        }

        /// <summary>The type the policy says a path should have (what restorecon sets).</summary>
        public static string DefaultType(Machine m, string path)
        {
            if (m != null)
            {
                // equivalences (semanage fcontext -a -e /var/www /web): /web/x is labelled like /var/www/x
                var subs = m.V.Cat(Machine.SeSubsFile);
                if (subs != null)
                    foreach (var line in subs.Split('\n'))
                    {
                        var sp = Regex.Split(line.Trim(), "\\s+");
                        if (sp.Length < 2 || sp[0].Length < 2) continue;
                        if (path == sp[0] || path.StartsWith(sp[0].TrimEnd('/') + "/", StringComparison.Ordinal)) { path = sp[1].TrimEnd('/') + path.Substring(sp[0].TrimEnd('/').Length); break; }
                    }
                // local customisations win; among them the last added (semanage lists them in order)
                var loc = LocalRules(m);
                for (int i = loc.Count - 1; i >= 0; i--)
                    if (Rx(loc[i][0]).IsMatch(path)) return TypeOf(loc[i][1]);
                foreach (var e in m.ContainerHomeRules()) if (Rx(e[0]).IsMatch(path)) return e[1];
            }
            foreach (var r in Rules) if (Rx(r[0]).IsMatch(path)) return r[1];
            return "default_t";
        }

        public static string DefaultContext(Machine m, string path, char t)
        {
            string ty = DefaultType(m, path);
            string user = (path.StartsWith("/home/", StringComparison.Ordinal) || path.StartsWith("/root", StringComparison.Ordinal)) && ty != "home_root_t" ? "unconfined_u" : "system_u";
            if (path == "/root") user = "system_u";
            return user + ":object_r:" + ty + ":s0";
        }

        public static string TypeOf(string ctx)
        {
            if (string.IsNullOrEmpty(ctx)) return "unlabeled_t";
            var p = ctx.Split(':');
            return p.Length >= 3 ? p[2] : ctx;
        }
        public static string WithType(string ctx, string type)
        {
            var p = (ctx ?? "system_u:object_r:unlabeled_t:s0").Split(':');
            if (p.Length < 4) return "system_u:object_r:" + type + ":s0";
            p[2] = type;
            return string.Join(":", p);
        }

        // ------------------------------------------------------------ ports
        public static readonly string[][] PortTypes = new[]
        {
            new[] { "http_port_t", "tcp", "80,81,443,488,8008,8009,8443,9000" },
            new[] { "http_cache_port_t", "tcp", "8080,8118,8123,10001-10010" },
            new[] { "ssh_port_t", "tcp", "22" },
            new[] { "ntp_port_t", "udp", "123" },
            new[] { "dns_port_t", "tcp", "53,853" }, new[] { "dns_port_t", "udp", "53,853" },
            new[] { "nfs_port_t", "tcp", "2049" }, new[] { "nfs_port_t", "udp", "2049" },
            new[] { "mysqld_port_t", "tcp", "1186,3306,63132-63164" },
            new[] { "postgresql_port_t", "tcp", "5432,9898" },
            new[] { "smtp_port_t", "tcp", "25,465,587" },
            new[] { "ftp_port_t", "tcp", "21,989,990" },
            new[] { "websm_port_t", "tcp", "9090" },
            new[] { "syslogd_port_t", "tcp", "601,20514" }, new[] { "syslogd_port_t", "udp", "514,601,20514" },
            new[] { "rsh_port_t", "tcp", "514" },
        };

        /// <summary>The type of a port: local (semanage port -a) first, then the policy's.</summary>
        public static string PortType(Machine m, string proto, int port)
        {
            foreach (var lp in m.SeLocalPorts) if (lp.Proto == proto && lp.Port == port) return lp.Type;
            foreach (var pt in PortTypes)
            {
                if (pt[1] != proto) continue;
                foreach (var part in pt[2].Split(','))
                {
                    var r = part.Split('-');
                    int a = int.Parse(r[0], CultureInfo.InvariantCulture), b = r.Length > 1 ? int.Parse(r[1], CultureInfo.InvariantCulture) : a;
                    if (port >= a && port <= b) return pt[0];
                }
            }
            return port < 1024 ? "reserved_port_t" : (port < 32768 ? "unreserved_port_t" : "ephemeral_port_t");
        }

        // ------------------------------------------------------------ booleans (name, default, description)
        public static readonly string[][] Booleans = new[]
        {
            new[] { "httpd_enable_homedirs", "off", "Allow httpd to read home directories" },
            new[] { "httpd_read_user_content", "off", "Allow httpd to read user content" },
            new[] { "httpd_can_network_connect", "off", "Allow HTTPD scripts and modules to connect to the network using TCP." },
            new[] { "httpd_can_network_connect_db", "off", "Allow HTTPD scripts and modules to connect to databases over the network." },
            new[] { "httpd_use_nfs", "off", "Allow httpd to access nfs file systems" },
            new[] { "httpd_builtin_scripting", "on", "Allow httpd to use built in scripting" },
            new[] { "httpd_can_sendmail", "off", "Allow http daemon to send mail" },
            new[] { "ftpd_anon_write", "off", "Determine whether ftpd can modify public files used for public file transfer services." },
            new[] { "ftpd_full_access", "off", "Determine whether ftpd can login to local users and can read and write all files on the system, governed by DAC." },
            new[] { "use_nfs_home_dirs", "off", "Support NFS home directories" },
            new[] { "samba_enable_home_dirs", "off", "Allow samba to share users home directories." },
            new[] { "samba_export_all_rw", "off", "Allow samba to share any file/directory read/write." },
            new[] { "container_manage_cgroup", "off", "Allow containers to manage cgroups" },
            new[] { "container_use_cephfs", "off", "Determine whether container can use ceph file system" },
            new[] { "virt_use_nfs", "on", "Allow confined virtual guests to manage nfs files" },
            new[] { "ssh_sysadm_login", "off", "Allow ssh logins as sysadm_r:sysadm_t" },
            new[] { "nis_enabled", "off", "Allow system to run with NIS" },
            new[] { "selinuxuser_execmod", "on", "Allow all unconfined executables to use libraries requiring text relocation that are not labeled textrel_shlib_t" },
            new[] { "deny_ptrace", "off", "Deny all processes the ability to ptrace other processes." },
            new[] { "mount_anyfile", "on", "Allow the mount commands to mount any directory or file." },
            new[] { "cron_userdomain_transition", "on", "Determine whether crond can execute jobs in the user domain as opposed to the the generic cronjob domain." },
            new[] { "antivirus_can_scan_system", "off", "Allow antivirus programs to read non security files on a system" },
        };

        public static bool BoolOn(Machine m, string name)
        {
            string v;
            if (m.SeBools.TryGetValue(name, out v)) return v == "on";
            foreach (var b in Booleans) if (b[0] == name) return b[1] == "on";
            return false;
        }
        public static bool KnownBool(string name) { return Booleans.Any(b => b[0] == name); }

        // ------------------------------------------------------------ access rules
        static readonly HashSet<string> HttpdRead = new HashSet<string> { "httpd_sys_content_t", "httpd_sys_rw_content_t", "httpd_sys_script_exec_t", "public_content_t", "public_content_rw_t", "httpd_config_t", "etc_t", "usr_t", "bin_t", "net_conf_t", "httpd_log_t", "cert_t", "locale_t", "passwd_file_t", "var_run_t", "tmp_t", "root_t", "var_t", "var_lib_t", "httpd_cache_t", "system_conf_t", "hostname_etc_t" };
        static readonly HashSet<string> HttpdDirsOk = new HashSet<string> { "root_t", "var_t", "home_root_t", "usr_t", "etc_t", "var_log_t", "var_lib_t", "var_run_t", "default_t", "mnt_t", "tmp_t", "admin_home_t", "user_home_dir_t" };
        static readonly HashSet<string> ContainerOk = new HashSet<string> { "container_file_t", "container_ro_file_t", "container_share_t", "container_var_lib_t", "etc_t", "usr_t", "bin_t" };

        public static bool Enforcing(Machine m) { return m.SeMode == "enforcing"; }

        public static bool AllowRead(Machine m, PCtx c, Loc l) { return Allow(m, c, l, "read"); }
        public static bool AllowWrite(Machine m, PCtx c, Loc l) { return Allow(m, c, l, "write"); }

        public static bool Allow(Machine m, PCtx c, Loc l, string perm)
        {
            if (m.SeMode == "disabled" || c == null || c.Domain == "unconfined_t" || c.Domain == "kernel_t" || c.Domain == "init_t") return true;
            var n = l.Node;
            string ty = TypeOf(n.Ctx);
            bool ok = true;
            if (c.Domain == "httpd_t")
            {
                if (n.T == 'd') ok = HttpdRead.Contains(ty) || HttpdDirsOk.Contains(ty) || ty == "httpd_user_content_t" || (ty == "user_home_t" && BoolOn(m, "httpd_enable_homedirs"));
                else if (perm == "read") ok = HttpdRead.Contains(ty) && ty != "tmp_t" && ty != "var_t" && ty != "root_t"
                        || (ty == "httpd_user_content_t" && (BoolOn(m, "httpd_enable_homedirs") || BoolOn(m, "httpd_read_user_content")))
                        || (ty == "user_home_t" && BoolOn(m, "httpd_read_user_content"))
                        || (ty == "nfs_t" && BoolOn(m, "httpd_use_nfs"));
                else ok = ty == "httpd_sys_rw_content_t" || ty == "public_content_rw_t" || ty == "httpd_log_t";
                if (ty == "nfs_t" && n.T == 'd') ok = BoolOn(m, "httpd_use_nfs");
            }
            else if (c.Domain == "container_t")
            {
                if (ty == "nfs_t") ok = BoolOn(m, "virt_use_nfs");
                else if (perm == "write") ok = ty == "container_file_t" || ty == "container_share_t" || ty == "tmp_t";
                else ok = ContainerOk.Contains(ty);
            }
            if (ok) return true;
            Avc(m, c, perm, n, l.Name, "file");
            return !Enforcing(m);
        }

        public static void Avc(Machine m, PCtx c, string perm, Inode n, string name, string cls)
        {
            string comm = c.Domain == "httpd_t" ? "httpd" : c.Domain == "container_t" ? "container" : c.Domain.Replace("_t", "");
            string tclass = n != null && n.T == 'd' ? "dir" : cls;
            Avc(m, c.Domain, comm, perm, name, n == null ? 0 : n.Ino, n == null ? "" : n.Ctx, tclass);
        }

        public static void Avc(Machine m, string domain, string comm, string perm, string name, int ino, string tctx, string tclass)
        {
            long t = m.Net.Now;
            int pid = m.ServicePid(comm == "httpd" ? "httpd.service" : comm);
            string line = string.Format(CultureInfo.InvariantCulture,
                "type=AVC msg=audit({0}.{1:000}:{2}): avc:  denied  {{ {3} }} for  pid={4} comm=\"{5}\" name=\"{6}\" dev=\"dm-0\" ino={7} scontext=system_u:system_r:{8}:s0 tcontext={9} tclass={10} permissive={11}\n",
                t, (m.AuditSerial * 37) % 1000, m.AuditSerial++, perm, pid, comm, name, ino, domain, tctx, tclass, Enforcing(m) ? 0 : 1);
            m.AppendLog("/var/log/audit/audit.log", line);
            if (m.PackageInstalled("setroubleshoot-server"))
                m.Log("setroubleshoot", 0, 4, "SELinux is preventing /usr/sbin/" + comm + " from " + perm + " access on the " + tclass + " " + name + ". For complete SELinux messages run: sealert -l " + m.AuditSerial.ToString(CultureInfo.InvariantCulture) + "-cert-lab", null);
        }
    }

    public class SePort { public string Type; public string Proto; public int Port; }
}
