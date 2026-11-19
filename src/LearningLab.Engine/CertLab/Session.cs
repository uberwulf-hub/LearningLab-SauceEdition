// Cert Lab - a terminal session: the machine's console (tty1) or a second terminal. Everything typed is handed
// to a worker thread that runs the console's programs (GRUB menu, login, the shell, su, ssh, fdisk, passwd...) as
// ordinary code that asks for its next line. Only one thread ever runs at a time: the caller waits until the worker
// asks for input again (or ends), so the simulation stays deterministic and needs no locks.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;

namespace CertLab
{
    public class SessionEnd : Exception { }
    public class MachineKick : Exception { public Machine Target; public string Why; public MachineKick(Machine m, string w) { Target = m; Why = w; } }

    public partial class Session
    {
        public Machine M;              // the machine this terminal belongs to (its console)
        public string Tty = "tty1";
        public bool IsConsole = true;
        public string AutoLogin;       // log this user in once without asking (exams, lessons)
        readonly StringBuilder outBuf = new StringBuilder();
        readonly object outLock = new object();
        Thread worker;
        readonly SemaphoreSlim toWorker = new SemaphoreSlim(0), toCaller = new SemaphoreSlim(0);
        volatile bool waiting, finished, aborting, started;
        string prompt = "", provided;
        bool secret, eof, brk, waitBreakOnly;
        public volatile bool BreakRequested;
        public GrubScreen Grub;         // the GRUB menu or its editor is on the screen (keys go to it, not to a line)
        readonly List<MachineKick> kicks = new List<MachineKick>();
        public readonly List<Machine> Remote = new List<Machine>();   // ssh: the machines this terminal is logged in to
        public readonly List<Sh> ShStack = new List<Sh>();
        public bool ClearRequested;
        public Func<Session, string, string, bool, string> EditorHook;   // (session, path, text, readOnly) -> new text or null; the window's vi / nano
        public EditRequest PendingEdit;
        public int Lines;
        public long SudoUntil; public string SudoUser = "";

        public Session(Machine m) { M = m; }

        public string Prompt { get { return waiting && !waitBreakOnly ? prompt : ""; } }
        public bool Secret { get { return waiting && secret; } }
        public bool Waiting { get { return waiting; } }
        public bool Busy { get { return started && !waiting && !finished; } }
        public bool Finished { get { return finished; } }
        public Sh CurrentSh { get { return ShStack.Count > 0 ? ShStack[ShStack.Count - 1] : null; } }
        public Machine Where { get { return Remote.Count > 0 ? Remote[Remote.Count - 1] : M; } }

        public void Start()
        {
            if (started) return;
            started = true;
            if (!M.Sessions.Contains(this)) M.Sessions.Add(this);
            worker = new Thread(Main) { IsBackground = true, Name = "certlab-" + M.Name + "-" + Tty };
            worker.Start();
            toCaller.Wait();
        }

        void Main()
        {
            try { Console_(); }
            catch (SessionEnd) { }
            catch (Exception e) { Output("\n[Cert Lab: the terminal stopped because of a problem: " + e.GetType().Name + ": " + e.Message + "]\n"); }
            finished = true;
            waiting = false;
            toCaller.Release();
        }

        public void Output(string s)
        {
            lock (outLock) { outBuf.Append(s); }
        }
        public string TakeOutput()
        {
            lock (outLock) { string s = outBuf.ToString(); outBuf.Clear(); return s; }
        }

        // ------------------------------------------------------------ the hand-over between the caller and the worker
        /// <summary>The worker asks for a line. Returns null for Ctrl+D; throws BreakSignal for Ctrl+C.</summary>
        public string ReadLine(string p, bool sec)
        {
            CheckKicks();
            prompt = p ?? ""; secret = sec;
            Hand();
            if (brk) { brk = false; throw new BreakSignal(); }
            if (eof) { eof = false; return null; }
            string r = provided; provided = null;
            return r ?? "";
        }

        /// <summary>A program that runs until Ctrl+C (ping without -c, tail -f, top).</summary>
        public void WaitForBreak()
        {
            CheckKicks();
            prompt = ""; secret = false; waitBreakOnly = true;
            try
            {
                for (;;)
                {
                    Hand();
                    if (brk) { brk = false; throw new BreakSignal(); }
                    if (eof) { eof = false; }
                    provided = null;
                }
            }
            finally { waitBreakOnly = false; }
        }
        public bool WaitingForBreak { get { return waiting && waitBreakOnly; } }

