// Cert Lab - Tab completion like bash with bash-completion: command names, file names, and for some commands
// their sub-commands and arguments (systemctl verbs and units, nmcli, firewall-cmd, dnf, podman, semanage...).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CertLab
{
    public partial class Session
    {
        static readonly Dictionary<string, string[]> Sub = new Dictionary<string, string[]>
        {
            { "systemctl", new[] { "start", "stop", "restart", "reload", "status", "enable", "disable", "is-active", "is-enabled", "is-failed", "mask", "unmask", "daemon-reload", "list-units", "list-unit-files", "list-timers", "get-default", "set-default", "isolate", "cat", "edit", "show", "reboot", "poweroff", "rescue", "emergency", "default", "kill", "reset-failed", "list-dependencies", "--now", "--user", "--type=", "--all" } },
            { "nmcli", new[] { "connection", "device", "general", "networking", "radio", "monitor" } },
            { "firewall-cmd", new[] { "--state", "--reload", "--permanent", "--list-all", "--list-services", "--list-ports", "--add-service=", "--remove-service=", "--add-port=", "--remove-port=", "--get-default-zone", "--set-default-zone=", "--get-active-zones", "--get-zones", "--get-services", "--zone=", "--add-source=", "--remove-source=", "--add-rich-rule=", "--remove-rich-rule=", "--list-all-zones", "--runtime-to-permanent", "--add-masquerade", "--add-interface=", "--change-interface=", "--query-service=", "--info-service=", "--new-service=", "--complete-reload", "--list-rich-rules", "--add-forward-port=" } },
            { "dnf", new[] { "install", "remove", "erase", "update", "upgrade", "info", "list", "search", "provides", "whatprovides", "repolist", "repoinfo", "history", "clean", "makecache", "group", "module", "config-manager", "check-update", "reinstall", "downgrade", "autoremove", "--enablerepo=", "--disablerepo=", "-y" } },
            { "yum", new[] { "install", "remove", "update", "info", "list", "search", "provides", "repolist", "clean", "makecache", "group" } },
            { "podman", new[] { "run", "ps", "images", "pull", "push", "search", "login", "logout", "inspect", "start", "stop", "restart", "rm", "rmi", "logs", "exec", "build", "tag", "generate", "volume", "port", "top", "stats", "info", "version", "create", "kill", "commit", "system", "unshare", "cp" } },
            { "semanage", new[] { "fcontext", "port", "boolean", "login", "user", "module", "permissive", "interface", "node", "dontaudit", "export", "import" } },
            { "hostnamectl", new[] { "status", "set-hostname", "hostname", "--static", "--transient", "--pretty" } },
            { "timedatectl", new[] { "status", "set-timezone", "list-timezones", "set-ntp", "set-time", "show" } },
            { "tuned-adm", new[] { "list", "active", "profile", "recommend", "off", "verify", "profile_info", "auto_profile", "profile_mode" } },
            { "chronyc", new[] { "sources", "tracking", "sourcestats", "makestep", "activity", "waitsync" } },
            { "loginctl", new[] { "enable-linger", "disable-linger", "show-user", "list-users", "list-sessions", "user-status" } },
            { "journalctl", new[] { "-u", "-b", "-p", "-f", "-n", "-x", "-e", "-k", "-r", "--since", "--until", "--list-boots", "--disk-usage", "--no-pager", "_COMM=", "_PID=", "_UID=" } },
            { "grubby", new[] { "--default-kernel", "--default-index", "--info=", "--update-kernel=", "--args=", "--remove-args=", "--set-default=" } },
            { "ip", new[] { "address", "addr", "link", "route", "neighbour", "-br", "-4", "-6", "-c" } },
            { "subscription-manager", new[] { "status", "register", "repos", "list" } },
            { "skopeo", new[] { "inspect", "copy", "list-tags", "login" } },
            { "rpm", new[] { "-q", "-qa", "-qi", "-ql", "-qf", "-qc", "-qd", "-qR", "-ivh", "-Uvh", "-e", "-V", "--import", "--checksig" } },
        };
        static readonly Dictionary<string, string[]> Sub2 = new Dictionary<string, string[]>
        {
            { "nmcli connection", new[] { "show", "up", "down", "add", "modify", "mod", "delete", "reload", "edit", "clone" } },
            { "nmcli con", new[] { "show", "up", "down", "add", "modify", "mod", "delete", "reload" } },
            { "nmcli device", new[] { "status", "show", "connect", "disconnect", "reapply", "set", "wifi" } },
            { "nmcli dev", new[] { "status", "show", "connect", "disconnect", "reapply" } },
            { "nmcli general", new[] { "status", "hostname", "permissions", "logging" } },
            { "semanage fcontext", new[] { "-a", "-d", "-m", "-l", "-t", "-C" } },
            { "semanage port", new[] { "-a", "-d", "-m", "-l", "-t", "-p" } },
            { "semanage boolean", new[] { "-l", "-m", "--on", "--off" } },
            { "podman generate", new[] { "systemd" } },
            { "podman volume", new[] { "create", "ls", "rm", "inspect", "prune" } },
            { "dnf group", new[] { "list", "install", "info", "remove" } },
            { "dnf config-manager", new[] { "--add-repo", "--enable", "--disable", "--set-enabled", "--set-disabled" } },
        };

        /// <summary>What Tab can complete at the end of line.</summary>
        public Completion Candidates(string line)
        {
            var sh = CurrentSh;
            if (sh == null || !Waiting || Secret) return null;
            var c = new Completion();
            // the word being typed
            int start = line.Length;
            while (start > 0 && line[start - 1] != ' ' && line[start - 1] != '=' && line[start - 1] != ':' && line[start - 1] != '|' && line[start - 1] != ';' && line[start - 1] != '&' && line[start - 1] != '(') start--;
            string word = line.Substring(start);
            string before = line.Substring(0, start).TrimEnd();
            var prev = before.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            // after a pipe, ;, && etc. the next word is a command again
            int cmdAt = Math.Max(Math.Max(before.LastIndexOf('|'), before.LastIndexOf(';')), before.LastIndexOf('&'));
            var words = (cmdAt >= 0 ? before.Substring(cmdAt + 1) : before).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            while (words.Count > 0 && (words[0] == "sudo" || words[0] == "time" || words[0] == "nohup" || words[0] == "nice" || words[0] == "exec")) words.RemoveAt(0);
            c.Start = start;
            bool firstWord = words.Count == 0 && !(line.Length > start && false) && (start == 0 || line[start - 1] == ' ' || line[start - 1] == '|' || line[start - 1] == ';' || line[start - 1] == '&' || line[start - 1] == '(');
            if (words.Count == 0 && !word.Contains("/"))
            {
                var set = new SortedSet<string>(StringComparer.Ordinal);
                foreach (var b in Sh.Builtins.Keys) if (b.StartsWith(word, StringComparison.Ordinal)) set.Add(b);
                foreach (var f in sh.Funcs.Keys) if (f.StartsWith(word, StringComparison.Ordinal)) set.Add(f);
                foreach (var al in sh.Aliases.Keys) if (al.StartsWith(word, StringComparison.Ordinal)) set.Add(al);
                foreach (var d in (sh.GetVar("PATH") ?? "").Split(':'))
                {
                    try
                    {
                        var l = sh.M.V.Resolve(sh.P, d, true);
                        if (!l.Exists || l.Node.T != 'd') continue;
                        foreach (var k in l.Node.Kids.Keys) if (k.StartsWith(word, StringComparison.Ordinal)) set.Add(k);
                    }
                    catch (VfsEx) { }
                }
                foreach (var k in new[] { "if", "then", "else", "elif", "fi", "for", "while", "until", "do", "done", "case", "esac", "function" }) if (k.StartsWith(word, StringComparison.Ordinal) && word.Length > 0) set.Add(k);
                c.Words = set.ToList();
                if (word.Length == 0 && c.Words.Count > 200) c.Words = c.Words.Take(200).ToList();
                return c;
            }
            string cmd = words.Count > 0 ? words[0] : "";
            // sub-commands
            string key2 = words.Count >= 2 ? cmd + " " + words[1] : null;
            string[] subs;
            if (words.Count == 1 && Sub.TryGetValue(cmd, out subs) && !word.StartsWith("/", StringComparison.Ordinal) && !word.StartsWith(".", StringComparison.Ordinal) && (cmd != "systemctl" || true))
            {
                c.Words = subs.Where(s => s.StartsWith(word, StringComparison.Ordinal)).ToList();
                foreach (var w in c.Words.Where(x => x.EndsWith("=", StringComparison.Ordinal))) c.NoSpace.Add(w);
                if (c.Words.Count > 0) return c;
            }
            if (key2 != null && words.Count == 2 && Sub2.TryGetValue(key2, out subs))
            {
                c.Words = subs.Where(s => s.StartsWith(word, StringComparison.Ordinal)).ToList();
                if (c.Words.Count > 0) return c;
            }
            var m = sh.M;
            if (cmd == "systemctl" && words.Count >= 2)
            {
                var units = new SortedSet<string>(StringComparer.Ordinal);
                foreach (var d in Machine.SystemPath) { try { var l = m.V.G(d); if (l.Exists && l.IsDir) foreach (var k in l.Node.Kids.Keys) if ((k.EndsWith(".service", StringComparison.Ordinal) || k.EndsWith(".timer", StringComparison.Ordinal) || k.EndsWith(".target", StringComparison.Ordinal) || k.EndsWith(".socket", StringComparison.Ordinal)) && k.StartsWith(word, StringComparison.Ordinal)) units.Add(k); } catch (VfsEx) { } }
                foreach (var s in m.Services.Keys) if (!s.StartsWith("user:", StringComparison.Ordinal) && s.StartsWith(word, StringComparison.Ordinal)) units.Add(s);
                c.Words = units.ToList();
                return c;
            }
            if ((cmd == "dnf" || cmd == "yum") && words.Count >= 2 && (words[1] == "install" || words[1] == "remove" || words[1] == "erase" || words[1] == "info" || words[1] == "reinstall"))
            {
                c.Words = Packages.All.Select(p => p.Name).Where(n => n.StartsWith(word, StringComparison.Ordinal)).OrderBy(n => n, StringComparer.Ordinal).ToList();
                return c;
            }
            if (cmd == "firewall-cmd" && before.EndsWith("--add-service=", StringComparison.Ordinal) || before.EndsWith("--remove-service=", StringComparison.Ordinal) || before.EndsWith("--query-service=", StringComparison.Ordinal))
            {
                c.Words = Firewall.ServiceNames(m).Where(s => s.StartsWith(word, StringComparison.Ordinal)).ToList();
                return c;
            }
            if (cmd == "firewall-cmd" && word.StartsWith("--", StringComparison.Ordinal))
            {
                c.Words = Sub["firewall-cmd"].Where(s => s.StartsWith(word, StringComparison.Ordinal)).ToList();
                foreach (var w in c.Words.Where(x => x.EndsWith("=", StringComparison.Ordinal))) c.NoSpace.Add(w);
                return c;
            }
            if ((cmd == "timedatectl" && words.Count >= 2 && words[1] == "set-timezone"))
            {
                c.Words = Tz.Zones.Select(z => z[0]).Where(z => z.StartsWith(word, StringComparison.Ordinal)).ToList();
                return c;
            }
            if (cmd == "tuned-adm" && words.Count >= 2 && words[1] == "profile")
            {
                c.Words = Packages.TunedProfiles.Select(p => p[0]).Where(p => p.StartsWith(word, StringComparison.Ordinal)).OrderBy(x => x, StringComparer.Ordinal).ToList();
                return c;
            }
            if ((cmd == "nmcli") && words.Count >= 3 && (words[2] == "up" || words[2] == "down" || words[2] == "modify" || words[2] == "mod" || words[2] == "delete" || words[2] == "show"))
            {
                c.Words = m.NmConns().Select(x => x.Id).Where(x => x.StartsWith(word, StringComparison.Ordinal)).ToList();
                if (c.Words.Count > 0) return c;
            }
            if ((cmd == "su" || cmd == "passwd" || cmd == "usermod" || cmd == "userdel" || cmd == "chage" || cmd == "id" || cmd == "groups") && !word.StartsWith("-", StringComparison.Ordinal) && !word.StartsWith("/", StringComparison.Ordinal))
            {
                c.Words = m.Passwd().Select(p => p.Name).Where(n => n.StartsWith(word, StringComparison.Ordinal)).ToList();
                return c;
            }
            if ((cmd == "ssh" || cmd == "ping" || cmd == "scp" || cmd == "ssh-copy-id") && !word.StartsWith("-", StringComparison.Ordinal) && !word.Contains("/"))
            {
                string pre = "", w2 = word;
                int at = word.IndexOf('@'); if (at >= 0) { pre = word.Substring(0, at + 1); w2 = word.Substring(at + 1); }
                var hosts = new SortedSet<string>(StringComparer.Ordinal);
                foreach (var h in m.Net.Dns.Keys) { if (h.StartsWith(w2, StringComparison.Ordinal)) hosts.Add(h); var s = h.Split('.')[0]; if (s.StartsWith(w2, StringComparison.Ordinal)) hosts.Add(s); }
                c.Words = hosts.Select(h => pre + h).ToList();
                if (c.Words.Count > 0) return c;
            }
            // file names
            FileWords(sh, word, c, words.Count > 0 && (cmd == "cd" || cmd == "rmdir" || cmd == "pushd"));
            return c;
        }

        static void FileWords(Sh sh, string word, Completion c, bool dirsOnly)
        {
            string w = word;
            string expanded = w;
            string home = sh.GetVar("HOME") ?? "/";
            if (w.StartsWith("~/", StringComparison.Ordinal)) expanded = home + w.Substring(1);
            else if (w == "~") { c.Words.Add("~/"); c.NoSpace.Add("~/"); return; }
            int slash = expanded.LastIndexOf('/');
            string dir = slash < 0 ? "." : (slash == 0 ? "/" : expanded.Substring(0, slash));
            string part = slash < 0 ? expanded : expanded.Substring(slash + 1);
            string shownDir = slash < 0 ? "" : w.Substring(0, w.LastIndexOf('/') + 1);
            try
            {
                var l = sh.M.V.Resolve(sh.P, dir, true);
                if (!l.Exists || l.Node.T != 'd' || !sh.M.V.Can(sh.P.Cr, l.Node, 5)) return;
                foreach (var k in Vfs.Sorted(l.Node.Kids.Keys))
                {
                    if (!k.StartsWith(part, StringComparison.Ordinal)) continue;
                    if (k.StartsWith(".", StringComparison.Ordinal) && !part.StartsWith(".", StringComparison.Ordinal)) continue;
                    bool isDir = false;
                    try { var cl = sh.M.V.Resolve(sh.P, Vfs.Join(l.Path, k), true); isDir = cl.Exists && cl.Node.T == 'd'; } catch (VfsEx) { }
                    if (dirsOnly && !isDir) continue;
                    string full = shownDir + k + (isDir ? "/" : "");
                    c.Words.Add(full);
                    if (isDir) c.NoSpace.Add(full);
                }
            }
            catch (VfsEx) { }
        }
    }

    public partial class LabNet
    {
        /// <summary>After a line on one terminal: let the others show what changed for them (a reboot, a closed ssh).</summary>
        public void PumpOthers(Session from) { }
    }
}
