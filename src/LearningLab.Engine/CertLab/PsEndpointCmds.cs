// Cert Lab - PowerShell commands for the MD-102 course's pretend devices: BitLocker (Get-BitLockerVolume), Defender
// (Get-MpComputerStatus, Get-MpPreference, Start-MpScan, Update-MpSignature), the TPM and Secure Boot, provisioning
// packages, Windows LAPS (Invoke-LapsPolicyProcessing, Reset-LapsPassword) and Get-WindowsAutopilotInfo (a script
// from the PowerShell Gallery on a real PC; Cert Lab has it ready). They read the tenant's view of the device
// (PsMachine.EpDev); on machines of the PowerShell course they show a plain, unmanaged PC.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public partial class PsCmdlets
    {
        static EpDevice Dev(CmdCtx c) { return c.M.EpDev as EpDevice; }
        static string Prop(EpDevice d, string k, string def) { return d == null || d[k].Length == 0 ? def : d[k]; }

        static partial void RegisterEndpoint()
        {
            PsFormat.Table("Microsoft.BitLocker.Structures.BitLockerVolume", PsFormat.P("VolumeType", 15), PsFormat.P("MountPoint", 10), PsFormat.PR("CapacityGB", 10), PsFormat.P("VolumeStatus", 22), PsFormat.PR("EncryptionPercentage", 20), PsFormat.P("ProtectionStatus", 16));
            Add("Get-BitLockerVolume", "BitLocker", "MountPoint:string[]@0 pipe", (c, o) =>
            {
                var d = Dev(c);
                string st = Prop(d, "bitlocker", "Off");
                var v = new PsObj("Microsoft.BitLocker.Structures.BitLockerVolume", "System.Object");
                v.Val("ComputerName", "string", c.M.Name).Val("MountPoint", "string", "C:");
                v.Val("EncryptionMethod", "Microsoft.BitLocker.Structures.BitLockerVolumeEncryptionMethodOnGet", st == "Off" ? "None" : Prop(d, "bitlockerMethod", "XTS-AES 128-bit").Contains("256") ? "XtsAes256" : "XtsAes128");
                v.Val("AutoUnlockEnabled", "System.Nullable[bool]", null).Val("AutoUnlockKeyStored", "System.Nullable[bool]", null).Val("MetadataVersion", "int", 2);
                v.Val("VolumeStatus", "Microsoft.BitLocker.Structures.BitLockerVolumeStatus", st == "On" ? "FullyEncrypted" : st == "Encrypting" ? "EncryptionInProgress" : "FullyDecrypted");
                v.Val("ProtectionStatus", "Microsoft.BitLocker.Structures.BitLockerVolumeProtectionStatus", st == "On" ? "On" : "Off");
                v.Val("LockStatus", "Microsoft.BitLocker.Structures.BitLockerVolumeLockStatus", "Unlocked");
                v.Val("EncryptionPercentage", "single", st == "On" ? 100.0 : st == "Encrypting" ? 47.3 : 0.0).Val("WipePercentage", "single", 0.0);
                v.Val("VolumeType", "Microsoft.BitLocker.Structures.BitLockerVolumeType", "OperatingSystem");
                v.Val("CapacityGB", "single", 237.32);
                v.Val("KeyProtector", "System.Collections.ObjectModel.ReadOnlyCollection[Microsoft.BitLocker.Structures.BitLockerVolumeKeyProtector]", st == "Off" ? new object[0] : new object[] { "Tpm", "RecoveryPassword" });
                c.Out(v);
            });
            Add("Get-MpComputerStatus", "Defender", "", (c, o) =>
            {
                var d = Dev(c);
                int age = d == null ? 0 : d.Int("sigAgeDays", 0);
                var s = new PsObj("Microsoft.Management.Infrastructure.CimInstance#ROOT/Microsoft/Windows/Defender/MSFT_MpComputerStatus", "Microsoft.Management.Infrastructure.CimInstance");
                s.Val("AMRunningMode", "string", "Normal").Val("AMServiceEnabled", "bool", true).Val("AntispywareEnabled", "bool", Prop(d, "av", "On") == "On");
                s.Val("AntispywareSignatureAge", "uint32", (uint)age).Val("AntivirusEnabled", "bool", Prop(d, "av", "On") == "On").Val("AntivirusSignatureAge", "uint32", (uint)age);
                s.Val("AntivirusSignatureLastUpdated", "datetime", c.M.Now.Date.AddDays(-age).AddHours(4)).Val("BehaviorMonitorEnabled", "bool", true);
                s.Val("ComputerState", "uint32", 0u).Val("DefenderSignaturesOutOfDate", "bool", age > 3).Val("IoavProtectionEnabled", "bool", true);
                s.Val("IsTamperProtected", "bool", Prop(d, "tamper", "On") != "Off").Val("NISEnabled", "bool", true).Val("OnAccessProtectionEnabled", "bool", Prop(d, "rtp", "On") == "On");
                s.Val("QuickScanAge", "uint32", 1u).Val("RealTimeProtectionEnabled", "bool", Prop(d, "rtp", "On") == "On").Val("TamperProtectionSource", "string", d != null && d["tamper"] == "On" ? "Intune" : "Signatures");
                c.Out(s);
            });
            Add("Get-MpPreference", "Defender", "", (c, o) =>
            {
                var d = Dev(c);
                var ids = new List<object>(); var acts = new List<object>();
                if (d != null) foreach (var r in EpCatalog.AsrRules) { var mode = d["asr:" + r.Key]; if (mode.Length == 0 || mode == "Not configured") continue; ids.Add(r.Guid); acts.Add((byte)(mode == "Block" ? 1 : mode == "Audit" ? 2 : mode == "Warn" ? 6 : 0)); }
                var p = new PsObj("Microsoft.Management.Infrastructure.CimInstance#ROOT/Microsoft/Windows/Defender/MSFT_MpPreference", "Microsoft.Management.Infrastructure.CimInstance");
                p.Val("AttackSurfaceReductionRules_Ids", "string[]", ids.ToArray()).Val("AttackSurfaceReductionRules_Actions", "byte[]", acts.ToArray());
                p.Val("DisableRealtimeMonitoring", "bool", Prop(d, "rtp", "On") != "On");
                string cfa = Prop(d, "cfa", "Disabled");
                p.Val("EnableControlledFolderAccess", "byte", (byte)(cfa == "Enabled" ? 1 : cfa == "Audit Mode" ? 2 : 0));
                p.Val("ExclusionPath", "string[]", d == null ? new object[0] : d.List("avExclusions").Cast<object>().ToArray());
                p.Val("MAPSReporting", "byte", (byte)(Prop(d, "cloud", "On") == "On" ? 2 : 0));
                string pua = Prop(d, "pua", "Off");
                p.Val("PUAProtection", "byte", (byte)(pua == "On" ? 1 : pua == "Audit" ? 2 : 0));
                p.Val("SignatureUpdateInterval", "uint32", 0u);
                c.Out(p);
            });
            Add("Start-MpScan", "Defender", "ScanType:string set=QuickScan|FullScan|CustomScan; ScanPath:string", (c, o) =>
            {
                var d = Dev(c); if (d != null) { d["lastScan"] = EpText.Date(c.M.Now); d.Note("Microsoft Defender Antivirus " + (c.Str("ScanType", "QuickScan") == "FullScan" ? "full" : "quick") + " scan from PowerShell: no threats found."); }
            });
            Add("Update-MpSignature", "Defender", "UpdateSource:string", (c, o) => { var d = Dev(c); if (d != null) d["sigAgeDays"] = "0"; });
            Add("Get-Tpm", "TrustedPlatformModule", "", (c, o) =>
            {
                var d = Dev(c);
                string v = Prop(d, "tpm", "2.0");
                var t = new PsObj("Microsoft.Tpm.Commands.TpmObject", "System.Object");
                bool has = v != "None";
                t.Val("TpmPresent", "bool", has).Val("TpmReady", "bool", has).Val("TpmEnabled", "bool", has).Val("TpmActivated", "bool", has).Val("TpmOwned", "bool", has);
                t.Val("RestartPending", "bool", false).Val("ManufacturerId", "uint32", 1229346816u).Val("ManufacturerIdTxt", "string", "INTC");
                t.Val("ManufacturerVersion", "string", v == "2.0" ? "600.18.0.0" : "5.81.2.1").Val("ManufacturerVersionFull20", "string", v == "2.0" ? "600.18.0.0" : "Not Supported");
                t.Val("ManagedAuthLevel", "string", "Full").Val("OwnerAuth", "string", null).Val("OwnerClearDisabled", "bool", true).Val("AutoProvisioning", "string", "Enabled");
                t.Val("LockedOut", "bool", false).Val("LockoutHealTime", "string", "10 minutes").Val("LockoutCount", "uint32", 0u).Val("LockoutMax", "uint32", 31u).Val("SelfTest", "byte[]", new object[0]);
                c.Out(t);
            });
            Add("Confirm-SecureBootUEFI", "SecureBoot", "", (c, o) => { var d = Dev(c); c.Out(d == null || d.Yes("secureBoot")); });
            Add("Install-ProvisioningPackage", "Provisioning", "PackagePath:string@0 ! alias=Path; ForceInstall:switch; QuietInstall:switch; LogsDirectoryPath:string", (c, o) =>
            {
                var d = Dev(c);
                if (d == null) throw c.Fail("Cert Lab: provisioning packages are part of the MD-102 course's devices.", "NotSimulated", "NotImplemented", null, "System.NotSupportedException");
                string path = c.R.FullFsPath(c.Str("PackagePath"), c.R.CurrentFsDir);
                string why = d.T.InstallPackage(d, path);
                if (why != null) throw c.Fail(why, "InstallProvisioningPackageFailed", "InvalidOperation", path, "System.InvalidOperationException");
                var p = new PsObj("Microsoft.Windows.Provisioning.ProvisioningPackage", "System.Object");
                p.Val("PackageId", "guid", c.M.NewGuid()).Val("PackageName", "string", WinPath.GetFileNameWithoutExtension(path)).Val("PackagePath", "string", path).Val("IsInstalled", "bool", true).Val("Rank", "uint32", 1u);
                p.DefaultDisplay = new[] { "PackageId", "PackageName", "PackagePath", "IsInstalled", "Rank" };
                c.Out(p);
            });
            Add("Invoke-LapsPolicyProcessing", "LAPS", "", (c, o) =>
            {
                var d = Dev(c);
                if (d == null) return;
                d.T.ApplyLaps(d);
                c.Verbose("LAPS policy processing finished: " + (d["laps"] == "On" ? "the password is current." : d["lapsWhy"]));
            });
            Add("Reset-LapsPassword", "LAPS", "", (c, o) =>
            {
                var d = Dev(c);
                if (d == null) return;
                if (d["laps"] != "On") throw c.Fail("The local admin password couldn't be reset: there is no working LAPS policy (" + (d["lapsWhy"].Length > 0 ? d["lapsWhy"] : "none assigned") + ").", "ResetLapsPasswordFailed", "InvalidOperation", null, "System.InvalidOperationException");
                d["laps.rotate"] = "Yes"; d.T.ApplyLaps(d);
            });
            Add("Get-WindowsAutopilotInfo", "WindowsAutopilotInfo", "OutputFile:string; GroupTag:string; AssignedUser:string; Append:switch; Online:switch", (c, o) =>
            {
                if (c.Sw("Online")) throw c.Fail("Cert Lab doesn't sign in anywhere: save the hash with -OutputFile and import the file in the practice portal.", "NoOnline", "NotImplemented", null, "System.NotSupportedException");
                var d = Dev(c);
                string serial = d == null ? c.M.SerialNumber : d["serial"];
                var rng = new Random(FwHash.Of(serial));
                var hash = new StringBuilder("T0Fy");
                const string b64 = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
                for (int i = 0; i < 116; i++) hash.Append(b64[rng.Next(64)]);
                string pid = c.M.ProductId ?? "";
                string tag = c.Str("GroupTag", "");
                if (c.Has("OutputFile"))
                {
                    string path = c.R.FullFsPath(c.Str("OutputFile"), c.R.CurrentFsDir);
                    string row = serial + "," + pid + "," + hash + (tag.Length > 0 || c.Has("GroupTag") ? "," + tag : "");
                    var f = c.M.FindFile(path);
                    string head = "Device Serial Number,Windows Product ID,Hardware Hash" + (c.Has("GroupTag") ? ",Group Tag" : "");
                    string text = c.Sw("Append") && f != null ? f.Text.TrimEnd('\r', '\n') + "\r\n" + row : head + "\r\n" + row;
                    var why = c.R.WriteFile(path, text + "\r\n", "ascii", false);
                    if (why != null) throw c.Fail(why, "WriteFailed", "WriteError", path, "System.IO.IOException");
                    return;
                }
                var r = new PsObj("System.Management.Automation.PSCustomObject", "System.Object");
                r.Val("Device Serial Number", "string", serial).Val("Windows Product ID", "string", pid).Val("Hardware Hash", "string", hash.ToString());
                if (tag.Length > 0) r.Val("Group Tag", "string", tag);
                c.Out(r);
            });
        }
    }
}
