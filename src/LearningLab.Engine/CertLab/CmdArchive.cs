// Cert Lab - archives and compression (tar with gzip / bzip2 / xz, gzip, bzip2, xz and their un- and cat
// forms, zip / unzip) and POSIX ACL tools (getfacl, setfacl). An archive keeps its members in the file's Blob:
// a kind line and one encoded line per member (path, type, mode, owner, time, label, ACL, contents).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CertLab
{
    public class ArEntry
    {
        public string Path = "";
        public char T = 'f';
        public int Mode = 0x1A4, Uid, Gid;
        public long Mtime;
        public string Ctx = "", Target = "";
        public string Text; public long BinSize = -1; public string Blob; public string Prog;
        public string Acl = "", DefAcl = "";
        public long Size { get { return T != 'f' ? 0 : Text != null ? Encoding.UTF8.GetByteCount(Text) : Math.Max(0, BinSize); } }

        static string E(string s) { return s == null ? "-" : "+" + Convert.ToBase64String(Encoding.UTF8.GetBytes(s)); }
        static string D(string s) { return s == "-" ? null : Encoding.UTF8.GetString(Convert.FromBase64String(s.Substring(1))); }
        public string Encode()
        {
            return string.Join(" ", new[] { E(Path), T.ToString(), Mode.ToString(CultureInfo.InvariantCulture), Uid.ToString(CultureInfo.InvariantCulture), Gid.ToString(CultureInfo.InvariantCulture), Mtime.ToString(CultureInfo.InvariantCulture), E(Ctx), E(Target), E(Text), BinSize.ToString(CultureInfo.InvariantCulture), E(Blob), E(Prog), E(Acl), E(DefAcl) });
        }
        public static ArEntry Decode(string line)
        {
            var p = line.Split(' ');
            if (p.Length < 14) return null;
            var e = new ArEntry { Path = D(p[0]), T = p[1][0], Ctx = D(p[6]) ?? "", Target = D(p[7]) ?? "", Text = D(p[8]), Blob = D(p[10]), Prog = D(p[11]), Acl = D(p[12]) ?? "", DefAcl = D(p[13]) ?? "" };
            int.TryParse(p[2], out e.Mode); int.TryParse(p[3], out e.Uid); int.TryParse(p[4], out e.Gid); long.TryParse(p[5], out e.Mtime); long.TryParse(p[9], out e.BinSize);
            return e;
        }
    }

    public static partial class Cmd
    {
        static void RegArchive()
        {
            P("tar", Tar);
            foreach (var z in new[] { "gzip", "gunzip", "zcat", "bzip2", "bunzip2", "bzcat", "xz", "unxz", "xzcat" }) P(z, Compress);
            P("zip", Zip); P("unzip", Unzip);
            P("getfacl", Getfacl); P("setfacl", Setfacl);
        }

        // ------------------------------------------------------------ the packed form
        static string Pack(string kind, IEnumerable<ArEntry> entries) { return kind + "\n" + string.Join("\n", entries.Select(e => e.Encode()).ToArray()); }
        static string KindOf(string blob) { int i = blob == null ? -1 : blob.IndexOf('\n'); return blob == null ? "" : i < 0 ? blob : blob.Substring(0, i); }
        static List<ArEntry> Unpack(string blob)
        {
            var l = new List<ArEntry>();
            if (blob == null) return l;
            foreach (var line in blob.Split('\n').Skip(1)) { if (line.Length == 0) continue; try { var e = ArEntry.Decode(line); if (e != null) l.Add(e); } catch (FormatException) { } }
            return l;
        }
        static long TarSize(List<ArEntry> es) { long s = 1024; foreach (var e in es) s += 512 + (e.Size + 511) / 512 * 512; return Math.Max(10240, (s + 10239) / 10240 * 10240); }
        static long Squeeze(long n, string kind) { double f = kind.EndsWith("xz", StringComparison.Ordinal) ? 0.18 : kind.EndsWith("bz2", StringComparison.Ordinal) ? 0.22 : 0.25; return Math.Max(kind.EndsWith("xz", StringComparison.Ordinal) ? 60 : 30, (long)(n * f) + 20); }

        static string ArchiveKind(string blob)
        {
            string k = KindOf(blob);
            if (k == "tar") return "POSIX tar archive (GNU)";
            if (k.EndsWith("gz", StringComparison.Ordinal)) return "gzip compressed data, from Unix, original size modulo 2^32 " + InnerSize(blob);
            if (k.EndsWith("bz2", StringComparison.Ordinal)) return "bzip2 compressed data, block size = 900k";
            if (k.EndsWith("xz", StringComparison.Ordinal)) return "XZ compressed data, checksum CRC64";
            if (k == "zip") return "Zip archive data, at least v1.0 to extract, compression method=store";
            if (k == "swap") return "Linux swap file, 4k page size, little endian, version 1";
            return "data";
        }
        static long InnerSize(string blob)
        {
            var es = Unpack(blob);
            return KindOf(blob).StartsWith("tar", StringComparison.Ordinal) ? TarSize(es) : es.Sum(e => e.Size);
        }

        static string AclText(List<AclEntry> l) { return l == null ? "" : string.Join(",", l.Select(e => e.Tag + ":" + e.Who + ":" + e.Perm).ToArray()); }
        static List<AclEntry> AclParse(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            return s.Split(',').Select(x => x.Split(':')).Where(p => p.Length == 3).Select(p => new AclEntry(p[0], p[1], int.Parse(p[2], CultureInfo.InvariantCulture))).ToList();
        }

        /// <summary>Reads a tree into archive entries (as the user: unreadable files are reported).</summary>
        static void Collect(Sh sh, Io io, string path, string name, List<ArEntry> into, List<string> excludes, string prog, ref int status)
        {
            Loc l;
            try { l = sh.M.V.Resolve(sh.P, path, false); }
            catch (VfsEx e) { io.E(prog + ": " + path + ": Cannot stat: " + e.Message); status = 2; return; }
            if (!l.Exists) { io.E(prog + ": " + path + ": Cannot stat: No such file or directory"); status = 2; return; }
            if (excludes.Any(x => Sh.GlobMatch(x, Vfs.Base(name)) || Sh.GlobMatch(x, name))) return;
            var n = l.Node;
            var e2 = new ArEntry { Path = name, T = n.T == 'd' ? 'd' : n.T == 'l' ? 'l' : 'f', Mode = n.Mode & 0xFFF, Uid = n.Uid, Gid = n.Gid, Mtime = n.Mtime, Ctx = n.Ctx ?? "", Target = n.Target ?? "", Acl = AclText(n.Acl), DefAcl = AclText(n.DefAcl) };
            if (n.T == 'f')
            {
                try { var t = sh.M.V.ReadNode(sh.P, l); e2.Text = n.Text == null ? null : t; e2.BinSize = n.BinSize; e2.Blob = n.Blob; e2.Prog = n.Prog; }
                catch (VfsEx e) { io.E(prog + ": " + path + ": Cannot open: " + e.Message); status = 2; return; }
            }
            if (n.T == 'b' || n.T == 'c' || n.T == 'p' || n.T == 's') return;
            into.Add(e2);
            if (n.T == 'd')
            {
                if (!sh.M.V.Can(sh.P.Cr, n, 5)) { io.E(prog + ": " + path + ": Cannot open: Permission denied"); status = 2; return; }
                foreach (var k in Vfs.Sorted(n.Kids.Keys)) Collect(sh, io, path.TrimEnd('/') + "/" + k, name.TrimEnd('/') + "/" + k, into, excludes, prog, ref status);
            }
        }

        /// <summary>Writes archive entries below a folder. Root keeps owners and modes; labels and ACLs only when asked.</summary>
        static bool Extract(Sh sh, Io io, ArEntry e, string dest, bool keepCtx, bool keepAcl, bool keepOld, string prog)
        {
            var m = sh.M;
            string path = dest.TrimEnd('/') + "/" + e.Path.TrimEnd('/');
            if (e.Path.StartsWith("/", StringComparison.Ordinal)) path = e.Path;
            bool root = sh.P.Cr.Uid == 0;
            try
            {
                // missing parents (archives made without the folder entries)
                string parent = Vfs.Parent(Vfs.Norm(Vfs.Join(sh.P.Cwd, path)));
                if (parent != "/") m.V.Mkdir(sh.P, parent, 0x1ED, true);
                var l = m.V.Resolve(sh.P, path, false);
                Inode n;
                if (e.T == 'd')
                {
                    n = l.Exists && l.Node.T == 'd' ? l.Node : m.V.Mkdir(sh.P, path, 0x1ED, false);
                }
                else if (e.T == 'l')
                {
                    if (l.Exists) { if (keepOld) { io.E(prog + ": " + e.Path + ": Cannot open: File exists"); return false; } m.V.Unlink(sh.P, l); l = m.V.Resolve(sh.P, path, false); }
                    n = m.V.Symlink(sh.P, e.Target, l);
                    return true;
                }
                else
                {
                    if (l.Exists && keepOld) { io.E(prog + ": " + e.Path + ": Cannot open: File exists"); return false; }
                    if (l.Exists && l.Node.T == 'd') { io.E(prog + ": " + e.Path + ": Cannot open: Is a directory"); return false; }
                    n = m.V.WriteLoc(sh.P, l, e.Text ?? "", false, 0x1B6);
                    if (e.Text == null) { n.Text = null; n.BinSize = e.BinSize; }
                    n.Blob = e.Blob; n.Prog = e.Prog;
                }
                if (root) { n.Uid = e.Uid; n.Gid = e.Gid; n.Mode = (n.Mode & ~0xFFF) | e.Mode; }
                else n.Mode = (n.Mode & ~0xFFF) | (e.Mode & ~sh.P.Umask & 0x1FF);
                n.Mtime = e.Mtime;
                if (keepCtx && e.Ctx.Length > 0 && m.SeMode != "disabled") n.Ctx = e.Ctx;
                if (keepAcl) { n.Acl = AclParse(e.Acl); n.DefAcl = AclParse(e.DefAcl); }
                return true;
            }
            catch (VfsEx ex) { io.E(prog + ": " + e.Path + ": Cannot open: " + ex.Message); return false; }
        }

        static string ListLine(Machine m, ArEntry e)
        {
            char t = e.T == 'd' ? 'd' : e.T == 'l' ? 'l' : '-';
            string mode = t + Vfs.Rwx((e.Mode >> 6) & 7) + Vfs.Rwx((e.Mode >> 3) & 7) + Vfs.Rwx(e.Mode & 7);
            var d = m.LocalTime(e.Mtime);
            return mode + " " + (m.UName(e.Uid) + "/" + m.GName(e.Gid)).PadRight(13) + " " + e.Size.ToString(CultureInfo.InvariantCulture).PadLeft(8) + " " + d.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " " + e.Path + (e.T == 'd' ? "/" : "") + (e.T == 'l' ? " -> " + e.Target : "");
        }

        // ------------------------------------------------------------ tar
        static int Tar(Sh sh, List<string> a, Io io)
        {
            var m = sh.M;
            var args = a.Skip(1).ToList();
            // old style: "tar cvzf x.tgz dir" (the first word is the letters)
            if (args.Count > 0 && !args[0].StartsWith("-", StringComparison.Ordinal) && Regex.IsMatch(args[0], "^[cxtruvzjJfpaPkC]+$")) args[0] = "-" + args[0];
            string mode = null, file = null, dir = null, comp = null;
            bool verbose = false, keepCtx = false, keepAcl = false, absolute = false, keepOld = false, preserve = false;
            var excludes = new List<string>();
            var names = new List<string>();
            for (int i = 0; i < args.Count; i++)
            {
                string s = args[i];
                if (s.StartsWith("--", StringComparison.Ordinal))
                {
                    string k = s, v = null; int eq = s.IndexOf('='); if (eq > 0) { k = s.Substring(0, eq); v = s.Substring(eq + 1); }
                    Func<string> next = () => v ?? (i + 1 < args.Count ? args[++i] : "");
                    switch (k)
                    {
                        case "--create": mode = "c"; break;
                        case "--extract": case "--get": mode = "x"; break;
                        case "--list": mode = "t"; break;
                        case "--append": mode = "r"; break;
                        case "--file": file = next(); break;
                        case "--directory": dir = next(); break;
                        case "--gzip": comp = "gz"; break;
                        case "--bzip2": comp = "bz2"; break;
                        case "--xz": comp = "xz"; break;
                        case "--auto-compress": comp = "auto"; break;
                        case "--verbose": verbose = true; break;
                        case "--selinux": keepCtx = true; break;
                        case "--acls": keepAcl = true; break;
                        case "--xattrs": keepCtx = true; break;
                        case "--no-selinux": case "--no-acls": case "--no-xattrs": case "--overwrite": case "--same-owner": case "--numeric-owner": break;
                        case "--preserve-permissions": preserve = true; break;
                        case "--absolute-names": absolute = true; break;
                        case "--keep-old-files": keepOld = true; break;
                        case "--exclude": excludes.Add(next()); break;
                        case "--help": io.L("Usage: tar [OPTION...] [FILE]...\nExamples:\n  tar -cf archive.tar foo bar  # Create archive.tar from files foo and bar.\n  tar -tvf archive.tar         # List all files in archive.tar verbosely.\n  tar -xf archive.tar          # Extract all files from archive.tar."); return 0;
                        default: io.E("tar: unrecognized option '" + s + "'\nTry 'tar --help' or 'tar --usage' for more information."); return 2;
                    }
                    continue;
                }
                if (s.StartsWith("-", StringComparison.Ordinal) && s.Length > 1)
                {
                    for (int k = 1; k < s.Length; k++)
                    {
                        char c = s[k];
                        switch (c)
                        {
                            case 'c': case 'x': case 't': case 'r': case 'u':
                                if (mode != null && mode != c.ToString()) { io.E("tar: You may not specify more than one '-Acdtrux', '--delete' or  '--test-label' option\nTry 'tar --help' or 'tar --usage' for more information."); return 2; }
                                mode = c == 'u' ? "r" : c.ToString(); break;
                            case 'v': verbose = true; break;
                            case 'z': comp = "gz"; break;
                            case 'j': comp = "bz2"; break;
                            case 'J': comp = "xz"; break;
                            case 'a': comp = "auto"; break;
                            case 'p': preserve = true; break;
                            case 'P': absolute = true; break;
                            case 'k': keepOld = true; break;
                            case 'f': case 'C':
                                {
                                    string v = s.Substring(k + 1);
                                    if (v.Length == 0) { if (i + 1 >= args.Count) { io.E("tar: option requires an argument -- '" + c + "'\nTry 'tar --help' or 'tar --usage' for more information."); return 2; } v = args[++i]; }
                                    if (c == 'f') file = v; else dir = v;
                                    k = s.Length;
                                    break;
                                }
                            default: io.E("tar: invalid option -- '" + c + "'\nTry 'tar --help' or 'tar --usage' for more information."); return 2;
                        }
                    }
                    continue;
                }
                names.Add(s);
            }
            if (mode == null) { io.E("tar: You must specify one of the '-Acdtrux', '--delete' or '--test-label' options\nTry 'tar --help' or 'tar --usage' for more information."); return 2; }
            if (file == null) { io.E("tar: Refusing to " + (mode == "c" ? "write archive contents to" : "read archive contents from") + " terminal (missing -f option?)\ntar: Error is not recoverable: exiting now"); return 2; }
            // -C applies to the files that follow; Cert Lab applies it to all of them (the usual way people use it)
            var saveCwd = sh.P.Cwd;
            string archivePath = Vfs.Norm(Vfs.Join(sh.P.Cwd, file));
            try
            {
                if (dir != null)
                {
                    var dl = m.V.Resolve(sh.P, dir, true);
                    if (!dl.Exists || !dl.IsDir) { io.E("tar: " + dir + ": Cannot open: No such file or directory\ntar: Error is not recoverable: exiting now"); return 2; }
                    sh.P.Cwd = Vfs.Norm(Vfs.Join(sh.P.Cwd, dir));
                }
                int status = 0;
                if (mode == "c" || mode == "r")
                {
                    if (names.Count == 0) { io.E("tar: Cowardly refusing to create an empty archive\nTry 'tar --help' or 'tar --usage' for more information."); return 2; }
                    var entries = new List<ArEntry>();
                    if (mode == "r")
                    {
                        try { var al = m.V.Resolve(sh.P, archivePath, true); if (al.Exists) { if (KindOf(al.Node.Blob) != "tar") { io.E("tar: Cannot update compressed archives\ntar: Error is not recoverable: exiting now"); return 2; } entries = Unpack(al.Node.Blob); } }
                        catch (VfsEx) { }
                    }
                    bool warned = false;
                    foreach (var nm in names)
                    {
                        string stored = nm;
                        if (!absolute && stored.StartsWith("/", StringComparison.Ordinal))
                        {
                            if (!warned) { io.E("tar: Removing leading `/' from member names"); warned = true; }
                            stored = stored.TrimStart('/');
                            if (stored.Length == 0) stored = ".";
                        }
                        stored = stored.Length > 1 ? stored.TrimEnd('/') : stored;
                        var before = entries.Count;
                        Collect(sh, io, nm, stored, entries, excludes, "tar", ref status);
                        if (verbose) foreach (var e in entries.Skip(before)) io.L(e.Path + (e.T == 'd' ? "/" : ""));
                    }
                    string kind = comp == "auto" ? (Regex.IsMatch(file, "\\.(tgz|tar\\.gz)$") ? "gz" : Regex.IsMatch(file, "\\.(tbz2?|tar\\.bz2)$") ? "bz2" : Regex.IsMatch(file, "\\.(txz|tar\\.xz)$") ? "xz" : null) : comp;
                    string k2 = kind == null ? "tar" : "tar." + kind;
                    sh.P.Cwd = saveCwd;
                    Inode an;
                    try { an = m.V.WriteText(sh.P, archivePath, "", false); }
                    catch (VfsEx e) { io.E("tar: " + file + ": Cannot open: " + e.Message + "\ntar: Error is not recoverable: exiting now"); return 2; }
                    an.Text = null; an.Blob = Pack(k2, entries);
                    long raw = TarSize(entries);
                    an.BinSize = kind == null ? raw : Squeeze(entries.Sum(x => x.Size) + 512 * entries.Count, kind);
                    if (status != 0) io.E("tar: Exiting with failure status due to previous errors");
                    return status;
                }
                // t, x: read the archive
                sh.P.Cwd = saveCwd;
                Loc lf;
                try { lf = m.V.Resolve(sh.P, archivePath, true); }
                catch (VfsEx e) { io.E("tar: " + file + ": Cannot open: " + e.Message + "\ntar: Error is not recoverable: exiting now"); return 2; }
                if (!lf.Exists) { io.E("tar: " + file + ": Cannot open: No such file or directory\ntar: Error is not recoverable: exiting now"); return 2; }
                try { m.V.ReadNode(sh.P, lf); } catch (VfsEx e) { io.E("tar: " + file + ": Cannot open: " + e.Message + "\ntar: Error is not recoverable: exiting now"); return 2; }
                string kd = KindOf(lf.Node.Blob);
                if (!kd.StartsWith("tar", StringComparison.Ordinal)) { io.E("tar: This does not look like a tar archive\ntar: Exiting with failure status due to previous errors"); return 2; }
                var es = Unpack(lf.Node.Blob);
                if (names.Count > 0)
                {
                    var want = names.Select(x => x.TrimEnd('/').TrimStart('/')).ToList();
                    var miss = want.Where(w => !es.Any(e => e.Path == w || e.Path.StartsWith(w + "/", StringComparison.Ordinal))).ToList();
                    es = es.Where(e => want.Any(w => e.Path == w || e.Path.StartsWith(w + "/", StringComparison.Ordinal))).ToList();
                    foreach (var x in miss) { io.E("tar: " + x + ": Not found in archive"); status = 2; }
                }
                if (dir != null) sh.P.Cwd = Vfs.Norm(Vfs.Join(saveCwd, dir));
                foreach (var e in es)
                {
                    if (mode == "t") { io.L(verbose ? ListLine(m, e) : e.Path + (e.T == 'd' ? "/" : "")); continue; }
                    if (verbose) io.L(e.Path + (e.T == 'd' ? "/" : ""));
                    if (!Extract(sh, io, e, sh.P.Cwd, keepCtx, keepAcl, keepOld, "tar")) status = 2;
                }
                if (status != 0) io.E("tar: Exiting with failure status due to previous errors");
                return status;
            }
            finally { sh.P.Cwd = saveCwd; }
        }

        // ------------------------------------------------------------ gzip, bzip2, xz
        static int Compress(Sh sh, List<string> a, Io io)
        {
            var m = sh.M;
            string prog = a[0];
            string fam = prog.Contains("gz") || prog == "gunzip" || prog == "zcat" ? "gz" : prog.Contains("bz") || prog == "bunzip2" ? "bz2" : "xz";
            string suffix = fam == "gz" ? ".gz" : fam == "bz2" ? ".bz2" : ".xz";
            string toolName = fam == "gz" ? "gzip" : fam == "bz2" ? "bzip2" : "xz";
            var o = Opts.Parse(a, "dckfvlrtq123456789T:", "decompress|d,stdout|c,keep|k,force|f,verbose|v,list|l,recursive|r,test|t,quiet|q,best|9,fast|1,threads=|T", prog);
            if (o.Error != null) return Fail(io, o);
            bool decomp = o.Has("d") || prog.StartsWith("gun", StringComparison.Ordinal) || prog.StartsWith("bun", StringComparison.Ordinal) || prog.StartsWith("unxz", StringComparison.Ordinal) || prog.EndsWith("cat", StringComparison.Ordinal);
            bool toOut = o.Has("c") || prog.EndsWith("cat", StringComparison.Ordinal);
            if (o.Args.Count == 0)
            {
                if (decomp && !io.In.IsTty) { io.E(prog + ": stdin: not in " + toolName + " format"); return 1; }
                io.E(prog + ": compressed data not written to a terminal. Use -f to force compression.\nFor help, type: " + prog + " -h");
                return 1;
            }
            int st = 0;
            foreach (var f in o.Args)
            {
                Loc l;
                try { l = m.V.Resolve(sh.P, f, true); }
                catch (VfsEx e) { io.E(prog + ": " + f + ": " + e.Message); st = 1; continue; }
                if (!l.Exists) { io.E(prog + ": " + f + ": No such file or directory"); st = 1; continue; }
                if (l.IsDir) { io.E(prog + ": " + f + " is a directory -- ignored"); st = 2; continue; }
                string text;
                try { text = m.V.ReadNode(sh.P, l); } catch (VfsEx e) { io.E(prog + ": " + f + ": " + e.Message); st = 1; continue; }
                var n = l.Node;
                if (o.Has("l") && fam == "gz")
                {
                    long inner = InnerSize(n.Blob);
                    io.L("         compressed        uncompressed  ratio uncompressed_name\n" + n.Size.ToString().PadLeft(19) + " " + inner.ToString().PadLeft(19) + "  " + (inner == 0 ? "0.0" : ((1 - (double)n.Size / inner) * 100).ToString("0.0", CultureInfo.InvariantCulture)) + "% " + (f.EndsWith(suffix, StringComparison.Ordinal) ? f.Substring(0, f.Length - suffix.Length) : f));
                    continue;
                }
                if (decomp)
                {
                    string k = KindOf(n.Blob);
                    if (!k.EndsWith(fam, StringComparison.Ordinal)) { io.E(prog + ": " + f + ": not in " + toolName + " format"); st = 1; continue; }
                    var inner = Unpack(n.Blob);
                    // a compressed tar becomes a plain tar; a compressed file becomes that file
                    ArEntry plain = k.StartsWith("tar", StringComparison.Ordinal) ? new ArEntry { Text = null, Blob = Pack("tar", inner), BinSize = TarSize(inner), Mode = n.Mode & 0xFFF } : inner.FirstOrDefault();
                    if (plain == null) { io.E(prog + ": " + f + ": unexpected end of file"); st = 1; continue; }
                    if (toOut) { io.P(plain.Blob != null || plain.Text == null ? "" : plain.Text); continue; }
                    if (!f.EndsWith(suffix, StringComparison.Ordinal) && !(fam == "gz" && f.EndsWith(".tgz", StringComparison.Ordinal))) { io.E(prog + ": " + f + ": unknown suffix -- ignored"); st = 2; continue; }
                    string outName = f.EndsWith(".tgz", StringComparison.Ordinal) ? f.Substring(0, f.Length - 4) + ".tar" : f.Substring(0, f.Length - suffix.Length);
                    try
                    {
                        var ol = m.V.Resolve(sh.P, outName, true);
                        if (ol.Exists && !o.Has("f")) { io.E(prog + ": " + outName + " already exists; not overwritten"); st = 2; continue; }
                        var on = m.V.WriteText(sh.P, outName, plain.Text ?? "", false);
                        if (plain.Text == null) { on.Text = null; on.BinSize = plain.BinSize; }
                        on.Blob = plain.Blob; on.Prog = plain.Prog;
                        on.Mode = (on.Mode & ~0xFFF) | (n.Mode & 0xFFF); on.Mtime = n.Mtime;
                        if (!o.Has("k")) m.V.Unlink(sh.P, m.V.Resolve(sh.P, f, false));
                        if (o.Has("v")) io.E(f + ":\t" + (inner.Sum(x => x.Size) == 0 ? "0.0" : "71.3") + "% -- replaced with " + outName);
                    }
                    catch (VfsEx e) { io.E(prog + ": " + outName + ": " + e.Message); st = 1; }
                    continue;
                }
                if (f.EndsWith(suffix, StringComparison.Ordinal)) { io.E(prog + ": " + f + " already has " + suffix + " suffix -- unchanged"); st = 2; continue; }
                var entry = new ArEntry { Path = Vfs.Base(f), Mode = n.Mode & 0xFFF, Uid = n.Uid, Gid = n.Gid, Mtime = n.Mtime, Text = n.Text == null ? null : text, BinSize = n.BinSize, Blob = n.Blob, Prog = n.Prog };
                string kind = KindOf(n.Blob) == "tar" ? "tar." + fam : fam;
                var list = KindOf(n.Blob) == "tar" ? Unpack(n.Blob) : new List<ArEntry> { entry };
                string blob = Pack(kind, list);
                long size = Squeeze(entry.Size, fam);
                if (toOut) { io.E("(Cert Lab: compressed data on the terminal is binary; redirect it to a file, e.g. " + prog + " -c " + f + " > " + f + suffix + ")"); continue; }
                try
                {
                    var ol = m.V.Resolve(sh.P, f + suffix, true);
                    if (ol.Exists && !o.Has("f")) { io.E(prog + ": " + f + suffix + " already exists; not overwritten"); st = 2; continue; }
                    var on = m.V.WriteText(sh.P, f + suffix, "", false);
                    on.Text = null; on.Blob = blob; on.BinSize = size;
                    on.Mode = (on.Mode & ~0xFFF) | (n.Mode & 0xFFF); on.Mtime = n.Mtime;
                    if (!o.Has("k")) m.V.Unlink(sh.P, m.V.Resolve(sh.P, f, false));
                    if (o.Has("v")) io.E(f + ":\t " + (entry.Size == 0 ? "0.0" : ((1 - (double)size / Math.Max(1, entry.Size)) * 100).ToString("0.0", CultureInfo.InvariantCulture)) + "% -- replaced with " + f + suffix);
                }
                catch (VfsEx e) { io.E(prog + ": " + f + suffix + ": " + e.Message); st = 1; }
            }
            return st;
        }

        // ------------------------------------------------------------ zip / unzip
        static int Zip(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "rqv9", "", "zip");
            if (o.Error != null) return Fail(io, o);
            if (o.Args.Count < 2) { io.E("zip error: Nothing to do! (" + (o.Args.Count > 0 ? o.Args[0] : "") + ")"); return 12; }
            string zf = o.Args[0].EndsWith(".zip", StringComparison.Ordinal) ? o.Args[0] : o.Args[0] + ".zip";
            var entries = new List<ArEntry>();
            int st = 0;
            foreach (var p in o.Args.Skip(1))
            {
                var before = entries.Count;
                var tmp = new List<ArEntry>();
                Collect(sh, io, p, p.TrimStart('/'), tmp, new List<string>(), "zip", ref st);
                if (!o.Has("r")) tmp = tmp.Take(1).ToList();
                entries.AddRange(tmp);
                if (!o.Has("q")) foreach (var e in tmp) io.L("  adding: " + e.Path + (e.T == 'd' ? "/" : "") + " (" + (e.T == 'd' || e.Size < 20 ? "stored 0%" : "deflated 61%") + ")");
            }
            try
            {
                var n = sh.M.V.WriteText(sh.P, zf, "", false);
                n.Text = null; n.Blob = Pack("zip", entries); n.BinSize = 22 + entries.Sum(e => 76 + e.Path.Length * 2 + e.Size / 2);
            }
            catch (VfsEx e) { io.E("zip I/O error: " + e.Message + "\nzip error: Could not create output file (" + zf + ")"); return 15; }
            return 0;
        }

        static int Unzip(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "ld:oqn", "", "unzip");
            if (o.Error != null) return Fail(io, o);
            if (o.Args.Count == 0) { io.E("UnZip 6.00 of 20 April 2009, by Info-ZIP.\nUsage: unzip [-Z] [-opts[modifiers]] file[.zip] [list] [-x xlist] [-d exdir]"); return 0; }
            string zf = o.Args[0];
            Loc l;
            try { l = sh.M.V.Resolve(sh.P, zf, true); if (!l.Exists && !zf.EndsWith(".zip", StringComparison.Ordinal)) { zf += ".zip"; l = sh.M.V.Resolve(sh.P, zf, true); } }
            catch (VfsEx e) { io.E("unzip:  cannot find or open " + zf + ": " + e.Message); return 9; }
            if (!l.Exists) { io.E("unzip:  cannot find or open " + o.Args[0] + ", " + o.Args[0] + ".zip or " + o.Args[0] + ".ZIP."); return 9; }
            if (KindOf(l.Node.Blob) != "zip") { io.E("Archive:  " + zf + "\n  End-of-central-directory signature not found.  Either this file is not\n  a zipfile, or it constitutes one disk of a multi-part archive."); return 9; }
            var es = Unpack(l.Node.Blob);
            io.L("Archive:  " + zf);
            if (o.Has("l"))
            {
                io.L("  Length      Date    Time    Name\n---------  ---------- -----   ----");
                foreach (var e in es) io.L(e.Size.ToString().PadLeft(9) + "  " + sh.M.LocalTime(e.Mtime).ToString("MM-dd-yyyy HH:mm", CultureInfo.InvariantCulture) + "   " + e.Path + (e.T == 'd' ? "/" : ""));
                io.L("---------                     -------\n" + es.Sum(e => e.Size).ToString().PadLeft(9) + "                     " + es.Count(e => true) + " files");
                return 0;
            }
            string dest = o.Get("d") ?? sh.P.Cwd;
            try { var dl = sh.M.V.Resolve(sh.P, dest, true); if (!dl.Exists) sh.M.V.Mkdir(sh.P, dest, 0x1FF, true); } catch (VfsEx e) { io.E("checkdir error:  cannot create " + dest + "\n                 " + e.Message); return 3; }
            dest = Vfs.Norm(Vfs.Join(sh.P.Cwd, dest));
            int st = 0;
            foreach (var e in es)
            {
                if (!o.Has("q")) io.L((e.T == 'd' ? "   creating: " : "  inflating: ") + (o.Has("d") ? o.Get("d").TrimEnd('/') + "/" : "") + e.Path + (e.T == 'd' ? "/" : ""));
                var e2 = new ArEntry { Path = e.Path, T = e.T, Mode = e.Mode, Uid = sh.P.Cr.Uid, Gid = sh.P.Cr.Gid, Mtime = e.Mtime, Text = e.Text, BinSize = e.BinSize, Blob = e.Blob, Prog = e.Prog, Target = e.Target };
                if (!Extract(sh, io, e2, dest, false, false, o.Has("n"), "unzip")) st = 1;
            }
            return st;
        }

        // ------------------------------------------------------------ ACLs
        static string Who(Machine m, AclEntry e)
        {
            if (e.Who == "") return "";
            int id = int.Parse(e.Who, CultureInfo.InvariantCulture);
            return e.Tag == "user" ? m.UName(id) : m.GName(id);
        }

        static int Getfacl(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "acdeEnpRsLPt", "access|a,omit-header|c,default|d,all-effective|e,no-effective|E,numeric|n,absolute-names|p,recursive|R,skip-base|s,physical|P,logical|L,tabular|t", "getfacl");
            if (o.Error != null) return Fail(io, o);
            if (o.Args.Count == 0) { io.E("Usage: getfacl [-aceEsRLPtpndvh] file ...\nTry `getfacl --help' for more information."); return 1; }
            var m = sh.M;
            int st = 0; bool warned = false;
            Action<Loc, string> show = null;
            show = (l, shown) =>
            {
                var n = l.Node;
                if (o.Has("s") && !n.HasExtAcl && n.DefAcl == null) return;
                string name = shown;
                if (!o.Has("p") && name.StartsWith("/", StringComparison.Ordinal) && name.Length > 1) { if (!warned) { io.E("getfacl: Removing leading '/' from absolute path names"); warned = true; } name = name.TrimStart('/'); }
                var sb = new StringBuilder();
                if (!o.Has("c")) sb.Append("# file: " + name + "\n# owner: " + (o.Has("n") ? n.Uid.ToString() : m.UName(n.Uid)) + "\n# group: " + (o.Has("n") ? n.Gid.ToString() : m.GName(n.Gid)) + "\n");
                if (!o.Has("c") && (n.Mode & 0xE00) != 0) sb.Append("# flags: " + ((n.Mode & 0x800) != 0 ? "s" : "-") + ((n.Mode & 0x400) != 0 ? "s" : "-") + ((n.Mode & 0x200) != 0 ? "t" : "-") + "\n");
                int mask = n.HasExtAcl ? (n.Mode >> 3) & 7 : 7;
                Func<AclEntry, string> name2 = e => o.Has("n") ? e.Who : Who(m, e);
                Func<int, int, string> eff = (perm, mk) => (perm & mk) != perm || o.Has("e") ? "\t#effective:" + Vfs.Rwx(perm & mk) : "";
                if (!o.Has("d"))
                {
                    sb.Append("user::" + Vfs.Rwx((n.Mode >> 6) & 7) + "\n");
                    if (n.HasExtAcl)
                    {
                        foreach (var e in n.Acl.Where(x => x.Tag == "user" && x.Who != "")) sb.Append("user:" + name2(e) + ":" + Vfs.Rwx(e.Perm) + (o.Has("E") ? "" : eff(e.Perm, mask)) + "\n");
                        var g = n.Acl.FirstOrDefault(x => x.Tag == "group" && x.Who == "");
                        int gp = g == null ? mask : g.Perm;
                        sb.Append("group::" + Vfs.Rwx(gp) + (o.Has("E") ? "" : eff(gp, mask)) + "\n");
                        foreach (var e in n.Acl.Where(x => x.Tag == "group" && x.Who != "")) sb.Append("group:" + name2(e) + ":" + Vfs.Rwx(e.Perm) + (o.Has("E") ? "" : eff(e.Perm, mask)) + "\n");
                        sb.Append("mask::" + Vfs.Rwx(mask) + "\n");
                    }
                    else sb.Append("group::" + Vfs.Rwx((n.Mode >> 3) & 7) + "\n");
                    sb.Append("other::" + Vfs.Rwx(n.Mode & 7) + "\n");
                }
                if (n.DefAcl != null && n.DefAcl.Count > 0 && !o.Has("a"))
                {
                    var dm = n.DefAcl.FirstOrDefault(x => x.Tag == "mask");
                    int dmask = dm == null ? 7 : dm.Perm;
                    string pre = o.Has("d") ? "" : "default:";
                    foreach (var tag in new[] { "user", "group" })
                    {
                        foreach (var e in n.DefAcl.Where(x => x.Tag == tag && x.Who == "")) sb.Append(pre + tag + "::" + Vfs.Rwx(e.Perm) + (tag == "group" && dm != null && !o.Has("E") ? eff(e.Perm, dmask) : "") + "\n");
                        foreach (var e in n.DefAcl.Where(x => x.Tag == tag && x.Who != "")) sb.Append(pre + tag + ":" + name2(e) + ":" + Vfs.Rwx(e.Perm) + (dm != null && !o.Has("E") ? eff(e.Perm, dmask) : "") + "\n");
                    }
                    if (dm != null) sb.Append(pre + "mask::" + Vfs.Rwx(dm.Perm) + "\n");
                    foreach (var e in n.DefAcl.Where(x => x.Tag == "other")) sb.Append(pre + "other::" + Vfs.Rwx(e.Perm) + "\n");
                }
                io.L(sb.ToString());
                if (o.Has("R") && n.T == 'd')
                    foreach (var k in Vfs.Sorted(n.Kids.Keys))
                    {
                        string cp = shown.TrimEnd('/') + "/" + k;
                        try { var cl = m.V.Resolve(sh.P, cp, false); if (cl.Exists && cl.Node.T != 'l') show(cl, cp); } catch (VfsEx) { }
                    }
            };
            foreach (var p in o.Args)
            {
                try
                {
                    var l = m.V.Resolve(sh.P, p, true);
                    if (!l.Exists) { io.E("getfacl: " + p + ": No such file or directory"); st = 1; continue; }
                    show(l, p);
                }
                catch (VfsEx e) { io.E("getfacl: " + p + ": " + e.Message); st = 1; }
            }
            return st;
        }

        static int ParsePerm(string s, Inode n, out bool ok)
        {
            ok = true;
            if (Regex.IsMatch(s, "^[0-7]$")) return s[0] - '0';
            int p = 0;
            foreach (var c in s)
            {
                if (c == 'r') p |= 4; else if (c == 'w') p |= 2; else if (c == 'x') p |= 1;
                else if (c == 'X') { if (n.T == 'd' || (n.Mode & 0x49) != 0) p |= 1; }
                else if (c == '-') { }
                else { ok = false; return 0; }
            }
            return p;
        }

        class AclSpec { public bool Def; public string Tag; public string Who = ""; public string Perm; }

        static AclSpec ParseSpec(Machine m, string s, bool needPerm, out string err)
        {
            err = null;
            var sp = new AclSpec();
            var p = s.Split(':').ToList();
            if (p.Count > 0 && (p[0] == "d" || p[0] == "default")) { sp.Def = true; p.RemoveAt(0); }
            if (p.Count == 0) { err = "1"; return null; }
            string t = p[0];
            sp.Tag = t == "u" || t == "user" ? "user" : t == "g" || t == "group" ? "group" : t == "m" || t == "mask" ? "mask" : t == "o" || t == "other" ? "other" : null;
            if (sp.Tag == null)
            {
                // "name:rwx" without a tag means user (setfacl accepts it)
                err = "1"; return null;
            }
            string who = p.Count > 1 ? p[1] : "";
            string perm = p.Count > 2 ? p[2] : (sp.Tag == "other" || sp.Tag == "mask") && p.Count == 2 ? p[1] : null;
            if ((sp.Tag == "other" || sp.Tag == "mask") && p.Count == 2) who = "";
            if (needPerm && string.IsNullOrEmpty(perm)) { err = (s.Length).ToString(); return null; }
            if (who.Length > 0)
            {
                if (sp.Tag == "user") { var u = m.User(who); int id; if (u != null) sp.Who = u.Uid.ToString(CultureInfo.InvariantCulture); else if (int.TryParse(who, out id)) sp.Who = who; else { err = (s.IndexOf(who, StringComparison.Ordinal) + 1).ToString(); return null; } }
                else if (sp.Tag == "group") { var g = m.Groups().FirstOrDefault(x => x.Name == who); int id; if (g != null) sp.Who = g.Gid.ToString(CultureInfo.InvariantCulture); else if (int.TryParse(who, out id)) sp.Who = who; else { err = (s.IndexOf(who, StringComparison.Ordinal) + 1).ToString(); return null; } }
                else { err = "3"; return null; }
            }
            sp.Perm = perm;
            return sp;
        }

        static int Setfacl(Sh sh, List<string> a, Io io)
        {
            var m = sh.M;
            var ops = new List<string[]>();   // op, text
            bool recursive = false, noMask = false, test = false;
            var files = new List<string>();
            for (int i = 1; i < a.Count; i++)
            {
                string s = a[i];
                if (s == "-R" || s == "--recursive") recursive = true;
                else if (s == "-n" || s == "--no-mask") noMask = true;
                else if (s == "--mask") noMask = false;
                else if (s == "--test") test = true;
                else if (s == "-b" || s == "--remove-all") ops.Add(new[] { "b", "" });
                else if (s == "-k" || s == "--remove-default") ops.Add(new[] { "k", "" });
                else if (s == "-d" || s == "--default") ops.Add(new[] { "d", "" });
                else if (s == "-m" || s == "-x" || s == "--modify" || s == "--remove" || s == "--set" || s == "-M" || s == "-X")
                {
                    if (i + 1 >= a.Count) { io.E("setfacl: option requires an argument -- '" + s.TrimStart('-')[0] + "'\nUsage: setfacl [-bkndRLP] { -m|-M|-x|-X ... } file ...\nTry `setfacl --help' for more information."); return 2; }
                    string op = s == "-m" || s == "--modify" ? "m" : s == "-x" || s == "--remove" ? "x" : s == "--set" ? "set" : s == "-M" ? "M" : "X";
                    ops.Add(new[] { op, a[++i] });
                }
                else if (s.StartsWith("-m", StringComparison.Ordinal) || s.StartsWith("-x", StringComparison.Ordinal)) ops.Add(new[] { s.Substring(1, 1), s.Substring(2) });
                else if (s.StartsWith("--modify=", StringComparison.Ordinal) || s.StartsWith("--set=", StringComparison.Ordinal)) ops.Add(new[] { s.StartsWith("--set=", StringComparison.Ordinal) ? "set" : "m", s.Substring(s.IndexOf('=') + 1) });
                else if (s.StartsWith("-", StringComparison.Ordinal) && s.Length > 1 && s.Substring(1).All(c => "bkndR".IndexOf(c) >= 0)) { foreach (var c in s.Substring(1)) { if (c == 'R') recursive = true; else if (c == 'n') noMask = true; else ops.Add(new[] { c.ToString(), "" }); } }
                else if (s.StartsWith("-", StringComparison.Ordinal) && s.Length > 1) { io.E("setfacl: invalid option -- '" + s[1] + "'\nUsage: setfacl [-bkndRLP] { -m|-M|-x|-X ... } file ...\nTry `setfacl --help' for more information."); return 2; }
                else files.Add(s);
            }
            if (ops.Count == 0 || files.Count == 0) { io.E("Usage: setfacl [-bkndRLP] { -m|-M|-x|-X ... } file ...\nTry `setfacl --help' for more information."); return 2; }
            bool defAll = ops.Any(x => x[0] == "d");
            // resolve -M / -X files
            for (int i = 0; i < ops.Count; i++)
                if (ops[i][0] == "M" || ops[i][0] == "X")
                {
                    string t;
                    try { t = m.V.ReadText(sh.P, ops[i][1]); } catch (VfsEx e) { io.E("setfacl: " + ops[i][1] + ": " + e.Message); return 1; }
                    ops[i] = new[] { ops[i][0] == "M" ? "m" : "x", string.Join(",", t.Split('\n').Select(x => Regex.Replace(x, "#.*$", "").Trim()).Where(x => x.Length > 0).ToArray()) };
                }
            // check every spec first (setfacl changes nothing when one is wrong)
            var parsed = new List<Tuple<string, List<AclSpec>>>();
            foreach (var op in ops)
            {
                if (op[0] == "b" || op[0] == "k" || op[0] == "d") { parsed.Add(Tuple.Create(op[0], (List<AclSpec>)null)); continue; }
                var list = new List<AclSpec>();
                foreach (var part in op[1].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string err;
                    var sp = ParseSpec(m, part.Trim(), op[0] != "x", out err);
                    if (sp == null) { io.E("setfacl: Option -" + (op[0] == "set" ? "-set" : op[0]) + ": Invalid argument near character " + err); return 2; }
                    if (defAll) sp.Def = true;
                    list.Add(sp);
                }
                parsed.Add(Tuple.Create(op[0], list));
            }
            int st = 0;
            Action<Loc, string> apply = null;
            apply = (l, path) =>
            {
                var n = l.Node;
                var fsType = l.Fs == null ? "" : l.Fs.Type;
                if (fsType == "vfat" || fsType == "nfs4" || fsType == "iso9660" || fsType == "proc" || fsType == "sysfs" || fsType == "tmpfs" && false) { io.E("setfacl: " + path + ": Operation not supported"); st = 1; return; }
                if (sh.P.Cr.Uid != 0 && n.Uid != sh.P.Cr.Uid) { io.E("setfacl: " + path + ": Operation not permitted"); st = 1; return; }
                if (l.Mnt != null && l.Mnt.Ro) { io.E("setfacl: " + path + ": Read-only file system"); st = 1; return; }
                var acl = n.Acl == null ? new List<AclEntry>() : n.Acl.Select(e => new AclEntry(e.Tag, e.Who, e.Perm)).ToList();
                int mode = n.Mode;
                int uPerm = (mode >> 6) & 7, oPerm = mode & 7;
                int gPerm = n.HasExtAcl ? (acl.FirstOrDefault(x => x.Tag == "group" && x.Who == "") ?? new AclEntry("group", "", (mode >> 3) & 7)).Perm : (mode >> 3) & 7;
                int? maskSet = null;
                bool explicitMask = false;
                var def = n.DefAcl == null ? null : n.DefAcl.Select(e => new AclEntry(e.Tag, e.Who, e.Perm)).ToList();
                bool defChanged = false;
                foreach (var pr in parsed)
                {
                    string op = pr.Item1;
                    if (op == "b") { acl.Clear(); def = null; maskSet = null; defChanged = true; continue; }
                    if (op == "k") { def = null; defChanged = true; continue; }
                    if (op == "d") continue;
                    if (op == "set") { acl.Clear(); }
                    foreach (var sp in pr.Item2)
                    {
                        bool ok = true;
                        int perm = sp.Perm == null ? 0 : ParsePerm(sp.Perm, n, out ok);
                        if (!ok) { io.E("setfacl: Option -m: Invalid argument near character " + (sp.Tag.Length + sp.Who.Length + 3)); st = 2; return; }
                        if (sp.Def)
                        {
                            if (n.T != 'd') { if (op != "x") { io.E("setfacl: " + path + ": Only directories can have default ACLs"); st = 1; return; } continue; }
                            if (def == null) def = new List<AclEntry> { new AclEntry("user", "", uPerm), new AclEntry("group", "", gPerm), new AclEntry("other", "", oPerm) };
                            def.RemoveAll(e => e.Tag == sp.Tag && e.Who == sp.Who);
                            if (op != "x") def.Add(new AclEntry(sp.Tag, sp.Who, perm));
                            else if (sp.Who == "" && sp.Tag != "mask") { io.E("setfacl: " + path + ": Malformed access ACL `" + sp.Tag + "::': Missing or wrong entry at entry 1"); st = 1; return; }
                            defChanged = true;
                            continue;
                        }
                        if (sp.Tag == "user" && sp.Who == "") { if (op != "x") uPerm = perm; continue; }
                        if (sp.Tag == "other") { if (op != "x") oPerm = perm; continue; }
                        if (sp.Tag == "group" && sp.Who == "") { if (op != "x") gPerm = perm; continue; }
                        if (sp.Tag == "mask") { if (op != "x") { maskSet = perm; explicitMask = true; } else maskSet = null; continue; }
                        acl.RemoveAll(e => e.Tag == sp.Tag && e.Who == sp.Who);
                        if (op != "x") acl.Add(new AclEntry(sp.Tag, sp.Who, perm));
                    }
                }
                var named = acl.Where(e => e.Who != "").OrderBy(e => e.Tag == "user" ? 0 : 1).ThenBy(e => int.Parse(e.Who, CultureInfo.InvariantCulture)).ToList();
                if (test) { io.L(path + ": " + string.Join(",", named.Select(e => e.Tag[0] + ":" + Who(m, e) + ":" + Vfs.Rwx(e.Perm)).ToArray())); return; }
                int maskVal;
                if (named.Count > 0 || explicitMask)
                {
                    int union = gPerm; foreach (var e in named) union |= e.Perm;
                    int oldMask = n.HasExtAcl ? (n.Mode >> 3) & 7 : union;
                    maskVal = explicitMask ? maskSet.Value : noMask ? oldMask : union;
                    var list = new List<AclEntry>(named); list.Insert(named.Count(e => e.Tag == "user"), new AclEntry("group", "", gPerm));
                    n.Acl = list;
                }
                else { n.Acl = null; maskVal = gPerm; }
                n.Mode = (n.Mode & ~0x1FF) | (uPerm << 6) | (maskVal << 3) | oPerm;
                if (defChanged)
                {
                    if (def != null)
                    {
                        def.RemoveAll(e => e.Tag == "mask" && false);
                        var dn = def.Where(e => e.Who != "" && (e.Tag == "user" || e.Tag == "group")).ToList();
                        if (dn.Count > 0 && !def.Any(e => e.Tag == "mask")) { int u2 = def.Where(e => e.Tag == "group").Aggregate(0, (x, e) => x | e.Perm) | dn.Aggregate(0, (x, e) => x | e.Perm); def.Add(new AclEntry("mask", "", u2)); }
                        else if (def.Any(e => e.Tag == "mask") && !noMask && !parsed.Any(p2 => p2.Item2 != null && p2.Item2.Any(s2 => s2.Def && s2.Tag == "mask")))
                        {
                            var mk = def.First(e => e.Tag == "mask"); mk.Perm = def.Where(e => e.Tag == "group" || (e.Tag == "user" && e.Who != "")).Aggregate(0, (x, e) => x | e.Perm);
                        }
                    }
                    n.DefAcl = def;
                }
                n.Ctime = m.Now;
                if (recursive && n.T == 'd')
                    foreach (var k in Vfs.Sorted(n.Kids.Keys))
                    {
                        string cp = path.TrimEnd('/') + "/" + k;
                        try { var cl = m.V.Resolve(sh.P, cp, false); if (cl.Exists && cl.Node.T != 'l') apply(cl, cp); } catch (VfsEx) { }
                    }
            };
            foreach (var f in files)
            {
                try
                {
                    var l = m.V.Resolve(sh.P, f, true);
                    if (!l.Exists) { io.E("setfacl: " + f + ": No such file or directory"); st = 1; continue; }
                    apply(l, f);
                }
                catch (VfsEx e) { io.E("setfacl: " + f + ": " + e.Message); st = 1; }
            }
            return st;
        }
    }
}
