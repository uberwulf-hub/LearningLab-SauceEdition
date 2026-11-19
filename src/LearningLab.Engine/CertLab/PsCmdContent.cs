// Cert Lab - cmdlets that change provider items in the simulated Windows PowerShell 5.1: New-Item, Remove-Item,
// Copy-Item, Move-Item, Rename-Item, Set-Item, Clear-Item, Invoke-Item; registry values and file properties
// (Get-ItemProperty, Get-ItemPropertyValue, Set-ItemProperty, New-ItemProperty, Remove-ItemProperty,
// Rename-ItemProperty, Clear-ItemProperty); content (Get-Content, Set-Content, Add-Content, Clear-Content), and
// Select-String, Get-FileHash and Get-Acl. Windows PowerShell 5.1's defaults are kept: Set-Content writes ANSI
// ("Default"), > and Out-File write UTF-16, Add-Content keeps a file's encoding, and a UTF-8 file without a byte order
// mark reads as ANSI unless -Encoding UTF8 is given.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CertLab
{
    /// <summary>What a Select-String match shows ("app.log:5:text" relative to the current folder, or the line alone for piped text).</summary>
    public class MatchData
    {
        public PsRunspace R; public string Path = "InputStream", Line = ""; public int LineNumber; public bool PathSet, HasContext;
        public string[] Pre = new string[0], Post = new string[0];
        public string Display() { string dir = null; try { dir = R == null ? null : R.CurrentPPath.DrivePath; } catch (ProvError) { } return Text(dir); }
        public string Text(string dir)
        {
            string shown = Path;
            if (dir != null && PathSet && Path.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
            {
                int off = dir.Length;
                if (off < Path.Length) { if (dir[off - 1] == '\\') shown = Path.Substring(off); else if (Path[off] == '\\') shown = Path.Substring(off + 1); }
            }
            Func<string, int, string, string> fmt = (prefix, n, t) => PathSet ? prefix + shown + ":" + n + ":" + t : prefix + t;
            if (!HasContext) return fmt("", LineNumber, Line);
            var l = new List<string>();
            for (int i = 0; i < Pre.Length; i++) l.Add(fmt("  ", LineNumber - Pre.Length + i, Pre[i]));
            l.Add(fmt("> ", LineNumber, Line));
            for (int i = 0; i < Post.Length; i++) l.Add(fmt("  ", LineNumber + 1 + i, Post[i]));
            return string.Join("\n", l.ToArray());
        }
    }

    public partial class PsCmdlets
    {
        /// <summary>Copy-Item -ToSession / -FromSession: another part of Cert Lab (remoting) can handle them; return true when done.</summary>
        public static Func<CmdCtx, object, bool> CopySessionHook;

        static string StoreEnc(string e)
        {
            switch ((e ?? "").ToLowerInvariant())
            {
                case "": return null;
                case "utf8": return "utf8bom";
                case "unicode": case "string": case "unknown": return "unicode";
                case "bigendianunicode": return "bigendianunicode";
                case "utf32": case "bigendianutf32": return "utf32";
                default: return "ascii";
            }
        }
        static string FsAction(object node, string verb) { var f = node as WinFile; return f == null ? verb + " Item" : verb + (f.IsDir ? " Directory" : " File"); }
        static string Shown(PPath p) { return p.P is FsProvider ? p.Path : p.P is FlatProvider ? p.Leaf : p.Path; }
        static bool MissingPathPrompt(CmdCtx c, out string[] paths)
        {
            paths = null;
            if (c.R.Host == null || !c.R.Host.Interactive || !c.First) { var r = c.MakeError("Cannot process command because of one or more missing mandatory parameters: Path.", "MissingMandatoryParameter", "InvalidArgument", null, "System.Management.Automation.ParameterBindingException"); throw new PsRuntimeException(r, false); }
            c.R.HostWrite("\ncmdlet " + c.Def.Name + " at command pipeline position " + c.Position + "\nSupply values for the following parameters:\n");
            var l = new List<string>();
            for (int i = 0; ; i++) { var s = c.R.Host.ReadLine("Path[" + i + "]: ", false); if (string.IsNullOrEmpty(s)) break; l.Add(s); }
            if (l.Count == 0) { var r = c.MakeError("Cannot bind argument to parameter 'Path' because it is an empty array.", "ParameterArgumentValidationErrorEmptyArrayNotAllowed", "InvalidData", null, "System.Management.Automation.ParameterBindingValidationException"); throw new PsRuntimeException(r, false); }
            paths = l.ToArray();
            return true;
        }

        static void RegisterContentCmdlets()
        {
            RegisterChangeCmdlets();
            RegisterPropertyCmdlets();
            RegisterTextCmdlets();
            RegisterSearchCmdlets();
        }

        // ================================================================ New / Remove / Copy / Move / Rename / Set / Clear / Invoke
        static void RegisterChangeCmdlets()
        {
            var ni = Add("New-Item", MgmtMod, "Path:string[]@0 prop; Name:string prop; ItemType:string prop alias=Type; Value:object pipe prop; Force:switch; Credential:pscredential", (c, o) =>
            {
                var R = c.R; var x = X(c);
                string[] typed = c.Strs("Path");
                if (typed == null && !c.Has("Name")) { if (!MissingPathPrompt(c, out typed)) return; }
                if (typed == null) typed = new[] { "." };
                string type = c.Str("ItemType");
                foreach (var t in typed)
                {
                    PPath p;
                    try
                    {
                        p = R.ParsePath(t);
                        if (c.Has("Name")) foreach (var part in (c.Str("Name") ?? "").Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries)) p = p.Child(part);
                    }
                    catch (ProvError e) { PErr(c, e); continue; }
                    if (p.Segs.Count == 0) { c.Error("Cannot create an item at the root of a drive.", "Argument", "InvalidArgument", t, "System.ArgumentException"); continue; }
                    string tl = (type ?? "").ToLowerInvariant();
                    string action = p.P is FsProvider ? (tl == "directory" || tl == "container" ? "Create Directory" : "Create File") : p.P is RegProvider ? "Create Key" : "New Item";
                    if (!c.ShouldProcess("Destination: " + p.Path, action)) continue;
                    try { c.Out(p.P.NewItem(x, p, type, c.Get("Value"))); }
                    catch (ProvError e) { PErr(c, e); }
                }
            });
            ni.ShouldProcess = true;

            var ri = Add("Remove-Item", MgmtMod, "Path:string[]@0 ! pipe prop ps=Path; LiteralPath:string[] ! prop alias=PSPath ps=LiteralPath; Filter:string; Include:string[]; Exclude:string[]; Recurse:switch; Force:switch; Stream:string[]; Credential:pscredential", (c, o) =>
            {
                CheckPathArg(c, IsLiteral(c) ? "LiteralPath" : "Path");
                var R = c.R; var x = X(c);
                bool filtered = c.Has("Include") || c.Has("Exclude") || c.Has("Filter");
                foreach (var p in ItemsUnfiltered(c, x))
                {
                    object node;
                    try { node = p.P.Find(x, p.Path); } catch (ProvError e) { PErr(c, e); continue; }
                    if (node == null) continue;
                    if (c.Has("Stream"))
                    {
                        var f = node as WinFile;
                        if (f == null || f.IsDir) continue;
                        foreach (var sn in c.Strs("Stream"))
                        {
                            var hits = f.Streams.Keys.Where(k => PsConv.Like(k, sn, false)).ToList();
                            if (hits.Count == 0 && !PsConv.HasWildcard(sn)) { c.Error("Could not open the alternate data stream '" + sn + "' of the file '" + f.FullPath + "'.", "AlternateDataStreamNotFound", "ObjectNotFound", f.FullPath, "System.Management.Automation.ItemNotFoundException"); continue; }
                            foreach (var h in hits) if (c.ShouldProcess(f.FullPath + " Stream: " + h, "Remove File")) f.Streams.Remove(h);
                        }
                        continue;
                    }
                    var targets = new List<KeyValuePair<PPath, object>>();
                    if (filtered && p.P.IsContainer(node) && !p.HasWild) CollectMatching(c, x, p, node, c.Sw("Recurse"), targets);
                    else if (!filtered || NameOk(c, p.P.LeafOf(p.Path))) targets.Add(new KeyValuePair<PPath, object>(p, node));
                    foreach (var tg in targets)
                    {
                        var tp = tg.Key; var tn = tg.Value;
                        string action = tp.P is FsProvider ? FsAction(tn, "Remove") : tp.P is RegProvider ? "Remove Key" : tp.P is VarProvider ? "Remove Variable" : "Remove Item";
                        string target = tp.P is FsProvider ? tp.Path : tp.P is VarProvider ? "Name: " + tp.Leaf : "Item: " + (tp.P is FlatProvider ? tp.Leaf : tp.Path);
                        if (!c.ShouldProcess(target, action)) continue;
                        if (tp.P.IsContainer(tn) && !c.Sw("Recurse") && tp.P.Kids(x, tp.Path, tn).Any())
                        {
                            if (c.Data.ContainsKey("NoChildrenAll")) continue;
                            if (!c.Data.ContainsKey("ChildrenAll"))
                            {
                                string a = R.AskChoice("Confirm", "The item at " + tp.DrivePath + " has children and the Recurse parameter was not specified. If you continue, all children will be removed with the item. Are you sure you want to continue?", "Y");
                                if (a == "A") c.Data["ChildrenAll"] = true;
                                else if (a == "L") { c.Data["NoChildrenAll"] = true; continue; }
                                else if (a != "Y") continue;
                            }
                        }
                        try { tp.P.RemoveItem(x, tp.Path, tn, true); }
                        catch (ProvError e) { PErr(c, e); }
                    }
                }
            });
            ri.ShouldProcess = true; ri.DefaultSet = "Path";

            Action<CmdCtx, object> copyMove = (c, o) =>
            {
                var R = c.R; var x = X(c);
                bool move = c.Def.Name == "Move-Item";
                if (!move && (c.Has("ToSession") || c.Has("FromSession")))
                {
                    if (CopySessionHook != null && CopySessionHook(c, o)) return;
                    c.Error("Cert Lab's practice console does not simulate copying files through a remote session (-ToSession / -FromSession).", "CertLabNotSimulated", "NotImplemented", null, "System.Management.Automation.PSNotSupportedException");
                    return;
                }
                CheckPathArg(c, IsLiteral(c) ? "LiteralPath" : "Path");
                string destT = c.Str("Destination") ?? ".";
                PPath dp;
                try { dp = R.ParsePath(destT); }
                catch (ProvError e) { PErr(c, e); return; }
                foreach (var src in Items(c, x, null, false))
                {
                    if (src.P != dp.P) { c.Error("Source and destination path did not resolve to the same provider.", move ? "MoveItemSourceAndDestinationNotSameProvider" : "CopyItemSourceAndDestinationNotSameProvider", "InvalidArgument", src.Path, "System.Management.Automation.PSArgumentException"); continue; }
                    object node, dnode;
                    try { node = src.P.Find(x, src.Path); dnode = dp.P.Find(x, dp.Path); } catch (ProvError e) { PErr(c, e); continue; }
                    var target = dnode != null && dp.P.IsContainer(dnode) && !(dp.P is FlatProvider && dnode != FlatProvider.RootNode) ? dp.Child(src.P.LeafOf(src.Path)) : dp;
                    if (target.Path.Equals(src.Path, StringComparison.OrdinalIgnoreCase))
                    {
                        if (move) continue;
                        c.Error("Cannot overwrite the item " + src.Path + " with itself.", "CopyError", "WriteError", src.Path, "System.IO.IOException");
                        continue;
                    }
                    string action = src.P is FsProvider ? FsAction(node, move ? "Move" : "Copy") : src.P is RegProvider ? (move ? "Move Item" : "Copy Key") : (move ? "Move Item" : "Copy Item");
                    if (!c.ShouldProcess("Item: " + Shown(src) + " Destination: " + Shown(target), action)) continue;
                    try
                    {
                        src.P.CopyItem(x, src.Path, node, target.Path, c.Sw("Recurse") || move, move);
                        if (c.Sw("PassThru")) c.Out(R.ItemOf(target, x));
                    }
                    catch (ProvError e) { PErr(c, e); }
                }
            };
            var ci = Add("Copy-Item", MgmtMod, "Path:string[]@0 ! pipe prop ps=Path; LiteralPath:string[] ! prop alias=PSPath ps=LiteralPath; Destination:string@1 prop; Container:switch; Force:switch; Filter:string; Include:string[]; Exclude:string[]; Recurse:switch; PassThru:switch; Credential:pscredential; ToSession:PSSession; FromSession:PSSession", copyMove);
            ci.ShouldProcess = true; ci.DefaultSet = "Path";
            var mi = Add("Move-Item", MgmtMod, "Path:string[]@0 ! pipe prop ps=Path; LiteralPath:string[] ! prop alias=PSPath ps=LiteralPath; Destination:string@1 prop; Force:switch; Filter:string; Include:string[]; Exclude:string[]; PassThru:switch; Credential:pscredential", copyMove);
            mi.ShouldProcess = true; mi.DefaultSet = "Path";

            var rn = Add("Rename-Item", MgmtMod, "Path:string@0 ! pipe prop ps=ByPath; LiteralPath:string ! prop alias=PSPath ps=ByLiteralPath; NewName:string@1 ! prop; Force:switch; PassThru:switch; Credential:pscredential", (c, o) =>
            {
                CheckPathArg(c, IsLiteral(c) ? "LiteralPath" : "Path");
                var R = c.R; var x = X(c);
                var list = Items(c, x, null, false);
                if (list.Count == 0)
                {
                    // A wildcard -Path that matches nothing ("summary [old].txt"): 5.1 says the item does not exist.
                    string wp = IsLiteral(c) ? null : c.Str("Path");
                    if (wp != null && wp.IndexOfAny(new[] { '*', '?', '[' }) >= 0)
                        c.Error("Cannot rename because item at '" + wp + "' does not exist.", "InvalidOperation", "InvalidOperation", wp, "System.Management.Automation.PSInvalidOperationException");
                    return;
                }
                if (list.Count > 1) { c.Error("Cannot rename because the target specified represents more than one item.", "Argument", "InvalidArgument", c.Str("Path"), "System.Management.Automation.PSArgumentException"); return; }
                var p = list[0];
                string nn = c.Str("NewName") ?? "";
                if (nn.IndexOfAny(new[] { '\\', '/' }) >= 0)
                {
                    PPath np = null;
                    try { np = R.ParsePath(nn); } catch (ProvError) { }
                    if (np == null || np.P != p.P || !np.P.ParentOf(np.Path).Equals(p.P.ParentOf(p.Path), StringComparison.OrdinalIgnoreCase))
                    { c.Error("Cannot rename the specified target, because it represents a path or device name.", "Argument", "InvalidArgument", nn, "System.Management.Automation.PSArgumentException"); return; }
                    nn = np.Leaf;
                }
                if (!p.P.ValidName(nn)) { c.Error("Cannot rename the specified target, because it represents a path or device name.", "Argument", "InvalidArgument", nn, "System.Management.Automation.PSArgumentException"); return; }
                object node = p.P.Find(x, p.Path);
                var parent = p.Parent();
                var dest = parent == null ? p : parent.Child(nn);
                string action = p.P is FsProvider ? FsAction(node, "Rename") : "Rename Item";
                if (!c.ShouldProcess("Item: " + Shown(p) + " Destination: " + Shown(dest), action)) return;
                try
                {
                    p.P.RenameItem(x, p.Path, node, nn);
                    if (c.Sw("PassThru")) c.Out(R.ItemOf(dest, x));
                }
                catch (ProvError e) { PErr(c, e); }
            });
            rn.ShouldProcess = true; rn.DefaultSet = "ByPath";

            var si = Add("Set-Item", MgmtMod, "Path:string[]@0 ! pipe prop ps=Path; LiteralPath:string[] ! prop alias=PSPath ps=LiteralPath; Value:object@1 pipe prop; Force:switch; PassThru:switch; Filter:string; Include:string[]; Exclude:string[]; Credential:pscredential", (c, o) =>
            {
                CheckPathArg(c, IsLiteral(c) ? "LiteralPath" : "Path");
                var R = c.R; var x = X(c);
                foreach (var t in Typed(c, null))
                {
                    PPath p;
                    try { p = R.ParsePath(t); } catch (ProvError e) { PErr(c, e); continue; }
                    var targets = new List<PPath>();
                    if (!IsLiteral(c) && p.HasWild) targets.AddRange(R.Expand(p, x));
                    else targets.Add(IsLiteral(c) ? p : Unescaped(p));
                    foreach (var tp in targets)
                    {
                        try
                        {
                            var node = tp.P.Find(x, tp.Path);
                            if (node == null && !(tp.P is FlatProvider)) { PErr(c, ProvError.NotFound(tp.P.ShownPath(tp))); continue; }
                            if (tp.P is FsProvider) throw ProvError.NotSupported();
                            if (!c.ShouldProcess("Item: " + Shown(tp) + " Value: " + PsConv.ToStr(c.Get("Value")), "Set Item")) continue;
                            if (node == null) ((FlatProvider)tp.P).Assign(x, tp.Leaf, c.Get("Value"), true);
                            else tp.P.SetItem(x, tp.Path, node, c.Get("Value"));
                            if (c.Sw("PassThru")) c.Out(R.ItemOf(tp, x));
                        }
                        catch (ProvError e) { PErr(c, e); }
                    }
                }
            });
            si.ShouldProcess = true; si.DefaultSet = "Path";

            var cl = Add("Clear-Item", MgmtMod, "Path:string[]@0 ! pipe prop ps=Path; LiteralPath:string[] ! prop alias=PSPath ps=LiteralPath; Force:switch; Filter:string; Include:string[]; Exclude:string[]; Credential:pscredential", (c, o) =>
            {
                CheckPathArg(c, IsLiteral(c) ? "LiteralPath" : "Path");
                var x = X(c);
                foreach (var p in Items(c, x, null, false))
                {
                    try
                    {
                        var node = p.P.Find(x, p.Path);
                        if (p.P is FsProvider) throw ProvError.NotSupported();
                        if (!c.ShouldProcess("Item: " + Shown(p), "Clear Item")) continue;
                        p.P.ClearItem(x, p.Path, node);
                    }
                    catch (ProvError e) { PErr(c, e); }
                }
            });
            cl.ShouldProcess = true; cl.DefaultSet = "Path";

            var ii = Add("Invoke-Item", MgmtMod, "Path:string[]@0 ! pipe prop ps=Path; LiteralPath:string[] ! prop alias=PSPath ps=LiteralPath; Filter:string; Include:string[]; Exclude:string[]; Credential:pscredential", (c, o) =>
            {
                CheckPathArg(c, IsLiteral(c) ? "LiteralPath" : "Path");
                var x = X(c);
                foreach (var p in Items(c, x, null, false))
                {
                    if (!c.ShouldProcess("Item: " + Shown(p), "Invoke Item")) continue;
                    c.Host("Cert Lab: on a real PC, Invoke-Item opens " + Shown(p) + " with the program Windows uses for it. The practice machines have no desktop, so nothing opens here.");
                }
            });
            ii.ShouldProcess = true; ii.DefaultSet = "Path";
        }
        static List<PPath> ItemsUnfiltered(CmdCtx c, ProvCtx x)
        {
            // wildcards expand first; -Include / -Exclude / -Filter are applied by the caller
            var res = new List<PPath>();
            bool lit = IsLiteral(c);
            foreach (var t in Typed(c, null))
            {
                PPath p;
                try { p = c.R.ParsePath(t); } catch (ProvError e) { PErr(c, e); continue; }
                try
                {
                    if (!lit && p.HasWild) { res.AddRange(c.R.Expand(p, x)); continue; }
                    if (!lit) p = Unescaped(p);
                    if (p.P.Find(x, p.Path) == null) { PErr(c, ProvError.NotFound(p.P.ShownPath(p))); continue; }
                    res.Add(p);
                }
                catch (ProvError e) { PErr(c, e); }
            }
            return res;
        }
        static void CollectMatching(CmdCtx c, ProvCtx x, PPath dir, object node, bool recurse, List<KeyValuePair<PPath, object>> into)
        {
            foreach (var k in dir.P.Kids(x, dir.Path, node).ToList())
            {
                if (!x.Force && dir.P.IsHidden(k.Value)) continue;
                var cp = dir.Child(k.Key);
                if (NameOk(c, k.Key)) into.Add(new KeyValuePair<PPath, object>(cp, k.Value));
                else if (recurse && dir.P.IsContainer(k.Value)) CollectMatching(c, x, cp, k.Value, true, into);
            }
        }

        // ================================================================ item properties
        static PsObj PropObj(PsRunspace R, PPath p, IEnumerable<KeyValuePair<string, object>> values)
        {
            var o = PsObj.Custom();
            foreach (var kv in values) o.Add(kv.Key, kv.Value);
            o.Add("PSPath", p.P.Qualified + "::" + p.Path);
            string parent = p.P.ParentOf(p.Path);
            o.Add("PSParentPath", parent.Length == 0 ? "" : p.P.Qualified + "::" + parent);
            o.Add("PSChildName", p.P.LeafOf(p.Path));
            o.Add("PSDrive", PsProviders.DriveObj(R, p.Drive));
            o.Add("PSProvider", PsProviders.ProviderObj(p.P));
            return o;
        }
        static ProvError NoProperty(string name, PPath p)
        {
            return new ProvError("Property " + name + " does not exist at path " + p.Path + ".", "System.Management.Automation.PSArgumentException", "InvalidArgument", name, "System.Management.Automation.PSArgumentException");
        }
        /// <summary>The properties Get-ItemProperty shows for one item (registry values, or a file's properties).</summary>
        static List<KeyValuePair<string, object>> PropsOf(CmdCtx c, ProvCtx x, PPath p, object node, string[] names, bool reportMissing)
        {
            var res = new List<KeyValuePair<string, object>>();
            var k = node as RegKey;
            if (k != null)
            {
                if (names == null) { foreach (var v in k.Values) res.Add(new KeyValuePair<string, object>(PsProviders.RegValueName(v), PsProviders.RegData(c.R, v))); return res; }
                foreach (var n in names)
                {
                    bool wild = PsConv.HasWildcard(n);
                    var hits = k.Values.Where(v => wild ? PsConv.Like(PsProviders.RegValueName(v), n, false) : PsProviders.RegValueName(v).Equals(n, StringComparison.OrdinalIgnoreCase) || n.Length == 0 && v.Name.Length == 0).ToList();
                    if (hits.Count == 0 && !wild && reportMissing) { PErr(c, NoProperty(n, p)); continue; }
                    foreach (var v in hits) res.Add(new KeyValuePair<string, object>(PsProviders.RegValueName(v), PsProviders.RegData(c.R, v)));
                }
                return res;
            }
            var item = p.P.Item(x, p.Path, node, p.Drive);
            foreach (var n in names ?? new string[0])
            {
                var hits = item.Props.Where(pp => PsConv.Like(pp.Name, n, false) && !pp.Name.StartsWith("PS", StringComparison.Ordinal)).ToList();
                if (hits.Count == 0 && !PsConv.HasWildcard(n) && reportMissing) { PErr(c, NoProperty(n, p)); continue; }
                foreach (var pp in hits) res.Add(new KeyValuePair<string, object>(pp.Name, item.ValueOf(pp)));
            }
            return res;
        }
        static object RegConvert(object v, string kind)
        {
            v = PsConv.Base(v);
            switch (kind)
            {
                case "DWord": { var l = PsConv.Items(v).ToList(); long n = l.Count == 0 ? 0 : PsConv.ToLong(l[0]); return unchecked((int)n); }
                case "QWord": { var l = PsConv.Items(v).ToList(); return l.Count == 0 ? 0L : PsConv.ToLong(l[0]); }
                case "MultiString": return v == null ? new string[0] : PsConv.Items(v).Select(z => PsConv.ToStr(z)).ToArray();
                case "Binary": case "Unknown": case "None":
                    if (v is byte[]) return v;
                    return v == null ? new byte[0] : PsConv.Items(v).Select(z => (byte)PsConv.ToInt(z)).ToArray();
                default: return v == null ? "" : string.Join(" ", PsConv.Items(v).Select(z => PsConv.ToStr(z)).ToArray());
            }
        }
        static string KindOf(object v)
        {
            v = PsConv.Base(v);
            if (v is int || v is short || v is byte) return "DWord";
            if (v is long) return "QWord";
            if (v is byte[]) return "Binary";
            if (v is string[] || v is object[]) return "MultiString";
            return "String";
        }
        static string KindName(CmdCtx c, string param)
        {
            var v = c.Get(param);
            if (v == null) return null;
            var e = v as PsEnum;
            string s = e != null ? e.Name : PsConv.ToStr(v);
            var t = PsEnums.Get("Microsoft.Win32.RegistryValueKind").Parse(s);
            return t == null ? s : t.Name;
        }

        static void RegisterPropertyCmdlets()
        {
            Add("Get-ItemProperty", MgmtMod, "Path:string[]@0 ! pipe prop ps=Path; LiteralPath:string[] ! prop alias=PSPath ps=LiteralPath; Name:string[]@1 alias=PSProperty; Filter:string; Include:string[]; Exclude:string[]; Credential:pscredential", (c, o) =>
            {
                CheckPathArg(c, IsLiteral(c) ? "LiteralPath" : "Path");
                var x = X(c);
                foreach (var p in Items(c, x, null, false))
                {
                    try
                    {
                        var node = p.P.Find(x, p.Path);
                        if (!(node is RegKey) && !(node is WinFile)) throw ProvError.NotSupported();
                        if (node is WinFile && !c.Has("Name")) { c.Out(p.P.Item(x, p.Path, node, p.Drive)); continue; }
                        var props = PropsOf(c, x, p, node, c.Strs("Name"), true);
                        if (props.Count == 0) continue;
                        c.Out(PropObj(c.R, p, props));
                    }
                    catch (ProvError e) { PErr(c, e); }
                }
            }).DefaultSet = "Path";

            Add("Get-ItemPropertyValue", MgmtMod, "Path:string[]@0 pipe prop ps=Path; LiteralPath:string[] ! prop alias=PSPath ps=LiteralPath; Name:string[]@1 ! alias=PSProperty; Filter:string; Include:string[]; Exclude:string[]; Credential:pscredential", (c, o) =>
            {
                var x = X(c);
                foreach (var p in Items(c, x, ".", false))
                {
                    try
                    {
                        var node = p.P.Find(x, p.Path);
                        if (!(node is RegKey) && !(node is WinFile)) throw ProvError.NotSupported();
                        foreach (var kv in PropsOf(c, x, p, node, c.Strs("Name"), true)) c.Out(kv.Value);
                    }
                    catch (ProvError e) { PErr(c, e); }
                }
            }).DefaultSet = "Path";

            var sp = Add("Set-ItemProperty", MgmtMod, "Path:string[]@0 ! pipe prop ps=propertyValuePathSet; LiteralPath:string[] ! prop alias=PSPath ps=propertyValueLiteralPathSet; Name:string@1 ! prop alias=PSProperty; Value:object@2 pipe prop; Type:RegistryValueKind alias=PropertyType; PassThru:switch; Force:switch; Filter:string; Include:string[]; Exclude:string[]; Credential:pscredential", (c, o) =>
            {
                CheckPathArg(c, IsLiteral(c) ? "LiteralPath" : "Path");
                var R = c.R; var x = X(c);
                string name = c.Str("Name") ?? "";
                foreach (var p in Items(c, x, null, false))
                {
                    try
                    {
                        var node = p.P.Find(x, p.Path);
                        if (!c.ShouldProcess("Item: " + p.Path + " Property: " + name, "Set Property")) continue;
                        var k = node as RegKey;
                        if (k != null)
                        {
                            string vn = name.Equals("(default)", StringComparison.OrdinalIgnoreCase) ? "" : name;
                            var old = k.Value(vn);
                            string kind = KindName(c, "Type") ?? (old != null ? old.Kind : KindOf(c.Get("Value")));
                            object data;
                            try { data = RegConvert(c.Get("Value"), kind); }
                            catch (PsConvertError e) { c.Error(e.Message, "System.ArgumentException", "InvalidArgument", name, "System.ArgumentException"); continue; }
                            k.Set(vn, data, kind);
                            if (c.Sw("PassThru")) c.Out(PropObj(R, p, PropsOf(c, x, p, k, new[] { PsProviders.RegValueName(k.Value(vn)) }, false)));
                            continue;
                        }
                        if (node is WinFile)
                        {
                            var item = p.P.Item(x, p.Path, node, p.Drive);
                            var pp = item.Find(name);
                            if (pp == null || pp.Setter == null) throw new ProvError("Property " + name + " does not exist or cannot be changed at path " + p.Path + ".", "System.Management.Automation.PSArgumentException", "InvalidArgument", name, "System.Management.Automation.PSArgumentException");
                            try { pp.Setter(c.Get("Value")); }
                            catch (PsConvertError e) { c.Error(e.Message, "System.ArgumentException", "InvalidArgument", name, "System.ArgumentException"); continue; }
                            ((WinFile)node).Modified = ((WinFile)node).Modified;
                            if (c.Sw("PassThru")) c.Out(PropObj(R, p, PropsOf(c, x, p, node, new[] { pp.Name }, false)));
                            continue;
                        }
                        throw ProvError.NotSupported();
                    }
                    catch (ProvError e) { PErr(c, e); }
                }
            });
            sp.ShouldProcess = true; sp.DefaultSet = "propertyValuePathSet";

            var np = Add("New-ItemProperty", MgmtMod, "Path:string[]@0 ! pipe prop ps=Path; LiteralPath:string[] ! prop alias=PSPath ps=LiteralPath; Name:string@1 ! prop alias=PSProperty; PropertyType:RegistryValueKind prop alias=Type; Value:object@2 pipe prop; Force:switch; Filter:string; Include:string[]; Exclude:string[]; Credential:pscredential", (c, o) =>
            {
                CheckPathArg(c, IsLiteral(c) ? "LiteralPath" : "Path");
                var R = c.R; var x = X(c);
                string name = c.Str("Name") ?? "";
                foreach (var p in Items(c, x, null, false))
                {
                    try
                    {
                        var k = p.P.Find(x, p.Path) as RegKey;
                        if (k == null) throw ProvError.NotSupported();
                        string vn = name.Equals("(default)", StringComparison.OrdinalIgnoreCase) ? "" : name;
                        if (k.Value(vn) != null && !c.Sw("Force")) { c.Error("The property already exists.", "System.IO.IOException", "ResourceExists", p.Path, "System.IO.IOException"); continue; }
                        if (!c.ShouldProcess("Item: " + p.Path + " Property: " + name, "New Property")) continue;
                        string kind = KindName(c, "PropertyType") ?? "String";
                        object data;
                        try { data = RegConvert(c.Get("Value"), kind); }
                        catch (PsConvertError e) { c.Error(e.Message, "System.ArgumentException", "InvalidArgument", name, "System.ArgumentException"); continue; }
                        k.Set(vn, data, kind);
                        c.Out(PropObj(R, p, PropsOf(c, x, p, k, new[] { PsProviders.RegValueName(k.Value(vn)) }, false)));
                    }
                    catch (ProvError e) { PErr(c, e); }
                }
            });
            np.ShouldProcess = true; np.DefaultSet = "Path";

            var rp = Add("Remove-ItemProperty", MgmtMod, "Path:string[]@0 ! pipe prop ps=Path; LiteralPath:string[] ! prop alias=PSPath ps=LiteralPath; Name:string[]@1 ! prop alias=PSProperty; Force:switch; Filter:string; Include:string[]; Exclude:string[]; Credential:pscredential", (c, o) =>
            {
                CheckPathArg(c, IsLiteral(c) ? "LiteralPath" : "Path");
                var x = X(c);
                foreach (var p in Items(c, x, null, false))
                {
                    try
                    {
                        var k = p.P.Find(x, p.Path) as RegKey;
                        if (k == null) throw ProvError.NotSupported();
                        foreach (var n in c.Strs("Name"))
                        {
                            bool wild = PsConv.HasWildcard(n);
                            var hits = k.Values.Where(v => wild ? PsConv.Like(PsProviders.RegValueName(v), n, false) : PsProviders.RegValueName(v).Equals(n, StringComparison.OrdinalIgnoreCase)).ToList();
                            if (hits.Count == 0 && !wild) { PErr(c, NoProperty(n, p)); continue; }
                            foreach (var v in hits) if (c.ShouldProcess("Item: " + p.Path + " Property: " + PsProviders.RegValueName(v), "Remove Property")) k.Values.Remove(v);
                        }
                    }
                    catch (ProvError e) { PErr(c, e); }
                }
            });
            rp.ShouldProcess = true; rp.DefaultSet = "Path";

            var rnp = Add("Rename-ItemProperty", MgmtMod, "Path:string@0 ! pipe prop ps=Path; LiteralPath:string ! prop alias=PSPath ps=LiteralPath; Name:string@1 ! prop alias=PSProperty; NewName:string@2 ! prop; PassThru:switch; Force:switch; Filter:string; Include:string[]; Exclude:string[]; Credential:pscredential", (c, o) =>
            {
                CheckPathArg(c, IsLiteral(c) ? "LiteralPath" : "Path");
                var R = c.R; var x = X(c);
                string name = c.Str("Name"), nn = c.Str("NewName");
                foreach (var p in Items(c, x, null, false))
                {
                    try
                    {
                        var k = p.P.Find(x, p.Path) as RegKey;
                        if (k == null) throw ProvError.NotSupported();
                        var v = k.Value(name);
                        if (v == null) { PErr(c, NoProperty(name, p)); continue; }
                        if (k.Value(nn) != null && !nn.Equals(name, StringComparison.OrdinalIgnoreCase) && !c.Sw("Force")) { c.Error("The property already exists.", "System.IO.IOException", "ResourceExists", p.Path, "System.IO.IOException"); continue; }
                        if (!c.ShouldProcess("Item: " + p.Path + " Source Property: " + name + " Destination Property: " + nn, "Rename Property")) continue;
                        var other = k.Value(nn); if (other != null && other != v) k.Values.Remove(other);
                        v.Name = nn;
                        if (c.Sw("PassThru")) c.Out(PropObj(R, p, PropsOf(c, x, p, k, new[] { nn }, false)));
                    }
                    catch (ProvError e) { PErr(c, e); }
                }
            });
            rnp.ShouldProcess = true; rnp.DefaultSet = "Path";

            var cp = Add("Clear-ItemProperty", MgmtMod, "Path:string[]@0 ! pipe prop ps=Path; LiteralPath:string[] ! prop alias=PSPath ps=LiteralPath; Name:string@1 ! prop alias=PSProperty; PassThru:switch; Force:switch; Filter:string; Include:string[]; Exclude:string[]; Credential:pscredential", (c, o) =>
            {
                CheckPathArg(c, IsLiteral(c) ? "LiteralPath" : "Path");
                var R = c.R; var x = X(c);
                string name = c.Str("Name");
                foreach (var p in Items(c, x, null, false))
                {
                    try
                    {
                        var k = p.P.Find(x, p.Path) as RegKey;
                        if (k == null) throw ProvError.NotSupported();
                        var v = k.Value(name.Equals("(default)", StringComparison.OrdinalIgnoreCase) ? "" : name);
                        if (v == null) { PErr(c, NoProperty(name, p)); continue; }
                        if (!c.ShouldProcess("Item: " + p.Path + " Property: " + name, "Clear Property")) continue;
                        v.Data = RegConvert(null, v.Kind);
                        if (c.Sw("PassThru")) c.Out(PropObj(R, p, PropsOf(c, x, p, k, new[] { PsProviders.RegValueName(v) }, false)));
                    }
                    catch (ProvError e) { PErr(c, e); }
                }
            });
            cp.ShouldProcess = true; cp.DefaultSet = "Path";
        }

        // ================================================================ content
        static object NoteLine(string s, PPath p, long n)
        {
            if (s.Length == 0) return s;
            var line = new string(s.ToCharArray());
            var notes = PsNotes.For(line);
            notes.Add("PSPath", p.P.Qualified + "::" + p.Path);
            string parent = p.P.ParentOf(p.Path);
            notes.Add("PSParentPath", parent.Length == 0 ? "" : p.P.Qualified + "::" + parent);
            notes.Add("PSChildName", p.P.LeafOf(p.Path));
            notes.Add("PSDrive", PsProviders.DriveObj(p.Drive));
            notes.Add("PSProvider", PsProviders.ProviderObj(p.P));
            notes.Add("ReadCount", n);
            return line;
        }

        static void RegisterTextCmdlets()
        {
            const string encSet = "Encoding:FileSystemCmdletProviderEncoding";
            Add("Get-Content", MgmtMod, "Path:string[]@0 ! pipe prop ps=Path; LiteralPath:string[] ! prop alias=PSPath ps=LiteralPath; ReadCount:long; TotalCount:long alias=First,Head; Tail:int alias=Last; " + encSet + "; Raw:switch; Delimiter:string; Wait:switch; Stream:string; Force:switch; Filter:string; Include:string[]; Exclude:string[]; Credential:pscredential", (c, o) =>
            {
                CheckPathArg(c, IsLiteral(c) ? "LiteralPath" : "Path");
                var R = c.R; var x = X(c);
                long total = c.Has("TotalCount") ? c.Long("TotalCount", -1) : -1;
                int tail = c.Has("Tail") ? c.Int("Tail", -1) : -1;
                long rc = c.Has("ReadCount") ? c.Long("ReadCount", 1) : 1;
                foreach (var p in Items(c, x, null, true))
                {
                    List<string> lines;
                    try
                    {
                        var node = p.P.Find(x, p.Path);
                        var f = node as WinFile;
                        if (f != null)
                        {
                            if (f.IsDir) throw new ProvError("Access to the path '" + f.FullPath + "' is denied.", "GetContentReaderUnauthorizedAccessError", "PermissionDenied", f.FullPath, "System.UnauthorizedAccessException");
                            string text;
                            if (c.Has("Stream") && !c.Str("Stream").Equals(":$DATA", StringComparison.OrdinalIgnoreCase))
                            {
                                string sn = f.Streams.Keys.FirstOrDefault(k => k.Equals(c.Str("Stream"), StringComparison.OrdinalIgnoreCase));
                                if (sn == null) throw new ProvError("Could not open the alternate data stream '" + c.Str("Stream") + "' of the file '" + f.FullPath + "'.", "GetContentReaderFileNotFoundError", "ObjectNotFound", f.FullPath, "System.IO.FileNotFoundException");
                                text = f.Streams[sn];
                            }
                            else text = c.Str("Encoding") == "Byte" ? PsProviders.Latin(PsProviders.FileBytes(f)) : PsProviders.ReadAs(f, c.Str("Encoding"));
                            f.Accessed = x.M.Now;
                            if (c.Str("Encoding") == "Byte")
                            {
                                var bytes = PsProviders.FileBytes(f).AsEnumerable();
                                if (total >= 0) bytes = bytes.Take((int)total);
                                if (c.Sw("Raw")) { c.Out(bytes.ToArray()); continue; }
                                foreach (var b in bytes) c.Out(b);
                                continue;
                            }
                            if (c.Sw("Raw")) { if (text.Length > 0) c.Out(NoteLine(text, p, 1)); continue; }
                            if (c.Has("Delimiter") && c.Str("Delimiter").Length > 0)
                            {
                                string dl = c.Str("Delimiter"); lines = new List<string>();
                                int st = 0;
                                while (st < text.Length) { int i = text.IndexOf(dl, st, StringComparison.Ordinal); if (i < 0) { lines.Add(text.Substring(st)); break; } lines.Add(text.Substring(st, i - st + dl.Length)); st = i + dl.Length; }
                            }
                            else lines = PsRunspace.SplitLines(text);
                        }
                        else
                        {
                            lines = p.P.ReadContent(x, p.Path, node);
                            if (c.Sw("Raw")) { c.Out(string.Join("\r\n", lines.ToArray())); continue; }
                        }
                    }
                    catch (ProvError e) { PErr(c, e); continue; }
                    var nums = Enumerable.Range(1, lines.Count).Select(i => (long)i).ToList();
                    IEnumerable<int> idx = Enumerable.Range(0, lines.Count);
                    if (total >= 0) idx = idx.Take((int)Math.Min(int.MaxValue, total));
                    if (tail >= 0) { var l = idx.ToList(); idx = l.Skip(Math.Max(0, l.Count - tail)); }
                    var outLines = idx.Select(i => NoteLine(lines[i], p, nums[i])).ToList();
                    if (rc <= 0) { if (outLines.Count > 0) c.Out(outLines.ToArray()); }
                    else if (rc == 1) foreach (var l in outLines) c.Out(l);
                    else for (int i = 0; i < outLines.Count; i += (int)rc) c.Out(outLines.Skip(i).Take((int)rc).ToArray());
                    if (c.Sw("Wait")) c.Host("Cert Lab: with -Wait, a real PC keeps showing new lines as they are added until you press Ctrl+C. The practice console stops here.");
                }
            }).DefaultSet = "Path";

            Action<CmdCtx, object> setAdd = (c, o) =>
            {
                CheckPathArg(c, IsLiteral(c) ? "LiteralPath" : "Path");
                var R = c.R; var x = X(c);
                bool add = c.Def.Name == "Add-Content";
                var values = c.Has("Value") ? (c.Get("Value") == null ? new List<object>() : PsConv.Items(c.Get("Value")).ToList()) : new List<object>();
                bool lit = IsLiteral(c);
                foreach (var t in Typed(c, null))
                {
                    PPath p;
                    try { p = R.ParsePath(t); } catch (ProvError e) { PErr(c, e); continue; }
                    var targets = new List<PPath>();
                    try
                    {
                        if (!lit && p.HasWild)
                        {
                            targets = R.Expand(p, x).Where(q => NameOk(c, q.P.LeafOf(q.Path))).ToList();
                            if (targets.Count == 0) { string sh = p.P.ShownPath(p); c.Error("An object at the specified path " + sh + " does not exist, or has been filtered by the -Include or -Exclude parameter.", "ItemNotFound", "ObjectNotFound", sh, "System.Management.Automation.ItemNotFoundException"); continue; }
                        }
                        else targets.Add(lit ? p : Unescaped(p));
                    }
                    catch (ProvError e) { PErr(c, e); continue; }
                    foreach (var tp in targets)
                    {
                        string key = "written|" + tp.P.Name + "|" + tp.Path;
                        bool append = add || c.Data.ContainsKey(key);
                        if (!c.Data.ContainsKey(key) && !c.ShouldProcess("Path: " + Shown(tp), add ? "Add Content" : "Set Content")) continue;
                        try
                        {
                            if (tp.P is FsProvider) WriteFsContent(c, x, tp, values, append, add);
                            else
                            {
                                var node = tp.P.Find(x, tp.Path);
                                if (node == null && !(tp.P is FlatProvider)) throw ProvError.NotFound(tp.P.ShownPath(tp));
                                tp.P.WriteContent(x, tp.Leaf, node, values, append);
                            }
                            c.Data[key] = true;
                            if (c.Sw("PassThru")) foreach (var v in values) c.Out(v);
                        }
                        catch (ProvError e) { PErr(c, e); }
                    }
                }
            };
            var sc = Add("Set-Content", MgmtMod, "Path:string[]@0 ! prop ps=Path; LiteralPath:string[] ! prop alias=PSPath ps=LiteralPath; Value:object[]@1 ! pipe prop; PassThru:switch; " + encSet + "; NoNewline:switch; Force:switch; Stream:string; Filter:string; Include:string[]; Exclude:string[]; Credential:pscredential", setAdd);
            sc.ShouldProcess = true; sc.DefaultSet = "Path";
            var ac = Add("Add-Content", MgmtMod, "Path:string[]@0 ! prop ps=Path; LiteralPath:string[] ! prop alias=PSPath ps=LiteralPath; Value:object[]@1 ! pipe prop; PassThru:switch; " + encSet + "; NoNewline:switch; Force:switch; Stream:string; Filter:string; Include:string[]; Exclude:string[]; Credential:pscredential", setAdd);
            ac.ShouldProcess = true; ac.DefaultSet = "Path";

            var cc = Add("Clear-Content", MgmtMod, "Path:string[]@0 ! pipe prop ps=Path; LiteralPath:string[] ! prop alias=PSPath ps=LiteralPath; Force:switch; Stream:string; Filter:string; Include:string[]; Exclude:string[]; Credential:pscredential", (c, o) =>
            {
                CheckPathArg(c, IsLiteral(c) ? "LiteralPath" : "Path");
                var x = X(c);
                foreach (var p in Items(c, x, null, true))
                {
                    try
                    {
                        var node = p.P.Find(x, p.Path);
                        if (!c.ShouldProcess("Item: " + Shown(p), "Clear Content")) continue;
                        var f = node as WinFile;
                        if (f != null)
                        {
                            if (f.IsDir) throw new ProvError("Access to the path '" + f.FullPath + "' is denied.", "ClearContentUnauthorizedAccessError", "PermissionDenied", f.FullPath, "System.UnauthorizedAccessException");
                            if (f.ReadOnly && !x.Force) throw new ProvError("Access to the path '" + f.FullPath + "' is denied.", "ClearContentUnauthorizedAccessError", "PermissionDenied", f.FullPath, "System.UnauthorizedAccessException");
                            if (c.Has("Stream")) { var sn = f.Streams.Keys.FirstOrDefault(k => k.Equals(c.Str("Stream"), StringComparison.OrdinalIgnoreCase)); if (sn != null) f.Streams[sn] = ""; continue; }
                            f.Text = ""; f.Encoding = "ascii"; f.FakeSize = -1; f.Bytes = null; f.Modified = x.M.Now;
                            continue;
                        }
                        p.P.ClearContent(x, p.Path, node);
                    }
                    catch (ProvError e) { PErr(c, e); }
                }
            });
            cc.ShouldProcess = true; cc.DefaultSet = "Path";
        }

        static void WriteFsContent(CmdCtx c, ProvCtx x, PPath p, List<object> values, bool append, bool addCmd)
        {
            var R = c.R;
            string full = p.Path;
            var f = x.M.FindFile(full);
            string id = "GetContentWriter";
            if (f != null && f.IsDir) throw new ProvError("Access to the path '" + full + "' is denied.", id + "UnauthorizedAccessError", "PermissionDenied", full, "System.UnauthorizedAccessException");
            string encName = c.Str("Encoding");
            bool bytes = encName == "Byte";
            var sb = new StringBuilder();
            if (bytes) sb.Append(PsProviders.Latin(values.SelectMany(v => v is byte[] ? (IEnumerable<object>)((byte[])v).Cast<object>() : new[] { v }).Select(v => (byte)PsConv.ToInt(v)).ToArray()));
            else foreach (var v in values) { sb.Append(PsConv.ToStr(v)); if (!c.Sw("NoNewline")) sb.Append("\r\n"); }
            if (c.Has("Stream") && !c.Str("Stream").Equals(":$DATA", StringComparison.OrdinalIgnoreCase))
            {
                if (f == null) { var why0 = R.WriteFile(full, "", "ascii", false); if (why0 != null) throw new ProvError(why0, id + "DirectoryNotFoundError", "ObjectNotFound", full, "System.IO.DirectoryNotFoundException"); f = x.M.FindFile(full); }
                string sn = f.Streams.Keys.FirstOrDefault(k => k.Equals(c.Str("Stream"), StringComparison.OrdinalIgnoreCase)) ?? c.Str("Stream");
                string old; f.Streams.TryGetValue(sn, out old);
                f.Streams[sn] = (append && old != null ? old : "") + sb.ToString();
                return;
            }
            if (f != null && f.ReadOnly && !x.Force) throw new ProvError("Access to the path '" + full + "' is denied.", id + "UnauthorizedAccessError", "PermissionDenied", full, "System.UnauthorizedAccessException");
            if (f != null && f.Hidden && !x.Force && !append) throw new ProvError("Access to the path '" + full + "' is denied.", id + "UnauthorizedAccessError", "PermissionDenied", full, "System.UnauthorizedAccessException");
            bool wasRo = f != null && f.ReadOnly;
            if (wasRo) f.Attr &= ~1;
            string enc = bytes ? "ascii" : StoreEnc(encName);
            if (enc == null && !append) enc = "ascii";
            if (enc == null && append && f == null) enc = "ascii";
            var why = R.WriteFile(full, sb.ToString(), enc, append && f != null);
            if (wasRo) { var nf = x.M.FindFile(full); if (nf != null) nf.Attr |= 1; }
            if (why != null) throw new ProvError(why, id + "DirectoryNotFoundError", "ObjectNotFound", full, "System.IO.DirectoryNotFoundException");
        }

        // ================================================================ Select-String, Get-FileHash, Get-Acl
        static void RegisterSearchCmdlets()
        {
            var ss = Add("Select-String", "Microsoft.PowerShell.Utility", "InputObject:psobject ! pipe ps=Object; Pattern:string[]@0 !; Path:string[]@1 ! prop ps=File; LiteralPath:string[] ! prop alias=PSPath ps=LiteralFile; SimpleMatch:switch; CaseSensitive:switch; Quiet:switch; List:switch; Include:string[]; Exclude:string[]; NotMatch:switch; AllMatches:switch; Encoding:string; Context:int[]", null, (c, o) =>
            {
                if (c.HasInput && !c.Has("Path") && !c.Has("LiteralPath")) { c.Items.Add(c.Get("InputObject")); return; }
                if (!c.Has("Path") && !c.Has("LiteralPath")) return;
                var x = X(c);
                foreach (var p in Items(c, x, null, false))
                {
                    var f = p.P.Find(x, p.Path) as WinFile;
                    if (f == null) continue;
                    if (f.IsDir) { c.Error("The file " + f.FullPath + " cannot be read: Access to the path '" + f.FullPath + "' is denied.", "ProcessingFile", "InvalidArgument", f.FullPath, "System.ArgumentException"); continue; }
                    if (Done(c)) return;
                    SearchLines(c, PsRunspace.SplitLines(PsProviders.ReadAs(f, c.Str("Encoding"))), f.FullPath, f.Name);
                }
            }, c =>
            {
                // piped input: text lines (one "InputStream"), or files from Get-ChildItem
                var lines = new List<string>();
                foreach (var it in c.Items)
                {
                    var po = PsConv.Base(it) as PsObj;
                    var f = po == null ? null : po.Base as WinFile;
                    if (f != null)
                    {
                        if (lines.Count > 0) { SearchLines(c, lines, null, "InputStream"); lines = new List<string>(); }
                        if (f.IsDir) continue;
                        if (!Done(c)) SearchLines(c, PsRunspace.SplitLines(PsProviders.ReadAs(f, c.Str("Encoding"))), f.FullPath, f.Name);
                        continue;
                    }
                    lines.Add(PsConv.ToStr(it));
                }
                if (lines.Count > 0 && !Done(c)) SearchLines(c, lines, null, "InputStream");
                if (c.Sw("Quiet")) c.Out(c.Data.ContainsKey("any"));
            });
            ss.DefaultSet = "File";

            Add("Get-FileHash", "Microsoft.PowerShell.Utility", "Path:string[]@0 ! pipe prop ps=Path; LiteralPath:string[] ! prop alias=PSPath ps=LiteralPath; InputStream:object ! ps=Stream; Algorithm:string set=SHA1|SHA256|SHA384|SHA512|MACTripleDES|MD5|RIPEMD160", (c, o) =>
            {
                string alg = (c.Str("Algorithm") ?? "SHA256").ToUpperInvariant();
                if (c.Has("InputStream")) { c.Error("Cert Lab's practice console does not simulate .NET streams; give Get-FileHash a -Path instead.", "CertLabNotSimulated", "NotImplemented", null, "System.Management.Automation.PSNotSupportedException"); return; }
                if (alg == "MACTRIPLEDES") { c.Error("Cert Lab's practice console does not simulate the MACTripleDES algorithm; use SHA256 (the default), SHA1 or MD5.", "CertLabNotSimulated", "NotImplemented", null, "System.Management.Automation.PSNotSupportedException"); return; }
                var x = X(c);
                foreach (var p in Items(c, x, null, false))
                {
                    var f = p.P.Find(x, p.Path) as WinFile;
                    if (f == null || f.IsDir) continue;
                    string hash = Hash(alg, PsProviders.FileBytes(f));
                    var h = new PsObj("Microsoft.Powershell.Utility.FileHash", "System.Management.Automation.PSCustomObject", "System.Object");
                    h.Add("Algorithm", alg == "RIPEMD160" ? "RIPEMD160" : alg);
                    h.Add("Hash", hash);
                    h.Add("Path", f.FullPath);
                    h.ToStr = y => "@{Algorithm=" + alg + "; Hash=" + hash + "; Path=" + f.FullPath + "}";
                    c.Out(h);
                }
            }).DefaultSet = "Path";

            Add("Get-Acl", "Microsoft.PowerShell.Security", "Path:string[]@0 pipe prop ps=ByPath; LiteralPath:string[] ! prop alias=PSPath ps=ByLiteralPath; InputObject:psobject ! ps=ByInputObject; Audit:switch; Filter:string; Include:string[]; Exclude:string[]", (c, o) =>
            {
                var x = X(c); x.Force = true;
                if (c.Has("InputObject")) { c.Error("Cert Lab's practice console does not simulate Get-Acl -InputObject; give it a -Path instead.", "CertLabNotSimulated", "NotImplemented", null, "System.Management.Automation.PSNotSupportedException"); return; }
                foreach (var p in Items(c, x, ".", false))
                {
                    var node = p.P.Find(x, p.Path);
                    if (node is WinFile) c.Out(FileAcl(c.R, p, (WinFile)node));
                    else if (node is RegKey) c.Out(KeyAcl(c.R, p, (RegKey)node));
                    else c.Error("Cert Lab's practice console shows permissions only for files, folders and registry keys.", "NotSupported", "NotImplemented", p.Path, "System.Management.Automation.PSNotSupportedException");
                }
            }).DefaultSet = "ByPath";
        }
        static bool Done(CmdCtx c) { return c.Data.ContainsKey("any") && c.Sw("Quiet"); }

        static string Hash(string alg, byte[] data)
        {
            byte[] h;
            switch (alg)
            {
                case "SHA1": h = System.Security.Cryptography.SHA1.HashData(data); break;
                case "SHA384": h = System.Security.Cryptography.SHA384.HashData(data); break;
                case "SHA512": h = System.Security.Cryptography.SHA512.HashData(data); break;
                case "MD5": h = System.Security.Cryptography.MD5.HashData(data); break;
                case "RIPEMD160": h = Ripemd160.Hash(data); break;   // .NET 10 has none of its own
                default: h = System.Security.Cryptography.SHA256.HashData(data); break;
            }
            return BitConverter.ToString(h).Replace("-", "");
        }

        static void SearchLines(CmdCtx c, List<string> lines, string path, string filename)
        {
            var pats = c.Strs("Pattern") ?? new string[0];
            bool cs = c.Sw("CaseSensitive"), simple = c.Sw("SimpleMatch"), not = c.Sw("NotMatch");
            var rx = new List<Regex>();
            if (!simple)
                foreach (var p in pats)
                {
                    try { rx.Add(new Regex(p, (cs ? RegexOptions.None : RegexOptions.IgnoreCase) | RegexOptions.CultureInvariant)); }
                    catch (ArgumentException e) { throw c.Fail("The string " + p + " is not a valid regular expression: " + e.Message, "InvalidRegex", "InvalidArgument", p, "System.ArgumentException"); }
                }
            int n = lines.Count;
            var hit = new int[n];        // -1: no match, else the index of the pattern that matched
            for (int i = 0; i < n; i++)
            {
                hit[i] = -1;
                for (int j = 0; j < pats.Length; j++)
                {
                    bool m = simple ? lines[i].IndexOf(pats[j], cs ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase) >= 0 : rx[j].IsMatch(lines[i]);
                    if (m) { hit[i] = j; break; }
                }
                if (not) hit[i] = hit[i] < 0 ? 0 : -1;
            }
            var ctx = c.Has("Context") ? c.Get("Context") as object[] : null;
            int pre = 0, post = 0; bool hasCtx = ctx != null;
            if (ctx != null) { pre = ctx.Length > 0 ? PsConv.ToInt(ctx[0]) : 0; post = ctx.Length > 1 ? PsConv.ToInt(ctx[1]) : pre; }
            int lastShown = -1;
            for (int i = 0; i < n; i++)
            {
                if (hit[i] < 0) continue;
                c.Data["any"] = true;
                if (c.Sw("Quiet")) return;
                var md = new MatchData { R = c.R, Line = lines[i], LineNumber = i + 1, PathSet = path != null, Path = path ?? "InputStream", HasContext = hasCtx };
                if (hasCtx)
                {
                    int from = Math.Max(Math.Max(0, i - pre), lastShown + 1);
                    md.Pre = lines.Skip(from).Take(i - from).ToArray();
                    var pl = new List<string>();
                    for (int k = i + 1; k < n && k <= i + post && hit[k] < 0; k++) pl.Add(lines[k]);
                    md.Post = pl.ToArray();
                    lastShown = i + pl.Count;
                }
                string pat = not ? pats.FirstOrDefault() : pats[hit[i]];
                var mo = new PsObj("Microsoft.PowerShell.Commands.MatchInfo", "System.Object");
                mo.Val("IgnoreCase", "bool", !cs);
                mo.Val("LineNumber", "int", i + 1);
                mo.Val("Line", "string", lines[i]);
                mo.Val("Filename", "string", path == null ? "InputStream" : filename);
                mo.Val("Path", "string", path ?? "InputStream");
                mo.Val("Pattern", "string", pat);
                object context = null;
                if (hasCtx)
                {
                    var co = new PsObj("Microsoft.PowerShell.Commands.MatchInfoContext", "System.Object");
                    co.Val("PreContext", "string[]", md.Pre.Cast<object>().ToArray()); co.Val("PostContext", "string[]", md.Post.Cast<object>().ToArray());
                    co.Val("DisplayPreContext", "string[]", md.Pre.Cast<object>().ToArray()); co.Val("DisplayPostContext", "string[]", md.Post.Cast<object>().ToArray());
                    co.ToStr = y => "Microsoft.PowerShell.Commands.MatchInfoContext";
                    context = co;
                }
                mo.Val("Context", "Microsoft.PowerShell.Commands.MatchInfoContext", context);
                object matches = new object[0];
                if (!not && !simple)
                {
                    var r = rx[hit[i]];
                    matches = c.Sw("AllMatches") ? r.Matches(lines[i]).Cast<Match>().Cast<object>().ToArray() : new object[] { r.Match(lines[i]) };
                }
                mo.Val("Matches", "System.Text.RegularExpressions.Match[]", matches);
                mo.Base = md;
                mo.ToStr = y => md.Text(null);
                c.Out(mo);
                if (c.Sw("List")) return;
            }
        }

        static PsObj Rule(string typeName, string rightsProp, string who, string rights, bool inherited, bool container)
        {
            var r = new PsObj(typeName, "System.Security.AccessControl.AccessRule", "System.Security.AccessControl.AuthorizationRule", "System.Object");
            r.Val(rightsProp, "string", rights);
            r.Val("AccessControlType", "System.Security.AccessControl.AccessControlType", "Allow");
            r.Val("IdentityReference", "System.Security.Principal.IdentityReference", who);
            r.Val("IsInherited", "bool", inherited);
            r.Val("InheritanceFlags", "System.Security.AccessControl.InheritanceFlags", container ? "ContainerInherit, ObjectInherit" : "None");
            r.Val("PropagationFlags", "System.Security.AccessControl.PropagationFlags", "None");
            r.ToStr = y => typeName;
            return r;
        }
        static PsObj AclObj(PsRunspace R, PPath p, string typeName, string owner, List<PsObj> rules, string rightsProp, string sddl)
        {
            var o = new PsObj(typeName, "System.Security.AccessControl.NativeObjectSecurity", "System.Security.AccessControl.CommonObjectSecurity", "System.Security.AccessControl.ObjectSecurity", "System.Object");
            PsProviders.AddNotes(o, p.P, p.Drive, p.Path, p.P.ParentOf(p.Path), p.P.LeafOf(p.Path), false);
            o.Remove("PSIsContainer");
            string access = string.Join("\n", rules.Select(r => PsConv.ToStr(r.Get("IdentityReference")) + " Allow  " + PsConv.ToStr(r.Get(rightsProp))).ToArray());
            o.Val("AccessToString", "string", access);
            o.Val("AuditToString", "string", "");
            o.Val("Path", "string", p.P.Qualified + "::" + p.Path);
            o.Val("Owner", "string", owner);
            o.Val("Group", "string", "NT AUTHORITY\\SYSTEM");
            o.Val("Access", "System.Security.AccessControl.AuthorizationRuleCollection", rules.Cast<object>().ToArray());
            o.Val("Sddl", "string", sddl);
            o.Val("AccessRightType", "type", typeName == "System.Security.AccessControl.RegistrySecurity" ? "System.Security.AccessControl.RegistryRights" : "System.Security.AccessControl.FileSystemRights");
            o.Val("AreAccessRulesProtected", "bool", false);
            o.Val("AreAuditRulesProtected", "bool", false);
            o.ToStr = y => typeName;
            return o;
        }
        static PsObj FileAcl(PsRunspace R, PPath p, WinFile f)
        {
            var ws = WsFileAcl(R, p, f);   // the AZ-800 servers keep real NTFS entries (PsWsAcl.cs)
            if (ws != null) return ws;
            bool dir = f.IsDir;
            string t = "System.Security.AccessControl.FileSystemAccessRule";
            var rules = new List<PsObj> { Rule(t, "FileSystemRights", "NT AUTHORITY\\SYSTEM", "FullControl", f.Parent != null, dir), Rule(t, "FileSystemRights", "BUILTIN\\Administrators", "FullControl", f.Parent != null, dir), Rule(t, "FileSystemRights", "BUILTIN\\Users", "ReadAndExecute, Synchronize", f.Parent != null, dir) };
            string fl = dir ? "OICIID" : "ID";
            string owner = f.Owner ?? "BUILTIN\\Administrators";
            string sddl = "O:" + (owner == "BUILTIN\\Administrators" ? "BA" : owner == "NT AUTHORITY\\SYSTEM" ? "SY" : "S-1-5-21-3623811015-3361044348-30300820-500") + "G:SYD:AI(A;" + fl + ";FA;;;SY)(A;" + fl + ";FA;;;BA)(A;" + fl + ";0x1200a9;;;BU)";
            return AclObj(R, p, "System.Security.AccessControl.FileSecurity", owner, rules, "FileSystemRights", sddl);
        }
        static PsObj KeyAcl(PsRunspace R, PPath p, RegKey k)
        {
            string t = "System.Security.AccessControl.RegistryAccessRule";
            var rules = new List<PsObj> { Rule(t, "RegistryRights", "BUILTIN\\Users", "ReadKey", true, true), Rule(t, "RegistryRights", "BUILTIN\\Administrators", "FullControl", true, true), Rule(t, "RegistryRights", "NT AUTHORITY\\SYSTEM", "FullControl", true, true), Rule(t, "RegistryRights", "CREATOR OWNER", "FullControl", true, true) };
            return AclObj(R, p, "System.Security.AccessControl.RegistrySecurity", "BUILTIN\\Administrators", rules, "RegistryRights", "O:BAG:SYD:AI(A;CIID;KR;;;BU)(A;CIID;KA;;;BA)(A;CIID;KA;;;SY)(A;CIIOID;KA;;;CO)");
        }
    }
}
