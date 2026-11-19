// Cert Lab - what each daemon checks when systemd starts it (its configuration, the SELinux type of its ports,
// ports already taken), the processes it shows in ps, and what it does when it stops.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CertLab
{
    public static class Daemons
    {
        static readonly Dictionary<string, Func<Machine, ServiceState, UnitInfo, string>> starts = new Dictionary<string, Func<Machine, ServiceState, UnitInfo, string>>(StringComparer.Ordinal);
        static readonly Dictionary<string, Action<Machine, ServiceState>> stops = new Dictionary<string, Action<Machine, ServiceState>>(StringComparer.Ordinal);

        public static Func<Machine, ServiceState, UnitInfo, string> Get(string bin) { Func<Machine, ServiceState, UnitInfo, string> f; starts.TryGetValue(bin, out f); return f; }
        public static Action<Machine, ServiceState> GetStop(string bin) { Action<Machine, ServiceState> f; stops.TryGetValue(bin, out f); return f; }

        static int Proc(Machine m, ServiceState st, string user, string cmd, string domain)
        {
            var p = m.Spawn(user, cmd, st.Unit, "system_r:" + domain + ":s0");
            if (st.Pid == 0) st.Pid = p.Pid;
            return p.Pid;
        }

        /// <summary>Is the port free for this service (not used by another one)?</summary>
        static string PortTaken(Machine m, ServiceState st, int port)
        {
            foreach (var s in m.Services.Values) if (s != st && s.Active == "active" && s.Ports.Contains(port)) return s.Unit;
            foreach (var c in m.Containers) if (c.Running && c.Ports.Any(p => p.Host == port)) return "container " + c.Name;
            return null;
        }

        static Daemons()
        {
            starts["sshd"] = (m, st, u) =>
            {
                string err;
                var cfg = m.SshdConfig(out err);
                int pid = m.NextPid;
                if (err != null)
                {
                    m.Log("sshd", pid, 3, err, st.Unit);
                    m.Log("sshd", pid, 3, "/etc/ssh/sshd_config: terminating, 1 bad configuration options", st.Unit);
                    return "config";
                }
                string pv; var ports = new List<int>();
                if (cfg.TryGetValue("Port", out pv)) foreach (var p in pv.Split(',')) { int n; if (int.TryParse(p.Trim(), out n)) ports.Add(n); }
                if (ports.Count == 0) ports.Add(22);
                foreach (var port in ports)
                {
                    if (Selinux.Enforcing(m) && Selinux.PortType(m, "tcp", port) != "ssh_port_t")
                    {
                        m.Log("sshd", pid, 3, "error: Bind to port " + port + " on 0.0.0.0 failed: Permission denied.", st.Unit);
                        m.Log("sshd", pid, 3, "error: Bind to port " + port + " on :: failed: Permission denied.", st.Unit);
                        m.Log("sshd", pid, 2, "fatal: Cannot bind any address.", st.Unit);
                        Selinux.Avc(m, "sshd_t", "sshd", "name_bind", "", 0, "system_u:object_r:" + Selinux.PortType(m, "tcp", port) + ":s0", "tcp_socket");
                        return "bind";
                    }
                    var t = PortTaken(m, st, port);
                    if (t != null) { m.Log("sshd", pid, 3, "error: Bind to port " + port + " on 0.0.0.0 failed: Address already in use.", st.Unit); m.Log("sshd", pid, 2, "fatal: Cannot bind any address.", st.Unit); return "inuse"; }
                }
                st.Ports.AddRange(ports);
                Proc(m, st, "root", "sshd: /usr/sbin/sshd -D [listener] 0 of 10-100 startups", "sshd_t");
                foreach (var port in ports) { m.Log("sshd", st.Pid, 6, "Server listening on 0.0.0.0 port " + port + ".", st.Unit); m.Log("sshd", st.Pid, 6, "Server listening on :: port " + port + ".", st.Unit); }
                return null;
            };
            starts["httpd"] = (m, st, u) =>
            {
                var conf = Httpd.Load(m);
                int pid = m.NextPid;
                if (conf.Error != null) { m.Log("httpd", pid, 3, conf.Error, st.Unit); return "config"; }
                foreach (var w in conf.Warnings) m.Log("httpd", pid, 4, w, st.Unit);
                foreach (var port in conf.Ports)
                {
                    string ty = Selinux.PortType(m, "tcp", port);
                    if (Selinux.Enforcing(m) && ty != "http_port_t" && ty != "http_cache_port_t")
                    {
                        m.Log("httpd", pid, 3, "(13)Permission denied: AH00072: make_sock: could not bind to address [::]:" + port, st.Unit);
                        m.Log("httpd", pid, 3, "(13)Permission denied: AH00072: make_sock: could not bind to address 0.0.0.0:" + port, st.Unit);
                        m.Log("httpd", pid, 3, "no listening sockets available, shutting down", st.Unit);
                        m.Log("httpd", pid, 3, "AH00015: Unable to open logs", st.Unit);
                        Selinux.Avc(m, "httpd_t", "httpd", "name_bind", "", 0, "system_u:object_r:" + ty + ":s0", "tcp_socket");
                        return "bind";
                    }
                    var t = PortTaken(m, st, port);
                    if (t != null) { m.Log("httpd", pid, 3, "(98)Address already in use: AH00072: make_sock: could not bind to address [::]:" + port, st.Unit); m.Log("httpd", pid, 3, "no listening sockets available, shutting down", st.Unit); return "inuse"; }
                }
                st.Ports.AddRange(conf.Ports);
                Proc(m, st, "root", "/usr/sbin/httpd -DFOREGROUND", "httpd_t");
                for (int i = 0; i < 4; i++) Proc(m, st, "apache", "/usr/sbin/httpd -DFOREGROUND", "httpd_t");
                m.Log("httpd", st.Pid, 5, "Server configured, listening on: " + string.Join(", ", conf.Ports.Select(p => "port " + p).ToArray()), st.Unit);
                st.Tasks = 177; st.MemKb = 21400;
                return null;
            };
            stops["chronyd"] = (m, st) => { m.ChronySyncedAt = -1; m.ChronySource = ""; };
            starts["chronyd"] = (m, st, u) =>
            {
                var t = m.V.Cat("/etc/chrony.conf");
                if (t == null) { m.Log("chronyd", m.NextPid, 3, "Could not open configuration file /etc/chrony.conf : No such file or directory", st.Unit); return "config"; }
                Proc(m, st, "chrony", "/usr/sbin/chronyd -F 2", "chronyd_t");
                m.ChronySyncedAt = -1;
                m.Log("chronyd", st.Pid, 6, "chronyd version 4.5 starting (+CMDMON +NTP +REFCLOCK +RTC +PRIVDROP +SCFILTER +SIGND +ASYNCDNS +NTS +SECHASH +IPV6 +DEBUG)", st.Unit);
                return null;
            };
            starts["crond"] = (m, st, u) => { Proc(m, st, "root", "/usr/sbin/crond -n", "crond_t"); m.Log("crond", st.Pid, 6, "(CRON) INFO (Syslog will be used instead of sendmail.)", st.Unit); m.Log("crond", st.Pid, 6, "(CRON) INFO (running with inotify support)", st.Unit); return null; };
            starts["atd"] = (m, st, u) => { Proc(m, st, "root", "/usr/sbin/atd -f", "crond_t"); return null; };
            starts["rsyslogd"] = (m, st, u) => { Proc(m, st, "root", "/usr/sbin/rsyslogd -n", "syslogd_t"); return null; };
            starts["auditd"] = (m, st, u) => { Proc(m, st, "root", "/sbin/auditd", "auditd_t"); return null; };
            starts["tuned"] = (m, st, u) =>
            {
                Proc(m, st, "root", "/usr/bin/python3 -Es /usr/sbin/tuned -l -P", "tuned_t");
                string prof = (m.V.Cat("/etc/tuned/active_profile") ?? "").Trim();
                m.TunedProfile = prof.Length > 0 ? prof : "virtual-guest";
                return null;
            };
            stops["tuned"] = (m, st) => { };
            starts["NetworkManager"] = (m, st, u) => { Proc(m, st, "root", "/usr/sbin/NetworkManager --no-daemon", "NetworkManager_t"); m.NmStart(); return null; };
            starts["firewalld"] = (m, st, u) =>
            {
                Proc(m, st, "root", "/usr/bin/python3 -s /usr/sbin/firewalld --nofork --nopid", "firewalld_t");
                m.FwStart();
                foreach (var z in m.FwRuntime.Values) foreach (var s in z.Services.ToList()) if (!Firewall.ServiceNames(m).Contains(s)) m.Log("firewalld", st.Pid, 3, "ERROR: INVALID_SERVICE: " + s, st.Unit);
                return null;
            };
            stops["firewalld"] = (m, st) => { m.FwRunning = false; };
            starts["automount"] = (m, st, u) =>
            {
                if (m.V.Cat("/etc/auto.master") == null) { m.Log("automount", m.NextPid, 3, "lookup(file): file map /etc/auto.master missing or not readable", st.Unit); }
                Proc(m, st, "root", "/usr/sbin/automount --systemd-service --dont-check-daemon", "automount_t");
                foreach (var p in m.StartAutofsWithService(st)) m.Log("automount", st.Pid, 3, p, st.Unit);
                return null;
            };
            stops["automount"] = (m, st) => { m.StopAutofs(); };
            starts["rpc.nfsd"] = (m, st, u) => { st.Ports.Add(2049); return null; };
            starts["cockpit-ws"] = (m, st, u) => { Proc(m, st, "cockpit-ws", "/usr/libexec/cockpit-ws --for-tls-proxy --port=0", "cockpit_ws_t"); st.Ports.Add(9090); return null; };
            starts["nginx"] = (m, st, u) =>
            {
                var t = PortTaken(m, st, 80);
                if (t != null) { m.Log("nginx", m.NextPid, 2, "nginx: [emerg] bind() to 0.0.0.0:80 failed (98: Address already in use)", st.Unit); return "inuse"; }
                st.Ports.Add(80);
                Proc(m, st, "root", "nginx: master process /usr/sbin/nginx", "httpd_t");
                Proc(m, st, "nginx", "nginx: worker process", "httpd_t");
                return null;
            };
            starts["vsftpd"] = (m, st, u) =>
            {
                if (PortTaken(m, st, 21) != null) return "inuse";
                st.Ports.Add(21);
                Proc(m, st, "root", "/usr/sbin/vsftpd /etc/vsftpd/vsftpd.conf", "ftpd_t");
                return null;
            };
            starts["mariadbd"] = (m, st, u) =>
            {
                if (PortTaken(m, st, 3306) != null) return "inuse";
                st.Ports.Add(3306);
                m.V.EnsureDir("/var/lib/mysql", 0x1ED, null);
                Proc(m, st, "mysql", "/usr/libexec/mariadbd --basedir=/usr", "mysqld_t");
                return null;
            };
            starts["systemd-journald"] = (m, st, u) => { Proc(m, st, "root", "/usr/lib/systemd/systemd-journald", "syslogd_t"); if (m.State == "running") m.FinishJournal(); return null; };
            starts["systemd-logind"] = (m, st, u) => { Proc(m, st, "root", "/usr/lib/systemd/systemd-logind", "systemd_logind_t"); return null; };
        }
    }

    /// <summary>The Apache configuration: Listen, DocumentRoot, VirtualHost and UserDir, with a syntax check.</summary>
    public class Httpd
    {
        public List<int> Ports = new List<int>();
        public string DocRoot = "/var/www/html";
        public List<string[]> VHosts = new List<string[]>();   // port, servername, docroot
        public string UserDir;
        public string Error;
        public List<string> Warnings = new List<string>();
        public List<string> Aliases = new List<string>();   // "/url path"

        static readonly HashSet<string> Known = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ServerRoot", "Listen", "Include", "IncludeOptional", "User", "Group", "ServerAdmin", "ServerName", "ServerAlias", "DocumentRoot", "Directory", "/Directory", "DirectoryMatch", "/DirectoryMatch", "Files", "/Files", "FilesMatch", "/FilesMatch", "Location", "/Location", "LocationMatch", "/LocationMatch", "VirtualHost", "/VirtualHost", "IfModule", "/IfModule", "AllowOverride", "Require", "Options", "DirectoryIndex", "ErrorLog", "CustomLog", "LogLevel", "LogFormat", "AddDefaultCharset", "EnableSendfile", "EnableMMAP", "TypesConfig", "AddType", "AddEncoding", "AddOutputFilter", "MIMEMagicFile", "Alias", "ScriptAlias", "ErrorDocument", "UserDir", "Redirect", "RewriteEngine", "RewriteRule", "RewriteCond", "SSLEngine", "SSLCertificateFile", "SSLCertificateKeyFile", "LoadModule", "Timeout", "KeepAlive", "MaxKeepAliveRequests", "KeepAliveTimeout", "Header", "Order", "Allow", "Deny", "Satisfy", "AuthType", "AuthName", "AuthUserFile", "AuthBasicProvider", "RequireAll", "/RequireAll", "RequireAny", "/RequireAny", "ProxyPass", "ProxyPassReverse", "ServerTokens", "ServerSignature", "TraceEnable", "Protocols", "SetHandler", "AddHandler", "ExpiresActive", "ExpiresDefault", "DefaultRuntimeDir", "PidFile", "Mutex", "HostnameLookups", "UseCanonicalName", "ContentDigest", "IndexOptions", "IndexIgnore", "AddIcon", "DefaultIcon", "ReadmeName", "HeaderName", "AddLanguage", "LanguagePriority", "ForceType", "AcceptFilter", "LimitRequestBody", "SSLProtocol", "SSLCipherSuite", "SSLHonorCipherOrder", "SSLPassPhraseDialog", "SSLSessionCache", "SSLSessionCacheTimeout", "SSLRandomSeed", "SSLCryptoDevice" };

        public static Httpd Load(Machine m)
        {
            var h = new Httpd();
            var files = new List<string> { "/etc/httpd/conf/httpd.conf" };
            if (m.V.Cat(files[0]) == null) { h.Error = "httpd: Could not open configuration file /etc/httpd/conf/httpd.conf: No such file or directory"; return h; }
            try
            {
                var d = m.V.G("/etc/httpd/conf.d");
                if (d.Exists && d.IsDir) foreach (var k in d.Node.Kids.Keys.OrderBy(x => x, StringComparer.Ordinal)) if (k.EndsWith(".conf", StringComparison.Ordinal)) files.Add("/etc/httpd/conf.d/" + k);
            }
            catch (VfsEx) { }
            foreach (var f in files)
            {
                var text = m.V.Cat(f) ?? "";
                int n = 0;
                string[] vhost = null;
                int depth = 0;
                foreach (var raw in text.Split('\n'))
                {
                    n++;
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                    string word = line.TrimStart('<').Split(new[] { ' ', '\t', '>' }, StringSplitOptions.RemoveEmptyEntries)[0];
                    if (!Known.Contains(word))
                    {
                        h.Error = "AH00526: Syntax error on line " + n + " of " + f + ":\nInvalid command '" + word + "', perhaps misspelled or defined by a module not included in the server configuration";
                        return h;
                    }
                    if (line.StartsWith("</", StringComparison.Ordinal)) { depth--; if (word.Equals("/VirtualHost", StringComparison.OrdinalIgnoreCase) && vhost != null) { h.VHosts.Add(vhost); vhost = null; } continue; }
                    if (line.StartsWith("<", StringComparison.Ordinal))
                    {
                        if (!line.EndsWith(">", StringComparison.Ordinal)) { h.Error = "AH00526: Syntax error on line " + n + " of " + f + ":\n<" + word + "> directive missing closing '>'"; return h; }
                        depth++;
                        if (word.Equals("VirtualHost", StringComparison.OrdinalIgnoreCase))
                        {
                            var pm = Regex.Match(line, ":(\\d+)>");
                            vhost = new[] { pm.Success ? pm.Groups[1].Value : "80", "", h.DocRoot };
                        }
                        continue;
                    }
                    var args = Regex.Split(line, "\\s+").Skip(1).Select(x => x.Trim('"')).ToList();
                    if (word.Equals("Listen", StringComparison.OrdinalIgnoreCase) && args.Count > 0)
                    {
                        var a = args[0]; int colon = a.LastIndexOf(':');
                        int port; if (int.TryParse(colon >= 0 ? a.Substring(colon + 1) : a, out port) && !h.Ports.Contains(port)) h.Ports.Add(port);
                    }
                    else if (word.Equals("DocumentRoot", StringComparison.OrdinalIgnoreCase) && args.Count > 0)
                    {
                        if (vhost != null) vhost[2] = args[0]; else h.DocRoot = args[0];
                        if (!m.V.IsDirG(args[0])) h.Warnings.Add("AH00112: Warning: DocumentRoot [" + args[0] + "] does not exist");
                    }
                    else if (word.Equals("ServerName", StringComparison.OrdinalIgnoreCase) && args.Count > 0 && vhost != null) vhost[1] = args[0].Split(':')[0];
                    else if (word.Equals("UserDir", StringComparison.OrdinalIgnoreCase) && args.Count > 0 && depth > 0) { if (!args[0].Equals("disabled", StringComparison.OrdinalIgnoreCase)) h.UserDir = args[0]; }
                    else if (word.Equals("UserDir", StringComparison.OrdinalIgnoreCase) && args.Count > 0) { if (!args[0].Equals("disabled", StringComparison.OrdinalIgnoreCase)) h.UserDir = args[0]; }
                    else if (word.Equals("Alias", StringComparison.OrdinalIgnoreCase) && args.Count > 1) h.Aliases.Add(args[0] + " " + args[1]);
                }
                if (depth != 0) { h.Error = "AH00526: Syntax error on line " + n + " of " + f + ":\nExpected </" + "Directory> but saw end of file"; return h; }
            }
            if (h.Ports.Count == 0) { h.Error = "AH00534: httpd: Configuration error: No MPM loaded.\nno listening sockets available, shutting down"; return h; }
            return h;
        }
    }

    public partial class Machine
    {
        public List<string> StartAutofsWithService(ServiceState st) { return StartAutofs(); }
    }
}
