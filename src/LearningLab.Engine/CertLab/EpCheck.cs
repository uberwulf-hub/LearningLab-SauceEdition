// Cert Lab - the checks of the MD-102 course's try-its, labs and exams that look at the pretend tenant (machine
// "portal"). One assertion per line; all must hold:
//   compliance["Win11 baseline"].bitlocker == Require      device["RB-LT-001"].compliance == Compliant
//   compliance["Win11 baseline"].include has "Sales"        device["RB-LT-001"].app["7-Zip"] == Installed
//   group["Windows devices"].members has RB-LT-001          device["RB-LT-001"].setting["Camera/AllowCamera"] == "Not allowed"
//   signin(user=jordan, device=RB-LT-001, app="Office 365") == Blocked
//   count(compliance, platform="Windows 10 and later") >= 1  not config["Old profile"]     tenant.lapsEnabled == Yes
// Operators: == != has !has like (wildcards *) contains >= <= > <. Without an operator the value must be non-empty
// (an object that exists). Values compare without regard to case; numbers and versions compare as numbers.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CertLab
{
    public static class EpCheck
    {
        class Val { public EpObj Obj; public string Text = ""; public List<string> List; public string Str { get { return List != null ? EpText.Join(List) : Obj != null ? Obj.Name : Text; } } }

        static List<string> Lex(string s)
        {
            var l = new List<string>();
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (c == '"')
                {
                    var sb = new StringBuilder("\""); i++;
                    while (i < s.Length && s[i] != '"') { if (s[i] == '\\' && i + 1 < s.Length) i++; sb.Append(s[i++]); }
                    i++; l.Add(sb.ToString()); continue;
                }
                string two = i + 1 < s.Length ? s.Substring(i, 2) : "";
                if (two == "==" || two == "!=" || two == ">=" || two == "<=") { l.Add(two); i += 2; continue; }
                if ("[]().,=<>".IndexOf(c) >= 0) { l.Add(c.ToString()); i++; continue; }
                int st = i;
                while (i < s.Length && !char.IsWhiteSpace(s[i]) && ("[]().,=<>\"".IndexOf(s[i]) < 0 || s[i] == '.' && i > st && i + 1 < s.Length && char.IsDigit(s[i + 1]) && Regex.IsMatch(s.Substring(st, i - st), "^\\d+(\\.\\d+)*$")) && !(s[i] == '!' && i + 1 < s.Length && s[i + 1] == '=')) i++;
                if (i == st) i++;
                l.Add(s.Substring(st, i - st));
            }
            return l;
        }
        /// <summary>The tokens before the operator, the operator, and everything after it as one raw value.</summary>
        static List<string> LexLine(string s)
        {
            var l = Lex(s);
            int depth = 0;
            for (int k = 0; k < l.Count; k++)
            {
                if (l[k] == "(" || l[k] == "[") depth++;
                else if (l[k] == ")" || l[k] == "]") depth--;
                else if (depth == 0 && k > 0 && Ops.Contains(l[k]))
                {
                    // find where the operator ends in the text: the n-th operator occurrence at depth 0
                    int pos = OpEnd(s, l[k]);
                    string rest = pos < 0 ? string.Join(" ", l.Skip(k + 1).ToArray()) : s.Substring(pos).Trim();
                    if (rest.Length >= 2 && rest.StartsWith("\"", StringComparison.Ordinal) && rest.EndsWith("\"", StringComparison.Ordinal) && rest.IndexOf('"', 1) == rest.Length - 1) rest = rest.Substring(1, rest.Length - 2);
                    var res = l.Take(k + 1).ToList();
                    res.Add("\"" + rest);
                    return res;
                }
            }
            return l;
        }
        static int OpEnd(string s, string op)
        {
            int depth = 0; bool q = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '"') { q = !q; continue; }
                if (q) continue;
                if (c == '(' || c == '[') depth++;
                else if (c == ')' || c == ']') depth--;
                else if (depth == 0 && string.CompareOrdinal(s, i, op, 0, op.Length) == 0 && (i == 0 || char.IsWhiteSpace(s[i - 1]) || !char.IsLetter(op[0])) && (i + op.Length >= s.Length || !char.IsLetterOrDigit(s[i + op.Length]) || !char.IsLetter(op[op.Length - 1])))
                {
                    if (char.IsLetter(op[0]) && i > 0 && !char.IsWhiteSpace(s[i - 1])) continue;
                    return i + op.Length;
                }
            }
            return -1;
        }
        static string Unq(string t) { return t.StartsWith("\"", StringComparison.Ordinal) ? t.Substring(1) : t; }
        static readonly string[] Ops = { "==", "!=", ">=", "<=", ">", "<", "has", "!has", "like", "contains" };

        /// <summary>Runs a check script (several lines); output says which line failed and what it found.</summary>
        public static bool Run(EpTenant t, string script, out string output)
        {
            var sb = new StringBuilder();
            bool ok = true;
            foreach (var raw in script.Replace("\r", "").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                string got;
                bool r;
                try { r = Line(t, line, out got); }
                catch (Exception e) { r = false; got = "error: " + e.Message; }
                if (!r) { ok = false; sb.Append("not true: ").Append(line).Append("  (found: ").Append(got.Replace("\n", ", ")).Append(")\n"); }
            }
            output = sb.ToString();
            return ok;
        }

        public static bool Line(EpTenant t, string line, out string found)
        {
            var tok = LexLine(line);
            bool neg = false;
            int i = 0;
            if (tok.Count > 0 && tok[0] == "not") { neg = true; i = 1; }
            var v = Expr(t, tok, ref i);
            found = v == null ? "(nothing)" : v.Str;
            bool res;
            if (i >= tok.Count) res = v != null && (v.Obj != null || v.List != null && v.List.Count > 0 || v.List == null && v.Text.Length > 0 && !EpText.Eq(v.Text, "false") && !EpText.Eq(v.Text, "no"));
            else
            {
                string op = tok[i++];
                if (!Ops.Contains(op)) throw new Exception("unknown operator " + op);
                string want = string.Join(" ", tok.Skip(i).Select(Unq).ToArray());
                res = Compare(v, op, want);
            }
            return neg ? !res : res;
        }

        static bool Compare(Val v, string op, string want)
        {
            var items = v == null ? new List<string>() : v.List ?? new List<string> { v.Str };
            string s = v == null ? "" : v.Str;
            switch (op)
            {
                case "has": return items.Any(x => EpText.Eq(x, want));
                case "!has": return !items.Any(x => EpText.Eq(x, want));
                case "contains": return s.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0;
                case "like": return Regex.IsMatch(s, "^" + Regex.Escape(want).Replace("\\*", ".*").Replace("\\?", ".") + "$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                case "==": return Cmp(s, want) == 0;
                case "!=": return Cmp(s, want) != 0;
                case ">=": return Cmp(s, want) >= 0;
                case "<=": return Cmp(s, want) <= 0;
                case ">": return Cmp(s, want) > 0;
                case "<": return Cmp(s, want) < 0;
            }
            return false;
        }
        static int Cmp(string a, string b)
        {
            if (Regex.IsMatch(a, "^-?\\d+(\\.\\d+)*$") && Regex.IsMatch(b, "^-?\\d+(\\.\\d+)*$")) return EpTenant.CompareVersion(a, b);
            return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
        }

        static Val Expr(EpTenant t, List<string> tok, ref int i)
        {
            if (i >= tok.Count) throw new Exception("nothing to check");
            string head = tok[i++];
            Val v;
            if (i < tok.Count && tok[i] == "(") v = Call(t, head, Args(tok, ref i));
            else if (i < tok.Count && tok[i] == "[")
            {
                i++; string name = Unq(tok[i++]); Expect(tok, ref i, "]");
                string kind = EpOps.Kind(head);
                var o = t.Find(kind, name);
                v = o == null ? null : new Val { Obj = o };
            }
            else if (head == "tenant") v = new Val { Obj = t.Settings };
            else v = new Val { Text = Unq(head) };
            while (i < tok.Count && tok[i] == ".")
            {
                i++;
                string prop = tok[i++];
                // dotted property names: laps.password, laps.backup, fw.public...
                while ((prop == "laps" || prop == "fw") && i + 1 < tok.Count && tok[i] == "." && Regex.IsMatch(tok[i + 1], "^\\w+$")) { prop += "." + tok[i + 1]; i += 2; }
                string key = null;
                if (i < tok.Count && tok[i] == "[") { i++; key = Unq(tok[i++]); Expect(tok, ref i, "]"); }
                v = Member(t, v, prop, key);
            }
            return v;
        }
        static void Expect(List<string> tok, ref int i, string s) { if (i >= tok.Count || tok[i] != s) throw new Exception("expected " + s); i++; }

        static List<KeyValuePair<string, string>> Args(List<string> tok, ref int i)
        {
            var l = new List<KeyValuePair<string, string>>();
            Expect(tok, ref i, "(");
            while (i < tok.Count && tok[i] != ")")
            {
                if (tok[i] == ",") { i++; continue; }
                string a = tok[i++];
                if (i < tok.Count && tok[i] == "=") { i++; l.Add(new KeyValuePair<string, string>(a, Unq(tok[i++]))); }
                else l.Add(new KeyValuePair<string, string>("", Unq(a)));
            }
            Expect(tok, ref i, ")");
            return l;
        }
        static string Arg(List<KeyValuePair<string, string>> a, string name, int pos)
        {
            foreach (var kv in a) if (EpText.Eq(kv.Key, name)) return kv.Value;
            var loose = a.Where(x => x.Key.Length == 0).ToList();
            return pos >= 0 && pos < loose.Count ? loose[pos].Value : "";
        }

        static Val Call(EpTenant t, string fn, List<KeyValuePair<string, string>> a)
        {
            switch (fn.ToLowerInvariant())
            {
                case "signin":
                    {
                        var s = new EpSignIn { User = Arg(a, "user", 0), Device = Arg(a, "device", 1), App = Arg(a, "app", 2), Platform = Arg(a, "platform", -1), Location = Arg(a, "location", -1), Client = Arg(a, "client", -1), ClientApp = Arg(a, "clientApp", -1) };
                        if (s.App.Length == 0) s.App = "Office 365";
                        if (s.Client.Length == 0) s.Client = "browser";
                        s.Mfa = EpText.Eq(Arg(a, "mfa", -1), "yes") || EpText.Eq(Arg(a, "mfa", -1), "true");
                        return new Val { Text = t.EvaluateSignIn(s).Outcome };
                    }
                case "count":
                    {
                        string kind = EpOps.Kind(Arg(a, "", 0));
                        var objs = t.Of(kind).Where(o => a.Where(x => x.Key.Length > 0).All(x => EpText.Eq(o.Get(x.Key), x.Value))).ToList();
                        return new Val { Text = objs.Count.ToString(CultureInfo.InvariantCulture) };
                    }
                case "members": return new Val { List = t.Members(Arg(a, "", 0)).Select(o => o.Kind == "user" ? o["alias"] : o.Name).ToList() };
                case "canmanage":
                    return new Val { Text = t.CanManage(t.User(Arg(a, "user", 0)), t.Device(Arg(a, "device", 1))) ? "yes" : "no" };
            }
            throw new Exception("unknown function " + fn);
        }

        static Val Member(EpTenant t, Val v, string prop, string key)
        {
            if (v == null) return null;
            var o = v.Obj;
            string p = prop.ToLowerInvariant();
            if (p == "count") return new Val { Text = (v.List != null ? v.List.Count : v.Str.Length > 0 ? 1 : 0).ToString(CultureInfo.InvariantCulture) };
            if (o == null) return null;
            var d = o as EpDevice;
            switch (p)
            {
                case "name": return new Val { Text = o.Name };
                case "include": case "exclude": case "required": case "available": case "uninstall":
                    return new Val { List = o.Groups(p).ToList() };
                case "assigned": return new Val { List = o.Assignments.Where(x => !x.Exclude).Select(x => x.Group).ToList() };
                case "intent": { var a = o.Assignments.FirstOrDefault(x => EpText.Eq(x.Group, key)); return new Val { Text = a == null ? "" : a.Intent }; }
                case "filter": { var a = o.Assignments.FirstOrDefault(x => EpText.Eq(x.Group, key)); return new Val { Text = a == null ? "" : a.Filter }; }
                case "filtermode": { var a = o.Assignments.FirstOrDefault(x => EpText.Eq(x.Group, key)); return new Val { Text = a == null ? "" : a.FilterMode }; }
                case "members": return new Val { List = t.Members(o.Name).Select(x => x.Kind == "user" ? x["alias"] : x.Name).ToList() };
                case "setting": return new Val { Text = d != null ? d.Setting(SettingKey(key)) : o.Get("s:" + SettingKey(key)) };
                case "asr": return new Val { Text = o.Get("asr:" + key) };
                case "app": return new Val { Text = d == null ? "" : d.AppStatus(key) };
                case "policy": return new Val { Text = d == null ? "" : d.PolicyStatus(key) };
                case "list": return new Val { List = o.List(key) };
            }
            if (d != null && p == "groups") return new Val { List = t.GroupsList.Where(g => t.InGroup(g.Name, d)).Select(g => g.Name).ToList() };
            if (o.Kind == "user" && p == "groups") return new Val { List = t.GroupsList.Where(g => t.InGroup(g.Name, o)).Select(g => g.Name).ToList() };
            if (o.Kind == "user" && p == "devices") return new Val { List = t.Devices.Where(x => EpText.Eq(x.PrimaryUser, o["alias"])).Select(x => x.Name).ToList() };
            string val = o.Get(prop);
            if (val.IndexOf('\n') >= 0) return new Val { List = EpText.Lines(val), Text = val };
            return new Val { Text = val.Length > 0 ? val : EpSchemas.Value(o, prop) };
        }
        static string SettingKey(string k) { var s = EpCatalog.Find(k); return s == null ? k : s.Key; }
    }
}
