// Cert Lab - the simulated file systems: inodes with owners, modes, ACLs and SELinux labels, file systems that live
// on block devices, a mount table (with bind and automount points) and path lookup that enforces permissions
// the way Linux does (search permission on every directory, sticky directories, read-only mounts, ACL masks).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public class Inode
    {
        public int Ino;
        public char T = 'f';            // f file, d directory, l symlink, b block device, c char device, p fifo, s socket
        public int Mode = 0x1A4;         // permission bits incl. setuid 04000, setgid 02000, sticky 01000
        public int Uid, Gid;
        public int Nlink = 1;
        public long Mtime, Ctime, Atime;
        public string Text;              // file contents (text); null with BinSize >= 0 for binary data
        public long BinSize = -1;
        public string Target;            // symlink target
        public Dictionary<string, int> Kids;   // directory entries
        public List<AclEntry> Acl;       // extended entries (named users and groups) + the owning group's entry; mask lives in Mode's group bits
        public List<AclEntry> DefAcl;    // default ACL of a directory (full list incl. user:: group:: other:: mask::)
        public string Ctx = "system_u:object_r:unlabeled_t:s0";
        public string Dev;               // b / c: the device's name (vdb, null, zero...)
        public string Prog;              // a program of the simulator (/usr/bin/ls -> "ls")
        public string Pkg;               // package that owns it
        public string Blob;              // packed contents (archives, compressed files); Size is then BinSize

        public long Size
        {
            get
            {
                if (T == 'f') return Blob != null ? Math.Max(0, BinSize) : Text != null ? Encoding.UTF8.GetByteCount(Text) : Math.Max(0, BinSize);
                if (T == 'd') return Kids == null ? 6 : Math.Max(6, 6 + Kids.Count * 18);
                if (T == 'l') return Target == null ? 0 : Target.Length;
                return 0;
            }
        }
        public bool HasExtAcl { get { return Acl != null && Acl.Count > 0; } }
    }

    public class AclEntry
    {
        public string Tag;   // user, group, mask, other
        public string Who;   // "" for the base entries, else a user / group id (number)
        public int Perm;     // rwx = 4 2 1
        public AclEntry() { }
        public AclEntry(string tag, string who, int perm) { Tag = tag; Who = who; Perm = perm; }
    }

    public class FileSys
    {
        public string Id;
        public string Type;      // xfs ext4 vfat swap tmpfs proc sysfs devtmpfs nfs4 autofs rootfs(initramfs)
        public string Uuid = "";
        public string Label = "";
        public long Bytes;       // capacity
        public long BaseUsed;    // what the pretend operating system already uses
        public int RootIno = 1;
        public int NextIno = 2;
        public Dictionary<int, Inode> Nodes = new Dictionary<int, Inode>();
        public string Device = "";   // the block device it was made on (for messages)
        public bool Dirty;           // ext4 not cleanly unmounted (fsck message only)
        public string NfsExport;     // nfs: the export it is

        public Inode Node(int ino) { Inode n; Nodes.TryGetValue(ino, out n); return n; }
        public Inode Root { get { return Node(RootIno); } }
        public Inode NewNode(char t, int mode, int uid, int gid, long now)
        {
            var n = new Inode { Ino = NextIno++, T = t, Mode = mode, Uid = uid, Gid = gid, Mtime = now, Ctime = now, Atime = now };
            if (t == 'd') { n.Kids = new Dictionary<string, int>(StringComparer.Ordinal); n.Nlink = 2; }
            Nodes[n.Ino] = n;
            return n;
        }
        public long Used
        {
            get
            {
                long u = BaseUsed;
                foreach (var n in Nodes.Values) if (n.T == 'f') u += (n.Size + 4095) / 4096 * 4096; else if (n.T == 'd') u += 0;
                return u;
            }
        }
        public long InodesUsed { get { return Nodes.Count; } }
    }

    public class Mount
    {
        public string Path;       // global path
        public string FsId;
        public int RootIno;       // the directory of the file system that appears here (bind mounts: not its root)
        public string Source;     // /dev/vdb1, server:/export, tmpfs, /srv/x (bind)
        public string FsType;
        public string Opts = "rw";
        public bool Ro;
        public bool Bind;
        public string AutoMap;    // autofs: the map file of an indirect map / direct map key
        public string AutoOpts;
        public bool AutoDirect;
        public string AutoTimeoutKey;
    }

    public class Cred
    {
        public int Uid, Gid;
        public List<int> Groups = new List<int>();
        public string User = "root";
        public bool IsRoot { get { return Uid == 0; } }
        public Cred Copy() { return new Cred { Uid = Uid, Gid = Gid, Groups = new List<int>(Groups), User = User }; }
        public bool InGroup(int g) { return Gid == g || Groups.Contains(g); }
    }

    /// <summary>Where a process stands: who it is, its directory, its root (chroot) and umask.</summary>
    public class PCtx
    {
        public Cred Cr = new Cred();
        public string Root = "/";     // global path of the process's root directory
        public string Cwd = "/";      // path as the process sees it
        public int Umask = 0x12;      // 022
        public string Domain = "unconfined_t";
        public PCtx Copy() { return new PCtx { Cr = Cr.Copy(), Root = Root, Cwd = Cwd, Umask = Umask, Domain = Domain }; }
    }

    public class VfsEx : Exception
    {
        public readonly string Errno;
        public VfsEx(string errno) : base(Msg(errno)) { Errno = errno; }
        public static string Msg(string e)
        {
            switch (e)
            {
                case "ENOENT": return "No such file or directory";
                case "ENOTDIR": return "Not a directory";
                case "EACCES": return "Permission denied";
                case "EPERM": return "Operation not permitted";
                case "EROFS": return "Read-only file system";
                case "EEXIST": return "File exists";
                case "EISDIR": return "Is a directory";
                case "ENOTEMPTY": return "Directory not empty";
                case "ELOOP": return "Too many levels of symbolic links";
                case "EXDEV": return "Invalid cross-device link";
                case "ENOSPC": return "No space left on device";
                case "EBUSY": return "Device or resource busy";
                case "EINVAL": return "Invalid argument";
                case "ENODEV": return "No such device";
                case "EIO": return "Input/output error";
            }
            return e;
        }
    }

    public class Loc
    {
        public FileSys Fs; public Inode Node;      // null Node: the last part does not exist (Parent is where it would go)
        public FileSys PFs; public Inode Parent;
        public string Name = "";
        public string Path = "/";                  // as the process sees it, canonical
        public string GPath = "/";                 // global
        public Mount Mnt;                          // the mount the node (or where it would be) is on
        public bool Exists { get { return Node != null; } }
        public bool IsDir { get { return Node != null && Node.T == 'd'; } }
    }

    public class Vfs
    {
        public readonly Machine M;
        public Vfs(Machine m) { M = m; }

        List<Mount> Mounts { get { return M.Mounts; } }
        public FileSys Fs(string id)
        {
            FileSys f;
            if (id == null) return null;
            if (M.Fss.TryGetValue(id, out f)) return f;
            if (M.Net != null && M.Net.NfsFs.TryGetValue(id, out f)) return f;
            if (M.RunFs.TryGetValue(id, out f)) return f;
            return null;
        }

        public static string Join(string a, string b) { if (b.StartsWith("/", StringComparison.Ordinal)) return b; if (a.EndsWith("/", StringComparison.Ordinal)) return a + b; return a + "/" + b; }

        public static string Norm(string p)
        {
            var parts = new List<string>();
            foreach (var s in p.Split('/'))
            {
                if (s.Length == 0 || s == ".") continue;
                if (s == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); continue; }
                parts.Add(s);
            }
            return "/" + string.Join("/", parts);
        }

        public static string Global(PCtx c, string vis) { if (c == null || c.Root == "/") return vis; return vis == "/" ? c.Root : c.Root + vis; }

        public Mount MountAt(string gpath)
        {
            for (int i = Mounts.Count - 1; i >= 0; i--) if (Mounts[i].Path == gpath) return Mounts[i];
            return null;
        }

        /// <summary>The mount a global path is on (the longest mount point above it).</summary>
        public Mount MountOf(string gpath)
        {
            Mount best = null; int len = -1;
            for (int i = Mounts.Count - 1; i >= 0; i--)
            {
                var m = Mounts[i];
                bool under = gpath == m.Path || m.Path == "/" || gpath.StartsWith(m.Path + "/", StringComparison.Ordinal);
                if (under && m.Path.Length > len) { best = m; len = m.Path.Length; }
            }
            return best;
        }

        public static int Perm(Inode n, Cred c, int ownerShift)
        {
            return (n.Mode >> ownerShift) & 7;
        }

        /// <summary>Linux's permission check incl. POSIX ACLs; root may read and write anything and execute what has any x.</summary>
        public bool Can(Cred c, Inode n, int want)
        {
            if (c == null) return true;
            if (c.Uid == 0)
            {
                if ((want & 1) == 0) return true;
                return n.T == 'd' || (n.Mode & 0x49) != 0;
            }
            if (n.Uid == c.Uid) return (((n.Mode >> 6) & 7) & want) == want;
            if (n.HasExtAcl)
            {
                int mask = (n.Mode >> 3) & 7;
                foreach (var e in n.Acl) if (e.Tag == "user" && e.Who == c.Uid.ToString(CultureInfo.InvariantCulture)) return ((e.Perm & mask) & want) == want;
                bool groupMatch = false;
                foreach (var e in n.Acl)
                {
                    if (e.Tag != "group") continue;
                    int gid = e.Who == "" ? n.Gid : int.Parse(e.Who, CultureInfo.InvariantCulture);
                    if (c.InGroup(gid)) { groupMatch = true; if (((e.Perm & mask) & want) == want) return true; }
                }
                if (groupMatch) return false;
                return ((n.Mode & 7) & want) == want;
            }
            if (c.InGroup(n.Gid)) return (((n.Mode >> 3) & 7) & want) == want;
            return ((n.Mode & 7) & want) == want;
        }

        public Loc Resolve(PCtx c, string path, bool followLast) { return Walk(c, path, followLast, 0, true); }
        public Loc ResolveNoCheck(PCtx c, string path, bool followLast) { return Walk(c, path, followLast, 0, false); }

        /// <summary>Looks a path up. Throws ENOENT / ENOTDIR / EACCES / ELOOP for problems before the last part;
        /// a missing last part gives a Loc with Node == null.</summary>
        Loc Walk(PCtx c, string path, bool followLast, int depth, bool check)
        {
            if (depth > 40) throw new VfsEx("ELOOP");
            if (path == null || path.Length == 0) throw new VfsEx("ENOENT");
            string root = c == null ? "/" : c.Root;
            string start = path.StartsWith("/", StringComparison.Ordinal) ? "/" : (c == null ? "/" : c.Cwd);
            var names = new List<string>();
            foreach (var s in start.Split('/')) if (s.Length > 0) names.Add(s);
            var comps = path.Split('/').Where(s => s.Length > 0).ToList();
            // walk the start (cwd) without checks again: just rebuild the chain
            var chainFs = new List<FileSys>(); var chainNode = new List<Inode>(); var chainMnt = new List<Mount>();
            Mount rm = MountOf(root);
            FileSys fs; Inode node; Mount mnt;
            LocateGlobal(root, out fs, out node, out mnt);
            if (node == null) throw new VfsEx("ENOENT");
            chainFs.Add(fs); chainNode.Add(node); chainMnt.Add(mnt);
            var visNames = new List<string>();
            // re-walk the starting directory (cwd) by name
            var all = new List<string>(names); all.AddRange(comps);
            int startCount = names.Count;
            for (int i = 0; i < all.Count; i++)
            {
                string nm = all[i];
                bool last = i == all.Count - 1;
                bool fromCwd = i < startCount;
                if (nm == ".") continue;
                if (nm == "..")
                {
                    if (visNames.Count > 0) { visNames.RemoveAt(visNames.Count - 1); chainFs.RemoveAt(chainFs.Count - 1); chainNode.RemoveAt(chainNode.Count - 1); chainMnt.RemoveAt(chainMnt.Count - 1); }
                    if (last) return MakeLoc(chainFs, chainNode, chainMnt, visNames, root, null, null, null, "");
                    continue;
                }
                var curFs = chainFs[chainFs.Count - 1]; var cur = chainNode[chainNode.Count - 1];
                if (cur.T != 'd') throw new VfsEx("ENOTDIR");
                if (check && !fromCwd && !Can(c == null ? null : c.Cr, cur, 1)) throw new VfsEx("EACCES");
                if (curFs.Type == "proc") M.RefreshProc(curFs, cur);
                int ino;
                Inode child = null;
                if (cur.Kids.TryGetValue(nm, out ino)) child = curFs.Node(ino);
                string gpChild = GJoin(root, visNames, nm);
                if (child == null && curFs.Type == "autofs")
                {
                    if (M.AutoMount(gpChild)) { if (cur.Kids.TryGetValue(nm, out ino)) child = curFs.Node(ino); }
                }
                if (child == null)
                {
                    if (last) return MakeLoc(chainFs, chainNode, chainMnt, visNames, root, null, curFs, cur, nm);
                    throw new VfsEx("ENOENT");
                }
                if (child.T == 'l' && (!last || followLast))
                {
                    string rest = string.Join("/", all.Skip(i + 1).ToArray());
                    string target = child.Target ?? "";
                    string np;
                    if (target.StartsWith("/", StringComparison.Ordinal)) np = target + (rest.Length > 0 ? "/" + rest : "");
                    else np = "/" + string.Join("/", visNames.ToArray()) + "/" + target + (rest.Length > 0 ? "/" + rest : "");
                    var c2 = c == null ? null : c.Copy();
                    var lr = Walk(c2, np, followLast, depth + 1, check);
                    return lr;
                }
                // a mount on top of this directory?
                Mount over = MountAt(gpChild);
                FileSys nfs = curFs; Inode nn = child; Mount nm2 = chainMnt[chainMnt.Count - 1];
                if (over != null && child.T == 'd')
                {
                    var mf = Fs(over.FsId);
                    if (mf != null) { nfs = mf; nn = mf.Node(over.RootIno) ?? mf.Root; nm2 = over; }
                }
                visNames.Add(nm); chainFs.Add(nfs); chainNode.Add(nn); chainMnt.Add(nm2);
            }
            if (chainNode.Count == 1) return MakeLoc(chainFs, chainNode, chainMnt, visNames, root, null, null, null, "");
            return MakeLoc(chainFs, chainNode, chainMnt, visNames, root, null, null, null, "");
        }

        static string GJoin(string root, List<string> vis, string nm)
        {
            var sb = new StringBuilder(root == "/" ? "" : root);
            foreach (var v in vis) sb.Append('/').Append(v);
            sb.Append('/').Append(nm);
            return sb.ToString();
        }

        Loc MakeLoc(List<FileSys> fss, List<Inode> nodes, List<Mount> mnts, List<string> vis, string root, Inode unused, FileSys pfs, Inode parent, string missing)
        {
            var l = new Loc();
            if (parent != null)
            {
                l.PFs = pfs; l.Parent = parent; l.Name = missing; l.Node = null; l.Fs = pfs;
                l.Mnt = mnts[mnts.Count - 1];
                string vp = "/" + string.Join("/", vis.ToArray());
                l.Path = vp == "/" ? "/" + missing : vp + "/" + missing;
            }
            else
            {
                l.Fs = fss[fss.Count - 1]; l.Node = nodes[nodes.Count - 1]; l.Mnt = mnts[mnts.Count - 1];
                if (nodes.Count >= 2) { l.PFs = fss[fss.Count - 2]; l.Parent = nodes[nodes.Count - 2]; }
                l.Name = vis.Count > 0 ? vis[vis.Count - 1] : "/";
                l.Path = "/" + string.Join("/", vis.ToArray());
            }
            l.GPath = root == "/" ? l.Path : (l.Path == "/" ? root : root + l.Path);
            return l;
        }

        /// <summary>Finds a global path from the top of the mount tree (no permission checks, symlinks followed).</summary>
        public void LocateGlobal(string gpath, out FileSys fs, out Inode node, out Mount mnt)
        {
            mnt = MountAt("/");
            fs = mnt == null ? null : Fs(mnt.FsId);
            node = fs == null ? null : (fs.Node(mnt.RootIno) ?? fs.Root);
            if (gpath == "/" || node == null) return;
            var l = Walk(null, gpath, true, 0, false);
            fs = l.Fs; node = l.Node; mnt = l.Mnt;
        }

        public Loc G(string gpath) { return Walk(null, gpath, true, 0, false); }
        public Loc GNoFollow(string gpath) { return Walk(null, gpath, false, 0, false); }

        // ------------------------------------------------------------ operations
        long Now { get { return M.Now; } }

        void CheckWritable(Loc l)
        {
            if (l.Mnt != null && l.Mnt.Ro) throw new VfsEx("EROFS");
        }

        public string NewContext(Loc parentLoc, Inode parent, PCtx c, bool dir)
        {
            string ptype = Selinux.TypeOf(parent.Ctx);
            string user = (c == null || c.Domain != "unconfined_t") ? "system_u" : "unconfined_u";
            // files made in the initramfs / chroot without a loaded policy have no label
            if (c != null && c.Domain == "kernel_t") return "system_u:object_r:unlabeled_t:s0";
            // the policy's type transitions for files people make in their home folders and in /tmp
            // (parentLoc is the new entry's own location: its Name is the new file's name)
            string newName = parentLoc == null ? "" : parentLoc.Name;
            if (ptype == "user_home_dir_t" || ptype == "admin_home_t")
            {
                if (newName == ".ssh") ptype = "ssh_home_t";
                else if (ptype == "user_home_dir_t") ptype = "user_home_t";
            }
            else if (ptype == "tmp_t" && c != null && c.Domain == "unconfined_t" && c.Cr.Uid != 0) ptype = "user_tmp_t";
            return user + ":object_r:" + ptype + ":s0";
        }

        /// <summary>Creates a node (file, dir, fifo, symlink) where l says the name is missing.</summary>
        public Inode Create(PCtx c, Loc l, char t, int mode)
        {
            if (l.Exists) throw new VfsEx("EEXIST");
            if (l.Parent == null) throw new VfsEx("ENOENT");
            if (l.Parent.T != 'd') throw new VfsEx("ENOTDIR");
            if (c != null && !Can(c.Cr, l.Parent, 3)) throw new VfsEx("EACCES");
            CheckWritable(l);
            if (l.PFs.Type == "proc" || l.PFs.Type == "sysfs") throw new VfsEx("EACCES");
            int uid = c == null ? 0 : c.Cr.Uid, gid = c == null ? 0 : c.Cr.Gid;
            if ((l.Parent.Mode & 0x400) != 0) gid = l.Parent.Gid;   // setgid directory: new files get its group
            if (l.PFs.Type == "nfs4" && uid == 0 && M.Net.NfsRootSquash(l.PFs)) { uid = 65534; gid = 65534; }
            int um = c == null ? 0x12 : c.Umask;
            int m = mode;
            var n = l.PFs.NewNode(t, 0, uid, gid, Now);
            if (l.Parent.DefAcl != null && l.Parent.DefAcl.Count > 0)
            {
                // default ACL: the new node's ACL is the default; its mode is mode AND the default's base entries
                int du = 0, dg = 0, dm = -1, dother = 0;
                n.Acl = new List<AclEntry>();
                foreach (var e in l.Parent.DefAcl)
                {
                    if (e.Tag == "user" && e.Who == "") du = e.Perm;
                    else if (e.Tag == "group" && e.Who == "") { dg = e.Perm; n.Acl.Add(new AclEntry("group", "", e.Perm)); }
                    else if (e.Tag == "mask") dm = e.Perm;
                    else if (e.Tag == "other") dother = e.Perm;
                    else n.Acl.Add(new AclEntry(e.Tag, e.Who, e.Perm));
                }
                int grp = dm >= 0 ? dm : dg;
                m = (mode & ~0x1FF) | (mode & ((du << 6) | (grp << 3) | dother));
                if (n.Acl.Count == 1 && dm < 0) n.Acl = null;   // only group:: -> no extended ACL
                if (t == 'd') n.DefAcl = l.Parent.DefAcl.Select(e => new AclEntry(e.Tag, e.Who, e.Perm)).ToList();
            }
            else m = mode & ~um;
            if (t == 'd' && (l.Parent.Mode & 0x400) != 0) m |= 0x400;   // setgid propagates to new directories
            n.Mode = m;
            n.Ctx = NewContext(l, l.Parent, c, t == 'd');
            l.Parent.Kids[l.Name] = n.Ino;
            l.Parent.Mtime = Now; l.Parent.Ctime = Now;
            if (t == 'd') l.Parent.Nlink++;
            l.Node = n; l.Fs = l.PFs;
            return n;
        }

        public void CheckSpace(FileSys fs, long add)
        {
            if (fs.Type == "proc" || fs.Type == "sysfs" || fs.Type == "devtmpfs") return;
            if (fs.Bytes > 0 && fs.Used + add > fs.Bytes) throw new VfsEx("ENOSPC");
        }

        public string ReadText(PCtx c, string path)
        {
            var l = Resolve(c, path, true);
            if (!l.Exists) throw new VfsEx("ENOENT");
            return ReadNode(c, l);
        }

        public string ReadNode(PCtx c, Loc l)
        {
            var n = l.Node;
            if (n.T == 'd') throw new VfsEx("EISDIR");
            if (c != null && !Can(c.Cr, n, 4)) throw new VfsEx("EACCES");
            if (c != null && !Selinux.AllowRead(M, c, l)) throw new VfsEx("EACCES");
            if (n.T == 'c')
            {
                if (n.Dev == "null") return "";
                if (n.Dev == "zero") return "";
                if (n.Dev == "urandom" || n.Dev == "random") return "";
                return "";
            }
            if (n.T == 'b') return "";
            n.Atime = Now;
            if (n.Text == null) return n.BinSize > 0 ? new string('\u0001', (int)Math.Min(n.BinSize, 64)) : "";
            return n.Text;
        }

        /// <summary>Writes a file (creating it like open(O_CREAT)); returns the inode.</summary>
        public Inode WriteText(PCtx c, string path, string text, bool append)
        {
            var l = Resolve(c, path, true);
            return WriteLoc(c, l, text, append, 0x1B6);
        }

        public Inode WriteLoc(PCtx c, Loc l, string text, bool append, int createMode)
        {
            if (!l.Exists)
            {
                if (l.Parent == null) throw new VfsEx("ENOENT");
                CheckSpace(l.PFs, Encoding.UTF8.GetByteCount(text));
                var n = Create(c, l, 'f', createMode);
                n.Text = text;
                return n;
            }
            var node = l.Node;
            if (node.T == 'd') throw new VfsEx("EISDIR");
            if (node.T == 'c') { if (node.Dev == "null" || node.Dev == "zero") return node; if (node.Dev == "tty" || node.Dev == "console") return node; }
            if (c != null && !Can(c.Cr, node, 2)) throw new VfsEx("EACCES");
            if (c != null && !Selinux.AllowWrite(M, c, l)) throw new VfsEx("EACCES");
            CheckWritable(l);
            if (l.Fs.Type == "proc" || l.Fs.Type == "sysfs") { M.WriteProc(l, text); return node; }
            string old = node.Text ?? "";
            long add = Encoding.UTF8.GetByteCount(text) - (append ? 0 : node.Size);
            if (add > 0) CheckSpace(l.Fs, add);
            node.Text = append ? old + text : text;
            node.BinSize = -1;
            node.Mtime = Now; node.Ctime = Now;
            return node;
        }

        public Inode Mkdir(PCtx c, string path, int mode, bool parents)
        {
            if (parents)
            {
                string full = Norm(Join(c == null ? "/" : c.Cwd, path));
                var parts = full.Split('/').Where(s => s.Length > 0).ToList();
                string cur = "";
                Inode last = null;
                foreach (var p in parts)
                {
                    cur += "/" + p;
                    var l = Resolve(c, cur, true);
                    if (l.Exists) { if (l.Node.T != 'd') throw new VfsEx("ENOTDIR"); last = l.Node; continue; }
                    last = Create(c, l, 'd', mode);
                }
                return last;
            }
            var lc = Resolve(c, path, false);
            if (lc.Exists) throw new VfsEx("EEXIST");
            return Create(c, lc, 'd', mode);
        }

        bool StickyBlocks(Cred cr, Inode dir, Inode n)
        {
            if (cr == null || cr.Uid == 0) return false;
            if ((dir.Mode & 0x200) == 0) return false;
            return n.Uid != cr.Uid && dir.Uid != cr.Uid;
        }

        public bool IsMountPoint(Loc l)
        {
            foreach (var m in Mounts) if (m.Path == l.GPath && m.Path != "/") return true;
            return false;
        }

        public void Unlink(PCtx c, Loc l)
        {
            if (!l.Exists) throw new VfsEx("ENOENT");
            if (l.Node.T == 'd') throw new VfsEx("EISDIR");
            RemoveEntry(c, l);
        }

        public void Rmdir(PCtx c, Loc l)
        {
            if (!l.Exists) throw new VfsEx("ENOENT");
            if (l.Node.T != 'd') throw new VfsEx("ENOTDIR");
            if (IsMountPoint(l)) throw new VfsEx("EBUSY");
            if (l.Node.Kids.Count > 0) throw new VfsEx("ENOTEMPTY");
            RemoveEntry(c, l);
        }

        void RemoveEntry(PCtx c, Loc l)
        {
            if (l.Parent == null) throw new VfsEx("EBUSY");
            if (c != null && !Can(c.Cr, l.Parent, 3)) throw new VfsEx("EACCES");
            if (c != null && StickyBlocks(c.Cr, l.Parent, l.Node)) throw new VfsEx("EPERM");
            CheckWritable(l);
            l.Parent.Kids.Remove(l.Name);
            l.Parent.Mtime = Now;
            if (l.Node.T == 'd') l.Parent.Nlink--;
            l.Node.Nlink--;
            if (l.Node.Nlink <= 0 || l.Node.T == 'd') l.PFs.Nodes.Remove(l.Node.Ino);
        }

        /// <summary>Removes a whole tree (rm -r); returns the paths it could not remove with why.</summary>
        public void RemoveTree(PCtx c, Loc l, List<string> errors, string shown)
        {
            if (l.Node.T == 'd' && !IsMountPoint(l))
            {
                if (c != null && !Can(c.Cr, l.Node, 7) && l.Node.Kids.Count > 0) { errors.Add("cannot remove '" + shown + "': Permission denied"); return; }
                foreach (var k in l.Node.Kids.Keys.ToList())
                {
                    var cl = Resolve(c, Join(l.Path, k), false);
                    if (cl.Exists) RemoveTree(c, cl, errors, shown + "/" + k);
                }
                if (l.Node.Kids.Count > 0) return;
                try { Rmdir(c, l); } catch (VfsEx e) { errors.Add("cannot remove '" + shown + "': " + e.Message); }
                return;
            }
            try { if (l.Node.T == 'd') Rmdir(c, l); else Unlink(c, l); } catch (VfsEx e) { errors.Add("cannot remove '" + shown + "': " + e.Message); }
        }

        public void Rename(PCtx c, Loc from, Loc to)
        {
            if (!from.Exists) throw new VfsEx("ENOENT");
            if (from.Parent == null) throw new VfsEx("EBUSY");
            if (to.Parent == null) throw new VfsEx("ENOENT");
            if (c != null && (!Can(c.Cr, from.Parent, 3) || !Can(c.Cr, to.Parent, 3))) throw new VfsEx("EACCES");
            if (c != null && StickyBlocks(c.Cr, from.Parent, from.Node)) throw new VfsEx("EPERM");
            CheckWritable(from); CheckWritable(to);
            if (from.PFs != to.PFs) throw new VfsEx("EXDEV");
            if (to.Exists)
            {
                if (to.Node == from.Node) return;
                if (to.Node.T == 'd') { if (from.Node.T != 'd') throw new VfsEx("EISDIR"); if (to.Node.Kids.Count > 0) throw new VfsEx("ENOTEMPTY"); to.PFs.Nodes.Remove(to.Node.Ino); }
                else { to.Node.Nlink--; if (to.Node.Nlink <= 0) to.PFs.Nodes.Remove(to.Node.Ino); }
            }
            from.Parent.Kids.Remove(from.Name);
            to.Parent.Kids[to.Name] = from.Node.Ino;
            if (from.Node.T == 'd' && from.Parent != to.Parent) { from.Parent.Nlink--; to.Parent.Nlink++; }
            from.Node.Ctime = Now; from.Parent.Mtime = Now; to.Parent.Mtime = Now;
        }

        public void HardLink(PCtx c, Loc target, Loc link)
        {
            if (!target.Exists) throw new VfsEx("ENOENT");
            if (target.Node.T == 'd') throw new VfsEx("EPERM");
            if (link.Exists) throw new VfsEx("EEXIST");
            if (link.Parent == null) throw new VfsEx("ENOENT");
            if (target.Fs != link.PFs) throw new VfsEx("EXDEV");
            if (c != null && !Can(c.Cr, link.Parent, 3)) throw new VfsEx("EACCES");
            CheckWritable(link);
            link.Parent.Kids[link.Name] = target.Node.Ino;
            target.Node.Nlink++;
            target.Node.Ctime = Now;
        }

        public Inode Symlink(PCtx c, string target, Loc link)
        {
            if (link.Exists) throw new VfsEx("EEXIST");
            var n = Create(c, link, 'l', 0x1FF);
            n.Mode = 0x1FF;
            n.Target = target;
            return n;
        }

        public void Chmod(PCtx c, Loc l, int mode)
        {
            if (c != null && c.Cr.Uid != 0 && c.Cr.Uid != l.Node.Uid) throw new VfsEx("EPERM");
            CheckWritable(l);
            var n = l.Node;
            // a non-member may not keep setgid on a file of another group (the kernel clears it)
            if (c != null && c.Cr.Uid != 0 && (mode & 0x400) != 0 && n.T != 'd' && !c.Cr.InGroup(n.Gid)) mode &= ~0x400;
            n.Mode = mode & 0xFFF;
            n.Ctime = Now;
            if (n.HasExtAcl)
            {
                // the group bits are the ACL mask now
            }
        }

        public void Chown(PCtx c, Loc l, int uid, int gid)
        {
            var n = l.Node;
            if (c != null && c.Cr.Uid != 0)
            {
                if (uid >= 0 && uid != n.Uid) throw new VfsEx("EPERM");
                if (gid >= 0 && (n.Uid != c.Cr.Uid || !c.Cr.InGroup(gid))) throw new VfsEx("EPERM");
            }
            CheckWritable(l);
            if (uid >= 0) n.Uid = uid;
            if (gid >= 0) n.Gid = gid;
            if (n.T == 'f' && (uid >= 0 || gid >= 0)) n.Mode &= ~0x800;   // setuid cleared on chown
            n.Ctime = Now;
        }

        // ------------------------------------------------------------ helpers used everywhere
        public bool Exists(string gpath) { try { return G(gpath).Exists; } catch (VfsEx) { return false; } }
        public bool IsDirG(string gpath) { try { var l = G(gpath); return l.Exists && l.Node.T == 'd'; } catch (VfsEx) { return false; } }
        public string Cat(string gpath) { try { var l = G(gpath); if (!l.Exists || l.Node.T != 'f') return null; return l.Node.Text ?? ""; } catch (VfsEx) { return null; } }

        /// <summary>System writes (packages, the simulator itself): no permission checks, parents made as root 0755.</summary>
        public Inode Put(string gpath, string text, int mode, int uid, int gid, string ctx)
        {
            EnsureDir(Parent(gpath), 0x1ED, null);
            var l = GNoFollow(gpath);
            Inode n;
            if (l.Exists && l.Node.T == 'f') { n = l.Node; n.Text = text; n.BinSize = -1; n.Mtime = Now; }
            else
            {
                if (l.Exists) RemoveEntryForce(l);
                l = GNoFollow(gpath);
                n = l.PFs.NewNode('f', mode, uid, gid, Now);
                l.Parent.Kids[l.Name] = n.Ino;
                n.Text = text;
            }
            n.Mode = mode; n.Uid = uid; n.Gid = gid;
            n.Ctx = ctx ?? Selinux.DefaultContext(M, gpath, 'f');
            return n;
        }
        public Inode Put(string gpath, string text) { return Put(gpath, text, 0x1A4, 0, 0, null); }

        public void PutLink(string gpath, string target)
        {
            EnsureDir(Parent(gpath), 0x1ED, null);
            var l = GNoFollow(gpath);
            if (l.Exists) RemoveEntryForce(l);
            l = GNoFollow(gpath);
            var n = l.PFs.NewNode('l', 0x1FF, 0, 0, Now);
            n.Target = target;
            n.Ctx = Selinux.DefaultContext(M, gpath, 'l');
            l.Parent.Kids[l.Name] = n.Ino;
        }

        public Inode EnsureDir(string gpath, int mode, string ctx)
        {
            if (gpath == "/") return G("/").Node;
            Loc l = null;
            try { l = G(gpath); } catch (VfsEx) { }
            if (l != null && l.Exists) return l.Node;
            EnsureDir(Parent(gpath), 0x1ED, null);
            l = G(gpath);
            if (l.Exists) return l.Node;
            var n = l.PFs.NewNode('d', mode, 0, 0, Now);
            n.Ctx = ctx ?? Selinux.DefaultContext(M, gpath, 'd');
            l.Parent.Kids[l.Name] = n.Ino;
            l.Parent.Nlink++;
            return n;
        }

        public void RemoveEntryForce(Loc l)
        {
            if (l.Parent == null || !l.Exists) return;
            l.Parent.Kids.Remove(l.Name);
            l.Node.Nlink--;
            if (l.Node.T == 'd') { RemoveSubtree(l.PFs, l.Node); l.Parent.Nlink--; }
            if (l.Node.Nlink <= 0 || l.Node.T == 'd') l.PFs.Nodes.Remove(l.Node.Ino);
        }
        void RemoveSubtree(FileSys fs, Inode d)
        {
            foreach (var ino in d.Kids.Values.ToList())
            {
                var n = fs.Node(ino);
                if (n == null) continue;
                if (n.T == 'd') RemoveSubtree(fs, n);
                n.Nlink--;
                if (n.Nlink <= 0 || n.T == 'd') fs.Nodes.Remove(ino);
            }
            d.Kids.Clear();
        }
        public void RemoveG(string gpath) { try { var l = GNoFollow(gpath); if (l.Exists) RemoveEntryForce(l); } catch (VfsEx) { } }

        public static string Parent(string p) { int i = p.LastIndexOf('/'); if (i <= 0) return "/"; return p.Substring(0, i); }
        public static string Base(string p) { p = p.TrimEnd('/'); int i = p.LastIndexOf('/'); return i < 0 ? p : p.Substring(i + 1); }

        /// <summary>Directory entries in ls order (locale-like: case and punctuation ignored first).</summary>
        public static List<string> Sorted(IEnumerable<string> names)
        {
            var l = names.ToList();
            l.Sort(LsCompare);
            return l;
        }
        public static int LsCompare(string a, string b)
        {
            string ka = Key(a), kb = Key(b);
            int r = string.CompareOrdinal(ka, kb);
            if (r != 0) return r;
            return string.CompareOrdinal(b, a) ;
        }
        static string Key(string s)
        {
            var sb = new StringBuilder();
            foreach (char ch in s) if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
            return sb.Length == 0 ? s : sb.ToString();
        }

        public static string ModeString(Inode n)
        {
            var sb = new StringBuilder();
            sb.Append(n.T == 'f' ? '-' : n.T);
            int m = n.Mode;
            sb.Append((m & 0x100) != 0 ? 'r' : '-');
            sb.Append((m & 0x80) != 0 ? 'w' : '-');
            sb.Append((m & 0x800) != 0 ? ((m & 0x40) != 0 ? 's' : 'S') : ((m & 0x40) != 0 ? 'x' : '-'));
            sb.Append((m & 0x20) != 0 ? 'r' : '-');
            sb.Append((m & 0x10) != 0 ? 'w' : '-');
            sb.Append((m & 0x400) != 0 ? ((m & 0x8) != 0 ? 's' : 'S') : ((m & 0x8) != 0 ? 'x' : '-'));
            sb.Append((m & 0x4) != 0 ? 'r' : '-');
            sb.Append((m & 0x2) != 0 ? 'w' : '-');
            sb.Append((m & 0x200) != 0 ? ((m & 0x1) != 0 ? 't' : 'T') : ((m & 0x1) != 0 ? 'x' : '-'));
            return sb.ToString();
        }
        public static string Rwx(int p) { return ((p & 4) != 0 ? "r" : "-") + ((p & 2) != 0 ? "w" : "-") + ((p & 1) != 0 ? "x" : "-"); }
    }
}
