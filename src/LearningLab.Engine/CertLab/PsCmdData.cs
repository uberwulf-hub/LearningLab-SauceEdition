// Cert Lab - CSV and JSON in the simulated PowerShell, the way Windows PowerShell 5.1 writes them: a #TYPE line
// unless -NoTypeInformation, every value in quotes; JSON with 4-space indents, "key":  value, nested objects lined
// up under their key, dates as \/Date(ms)\/, enums as numbers, depth 2 by default.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public partial class PsCmdlets
    {
        static string CsvQuote(object v)
        {
            v = PsConv.Base(v);
            if (v == null) return "";
            string s = v is PsEnum ? ((PsEnum)v).Name : PsFormat.Cell(null, v);
            if (v is DateTime) s = ((DateTime)v).ToString(PsConv.Us);
            if (PsConv.IsCollection(v)) s = PsTypes.TypeNameOf(v);
            if (v is IDictionary) s = PsTypes.TypeNameOf(v);
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        public static List<string> ToCsv(CmdCtx c, List<object> items, string delim, bool noType)
        {
            var l = new List<string>();
            if (items.Count == 0) return l;
            var first = PsConv.Base(items[0]);
            var names = PsMembers.Properties(first).Select(kv => kv.Key).ToList();
            if (!noType) l.Add("#TYPE " + (first is PsObj ? ((PsObj)first).TypeName : PsTypes.TypeNameOf(first)));
            l.Add(string.Join(delim, names.Select(n => "\"" + n + "\"").ToArray()));
            foreach (var x in items)
            {
                var props = PsMembers.Properties(x);
                l.Add(string.Join(delim, names.Select(n => { var kv = props.FirstOrDefault(p => p.Key.Equals(n, StringComparison.OrdinalIgnoreCase)); return kv.Key == null ? "" : CsvQuote(kv.Value); }).ToArray()));
            }
            return l;
        }

        static List<string> CsvFields(string line, char d)
        {
            var f = new List<string>(); var sb = new StringBuilder(); bool q = false;
            for (int i = 0; i < line.Length; i++)
            {
                char ch = line[i];
                if (q) { if (ch == '"') { if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } else q = false; } else sb.Append(ch); continue; }
                if (ch == '"') { q = true; continue; }
                if (ch == d) { f.Add(sb.ToString()); sb.Clear(); continue; }
                sb.Append(ch);
            }
            f.Add(sb.ToString());
            return f;
        }

        public static List<object> FromCsv(List<string> lines, char d, string[] header)
        {
            var res = new List<object>();
            int i = 0;
            string type = null;
            while (i < lines.Count && lines[i].StartsWith("#", StringComparison.Ordinal)) { if (lines[i].StartsWith("#TYPE ", StringComparison.Ordinal)) type = lines[i].Substring(6).Trim(); i++; }
            List<string> names;
            if (header != null) names = header.ToList();
            else { if (i >= lines.Count) return res; names = CsvFields(lines[i++], d); }
            for (int k = 0; k < names.Count; k++) if (names[k].Length == 0) names[k] = "H" + (k + 1);
            for (; i < lines.Count; i++)
            {
                if (lines[i].Length == 0) continue;
                var f = CsvFields(lines[i], d);
                var o = PsObj.Custom();
                if (type != null) o.TypeNames.Insert(0, "CSV:" + type);
                for (int k = 0; k < names.Count; k++) o.Add(names[k], k < f.Count ? f[k] : null);
                res.Add(o);
            }
            return res;
        }

        static char Delim(CmdCtx c) { var s = c.Str("Delimiter"); if (c.Sw("UseCulture")) return ','; return string.IsNullOrEmpty(s) ? ',' : s[0]; }

        static partial void RegisterData()
        {
            Add("ConvertTo-Csv", Util, "InputObject:psobject ! pipe; Delimiter:char@1; NoTypeInformation:switch alias=NTI; UseCulture:switch", (c, o) => { c.Items.Add(c.HasInput ? o : c.Get("InputObject")); }).End = c =>
            {
                foreach (var l in ToCsv(c, c.Items, Delim(c).ToString(), c.Sw("NoTypeInformation"))) c.Out(l);
            };
            Add("Export-Csv", Util, "Path:string@0 alias=PSPath,LiteralPath; InputObject:psobject ! pipe; Delimiter:char@1; NoTypeInformation:switch alias=NTI; Append:switch; Force:switch; NoClobber:switch; Encoding:string; UseCulture:switch", (c, o) => { c.Items.Add(c.HasInput ? o : c.Get("InputObject")); }).End = c =>
            {
                string path = c.R.FullFsPath(c.Str("Path"), c.R.CurrentFsDir);
                if (!c.ShouldProcess(path, "Export-Csv")) return;
                var existing = c.R.FsNode(path);
                bool append = c.Sw("Append") && existing != null && existing.Text.Length > 0;
                var lines = ToCsv(c, c.Items, Delim(c).ToString(), c.Sw("NoTypeInformation") || append);
                if (append && lines.Count > 0) lines.RemoveAt(0);
                var sb = new StringBuilder(); foreach (var l in lines) sb.Append(l).Append("\r\n");
                var why = c.R.WriteFile(path, sb.ToString(), append ? existing.Encoding : (c.Has("Encoding") ? EncodingName(c.Str("Encoding")) : "ascii"), append);
                if (why != null) throw c.Fail(why, "FileOpenFailure", "OpenError", path, "System.IO.DirectoryNotFoundException");
            };
            Add("ConvertFrom-Csv", Util, "InputObject:psobject[]@0 ! pipe; Delimiter:char@1; Header:string[]; UseCulture:switch", (c, o) => { foreach (var x in PsConv.Items(c.HasInput ? o : c.Get("InputObject"))) c.Items.Add(PsConv.ToStr(x)); }).End = c =>
            {
                foreach (var x in FromCsv(c.Items.Select(x => PsConv.ToStr(x)).ToList(), Delim(c), c.Strs("Header"))) c.Out(x);
            };
            Add("Import-Csv", Util, "Path:string[]@0 pipe alias=PSPath,LiteralPath; Delimiter:char@1; Header:string[]; Encoding:string; UseCulture:switch", (c, o) =>
            {
                foreach (var p in c.Strs("Path") ?? new string[0])
                {
                    string full = c.R.FullFsPath(p, c.R.CurrentFsDir);
                    var t = c.R.ReadFileText(full);
                    if (t == null) { c.Error("Could not find file '" + full + "'.", "FileOpenFailure", "OpenError", full, "System.IO.FileNotFoundException"); continue; }
                    foreach (var x in FromCsv(PsRunspace.SplitLines(t), Delim(c), c.Strs("Header"))) c.Out(x);
                }
            });
            Add("ConvertTo-Json", Util, "InputObject:object@0 ! pipe; Depth:int; Compress:switch", (c, o) => { c.Items.Add(c.HasInput ? o : c.Get("InputObject")); }).End = c =>
            {
                if (c.Items.Count == 0) return;
                object v = c.Items.Count == 1 ? c.Items[0] : (object)c.Items.ToArray();
                var sb = new StringBuilder();
                PsJson.Write(sb, v, 0, c.Int("Depth", 2), c.Sw("Compress"), 0);
                c.Out(sb.ToString());
            };
            Add("ConvertFrom-Json", Util, "InputObject:string@0 ! pipe", (c, o) => { c.Items.Add(c.HasInput ? o : c.Get("InputObject")); }).End = c =>
            {
                var text = string.Join("\n", c.Items.Select(x => PsConv.ToStr(x)).ToArray());
                object v;
                try { v = PsJson.Parse(text); }
                catch (FormatException e) { throw c.Fail("Invalid JSON primitive: " + e.Message, "System.ArgumentException", "NotSpecified", null, "System.ArgumentException"); }
                c.Out(v);
            };
        }
    }

    public static class PsJson
    {
        public static void Write(StringBuilder sb, object v, int col, int depth, bool compress, int level)
        {
            v = PsConv.Base(v);
            if (v == null) { sb.Append("null"); return; }
            if (v is string || v is char) { Str(sb, v.ToString()); return; }
            if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
            if (v is PsEnum) { sb.Append(((PsEnum)v).Value.ToString(CultureInfo.InvariantCulture)); return; }
            if (v is double) { sb.Append(((double)v).ToString("R", CultureInfo.InvariantCulture)); return; }
            if (PsConv.IsNumber(v)) { sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture)); return; }
            if (v is DateTime) { Str(sb, "\\/Date(" + (long)(((DateTime)v).AddHours(8) - new DateTime(1970, 1, 1)).TotalMilliseconds + ")\\/", true); return; }
            if (v is PsSwitch) { sb.Append(((PsSwitch)v).IsPresent ? "true" : "false"); return; }
            if (level > depth) { Str(sb, PsConv.ToStr(v)); return; }
            if (PsConv.IsCollection(v))
            {
                var items = ((IEnumerable)v).Cast<object>().ToList();
                if (compress) { sb.Append('['); for (int i = 0; i < items.Count; i++) { if (i > 0) sb.Append(','); Write(sb, items[i], 0, depth, true, level + 1); } sb.Append(']'); return; }
                sb.Append("[\n");
                if (items.Count == 0) sb.Append("\n");
                for (int i = 0; i < items.Count; i++)
                {
                    sb.Append(new string(' ', col + 4));
                    Write(sb, items[i], col + 4, depth, false, level + 1);
                    if (i < items.Count - 1) sb.Append(',');
                    sb.Append('\n');
                }
                sb.Append(new string(' ', col)).Append(']');
                return;
            }
            var props = new List<KeyValuePair<string, object>>();
            if (v is IDictionary) foreach (DictionaryEntry e in (IDictionary)v) props.Add(new KeyValuePair<string, object>(PsConv.ToStr(e.Key), e.Value));
            else props = PsMembers.Properties(v);
            if (props.Count == 0 && !(v is PsObj) && !(v is IDictionary)) { Str(sb, PsConv.ToStr(v)); return; }
            if (compress)
            {
                sb.Append('{');
                for (int i = 0; i < props.Count; i++) { if (i > 0) sb.Append(','); Str(sb, props[i].Key); sb.Append(':'); Write(sb, props[i].Value, 0, depth, true, level + 1); }
                sb.Append('}'); return;
            }
            sb.Append("{\n");
            for (int i = 0; i < props.Count; i++)
            {
                sb.Append(new string(' ', col + 4));
                int before = sb.Length;
                Str(sb, props[i].Key);
                sb.Append(":  ");
                int keyLen = sb.Length - before;
                Write(sb, props[i].Value, col + 4 + keyLen, depth, false, level + 1);
                if (i < props.Count - 1) sb.Append(',');
                sb.Append('\n');
            }
            sb.Append(new string(' ', col)).Append('}');
        }

        static void Str(StringBuilder sb, string s) { Str(sb, s, false); }
        static void Str(StringBuilder sb, string s, bool raw)
        {
            sb.Append('"');
            if (raw) { sb.Append(s); sb.Append('"'); return; }
            foreach (char ch in s)
            {
                switch (ch)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\'': sb.Append("\\u0027"); break;
                    case '<': sb.Append("\\u003c"); break;
                    case '>': sb.Append("\\u003e"); break;
                    case '&': sb.Append("\\u0026"); break;
                    default: if (ch < 32) sb.Append("\\u" + ((int)ch).ToString("x4")); else sb.Append(ch); break;
                }
            }
            sb.Append('"');
        }

        public static object Parse(string s)
        {
            int i = 0;
            var v = Value(s, ref i);
            Ws(s, ref i);
            if (i < s.Length) throw new FormatException(s.Substring(i, Math.Min(10, s.Length - i)));
            return v;
        }
        static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }
        static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) throw new FormatException("(end)");
            char c = s[i];
            if (c == '{')
            {
                i++; var o = PsObj.Custom();
                Ws(s, ref i);
                if (i < s.Length && s[i] == '}') { i++; return o; }
                while (true)
                {
                    Ws(s, ref i);
                    if (i >= s.Length || s[i] != '"') throw new FormatException(i < s.Length ? s[i].ToString() : "(end)");
                    string k = StrVal(s, ref i);
                    Ws(s, ref i);
                    if (i >= s.Length || s[i] != ':') throw new FormatException(":");
                    i++;
                    o.Add(k, Value(s, ref i));
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == '}') { i++; return o; }
                    throw new FormatException(i < s.Length ? s[i].ToString() : "(end)");
                }
            }
            if (c == '[')
            {
                i++; var l = new List<object>();
                Ws(s, ref i);
                if (i < s.Length && s[i] == ']') { i++; return l.ToArray(); }
                while (true)
                {
                    l.Add(Value(s, ref i));
                    Ws(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == ']') { i++; return l.ToArray(); }
                    throw new FormatException(i < s.Length ? s[i].ToString() : "(end)");
                }
            }
            if (c == '"')
            {
                var str = StrVal(s, ref i);
                if (str.StartsWith("/Date(", StringComparison.Ordinal) && str.EndsWith(")/", StringComparison.Ordinal)) { long ms; if (long.TryParse(str.Substring(6, str.Length - 8), out ms)) return new DateTime(1970, 1, 1).AddMilliseconds(ms).AddHours(-8); }
                return str;
            }
            foreach (var w in new[] { "true", "false", "null" })
                if (string.Compare(s, i, w, 0, w.Length, StringComparison.Ordinal) == 0) { i += w.Length; return w == "null" ? null : (object)(w == "true"); }
            int st = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            string num = s.Substring(st, i - st);
            if (num.Length == 0) throw new FormatException(s.Substring(st, Math.Min(10, s.Length - st)));
            int n; long ln; decimal d;
            if (int.TryParse(num, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return n;
            if (long.TryParse(num, NumberStyles.Integer, CultureInfo.InvariantCulture, out ln)) return ln;
            if (decimal.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
            return double.Parse(num, CultureInfo.InvariantCulture);
        }
        static string StrVal(string s, ref int i)
        {
            i++;
            var sb = new StringBuilder();
            while (i < s.Length && s[i] != '"')
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    char e = s[++i];
                    switch (e) { case 'n': sb.Append('\n'); break; case 't': sb.Append('\t'); break; case 'r': sb.Append('\r'); break; case 'b': sb.Append('\b'); break; case 'f': sb.Append('\f'); break; case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i + 1, 4), 16)); i += 4; break; default: sb.Append(e); break; }
                    i++; continue;
                }
                sb.Append(s[i++]);
            }
            i++;
            return sb.ToString();
        }
    }
}