        void Hand()
        {
            waiting = true;
            toCaller.Release();
            toWorker.Wait();
            waiting = false;
            if (aborting) throw new SessionEnd();
            CheckKicks();
        }

        void CheckKicks()
        {
            MachineKick k = null;
            lock (kicks) { if (kicks.Count > 0) { k = kicks[0]; kicks.RemoveAt(0); } }
            if (k != null) throw k;
        }

        void Resume()
        {
            if (!started || finished) return;
            toWorker.Release();
            toCaller.Wait();
        }

        public void Execute(string line)
        {
            if (!started) Start();
            if (!waiting) return;
            if (waitBreakOnly) { Output(line + "\n"); }
            provided = line;
            Lines++;
            Resume();
        }
        public void Break()
        {
            if (!started || finished) return;
            if (waiting) { brk = true; Resume(); }
            else BreakRequested = true;
        }
        public void Eof()
        {
            if (!started || finished || !waiting) return;
            eof = true; Resume();
        }

        /// <summary>The machine (or one logged in to with ssh) rebooted or went away: unwind to whoever handles it.</summary>
        public void Kick(Machine m, string why)
        {
            lock (kicks) { kicks.Add(new MachineKick(m, why)); }
            if (waiting && Thread.CurrentThread != worker) Resume();
        }

        public void Dispose()
        {
            aborting = true;
            if (waiting) { try { toWorker.Release(); } catch (Exception) { } }
            M.Sessions.Remove(this);
        }

        // ------------------------------------------------------------ the console
        void Console_()
        {
            for (;;)
            {
                try
                {
                    switch (M.State)
                    {
                        case "off":
                            Output("\n[" + M.Name + " is switched off. Press Enter to switch it on.]\n");
                            ReadLine("", false);
                            M.PowerOn();
                            break;
                        case "grub": GrubLoop(); break;
                        case "emergency": case "rescue": Emergency(); break;
                        case "rdbreak": RdBreak(); break;
                        case "initbash": InitBash(); break;
                        default: LoginLoop(); break;
                    }
                }
                catch (MachineKick k)
                {
                    Remote.Clear(); ShStack.Clear();
                    if (k.Why != null) Output(k.Why);
                }
                catch (BreakSignal) { Output("^C\n"); }
            }
        }

        /// <summary>The GRUB screen: the worker waits while the caller's keys change it (GrubKey / GrubType /
        /// GrubTick, never a typed line), then boots what it says.</summary>
        void GrubLoop()
        {
            var g = new GrubScreen(M);
            Grub = g;
            try { while (!g.Done) ReadLine("", false); }
            finally { Grub = null; }
            if (g.Action == "halt") { M.State = "off"; return; }
            if (g.Action != "boot") return;   // reboot: the menu again
            M.Boot(g.BootKernel, g.BootArgs, g.BootRescue);
            Output(string.Join("\n", M.BootLog.ToArray()) + (M.BootLog.Count > 0 ? "\n" : ""));
        }

        /// <summary>A key on the GRUB screen (names as GrubScreen.Key takes them). The caller's thread only.</summary>
        public bool GrubKey(string key) { var g = Grub; if (g == null || !waiting) return false; bool r = g.Key(key); GrubAfter(g); return r; }
        /// <summary>Characters typed on the GRUB screen.</summary>
        public void GrubType(string text) { var g = Grub; if (g == null || !waiting) return; g.Type(text); GrubAfter(g); }
        /// <summary>One second of the GRUB countdown.</summary>
        public void GrubTick() { var g = Grub; if (g == null || !waiting) return; g.Tick(); GrubAfter(g); }
        void GrubAfter(GrubScreen g)
        {
            if (g.Done && waiting && Grub == g) { provided = ""; Lines++; Resume(); }
        }

