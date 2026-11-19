// Cert Lab - the Hyper-V module on HV1: virtual machines (create, start, stop, settings, processor and nested
// virtualization, memory), virtual switches, network adapters (MAC address spoofing), virtual hard disks, checkpoints,
// integration services, the host's settings and firmware (Secure Boot). Rules and messages come from WsHvHost.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public partial class WsLab
    {
        public static readonly Dictionary<string, string> ModuleAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        { { "Get-VMCheckpoint", "Get-VMSnapshot" }, { "Restore-VMCheckpoint", "Restore-VMSnapshot" }, { "Remove-VMCheckpoint", "Remove-VMSnapshot" } };
    }

    public partial class PsCmdlets
    {
        const string HvMod = "Hyper-V";
        static Func<PsMachine, bool> HasHv = m => m.Ws != null && m.FeatureInstalled("Hyper-V");
        static CmdletDef HvCmd(string name, string spec, Action<CmdCtx, object> run) { var d = Add(name, HvMod, spec + (spec.Length > 0 ? "; " : "") + "ComputerName:string[]; Passthru:switch alias=PassThru", run); d.Available = HasHv; d.FqClass = "Microsoft.HyperV.PowerShell.Commands." + name.Replace("-", "") + "Command"; return d; }
        static WsHvHost Hv(CmdCtx c)
        {
            var s = c.Has("ComputerName") ? Lab(c).Server(c.Strs("ComputerName")[0]) : c.M.WsSrv;
            if (s == null || s.Hv == null || !s.Hv.Up) throw c.Fail("Hyper-V was unable to find a virtual machine management service on " + (s == null ? c.Str("ComputerName") : s.Name) + ".", "Unspecified", "NotSpecified", null, "Microsoft.HyperV.PowerShell.VirtualizationException");
            return s.Hv;
        }
        static void HvOk(CmdCtx c, string err, object target) { if (err != null) throw c.Fail(err, "InvalidState", "InvalidOperation", target, "Microsoft.HyperV.PowerShell.VirtualizationException"); }
        /// <summary>The VMs named by -Name / -VMName (wildcards) or piped VM objects.</summary>
        static List<WsVm> HvVms(CmdCtx c, WsHvHost h, object input, string param)
        {
            var res = new List<WsVm>();
            var po = PsConv.Base(input) as PsObj;
            if (po != null && po.Base is WsVm) { res.Add((WsVm)po.Base); return res; }
            var names = c.Strs(param) ?? (input != null ? new[] { PsConv.ToStr(PsConv.Base(input)) } : null);
            if (names == null) return h.Vms.ToList();
            foreach (var n in names)
            {
                var hit = h.Vms.Where(v => WsText.Like(v.Name, n)).ToList();
                if (hit.Count == 0) throw c.Fail("Hyper-V was unable to find a virtual machine with name \"" + n + "\".", "InvalidParameter", "InvalidArgument", n, "Microsoft.HyperV.PowerShell.VirtualizationException");
                res.AddRange(hit);
            }
            return res;
        }
        public static PsObj VmObj(WsHvHost h, WsVm v)
        {
            var p = new PsObj("Microsoft.HyperV.PowerShell.VirtualMachine", "Microsoft.HyperV.PowerShell.VirtualizationObject", "System.Object") { Base = v };
            bool on = v.State == "Running";
            p.Val("Name", "string", v.Name); p.Val("State", "Microsoft.HyperV.PowerShell.VMState", v.State); p.Val("CPUUsage", "int", on ? 2 : 0); p.Val("MemoryAssigned", "long", on ? v.Memory : 0L);
            p.Val("Uptime", "timespan", on ? h.S.Lab.Now - v.Started : TimeSpan.Zero); p.Val("Status", "string", "Operating normally"); p.Val("Version", "string", v.Version);
            p.Val("Generation", "int", v.Generation); p.Val("ProcessorCount", "long", (long)v.Cpus); p.Val("MemoryStartup", "long", v.Memory); p.Val("DynamicMemoryEnabled", "bool", v.Dynamic);
            p.Val("MemoryMinimum", "long", v.MemMin); p.Val("MemoryMaximum", "long", v.MemMax); p.Val("CheckpointType", "Microsoft.HyperV.PowerShell.CheckpointType", v.CheckpointType);
            p.Val("AutomaticStartAction", "string", v.StartAction); p.Val("AutomaticStopAction", "string", v.StopAction); p.Val("Path", "string", v.Path); p.Val("Notes", "string", v.Notes);
            p.Val("VMId", "guid", v.Id); p.Val("ComputerName", "string", h.S.Name); p.Val("ParentCheckpointName", "string", v.Current);
            p.DefaultDisplay = new[] { "Name", "State", "CPUUsage", "MemoryAssigned", "Uptime", "Status", "Version" };
            p.ToStr = x => "VirtualMachine (Name = '" + v.Name + "') [Id = '" + v.Id + "']";
            return p;
        }

        static partial void RegWsHv()
        {
            Func<object, WsVm> vmOf = x => { var po = PsConv.Base(x) as PsObj; return po == null ? null : po.Base as WsVm; };
            PsFormat.Table("Microsoft.HyperV.PowerShell.VirtualMachine", PsFormat.P("Name", 12), PsFormat.P("State", 7),
                new FmtCol { Header = "CPUUsage(%)", Width = 11, Right = true, Get = x => { var v = vmOf(x); return v != null && v.State == "Running" ? 2 : 0; } },
                new FmtCol { Header = "MemoryAssigned(M)", Width = 17, Right = true, Get = x => { var v = vmOf(x); return v != null && v.State == "Running" ? v.Memory >> 20 : 0L; } },
                new FmtCol { Header = "Uptime", Width = 16, Get = x => { var po = PsConv.Base(x) as PsObj; var t = po == null ? TimeSpan.Zero : (TimeSpan)po.Get("Uptime"); return new TimeSpan(t.Days, t.Hours, t.Minutes, t.Seconds).ToString("c", CultureInfo.InvariantCulture); } },
                PsFormat.P("Status", 18), PsFormat.P("Version", 7));
            HvCmd("Get-VM", "Name:string[]@0 alias=VMName", (c, o) => { var h = Hv(c); foreach (var v in HvVms(c, h, null, "Name").OrderBy(x => x.Name, PsConv.NameOrder)) c.Out(VmObj(h, v)); });
            HvCmd("New-VM", "Name:string@0 alias=VMName; Generation:int set=1|2; MemoryStartupBytes:long; NewVHDPath:string; NewVHDSizeBytes:long; VHDPath:string; SwitchName:string; Path:string; BootDevice:string; NoVHD:switch; Version:string", (c, o) =>
            {
                var h = Hv(c); WsVm v;
                string vhd = c.Str("NewVHDPath") ?? c.Str("VHDPath");
                if (vhd != null && !vhd.Contains("\\")) vhd = h.VhdPath + "\\" + vhd;
                HvOk(c, h.NewVm(c.Str("Name") ?? "New Virtual Machine", c.Int("Generation", 1), c.Long("MemoryStartupBytes", 0), vhd, c.Has("NewVHDPath") ? c.Long("NewVHDSizeBytes", 0) : 0, c.Str("SwitchName"), out v), c.Str("Name"));
                if (c.Has("NewVHDPath") && !c.Has("NewVHDSizeBytes")) HvOk(c, "-NewVHDPath needs -NewVHDSizeBytes.", vhd);
                if (c.Has("Path")) v.Path = c.Str("Path");
                c.Out(VmObj(h, v));
            });
            foreach (var verb in new[] { "Start", "Stop", "Suspend", "Resume", "Save", "Remove" })
            {
                string vb = verb;
                var d = HvCmd(verb + "-VM", "Name:string[]@0 alias=VMName; VM:object[] pipe; Force:switch; TurnOff:switch; Save:switch", (c, o) =>
                {
                    var h = Hv(c);
                    foreach (var v in HvVms(c, h, c.Has("VM") ? c.Get("VM") : null, "Name"))
                    {
                        string err = null;
                        if (vb == "Start") err = h.Start(v);
                        else if (vb == "Stop") err = h.Stop(v, c.Sw("TurnOff") ? "turnoff" : c.Sw("Save") ? "save" : "shutdown");
                        else if (vb == "Save") err = h.Stop(v, "save");
                        else if (vb == "Suspend") { if (v.State != "Running") err = "The virtual machine '" + v.Name + "' isn't running."; else v.State = "Paused"; }
                        else if (vb == "Resume") { if (v.State != "Paused") err = "The virtual machine '" + v.Name + "' isn't paused."; else v.State = "Running"; }
                        else if (vb == "Remove") { if (v.State != "Off") err = "The virtual machine '" + v.Name + "' must be turned off before it can be removed."; else if (c.Sw("Force") || c.ShouldProcess(v.Name, "Remove virtual machine")) h.Vms.Remove(v); }
                        HvOk(c, err, v.Name);
                        if (c.Sw("Passthru") && vb != "Remove") c.Out(VmObj(h, v));
                    }
                });
                if (verb == "Remove") { d.ShouldProcess = true; d.ConfirmImpact = "High"; }
            }
            HvCmd("Set-VM", "Name:string[]@0 alias=VMName; VM:object[] pipe; ProcessorCount:long; MemoryStartupBytes:long; DynamicMemory:switch; StaticMemory:switch; MemoryMinimumBytes:long; MemoryMaximumBytes:long; CheckpointType:string set=Disabled|Production|ProductionOnly|Standard; AutomaticStartAction:string set=Nothing|StartIfRunning|Start; AutomaticStopAction:string set=TurnOff|Save|ShutDown; Notes:string; NewVMName:string", (c, o) =>
            {
                var h = Hv(c);
                foreach (var v in HvVms(c, h, c.Has("VM") ? c.Get("VM") : null, "Name"))
                {
                    if (c.Has("ProcessorCount")) HvOk(c, h.SetProcessor(v, (int)c.Long("ProcessorCount", 0), null), v.Name);
                    if (c.Sw("DynamicMemory") || c.Sw("StaticMemory") || c.Has("MemoryStartupBytes") || c.Has("MemoryMinimumBytes") || c.Has("MemoryMaximumBytes"))
                        HvOk(c, h.SetMemory(v, c.Sw("DynamicMemory") ? (bool?)true : c.Sw("StaticMemory") ? (bool?)false : null, c.Long("MemoryStartupBytes", 0), c.Long("MemoryMinimumBytes", 0), c.Long("MemoryMaximumBytes", 0)), v.Name);
                    if (c.Has("CheckpointType")) v.CheckpointType = c.Str("CheckpointType");
                    if (c.Has("AutomaticStartAction")) v.StartAction = c.Str("AutomaticStartAction");
                    if (c.Has("AutomaticStopAction")) v.StopAction = c.Str("AutomaticStopAction");
                    if (c.Has("Notes")) v.Notes = c.Str("Notes");
                    if (c.Has("NewVMName")) v.Name = c.Str("NewVMName");
                }
            });
            HvCmd("Set-VMProcessor", "VMName:string[]@0; VM:object[] pipe; Count:long; ExposeVirtualizationExtensions:bool; CompatibilityForMigrationEnabled:bool", (c, o) =>
            {
                var h = Hv(c);
                foreach (var v in HvVms(c, h, c.Has("VM") ? c.Get("VM") : null, "VMName"))
                    HvOk(c, h.SetProcessor(v, (int)c.Long("Count", 0), c.Has("ExposeVirtualizationExtensions") ? (bool?)PsConv.ToBool(c.Get("ExposeVirtualizationExtensions")) : null), v.Name);
            });
            HvCmd("Get-VMProcessor", "VMName:string[]@0; VM:object[] pipe", (c, o) =>
            {
                var h = Hv(c);
                foreach (var v in HvVms(c, h, c.Has("VM") ? c.Get("VM") : null, "VMName")) { var p = new PsObj("Microsoft.HyperV.PowerShell.VMProcessor", "System.Object"); p.Val("VMName", "string", v.Name); p.Val("Count", "long", (long)v.Cpus); p.Val("CompatibilityForMigrationEnabled", "bool", false); p.Val("CompatibilityForOlderOperatingSystemsEnabled", "bool", false); p.Val("ExposeVirtualizationExtensions", "bool", v.Nested); p.DefaultDisplay = new[] { "VMName", "Count", "CompatibilityForMigrationEnabled", "CompatibilityForOlderOperatingSystemsEnabled" }; c.Out(p); }
            });
            HvCmd("Set-VMMemory", "VMName:string[]@0; VM:object[] pipe; DynamicMemoryEnabled:bool; StartupBytes:long; MinimumBytes:long; MaximumBytes:long; Buffer:int; Priority:int", (c, o) =>
            {
                var h = Hv(c);
                foreach (var v in HvVms(c, h, c.Has("VM") ? c.Get("VM") : null, "VMName"))
                    HvOk(c, h.SetMemory(v, c.Has("DynamicMemoryEnabled") ? (bool?)PsConv.ToBool(c.Get("DynamicMemoryEnabled")) : null, c.Long("StartupBytes", 0), c.Long("MinimumBytes", 0), c.Long("MaximumBytes", 0)), v.Name);
            });
            HvCmd("Get-VMMemory", "VMName:string[]@0; VM:object[] pipe", (c, o) =>
            {
                var h = Hv(c);
                foreach (var v in HvVms(c, h, c.Has("VM") ? c.Get("VM") : null, "VMName")) { var p = new PsObj("Microsoft.HyperV.PowerShell.VMMemory", "System.Object"); p.Val("VMName", "string", v.Name); p.Val("DynamicMemoryEnabled", "bool", v.Dynamic); p.Val("Minimum(M)", "long", v.MemMin >> 20); p.Val("Startup(M)", "long", v.Memory >> 20); p.Val("Maximum(M)", "long", v.MemMax >> 20); c.Out(p); }
            });
            RegWsHv2();
        }
    }
}
