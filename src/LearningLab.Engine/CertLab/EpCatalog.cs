// Cert Lab - the practice portal's catalogues: the settings catalog (a small choice of real Windows policy settings,
// with the policy area and value each writes under PolicyManager on the device), the attack surface reduction
// rules, the pretend .intunewin packages (what their installers need and leave on the PC) and the pretend store.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CertLab
{
    public class EpSetting
    {
        public string Key = "", Category = "", Name = "", Description = "", Type = "choice", RegPath = "";
        public string[] Options = new string[0];      // "Display=data"
        public int Min, Max;
        public string Area { get { return Key.Substring(0, Key.IndexOf('/')); } }
        public string ValueName { get { return Key.Substring(Key.IndexOf('/') + 1); } }
        public string Data(string display)
        {
            if (Type != "choice") return display;
            foreach (var o in Options) { int e = o.LastIndexOf('='); if (EpText.Eq(o.Substring(0, e), display)) return o.Substring(e + 1); }
            return null;
        }
        public IEnumerable<string> Choices { get { return Options.Select(o => o.Substring(0, o.LastIndexOf('='))); } }
    }

    public class EpAsrRule { public string Key = "", Name = "", Guid = ""; }

    /// <summary>A pretend Win32 app package: the file you upload, the setup inside it, what a silent install needs,
    /// and what the install leaves on the PC (so detection rules can be right or wrong).</summary>
    public class EpPackage
    {
        public string File = "", Setup = "", App = "", Publisher = "", Version = "", InstallPath = "", ExeName = "", UninstallKey = "", MsiCode = "", NeedsApp = "";
        public string[] Silent = new string[0];      // one of these must be in the install command
        public string[] Arch = { "x64" };
        public string MinOs = "";                       // e.g. 10.0.22631 (the installer refuses older)
        public int ExitCode;                            // what a good install returns (0, or 3010 for a soft restart)
    }

    public class EpStoreApp { public string Id = "", Name = "", Publisher = ""; }

    public static class EpCatalog
    {
        static List<EpSetting> settings;
        public static List<EpSetting> Settings { get { if (settings == null) Init(); return settings; } }
        public static EpSetting Find(string key) { return Settings.FirstOrDefault(s => EpText.Eq(s.Key, key) || EpText.Eq(s.Name, key)); }

        static EpSetting S(string key, string cat, string name, string desc, params string[] opts) { var s = new EpSetting { Key = key, Category = cat, Name = name, Description = desc, Options = opts }; settings.Add(s); return s; }
        static EpSetting N(string key, string cat, string name, string desc, int min, int max) { var s = new EpSetting { Key = key, Category = cat, Name = name, Description = desc, Type = "number", Min = min, Max = max }; settings.Add(s); return s; }
        static EpSetting Tx(string key, string cat, string name, string desc) { var s = new EpSetting { Key = key, Category = cat, Name = name, Description = desc, Type = "text" }; settings.Add(s); return s; }

        static void Init()
        {
            settings = new List<EpSetting>();
            S("Camera/AllowCamera", "Camera", "Allow Camera", "Whether the camera can be used.", "Not allowed=0", "Allowed=1");
            S("Connectivity/AllowBluetooth", "Connectivity", "Allow Bluetooth", "Whether people can turn Bluetooth on.", "Disallow Bluetooth=0", "Allow=2");
            S("Experience/AllowCortana", "Experience", "Allow Cortana", "Whether Cortana is available.", "Block=0", "Allow=1");
            S("Experience/AllowWindowsConsumerFeatures", "Experience", "Allow Windows Consumer Features", "Suggested apps and offers from the Store on the Start menu.", "Block=0", "Allow=1");
            S("Experience/AllowManualMDMUnenrollment", "Experience", "Allow Manual MDM Unenrollment", "Whether the user can remove the work account (and so unenrol) in Settings.", "Block=0", "Allow=1");
            N("DeviceLock/MinDevicePasswordLength", "Device Lock", "Min Device Password Length", "The shortest password or PIN allowed.", 4, 16);
            N("DeviceLock/MaxInactivityTimeDeviceLock", "Device Lock", "Max Inactivity Time Device Lock (minutes)", "Lock the screen after this many idle minutes.", 0, 999);
            S("Privacy/LetAppsAccessCamera", "Privacy", "Let Apps Access Camera", "Whether apps may use the camera.", "User in control=0", "Force allow=1", "Force deny=2");
            S("Privacy/LetAppsAccessLocation", "Privacy", "Let Apps Access Location", "Whether apps may use the location.", "User in control=0", "Force allow=1", "Force deny=2");
            S("System/AllowTelemetry", "System", "Allow Telemetry", "How much diagnostic data Windows sends. Update reports and Endpoint analytics need at least Required (Basic).", "Security=0", "Basic=1", "Enhanced=2", "Full=3");
            S("Storage/RemovableDiskDenyWriteAccess", "Storage", "Removable Disk Deny Write Access", "Block writing to USB drives and other removable disks.", "Disabled=0", "Enabled=1");
            S("DeliveryOptimization/DODownloadMode", "Delivery Optimization", "DO Download Mode", "Where devices may fetch update pieces from.", "HTTP only=0", "LAN=1", "Group=2", "Internet=3", "Simple=99", "Bypass=100");
            S("Start/HideRecentlyAddedApps", "Start", "Hide Recently Added Apps", "Hide the list of new apps on Start.", "Disabled=0", "Enabled=1");
            Tx("LocalPoliciesSecurityOptions/InteractiveLogon_MessageTitleForUsersAttemptingToLogOn", "Local Policies Security Options", "Interactive Logon Message Title For Users Attempting To Log On", "The title of the notice shown before sign-in.");
            Tx("LocalPoliciesSecurityOptions/InteractiveLogon_MessageTextForUsersAttemptingToLogOn", "Local Policies Security Options", "Interactive Logon Message Text For Users Attempting To Log On", "The text of the notice shown before sign-in.");
            S("Defender/AllowRealtimeMonitoring", "Defender", "Allow Realtime Monitoring", "Microsoft Defender Antivirus real-time protection.", "Not allowed=0", "Allowed=1");
            S("ApplicationManagement/AllowGameDVR", "Application Management", "Allow Game DVR", "Recording and broadcasting of games.", "Block=0", "Allow=1");
            Tx("Settings/PageVisibilityList", "Settings", "Page Visibility List", "Pages of the Settings app to show or hide, for example hide:gaming-gamebar;windowsupdate.");
            S("LanmanWorkstation/EnableInsecureGuestLogons", "Lanman Workstation", "Enable Insecure Guest Logons", "Connecting to file shares without signing in (unsafe).", "Disabled=0", "Enabled=1");
            S("Accounts/AllowMicrosoftAccountConnection", "Accounts", "Allow Microsoft Account Connection", "Whether a personal Microsoft account can be used on the device.", "Not allowed=0", "Allowed=1");
            S("DeviceGuard/LsaCfgFlags", "Device Guard", "Credential Guard", "Protect sign-in secrets with virtualisation-based security.", "Disabled=0", "Enabled with UEFI lock=1", "Enabled without lock=2");
            Tx("Edge/HomepageLocation", "Microsoft Edge", "Configure the home page URL", "The page the Home button opens.").RegPath = "SOFTWARE\\Policies\\Microsoft\\Edge";
            S("WindowsLogon/EnableFirstLogonAnimation", "Windows Logon", "Enable First Logon Animation", "The \"Hi\" animation at the first sign-in.", "Disabled=0", "Enabled=1");
            S("Bluetooth/AllowDiscoverableMode", "Bluetooth", "Allow Discoverable Mode", "Whether other devices can find this PC over Bluetooth.", "Block=0", "Allow=1");
        }

        public static readonly List<EpAsrRule> AsrRules = new List<EpAsrRule>
        {
            new EpAsrRule { Key = "emailExe", Name = "Block executable content from email client and webmail", Guid = "be9ba2d9-53ea-4cdc-84e5-9b1eeee46550" },
            new EpAsrRule { Key = "officeChild", Name = "Block all Office applications from creating child processes", Guid = "d4f940ab-401b-4efc-aadc-ad5f3c50688a" },
            new EpAsrRule { Key = "officeExe", Name = "Block Office applications from creating executable content", Guid = "3b576869-a4ec-4529-8536-b80a7769e899" },
            new EpAsrRule { Key = "officeInject", Name = "Block Office applications from injecting code into other processes", Guid = "75668c1f-73b5-4cf0-bb93-3ecf5cb7cc84" },
            new EpAsrRule { Key = "scriptDownload", Name = "Block JavaScript or VBScript from launching downloaded executable content", Guid = "d3e037e1-3eb8-44c8-a917-57927947596d" },
            new EpAsrRule { Key = "obfuscated", Name = "Block execution of potentially obfuscated scripts", Guid = "5beb7efe-fd9a-4556-801d-275e5ffc04cc" },
            new EpAsrRule { Key = "macroWin32", Name = "Block Win32 API calls from Office macros", Guid = "92e97fa1-2edf-4476-bdd6-9dd0b4dddc7b" },
            new EpAsrRule { Key = "lsass", Name = "Block credential stealing from the Windows local security authority subsystem", Guid = "9e6c4e1f-7d60-472f-ba1a-a39ef669e4b2" },
            new EpAsrRule { Key = "psexecWmi", Name = "Block process creations originating from PSExec and WMI commands", Guid = "d1e49aac-8f56-4280-b9ba-993a6d77406c" },
            new EpAsrRule { Key = "usb", Name = "Block untrusted and unsigned processes that run from USB", Guid = "b2b3f03d-6a65-4f7b-a9c7-1c7ef74a9ba4" },
            new EpAsrRule { Key = "ransomware", Name = "Use advanced protection against ransomware", Guid = "c1db55ab-c21a-4637-bb3f-a12568109d35" },
        };
        public static EpAsrRule Asr(string key) { return AsrRules.FirstOrDefault(r => EpText.Eq(r.Key, key) || EpText.Eq(r.Name, key)); }
        public static readonly string[] AsrModes = { "Not configured", "Off", "Block", "Audit", "Warn" };

        public static readonly List<EpPackage> Packages = new List<EpPackage>
        {
            new EpPackage { File = "7z2301-x64.intunewin", Setup = "7z2301-x64.exe", App = "7-Zip", Publisher = "Igor Pavlov", Version = "23.01", InstallPath = "C:\\Program Files\\7-Zip", ExeName = "7z.exe", UninstallKey = "7-Zip", Silent = new[] { "/S" } },
            new EpPackage { File = "npp.8.6.Installer.x64.intunewin", Setup = "npp.8.6.Installer.x64.exe", App = "Notepad++", Publisher = "Notepad++ Team", Version = "8.6", InstallPath = "C:\\Program Files\\Notepad++", ExeName = "notepad++.exe", UninstallKey = "Notepad++", Silent = new[] { "/S" } },
            new EpPackage { File = "RiverbendRuntime-1.0.intunewin", Setup = "rbruntime.exe", App = "Riverbend Runtime", Publisher = "Riverbend IT", Version = "1.0", InstallPath = "C:\\Program Files\\Riverbend\\Runtime", ExeName = "rbrt.dll", UninstallKey = "RiverbendRuntime", Silent = new[] { "/quiet" } },
            new EpPackage { File = "RiverbendExpenses-2.4.intunewin", Setup = "RiverbendExpenses-2.4.msi", App = "Riverbend Expenses", Publisher = "Riverbend IT", Version = "2.4", InstallPath = "C:\\Program Files\\Riverbend\\Expenses", ExeName = "Expenses.exe", UninstallKey = "{6B1A3C55-2F0E-4C7A-9D11-0A7E5B3C2D41}", MsiCode = "{6B1A3C55-2F0E-4C7A-9D11-0A7E5B3C2D41}", Silent = new[] { "/qn", "/quiet" }, NeedsApp = "Riverbend Runtime" },
            new EpPackage { File = "RiverbendExpenses-3.0.intunewin", Setup = "RiverbendExpenses-3.0.msi", App = "Riverbend Expenses", Publisher = "Riverbend IT", Version = "3.0", InstallPath = "C:\\Program Files\\Riverbend\\Expenses", ExeName = "Expenses.exe", UninstallKey = "{9C2E7D10-5B44-4F3A-8E6D-1F2A3B4C5D60}", MsiCode = "{9C2E7D10-5B44-4F3A-8E6D-1F2A3B4C5D60}", Silent = new[] { "/qn", "/quiet" }, NeedsApp = "Riverbend Runtime" },
            new EpPackage { File = "RiverbendVPN-5.2.intunewin", Setup = "RbVpnSetup.exe", App = "Riverbend VPN", Publisher = "Riverbend IT", Version = "5.2", InstallPath = "C:\\Program Files\\Riverbend\\VPN", ExeName = "rbvpn.exe", UninstallKey = "RiverbendVPN", Silent = new[] { "/quiet" }, ExitCode = 3010 },
            new EpPackage { File = "LabelPrinter-1.2.intunewin", Setup = "LabelPrinterSetup.exe", App = "Label Printer Tool", Publisher = "Riverbend IT", Version = "1.2", InstallPath = "C:\\Program Files\\LabelPrinter", ExeName = "labels.exe", UninstallKey = "LabelPrinter", Silent = new[] { "/silent" }, MinOs = "10.0.22631" },
        };
        public static EpPackage Package(string file) { return Packages.FirstOrDefault(p => EpText.Eq(p.File, file)); }

        public static readonly List<EpStoreApp> Store = new List<EpStoreApp>
        {
            new EpStoreApp { Id = "9PRCL0000001", Name = "Company Portal", Publisher = "Microsoft Corporation" },
            new EpStoreApp { Id = "9PRCL0000002", Name = "Windows Terminal", Publisher = "Microsoft Corporation" },
            new EpStoreApp { Id = "9PRCL0000003", Name = "Microsoft To Do", Publisher = "Microsoft Corporation" },
            new EpStoreApp { Id = "9PRCL0000004", Name = "Microsoft Whiteboard", Publisher = "Microsoft Corporation" },
            new EpStoreApp { Id = "9PRCL0000005", Name = "Power BI Desktop", Publisher = "Microsoft Corporation" },
            new EpStoreApp { Id = "9PRCL0000006", Name = "Microsoft PowerToys", Publisher = "Microsoft Corporation" },
        };
        public static EpStoreApp StoreApp(string idOrName) { return Store.FirstOrDefault(s => EpText.Eq(s.Id, idOrName) || EpText.Eq(s.Name, idOrName)); }

        /// <summary>Windows versions in the simulation, newest first: name, build, version string.</summary>
        public static readonly string[][] WinVersions =
        {
            new[] { "Windows 11, version 24H2", "26100", "24H2", "11" },
            new[] { "Windows 11, version 23H2", "22631", "23H2", "11" },
            new[] { "Windows 10, version 22H2", "19045", "22H2", "10" },
        };
    }
}
