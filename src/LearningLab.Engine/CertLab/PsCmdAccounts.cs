// Cert Lab - Windows administration cmdlets, part 4: local users and groups (Microsoft.PowerShell.LocalAccounts):
// Get-, New-, Set-, Remove-, Rename-, Enable-, Disable-LocalUser; Get-, New-, Set-, Remove-, Rename-LocalGroup;
// Add-, Get-, Remove-LocalGroupMember. Changes write the Security log's account events (4720, 4732...).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public partial class PsCmdlets
    {
        const string LaNs = "Microsoft.PowerShell.Commands.";
        const int MaxPasswordAgeDays = 42;

        public static PsObj LocalUserObj(PsMachine m, WinUser u)
        {
            var o = new PsObj("Microsoft.PowerShell.Commands.LocalUser", "Microsoft.PowerShell.Commands.LocalPrincipal", "System.Object");
            o.Base = u;
            o.Val("AccountExpires", "System.Nullable[datetime]", u.AccountExpires);
            o.Val("Description", "string", u.Description);
            o.Val("Enabled", "bool", u.Enabled);
            o.Val("FullName", "string", u.FullName);
            o.Val("PasswordChangeableDate", "System.Nullable[datetime]", u.PasswordLastSet.HasValue && u.UserMayChangePassword ? u.PasswordLastSet : null);
            o.Val("PasswordExpires", "System.Nullable[datetime]", u.PasswordLastSet.HasValue && !u.PasswordNeverExpires ? (object)u.PasswordLastSet.Value.AddDays(MaxPasswordAgeDays) : null);
            o.Val("UserMayChangePassword", "bool", u.UserMayChangePassword);
            o.Val("PasswordRequired", "bool", u.PasswordRequired);
            o.Val("PasswordLastSet", "System.Nullable[datetime]", u.PasswordLastSet);
            o.Val("LastLogon", "System.Nullable[datetime]", u.LastLogon);
            o.Val("Name", "string", u.Name);
            o.Val("SID", "System.Security.Principal.SecurityIdentifier", SidObj(u.Sid));
            o.Val("PrincipalSource", "System.Nullable[Microsoft.PowerShell.Commands.PrincipalSource]", En("Microsoft.PowerShell.Commands.PrincipalSource", "Local"));
            o.Val("ObjectClass", "string", "User");
            o.ToStr = x => u.Name;
            o.DefaultDisplay = new[] { "Name", "Enabled", "Description" };
            o.Method("Clone", "Microsoft.PowerShell.Commands.LocalUser Clone()", (self, a) => LocalUserObj(m, u));
            return o;
        }
        public static PsObj LocalGroupObj(PsMachine m, WinGroup g)
        {
            var o = new PsObj("Microsoft.PowerShell.Commands.LocalGroup", "Microsoft.PowerShell.Commands.LocalPrincipal", "System.Object");
            o.Base = g;
            o.Val("Description", "string", g.Description);
            o.Val("Name", "string", g.Name);
            o.Val("SID", "System.Security.Principal.SecurityIdentifier", SidObj(g.Sid));
            o.Val("PrincipalSource", "System.Nullable[Microsoft.PowerShell.Commands.PrincipalSource]", En("Microsoft.PowerShell.Commands.PrincipalSource", "Local"));
            o.Val("ObjectClass", "string", "Group");
            o.ToStr = x => g.Name;
            o.DefaultDisplay = new[] { "Name", "Description" };
            return o;
        }
        static PsObj PrincipalObj(WinMember mb)
        {
            var o = new PsObj("Microsoft.PowerShell.Commands.LocalPrincipal", "System.Object");
            o.Base = mb;
            o.Val("Name", "string", mb.Name);
            o.Val("SID", "System.Security.Principal.SecurityIdentifier", SidObj(mb.Sid));
            o.Val("PrincipalSource", "System.Nullable[Microsoft.PowerShell.Commands.PrincipalSource]", En("Microsoft.PowerShell.Commands.PrincipalSource", mb.Source));
            o.Val("ObjectClass", "string", mb.Class);
            o.ToStr = x => mb.Name;
            o.DefaultDisplay = new[] { "ObjectClass", "Name", "PrincipalSource" };
            return o;
        }

        /// <summary>A member a person names: bob, WIN-SRV1\bob, .\bob, LAB\jdoe, jdoe@lab.example.com, a SID, a user or group object.</summary>
        static WinMember ResolvePrincipal(PsMachine m, object v)
        {
            v = PsConv.Base(v);
            var po = v as PsObj;
            if (po != null)
            {
                if (po.Base is WinUser) { var u = (WinUser)po.Base; return new WinMember { Name = m.Name + "\\" + u.Name, Class = "User", Source = "Local", Sid = u.Sid }; }
                if (po.Base is WinGroup) return null;
                if (po.Base is WinMember) { var x = (WinMember)po.Base; return new WinMember { Name = x.Name, Class = x.Class, Source = x.Source, Sid = x.Sid }; }
                v = po.Get("Name") ?? po.ToString();
            }
            string s = PsConv.ToStr(v).Trim();
            if (s.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase))
            {
                var lu = m.Users.FirstOrDefault(u => u.Sid.Equals(s, StringComparison.OrdinalIgnoreCase));
                if (lu != null) return new WinMember { Name = m.Name + "\\" + lu.Name, Sid = lu.Sid };
                foreach (var d in PsMachine.DomainPrincipals) { var p = d.Split('|'); if ((m.DomainSid + "-" + p[2]).Equals(s, StringComparison.OrdinalIgnoreCase)) return new WinMember { Name = m.DomainShort + "\\" + p[0], Class = p[1], Source = "ActiveDirectory", Sid = s }; }
                return null;
            }
            string dom = null, name = s;
            int bs = s.IndexOf('\\');
            if (bs >= 0) { dom = s.Substring(0, bs); name = s.Substring(bs + 1); }
            int at = name.IndexOf('@');
            if (at > 0 && dom == null) { dom = name.Substring(at + 1); name = name.Substring(0, at); }
            bool local = dom == null || dom == "." || dom.Equals(m.Name, StringComparison.OrdinalIgnoreCase);
            if (local)
            {
                var lu = m.FindUser(name);
                if (lu != null) return new WinMember { Name = m.Name + "\\" + lu.Name, Class = "User", Source = "Local", Sid = lu.Sid };
                if (dom != null) return null;
            }
            bool domain = dom == null || dom.Equals(m.DomainShort, StringComparison.OrdinalIgnoreCase) || dom.Equals(m.Domain, StringComparison.OrdinalIgnoreCase);
            if (domain)
                foreach (var d in PsMachine.DomainPrincipals)
                {
                    var p = d.Split('|');
                    if (p[0].Equals(name, StringComparison.OrdinalIgnoreCase)) return new WinMember { Name = m.DomainShort + "\\" + p[0], Class = p[1], Source = "ActiveDirectory", Sid = m.DomainSid + "-" + p[2] };
                }
            if (dom != null && dom.Equals("NT AUTHORITY", StringComparison.OrdinalIgnoreCase))
            {
                switch (name.ToUpperInvariant())
                {
                    case "AUTHENTICATED USERS": return new WinMember { Name = "NT AUTHORITY\\Authenticated Users", Class = "Group", Source = "Unknown", Sid = "S-1-5-11" };
                    case "INTERACTIVE": return new WinMember { Name = "NT AUTHORITY\\INTERACTIVE", Class = "Group", Source = "Unknown", Sid = "S-1-5-4" };
                    case "NETWORK SERVICE": return new WinMember { Name = "NT AUTHORITY\\NETWORK SERVICE", Class = "User", Source = "Unknown", Sid = "S-1-5-20" };
                    case "SYSTEM": return new WinMember { Name = "NT AUTHORITY\\SYSTEM", Class = "User", Source = "Unknown", Sid = "S-1-5-18" };
                }
            }
            if (dom == null && name.Equals("Everyone", StringComparison.OrdinalIgnoreCase)) return new WinMember { Name = "Everyone", Class = "Group", Source = "Unknown", Sid = "S-1-1-0" };
            return null;
        }

        static string PasswordProblem(PsMachine m, string user, string pw)
        {
            if (!m.IsServer || m.NoComplexity) return null;   // Cyber Range: its pretend companies switched the policy off
            if (pw.Length < 6) return "x";
            int kinds = (pw.Any(char.IsUpper) ? 1 : 0) + (pw.Any(char.IsLower) ? 1 : 0) + (pw.Any(char.IsDigit) ? 1 : 0) + (pw.Any(ch => !char.IsLetterOrDigit(ch)) ? 1 : 0);
            if (kinds < 3) return "x";
            if (user.Length >= 3 && pw.IndexOf(user, StringComparison.OrdinalIgnoreCase) >= 0) return "x";
            return null;
        }
        const string BadPassword = "Unable to update the password. The value provided for the new password does not meet the length, complexity, or history requirements of the domain.";
        static readonly char[] BadNameChars = "\"/\\[]:;|=,+*?<>@".ToCharArray();

        static List<WinUser> PickUsers(CmdCtx c, bool errors)
        {
            var res = new List<WinUser>();
            if (c.Has("InputObject"))
            {
                foreach (var x in c.Objs("InputObject"))
                {
                    var po = PsConv.Base(x) as PsObj; var u = po == null ? null : po.Base as WinUser;
                    if (u == null && po != null) u = c.M.FindUser(PsConv.ToStr(po.Get("Name")));
                    if (u == null || !c.M.Users.Contains(u)) { if (errors) c.Error("User " + (po == null ? PsConv.ToStr(x) : PsConv.ToStr(po.Get("Name"))) + " was not found.", "UserNotFound", "ObjectNotFound", po == null ? PsConv.ToStr(x) : PsConv.ToStr(po.Get("Name")), LaNs + "UserNotFoundException"); continue; }
                    res.Add(u);
                }
                return res;
            }
            if (c.Has("SID"))
            {
                foreach (var s in c.Strs("SID"))
                {
                    var u = c.M.Users.FirstOrDefault(x => x.Sid.Equals(s, StringComparison.OrdinalIgnoreCase));
                    if (u == null) { if (errors) c.Error("User " + s + " was not found.", "UserNotFound", "ObjectNotFound", s, LaNs + "UserNotFoundException"); continue; }
                    res.Add(u);
                }
                return res;
            }
            var names = c.Strs("Name") ?? new[] { "*" };
            foreach (var n in names)
            {
                if (PsConv.HasWildcard(n)) { foreach (var u in c.M.Users.Where(x => PsConv.Like(x.Name, n, false)).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)) if (!res.Contains(u)) res.Add(u); continue; }
                var one = c.M.FindUser(n);
                if (one == null) { if (errors) c.Error("User " + n + " was not found.", "UserNotFound", "ObjectNotFound", n, LaNs + "UserNotFoundException"); continue; }
                if (!res.Contains(one)) res.Add(one);
            }
            return res;
        }
        static List<WinGroup> PickGroups(CmdCtx c, string param, bool errors)
        {
            var res = new List<WinGroup>();
            if (c.Has("InputObject") && param == "Name")
            {
                foreach (var x in c.Objs("InputObject")) { var po = PsConv.Base(x) as PsObj; var g = po == null ? null : po.Base as WinGroup ?? c.M.FindGroup(PsConv.ToStr(po.Get("Name"))); if (g != null) res.Add(g); }
                return res;
            }
            if (c.Has("SID"))
            {
                foreach (var s in c.Strs("SID")) { var g = c.M.Groups.FirstOrDefault(x => x.Sid.Equals(s, StringComparison.OrdinalIgnoreCase)); if (g == null) { if (errors) c.Error("Group " + s + " was not found.", "GroupNotFound", "ObjectNotFound", s, LaNs + "GroupNotFoundException"); continue; } res.Add(g); }
                return res;
            }
            var raw = c.Objs(c.Has("Group") ? "Group" : param);
            if (raw.Count == 0 && param == "Name") raw = new List<object> { "*" };
            foreach (var r in raw)
            {
                var po = PsConv.Base(r) as PsObj;
                if (po != null && po.Base is WinGroup) { res.Add((WinGroup)po.Base); continue; }
                string n = po != null ? PsConv.ToStr(po.Get("Name")) : PsConv.ToStr(r);
                if (PsConv.HasWildcard(n) && param == "Name") { foreach (var g in c.M.Groups.Where(x => PsConv.Like(x.Name, n, false)).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)) if (!res.Contains(g)) res.Add(g); continue; }
                var one = c.M.FindGroup(n);
                if (one == null) { if (errors) c.Error("Group " + n + " was not found.", "GroupNotFound", "ObjectNotFound", n, LaNs + "GroupNotFoundException"); continue; }
                if (!res.Contains(one)) res.Add(one);
            }
            return res;
        }
        static string Pw(object v)
        {
            v = PsConv.Base(v);
            var s = v as PsSecureString;
            return s == null ? (v == null ? null : PsConv.ToStr(v)) : s.Plain;
        }
        static bool LengthOk(CmdCtx c, string param, int max)
        {
            var s = c.Str(param);
            if (s == null || s.Length <= max) return true;
            var r = c.MakeError("Cannot validate argument on parameter '" + param + "'. The character length of the " + s.Length + " argument is too long. Shorten the character length of the argument so it is fewer than or equal to \"" + max + "\" characters, and then try the command again.", "ParameterArgumentValidationError", "InvalidData", null, "System.Management.Automation.ParameterBindingValidationException");
            throw new PsRuntimeException(r, false);
        }

        static void RegAccounts()
        {
            const string userSel = "Name:string[]@0 pipe prop ps=Default; SID:SecurityIdentifier[] pipe prop ps=SecurityIdentifier; InputObject:LocalUser[] pipe ps=InputObject";
            Add("Get-LocalUser", Local, "Name:string[]@0 pipe prop ps=Default; SID:string[] pipe prop ps=SecurityIdentifier", (c, o) =>
            {
                foreach (var u in PickUsers(c, true)) c.Out(LocalUserObj(c.M, u));
            }).DefaultSet = "Default";

            var nu = Add("New-LocalUser", Local, "Name:string@0 ! pipe prop; Password:securestring ! ps=Password; NoPassword:switch ! ps=NoPassword; AccountExpires:datetime prop; AccountNeverExpires:switch prop; Description:string prop; Disabled:switch prop; FullName:string prop; PasswordNeverExpires:switch prop; UserMayNotChangePassword:switch prop", (c, o) =>
            {
                string n = c.Str("Name");
                LengthOk(c, "Name", 20); LengthOk(c, "Description", 48);
                if (n.IndexOfAny(BadNameChars) >= 0 || n.Trim().Trim('.').Length == 0) { c.Error("The name " + n + " contains invalid characters.", "InvalidName", "InvalidArgument", n, LaNs + "InvalidNameException"); return; }
                if (c.M.FindUser(n) != null) { c.Error("User " + n + " already exists.", "UserExists", "ResourceExists", n, LaNs + "UserExistsException"); return; }
                if (c.M.FindGroup(n) != null) { c.Error("The name " + n + " is already in use.", "NameInUse", "ResourceExists", n, LaNs + "NameInUseException"); return; }
                string pw = c.Has("Password") ? Pw(c.Get("Password")) : null;
                if (pw != null && PasswordProblem(c.M, n, pw) != null) { c.Error(BadPassword, "InvalidPassword", "InvalidOperation", null, LaNs + "InvalidPasswordException"); return; }
                if (!c.ShouldProcess(n, "Create local user")) return;
                var u = new WinUser { Name = n, FullName = c.Str("FullName") ?? "", Description = c.Str("Description") ?? "", Enabled = !c.Sw("Disabled"), Rid = c.M.NextRid++ };
                u.Sid = c.M.MachineSid + "-" + u.Rid;
                if (pw != null) { u.Password = pw; u.HasPassword = true; u.PasswordLastSet = c.M.Now; }
                else { u.PasswordRequired = false; }
                u.PasswordNeverExpires = c.Sw("PasswordNeverExpires") || pw == null;
                u.UserMayChangePassword = !c.Sw("UserMayNotChangePassword");
                if (c.Has("AccountExpires")) u.AccountExpires = (DateTime)c.Get("AccountExpires");
                c.M.Users.Add(u);
                c.M.AccountEvent(4720, u.Name, u.Sid, "Attributes:\r\n\tSAM Account Name:\t" + u.Name + "\r\n\tDisplay Name:\t\t" + (u.FullName.Length > 0 ? u.FullName : "<value not set>") + "\r\n\tUser Principal Name:\t-\r\n\tPassword Last Set:\t" + (u.PasswordLastSet.HasValue ? u.PasswordLastSet.Value.ToString(PsConv.Us) : "<never>") + "\r\n\tAccount Expires:\t\t" + (u.AccountExpires.HasValue ? u.AccountExpires.Value.ToString(PsConv.Us) : "<never>"));
                if (u.Enabled) c.M.AccountEvent(4722, u.Name, u.Sid, null);
                c.M.AccountEvent(4738, u.Name, u.Sid, null);
                c.M.AdminChanged("user:" + u.Name);
                c.Out(LocalUserObj(c.M, u));
            });
            nu.ShouldProcess = true; nu.DefaultSet = "Password";

            var su = Add("Set-LocalUser", Local, "Name:string@0 ! pipe prop ps=Name; SID:string ! pipe prop ps=SecurityIdentifier; InputObject:LocalUser ! pipe ps=InputObject; AccountExpires:datetime; AccountNeverExpires:switch; Description:string; FullName:string; Password:securestring; PasswordNeverExpires:bool; UserMayChangePassword:bool", (c, o) =>
            {
                LengthOk(c, "Description", 48);
                foreach (var u in PickUsers(c, true))
                {
                    if (!c.ShouldProcess(u.Name, "Modify local user")) continue;
                    if (c.Has("Password"))
                    {
                        string pw = Pw(c.Get("Password"));
                        if (PasswordProblem(c.M, u.Name, pw) != null) { c.Error(BadPassword, "InvalidPassword", "InvalidOperation", null, LaNs + "InvalidPasswordException"); continue; }
                        u.Password = pw; u.HasPassword = true; u.PasswordLastSet = c.M.Now; u.PasswordRequired = true;
                        if (!c.Has("PasswordNeverExpires") && u.PasswordNeverExpires && u.Rid >= 1000 && !u.HasPassword) u.PasswordNeverExpires = false;
                        c.M.AccountEvent(4724, u.Name, u.Sid, null);
                    }
                    if (c.Has("Description")) u.Description = c.Str("Description");
                    if (c.Has("FullName")) u.FullName = c.Str("FullName");
                    if (c.Has("AccountExpires")) u.AccountExpires = (DateTime)c.Get("AccountExpires");
                    if (c.Sw("AccountNeverExpires")) u.AccountExpires = null;
                    if (c.Has("PasswordNeverExpires")) u.PasswordNeverExpires = c.Sw("PasswordNeverExpires");
                    if (c.Has("UserMayChangePassword")) u.UserMayChangePassword = c.Sw("UserMayChangePassword");
                    c.M.AccountEvent(4738, u.Name, u.Sid, null);
                    c.M.AdminChanged("user:" + u.Name);
                }
            });
            su.ShouldProcess = true; su.DefaultSet = "Name";

            Action<string, string, Action<CmdCtx, WinUser>> userCmd = (name, action, f) =>
            {
                var d = Add(name, Local, userSel, (c, o) => { foreach (var u in PickUsers(c, true)) { if (!c.ShouldProcess(u.Name, action)) continue; f(c, u); c.M.AdminChanged("user:" + u.Name); } });
                d.ShouldProcess = true; d.DefaultSet = "Default";
            };
            userCmd("Remove-LocalUser", "Remove local user", (c, u) =>
            {
                if (u.Rid == 500 || u.Rid == 501 || u.Rid == 503 || u.Rid == 504) { c.Error("Cannot perform this operation on built-in accounts.", "AccessDenied", "PermissionDenied", u.Name, LaNs + "AccessDeniedException"); return; }
                c.M.Users.Remove(u);
                foreach (var g in c.M.Groups) g.Members.RemoveAll(x => x.Sid == u.Sid);
                c.M.AccountEvent(4726, u.Name, u.Sid, null);
            });
            userCmd("Enable-LocalUser", "Enable local user", (c, u) => { if (!u.Enabled) { u.Enabled = true; c.M.AccountEvent(4722, u.Name, u.Sid, null); } });
            userCmd("Disable-LocalUser", "Disable local user", (c, u) => { if (u.Enabled) { u.Enabled = false; c.M.AccountEvent(4725, u.Name, u.Sid, null); } });

            var ru = Add("Rename-LocalUser", Local, "Name:string@0 ! pipe prop ps=Default; SID:string ! pipe prop ps=SecurityIdentifier; InputObject:LocalUser ! pipe ps=InputObject; NewName:string@1 !", (c, o) =>
            {
                foreach (var u in PickUsers(c, true))
                {
                    string nn = c.Str("NewName");
                    if (nn.Length > 20 || nn.IndexOfAny(BadNameChars) >= 0) { c.Error("The name " + nn + " contains invalid characters.", "InvalidName", "InvalidArgument", nn, LaNs + "InvalidNameException"); continue; }
                    if (c.M.FindUser(nn) != null || c.M.FindGroup(nn) != null) { c.Error("The name " + nn + " is already in use.", "NameInUse", "ResourceExists", nn, LaNs + "NameInUseException"); continue; }
                    if (!c.ShouldProcess(u.Name, "Rename local user")) continue;
                    string old = u.Name;
                    u.Name = nn;
                    foreach (var g in c.M.Groups) foreach (var mb in g.Members) if (mb.Sid == u.Sid) mb.Name = c.M.Name + "\\" + nn;
                    c.M.AccountEvent(4781, nn, u.Sid, "Old Account Name:\t" + old + "\r\nNew Account Name:\t" + nn);
                    c.M.AdminChanged("user:" + nn);
                }
            });
            ru.ShouldProcess = true; ru.DefaultSet = "Default";

            // ---------------- groups
            Add("Get-LocalGroup", Local, "Name:string[]@0 pipe prop ps=Default; SID:string[] pipe prop ps=SecurityIdentifier", (c, o) =>
            {
                foreach (var g in PickGroups(c, "Name", true)) c.Out(LocalGroupObj(c.M, g));
            }).DefaultSet = "Default";
            var ng = Add("New-LocalGroup", Local, "Name:string@0 ! pipe prop; Description:string prop", (c, o) =>
            {
                string n = c.Str("Name");
                LengthOk(c, "Name", 256); LengthOk(c, "Description", 48);
                if (n.IndexOfAny(BadNameChars) >= 0) { c.Error("The name " + n + " contains invalid characters.", "InvalidName", "InvalidArgument", n, LaNs + "InvalidNameException"); return; }
                if (c.M.FindGroup(n) != null) { c.Error("Group " + n + " already exists.", "GroupExists", "ResourceExists", n, LaNs + "GroupExistsException"); return; }
                if (c.M.FindUser(n) != null) { c.Error("The name " + n + " is already in use.", "NameInUse", "ResourceExists", n, LaNs + "NameInUseException"); return; }
                if (!c.ShouldProcess(n, "Create local group")) return;
                var g = new WinGroup { Name = n, Description = c.Str("Description") ?? "", Sid = c.M.MachineSid + "-" + c.M.NextRid++ };
                c.M.Groups.Add(g);
                c.M.AccountEvent(4731, g.Name, g.Sid, null);
                c.M.AdminChanged("group:" + g.Name);
                c.Out(LocalGroupObj(c.M, g));
            });
            ng.ShouldProcess = true;
            var sg = Add("Set-LocalGroup", Local, "Name:string@0 ! pipe prop ps=Default; SID:string ! pipe prop ps=SecurityIdentifier; InputObject:LocalGroup ! pipe ps=InputObject; Description:string !", (c, o) =>
            {
                LengthOk(c, "Description", 48);
                foreach (var g in PickGroups(c, "Name", true)) { if (!c.ShouldProcess(g.Name, "Modify local group")) continue; g.Description = c.Str("Description"); c.M.AccountEvent(4735, g.Name, g.Sid, null); c.M.AdminChanged("group:" + g.Name); }
            });
            sg.ShouldProcess = true; sg.DefaultSet = "Default";
            var rg = Add("Remove-LocalGroup", Local, "Name:string[]@0 ! pipe prop ps=Default; SID:string[] ! pipe prop ps=SecurityIdentifier; InputObject:LocalGroup[] ! pipe ps=InputObject", (c, o) =>
            {
                foreach (var g in PickGroups(c, "Name", true))
                {
                    if (g.Sid.StartsWith("S-1-5-32-", StringComparison.Ordinal)) { c.Error("Cannot perform this operation on built-in accounts.", "AccessDenied", "PermissionDenied", g.Name, LaNs + "AccessDeniedException"); continue; }
                    if (!c.ShouldProcess(g.Name, "Remove local group")) continue;
                    c.M.Groups.Remove(g); c.M.AccountEvent(4734, g.Name, g.Sid, null); c.M.AdminChanged("group:" + g.Name);
                }
            });
            rg.ShouldProcess = true; rg.DefaultSet = "Default";
            var rng = Add("Rename-LocalGroup", Local, "Name:string@0 ! pipe prop ps=Default; SID:string ! pipe prop ps=SecurityIdentifier; InputObject:LocalGroup ! pipe ps=InputObject; NewName:string@1 !", (c, o) =>
            {
                foreach (var g in PickGroups(c, "Name", true))
                {
                    string nn = c.Str("NewName");
                    if (c.M.FindGroup(nn) != null || c.M.FindUser(nn) != null) { c.Error("The name " + nn + " is already in use.", "NameInUse", "ResourceExists", nn, LaNs + "NameInUseException"); continue; }
                    if (!c.ShouldProcess(g.Name, "Rename local group")) continue;
                    g.Name = nn; c.M.AccountEvent(4735, g.Name, g.Sid, null); c.M.AdminChanged("group:" + nn);
                }
            });
            rng.ShouldProcess = true; rng.DefaultSet = "Default";

            // ---------------- members
            const string grpSel = "Name:string@0 ! ps=Default; Group:object ! pipe ps=Group; SID:string ! ps=SecurityIdentifier";
            var am = Add("Add-LocalGroupMember", Local, grpSel + "; Member:object[]@1 ! pipe", (c, o) =>
            {
                var gl = PickGroups(c, c.Has("Group") ? "Group" : "Name", true);
                if (gl.Count == 0) return;
                var g = gl[0];
                foreach (var mv in c.Objs("Member"))
                {
                    var mb = ResolvePrincipal(c.M, mv);
                    string label = PsConv.ToStr(PsConv.Base(mv) is PsObj ? ((PsObj)PsConv.Base(mv)).Get("Name") : mv);
                    if (mb == null) { c.Error("Principal " + label + " was not found.", "PrincipalNotFound", "ObjectNotFound", label, LaNs + "PrincipalNotFoundException"); continue; }
                    if (g.Members.Any(x => x.Sid == mb.Sid && mb.Sid.Length > 0 || x.Name.Equals(mb.Name, StringComparison.OrdinalIgnoreCase))) { c.Error(mb.Name + " is already a member of group " + g.Name + ".", "MemberExists", "ResourceExists", g.Name, LaNs + "MemberExistsException"); continue; }
                    if (!c.ShouldProcess(g.Name, "Add member " + mb.Name)) continue;
                    g.Members.Add(mb);
                    c.M.AccountEvent(4732, g.Name, g.Sid, "Member:\r\n\tSecurity ID:\t\t" + mb.Sid + "\r\n\tAccount Name:\t\t" + mb.Name);
                    c.M.AdminChanged("group:" + g.Name);
                }
            });
            am.ShouldProcess = true; am.DefaultSet = "Default";
            var rm = Add("Remove-LocalGroupMember", Local, grpSel + "; Member:object[]@1 ! pipe", (c, o) =>
            {
                var gl = PickGroups(c, c.Has("Group") ? "Group" : "Name", true);
                if (gl.Count == 0) return;
                var g = gl[0];
                foreach (var mv in c.Objs("Member"))
                {
                    var mb = ResolvePrincipal(c.M, mv);
                    string label = PsConv.ToStr(PsConv.Base(mv) is PsObj ? ((PsObj)PsConv.Base(mv)).Get("Name") : mv);
                    if (mb == null) { c.Error("Principal " + label + " was not found.", "PrincipalNotFound", "ObjectNotFound", label, LaNs + "PrincipalNotFoundException"); continue; }
                    var have = g.Members.FirstOrDefault(x => x.Sid == mb.Sid && mb.Sid.Length > 0 || x.Name.Equals(mb.Name, StringComparison.OrdinalIgnoreCase));
                    if (have == null) { c.Error("Member " + mb.Name + " was not found in group " + g.Name + ".", "MemberNotFound", "ObjectNotFound", label, LaNs + "MemberNotFoundException"); continue; }
                    if (!c.ShouldProcess(g.Name, "Remove member " + mb.Name)) continue;
                    if (g.Sid == "S-1-5-32-544" && have.Sid.EndsWith("-500", StringComparison.Ordinal) && have.Source == "Local") { c.Error("Cannot perform this operation on built-in accounts.", "AccessDenied", "PermissionDenied", label, LaNs + "AccessDeniedException"); continue; }
                    g.Members.Remove(have);
                    c.M.AccountEvent(4733, g.Name, g.Sid, "Member:\r\n\tSecurity ID:\t\t" + have.Sid + "\r\n\tAccount Name:\t\t" + have.Name);
                    c.M.AdminChanged("group:" + g.Name);
                }
            });
            rm.ShouldProcess = true; rm.DefaultSet = "Default";
            Add("Get-LocalGroupMember", Local, grpSel + "; Member:string@1", (c, o) =>
            {
                var gl = PickGroups(c, c.Has("Group") ? "Group" : "Name", true);
                foreach (var g in gl)
                {
                    var pat = c.Str("Member");
                    var list = g.Members.Where(x => pat == null || PsConv.Like(x.Name, pat, false) || PsConv.Like(x.Name.Substring(x.Name.IndexOf('\\') + 1), pat, false)).ToList();
                    if (pat != null && list.Count == 0 && !PsConv.HasWildcard(pat)) { c.Error("Member " + pat + " was not found in group " + g.Name + ".", "MemberNotFound", "ObjectNotFound", pat, LaNs + "MemberNotFoundException"); continue; }
                    foreach (var mb in list) c.Out(PrincipalObj(mb));
                }
            }).DefaultSet = "Default";
        }
    }
}
