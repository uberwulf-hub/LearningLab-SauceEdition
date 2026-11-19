// Cert Lab - systemd: unit files (search path, drop-ins, daemon-reload), enable / disable as symlinks in *.wants
// folders, starting services (what each daemon really checks before it runs: its configuration, SELinux ports
// and files), targets, timers, user units with lingering, and the schedulers that run on simulated time:
// cron (crontabs, /etc/cron.d, run-parts) and at.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CertLab
{
    public class UnitInfo
    {
        public string Name, Path, Text = "";
        public bool Masked, Found, Generated;
        public Dictionary<string, List<string>> Keys = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        public List<string> DropIns = new List<string>();
        public string Get(string key) { List<string> v; return Keys.TryGetValue(key, out v) && v.Count > 0 ? v[v.Count - 1] : null; }
        public List<string> All(string key) { List<string> v; return Keys.TryGetValue(key, out v) ? v : new List<string>(); }
        public string Description { get { return Get("Unit.Description") ?? Name; } }
        public string Kind { get { int i = Name.LastIndexOf('.'); return i < 0 ? "" : Name.Substring(i + 1); } }
    }

    public partial class Machine
    {
        // ------------------------------------------------------------ unit files
        public static readonly string[] SystemPath = { "/etc/systemd/system", "/run/systemd/system", "/usr/lib/systemd/system" };

        public string[] UserPath(string home)
        {
            return new[] { home + "/.config/systemd/user", "/etc/systemd/user", "/run/systemd/user", "/usr/lib/systemd/user" };
        }

        public UnitInfo GetUnit(string name, string user)
        {
            if (!name.Contains(".")) name += ".service";
            var u = new UnitInfo { Name = name };
            string home = user == null ? null : (User(user) == null ? null : User(user).Home);
            var paths = user == null ? SystemPath : UserPath(home);
            foreach (var dir in paths)
            {
                string p = dir + "/" + name;
                Loc l;
                try { l = V.GNoFollow(p); } catch (VfsEx) { continue; }
                if (!l.Exists) continue;
                if (l.Node.T == 'l' && (l.Node.Target == "/dev/null")) { u.Masked = true; u.Path = p; u.Found = true; return u; }
                string t;
                try { var lf = V.G(p); if (!lf.Exists || lf.Node.T != 'f') continue; t = lf.Node.Text ?? ""; } catch (VfsEx) { continue; }
                u.Path = p; u.Text = t; u.Found = true;
                break;
            }
            // podman Quadlet: NAME.container becomes NAME.service
            if (!u.Found && name.EndsWith(".service", StringComparison.Ordinal))
            {
                string q = name.Substring(0, name.Length - 8) + ".container";
                var qdirs = user == null ? new[] { "/etc/containers/systemd", "/usr/share/containers/systemd" } : new[] { home + "/.config/containers/systemd", "/etc/containers/systemd/users" };
                foreach (var dir in qdirs)
                {
                    string t = V.Cat(dir + "/" + q);
                    if (t == null) continue;
                    u.Text = Quadlet(t, name.Substring(0, name.Length - 8), user != null);
                    u.Path = "/run/" + (user == null ? "systemd/generator/" : "user/" + User(user).Uid + "/systemd/generator/") + name;
                    u.Found = true; u.Generated = true;
                    break;
                }
            }
            if (!u.Found) return u;
            Parse(u.Text, u.Keys);
            // drop-ins: NAME.d/*.conf
            foreach (var dir in paths)
            {
                try
                {
                    var d = V.G(dir + "/" + name + ".d");
                    if (!d.Exists || !d.IsDir) continue;
                    foreach (var k in Vfs.Sorted(d.Node.Kids.Keys))
                    {
                        if (!k.EndsWith(".conf", StringComparison.Ordinal)) continue;
                        var t = V.Cat(dir + "/" + name + ".d/" + k);
                        if (t == null) continue;
                        u.DropIns.Add(dir + "/" + name + ".d/" + k);
                        Parse(t, u.Keys);
                    }
                }
                catch (VfsEx) { }
            }
            return u;
        }

        public static void Parse(string text, Dictionary<string, List<string>> keys)
        {
            string sec = "";
            foreach (var raw in text.Replace("\\\n", " ").Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal) || line.StartsWith(";", StringComparison.Ordinal)) continue;
                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal)) { sec = line.Substring(1, line.Length - 2); continue; }
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string k = sec + "." + line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
                List<string> l;
                if (!keys.TryGetValue(k, out l)) { l = new List<string>(); keys[k] = l; }
                if (v.Length == 0 && (k.EndsWith(".ExecStart", StringComparison.Ordinal) || k.EndsWith(".WantedBy", StringComparison.Ordinal))) { l.Clear(); continue; }
                l.Add(v);
            }
        }

        /// <summary>What podman's Quadlet generator makes of a .container file.</summary>
        public static string Quadlet(string text, string name, bool user)
        {
            var k = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            Parse(text, k);
            Func<string, List<string>> all = key => { List<string> l; return k.TryGetValue(key, out l) ? l : new List<string>(); };
            Func<string, string> one = key => { var l = all(key); return l.Count > 0 ? l[l.Count - 1] : null; };
            var cmd = new StringBuilder("/usr/bin/podman run --name " + (one("Container.ContainerName") ?? ("systemd-" + name)) + " --cidfile=%t/%N.cid --replace --rm --cgroups=split --sdnotify=conmon -d");
            foreach (var p in all("Container.PublishPort")) cmd.Append(" --publish " + p);
            foreach (var v in all("Container.Volume")) cmd.Append(" -v " + v);
            foreach (var e in all("Container.Environment")) cmd.Append(" --env " + e);
            string img = one("Container.Image") ?? "";
            cmd.Append(" " + img);
            if (one("Container.Exec") != null) cmd.Append(" " + one("Container.Exec"));
            var sb = new StringBuilder();
            sb.Append("# Automatically generated by /usr/lib/systemd/" + (user ? "user" : "system") + "-generators/podman-" + (user ? "user-" : "system-") + "generator\n#\n");
            sb.Append("[Unit]\nWants=network-online.target\nAfter=network-online.target\nDescription=" + (one("Unit.Description") ?? (name + " container")) + "\nSourcePath=" + name + ".container\nRequiresMountsFor=%t/containers\n\n");
            sb.Append("[X-Container]\nImage=" + img + "\n\n");
            sb.Append("[Service]\nRestart=" + (one("Service.Restart") ?? "no") + "\nEnvironment=PODMAN_SYSTEMD_UNIT=%n\nKillMode=mixed\nExecStop=/usr/bin/podman rm -v -f -i --cidfile=%t/%N.cid\nExecStopPost=-/usr/bin/podman rm -v -f -i --cidfile=%t/%N.cid\nDelegate=yes\nType=notify\nNotifyAccess=all\nSyslogIdentifier=%N\nExecStart=" + cmd + "\n\n");
            var wb = all("Install.WantedBy");
            if (wb.Count > 0) sb.Append("[Install]\nWantedBy=" + string.Join(" ", wb.ToArray()) + "\n");
            return sb.ToString();
        }

        /// <summary>systemctl daemon-reload: what systemd sees from now on.</summary>
        public void DaemonReload(string user)
        {
            foreach (var k in LoadedUnits.Keys.Where(x => (user == null) == !x.StartsWith("user:", StringComparison.Ordinal)).ToList()) LoadedUnits.Remove(k);
            Log("systemd", 1, 6, "Reloading.", null);
        }
        /// <summary>Has the unit file changed since systemd last read it?</summary>
        public bool UnitChanged(string unit, string user, UnitInfo u)
        {
            string key = (user == null ? "" : "user:" + user + ":") + unit;
            string seen;
            if (!LoadedUnits.TryGetValue(key, out seen)) { LoadedUnits[key] = u.Text + string.Join("|", u.DropIns.ToArray()); return false; }
            return seen != u.Text + string.Join("|", u.DropIns.ToArray());
        }
        public void MarkLoaded(string unit, string user, UnitInfo u) { LoadedUnits[(user == null ? "" : "user:" + user + ":") + unit] = u.Text + string.Join("|", u.DropIns.ToArray()); }

        // ------------------------------------------------------------ enable / disable
        public string EnabledState(string unit, string user)
        {
            var u = GetUnit(unit, user);
            if (!u.Found) return "not-found";
            if (u.Masked) return "masked";
            if (u.Generated) return "generated";
            if (unit.EndsWith(".mount", StringComparison.Ordinal) || unit.EndsWith(".device", StringComparison.Ordinal)) return "generated";
            var wanted = u.All("Install.WantedBy").SelectMany(x => x.Split(' ')).Concat(u.All("Install.RequiredBy").SelectMany(x => x.Split(' '))).Where(x => x.Length > 0).ToList();
            string baseDir = user == null ? "/etc/systemd/system" : User(user).Home + "/.config/systemd/user";
            foreach (var t in wanted)
                if (V.Exists(baseDir + "/" + t + ".wants/" + unit) || V.Exists(baseDir + "/" + t + ".requires/" + unit)) return "enabled";
            // links in other targets' wants (enabled by hand with ln -s)
            try
            {
                var d = V.G(baseDir);
                if (d.Exists && d.IsDir)
                    foreach (var k in d.Node.Kids.Keys)
                        if ((k.EndsWith(".wants", StringComparison.Ordinal) || k.EndsWith(".requires", StringComparison.Ordinal)) && V.Exists(baseDir + "/" + k + "/" + unit)) return wanted.Count > 0 ? "enabled" : "enabled";
            }
            catch (VfsEx) { }
            if (wanted.Count == 0 && u.All("Install.Alias").Count == 0 && u.All("Install.Also").Count == 0) return "static";
            if (u.Kind == "target" && wanted.Count == 0) return "static";
            return "disabled";
        }

        public List<string> Enable(string unit, string user, out string error)
        {
            error = null;
            var msgs = new List<string>();
            var u = GetUnit(unit, user);
            if (!u.Found) { error = "Failed to enable unit: Unit file " + unit + " does not exist."; return msgs; }
            if (u.Masked) { error = "Failed to enable unit: Unit file " + u.Path + " is masked."; return msgs; }
            if (u.Generated) { error = "Failed to enable unit: Unit " + u.Path + " is transient or generated."; return msgs; }
            var wanted = u.All("Install.WantedBy").SelectMany(x => x.Split(' ')).Where(x => x.Length > 0).ToList();
            var req = u.All("Install.RequiredBy").SelectMany(x => x.Split(' ')).Where(x => x.Length > 0).ToList();
            if (wanted.Count == 0 && req.Count == 0 && u.All("Install.Alias").Count == 0)
            {
                msgs.Add("The unit files have no installation config (WantedBy=, RequiredBy=, Also=,\nAlias= settings in the [Install] section, and DefaultInstance= for template\nunits). This means they are not meant to be enabled or disabled using systemctl.\n \nPossible reasons for having this kind of units are:\n\u2022 A unit may be statically enabled by being symlinked from another unit's\n  .wants/ or .requires/ directory.\n\u2022 A unit's purpose may be to act as a helper for some other unit which has\n  a requirement dependency on it.\n\u2022 A unit may be started when needed via activation (socket, path, timer,\n  D-Bus, udev, scripted systemctl call, ...).\n\u2022 In case of template units, the unit is meant to be enabled with some\n  instance name specified.");
                return msgs;
            }
            string baseDir = user == null ? "/etc/systemd/system" : User(user).Home + "/.config/systemd/user";
            foreach (var t in wanted.Select(x => x + ".wants").Concat(req.Select(x => x + ".requires")))
            {
                string link = baseDir + "/" + t + "/" + unit;
                if (V.Exists(link)) continue;
                if (user != null) { MkUserDirs(user, baseDir + "/" + t); }
                V.EnsureDir(baseDir + "/" + t, 0x1ED, null);
                V.PutLink(link, u.Path);
                if (user != null) { var pw = User(user); var ln = V.GNoFollow(link); ln.Node.Uid = pw.Uid; ln.Node.Gid = pw.Gid; }
                msgs.Add("Created symlink " + link + " \u2192 " + u.Path + ".");
            }
            foreach (var al in u.All("Install.Alias").SelectMany(x => x.Split(' ')).Where(x => x.Length > 0))
            {
                string link = baseDir + "/" + al;
                if (V.Exists(link)) continue;
                V.PutLink(link, u.Path);
                msgs.Add("Created symlink " + link + " \u2192 " + u.Path + ".");
            }
            foreach (var also in u.All("Install.Also").SelectMany(x => x.Split(' ')).Where(x => x.Length > 0))
            {
                string e2;
                if (GetUnit(also, user).Found) msgs.AddRange(Enable(also, user, out e2));
            }
            Log("systemd", 1, 6, "Reloading.", null);
            return msgs;
        }

        void MkUserDirs(string user, string dir)
        {
            var pw = User(user);
            string cur = "";
            foreach (var part in dir.Split('/').Where(x => x.Length > 0))
            {
                cur += "/" + part;
                if (!cur.StartsWith(pw.Home, StringComparison.Ordinal)) continue;
                if (!V.Exists(cur)) { var n = V.EnsureDir(cur, 0x1ED, null); n.Uid = pw.Uid; n.Gid = pw.Gid; }
            }
        }

        public List<string> Disable(string unit, string user)
        {
            var msgs = new List<string>();
            string baseDir = user == null ? "/etc/systemd/system" : User(user).Home + "/.config/systemd/user";
            try
            {
                var d = V.G(baseDir);
                if (d.Exists && d.IsDir)
                    foreach (var k in d.Node.Kids.Keys.ToList())
                    {
                        if (!(k.EndsWith(".wants", StringComparison.Ordinal) || k.EndsWith(".requires", StringComparison.Ordinal))) continue;
                        string link = baseDir + "/" + k + "/" + unit;
                        try { var l = V.GNoFollow(link); if (l.Exists) { V.RemoveEntryForce(l); msgs.Add("Removed \"" + link + "\"."); } } catch (VfsEx) { }
                    }
            }
            catch (VfsEx) { }
            var u = GetUnit(unit, user);
            foreach (var al in u.All("Install.Alias").SelectMany(x => x.Split(' ')).Where(x => x.Length > 0))
            {
                string link = baseDir + "/" + al;
                try { var l = V.GNoFollow(link); if (l.Exists && l.Node.T == 'l') { V.RemoveEntryForce(l); msgs.Add("Removed \"" + link + "\"."); } } catch (VfsEx) { }
            }
            return msgs;
        }

        // ------------------------------------------------------------ targets
        /// <summary>The units a target pulls in (its .wants folders, and those of the targets it needs).</summary>
        public List<string> WantedUnits(string target)
        {
            var res = new List<string>();
            var seen = new HashSet<string>();
            var todo = new Queue<string>();
            todo.Enqueue(target);
            foreach (var t in new[] { "sysinit.target", "basic.target", "sockets.target", "timers.target", "getty.target", "local-fs.target", "network.target", "remote-fs.target" }) todo.Enqueue(t);
            if (target == "graphical.target") todo.Enqueue("multi-user.target");
            if (target == "graphical.target" || target == "multi-user.target") todo.Enqueue("default.target");   // default.target is an alias of the default target: its .wants count
            while (todo.Count > 0)
            {
                var t = todo.Dequeue();
                if (!seen.Add(t)) continue;
                foreach (var dir in new[] { "/etc/systemd/system", "/usr/lib/systemd/system" })
                    foreach (var suffix in new[] { ".wants", ".requires" })
                    {
                        try
                        {
                            var d = V.G(dir + "/" + t + suffix);
                            if (!d.Exists || !d.IsDir) continue;
                            foreach (var k in Vfs.Sorted(d.Node.Kids.Keys))
                            {
                                if (k.EndsWith(".target", StringComparison.Ordinal)) todo.Enqueue(k);
                                else if (!res.Contains(k)) res.Add(k);
                            }
                        }
                        catch (VfsEx) { }
                    }
            }
            return res;
        }

        public bool Isolate(string target, out string error)
        {
            error = null;
            var u = GetUnit(target, null);
            if (!u.Found) { error = "Failed to start " + target + ": Unit " + target + " not found."; return false; }
            if (u.Get("Unit.AllowIsolate") != "yes") { error = "Failed to start " + target + ": Operation refused, unit may not be isolated."; return false; }
            var keep = new HashSet<string>(WantedUnits(target)) { "systemd-journald.service", "systemd-udevd.service" };
            if (target == "rescue.target" || target == "emergency.target")
            {
                foreach (var s in Services.Keys.ToList()) if (!keep.Contains(s) && s != "systemd-journald.service" && !s.StartsWith("user:", StringComparison.Ordinal)) StopService(s, null);
                State = target == "rescue.target" ? "rescue" : "emergency";
                Target = target;
                foreach (var s in Sessions.ToList()) if (s.IsConsole) s.Kick(this, "\n");
                return true;
            }
            foreach (var s in Services.Keys.ToList()) if (!keep.Contains(s) && !s.StartsWith("user:", StringComparison.Ordinal) && !s.StartsWith("container-", StringComparison.Ordinal)) StopService(s, null);
            foreach (var w in WantedUnits(target)) StartService(w, null);
            Target = target;
            return true;
        }

        // ------------------------------------------------------------ services
        string SKey(string unit, string user) { return user == null ? unit : "user:" + user + ":" + unit; }
        public ServiceState Svc(string unit, string user) { ServiceState s; Services.TryGetValue(SKey(unit, user), out s); return s; }
        public bool ServiceActive(string unit) { var s = Svc(unit, null); return s != null && s.Active == "active"; }
        public bool ServiceActive(string unit, string user) { var s = Svc(unit, user); return s != null && s.Active == "active"; }
        public string UnitDescription(string unit) { var u = GetUnit(unit.StartsWith("user:", StringComparison.Ordinal) ? unit.Split(':')[2] : unit, unit.StartsWith("user:", StringComparison.Ordinal) ? unit.Split(':')[1] : null); return u.Description; }

        /// <summary>Starts a unit. Returns null when it runs, else why not ("notfound", "masked", "failed:...").</summary>
        public string StartService(string unit, string user)
        {
            if (!unit.Contains(".")) unit += ".service";
            var u = GetUnit(unit, user);
            if (!u.Found) return "notfound";
            if (u.Masked) return "masked";
            MarkLoaded(unit, user, u);
            var cur = Svc(unit, user);
            if (cur != null && cur.Active == "active") return null;
            string key = SKey(unit, user);
            var st = new ServiceState { Unit = unit, Since = Now, Owner = user ?? "" };
            if (u.Kind == "target") { st.Active = "active"; st.Sub = "active"; Services[key] = st; return null; }
            if (u.Kind == "timer")
            {
                st.Active = "active"; st.Sub = "waiting"; Services[key] = st;
                TimerLast[key] = Now;
                Log("systemd", 1, 6, "Started " + u.Description + ".", unit);
                return null;
            }
            if (u.Kind == "socket")
            {
                st.Active = "active"; st.Sub = "listening"; Services[key] = st;
                foreach (var ls in u.All("Socket.ListenStream")) { int port; if (int.TryParse(ls.Split(':').Last(), out port)) st.Ports.Add(port); }
                Log("systemd", 1, 6, "Listening on " + u.Description + ".", unit);
                return null;
            }
            if (u.Kind == "mount") { return null; }
            if (u.Kind != "service") return null;
            Log("systemd", 1, 6, "Starting " + u.Description + "...", unit);
            string exec = u.Get("Service.ExecStart") ?? "";
            exec = exec.TrimStart('-', '@', '+', '!', ':');
            string bin = exec.Split(' ')[0];
            string name = Vfs.Base(bin);
            string type = u.Get("Service.Type") ?? (exec.Length > 0 ? "simple" : "oneshot");
            string runAs = u.Get("Service.User") ?? (user ?? "root");
            string err = null;
            var daemon = Daemons.Get(name);
            if (exec.Length == 0) { err = "203/EXEC"; Log("systemd", 1, 3, unit + ": Service has no ExecStart=, ExecStop=, or SuccessAction=. Refusing.", unit); }
            else if (daemon != null && PackageOwnsBinary(bin))
            {
                err = daemon(this, st, u);
                if (err == null) { st.Active = "active"; st.Sub = u.Get("Service.RemainAfterExit") == "yes" ? "exited" : "running"; }
            }
            else
            {
                // a program or script of its own
                err = RunExecStart(u, exec, runAs, st, type);
            }
            if (err != null)
            {
                st.Active = "failed"; st.Sub = "failed"; st.Result = "exit-code"; st.Pid = 0;
                Services[key] = st;
                string code = err.StartsWith("203", StringComparison.Ordinal) ? "203/EXEC" : "1/FAILURE";
                Log("systemd", 1, 5, unit + ": Main process exited, code=exited, status=" + code, unit);
                Log("systemd", 1, 4, unit + ": Failed with result 'exit-code'.", unit);
                Log("systemd", 1, 3, "Failed to start " + u.Description + ".", unit);
                return "failed";
            }
            Services[key] = st;
            if (st.Sub == "dead") { Log("systemd", 1, 6, unit + ": Deactivated successfully.", unit); Log("systemd", 1, 6, "Finished " + u.Description + ".", unit); }
            else Log("systemd", 1, 6, "Started " + u.Description + ".", unit);
            return null;
        }

        bool PackageOwnsBinary(string bin)
        {
            try
            {
                var l = V.G(bin.StartsWith("/", StringComparison.Ordinal) ? bin : "/usr/sbin/" + bin);
                return l.Exists && l.Node.Prog != null;
            }
            catch (VfsEx) { return false; }
        }

        string RunExecStart(UnitInfo u, string exec, string runAs, ServiceState st, string type)
        {
            string bin = exec.Split(' ')[0];
            var pw = User(runAs);
            if (pw == null) { Log("systemd", 1, 3, u.Name + ": Failed to determine user credentials: No such process", u.Name); return "217/USER"; }
            Loc l;
            try { l = V.G(bin); } catch (VfsEx) { l = null; }
            if (!bin.StartsWith("/", StringComparison.Ordinal) || l == null || !l.Exists)
            {
                Log("systemd", 1, 3, u.Name + ": Failed to locate executable " + bin + ": No such file or directory", u.Name);
                Log("systemd", 1, 3, u.Name + ": Failed at step EXEC spawning " + bin + ": No such file or directory", u.Name);
                return "203/EXEC";
            }
            var cr = CredFor(runAs);
            if (!V.Can(cr, l.Node, 1)) { Log("systemd", 1, 3, u.Name + ": Failed at step EXEC spawning " + bin + ": Permission denied", u.Name); return "203/EXEC"; }
            string cmd = exec.Replace("%t", pw.Uid == 0 ? "/run" : "/run/user/" + pw.Uid).Replace("%N", Vfs.Base(u.Name).Replace(".service", "")).Replace("%n", u.Name);
            var res = RunCommand(runAs, cmd, null, 30000, u.Name);
            st.Pid = NextPid; NextPid += 3;
            if (res.Status != 0)
            {
                foreach (var line in res.Output.Split('\n').Where(x => x.Length > 0).Take(5)) Log(Vfs.Base(bin), st.Pid, 3, line, u.Name);
                return "1/FAILURE";
            }
            foreach (var line in res.Output.Split('\n').Where(x => x.Length > 0).Take(5)) Log(Vfs.Base(bin), st.Pid, 6, line, u.Name);
            bool remain = u.Get("Service.RemainAfterExit") == "yes";
            bool podmanDetached = cmd.Contains("podman") && (cmd.Contains(" -d") || cmd.Contains("--detach"));
            if (type == "oneshot") { st.Active = remain ? "active" : "inactive"; st.Sub = remain ? "exited" : "dead"; return null; }
            if (podmanDetached || res.LongRunning || type == "forking" || type == "notify") { st.Active = "active"; st.Sub = "running"; Spawn(runAs, cmd, u.Name, "system_r:unconfined_service_t").Pid = st.Pid; return null; }
            // a simple service whose program finished at once
            st.Active = "inactive"; st.Sub = "dead";
            return null;
        }

        public class RunResult { public int Status; public string Output = ""; public bool LongRunning; }

        /// <summary>Runs a command line non-interactively as a user (cron, at, services, the graders' checks).</summary>
        public RunResult RunCommand(string user, string cmd, string stdin, int maxSteps, string unit)
        {
            var pw = User(user) ?? User("root");
            var p = new PCtx { Cr = CredFor(pw.Name), Cwd = pw.Home, Root = "/" };
            try { if (!V.G(pw.Home).Exists) p.Cwd = "/"; } catch (VfsEx) { p.Cwd = "/"; }
            var sh = new Sh(this, null, p);
            sh.SetVar("HOME", pw.Home); sh.SetVar("LOGNAME", pw.Name); sh.SetVar("USER", pw.Name); sh.SetVar("SHELL", "/bin/bash");
            sh.SetVar("PATH", "/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin");
            foreach (var k in new[] { "HOME", "LOGNAME", "USER", "SHELL", "PATH" }) sh.GetVarObj(k).Export = true;
            sh.MaxSteps = maxSteps;
            if (unit != null) sh.SetVar("CERTLAB_UNIT", unit);   // podman records which service started a container
            if (pw.Uid != 0 && unit != null && unit.EndsWith(".service", StringComparison.Ordinal)) { sh.SetVar("XDG_RUNTIME_DIR", "/run/user/" + pw.Uid); sh.GetVarObj("XDG_RUNTIME_DIR").Export = true; }
            sh.Background = true;
            var buf = new BufSink();
            var io = new Io { In = new StrSrc(stdin ?? ""), Out = buf, Err = buf };
            var r = new RunResult();
            try { r.Status = sh.RunText(cmd, io); }
            catch (ExitEx e) { r.Status = e.Code; }
            catch (TooLong) { r.LongRunning = true; }
            catch (BreakSignal) { r.Status = 130; }
            catch (MachineKick) { r.Status = 0; }
            catch (Exception e) { buf.Write(e.Message + "\n"); r.Status = 1; }
            if (sh.BgSeconds > 0 || sh.LongRunning) r.LongRunning = true;
            r.Output = buf.Text;
            return r;
        }

        public void StopService(string unit, string user)
        {
            if (!unit.Contains(".")) unit += ".service";
            string key = SKey(unit, user);
            ServiceState s;
            if (!Services.TryGetValue(key, out s)) return;
            if (s.Active == "active")
            {
                var u = GetUnit(unit, user);
                Log("systemd", 1, 6, "Stopping " + u.Description + "...", unit);
                var stopper = Daemons.GetStop(Vfs.Base((u.Get("Service.ExecStart") ?? "").TrimStart('-', '@', '+', '!').Split(' ')[0]));
                if (stopper != null) stopper(this, s);
                string stopCmd = u.Get("Service.ExecStop");
                if (stopCmd != null && Daemons.Get(Vfs.Base((u.Get("Service.ExecStart") ?? "").TrimStart('-').Split(' ')[0])) == null)
                {
                    var pw = User(u.Get("Service.User") ?? user ?? "root");
                    RunCommand(pw == null ? "root" : pw.Name, stopCmd.TrimStart('-').Replace("%t", pw == null || pw.Uid == 0 ? "/run" : "/run/user/" + pw.Uid).Replace("%N", unit.Replace(".service", "")), null, 20000, unit);
                }
                // a container started by the unit stops with it
                foreach (var c in Containers.Where(x => x.Unit == unit && x.Running).ToList()) { c.Running = false; c.Status = "Exited (0)"; if (c.AutoRemove) Containers.Remove(c); }
                Log("systemd", 1, 6, unit + ": Deactivated successfully.", unit);
                Log("systemd", 1, 6, "Stopped " + u.Description + ".", unit);
                Procs.RemoveAll(p => p.Unit == unit || (s.Pid > 0 && p.Pid == s.Pid));
            }
            Services.Remove(key);
        }

        // ------------------------------------------------------------ the clock ticks
        public void Tick()
        {
            if (State != "running") return;
            Procs.RemoveAll(p => p.EndsAt >= 0 && p.EndsAt <= Now && p.EndsAt != long.MaxValue);
            if (ServiceActive("crond.service")) RunCron();
            if (ServiceActive("atd.service")) RunAt();
            RunTimers();
            ChronyTick();
            LastTick = Now;
        }

        // ---- cron
        void RunCron()
        {
            var t = LocalTime(Now);
            if (t.Second != 0 && Now % 60 != 0) return;
            var jobs = new List<string[]>();   // user, command, source
            Action<string, bool> read = (file, hasUser) =>
            {
                string text = V.Cat(file);
                if (text == null) return;
                foreach (var raw in text.Split('\n'))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                    if (Regex.IsMatch(line, "^[A-Za-z_][A-Za-z0-9_]*\\s*=")) continue;
                    string user = null, cmd;
                    if (line.StartsWith("@", StringComparison.Ordinal))
                    {
                        var p = Regex.Split(line, "\\s+");
                        string when = p[0];
                        int ci = 1;
                        if (hasUser) { user = p.Length > 1 ? p[1] : "root"; ci = 2; }
                        cmd = string.Join(" ", p.Skip(ci).ToArray());
                        if (when == "@reboot") continue;
                        if (!CronAt(when, t)) continue;
                    }
                    else
                    {
                        var p = Regex.Split(line, "\\s+");
                        if (p.Length < (hasUser ? 7 : 6)) continue;
                        if (!CronMatch(p[0], p[1], p[2], p[3], p[4], t)) continue;
                        int ci = 5;
                        if (hasUser) { user = p[5]; ci = 6; }
                        cmd = string.Join(" ", p.Skip(ci).ToArray());
                        // keep the command's own spacing
                        var m = Regex.Match(line, "^(\\S+\\s+){" + ci + "}");
                        if (m.Success) cmd = line.Substring(m.Length);
                    }
                    jobs.Add(new[] { user, cmd, file });
                }
            };
            read("/etc/crontab", true);
            try { var d = V.G("/etc/cron.d"); if (d.Exists && d.IsDir) foreach (var k in Vfs.Sorted(d.Node.Kids.Keys)) if (!k.Contains(".") || k.EndsWith(".cron", StringComparison.Ordinal)) read("/etc/cron.d/" + k, true); } catch (VfsEx) { }
            try
            {
                var d = V.G("/var/spool/cron");
                if (d.Exists && d.IsDir)
                    foreach (var k in Vfs.Sorted(d.Node.Kids.Keys))
                    {
                        int before = jobs.Count;
                        read("/var/spool/cron/" + k, false);
                        for (int i = before; i < jobs.Count; i++) jobs[i][0] = k;
                    }
            }
            catch (VfsEx) { }
            foreach (var j in jobs)
            {
                if (User(j[0]) == null) continue;
                string cmd = j[1];
                string input = null;
                int pct = IndexOfUnescaped(cmd, '%');
                if (pct >= 0) { input = cmd.Substring(pct + 1).Replace("%", "\n"); cmd = cmd.Substring(0, pct); }
                cmd = cmd.Replace("\\%", "%");
                int pid = NextPid; NextPid += 2;
                Syslog("CROND", pid, 6, "(" + j[0] + ") CMD (" + cmd + ")", "cron");
                Journal.Add(new JournalEntry { Time = Now, Boot = BootId, Ident = "CROND", Pid = pid, Pri = 6, Msg = "(" + j[0] + ") CMD (" + cmd + ")", Unit = "crond.service" });
                var r = RunCommand(j[0], cmd, input, 20000, "crond.service");
                if (r.Output.Length > 0) Mail(j[0], "Cron <" + j[0] + "@" + ShortHost + "> " + cmd, r.Output);
            }
            // the daily / weekly folders through anacron, at 03:05
            if (t.Hour == 3 && t.Minute == 5) { RunParts("/etc/cron.daily"); if (t.DayOfWeek == DayOfWeek.Sunday) RunParts("/etc/cron.weekly"); if (t.Day == 1) RunParts("/etc/cron.monthly"); }
        }
        static int IndexOfUnescaped(string s, char c) { for (int i = 0; i < s.Length; i++) { if (s[i] == '\\') { i++; continue; } if (s[i] == c) return i; } return -1; }

        public void RunParts(string dir)
        {
            try
            {
                var d = V.G(dir);
                if (!d.Exists || !d.IsDir) return;
                foreach (var k in Vfs.Sorted(d.Node.Kids.Keys))
                {
                    var f = V.G(dir + "/" + k);
                    if (!f.Exists || f.Node.T != 'f' || (f.Node.Mode & 0x40) == 0) continue;
                    Syslog("run-parts", NextPid, 6, "(" + dir + ") starting " + k, "cron");
                    RunCommand("root", dir + "/" + k, null, 20000, "crond.service");
                    Syslog("run-parts", NextPid, 6, "(" + dir + ") finished " + k, "cron");
                }
            }
            catch (VfsEx) { }
        }

        static bool CronAt(string when, DateTime t)
        {
            switch (when)
            {
                case "@yearly": case "@annually": return t.Month == 1 && t.Day == 1 && t.Hour == 0 && t.Minute == 0;
                case "@monthly": return t.Day == 1 && t.Hour == 0 && t.Minute == 0;
                case "@weekly": return t.DayOfWeek == DayOfWeek.Sunday && t.Hour == 0 && t.Minute == 0;
                case "@daily": case "@midnight": return t.Hour == 0 && t.Minute == 0;
                case "@hourly": return t.Minute == 0;
            }
            return false;
        }

        static readonly string[] Months = { "jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec" };
        static readonly string[] Days = { "sun", "mon", "tue", "wed", "thu", "fri", "sat" };

        public static bool CronMatch(string min, string hour, string dom, string mon, string dow, DateTime t)
        {
            bool m = CronField(min, t.Minute, 0, 59, null), h = CronField(hour, t.Hour, 0, 23, null);
            bool mo = CronField(mon, t.Month, 1, 12, Months);
            bool dm = CronField(dom, t.Day, 1, 31, null);
            int wd = (int)t.DayOfWeek;
            bool dw = CronField(dow, wd, 0, 7, Days) || (wd == 0 && CronField(dow, 7, 0, 7, Days));
            bool day;
            if (dom != "*" && dow != "*") day = dm || dw;   // either one (cron's rule)
            else day = dm && dw;
            return m && h && mo && day;
        }

        public static bool CronField(string f, int v, int lo, int hi, string[] names)
        {
            foreach (var part in f.ToLowerInvariant().Split(','))
            {
                string p = part; int step = 1;
                int sl = p.IndexOf('/');
                if (sl >= 0) { if (!int.TryParse(p.Substring(sl + 1), out step) || step < 1) return false; p = p.Substring(0, sl); }
                int a, b;
                if (p == "*") { a = lo; b = hi; }
                else
                {
                    var r = p.Split('-');
                    if (!CronNum(r[0], names, lo, out a)) return false;
                    if (r.Length > 1) { if (!CronNum(r[1], names, lo, out b)) return false; }
                    else b = sl >= 0 ? hi : a;
                }
                for (int x = a; x <= b; x += step) if (x == v) return true;
            }
            return false;
        }
        static bool CronNum(string s, string[] names, int lo, out int n)
        {
            if (int.TryParse(s, out n)) return true;
            if (names != null) { int i = Array.IndexOf(names, s.Length >= 3 ? s.Substring(0, 3) : s); if (i >= 0) { n = i + (names == Months ? 1 : 0); return true; } }
            return false;
        }

        /// <summary>Checks a crontab line like crontab does when it is installed; null when fine.</summary>
        public static string CronLineError(string line, bool hasUser)
        {
            line = line.Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal) || Regex.IsMatch(line, "^[A-Za-z_][A-Za-z0-9_]*\\s*=")) return null;
            var p = Regex.Split(line, "\\s+");
            if (line.StartsWith("@", StringComparison.Ordinal)) { if (new[] { "@reboot", "@yearly", "@annually", "@monthly", "@weekly", "@daily", "@midnight", "@hourly" }.Contains(p[0])) return p.Length < 2 ? "bad command" : null; return "bad time specifier"; }
            if (p.Length < (hasUser ? 7 : 6)) return "bad command";
            int[] lo = { 0, 0, 1, 1, 0 }, hi = { 59, 23, 31, 12, 7 };
            string[] what = { "minute", "hour", "day-of-month", "month", "day-of-week" };
            for (int i = 0; i < 5; i++)
            {
                foreach (var part in p[i].ToLowerInvariant().Split(','))
                {
                    var q = part.Split('/')[0];
                    if (q == "*") continue;
                    foreach (var r in q.Split('-'))
                    {
                        int n;
                        if (int.TryParse(r, out n)) { if (n < lo[i] || n > hi[i]) return "bad " + what[i]; continue; }
                        if (i == 3 && Months.Contains(r)) continue;
                        if (i == 4 && Days.Contains(r)) continue;
                        return "bad " + what[i];
                    }
                }
            }
            return null;
        }

        public void Mail(string user, string subject, string body)
        {
            var d = LocalTime(Now);
            string date = d.ToString("ddd MMM", CultureInfo.InvariantCulture) + " " + d.Day.ToString().PadLeft(2) + " " + d.ToString("HH:mm:ss yyyy", CultureInfo.InvariantCulture);
            AppendLog("/var/spool/mail/" + user, "From root@" + HostnameNow + "  " + date + "\nFrom: \"(Cron Daemon)\" <root@" + HostnameNow + ">\nTo: " + user + "@" + HostnameNow + "\nSubject: " + subject + "\n\n" + body + (body.EndsWith("\n", StringComparison.Ordinal) ? "" : "\n") + "\n");
        }

        // ---- at
        public class AtJob { public int Id; public long When; public string User, Queue = "a"; public string Cmd; public string File; }

        public List<AtJob> AtJobs()
        {
            var l = new List<AtJob>();
            try
            {
                var d = V.G("/var/spool/at");
                if (!d.Exists || !d.IsDir) return l;
                foreach (var k in d.Node.Kids.Keys)
                {
                    var m = Regex.Match(k, "^([a-z=])([0-9a-f]{5})([0-9a-f]{8})$");
                    if (!m.Success) continue;
                    var f = V.G("/var/spool/at/" + k);
                    string text = f.Node.Text ?? "";
                    var um = Regex.Match(text, "(?m)^# atrun uid=(\\d+)");
                    int uid = um.Success ? int.Parse(um.Groups[1].Value) : f.Node.Uid;
                    string body = text;
                    int mark = text.IndexOf("\n# --- commands ---\n", StringComparison.Ordinal);
                    if (mark >= 0) body = text.Substring(mark + 20);
                    l.Add(new AtJob { Id = Convert.ToInt32(m.Groups[2].Value, 16), When = Convert.ToInt64(m.Groups[3].Value, 16) * 60, User = UName(uid), Queue = m.Groups[1].Value, Cmd = body, File = "/var/spool/at/" + k });
                }
            }
            catch (VfsEx) { }
            return l.OrderBy(j => j.When).ThenBy(j => j.Id).ToList();
        }

        public int AddAtJob(string user, long when, string commands, string queue)
        {
            string seqText = V.Cat("/var/spool/at/.SEQ") ?? "0";
            int seq; int.TryParse(seqText.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out seq);
            seq++;
            V.Put("/var/spool/at/.SEQ", seq.ToString("x5") + "\n", 0x180, 2, 2, null);
            string name = queue + seq.ToString("x5") + (when / 60).ToString("x8");
            var pw = User(user);
            var text = "#!/bin/sh\n# atrun uid=" + pw.Uid + " gid=" + pw.Gid + "\n# mail " + user + " 0\numask 22\nHOME=" + pw.Home + "; export HOME\nLOGNAME=" + user + "; export LOGNAME\ncd " + pw.Home + " || {\n\t echo 'Execution directory inaccessible' >&2\n\t exit 1\n}\n# --- commands ---\n" + commands;
            V.Put("/var/spool/at/" + name, text, 0x1C0, pw.Uid, 2, "system_u:object_r:user_cron_spool_t:s0");
            return seq;
        }

        void RunAt()
        {
            foreach (var j in AtJobs())
            {
                if (j.When > Now) continue;
                V.RemoveG(j.File);
                Log("atd", NextPid, 6, "Starting job " + j.Id + " (" + Vfs.Base(j.File) + ") for user '" + j.User + "' (" + (User(j.User) == null ? 0 : User(j.User).Uid) + ")", "atd.service");
                var r = RunCommand(j.User, j.Cmd, null, 20000, "atd.service");
                if (r.Output.Length > 0) Mail(j.User, "Output from your job " + j.Id, r.Output);
            }
        }

        // ---- timers
        void RunTimers()
        {
            foreach (var kv in Services.ToList())
            {
                if (!kv.Key.EndsWith(".timer", StringComparison.Ordinal) || kv.Value.Active != "active") continue;
                string unit = kv.Key.StartsWith("user:", StringComparison.Ordinal) ? kv.Key.Split(':')[2] : kv.Key;
                string user = kv.Key.StartsWith("user:", StringComparison.Ordinal) ? kv.Key.Split(':')[1] : null;
                var u = GetUnit(unit, user);
                if (!u.Found) continue;
                long last; if (!TimerLast.TryGetValue(kv.Key, out last)) last = kv.Value.Since;
                long next = NextTimerRun(u, last, kv.Value.Since);
                if (next < 0 || next > Now) continue;
                TimerLast[kv.Key] = Now;
                string svc = u.Get("Timer.Unit") ?? unit.Substring(0, unit.Length - 6) + ".service";
                var s = Svc(svc, user);
                if (s != null) Services.Remove(user == null ? svc : "user:" + user + ":" + svc);
                StartService(svc, user);
            }
        }

        /// <summary>When a timer fires next after last (-1: never).</summary>
        public long NextTimerRun(UnitInfo u, long last, long activated)
        {
            long best = -1;
            foreach (var cal in u.All("Timer.OnCalendar"))
            {
                long n = NextCalendar(cal, last);
                if (n >= 0 && (best < 0 || n < best)) best = n;
            }
            foreach (var k in new[] { "Timer.OnBootSec", "Timer.OnStartupSec" })
            {
                var v = u.Get(k);
                if (v != null) { long n = BootTime + ParseSpan(v); if (n > last && (best < 0 || n < best)) best = n; }
            }
            foreach (var k in new[] { "Timer.OnUnitActiveSec", "Timer.OnActiveSec", "Timer.OnUnitInactiveSec" })
            {
                var v = u.Get(k);
                if (v != null) { long n = last + ParseSpan(v); if (best < 0 || n < best) best = n; }
            }
            return best;
        }

        public static long ParseSpan(string s)
        {
            long total = 0;
            foreach (Match m in Regex.Matches(s.Trim(), "(\\d+)\\s*(us|ms|seconds?|secs?|s|minutes?|mins?|m|hours?|hr|h|days?|d|weeks?|w|months?|M|years?|y)?"))
            {
                if (m.Groups[1].Value.Length == 0) continue;
                long n = long.Parse(m.Groups[1].Value);
                string unit = m.Groups[2].Value;
                if (unit == "" || unit.StartsWith("s", StringComparison.Ordinal)) total += n;
                else if (unit.StartsWith("min", StringComparison.Ordinal) || unit == "m") total += n * 60;
                else if (unit.StartsWith("h", StringComparison.Ordinal)) total += n * 3600;
                else if (unit.StartsWith("d", StringComparison.Ordinal)) total += n * 86400;
                else if (unit.StartsWith("w", StringComparison.Ordinal)) total += n * 604800;
                else if (unit == "M" || unit.StartsWith("mon", StringComparison.Ordinal)) total += n * 2629800;
                else if (unit.StartsWith("y", StringComparison.Ordinal)) total += n * 31557600;
            }
            return total;
        }

        /// <summary>systemd calendar events: minutely, hourly, daily, weekly, monthly, "Mon *-*-* 02:00:00", "*-*-* *:0/15"...</summary>
        public long NextCalendar(string spec, long after)
        {
            var parsed = CalendarSpec(spec);
            if (parsed == null) return -1;
            // walk minute by minute in local time (up to about two months)
            long start = (after / 60 + 1) * 60;
            for (long t = start; t < after + 62L * 86400; t += 60)
            {
                var d = LocalTime(t);
                if (CalMatch(parsed, d)) return t;
            }
            return -1;
        }

        public static string[] CalendarSpec(string spec)
        {
            string s = spec.Trim();
            switch (s.ToLowerInvariant())
            {
                case "minutely": return new[] { "*", "*", "*", "*", "*", "*", "00" };
                case "hourly": return new[] { "*", "*", "*", "*", "*", "00", "00" };
                case "daily": return new[] { "*", "*", "*", "*", "00", "00", "00" };
                case "weekly": return new[] { "mon", "*", "*", "*", "00", "00", "00" };
                case "monthly": return new[] { "*", "*", "*", "01", "00", "00", "00" };
                case "yearly": case "annually": return new[] { "*", "*", "01", "01", "00", "00", "00" };
                case "quarterly": return new[] { "*", "*", "01,04,07,10", "01", "00", "00", "00" };
            }
            var parts = s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            string dow = "*", date = "*-*-*", time = "00:00:00";
            if (parts.Count > 0 && Regex.IsMatch(parts[0], "^[A-Za-z]")) { dow = parts[0].ToLowerInvariant(); parts.RemoveAt(0); }
            foreach (var p in parts) { if (p.Contains("-")) date = p; else if (p.Contains(":")) time = p; }
            var dp = date.Split('-');
            if (dp.Length == 2) dp = new[] { "*", dp[0], dp[1] };
            if (dp.Length != 3) return null;
            var tp = time.Split(':');
            if (tp.Length == 2) tp = new[] { tp[0], tp[1], "00" };
            if (tp.Length != 3) return null;
            return new[] { dow, dp[0], dp[1], dp[2], tp[0], tp[1], tp[2] };
        }
        static bool CalMatch(string[] c, DateTime d)
        {
            if (c[0] != "*")
            {
                bool ok = false;
                string[] names = { "sun", "mon", "tue", "wed", "thu", "fri", "sat" };
                foreach (var part in c[0].Split(','))
                {
                    var r = part.Split(new[] { ".." }, StringSplitOptions.None);
                    int a = Array.IndexOf(names, r[0].Length >= 3 ? r[0].Substring(0, 3) : r[0]);
                    int b = r.Length > 1 ? Array.IndexOf(names, r[1].Length >= 3 ? r[1].Substring(0, 3) : r[1]) : a;
                    int w = (int)d.DayOfWeek;
                    if (a >= 0 && b >= 0 && (a <= b ? w >= a && w <= b : w >= a || w <= b)) ok = true;
                }
                if (!ok) return false;
            }
            return CalField(c[1], d.Year) && CalField(c[2], d.Month) && CalField(c[3], d.Day) && CalField(c[4], d.Hour) && CalField(c[5], d.Minute) && CalField(c[6].Split('.')[0], 0);
        }
        static bool CalField(string f, int v)
        {
            if (f == "*") return true;
            foreach (var part in f.Split(','))
            {
                string p = part; int step = 0;
                int sl = p.IndexOf('/');
                if (sl >= 0) { int.TryParse(p.Substring(sl + 1), out step); p = p.Substring(0, sl); }
                var r = p.Split(new[] { ".." }, StringSplitOptions.None);
                int a, b;
                if (p == "*") a = 0; else if (!int.TryParse(r[0], out a)) return false;
                if (r.Length > 1) { if (!int.TryParse(r[1], out b)) return false; } else b = step > 0 ? 9999 : a;
                if (step > 0) { for (int x = a; x <= b && x <= 9999; x += step) if (x == v) return true; }
                else if (v >= a && v <= b) return true;
            }
            return false;
        }

        // ------------------------------------------------------------ user managers (systemctl --user)
        public bool Lingering(string user) { return V.Exists("/var/lib/systemd/linger/" + user); }

        public void StartUserManager(string user)
        {
            string key = "user@" + (User(user) == null ? 0 : User(user).Uid) + ".service";
            if (ServiceActive(key)) return;
            Services[key] = new ServiceState { Unit = key, Active = "active", Sub = "running", Since = Now, Pid = NextPid };
            NextPid += 4;
            var pw = User(user);
            try { var d = V.EnsureDir("/run/user/" + pw.Uid, 0x1C0, null); d.Uid = pw.Uid; d.Gid = pw.Gid; } catch (VfsEx) { }
            // the user's enabled units (default.target.wants)
            string wants = pw.Home + "/.config/systemd/user/default.target.wants";
            try
            {
                var d = V.G(wants);
                if (d.Exists && d.IsDir) foreach (var k in Vfs.Sorted(d.Node.Kids.Keys)) StartService(k, user);
            }
            catch (VfsEx) { }
            // Quadlet units with WantedBy=default.target start too
            try
            {
                var d = V.G(pw.Home + "/.config/containers/systemd");
                if (d.Exists && d.IsDir)
                    foreach (var k in Vfs.Sorted(d.Node.Kids.Keys))
                    {
                        if (!k.EndsWith(".container", StringComparison.Ordinal)) continue;
                        var u = GetUnit(k.Replace(".container", ".service"), user);
                        if (u.All("Install.WantedBy").Any(x => x.Contains("default.target") || x.Contains("multi-user.target"))) StartService(u.Name, user);
                    }
            }
            catch (VfsEx) { }
        }

        void StartLingerUsers()
        {
            try
            {
                var d = V.G("/var/lib/systemd/linger");
                if (d.Exists && d.IsDir) foreach (var k in d.Node.Kids.Keys.ToList()) if (User(k) != null) StartUserManager(k);
            }
            catch (VfsEx) { }
            // system Quadlet units
            try
            {
                var d = V.G("/etc/containers/systemd");
                if (d.Exists && d.IsDir)
                    foreach (var k in Vfs.Sorted(d.Node.Kids.Keys))
                    {
                        if (!k.EndsWith(".container", StringComparison.Ordinal)) continue;
                        var u = GetUnit(k.Replace(".container", ".service"), null);
                        if (u.All("Install.WantedBy").Any(x => x.Contains("multi-user.target") || x.Contains("default.target"))) StartService(u.Name, null);
                    }
            }
            catch (VfsEx) { }
        }
    }
}
