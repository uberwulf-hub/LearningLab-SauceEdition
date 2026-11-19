// Cert Lab - hooks between the parts of the simulated PowerShell that were built separately: remoting uses the WinRM
// service and the firewall of the administration and networking parts, and JEA endpoints use the security part's
// role capabilities and the local groups.
using System;
using System.Linq;

namespace CertLab
{
    public partial class PsCmdlets
    {
        static partial void RegisterWiring()
        {
            // Enable-PSRemoting: WinRM starts now and with Windows, and the WinRM firewall rules are turned on.
            PsRemoteHook.OnEnable = m =>
            {
                var s = m.Svc("WinRM");
                if (s != null)
                {
                    if (s.StartType != "Automatic") m.SetStartType(s, "Automatic", true);
                    if (s.Status != "Running") m.StartService(s, true);
                }
                foreach (var r in m.FwRules.Where(r => r.Name.StartsWith("WINRM-HTTP-In-TCP", StringComparison.OrdinalIgnoreCase))) r.Enabled = true;
            };
            // A remote connection needs the WinRM service running on the target and its firewall letting port 5985 in.
            PsRemoteHook.PortOpen = (from, to) =>
            {
                var s = to.Svc("WinRM");
                if (s != null && s.Status != "Running") return false;
                return to.FirewallAllows("TCP", 5985, from.Ip);
            };
            PsRemoteHook.EndpointCommands = (m, name, user) => m.EndpointCommands(name, user);
            // The CIM-based modules are made of functions in 5.1 (Get-Command shows CommandType Function).
            var cdxml = new[] { "NetTCPIP", "NetAdapter", "NetSecurity", "NetConnection", "ScheduledTasks", "DnsClient" };
            foreach (var d in All.Values.Where(x => cdxml.Contains(x.Module, StringComparer.OrdinalIgnoreCase) && x.Name != "Resolve-DnsName")) d.IsFunction = true;
            // JEA role definitions name groups: a local group of the target machine ("Print Operators", "WIN-SRV1\Helpers").
            PsMachine.IsMemberHook = (m, user, group) =>
            {
                string g = group.Contains("\\") ? group.Substring(group.IndexOf('\\') + 1) : group;
                string dom = group.Contains("\\") ? group.Substring(0, group.IndexOf('\\')) : "";
                if (dom.Equals("LAB", StringComparison.OrdinalIgnoreCase))
                {
                    // the pretend domain's groups (PsMachine.DomainPrincipals) and who is in them
                    string bareUser = user.Contains("\\") ? user.Substring(user.IndexOf('\\') + 1) : user;
                    string[] members;
                    switch (g.ToLowerInvariant())
                    {
                        case "domain admins": members = new[] { "Administrator" }; break;
                        case "helpdesk": members = new[] { "jdoe", "asmith" }; break;
                        case "server admins": members = new[] { "Administrator", "bwilson" }; break;
                        case "web admins": members = new[] { "bwilson", "svc_web" }; break;
                        case "backup admins": members = new[] { "svc_backup" }; break;
                        default: members = new string[0]; break;
                    }
                    return members.Contains(bareUser, StringComparer.OrdinalIgnoreCase) && (!user.Contains("\\") || user.StartsWith("LAB\\", StringComparison.OrdinalIgnoreCase));
                }
                if (dom.Length > 0 && !dom.Equals(m.Name, StringComparison.OrdinalIgnoreCase) && !dom.Equals("BUILTIN", StringComparison.OrdinalIgnoreCase)) return false;
                var lg = m.FindGroup(g);
                if (lg == null) return false;
                string u = user.Contains("\\") ? user.Substring(user.IndexOf('\\') + 1) : user;
                return lg.Members.Any(x => x.Name.Equals(user, StringComparison.OrdinalIgnoreCase) || x.Name.EndsWith("\\" + u, StringComparison.OrdinalIgnoreCase) && (!user.Contains("\\") || x.Name.Equals(user, StringComparison.OrdinalIgnoreCase)));
            };
        }
    }
}
