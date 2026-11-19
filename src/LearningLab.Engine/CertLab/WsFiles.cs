// Cert Lab - NTFS permissions on the AZ-800 servers' folders: access control entries (allow / deny, rights, the
// inheritance flags OI CI IO NP), inheritance from the parent folder unless it is disabled ("protected"), and how
// Windows decides access: explicit deny, explicit allow, then inherited entries from the nearest parent outwards; the
// first entry that mentions a right decides it. Rights are simplified to eight bits (read, write, execute, delete,
// delete children, read / change permissions, take ownership); FullControl, Modify, ReadAndExecute, Read and Write are
// the usual combinations of them.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public class WsAce
    {
        public string Trustee = ""; public bool Allow = true; public int Rights; public bool OI, CI, IO, NP;
        public bool Inherited; public int Depth;            // set on the copies an effective ACL holds
        public WsAce Copy() { return new WsAce { Trustee = Trustee, Allow = Allow, Rights = Rights, OI = OI, CI = CI, IO = IO, NP = NP, Inherited = Inherited, Depth = Depth }; }
        public string Flags { get { var s = (OI ? "(OI)" : "") + (CI ? "(CI)" : "") + (IO ? "(IO)" : "") + (NP ? "(NP)" : ""); return s; } }
    }
    public class WsAcl { public readonly List<WsAce> Explicit = new List<WsAce>(); public bool Protected; public string Owner = "BUILTIN\\Administrators"; }

    public static class NtfsRights
    {
        public const int R = 1, W = 2, X = 4, D = 8, DC = 16, RP = 32, P = 64, O = 128;
        public const int Full = 255, Modify = R | W | X | D | RP, ReadExecute = R | X | RP, Read = R | RP, Write = W;
        public static int Parse(string s)
        {
            int m = 0;
            foreach (var part in (s ?? "").Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                switch (part.Trim().ToLowerInvariant())
                {
                    case "f": case "fullcontrol": case "full": m |= Full; break;
                    case "m": case "modify": case "change": m |= Modify; break;
                    case "rx": case "readandexecute": case "listdirectory": case "listfoldercontents": case "traverse": m |= ReadExecute; break;
                    case "r": case "read": case "readdata": m |= Read; break;
                    case "w": case "write": case "writedata": case "createfiles": m |= Write; break;
                    case "d": case "delete": m |= D; break;
                    case "x": case "executefile": m |= X; break;
                    case "readpermissions": m |= RP; break;
                    case "changepermissions": case "wdac": m |= P; break;
                    case "takeownership": case "wo": m |= O; break;
                    case "synchronize": break;
                    default: return -1;
                }
            return m;
        }
        /// <summary>The usual name of a mask ("FullControl", "Modify, Synchronize"...), as Get-Acl shows it.</summary>
        public static string Name(int m)
        {
            if (m == Full) return "FullControl";
            if (m == Modify) return "Modify, Synchronize";
            if (m == ReadExecute) return "ReadAndExecute, Synchronize";
            if (m == Read) return "Read, Synchronize";
            if (m == Write) return "Write, Synchronize";
            var l = new List<string>();
            if ((m & R) != 0) l.Add("ReadData"); if ((m & W) != 0) l.Add("WriteData"); if ((m & X) != 0) l.Add("ExecuteFile"); if ((m & D) != 0) l.Add("Delete");
            if ((m & DC) != 0) l.Add("DeleteSubdirectoriesAndFiles"); if ((m & RP) != 0) l.Add("ReadPermissions"); if ((m & P) != 0) l.Add("ChangePermissions"); if ((m & O) != 0) l.Add("TakeOwnership");
            return string.Join(", ", l.ToArray());
        }
        /// <summary>icacls' short form: F, M, RX, R, W or a list such as (R,W,D).</summary>
        public static string Short(int m)
        {
            if (m == Full) return "F"; if (m == Modify) return "M"; if (m == ReadExecute) return "RX"; if (m == Read) return "R"; if (m == Write) return "W";
            var l = new List<string>();
            if ((m & R) != 0) l.Add("RD"); if ((m & W) != 0) l.Add("WD"); if ((m & X) != 0) l.Add("X"); if ((m & D) != 0) l.Add("D"); if ((m & DC) != 0) l.Add("DC"); if ((m & RP) != 0) l.Add("RC"); if ((m & P) != 0) l.Add("WDAC"); if ((m & O) != 0) l.Add("WO");
            return "(" + string.Join(",", l.ToArray()) + ")";
        }
        /// <summary>What a person reads: Full control, Modify, Read and execute, Read, Write, or the parts.</summary>
        public static string Words(int m)
        {
            if (m == 0) return "No access";
            if ((m & Full) == Full) return "Full control"; if ((m & Modify) == Modify) return "Modify"; if ((m & ReadExecute) == ReadExecute) return "Read and execute" + ((m & W) != 0 ? ", write" : "") + ((m & D) != 0 ? ", delete" : ""); if ((m & Read) == Read) return "Read";
            return Name(m);
        }
    }

    public partial class WsServer
    {
        public readonly Dictionary<WinFile, WsAcl> Acls = new Dictionary<WinFile, WsAcl>();
        public WsAcl AclOf(WinFile f, bool create) { WsAcl a; if (!Acls.TryGetValue(f, out a) && create) { a = new WsAcl(); Acls[f] = a; } return a; }

        /// <summary>The effective entries on a file or folder: its own, then those inherited from each parent (nearest first).</summary>
        public List<WsAce> EffectiveAces(WinFile f)
        {
            var res = new List<WsAce>();
            var own = AclOf(f, false);
            if (own != null) foreach (var a in own.Explicit) { if (!a.IO) { var c = a.Copy(); c.Inherited = false; c.Depth = 0; res.Add(c); } }
            if (own != null && own.Protected) return res;
            int depth = 1;
            var child = f;
            bool childIsDir = f.IsDir;
            for (var p = f.Parent; p != null; p = p.Parent, depth++)
            {
                var acl = AclOf(p, false);
                if (acl != null)
                    foreach (var a in acl.Explicit)
                    {
                        // a file gets entries with OI, a folder those with CI; NP stops them after the first level
                        bool reach = (childIsDir ? a.CI : a.OI) && (depth == 1 || !a.NP);
                        if (!reach) continue;
                        var c = a.Copy(); c.Inherited = true; c.Depth = depth; c.IO = false; res.Add(c);
                    }
                if (acl != null && acl.Protected) break;
            }
            return res;
        }

        /// <summary>The rights a token gets on a file or folder through NTFS alone.</summary>
        public int NtfsAccess(WinFile f, HashSet<string> token)
        {
            var aces = EffectiveAces(f);
            var ordered = aces.Where(a => !a.Inherited && !a.Allow).Concat(aces.Where(a => !a.Inherited && a.Allow))
                .Concat(aces.Where(a => a.Inherited).OrderBy(a => a.Depth).ThenBy(a => a.Allow ? 1 : 0));
            int granted = 0, decided = 0;
            foreach (var a in ordered)
            {
                if (!Matches(a.Trustee, token)) continue;
                int bits = a.Rights & ~decided;
                if (a.Allow) granted |= bits;
                decided |= bits;
            }
            return granted;
        }
        public static bool Matches(string trustee, HashSet<string> token)
        {
            string t = trustee ?? "";
            if (token.Contains(t)) return true;
            int i = t.IndexOf('\\');
            return i >= 0 && token.Contains(t.Substring(i + 1)) && !t.StartsWith("BUILTIN\\", StringComparison.OrdinalIgnoreCase) && !t.StartsWith("NT AUTHORITY\\", StringComparison.OrdinalIgnoreCase)
                || t.StartsWith("BUILTIN\\", StringComparison.OrdinalIgnoreCase) && token.Contains("BUILTIN\\" + t.Substring(8))
                || t.StartsWith("NT AUTHORITY\\", StringComparison.OrdinalIgnoreCase) && token.Contains(t.Substring(13));
        }
    }
}
