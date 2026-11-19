// Cert Lab - the kinds of the practice portal, part 2: compliance, Windows updates, scripts, apps, app protection and
// configuration, endpoint security (antivirus, disk encryption, firewall, attack surface reduction, LAPS, EDR,
// baselines) and Conditional Access.
using System;
using System.Collections.Generic;

namespace CertLab
{
    public static partial class EpSchemas
    {
        static void InitUpdates()
        {
            const string W = "if=platform=" + Win, I = "if=platform=" + Ios, A = "if=platform=" + Android;
            K("compliance", "Compliance policy", "Compliance policies", "Devices", "Devices > Compliance", "platform", true, new[] { "platform", "description" },
                "platform|Platform|choice:" + Win + "," + Ios + "," + Android + "|req",
                "description|Description|text",
                "bitlocker|Require BitLocker|choice:Not configured,Require|sec=Device Health|" + W,
                "secureBoot|Require Secure Boot to be enabled on the device|choice:Not configured,Require|sec=Device Health|" + W,
                "codeIntegrity|Require code integrity|choice:Not configured,Require|sec=Device Health|" + W,
                "minOs|Minimum OS version|text|sec=Device Properties|help=For example 10.0.22631 (Windows) or 17.0 (iOS).",
                "maxOs|Maximum OS version|text|sec=Device Properties",
                "password|Require a password to unlock the device|choice:Not configured,Require|sec=System Security",
                "passwordMin|Minimum password length|number|min=4|max=16|sec=System Security",
                "firewall|Firewall|choice:Not configured,Require|sec=System Security|" + W,
                "tpm|Trusted Platform Module (TPM)|choice:Not configured,Require|sec=System Security|" + W,
                "antivirus|Antivirus|choice:Not configured,Require|sec=System Security|" + W,
                "antispyware|Antispyware|choice:Not configured,Require|sec=System Security|" + W,
                "defender|Microsoft Defender Antimalware|choice:Not configured,Require|sec=Defender|" + W,
                "rtp|Real-time protection|choice:Not configured,Require|sec=Defender|" + W,
                "sigUpToDate|Microsoft Defender Antimalware security intelligence up-to-date|choice:Not configured,Require|sec=Defender|" + W,
                "mdeRisk|Require the device to be at or under the machine risk score|choice:Not configured,Clear,Low,Medium,High|sec=Microsoft Defender for Endpoint|" + W,
                "jailbreak|Jailbroken devices|choice:Not configured,Block|sec=Device Health|" + I,
                "rooted|Rooted devices|choice:Not configured,Block|sec=Device Health|" + A,
                "playIntegrity|Play Integrity verdict|choice:Not configured,Check basic integrity,Check basic integrity & device integrity|sec=Device Health|" + A,
                "noncompliantDays|Mark device noncompliant (days after noncompliance)|number|min=0|max=365|def=0|sec=Actions for noncompliance",
                "emailDays|Send email to end user (days after noncompliance, empty = no email)|number|min=0|max=365|sec=Actions for noncompliance",
                "retireDays|Add device to retire list (days, empty = never)|number|min=0|max=365|sec=Actions for noncompliance");
            K("ring", "Update ring", "Update rings for Windows 10 and later", "Devices", "Devices > Windows updates > Update rings", "", true, new[] { "qualityDeferral", "featureDeferral", "behavior" },
                "servicing|Servicing channel|choice:General Availability channel|sec=Update ring settings",
                "msUpdates|Microsoft product updates|choice:Allow,Block|sec=Update ring settings",
                "drivers|Windows drivers|choice:Allow,Block|sec=Update ring settings",
                "qualityDeferral|Quality update deferral period (days)|number|req|min=0|max=30|def=0|sec=Update ring settings",
                "featureDeferral|Feature update deferral period (days)|number|req|min=0|max=365|def=0|sec=Update ring settings",
                "upgradeWin11|Upgrade Windows 10 devices to Latest Windows 11 release|choice:No,Yes|sec=Update ring settings",
                "uninstallDays|Set feature update uninstall period (2 - 60 days)|number|min=2|max=60|def=10|sec=Update ring settings",
                "behavior|Automatic update behavior|choice:Auto install at maintenance time,Auto install and restart at maintenance time,Auto install and restart at a scheduled time,Auto install and restart without end-user control,Reset to default|sec=User experience settings",
                "activeStart|Active hours start (hour)|number|min=0|max=23|def=8|sec=User experience settings",
                "activeEnd|Active hours end (hour)|number|min=0|max=23|def=17|sec=User experience settings",
                "featureDeadline|Deadline for feature updates (days)|number|min=0|max=30|sec=User experience settings",
                "qualityDeadline|Deadline for quality updates (days)|number|min=0|max=30|sec=User experience settings",
                "grace|Grace period (days)|number|min=0|max=7|sec=User experience settings",
                "pausedQuality|Quality updates paused|choice:No,Yes|sec=Status",
                "pausedFeature|Feature updates paused|choice:No,Yes|sec=Status");
            K("featureUpdate", "Feature updates policy", "Feature updates for Windows 10 and later", "Devices", "Devices > Windows updates > Feature updates", "", true, new[] { "version", "rollout" },
                "version|Feature update to deploy|choice:Windows 11&#44; version 24H2,Windows 11&#44; version 23H2,Windows 10&#44; version 22H2|req",
                "rollout|Rollout options|choice:Make update available as soon as possible,Make update available on a specific date,Make update available gradually");
            K("qualityUpdate", "Quality updates policy (expedite)", "Quality updates for Windows 10 and later", "Devices", "Devices > Windows updates > Quality updates", "", true, new[] { "expediteBelow", "restartDays" },
                "expediteBelow|Expedite installation of quality updates if device OS version is less than|choice:2026-03 cumulative update,2026-02 cumulative update|req",
                "restartDays|Number of days to wait before restart is enforced|number|min=0|max=2|def=1");
            K("driverUpdate", "Driver updates policy", "Driver updates for Windows 10 and later", "Devices", "Devices > Windows updates > Driver updates", "", true, new[] { "approval" },
                "approval|Approval method|choice:Manually approve and deploy driver updates,Automatically approve all recommended driver updates|req",
                "deferDays|Make updates available after (days)|number|min=0|max=30|def=0");
            K("script", "Platform script", "Platform scripts", "Devices", "Devices > Scripts and remediations > Platform scripts", "", true, new[] { "runAsUser", "run64" },
                "description|Description|text",
                "content|Script (PowerShell)|script|req|sec=Script settings",
                "runAsUser|Run this script using the logged on credentials|choice:No,Yes|sec=Script settings",
                "enforceSig|Enforce script signature check|choice:No,Yes|sec=Script settings",
                "run64|Run script in 64 bit PowerShell Host|choice:No,Yes|sec=Script settings");
            K("remediation", "Remediation", "Remediations", "Devices", "Devices > Scripts and remediations > Remediations", "", true, new[] { "schedule" },
                "detect|Detection script|script|req|sec=Settings|help=Exit 1 means \"needs fixing\", exit 0 means fine.",
                "remediate|Remediation script|script|sec=Settings",
                "runAsUser|Run this script using the logged-on credentials|choice:No,Yes|sec=Settings",
                "run64|Run script in 64-bit PowerShell|choice:No,Yes|sec=Settings",
                "schedule|Schedule|choice:Daily,Hourly,Once|sec=Assignments");
        }

