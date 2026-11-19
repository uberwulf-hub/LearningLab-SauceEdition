// Cert Lab - checks of the MD-102 course's pretend tenant: form validation, assignments (include / exclude, user and
// device groups, filters), dynamic group rules, compliance with reasons and grace periods, update rings and feature
// updates, BitLocker and LAPS, Autopilot profile assignment and setup, app installs after a sync, and Conditional
// Access (who gets blocked).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CertLab
{
    public static partial class Tests
    {
        static IEnumerable<KeyValuePair<string, Action>> EpGroups()
        {
            yield return new KeyValuePair<string, Action>("ep-rules", EpRuleTests);
            yield return new KeyValuePair<string, Action>("ep-forms", EpFormTests);
            yield return new KeyValuePair<string, Action>("ep-assign", EpAssignTests);
            yield return new KeyValuePair<string, Action>("ep-compliance", EpComplianceTests);
            yield return new KeyValuePair<string, Action>("ep-security", EpSecurityTests);
            yield return new KeyValuePair<string, Action>("ep-updates", EpUpdateTests);
            yield return new KeyValuePair<string, Action>("ep-autopilot", EpAutopilotTests);
            yield return new KeyValuePair<string, Action>("ep-apps", EpAppTests);
            yield return new KeyValuePair<string, Action>("ep-ca", EpCaTests);
            foreach (var g in EpMoreGroups()) yield return g;
        }
        static partial void EpGroupsMore(List<KeyValuePair<string, Action>> l);
        static IEnumerable<KeyValuePair<string, Action>> EpMoreGroups() { var l = new List<KeyValuePair<string, Action>>(); EpGroupsMore(l); return l; }

        /// <summary>A fresh pretend tenant: Do runs a portal command and expects it to work; Bad expects it to fail.</summary>
        class E
        {
            public EpSim S = EpSim.Create(3);
            public EpTenant T { get { return S.T; } }
            public EpResult Last;
            public EpResult Do(string line) { Last = EpOps.Run(T, line); Check("portal: " + line, Last.Ok, Last.Text); return Last; }
            public bool Bad(string line, string expect)
            {
                Last = EpOps.Run(T, line);
                bool ok = !Last.Ok && (expect == null || Last.Text.IndexOf(expect, StringComparison.OrdinalIgnoreCase) >= 0);
                Check("refused: " + line + (expect == null ? "" : " (" + expect + ")"), ok, Last.Text);
                return ok;
            }
            public bool Is(string assertion) { string o; return EpCheck.Run(T, assertion, out o); }
            public void Yes(string assertion) { string o; Check(assertion, EpCheck.Run(T, assertion, out o), o); }
            public void No(string assertion) { string o; Check("not: " + assertion, !EpCheck.Run(T, assertion, out o), "it holds"); }
            public EpDevice D(string n) { return T.Device(n); }
            public void Sync(string n) { Do("remote sync \"" + n + "\""); }
        }

        static void EpRuleTests()
        {
            var e = new E();
            Func<string, string, bool> ok = (r, k) => EpRules.Check(r, k) == null;
            Check("a simple user rule parses", ok("(user.department -eq \"Sales\")", "group"));
            Check("-and / -or and -not", ok("(user.department -eq \"Sales\") -and -not (user.city -eq \"Denver\") -or (user.jobTitle -startsWith \"Sales\")", "group"));
            Check("-in a list", ok("user.city -in [\"Portland\",\"Denver\"]", "group"));
            Check("-any over devicePhysicalIds", ok("(device.devicePhysicalIds -any (_ -startsWith \"[ZTDid]\"))", "group"));
            Check("mixing user and device properties is refused", EpRules.Check("(user.department -eq \"Sales\") -and (device.deviceOSType -eq \"Windows\")", "group").Contains("not both"));
            Check("an unknown property is refused", EpRules.Check("(user.departmnet -eq \"Sales\")", "group").Contains("isn't supported"));
            Check("values need quotes", EpRules.Check("(user.department -eq Sales)", "group") != null);
            Check("an unclosed bracket is refused", EpRules.Check("(user.department -eq \"Sales\"", "group") != null);
            Check("filters take and / or", ok("(device.osVersion -startsWith \"10.0.26\") and (device.deviceOwnership -eq \"Corporate\")", "filter"));
            Check("filters use filter properties", EpRules.Check("(device.deviceOSType -eq \"Windows\")", "filter") != null);
            // membership
            e.Do("create group name=\"Sales users\" type=Security membership=\"Dynamic user\" rule=\"(user.department -eq \\\"Sales\\\")\"");
            e.Yes("group[\"Sales users\"].members has jordan");
            e.Yes("group[\"Sales users\"].members has priya");
            e.No("group[\"Sales users\"].members has sam");
            e.Do("set user sam department=Sales");
            e.Yes("group[\"Sales users\"].members has sam");
            e.Yes("group[\"Windows devices\"].members has RB-LT-001");
            e.No("group[\"Windows devices\"].members has RB-LT-003");
            e.No("group[\"Windows devices\"].members has \"Jordan's iPhone\"");
            e.No("group[\"Corporate Windows devices\"].members has SAM-HOME-PC");
            e.Bad("create group name=Mixed type=Security membership=\"Dynamic device\" rule=\"(user.department -eq \\\"IT\\\")\"", "device. properties");
            e.Bad("members add \"Windows devices\" RB-LT-001", "dynamic");
            // nested groups count for targeting
            e.Do("create group name=\"All sales staff\" type=Security membership=Assigned members=Sales");
            Check("nested group membership", e.T.InGroup("All sales staff", e.T.User("jordan")));
            e.S.Dispose();
        }

        static void EpFormTests()
        {
            var e = new E();
            e.Bad("create compliance platform=\"Windows 10 and later\"", "Name is required");
            e.Bad("create compliance name=X", "Platform is required");
            e.Bad("create compliance name=X platform=\"Windows 10 and later\" minOs=ten", "must look like");
            e.Bad("create compliance name=X platform=\"Windows 10 and later\" passwordMin=40", "between 4 and 16");
            e.Bad("create compliance name=X platform=\"Windows 10 and later\" bitlocker=Maybe", "pick one of");
            e.Do("create compliance name=\"Win baseline\" platform=\"windows 10 and later\" bitlocker=require");
            Check("choices are stored in their canonical spelling", e.T.Find("compliance", "Win baseline")["bitlocker"] == "Require" && e.T.Find("compliance", "Win baseline")["platform"] == EpSchemas.Win);
            Check("defaults are filled in", e.T.Find("compliance", "Win baseline")["noncompliantDays"] == "0");
            e.Bad("create compliance name=\"Win baseline\" platform=\"Windows 10 and later\"", "already called");
            e.Bad("create ring name=R1 qualityDeferral=45 featureDeferral=0", "between 0 and 30");
            e.Bad("create ring name=R1 qualityDeferral=7 featureDeferral=0 activeStart=6 activeEnd=2", "18 hours");
            e.Bad("create laps name=L1 backup=\"Backup the password to Microsoft Entra ID only\" ageDays=3", "at least 7");
            e.Do("create laps name=L1 backup=\"Backup the password to Active Directory only\" ageDays=3");
            e.Bad("create autopilotProfile name=AP mode=Self-deploying joinType=\"Microsoft Entra hybrid joined\"", "Entra join only");
            e.Bad("create autopilotProfile name=AP mode=User-driven nameTemplate=RB-LAPTOP-OFFICE-%RAND:2%", "15 characters");
            e.Bad("create autopilotProfile name=AP mode=User-driven nameTemplate=RB_%RAND:4%", "letters, digits");
            e.Bad("create autopilotProfile name=AP mode=User-driven joinType=\"Microsoft Entra hybrid joined\" nameTemplate=RB-%RAND:4%", "Domain Join");
            e.Do("create autopilotProfile name=AP mode=User-driven nameTemplate=RB-%RAND:5%");
            e.Bad("create config name=C1 platform=\"Windows 10 and later\" type=\"Settings catalog\"", "at least one setting");
            e.Bad("create config name=C1 platform=\"Windows 10 and later\" type=\"Settings catalog\" s:Camera/AllowCamera=Off", "pick one of");
            e.Do("create config name=C1 platform=\"Windows 10 and later\" type=\"Settings catalog\" \"s:Allow Camera=Not allowed\"");
            Check("settings are stored under their catalog key", e.T.Find("config", "C1")["s:Camera/AllowCamera"] == "Not allowed");
            e.Bad("create config name=K1 platform=\"Windows 10 and later\" type=Kiosk kioskApp=\"Microsoft Edge browser\"", "Start URL");
            e.Bad("create config name=W1 platform=\"Windows 10 and later\" type=Wi-Fi ssid=Office wifiKey=short", "8 to 63");
            e.Bad("create config name=I1 platform=iOS/iPadOS type=Kiosk", "for Windows 10 and later");
            e.Bad("create app name=Zip type=\"Windows app (Win32)\" publisher=X package=7z2301-x64.intunewin installCmd=\"7z2301-x64.exe /S\" uninstallCmd=u arch=x64 minOs=\"Windows 10 1607\" detect=MSI msiCode=nope", "MSI product code");
            e.Bad("create app name=Zip type=\"Windows app (Win32)\" publisher=X package=missing.intunewin installCmd=a uninstallCmd=u arch=x64 minOs=\"Windows 10 1607\" detect=File detectPath=C:\\x detectName=y", ".intunewin");
            e.Bad("create ca name=CA1 usersInclude=\"All users\" apps=\"All cloud apps\" grant=\"Grant access\" state=On", "at least one control");
            e.Bad("create ca name=CA1 usersInclude=nobody apps=\"Office 365\" grant=\"Block access\" state=On", "no user or group");
            e.Bad("create script name=S1 content=\"if ($x {\"", "syntax error");
            e.Bad("create user name=Test upn=test@contoso.com", "riverbend.example");
            e.Bad("create user name=Test upn=test@riverbend.example licenses=\"Microsoft 365 E3\"", "usage location");
            e.Do("create user name=Test upn=test@riverbend.example usageLocation=US licenses=\"Microsoft 365 E3\"");
            Check("a new user's alias comes from the sign-in name", e.T.User("test") != null);
            e.Bad("create firewallRules name=F1 rules=\"Bad rule\"", "In or Out");
            e.Do("create firewallRules name=F1 rules=\"Expenses app, In, Allow, TCP, 8443\"");
            e.Bad("create location name=HQ ranges=203.0.113.0", "CIDR");
            e.Bad("tenant mdmScope=Some", "MDM user scope");
            e.Do("tenant mdmScope=Some mdmGroup=\"Intune pilot users\"");
            e.Do("set compliance \"Win baseline\" +description=\"for all\" secureBoot=Require");
            Check("set changes only what it names", e.T.Find("compliance", "Win baseline")["secureBoot"] == "Require" && e.T.Find("compliance", "Win baseline")["bitlocker"] == "Require");
            Check("describe: a create reads as the portal path", EpOps.Describe("create compliance name=\"Win baseline\" platform=\"Windows 10 and later\" bitlocker=Require").StartsWith("Devices > Compliance > Create: Name = Win baseline; Platform = Windows 10 and later; Require BitLocker = Require"), EpOps.Describe("create compliance name=\"Win baseline\" platform=\"Windows 10 and later\" bitlocker=Require"));
            e.S.Dispose();
        }
    }
}
