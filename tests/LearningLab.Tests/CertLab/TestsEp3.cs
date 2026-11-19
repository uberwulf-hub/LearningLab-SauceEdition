// Cert Lab - checks of the pretend tenant, part 3: Windows updates, Autopilot, apps and Conditional Access.
using System;
using System.Collections.Generic;
using System.Linq;

namespace CertLab
{
    public static partial class Tests
    {
        static void EpUpdateTests()
        {
            Check("Patch Tuesday in March 2026 is the 10th", EpTenant.PatchTuesday(2026, 3).Day == 10);
            Check("Patch Tuesday in October 2025 is the 14th", EpTenant.PatchTuesday(2025, 10).Day == 14);
            var e = new E();
            Check("before 10 March the newest update is February's", e.T.LatestPatch(0) == "2026-02");
            e.Do("create ring name=Broad qualityDeferral=14 featureDeferral=0");
            e.Do("create ring name=Pilot qualityDeferral=0 featureDeferral=0");
            e.Do("assign ring Broad \"Windows devices\"");
            e.Do("assign ring Broad \"Pilot devices\" intent=exclude");
            e.Do("assign ring Pilot \"Pilot devices\"");
            e.S.Advance(9 * 86400);   // past 10 March
            e.Sync("RB-LT-001"); e.Sync("RB-LT-002");
            e.Yes("device[\"RB-LT-002\"].patch == 2026-03");
            e.Yes("device[\"RB-LT-002\"].ring == Pilot");
            e.Yes("device[\"RB-LT-001\"].patch == 2026-02");
            e.Yes("device[\"RB-LT-001\"].osVersion == 10.0.26100.7840");
            e.Yes("device[\"RB-LT-002\"].osVersion == 10.0.26100.7920");
            // expedite the March update for everyone
            e.Do("create qualityUpdate name=\"March now\" expediteBelow=\"2026-03 cumulative update\"");
            e.Do("assign qualityUpdate \"March now\" \"Windows devices\"");
            e.Sync("RB-LT-001");
            e.Yes("device[\"RB-LT-001\"].patch == 2026-03");
            // pause
            e.Do("set ring Broad pausedQuality=Yes");
            e.Do("delete qualityUpdate \"March now\"");
            e.Do("device RB-KIOSK-01 patch=2026-02");
            e.S.Advance(40 * 86400);
            e.Sync("RB-KIOSK-01");
            e.Yes("device[\"RB-KIOSK-01\"].patch == 2026-02");
            e.Do("set ring Broad pausedQuality=No");
            e.Sync("RB-KIOSK-01");
            e.Yes("device[\"RB-KIOSK-01\"].patch == 2026-03");
            // Windows 10 gets nothing after October 2025; a feature updates policy moves it to Windows 11
            e.Sync("RB-DT-010");
            e.Yes("device[\"RB-DT-010\"].patch == 2025-10");
            e.Do("create group name=\"Win10 desktops\" type=Security membership=Assigned members=\"RB-DT-010\nRB-DT-007\"");
            e.Do("create featureUpdate name=\"Win11 24H2\" version=\"Windows 11, version 24H2\"");
            e.Bad("assign featureUpdate \"Win11 24H2\" Finance", "device groups");
            e.Do("assign featureUpdate \"Win11 24H2\" \"Win10 desktops\"");
            e.Sync("RB-DT-010"); e.Sync("RB-DT-007");
            e.Yes("device[\"RB-DT-010\"].featureVersion == 24H2");
            e.Yes("device[\"RB-DT-010\"].osVersion like 10.0.26100.*");
            e.Yes("device[\"RB-DT-007\"].featureVersion == 22H2");
            e.Yes("device[\"RB-DT-007\"].policy[\"Win11 24H2\"] == \"Not applicable\"");
            e.S.Dispose();
        }

