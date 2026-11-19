// Cert Lab - checks of the simulated PowerShell's networking, module, security and DSC commands against Windows
// PowerShell 5.1's output and behaviour (groups ps-net, ps-modules, ps-security, ps-dsc).
using System;
using System.Collections.Generic;
using System.Linq;

namespace CertLab
{
    public static partial class Tests
    {
        static partial void PsGroupsNet(List<KeyValuePair<string, Action>> l)
        {
            l.Add(new KeyValuePair<string, Action>("ps-net", PsNetTests));
            l.Add(new KeyValuePair<string, Action>("ps-modules", PsModuleTests));
            l.Add(new KeyValuePair<string, Action>("ps-security", PsSecurityTests));
            l.Add(new KeyValuePair<string, Action>("ps-dsc", PsDscTests));
        }

        static void Has(string name, string got, string part) { Check(name, got.Contains(part), "got:\n" + got + "\nwanted a part:\n" + part); }
        static void Lacks(string name, string got, string part) { Check(name, !got.Contains(part), "got:\n" + got + "\nshould not contain:\n" + part); }

        static void PsNetTests()
        {
            var t = new P();
            Eq("Get-NetAdapter table", t.Run("Get-NetAdapter"), "\nName                      InterfaceDescription                    ifIndex Status       MacAddress             LinkSpeed\n----                      --------------------                    ------- ------       ----------             ---------\nEthernet                  Microsoft Hyper-V Network Adapter             6 Up           00-15-5D-0A-0A-0B        10 Gbps\n\n\n");
            Has("Get-NetAdapter -Name not found", t.Run("Get-NetAdapter -Name Wi-Fi"), "Get-NetAdapter : No MSFT_NetAdapter objects found with property 'Name' equal to 'Wi-Fi'.  Verify the value of the property and retry.");
            Has("not found: FullyQualifiedErrorId", t.Last, "CmdletizationQuery_NotFound_Name,Get-NetAdapter");
            Eq("Get-NetAdapter wildcard without match: no error", t.T("Get-NetAdapter -Name Wi*"), "");
            Eq("Get-NetIPAddress IPv4 list", t.Run("Get-NetIPAddress -AddressFamily IPv4 -InterfaceAlias Ethernet"), "\nIPAddress         : 10.0.10.11\nInterfaceIndex    : 6\nInterfaceAlias    : Ethernet\nAddressFamily     : IPv4\nType              : Unicast\nPrefixLength      : 24\nPrefixOrigin      : Manual\nSuffixOrigin      : Manual\nAddressState      : Preferred\nValidLifetime     : Infinite ([TimeSpan]::MaxValue)\nPreferredLifetime : Infinite ([TimeSpan]::MaxValue)\nSkipAsSource      : False\nPolicyStore       : ActiveStore\n\n\n");
            Eq("Get-NetIPAddress all four", t.T("(Get-NetIPAddress).Count"), "4");
            Eq("IPv6 link-local has %ifIndex", t.T("(Get-NetIPAddress -AddressFamily IPv6 -InterfaceIndex 6).IPAddress"), "fe80::8c41:5d2a:3b1f:a0b%6");
            Eq("Where-Object on AddressFamily", t.T("Get-NetIPAddress | Where-Object AddressFamily -eq 'IPv4' | ForEach-Object IPAddress"), "10.0.10.11\n127.0.0.1");
            Has("Get-NetIPAddress no match: CIM query error", t.Run("Get-NetIPAddress -InterfaceAlias Wi-Fi"), "No matching MSFT_NetIPAddress objects found by CIM query for instances of the ROOT/StandardCimv2/MSFT_NetIPAddress class");
            Eq("Get-NetIPConfiguration list", t.Run("Get-NetIPConfiguration"), "\nInterfaceAlias       : Ethernet\nInterfaceIndex       : 6\nInterfaceDescription : Microsoft Hyper-V Network Adapter\nNetProfile.Name      : lab.example.com\nIPv4Address          : 10.0.10.11\nIPv6DefaultGateway   :\nIPv4DefaultGateway   : 10.0.10.1\nDNSServer            : 10.0.10.10\n\n\n");
            Eq("Get-DnsClientServerAddress two-line header", t.Run("Get-DnsClientServerAddress"), "\nInterfaceAlias               Interface Address ServerAddresses\n                             Index     Family\n--------------               --------- ------- ---------------\nEthernet                             6 IPv4    {10.0.10.10}\nEthernet                             6 IPv6    {}\nLoopback Pseudo-Interface 1          1 IPv4    {}\nLoopback Pseudo-Interface 1          1 IPv6    {fec0:0:0:ffff::1, fec0:0:0:ffff::2, fec0:0:0:ffff::3}\n\n\n");
            Eq("Get-DnsClientServerAddress -AddressFamily IPv4", t.T("(Get-DnsClientServerAddress -InterfaceAlias Ethernet -AddressFamily IPv4).ServerAddresses"), "10.0.10.10");
            Eq("Resolve-DnsName A table", t.Run("Resolve-DnsName web01"), "\nName                                           Type   TTL   Section    IPAddress\n----                                           ----   ---   -------    ---------\nweb01.lab.example.com                          A      1200  Answer     10.0.10.50\n\n\n");
            Has("Resolve-DnsName CNAME table", t.Run("Resolve-DnsName www.lab.example.com"), "www.lab.example.com            CNAME  3600  Answer     web01.lab.example.com");
            Has("CNAME chain: the A record as a list", t.Last, "Name       : web01.lab.example.com\nQueryType  : A\nTTL        : 1200\nSection    : Answer\nIP4Address : 10.0.10.50");
            Eq("Resolve-DnsName -Type CNAME only", t.T("(Resolve-DnsName www.lab.example.com -Type CNAME).NameHost"), "web01.lab.example.com");
            Has("Resolve-DnsName unknown name", t.Run("Resolve-DnsName nosuch"), "Resolve-DnsName : nosuch : DNS name does not exist\n");
            Has("unknown name: id and category", t.Last, "ResourceUnavailable: (nosuch:String) [Resolve-DnsName], Win32Exception\n    + FullyQualifiedErrorId : DNS_ERROR_RCODE_NAME_ERROR,Microsoft.DnsClient.Commands.ResolveDnsName");
            Has("no internet names", t.Run("Resolve-DnsName www.microsoft.com"), "www.microsoft.com : DNS name does not exist");
            Eq("Resolve-DnsName of an address: PTR", t.T("(Resolve-DnsName 10.0.10.50).NameHost"), "web01.lab.example.com");
            Has("Resolve-DnsName -Type NS", t.Run("Resolve-DnsName lab.example.com -Type NS"), "lab.example.com                NS     3600  Answer     dc01.lab.example.com");
            Has("Resolve-DnsName -Type MX: SOA in Authority", t.Run("Resolve-DnsName lab.example.com -Type MX"), "SOA  3600  Authority  dc01.lab.example.com");
            Eq("own name resolves without DNS", t.T("(Resolve-DnsName WIN-SRV1 -Type A).IPAddress"), "10.0.10.11");
            Eq("hosts file entries win", t.T("[System.IO.File]::AppendAllText('C:\\Windows\\System32\\drivers\\etc\\hosts', \"10.0.10.50 shop`r`n\"); (Resolve-DnsName shop).IPAddress"), "10.0.10.50");
            Eq("[System.Net.Dns]::GetHostAddresses", t.T("[System.Net.Dns]::GetHostAddresses('dc01')[0].IPAddressToString"), "10.0.10.10");
            Eq("[System.Net.Dns]::GetHostEntry aliases", t.T("([System.Net.Dns]::GetHostEntry('intranet.lab.example.com')).HostName"), "web01.lab.example.com");
            Has("[System.Net.Dns] unknown host", t.Run("[System.Net.Dns]::GetHostAddresses('nosuch')"), "Exception calling \"GetHostAddresses\" with \"1\" argument(s): \"No such host is known\"");
            Eq("Test-NetConnection ping", t.Run("Test-NetConnection web01"), "\nComputerName           : web01\nRemoteAddress          : 10.0.10.50\nInterfaceAlias         : Ethernet\nSourceAddress          : 10.0.10.11\nPingSucceeded          : True\nPingReplyDetails (RTT) : 0 ms\n\n\n");
            Eq("Test-NetConnection -Port open", t.Run("Test-NetConnection web01 -Port 80"), "\nComputerName     : web01\nRemoteAddress    : 10.0.10.50\nRemotePort       : 80\nInterfaceAlias   : Ethernet\nSourceAddress    : 10.0.10.11\nTcpTestSucceeded : True\n\n\n");
            Has("Test-NetConnection -Port closed: warning", t.Run("Test-NetConnection web01 -Port 8080"), "WARNING: TCP connect to (10.0.10.50 : 8080) failed\n");
            Has("closed port: TcpTestSucceeded False and ping details", t.Last, "PingSucceeded          : True\nPingReplyDetails (RTT) : 0 ms\nTcpTestSucceeded       : False");
            Eq("-InformationLevel Quiet", t.T("Test-NetConnection dc01 -Port 389 -InformationLevel Quiet"), "True");
            Eq("-CommonTCPPort RDP to WEB01", t.T("(Test-NetConnection web01 -CommonTCPPort RDP).TcpTestSucceeded"), "True");
            Has("unknown name warning", t.Run("Test-NetConnection nosuch"), "WARNING: Name resolution of nosuch failed");
            Has("unknown name: no ping details", t.Last, "PingSucceeded  : False");
            Has("WIN-CL1 blocks ping and WinRM by default", t.Run("Test-NetConnection WIN-CL1 -CommonTCPPort WINRM"), "WARNING: TCP connect to (10.0.10.21 : 5985) failed\nWARNING: Ping to 10.0.10.21 failed with status: TimedOut");
            Eq("WIN-CL1 SMB blocked by its firewall", t.T("Test-NetConnection WIN-CL1 -Port 445 -InformationLevel Quiet -WarningAction SilentlyContinue"), "False");
            t.On("WIN-CL1", "Enable-NetFirewallRule -Name FPS-SMB-In-TCP, FPS-ICMP4-ERQ-In");
            Eq("enabling the rule on WIN-CL1 opens SMB", t.T("Test-NetConnection WIN-CL1 -Port 445 -InformationLevel Quiet"), "True");
            Eq("and ping", t.T("Test-NetConnection WIN-CL1 -InformationLevel Quiet"), "True");
            t.On("WIN-CL1", "New-NetFirewallRule -DisplayName 'Block SMB from SRV1' -Direction Inbound -Protocol TCP -LocalPort 445 -RemoteAddress 10.0.10.11 -Action Block | Out-Null");
            Eq("a Block rule wins over Allow", t.T("Test-NetConnection WIN-CL1 -Port 445 -InformationLevel Quiet -WarningAction SilentlyContinue"), "False");
            t.On("WIN-CL1", "Set-NetFirewallProfile -Profile Domain,Public,Private -Enabled False");
            Eq("firewall off: everything that listens answers", t.T("Test-NetConnection WIN-CL1 -Port 445 -InformationLevel Quiet"), "True");
            t.On("WIN-CL1", "Set-NetFirewallProfile -All -Enabled True");
            Eq("Get-NetFirewallProfile Enabled", t.T("(Get-NetFirewallProfile -Name Domain).Enabled"), "True");
            Has("Get-NetFirewallProfile list", t.Run("Get-NetFirewallProfile -Name Public"), "Name                            : Public\nEnabled                         : True\nDefaultInboundAction            : NotConfigured");
            Has("Get-NetFirewallRule list", t.Run("Get-NetFirewallRule -Name WINRM-HTTP-In-TCP"), "Name                  : WINRM-HTTP-In-TCP\nDisplayName           : Windows Remote Management (HTTP-In)\nDescription           : Inbound rule for Windows Remote Management via WS-Management. [TCP 5985]\nDisplayGroup          : Windows Remote Management\nGroup                 : @FirewallAPI.dll,-30267\nEnabled               : True\nProfile               : Domain, Private\nPlatform              : {}\nDirection             : Inbound\nAction                : Allow");
            Has("Get-NetFirewallRule -DisplayName not found", t.Run("Get-NetFirewallRule -DisplayName 'Nope'"), "No MSFT_NetFirewallRule objects found with property 'DisplayName' equal to 'Nope'.");
            Eq("-Enabled False filter", t.T("(Get-NetFirewallRule -Enabled False | Measure-Object).Count"), "2");
            Eq("New-NetFirewallRule", t.T("New-NetFirewallRule -DisplayName 'Web 8080' -Direction Inbound -Protocol TCP -LocalPort 8080 -Action Allow | ForEach-Object { $_.DisplayName + '|' + $_.Enabled + '|' + $_.Profile }"), "Web 8080|True|Any");
            Eq("Get-NetFirewallPortFilter", t.T("Get-NetFirewallRule -DisplayName 'Web 8080' | Get-NetFirewallPortFilter | ForEach-Object { $_.Protocol + ' ' + $_.LocalPort }"), "TCP 8080");
            Has("port without TCP/UDP fails", t.Run("New-NetFirewallRule -DisplayName x -LocalPort 99"), "The port filter can only be set when the protocol is TCP or UDP");
            t.Run("Set-NetFirewallRule -DisplayName 'Web 8080' -Enabled False");
            Eq("Set-NetFirewallRule -Enabled", t.T("(Get-NetFirewallRule -DisplayName 'Web 8080').Enabled"), "False");
            t.Run("Get-NetFirewallRule -DisplayName 'Web 8080' | Remove-NetFirewallRule");
            Eq("Remove-NetFirewallRule by pipeline", t.T("Get-NetFirewallRule -DisplayName 'Web 8080' -ErrorAction SilentlyContinue; 'gone'"), "gone");
            Eq("Disable-NetFirewallRule -DisplayGroup", t.T("Disable-NetFirewallRule -DisplayGroup 'Remote Desktop'; @(Get-NetFirewallRule -DisplayGroup 'Remote Desktop' | Where-Object Enabled -eq 'True').Count"), "0");
            Eq("own RDP now blocked from WIN-CL1", t.On("WIN-CL1", "Test-NetConnection WIN-SRV1 -Port 3389 -InformationLevel Quiet -WarningAction SilentlyContinue").Replace("\r", "").Trim(), "False");
            Eq("Test-Connection table", t.Run("Test-Connection dc01 -Count 2"), "\nSource        Destination     IPV4Address      IPV6Address                              Bytes    Time(ms)\n------        -----------     -----------      -----------                              -----    --------\nWIN-SRV1      dc01            10.0.10.10                                                32       0\nWIN-SRV1      dc01            10.0.10.10                                                32       0\n\n\n");
            Eq("Test-Connection -Quiet", t.T("Test-Connection web01 -Count 1 -Quiet"), "True");
            Has("Test-Connection failure", t.Run("Test-Connection 10.0.10.99 -Count 1"), "Test-Connection : Testing connection to computer '10.0.10.99' failed: Error due to lack of resources");
            Has("Test-Connection failure id", t.Last, "TestConnectionException,Microsoft.PowerShell.Commands.TestConnectionCommand");
            Has("Get-NetTCPConnection -State Listen", t.Run("Get-NetTCPConnection -State Listen -LocalPort 5985"), "::                                  5985      ::                                  0          Listen");
            Has("Get-NetTCPConnection none", t.Run("Get-NetTCPConnection -LocalPort 8080"), "No matching MSFT_NetTCPConnection objects found by CIM query");
            Has("Get-NetRoute default route", t.Run("Get-NetRoute -DestinationPrefix 0.0.0.0/0"), "6       0.0.0.0/0                                      10.0.10.1");
            Eq("New-NetIPAddress adds two stores", t.T("New-NetIPAddress -InterfaceAlias Ethernet -IPAddress 10.0.10.12 -PrefixLength 24 | ForEach-Object PolicyStore"), "ActiveStore\nPersistentStore");
            Eq("the new address answers ping from WIN-CL1", t.On("WIN-CL1", "Test-NetConnection 10.0.10.12 -InformationLevel Quiet").Replace("\r", "").Trim(), "True");
            Has("duplicate address", t.Run("New-NetIPAddress -InterfaceAlias Ethernet -IPAddress 10.0.10.12 -PrefixLength 24"), "New-NetIPAddress : The object already exists.");
            Has("duplicate: CIM error id", t.Last, "ResourceExists: (MSFT_NetIPAddress:ROOT/StandardCimv2/MSFT_NetIPAddress) [New-NetIPAddress], CimException\n    + FullyQualifiedErrorId : Windows System Error 5010,New-NetIPAddress");
            Has("second default gateway", t.Run("New-NetIPAddress -InterfaceAlias Ethernet -IPAddress 10.0.10.13 -PrefixLength 24 -DefaultGateway 10.0.10.1"), "Instance DefaultGateway already exists");
            t.Run("Remove-NetIPAddress -IPAddress 10.0.10.12 -Confirm:$false");
            t.Run("Remove-NetIPAddress -IPAddress 10.0.10.13 -Confirm:$false");
            Eq("Remove-NetIPAddress", t.T("(Get-NetIPAddress -AddressFamily IPv4).Count"), "2");
            Eq("Get-NetIPInterface Dhcp", t.T("(Get-NetIPInterface -InterfaceAlias Ethernet -AddressFamily IPv4).Dhcp"), "Disabled");
            t.Run("Set-DnsClientServerAddress -InterfaceAlias Ethernet -ServerAddresses 8.8.8.8");
            Has("an unreachable DNS server times out", t.Run("Resolve-DnsName web01"), "web01 : This operation returned because the timeout period expired");
            t.Run("Set-DnsClientServerAddress -InterfaceAlias Ethernet -ServerAddresses 10.0.10.10, 10.0.10.11");
            Has("two DNS servers in Get-NetIPConfiguration", t.Run("Get-NetIPConfiguration"), "DNSServer            : 10.0.10.10\n                       10.0.10.11");
            t.Run("Set-DnsClientServerAddress -InterfaceAlias Ethernet -ResetServerAddresses");
            Has("reset: no DNS servers", t.Run("Resolve-DnsName dc01"), "No DNS servers configured for local system");
            t.Run("Set-DnsClientServerAddress -InterfaceIndex 6 -ServerAddresses 10.0.10.10");
            Eq("Clear-DnsClientCache", t.T("Clear-DnsClientCache; 'ok'"), "ok");
            Has("Get-NetConnectionProfile", t.Run("Get-NetConnectionProfile"), "Name             : lab.example.com\nInterfaceAlias   : Ethernet\nInterfaceIndex   : 6\nNetworkCategory  : DomainAuthenticated");
            Eq("ipconfig", t.Run("ipconfig"), "\nWindows IP Configuration\n\n\nEthernet adapter Ethernet:\n\n   Connection-specific DNS Suffix  . :\n   Link-local IPv6 Address . . . . . : fe80::8c41:5d2a:3b1f:a0b%6\n   IPv4 Address. . . . . . . . . . . : 10.0.10.11\n   Subnet Mask . . . . . . . . . . . : 255.255.255.0\n   Default Gateway . . . . . . . . . : 10.0.10.1\n");
            Has("ipconfig /all", t.Run("ipconfig /all"), "   Host Name . . . . . . . . . . . . : WIN-SRV1\n   Primary Dns Suffix  . . . . . . . : lab.example.com\n");
            Has("ipconfig /all DNS and MAC", t.Last, "   Physical Address. . . . . . . . . : 00-15-5D-0A-0A-0B\n   DHCP Enabled. . . . . . . . . . . : No");
            Has("ipconfig /flushdns", t.Run("ipconfig /flushdns"), "Successfully flushed the DNS Resolver Cache.");
            Has("ping by name", t.Run("ping web01"), "Pinging web01.lab.example.com [10.0.10.50] with 32 bytes of data:\nReply from 10.0.10.50: bytes=32 time<1ms TTL=128\n");
            Has("ping -n 2", t.Run("ping -n 2 10.0.10.10"), "Packets: Sent = 2, Received = 2, Lost = 0 (0% loss),");
            Has("ping unknown", t.Run("ping nosuch"), "Ping request could not find host nosuch. Please check the name and try again.");
            Eq("ping exit code", t.T("ping nosuch | Out-Null; $LASTEXITCODE"), "1");
            Has("ping timeout to WIN-CL1 from a blocked rule? no: enabled earlier", t.Run("ping -n 1 10.0.10.99"), "Request timed out.");
            Has("nslookup", t.Run("nslookup www"), "Server:  dc01.lab.example.com\nAddress:  10.0.10.10\n\nName:    web01.lab.example.com\nAddress:  10.0.10.50\nAliases:  www.lab.example.com\n");
            Has("nslookup unknown", t.Run("nslookup nosuch"), "*** dc01.lab.example.com can't find nosuch: Non-existent domain");
            Has("netstat -an", t.Run("netstat -an"), "  TCP    0.0.0.0:5985           0.0.0.0:0              LISTENING");
            Has("tracert", t.Run("tracert web01"), "  1    <1 ms    <1 ms    <1 ms  web01.lab.example.com [10.0.10.50]\n\nTrace complete.");
            Has("route print", t.Run("route print"), "          0.0.0.0          0.0.0.0        10.0.10.1      10.0.10.11    271");
            // a client moved to another subnet without a gateway cannot reach the lab
            t.On("WIN-CL1", "Remove-NetIPAddress -IPAddress 10.0.10.21 -Confirm:$false; Remove-NetRoute -DestinationPrefix 0.0.0.0/0 -Confirm:$false; New-NetIPAddress -InterfaceAlias Ethernet -IPAddress 192.168.5.21 -PrefixLength 24 | Out-Null");
            Eq("another subnet: no way to DC01", t.On("WIN-CL1", "Test-NetConnection 10.0.10.10 -InformationLevel Quiet -WarningAction SilentlyContinue").Replace("\r", "").Trim(), "False");
            Eq("M.Ip follows the change", t.M.Net.Get("WIN-CL1").Ip, "192.168.5.21");
            t.Dispose();
        }

