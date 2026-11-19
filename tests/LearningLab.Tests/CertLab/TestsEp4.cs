// Cert Lab - checks of the pretend tenant, part 4: Win32, store and Microsoft 365 apps after a sync, and Conditional
// Access sign-ins (who gets blocked).
using System;
using System.Collections.Generic;
using System.Linq;

namespace CertLab
{
    public static partial class Tests
    {
        static string Win32(string name, string pkg, string install, string detect)
        {
            return "create app name=\"" + name + "\" type=\"Windows app (Win32)\" publisher=\"Riverbend IT\" package=" + pkg + " installCmd=\"" + install + "\" uninstallCmd=\"uninstall.exe /S\" installBehavior=System arch=x64 minOs=\"Windows 10 1607\" " + detect;
        }

        static void EpAppTests()
        {
            var e = new E();
            e.Do(Win32("7-Zip", "7z2301-x64.intunewin", "7z2301-x64.exe /S", "detect=File detectPath=\"C:\\Program Files\\7-Zip\" detectName=7z.exe"));
            e.Do("assign app 7-Zip \"Windows devices\" intent=required");
            e.Sync("RB-LT-001");
            e.Yes("device[\"RB-LT-001\"].app[\"7-Zip\"] == Installed");
            Check("the app's files are on the device", e.T.MachineFor(e.D("RB-LT-001")).FindFile("C:\\Program Files\\7-Zip\\7z.exe") != null);
            // not silent
            e.Do(Win32("Notepad++", "npp.8.6.Installer.x64.intunewin", "npp.8.6.Installer.x64.exe", "detect=File detectPath=\"C:\\Program Files\\Notepad++\" detectName=notepad++.exe"));
            e.Do("assign app Notepad++ \"Pilot devices\"");
            e.Sync("RB-LT-002");
            e.Yes("device[\"RB-LT-002\"].app[\"Notepad++\"] == Failed");
            Check("not silent: the installer timed out", e.D("RB-LT-002").Apps["Notepad++"].Detail.Contains("silent"), e.D("RB-LT-002").Apps["Notepad++"].Detail);
            // wrong detection rule
            e.Do("set app Notepad++ installCmd=\"npp.8.6.Installer.x64.exe /S\" detectName=npp.exe");
            e.Sync("RB-LT-002");
            Check("a wrong detection rule: not detected after installation", e.D("RB-LT-002").Apps["Notepad++"].Detail.Contains("0x87D1041C"), e.D("RB-LT-002").Apps["Notepad++"].Detail);
            e.Do("set app Notepad++ detectName=notepad++.exe");
            e.Sync("RB-LT-002");
            e.Yes("device[\"RB-LT-002\"].app[\"Notepad++\"] == Installed");
            // dependencies
            e.Do(Win32("Riverbend Runtime", "RiverbendRuntime-1.0.intunewin", "rbruntime.exe /quiet", "detect=File detectPath=\"C:\\Program Files\\Riverbend\\Runtime\" detectName=rbrt.dll"));
            e.Do(Win32("Expenses 2.4", "RiverbendExpenses-2.4.intunewin", "msiexec /i RiverbendExpenses-2.4.msi /qn", "detect=MSI msiCode={6B1A3C55-2F0E-4C7A-9D11-0A7E5B3C2D41}"));
            e.Do("assign app \"Expenses 2.4\" Finance");
            e.Sync("RB-DT-010");
            e.Yes("device[\"RB-DT-010\"].app[\"Expenses 2.4\"] == Failed");
            Check("a missing dependency: MSI 1603", e.D("RB-DT-010").Apps["Expenses 2.4"].Detail.Contains("1603"));
            e.Do("set app \"Expenses 2.4\" dependsOn=\"Riverbend Runtime\"");
            e.Sync("RB-DT-010");
            e.Yes("device[\"RB-DT-010\"].app[\"Expenses 2.4\"] == Installed");
            e.Yes("device[\"RB-DT-010\"].app[\"Riverbend Runtime\"] == Installed");
            // supersedence
            e.Do(Win32("Expenses 3.0", "RiverbendExpenses-3.0.intunewin", "msiexec /i RiverbendExpenses-3.0.msi /qn", "detect=MSI msiCode={9C2E7D10-5B44-4F3A-8E6D-1F2A3B4C5D60} dependsOn=\"Riverbend Runtime\" supersedes=\"Expenses 2.4\" uninstallPrevious=Yes"));
            e.Do("assign app \"Expenses 3.0\" Finance");
            e.Sync("RB-DT-010");
            e.Yes("device[\"RB-DT-010\"].app[\"Expenses 3.0\"] == Installed");
            Check("supersedence removed the old version", !e.T.Detected(e.D("RB-DT-010"), e.T.Find("app", "Expenses 2.4")));
            // requirements
            e.Do(Win32("Label printer", "LabelPrinter-1.2.intunewin", "LabelPrinterSetup.exe /silent", "detect=File detectPath=C:\\Progra~1 detectName=x").Replace("minOs=\"Windows 10 1607\"", "minOs=\"Windows 11 23H2\""));
            e.Do("assign app \"Label printer\" Finance");
            e.Sync("RB-DT-010");
            e.Yes("device[\"RB-DT-010\"].app[\"Label printer\"] == \"Not applicable\"");
            // available and Company Portal
            e.Do("assign app Notepad++ Sales intent=available");
            e.Bad("assign app Notepad++ \"Windows devices\" intent=available", "user groups");
            e.Sync("RB-LT-001");
            e.Yes("device[\"RB-LT-001\"].app[\"Notepad++\"] == Available");
            e.S.Type("RB-LT-001", "#portal install Notepad++");
            e.Yes("device[\"RB-LT-001\"].app[\"Notepad++\"] == Installed");
            // uninstall
            e.Do("assign app 7-Zip \"Windows devices\" intent=uninstall");
            e.Sync("RB-LT-001");
            e.Yes("device[\"RB-LT-001\"].app[\"7-Zip\"] == \"Not installed\"");
            // store and Microsoft 365 apps
            e.Do("create app name=\"Company Portal\" type=\"Microsoft Store app (new)\" publisher=Microsoft storeId=\"Company Portal\" installBehavior=System");
            e.Yes("app[\"Company Portal\"].storeId == 9PRCL0000001");
            e.Do("assign app \"Company Portal\" \"All devices\"");
            e.Do("create app name=\"Microsoft 365 Apps\" type=\"Microsoft 365 Apps (Windows 10 and later)\" publisher=Microsoft suiteApps=\"Word,Excel,Outlook,Teams\" channel=\"Monthly Enterprise Channel\"");
            e.Do("assign app \"Microsoft 365 Apps\" \"Windows devices\"");
            e.Sync("RB-LT-001");
            e.Yes("device[\"RB-LT-001\"].app[\"Company Portal\"] == Installed");
            e.Yes("device[\"RB-LT-001\"].app[\"Microsoft 365 Apps\"] == Installed");
            Check("Word is on the device", e.T.MachineFor(e.D("RB-LT-001")).FindFile("C:\\Program Files\\Microsoft Office\\root\\Office16\\WINWORD.EXE") != null);
            e.S.Dispose();
        }

