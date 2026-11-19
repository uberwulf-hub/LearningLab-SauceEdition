// Cert Lab - checks of Group Policy processing in the AZ-800 network.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CertLab
{
    public static partial class Tests
    {
        static void WsGpoTests()
        {
            var w = new W();
            const string rs = "rsop(computer=CL1, user=bcole)";
            w.Yes("applied(CL1).setting[\"LockScreenCamera\"] == Enabled");
            w.Yes(rs + ".setting[\"NoRegedit\"] == Enabled");
            w.Yes(rs + ".setting[\"Wallpaper\"].value == \"\\\\FS1\\Public\\harborline.jpg\"");
            w.Yes(rs + ".applied has \"Default Domain Policy\"");
            Check("applying wrote the policy value into CL1's registry", w.Ps("CL1", "(Get-ItemProperty HKLM:\\Software\\Policies\\Microsoft\\Windows\\Personalization).NoLockScreenCamera").Trim() == "1", w.Ps("CL1", "Get-ItemProperty HKLM:\\Software\\Policies\\Microsoft\\Windows\\Personalization"));
            // a GPO linked to the Sales OU
            w.Do("gpo new \"Sales lockdown\"");
            w.Do("gpo set \"Sales lockdown\" setting=NoControlPanel state=Enabled");
            w.Bad("gpo set \"Sales lockdown\" setting=Loopback state=Enabled value=Sometimes", "Merge");
            w.Do("gpo link \"Sales lockdown\" Staff/Sales");
            w.Yes(rs + ".setting[\"NoControlPanel\"] == Enabled");
            w.Yes("rsop(computer=CL1, user=ewalsh).setting[\"NoControlPanel\"] == \"Not configured\"");
            w.No("applied(CL1, user=bcole).setting[\"NoControlPanel\"] == Enabled");
            w.Do("gpupdate CL1");
            w.Yes("applied(CL1, user=bcole).setting[\"NoControlPanel\"] == Enabled");
            // conflicts: the OU closer to the user wins; within one OU, link order 1 wins
            w.Do("gpo new \"Sales wallpaper\"");
            w.Do("gpo set \"Sales wallpaper\" setting=Wallpaper state=Enabled value=sales.jpg");
            w.Do("gpo link \"Sales wallpaper\" Staff/Sales");
            w.Yes(rs + ".setting[\"Wallpaper\"].value == sales.jpg");
            w.Do("gpo new \"Staff wallpaper 2\"");
            w.Do("gpo set \"Staff wallpaper 2\" setting=Wallpaper state=Enabled value=two.jpg");
            w.Do("gpo link \"Staff wallpaper 2\" Staff");
            w.Yes("rsop(computer=CL1, user=ewalsh).setting[\"Wallpaper\"].value == \"\\\\FS1\\Public\\harborline.jpg\"");
            w.Do("gpo setlink \"Staff wallpaper 2\" Staff order=1");
            w.Yes("rsop(computer=CL1, user=ewalsh).setting[\"Wallpaper\"].value == two.jpg");
            w.Yes("gpo[\"Staff wallpaper 2\"].linkorder[\"Staff\"] == 1");
            // Block Inheritance and Enforced
            w.Do("inheritance block Staff/Sales");
            w.Yes(rs + ".setting[\"NoRegedit\"] == \"Not configured\"");
            w.Yes(rs + ".reason[\"Staff desktop\"] == \"Blocked (Inheritance)\"");
            w.Do("gpo setlink \"Staff wallpaper 2\" Staff enforced=yes");
            w.Yes(rs + ".setting[\"Wallpaper\"].value == two.jpg");
            w.Yes(rs + ".setting[\"Wallpaper\"].winner == \"Staff wallpaper 2\"");
            w.Do("inheritance unblock Staff/Sales");
            w.Yes(rs + ".setting[\"NoRegedit\"] == Enabled");
            // security filtering (and MS16-072: the computer must still read the GPO)
            w.Do("gpo filter \"Sales lockdown\" remove \"Authenticated Users\"");
            w.Do("gpo filter \"Sales lockdown\" add \"Sales Managers\"");
            w.Yes("rsop(computer=CL1, user=imoreau).reason[\"Sales lockdown\"] contains \"can't read\"");
            w.Do("gpo perm \"Sales lockdown\" \"Domain Computers\" GpoRead");
            w.Yes("rsop(computer=CL1, user=imoreau).setting[\"NoControlPanel\"] == Enabled");
            w.Yes(rs + ".setting[\"NoControlPanel\"] == \"Not configured\"");
            w.Yes(rs + ".reason[\"Sales lockdown\"] == \"Denied (Security)\"");
            // WMI filters (evaluated on the computer)
            w.Do("wmi new name=\"Windows 11\" query=\"SELECT * FROM Win32_OperatingSystem WHERE Version LIKE '10.0.22%' AND ProductType = '1'\"");
            w.Do("wmi new name=\"Servers only\" query=\"SELECT * FROM Win32_OperatingSystem WHERE ProductType = '3'\"");
            w.Bad("wmi new name=Broken query=\"SELECT FROM\"", "SELECT");
            w.Do("gpo wmi \"Sales wallpaper\" \"Servers only\"");
            w.Yes(rs + ".reason[\"Sales wallpaper\"] == \"Denied (WMI Filter)\"");
            w.Do("gpo wmi \"Sales wallpaper\" \"Windows 11\"");
            w.Yes(rs + ".usergpos has \"Sales wallpaper\"");
            // loopback: user settings from the computer's OU
            w.Do("gpo new \"Kiosk users\"");
            w.Do("gpo set \"Kiosk users\" setting=NoCmd state=Enabled");
            w.Do("gpo set \"Kiosk users\" setting=Loopback state=Enabled value=Replace");
            w.Do("gpo link \"Kiosk users\" Workstations");
            w.Yes(rs + ".loopback == Replace");
            w.Yes(rs + ".setting[\"NoCmd\"] == Enabled");
            w.Yes(rs + ".setting[\"NoRegedit\"] == \"Not configured\"");
            w.Do("gpo set \"Kiosk users\" setting=Loopback state=Enabled value=Merge");
            w.Yes(rs + ".setting[\"NoCmd\"] == Enabled");
            w.Yes(rs + ".setting[\"NoRegedit\"] == Enabled");
            w.Yes("rsop(computer=FS1, user=bcole).setting[\"NoCmd\"] == \"Not configured\"");
            // disabled links and GPO status
            w.Do("gpo setlink \"Kiosk users\" Workstations enabled=no");
            w.Yes(rs + ".reason[\"Kiosk users\"] == \"Disabled (Link)\"");
            w.Do("gpo status \"Staff desktop\" UserSettingsDisabled");
            w.Yes(rs + ".reason[\"Staff desktop\"] == \"Disabled (GPO)\"");
            // the domain's password policy comes from GPOs linked to the domain (written by the PDC emulator)
            w.Do("gpo set \"Default Domain Policy\" setting=MinPwdLength state=Enabled value=12");
            w.Yes("domain.minPwdLength == 12");
            w.Bad("new user name=\"Short Pw\" sam=shortpw path=Staff/HR password=\"Ab1#defg\"", "12 characters");
            w.Do("gpo new \"OU password policy\"");
            w.Do("gpo set \"OU password policy\" setting=MinPwdLength state=Enabled value=4");
            w.Do("gpo link \"OU password policy\" Staff/HR");
            w.Yes("domain.minPwdLength == 12");
            // backups
            w.Do("gpo backup \"Sales lockdown\"");
            w.Do("gpo delete \"Sales lockdown\"");
            w.No("gpo[\"Sales lockdown\"]");
            w.Do("gpo restore \"Sales lockdown\"");
            w.Yes("gpo[\"Sales lockdown\"].setting[\"NoControlPanel\"] == Enabled");
            w.Yes("gpo[\"Sales lockdown\"].links !has \"OU=Sales,OU=Staff,DC=corp,DC=example\"");
            // the central store
            w.No("centralstore(DC1)");
            w.Do("centralstore create");
            w.Yes("centralstore(DC1) == Yes");
            w.Wait(16 * 60);
            w.Yes("centralstore(DC2) == Yes");
            w.Dispose();
        }
    }
}
