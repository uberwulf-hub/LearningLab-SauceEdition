// Cert Lab - systemctl, journalctl and loginctl.
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
        const string Bullet = "\u25cf";
        const string Tree1 = "\u251c\u2500", Tree2 = "\u2514\u2500";

        static bool UserBusMissing(Sh sh, Io io, bool user)
        {
            if (!user) return false;

            // su - does not give the person a systemd user session; ssh (or a console login) does
            if (sh.GetVar("XDG_RUNTIME_DIR") == null)
            {
                io.E("Failed to connect to bus: $DBUS_SESSION_BUS_ADDRESS and $XDG_RUNTIME_DIR not defined (consider using --machine=<user>@.host --user to connect to bus of other user)");
                return true;
            }
            return false;
        }

        static string Ago(long secs)
        {
            if (secs < 60) return secs + "s ago";
            if (secs < 3600) return secs / 60 + "min " + secs % 60 + "s ago";
            if (secs < 86400) return secs / 3600 + "h " + secs % 3600 / 60 + "min ago";
            return secs / 86400 + " days ago";
        }
        static string StatusDate(Machine m, long t) { var d = m.LocalTime(t); return d.ToString("ddd yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " " + Tz.Abbrev(m.TimeZoneName, t); }

        static int Systemctl(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "tap:qlf", "type=|t,all|a,user,system,now,property=|p,value,quiet|q,no-pager,full|l,state=,force|f,lines=|n,failed,no-legend,plain,runtime,show-types,global", "systemctl");
            if (o.Error != null) return Fail(io, o);
            var m = sh.M;
            bool user = o.Has("user");
            string uname = user ? sh.P.Cr.User : null;
            if (o.Args.Count == 0) return ListUnits(sh, io, o, uname);
            string verb = o.Args[0];
            var units = o.Args.Skip(1).Select(u => u.Contains(".") ? u : u + (verb == "isolate" || verb == "set-default" ? ".target" : ".service")).ToList();
            bool root = sh.P.Cr.Uid == 0 || user;
            Func<string, bool> needRoot = what =>
            {
                if (root) return false;
                io.E("Failed to " + what + ": Interactive authentication required.\nSee system logs and 'systemctl status " + (units.Count > 0 ? units[0] : "") + "' for details.");
                return true;
            };
            if (UserBusMissing(sh, io, user)) return 1;
            switch (verb)
            {
                case "list-units": return ListUnits(sh, io, o, uname);
                case "list-unit-files": return ListUnitFiles(sh, io, o, uname);
                case "list-timers": return ListTimers(sh, io, o, uname);
                case "list-dependencies": io.L((units.Count > 0 ? units[0] : "default.target")); foreach (var w in m.WantedUnits(units.Count > 0 ? units[0] : m.DefaultTarget()).Take(30)) io.L(Bullet + " " + Tree1 + w); return 0;
                case "status": return SysStatus(sh, io, units, uname, o);
                case "is-active":
                    {
                        int st = 3;
                        foreach (var u in units) { var s = m.Svc(u, uname); string a2 = s == null ? (uname == null && TargetReached(m, u) ? "active" : "inactive") : s.Active; if (!o.Has("q")) io.L(a2); if (a2 == "active") st = 0; }
                        return st;
                    }
                case "is-system-running":
                    {
                        string state = m.State == "emergency" || m.State == "rescue" ? "maintenance" : m.Services.Values.Any(x => x.Active == "failed" && x.Owner.Length == 0) ? "degraded" : "running";
                        if (!o.Has("q")) io.L(state);
                        return state == "running" ? 0 : 1;
                    }
                case "is-failed": { int st = 1; foreach (var u in units) { var s = m.Svc(u, uname); bool f = s != null && s.Active == "failed"; if (!o.Has("q")) io.L(f ? "failed" : (s == null ? "inactive" : s.Active)); if (f) st = 0; } return st; }
                case "is-enabled":
                    {
                        int st = 1;
                        foreach (var u in units)
                        {
                            string e = m.EnabledState(u, uname);
                            if (e == "not-found") { io.E("Failed to get unit file state for " + u + ": No such file or directory"); st = 1; continue; }
                            if (!o.Has("q")) io.L(e);
                            if (e == "enabled" || e == "static" || e == "alias" || e == "indirect" || e == "generated" || e == "enabled-runtime") st = 0;
                        }
                        return st;
                    }
                case "start": case "restart": case "try-restart": case "reload": case "reload-or-restart": case "condrestart":
                    {
                        if (units.Count == 0) { io.E("Too few arguments."); return 1; }
                        if (needRoot((verb.StartsWith("reload", StringComparison.Ordinal) ? "reload" : verb == "start" ? "start" : "restart") + " " + units[0])) return 1;
                        int st = 0;
                        foreach (var u in units)
                        {
                            var ui = m.GetUnit(u, uname);
                            if (ui.Found && m.UnitChanged(u, uname, ui)) io.E("Warning: The unit file, source configuration file or drop-ins of " + u + " changed on disk. Run 'systemctl " + (user ? "--user " : "") + "daemon-reload' to reload units.");
                            if (verb == "try-restart" || verb == "condrestart") { if (!m.ServiceActive(u, uname)) continue; }
                            if (verb == "reload")
                            {
                                if (!m.ServiceActive(u, uname)) { io.E("Job for " + u + " invalid."); st = 1; continue; }
                                if (u == "firewalld.service") m.FwStart();
                                if (u == "autofs.service") m.StartAutofs();
                                if (u == "sshd.service" || u == "httpd.service") { var s0 = m.Svc(u, uname); string err0; if (u == "sshd.service" && (m.SshdConfig(out err0) != null && err0 != null)) { } }
                                m.Log("systemd", 1, 6, "Reloading " + ui.Description + "...", u);
                                m.Log("systemd", 1, 6, "Reloaded " + ui.Description + ".", u);
                                continue;
                            }
                            if (verb != "start") m.StopService(u, uname);
                            string r = m.StartService(u, uname);
                            if (r == "notfound") { io.E("Failed to " + (verb == "start" ? "start" : "restart") + " " + u + ": Unit " + u + " not found."); st = 5; }
                            else if (r == "masked") { io.E("Failed to " + (verb == "start" ? "start" : "restart") + " " + u + ": Unit " + u + " is masked."); st = 1; }
                            else if (r == "failed") { io.E("Job for " + u + " failed because the control process exited with error code.\nSee \"systemctl " + (user ? "--user " : "") + "status " + u + "\" and \"journalctl " + (user ? "--user " : "") + "-xeu " + u + "\" for details."); st = 1; }
                        }
                        return st;
                    }
                case "stop":
                    {
                        if (units.Count == 0) { io.E("Too few arguments."); return 1; }
                        if (needRoot("stop " + units[0])) return 1;
                        foreach (var u in units)
                        {
                            if (!m.GetUnit(u, uname).Found && m.Svc(u, uname) == null) { io.E("Failed to stop " + u + ": Unit " + u + " not loaded."); return 5; }
                            if (u == "auditd.service") { io.E("Failed to stop auditd.service: Operation refused, unit auditd.service may be requested by dependency only (it is configured to refuse manual start/stop).\nSee system logs and 'systemctl status auditd.service' for details."); return 1; }
                            m.StopService(u, uname);
                        }
                        return 0;
                    }
                case "enable": case "disable": case "reenable":
                    {
                        if (units.Count == 0) { io.E("Too few arguments."); return 1; }
                        if (needRoot(verb + " unit")) return 1;
                        int st = 0;
                        foreach (var u in units)
                        {
                            if (verb == "disable" || verb == "reenable")
                            {
                                if (!m.GetUnit(u, uname).Found) { io.E("Failed to disable unit: Unit file " + u + " does not exist."); st = 1; continue; }
                                foreach (var msg in m.Disable(u, uname)) io.E(msg);
                                if (o.Has("now")) m.StopService(u, uname);
                            }
                            if (verb == "enable" || verb == "reenable")
                            {
                                string err;
                                var msgs = m.Enable(u, uname, out err);
                                if (err != null) { io.E(err); st = 1; continue; }
                                foreach (var msg in msgs) io.E(msg);
                                if (o.Has("now"))
                                {
                                    string r = m.StartService(u, uname);
                                    if (r == "failed") { io.E("Job for " + u + " failed because the control process exited with error code.\nSee \"systemctl status " + u + "\" and \"journalctl -xeu " + u + "\" for details."); st = 1; }
                                }
                            }
                        }
                        return st;
                    }
                case "mask": case "unmask":
                    {
                        if (needRoot(verb + " unit")) return 1;
                        string dir = user ? m.User(uname).Home + "/.config/systemd/user" : "/etc/systemd/system";
                        foreach (var u in units)
                        {
                            string link = dir + "/" + u;
                            if (verb == "mask")
                            {
                                try { var l = m.V.GNoFollow(link); if (l.Exists && l.Node.T == 'f') { io.E("Failed to mask unit: File " + link + " already exists."); return 1; } } catch (VfsEx) { }
                                m.V.PutLink(link, "/dev/null");
                                io.E("Created symlink " + link + " \u2192 /dev/null.");
                            }
                            else
                            {
                                try { var l = m.V.GNoFollow(link); if (l.Exists && l.Node.T == 'l' && l.Node.Target == "/dev/null") { m.V.RemoveEntryForce(l); io.E("Removed \"" + link + "\"."); } } catch (VfsEx) { }
                            }
                        }
                        return 0;
                    }
                case "daemon-reload": case "daemon-reexec": if (needRoot("reload daemon")) return 1; m.DaemonReload(uname); return 0;
                case "get-default": io.L(m.DefaultTarget()); return 0;
                case "set-default":
                    {
                        if (needRoot("set default target")) return 1;
                        if (units.Count == 0) { io.E("Too few arguments."); return 1; }
                        var u = m.GetUnit(units[0], null);
                        if (!u.Found) { io.E("Failed to set default target: Unit file " + units[0] + " does not exist."); return 1; }
                        var cur = m.V.GNoFollow("/etc/systemd/system/default.target");
                        if (cur.Exists) { m.V.RemoveEntryForce(cur); io.E("Removed \"/etc/systemd/system/default.target\"."); }
                        m.V.PutLink("/etc/systemd/system/default.target", u.Path);
                        io.E("Created symlink /etc/systemd/system/default.target \u2192 " + u.Path + ".");
                        return 0;
                    }
                case "isolate":
                    {
                        if (needRoot("start " + (units.Count > 0 ? units[0] : ""))) return 1;
                        string err;
                        if (!m.Isolate(units[0], out err)) { io.E(err); return 1; }
                        return 0;
                    }
                case "rescue": case "emergency":
                    {
                        if (needRoot("start " + verb + ".target")) return 1;
                        string err; m.Isolate(verb + ".target", out err);
                        return 0;
                    }
                case "default":
                    {
                        if (m.State == "emergency" || m.State == "rescue") { m.ContinueBoot(); return 0; }
                        string err; m.Isolate(m.DefaultTarget(), out err); return 0;
                    }
                case "reboot": case "poweroff": case "halt": case "kexec":
                    if (needRoot(verb)) return 1;
                    Power(sh, io, verb == "reboot" || verb == "kexec");
                    return 0;
                case "cat":
                    {
                        int st = 0;
                        foreach (var u in units)
                        {
                            var ui = m.GetUnit(u, uname);
                            if (!ui.Found) { io.E("No files found for " + u + "."); st = 1; continue; }
                            io.L("# " + ui.Path); io.P(ui.Text.EndsWith("\n", StringComparison.Ordinal) ? ui.Text : ui.Text + "\n");
                            foreach (var d in ui.DropIns) { io.L("\n# " + d); io.P(m.V.Cat(d) ?? ""); }
                        }
                        return st;
                    }
                case "edit":
                    {
                        if (needRoot("edit")) return 1;
                        if (units.Count == 0) { io.E("Too few arguments."); return 1; }
                        var ui = m.GetUnit(units[0], uname);
                        if (!ui.Found) { io.E("Failed to edit unit: Unit " + units[0] + " not found."); return 1; }
                        string dir = (user ? m.User(uname).Home + "/.config/systemd/user/" : "/etc/systemd/system/") + units[0] + ".d";
                        string file = o.Has("full") ? (user ? m.User(uname).Home + "/.config/systemd/user/" : "/etc/systemd/system/") + units[0] : dir + "/override.conf";
                        string existing = m.V.Cat(file) ?? (o.Has("full") ? ui.Text : "");
                        string template = o.Has("full") ? existing : "### Editing " + file + "\n### Anything between here and the comment below will become the contents of the drop-in file\n\n" + existing + "\n### Edits below this comment will be discarded\n\n\n### " + ui.Path + "\n" + string.Join("\n", ui.Text.Split('\n').Select(x => "# " + x).ToArray());
                        string res = sh.S == null ? null : sh.S.Edit(file, template, false, "vi");
                        if (res == null) { io.E("Editing \"" + file + "\" canceled: temporary file is empty."); return 1; }
                        if (!o.Has("full"))
                        {
                            int cut = res.IndexOf("### Edits below this comment will be discarded", StringComparison.Ordinal);
                            string body = cut >= 0 ? res.Substring(0, cut) : res;
                            body = string.Join("\n", body.Split('\n').Where(x => !x.StartsWith("### ", StringComparison.Ordinal)).ToArray()).Trim() + "\n";
                            if (body.Trim().Length == 0) { io.E("Editing \"" + file + "\" canceled: temporary file is empty."); return 1; }
                            m.V.EnsureDir(dir, 0x1ED, null);
                            m.SysWrite(file, body);
                        }
                        else m.SysWrite(file, res);
                        m.DaemonReload(uname);
                        return 0;
                    }
                case "revert":
                    {
                        foreach (var u in units) { m.V.RemoveG("/etc/systemd/system/" + u + ".d"); io.E("Removed \"/etc/systemd/system/" + u + ".d/override.conf\"."); }
                        m.DaemonReload(null);
                        return 0;
                    }
                case "show":
                    {
                        string unit = units.Count > 0 ? units[0] : null;
                        var props = new List<KeyValuePair<string, string>>();
                        if (unit == null) { props.Add(new KeyValuePair<string, string>("Version", "252-46.el9_5.2")); props.Add(new KeyValuePair<string, string>("DefaultTarget", m.DefaultTarget())); }
                        else
                        {
                            var ui = m.GetUnit(unit, uname); var s = m.Svc(unit, uname);
                            props.Add(new KeyValuePair<string, string>("Id", unit));
                            props.Add(new KeyValuePair<string, string>("Description", ui.Description));
                            props.Add(new KeyValuePair<string, string>("LoadState", ui.Masked ? "masked" : ui.Found ? "loaded" : "not-found"));
                            props.Add(new KeyValuePair<string, string>("ActiveState", s == null ? "inactive" : s.Active));
                            props.Add(new KeyValuePair<string, string>("SubState", s == null ? "dead" : s.Sub));
                            props.Add(new KeyValuePair<string, string>("UnitFileState", m.EnabledState(unit, uname)));
                            props.Add(new KeyValuePair<string, string>("MainPID", s == null ? "0" : s.Pid.ToString()));
                            props.Add(new KeyValuePair<string, string>("FragmentPath", ui.Path ?? ""));
                            props.Add(new KeyValuePair<string, string>("ExecStart", ui.Get("Service.ExecStart") ?? ""));
                            props.Add(new KeyValuePair<string, string>("User", ui.Get("Service.User") ?? ""));
                            props.Add(new KeyValuePair<string, string>("Type", ui.Get("Service.Type") ?? "simple"));
                            props.Add(new KeyValuePair<string, string>("WantedBy", string.Join(" ", ui.All("Install.WantedBy").ToArray())));
                            props.Add(new KeyValuePair<string, string>("Restart", ui.Get("Service.Restart") ?? "no"));
                        }
                        var want = o.All("p").SelectMany(x => x.Split(',')).ToList();
                        foreach (var kv in props) if (want.Count == 0 || want.Contains(kv.Key)) io.L(o.Has("value") ? kv.Value : kv.Key + "=" + kv.Value);
                        return 0;
                    }
                case "kill":
                    {
                        foreach (var u in units) { var s = m.Svc(u, uname); if (s != null && s.Active == "active") { m.StopService(u, uname); var st = new ServiceState { Unit = u, Active = "failed", Sub = "failed", Result = "signal", Since = m.Now }; m.Services[u] = st; } }
                        return 0;
                    }
                case "reset-failed": foreach (var k in m.Services.Where(x => x.Value.Active == "failed").Select(x => x.Key).ToList()) m.Services.Remove(k); return 0;
                case "list-jobs": io.L("No jobs running."); return 0;
                case "list-sockets": io.L("LISTEN         UNIT              ACTIVATES\n/run/dbus/system_bus_socket dbus.socket dbus-broker.service" + (m.ServiceActive("cockpit.socket") ? "\n[::]:9090      cockpit.socket    cockpit.service" : "") + "\n"); return 0;
                case "preset": return 0;
            }
            io.E("Unknown command verb " + verb + ".");
            return 1;
        }

        static int ListUnits(Sh sh, Io io, Opts o, string user)
        {
            var m = sh.M;
            string type = o.Get("t");
            var rows = new List<string[]>();
            var names = new HashSet<string>();
            foreach (var kv in m.Services)
            {
                bool isUser = kv.Key.StartsWith("user:", StringComparison.Ordinal);
                if ((user != null) != isUser) continue;
                if (isUser && kv.Key.Split(':')[1] != user) continue;
                var s = kv.Value;
                if (type != null && !s.Unit.EndsWith("." + type, StringComparison.Ordinal)) continue;
                if (!o.Has("a") && s.Active == "inactive") continue;
                if (o.Has("state") && s.Active != o.Get("state") && s.Sub != o.Get("state")) continue;
                if (o.Has("failed") && s.Active != "failed") continue;
                names.Add(s.Unit);
                rows.Add(new[] { (s.Active == "failed" ? Bullet + " " : "  ") + s.Unit, "loaded", s.Active, s.Sub, m.UnitDescription(kv.Key) });
            }
            if (user == null && (type == null || type == "target") && !o.Has("failed"))
                foreach (var t in new[] { "basic.target", "local-fs.target", "multi-user.target", "network.target", "network-online.target", "remote-fs.target", "sockets.target", "swap.target", "sysinit.target", "timers.target", "getty.target" })
                    if (m.State == "running") rows.Add(new[] { "  " + t, "loaded", "active", "active", Machine.TargetDescription(t) });
            if (user == null && (type == null || type == "mount") && !o.Has("failed"))
                foreach (var mt in m.Mounts.Where(x => x.Path != "/" || true).Where(x => x.FsType != "autofs" || true))
                    rows.Add(new[] { "  " + Machine.UnitNameForPath(mt.Path) + ".mount", "loaded", "active", "mounted", mt.Path });
            rows = rows.OrderBy(r => r[0].Trim().TrimStart('\u25cf').Trim(), StringComparer.Ordinal).ToList();
            var w = new int[4];
            var head = new[] { "  UNIT", "LOAD", "ACTIVE", "SUB" };
            for (int i = 0; i < 4; i++) w[i] = Math.Max(head[i].Length, rows.Count == 0 ? 0 : rows.Max(r => r[i].Length));
            if (!o.Has("no-legend")) io.L(head[0].PadRight(w[0]) + " " + head[1].PadRight(w[1]) + " " + head[2].PadRight(w[2]) + " " + head[3].PadRight(w[3]) + " DESCRIPTION");
            foreach (var r in rows) io.L(r[0].PadRight(w[0]) + " " + r[1].PadRight(w[1]) + " " + r[2].PadRight(w[2]) + " " + r[3].PadRight(w[3]) + " " + r[4]);
            if (!o.Has("no-legend")) io.L("\nLOAD   = Reflects whether the unit definition was properly loaded.\nACTIVE = The high-level unit activation state, i.e. generalization of SUB.\nSUB    = The low-level unit activation state, values depend on unit type.\n" + rows.Count + " loaded units listed." + (o.Has("a") ? "" : " Pass --all to see loaded but inactive units, too.") + "\nTo show all installed unit files use 'systemctl list-unit-files'.");
            return 0;
        }

        static int ListUnitFiles(Sh sh, Io io, Opts o, string user)
        {
            var m = sh.M;
            string type = o.Get("t");
            var names = new SortedSet<string>(StringComparer.Ordinal);
            var dirs = user == null ? Machine.SystemPath : m.UserPath(m.User(user).Home);
            foreach (var d in dirs)
            {
                try
                {
                    var l = m.V.G(d);
                    if (!l.Exists || !l.IsDir) continue;
                    foreach (var k in l.Node.Kids.Keys) { var n = m.V.GNoFollow(d + "/" + k); if (n.Node.T == 'd') continue; if (Regex.IsMatch(k, "\\.(service|target|timer|socket|mount|path|slice)$")) names.Add(k); }
                }
                catch (VfsEx) { }
            }
            var filter = o.Args.Skip(1).ToList();
            var rows = new List<string[]>();
            foreach (var n in names)
            {
                if (type != null && !n.EndsWith("." + type, StringComparison.Ordinal)) continue;
                if (filter.Count > 0 && !filter.Any(f => Sh.GlobMatch(f, n))) continue;
                string st = m.EnabledState(n, user);
                if (o.Has("state") && st != o.Get("state")) continue;
                string preset = st == "static" ? "-" : (new[] { "sshd.service", "crond.service", "chronyd.service", "firewalld.service", "NetworkManager.service", "rsyslog.service", "tuned.service", "auditd.service", "atd.service", "logrotate.timer" }.Contains(n) ? "enabled" : "disabled");
                rows.Add(new[] { n, st, preset });
            }
            int w0 = Math.Max(9, rows.Count == 0 ? 0 : rows.Max(r => r[0].Length)), w1 = Math.Max(5, rows.Count == 0 ? 0 : rows.Max(r => r[1].Length));
            if (!o.Has("no-legend")) io.L("UNIT FILE".PadRight(w0) + " " + "STATE".PadRight(w1) + " PRESET");
            foreach (var r in rows) io.L(r[0].PadRight(w0) + " " + r[1].PadRight(w1) + " " + r[2]);
            if (!o.Has("no-legend")) io.L("\n" + rows.Count + " unit files listed.");
            return 0;
        }

        static int ListTimers(Sh sh, Io io, Opts o, string user)
        {
            var m = sh.M;
            io.L("NEXT                        LEFT          LAST                        PASSED       UNIT                         ACTIVATES");
            int n = 0;
            foreach (var kv in m.Services.Where(x => x.Key.EndsWith(".timer", StringComparison.Ordinal) && x.Value.Active == "active" && (user == null ? !x.Key.StartsWith("user:", StringComparison.Ordinal) : x.Key.StartsWith("user:" + user + ":", StringComparison.Ordinal))))
            {
                string unit = kv.Value.Unit;
                var u = m.GetUnit(unit, user);
                long last; if (!m.TimerLast.TryGetValue(kv.Key, out last)) last = kv.Value.Since;
                long next = m.NextTimerRun(u, last, kv.Value.Since);
                string act = u.Get("Timer.Unit") ?? unit.Replace(".timer", ".service");
                io.L((next < 0 ? "-" : StatusDate(m, next)).PadRight(27) + " " + (next < 0 ? "-" : Left(next - m.Now)).PadRight(13) + " " + (last == kv.Value.Since ? "-" : StatusDate(m, last)).PadRight(27) + " " + (last == kv.Value.Since ? "-" : Ago(m.Now - last).Replace(" ago", "")).PadRight(12) + " " + unit.PadRight(28) + " " + act);
                n++;
            }
            io.L("\n" + n + " timers listed.\nPass --all to see loaded but inactive timers, too.");
            return 0;
        }
        static string Left(long s) { if (s < 0) s = 0; if (s < 60) return s + "s"; if (s < 3600) return s / 60 + "min " + s % 60 + "s"; if (s < 86400) return s / 3600 + "h " + s % 3600 / 60 + "min"; return s / 86400 + " day" + (s / 86400 > 1 ? "s" : "") + " " + s % 86400 / 3600 + "h"; }

        static int SysStatus(Sh sh, Io io, List<string> units, string user, Opts o)
        {
            var m = sh.M;
            if (units.Count == 0)
            {
                io.L(Bullet + " " + m.HostnameNow + "\n    State: " + (m.Services.Values.Any(s => s.Active == "failed") ? "degraded" : "running") + "\n    Units: " + (m.Services.Count + 120) + " loaded (incl. loaded aliases)\n     Jobs: 0 queued\n   Failed: " + m.Services.Values.Count(s => s.Active == "failed") + " units\n    Since: " + StatusDate(m, m.BootTime) + "; " + Ago(m.Now - m.BootTime) + "\n  systemd: 252-46.el9_5.2\n   CGroup: /\n           " + Tree1 + "user.slice\n           " + Tree1 + "init.scope\n           " + Tree2 + "system.slice");
                return 0;
            }
            int st = 0;
            bool first = true;
            foreach (var u in units)
            {
                if (!first) io.L("");
                first = false;
                // a device or a path: the unit that has it
                string unit = u;
                if (u.StartsWith("/", StringComparison.Ordinal))
                {
                    var mt = m.Mounts.LastOrDefault(x => x.Path == Vfs.Norm(u));
                    if (mt != null) unit = Machine.UnitNameForPath(mt.Path) + ".mount";
                }
                if (unit.EndsWith(".mount", StringComparison.Ordinal))
                {
                    var mt = m.Mounts.FirstOrDefault(x => Machine.UnitNameForPath(x.Path) + ".mount" == unit);
                    if (mt == null) { io.E("Unit " + unit + " could not be found."); st = 4; continue; }
                    io.L(Bullet + " " + unit + " - " + mt.Path + "\n     Loaded: loaded (/etc/fstab; generated)\n     Active: active (mounted) since " + StatusDate(m, m.BootTime) + "; " + Ago(m.Now - m.BootTime) + "\n      Where: " + mt.Path + "\n       What: " + mt.Source + "\n       Docs: man:fstab(5)\n             man:systemd-fstab-generator(8)");
                    continue;
                }
                var ui = m.GetUnit(unit, user);
                var s = m.Svc(unit, user);
                if (!ui.Found && s == null) { io.E("Unit " + unit + " could not be found."); st = 4; continue; }
                string active = s == null ? "inactive" : s.Active, sub = s == null ? "dead" : s.Sub;
                string dot = active == "active" ? Bullet : active == "failed" ? "\u00d7" : "\u25cb";
                string enabled = m.EnabledState(unit, user);
                string preset = new[] { "sshd.service", "crond.service", "chronyd.service", "firewalld.service", "NetworkManager.service", "rsyslog.service", "tuned.service", "auditd.service", "atd.service" }.Contains(unit) ? "enabled" : "disabled";
                io.L(dot + " " + unit + " - " + ui.Description);
                if (ui.Masked) io.L("     Loaded: masked (Reason: Unit " + unit + " is masked.)");
                else io.L("     Loaded: loaded (" + ui.Path + "; " + enabled + (enabled == "static" ? "" : "; preset: " + preset) + ")" + (ui.DropIns.Count > 0 ? "\n    Drop-In: " + string.Join("\n             ", ui.DropIns.ToArray()) : ""));
                string since = s == null ? "" : " since " + StatusDate(m, s.Since) + "; " + Ago(m.Now - s.Since);
                string act = active == "failed" ? "failed (Result: " + s.Result + ")" : active + " (" + sub + ")";
                io.L("     Active: " + act + since);
                if (ui.Kind == "timer" && s != null && s.Active == "active")
                {
                    long last; if (!m.TimerLast.TryGetValue(user == null ? unit : "user:" + user + ":" + unit, out last)) last = s.Since;
                    long next = m.NextTimerRun(ui, last, s.Since);
                    io.L("    Trigger: " + (next < 0 ? "n/a" : StatusDate(m, next) + "; " + Left(next - m.Now) + " left"));
                    io.L("   Triggers: " + Bullet + " " + (ui.Get("Timer.Unit") ?? unit.Replace(".timer", ".service")));
                }
                var docs = ui.All("Unit.Documentation").SelectMany(x => x.Split(' ')).Where(x => x.Length > 0).ToList();
                if (docs.Count > 0) io.L("       Docs: " + string.Join("\n             ", docs.ToArray()));
                if (s != null && s.Active == "active" && s.Pid > 0)
                {
                    var procs = m.Procs.Where(p => p.Unit == unit).ToList();
                    var main = m.ProcByPid(s.Pid);
                    io.L("   Main PID: " + s.Pid + " (" + (main != null ? main.Comm : Vfs.Base((ui.Get("Service.ExecStart") ?? "x").TrimStart('-').Split(' ')[0])) + ")");
                    if (unit == "httpd.service") io.L("     Status: \"Started, listening on: " + string.Join(", ", s.Ports.Select(p => "port " + p).ToArray()) + "\"");
                    io.L("      Tasks: " + Math.Max(1, procs.Count) + " (limit: 22964)");
                    io.L("     Memory: " + (s.MemKb / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + "M");
                    io.L("        CPU: " + (37 + unit.Length * 3) + "ms");
                    io.L("     CGroup: /" + (user == null ? "system.slice/" : "user.slice/user-" + m.User(user).Uid + ".slice/user@" + m.User(user).Uid + ".service/app.slice/") + unit);
                    if (procs.Count == 0) procs.Add(new ProcEntry { Pid = s.Pid, Cmd = ui.Get("Service.ExecStart") ?? unit });
                    for (int i = 0; i < procs.Count; i++) io.L("             " + (i == procs.Count - 1 ? Tree2 : Tree1) + procs[i].Pid + " \"" + procs[i].Cmd + "\"");
                }
                else if (s != null && s.Active == "active" && s.Sub == "exited") io.L("    Process: " + (s.Pid > 0 ? s.Pid : 1000) + " ExecStart=" + (ui.Get("Service.ExecStart") ?? "") + " (code=exited, status=0/SUCCESS)\n   Main PID: " + (s.Pid > 0 ? s.Pid : 1000) + " (code=exited, status=0/SUCCESS)");
                else if (s != null && s.Active == "failed") io.L("    Process: " + (s.Pid > 0 ? s.Pid : m.NextPid) + " ExecStart=" + (ui.Get("Service.ExecStart") ?? "") + " (code=exited, status=1/FAILURE)\n   Main PID: " + (s.Pid > 0 ? s.Pid : m.NextPid) + " (code=exited, status=1/FAILURE)\n        CPU: 31ms");
                // the last lines of its journal
                int n = o.Has("n") ? int.Parse(o.Get("n")) : 10;
                var lines = m.Journal.Where(j => j.Unit == unit && j.Boot == m.BootId).ToList();
                if (lines.Count > 0 && n > 0)
                {
                    io.L("");
                    foreach (var j in lines.Skip(Math.Max(0, lines.Count - n))) io.L(JournalLine(m, j, "short"));
                }
                if (active != "active") st = 3;
            }
            return st;
        }

        // ------------------------------------------------------------ journalctl
        public static string BootIdHex(Machine m, int boot) { var r = new Random(m.Seed * 977 + boot * 31); var sb = new StringBuilder(); for (int i = 0; i < 32; i++) sb.Append("0123456789abcdef"[r.Next(16)]); return sb.ToString(); }

        public static string JournalLine(Machine m, JournalEntry j, string fmt)
        {
            var d = m.LocalTime(j.Time);
            string host = m.ShortHost == "localhost" ? "localhost" : m.HostnameNow;
            if (fmt == "cat") return j.Msg;
            if (fmt == "short-iso") return d.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture) + Tz.OffsetText(m.TimeZoneName, j.Time).Insert(3, ":") + " " + host + " " + j.Ident + (j.Pid > 0 ? "[" + j.Pid + "]" : "") + ": " + j.Msg;
            if (fmt == "short-precise") return d.ToString("MMM dd HH:mm:ss", CultureInfo.InvariantCulture) + ".123456 " + host + " " + j.Ident + (j.Pid > 0 ? "[" + j.Pid + "]" : "") + ": " + j.Msg;
            if (fmt == "verbose") return d.ToString("ddd yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture) + " " + Tz.Abbrev(m.TimeZoneName, j.Time) + " [s=" + BootIdHex(m, j.Boot).Substring(0, 12) + "]\n    PRIORITY=" + j.Pri + "\n    _BOOT_ID=" + BootIdHex(m, j.Boot) + "\n    _HOSTNAME=" + host + "\n    SYSLOG_IDENTIFIER=" + j.Ident + (j.Pid > 0 ? "\n    _PID=" + j.Pid : "") + (j.Unit.Length > 0 ? "\n    _SYSTEMD_UNIT=" + j.Unit : "") + "\n    MESSAGE=" + j.Msg;
            if (fmt == "json") return "{\"__REALTIME_TIMESTAMP\":\"" + j.Time + "000000\",\"_BOOT_ID\":\"" + BootIdHex(m, j.Boot) + "\",\"PRIORITY\":\"" + j.Pri + "\",\"_HOSTNAME\":\"" + host + "\",\"SYSLOG_IDENTIFIER\":\"" + j.Ident + "\",\"_PID\":\"" + j.Pid + "\",\"_SYSTEMD_UNIT\":\"" + j.Unit + "\",\"MESSAGE\":\"" + j.Msg.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"}";
            return d.ToString("MMM dd HH:mm:ss", CultureInfo.InvariantCulture) + " " + host + " " + j.Ident + (j.Pid > 0 ? "[" + j.Pid + "]" : "") + ": " + j.Msg;
        }

        /// <summary>Is a target unit reached now (the machine's target and the targets it pulls in)?</summary>
        static bool TargetReached(Machine m, string unit)
        {
            if (!unit.EndsWith(".target", StringComparison.Ordinal)) return false;
            var on = new List<string> { "sysinit.target", "local-fs.target", "basic.target" };
            if (m.State == "emergency") return unit == "emergency.target";
            if (m.State == "rescue") { on.Add("rescue.target"); return on.Contains(unit); }
            if (m.State != "running") return false;
            on.AddRange(new[] { "sockets.target", "timers.target", "paths.target", "network.target", "network-online.target", "remote-fs.target", "getty.target", "multi-user.target" });
            on.Add(m.Target);
            if (m.Target == m.DefaultTarget()) on.Add("default.target");
            if (m.Target != "graphical.target") on.Remove("graphical.target");
            return on.Contains(unit);
        }

        static int Journalctl(Sh sh, List<string> a, Io io)
        {
            // -b takes an optional value: "-b" alone (or "-xb", "-b -p err") means the current boot
            a = new List<string>(a);
            for (int i = 1; i < a.Count; i++)
            {
                string s = a[i];
                if (s.StartsWith("-", StringComparison.Ordinal) && !s.StartsWith("--", StringComparison.Ordinal) && s.EndsWith("b", StringComparison.Ordinal) && s.Length >= 2 && s.Substring(1).All(char.IsLetter))
                {
                    bool valueNext = i + 1 < a.Count && System.Text.RegularExpressions.Regex.IsMatch(a[i + 1], "^([+-]?\\d+|[0-9a-f]{8,32})$");
                    if (!valueNext) { a[i] = s.Length > 2 ? s.Substring(0, s.Length - 1) : "--boot="; if (s.Length > 2) a.Insert(i + 1, "--boot="); }
                }
            }
            var o = Opts.Parse(a, "u:b:p:n:fraexkt:o:qmg:S:U:", "unit=|u,boot=?|b,priority=|p,lines=?|n,follow|f,reverse|r,pager-end|e,catalog|x,dmesg|k,identifier=|t,output=|o,quiet|q,merge|m,grep=|g,since=|S,until=|U,list-boots,disk-usage,no-pager,user,system,vacuum-size=,vacuum-time=,flush,rotate,verify,no-hostname,utc,all|a,case-sensitive=?,field=,user-unit=,header,sync,relinquish-var", "journalctl");
            if (o.Error != null) return Fail(io, o);
            var m = sh.M;
            if (o.Has("flush") || o.Has("rotate") || o.Has("sync") || o.Has("relinquish-var")) { if (sh.P.Cr.Uid != 0) { io.E("Failed to flush journal: Access denied"); return 1; } m.FinishJournal(); return 0; }
            if (o.Has("vacuum-size") || o.Has("vacuum-time")) { io.L("Vacuuming done, freed 0B of archived journals from /var/log/journal/" + m.MachineId + "."); return 0; }
            if (o.Has("verify")) { io.L("PASS: /run/log/journal/" + m.MachineId + "/system.journal"); return 0; }
            bool persistent = m.JournalPersistentThisBoot;
            if (o.Has("disk-usage")) { io.L("Archived and active journals take up " + (persistent ? "16.0M in the file system." : "8.0M in the file system.")); return 0; }
            var boots = m.Journal.Select(j => j.Boot).Distinct().OrderBy(x => x).ToList();
            if (o.Has("list-boots"))
            {
                io.L("IDX BOOT ID                          FIRST ENTRY                  LAST ENTRY                  ");
                for (int i = 0; i < boots.Count; i++)
                {
                    var es = m.Journal.Where(j => j.Boot == boots[i]).ToList();
                    io.L((i - boots.Count + 1).ToString().PadLeft(3) + " " + BootIdHex(m, boots[i]) + " " + StatusDate(m, es.First().Time) + " " + StatusDate(m, es.Last().Time));
                }
                return 0;
            }
            IEnumerable<JournalEntry> q = m.Journal;
            bool privileged = sh.P.Cr.Uid == 0 || sh.P.Cr.InGroup(10) || sh.P.Cr.InGroup(4) || sh.P.Cr.InGroup(190);
            if (!privileged) q = q.Where(j => j.Uid == sh.P.Cr.Uid && j.Uid != 0);
            if (o.Has("b"))
            {
                string b = o.Get("b");
                int off = 0;
                if (b.Length > 0 && !int.TryParse(b, out off))
                {
                    int idx = boots.FindIndex(x => BootIdHex(m, x).StartsWith(b, StringComparison.Ordinal));
                    if (idx < 0) { io.E("Data from the specified boot (" + b + ") is not available: No such boot ID in journal"); return 1; }
                    off = idx - boots.Count + 1;
                }
                if (off > 0) off = off - boots.Count;
                int wantIdx = boots.Count - 1 + off;
                if (wantIdx < 0 || wantIdx >= boots.Count) { io.E("Data from the specified boot (" + (off == 0 ? "+0" : off.ToString()) + ") is not available: No such boot ID in journal"); return 1; }
                int want = boots[wantIdx];
                q = q.Where(j => j.Boot == want);
            }
            var units = o.All("u").Select(u => u.Contains(".") ? u : u + ".service").ToList();   // several -u: any of them
            if (units.Count > 0) q = q.Where(j => units.Any(uu => j.Unit == uu || Sh.GlobMatch(uu, j.Unit)));
            if (o.Has("t")) { var ids = o.All("t"); q = q.Where(j => ids.Contains(j.Ident)); }
            if (o.Has("k")) q = q.Where(j => j.Ident == "kernel");
            if (o.Has("p"))
            {
                string p = o.Get("p");
                var pr = p.Split(new[] { ".." }, StringSplitOptions.None);
                int hi = PrioNum(pr[0]), lo = pr.Length > 1 ? PrioNum(pr[1]) : 0;
                if (hi < 0) { io.E("Failed to parse log level: " + p); return 1; }
                if (pr.Length > 1) { int a1 = Math.Min(hi, lo), b1 = Math.Max(hi, lo); q = q.Where(j => j.Pri >= a1 && j.Pri <= b1); }
                else q = q.Where(j => j.Pri <= hi);
            }
            foreach (var match in o.Args)
            {
                var mm = Regex.Match(match, "^(_COMM|_PID|_UID|_SYSTEMD_UNIT|SYSLOG_IDENTIFIER|PRIORITY|_EXE)=(.*)$");
                if (!mm.Success) { if (match.StartsWith("/", StringComparison.Ordinal)) { string c = Vfs.Base(match); q = q.Where(j => j.Ident == c); continue; } io.E("\"" + match + "\" is not a valid match."); return 1; }
                string v = mm.Groups[2].Value;
                switch (mm.Groups[1].Value)
                {
                    case "_COMM": case "SYSLOG_IDENTIFIER": q = q.Where(j => j.Ident == v); break;
                    case "_PID": q = q.Where(j => j.Pid.ToString() == v); break;
                    case "_UID": q = q.Where(j => j.Uid.ToString() == v); break;
                    case "_SYSTEMD_UNIT": q = q.Where(j => j.Unit == v); break;
                    case "PRIORITY": q = q.Where(j => j.Pri.ToString() == v); break;
                    case "_EXE": q = q.Where(j => j.Ident == Vfs.Base(v)); break;
                }
            }
            if (o.Has("S")) { long t; if (!JournalTime(m, o.Get("S"), out t)) { io.E("Failed to parse timestamp: " + o.Get("S")); return 1; } q = q.Where(j => j.Time >= t); }
            if (o.Has("U")) { long t; if (!JournalTime(m, o.Get("U"), out t)) { io.E("Failed to parse timestamp: " + o.Get("U")); return 1; } q = q.Where(j => j.Time <= t); }
            if (o.Has("g")) { var rx = new Regex(o.Get("g"), RegexOptions.IgnoreCase); q = q.Where(j => rx.IsMatch(j.Msg)); }
            var list = q.ToList();
            if (o.Has("n") || o.Has("e") || o.Has("f"))
            {
                int n = 10; string ns = o.Get("n"); if (!string.IsNullOrEmpty(ns) && ns != "all") int.TryParse(ns, out n);
                if (o.Has("e") && !o.Has("n")) n = 1000;
                if (ns != "all") list = list.Skip(Math.Max(0, list.Count - n)).ToList();
            }
            if (o.Has("r")) list.Reverse();
            string fmt = o.Get("o", "short");
            if (!privileged && !o.Has("q")) io.L("Hint: You are currently not seeing messages from other users and the system.\n      Users in groups 'adm', 'systemd-journal', 'wheel' can see all messages.\n      Pass -q to turn off this notice.");
            if (list.Count == 0) { if (!o.Has("q")) io.L("-- No entries --"); }
            int lastBoot = -1;
            foreach (var j in list)
            {
                if (lastBoot >= 0 && j.Boot != lastBoot && fmt.StartsWith("short", StringComparison.Ordinal)) io.L("-- Boot " + BootIdHex(m, j.Boot) + " --");
                lastBoot = j.Boot;
                io.L(JournalLine(m, j, fmt));
                if (o.Has("x") && j.Msg.StartsWith("Failed to start", StringComparison.Ordinal)) io.L("\u2591\u2591 Subject: A start job for unit " + j.Unit + " has failed\n\u2591\u2591 Defined-By: systemd\n\u2591\u2591 Support: https://access.redhat.com/support\n\u2591\u2591 \n\u2591\u2591 A start job for unit " + j.Unit + " has finished with a failure.\n\u2591\u2591 \n\u2591\u2591 The job identifier is " + (j.Pid + 900) + " and the job result is failed.");
            }
            if (o.Has("f") && sh.S != null && io.Out.IsTty) { try { sh.S.WaitForBreak(); } catch (BreakSignal) { io.P("^C\n"); return 130; } }
            return 0;
        }
        static int PrioNum(string s)
        {
            int n; if (int.TryParse(s, out n)) return n;
            switch (s) { case "emerg": return 0; case "alert": return 1; case "crit": return 2; case "err": case "error": return 3; case "warning": case "warn": return 4; case "notice": return 5; case "info": return 6; case "debug": return 7; }
            return -1;
        }
        static bool JournalTime(Machine m, string s, out long t)
        {
            s = s.Trim();
            var today = m.LocalTime(m.Now).Date;
            if (s == "today") { t = LocalToEpoch(m, today); return true; }
            if (s == "yesterday") { t = LocalToEpoch(m, today.AddDays(-1)); return true; }
            if (s == "now") { t = m.Now; return true; }
            if (s == "tomorrow") { t = LocalToEpoch(m, today.AddDays(1)); return true; }
            var rel = Regex.Match(s, "^-(\\d+)\\s*(s|sec|min|m|h|hour|hours|d|day|days)$");
            if (rel.Success) { long n = long.Parse(rel.Groups[1].Value); string u = rel.Groups[2].Value; t = m.Now - n * (u.StartsWith("s", StringComparison.Ordinal) ? 1 : u.StartsWith("mi", StringComparison.Ordinal) || u == "m" ? 60 : u.StartsWith("h", StringComparison.Ordinal) ? 3600 : 86400); return true; }
            var ago = Regex.Match(s, "^(\\d+)\\s*(second|minute|hour|day)s?\\s+ago$");
            if (ago.Success) { long n = long.Parse(ago.Groups[1].Value); string u = ago.Groups[2].Value; t = m.Now - n * (u == "second" ? 1 : u == "minute" ? 60 : u == "hour" ? 3600 : 86400); return true; }
            return ParseDate(m, s, out t);
        }

        static int Loginctl(Sh sh, List<string> a, Io io)
        {
            var m = sh.M;
            string verb = a.Count > 1 ? a[1] : "list-sessions";
            var users = a.Skip(2).ToList();
            if (users.Count == 0) users.Add(sh.P.Cr.User);
            switch (verb)
            {
                case "enable-linger": case "disable-linger":
                    foreach (var u in users)
                    {
                        var pw = m.User(u);
                        if (pw == null) { io.E("Failed to look up user " + u + ": No such process"); return 1; }
                        if (sh.P.Cr.Uid != 0 && u != sh.P.Cr.User) { io.E("Could not enable linger: Access denied"); return 1; }
                        if (verb == "enable-linger") { m.V.EnsureDir("/var/lib/systemd/linger", 0x1ED, null); m.V.Put("/var/lib/systemd/linger/" + u, "", 0x1A4, 0, 0, null); m.StartUserManager(u); }
                        else m.V.RemoveG("/var/lib/systemd/linger/" + u);
                    }
                    return 0;
                case "show-user":
                    {
                        var pw = m.User(users[0]);
                        if (pw == null) { io.E("Failed to get user: User ID " + users[0] + " is not logged in or lingering"); return 1; }
                        bool linger = m.Lingering(pw.Name);
                        var props = new List<string> { "UID=" + pw.Uid, "GID=" + pw.Gid, "Name=" + pw.Name, "RuntimePath=/run/user/" + pw.Uid, "State=" + (m.Logins.Any(x => x.StartsWith(pw.Name + " ", StringComparison.Ordinal)) ? "active" : linger ? "lingering" : "offline"), "Linger=" + (linger ? "yes" : "no") };
                        var want = a.Where(x => x.StartsWith("--property=", StringComparison.Ordinal)).Select(x => x.Substring(11)).Concat(a.SkipWhile(x => x != "-p").Skip(1).Take(1)).ToList();
                        foreach (var p in props) if (want.Count == 0 || want.Any(w => p.StartsWith(w + "=", StringComparison.Ordinal))) io.L(a.Contains("--value") ? p.Substring(p.IndexOf('=') + 1) : p);
                        return 0;
                    }
                case "list-users": io.L("  UID USER    LINGER STATE  "); foreach (var l in m.Logins.Select(x => x.Split(' ')[0]).Distinct()) { var pw = m.User(l); io.L(pw.Uid.ToString().PadLeft(5) + " " + l.PadRight(7) + " " + (m.Lingering(l) ? "yes" : "no").PadRight(6) + " active"); } io.L("\n" + m.Logins.Select(x => x.Split(' ')[0]).Distinct().Count() + " users listed."); return 0;
                case "list-sessions": io.L("SESSION  UID USER    SEAT  TTY  "); int i = 1; foreach (var l in m.Logins) { var p = l.Split(' '); var pw = m.User(p[0]); io.L((i++).ToString().PadLeft(7) + " " + (pw == null ? 0 : pw.Uid).ToString().PadLeft(4) + " " + p[0].PadRight(7) + " " + (p[1] == "tty1" ? "seat0" : "").PadRight(5) + " " + p[1]); } io.L("\n" + m.Logins.Count + " sessions listed."); return 0;
                case "terminate-user": case "kill-user": return 0;
                case "user-status": io.L(users[0] + " (" + (m.User(users[0]) == null ? 0 : m.User(users[0]).Uid) + ")\n           Since: " + StatusDate(m, m.BootTime) + "\n           State: " + (m.Lingering(users[0]) ? "lingering" : "active") + "\n          Linger: " + (m.Lingering(users[0]) ? "yes" : "no")); return 0;
            }
            io.E("Unknown command verb " + verb + ".");
            return 1;
        }
    }
}