        void LoginLoop()
        {
            Output("\n" + M.IssueText() + "\n");
            string user;
            if (AutoLogin != null) { user = AutoLogin; AutoLogin = null; if (!LoginAs(user, true)) return; return; }
            user = ReadLine(M.ShortHost + " login: ", false);
            if (user == null) return;
            user = user.Trim();
            if (user.Length == 0) return;
            string pw = ReadLine("Password: ", true);
            if (pw == null) return;
            if (!M.Authenticate(user, pw)) { Output("Login incorrect\n"); M.Log("login", 0, 5, "FAILED LOGIN 1 FROM " + Tty + " FOR " + user + ", Authentication failure", null); return; }
            LoginAs(user, false);
        }

        public bool LoginAs(string user, bool auto)
        {
            var pw = M.User(user);
            if (pw == null) return false;
            if (pw.Shell.EndsWith("nologin", StringComparison.Ordinal) || pw.Shell.EndsWith("false", StringComparison.Ordinal)) { Output("This account is currently not available.\n"); return false; }
            if (!auto && !ForcedChange(user)) return false;
            string last = M.LastLoginText(user, Tty);
            if (last.Length > 0) Output(last + "\n");
            M.Logins.Add(user + " " + Tty + " " + M.Now);
            M.Log("systemd-logind", 900, 6, "New session of user " + user + ".", null);
            var cr = M.CredFor(user);
            var p = new PCtx { Cr = cr, Cwd = pw.Home, Root = "/" };
            try { var hl = M.V.Resolve(p, pw.Home, true); if (!hl.Exists || hl.Node.T != 'd') { Output("Could not chdir to home directory " + pw.Home + ": No such file or directory\n"); p.Cwd = "/"; } }
            catch (VfsEx) { p.Cwd = "/"; }
            var sh = new Sh(M, this, p) { LoginShell = true };
            sh.InitLogin(pw);
            sh.SetVar("PWD", p.Cwd);
            // a real login session: systemd starts the user's manager (systemctl --user works)
            sh.SetVar("XDG_RUNTIME_DIR", "/run/user/" + pw.Uid); sh.GetVarObj("XDG_RUNTIME_DIR").Export = true;
            M.StartUserManager(user);
            sh.LoadHistory();
            ShStack.Add(sh);
            try { sh.Repl(); }
            finally { ShStack.Remove(sh); sh.SaveHistory(); M.Logins.RemoveAll(x => x.StartsWith(user + " " + Tty + " ", StringComparison.Ordinal)); }
            return true;
        }

        /// <summary>chage -d 0 / passwd -e: the password must be changed at login.</summary>
        bool ForcedChange(string user)
        {
            var s = M.Shadow().FirstOrDefault(x => x.Name == user);
            if (s == null) return true;
            bool expired = s.LastChg == "0";
            long max, last;
            if (!expired && long.TryParse(s.Max, out max) && long.TryParse(s.LastChg, out last) && max >= 0 && max < 99999 && M.DaysNow > last + max) expired = true;
            if (!expired) return true;
            Output(s.LastChg == "0" ? "You are required to change your password immediately (administrator enforced).\n" : "You are required to change your password immediately (password expired).\n");
            Output("Changing password for user " + user + ".\n");
            ReadLine("Current password: ", true);
            for (int tries = 0; tries < 3; tries++)
            {
                string a = ReadLine("New password: ", true);
                string b = ReadLine("Retype new password: ", true);
                if (a == null || b == null) return false;
                if (a != b) { Output("Sorry, passwords do not match.\n"); continue; }
                if (a.Length < 8) { Output("BAD PASSWORD: The password is shorter than 8 characters\n"); }
                M.SetPassword(user, a);
                return true;
            }
            return false;
        }

        void Emergency()
        {
            bool resc = M.State == "rescue";
            Output("\nYou are in " + (resc ? "rescue" : "emergency") + " mode. After logging in, type \"journalctl -xb\" to view\nsystem logs, \"systemctl reboot\" to reboot, \"systemctl default\" or \"exit\"\nto boot into default mode.\n");
            string pw = ReadLine("Give root password for maintenance\n(or press Control-D to continue): ", true);
            if (pw == null) { M.ContinueBoot(); Output(string.Join("\n", M.BootLog.ToArray()) + "\n"); return; }
            if (!M.CheckPassword("root", pw) && !(M.Shadow().Any(x => x.Name == "root" && x.Hash.StartsWith("!", StringComparison.Ordinal)))) { Output("Login incorrect\n"); return; }
            var p = new PCtx { Cr = M.CredFor("root"), Cwd = "/root" };
            var sh = new Sh(M, this, p) { LoginShell = true };
            sh.InitLogin(M.User("root"));
            ShStack.Add(sh);
            int st;
            try { st = sh.Repl(); }
            finally { ShStack.Remove(sh); }
            if (M.State == "emergency" || M.State == "rescue") { M.ContinueBoot(); Output(string.Join("\n", M.BootLog.ToArray()) + "\n"); }
        }