        static void PsModuleTests()
        {
            var t = new P();
            Eq("Get-Module at start", t.Run("Get-Module | Select-Object -First 1 -ExpandProperty Name"), "Microsoft.PowerShell.Management\n");
            Has("Get-Module table header", t.Run("Get-Module"), "\nModuleType Version    Name                                ExportedCommands\n---------- -------    ----                                ----------------\nManifest   3.1.0.0    Microsoft.PowerShell.Management     {");
            t.Run("[System.IO.Directory]::CreateDirectory('C:\\Program Files\\WindowsPowerShell\\Modules\\Inv') | Out-Null");
            t.Run("[System.IO.File]::WriteAllText('C:\\Program Files\\WindowsPowerShell\\Modules\\Inv\\Inv.psm1', \"function Get-Inv { 'inv ' + (helper) + `$script:count }`r`nfunction helper { 'h' }`r`n`$script:count = 5`r`n`$Shared = 'yes'`r`nExport-ModuleMember -Function Get-Inv -Variable Shared\")");
            Eq("Import-Module -Verbose", t.T("Import-Module Inv -Verbose"), "VERBOSE: Loading module from path 'C:\\Program Files\\WindowsPowerShell\\Modules\\Inv\\Inv.psm1'.\nVERBOSE: Importing function 'Get-Inv'.\nVERBOSE: Importing variable 'Shared'.");
            Eq("exported function sees module scope", t.T("Get-Inv"), "inv h5");
            Has("private function hidden", t.Run("helper"), "The term 'helper' is not recognized");
            Eq("module script variables are private", t.T("$null -eq $count"), "True");
            Eq("exported variable visible", t.T("$Shared"), "yes");
            Eq("Get-Command -Module", t.T("Get-Command -Module Inv | ForEach-Object Name"), "Get-Inv");
            Eq("Get-Module of a script module", t.Run("Get-Module Inv"), "\nModuleType Version    Name                                ExportedCommands\n---------- -------    ----                                ----------------\nScript     0.0        Inv                                 {Get-Inv}\n\n\n");
            Eq("Import-Module again without -Force does nothing", t.T("Import-Module Inv -Verbose"), "");
            t.Run("Remove-Module Inv");
            Eq("Remove-Module unloads it", t.T("@(Get-Module Inv).Count"), "0");
            Eq("Remove-Module removes exported variables", t.T("$null -eq $Shared"), "True");
            Has("Remove-Module of a module that is not loaded", t.Run("Remove-Module Inv"), "Remove-Module : No modules were removed. Verify that the specification of modules to remove is correct and those modules exist in the runspace.");
            Has("Remove-Module error id", t.Last, "Modules_NoModulesRemoved,Microsoft.PowerShell.Commands.RemoveModuleCommand");
            Eq("auto-loading imports the module on first use", t.T("Get-Inv; (Get-Module Inv).Name"), "inv h5\nInv");
            Has("Import-Module unknown", t.Run("Import-Module Nope"), "Import-Module : The specified module 'Nope' was not loaded because no valid module file was found in any module directory.");
            Has("Import-Module unknown: id", t.Last, "ResourceUnavailable: (Nope:String) [Import-Module], FileNotFoundException\n    + FullyQualifiedErrorId : Modules_ModuleNotFound,Microsoft.PowerShell.Commands.ImportModuleCommand");
            Has("Import-Module by missing path", t.Run("Import-Module C:\\LabFiles\\none.psm1"), "The specified module 'C:\\LabFiles\\none.psm1' was not loaded");
            t.Run("New-ModuleManifest -Path 'C:\\Program Files\\WindowsPowerShell\\Modules\\Inv\\Inv.psd1' -RootModule Inv.psm1 -ModuleVersion 2.1 -Author Ada -Description 'Inventory tools' -FunctionsToExport Get-Inv");
            var psd = t.M.FindFile("C:\\Program Files\\WindowsPowerShell\\Modules\\Inv\\Inv.psd1");
            Check("New-ModuleManifest wrote a UTF-16 .psd1", psd != null && psd.Encoding == "unicode");
            Has("manifest keys", psd == null ? "" : psd.Text, "RootModule = 'Inv.psm1'\r\n");
            Has("manifest version", psd == null ? "" : psd.Text, "ModuleVersion = '2.1'");
            Has("manifest functions", psd == null ? "" : psd.Text, "FunctionsToExport = 'Get-Inv'");
            Eq("Test-ModuleManifest reads it back", t.T("$m = Test-ModuleManifest 'C:\\Program Files\\WindowsPowerShell\\Modules\\Inv\\Inv.psd1'; $m.Version.ToString() + ' ' + $m.Author + ' ' + $m.Description"), "2.1 Ada Inventory tools");
            Eq("Import-Module -Force -PassThru with the manifest", t.T("(Import-Module Inv -Force -PassThru).Version.ToString()"), "2.1");
            Eq("manifest module type is Script", t.T("(Get-Module Inv).ModuleType"), "Script");
            Eq("-Prefix renames nouns", t.T("Import-Module Inv -Force -Prefix Lab; (Get-Command -Module Inv).Name"), "Get-LabInv");
            t.Run("[System.IO.File]::WriteAllText('C:\\LabFiles\\bad.psd1', \"@{ RootModule = 'missing.psm1'; ModuleVersion = '1.0' }\")");
            Has("Test-ModuleManifest: missing root module", t.Run("Test-ModuleManifest C:\\LabFiles\\bad.psd1"), "The module to process 'missing.psm1', listed in field 'ModuleToProcess/RootModule' of module manifest 'C:\\LabFiles\\bad.psd1' was not processed because no valid module was found in any module directory.");
            t.Run("[System.IO.File]::WriteAllText('C:\\LabFiles\\req.psd1', \"@{ ModuleVersion = '1.0'; RequiredModules = @('NotThere') }\")");
            Has("RequiredModules missing", t.Run("Import-Module C:\\LabFiles\\req.psd1"), "The required module 'NotThere' is not loaded. Load the module or remove the module from 'RequiredModules' in the file 'C:\\LabFiles\\req.psd1'.");
            Has("Export-ModuleMember outside a module", t.Run("Export-ModuleMember -Function x"), "Export-ModuleMember : The Export-ModuleMember cmdlet can only be called from inside a module.");
            Has("-ListAvailable groups by folder", t.Run("Get-Module -ListAvailable Inv, Pester, NetTCPIP"), "\n\n    Directory: C:\\Program Files\\WindowsPowerShell\\Modules\n\n\nModuleType Version    Name                                ExportedCommands\n---------- -------    ----                                ----------------\nScript     2.1        Inv                                 {Get-Inv}\nScript     3.4.0      Pester                              {AfterEach, Assert-MockCalled, BeforeEach, Context...}\n");
            Has("-ListAvailable second folder", t.Last, "    Directory: C:\\Windows\\system32\\WindowsPowerShell\\v1.0\\Modules\n\n\nModuleType Version    Name                                ExportedCommands\n---------- -------    ----                                ----------------\nManifest   1.0.0.0    NetTCPIP                            {");
            Eq("ServerManager only on servers", t.On("WIN-CL1", "@(Get-Module -ListAvailable ServerManager).Count").Replace("\r", "").Trim(), "0");
            Eq("Import-Module of a built-in module", t.T("Import-Module NetSecurity -PassThru | ForEach-Object Name"), "NetSecurity");
            Has("Get-PSRepository", t.Run("Get-PSRepository"), "Name                      InstallationPolicy   SourceLocation\n----                      ------------------   --------------\nPSGallery                 Untrusted            https://www.powershellgallery.com/api/v2");
            t.Run("Set-PSRepository -Name PSGallery -InstallationPolicy Trusted");
            Eq("Set-PSRepository", t.T("(Get-PSRepository PSGallery).InstallationPolicy"), "Trusted");
            Has("Find-Module: no internet", t.Run("Find-Module Az"), "WARNING: Unable to resolve package source 'https://www.powershellgallery.com/api/v2'.");
            Has("Install-Module error", t.Run("Install-Module PSWindowsUpdate"), "No match was found for the specified search criteria and module name 'PSWindowsUpdate'. Try Get-PSRepository to see all available registered module repositories.");
            Has("Install-Module comes from PackageManagement", t.Last, "PackageManagement\\Install-Package : ");
            t.Dispose();
        }

