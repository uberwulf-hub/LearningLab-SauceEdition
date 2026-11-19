// Cert Lab - a session's current location and the file system helpers the language needs: resolving paths,
// reading and writing files (redirection with > writes UTF-16 like Windows PowerShell 5.1), ${C:\file} and
// the [System.IO.File] / [System.IO.Directory] statics (which resolve relative paths against the process folder).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CertLab
{
    public partial class PsRunspace
    {
        public string Location;                       // C:\Windows\system32, HKLM:\SOFTWARE, Env:\ ...
        public readonly Stack<string> LocationStack = new Stack<string>();
        /// <summary>The folder relative file paths start from: the current location, or the last file system one while the location is HKLM:, Env:...</summary>
        public string CurrentFsDir { get { return lastFsDir; } }
        string lastFsDir = "C:\\Windows\\system32";

        /// <summary>An absolute file system path for a path typed relative to a folder (handles ., .., C:, \).</summary>
        public string FullFsPath(string p, string baseDir)
        {
            if (p == null) return null;
            p = p.Replace('/', '\\').Trim();
            if (p.StartsWith("Microsoft.PowerShell.Core\\FileSystem::", StringComparison.OrdinalIgnoreCase)) p = p.Substring(38);
            if (p.StartsWith("FileSystem::", StringComparison.OrdinalIgnoreCase)) p = p.Substring(12);
            if (p == "~" || p.StartsWith("~\\", StringComparison.Ordinal)) p = Home + p.Substring(1);
            // a drive made with New-PSDrive (Lab:\x) stands for its root folder
            int dc = p.IndexOf(':');
            if (dc > 1 && p.Substring(0, dc).All(char.IsLetterOrDigit)) { var d = FindDrive(p.Substring(0, dc)); if (d != null && d.Provider == "FileSystem") p = d.Root.TrimEnd('\\') + "\\" + p.Substring(dc + 1).TrimStart('\\'); }
            string full;
            if (p.Length >= 2 && p[1] == ':') full = p.Length == 2 ? (baseDir != null && baseDir.StartsWith(p, StringComparison.OrdinalIgnoreCase) ? baseDir : p + "\\") : (p[2] == '\\' ? p : p.Substring(0, 2) + "\\" + p.Substring(2));
            else if (p.StartsWith("\\", StringComparison.Ordinal)) full = "C:" + p;
            else full = (baseDir ?? "C:\\").TrimEnd('\\') + "\\" + p;
            var parts = new List<string>();
            foreach (var part in full.Split('\\'))
            {
                if (part.Length == 0 || part == ".") continue;
                if (part == "..") { if (parts.Count > 1) parts.RemoveAt(parts.Count - 1); continue; }
                parts.Add(part);
            }
            if (parts.Count == 0) return "C:\\";
            string drive = parts[0].ToUpperInvariant();
            if (parts.Count == 1) return drive + "\\";
            return drive + "\\" + string.Join("\\", parts.Skip(1).ToArray());
        }

        /// <summary>The file or folder at a path (relative to the current location), or null.</summary>
        public WinFile FsNode(string fullPath) { return fullPath == null ? null : M.FindFile(fullPath); }
        public WinFile FsAt(string path) { return FsNode(FullFsPath(path, CurrentFsDir)); }

        public string ReadFileText(string fullPath)
        {
            var f = FsNode(fullPath);
            if (f == null || f.IsDir) return null;
            f.Accessed = M.Now;
            return f.Text;
        }
        public List<string> ReadFileLines(string path, PsScope sc)
        {
            var t = ReadFileText(FullFsPath(path, CurrentFsDir));
            if (t == null) throw RtError(null, "Cannot find path '" + FullFsPath(path, CurrentFsDir) + "' because it does not exist.", "PathNotFound", "ObjectNotFound", "System.Management.Automation.ItemNotFoundException", path);
            return SplitLines(t);
        }
        public static List<string> SplitLines(string t)
        {
            var l = t.Replace("\r\n", "\n").Split('\n').ToList();
            if (l.Count > 0 && l[l.Count - 1].Length == 0) l.RemoveAt(l.Count - 1);
            return l;
        }

        /// <summary>Creates or replaces (or appends to) a file. Returns null, or why it could not.</summary>
        public string WriteFile(string fullPath, string text, string encoding, bool append)
        {
            if (M.Access != null && PsMachine.Acting != null && !M.Access(PsMachine.Acting, fullPath, true)) return "Access to the path '" + fullPath + "' is denied.";
            int i = fullPath.LastIndexOf('\\');
            var dir = FsNode(i <= 2 ? fullPath.Substring(0, 3) : fullPath.Substring(0, i));
            if (dir == null || !dir.IsDir) return "Could not find a part of the path '" + fullPath + "'.";
            string name = fullPath.Substring(i + 1);
            var f = dir.Kid(name);
            if (f != null && f.IsDir) return "Access to the path '" + fullPath + "' is denied.";
            if (f != null && f.ReadOnly) return "Access to the path '" + fullPath + "' is denied.";
            if (f == null) { f = dir.Add(new WinFile { Name = name, Created = M.Now, Encoding = encoding ?? "ascii" }); f.Text = ""; }
            else if (!append && encoding != null) f.Encoding = encoding;
            f.Text = append ? f.Text + text : text;
            f.Modified = M.Now; f.Accessed = M.Now; f.Attr |= 32;
            f.FakeSize = -1; f.Bytes = null;
            dir.Modified = M.Now;
            return null;
        }

        /// <summary>cmd > file: the output formatted as text, written as UTF-16 (Out-File's default in 5.1).</summary>
        public void RedirectToFile(string path, List<object> items, bool append, PsScope sc, PsAst at)
        {
            string full = FullFsPath(path, CurrentFsDir);
            var lines = PsFormat.ToLines(this, items, Width);
            var sb = new StringBuilder();
            foreach (var l in lines) sb.Append(l).Append("\r\n");
            var f = FsNode(full);
            string why = WriteFile(full, sb.ToString(), f != null && append ? f.Encoding : "unicode", append);
            if (why != null) { var r = MakeError(at, why, "FileOpenFailure", "OpenError", "System.IO.DirectoryNotFoundException", full); r.CommandName = "out-file"; r.FullCommandName = "Microsoft.PowerShell.Commands.OutFileCommand"; AddError(r); throw new PsRuntimeException(r, false); }
        }

        /// <summary>${C:\file.txt} reads the file's lines.</summary>
        public object ProviderGetContent(string path, PsScope sc, PsAst at)
        {
            var t = ReadFileText(FullFsPath(path, CurrentFsDir));
            if (t == null) return null;
            var l = SplitLines(t);
            return l.Count == 1 ? (object)l[0] : l.Cast<object>().ToArray();
        }
        public void ProviderSetContent(string path, object v, PsScope sc)
        {
            var sb = new StringBuilder();
            foreach (var x in PsConv.Items(v)) sb.Append(PsConv.ToStr(x)).Append("\r\n");
            WriteFile(FullFsPath(path, CurrentFsDir), sb.ToString(), "ascii", false);
        }

        /// <summary>[System.IO.File]::... and [System.IO.Directory]::... on the simulated disk.</summary>
        public object FileStatic(string type, string n, object[] args, PsAst at, out bool found)
        {
            found = true;
            string a0 = args.Length > 0 ? FullFsPath(PsConv.ToStr(args[0]), ProcessDirectory) : null;
            var node = a0 == null ? null : FsNode(a0);
            Func<string, PsRuntimeException> notFound = p => PsMembers.MethodError(this, at, n, args.Length, "Could not find file '" + p + "'.", "System.IO.FileNotFoundException");
            if (type == "System.IO.File")
            {
                switch (n)
                {
                    case "exists": return node != null && !node.IsDir;
                    case "readalltext": if (node == null || node.IsDir) throw notFound(a0); return node.Text;
                    case "readalllines": if (node == null || node.IsDir) throw notFound(a0); return SplitLines(node.Text).Cast<object>().ToArray();
                    case "writealltext": case "appendalltext":
                        { var why = WriteFile(a0, PsConv.ToStr(args.Length > 1 ? args[1] : ""), "utf8", n == "appendalltext"); if (why != null) throw PsMembers.MethodError(this, at, n, args.Length, why, "System.IO.DirectoryNotFoundException"); return AutoNull.Value; }
                    case "writealllines": case "appendalllines":
                        {
                            var sb = new StringBuilder();
                            foreach (var x in PsConv.Items(args.Length > 1 ? args[1] : null)) sb.Append(PsConv.ToStr(x)).Append("\r\n");
                            var why = WriteFile(a0, sb.ToString(), "utf8", n == "appendalllines");
                            if (why != null) throw PsMembers.MethodError(this, at, n, args.Length, why, "System.IO.DirectoryNotFoundException");
                            return AutoNull.Value;
                        }
                    case "delete": if (node != null && !node.IsDir) node.Parent.Kids.Remove(node.Name); return AutoNull.Value;
                    case "copy":
                        {
                            if (node == null || node.IsDir) throw notFound(a0);
                            string dst = FullFsPath(PsConv.ToStr(args[1]), ProcessDirectory);
                            var why = WriteFile(dst, node.Text, node.Encoding, false);
                            if (why != null) throw PsMembers.MethodError(this, at, n, args.Length, why, "System.IO.IOException");
                            return AutoNull.Value;
                        }
                }
            }
            else
            {
                switch (n)
                {
                    case "exists": return node != null && node.IsDir;
                    case "getcurrentdirectory": return ProcessDirectory;
                    case "setcurrentdirectory": ProcessDirectory = a0; return AutoNull.Value;
                    case "createdirectory": M.MakeDir(a0, M.Now); return AutoNull.Value;
                    case "getfiles": case "getdirectories":
                        if (node == null || !node.IsDir) throw PsMembers.MethodError(this, at, n, args.Length, "Could not find a part of the path '" + a0 + "'.", "System.IO.DirectoryNotFoundException");
                        return node.Kids.Values.Where(k => k.IsDir == (n == "getdirectories")).OrderBy(k => k.Name, StringComparer.OrdinalIgnoreCase).Select(k => (object)k.FullPath).ToArray();
                }
            }
            found = false;
            return null;
        }
    }
}
