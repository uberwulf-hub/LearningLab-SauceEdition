// Cert Lab - Hyper-V's virtual switches, HV1's starting virtual machines, and Windows Server containers with a pretend
// Docker engine on HV1: images (pulled from a pretend registry; nothing is downloaded), containers with process or
// Hyper-V isolation (process isolation needs an image built for the host's Windows version, Hyper-V isolation runs
// older images too), published ports and the default nat network.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public class WsContainer { public string Id = "", Name = "", Image = "", Isolation = "process", Status = "Up", Ports = "", Command = ""; public DateTime Created; }

    public partial class WsHvHost
    {
        public readonly List<string> Images = new List<string>();
        public readonly List<WsContainer> Containers = new List<WsContainer>();
        public bool Docker;
        public static readonly string[] Registry = { "mcr.microsoft.com/windows/servercore:ltsc2022", "mcr.microsoft.com/windows/nanoserver:ltsc2022", "mcr.microsoft.com/windows/servercore:ltsc2019", "mcr.microsoft.com/windows/servercore/iis:windowsservercore-ltsc2022", "mcr.microsoft.com/dotnet/framework/aspnet:4.8-windowsservercore-ltsc2022" };

        public string NewSwitch(string name, string type, string adapter, bool mgmt)
        {
            if (Switch(name) != null) return "A virtual switch named " + name + " already exists.";
            if (!string.IsNullOrEmpty(adapter))
            {
                if (S.M.AdapterByAlias(adapter) == null) return "The network adapter " + adapter + " wasn't found.";
                if (Switches.Any(x => WsText.Eq(x.Adapter, adapter))) return "The network adapter " + adapter + " is already bound to an external virtual switch.";
                type = "External";
            }
            else if (WsText.Eq(type, "External")) return "An external switch needs a physical network adapter (-NetAdapterName).";
            type = WsText.Eq(type, "Private") ? "Private" : WsText.Eq(type, "External") ? "External" : "Internal";
            Switches.Add(new WsVSwitch { Name = name, Type = type, Adapter = adapter ?? "", ManagementOs = type == "Internal" || type == "External" && mgmt });
            return null;
        }
        public string RemoveSwitch(string name)
        {
            var sw = Switch(name);
            if (sw == null) return "There is no virtual switch " + name + ".";
            Switches.Remove(sw);
            foreach (var n in Vms.SelectMany(v => v.Nics).Where(n => WsText.Eq(n.Switch, name))) n.Switch = "";
            return null;
        }

        public static string ImageOs(string image) { return image.Contains("2019") ? "10.0.17763" : "10.0.20348"; }
        public string Run(string image, string name, string isolation, string ports, string cmd, bool detach, out WsContainer ct)
        {
            ct = null;
            if (!Docker) return "docker: the Docker engine isn't installed on this server.";
            if (!S.Has("Containers")) return "docker: the Containers feature isn't installed (Install-WindowsFeature Containers, then restart).";
            if (!image.Contains(":")) image += ":latest";
            if (!Images.Contains(image, StringComparer.OrdinalIgnoreCase))
            {
                if (!Registry.Contains(image, StringComparer.OrdinalIgnoreCase)) return "Unable to find image '" + image + "' locally\ndocker: Error response from daemon: manifest for " + image + " not found: manifest unknown.";
                Images.Add(image);
            }
            isolation = string.IsNullOrEmpty(isolation) || isolation == "default" ? "process" : isolation.ToLowerInvariant();
            if (isolation == "process" && ImageOs(image) != S.M.OsVersion) return "docker: Error response from daemon: hcs::CreateComputeSystem: The container operating system does not match the host operating system. Run it with --isolation=hyperv, or use an image built for this version of Windows.";
            if (isolation == "hyperv" && !S.Has("Hyper-V")) return "docker: Hyper-V isolation needs the Hyper-V role on the container host.";
            if (!string.IsNullOrEmpty(name) && Containers.Any(c => WsText.Eq(c.Name, name))) return "docker: Error response from daemon: Conflict. The container name \"/" + name + "\" is already in use.";
            var id = S.Lab.NewGuid().Replace("-", "").Substring(0, 12);
            ct = new WsContainer { Id = id, Name = string.IsNullOrEmpty(name) ? "harbor_" + id.Substring(0, 5) : name, Image = image, Isolation = isolation, Ports = ports ?? "", Command = cmd ?? "", Created = S.Lab.Now, Status = detach || cmd != null && cmd.Contains("ping -t") ? "Up" : "Exited (0)" };
            Containers.Add(ct);
            return null;
        }
        public WsContainer Container(string key) { return Containers.FirstOrDefault(c => WsText.Eq(c.Name, key) || c.Id.StartsWith(key ?? "-", StringComparison.OrdinalIgnoreCase)); }
    }

    public partial class WsServer
    {
        WsHvHost hv;
        public WsHvHost Hv { get { if (!Has("Hyper-V")) return null; if (hv == null) hv = new WsHvHost { S = this }; return hv; } }
    }

    public partial class WsLab
    {
        partial void SeedHyperV()
        {
            var s = Server("HV1");
            s.M.MemoryBytes = 64L << 30; s.M.Cpus = 16;
            var eth2 = new NetAdapterInfo { Name = "Ethernet 2", IfIndex = 9, Mac = "00-15-5D-0A-0A-31", Guid = "{6F1E2D3C-4B5A-4978-8695-A4B3C2D1E0F9}", Status = "Up" };
            s.M.Adapters.Add(eth2);
            foreach (var m in Net.Machines.Where(x => x.IsServer)) m.Features.Add(new WinFeature { Name = "Containers", Display = "Containers", Type = "Feature", State = "Available", Restart = true });
            InstallFeatures(s.M, "Containers");
            var h = s.Hv;
            h.Docker = true;
            h.Images.Add("mcr.microsoft.com/windows/servercore:ltsc2022");
            h.NewSwitch("LAN", "External", "Ethernet 2", false);
            WsVhd v; WsVm vm;
            h.NewVhd("D:\\Hyper-V\\Virtual Hard Disks\\APP-OLD.vhdx", 60L << 30, "Dynamic", null, out v); v.FileSize = 18L << 30;
            h.NewVm("APP-OLD", 1, 2L << 30, "D:\\Hyper-V\\Virtual Hard Disks\\APP-OLD.vhdx", 0, "LAN", out vm); vm.State = "Running"; vm.Started = Now.AddDays(-12).AddHours(-3); vm.Version = "9.0"; vm.Notes = "Old order-tracking app (Windows Server 2016)";
            h.NewVhd("D:\\Hyper-V\\Virtual Hard Disks\\WEB-TEST.vhdx", 80L << 30, "Dynamic", null, out v); v.FileSize = 11L << 30;
            h.NewVm("WEB-TEST", 2, 4L << 30, "D:\\Hyper-V\\Virtual Hard Disks\\WEB-TEST.vhdx", 0, "LAN", out vm);
            h.NewVhd("D:\\Hyper-V\\Virtual Hard Disks\\LAB-HOST.vhdx", 127L << 30, "Dynamic", null, out v); v.FileSize = 9L << 30;
            h.NewVm("LAB-HOST", 2, 8L << 30, "D:\\Hyper-V\\Virtual Hard Disks\\LAB-HOST.vhdx", 0, "LAN", out vm); vm.Dynamic = true; vm.MemMin = 2L << 30; vm.MemMax = 16L << 30; vm.Cpus = 2; vm.Notes = "Will run its own Hyper-V for the training team";
        }
    }
}
