// Cert Lab - Hyper-V on HV1: virtual machines (generation 1 or 2, memory - static or dynamic -, processors and nested
// virtualization, disks, network adapters with MAC address spoofing, checkpoints of the standard or production kind,
// automatic start / stop actions, integration services), virtual switches (external on a physical adapter, internal,
// private) and virtual hard disks (VHD / VHDX; fixed, dynamic or differencing). The rules are Hyper-V's: a generation
// can't change, nested virtualization and dynamic memory need the VM switched off and don't go together, a physical
// adapter carries one external switch, a VM starts only if the host has the memory.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public class WsVmNic { public string Name = "Network Adapter", Switch = "", Mac = "", Spoofing = "Off"; public int Vlan; }
    public class WsCheckpoint { public string Name = "", Parent = "", Type = "Production", VmState = "Off"; public DateTime Created; public long Memory; public int Cpus; public bool Nested; }
    public class WsVm
    {
        public string Name = "", State = "Off", Path = "", CheckpointType = "Production", StartAction = "StartIfRunning", StopAction = "Save", Version = "10.0", Id = "", Notes = "";
        public int Generation = 2, Cpus = 1;
        public long Memory = 1L << 30, MemMin = 512L << 20, MemMax = 1L << 40; public bool Dynamic, Nested, SecureBoot = true, Tpm;
        public readonly List<string> Disks = new List<string>();
        public readonly List<WsVmNic> Nics = new List<WsVmNic>();
        public readonly List<WsCheckpoint> Checkpoints = new List<WsCheckpoint>();
        public string Current = "";
        public readonly Dictionary<string, bool> Integration = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) { { "Guest Service Interface", false }, { "Heartbeat", true }, { "Key-Value Pair Exchange", true }, { "Shutdown", true }, { "Time Synchronization", true }, { "VSS", true } };
        public DateTime Started;
    }
    public class WsVSwitch { public string Name = "", Type = "Internal", Adapter = ""; public bool ManagementOs = true; }
    public class WsVhd { public string Path = "", Format = "VHDX", Type = "Dynamic", Parent = ""; public long Size, FileSize; }

    public partial class WsHvHost
    {
        public WsServer S;
        public readonly List<WsVm> Vms = new List<WsVm>();
        public readonly List<WsVSwitch> Switches = new List<WsVSwitch>();
        public readonly List<WsVhd> Vhds = new List<WsVhd>();
        public string VmPath = "D:\\Hyper-V", VhdPath = "D:\\Hyper-V\\Virtual Hard Disks";
        public bool EnhancedSession, NumaSpanning = true;
        public long HostMemory = 64L << 30;
        public bool Up { get { return S.Running && S.Has("Hyper-V") && S.SvcRunning("vmms"); } }
        public WsVm Vm(string n) { return Vms.FirstOrDefault(v => WsText.Eq(v.Name, n)); }
        public WsVSwitch Switch(string n) { return Switches.FirstOrDefault(v => WsText.Eq(v.Name, n)); }
        public WsVhd Vhd(string p) { return Vhds.FirstOrDefault(v => WsText.Eq(v.Path, p)); }
        public long MemoryInUse { get { return Vms.Where(v => v.State == "Running" || v.State == "Paused").Sum(v => v.Memory); } }
        int macs = 0x10;
        public string NewMac() { macs++; return "00155D0A1E" + macs.ToString("X2", CultureInfo.InvariantCulture); }

        public string NewVhd(string path, long size, string type, string parent, out WsVhd vhd)
        {
            vhd = null;
            if (Vhd(path) != null || S.M.FindFile(path) != null) return "The file " + path + " already exists.";
            string ext = WinPath.GetExtension(path).ToLowerInvariant();
            if (ext != ".vhd" && ext != ".vhdx") return "A virtual hard disk file ends in .vhd or .vhdx.";
            if (type == "Differencing") { var p = Vhd(parent); if (p == null) return "The parent disk " + parent + " doesn't exist."; size = p.Size; }
            if (size <= 0) return "Give the disk a size (-SizeBytes 60GB).";
            if (ext == ".vhd" && size > 2040L << 30) return "A VHD can be at most 2040 GB; use VHDX (up to 64 TB).";
            int slash = path.LastIndexOf('\\');
            if (slash < 3 || S.M.FindFile(path.Substring(0, slash)) == null) return "The folder " + (slash > 0 ? path.Substring(0, slash) : path) + " doesn't exist.";
            vhd = new WsVhd { Path = path, Format = ext == ".vhd" ? "VHD" : "VHDX", Type = type, Parent = parent ?? "", Size = size, FileSize = type == "Fixed" ? size : 4L << 20 };
            Vhds.Add(vhd);
            var f = S.M.File(path, "", S.Lab.Now); f.FakeSize = vhd.FileSize;
            return null;
        }

        public string NewVm(string name, int gen, long mem, string vhdPath, long newVhdSize, string sw, out WsVm vm)
        {
            vm = null;
            if (Vm(name) != null) return "A virtual machine named " + name + " already exists.";
            if (gen != 1 && gen != 2) return "The generation must be 1 or 2.";
            if (!string.IsNullOrEmpty(sw) && Switch(sw) == null) return "The virtual switch " + sw + " doesn't exist.";
            vm = new WsVm { Name = name, Generation = gen, Memory = mem > 0 ? mem : (gen == 2 ? 1L << 30 : 512L << 20), Path = VmPath, Id = S.Lab.NewGuid(), SecureBoot = gen == 2 };
            if (newVhdSize > 0)
            {
                WsVhd v; string err = NewVhd(vhdPath, newVhdSize, "Dynamic", null, out v);
                if (err != null) { vm = null; return err; }
            }
            else if (!string.IsNullOrEmpty(vhdPath) && Vhd(vhdPath) == null) { vm = null; return "The virtual hard disk " + vhdPath + " doesn't exist."; }
            if (!string.IsNullOrEmpty(vhdPath)) vm.Disks.Add(vhdPath);
            vm.Nics.Add(new WsVmNic { Switch = sw ?? "", Mac = NewMac() });
            Vms.Add(vm);
            return null;
        }

        public string Start(WsVm vm)
        {
            if (vm.State == "Running") return null;
            if (vm.State == "Paused") { vm.State = "Running"; return null; }
            if (vm.State != "Saved" && MemoryInUse + vm.Memory > HostMemory - (4L << 30)) return "'" + vm.Name + "' could not initialize: not enough memory in the system to start the virtual machine.";
            if (vm.Disks.Any(d => Vhd(d) == null)) return "'" + vm.Name + "' failed to start: a virtual hard disk is missing.";
            vm.State = "Running"; vm.Started = S.Lab.Now;
            return null;
        }
        public string Stop(WsVm vm, string how)
        {
            if (vm.State == "Off") return null;
            if (how == "save") { vm.State = "Saved"; return null; }
            if (how == "shutdown" && vm.State == "Running" && !vm.Integration["Shutdown"]) return "The virtual machine can't be shut down: the Shutdown integration service is off. Use -TurnOff.";
            vm.State = "Off";
            return null;
        }
        public string NeedOff(WsVm vm, string what) { return vm.State == "Off" ? null : "The operation cannot be performed while the virtual machine '" + vm.Name + "' is in its current state (" + vm.State + "): " + what + " needs the VM turned off."; }

        public string SetProcessor(WsVm vm, int count, bool? nested)
        {
            if (nested.HasValue && nested.Value != vm.Nested)
            {
                var e = NeedOff(vm, "changing nested virtualization"); if (e != null) return e;
                if (nested.Value && vm.Dynamic) return "Nested virtualization can't be turned on while the VM uses dynamic memory: turn dynamic memory off first (Set-VMMemory -DynamicMemoryEnabled $false).";
                vm.Nested = nested.Value;
            }
            if (count > 0 && count != vm.Cpus) { var e = NeedOff(vm, "changing the number of processors"); if (e != null) return e; if (count > S.M.Cpus * 4) return "The host doesn't have that many logical processors."; vm.Cpus = count; }
            return null;
        }
        public string SetMemory(WsVm vm, bool? dynamic, long startup, long min, long max)
        {
            if (dynamic.HasValue && dynamic.Value != vm.Dynamic)
            {
                var e = NeedOff(vm, "turning dynamic memory on or off"); if (e != null) return e;
                if (dynamic.Value && vm.Nested) return "Dynamic memory isn't supported on a VM with nested virtualization.";
                vm.Dynamic = dynamic.Value;
            }
            if (startup > 0 && startup != vm.Memory) { var e = NeedOff(vm, "changing the startup memory"); if (e != null) return e; vm.Memory = startup; }
            if (min > 0) vm.MemMin = min; if (max > 0) vm.MemMax = max;
            if (vm.Dynamic && (vm.MemMin > vm.Memory || vm.MemMax < vm.Memory)) return "With dynamic memory, minimum <= startup <= maximum.";
            return null;
        }

        public string Checkpoint(WsVm vm, string name)
        {
            if (vm.CheckpointType == "Disabled") return "Checkpoints are disabled for '" + vm.Name + "'.";
            if (string.IsNullOrEmpty(name)) name = vm.Name + " - (" + S.Lab.Now.ToString("M/d/yyyy - h:mm:ss tt", CultureInfo.InvariantCulture) + ")";
            if (vm.Checkpoints.Any(c => WsText.Eq(c.Name, name))) name += " (2)";
            string type = vm.CheckpointType == "Standard" ? "Standard" : vm.State == "Running" && !vm.Integration["VSS"] ? (vm.CheckpointType == "ProductionOnly" ? null : "Standard") : "Production";
            if (type == null) return "A production checkpoint couldn't be created (the guest's backup integration service is off) and the VM allows production checkpoints only.";
            vm.Checkpoints.Add(new WsCheckpoint { Name = name, Parent = vm.Current, Type = type, Created = S.Lab.Now, VmState = type == "Standard" ? vm.State : "Off", Memory = vm.Memory, Cpus = vm.Cpus, Nested = vm.Nested });
            vm.Current = name;
            return null;
        }
        public string Restore(WsVm vm, string name)
        {
            var c = vm.Checkpoints.FirstOrDefault(x => WsText.Eq(x.Name, name));
            if (c == null) return "There is no checkpoint '" + name + "' for '" + vm.Name + "'.";
            vm.State = c.VmState == "Running" ? "Saved" : c.VmState == "Saved" ? "Saved" : "Off";
            vm.Memory = c.Memory; vm.Cpus = c.Cpus; vm.Nested = c.Nested;
            vm.Current = c.Name;
            return null;
        }
        public string RemoveCheckpoint(WsVm vm, string name, bool tree)
        {
            var c = vm.Checkpoints.FirstOrDefault(x => WsText.Eq(x.Name, name));
            if (c == null) return "There is no checkpoint '" + name + "' for '" + vm.Name + "'.";
            var gone = new List<WsCheckpoint> { c };
            if (tree) { for (int i = 0; i < gone.Count; i++) gone.AddRange(vm.Checkpoints.Where(x => WsText.Eq(x.Parent, gone[i].Name) && !gone.Contains(x))); }
            foreach (var x in vm.Checkpoints.Where(k => WsText.Eq(k.Parent, c.Name) && !gone.Contains(k))) x.Parent = c.Parent;
            foreach (var g in gone) vm.Checkpoints.Remove(g);
            if (gone.Any(g => WsText.Eq(g.Name, vm.Current))) vm.Current = c.Parent;
            return null;
        }
    }
}