        static void EpAutopilotTests()
        {
            // without registration: ordinary setup, a personal device
            var e = new E();
            e.S.Type("RB-LT-003", "#oobe jordan");
            e.Yes("device[\"LB14-55821\"].join == \"Entra joined\"");
            e.Yes("device[\"LB14-55821\"].ownership == Personal");
            e.Yes("device[\"LB14-55821\"].name like DESKTOP-*");
            e.S.Dispose();
            // the Autopilot way: hash, group tag, dynamic group, profile, user-driven setup
            e = new E();
            e.Do("create group name=\"Autopilot Sales\" type=Security membership=\"Dynamic device\" rule=\"(device.devicePhysicalIds -any (_ -eq \\\"[OrderID]:Sales\\\"))\"");
            e.Do("create autopilotProfile name=\"Sales laptops\" mode=User-driven joinType=\"Microsoft Entra joined\" userType=Standard nameTemplate=RB-%SERIAL%");
            e.Do("create autopilotProfile name=\"Later profile\" mode=User-driven nameTemplate=X-%RAND:4%");
            e.Do("assign autopilotProfile \"Sales laptops\" \"Autopilot Sales\"");
            e.Do("assign autopilotProfile \"Later profile\" \"Autopilot Sales\"");
            e.S.Type("RB-LT-003", "Get-WindowsAutopilotInfo -OutputFile C:\\HWID\\hash.csv");
            var con = e.S.Console("RB-LT-003"); con.TakeOutput();
            e.Do("import autopilot RB-LT-003 C:\\HWID\\hash.csv groupTag=Sales");
            e.Yes("autopilot[\"LB14-55821\"].profileStatus == Assigning");
            e.Yes("group[\"Autopilot Sales\"].members has RB-LT-003");
            e.S.Advance(20 * 60);
            e.Yes("autopilot[\"LB14-55821\"].profileStatus == Assigned");
            e.Yes("autopilot[\"LB14-55821\"].profile == \"Sales laptops\"");   // the oldest profile wins
            e.S.Type("RB-LT-003", "#oobe priya");
            Check("Autopilot setup ran", e.S.Errors.Count == 0, string.Join("\n", e.S.Errors.ToArray()));
            e.Yes("device[\"LB14-55821\"].name == RB-LB14-55821");
            e.Yes("device[\"RB-LB14-55821\"].ownership == Corporate");
            e.Yes("device[\"RB-LB14-55821\"].managed == Intune");
            e.Yes("device[\"RB-LB14-55821\"].enrollmentProfile == \"Sales laptops\"");
            e.Yes("device[\"RB-LB14-55821\"].user == priya");
            e.S.Dispose();
            // self-deploying needs TPM 2.0; hybrid join needs the connector and a Domain Join profile
            e = new E();
            e.Do("create group name=APall type=Security membership=\"Dynamic device\" rule=\"(device.devicePhysicalIds -any (_ -startsWith \\\"[ZTDid]\\\"))\"");
            e.Do("create autopilotProfile name=Kiosk mode=Self-deploying");
            e.Do("assign autopilotProfile Kiosk APall");
            e.Do("create autopilot name=LB14-55821 serial=LB14-55821");
            e.Do("device RB-LT-003 tpm=1.2");
            e.S.Advance(20 * 60);
            e.Yes("autopilot[\"LB14-55821\"].profileStatus == Assigned");
            e.S.Type("RB-LT-003", "#oobe");
            Check("self-deploying refuses TPM 1.2", e.S.Errors.Any(x => x.Contains("TPM 2.0")), string.Join("\n", e.S.Errors.ToArray()));
            e.S.Errors.Clear();
            e.Do("delete autopilotProfile Kiosk");
            e.Do("create autopilotProfile name=Hybrid mode=User-driven joinType=\"Microsoft Entra hybrid joined\"");
            e.Do("assign autopilotProfile Hybrid APall");
            e.S.Advance(20 * 60);
            e.Do("tenant adConnector=\"Not installed\"");
            e.S.Type("RB-LT-003", "#oobe sam");
            Check("hybrid join needs the connector", e.S.Errors.Any(x => x.Contains("Connector")), string.Join("\n", e.S.Errors.ToArray()));
            e.S.Errors.Clear();
            e.Do("tenant adConnector=Installed");
            e.S.Type("RB-LT-003", "#oobe sam");
            Check("hybrid join needs a Domain Join profile", e.S.Errors.Any(x => x.Contains("Domain Join")), string.Join("\n", e.S.Errors.ToArray()));
            e.S.Errors.Clear();
            e.Do("create config name=DJ " + WinP + " type=\"Domain Join\" prefix=RBHY- domain=riverbend.local ou=\"OU=Laptops,DC=riverbend,DC=local\"");
            e.Do("assign config DJ APall");
            e.S.Type("RB-LT-003", "#oobe sam");
            e.Yes("device[\"LB14-55821\"].join == \"Hybrid joined\"");
            e.Yes("device[\"LB14-55821\"].name like RBHY-*");
            e.Yes("device[\"LB14-55821\"].name.count == 1");
            e.S.Dispose();
            // provisioning package (bulk enrolment)
            e = new E();
            e.S.RunSetup(new List<SolutionStep> { new SolutionStep { Machine = "RB-LT-003", Line = "Set-Content C:\\Users\\Public\\Bulk.ppkg -Value \"DeviceName=RB-BULK-%RAND:3%`nTokenExpires=2026-01-31\"" } });
            e.S.Type("RB-LT-003", "Install-ProvisioningPackage -PackagePath C:\\Users\\Public\\Bulk.ppkg -QuietInstall");
            e.Yes("device[\"LB14-55821\"].join == None");
            e.S.RunSetup(new List<SolutionStep> { new SolutionStep { Machine = "RB-LT-003", Line = "Set-Content C:\\Users\\Public\\Bulk.ppkg -Value \"DeviceName=RB-BULK-%RAND:3%`nTokenExpires=2026-08-31\"" } });
            e.S.Type("RB-LT-003", "Install-ProvisioningPackage -PackagePath C:\\Users\\Public\\Bulk.ppkg -QuietInstall");
            e.Yes("device[\"LB14-55821\"].join == \"Entra joined\"");
            e.Yes("device[\"LB14-55821\"].managed == Intune");
            e.Yes("device[\"LB14-55821\"].name like RB-BULK-*");
            e.S.Dispose();
        }
    }
}
