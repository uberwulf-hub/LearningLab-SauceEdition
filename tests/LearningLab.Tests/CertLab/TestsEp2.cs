// Cert Lab - checks of the pretend tenant, part 2: assignments and filters, configuration conflicts, compliance,
// BitLocker, LAPS, the firewall and Defender for Endpoint.
using System;
using System.Collections.Generic;
using System.Linq;

namespace CertLab
{
    public static partial class Tests
    {
        const string WinP = "platform=\"Windows 10 and later\"";

        static void EpAssignTests()
        {
            var e = new E();
            e.Do("create config name=\"Camera off\" " + WinP + " type=\"Settings catalog\" s:Camera/AllowCamera=\"Not allowed\"");
            e.Do("assign config \"Camera off\" \"Windows devices\"");
            e.Do("assign config \"Camera off\" \"Pilot devices\" intent=exclude");
            e.Sync("RB-LT-001"); e.Sync("RB-LT-002");
            e.Yes("device[\"RB-LT-001\"].setting[\"Camera/AllowCamera\"] == \"Not allowed\"");
            e.Yes("device[\"RB-LT-001\"].policy[\"Camera off\"] == Succeeded");
            e.No("device[\"RB-LT-002\"].setting[\"Camera/AllowCamera\"] == \"Not allowed\"");
            var m = e.T.MachineFor(e.D("RB-LT-001"));
            var k = m.HKLM.Find("SOFTWARE\\Microsoft\\PolicyManager\\current\\device\\Camera");
            Check("the setting is written under PolicyManager on the device", k != null && k.Value("AllowCamera") != null && (int)k.Value("AllowCamera").Data == 0);
            // a user group included, a device group excluded: Intune ignores that exclusion (the portal warns)
            e.Do("create config name=\"No Cortana\" " + WinP + " type=\"Settings catalog\" s:Experience/AllowCortana=Block");
            e.Do("assign config \"No Cortana\" Sales");
            var w = e.Do("assign config \"No Cortana\" \"Pilot devices\" intent=exclude");
            Check("mixing user and device groups warns", w.Warnings.Any(x => x.Contains("isn't supported")), string.Join(" ", w.Warnings.ToArray()));
            e.Sync("RB-LT-002");
            e.Yes("device[\"RB-LT-002\"].setting[\"Experience/AllowCortana\"] == Block");
            // filters
            e.Do("create filter name=\"Pro edition\" " + WinP + " rule=\"(device.operatingSystemSKU -eq \\\"Professional\\\")\"");
            e.Do("create config name=\"No consumer\" " + WinP + " type=\"Settings catalog\" s:Experience/AllowWindowsConsumerFeatures=Block");
            e.Do("assign config \"No consumer\" \"All devices\" filter=\"Pro edition\" mode=include");
            e.Sync("RB-LT-001"); e.Sync("RB-LT-002");
            e.Yes("device[\"RB-LT-002\"].setting[\"Experience/AllowWindowsConsumerFeatures\"] == Block");
            e.No("device[\"RB-LT-001\"].setting[\"Experience/AllowWindowsConsumerFeatures\"] == Block");
            e.Do("assign config \"No consumer\" \"All devices\" filter=\"Pro edition\" mode=exclude");
            e.Sync("RB-LT-001"); e.Sync("RB-LT-002");
            e.Yes("device[\"RB-LT-001\"].setting[\"Experience/AllowWindowsConsumerFeatures\"] == Block");
            e.No("device[\"RB-LT-002\"].setting[\"Experience/AllowWindowsConsumerFeatures\"] == Block");
            e.Yes("config[\"No consumer\"].filtermode[\"All devices\"] == exclude");
            e.Bad("assign config \"No consumer\" Sales intent=exclude filter=\"Pro edition\"", "excluded groups");
            e.Do("create filter name=\"iPhones\" platform=iOS/iPadOS rule=\"(device.model -startsWith \\\"iPhone\\\")\"");
            e.Bad("assign config \"No consumer\" Sales filter=iPhones", "is for iOS/iPadOS");
            e.Bad("assign config \"No consumer\" \"All devices\" intent=exclude", "can't be excluded");
            e.Bad("assign config \"No consumer\" Nobody", "no group");
            e.Do("create autopilotProfile name=AP1 mode=User-driven");
            e.Bad("assign autopilotProfile AP1 Sales", "device groups");
            e.Bad("assign ca X Sales", null);
            // conflicts: two profiles set one setting differently
            e.Do("create config name=\"Camera on\" " + WinP + " type=\"Settings catalog\" s:Camera/AllowCamera=Allowed");
            e.Do("assign config \"Camera on\" Sales");
            e.Sync("RB-LT-001");
            e.Yes("device[\"RB-LT-001\"].policy[\"Camera on\"] == Conflict");
            e.Yes("device[\"RB-LT-001\"].policy[\"Camera off\"] == Conflict");
            e.Yes("device[\"RB-LT-001\"].setting[\"Camera/AllowCamera\"] == \"\"");
            Check("a conflicting setting isn't on the device", m.HKLM.Find("SOFTWARE\\Microsoft\\PolicyManager\\current\\device\\Camera").Value("AllowCamera") == null);
            e.Do("unassign config \"Camera on\" Sales");
            e.Sync("RB-LT-001");
            e.Yes("device[\"RB-LT-001\"].setting[\"Camera/AllowCamera\"] == \"Not allowed\"");
            // a custom OMA-URI profile maps to the same Policy CSP setting
            e.Do("create config name=\"Bluetooth custom\" " + WinP + " type=Custom omaName=Bluetooth omaUri=./Device/Vendor/MSFT/Policy/Config/Connectivity/AllowBluetooth omaType=Integer omaValue=0");
            e.Do("assign config \"Bluetooth custom\" \"Windows devices\"");
            e.Sync("RB-LT-001");
            e.Yes("device[\"RB-LT-001\"].setting[\"Connectivity/AllowBluetooth\"] == \"Disallow Bluetooth\"");
            e.Do("delete config \"Bluetooth custom\"");
            e.Bad("delete group \"Windows devices\"", "still assigned");
            e.S.Dispose();
        }

