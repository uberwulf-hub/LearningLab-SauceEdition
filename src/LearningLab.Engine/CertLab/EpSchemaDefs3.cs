// Cert Lab - the kinds of the practice portal, part 3: endpoint security and Conditional Access.
using System;
using System.Collections.Generic;

namespace CertLab
{
    public static partial class EpSchemas
    {
        static void InitSecurity()
        {
            K("av", "Antivirus policy", "Antivirus", "Endpoint security", "Endpoint security > Antivirus", "", true, new[] { "rtp", "cloud", "pua" },
                "rtp|Allow Realtime Monitoring|choice:Not configured,Allowed,Not allowed|sec=Defender",
                "cloud|Allow Cloud Protection|choice:Not configured,Allowed,Not allowed|sec=Defender",
                "cloudLevel|Cloud Block Level|choice:Not configured,High,High plus,Zero tolerance|sec=Defender",
                "pua|PUA Protection|choice:Not configured,PUA Protection off,PUA Protection on,Audit mode|sec=Defender",
                "sigHours|Signature Update Interval (hours)|number|min=0|max=24|sec=Defender",
                "scanDay|Schedule Scan Day|choice:Not configured,Every day,Sunday,Monday,Tuesday,Wednesday,Thursday,Friday,Saturday|sec=Defender",
                "exclusions|Excluded Paths|longtext|sec=Exclusions");
            K("wsx", "Windows Security experience", "Windows Security experience", "Endpoint security", "Endpoint security > Antivirus > Windows Security experience", "", true, new[] { "tamper" },
                "tamper|TamperProtection (Device)|choice:Not configured,On,Off|sec=Defender",
                "hideVirus|Hide the Virus and threat protection area|choice:Not configured,Hide,Show|sec=Windows Security");
            K("diskEncryption", "Disk encryption policy (BitLocker)", "Disk encryption", "Endpoint security", "Endpoint security > Disk encryption", "", true, new[] { "requireEncryption", "osMethod" },
                "requireEncryption|Require Device Encryption|choice:Not configured,Enabled,Disabled|sec=BitLocker",
                "allowWarning|Allow Warning For Other Disk Encryption|choice:Not configured,Enabled,Disabled|sec=BitLocker|help=Disabled turns on silent encryption: no prompts for the user.",
                "standardUser|Allow Standard User Encryption|choice:Not configured,Enabled,Disabled|sec=BitLocker|if=allowWarning=Disabled",
                "osMethod|Encryption method for operating system drives|choice:XTS-AES 128-bit,XTS-AES 256-bit,AES-CBC 128-bit,AES-CBC 256-bit|sec=Operating System Drives",
                "startupAuth|Require additional authentication at startup|choice:Not configured,Enabled,Disabled|sec=Operating System Drives",
                "tpmPin|Configure TPM startup PIN|choice:Allow startup PIN with TPM,Require startup PIN with TPM,Do not allow startup PIN with TPM|sec=Operating System Drives|if=startupAuth=Enabled",
                "backup|Back up the recovery information to Microsoft Entra ID|choice:Not configured,Enabled|sec=Operating System Drives",
                "backupFirst|Do not enable BitLocker until recovery information is stored|choice:Not configured,Enabled|sec=Operating System Drives",
                "rotation|Configure Recovery Password Rotation|choice:Not configured,Refresh off,Refresh on for Microsoft Entra joined devices,Refresh on for Microsoft Entra joined and hybrid joined devices|sec=BitLocker");
            K("firewall", "Firewall policy", "Firewall", "Endpoint security", "Endpoint security > Firewall", "", true, new[] { "domainOn", "privateOn", "publicOn" },
                "domainOn|Enable Domain Network Firewall|choice:Not configured,True,False|sec=Domain profile",
                "domainIn|Default Inbound Action for Domain Profile|choice:Not configured,Block,Allow|sec=Domain profile",
                "privateOn|Enable Private Network Firewall|choice:Not configured,True,False|sec=Private profile",
                "privateIn|Default Inbound Action for Private Profile|choice:Not configured,Block,Allow|sec=Private profile",
                "publicOn|Enable Public Network Firewall|choice:Not configured,True,False|sec=Public profile",
                "publicIn|Default Inbound Action for Public Profile|choice:Not configured,Block,Allow|sec=Public profile");
            K("firewallRules", "Firewall rules policy", "Firewall rules", "Endpoint security", "Endpoint security > Firewall > Firewall rules", "", true, new[] { "rules" },
                "rules|Rules (one per line: name, In or Out, Allow or Block, TCP / UDP / Any, ports)|longtext|req|help=For example: Expenses app, In, Allow, TCP, 8443");
            K("asr", "Attack surface reduction rules", "Attack surface reduction", "Endpoint security", "Endpoint security > Attack surface reduction", "", true, new[] { "description" },
                "description|Description|text",
                "cfa|Enable Controlled Folder Access|choice:Not configured,Enabled,Audit Mode,Disabled|sec=Defender",
                "asrExclusions|Attack Surface Reduction Only Exclusions|longtext|sec=Defender");
            foreach (var r in EpCatalog.AsrRules)
                Kinds["asr"].Fields.Add(new EpField { Key = "asr:" + r.Key, Label = r.Name, Type = "choice", Options = EpCatalog.AsrModes, Default = "Not configured", Section = "Attack Surface Reduction Rules" });
            K("laps", "Local admin password solution (Windows LAPS)", "Account protection: LAPS", "Endpoint security", "Endpoint security > Account protection", "", true, new[] { "backup", "ageDays" },
                "backup|Backup Directory|choice:Disabled,Backup the password to Microsoft Entra ID only,Backup the password to Active Directory only|req|sec=LAPS",
                "ageDays|Password Age Days|number|min=1|max=365|def=30|sec=LAPS|help=With Microsoft Entra ID the smallest value is 7.",
                "adminName|Administrator Account Name|text|sec=LAPS|help=Empty: the built-in Administrator account.",
                "complexity|Password Complexity|choice:Large letters + small letters + numbers + special characters,Large letters,Large letters + small letters,Large letters + small letters + numbers|sec=LAPS",
                "length|Password Length|number|min=8|max=64|def=14|sec=LAPS",
                "postAuth|Post Authentication Actions|choice:Reset the password and logoff the managed account,Reset password,Reset the password and reboot the device,Disabled|sec=LAPS",
                "postDelay|Post Authentication Reset Delay (hours)|number|min=0|max=24|def=24|sec=LAPS");
            K("edr", "Endpoint detection and response policy", "Endpoint detection and response", "Endpoint security", "Endpoint security > Endpoint detection and response", "", true, new[] { "package" },
                "package|Microsoft Defender for Endpoint client configuration package type|choice:Auto from connector,Onboard,Offboard|req",
                "sampleSharing|Sample Sharing|choice:All,None");
            K("baseline", "Security baseline profile", "Security baselines", "Endpoint security", "Endpoint security > Security baselines", "", true, new[] { "baseline" },
                "baseline|Baseline|choice:Security Baseline for Windows 10 and later,Microsoft Defender for Endpoint Baseline,Microsoft Edge Baseline,Windows 365 Security Baseline|req",
                "description|Description|text");
        }

