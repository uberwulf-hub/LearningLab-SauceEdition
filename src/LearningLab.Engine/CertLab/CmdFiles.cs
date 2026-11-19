// Cert Lab - file programs: ls, cp, mv, rm, mkdir, rmdir, touch, cat, ln, chmod, chown, chgrp, stat, find, du,
// df, file, tree, realpath, readlink, basename, dirname, mktemp, dd, tee, truncate, install, mkfifo, namei.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CertLab
{
    public static partial class Cmd
    {
        static void RegFiles()
        {
            P("ls", Ls); P("dir", Ls); P("vdir", Ls); P("cp", Cp); P("mv", Mv); P("rm", Rm); P("mkdir", Mkdir); P("rmdir", Rmdir); P("touch", Touch);
            P("cat", Cat); P("ln", Ln); P("link", Ln); P("chmod", Chmod); P("chown", Chown); P("chgrp", Chgrp); P("stat", Stat); P("find", FindCmd);
            P("du", Du); P("df", Df); P("file", FileCmd); P("tree", Tree); P("realpath", Realpath); P("readlink", Readlink); P("basename", Basename);
            P("dirname", Dirname); P("mktemp", Mktemp); P("dd", Dd); P("tee", Tee); P("truncate", Truncate); P("install", Install); P("mkfifo", Mkfifo);
            P("namei", Namei); P("unlink", (sh, a, io) => Rm(sh, new List<string> { "rm", a.Count > 1 ? a[1] : "" }, io)); P("sync", (sh, a, io) => 0); P("shred", Shred);
            P("lsattr", (sh, a, io) => { foreach (var f in a.Skip(1).Where(x => !x.StartsWith("-", StringComparison.Ordinal))) io.L("--------------e------- " + f); return 0; });
            P("chattr", (sh, a, io) => 0);
        }

        // ------------------------------------------------------------ ls
        class LsEnt { public string Name; public Loc L; public Inode N; }

        static int Ls(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "lalAdhRtrS1iFnZgoGpkcuUvL", "all|a,almost-all|A,directory|d,human-readable|h,recursive|R,reverse|r,inode|i,context|Z,color=?,full-time,classify|F,numeric-uid-gid|n,time-style=,sort=,group-directories-first,dereference|L", "ls");
            if (o.Error != null) return Fail(io, o);
            bool longf = o.Has("l") || o.Has("n") || o.Has("g") || o.Has("o") || o.Has("full-time") || a[0] == "vdir";
            var targets = o.Args.Count == 0 ? new List<string> { "." } : o.Args;
            int st = 0;
            var files = new List<LsEnt>(); var dirs = new List<LsEnt>();
            foreach (var t in targets)
            {
                Loc l;
                try { l = Find(sh, t, true); if (!l.Exists) { l = Find(sh, t, false); } }
                catch (VfsEx e) { io.E("ls: cannot access '" + t + "': " + e.Message); st = 2; continue; }
                if (!l.Exists) { io.E("ls: cannot access '" + t + "': No such file or directory"); st = 2; continue; }
                // a symlink named without -L/-l to a dir is followed; with -l (and no trailing /) it is listed itself
                Loc ln = null;
                try { ln = Find(sh, t, false); } catch (VfsEx) { }
                if (ln != null && ln.Exists && ln.Node.T == 'l' && (o.Has("d") || (longf && !t.EndsWith("/", StringComparison.Ordinal) && !o.Has("L")))) { files.Add(new LsEnt { Name = t, L = ln, N = ln.Node }); continue; }
                if (l.Node.T == 'd' && !o.Has("d")) dirs.Add(new LsEnt { Name = t, L = l, N = l.Node });
                else files.Add(new LsEnt { Name = t, L = l, N = l.Node });
            }
            bool first = true;
            if (files.Count > 0) { PrintEntries(sh, io, o, SortEnts(sh, o, files), longf, false, 0); first = false; }
            bool headers = targets.Count > 1 || o.Has("R");
            foreach (var d in SortEnts(sh, o, dirs)) { if (ListDir(sh, io, o, d.Name, d.L, longf, headers, ref first) != 0) st = 2; }
            return st;
        }

        static int ListDir(Sh sh, Io io, Opts o, string shown, Loc l, bool longf, bool headers, ref bool first)
        {
            int st = 0;
            if (!sh.M.V.Can(sh.P.Cr, l.Node, 4)) { io.E("ls: cannot open directory '" + shown + "': Permission denied"); return 2; }
            if (!first) io.L("");
            first = false;
            if (headers) io.L(shown + ":");
            if (l.Fs.Type == "proc") sh.M.RefreshProc(l.Fs, l.Node);
            var ents = new List<LsEnt>();
            bool all = o.Has("a"), almost = o.Has("A");
            if (all) { ents.Add(new LsEnt { Name = ".", L = l, N = l.Node }); Loc par = null; try { par = Find(sh, Vfs.Join(l.Path, ".."), true); } catch (VfsEx) { } ents.Add(new LsEnt { Name = "..", L = par ?? l, N = par != null ? par.Node : l.Node }); }
            bool canSearch = sh.M.V.Can(sh.P.Cr, l.Node, 1);
            foreach (var k in l.Node.Kids.Keys.ToList())
            {
                if (k.StartsWith(".", StringComparison.Ordinal) && !all && !almost) continue;
                Loc cl = null;
                try { cl = sh.M.V.Resolve(sh.P, Vfs.Join(l.Path, k), false); } catch (VfsEx) { }
                if (cl == null || !cl.Exists)
                {
                    if (!canSearch) { ents.Add(new LsEnt { Name = k, L = null, N = null }); continue; }
                    continue;
                }
                ents.Add(new LsEnt { Name = k, L = cl, N = cl.Node });
            }
            if (!canSearch && ents.Any(e => e.N == null)) foreach (var e in ents.Where(x => x.N == null)) io.E("ls: cannot access '" + Vfs.Join(shown, e.Name) + "': Permission denied");
            ents = SortEnts(sh, o, ents);
            PrintEntries(sh, io, o, ents.Where(e => e.N != null || longf).ToList(), longf, true, 0);
            if (o.Has("R"))
                foreach (var e in ents)
                {
                    if (e.N == null || e.N.T != 'd' || e.Name == "." || e.Name == "..") continue;
                    var cl = e.L;
                    if (ListDir(sh, io, o, (shown.EndsWith("/", StringComparison.Ordinal) ? shown : shown + "/") + e.Name, cl, longf, true, ref first) != 0) st = 2;
                }
            return st;
        }

        static List<LsEnt> SortEnts(Sh sh, Opts o, List<LsEnt> l)
        {
            List<LsEnt> r;
            if (o.Has("U") || o.Get("sort") == "none") r = l.ToList();
            else if (o.Has("t") || o.Get("sort") == "time") r = l.OrderByDescending(e => e.N == null ? 0 : e.N.Mtime).ThenBy(e => e.Name, Comparer<string>.Create(Vfs.LsCompare)).ToList();
            else if (o.Has("S") || o.Get("sort") == "size") r = l.OrderByDescending(e => e.N == null ? 0 : e.N.Size).ThenBy(e => e.Name, Comparer<string>.Create(Vfs.LsCompare)).ToList();
            else { r = l.ToList(); r.Sort((x, y) => Vfs.LsCompare(x.Name, y.Name)); }
            if (o.Has("r")) r.Reverse();
            return r;
        }

        static string Classify(Inode n)
        {
            if (n == null) return "";
            if (n.T == 'd') return "/";
            if (n.T == 'l') return "@";
            if (n.T == 'p') return "|";
            if (n.T == 'f' && (n.Mode & 0x49) != 0) return "*";
            return "";
        }

        static void PrintEntries(Sh sh, Io io, Opts o, List<LsEnt> ents, bool longf, bool inDir, int indent)
        {
            var m = sh.M;
            if (!longf)
            {
                var names = ents.Select(e => (o.Has("i") && e.N != null ? e.N.Ino + " " : "") + (o.Has("Z") && e.N != null ? e.N.Ctx + " " : "") + e.Name + (o.Has("F") ? Classify(e.N) : o.Has("p") && e.N != null && e.N.T == 'd' ? "/" : "")).ToList();
                if (names.Count == 0) return;
                if (o.Has("1") || !io.Out.IsTty || o.Has("Z")) { foreach (var n in names) io.L(n); return; }
                // columns like GNU ls (down then across) for an 80-column terminal
                int width = 80;
                int bestCols = 1; int[] bestW = null;
                for (int cols = Math.Min(names.Count, 12); cols >= 1; cols--)
                {
                    int rows = (names.Count + cols - 1) / cols;
                    var w = new int[cols];
                    for (int i = 0; i < names.Count; i++) { int c = i / rows; w[c] = Math.Max(w[c], names[i].Length); }
                    int total = w.Sum() + 2 * (cols - 1);
                    if (total <= width) { bestCols = cols; bestW = w; break; }
                }
                int rws = (names.Count + bestCols - 1) / bestCols;
                if (bestW == null) bestW = new[] { names.Max(x => x.Length) };
                for (int r = 0; r < rws; r++)
                {
                    var sb = new StringBuilder();
                    for (int c = 0; c < bestCols; c++)
                    {
                        int i = c * rws + r;
                        if (i >= names.Count) break;
                        bool last = c == bestCols - 1 || (c + 1) * rws + r >= names.Count;
                        sb.Append(last ? names[i] : names[i].PadRight(bestW[c] + 2));
                    }
                    io.L(sb.ToString());
                }
                return;
            }
            if (inDir)
            {
                long blocks = ents.Where(e => e.N != null && e.Name != "." && e.Name != "..").Sum(e => e.N.T == 'f' ? (e.N.Size + 4095) / 4096 * 4 : 0);
                if (o.Has("a")) blocks += 0;
                io.L("total " + (o.Has("h") ? Human(blocks * 1024) : blocks.ToString(CultureInfo.InvariantCulture)));
            }
            bool numeric = o.Has("n");
            var rows2 = new List<string[]>();
            foreach (var e in ents)
            {
                var n = e.N;
                if (n == null) { rows2.Add(new[] { "", "d?????????", "?", "?", "?", "?", "?", "", e.Name }); continue; }
                string mode = Vfs.ModeString(n) + (n.HasExtAcl ? "+" : (m.SeMode != "disabled" && Selinux.TypeOf(n.Ctx) != "" ? "." : ""));
                string user = numeric ? n.Uid.ToString() : Owner(m, n.Uid), group = numeric ? n.Gid.ToString() : Grp(m, n.Gid);
                string size = n.T == 'b' || n.T == 'c' ? DevNums(m, n) : (o.Has("h") ? Human(n.Size) : n.Size.ToString(CultureInfo.InvariantCulture));
                string date = o.Has("full-time") ? FullDate(m, n.Mtime) : LsDate(m, o.Has("u") ? n.Atime : o.Has("c") ? n.Ctime : n.Mtime);
                string name = e.Name + (n.T == 'l' ? " -> " + n.Target : "") + (o.Has("F") ? Classify(n) : "");
                rows2.Add(new[] { o.Has("i") ? n.Ino.ToString() : "", mode, n.Nlink.ToString(), o.Has("g") ? null : user, o.Has("o") || o.Has("G") ? null : group, size, date, o.Has("Z") ? n.Ctx : "", name });
            }
            int[] w2 = new int[9];
            foreach (var r in rows2) for (int i = 0; i < 9; i++) if (r[i] != null) w2[i] = Math.Max(w2[i], r[i].Length);
            foreach (var r in rows2)
            {
                var sb = new StringBuilder();
                if (o.Has("i")) sb.Append(r[0].PadLeft(w2[0])).Append(' ');
                sb.Append(r[1]).Append(' ').Append(r[2].PadLeft(w2[2])).Append(' ');
                if (r[3] != null) sb.Append(r[3].PadRight(w2[3])).Append(' ');
                if (r[4] != null) sb.Append(r[4].PadRight(w2[4])).Append(' ');
                if (o.Has("Z")) sb.Append(r[7].PadRight(w2[7])).Append(' ');
                sb.Append(r[5].PadLeft(w2[5])).Append(' ').Append(r[6]).Append(' ').Append(r[8]);
                io.L(sb.ToString());
            }
        }

        static string DevNums(Machine m, Inode n)
        {
            if (n.T == 'c')
            {
                switch (n.Dev) { case "null": return "1, 3"; case "zero": return "1, 5"; case "random": return "1, 8"; case "urandom": return "1, 9"; case "tty": return "5, 0"; case "console": return "5, 1"; case "full": return "1, 7"; }
                return "10, 236";
            }
            var d = m.Devices().FirstOrDefault(x => x.Name == n.Dev || ("dm-" + x.Minor == n.Dev && x.Type == "lvm"));
            return d == null ? "252, 0" : d.Major + ", " + d.Minor.ToString().PadLeft(2);
        }

        // ------------------------------------------------------------ cp / mv
        static int Cp(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "rRafpinvuLPdlsbt:T", "recursive|r,archive|a,force|f,interactive|i,no-clobber|n,verbose|v,preserve=?,parents,remove-destination,target-directory=|t,no-target-directory|T,update|u,dereference|L,no-dereference|P,link|l,symbolic-link|s,backup=?|b,context=?", "cp");
            if (o.Error != null) return Fail(io, o);
            bool rec = o.Has("r") || o.Has("R") || o.Has("a");
            bool preserveAll = o.Has("a"), preserve = o.Has("p") || o.Has("a") || o.Has("preserve");
            bool keepCtx = o.Has("a") || (o.Get("preserve") ?? "").Contains("context") || (o.Get("preserve") ?? "").Contains("all");
            var args = o.Args;
            string targetDir = o.Get("t");
            if (targetDir == null && args.Count < 2) { io.E(args.Count == 0 ? "cp: missing file operand\nTry 'cp --help' for more information." : "cp: missing destination file operand after '" + args[0] + "'\nTry 'cp --help' for more information."); return 1; }
            var srcs = targetDir != null ? args : args.Take(args.Count - 1).ToList();
            string dest = targetDir ?? args[args.Count - 1];
            Loc dl = null;
            try { dl = Find(sh, dest, true); } catch (VfsEx e) { io.E("cp: cannot create regular file '" + dest + "': " + e.Message); return 1; }
            bool destIsDir = dl.Exists && dl.Node.T == 'd' && !o.Has("T");
            if (srcs.Count > 1 && !destIsDir) { io.E("cp: target '" + dest + "' is not a directory"); return 1; }
            int st = 0;
            foreach (var s in srcs)
            {
                Loc sl;
                try { sl = Find(sh, s, !(o.Has("P") || o.Has("d") || preserveAll) || o.Has("L")); }
                catch (VfsEx e) { io.E("cp: cannot stat '" + s + "': " + e.Message); st = 1; continue; }
                if (!sl.Exists) { io.E("cp: cannot stat '" + s + "': No such file or directory"); st = 1; continue; }
                if (sl.Node.T == 'd' && !rec) { io.E("cp: -r not specified; omitting directory '" + s + "'"); st = 1; continue; }
                string target = destIsDir ? Vfs.Join(dest, Vfs.Base(s.TrimEnd('/'))) : dest;
                if (sl.Node.T == 'd')
                {
                    var tl0 = Find(sh, target, true);
                    if (destIsDir || !tl0.Exists || tl0.Node.T == 'd') { }
                    if (Abs(sh, target).StartsWith(Abs(sh, s).TrimEnd('/') + "/", StringComparison.Ordinal)) { io.E("cp: cannot copy a directory, '" + s + "', into itself, '" + target + "'"); st = 1; continue; }
                }
                if (!CopyTree(sh, io, o, sl, s, target, preserve, keepCtx, preserveAll)) st = 1;
            }
            return st;
        }

        static bool CopyTree(Sh sh, Io io, Opts o, Loc sl, string shownSrc, string target, bool preserve, bool keepCtx, bool archive)
        {
            var v = sh.M.V;
            var n = sl.Node;
            Loc tl;
            try { tl = v.Resolve(sh.P, target, true); }
            catch (VfsEx e) { io.E("cp: cannot create " + (n.T == 'd' ? "directory" : "regular file") + " '" + target + "': " + e.Message); return false; }
            if (n.T == 'l' && archive)
            {
                try { var tl2 = v.Resolve(sh.P, target, false); if (tl2.Exists) v.Unlink(sh.P, tl2); v.Symlink(sh.P, n.Target, v.Resolve(sh.P, target, false)); }
                catch (VfsEx e) { io.E("cp: cannot create symbolic link '" + target + "': " + e.Message); return false; }
                return true;
            }
            if (n.T == 'd')
            {
                if (!v.Can(sh.P.Cr, n, 5)) { io.E("cp: cannot access '" + shownSrc + "': Permission denied"); return false; }
                Inode dn;
                if (tl.Exists)
                {
                    if (tl.Node.T != 'd') { io.E("cp: cannot overwrite non-directory '" + target + "' with directory '" + shownSrc + "'"); return false; }
                    dn = tl.Node;
                }
                else
                {
                    try { dn = v.Create(sh.P, tl, 'd', n.Mode & 0xFFF); dn.Mode = (n.Mode & 0x1FF & ~(preserve ? 0 : sh.P.Umask)) | (dn.Mode & 0xE00); }
                    catch (VfsEx e) { io.E("cp: cannot create directory '" + target + "': " + e.Message); return false; }
                    if (o.Has("v")) io.L("'" + shownSrc + "' -> '" + target + "'");
                }
                bool ok = true;
                foreach (var k in Vfs.Sorted(n.Kids.Keys))
                {
                    Loc cl;
                    try { cl = v.Resolve(sh.P, Vfs.Join(sl.Path, k), !archive && !o.Has("P")); } catch (VfsEx e) { io.E("cp: cannot access '" + shownSrc + "/" + k + "': " + e.Message); ok = false; continue; }
                    if (!cl.Exists) continue;
                    if (!CopyTree(sh, io, o, cl, shownSrc.TrimEnd('/') + "/" + k, Vfs.Join(target, k), preserve, keepCtx, archive)) ok = false;
                }
                if (preserve) { dn.Mode = n.Mode; if (sh.P.Cr.Uid == 0) { dn.Uid = n.Uid; dn.Gid = n.Gid; } dn.Mtime = n.Mtime; }
                if (keepCtx) dn.Ctx = n.Ctx;
                if (archive && n.Acl != null) { dn.Acl = n.Acl.Select(e => new AclEntry(e.Tag, e.Who, e.Perm)).ToList(); dn.DefAcl = n.DefAcl == null ? null : n.DefAcl.Select(e => new AclEntry(e.Tag, e.Who, e.Perm)).ToList(); }
                return ok;
            }
            string text;
            try { text = v.ReadNode(sh.P, sl); }
            catch (VfsEx e) { io.E("cp: cannot open '" + shownSrc + "' for reading: " + e.Message); return false; }
            if (tl.Exists)
            {
                if (tl.Node == n) { io.E("cp: '" + shownSrc + "' and '" + target + "' are the same file"); return false; }
                if (tl.Node.T == 'd') { io.E("cp: cannot overwrite directory '" + target + "' with non-directory"); return false; }
                if (o.Has("n")) return true;
                if (o.Has("u") && tl.Node.Mtime >= n.Mtime) return true;
                if (o.Has("i") && !o.Has("f") && !Confirm(sh, io, "cp: overwrite '" + target + "'? ")) return true;
            }
            try
            {
                bool existed = tl.Exists;
                Inode dn;
                if (existed && o.Has("f") && !v.Can(sh.P.Cr, tl.Node, 2)) { v.Unlink(sh.P, tl); tl = v.Resolve(sh.P, target, true); existed = false; }
                if (existed) { dn = v.WriteLoc(sh.P, tl, text, false, 0x1B6); }
                else
                {
                    v.CheckSpace(tl.PFs, n.Size);
                    dn = v.Create(sh.P, tl, 'f', n.Mode & 0x1FF);
                    dn.Text = n.Text; dn.BinSize = n.BinSize; dn.Prog = n.Prog; dn.Blob = n.Blob;
                    if (n.Text == null) dn.Text = null;
                }
                if (existed) { dn.Text = n.Text; dn.BinSize = n.BinSize; dn.Prog = n.Prog; dn.Blob = n.Blob; }
                if (preserve) { dn.Mode = n.Mode & 0xFFF; dn.Mtime = n.Mtime; dn.Atime = n.Atime; if (sh.P.Cr.Uid == 0) { dn.Uid = n.Uid; dn.Gid = n.Gid; } }
                if (keepCtx) dn.Ctx = n.Ctx;
                if (archive && n.Acl != null) dn.Acl = n.Acl.Select(e => new AclEntry(e.Tag, e.Who, e.Perm)).ToList();
                if (o.Has("v")) io.L("'" + shownSrc + "' -> '" + target + "'");
            }
            catch (VfsEx e) { io.E("cp: cannot create regular file '" + target + "': " + e.Message); return false; }
            return true;
        }

        static int Mv(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "finvuTt:Z", "force|f,interactive|i,no-clobber|n,verbose|v,update|u,target-directory=|t,no-target-directory|T,context|Z", "mv");
            if (o.Error != null) return Fail(io, o);
            var args = o.Args;
            string targetDir = o.Get("t");
            if (targetDir == null && args.Count < 2) { io.E(args.Count == 0 ? "mv: missing file operand\nTry 'mv --help' for more information." : "mv: missing destination file operand after '" + args[0] + "'\nTry 'mv --help' for more information."); return 1; }
            var srcs = targetDir != null ? args : args.Take(args.Count - 1).ToList();
            string dest = targetDir ?? args[args.Count - 1];
            var v = sh.M.V;
            Loc dl;
            try { dl = Find(sh, dest, true); } catch (VfsEx e) { io.E("mv: cannot move to '" + dest + "': " + e.Message); return 1; }
            bool destIsDir = dl.Exists && dl.Node.T == 'd' && !o.Has("T");
            if (srcs.Count > 1 && !destIsDir) { io.E("mv: target '" + dest + "' is not a directory"); return 1; }
            int st = 0;
            foreach (var s in srcs)
            {
                Loc sl;
                try { sl = Find(sh, s, false); } catch (VfsEx e) { io.E("mv: cannot stat '" + s + "': " + e.Message); st = 1; continue; }
                if (!sl.Exists) { io.E("mv: cannot stat '" + s + "': No such file or directory"); st = 1; continue; }
                string target = destIsDir ? Vfs.Join(dest, Vfs.Base(s.TrimEnd('/'))) : dest;
                Loc tl;
                try { tl = Find(sh, target, false); } catch (VfsEx e) { io.E("mv: cannot move '" + s + "' to '" + target + "': " + e.Message); st = 1; continue; }
                if (tl.Exists && tl.Node == sl.Node) { io.E("mv: '" + s + "' and '" + target + "' are the same file"); st = 1; continue; }
                if (tl.Exists && o.Has("n")) continue;
                if (tl.Exists && o.Has("i") && !o.Has("f") && !Confirm(sh, io, "mv: overwrite '" + target + "'? ")) continue;
                if (sl.Node.T == 'd' && Abs(sh, target).StartsWith(Abs(sh, s).TrimEnd('/') + "/", StringComparison.Ordinal)) { io.E("mv: cannot move '" + s + "' to a subdirectory of itself, '" + target + "'"); st = 1; continue; }
                try
                {
                    if (tl.Exists && tl.Node.T == 'd' && sl.Node.T != 'd') { io.E("mv: cannot overwrite directory '" + target + "' with non-directory"); st = 1; continue; }
                    if (v.IsMountPoint(sl)) throw new VfsEx("EBUSY");
                    if (sl.PFs != tl.PFs)
                    {
                        // another file system: copy (keeping everything, the label too), then remove
                        var copyOpts = Opts.Parse(new List<string> { "cp", "-a" }, "a", "", "cp");
                        if (!CopyTree(sh, io, copyOpts, sl, s, target, true, true, true)) { st = 1; continue; }
                        var errs = new List<string>();
                        v.RemoveTree(sh.P, Find(sh, s, false), errs, s);
                        foreach (var e in errs) io.E("mv: " + e);
                    }
                    else v.Rename(sh.P, sl, tl);
                    if (o.Has("v")) io.L("renamed '" + s + "' -> '" + target + "'");
                }
                catch (VfsEx e) { io.E("mv: cannot move '" + s + "' to '" + target + "': " + e.Message); st = 1; }
            }
            return st;
        }

        // ------------------------------------------------------------ rm / mkdir / rmdir / touch
        static int Rm(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "rRfidvI", "recursive|r,force|f,interactive=?|i,dir|d,verbose|v,no-preserve-root,preserve-root", "rm");
            if (o.Error != null) return Fail(io, o);
            bool force = o.Has("f"), rec = o.Has("r") || o.Has("R");
            bool interactive = o.Has("i") && !(force && a.LastIndexOf("-f") > a.LastIndexOf("-i"));
            if (force && a.IndexOf("-f") > Math.Max(a.IndexOf("-i"), -1)) interactive = false;
            if (o.Args.Count == 0) { if (force) return 0; io.E("rm: missing operand\nTry 'rm --help' for more information."); return 1; }
            int st = 0;
            var v = sh.M.V;
            foreach (var f in o.Args)
            {
                if (Abs(sh, f) == "/" && rec) { io.E("rm: it is dangerous to operate recursively on '/'\nrm: use --no-preserve-root to override this failsafe"); st = 1; continue; }
                Loc l;
                try { l = Find(sh, f, false); }
                catch (VfsEx e) { if (!force) io.E("rm: cannot remove '" + f + "': " + e.Message); st = 1; continue; }
                if (!l.Exists) { if (!force) { io.E("rm: cannot remove '" + f + "': No such file or directory"); st = 1; } continue; }
                if (l.Node.T == 'd')
                {
                    if (!rec && !(o.Has("d") && l.Node.Kids.Count == 0)) { io.E("rm: cannot remove '" + f + "': Is a directory"); st = 1; continue; }
                    if (interactive && !Confirm(sh, io, "rm: descend into directory '" + f + "'? ")) continue;
                    if (!RmTree(sh, io, l, f, interactive, o.Has("v"))) st = 1;
                    continue;
                }
                if (interactive && !Confirm(sh, io, "rm: remove " + Kind(l.Node) + " '" + f + "'? ")) continue;
                if (!force && !interactive && !v.Can(sh.P.Cr, l.Node, 2) && l.Node.T != 'l' && sh.S != null && io.In.IsTty)
                {
                    if (!Confirm(sh, io, "rm: remove write-protected " + Kind(l.Node) + " '" + f + "'? ")) continue;
                }
                try { v.Unlink(sh.P, l); if (o.Has("v")) io.L("removed '" + f + "'"); }
                catch (VfsEx e) { io.E("rm: cannot remove '" + f + "': " + e.Message); st = 1; }
            }
            return st;
        }

        static string Kind(Inode n)
        {
            switch (n.T)
            {
                case 'd': return "directory";
                case 'l': return "symbolic link";
                case 'p': return "fifo";
                case 'b': return "block special file";
                case 'c': return "character special file";
            }
            return n.Size == 0 ? "regular empty file" : "regular file";
        }

        static bool RmTree(Sh sh, Io io, Loc l, string shown, bool interactive, bool verbose)
        {
            var v = sh.M.V;
            bool ok = true;
            if (!v.Can(sh.P.Cr, l.Node, 7) && l.Node.Kids.Count > 0) { io.E("rm: cannot remove '" + shown + "': Permission denied"); return false; }
            foreach (var k in Vfs.Sorted(l.Node.Kids.Keys))
            {
                Loc cl;
                try { cl = v.Resolve(sh.P, Vfs.Join(l.Path, k), false); } catch (VfsEx e) { io.E("rm: cannot remove '" + shown + "/" + k + "': " + e.Message); ok = false; continue; }
                if (!cl.Exists) continue;
                string cs = shown.TrimEnd('/') + "/" + k;
                if (cl.Node.T == 'd' && !v.IsMountPoint(cl))
                {
                    if (interactive && !Confirm(sh, io, "rm: descend into directory '" + cs + "'? ")) { ok = false; continue; }
                    if (!RmTree(sh, io, cl, cs, interactive, verbose)) ok = false;
                    continue;
                }
                if (interactive && !Confirm(sh, io, "rm: remove " + Kind(cl.Node) + " '" + cs + "'? ")) { ok = false; continue; }
                try { if (cl.Node.T == 'd') v.Rmdir(sh.P, cl); else v.Unlink(sh.P, cl); if (verbose) io.L("removed '" + cs + "'"); }
                catch (VfsEx e) { io.E("rm: cannot remove '" + cs + "': " + e.Message); ok = false; }
            }
            if (!ok) return false;
            if (interactive && !Confirm(sh, io, "rm: remove directory '" + shown + "'? ")) return false;
            try { v.Rmdir(sh.P, l); if (verbose) io.L("removed directory '" + shown + "'"); }
            catch (VfsEx e) { io.E("rm: cannot remove '" + shown + "': " + e.Message); return false; }
            return true;
        }

        /// <summary>shred: overwrites a file's contents (it stays, unless -u removes it afterwards).</summary>
        static int Shred(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "fn:uvxz", "force|f,iterations=|n,remove=?|u,verbose|v,exact|x,zero|z", "shred");
            if (o.Error != null) return Fail(io, o);
            if (o.Args.Count == 0) { io.E("shred: missing file operand\nTry 'shred --help' for more information."); return 1; }
            int st = 0;
            foreach (var f in o.Args)
            {
                try
                {
                    var l = Find(sh, f, true);
                    if (!l.Exists) { io.E("shred: " + f + ": failed to open for writing: No such file or directory"); st = 1; continue; }
                    if (l.Node.T != 'f') { io.E("shred: " + f + ": invalid file type"); st = 1; continue; }
                    long size = l.Node.Size;
                    var n = sh.M.V.WriteLoc(sh.P, l, "", false, 0x1B6);
                    n.Text = null; n.Blob = null; n.BinSize = size;
                    if (o.Has("v")) io.E("shred: " + f + ": pass 1/3 (random)...\nshred: " + f + ": pass 2/3 (random)...\nshred: " + f + ": pass 3/3 (random)...");
                    if (o.Has("u")) { sh.M.V.Unlink(sh.P, Find(sh, f, false)); if (o.Has("v")) io.E("shred: " + f + ": removed"); }
                }
                catch (VfsEx e) { io.E("shred: " + f + ": failed to open for writing: " + e.Message); st = 1; }
            }
            return st;
        }

        static int Mkdir(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "pm:vZ", "parents|p,mode=|m,verbose|v,context=?", "mkdir");
            if (o.Error != null) return Fail(io, o);
            if (o.Args.Count == 0) { io.E("mkdir: missing operand\nTry 'mkdir --help' for more information."); return 1; }
            int st = 0;
            foreach (var d in o.Args)
            {
                try
                {
                    if (!o.Has("p")) { var l = Find(sh, d, false); if (l.Exists) { io.E("mkdir: cannot create directory '" + d + "': File exists"); st = 1; continue; } }
                    else { Loc l = null; try { l = Find(sh, d, true); } catch (VfsEx e0) { if (e0.Errno != "ENOENT") throw; } if (l != null && l.Exists) { if (l.Node.T != 'd') { io.E("mkdir: cannot create directory '" + d + "': File exists"); st = 1; } continue; } }
                    var n = sh.M.V.Mkdir(sh.P, d, 0x1FF, o.Has("p"));
                    if (o.Has("m")) { bool ok; int md = ParseMode(o.Get("m"), 0x1FF, 'd', out ok); if (!ok) { io.E("mkdir: invalid mode '" + o.Get("m") + "'"); return 1; } n.Mode = md & 0xFFF; }
                    if (o.Has("v")) io.L("mkdir: created directory '" + d + "'");
                }
                catch (VfsEx e) { io.E("mkdir: cannot create directory '" + d + "': " + e.Message); st = 1; }
            }
            return st;
        }

        static int Rmdir(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "pv", "parents|p,verbose|v,ignore-fail-on-non-empty", "rmdir");
            if (o.Error != null) return Fail(io, o);
            if (o.Args.Count == 0) { io.E("rmdir: missing operand\nTry 'rmdir --help' for more information."); return 1; }
            int st = 0;
            foreach (var d in o.Args)
            {
                var list = new List<string> { d };
                if (o.Has("p")) { string cur = d.TrimEnd('/'); while (cur.Contains("/")) { cur = cur.Substring(0, cur.LastIndexOf('/')); if (cur.Length > 0) list.Add(cur); } }
                foreach (var x in list)
                {
                    try
                    {
                        var l = Find(sh, x, false);
                        if (!l.Exists) throw new VfsEx("ENOENT");
                        sh.M.V.Rmdir(sh.P, l);
                        if (o.Has("v")) io.L("rmdir: removing directory, '" + x + "'");
                    }
                    catch (VfsEx e)
                    {
                        if (o.Has("ignore-fail-on-non-empty") && e.Errno == "ENOTEMPTY") break;
                        io.E("rmdir: failed to remove '" + x + "': " + e.Message); st = 1; break;
                    }
                }
            }
            return st;
        }

        static int Touch(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "acmd:t:r:h", "no-create|c,date=|d,reference=|r,time=,no-dereference|h", "touch");
            if (o.Error != null) return Fail(io, o);
            if (o.Args.Count == 0) { io.E("touch: missing file operand\nTry 'touch --help' for more information."); return 1; }
            long when = sh.M.Now;
            if (o.Has("d")) { long w; if (!ParseDate(sh.M, o.Get("d"), out w)) { io.E("touch: invalid date format '" + o.Get("d") + "'"); return 1; } when = w; }
            if (o.Has("t"))
            {
                var m = Regex.Match(o.Get("t"), "^(\\d{2})?(\\d{2})(\\d{2})(\\d{2})(\\d{2})(\\d{2})(\\.(\\d{2}))?$");
                if (!m.Success) { io.E("touch: invalid date format '" + o.Get("t") + "'"); return 1; }
                var now = sh.M.LocalTime(sh.M.Now);
                int year = m.Groups[1].Success && m.Groups[1].Value.Length > 0 ? 2000 + int.Parse(m.Groups[1].Value) : now.Year;
                var lt = new DateTime(year, int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value), int.Parse(m.Groups[4].Value), int.Parse(m.Groups[5].Value), m.Groups[8].Success && m.Groups[8].Value.Length > 0 ? int.Parse(m.Groups[8].Value) : 0);
                when = LocalToEpoch(sh.M, lt);
            }
            int st = 0;
            foreach (var f in o.Args)
            {
                try
                {
                    var l = Find(sh, f, true);
                    if (!l.Exists)
                    {
                        if (o.Has("c")) continue;
                        if (l.Parent == null) throw new VfsEx("ENOENT");
                        var n = sh.M.V.Create(sh.P, l, 'f', 0x1B6);
                        n.Text = "";
                        n.Mtime = n.Atime = when;
                        continue;
                    }
                    if (l.Node.Uid != sh.P.Cr.Uid && !sh.M.V.Can(sh.P.Cr, l.Node, 2)) throw new VfsEx("EACCES");
                    if (l.Mnt != null && l.Mnt.Ro) throw new VfsEx("EROFS");
                    if (!o.Has("a") || o.Has("m")) l.Node.Mtime = when;
                    if (!o.Has("m") || o.Has("a")) l.Node.Atime = when;
                    l.Node.Ctime = sh.M.Now;
                }
                catch (VfsEx e) { io.E("touch: cannot touch '" + f + "': " + e.Message); st = 1; }
            }
            return st;
        }

        public static long LocalToEpoch(Machine m, DateTime local)
        {
            long guess = (long)(local - new DateTime(1970, 1, 1)).TotalSeconds;
            int off = Tz.OffsetMinutes(m.TimeZoneName, guess);
            return guess - off * 60;
        }

        /// <summary>The date forms people use with date -d / touch -d / at: "2026-03-05 10:00", "tomorrow", "+2 days", "now".</summary>
        public static bool ParseDate(Machine m, string s, out long t)
        {
            t = m.Now;
            s = s.Trim();
            var now = m.LocalTime(m.Now);
            if (s == "" || s == "now") return true;
            if (s.StartsWith("@", StringComparison.Ordinal)) { long e; if (long.TryParse(s.Substring(1), out e)) { t = e; return true; } return false; }
            var rel = Regex.Match(s, "^([+-]?\\s*\\d+)\\s*(second|sec|minute|min|hour|day|week|month|year)s?(\\s+ago)?$");
            if (rel.Success)
            {
                long n = long.Parse(rel.Groups[1].Value.Replace(" ", ""));
                if (rel.Groups[3].Success && rel.Groups[3].Value.Length > 0) n = -n;
                string u = rel.Groups[2].Value;
                long secs = u.StartsWith("sec", StringComparison.Ordinal) ? 1 : u.StartsWith("min", StringComparison.Ordinal) ? 60 : u == "hour" ? 3600 : u == "day" ? 86400 : u == "week" ? 604800 : u == "month" ? 2592000 : 31536000;
                t = m.Now + n * secs; return true;
            }
            if (s == "tomorrow") { t = m.Now + 86400; return true; }
            if (s == "yesterday") { t = m.Now - 86400; return true; }
            DateTime d;
            string[] formats = { "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "MM/dd/yyyy", "MM/dd/yyyy HH:mm", "HH:mm", "HH:mm:ss", "MMM d yyyy", "d MMM yyyy", "yyyyMMdd" };
            if (DateTime.TryParseExact(s, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
            {
                if (s.Length <= 8 && s.Contains(":")) d = new DateTime(now.Year, now.Month, now.Day, d.Hour, d.Minute, d.Second);
                t = LocalToEpoch(m, d); return true;
            }
            return false;
        }

        // ------------------------------------------------------------ cat
        static int Cat(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "nbsAETvet", "number|n,number-nonblank|b,squeeze-blank|s,show-all|A,show-ends|E,show-tabs|T", "cat");
            if (o.Error != null) return Fail(io, o);
            int st = 0;
            if (o.Args.Count == 0 && io.In.IsTty && sh.S != null)
            {
                // cat with no file reads the terminal until Ctrl+D
                string l;
                while ((l = io.In.ReadLine()) != null) io.L(l);
                return 0;
            }
            var ins = Inputs(sh, o.Args, io, "cat", ref st);
            int num = 1;
            foreach (var kv in ins)
            {
                string t = kv.Value;
                if (!o.Has("n") && !o.Has("b") && !o.Has("s") && !o.Has("A") && !o.Has("E") && !o.Has("T")) { io.P(t); continue; }
                var lines = t.Split('\n');
                bool lastBlank = false;
                for (int i = 0; i < lines.Length; i++)
                {
                    if (i == lines.Length - 1 && lines[i].Length == 0) break;
                    string l = lines[i];
                    if (o.Has("s") && l.Length == 0) { if (lastBlank) continue; lastBlank = true; } else lastBlank = false;
                    if (o.Has("A") || o.Has("T")) l = l.Replace("\t", "^I");
                    if (o.Has("A") || o.Has("E")) l += "$";
                    if (o.Has("b")) { if (lines[i].Length > 0) io.L((num++).ToString().PadLeft(6) + "\t" + l); else io.L(l); }
                    else if (o.Has("n")) io.L((num++).ToString().PadLeft(6) + "\t" + l);
                    else io.L(l);
                }
            }
            return st;
        }

        // ------------------------------------------------------------ ln
        static int Ln(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "sfvnrTt:L", "symbolic|s,force|f,verbose|v,no-dereference|n,relative|r,no-target-directory|T,target-directory=|t", "ln");
            if (o.Error != null) return Fail(io, o);
            var v = sh.M.V;
            if (o.Args.Count == 0) { io.E("ln: missing file operand\nTry 'ln --help' for more information."); return 1; }
            var srcs = o.Args.Count == 1 ? o.Args : o.Args.Take(o.Args.Count - 1).ToList();
            string dest = o.Args.Count == 1 ? "." : o.Args[o.Args.Count - 1];
            if (o.Has("t")) { srcs = o.Args; dest = o.Get("t"); }
            Loc dl;
            try { dl = Find(sh, dest, !o.Has("n")); } catch (VfsEx e) { io.E("ln: failed to access '" + dest + "': " + e.Message); return 1; }
            bool destIsDir = dl.Exists && dl.Node.T == 'd' && !o.Has("T");
            if (srcs.Count > 1 && !destIsDir) { io.E("ln: target '" + dest + "' is not a directory"); return 1; }
            int st = 0;
            foreach (var s in srcs)
            {
                string target = destIsDir ? Vfs.Join(dest, Vfs.Base(s)) : dest;
                try
                {
                    var tl = Find(sh, target, false);
                    if (tl.Exists)
                    {
                        if (!o.Has("f")) { io.E("ln: failed to create " + (o.Has("s") ? "symbolic" : "hard") + " link '" + target + "': File exists"); st = 1; continue; }
                        if (tl.Node.T == 'd') { io.E("ln: '" + target + "': cannot overwrite directory"); st = 1; continue; }
                        v.Unlink(sh.P, tl);
                        tl = Find(sh, target, false);
                    }
                    if (o.Has("s")) v.Symlink(sh.P, s, tl);
                    else
                    {
                        var sl = Find(sh, s, false);
                        if (!sl.Exists) { io.E("ln: failed to access '" + s + "': No such file or directory"); st = 1; continue; }
                        if (sl.Node.T == 'd') { io.E("ln: " + s + ": hard link not allowed for directory"); st = 1; continue; }
                        v.HardLink(sh.P, sl, tl);
                    }
                    if (o.Has("v")) io.L("'" + target + "' -> '" + s + "'");
                }
                catch (VfsEx e) { io.E("ln: failed to create " + (o.Has("s") ? "symbolic" : "hard") + " link '" + target + "'" + (e.Errno == "EXDEV" ? " => '" + s + "'" : "") + ": " + e.Message); st = 1; }
            }
            return st;
        }

        // ------------------------------------------------------------ chmod / chown / chgrp
        static int Chmod(Sh sh, List<string> a, Io io)
        {
            // a mode like -w or -x looks like an option: take it as the mode
            var a2 = new List<string>(a);
            string mode = null;
            for (int i = 1; i < a2.Count; i++) if (Regex.IsMatch(a2[i], "^[-+=][rwxXst]+$") && mode == null) { mode = a2[i]; a2.RemoveAt(i); break; }
            var o = Opts.Parse(a2, "Rvcf", "recursive|R,verbose|v,changes|c,silent|f,quiet|f,reference=", "chmod");
            if (o.Error != null) return Fail(io, o);
            var args = o.Args;
            if (mode == null && o.Get("reference") == null) { if (args.Count == 0) { io.E("chmod: missing operand\nTry 'chmod --help' for more information."); return 1; } mode = args[0]; args = args.Skip(1).ToList(); }
            if (args.Count == 0) { io.E("chmod: missing operand after '" + mode + "'\nTry 'chmod --help' for more information."); return 1; }
            int st = 0;
            foreach (var f in args)
            {
                try
                {
                    var l = Find(sh, f, true);
                    if (!l.Exists) { io.E("chmod: cannot access '" + f + "': No such file or directory"); st = 1; continue; }
                    if (!ChmodOne(sh, io, l, f, mode, o)) st = 1;
                }
                catch (VfsEx e) { io.E("chmod: cannot access '" + f + "': " + e.Message); st = 1; }
            }
            return st;
        }
        static bool ChmodOne(Sh sh, Io io, Loc l, string shown, string mode, Opts o)
        {
            bool ok;
            int old = l.Node.Mode;
            int nm = ParseMode(mode, old, l.Node.T, out ok);
            if (!ok) { io.E("chmod: invalid mode: '" + mode + "'\nTry 'chmod --help' for more information."); return false; }
            try { sh.M.V.Chmod(sh.P, l, nm); }
            catch (VfsEx e) { io.E("chmod: changing permissions of '" + shown + "': " + e.Message); return false; }
            if (o.Has("v") || (o.Has("c") && old != nm)) io.L("mode of '" + shown + "' " + (old == nm ? "retained as " : "changed from ") + Convert.ToString(old & 0xFFF, 8).PadLeft(4, '0') + " (" + Vfs.ModeString(new Inode { T = l.Node.T, Mode = old }).Substring(1) + ")" + (old == nm ? "" : " to " + Convert.ToString(nm & 0xFFF, 8).PadLeft(4, '0') + " (" + Vfs.ModeString(l.Node).Substring(1) + ")"));
            bool all = true;
            if (o.Has("R") && l.Node.T == 'd')
                foreach (var k in Vfs.Sorted(l.Node.Kids.Keys))
                {
                    Loc cl;
                    try { cl = sh.M.V.Resolve(sh.P, Vfs.Join(l.Path, k), false); } catch (VfsEx e) { io.E("chmod: cannot access '" + shown + "/" + k + "': " + e.Message); all = false; continue; }
                    if (!cl.Exists || cl.Node.T == 'l') continue;
                    if (!ChmodOne(sh, io, cl, shown.TrimEnd('/') + "/" + k, mode, o)) all = false;
                }
            return all;
        }

        static int Chown(Sh sh, List<string> a, Io io) { return ChownImpl(sh, a, io, false); }
        static int Chgrp(Sh sh, List<string> a, Io io) { return ChownImpl(sh, a, io, true); }
        static int ChownImpl(Sh sh, List<string> a, Io io, bool grpOnly)
        {
            string prog = grpOnly ? "chgrp" : "chown";
            var o = Opts.Parse(a, "RvchfHLP", "recursive|R,verbose|v,changes|c,no-dereference|h,silent|f,reference=,from=", prog);
            if (o.Error != null) return Fail(io, o);
            if (o.Args.Count < 2) { io.E(prog + ": missing operand\nTry '" + prog + " --help' for more information."); return 1; }
            string spec = o.Args[0];
            int uid = -1, gid = -1;
            if (grpOnly) { gid = Gid(sh.M, spec); if (gid < 0) { io.E("chgrp: invalid group: '" + spec + "'"); return 1; } }
            else
            {
                int sep = spec.IndexOfAny(new[] { ':', '.' });
                string u = sep >= 0 ? spec.Substring(0, sep) : spec, g = sep >= 0 ? spec.Substring(sep + 1) : null;
                if (u.Length > 0) { uid = Uid(sh.M, u); if (uid < 0) { io.E("chown: invalid user: '" + spec + "'"); return 1; } }
                if (g != null && g.Length > 0) { gid = Gid(sh.M, g); if (gid < 0) { io.E("chown: invalid group: '" + spec + "'"); return 1; } }
                else if (g != null && u.Length > 0) { var pw = sh.M.User(u); if (pw != null) gid = pw.Gid; }
            }
            int st = 0;
            foreach (var f in o.Args.Skip(1))
            {
                try
                {
                    var l = Find(sh, f, !o.Has("h"));
                    if (!l.Exists) { io.E(prog + ": cannot access '" + f + "': No such file or directory"); st = 1; continue; }
                    if (!ChownOne(sh, io, l, f, uid, gid, o, prog)) st = 1;
                }
                catch (VfsEx e) { io.E(prog + ": cannot access '" + f + "': " + e.Message); st = 1; }
            }
            return st;
        }
        static bool ChownOne(Sh sh, Io io, Loc l, string shown, int uid, int gid, Opts o, string prog)
        {
            try { sh.M.V.Chown(sh.P, l, uid, gid); }
            catch (VfsEx e) { io.E(prog + ": changing " + (uid >= 0 ? "ownership" : "group") + " of '" + shown + "': " + e.Message); return false; }
            if (o.Has("v")) io.L((uid >= 0 ? "ownership" : "group") + " of '" + shown + "' retained as " + (uid >= 0 ? Owner(sh.M, l.Node.Uid) + (gid >= 0 ? ":" + Grp(sh.M, l.Node.Gid) : "") : Grp(sh.M, l.Node.Gid)));
            bool ok = true;
            if (o.Has("R") && l.Node.T == 'd')
                foreach (var k in Vfs.Sorted(l.Node.Kids.Keys))
                {
                    Loc cl;
                    try { cl = sh.M.V.Resolve(sh.P, Vfs.Join(l.Path, k), false); } catch (VfsEx) { ok = false; continue; }
                    if (!cl.Exists) continue;
                    if (!ChownOne(sh, io, cl, shown.TrimEnd('/') + "/" + k, uid, gid, o, prog)) ok = false;
                }
            return ok;
        }

        // ------------------------------------------------------------ stat
        static int Stat(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "c:LfZt", "format=|c,dereference|L,file-system|f,printf=,terse|t", "stat");
            if (o.Error != null) return Fail(io, o);
            if (o.Args.Count == 0) { io.E("stat: missing operand\nTry 'stat --help' for more information."); return 1; }
            int st = 0;
            foreach (var f in o.Args)
            {
                Loc l;
                try { l = Find(sh, f, o.Has("L")); } catch (VfsEx e) { io.E("stat: cannot statx '" + f + "': " + e.Message); st = 1; continue; }
                if (!l.Exists) { io.E("stat: cannot statx '" + f + "': No such file or directory"); st = 1; continue; }
                var n = l.Node; var m = sh.M;
                string fmt = o.Get("c") ?? o.Get("printf");
                if (o.Has("f"))
                {
                    var fs = l.Fs;
                    io.L("  File: \"" + f + "\"\n    ID: 0        Namelen: 255     Type: " + fs.Type + "\nBlock size: 4096       Fundamental block size: 4096\nBlocks: Total: " + fs.Bytes / 4096 + "    Free: " + (fs.Bytes - fs.Used) / 4096 + "    Available: " + (fs.Bytes - fs.Used) / 4096 + "\nInodes: Total: " + fs.Bytes / 2048 + "    Free: " + (fs.Bytes / 2048 - fs.Nodes.Count));
                    continue;
                }
                if (fmt != null) { io.P(StatFormat(sh, fmt, l, f) + (o.Has("printf") ? "" : "\n")); continue; }
                string type = n.T == 'd' ? "directory" : n.T == 'l' ? "symbolic link" : n.T == 'b' ? "block special file" : n.T == 'c' ? "character special file" : n.T == 'p' ? "fifo" : (n.Size == 0 ? "regular empty file" : "regular file");
                io.L("  File: " + f + (n.T == 'l' ? " -> " + n.Target : ""));
                io.L("  Size: " + n.Size.ToString().PadRight(15) + "Blocks: " + ((n.Size + 4095) / 4096 * 8).ToString().PadRight(10) + " IO Block: 4096   " + type);
                io.L("Device: fd00h/64768d\tInode: " + n.Ino.ToString().PadRight(11) + " Links: " + n.Nlink);
                io.L("Access: (" + Convert.ToString(n.Mode & 0xFFF, 8).PadLeft(4, '0') + "/" + Vfs.ModeString(n) + ")  Uid: (" + n.Uid.ToString().PadLeft(5) + "/" + Owner(m, n.Uid).PadLeft(8) + ")   Gid: (" + n.Gid.ToString().PadLeft(5) + "/" + Grp(m, n.Gid).PadLeft(8) + ")");
                io.L("Context: " + n.Ctx);
                io.L("Access: " + FullDate(m, n.Atime));
                io.L("Modify: " + FullDate(m, n.Mtime));
                io.L("Change: " + FullDate(m, n.Ctime));
                io.L(" Birth: " + FullDate(m, Math.Min(n.Ctime, n.Mtime)));
            }
            return st;
        }
        static string StatFormat(Sh sh, string fmt, Loc l, string name)
        {
            var n = l.Node; var m = sh.M;
            var sb = new StringBuilder();
            for (int i = 0; i < fmt.Length; i++)
            {
                if (fmt[i] == '\\' && i + 1 < fmt.Length) { sb.Append(Printf.Escapes(fmt.Substring(i, 2), false)); i++; continue; }
                if (fmt[i] != '%' || i + 1 >= fmt.Length) { sb.Append(fmt[i]); continue; }
                char c = fmt[++i];
                switch (c)
                {
                    case 'a': sb.Append(Convert.ToString(n.Mode & 0xFFF, 8)); break;
                    case 'A': sb.Append(Vfs.ModeString(n)); break;
                    case 'U': sb.Append(Owner(m, n.Uid)); break;
                    case 'u': sb.Append(n.Uid); break;
                    case 'G': sb.Append(Grp(m, n.Gid)); break;
                    case 'g': sb.Append(n.Gid); break;
                    case 's': sb.Append(n.Size); break;
                    case 'n': sb.Append(name); break;
                    case 'N': sb.Append("'" + name + "'" + (n.T == 'l' ? " -> '" + n.Target + "'" : "")); break;
                    case 'i': sb.Append(n.Ino); break;
                    case 'h': sb.Append(n.Nlink); break;
                    case 'F': sb.Append(n.T == 'd' ? "directory" : n.T == 'l' ? "symbolic link" : n.T == 'b' ? "block special file" : n.T == 'c' ? "character special file" : n.Size == 0 ? "regular empty file" : "regular file"); break;
                    case 'C': sb.Append(n.Ctx); break;
                    case 'y': sb.Append(FullDate(m, n.Mtime)); break;
                    case 'Y': sb.Append(n.Mtime); break;
                    case 'x': sb.Append(FullDate(m, n.Atime)); break;
                    case 'X': sb.Append(n.Atime); break;
                    case 'z': sb.Append(FullDate(m, n.Ctime)); break;
                    case 'Z': sb.Append(n.Ctime); break;
                    case 'b': sb.Append((n.Size + 511) / 512); break;
                    case 'B': sb.Append(512); break;
                    case 'm': sb.Append(l.Mnt == null ? "/" : l.Mnt.Path); break;
                    case 'f': sb.Append(Convert.ToString(n.Mode | (n.T == 'd' ? 0x4000 : 0x8000), 16)); break;
                    case 'T': sb.Append(l.Fs.Type); break;
                    case '%': sb.Append('%'); break;
                    default: sb.Append('%').Append(c); break;
                }
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------ find
        class FindExpr { public string Op; public string Arg; public List<string> Exec; public bool ExecPlus; public FindExpr L, R; public bool Not; }

        static int FindCmd(Sh sh, List<string> a, Io io)
        {
            var paths = new List<string>();
            int i = 1;
            // options before the paths
            while (i < a.Count && (a[i] == "-L" || a[i] == "-H" || a[i] == "-P")) i++;
            while (i < a.Count && !a[i].StartsWith("-", StringComparison.Ordinal) && a[i] != "(" && a[i] != "!") paths.Add(a[i++]);
            if (paths.Count == 0) paths.Add(".");
            var toks = a.Skip(i).ToList();
            int maxDepth = int.MaxValue, minDepth = 0;
            // global options
            for (int k = 0; k < toks.Count; k++)
            {
                if (toks[k] == "-maxdepth" && k + 1 < toks.Count) { int.TryParse(toks[k + 1], out maxDepth); toks.RemoveRange(k, 2); k--; }
                else if (toks[k] == "-mindepth" && k + 1 < toks.Count) { int.TryParse(toks[k + 1], out minDepth); toks.RemoveRange(k, 2); k--; }
                else if (toks[k] == "-xdev" || toks[k] == "-mount" || toks[k] == "-depth" || toks[k] == "-noleaf" || toks[k] == "-daystart") { toks.RemoveAt(k); k--; }
            }
            FindExpr expr;
            int pos = 0;
            try { expr = toks.Count == 0 ? null : FindOr(toks, ref pos); }
            catch (ShError e) { io.E("find: " + e.Message); return 1; }
            if (pos < toks.Count) { io.E("find: paths must precede expression: `" + toks[pos] + "'"); return 1; }
            bool hasAction = HasAction(expr);
            int st = 0;
            var plusBatches = new Dictionary<FindExpr, List<string>>();
            foreach (var p in paths)
            {
                Loc l;
                try { l = Find(sh, p, true); } catch (VfsEx e) { io.E("find: '" + p + "': " + e.Message); st = 1; continue; }
                if (!l.Exists) { io.E("find: '" + p + "': No such file or directory"); st = 1; continue; }
                FindWalk(sh, io, l, p, 0, maxDepth, minDepth, expr, hasAction, ref st, plusBatches);
            }
            foreach (var kv in plusBatches)
            {
                var cmd = new List<string>();
                foreach (var w in kv.Key.Exec) { if (w == "{}") cmd.AddRange(kv.Value); else cmd.Add(w); }
                if (cmd.Count > 0) sh.Run(cmd, io);
            }
            return st;
        }
        static bool HasAction(FindExpr e)
        {
            if (e == null) return false;
            if (e.Op == "-print" || e.Op == "-exec" || e.Op == "-ok" || e.Op == "-delete" || e.Op == "-ls" || e.Op == "-print0" || e.Op == "-fprint" || e.Op == "-printf") return true;
            return HasAction(e.L) || HasAction(e.R);
        }
        static FindExpr FindOr(List<string> t, ref int p)
        {
            var l = FindAnd(t, ref p);
            while (p < t.Count && (t[p] == "-o" || t[p] == "-or")) { p++; var r = FindAnd(t, ref p); l = new FindExpr { Op = "or", L = l, R = r }; }
            return l;
        }
        static FindExpr FindAnd(List<string> t, ref int p)
        {
            var l = FindNot(t, ref p);
            while (p < t.Count && t[p] != "-o" && t[p] != "-or" && t[p] != ")")
            {
                if (t[p] == "-a" || t[p] == "-and") p++;
                var r = FindNot(t, ref p);
                l = new FindExpr { Op = "and", L = l, R = r };
            }
            return l;
        }
        static FindExpr FindNot(List<string> t, ref int p)
        {
            if (p < t.Count && (t[p] == "!" || t[p] == "-not")) { p++; var e = FindNot(t, ref p); return new FindExpr { Op = "not", L = e }; }
            if (p < t.Count && t[p] == "(") { p++; var e = FindOr(t, ref p); if (p >= t.Count || t[p] != ")") throw new ShError("invalid expression; I was expecting to find a ')' somewhere but did not see one."); p++; return e; }
            if (p >= t.Count) throw new ShError("invalid expression; you have used a binary operator '-a' with nothing before it.");
            string op = t[p++];
            var unary = new[] { "-print", "-print0", "-delete", "-ls", "-empty", "-nouser", "-nogroup", "-executable", "-readable", "-writable", "-true", "-false", "-quit", "-prune" };
            if (unary.Contains(op)) return new FindExpr { Op = op };
            if (op == "-exec" || op == "-ok" || op == "-execdir")
            {
                var cmd = new List<string>();
                bool plus = false;
                while (p < t.Count && t[p] != ";" && !(t[p] == "+" && cmd.Count > 0 && cmd[cmd.Count - 1] == "{}")) cmd.Add(t[p++]);
                if (p >= t.Count) throw new ShError("missing argument to `" + op + "'");
                if (t[p] == "+") plus = true;
                p++;
                return new FindExpr { Op = op == "-execdir" ? "-exec" : op, Exec = cmd, ExecPlus = plus };
            }
            var withArg = new[] { "-name", "-iname", "-type", "-user", "-group", "-perm", "-size", "-mtime", "-mmin", "-atime", "-amin", "-ctime", "-cmin", "-newer", "-path", "-ipath", "-regex", "-iregex", "-inum", "-links", "-uid", "-gid", "-samefile", "-context", "-printf", "-fprint", "-wholename" };
            if (withArg.Contains(op))
            {
                if (p >= t.Count) throw new ShError("missing argument to `" + op + "'");
                return new FindExpr { Op = op, Arg = t[p++] };
            }
            throw new ShError("unknown predicate `" + op + "'");
        }

        static void FindWalk(Sh sh, Io io, Loc l, string shown, int depth, int maxDepth, int minDepth, FindExpr expr, bool hasAction, ref int st, Dictionary<FindExpr, List<string>> plus)
        {
            if (depth >= minDepth)
            {
                bool match = expr == null || FindEval(sh, io, expr, l, shown, plus);
                if (match && !hasAction) io.L(shown);
            }
            if (l.Node == null || l.Node.T != 'd' || depth >= maxDepth) return;
            if (!sh.M.V.Can(sh.P.Cr, l.Node, 5)) { io.E("find: '" + shown + "': Permission denied"); st = 1; return; }
            if (l.Fs.Type == "proc" && depth > 0) return;
            foreach (var k in l.Node.Kids.Keys.ToList())
            {
                Loc cl;
                try { cl = sh.M.V.Resolve(sh.P, Vfs.Join(l.Path, k), false); } catch (VfsEx) { continue; }
                if (!cl.Exists) continue;
                FindWalk(sh, io, cl, shown == "/" ? "/" + k : shown.TrimEnd('/') + "/" + k, depth + 1, maxDepth, minDepth, expr, hasAction, ref st, plus);
            }
        }

        static bool NumCmp(string spec, double v)
        {
            if (spec.StartsWith("+", StringComparison.Ordinal)) { double n; return double.TryParse(spec.Substring(1), NumberStyles.Float, CultureInfo.InvariantCulture, out n) && v > n; }
            if (spec.StartsWith("-", StringComparison.Ordinal)) { double n; return double.TryParse(spec.Substring(1), NumberStyles.Float, CultureInfo.InvariantCulture, out n) && v < n; }
            double x; return double.TryParse(spec, NumberStyles.Float, CultureInfo.InvariantCulture, out x) && Math.Abs(v - x) < 0.0001;
        }

        static bool FindEval(Sh sh, Io io, FindExpr e, Loc l, string shown, Dictionary<FindExpr, List<string>> plus)
        {
            var n = l.Node; var m = sh.M;
            switch (e.Op)
            {
                case "and": return FindEval(sh, io, e.L, l, shown, plus) && FindEval(sh, io, e.R, l, shown, plus);
                case "or": return FindEval(sh, io, e.L, l, shown, plus) || FindEval(sh, io, e.R, l, shown, plus);
                case "not": return !FindEval(sh, io, e.L, l, shown, plus);
                case "-true": return true;
                case "-false": return false;
                case "-prune": case "-quit": return true;
                case "-print": io.L(shown); return true;
                case "-print0": io.P(shown + "\0"); return true;
                case "-ls": io.L(n.Ino.ToString().PadLeft(9) + " " + ((n.Size + 1023) / 1024 * 4).ToString().PadLeft(4) + " " + Vfs.ModeString(n) + " " + n.Nlink.ToString().PadLeft(3) + " " + Owner(m, n.Uid).PadRight(8) + " " + Grp(m, n.Gid).PadRight(8) + " " + n.Size.ToString().PadLeft(8) + " " + LsDate(m, n.Mtime) + " " + shown + (n.T == 'l' ? " -> " + n.Target : "")); return true;
                case "-printf": io.P(FindPrintf(sh, e.Arg, l, shown)); return true;
                case "-delete":
                    try { if (n.T == 'd') m.V.Rmdir(sh.P, l); else m.V.Unlink(sh.P, l); } catch (VfsEx ex) { io.E("find: cannot delete '" + shown + "': " + ex.Message); }
                    return true;
                case "-exec": case "-ok":
                    {
                        if (e.ExecPlus) { List<string> lst; if (!plus.TryGetValue(e, out lst)) { lst = new List<string>(); plus[e] = lst; } lst.Add(shown); return true; }
                        var cmd = e.Exec.Select(w => w.Replace("{}", shown)).ToList();
                        if (cmd.Count == 0) return false;
                        if (e.Op == "-ok" && !Confirm(sh, io, "< " + cmd[0] + " ... " + shown + " > ? ")) return false;
                        return sh.Run(cmd, io) == 0;
                    }
                case "-name": return Sh.GlobMatch(e.Arg, Vfs.Base(shown) == "" ? shown : Vfs.Base(shown));
                case "-iname": return Sh.GlobMatch(e.Arg.ToLowerInvariant(), Vfs.Base(shown).ToLowerInvariant());
                case "-path": case "-wholename": return Sh.GlobMatch(e.Arg, shown);
                case "-ipath": return Sh.GlobMatch(e.Arg.ToLowerInvariant(), shown.ToLowerInvariant());
                case "-regex": return Regex.IsMatch(shown, "^(" + e.Arg + ")$");
                case "-iregex": return Regex.IsMatch(shown, "^(" + e.Arg + ")$", RegexOptions.IgnoreCase);
                case "-type":
                    {
                        char t = n.T;
                        foreach (var x in e.Arg.Split(',')) if (x.Length == 1 && (x[0] == t || (x[0] == 'f' && t == 'f'))) return true;
                        return false;
                    }
                case "-user": { int u = Uid(m, e.Arg); if (u < 0) throw new ShError("'" + e.Arg + "' is not the name of a known user"); return n.Uid == u; }
                case "-group": { int g = Gid(m, e.Arg); if (g < 0) throw new ShError("'" + e.Arg + "' is not the name of an existing group"); return n.Gid == g; }
                case "-uid": return n.Uid.ToString() == e.Arg;
                case "-gid": return n.Gid.ToString() == e.Arg;
                case "-nouser": return m.UserById(n.Uid) == null;
                case "-nogroup": return m.GroupById(n.Gid) == null;
                case "-empty": return n.T == 'd' ? n.Kids.Count == 0 : n.T == 'f' && n.Size == 0;
                case "-executable": return m.V.Can(sh.P.Cr, n, 1);
                case "-readable": return m.V.Can(sh.P.Cr, n, 4);
                case "-writable": return m.V.Can(sh.P.Cr, n, 2);
                case "-inum": return n.Ino.ToString() == e.Arg;
                case "-links": return NumCmp(e.Arg, n.Nlink);
                case "-context": return Sh.GlobMatch(e.Arg, n.Ctx) || Sh.GlobMatch(e.Arg, Selinux.TypeOf(n.Ctx));
                case "-samefile": { try { var o2 = Find(sh, e.Arg, false); return o2.Node == n; } catch (VfsEx) { return false; } }
                case "-perm":
                    {
                        string s = e.Arg; int mode;
                        bool all = s.StartsWith("-", StringComparison.Ordinal), any = s.StartsWith("/", StringComparison.Ordinal);
                        string ms = all || any ? s.Substring(1) : s;
                        bool ok;
                        mode = ParseMode(ms, 0, 'f', out ok);
                        if (!ok) throw new ShError("invalid mode '" + s + "'");
                        int have = n.Mode & 0xFFF;
                        if (all) return (have & mode) == mode;
                        if (any) return mode == 0 || (have & mode) != 0;
                        return have == mode;
                    }
                case "-size":
                    {
                        var mm = Regex.Match(e.Arg, "^([+-]?)(\\d+)([bckwMG]?)$");
                        if (!mm.Success) throw new ShError("invalid -size type `" + e.Arg + "'");
                        long unit = 512;
                        switch (mm.Groups[3].Value) { case "c": unit = 1; break; case "k": unit = 1024; break; case "M": unit = 1024 * 1024; break; case "G": unit = 1024L * 1024 * 1024; break; case "w": unit = 2; break; }
                        long units = (n.Size + unit - 1) / unit;
                        long want = long.Parse(mm.Groups[2].Value);
                        if (mm.Groups[1].Value == "+") return units > want;
                        if (mm.Groups[1].Value == "-") return units < want;
                        return units == want;
                    }
                case "-mtime": return NumCmp(e.Arg, Math.Floor((m.Now - n.Mtime) / 86400.0));
                case "-atime": return NumCmp(e.Arg, Math.Floor((m.Now - n.Atime) / 86400.0));
                case "-ctime": return NumCmp(e.Arg, Math.Floor((m.Now - n.Ctime) / 86400.0));
                case "-mmin": return NumCmp(e.Arg, Math.Ceiling((m.Now - n.Mtime) / 60.0));
                case "-amin": return NumCmp(e.Arg, Math.Ceiling((m.Now - n.Atime) / 60.0));
                case "-cmin": return NumCmp(e.Arg, Math.Ceiling((m.Now - n.Ctime) / 60.0));
                case "-newer": { try { var o2 = Find(sh, e.Arg, true); return o2.Exists && n.Mtime > o2.Node.Mtime; } catch (VfsEx) { return false; } }
            }
            return false;
        }
        static string FindPrintf(Sh sh, string fmt, Loc l, string shown)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < fmt.Length; i++)
            {
                if (fmt[i] == '\\' && i + 1 < fmt.Length) { sb.Append(Printf.Escapes(fmt.Substring(i, 2), false)); i++; continue; }
                if (fmt[i] != '%' || i + 1 >= fmt.Length) { sb.Append(fmt[i]); continue; }
                char c = fmt[++i];
                var n = l.Node;
                switch (c)
                {
                    case 'p': sb.Append(shown); break;
                    case 'f': sb.Append(Vfs.Base(shown)); break;
                    case 'h': sb.Append(Vfs.Parent(shown)); break;
                    case 's': sb.Append(n.Size); break;
                    case 'u': sb.Append(Owner(sh.M, n.Uid)); break;
                    case 'g': sb.Append(Grp(sh.M, n.Gid)); break;
                    case 'm': sb.Append(Convert.ToString(n.Mode & 0xFFF, 8)); break;
                    case 'M': sb.Append(Vfs.ModeString(n)); break;
                    case 'i': sb.Append(n.Ino); break;
                    case 'y': sb.Append(n.T == 'f' ? 'f' : n.T); break;
                    case 'Z': sb.Append(n.Ctx); break;
                    case '%': sb.Append('%'); break;
                    default: sb.Append('%').Append(c); break;
                }
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------ du / df
        static int Du(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "shacbkmxd:", "summarize|s,human-readable|h,all|a,total|c,max-depth=|d,apparent-size,bytes|b", "du");
            if (o.Error != null) return Fail(io, o);
            var targets = o.Args.Count == 0 ? new List<string> { "." } : o.Args;
            int maxd = o.Has("s") ? 0 : (o.Has("d") ? int.Parse(o.Get("d")) : int.MaxValue);
            long total = 0;
            int st = 0;
            foreach (var t in targets)
            {
                Loc l;
                try { l = Find(sh, t, true); } catch (VfsEx e) { io.E("du: cannot access '" + t + "': " + e.Message); st = 1; continue; }
                if (!l.Exists) { io.E("du: cannot access '" + t + "': No such file or directory"); st = 1; continue; }
                total += DuWalk(sh, io, l, t, 0, maxd, o, ref st);
            }
            if (o.Has("c")) io.L(DuSize(o, total) + "\ttotal");
            return st;
        }
        static string DuSize(Opts o, long bytes) { return o.Has("h") ? Human(bytes) : o.Has("b") ? bytes.ToString() : o.Has("m") ? ((bytes + 1048575) / 1048576).ToString() : ((bytes + 1023) / 1024).ToString(); }
        static long DuWalk(Sh sh, Io io, Loc l, string shown, int depth, int maxd, Opts o, ref int st)
        {
            var n = l.Node;
            long size = n.T == 'f' ? (o.Has("b") || o.Has("apparent-size") ? n.Size : (n.Size + 4095) / 4096 * 4096) : (n.T == 'd' ? 0 : 0);
            if (n.T == 'd')
            {
                if (!sh.M.V.Can(sh.P.Cr, n, 5)) { io.E("du: cannot read directory '" + shown + "': Permission denied"); st = 1; }
                else
                    foreach (var k in Vfs.Sorted(n.Kids.Keys))
                    {
                        Loc cl;
                        try { cl = sh.M.V.Resolve(sh.P, Vfs.Join(l.Path, k), false); } catch (VfsEx) { continue; }
                        if (!cl.Exists || sh.M.V.IsMountPoint(cl) && cl.Fs != l.Fs) { if (cl.Exists && cl.Node.T == 'd') { size += DuWalk(sh, io, cl, shown.TrimEnd('/') + "/" + k, depth + 1, maxd, o, ref st); } continue; }
                        size += DuWalk(sh, io, cl, shown.TrimEnd('/') + "/" + k, depth + 1, maxd, o, ref st);
                    }
                if (depth <= maxd) io.L(DuSize(o, size) + "\t" + shown);
            }
            else if (o.Has("a") && depth <= maxd || depth == 0) io.L(DuSize(o, size) + "\t" + shown);
            return size;
        }

        static int Df(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "hHTiPlkmat:x:", "human-readable|h,si|H,print-type|T,inodes|i,portability|P,local|l,all|a,type=|t,exclude-type=|x,output=?", "df");
            if (o.Error != null) return Fail(io, o);
            var m = sh.M;
            var rows = new List<string[]>();
            bool T = o.Has("T"), inodes = o.Has("i");
            var mounts = new List<Mount>();
            if (o.Args.Count > 0)
            {
                foreach (var f in o.Args)
                {
                    Loc l;
                    try { l = Find(sh, f, true); } catch (VfsEx e) { io.E("df: " + f + ": " + e.Message); return 1; }
                    if (!l.Exists) { var d = m.FindDev(f); if (d == null) { io.E("df: " + f + ": No such file or directory"); return 1; } var c = m.ContentOf(d); var mm = c == null ? null : m.Mounts.FirstOrDefault(x => x.FsId == c.FsId); if (mm != null) mounts.Add(mm); continue; }
                    var dev = m.FindDev(f);
                    if (l.Node.T == 'b' && dev != null) { var c = m.ContentOf(dev); var mm = c == null ? null : m.Mounts.FirstOrDefault(x => x.FsId == c.FsId); if (mm != null) { mounts.Add(mm); continue; } }
                    mounts.Add(l.Mnt ?? m.V.MountOf(l.GPath));
                }
            }
            else mounts = m.Mounts.Where(x => !x.Bind && x.FsType != "proc" && x.FsType != "sysfs" && x.FsType != "autofs" && x.FsType != "overlay" && x.FsType != "rootfs" && (o.Has("a") || x.FsType != "devtmpfs" || true)).ToList();
            if (o.Has("t")) mounts = mounts.Where(x => x.FsType == o.Get("t") || (o.Get("t") == "nfs" && x.FsType.StartsWith("nfs", StringComparison.Ordinal))).ToList();
            if (o.Has("x")) mounts = mounts.Where(x => x.FsType != o.Get("x")).ToList();
            foreach (var mt in mounts)
            {
                if (mt == null) continue;
                var fs = m.V.Fs(mt.FsId);
                if (fs == null) continue;
                long size = fs.Bytes, used = Math.Min(fs.Used, size), avail = Math.Max(0, size - used - (fs.Type == "ext4" ? (long)(size * 0.05) : 0));
                if (fs.Type == "devtmpfs") { size = 4194304; used = 0; avail = size; }
                string src = mt.Source;
                if (fs.Type == "devtmpfs") src = "devtmpfs";
                string use = size == 0 ? "-" : ((int)Math.Ceiling(used * 100.0 / size)).ToString() + "%";
                if (inodes)
                {
                    long it = Math.Max(fs.Nodes.Count, size / 2048), iu = fs.Nodes.Count;
                    rows.Add(new[] { src, fs.Type, it.ToString(), iu.ToString(), (it - iu).ToString(), it == 0 ? "-" : ((int)Math.Ceiling(iu * 100.0 / it)) + "%", mt.Path });
                    continue;
                }
                Func<long, string> f2 = x => o.Has("h") ? Human(x) : o.Has("H") ? Machine.Size(x, true) : o.Has("m") ? ((x + 1048575) / 1048576).ToString() : ((x + 1023) / 1024).ToString();
                rows.Add(new[] { src, fs.Type, f2(size), f2(used), f2(avail), use, mt.Path });
            }
            string[] head = inodes ? new[] { "Filesystem", "Type", "Inodes", "IUsed", "IFree", "IUse%", "Mounted on" } : new[] { "Filesystem", "Type", o.Has("h") || o.Has("H") ? "Size" : (o.Has("m") ? "1M-blocks" : "1K-blocks"), "Used", o.Has("h") ? "Avail" : "Available", "Use%", "Mounted on" };
            var all = new List<string[]> { head }; all.AddRange(rows);
            if (o.Has("output"))
            {
                // --output=source,fstype,size,used,avail,pcent,target (the order given)
                var fields = (o.Get("output").Length > 0 ? o.Get("output") : "source,fstype,size,used,avail,pcent,target").Split(',').Select(x => x.Trim()).ToList();
                var map = new Dictionary<string, int> { { "source", 0 }, { "fstype", 1 }, { "size", 2 }, { "itotal", 2 }, { "used", 3 }, { "iused", 3 }, { "avail", 4 }, { "iavail", 4 }, { "pcent", 5 }, { "ipcent", 5 }, { "target", 6 }, { "file", 6 } };
                foreach (var f in fields) if (!map.ContainsKey(f)) { io.E("df: option --output: field '" + f + "' unknown\nTry 'df --help' for more information."); return 1; }
                if (!o.Has("h") && !o.Has("m")) head[2] = "1K-blocks";
                var outRows = all.Select(r => fields.Select(f => r[map[f]]).ToArray()).ToList();
                var ow = new int[fields.Count];
                foreach (var r in outRows) for (int i = 0; i < fields.Count; i++) ow[i] = Math.Max(ow[i], r[i].Length);
                foreach (var r in outRows)
                    io.L(string.Join(" ", r.Select((x, i) => { int c = map[fields[i]]; return c >= 2 && c <= 5 ? x.PadLeft(ow[i]) : (i == r.Length - 1 ? x : x.PadRight(ow[i])); }).ToArray()));
                return 0;
            }
            var w = new int[7];
            foreach (var r in all) for (int i = 0; i < 7; i++) w[i] = Math.Max(w[i], r[i].Length);
            foreach (var r in all)
            {
                var sb = new StringBuilder(r[0].PadRight(w[0]));
                if (T) sb.Append(" ").Append(r[1].PadRight(w[1]));
                for (int i = 2; i <= 5; i++) sb.Append(" ").Append(r[i].PadLeft(w[i]));
                sb.Append(" ").Append(r[6]);
                io.L(sb.ToString());
            }
            return 0;
        }

        // ------------------------------------------------------------ file, tree, realpath...
        static int FileCmd(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "bisLz", "brief|b,mime|i,special-files|s,dereference|L", "file");
            if (o.Error != null) return Fail(io, o);
            foreach (var f in o.Args)
            {
                Loc l;
                try { l = Find(sh, f, o.Has("L")); } catch (VfsEx e) { io.L(f + ": cannot open `" + f + "' (" + e.Message + ")"); continue; }
                if (!l.Exists) { io.L(f + ": cannot open `" + f + "' (No such file or directory)"); continue; }
                io.L((o.Has("b") ? "" : f + ": ") + FileType(sh, l));
            }
            return 0;
        }
        static string FileType(Sh sh, Loc l)
        {
            var n = l.Node;
            switch (n.T)
            {
                case 'd': return "directory";
                case 'l': return "symbolic link to " + n.Target;
                case 'c': return "character special (" + DevNums(sh.M, n).Replace(" ", "") + ")";
                case 'p': return "fifo (named pipe)";
            }
            if (n.T == 'b')
            {
                var d = sh.M.Devices().FirstOrDefault(x => x.Name == n.Dev || "dm-" + x.Minor == n.Dev);
                var c = sh.M.ContentOf(d);
                if (c != null && c.Kind == "xfs") return "block special (" + DevNums(sh.M, n).Replace(" ", "") + ")";
                return "block special (" + DevNums(sh.M, n).Replace(" ", "") + ")";
            }
            if (n.Prog != null) return "ELF 64-bit LSB pie executable, x86-64, version 1 (SYSV), dynamically linked, interpreter /lib64/ld-linux-x86-64.so.2, BuildID[sha1]=" + ((uint)FwHash.Of(n.Prog)).ToString("x8") + "0c9f1e2a3b4c5d6e7f8091a2b3c4d5e6f, for GNU/Linux 3.2.0, stripped";
            if (n.Blob != null) return ArchiveKind(n.Blob);
            if (n.Text == null) return n.BinSize == 0 ? "empty" : "data";
            string t = n.Text;
            if (t.Length == 0) return "empty";
            if (t.StartsWith("\u0000SWAPSPACE2", StringComparison.Ordinal)) return "Linux swap file, 4k page size, little endian, version 1, size " + (n.BinSize / 4096) + " pages, 0 bad pages, no label, UUID=" + sh.M.NewUuid();
            if (t.StartsWith("#!/bin/bash", StringComparison.Ordinal) || t.StartsWith("#!/usr/bin/bash", StringComparison.Ordinal) || t.StartsWith("#!/usr/bin/env bash", StringComparison.Ordinal)) return "Bourne-Again shell script, ASCII text executable";
            if (t.StartsWith("#!/bin/sh", StringComparison.Ordinal)) return "POSIX shell script, ASCII text executable";
            if (t.StartsWith("<?xml", StringComparison.Ordinal)) return "XML 1.0 document, ASCII text";
            if (t.TrimStart().StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase) || t.TrimStart().StartsWith("<html", StringComparison.OrdinalIgnoreCase)) return "HTML document, ASCII text";
            if (t.StartsWith("-----BEGIN PGP", StringComparison.Ordinal)) return "PGP public key block Public-Key (old)";
            if (t.StartsWith("ssh-rsa", StringComparison.Ordinal) || t.StartsWith("ssh-ed25519", StringComparison.Ordinal) || t.StartsWith("ecdsa-", StringComparison.Ordinal)) return "OpenSSH " + (t.StartsWith("ssh-rsa", StringComparison.Ordinal) ? "RSA" : t.StartsWith("ssh-ed25519", StringComparison.Ordinal) ? "ED25519" : "ECDSA") + " public key";
            if (t.StartsWith("-----BEGIN OPENSSH PRIVATE KEY", StringComparison.Ordinal)) return "OpenSSH private key";
            bool ascii = t.All(ch => ch < 128);
            bool longLines = t.Split('\n').Any(x => x.Length > 300);
            return (ascii ? "ASCII text" : "Unicode text, UTF-8 text") + (longLines ? ", with very long lines" : "") + (t.EndsWith("\n", StringComparison.Ordinal) ? "" : ", with no line terminators");
        }

        static int Tree(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "adfL:pugDi", "", "tree");
            if (o.Error != null) return Fail(io, o);
            var root = o.Args.Count > 0 ? o.Args[0] : ".";
            Loc l;
            try { l = Find(sh, root, true); } catch (VfsEx e) { io.L(root + " [error opening dir]"); return 2; }
            if (!l.Exists) { io.L(root + "  [error opening dir]\n\n0 directories, 0 files"); return 2; }
            int dirs = 0, files = 0;
            int maxL = o.Has("L") ? int.Parse(o.Get("L")) : int.MaxValue;
            io.L(root);
            TreeWalk(sh, io, o, l, "", 1, maxL, ref dirs, ref files);
            io.L("\n" + dirs + " director" + (dirs == 1 ? "y" : "ies") + (o.Has("d") ? "" : ", " + files + " file" + (files == 1 ? "" : "s")));
            return 0;
        }
        static void TreeWalk(Sh sh, Io io, Opts o, Loc l, string prefix, int depth, int maxL, ref int dirs, ref int files)
        {
            if (depth > maxL || !sh.M.V.Can(sh.P.Cr, l.Node, 5)) return;
            var names = Vfs.Sorted(l.Node.Kids.Keys.Where(k => o.Has("a") || !k.StartsWith(".", StringComparison.Ordinal)));
            var ents = new List<Loc>();
            foreach (var k in names) { try { var cl = sh.M.V.Resolve(sh.P, Vfs.Join(l.Path, k), false); if (cl.Exists && (!o.Has("d") || cl.Node.T == 'd')) ents.Add(cl); } catch (VfsEx) { } }
            for (int i = 0; i < ents.Count; i++)
            {
                var c = ents[i];
                bool last = i == ents.Count - 1;
                string label = (o.Has("p") ? "[" + Vfs.ModeString(c.Node) + "]  " : "") + c.Name + (c.Node.T == 'l' ? " -> " + c.Node.Target : "");
                io.L(prefix + (last ? "\u2514\u2500\u2500 " : "\u251c\u2500\u2500 ") + label);
                if (c.Node.T == 'd') { dirs++; TreeWalk(sh, io, o, c, prefix + (last ? "    " : "\u2502   "), depth + 1, maxL, ref dirs, ref files); }
                else files++;
            }
        }

        static int Realpath(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "esmqL", "canonicalize-existing|e,no-symlinks|s,quiet|q", "realpath");
            int st = 0;
            foreach (var f in o.Args)
            {
                try
                {
                    var l = Find(sh, f, true);
                    if (!l.Exists && (o.Has("e") || l.Parent == null)) { io.E("realpath: " + f + ": No such file or directory"); st = 1; continue; }
                    io.L(o.Has("s") ? Abs(sh, f) : l.Path);
                }
                catch (VfsEx e) { io.E("realpath: " + f + ": " + e.Message); st = 1; }
            }
            return st;
        }
        static int Readlink(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "femnqsvz", "canonicalize|f,canonicalize-existing|e,canonicalize-missing|m,no-newline|n", "readlink");
            int st = 0;
            foreach (var f in o.Args)
            {
                try
                {
                    if (o.Has("f") || o.Has("e") || o.Has("m")) { var l = Find(sh, f, true); if (!l.Exists && o.Has("e")) { st = 1; continue; } io.P(l.Path + (o.Has("n") ? "" : "\n")); continue; }
                    var ln = Find(sh, f, false);
                    if (!ln.Exists || ln.Node.T != 'l') { st = 1; continue; }
                    io.P(ln.Node.Target + (o.Has("n") ? "" : "\n"));
                }
                catch (VfsEx) { st = 1; }
            }
            return st;
        }
        static int Basename(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "as:z", "multiple|a,suffix=|s", "basename");
            if (o.Args.Count == 0) { io.E("basename: missing operand\nTry 'basename --help' for more information."); return 1; }
            var names = o.Has("a") || o.Has("s") ? o.Args : o.Args.Take(1).ToList();
            string suffix = o.Get("s") ?? (!o.Has("a") && o.Args.Count > 1 ? o.Args[1] : null);
            foreach (var n in names)
            {
                string b = n == "/" ? "/" : Vfs.Base(n);
                if (suffix != null && b.EndsWith(suffix, StringComparison.Ordinal) && b != suffix) b = b.Substring(0, b.Length - suffix.Length);
                io.L(b);
            }
            return 0;
        }
        static int Dirname(Sh sh, List<string> a, Io io)
        {
            if (a.Count < 2) { io.E("dirname: missing operand\nTry 'dirname --help' for more information."); return 1; }
            foreach (var n in a.Skip(1))
            {
                string p = n.TrimEnd('/');
                int i = p.LastIndexOf('/');
                io.L(i < 0 ? "." : i == 0 ? "/" : p.Substring(0, i));
            }
            return 0;
        }
        static int Mktemp(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "dup:t", "directory|d,dry-run|u,tmpdir=?|p,quiet|q", "mktemp");
            string tmpl = o.Args.Count > 0 ? o.Args[0] : "tmp.XXXXXXXXXX";
            string dir = o.Has("p") && o.Get("p").Length > 0 ? o.Get("p") : (o.Args.Count > 0 && o.Args[0].Contains("/") ? "" : "/tmp");
            var r = new Random(sh.M.Seed + (sh.M.Counter++) * 11);
            const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var sb = new StringBuilder();
            foreach (char c in tmpl) sb.Append(c == 'X' ? chars[r.Next(chars.Length)] : c);
            string path = dir.Length > 0 ? dir.TrimEnd('/') + "/" + sb : sb.ToString();
            try
            {
                var l = Find(sh, path, false);
                if (o.Has("d")) { var n = sh.M.V.Create(sh.P, l, 'd', 0x1C0); n.Mode = 0x1C0; }
                else { var n = sh.M.V.Create(sh.P, l, 'f', 0x180); n.Mode = 0x180; n.Text = ""; }
            }
            catch (VfsEx e) { io.E("mktemp: failed to create " + (o.Has("d") ? "directory" : "file") + " via template '" + tmpl + "': " + e.Message); return 1; }
            io.L(path);
            return 0;
        }

        static int Dd(Sh sh, List<string> a, Io io)
        {
            string inf = null, outf = null; long bs = 512, count = -1; bool quiet = false;
            foreach (var x in a.Skip(1))
            {
                int eq = x.IndexOf('=');
                if (eq < 0) { io.E("dd: unrecognized operand '" + x + "'\nTry 'dd --help' for more information."); return 1; }
                string k = x.Substring(0, eq), v = x.Substring(eq + 1);
                switch (k)
                {
                    case "if": inf = v; break;
                    case "of": outf = v; break;
                    case "bs": bs = ParseSize(v.Replace("B", ""), 1); break;
                    case "count": long.TryParse(v, out count); break;
                    case "status": quiet = v == "none"; break;
                    case "conv": case "oflag": case "iflag": case "seek": case "skip": break;
                    default: io.E("dd: unrecognized operand '" + x + "'"); return 1;
                }
            }
            if (bs <= 0) { io.E("dd: invalid number"); return 1; }
            bool infinite = (inf == "/dev/zero" || inf == "/dev/urandom" || inf == "/dev/random") && count < 0;
            if (infinite)
            {
                // a CPU / disk hog: runs until stopped
                if (sh.Background) { sh.LongRunning = true; sh.BgCpu = 99.7; sh.BgComm = "dd"; sh.BgCmd = string.Join(" ", a.ToArray()); return 0; }
                if (sh.S != null) { try { sh.S.WaitForBreak(); } catch (BreakSignal) { if (!quiet) io.E("^C12345678+0 records in\n12345678+0 records out\n6320987136 bytes (6.3 GB, 5.9 GiB) copied, 4.21 s, 1.5 GB/s"); return 130; } }
                return 1;
            }
            long bytes;
            string text = null;
            if (inf != null && inf != "/dev/zero" && inf != "/dev/urandom")
            {
                try
                {
                    var il = Find(sh, inf, true);
                    if (!il.Exists) { io.E("dd: failed to open '" + inf + "': No such file or directory"); return 1; }
                    if (il.Node.T == 'b') { var d = sh.M.FindDev(il.Path) ?? sh.M.Devices().FirstOrDefault(x => x.Name == il.Node.Dev); bytes = d == null ? 0 : d.Bytes; }
                    else { text = sh.M.V.ReadNode(sh.P, il); bytes = il.Node.Size; }
                }
                catch (VfsEx e) { io.E("dd: failed to open '" + inf + "': " + e.Message); return 1; }
                if (count >= 0) bytes = Math.Min(bytes, count * bs);
            }
            else if (inf == null) { text = io.In.ReadAll(); bytes = Encoding.UTF8.GetByteCount(text); }
            else bytes = count * bs;
            if (outf != null && outf != "/dev/null")
            {
                try
                {
                    var ol = Find(sh, outf, true);
                    if (ol.Exists && ol.Node.T == 'b')
                    {
                        // writing zeros over a device wipes what was on it
                        var d = sh.M.FindDev(ol.Path) ?? sh.M.Devices().FirstOrDefault(x => x.Name == ol.Node.Dev);
                        if (d != null && sh.P.Cr.Uid == 0 && bytes >= 1024 * 1024) sh.M.SetContent(d, null);
                        if (d != null && d.Type == "disk" && bytes >= 1024 * 1024 && sh.P.Cr.Uid == 0) { d.Disk.Label = ""; d.Disk.Parts.Clear(); sh.M.SyncDev(); }
                    }
                    else
                    {
                        sh.M.V.CheckSpace(ol.Exists ? ol.Fs : ol.PFs, bytes);
                        var n = sh.M.V.WriteLoc(sh.P, ol, "", false, 0x1B6);
                        if (text != null && inf != null && inf != "/dev/zero") n.Text = text; else { n.Text = null; n.BinSize = bytes; }
                    }
                }
                catch (VfsEx e) { io.E("dd: failed to open '" + outf + "': " + e.Message); return 1; }
            }
            else if (outf == null && text != null) io.P(text);
            if (!quiet)
            {
                long recs = bs > 0 ? (bytes + bs - 1) / bs : 0;
                io.E(recs + "+0 records in\n" + recs + "+0 records out\n" + bytes + " bytes (" + Machine.Size(bytes, true).Replace("M", " MB").Replace("G", " GB").Replace("K", " kB") + ", " + Machine.Size(bytes, false).Replace("M", " MiB").Replace("G", " GiB").Replace("K", " KiB") + ") copied, 0.0412 s, " + Machine.Size(bytes * 24, true).Replace("M", " MB") + "/s");
            }
            return 0;
        }

        static int Tee(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "ai", "append|a,ignore-interrupts|i", "tee");
            if (o.Error != null) return Fail(io, o);
            string text = io.In.ReadAll();
            int st = 0;
            foreach (var f in o.Args)
            {
                try
                {
                    if (f == "/dev/null") continue;
                    var l = Find(sh, f, true);
                    sh.M.V.WriteLoc(sh.P, l, text, o.Has("a"), 0x1B6);
                }
                catch (VfsEx e) { io.E("tee: " + f + ": " + e.Message); st = 1; }
            }
            io.P(text);
            return st;
        }

        static int Truncate(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "s:c", "size=|s,no-create|c", "truncate");
            if (o.Error != null) return Fail(io, o);
            if (!o.Has("s")) { io.E("truncate: you must specify either '--size' or '--reference'\nTry 'truncate --help' for more information."); return 1; }
            long size = ParseSize(o.Get("s"), 1);
            int st = 0;
            foreach (var f in o.Args)
            {
                try
                {
                    var l = Find(sh, f, true);
                    var n = sh.M.V.WriteLoc(sh.P, l, "", false, 0x1B6);
                    if (size > 0) { n.Text = null; n.BinSize = size; }
                }
                catch (VfsEx e) { io.E("truncate: cannot open '" + f + "' for writing: " + e.Message); st = 1; }
            }
            return st;
        }

        static int Install(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "dm:o:g:vDcpCt:", "directory|d,mode=|m,owner=|o,group=|g,verbose|v", "install");
            if (o.Error != null) return Fail(io, o);
            int mode = o.Has("m") ? Convert.ToInt32(o.Get("m"), 8) : 0x1ED;
            int uid = o.Has("o") ? Uid(sh.M, o.Get("o")) : -1, gid = o.Has("g") ? Gid(sh.M, o.Get("g")) : -1;
            try
            {
                if (o.Has("d"))
                {
                    foreach (var d in o.Args) { var n = sh.M.V.Mkdir(sh.P, d, 0x1ED, true); n.Mode = mode; var l = Find(sh, d, true); if (uid >= 0 || gid >= 0) sh.M.V.Chown(sh.P, l, uid, gid); }
                    return 0;
                }
                if (o.Args.Count < 2) { io.E("install: missing destination file operand after '" + (o.Args.Count > 0 ? o.Args[0] : "") + "'"); return 1; }
                string dest = o.Args[o.Args.Count - 1];
                foreach (var s in o.Args.Take(o.Args.Count - 1))
                {
                    var sl = Find(sh, s, true);
                    if (!sl.Exists) { io.E("install: cannot stat '" + s + "': No such file or directory"); return 1; }
                    var dl = Find(sh, dest, true);
                    string target = dl.Exists && dl.Node.T == 'd' ? Vfs.Join(dest, Vfs.Base(s)) : dest;
                    var tl = Find(sh, target, true);
                    var n = sh.M.V.WriteLoc(sh.P, tl, sh.M.V.ReadNode(sh.P, sl), false, 0x1ED);
                    n.Prog = sl.Node.Prog; n.BinSize = sl.Node.BinSize; if (sl.Node.Text == null) n.Text = null;
                    n.Mode = mode;
                    if (uid >= 0 || gid >= 0) sh.M.V.Chown(sh.P, Find(sh, target, true), uid, gid);
                    if (o.Has("v")) io.L("'" + s + "' -> '" + target + "'");
                }
            }
            catch (VfsEx e) { io.E("install: " + e.Message); return 1; }
            return 0;
        }

        static int Mkfifo(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "m:", "mode=|m", "mkfifo");
            foreach (var f in o.Args)
            {
                try { var n = sh.M.V.Create(sh.P, Find(sh, f, false), 'p', 0x1B6); }
                catch (VfsEx e) { io.E("mkfifo: cannot create fifo '" + f + "': " + e.Message); return 1; }
            }
            return 0;
        }

        static int Namei(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "lmovx", "long|l,modes|m,owners|o", "namei");
            foreach (var f in o.Args)
            {
                io.L("f: " + f);
                string cur = f.StartsWith("/", StringComparison.Ordinal) ? "" : sh.P.Cwd;
                var parts = new List<string>(); if (f.StartsWith("/", StringComparison.Ordinal)) parts.Add("/");
                parts.AddRange(f.Split('/').Where(x => x.Length > 0));
                string path = f.StartsWith("/", StringComparison.Ordinal) ? "" : sh.P.Cwd;
                foreach (var p in parts)
                {
                    path = p == "/" ? "/" : (path == "/" || path == "" ? "/" + p : path + "/" + p);
                    if (path == "//" ) path = "/";
                    Loc l;
                    try { l = Find(sh, path, false); } catch (VfsEx e) { io.L("  " + p + " - " + e.Message); break; }
                    if (!l.Exists) { io.L("  " + p + " - No such file or directory"); break; }
                    var n = l.Node;
                    string kind = n.T == 'd' ? "d" : n.T == 'l' ? "l" : "-";
                    if (o.Has("l") || o.Has("m")) io.L(" " + Vfs.ModeString(n) + (o.Has("l") || o.Has("o") ? " " + Owner(sh.M, n.Uid).PadRight(8) + " " + Grp(sh.M, n.Gid).PadRight(8) : "") + " " + p + (n.T == 'l' ? " -> " + n.Target : ""));
                    else io.L(" " + kind + " " + p + (n.T == 'l' ? " -> " + n.Target : ""));
                }
            }
            return 0;
        }
    }
}
