// Cert Lab - the Group Policy settings the AZ-800 course's pretend Group Policy Management Editor knows: where each one
// sits in the editor's tree, the registry value it writes (Set-GPRegistryValue with the same key and value name is the
// same setting), and its options. Security settings (password and lockout policy) aren't registry values. Wording
// follows the real editor closely enough to find the setting; the list is short on purpose.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CertLab
{
    public class GpSetting
    {
        public string Id = "", Scope = "C", Path = "", Name = "", Key = "", ValueName = "", Type = "DWord", Kind = "toggle", Explain = "";
        public string On = "1", Off = "0";
        public List<string> Options = new List<string>();     // kind "choice": the option names, written as their position (or the value after '=')
        public string Folder { get { return (Scope == "C" ? "Computer Configuration" : "User Configuration") + "/Policies/" + Path; } }
        public bool IsRegistry { get { return Key.Length > 0; } }
        /// <summary>The registry data a value of this setting writes ("Enabled", "Disabled", a number, text, or a choice).</summary>
        public string Data(string state, string value)
        {
            if (WsText.Eq(state, "Disabled")) return Off;
            if (Kind == "toggle") return On;
            if (Kind == "choice") { foreach (var o in Options) { var p = o.Split('='); if (WsText.Eq(p[0], value)) return p.Length > 1 ? p[1] : (Options.IndexOf(o) + 1).ToString(CultureInfo.InvariantCulture); } return value; }
            return value;
        }
    }

    public static class GpCatalog
    {
        // id|scope|path (Administrative Templates/... or Windows Settings/...)|name|key|value name|type|kind|on|off|options (a,b=2)
        static readonly string[] Data = {
            "LockScreenCamera|C|Administrative Templates/Control Panel/Personalization|Prevent enabling lock screen camera|HKLM\\Software\\Policies\\Microsoft\\Windows\\Personalization|NoLockScreenCamera|DWord|toggle|1|0|",
            "LockScreenImage|C|Administrative Templates/Control Panel/Personalization|Force a specific default lock screen and logon image|HKLM\\Software\\Policies\\Microsoft\\Windows\\Personalization|LockScreenImage|String|text|||",
            "Loopback|C|Administrative Templates/System/Group Policy|Configure user Group Policy loopback processing mode|HKLM\\Software\\Policies\\Microsoft\\Windows\\System|UserPolicyMode|DWord|choice|||Merge=1,Replace=2",
            "RefreshInterval|C|Administrative Templates/System/Group Policy|Set Group Policy refresh interval for computers|HKLM\\Software\\Policies\\Microsoft\\Windows\\System|GroupPolicyRefreshTime|DWord|number|||",
            "WaitForNetwork|C|Administrative Templates/System/Logon|Always wait for the network at computer startup and logon|HKLM\\Software\\Policies\\Microsoft\\Windows NT\\CurrentVersion\\Winlogon|SyncForegroundPolicy|DWord|toggle|1|0|",
            "AutoUpdate|C|Administrative Templates/Windows Components/Windows Update/Manage end user experience|Configure Automatic Updates|HKLM\\Software\\Policies\\Microsoft\\Windows\\WindowsUpdate\\AU|AUOptions|DWord|choice|||Notify for download and auto install=2,Auto download and notify for install=3,Auto download and schedule the install=4,Allow local admin to choose setting=5",
            "WsusServer|C|Administrative Templates/Windows Components/Windows Update/Manage updates offered from Windows Server Update Service|Specify intranet Microsoft update service location|HKLM\\Software\\Policies\\Microsoft\\Windows\\WindowsUpdate|WUServer|String|text|||",
            "AllowRdp|C|Administrative Templates/Windows Components/Remote Desktop Services/Remote Desktop Session Host/Connections|Allow users to connect remotely by using Remote Desktop Services|HKLM\\Software\\Policies\\Microsoft\\Windows NT\\Terminal Services|fDenyTSConnections|DWord|toggle|0|1|",
            "FirewallDomain|C|Administrative Templates/Network/Network Connections/Windows Defender Firewall/Domain Profile|Windows Defender Firewall: Protect all network connections|HKLM\\Software\\Policies\\Microsoft\\WindowsFirewall\\DomainProfile|EnableFirewall|DWord|toggle|1|0|",
            "DenyRemovable|C|Administrative Templates/System/Removable Storage Access|All Removable Storage classes: Deny all access|HKLM\\Software\\Policies\\Microsoft\\Windows\\RemovableStorageDevices|Deny_All|DWord|toggle|1|0|",
            "PsExecution|C|Administrative Templates/Windows Components/Windows PowerShell|Turn on Script Execution|HKLM\\Software\\Policies\\Microsoft\\Windows\\PowerShell|ExecutionPolicy|String|choice|||Allow only signed scripts=AllSigned,Allow local scripts and remote signed scripts=RemoteSigned,Allow all scripts=Unrestricted",
            "PsScriptBlockLog|C|Administrative Templates/Windows Components/Windows PowerShell|Turn on PowerShell Script Block Logging|HKLM\\Software\\Policies\\Microsoft\\Windows\\PowerShell\\ScriptBlockLogging|EnableScriptBlockLogging|DWord|toggle|1|0|",
            "WinRmAllow|C|Administrative Templates/Windows Components/Windows Remote Management (WinRM)/WinRM Service|Allow remote server management through WinRM|HKLM\\Software\\Policies\\Microsoft\\Windows\\WinRM\\Service|AllowAutoConfig|DWord|toggle|1|0|",
            "DnsSuffixList|C|Administrative Templates/Network/DNS Client|DNS suffix search list|HKLM\\Software\\Policies\\Microsoft\\Windows NT\\DNSClient|SearchList|String|text|||",
            "WakePassword|C|Administrative Templates/System/Power Management/Sleep Settings|Require a password when a computer wakes (plugged in)|HKLM\\Software\\Policies\\Microsoft\\Power\\PowerSettings\\0e796bdb-100d-47d6-a2d5-f7d2daa51f51|ACSettingIndex|DWord|toggle|1|0|",
            "LogonTitle|C|Windows Settings/Security Settings/Local Policies/Security Options|Interactive logon: Message title for users attempting to log on|HKLM\\Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\System|legalnoticecaption|String|text|||",
            "LogonText|C|Windows Settings/Security Settings/Local Policies/Security Options|Interactive logon: Message text for users attempting to log on|HKLM\\Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\System|legalnoticetext|String|text|||",
            "MinPwdLength|C|Windows Settings/Security Settings/Account Policies/Password Policy|Minimum password length||minPwdLength|sec|number|||",
            "PwdHistory|C|Windows Settings/Security Settings/Account Policies/Password Policy|Enforce password history||pwdHistoryLength|sec|number|||",
            "MaxPwdAge|C|Windows Settings/Security Settings/Account Policies/Password Policy|Maximum password age||maxPwdAge|sec|number|||",
            "PwdComplexity|C|Windows Settings/Security Settings/Account Policies/Password Policy|Password must meet complexity requirements||complexityEnabled|sec|toggle|TRUE|FALSE|",
            "LockoutThreshold|C|Windows Settings/Security Settings/Account Policies/Account Lockout Policy|Account lockout threshold||lockoutThreshold|sec|number|||",
            "LockoutDuration|C|Windows Settings/Security Settings/Account Policies/Account Lockout Policy|Account lockout duration||lockoutDuration|sec|number|||",
            "NoControlPanel|U|Administrative Templates/Control Panel|Prohibit access to Control Panel and PC settings|HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\Explorer|NoControlPanel|DWord|toggle|1|0|",
            "Wallpaper|U|Administrative Templates/Desktop/Desktop|Desktop Wallpaper|HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\System|Wallpaper|String|text|||",
            "NoCmd|U|Administrative Templates/System|Prevent access to the command prompt|HKCU\\Software\\Policies\\Microsoft\\Windows\\System|DisableCMD|DWord|toggle|1|0|",
            "NoRegedit|U|Administrative Templates/System|Prevent access to registry editing tools|HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\System|DisableRegistryTools|DWord|toggle|1|0|",
            "NoRun|U|Administrative Templates/Start Menu and Taskbar|Remove Run menu from Start Menu|HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\Explorer|NoRun|DWord|toggle|1|0|",
            "ScreenSaver|U|Administrative Templates/Control Panel/Personalization|Enable screen saver|HKCU\\Software\\Policies\\Microsoft\\Windows\\Control Panel\\Desktop|ScreenSaveActive|String|toggle|1|0|",
            "ScreenSaverTimeout|U|Administrative Templates/Control Panel/Personalization|Screen saver timeout|HKCU\\Software\\Policies\\Microsoft\\Windows\\Control Panel\\Desktop|ScreenSaveTimeOut|String|number|||",
            "ScreenSaverSecure|U|Administrative Templates/Control Panel/Personalization|Password protect the screen saver|HKCU\\Software\\Policies\\Microsoft\\Windows\\Control Panel\\Desktop|ScreenSaverIsSecure|String|toggle|1|0|",
            "UserDenyRemovable|U|Administrative Templates/System/Removable Storage Access|All Removable Storage classes: Deny all access|HKCU\\Software\\Policies\\Microsoft\\Windows\\RemovableStorageDevices|Deny_All|DWord|toggle|1|0|",
            "NoRecycleBinIcon|U|Administrative Templates/Desktop|Remove Recycle Bin icon from desktop|HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Policies\\NonEnum|{645FF040-5081-101B-9F08-00AA002F954E}|DWord|toggle|1|0|",
            "DriveMap|U|Preferences/Windows Settings/Drive Maps|Mapped drive (letter=\\\\server\\share)||DriveMap|pref|text|||",
        };

        static List<GpSetting> all;
        public static List<GpSetting> All
        {
            get
            {
                if (all != null) return all;
                var l = new List<GpSetting>();
                foreach (var row in Data)
                {
                    var p = row.Split('|');
                    var s = new GpSetting { Id = p[0], Scope = p[1], Path = p[2], Name = p[3], Key = p[4], ValueName = p[5], Type = p[6], Kind = p[7], On = p[8], Off = p[9] };
                    if (p[10].Length > 0) s.Options = p[10].Split(',').ToList();
                    if (s.Type == "sec" || s.Type == "pref") s.Key = "";
                    l.Add(s);
                }
                all = l;
                return all;
            }
        }
        public static GpSetting Get(string id) { return All.FirstOrDefault(s => WsText.Eq(s.Id, id) || WsText.Eq(s.Name, id) || WsText.Eq(s.Folder + "/" + s.Name, id)); }
        /// <summary>The catalog setting a registry value belongs to (Set-GPRegistryValue), or null.</summary>
        public static GpSetting ByRegistry(string key, string valueName)
        {
            string k = NormKey(key);
            return All.FirstOrDefault(s => s.IsRegistry && WsText.Eq(NormKey(s.Key), k) && WsText.Eq(s.ValueName, valueName));
        }
        public static string NormKey(string k)
        {
            k = (k ?? "").Replace("HKEY_LOCAL_MACHINE", "HKLM").Replace("HKEY_CURRENT_USER", "HKCU").Replace("HKLM:", "HKLM").Replace("HKCU:", "HKCU").TrimEnd('\\');
            return k;
        }
    }
}
