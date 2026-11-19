// Cert Lab - the location, drive, provider and path cmdlets of the simulated Windows PowerShell 5.1 (Get-Location,
// Set-Location, Push-Location, Pop-Location, Get-PSDrive, New-PSDrive, Remove-PSDrive, Get-PSProvider, Test-Path,
// Resolve-Path, Convert-Path, Split-Path, Join-Path), Get-ChildItem and Get-Item, and the format views of the
// providers' objects (registry keys with their "Hive:" header, certificates, providers). Changes to items, their
// properties and their content are in PsCmdContent.cs.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CertLab
{
    public partial class PsCmdlets
    {
        const string MgmtMod = "Microsoft.PowerShell.Management";

        internal static void PErr(CmdCtx c, ProvError e) { c.Error(e.Message, e.Id, e.Cat, e.Target, e.ExType); }
        internal static ProvCtx X(CmdCtx c) { return c.R.Ctx(c.Scope, c.Sw("Force")); }
        internal static bool IsLiteral(CmdCtx c) { return c.Has("LiteralPath"); }
        internal static string[] Typed(CmdCtx c, string def)
        {
            var l = c.Strs("LiteralPath");
            if (l != null) return l;
            var p = c.Strs("Path");
            if (p != null) return p;
            return def == null ? new string[0] : new[] { def };
        }
        /// <summary>A mandatory path given as $null or '' (Windows PowerShell refuses it while binding).</summary>
        internal static void CheckPathArg(CmdCtx c, string name)
        {
            if (!c.Has(name)) return;
            var v = c.Get(name);
            string target = null;
            if (v == null || PsConv.Items(v).Any(x => x == null)) { var r = c.MakeError("Cannot bind argument to parameter '" + name + "' because it is null.", "ParameterArgumentValidationErrorNullNotAllowed", "InvalidData", target, "System.Management.Automation.ParameterBindingValidationException"); throw new PsRuntimeException(r, false); }
            if (PsConv.Items(v).Any(x => PsConv.ToStr(x).Length == 0)) { var r = c.MakeError("Cannot bind argument to parameter '" + name + "' because it is an empty string.", "ParameterArgumentValidationErrorEmptyStringNotAllowed", "InvalidData", target, "System.Management.Automation.ParameterBindingValidationException"); throw new PsRuntimeException(r, false); }
        }
        /// <summary>Names filtered by -Include / -Exclude / -Filter.</summary>
        internal static bool NameOk(CmdCtx c, string name)
        {
            var inc = c.Strs("Include"); var exc = c.Strs("Exclude"); var f = c.Str("Filter");
            if (f != null && !PsConv.Like(name, f, false)) return false;
            if (inc != null && !inc.Any(i => PsConv.Like(name, i, false))) return false;
            if (exc != null && exc.Any(e => PsConv.Like(name, e, false))) return false;
            return true;
        }
        /// <summary>The items a -Path / -LiteralPath names (wildcards expanded); missing ones are reported.</summary>
        internal static List<PPath> Items(CmdCtx c, ProvCtx x, string def, bool contentStyle)
        {
            var res = new List<PPath>();
            bool lit = IsLiteral(c);
            foreach (var t in Typed(c, def))
            {
                PPath p;
                try { p = c.R.ParsePath(t); }
                catch (ProvError e) { PErr(c, e); continue; }
                if (!lit && p.HasWild)
                {
                    List<PPath> m;
                    try { m = c.R.Expand(p, x); }
                    catch (ProvError e) { PErr(c, e); continue; }
                    m = m.Where(q => NameOk(c, q.P.LeafOf(q.Path))).ToList();
                    if (m.Count == 0 && contentStyle) { string sh = p.P.ShownPath(p); c.Error("An object at the specified path " + sh + " does not exist, or has been filtered by the -Include or -Exclude parameter.", "ItemNotFound", "ObjectNotFound", sh, "System.Management.Automation.ItemNotFoundException"); }
                    res.AddRange(m);
                    continue;
                }
                if (!lit) p = Unescaped(p);
                object node;
                try { node = p.P.Find(x, p.Path); }
                catch (ProvError e) { PErr(c, e); continue; }
                if (node == null) { PErr(c, ProvError.NotFound(p.P.ShownPath(p))); continue; }
                res.Add(p);
            }
            return res;
        }
        internal static PPath Unescaped(PPath p)
        {
            if (!p.Segs.Any(s => s.Contains("`"))) return p;
            var c = new PPath { P = p.P, Drive = p.Drive, Root = p.Root, Typed = p.Typed };
            foreach (var s in p.Segs) c.Segs.Add(Regex.Replace(s, "`(.)", "$1"));
            return c;
        }
        static object G(object o, string p) { var po = PsConv.Base(o) as PsObj; return po == null ? null : po.Get(p); }
        static string StripProv(string p) { int i = p.IndexOf("::", StringComparison.Ordinal); return i >= 0 ? p.Substring(i + 2) : p; }

        static partial void RegisterProviders()
        {
            PsEnums.Define("System.Management.Automation.Provider.ProviderCapabilities", "None=0,Include=1,Exclude=2,Filter=4,ExpandWildcards=8,ShouldProcess=16,Credentials=32,Transactions=64", true);
            PsEnums.Define("Microsoft.PowerShell.Commands.FileSystemCmdletProviderEncoding", "Unknown=0,String=1,Unicode=2,Byte=3,BigEndianUnicode=4,UTF8=5,UTF7=6,UTF32=7,Ascii=8,Default=9,Oem=10,BigEndianUTF32=11");
            AddType("FileSystemCmdletProviderEncoding", "Microsoft.PowerShell.Commands.FileSystemCmdletProviderEncoding");
            RegisterProviderViews();
            RegisterLocationCmdlets();
            RegisterItemCmdlets();
            RegisterContentCmdlets();
        }

        static void RegisterProviderViews()
        {
            PsFormat.View("Microsoft.Win32.RegistryKey", new FmtView
            {
                Cols = new List<FmtCol> { PsFormat.Col("Name", 30, false, o => G(o, "PSChildName")), new FmtCol { Header = "Property", MultiLine = true, Get = o => { var po = PsConv.Base(o) as PsObj; var k = po == null ? null : po.Base as RegKey; return k == null ? "" : PsProviders.KeyPropertyText(PsProviders.OwnerOf(po), k); } } },
                GroupKey = o => PsConv.ToStr(G(o, "PSParentPath")),
                GroupHeader = o => "    Hive: " + StripProv(PsConv.ToStr(G(o, "PSParentPath"))),
                GroupGap = 2
            });
            PsFormat.View("System.Security.Cryptography.X509Certificates.X509Certificate2", new FmtView
            {
                Cols = new List<FmtCol> { PsFormat.P("Thumbprint", 41), PsFormat.P("Subject", 0) },
                GroupKey = o => PsConv.ToStr(G(o, "PSParentPath")),
                GroupHeader = o => "   PSParentPath: " + PsConv.ToStr(G(o, "PSParentPath")),
                GroupGap = 1
            });
            PsFormat.ListProps("System.Security.Cryptography.X509Certificates.X509Certificate2", "Subject", "Issuer", "Thumbprint", "FriendlyName", "NotBefore", "NotAfter", "Extensions");
            PsFormat.View("Microsoft.PowerShell.Commands.X509StoreLocation", new FmtView { Kind = "list", Cols = new List<FmtCol> { PsFormat.P("Location", 0), PsFormat.P("StoreNames", 0) } });
            PsFormat.View("System.Security.Cryptography.X509Certificates.X509Store", new FmtView { Kind = "list", Cols = new List<FmtCol> { PsFormat.P("Name", 0) } });
            PsFormat.Table("System.Management.Automation.ProviderInfo", PsFormat.P("Name", 20), PsFormat.P("Capabilities", 49), PsFormat.P("Drives", 0));
            PsFormat.Table("Microsoft.Powershell.Utility.FileHash", PsFormat.P("Algorithm", 15), PsFormat.P("Hash", 70), PsFormat.P("Path", 0));
            PsFormat.View("Microsoft.PowerShell.Commands.MatchInfo", new FmtView { Kind = "custom", Custom = o => { var po = PsConv.Base(o) as PsObj; var m = po == null ? null : po.Base as MatchData; return m == null ? PsConv.ToStr(o) : m.Display(); } });
            Func<object, object> aclPath = o => { var p = StripProv(PsConv.ToStr(G(o, "Path"))); int i = p.TrimEnd('\\').LastIndexOf('\\'); return i < 0 || p.Length <= 3 ? p : p.Substring(i + 1); };
            Func<object, object> aclAccess = o => { var s = PsConv.ToStr(G(o, "AccessToString")).Replace("\r", ""); int i = s.IndexOf('\n'); return i < 0 ? s : s.Substring(0, i) + "..."; };
            PsFormat.View("System.Security.AccessControl.FileSecurity", new FmtView
            {
                Auto = true,
                Cols = new List<FmtCol> { PsFormat.Col("Path", 0, false, aclPath), PsFormat.P("Owner", 0), PsFormat.Col("Access", 0, false, aclAccess) },
                GroupKey = o => PsConv.ToStr(G(o, "PSParentPath")),
                GroupHeader = o => "    Directory: " + StripProv(PsConv.ToStr(G(o, "PSParentPath"))),
                GroupGap = 2
            });
            PsFormat.View("System.Security.AccessControl.RegistrySecurity", new FmtView { Auto = true, Cols = new List<FmtCol> { PsFormat.Col("Path", 0, false, aclPath), PsFormat.P("Owner", 0), PsFormat.Col("Access", 0, false, aclAccess) } });
        }

        // ================================================================ locations, drives, providers, paths
        static void RegisterLocationCmdlets()
        {
            Add("Get-Location", MgmtMod, "PSProvider:string[] ps=Location; PSDrive:string[] ps=Location; Stack:switch ps=Stack; StackName:string[] ps=Stack", (c, o) =>
            {
                var R = c.R;
                if (c.Sw("Stack") || c.Has("StackName"))
                {
                    foreach (var name in c.Strs("StackName") ?? new[] { R.DefaultStackName })
                    {
                        var st = StackOf(R, name, false);
                        if (st == null) { c.Error("The location stack '" + name + "' does not exist.", "StackNotFound", "ObjectNotFound", name, "System.Management.Automation.PSArgumentException"); continue; }
                        foreach (var s in st) { try { c.Out(R.PathInfo(R.ParsePath(s))); } catch (ProvError) { } }
                    }
                    return;
                }
                if (c.Has("PSDrive"))
                {
                    foreach (var dn in c.Strs("PSDrive"))
                    {
                        var d = R.FindDrive(dn);
                        if (d == null) { c.Error("Cannot find drive. A drive with the name '" + dn + "' does not exist.", "GetLocationNoMatchingDrive", "ObjectNotFound", dn, "System.Management.Automation.DriveNotFoundException"); continue; }
                        try { c.Out(R.PathInfo(R.ParsePath(d.Name + ":"))); } catch (ProvError e) { PErr(c, e); }
                    }
                    return;
                }
                if (c.Has("PSProvider"))
                {
                    foreach (var pn in c.Strs("PSProvider"))
                    {
                        var p = PsProviders.Get(pn);
                        if (p == null) { c.Error("Cannot find a provider with the name '" + pn + "'.", "ProviderNotFound", "ObjectNotFound", pn, "System.Management.Automation.ProviderNotFoundException"); continue; }
                        var d = R.CurrentDrive.Provider == p.Name ? R.CurrentDrive : R.Drives.FirstOrDefault(x => x.Provider == p.Name);
                        if (d != null) c.Out(R.PathInfo(R.ParsePath(d.Name + ":")));
                    }
                    return;
                }
                c.Out(R.PathInfo(R.CurrentPPath));
            }).DefaultSet = "Location";

            Action<CmdCtx, object> setLoc = (c, o) =>
            {
                var R = c.R;
                if (c.Has("StackName") && !c.Has("Path") && !c.Has("LiteralPath") && c.Def.Name == "Set-Location") { R.DefaultStackName = c.Str("StackName"); if (c.Sw("PassThru")) c.Out(R.PathInfo(R.CurrentPPath)); return; }
                string t = c.Str("LiteralPath") ?? c.Str("Path");
                if (t == null) { if (c.Def.Name == "Push-Location") { StackOf(R, c.Str("StackName") ?? R.DefaultStackName, true).Push(R.CurrentPPath.DrivePath); if (c.Sw("PassThru")) c.Out(R.PathInfo(R.CurrentPPath)); } return; }
                var x = X(c);
                PPath p;
                try { p = R.ParsePath(t); }
                catch (ProvError e) { PErr(c, e); return; }
                if (!IsLiteral(c) && p.HasWild)
                {
                    var m = R.Expand(p, x).Where(q => q.P.IsContainer(q.P.Find(x, q.Path))).ToList();
                    if (m.Count == 0) { PErr(c, ProvError.NotFound(t)); return; }
                    if (m.Count > 1) { c.Error("Cannot set the location because path '" + t + "' resolved to multiple containers. You can only the set the same location to one container at a time.", "Argument", "InvalidArgument", t, "System.Management.Automation.PSArgumentException"); return; }
                    p = m[0];
                }
                else if (!IsLiteral(c)) p = Unescaped(p);
                object node;
                try { node = p.P.Find(x, p.Path); }
                catch (ProvError e) { PErr(c, e); return; }
                if (node == null || !p.P.IsContainer(node)) { PErr(c, ProvError.NotFound(p.P.ShownPath(p))); return; }
                if (c.Def.Name == "Push-Location") StackOf(R, c.Str("StackName") ?? R.DefaultStackName, true).Push(R.CurrentPPath.DrivePath);
                try { R.GoTo(p); }
                catch (ProvError e) { PErr(c, e); return; }
                if (c.Sw("PassThru")) c.Out(R.PathInfo(R.CurrentPPath));
            };
            Add("Set-Location", MgmtMod, "Path:string@0 pipe prop ps=Path; LiteralPath:string prop alias=PSPath ps=LiteralPath; PassThru:switch; StackName:string ps=Stack", setLoc).DefaultSet = "Path";
            Add("Push-Location", MgmtMod, "Path:string@0 pipe prop ps=Path; LiteralPath:string prop alias=PSPath ps=LiteralPath; PassThru:switch; StackName:string", setLoc).DefaultSet = "Path";
            Add("Pop-Location", MgmtMod, "PassThru:switch; StackName:string", (c, o) =>
            {
                var R = c.R;
                string name = c.Str("StackName") ?? R.DefaultStackName;
                var st = StackOf(R, name, false);
                if (st == null) { c.Error("The location stack '" + name + "' does not exist.", "StackNotFound", "ObjectNotFound", name, "System.Management.Automation.PSArgumentException"); return; }
                if (st.Count == 0) return;
                var s = st.Pop();
                try { R.GoTo(R.ParsePath(s)); }
                catch (ProvError e) { PErr(c, e); return; }
                if (c.Sw("PassThru")) c.Out(R.PathInfo(R.CurrentPPath));
            });

            Add("Get-PSDrive", MgmtMod, "Name:string[]@0 ps=Name; LiteralName:string[] ps=LiteralName; PSProvider:string[]; Scope:string", (c, o) =>
            {
                var R = c.R;
                var provs = c.Strs("PSProvider");
                var all = R.Drives.Where(d => provs == null || provs.Any(p => PsConv.Like(d.Provider, p, false))).OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();
                var names = c.Strs("LiteralName") ?? c.Strs("Name");
                if (names == null) { foreach (var d in all) c.Out(PsProviders.DriveObj(R, d)); return; }
                foreach (var n in names)
                {
                    bool wild = !c.Has("LiteralName") && PsConv.HasWildcard(n);
                    var hits = all.Where(d => wild ? PsConv.Like(d.Name, n, false) : d.Name.Equals(n, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (hits.Count == 0 && !wild) { c.Error("Cannot find drive. A drive with the name '" + n + "' does not exist.", "GetLocationNoMatchingDrive", "ObjectNotFound", n, "System.Management.Automation.DriveNotFoundException"); continue; }
                    foreach (var d in hits) c.Out(PsProviders.DriveObj(R, d));
                }
            }).DefaultSet = "Name";

            var nd = Add("New-PSDrive", MgmtMod, "Name:string@0 ! prop; PSProvider:string@1 ! prop; Root:string@2 ! prop; Description:string prop; Scope:string prop; Persist:switch prop; Credential:pscredential prop", (c, o) =>
            {
                var R = c.R;
                string name = c.Str("Name");
                if (R.FindDrive(name) != null) { c.Error("A drive with the name '" + name + "' already exists.", "DriveAlreadyExists", "ResourceExists", name, "System.Management.Automation.SessionStateException"); return; }
                var p = PsProviders.Get(c.Str("PSProvider"));
                if (p == null) { c.Error("Cannot find a provider with the name '" + c.Str("PSProvider") + "'.", "ProviderNotFound", "ObjectNotFound", c.Str("PSProvider"), "System.Management.Automation.ProviderNotFoundException"); return; }
                string root = c.Str("Root") ?? "";
                var x = X(c);
                string rootPath = root;
                if (p is FsProvider || p is RegProvider)
                {
                    PPath rp = null;
                    try
                    {
                        if (p is RegProvider && RegProvider.HiveName(root.Split('\\')[0]) != null && !root.Contains(":")) { rp = new PPath { P = p }; p.SplitProviderPath(root, rp); }
                        else rp = R.ParsePath(root);
                    }
                    catch (ProvError) { rp = null; }
                    object node = null;
                    if (rp != null && rp.P == p) { try { node = p.Find(x, rp.Path); } catch (ProvError) { } }
                    if (node == null || !p.IsContainer(node))
                    {
                        var dd = new PsDrive { Name = name, Provider = p.Name, Root = root };
                        c.Error(p is FsProvider ? "The specified drive root \"" + root + "\" either does not exist, or it is not a folder." : "The registry key at the specified path does not exist.", "DriveRootError", "ReadError", PsProviders.DriveObj(R, dd), "System.IO.IOException");
                        return;
                    }
                    rootPath = rp.Path;
                }
                else if (p is CertProvider) rootPath = "\\";
                else rootPath = "";
                var d = new PsDrive { Name = name, Provider = p.Name, Root = rootPath, Description = c.Str("Description") ?? "" };
                if (!c.ShouldProcess("Name: " + name + " Provider: " + p.Qualified + " Root: " + rootPath, "New drive")) return;
                R.Drives.Add(d);
                c.Out(PsProviders.DriveObj(R, d));
            });
            nd.ShouldProcess = true; nd.ConfirmImpact = "Low";

            var rd = Add("Remove-PSDrive", MgmtMod, "Name:string[]@0 ! ps=Name; LiteralName:string[] ! ps=LiteralName; PSProvider:string[]; Scope:string; Force:switch", (c, o) =>
            {
                var R = c.R;
                foreach (var n in c.Strs("LiteralName") ?? c.Strs("Name"))
                {
                    bool wild = !c.Has("LiteralName") && PsConv.HasWildcard(n);
                    var hits = R.Drives.Where(d => wild ? PsConv.Like(d.Name, n, false) : d.Name.Equals(n, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (hits.Count == 0 && !wild) { c.Error("Cannot find drive. A drive with the name '" + n + "' does not exist.", "DriveNotFound", "ObjectNotFound", n, "System.Management.Automation.DriveNotFoundException"); continue; }
                    foreach (var d in hits)
                    {
                        if (d == R.CurrentDrive && !c.Sw("Force")) { c.Error("Cannot remove drive '" + d.Name + "' because it is in use.", "InvalidOperation", "InvalidOperation", d.Name, "System.Management.Automation.PSInvalidOperationException"); continue; }
                        var p = PsProviders.Get(d.Provider);
                        if (!c.ShouldProcess("Name: " + d.Name + " Provider: " + (p == null ? d.Provider : p.Qualified) + " Root: " + d.Root, "Remove Drive")) continue;
                        R.Drives.Remove(d);
                        if (d == R.CurrentDrive) R.GoTo(R.ParsePath("C:"));
                    }
                }
            });
            rd.ShouldProcess = true; rd.ConfirmImpact = "Low"; rd.DefaultSet = "Name";

            Add("Get-PSProvider", MgmtMod, "PSProvider:string[]@0", (c, o) =>
            {
                var R = c.R;
                var names = c.Strs("PSProvider");
                foreach (var p in PsProviders.All)
                {
                    if (names != null && !names.Any(n => PsConv.Like(p.Name, n, false) || PsConv.Like(p.Qualified, n, false))) continue;
                    var po = PsProviders.ProviderObj(p);
                    var pp = p;
                    po.Prop("Drives", "System.Collections.ObjectModel.Collection[System.Management.Automation.PSDriveInfo]", () => R.Drives.Where(d => d.Provider.Equals(pp.Name, StringComparison.OrdinalIgnoreCase)).Select(d => (object)PsProviders.DriveObj(R, d)).ToArray());
                    c.Out(po);
                }
                if (names != null) foreach (var n in names) if (!PsConv.HasWildcard(n) && PsProviders.Get(n) == null) c.Error("Cannot find a provider with the name '" + n + "'.", "ProviderNotFound", "ObjectNotFound", n, "System.Management.Automation.ProviderNotFoundException");
            });

            Add("Test-Path", MgmtMod, "Path:string[]@0 ! pipe prop ps=Path; LiteralPath:string[] ! prop alias=PSPath ps=LiteralPath; Filter:string; Include:string[]; Exclude:string[]; PathType:string alias=Type set=Any|Container|Leaf; IsValid:switch; Credential:pscredential; OlderThan:datetime; NewerThan:datetime", (c, o) =>
            {
                CheckPathArg(c, IsLiteral(c) ? "LiteralPath" : "Path");
                var R = c.R; var x = X(c);
                string type = c.Str("PathType") ?? "Any";
                foreach (var t in Typed(c, null))
                {
                    PPath p;
                    try { p = R.ParsePath(t); }
                    catch (ProvError) { c.Out(false); continue; }
                    if (c.Sw("IsValid"))
                    {
                        bool ok = p.P is FsProvider ? p.Segs.All(s => s.IndexOfAny(new[] { '<', '>', '|', '"', ':' }) < 0) : true;
                        c.Out(ok);
                        continue;
                    }
                    List<PPath> hits;
                    try
                    {
                        if (!IsLiteral(c) && p.HasWild) hits = R.Expand(p, x);
                        else { var q = IsLiteral(c) ? p : Unescaped(p); hits = q.P.Find(x, q.Path) != null ? new List<PPath> { q } : new List<PPath>(); }
                    }
                    catch (ProvError) { c.Out(false); continue; }
                    bool any = hits.Any(h =>
                    {
                        var n = h.P.Find(x, h.Path);
                        bool cont = h.P.IsContainer(n);
                        if (type.Equals("Leaf", StringComparison.OrdinalIgnoreCase) && cont) return false;
                        if (type.Equals("Container", StringComparison.OrdinalIgnoreCase) && !cont) return false;
                        if (!NameOk(c, h.P.LeafOf(h.Path)) && (c.Has("Include") || c.Has("Exclude") || c.Has("Filter"))) return false;
                        var f = n as WinFile;
                        if (f != null && c.Has("OlderThan") && !(f.Modified < (DateTime)c.Get("OlderThan"))) return false;
                        if (f != null && c.Has("NewerThan") && !(f.Modified > (DateTime)c.Get("NewerThan"))) return false;
                        return true;
                    });
                    c.Out(any);
                }
            }).DefaultSet = "Path";

            Add("Resolve-Path", MgmtMod, "Path:string[]@0 ! pipe prop ps=Path; LiteralPath:string[] ! prop alias=PSPath ps=LiteralPath; Relative:switch; Credential:pscredential", (c, o) =>
            {
                var R = c.R; var x = X(c); x.Force = true;
                foreach (var t in Typed(c, null))
                {
                    PPath p;
                    try { p = R.ParsePath(t); }
                    catch (ProvError e) { PErr(c, e); continue; }
                    List<PPath> hits;
                    if (!IsLiteral(c) && p.HasWild) hits = R.Expand(p, x);
                    else { var q = IsLiteral(c) ? p : Unescaped(p); hits = q.P.Find(x, q.Path) != null ? new List<PPath> { q } : new List<PPath>(); }
                    if (hits.Count == 0) { PErr(c, ProvError.NotFound(p.HasWild ? t : p.P.ShownPath(p))); continue; }
                    foreach (var h in hits)
                    {
                        if (c.Sw("Relative")) c.Out(RelativePath(R, h));
                        else c.Out(R.PathInfo(h));
                    }
                }
            }).DefaultSet = "Path";

            Add("Convert-Path", MgmtMod, "Path:string[]@0 ! pipe prop ps=Path; LiteralPath:string[] ! prop alias=PSPath,LP ps=LiteralPath", (c, o) =>
            {
                var x = X(c); x.Force = true;
                foreach (var p in Items(c, x, null, false)) c.Out(p.Path);
            }).DefaultSet = "Path";

            Add("Split-Path", MgmtMod, "Path:string[]@0 ! pipe prop; LiteralPath:string[] prop alias=PSPath; Qualifier:switch ps=QualifierSet; NoQualifier:switch ps=NoQualifierSet; Parent:switch ps=ParentSet; Leaf:switch ps=LeafSet; Resolve:switch; IsAbsolute:switch ps=IsAbsoluteSet; Credential:pscredential", (c, o) =>
            {
                var R = c.R; var x = X(c); x.Force = true;
                var list = new List<string>();
                if (c.Sw("Resolve")) { foreach (var p in Items(c, x, null, false)) list.Add(p.DrivePath); }
                else list.AddRange(Typed(c, null));
                foreach (var t in list)
                {
                    var qm = Regex.Match(t, "^((?:[\\w.]+\\\\)?\\w+::|[^\\\\/:]+:)(.*)$");
                    if (c.Sw("Qualifier"))
                    {
                        if (!qm.Success) { c.Error("Cannot parse path because path '" + t + "' does not have a qualifier specified.", "ParsePathFormatError", "InvalidArgument", t, "System.Management.Automation.PSArgumentException"); continue; }
                        c.Out(qm.Groups[1].Value);
                        continue;
                    }
                    if (c.Sw("NoQualifier")) { c.Out(qm.Success ? qm.Groups[2].Value : t); continue; }
                    if (c.Sw("IsAbsolute")) { c.Out(qm.Success || t.StartsWith("\\", StringComparison.Ordinal) || t.StartsWith("/", StringComparison.Ordinal)); continue; }
                    string q = qm.Success ? qm.Groups[1].Value : "", rest = qm.Success ? qm.Groups[2].Value : t;
                    string trimmed = rest.TrimEnd('\\', '/');
                    int i = trimmed.LastIndexOfAny(new[] { '\\', '/' });
                    if (c.Sw("Leaf")) { c.Out(i < 0 ? trimmed : trimmed.Substring(i + 1)); continue; }
                    // the parent (the default)
                    if (trimmed.Length == 0) { c.Out(""); continue; }
                    if (i < 0) { c.Out(q.Length > 0 && rest.StartsWith("\\", StringComparison.Ordinal) ? q + "\\" : q); continue; }
                    if (i == 0) { c.Out(q + rest.Substring(0, 1)); continue; }
                    c.Out(q + trimmed.Substring(0, i));
                }
            });

            Add("Join-Path", MgmtMod, "Path:string[]@0 ! pipe prop alias=PSPath; ChildPath:string@1 ! prop; Resolve:switch; Credential:pscredential", (c, o) =>
            {
                var R = c.R; var x = X(c); x.Force = true;
                string child = c.Str("ChildPath") ?? "";
                foreach (var t in c.Strs("Path"))
                {
                    var dm = Regex.Match(t, "^([^\\\\/:*?\"<>|]+):");
                    if (dm.Success && !t.Contains("::") && R.FindDrive(dm.Groups[1].Value) == null) { PErr(c, ProvError.NoDrive(dm.Groups[1].Value)); continue; }
                    string j;
                    if (child.Length == 0) j = t;
                    else if (t.Length == 0) j = child;
                    else if (t.EndsWith("\\", StringComparison.Ordinal) || t.EndsWith("/", StringComparison.Ordinal)) j = t + child.TrimStart('\\', '/');
                    else j = t + "\\" + child.TrimStart('\\', '/');
                    if (!c.Sw("Resolve")) { c.Out(j); continue; }
                    PPath p;
                    try { p = R.ParsePath(j); }
                    catch (ProvError e) { PErr(c, e); continue; }
                    var hits = p.HasWild ? R.Expand(p, x) : (p.P.Find(x, Unescaped(p).Path) != null ? new List<PPath> { Unescaped(p) } : new List<PPath>());
                    if (hits.Count == 0) { PErr(c, ProvError.NotFound(p.HasWild ? j : p.P.ShownPath(p))); continue; }
                    foreach (var h in hits) c.Out(h.DrivePath);
                }
            });
        }
        static Stack<string> StackOf(PsRunspace R, string name, bool create)
        {
            if (name.Equals("default", StringComparison.OrdinalIgnoreCase)) return R.LocationStack;
            Stack<string> s;
            if (R.NamedStacks.TryGetValue(name, out s)) return s;
            if (!create) return null;
            s = new Stack<string>(); R.NamedStacks[name] = s;
            return s;
        }
        static string RelativePath(PsRunspace R, PPath h)
        {
            var cur = R.CurrentPPath;
            if (cur.Drive != h.Drive || cur.P != h.P) return h.DrivePath;
            int common = 0;
            while (common < cur.Segs.Count && common < h.Segs.Count && cur.Segs[common].Equals(h.Segs[common], StringComparison.OrdinalIgnoreCase)) common++;
            var parts = new List<string>();
            int up = cur.Segs.Count - common;
            if (up == 0) parts.Add(".");
            for (int i = 0; i < up; i++) parts.Add("..");
            parts.AddRange(h.Segs.Skip(common));
            return string.Join("\\", parts.ToArray());
        }

        // ================================================================ Get-ChildItem, Get-Item
        static void RegisterItemCmdlets()
        {
            Add("Get-ChildItem", MgmtMod, "Path:string[]@0 pipe prop ps=Items; LiteralPath:string[] ! prop alias=PSPath ps=LiteralItems; Filter:string@1; Include:string[]; Exclude:string[]; Recurse:switch alias=s; Depth:uint32; Force:switch; Name:switch; Attributes:string; Directory:switch alias=ad,d; File:switch alias=af; Hidden:switch alias=ah,h; CodeSigningCert:switch", (c, o) =>
            {
                var R = c.R;
                var x = X(c);
                if (c.Sw("Hidden")) x.Force = true;
                bool recurse = c.Sw("Recurse") || c.Has("Depth");
                int depth = c.Has("Depth") ? (int)Math.Min(int.MaxValue, PsConv.ToLong(c.Get("Depth"))) : (recurse ? int.MaxValue : 0);
                if (!recurse) depth = 0;
                bool lit = IsLiteral(c);
                foreach (var t in Typed(c, "."))
                {
                    PPath p;
                    try { p = R.ParsePath(t); }
                    catch (ProvError e) { PErr(c, e); continue; }
                    if (c.Has("Filter") && !(p.P is FsProvider)) { c.Error("Cannot call method. The provider does not support the use of filters.", "NotSupported", "NotImplemented", null, "System.Management.Automation.PSNotSupportedException"); return; }
                    try
                    {
                        if (!lit && p.HasWild)
                        {
                            bool leafOnly = PsConv.HasWildcard(p.Leaf) && !p.Segs.Take(p.Segs.Count - 1).Any(PsConv.HasWildcard);
                            if (recurse && leafOnly)
                            {
                                // dir C:\x\*.log -Recurse: the last part becomes a name filter for the whole tree
                                var basep = p.Parent();
                                var bn = basep.P.Find(x, basep.Path);
                                if (bn == null || !basep.P.IsContainer(bn)) continue;
                                string pat = p.Leaf;
                                Walk(c, x, basep, bn, depth, basep, name => PsConv.Like(name, pat, false));
                                continue;
                            }
                            foreach (var m in R.Expand(p, x))
                            {
                                var mn = m.P.Find(x, m.Path);
                                if (Show(c, x, m.P, m.P.LeafOf(m.Path), mn, null)) Emit(c, x, m, mn, m.Parent() ?? m, false);
                                if (recurse && m.P.IsContainer(mn)) Walk(c, x, m, mn, depth - 1 < 0 ? 0 : depth - 1, m.Parent() ?? m, null);
                            }
                            continue;
                        }
                        if (!lit) p = Unescaped(p);
                        var node = p.P.Find(x, p.Path);
                        if (node == null) { PErr(c, ProvError.NotFound(p.P.ShownPath(p))); continue; }
                        if (!p.P.IsContainer(node)) { if (Show(c, x, p.P, p.P.LeafOf(p.Path), node, null)) Emit(c, x, p, node, p.Parent() ?? p, false); continue; }
                        if (c.Has("Include") && !recurse) continue;    // Windows PowerShell 5.1: -Include needs -Recurse or a wildcard in the path
                        Walk(c, x, p, node, depth, p, null);
                    }
                    catch (ProvError e) { PErr(c, e); }
                }
            }).DefaultSet = "Items";

            Add("Get-Item", MgmtMod, "Path:string[]@0 ! pipe prop ps=Path; LiteralPath:string[] ! prop alias=PSPath ps=LiteralPath; Filter:string; Include:string[]; Exclude:string[]; Force:switch; Stream:string[]; Credential:pscredential", (c, o) =>
            {
                CheckPathArg(c, IsLiteral(c) ? "LiteralPath" : "Path");
                var x = X(c);
                foreach (var p in Items(c, x, null, false))
                {
                    object node;
                    try { node = p.P.Find(x, p.Path); } catch (ProvError e) { PErr(c, e); continue; }
                    if (c.Has("Stream"))
                    {
                        var f = node as WinFile;
                        if (f == null || f.IsDir) { c.Error("Cert Lab: only files have alternate data streams.", "NotSupported", "NotImplemented", p.Path, "System.Management.Automation.PSNotSupportedException"); continue; }
                        foreach (var sn in c.Strs("Stream"))
                        {
                            bool wild = PsConv.HasWildcard(sn);
                            bool any = false;
                            if (PsConv.Like(":$DATA", sn, false) || sn.Equals(":$DATA", StringComparison.OrdinalIgnoreCase) || sn.Length == 0) { c.Out(StreamObj(c.R, f, ":$DATA", f.Length)); any = true; }
                            foreach (var s in f.Streams.Where(s => wild ? PsConv.Like(s.Key, sn, false) : s.Key.Equals(sn, StringComparison.OrdinalIgnoreCase))) { c.Out(StreamObj(c.R, f, s.Key, WinFile.ByteCount(s.Value, "ascii"))); any = true; }
                            if (!any && !wild) c.Error("Could not open the alternate data stream '" + sn + "' of the file '" + f.FullPath + "'.", "AlternateDataStreamNotFound", "ObjectNotFound", f.FullPath, "System.Management.Automation.ItemNotFoundException");
                        }
                        continue;
                    }
                    try { c.Out(p.P.Item(x, p.Path, node, p.Drive)); } catch (ProvError e) { PErr(c, e); }
                }
            }).DefaultSet = "Path";
        }
        static PsObj StreamObj(PsRunspace R, WinFile f, string stream, long len)
        {
            var o = new PsObj("Microsoft.PowerShell.Commands.AlternateStreamData", "System.Object");
            var fs = PsProviders.Get("FileSystem");
            string child = stream == ":$DATA" ? f.Name + "::$DATA" : f.Name + ":" + stream;
            PsProviders.AddNotes(o, fs, PsProviders.DriveFor(R, fs, f.FullPath), (stream == ":$DATA" ? f.FullPath + "::$DATA" : f.FullPath + ":" + stream), f.Parent.FullPath, child, false);
            o.Val("FileName", "string", f.FullPath);
            o.Val("Stream", "string", stream);
            o.Val("Length", "long", len);
            return o;
        }

        static bool Show(CmdCtx c, ProvCtx x, PsProvider p, string name, object node, Func<string, bool> extra)
        {
            bool cont = p.IsContainer(node);
            if (c.Sw("File") && cont) return false;
            if (c.Sw("Directory") && !cont) return false;
            if (c.Sw("Hidden") && !p.IsHidden(node)) return false;
            if (c.Sw("CodeSigningCert") && !(node is WinCert && ((WinCert)node).CodeSigning)) return false;
            if (extra != null && !extra(name)) return false;
            return NameOk(c, name);
        }
        static void Emit(CmdCtx c, ProvCtx x, PPath p, object node, PPath relBase, bool listing)
        {
            if (c.Sw("Name"))
            {
                var rel = p.Segs.Skip(relBase.Segs.Count).ToList();
                c.Out(rel.Count == 0 ? p.P.LeafOf(p.Path) : string.Join("\\", rel.ToArray()));
                return;
            }
            var item = p.P.Item(x, p.Path, node, p.Drive);
            if (listing && node is WinFile) item.FileNameOnly = true;
            c.Out(item);
        }
        /// <summary>A container's children (directories then files), then each subfolder's, as Windows PowerShell 5.1 lists them.</summary>
        static void Walk(CmdCtx c, ProvCtx x, PPath dir, object node, int depth, PPath relBase, Func<string, bool> extra)
        {
            var kids = dir.P.Kids(x, dir.Path, node).Where(k => x.Force || !dir.P.IsHidden(k.Value)).ToList();
            foreach (var k in kids)
                if (Show(c, x, dir.P, k.Key, k.Value, extra)) Emit(c, x, dir.Child(k.Key), k.Value, relBase, true);
            if (depth <= 0) return;
            foreach (var k in kids)
                if (dir.P.IsContainer(k.Value) && dir.P.Hierarchical) Walk(c, x, dir.Child(k.Key), k.Value, depth - 1, relBase, extra);
        }
    }
}
