// Cert Lab - a small awk: patterns (BEGIN, END, /regex/, expressions, ranges), fields and NF / NR / FS / OFS,
// print and printf (with > and >> files), if / while / do / for / for-in, associative arrays, user functions and
// the usual built-in functions (length, substr, index, split, sub, gsub, match, sprintf, tolower, toupper...).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CertLab
{
    public class Awk
    {
        // ------------------------------------------------------------ values
        class Val
        {
            public double N; public string S; public bool IsNum, StrNum;
            public static Val Num(double n) { return new Val { N = n, IsNum = true }; }
            public static Val Str(string s) { return new Val { S = s }; }
            public static Val Field(string s)
            {
                // input that looks like a number compares as one ("strnum")
                double n;
                if (double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out n) && s.Trim().Length > 0) return new Val { S = s, N = n, StrNum = true };
                return new Val { S = s };
            }
            public double ToNum()
            {
                if (IsNum || StrNum) return N;
                var m = Regex.Match(S ?? "", "^\\s*[-+]?(\\d+\\.?\\d*|\\.\\d+)([eE][-+]?\\d+)?");
                return m.Success ? double.Parse(m.Value.Trim(), CultureInfo.InvariantCulture) : 0;
            }
            public string ToStr(string convfmt)
            {
                if (!IsNum) return S ?? "";
                if (N == Math.Floor(N) && Math.Abs(N) < 1e16) return ((long)N).ToString(CultureInfo.InvariantCulture);
                string err;
                return Printf.Format(convfmt, new List<string> { N.ToString("R", CultureInfo.InvariantCulture) }, out err);
            }
            public bool Truth() { if (IsNum) return N != 0; if (StrNum) return N != 0; return !string.IsNullOrEmpty(S); }
        }

        // ------------------------------------------------------------ lexer
        class Tok { public string T; public string V; public double N; public override string ToString() { return T + ":" + V; } }
        readonly List<Tok> toks = new List<Tok>();
        int p;

        static readonly string[] Ops = { "+=", "-=", "*=", "/=", "%=", "^=", "==", "<=", ">=", "!=", "++", "--", "&&", "||", ">>", "!~", "**", "+", "-", "*", "/", "%", "^", "!", ">", "<", "|", "?", ":", "~", "=", "$", "(", ")", "[", "]", "{", "}", ",", ";" };
        static readonly HashSet<string> Keywords = new HashSet<string> { "BEGIN", "END", "function", "func", "if", "else", "while", "for", "do", "break", "continue", "next", "nextfile", "exit", "return", "delete", "in", "getline", "print", "printf" };
        static readonly HashSet<string> Builtins = new HashSet<string> { "length", "substr", "index", "split", "sub", "gsub", "match", "sprintf", "tolower", "toupper", "int", "sqrt", "exp", "log", "sin", "cos", "atan2", "rand", "srand", "system", "systime", "strftime", "close", "fflush" };

        void Lex(string s)
        {
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '\\' && i + 1 < s.Length && s[i + 1] == '\n') { i += 2; continue; }
                if (c == ' ' || c == '\t' || c == '\r') { i++; continue; }
                if (c == '\n') { toks.Add(new Tok { T = "nl" }); i++; continue; }
                if (c == '#') { while (i < s.Length && s[i] != '\n') i++; continue; }
                if (c == '"')
                {
                    var sb = new StringBuilder(); i++;
                    while (i < s.Length && s[i] != '"')
                    {
                        if (s[i] == '\\' && i + 1 < s.Length)
                        {
                            char d = s[++i];
                            switch (d) { case 'n': sb.Append('\n'); break; case 't': sb.Append('\t'); break; case 'r': sb.Append('\r'); break; case '\\': sb.Append('\\'); break; case '"': sb.Append('"'); break; case '/': sb.Append('/'); break; case 'a': sb.Append('\a'); break; default: sb.Append('\\').Append(d); break; }
                            i++; continue;
                        }
                        sb.Append(s[i++]);
                    }
                    i++;
                    toks.Add(new Tok { T = "str", V = sb.ToString() });
                    continue;
                }
                if (c == '/' && RegexAllowed())
                {
                    var sb = new StringBuilder(); i++;
                    while (i < s.Length && s[i] != '/') { if (s[i] == '\\' && i + 1 < s.Length) { if (s[i + 1] == '/') { sb.Append('/'); i += 2; continue; } sb.Append(s[i]).Append(s[i + 1]); i += 2; continue; } sb.Append(s[i++]); }
                    i++;
                    toks.Add(new Tok { T = "ere", V = sb.ToString() });
                    continue;
                }
                if (char.IsDigit(c) || (c == '.' && i + 1 < s.Length && char.IsDigit(s[i + 1])))
                {
                    var m = Regex.Match(s.Substring(i), "^(0[xX][0-9a-fA-F]+|\\d*\\.?\\d+([eE][-+]?\\d+)?|\\d+\\.)");
                    string t = m.Value;
                    double n = t.StartsWith("0x", StringComparison.Ordinal) || t.StartsWith("0X", StringComparison.Ordinal) ? Convert.ToInt64(t.Substring(2), 16) : double.Parse(t, CultureInfo.InvariantCulture);
                    toks.Add(new Tok { T = "num", N = n, V = t });
                    i += t.Length;
                    continue;
                }
                if (char.IsLetter(c) || c == '_')
                {
                    int st = i;
                    while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;
                    string w = s.Substring(st, i - st);
                    if (Keywords.Contains(w)) toks.Add(new Tok { T = w == "func" ? "function" : w, V = w });
                    else if (Builtins.Contains(w)) toks.Add(new Tok { T = "builtin", V = w });
                    else if (i < s.Length && s[i] == '(') toks.Add(new Tok { T = "funcname", V = w });
                    else toks.Add(new Tok { T = "name", V = w });
                    continue;
                }
                bool found = false;
                foreach (var op in Ops)
                    if (string.CompareOrdinal(s, i, op, 0, op.Length) == 0) { toks.Add(new Tok { T = op == "**" ? "^" : op, V = op }); i += op.Length; found = true; break; }
                if (!found) throw new ShError("syntax error at source line 1: unexpected character '" + c + "'", 2);
            }
            toks.Add(new Tok { T = "eof" });
        }
        bool RegexAllowed()
        {
            if (toks.Count == 0) return true;
            var t = toks[toks.Count - 1].T;
            return !(t == "num" || t == "str" || t == "name" || t == ")" || t == "]" || t == "$" || t == "builtin" || t == "++" || t == "--" || t == "ere");
        }

        // ------------------------------------------------------------ AST
        abstract class Node { }
        class NNum : Node { public double V; }
        class NStr : Node { public string V; }
        class NRe : Node { public string V; }
        class NVar : Node { public string Name; }
        class NIdx : Node { public string Name; public List<Node> Keys; }
        class NField : Node { public Node E; }
        class NBin : Node { public string Op; public Node L, R; }
        class NUn : Node { public string Op; public Node E; }
        class NAssign : Node { public string Op; public Node L, R; }
        class NIncDec : Node { public string Op; public bool Post; public Node L; }
        class NCond : Node { public Node C, A, B; }
        class NCall : Node { public string Name; public List<Node> Args; public bool Builtin; }
        class NIn : Node { public List<Node> Keys; public string Arr; }
        class NMatch : Node { public bool Neg; public Node L, R; }
        class NGetline : Node { public Node Var; public Node File; }
        class NGroup : Node { public Node E; }

        abstract class Stmt { }
        class SExpr : Stmt { public Node E; }
        class SPrint : Stmt { public bool F; public List<Node> Args; public string Redir; public Node Target; }
        class SIf : Stmt { public Node C; public Stmt A, B; }
        class SWhile : Stmt { public Node C; public Stmt Body; public bool Do; }
        class SFor : Stmt { public Stmt Init; public Node C; public Stmt Step; public Stmt Body; }
        class SForIn : Stmt { public string Var, Arr; public Stmt Body; }
        class SBlock : Stmt { public List<Stmt> L = new List<Stmt>(); }
        class SSimple : Stmt { public string K; public Node E; }
        class SDelete : Stmt { public string Arr; public List<Node> Keys; }

        class Rule { public Node P1, P2; public bool Begin, End; public SBlock Action; public bool InRange; }
        class Func { public string Name; public List<string> Params; public SBlock Body; }

        readonly List<Rule> rules = new List<Rule>();
        readonly Dictionary<string, Func> funcs = new Dictionary<string, Func>();

        Tok Peek { get { return toks[p]; } }
        Tok Next() { return toks[p++]; }
        bool Is(string t) { return toks[p].T == t; }
        void Expect(string t) { if (!Is(t)) throw new ShError("syntax error at source line 1 near '" + (Peek.V ?? Peek.T) + "'", 2); p++; }
        void Nl() { while (Is("nl")) p++; }
        void Term() { while (Is("nl") || Is(";")) p++; }

        void ParseProgram()
        {
            Term();
            while (!Is("eof"))
            {
                if (Is("function"))
                {
                    p++;
                    string name = Next().V;
                    Expect("(");
                    var ps = new List<string>();
                    while (!Is(")")) { ps.Add(Next().V); if (Is(",")) { p++; Nl(); } }
                    p++; Nl();
                    funcs[name] = new Func { Name = name, Params = ps, Body = ParseBlock() };
                    Term();
                    continue;
                }
                var r = new Rule();
                if (Is("BEGIN")) { p++; r.Begin = true; }
                else if (Is("END")) { p++; r.End = true; }
                else if (!Is("{"))
                {
                    r.P1 = Expr(false);
                    if (Is(",")) { p++; Nl(); r.P2 = Expr(false); }
                }
                if (Is("{")) r.Action = ParseBlock();
                else if (r.Begin || r.End) throw new ShError("syntax error: BEGIN and END need an action", 2);
                rules.Add(r);
                Term();
            }
        }

        SBlock ParseBlock()
        {
            Expect("{");
            var b = new SBlock();
            Term();
            while (!Is("}")) { if (Is("eof")) throw new ShError("syntax error: missing }", 2); b.L.Add(ParseStmt()); Term(); }
            p++;
            return b;
        }

        Stmt ParseSimpleOrBlock() { Nl(); if (Is("{")) return ParseBlock(); if (Is(";")) { p++; return new SBlock(); } return ParseStmt(); }

        Stmt ParseStmt()
        {
            var t = Peek.T;
            switch (t)
            {
                case "{": return ParseBlock();
                case "if":
                    {
                        p++; Expect("("); var c = Expr(false); Expect(")");
                        var a = ParseSimpleOrBlock();
                        int save = p; Term();
                        if (Is("else")) { p++; return new SIf { C = c, A = a, B = ParseSimpleOrBlock() }; }
                        p = save;
                        return new SIf { C = c, A = a };
                    }
                case "while": { p++; Expect("("); var c = Expr(false); Expect(")"); if (Is(";")) { p++; return new SWhile { C = c, Body = new SBlock() }; } return new SWhile { C = c, Body = ParseSimpleOrBlock() }; }
                case "do": { p++; var body = ParseSimpleOrBlock(); Term(); Expect("while"); Expect("("); var c = Expr(false); Expect(")"); return new SWhile { C = c, Body = body, Do = true }; }
                case "for":
                    {
                        p++; Expect("(");
                        if (Is("name") && toks[p + 1].T == "in" && toks[p + 2].T == "name" && toks[p + 3].T == ")")
                        {
                            string v = Next().V; p++; string arr = Next().V; p++;
                            return new SForIn { Var = v, Arr = arr, Body = ParseSimpleOrBlock() };
                        }
                        Stmt init = Is(";") ? null : new SExpr { E = Expr(false) };
                        Expect(";"); Nl();
                        Node cond = Is(";") ? null : Expr(false);
                        Expect(";"); Nl();
                        Stmt step = Is(")") ? null : new SExpr { E = Expr(false) };
                        Expect(")");
                        return new SFor { Init = init, C = cond, Step = step, Body = ParseSimpleOrBlock() };
                    }
                case "break": case "continue": case "next": case "nextfile": p++; return new SSimple { K = t };
                case "exit": case "return": { p++; Node e = null; if (!Is(";") && !Is("nl") && !Is("}") && !Is("eof")) e = Expr(false); return new SSimple { K = t, E = e }; }
                case "delete":
                    {
                        p++; string arr = Next().V;
                        List<Node> keys = null;
                        if (Is("[")) { p++; keys = ExprList(); Expect("]"); }
                        return new SDelete { Arr = arr, Keys = keys };
                    }
                case "print": case "printf":
                    {
                        p++;
                        var s = new SPrint { F = t == "printf", Args = new List<Node>() };
                        bool paren = Is("(") && ParenIsWholeArgList();
                        if (paren) { p++; s.Args = ExprList(); Expect(")"); }
                        else if (!Is(";") && !Is("nl") && !Is("}") && !Is(">") && !Is(">>") && !Is("|") && !Is("eof")) s.Args = ExprListNoGt();
                        if (Is(">") || Is(">>") || Is("|")) { s.Redir = Next().T; s.Target = Concat(true); }
                        return s;
                    }
            }
            return new SExpr { E = Expr(false) };
        }
        bool ParenIsWholeArgList()
        {
            int depth = 0;
            for (int k = p; k < toks.Count; k++)
            {
                if (toks[k].T == "(") depth++;
                else if (toks[k].T == ")") { depth--; if (depth == 0) { var n = toks[k + 1].T; return n == ";" || n == "nl" || n == "}" || n == ">" || n == ">>" || n == "|" || n == "eof"; } }
            }
            return false;
        }
        List<Node> ExprList() { var l = new List<Node> { Expr(false) }; while (Is(",")) { p++; Nl(); l.Add(Expr(false)); } return l; }
        List<Node> ExprListNoGt() { var l = new List<Node> { Expr(true) }; while (Is(",")) { p++; Nl(); l.Add(Expr(true)); } return l; }

        Node Expr(bool noGt) { return Ternary(noGt); }
        Node Ternary(bool noGt)
        {
            var c = Or(noGt);
            if (Is("?")) { p++; Nl(); var a = Ternary(noGt); Nl(); Expect(":"); Nl(); var b = Ternary(noGt); return new NCond { C = c, A = a, B = b }; }
            if (Is("=") || Is("+=") || Is("-=") || Is("*=") || Is("/=") || Is("%=") || Is("^="))
            {
                if (c is NVar || c is NIdx || c is NField) { string op = Next().T; Nl(); var r = Ternary(noGt); return new NAssign { Op = op, L = c, R = r }; }
            }
            return c;
        }
        Node Or(bool noGt) { var l = And(noGt); while (Is("||")) { p++; Nl(); l = new NBin { Op = "||", L = l, R = And(noGt) }; } return l; }
        Node And(bool noGt) { var l = InExpr(noGt); while (Is("&&")) { p++; Nl(); l = new NBin { Op = "&&", L = l, R = InExpr(noGt) }; } return l; }
        Node InExpr(bool noGt)
        {
            var l = MatchExpr(noGt);
            while (Is("in")) { p++; string arr = Next().V; l = new NIn { Keys = new List<Node> { l }, Arr = arr }; }
            return l;
        }
        Node MatchExpr(bool noGt)
        {
            var l = Rel(noGt);
            while (Is("~") || Is("!~")) { bool neg = Next().T == "!~"; l = new NMatch { Neg = neg, L = l, R = Rel(noGt) }; }
            return l;
        }
        Node Rel(bool noGt)
        {
            var l = Concat(noGt);
            if (Is("<") || Is("<=") || Is("!=") || Is("==") || Is(">=") || (Is(">") && !noGt)) { string op = Next().T; return new NBin { Op = op, L = l, R = Concat(noGt) }; }
            return l;
        }
        Node Concat(bool noGt)
        {
            var l = Add();
            while (StartsPrimary() ) l = new NBin { Op = "cat", L = l, R = Add() };
            return l;
        }
        bool StartsPrimary()
        {
            var t = Peek.T;
            if (t == "in") return false;
            return t == "num" || t == "str" || t == "ere" || t == "name" || t == "funcname" || t == "builtin" || t == "$" || t == "!" || t == "(" || t == "-" || t == "+" || t == "++" || t == "--";
        }
        Node Add() { var l = Mul(); for (;;) { if (Is("+") || Is("-")) { string op = Next().T; l = new NBin { Op = op, L = l, R = Mul() }; } else return l; } }
        Node Mul() { var l = Unary(); for (;;) { if (Is("*") || Is("/") || Is("%")) { string op = Next().T; l = new NBin { Op = op, L = l, R = Unary() }; } else return l; } }
        Node Unary()
        {
            if (Is("!")) { p++; return new NUn { Op = "!", E = Unary() }; }
            if (Is("-")) { p++; return new NUn { Op = "-", E = Unary() }; }
            if (Is("+")) { p++; return new NUn { Op = "+", E = Unary() }; }
            return Pow();
        }
        Node Pow() { var b = PostInc(); if (Is("^")) { p++; return new NBin { Op = "^", L = b, R = Unary() }; } return b; }
        Node PostInc()
        {
            if (Is("++") || Is("--")) { string op = Next().T; var e = Primary(); return new NIncDec { Op = op, L = e }; }
            var x = Primary();
            if ((Is("++") || Is("--")) && (x is NVar || x is NIdx || x is NField)) { string op = Next().T; return new NIncDec { Op = op, Post = true, L = x }; }
            return x;
        }
        Node Primary()
        {
            var t = Next();
            switch (t.T)
            {
                case "num": return new NNum { V = t.N };
                case "str": return new NStr { V = t.V };
                case "ere": return new NRe { V = t.V };
                case "$": return new NField { E = PrimaryNoConcat() };
                case "(":
                    {
                        var e = Expr(false);
                        if (Is(",")) { var keys = new List<Node> { e }; while (Is(",")) { p++; keys.Add(Expr(false)); } Expect(")"); Expect("in"); return new NIn { Keys = keys, Arr = Next().V }; }
                        Expect(")"); return new NGroup { E = e };
                    }
                case "name":
                    if (Is("[")) { p++; var keys = ExprList(); Expect("]"); return new NIdx { Name = t.V, Keys = keys }; }
                    return new NVar { Name = t.V };
                case "funcname": { Expect("("); var args = Is(")") ? new List<Node>() : ExprList(); Expect(")"); return new NCall { Name = t.V, Args = args }; }
                case "builtin":
                    {
                        var args = new List<Node>();
                        if (Is("(")) { p++; if (!Is(")")) args = ExprList(); Expect(")"); }
                        return new NCall { Name = t.V, Args = args, Builtin = true };
                    }
                case "getline": { Node v = null; if (Is("name") || Is("$")) v = PostInc(); Node f = null; if (Is("<")) { p++; f = Primary(); } return new NGetline { Var = v, File = f }; }
                case "-": return new NUn { Op = "-", E = Primary() };
                case "!": return new NUn { Op = "!", E = Primary() };
            }
            throw new ShError("syntax error at source line 1 near '" + (t.V ?? t.T) + "'", 2);
        }
        Node PrimaryNoConcat()
        {
            if (Is("++") || Is("--")) return PostInc();
            if (Is("-")) { p++; return new NUn { Op = "-", E = Primary() }; }
            return Primary();
        }

        // ------------------------------------------------------------ running
        readonly Dictionary<string, Val> vars = new Dictionary<string, Val>();
        readonly Dictionary<string, Dictionary<string, Val>> arrays = new Dictionary<string, Dictionary<string, Val>>();
        readonly List<Dictionary<string, object>> frames = new List<Dictionary<string, object>>();
        List<string> fields = new List<string> { "" };
        bool fieldsDirty;
        string record = "";
        readonly StringBuilder outBuf = new StringBuilder();
        Sh sh; Io io;
        readonly Dictionary<string, StringBuilder> files = new Dictionary<string, StringBuilder>();
        readonly Dictionary<string, bool> fileAppend = new Dictionary<string, bool>();
        Random rnd = new Random(1);
        int steps;

        class NextEx : Exception { }
        class ExitEx2 : Exception { public int Code; }
        class RetEx : Exception { public Val V; }
        class BrkEx : Exception { }
        class ContEx : Exception { }

        string V(string n) { Val v; return vars.TryGetValue(n, out v) ? v.ToStr(Conv) : ""; }
        string Conv { get { Val v; return vars.TryGetValue("CONVFMT", out v) ? v.S : "%.6g"; } }
        string OFmt { get { Val v; return vars.TryGetValue("OFMT", out v) ? v.S : "%.6g"; } }

        public static int Run(Sh sh, List<string> a, Io io)
        {
            var aw = new Awk { sh = sh, io = io };
            string fs = " ";
            string prog = null;
            var assigns = new List<string>();
            var files = new List<string>();
            for (int i = 1; i < a.Count; i++)
            {
                string x = a[i];
                if (prog == null && x == "-F" && i + 1 < a.Count) { fs = a[++i]; continue; }
                if (prog == null && x.StartsWith("-F", StringComparison.Ordinal)) { fs = x.Substring(2); continue; }
                if (prog == null && x == "-v" && i + 1 < a.Count) { assigns.Add(a[++i]); continue; }
                if (prog == null && x.StartsWith("-v", StringComparison.Ordinal) && x.Length > 2) { assigns.Add(x.Substring(2)); continue; }
                if (prog == null && x == "-f" && i + 1 < a.Count) { try { prog = Cmd.Read(sh, a[++i]); } catch (VfsEx e) { io.E("awk: fatal: can't open source file `" + a[i] + "' for reading: " + e.Message); return 2; } continue; }
                if (prog == null && x == "--") continue;
                if (prog == null) { prog = x; continue; }
                files.Add(x);
            }
            if (prog == null) { io.E("usage: awk [POSIX or GNU style options] -f progfile [--] file ...\nusage: awk [POSIX or GNU style options] [--] 'program' file ..."); return 2; }
            if (fs == "t") fs = "\t";
            fs = Printf.Escapes(fs, false);
            try { aw.Lex(prog); aw.ParseProgram(); }
            catch (ShError e) { io.E("awk: cmd. line:1: " + e.Message); return 2; }
            aw.vars["FS"] = Val.Str(fs); aw.vars["OFS"] = Val.Str(" "); aw.vars["ORS"] = Val.Str("\n"); aw.vars["RS"] = Val.Str("\n");
            aw.vars["NR"] = Val.Num(0); aw.vars["NF"] = Val.Num(0); aw.vars["FNR"] = Val.Num(0); aw.vars["SUBSEP"] = Val.Str("\u001c");
            aw.vars["CONVFMT"] = Val.Str("%.6g"); aw.vars["OFMT"] = Val.Str("%.6g"); aw.vars["FILENAME"] = Val.Str("");
            var env = new Dictionary<string, Val>(); foreach (var kv in sh.Env()) env[kv.Key] = Val.Str(kv.Value);
            aw.arrays["ENVIRON"] = env;
            foreach (var asg in assigns) { int eq = asg.IndexOf('='); if (eq > 0) aw.vars[asg.Substring(0, eq)] = Val.Field(Printf.Escapes(asg.Substring(eq + 1), false)); }
            int code = 0;
            try
            {
                foreach (var r in aw.rules.Where(r => r.Begin)) aw.Exec(r.Action);
                bool hasMain = aw.rules.Any(r => !r.Begin && !r.End);
                bool hasEnd = aw.rules.Any(r => r.End);
                if (hasMain || hasEnd)
                {
                    var inputs = new List<KeyValuePair<string, string>>();
                    if (files.Count == 0 || files.All(f => f.Contains("=") && !f.StartsWith("/", StringComparison.Ordinal))) inputs.Add(new KeyValuePair<string, string>("", io.In.ReadAll()));
                    foreach (var f in files)
                    {
                        if (f.Contains("=") && Regex.IsMatch(f, "^[A-Za-z_][A-Za-z0-9_]*=")) { int eq = f.IndexOf('='); inputs.Add(new KeyValuePair<string, string>("\u0001" + f.Substring(0, eq), f.Substring(eq + 1))); continue; }
                        try { inputs.Add(new KeyValuePair<string, string>(f, f == "-" ? io.In.ReadAll() : Cmd.Read(sh, f))); }
                        catch (VfsEx e) { io.E("awk: fatal: cannot open file `" + f + "' for reading: " + e.Message); return 2; }
                    }
                    foreach (var inp in inputs)
                    {
                        if (inp.Key.StartsWith("\u0001", StringComparison.Ordinal)) { aw.vars[inp.Key.Substring(1)] = Val.Field(inp.Value); continue; }
                        aw.vars["FILENAME"] = Val.Str(inp.Key);
                        aw.vars["FNR"] = Val.Num(0);
                        string rs = aw.V("RS");
                        var recs = rs == "\n" ? Cmd.Lines(inp.Value) : rs == "" ? Regex.Split(inp.Value.Trim('\n'), "\n\n+").ToList() : inp.Value.Split(rs[0]).ToList();
                        if (rs != "\n" && recs.Count > 0 && recs[recs.Count - 1].TrimEnd('\n').Length == 0) recs.RemoveAt(recs.Count - 1);
                        foreach (var rec in recs)
                        {
                            aw.vars["NR"] = Val.Num(aw.vars["NR"].ToNum() + 1);
                            aw.vars["FNR"] = Val.Num(aw.vars["FNR"].ToNum() + 1);
                            aw.SetRecord(rec);
                            try
                            {
                                foreach (var r in aw.rules)
                                {
                                    if (r.Begin || r.End) continue;
                                    bool sel;
                                    if (r.P1 == null) sel = true;
                                    else if (r.P2 == null) sel = aw.Cond(r.P1);
                                    else
                                    {
                                        if (r.InRange) { sel = true; if (aw.Cond(r.P2)) r.InRange = false; }
                                        else if (aw.Cond(r.P1)) { sel = true; r.InRange = !aw.Cond(r.P2); }
                                        else sel = false;
                                    }
                                    if (!sel) continue;
                                    if (r.Action == null) aw.Emit(aw.Record() + aw.V("ORS"), null, null);
                                    else aw.Exec(r.Action);
                                }
                            }
                            catch (NextEx) { }
                        }
                    }
                }
                foreach (var r in aw.rules.Where(r => r.End)) aw.Exec(r.Action);
            }
            catch (ExitEx2 e)
            {
                code = e.Code;
                // exit in a main rule still runs END
                try { if (!aw.inEnd) { aw.inEnd = true; foreach (var r in aw.rules.Where(r => r.End)) aw.Exec(r.Action); } } catch (ExitEx2 e2) { code = e2.Code; }
            }
            catch (ShError e) { io.P(aw.outBuf.ToString()); io.E("awk: " + e.Message); return 2; }
            io.P(aw.outBuf.ToString());
            foreach (var kv in aw.files)
            {
                try { var l = Cmd.Find(sh, kv.Key, true); sh.M.V.WriteLoc(sh.P, l, kv.Value.ToString(), aw.fileAppend[kv.Key], 0x1A4); }
                catch (VfsEx e) { io.E("awk: cannot redirect to `" + kv.Key + "': " + e.Message); code = 2; }
            }
            return code;
        }
        bool inEnd;

        void SetRecord(string r) { record = r; SplitFields(); }
        void SplitFields()
        {
            string fs = V("FS");
            fields = new List<string> { record };
            if (record.Length > 0)
            {
                if (fs == " ") fields.AddRange(Regex.Split(record.Trim(' ', '\t', '\n'), "[ \\t\\n]+"));
                else if (fs.Length == 1 && fs != "\\") fields.AddRange(record.Split(fs[0]));
                else fields.AddRange(Regex.Split(record, Cmd.PosixToNet(fs, true)));
                if (fs == " " && record.Trim().Length == 0) fields = new List<string> { record };
            }
            vars["NF"] = Val.Num(fields.Count - 1);
            fieldsDirty = false;
        }
        string Record()
        {
            if (fieldsDirty) { record = string.Join(V("OFS"), fields.Skip(1).ToArray()); fields[0] = record; fieldsDirty = false; }
            return record;
        }
        string GetField(int i)
        {
            if (i == 0) return Record();
            return i < fields.Count ? fields[i] : "";
        }
        void SetField(int i, string v)
        {
            if (i == 0) { SetRecord(v); return; }
            while (fields.Count <= i) fields.Add("");
            fields[i] = v;
            vars["NF"] = Val.Num(fields.Count - 1);
            fieldsDirty = true;
        }

        void Emit(string s, string redir, string target)
        {
            if (redir == null) { outBuf.Append(s); return; }
            if (redir == "|")
            {
                var res = new BufSink();
                var cio = new Io { In = new StrSrc(s), Out = res, Err = io.Err };
                sh.Subshell().RunText(target, cio);
                outBuf.Append(res.Text);
                return;
            }
            StringBuilder b;
            if (!files.TryGetValue(target, out b)) { b = new StringBuilder(); files[target] = b; fileAppend[target] = redir == ">>"; }
            b.Append(s);
        }

        void Exec(Stmt s)
        {
            if (++steps > 2000000) throw new ShError("program ran too long (Cert Lab stopped it)", 2);
            var b = s as SBlock; if (b != null) { foreach (var x in b.L) Exec(x); return; }
            var e = s as SExpr; if (e != null) { Eval(e.E); return; }
            var pr = s as SPrint;
            if (pr != null)
            {
                string target = pr.Target == null ? null : Eval(pr.Target).ToStr(Conv);
                if (pr.F)
                {
                    if (pr.Args.Count == 0) return;
                    var fmt = Eval(pr.Args[0]).ToStr(Conv);
                    var args = pr.Args.Skip(1).Select(x => { var v = Eval(x); return v.IsNum ? v.N.ToString("R", CultureInfo.InvariantCulture) : v.ToStr(Conv); }).ToList();
                    string err;
                    Emit(Printf.Format(fmt, args, out err), pr.Redir, target);
                    return;
                }
                string line = pr.Args.Count == 0 ? Record() : string.Join(V("OFS"), pr.Args.Select(x => { var v = Eval(x); return v.IsNum ? NumOut(v.N) : v.ToStr(Conv); }).ToArray());
                Emit(line + V("ORS"), pr.Redir, target);
                return;
            }
            var ifs = s as SIf; if (ifs != null) { if (Cond(ifs.C)) Exec(ifs.A); else if (ifs.B != null) Exec(ifs.B); return; }
            var w = s as SWhile;
            if (w != null)
            {
                if (w.Do) { do { try { Exec(w.Body); } catch (BrkEx) { return; } catch (ContEx) { } steps++; } while (Cond(w.C)); return; }
                while (Cond(w.C)) { try { Exec(w.Body); } catch (BrkEx) { return; } catch (ContEx) { } if (++steps > 2000000) throw new ShError("program ran too long", 2); }
                return;
            }
            var f = s as SFor;
            if (f != null)
            {
                if (f.Init != null) Exec(f.Init);
                while (f.C == null || Cond(f.C))
                {
                    try { Exec(f.Body); } catch (BrkEx) { return; } catch (ContEx) { }
                    if (f.Step != null) Exec(f.Step);
                    if (++steps > 2000000) throw new ShError("program ran too long", 2);
                }
                return;
            }
            var fi = s as SForIn;
            if (fi != null)
            {
                var arr = Arr(fi.Arr);
                foreach (var k in arr.Keys.ToList())
                {
                    SetVar(fi.Var, Val.Field(k));
                    try { Exec(fi.Body); } catch (BrkEx) { return; } catch (ContEx) { }
                }
                return;
            }
            var del = s as SDelete;
            if (del != null) { var arr = Arr(del.Arr); if (del.Keys == null) arr.Clear(); else arr.Remove(Key(del.Keys)); return; }
            var sm = s as SSimple;
            if (sm != null)
            {
                switch (sm.K)
                {
                    case "break": throw new BrkEx();
                    case "continue": throw new ContEx();
                    case "next": case "nextfile": throw new NextEx();
                    case "exit": throw new ExitEx2 { Code = sm.E == null ? 0 : (int)Eval(sm.E).ToNum() };
                    case "return": throw new RetEx { V = sm.E == null ? Val.Str("") : Eval(sm.E) };
                }
            }
        }

        string NumOut(double n)
        {
            if (n == Math.Floor(n) && Math.Abs(n) < 1e16) return ((long)n).ToString(CultureInfo.InvariantCulture);
            string err;
            return Printf.Format(OFmt, new List<string> { n.ToString("R", CultureInfo.InvariantCulture) }, out err);
        }

        bool Cond(Node n)
        {
            var re = n as NRe;
            if (re != null) return Regex.IsMatch(Record(), Cmd.PosixToNet(re.V, true));
            return Eval(n).Truth();
        }

        Dictionary<string, Val> Arr(string name)
        {
            for (int i = frames.Count - 1; i >= 0; i--)
            {
                object o;
                if (frames[i].TryGetValue(name, out o)) { var d = o as Dictionary<string, Val>; if (d != null) return d; var nd = new Dictionary<string, Val>(); frames[i][name] = nd; return nd; }
                break;
            }
            Dictionary<string, Val> a;
            if (!arrays.TryGetValue(name, out a)) { a = new Dictionary<string, Val>(); arrays[name] = a; }
            return a;
        }
        string Key(List<Node> keys) { return string.Join(V("SUBSEP"), keys.Select(k => { var v = Eval(k); return v.IsNum ? NumOut(v.N) : v.ToStr(Conv); }).ToArray()); }

        Val GetVar(string name)
        {
            if (frames.Count > 0) { object o; if (frames[frames.Count - 1].TryGetValue(name, out o)) return o as Val ?? Val.Str(""); }
            if (name == "NF") { Record(); return Val.Num(fields.Count - 1); }
            Val v; return vars.TryGetValue(name, out v) ? v : Val.Str("");
        }
        void SetVar(string name, Val v)
        {
            if (frames.Count > 0 && frames[frames.Count - 1].ContainsKey(name)) { frames[frames.Count - 1][name] = v; return; }
            if (name == "NF")
            {
                int nf = (int)v.ToNum();
                Record();
                while (fields.Count - 1 < nf) fields.Add("");
                if (fields.Count - 1 > nf) fields.RemoveRange(nf + 1, fields.Count - 1 - nf);
                fieldsDirty = true;
            }
            vars[name] = v;
            if (name == "FS") { }
        }

        Val Assign(Node l, Val v)
        {
            var nv = l as NVar; if (nv != null) { SetVar(nv.Name, v); return v; }
            var ni = l as NIdx; if (ni != null) { Arr(ni.Name)[Key(ni.Keys)] = v; return v; }
            var nf = l as NField; if (nf != null) { SetField((int)Eval(nf.E).ToNum(), v.IsNum ? NumOut(v.N) : v.ToStr(Conv)); return v; }
            var ng = l as NGroup; if (ng != null) return Assign(ng.E, v);
            throw new ShError("assignment to a non-variable", 2);
        }

        Val Eval(Node n)
        {
            var num = n as NNum; if (num != null) return Val.Num(num.V);
            var str = n as NStr; if (str != null) return Val.Str(str.V);
            var re = n as NRe; if (re != null) return Val.Num(Regex.IsMatch(Record(), Cmd.PosixToNet(re.V, true)) ? 1 : 0);
            var grp = n as NGroup; if (grp != null) return Eval(grp.E);
            var nv = n as NVar; if (nv != null) return GetVar(nv.Name);
            var ni = n as NIdx; if (ni != null) { var arr = Arr(ni.Name); string k = Key(ni.Keys); Val v; if (!arr.TryGetValue(k, out v)) { v = Val.Str(""); arr[k] = v; } return v; }
            var nf = n as NField; if (nf != null) { int i = (int)Eval(nf.E).ToNum(); if (i < 0) throw new ShError("attempt to access field " + i, 2); return Val.Field(GetField(i)); }
            var asg = n as NAssign;
            if (asg != null)
            {
                var r = Eval(asg.R);
                if (asg.Op == "=") return Assign(asg.L, r.IsNum ? Val.Num(r.N) : (r.StrNum ? Val.Field(r.S) : Val.Str(r.S)));
                double cur = Eval(asg.L).ToNum(), rv = r.ToNum(), res;
                switch (asg.Op)
                {
                    case "+=": res = cur + rv; break;
                    case "-=": res = cur - rv; break;
                    case "*=": res = cur * rv; break;
                    case "/=": if (rv == 0) throw new ShError("division by zero in /=", 2); res = cur / rv; break;
                    case "%=": if (rv == 0) throw new ShError("division by zero in %=", 2); res = cur % rv; break;
                    default: res = Math.Pow(cur, rv); break;
                }
                return Assign(asg.L, Val.Num(res));
            }
            var inc = n as NIncDec;
            if (inc != null)
            {
                double cur = Eval(inc.L).ToNum();
                double nw = inc.Op == "++" ? cur + 1 : cur - 1;
                Assign(inc.L, Val.Num(nw));
                return Val.Num(inc.Post ? cur : nw);
            }
            var un = n as NUn;
            if (un != null)
            {
                var v = Eval(un.E);
                if (un.Op == "!") return Val.Num(v.Truth() ? 0 : 1);
                if (un.Op == "-") return Val.Num(-v.ToNum());
                return Val.Num(v.ToNum());
            }
            var cn = n as NCond; if (cn != null) return Cond(cn.C) ? Eval(cn.A) : Eval(cn.B);
            var mt = n as NMatch;
            if (mt != null)
            {
                string s = Eval(mt.L).ToStr(Conv);
                var rr = mt.R as NRe;
                string pat = rr != null ? rr.V : Eval(mt.R).ToStr(Conv);
                bool m = Regex.IsMatch(s, Cmd.PosixToNet(pat, true));
                return Val.Num(m != mt.Neg ? 1 : 0);
            }
            var nin = n as NIn; if (nin != null) return Val.Num(Arr(nin.Arr).ContainsKey(Key(nin.Keys)) ? 1 : 0);
            var gl = n as NGetline;
            if (gl != null)
            {
                if (gl.File != null)
                {
                    string fn = Eval(gl.File).ToStr(Conv);
                    List<string> lines;
                    if (!getlineFiles.TryGetValue(fn, out lines)) { try { lines = Cmd.Lines(Cmd.Read(sh, fn)); } catch (VfsEx) { return Val.Num(-1); } getlineFiles[fn] = lines; }
                    if (lines.Count == 0) return Val.Num(0);
                    string l = lines[0]; lines.RemoveAt(0);
                    if (gl.Var != null) Assign(gl.Var, Val.Field(l)); else SetRecord(l);
                    return Val.Num(1);
                }
                return Val.Num(0);
            }
            var bin = n as NBin;
            if (bin != null)
            {
                if (bin.Op == "&&") return Val.Num(Cond(bin.L) && Cond(bin.R) ? 1 : 0);
                if (bin.Op == "||") return Val.Num(Cond(bin.L) || Cond(bin.R) ? 1 : 0);
                var l = Eval(bin.L); var r = Eval(bin.R);
                switch (bin.Op)
                {
                    case "cat": return Val.Str((l.IsNum ? NumOut(l.N) : l.ToStr(Conv)) + (r.IsNum ? NumOut(r.N) : r.ToStr(Conv)));
                    case "+": return Val.Num(l.ToNum() + r.ToNum());
                    case "-": return Val.Num(l.ToNum() - r.ToNum());
                    case "*": return Val.Num(l.ToNum() * r.ToNum());
                    case "/": { double d = r.ToNum(); if (d == 0) throw new ShError("division by zero", 2); return Val.Num(l.ToNum() / d); }
                    case "%": { double d = r.ToNum(); if (d == 0) throw new ShError("division by zero in %", 2); return Val.Num(l.ToNum() % d); }
                    case "^": return Val.Num(Math.Pow(l.ToNum(), r.ToNum()));
                    default:
                        {
                            int c;
                            bool numeric = (l.IsNum || l.StrNum) && (r.IsNum || r.StrNum);
                            if (numeric) c = l.ToNum().CompareTo(r.ToNum());
                            else c = string.CompareOrdinal(l.IsNum ? NumOut(l.N) : l.ToStr(Conv), r.IsNum ? NumOut(r.N) : r.ToStr(Conv));
                            bool res = bin.Op == "<" ? c < 0 : bin.Op == "<=" ? c <= 0 : bin.Op == ">" ? c > 0 : bin.Op == ">=" ? c >= 0 : bin.Op == "==" ? c == 0 : c != 0;
                            return Val.Num(res ? 1 : 0);
                        }
                }
            }
            var call = n as NCall; if (call != null) return Call(call);
            return Val.Str("");
        }
        readonly Dictionary<string, List<string>> getlineFiles = new Dictionary<string, List<string>>();

        Val Call(NCall c)
        {
            if (!c.Builtin)
            {
                Func f;
                if (!funcs.TryGetValue(c.Name, out f)) throw new ShError("function `" + c.Name + "' not defined", 2);
                var frame = new Dictionary<string, object>();
                for (int i = 0; i < f.Params.Count; i++)
                {
                    if (i < c.Args.Count)
                    {
                        var an = c.Args[i] as NVar;
                        if (an != null && (arrays.ContainsKey(an.Name) || (frames.Count > 0 && frames[frames.Count - 1].ContainsKey(an.Name) && frames[frames.Count - 1][an.Name] is Dictionary<string, Val>))) frame[f.Params[i]] = Arr(an.Name);
                        else frame[f.Params[i]] = Eval(c.Args[i]);
                    }
                    else frame[f.Params[i]] = Val.Str("");
                }
                if (frames.Count > 200) throw new ShError("function call nesting too deep", 2);
                frames.Add(frame);
                try { Exec(f.Body); return Val.Str(""); }
                catch (RetEx r) { return r.V; }
                finally { frames.RemoveAt(frames.Count - 1); }
            }
            Func<int, string> S = i => i < c.Args.Count ? Eval(c.Args[i]).ToStr(Conv) : "";
            Func<int, double> N = i => i < c.Args.Count ? Eval(c.Args[i]).ToNum() : 0;
            switch (c.Name)
            {
                case "length":
                    {
                        if (c.Args.Count == 0) return Val.Num(Record().Length);
                        var an = c.Args[0] as NVar;
                        if (an != null && arrays.ContainsKey(an.Name)) return Val.Num(arrays[an.Name].Count);
                        return Val.Num(S(0).Length);
                    }
                case "substr":
                    {
                        string s = S(0);
                        double start = Math.Round(N(1));
                        double len = c.Args.Count > 2 ? Math.Round(N(2)) : double.MaxValue / 4;
                        double end = start + len;
                        if (start < 1) start = 1;
                        if (end > s.Length + 1) end = s.Length + 1;
                        if (end <= start) return Val.Str("");
                        return Val.Str(s.Substring((int)start - 1, (int)(end - start)));
                    }
                case "index": return Val.Num(S(0).IndexOf(S(1), StringComparison.Ordinal) + 1);
                case "split":
                    {
                        string s = S(0);
                        var an = c.Args[1] as NVar;
                        var arr = Arr(an == null ? "_" : an.Name);
                        arr.Clear();
                        string fs = c.Args.Count > 2 ? (c.Args[2] is NRe ? ((NRe)c.Args[2]).V : S(2)) : V("FS");
                        string[] parts;
                        if (s.Length == 0) parts = new string[0];
                        else if (fs == " ") parts = Regex.Split(s.Trim(), "\\s+");
                        else if (fs.Length == 1) parts = s.Split(fs[0]);
                        else parts = Regex.Split(s, Cmd.PosixToNet(fs, true));
                        for (int i = 0; i < parts.Length; i++) arr[(i + 1).ToString()] = Val.Field(parts[i]);
                        return Val.Num(parts.Length);
                    }
                case "sub": case "gsub":
                    {
                        string pat = c.Args[0] is NRe ? ((NRe)c.Args[0]).V : S(0);
                        string rep = S(1);
                        Node target = c.Args.Count > 2 ? c.Args[2] : new NField { E = new NNum { V = 0 } };
                        string s = Eval(target).ToStr(Conv);
                        int count = 0;
                        var rx = new Regex(Cmd.PosixToNet(pat, true));
                        string res = rx.Replace(s, m =>
                        {
                            if (c.Name == "sub" && count > 0) return m.Value;
                            count++;
                            var sb = new StringBuilder();
                            for (int i = 0; i < rep.Length; i++)
                            {
                                if (rep[i] == '\\' && i + 1 < rep.Length && rep[i + 1] == '&') { sb.Append('&'); i++; }
                                else if (rep[i] == '&') sb.Append(m.Value);
                                else sb.Append(rep[i]);
                            }
                            return sb.ToString();
                        }, c.Name == "sub" ? 1 : -1);
                        if (count > 0) Assign(target, Val.Str(res));
                        return Val.Num(count);
                    }
                case "match":
                    {
                        string pat = c.Args[1] is NRe ? ((NRe)c.Args[1]).V : S(1);
                        var m = Regex.Match(S(0), Cmd.PosixToNet(pat, true));
                        vars["RSTART"] = Val.Num(m.Success ? m.Index + 1 : 0);
                        vars["RLENGTH"] = Val.Num(m.Success ? m.Length : -1);
                        return Val.Num(m.Success ? m.Index + 1 : 0);
                    }
                case "sprintf":
                    {
                        string err;
                        var args = c.Args.Skip(1).Select(x => { var v = Eval(x); return v.IsNum ? v.N.ToString("R", CultureInfo.InvariantCulture) : v.ToStr(Conv); }).ToList();
                        return Val.Str(Printf.Format(S(0), args, out err));
                    }
                case "tolower": return Val.Str(S(0).ToLowerInvariant());
                case "toupper": return Val.Str(S(0).ToUpperInvariant());
                case "int": return Val.Num(Math.Truncate(N(0)));
                case "sqrt": return Val.Num(Math.Sqrt(N(0)));
                case "exp": return Val.Num(Math.Exp(N(0)));
                case "log": return Val.Num(Math.Log(N(0)));
                case "sin": return Val.Num(Math.Sin(N(0)));
                case "cos": return Val.Num(Math.Cos(N(0)));
                case "atan2": return Val.Num(Math.Atan2(N(0), N(1)));
                case "rand": return Val.Num(rnd.NextDouble());
                case "srand": rnd = new Random(c.Args.Count > 0 ? (int)N(0) : (int)sh.M.Now); return Val.Num(0);
                case "systime": return Val.Num(sh.M.Now);
                case "strftime": return Val.Str(sh.M.LocalTime(c.Args.Count > 1 ? (long)N(1) : sh.M.Now).ToString("ddd MMM dd HH:mm:ss yyyy", CultureInfo.InvariantCulture));
                case "system":
                    {
                        io.P(outBuf.ToString()); outBuf.Clear();
                        int st = sh.Subshell().RunText(S(0), io);
                        return Val.Num(st);
                    }
                case "close": case "fflush": return Val.Num(0);
            }
            return Val.Str("");
        }
    }
}
