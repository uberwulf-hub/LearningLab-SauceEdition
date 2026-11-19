// Cert Lab - Hyper-V module: checkpoints (Checkpoint-VM, Get / Restore / Remove-VMSnapshot, their -VMCheckpoint
// aliases), integration services, the host's settings and VM firmware.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public partial class PsCmdlets
    {
        static void RegWsHv3()
        {
            HvCmd("Checkpoint-VM", "Name:string[]@0 alias=VMName; VM:object[] pipe; SnapshotName:string@1", (c, o) =>
            {
                var h = Hv(c);
                foreach (var v in HvVms(c, h, c.Has("VM") ? c.Get("VM") : null, "Name")) { HvOk(c, h.Checkpoint(v, c.Str("SnapshotName")), v.Name); if (c.Sw("Passthru")) c.Out(CheckpointObj(v, v.Checkpoints.Last())); }
            });
            HvCmd("Get-VMSnapshot", "VMName:string[]@0; VM:object[] pipe; Name:string; SnapshotType:string", (c, o) =>
            {
                var h = Hv(c);
                foreach (var v in HvVms(c, h, c.Has("VM") ? c.Get("VM") : null, "VMName")) foreach (var k in v.Checkpoints.Where(x => !c.Has("Name") || WsText.Like(x.Name, c.Str("Name")))) c.Out(CheckpointObj(v, k));
            });
            var rst = HvCmd("Restore-VMSnapshot", "Name:string@0; VMName:string; VMSnapshot:object pipe", (c, o) =>
            {
                var h = Hv(c); string vmn = c.Str("VMName"), name = c.Str("Name");
                var po = PsConv.Base(c.Get("VMSnapshot")) as PsObj; if (po != null) { vmn = PsConv.ToStr(po.Get("VMName")); name = PsConv.ToStr(po.Get("Name")); }
                var v = h.Vm(vmn ?? ""); if (v == null) HvOk(c, "Hyper-V was unable to find a virtual machine with name \"" + vmn + "\".", vmn);
                if (!c.ShouldProcess(name, "Apply checkpoint")) return;
                HvOk(c, h.Restore(v, name), name);
            });
            rst.ShouldProcess = true; rst.ConfirmImpact = "High";
            var rmk = HvCmd("Remove-VMSnapshot", "VMName:string[]@0; Name:string@1; VMSnapshot:object pipe; IncludeAllChildSnapshots:switch", (c, o) =>
            {
                var h = Hv(c);
                var po = PsConv.Base(c.Get("VMSnapshot")) as PsObj;
                string vmn = po != null ? PsConv.ToStr(po.Get("VMName")) : c.Strs("VMName")[0], name = po != null ? PsConv.ToStr(po.Get("Name")) : c.Str("Name");
                var v = h.Vm(vmn); if (v == null) HvOk(c, "Hyper-V was unable to find a virtual machine with name \"" + vmn + "\".", vmn);
                foreach (var k in v.Checkpoints.Where(x => WsText.Like(x.Name, name ?? "*")).ToList()) if (v.Checkpoints.Contains(k)) HvOk(c, h.RemoveCheckpoint(v, k.Name, c.Sw("IncludeAllChildSnapshots")), k.Name);
            });
            rmk.ShouldProcess = true;
            HvCmd("Get-VMIntegrationService", "VMName:string[]@0; VM:object[] pipe; Name:string[]", (c, o) =>
            {
                var h = Hv(c);
                foreach (var v in HvVms(c, h, c.Has("VM") ? c.Get("VM") : null, "VMName"))
                    foreach (var kv in v.Integration.Where(x => !c.Has("Name") || c.Strs("Name").Any(n => WsText.Like(x.Key, n))))
                    { var p = new PsObj("Microsoft.HyperV.PowerShell.VMIntegrationComponent", "System.Object"); p.Val("VMName", "string", v.Name); p.Val("Name", "string", kv.Key); p.Val("Enabled", "bool", kv.Value); p.Val("PrimaryStatusDescription", "string", kv.Value && v.State == "Running" ? "OK" : ""); p.Val("SecondaryStatusDescription", "string", ""); c.Out(p); }
            });
            foreach (var verb in new[] { "Enable", "Disable" })
            {
                bool on = verb == "Enable";
                HvCmd(verb + "-VMIntegrationService", "Name:string[]@0 !; VMName:string[]@1", (c, o) =>
                {
                    var h = Hv(c);
                    foreach (var v in HvVms(c, h, null, "VMName")) foreach (var n in c.Strs("Name")) { var k = v.Integration.Keys.FirstOrDefault(x => WsText.Eq(x, n)); if (k == null) HvOk(c, "There is no integration service \"" + n + "\".", n); v.Integration[k] = on; }
                });
            }
            HvCmd("Get-VMHost", "", (c, o) =>
            {
                var h = Hv(c); var p = new PsObj("Microsoft.HyperV.PowerShell.VMHost", "System.Object");
                p.Val("Name", "string", h.S.Name); p.Val("LogicalProcessorCount", "int", h.S.M.Cpus); p.Val("MemoryCapacity", "long", h.HostMemory); p.Val("VirtualMachinePath", "string", h.VmPath); p.Val("VirtualHardDiskPath", "string", h.VhdPath);
                p.Val("EnableEnhancedSessionMode", "bool", h.EnhancedSession); p.Val("NumaSpanningEnabled", "bool", h.NumaSpanning); p.Val("VirtualMachineMigrationEnabled", "bool", false);
                c.Out(p);
            });
            HvCmd("Set-VMHost", "VirtualMachinePath:string; VirtualHardDiskPath:string; EnableEnhancedSessionMode:bool; NumaSpanningEnabled:bool", (c, o) =>
            {
                var h = Hv(c);
                foreach (var n in new[] { "VirtualMachinePath", "VirtualHardDiskPath" }) if (c.Has(n) && c.M.FindFile(c.R.FullFsPath(c.Str(n), null)) == null) HvOk(c, "The path " + c.Str(n) + " doesn't exist.", c.Str(n));
                if (c.Has("VirtualMachinePath")) h.VmPath = c.R.FullFsPath(c.Str("VirtualMachinePath"), null);
                if (c.Has("VirtualHardDiskPath")) h.VhdPath = c.R.FullFsPath(c.Str("VirtualHardDiskPath"), null);
                if (c.Has("EnableEnhancedSessionMode")) h.EnhancedSession = PsConv.ToBool(c.Get("EnableEnhancedSessionMode"));
                if (c.Has("NumaSpanningEnabled")) h.NumaSpanning = PsConv.ToBool(c.Get("NumaSpanningEnabled"));
            });
            HvCmd("Get-VMFirmware", "VMName:string[]@0; VM:object[] pipe", (c, o) =>
            {
                var h = Hv(c);
                foreach (var v in HvVms(c, h, c.Has("VM") ? c.Get("VM") : null, "VMName"))
                {
                    if (v.Generation == 1) HvOk(c, "Generation 1 virtual machines use BIOS, not UEFI firmware: Get-VMFirmware works on generation 2 VMs.", v.Name);
                    var p = new PsObj("Microsoft.HyperV.PowerShell.VMFirmware", "System.Object"); p.Val("VMName", "string", v.Name); p.Val("SecureBoot", "string", v.SecureBoot ? "On" : "Off"); p.Val("SecureBootTemplate", "string", "MicrosoftWindows"); p.Val("PreferredNetworkBootProtocol", "string", "IPv4"); p.Val("BootOrder", "object[]", new object[0]);
                    p.DefaultDisplay = new[] { "VMName", "SecureBoot", "PreferredNetworkBootProtocol", "BootOrder" }; c.Out(p);
                }
            });
            HvCmd("Set-VMFirmware", "VMName:string[]@0; EnableSecureBoot:string set=On|Off; SecureBootTemplate:string", (c, o) =>
            {
                var h = Hv(c);
                foreach (var v in HvVms(c, h, null, "VMName")) { if (v.Generation == 1) HvOk(c, "Generation 1 virtual machines have no UEFI firmware settings.", v.Name); HvOk(c, h.NeedOff(v, "changing Secure Boot"), v.Name); if (c.Has("EnableSecureBoot")) v.SecureBoot = WsText.Eq(c.Str("EnableSecureBoot"), "On"); }
            });
        }
    }
}
