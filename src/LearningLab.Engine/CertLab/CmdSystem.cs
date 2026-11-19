// Cert Lab - system programs: date, hostname, hostnamectl, timedatectl, uptime, uname, reboot / poweroff /
// shutdown, ps, top, kill, pkill, pgrep, nice, renice, free, lscpu, sysctl, tuned-adm, chronyc, logger, dmesg,
// crontab, at / atq / atrm, grubby, grub2-mkconfig, load_policy, chroot, env, sleep, which, man, info.
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
        static void RegSystem()
        {
            P("systemctl", Systemctl); P("journalctl", Journalctl); P("loginctl", Loginctl);
            P("date", Date); P("hostname", Hostname); P("hostnamectl", Hostnamectl); P("timedatectl", Timedatectl); P("uptime", UptimeCmd); P("uname", Uname);
            P("reboot", (sh, a, io) => PowerCmd(sh, a, io, true)); P("poweroff", (sh, a, io) => PowerCmd(sh, a, io, false)); P("halt", (sh, a, io) => PowerCmd(sh, a, io, false));
            P("shutdown", Shutdown); P("init", Init); P("telinit", Init); P("runlevel", (sh, a, io) => { io.L("N " + (sh.M.Target == "graphical.target" ? 5 : sh.M.Target == "rescue.target" ? 1 : 3)); return 0; });
            P("ps", Ps); P("top", Top); P("kill", Kill); P("killall", Killall); P("pkill", Pkill); P("pgrep", Pgrep); P("pidof", Pidof); P("nice", Nice); P("renice", Renice);
            P("free", Free); P("lscpu", Lscpu); P("sysctl", Sysctl); P("tuned-adm", TunedAdm); P("chronyc", Chronyc); P("logger", Logger); P("dmesg", Dmesg);
            P("systemd-analyze", (sh, a, io) => { if (a.Contains("blame")) { foreach (var s in sh.M.Services.Values.Take(15)) io.L(((s.Unit.Length * 37) % 900 + 50).ToString().PadLeft(6) + "ms " + s.Unit); return 0; } io.L("Startup finished in 1.402s (kernel) + 3.218s (initrd) + 9.874s (userspace) = 14.495s \nmulti-user.target reached after 9.861s in userspace."); return 0; });
            P("systemd-run", (sh, a, io) => { io.L("Running as unit: run-u" + sh.M.NextPid + ".service"); return 0; });
            P("systemd-cat", (sh, a, io) => { foreach (var l in Lines(io.In.ReadAll())) sh.M.Log("unknown", sh.Pid, 6, l, null); return 0; });
            P("crontab", Crontab); P("at", At); P("batch", At); P("atq", Atq); P("atrm", Atrm); P("run-parts", (sh, a, io) => { if (a.Count > 1) sh.M.RunParts(a[a.Count - 1]); return 0; });
            P("grubby", Grubby); P("grub2-mkconfig", Grub2Mkconfig); P("grub2-set-default", Grub2SetDefault); P("grub2-editenv", Grub2Editenv);
            P("grub2-install", (sh, a, io) => { io.L("Installing for i386-pc platform.\nInstallation finished. No error reported."); return 0; }); P("grub2-probe", (sh, a, io) => { io.L("xfs"); return 0; });
            P("load_policy", (sh, a, io) => { if (sh.P.Cr.Uid != 0) { io.E("load_policy:  Can't load policy:  Permission denied"); return 1; } sh.P.Domain = "unconfined_t"; return 0; });
            P("chroot", Chroot);
            P("env", EnvCmd); P("printenv", Printenv); P("sleep", Sleep); P("which", Which); P("clear", (sh, a, io) => { if (sh.S != null) sh.S.ClearRequested = true; return 0; }); P("reset", (sh, a, io) => 0);
            P("tput", (sh, a, io) => { if (a.Count > 1 && a[1] == "cols") io.L("80"); else if (a.Count > 1 && a[1] == "lines") io.L("24"); return 0; });
            P("localectl", (sh, a, io) => { if (a.Count > 1 && a[1] == "list-locales") { io.L("C.utf8\nen_US.utf8"); return 0; } io.L("   System Locale: LANG=en_US.UTF-8\n       VC Keymap: us\n      X11 Layout: us"); return 0; });
            P("man", Man.Cmd); P("whatis", Man.Whatis); P("apropos", (sh, a, io) => { var b = new List<string> { "man", "-k" }; b.AddRange(a.Skip(1)); return Man.Cmd(sh, b, io); }); P("info", Man.Info); P("mandb", (sh, a, io) => { io.L("Processing manual pages under /usr/share/man...\nUpdating index cache for path `/usr/share/man/man1'. Wait...done.\nChecking for stray cats under /usr/share/man...\n" + Man.Count + " man subdirectories contained newer manual pages.\n" + Man.Count + " manual pages were added."); return 0; });
            P("bash", BashCmd); P("sh", BashCmd);
            P("tty", (sh, a, io) => { io.L(sh.S == null ? "not a tty" : "/dev/" + sh.S.Tty); return sh.S == null ? 1 : 0; });
            P("stty", (sh, a, io) => 0); P("mesg", (sh, a, io) => 0); P("wall", (sh, a, io) => { string t = a.Count > 1 ? string.Join(" ", a.Skip(1).ToArray()) : io.In.ReadAll(); foreach (var s in sh.M.Sessions) s.Output("\nBroadcast message from " + sh.P.Cr.User + "@" + sh.M.HostnameNow + " (" + (sh.S == null ? "?" : sh.S.Tty) + ") (" + DateText(sh.M, sh.M.Now) + "):\n\n" + t.TrimEnd() + "\n\n"); return 0; });
            P("nohup", (sh, a, io) => { if (a.Count < 2) { io.E("nohup: missing operand"); return 125; } return sh.Run(a.Skip(1).ToList(), io); });
            P("timeout", TimeoutCmd);
            P("watch", (sh, a, io) => { var b = a.Skip(1).Where(x => !x.StartsWith("-", StringComparison.Ordinal) && !Regex.IsMatch(x, "^\\d+$")).ToList(); io.L("Every 2.0s: " + string.Join(" ", b.ToArray()) + "                    " + sh.M.HostnameNow + ": " + DateText(sh.M, sh.M.Now) + "\n"); sh.Subshell().RunText(string.Join(" ", b.ToArray()), io); if (sh.S != null && io.Out.IsTty) { try { sh.S.WaitForBreak(); } catch (BreakSignal) { return 0; } } return 0; });
            P("lsmod", (sh, a, io) => { io.L("Module                  Size  Used by\nxfs                  2408448  3\nnft_fib_inet           16384  1\nnf_tables             331776  233 nft_ct,nft_reject_inet,nft_fib_ipv6,nft_fib_ipv4\nvirtio_net             73728  0\nvirtio_blk             24576  4"); return 0; });
            P("modprobe", (sh, a, io) => 0); P("modinfo", (sh, a, io) => { io.L("filename:       /lib/modules/" + sh.M.KernelRelease + "/kernel/fs/" + (a.Count > 1 ? a[1] : "xfs") + "\nlicense:        GPL"); return 0; });
            P("lspci", (sh, a, io) => { io.L("00:00.0 Host bridge: Intel Corporation 82G33/G31/P35/P31 Express DRAM Controller\n00:01.0 VGA compatible controller: Red Hat, Inc. Virtio 1.0 GPU (rev 01)\n01:00.0 Ethernet controller: Red Hat, Inc. Virtio 1.0 network device (rev 01)\n04:00.0 SCSI storage controller: Red Hat, Inc. Virtio 1.0 block device (rev 01)"); return 0; });
            P("subscription-manager", (sh, a, io) => { string v = a.Count > 1 ? a[1] : ""; if (v == "status") { io.L("+-------------------------------------------+\n   System Status Details\n+-------------------------------------------+\nOverall Status: Disabled\nContent Access Mode is set to Simple Content Access. This host has access to content, regardless of subscription status.\n\nSystem Purpose Status: Disabled\n\n(Cert Lab: this practice system has no Red Hat subscription; it uses the local repositories on repo.lab.example.com or the installation DVD.)"); return 0; } if (v == "repos") { io.L("This system has no repositories available through subscriptions."); return 0; } io.L("This system is not registered with an entitlement server. You can use \"rhc\" or \"subscription-manager\" to register.\n(Cert Lab practice machines use local repositories instead.)"); return 0; });
        }
        static void RegMisc() { P("certlab-check", CertlabCheck); RegSec(); RegRange(); }

        /// <summary>The graders' helper: things a check script cannot easily ask the shell (a password, a login's umask...).</summary>
        static int CertlabCheck(Sh sh, List<string> a, Io io)
        {
            var m = sh.M;
            if (sh.P.Cr.Uid != 0) { io.E("certlab-check: only the graders (root) use this"); return 2; }
            string what = a.Count > 1 ? a[1] : "--help";
            switch (what)
            {
                case "password":
                    // certlab-check password USER PASSWORD: would this password log the user in?
                    if (a.Count < 4) return 2;
                    return m.Authenticate(a[2], a[3]) ? 0 : 1;
                case "booted":
                    // the machine finished booting normally (not emergency / rescue mode)
                    return m.State == "running" ? 0 : 1;
                case "target":
                    io.L(m.Target); return 0;
                case "login":
                    {
                        // certlab-check login USER 'command': runs the command in USER's login shell (aliases, umask, PATH)
                        if (a.Count < 4) return 2;
                        var pw = m.User(a[2]);
                        if (pw == null) return 1;
                        var p = new PCtx { Cr = m.CredFor(pw.Name), Cwd = pw.Home, Root = "/" };
                        try { if (!m.V.G(pw.Home).Exists) p.Cwd = "/"; } catch (VfsEx) { p.Cwd = "/"; }
                        var ls = new Sh(m, null, p) { LoginShell = true };
                        ls.InitLogin(pw);
                        ls.Interactive = true;
                        // a login session has the user's systemd manager (systemctl --user)
                        if (m.ServiceActive("user@" + pw.Uid + ".service")) { ls.SetVar("XDG_RUNTIME_DIR", "/run/user/" + pw.Uid); ls.GetVarObj("XDG_RUNTIME_DIR").Export = true; }
                        try { return ls.RunText(a[3], io); } catch (ExitEx e) { return e.Code; }
                    }
                case "sshkey":
                    {
                        // certlab-check sshkey FROMUSER [TOUSER@]HOST: a key login without any password works
                        if (a.Count < 4) return 2;
                        var pw = m.User(a[2]);
                        if (pw == null) return 1;
                        var us = new Sh(m, null, new PCtx { Cr = m.CredFor(pw.Name), Cwd = pw.Home, Root = "/" });
                        us.SetVar("HOME", pw.Home);
                        var so = new SshOpts { Batch = true, NoPassword = true, Quiet = true };
                        SplitUserHost(a[3], so);
                        var buf = new BufSink();
                        var lg = SshLoginTo(us, new Io { In = new StrSrc(""), Out = buf, Err = buf }, so, "ssh");
                        if (lg == null) { io.P(buf.Text); return 1; }
                        return 0;
                    }
                case "umask":
                    {
                        if (a.Count < 3) return 2;
                        var pw = m.User(a[2]);
                        if (pw == null) return 1;
                        var ls = new Sh(m, null, new PCtx { Cr = m.CredFor(pw.Name), Cwd = "/", Root = "/" }) { LoginShell = true };
                        ls.InitLogin(pw);
                        io.L(Convert.ToString(ls.P.Umask, 8).PadLeft(4, '0'));
                        return 0;
                    }
            }
            io.L("certlab-check password USER PASS | booted | target | login USER 'command' | sshkey FROMUSER [USER@]HOST | umask USER");
            return what == "--help" ? 0 : 2;
        }

        // ------------------------------------------------------------ date / time
        public static string DateFormat(Machine m, long t, string fmt, bool utc)
        {
            string zone = utc ? "UTC" : m.TimeZoneName;
            var d = utc ? new DateTime(1970, 1, 1).AddSeconds(t) : m.LocalTime(t);
            var sb = new StringBuilder();
            for (int i = 0; i < fmt.Length; i++)
            {
                char c = fmt[i];
                if (c != '%' || i + 1 >= fmt.Length) { sb.Append(c); continue; }
                char f = fmt[++i];
                bool noPad = false;
                if (f == '-' && i + 1 < fmt.Length) { noPad = true; f = fmt[++i]; }
                Func<int, int, string> pad = (v, w) => noPad ? v.ToString() : v.ToString().PadLeft(w, '0');
                var ci = CultureInfo.InvariantCulture;
                switch (f)
                {
                    case 'Y': sb.Append(d.Year); break;
                    case 'y': sb.Append(pad(d.Year % 100, 2)); break;
                    case 'm': sb.Append(pad(d.Month, 2)); break;
                    case 'd': sb.Append(pad(d.Day, 2)); break;
                    case 'e': sb.Append(d.Day.ToString().PadLeft(2)); break;
                    case 'H': sb.Append(pad(d.Hour, 2)); break;
                    case 'k': sb.Append(d.Hour.ToString().PadLeft(2)); break;
                    case 'I': sb.Append(pad(d.Hour % 12 == 0 ? 12 : d.Hour % 12, 2)); break;
                    case 'l': sb.Append((d.Hour % 12 == 0 ? 12 : d.Hour % 12).ToString().PadLeft(2)); break;
                    case 'M': sb.Append(pad(d.Minute, 2)); break;
                    case 'S': sb.Append(pad(d.Second, 2)); break;
                    case 'N': sb.Append("000000000"); break;
                    case 'p': sb.Append(d.Hour < 12 ? "AM" : "PM"); break;
                    case 'P': sb.Append(d.Hour < 12 ? "am" : "pm"); break;
                    case 'a': sb.Append(d.ToString("ddd", ci)); break;
                    case 'A': sb.Append(d.ToString("dddd", ci)); break;
                    case 'b': case 'h': sb.Append(d.ToString("MMM", ci)); break;
                    case 'B': sb.Append(d.ToString("MMMM", ci)); break;
                    case 'j': sb.Append(pad(d.DayOfYear, 3)); break;
                    case 'u': sb.Append(d.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)d.DayOfWeek); break;
                    case 'w': sb.Append((int)d.DayOfWeek); break;
                    case 's': sb.Append(t); break;
                    case 'F': sb.Append(d.ToString("yyyy-MM-dd", ci)); break;
                    case 'T': sb.Append(d.ToString("HH:mm:ss", ci)); break;
                    case 'R': sb.Append(d.ToString("HH:mm", ci)); break;
                    case 'D': sb.Append(d.ToString("MM/dd/yy", ci)); break;
                    case 'c': sb.Append(d.ToString("ddd dd MMM yyyy hh:mm:ss tt", ci) + " " + Tz.Abbrev(zone, t)); break;
                    case 'x': sb.Append(d.ToString("MM/dd/yyyy", ci)); break;
                    case 'X': sb.Append(d.ToString("hh:mm:ss tt", ci)); break;
                    case 'Z': sb.Append(Tz.Abbrev(zone, t)); break;
                    case 'z': sb.Append(Tz.OffsetText(zone, t)); break;
                    case 'U': sb.Append(pad((d.DayOfYear + 6 - (int)d.DayOfWeek) / 7, 2)); break;
                    case 'V': sb.Append(pad(CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(d, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday), 2)); break;
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case '%': sb.Append('%'); break;
                    default: sb.Append('%').Append(f); break;
                }
            }
            return sb.ToString();
        }

        static int Date(Sh sh, List<string> a, Io io)
        {
            var m = sh.M;
            bool utc = false; string fmt = null; long t = m.Now; string set = null;
            for (int i = 1; i < a.Count; i++)
            {
                string x = a[i];
                if (x == "-u" || x == "--utc" || x == "--universal") utc = true;
                else if ((x == "-d" || x == "--date") && i + 1 < a.Count) { if (!ParseDate(m, a[++i], out t)) { io.E("date: invalid date '" + a[i] + "'"); return 1; } }
                else if (x.StartsWith("--date=", StringComparison.Ordinal)) { if (!ParseDate(m, x.Substring(7), out t)) { io.E("date: invalid date '" + x.Substring(7) + "'"); return 1; } }
                else if ((x == "-s" || x == "--set") && i + 1 < a.Count) set = a[++i];
                else if (x == "-R" || x == "--rfc-email") fmt = "%a, %d %b %Y %H:%M:%S %z";
                else if (x == "-I" || x == "--iso-8601") fmt = "%F";
                else if (x.StartsWith("+", StringComparison.Ordinal)) fmt = x.Substring(1);
                else { io.E("date: invalid date '" + x + "'"); return 1; }
            }
            if (set != null)
            {
                if (sh.P.Cr.Uid != 0) { io.E("date: cannot set date: Operation not permitted"); return 1; }
                long nt; if (!ParseDate(m, set, out nt)) { io.E("date: invalid date '" + set + "'"); return 1; }
                if (nt > m.Now) m.Net.Advance(nt - m.Now);
                t = m.Now;
            }
            io.L(fmt != null ? DateFormat(m, t, fmt, utc) : DateFormat(m, t, "%a %b %e %I:%M:%S %p %Z %Y", utc));
            return 0;
        }

        static int Timedatectl(Sh sh, List<string> a, Io io)
        {
            var m = sh.M;
            string verb = a.Count > 1 ? a[1] : "status";
            switch (verb)
            {
                case "status":
                    {
                        string z = m.TimeZoneName;
                        io.L("               Local time: " + DateFormat(m, m.Now, "%a %Y-%m-%d %H:%M:%S %Z", false));
                        io.L("           Universal time: " + DateFormat(m, m.Now, "%a %Y-%m-%d %H:%M:%S UTC", true));
                        io.L("                 RTC time: " + DateFormat(m, m.Now, "%a %Y-%m-%d %H:%M:%S", true));
                        io.L("                Time zone: " + z + " (" + Tz.Abbrev(z, m.Now) + ", " + Tz.OffsetText(z, m.Now) + ")");
                        io.L("System clock synchronized: " + (m.NtpSynced ? "yes" : "no"));
                        io.L("              NTP service: " + (m.ServiceActive("chronyd.service") ? "active" : "inactive"));
                        io.L("          RTC in local TZ: no");
                        return 0;
                    }
                case "show": io.L("Timezone=" + m.TimeZoneName + "\nLocalRTC=no\nCanNTP=yes\nNTP=" + (m.ServiceActive("chronyd.service") ? "yes" : "no") + "\nNTPSynchronized=" + (m.NtpSynced ? "yes" : "no") + "\nTimeUSec=" + DateFormat(m, m.Now, "%a %Y-%m-%d %H:%M:%S %Z", false)); return 0;
                case "list-timezones": foreach (var z in Tz.Zones.Select(x => x[0]).Where(x => x.Contains("/") && !x.StartsWith("Etc", StringComparison.Ordinal)).OrderBy(x => x, StringComparer.Ordinal)) io.L(z); io.L("UTC"); return 0;
                case "set-timezone":
                    {
                        if (a.Count < 3) { io.E("Too few arguments."); return 1; }
                        if (sh.P.Cr.Uid != 0) { io.E("Failed to set time zone: Access denied"); return 1; }
                        if (Tz.Find(a[2]) == null) { io.E("Failed to set time zone: Invalid or not installed time zone '" + a[2] + "'"); return 1; }
                        m.V.PutLink("/etc/localtime", "../usr/share/zoneinfo/" + a[2]);
                        return 0;
                    }
                case "set-ntp":
                    {
                        if (a.Count < 3) { io.E("Too few arguments."); return 1; }
                        if (sh.P.Cr.Uid != 0) { io.E("Failed to set ntp: Access denied"); return 1; }
                        bool on = a[2] == "true" || a[2] == "yes" || a[2] == "1" || a[2] == "on";
                        string err;
                        if (on) { m.Enable("chronyd.service", null, out err); m.StartService("chronyd.service", null); }
                        else { m.Disable("chronyd.service", null); m.StopService("chronyd.service", null); }
                        return 0;
                    }
                case "set-time":
                    {
                        if (sh.P.Cr.Uid != 0) { io.E("Failed to set time: Access denied"); return 1; }
                        if (m.ServiceActive("chronyd.service")) { io.E("Failed to set time: Automatic time synchronization is enabled"); return 1; }
                        long t; if (a.Count < 3 || !ParseDate(m, a[2], out t)) { io.E("Failed to parse time specification '" + (a.Count > 2 ? a[2] : "") + "': Invalid argument"); return 1; }
                        if (t > m.Now) m.Net.Advance(t - m.Now);
                        return 0;
                    }
                case "set-local-rtc": return 0;
                case "timesync-status": io.E("Failed to query server: The name org.freedesktop.timesync1 was not provided by any .service files"); return 1;
            }
            io.E("Unknown command verb " + verb + ".");
            return 1;
        }

        static int Hostname(Sh sh, List<string> a, Io io)
        {
            var m = sh.M;
            var o = Opts.Parse(a, "sfIidAb", "short|s,fqdn|f,long|f,all-ip-addresses|I,ip-address|i,domain|d,all-fqdns|A,boot|b", "hostname");
            if (o.Error != null) return Fail(io, o);
            if (o.Has("I")) { io.L(string.Join(" ", m.Ifs.Where(x => x.Up).SelectMany(x => x.V4.Select(v => v.Split('/')[0]).Concat(x.V6.Where(v => !v.StartsWith("fe80", StringComparison.Ordinal)).Select(v => v.Split('/')[0]))).ToArray()) + " "); return 0; }
            if (o.Has("i")) { io.L(m.FirstV4() ?? "127.0.0.1"); return 0; }
            string h = m.TransientHostname.Length > 0 ? m.TransientHostname : m.HostnameNow;
            if (o.Has("s")) { io.L(h.Split('.')[0]); return 0; }
            if (o.Has("d")) { int i = h.IndexOf('.'); io.L(i > 0 ? h.Substring(i + 1) : ""); return 0; }
            if (o.Args.Count > 0)
            {
                if (sh.P.Cr.Uid != 0) { io.E("hostname: you must be root to change the host name"); return 1; }
                m.TransientHostname = o.Args[0]; m.Hostname = o.Args[0];
                return 0;
            }
            io.L(h);
            return 0;
        }

        static int Hostnamectl(Sh sh, List<string> a, Io io)
        {
            var m = sh.M;
            var o = Opts.Parse(a, "", "static,transient,pretty,json=", "hostnamectl");
            string verb = o.Args.Count > 0 ? o.Args[0] : "status";
            if (verb == "hostname" && o.Args.Count == 1) { io.L(m.HostnameNow); return 0; }
            if (verb == "set-hostname" || (verb == "hostname" && o.Args.Count > 1))
            {
                if (o.Args.Count < 2) { io.E("Too few arguments."); return 1; }
                if (sh.P.Cr.Uid != 0) { io.E("Could not set static hostname: Access denied"); return 1; }
                string n = o.Args[1];
                if (!Regex.IsMatch(n, "^[A-Za-z0-9][A-Za-z0-9.-]{0,252}$")) { io.E("Invalid static hostname '" + n + "'"); return 1; }
                if (!o.Has("transient")) m.SysWrite("/etc/hostname", n + "\n");
                if (!o.Has("static")) m.Hostname = n;
                m.TransientHostname = "";
                m.Log("systemd-hostnamed", m.NextPid, 6, "Changed static host name to '" + n + "'", "systemd-hostnamed.service");
                return 0;
            }
            if (verb == "status")
            {
                string st = (m.V.Cat("/etc/hostname") ?? "").Trim();
                if (o.Has("static")) { io.L(st); return 0; }
                io.L(" Static hostname: " + (st.Length > 0 ? st : "(unset)"));
                if (st != m.HostnameNow) io.L("Transient hostname: " + m.HostnameNow);
                io.L("       Icon name: computer-vm\n         Chassis: vm\n      Machine ID: " + m.MachineId + "\n         Boot ID: " + BootIdHex(m, m.BootId) + "\n  Virtualization: kvm\nOperating System: Red Hat Enterprise Linux 9.5 (Plow)\n     CPE OS Name: cpe:/o:redhat:enterprise_linux:9::baseos\n          Kernel: Linux " + m.KernelRelease + "\n    Architecture: x86-64\n Hardware Vendor: QEMU\n  Hardware Model: Standard PC _Q35 + ICH9, 2009_\nFirmware Version: 1.16.3-2.el9\n   Firmware Date: Tue 2014-04-01");
                return 0;
            }
            io.E("Unknown command verb " + verb + ".");
            return 1;
        }

        static int UptimeCmd(Sh sh, List<string> a, Io io)
        {
            var m = sh.M;
            long up = Math.Max(60, m.Now - m.BootTime);
            if (a.Contains("-p") || a.Contains("--pretty")) { long h = up / 3600, mi = up % 3600 / 60; io.L("up " + (h > 0 ? h + " hour" + (h == 1 ? "" : "s") + ", " : "") + mi + " minute" + (mi == 1 ? "" : "s")); return 0; }
            if (a.Contains("-s") || a.Contains("--since")) { io.L(DateFormat(m, m.BootTime, "%Y-%m-%d %H:%M:%S", false)); return 0; }
            int users = m.Logins.Select(x => x.Split(' ')[0]).Count();
            io.L(" " + DateFormat(m, m.Now, "%H:%M:%S", false) + " up " + Uptime(up) + ",  " + users + " user" + (users == 1 ? "" : "s") + ",  load average: " + m.LoadAvg().Replace(" ", ", "));
            return 0;
        }

        static int Uname(Sh sh, List<string> a, Io io)
        {
            var m = sh.M;
            var o = Opts.Parse(a, "asnrvmpio", "all|a,kernel-name|s,nodename|n,kernel-release|r,kernel-version|v,machine|m,processor|p,hardware-platform|i,operating-system|o", "uname");
            if (o.Error != null) return Fail(io, o);
            string ver = "#1 SMP PREEMPT_DYNAMIC Fri Nov 15 10:47:23 EST 2024";
            if (o.Has("a")) { io.L("Linux " + m.HostnameNow + " " + m.KernelRelease + " " + ver + " x86_64 x86_64 x86_64 GNU/Linux"); return 0; }
            var parts = new List<string>();
            if (o.Has("s") || o.V.Count == 0) parts.Add("Linux");
            if (o.Has("n")) parts.Add(m.HostnameNow);
            if (o.Has("r")) parts.Add(m.KernelRelease);
            if (o.Has("v")) parts.Add(ver);
            if (o.Has("m")) parts.Add("x86_64");
            if (o.Has("p")) parts.Add("x86_64");
            if (o.Has("i")) parts.Add("x86_64");
            if (o.Has("o")) parts.Add("GNU/Linux");
            io.L(string.Join(" ", parts.ToArray()));
            return 0;
        }

        // ------------------------------------------------------------ power
        static void Power(Sh sh, Io io, bool reboot)
        {
            var m = sh.M;
            m.Log("systemd-logind", 900, 6, "System is " + (reboot ? "rebooting" : "powering down") + ".", null);
            m.Shutdown(reboot);
        }
        static int PowerCmd(Sh sh, List<string> a, Io io, bool reboot)
        {
            var m = sh.M;
            if (sh.P.Cr.Uid != 0) { io.E("Call to " + (reboot ? "Reboot" : "PowerOff") + " failed: Interactive authentication required."); return 1; }
            if (m.State == "rdbreak" || m.State == "initbash")
            {
                // no systemd yet: only a forced reboot works
                if (!a.Contains("-f") && !a.Contains("--force")) { io.E("System has not been booted with systemd as init system (PID 1). Can't operate.\nFailed to connect to bus: Host is down\nUse " + a[0] + " -f to " + (reboot ? "reboot" : "power off") + " anyway."); return 1; }
            }
            Power(sh, io, reboot);
            return 0;
        }
        static int Shutdown(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "rhPHck", "reboot|r,halt|H,poweroff|P,no-wall", "shutdown");
            if (o.Error != null) return Fail(io, o);
            if (sh.P.Cr.Uid != 0) { io.E("Call to ScheduleShutdown failed: Interactive authentication required."); return 1; }
            if (o.Has("c")) { sh.M.ShutdownAt = -1; io.L(""); return 0; }
            string when = o.Args.Count > 0 ? o.Args[0] : "+1";
            bool reboot = o.Has("r");
            if (when == "now" || when == "+0") { Power(sh, io, reboot); return 0; }
            long t = sh.M.Now;
            var mm = Regex.Match(when, "^\\+(\\d+)$");
            if (mm.Success) t += long.Parse(mm.Groups[1].Value) * 60;
            else { long w; if (!ParseDate(sh.M, when, out w)) { io.E("Failed to parse time specification: " + when); return 1; } t = w < sh.M.Now ? w + 86400 : w; }
            sh.M.ShutdownAt = t; sh.M.ShutdownReboot = reboot;
            io.L((reboot ? "Reboot" : "Shutdown") + " scheduled for " + StatusDate(sh.M, t) + ", use 'shutdown -c' to cancel.");
            return 0;
        }
        static int Init(Sh sh, List<string> a, Io io)
        {
            if (a.Count < 2) { io.E("init: required argument missing."); return 1; }
            if (sh.P.Cr.Uid != 0) { io.E("Failed to talk to init daemon: Access denied"); return 1; }
            string err;
            switch (a[1])
            {
                case "0": Power(sh, io, false); return 0;
                case "6": Power(sh, io, true); return 0;
                case "1": case "s": case "S": sh.M.Isolate("rescue.target", out err); return 0;
                case "3": case "2": case "4": sh.M.Isolate("multi-user.target", out err); return 0;
                case "5": sh.M.Isolate("graphical.target", out err); return 0;
            }
            io.E("Unknown command verb " + a[1] + ".");
            return 1;
        }

        // ------------------------------------------------------------ processes
        class PsRow { public ProcEntry P; public bool Self; }
        static List<ProcEntry> AllProcs(Sh sh)
        {
            var m = sh.M;
            var l = new List<ProcEntry>(m.Procs);
            // the shells on this machine's terminals
            foreach (var mm in m.Net.Machines)
                foreach (var s in mm.Sessions)
                    foreach (var x in s.ShStack)
                        if (x.M == m && l.All(p => p.Pid != x.Pid))
                            l.Add(new ProcEntry { Pid = x.Pid, PPid = 1, Uid = x.P.Cr.Uid, User = x.P.Cr.User, Cmd = x.LoginShell ? "-bash" : "bash", Comm = "bash", Tty = s == sh.S ? s.Tty : (s.M == m ? s.Tty : "pts/1"), Start = m.BootTime, Domain = "unconfined_r:unconfined_t", Rss = 5200, Vsz = 224000 });
            l.Add(new ProcEntry { Pid = sh.M.NextPid + 1, PPid = sh.Pid, Uid = sh.P.Cr.Uid, User = sh.P.Cr.User, Cmd = "ps", Comm = "ps", Tty = sh.S == null ? "?" : sh.S.Tty, Start = m.Now, Domain = "unconfined_r:unconfined_t", Rss = 3700, Vsz = 225000, State = "R" });
            return l.OrderBy(p => p.Pid).ToList();
        }
        static string PsField(Machine m, ProcEntry p, string f)
        {
            switch (f)
            {
                case "pid": return p.Pid.ToString();
                case "ppid": return p.PPid.ToString();
                case "user": case "euser": case "ruser": return p.User.Length > 8 ? p.User.Substring(0, 7) + "+" : p.User;
                case "uid": case "euid": return p.Uid.ToString();
                case "group": return p.Group;
                case "ni": case "nice": return p.Nice.ToString();
                case "pri": return (19 - p.Nice).ToString();
                case "priority": case "prio": return p.Prio.ToString();
                case "%cpu": case "pcpu": case "c": return f == "c" ? ((int)p.Cpu).ToString() : p.Cpu.ToString("0.0", CultureInfo.InvariantCulture);
                case "%mem": case "pmem": return (p.Rss * 100.0 / m.MemTotalKb).ToString("0.0", CultureInfo.InvariantCulture);
                case "rss": case "rssize": return p.Rss.ToString();
                case "vsz": case "vsize": return p.Vsz.ToString();
                case "tty": case "tt": return p.Tty;
                case "stat": case "s": case "state": return p.State + (p.Pid == 1 ? "s" : "") + (p.Nice > 0 ? "N" : p.Nice < 0 ? "<" : "");
                case "bsdtime": { long secs = (long)(p.Cpu / 100.0 * Math.Max(0, m.Now - p.Start)); return (secs / 60) + ":" + (secs % 60).ToString("00"); }
                case "time": case "cputime": { long secs = (long)(p.Cpu / 100.0 * Math.Max(0, m.Now - p.Start)); return (secs / 3600).ToString("00") + ":" + (secs % 3600 / 60).ToString("00") + ":" + (secs % 60).ToString("00"); }
                case "start": case "stime": case "start_time": return DateFormat(m, p.Start, m.Now - p.Start < 86400 ? "%H:%M" : "%b%d", false);
                case "lstart": return DateFormat(m, p.Start, "%a %b %e %H:%M:%S %Y", false);
                case "etime": { long s = m.Now - p.Start; return (s / 60).ToString("00") + ":" + (s % 60).ToString("00"); }
                case "etimes": return (m.Now - p.Start).ToString();
                case "comm": case "ucomm": case "ucmd": return p.Comm;
                case "cmd": case "args": case "command": return p.Cmd;
                case "label": case "context": return "system_u:" + p.Domain + ":s0" + (p.Domain.Contains("unconfined_t") ? "-s0:c0.c1023" : "");
                case "unit": return p.Unit.Length > 0 ? p.Unit : "-";
            }
            return "-";
        }
        static string PsHeader(string f)
        {
            switch (f)
            {
                case "pid": return "PID"; case "ppid": return "PPID"; case "user": case "euser": return "USER"; case "ruser": return "RUSER"; case "uid": case "euid": return "UID";
                case "ni": case "nice": return "NI"; case "pri": return "PRI"; case "priority": case "prio": return "PRI"; case "%cpu": case "pcpu": return "%CPU"; case "%mem": case "pmem": return "%MEM";
                case "rss": case "rssize": return "RSS"; case "vsz": case "vsize": return "VSZ"; case "tty": case "tt": return "TT"; case "stat": case "s": case "state": return "STAT";
                case "time": case "cputime": case "bsdtime": return "TIME"; case "start": case "start_time": return "START"; case "stime": return "STIME"; case "etime": return "ELAPSED"; case "etimes": return "ELAPSED";
                case "comm": case "ucomm": case "ucmd": return "COMMAND"; case "cmd": return "CMD"; case "args": case "command": return "COMMAND"; case "label": case "context": return "LABEL"; case "c": return "C"; case "group": return "GROUP"; case "lstart": return "STARTED"; case "unit": return "UNIT";
            }
            return f.ToUpperInvariant();
        }
        static bool PsRight(string f) { return f == "pid" || f == "ppid" || f == "ni" || f == "nice" || f == "pri" || f == "%cpu" || f == "pcpu" || f == "%mem" || f == "pmem" || f == "rss" || f == "vsz" || f == "uid" || f == "c" || f == "priority" || f == "etime" || f == "etimes" || f == "time"; }

        static int Ps(Sh sh, List<string> a, Io io)
        {
            var m = sh.M;
            // BSD style (ps aux) and Unix style (ps -ef)
            var b = new List<string>(a);
            bool bsd = b.Count > 1 && !b[1].StartsWith("-", StringComparison.Ordinal) && Regex.IsMatch(b[1], "^[auxlfwZejhr]+$");
            string bsdOpts = bsd ? b[1] : "";
            if (bsd) b.RemoveAt(1);
            var o = Opts.Parse(b, "efAaxlo:p:u:U:C:ZHjFwdgt:", "format=|o,sort=,pid=|p,user=|u,User=|U,forest,no-headers,headers,context|Z,ppid=", "ps");
            if (o.Error != null) return Fail(io, o);
            var all = AllProcs(sh);
            IEnumerable<ProcEntry> sel = all;
            bool everyone = o.Has("e") || o.Has("A") || bsdOpts.Contains("a") && bsdOpts.Contains("x") || bsdOpts.Contains("x") && bsdOpts.Contains("a");
            bool anyFilter = o.Has("p") || o.Has("u") || o.Has("U") || o.Has("C") || o.Has("ppid");
            if (!everyone && !anyFilter)
            {
                if (bsdOpts.Contains("a")) sel = sel.Where(p => p.Tty != "?");
                else if (bsdOpts.Contains("x")) sel = sel.Where(p => p.Uid == sh.P.Cr.Uid);
                else sel = sel.Where(p => sh.S != null && p.Tty == sh.S.Tty && p.Uid == sh.P.Cr.Uid);
            }
            if (o.Has("p")) { var ids = o.All("p").SelectMany(x => x.Split(',')).ToList(); sel = all.Where(p => ids.Contains(p.Pid.ToString())); }
            if (o.Has("ppid")) { var ids = o.All("ppid").SelectMany(x => x.Split(',')).ToList(); sel = all.Where(p => ids.Contains(p.PPid.ToString())); }
            if (o.Has("u") || o.Has("U")) { var us = o.All("u").Concat(o.All("U")).SelectMany(x => x.Split(',')).ToList(); sel = all.Where(p => us.Contains(p.User) || us.Contains(p.Uid.ToString())); }
            if (o.Has("C")) { var cs = o.All("C").SelectMany(x => x.Split(',')).ToList(); sel = all.Where(p => cs.Contains(p.Comm)); }
            var list = sel.ToList();
            List<string> fields;
            bool ctx = o.Has("Z") || bsdOpts.Contains("Z");
            if (o.Has("o")) fields = o.All("o").SelectMany(x => x.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)).ToList();
            else if (bsdOpts.Contains("u")) fields = new List<string> { "user", "pid", "%cpu", "%mem", "vsz", "rss", "tty=TTY", "stat", "start", "bsdtime=TIME", "command" };
            else if (bsdOpts.Contains("l") || o.Has("l")) fields = new List<string> { "f", "s=S", "uid", "pid", "ppid", "c", "pri", "ni", "addr", "sz", "wchan", "tty=TTY", "time", "cmd=CMD" };
            else if (o.Has("f") || o.Has("F")) fields = new List<string> { "uid_name", "pid", "ppid", "c", "stime", "tty=TTY", "time", "cmd=CMD" };
            else if (bsd) fields = new List<string> { "pid", "tty=TTY", "stat", "bsdtime=TIME", "command" };
            else fields = new List<string> { "pid", "tty=TTY", "time", "comm=CMD" };
            if (ctx && !o.Has("o")) fields.Insert(0, "label");
            // the header names that may carry a = (ps -o pid=,comm=)
            var heads = new List<string>(); var keys = new List<string>();
            foreach (var f in fields) { int eq = f.IndexOf('='); keys.Add(eq >= 0 ? f.Substring(0, eq) : f); heads.Add(eq >= 0 ? f.Substring(eq + 1) : null); }
            string sort = o.Get("sort");
            if (sort != null)
            {
                foreach (var s in sort.Split(',').Reverse())
                {
                    bool desc = s.StartsWith("-", StringComparison.Ordinal); string k = s.TrimStart('-', '+');
                    Func<ProcEntry, IComparable> key = p => k == "%cpu" || k == "pcpu" ? (IComparable)p.Cpu : k == "%mem" || k == "pmem" || k == "rss" ? (IComparable)p.Rss : k == "pid" ? (IComparable)p.Pid : k == "ni" || k == "nice" ? (IComparable)p.Nice : k == "start_time" || k == "start" ? (IComparable)p.Start : (IComparable)PsField(m, p, k);
                    list = desc ? list.OrderByDescending(key).ToList() : list.OrderBy(key).ToList();
                }
            }
            var rows = new List<string[]>();
            Func<ProcEntry, string, string> val = (p, f) =>
            {
                switch (f)
                {
                    case "uid_name": return p.User.Length > 8 ? p.User.Substring(0, 7) + "+" : p.User;
                    case "f": return "4"; case "addr": return "-"; case "sz": return (p.Vsz / 4).ToString(); case "wchan": return "do_wai";
                }
                return PsField(m, p, f);
            };
            bool noHead = o.Has("no-headers") || heads.Count > 0 && heads.All(h => h != null && h.Length == 0);
            var w = new int[keys.Count];
            var hdr = keys.Select((k, i) => heads[i] ?? (k == "uid_name" ? "UID" : k == "f" ? "F" : k == "addr" ? "ADDR" : k == "sz" ? "SZ" : k == "wchan" ? "WCHAN" : PsHeader(k))).ToArray();
            foreach (var p in list) rows.Add(keys.Select(k => val(p, k)).ToArray());
            for (int i = 0; i < keys.Count; i++) { w[i] = noHead ? 0 : hdr[i].Length; foreach (var r in rows) if (i < keys.Count - 1) w[i] = Math.Max(w[i], r[i].Length); }
            for (int i = 0; i < keys.Count; i++) if (keys[i] == "pid" || keys[i] == "ppid") w[i] = Math.Max(w[i], 7);
            Func<string[], string> fmt = r =>
            {
                var sb = new StringBuilder();
                for (int i = 0; i < r.Length; i++)
                {
                    if (i > 0) sb.Append(' ');
                    if (i == r.Length - 1) sb.Append(r[i]);
                    else sb.Append(PsRight(keys[i]) ? r[i].PadLeft(w[i]) : r[i].PadRight(w[i]));
                }
                return sb.ToString().TrimEnd();
            };
            if (!noHead) io.L(fmt(hdr));
            foreach (var r in rows) io.L(fmt(r));
            return list.Count == 0 && (o.Has("p") || o.Has("C")) ? 1 : 0;
        }

        static int Top(Sh sh, List<string> a, Io io)
        {
            var m = sh.M;
            var all = AllProcs(sh).OrderByDescending(p => p.Cpu).ThenBy(p => p.Pid).ToList();
            long tot = m.MemTotalKb, used = m.MemUsedKb;
            double cpu = Math.Min(100, m.Procs.Sum(p => p.Cpu) / 2);
            long swap = m.Swaps.Sum(s => s.Bytes / 1024);
            int n = 20;
            for (int i = 1; i < a.Count; i++) if (a[i] == "-n" && i + 1 < a.Count) i++;
            io.L("top - " + DateFormat(m, m.Now, "%H:%M:%S", false) + " up " + Uptime(Math.Max(60, m.Now - m.BootTime)) + ",  " + m.Logins.Count + " user,  load average: " + m.LoadAvg().Replace(" ", ", "));
            io.L("Tasks: " + (all.Count + 120) + " total,   " + Math.Max(1, all.Count(p => p.Cpu > 1)) + " running, " + (all.Count + 119) + " sleeping,   0 stopped,   0 zombie");
            io.L("%Cpu(s): " + cpu.ToString("0.0", CultureInfo.InvariantCulture).PadLeft(4) + " us,  0.3 sy,  0.0 ni, " + (99.7 - cpu).ToString("0.0", CultureInfo.InvariantCulture).PadLeft(4) + " id,  0.0 wa,  0.0 hi,  0.0 si,  0.0 st");
            io.L("MiB Mem :  " + (tot / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " total,  " + ((tot - used - 986000) / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " free,  " + (used / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " used,   " + (986000 / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " buff/cache");
            io.L("MiB Swap:  " + (swap / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " total,  " + (swap / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " free,      0.0 used.  " + ((tot - used) / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " avail Mem");
            io.L("");
            io.L("    PID USER      PR  NI    VIRT    RES    SHR S  %CPU  %MEM     TIME+ COMMAND");
            foreach (var p in all.Take(n))
                io.L(p.Pid.ToString().PadLeft(7) + " " + PsField(m, p, "user").PadRight(8) + "  " + (p.Prio).ToString().PadLeft(2) + " " + p.Nice.ToString().PadLeft(3) + " " + p.Vsz.ToString().PadLeft(7) + " " + p.Rss.ToString().PadLeft(6) + " " + (p.Rss / 2).ToString().PadLeft(6) + " " + (p.Cpu > 1 ? "R" : "S") + " " + p.Cpu.ToString("0.0", CultureInfo.InvariantCulture).PadLeft(5) + " " + (p.Rss * 100.0 / tot).ToString("0.0", CultureInfo.InvariantCulture).PadLeft(5) + "   " + "0:00.12".PadLeft(7) + " " + p.Comm);
            if (!a.Contains("-b") && sh.S != null && io.Out.IsTty)
            {
                io.L("\n(Cert Lab's top shows one screen; press Ctrl+C or type q and Enter to leave.)");
                try { for (;;) { var l = sh.S.ReadLine("", false); if (l == null || l.Trim() == "q") break; if (l.Trim() == "k") io.L("PID to signal/kill [default pid = " + all[0].Pid + "]: use the kill command in Cert Lab"); } } catch (BreakSignal) { }
            }
            return 0;
        }

        static int SignalOne(Sh sh, Io io, int pid, int sig, string prog)
        {
            var m = sh.M;
            var p = AllProcs(sh).FirstOrDefault(x => x.Pid == pid);
            if (p == null) { io.E(prog == "kill" ? "bash: kill: (" + pid + ") - No such process" : prog + ": (" + pid + ") - No such process"); return 1; }
            if (sh.P.Cr.Uid != 0 && p.Uid != sh.P.Cr.Uid) { io.E(prog == "kill" ? "bash: kill: (" + pid + ") - Operation not permitted" : prog + ": killing pid " + pid + " failed: Operation not permitted"); return 1; }
            if (sig == 0 || sig == 18 || sig == 28 || sig == 17) return 0;
            if (pid == 1) return 0;
            // a service's main process: systemd sees it die
            var svc = m.Services.Values.FirstOrDefault(s => s.Active == "active" && s.Pid == pid);
            m.Procs.RemoveAll(x => x.Pid == pid);
            if (sig == 1 && svc != null) { m.Procs.Add(p); return 0; }   // HUP: daemons reload
            if (svc != null)
            {
                string key = m.Services.First(kv => kv.Value == svc).Key;
                var ui = m.GetUnit(svc.Unit, svc.Owner.Length > 0 ? svc.Owner : null);
                m.Procs.RemoveAll(x => x.Unit == svc.Unit);
                svc.Active = "failed"; svc.Sub = "failed"; svc.Result = "signal"; svc.Pid = 0; svc.Ports.Clear();
                m.Log("systemd", 1, 5, svc.Unit + ": Main process exited, code=killed, status=" + sig + "/" + Sh.SigName(sig), svc.Unit);
                m.Log("systemd", 1, 4, svc.Unit + ": Failed with result 'signal'.", svc.Unit);
                string restart = ui.Get("Service.Restart") ?? "no";
                if (restart == "always" || restart == "on-failure" || restart == "on-abnormal" || (restart == "on-abort" && sig != 15))
                {
                    m.Services.Remove(key);
                    m.StartService(svc.Unit, svc.Owner.Length > 0 ? svc.Owner : null);
                    m.Log("systemd", 1, 6, svc.Unit + ": Scheduled restart job, restart counter is at 1.", svc.Unit);
                }
            }
            foreach (var c in m.Containers.Where(c => c.Running && m.Procs.All(x => x.Container != c.Name))) { }
            // the shell's jobs see their process end
            if (sh.S != null) foreach (var x in sh.S.ShStack) foreach (var j in x.Jobs) if (j.Pid == pid) { j.Done = true; if (sig == 9 || sig == 15) j.Reported = false; }
            return 0;
        }

        static int ParseSig(string s)
        {
            if (s.StartsWith("SIG", StringComparison.Ordinal)) s = s.Substring(3);
            return Sh.SigNum(s);
        }

        static int Kill(Sh sh, List<string> a, Io io)
        {
            int sig = 15;
            var targets = new List<string>();
            for (int i = 1; i < a.Count; i++)
            {
                string x = a[i];
                if (x == "-l" || x == "-L") { var sb = new StringBuilder(); for (int k = 1; k < 32; k++) sb.Append((k.ToString().PadLeft(2) + ") SIG" + Sh.SigName(k)).PadRight(16) + (k % 5 == 0 ? "\n" : "\t")); io.L(sb.ToString().TrimEnd()); return 0; }
                if ((x == "-s" || x == "-n") && i + 1 < a.Count) { sig = ParseSig(a[++i].ToUpperInvariant()); continue; }
                if (x.StartsWith("-", StringComparison.Ordinal) && x.Length > 1 && targets.Count == 0) { sig = ParseSig(x.Substring(1).ToUpperInvariant()); if (sig < 0) { io.E("bash: kill: " + x.Substring(1) + ": invalid signal specification"); return 1; } continue; }
                targets.Add(x);
            }
            if (targets.Count == 0) { io.E("kill: usage: kill [-s sigspec | -n signum | -sigspec] pid | jobspec ... or kill -l [sigspec]"); return 2; }
            int st = 0;
            foreach (var t in targets)
            {
                if (t.StartsWith("%", StringComparison.Ordinal))
                {
                    int id; ShJob j = null;
                    if (int.TryParse(t.Substring(1), out id)) j = sh.Jobs.FirstOrDefault(x => x.Id == id);
                    else if (t == "%%" || t == "%+") j = sh.Jobs.LastOrDefault();
                    if (j == null) { io.E("bash: kill: " + t + ": no such job"); st = 1; continue; }
                    sh.M.Procs.RemoveAll(x => x.Pid == j.Pid);
                    j.Done = true;
                    io.E("[" + j.Id + "]+  " + (sig == 9 ? "Killed" : "Terminated").PadRight(24) + j.Text);
                    j.Reported = true;
                    continue;
                }
                int pid;
                if (!int.TryParse(t, out pid)) { io.E("bash: kill: " + t + ": arguments must be process or job IDs"); st = 1; continue; }
                if (SignalOne(sh, io, pid, sig, "kill") != 0) st = 1;
            }
            return st;
        }
        static int Killall(Sh sh, List<string> a, Io io)
        {
            int sig = 15; var names = new List<string>();
            for (int i = 1; i < a.Count; i++) { if (a[i] == "-s" && i + 1 < a.Count) sig = ParseSig(a[++i]); else if (a[i].StartsWith("-", StringComparison.Ordinal) && Sh.SigNum(a[i].Substring(1)) > 0) sig = Sh.SigNum(a[i].Substring(1)); else if (!a[i].StartsWith("-", StringComparison.Ordinal)) names.Add(a[i]); }
            int st = 0;
            foreach (var n in names)
            {
                var ps = AllProcs(sh).Where(p => p.Comm == n && p.Comm != "bash").ToList();
                if (ps.Count == 0) { io.E(n + ": no process found"); st = 1; continue; }
                foreach (var p in ps) SignalOne(sh, io, p.Pid, sig, "killall");
            }
            return st;
        }
        static List<ProcEntry> MatchProcs(Sh sh, List<string> a, out int sig, out bool full, out string user, out string pattern, out bool listName, out bool listFull)
        {
            sig = 15; full = false; user = null; pattern = null; listName = false; listFull = false; bool exact = false;
            for (int i = 1; i < a.Count; i++)
            {
                string x = a[i];
                if (x == "-u" && i + 1 < a.Count) { user = a[++i]; continue; }
                if (x == "-U" && i + 1 < a.Count) { user = a[++i]; continue; }
                if (x == "-f") { full = true; continue; }
                if (x == "-x" || x == "--exact") { exact = true; continue; }
                if (x == "-l") { listName = true; continue; }
                if (x == "-a") { listFull = true; continue; }
                if (x == "--signal" && i + 1 < a.Count) { sig = ParseSig(a[++i].ToUpperInvariant()); continue; }
                if (x.StartsWith("-", StringComparison.Ordinal) && x.Length > 1 && Sh.SigNum(x.Substring(1).ToUpperInvariant()) > 0) { sig = Sh.SigNum(x.Substring(1).ToUpperInvariant()); continue; }
                pattern = x;
            }
            var all = AllProcs(sh).Where(p => p.Comm != "ps").ToList();
            IEnumerable<ProcEntry> q = all;
            if (user != null) { var u = user; q = q.Where(p => p.User == u || p.Uid.ToString() == u); }
            if (pattern != null) { var rx = new Regex(exact ? "^(?:" + PosixToNet(pattern, true) + ")$" : PosixToNet(pattern, true)); bool f = full; q = q.Where(p => rx.IsMatch(f ? p.Cmd : p.Comm)); }
            return q.ToList();
        }
        static int Pkill(Sh sh, List<string> a, Io io)
        {
            int sig; bool full, ln, lf; string user, pat;
            var ps = MatchProcs(sh, a, out sig, out full, out user, out pat, out ln, out lf);
            if (pat == null && user == null) { io.E("pkill: no matching criteria specified\nTry `pkill --help' for more information."); return 2; }
            ps = ps.Where(p => p.Pid != sh.Pid && !(user != null && p.Comm == "bash" && sh.S != null && sh.S.ShStack.Any(x => x.Pid == p.Pid && x == sh))).ToList();
            if (ps.Count == 0) return 1;
            foreach (var p in ps)
            {
                if (p.Comm == "bash")
                {
                    // the shells of that user: their terminal session ends
                    foreach (var mm in sh.M.Net.Machines) foreach (var s in mm.Sessions) foreach (var x in s.ShStack.ToList()) if (x.Pid == p.Pid && x != sh) s.Kick(sh.M, "\nConnection closed (killed).\n");
                    continue;
                }
                SignalOne(sh, io, p.Pid, sig, "pkill");
            }
            return 0;
        }
        static int Pgrep(Sh sh, List<string> a, Io io)
        {
            int sig; bool full, ln, lf; string user, pat;
            var ps = MatchProcs(sh, a, out sig, out full, out user, out pat, out ln, out lf);
            if (pat == null && user == null) { io.E("pgrep: no matching criteria specified\nTry `pgrep --help' for more information."); return 2; }
            foreach (var p in ps) io.L(p.Pid + (ln ? " " + p.Comm : lf ? " " + p.Cmd : ""));
            return ps.Count == 0 ? 1 : 0;
        }
        static int Pidof(Sh sh, List<string> a, Io io)
        {
            var ps = AllProcs(sh).Where(p => a.Skip(1).Contains(p.Comm)).OrderByDescending(p => p.Pid).ToList();
            if (ps.Count == 0) return 1;
            io.L(string.Join(" ", ps.Select(p => p.Pid.ToString()).ToArray()));
            return 0;
        }
        static int Nice(Sh sh, List<string> a, Io io)
        {
            int n = 10; int i = 1;
            if (i < a.Count && a[i] == "-n" && i + 1 < a.Count) { int.TryParse(a[i + 1], out n); i += 2; }
            else if (i < a.Count && a[i].StartsWith("--adjustment=", StringComparison.Ordinal)) { int.TryParse(a[i].Substring(13), out n); i++; }
            else if (i < a.Count && Regex.IsMatch(a[i], "^-\\d+$")) { n = int.Parse(a[i].Substring(1)); i++; }
            else if (i < a.Count && Regex.IsMatch(a[i], "^--\\d+$")) { n = -int.Parse(a[i].Substring(2)); i++; }
            if (i >= a.Count) { io.L(sh.BgNice.ToString()); return 0; }   // nice alone prints the current niceness
            if (n < 0 && sh.P.Cr.Uid != 0) { io.E("nice: cannot set niceness: Permission denied"); n = 0; }
            n = Math.Max(-20, Math.Min(19, sh.BgNice + n));   // the increment adds to the current niceness
            int save = sh.BgNice; sh.BgNice = n;
            try { return sh.Run(a.Skip(i).ToList(), io); }
            finally { if (!sh.Background) sh.BgNice = save; }
        }
        static int Renice(Sh sh, List<string> a, Io io)
        {
            int n = 0; bool haveN = false; string mode = "p";
            var ids = new List<string>();
            for (int i = 1; i < a.Count; i++)
            {
                string x = a[i];
                if ((x == "-n" || x == "--priority") && i + 1 < a.Count) { int.TryParse(a[++i], out n); haveN = true; continue; }
                if (x == "-p" || x == "--pid") { mode = "p"; continue; }
                if (x == "-u" || x == "--user") { mode = "u"; continue; }
                if (x == "-g" || x == "--pgrp") { mode = "g"; continue; }
                if (!haveN && Regex.IsMatch(x, "^[-+]?\\d+$")) { n = int.Parse(x); haveN = true; continue; }
                ids.Add(x);
            }
            int st = 0;
            var all = AllProcs(sh);
            foreach (var id in ids)
            {
                var targets = mode == "u" ? sh.M.Procs.Where(p => p.User == id || p.Uid.ToString() == id).ToList() : sh.M.Procs.Where(p => p.Pid.ToString() == id).ToList();
                if (targets.Count == 0) { io.E("renice: failed to get priority for " + id + " (process ID): No such process"); st = 1; continue; }
                foreach (var p in targets)
                {
                    if (sh.P.Cr.Uid != 0 && (p.Uid != sh.P.Cr.Uid || n < p.Nice)) { io.E("renice: failed to set priority for " + p.Pid + " (process ID): Permission denied"); st = 1; continue; }
                    int old = p.Nice;
                    p.Nice = Math.Max(-20, Math.Min(19, n));
                    io.L(p.Pid + " (process ID) old priority " + old + ", new priority " + p.Nice);
                }
            }
            return st;
        }

        static int Free(Sh sh, List<string> a, Io io)
        {
            var m = sh.M;
            long tot = m.MemTotalKb, used = m.MemUsedKb, cache = 986000, free = tot - used - cache, swap = m.Swaps.Sum(s => s.Bytes / 1024 - 4);
            Func<long, string> f = kb => a.Contains("-h") || a.Contains("--human") ? (kb < 1024 ? kb + "Ki" : kb < 1048576 ? (kb / 1024.0).ToString(kb < 10240 ? "0.0" : "0", CultureInfo.InvariantCulture) + "Mi" : (kb / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + "Gi") : a.Contains("-m") ? (kb / 1024).ToString() : a.Contains("-g") ? (kb / 1048576).ToString() : kb.ToString();
            io.L("               total        used        free      shared  buff/cache   available");
            io.L("Mem:    " + f(tot).PadLeft(12) + f(used).PadLeft(12) + f(free).PadLeft(12) + f(18000).PadLeft(12) + f(cache).PadLeft(12) + f(free + cache - 100000).PadLeft(12));
            io.L("Swap:   " + f(swap).PadLeft(12) + f(0).PadLeft(12) + f(swap).PadLeft(12));
            return 0;
        }
        static int Lscpu(Sh sh, List<string> a, Io io)
        {
            io.L("Architecture:            x86_64\n  CPU op-mode(s):        32-bit, 64-bit\n  Address sizes:         46 bits physical, 48 bits virtual\n  Byte Order:            Little Endian\nCPU(s):                  2\n  On-line CPU(s) list:   0,1\nVendor ID:               GenuineIntel\n  Model name:            Intel Xeon Processor (Cascadelake)\n    CPU family:          6\n    Model:               85\n    Thread(s) per core:  1\n    Core(s) per socket:  1\n    Socket(s):           2\nVirtualization features: \n  Hypervisor vendor:     KVM\n  Virtualization type:   full\nNUMA:                    \n  NUMA node(s):          1\n  NUMA node0 CPU(s):     0,1");
            return 0;
        }
        static int Sysctl(Sh sh, List<string> a, Io io)
        {
            var m = sh.M;
            var o = Opts.Parse(a, "awpnqe", "all|a,write|w,load=?|p,values|n,quiet|q,system", "sysctl");
            if (o.Error != null) return Fail(io, o);
            if (o.Has("a")) { foreach (var kv in m.SysctlAll()) io.L(kv.Key + " = " + kv.Value); return 0; }
            if (o.Has("system") || o.Has("p"))
            {
                if (sh.P.Cr.Uid != 0) { io.E("sysctl: permission denied on key"); return 1; }
                var files = o.Has("system") ? new List<string> { "/etc/sysctl.conf" } : new List<string> { string.IsNullOrEmpty(o.Get("p")) ? "/etc/sysctl.conf" : o.Get("p") };
                if (o.Has("system")) try { var d = m.V.G("/etc/sysctl.d"); if (d.Exists) foreach (var k in Vfs.Sorted(d.Node.Kids.Keys)) files.Insert(0, "/etc/sysctl.d/" + k); } catch (VfsEx) { }
                foreach (var f in files)
                {
                    var t = m.V.Cat(f);
                    if (t == null) { io.E("sysctl: cannot open \"" + f + "\": No such file or directory"); return 1; }
                    if (o.Has("system")) io.L("* Applying " + f + " ...");
                    foreach (var raw in t.Split('\n'))
                    {
                        var l = raw.Trim(); if (l.Length == 0 || l.StartsWith("#", StringComparison.Ordinal) || l.StartsWith(";", StringComparison.Ordinal)) continue;
                        int eq = l.IndexOf('='); if (eq < 0) continue;
                        string k = l.Substring(0, eq).Trim(), v = l.Substring(eq + 1).Trim();
                        m.Sysctl[k.Replace('/', '.')] = v;
                        io.L(k + " = " + v);
                    }
                }
                return 0;
            }
            int st = 0;
            foreach (var x in o.Args)
            {
                int eq = x.IndexOf('=');
                if (eq > 0)
                {
                    if (sh.P.Cr.Uid != 0) { io.E("sysctl: permission denied on key \"" + x.Substring(0, eq) + "\""); st = 1; continue; }
                    m.Sysctl[x.Substring(0, eq).Trim()] = x.Substring(eq + 1).Trim();
                    io.L(x.Substring(0, eq).Trim() + " = " + x.Substring(eq + 1).Trim());
                    continue;
                }
                var hit = m.SysctlAll().FirstOrDefault(kv => kv.Key == x.Replace('/', '.'));
                if (hit.Key == null) { io.E("sysctl: cannot stat /proc/sys/" + x.Replace('.', '/') + ": No such file or directory"); st = 255; continue; }
                io.L(o.Has("n") ? hit.Value : hit.Key + " = " + hit.Value);
            }
            return st;
        }

        static int TunedAdm(Sh sh, List<string> a, Io io)
        {
            var m = sh.M;
            string verb = a.Count > 1 ? a[1] : "";
            bool running = m.ServiceActive("tuned.service");
            switch (verb)
            {
                case "list":
                    io.L("Available profiles:");
                    foreach (var p in Packages.TunedProfiles.OrderBy(x => x[0], StringComparer.Ordinal)) io.L("- " + p[0].PadRight(28) + "- " + p[1]);
                    io.L((running ? (m.TunedProfile.Length > 0 ? "Current active profile: " + m.TunedProfile : "No current active profile.") : "Cannot talk to TuneD daemon via DBus. Is TuneD daemon running?"));
                    return 0;
                case "active":
                    if (!running) { io.L("Cannot talk to TuneD daemon via DBus. Is TuneD daemon running?\nNo current active profile."); return 1; }
                    io.L(m.TunedProfile.Length > 0 ? "Current active profile: " + m.TunedProfile : "No current active profile.");
                    return 0;
                case "recommend": io.L("virtual-guest"); return 0;
                case "profile":
                    {
                        if (a.Count < 3) { io.E("usage: tuned-adm profile [-h] [profiles ...]"); return 1; }
                        if (sh.P.Cr.Uid != 0) { io.E("Cannot change profile: Access denied (Cert Lab: run it as root)"); return 1; }
                        string p = string.Join(" ", a.Skip(2).ToArray());
                        if (!Packages.TunedProfiles.Any(x => x[0] == a[2])) { io.E("Requested profile '" + a[2] + "' doesn't exist."); return 1; }
                        m.SysWrite("/etc/tuned/active_profile", p + "\n");
                        m.SysWrite("/etc/tuned/profile_mode", "manual\n");
                        if (!running) { io.L("Trying to (re)start tuned..."); m.StartService("tuned.service", null); io.L("TuneD (re)started, changes applied."); }
                        m.TunedProfile = p;
                        return 0;
                    }
                case "off":
                    if (sh.P.Cr.Uid != 0) { io.E("Cannot disable active profile: Access denied"); return 1; }
                    m.SysWrite("/etc/tuned/active_profile", "\n"); m.TunedProfile = ""; return 0;
                case "auto_profile":
                    m.SysWrite("/etc/tuned/profile_mode", "auto\n"); m.SysWrite("/etc/tuned/active_profile", "virtual-guest\n"); m.TunedProfile = "virtual-guest"; return 0;
                case "profile_mode": io.L("Profile selection mode: " + (m.V.Cat("/etc/tuned/profile_mode") ?? "auto").Trim()); return 0;
                case "verify":
                    if (!running) { io.L("Cannot talk to TuneD daemon via DBus. Is TuneD daemon running?"); return 1; }
                    io.L("Verification succeeded, current system settings match the preset profile.\nSee TuneD log file ('/var/log/tuned/tuned.log') for details."); return 0;
                case "profile_info":
                    {
                        string p = a.Count > 2 ? a[2] : m.TunedProfile;
                        var pr = Packages.TunedProfiles.FirstOrDefault(x => x[0] == p);
                        if (pr == null) { io.E("Unable to get information about profile '" + p + "'"); return 1; }
                        io.L("Profile name:\n" + pr[0] + "\n\nProfile summary:\n" + pr[1] + "\n\nProfile description:\n");
                        return 0;
                    }
            }
            io.E("usage: tuned-adm [-h] [--version] [--debug] [--async] [--timeout TIMEOUT] [--loglevel LOGLEVEL]\n                 {list,active,off,profile,profile_info,recommend,verify,auto_profile,profile_mode,instance_acquire_devices,get_instances,instance_get_devices} ...");
            return 1;
        }

        static int Chronyc(Sh sh, List<string> a, Io io)
        {
            var m = sh.M;
            var args = a.Skip(1).Where(x => x != "-n" && x != "-c").ToList();
            string verb = args.Count > 0 ? args[0] : "";
            if (!m.ServiceActive("chronyd.service")) { io.E("506 Cannot talk to daemon"); return 1; }
            var servers = m.ChronyServers();
            switch (verb)
            {
                case "sources":
                    {
                        if (args.Contains("-v")) io.L("\n  .-- Source mode  '^' = server, '=' = peer, '#' = local clock.\n / .- Source state '*' = current best, '+' = combined, '-' = not combined,\n| /             'x' = may be in error, '~' = too variable, '?' = unusable.\n||                                                 .- xxxx [ yyyy ] +/- zzzz\n||      Reachability register (octal) -.           |  xxxx = adjusted offset,\n||      Log2(Polling interval) --.      |          |  yyyy = measured offset,\n||                                \\     |          |  zzzz = estimated error.\n||                                 |    |           \\");
                        io.L("MS Name/IP address         Stratum Poll Reach LastRx Last sample               \n===============================================================================");
                        foreach (var s in servers)
                        {
                            string host = s.Split(' ')[1];
                            string ip = m.Net.Resolve(m, host);
                            bool ok = ip == m.Net.UtilityIp && m.Net.Reachable(m, ip);
                            if (s.StartsWith("pool", StringComparison.Ordinal) && ip == null) continue;
                            bool best = ok && m.ChronySyncedAt >= 0 && m.ChronySource == host;
                            io.L((best ? "^*" : ok ? "^+" : "^?") + " " + (host.Length > 27 ? host.Substring(0, 27) : host).PadRight(27) + (ok ? "2" : "0").PadLeft(2) + "   6   " + (ok ? "377" : "  0") + "    " + (ok ? "35" : " -").PadLeft(3) + (ok ? "   +127us[ +254us] +/-   12ms" : "     +0ns[   +0ns] +/-    0ns"));
                        }
                        return 0;
                    }
                case "tracking":
                    {
                        bool sync = m.NtpSynced;
                        string ip = sync ? m.Net.UtilityIp : "0.0.0.0";
                        io.L("Reference ID    : " + (sync ? "C0A832FE (" + m.ChronySource + ")" : "00000000 ()") + "\nStratum         : " + (sync ? "3" : "0") + "\nRef time (UTC)  : " + (sync ? DateFormat(m, m.Now - 30, "%a %b %d %H:%M:%S %Y", true) : "Thu Jan 01 00:00:00 1970") + "\nSystem time     : 0.000012345 seconds fast of NTP time\nLast offset     : +0.000003456 seconds\nRMS offset      : 0.000045678 seconds\nFrequency       : 12.345 ppm slow\nResidual freq   : +0.001 ppm\nSkew            : 0.123 ppm\nRoot delay      : 0.012345678 seconds\nRoot dispersion : 0.001234567 seconds\nUpdate interval : 64.5 seconds\nLeap status     : " + (sync ? "Normal" : "Not synchronised"));
                        return 0;
                    }
                case "sourcestats": io.L("Name/IP Address            NP  NR  Span  Frequency  Freq Skew  Offset  Std Dev\n=============================================================================="); foreach (var s in servers) { if (m.ChronySource == s.Split(' ')[1]) io.L(s.Split(' ')[1].PadRight(25) + "  12   7   11m     -0.012      0.123    +45us    89us"); } return 0;
                case "makestep": io.L("200 OK"); return 0;
                case "waitsync": return m.NtpSynced ? 0 : 1;
                case "activity": io.L("200 OK\n" + servers.Count + " sources online\n0 sources offline\n0 sources doing burst (return to online)\n0 sources doing burst (return to offline)\n0 sources with unknown address"); return 0;
                case "reload": return 0;
            }
            io.E("Unrecognized command");
            return 1;
        }

        static int Logger(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "p:t:isf:", "priority=|p,tag=|t,id=?|i,stderr|s,file=|f,journald", "logger");
            if (o.Error != null) return Fail(io, o);
            string pri = o.Get("p", "user.notice");
            string fac = pri.Contains(".") ? pri.Split('.')[0] : "user", lvl = pri.Contains(".") ? pri.Split('.')[1] : pri;
            int lv = Machine.PriOf(lvl); if (lv < 0) { io.E("logger: unknown priority name: " + lvl); return 1; }
            string tag = o.Get("t", sh.P.Cr.User);
            string msg = o.Args.Count > 0 ? string.Join(" ", o.Args.ToArray()) : io.In.ReadAll().TrimEnd('\n');
            foreach (var line in msg.Split('\n'))
            {
                sh.M.Journal.Add(new JournalEntry { Time = sh.M.Now, Boot = sh.M.BootId, Ident = tag, Pid = o.Has("i") ? sh.Pid : 0, Pri = lv, Msg = line, Uid = sh.P.Cr.Uid });
                sh.M.Syslog(tag, o.Has("i") ? sh.Pid : 0, lv, line, fac);
                if (o.Has("s")) io.E("<" + lv + ">" + DateText(sh.M, sh.M.Now) + " " + tag + ": " + line);
            }
            return 0;
        }
        static int Dmesg(Sh sh, List<string> a, Io io)
        {
            if (sh.P.Cr.Uid != 0) { io.E("dmesg: read kernel buffer failed: Operation not permitted"); return 1; }
            var m = sh.M;
            io.L("[    0.000000] Linux version " + m.KernelRelease + " (mockbuild@cert-lab) (gcc (GCC) 11.5.0 20240719 (Red Hat 11.5.0-2), GNU ld version 2.35.2-54.el9) #1 SMP PREEMPT_DYNAMIC");
            io.L("[    0.000000] Command line: " + m.Cmdline);
            io.L("[    0.000000] BIOS-provided physical RAM map:");
            io.L("[    1.204567] virtio_blk virtio2: [vda] 41943040 512-byte logical blocks (21.5 GB/20.0 GiB)");
            foreach (var d in m.Disks.Skip(1)) io.L("[    1.21" + d.Index + "345] virtio_blk virtio" + (d.Index + 2) + ": [" + d.Name + "] " + d.Bytes / 512 + " 512-byte logical blocks (" + Machine.Size(d.Bytes, true).Replace("G", " GB") + "/" + Machine.Size(d.Bytes, false).Replace("G", " GiB") + ")");
            io.L("[    2.345678] SELinux:  Initializing.");
            io.L("[    4.567890] XFS (dm-0): Mounting V5 Filesystem");
            foreach (var j in m.Journal.Where(x => x.Ident == "kernel" && x.Boot == m.BootId).Skip(2)) io.L("[" + ((j.Time - m.BootTime) + 5.0).ToString("0.000000", CultureInfo.InvariantCulture).PadLeft(12) + "] " + j.Msg);
            return 0;
        }

        // ------------------------------------------------------------ crontab / at
        static bool SchedAllowed(Machine m, string user, string allow, string deny)
        {
            if (user == "root") return true;
            var al = m.V.Cat(allow);
            if (al != null) return al.Split('\n').Any(x => x.Trim() == user);
            var dl = m.V.Cat(deny);
            if (dl != null) return !dl.Split('\n').Any(x => x.Trim() == user);
            return false;
        }

        static int Crontab(Sh sh, List<string> a, Io io)
        {
            var m = sh.M;
            var o = Opts.Parse(a, "u:elris", "", "crontab");
            if (o.Error != null) return Fail(io, o);
            string user = o.Get("u") ?? sh.P.Cr.User;
            if (o.Has("u") && sh.P.Cr.Uid != 0 && user != sh.P.Cr.User) { io.E("must be privileged to use -u"); return 1; }
            if (m.User(user) == null) { io.E("crontab: user `" + user + "' unknown"); return 1; }
            if (!SchedAllowed(m, user, "/etc/cron.allow", "/etc/cron.deny")) { io.E("You (" + user + ") are not allowed to use this program (crontab)\nSee crontab(1) for more information"); return 1; }
            string file = "/var/spool/cron/" + user;
            string cur = m.V.Cat(file);
            if (o.Has("l")) { if (cur == null) { io.E("no crontab for " + user); return 1; } io.P(cur); return 0; }
            if (o.Has("r"))
            {
                if (cur == null) { io.E("no crontab for " + user); return 1; }
                if (o.Has("i") && !Confirm(sh, io, "crontab: really delete " + user + "'s crontab? ")) return 0;
                m.V.RemoveG(file);
                return 0;
            }
            string text;
            if (o.Has("e"))
            {
                if (sh.S == null) return 1;
                string start = cur ?? "";
                for (;;)
                {
                    string res = sh.S.Edit("/tmp/crontab." + m.NewHex(6), start, false, (sh.GetVar("EDITOR") ?? sh.GetVar("VISUAL") ?? "vi").Contains("nano") ? "nano" : "vi");
                    if (res == null || res == cur) { io.E(cur == null && (res == null || res.Length == 0) ? "no crontab for " + user + " - using an empty one\ncrontab: no changes made to crontab" : "crontab: no changes made to crontab"); return 0; }
                    string err = CronCheck(res);
                    if (err == null) { text = res; break; }
                    io.E("\"/tmp/crontab.XXXXXX\":" + err + "\nerrors in crontab file, can't install.");
                    if (!Confirm(sh, io, "Do you want to retry the same edit? (y/n) ")) return 1;
                    start = res;
                }
            }
            else
            {
                string src = o.Args.Count > 0 ? o.Args[0] : "-";
                try { text = src == "-" ? io.In.ReadAll() : Read(sh, src); }
                catch (VfsEx e) { io.E(src + ": " + e.Message); return 1; }
                string err = CronCheck(text);
                if (err != null) { io.E("\"" + (src == "-" ? "-" : src) + "\":" + err + "\nerrors in crontab file, can't install."); return 1; }
            }
            if (!text.EndsWith("\n", StringComparison.Ordinal) && text.Length > 0) { io.E("new crontab file is missing newline before EOF, can't install."); return 1; }
            var pw = m.User(user);
            m.V.EnsureDir("/var/spool/cron", 0x1C0, null);
            m.V.Put(file, text, 0x180, pw.Uid, pw.Gid, "unconfined_u:object_r:user_cron_spool_t:s0");
            m.Syslog("crontab", sh.Pid, 6, "(" + user + ") REPLACE (" + user + ")", "cron");
            return 0;
        }
        static string CronCheck(string text)
        {
            int n = 0;
            foreach (var line in text.Split('\n'))
            {
                n++;
                var e = Machine.CronLineError(line, false);
                if (e != null) return n + ": " + e;
            }
            return null;
        }

        /// <summary>at's time forms: now + 5 minutes, 16:00, 4pm, noon, midnight, teatime, tomorrow, 16:00 tomorrow, 10:00 2026-03-05.</summary>
        public static bool AtTime(Machine m, string spec, out long t)
        {
            t = m.Now;
            string s = spec.Trim().ToLowerInvariant();
            var local = m.LocalTime(m.Now);
            var rel = Regex.Match(s, "^now\\s*(\\+\\s*(\\d+)\\s*(minute|min|hour|day|week|month|year)s?)?$");
            if (rel.Success)
            {
                if (!rel.Groups[1].Success || rel.Groups[1].Value.Length == 0) { t = m.Now; return true; }
                long n = long.Parse(rel.Groups[2].Value); string u = rel.Groups[3].Value;
                t = (m.Now / 60) * 60 + n * (u.StartsWith("min", StringComparison.Ordinal) ? 60 : u == "hour" ? 3600 : u == "day" ? 86400 : u == "week" ? 604800 : u == "month" ? 2592000 : 31536000);
                return true;
            }
            DateTime day = local.Date;
            bool dayGiven = false;
            var dm = Regex.Match(s, "\\s*(tomorrow|today|(\\d{4})-(\\d{1,2})-(\\d{1,2})|(\\d{1,2})/(\\d{1,2})/(\\d{2,4})|(\\d{1,2})\\.(\\d{1,2})\\.(\\d{2,4}))$");
            if (dm.Success)
            {
                dayGiven = true;
                if (dm.Groups[1].Value == "tomorrow") day = day.AddDays(1);
                else if (dm.Groups[2].Success && dm.Groups[2].Value.Length > 0) day = new DateTime(int.Parse(dm.Groups[2].Value), int.Parse(dm.Groups[3].Value), int.Parse(dm.Groups[4].Value));
                else if (dm.Groups[5].Success && dm.Groups[5].Value.Length > 0) { int y = int.Parse(dm.Groups[7].Value); if (y < 100) y += 2000; day = new DateTime(y, int.Parse(dm.Groups[5].Value), int.Parse(dm.Groups[6].Value)); }
                else if (dm.Groups[8].Success && dm.Groups[8].Value.Length > 0) { int y = int.Parse(dm.Groups[10].Value); if (y < 100) y += 2000; day = new DateTime(y, int.Parse(dm.Groups[9].Value), int.Parse(dm.Groups[8].Value)); }
                s = s.Substring(0, dm.Index).Trim();
            }
            int hour, min = 0;
            if (s == "noon") hour = 12;
            else if (s == "midnight") hour = 0;
            else if (s == "teatime") hour = 16;
            else if (s.Length == 0 && dayGiven) { hour = local.Hour; min = local.Minute; }
            else
            {
                var tm = Regex.Match(s, "^(\\d{1,2})(:(\\d{2}))?\\s*(am|pm)?$");
                if (!tm.Success) { var t4 = Regex.Match(s, "^(\\d{2})(\\d{2})$"); if (!t4.Success) return false; hour = int.Parse(t4.Groups[1].Value); min = int.Parse(t4.Groups[2].Value); }
                else
                {
                    hour = int.Parse(tm.Groups[1].Value);
                    if (tm.Groups[3].Success && tm.Groups[3].Value.Length > 0) min = int.Parse(tm.Groups[3].Value);
                    if (tm.Groups[4].Value == "pm" && hour < 12) hour += 12;
                    if (tm.Groups[4].Value == "am" && hour == 12) hour = 0;
                }
            }
            if (hour > 23 || min > 59) return false;
            var when = day.AddHours(hour).AddMinutes(min);
            t = LocalToEpoch(m, when);
            if (!dayGiven && t <= m.Now) t += 86400;
            return true;
        }

        static int At(Sh sh, List<string> a, Io io)
        {
            var m = sh.M;
            var o = Opts.Parse(a, "f:q:lcdmMvt:", "", "at");
            if (o.Error != null) return Fail(io, o);
            if (o.Has("l")) return Atq(sh, new List<string> { "atq" }, io);
            if (o.Has("d")) { var b = new List<string> { "atrm" }; b.AddRange(o.Args); return Atrm(sh, b, io); }
            if (!SchedAllowed(m, sh.P.Cr.User, "/etc/at.allow", "/etc/at.deny")) { io.E("You do not have permission to use at."); return 1; }
            if (o.Has("c"))
            {
                foreach (var id in o.Args) { var j = m.AtJobs().FirstOrDefault(x => x.Id.ToString() == id); if (j == null) { io.E("Cannot find jobid " + id); return 1; } io.P(m.V.Cat(j.File)); }
                return 0;
            }
            string spec = string.Join(" ", o.Args.ToArray());
            if (a[0] == "batch") spec = "now";
            if (spec.Length == 0) { io.E("Garbled time"); return 1; }
            long t;
            if (!AtTime(m, spec, out t)) { io.E("syntax error. Last token seen: " + o.Args.Last() + "\nGarbled time"); return 1; }
            string cmds;
            if (o.Has("f")) { try { cmds = Read(sh, o.Get("f")); } catch (VfsEx e) { io.E("Cannot open input file " + o.Get("f") + ": " + e.Message); return 1; } }
            else if (io.In.IsTty && sh.S != null)
            {
                var sb = new StringBuilder();
                for (;;)
                {
                    string l = sh.S.ReadLine("at> ", false);
                    if (l == null) { io.L("<EOT>"); break; }
                    sb.Append(l).Append('\n');
                }
                cmds = sb.ToString();
            }
            else cmds = io.In.ReadAll();
            int jobId = m.AddAtJob(sh.P.Cr.User, t, cmds, o.Get("q") ?? (a[0] == "batch" ? "b" : "a"));
            io.E("warning: commands will be executed using /bin/sh\njob " + jobId + " at " + DateFormat(m, t, "%a %b %e %H:%M:00 %Y", false));
            return 0;
        }
        static int Atq(Sh sh, List<string> a, Io io)
        {
            var m = sh.M;
            foreach (var j in m.AtJobs())
            {
                if (sh.P.Cr.Uid != 0 && j.User != sh.P.Cr.User) continue;
                io.L(j.Id + "\t" + DateFormat(m, j.When, "%a %b %e %H:%M:00 %Y", false) + " " + j.Queue + " " + j.User);
            }
            return 0;
        }
        static int Atrm(Sh sh, List<string> a, Io io)
        {
            var m = sh.M;
            int st = 0;
            foreach (var id in a.Skip(1))
            {
                var j = m.AtJobs().FirstOrDefault(x => x.Id.ToString() == id);
                if (j == null) { io.E("Cannot find jobid " + id); st = 1; continue; }
                if (sh.P.Cr.Uid != 0 && j.User != sh.P.Cr.User) { io.E(id + ": Not owner"); st = 1; continue; }
                m.V.RemoveG(j.File);
            }
            return st;
        }

        // ------------------------------------------------------------ the boot loader
        static List<string> EntryFiles(Machine m)
        {
            var l = new List<string>();
            try { var d = m.V.G("/boot/loader/entries"); if (d.Exists) foreach (var k in d.Node.Kids.Keys.OrderByDescending(x => x, StringComparer.Ordinal)) if (k.EndsWith(".conf", StringComparison.Ordinal)) l.Add("/boot/loader/entries/" + k); } catch (VfsEx) { }
            return l;
        }
        static string EntryValue(string text, string key)
        {
            var mm = Regex.Match(text, "(?m)^" + key + " (.*)$");
            return mm.Success ? mm.Groups[1].Value.Trim() : "";
        }
        static int Grubby(Sh sh, List<string> a, Io io)
        {
            var m = sh.M;
            var o = Opts.Parse(a, "", "default-kernel,default-index,default-title,info=,update-kernel=,args=,remove-args=,set-default=,set-default-index=,add-kernel=,remove-kernel=,title=,initrd=,make-default,copy-default,bls-directory=,env=", "grubby");
            if (o.Error != null) { io.E(o.Error.Replace("Try 'grubby --help'", "Usage: grubby [OPTION...]")); return 1; }
            if (!m.V.Exists("/boot/loader/entries")) { io.E("grubby: /boot/loader/entries: No such file or directory (is /boot mounted?)"); return 1; }
            var files = EntryFiles(m).Where(f => !f.Contains("rescue")).ToList();
            var rescue = EntryFiles(m).Where(f => f.Contains("rescue")).ToList();
            var ordered = files.Concat(rescue).ToList();
            int def = m.DefaultEntryIndex();
            if (sh.P.Cr.Uid != 0 && (o.Has("update-kernel") || o.Has("set-default") || o.Has("set-default-index"))) { io.E("grubby: Permission denied"); return 1; }
            if (o.Has("default-kernel")) { if (def < ordered.Count) io.L("/boot/vmlinuz-" + EntryValue(m.V.Cat(ordered[def]), "version")); return 0; }
            if (o.Has("default-index")) { io.L(def.ToString()); return 0; }
            if (o.Has("default-title")) { if (def < ordered.Count) io.L(EntryValue(m.V.Cat(ordered[def]), "title")); return 0; }
            Func<string, List<string>> pick = sel =>
            {
                if (sel == "ALL") return ordered;
                if (sel == "DEFAULT") return def < ordered.Count ? new List<string> { ordered[def] } : new List<string>();
                int idx; if (int.TryParse(sel, out idx)) return idx < ordered.Count ? new List<string> { ordered[idx] } : new List<string>();
                return ordered.Where(f => "/boot/vmlinuz-" + EntryValue(m.V.Cat(f), "version") == sel || EntryValue(m.V.Cat(f), "version") == sel.Replace("/boot/vmlinuz-", "")).ToList();
            };
            if (o.Has("info"))
            {
                var sel = pick(o.Get("info"));
                if (sel.Count == 0) { io.E("The param " + o.Get("info") + " is incorrect"); return 1; }
                foreach (var f in sel)
                {
                    string t = m.V.Cat(f);
                    string opts = EntryValue(t, "options").Replace("$tuned_params", "").Trim();
                    var root = Regex.Match(opts, "root=(\\S+)");
                    io.L("index=" + ordered.IndexOf(f));
                    io.L("kernel=\"/boot" + EntryValue(t, "linux") + "\"");
                    io.L("args=\"" + Regex.Replace(opts, "root=\\S+\\s*", "").Trim() + (opts.Length > 0 ? " $tuned_params" : "") + "\"");
                    if (root.Success) io.L("root=\"" + root.Groups[1].Value + "\"");
                    io.L("initrd=\"/boot" + EntryValue(t, "initrd") + "\"");
                    io.L("title=\"" + EntryValue(t, "title") + "\"");
                    io.L("id=\"" + Vfs.Base(f).Replace(".conf", "") + "\"");
                }
                return 0;
            }
            if (o.Has("update-kernel"))
            {
                var sel = pick(o.Get("update-kernel"));
                if (sel.Count == 0) { io.E("The param " + o.Get("update-kernel") + " is incorrect"); return 1; }
                var add = (o.Get("args") ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var rem = (o.Get("remove-args") ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var f in sel)
                {
                    string t = m.V.Cat(f);
                    var opts = EntryValue(t, "options").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                    opts = UpdateArgs(opts, add, rem);
                    m.SysWrite(f, Regex.Replace(t, "(?m)^options .*$", "options " + string.Join(" ", opts.ToArray())));
                }
                if (o.Get("update-kernel") == "ALL")
                {
                    // ALL also keeps /etc/default/grub in step (new kernels get the same arguments)
                    string g = m.V.Cat("/etc/default/grub") ?? "";
                    var mm = Regex.Match(g, "(?m)^GRUB_CMDLINE_LINUX=\"([^\"]*)\"");
                    if (mm.Success)
                    {
                        var cur = mm.Groups[1].Value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                        cur = UpdateArgs(cur, add, rem);
                        m.SysWrite("/etc/default/grub", g.Substring(0, mm.Index) + "GRUB_CMDLINE_LINUX=\"" + string.Join(" ", cur.ToArray()) + "\"" + g.Substring(mm.Index + mm.Length));
                    }
                }
                return 0;
            }
            if (o.Has("set-default") || o.Has("set-default-index"))
            {
                var sel = o.Has("set-default") ? pick(o.Get("set-default")) : pick(o.Get("set-default-index"));
                if (sel.Count == 0) { io.E("The param " + (o.Get("set-default") ?? o.Get("set-default-index")) + " is incorrect"); return 1; }
                string id = Vfs.Base(sel[0]).Replace(".conf", "");
                string env = m.V.Cat("/boot/grub2/grubenv") ?? "";
                env = Regex.IsMatch(env, "(?m)^saved_entry=") ? Regex.Replace(env, "(?m)^saved_entry=.*$", "saved_entry=" + id) : "saved_entry=" + id + "\n" + env;
                m.SysWrite("/boot/grub2/grubenv", env);
                io.L("The default is /boot/loader/entries/" + id + ".conf with index " + ordered.IndexOf(sel[0]) + " and kernel /boot/vmlinuz-" + EntryValue(m.V.Cat(sel[0]), "version"));
                return 0;
            }
            io.E("Usage: grubby [OPTION...]\n  --default-kernel, --default-index, --info=ALL|DEFAULT|<kernel>, --update-kernel=ALL|DEFAULT|<kernel> --args=... --remove-args=..., --set-default=<kernel>, --set-default-index=N");
            return 1;
        }
        static List<string> UpdateArgs(List<string> cur, string[] add, string[] rem)
        {
            var l = new List<string>(cur);
            foreach (var r in rem) l.RemoveAll(x => x == r || x.Split('=')[0] == r.Split('=')[0] && !r.Contains("="));
            foreach (var x in add)
            {
                string k = x.Split('=')[0];
                int i = l.FindIndex(y => y.Split('=')[0] == k);
                if (i >= 0) l[i] = x; else l.Add(x);
            }
            return l;
        }
        static int Grub2Mkconfig(Sh sh, List<string> a, Io io)
        {
            var m = sh.M;
            var o = Opts.Parse(a, "o:", "output=|o,update-bls-cmdline", "grub2-mkconfig");
            if (o.Error != null) return Fail(io, o);
            if (sh.P.Cr.Uid != 0) { io.E("grub2-mkconfig: You must run this as root"); return 1; }
            io.E("Generating grub configuration file ...");
            string cfg = m.GrubCfg();
            if (o.Has("update-bls-cmdline"))
            {
                string cmd = "root=" + Machine.RootDev + " ro " + m.GrubCmdline();
                foreach (var f in EntryFiles(m).Where(x => !x.Contains("rescue"))) m.SysWrite(f, Regex.Replace(m.V.Cat(f), "(?m)^options .*$", "options " + cmd.Trim() + " $tuned_params"));
            }
            if (o.Has("o")) { try { var l = Find(sh, o.Get("o"), true); sh.M.V.WriteLoc(sh.P, l, cfg, false, 0x180); } catch (VfsEx e) { io.E("grub2-mkconfig: " + e.Message); return 1; } }
            else io.P(cfg);
            io.E("done");
            return 0;
        }
        static int Grub2SetDefault(Sh sh, List<string> a, Io io)
        {
            if (sh.P.Cr.Uid != 0) { io.E("grub2-set-default: Permission denied"); return 1; }
            if (a.Count < 2) { io.E("grub2-set-default: entry not specified."); return 1; }
            var files = EntryFiles(sh.M);
            string id = a[1];
            int n; if (int.TryParse(id, out n) && n < files.Count) id = Vfs.Base(files[n]).Replace(".conf", "");
            string env = sh.M.V.Cat("/boot/grub2/grubenv") ?? "";
            env = Regex.IsMatch(env, "(?m)^saved_entry=") ? Regex.Replace(env, "(?m)^saved_entry=.*$", "saved_entry=" + id) : "saved_entry=" + id + "\n" + env;
            sh.M.SysWrite("/boot/grub2/grubenv", env);
            return 0;
        }
        static int Grub2Editenv(Sh sh, List<string> a, Io io)
        {
            string env = sh.M.V.Cat("/boot/grub2/grubenv") ?? "";
            if (a.Contains("list")) { foreach (var l in env.Split('\n')) if (l.Contains("=") && !l.StartsWith("#", StringComparison.Ordinal)) io.L(l); return 0; }
            return 0;
        }

        // ------------------------------------------------------------ chroot, env, sleep, which, bash
        static int Chroot(Sh sh, List<string> a, Io io)
        {
            if (sh.P.Cr.Uid != 0) { io.E("chroot: cannot change root directory to '" + (a.Count > 1 ? a[1] : "") + "': Operation not permitted"); return 125; }
            if (a.Count < 2) { io.E("chroot: missing operand\nTry 'chroot --help' for more information."); return 125; }
            Loc l;
            try { l = Find(sh, a[1], true); } catch (VfsEx e) { io.E("chroot: cannot change root directory to '" + a[1] + "': " + e.Message); return 125; }
            if (!l.Exists || l.Node.T != 'd') { io.E("chroot: cannot change root directory to '" + a[1] + "': No such file or directory"); return 125; }
            var ctx = sh.P.Copy();
            ctx.Root = l.GPath;
            ctx.Cwd = "/";
            var child = new Sh(sh.M, sh.S, ctx) { Host = sh.Host == "switch_root" ? "" : sh.Host };
            foreach (var kv in sh.Vars) child.Vars[kv.Key] = kv.Value.Copy();
            child.SetVar("PS1", "sh-5.1# ");
            child.SetVar("PATH", "/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin");
            child.Io = io;
            if (a.Count > 2) { try { return child.Run(a.Skip(2).ToList(), io); } catch (ExitEx e) { return e.Code; } }
            if (sh.S == null) return 0;
            sh.S.ShStack.Add(child);
            try { return child.Repl(); } finally { sh.S.ShStack.Remove(child); }
        }

        static int EnvCmd(Sh sh, List<string> a, Io io)
        {
            int i = 1;
            var extra = new Dictionary<string, string>();
            bool clear = false;
            for (; i < a.Count; i++)
            {
                if (a[i] == "-i" || a[i] == "-") { clear = true; continue; }
                if (a[i] == "-u" && i + 1 < a.Count) { extra[a[++i]] = null; continue; }
                if (a[i].Contains("=") && ShParser.IsAssign(a[i])) { int eq = a[i].IndexOf('='); extra[a[i].Substring(0, eq)] = a[i].Substring(eq + 1); continue; }
                break;
            }
            if (i >= a.Count)
            {
                var env = clear ? new Dictionary<string, string>() : sh.Env();
                foreach (var kv in extra) { if (kv.Value == null) env.Remove(kv.Key); else env[kv.Key] = kv.Value; }
                foreach (var kv in env.OrderBy(x => x.Key, StringComparer.Ordinal)) io.L(kv.Key + "=" + kv.Value);
                return 0;
            }
            var sub = sh.Subshell();
            foreach (var kv in extra) { if (kv.Value == null) sub.Unset(kv.Key); else { sub.SetVar(kv.Key, kv.Value); sub.GetVarObj(kv.Key).Export = true; } }
            return sub.Run(a.Skip(i).ToList(), io);
        }
        static int Printenv(Sh sh, List<string> a, Io io)
        {
            var env = sh.Env();
            if (a.Count == 1) { foreach (var kv in env.OrderBy(x => x.Key, StringComparer.Ordinal)) io.L(kv.Key + "=" + kv.Value); return 0; }
            int st = 0;
            foreach (var k in a.Skip(1)) { string v; if (env.TryGetValue(k, out v)) io.L(v); else st = 1; }
            return st;
        }
        static int Sleep(Sh sh, List<string> a, Io io)
        {
            if (a.Count < 2) { io.E("sleep: missing operand\nTry 'sleep --help' for more information."); return 1; }
            double total = 0;
            foreach (var x in a.Skip(1))
            {
                if (x == "infinity") { if (sh.Background) { sh.LongRunning = true; return 0; } total = double.MaxValue; break; }
                var mm = Regex.Match(x, "^(\\d+(\\.\\d+)?)([smhd]?)$");
                if (!mm.Success) { io.E("sleep: invalid time interval '" + x + "'\nTry 'sleep --help' for more information."); return 1; }
                double v = double.Parse(mm.Groups[1].Value, CultureInfo.InvariantCulture);
                switch (mm.Groups[3].Value) { case "m": v *= 60; break; case "h": v *= 3600; break; case "d": v *= 86400; break; }
                total += v;
            }
            if (sh.Background) { sh.BgSeconds += (long)Math.Ceiling(total); if (sh.BgComm == null) { sh.BgComm = "sleep"; sh.BgCmd = string.Join(" ", a.ToArray()); } return 0; }
            if (total == double.MaxValue || total > 3600 && sh.S != null && io.Out.IsTty)
            {
                if (sh.S == null) return 0;
                try { sh.S.WaitForBreak(); } catch (BreakSignal) { return 130; }
                return 0;
            }
            sh.M.Net.Advance((long)Math.Ceiling(total));
            return 0;
        }
        /// <summary>timeout [-s SIG] DURATION COMMAND...: 124 when the command would still be running (sleep, ping, tail -f...).</summary>
        static int TimeoutCmd(Sh sh, List<string> a, Io io)
        {
            int i = 1;
            while (i < a.Count && a[i].StartsWith("-", StringComparison.Ordinal)) { if ((a[i] == "-s" || a[i] == "--signal" || a[i] == "-k" || a[i] == "--kill-after") && i + 1 < a.Count) i++; i++; }
            if (a.Count - i < 2) { io.E("timeout: missing operand\nTry 'timeout --help' for more information."); return 125; }
            long limit = ParseDuration(a[i]);
            if (limit < 0) { io.E("timeout: invalid time interval '" + a[i] + "'"); return 125; }
            var cmd = a.Skip(i + 1).ToList();
            long need = -1;
            if (cmd[0] == "sleep" && cmd.Count > 1) need = cmd.Skip(1).Sum(x => Math.Max(0, ParseDuration(x)));
            if ((cmd[0] == "ping" && !cmd.Contains("-c")) || cmd[0] == "yes" || (cmd[0] == "tail" && (cmd.Contains("-f") || cmd.Contains("-F")))) need = long.MaxValue;
            if (limit > 0 && need > limit)
            {
                sh.M.Net.Advance(limit);
                return 124;
            }
            return sh.Run(cmd, io);
        }
        static long ParseDuration(string s)
        {
            var mm = Regex.Match(s, "^(\\d+(?:\\.\\d+)?)([smhd]?)$");
            if (!mm.Success) return -1;
            double v = double.Parse(mm.Groups[1].Value, CultureInfo.InvariantCulture);
            switch (mm.Groups[2].Value) { case "m": v *= 60; break; case "h": v *= 3600; break; case "d": v *= 86400; break; }
            return (long)Math.Ceiling(v);
        }

        static int Which(Sh sh, List<string> a, Io io)
        {
            int st = 0;
            foreach (var n in a.Skip(1).Where(x => !x.StartsWith("-", StringComparison.Ordinal)))
            {
                string shown, al;
                // like RHEL's which alias (which --read-alias): an interactive shell's alias comes first
                bool hasAlias = sh.Interactive && sh.Aliases.TryGetValue(n, out al);
                if (hasAlias) io.L("alias " + n + "='" + sh.Aliases[n] + "'");
                var l = sh.FindProgram(n, out shown);
                if (l == null) { if (!hasAlias) { io.E("/usr/bin/which: no " + n + " in (" + (sh.GetVar("PATH") ?? "") + ")"); st = 1; } continue; }
                io.L((hasAlias ? "\t" : "") + shown);
            }
            return st;
        }
        static int BashCmd(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "c:xnelivs", "login,norc,noprofile,posix,rcfile=,version", a[0], true);
            if (o.Has("version")) { io.L("GNU bash, version 5.1.8(1)-release (x86_64-redhat-linux-gnu)\nCopyright (C) 2020 Free Software Foundation, Inc.\nLicense GPLv3+: GNU GPL version 3 or later <http://gnu.org/licenses/gpl.html>\n\nThis is free software; you are free to change and redistribute it.\nThere is NO WARRANTY, to the extent permitted by law."); return 0; }
            if (o.Has("c"))
            {
                var child = sh.Child(null);
                child.Arg0 = o.Args.Count > 0 ? o.Args[0] : a[0];
                child.Pos = o.Args.Skip(1).ToList();
                child.XTrace = o.Has("x");
                try { return child.RunText(o.Get("c"), io); } catch (ExitEx e) { return e.Code; } finally { sh.Steps = child.Steps; }
            }
            if (o.Args.Count > 0)
            {
                Loc l;
                try { l = Find(sh, o.Args[0], true); } catch (VfsEx e) { io.E(a[0] + ": " + o.Args[0] + ": " + e.Message); return 127; }
                if (!l.Exists) { io.E(a[0] + ": " + o.Args[0] + ": No such file or directory"); return 127; }
                if (!sh.M.V.Can(sh.P.Cr, l.Node, 4)) { io.E(a[0] + ": " + o.Args[0] + ": Permission denied"); return 126; }
                if (o.Has("n")) { try { ShParser.Parse(l.Node.Text ?? ""); return 0; } catch (ShIncomplete) { io.E(o.Args[0] + ": line " + (l.Node.Text ?? "").Split('\n').Length + ": syntax error: unexpected end of file"); return 2; } catch (ShSyntax e) { io.E(o.Args[0] + ": " + e.Message); return 2; } }
                var child = sh.Child(null);
                child.Arg0 = o.Args[0];
                child.Pos = o.Args.Skip(1).ToList();
                child.XTrace = o.Has("x");
                child.ErrExit = o.Has("e");
                int st;
                try { st = child.RunText(l.Node.Text ?? "", io); } catch (ExitEx e) { st = e.Code; }
                finally { child.RunTrap("EXIT"); sh.Steps = child.Steps; }
                if (child.BgSeconds > 0) sh.BgSeconds += child.BgSeconds;
                if (child.LongRunning) sh.LongRunning = true;
                return st;
            }
            // an interactive bash inside bash
            if (sh.S == null) { var child0 = sh.Child(null); return child0.RunText(io.In.ReadAll(), io); }
            var child2 = sh.Child(null);
            foreach (var kv in sh.Aliases) child2.Aliases[kv.Key] = kv.Value;
            child2.SetVar("PS1", sh.GetVar("PS1") ?? "\\$ ");
            sh.S.ShStack.Add(child2);
            try { return child2.Repl(); } finally { sh.S.ShStack.Remove(child2); }
        }
    }

    public partial class Machine
    {
        public long ShutdownAt = -1;
        public bool ShutdownReboot;
    }
}
