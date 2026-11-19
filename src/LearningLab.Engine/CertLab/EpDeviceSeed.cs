// Cert Lab - what a pretend Windows device of the MD-102 course has on its own disk: the services that talk to Intune
// (Intune Management Extension, the push service) and Defender for Endpoint (Sense), the Intune Management
// Extension's log, the enrolment keys, and the serial number. The device's PowerShell commands (dsregcmd,
// Get-BitLockerVolume, Get-MpComputerStatus...) read the tenant's view of the device through PsMachine.Endpoint.
using System;
using System.Collections.Generic;
using System.Linq;

namespace CertLab
{
    public partial class PsMachine
    {
        /// <summary>The MD-102 course: the tenant's device behind this PC (an EpDevice), or null.</summary>
        public object EpDev;
    }

    public static class EpDeviceSeed
    {
        public const string ImeLog = "C:\\ProgramData\\Microsoft\\IntuneManagementExtension\\Logs\\IntuneManagementExtension.log";

        public static void Prepare(PsMachine m, EpDevice d)
        {
            m.SerialNumber = d["serial"];
            m.BiosSerial = d["serial"];
            Svc(m, "IntuneManagementExtension", "Microsoft Intune Management Extension", "Microsoft.Management.Services.IntuneWindowsAgent", d.Enrolled, "Runs Win32 apps, PowerShell scripts and remediations that Intune sends.");
            Svc(m, "dmwappushservice", "Device Management Wireless Application Protocol (WAP) Push message Routing Service", "svchost", true, "Routes push messages that tell the device to check in with its MDM service.");
            Svc(m, "Sense", "Windows Defender Advanced Threat Protection Service", "MsSense", d.Is("mde", "Yes"), "Microsoft Defender for Endpoint's sensor.");
            m.Dir("C:\\ProgramData\\Microsoft\\IntuneManagementExtension\\Logs");
            m.File(ImeLog, string.Join("\r\n", d.Log.ToArray()), m.Now);
            if (d.Enrolled) m.HKLM.Sub("SOFTWARE\\Microsoft\\Enrollments\\" + d.Id).S("ProviderID", "MS DM Server").S("UPN", d.PrimaryUser.Length == 0 ? "" : d.PrimaryUser + "@riverbend.example");
            m.Dir("C:\\HWID");
            if (d.PrimaryUser.Length > 0)
            {
                var home = "C:\\Users\\" + d.PrimaryUser;
                m.MakeDir(home + "\\Documents", m.Now);
                m.MakeDir(home + "\\Desktop", m.Now);
            }
            if (!d.Yes("localPassword")) { var u = m.FindUser("LabUser"); if (u != null) u.HasPassword = false; }
            var a = m.FindUser("Administrator"); if (a != null) a.Enabled = false;   // the built-in account starts disabled on Windows clients
        }

        static void Svc(PsMachine m, string name, string display, string exe, bool on, string desc)
        {
            var s = m.Svc(name);
            if (s == null) { s = new WinService { Name = name, DisplayName = display, Exe = exe, Description = desc, BinaryPath = "C:\\Program Files\\" + exe + ".exe" }; m.Services.Add(s); }
            s.StartType = on ? "Automatic" : "Manual";
            s.Status = on ? "Running" : "Stopped";
        }

        /// <summary>Adds a line to the Intune Management Extension's log on the device.</summary>
        public static void AppendLog(PsMachine m, string line)
        {
            var f = m.FindFile(ImeLog) ?? m.File(ImeLog, "", m.Now);
            f.Text = (f.Text.Length > 0 ? f.Text + "\r\n" : "") + line;
            f.Modified = m.Now;
        }
    }
}
