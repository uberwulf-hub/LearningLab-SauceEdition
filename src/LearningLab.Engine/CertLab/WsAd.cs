// Cert Lab - changes to the pretend Active Directory: every change is made on one domain controller's copy (the one a
// command talks to) and then replicates. Group membership follows the scope rules (global, domain local, universal),
// OUs are protected from accidental deletion by default, and deleted objects stay as tombstones that the AD Recycle
// Bin can restore. Messages are the plain-words versions of what Windows says.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public partial class WsLab
    {
        public WsDir Dir(string dc) { WsDir d; return dc != null && Dirs.TryGetValue(dc, out d) ? d : null; }
        public IEnumerable<string> DcNames { get { return Dirs.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase); } }
        public bool DcUp(string dc) { var s = Server(dc); return s != null && s.Running && Dirs.ContainsKey(dc); }
        public WsDir Main { get { return Dir(Fsmo["PDCEmulator"]) ?? Dirs.Values.First(); } }

        /// <summary>The DC a machine's commands talk to: -Server if given, the machine itself if it is a DC, else the first
        /// running DC in the machine's site, else any running DC (the DC locator, simplified).</summary>
        public string LocateDc(PsMachine from, string server, out string error)
        {
            error = null;
            if (!string.IsNullOrEmpty(server))
            {
                var s = Server(server);
                if (s == null || !Dirs.ContainsKey(s.Name)) { error = "Unable to contact the server. This may be because this server does not exist, it is currently down, or it does not have the Active Directory Web Services running."; return null; }
                if (!s.Running) { error = "Unable to contact the server " + s.Name + ". It is not running."; return null; }
                return s.Name;
            }
            if (from != null && Dirs.ContainsKey(from.Name) && from.State == "running") return from.Name;
            string site = from == null ? null : SiteOfIp(from.Ip);
            var up = DcNames.Where(DcUp).ToList();
            var inSite = up.FirstOrDefault(d => site != null && WsText.Eq(SiteOfDc(d), site));
            if (inSite != null) return inSite;
            if (up.Count == 0) { error = "Unable to find a default server with Active Directory Web Services running."; return null; }
            return up[0];
        }

        /// <summary>Stamps a change on a DC's copy and schedules its replication to the other DCs.</summary>
        public void Touch(string dc, WsAdObj o)
        {
            o.Version++; o.Stamp = Now; o.Origin = dc;
            var d = Dir(dc);
            if (d != null && !d.Objs.ContainsKey(o.Guid)) d.Objs[o.Guid] = o;
            Changed(dc);
        }

        public WsAdObj Create(string dc, string cls, string dn, params string[] kv)
        {
            var o = new WsAdObj { Guid = NewGuid(), Dn = Dn.Norm(dn), Class = cls };
            o.Set("name", Dn.RdnValue(dn)).Set("objectClass", cls);
            for (int i = 0; i + 1 < kv.Length; i += 2) o.Set(kv[i], kv[i + 1]);
            o.Set("whenCreated", WsText.Date(Now));
            if (o.IsPrincipal) o.Set("objectSid", "S-1-5-21-3623811015-3361044348-30300820-" + (1100 + guidCount));
            Touch(dc, o);
            return o;
        }

        /// <summary>Checks before creating an object under a parent DN (the parent exists and is a container; no clash).</summary>
        public string CanCreate(WsDir d, string parentDn, string rdn, string sam)
        {
            var p = d.ByDn(parentDn);
            if (p == null || !p.IsContainerLike) return "Directory object not found: the path " + parentDn + " doesn't exist.";
            if (d.ByDn(rdn + "," + parentDn) != null) return "An attempt was made to add an object to the directory with a name that is already in use.";
            if (!string.IsNullOrEmpty(sam) && d.Live.Any(o => o.Sam.Equals(sam, StringComparison.OrdinalIgnoreCase))) return "The specified account already exists: " + sam + ".";
            return null;
        }

        public string Move(string dc, WsAdObj o, string targetDn)
        {
            var d = Dir(dc);
            var t = d.ByDn(targetDn);
            if (t == null || !t.IsContainerLike) return "Directory object not found: " + targetDn + ".";
            if (o.Yes("protectedFromAccidentalDeletion")) return "Access is denied: the object is protected from accidental deletion. Clear the protection first.";
            if (Dn.Under(targetDn, o.Dn)) return "An object can't be moved below itself.";
            string newDn = Dn.Rdn(o.Dn) + "," + Dn.Norm(t.Dn);
            if (d.ByDn(newDn) != null) return "An object with that name already exists in " + targetDn + ".";
            RebaseTree(dc, d, o, newDn);
            return null;
        }
        public string Rename(string dc, WsAdObj o, string newName)
        {
            var d = Dir(dc);
            string prefix = Dn.Rdn(o.Dn).Substring(0, Dn.Rdn(o.Dn).IndexOf('=') + 1);
            string newDn = prefix + newName + "," + Dn.Parent(o.Dn);
            if (d.ByDn(newDn) != null) return "An object with that name already exists.";
            RebaseTree(dc, d, o, newDn);
            o.Set("name", newName); Touch(dc, o);
            return null;
        }
        void RebaseTree(string dc, WsDir d, WsAdObj o, string newDn)
        {
            string old = o.Dn;
            foreach (var k in d.Below(old).ToList())
            {
                if (k == o) continue;
                k.Dn = k.Dn.Substring(0, k.Dn.Length - old.Length) + newDn;
                Touch(dc, k);
            }
            o.Dn = newDn; Touch(dc, o);
        }

        public string Delete(string dc, WsAdObj o, bool recursive)
        {
            var d = Dir(dc);
            if (o.Yes("protectedFromAccidentalDeletion")) return "Access is denied: the object is protected from accidental deletion. Clear \"Protect object from accidental deletion\" (Set-ADObject -ProtectedFromAccidentalDeletion $false) first.";
            var kids = d.Below(o.Dn).Where(k => k != o).ToList();
            if (kids.Count > 0 && !recursive) return "The operation cannot be performed because the object has child objects. Use -Recursive to delete them too.";
            if (kids.Any(k => k.Yes("protectedFromAccidentalDeletion"))) return "Access is denied: an object inside is protected from accidental deletion.";
            foreach (var k in kids.Concat(new[] { o })) { k.Deleted = true; k.Set("lastKnownParent", Dn.Parent(k.Dn)); k.Set("whenDeleted", WsText.Date(Now)); Touch(dc, k); }
            return null;
        }
        public bool RecycleBin { get { var x = Main.ByDn("CN=Recycle Bin Feature,CN=Optional Features,CN=Directory Service,CN=Windows NT,CN=Services," + ConfigDn); return x != null && x.Yes("enabled"); } }

        public string Restore(string dc, WsAdObj o)
        {
            if (!o.Deleted) return "The object isn't deleted.";
            if (!RecycleBin) return "Cert Lab: without the AD Recycle Bin a deleted object can only come back from a backup (an authoritative restore). Enable the Recycle Bin before you need it.";
            var d = Dir(dc);
            if (d.ByDn(o.Get("lastKnownParent")) == null) return "The object's last known parent doesn't exist any more: restore the parent first.";
            o.Deleted = false; o.Set("whenDeleted", null); Touch(dc, o);
            return null;
        }

        // ------------------------------------------------------------ groups
        public IEnumerable<WsAdObj> Members(WsDir d, WsAdObj g, bool recursive)
        {
            var seen = new HashSet<string>();
            var res = new List<WsAdObj>();
            Action<WsAdObj> walk = null;
            walk = x =>
            {
                foreach (var id in x.List("member"))
                {
                    var m = d.ById(id);
                    if (m == null || !seen.Add(m.Guid)) continue;
                    if (recursive && m.Is("group")) walk(m); else res.Add(m);
                }
            };
            walk(g);
            return res;
        }
        /// <summary>The groups a principal is in (directly, or through nested groups), plus the built-in identities.</summary>
        public List<WsAdObj> GroupsOf(WsDir d, WsAdObj p, bool recursive)
        {
            var res = new List<WsAdObj>();
            var todo = new Queue<WsAdObj>(); todo.Enqueue(p);
            while (todo.Count > 0)
            {
                var x = todo.Dequeue();
                foreach (var g in d.OfClass("group").Where(g => g.List("member").Contains(x.Guid, StringComparer.OrdinalIgnoreCase)))
                    if (!res.Contains(g)) { res.Add(g); if (recursive) todo.Enqueue(g); }
                var prim = x.Get("primaryGroup");
                if (prim.Length > 0) { var pg = d.Find(prim, "group"); if (pg != null && !res.Contains(pg)) { res.Add(pg); if (recursive) todo.Enqueue(pg); } }
            }
            return res;
        }
        /// <summary>Every name a principal's access token holds: itself, its groups, Everyone, Authenticated Users...</summary>
        public HashSet<string> TokenNames(WsDir d, WsAdObj p)
        {
            var s = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { p.Sam, p.Name, "Everyone", "Authenticated Users", "NT AUTHORITY\\Authenticated Users" };
            if (p.Is("computer")) s.Add("Domain Computers");
            foreach (var g in GroupsOf(d, p, true)) { s.Add(g.Sam); s.Add(g.Name); s.Add(NetBios + "\\" + g.Sam); }
            s.Add(NetBios + "\\" + p.Sam);
            return s;
        }
    }
}
