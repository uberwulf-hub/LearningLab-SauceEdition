// Cert Lab - core cmdlets, part 2: Select-Object, Sort-Object, Group-Object, Measure-Object, Compare-Object,
// Tee-Object, Get-Unique.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CertLab
{
    public partial class PsCmdlets
    {
        /// <summary>A copy with only some properties (Select-Object -Property): type name "Selected.TYPE".</summary>
        public static PsObj Project(CmdCtx c, object o, List<object> props, string[] exclude)
        {
            var src = PsConv.Base(o);
            var r = new PsObj();
            var po = src as PsObj;
            string tn = po != null ? po.TypeName : PsTypes.TypeNameOf(src);
            r.TypeNames.Add("Selected." + (tn == "System.Management.Automation.PSCustomObject" ? tn : tn));
            r.TypeNames.Add("System.Management.Automation.PSCustomObject"); r.TypeNames.Add("System.Object");
            foreach (var p in props)
            {
                var h = PsConv.Base(p) as IDictionary;
                if (h != null)
                {
                    var col = PsRender.Calculated(c.R, c.Scope, h);
                    if (col.Header == null || col.Header.Length == 0) col.Header = col.Prop;
                    r.Add(col.Header, col.Value(c.R, src));
                    continue;
                }
                string name = PsConv.ToStr(p);
                if (PsConv.HasWildcard(name))
                {
                    foreach (var kv in PsMembers.Properties(src))
                        if (PsConv.Like(kv.Key, name, false) && (exclude == null || !exclude.Any(e => PsConv.Like(kv.Key, e, false)))) r.Add(kv.Key, kv.Value);
                    continue;
                }
                if (exclude != null && exclude.Any(e => PsConv.Like(name, e, false))) continue;
                object v = src == null ? null : c.R.GetMember(src, name, c.Ast);
                string realName = name;
                if (po != null) { var pp = po.Find(name); if (pp != null) realName = pp.Name; }
                else foreach (var kv in PsMembers.Properties(src)) if (kv.Key.Equals(name, StringComparison.OrdinalIgnoreCase)) realName = kv.Key;
                r.Add(realName, v);
            }
            if (po != null && po.Computer != null) r.Computer = po.Computer;
            return r;
        }

        static object SortKey(CmdCtx c, object o, object key)
        {
            var h = PsConv.Base(key) as IDictionary;
            if (h != null)
            {
                foreach (DictionaryEntry e in h) if ("expression".StartsWith(PsConv.ToStr(e.Key).ToLowerInvariant(), StringComparison.Ordinal)) key = e.Value;
            }
            var sb = PsConv.Base(key) as PsScriptBlock;
            if (sb != null) return c.R.InvokeBlockValue(sb, c.Scope, o, null, false);
            if (key == null)
            {
                var po = PsConv.Base(o) as PsObj;
                if (po != null && !po.IsCustom && po.Has("Name")) return po.Get("Name");
                return o;
            }
            return PsConv.Base(o) == null ? null : c.R.GetMember(o, PsConv.ToStr(key), c.Ast);
        }

        static bool KeyDescending(object key, bool def)
        {
            var h = PsConv.Base(key) as IDictionary;
            if (h == null) return def;
            foreach (DictionaryEntry e in h)
            {
                string k = PsConv.ToStr(e.Key).ToLowerInvariant();
                if ("descending".StartsWith(k, StringComparison.Ordinal) && k.Length > 0) return PsConv.ToBool(e.Value);
                if ("ascending".StartsWith(k, StringComparison.Ordinal) && k.Length > 0) return !PsConv.ToBool(e.Value);
            }
            return def;
        }

        static void Collect(CmdCtx c, object o) { if (c.HasInput) c.Items.Add(o); else if (c.Has("InputObject")) c.Items.Add(c.Get("InputObject")); }

        static partial void RegisterCore2()
        {
            Add("Select-Object", Util, "Property:object[]@0; ExcludeProperty:string[]; ExpandProperty:string; Unique:switch; Last:int; First:int; Skip:int; SkipLast:int; Index:int[]; InputObject:psobject pipe; Wait:switch", (c, o) =>
            {
                object x = c.HasInput ? o : c.Get("InputObject");
                if (!c.HasInput && !c.Has("InputObject")) return;
                bool streaming = !c.Has("Last") && !c.Has("Unique") && !c.Has("SkipLast") && !c.Has("Index");
                if (!streaming) { c.Items.Add(x); return; }
                int n = c.Data.ContainsKey("n") ? (int)c.Data["n"] : 0;
                c.Data["n"] = n + 1;
                if (n < c.Int("Skip", 0)) return;
                if (c.Has("First") && n - c.Int("Skip", 0) >= c.Int("First", 0)) throw new PsSelectStop();
                EmitSelected(c, x);
                if (c.Has("First") && n + 1 - c.Int("Skip", 0) >= c.Int("First", 0) && !c.Has("Last")) throw new PsSelectStop();
            }).End = c =>
            {
                if (c.Items.Count == 0) return;
                IEnumerable<object> l = c.Items;
                if (c.Has("Index")) { var idx = c.Objs("Index").Select(i => PsConv.ToInt(i)).ToList(); l = c.Items.Where((x, i) => idx.Contains(i)); }
                if (c.Has("Skip")) l = l.Skip(c.Int("Skip", 0));
                if (c.Has("SkipLast")) { var ll = l.ToList(); l = ll.Take(Math.Max(0, ll.Count - c.Int("SkipLast", 0))); }
                if (c.Has("First")) l = l.Take(c.Int("First", 0));
                if (c.Has("Last")) { var ll = l.ToList(); l = ll.Skip(Math.Max(0, ll.Count - c.Int("Last", 0))); }
                var outp = new List<object>();
                foreach (var x in l) { var sub = new List<object>(); var keep = c.St; c.St = c.St.WithOut(y => sub.Add(y)); EmitSelected(c, x); c.St = keep; outp.AddRange(sub); }
                if (c.Sw("Unique"))
                {
                    var uniq = new List<object>();
                    foreach (var x in outp) if (!uniq.Any(u => SameObject(u, x))) uniq.Add(x);
                    outp = uniq;
                }
                foreach (var x in outp) c.Out(x);
            };
            Add("Sort-Object", Util, "Property:object[]@0; Descending:switch; Unique:switch; CaseSensitive:switch; InputObject:psobject pipe; Culture:string", (c, o) => Collect(c, o)).End = c =>
            {
                var keys = c.Has("Property") ? c.Objs("Property") : new List<object> { null };
                bool desc = c.Sw("Descending"), cs = c.Sw("CaseSensitive");
                var items = c.Items.Select((x, i) => new { x, i, k = keys.Select(k => SortKey(c, x, k)).ToList() }).ToList();
                items.Sort((a, b) =>
                {
                    for (int j = 0; j < keys.Count; j++)
                    {
                        int r;
                        try { r = PsConv.Compare(a.k[j], b.k[j], cs); } catch (PsConvertError) { r = string.Compare(PsConv.ToStr(a.k[j]), PsConv.ToStr(b.k[j]), StringComparison.OrdinalIgnoreCase); }
                        if (KeyDescending(keys[j], desc)) r = -r;
                        if (r != 0) return r;
                    }
                    return a.i.CompareTo(b.i);
                });
                var res = items.ToList();
                if (c.Sw("Unique")) { var u = new List<object>(); var seen = new List<List<object>>(); foreach (var it in res) { if (seen.Any(s => s.Zip(it.k, (p, q) => PsConv.Eq(p, q, cs)).All(z => z))) continue; seen.Add(it.k); c.Out(it.x); } return; }
                foreach (var it in res) c.Out(it.x);
            };
            Add("Group-Object", Util, "Property:object[]@0; NoElement:switch; AsHashTable:switch; AsString:switch; CaseSensitive:switch; InputObject:psobject pipe", (c, o) => Collect(c, o)).End = c =>
            {
                var keys = c.Has("Property") ? c.Objs("Property") : new List<object> { null };
                var groups = new List<KeyValuePair<List<object>, List<object>>>();
                foreach (var x in c.Items)
                {
                    var k = keys.Select(kk => kk == null ? x : SortKey(c, x, kk)).ToList();
                    var g = groups.FirstOrDefault(gr => gr.Key.Zip(k, (p, q) => string.Equals(PsConv.ToStr(p), PsConv.ToStr(q), c.Sw("CaseSensitive") ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase)).All(z => z));
                    if (g.Key == null) { g = new KeyValuePair<List<object>, List<object>>(k, new List<object>()); groups.Add(g); }
                    g.Value.Add(x);
                }
                if (c.Sw("AsHashTable"))
                {
                    var h = PsConv.NewHashtable();
                    foreach (var g in groups) { object key = keys.Count == 1 ? (c.Sw("AsString") ? PsConv.ToStr(g.Key[0]) : PsConv.Base(g.Key[0])) : string.Join(", ", g.Key.Select(z => PsConv.ToStr(z)).ToArray()); h[key ?? ""] = g.Value.Count == 1 ? g.Value[0] : (object)g.Value.ToArray(); }
                    c.Out(h); return;
                }
                foreach (var g in groups)
                {
                    var gi = c.Sw("NoElement") ? new PsObj("Microsoft.PowerShell.Commands.GroupInfoNoElement", "Microsoft.PowerShell.Commands.GroupInfo", "System.Object") : new PsObj("Microsoft.PowerShell.Commands.GroupInfo", "System.Object");
                    gi.Val("Count", "int", g.Value.Count);
                    gi.Val("Name", "string", string.Join(", ", g.Key.Select(z => PsConv.ToStr(z)).ToArray()));
                    gi.Val("Group", "System.Collections.ObjectModel.Collection[psobject]", c.Sw("NoElement") ? null : g.Value.ToArray());
                    gi.Val("Values", "System.Collections.ArrayList", new ArrayList(g.Key));
                    if (c.Sw("NoElement")) gi.Remove("Group");
                    c.Out(gi);
                }
            };
            Add("Measure-Object", Util, "Property:object[]@0; Sum:switch; Average:switch; Maximum:switch; Minimum:switch; Line:switch; Word:switch; Character:switch; IgnoreWhiteSpace:switch; InputObject:psobject pipe", (c, o) => Collect(c, o)).End = c =>
            {
                if (c.Sw("Line") || c.Sw("Word") || c.Sw("Character"))
                {
                    int lines = 0, words = 0, chars = 0;
                    foreach (var x in c.Items) { var s = PsConv.ToStr(x); lines += s.Length == 0 ? 0 : s.Split('\n').Length; words += s.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length; chars += c.Sw("IgnoreWhiteSpace") ? s.Count(ch => !char.IsWhiteSpace(ch)) : s.Length; }
                    var t = new PsObj("Microsoft.PowerShell.Commands.TextMeasureInfo", "Microsoft.PowerShell.Commands.MeasureInfo", "System.Object");
                    t.Val("Lines", "System.Nullable[int]", c.Sw("Line") ? (object)lines : null); t.Val("Words", "System.Nullable[int]", c.Sw("Word") ? (object)words : null);
                    t.Val("Characters", "System.Nullable[int]", c.Sw("Character") ? (object)chars : null); t.Val("Property", "string", null);
                    c.Out(t); return;
                }
                var props = c.Has("Property") ? c.Objs("Property") : new List<object> { null };
                foreach (var p in props)
                {
                    var vals = new List<object>();
                    int count = 0;
                    foreach (var x in c.Items)
                    {
                        object v = p == null ? x : SortKey(c, x, p);
                        if (p != null && PsConv.Base(x) is PsObj && !((PsObj)PsConv.Base(x)).Has(PsConv.ToStr(p)) && !(PsConv.Base(p) is PsScriptBlock)) continue;
                        count++;
                        if (v != null) vals.Add(v);
                    }
                    var m = new PsObj("Microsoft.PowerShell.Commands.GenericMeasureInfo", "Microsoft.PowerShell.Commands.MeasureInfo", "System.Object");
                    m.Val("Count", "int", p == null ? c.Items.Count : count);
                    bool num = c.Sw("Sum") || c.Sw("Average");
                    Func<List<double>> nums = () => vals.Select(v => { try { return PsConv.ToDouble(v); } catch (PsConvertError) { throw c.Fail("Input object \"" + PsConv.ToStr(v) + "\" is not numeric.", "NonNumericInputObject", "InvalidType", v, "System.Management.Automation.PSInvalidOperationException"); } }).ToList();
                    m.Val("Average", "System.Nullable[double]", c.Sw("Average") && vals.Count > 0 ? (object)nums().Average() : null);
                    m.Val("Sum", "System.Nullable[double]", c.Sw("Sum") ? (object)nums().Sum() : null);
                    object max = null, min = null;
                    if (c.Sw("Maximum") || c.Sw("Minimum"))
                        foreach (var v in vals)
                        {
                            object nv = v; object tmp; if (v is string && PsConv.TryNumber((string)v, out tmp)) nv = tmp;
                            if (max == null || PsConv.Compare(nv, max, false) > 0) max = nv;
                            if (min == null || PsConv.Compare(nv, min, false) < 0) min = nv;
                        }
                    m.Val("Maximum", "System.Nullable[double]", c.Sw("Maximum") && max != null ? (PsConv.IsNumber(max) ? (object)PsConv.ToDouble(max) : max) : null);
                    m.Val("Minimum", "System.Nullable[double]", c.Sw("Minimum") && min != null ? (PsConv.IsNumber(min) ? (object)PsConv.ToDouble(min) : min) : null);
                    m.Val("Property", "string", p == null ? null : PsConv.ToStr(p is IDictionary ? "" : p));
                    c.Out(m);
                }
            };
            Add("Compare-Object", Util, "ReferenceObject:psobject[]@0 !; DifferenceObject:psobject[]@1 ! pipe; Property:object[]; IncludeEqual:switch; ExcludeDifferent:switch; PassThru:switch; CaseSensitive:switch; SyncWindow:int", c => { }, (c, o) => { if (c.HasInput) c.Items.Add(o); }, c =>
            {
                var refs = c.Objs("ReferenceObject");
                var diffs = c.Items.Count > 0 ? c.Items : c.Objs("DifferenceObject");
                var props = c.Has("Property") ? c.Objs("Property") : null;
                Func<object, string> key = x => props == null ? PsConv.ToStr(x) : string.Join("\u0001", props.Select(p => PsConv.ToStr(SortKey(c, x, p))).ToArray());
                var cmp = c.Sw("CaseSensitive") ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
                var dLeft = diffs.ToList();
                var results = new List<KeyValuePair<object, string>>();
                foreach (var r in refs)
                {
                    int i = dLeft.FindIndex(d => cmp.Equals(key(d), key(r)));
                    if (i >= 0) { if (c.Sw("IncludeEqual")) results.Add(new KeyValuePair<object, string>(r, "==")); dLeft.RemoveAt(i); }
                    else if (!c.Sw("ExcludeDifferent")) results.Add(new KeyValuePair<object, string>(r, "<="));
                }
                if (!c.Sw("ExcludeDifferent")) foreach (var d in dLeft) results.Add(new KeyValuePair<object, string>(d, "=>"));
                var ordered = results.Where(x => x.Value == "==").Concat(results.Where(x => x.Value == "=>")).Concat(results.Where(x => x.Value == "<="));
                foreach (var kv in ordered)
                {
                    if (c.Sw("PassThru")) { var po = PsConv.Base(kv.Key) as PsObj; var o2 = po != null ? po.Copy() : kv.Key; if (o2 is PsObj) ((PsObj)o2).Add("SideIndicator", kv.Value); else PsNotes.For(o2).Add("SideIndicator", kv.Value); c.Out(o2); continue; }
                    var o = PsObj.Custom();
                    if (props == null) o.Add("InputObject", kv.Key);
                    else foreach (var p in props) o.Add(PsConv.ToStr(p), SortKey(c, kv.Key, p));
                    o.Add("SideIndicator", kv.Value);
                    c.Out(o);
                }
            });
            Add("Tee-Object", Util, "FilePath:string@0 alias=Path; Variable:string; Append:switch; InputObject:psobject pipe", (c, o) => { if (c.HasInput) { c.Items.Add(o); c.Out(o); } }).End = c =>
            {
                if (c.Has("Variable")) c.R.SetVar(c.Str("Variable"), PsConv.Collapse(c.Items.ToList()), c.Scope);
                if (c.Has("FilePath")) c.R.RedirectToFile(c.Str("FilePath"), c.Items, c.Sw("Append"), c.Scope, c.Ast);
            };
            Add("Get-Unique", Util, "InputObject:psobject pipe; AsString:switch; OnType:switch", (c, o) =>
            {
                if (!c.HasInput) return;
                object last; bool has = c.Data.TryGetValue("last", out last);
                if (has && (c.Sw("OnType") ? PsTypes.TypeNameOf(last) == PsTypes.TypeNameOf(o) : string.Equals(PsConv.ToStr(last), PsConv.ToStr(o), StringComparison.Ordinal))) return;
                c.Data["last"] = o; c.Out(o);
            });
        }

        static void EmitSelected(CmdCtx c, object x)
        {
            if (c.Has("ExpandProperty"))
            {
                string ep = c.Str("ExpandProperty");
                var src = PsConv.Base(x);
                bool f = src is PsObj ? ((PsObj)src).Has(ep) : PsMembers.Properties(src).Any(kv => kv.Key.Equals(ep, StringComparison.OrdinalIgnoreCase));
                if (!f) { c.Error("Property \"" + ep + "\" cannot be found.", "ExpandPropertyNotFound", "InvalidArgument", x, "System.Management.Automation.PSArgumentException"); return; }
                var v = c.R.GetMember(src, ep, c.Ast);
                PsRunspace.EmitValue(v, c.Out);
                return;
            }
            if (c.Has("Property")) { c.Out(Project(c, x, c.Objs("Property"), c.Strs("ExcludeProperty"))); return; }
            c.Out(x);
        }

        static bool SameObject(object a, object b)
        {
            var pa = PsConv.Base(a) as PsObj; var pb = PsConv.Base(b) as PsObj;
            if (pa != null && pb != null)
            {
                if (pa.Props.Count != pb.Props.Count) return false;
                for (int i = 0; i < pa.Props.Count; i++) if (!PsConv.Eq(pa.ValueOf(pa.Props[i]), pb.Get(pa.Props[i].Name), false) && PsConv.ToStr(pa.ValueOf(pa.Props[i])) != PsConv.ToStr(pb.Get(pa.Props[i].Name))) return false;
                return true;
            }
            return PsConv.Eq(a, b, true);
        }
    }
}