        static void EpCaTests()
        {
            var e = new E();
            Func<string, string> sign = a => { string o; EpCheck.Line(e.T, "signin(" + a + ") == x", out o); return o; };
            Check("no policies: allowed", sign("user=jordan, device=RB-LT-001") == "Allowed");
            e.Do("create ca name=\"MFA for everyone\" usersInclude=\"All users\" usersExclude=breakglass apps=\"All cloud apps\" grant=\"Grant access\" controls=\"Require multifactor authentication\" state=On");
            Check("MFA policy: MFA prompt", sign("user=jordan, device=RB-LT-001") == "Allowed after MFA", sign("user=jordan, device=RB-LT-001"));
            Check("MFA done: allowed", sign("user=jordan, device=RB-LT-001, mfa=yes") == "Allowed");
            Check("the emergency account is excluded", sign("user=breakglass") == "Allowed");
            e.Do("create ca name=\"Compliant devices for Office\" usersInclude=Sales apps=\"Office 365\" grant=\"Grant access\" controls=\"Require device to be marked as compliant\" state=On");
            Check("compliant device: allowed after MFA", sign("user=jordan, device=RB-LT-001, app=\"Office 365 Exchange Online\"") == "Allowed after MFA");
            Check("a personal unmanaged PC is blocked", sign("user=priya, device=SAM-HOME-PC, mfa=yes") == "Blocked");
            Check("Finance isn't in the policy", sign("user=sam, device=SAM-HOME-PC, mfa=yes") == "Allowed");
            Check("another app isn't targeted", sign("user=priya, device=SAM-HOME-PC, mfa=yes, app=\"Microsoft Azure Management\"") == "Allowed");
            e.Do("device RB-LT-001 compliance=\"Not compliant\"");
            Check("a noncompliant device is blocked", sign("user=jordan, device=RB-LT-001, mfa=yes") == "Blocked");
            e.Do("device RB-LT-001 compliance=\"In grace period\"");
            Check("in grace period counts as compliant", sign("user=jordan, device=RB-LT-001, mfa=yes") == "Allowed");
            // report-only and block legacy authentication
            e.Do("create ca name=\"Block legacy\" usersInclude=\"All users\" apps=\"All cloud apps\" clientApps=\"Exchange ActiveSync clients,Other clients\" grant=\"Block access\" state=Report-only");
            Check("report-only doesn't block", sign("user=sam, client=eas, mfa=yes") == "Allowed");
            e.Do("set ca \"Block legacy\" state=On");
            Check("legacy clients are blocked", sign("user=sam, client=eas, mfa=yes") == "Blocked");
            Check("browsers aren't legacy", sign("user=sam, client=browser, mfa=yes") == "Allowed");
            // trusted locations
            e.Do("create location name=\"Head office\" ranges=203.0.113.0/24 trusted=Yes");
            e.Do("set ca \"MFA for everyone\" locationsExclude=\"All trusted locations\"");
            Check("no MFA from the trusted office", sign("user=sam, location=\"Head office\"") == "Allowed");
            Check("MFA elsewhere", sign("user=sam, location=Other") == "Allowed after MFA");
            // app protection on phones
            e.Do("create ca name=\"Protected apps on phones\" usersInclude=Sales apps=\"Office 365\" platforms=\"iOS,Android\" grant=\"Grant access\" controls=\"Require app protection policy\" state=On");
            Check("no app protection policy: blocked", sign("user=jordan, device=\"Jordan's iPhone\", client=mobile, clientApp=\"Microsoft Outlook\", mfa=yes") == "Blocked");
            e.Do("create appProtection name=\"Protect Outlook\" platform=iOS/iPadOS targetApps=\"Microsoft Outlook,Microsoft Teams\"");
            e.Do("assign appProtection \"Protect Outlook\" Sales");
            e.Bad("assign appProtection \"Protect Outlook\" \"Pilot devices\"", "user groups");
            e.Do("set ca \"Compliant devices for Office\" platformsExclude=\"iOS,Android\"");
            Check("with app protection: allowed", sign("user=jordan, device=\"Jordan's iPhone\", client=mobile, clientApp=\"Microsoft Outlook\", mfa=yes") == "Allowed", sign("user=jordan, device=\"Jordan's iPhone\", client=mobile, clientApp=\"Microsoft Outlook\", mfa=yes"));
            // hybrid or compliant
            e.Do("create ca name=\"Finance devices\" usersInclude=Finance apps=\"Riverbend Expenses (web app)\" grant=\"Grant access\" controls=\"Require device to be marked as compliant,Require Microsoft Entra hybrid joined device\" controlsOp=\"Require one of the selected controls\" state=On");
            Check("hybrid joined desktop: allowed", sign("user=sam, device=RB-DT-010, mfa=yes, app=\"Riverbend Expenses (web app)\"") == "Allowed");
            Check("home PC: blocked", sign("user=sam, device=SAM-HOME-PC, mfa=yes, app=\"Riverbend Expenses (web app)\"") == "Blocked");
            var r = e.T.EvaluateSignIn(new EpSignIn { User = "sam", Device = "SAM-HOME-PC", App = "Riverbend Expenses (web app)", Mfa = true });
            Check("What If lists each policy with why", r.Lines.Any(l => l.Policy == "Finance devices" && l.Result == "Block") && r.Lines.Any(l => l.Policy == "Protected apps on phones" && l.Applies == "Not applied"));
            // RBAC
            e.Do("create group name=\"Help desk\" type=Security membership=Assigned members=nils");
            e.Do("create roleAssignment name=\"Sales help desk\" role=\"Help Desk Operator\" admins=\"Help desk\" scopeGroups=Sales");
            e.Yes("canmanage(nils, RB-LT-001) == yes");
            e.Yes("canmanage(nils, RB-DT-010) == no");
            e.Yes("canmanage(alex, RB-DT-010) == yes");
            e.S.Dispose();
        }
    }
}