        static void EpComplianceTests()
        {
            var e = new E();
            e.Yes("device[\"RB-LT-001\"].compliance == Compliant");
            e.Do("create compliance name=\"Win baseline\" " + WinP + " bitlocker=Require secureBoot=Require minOs=10.0.26100.7000");
            e.Do("assign compliance \"Win baseline\" \"Windows devices\"");
            e.Yes("device[\"RB-LT-001\"].compliance == Compliant");   // nothing changes before a check-in
            e.Sync("RB-LT-001");
            e.Yes("device[\"RB-LT-001\"].compliance == \"Not compliant\"");
            e.Yes("device[\"RB-LT-001\"].reasons contains BitLocker");
            e.Sync("RB-DT-010");
            e.Yes("device[\"RB-DT-010\"].reasons contains \"older than the minimum\"");
            e.No("device[\"RB-DT-010\"].reasons contains BitLocker");
            // grace period
            e.Do("set compliance \"Win baseline\" noncompliantDays=2 minOs=\"\"");
            e.Do("device RB-LT-002 compliance=Compliant");
            e.D("RB-LT-002").NoncompliantSince = DateTime.MinValue;
            e.Sync("RB-LT-002");
            e.Yes("device[\"RB-LT-002\"].compliance == \"In grace period\"");
            e.S.Advance(3 * 86400);
            e.Yes("device[\"RB-LT-002\"].compliance == \"Not compliant\"");
            // no policy assigned
            e.Do("unassign compliance \"Win baseline\" \"Windows devices\"");
            e.Sync("RB-LT-001");
            e.Yes("device[\"RB-LT-001\"].compliance == Compliant");
            e.Do("tenant noPolicyMark=\"Not compliant\"");
            e.Sync("RB-LT-001");
            e.Yes("device[\"RB-LT-001\"].compliance == \"Not compliant\"");
            e.Yes("device[\"RB-LT-001\"].reasons contains \"no compliance policy\"");
            e.Do("tenant noPolicyMark=Compliant");
            // validity period
            e.Do("device RB-LT-001 lastSyncDaysAgo=40");
            e.T.Evaluate(e.D("RB-LT-001"));
            e.Yes("device[\"RB-LT-001\"].reasons contains \"validity period\"");
            // mobile
            e.Do("create compliance name=\"iOS basics\" platform=iOS/iPadOS jailbreak=Block minOs=17.5");
            e.Do("assign compliance \"iOS basics\" Sales");
            e.Sync("Jordan's iPhone");
            e.Yes("device[\"Jordan's iPhone\"].reasons contains \"17.5\"");
            e.Do("device \"Jordan's iPhone\" osVersion=17.6");
            e.Sync("Jordan's iPhone");
            e.Yes("device[\"Jordan's iPhone\"].compliance == Compliant");
            // subscription activation: Pro steps up to Enterprise once the user has a Windows Enterprise licence
            e.Yes("device[\"RB-LT-002\"].edition == Pro");
            e.Do("set user priya +licenses=\"Windows Enterprise E3\"");
            e.Sync("RB-LT-002");
            e.Yes("device[\"RB-LT-002\"].edition == Enterprise");
            e.S.Dispose();
        }

