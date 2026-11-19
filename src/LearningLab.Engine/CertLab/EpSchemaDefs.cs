// Cert Lab - the kinds of the practice portal, part 1: identity (users, groups, named locations, filters, roles,
// scope tags, tenant settings), enrolment and Autopilot, device configuration, compliance and updates. Labels are
// shortened from the real portal's where that helps; the course says so.
using System;
using System.Collections.Generic;

namespace CertLab
{
    public static partial class EpSchemas
    {
        const string NC = "Not configured";

        static void InitIdentity()
        {
            K("user", "User", "Users", "Users and groups", "Users and groups > Users", "", false, new[] { "upn", "department", "jobTitle", "licenses" },
                "upn|User principal name|text|req|help=The sign-in name, for example jordan@riverbend.example.",
                "department|Department|text", "jobTitle|Job title|text", "city|City|text", "country|Country or region|text",
                "usageLocation|Usage location|choice:,US,GB,CA,AU,DE,FR|help=Needed before a licence can be assigned.",
                "accountEnabled|Account enabled|choice:Yes,No",
                "licenses|Licences|multi:Microsoft 365 E3,Microsoft 365 E5,Microsoft 365 Business Premium,Enterprise Mobility + Security E3,Intune Plan 1,Office 365 E3,Windows Enterprise E3|sec=Licences",
                "mfa|Multifactor authentication|choice:Registered,Not registered|sec=Authentication",
                "roles|Microsoft Entra roles|multi:Global Administrator,Intune Administrator,Cloud Device Administrator,Helpdesk Administrator,Security Administrator,Global Reader|sec=Roles");
            K("group", "Group", "Groups", "Users and groups", "Users and groups > Groups", "", false, new[] { "type", "membership", "description" },
                "type|Group type|choice:Security,Microsoft 365|req|def=Security",
                "description|Description|text",
                "membership|Membership type|choice:Assigned,Dynamic user,Dynamic device|req|def=Assigned|help=Dynamic groups need Microsoft Entra ID P1. A dynamic group holds users or devices, never both.",
                "rule|Dynamic membership rule|rule|if=membership=Dynamic user/Dynamic device|help=For example (user.department -eq \"Sales\") or (device.deviceOSType -eq \"Windows\").",
                "members|Members|members|if=membership=Assigned|help=Users, devices or other groups, one per line.");
            K("location", "Named location", "Named locations", "Conditional Access", "Conditional Access > Named locations", "", false, new[] { "ranges", "trusted" },
                "ranges|IP ranges (CIDR)|longtext|req|help=One range per line, for example 203.0.113.0/24.",
                "trusted|Mark as trusted location|choice:No,Yes");
            K("filter", "Filter", "Filters", "Tenant administration", "Tenant administration > Filters", "platform", false, new[] { "platform", "rule" },
                "platform|Platform|choice:" + Win + "," + Ios + "," + Android + "|req",
                "rule|Rule|filterrule|req|help=For example (device.osVersion -startsWith \"10.0.22\") and (device.deviceOwnership -eq \"Corporate\").");
            K("scopeTag", "Scope tag", "Scope tags", "Tenant administration", "Tenant administration > Roles > Scope tags", "", false, new[] { "description" },
                "description|Description|text");
            K("roleAssignment", "Role assignment", "Role assignments", "Tenant administration", "Tenant administration > Roles", "", false, new[] { "role", "admins", "scopeGroups" },
                "role|Role|choice:Help Desk Operator,Policy and Profile manager,Application Manager,Read Only Operator,Endpoint Security Manager,Intune Role Administrator,School Administrator|req",
                "admins|Admin groups (members)|groups|req|help=The people who get the role, as groups.",
                "scopeGroups|Scope (groups)|groups|req|help=The users and devices they may manage.",
                "scopeTags|Scope tags|text|help=Objects with one of these tags are the ones they see. Leave empty for Default.");
            K("tenant", "Tenant settings", "Tenant settings", "Tenant administration", "Tenant administration > Settings", "", false, new string[0],
                "mdmScope|MDM user scope (automatic enrolment)|choice:None,Some,All|sec=Automatic enrolment|help=Who is enrolled into Intune when they join a Windows device to Microsoft Entra ID.",
                "mdmGroup|MDM user scope group|group|sec=Automatic enrolment|if=mdmScope=Some",
                "joinScope|Users may join devices to Microsoft Entra|choice:All,Selected,None|sec=Microsoft Entra device settings",
                "joinGroup|Users who may join|group|sec=Microsoft Entra device settings|if=joinScope=Selected",
                "lapsEnabled|Enable Microsoft Entra Local Administrator Password Solution (LAPS)|choice:No,Yes|sec=Microsoft Entra device settings",
                "noPolicyMark|Mark devices with no compliance policy assigned as|choice:Compliant,Not compliant|sec=Compliance policy settings",
                "validityDays|Compliance status validity period (days)|number|min=1|max=120|def=30|sec=Compliance policy settings",
                "enrollLimit|Device limit per user (default restriction)|number|min=1|max=15|def=5|sec=Enrolment restrictions",
                "winPersonal|Personally owned Windows devices (default restriction)|choice:Allow,Block|sec=Enrolment restrictions",
                "mdeConnector|Connect Windows devices to Microsoft Defender for Endpoint|choice:Off,On|sec=Connectors",
                "windowsData|Enable features that need Windows diagnostic data|choice:Off,On|sec=Connectors|help=Windows update reports and Endpoint analytics need it.",
                "adConnector|Intune Connector for Active Directory|choice:Not installed,Installed|sec=Connectors|help=Hybrid join with Autopilot needs it.");
        }

