// Cert Lab - users and groups: id, groups, useradd, usermod, userdel, groupadd, groupmod, groupdel, gpasswd,
// passwd, chage, chpasswd, su, sudo (with /etc/sudoers and sudoers.d rules), visudo, getent, who, w, last.
// They read and write /etc/passwd, /etc/shadow, /etc/group and /etc/gshadow and honour /etc/login.defs and
// /etc/default/useradd like shadow-utils does.
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
        static void RegUsers()
        {
            P("id", Id); P("whoami", (sh, a, io) => { io.L(sh.P.Cr.User); return 0; }); P("logname", (sh, a, io) => { io.L(sh.GetVar("LOGNAME") ?? sh.P.Cr.User); return 0; });
            P("groups", Groups); P("useradd", Useradd); P("adduser", Useradd); P("usermod", Usermod); P("userdel", Userdel);
            P("groupadd", Groupadd); P("groupmod", Groupmod); P("groupdel", Groupdel); P("gpasswd", Gpasswd); P("passwd", Passwd);
            P("chage", Chage); P("chpasswd", Chpasswd); P("su", Su); P("sudo", Sudo); P("sudoedit", (sh, a, io) => { var b = new List<string>(a); b[0] = "sudo"; b.Insert(1, "-e"); return Sudo(sh, b, io); });
            P("getent", Getent); P("who", Who); P("w", W); P("users", (sh, a, io) => { io.L(string.Join(" ", sh.M.Logins.Select(x => x.Split(' ')[0]).Distinct().OrderBy(x => x).ToArray())); return 0; });
            P("last", Last); P("lastb", (sh, a, io) => { io.L("\nbtmp begins " + DateText(sh.M, sh.M.BootTime)); return 0; }); P("lastlog", Lastlog);
            P("newgrp", Newgrp); P("pwck", (sh, a, io) => 0); P("grpck", (sh, a, io) => 0); P("pwconv", (sh, a, io) => 0); P("grpconv", (sh, a, io) => 0);
            P("newusers", (sh, a, io) => { foreach (var l in Lines(io.In.ReadAll())) { var p = l.Split(':'); if (p.Length >= 7) { Useradd(sh, new List<string> { "useradd", "-m", "-c", p[4], "-s", p[6], p[0] }, new Io { In = io.In, Out = new NullSink(), Err = io.Err }); sh.M.SetPassword(p[0], p[1]); } } return 0; });
        }

        static string LoginDef(Machine m, string key, string def)
        {
            var t = m.V.Cat("/etc/login.defs") ?? "";
            var mm = Regex.Match(t, "(?m)^\\s*" + key + "\\s+(\\S+)");
            return mm.Success ? mm.Groups[1].Value : def;
        }
        static string UseraddDef(Machine m, string key, string def)
        {
            var t = m.V.Cat("/etc/default/useradd") ?? "";
            var mm = Regex.Match(t, "(?m)^\\s*" + key + "=(.*)$");
            return mm.Success ? mm.Groups[1].Value.Trim() : def;
        }
        static bool RootOnly(Sh sh, Io io, string prog)
        {
            if (sh.P.Cr.Uid == 0) return true;
            io.E(prog + ": Permission denied.\n" + prog + ": cannot lock /etc/passwd; try again later.");
            return false;
        }
        static readonly Regex NameRx = new Regex("^[a-z_][a-z0-9_.-]*[$]?$");

        // ------------------------------------------------------------ id / groups
        static int Id(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "ugGnrZaz", "user|u,group|g,groups|G,name|n,real|r,context|Z,zero|z", "id");
            if (o.Error != null) return Fail(io, o);
            var m = sh.M;
            Cred c; bool self = o.Args.Count == 0;
            if (self) c = sh.P.Cr;
            else { c = m.CredFor(o.Args[0]); if (c == null) { int uid; if (int.TryParse(o.Args[0], out uid) && m.UserById(uid) != null) c = m.CredFor(m.UserById(uid).Name); else { io.E("id: '" + o.Args[0] + "': no such user"); return 1; } } }
            Func<int, string> un = x => m.UName(x); Func<int, string> gn = x => m.GName(x);
            var groups = new List<int> { c.Gid }; groups.AddRange(c.Groups.Where(g => g != c.Gid));
            if (o.Has("Z")) { io.L(self ? (sh.P.Domain == "kernel_t" ? "system_u:system_r:kernel_t:s0" : "unconfined_u:unconfined_r:unconfined_t:s0-s0:c0.c1023") : "id: --context (-Z) works only on an SELinux-enabled kernel"); return 0; }
            if (o.Has("u")) { io.L(o.Has("n") ? un(c.Uid) : c.Uid.ToString()); return 0; }
            if (o.Has("g")) { io.L(o.Has("n") ? gn(c.Gid) : c.Gid.ToString()); return 0; }
            if (o.Has("G")) { io.L(string.Join(" ", groups.Select(g => o.Has("n") ? gn(g) : g.ToString()).ToArray())); return 0; }
            string ctx = self && m.SeMode != "disabled" ? " context=" + (sh.P.Domain == "kernel_t" ? "system_u:system_r:kernel_t:s0" : sh.P.Domain == "unconfined_t" ? "unconfined_u:unconfined_r:unconfined_t:s0-s0:c0.c1023" : "system_u:system_r:" + sh.P.Domain + ":s0") : "";
            io.L("uid=" + c.Uid + "(" + un(c.Uid) + ") gid=" + c.Gid + "(" + gn(c.Gid) + ") groups=" + string.Join(",", groups.Select(g => g + "(" + gn(g) + ")").ToArray()) + ctx);
            return 0;
        }
        static int Groups(Sh sh, List<string> a, Io io)
        {
            var m = sh.M;
            if (a.Count == 1) { var c = sh.P.Cr; var g = new List<int> { c.Gid }; g.AddRange(c.Groups.Where(x => x != c.Gid)); io.L(string.Join(" ", g.Select(x => m.GName(x)).ToArray())); return 0; }
            int st = 0;
            foreach (var u in a.Skip(1))
            {
                var c = m.CredFor(u);
                if (c == null) { io.E("groups: '" + u + "': no such user"); st = 1; continue; }
                var g = new List<int> { c.Gid }; g.AddRange(c.Groups.Where(x => x != c.Gid));
                io.L(u + " : " + string.Join(" ", g.Select(x => m.GName(x)).ToArray()));
            }
            return st;
        }

        // ------------------------------------------------------------ useradd / usermod / userdel
        static int Useradd(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "u:g:G:d:mMs:c:e:f:rNUk:p:ob:D", "uid=|u,gid=|g,groups=|G,home-dir=|d,create-home|m,no-create-home|M,shell=|s,comment=|c,expiredate=|e,inactive=|f,system|r,no-user-group|N,user-group|U,skel=|k,password=|p,non-unique|o,base-dir=|b,badname,defaults|D", "useradd");
            if (o.Error != null) return Fail(io, o);
            if (!RootOnly(sh, io, "useradd")) return 1;
            var m = sh.M;
            if (o.Has("D")) { io.L("GROUP=100\nHOME=" + UseraddDef(m, "HOME", "/home") + "\nINACTIVE=" + UseraddDef(m, "INACTIVE", "-1") + "\nEXPIRE=" + UseraddDef(m, "EXPIRE", "") + "\nSHELL=" + UseraddDef(m, "SHELL", "/bin/bash") + "\nSKEL=" + UseraddDef(m, "SKEL", "/etc/skel") + "\nCREATE_MAIL_SPOOL=yes"); return 0; }
            if (o.Args.Count != 1) { io.E("Usage: useradd [options] LOGIN\n       useradd -D\n       useradd -D [options]"); return 2; }
            string name = o.Args[0];
            if (!NameRx.IsMatch(name) || name.Length > 32) { io.E("useradd: invalid user name '" + name + "': use --badname to ignore"); return 3; }
            var pw = m.Passwd(); var gr = m.Groups();
            if (pw.Any(x => x.Name == name)) { io.E("useradd: user '" + name + "' already exists"); return 9; }
            bool sys = o.Has("r");
            int uidMin = int.Parse(LoginDef(m, sys ? "SYS_UID_MIN" : "UID_MIN", sys ? "201" : "1000")), uidMax = int.Parse(LoginDef(m, sys ? "SYS_UID_MAX" : "UID_MAX", sys ? "999" : "60000"));
            int gidMin = int.Parse(LoginDef(m, sys ? "SYS_GID_MIN" : "GID_MIN", sys ? "201" : "1000")), gidMax = int.Parse(LoginDef(m, sys ? "SYS_GID_MAX" : "GID_MAX", sys ? "999" : "60000"));
            int uid;
            if (o.Has("u"))
            {
                if (!int.TryParse(o.Get("u"), out uid) || uid < 0) { io.E("useradd: invalid user ID '" + o.Get("u") + "'"); return 3; }
                if (pw.Any(x => x.Uid == uid) && !o.Has("o")) { io.E("useradd: UID " + uid + " is not unique"); return 4; }
            }
            else
            {
                var used = pw.Where(x => x.Uid >= uidMin && x.Uid <= uidMax).Select(x => x.Uid).ToList();
                if (sys) { uid = uidMax; while (pw.Any(x => x.Uid == uid)) uid--; }
                else uid = used.Count == 0 ? uidMin : used.Max() + 1;
            }
            bool userGroups = LoginDef(m, "USERGROUPS_ENAB", "yes") == "yes" && !o.Has("N") || o.Has("U");
            int gid;
            string newGroup = null;
            if (o.Has("g"))
            {
                var g = gr.FirstOrDefault(x => x.Name == o.Get("g") || x.Gid.ToString() == o.Get("g"));
                if (g == null) { io.E("useradd: group '" + o.Get("g") + "' does not exist"); return 6; }
                gid = g.Gid;
            }
            else if (userGroups)
            {
                if (gr.Any(x => x.Name == name)) { io.E("useradd: group " + name + " exists - if you want to add this user to that group, use -g."); return 9; }
                gid = uid;
                if (gr.Any(x => x.Gid == gid) || gid < gidMin || gid > gidMax)
                {
                    var usedG = gr.Where(x => x.Gid >= gidMin && x.Gid <= gidMax).Select(x => x.Gid).ToList();
                    gid = gr.Any(x => x.Gid == uid) ? (usedG.Count == 0 ? gidMin : usedG.Max() + 1) : uid;
                }
                newGroup = name;
            }
            else gid = int.Parse(UseraddDef(m, "GROUP", "100"));
            var extra = new List<string>();
            if (o.Has("G"))
                foreach (var g in o.Get("G").Split(',').Where(x => x.Length > 0))
                {
                    var ge = gr.FirstOrDefault(x => x.Name == g || x.Gid.ToString() == g);
                    if (ge == null) { io.E("useradd: group '" + g + "' does not exist"); return 6; }
                    extra.Add(ge.Name);
                }
            string homeBase = o.Get("b") ?? UseraddDef(m, "HOME", "/home");
            string home = o.Get("d") ?? (homeBase.TrimEnd('/') + "/" + name);
            string shell = o.Get("s") ?? UseraddDef(m, "SHELL", "/bin/bash");
            bool createHome = o.Has("m") || (!o.Has("M") && !sys && LoginDef(m, "CREATE_HOME", "no") == "yes");
            // write the files
            if (newGroup != null) { gr.Add(new GrEnt { Name = newGroup, Gid = gid }); }
            foreach (var g in extra) gr.First(x => x.Name == g).Members.Add(name);
            m.WriteGroups(gr);
            pw.Add(new PwEnt { Name = name, Uid = uid, Gid = gid, Gecos = o.Get("c") ?? "", Home = home, Shell = shell });
            m.WritePasswd(pw);
            var shd = m.Shadow();
            string expire = "";
            if (o.Has("e") && o.Get("e").Length > 0) { var d = ParseDay(o.Get("e")); if (d < -1) { io.E("useradd: invalid date '" + o.Get("e") + "'"); return 3; } expire = d < 0 ? "" : d.ToString(); }
            else if (UseraddDef(m, "EXPIRE", "").Length > 0) { var d = ParseDay(UseraddDef(m, "EXPIRE", "")); if (d >= 0) expire = d.ToString(); }
            string inactive = o.Get("f") ?? UseraddDef(m, "INACTIVE", "-1"); if (inactive == "-1") inactive = "";
            shd.Add(new ShEnt { Name = name, Hash = o.Get("p") ?? "!!", LastChg = m.DaysNow.ToString(), Min = sys ? "" : LoginDef(m, "PASS_MIN_DAYS", "0"), Max = sys ? "" : LoginDef(m, "PASS_MAX_DAYS", "99999"), Warn = sys ? "" : LoginDef(m, "PASS_WARN_AGE", "7"), Inactive = inactive, Expire = expire });
            m.WriteShadow(shd);
            if (!sys)
            {
                var sub = m.V.Cat("/etc/subuid");
                if (sub != null && !sub.Split('\n').Any(x => x.StartsWith(name + ":", StringComparison.Ordinal)))
                {
                    long start = 100000 + 65536L * sub.Split('\n').Count(x => x.Length > 0);
                    m.SysWrite("/etc/subuid", sub + name + ":" + start + ":65536\n");
                    m.SysWrite("/etc/subgid", (m.V.Cat("/etc/subgid") ?? "") + name + ":" + start + ":65536\n");
                }
            }
            if (createHome)
            {
                bool exists = m.V.Exists(home);
                if (exists) io.E("useradd: warning: the home directory " + home + " already exists.\nuseradd: Not copying any file from skel directory into it.");
                else
                {
                    try
                    {
                        if (!m.V.Exists(Vfs.Parent(home))) m.V.EnsureDir(Vfs.Parent(home), 0x1ED, null);
                        var n = m.V.EnsureDir(home, 0x1C0, null);
                        int hm = Convert.ToInt32(LoginDef(m, "HOME_MODE", "0700"), 8);
                        n.Uid = uid; n.Gid = gid; n.Mode = hm;
                        n.Ctx = Selinux.DefaultContext(m, home, 'd');
                        string skel = o.Get("k") ?? UseraddDef(m, "SKEL", "/etc/skel");
                        var sl = m.V.G(skel);
                        if (sl.Exists && sl.IsDir)
                            foreach (var k in sl.Node.Kids.Keys.ToList())
                            {
                                var f = m.V.G(skel + "/" + k);
                                if (f.Node.T != 'f') continue;
                                var c = m.V.Put(home + "/" + k, f.Node.Text ?? "", f.Node.Mode, uid, gid, null);
                            }
                    }
                    catch (VfsEx e) { io.E("useradd: cannot create directory " + home + ": " + e.Message); }
                }
                if (!m.V.Exists("/var/spool/mail/" + name)) m.V.Put("/var/spool/mail/" + name, "", 0x1B0, uid, 12, null);
            }
            m.Log("useradd", sh.Pid, 6, "new user: name=" + name + ", UID=" + uid + ", GID=" + gid + ", home=" + home + ", shell=" + shell + ", from=/dev/" + (sh.S == null ? "pts/0" : sh.S.Tty), null);
            if (newGroup != null) m.Log("useradd", sh.Pid, 6, "new group: name=" + name + ", GID=" + gid, null);
            return 0;
        }

        /// <summary>A date as days since 1970 (YYYY-MM-DD, a number of days, -1 for none); -2 if invalid.</summary>
        static long ParseDay(string s)
        {
            if (s == "-1" || s == "") return -1;
            long d; if (long.TryParse(s, out d)) return d;
            DateTime t;
            if (DateTime.TryParseExact(s, new[] { "yyyy-MM-dd", "MM/dd/yyyy", "yyyy-M-d" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) return (long)(t - new DateTime(1970, 1, 1)).TotalDays;
            return -2;
        }
        static string DayText(string days, string never)
        {
            long d;
            if (!long.TryParse(days, out d) || d < 0) return never;
            return new DateTime(1970, 1, 1).AddDays(d).ToString("MMM dd, yyyy", CultureInfo.InvariantCulture);
        }

        static bool UserBusy(Sh sh, string user)
        {
            foreach (var mm in sh.M.Net.Machines)
                foreach (var s in mm.Sessions)
                    foreach (var x in s.ShStack)
                        if (x.M == sh.M && x.P.Cr.User == user && x != sh) return true;
            return sh.M.Procs.Any(p => p.User == user);
        }

        static int Usermod(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "aG:g:l:d:ms:c:u:LUe:f:p:o", "append|a,groups=|G,gid=|g,login=|l,home=|d,move-home|m,shell=|s,comment=|c,uid=|u,lock|L,unlock|U,expiredate=|e,inactive=|f,password=|p,non-unique|o", "usermod");
            if (o.Error != null) return Fail(io, o);
            if (!RootOnly(sh, io, "usermod")) return 1;
            var m = sh.M;
            if (o.Args.Count != 1) { io.E("Usage: usermod [options] LOGIN"); return 2; }
            string name = o.Args[0];
            var pw = m.Passwd(); var gr = m.Groups(); var shd = m.Shadow();
            var u = pw.FirstOrDefault(x => x.Name == name);
            if (u == null) { io.E("usermod: user '" + name + "' does not exist"); return 6; }
            if (o.Has("a") && !o.Has("G")) { io.E("usermod: -a flag is ONLY allowed with the -G flag\nUsage: usermod [options] LOGIN"); return 2; }
            if (o.Count("V") == 0 && a.Count == 2) { io.E("usermod: no changes"); return 0; }
            if (o.Has("g"))
            {
                var g = gr.FirstOrDefault(x => x.Name == o.Get("g") || x.Gid.ToString() == o.Get("g"));
                if (g == null) { io.E("usermod: group '" + o.Get("g") + "' does not exist"); return 6; }
                u.Gid = g.Gid;
            }
            if (o.Has("G"))
            {
                var want = new List<GrEnt>();
                foreach (var g in o.Get("G").Split(',').Where(x => x.Length > 0))
                {
                    var ge = gr.FirstOrDefault(x => x.Name == g || x.Gid.ToString() == g);
                    if (ge == null) { io.E("usermod: group '" + g + "' does not exist"); return 6; }
                    want.Add(ge);
                }
                if (!o.Has("a")) foreach (var g in gr) g.Members.Remove(name);
                foreach (var g in want) if (!g.Members.Contains(name)) g.Members.Add(name);
            }
            if (o.Has("u"))
            {
                int nu; if (!int.TryParse(o.Get("u"), out nu)) { io.E("usermod: invalid user ID '" + o.Get("u") + "'"); return 3; }
                if (pw.Any(x => x.Uid == nu && x != u) && !o.Has("o")) { io.E("usermod: UID '" + nu + "' already exists"); return 4; }
                if (UserBusy(sh, name)) { io.E("usermod: user " + name + " is currently used by process " + (sh.Pid + 1)); return 8; }
                int old = u.Uid; u.Uid = nu;
                // files in the home directory follow the new UID
                try { var hl = m.V.G(u.Home); if (hl.Exists) ChownTreeUid(m, hl, old, nu); } catch (VfsEx) { }
                try { var ml = m.V.G("/var/spool/mail/" + name); if (ml.Exists && ml.Node.Uid == old) ml.Node.Uid = nu; } catch (VfsEx) { }
            }
            if (o.Has("d"))
            {
                string nh = o.Get("d"), oh = u.Home;
                if (o.Has("m") && m.V.Exists(oh) && !m.V.Exists(nh))
                {
                    try { var from = m.V.G(oh); var to = m.V.G(nh); if (from.PFs == to.PFs) m.V.Rename(null, from, to); } catch (VfsEx e) { io.E("usermod: cannot rename directory " + oh + " to " + nh + ": " + e.Message); }
                }
                u.Home = nh;
            }
            if (o.Has("s")) u.Shell = o.Get("s");
            if (o.Has("c")) u.Gecos = o.Get("c");
            var s = shd.FirstOrDefault(x => x.Name == name);
            if (s != null)
            {
                if (o.Has("L") && !s.Hash.StartsWith("!", StringComparison.Ordinal)) s.Hash = "!" + s.Hash;
                if (o.Has("U")) { if (s.Hash.StartsWith("!", StringComparison.Ordinal) && s.Hash.Length > 1 && s.Hash != "!!") s.Hash = s.Hash.Substring(1); else if (s.Hash == "!!" || s.Hash == "!") { io.E("usermod: unlocking the user's password would result in a passwordless account.\nYou should set a password with usermod -p to unlock this user's password."); } }
                if (o.Has("e")) { var d = ParseDay(o.Get("e")); if (d < -1) { io.E("usermod: invalid date '" + o.Get("e") + "'"); return 3; } s.Expire = d < 0 ? "" : d.ToString(); }
                if (o.Has("f")) s.Inactive = o.Get("f") == "-1" ? "" : o.Get("f");
                if (o.Has("p")) s.Hash = o.Get("p");
            }
            if (o.Has("l"))
            {
                string nn = o.Get("l");
                if (pw.Any(x => x.Name == nn)) { io.E("usermod: user '" + nn + "' already exists"); return 9; }
                if (UserBusy(sh, name)) { io.E("usermod: user " + name + " is currently used by process " + (sh.Pid + 1)); return 8; }
                foreach (var g in gr) for (int i = 0; i < g.Members.Count; i++) if (g.Members[i] == name) g.Members[i] = nn;
                if (s != null) s.Name = nn;
                u.Name = nn;
            }
            m.WritePasswd(pw); m.WriteGroups(gr); m.WriteShadow(shd);
            return 0;
        }
        static void ChownTreeUid(Machine m, Loc l, int old, int nu)
        {
            if (l.Node.Uid == old) l.Node.Uid = nu;
            if (l.Node.T != 'd') return;
            foreach (var k in l.Node.Kids.Keys.ToList()) { try { var c = m.V.GNoFollow(l.GPath + "/" + k); if (c.Exists) ChownTreeUid(m, c, old, nu); } catch (VfsEx) { } }
        }

        static int Userdel(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "rfZ", "remove|r,force|f", "userdel");
            if (o.Error != null) return Fail(io, o);
            if (!RootOnly(sh, io, "userdel")) return 1;
            var m = sh.M;
            if (o.Args.Count != 1) { io.E("Usage: userdel [options] LOGIN"); return 2; }
            string name = o.Args[0];
            var pw = m.Passwd();
            var u = pw.FirstOrDefault(x => x.Name == name);
            if (u == null) { io.E("userdel: user '" + name + "' does not exist"); return 6; }
            if (!o.Has("f") && UserBusy(sh, name)) { io.E("userdel: user " + name + " is currently used by process " + (sh.Pid + 3)); return 8; }
            pw.Remove(u); m.WritePasswd(pw);
            var shd = m.Shadow(); shd.RemoveAll(x => x.Name == name); m.WriteShadow(shd);
            var gr = m.Groups();
            foreach (var g in gr) g.Members.Remove(name);
            var own = gr.FirstOrDefault(x => x.Name == name && x.Gid == u.Gid);
            if (own != null && LoginDef(m, "USERGROUPS_ENAB", "yes") == "yes" && own.Members.Count == 0 && !pw.Any(x => x.Gid == own.Gid)) gr.Remove(own);
            m.WriteGroups(gr);
            foreach (var f in new[] { "/etc/subuid", "/etc/subgid" }) { var t = m.V.Cat(f); if (t != null) m.SysWrite(f, string.Join("", t.Split('\n').Where(x => x.Length > 0 && !x.StartsWith(name + ":", StringComparison.Ordinal)).Select(x => x + "\n").ToArray())); }
            m.V.RemoveG("/var/spool/cron/" + name);
            if (o.Has("r"))
            {
                if (m.V.Exists(u.Home)) m.V.RemoveG(u.Home);
                else io.E("userdel: " + u.Home + " home directory (" + u.Home + ") not found");
                if (m.V.Exists("/var/spool/mail/" + name)) m.V.RemoveG("/var/spool/mail/" + name);
                else io.E("userdel: " + name + " mail spool (/var/spool/mail/" + name + ") not found");
            }
            m.Log("userdel", sh.Pid, 6, "delete user '" + name + "'", null);
            return 0;
        }

        // ------------------------------------------------------------ groups
        static int Groupadd(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "g:rfoK:p:", "gid=|g,system|r,force|f,non-unique|o", "groupadd");
            if (o.Error != null) return Fail(io, o);
            if (!RootOnly(sh, io, "groupadd")) return 1;
            var m = sh.M;
            if (o.Args.Count != 1) { io.E("Usage: groupadd [options] GROUP"); return 2; }
            string name = o.Args[0];
            if (!NameRx.IsMatch(name)) { io.E("groupadd: '" + name + "' is not a valid group name"); return 3; }
            var gr = m.Groups();
            if (gr.Any(x => x.Name == name)) { if (o.Has("f")) return 0; io.E("groupadd: group '" + name + "' already exists"); return 9; }
            int gid;
            bool sys = o.Has("r");
            int min = int.Parse(LoginDef(m, sys ? "SYS_GID_MIN" : "GID_MIN", sys ? "201" : "1000")), max = int.Parse(LoginDef(m, sys ? "SYS_GID_MAX" : "GID_MAX", sys ? "999" : "60000"));
            if (o.Has("g"))
            {
                if (!int.TryParse(o.Get("g"), out gid)) { io.E("groupadd: invalid group ID '" + o.Get("g") + "'"); return 3; }
                if (gr.Any(x => x.Gid == gid) && !o.Has("o")) { if (o.Has("f")) { gid = gr.Where(x => x.Gid >= min && x.Gid <= max).Select(x => x.Gid).DefaultIfEmpty(min - 1).Max() + 1; } else { io.E("groupadd: GID '" + gid + "' already exists"); return 4; } }
            }
            else if (sys) { gid = max; while (gr.Any(x => x.Gid == gid)) gid--; }
            else gid = gr.Where(x => x.Gid >= min && x.Gid <= max).Select(x => x.Gid).DefaultIfEmpty(min - 1).Max() + 1;
            gr.Add(new GrEnt { Name = name, Gid = gid });
            m.WriteGroups(gr);
            m.Log("groupadd", sh.Pid, 6, "group added to /etc/group: name=" + name + ", GID=" + gid, null);
            return 0;
        }
        static int Groupmod(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "g:n:o", "gid=|g,new-name=|n,non-unique|o", "groupmod");
            if (o.Error != null) return Fail(io, o);
            if (!RootOnly(sh, io, "groupmod")) return 1;
            var m = sh.M;
            if (o.Args.Count != 1) { io.E("Usage: groupmod [options] GROUP"); return 2; }
            var gr = m.Groups();
            var g = gr.FirstOrDefault(x => x.Name == o.Args[0]);
            if (g == null) { io.E("groupmod: group '" + o.Args[0] + "' does not exist"); return 6; }
            if (o.Has("n")) { if (gr.Any(x => x.Name == o.Get("n"))) { io.E("groupmod: group '" + o.Get("n") + "' already exists"); return 9; } g.Name = o.Get("n"); }
            if (o.Has("g"))
            {
                int ng; if (!int.TryParse(o.Get("g"), out ng)) { io.E("groupmod: invalid group ID '" + o.Get("g") + "'"); return 3; }
                if (gr.Any(x => x.Gid == ng && x != g) && !o.Has("o")) { io.E("groupmod: GID '" + ng + "' already exists"); return 4; }
                int old = g.Gid; g.Gid = ng;
                var pw = m.Passwd(); foreach (var u in pw) if (u.Gid == old) u.Gid = ng; m.WritePasswd(pw);
            }
            m.WriteGroups(gr);
            return 0;
        }
        static int Groupdel(Sh sh, List<string> a, Io io)
        {
            if (!RootOnly(sh, io, "groupdel")) return 1;
            var m = sh.M;
            var names = a.Skip(1).Where(x => !x.StartsWith("-", StringComparison.Ordinal)).ToList();
            if (names.Count != 1) { io.E("Usage: groupdel [options] GROUP"); return 2; }
            var gr = m.Groups();
            var g = gr.FirstOrDefault(x => x.Name == names[0]);
            if (g == null) { io.E("groupdel: group '" + names[0] + "' does not exist"); return 6; }
            var prim = m.Passwd().FirstOrDefault(x => x.Gid == g.Gid);
            if (prim != null && !a.Contains("-f")) { io.E("groupdel: cannot remove the primary group of user '" + prim.Name + "'"); return 8; }
            gr.Remove(g);
            m.WriteGroups(gr);
            return 0;
        }
        static int Gpasswd(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "a:d:M:A:rR", "add=|a,delete=|d,members=|M,administrators=|A", "gpasswd");
            if (o.Error != null) return Fail(io, o);
            var m = sh.M;
            if (o.Args.Count != 1) { io.E("Usage: gpasswd [option] GROUP"); return 2; }
            var gr = m.Groups();
            var g = gr.FirstOrDefault(x => x.Name == o.Args[0]);
            if (g == null) { io.E("gpasswd: group '" + o.Args[0] + "' does not exist in /etc/group"); return 3; }
            if (sh.P.Cr.Uid != 0) { io.E("gpasswd: Permission denied."); return 1; }
            if (o.Has("a"))
            {
                string u = o.Get("a");
                if (m.User(u) == null) { io.E("gpasswd: user '" + u + "' does not exist"); return 3; }
                io.L("Adding user " + u + " to group " + g.Name);
                if (!g.Members.Contains(u)) g.Members.Add(u);
            }
            else if (o.Has("d"))
            {
                string u = o.Get("d");
                io.L("Removing user " + u + " from group " + g.Name);
                if (!g.Members.Remove(u)) { io.E("gpasswd: user '" + u + "' is not a member of '" + g.Name + "'"); return 3; }
            }
            else if (o.Has("M"))
            {
                var list = o.Get("M").Split(',').Where(x => x.Length > 0).ToList();
                foreach (var u in list) if (m.User(u) == null) { io.E("gpasswd: user '" + u + "' does not exist"); return 3; }
                g.Members = list;
            }
            else if (o.Has("A")) { }
            else
            {
                io.L("Changing the password for group " + g.Name);
                ReadSecret(sh, io, "New Password: "); ReadSecret(sh, io, "Re-enter new password: ");
            }
            m.WriteGroups(gr);
            return 0;
        }
        static int Newgrp(Sh sh, List<string> a, Io io)
        {
            var m = sh.M;
            string gname = a.Count > 1 ? a[a.Count - 1] : m.GName(m.User(sh.P.Cr.User).Gid);
            var g = m.Group(gname);
            if (g == null) { io.E("newgrp: group '" + gname + "' does not exist"); return 1; }
            if (sh.P.Cr.Uid != 0 && !sh.P.Cr.InGroup(g.Gid)) { io.E("Password: \nnewgrp: failed to crypt password with previous salt: Invalid argument"); return 1; }
            var p = sh.P.Copy();
            if (!p.Cr.Groups.Contains(p.Cr.Gid)) p.Cr.Groups.Add(p.Cr.Gid);
            p.Cr.Gid = g.Gid;
            var child = new Sh(m, sh.S, p) { Interactive = true };
            foreach (var kv in sh.Vars) child.Vars[kv.Key] = kv.Value.Copy();
            child.Aliases = sh.Aliases;
            if (sh.S == null) return 0;
            sh.S.ShStack.Add(child);
            try { return child.Repl(); } finally { sh.S.ShStack.Remove(child); }
        }

        // ------------------------------------------------------------ passwords
        static string PwQuality(string pw, string user)
        {
            if (pw.Length < 8) return "BAD PASSWORD: The password is shorter than 8 characters";
            if (pw.ToLowerInvariant().Contains(user.ToLowerInvariant()) && user.Length >= 3) return "BAD PASSWORD: The password contains the user name in some form";
            var dict = new[] { "password", "redhat", "welcome", "qwerty", "letmein", "dragon", "monkey", "master", "sunshine", "princess", "football", "baseball", "superman", "computer", "admin" };
            if (dict.Any(d => pw.ToLowerInvariant().Contains(d))) return "BAD PASSWORD: The password fails the dictionary check - it is based on a dictionary word";
            if (pw.Distinct().Count() < 4) return "BAD PASSWORD: The password contains less than 4 different characters";
            return null;
        }

        static int Passwd(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "ludeSn:x:w:i:kfs", "lock|l,unlock|u,delete|d,expire|e,status|S,stdin,minimum=|n,maximum=|x,warning=|w,inactive=|i,keep-tokens|k,force|f", "passwd");
            if (o.Error != null) return Fail(io, o);
            var m = sh.M;
            string user = o.Args.Count > 0 ? o.Args[0] : sh.P.Cr.User;
            if (o.Args.Count > 0 && sh.P.Cr.Uid != 0 && user != sh.P.Cr.User) { io.E("passwd: Only root can specify a user name."); return 1; }
            if (m.User(user) == null) { io.E("passwd: Unknown user name '" + user + "'."); return 1; }
            var shd = m.Shadow();
            var s = shd.FirstOrDefault(x => x.Name == user);
            if (s == null) { s = new ShEnt { Name = user }; shd.Add(s); }
            bool admin = o.Has("l") || o.Has("u") || o.Has("d") || o.Has("e") || o.Has("n") || o.Has("x") || o.Has("w") || o.Has("i");
            if (admin && sh.P.Cr.Uid != 0) { io.E("passwd: Only root can do that."); return 1; }
            if (o.Has("S"))
            {
                string state = s.Hash.StartsWith("!", StringComparison.Ordinal) ? "LK" : s.Hash.Length == 0 ? "NP" : "PS";
                long lc; string d = long.TryParse(s.LastChg, out lc) ? new DateTime(1970, 1, 1).AddDays(lc).ToString("yyyy-MM-dd") : "1970-01-01";
                io.L(user + " " + state + " " + d + " " + (s.Min.Length > 0 ? s.Min : "0") + " " + (s.Max.Length > 0 ? s.Max : "99999") + " " + (s.Warn.Length > 0 ? s.Warn : "7") + " " + (s.Inactive.Length > 0 ? s.Inactive : "-1") + " (" + (state == "LK" ? "Password locked." : state == "NP" ? "Empty password." : "Password set, SHA512 crypt.") + ")");
                return 0;
            }
            if (o.Has("l")) { io.L("Locking password for user " + user + "."); if (!s.Hash.StartsWith("!", StringComparison.Ordinal)) s.Hash = "!!" + s.Hash; m.WriteShadow(shd); io.L("passwd: Success"); return 0; }
            if (o.Has("u"))
            {
                io.L("Unlocking password for user " + user + ".");
                string h = s.Hash.TrimStart('!');
                if (h.Length == 0) { io.E("passwd: Unsafe operation (use -f to force)"); return 1; }
                s.Hash = h; m.WriteShadow(shd); io.L("passwd: Success"); return 0;
            }
            if (o.Has("d")) { io.L("Removing password for user " + user + "."); s.Hash = ""; m.WriteShadow(shd); io.L("passwd: Success"); return 0; }
            if (o.Has("e")) { io.L("Expiring password for user " + user + "."); s.LastChg = "0"; m.WriteShadow(shd); io.L("passwd: Success"); return 0; }
            if (o.Has("n") || o.Has("x") || o.Has("w") || o.Has("i"))
            {
                io.L("Adjusting aging data for user " + user + ".");
                if (o.Has("n")) s.Min = o.Get("n"); if (o.Has("x")) s.Max = o.Get("x"); if (o.Has("w")) s.Warn = o.Get("w"); if (o.Has("i")) s.Inactive = o.Get("i");
                m.WriteShadow(shd); io.L("passwd: Success"); return 0;
            }
            io.L("Changing password for user " + user + ".");
            string pw1, pw2;
            if (o.Has("stdin"))
            {
                if (sh.P.Cr.Uid != 0) { io.E("passwd: Only root can do that."); return 1; }
                pw1 = io.In.ReadLine();
                if (pw1 == null) { io.E("passwd: Authentication token manipulation error"); return 1; }
                SetPw(sh, user, pw1, io);
                io.L("passwd: all authentication tokens updated successfully.");
                return 0;
            }
            if (sh.P.Cr.Uid != 0)
            {
                io.L("Current password: ");
                string cur = ReadSecret(sh, io, "Current password: ");
                if (cur == null || !m.CheckPassword(user, cur)) { io.E("passwd: Authentication token manipulation error"); return 1; }
                long min, last;
                if (long.TryParse(s.Min, out min) && long.TryParse(s.LastChg, out last) && min > 0 && m.DaysNow < last + min) { io.E("You must wait longer to change your password.\npasswd: Authentication token manipulation error"); return 1; }
            }
            for (int tries = 0; tries < 3; tries++)
            {
                pw1 = ReadSecret(sh, io, "New password: ");
                if (pw1 == null) { io.E("passwd: Authentication token manipulation error"); return 1; }
                string q = PwQuality(pw1, user);
                if (q != null) { io.L(q); if (sh.P.Cr.Uid != 0) { continue; } }
                pw2 = ReadSecret(sh, io, "Retype new password: ");
                if (pw1 != pw2) { io.L("Sorry, passwords do not match."); if (tries == 2) { io.E("passwd: Have exhausted maximum number of retries for service"); return 1; } continue; }
                SetPw(sh, user, pw1, io);
                io.L("passwd: all authentication tokens updated successfully.");
                m.Log("passwd", sh.Pid, 5, "pam_unix(passwd:chauthtok): password changed for " + user, null);
                return 0;
            }
            io.E("passwd: Have exhausted maximum number of retries for service");
            return 1;
        }

        /// <summary>Sets a password. passwd writes a new /etc/shadow (new inode): in the initramfs or a chroot without
        /// the SELinux policy loaded, the new file has no label (the reason for touch /.autorelabel).</summary>
        static void SetPw(Sh sh, string user, string pw, Io io)
        {
            var m = sh.M;
            try
            {
                var l = m.V.Resolve(sh.P, "/etc/shadow", true);
                if (l.Mnt != null && l.Mnt.Ro) { io.E("passwd: Authentication token manipulation error"); throw new ShError("read-only"); }
            }
            catch (VfsEx) { }
            // in a chroot (rd.break) the files are those of the real root
            var root = sh.P.Root;
            m.SetPasswordAt(root, user, pw, sh.P.Domain == "kernel_t");
        }

        static int Chpasswd(Sh sh, List<string> a, Io io)
        {
            if (sh.P.Cr.Uid != 0) { io.E("chpasswd: Permission denied."); return 1; }
            int st = 0, n = 0;
            foreach (var line in Lines(io.In.ReadAll()))
            {
                n++;
                int c = line.IndexOf(':');
                if (c <= 0) { io.E("chpasswd: line " + n + ": missing new password"); st = 1; continue; }
                string u = line.Substring(0, c), p = line.Substring(c + 1);
                if (sh.M.User(u) == null) { io.E("chpasswd: (user " + u + ") pam_chauthtok() failed, error:\nAuthentication token manipulation error"); st = 1; continue; }
                sh.M.SetPassword(u, p);
            }
            return st;
        }

        static int Chage(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "ld:E:I:m:M:W:i", "list|l,lastday=|d,expiredate=|E,inactive=|I,mindays=|m,maxdays=|M,warndays=|W,iso8601|i", "chage");
            if (o.Error != null) return Fail(io, o);
            var m = sh.M;
            if (o.Args.Count != 1) { io.E("Usage: chage [options] LOGIN"); return 2; }
            string user = o.Args[0];
            if (m.User(user) == null) { io.E("chage: user '" + user + "' does not exist in /etc/passwd"); return 1; }
            var shd = m.Shadow();
            var s = shd.FirstOrDefault(x => x.Name == user);
            if (s == null) { s = new ShEnt { Name = user }; shd.Add(s); }
            if (o.Has("l"))
            {
                if (sh.P.Cr.Uid != 0 && sh.P.Cr.User != user) { io.E("chage: Permission denied."); return 1; }
                long lc, mx, iv;
                string expires = "never", inact = "never";
                if (s.LastChg == "0") { expires = "password must be changed"; inact = "password must be changed"; }
                else if (long.TryParse(s.LastChg, out lc) && long.TryParse(s.Max, out mx) && mx >= 0 && mx < 99999)
                {
                    expires = DayText((lc + mx).ToString(), "never");
                    if (long.TryParse(s.Inactive, out iv) && iv >= 0) inact = DayText((lc + mx + iv).ToString(), "never");
                }
                io.L("Last password change\t\t\t\t\t: " + (s.LastChg == "0" ? "password must be changed" : DayText(s.LastChg, "never")));
                io.L("Password expires\t\t\t\t\t: " + expires);
                io.L("Password inactive\t\t\t\t\t: " + inact);
                io.L("Account expires\t\t\t\t\t\t: " + DayText(s.Expire, "never"));
                io.L("Minimum number of days between password change\t\t: " + (s.Min.Length > 0 ? s.Min : "-1"));
                io.L("Maximum number of days between password change\t\t: " + (s.Max.Length > 0 ? s.Max : "-1"));
                io.L("Number of days of warning before password expires\t: " + (s.Warn.Length > 0 ? s.Warn : "-1"));
                return 0;
            }
            if (sh.P.Cr.Uid != 0) { io.E("chage: Permission denied."); return 1; }
            bool any = false;
            if (o.Has("d")) { var d = ParseDay(o.Get("d")); if (d < -1) { io.E("chage: invalid date '" + o.Get("d") + "'"); return 2; } s.LastChg = d < 0 ? "" : d.ToString(); any = true; }
            if (o.Has("E")) { var d = ParseDay(o.Get("E")); if (d < -1) { io.E("chage: invalid date '" + o.Get("E") + "'"); return 2; } s.Expire = d < 0 ? "" : d.ToString(); any = true; }
            if (o.Has("I")) { s.Inactive = o.Get("I") == "-1" ? "" : o.Get("I"); any = true; }
            if (o.Has("m")) { s.Min = o.Get("m") == "-1" ? "" : o.Get("m"); any = true; }
            if (o.Has("M")) { s.Max = o.Get("M") == "-1" ? "" : o.Get("M"); any = true; }
            if (o.Has("W")) { s.Warn = o.Get("W") == "-1" ? "" : o.Get("W"); any = true; }
            if (!any)
            {
                // the interactive form
                io.L("Changing the aging information for " + user + "\nEnter the new value, or press ENTER for the default\n");
                Func<string, string, string> ask = (label, cur) => { var r = sh.S == null ? null : sh.S.ReadLine("\t" + label + " [" + cur + "]: ", false); return string.IsNullOrEmpty(r) ? cur : r.Trim(); };
                s.Min = ask("Minimum Password Age", s.Min.Length > 0 ? s.Min : "0");
                s.Max = ask("Maximum Password Age", s.Max.Length > 0 ? s.Max : "99999");
                long l2, e2; string lc = ask("Last Password Change (YYYY-MM-DD)", long.TryParse(s.LastChg, out l2) ? new DateTime(1970, 1, 1).AddDays(l2).ToString("yyyy-MM-dd") : "-1");
                var d = ParseDay(lc); if (d >= -1) s.LastChg = d < 0 ? "" : d.ToString();
                s.Warn = ask("Password Expiration Warning", s.Warn.Length > 0 ? s.Warn : "7");
                string iv = ask("Password Inactive", s.Inactive.Length > 0 ? s.Inactive : "-1"); s.Inactive = iv == "-1" ? "" : iv;
                string ex = ask("Account Expiration Date (YYYY-MM-DD)", long.TryParse(s.Expire, out e2) ? new DateTime(1970, 1, 1).AddDays(e2).ToString("yyyy-MM-dd") : "-1");
                var de = ParseDay(ex); if (de >= -1) s.Expire = de < 0 ? "" : de.ToString();
            }
            m.WriteShadow(shd);
            m.Log("chage", sh.Pid, 6, "changed password expiry for " + user, null);
            return 0;
        }

        // ------------------------------------------------------------ su
        static int Su(Sh sh, List<string> a, Io io)
        {
            var b = new List<string>(a);
            bool login = false;
            if (b.Contains("-")) { login = true; b.Remove("-"); }
            var o = Opts.Parse(b, "lc:s:mpg:G:", "login|l,command=|c,shell=|s,preserve-environment|m,session-command=", "su");
            if (o.Error != null) return Fail(io, o);
            if (o.Has("l")) login = true;
            var m = sh.M;
            string user = o.Args.Count > 0 ? o.Args[0] : "root";
            var pw = m.User(user);
            if (pw == null) { io.E("su: user " + user + " does not exist or the user entry does not contain all the required fields"); return 1; }
            if (sh.P.Cr.Uid != 0)
            {
                string p = ReadSecret(sh, io, "Password: ");
                if (p == null || !m.Authenticate(user, p))
                {
                    m.Log("su", sh.Pid, 5, "pam_unix(su-l:auth): authentication failure; logname=" + sh.P.Cr.User + " uid=" + sh.P.Cr.Uid + " euid=0 tty=" + (sh.S == null ? "" : sh.S.Tty) + " ruser=" + sh.P.Cr.User + " rhost=  user=" + user, null);
                    io.E("su: Authentication failure");
                    return 1;
                }
            }
            string shell = o.Get("s") ?? pw.Shell;
            if (shell.EndsWith("nologin", StringComparison.Ordinal) || shell.EndsWith("/false", StringComparison.Ordinal)) { io.L("This account is currently not available."); return 1; }
            m.Log("su", sh.Pid, 5, "pam_unix(su" + (login ? "-l" : "") + ":session): session opened for user " + user + "(uid=" + pw.Uid + ") by " + sh.P.Cr.User + "(uid=" + sh.P.Cr.Uid + ")", null);
            if (m.Net != null && m.Net.Range != null && sh.P.Cr.Uid != 0) m.Net.Range.Privilege(m, sh.P.Cr.User, "su " + user);   // Cyber Range: a defender may watch for this
            var ctx = new PCtx { Cr = m.CredFor(user), Root = sh.P.Root, Cwd = login ? pw.Home : sh.P.Cwd, Umask = sh.P.Umask, Domain = sh.P.Domain };
            if (login) { try { if (!m.V.Resolve(ctx, pw.Home, true).Exists) { io.E("su: warning: cannot change directory to " + pw.Home + ": No such file or directory"); ctx.Cwd = sh.P.Cwd; } } catch (VfsEx) { io.E("su: warning: cannot change directory to " + pw.Home + ": Permission denied"); ctx.Cwd = sh.P.Cwd; } }
            var child = new Sh(m, sh.S, ctx) { Host = sh.Host };
            if (login) { child.InitLogin(pw); child.LoginShell = true; }
            else
            {
                foreach (var kv in sh.Vars) child.Vars[kv.Key] = kv.Value.Copy();
                foreach (var kv in sh.Aliases) child.Aliases[kv.Key] = kv.Value;
                if (!o.Has("m")) { child.SetVar("HOME", pw.Home); child.SetVar("SHELL", shell); if (pw.Uid != 0) { child.SetVar("USER", user); child.SetVar("LOGNAME", user); } }
            }
            child.Io = io;
            int st;
            if (o.Has("c"))
            {
                try { st = child.RunText(o.Get("c"), io); } catch (ExitEx e) { st = e.Code; }
                return st;
            }
            if (sh.S == null) return 0;
            sh.S.ShStack.Add(child);
            try { st = child.Repl(); }
            finally { sh.S.ShStack.Remove(child); child.SaveHistory(); }
            m.Log("su", sh.Pid, 5, "pam_unix(su:session): session closed for user " + user, null);
            return st;
        }

        // ------------------------------------------------------------ sudo
        public class SudoRule { public string Who; public string Hosts; public string RunAs; public bool NoPasswd; public List<string> Cmds = new List<string>(); public string File; }

        /// <summary>Reads /etc/sudoers and its #includedir; returns the rules, or the parse error in sudo's words.</summary>
        public static List<SudoRule> SudoRules(Machine m, out string error)
        {
            error = null;
            var rules = new List<SudoRule>();
            var aliases = new Dictionary<string, List<string>>();
            var files = new List<string> { "/etc/sudoers" };
            for (int fi = 0; fi < files.Count; fi++)
            {
                string f = files[fi];
                string t = m.V.Cat(f);
                if (t == null) continue;
                int n = 0;
                foreach (var raw0 in t.Replace("\\\n", " ").Split('\n'))
                {
                    n++;
                    string raw = raw0.Trim();
                    if (raw.StartsWith("#includedir", StringComparison.Ordinal) || raw.StartsWith("@includedir", StringComparison.Ordinal))
                    {
                        string dir = raw.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).Last();
                        try
                        {
                            var d = m.V.G(dir);
                            if (d.Exists && d.IsDir) foreach (var k in d.Node.Kids.Keys.OrderBy(x => x, StringComparer.Ordinal)) if (!k.Contains(".") && !k.EndsWith("~", StringComparison.Ordinal)) files.Add(dir + "/" + k);
                        }
                        catch (VfsEx) { }
                        continue;
                    }
                    if (raw.StartsWith("#include ", StringComparison.Ordinal) || raw.StartsWith("@include ", StringComparison.Ordinal)) { files.Add(raw.Substring(9).Trim()); continue; }
                    int hash = raw.IndexOf('#'); if (hash >= 0) raw = raw.Substring(0, hash).Trim();
                    if (raw.Length == 0) continue;
                    if (raw.StartsWith("Defaults", StringComparison.Ordinal)) continue;
                    var am = Regex.Match(raw, "^(User_Alias|Cmnd_Alias|Host_Alias|Runas_Alias)\\s+([A-Z][A-Z0-9_]*)\\s*=\\s*(.+)$");
                    if (am.Success) { aliases[am.Groups[2].Value] = am.Groups[3].Value.Split(',').Select(x => x.Trim()).ToList(); continue; }
                    var rm = Regex.Match(raw, "^(\\S+)\\s+([^=\\s]+)\\s*=\\s*(\\(([^)]*)\\))?\\s*((NOPASSWD|PASSWD|NOEXEC|EXEC|SETENV|NOSETENV)\\s*:\\s*)*(.+)$");
                    if (!rm.Success) { error = ">>> " + f + ": syntax error near line " + n + " <<<"; return rules; }
                    var r = new SudoRule { Who = rm.Groups[1].Value, Hosts = rm.Groups[2].Value, RunAs = rm.Groups[4].Success ? rm.Groups[4].Value : "root", NoPasswd = raw.Contains("NOPASSWD:"), File = f };
                    string cmds = rm.Groups[7].Value;
                    foreach (var c in cmds.Split(','))
                    {
                        var cc = c.Trim();
                        cc = Regex.Replace(cc, "^(NOPASSWD|PASSWD)\\s*:\\s*", "");
                        if (cc.Length == 0) continue;
                        if (cc != "ALL" && !cc.StartsWith("/", StringComparison.Ordinal) && !cc.StartsWith("!", StringComparison.Ordinal) && !aliases.ContainsKey(cc)) { error = ">>> " + f + ": syntax error near line " + n + " <<<"; return rules; }
                        List<string> al;
                        if (aliases.TryGetValue(cc, out al)) r.Cmds.AddRange(al); else r.Cmds.Add(cc);
                    }
                    List<string> ul;
                    if (aliases.TryGetValue(r.Who, out ul)) { foreach (var u in ul) rules.Add(new SudoRule { Who = u, Hosts = r.Hosts, RunAs = r.RunAs, NoPasswd = r.NoPasswd, Cmds = r.Cmds, File = f }); continue; }
                    rules.Add(r);
                }
            }
            return rules;
        }

        static bool SudoWhoMatches(Machine m, SudoRule r, Cred c)
        {
            foreach (var w0 in r.Who.Split(','))
            {
                var w = w0.Trim();
                if (w == "ALL") return true;
                if (w.StartsWith("%", StringComparison.Ordinal)) { var g = m.Group(w.Substring(1)); if (g != null && c.InGroup(g.Gid)) return true; continue; }
                if (w == c.User || w == "#" + c.Uid) return true;
            }
            return false;
        }
        static bool SudoCmdMatches(SudoRule r, string path, string args)
        {
            bool allowed = false;
            foreach (var c in r.Cmds)
            {
                bool neg = c.StartsWith("!", StringComparison.Ordinal);
                string cc = neg ? c.Substring(1).Trim() : c;
                bool match;
                if (cc == "ALL") match = true;
                else
                {
                    var parts = cc.Split(new[] { ' ' }, 2);
                    string cp = parts[0];
                    bool pathOk = cp == path || (cp.EndsWith("/", StringComparison.Ordinal) && path.StartsWith(cp, StringComparison.Ordinal)) || Sh.GlobMatch(cp, path) || (cp.Replace("/usr/bin/", "/bin/") == path.Replace("/usr/bin/", "/bin/")) || (cp.Replace("/usr/sbin/", "/sbin/") == path.Replace("/usr/sbin/", "/sbin/"));
                    match = pathOk && (parts.Length == 1 || parts[1] == "*" ? true : (parts[1] == "\"\"" ? args.Length == 0 : Sh.GlobMatch(parts[1], args)));
                }
                if (match) { if (neg) return false; allowed = true; }
            }
            return allowed;
        }

        static int Sudo(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "iu:slkKvEbnehg:HSAp:U:", "other-user=|U,login|i,user=|u,shell|s,list|l,reset-timestamp|k,remove-timestamp|K,validate|v,preserve-env|E,background|b,non-interactive|n,edit|e,help|h,group=|g,set-home|H,stdin|S", "sudo", true);
            if (o.Error != null) { io.E(o.Error.Replace("Try 'sudo --help'", "usage: sudo -h | -K | -k | -V\nTry 'sudo --help'")); return 1; }
            var m = sh.M;
            string target = o.Get("u") ?? "root";
            var tpw = m.User(target.TrimStart('#'));
            if (tpw == null) { io.E("sudo: unknown user " + target); return 1; }
            var me = sh.P.Cr;
            if (o.Has("k") || o.Has("K")) { if (sh.S != null) sh.S.SudoUntil = 0; if (o.Args.Count == 0) return 0; }
            string error;
            var rules = SudoRules(m, out error);
            if (error != null) { io.E("sudo: " + error + "\nsudo: no valid sudoers sources found, quitting\nsudo: error initializing audit plugin sudoers_audit"); return 1; }
            var mine = rules.Where(r => SudoWhoMatches(m, r, me)).ToList();
            if (o.Has("l") && o.Has("U"))
            {
                // sudo -l -U USER: root (or a sudo user) lists what USER may do
                var opw = m.User(o.Get("U"));
                if (opw == null) { io.E("sudo: unknown user " + o.Get("U")); return 1; }
                if (me.Uid != 0 && !SudoAuth(sh, io, mine, true, o.Has("n"))) return 1;
                var oc = m.CredFor(opw.Name);
                var theirs = rules.Where(r => SudoWhoMatches(m, r, oc)).ToList();
                if (theirs.Count == 0) { io.L("User " + opw.Name + " is not allowed to run sudo on " + m.ShortHost + "."); return 0; }
                io.L("Matching Defaults entries for " + opw.Name + " on " + m.ShortHost + ":\n    !visiblepw, always_set_home, match_group_by_gid, always_query_group_plugin, env_reset, env_keep=\"COLORS DISPLAY HOSTNAME HISTSIZE KDEDIR LS_COLORS\", env_keep+=\"MAIL QTDIR USERNAME LANG LC_ADDRESS LC_CTYPE\", secure_path=/sbin\\:/bin\\:/usr/sbin\\:/usr/bin\n");
                io.L("User " + opw.Name + " may run the following commands on " + m.ShortHost + ":");
                foreach (var r in theirs) io.L("    (" + r.RunAs + ") " + (r.NoPasswd ? "NOPASSWD: " : "") + string.Join(", ", r.Cmds.ToArray()));
                return 0;
            }
            if (o.Has("l"))
            {
                // listpw=any (the default): no password when one of the user's rules is NOPASSWD
                if (me.Uid != 0 && !mine.Any(r => r.NoPasswd) && !SudoAuth(sh, io, mine, true, o.Has("n"))) return 1;
                io.L("Matching Defaults entries for " + me.User + " on " + m.ShortHost + ":\n    !visiblepw, always_set_home, match_group_by_gid, always_query_group_plugin, env_reset, env_keep=\"COLORS DISPLAY HOSTNAME HISTSIZE KDEDIR LS_COLORS\", env_keep+=\"MAIL QTDIR USERNAME LANG LC_ADDRESS LC_CTYPE\", secure_path=/sbin\\:/bin\\:/usr/sbin\\:/usr/bin\n");
                if (mine.Count == 0 && me.Uid != 0) { io.L("User " + me.User + " is not allowed to run sudo on " + m.ShortHost + "."); return 1; }
                io.L("User " + me.User + " may run the following commands on " + m.ShortHost + ":");
                foreach (var r in (me.Uid == 0 ? rules.Where(r => r.Who == "root").ToList() : mine)) io.L("    (" + r.RunAs + ") " + (r.NoPasswd ? "NOPASSWD: " : "") + string.Join(", ", r.Cmds.ToArray()));
                return 0;
            }
            // what to run
            List<string> cmd = o.Args;
            bool loginShell = o.Has("i"), shellMode = o.Has("s");
            if (cmd.Count == 0 && !loginShell && !shellMode && !o.Has("v")) { io.E("usage: sudo -h | -K | -k | -V\nusage: sudo -v [-ABknS] [-g group] [-h host] [-p prompt] [-u user]\nusage: sudo -l [-ABknS] [-g group] [-h host] [-p prompt] [-U user] [-u user] [command]\nusage: sudo [-ABbEHknPS] [-r role] [-t type] [-C num] [-D directory] [-g group] [-h host] [-p prompt] [-R directory] [-T timeout] [-u user] [VAR=value] [-i|-s] [<command>]"); return 1; }
            string path = null, args = "";
            if (cmd.Count > 0)
            {
                string shown;
                var save = sh.GetVar("PATH");
                sh.SetVar("PATH", "/sbin:/bin:/usr/sbin:/usr/bin");
                var l = sh.FindProgram(cmd[0], out shown);
                sh.SetVar("PATH", save);
                if (l == null && !Sh.Builtins.ContainsKey(cmd[0])) { io.E("sudo: " + cmd[0] + ": command not found"); return 1; }
                path = l != null ? (shown.StartsWith("/", StringComparison.Ordinal) ? shown : l.Path) : cmd[0];
                args = string.Join(" ", cmd.Skip(1).ToArray());
            }
            else path = tpw.Shell;
            if (me.Uid != 0)
            {
                bool allowed = mine.Any(r => (r.RunAs == "ALL" || r.RunAs.Split(':')[0].Split(',').Any(x => x.Trim() == target || x.Trim() == "ALL") || (r.RunAs == "root" && target == "root")) && SudoCmdMatches(r, path, args));
                if (mine.Count == 0)
                {
                    if (!SudoAuth(sh, io, mine, false, o.Has("n"))) return 1;
                    io.E(me.User + " is not in the sudoers file.  This incident has been reported to the administrator.");
                    m.Log("sudo", sh.Pid, 2, me.User + " : user NOT in sudoers ; TTY=" + (sh.S == null ? "pts/0" : sh.S.Tty) + " ; PWD=" + sh.P.Cwd + " ; USER=root ; COMMAND=" + path + (args.Length > 0 ? " " + args : ""), null);
                    return 1;
                }
                bool nopw = mine.Any(r => r.NoPasswd && SudoCmdMatches(r, path, args));
                if (!nopw && !SudoAuth(sh, io, mine, false, o.Has("n"))) return 1;
                if (!allowed)
                {
                    io.E("Sorry, user " + me.User + " is not allowed to execute '" + path + (args.Length > 0 ? " " + args : "") + "' as " + target + " on " + m.HostnameNow + ".");
                    m.Log("sudo", sh.Pid, 2, me.User + " : command not allowed ; TTY=" + (sh.S == null ? "pts/0" : sh.S.Tty) + " ; PWD=" + sh.P.Cwd + " ; USER=" + target + " ; COMMAND=" + path + (args.Length > 0 ? " " + args : ""), null);
                    return 1;
                }
            }
            if (o.Has("v")) return 0;
            m.Log("sudo", sh.Pid, 5, "  " + me.User + " : TTY=" + (sh.S == null ? "pts/0" : sh.S.Tty) + " ; PWD=" + sh.P.Cwd + " ; USER=" + target + " ; COMMAND=" + path + (args.Length > 0 ? " " + args : ""), null);
            if (m.Net != null && m.Net.Range != null && me.Uid != 0) m.Net.Range.Privilege(m, me.User, "sudo " + path);   // Cyber Range: a defender may watch for this
            var ctx = new PCtx { Cr = m.CredFor(tpw.Name), Root = sh.P.Root, Cwd = loginShell ? tpw.Home : sh.P.Cwd, Umask = sh.P.Umask, Domain = sh.P.Domain };
            var child = new Sh(m, sh.S, ctx) { Host = sh.Host };
            if (loginShell) { child.InitLogin(tpw); child.LoginShell = true; }
            else
            {
                foreach (var kv in sh.Vars) if (kv.Value.Export || o.Has("E")) child.Vars[kv.Key] = kv.Value.Copy();
                child.SetVar("PATH", "/sbin:/bin:/usr/sbin:/usr/bin"); child.SetVar("HOME", tpw.Home); child.SetVar("USER", tpw.Name); child.SetVar("LOGNAME", tpw.Name);
                child.SetVar("SUDO_USER", me.User); child.SetVar("SUDO_UID", me.Uid.ToString()); child.SetVar("SUDO_COMMAND", path + (args.Length > 0 ? " " + args : ""));
                child.SetVar("PS1", sh.GetVar("PS1") ?? "[\\u@\\h \\W]\\$ ");
                foreach (var k in new[] { "PATH", "HOME", "USER", "LOGNAME", "SUDO_USER", "SUDO_UID", "SUDO_COMMAND" }) child.GetVarObj(k).Export = true;
            }
            child.Io = io;
            int st;
            if (cmd.Count > 0)
            {
                child.Interactive = false;
                try { st = loginShell || shellMode ? child.RunText(string.Join(" ", cmd.Select(Sh.Quote).ToArray()), io) : child.Run(cmd, io); }
                catch (ExitEx e) { st = e.Code; }
                if (o.Has("e")) { }
                Steps(sh, child);
                return st;
            }
            if (sh.S == null) return 0;
            sh.S.ShStack.Add(child);
            try { st = child.Repl(); }
            finally { sh.S.ShStack.Remove(child); child.SaveHistory(); }
            return st;
        }
        static void Steps(Sh a, Sh b) { a.Steps = Math.Max(a.Steps, b.Steps); }

        static bool SudoAuth(Sh sh, Io io, List<SudoRule> mine, bool listOnly, bool nonInteractive = false)
        {
            if (sh.P.Cr.Uid == 0) return true;
            if (sh.S != null && sh.S.SudoUntil > sh.M.Now && sh.S.SudoUser == sh.P.Cr.User) return true;
            if (nonInteractive) { io.E("sudo: a password is required"); return false; }
            if (sh.S == null) { io.E("sudo: a terminal is required to read the password; either use the -S option to read from standard input or configure an askpass helper\nsudo: a password is required"); return false; }
            for (int i = 0; i < 3; i++)
            {
                string p = ReadSecret(sh, io, "[sudo] password for " + sh.P.Cr.User + ": ");
                if (p == null) { io.E("sudo: a password is required"); return false; }
                if (sh.M.CheckPassword(sh.P.Cr.User, p)) { sh.S.SudoUntil = sh.M.Now + 300; sh.S.SudoUser = sh.P.Cr.User; return true; }
                if (i < 2) io.E("Sorry, try again.");
            }
            io.E("sudo: 3 incorrect password attempts");
            sh.M.Log("sudo", sh.Pid, 2, sh.P.Cr.User + " : 3 incorrect password attempts ; TTY=" + sh.S.Tty + " ; PWD=" + sh.P.Cwd + " ; USER=root", null);
            return false;
        }

        // ------------------------------------------------------------ getent / who / w / last
        static int Getent(Sh sh, List<string> a, Io io)
        {
            if (a.Count < 2) { io.E("Usage: getent [OPTION...] database [key ...]"); return 1; }
            var m = sh.M;
            string db = a[1];
            var keys = a.Skip(2).ToList();
            List<string> lines;
            switch (db)
            {
                case "passwd": lines = Lines(m.V.Cat("/etc/passwd") ?? ""); break;
                case "group": lines = Lines(m.V.Cat("/etc/group") ?? ""); break;
                case "shadow": if (sh.P.Cr.Uid != 0) return 2; lines = Lines(m.V.Cat("/etc/shadow") ?? ""); break;
                case "gshadow": if (sh.P.Cr.Uid != 0) return 2; lines = Lines(m.V.Cat("/etc/gshadow") ?? ""); break;
                case "hosts": case "ahosts":
                    {
                        if (keys.Count == 0) { foreach (var l in Lines(m.V.Cat("/etc/hosts") ?? "")) if (l.Trim().Length > 0 && !l.StartsWith("#", StringComparison.Ordinal)) io.L(Regex.Replace(l.Trim(), "\\s+", "       ")); return 0; }
                        int st = 0;
                        foreach (var k in keys) { var ip = m.Net.Resolve(m, k); if (ip == null) { st = 2; continue; } io.L(ip.PadRight(15) + " " + k); }
                        return st;
                    }
                case "services": lines = new List<string> { "ssh                   22/tcp", "http                  80/tcp", "https                 443/tcp", "nfs                   2049/tcp", "ntp                   123/udp", "domain                53/udp" }; break;
                default: io.E("Unknown database: " + db + "\nTry `getent --help' or `getent --usage' for more information."); return 1;
            }
            if (keys.Count == 0) { foreach (var l in lines) io.L(l); return 0; }
            int status = 0;
            foreach (var k in keys)
            {
                var hit = lines.FirstOrDefault(l => l.StartsWith(k + ":", StringComparison.Ordinal) || (l.Split(':').Length > 2 && l.Split(':')[2] == k) || l.StartsWith(k + " ", StringComparison.Ordinal));
                if (hit == null) status = 2; else io.L(hit);
            }
            return status;
        }
        static int Who(Sh sh, List<string> a, Io io)
        {
            var m = sh.M;
            if (a.Contains("-b")) { var d = m.LocalTime(m.BootTime); io.L("         system boot  " + d.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)); return 0; }
            if (a.Contains("-r")) { io.L("         run-level 3  " + m.LocalTime(m.BootTime).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)); return 0; }
            if (a.Contains("am") || a.Contains("-m")) { io.L(sh.P.Cr.User.PadRight(8) + " " + (sh.S == null ? "pts/0" : sh.S.Tty).PadRight(12) + " " + m.LocalTime(m.Now).ToString("yyyy-MM-dd HH:mm")); return 0; }
            foreach (var l in m.Logins)
            {
                var p = l.Split(' ');
                long t; long.TryParse(p.Length > 2 ? p[2] : "0", out t);
                io.L(p[0].PadRight(8) + " " + p[1].PadRight(12) + " " + m.LocalTime(t).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + (p.Length > 3 ? " (" + p[3] + ")" : ""));
            }
            return 0;
        }
        static int W(Sh sh, List<string> a, Io io)
        {
            var m = sh.M;
            long up = Math.Max(1, m.Now - m.BootTime);
            io.L(" " + m.LocalTime(m.Now).ToString("HH:mm:ss") + " up " + Uptime(up) + ",  " + m.Logins.Count + " user" + (m.Logins.Count == 1 ? "" : "s") + ",  load average: " + m.LoadAvg().Replace(" ", ", "));
            io.L("USER     TTY        LOGIN@   IDLE   JCPU   PCPU WHAT");
            foreach (var l in m.Logins) { var p = l.Split(' '); long t; long.TryParse(p[2], out t); io.L(p[0].PadRight(8) + " " + p[1].PadRight(10) + " " + m.LocalTime(t).ToString("HH:mm").PadRight(8) + " 0.00s  0.02s  0.00s -bash"); }
            return 0;
        }
        public static string Uptime(long secs)
        {
            long d = secs / 86400, h = secs % 86400 / 3600, mi = secs % 3600 / 60;
            string s = d > 0 ? d + " day" + (d == 1 ? "" : "s") + ", " : "";
            s += h > 0 ? h + ":" + mi.ToString("00") : mi + " min";
            return s;
        }
        static int Last(Sh sh, List<string> a, Io io)
        {
            var m = sh.M;
            foreach (var l in m.Logins.AsEnumerable().Reverse()) { var p = l.Split(' '); long t; long.TryParse(p[2], out t); io.L(p[0].PadRight(8) + " " + p[1].PadRight(12) + " " + (p[1].StartsWith("pts", StringComparison.Ordinal) ? "192.168.50.12" : "").PadRight(16) + " " + m.LocalTime(t).ToString("ddd MMM dd HH:mm", CultureInfo.InvariantCulture) + "   still logged in"); }
            io.L("reboot   system boot  " + m.KernelRelease.PadRight(16) + " " + m.LocalTime(m.BootTime).ToString("ddd MMM dd HH:mm", CultureInfo.InvariantCulture) + "   still running");
            io.L("\nwtmp begins " + DateText(m, m.BootTime - 3600));
            return 0;
        }
        static int Lastlog(Sh sh, List<string> a, Io io)
        {
            io.L("Username         Port     From                                       Latest");
            foreach (var p in sh.M.Passwd())
            {
                var hit = sh.M.Logins.FirstOrDefault(x => x.StartsWith(p.Name + " ", StringComparison.Ordinal));
                io.L(p.Name.PadRight(16) + " " + (hit != null ? hit.Split(' ')[1].PadRight(8) + " " + "".PadRight(42) + DateText(sh.M, sh.M.Now) : "         **Never logged in**"));
            }
            return 0;
        }
    }

    public partial class Machine
    {
        /// <summary>Changes a password as passwd does: a new /etc/shadow; without a loaded policy it has no label.</summary>
        public void SetPasswordAt(string root, string user, string pw, bool noPolicy)
        {
            string path = (root == "/" ? "" : root) + "/etc/shadow";
            var l = V.G(path);
            if (!l.Exists) return;
            if (l.Mnt != null && l.Mnt.Ro) throw new VfsEx("EROFS");
            var lines = (l.Node.Text ?? "").Split('\n').ToList();
            bool found = false;
            for (int i = 0; i < lines.Count; i++)
            {
                var p = lines[i].Split(':');
                if (p.Length < 2 || p[0] != user) continue;
                p[1] = HashPassword(NewSalt(), pw);
                if (p.Length > 2) p[2] = DaysNow.ToString(CultureInfo.InvariantCulture);
                lines[i] = string.Join(":", p);
                found = true;
            }
            if (!found) lines.Insert(lines.Count > 0 && lines[lines.Count - 1].Length == 0 ? lines.Count - 1 : lines.Count, user + ":" + HashPassword(NewSalt(), pw) + ":" + DaysNow + ":0:99999:7:::");
            string text = string.Join("\n", lines.ToArray());
            // a new file replaces the old one
            var parent = l.Parent; var fs = l.PFs;
            var n = fs.NewNode('f', l.Node.Mode, l.Node.Uid, l.Node.Gid, Now);
            n.Text = text;
            n.Ctx = noPolicy ? "system_u:object_r:unlabeled_t:s0" : l.Node.Ctx;
            fs.Nodes.Remove(l.Node.Ino);
            parent.Kids[l.Name] = n.Ino;
        }
    }
}
