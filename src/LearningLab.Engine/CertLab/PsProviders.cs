// Cert Lab - PowerShell providers and drives in the simulated Windows PowerShell 5.1: FileSystem (C:), Registry
// (HKLM:, HKCU:, and drives made with New-PSDrive such as HKCR:), Environment (Env:), Variable, Alias, Function and
// Certificate (Cert:). A typed path ("..\Reports", "HKLM:\SOFTWARE", "Registry::HKEY_CURRENT_USER", "~") is parsed
// into a provider, a drive and the provider's own path; wildcards are expanded against the provider's items; and
// every item comes out as an object with PSPath, PSParentPath, PSChildName, PSDrive, PSProvider and PSIsContainer
// so the item cmdlets can bind it from the pipeline. Everything works on the pretend machine's data in memory.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CertLab
{
    /// <summary>What a provider operation reports when it cannot do something (becomes the cmdlet's error).</summary>
    public class ProvError : Exception
    {
        public string Id, Cat, ExType; public object Target;
        public ProvError(string msg, string id, string cat, object target, string exType) : base(msg) { Id = id; Cat = cat; Target = target; ExType = exType; }
        public static ProvError NotFound(string shown) { return new ProvError("Cannot find path '" + shown + "' because it does not exist.", "PathNotFound", "ObjectNotFound", shown, "System.Management.Automation.ItemNotFoundException"); }
        public static ProvError NotSupported() { return new ProvError("Provider execution stopped because the provider does not support this operation.", "NotSupported", "NotImplemented", null, "System.Management.Automation.PSNotSupportedException"); }
        public static ProvError NoDrive(string name) { return new ProvError("Cannot find drive. A drive with the name '" + name + "' does not exist.", "DriveNotFound", "ObjectNotFound", name, "System.Management.Automation.DriveNotFoundException"); }
    }

    /// <summary>A PowerShell drive: C (FileSystem, C:\), HKLM (Registry, HKEY_LOCAL_MACHINE), Env, a New-PSDrive drive...</summary>
    public class PsDrive
    {
        public string Name = "", Provider = "", Root = "", Description = "";
        public List<string> Current = new List<string>();      // the drive's current location below its root
        public bool BuiltIn;
    }

    /// <summary>The context of a provider operation.</summary>
    public class ProvCtx
    {
        public PsRunspace R; public PsScope Scope; public bool Force;
        public PsMachine M { get { return R.M; } }
    }

    /// <summary>A parsed path: provider, drive, the root it hangs from and the parts below it.</summary>
    public class PPath
    {
        public PsProvider P; public PsDrive Drive; public string Root = ""; public List<string> Segs = new List<string>();
        public string Typed = "";
        public string Path { get { return P.Combine(Root, Segs); } }
        public PPath Child(string name) { var c = new PPath { P = P, Drive = Drive, Root = Root, Typed = Typed }; c.Segs.AddRange(Segs); c.Segs.Add(name); return c; }
        public PPath Parent() { if (Segs.Count == 0) return null; var c = new PPath { P = P, Drive = Drive, Root = Root, Typed = Typed }; c.Segs.AddRange(Segs.Take(Segs.Count - 1)); return c; }
        public bool HasWild { get { return Segs.Any(PsConv.HasWildcard); } }
        public string Leaf { get { return Segs.Count == 0 ? "" : Segs[Segs.Count - 1]; } }
        /// <summary>The path as PowerShell shows it: HKLM:\SOFTWARE, C:\LabFiles, Env:\Path (the drive's name, then the parts).</summary>
        public string DrivePath { get { return Drive == null ? P.Qualified + "::" + Path : Drive.Name + ":\\" + string.Join("\\", Segs.ToArray()); } }
    }

    /// <summary>A provider: how its paths look, how to find items and their children, and what it can change.</summary>
    public abstract class PsProvider
    {
        public string Name = "", Module = "Microsoft.PowerShell.Core", Caps = "ShouldProcess", Help = "";
        public string Qualified { get { return Module + "\\" + Name; } }
        public virtual bool Hierarchical { get { return true; } }
        public virtual string Combine(string root, IList<string> segs)
        {
            var l = new List<string>();
            if (root.Length > 0) l.Add(root.TrimEnd('\\'));
            l.AddRange(segs);
            return string.Join("\\", l.ToArray());
        }
        /// <summary>The provider path of the parent ("" when there is none).</summary>
        public virtual string ParentOf(string path) { int i = path.TrimEnd('\\').LastIndexOf('\\'); return i < 0 ? "" : path.Substring(0, i); }
        public virtual string LeafOf(string path) { var p = path.TrimEnd('\\'); int i = p.LastIndexOf('\\'); return i < 0 ? p : p.Substring(i + 1); }
        /// <summary>A provider-qualified path's rest ("HKEY_LOCAL_MACHINE\SOFTWARE") as a root and parts.</summary>
        public virtual void SplitProviderPath(string rest, PPath into) { into.Root = ""; into.Segs.AddRange(rest.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries)); }

        public abstract object Find(ProvCtx x, string path);
        public abstract bool IsContainer(object node);
        /// <summary>The children of a container, in the order the provider lists them.</summary>
        public abstract IEnumerable<KeyValuePair<string, object>> Kids(ProvCtx x, string path, object node);
        public virtual bool IsHidden(object node) { return false; }
        public abstract PsObj Item(ProvCtx x, string path, object node, PsDrive d);
        public virtual string ShownPath(PPath p) { return p.DrivePath; }

        public virtual PsObj NewItem(ProvCtx x, PPath p, string type, object value) { throw ProvError.NotSupported(); }
        public virtual void RemoveItem(ProvCtx x, string path, object node, bool recurse) { throw ProvError.NotSupported(); }
        public virtual void RenameItem(ProvCtx x, string path, object node, string newName) { throw ProvError.NotSupported(); }
        /// <summary>Copies (or moves) an item to a path that does not exist yet.</summary>
        public virtual void CopyItem(ProvCtx x, string path, object node, string dest, bool recurse, bool move) { throw ProvError.NotSupported(); }
        public virtual void SetItem(ProvCtx x, string path, object node, object value) { throw ProvError.NotSupported(); }
        public virtual void ClearItem(ProvCtx x, string path, object node) { throw ProvError.NotSupported(); }
        /// <summary>The content as lines (Get-Content), or null when the provider has none.</summary>
        public virtual List<string> ReadContent(ProvCtx x, string path, object node) { throw ProvError.NotSupported(); }
        public virtual void WriteContent(ProvCtx x, string path, object node, List<object> values, bool append) { throw ProvError.NotSupported(); }
        public virtual void ClearContent(ProvCtx x, string path, object node) { throw ProvError.NotSupported(); }
        public virtual bool ValidName(string name) { return name.Length > 0; }
    }

    // ================================================================ FileSystem
    public class FsProvider : PsProvider
    {
        public FsProvider() { Name = "FileSystem"; Caps = "Filter, ShouldProcess, Credentials"; }
        public override string Combine(string root, IList<string> segs)
        {
            string r = root.TrimEnd('\\');
            if (segs.Count == 0) return r.Length == 2 && r[1] == ':' ? r + "\\" : (r.Length == 0 ? "" : r);
            return r + "\\" + string.Join("\\", segs.ToArray());
        }
        public override string ParentOf(string path)
        {
            var p = path.TrimEnd('\\');
            int i = p.LastIndexOf('\\');
            if (i < 0) return "";
            if (i == 2 && p[1] == ':') return p.Substring(0, 3);
            return p.Substring(0, i);
        }
        public override string LeafOf(string path) { var p = path.TrimEnd('\\'); int i = p.LastIndexOf('\\'); return i < 0 ? p + (p.EndsWith(":", StringComparison.Ordinal) ? "\\" : "") : p.Substring(i + 1); }
        public override void SplitProviderPath(string rest, PPath into)
        {
            var parts = rest.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (parts.Count > 0 && parts[0].Length == 2 && parts[0][1] == ':') { into.Root = parts[0].ToUpperInvariant() + "\\"; parts.RemoveAt(0); }
            else into.Root = "C:\\";
            into.Segs.AddRange(parts);
        }
        public override object Find(ProvCtx x, string path) { return x.M.FindFile(path); }
        public override bool IsContainer(object node) { var f = node as WinFile; return f != null && f.IsDir; }
        public override bool IsHidden(object node) { var f = node as WinFile; return f != null && f.Hidden; }
        public override IEnumerable<KeyValuePair<string, object>> Kids(ProvCtx x, string path, object node)
        {
            var f = node as WinFile;
            if (f == null || !f.IsDir) yield break;
            foreach (var k in f.Kids.Values.Where(k => k.IsDir).OrderBy(k => k.Name, StringComparer.OrdinalIgnoreCase)) yield return new KeyValuePair<string, object>(k.Name, k);
            foreach (var k in f.Kids.Values.Where(k => !k.IsDir).OrderBy(k => k.Name, StringComparer.OrdinalIgnoreCase)) yield return new KeyValuePair<string, object>(k.Name, k);
        }
        public override PsObj Item(ProvCtx x, string path, object node, PsDrive d) { return PsProviders.FileItem(x.R, (WinFile)node, d, false); }
        public override string ShownPath(PPath p) { return p.Path; }
        public override bool ValidName(string name) { return name.Length > 0 && name.IndexOfAny(new[] { '<', '>', '|', '"', ':', '*', '?', '\\', '/' }) < 0; }

        public override PsObj NewItem(ProvCtx x, PPath p, string type, object value)
        {
            string full = p.Path;
            string t = (type ?? "File").ToLowerInvariant();
            var parentPath = ParentOf(full);
            var parent = x.M.FindFile(parentPath);
            var existing = x.M.FindFile(full);
            if (t == "directory" || t == "container")
            {
                if (existing != null)
                {
                    if (x.Force && existing.IsDir) return PsProviders.FileItem(x.R, existing, p.Drive, false);
                    throw new ProvError("An item with the specified name " + full + " already exists.", "DirectoryExist", "ResourceExists", full, "System.IO.IOException");
                }
                var dir = x.M.MakeDir(full, x.M.Now);
                if (parent != null) parent.Modified = x.M.Now;
                return PsProviders.FileItem(x.R, dir, p.Drive, false);
            }
            if (t != "file" && t != "leaf")
            {
                if (t == "symboliclink" || t == "junction" || t == "hardlink") throw new ProvError("Cert Lab's practice console does not simulate links (" + type + ").", "CertLabNotSimulated", "NotImplemented", full, "System.Management.Automation.PSNotSupportedException");
                throw new ProvError("The type is not a known type for the file system. Only \"file\",\"directory\" or \"symboliclink\" can be specified.", "Argument", "InvalidArgument", full, "System.ArgumentException");
            }
            if (existing != null && !x.Force)
                throw new ProvError("The file '" + full + "' already exists.", "NewItemIOError", "ResourceExists", full, "System.IO.IOException");
            if (existing != null && existing.IsDir) throw new ProvError("Access to the path '" + full + "' is denied.", "NewItemUnauthorizedAccessError", "PermissionDenied", full, "System.UnauthorizedAccessException");
            if (parent == null)
            {
                if (!x.Force) throw new ProvError("Could not find a part of the path '" + full + "'.", "NewItemIOError", "WriteError", full, "System.IO.DirectoryNotFoundException");
                x.M.MakeDir(parentPath, x.M.Now);
            }
            string text = value == null ? "" : PsConv.ToStr(value);
            if (existing != null && existing.ReadOnly) throw new ProvError("Access to the path '" + full + "' is denied.", "NewItemUnauthorizedAccessError", "PermissionDenied", full, "System.UnauthorizedAccessException");
            var why = x.R.WriteFile(full, text, "utf8", false);
            if (why != null) throw new ProvError(why, "NewItemIOError", "WriteError", full, "System.IO.IOException");
            return PsProviders.FileItem(x.R, x.M.FindFile(full), p.Drive, false);
        }

        public override void RemoveItem(ProvCtx x, string path, object node, bool recurse)
        {
            var f = (WinFile)node;
            if (f.Parent == null) throw new ProvError("Cannot remove the root of drive C:.", "RemoveItemUnauthorizedAccessError", "PermissionDenied", path, "System.UnauthorizedAccessException");
            if (!x.Force && !f.IsDir && (f.Attr & 7) != 0)
                throw new ProvError("Cannot remove item " + f.FullPath + ": You do not have sufficient access rights to perform this operation.", "RemoveFileSystemItemUnAuthorizedAccess", "PermissionDenied", PsProviders.FileItem(x.R, f, null, false), "System.IO.IOException");
            if (f.IsDir && !x.Force)
            {
                var ro = Descendants(f).FirstOrDefault(k => !k.IsDir && (k.Attr & 7) != 0);
                if (ro != null) throw new ProvError("Cannot remove item " + ro.FullPath + ": You do not have sufficient access rights to perform this operation.", "RemoveFileSystemItemUnAuthorizedAccess", "PermissionDenied", PsProviders.FileItem(x.R, ro, null, false), "System.IO.IOException");
            }
            if (IsUnder(x.R.CurrentFsDir, f.FullPath) && f.IsDir)
                throw new ProvError("Cannot remove the item at '" + f.FullPath + "' because it is in use.", "InvalidOperation", "InvalidOperation", f.FullPath, "System.Management.Automation.PSInvalidOperationException");
            f.Parent.Kids.Remove(f.Name);
            f.Parent.Modified = x.M.Now;
        }
        static bool IsUnder(string cur, string dir) { return cur != null && (cur.Equals(dir, StringComparison.OrdinalIgnoreCase) || cur.StartsWith(dir.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)); }
        public static IEnumerable<WinFile> Descendants(WinFile f) { foreach (var k in f.Kids.Values) { yield return k; if (k.IsDir) foreach (var d in Descendants(k)) yield return d; } }

        public override void RenameItem(ProvCtx x, string path, object node, string newName)
        {
            var f = (WinFile)node;
            if (f.Parent == null) throw ProvError.NotSupported();
            var other = f.Parent.Kid(newName);
            if (other != null && other != f) throw new ProvError("Cannot create a file when that file already exists.", "RenameItemIOError", "WriteError", f.FullPath, "System.IO.IOException");
            f.Parent.Kids.Remove(f.Name);
            f.Name = newName;
            f.Parent.Kids[newName] = f;
            f.Parent.Modified = x.M.Now;
        }

        public override void CopyItem(ProvCtx x, string path, object node, string dest, bool recurse, bool move)
        {
            var f = (WinFile)node;
            string dparent = ParentOf(dest);
            var dp = x.M.FindFile(dparent);
            if (dp == null || !dp.IsDir) throw new ProvError("Could not find a part of the path '" + dest + "'.", move ? "MoveFileInfoItemIOError" : "CopyDirectoryInfoItemIOError", "WriteError", dest, "System.IO.DirectoryNotFoundException");
            string name = LeafOf(dest);
            var old = dp.Kid(name);
            if (move)
            {
                if (f.IsDir && IsUnder(dest, f.FullPath)) throw new ProvError("Destination path cannot be a subdirectory of the source: " + dest + ".", "MoveItemArgumentError", "InvalidArgument", dest, "System.ArgumentException");
                if (old != null)
                {
                    if (!x.Force || old.IsDir) throw new ProvError("Cannot create a file when that file already exists.", f.IsDir ? "MoveDirectoryItemIOError" : "MoveFileInfoItemIOError", "WriteError", f.FullPath, "System.IO.IOException");
                    dp.Kids.Remove(old.Name);
                }
                f.Parent.Kids.Remove(f.Name);
                f.Parent.Modified = x.M.Now;
                f.Name = name;
                dp.Add(f);
                dp.Modified = x.M.Now;
                return;
            }
            if (f.IsDir)
            {
                var copy = new WinFile { Name = name, IsDir = true, Attr = f.Attr, Created = x.M.Now, Modified = x.M.Now, Accessed = x.M.Now, Owner = f.Owner };
                if (old != null && old.IsDir) copy = old;
                else if (old != null) throw new ProvError("An item with the specified name " + dest + " already exists.", "DirectoryExist", "ResourceExists", dest, "System.IO.IOException");
                else dp.Add(copy);
                if (recurse) foreach (var k in f.Kids.Values.ToList()) CopyInto(x, k, copy);
                dp.Modified = x.M.Now;
                return;
            }
            if (old != null && old.IsDir) throw new ProvError("An item with the specified name " + dest + " already exists.", "CopyError", "WriteError", dest, "System.IO.IOException");
            if (old != null && old.ReadOnly && !x.Force) throw new ProvError("Access to the path '" + dest + "' is denied.", "CopyFileInfoItemUnauthorizedAccessError", "PermissionDenied", dest, "System.UnauthorizedAccessException");
            var c = f.CloneTree(); c.Name = name; c.Created = x.M.Now; c.Accessed = x.M.Now;
            if (old != null) dp.Kids.Remove(old.Name);
            dp.Add(c);
            dp.Modified = x.M.Now;
        }
        void CopyInto(ProvCtx x, WinFile src, WinFile dstDir)
        {
            var old = dstDir.Kid(src.Name);
            if (src.IsDir)
            {
                var d = old != null && old.IsDir ? old : dstDir.Add(new WinFile { Name = src.Name, IsDir = true, Attr = src.Attr, Created = x.M.Now, Modified = x.M.Now, Accessed = x.M.Now, Owner = src.Owner });
                foreach (var k in src.Kids.Values.ToList()) CopyInto(x, k, d);
                return;
            }
            var c = src.CloneTree(); c.Created = x.M.Now; c.Accessed = x.M.Now;
            if (old != null) dstDir.Kids.Remove(old.Name);
            dstDir.Add(c);
        }

        public override List<string> ReadContent(ProvCtx x, string path, object node)
        {
            var f = (WinFile)node;
            if (f.IsDir) throw new ProvError("Access to the path '" + f.FullPath + "' is denied.", "GetContentReaderUnauthorizedAccessError", "PermissionDenied", f.FullPath, "System.UnauthorizedAccessException");
            f.Accessed = x.M.Now;
            return PsRunspace.SplitLines(f.Text ?? "");
        }
    }

    // ================================================================ Registry
    public class RegProvider : PsProvider
    {
        public RegProvider() { Name = "Registry"; Caps = "ShouldProcess, Transactions"; }
        public static string HiveName(string s)
        {
            switch ((s ?? "").ToUpperInvariant())
            {
                case "HKLM": case "HKEY_LOCAL_MACHINE": return "HKEY_LOCAL_MACHINE";
                case "HKCU": case "HKEY_CURRENT_USER": return "HKEY_CURRENT_USER";
                case "HKCR": case "HKEY_CLASSES_ROOT": return "HKEY_CLASSES_ROOT";
                case "HKU": case "HKEY_USERS": return "HKEY_USERS";
                case "HKCC": case "HKEY_CURRENT_CONFIG": return "HKEY_CURRENT_CONFIG";
            }
            return null;
        }
        public override void SplitProviderPath(string rest, PPath into)
        {
            into.Root = "";
            var parts = rest.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (parts.Count > 0) { var h = HiveName(parts[0]); if (h != null) parts[0] = h; }
            into.Segs.AddRange(parts);
        }
        static RegKey Hive(PsMachine m, string name)
        {
            switch (name.ToUpperInvariant())
            {
                case "HKEY_LOCAL_MACHINE": return m.HKLM;
                case "HKEY_CURRENT_USER": return m.HKCU;
                case "HKEY_CLASSES_ROOT": return m.HKLM.Sub("SOFTWARE\\Classes");
            }
            return null;
        }
        public static readonly object TopNode = new object();
        public override object Find(ProvCtx x, string path)
        {
            var parts = path.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return TopNode;
            var k = Hive(x.M, parts[0]);
            for (int i = 1; i < parts.Length && k != null; i++) k = k.Kid(parts[i]);
            return k;
        }
        public static RegKey KeyAt(PsMachine m, string providerPath) { return new RegProvider().Find(new ProvCtx { R = null }, providerPath) as RegKey; }
        public override bool IsContainer(object node) { return node is RegKey || node == TopNode; }
        public override IEnumerable<KeyValuePair<string, object>> Kids(ProvCtx x, string path, object node)
        {
            if (node == TopNode)
            {
                foreach (var h in new[] { "HKEY_LOCAL_MACHINE", "HKEY_CURRENT_USER", "HKEY_CLASSES_ROOT" }) yield return new KeyValuePair<string, object>(h, Hive(x.M, h));
                yield break;
            }
            var k = node as RegKey;
            if (k == null) yield break;
            foreach (var c in k.Kids.Values.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)) yield return new KeyValuePair<string, object>(c.Name, c);
        }
        public override PsObj Item(ProvCtx x, string path, object node, PsDrive d) { return PsProviders.KeyItem(x.R, (RegKey)node, path, d); }
        public override bool ValidName(string name) { return name.Length > 0 && name.IndexOf('\\') < 0; }

        public override PsObj NewItem(ProvCtx x, PPath p, string type, object value)
        {
            string full = p.Path;
            if (Find(x, full) is RegKey) throw new ProvError("A key at this path already exists.", "System.IO.IOException", "ResourceExists", p.P.Qualified + "::" + full, "System.IO.IOException");
            var parent = Find(x, ParentOf(full)) as RegKey;
            if (parent == null)
            {
                if (!x.Force) throw ProvError.NotFound(p.Parent().DrivePath);
                var pp = p.Parent(); var chain = new List<string>();
                while (pp != null && !(Find(x, pp.Path) is RegKey)) { chain.Add(pp.Leaf); pp = pp.Parent(); }
                if (pp == null) throw ProvError.NotFound(p.DrivePath);
                parent = ((RegKey)Find(x, pp.Path)).Sub(string.Join("\\", Enumerable.Reverse(chain).ToArray()));
            }
            var k = parent.Sub(LeafOf(full));
            if (value != null) k.Set("", PsConv.ToStr(value), "String");
            return PsProviders.KeyItem(x.R, k, full, p.Drive);
        }
        public override void RemoveItem(ProvCtx x, string path, object node, bool recurse)
        {
            var k = node as RegKey;
            if (k == null || k.Parent == null) throw ProvError.NotSupported();
            k.Parent.Kids.Remove(k.Name);
        }
        public override void RenameItem(ProvCtx x, string path, object node, string newName)
        {
            var k = (RegKey)node;
            if (k.Parent == null) throw ProvError.NotSupported();
            if (k.Parent.Kid(newName) != null && k.Parent.Kid(newName) != k) throw new ProvError("A key at this path already exists.", "System.IO.IOException", "ResourceExists", path, "System.IO.IOException");
            k.Parent.Kids.Remove(k.Name);
            k.Name = newName;
            k.Parent.Kids[newName] = k;
        }
        public override void CopyItem(ProvCtx x, string path, object node, string dest, bool recurse, bool move)
        {
            var k = (RegKey)node;
            var dp = Find(x, ParentOf(dest)) as RegKey;
            if (dp == null) throw ProvError.NotFound(ParentOf(dest));
            string name = LeafOf(dest);
            if (dp.Kid(name) != null) throw new ProvError("A key at this path already exists.", "System.IO.IOException", "ResourceExists", dest, "System.IO.IOException");
            if (move)
            {
                k.Parent.Kids.Remove(k.Name);
                k.Name = name; k.Parent = dp; dp.Kids[name] = k;
                return;
            }
            var c = k.CloneTree(dp); c.Name = name;
            if (!recurse) c.Kids.Clear();
            dp.Kids[name] = c;
        }
        public override void SetItem(ProvCtx x, string path, object node, object value) { ((RegKey)node).Set("", PsConv.ToStr(value), "String"); }
        public override void ClearItem(ProvCtx x, string path, object node) { ((RegKey)node).Values.Clear(); }
    }

    // ================================================================ flat providers: Environment, Variable, Alias, Function
    public abstract class FlatProvider : PsProvider
    {
        public static readonly object RootNode = new object();
        public override bool Hierarchical { get { return false; } }
        public override void SplitProviderPath(string rest, PPath into) { into.Root = ""; var t = rest.Trim('\\', '/'); if (t.Length > 0) into.Segs.Add(t); }
        public override string ParentOf(string path) { return ""; }
        public abstract IEnumerable<string> Names(ProvCtx x);
        public abstract object Value(ProvCtx x, string name);
        public override object Find(ProvCtx x, string path)
        {
            if (path.Length == 0) return RootNode;
            foreach (var n in Names(x)) if (n.Equals(path, StringComparison.OrdinalIgnoreCase)) return n;
            return null;
        }
        public override bool IsContainer(object node) { return node == RootNode; }
        public override IEnumerable<KeyValuePair<string, object>> Kids(ProvCtx x, string path, object node)
        {
            if (node != RootNode) yield break;
            foreach (var n in Names(x).OrderBy(n => n, StringComparer.OrdinalIgnoreCase)) yield return new KeyValuePair<string, object>(n, n);
        }
        protected PsObj Notes(PsObj o, string name, PsDrive d)
        {
            o.Add("PSPath", Qualified + "::" + name);
            o.Add("PSDrive", PsProviders.DriveObj(d));
            o.Add("PSProvider", PsProviders.ProviderObj(this));
            o.Add("PSIsContainer", false);
            return o;
        }
        public override List<string> ReadContent(ProvCtx x, string path, object node)
        {
            if (node == RootNode) throw ProvError.NotSupported();
            var v = Value(x, (string)node);
            return PsConv.Items(v).Select(o => PsConv.ToStr(o)).ToList();
        }
        public override PsObj NewItem(ProvCtx x, PPath p, string type, object value)
        {
            string name = p.Leaf;
            if (Find(x, name) is string && !x.Force) throw Exists(name);
            Assign(x, name, value, true);
            return Item(x, name, Find(x, name), p.Drive);
        }
        protected virtual ProvError Exists(string name) { return new ProvError("A " + Noun + " with name '" + name + "' already exists.", Noun.Substring(0, 1).ToUpperInvariant() + Noun.Substring(1) + "AlreadyExists", "ResourceExists", name, "System.Management.Automation.SessionStateException"); }
        protected abstract string Noun { get; }
        public abstract void Assign(ProvCtx x, string name, object value, bool create);
        public abstract void Delete(ProvCtx x, string name);
        public override void SetItem(ProvCtx x, string path, object node, object value) { Assign(x, (string)node, value, false); }
        public override void RemoveItem(ProvCtx x, string path, object node, bool recurse) { if (node == RootNode) throw ProvError.NotSupported(); Delete(x, (string)node); }
        public override void RenameItem(ProvCtx x, string path, object node, string newName)
        {
            if (Find(x, newName) is string && !string.Equals(newName, (string)node, StringComparison.OrdinalIgnoreCase)) throw Exists(newName);
            var v = Value(x, (string)node);
            Delete(x, (string)node);
            Assign(x, newName, v, true);
        }
        public override void CopyItem(ProvCtx x, string path, object node, string dest, bool recurse, bool move)
        {
            var v = Value(x, (string)node);
            if (move) Delete(x, (string)node);
            Assign(x, LeafOf(dest), v, true);
        }
        public override void WriteContent(ProvCtx x, string path, object node, List<object> values, bool append)
        {
            string name = node as string ?? path;
            object v = values.Count == 1 ? values[0] : (object)values.ToArray();
            if (append && node is string)
            {
                var old = Value(x, (string)node);
                var l = PsConv.Items(old).ToList(); l.AddRange(values);
                v = l.Count == 1 ? l[0] : (object)l.ToArray();
            }
            Assign(x, name, v, !(node is string));
        }
        public override void ClearContent(ProvCtx x, string path, object node) { ClearItem(x, path, node); }
    }

    public class EnvProvider : FlatProvider
    {
        public EnvProvider() { Name = "Environment"; }
        protected override string Noun { get { return "environment variable"; } }
        public override IEnumerable<string> Names(ProvCtx x) { return x.R.Env.Keys.ToList(); }
        public override object Value(ProvCtx x, string name) { return x.R.GetEnv(name); }
        public override PsObj Item(ProvCtx x, string path, object node, PsDrive d)
        {
            if (node == RootNode) return PsProviders.ContainerItem(this, d);
            string n = (string)node;
            string real = x.R.Env.Keys.First(k => k.Equals(n, StringComparison.OrdinalIgnoreCase));
            var o = new PsObj("System.Collections.DictionaryEntry", "System.ValueType", "System.Object");
            Notes(o, real, d);
            o.Alias("Name", "Key");
            o.Val("Key", "System.Object", real);
            o.Val("Value", "System.Object", x.R.GetEnv(real));
            o.DefaultDisplay = new[] { "Name", "Value" };
            o.ToStr = y => "System.Collections.DictionaryEntry";
            return o;
        }
        public override void Assign(ProvCtx x, string name, object value, bool create)
        {
            string real = x.R.Env.Keys.FirstOrDefault(k => k.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? name;
            string s = value == null ? null : string.Join(PsConv.Ofs, PsConv.Items(value).Select(o => PsConv.ToStr(o)).ToArray());
            if (string.IsNullOrEmpty(s)) { x.R.SetEnv(real, null); return; }
            x.R.SetEnv(real, s);
        }
        public override void Delete(ProvCtx x, string name) { x.R.SetEnv(name, null); }
        public override void ClearItem(ProvCtx x, string path, object node) { if (node is string) x.R.SetEnv((string)node, null); }
    }

    public class VarProvider : FlatProvider
    {
        public VarProvider() { Name = "Variable"; }
        protected override string Noun { get { return "variable"; } }
        protected override ProvError Exists(string name) { return new ProvError("A variable with name '" + name + "' already exists.", "VariableAlreadyExists", "ResourceExists", name, "System.Management.Automation.SessionStateException"); }
        public override IEnumerable<string> Names(ProvCtx x) { return PsCmdlets.VisibleVars(x.Scope ?? x.R.Global).Select(v => v.Name).ToList(); }
        public override object Value(ProvCtx x, string name) { var v = x.R.FindVar(name, x.Scope ?? x.R.Global); return v == null ? null : v.Value; }
        public override PsObj Item(ProvCtx x, string path, object node, PsDrive d)
        {
            if (node == RootNode) return PsProviders.ContainerItem(this, d);
            var v = x.R.FindVar((string)node, x.Scope ?? x.R.Global);
            return Notes(PsCmdlets.VarItem(v), v.Name, d);
        }
        public override void Assign(ProvCtx x, string name, object value, bool create)
        {
            var v = x.R.FindVar(name, x.Scope ?? x.R.Global);
            if (v != null && (v.Constant || v.ReadOnly && !x.Force)) throw new ProvError("Cannot overwrite variable " + name + " because it is read-only or constant.", "VariableNotWritable", "WriteError", name, "System.Management.Automation.SessionStateUnauthorizedAccessException");
            if (v == null) { v = new PsVariable { Name = name }; (x.Scope ?? x.R.Global).Vars[name] = v; }
            v.Value = value;
        }
        public override void Delete(ProvCtx x, string name)
        {
            for (var s = x.Scope ?? x.R.Global; s != null; s = s.Parent)
            {
                PsVariable v;
                if (!s.Vars.TryGetValue(name, out v)) continue;
                if (v.Constant || v.ReadOnly && !x.Force) throw new ProvError("Cannot remove variable " + name + " because it is constant or read-only. If the variable is read-only, try the operation again specifying the Force option.", "VariableNotRemovable", "WriteError", name, "System.Management.Automation.SessionStateUnauthorizedAccessException");
                s.Vars.Remove(name);
                return;
            }
        }
        public override void ClearItem(ProvCtx x, string path, object node) { if (node is string) { var v = x.R.FindVar((string)node, x.Scope ?? x.R.Global); if (v != null) v.Value = null; } }
    }

    public class AliasProvider : FlatProvider
    {
        public AliasProvider() { Name = "Alias"; }
        protected override string Noun { get { return "alias"; } }
        protected override ProvError Exists(string name) { return new ProvError("The alias is not allowed, because an alias with the name '" + name + "' already exists.", "AliasAlreadyExists", "ResourceExists", name, "System.Management.Automation.SessionStateException"); }
        public override IEnumerable<string> Names(ProvCtx x) { return x.R.Aliases.Keys.ToList(); }
        public override object Value(ProvCtx x, string name) { string t; return x.R.Aliases.TryGetValue(name, out t) ? t : null; }
        public override PsObj Item(ProvCtx x, string path, object node, PsDrive d)
        {
            if (node == RootNode) return PsProviders.ContainerItem(this, d);
            string n = x.R.Aliases.Keys.First(k => k.Equals((string)node, StringComparison.OrdinalIgnoreCase));
            return Notes(PsCmdlets.AliasInfo(n, x.R.Aliases[n]), n, d);
        }
        public override void Assign(ProvCtx x, string name, object value, bool create) { x.R.Aliases[name] = PsConv.ToStr(value); }
        public override void Delete(ProvCtx x, string name) { x.R.Aliases.Remove(name); }
    }

    public class FuncProvider : FlatProvider
    {
        public FuncProvider() { Name = "Function"; }
        protected override string Noun { get { return "function"; } }
        protected override ProvError Exists(string name) { return new ProvError("The function " + name + " already exists.", "FunctionAlreadyExists", "ResourceExists", name, "System.Management.Automation.SessionStateException"); }
        IEnumerable<PsFunction> All(ProvCtx x)
        {
            var d = new Dictionary<string, PsFunction>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in x.R.LoadedModules) foreach (var f in m.Exported.Values) d[f.Name] = f;
            var scopes = new List<PsScope>();
            for (var s = x.Scope ?? x.R.Global; s != null; s = s.Parent) scopes.Add(s);
            scopes.Reverse();
            foreach (var s in scopes) foreach (var f in s.Funcs.Values) d[f.Name] = f;
            return d.Values;
        }
        public override IEnumerable<string> Names(ProvCtx x) { return All(x).Select(f => f.Name).ToList(); }
        public override object Value(ProvCtx x, string name) { var f = x.R.FindFunction(name, x.Scope ?? x.R.Global); return f == null ? null : new PsScriptBlock { Ast = f.Ast, Module = f.Module }; }
        public override PsObj Item(ProvCtx x, string path, object node, PsDrive d)
        {
            if (node == RootNode) return PsProviders.ContainerItem(this, d);
            var f = All(x).First(k => k.Name.Equals((string)node, StringComparison.OrdinalIgnoreCase));
            var ci = new PsCommandInfo { Kind = f.IsConfiguration ? "Configuration" : f.Filter ? "Filter" : "Function", Name = f.Name, Func = f, Module = f.Module == null ? "" : f.Module.Name, Version = f.Module == null ? "" : f.Module.Version.ToString(), Definition = f.Definition };
            return Notes(PsCmdlets.CommandObj(x.R, ci), f.Name, d);
        }
        public override List<string> ReadContent(ProvCtx x, string path, object node)
        {
            if (node == RootNode) throw ProvError.NotSupported();
            var f = x.R.FindFunction((string)node, x.Scope ?? x.R.Global);
            return new List<string> { f == null ? "" : f.Definition };
        }
        public override void Assign(ProvCtx x, string name, object value, bool create)
        {
            if (value is object[] && ((object[])value).Length == 1) value = ((object[])value)[0];
            x.R.DefineFunctionAt(name, value, x.Scope ?? x.R.Global);
        }
        public override void Delete(ProvCtx x, string name)
        {
            for (var s = x.Scope ?? x.R.Global; s != null; s = s.Parent) if (s.Funcs.Remove(name)) return;
        }
    }

    // ================================================================ Certificate
    public class CertProvider : PsProvider
    {
        public CertProvider() { Name = "Certificate"; Module = "Microsoft.PowerShell.Security"; Caps = "ShouldProcess"; }
        public static readonly string[] MachineStores = { "TrustedPublisher", "ClientAuthIssuer", "Remote Desktop", "Root", "TrustedDevices", "WebHosting", "CA", "AuthRoot", "TrustedPeople", "My", "SmartCardRoot", "Trust", "Disallowed" };
        public static readonly string[] UserStores = { "TrustedPublisher", "ClientAuthIssuer", "Root", "UserDS", "CA", "AuthRoot", "TrustedPeople", "My", "SmartCardRoot", "Trust", "Disallowed" };
        public class Loc { public string Name; }
        public class Store { public string Location, Name; }
        public override void SplitProviderPath(string rest, PPath into) { into.Root = ""; into.Segs.AddRange(rest.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries)); }
        static readonly object Top = new object();
        public override object Find(ProvCtx x, string path)
        {
            x.M.EnsureLabCerts();
            var parts = path.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return Top;
            string loc = parts[0].Equals("LocalMachine", StringComparison.OrdinalIgnoreCase) ? "LocalMachine" : parts[0].Equals("CurrentUser", StringComparison.OrdinalIgnoreCase) ? "CurrentUser" : null;
            if (loc == null) return null;
            if (parts.Length == 1) return new Loc { Name = loc };
            var stores = loc == "LocalMachine" ? MachineStores : UserStores;
            string st = stores.FirstOrDefault(s => s.Equals(parts[1], StringComparison.OrdinalIgnoreCase));
            if (st == null) return null;
            if (parts.Length == 2) return new Store { Location = loc, Name = st };
            if (parts.Length > 3) return null;
            return x.M.Certs.FirstOrDefault(c => c.Store.Equals(loc + "\\" + st, StringComparison.OrdinalIgnoreCase) && c.Thumbprint.Equals(parts[2], StringComparison.OrdinalIgnoreCase));
        }
        public override bool IsContainer(object node) { return node == Top || node is Loc || node is Store; }
        public override IEnumerable<KeyValuePair<string, object>> Kids(ProvCtx x, string path, object node)
        {
            if (node == Top) { yield return new KeyValuePair<string, object>("CurrentUser", new Loc { Name = "CurrentUser" }); yield return new KeyValuePair<string, object>("LocalMachine", new Loc { Name = "LocalMachine" }); yield break; }
            var l = node as Loc;
            if (l != null) { foreach (var s in l.Name == "LocalMachine" ? MachineStores : UserStores) yield return new KeyValuePair<string, object>(s, new Store { Location = l.Name, Name = s }); yield break; }
            var st = node as Store;
            if (st != null) foreach (var c in x.M.Certs.Where(c => c.Store.Equals(st.Location + "\\" + st.Name, StringComparison.OrdinalIgnoreCase))) yield return new KeyValuePair<string, object>(c.Thumbprint, c);
        }
        public override PsObj Item(ProvCtx x, string path, object node, PsDrive d)
        {
            if (node == Top) return PsProviders.ContainerItem(this, d);
            var l = node as Loc;
            if (l != null)
            {
                var o = new PsObj("Microsoft.PowerShell.Commands.X509StoreLocation", "System.Object");
                PsProviders.AddNotes(o, this, d, l.Name, "", l.Name, true);
                o.Val("Location", "System.Security.Cryptography.X509Certificates.StoreLocation", l.Name);
                var names = PsConv.NewOrdered();
                foreach (var s in l.Name == "LocalMachine" ? MachineStores : UserStores) names[s] = true;
                o.Val("StoreNames", "System.Collections.Hashtable", names);
                o.ToStr = y => "Microsoft.PowerShell.Commands.X509StoreLocation";
                return o;
            }
            var st = node as Store;
            if (st != null)
            {
                var o = new PsObj("System.Security.Cryptography.X509Certificates.X509Store", "System.Object");
                PsProviders.AddNotes(o, this, d, st.Location + "\\" + st.Name, st.Location, st.Name, true);
                o.Val("Name", "string", st.Name);
                o.Val("Location", "System.Security.Cryptography.X509Certificates.StoreLocation", st.Location);
                o.ToStr = y => "System.Security.Cryptography.X509Certificates.X509Store";
                return o;
            }
            return PsProviders.CertItem(x.R, (WinCert)node, d);
        }
        public override void RemoveItem(ProvCtx x, string path, object node, bool recurse)
        {
            var c = node as WinCert;
            if (c == null) throw ProvError.NotSupported();
            x.M.Certs.Remove(c);
        }
        public override void CopyItem(ProvCtx x, string path, object node, string dest, bool recurse, bool move)
        {
            var c = node as WinCert;
            if (c == null || !move) throw ProvError.NotSupported();
            var store = Find(x, ParentOf(dest)) as Store;
            if (store == null) throw ProvError.NotFound(ParentOf(dest));
            c.Store = store.Location + "\\" + store.Name;
        }
    }

    /// <summary>A provider Cert Lab shows but does not simulate yet (WSMan:): every operation says so.</summary>
    public class StubProvider : PsProvider
    {
        public StubProvider(string name, string caps) { Name = name; Caps = caps; }
        ProvError Why() { return new ProvError("Cert Lab's practice console does not simulate the " + Name + " provider yet.", "CertLabNotSimulated", "NotImplemented", null, "System.Management.Automation.PSNotSupportedException"); }
        public override object Find(ProvCtx x, string path) { throw Why(); }
        public override bool IsContainer(object node) { return true; }
        public override IEnumerable<KeyValuePair<string, object>> Kids(ProvCtx x, string path, object node) { throw Why(); }
        public override PsObj Item(ProvCtx x, string path, object node, PsDrive d) { throw Why(); }
    }

    // ================================================================ the provider table, items and helpers
    public static class PsProviders
    {
        static readonly List<PsProvider> list = new List<PsProvider>();
        static readonly object gate = new object();
        /// <summary>The providers in Get-PSProvider's order. Register replaces one with the same name (WSMan: for another part of Cert Lab).</summary>
        public static List<PsProvider> All
        {
            get
            {
                lock (gate)
                {
                    if (list.Count == 0)
                    {
                        list.Add(new RegProvider()); list.Add(new AliasProvider()); list.Add(new EnvProvider()); list.Add(new FsProvider());
                        list.Add(new FuncProvider()); list.Add(new VarProvider()); list.Add(new CertProvider()); list.Add(new StubProvider("WSMan", "Credentials"));
                    }
                    return list;
                }
            }
        }
        public static void Register(PsProvider p) { lock (gate) { var l = All; int i = l.FindIndex(x => x.Name.Equals(p.Name, StringComparison.OrdinalIgnoreCase)); if (i >= 0) l[i] = p; else l.Add(p); } }
        public static PsProvider Get(string name)
        {
            if (name == null) return null;
            int bs = name.LastIndexOf('\\');
            string n = bs >= 0 ? name.Substring(bs + 1) : name;
            return All.FirstOrDefault(p => p.Name.Equals(n, StringComparison.OrdinalIgnoreCase) && (bs < 0 || p.Module.Equals(name.Substring(0, bs), StringComparison.OrdinalIgnoreCase)));
        }

        public static PsObj ProviderObj(PsProvider p)
        {
            var o = new PsObj("System.Management.Automation.ProviderInfo", "System.Object");
            o.Val("ImplementingType", "type", "Microsoft.PowerShell.Commands." + p.Name + "Provider");
            o.Val("HelpFile", "string", p.Module + ".dll-Help.xml");
            o.Val("Name", "string", p.Name);
            o.Val("PSSnapIn", "System.Management.Automation.PSSnapInInfo", p.Module == "Microsoft.PowerShell.Core" ? p.Module : null);
            o.Val("ModuleName", "string", p.Module);
            o.Val("Module", "psmoduleinfo", p.Module == "Microsoft.PowerShell.Core" ? null : p.Module);
            o.Val("Description", "string", "");
            o.Val("Capabilities", "System.Management.Automation.Provider.ProviderCapabilities", PsEnums.Get("System.Management.Automation.Provider.ProviderCapabilities").Parse(p.Caps));
            o.Val("Home", "string", p is FsProvider ? "C:\\Users\\Administrator" : null);
            o.Base = p;
            o.ToStr = x => p.Qualified;
            return o;
        }
        public static PsObj DriveObj(PsDrive d) { return DriveObj(null, d); }
        public static PsObj DriveObj(PsRunspace R, PsDrive d)
        {
            if (d == null) return null;
            var o = new PsObj("System.Management.Automation.PSDriveInfo", "System.Object");
            var p = Get(d.Provider);
            o.Val("CurrentLocation", "string", string.Join("\\", d.Current.ToArray()));
            o.Val("Name", "string", d.Name);
            o.Val("Provider", "System.Management.Automation.ProviderInfo", p == null ? null : ProviderObj(p));
            o.Val("Root", "string", d.Root);
            o.Val("Description", "string", d.Description);
            o.Val("MaximumSize", "System.Nullable[long]", null);
            o.Val("Credential", "pscredential", null);
            o.Val("DisplayRoot", "string", null);
            if (d.Provider == "FileSystem" && R != null && d.Root.Length <= 3)
            {
                long used = 22941143040L + FsProvider.Descendants(R.M.C).Where(f => !f.IsDir).Sum(f => f.Length);
                long total = 135771664384L;
                o.Val("Used", "long", used);
                o.Val("Free", "long", total - used);
            }
            o.Base = d;
            o.ToStr = x => d.Name;
            return o;
        }

        /// <summary>PSPath, PSParentPath, PSChildName, PSDrive, PSProvider, PSIsContainer (the provider's note properties).</summary>
        public static void AddNotes(PsObj o, PsProvider p, PsDrive d, string path, string parent, string child, bool container)
        {
            o.Add("PSPath", p.Qualified + "::" + path);
            o.Add("PSParentPath", parent == null || (parent.Length == 0 && !(p is CertProvider && path.Contains("\\"))) ? "" : p.Qualified + "::" + parent);
            o.Add("PSChildName", child);
            o.Add("PSDrive", DriveObj(d));
            o.Add("PSProvider", ProviderObj(p));
            o.Add("PSIsContainer", container);
        }
        public static PsObj ContainerItem(PsProvider p, PsDrive d)
        {
            var o = new PsObj("System.Management.Automation.PSObject", "System.Object");
            AddNotes(o, p, d, "", "", "", true);
            o.ToStr = x => p.Qualified + "::";
            return o;
        }

        // ------------------------------------------------------------ files
        static readonly DateTime Epoch = new DateTime(1601, 1, 1);
        public static DateTime Utc(PsMachine m, DateTime t) { return t.AddHours(-m.UtcOffsetHours); }
        public static PsDrive DriveFor(PsRunspace R, PsProvider p, string path)
        {
            if (R == null) return null;
            PsDrive best = null;
            foreach (var d in R.Drives)
            {
                if (!d.Provider.Equals(p.Name, StringComparison.OrdinalIgnoreCase)) continue;
                string root = d.Root.TrimEnd('\\');
                bool under = root.Length == 0 || path.Equals(root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase) || path.Equals(d.Root, StringComparison.OrdinalIgnoreCase);
                if (!under) continue;
                if (best == null || root.Length > best.Root.TrimEnd('\\').Length || root.Length == best.Root.TrimEnd('\\').Length && d.BuiltIn && !best.BuiltIn) best = d;
            }
            return best;
        }

        /// <summary>A System.IO.FileInfo or DirectoryInfo for a file or folder (nameOnly: ToString() gives only the name, as in a folder listing).</summary>
        public static PsObj FileItem(PsRunspace R, WinFile f, PsDrive d, bool nameOnly)
        {
            var m = R.M;
            string full = f.FullPath;
            var fs = (FsProvider)Get("FileSystem");
            if (d == null || !d.Provider.Equals("FileSystem", StringComparison.OrdinalIgnoreCase)) d = DriveFor(R, fs, full);
            var o = new PsObj(f.IsDir ? "System.IO.DirectoryInfo" : "System.IO.FileInfo", "System.IO.FileSystemInfo", "System.MarshalByRefObject", "System.Object");
            AddNotes(o, fs, d, full, f.Parent == null ? "" : f.Parent.FullPath, f.Parent == null ? full : f.Name, f.IsDir);
            o.Prop("Mode", "string", () => f.Mode);
            if (!f.IsDir) o.Prop("VersionInfo", "System.Diagnostics.FileVersionInfo", () => VersionInfo(m, f));
            o.Prop("BaseName", "string", () => f.IsDir || f.Parent == null ? f.Name : (f.Name.LastIndexOf('.') > 0 ? f.Name.Substring(0, f.Name.LastIndexOf('.')) : f.Name));
            o.Val("Target", "System.Collections.Generic.IEnumerable[string]", new object[0]);
            o.Val("LinkType", "string", null);
            o.Prop("Name", "string", () => f.Parent == null ? full : f.Name);
            if (f.IsDir)
            {
                o.Prop("FullName", "string", () => f.FullPath);
                o.Prop("Parent", "System.IO.DirectoryInfo", () => f.Parent == null ? null : FileItem(R, f.Parent, null, false));
                o.Prop("Exists", "bool", () => m.FindFile(f.FullPath) == f);
                o.Prop("Root", "System.IO.DirectoryInfo", () => FileItem(R, m.C, null, false));
            }
            else
            {
                o.Prop("Length", "long", () => f.Length);
                o.Prop("DirectoryName", "string", () => f.Parent.FullPath);
                o.Prop("Directory", "System.IO.DirectoryInfo", () => FileItem(R, f.Parent, null, false));
                o.Prop("IsReadOnly", "bool", () => f.ReadOnly, v => { if (PsConv.ToBool(v)) f.Attr |= 1; else f.Attr &= ~1; });
                o.Prop("Exists", "bool", () => m.FindFile(f.FullPath) == f);
                o.Prop("FullName", "string", () => f.FullPath);
            }
            o.Prop("Extension", "string", () => { int i = f.Name.LastIndexOf('.'); return i < 0 || f.Parent == null ? "" : f.Name.Substring(i); });
            o.Prop("CreationTime", "datetime", () => f.Created, v => f.Created = (DateTime)PsConv.To(v, "datetime"));
            o.Prop("CreationTimeUtc", "datetime", () => Utc(m, f.Created));
            o.Prop("LastAccessTime", "datetime", () => f.Accessed, v => f.Accessed = (DateTime)PsConv.To(v, "datetime"));
            o.Prop("LastAccessTimeUtc", "datetime", () => Utc(m, f.Accessed));
            o.Prop("LastWriteTime", "datetime", () => f.Modified, v => f.Modified = (DateTime)PsConv.To(v, "datetime"));
            o.Prop("LastWriteTimeUtc", "datetime", () => Utc(m, f.Modified));
            o.Prop("Attributes", "System.IO.FileAttributes", () => PsEnums.Get("System.IO.FileAttributes").Of(f.Attr), v => f.Attr = ParseAttributes(v, f.IsDir));
            o.Base = f;
            o.FileNameOnly = nameOnly;
            o.ToStr = x => x.FileNameOnly ? f.Name : f.FullPath;
            AddFileMethods(R, o, f);
            return o;
        }
        public static int ParseAttributes(object v, bool dir)
        {
            v = PsConv.Base(v);
            long n;
            if (v is PsEnum) n = ((PsEnum)v).Value;
            else if (PsConv.IsNumber(v)) n = PsConv.ToLong(v);
            else
            {
                var e = PsEnums.Get("System.IO.FileAttributes").Parse(PsConv.ToStr(v));
                if (e == null) throw new PsConvertError(v, "System.IO.FileAttributes", null);
                n = e.Value;
            }
            if (n == 128) n = 0;
            if (dir) n |= 16; else n &= ~16;
            return (int)n;
        }
        static PsObj VersionInfo(PsMachine m, WinFile f)
        {
            var o = new PsObj("System.Diagnostics.FileVersionInfo", "System.Object");
            bool exe = f.Bytes != null && (f.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || f.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
            string ver = exe ? m.OsVersion + ".1 (WinBuild.160101.0800)" : null;
            string pver = exe ? m.OsVersion + ".1" : null;
            o.Val("FileVersion", "string", ver); o.Val("ProductVersion", "string", pver); o.Val("FileName", "string", f.FullPath);
            o.Val("CompanyName", "string", exe ? "Microsoft Corporation" : null);
            o.Val("ProductName", "string", exe ? "Microsoft\u00ae Windows\u00ae Operating System" : null);
            o.Val("OriginalFilename", "string", exe ? f.Name.ToUpperInvariant() + ".MUI" : null);
            o.ToStr = x => "File:             " + f.FullPath + "\nInternalName:     " + (exe ? f.Name.Substring(0, f.Name.LastIndexOf('.')) : "") + "\nOriginalFilename: " + (exe ? f.Name.ToUpperInvariant() + ".MUI" : "") + "\nFileVersion:      " + (ver ?? "") +
                "\nFileDescription:  \nProduct:          " + (exe ? "Microsoft\u00ae Windows\u00ae Operating System" : "") + "\nProductVersion:   " + (pver ?? "") + "\nDebug:            False\nPatched:          False\nPreRelease:       False\nPrivateBuild:     False\nSpecialBuild:     False\nLanguage:         " + (exe ? "English (United States)" : "") + "\n";
            return o;
        }
        static PsRuntimeException MErr(PsRunspace R, string name, int n, string msg, string ex) { return PsMembers.MethodError(R, null, name, n, msg, ex); }
        static void AddFileMethods(PsRunspace R, PsObj o, WinFile f)
        {
            var m = R.M;
            o.Method("Delete", f.IsDir ? "void Delete(), void Delete(bool recursive)" : "void Delete()", (self, a) =>
            {
                if (m.FindFile(f.FullPath) != f) { if (f.IsDir) throw MErr(R, "Delete", a.Length, "Could not find a part of the path '" + f.FullPath + "'.", "System.IO.DirectoryNotFoundException"); return AutoNull.Value; }
                if (f.IsDir && f.Kids.Count > 0 && !(a.Length > 0 && PsConv.ToBool(a[0]))) throw MErr(R, "Delete", a.Length, "The directory is not empty.\r\n", "System.IO.IOException");
                if (f.ReadOnly) throw MErr(R, "Delete", a.Length, "Access to the path '" + f.FullPath + "' is denied.", "System.UnauthorizedAccessException");
                f.Parent.Kids.Remove(f.Name); f.Parent.Modified = m.Now;
                return AutoNull.Value;
            });
            o.Method("Refresh", "void Refresh()", (self, a) => AutoNull.Value);
            o.Method("ToString", "string ToString()", (self, a) => self.ToString());
            if (!f.IsDir)
            {
                o.Method("CopyTo", "System.IO.FileInfo CopyTo(string destFileName), System.IO.FileInfo CopyTo(string destFileName, bool overwrite)", (self, a) =>
                {
                    if (a.Length == 0) throw MErr(R, "CopyTo", 0, "Value cannot be null.", "System.ArgumentNullException");
                    string dst = R.FullFsPath(PsConv.ToStr(a[0]), R.ProcessDirectory);
                    var old = m.FindFile(dst);
                    if (old != null && !(a.Length > 1 && PsConv.ToBool(a[1]))) throw MErr(R, "CopyTo", a.Length, "The file '" + dst + "' already exists.", "System.IO.IOException");
                    var why = R.WriteFile(dst, f.Text, f.Encoding, false);
                    if (why != null) throw MErr(R, "CopyTo", a.Length, why, "System.IO.DirectoryNotFoundException");
                    return FileItem(R, m.FindFile(dst), null, false);
                });
                o.Method("MoveTo", "void MoveTo(string destFileName)", (self, a) =>
                {
                    string dst = R.FullFsPath(PsConv.ToStr(a.Length > 0 ? a[0] : ""), R.ProcessDirectory);
                    int i = dst.LastIndexOf('\\');
                    var dp = m.FindFile(i <= 2 ? dst.Substring(0, 3) : dst.Substring(0, i));
                    if (dp == null || !dp.IsDir) throw MErr(R, "MoveTo", a.Length, "Could not find a part of the path.", "System.IO.DirectoryNotFoundException");
                    if (dp.Kid(dst.Substring(i + 1)) != null) throw MErr(R, "MoveTo", a.Length, "Cannot create a file when that file already exists.\r\n", "System.IO.IOException");
                    f.Parent.Kids.Remove(f.Name); f.Name = dst.Substring(i + 1); dp.Add(f);
                    return AutoNull.Value;
                });
                o.Method("OpenText", "System.IO.StreamReader OpenText()", (self, a) => { throw MErr(R, "OpenText", 0, "Cert Lab's practice console does not simulate file streams; use Get-Content instead.", "System.NotSupportedException"); });
            }
            else
            {
                PsMethodFn list = null;
                Func<bool?, PsMethodFn> lister = kind => (self, a) =>
                {
                    string pat = a.Length > 0 ? PsConv.ToStr(a[0]) : "*";
                    bool deep = a.Length > 1 && PsConv.ToStr(a[1]).Equals("AllDirectories", StringComparison.OrdinalIgnoreCase);
                    var res = new List<object>();
                    Action<WinFile> walk = null;
                    walk = dir =>
                    {
                        foreach (var k in dir.Kids.Values.OrderBy(k => k.Name, StringComparer.OrdinalIgnoreCase))
                            if ((kind == null || k.IsDir == kind.Value) && PsConv.Like(k.Name, pat, false)) res.Add(FileItem(R, k, null, true));
                        if (deep) foreach (var k in dir.Kids.Values.Where(k => k.IsDir).OrderBy(k => k.Name, StringComparer.OrdinalIgnoreCase)) walk(k);
                    };
                    walk(f);
                    return res.ToArray();
                };
                list = lister(false);
                o.Method("GetFiles", "System.IO.FileInfo[] GetFiles(), System.IO.FileInfo[] GetFiles(string searchPattern), System.IO.FileInfo[] GetFiles(string searchPattern, System.IO.SearchOption searchOption)", list);
                o.Method("GetDirectories", "System.IO.DirectoryInfo[] GetDirectories(), System.IO.DirectoryInfo[] GetDirectories(string searchPattern)", lister(true));
                o.Method("GetFileSystemInfos", "System.IO.FileSystemInfo[] GetFileSystemInfos(), System.IO.FileSystemInfo[] GetFileSystemInfos(string searchPattern)", lister(null));
                o.Method("EnumerateFiles", "System.Collections.Generic.IEnumerable[System.IO.FileInfo] EnumerateFiles()", list);
                o.Method("Create", "void Create()", (self, a) => { m.MakeDir(f.FullPath, m.Now); return AutoNull.Value; });
                o.Method("CreateSubdirectory", "System.IO.DirectoryInfo CreateSubdirectory(string path)", (self, a) => FileItem(R, m.MakeDir(f.FullPath.TrimEnd('\\') + "\\" + PsConv.ToStr(a.Length > 0 ? a[0] : ""), m.Now), null, false));
            }
        }

        // ------------------------------------------------------------ registry
        /// <summary>A value as Get-ItemProperty gives it: ExpandString expanded, DWord as int, MultiString as string[]...</summary>
        public static object RegData(PsRunspace R, RegValue v)
        {
            if (v.Data == null) return null;
            if (v.Kind == "ExpandString" && R != null) return R.ExpandEnv(PsConv.ToStr(v.Data));
            return v.Data;
        }
        public static string RegValueName(RegValue v) { return v.Name.Length == 0 ? "(default)" : v.Name; }
        public static PsObj KeyItem(PsRunspace R, RegKey k, string path, PsDrive d)
        {
            var rp = Get("Registry");
            if (d == null || !d.Provider.Equals("Registry", StringComparison.OrdinalIgnoreCase)) d = DriveFor(R, rp, path);
            var o = new PsObj("Microsoft.Win32.RegistryKey", "System.MarshalByRefObject", "System.Object");
            o.Prop("Property", "string[]", () => k.Values.Select(v => (object)RegValueName(v)).ToArray());
            AddNotes(o, rp, d, path, rp.ParentOf(path), rp.LeafOf(path), true);
            o.Prop("SubKeyCount", "int", () => k.Kids.Count);
            o.Val("View", "Microsoft.Win32.RegistryView", "Default");
            o.Val("Handle", "Microsoft.Win32.SafeHandles.SafeRegistryHandle", "Microsoft.Win32.SafeHandles.SafeRegistryHandle");
            o.Prop("ValueCount", "int", () => k.Values.Count);
            o.Val("Name", "string", path);
            o.Base = k;
            if (R != null) owners.Add(o, R);
            o.ToStr = x => path;
            o.Method("GetValue", "System.Object GetValue(string name), System.Object GetValue(string name, System.Object defaultValue)", (self, a) =>
            {
                var v = k.Value(a.Length > 0 ? PsConv.ToStr(a[0]) : "");
                return v == null ? (a.Length > 1 ? a[1] : null) : RegData(R, v);
            });
            o.Method("GetValueNames", "string[] GetValueNames()", (self, a) => k.Values.Select(v => (object)v.Name).ToArray());
            o.Method("GetSubKeyNames", "string[] GetSubKeyNames()", (self, a) => k.Kids.Values.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Select(c => (object)c.Name).ToArray());
            o.Method("GetValueKind", "Microsoft.Win32.RegistryValueKind GetValueKind(string name)", (self, a) =>
            {
                var v = k.Value(a.Length > 0 ? PsConv.ToStr(a[0]) : "");
                if (v == null) throw MErr(R, "GetValueKind", a.Length, "The specified registry key does not exist.", "System.IO.IOException");
                return PsEnums.V("Microsoft.Win32.RegistryValueKind", v.Kind);
            });
            o.Method("OpenSubKey", "Microsoft.Win32.RegistryKey OpenSubKey(string name), Microsoft.Win32.RegistryKey OpenSubKey(string name, bool writable)", (self, a) =>
            {
                string sub = a.Length > 0 ? PsConv.ToStr(a[0]) : "";
                var c = k.Find(sub);
                return c == null ? null : KeyItem(R, c, path + "\\" + sub.Trim('\\'), d);
            });
            o.Method("Close", "void Close()", (self, a) => AutoNull.Value);
            o.Method("Dispose", "void Dispose()", (self, a) => AutoNull.Value);
            o.Method("ToString", "string ToString()", (self, a) => path);
            return o;
        }
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PsObj, PsRunspace> owners = new System.Runtime.CompilerServices.ConditionalWeakTable<PsObj, PsRunspace>();
        /// <summary>The session a registry key object came from (its listing expands %variables% with that session's environment).</summary>
        public static PsRunspace OwnerOf(PsObj o) { PsRunspace r; return o != null && owners.TryGetValue(o, out r) ? r : null; }
        /// <summary>The Property column of a registry listing: "Name : value" lines like Format-List.</summary>
        public static string KeyPropertyText(PsRunspace R, RegKey k)
        {
            if (k.Values.Count == 0) return "";
            int w = k.Values.Max(v => RegValueName(v).Length);
            return string.Join("\n", k.Values.Select(v => RegValueName(v).PadRight(w) + " : " + PsFormat.Cell(R, RegData(R, v))).ToArray());
        }

        // ------------------------------------------------------------ certificates
        static readonly Dictionary<string, string> ekuOids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Server Authentication", "1.3.6.1.5.5.7.3.1" }, { "Client Authentication", "1.3.6.1.5.5.7.3.2" }, { "Code Signing", "1.3.6.1.5.5.7.3.3" },
            { "Secure Email", "1.3.6.1.5.5.7.3.4" }, { "Time Stamping", "1.3.6.1.5.5.7.3.8" }, { "Document Encryption", "1.3.6.1.4.1.311.80.1" },
        };
        /// <summary>A certificate object (X509Certificate2) for a certificate in a store, as Cert:\ shows it.</summary>
        public static PsObj CertItem(PsRunspace R, WinCert c, PsDrive d)
        {
            var cp = Get("Certificate");
            if (d == null && R != null) d = R.Drives.FirstOrDefault(x => x.Provider == "Certificate");
            var o = new PsObj("System.Security.Cryptography.X509Certificates.X509Certificate2", "System.Security.Cryptography.X509Certificates.X509Certificate", "System.Object");
            AddNotes(o, cp, d, c.Store + "\\" + c.Thumbprint, c.Store, c.Thumbprint, false);
            var ekus = (c.CodeSigning && !c.EnhancedKeyUsage.Contains("Code Signing") ? c.EnhancedKeyUsage.Concat(new[] { "Code Signing" }) : c.EnhancedKeyUsage).Select(n =>
            {
                string oid; ekuOids.TryGetValue(n, out oid);
                var e = new PsObj("Microsoft.PowerShell.Commands.EnhancedKeyUsageRepresentation", "System.Object");
                e.Val("FriendlyName", "string", n); e.Val("ObjectId", "string", oid ?? "");
                e.ToStr = y => n + " (" + (oid ?? "") + ")";
                return (object)e;
            }).ToArray();
            o.Prop("EnhancedKeyUsageList", "System.Collections.Generic.List[Microsoft.PowerShell.Commands.EnhancedKeyUsageRepresentation]", () => ekus);
            o.Prop("DnsNameList", "System.Collections.Generic.List[Microsoft.PowerShell.Commands.DnsNameRepresentation]", () => { var m = Regex.Match(c.Subject, "CN=([^,]+)"); return m.Success ? new object[] { m.Groups[1].Value.Trim() } : new object[0]; });
            o.Val("SendAsTrustedIssuer", "bool", false);
            o.Val("Archived", "bool", false);
            o.Prop("Extensions", "System.Security.Cryptography.X509Certificates.X509ExtensionCollection", () => new object[] { "System.Security.Cryptography.Oid", "System.Security.Cryptography.Oid", "System.Security.Cryptography.Oid", "System.Security.Cryptography.Oid" });
            o.Prop("FriendlyName", "string", () => c.FriendlyName, v => c.FriendlyName = PsConv.ToStr(v));
            o.Prop("IssuerName", "System.Security.Cryptography.X509Certificates.X500DistinguishedName", () => c.Issuer);
            o.Prop("NotAfter", "datetime", () => c.NotAfter);
            o.Prop("NotBefore", "datetime", () => c.NotBefore);
            o.Prop("HasPrivateKey", "bool", () => c.HasPrivateKey);
            o.Prop("SerialNumber", "string", () => SerialOf(c));
            o.Prop("SubjectName", "System.Security.Cryptography.X509Certificates.X500DistinguishedName", () => c.Subject);
            o.Val("SignatureAlgorithm", "System.Security.Cryptography.Oid", "sha256RSA");
            o.Prop("Thumbprint", "string", () => c.Thumbprint);
            o.Val("Version", "int", 3);
            o.Prop("Issuer", "string", () => c.Issuer);
            o.Prop("Subject", "string", () => c.Subject);
            o.Base = c;
            o.ToStr = x => "[Subject]\r\n  " + c.Subject + "\r\n\r\n[Issuer]\r\n  " + c.Issuer + "\r\n\r\n[Serial Number]\r\n  " + SerialOf(c) + "\r\n\r\n[Not Before]\r\n  " + c.NotBefore.ToString("M/d/yyyy h:mm:ss tt", PsConv.Us) +
                "\r\n\r\n[Not After]\r\n  " + c.NotAfter.ToString("M/d/yyyy h:mm:ss tt", PsConv.Us) + "\r\n\r\n[Thumbprint]\r\n  " + c.Thumbprint + "\r\n";
            o.Method("Verify", "bool Verify()", (self, a) => c.NotAfter > R.M.Now && R.M.Certs.Any(r => r.Store.EndsWith("\\Root", StringComparison.OrdinalIgnoreCase) && r.Subject == c.Issuer));
            o.Method("GetNameInfo", "string GetNameInfo(System.Security.Cryptography.X509Certificates.X509NameType nameType, bool forIssuer)", (self, a) => { var m = Regex.Match(a.Length > 1 && PsConv.ToBool(a[1]) ? c.Issuer : c.Subject, "CN=([^,]+)"); return m.Success ? m.Groups[1].Value : ""; });
            return o;
        }
        static string SerialOf(WinCert c)
        {
            var sb = new StringBuilder();
            foreach (var ch in (c.Thumbprint + c.Subject).ToCharArray()) sb.Append(((int)ch * 7 % 16).ToString("X"));
            return sb.ToString().Substring(0, 32);
        }

        // ------------------------------------------------------------ content bytes (Get-FileHash, -Encoding Byte)
        static readonly Encoding Ansi = Windows1252();
        /// <summary>Windows-1252, what 5.1 calls "Default" here. .NET 10 has it only through the code pages provider
        /// (part of the framework, registered once).</summary>
        static Encoding Windows1252()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1252);
        }
        public static byte[] FileBytes(WinFile f)
        {
            if (f.Bytes != null) return f.Bytes.Length > 0 ? f.Bytes : Ansi.GetBytes(f.Name + "|" + f.Length + "|simulated program");
            string t = f.Text ?? "";
            switch ((f.Encoding ?? "ascii").ToLowerInvariant())
            {
                case "unicode": return new byte[] { 0xFF, 0xFE }.Concat(Encoding.Unicode.GetBytes(t)).ToArray();
                case "bigendianunicode": return new byte[] { 0xFE, 0xFF }.Concat(Encoding.BigEndianUnicode.GetBytes(t)).ToArray();
                case "utf32": return new byte[] { 0xFF, 0xFE, 0, 0 }.Concat(Encoding.UTF32.GetBytes(t)).ToArray();
                case "utf8bom": return new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes(t)).ToArray();
                case "utf8": return Encoding.UTF8.GetBytes(t);
                default: return Ansi.GetBytes(t);
            }
        }
        /// <summary>Text read the way Windows PowerShell 5.1 reads it: a byte order mark wins; UTF-8 without one is read as ANSI unless -Encoding UTF8.</summary>
        public static string ReadAs(WinFile f, string encoding)
        {
            if (PsMachine.ReadDenied(f.FullPath)) throw new ProvError("Access to the path '" + f.FullPath + "' is denied.", "UnauthorizedAccessError", "PermissionDenied", f.FullPath, "System.UnauthorizedAccessException");
            string t = f.Text ?? "";
            string fe = (f.Encoding ?? "ascii").ToLowerInvariant();
            string want = (encoding ?? "default").ToLowerInvariant();
            if (fe == "utf8" && want != "utf8" && t.Any(ch => ch > 127)) return Ansi.GetString(Encoding.UTF8.GetBytes(t));
            if (fe == "ascii" && want == "ascii") return new string(t.Select(ch => ch > 127 ? '?' : ch).ToArray());
            return t;
        }
        public static string Latin(byte[] b) { return Ansi.GetString(b); }
    }

    public partial class PsCmdlets
    {
        internal static PsObj VarItem(PsVariable v) { return VarInfo(v); }
        internal static IEnumerable<PsVariable> VisibleVars(PsScope sc) { return AllVars(sc); }
    }

    public partial class PsMachine
    {
        bool labCertsSeeded;
        /// <summary>The certificates the course starts with (trusted roots, the lab's CA, this server's own certificate).</summary>
        public void EnsureLabCerts()
        {
            if (labCertsSeeded) return;
            labCertsSeeded = true;
            Action<string, string, string, string, DateTime, DateTime, bool, string[]> add = (store, subject, issuer, thumb, nb, na, key, eku) =>
            {
                if (Certs.Any(k => k.Thumbprint.Equals(thumb, StringComparison.OrdinalIgnoreCase) && k.Store.Equals(store, StringComparison.OrdinalIgnoreCase))) return;
                var cert = new WinCert { Store = store, Subject = subject, Issuer = issuer, Thumbprint = thumb, NotBefore = nb, NotAfter = na, HasPrivateKey = key };
                cert.EnhancedKeyUsage.AddRange(eku);
                Certs.Add(cert);
            };
            string msRoot = "CN=Microsoft Root Certificate Authority 2011, O=Microsoft Corporation, L=Redmond, S=Washington, C=US";
            string msRoot10 = "CN=Microsoft Root Certificate Authority 2010, O=Microsoft Corporation, L=Redmond, S=Washington, C=US";
            string digi = "CN=DigiCert Global Root G2, OU=www.digicert.com, O=DigiCert Inc, C=US";
            string labCa = "CN=lab-DC01-CA, DC=lab, DC=example, DC=com";
            foreach (var root in new[] { "LocalMachine\\Root", "CurrentUser\\Root" })
            {
                add(root, msRoot10, msRoot10, "3B1EFD3A66EA28B16697394703A72CA340A05BD5", new DateTime(2010, 6, 23, 15, 57, 24), new DateTime(2035, 6, 23, 16, 4, 1), false, new string[0]);
                add(root, msRoot, msRoot, "8F43288AD272F3103B6FB1428485EA3014C0BCFE", new DateTime(2011, 3, 22, 15, 5, 28), new DateTime(2036, 3, 22, 15, 13, 4), false, new string[0]);
                add(root, digi, digi, "DF3C24F9BFD666761B268073FE06D1CC8D4F82A4", new DateTime(2013, 8, 1, 5, 0, 0), new DateTime(2038, 1, 15, 4, 0, 0), false, new string[0]);
                add(root, labCa, labCa, "6F1E2D3C4B5A69788796A5B4C3D2E1F00F1E2D3C", new DateTime(2026, 1, 5, 8, 0, 0), new DateTime(2036, 1, 5, 8, 10, 0), false, new string[0]);
            }
            add("LocalMachine\\CA", "CN=Microsoft Windows Production PCA 2011, O=Microsoft Corporation, L=Redmond, S=Washington, C=US", msRoot10, "580A6F4CC4E4B669B9EBDC1B2B3E087B80D0678D", new DateTime(2011, 10, 19, 11, 41, 42), new DateTime(2026, 10, 19, 11, 51, 42), false, new string[0]);
            string host = Name + "." + Domain;
            string own = IsServer ? "A3C5E7F90B1D3F5A7C9E1B3D5F7A9C1E3B5D7F90" : "B4D6F80A1C3E5A7C9E1B3D5F7A9C1E3B5D7F9A2C";
            add("LocalMachine\\My", "CN=" + host, labCa, own, InstallDate.AddDays(2), InstallDate.AddDays(2).AddYears(1), true, new[] { "Client Authentication", "Server Authentication" });
        }
    }

    public partial class PsRunspace
    {
        List<PsDrive> drives;
        PsDrive curDrive;
        /// <summary>The session's drives (New-PSDrive adds to them; they end with the session).</summary>
        public List<PsDrive> Drives
        {
            get
            {
                if (drives != null) return drives;
                drives = new List<PsDrive>();
                Func<string, string, string, PsDrive> d = (n, p, r) => { var x = new PsDrive { Name = n, Provider = p, Root = r, BuiltIn = true }; drives.Add(x); return x; };
                d("Alias", "Alias", "").Description = "Drive containing a view of the aliases stored in session state.";
                var c = d("C", "FileSystem", "C:\\");
                c.Description = "";
                d("Cert", "Certificate", "\\").Description = "X509 Certificate Provider";
                foreach (var x in M.ExtraDrives.Keys.OrderBy(k => k)) d(x, "FileSystem", x + ":\\").Description = "";
                d("Env", "Environment", "").Description = "Drive containing a view of the environment variables for the process.";
                d("Function", "Function", "").Description = "Drive containing a view of the functions stored in session state.";
                d("HKCU", "Registry", "HKEY_CURRENT_USER").Description = "The software settings for the current user";
                d("HKLM", "Registry", "HKEY_LOCAL_MACHINE").Description = "The configuration settings for the local computer";
                d("Variable", "Variable", "").Description = "Drive containing a view of the variables stored in session state.";
                d("WSMan", "WSMan", "").Description = "Root of WsManProvider";
                var loc = (Location ?? "C:\\Windows\\system32");
                if (loc.Length >= 3 && loc[1] == ':') c.Current = loc.Substring(3).Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                curDrive = c;
                return drives;
            }
        }
        public PsDrive FindDrive(string name) { return Drives.FirstOrDefault(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); }
        public PsDrive CurrentDrive { get { var x = Drives; return curDrive ?? x.First(d => d.Name == "C"); } }
        public PsProvider ProviderOf(PsDrive d) { return PsProviders.Get(d.Provider); }
        public ProvCtx Ctx(PsScope sc, bool force) { return new ProvCtx { R = this, Scope = sc ?? Global, Force = force }; }

        /// <summary>The current location as a parsed path.</summary>
        public PPath CurrentPPath
        {
            get
            {
                var d = CurrentDrive; var p = ProviderOf(d);
                var r = new PPath { P = p, Drive = d, Root = DriveRootPath(d, p), Typed = "." };
                r.Segs.AddRange(d.Current);
                return r;
            }
        }
        static string DriveRootPath(PsDrive d, PsProvider p)
        {
            if (p is CertProvider) return "";
            return d.Root;
        }

        /// <summary>Parses a typed path into provider, drive and parts (no wildcard expansion; nothing must exist).</summary>
        public PPath ParsePath(string typed)
        {
            string s = (typed ?? "").Trim();
            var r = new PPath { Typed = typed ?? "" };
            var m = Regex.Match(s, "^(?:([A-Za-z][\\w.]*)\\\\)?([A-Za-z]\\w*)::(.*)$");
            if (m.Success)
            {
                var p = PsProviders.Get(m.Groups[1].Success && m.Groups[1].Length > 0 ? m.Groups[1].Value + "\\" + m.Groups[2].Value : m.Groups[2].Value);
                if (p == null) throw new ProvError("Cannot find a provider with the name '" + m.Groups[2].Value + "'.", "ProviderNotFound", "ObjectNotFound", m.Groups[2].Value, "System.Management.Automation.ProviderNotFoundException");
                r.P = p;
                var tmp = new PPath { P = p };
                p.SplitProviderPath(m.Groups[3].Value, tmp);
                r.Root = tmp.Root;
                r.Segs = Normalize(tmp.Segs);
                // show it through a drive when one covers it (C:\..., HKLM:\...)
                var d = PsProviders.DriveFor(this, p, r.Path);
                if (d != null && !(p is FlatProvider))
                {
                    var rel = RelativeTo(d, p, r.Path);
                    if (rel != null) { r.Drive = d; r.Root = DriveRootPath(d, p); r.Segs = rel; }
                }
                else if (p is FlatProvider) r.Drive = Drives.FirstOrDefault(x => x.Provider == p.Name);
                return r;
            }
            if (s == "~" || s.StartsWith("~\\", StringComparison.Ordinal) || s.StartsWith("~/", StringComparison.Ordinal)) s = Home + s.Substring(1);
            PsDrive drive; string rest;
            var dm = Regex.Match(s, "^([^\\\\/:*?\"<>|]+):(.*)$", RegexOptions.Singleline);
            if (dm.Success && !dm.Groups[1].Value.Contains(" ") || dm.Success && FindDrive(dm.Groups[1].Value) != null)
            {
                drive = FindDrive(dm.Groups[1].Value);
                if (drive == null) throw ProvError.NoDrive(dm.Groups[1].Value);
                rest = dm.Groups[2].Value;
            }
            else { drive = CurrentDrive; rest = s; }
            var prov = ProviderOf(drive);
            if (prov == null) throw new ProvError("Cannot find a provider with the name '" + drive.Provider + "'.", "ProviderNotFound", "ObjectNotFound", drive.Provider, "System.Management.Automation.ProviderNotFoundException");
            r.P = prov; r.Drive = drive; r.Root = DriveRootPath(drive, prov);
            var segs = new List<string>();
            bool fromRoot = rest.StartsWith("\\", StringComparison.Ordinal) || rest.StartsWith("/", StringComparison.Ordinal);
            if (!fromRoot) segs.AddRange(drive.Current);
            segs.AddRange(rest.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries));
            r.Segs = Normalize(segs);
            return r;
        }
        static List<string> Normalize(List<string> segs)
        {
            var l = new List<string>();
            foreach (var x in segs)
            {
                if (x == ".") continue;
                if (x == "..") { if (l.Count > 0) l.RemoveAt(l.Count - 1); continue; }
                l.Add(x);
            }
            return l;
        }
        static List<string> RelativeTo(PsDrive d, PsProvider p, string path)
        {
            string root = d.Root.TrimEnd('\\');
            if (p is CertProvider) root = "";
            if (root.Length == 0) return path.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (path.Equals(root, StringComparison.OrdinalIgnoreCase) || path.Equals(d.Root, StringComparison.OrdinalIgnoreCase)) return new List<string>();
            if (!path.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase)) return null;
            return path.Substring(root.Length + 1).Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        }
        /// <summary>A provider path seen through the best drive (for PSDrive and for showing paths).</summary>
        public PPath FromProviderPath(PsProvider p, string path)
        {
            var r = new PPath { P = p };
            var d = PsProviders.DriveFor(this, p, path);
            var rel = d == null ? null : RelativeTo(d, p, path);
            if (d != null && rel != null) { r.Drive = d; r.Root = DriveRootPath(d, p); r.Segs = rel; }
            else { var tmp = new PPath { P = p }; p.SplitProviderPath(path, tmp); r.Root = tmp.Root; r.Segs = tmp.Segs; }
            return r;
        }

        /// <summary>Expands the wildcards of a path into the items that exist (hidden ones only with force).</summary>
        public List<PPath> Expand(PPath p, ProvCtx x)
        {
            var cur = new List<PPath> { new PPath { P = p.P, Drive = p.Drive, Root = p.Root, Typed = p.Typed } };
            foreach (var seg in p.Segs)
            {
                var next = new List<PPath>();
                if (!PsConv.HasWildcard(seg)) { foreach (var c in cur) next.Add(c.Child(seg)); cur = next; continue; }
                var rx = PsConv.Wildcard(seg, false);
                foreach (var c in cur)
                {
                    var node = p.P.Find(x, c.Path);
                    if (node == null || !p.P.IsContainer(node)) continue;
                    foreach (var kid in p.P.Kids(x, c.Path, node))
                        if ((x.Force || !p.P.IsHidden(kid.Value)) && rx.IsMatch(kid.Key)) next.Add(c.Child(kid.Key));
                }
                cur = next;
            }
            return cur.Where(c => p.P.Find(x, c.Path) != null).ToList();
        }

        /// <summary>Paths from -Path (wildcards expanded) or -LiteralPath. Errors (no drive, not found) go to onError.</summary>
        public List<PPath> ResolvePaths(IEnumerable<string> typed, bool literal, ProvCtx x, Action<ProvError> onError, bool mustExist)
        {
            var res = new List<PPath>();
            foreach (var t in typed)
            {
                PPath p;
                try { p = ParsePath(t); }
                catch (ProvError e) { onError(e); continue; }
                if (!literal && p.HasWild) { res.AddRange(Expand(p, x)); continue; }
                if (!literal) p = Unescape(p);
                if (mustExist && SafeFind(p, x) == null) { onError(ProvError.NotFound(p.P.ShownPath(p))); continue; }
                res.Add(p);
            }
            return res;
        }
        static PPath Unescape(PPath p)
        {
            if (!p.Segs.Any(s => s.Contains("`"))) return p;
            var c = new PPath { P = p.P, Drive = p.Drive, Root = p.Root, Typed = p.Typed };
            foreach (var s in p.Segs) c.Segs.Add(Regex.Replace(s, "`(.)", "$1"));
            return c;
        }
        public object SafeFind(PPath p, ProvCtx x) { return p.P.Find(x, p.Path); }
        public PsObj ItemOf(PPath p, ProvCtx x) { var n = p.P.Find(x, p.Path); return n == null ? null : p.P.Item(x, p.Path, n, p.Drive); }

        // ------------------------------------------------------------ the location
        /// <summary>Moves the session to a path (Set-Location): the drive's current location and the prompt follow.</summary>
        public void GoTo(PPath p)
        {
            if (p.Drive == null) throw new ProvError("Cert Lab's practice console can only change location through a drive (for example C:\\ or HKLM:\\).", "CertLabNotSimulated", "NotImplemented", p.Typed, "System.Management.Automation.PSNotSupportedException");
            var dummy = Drives;
            p.Drive.Current = p.Segs.ToList();
            curDrive = p.Drive;
            string loc = p.Drive.Name + ":\\" + string.Join("\\", p.Segs.ToArray());
            Location = loc;
            if (p.P is FsProvider) lastFsDir = p.Path;
        }
        /// <summary>Sets the location from a drive-qualified path string (kept for older callers).</summary>
        public void SetLocation(string loc)
        {
            try { var p = ParsePath(loc); GoTo(p); }
            catch (ProvError) { Location = loc; }
        }
        /// <summary>A PathInfo object (Get-Location, Resolve-Path, Push-Location -PassThru).</summary>
        public PsObj PathInfo(PPath p)
        {
            var o = new PsObj("System.Management.Automation.PathInfo", "System.Object");
            string shown = p.Drive == null ? p.P.Qualified + "::" + p.Path : p.DrivePath;
            o.Val("Drive", "System.Management.Automation.PSDriveInfo", PsProviders.DriveObj(this, p.Drive));
            o.Val("Provider", "System.Management.Automation.ProviderInfo", PsProviders.ProviderObj(p.P));
            o.Val("ProviderPath", "string", p.Path);
            o.Val("Path", "string", shown);
            o.DefaultDisplay = new[] { "Path" };
            o.ToStr = x => shown;
            return o;
        }
        public readonly Dictionary<string, Stack<string>> NamedStacks = new Dictionary<string, Stack<string>>(StringComparer.OrdinalIgnoreCase);
        public string DefaultStackName = "default";

        internal void DefineFunctionAt(string name, object v, PsScope sc) { DefineFunctionValue(name, v, sc); }

        partial void InitProviders()
        {
            var x = Drives;
            M.EnsureLabCerts();
            var sb = new StringBuilder();
            sb.Append("function cd.. { Set-Location .. }\n");
            sb.Append("function cd\\ { Set-Location \\ }\n");
            foreach (var ch in "ABCDEFGHIJKLMNOPQRSTUVWXYZ") sb.Append("function " + ch + ": { Set-Location " + ch + ": }\n");
            sb.Append("function mkdir {\n<#\n.FORWARDHELPTARGETNAME New-Item\n.FORWARDHELPCATEGORY Cmdlet\n#>\n[CmdletBinding(DefaultParameterSetName='pathSet', SupportsShouldProcess=$true, SupportsTransactions=$true, ConfirmImpact='Medium')]\n" +
                "param(\n    [Parameter(ParameterSetName='nameSet', Position=0, ValueFromPipelineByPropertyName=$true)]\n    [Parameter(ParameterSetName='pathSet', Mandatory=$true, Position=0, ValueFromPipelineByPropertyName=$true)]\n    [System.String[]]\n    ${Path},\n\n" +
                "    [Parameter(ParameterSetName='nameSet', Mandatory=$true, ValueFromPipelineByPropertyName=$true)]\n    [AllowNull()]\n    [AllowEmptyString()]\n    [System.String]\n    ${Name},\n\n    [Parameter(ValueFromPipeline=$true, ValueFromPipelineByPropertyName=$true)]\n    [System.Object]\n    ${Value},\n\n" +
                "    [Switch]\n    ${Force}\n)\nprocess {\n    New-Item -ItemType Directory @PSBoundParameters\n}\n}\n");
            sb.Append("function help {\n[CmdletBinding(DefaultParameterSetName='AllUsersView')]\nparam(\n    [Parameter(Position=0, ValueFromPipelineByPropertyName=$true)]\n    [string]\n    ${Name},\n    [string]\n    ${Path},\n    [string[]]\n    ${Category},\n    [string[]]\n    ${Component},\n" +
                "    [string[]]\n    ${Functionality},\n    [string[]]\n    ${Role},\n    [switch]\n    ${Detailed},\n    [switch]\n    ${Full},\n    [switch]\n    ${Examples},\n    [string]\n    ${Parameter},\n    [switch]\n    ${Online},\n    [switch]\n    ${ShowWindow}\n)\nGet-Help @PSBoundParameters\n}\n");
            try
            {
                var ast = PsParser.Parse(sb.ToString(), "");
                var st = new Streams();
                ExecBlock(ast.Body, Global, st);
            }
            catch (PsParseError) { }
            catch (PsRuntimeException) { }
        }
    }
}
