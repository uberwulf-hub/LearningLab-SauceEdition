// Cert Lab - Group Policy on the pretend computers: the default GPOs, each computer's last applied policy (computer
// part and, on CL1, the user part for everyone who signs in there), the background refresh (every 90 minutes plus a
// fixed offset of up to 30 on members, every 5 minutes on DCs), gpupdate, and what applying writes: registry values
// under HKLM\Software\Policies (and the PowerShell execution policy). The PDC emulator writes the password and lockout
// settings of GPOs linked to the domain onto the domain (when such a GPO changes, and every 16 hours).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public partial class WsLab
    {
        public readonly Dictionary<string, RsopResult> LastComputer = new Dictionary<string, RsopResult>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, RsopResult> LastUser = new Dictionary<string, RsopResult>(StringComparer.OrdinalIgnoreCase);   // "CL1|bcole"

        partial void SeedGpos(string dc)
        {
            var d = Dir(dc);
            var ddp = new WsAdObj { Guid = DdpGuid, Dn = "CN={" + DdpGuid.ToUpperInvariant() + "}," + PoliciesDn, Class = "groupPolicyContainer" };
            var ddcp = new WsAdObj { Guid = DdcpGuid, Dn = "CN={" + DdcpGuid.ToUpperInvariant() + "}," + PoliciesDn, Class = "groupPolicyContainer" };
            foreach (var x in new[] { ddp, ddcp })
            {
                x.Set("name", "{" + x.Guid.ToUpperInvariant() + "}").Set("displayName", x == ddp ? "Default Domain Policy" : "Default Domain Controllers Policy").Set("flags", "0").Set("versionUser", "0").Set("versionComputer", "3");
                x.SetList("gpPerm", new[] { "Authenticated Users|GpoApply", "Domain Admins|GpoEditDeleteModifySecurity", "Enterprise Admins|GpoEditDeleteModifySecurity", "SYSTEM|GpoEditDeleteModifySecurity", "ENTERPRISE DOMAIN CONTROLLERS|GpoRead" });
                x.Set("whenCreated", "2026-01-12 10:40");
                Touch(dc, x);
            }
            ddp.SetList("gpSetting", new[] { "C|PwdHistory|Enabled|24", "C|MaxPwdAge|Enabled|42", "C|MinPwdLength|Enabled|7", "C|PwdComplexity|Enabled|", "C|LockoutThreshold|Enabled|0" });
            ddcp.SetList("gpSetting", new[] { "C|AllowRdp|Enabled|" });
            Link(dc, ddp, d.ByDn(DomainDn), true, false, 0);
            Link(dc, ddcp, d.ByDn("OU=Domain Controllers," + DomainDn), true, false, 0);
            string err;
            var wb = NewGpo(dc, "Workstation baseline", "Settings every company PC gets", out err);
            SetSetting(dc, wb, "C", "LockScreenCamera", "Enabled", "");
            SetSetting(dc, wb, "C", "LogonTitle", "Enabled", Company);
            Link(dc, wb, d.ByDn("OU=Workstations," + DomainDn), true, false, 0);
            var sd = NewGpo(dc, "Staff desktop", "The desktop for staff accounts", out err);
            SetSetting(dc, sd, "U", "NoRegedit", "Enabled", "");
            SetSetting(dc, sd, "U", "Wallpaper", "Enabled", "\\\\FS1\\Public\\harborline.jpg");
            Link(dc, sd, d.ByDn("OU=Staff," + DomainDn), true, false, 0);
        }

        /// <summary>Who signs in to a computer (their user policy is refreshed with it): everyone on CL1, the administrator elsewhere.</summary>
        public IEnumerable<WsAdObj> UsersOn(WsDir d, string computer)
        {
            var admin = d.Find("Administrator", "user");
            if (admin != null) yield return admin;
            if (!WsText.Eq(computer, "CL1")) yield break;
            foreach (var row in People) { var u = d.Find(row.Split('|')[1], "user"); if (u != null) yield return u; }
        }

        /// <summary>A Group Policy refresh on a machine (gpupdate, the background timer, startup). Returns what it says.</summary>
        public string Refresh(string machine, string target)
        {
            var s = Server(machine);
            if (s == null || !s.Running) return "The computer isn't running.";
            string err;
            string dc = LocateDc(s.M, null, out err);
            if (dc == null) return "Computer policy could not be updated successfully: no domain controller could be reached.";
            var d = Dir(dc);
            var comp = d.Find(machine, "computer");
            if (comp == null) return "The computer account " + machine + " wasn't found on " + dc + ".";
            var r = Rsop(dc, comp, null, null);
            if (target != "user") { LastComputer[machine] = r; Apply(s.M, r); }
            if (target != "computer")
                foreach (var u in UsersOn(d, machine)) LastUser[machine + "|" + u.Sam] = Rsop(dc, comp, u, null);
            return null;
        }

        void Apply(PsMachine m, RsopResult r)
        {
            var root = m.HKLM.Sub("SOFTWARE\\Policies");
            foreach (var v in r.C.Values)
            {
                var def = v.Def;
                if (def == null || !def.IsRegistry || !def.Key.StartsWith("HKLM\\", StringComparison.OrdinalIgnoreCase)) continue;
                var k = m.HKLM.Sub(def.Key.Substring(5));
                string data = def.Data(v.State, v.Value);
                if (def.Type == "DWord") { int n; k.Set(def.ValueName, int.TryParse(data, out n) ? n : 0, "DWord"); }
                else k.Set(def.ValueName, data, "String");
            }
            RsopValue ps;
            m.ExecPolicyGpo = r.C.TryGetValue("PsExecution", out ps) && ps.State == "Enabled" ? ps.Def.Data("Enabled", ps.Value) : null;
            GC.KeepAlive(root);
        }

        /// <summary>The next background refresh of a machine.</summary>
        public void ScheduleRefresh(string machine)
        {
            var s = Server(machine);
            if (s == null) return;
            int mins = s.IsDc ? 5 : 90 + (Math.Abs(FwHash.Of(machine)) % 7) * 4;
            RsopResult last; LastComputer.TryGetValue(machine, out last);
            RsopValue ri;
            if (last != null && last.C.TryGetValue("RefreshInterval", out ri) && ri.State == "Enabled") { int v; if (int.TryParse(ri.Value, out v) && v > 0) mins = v; }
            At("gp:" + machine, Now.AddMinutes(mins), () => { Refresh(machine, null); ScheduleRefresh(machine); }, false);
        }

        partial void OnGpoChanged(string dc, WsAdObj g)
        {
            if (WsText.Eq(dc, Fsmo["PDCEmulator"])) ApplyDomainSecurity(dc);
        }
        partial void DirChanged(string dc) { if (WsText.Eq(dc, Fsmo["PDCEmulator"])) ApplyDomainSecurity(dc); }

        /// <summary>The PDC emulator writes the account policies of domain-linked GPOs onto the domain object.</summary>
        public void ApplyDomainSecurity(string pdc)
        {
            var d = Dir(pdc);
            var dom = d.ByDn(DomainDn);
            if (dom == null) return;
            var vals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in GpoOrder(d, "CN=x," + DomainDn, null, null))
                foreach (var kv in Settings(r.Gpo, "C"))
                {
                    var def = GpCatalog.Get(kv.Key);
                    if (def == null || def.Type != "sec") continue;
                    var p = kv.Value.Split(new[] { '|' }, 2);
                    vals[def.ValueName] = def.Data(p[0], p.Length > 1 ? p[1] : "");
                }
            bool changed = false;
            foreach (var kv in vals) if (dom.Get(kv.Key) != kv.Value) { dom.Set(kv.Key, kv.Value); changed = true; }
            if (changed) Touch(pdc, dom);
            At("gp:secrefresh", Now.AddHours(16), () => ApplyDomainSecurity(Fsmo["PDCEmulator"]), false);
        }
    }

    public partial class WsServer
    {
        partial void OnAfterBoot()
        {
            if (!Running) return;
            Lab.Refresh(Name, null);
            Lab.ScheduleRefresh(Name);
        }
    }
}
