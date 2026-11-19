// Cert Lab - the simulated PowerShell's parser: the PowerShell language the course uses, read straight from the text
// in its two modes (command mode: Get-Item -Path C:\x, barewords are strings; expression mode: $a -eq 5, numbers,
// operators). Statements (if, switch, foreach, for, while, do, function, try / catch / finally, throw, return,
// break, continue, exit, Configuration), pipelines, assignments, script blocks with param / begin / process / end,
// strings with $var and $( ) expansion, here-strings, hashtables, arrays, casts, member access and method calls.
// An unfinished statement throws PsParseError with Incomplete = true, so the console shows the >> prompt.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public class PsParseError : Exception
    {
        public int Pos, Len = 1; public bool Incomplete; public string Id; public string Text = "", File = "";
        public PsParseError(string msg, int pos, string id, bool incomplete) : base(msg) { Pos = pos; Id = id; Incomplete = incomplete; }
    }

    // ------------------------------------------------------------------ the syntax tree
    public abstract class PsAst { public int Pos, End; public PsSource Src; public string Extent { get { return Src == null ? "" : Src.Text.Substring(Math.Max(0, Pos), Math.Max(0, Math.Min(Src.Text.Length, End) - Pos)); } } }
    public class PsSource { public string Text = "", File = ""; }

    public class StmtBlock : PsAst { public List<PsAst> Stmts = new List<PsAst>(); }
    public class PipelineAst : PsAst { public List<PsAst> Elements = new List<PsAst>(); }       // CommandAst or CmdExprAst
    public class Redir { public string Stream = "1", Op = ">"; public PsAst Target; public string MergeTo; }   // 2>&1: Stream=2, MergeTo=1
    public class CmdElem { public string Param; public PsAst Arg; public bool ColonArg, Splat; public int Pos; public string Text = ""; }
    public class CommandAst : PsAst
    {
        public string Name; public PsAst NameExpr; public string Invoke;   // "&", "." or null
        public List<CmdElem> Elems = new List<CmdElem>();
        public List<Redir> Redirs = new List<Redir>();
    }
    public class CmdExprAst : PsAst { public PsAst Expr; public List<Redir> Redirs = new List<Redir>(); }
    public class AssignAst : PsAst { public PsAst Target; public string Op = "="; public PsAst Value; }
    public class IfAst : PsAst { public List<PsAst> Conds = new List<PsAst>(); public List<StmtBlock> Blocks = new List<StmtBlock>(); public StmtBlock Else; }
    public class SwitchClause { public PsAst Cond; public bool IsDefault; public StmtBlock Body; }
    public class SwitchAst : PsAst { public string Label; public bool Regex, Wildcard, Exact, CaseSensitive, File; public PsAst Value; public List<SwitchClause> Clauses = new List<SwitchClause>(); }
    public class ForEachAst : PsAst { public string Label, Var; public PsAst Coll; public StmtBlock Body; }
    public class ForAst : PsAst { public string Label; public PsAst Init, Cond, Iter; public StmtBlock Body; }
    public class WhileAst : PsAst { public string Label; public PsAst Cond; public StmtBlock Body; public bool Do, Until; }
    public class FunctionAst : PsAst { public string Name; public bool Filter; public ScriptBlockAst Body; }
    public class CatchClause { public List<string> Types = new List<string>(); public StmtBlock Body; }
    public class TryAst : PsAst { public StmtBlock Body; public List<CatchClause> Catches = new List<CatchClause>(); public StmtBlock Finally; }
    public class TrapAst : PsAst { public string Type; public StmtBlock Body; }
    public class FlowAst : PsAst { public string Kind; public PsAst Value; public string Label; }   // throw, return, break, continue, exit
    public class ConfigAst : PsAst { public string Name; public ScriptBlockAst Body; }
    public class NodeAst : PsAst { public PsAst Names; public StmtBlock Body; }
    public class ResourceAst : PsAst { public string Type; public PsAst Name; public HashAst Props; }
    public class DataAst : PsAst { public StmtBlock Body; }

    // expressions
    public class ConstAst : PsAst { public object Value; }
    public class StrAst : PsAst { public List<object> Parts = new List<object>(); public bool Here; }   // string pieces and PsAst pieces
    public class VarAst : PsAst { public string Name = "", Scope = ""; public bool Splat, Braced; public string Full { get { return Scope.Length > 0 ? Scope + ":" + Name : Name; } } }
    public class ArrayLitAst : PsAst { public List<PsAst> Items = new List<PsAst>(); }
    public class ArrayExprAst : PsAst { public StmtBlock Body; }
    public class SubExprAst : PsAst { public StmtBlock Body; }
    public class ParenAst : PsAst { public PsAst Inner; }
    public class HashAst : PsAst { public List<KeyValuePair<PsAst, PsAst>> Pairs = new List<KeyValuePair<PsAst, PsAst>>(); public bool Ordered; }
    public class BinAst : PsAst { public string Op; public PsAst L, R; }
    public class UnAst : PsAst { public string Op; public PsAst X; public bool Postfix; }
    public class CastAst : PsAst { public string Type; public PsAst X; }
    public class TypeAst : PsAst { public string Type; }
    public class MemberAst : PsAst { public PsAst Target; public PsAst Name; public bool Static; }
    public class InvokeAst : PsAst { public PsAst Target; public PsAst Name; public bool Static; public List<PsAst> Args = new List<PsAst>(); }
    public class IndexAst : PsAst { public PsAst Target; public PsAst Index; }
    public class ScriptBlockAst : PsAst
    {
        public List<ParamAst> Params; public List<AttrAst> BlockAttrs = new List<AttrAst>();
        public StmtBlock Begin, Process, EndBlock, Body;   // Body = statements without named blocks (the end block)
        public string Text = "", Help;
        public bool HasParamBlock;
        public bool Advanced { get { return BlockAttrs.Any(a => a.Name.Equals("CmdletBinding", StringComparison.OrdinalIgnoreCase)) || (Params != null && Params.Any(p => p.Attrs.Any(a => a.Name.Equals("Parameter", StringComparison.OrdinalIgnoreCase)))); } }
    }
    public class AttrAst : PsAst { public string Name = ""; public List<PsAst> Args = new List<PsAst>(); public List<KeyValuePair<string, PsAst>> Named = new List<KeyValuePair<string, PsAst>>(); public bool IsType; }
    public class ParamAst : PsAst { public string Name = ""; public string Type; public PsAst Default; public List<AttrAst> Attrs = new List<AttrAst>(); }

    public class PsParser
    {
        readonly string s;
        int p;
        readonly PsSource src;
        int dscDepth;

        PsParser(string text, string file) { s = text ?? ""; src = new PsSource { Text = s, File = file ?? "" }; }

        public static ScriptBlockAst Parse(string text, string file)
        {
            var ps = new PsParser(text, file);
            var sb = ps.ScriptBody(true, '\0', 0);
            ps.SkipAll();
            if (ps.p < ps.s.Length) throw ps.Err("Unexpected token '" + ps.TokenText() + "' in expression or statement.", "UnexpectedToken");
            sb.Text = text; sb.Src = ps.src; sb.Pos = 0; sb.End = text.Length;
            return sb;
        }

        // ------------------------------------------------------------ characters
        char C { get { return p < s.Length ? s[p] : '\0'; } }
        char At(int i) { return p + i < s.Length && p + i >= 0 ? s[p + i] : '\0'; }
        bool Eof { get { return p >= s.Length; } }
        static bool IsDash(char c) { return c == '-' || c == '\u2013' || c == '\u2014' || c == '\u2015'; }
        static bool IsNl(char c) { return c == '\n' || c == '\r'; }
        static bool IsSpace(char c) { return c == ' ' || c == '\t' || c == '\f' || c == '\v' || c == '\u00a0'; }
        static bool IsIdent(char c) { return char.IsLetterOrDigit(c) || c == '_'; }
        static bool IsVarChar(char c) { return char.IsLetterOrDigit(c) || c == '_' || c == '?'; }

        // an error at the very end of the text means the statement is not finished yet (the console asks for more with >>)
        PsParseError Err(string msg, string id) { bool atEnd = true; for (int i = p; i < s.Length; i++) if (!char.IsWhiteSpace(s[i])) { atEnd = false; break; } return new PsParseError(msg, p, id, atEnd && p > 0) { Text = s, File = src.File }; }
        PsParseError Inc(string msg, string id) { return new PsParseError(msg, p, id, true) { Text = s, File = src.File }; }
        T Mark<T>(T a, int start) where T : PsAst { a.Pos = start; a.End = p; a.Src = src; return a; }

        /// <summary>Skips spaces, comments and line continuations (backtick + newline); newlines too if asked.</summary>
        void Skip(bool newlines)
        {
            while (p < s.Length)
            {
                char c = s[p];
                if (IsSpace(c)) { p++; continue; }
                if (c == '`' && p + 1 < s.Length && IsNl(s[p + 1])) { p++; if (s[p] == '\r' && At(1) == '\n') p++; p++; continue; }
                if (c == '`' && p + 1 >= s.Length) throw Inc("Missing expression after '`'.", "MissingExpression");
                if (newlines && IsNl(c)) { p++; continue; }
                if (c == '#') { while (p < s.Length && !IsNl(s[p])) p++; continue; }
                if (c == '<' && At(1) == '#')
                {
                    int e = s.IndexOf("#>", p + 2, StringComparison.Ordinal);
                    if (e < 0) { p = s.Length; throw Inc("The comment is not closed: <# needs a closing #>.", "MissingEndOfComment"); }
                    p = e + 2; continue;
                }
                break;
            }
        }
        void SkipAll() { Skip(true); }
        void SkipSemis() { while (true) { Skip(true); if (C == ';') { p++; continue; } break; } }

        string TokenText()
        {
            int st = p, e = p;
            if (e < s.Length && (s[e] == '}' || s[e] == ')' || s[e] == ']' || s[e] == '{' || s[e] == '(' || s[e] == ',' || s[e] == '|')) return s[e].ToString();
            while (e < s.Length && !char.IsWhiteSpace(s[e]) && "(){};,|".IndexOf(s[e]) < 0) e++;
            return s.Substring(st, Math.Max(1, e - st)).TrimEnd();
        }

        bool Word(string w)
        {
            // a keyword at p (case-insensitive), followed by a delimiter
            if (p + w.Length > s.Length) return false;
            if (string.Compare(s, p, w, 0, w.Length, StringComparison.OrdinalIgnoreCase) != 0) return false;
            char n = p + w.Length < s.Length ? s[p + w.Length] : '\0';
            return n == '\0' || char.IsWhiteSpace(n) || "(){};,|".IndexOf(n) >= 0 || (n == '#' ) || n == '$' || n == '\'' || n == '"';
        }
        string PeekWord()
        {
            int e = p;
            while (e < s.Length && (IsIdent(s[e]) || s[e] == '-')) e++;
            return s.Substring(p, e - p);
        }

        // ------------------------------------------------------------ script blocks and statements
        /// <summary>The inside of a script block (or a whole script): param, begin / process / end, or statements.</summary>
        ScriptBlockAst ScriptBody(bool top, char close, int openPos)
        {
            var sb = new ScriptBlockAst();
            int start = p;
            SkipSemis();
            // comment-based help at the top of the block
            sb.Help = HelpComment(start, p);
            var attrs = new List<AttrAst>();
            int save = p;
            while (C == '[')
            {
                int a0 = p;
                AttrAst a = TryAttr();
                if (a == null) { p = a0; break; }
                attrs.Add(a);
                SkipAll();
            }
            if (attrs.Count > 0 && attrs.Any(x => !x.IsType) && Eof) throw Inc("Missing param block after the attribute.", "MissingParamBlock");
            if (Word("param"))
            {
                p += 5; Skip(true);
                if (C != '(') throw Err("Missing '(' after 'param' in the script block.", "MissingParenthesisInFunctionDefinition");
                p++;
                sb.Params = ParamList(')');
                sb.HasParamBlock = true;
                sb.BlockAttrs = attrs;
                SkipSemis();
            }
            else p = save;
            // named blocks
            int named = p;
            string w = PeekWord().ToLowerInvariant();
            if ((w == "begin" || w == "process" || w == "end" || w == "dynamicparam") && LooksLikeNamedBlock(w.Length))
            {
                while (true)
                {
                    SkipSemis();
                    if (Eof || C == close) break;
                    w = PeekWord().ToLowerInvariant();
                    if (!(w == "begin" || w == "process" || w == "end" || w == "dynamicparam") || !LooksLikeNamedBlock(w.Length)) throw Err("Unexpected token '" + TokenText() + "' in expression or statement.", "UnexpectedToken");
                    p += w.Length; Skip(true);
                    var blk = Block();
                    if (w == "begin") sb.Begin = blk; else if (w == "process") sb.Process = blk; else if (w == "end") sb.EndBlock = blk;
                }
            }
            else sb.Body = StatementList(close);
            if (close != '\0')
            {
                SkipAll();
                if (Eof) { p = openPos; throw Inc("Missing closing '}' in statement block or type definition.", "MissingEndCurlyBrace"); }
                if (C != close) throw Err("Unexpected token '" + TokenText() + "' in expression or statement.", "UnexpectedToken");
            }
            return sb;
        }
        bool LooksLikeNamedBlock(int len)
        {
            int q = p + len;
            while (q < s.Length && char.IsWhiteSpace(s[q])) q++;
            return q < s.Length && s[q] == '{';
        }

        string HelpComment(int from, int to)
        {
            // the first <# ... #> with .SYNOPSIS / .DESCRIPTION between from and to (or a run of # lines)
            if (to <= from) return null;
            string chunk = s.Substring(from, to - from);
            int a = chunk.IndexOf("<#", StringComparison.Ordinal);
            if (a >= 0)
            {
                int b = chunk.IndexOf("#>", a, StringComparison.Ordinal);
                if (b > a)
                {
                    string body = chunk.Substring(a + 2, b - a - 2);
                    if (body.IndexOf(".SYNOPSIS", StringComparison.OrdinalIgnoreCase) >= 0 || body.IndexOf(".DESCRIPTION", StringComparison.OrdinalIgnoreCase) >= 0) return body;
                }
            }
            var lines = chunk.Replace("\r", "").Split('\n').Where(l => l.TrimStart().StartsWith("#", StringComparison.Ordinal)).Select(l => l.TrimStart().Substring(1)).ToList();
            string joined = string.Join("\n", lines.ToArray());
            if (joined.IndexOf(".SYNOPSIS", StringComparison.OrdinalIgnoreCase) >= 0) return joined;
            return null;
        }

        StmtBlock Block()
        {
            Skip(true);
            int st = p;
            if (C != '{') throw Err("Missing statement block.", "MissingStatementBlock");
            p++;
            var b = StatementList('}');
            SkipAll();
            if (Eof) { p = st; throw Inc("Missing closing '}' in statement block or type definition.", "MissingEndCurlyBrace"); }
            if (C != '}') throw Err("Unexpected token '" + TokenText() + "' in expression or statement.", "UnexpectedToken");
            p++;
            b.Pos = st; b.End = p; b.Src = src;
            return b;
        }

        StmtBlock StatementList(char close)
        {
            var b = new StmtBlock { Pos = p, Src = src };
            while (true)
            {
                SkipSemis();
                if (Eof || (close != '\0' && C == close)) break;
                if (close == '\0' && (C == '}' || C == ')')) break;
                var st = Statement();
                if (st != null) b.Stmts.Add(st);
                Skip(false);
                if (Eof || C == ';' || IsNl(C) || C == close) continue;
                if (close == '\0' && (C == '}' || C == ')')) break;
                throw Err("Unexpected token '" + TokenText() + "' in expression or statement.", "UnexpectedToken");
            }
            b.End = p;
            return b;
        }

        PsAst Statement()
        {
            int st = p;
            string label = null;
            if (C == ':' && IsIdent(At(1)))
            {
                int q = p + 1; while (q < s.Length && IsIdent(s[q])) q++;
                label = s.Substring(p + 1, q - p - 1); p = q; Skip(true);
            }
            string w = PeekWord().ToLowerInvariant();
            switch (w)
            {
                case "if": if (Word("if")) return If(st); break;
                case "switch": if (Word("switch")) return Switch(st, label); break;
                case "foreach": if (Word("foreach")) return ForEach(st, label); break;
                case "for": if (Word("for")) return For(st, label); break;
                case "while": if (Word("while")) return While(st, label); break;
                case "do": if (Word("do")) return Do(st, label); break;
                case "function": case "filter": case "workflow": if (Word(w)) return Function(st, w); break;
                case "try": if (Word("try")) return Try(st); break;
                case "trap": if (Word("trap")) return Trap(st); break;
                case "throw": case "return": case "exit":
                    if (Word(w))
                    {
                        p += w.Length; Skip(false);
                        PsAst v = null;
                        if (!Eof && !IsNl(C) && C != ';' && C != '}' && C != ')') v = PipelineOrAssign();
                        return Mark(new FlowAst { Kind = w, Value = v }, st);
                    }
                    break;
                case "break": case "continue":
                    if (Word(w))
                    {
                        p += w.Length; Skip(false);
                        string lb = null;
                        if (!Eof && !IsNl(C) && C != ';' && C != '}' && C != ')') { int q = p; while (q < s.Length && !char.IsWhiteSpace(s[q]) && ";})".IndexOf(s[q]) < 0) q++; lb = s.Substring(p, q - p).TrimStart(':'); p = q; }
                        return Mark(new FlowAst { Kind = w, Label = lb }, st);
                    }
                    break;
                case "configuration":
                    if (Word("configuration")) { int save = p; var c = Configuration(st); if (c != null) return c; p = save; }
                    break;
                case "data":
                    if (Word("data")) { int save = p; p += 4; Skip(true); if (C == '{') { var b = Block(); return Mark(new DataAst { Body = b }, st); } p = save; }
                    break;
                case "class": case "enum": case "using":
                    if (Word(w)) throw Err("'" + w + "' (" + (w == "using" ? "using statements" : "PowerShell classes and enums") + ") is real PowerShell 5.1, but Cert Lab's practice console does not simulate it. Use functions and [pscustomobject] instead.", "NotSimulated");
                    break;
                case "param":
                    if (Word("param")) throw Err("The param block must be the first thing in a script, function or script block.", "UnexpectedToken");
                    break;
            }
            if (dscDepth > 0)
            {
                var r = TryDsc(st);
                if (r != null) return r;
            }
            if (label != null) throw Err("A label can only be used before a loop (foreach, for, while, do, switch).", "UnexpectedToken");
            return PipelineOrAssign();
        }

        PsAst Condition()
        {
            Skip(true);
            if (C != '(') throw Err("Missing '(' after the keyword.", "MissingOpenParenthesisAfterKeyword");
            int open = p;
            p++; Skip(true);
            if (C == ')') throw Err("Missing condition in the statement after '('.", "MissingCondition");
            var c = PipelineOrAssign();
            Skip(true);
            if (Eof) { p = open; throw Inc("Missing closing ')' after expression.", "MissingEndParenthesisAfterStatement"); }
            if (C != ')') throw Err("Missing closing ')' after expression.", "MissingEndParenthesisAfterStatement");
            p++;
            return c;
        }

        PsAst If(int st)
        {
            var a = new IfAst();
            p += 2;
            a.Conds.Add(Condition()); a.Blocks.Add(Block());
            while (true)
            {
                int save = p;
                Skip(true);
                if (Word("elseif")) { p += 6; a.Conds.Add(Condition()); a.Blocks.Add(Block()); continue; }
                if (Word("else"))
                {
                    p += 4; Skip(true);
                    if (Word("if")) throw Err("'else if' is two words in PowerShell: write 'elseif'.", "MissingStatementBlockAfterElse");
                    a.Else = Block(); break;
                }
                p = save; break;
            }
            return Mark(a, st);
        }

        PsAst Switch(int st, string label)
        {
            var a = new SwitchAst { Label = label };
            p += 6; Skip(true);
            while (IsDash(C))
            {
                int q = p + 1; while (q < s.Length && IsIdent(s[q])) q++;
                string opt = s.Substring(p + 1, q - p - 1).ToLowerInvariant();
                p = q; Skip(true);
                if ("regex".StartsWith(opt, StringComparison.Ordinal) && opt.Length > 0) a.Regex = true;
                else if ("wildcard".StartsWith(opt, StringComparison.Ordinal) && opt.Length > 0) a.Wildcard = true;
                else if ("exact".StartsWith(opt, StringComparison.Ordinal) && opt.Length > 0) a.Exact = true;
                else if ("casesensitive".StartsWith(opt, StringComparison.Ordinal) && opt.Length > 0) a.CaseSensitive = true;
                else if ("file".StartsWith(opt, StringComparison.Ordinal) && opt.Length > 0) { a.File = true; a.Value = CommandArg(); Skip(true); }
                else if ("parallel".StartsWith(opt, StringComparison.Ordinal)) throw Err("switch -Parallel is only for workflows.", "InvalidSwitchFlag");
                else throw Err("Invalid switch statement flag '-" + opt + "'.", "InvalidSwitchFlag");
            }
            if (!a.File) a.Value = Condition();
            Skip(true);
            if (C != '{') throw Err("Missing '{' in switch statement.", "MissingCurlyBraceInSwitchStatement");
            int open = p; p++;
            while (true)
            {
                SkipSemis();
                if (Eof) { p = open; throw Inc("Missing closing '}' in statement block or type definition.", "MissingEndCurlyBrace"); }
                if (C == '}') { p++; break; }
                var cl = new SwitchClause();
                if (Word("default")) { p += 7; cl.IsDefault = true; }
                else cl.Cond = SwitchCond();
                cl.Body = Block();
                a.Clauses.Add(cl);
            }
            return Mark(a, st);
        }
        PsAst SwitchCond()
        {
            Skip(true);
            int st = p;
            if (C == '{') { return ScriptBlockLit(); }
            if (C == '$' || C == '(' || C == '\'' || C == '"' || C == '@' || C == '[') return Unary();
            // a bareword, or a number
            int q = p; while (q < s.Length && !char.IsWhiteSpace(s[q]) && s[q] != '{') q++;
            string w = s.Substring(p, q - p); p = q;
            object num;
            if (PsConv.TryNumber(w, out num) && w.Length > 0 && (char.IsDigit(w[0]) || IsDash(w[0]) && w.Length > 1)) return Mark(new ConstAst { Value = num }, st);
            return Mark(new ConstAst { Value = w }, st);
        }

        PsAst ForEach(int st, string label)
        {
            var a = new ForEachAst { Label = label };
            p += 7; Skip(true);
            if (IsDash(C)) { int q = p; while (q < s.Length && !char.IsWhiteSpace(s[q])) q++; p = q; Skip(true); }   // -parallel
            if (C != '(') throw Err("Missing opening '(' after keyword 'foreach'.", "MissingOpenParenthesisAfterKeyword");
            int open = p;
            p++; Skip(true);
            if (C != '$') throw Err("Missing variable name after foreach.", "MissingVariableNameAfterForeach");
            var v = (VarAst)Variable();
            a.Var = v.Name;
            Skip(true);
            if (!Word("in")) throw Err("Missing 'in' after variable in foreach loop.", "MissingInInForeach");
            p += 2; Skip(true);
            a.Coll = PipelineOrAssign();
            Skip(true);
            if (Eof) { p = open; throw Inc("Missing closing ')' after expression.", "MissingEndParenthesisAfterForeach"); }
            if (C != ')') throw Err("Unexpected token '" + TokenText() + "' in expression or statement.", "UnexpectedToken");
            p++;
            a.Body = Block();
            return Mark(a, st);
        }

        PsAst For(int st, string label)
        {
            var a = new ForAst { Label = label };
            p += 3; Skip(true);
            if (C != '(') throw Err("Missing opening '(' after keyword 'for'.", "MissingOpenParenthesisAfterKeyword");
            int open = p; p++;
            Skip(true);
            if (C != ';') a.Init = PipelineOrAssign();
            Skip(true); if (C == ';' || IsNl(C)) p++;
            Skip(true);
            if (C != ';') a.Cond = PipelineOrAssign();
            Skip(true); if (C == ';' || IsNl(C)) p++;
            Skip(true);
            if (C != ')') a.Iter = PipelineOrAssign();
            Skip(true);
            if (Eof) { p = open; throw Inc("Missing closing ')' after expression.", "MissingEndParenthesisAfterStatement"); }
            if (C != ')') throw Err("Missing closing ')' after expression.", "MissingEndParenthesisAfterStatement");
            p++;
            a.Body = Block();
            return Mark(a, st);
        }

        PsAst While(int st, string label)
        {
            p += 5;
            var a = new WhileAst { Label = label, Cond = Condition() };
            a.Body = Block();
            return Mark(a, st);
        }

        PsAst Do(int st, string label)
        {
            p += 2;
            var a = new WhileAst { Label = label, Do = true };
            a.Body = Block();
            Skip(true);
            if (Word("while")) { p += 5; }
            else if (Word("until")) { p += 5; a.Until = true; }
            else if (Eof) throw Inc("Missing 'while' or 'until' in do loop.", "MissingWhileOrUntilInDoWhile");
            else throw Err("Missing 'while' or 'until' in do loop.", "MissingWhileOrUntilInDoWhile");
            a.Cond = Condition();
            return Mark(a, st);
        }

        PsAst Function(int st, string kw)
        {
            if (kw == "workflow") throw Err("Workflows are real Windows PowerShell 5.1 (not PowerShell 7), but Cert Lab does not simulate them.", "NotSimulated");
            int helpFrom = PrecedingCommentStart(st);
            p += kw.Length; Skip(false);
            int q = p;
            while (q < s.Length && !char.IsWhiteSpace(s[q]) && s[q] != '{' && s[q] != '(') q++;
            string name = s.Substring(p, q - p);
            if (name.Length == 0) throw Err("Missing name after function keyword.", "MissingNameAfterKeyword");
            p = q; Skip(true);
            List<ParamAst> pars = null;
            if (C == '(') { p++; pars = ParamList(')'); Skip(true); }
            if (C != '{') { if (Eof) throw Inc("Missing function body in function declaration.", "MissingFunctionBody"); throw Err("Missing function body in function declaration.", "MissingFunctionBody"); }
            int open = p; p++;
            var body = ScriptBody(false, '}', open);
            p++;
            body.Pos = open; body.End = p; body.Src = src; body.Text = s.Substring(open + 1, p - open - 2);
            if (pars != null) { if (body.Params != null) throw Err("A function cannot have both a parameter list after its name and a param block.", "OnlyOneParameterListAllowed"); body.Params = pars; }
            if (body.Help == null && helpFrom >= 0) body.Help = HelpComment(helpFrom, st);
            return Mark(new FunctionAst { Name = name, Filter = kw == "filter", Body = body }, st);
        }
        int PrecedingCommentStart(int st)
        {
            // comment-based help just before "function": a <# #> block or # lines ending on the line above
            int i = st - 1;
            while (i >= 0 && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n')) i--;
            if (i >= 1 && s[i] == '>' && s[i - 1] == '#') { int a = s.LastIndexOf("<#", i, StringComparison.Ordinal); return a; }
            return -1;
        }

        List<ParamAst> ParamList(char close)
        {
            var l = new List<ParamAst>();
            int open = p - 1;
            while (true)
            {
                SkipAll();
                if (Eof) { p = open; throw Inc("Missing ')' in function parameter list.", "MissingEndParenthesisInFunctionParameterList"); }
                if (C == close) { p++; break; }
                var pa = new ParamAst { Pos = p, Src = src };
                while (C == '[')
                {
                    var a = TryAttr();
                    if (a == null) throw Err("Missing type name after '['.", "MissingTypename");
                    if (a.IsType) pa.Type = a.Name; else pa.Attrs.Add(a);
                    SkipAll();
                }
                if (C != '$') throw Err("Missing variable name in parameter list.", "InvalidFunctionParameter");
                var v = (VarAst)Variable();
                pa.Name = v.Name;
                SkipAll();
                if (C == '=') { p++; SkipAll(); pa.Default = MethodArg(); SkipAll(); }
                pa.End = p;
                l.Add(pa);
                if (C == ',') { p++; continue; }
                if (C == close) { p++; break; }
                if (Eof) { p = open; throw Inc("Missing ')' in function parameter list.", "MissingEndParenthesisInFunctionParameterList"); }
                throw Err("Missing ')' in function parameter list.", "MissingEndParenthesisInFunctionParameterList");
            }
            return l;
        }

        /// <summary>[Name(args)] or [type]: an attribute or a type constraint in a param block. Null if not one.</summary>
        AttrAst TryAttr()
        {
            int st = p;
            if (C != '[') return null;
            int depth = 0, q = p;
            // find the matching ] (attributes can contain strings and nested [ ])
            var name = new StringBuilder();
            p++;
            Skip(false);
            while (p < s.Length && (IsIdent(C) || C == '.' || C == '`')) { name.Append(C); p++; }
            if (name.Length == 0) { p = st; return null; }
            Skip(false);
            var a = new AttrAst { Name = name.ToString(), Pos = st, Src = src };
            if (C == '(')
            {
                p++;
                while (true)
                {
                    SkipAll();
                    if (C == ')') { p++; break; }
                    if (Eof) throw Inc("Missing closing ')' in the attribute.", "MissingEndParenthesisInExpression");
                    // Name = value, or a bare name (Mandatory), or a positional value
                    int save = p;
                    if ((char.IsLetter(C) || C == '_'))
                    {
                        int e = p; while (e < s.Length && IsIdent(s[e])) e++;
                        string nm = s.Substring(p, e - p);
                        p = e; SkipAll();
                        if (C == '=') { p++; SkipAll(); a.Named.Add(new KeyValuePair<string, PsAst>(nm, MethodArg())); }
                        else if (C == ',' || C == ')') a.Named.Add(new KeyValuePair<string, PsAst>(nm, new ConstAst { Value = true }));
                        else { p = save; a.Args.Add(MethodArg()); }
                    }
                    else a.Args.Add(MethodArg());
                    SkipAll();
                    if (C == ',') { p++; continue; }
                    if (C == ')') { p++; break; }
                    throw Err("Missing ')' in the attribute argument list.", "MissingEndParenthesisInExpression");
                }
                Skip(false);
                if (C != ']') throw Err("Missing ']' after the attribute.", "EndSquareBracketExpectedAtEndOfAttribute");
                p++;
                a.End = p;
                return a;
            }
            // a type, possibly generic or an array: [string[]], [List[string]]
            p = st;
            string ty = TypeName();
            if (ty == null) { p = st; return null; }
            a.Name = ty; a.IsType = true; a.End = p;
            return a;
        }

        PsAst Try(int st)
        {
            var a = new TryAst();
            p += 3;
            a.Body = Block();
            while (true)
            {
                int save = p;
                Skip(true);
                if (Word("catch"))
                {
                    p += 5; Skip(true);
                    var cc = new CatchClause();
                    while (C == '[')
                    {
                        string t = TypeName();
                        if (t == null) throw Err("Missing type name after '['.", "MissingTypename");
                        cc.Types.Add(t);
                        Skip(true);
                        if (C == ',') { p++; Skip(true); continue; }
                        break;
                    }
                    cc.Body = Block();
                    a.Catches.Add(cc);
                    continue;
                }
                if (Word("finally")) { p += 7; a.Finally = Block(); break; }
                p = save; break;
            }
            if (a.Catches.Count == 0 && a.Finally == null)
            {
                if (Eof) throw Inc("The Try statement is missing its Catch or Finally block.", "MissingCatchOrFinally");
                throw Err("The Try statement is missing its Catch or Finally block.", "MissingCatchOrFinally");
            }
            return Mark(a, st);
        }

        PsAst Trap(int st)
        {
            p += 4; Skip(true);
            var a = new TrapAst();
            if (C == '[') a.Type = TypeName();
            a.Body = Block();
            return Mark(a, st);
        }

        PsAst Configuration(int st)
        {
            p += 13; Skip(false);
            int q = p; while (q < s.Length && (IsIdent(s[q]) || s[q] == '-')) q++;
            if (q == p) return null;
            string name = s.Substring(p, q - p);
            p = q; Skip(true);
            if (C != '{') return null;
            int open = p; p++;
            dscDepth++;
            ScriptBlockAst body;
            try { body = ScriptBody(false, '}', open); }
            finally { dscDepth--; }
            p++;
            body.Pos = open; body.End = p; body.Src = src; body.Text = s.Substring(open + 1, p - open - 2);
            return Mark(new ConfigAst { Name = name, Body = body }, st);
        }

        PsAst TryDsc(int st)
        {
            // Node NAMES { ... }   or   ResourceType ResourceName { Prop = value ... }
            if (!IsIdent(C)) return null;
            string w = PeekWord();
            if (w.Length == 0) return null;
            int save = p;
            if (w.Equals("Node", StringComparison.OrdinalIgnoreCase) && Word("node"))
            {
                p += 4; Skip(false);
                var names = CommandArg();
                Skip(true);
                if (C != '{') { p = save; return null; }
                var b = Block();
                return Mark(new NodeAst { Names = names, Body = b }, st);
            }
            if (w.Equals("Import-DscResource", StringComparison.OrdinalIgnoreCase)) return null;
            p += w.Length;
            if (!IsSpace(C)) { p = save; return null; }
            Skip(false);
            if (Eof || IsNl(C) || C == '{' || IsDash(C)) { p = save; return null; }
            PsAst rname;
            try { rname = CommandArg(); } catch (PsParseError) { p = save; return null; }
            Skip(false);
            if (C != '{') { p = save; return null; }
            var h = HashBody(p);
            return Mark(new ResourceAst { Type = w, Name = rname, Props = h }, st);
        }

        // ------------------------------------------------------------ pipelines and commands
        static readonly string[] AssignOps = { "=", "+=", "-=", "*=", "/=", "%=" };

        PsAst PipelineOrAssign()
        {
            int saveNc = noComma; noComma = 0;
            try { return PipelineOrAssign2(); } finally { noComma = saveNc; }
        }
        PsAst PipelineOrAssign2()
        {
            int st = p;
            Skip(false);
            if (IsExpressionStart())
            {
                var e = Expression();
                Skip(false);
                // assignment?
                string op = null;
                foreach (var o in AssignOps) if (string.Compare(s, p, o, 0, o.Length, StringComparison.Ordinal) == 0 && !(o == "=" && At(1) == '=')) { op = o; break; }
                if (op == null && IsDash(C) && At(1) == '=') op = "-=";
                if (op != null)
                {
                    if (!IsLValue(e)) throw Err("The assignment expression is not valid. The input to an assignment operator must be an object that is able to accept assignments, such as a variable or a property.", "InvalidLeftHandSide");
                    p += op.Length;
                    Skip(true);
                    if (Eof) throw Inc("You must provide a value expression following the '" + op + "' operator.", "ExpectedValueExpression");
                    var v = StatementForAssign();
                    return Mark(new AssignAst { Target = e, Op = op, Value = v }, st);
                }
                var first = new CmdExprAst { Expr = e };
                Redirections(first.Redirs);
                Mark(first, st);
                return PipelineRest(first, st);
            }
            var cmd = Command();
            return PipelineRest(cmd, st);
        }

        PsAst StatementForAssign()
        {
            int st = p;
            string w = PeekWord().ToLowerInvariant();
            if ((w == "if" || w == "switch" || w == "foreach" || w == "for" || w == "while" || w == "do" || w == "try") && Word(w)) return Statement();
            return PipelineOrAssign();
        }

        static bool IsLValue(PsAst e)
        {
            if (e is VarAst) return true;
            if (e is MemberAst) return true;
            if (e is IndexAst) return true;
            var c = e as CastAst;
            if (c != null) return c.X is VarAst;
            var arr = e as ArrayLitAst;
            if (arr != null) return arr.Items.All(IsLValue);
            return false;
        }

        PsAst PipelineRest(PsAst first, int st)
        {
            Skip(false);
            if (C != '|' || At(1) == '|') return first;
            var pl = new PipelineAst();
            pl.Elements.Add(first);
            while (C == '|' && At(1) != '|')
            {
                p++; Skip(true);
                if (Eof) throw Inc("An empty pipe element is not allowed.", "EmptyPipeElement");
                if (C == '|' || C == ';' || C == ')' || C == '}') throw Err("An empty pipe element is not allowed.", "EmptyPipeElement");
                if (IsExpressionStart() && C != '&' && !(C == '.' && (At(1) == ' ' || At(1) == '\\' || At(1) == '/')))
                {
                    int es = p;
                    var e = Expression();
                    p = es;
                    throw Err("Expressions are only allowed as the first element of a pipeline.", "ExpressionsMustBeFirstInPipeline");
                }
                pl.Elements.Add(Command());
                Skip(false);
            }
            if (C == '|' && At(1) == '|') throw Err("The token '||' is not a valid statement separator in this version.", "InvalidEndOfLine");
            return Mark(pl, st);
        }

        bool IsExpressionStart()
        {
            char c = C;
            if (c == '$' || c == '\'' || c == '"' || c == '(' || c == '[' || c == '{' || c == ',' || c == '!' || c == '\u2018' || c == '\u2019' || c == '\u201C' || c == '\u201D') return true;
            if (c == '@') return At(1) == '(' || At(1) == '{' || At(1) == '"' || At(1) == '\'';
            if (char.IsDigit(c)) { int q = p; while (q < s.Length && !char.IsWhiteSpace(s[q]) && "|;)}".IndexOf(s[q]) < 0) q++; object v; string w = s.Substring(p, q - p); return PsConv.TryNumber(w, out v) || w.Contains("..") || IsArithWord(w); }
            if (c == '.' && char.IsDigit(At(1))) return true;
            if ((c == '+' || IsDash(c)) && (At(1) == '+' || IsDash(At(1))) && At(2) == '$') return true;   // ++$i
            if (IsDash(c))
            {
                if (char.IsDigit(At(1)) || At(1) == '.' && char.IsDigit(At(2))) return true;
                if (At(1) == '$' || At(1) == '(') return true;
                int q = p + 1; while (q < s.Length && char.IsLetter(s[q])) q++;
                string op = s.Substring(p + 1, q - p - 1).ToLowerInvariant();
                if ((op == "not" || op == "bnot" || op == "split" || op == "join") && (q >= s.Length || !IsIdent(s[q]))) return true;
            }
            return false;
        }
        static bool IsArithWord(string w) { return w.IndexOfAny(new[] { '+', '*', '/', '%', '-', ',' }) > 0; }

        PsAst Command()
        {
            int st = p;
            var c = new CommandAst();
            Skip(false);
            if (C == '&' || (C == '.' && (IsSpace(At(1)) || At(1) == '$' || At(1) == '(' || At(1) == '{' || At(1) == '\'' || At(1) == '"')))
            {
                c.Invoke = C.ToString();
                p++;
                Skip(false);
                if (C == '$' || C == '(' || C == '{' || C == '\'' || C == '"' || C == '@') c.NameExpr = Postfix(Primary(true));
                else { c.Name = Bareword(); if (c.Name.Length == 0) throw Err("Missing expression after '" + c.Invoke + "'.", "MissingExpression"); }
            }
            else
            {
                c.Name = Bareword();
                if (c.Name.Length == 0)
                {
                    if (C == '&' && At(1) == '&') throw Err("The token '&&' is not a valid statement separator in this version.", "InvalidEndOfLine");
                    throw Err("Unexpected token '" + TokenText() + "' in expression or statement.", "UnexpectedToken");
                }
            }
            // arguments
            while (true)
            {
                Skip(false);
                if (Eof || IsNl(C) || C == ';' || C == '|' || C == ')' || C == '}') break;
                if (C == '&' && At(1) == '&') throw Err("The token '&&' is not a valid statement separator in this version.", "InvalidEndOfLine");
                if (C == '&') throw Err("The ampersand (&) character is not allowed. The & operator is reserved for future use; wrap an ampersand in double quotation marks (\"&\") to pass it as part of a string.", "AmpersandNotAllowed");
                if (Redirection(c.Redirs)) continue;
                int ep = p;
                if (C == '-' && At(1) == '-' && At(2) == '%')
                {
                    p += 3;
                    int e = p; while (e < s.Length && !IsNl(s[e])) e++;
                    string rest = s.Substring(p, e - p).Trim();
                    p = e;
                    foreach (var w in rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)) c.Elems.Add(new CmdElem { Arg = new ConstAst { Value = w }, Pos = ep });
                    break;
                }
                if (IsDash(C) && IsParamToken())
                {
                    int q = p + 1;
                    while (q < s.Length && (IsIdent(s[q]) || s[q] == '-' && q > p + 1 && IsIdent(At(q - p + 1 - 1 + 1)) && false)) q++;
                    while (q < s.Length && (IsIdent(s[q]) || s[q] == '?')) q++;
                    string name = s.Substring(p + 1, q - p - 1);
                    var el = new CmdElem { Param = name, Pos = ep };
                    p = q;
                    if (C == ':')
                    {
                        p++;
                        Skip(false);
                        el.ColonArg = true;
                        el.Arg = CommandArg();
                    }
                    el.Text = s.Substring(ep, p - ep);
                    c.Elems.Add(el);
                    continue;
                }
                if (C == '-' && At(1) == '-' && (At(2) == '\0' || char.IsWhiteSpace(At(2)))) { p += 2; c.Elems.Add(new CmdElem { Param = "--", Pos = ep, Text = "--" }); continue; }
                if (C == '@' && (IsIdent(At(1)) || At(1) == '_'))
                {
                    p++;
                    int q = p; while (q < s.Length && IsIdent(s[q])) q++;
                    var v = new VarAst { Name = s.Substring(p, q - p), Splat = true };
                    p = q;
                    c.Elems.Add(new CmdElem { Arg = Mark(v, ep), Splat = true, Pos = ep, Text = s.Substring(ep, p - ep) });
                    continue;
                }
                var arg = CommandArg();
                c.Elems.Add(new CmdElem { Arg = arg, Pos = ep, Text = s.Substring(ep, p - ep) });
            }
            return Mark(c, st);
        }

        bool IsParamToken()
        {
            // -Name, -Name:value; not -5, not -, not --
            char n = At(1);
            if (!(char.IsLetter(n) || n == '_' || n == '?')) return false;
            int q = p + 1;
            while (q < s.Length && (IsIdent(s[q]) || s[q] == '?')) q++;
            char e = q < s.Length ? s[q] : '\0';
            return e == '\0' || char.IsWhiteSpace(e) || e == ':' || e == ';' || e == '|' || e == ')' || e == '}' || e == ',' && false || e == '(';
        }

        bool Redirection(List<Redir> list)
        {
            // >, >>, 2>, 2>>, 2>&1, *>, *>&1, 3>..6>, <
            int q = p;
            string stream = "1";
            if (C == '*' && At(1) == '>') { stream = "*"; q++; }
            else if (char.IsDigit(C) && At(1) == '>') { stream = C.ToString(); q++; }
            else if (C != '>')
            {
                if (C == '<' && At(1) != '#') throw Err("The '<' operator is reserved for future use.", "RedirectionNotSupported");
                return false;
            }
            if (q >= s.Length || s[q] != '>') return false;
            p = q + 1;
            var r = new Redir { Stream = stream, Op = ">" };
            if (C == '>') { r.Op = ">>"; p++; }
            if (C == '&' && char.IsDigit(At(1))) { r.MergeTo = At(1).ToString(); p += 2; list.Add(r); return true; }
            Skip(false);
            if (Eof || IsNl(C) || C == ';' || C == '|') throw Err("Missing file specification after redirection operator.", "MissingFileSpecification");
            r.Target = CommandArg();
            list.Add(r);
            return true;
        }
        void Redirections(List<Redir> list) { while (true) { Skip(false); if (!Redirection(list)) break; } }

        /// <summary>A bareword in command mode (a command name or a string argument).</summary>
        string Bareword()
        {
            int st = p;
            var sb = new StringBuilder();
            while (p < s.Length)
            {
                char c = s[p];
                if (char.IsWhiteSpace(c) || "|;(){},&".IndexOf(c) >= 0) break;
                if (c == '`' && p + 1 < s.Length && !IsNl(s[p + 1])) { sb.Append(Escape(s[p + 1])); p += 2; continue; }
                if ((c == '\'' || c == '"') && sb.Length > 0) break;
                if (c == '>' ) break;
                if (c == '$' && sb.Length > 0) break;
                sb.Append(c); p++;
            }
            return sb.ToString();
        }

        /// <summary>One argument in command mode: a value, with commas making an array.</summary>
        PsAst CommandArg()
        {
            int st = p;
            var first = CommandValue();
            Skip(false);
            if (C != ',') return first;
            var arr = new ArrayLitAst();
            arr.Items.Add(first);
            while (C == ',')
            {
                p++; Skip(true);
                if (Eof) throw Inc("Missing argument in parameter list.", "MissingArgument");
                arr.Items.Add(CommandValue());
                Skip(false);
            }
            return Mark(arr, st);
        }

        PsAst CommandValue()
        {
            int st = p;
            char c = C;
            if (c == '$' || c == '(' || c == '\'' || c == '"' || c == '{' || (c == '@' && (At(1) == '(' || At(1) == '{' || At(1) == '"' || At(1) == '\'')) || c == '\u2018' || c == '\u2019' || c == '\u201C' || c == '\u201D')
            {
                var prim = Postfix(Primary(true));
                // "$a"b style concatenation is rare; a value directly followed by more bareword text becomes a string
                if (!Eof && !char.IsWhiteSpace(C) && "|;(){},&>".IndexOf(C) < 0 && (prim is VarAst || prim is StrAst))
                {
                    var str = new StrAst();
                    if (prim is StrAst) str.Parts.AddRange(((StrAst)prim).Parts); else str.Parts.Add(prim);
                    var rest = BarewordExpandable();
                    str.Parts.AddRange(rest.Parts);
                    return Mark(str, st);
                }
                return prim;
            }
            if (c == '[')
            {
                // a type or a cast in command mode: [int]5, [string]::Join(...)
                int save = p;
                string ty = TypeName();
                if (ty != null && (C == ':' && At(1) == ':' || C == '$' || C == '(' || C == '[' || char.IsDigit(C) || C == '"' || C == '\''))
                {
                    p = save;
                    return Postfix(Unary());
                }
                p = save;
            }
            if (c == '@' && IsIdent(At(1))) { p++; var nm = Bareword(); return Mark(new VarAst { Name = nm, Splat = true }, st); }
            var w = BarewordExpandable();
            // a number?
            if (w.Parts.Count == 1 && w.Parts[0] is string)
            {
                string t = (string)w.Parts[0];
                object num;
                if (t.Length > 0 && (char.IsDigit(t[0]) || (IsDash(t[0]) || t[0] == '.' || t[0] == '+') && t.Length > 1 && (char.IsDigit(t[1]) || t[1] == '.')) && PsConv.TryNumber(t.Replace('\u2013', '-'), out num) && !t.EndsWith(".", StringComparison.Ordinal)) return Mark(new ConstAst { Value = num }, st);
                return Mark(new ConstAst { Value = t }, st);
            }
            return Mark(w, st);
        }

        StrAst BarewordExpandable()
        {
            var a = new StrAst();
            var sb = new StringBuilder();
            while (p < s.Length)
            {
                char c = s[p];
                if (char.IsWhiteSpace(c) || "|;(){},&".IndexOf(c) >= 0) break;
                if (c == '>') break;
                if (c == '`' && p + 1 < s.Length && !IsNl(s[p + 1])) { sb.Append(Escape(s[p + 1])); p += 2; continue; }
                if (c == '$' && (IsVarChar(At(1)) || At(1) == '{' || At(1) == '(' || At(1) == '$' || At(1) == '^'))
                {
                    if (sb.Length > 0) { a.Parts.Add(sb.ToString()); sb.Clear(); }
                    if (At(1) == '(') { int st = p; p += 2; var b = StatementList(')'); SkipAll(); if (C != ')') throw Inc("Missing closing ')' in subexpression.", "MissingEndParenthesisInSubexpression"); p++; a.Parts.Add(Mark(new SubExprAst { Body = b }, st)); }
                    else a.Parts.Add(Variable());
                    continue;
                }
                if (c == '"' || c == '\'')
                {
                    // quoted part inside a bareword: C:\'my files'
                    var q = Primary(true) as StrAst;
                    if (q != null) { if (sb.Length > 0) { a.Parts.Add(sb.ToString()); sb.Clear(); } a.Parts.AddRange(q.Parts); continue; }
                    var cq = q as object;
                    continue;
                }
                sb.Append(c); p++;
            }
            if (sb.Length > 0 || a.Parts.Count == 0) a.Parts.Add(sb.ToString());
            // merge literal pieces
            var merged = new List<object>();
            foreach (var part in a.Parts)
            {
                if (part is string && merged.Count > 0 && merged[merged.Count - 1] is string) merged[merged.Count - 1] = (string)merged[merged.Count - 1] + (string)part;
                else merged.Add(part);
            }
            a.Parts = merged;
            return a;
        }

        // ------------------------------------------------------------ expressions
        public PsAst Expression() { return Logical(); }

        static readonly string[] CmpOps = { "eq", "ne", "gt", "ge", "lt", "le", "like", "notlike", "match", "notmatch", "contains", "notcontains", "in", "notin", "replace", "split", "join", "is", "isnot", "as" };
        static readonly HashSet<string> CmpSet = BuildCmp();
        static HashSet<string> BuildCmp()
        {
            var h = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var o in CmpOps) { h.Add(o); if (o != "is" && o != "isnot" && o != "as" && o != "join") { h.Add("i" + o); h.Add("c" + o); } }
            return h;
        }

        /// <summary>The -word operator at p (without consuming), or null.</summary>
        string DashOp()
        {
            if (!IsDash(C)) return null;
            int q = p + 1;
            while (q < s.Length && char.IsLetter(s[q])) q++;
            if (q == p + 1) return null;
            if (q < s.Length && (IsIdent(s[q]))) return null;
            return s.Substring(p + 1, q - p - 1).ToLowerInvariant();
        }

        void ExpectOperand(string op)
        {
            Skip(true);
            if (Eof) throw Inc("You must provide a value expression following the '" + op + "' operator.", "ExpectedValueExpression");
            if (C == ')' || C == '}' || C == ';' || C == '|') throw Err("You must provide a value expression following the '" + op + "' operator.", "ExpectedValueExpression");
        }

        PsAst Logical()
        {
            int st = p;
            var l = Comparison();
            while (true)
            {
                Skip(false);
                string op = DashOp();
                if (op == "and" || op == "or" || op == "xor")
                {
                    p += op.Length + 1;
                    ExpectOperand("-" + op);
                    var r = Comparison();
                    l = Mark(new BinAst { Op = op, L = l, R = r }, st);
                    continue;
                }
                return l;
            }
        }

        PsAst Comparison()
        {
            int st = p;
            var l = Bitwise();
            while (true)
            {
                Skip(false);
                string op = DashOp();
                if (op != null && CmpSet.Contains(op))
                {
                    p += op.Length + 1;
                    ExpectOperand("-" + op);
                    PsAst r;
                    if (op == "is" || op == "isnot" || op == "as")
                    {
                        Skip(true);
                        if (C == '[') { int ts = p; var t = TypeName(); if (t != null && !(C == ':' && At(1) == ':')) r = Mark(new TypeAst { Type = t }, ts); else { p = ts; r = Bitwise(); } }
                        else r = Bitwise();
                    }
                    else r = Bitwise();
                    l = Mark(new BinAst { Op = op, L = l, R = r }, st);
                    continue;
                }
                if (op != null && op != "and" && op != "or" && op != "xor" && op != "band" && op != "bor" && op != "bxor" && op != "shl" && op != "shr" && op != "f" && op != "not" && op != "bnot")
                    throw Err("Unexpected token '-" + op + "' in expression or statement.", "UnexpectedToken");
                return l;
            }
        }

        PsAst Bitwise()
        {
            int st = p;
            var l = Additive();
            while (true)
            {
                Skip(false);
                string op = DashOp();
                if (op == "band" || op == "bor" || op == "bxor" || op == "shl" || op == "shr")
                {
                    p += op.Length + 1; ExpectOperand("-" + op);
                    l = Mark(new BinAst { Op = op, L = l, R = Additive() }, st);
                    continue;
                }
                return l;
            }
        }

        PsAst Additive()
        {
            int st = p;
            var l = Multiplicative();
            while (true)
            {
                Skip(false);
                if (C == '+' && At(1) != '=' && At(1) != '+') { p++; ExpectOperand("+"); l = Mark(new BinAst { Op = "+", L = l, R = Multiplicative() }, st); continue; }
                if (IsDash(C) && At(1) != '=' && !IsDash(At(1)) && DashOp() == null) { p++; ExpectOperand("-"); l = Mark(new BinAst { Op = "-", L = l, R = Multiplicative() }, st); continue; }
                return l;
            }
        }

        PsAst Multiplicative()
        {
            int st = p;
            var l = Format();
            while (true)
            {
                Skip(false);
                if ((C == '*' || C == '/' || C == '%') && At(1) != '=' && !(C == '*' && At(1) == '>'))
                {
                    string op = C.ToString(); p++; ExpectOperand(op);
                    l = Mark(new BinAst { Op = op, L = l, R = Format() }, st);
                    continue;
                }
                return l;
            }
        }

        PsAst Format()
        {
            int st = p;
            var l = Range();
            while (true)
            {
                Skip(false);
                if (DashOp() == "f") { p += 2; ExpectOperand("-f"); l = Mark(new BinAst { Op = "f", L = l, R = Range() }, st); continue; }
                return l;
            }
        }

        PsAst Range()
        {
            int st = p;
            var l = ArrayLit();
            Skip(false);
            if (C == '.' && At(1) == '.' && At(2) != '.')
            {
                p += 2; ExpectOperand("..");
                var r = ArrayLit();
                return Mark(new BinAst { Op = "..", L = l, R = r }, st);
            }
            return l;
        }

        PsAst ArrayLit()
        {
            int st = p;
            var first = Unary();
            Skip(false);
            if (C != ',' || noComma > 0) return first;
            var a = new ArrayLitAst();
            a.Items.Add(first);
            while (C == ',')
            {
                p++; Skip(true);
                if (Eof) throw Inc("Missing expression after ','.", "MissingExpressionAfterToken");
                a.Items.Add(Unary());
                Skip(false);
            }
            return Mark(a, st);
        }

        PsAst Unary()
        {
            int st = p;
            Skip(false);
            if (C == '!') { p++; Skip(true); return Mark(new UnAst { Op = "not", X = Unary() }, st); }
            if (C == ',') { p++; Skip(true); var a = new ArrayLitAst(); a.Items.Add(Unary()); return Mark(a, st); }
            if ((C == '+' && At(1) == '+') || (IsDash(C) && IsDash(At(1)))) { string op = C == '+' ? "++" : "--"; p += 2; Skip(false); return Mark(new UnAst { Op = op, X = Unary() }, st); }
            string dop = DashOp();
            if (dop == "not" || dop == "bnot" || dop == "split" || dop == "join")
            {
                p += dop.Length + 1; ExpectOperand("-" + dop);
                return Mark(new UnAst { Op = dop, X = Unary() }, st);
            }
            if (IsDash(C) && dop == null) { p++; Skip(false); return Mark(new UnAst { Op = "-", X = Unary() }, st); }
            if (C == '+' && At(1) != '=') { p++; Skip(false); return Mark(new UnAst { Op = "+", X = Unary() }, st); }
            if (C == '[')
            {
                int save = p;
                string t = TypeName();
                if (t != null)
                {
                    if (C == ':' && At(1) == ':') return Postfix(Mark(new TypeAst { Type = t }, st), st);
                    if (C == '.' && IsIdent(At(1))) return Postfix(Mark(new TypeAst { Type = t }, st), st);
                    Skip(false);
                    if (StartsOperand())
                    {
                        var x = Unary();
                        return Mark(new CastAst { Type = t, X = x }, st);
                    }
                    return Postfix(Mark(new TypeAst { Type = t }, st), st);
                }
                p = save;
            }
            var prim = Primary(false);
            return Postfix(prim, st);
        }

        bool StartsOperand()
        {
            char c = C;
            if (c == '$' || c == '(' || c == '\'' || c == '"' || c == '@' || c == '[' || c == '{' || char.IsDigit(c) || c == '!' || c == '\u2018' || c == '\u201C') return true;
            if (IsDash(c) && (char.IsDigit(At(1)) || At(1) == '$' || DashOp() == "not")) return true;
            if (c == '+' && char.IsDigit(At(1))) return true;
            if (c == '.' && char.IsDigit(At(1))) return true;
            return false;
        }

        PsAst Postfix(PsAst x) { return Postfix(x, x.Pos); }
        PsAst Postfix(PsAst x, int st)
        {
            while (true)
            {
                if (C == '.' && At(1) != '.' && (IsIdent(At(1)) || At(1) == '$' || At(1) == '\'' || At(1) == '"' || At(1) == '('))
                {
                    p++;
                    x = MemberRest(x, false, st);
                    continue;
                }
                if (C == ':' && At(1) == ':')
                {
                    p += 2;
                    x = MemberRest(x, true, st);
                    continue;
                }
                if (C == '[' && !(x is TypeAst))
                {
                    int open = p;
                    p++; Skip(true);
                    if (C == ']') throw Err("Array index expression is missing or not valid.", "MissingArrayIndexExpression");
                    int saveNc = noComma; noComma = 0;
                    PsAst idx;
                    try { idx = Expression(); } finally { noComma = saveNc; }
                    Skip(true);
                    if (Eof) { p = open; throw Inc("Missing ']' after array index expression.", "MissingEndSquareBracket"); }
                    if (C != ']') throw Err("Missing ']' after array index expression.", "MissingEndSquareBracket");
                    p++;
                    x = Mark(new IndexAst { Target = x, Index = idx }, st);
                    continue;
                }
                if ((C == '+' && At(1) == '+') || (IsDash(C) && IsDash(At(1)) && !IsIdent(At(2))))
                {
                    if (x is VarAst || x is MemberAst || x is IndexAst) { string op = C == '+' ? "++" : "--"; p += 2; x = Mark(new UnAst { Op = op, X = x, Postfix = true }, st); continue; }
                }
                return x;
            }
        }

        PsAst MemberRest(PsAst target, bool isStatic, int st)
        {
            PsAst name;
            int ns = p;
            if (C == '$') name = Variable();
            else if (C == '\'' || C == '"') name = Primary(false);
            else if (C == '(') { p++; Skip(true); var e = PipelineOrAssign(); Skip(true); if (C != ')') throw Err("Missing closing ')' in expression.", "MissingEndParenthesisInExpression"); p++; name = e; }
            else
            {
                int q = p; while (q < s.Length && IsIdent(s[q])) q++;
                if (q == p) throw Err("Missing property name after reference operator.", "MissingPropertyName");
                name = Mark(new ConstAst { Value = s.Substring(p, q - p) }, ns);
                p = q;
            }
            if (C == '(')
            {
                int open = p;
                p++;
                var inv = new InvokeAst { Target = target, Name = name, Static = isStatic };
                Skip(true);
                if (C != ')')
                {
                    while (true)
                    {
                        Skip(true);
                        inv.Args.Add(MethodArg());
                        Skip(true);
                        if (C == ',') { p++; continue; }
                        if (C == ')') break;
                        if (Eof) { p = open; throw Inc("Missing ')' in method call.", "MissingEndParenthesisInMethodCall"); }
                        throw Err("Missing ')' in method call.", "MissingEndParenthesisInMethodCall");
                    }
                }
                p++;
                return Mark(inv, st);
            }
            return Mark(new MemberAst { Target = target, Name = name, Static = isStatic }, st);
        }
        PsAst MethodArg()
        {
            // a full expression without the comma operator
            int st = p;
            var l = Logical2();
            return l;
        }
        PsAst Logical2()
        {
            // like Logical, but commas end the argument (Range -> Unary instead of ArrayLit)
            noComma++;
            try { return Logical(); } finally { noComma--; }
        }
        int noComma;

        string TypeName()
        {
            // [Name], [Name[]], [Name[Arg,Arg]], [Name.Sub]; returns the text inside the outer brackets
            if (C != '[') return null;
            int st = p;
            p++;
            Skip(false);
            int depth = 1;
            var sb = new StringBuilder();
            if (!(char.IsLetter(C) || C == '_')) { p = st; return null; }
            while (p < s.Length && depth > 0)
            {
                char c = s[p];
                if (c == '[') { depth++; sb.Append(c); p++; continue; }
                if (c == ']') { depth--; if (depth == 0) { p++; break; } sb.Append(c); p++; continue; }
                if (IsIdent(c) || c == '.' || c == ',' || c == '`' || c == '+' || c == ' ') { sb.Append(c); p++; continue; }
                p = st; return null;
            }
            if (depth > 0) { p = st; return null; }
            return sb.ToString().Trim();
        }

        PsAst Primary(bool commandMode)
        {
            int st = p;
            char c = C;
            if (c == '$')
            {
                if (At(1) == '(') { p += 2; var b = StatementList(')'); SkipAll(); if (Eof) { p = st; throw Inc("Missing closing ')' in subexpression.", "MissingEndParenthesisInSubexpression"); } if (C != ')') throw Err("Missing closing ')' in subexpression.", "MissingEndParenthesisInSubexpression"); p++; return Mark(new SubExprAst { Body = b }, st); }
                return Variable();
            }
            if (c == '(')
            {
                p++;
                Skip(true);
                if (C == ')') throw Err("An expression was expected after '('.", "ExpectedExpression");
                var inner = PipelineOrAssign();
                Skip(true);
                if (Eof) { p = st; throw Inc("Missing closing ')' in expression.", "MissingEndParenthesisInExpression"); }
                if (C != ')') throw Err("Missing closing ')' in expression.", "MissingEndParenthesisInExpression");
                p++;
                return Mark(new ParenAst { Inner = inner }, st);
            }
            if (c == '@' && At(1) == '(')
            {
                p += 2;
                var b = StatementList(')');
                SkipAll();
                if (Eof) { p = st; throw Inc("Missing closing ')' in subexpression.", "MissingEndParenthesisInSubexpression"); }
                if (C != ')') throw Err("Missing closing ')' in subexpression.", "MissingEndParenthesisInSubexpression");
                p++;
                return Mark(new ArrayExprAst { Body = b }, st);
            }
            if (c == '@' && At(1) == '{') { p++; return HashBody(st); }
            if (c == '@' && (At(1) == '"' || At(1) == '\'')) return HereString();
            if (c == '\'' || c == '\u2018' || c == '\u2019') return SingleQuoted();
            if (c == '"' || c == '\u201C' || c == '\u201D') return DoubleQuoted();
            if (c == '{') return ScriptBlockLit();
            if (char.IsDigit(c) || c == '.' && char.IsDigit(At(1)))
            {
                int q = p;
                while (q < s.Length && (char.IsLetterOrDigit(s[q]) || s[q] == '.' && !(q + 1 < s.Length && s[q + 1] == '.'))) q++;
                // exponent sign: 1e-3
                if (q < s.Length && (s[q] == '-' || s[q] == '+') && q > p && (s[q - 1] == 'e' || s[q - 1] == 'E') && q + 1 < s.Length && char.IsDigit(s[q + 1])) { q++; while (q < s.Length && char.IsDigit(s[q])) q++; }
                string w = s.Substring(p, q - p);
                object v;
                string wl = w.ToLowerInvariant();
                if (wl.EndsWith("l", StringComparison.Ordinal) && !wl.StartsWith("0x", StringComparison.Ordinal) && PsConv.TryNumber(w.Substring(0, w.Length - 1), out v)) { p = q; return Mark(new ConstAst { Value = Convert.ToInt64(v, CultureInfo.InvariantCulture) }, st); }
                if (wl.EndsWith("d", StringComparison.Ordinal) && !wl.StartsWith("0x", StringComparison.Ordinal) && PsConv.TryNumber(w.Substring(0, w.Length - 1), out v)) { p = q; return Mark(new ConstAst { Value = Convert.ToDecimal(v, CultureInfo.InvariantCulture) }, st); }
                if (PsConv.TryNumber(w, out v)) { p = q; return Mark(new ConstAst { Value = v }, st); }
                if (commandMode) { var bw = Bareword(); return Mark(new ConstAst { Value = bw }, st); }
                throw Err("Unexpected token '" + w + "' in expression or statement.", "UnexpectedToken");
            }
            if (c == '[')
            {
                string t = TypeName();
                if (t != null) return Mark(new TypeAst { Type = t }, st);
            }
            if (Eof) throw Inc("You must provide a value expression.", "ExpectedValueExpression");
            if (commandMode)
            {
                var w = BarewordExpandable();
                if (w.Parts.Count == 1 && w.Parts[0] is string) return Mark(new ConstAst { Value = w.Parts[0] }, st);
                return Mark(w, st);
            }
            // a bareword in expression mode: Get-Date in ( ) is handled by PipelineOrAssign; here it is an error
            throw Err("Unexpected token '" + TokenText() + "' in expression or statement.", "UnexpectedToken");
        }

        PsAst Variable()
        {
            int st = p;
            p++; // $
            var v = new VarAst();
            if (C == '{')
            {
                int e = s.IndexOf('}', p);
                if (e < 0) throw Inc("Missing '}' in the variable name.", "IncompleteDollarVariableReference");
                string full = s.Substring(p + 1, e - p - 1);
                p = e + 1;
                v.Braced = true;
                int colon = full.IndexOf(':');
                if (colon > 0 && full.IndexOf('\\') < 0 || colon > 0 && full.StartsWith("env:", StringComparison.OrdinalIgnoreCase)) { v.Scope = full.Substring(0, colon); v.Name = full.Substring(colon + 1); }
                else v.Name = full;
                return Mark(v, st);
            }
            if (C == '$' || C == '^' || C == '?' || C == '_' && !IsIdent(At(1))) { v.Name = C.ToString(); p++; return Mark(v, st); }
            int q = p;
            while (q < s.Length && IsIdent(s[q])) q++;
            if (q == p) throw Err("Variable reference is not valid. '$' was not followed by a valid variable name character. Consider using ${} to delimit the name.", "InvalidVariableReference");
            string name = s.Substring(p, q - p);
            p = q;
            if (C == ':' && At(1) != ':')
            {
                // $env:NAME, $script:x, $global:x
                p++;
                int r = p;
                while (r < s.Length && (IsIdent(s[r]) || s[r] == '-' && v.Scope == "" && name.Equals("function", StringComparison.OrdinalIgnoreCase))) r++;
                if (r == p) throw Err("Variable reference is not valid. ':' was not followed by a valid variable name character. Consider using ${} to delimit the name.", "InvalidVariableReferenceWithDrive");
                v.Scope = name; v.Name = s.Substring(p, r - p);
                p = r;
                return Mark(v, st);
            }
            v.Name = name;
            return Mark(v, st);
        }

        PsAst SingleQuoted()
        {
            int st = p;
            p++;
            var sb = new StringBuilder();
            while (true)
            {
                if (Eof) { p = st; throw Inc("The string is missing the terminator: '.", "TerminatorExpectedAtEndOfString"); }
                char c = s[p];
                if (IsSingleQuote(c)) { if (p + 1 < s.Length && IsSingleQuote(s[p + 1])) { sb.Append('\''); p += 2; continue; } p++; break; }
                sb.Append(c); p++;
            }
            return Mark(new ConstAst { Value = sb.ToString() }, st);
        }
        static bool IsSingleQuote(char c) { return c == '\'' || c == '\u2018' || c == '\u2019' || c == '\u201A' || c == '\u201B'; }
        static bool IsDoubleQuote(char c) { return c == '"' || c == '\u201C' || c == '\u201D' || c == '\u201E'; }

        static string Escape(char c)
        {
            switch (c)
            {
                case 'n': return "\n";
                case 't': return "\t";
                case 'r': return "\r";
                case '0': return "\0";
                case 'a': return "\a";
                case 'b': return "\b";
                case 'f': return "\f";
                case 'v': return "\v";
                case 'e': return "\u001b";
            }
            return c.ToString();
        }

        PsAst DoubleQuoted()
        {
            int st = p;
            p++;
            var a = new StrAst();
            var sb = new StringBuilder();
            while (true)
            {
                if (Eof) { p = st; throw Inc("The string is missing the terminator: \".", "TerminatorExpectedAtEndOfString"); }
                char c = s[p];
                if (IsDoubleQuote(c)) { if (p + 1 < s.Length && IsDoubleQuote(s[p + 1])) { sb.Append('"'); p += 2; continue; } p++; break; }
                if (c == '`' && p + 1 < s.Length) { sb.Append(Escape(s[p + 1])); p += 2; continue; }
                if (c == '$' && ExpandStart())
                {
                    if (sb.Length > 0) { a.Parts.Add(sb.ToString()); sb.Clear(); }
                    a.Parts.Add(Embedded());
                    continue;
                }
                sb.Append(c); p++;
            }
            if (sb.Length > 0) a.Parts.Add(sb.ToString());
            if (a.Parts.Count == 0) return Mark(new ConstAst { Value = "" }, st);
            if (a.Parts.Count == 1 && a.Parts[0] is string) return Mark(new ConstAst { Value = a.Parts[0] }, st);
            return Mark(a, st);
        }
        bool ExpandStart() { char n = At(1); return IsVarChar(n) || n == '{' || n == '(' || n == '$' || n == '^'; }
        PsAst Embedded()
        {
            int st = p;
            if (At(1) == '(')
            {
                p += 2;
                var b = StatementList(')');
                SkipAll();
                if (Eof) { p = st; throw Inc("Missing closing ')' in subexpression.", "MissingEndParenthesisInSubexpression"); }
                if (C != ')') throw Err("Missing closing ')' in subexpression.", "MissingEndParenthesisInSubexpression");
                p++;
                return Mark(new SubExprAst { Body = b }, st);
            }
            return Variable();
        }

        PsAst HereString()
        {
            int st = p;
            bool expand = At(1) == '"';
            p += 2;
            while (p < s.Length && (s[p] == ' ' || s[p] == '\t')) p++;
            if (Eof) { p = st; throw Inc("The string is missing the terminator: " + (expand ? "\"@" : "'@") + ".", "TerminatorExpectedAtEndOfString"); }
            if (!IsNl(C)) throw Err("No characters are allowed after a here-string header but before the end of the line.", "UnexpectedCharactersAfterHereStringHeader");
            if (C == '\r') p++;
            if (C == '\n') p++;
            string end = expand ? "\"@" : "'@";
            // find a line starting with the terminator
            int bodyStart = p, e = -1, q = p;
            while (q <= s.Length)
            {
                if (string.Compare(s, q, end, 0, 2, StringComparison.Ordinal) == 0 && (q == bodyStart || s[q - 1] == '\n')) { e = q; break; }
                int nl = s.IndexOf('\n', q);
                if (nl < 0) break;
                q = nl + 1;
            }
            if (e < 0) { p = st; throw Inc("The string is missing the terminator: " + end + ".", "TerminatorExpectedAtEndOfString"); }
            int bodyEnd = e;
            if (bodyEnd > bodyStart && s[bodyEnd - 1] == '\n') bodyEnd--;
            if (bodyEnd > bodyStart && s[bodyEnd - 1] == '\r') bodyEnd--;
            string body = bodyEnd > bodyStart ? s.Substring(bodyStart, bodyEnd - bodyStart) : "";
            p = e + 2;
            if (!expand) return Mark(new ConstAst { Value = body.Replace("\r\n", "\n") }, st);
            // expand $ inside the body
            var a = new StrAst { Here = true };
            var sb = new StringBuilder();
            int save = p;
            p = bodyStart;
            while (p < bodyStart + body.Length)
            {
                char c = s[p];
                if (c == '`' && p + 1 < bodyStart + body.Length) { sb.Append(Escape(s[p + 1])); p += 2; continue; }
                if (c == '$' && ExpandStart()) { if (sb.Length > 0) { a.Parts.Add(sb.ToString()); sb.Clear(); } a.Parts.Add(Embedded()); continue; }
                if (c == '\r') { p++; continue; }
                sb.Append(c); p++;
            }
            if (sb.Length > 0) a.Parts.Add(sb.ToString());
            p = save;
            if (a.Parts.Count == 0) return Mark(new ConstAst { Value = "" }, st);
            return Mark(a, st);
        }

        HashAst HashBody(int st)
        {
            // at '{'
            int open = p;
            p++;
            var h = new HashAst();
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (true)
            {
                SkipSemis();
                if (Eof) { p = open; throw Inc("Missing closing '}' in hash literal.", "MissingEndCurlyBrace"); }
                if (C == '}') { p++; break; }
                int ks = p;
                PsAst key;
                if (C == '\'' || C == '"') key = Primary(false);
                else if (C == '$' || C == '(') key = Unary();
                else
                {
                    int q = p; while (q < s.Length && !char.IsWhiteSpace(s[q]) && s[q] != '=' && s[q] != ';' && s[q] != '}') q++;
                    string k = s.Substring(p, q - p);
                    if (k.Length == 0) throw Err("Missing key before '=' in hash literal.", "MissingKeyInHashLiteral");
                    object num;
                    key = PsConv.TryNumber(k, out num) && char.IsDigit(k[0]) ? Mark(new ConstAst { Value = num }, ks) : Mark(new ConstAst { Value = k }, ks);
                    p = q;
                }
                Skip(false);
                if (C != '=') { if (Eof) throw Inc("Missing '=' operator after key in hash literal.", "MissingEqualsInHashLiteral"); throw Err("Missing '=' operator after key in hash literal.", "MissingEqualsInHashLiteral"); }
                p++;
                Skip(true);
                if (Eof) throw Inc("Missing statement after '=' in hash literal.", "MissingStatementInHashLiteral");
                var ck = key as ConstAst;
                if (ck != null)
                {
                    string kt = PsConv.ToStr(ck.Value);
                    if (!keys.Add(kt)) { p = ks; throw Err("Duplicate keys '" + kt + "' are not allowed in hash literals.", "DuplicateKeyInHashLiteral"); }
                }
                var val = StatementForAssign();
                h.Pairs.Add(new KeyValuePair<PsAst, PsAst>(key, val));
                Skip(false);
                if (C == ';' || IsNl(C)) continue;
                if (C == '}') continue;
                if (Eof) { p = open; throw Inc("Missing closing '}' in hash literal.", "MissingEndCurlyBrace"); }
                throw Err("The hash literal was incomplete.", "IncompleteHashLiteral");
            }
            return Mark(h, st);
        }

        ScriptBlockAst ScriptBlockLit()
        {
            int st = p;
            p++;
            var body = ScriptBody(false, '}', st);
            p++;
            body.Text = s.Substring(st + 1, p - st - 2);
            return Mark(body, st);
        }
    }
}
