// Cert Lab - programs on the MD-102 course's pretend devices: dsregcmd /status (the device's Microsoft Entra state),
// gpresult /r (Group Policy, for domain joined PCs) and mdmdiagnosticstool (collects MDM diagnostics into a folder or
// a .cab file). Their text follows the layout of the real tools, shortened.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public static partial class PsNatives
    {
        static string Box(string title) { return "+----------------------------------------------------------------------+\n| " + title.PadRight(69) + "|\n+----------------------------------------------------------------------+\n"; }
        static string Row(string k, string v) { return k.PadLeft(26) + " : " + v + "\n"; }

        static partial void RegisterEndpointNatives()
        {
            Add("dsregcmd", null, c =>
            {
                var a = c.Args.Select(x => x.ToLowerInvariant()).ToList();
                if (!a.Contains("/status")) { c.L("DSREGCMD switches\n                        /? : Displays the help message for DSREGCMD\n                   /status : Displays the device join status\n                    /leave : Performs Hybrid Unjoin\n                    /debug : Displays debug messages\n\nCert Lab simulates /status only."); return a.Count == 0 ? 0 : 1; }
                var d = c.M.EpDev as EpDevice;
                bool entra = d != null && d.EntraJoined, hybrid = d != null && d.Is("join", "Hybrid joined"), reg = d != null && d.Is("join", "Entra registered");
                bool domain = hybrid || d == null && c.M.Domain != "WORKGROUP";
                var sb = new StringBuilder("\n");
                sb.Append(Box("Device State")).Append('\n');
                sb.Append(Row("AzureAdJoined", entra ? "YES" : "NO")).Append(Row("EnterpriseJoined", "NO")).Append(Row("DomainJoined", domain ? "YES" : "NO"));
                if (domain) sb.Append(Row("DomainName", c.M.DomainShort));
                sb.Append(Row("Device Name", c.M.Name + (domain ? "." + c.M.Domain : ""))).Append('\n');
                if (entra)
                {
                    sb.Append(Box("Device Details")).Append('\n');
                    sb.Append(Row("DeviceId", FwHash.Of(d.Id).ToString("x8", CultureInfo.InvariantCulture) + "-5d1e-4c7b-9a2f-" + Math.Abs(FwHash.Of(d.Name)).ToString("x12", CultureInfo.InvariantCulture).PadLeft(12, '0').Substring(0, 12)));
                    sb.Append(Row("KeyProvider", "Microsoft Platform Crypto Provider")).Append(Row("TpmProtected", d.Is("tpm", "None") ? "NO" : "YES")).Append(Row("DeviceAuthStatus", "SUCCESS")).Append('\n');
                    sb.Append(Box("Tenant Details")).Append('\n');
                    sb.Append(Row("TenantName", d.T.TenantName)).Append(Row("TenantId", d.T.TenantId)).Append(Row("Idp", "login.windows.net"));
                    if (d.Enrolled || d.T.Settings["mdmScope"] != "None")
                    {
                        sb.Append(Row("MdmUrl", "https://enrollment.manage.microsoft.com/enrollmentserver/discovery.svc"));
                        sb.Append(Row("MdmTouUrl", "https://portal.manage.microsoft.com/TermsofUse.aspx"));
                        sb.Append(Row("MdmComplianceUrl", "https://portal.manage.microsoft.com/?portalAction=Compliance"));
                    }
                    else sb.Append(Row("MdmUrl", "")).Append(Row("MdmTouUrl", "")).Append(Row("MdmComplianceUrl", ""));
                    sb.Append('\n');
                }
                sb.Append(Box("User State")).Append('\n');
                sb.Append(Row("NgcSet", "NO")).Append(Row("WorkplaceJoined", reg ? "YES" : "NO"));
                if (reg) sb.Append(Row("WorkplaceTenantName", d.T.TenantName));
                sb.Append(Row("WamDefaultSet", entra ? "YES" : "NO")).Append('\n');
                sb.Append(Box("SSO State")).Append('\n');
                sb.Append(Row("AzureAdPrt", entra ? "YES" : "NO"));
                if (entra) sb.Append(Row("AzureAdPrtAuthority", "https://login.microsoftonline.com/" + d.T.TenantId));
                sb.Append(Row("EnterprisePrt", hybrid ? "YES" : "NO"));
                c.L(sb.ToString());
                return 0;
            });
            Add("gpresult", null, c =>
            {
                var a = c.Args.Select(x => x.ToLowerInvariant()).ToList();
                if (!a.Contains("/r")) { c.L("Cert Lab simulates gpresult /r only."); return 1; }
                var d = c.M.EpDev as EpDevice;
                bool domain = d != null ? d.Is("join", "Hybrid joined") : c.M.Domain != "WORKGROUP";
                if (!domain) { c.L("\nINFO: The user \"" + c.M.Name + "\\administrator\" does not have RSOP data.\n\nThis PC isn't joined to an Active Directory domain, so no Group Policy applies: on a Microsoft Entra joined device, settings come from Intune."); return 1; }
                var sb = new StringBuilder();
                sb.Append("\nMicrosoft (R) Windows (R) Operating System Group Policy Result tool v2.0\n\nCOMPUTER SETTINGS\n------------------\n");
                sb.Append("    CN=" + c.M.Name + ",OU=Desktops,DC=" + c.M.Domain.Replace(".", ",DC=") + "\n    Last time Group Policy was applied: " + c.M.Now.AddHours(-2).ToString("M/d/yyyy 'at' h:mm:ss tt", CultureInfo.InvariantCulture) + "\n");
                sb.Append("    Group Policy was applied from:      DC01." + c.M.Domain + "\n    Domain Name:                         " + c.M.DomainShort + "\n    Domain Type:                         Windows 2008 or later\n\n");
                sb.Append("    Applied Group Policy Objects\n    -----------------------------\n        Intune automatic enrollment\n        Default Domain Policy\n\n");
                sb.Append("    The computer is a part of the following security groups\n    -------------------------------------------------------\n        BUILTIN\\Administrators\n        Everyone\n        Domain Computers\n");
                c.L(sb.ToString());
                return 0;
            });
            Add("mdmdiagnosticstool", "C:\\Windows\\System32\\MdmDiagnosticsTool.exe", c =>
            {
                var a = c.Args;
                int iCab = a.FindIndex(x => x.Equals("-cab", StringComparison.OrdinalIgnoreCase)), iOut = a.FindIndex(x => x.Equals("-out", StringComparison.OrdinalIgnoreCase)), iZip = a.FindIndex(x => x.Equals("-zip", StringComparison.OrdinalIgnoreCase));
                int iArea = a.FindIndex(x => x.Equals("-area", StringComparison.OrdinalIgnoreCase));
                if (iCab < 0 && iOut < 0 && iZip < 0) { c.L("Usage:\n  MdmDiagnosticsTool.exe -out <folder>\n  MdmDiagnosticsTool.exe -area <area;area...> -cab <file.cab>\n  MdmDiagnosticsTool.exe -area <area;area...> -zip <file.zip>\nAreas: Autopilot, DeviceEnrollment, DeviceProvisioning, TPM, ManagementService, DeviceHealthMonitoring"); return 1; }
                var d = c.M.EpDev as EpDevice;
                var report = new StringBuilder("MDM diagnostic report (Cert Lab)\r\nDevice: " + c.M.Name + "\r\nEnrolled: " + (d != null && d.Enrolled ? "yes, " + d.T.TenantName : "no") + "\r\n");
                if (d != null) { report.Append("Join type: " + d.JoinType + "\r\nPolicies:\r\n"); foreach (var p in d.Policies) report.Append("  " + p.Kind + " " + p.Policy + ": " + p.Status + "\r\n"); foreach (var s in d.Applied.Values) report.Append("  setting " + s.Key + " = " + s.Value + " (" + s.From + ")\r\n"); }
                if (iArea >= 0 && iArea + 1 < a.Count) report.Append("Areas: " + a[iArea + 1] + "\r\n");
                int idx = iCab >= 0 ? iCab : iZip >= 0 ? iZip : iOut;
                if (idx + 1 >= a.Count) { c.L("A path is missing after " + a[idx] + "."); return 1; }
                string target = c.R.FullFsPath(a[idx + 1], c.R.CurrentFsDir);
                if (iOut >= 0) { c.M.MakeDir(target, c.M.Now); c.M.File(target + "\\MDMDiagReport.html", "<html><body><pre>" + report + "</pre></body></html>", c.M.Now); c.M.File(target + "\\MDMDiagReport.xml", report.ToString(), c.M.Now); }
                else { int s = target.LastIndexOf('\\'); c.M.MakeDir(target.Substring(0, s), c.M.Now); c.M.File(target, report.ToString(), c.M.Now).FakeSize = 3458112; }
                return 0;
            });
        }
    }
}
