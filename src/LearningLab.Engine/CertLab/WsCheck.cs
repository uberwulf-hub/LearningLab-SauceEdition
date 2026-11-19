// Cert Lab - the checks of the AZ-800 course's try-its, labs and exams that look at the lab (machine "admin"). One
// assertion per line, all must hold. A value is a path from a root: user["bcole"].department, gpo["Kiosk"].links,
// rsop(computer=CL1, user=bcole).setting["NoControlPanel"], dns(DC1).zone["corp.example"].record["www/A"],
// resolve(client=CL1, name=files.tidewater.example), access(user=bcole, path="\\FS1\Sales").rights, vm["LAB-HOST"].nested,
// arc["SRV1"].status, cloud.user["bcole"].upn. "@DC2 user[...]" reads DC2's copy of the directory. Operators as in
// the MD-102 checks: == != has !has like contains >= <= > <; a line without one holds when the value exists.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CertLab
{
    /// <summary>A value in a check: text, a list, or an object whose members are looked up by name (and an index).</summary>
    public class WsVal
    {
        public string Text; public List<string> List; public Func<string, string, WsVal> Member;
        public static WsVal T(string s) { return s == null ? null : new WsVal { Text = s }; }
        public static WsVal L(IEnumerable<string> l) { return new WsVal { List = l.ToList() }; }
        public static WsVal O(string text, Func<string, string, WsVal> m) { return new WsVal { Text = text ?? "", Member = m }; }
        public string Str { get { return List != null ? string.Join(", ", List.ToArray()) : Text ?? ""; } }
    }

    public static partial class WsCheck
    {
        static readonly string[] Ops = { "==", "!=", ">=", "<=", ">", "<", "has", "!has", "like", "contains" };

        public static bool Run(WsLab lab, string script, out string output)
        {
            var sb = new StringBuilder(); bool ok = true;
            foreach (var raw in script.Replace("\r", "").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                string got; bool r;
                try { r = Line(lab, line, out got); } catch (Exception e) { r = false; got = "error: " + e.Message; }
                if (!r) { ok = false; sb.Append("not true: ").Append(line).Append("  (found: ").Append(got).Append(")\n"); }
            }
            output = sb.ToString();
            return ok;
        }

        public static bool Line(WsLab lab, string line, out string found)
        {
            string dc = null;
            if (line.StartsWith("@", StringComparison.Ordinal)) { int sp = line.IndexOf(' '); dc = line.Substring(1, sp - 1); line = line.Substring(sp + 1).Trim(); }
            bool neg = false;
            if (line.StartsWith("not ", StringComparison.Ordinal)) { neg = true; line = line.Substring(4).Trim(); }
            string left = line, op = null, right = null;
            int depth = 0; bool q = false;
            for (int i = 0; i < line.Length && op == null; i++)
            {
                char c = line[i];
                if (c == '"') { q = !q; continue; }
                if (q) continue;
                if (c == '(' || c == '[') depth++; else if (c == ')' || c == ']') depth--;
                if (depth != 0 || !char.IsWhiteSpace(c)) continue;
                foreach (var o in Ops)
                    if (string.CompareOrdinal(line, i + 1, o, 0, o.Length) == 0 && i + 1 + o.Length <= line.Length && (i + 1 + o.Length == line.Length || char.IsWhiteSpace(line[i + 1 + o.Length])))
                    { op = o; left = line.Substring(0, i).Trim(); right = line.Substring(i + 1 + o.Length).Trim(); break; }
            }
            if (right != null && right.Length >= 2 && right.StartsWith("\"", StringComparison.Ordinal) && right.EndsWith("\"", StringComparison.Ordinal)) right = right.Substring(1, right.Length - 2);
            var v = Eval(lab, dc, left);
            found = v == null ? "(nothing)" : v.Str;
            bool res;
            if (op == null) res = v != null && (v.Member != null || v.List != null && v.List.Count > 0 || v.List == null && !string.IsNullOrEmpty(v.Text) && !WsText.Eq(v.Text, "no") && !WsText.Eq(v.Text, "false"));
            else res = Compare(v, op, right ?? "");
            return neg ? !res : res;
        }

        static bool Compare(WsVal v, string op, string want)
        {
            if (op == "has" || op == "!has") { bool h = v != null && (v.List != null ? v.List.Any(x => WsText.Eq(x, want) || WsText.Eq(Short(x), want)) : WsText.Eq(v.Text, want)); return op == "has" ? h : !h; }
            string s = v == null ? "" : v.Str;
            switch (op)
            {
                case "==": return Eq(s, want);
                case "!=": return !Eq(s, want);
                case "like": return WsText.Like(s, want);
                case "contains": return s.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0;
            }
            double a, b;
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out a) || !double.TryParse(want, NumberStyles.Float, CultureInfo.InvariantCulture, out b)) return false;
            return op == ">=" ? a >= b : op == "<=" ? a <= b : op == ">" ? a > b : a < b;
        }
        static string Short(string x) { int i = x.IndexOf('\\'); return i >= 0 ? x.Substring(i + 1) : x; }
        static bool Eq(string a, string b)
        {
            if (WsText.Eq(a, b)) return true;
            if ((WsText.True(a) || WsText.Eq(a, "true")) && (WsText.True(b)) || (WsText.Eq(a, "no") || WsText.Eq(a, "false") || a.Length == 0) && (WsText.Eq(b, "no") || WsText.Eq(b, "false"))) return true;
            double x, y;
            return double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out x) && double.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out y) && x == y;
        }

        /// <summary>Evaluates root(args).member["index"].member...</summary>
        public static WsVal Eval(WsLab lab, string dc, string expr)
        {
            int i = 0;
            string root = Ident(expr, ref i);
            Dictionary<string, string> args = null; string index = null;
            if (i < expr.Length && expr[i] == '(') args = ArgList(expr, ref i);
            if (i < expr.Length && expr[i] == '[') index = Index(expr, ref i);
            var v = Root(lab, dc ?? lab.Fsmo["PDCEmulator"], root, args, index);
            while (v != null && i < expr.Length)
            {
                if (expr[i] != '.') throw new Exception("expected . at " + expr.Substring(i));
                i++;
                string m = Ident(expr, ref i);
                string ix = null;
                if (i < expr.Length && expr[i] == '[') ix = Index(expr, ref i);
                if (v.Member == null) return null;
                v = v.Member(m.ToLowerInvariant(), ix);
            }
            return v;
        }
        static string Ident(string s, ref int i) { int st = i; while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_' || s[i] == '-')) i++; return s.Substring(st, i - st); }
        static string Index(string s, ref int i)
        {
            i++; // [
            var sb = new StringBuilder();
            if (s[i] == '"') { i++; while (s[i] != '"') sb.Append(s[i++]); i++; } else while (s[i] != ']') sb.Append(s[i++]);
            while (s[i] != ']') i++;
            i++;
            return sb.ToString();
        }
        static Dictionary<string, string> ArgList(string s, ref int i)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            i++; int n = 0;
            while (i < s.Length && s[i] != ')')
            {
                while (s[i] == ' ' || s[i] == ',') i++;
                if (s[i] == ')') break;
                string key, val;
                if (s[i] == '"') { var sb0 = new StringBuilder(); i++; while (s[i] != '"') sb0.Append(s[i++]); i++; d["#" + n] = sb0.ToString(); n++; continue; }
                int st = i; while (s[i] != '=' && s[i] != ',' && s[i] != ')') i++;
                key = s.Substring(st, i - st).Trim();
                if (s[i] != '=') { val = key; key = "#" + n; }
                else
                {
                    i++;
                    while (s[i] == ' ') i++;
                    if (s[i] == '"') { var sb = new StringBuilder(); i++; while (s[i] != '"') sb.Append(s[i++]); i++; val = sb.ToString(); }
                    else { int b = i; while (s[i] != ',' && s[i] != ')') i++; val = s.Substring(b, i - b).Trim(); }
                }
                d[key] = val; n++;
            }
            i++;
            return d;
        }
        static string A(Dictionary<string, string> a, string key, int pos) { string v; if (a == null) return null; if (a.TryGetValue(key, out v)) return v; return a.TryGetValue("#" + pos, out v) ? v : null; }
    }
}
