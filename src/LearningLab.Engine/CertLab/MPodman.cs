// Cert Lab - containers: images (in the utility server's registry and in each user's local storage, root and
// rootless kept apart like podman does), containers with published ports and volumes, and the small file system
// each container gets (mounted under the storage folder so commands can run "inside" it).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public class CImage
    {
        public string Repo = "";        // registry.lab.example.com/ubi9/httpd-24
        public string Tag = "latest";
        public string Id = "";          // 64 hex
        public string Kind = "ubi";     // ubi, httpd, mariadb, nginx, custom
        public long Size = 216000000;
        public long Created = 1767225600;
        public string Owner = "";       // local storage: "" root, else the user
        public string Cmd = "/bin/bash";
        public string User = "0";
        public string WorkDir = "/";
        public List<string> Env = new List<string>();
        public List<string> Exposed = new List<string>();
        public List<string> Labels = new List<string>();
        public string Description = "";
        public List<string> ExtraFiles = new List<string>();   // custom images: "path\ttext"
        public string Full { get { return Repo + ":" + Tag; } }
        public string Short { get { return Id.Substring(0, 12); } }
        public CImage Copy() { return new CImage { Repo = Repo, Tag = Tag, Id = Id, Kind = Kind, Size = Size, Created = Created, Owner = Owner, Cmd = Cmd, User = User, WorkDir = WorkDir, Env = new List<string>(Env), Exposed = new List<string>(Exposed), Labels = new List<string>(Labels), Description = Description, ExtraFiles = new List<string>(ExtraFiles) }; }

        public static List<CImage> Catalog()
        {
            var l = new List<CImage>();
            Func<string, string, string, string, long, string, string, string, string[], CImage> mk = (repo, tag, id, kind, size, cmd, user, desc, exposed) =>
                new CImage { Repo = "registry.lab.example.com/" + repo, Tag = tag, Id = id, Kind = kind, Size = size, Cmd = cmd, User = user, Description = desc, Exposed = exposed.ToList(), Env = new List<string> { "PATH=/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin", "container=oci" } };
            l.Add(mk("ubi9/ubi", "latest", "8a4b8c2f1e4d7f0a9b3c6d2e5f8a1b4c7d0e3f6a9b2c5d8e1f4a7b0c3d6e9f2a", "ubi", 216401234, "/bin/bash", "0", "The Universal Base Image is designed and engineered to be the base layer for all of your containerized applications.", new string[0]));
            l.Add(mk("ubi9/ubi", "9.5", "8a4b8c2f1e4d7f0a9b3c6d2e5f8a1b4c7d0e3f6a9b2c5d8e1f4a7b0c3d6e9f2a", "ubi", 216401234, "/bin/bash", "0", "The Universal Base Image is designed and engineered to be the base layer for all of your containerized applications.", new string[0]));
            l.Add(mk("ubi9/ubi-minimal", "latest", "c1d2e3f4a5b6c7d8e9f0a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6e7f8a9b0c1d2", "ubi", 101203456, "/bin/bash", "0", "The Universal Base Image Minimal is a stripped down image that uses microdnf as a package manager.", new string[0]));
            l.Add(mk("ubi9/httpd-24", "latest", "4f2a9e7c1b3d5f7a9c1e3b5d7f9a1c3e5b7d9f1a3c5e7b9d1f3a5c7e9b1d3f5a", "httpd", 381245678, "/usr/bin/run-httpd", "1001", "Apache HTTP Server 2.4 available as container, is a powerful, efficient, and extensible web server.", new[] { "8080/tcp", "8443/tcp" }));
            l.Add(mk("rhel9/mariadb-105", "latest", "9e8d7c6b5a4f3e2d1c0b9a8f7e6d5c4b3a2f1e0d9c8b7a6f5e4d3c2b1a0f9e8d", "mariadb", 563789012, "run-mysqld", "27", "MariaDB is a multi-user, multi-threaded SQL database server.", new[] { "3306/tcp" }));
            l.Add(mk("library/nginx", "latest", "2b4d6f8a0c2e4b6d8f0a2c4e6b8d0f2a4c6e8b0d2f4a6c8e0b2d4f6a8c0e2b4d", "nginx", 192345678, "nginx -g 'daemon off;'", "0", "Official build of Nginx.", new[] { "80/tcp" }));
            l.Add(mk("library/busybox", "latest", "6c8e0a2b4d6f8c0e2a4b6d8f0c2e4a6b8d0f2c4e6a8b0d2f4c6e8a0b2d4f6c8e", "ubi", 4261234, "sh", "0", "Busybox base image.", new string[0]));
            foreach (var i in l) { i.Labels.Add("vendor=Cert Lab practice registry"); i.Labels.Add("summary=" + i.Description); }
            return l;
        }
    }

    public class CPort { public int Host; public int Cont; public string Proto = "tcp"; public string Ip = ""; }
    public class CVol { public string Host = ""; public string Cont = ""; public string Opts = ""; public bool Named; }

    public class Container
    {
        public string Id = "", Name = "", Image = "", ImageId = "", Cmd = "", Owner = "", Unit = "";
        public bool Running; public string Status = "Created"; public long Created; public long StartedAt; public long StoppedAt;
        public List<CPort> Ports = new List<CPort>();
        public List<CVol> Volumes = new List<CVol>();
        public List<string> Env = new List<string>();
        public bool AutoRemove; public string Restart = "no"; public int ExitCode;
        public string Logs = "";
        public string FsId = "";
        public string Kind = "ubi";
        public string User = "0";
        public string Short { get { return Id.Substring(0, 12); } }
        public string Mount { get { return (Owner.Length == 0 ? "/var/lib/containers/storage" : "/home/" + Owner + "/.local/share/containers/storage") + "/overlay/" + Id + "/merged"; } }
    }

    public partial class Machine
    {
        public string StorageRoot(string owner) { return owner.Length == 0 ? "/var/lib/containers/storage" : User(owner).Home + "/.local/share/containers/storage"; }

        /// <summary>A new container file system from the image (a few folders, the programs a base image has).</summary>
        public FileSys ContainerFs(CImage img, string id)
        {
            var fs = new FileSys { Id = "ctr" + id.Substring(0, 12), Type = "overlay", Bytes = 10L * 1024 * 1024 * 1024, BaseUsed = img.Size };
            var root = new Inode { Ino = 1, T = 'd', Mode = 0x1ED, Kids = new Dictionary<string, int>(StringComparer.Ordinal), Nlink = 2, Ctx = "system_u:object_r:container_file_t:s0:c123,c456", Mtime = Now, Ctime = Now, Atime = Now };
            fs.Nodes[1] = root;
            Func<Inode, string, int, Inode> dir = (p, n, mode) => { var d = fs.NewNode('d', mode, 0, 0, Now); d.Ctx = root.Ctx; p.Kids[n] = d.Ino; p.Nlink++; return d; };
            Action<Inode, string, string, int> file = (p, n, text, mode) => { var f = fs.NewNode('f', mode, 0, 0, Now); f.Text = text; f.Ctx = root.Ctx; p.Kids[n] = f.Ino; };
            var usr = dir(root, "usr", 0x1ED);
            var bin = dir(usr, "bin", 0x1ED);
            dir(usr, "sbin", 0x1ED); dir(usr, "lib", 0x1ED); dir(usr, "share", 0x1ED);
            foreach (var p in new[] { "bash", "sh", "ls", "cat", "echo", "env", "id", "hostname", "mkdir", "touch", "rm", "cp", "mv", "date", "grep", "sed", "sleep", "ps", "pwd", "head", "tail", "wc", "uname", "whoami", "chmod", "chown", "ln", "find", "df", "du", "stat", "test", "true", "false", "printf", "sort", "tr", "cut" })
            {
                var f = fs.NewNode('f', 0x1ED, 0, 0, Now); f.Prog = p; f.BinSize = 30000; f.Ctx = root.Ctx; bin.Kids[p] = f.Ino;
            }
            var l = fs.NewNode('l', 0x1FF, 0, 0, Now); l.Target = "usr/bin"; root.Kids["bin"] = l.Ino;
            var l2 = fs.NewNode('l', 0x1FF, 0, 0, Now); l2.Target = "usr/sbin"; root.Kids["sbin"] = l2.Ino;
            var etc = dir(root, "etc", 0x1ED);
            file(etc, "os-release", "NAME=\"Red Hat Enterprise Linux\"\nVERSION=\"9.5 (Plow)\"\nID=\"rhel\"\nPRETTY_NAME=\"Red Hat Enterprise Linux 9.5 (Plow)\"\n", 0x1A4);
            file(etc, "passwd", "root:x:0:0:root:/root:/bin/bash\ndefault:x:1001:0:Default Application User:/opt/app-root/src:/sbin/nologin\nmysql:x:27:27:MySQL Server:/var/lib/mysql:/sbin/nologin\n", 0x1A4);
            file(etc, "group", "root:x:0:\nmysql:x:27:\n", 0x1A4);
            file(etc, "hostname", id.Substring(0, 12) + "\n", 0x1A4);
            file(etc, "hosts", "127.0.0.1\tlocalhost\n::1\tlocalhost\n", 0x1A4);
            dir(root, "tmp", 0x3FF); dir(root, "root", 0x168); dir(root, "home", 0x1ED); dir(root, "proc", 0x16D); dir(root, "dev", 0x1ED); dir(root, "run", 0x1ED);
            var var = dir(root, "var", 0x1ED);
            dir(var, "log", 0x1ED); dir(var, "tmp", 0x3FF); var lib = dir(var, "lib", 0x1ED);
            if (img.Kind == "httpd")
            {
                var www = dir(var, "www", 0x1ED);
                var html = dir(www, "html", 0x1ED);
                var opt = dir(root, "opt", 0x1ED); var app = dir(opt, "app-root", 0x1ED); var src = dir(app, "src", 0x1FD);
                src.Uid = 1001;
            }
            if (img.Kind == "mariadb") { var my = dir(lib, "mysql", 0x1F8); my.Uid = 27; my.Gid = 27; }
            if (img.Kind == "nginx") { var share = fs.Node(usr.Kids["share"]); var ng = dir(share, "nginx", 0x1ED); var h = dir(ng, "html", 0x1ED); file(h, "index.html", "<!DOCTYPE html>\n<html><head><title>Welcome to nginx!</title></head><body><h1>Welcome to nginx!</h1></body></html>\n", 0x1A4); }
            foreach (var x in img.ExtraFiles)
            {
                var p = x.Split(new[] { '\t' }, 2);
                if (p.Length < 2) continue;
                var parts = p[0].Trim('/').Split('/');
                Inode cur = root;
                for (int i = 0; i < parts.Length - 1; i++) { int ino; cur = cur.Kids.TryGetValue(parts[i], out ino) ? fs.Node(ino) : dir(cur, parts[i], 0x1ED); }
                file(cur, parts[parts.Length - 1], p[1], 0x1A4);
            }
            Fss[fs.Id] = fs;
            return fs;
        }

        /// <summary>Puts a running container's file system (and its volumes) into the mount table.</summary>
        public void MountContainer(Container c)
        {
            UnmountContainer(c);
            var fs = V.Fs(c.FsId);
            if (fs == null) return;
            string mp = c.Mount;
            try { V.EnsureDir(mp, 0x1ED, "system_u:object_r:container_ro_file_t:s0"); } catch (VfsEx) { return; }
            Mounts.Add(new Mount { Path = mp, FsId = fs.Id, RootIno = 1, Source = "overlay", FsType = "overlay", Opts = "rw,relatime,context=\"system_u:object_r:container_file_t:s0\"" });
            foreach (var v in c.Volumes)
            {
                string target = mp + v.Cont;
                // the folder inside the container
                var parts = v.Cont.Trim('/').Split('/');
                var cur = fs.Root;
                foreach (var p in parts)
                {
                    int ino;
                    if (cur.Kids.TryGetValue(p, out ino)) { cur = fs.Node(ino); continue; }
                    var d = fs.NewNode('d', 0x1ED, 0, 0, Now); d.Ctx = fs.Root.Ctx; cur.Kids[p] = d.Ino; cur = d;
                }
                try
                {
                    var src = V.G(v.Host);
                    if (!src.Exists) continue;
                    Mounts.Add(new Mount { Path = target, FsId = src.Fs.Id, RootIno = src.Node.Ino, Source = v.Host, FsType = src.Fs.Type, Opts = v.Opts.Contains("ro") ? "ro" : "rw", Ro = v.Opts.Split(',').Contains("ro"), Bind = true });
                }
                catch (VfsEx) { }
            }
        }
        public void UnmountContainer(Container c)
        {
            string mp = c.Mount;
            Mounts.RemoveAll(m => m.Path == mp || m.Path.StartsWith(mp + "/", StringComparison.Ordinal));
        }
    }
}
