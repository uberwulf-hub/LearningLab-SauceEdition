// Cert Lab - the -Filter language of the ActiveDirectory cmdlets: * (everything), Property -eq / -ne / -like /
// -notlike / -gt / -lt / -ge / -le value, joined with -and / -or, -not, parentheses. Values are 'quoted' or "quoted"
// text, numbers, $true / $false, or $variables (read from the caller's session, as the module does for a script block).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public class AdFilter
    {
        Func<WsAdObj, bool> test = o => true;
        public bool Match(WsAdObj o) { return test(o); }

        public static AdFilter Parse(string text, Func<string, string> var)
        {
            var toks = Lex((text ?? "*").Trim());
            var f = new AdFilter();
            if (toks.Count == 0 || toks.Count == 1 && toks[0] == "*") return f;
            int i = 0;
            f.test = Or(toks, ref i, var);
            if (i < toks.Count) throw new Exception("Error parsing query: '" + text + "' Error Message: 'syntax error' at position: '" + (i + 1) + "'.");
            return f;
        }
        static List<string> Lex(string s)
        {
            var l = new List<string>(); int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (c == '(' || c == ')') { l.Add(c.ToString()); i++; continue; }
                if (c == '\'' || c == '"') { int j = s.IndexOf(c, i + 1); if (j < 0) j = s.Length; l.Add("'" + s.Substring(i + 1, j - i - 1)); i = j + 1; continue; }
                int b = i; while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] != '(' && s[i] != ')') i++;
                l.Add(s.Substring(b, i - b));
            }
            return l;
        }
        static Func<WsAdObj, bool> Or(List<string> t, ref int i, Func<string, string> var)
        {
            var a = And(t, ref i, var);
            while (i < t.Count && WsText.Eq(t[i], "-or")) { i++; var b = And(t, ref i, var); var a0 = a; a = o => a0(o) || b(o); }
            return a;
        }
        static Func<WsAdObj, bool> And(List<string> t, ref int i, Func<string, string> var)
        {
            var a = Not(t, ref i, var);
            while (i < t.Count && WsText.Eq(t[i], "-and")) { i++; var b = Not(t, ref i, var); var a0 = a; a = o => a0(o) && b(o); }
            return a;
        }
        static Func<WsAdObj, bool> Not(List<string> t, ref int i, Func<string, string> var)
        {
            if (i < t.Count && WsText.Eq(t[i], "-not")) { i++; var x = Not(t, ref i, var); return o => !x(o); }
            if (i < t.Count && t[i] == "(") { i++; var x = Or(t, ref i, var); if (i < t.Count && t[i] == ")") i++; return x; }
            if (i + 2 >= t.Count) throw new Exception("Error parsing query: a comparison is incomplete.");
            string prop = t[i], op = t[i + 1].ToLowerInvariant(), raw = t[i + 2];
            i += 3;
            string val = raw.StartsWith("'", StringComparison.Ordinal) ? raw.Substring(1) : raw.StartsWith("$", StringComparison.Ordinal) ? (WsText.Eq(raw, "$true") ? "TRUE" : WsText.Eq(raw, "$false") ? "FALSE" : WsText.Eq(raw, "$null") ? "" : var(raw.Substring(1)) ?? "") : raw;
            Func<WsAdObj, string> get = o => Value(o, prop);
            switch (op)
            {
                case "-eq": return o => Same(get(o), val);
                case "-ne": return o => !Same(get(o), val);
                case "-like": return o => WsText.Like(get(o), val);
                case "-notlike": return o => !WsText.Like(get(o), val);
                case "-gt": case "-lt": case "-ge": case "-le":
                    return o =>
                    {
                        double a, b; int cmp = double.TryParse(get(o), NumberStyles.Float, CultureInfo.InvariantCulture, out a) && double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out b) ? a.CompareTo(b) : string.Compare(get(o), val, StringComparison.OrdinalIgnoreCase);
                        return op == "-gt" ? cmp > 0 : op == "-lt" ? cmp < 0 : op == "-ge" ? cmp >= 0 : cmp <= 0;
                    };
            }
            throw new Exception("Operator Not supported: " + t[i - 2]);
        }
        static bool Same(string a, string b) { return WsText.Eq(a, b); }
        public static string Value(WsAdObj o, string prop)
        {
            switch (prop.ToLowerInvariant())
            {
                case "enabled": return o.Get("enabled") == "FALSE" ? "FALSE" : "TRUE";
                case "objectclass": return o.Class;
                case "distinguishedname": return o.Dn;
                case "objectguid": return o.Guid;
                case "lockedout": return o.Yes("lockedOut") ? "TRUE" : "FALSE";
                case "protectedfromaccidentaldeletion": return o.Yes("protectedFromAccidentalDeletion") ? "TRUE" : "FALSE";
                case "isdeleted": return o.Deleted ? "TRUE" : "FALSE";
            }
            return o.Get(PsCmdlets.AdAttrOf(prop));
        }
    }

    public partial class PsCmdlets
    {
        public static string AdAttrOf(string ps) { return AdAttr(ps); }
    }
}
