// Cert Lab - the bash-like shell, part 1: the lexer and parser. Words keep their quotes (they are expanded when
// the command runs); lists, pipelines, if / for / while / until / case, functions, { }, ( ), [[ ]], (( )),
// redirections and here-documents. Input that is not finished yet (an open quote, an if without fi...) throws
// ShIncomplete, so the terminal asks for the next line with the "> " prompt like bash does.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CertLab
{
    public class ShIncomplete : Exception { public ShIncomplete(string m) : base(m) { } }
    public class ShSyntax : Exception { public ShSyntax(string m) : base(m) { } }

    public abstract class ShNode { public int Line; }
    public class ShList : ShNode { public List<ShNode> Items = new List<ShNode>(); public List<bool> Background = new List<bool>(); }
    public class ShAndOr : ShNode { public List<ShNode> Parts = new List<ShNode>(); public List<string> Ops = new List<string>(); }
    public class ShPipe : ShNode { public List<ShNode> Cmds = new List<ShNode>(); public bool Negate; public string Text = ""; }
    public class ShRedir { public int Fd = -1; public string Op; public string Target; public string Here; public bool HereQuoted; }
    public class ShSimple : ShNode { public List<string> Assigns = new List<string>(); public List<string> Words = new List<string>(); public List<ShRedir> Redirs = new List<ShRedir>(); }
    public class ShCompound : ShNode { public List<ShRedir> Redirs = new List<ShRedir>(); }
    public class ShIf : ShCompound { public List<ShNode> Conds = new List<ShNode>(); public List<ShNode> Bodies = new List<ShNode>(); public ShNode Else; }
    public class ShFor : ShCompound { public string Var; public List<string> Words; public ShNode Body; public string CInit, CCond, CStep; }
    public class ShWhile : ShCompound { public ShNode Cond; public ShNode Body; public bool Until; }
    public class ShCase : ShCompound { public string Word; public List<List<string>> Patterns = new List<List<string>>(); public List<ShNode> Bodies = new List<ShNode>(); public List<string> Ends = new List<string>(); }
    public class ShGroup : ShCompound { public ShNode Body; public bool Subshell; }
    public class ShFunc : ShNode { public string Name; public ShNode Body; public string Source = ""; }
    public class ShCond : ShCompound { public List<string> Words = new List<string>(); }
    public class ShArith : ShCompound { public string Expr; }

    public class ShTok
    {
        public string T;      // "w" word, "op" operator, "nl" newline, "eof"
        public string V = "";
        public int Fd = -1;   // an IO number before a redirection
        public int Line;
        public override string ToString() { return T + ":" + V; }
    }

    public class ShLexer
    {
        readonly string s;
        int i;
        int line = 1;
        public readonly List<ShRedir> PendingHere = new List<ShRedir>();
        ShTok peeked;
        public ShLexer(string src) { s = src; }
        public int Pos { get { return i; } }

        static readonly string[] Ops = { ";;&", "&>>", "<<<", "<<-", "&&", "||", ";;", ";&", "|&", "<<", ">>", "<&", ">&", "<>", ">|", "&>", "((", "|", ";", "&", "(", ")", "<", ">" };

        public ShTok Peek() { if (peeked == null) peeked = Read(); return peeked; }
        public ShTok Next() { var t = Peek(); peeked = null; return t; }
        public void Unread(ShTok t) { peeked = t; }

        bool IsMeta(char c) { return c == ' ' || c == '\t' || c == '\n' || c == ';' || c == '&' || c == '|' || c == '<' || c == '>' || c == '(' || c == ')'; }

        ShTok Read()
        {
            for (;;)
            {
                while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r')) i++;
                if (i + 1 < s.Length && s[i] == '\\' && s[i + 1] == '\n') { i += 2; line++; continue; }
                if (i < s.Length && s[i] == '#') { while (i < s.Length && s[i] != '\n') i++; continue; }
                break;
            }
            if (i >= s.Length)
            {
                if (PendingHere.Count > 0) throw new ShIncomplete("here-document");
                return new ShTok { T = "eof", Line = line };
            }
            char c = s[i];
            if (c == '\n')
            {
                i++;
                var t = new ShTok { T = "nl", Line = line };
                line++;
                if (PendingHere.Count > 0) ReadHereDocs();
                return t;
            }
            // an IO number: digits then < or >
            int j = i;
            while (j < s.Length && char.IsDigit(s[j])) j++;
            if (j > i && j < s.Length && (s[j] == '<' || s[j] == '>'))
            {
                int fd = int.Parse(s.Substring(i, j - i));
                i = j;
                var op = ReadOp();
                op.Fd = fd;
                return op;
            }
            foreach (var o in Ops)
                if (string.CompareOrdinal(s, i, o, 0, o.Length) == 0) return ReadOp();
            return new ShTok { T = "w", V = ReadWord(), Line = line };
        }

        ShTok ReadOp()
        {
            foreach (var o in Ops)
                if (string.CompareOrdinal(s, i, o, 0, o.Length) == 0)
                {
                    i += o.Length;
                    return new ShTok { T = "op", V = o, Line = line };
                }
            throw new ShSyntax("syntax error near unexpected token `" + s[i] + "'");
        }

        string ReadWord()
        {
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i];
                if (IsMeta(c))
                {
                    // name=( ... ): an array assignment keeps its parentheses in the word
                    if (c == '(' && sb.Length > 0 && sb[sb.Length - 1] == '=' && IsAssignStart(sb.ToString()))
                    {
                        int st = i;
                        SkipBalanced('(', ')');
                        sb.Append(s, st, i - st);
                        continue;
                    }
                    // extglob-like or [[ =~ ( ]] patterns are not supported; stop here
                    break;
                }
                if (c == '\\')
                {
                    if (i + 1 < s.Length && s[i + 1] == '\n') { i += 2; line++; continue; }
                    sb.Append(c); i++;
                    if (i < s.Length) { sb.Append(s[i]); i++; }
                    continue;
                }
                if (c == '\'')
                {
                    int e = s.IndexOf('\'', i + 1);
                    if (e < 0) throw new ShIncomplete("quote");
                    sb.Append(s, i, e - i + 1);
                    CountLines(i, e);
                    i = e + 1;
                    continue;
                }
                if (c == '"') { int st = i; SkipDouble(); sb.Append(s, st, i - st); continue; }
                if (c == '`') { int st = i; SkipBacktick(); sb.Append(s, st, i - st); continue; }
                if (c == '$' && i + 1 < s.Length && (s[i + 1] == '(' || s[i + 1] == '{'))
                {
                    int st = i; i++;
                    if (s[i] == '(') SkipBalanced('(', ')'); else SkipBalanced('{', '}');
                    sb.Append(s, st, i - st);
                    continue;
                }
                if (c == '$' && i + 1 < s.Length && s[i + 1] == '\'')
                {
                    // $'...' ANSI-C quoting
                    int st = i; i += 2;
                    while (i < s.Length && s[i] != '\'') { if (s[i] == '\\') i++; i++; }
                    if (i >= s.Length) throw new ShIncomplete("quote");
                    i++;
                    sb.Append(s, st, i - st);
                    continue;
                }
                sb.Append(c); i++;
            }
            return sb.ToString();
        }

        static bool IsAssignStart(string w)
        {
            int eq = w.IndexOf('=');
            if (eq <= 0) return false;
            string n = w.Substring(0, eq).TrimEnd('+');
            if (n.Length == 0 || !(char.IsLetter(n[0]) || n[0] == '_')) return false;
            return n.All(ch => char.IsLetterOrDigit(ch) || ch == '_');
        }

        void CountLines(int a, int b) { for (int k = a; k <= b && k < s.Length; k++) if (s[k] == '\n') line++; }

        void SkipDouble()
        {
            i++; // opening "
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '\\') { i += 2; continue; }
                if (c == '"') { i++; return; }
                if (c == '\n') line++;
                if (c == '`') { SkipBacktick(); continue; }
                if (c == '$' && i + 1 < s.Length && (s[i + 1] == '(' || s[i + 1] == '{')) { i++; if (s[i] == '(') SkipBalanced('(', ')'); else SkipBalanced('{', '}'); continue; }
                i++;
            }
            throw new ShIncomplete("quote");
        }

        void SkipBacktick()
        {
            i++;
            while (i < s.Length)
            {
                if (s[i] == '\\') { i += 2; continue; }
                if (s[i] == '`') { i++; return; }
                if (s[i] == '\n') line++;
                i++;
            }
            throw new ShIncomplete("backquote");
        }

        /// <summary>From an opening bracket at i to just after its match, skipping quotes inside.</summary>
        void SkipBalanced(char open, char close)
        {
            int depth = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '\\') { i += 2; continue; }
                if (c == '\'' && open == '(') { int e = s.IndexOf('\'', i + 1); if (e < 0) throw new ShIncomplete("quote"); i = e + 1; continue; }
                if (c == '"') { SkipDouble(); continue; }
                if (c == '`') { SkipBacktick(); continue; }
                if (c == '\n') line++;
                if (c == open) depth++;
                else if (c == close) { depth--; if (depth == 0) { i++; return; } }
                i++;
            }
            throw new ShIncomplete("bracket");
        }

        /// <summary>The raw text of (( ... )) after the opening ((.</summary>
        public string ReadArithBody()
        {
            peeked = null;
            int depth = 0;
            int st = i;
            while (i < s.Length)
            {
                if (s[i] == '(') depth++;
                else if (s[i] == ')')
                {
                    if (depth == 0 && i + 1 < s.Length && s[i + 1] == ')') { string body = s.Substring(st, i - st); i += 2; return body; }
                    depth--;
                }
                i++;
            }
            throw new ShIncomplete("arith");
        }

        void ReadHereDocs()
        {
            foreach (var h in PendingHere)
            {
                var body = new StringBuilder();
                bool found = false;
                while (i < s.Length)
                {
                    int e = s.IndexOf('\n', i);
                    string ln = e < 0 ? s.Substring(i) : s.Substring(i, e - i);
                    i = e < 0 ? s.Length : e + 1;
                    line++;
                    string cmp = h.Op == "<<-" ? ln.TrimStart('\t') : ln;
                    if (cmp.TrimEnd('\r') == h.Target) { found = true; break; }
                    if (e < 0 && i >= s.Length) { body.Append(h.Op == "<<-" ? ln.TrimStart('\t') : ln); break; }
                    body.Append(h.Op == "<<-" ? ln.TrimStart('\t') : ln).Append('\n');
                }
                if (!found) throw new ShIncomplete("here-document");
                h.Here = body.ToString();
            }
            PendingHere.Clear();
        }
    }

    public class ShParser
    {
        readonly ShLexer lx;
        readonly string src;
        public ShParser(string s) { src = s; lx = new ShLexer(s); }

        public static ShList Parse(string s)
        {
            var p = new ShParser(s);
            var l = p.ParseList(new string[0]);
            var t = p.lx.Peek();
            if (t.T != "eof") throw new ShSyntax("syntax error near unexpected token `" + t.V + "'");
            return l;
        }

        static readonly HashSet<string> Reserved = new HashSet<string> { "then", "else", "elif", "fi", "do", "done", "esac", "}", "in" };

        bool IsWord(ShTok t, string w) { return t.T == "w" && t.V == w; }
        void SkipNl() { while (lx.Peek().T == "nl") lx.Next(); }

        /// <summary>A list of and-or lists, until one of the stop words (then, fi, done...) or ) / eof.</summary>
        ShList ParseList(string[] stops)
        {
            var list = new ShList();
            for (;;)
            {
                SkipNl();
                var t = lx.Peek();
                if (t.T == "eof") break;
                if (t.T == "w" && stops.Contains(t.V)) break;
                if (t.T == "op" && (t.V == ")" || t.V == ";;" || t.V == ";&" || t.V == ";;&")) break;
                if (t.T == "op" && (t.V == ";" || t.V == "&")) throw new ShSyntax("syntax error near unexpected token `" + t.V + "'");
                var ao = ParseAndOr();
                bool bg = false;
                var n = lx.Peek();
                if (n.T == "op" && n.V == "&") { lx.Next(); bg = true; }
                else if (n.T == "op" && n.V == ";") lx.Next();
                list.Items.Add(ao); list.Background.Add(bg);
            }
            return list;
        }

        ShNode ParseAndOr()
        {
            var ao = new ShAndOr();
            ao.Parts.Add(ParsePipe());
            for (;;)
            {
                var t = lx.Peek();
                if (t.T == "op" && (t.V == "&&" || t.V == "||"))
                {
                    lx.Next(); SkipNl();
                    if (lx.Peek().T == "eof") throw new ShIncomplete("and-or");
                    ao.Ops.Add(t.V); ao.Parts.Add(ParsePipe());
                }
                else break;
            }
            return ao.Parts.Count == 1 ? ao.Parts[0] : ao;
        }

        ShNode ParsePipe()
        {
            var p = new ShPipe();
            int st = lx.Pos;
            if (IsWord(lx.Peek(), "!")) { lx.Next(); p.Negate = true; }
            p.Cmds.Add(ParseCommand());
            for (;;)
            {
                var t = lx.Peek();
                if (t.T == "op" && (t.V == "|" || t.V == "|&"))
                {
                    lx.Next(); SkipNl();
                    if (lx.Peek().T == "eof") throw new ShIncomplete("pipe");
                    if (t.V == "|&") { var last = p.Cmds[p.Cmds.Count - 1]; AddRedir(last, new ShRedir { Fd = 2, Op = ">&", Target = "1" }); }
                    p.Cmds.Add(ParseCommand());
                }
                else break;
            }
            int en = Math.Min(lx.Pos, src.Length);
            p.Text = st < en ? src.Substring(st, en - st).Trim() : "";
            return p;
        }

        static void AddRedir(ShNode n, ShRedir r)
        {
            var s = n as ShSimple; if (s != null) { s.Redirs.Add(r); return; }
            var c = n as ShCompound; if (c != null) c.Redirs.Add(r);
        }

        ShNode ParseCommand()
        {
            var t = lx.Peek();
            if (t.T == "eof") throw new ShIncomplete("command");
            if (t.T == "op" && t.V == "((")
            {
                lx.Next();
                var a = new ShArith { Expr = lx.ReadArithBody(), Line = t.Line };
                ParseRedirs(a.Redirs);
                return a;
            }
            if (t.T == "op" && t.V == "(")
            {
                lx.Next();
                var g = new ShGroup { Subshell = true, Line = t.Line };
                g.Body = ParseList(new string[0]);
                Expect(")");
                ParseRedirs(g.Redirs);
                return g;
            }
            if (t.T == "w")
            {
                switch (t.V)
                {
                    case "if": return ParseIf();
                    case "for": return ParseFor();
                    case "while": case "until": return ParseWhile();
                    case "case": return ParseCase();
                    case "{":
                        {
                            lx.Next();
                            var g = new ShGroup { Line = t.Line };
                            g.Body = ParseList(new[] { "}" });
                            ExpectWord("}");
                            ParseRedirs(g.Redirs);
                            return g;
                        }
                    case "[[": return ParseCond();
                    case "function":
                        {
                            lx.Next();
                            var nm = lx.Next();
                            if (nm.T != "w") throw new ShSyntax("syntax error near unexpected token `" + nm.V + "'");
                            string name = nm.V;
                            if (lx.Peek().T == "op" && lx.Peek().V == "(") { lx.Next(); Expect(")"); }
                            return ParseFuncBody(name, t.Line);
                        }
                }
                if (Reserved.Contains(t.V)) throw new ShSyntax("syntax error near unexpected token `" + t.V + "'");
            }
            return ParseSimple();
        }

        ShNode ParseFuncBody(string name, int line)
        {
            SkipNl();
            int st = lx.Pos;
            var body = ParseCommand();
            return new ShFunc { Name = name, Body = body, Line = line, Source = src.Substring(Math.Max(0, st), Math.Max(0, Math.Min(lx.Pos, src.Length) - st)) };
        }

        void Expect(string op)
        {
            SkipNl();
            var t = lx.Next();
            if (t.T == "eof") throw new ShIncomplete(op);
            if (t.T != "op" || t.V != op) throw new ShSyntax("syntax error near unexpected token `" + (t.T == "nl" ? "newline" : t.V) + "'");
        }
        void ExpectWord(string w)
        {
            SkipNl();
            var t = lx.Next();
            if (t.T == "eof") throw new ShIncomplete(w);
            if (!IsWord(t, w)) throw new ShSyntax("syntax error near unexpected token `" + (t.T == "nl" ? "newline" : t.V) + "'");
        }

        ShNode ParseIf()
        {
            var n = new ShIf { Line = lx.Next().Line };
            n.Conds.Add(ParseList(new[] { "then" }));
            ExpectWord("then");
            n.Bodies.Add(ParseList(new[] { "elif", "else", "fi" }));
            for (;;)
            {
                SkipNl();
                var t = lx.Next();
                if (t.T == "eof") throw new ShIncomplete("fi");
                if (IsWord(t, "elif")) { n.Conds.Add(ParseList(new[] { "then" })); ExpectWord("then"); n.Bodies.Add(ParseList(new[] { "elif", "else", "fi" })); continue; }
                if (IsWord(t, "else")) { n.Else = ParseList(new[] { "fi" }); ExpectWord("fi"); break; }
                if (IsWord(t, "fi")) break;
                throw new ShSyntax("syntax error near unexpected token `" + t.V + "'");
            }
            ParseRedirs(n.Redirs);
            return n;
        }

        ShNode ParseFor()
        {
            var n = new ShFor { Line = lx.Next().Line };
            var t = lx.Peek();
            if (t.T == "op" && t.V == "((")
            {
                lx.Next();
                var body = lx.ReadArithBody();
                var parts = body.Split(';');
                if (parts.Length != 3) throw new ShSyntax("syntax error: arithmetic expression required");
                n.CInit = parts[0]; n.CCond = parts[1]; n.CStep = parts[2];
                t = lx.Peek();
                if (t.T == "op" && t.V == ";") lx.Next();
            }
            else
            {
                var v = lx.Next();
                if (v.T == "eof") throw new ShIncomplete("for");
                if (v.T != "w") throw new ShSyntax("syntax error near unexpected token `" + v.V + "'");
                n.Var = v.V;
                SkipNlIfIn();
                t = lx.Peek();
                if (IsWord(t, "in"))
                {
                    lx.Next();
                    n.Words = new List<string>();
                    for (;;)
                    {
                        var w = lx.Peek();
                        if (w.T == "w") { lx.Next(); n.Words.Add(w.V); continue; }
                        if (w.T == "op" && w.V == ";") { lx.Next(); break; }
                        if (w.T == "nl") { lx.Next(); break; }
                        if (w.T == "eof") throw new ShIncomplete("for");
                        throw new ShSyntax("syntax error near unexpected token `" + w.V + "'");
                    }
                }
                else if (t.T == "op" && t.V == ";") lx.Next();
            }
            SkipNl();
            ExpectWord("do");
            n.Body = ParseList(new[] { "done" });
            ExpectWord("done");
            ParseRedirs(n.Redirs);
            return n;
        }
        void SkipNlIfIn()
        {
            // "for x\nin a b" is allowed
            var t = lx.Peek();
            if (t.T == "nl") { lx.Next(); var u = lx.Peek(); if (!IsWord(u, "in") && !IsWord(u, "do")) { } }
        }

        ShNode ParseWhile()
        {
            var kw = lx.Next();
            var n = new ShWhile { Until = kw.V == "until", Line = kw.Line };
            n.Cond = ParseList(new[] { "do" });
            ExpectWord("do");
            n.Body = ParseList(new[] { "done" });
            ExpectWord("done");
            ParseRedirs(n.Redirs);
            return n;
        }

        ShNode ParseCase()
        {
            var n = new ShCase { Line = lx.Next().Line };
            var w = lx.Next();
            if (w.T == "eof") throw new ShIncomplete("case");
            n.Word = w.V;
            SkipNl();
            ExpectWord("in");
            for (;;)
            {
                SkipNl();
                var t = lx.Peek();
                if (t.T == "eof") throw new ShIncomplete("esac");
                if (IsWord(t, "esac")) { lx.Next(); break; }
                if (t.T == "op" && t.V == "(") lx.Next();
                var pats = new List<string>();
                for (;;)
                {
                    var p = lx.Next();
                    if (p.T == "eof") throw new ShIncomplete("esac");
                    if (p.T != "w") throw new ShSyntax("syntax error near unexpected token `" + p.V + "'");
                    pats.Add(p.V);
                    var s = lx.Next();
                    if (s.T == "op" && s.V == "|") continue;
                    if (s.T == "op" && s.V == ")") break;
                    if (s.T == "eof") throw new ShIncomplete("esac");
                    throw new ShSyntax("syntax error near unexpected token `" + s.V + "'");
                }
                var body = ParseList(new[] { "esac" });
                n.Patterns.Add(pats); n.Bodies.Add(body);
                SkipNl();
                var e = lx.Peek();
                if (e.T == "op" && (e.V == ";;" || e.V == ";&" || e.V == ";;&")) { lx.Next(); n.Ends.Add(e.V); }
                else n.Ends.Add(";;");
            }
            ParseRedirs(n.Redirs);
            return n;
        }

        ShNode ParseCond()
        {
            var n = new ShCond { Line = lx.Next().Line };
            for (;;)
            {
                var t = lx.Next();
                if (t.T == "eof" || t.T == "nl") throw new ShIncomplete("]]");
                if (IsWord(t, "]]")) break;
                if (t.T == "op") { n.Words.Add("\u0001" + t.V); continue; }   // && || < > ( ) inside [[ ]]
                n.Words.Add(t.V);
            }
            ParseRedirs(n.Redirs);
            return n;
        }

        void ParseRedirs(List<ShRedir> list)
        {
            for (;;)
            {
                var t = lx.Peek();
                if (t.T == "op" && IsRedirOp(t.V)) { lx.Next(); list.Add(ReadRedir(t)); }
                else break;
            }
        }

        static bool IsRedirOp(string v) { return v == "<" || v == ">" || v == ">>" || v == "<<" || v == "<<-" || v == "<<<" || v == "<&" || v == ">&" || v == "<>" || v == ">|" || v == "&>" || v == "&>>"; }

        ShRedir ReadRedir(ShTok op)
        {
            var w = lx.Next();
            if (w.T == "eof") throw new ShIncomplete("redirect");
            if (w.T != "w") throw new ShSyntax("syntax error near unexpected token `" + (w.T == "nl" ? "newline" : w.V) + "'");
            var r = new ShRedir { Fd = op.Fd, Op = op.V, Target = w.V };
            if (op.V == "<<" || op.V == "<<-")
            {
                string d = w.V;
                r.HereQuoted = d.IndexOf('\'') >= 0 || d.IndexOf('"') >= 0 || d.IndexOf('\\') >= 0;
                r.Target = d.Replace("'", "").Replace("\"", "").Replace("\\", "");
                lx.PendingHere.Add(r);
            }
            return r;
        }

        ShNode ParseSimple()
        {
            var n = new ShSimple { Line = lx.Peek().Line };
            for (;;)
            {
                var t = lx.Peek();
                if (t.T == "w")
                {
                    if (n.Words.Count == 0 && IsAssign(t.V)) { lx.Next(); n.Assigns.Add(t.V); continue; }
                    lx.Next();
                    // name () { ... } : a function definition
                    if (n.Words.Count == 0 && n.Assigns.Count == 0 && lx.Peek().T == "op" && lx.Peek().V == "(")
                    {
                        lx.Next();
                        var c = lx.Next();
                        if (c.T != "op" || c.V != ")") throw new ShSyntax("syntax error near unexpected token `" + c.V + "'");
                        return ParseFuncBody(t.V, t.Line);
                    }
                    n.Words.Add(t.V);
                    continue;
                }
                if (t.T == "op" && IsRedirOp(t.V)) { lx.Next(); n.Redirs.Add(ReadRedir(t)); continue; }
                break;
            }
            if (n.Words.Count == 0 && n.Assigns.Count == 0 && n.Redirs.Count == 0)
            {
                var t = lx.Peek();
                if (t.T == "eof") throw new ShIncomplete("command");
                throw new ShSyntax("syntax error near unexpected token `" + (t.T == "nl" ? "newline" : t.V) + "'");
            }
            return n;
        }

        public static bool IsAssign(string w)
        {
            int eq = w.IndexOf('=');
            if (eq <= 0) return false;
            string n = w.Substring(0, eq);
            if (n.EndsWith("+", StringComparison.Ordinal)) n = n.Substring(0, n.Length - 1);
            int br = n.IndexOf('[');
            if (br > 0) { if (!n.EndsWith("]", StringComparison.Ordinal)) return false; n = n.Substring(0, br); }
            if (n.Length == 0 || !(char.IsLetter(n[0]) || n[0] == '_')) return false;
            foreach (char c in n) if (!(char.IsLetterOrDigit(c) || c == '_')) return false;
            return true;
        }
    }
}
