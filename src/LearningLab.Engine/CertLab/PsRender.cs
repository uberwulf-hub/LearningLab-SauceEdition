// Cert Lab - drawing tables, lists and wide lists as text (column widths, alignment, "..." truncation), and
// Format-Table / Format-List / Format-Wide with chosen and calculated properties.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CertLab
{
    public static class PsRender
    {
        static string CellOf(PsRunspace R, FmtCol c, object o)
        {
            var v = c.Value(R, o);
            if (c.FormatString != null && v != null) { try { return string.Format(PsConv.Us, "{0:" + c.FormatString + "}", PsConv.FwNum(PsConv.Base(v))); } catch (FormatException) { } }
            return PsFormat.Cell(R, v);
        }

        public static List<string> Header(List<FmtCol> cols, int[] w, int width)
        {
            if (cols.Any(c => c.Header.Contains("\n"))) return MultiHeader(cols, w, width);
            var h = new StringBuilder(); var d = new StringBuilder();
            int used = 0;
            for (int i = 0; i < cols.Count; i++)
            {
                bool last = i == cols.Count - 1;
                int cw = last && !cols[i].Right ? Math.Max(1, width - used - 1) : w[i];
                if (cw <= 0) cw = cols[i].Header.Length;
                string hd = cols[i].Header;
                if (hd.Length > cw) hd = cw > 3 ? hd.Substring(0, cw - 3) + "..." : hd.Substring(0, cw);
                string dash = new string('-', hd.Length);
                h.Append(cols[i].Right ? hd.PadLeft(cw) : (last ? hd : hd.PadRight(cw)));
                d.Append(cols[i].Right ? dash.PadLeft(cw) : (last ? dash : dash.PadRight(cw)));
                if (!last) { h.Append(' '); d.Append(' '); }
                used += cw + 1;
            }
            return new List<string> { h.ToString().TrimEnd(), d.ToString().TrimEnd() };
        }

        /// <summary>A header written on two or more lines ("Interface\nIndex", as Get-DnsClientServerAddress shows it).</summary>
        static List<string> MultiHeader(List<FmtCol> cols, int[] w, int width)
        {
            var parts = cols.Select(c => c.Header.Split('\n')).ToList();
            int n = parts.Max(p => p.Length);
            var lines = new List<StringBuilder>();
            for (int k = 0; k <= n; k++) lines.Add(new StringBuilder());
            int used = 0;
            for (int i = 0; i < cols.Count; i++)
            {
                bool last = i == cols.Count - 1;
                int cw = last && !cols[i].Right ? Math.Max(1, width - used - 1) : w[i];
                if (cw <= 0) cw = parts[i].Max(x => x.Length);
                for (int k = 0; k < n; k++)
                {
                    string t = k < parts[i].Length ? parts[i][k] : "";
                    if (t.Length > cw) t = t.Substring(0, cw);
                    lines[k].Append(cols[i].Right && parts[i].Length == 1 ? t.PadLeft(cw) : t.PadRight(cw));
                    if (!last) lines[k].Append(' ');
                }
                string dash = new string('-', Math.Min(cw, parts[i].Max(x => x.Length)));
                lines[n].Append(cols[i].Right && parts[i].Length == 1 ? dash.PadLeft(cw) : dash.PadRight(cw));
                if (!last) lines[n].Append(' ');
                used += cw + 1;
            }
            return lines.Select(b => b.ToString().TrimEnd()).ToList();
        }

        public static string Row(PsRunspace R, object o, List<FmtCol> cols, int[] w, int width)
        {
            var sb = new StringBuilder();
            int used = 0;
            for (int i = 0; i < cols.Count; i++)
            {
                bool last = i == cols.Count - 1;
                string s = CellOf(R, cols[i], o);
                int cw = w[i];
                if (last && !cols[i].Right) cw = Math.Max(1, width - used - 1);
                if (last && cols[i].MultiLine && s.Contains("\n"))
                {
                    var parts = s.Replace("\r", "").Split('\n');
                    sb.Append(PsFormat.Pad(parts[0], cw, false).TrimEnd());
                    for (int k = 1; k < parts.Length; k++) sb.Append('\n').Append(new string(' ', used)).Append(PsFormat.Pad(parts[k], cw, false).TrimEnd());
                    break;
                }
                sb.Append(PsFormat.Pad(s, cw, cols[i].Right && !(last && cw <= 0)));
                if (!last) sb.Append(' ');
                used += cw + 1;
            }
            return sb.ToString().TrimEnd();
        }

        public static int[] AutoWidths(PsRunspace R, List<object> items, List<FmtCol> cols, int width)
        {
            var w = new int[cols.Count];
            for (int i = 0; i < cols.Count; i++)
            {
                int m = cols[i].Header.Length;
                bool numeric = true, anyVal = false;
                foreach (var o in items)
                {
                    var v = PsConv.Base(cols[i].Value(R, o));
                    if (v != null) { anyVal = true; if (!PsConv.IsNumber(v)) numeric = false; }
                    m = Math.Max(m, CellOf(R, cols[i], o).Replace("\n", " ").Length);
                }
                if (anyVal && numeric && cols[i].Get == null) cols[i].Right = true;
                w[i] = m;
            }
            // too wide for the console: the last columns shrink
            int total = w.Sum() + w.Length - 1;
            for (int i = w.Length - 1; i > 0 && total > width; i--)
            {
                int cut = Math.Min(w[i] - Math.Max(cols[i].Header.Length, 4), total - width);
                if (cut > 0) { w[i] -= cut; total -= cut; }
            }
            return w;
        }

        public static List<string> ListBlock(PsRunspace R, object o, List<FmtCol> cols, int width)
        {
            var l = new List<string>();
            if (cols.Count == 0) return l;
            int nw = cols.Max(c => c.Header.Length);
            foreach (var c in cols)
            {
                string v = CellOf(R, c, o);
                var vl = v.Replace("\r", "").Split('\n');
                int room = Math.Max(10, width - nw - 3);
                bool first = true;
                foreach (var part in vl)
                {
                    string rest = part;
                    do
                    {
                        string piece = rest.Length > room ? rest.Substring(0, room) : rest;
                        rest = rest.Length > room ? rest.Substring(room) : "";
                        l.Add((first ? c.Header.PadRight(nw) + " : " : new string(' ', nw + 3)) + piece);
                        first = false;
                    } while (rest.Length > 0);
                }
            }
            for (int i = 0; i < l.Count; i++) l[i] = l[i].TrimEnd();
            return l;
        }

        public static List<string> Wide(PsRunspace R, List<object> items, string prop, int columns, int width)
        {
            var l = new List<string> { "" };
            if (columns <= 0) columns = 2;
            int cw = width / columns;
            var sb = new StringBuilder();
            int n = 0;
            foreach (var o in items)
            {
                object v = prop == null ? WideDefault(R, o) : PsFormat.Cell(R, R.GetMember(o, prop, null));
                string s = PsConv.ToStr(v);
                sb.Append(PsFormat.Pad(s, cw - 1, false)).Append(' ');
                if (++n % columns == 0) { l.Add(sb.ToString().TrimEnd()); sb.Clear(); }
            }
            if (sb.Length > 0) l.Add(sb.ToString().TrimEnd());
            l.Add(""); l.Add("");
            return l;
        }
        static object WideDefault(PsRunspace R, object o)
        {
            var po = PsConv.Base(o) as PsObj;
            if (po != null)
            {
                if (po.Has("Name")) { var n = PsConv.ToStr(po.Get("Name")); return po.Has("PSIsContainer") && PsConv.ToBool(po.Get("PSIsContainer")) ? "[" + n + "]" : n; }
                if (po.Props.Count > 0) return PsFormat.Cell(R, po.ValueOf(po.Props[0]));
            }
            return PsFormat.ScalarText(o);
        }

        /// <summary>Columns for Format-Table / Format-List from -Property (names, wildcards, calculated hashtables).</summary>
        public static List<FmtCol> Columns(PsRunspace R, PsScope sc, object first, List<object> props, bool forList)
        {
            var cols = new List<FmtCol>();
            foreach (var p in props)
            {
                var h = PsConv.Base(p) as IDictionary;
                if (h != null) { cols.Add(Calculated(R, sc, h)); continue; }
                var sb = PsConv.Base(p) as PsScriptBlock;
                if (sb != null) { var b = sb; cols.Add(new FmtCol { Header = sb.Text.Trim(), Get = o => R.InvokeBlockValue(b, sc, o, null, false) }); continue; }
                string name = PsConv.ToStr(p);
                if (PsConv.HasWildcard(name))
                {
                    foreach (var kv in PsMembers.Properties(first)) if (PsConv.Like(kv.Key, name, false)) cols.Add(PsFormat.P(kv.Key, 0));
                    continue;
                }
                var pn = name;
                cols.Add(new FmtCol { Header = name, Prop = name });
            }
            return cols;
        }

        public static FmtCol Calculated(PsRunspace R, PsScope sc, IDictionary h)
        {
            object label = null, expr = null, width = null, align = null, fmt = null;
            foreach (DictionaryEntry e in h)
            {
                string k = PsConv.ToStr(e.Key).ToLowerInvariant();
                if ("label".StartsWith(k, StringComparison.Ordinal) || "name".StartsWith(k, StringComparison.Ordinal)) label = e.Value;
                else if ("expression".StartsWith(k, StringComparison.Ordinal)) expr = e.Value;
                else if ("width".StartsWith(k, StringComparison.Ordinal)) width = e.Value;
                else if ("alignment".StartsWith(k, StringComparison.Ordinal)) align = e.Value;
                else if ("formatstring".StartsWith(k, StringComparison.Ordinal)) fmt = e.Value;
                else throw new PsBinder.BindError("The " + PsConv.ToStr(e.Key) + " key is not valid.", "IllegalKey", null);
            }
            var c = new FmtCol();
            var sbx = PsConv.Base(expr) as PsScriptBlock;
            if (sbx != null) { c.Get = o => R.InvokeBlockValue(sbx, sc, o, null, false); c.Header = label != null ? PsConv.ToStr(label) : sbx.Text.Trim(); }
            else { c.Prop = PsConv.ToStr(expr); c.Header = label != null ? PsConv.ToStr(label) : c.Prop; }
            if (width != null) c.Width = PsConv.ToInt(width);
            if (align != null) c.Right = PsConv.ToStr(align).Equals("Right", StringComparison.OrdinalIgnoreCase);
            if (fmt != null) c.FormatString = PsConv.ToStr(fmt);
            return c;
        }

        /// <summary>Format-Table, Format-List or Format-Wide of the given objects, as lines.</summary>
        public static List<string> Render(PsRunspace R, PsScope sc, List<object> items, string kind, FmtOptions opt, int width)
        {
            var lines = new List<string>();
            items = items.Where(x => !(PsConv.Base(x) is AutoNull) && x != null).SelectMany(x => PsConv.Base(x) is IDictionary ? ((IDictionary)PsConv.Base(x)).Cast<DictionaryEntry>().Select(e => (object)e) : new[] { x }).ToList();
            if (items.Count == 0) return lines;
            if (kind == "wide")
            {
                string prop = opt.Props != null && opt.Props.Count > 0 ? PsConv.ToStr(opt.Props[0]) : null;
                return Wide(R, items, prop, opt.Columns, width);
            }
            var first = items[0];
            List<FmtCol> cols;
            FmtView view = opt.Props == null ? PsFormat.ViewFor(first) : null;
            if (opt.Props != null) cols = Columns(R, sc, first, opt.Props, kind == "list");
            else if (kind == "table" && view != null && view.Kind == "table") cols = view.Cols.Select(c => new FmtCol { Header = c.Header, Prop = c.Prop, Width = c.Width, Right = c.Right, Get = c.Get, MultiLine = c.MultiLine }).ToList();
            else if (kind == "list") { var lp = PsFormat.ListPropsFor(first); var po = PsConv.Base(first) as PsObj; cols = (lp != null && (po == null || po.DefaultDisplay == null) ? lp.ToList() : PsFormat.DefaultProps(first)).Select(p => PsFormat.P(p, 0)).ToList(); }
            else cols = PsFormat.DefaultProps(first).Select(p => PsFormat.P(p, 0)).ToList();
            if (kind == "list")
            {
                lines.Add("");
                foreach (var o in items) { lines.AddRange(ListBlock(R, o, cols, width)); lines.Add(""); }
                lines.Add("");
                return lines;
            }
            int[] w;
            bool fixedView = view != null && !view.Auto && opt.Props == null && !opt.AutoSize;
            if (fixedView) w = cols.Select(c => c.Width).ToArray();
            else { w = AutoWidths(R, items, cols, width); for (int i = 0; i < cols.Count; i++) if (cols[i].Width > 0) w[i] = cols[i].Width; }
            string group = null;
            Func<object, string> gk = opt.GroupBy != null ? (Func<object, string>)(o => PsFormat.Cell(R, R.GetMember(o, opt.GroupBy, null))) : (fixedView && view.GroupKey != null ? view.GroupKey : null);
            bool started = false;
            foreach (var o in items)
            {
                string g = gk == null ? null : gk(o);
                if (g != null)
                {
                    if (g != group)
                    {
                        if (started) lines.Add("");
                        group = g;
                        lines.Add(""); lines.Add("");
                        lines.Add(opt.GroupBy != null ? "   " + opt.GroupBy + ": " + g : view.GroupHeader(o));
                        lines.Add(""); if (opt.GroupBy == null && !view.GroupOneBlank) lines.Add("");
                        if (!opt.HideHeaders) lines.AddRange(Header(cols, w, width));
                    }
                }
                else if (!started) { lines.Add(""); if (!opt.HideHeaders) lines.AddRange(Header(cols, w, width)); }
                started = true;
                lines.Add(Row(R, o, cols, w, width));
            }
            lines.Add(""); lines.Add("");
            return lines;
        }
    }

    public static partial class PsFormat
    {
        /// <summary>Objects as the console would show them, line by line (Out-String, Out-File, cmd > file).</summary>
        public static List<string> ToLines(PsRunspace R, List<object> items, int width)
        {
            var sb = new StringBuilder();
            var od = new OutDefault(R, s => sb.Append(s)) { Width = width };
            foreach (var o in items) od.Push(o);
            od.Finish();
            var l = sb.ToString().Split('\n').ToList();
            if (l.Count > 0 && l[l.Count - 1].Length == 0) l.RemoveAt(l.Count - 1);
            return l;
        }
        public static string ToText(PsRunspace R, List<object> items, int width)
        {
            var sb = new StringBuilder();
            foreach (var l in ToLines(R, items, width)) sb.Append(l).Append("\r\n");
            return sb.ToString();
        }
    }
}
