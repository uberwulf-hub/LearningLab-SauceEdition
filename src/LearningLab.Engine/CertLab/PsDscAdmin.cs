// Cert Lab - the DSC resources that manage the administration model (PsMachineAdmin.cs): Service, User, Group and
// WindowsFeature from PSDesiredStateConfiguration 1.1. Each has Test (is the machine as described?), Set (make it so,
// with verbose lines in our own words) and Get (the current values). WindowsFeature runs Install-WindowsFeature /
// Uninstall-WindowsFeature so it follows the same rules (parents, restarts, server only).
using System;
using System.Collections.Generic;
using System.Linq;

namespace CertLab
{
    public static class PsDscAdmin
    {
        static bool Absent(DscCall c) { return c.Str("Ensure", "Present").Equals("Absent", StringComparison.OrdinalIgnoreCase); }
        static string Short(string n) { int i = n.LastIndexOf('\\'); return i < 0 ? n : n.Substring(i + 1); }

        public static void RegisterAll()
        {
            // ---------------------------------------------------------------- Service
            PsDsc.Register(new DscResourceDef
            {
                Name = "Service", MofClass = "MSFT_ServiceResource",
                Props = DscProp.List("Name:string key; BuiltInAccount:string=LocalService|LocalSystem|NetworkService; Credential:PSCredential; Dependencies:string[]; Description:string; DisplayName:string; Ensure:string=Absent|Present; Path:string; StartupType:string=Automatic|Disabled|Manual; State:string=Running|Stopped"),
                Test = c =>
                {
                    var s = c.M.Svc(c.Str("Name"));
                    if (Absent(c)) return s == null;
                    if (s == null) { c.Verbose("Service '" + c.Str("Name") + "' does not exist."); return false; }
                    if (c.Has("StartupType") && !s.StartType.Equals(c.Str("StartupType"), StringComparison.OrdinalIgnoreCase)) { c.Verbose("Service '" + s.Name + "' starts " + s.StartType + ", not " + c.Str("StartupType") + "."); return false; }
                    if (c.Has("State") && !s.Status.Equals(c.Str("State"), StringComparison.OrdinalIgnoreCase)) { c.Verbose("Service '" + s.Name + "' is " + s.Status + ", not " + c.Str("State") + "."); return false; }
                    if (c.Has("DisplayName") && s.DisplayName != c.Str("DisplayName")) { c.Verbose("Service '" + s.Name + "' has a different display name."); return false; }
                    if (c.Has("Description") && s.Description != c.Str("Description")) { c.Verbose("Service '" + s.Name + "' has a different description."); return false; }
                    return true;
                },
                Set = c =>
                {
                    var s = c.M.Svc(c.Str("Name"));
                    if (Absent(c)) { if (s != null) throw c.Fail("Removing services is not part of the Cert Lab simulation. Use Ensure = 'Present' or leave Ensure out."); return; }
                    if (s == null) throw c.Fail("Service '" + c.Str("Name") + "' does not exist, and creating one needs Path, which the Cert Lab simulation does not support in DSC. Use New-Service first.");
                    if (c.Has("DisplayName")) s.DisplayName = c.Str("DisplayName");
                    if (c.Has("Description")) s.Description = c.Str("Description");
                    if (c.Has("StartupType") && !s.StartType.Equals(c.Str("StartupType"), StringComparison.OrdinalIgnoreCase))
                    {
                        string st = c.Str("StartupType"); st = char.ToUpperInvariant(st[0]) + st.Substring(1).ToLowerInvariant();
                        c.M.SetStartType(s, st, true);
                        c.Verbose("Service '" + s.Name + "' now starts " + st + ".");
                    }
                    if (c.Has("State"))
                    {
                        bool run = c.Str("State").Equals("Running", StringComparison.OrdinalIgnoreCase);
                        if (run && s.Status != "Running")
                        {
                            if (s.StartType == "Disabled") throw c.Fail("Service '" + s.Name + "' is disabled, so it cannot be started. Set StartupType as well.");
                            var err = c.M.StartService(s, true);
                            if (err != null) throw c.Fail("Failed to start service '" + s.Name + "' (Windows error " + err + ").");
                            c.Verbose("Service '" + s.Name + "' started.");
                        }
                        else if (!run && s.Status != "Stopped")
                        {
                            var err = c.M.StopService(s, true, true);
                            if (err != null) throw c.Fail("Failed to stop service '" + s.Name + "' (Windows error " + err + ").");
                            c.Verbose("Service '" + s.Name + "' stopped.");
                        }
                    }
                    c.M.AdminChanged("service:" + s.Name);
                },
                Get = c =>
                {
                    var s = c.M.Svc(c.Str("Name"));
                    return new Dictionary<string, object> { { "BuiltInAccount", s == null ? null : s.Account }, { "Credential", null }, { "Dependencies", s == null ? null : s.DependsOn.Cast<object>().ToArray() }, { "Description", s == null ? null : s.Description }, { "DisplayName", s == null ? null : s.DisplayName }, { "Ensure", s == null ? "Absent" : "Present" }, { "Name", c.Str("Name") }, { "Path", s == null ? null : s.BinaryPath }, { "StartupType", s == null ? null : s.StartType }, { "State", s == null ? null : s.Status } };
                }
            });

            // ---------------------------------------------------------------- User
            PsDsc.Register(new DscResourceDef
            {
                Name = "User", MofClass = "MSFT_UserResource",
                Props = DscProp.List("UserName:string key; Description:string; Disabled:bool; Ensure:string=Absent|Present; FullName:string; Password:PSCredential; PasswordChangeNotAllowed:bool; PasswordChangeRequired:bool; PasswordNeverExpires:bool"),
                Test = c =>
                {
                    var u = c.M.FindUser(c.Str("UserName"));
                    if (Absent(c)) return u == null;
                    if (u == null) { c.Verbose("User '" + c.Str("UserName") + "' does not exist."); return false; }
                    if (c.Has("Disabled") && u.Enabled == c.Bool("Disabled")) { c.Verbose("User '" + u.Name + "' is " + (u.Enabled ? "enabled" : "disabled") + "."); return false; }
                    if (c.Has("FullName") && u.FullName != c.Str("FullName")) { c.Verbose("User '" + u.Name + "' has a different full name."); return false; }
                    if (c.Has("Description") && u.Description != c.Str("Description")) { c.Verbose("User '" + u.Name + "' has a different description."); return false; }
                    if (c.Has("PasswordNeverExpires") && u.PasswordNeverExpires != c.Bool("PasswordNeverExpires")) { c.Verbose("User '" + u.Name + "': PasswordNeverExpires differs."); return false; }
                    if (c.Has("PasswordChangeNotAllowed") && u.UserMayChangePassword == c.Bool("PasswordChangeNotAllowed")) { c.Verbose("User '" + u.Name + "': PasswordChangeNotAllowed differs."); return false; }
                    return true;
                },
                Set = c =>
                {
                    var m = c.M;
                    var u = m.FindUser(c.Str("UserName"));
                    if (Absent(c))
                    {
                        if (u == null) return;
                        if (u.Rid < 1000) throw c.Fail("User '" + u.Name + "' is a built-in account and cannot be removed.");
                        m.Users.Remove(u);
                        foreach (var g in m.Groups) g.Members.RemoveAll(x => x.Sid == u.Sid);
                        m.AccountEvent(4726, u.Name, u.Sid, null);
                        c.Verbose("User '" + u.Name + "' removed.");
                        m.AdminChanged("user:" + u.Name);
                        return;
                    }
                    if (u == null)
                    {
                        u = new WinUser { Name = c.Str("UserName"), Rid = m.NextRid++, PasswordLastSet = m.Now };
                        u.Sid = m.MachineSid + "-" + u.Rid;
                        m.Users.Add(u);
                        var ug = m.FindGroup("Users"); if (ug != null) ug.Members.Add(new WinMember { Name = m.Name + "\\" + u.Name, Sid = u.Sid });
                        m.AccountEvent(4720, u.Name, u.Sid, null); m.AccountEvent(4722, u.Name, u.Sid, null);
                        c.Verbose("User '" + u.Name + "' created.");
                    }
                    if (c.Has("Disabled")) u.Enabled = !c.Bool("Disabled");
                    if (c.Has("FullName")) u.FullName = c.Str("FullName");
                    if (c.Has("Description")) u.Description = c.Str("Description");
                    if (c.Has("PasswordNeverExpires")) u.PasswordNeverExpires = c.Bool("PasswordNeverExpires");
                    if (c.Has("PasswordChangeNotAllowed")) u.UserMayChangePassword = !c.Bool("PasswordChangeNotAllowed");
                    c.Verbose("User '" + u.Name + "' configured.");
                    m.AdminChanged("user:" + u.Name);
                },
                Get = c =>
                {
                    var u = c.M.FindUser(c.Str("UserName"));
                    return new Dictionary<string, object> { { "Description", u == null ? null : u.Description }, { "Disabled", u == null ? null : (object)!u.Enabled }, { "Ensure", u == null ? "Absent" : "Present" }, { "FullName", u == null ? null : u.FullName }, { "Password", null }, { "PasswordChangeNotAllowed", u == null ? null : (object)!u.UserMayChangePassword }, { "PasswordChangeRequired", null }, { "PasswordNeverExpires", u == null ? null : (object)u.PasswordNeverExpires }, { "UserName", c.Str("UserName") } };
                }
            });

            // ---------------------------------------------------------------- Group
            PsDsc.Register(new DscResourceDef
            {
                Name = "Group", MofClass = "MSFT_GroupResource",
                Props = DscProp.List("GroupName:string key; Credential:PSCredential; Description:string; Ensure:string=Absent|Present; Members:string[]; MembersToExclude:string[]; MembersToInclude:string[]"),
                Test = c =>
                {
                    var g = c.M.FindGroup(c.Str("GroupName"));
                    if (Absent(c)) return g == null;
                    if (g == null) { c.Verbose("Group '" + c.Str("GroupName") + "' does not exist."); return false; }
                    if (c.Has("Description") && g.Description != c.Str("Description")) { c.Verbose("Group '" + g.Name + "' has a different description."); return false; }
                    var have = g.Members.Select(x => Short(x.Name)).ToList();
                    Func<string, bool> isIn = n => have.Contains(Short(n), StringComparer.OrdinalIgnoreCase);
                    if (c.Has("Members"))
                    {
                        var want = c.Strs("Members");
                        if (want.Length != have.Count || !want.All(isIn)) { c.Verbose("Group '" + g.Name + "' does not have exactly the listed members."); return false; }
                    }
                    if (c.Has("MembersToInclude") && !c.Strs("MembersToInclude").All(isIn)) { c.Verbose("Group '" + g.Name + "' is missing members."); return false; }
                    if (c.Has("MembersToExclude") && c.Strs("MembersToExclude").Any(isIn)) { c.Verbose("Group '" + g.Name + "' has members that should not be in it."); return false; }
                    return true;
                },
                Set = c =>
                {
                    var m = c.M;
                    var g = m.FindGroup(c.Str("GroupName"));
                    if (Absent(c))
                    {
                        if (g == null) return;
                        if (g.Sid.StartsWith("S-1-5-32-", StringComparison.Ordinal)) throw c.Fail("Group '" + g.Name + "' is a built-in group and cannot be removed.");
                        m.Groups.Remove(g); c.Verbose("Group '" + g.Name + "' removed."); m.AdminChanged("group:" + g.Name);
                        return;
                    }
                    if (c.Has("Members") && (c.Has("MembersToInclude") || c.Has("MembersToExclude")))
                        throw c.Fail("Use either Members, or MembersToInclude / MembersToExclude, not both.");
                    if (g == null)
                    {
                        g = new WinGroup { Name = c.Str("GroupName"), Sid = m.MachineSid + "-" + m.NextRid++ };
                        m.Groups.Add(g); m.AccountEvent(4731, g.Name, g.Sid, null);
                        c.Verbose("Group '" + g.Name + "' created.");
                    }
                    if (c.Has("Description")) g.Description = c.Str("Description");
                    Action<string> add = n =>
                    {
                        if (g.Members.Any(x => Short(x.Name).Equals(Short(n), StringComparison.OrdinalIgnoreCase))) return;
                        var mb = PsCmdlets.ResolveMember(m, n);
                        if (mb == null) throw c.Fail("Could not find a user or group named '" + n + "'.");
                        g.Members.Add(mb); c.Verbose("Added '" + mb.Name + "' to group '" + g.Name + "'.");
                    };
                    Action<string> drop = n => { if (g.Members.RemoveAll(x => Short(x.Name).Equals(Short(n), StringComparison.OrdinalIgnoreCase)) > 0) c.Verbose("Removed '" + n + "' from group '" + g.Name + "'."); };
                    if (c.Has("Members"))
                    {
                        var want = c.Strs("Members");
                        foreach (var x in g.Members.Select(x => x.Name).ToList()) if (!want.Any(w => Short(w).Equals(Short(x), StringComparison.OrdinalIgnoreCase))) drop(x);
                        foreach (var w in want) add(w);
                    }
                    foreach (var w in c.Strs("MembersToInclude") ?? new string[0]) add(w);
                    foreach (var w in c.Strs("MembersToExclude") ?? new string[0]) drop(w);
                    m.AdminChanged("group:" + g.Name);
                },
                Get = c =>
                {
                    var g = c.M.FindGroup(c.Str("GroupName"));
                    return new Dictionary<string, object> { { "Credential", null }, { "Description", g == null ? null : g.Description }, { "Ensure", g == null ? "Absent" : "Present" }, { "GroupName", c.Str("GroupName") }, { "Members", g == null ? null : g.Members.Select(x => (object)x.Name).ToArray() }, { "MembersToExclude", null }, { "MembersToInclude", null } };
                }
            });

            // ---------------------------------------------------------------- WindowsFeature
            PsDsc.Register(new DscResourceDef
            {
                Name = "WindowsFeature", MofClass = "MSFT_RoleResource",
                Props = DscProp.List("Name:string key; Credential:PSCredential; Ensure:string=Absent|Present; IncludeAllSubFeature:bool; LogPath:string; Source:string"),
                Test = c =>
                {
                    if (!c.M.IsServer) throw c.Fail("The WindowsFeature resource works only on Windows Server. On a client, Windows features are turned on with Enable-WindowsOptionalFeature.");
                    var f = c.M.FindFeature(c.Str("Name"));
                    if (f == null) throw c.Fail("The requested feature " + c.Str("Name") + " could not be found.");
                    bool on = f.State == "Installed" || f.State == "InstallPending";
                    if (Absent(c)) return !on;
                    if (!on) c.Verbose("The feature " + f.Name + " is not installed.");
                    return on;
                },
                Set = c =>
                {
                    string cmd = (Absent(c) ? "Uninstall-WindowsFeature" : "Install-WindowsFeature") + " -Name '" + c.Str("Name").Replace("'", "''") + "'" + (!Absent(c) && c.Bool("IncludeAllSubFeature") ? " -IncludeAllSubFeature" : "") + " -WarningAction SilentlyContinue | Out-Null";
                    try { c.R.InvokeBlock(new PsScriptBlock { Ast = PsParser.Parse(cmd, "") }, c.R.Global, null, null, false); }
                    catch (PsRuntimeException e) { throw c.Fail(e.Rec.Exception.Message); }
                    var f = c.M.FindFeature(c.Str("Name"));
                    c.Verbose((Absent(c) ? "Uninstalled " : "Installed ") + "the feature " + c.Str("Name") + "." + (f != null && f.State == "InstallPending" ? " A restart is needed to finish." : ""));
                },
                Get = c =>
                {
                    var f = c.M.FindFeature(c.Str("Name"));
                    return new Dictionary<string, object> { { "Credential", null }, { "DisplayName", f == null ? null : f.Display }, { "Ensure", f != null && (f.State == "Installed" || f.State == "InstallPending") ? "Present" : "Absent" }, { "IncludeAllSubFeature", null }, { "LogPath", null }, { "Name", c.Str("Name") }, { "Source", null } };
                }
            });
        }
    }
}