        static void PsSecurityTests()
        {
            var t = new P();
            Eq("Get-ExecutionPolicy on the server", t.T("Get-ExecutionPolicy"), "RemoteSigned");
            Eq("Get-ExecutionPolicy -List", t.Run("Get-ExecutionPolicy -List"), "\n        Scope ExecutionPolicy\n        ----- ---------------\nMachinePolicy       Undefined\n   UserPolicy       Undefined\n      Process       Undefined\n  CurrentUser       Undefined\n LocalMachine    RemoteSigned\n\n\n");
            Eq("WIN-CL1 is Restricted", t.On("WIN-CL1", "Get-ExecutionPolicy").Replace("\r", "").Trim(), "Restricted");
            t.On("WIN-CL1", "'hello from a script' > C:\\LabFiles\\hi.ps1; [System.IO.File]::WriteAllText('C:\\LabFiles\\hi.ps1', \"'hello from a script'\")");
            var cl = t.On("WIN-CL1", "C:\\LabFiles\\hi.ps1");
            Has("Restricted: scripts do not run", cl, "C:\\LabFiles\\hi.ps1 : File C:\\LabFiles\\hi.ps1 cannot be loaded because running scripts is disabled on this system. For more information, see about_Execution_Policies at https:/go.microsoft.com/fwlink/?LinkID=135170.");
            Has("Restricted: category", cl, "    + CategoryInfo          : SecurityError: (:) [], PSSecurityException\n    + FullyQualifiedErrorId : UnauthorizedAccess");
            Eq("Set-ExecutionPolicy -Scope Process", t.On("WIN-CL1", "Set-ExecutionPolicy Bypass -Scope Process -Force; C:\\LabFiles\\hi.ps1").Replace("\r", "").Trim(), "hello from a script");
            Eq("the Process scope wins", t.On("WIN-CL1", "Get-ExecutionPolicy -Scope Process").Replace("\r", "").Trim(), "Bypass");
            Has("override warning", t.On("WIN-CL1", "Set-ExecutionPolicy RemoteSigned -Force"), "Windows PowerShell updated your execution policy successfully, but the setting is overridden by a policy defined at a more specific scope.  Due to the override, your shell will retain its current effective execution policy of Bypass.");
            Has("override id", t.Last, "ExecutionPolicyOverride,Microsoft.PowerShell.Commands.SetExecutionPolicyCommand");
            Eq("LocalMachine was still changed", t.M.Net.Get("WIN-CL1").ExecPolicyMachine, "RemoteSigned");
            Has("bad policy name", t.Run("Set-ExecutionPolicy Lax"), "Cannot convert value \"Lax\" to type \"Microsoft.PowerShell.ExecutionPolicy\"");
            Has("the confirmation prompt", t.Run("Set-ExecutionPolicy AllSigned"), "Execution Policy Change\nThe execution policy helps protect you from scripts that you do not trust.");
            t.Run("N");
            Eq("answering N keeps the policy", t.T("Get-ExecutionPolicy"), "RemoteSigned");
            const string dl = "C:\\Users\\Administrator\\Downloads\\Get-Inventory.ps1";
            Has("RemoteSigned blocks a downloaded script", t.Run("& " + dl), "File " + dl + " cannot be loaded. The file " + dl + " is not digitally signed. You cannot run this script on the current system.");
            Eq("its Zone.Identifier says Internet", t.T("[System.IO.File]::Exists('" + dl + "')"), "True");
            t.Run("[System.IO.File]::WriteAllText('C:\\LabFiles\\local.ps1', \"'local'\")");
            Eq("RemoteSigned runs local scripts", t.T("& C:\\LabFiles\\local.ps1"), "local");
            t.Run("Unblock-File -Path " + dl);
            Check("Unblock-File removes Zone.Identifier", !t.M.FindFile(dl).Streams.ContainsKey("Zone.Identifier"));
            Lacks("after Unblock-File it runs", t.Run("& " + dl), "cannot be loaded");
            Has("Unblock-File missing file", t.Run("Unblock-File C:\\nope.ps1"), "Unblock-File : Cannot find path 'C:\\nope.ps1' because it does not exist.");
            Eq("New-SelfSignedCertificate", t.T("$c = New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=Lab Code Signing' -CertStoreLocation Cert:\\CurrentUser\\My; $c.Subject + ' ' + $c.Thumbprint.Length + ' ' + $c.HasPrivateKey"), "CN=Lab Code Signing 40 True");
            Has("certificate table", t.Run("$c"), "   PSParentPath: Microsoft.PowerShell.Security\\Certificate::CurrentUser\\My");
            Has("certificate table row", t.Last, "Thumbprint                                Subject\n----------                                -------\n");
            Eq("code signing usage", t.T("$c.EnhancedKeyUsageList"), "Code Signing (1.3.6.1.5.5.7.3.3)");
            Has("a web certificate cannot sign code", t.Run("$w = New-SelfSignedCertificate -DnsName web01.lab.example.com -CertStoreLocation Cert:\\LocalMachine\\My; Set-AuthenticodeSignature -FilePath C:\\LabFiles\\local.ps1 -Certificate $w"), "Set-AuthenticodeSignature : Cannot sign code. The specified certificate is not suitable for code signing.");
            t.Run("[System.IO.File]::WriteAllText('C:\\LabFiles\\signed.ps1', \"'signed script ran'`r`n\")");
            Has("Set-AuthenticodeSignature: not trusted yet", t.Run("Set-AuthenticodeSignature -FilePath C:\\LabFiles\\signed.ps1 -Certificate $c"), "UnknownError                           signed.ps1");
            Has("signature table groups by folder", t.Last, "    Directory: C:\\LabFiles\n\n\nSignerCertificate                         Status                                 Path\n-----------------                         ------                                 ----\n");
            Has("signature block appended", t.M.FindFile("C:\\LabFiles\\signed.ps1").Text, "\r\n# SIG # Begin signature block\r\n# ");
            Has("untrusted root message", t.Run("(Get-AuthenticodeSignature C:\\LabFiles\\signed.ps1).StatusMessage"), "A certificate chain processed, but terminated in a root certificate which is not trusted by the trust provider.");
            t.Run("Export-Certificate -Cert $c -FilePath C:\\LabFiles\\cs.cer | Out-Null; Import-Certificate -FilePath C:\\LabFiles\\cs.cer -CertStoreLocation Cert:\\LocalMachine\\Root | Out-Null");
            Eq("trusted root: Valid", t.T("(Get-AuthenticodeSignature C:\\LabFiles\\signed.ps1).Status"), "Valid");
            Eq("unsigned file: NotSigned", t.T("(Get-AuthenticodeSignature C:\\LabFiles\\local.ps1).Status"), "NotSigned");
            Eq("Windows programs are signed", t.T("(Get-AuthenticodeSignature C:\\Windows\\System32\\PING.EXE).Status"), "Valid");
            t.Run("Set-ExecutionPolicy AllSigned -Force");
            Has("AllSigned: unknown publisher prompt", t.Run("C:\\LabFiles\\signed.ps1"), "Do you want to run software from this untrusted publisher?\nFile C:\\LabFiles\\signed.ps1 is published by CN=Lab Code Signing and is not trusted on your system.");
            Eq("Always run trusts the publisher", t.Run("A").Replace("\r", "").Trim(), "signed script ran");
            Eq("then it runs without asking", t.T("C:\\LabFiles\\signed.ps1"), "signed script ran");
            Has("AllSigned blocks unsigned local scripts", t.Run("C:\\LabFiles\\local.ps1"), "is not digitally signed");
            t.Run("[System.IO.File]::WriteAllText('C:\\LabFiles\\signed.ps1', [System.IO.File]::ReadAllText('C:\\LabFiles\\signed.ps1').Replace('ran', 'was changed'))");
            Eq("an edit after signing: HashMismatch", t.T("(Get-AuthenticodeSignature C:\\LabFiles\\signed.ps1).Status"), "HashMismatch");
            Has("HashMismatch blocks the script", t.Run("C:\\LabFiles\\signed.ps1"), "The contents of file C:\\LabFiles\\signed.ps1 might have been changed by an unauthorized user or process, because the hash of the file does not match the hash stored in the digital signature.");
            t.Run("Set-AuthenticodeSignature C:\\LabFiles\\signed.ps1 $c | Out-Null");
            Eq("signing again fixes it", t.T("C:\\LabFiles\\signed.ps1"), "signed script was changed");
            Eq("text after the block breaks it", t.T("[System.IO.File]::AppendAllText('C:\\LabFiles\\signed.ps1', \"'more'`r`n\"); (Get-AuthenticodeSignature C:\\LabFiles\\signed.ps1).Status"), "HashMismatch");
            t.Run("Set-ExecutionPolicy RemoteSigned -Force");
            Has("ConvertTo-SecureString without -Force", t.Run("ConvertTo-SecureString 'P@ss' -AsPlainText"), "ConvertTo-SecureString : The system cannot protect plain text input. To suppress this warning and convert the plain text to a SecureString, reissue the command specifying the Force parameter. For more information ,type: get-help ConvertTo-SecureString.");
            Has("its id", t.Last, "ImportSecureString_InvalidArgument,Microsoft.PowerShell.Commands.ConvertToSecureStringCommand");
            Eq("a SecureString shows its type", t.T("ConvertTo-SecureString 'P@ss' -AsPlainText -Force"), "System.Security.SecureString");
            Eq("SecureString length", t.T("(ConvertTo-SecureString 'P@ss' -AsPlainText -Force).Length"), "4");
            Eq("ConvertFrom-SecureString starts like a DPAPI blob", t.T("$e = ConvertTo-SecureString 'P@ss' -AsPlainText -Force | ConvertFrom-SecureString; $e.StartsWith('01000000d08c9ddf0115d1118c7a00c04fc297eb')"), "True");
            Eq("round trip on the same machine and user", t.T("$back = ConvertTo-SecureString $e; (New-Object System.Management.Automation.PSCredential('u', $back)).GetNetworkCredential().Password"), "P@ss");
            string blob = t.T("$e");
            Has("another machine cannot read it", t.On("WIN-CL1", "ConvertTo-SecureString '" + blob + "'"), "ConvertTo-SecureString : Key not valid for use in specified state.");
            Eq("with -Key it works anywhere", t.On("WIN-CL1", "$k = 1..16; $x = ConvertTo-SecureString 'Key1' -AsPlainText -Force | ConvertFrom-SecureString -Key $k; (New-Object System.Management.Automation.PSCredential('u', (ConvertTo-SecureString $x -Key $k))).GetNetworkCredential().Password").Replace("\r", "").Trim(), "Key1");
            Has("wrong key", t.On("WIN-CL1", "ConvertTo-SecureString $x -Key (2..17)"), "Padding is invalid and cannot be removed.");
            Has("bad key length", t.On("WIN-CL1", "ConvertFrom-SecureString (ConvertTo-SecureString a -AsPlainText -Force) -Key (1..5)"), "Valid key length settings are either 128 bits, 192 bits, or 256 bits.");
            Has("not a protected string", t.Run("ConvertTo-SecureString 'hello'"), "Input string was not in a correct format.");
            t.Run("$cr = Get-Credential -UserName LAB\\alice -Message 'Account for the job'");
            t.Run("Pa55word");
            Eq("Get-Credential asks for the password", t.T("$cr.UserName + ' ' + $cr.GetNetworkCredential().Password"), "LAB\\alice Pa55word");
            Check("Get-Credential in a check fails cleanly", !t.Ok("$x = Get-Credential; $true"));
            // JEA
            t.Run("[System.IO.Directory]::CreateDirectory('C:\\Program Files\\WindowsPowerShell\\Modules\\LabJEA\\RoleCapabilities') | Out-Null");
            t.Run("New-PSRoleCapabilityFile -Path 'C:\\Program Files\\WindowsPowerShell\\Modules\\LabJEA\\RoleCapabilities\\Helpdesk.psrc' -VisibleCmdlets 'Get-Service', @{ Name = 'Restart-Service'; Parameters = @{ Name = 'Name'; ValidateSet = 'Spooler' } } -VisibleExternalCommands 'C:\\Windows\\System32\\whoami.exe'");
            var psrc = t.M.FindFile("C:\\Program Files\\WindowsPowerShell\\Modules\\LabJEA\\RoleCapabilities\\Helpdesk.psrc");
            Has("role capability file", psrc == null ? "" : psrc.Text, "VisibleCmdlets = 'Get-Service', @{");
            Has("unset keys are comments", psrc == null ? "" : psrc.Text, "# VisibleFunctions = ");
            t.Run("New-PSSessionConfigurationFile -Path C:\\LabFiles\\Helpdesk.pssc -SessionType RestrictedRemoteServer -RunAsVirtualAccount -TranscriptDirectory C:\\Transcripts -RoleDefinitions @{ 'LAB\\Helpdesk' = @{ RoleCapabilities = 'Helpdesk' }; 'LAB\\bob' = @{ VisibleCmdlets = 'Get-Process' } }");
            var pssc = t.M.FindFile("C:\\LabFiles\\Helpdesk.pssc");
            Has("session configuration file", pssc == null ? "" : pssc.Text, "SessionType = 'RestrictedRemoteServer'");
            Has("session configuration: virtual account", pssc == null ? "" : pssc.Text, "RunAsVirtualAccount = $true");
            Eq("Test-PSSessionConfigurationFile", t.T("Test-PSSessionConfigurationFile C:\\LabFiles\\Helpdesk.pssc"), "True");
            Has("Register-PSSessionConfiguration", t.Run("Register-PSSessionConfiguration -Name Helpdesk -Path C:\\LabFiles\\Helpdesk.pssc -Force"), "Container       {Name=Helpdesk}                     Helpdesk");
            Has("Register: WSMan group header", t.Last, "   WSManConfig: Microsoft.WSMan.Management\\WSMan::localhost\\Plugin");
            Has("Get-PSSessionConfiguration list", t.Run("Get-PSSessionConfiguration -Name Helpdesk"), "Name          : Helpdesk\nPSVersion     : 5.1\nStartupScript :\nRunAsUser     :\nPermission    : LAB\\Helpdesk AccessAllowed, LAB\\bob AccessAllowed");
            Eq("built-in endpoints", t.T("(Get-PSSessionConfiguration).Name -join ','"), "microsoft.powershell,microsoft.powershell.workflow,microsoft.powershell32,microsoft.windows.servermanagerworkflows,Helpdesk");
            Eq("EndpointCommands for a direct user", string.Join(",", t.M.EndpointCommands("Helpdesk", "LAB\\bob").ToArray()), "Clear-Host,Exit-PSSession,Get-Command,Get-FormatData,Get-Help,Measure-Object,Out-Default,Select-Object,Get-Process");
            PsMachine.IsMemberHook = (m, u, g) => g.Equals("LAB\\Helpdesk", StringComparison.OrdinalIgnoreCase) && u.EndsWith("alice", StringComparison.OrdinalIgnoreCase);
            var rules = t.M.EndpointCommandRules("Helpdesk", "LAB\\alice");
            Check("EndpointCommands through a role capability file", rules != null && rules.Any(r => r.Name == "Get-Service") && rules.Any(r => r.Name.EndsWith("whoami.exe")));
            var rs = rules == null ? null : rules.FirstOrDefault(r => r.Name == "Restart-Service");
            Check("parameter limits of a role", rs != null && rs.Parameters != null && rs.Parameters.ContainsKey("Name") && rs.Parameters["Name"].SequenceEqual(new[] { "Spooler" }));
            Has("Get-PSSessionCapability", t.Run("Get-PSSessionCapability -ConfigurationName Helpdesk -Username LAB\\alice"), "Restart-Service");
            PsMachine.IsMemberHook = null;
            Check("no role: refused", t.M.EndpointCommands("Helpdesk", "LAB\\mallory") == null);
            t.Run("Unregister-PSSessionConfiguration -Name Helpdesk -Force");
            Has("Unregister-PSSessionConfiguration", t.Run("Get-PSSessionConfiguration -Name Helpdesk"), "No session configuration matches criteria \"Helpdesk\".");
            Has("WinRM stopped on WIN-CL1", t.On("WIN-CL1", "Get-PSSessionConfiguration"), "The client cannot connect to the destination specified in the request.");
            t.Dispose();
        }

