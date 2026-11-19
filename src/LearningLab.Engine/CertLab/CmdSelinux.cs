// Cert Lab - SELinux tools: getenforce, setenforce, sestatus, chcon, restorecon, semanage (fcontext, port,
// boolean, login), getsebool, setsebool, matchpathcon, ausearch, sealert, audit2why, fixfiles.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CertLab
{
    public partial class Machine
    {
        public const string SeBoolFile = "/var/lib/selinux/targeted/active/booleans.local";
        public const string SeSubsFile = "/etc/selinux/targeted/contexts/files/file_contexts.subs";

        /// <summary>Persistent booleans (setsebool -P) come back at every boot.</summary>
        public void LoadSeBools()
        {
            var t = V.Cat(SeBoolFile);
            if (t == null) return;
            foreach (var line in t.Split('\n'))
            {
                var p = line.Split('=');
                if (p.Length == 2) SeBools[p[0].Trim()] = p[1].Trim() == "1" ? "on" : "off";
            }
        }
        public Dictionary<string, string> SePersistentBools()
        {
            var d = new Dictionary<string, string>();
            var t = V.Cat(SeBoolFile);
            if (t != null) foreach (var line in t.Split('\n')) { var p = line.Split('='); if (p.Length == 2) d[p[0].Trim()] = p[1].Trim() == "1" ? "on" : "off"; }
            return d;
        }
    }

    public static partial class Cmd
    {
        static readonly string[] ExtraTypes = { "httpd_sys_content_t", "httpd_sys_rw_content_t", "httpd_sys_script_exec_t", "httpd_user_content_t", "httpd_log_t", "httpd_config_t", "httpd_cache_t", "public_content_t", "public_content_rw_t", "samba_share_t", "nfs_t", "container_file_t", "container_share_t", "container_ro_file_t", "user_home_t", "user_home_dir_t", "admin_home_t", "ssh_home_t", "home_root_t", "tmp_t", "user_tmp_t", "var_t", "var_log_t", "var_lib_t", "etc_t", "usr_t", "bin_t", "default_t", "mnt_t", "boot_t", "root_t", "shadow_t", "passwd_file_t", "net_conf_t", "mysqld_db_t", "mysqld_log_t", "ftp_home_t", "xen_image_t", "virt_image_t", "svirt_sandbox_file_t", "cert_t", "unlabeled_t", "lib_t", "man_t", "device_t", "system_cron_spool_t", "user_cron_spool_t", "cron_spool_t", "mail_spool_t", "auditd_log_t", "chronyd_conf_t", "systemd_unit_file_t", "NetworkManager_etc_t", "NetworkManager_etc_rw_t", "selinux_config_t", "sshd_key_t", "hostname_etc_t", "iso9660_t", "dosfs_t", "swapfile_t", "fixed_disk_device_t", "etc_runtime_t", "proc_t", "sysfs_t", "firewalld_etc_rw_t", "var_lib_nfs_t", "var_run_t", "container_var_lib_t" };
        static bool KnownFileType(string t) { return ExtraTypes.Contains(t) || Selinux.Rules.Any(r => r[1] == t); }
        static readonly string[] PortTypeNames = { "http_port_t", "http_cache_port_t", "ssh_port_t", "ntp_port_t", "dns_port_t", "nfs_port_t", "mysqld_port_t", "postgresql_port_t", "smtp_port_t", "ftp_port_t", "websm_port_t", "syslogd_port_t", "rsh_port_t", "unreserved_port_t", "reserved_port_t", "squid_port_t", "pop_port_t", "ldap_port_t", "kerberos_port_t", "vnc_port_t", "xserver_port_t", "redis_port_t", "transproxy_port_t" };

        static void RegSelinux()
        {
            P("getenforce", (sh, a, io) => { io.L(sh.M.SeMode == "enforcing" ? "Enforcing" : sh.M.SeMode == "permissive" ? "Permissive" : "Disabled"); return 0; });
            P("selinuxenabled", (sh, a, io) => sh.M.SeMode == "disabled" ? 1 : 0);
            P("setenforce", Setenforce); P("sestatus", Sestatus); P("chcon", Chcon); P("restorecon", Restorecon); P("setfiles", Restorecon);
            P("semanage", Semanage); P("getsebool", Getsebool); P("setsebool", Setsebool); P("matchpathcon", Matchpathcon);
            P("ausearch", Ausearch); P("sealert", Sealert); P("audit2why", Audit2); P("audit2allow", Audit2);
            P("fixfiles", (sh, a, io) =>
            {
                if (sh.P.Cr.Uid != 0) { io.E("fixfiles: must be run as root"); return 1; }
                if (a.Contains("onboot")) { sh.M.V.WriteText(sh.P, "/.autorelabel", a.Contains("-F") ? "-F \n" : "\n", false); io.L("System will relabel on next boot"); return 0; }
                if (a.Contains("restore") || a.Contains("relabel")) { sh.M.RelabelAll(); return 0; }
                io.E("Usage: fixfiles [-F] [-B] [-T nthreads] { -f | onboot | relabel | restore | check | verify } [[dir/file] ... ]");
                return 1;
            });
            P("semodule", (sh, a, io) =>
            {
                if (a.Contains("-l")) { foreach (var x in new[] { "abrt", "accountsd", "apache", "auditadm", "chronyd", "container", "cron", "dbus", "firewalld", "ftp", "mysql", "networkmanager", "nfs", "ntp", "podman", "samba", "ssh", "sudo", "systemd", "unconfined" }) io.L(x); return 0; }
                if (sh.P.Cr.Uid != 0) { io.E("libsemanage.semanage_create_store: Could not read from module store (Permission denied)."); return 1; }
                return 0;
            });
        }

        static int Setenforce(Sh sh, List<string> a, Io io)
        {
            var m = sh.M;
            if (a.Count != 2) { io.E("usage:  setenforce [ Enforcing | Permissive | 1 | 0 ]"); return 1; }
            if (m.SeMode == "disabled") { io.E("setenforce: SELinux is disabled"); return 1; }
            if (sh.P.Cr.Uid != 0) { io.E("setenforce:  security_setenforce() failed:  Permission denied"); return 1; }
            string v = a[1].ToLowerInvariant();
            if (v == "1" || v == "enforcing") m.SeMode = "enforcing";
            else if (v == "0" || v == "permissive") m.SeMode = "permissive";
            else { io.E("usage:  setenforce [ Enforcing | Permissive | 1 | 0 ]"); return 1; }
            m.Log("kernel", 0, 5, "SELinux:  " + (m.SeMode == "enforcing" ? "enforcing" : "permissive") + "=" + (m.SeMode == "enforcing" ? 1 : 0) + " old_enforcing=" + (m.SeMode == "enforcing" ? 0 : 1) + " auid=0 ses=1", null);
            return 0;
        }

        static string ConfigMode(Machine m)
        {
            var t = m.V.Cat("/etc/selinux/config") ?? "";
            var mm = Regex.Match(t, "(?m)^\\s*SELINUX\\s*=\\s*(\\w+)");
            return mm.Success ? mm.Groups[1].Value.ToLowerInvariant() : "enforcing";
        }

        static int Sestatus(Sh sh, List<string> a, Io io)
        {
            var m = sh.M;
            if (m.SeMode == "disabled") { io.L("SELinux status:                 disabled"); return 0; }
            io.L("SELinux status:                 enabled\nSELinuxfs mount:                /sys/fs/selinux\nSELinux root directory:         /etc/selinux\nLoaded policy name:             targeted\nCurrent mode:                   " + m.SeMode + "\nMode from config file:          " + ConfigMode(m) + "\nPolicy MLS status:              enabled\nPolicy deny_unknown status:     allowed\nMemory protection checking:     actual (secure)\nMax kernel policy version:      33");
            if (a.Contains("-b"))
            {
                io.L("\nPolicy booleans:");
                foreach (var b in Selinux.Booleans.OrderBy(x => x[0], StringComparer.Ordinal)) io.L(b[0].PadRight(32) + (Selinux.BoolOn(m, b[0]) ? "on" : "off"));
            }
            return 0;
        }

        static int Chcon(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "Rvhu:r:t:l:", "recursive|R,verbose|v,no-dereference|h,user=|u,role=|r,type=|t,range=|l,reference=,dereference", "chcon");
            if (o.Error != null) return Fail(io, o);
            var m = sh.M;
            if (m.SeMode == "disabled") { io.E("chcon: can't apply partial context to unlabeled file (SELinux is disabled)"); return 1; }
            var args = new List<string>(o.Args);
            string full = null, refCtx = null;
            if (o.Has("reference"))
            {
                try { var rl = m.V.Resolve(sh.P, o.Get("reference"), true); if (!rl.Exists) { io.E("chcon: failed to get security context of '" + o.Get("reference") + "': No such file or directory"); return 1; } refCtx = rl.Node.Ctx; }
                catch (VfsEx e) { io.E("chcon: " + e.Message); return 1; }
            }
            else if (!o.Has("t") && !o.Has("u") && !o.Has("r") && !o.Has("l"))
            {
                if (args.Count < 2) { io.E("chcon: missing operand\nTry 'chcon --help' for more information."); return 1; }
                full = args[0]; args.RemoveAt(0);
                if (full.Split(':').Length < 4) { io.E("chcon: invalid context: '" + full + "'"); return 1; }
            }
            if (args.Count == 0) { io.E("chcon: missing operand\nTry 'chcon --help' for more information."); return 1; }
            string type = o.Get("t");
            if (type != null && !KnownFileType(type) && !type.EndsWith("_t", StringComparison.Ordinal)) { io.E("chcon: failed to change context of '" + args[0] + "' to 'unconfined_u:object_r:" + type + ":s0': Invalid argument"); return 1; }
            int st = 0;
            Action<Loc, string> apply = null;
            apply = (l, path) =>
            {
                var n = l.Node;
                if (sh.P.Cr.Uid != 0 && n.Uid != sh.P.Cr.Uid) { io.E("chcon: failed to change context of '" + path + "' to '" + n.Ctx + "': Operation not permitted"); st = 1; return; }
                string old = n.Ctx;
                var parts = (n.Ctx ?? "system_u:object_r:unlabeled_t:s0").Split(':').ToList();
                while (parts.Count < 4) parts.Add("s0");
                if (refCtx != null) n.Ctx = refCtx;
                else if (full != null) n.Ctx = full;
                else
                {
                    if (o.Has("u")) parts[0] = o.Get("u");
                    if (o.Has("r")) parts[1] = o.Get("r");
                    if (type != null) parts[2] = type;
                    if (o.Has("l")) { parts = parts.Take(3).ToList(); parts.Add(o.Get("l")); }
                    n.Ctx = string.Join(":", parts.ToArray());
                }
                if (l.Fs != null && (l.Fs.Type == "nfs4" || l.Fs.Type == "iso9660" || l.Fs.Type == "proc")) { n.Ctx = old; io.E("chcon: failed to change context of '" + path + "' to '" + (full ?? type) + "': Operation not supported"); st = 1; return; }
                if (o.Has("v")) io.L("changing security context of '" + path + "'");
                if (o.Has("R") && n.T == 'd')
                    foreach (var k in Vfs.Sorted(n.Kids.Keys))
                    {
                        string cp = path.TrimEnd('/') + "/" + k;
                        try { var cl = m.V.Resolve(sh.P, cp, false); if (cl.Exists) apply(cl, cp); } catch (VfsEx) { }
                    }
            };
            foreach (var p in args)
            {
                try
                {
                    var l = m.V.Resolve(sh.P, p, !o.Has("h"));
                    if (!l.Exists) { io.E("chcon: cannot access '" + p + "': No such file or directory"); st = 1; continue; }
                    apply(l, p);
                }
                catch (VfsEx e) { io.E("chcon: cannot access '" + p + "': " + e.Message); st = 1; }
            }
            return st;
        }

        static int Restorecon(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "RrvFnipe:f:WDTx0:", "", a[0]);
            if (o.Error != null) { io.E(o.Error.Split('\n')[0] + "\nusage:  restorecon [-iIDFmnprRv0x] [-e excludedir] pathname...\nusage:  restorecon [-iIDFmnprRv0x] [-e excludedir] -f filename"); return 1; }
            var m = sh.M;
            if (m.SeMode == "disabled") return 0;
            if (o.Args.Count == 0 && !o.Has("f")) { io.E("usage:  restorecon [-iIDFmnprRv0x] [-e excludedir] pathname...\nusage:  restorecon [-iIDFmnprRv0x] [-e excludedir] -f filename"); return 1; }
            if (sh.P.Cr.Uid != 0) { foreach (var p in o.Args) io.E("restorecon: Could not set context for " + p + ":  Permission denied"); return 255; }
            bool rec = o.Has("R") || o.Has("r");
            bool verbose = o.Has("v"), force = o.Has("F"), dry = o.Has("n");
            int st = 0;
            Action<Loc, string> fix = null;
            fix = (l, vpath) =>
            {
                var n = l.Node;
                string gp = l.GPath;
                if (l.Fs != null && (l.Fs.Type == "nfs4" || l.Fs.Type == "proc" || l.Fs.Type == "sysfs" || l.Fs.Type == "iso9660" || l.Fs.Type == "overlay")) return;
                if (l.Mnt != null && l.Mnt.Opts != null && l.Mnt.Opts.Contains("context=")) return;
                string ty = Selinux.DefaultType(m, gp);
                string old = n.Ctx ?? "system_u:object_r:unlabeled_t:s0";
                string nw = force ? Selinux.DefaultContext(m, gp, n.T) : Selinux.WithType(old, ty);
                if (nw != old)
                {
                    if (!dry) n.Ctx = nw;
                    if (verbose) io.L("Relabeled " + vpath + " from " + old + " to " + nw);
                }
                if (rec && n.T == 'd')
                    foreach (var k in Vfs.Sorted(n.Kids.Keys))
                    {
                        string cp = vpath.TrimEnd('/') + "/" + k;
                        try
                        {
                            var cl = m.V.Resolve(sh.P, cp, false);
                            if (!cl.Exists) continue;
                            if (cl.Node.T == 'd' && cl.Mnt != l.Mnt && cl.Mnt != null && (cl.Mnt.FsType == "nfs4" || cl.Mnt.FsType == "proc" || cl.Mnt.FsType == "sysfs" || cl.Mnt.FsType == "overlay")) continue;
                            fix(cl, cp);
                        }
                        catch (VfsEx) { }
                    }
            };
            var paths = new List<string>(o.Args);
            if (o.Has("f")) { try { paths.AddRange(m.V.ReadText(sh.P, o.Get("f")).Split('\n').Where(x => x.Trim().Length > 0)); } catch (VfsEx e) { io.E("restorecon: " + e.Message); return 1; } }
            foreach (var p in paths)
            {
                try
                {
                    var l = m.V.Resolve(sh.P, p, false);
                    if (!l.Exists) { io.E(a[0] + ": lstat(" + p + ") failed: No such file or directory"); st = 255; continue; }
                    fix(l, Vfs.Norm(Vfs.Join(sh.P.Cwd, p)));
                }
                catch (VfsEx e) { io.E(a[0] + ": lstat(" + p + ") failed: " + e.Message); st = 255; }
            }
            return st;
        }

        static int Matchpathcon(Sh sh, List<string> a, Io io)
        {
            var m = sh.M;
            bool verify = a.Contains("-V");
            int st = 0;
            foreach (var p in a.Skip(1).Where(x => !x.StartsWith("-", StringComparison.Ordinal)))
            {
                string full = Vfs.Norm(Vfs.Join(sh.P.Cwd, p));
                string want = Selinux.DefaultContext(m, full, 'f');
                want = "system_u" + want.Substring(want.IndexOf(':'));
                if (!verify) { io.L(full + "\t" + want); continue; }
                try
                {
                    var l = m.V.Resolve(sh.P, full, false);
                    if (!l.Exists) { io.L(full + " has context <<none>>, should be " + want); st = 1; continue; }
                    if (Selinux.TypeOf(l.Node.Ctx) == Selinux.TypeOf(want)) io.L(full + " verified.");
                    else { io.L(full + " has context " + l.Node.Ctx + ", should be " + want); st = 1; }
                }
                catch (VfsEx) { st = 1; }
            }
            return st;
        }

        // ------------------------------------------------------------ booleans
        static int Getsebool(Sh sh, List<string> a, Io io)
        {
            var m = sh.M;
            if (m.SeMode == "disabled") { io.E("getsebool:  SELinux is disabled"); return 1; }
            if (a.Count < 2) { io.E("usage:  getsebool -a or getsebool boolean..."); return 1; }
            if (a[1] == "-a") { foreach (var b in Selinux.Booleans.OrderBy(x => x[0], StringComparer.Ordinal)) io.L(b[0] + " --> " + (Selinux.BoolOn(m, b[0]) ? "on" : "off")); return 0; }
            int st = 0;
            foreach (var n in a.Skip(1))
            {
                if (!Selinux.KnownBool(n)) { io.E("Error getting active value for " + n); st = 1; continue; }
                io.L(n + " --> " + (Selinux.BoolOn(m, n) ? "on" : "off"));
            }
            return st;
        }

        static int Setsebool(Sh sh, List<string> a, Io io)
        {
            var m = sh.M;
            var args = a.Skip(1).ToList();
            bool perm = args.Remove("-P");
            args.Remove("-N"); args.Remove("-V");
            if (args.Count == 0) { io.E("usage:  setsebool [ -NPV ] boolean value | bool1=val1 bool2=val2..."); return 1; }
            if (m.SeMode == "disabled") { io.E("setsebool:  SELinux is disabled."); return 1; }
            var pairs = new List<string[]>();
            if (args.Count == 2 && !args[0].Contains("=")) pairs.Add(new[] { args[0], args[1] });
            else foreach (var x in args) { var p = x.Split('='); if (p.Length != 2) { io.E("usage:  setsebool [ -NPV ] boolean value | bool1=val1 bool2=val2..."); return 1; } pairs.Add(p); }
            if (sh.P.Cr.Uid != 0) { io.E(perm ? "Cannot set persistent booleans without managed policy." : "Could not change active booleans: Permission denied"); return 1; }
            foreach (var p in pairs)
            {
                string v = p[1].ToLowerInvariant();
                string on = v == "1" || v == "on" || v == "true" ? "on" : v == "0" || v == "off" || v == "false" ? "off" : null;
                if (on == null) { io.E("Illegal value for boolean " + p[0] + "=" + p[1]); return 1; }
                if (!Selinux.KnownBool(p[0])) { io.E("Boolean " + p[0] + " is not defined"); return 1; }
            }
            foreach (var p in pairs)
            {
                string v = p[1].ToLowerInvariant();
                string on = v == "1" || v == "on" || v == "true" ? "on" : "off";
                m.SeBools[p[0]] = on;
                if (perm)
                {
                    var d = m.SePersistentBools();
                    d[p[0]] = on;
                    m.V.EnsureDir("/var/lib/selinux/targeted/active", 0x1C0, "system_u:object_r:semanage_store_t:s0");
                    m.SysWrite(Machine.SeBoolFile, string.Join("", d.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Key + "=" + (x.Value == "on" ? 1 : 0) + "\n").ToArray()), 0x180, "system_u:object_r:semanage_store_t:s0");
                }
            }
            return 0;
        }

        // ------------------------------------------------------------ semanage
        static int Semanage(Sh sh, List<string> a, Io io)
        {
            var m = sh.M;
            const string usage = "usage: semanage [-h]\n                {import,export,login,user,port,ibpkey,ibendport,interface,module,node,fcontext,boolean,permissive,dontaudit}\n                ...";
            if (a.Count < 2) { io.E(usage + "\nsemanage: error: the following arguments are required: subcommand"); return 2; }
            string sub = a[1];
            var rest = a.Skip(2).ToList();
            bool list = rest.Contains("-l") || rest.Contains("--list");
            if (sh.P.Cr.Uid != 0) { io.E("ValueError: SELinux policy is not managed or store cannot be accessed."); return 1; }   // even listing reads the policy store
            if (m.SeMode == "disabled" && !list) { io.E("ValueError: SELinux policy is not managed or store cannot be accessed."); return 1; }
            Func<string, string> val = flag => { for (int i = 0; i < rest.Count; i++) { if (rest[i] == flag && i + 1 < rest.Count) return rest[i + 1]; if (rest[i].StartsWith(flag, StringComparison.Ordinal) && flag.Length == 2 && rest[i].Length > 2 && !rest[i].StartsWith("--", StringComparison.Ordinal)) return rest[i].Substring(2); if (flag.StartsWith("--", StringComparison.Ordinal) && rest[i].StartsWith(flag + "=", StringComparison.Ordinal)) return rest[i].Substring(flag.Length + 1); } return null; };
            Func<string, bool> has = f => rest.Any(x => x == f || (f.Length == 2 && x.StartsWith("-", StringComparison.Ordinal) && !x.StartsWith("--", StringComparison.Ordinal) && x.Length > 2 && x.Substring(1).All(char.IsLetter) && x.Contains(f.Substring(1)) && x.Length <= 4));
            var valueFlags = new HashSet<string> { "-t", "--type", "-p", "--proto", "-f", "--ftype", "-e", "--equal", "-s", "--seuser", "-r", "--range" };
            var positional = new List<string>();
            for (int i = 0; i < rest.Count; i++) { if (valueFlags.Contains(rest[i])) { i++; continue; } if (!rest[i].StartsWith("-", StringComparison.Ordinal) || rest[i] == "-") positional.Add(rest[i]); }
            bool add = has("-a") || rest.Contains("--add"), del = has("-d") || rest.Contains("--delete"), mod = has("-m") || rest.Contains("--modify");
            switch (sub)
            {
                case "fcontext":
                    {
                        string local = m.V.Cat(Selinux.LocalFile) ?? "";
                        var rules = Selinux.LocalRules(m);
                        string subs = m.V.Cat(Machine.SeSubsFile) ?? "";
                        if (list)
                        {
                            bool localOnly = has("-C") || rest.Contains("--locallist");
                            if (!localOnly)
                            {
                                io.L("SELinux fcontext                                   type               Context\n");
                                foreach (var r in Selinux.Rules) io.L(r[0].PadRight(50) + " all files          system_u:object_r:" + r[1] + ":s0 ");
                            }
                            if (rules.Count > 0) { io.L((localOnly ? "SELinux fcontext                                   type               Context\n\n" : "\nSELinux Local fcontext Customizations\n\n") + string.Join("\n", rules.Select(r => r[0].PadRight(50) + " all files          " + r[1] + " ").ToArray())); }
                            var eq = subs.Split('\n').Where(x => x.Trim().Length > 0).ToList();
                            if (eq.Count > 0) { io.L("\nSELinux Local fcontext Equivalence \n"); foreach (var e in eq) { var p = Regex.Split(e.Trim(), "\\s+"); if (p.Length >= 2) io.L(p[0] + " = " + p[1]); } }
                            return 0;
                        }
                        if (val("-e") != null || val("--equal") != null)
                        {
                            string target = val("-e") ?? val("--equal");
                            if (positional.Count == 0) { io.E("ValueError: Requires file_spec"); return 1; }
                            string src = positional[0];
                            var lines = subs.Split('\n').Where(x => x.Trim().Length > 0).ToList();
                            if (add)
                            {
                                if (lines.Any(x => Regex.Split(x.Trim(), "\\s+")[0] == src)) { io.E("ValueError: Equivalence class for " + src + " already exists"); return 1; }
                                lines.Add(src + " " + target);
                            }
                            else if (del) lines.RemoveAll(x => Regex.Split(x.Trim(), "\\s+")[0] == src);
                            m.SysWrite(Machine.SeSubsFile, string.Join("\n", lines.ToArray()) + (lines.Count > 0 ? "\n" : ""), 0x1A4, "system_u:object_r:file_context_t:s0");
                            return 0;
                        }
                        if (!add && !del && !mod) { io.E("usage: semanage fcontext [-h] [-n] [-N] [-S STORE] [ --add ( -t TYPE -f FTYPE -r RANGE -s SEUSER | -e EQUAL ) FILE_SPEC | --delete ( -t TYPE -f FTYPE | -e EQUAL ) FILE_SPEC | --deleteall | --extract | --list [-C] | --modify ( -t TYPE -f FTYPE -r RANGE -s SEUSER | -e EQUAL ) FILE_SPEC ]"); return 2; }
                        if (positional.Count == 0) { io.E("ValueError: Requires file_spec"); return 1; }
                        string spec = positional[positional.Count - 1];
                        if (!spec.StartsWith("/", StringComparison.Ordinal)) { io.E("ValueError: File spec " + spec + " conflicts with equivalency rule '/ /'"); return 1; }
                        string ty = val("-t") ?? val("--type");
                        var existing = rules.FindIndex(r => r[0] == spec);
                        var newLines = local.Split('\n').Where(x => x.Trim().Length > 0 && !x.StartsWith("#", StringComparison.Ordinal)).ToList();
                        if (add || mod)
                        {
                            if (ty == null) { io.E("ValueError: SELinux Type is required"); return 1; }
                            if (!KnownFileType(ty)) { io.E("ValueError: Type " + ty + " is invalid, must be a file or device type"); return 1; }
                            if (add && existing >= 0) { io.E("ValueError: File context for " + spec + " already defined"); return 1; }
                            if (mod && existing < 0 && !Selinux.Rules.Any(r => r[0] == spec)) { io.E("ValueError: File context for " + spec + " is not defined"); return 1; }
                            newLines.RemoveAll(x => Regex.Split(x.Trim(), "\\s+")[0] == spec);
                            newLines.Add(spec + "    system_u:object_r:" + ty + ":s0");
                        }
                        else
                        {
                            if (existing < 0) { io.E(Selinux.Rules.Any(r => r[0] == spec) ? "ValueError: File context for " + spec + " is defined in policy, cannot be deleted" : "ValueError: File context for " + spec + " is not defined"); return 1; }
                            newLines.RemoveAll(x => Regex.Split(x.Trim(), "\\s+")[0] == spec);
                        }
                        m.SysWrite(Selinux.LocalFile, "# This file is auto-generated by libsemanage\n# Do not edit directly.\n\n" + string.Join("\n", newLines.ToArray()) + (newLines.Count > 0 ? "\n" : ""), 0x1A4, "system_u:object_r:file_context_t:s0");
                        return 0;
                    }
                case "port":
                    {
                        if (list)
                        {
                            bool localOnly = has("-C") || rest.Contains("--locallist");
                            io.L("SELinux Port Type              Proto    Port Number\n");
                            var types = Selinux.PortTypes.Select(p => p[0] + "\t" + p[1]).Concat(m.SeLocalPorts.Select(p => p.Type + "\t" + p.Proto)).Distinct().OrderBy(x => x, StringComparer.Ordinal);
                            foreach (var tp in types)
                            {
                                var p = tp.Split('\t');
                                var loc = m.SeLocalPorts.Where(x => x.Type == p[0] && x.Proto == p[1]).Select(x => x.Port.ToString(CultureInfo.InvariantCulture)).ToList();
                                var pol = Selinux.PortTypes.Where(x => x[0] == p[0] && x[1] == p[1]).SelectMany(x => x[2].Split(',')).ToList();
                                var all = localOnly ? loc : loc.Concat(pol).ToList();
                                if (all.Count == 0) continue;
                                io.L(p[0].PadRight(30) + " " + p[1].PadRight(8) + " " + string.Join(", ", all.ToArray()));
                            }
                            return 0;
                        }
                        string ty = val("-t") ?? val("--type"), proto = val("-p") ?? val("--proto");
                        if (positional.Count == 0) { io.E("ValueError: Port is required"); return 1; }
                        if (proto == null) { io.E("ValueError: Protocol udp or tcp is required"); return 1; }
                        if (proto != "tcp" && proto != "udp" && proto != "sctp" && proto != "dccp") { io.E("ValueError: Protocol has to be one of udp, tcp, dccp, sctp"); return 1; }
                        int port;
                        if (!int.TryParse(positional[0], out port) || port < 1 || port > 65535) { io.E("ValueError: Invalid Port"); return 1; }
                        var cur = m.SeLocalPorts.FirstOrDefault(x => x.Proto == proto && x.Port == port);
                        string polType = null;
                        foreach (var pt in Selinux.PortTypes) if (pt[1] == proto) foreach (var part in pt[2].Split(',')) { var r = part.Split('-'); int lo = int.Parse(r[0]), hi = r.Length > 1 ? int.Parse(r[1]) : lo; if (port >= lo && port <= hi && r.Length == 1) polType = pt[0]; }
                        if (add || mod)
                        {
                            if (ty == null) { io.E("ValueError: Type is required"); return 1; }
                            if (!PortTypeNames.Contains(ty)) { io.E("ValueError: Type " + ty + " is invalid, must be a port type"); return 1; }
                            if (add && (cur != null || polType != null)) { io.E("ValueError: Port " + proto + "/" + port + " already defined"); return 1; }
                            if (mod && cur == null && polType == null) { io.E("ValueError: Port @" + proto + "/" + port + " is not defined"); return 1; }
                            m.SeLocalPorts.RemoveAll(x => x.Proto == proto && x.Port == port);
                            m.SeLocalPorts.Add(new SePort { Type = ty, Proto = proto, Port = port });
                        }
                        else if (del)
                        {
                            if (cur == null) { io.E(polType != null ? "ValueError: Port " + proto + "/" + port + " is defined in policy, cannot be deleted" : "ValueError: Port " + proto + "/" + port + " is not defined"); return 1; }
                            m.SeLocalPorts.Remove(cur);
                        }
                        else { io.E("usage: semanage port [-h] [-n] [-N] [-S STORE] [ --add -t TYPE -p PROTOCOL -r RANGE port_name | port_range | --delete -p PROTOCOL port_name | port_range | --deleteall | --extract | --list [-C] | --modify -t TYPE -p PROTOCOL -r RANGE port_name | port_range ]"); return 2; }
                        return 0;
                    }
                case "boolean":
                    {
                        if (list)
                        {
                            io.L("SELinux boolean                State  Default Description\n");
                            var pers = m.SePersistentBools();
                            foreach (var b in Selinux.Booleans.OrderBy(x => x[0], StringComparer.Ordinal))
                            {
                                if ((has("-C") || rest.Contains("--locallist")) && !pers.ContainsKey(b[0])) continue;
                                string def; if (!pers.TryGetValue(b[0], out def)) def = b[1];
                                io.L(b[0].PadRight(30) + " (" + (Selinux.BoolOn(m, b[0]) ? "on " : "off") + "  ,  " + (def == "on" ? "on " : "off") + ")  " + b[2]);
                            }
                            return 0;
                        }
                        if (positional.Count == 0 || !mod) { io.E("usage: semanage boolean [-h] [-n] [-N] [-S STORE] [ --extract | --deleteall | --list [-C] | --modify ( --on | --off ) boolean ]"); return 2; }
                        bool on = rest.Contains("--on") || has("-1"), off = rest.Contains("--off") || has("-0");
                        if (!on && !off) { io.E("ValueError: You must specify one of the following values: --on, --off"); return 1; }
                        return Setsebool(sh, new List<string> { "setsebool", "-P", positional[0], on ? "on" : "off" }, io);
                    }
                case "login":
                    if (list) { io.L("\nLogin Name           SELinux User         MLS/MCS Range        Service\n\n__default__          unconfined_u         s0-s0:c0.c1023       *\nroot                 unconfined_u         s0-s0:c0.c1023       *"); return 0; }
                    io.E("Cert Lab: semanage login can list (-l) the mappings; changing them is not part of the RHCSA objectives and is not simulated.");
                    return 1;
                case "permissive":
                    if (list) { io.L("\nBuiltin Permissive Types \n\n"); return 0; }
                    return 0;
                case "-h": case "--help": io.L(usage); return 0;
                default: io.E(usage + "\nsemanage: error: argument subcommand: invalid choice: '" + sub + "'"); return 2;
            }
        }

        // ------------------------------------------------------------ the audit log
        static List<string> AvcLines(Machine m)
        {
            var t = m.V.Cat("/var/log/audit/audit.log") ?? "";
            return t.Split('\n').Where(x => x.StartsWith("type=AVC", StringComparison.Ordinal)).ToList();
        }

        static int Ausearch(Sh sh, List<string> a, Io io)
        {
            var m = sh.M;
            if (sh.P.Cr.Uid != 0) { io.E("Error opening /var/log/audit/audit.log (Permission denied)"); return 1; }
            // -ts takes words like recent / today; the simple parser cannot read two-letter options, so handle by hand
            string type = null, comm = null; bool recent = false, today = false;
            for (int i = 1; i < a.Count; i++)
            {
                if ((a[i] == "-m" || a[i] == "--message") && i + 1 < a.Count) type = a[++i];
                else if ((a[i] == "-c" || a[i] == "--comm") && i + 1 < a.Count) comm = a[++i];
                else if ((a[i] == "-ts" || a[i] == "--start") && i + 1 < a.Count) { var w = a[++i]; recent = w == "recent"; today = w == "today"; }
            }
            var all = (m.V.Cat("/var/log/audit/audit.log") ?? "").Split('\n').Where(x => x.Length > 0).ToList();
            var hits = all.Where(x =>
            {
                if (type != null && !type.Split(',').Any(ty => x.StartsWith("type=" + ty.Trim().ToUpperInvariant() + " ", StringComparison.Ordinal))) return false;
                if (comm != null && !x.Contains("comm=\"" + comm + "\"")) return false;
                var tm = Regex.Match(x, "audit\\((\\d+)\\.");
                long t = tm.Success ? long.Parse(tm.Groups[1].Value) : 0;
                if (recent && t < m.Now - 600) return false;
                if (today && m.LocalTime(t).Date != m.LocalTime(m.Now).Date) return false;
                return true;
            }).ToList();
            if (hits.Count == 0) { io.E("<no matches>"); return 1; }
            foreach (var h in hits)
            {
                var tm = Regex.Match(h, "audit\\((\\d+)\\.");
                long t = tm.Success ? long.Parse(tm.Groups[1].Value) : m.Now;
                io.L("----\ntime->" + m.LocalTime(t).ToString("ddd MMM d HH:mm:ss yyyy", CultureInfo.InvariantCulture) + "\n" + h);
            }
            return 0;
        }

        static string Advice(Machine m, string avc)
        {
            var g = Regex.Match(avc, "denied\\s+\\{ (\\w+) \\} for\\s+pid=\\d+ comm=\"([^\"]*)\" name=\"([^\"]*)\".*scontext=\\S+:(\\w+):s0 tcontext=\\S+:object_r:(\\w+):s0\\S* tclass=(\\w+)");
            if (!g.Success) return "";
            string perm = g.Groups[1].Value, comm = g.Groups[2].Value, name = g.Groups[3].Value, src = g.Groups[4].Value, tgt = g.Groups[5].Value, cls = g.Groups[6].Value;
            var sb = new StringBuilder();
            sb.Append("SELinux is preventing " + (comm == "httpd" ? "/usr/sbin/httpd" : comm) + " from " + perm + " access on the " + cls + " " + name + ".\n\n");
            if (cls == "tcp_socket")
            {
                string want = src == "httpd_t" ? "http_port_t" : src == "sshd_t" ? "ssh_port_t" : "<port type>";
                sb.Append("*****  Plugin bind_ports (99.5 confidence) suggests   ************************\n\nIf you want to allow " + comm + " to bind to network port <port>\nThen you need to modify the port type.\nDo\n# semanage port -a -t " + want + " -p tcp <port>\n    where <port> is the port number you chose.\n\n");
                return sb.ToString();
            }
            string wantType = src == "httpd_t" ? "httpd_sys_content_t" : src == "container_t" ? "container_file_t" : src == "sshd_t" ? "ssh_home_t" : "<type>";
            sb.Append("*****  Plugin restorecon (" + (tgt == "user_home_t" || tgt == "user_tmp_t" || tgt == "default_t" ? "92.2" : "99.5") + " confidence) suggests   ************************\n\nIf you want to fix the label.\n" + name + " default label should be " + wantType + ".\nThen you can run restorecon. The access attempt may have been stopped due to insufficient permissions to access a parent directory in which case try to change the following command accordingly.\nDo\n# /sbin/restorecon -v <path to " + name + ">\n\n");
            if (src == "httpd_t" && (tgt == "user_home_t" || tgt == "user_home_dir_t")) sb.Append("*****  Plugin catchall_boolean (7.83 confidence) suggests   ******************\n\nIf you want to allow httpd to read user content\nThen you must tell SELinux about this by enabling the 'httpd_read_user_content' boolean.\n\nDo\nsetsebool -P httpd_read_user_content 1\n\n");
            if (src == "httpd_t" && tgt == "nfs_t") sb.Append("*****  Plugin catchall_boolean (89.3 confidence) suggests   ******************\n\nIf you want to allow httpd to use nfs\nThen you must tell SELinux about this by enabling the 'httpd_use_nfs' boolean.\n\nDo\nsetsebool -P httpd_use_nfs 1\n\n");
            sb.Append("Additional Information:\nSource Context                system_u:system_r:" + src + ":s0\nTarget Context                unconfined_u:object_r:" + tgt + ":s0\nTarget Objects                " + name + " [ " + cls + " ]\n");
            return sb.ToString();
        }

        static int Sealert(Sh sh, List<string> a, Io io)
        {
            var m = sh.M;
            if (sh.P.Cr.Uid != 0) { io.E("Error opening /var/log/audit/audit.log (Permission denied)"); return 1; }
            var avcs = AvcLines(m);
            if (a.Contains("-l"))
            {
                if (avcs.Count == 0) { io.E("Error\nquery_alerts error (1003): id (" + a.Last() + ") not found"); return 1; }
                io.P(Advice(m, avcs[avcs.Count - 1]));
                return 0;
            }
            if (a.Contains("-a"))
            {
                io.L("100% done\n" +(avcs.Count == 0 ? "\nfound 0 alerts in /var/log/audit/audit.log" : "found " + avcs.Select(x => Regex.Replace(x, "audit\\([^)]*\\)|pid=\\d+|ino=\\d+", "")).Distinct().Count() + " alerts in /var/log/audit/audit.log\n--------------------------------------------------------------------------------\n"));
                foreach (var x in avcs.GroupBy(x => Regex.Replace(x, "audit\\([^)]*\\)|pid=\\d+|ino=\\d+", "")).Select(g => g.Last())) io.L(Advice(m, x) + "\n--------------------------------------------------------------------------------\n");
                return 0;
            }
            io.E("Usage: sealert [-b] [-s] [-l id] [-a file]");
            return 1;
        }

        static int Audit2(Sh sh, List<string> a, Io io)
        {
            string text = a.Contains("-a") || io.In.IsTty ? string.Join("\n", AvcLines(sh.M).ToArray()) : io.In.ReadAll();
            var lines = text.Split('\n').Where(x => x.Contains("avc:")).ToList();
            if (lines.Count == 0) { if (a[0] == "audit2why") io.E("Nothing to do"); return 0; }
            foreach (var l in lines)
            {
                if (a[0] == "audit2why")
                {
                    io.L(l + "\n\n\tWas caused by:\n\t\tMissing type enforcement (TE) allow rule.\n\n\t\tYou can use audit2allow to generate a loadable module to allow this access.\n");
                }
                else
                {
                    var g = Regex.Match(l, "denied\\s+\\{ (\\w+) \\}.*scontext=\\S+:(\\w+):s0 tcontext=\\S+:object_r:(\\w+):s0\\S* tclass=(\\w+)");
                    if (g.Success) io.L("\n#============= " + g.Groups[2].Value + " ==============\nallow " + g.Groups[2].Value + " " + g.Groups[3].Value + ":" + g.Groups[4].Value + " " + g.Groups[1].Value + ";\n\n(Cert Lab: a custom policy module is rarely the right fix. Check the file labels (restorecon), the port types (semanage port) and the booleans first.)");
                }
            }
            return 0;
        }
    }
}
