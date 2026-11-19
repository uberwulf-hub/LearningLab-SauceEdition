// Network Lab - a small JSON reader and writer (objects are Dictionary<string, object>, arrays List<object>,
// numbers double, plus string, bool and null). Projects (.netlab), brand.json and the layouts use it.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace NetworkLab
{
    public class JObj : Dictionary<string, object>
    {
        public JObj() : base(StringComparer.Ordinal) { }
        public string Str(string k, string def) { object v; if (TryGetValue(k, out v) && v != null) return Convert.ToString(v, CultureInfo.InvariantCulture); return def; }
        public string Str(string k) { return Str(k, ""); }
        public double Num(string k, double def) { object v; if (TryGetValue(k, out v) && v is double) return (double)v; if (v is string) { double d; if (double.TryParse((string)v, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d; } return def; }
        public int Int(string k, int def) { return (int)Math.Round(Num(k, def)); }
        public bool Bool(string k, bool def) { object v; if (TryGetValue(k, out v) && v is bool) return (bool)v; return def; }
        public JObj Obj(string k) { object v; if (TryGetValue(k, out v)) return v as JObj; return null; }
        public List<object> Arr(string k) { object v; if (TryGetValue(k, out v) && v is List<object>) return (List<object>)v; return new List<object>(); }
        public JObj Set(string k, object v) { this[k] = v; return this; }
    }

    public static class Json
    {
        public static object Parse(string text)
        {
            int i = 0;
            object v = Value(text, ref i);
            Ws(text, ref i);
            if (i < text.Length) throw new FormatException("Unexpected text after the JSON value at " + i);
            return v;
        }
        public static JObj ParseObj(string text) { return Parse(text) as JObj; }

        static void Ws(string s, ref int i) { while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n')) i++; }

        static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) throw new FormatException("The JSON ends too early");
            char c = s[i];
            if (c == '{')
            {
                var o = new JObj(); i++;
                Ws(s, ref i);
                if (i < s.Length && s[i] == '}') { i++; return o; }
                while (true)
                {
                    Ws(s, ref i);
                    string k = Str(s, ref i);
                    Ws(s, ref i);
                    if (i >= s.Length || s[i] != ':') throw new FormatException("Expected : at " + i);
                    i++;
                    o[k] = Value(s, ref i);
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == '}') { i++; return o; }
                    throw new FormatException("Expected , or } at " + i);
                }
            }
            if (c == '[')
            {
                var a = new List<object>(); i++;
                Ws(s, ref i);
                if (i < s.Length && s[i] == ']') { i++; return a; }
                while (true)
                {
                    a.Add(Value(s, ref i));
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == ']') { i++; return a; }
                    throw new FormatException("Expected , or ] at " + i);
                }
            }
            if (c == '"') return Str(s, ref i);
            if (s.Length - i >= 4 && s.Substring(i, 4) == "true") { i += 4; return true; }
            if (s.Length - i >= 5 && s.Substring(i, 5) == "false") { i += 5; return false; }
            if (s.Length - i >= 4 && s.Substring(i, 4) == "null") { i += 4; return null; }
            int st = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (st == i) throw new FormatException("Unexpected character '" + c + "' at " + i);
            return double.Parse(s.Substring(st, i - st), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        static string Str(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException("Expected a string at " + i);
            i++;
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                    default: sb.Append(e); break;
                }
            }
            throw new FormatException("A string never ends");
        }

        public static string Write(object v) { var sb = new StringBuilder(); W(sb, v, 0); return sb.ToString(); }

        static void Indent(StringBuilder sb, int n) { sb.Append('\n'); sb.Append(' ', n * 2); }

        static void W(StringBuilder sb, object v, int depth)
        {
            if (v == null) { sb.Append("null"); return; }
            if (v is string) { Q(sb, (string)v); return; }
            if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
            if (v is double || v is int || v is long || v is float || v is uint)
            {
                double d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
                if (Math.Abs(d - Math.Round(d)) < 1e-9 && Math.Abs(d) < 1e15) sb.Append(((long)Math.Round(d)).ToString(CultureInfo.InvariantCulture));
                else sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            var dict = v as IDictionary<string, object>;
            if (dict != null)
            {
                if (dict.Count == 0) { sb.Append("{}"); return; }
                sb.Append('{');
                bool first = true;
                foreach (var kv in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    Indent(sb, depth + 1);
                    Q(sb, kv.Key); sb.Append(": ");
                    W(sb, kv.Value, depth + 1);
                }
                Indent(sb, depth); sb.Append('}');
                return;
            }
            var list = v as System.Collections.IList;
            if (list != null)
            {
                if (list.Count == 0) { sb.Append("[]"); return; }
                bool simple = true;
                foreach (var x in list) if (x is IDictionary<string, object> || x is System.Collections.IList) simple = false;
                sb.Append('[');
                for (int k = 0; k < list.Count; k++)
                {
                    if (k > 0) sb.Append(simple ? ", " : ",");
                    if (!simple) Indent(sb, depth + 1);
                    W(sb, list[k], depth + 1);
                }
                if (!simple) Indent(sb, depth);
                sb.Append(']');
                return;
            }
            Q(sb, Convert.ToString(v, CultureInfo.InvariantCulture));
        }

        static void Q(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32 || c > 126) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
