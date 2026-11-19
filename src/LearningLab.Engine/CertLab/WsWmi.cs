// Cert Lab - WMI filters of Group Policy: a small WQL reader ("SELECT * FROM Win32_OperatingSystem WHERE Version LIKE
// '10.0.2%' AND ProductType = '1'") evaluated against a pretend computer's properties. Classes: Win32_OperatingSystem
// (Caption, Version, BuildNumber, ProductType 1 workstation / 2 domain controller / 3 server, OSArchitecture),
// Win32_ComputerSystem (Name, Domain, Manufacturer, Model, TotalPhysicalMemory, PartOfDomain) and Win32_Processor
// (AddressWidth, NumberOfCores). Operators: = <> != < > <= >= LIKE (% and _), AND, OR, NOT, parentheses.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CertLab
{
    public static class WsWmi
    {
        public static Dictionary<string, string> Props(PsMachine m, WsLab lab, string cls)
        {
            var p = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var s = lab == null ? null : lab.Server(m.Name);
            switch ((cls ?? "").ToLowerInvariant())
            {
                case "win32_operatingsystem":
                    p["Caption"] = m.OsCaption; p["Version"] = m.OsVersion; p["BuildNumber"] = m.OsBuild; p["OSArchitecture"] = "64-bit";
                    p["ProductType"] = !m.IsServer ? "1" : s != null && s.IsDc ? "2" : "3";
                    break;
                case "win32_computersystem":
                    p["Name"] = m.Name; p["Domain"] = m.Domain; p["PartOfDomain"] = "True"; p["Manufacturer"] = m.IsServer ? "Lumen Systems" : "Lumen Devices";
                    p["Model"] = m.IsServer ? "Virtual Machine" : "Lumen Book 14"; p["TotalPhysicalMemory"] = m.MemoryBytes.ToString(CultureInfo.InvariantCulture);
                    break;
                case "win32_processor":
                    p["AddressWidth"] = "64"; p["NumberOfCores"] = m.Cpus.ToString(CultureInfo.InvariantCulture); p["Name"] = "Virtual CPU";
                    break;
                default: return null;
            }
            return p;
        }

        /// <summary>Is the query's WHERE clause true on the computer? A syntax error, or an unknown class, is false.</summary>
        public static bool Matches(PsMachine m, WsLab lab, string query)
        {
            string err;
            return Eval(m, lab, query, out err) && err == null;
        }
        public static string Check(string query)
        {
            string err; var m = new Regex("^\\s*select\\s+\\*\\s+from\\s+(\\w+)(\\s+where\\s+(.+))?$", RegexOptions.IgnoreCase | RegexOptions.Singleline).Match(query ?? "");
            if (!m.Success) return "A WMI filter query has the form SELECT * FROM Class WHERE condition.";
            var toks = Lex(m.Groups[3].Value);
            int i = 0;
            try { if (m.Groups[3].Success) { Or(toks, ref i, null); if (i < toks.Count) return "The query has extra text after the condition: " + toks[i]; } }
            catch (Exception e) { return "The query's condition isn't valid: " + e.Message; }
            return null;
        }

        public static bool Eval(PsMachine pm, WsLab lab, string query, out string error)
        {
            error = Check(query);
            if (error != null) return false;
            var m = new Regex("^\\s*select\\s+\\*\\s+from\\s+(\\w+)(\\s+where\\s+(.+))?$", RegexOptions.IgnoreCase | RegexOptions.Singleline).Match(query);
            var props = Props(pm, lab, m.Groups[1].Value);
            if (props == null) { error = "Invalid class " + m.Groups[1].Value; return false; }
            if (!m.Groups[3].Success) return true;
            var toks = Lex(m.Groups[3].Value);
            int i = 0;
            return Or(toks, ref i, props);
        }

        static List<string> Lex(string s)
        {
            var l = new List<string>();
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (c == '\'' || c == '"') { int j = s.IndexOf(c, i + 1); if (j < 0) throw new Exception("a quote isn't closed"); l.Add("'" + s.Substring(i + 1, j - i - 1)); i = j + 1; continue; }
                if (c == '(' || c == ')') { l.Add(c.ToString()); i++; continue; }
                if ("<>=!".IndexOf(c) >= 0) { int st = i; while (i < s.Length && "<>=!".IndexOf(s[i]) >= 0) i++; l.Add(s.Substring(st, i - st)); continue; }
                int b = i;
                while (i < s.Length && !char.IsWhiteSpace(s[i]) && "()<>=!'\"".IndexOf(s[i]) < 0) i++;
                l.Add(s.Substring(b, i - b));
            }
            return l;
        }
        static bool Or(List<string> t, ref int i, Dictionary<string, string> p)
        {
            bool v = And(t, ref i, p);
            while (i < t.Count && t[i].Equals("OR", StringComparison.OrdinalIgnoreCase)) { i++; bool w = And(t, ref i, p); v = v || w; }
            return v;
        }
        static bool And(List<string> t, ref int i, Dictionary<string, string> p)
        {
            bool v = Not(t, ref i, p);
            while (i < t.Count && t[i].Equals("AND", StringComparison.OrdinalIgnoreCase)) { i++; bool w = Not(t, ref i, p); v = v && w; }
            return v;
        }
        static bool Not(List<string> t, ref int i, Dictionary<string, string> p)
        {
            if (i < t.Count && t[i].Equals("NOT", StringComparison.OrdinalIgnoreCase)) { i++; return !Not(t, ref i, p); }
            if (i < t.Count && t[i] == "(") { i++; bool v = Or(t, ref i, p); if (i >= t.Count || t[i] != ")") throw new Exception("a parenthesis isn't closed"); i++; return v; }
            if (i + 2 >= t.Count) throw new Exception("a comparison is incomplete");
            string prop = t[i], op = t[i + 1].ToUpperInvariant(), val = t[i + 2];
            i += 3;
            if (val.StartsWith("'", StringComparison.Ordinal)) val = val.Substring(1);
            if (p == null) return true;
            string have; if (!p.TryGetValue(prop, out have)) throw new Exception("no property " + prop);
            if (op == "LIKE") return Regex.IsMatch(have, "^" + Regex.Escape(val).Replace("%", ".*").Replace("_", ".") + "$", RegexOptions.IgnoreCase);
            double a, b;
            int cmp = double.TryParse(have, NumberStyles.Float, CultureInfo.InvariantCulture, out a) && double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out b) ? a.CompareTo(b) : string.Compare(have, val, StringComparison.OrdinalIgnoreCase);
            switch (op) { case "=": return cmp == 0; case "<>": case "!=": return cmp != 0; case "<": return cmp < 0; case ">": return cmp > 0; case "<=": return cmp <= 0; case ">=": return cmp >= 0; }
            throw new Exception("unknown operator " + op);
        }
    }
}
