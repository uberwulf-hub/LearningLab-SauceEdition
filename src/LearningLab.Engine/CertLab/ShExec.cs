// Cert Lab - the bash-like shell, part 3: running the parsed commands (lists, pipelines, compound commands,
// functions, scripts), redirections and variables. Pipelines run one command after the other with the output of
// each one as the input of the next, which gives the same results for the commands people type in practice.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    // ------------------------------------------------------------ input and output
    public abstract class Sink
    {
        public abstract void Write(string s);
        public void Line(string s) { Write(s + "\n"); }
        public virtual void Close() { }
        public virtual bool IsTty { get { return false; } }
    }
    public class BufSink : Sink
    {
        readonly StringBuilder sb = new StringBuilder();
        public override void Write(string s) { sb.Append(s); if (sb.Length > 4000000) throw new ShError("output too large"); }
        public string Text { get { return sb.ToString(); } }
    }
    public class NullSink : Sink { public override void Write(string s) { } }
    public class TtySink : Sink
    {
        readonly Session s;
        public TtySink(Session ses) { s = ses; }
        public override void Write(string t) { if (s != null) s.Output(t); }
        public override bool IsTty { get { return s != null; } }
    }
    public class FileSink : Sink
    {
        readonly Vfs v; readonly Loc l; readonly Inode n;
        public FileSink(Vfs vfs, Loc loc, Inode node) { v = vfs; l = loc; n = node; }
        public override void Write(string s)
        {
            if (n.T == 'c') return;
            v.CheckSpace(l.Fs ?? l.PFs, Encoding.UTF8.GetByteCount(s));
            n.Text = (n.Text ?? "") + s; n.BinSize = -1; n.Mtime = v.M.Now;
        }
    }
    public class TeeSink : Sink
    {
        readonly Sink a, b;
        public TeeSink(Sink x, Sink y) { a = x; b = y; }
        public override void Write(string s) { a.Write(s); b.Write(s); }
    }

    public abstract class Src
    {
        public abstract string ReadLine();
        public virtual string ReadAll() { var sb = new StringBuilder(); string l; while ((l = ReadLine()) != null) sb.Append(l).Append('\n'); return sb.ToString(); }
        public virtual bool IsTty { get { return false; } }
    }
    public class StrSrc : Src
    {
        readonly string t; int i;
        public StrSrc(string text) { t = text ?? ""; }
        public override string ReadLine()
        {
            if (i >= t.Length) return null;
            int e = t.IndexOf('\n', i);
            string r;
            if (e < 0) { r = t.Substring(i); i = t.Length; } else { r = t.Substring(i, e - i); i = e + 1; }
            return r;
        }
        public override string ReadAll() { string r = i >= t.Length ? "" : t.Substring(i); i = t.Length; return r; }
        public bool Partial { get { return t.Length > 0 && !t.EndsWith("\n", StringComparison.Ordinal); } }
    }
    public class TtySrc : Src
    {
        readonly Session s;
        public TtySrc(Session ses) { s = ses; }
        public override string ReadLine() { return s == null ? null : s.ReadLine("", false); }
        public override bool IsTty { get { return s != null; } }
    }

    public class Io
    {
        public Src In; public Sink Out; public Sink Err;
        public Dictionary<int, object> Fds;   // extra descriptors (3..9) opened with exec
        public void P(string s) { Out.Write(s); }
        public void L(string s) { Out.Write(s + "\n"); }
        public void E(string s) { Err.Write(s + "\n"); }
        public Io Copy() { return new Io { In = In, Out = Out, Err = Err, Fds = Fds }; }
    }

    // ------------------------------------------------------------ control flow
    public class ExitEx : Exception { public int Code; public ExitEx(int c) { Code = c; } }
    public class ReturnEx : Exception { public int Code; public ReturnEx(int c) { Code = c; } }
    public class LoopEx : Exception { public int Levels; public bool Continue; public LoopEx(int n, bool c) { Levels = n; Continue = c; } }
    public class KickEx : Exception { public string Why; public KickEx(string w) : base(w) { Why = w; } }
    public class BreakSignal : Exception { }
    public class TooLong : Exception { }

    public class ShVar
    {
        public string Val; public List<string> Arr; public Dictionary<string, string> Assoc; public List<string> AssocOrder;
        public bool Export, ReadOnly, Integer;
        public ShVar Copy() { return new ShVar { Val = Val, Arr = Arr == null ? null : new List<string>(Arr), Assoc = Assoc == null ? null : new Dictionary<string, string>(Assoc), AssocOrder = AssocOrder == null ? null : new List<string>(AssocOrder), Export = Export, ReadOnly = ReadOnly, Integer = Integer }; }
    }

    public class ShJob { public int Id; public int Pid; public string Text; public bool Done; public bool Reported; }

    public partial class Sh
    {
        public Machine M;
        public Session S;
        public PCtx P;
        public Io Io;
        public Dictionary<string, ShVar> Vars = new Dictionary<string, ShVar>(StringComparer.Ordinal);
        public List<Dictionary<string, ShVar>> Locals = new List<Dictionary<string, ShVar>>();
        public Dictionary<string, ShFunc> Funcs = new Dictionary<string, ShFunc>(StringComparer.Ordinal);
        public Dictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        public List<string> Pos = new List<string>();
        public string Arg0 = "-bash";
        public int Status;
        public int CmdSubstStatus;
        public bool Interactive, ErrExit, XTrace, NoUnset, NoClobber, PipeFail, LoginShell;
        public Dictionary<string, string> Traps = new Dictionary<string, string>();
        public int Steps;
        public int MaxSteps = 200000;
        public int LastBg;
        public int Pid;
        public List<ShJob> Jobs;
        public List<string> History;
        public bool Background;           // running a job started with &
        public long BgSeconds;            // how long that job sleeps
        public bool LongRunning;          // a background job that never ends
        public int FuncDepth;
        public int OptInd = 1; int optSub;
        public bool InCondition;
        public Sh Parent;
        public string Host = "";          // prompt: for ssh sessions, the remote host's name

        public Sh(Machine m, Session s, PCtx p)
        {
            M = m; S = s; P = p;
            Pid = m.NextPid; m.NextPid += 3;
            Io = new Io { In = new TtySrc(s), Out = new TtySink(s), Err = new TtySink(s) };
            Jobs = new List<ShJob>();
            History = new List<string>();
        }

        /// <summary>A login environment for a user (HOME, USER, PATH, PS1 like RHEL's /etc/profile and bashrc).</summary>
        public void InitLogin(PwEnt pw)
        {
            P.Umask = 0x12;   // a login starts from the system default (/etc/login.defs UMASK 022), whatever the caller had
            SetVar("HOME", pw.Home); SetVar("USER", pw.Name); SetVar("LOGNAME", pw.Name); SetVar("SHELL", pw.Shell);
            SetVar("PATH", pw.Uid == 0 ? "/root/.local/bin:/root/bin:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin" : "/home/" + pw.Name + "/.local/bin:/home/" + pw.Name + "/bin:/usr/local/bin:/usr/bin:/usr/local/sbin:/usr/sbin");
            SetVar("HOSTNAME", M.HostnameNow); SetVar("LANG", "en_US.UTF-8"); SetVar("TERM", "xterm-256color"); SetVar("HISTSIZE", "1000");
            SetVar("MAIL", "/var/spool/mail/" + pw.Name);
            SetVar("PS1", "[\\u@\\h \\W]\\$ ");
            SetVar("PS2", "> ");
            SetVar("PWD", P.Cwd);
            SetVar("SHLVL", "1");
            SetVar("EDITOR", "vi");
            foreach (var k in new[] { "HOME", "USER", "LOGNAME", "SHELL", "PATH", "HOSTNAME", "LANG", "TERM", "MAIL", "PWD", "SHLVL", "EDITOR", "XDG_RUNTIME_DIR", "HISTSIZE" }) { var v = GetVarObj(k); if (v != null) v.Export = true; }
            Aliases["ll"] = "ls -l --color=auto"; Aliases["ls"] = "ls --color=auto"; Aliases["grep"] = "grep --color=auto";
            Aliases["l."] = "ls -d .* --color=auto";   // RHEL also aliases which to read the aliases; Cert Lab's which does that itself
            if (pw.Uid == 0) { Aliases["rm"] = "rm -i"; Aliases["cp"] = "cp -i"; Aliases["mv"] = "mv -i"; }
            // the person's own ~/.bashrc additions (aliases, exports, functions)
            var startup = new List<string>();
            try { var pd = M.V.G("/etc/profile.d"); if (pd.Exists && pd.IsDir) foreach (var k in Vfs.Sorted(pd.Node.Kids.Keys)) if (k.EndsWith(".sh", StringComparison.Ordinal)) startup.Add("/etc/profile.d/" + k); } catch (VfsEx) { }
            startup.Add(pw.Home + "/.bash_profile"); startup.Add(pw.Home + "/.bashrc");
            foreach (var f in startup)
            {
                try
                {
                    var l = M.V.Resolve(P, f, true);
                    if (!l.Exists || l.Node.T != 'f') continue;
                    string t = l.Node.Text ?? "";
                    // skip the stock lines that source other files
                    var keep = string.Join("\n", t.Split('\n').Select(x => x.TrimStart().StartsWith(". ", StringComparison.Ordinal) || x.TrimStart().StartsWith("source ", StringComparison.Ordinal) ? ":" : x).ToArray());   // sourcing other files is skipped (":" keeps if / for blocks valid)
                    var ioq = new Io { In = new StrSrc(""), Out = new NullSink(), Err = new NullSink() };
                    RunText(keep, ioq);
                }
                catch (Exception) { }
            }
            Status = 0;
        }

        public Sh Subshell()
        {
            var s = new Sh(M, S, P.Copy());
            foreach (var kv in Vars) s.Vars[kv.Key] = kv.Value.Copy();
            foreach (var sc in Locals) { var d = new Dictionary<string, ShVar>(); foreach (var kv in sc) d[kv.Key] = kv.Value.Copy(); s.Locals.Add(d); }
            foreach (var kv in Funcs) s.Funcs[kv.Key] = kv.Value;
            foreach (var kv in Aliases) s.Aliases[kv.Key] = kv.Value;
            s.Pos = new List<string>(Pos); s.Arg0 = Arg0; s.Status = Status;
            s.ErrExit = ErrExit; s.XTrace = XTrace; s.NoUnset = NoUnset; s.PipeFail = PipeFail; s.Interactive = false;
            s.Io = Io; s.Steps = Steps; s.MaxSteps = MaxSteps; s.Background = Background; s.Jobs = Jobs; s.History = History; s.Parent = this; s.Pid = Pid;
            s.FuncDepth = FuncDepth;
            return s;
        }

        /// <summary>A new process (a script): only exported variables come along.</summary>
        public Sh Child(PCtx p)
        {
            var s = new Sh(M, S, p ?? P.Copy());
            foreach (var kv in AllVars()) if (kv.Value.Export) { var c = kv.Value.Copy(); s.Vars[kv.Key] = c; }
            s.Io = Io; s.Steps = Steps; s.MaxSteps = MaxSteps; s.Background = Background; s.Parent = this;
            int lvl; if (int.TryParse(GetVar("SHLVL") ?? "1", out lvl)) { s.SetVar("SHLVL", (lvl + 1).ToString(CultureInfo.InvariantCulture)); }
            return s;
        }

        // ------------------------------------------------------------ variables
        IEnumerable<KeyValuePair<string, ShVar>> AllVars()
        {
            var d = new Dictionary<string, ShVar>(Vars);
            foreach (var sc in Locals) foreach (var kv in sc) d[kv.Key] = kv.Value;
            return d;
        }
        public ShVar GetVarObj(string name)
        {
            for (int i = Locals.Count - 1; i >= 0; i--) { ShVar v; if (Locals[i].TryGetValue(name, out v)) return v; }
            ShVar g; Vars.TryGetValue(name, out g); return g;
        }
        public string GetVar(string name)
        {
            switch (name)
            {
                case "RANDOM": return M.Rand(32768).ToString(CultureInfo.InvariantCulture);
                case "SECONDS": return (M.Now - M.BootTime).ToString(CultureInfo.InvariantCulture);
                case "PPID": return "1";
                case "UID": return P.Cr.Uid.ToString(CultureInfo.InvariantCulture);
                case "EUID": return P.Cr.Uid.ToString(CultureInfo.InvariantCulture);
                case "BASHPID": return Pid.ToString(CultureInfo.InvariantCulture);
                case "BASH_VERSION": return "5.1.8(1)-release";
                case "HOSTTYPE": return "x86_64";
                case "OSTYPE": return "linux-gnu";
                case "OPTIND": return OptInd.ToString(CultureInfo.InvariantCulture);
            }
            var v = GetVarObj(name);
            if (v == null) return null;
            if (v.Arr != null) return v.Arr.Count > 0 ? v.Arr[0] : null;
            if (v.Assoc != null) { string a; return v.Assoc.TryGetValue("0", out a) ? a : null; }
            return v.Val;
        }
        public void SetVar(string name, string value)
        {
            if (name == "OPTIND") { int n; if (int.TryParse(value, out n)) { OptInd = n; optSub = 0; } }
            var v = GetVarObj(name);
            if (v != null && v.ReadOnly) throw new ShError(name + ": readonly variable");
            if (v == null) { v = new ShVar(); Vars[name] = v; }
            if (v.Integer) value = Arith(value).ToString(CultureInfo.InvariantCulture);
            if (v.Arr != null) { if (v.Arr.Count == 0) v.Arr.Add(value); else v.Arr[0] = value; return; }
            v.Val = value;
            if (name == "PWD") { }
        }
        public void Unset(string name)
        {
            for (int i = Locals.Count - 1; i >= 0; i--) if (Locals[i].ContainsKey(name)) { if (Locals[i][name].ReadOnly) throw new ShError(name + ": cannot unset: readonly variable"); Locals[i].Remove(name); return; }
            ShVar g;
            if (Vars.TryGetValue(name, out g) && g.ReadOnly) throw new ShError(name + ": cannot unset: readonly variable");
            Vars.Remove(name);
        }
        public List<string> GetArray(string name)
        {
            var v = GetVarObj(name);
            if (v == null) return null;
            if (v.Arr != null) return v.Arr.Where(x => x != null).ToList();
            if (v.Assoc != null) return v.AssocOrder.Select(k => v.Assoc[k]).ToList();
            return null;
        }
        public List<string> ArrayKeys(string name)
        {
            var v = GetVarObj(name);
            var l = new List<string>();
            if (v == null) return l;
            if (v.Arr != null) { for (int i = 0; i < v.Arr.Count; i++) if (v.Arr[i] != null) l.Add(i.ToString(CultureInfo.InvariantCulture)); }
            else if (v.Assoc != null) l.AddRange(v.AssocOrder);
            else if (v.Val != null) l.Add("0");
            return l;
        }
        public string ArrayGet(string name, string index)
        {
            var v = GetVarObj(name);
            if (v == null) return null;
            if (v.Assoc != null) { string a; return v.Assoc.TryGetValue(ExpandOne(index), out a) ? a : null; }
            long i = Arith(index);
            if (v.Arr == null) return i == 0 ? v.Val : null;
            if (i < 0) i += v.Arr.Count;
            return i >= 0 && i < v.Arr.Count ? v.Arr[(int)i] : null;
        }
        public void ArraySet(string name, string index, string value)
        {
            var v = GetVarObj(name);
            if (v != null && v.ReadOnly) throw new ShError(name + ": readonly variable");
            if (v == null) { v = new ShVar { Arr = new List<string>() }; Vars[name] = v; }
            if (v.Assoc != null) { string k = ExpandOne(index); if (!v.Assoc.ContainsKey(k)) v.AssocOrder.Add(k); v.Assoc[k] = value; return; }
            if (v.Arr == null) { v.Arr = new List<string>(); if (v.Val != null) v.Arr.Add(v.Val); v.Val = null; }
            long i = Arith(index);
            if (i < 0) i += v.Arr.Count;
            if (i < 0 || i > 100000) throw new ShError(name + "[" + index + "]: bad array subscript");
            while (v.Arr.Count <= i) v.Arr.Add(null);
            v.Arr[(int)i] = value;
        }
        public void SetArray(string name, List<string> values, bool assoc)
        {
            var v = GetVarObj(name);
            if (v != null && v.ReadOnly) throw new ShError(name + ": readonly variable");
            if (v == null) { v = new ShVar(); Vars[name] = v; }
            v.Val = null;
            if (assoc || v.Assoc != null)
            {
                v.Assoc = new Dictionary<string, string>(); v.AssocOrder = new List<string>(); v.Arr = null;
                foreach (var it in values)
                {
                    var m = System.Text.RegularExpressions.Regex.Match(it, "^\\[([^\\]]*)\\]=(.*)$", System.Text.RegularExpressions.RegexOptions.Singleline);
                    if (m.Success) { if (!v.Assoc.ContainsKey(m.Groups[1].Value)) v.AssocOrder.Add(m.Groups[1].Value); v.Assoc[m.Groups[1].Value] = m.Groups[2].Value; }
                }
                return;
            }
            v.Arr = new List<string>();
            foreach (var it in values)
            {
                var m = System.Text.RegularExpressions.Regex.Match(it, "^\\[([^\\]]*)\\]=(.*)$", System.Text.RegularExpressions.RegexOptions.Singleline);
                if (m.Success) { long i = Arith(m.Groups[1].Value); while (v.Arr.Count <= i) v.Arr.Add(null); v.Arr[(int)i] = m.Groups[2].Value; }
                else v.Arr.Add(it);
            }
        }

        public Dictionary<string, string> Env()
        {
            var d = new Dictionary<string, string>();
            foreach (var kv in AllVars()) if (kv.Value.Export && kv.Value.Val != null) d[kv.Key] = kv.Value.Val;
            return d;
        }

        // ------------------------------------------------------------ running text
        public int RunText(string text, Io io)
        {
            ShList list;
            try { list = ShParser.Parse(text); }
            catch (ShIncomplete) { io.E("bash: syntax error: unexpected end of file"); Status = 2; return 2; }
            catch (ShSyntax e) { io.E("bash: " + e.Message); Status = 2; return 2; }
            var save = Io; Io = io;
            try { return Exec(list); }
            catch (ShError e) { io.E("bash: " + e.Message); Status = e.Status; return Status; }
            finally { Io = save; }
        }

        void Step()
        {
            Steps++;
            if (Steps > MaxSteps) throw new TooLong();
            if (S != null && S.BreakRequested) { S.BreakRequested = false; throw new BreakSignal(); }
        }

        public int Exec(ShNode n)
        {
            if (n == null) return Status;
            var list = n as ShList;
            if (list != null)
            {
                for (int i = 0; i < list.Items.Count; i++)
                {
                    if (list.Background[i]) RunBackground(list.Items[i]);
                    else Status = Exec(list.Items[i]);
                }
                return Status;
            }
            var ao = n as ShAndOr;
            if (ao != null)
            {
                bool saveCond = InCondition;
                int st = 0;
                for (int i = 0; i < ao.Parts.Count; i++)
                {
                    if (i > 0)
                    {
                        string op = ao.Ops[i - 1];
                        if (op == "&&" && st != 0) continue;
                        if (op == "||" && st == 0) continue;
                    }
                    InCondition = i < ao.Parts.Count - 1 || saveCond;
                    st = Exec(ao.Parts[i]);
                    InCondition = saveCond;
                }
                Status = st;
                return st;
            }
            var pipe = n as ShPipe;
            if (pipe != null) return ExecPipe(pipe);
            var simple = n as ShSimple;
            if (simple != null) return ExecSimple(simple);
            var comp = n as ShCompound;
            if (comp != null)
            {
                var closers = new List<Sink>();
                Io saved = Io;
                try
                {
                    if (comp.Redirs.Count > 0) Io = Redirect(comp.Redirs, Io, closers);
                    return ExecCompound(comp);
                }
                finally { Io = saved; }
            }
            var fn = n as ShFunc;
            if (fn != null) { Funcs[fn.Name] = fn; Status = 0; return 0; }
            return 0;
        }

        int ExecPipe(ShPipe p)
        {
            Step();
            int st = 0;
            if (p.Cmds.Count == 1)
            {
                st = Exec(p.Cmds[0]);
                SetArray("PIPESTATUS", new List<string> { st.ToString(CultureInfo.InvariantCulture) }, false);
            }
            else
            {
                Io saved = Io;
                Src input = Io.In;
                var stats = new List<string>();
                int worst = 0;
                try
                {
                    for (int i = 0; i < p.Cmds.Count; i++)
                    {
                        bool last = i == p.Cmds.Count - 1;
                        BufSink buf = last ? null : new BufSink();
                        Io = new Io { In = input, Out = last ? saved.Out : buf, Err = saved.Err, Fds = saved.Fds };
                        int s1;
                        if (i == 0 || last) s1 = Exec(p.Cmds[i]);
                        else s1 = Exec(p.Cmds[i]);
                        stats.Add(s1.ToString(CultureInfo.InvariantCulture));
                        if (s1 != 0) worst = s1;
                        st = s1;
                        if (!last) input = new StrSrc(buf.Text);
                    }
                }
                finally { Io = saved; }
                SetArray("PIPESTATUS", stats, false);
                if (PipeFail && worst != 0) st = worst;
            }
            if (p.Negate) st = st == 0 ? 1 : 0;
            Status = st;
            if (ErrExit && st != 0 && !InCondition && !p.Negate) throw new ExitEx(st);
            return st;
        }

        int ExecCompound(ShCompound c)
        {
            var ifn = c as ShIf;
            if (ifn != null)
            {
                for (int i = 0; i < ifn.Conds.Count; i++)
                {
                    bool save = InCondition; InCondition = true;
                    int cs; try { cs = Exec(ifn.Conds[i]); } finally { InCondition = save; }
                    if (cs == 0) return Exec(ifn.Bodies[i]);
                }
                if (ifn.Else != null) return Exec(ifn.Else);
                Status = 0;
                return 0;
            }
            var fo = c as ShFor;
            if (fo != null)
            {
                int st = 0;
                if (fo.CCond != null)
                {
                    if (fo.CInit.Trim().Length > 0) Arith(fo.CInit);
                    for (;;)
                    {
                        Step();
                        if (fo.CCond.Trim().Length > 0 && Arith(fo.CCond) == 0) break;
                        try { st = Exec(fo.Body); }
                        catch (LoopEx le) { if (le.Levels > 1) { le.Levels--; throw; } if (!le.Continue) break; }
                        if (fo.CStep.Trim().Length > 0) Arith(fo.CStep);
                    }
                    Status = st; return st;
                }
                var items = fo.Words == null ? new List<string>(Pos) : ExpandWords(fo.Words);
                foreach (var it in items)
                {
                    Step();
                    SetVar(fo.Var, it);
                    try { st = Exec(fo.Body); }
                    catch (LoopEx le) { if (le.Levels > 1) { le.Levels--; throw; } if (!le.Continue) break; }
                }
                Status = st; return st;
            }
            var wh = c as ShWhile;
            if (wh != null)
            {
                int st = 0;
                for (;;)
                {
                    Step();
                    bool save = InCondition; InCondition = true;
                    int cs; try { cs = Exec(wh.Cond); } finally { InCondition = save; }
                    if (wh.Until ? cs == 0 : cs != 0) break;
                    try { st = Exec(wh.Body); }
                    catch (LoopEx le) { if (le.Levels > 1) { le.Levels--; throw; } if (!le.Continue) break; }
                }
                Status = st; return st;
            }
            var cs2 = c as ShCase;
            if (cs2 != null)
            {
                string w = ExpandOne(cs2.Word);
                bool fall = false;
                for (int i = 0; i < cs2.Patterns.Count; i++)
                {
                    bool match = fall || cs2.Patterns[i].Any(p => GlobMatch(ExpandPattern(p), w));
                    if (!match) continue;
                    int st = Exec(cs2.Bodies[i]);
                    string end = cs2.Ends[i];
                    if (end == ";&") { fall = true; continue; }
                    fall = false;
                    if (end == ";;&") continue;
                    return st;
                }
                Status = 0; return 0;
            }
            var g = c as ShGroup;
            if (g != null)
            {
                if (!g.Subshell) return Exec(g.Body);
                var sub = Subshell();
                sub.Io = Io;
                int st;
                try { st = sub.Exec(g.Body); }
                catch (ExitEx e) { st = e.Code; }
                Steps = sub.Steps;
                Status = st; return st;
            }
            var cond = c as ShCond;
            if (cond != null) { Step(); int st = CondEval(cond.Words) ? 0 : 1; Status = st; if (XTrace) Io.Err.Write("+ [[ " + string.Join(" ", cond.Words.Select(x => x.TrimStart('\u0001')).ToArray()) + " ]]\n"); return st; }
            var ar = c as ShArith;
            if (ar != null) { Step(); long v = Arith(ar.Expr); Status = v != 0 ? 0 : 1; return Status; }
            return 0;
        }

        // ------------------------------------------------------------ simple commands
        int ExecSimple(ShSimple sc)
        {
            Step();
            var words = new List<string>(sc.Words);
            // aliases (interactive shells, the first word)
            if (Interactive && words.Count > 0)
            {
                string a;
                var seen = new HashSet<string>();
                while (Aliases.TryGetValue(words[0], out a) && seen.Add(words[0]))
                {
                    var aw = AliasWords(a);
                    words.RemoveAt(0);
                    words.InsertRange(0, aw);
                    if (words.Count == 0 || !a.EndsWith(" ", StringComparison.Ordinal)) break;
                }
            }
            List<string> argv;
            try { argv = ExpandWords(words); }
            catch (ShError e) { Io.E("bash: " + e.Message); Status = 1; if (!Interactive) throw new ExitEx(1); return 1; }
            var closers = new List<Sink>();
            Io saved = Io;
            try
            {
                try { Io = Redirect(sc.Redirs, Io, closers); }
                catch (ShError e) { Io = saved; Io.E("bash: " + e.Message); Status = 1; return 1; }
                if (argv.Count == 0)
                {
                    foreach (var a in sc.Assigns) DoAssign(a, false);
                    Status = CmdSubstStatus; CmdSubstStatus = 0;
                    if (sc.Assigns.Count == 0) Status = 0;
                    return Status;
                }
                if (XTrace) Io.Err.Write("+ " + string.Join(" ", sc.Assigns.Concat(argv.Select(Quote)).ToArray()) + "\n");
                // assignments before a command: only for that command
                Dictionary<string, ShVar> temp = null;
                if (sc.Assigns.Count > 0)
                {
                    temp = new Dictionary<string, ShVar>();
                    foreach (var a in sc.Assigns)
                    {
                        int eq = a.IndexOf('=');
                        temp[a.Substring(0, eq)] = new ShVar { Val = ExpandOne(a.Substring(eq + 1)), Export = true };
                    }
                    Locals.Add(temp);
                }
                try
                {
                    int st = Run(argv, Io);
                    Status = st;
                    return st;
                }
                finally { if (temp != null) Locals.Remove(temp); }
            }
            finally { foreach (var c in closers) c.Close(); Io = saved; }
        }

        List<string> AliasWords(string text)
        {
            try
            {
                var l = ShParser.Parse(text);
                if (l.Items.Count == 1)
                {
                    var p = l.Items[0] as ShPipe;
                    if (p != null && p.Cmds.Count == 1 && p.Cmds[0] is ShSimple) return new List<string>(((ShSimple)p.Cmds[0]).Words);
                }
            }
            catch (Exception) { }
            return text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        public static string Quote(string s)
        {
            if (s.Length == 0) return "''";
            if (s.All(c => char.IsLetterOrDigit(c) || "-_./=:,+@%^".IndexOf(c) >= 0)) return s;
            return "'" + s.Replace("'", "'\\''") + "'";
        }

        public void DoAssign(string a, bool local)
        {
            int eq = a.IndexOf('=');
            string name = a.Substring(0, eq);
            string val = a.Substring(eq + 1);
            bool append = name.EndsWith("+", StringComparison.Ordinal);
            if (append) name = name.Substring(0, name.Length - 1);
            int br = name.IndexOf('[');
            if (br > 0)
            {
                string idx = name.Substring(br + 1, name.Length - br - 2);
                name = name.Substring(0, br);
                string v = ExpandOne(val);
                if (append) v = (ArrayGet(name, idx) ?? "") + v;
                ArraySet(name, idx, v);
                return;
            }
            if (val.StartsWith("(", StringComparison.Ordinal) && val.EndsWith(")", StringComparison.Ordinal))
            {
                var inner = val.Substring(1, val.Length - 2);
                var items = new List<string>();
                try
                {
                    var lx = new ShLexer(inner);
                    for (;;) { var t = lx.Next(); if (t.T == "eof") break; if (t.T == "w") items.Add(t.V); }
                }
                catch (Exception) { }
                var vals = new List<string>();
                foreach (var it in items)
                {
                    var m = System.Text.RegularExpressions.Regex.Match(it, "^\\[([^\\]]*)\\]=(.*)$", System.Text.RegularExpressions.RegexOptions.Singleline);
                    if (m.Success) vals.Add("[" + ExpandOne(m.Groups[1].Value) + "]=" + ExpandOne(m.Groups[2].Value));
                    else vals.AddRange(ExpandWords(new[] { it }));
                }
                if (append) { var old = GetArray(name) ?? new List<string>(); old.AddRange(vals); vals = old; }
                if (local && Locals.Count > 0) Locals[Locals.Count - 1][name] = new ShVar();
                var existing = GetVarObj(name);
                SetArray(name, vals, existing != null && existing.Assoc != null);
                return;
            }
            string value = ExpandOne(val);
            var ov = GetVarObj(name);
            if (append) value = (ov != null && ov.Integer) ? (Arith(GetVar(name) ?? "0") + Arith(value)).ToString(CultureInfo.InvariantCulture) : (GetVar(name) ?? "") + value;
            if (local && Locals.Count > 0)
            {
                ShVar lv;
                if (!Locals[Locals.Count - 1].TryGetValue(name, out lv)) { lv = new ShVar(); Locals[Locals.Count - 1][name] = lv; }
                lv.Val = value; return;
            }
            SetVar(name, value);
            if (name == "PATH" || name == "HOME") { }
        }

        // ------------------------------------------------------------ redirections
        public Io Redirect(List<ShRedir> rs, Io baseIo, List<Sink> closers)
        {
            if (rs.Count == 0) return baseIo;
            var io = baseIo.Copy();
            foreach (var r in rs)
            {
                int fd = r.Fd;
                string op = r.Op;
                if (op == "<<" || op == "<<-")
                {
                    string body = r.HereQuoted ? r.Here : ExpandHere(r.Here ?? "");
                    SetIn(io, fd < 0 ? 0 : fd, new StrSrc(body));
                    continue;
                }
                if (op == "<<<") { SetIn(io, fd < 0 ? 0 : fd, new StrSrc(ExpandOne(r.Target) + "\n")); continue; }
                string target = ExpandOne(r.Target);
                if (op == ">&" || op == "<&")
                {
                    if (fd < 0) fd = op == ">&" ? 1 : 0;
                    if (target == "-") { if (fd == 1) io.Out = new NullSink(); else if (fd == 2) io.Err = new NullSink(); continue; }
                    int n;
                    if (int.TryParse(target, out n))
                    {
                        if (op == ">&")
                        {
                            Sink s = n == 1 ? io.Out : n == 2 ? io.Err : FdSink(io, n);
                            if (s == null) throw new ShError(n + ": Bad file descriptor");
                            if (fd == 1) io.Out = s; else if (fd == 2) io.Err = s; else SetFd(io, fd, s);
                        }
                        else { var src = n == 0 ? io.In : FdSrc(io, n); if (src == null) throw new ShError(n + ": Bad file descriptor"); SetIn(io, fd, src); }
                        continue;
                    }
                    if (op == ">&" && fd == 1) { var s = OpenOut(target, false); closers.Add(s); io.Out = s; io.Err = s; continue; }
                    throw new ShError(target + ": ambiguous redirect");
                }
                if (op == "&>" || op == "&>>") { var s = OpenOut(target, op == "&>>"); closers.Add(s); io.Out = s; io.Err = s; continue; }
                if (op == ">" || op == ">>" || op == ">|")
                {
                    if (fd < 0) fd = 1;
                    if (op == ">" && NoClobber) { try { var l = M.V.Resolve(P, target, true); if (l.Exists && l.Node.T == 'f') throw new ShError(target + ": cannot overwrite existing file"); } catch (VfsEx) { } }
                    var s = OpenOut(target, op == ">>");
                    closers.Add(s);
                    if (fd == 1) io.Out = s; else if (fd == 2) io.Err = s; else SetFd(io, fd, s);
                    continue;
                }
                if (op == "<")
                {
                    if (fd < 0) fd = 0;
                    string text;
                    try
                    {
                        if (target == "/dev/stdin") { continue; }
                        var l = M.V.Resolve(P, target, true);
                        if (!l.Exists) throw new VfsEx("ENOENT");
                        text = M.V.ReadNode(P, l);
                    }
                    catch (VfsEx e) { throw new ShError(target + ": " + e.Message); }
                    SetIn(io, fd, new StrSrc(text));
                    continue;
                }
                if (op == "<>") { var s = OpenOut(target, true); closers.Add(s); continue; }
            }
            return io;
        }

        static void SetIn(Io io, int fd, Src s) { if (fd == 0) io.In = s; else SetFd(io, fd, s); }
        static void SetFd(Io io, int fd, object o) { io.Fds = io.Fds == null ? new Dictionary<int, object>() : new Dictionary<int, object>(io.Fds); io.Fds[fd] = o; }
        static Sink FdSink(Io io, int n) { object o; if (io.Fds != null && io.Fds.TryGetValue(n, out o)) return o as Sink; return null; }
        static Src FdSrc(Io io, int n) { object o; if (io.Fds != null && io.Fds.TryGetValue(n, out o)) return o as Src; return null; }

        string ExpandHere(string body)
        {
            // parameter, command and arithmetic expansion; quotes stay as they are
            return ExpandHereText(body);
        }

        public Sink OpenOut(string target, bool append)
        {
            if (target == "/dev/null") return new NullSink();
            if (target == "/dev/stdout") return Io.Out;
            if (target == "/dev/stderr") return Io.Err;
            if (target == "/dev/tty" || target == "/dev/console") return new TtySink(S);
            try
            {
                var l = M.V.Resolve(P, target, true);
                Inode n;
                if (!l.Exists)
                {
                    if (l.Parent == null) throw new VfsEx("ENOENT");
                    n = M.V.Create(P, l, 'f', 0x1B6);
                    n.Text = "";
                }
                else
                {
                    n = l.Node;
                    if (n.T == 'd') throw new VfsEx("EISDIR");
                    if (n.T == 'c') return new NullSink();
                    if (!M.V.Can(P.Cr, n, 2)) throw new VfsEx("EACCES");
                    if (!Selinux.AllowWrite(M, P, l)) throw new VfsEx("EACCES");
                    if (l.Mnt != null && l.Mnt.Ro) throw new VfsEx("EROFS");
                    if (l.Fs.Type == "proc") return new ProcSink(M, l);
                    if (!append) { n.Text = ""; n.BinSize = -1; n.Mtime = M.Now; }
                }
                return new FileSink(M.V, l, n);
            }
            catch (VfsEx e) { throw new ShError(target + ": " + e.Message); }
        }

        // ------------------------------------------------------------ running a command
        public int Run(List<string> argv, Io io)
        {
            string name = argv[0];
            ShFunc f;
            if (Funcs.TryGetValue(name, out f)) return CallFunc(f, argv, io);
            CmdFn b;
            if (Builtins.TryGetValue(name, out b)) return Invoke(b, argv, io, name);
            return RunExternal(argv, io);
        }

        int Invoke(CmdFn fn, List<string> argv, Io io, string name)
        {
            var save = Io; Io = io;
            try { return fn(this, argv, io); }
            catch (VfsEx e) { io.E(name + ": " + e.Message); return 1; }
            catch (ShError e) { io.E(name + ": " + e.Message); return e.Status; }
            finally { Io = save; }
        }

        public int CallFunc(ShFunc f, List<string> argv, Io io)
        {
            if (FuncDepth > 200) throw new ShError(f.Name + ": maximum function nesting level exceeded (200)");
            var savePos = Pos; var saveIo = Io;
            Pos = argv.Skip(1).ToList();
            Locals.Add(new Dictionary<string, ShVar>());
            FuncDepth++;
            Io = io;
            try { return Exec(f.Body); }
            catch (ReturnEx r) { Status = r.Code; return r.Code; }
            finally { FuncDepth--; Locals.RemoveAt(Locals.Count - 1); Pos = savePos; Io = saveIo; }
        }

        /// <summary>Finds a program: a path, or the first match in PATH. Returns the global Loc or null.</summary>
        /// <summary>The programs this user can run from PATH (first match of each name), sorted: Cyber Range's help lists them.</summary>
        public List<string> ProgramsOnPath()
        {
            var seen = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var dir in (GetVar("PATH") ?? "/usr/bin:/usr/sbin").Split(':'))
            {
                if (dir.Length == 0) continue;
                Loc d;
                try { d = M.V.Resolve(P, dir, true); } catch (VfsEx) { continue; }
                if (!d.Exists || d.Node.T != 'd' || d.Node.Kids == null || !M.V.Can(P.Cr, d.Node, 1)) continue;
                foreach (var name in d.Node.Kids.Keys)
                {
                    if (seen.Contains(name) || name.StartsWith(".", StringComparison.Ordinal)) continue;
                    string shown;
                    var l = FindProgram(name, out shown);
                    if (l != null && l.Node.Prog != null && M.V.Can(P.Cr, l.Node, 1)) seen.Add(name);
                }
            }
            return new List<string>(seen);
        }

        public Loc FindProgram(string name, out string shown)
        {
            shown = name;
            if (name.Contains("/"))
            {
                try { var l = M.V.Resolve(P, name, true); return l.Exists ? l : null; } catch (VfsEx) { return null; }
            }
            foreach (var dir in (GetVar("PATH") ?? "/usr/bin:/usr/sbin").Split(':'))
            {
                if (dir.Length == 0) continue;
                string p = dir.TrimEnd('/') + "/" + name;
                try
                {
                    var l = M.V.Resolve(P, p, true);
                    if (l.Exists && l.Node.T == 'f') { shown = p; return l; }
                }
                catch (VfsEx) { }
            }
            return null;
        }

        int RunExternal(List<string> argv, Io io)
        {
            string name = argv[0];
            string shown;
            var l = FindProgram(name, out shown);
            if (l == null)
            {
                if (name.Contains("/")) { io.E("bash: " + name + ": No such file or directory"); return 127; }
                io.E("bash: " + name + ": command not found...");
                string pkg = Packages.WhoProvides(name);
                if (pkg != null && !M.PackageInstalled(pkg)) io.E("Install package '" + pkg + "' to provide command '" + name + "'? [N/y] n\n");
                return 127;
            }
            if (l.Node.T == 'd') { io.E("bash: " + name + ": Is a directory"); return 126; }
            if (!M.V.Can(P.Cr, l.Node, 1)) { io.E("bash: " + shown + ": Permission denied"); return 126; }
            if (l.Mnt != null && l.Mnt.Opts.Split(',').Contains("noexec")) { io.E("bash: " + shown + ": Permission denied"); return 126; }
            if (l.Node.Prog != null)
            {
                CmdFn fn;
                if (Programs.TryGetValue(l.Node.Prog, out fn))
                {
                    var a2 = new List<string>(argv);
                    // Cyber Range only: a set-user-ID program runs as its owner (the courses' machines keep their behaviour)
                    if (M.Net != null && M.Net.Range != null && (l.Node.Mode & 0x800) != 0 && l.Node.Uid != P.Cr.Uid && !RangeNet.SelfCheckingSuid.Contains(l.Node.Prog))
                    {
                        var owner = M.UserById(l.Node.Uid);
                        var cr = owner != null ? M.CredFor(owner.Name) : new Cred { Uid = l.Node.Uid, Gid = P.Cr.Gid, User = l.Node.Uid.ToString(CultureInfo.InvariantCulture) };
                        var ctx = new PCtx { Cr = cr, Root = P.Root, Cwd = P.Cwd, Umask = P.Umask, Domain = P.Domain };
                        var su = new Sh(M, S, ctx) { Host = Host, Interactive = false };
                        foreach (var kv in Vars) su.Vars[kv.Key] = kv.Value.Copy();
                        su.Io = io;
                        int sst;
                        try { sst = su.Invoke(fn, a2, io, Vfs.Base(name)); }
                        catch (ExitEx e) { sst = e.Code; }
                        Steps = Math.Max(Steps, su.Steps);
                        return sst;
                    }
                    return Invoke(fn, a2, io, Vfs.Base(name));
                }
                // /usr/bin/echo, /usr/bin/test...: the programs that are also shell builtins
                if (Builtins.TryGetValue(l.Node.Prog, out fn)) return Invoke(fn, new List<string>(argv), io, Vfs.Base(name));
                io.E(shown + ": Cert Lab does not simulate this program (it is installed, but it is not part of what the course needs)");
                return 126;
            }
            // a script
            if (!M.V.Can(P.Cr, l.Node, 4)) { io.E("bash: " + shown + ": Permission denied"); return 126; }
            return RunScript(l, argv, io, shown);
        }

        public int RunScript(Loc l, List<string> argv, Io io, string shown)
        {
            string text = l.Node.Text ?? "";
            if (l.Node.Text == null && l.Node.BinSize >= 0) { io.E("bash: " + shown + ": cannot execute binary file: Exec format error"); return 126; }
            string first = text.Split('\n')[0].Trim();
            if (first.StartsWith("#!", StringComparison.Ordinal))
            {
                string interp = first.Substring(2).Trim();
                if (interp.StartsWith("/usr/bin/env ", StringComparison.Ordinal)) interp = interp.Substring(13).Trim();
                string ib = Vfs.Base(interp.Split(' ')[0]);
                if (ib != "bash" && ib != "sh")
                {
                    io.E("bash: " + shown + ": " + interp.Split(' ')[0] + ": bad interpreter: Cert Lab runs bash and sh scripts only");
                    return 126;
                }
            }
            var child = Child(null);
            child.Arg0 = shown;
            child.Pos = argv.Skip(1).ToList();
            child.Io = io;
            int st;
            try { st = child.RunText(text, io); }
            catch (ExitEx e) { st = e.Code; }
            finally { child.RunTrap("EXIT"); }
            Steps = child.Steps;
            if (child.BgSeconds > 0) BgSeconds += child.BgSeconds;
            if (child.LongRunning) LongRunning = true;
            return st;
        }

        public void RunTrap(string sig)
        {
            string t;
            if (Traps.TryGetValue(sig, out t) && t.Length > 0)
            {
                Traps.Remove(sig);
                try { RunText(t, Io); } catch (ExitEx) { }
            }
        }

        // ------------------------------------------------------------ background jobs (cmd &)
        void RunBackground(ShNode node)
        {
            var sub = Subshell();
            sub.Background = true;
            sub.BgSeconds = 0;
            sub.Io = new Io { In = new StrSrc(""), Out = Io.Out, Err = Io.Err, Fds = Io.Fds };
            sub.MaxSteps = Steps + 20000;
            bool forever = false;
            try { sub.Exec(node); }
            catch (ExitEx) { }
            catch (TooLong) { forever = true; }
            catch (BreakSignal) { }
            if (sub.LongRunning) forever = true;
            Steps = Math.Min(Steps, sub.Steps);
            string text = NodeText(node);
            var p = M.Spawn(P.Cr.User, text, "", "unconfined_u:unconfined_r:unconfined_t:s0-s0:c0.c1023");
            p.Tty = S == null ? "?" : S.Tty;
            p.Nice = sub.BgNice;
            p.EndsAt = forever ? long.MaxValue : M.Now + Math.Max(0, sub.BgSeconds);
            if (sub.BgCpu > 0) p.Cpu = sub.BgCpu;
            if (sub.BgComm != null) { p.Comm = sub.BgComm; p.Cmd = sub.BgCmd ?? text; }
            var job = new ShJob { Id = Jobs.Count(j => !j.Done) == 0 ? 1 : Jobs.Where(j => !j.Done).Max(j => j.Id) + 1, Pid = p.Pid, Text = text };
            if (p.EndsAt <= M.Now) { job.Done = true; M.Procs.Remove(p); }
            Jobs.Add(job);
            LastBg = p.Pid;
            if (Interactive) Io.Err.Write("[" + job.Id + "] " + p.Pid + "\n");
            Status = 0;
        }
        public int BgNice; public double BgCpu; public string BgComm, BgCmd;

        static string NodeText(ShNode n)
        {
            var p = n as ShPipe; if (p != null) return p.Text;
            var ao = n as ShAndOr; if (ao != null) return string.Join(" && ", ao.Parts.Select(NodeText).ToArray());
            return "job";
        }

        /// <summary>Jobs that finished since the last prompt ("[1]+  Done ...").</summary>
        public void ReportJobs(Sink err)
        {
            foreach (var j in Jobs)
            {
                if (j.Reported) continue;
                if (!j.Done && M.ProcByPid(j.Pid) == null) j.Done = true;
                if (j.Done) { j.Reported = true; err.Write("[" + j.Id + "]+  Done                    " + j.Text + "\n"); }
            }
            Jobs.RemoveAll(x => x.Reported);
        }

        // ------------------------------------------------------------ the prompt
        public string Prompt(string ps)
        {
            var sb = new StringBuilder();
            string host = Host.Length > 0 ? Host : M.HostnameNow;
            for (int i = 0; i < ps.Length; i++)
            {
                char c = ps[i];
                if (c != '\\' || i + 1 >= ps.Length) { sb.Append(c); continue; }
                char d = ps[++i];
                switch (d)
                {
                    case 'u': sb.Append(P.Cr.User); break;
                    case 'h': { int k = host.IndexOf('.'); sb.Append(k > 0 ? host.Substring(0, k) : host); break; }
                    case 'H': sb.Append(host); break;
                    case 'w': sb.Append(TildePath(P.Cwd)); break;
                    case 'W': { var t = TildePath(P.Cwd); sb.Append(t == "/" || t == "~" ? t : Vfs.Base(t)); break; }
                    case '$': sb.Append(P.Cr.Uid == 0 ? '#' : '$'); break;
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append(M.LocalTime(M.Now).ToString("HH:mm:ss", CultureInfo.InvariantCulture)); break;
                    case 'd': sb.Append(M.LocalTime(M.Now).ToString("ddd MMM dd", CultureInfo.InvariantCulture)); break;
                    case 's': sb.Append("bash"); break;
                    case 'v': sb.Append("5.1"); break;
                    case '[': case ']': break;
                    case 'e': sb.Append(""); break;
                    default: sb.Append('\\').Append(d); break;
                }
            }
            var r = sb.ToString();
            // colour codes from a custom PS1 are dropped
            r = System.Text.RegularExpressions.Regex.Replace(r, "\\[[0-9;]*m", "");
            return r;
        }
        public string TildePath(string p)
        {
            string home = GetVar("HOME");
            if (!string.IsNullOrEmpty(home) && home != "/" && (p == home || p.StartsWith(home + "/", StringComparison.Ordinal))) return "~" + p.Substring(home.Length);
            return p;
        }

        /// <summary>The interactive loop of a shell on a terminal: prompt, read (with "> " for unfinished input), run.</summary>
        public int Repl()
        {
            Interactive = true;
            for (;;)
            {
                ReportJobs(Io.Err);
                string ps1 = Prompt(GetVar("PS1") ?? "\\$ ");
                string line;
                // Ctrl+C at the prompt drops the line and shows a new prompt (the terminal already printed ^C); it
                // used to unwind the whole login shell back to the login prompt
                try { line = S.ReadLine(ps1, false); }
                catch (BreakSignal) { Status = 130; continue; }
                if (line == null) { Io.Out.Write("logout\n"); return Status; }
                string text = line;
                ShList parsed = null;
                for (;;)
                {
                    try { parsed = ShParser.Parse(HistExpand(text)); break; }
                    catch (ShIncomplete)
                    {
                        string more;
                        try { more = S.ReadLine(Prompt(GetVar("PS2") ?? "> "), false); }
                        catch (BreakSignal) { Status = 130; parsed = null; text = null; break; }   // Ctrl+C at "> " drops the command
                        if (more == null) { Io.E("bash: syntax error: unexpected end of file"); parsed = null; text = null; break; }
                        text += "\n" + more;
                    }
                    catch (ShSyntax e) { Io.E("-bash: " + e.Message); Status = 2; parsed = null; break; }
                    catch (ShError e) { Io.E("-bash: " + e.Message); Status = 1; parsed = null; text = null; break; }   // !x: event not found
                }
                if (text == null) continue;
                string hx = HistExpand(text);
                if (hx != text) Io.Out.Write(hx + "\n");
                if (text.Trim().Length > 0 && !text.StartsWith(" ", StringComparison.Ordinal)) { History.Add(hx); if (History.Count > 1000) History.RemoveAt(0); }
                if (parsed == null) continue;
                Steps = 0;
                try { Exec(parsed); }
                catch (ExitEx e) { RunTrap("EXIT"); return e.Code; }
                catch (ShError e) { Io.E("-bash: " + e.Message); Status = e.Status; }
                catch (LoopEx) { }
                catch (ReturnEx) { Io.E("-bash: return: can only `return' from a function or sourced script"); }
                catch (BreakSignal) { Io.Out.Write("^C\n"); Status = 130; }
                catch (TooLong) { Io.E("-bash: Cert Lab stopped this command: it ran too long (an endless loop?)"); Status = 130; }
                catch (VfsEx e) { Io.E("-bash: " + e.Message); Status = 1; }
            }
        }

        string HistExpand(string t)
        {
            if (t.IndexOf('!') < 0 || History.Count == 0) return t;
            var sb = new StringBuilder();
            bool sq = false;
            for (int i = 0; i < t.Length; i++)
            {
                char c = t[i];
                if (c == '\'') sq = !sq;
                if (c == '!' && !sq && i + 1 < t.Length)
                {
                    char d = t[i + 1];
                    if (d == '!') { sb.Append(History[History.Count - 1]); i++; continue; }
                    if (d == '$') { var w = History[History.Count - 1].Split(' '); sb.Append(w[w.Length - 1]); i++; continue; }
                    if (char.IsDigit(d) || d == '-')
                    {
                        int e = i + 1; if (t[e] == '-') e++;
                        while (e < t.Length && char.IsDigit(t[e])) e++;
                        int n; if (int.TryParse(t.Substring(i + 1, e - i - 1), out n))
                        {
                            int idx = n < 0 ? History.Count + n : n - 1;
                            if (idx >= 0 && idx < History.Count) { sb.Append(History[idx]); i = e - 1; continue; }
                            throw new ShError("!" + n + ": event not found");
                        }
                    }
                    if (char.IsLetter(d))
                    {
                        int e = i + 1; while (e < t.Length && !char.IsWhiteSpace(t[e])) e++;
                        string pre = t.Substring(i + 1, e - i - 1);
                        for (int k = History.Count - 1; k >= 0; k--) if (History[k].StartsWith(pre, StringComparison.Ordinal)) { sb.Append(History[k]); i = e - 1; goto next; }
                        throw new ShError("!" + pre + ": event not found");
                    }
                }
                sb.Append(c);
            next:;
            }
            return sb.ToString();
        }
    }

    public class ProcSink : Sink
    {
        readonly Machine m; readonly Loc l; readonly StringBuilder sb = new StringBuilder();
        public ProcSink(Machine mm, Loc ll) { m = mm; l = ll; }
        public override void Write(string s) { sb.Append(s); }
        public override void Close() { m.WriteProc(l, sb.ToString()); }
    }

    public delegate int CmdFn(Sh sh, List<string> a, Io io);
}
