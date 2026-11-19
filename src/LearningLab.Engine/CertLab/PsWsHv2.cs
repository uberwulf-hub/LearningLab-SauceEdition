// Cert Lab - Hyper-V module: virtual switches, network adapters, virtual hard disks and drives, checkpoints,
// integration services, the host and VM firmware.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public partial class PsCmdlets
    {
        static PsObj SwitchObj(WsHvHost h, WsVSwitch s)
        {
            var p = new PsObj("Microsoft.HyperV.PowerShell.VMSwitch", "System.Object");
            var ad = s.Adapter.Length > 0 ? h.S.M.AdapterByAlias(s.Adapter) : null;
            p.Val("Name", "string", s.Name); p.Val("SwitchType", "Microsoft.HyperV.PowerShell.VMSwitchType", s.Type); p.Val("NetAdapterInterfaceDescription", "string", ad == null ? "" : ad.Description + (s.Adapter.EndsWith("2", StringComparison.Ordinal) ? " #2" : ""));
            p.Val("AllowManagementOS", "bool", s.ManagementOs); p.Val("NetAdapterName", "string", s.Adapter);
            p.DefaultDisplay = new[] { "Name", "SwitchType", "NetAdapterInterfaceDescription" };
            return p;
        }
        static PsObj VhdObj(WsHvHost h, WsVhd v)
        {
            var p = new PsObj("Microsoft.Vhd.PowerShell.VirtualHardDisk", "System.Object");
            p.Val("ComputerName", "string", h.S.Name); p.Val("Path", "string", v.Path); p.Val("VhdFormat", "Microsoft.Vhd.PowerShell.VhdFormat", v.Format); p.Val("VhdType", "Microsoft.Vhd.PowerShell.VhdType", v.Type);
            p.Val("FileSize", "uint64", (ulong)v.FileSize); p.Val("Size", "uint64", (ulong)v.Size); p.Val("MinimumSize", "System.Nullable[uint64]", null); p.Val("LogicalSectorSize", "uint32", 512u); p.Val("PhysicalSectorSize", "uint32", v.Format == "VHDX" ? 4096u : 512u);
            p.Val("BlockSize", "uint32", v.Type == "Fixed" ? 0u : 33554432u); p.Val("ParentPath", "string", v.Parent); p.Val("FragmentationPercentage", "System.Nullable[uint32]", 0u); p.Val("Attached", "bool", h.Vms.Any(m => m.Disks.Contains(v.Path, StringComparer.OrdinalIgnoreCase) && m.State != "Off"));
            return p;
        }
        static PsObj CheckpointObj(WsVm vm, WsCheckpoint k)
        {
            var p = new PsObj("Microsoft.HyperV.PowerShell.VMSnapshot", "System.Object");
            p.Val("VMName", "string", vm.Name); p.Val("Name", "string", k.Name); p.Val("SnapshotType", "Microsoft.HyperV.PowerShell.SnapshotType", k.Type); p.Val("CreationTime", "datetime", k.Created); p.Val("ParentSnapshotName", "string", k.Parent);
            p.DefaultDisplay = new[] { "VMName", "Name", "SnapshotType", "CreationTime", "ParentSnapshotName" };
            return p;
        }

        static void RegWsHv2()
        {
            HvCmd("Get-VMSwitch", "Name:string[]@0; SwitchType:string set=External|Internal|Private", (c, o) => { var h = Hv(c); foreach (var s in h.Switches.Where(x => (!c.Has("Name") || c.Strs("Name").Any(n => WsText.Like(x.Name, n))) && (!c.Has("SwitchType") || WsText.Eq(x.Type, c.Str("SwitchType"))))) c.Out(SwitchObj(h, s)); });
            HvCmd("New-VMSwitch", "Name:string@0 !; NetAdapterName:string; AllowManagementOS:bool; SwitchType:string set=Internal|Private|External; EnableEmbeddedTeaming:bool", (c, o) =>
            {
                var h = Hv(c);
                if (c.Has("NetAdapterName") && c.Has("SwitchType")) HvOk(c, "Parameter set cannot be resolved: -NetAdapterName makes an external switch; don't give -SwitchType with it.", c.Str("Name"));
                if (!c.Has("NetAdapterName") && !c.Has("SwitchType")) HvOk(c, "Give -NetAdapterName (an external switch) or -SwitchType Internal or Private.", c.Str("Name"));
                HvOk(c, h.NewSwitch(c.Str("Name"), c.Str("SwitchType"), c.Str("NetAdapterName"), !c.Has("AllowManagementOS") || PsConv.ToBool(c.Get("AllowManagementOS"))), c.Str("Name"));
                c.Out(SwitchObj(h, h.Switch(c.Str("Name"))));
            });
            var rsw = HvCmd("Remove-VMSwitch", "Name:string[]@0 !; Force:switch", (c, o) => { var h = Hv(c); foreach (var n in c.Strs("Name")) { if (!c.Sw("Force") && !c.ShouldProcess(n, "Remove virtual switch")) continue; HvOk(c, h.RemoveSwitch(n), n); } });
            rsw.ShouldProcess = true; rsw.ConfirmImpact = "High";
            HvCmd("Get-VMNetworkAdapter", "VMName:string[]@0; VM:object[] pipe; ManagementOS:switch; All:switch", (c, o) =>
            {
                var h = Hv(c);
                if (c.Sw("ManagementOS")) { foreach (var s in h.Switches.Where(x => x.ManagementOs)) { var p = new PsObj("Microsoft.HyperV.PowerShell.VMNetworkAdapter", "System.Object"); p.Val("Name", "string", s.Name); p.Val("IsManagementOs", "bool", true); p.Val("VMName", "string", ""); p.Val("SwitchName", "string", s.Name); p.Val("MacAddress", "string", "00155D0A1E01"); p.Val("Status", "string", "{Ok}"); p.Val("IPAddresses", "string[]", new object[0]); c.Out(p); } return; }
                foreach (var v in HvVms(c, h, c.Has("VM") ? c.Get("VM") : null, "VMName"))
                    foreach (var n in v.Nics) { var p = new PsObj("Microsoft.HyperV.PowerShell.VMNetworkAdapter", "System.Object"); p.Val("Name", "string", n.Name); p.Val("IsManagementOs", "bool", false); p.Val("VMName", "string", v.Name); p.Val("SwitchName", "string", n.Switch); p.Val("MacAddress", "string", n.Mac); p.Val("Status", "string", v.State == "Running" ? "{Ok}" : ""); p.Val("IPAddresses", "string[]", new object[0]); p.Val("MacAddressSpoofing", "string", n.Spoofing); p.Val("VlanId", "int", n.Vlan); p.DefaultDisplay = new[] { "Name", "IsManagementOs", "VMName", "SwitchName", "MacAddress", "Status", "IPAddresses" }; c.Out(p); }
            });
            HvCmd("Connect-VMNetworkAdapter", "VMName:string[]@0; Name:string; SwitchName:string@1 !", (c, o) =>
            {
                var h = Hv(c); var sw = h.Switch(c.Str("SwitchName"));
                if (sw == null) HvOk(c, "Hyper-V was unable to find a virtual switch with name \"" + c.Str("SwitchName") + "\".", c.Str("SwitchName"));
                foreach (var v in HvVms(c, h, null, "VMName")) foreach (var n in v.Nics.Where(x => !c.Has("Name") || WsText.Eq(x.Name, c.Str("Name")))) n.Switch = sw.Name;
            });
            HvCmd("Disconnect-VMNetworkAdapter", "VMName:string[]@0; Name:string", (c, o) => { var h = Hv(c); foreach (var v in HvVms(c, h, null, "VMName")) foreach (var n in v.Nics.Where(x => !c.Has("Name") || WsText.Eq(x.Name, c.Str("Name")))) n.Switch = ""; });
            HvCmd("Set-VMNetworkAdapter", "VMName:string[]@0; Name:string; MacAddressSpoofing:string set=On|Off; StaticMacAddress:string; DhcpGuard:string set=On|Off; RouterGuard:string set=On|Off", (c, o) =>
            {
                var h = Hv(c);
                foreach (var v in HvVms(c, h, null, "VMName")) foreach (var n in v.Nics.Where(x => !c.Has("Name") || WsText.Eq(x.Name, c.Str("Name")))) { if (c.Has("MacAddressSpoofing")) n.Spoofing = c.Str("MacAddressSpoofing"); if (c.Has("StaticMacAddress")) n.Mac = c.Str("StaticMacAddress"); }
            });
            HvCmd("Add-VMNetworkAdapter", "VMName:string[]@0; Name:string; SwitchName:string", (c, o) =>
            {
                var h = Hv(c);
                foreach (var v in HvVms(c, h, null, "VMName")) { if (v.State != "Off" && v.Generation == 1) HvOk(c, h.NeedOff(v, "adding a network adapter to a generation 1 VM"), v.Name); v.Nics.Add(new WsVmNic { Name = c.Str("Name") ?? "Network Adapter", Switch = c.Str("SwitchName") ?? "", Mac = h.NewMac() }); }
            });
            HvCmd("New-VHD", "Path:string[]@0 !; SizeBytes:long@1; Dynamic:switch; Fixed:switch; Differencing:switch; ParentPath:string; BlockSizeBytes:uint32", (c, o) =>
            {
                var h = Hv(c);
                foreach (var p0 in c.Strs("Path"))
                {
                    string path = c.R.FullFsPath(p0, c.R.CurrentFsDir); WsVhd v;
                    string type = c.Has("ParentPath") || c.Sw("Differencing") ? "Differencing" : c.Sw("Fixed") ? "Fixed" : "Dynamic";
                    HvOk(c, h.NewVhd(path, c.Long("SizeBytes", 0), type, c.Has("ParentPath") ? c.R.FullFsPath(c.Str("ParentPath"), c.R.CurrentFsDir) : null, out v), path);
                    c.Out(VhdObj(h, v));
                }
            });
            HvCmd("Get-VHD", "Path:string[]@0 pipe; VMId:string", (c, o) =>
            {
                var h = Hv(c);
                foreach (var p0 in c.Strs("Path") ?? new string[0]) { var v = h.Vhd(c.R.FullFsPath(p0, c.R.CurrentFsDir)); if (v == null) HvOk(c, "Getting the mounted storage instance for the path '" + p0 + "' failed: the file doesn't exist.", p0); c.Out(VhdObj(h, v)); }
            });
            HvCmd("Resize-VHD", "Path:string[]@0 !; SizeBytes:long@1; ToMinimumSize:switch", (c, o) =>
            {
                var h = Hv(c);
                foreach (var p0 in c.Strs("Path"))
                {
                    var v = h.Vhd(c.R.FullFsPath(p0, c.R.CurrentFsDir)); if (v == null) HvOk(c, "The file " + p0 + " doesn't exist.", p0);
                    var vm = h.Vms.FirstOrDefault(m => m.Disks.Contains(v.Path, StringComparer.OrdinalIgnoreCase) && m.State != "Off");
                    if (vm != null && (v.Format != "VHDX" || vm.Generation == 1)) HvOk(c, "The disk is attached to the running VM " + vm.Name + ": only a VHDX on a SCSI controller (generation 2) can be resized online.", v.Path);
                    long ns = c.Long("SizeBytes", 0);
                    if (ns < v.Size && v.Format != "VHDX") HvOk(c, "Only a VHDX can be shrunk.", v.Path);
                    v.Size = ns; if (v.Type == "Fixed") v.FileSize = ns;
                }
            });
            HvCmd("Convert-VHD", "Path:string@0 !; DestinationPath:string@1 !; VHDType:string set=Fixed|Dynamic|Differencing; DeleteSource:switch", (c, o) =>
            {
                var h = Hv(c); var v = h.Vhd(c.R.FullFsPath(c.Str("Path"), c.R.CurrentFsDir)); if (v == null) HvOk(c, "The file " + c.Str("Path") + " doesn't exist.", c.Str("Path"));
                if (h.Vms.Any(m => m.Disks.Contains(v.Path, StringComparer.OrdinalIgnoreCase) && m.State != "Off")) HvOk(c, "The disk is in use by a running VM.", v.Path);
                WsVhd n; HvOk(c, h.NewVhd(c.R.FullFsPath(c.Str("DestinationPath"), c.R.CurrentFsDir), v.Size, c.Str("VHDType") ?? v.Type, null, out n), c.Str("DestinationPath"));
                if (c.Sw("DeleteSource")) h.Vhds.Remove(v);
                c.Out(VhdObj(h, n));
            });
            HvCmd("Add-VMHardDiskDrive", "VMName:string[]@0; Path:string@1; ControllerType:string set=IDE|SCSI; ControllerNumber:int; ControllerLocation:int", (c, o) =>
            {
                var h = Hv(c); string path = c.R.FullFsPath(c.Str("Path"), c.R.CurrentFsDir);
                if (h.Vhd(path) == null) HvOk(c, "The file " + path + " doesn't exist.", path);
                foreach (var v in HvVms(c, h, null, "VMName")) { if (v.Generation == 2 && WsText.Eq(c.Str("ControllerType"), "IDE")) HvOk(c, "A generation 2 VM has no IDE controller: use SCSI.", v.Name); if (v.State != "Off" && (v.Generation == 1 || WsText.Eq(c.Str("ControllerType"), "IDE"))) HvOk(c, h.NeedOff(v, "adding an IDE disk"), v.Name); v.Disks.Add(path); }
            });
            HvCmd("Get-VMHardDiskDrive", "VMName:string[]@0; VM:object[] pipe", (c, o) =>
            {
                var h = Hv(c);
                foreach (var v in HvVms(c, h, c.Has("VM") ? c.Get("VM") : null, "VMName")) for (int i = 0; i < v.Disks.Count; i++) { var p = new PsObj("Microsoft.HyperV.PowerShell.HardDiskDrive", "System.Object"); p.Val("VMName", "string", v.Name); p.Val("ControllerType", "string", v.Generation == 1 && i < 2 ? "IDE" : "SCSI"); p.Val("ControllerNumber", "int", 0); p.Val("ControllerLocation", "int", i); p.Val("DiskNumber", "System.Nullable[uint32]", null); p.Val("Path", "string", v.Disks[i]); c.Out(p); }
            });
            RegWsHv3();
        }
    }
}