        static void EpSecurityTests()
        {
            var e = new E();
            // BitLocker: not silent until warnings for other encryption are disabled
            e.Do("create diskEncryption name=BL requireEncryption=Enabled backup=Enabled");
            e.Do("assign diskEncryption BL \"Windows devices\"");
            e.Sync("RB-LT-001");
            e.Yes("device[\"RB-LT-001\"].bitlocker == Off");
            e.Yes("device[\"RB-LT-001\"].policy[BL] == Error");
            e.Do("set diskEncryption BL allowWarning=Disabled");
            e.Sync("RB-LT-001");
            e.Yes("device[\"RB-LT-001\"].bitlockerWhy contains \"standard user\"");
            e.Do("set diskEncryption BL standardUser=Enabled osMethod=\"XTS-AES 256-bit\"");
            e.Sync("RB-LT-001");
            e.Yes("device[\"RB-LT-001\"].bitlocker == Encrypting");
            e.S.Advance(7200);
            e.Yes("device[\"RB-LT-001\"].bitlocker == On");
            e.Yes("device[\"RB-LT-001\"].recoveryKeyIn == \"Microsoft Entra ID\"");
            e.Yes("device[\"RB-LT-001\"].bitlockerMethod == \"XTS-AES 256-bit\"");
            e.Bad("remote rotate-bitlocker RB-LT-001", "Rotation");
            e.Do("set diskEncryption BL rotation=\"Refresh on for Microsoft Entra joined devices\"");
            string key1 = e.D("RB-LT-001")["recoveryKey"];
            e.Do("remote rotate-bitlocker RB-LT-001");
            Check("BitLocker key rotation makes a new recovery key", e.D("RB-LT-001")["recoveryKey"] != key1);
            // LAPS
            e.Do("create laps name=LAPS backup=\"Backup the password to Microsoft Entra ID only\" ageDays=30");
            e.Do("assign laps LAPS \"Windows devices\"");
            e.Sync("RB-LT-001");
            e.Yes("device[\"RB-LT-001\"].laps == Error");
            e.Yes("device[\"RB-LT-001\"].lapsWhy contains \"isn't enabled\"");
            e.Do("tenant lapsEnabled=Yes");
            e.Sync("RB-LT-001");
            e.Yes("device[\"RB-LT-001\"].laps == On");
            string pw = e.D("RB-LT-001")["laps.password"];
            Check("LAPS password has the policy's length", pw.Length == 14, pw);
            Check("LAPS sets the account's password on the device", e.T.MachineFor(e.D("RB-LT-001")).FindUser("Administrator").Password == pw);
            e.Do("remote rotate-laps RB-LT-001");
            Check("Rotate local admin password makes a new one", e.D("RB-LT-001")["laps.password"] != pw);
            pw = e.D("RB-LT-001")["laps.password"];
            e.S.Type("RB-LT-001", "#laps use");
            e.S.Advance(23 * 3600);
            Check("not reset before the post-authentication delay", e.D("RB-LT-001")["laps.password"] == pw);
            e.S.Advance(2 * 3600);
            Check("reset after the post-authentication delay", e.D("RB-LT-001")["laps.password"] != pw);
            e.Do("set laps LAPS adminName=rbadmin");
            e.Sync("RB-LT-001");
            e.Yes("device[\"RB-LT-001\"].lapsWhy contains \"doesn't exist\"");
            e.Sync("RB-DT-010");
            e.Yes("device[\"RB-DT-010\"].laps == Error");
            // firewall
            e.Do("create firewall name=FW publicOn=False");
            e.Do("assign firewall FW \"Windows devices\"");
            e.Sync("RB-LT-002");
            e.Yes("device[\"RB-LT-002\"].firewall == Off");
            Check("the device's Public firewall profile is off", !e.T.MachineFor(e.D("RB-LT-002")).FwProfiles.First(p => p.Name == "Public").Enabled);
            // Defender for Endpoint and the machine risk score
            e.Do("device RB-LT-002 riskAfterOnboard=Medium");
            e.Do("create compliance name=Risk " + WinP + " mdeRisk=Low");
            e.Do("assign compliance Risk \"Pilot devices\"");
            e.Sync("RB-LT-002");
            e.Yes("device[\"RB-LT-002\"].reasons contains onboarded");
            e.Do("create edr name=Onboard package=\"Auto from connector\"");
            e.Do("assign edr Onboard \"Windows devices\"");
            e.Sync("RB-LT-002");
            e.Yes("device[\"RB-LT-002\"].policy[Onboard] == Error");
            e.Do("tenant mdeConnector=On");
            e.Sync("RB-LT-002");
            e.Yes("device[\"RB-LT-002\"].mde == Yes");
            e.Yes("device[\"RB-LT-002\"].reasons contains \"risk score is Medium\"");
            e.Do("set compliance Risk mdeRisk=Medium");
            e.Do("set firewall FW publicOn=True");
            e.Sync("RB-LT-002");
            e.Yes("device[\"RB-LT-002\"].compliance == Compliant");
            e.S.Dispose();
        }
    }
}