        static void InitAccess()
        {
            K("ca", "Conditional Access policy", "Conditional Access policies", "Conditional Access", "Conditional Access > Policies", "", false, new[] { "state", "grant" },
                "usersInclude|Users: include|people|req|sec=Assignments|help=All users, or users and groups, one per line.",
                "usersExclude|Users: exclude|people|sec=Assignments|help=Keep an emergency access account out of every policy.",
                "apps|Target resources (cloud apps)|cloudapps|req|sec=Assignments",
                "platforms|Conditions: device platforms (include)|multi:Any device,Windows,iOS,Android,macOS,Linux|sec=Conditions|help=Empty: the policy applies on every platform.",
                "platformsExclude|Conditions: device platforms (exclude)|multi:Windows,iOS,Android,macOS,Linux|sec=Conditions",
                "locations|Conditions: locations (include)|choice:Any location,All trusted locations|sec=Conditions",
                "locationsExclude|Conditions: locations (exclude)|choice:,All trusted locations|sec=Conditions",
                "clientApps|Conditions: client apps|multi:Browser,Mobile apps and desktop clients,Exchange ActiveSync clients,Other clients|sec=Conditions|help=Empty: every client app.",
                "grant|Grant|choice:Grant access,Block access|req|sec=Access controls",
                "controls|Controls|multi:Require multifactor authentication,Require device to be marked as compliant,Require Microsoft Entra hybrid joined device,Require app protection policy|sec=Access controls|if=grant=Grant access",
                "controlsOp|For multiple controls|choice:Require all the selected controls,Require one of the selected controls|sec=Access controls|if=grant=Grant access",
                "state|Enable policy|choice:Report-only,On,Off|req|def=Report-only|sec=Enable policy");
        }

        /// <summary>The cloud apps Conditional Access can target in the pretend tenant.</summary>
        public static readonly string[] CloudApps = { "Office 365", "Office 365 Exchange Online", "Office 365 SharePoint Online", "Microsoft Teams Services", "Microsoft Intune", "Microsoft Intune Enrollment", "Microsoft Azure Management", "Riverbend Expenses (web app)" };
    }
}