        static void InitApps()
        {
            const string W32 = "if=type=Windows app (Win32)";
            var app = K("app", "App", "Apps", "Apps", "Apps > All apps", "type", true, new[] { "type", "publisher", "version" },
                "type|App type|choice:Windows app (Win32),Microsoft Store app (new),Microsoft 365 Apps (Windows 10 and later),iOS store app,Managed Google Play app,Web link|req",
                "publisher|Publisher|text|req",
                "description|Description|text",
                "version|App version|text",
                "package|App package file (.intunewin)|package|req|sec=App information|" + W32,
                "installCmd|Install command|text|req|sec=Program|" + W32,
                "uninstallCmd|Uninstall command|text|req|sec=Program|" + W32,
                "installBehavior|Install behavior|choice:System,User|sec=Program|if=type=Windows app (Win32)/Microsoft Store app (new)",
                "restart|Device restart behavior|choice:App install may force a device restart,Determine behavior based on return codes,No specific action,Intune will force a mandatory device restart|sec=Program|" + W32,
                "arch|Operating system architecture|multi:x64,x86,ARM64|req|sec=Requirements|" + W32,
                "minOs|Minimum operating system|choice:Windows 10 1607,Windows 10 22H2,Windows 11 21H2,Windows 11 23H2,Windows 11 24H2|req|sec=Requirements|" + W32,
                "diskMb|Disk space required (MB)|number|min=0|max=100000|sec=Requirements|" + W32,
                "detect|Rules format / rule type|choice:MSI,File,Registry|req|sec=Detection rules|" + W32,
                "detectPath|Path (File) or key path (Registry)|text|sec=Detection rules|if=detect=File/Registry",
                "detectName|File or folder (File) or value name (Registry)|text|sec=Detection rules|if=detect=File/Registry",
                "msiCode|MSI product code|text|sec=Detection rules|if=detect=MSI",
                "dependsOn|Dependencies (apps, installed first)|apps|sec=Dependencies|" + W32,
                "supersedes|Supersedence (app this one replaces)|app|sec=Supersedence|" + W32,
                "uninstallPrevious|Uninstall previous version|choice:Yes,No|sec=Supersedence|" + W32,
                "storeId|Package identifier (from the store search)|storeapp|req|sec=App information|if=type=Microsoft Store app (new)",
                "suiteApps|Apps to include|multi:Word,Excel,PowerPoint,Outlook,Teams,OneDrive Desktop,OneNote,Access,Publisher|req|sec=Configure app suite|if=type=Microsoft 365 Apps (Windows 10 and later)",
                "officeArch|Architecture|choice:64-bit,32-bit|sec=Configure app suite|if=type=Microsoft 365 Apps (Windows 10 and later)",
                "channel|Update channel|choice:Current Channel,Monthly Enterprise Channel,Semi-Annual Enterprise Channel|sec=Configure app suite|if=type=Microsoft 365 Apps (Windows 10 and later)",
                "removeMsi|Remove other versions (MSI)|choice:Yes,No|sec=Configure app suite|if=type=Microsoft 365 Apps (Windows 10 and later)",
                "acceptEula|Accept the license terms for users|choice:No,Yes|sec=Configure app suite|if=type=Microsoft 365 Apps (Windows 10 and later)",
                "sharedActivation|Use shared computer activation|choice:No,Yes|sec=Configure app suite|if=type=Microsoft 365 Apps (Windows 10 and later)|help=For devices several people share (kiosks, Azure Virtual Desktop).",
                "bundle|App store link or bundle ID|text|req|sec=App information|if=type=iOS store app",
                "gplay|Managed Google Play app|text|req|sec=App information|if=type=Managed Google Play app",
                "url|App URL|text|req|sec=App information|if=type=Web link");
            app.Intents = new[] { "required", "available", "uninstall", "exclude" };
            K("appProtection", "App protection policy", "App protection policies", "Apps", "Apps > App protection policies", "platform", true, new[] { "platform", "targetApps" },
                "platform|Platform|choice:" + Ios + ",Android|req",
                "targetApps|Apps|multi:Microsoft Outlook,Microsoft Teams,Microsoft Word,Microsoft Excel,Microsoft PowerPoint,Microsoft OneDrive,Microsoft Edge|req|sec=Apps",
                "deviceTypes|Target policy to|choice:All device types,Unmanaged devices,Managed devices|sec=Apps",
                "backup|Back up org data to the phone's cloud backups|choice:Block,Allow|sec=Data protection",
                "sendData|Send org data to other apps|choice:Policy managed apps,All apps,None|sec=Data protection",
                "receiveData|Receive data from other apps|choice:All apps,Policy managed apps,None|sec=Data protection",
                "saveCopies|Save copies of org data|choice:Block,Allow|sec=Data protection",
                "cutCopy|Restrict cut, copy and paste between other apps|choice:Any app,Policy managed apps,Policy managed apps with paste in,Blocked|sec=Data protection",
                "printing|Printing org data|choice:Allow,Block|sec=Data protection",
                "pin|PIN for access|choice:Require,Not required|sec=Access requirements",
                "pinType|PIN type|choice:Numeric,Passcode|sec=Access requirements|if=pin=Require",
                "pinLength|Select minimum PIN length|number|min=4|max=14|def=4|sec=Access requirements|if=pin=Require",
                "biometric|Biometrics instead of PIN for access|choice:Allow,Block|sec=Access requirements|if=pin=Require",
                "creds|Work or school account credentials for access|choice:Not required,Require|sec=Access requirements",
                "maxPin|Max PIN attempts (then reset PIN)|number|min=1|max=10|def=5|sec=Conditional launch",
                "offlineBlock|Offline grace period: block access (minutes)|number|min=1|max=100000|def=720|sec=Conditional launch",
                "offlineWipe|Offline grace period: wipe data (days)|number|min=1|max=365|def=90|sec=Conditional launch",
                "jailbreak|Jailbroken or rooted devices|choice:Block access,Wipe data|sec=Conditional launch",
                "minOsApp|Min OS version (then block access)|text|sec=Conditional launch");
            K("appConfig", "App configuration policy", "App configuration policies", "Apps", "Apps > App configuration policies", "enrollment", true, new[] { "enrollment", "targetApp" },
                "enrollment|Policy for|choice:Managed apps,Managed devices|req",
                "platform|Platform|choice:" + Ios + "," + Android + "|if=enrollment=Managed devices",
                "targetApp|Targeted app|choice:Microsoft Outlook,Microsoft Teams,Microsoft Edge,Microsoft OneDrive|req",
                "settings|Configuration settings (name=value, one per line)|longtext|req|sec=Settings");
        }
    }
}