        void RdBreak()
        {
            Output("\nGenerating \"/run/initramfs/rdsosreport.txt\"\n\n\nEntering emergency mode. Exit the shell to continue.\nType \"journalctl\" to view system logs.\nYou might want to save \"/run/initramfs/rdsosreport.txt\" to a USB stick or /boot\nafter mounting them and attach it to a bug report.\n\n\n");
            string l = ReadLine("Press Enter for maintenance\n(or press Control-D to continue): ", false);
            if (l == null) { M.ContinueBoot(); Output(string.Join("\n", M.BootLog.ToArray()) + "\n"); return; }
            var p = new PCtx { Cr = new Cred(), Cwd = "/", Root = "/", Domain = "kernel_t" };
            var sh = new Sh(M, this, p) { Host = "switch_root" };
            sh.SetVar("PS1", "switch_root:\\w# ");
            sh.SetVar("PATH", "/usr/sbin:/usr/bin:/sbin:/bin");
            sh.SetVar("HOME", "/");
            ShStack.Add(sh);
            try { sh.Repl(); }
            finally { ShStack.Remove(sh); }
            M.ContinueBoot();
            Output(string.Join("\n", M.BootLog.ToArray()) + "\n");
        }

        void InitBash()
        {
            var p = new PCtx { Cr = new Cred(), Cwd = "/", Root = "/", Domain = "kernel_t" };
            var sh = new Sh(M, this, p);
            sh.SetVar("PS1", "bash-5.1# ");
            sh.SetVar("PATH", "/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin");
            sh.SetVar("HOME", "/");
            Output("bash: cannot set terminal process group (-1): Inappropriate ioctl for device\nbash: no job control in this shell\n");
            ShStack.Add(sh);
            try { sh.Repl(); }
            catch (ExitEx) { }
            finally { ShStack.Remove(sh); }
            if (M.State == "initbash")
            {
                Output("[  " + (M.Now - M.BootTime) + ".123456] Kernel panic - not syncing: Attempted to kill init! exitcode=0x00000000\n");
                Output("(Cert Lab: init=/bin/bash ended, which stops a real machine. It restarts now; next time use  exec /sbin/init  to continue booting.)\n");
                M.Shutdown(true);
            }
        }

        // ------------------------------------------------------------ editors (vi, nano) through the window
        /// <summary>Opens the window's editor and waits until it closes; tests answer through EditorHook.</summary>
        public string Edit(string path, string text, bool readOnly, string kind) { return Edit(path, text, readOnly, kind, null); }
        public string Edit(string path, string text, bool readOnly, string kind, Action<string, string> onSave)
        {
            if (EditorHook != null)
            {
                var r = EditorHook(this, path, text, readOnly);
                if (r != null && onSave != null) onSave(path, r);
                return r;
            }
            var req = new EditRequest { Path = path, Text = text, ReadOnly = readOnly, Kind = kind };
            if (onSave != null) req.OnSave = t => onSave(req.Path, t);
            PendingEdit = req;
            try
            {
                for (;;)
                {
                    ReadLine("", false);
                    if (PendingEdit.Done) return PendingEdit.Saved ? PendingEdit.Result : null;
                }
            }
            finally { PendingEdit = null; }
        }
        /// <summary>The window's editor finished.</summary>
        public void EditDone(string result, bool saved)
        {
            if (PendingEdit == null) return;
            PendingEdit.Result = result; PendingEdit.Saved = saved; PendingEdit.Done = true;
            provided = "";
            if (waiting) Resume();
        }
        /// <summary>Intermediate saves (:w) while the editor stays open.</summary>
        public Action<string> SaveHook;
    }

    public class EditRequest { public string Path, Text, Kind; public bool ReadOnly, Done, Saved; public string Result; public Action<string> OnSave; }
}
