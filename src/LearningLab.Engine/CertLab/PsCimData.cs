// Cert Lab - the CIM / WMI classes of the pretend machines (root/cimv2: Win32_OperatingSystem, Win32_ComputerSystem,
// Win32_BIOS, Win32_Processor, Win32_LogicalDisk, Win32_Service, Win32_Process, Win32_NetworkAdapterConfiguration,
// Win32_QuickFixEngineering, Win32_UserAccount, Win32_Group, Win32_Share, Win32_PhysicalMemory, Win32_StartupCommand,
// Win32_ComputerSystemProduct), built on demand from the machine's own data, and a small WQL reader.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CertLab
{
    /// <summary>One instance of a CIM class: its properties in order, its key properties and the record behind it.</summary>
    public class CimRow
    {
        public string Class = "";
        public readonly List<KeyValuePair<string, object>> Props = new List<KeyValuePair<string, object>>();
        public string[] Keys = new string[0];
        public object Base;
        public CimRow P(string n, object v) { Props.Add(new KeyValuePair<string, object>(n, v)); return this; }
        public object Get(string n) { foreach (var kv in Props) if (kv.Key.Equals(n, StringComparison.OrdinalIgnoreCase)) return kv.Value; return null; }
        public bool Has(string n) { return Props.Any(kv => kv.Key.Equals(n, StringComparison.OrdinalIgnoreCase)); }
    }

    public class CimClassInfo
    {
        public string Name = "", Namespace = "root/cimv2";
        public string[] Parents = new string[0], Methods = new string[0], View = new string[0];
        public Func<PsMachine, List<CimRow>> Rows;
    }

    public static class PsCim
    {
        public static string Dmtf(DateTime t, double offsetHours)
        {
            int min = (int)Math.Round(offsetHours * 60);
            return t.ToString("yyyyMMddHHmmss.ffffff", CultureInfo.InvariantCulture) + (min < 0 ? "-" : "+") + Math.Abs(min).ToString("000", CultureInfo.InvariantCulture);
        }
        static readonly Dictionary<string, CimClassInfo> classes = new Dictionary<string, CimClassInfo>(StringComparer.OrdinalIgnoreCase);
        static bool done; static readonly object gate = new object();
        public static IEnumerable<CimClassInfo> All { get { Init(); return classes.Values; } }
        public static CimClassInfo Find(string ns, string name)
        {
            Init();
            CimClassInfo c;
            if (!classes.TryGetValue(name, out c)) return null;
            return NsEq(c.Namespace, ns) ? c : null;
        }
        public static bool NsEq(string a, string b) { return Norm(a).Equals(Norm(b), StringComparison.OrdinalIgnoreCase); }
        public static string Norm(string ns) { return (ns ?? "root/cimv2").Replace('\\', '/').Trim('/'); }
        public static bool KnownNamespace(string ns) { var n = Norm(ns).ToLowerInvariant(); return n == "root/cimv2" || n == "root" || n == "root/microsoft/windows/taskscheduler" || n == "root/standardcimv2" || n == "root/subscription" || n == "root/default" || n == "root/microsoft/windows/smb" || n == "root/microsoft/windows/storage"; }

        static void Def(string name, string parents, string methods, string view, Func<PsMachine, List<CimRow>> rows)
        {
            classes[name] = new CimClassInfo { Name = name, Parents = parents.Length == 0 ? new string[0] : parents.Split(','), Methods = methods.Length == 0 ? new string[0] : methods.Split(','), View = view.Split(','), Rows = rows };
        }
        static List<CimRow> One(CimRow r) { return new List<CimRow> { r }; }
        static long UsedKb(PsMachine m) { return m.LiveProcesses().Sum(p => p.Ws) / 1024 + (m.IsServer ? 1572864 : 1153434); }
        static string Sku(PsMachine m) { return m.IsServer ? "Datacenter" : "Enterprise"; }

        static void Init()
        {
            lock (gate)
            {
                if (done) return;
                done = true;
                Def("Win32_OperatingSystem", "CIM_OperatingSystem,CIM_LogicalElement,CIM_ManagedSystemElement", "Reboot,Shutdown,Win32Shutdown,SetDateTime", "SystemDirectory,Organization,BuildNumber,RegisteredUser,SerialNumber,Version", m =>
                {
                    long total = m.MemoryBytes / 1024 - 460, free = Math.Max(200000, total - UsedKb(m));
                    var cv = m.HKLM.Sub("SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion");
                    var r = new CimRow { Class = "Win32_OperatingSystem", Keys = new[] { "Name" } };
                    r.P("Status", "OK").P("Name", m.OsCaption + "|C:\\Windows|\\Device\\Harddisk0\\Partition" + (m.IsServer ? "2" : "3")).P("FreePhysicalMemory", (ulong)free).P("FreeSpaceInPagingFiles", (ulong)(m.IsServer ? 1179436 : 1048576)).P("FreeVirtualMemory", (ulong)(free + 1100000))
                     .P("Caption", m.OsCaption).P("Description", "").P("InstallDate", m.InstallDate).P("CreationClassName", "Win32_OperatingSystem").P("CSCreationClassName", "Win32_ComputerSystem").P("CSName", m.Name)
                     .P("CurrentTimeZone", (short)(m.UtcOffsetHours * 60)).P("Distributed", false).P("LastBootUpTime", m.BootTime).P("LocalDateTime", m.Now).P("MaxNumberOfProcesses", 4294967295L).P("MaxProcessMemorySize", 137438953344UL)
                     .P("NumberOfLicensedUsers", 0L).P("NumberOfProcesses", (long)m.LiveProcesses().Count).P("NumberOfUsers", (long)(m.IsServer ? 2 : 1)).P("OSType", (ushort)18).P("OtherTypeDescription", null)
                     .P("SizeStoredInPagingFiles", (ulong)1245184).P("TotalSwapSpaceSize", null).P("TotalVirtualMemorySize", (ulong)(total + 1245184)).P("TotalVisibleMemorySize", (ulong)total).P("Version", m.OsVersion)
                     .P("BootDevice", "\\Device\\HarddiskVolume1").P("BuildNumber", m.OsBuild).P("BuildType", "Multiprocessor Free").P("CodeSet", "1252").P("CountryCode", "1").P("CSDVersion", null)
                     .P("DataExecutionPrevention_32BitApplications", true).P("DataExecutionPrevention_Available", true).P("DataExecutionPrevention_Drivers", true).P("DataExecutionPrevention_SupportPolicy", (byte)(m.IsServer ? 3 : 2))
                     .P("Debug", false).P("EncryptionLevel", 256L).P("ForegroundApplicationBoost", (byte)2).P("LargeSystemCache", null).P("Locale", "0409").P("Manufacturer", "Microsoft Corporation")
                     .P("MUILanguages", new object[] { "en-US" }).P("OperatingSystemSKU", (long)(m.IsServer ? 8 : 4)).P("Organization", PsConv.ToStr(cv.Value("RegisteredOrganization") == null ? "" : cv.Value("RegisteredOrganization").Data)).P("OSArchitecture", "64-bit").P("OSLanguage", 1033L)
                     .P("OSProductSuite", (long)(m.IsServer ? 400 : 256)).P("PortableOperatingSystem", false).P("Primary", true).P("ProductType", (long)(m.IsServer ? 3 : 1)).P("RegisteredUser", PsConv.ToStr(cv.Value("RegisteredOwner") == null ? "" : cv.Value("RegisteredOwner").Data)).P("SerialNumber", m.SerialNumber)
                     .P("ServicePackMajorVersion", (ushort)0).P("ServicePackMinorVersion", (ushort)0).P("SuiteMask", (long)(m.IsServer ? 400 : 272)).P("SystemDevice", "\\Device\\HarddiskVolume" + (m.IsServer ? "2" : "3")).P("SystemDirectory", "C:\\Windows\\system32").P("SystemDrive", "C:").P("WindowsDirectory", "C:\\Windows");
                    r.Base = m;
                    return One(r);
                });
                Def("Win32_ComputerSystem", "CIM_UnitaryComputerSystem,CIM_ComputerSystem,CIM_System,CIM_LogicalElement,CIM_ManagedSystemElement", "Rename,JoinDomainOrWorkgroup,UnjoinDomainOrWorkgroup", "Name,PrimaryOwnerName,Domain,TotalPhysicalMemory,Model,Manufacturer", m =>
                {
                    var r = new CimRow { Class = "Win32_ComputerSystem", Keys = new[] { "Name" }, Base = m };
                    r.P("AdminPasswordStatus", (ushort)3).P("BootupState", "Normal boot").P("ChassisBootupState", (ushort)3).P("KeyboardPasswordStatus", (ushort)3).P("PowerOnPasswordStatus", (ushort)3).P("PowerSupplyState", (ushort)3).P("PowerState", (ushort)0)
                     .P("FrontPanelResetStatus", (ushort)3).P("ThermalState", (ushort)3).P("Status", "OK").P("Name", m.Name).P("PowerManagementCapabilities", null).P("PowerManagementSupported", null).P("Caption", m.Name).P("Description", "AT/AT COMPATIBLE")
                     .P("InstallDate", null).P("CreationClassName", "Win32_ComputerSystem").P("NameFormat", null).P("PrimaryOwnerContact", null).P("PrimaryOwnerName", "Lab Administrator").P("Roles", m.IsServer ? new object[] { "LM_Workstation", "LM_Server", "NT", "Server_NT" } : new object[] { "LM_Workstation", "LM_Server", "NT" })
                     .P("AutomaticManagedPagefile", true).P("AutomaticResetBootOption", true).P("AutomaticResetCapability", true).P("BootROMSupported", true).P("CurrentTimeZone", (short)(m.UtcOffsetHours * 60)).P("DaylightInEffect", false).P("DNSHostName", m.Name)
                     .P("Domain", m.Domain).P("DomainRole", (ushort)(m.IsServer ? 3 : 1)).P("EnableDaylightSavingsTime", true).P("HypervisorPresent", true).P("InfraredSupported", false).P("Manufacturer", "Microsoft Corporation").P("Model", "Virtual Machine")
                     .P("NetworkServerModeEnabled", true).P("NumberOfLogicalProcessors", (long)m.Cpus).P("NumberOfProcessors", 1L).P("PartOfDomain", true).P("PCSystemType", (ushort)1).P("PCSystemTypeEx", (ushort)1).P("SystemFamily", "Virtual Machine")
                     .P("SystemSKUNumber", "None").P("SystemType", "x64-based PC").P("TotalPhysicalMemory", (ulong)(m.MemoryBytes - 471040)).P("UserName", m.IsServer ? m.DomainShort + "\\Administrator" : m.Name + "\\LabUser").P("WakeUpType", (ushort)6).P("Workgroup", null);
                    return One(r);
                });
                Def("Win32_BIOS", "CIM_BIOSElement,CIM_SoftwareElement,CIM_LogicalElement,CIM_ManagedSystemElement", "", "SMBIOSBIOSVersion,Manufacturer,Name,SerialNumber,Version", m =>
                {
                    var r = new CimRow { Class = "Win32_BIOS", Keys = new[] { "Name", "SoftwareElementID", "SoftwareElementState", "TargetOperatingSystem", "Version" } };
                    r.P("BiosCharacteristics", new object[] { 3, 9, 15, 16, 17, 19, 26, 27, 28, 32, 33, 39, 42, 43 }).P("BIOSVersion", new object[] { "VRTUAL - 4001", "Hyper-V UEFI Release v4.1", "EDK II - 10000" }).P("Caption", "Hyper-V UEFI Release v4.1").P("Description", "Hyper-V UEFI Release v4.1")
                     .P("Manufacturer", "Microsoft Corporation").P("Name", "Hyper-V UEFI Release v4.1").P("PrimaryBIOS", true).P("ReleaseDate", new DateTime(2023, 4, 5)).P("SerialNumber", m.BiosSerial).P("SMBIOSBIOSVersion", "Hyper-V UEFI Release v4.1")
                     .P("SMBIOSMajorVersion", (ushort)3).P("SMBIOSMinorVersion", (ushort)1).P("SMBIOSPresent", true).P("SoftwareElementID", "Hyper-V UEFI Release v4.1").P("SoftwareElementState", (ushort)3).P("Status", "OK").P("TargetOperatingSystem", (ushort)0).P("Version", "VRTUAL - 4001");
                    return One(r);
                });
                Def("Win32_ComputerSystemProduct", "CIM_Product", "", "IdentifyingNumber,Name,Vendor,Version,Caption", m =>
                {
                    var r = new CimRow { Class = "Win32_ComputerSystemProduct", Keys = new[] { "IdentifyingNumber", "Name", "Version" } };
                    r.P("Caption", "Computer System Product").P("Description", "Computer System Product").P("IdentifyingNumber", m.BiosSerial).P("Name", "Virtual Machine").P("SKUNumber", null).P("Vendor", "Microsoft Corporation").P("Version", "Hyper-V UEFI Release v4.1").P("UUID", m.Uuid);
                    return One(r);
                });
                Def("Win32_Processor", "CIM_Processor,CIM_LogicalDevice,CIM_LogicalElement,CIM_ManagedSystemElement", "", "DeviceID,Name,Caption,MaxClockSpeed,SocketDesignation,Manufacturer", m =>
                {
                    var r = new CimRow { Class = "Win32_Processor", Keys = new[] { "DeviceID" } };
                    r.P("Availability", (ushort)3).P("CpuStatus", (ushort)1).P("CurrentVoltage", null).P("DeviceID", "CPU0").P("ErrorCleared", null).P("LoadPercentage", (ushort)(2 + m.LiveProcesses().Count % 7)).P("Name", "Intel(R) Xeon(R) Gold 6338 CPU @ 2.00GHz").P("Status", "OK").P("StatusInfo", (ushort)3)
                     .P("Caption", "Intel64 Family 6 Model 106 Stepping 6").P("Description", "Intel64 Family 6 Model 106 Stepping 6").P("CreationClassName", "Win32_Processor").P("SystemName", m.Name).P("AddressWidth", (ushort)64).P("CurrentClockSpeed", 1995L).P("DataWidth", (ushort)64)
                     .P("Family", (ushort)179).P("MaxClockSpeed", 1995L).P("Architecture", (ushort)9).P("L2CacheSize", (long)(1280 * m.Cpus)).P("L3CacheSize", 49152L).P("Level", (ushort)6).P("Manufacturer", "GenuineIntel").P("NumberOfCores", (long)m.Cpus)
                     .P("NumberOfEnabledCore", (long)m.Cpus).P("NumberOfLogicalProcessors", (long)m.Cpus).P("ProcessorId", "1F8BFBFF000606A6").P("ProcessorType", (ushort)3).P("Revision", (ushort)27142).P("SocketDesignation", "None").P("ThreadCount", (long)m.Cpus)
                     .P("VirtualizationFirmwareEnabled", false).P("VMMonitorModeExtensions", false).P("SecondLevelAddressTranslationExtensions", false);
                    return One(r);
                });
                Def("Win32_LogicalDisk", "CIM_LogicalDisk,CIM_StorageExtent,CIM_LogicalDevice,CIM_LogicalElement,CIM_ManagedSystemElement", "Chkdsk,ScheduleAutoChk,ExcludeFromAutochk", "DeviceID,DriveType,ProviderName,VolumeName,Size,FreeSpace", m =>
                {
                    var l = new List<CimRow>();
                    var c = new CimRow { Class = "Win32_LogicalDisk", Keys = new[] { "DeviceID" } };
                    c.P("Access", (ushort)0).P("Availability", null).P("Caption", "C:").P("Compressed", false).P("CreationClassName", "Win32_LogicalDisk").P("Description", "Local Fixed Disk").P("DeviceID", "C:").P("DriveType", 3L).P("FileSystem", "NTFS")
                     .P("FreeSpace", (ulong)m.DiskFreeC).P("MaximumComponentLength", 255L).P("MediaType", 12L).P("Name", "C:").P("ProviderName", null).P("Size", (ulong)m.DiskSizeC).P("SystemName", m.Name).P("VolumeDirty", false).P("VolumeName", "").P("VolumeSerialNumber", m.IsServer ? "A4B2C1D0" : "6E3F9A12");
                    l.Add(c);
                    var d = new CimRow { Class = "Win32_LogicalDisk", Keys = new[] { "DeviceID" } };
                    d.P("Access", null).P("Availability", null).P("Caption", "D:").P("Compressed", null).P("CreationClassName", "Win32_LogicalDisk").P("Description", "CD-ROM Disc").P("DeviceID", "D:").P("DriveType", 5L).P("FileSystem", null)
                     .P("FreeSpace", null).P("MaximumComponentLength", null).P("MediaType", 11L).P("Name", "D:").P("ProviderName", null).P("Size", null).P("SystemName", m.Name).P("VolumeDirty", null).P("VolumeName", null).P("VolumeSerialNumber", null);
                    l.Add(d);
                    return l;
                });
                Def("Win32_Service", "Win32_BaseService,CIM_Service,CIM_LogicalElement,CIM_ManagedSystemElement", "StartService,StopService,PauseService,ResumeService,InterrogateService,UserControlService,Create,Change,ChangeStartMode,Delete", "ProcessId,Name,StartMode,State,Status,ExitCode", m =>
                    m.LiveServices.Where(s => s.ServiceType != "KernelDriver").OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).Select(s =>
                    {
                        var r = new CimRow { Class = "Win32_Service", Keys = new[] { "Name" }, Base = s };
                        r.P("Name", s.Name).P("Status", "OK").P("ExitCode", (long)(s.Running ? 0 : (s.ExitCode != 0 ? s.ExitCode : s.EverStarted ? 0 : 1077))).P("DesktopInteract", false).P("ErrorControl", "Normal").P("PathName", s.BinaryPath)
                         .P("ServiceType", s.ServiceType.Contains("Share") ? "Share Process" : "Own Process").P("StartMode", s.StartType == "Automatic" ? "Auto" : s.StartType).P("Caption", s.DisplayName).P("Description", s.Description).P("InstallDate", null)
                         .P("CreationClassName", "Win32_Service").P("Started", s.Running).P("SystemCreationClassName", "Win32_ComputerSystem").P("SystemName", m.Name).P("AcceptPause", s.CanPause && s.Running).P("AcceptStop", s.CanStop && s.Running)
                         .P("DisplayName", s.DisplayName).P("CheckPoint", 0L).P("DelayedAutoStart", s.Delayed).P("ProcessId", (long)s.Pid).P("ServiceSpecificExitCode", 0L).P("StartName", s.Account).P("State", s.Status).P("TagId", 0L).P("WaitHint", 0L);
                        return r;
                    }).ToList());
                Def("Win32_Process", "CIM_Process,CIM_LogicalElement,CIM_ManagedSystemElement", "Create,Terminate,GetOwner,GetOwnerSid,SetPriority,AttachDebugger,GetAvailableVirtualSize", "ProcessId,Name,HandleCount,WorkingSetSize,VirtualSize", m =>
                    m.LiveProcesses().OrderBy(p => p.Id == 0 ? -1 : p.Start.Ticks).ThenBy(p => p.Id).Select(p =>
                    {
                        string name = p.Id == 0 ? "System Idle Process" : p.Path == null ? p.Name : p.Name + ".exe";
                        bool hidden = p.Id <= 4 || p.Protected;
                        var r = new CimRow { Class = "Win32_Process", Keys = new[] { "Handle" }, Base = p };
                        r.P("ProcessName", name).P("Handles", (long)p.Handles).P("VM", (ulong)p.Vm).P("WS", (ulong)p.Ws).P("Path", hidden ? null : p.Path)
                         .P("Caption", name).P("CommandLine", hidden ? null : p.CommandLine).P("CreationClassName", "Win32_Process").P("CreationDate", p.Id == 0 ? (object)null : p.Start).P("CSCreationClassName", "Win32_ComputerSystem").P("CSName", m.Name)
                         .P("Description", name).P("ExecutablePath", hidden ? null : p.Path).P("ExecutionState", null).P("Handle", p.Id.ToString(CultureInfo.InvariantCulture)).P("HandleCount", (long)p.Handles).P("InstallDate", null)
                         .P("KernelModeTime", (ulong)(p.Cpu * 4000000)).P("MaximumWorkingSetSize", hidden ? null : (object)1380L).P("MinimumWorkingSetSize", hidden ? null : (object)200L).P("Name", name).P("OSCreationClassName", "Win32_OperatingSystem")
                         .P("OSName", m.OsCaption + "|C:\\Windows|\\Device\\Harddisk0\\Partition2").P("OtherOperationCount", (ulong)(p.Id * 37 % 100000)).P("PageFaults", (long)(p.Ws / 4096)).P("PageFileUsage", (long)(p.Pm / 1024)).P("ParentProcessId", (long)p.ParentId)
                         .P("PeakPageFileUsage", (long)(p.Pm / 1024 + 400)).P("PeakVirtualSize", (ulong)p.Vm + 4194304).P("PeakWorkingSetSize", (long)(p.Ws / 1024 + 900)).P("Priority", (long)p.Priority).P("PrivatePageCount", (ulong)p.Pm).P("ProcessId", (long)p.Id)
                         .P("QuotaNonPagedPoolUsage", (long)(p.Npm / 1024)).P("QuotaPagedPoolUsage", (long)(p.Pm / 40960)).P("SessionId", (long)p.SessionId).P("Status", null).P("TerminationDate", null).P("ThreadCount", (long)p.Threads)
                         .P("UserModeTime", (ulong)(p.Cpu * 6000000)).P("VirtualSize", (ulong)p.Vm).P("WindowsVersion", m.OsVersion).P("WorkingSetSize", (ulong)p.Ws).P("WriteOperationCount", (ulong)(p.Id * 11 % 5000));
                        return r;
                    }).ToList());
                Def("Win32_NetworkAdapterConfiguration", "CIM_Setting", "EnableDHCP,EnableStatic,SetGateways,SetDNSServerSearchOrder,RenewDHCPLease,ReleaseDHCPLease", "DHCPEnabled,IPAddress,DefaultIPGateway,DNSDomain,ServiceName,Description,Index", m =>
                {
                    var l = new List<CimRow>();
                    Func<int, string, string, CimRow> nic = (i, desc, svc) => { var r = new CimRow { Class = "Win32_NetworkAdapterConfiguration", Keys = new[] { "Index" } }; r.P("Caption", "[" + i.ToString("00000000") + "] " + desc).P("Description", desc).P("SettingID", "{" + (i * 1111).ToString("X8") + "-6A1F-4C8B-9E3D-2B7A5C0F1E9D}"); return r; };
                    var k = nic(0, "Microsoft Kernel Debug Network Adapter", "kdnic");
                    k.P("ArpAlwaysSourceRoute", null).P("DefaultIPGateway", null).P("DHCPEnabled", true).P("DHCPServer", null).P("DNSDomain", null).P("DNSHostName", null).P("DNSServerSearchOrder", null).P("Index", 0L).P("InterfaceIndex", 8L).P("IPAddress", null).P("IPEnabled", false).P("IPSubnet", null).P("MACAddress", null).P("ServiceName", "kdnic");
                    l.Add(k);
                    var e = nic(1, "Microsoft Hyper-V Network Adapter", "netvsc");
                    int last = int.Parse(m.Ip.Split('.')[3]);
                    e.P("ArpAlwaysSourceRoute", null).P("DefaultIPGateway", new object[] { "10.0.10.1" }).P("DHCPEnabled", false).P("DHCPServer", null).P("DNSDomain", m.Domain).P("DNSHostName", m.Name).P("DNSServerSearchOrder", new object[] { "10.0.10.10" })
                     .P("Index", 1L).P("InterfaceIndex", 6L).P("IPAddress", new object[] { m.Ip, "fe80::" + (0x2a1c + last).ToString("x") + ":7d3e:" + (0x1b00 + last * 3).ToString("x") + ":" + (0x9f10 + last).ToString("x") }).P("IPEnabled", true).P("IPSubnet", new object[] { "255.255.255.0", "64" })
                     .P("MACAddress", "00:15:5D:0A:0A:" + last.ToString("X2")).P("ServiceName", "netvsc").P("TcpipNetbiosOptions", 0L).P("DomainDNSRegistrationEnabled", false).P("FullDNSRegistrationEnabled", true);
                    l.Add(e);
                    var w = nic(2, "WAN Miniport (IP)", "NdisWan");
                    w.P("ArpAlwaysSourceRoute", null).P("DefaultIPGateway", null).P("DHCPEnabled", false).P("DHCPServer", null).P("DNSDomain", null).P("DNSHostName", null).P("DNSServerSearchOrder", null).P("Index", 2L).P("InterfaceIndex", 11L).P("IPAddress", null).P("IPEnabled", false).P("IPSubnet", null).P("MACAddress", null).P("ServiceName", "NdisWan");
                    l.Add(w);
                    return l;
                });
                Def("Win32_QuickFixEngineering", "CIM_LogicalElement,CIM_ManagedSystemElement", "", "Source,Description,HotFixID,InstalledBy,InstalledOn", m =>
                    m.Hotfixes.Select(h =>
                    {
                        var r = new CimRow { Class = "Win32_QuickFixEngineering", Keys = new[] { "HotFixID", "ServicePackInEffect" }, Base = h };
                        r.P("Caption", "http://support.microsoft.com/?kbid=" + h.Id.Substring(2)).P("Description", h.Description).P("InstallDate", null).P("Name", null).P("Status", null).P("CSName", m.Name).P("FixComments", "")
                         .P("HotFixID", h.Id).P("InstalledBy", h.By).P("InstalledOn", h.On).P("ServicePackInEffect", "");
                        return r;
                    }).ToList());
                Def("Win32_UserAccount", "Win32_Account,CIM_LogicalElement,CIM_ManagedSystemElement", "Rename", "Name,Caption,AccountType,SID,Domain", m =>
                {
                    var l = m.Users.Select(u =>
                    {
                        var r = new CimRow { Class = "Win32_UserAccount", Keys = new[] { "Domain", "Name" }, Base = u };
                        r.P("Caption", m.Name + "\\" + u.Name).P("Description", u.Description).P("InstallDate", null).P("Name", u.Name).P("Status", u.Enabled ? "OK" : "Degraded").P("Domain", m.Name).P("LocalAccount", true).P("SID", u.Sid).P("SIDType", (byte)1)
                         .P("AccountType", 512L).P("Disabled", !u.Enabled).P("FullName", u.FullName).P("Lockout", false).P("PasswordChangeable", u.UserMayChangePassword).P("PasswordExpires", !u.PasswordNeverExpires && u.HasPassword).P("PasswordRequired", u.PasswordRequired);
                        return r;
                    }).ToList();
                    foreach (var d in PsMachine.DomainPrincipals)
                    {
                        var p = d.Split('|'); if (p[1] != "User") continue;
                        var r = new CimRow { Class = "Win32_UserAccount", Keys = new[] { "Domain", "Name" } };
                        r.P("Caption", m.DomainShort + "\\" + p[0]).P("Description", p[0] == "Administrator" ? "Built-in account for administering the computer/domain" : "").P("InstallDate", null).P("Name", p[0]).P("Status", p[0] == "Guest" || p[0] == "krbtgt" ? "Degraded" : "OK").P("Domain", m.DomainShort).P("LocalAccount", false).P("SID", m.DomainSid + "-" + p[2]).P("SIDType", (byte)1)
                         .P("AccountType", 512L).P("Disabled", p[0] == "Guest" || p[0] == "krbtgt").P("FullName", "").P("Lockout", false).P("PasswordChangeable", true).P("PasswordExpires", false).P("PasswordRequired", true);
                        l.Add(r);
                    }
                    return l;
                });
                Def("Win32_Group", "Win32_Account,CIM_LogicalElement,CIM_ManagedSystemElement", "Rename", "SID,Name,Caption,Domain", m =>
                    m.Groups.Select(g =>
                    {
                        var r = new CimRow { Class = "Win32_Group", Keys = new[] { "Domain", "Name" }, Base = g };
                        r.P("Caption", m.Name + "\\" + g.Name).P("Description", g.Description).P("InstallDate", null).P("Name", g.Name).P("Status", "OK").P("Domain", m.Name).P("LocalAccount", true).P("SID", g.Sid).P("SIDType", (byte)4);
                        return r;
                    }).ToList());
                Def("Win32_Share", "CIM_LogicalElement,CIM_ManagedSystemElement", "Create,SetShareInfo,GetAccessMask,Delete", "Name,Path,Description", m =>
                {
                    var l = new List<CimRow>();
                    foreach (var s in new[] { "ADMIN$|C:\\Windows|Remote Admin|2147483648", "C$|C:\\|Default share|2147483648", "IPC$||Remote IPC|2147483651" }.Concat(m.ExtraShares))
                    {
                        var p = s.Split('|');
                        var r = new CimRow { Class = "Win32_Share", Keys = new[] { "Name" } };
                        r.P("Caption", p[2]).P("Description", p[2]).P("InstallDate", null).P("Name", p[0]).P("Status", "OK").P("AccessMask", null).P("AllowMaximum", true).P("MaximumAllowed", null).P("Path", p[1]).P("Type", long.Parse(p[3], CultureInfo.InvariantCulture));
                        l.Add(r);
                    }
                    return l;
                });
                Def("Win32_PhysicalMemory", "CIM_PhysicalMemory,CIM_Chip,CIM_PhysicalComponent,CIM_PhysicalElement,CIM_ManagedSystemElement", "", "Manufacturer,BankLabel,ConfiguredClockSpeed,DeviceLocator,Capacity,SerialNumber", m =>
                {
                    var l = new List<CimRow>();
                    int n = m.MemoryBytes >= 8589934592L ? 2 : 1;
                    for (int i = 0; i < n; i++)
                    {
                        var r = new CimRow { Class = "Win32_PhysicalMemory", Keys = new[] { "CreationClassName", "Tag" } };
                        r.P("Caption", "Physical Memory").P("Description", "Physical Memory").P("InstallDate", null).P("Name", "Physical Memory").P("Status", null).P("CreationClassName", "Win32_PhysicalMemory").P("Manufacturer", "Microsoft Corporation").P("Model", null)
                         .P("PartNumber", "None").P("SerialNumber", "None").P("Tag", "Physical Memory " + i).P("FormFactor", (ushort)0).P("BankLabel", "None").P("Capacity", (ulong)(m.MemoryBytes / n)).P("DataWidth", null).P("InterleavePosition", null)
                         .P("MemoryType", (ushort)0).P("Speed", null).P("TotalWidth", null).P("Attributes", 0L).P("ConfiguredClockSpeed", 0L).P("ConfiguredVoltage", 0L).P("DeviceLocator", "M000" + (i + 1)).P("SMBIOSMemoryType", 24L);
                        l.Add(r);
                    }
                    return l;
                });
                Def("Win32_StartupCommand", "CIM_LogicalElement,CIM_ManagedSystemElement", "", "Command,User,Caption", m =>
                {
                    var l = new List<CimRow>();
                    Action<RegKey, string, string, string> run = (k, loc, user, sid) =>
                    {
                        if (k == null) return;
                        foreach (var v in k.Values)
                        {
                            var r = new CimRow { Class = "Win32_StartupCommand", Keys = new[] { "Command", "Location", "Name", "User" } };
                            r.P("Caption", v.Name).P("Description", v.Name).P("SettingID", "").P("Command", PsConv.ToStr(v.Data)).P("Location", loc).P("Name", v.Name).P("User", user).P("UserSID", sid);
                            l.Add(r);
                        }
                    };
                    run(m.HKLM.Find("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run"), "HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", "Public", "Public");
                    run(m.HKCU.Find("Software\\Microsoft\\Windows\\CurrentVersion\\Run"), "HKU\\" + m.AdminUserSid + "\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", m.DomainShort + "\\Administrator", m.AdminUserSid);
                    return l;
                });
                Def("MSFT_ScheduledTask", "", "", "TaskPath,TaskName,State", m => m.Tasks.Select(t => new CimRow { Class = "MSFT_ScheduledTask", Base = t }).ToList());
                classes["MSFT_ScheduledTask"].Namespace = "root/Microsoft/Windows/TaskScheduler";
            }
        }

        // ------------------------------------------------------------ WQL
        /// <summary>A WHERE clause as a test on an instance: =, !=, &lt;&gt;, &lt;, &gt;, &lt;=, &gt;=, LIKE, IS [NOT] NULL, AND, OR, NOT, ( ).</summary>
        public static Func<CimRow, bool> Where(string text)
        {
            var toks = Lex(text);
            int i = 0;
            var f = Or(toks, ref i);
            if (i < toks.Count) throw new FormatException("Invalid query");
            return f;
        }
        static List<string> Lex(string s)
        {
            var l = new List<string>();
            int i = 0;
            while (i < s.Length)
            {
                char ch = s[i];
                if (char.IsWhiteSpace(ch)) { i++; continue; }
                if (ch == '\'' || ch == '"')
                {
                    var sb = new StringBuilder("'"); i++;
                    while (i < s.Length && s[i] != ch) { if (s[i] == '\\' && i + 1 < s.Length) { i++; } sb.Append(s[i]); i++; }
                    if (i >= s.Length) throw new FormatException("Invalid query");
                    i++; l.Add(sb.ToString()); continue;
                }
                if ("()".IndexOf(ch) >= 0) { l.Add(ch.ToString()); i++; continue; }
                if ("=<>!".IndexOf(ch) >= 0) { string op = ch.ToString(); i++; if (i < s.Length && "=>".IndexOf(s[i]) >= 0) { op += s[i]; i++; } l.Add(op); continue; }
                int st = i;
                while (i < s.Length && !char.IsWhiteSpace(s[i]) && "()=<>!'\"".IndexOf(s[i]) < 0) i++;
                l.Add(s.Substring(st, i - st));
            }
            return l;
        }
        static bool Is(List<string> t, int i, string w) { return i < t.Count && t[i].Equals(w, StringComparison.OrdinalIgnoreCase); }
        static Func<CimRow, bool> Or(List<string> t, ref int i)
        {
            var a = And(t, ref i);
            while (Is(t, i, "OR")) { i++; var b = And(t, ref i); var x = a; a = r => x(r) || b(r); }
            return a;
        }
        static Func<CimRow, bool> And(List<string> t, ref int i)
        {
            var a = Not(t, ref i);
            while (Is(t, i, "AND")) { i++; var b = Not(t, ref i); var x = a; a = r => x(r) && b(r); }
            return a;
        }
        static Func<CimRow, bool> Not(List<string> t, ref int i)
        {
            if (Is(t, i, "NOT")) { i++; var a = Not(t, ref i); return r => !a(r); }
            if (Is(t, i, "(")) { i++; var e = Or(t, ref i); if (!Is(t, i, ")")) throw new FormatException("Invalid query"); i++; return e; }
            if (i + 1 >= t.Count) throw new FormatException("Invalid query");
            string prop = t[i++];
            if (Is(t, i, "IS"))
            {
                i++; bool neg = false; if (Is(t, i, "NOT")) { neg = true; i++; }
                if (!Is(t, i, "NULL")) throw new FormatException("Invalid query");
                i++;
                return r => (r.Get(prop) == null) != neg;
            }
            if (Is(t, i, "LIKE") || Is(t, i, "NOT") && Is(t, i + 1, "LIKE"))
            {
                bool neg = Is(t, i, "NOT"); i += neg ? 2 : 1;
                if (i >= t.Count || !t[i].StartsWith("'", StringComparison.Ordinal)) throw new FormatException("Invalid query");
                string pat = t[i++].Substring(1);
                var rx = new Regex("^" + Regex.Escape(pat).Replace("%", ".*").Replace("_", ".").Replace("\\[", "[").Replace("\\]", "]") + "$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                return r => { var v = r.Get(prop); return (v != null && rx.IsMatch(PsConv.ToStr(v))) != neg; };
            }
            string op = t[i++];
            if (i >= t.Count) throw new FormatException("Invalid query");
            string lit = t[i++];
            object val = lit.StartsWith("'", StringComparison.Ordinal) ? (object)lit.Substring(1) : lit.Equals("TRUE", StringComparison.OrdinalIgnoreCase) ? (object)true : lit.Equals("FALSE", StringComparison.OrdinalIgnoreCase) ? (object)false : lit.Equals("NULL", StringComparison.OrdinalIgnoreCase) ? null : (object)lit;
            if (op != "=" && op != "!=" && op != "<>" && op != "<" && op != ">" && op != "<=" && op != ">=") throw new FormatException("Invalid query");
            return r => Compare(r.Get(prop), op, val);
        }
        static bool Compare(object a, string op, object b)
        {
            if (a == null || b == null) return op == "=" ? a == b : (op == "!=" || op == "<>") && a != b;
            int c;
            double da, db;
            if (a is bool || b is bool) c = PsConv.ToBool(a) == PsConv.ToBool(b is string ? (object)((string)b).Equals("true", StringComparison.OrdinalIgnoreCase) : b) ? 0 : 1;
            else if (double.TryParse(PsConv.ToStr(a), NumberStyles.Float, CultureInfo.InvariantCulture, out da) && double.TryParse(PsConv.ToStr(b), NumberStyles.Float, CultureInfo.InvariantCulture, out db)) c = da.CompareTo(db);
            else c = string.Compare(PsConv.ToStr(a), PsConv.ToStr(b), StringComparison.OrdinalIgnoreCase);
            switch (op) { case "=": return c == 0; case "!=": case "<>": return c != 0; case "<": return c < 0; case ">": return c > 0; case "<=": return c <= 0; default: return c >= 0; }
        }
        /// <summary>SELECT props FROM Class [WHERE ...]: the class, the properties (null: all) and the condition.</summary>
        public static bool ParseQuery(string q, out string cls, out string[] props, out string where)
        {
            cls = null; props = null; where = null;
            var m = Regex.Match(q ?? "", @"^\s*SELECT\s+(.+?)\s+FROM\s+([A-Za-z0-9_]+)(?:\s+WHERE\s+(.+?))?\s*$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!m.Success) return false;
            cls = m.Groups[2].Value;
            string sel = m.Groups[1].Value.Trim();
            if (sel != "*") props = sel.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
            if (m.Groups[3].Success) where = m.Groups[3].Value;
            return true;
        }
    }

    public partial class PsMachine
    {
        /// <summary>More shares for Win32_Share ("Name|Path|Description|Type"), for other parts of Cert Lab to add to.</summary>
        public readonly List<string> ExtraShares = new List<string>();
    }
}
