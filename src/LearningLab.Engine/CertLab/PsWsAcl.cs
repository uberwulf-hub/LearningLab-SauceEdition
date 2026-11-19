// Cert Lab - Get-Acl, Set-Acl and FileSystemAccessRule on the AZ-800 servers' NTFS model: Get-Acl returns a copy of a
// folder's security (its explicit and inherited entries, whether inheritance is disabled), the copy's methods
// (AddAccessRule, SetAccessRule, RemoveAccessRule, SetAccessRuleProtection, SetOwner) change only the copy, and Set-Acl
// writes the copy's explicit entries and protection to a path, as Windows does.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public class WsAclCopy { public WsServer S; public WinFile F; public List<WsAce> Explicit = new List<WsAce>(); public List<WsAce> Inherited = new List<WsAce>(); public bool Protected; public string Owner = ""; }

    public partial class PsCmdlets
    {
        static string Flags(WsAce a) { var l = new List<string>(); if (a.CI) l.Add("ContainerInherit"); if (a.OI) l.Add("ObjectInherit"); return l.Count == 0 ? "None" : string.Join(", ", l.ToArray()); }
        static PsObj RuleObj(WsAce a)
        {
            var r = new PsObj("System.Security.AccessControl.FileSystemAccessRule", "System.Security.AccessControl.AccessRule", "System.Security.AccessControl.AuthorizationRule", "System.Object") { Base = a };
            r.Val("FileSystemRights", "System.Security.AccessControl.FileSystemRights", NtfsRights.Name(a.Rights));
            r.Val("AccessControlType", "System.Security.AccessControl.AccessControlType", a.Allow ? "Allow" : "Deny");
            r.Val("IdentityReference", "System.Security.Principal.IdentityReference", a.Trustee);
            r.Val("IsInherited", "bool", a.Inherited);
            r.Val("InheritanceFlags", "System.Security.AccessControl.InheritanceFlags", Flags(a));
            r.Val("PropagationFlags", "System.Security.AccessControl.PropagationFlags", a.IO && a.NP ? "NoPropagateInherit, InheritOnly" : a.IO ? "InheritOnly" : a.NP ? "NoPropagateInherit" : "None");
            r.ToStr = y => "System.Security.AccessControl.FileSystemAccessRule";
            return r;
        }
        /// <summary>Get-Acl on a Windows Server machine of the AZ-800 network (null elsewhere: the PowerShell course's fixed ACL).</summary>
        public static PsObj WsFileAcl(PsRunspace R, PPath p, WinFile f)
        {
            var s = R.M.WsSrv;
            if (s == null) return null;
            var own = s.AclOf(f, false);
            var copy = new WsAclCopy { S = s, F = f, Protected = own != null && own.Protected, Owner = own == null ? "BUILTIN\\Administrators" : own.Owner };
            if (own != null) copy.Explicit.AddRange(own.Explicit.Select(x => x.Copy()));
            copy.Inherited.AddRange(s.EffectiveAces(f).Where(x => x.Inherited));
            var o = AclObj(R, p, "System.Security.AccessControl.FileSecurity", copy.Owner, new List<PsObj>(), "FileSystemRights", "");
            o.Base = copy;
            Refresh(o, copy);
            o.Method("AddAccessRule", "void AddAccessRule(System.Security.AccessControl.FileSystemAccessRule rule)", (self, a) => { copy.Explicit.Add(RuleArg(R, a)); Refresh(o, copy); return null; });
            o.Method("SetAccessRule", "void SetAccessRule(System.Security.AccessControl.FileSystemAccessRule rule)", (self, a) => { var x = RuleArg(R, a); copy.Explicit.RemoveAll(e => e.Allow == x.Allow && WsText.Eq(e.Trustee, x.Trustee)); copy.Explicit.Add(x); Refresh(o, copy); return null; });
            o.Method("RemoveAccessRule", "bool RemoveAccessRule(System.Security.AccessControl.FileSystemAccessRule rule)", (self, a) => { var x = RuleArg(R, a); int n = copy.Explicit.RemoveAll(e => e.Allow == x.Allow && WsText.Eq(e.Trustee, x.Trustee) && (e.Rights & x.Rights) == x.Rights); Refresh(o, copy); return n > 0; });
            o.Method("RemoveAccessRuleAll", "void RemoveAccessRuleAll(System.Security.AccessControl.FileSystemAccessRule rule)", (self, a) => { var x = RuleArg(R, a); copy.Explicit.RemoveAll(e => WsText.Eq(e.Trustee, x.Trustee)); Refresh(o, copy); return null; });
            o.Method("SetAccessRuleProtection", "void SetAccessRuleProtection(bool isProtected, bool preserveInheritance)", (self, a) =>
            {
                bool prot = a.Length > 0 && PsConv.ToBool(a[0]), keep = a.Length > 1 && PsConv.ToBool(a[1]);
                if (prot && !copy.Protected && keep) foreach (var e in copy.Inherited) { var c2 = e.Copy(); c2.Inherited = false; copy.Explicit.Add(c2); }
                if (prot) copy.Inherited.Clear();
                copy.Protected = prot; Refresh(o, copy); return null;
            });
            o.Method("SetOwner", "void SetOwner(System.Security.Principal.IdentityReference identity)", (self, a) => { copy.Owner = a.Length > 0 ? PsConv.ToStr(a[0]) : copy.Owner; Refresh(o, copy); return null; });
            return o;
        }
        static WsAce RuleArg(PsRunspace R, object[] a)
        {
            var po = a.Length > 0 ? PsConv.Base(a[0]) as PsObj : null;
            var ace = po == null ? null : po.Base as WsAce;
            if (ace == null) throw PsMembers.MethodError(R, null, "AddAccessRule", a.Length, "Cannot convert the argument to type \"System.Security.AccessControl.FileSystemAccessRule\": make the rule with New-Object System.Security.AccessControl.FileSystemAccessRule.", "System.Management.Automation.PSInvalidCastException");
            var c = ace.Copy(); c.Inherited = false; return c;
        }
        static void Refresh(PsObj o, WsAclCopy copy)
        {
            var rules = copy.Explicit.Concat(copy.Inherited).Select(RuleObj).ToList();
            o.Val("Access", "System.Security.AccessControl.AuthorizationRuleCollection", rules.Cast<object>().ToArray());
            o.Val("AccessToString", "string", string.Join("\n", copy.Explicit.Concat(copy.Inherited).Select(a => a.Trustee + " " + (a.Allow ? "Allow" : "Deny") + "  " + NtfsRights.Name(a.Rights)).ToArray()));
            o.Val("AreAccessRulesProtected", "bool", copy.Protected);
            o.Val("Owner", "string", copy.Owner);
        }

        static partial void RegWsFiles()
        {
            PsTypes.AddKnown("System.Security.AccessControl.FileSystemAccessRule", "System.Security.AccessControl.AccessRule");
            PsStatics.NewHook = (R, full, args) =>
            {
                if (full != "System.Security.AccessControl.FileSystemAccessRule") return null;
                if (args.Length != 3 && args.Length != 5) throw new PsRuntimeException(R.MakeError(null, "Cannot find an overload for \"FileSystemAccessRule\" and the argument count: \"" + args.Length + "\".", "ConstructorInvokedThrowException", "InvalidOperation", "System.Management.Automation.MethodException", null), false);
                string who = PsConv.ToStr(args[0]), rights = PsConv.ToStr(args[1]).Replace(" ", ""), type = PsConv.ToStr(args[args.Length - 1]);
                int m = NtfsRights.Parse(rights.Replace(",Synchronize", "").Replace("Synchronize,", ""));
                if (m <= 0) throw new PsRuntimeException(R.MakeError(null, "Cannot convert value \"" + rights + "\" to type \"System.Security.AccessControl.FileSystemRights\".", "InvalidCastFromStringToInteger", "InvalidArgument", "System.Management.Automation.PSInvalidCastException", null), false);
                var ace = new WsAce { Trustee = who, Rights = m, Allow = !WsText.Eq(type, "Deny") };
                if (args.Length == 5) { string inh = PsConv.ToStr(args[2]), prop = PsConv.ToStr(args[3]); ace.CI = inh.Contains("ContainerInherit"); ace.OI = inh.Contains("ObjectInherit"); ace.IO = prop.Contains("InheritOnly"); ace.NP = prop.Contains("NoPropagateInherit"); }
                return RuleObj(ace);
            };
            Add("Set-Acl", "Microsoft.PowerShell.Security", "Path:string[]@0 ! prop; AclObject:object@1 ! pipe; Passthru:switch; ClearCentralAccessPolicy:switch", (c, o) =>
            {
                var s = c.M.WsSrv;
                var po = PsConv.Base(c.Get("AclObject")) as PsObj; var copy = po == null ? null : po.Base as WsAclCopy;
                if (s == null || copy == null) throw c.Fail("Cert Lab simulates Set-Acl with an ACL from Get-Acl on the AZ-800 servers.", "CertLabNotSimulated", "NotImplemented", null, "System.NotSupportedException");
                foreach (var p0 in c.Strs("Path"))
                {
                    string path = c.R.FullFsPath(p0, c.R.CurrentFsDir); var f = c.M.FindFile(path);
                    if (f == null) { c.Error("Cannot find path '" + path + "' because it does not exist.", "PathNotFound", "ObjectNotFound", path, "System.Management.Automation.ItemNotFoundException"); continue; }
                    var acl = s.AclOf(f, true);
                    acl.Explicit.Clear(); acl.Explicit.AddRange(copy.Explicit.Select(x => x.Copy())); acl.Protected = copy.Protected; acl.Owner = copy.Owner;
                }
            }).Available = m => m.Ws != null;
            RegWsSmb();
        }
    }
}