        static void PsDscTests()
        {
            var t = new P();
            t.Run("Configuration WebConfig {\n param([string]$Site = 'site')\n Import-DscResource -ModuleName PSDesiredStateConfiguration\n Node localhost {\n  File Folder { DestinationPath = \"C:\\inetpub\\$Site\"; Type = 'Directory'; Ensure = 'Present' }\n  File Index { DestinationPath = \"C:\\inetpub\\$Site\\index.html\"; Contents = '<h1>Hi</h1>'; DependsOn = '[File]Folder' }\n  Registry Owner { Key = 'HKEY_LOCAL_MACHINE\\SOFTWARE\\Lab'; ValueName = 'Owner'; ValueData = 'IT'; ValueType = 'String' }\n  Environment Stage { Name = 'APP_STAGE'; Value = 'Test' }\n  Log Done { Message = 'Web site configured'; DependsOn = '[File]Index' }\n }\n}");
            Eq("a configuration is a command", t.T("(Get-Command WebConfig).CommandType"), "Configuration");
            {
                var w = new P();
                Eq("WinRM on WIN-CL1 is stopped and manual at first", w.On("WIN-CL1", "$s = Get-Service WinRM; \"$($s.Status) $($s.StartType)\"").Replace("\r", "").Trim(), "Stopped Manual");
                w.On("WIN-CL1", "Enable-PSRemoting -Force | Out-Null");
                Eq("Enable-PSRemoting starts WinRM and sets it to Automatic", w.On("WIN-CL1", "$s = Get-Service WinRM; \"$($s.Status) $($s.StartType)\"").Replace("\r", "").Trim(), "Running Automatic");
                Eq("remoting to WIN-CL1 works", w.T("Invoke-Command -ComputerName WIN-CL1 -ScriptBlock { hostname }"), "WIN-CL1");
                w.On("WIN-CL1", "Stop-Service WinRM");
                Has("with WinRM stopped the connection fails", w.Run("Invoke-Command -ComputerName WIN-CL1 -ScriptBlock { hostname }"), "WinRM cannot complete the operation");
                w.On("WIN-CL1", "Start-Service WinRM; Set-NetFirewallRule -Name WINRM-HTTP-In-TCP -Enabled False");
                Has("with the WinRM firewall rule off the connection fails", w.Run("Invoke-Command -ComputerName WIN-CL1 -ScriptBlock { hostname }"), "WinRM cannot complete the operation");
                Eq("CIM-based commands are functions, Resolve-DnsName a cmdlet", w.T("(Get-Command Get-NetIPAddress).CommandType; (Get-Command Get-ScheduledTask).CommandType; (Get-Command Resolve-DnsName).CommandType"), "Function\nFunction\nCmdlet");
                Eq("a firewall address prefix is kept as a mask", w.T("New-NetFirewallRule -DisplayName T1 -Direction Inbound -Protocol TCP -LocalPort 8080 -RemoteAddress 10.0.10.0/24 | Out-Null; (Get-NetFirewallRule -DisplayName T1 | Get-NetFirewallAddressFilter).RemoteAddress"), "10.0.10.0/255.255.255.0");
                Eq("a flags enum takes a list", w.T("(Get-Command -CommandType Cmdlet, Function).Count -eq ((Get-Command -CommandType Cmdlet).Count + (Get-Command -CommandType Function).Count)"), "True");
                Eq("programs are listed with -CommandType Application", w.T("(Get-Command -CommandType Application | Where-Object Name -eq 'ipconfig.exe').Source"), "C:\\Windows\\system32\\ipconfig.exe");
                w.Dispose();
            }
            {
                var a = new P();
                a.Run("Stop-Service Spooler; Configuration Adm { Import-DscResource -ModuleName PSDesiredStateConfiguration; Node 'localhost' { Service Spool { Name = 'Spooler'; State = 'Running'; StartupType = 'Automatic' }; User Kim { UserName = 'kim'; FullName = 'Kim Lee' }; Group Helpers { GroupName = 'Helpers'; MembersToInclude = 'kim'; DependsOn = '[User]Kim' }; WindowsFeature Web { Name = 'Web-Server' } } }; Adm -OutputPath C:\\DSC\\Adm | Out-Null");
                a.Run("Start-DscConfiguration -Path C:\\DSC\\Adm -Wait");
                Eq("DSC Service, User, Group, WindowsFeature resources apply", a.T("(Get-Service Spooler).Status; (Get-LocalUser kim).FullName; (Get-LocalGroupMember Helpers).Name; (Get-WindowsFeature Web-Server).InstallState; Test-DscConfiguration"), "Running\nKim Lee\nWIN-SRV1\\kim\nInstalled\nTrue");
                Eq("DSC Service drift is detected", a.T("Stop-Service Spooler; Test-DscConfiguration"), "False");
                Eq("Get-DscResource lists nine resources", a.T("(Get-DscResource).Count"), "9");
                a.Dispose();
            }
            Eq("compiling writes the MOF", t.Run("WebConfig -OutputPath C:\\DSC\\Web"), "\n\n    Directory: C:\\DSC\\Web\n\n\nMode                LastWriteTime         Length Name\n----                -------------         ------ ----\n-a----         3/2/2026   9:00 AM           " + t.M.FindFile("C:\\DSC\\Web\\localhost.mof").Length.ToString().PadLeft(4) + " localhost.mof\n\n\n");
            var mof = t.M.FindFile("C:\\DSC\\Web\\localhost.mof").Text;
            Has("MOF: instance block", mof, "instance of MSFT_FileDirectoryConfiguration as $MSFT_FileDirectoryConfiguration1ref\r\n{\r\nResourceID = \"[File]Folder\";\r\n DestinationPath = \"C:\\\\inetpub\\\\site\";");
            Has("MOF: DependsOn", mof, " DependsOn = {\r\n    \"[File]Folder\"};");
            Has("MOF: configuration name", mof, " ConfigurationName = \"WebConfig\";");
            Has("MOF: document", mof, "instance of OMI_ConfigurationDocument");
            Has("MOF: target node", mof, "@TargetNode='localhost'");
            Eq("default output folder is .\\Name", t.T("WebConfig -Site other | ForEach-Object FullName"), "C:\\Windows\\System32\\WebConfig\\localhost.mof");
            Has("Test-DscConfiguration before any apply", t.Run("Test-DscConfiguration"), "Test-DscConfiguration : Current configuration does not exist. Execute Start-DscConfiguration command with -Path parameter to specify a configuration file and create a current configuration first.");
            var v = t.Run("Start-DscConfiguration -Path C:\\DSC\\Web -Wait -Verbose");
            Has("LCM verbose: start", v, "VERBOSE: [WIN-SRV1]: LCM:  [ Start  Set      ]\n");
            Has("LCM verbose: resource test", v, "VERBOSE: [WIN-SRV1]: LCM:  [ Start  Test     ]  [[File]Folder]\nVERBOSE: [WIN-SRV1]:                            [[File]Folder] The system cannot find the file specified.");
            Has("LCM verbose: set", v, "VERBOSE: [WIN-SRV1]: LCM:  [ Start  Set      ]  [[File]Index]");
            Check("DependsOn order", v.IndexOf("[[File]Folder]") < v.IndexOf("[[File]Index]") && v.IndexOf("Start  Resource ]  [[File]Index]") < v.IndexOf("Start  Resource ]  [[Log]Done]"));
            Has("Log resource writes its message", v, "[[Log]Done] Web site configured");
            Eq("the file exists", t.T("[System.IO.File]::ReadAllText('C:\\inetpub\\site\\index.html')"), "<h1>Hi</h1>");
            Check("the registry value exists", t.M.HKLM.Find("SOFTWARE\\Lab") != null && PsConv.ToStr(t.M.HKLM.Find("SOFTWARE\\Lab").Value("Owner").Data) == "IT");
            Check("the machine environment variable exists", t.M.EnvMachine.ContainsKey("APP_STAGE"));
            Eq("Test-DscConfiguration False because of Log", t.T("Test-DscConfiguration"), "False");
            t.Run("Configuration Site2 { Node localhost { File Folder { DestinationPath = 'C:\\inetpub\\site'; Type = 'Directory' }; File Index { DestinationPath = 'C:\\inetpub\\site\\index.html'; Contents = '<h1>Hi</h1>'; DependsOn = '[File]Folder' }; Environment Stage { Name = 'APP_STAGE'; Value = 'Test' } } }");
            t.Run("Site2 -OutputPath C:\\DSC\\Site2 | Out-Null; Start-DscConfiguration -Path C:\\DSC\\Site2 -Wait");
            Eq("in the desired state", t.T("Test-DscConfiguration"), "True");
            Has("second apply skips Set", t.Run("Start-DscConfiguration -Path C:\\DSC\\Site2 -Wait -Verbose"), "LCM:  [ Skip   Set      ]  [[File]Index]");
            t.Run("[System.IO.File]::WriteAllText('C:\\inetpub\\site\\index.html', 'changed by hand')");
            Eq("drift detected", t.T("Test-DscConfiguration"), "False");
            Has("-Detailed names the resource", t.Run("Test-DscConfiguration -Detailed"), "{[File]Index}");
            Eq("-Detailed InDesiredState", t.T("(Test-DscConfiguration -Detailed).InDesiredState"), "False");
            t.Run("Start-DscConfiguration -UseExisting -Wait");
            Eq("-UseExisting corrects the drift", t.T("[System.IO.File]::ReadAllText('C:\\inetpub\\site\\index.html')"), "<h1>Hi</h1>");
            Has("Start-DscConfiguration without -Wait returns a job", t.Run("Start-DscConfiguration -Path C:\\DSC\\Site2"), "Configuratio... Completed     True          WIN-SRV1");
            Has("Get-DscConfiguration", t.Run("Get-DscConfiguration"), "ResourceId           : [File]Index");
            Eq("Get-DscConfiguration values", t.T("(Get-DscConfiguration | Where-Object ResourceId -eq '[Environment]Stage').Value"), "Test");
            Has("Get-DscConfigurationStatus", t.Run("Get-DscConfigurationStatus"), "Success    3/2/2026");
            Has("Get-DscResource table", t.Run("Get-DscResource"), "ImplementedAs   Name                      ModuleName                     Version    Properties\n-------------   ----                      ----------                     -------    ----------\n");
            Has("Get-DscResource File is Binary", t.Last, "Binary          File                                                                {DestinationPath");
            Has("Get-DscResource -Syntax", t.Run("Get-DscResource Registry -Syntax"), "Registry [String] #ResourceName\n{\n    Key = [string]\n    ValueName = [string]\n");
            Has("Get-DscLocalConfigurationManager", t.Run("Get-DscLocalConfigurationManager"), "ConfigurationMode              : ApplyAndMonitor\nConfigurationModeFrequencyMins : 15");
            Eq("LCM refresh mode", t.T("(Get-DscLocalConfigurationManager).RefreshMode"), "PUSH");
            t.Run("Configuration Bad { Node localhost { File A { DestinationPath = 'C:\\x'; Colour = 'Red' } } }");
            Has("unknown property", t.Run("Bad"), "The member 'Colour' is not valid. Valid members are");
            t.Run("Configuration Bad2 { Node localhost { WebSite A { Name = 'x' } } }");
            Has("unknown resource", t.Run("Bad2"), "Undefined DSC resource 'WebSite'. Use Import-DSCResource to import the resource.");
            t.Run("Configuration Bad3 { Node localhost { Log A { Message = 'a' }; Log A { Message = 'b' } } }");
            Has("duplicate resource", t.Run("Bad3"), "A duplicate resource identifier '[Log]A' was found while processing the specification for node 'localhost'.");
            t.Run("Configuration Bad4 { Node localhost { Log A { Message = 'a'; DependsOn = '[Log]Missing' } } }");
            Has("DependsOn to nothing", t.Run("Bad4"), "depends on [Log]Missing, which is not defined");
            t.Run("Configuration Bad5 { Import-DscResource -ModuleName xWebAdministration; Node localhost { } }");
            Has("Import-DscResource of a missing module", t.Run("Bad5"), "Could not find the module 'xWebAdministration'.");
            Has("Import-DscResource outside a configuration", t.Run("Import-DscResource -ModuleName PSDesiredStateConfiguration"), "can only be used inside a Configuration");
            t.Run("Configuration ScriptCfg { Node localhost { Script Flag { GetScript = { @{ Result = 'x' } }; TestScript = { [System.IO.File]::Exists('C:\\LabFiles\\flag.txt') }; SetScript = { [System.IO.File]::WriteAllText('C:\\LabFiles\\flag.txt', 'set') } } } }");
            t.Run("ScriptCfg -OutputPath C:\\DSC\\Script | Out-Null");
            var sv = t.Run("Start-DscConfiguration -Path C:\\DSC\\Script -Wait -Verbose");
            Check("Script resource ran SetScript", t.M.FindFile("C:\\LabFiles\\flag.txt") != null, sv);
            // configuration data
            t.Run("$cd = @{ AllNodes = @( @{ NodeName = '*'; Root = 'C:\\Sites' }, @{ NodeName = 'WIN-SRV1'; Role = 'Web' }, @{ NodeName = 'WIN-CL1'; Role = 'Client' } ) }");
            t.Run("Configuration ByData { Node $AllNodes.NodeName { File Root { DestinationPath = $Node.Root + '\\' + $Node.Role; Type = 'Directory' } } }");
            Eq("ConfigurationData: one MOF per node", t.T("ByData -ConfigurationData $cd -OutputPath C:\\DSC\\Data | ForEach-Object Name"), "WIN-SRV1.mof\nWIN-CL1.mof");
            Has("NodeName * values are merged", t.M.FindFile("C:\\DSC\\Data\\WIN-CL1.mof").Text, "DestinationPath = \"C:\\\\Sites\\\\Client\";");
            Has("ConfigurationData needs AllNodes", t.Run("ByData -ConfigurationData @{ Nodes = 1 }"), "ConfigurationData parameter need to have property AllNodes.");
            Has("WinRM stopped on the client", t.Run("Start-DscConfiguration -Path C:\\DSC\\Data -Wait"), "The client cannot connect to the destination specified in the request.");
            Eq("the server's own MOF still applied", t.T("[System.IO.Directory]::Exists('C:\\Sites\\Web')"), "True");
            t.Run("Remove-DscConfigurationDocument -Stage Current");
            Has("Remove-DscConfigurationDocument", t.Run("Test-DscConfiguration"), "Current configuration does not exist.");
            t.Dispose();
        }
    }
}