        static void InitDevices()
        {
            K("autopilot", "Autopilot device", "Autopilot devices", "Devices", "Devices > Enrollment > Windows Autopilot > Devices", "", false, new[] { "serial", "model", "groupTag", "profileStatus" },
                "serial|Serial number|text|req", "manufacturer|Manufacturer|text", "model|Model|text",
                "groupTag|Group tag|text|help=Becomes [OrderID]:tag on the device, for dynamic groups.",
                "user|Assigned user|user|help=Pre-fills the sign-in name during setup (user-driven mode).");
            K("autopilotProfile", "Autopilot deployment profile", "Autopilot deployment profiles", "Devices", "Devices > Enrollment > Windows Autopilot > Deployment profiles", "", true, new[] { "mode", "joinType", "nameTemplate" },
                "description|Description|text",
                "convertAll|Convert all targeted devices to Autopilot|choice:No,Yes",
                "mode|Deployment mode|choice:User-driven,Self-deploying|sec=Out-of-box experience (OOBE)",
                "joinType|Join to Microsoft Entra ID as|choice:Microsoft Entra joined,Microsoft Entra hybrid joined|sec=Out-of-box experience (OOBE)",
                "skipAdCheck|Skip AD connectivity check|choice:No,Yes|sec=Out-of-box experience (OOBE)|if=joinType=Microsoft Entra hybrid joined",
                "language|Language (Region)|choice:Operating system default,User select,English (United States),English (United Kingdom)|sec=Out-of-box experience (OOBE)",
                "eula|Microsoft Software License Terms|choice:Hide,Show|sec=Out-of-box experience (OOBE)",
                "privacy|Privacy settings|choice:Hide,Show|sec=Out-of-box experience (OOBE)",
                "changeAccount|Hide change account options|choice:Hide,Show|sec=Out-of-box experience (OOBE)",
                "userType|User account type|choice:Standard,Administrator|sec=Out-of-box experience (OOBE)",
                "preprovision|Allow pre-provisioned deployment|choice:No,Yes|sec=Out-of-box experience (OOBE)",
                "nameTemplate|Apply device name template|text|sec=Out-of-box experience (OOBE)|help=Up to 15 characters: letters, digits, hyphens, %SERIAL% or %RAND:n%. Empty: Windows picks a name. Entra joined only.");
            K("esp", "Enrollment Status Page", "Enrollment Status Pages", "Devices", "Devices > Enrollment > Enrollment Status Page", "", true, new[] { "showProgress", "blockUntil" },
                "showProgress|Show app and profile configuration progress|choice:Yes,No",
                "timeout|Show an error when installation takes longer than (minutes)|number|min=1|max=1440|def=60",
                "blockUntil|Block device use until these apps are installed|choice:All,Selected|if=showProgress=Yes",
                "blockApps|Apps to wait for|apps|if=blockUntil=Selected",
                "allowUse|Allow users to use the device if an installation error occurs|choice:No,Yes",
                "allowReset|Allow users to reset the device if an installation error occurs|choice:No,Yes");
            K("restriction", "Device platform restriction", "Device platform restrictions", "Devices", "Devices > Enrollment > Device platform restriction", "platform", true, new[] { "platform", "allow", "personal", "priority" },
                "platform|Platform|choice:Windows (MDM),iOS/iPadOS,Android Enterprise (work profile),macOS|req",
                "allow|Platform|choice:Allow,Block", "personal|Personally owned|choice:Allow,Block",
                "priority|Priority|number|min=1|max=99|def=1");
            K("config", "Configuration profile", "Configuration profiles", "Devices", "Devices > Configuration", "platform", true, new[] { "platform", "type", "description" },
                "platform|Platform|choice:" + Win + "," + Ios + "," + Android + "|req",
                "type|Profile type|choice:Settings catalog,Custom,Kiosk,Domain Join,Wi-Fi,Edition upgrade and mode switch,Device restrictions|req",
                "description|Description|text",
                "omaName|OMA-URI setting name|text|sec=Configuration settings|if=type=Custom",
                "omaUri|OMA-URI|text|sec=Configuration settings|if=type=Custom|help=For example ./Device/Vendor/MSFT/Policy/Config/Camera/AllowCamera",
                "omaType|Data type|choice:Integer,String,Boolean|sec=Configuration settings|if=type=Custom",
                "omaValue|Value|text|sec=Configuration settings|if=type=Custom",
                "kioskMode|Kiosk mode|choice:Single app&#44; full-screen kiosk,Multi app kiosk|sec=Configuration settings|if=type=Kiosk",
                "logon|User logon type|choice:Auto logon (local account),Local user account,Microsoft Entra user or group|sec=Configuration settings|if=type=Kiosk",
                "kioskApp|Application type|choice:Microsoft Edge browser,Store app|sec=Configuration settings|if=type=Kiosk",
                "edgeType|Microsoft Edge kiosk mode type|choice:Public browsing (InPrivate),Digital/Interactive signage|sec=Configuration settings|if=type=Kiosk",
                "startUrl|Start URL|text|sec=Configuration settings|if=type=Kiosk",
                "prefix|Computer name prefix|text|sec=Configuration settings|if=type=Domain Join",
                "domain|Domain name|text|sec=Configuration settings|if=type=Domain Join",
                "ou|Organizational unit|text|sec=Configuration settings|if=type=Domain Join|help=For example OU=Laptops,DC=riverbend,DC=local",
                "ssid|Network name (SSID)|text|sec=Configuration settings|if=type=Wi-Fi",
                "wifiSecurity|Wireless security type|choice:WPA/WPA2-Personal,Open (no authentication),WPA2-Enterprise|sec=Configuration settings|if=type=Wi-Fi",
                "wifiKey|Pre-shared key|text|sec=Configuration settings|if=type=Wi-Fi",
                "autoConnect|Connect automatically when in range|choice:Yes,No|sec=Configuration settings|if=type=Wi-Fi",
                "edition|Edition to upgrade to|choice:Windows 10/11 Enterprise,Windows 10/11 Education,Windows 10/11 Pro|sec=Configuration settings|if=type=Edition upgrade and mode switch",
                "camera|Block camera|choice:Not configured,Block|sec=Configuration settings|if=type=Device restrictions",
                "screenshot|Block screenshots and screen recording|choice:Not configured,Block|sec=Configuration settings|if=type=Device restrictions",
                "passcodeMin|Minimum passcode length|number|min=4|max=16|sec=Configuration settings|if=type=Device restrictions");
        }
    }
}
