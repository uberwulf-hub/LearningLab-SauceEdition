// Cert Lab - SMB shares on the AZ-800 servers and the access a person really gets: over the network the share
// permissions and NTFS both apply and the more restrictive wins; at the console only NTFS counts. Shares can hide
// folders a person can't read (access-based enumeration) and require SMB encryption. A UNC path \\server\share\path
// (or a DFS namespace path, WsDfs.cs) is resolved to a folder on a server.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public class WsShareAce { public string Trustee = ""; public string Right = "Read"; public bool Allow = true; }
    public class WsShare
    {
        public string Name = "", Path = "", Description = "", FolderEnumerationMode = "Unrestricted", CachingMode = "Manual";
        public bool EncryptData, Special;
        public uint ConcurrentUserLimit;
        public readonly List<WsShareAce> Access = new List<WsShareAce>();
        public static int Mask(string right) { switch ((right ?? "").ToLowerInvariant()) { case "full": return NtfsRights.Full; case "change": return NtfsRights.Modify; case "read": return NtfsRights.ReadExecute; } return 0; }
    }

    public partial class WsServer
    {
        public readonly List<WsShare> Shares = new List<WsShare>();
        public bool SmbEncrypt, Smb1;
        public WsShare Share(string name) { return Shares.FirstOrDefault(s => WsText.Eq(s.Name, name)); }

        /// <summary>A domain principal's token on this server: its domain identities plus the server's local groups it is in.</summary>
        public HashSet<string> LocalToken(WsAdObj p)
        {
            var t = Lab.TokenNames(Lab.Main, p);
            t.Add("BUILTIN\\Users");
            if (t.Contains("Domain Admins") || WsText.Eq(p.Sam, "Administrator") || t.Contains("Enterprise Admins")) t.Add("BUILTIN\\Administrators");
            if (t.Contains("Backup Operators")) t.Add("BUILTIN\\Backup Operators");
            return t;
        }
        public int ShareAccess(WsShare s, HashSet<string> token)
        {
            int granted = 0, denied = 0;
            foreach (var a in s.Access) if (Matches(a.Trustee, token)) { if (a.Allow) granted |= WsShare.Mask(a.Right); else denied |= WsShare.Mask(a.Right); }
            return granted & ~denied;
        }
    }

    public class AccessResult { public int Ntfs, Share = -1, Effective; public string Server = "", LocalPath = "", ShareName = "", Error; public bool Exists; }

    public partial class WsLab
    {
        /// <summary>Where a path lives: \\FS1\Sales\x -> FS1, D:\Shares\Sales\x; \\corp.example\Files\... through DFS; or SERVER:D:\path.</summary>
        public AccessResult Locate(string path)
        {
            var r = new AccessResult();
            string p = (path ?? "").Trim().Replace('/', '\\');
            if (p.StartsWith("\\\\", StringComparison.Ordinal))
            {
                var parts = p.Substring(2).Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) { r.Error = "A UNC path needs a server and a share: \\\\server\\share."; return r; }
                string target = DfsResolve(p);
                if (target != null && !WsText.Eq(target, p)) return Locate(target);
                var s = Server(parts[0]);
                if (s == null) { r.Error = "The network path " + p + " was not found."; return r; }
                var sh = s.Share(parts[1]);
                if (sh == null) { r.Error = "The network name cannot be found: " + parts[1] + " on " + s.Name + "."; return r; }
                r.Server = s.Name; r.ShareName = sh.Name;
                r.LocalPath = sh.Path.TrimEnd('\\') + (parts.Length > 2 ? "\\" + string.Join("\\", parts.Skip(2).ToArray()) : "");
                return r;
            }
            int colon = p.IndexOf(':');
            if (colon > 1 && p.Length > colon + 3 && p[colon + 2] == ':' && p[colon + 3] == '\\') { r.Server = p.Substring(0, colon); r.LocalPath = p.Substring(colon + 1); return r; }
            r.Error = "Give a UNC path (\\\\FS1\\Sales) or SERVER:D:\\folder.";
            return r;
        }

        /// <summary>The access a user gets to a path (share and NTFS for a UNC path, NTFS alone for SERVER:path).</summary>
        public AccessResult Access(string user, string path)
        {
            var r = Locate(path);
            if (r.Error != null) return r;
            var s = Server(r.Server);
            var u = Main.Find(user, "principal");
            if (u == null) { r.Error = "There is no user or group " + user + "."; return r; }
            var f = s.M.FindFile(r.LocalPath);
            if (f == null) { r.Error = "The path " + r.LocalPath + " doesn't exist on " + s.Name + "."; return r; }
            r.Exists = true;
            var token = s.LocalToken(u);
            r.Ntfs = s.NtfsAccess(f, token);
            r.Effective = r.Ntfs;
            if (r.ShareName.Length > 0) { r.Share = s.ShareAccess(s.Share(r.ShareName), token); r.Effective = r.Ntfs & r.Share; }
            return r;
        }

        partial void SeedFilesMore();
        partial void SeedFiles()
        {
            var fs = Server("FS1"); var m = fs.M; var t = new DateTime(2026, 1, 20, 9, 0, 0);
            foreach (var x in new[] { "D", "E" }) { var root = new WinFile { Name = x + ":", IsDir = true, Attr = 22, Created = t, Modified = t, Accessed = t }; m.ExtraDrives[x] = root; DefaultRootAcl(fs, root); }
            Action<string, bool, string[]> folder = (path, prot, aces) =>
            {
                var f = m.MakeDir(path, t);
                if (aces == null) return;
                var acl = fs.AclOf(f, true); acl.Protected = prot;
                foreach (var a in aces) { var p = a.Split('|'); acl.Explicit.Add(new WsAce { Trustee = p[0], Rights = NtfsRights.Parse(p[1]), OI = true, CI = true }); }
            };
            string[] admins = { "BUILTIN\\Administrators|F", "NT AUTHORITY\\SYSTEM|F" };
            folder("D:\\Shares", false, null);
            folder("D:\\Shares\\Sales", true, admins.Concat(new[] { "CORP\\FS-Sales-Modify|M" }).ToArray());
            folder("D:\\Shares\\Finance", true, admins.Concat(new[] { "CORP\\FS-Finance-Modify|M" }).ToArray());
            folder("D:\\Shares\\HR", true, admins.Concat(new[] { "CORP\\HR|M" }).ToArray());
            folder("D:\\Shares\\Public", false, new[] { "CORP\\Domain Users|M" });
            folder("D:\\Shares\\IT", true, admins.Concat(new[] { "CORP\\IT Admins|F" }).ToArray());
            foreach (var f in new[] { "Sales\\Price list 2026.xlsx", "Sales\\Customers.csv", "Finance\\Budget 2026.xlsx", "Finance\\Invoices Q1.xlsx", "HR\\Handbook.docx", "Public\\harborline.jpg", "Public\\Phone list.xlsx", "IT\\Runbook.docx" })
                m.File("D:\\Shares\\" + f, "", t).FakeSize = 40000 + f.Length * 977;
            Action<string, string, string[]> share = (n, path, acc) =>
            {
                var sh = new WsShare { Name = n, Path = path };
                foreach (var a in acc) { var p = a.Split('|'); sh.Access.Add(new WsShareAce { Trustee = p[0], Right = p[1] }); }
                fs.Shares.Add(sh);
            };
            share("Sales", "D:\\Shares\\Sales", new[] { "Everyone|Full" });
            share("Finance", "D:\\Shares\\Finance", new[] { "Everyone|Read" });
            share("HR", "D:\\Shares\\HR", new[] { "CORP\\HR|Change", "BUILTIN\\Administrators|Full" });
            share("Public", "D:\\Shares\\Public", new[] { "Everyone|Change" });
            share("IT", "D:\\Shares\\IT", new[] { "CORP\\IT Admins|Full" });
            foreach (var s in Servers.Values.Where(x => !x.IsClient))
            {
                foreach (var a in new[] { "ADMIN$|C:\\Windows|Remote Admin", "C$|C:\\|Default share", "IPC$||Remote IPC" }.Concat(s.M.ExtraDrives.Keys.Select(k => k + "$|" + k + ":\\|Default share")))
                { var p = a.Split('|'); s.Shares.Add(new WsShare { Name = p[0], Path = p[1], Description = p[2], Special = true }); }
                if (s.Kind == "dc") { s.M.MakeDir("C:\\Windows\\SYSVOL\\sysvol\\" + DomainDns + "\\Policies", t); s.M.MakeDir("C:\\Windows\\SYSVOL\\sysvol\\" + DomainDns + "\\scripts", t); s.Shares.Add(new WsShare { Name = "SYSVOL", Path = "C:\\Windows\\SYSVOL\\sysvol", Description = "Logon server share" }); s.Shares.Add(new WsShare { Name = "NETLOGON", Path = "C:\\Windows\\SYSVOL\\sysvol\\" + DomainDns + "\\scripts", Description = "Logon server share" }); }
                if (s.Kind == "dc") foreach (var adm in new[] { "windows.admx", "WindowsUpdate.admx", "GroupPolicy.admx", "ControlPanelDisplay.admx" }) s.M.File("C:\\Windows\\PolicyDefinitions\\" + adm, "<policyDefinitions/>", t);
            }
            SeedFilesMore();
        }
        public void DefaultRootAcl(WsServer s, WinFile root)
        {
            var acl = s.AclOf(root, true);
            acl.Explicit.Add(new WsAce { Trustee = "BUILTIN\\Administrators", Rights = NtfsRights.Full, OI = true, CI = true });
            acl.Explicit.Add(new WsAce { Trustee = "NT AUTHORITY\\SYSTEM", Rights = NtfsRights.Full, OI = true, CI = true });
            acl.Explicit.Add(new WsAce { Trustee = "CREATOR OWNER", Rights = NtfsRights.Full, OI = true, CI = true, IO = true });
            acl.Explicit.Add(new WsAce { Trustee = "BUILTIN\\Users", Rights = NtfsRights.ReadExecute, OI = true, CI = true });
        }
        /// <summary>Is the Group Policy central store there (PolicyDefinitions in the domain's SYSVOL on this DC)?</summary>
        public bool CentralStore(string dc) { var m = M(dc); return m != null && m.FindFile("C:\\Windows\\SYSVOL\\sysvol\\" + DomainDns + "\\Policies\\PolicyDefinitions") != null; }
    }
}
