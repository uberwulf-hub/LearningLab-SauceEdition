// Cert Lab - checks of the simulated PowerShell's providers against Windows PowerShell 5.1: file listings and their
// exact table text, wildcards and -LiteralPath, recursion, the registry (keys, values, the "Hive:" listing), Env:,
// Variable:, Alias:, Function:, Cert:, locations and drives, Test-Path and the path cmdlets, content and encodings,
// item changes with their errors and prompts, pipeline binding of items, Select-String, Get-FileHash and Get-Acl.
using System;
using System.Collections.Generic;
using System.Linq;

namespace CertLab
{
    public static partial class Tests
    {
        static partial void PsGroupsProviders(List<KeyValuePair<string, Action>> l)
        {
            l.Add(new KeyValuePair<string, Action>("ps-providers-fs", PsProvFsTests));
            l.Add(new KeyValuePair<string, Action>("ps-providers-reg", PsProvRegTests));
            l.Add(new KeyValuePair<string, Action>("ps-providers-content", PsProvContentTests));
            l.Add(new KeyValuePair<string, Action>("ps-providers-misc", PsProvMiscTests));
        }
        static bool Has(string s, string part) { return s.Contains(part); }

        static void PsProvFsTests()
        {
            var t = new P();
            Eq("Get-Location at start", t.T("Get-Location"), "Path\n----\nC:\\Windows\\system32");
            t.Run("cd C:\\LabFiles");
            Eq("prompt follows the location", t.C.Prompt, "PS C:\\LabFiles> ");
            Eq("dir: the 5.1 table", t.Run("dir"),
                "\n\n    Directory: C:\\LabFiles\n\n\n" +
                "Mode                LastWriteTime         Length Name\n" +
                "----                -------------         ------ ----\n" +
                "d-----        1/13/2026  10:14 AM                Archive\n" +
                "d-----        1/13/2026  10:14 AM                Reports\n" +
                "-a----        1/13/2026  10:14 AM            593 app.log\n" +
                "-a----        1/13/2026  10:14 AM            720 employees.csv\n" +
                "-a----        1/13/2026  10:14 AM            122 notes.txt\n" +
                "-a----        1/13/2026  10:14 AM             32 servers.txt\n" +
                "-a----        1/13/2026  10:14 AM            206 settings.json\n" +
                "-a----        1/13/2026  10:14 AM            126 shares.csv\n\n\n");
            Eq("wildcard in the path", t.T("(dir *.csv).Name -join ','"), "employees.csv,shares.csv");
            Eq("-Recurse groups by folder", Has(t.Run("dir -Recurse"), "    Directory: C:\\LabFiles\\Reports\n\n\nMode") ? "ok" : t.Last, "ok");
            Eq("-Recurse -Name gives relative paths", t.T("dir -Recurse -Name | Select-Object -Last 2"), "Reports\\2026-02.csv\nReports\\summary [old].txt");
            Eq("*.csv -Recurse searches the tree", t.T("(dir *.csv -Recurse).Name -join ','"), "employees.csv,shares.csv,2026-01.csv,2026-02.csv");
            Eq("-Include without -Recurse finds nothing (5.1)", t.T("(Get-ChildItem C:\\LabFiles -Include *.csv | Measure-Object).Count"), "0");
            Eq("-Include with a wildcard path", t.T("(Get-ChildItem C:\\LabFiles\\* -Include *.csv).Name -join ','"), "employees.csv,shares.csv");
            Eq("-Exclude", t.T("(Get-ChildItem C:\\LabFiles -Exclude *.csv,*.log,*.json).Name -join ','"), "Archive,Reports,notes.txt,servers.txt");
            Eq("-Filter -Recurse", t.T("(Get-ChildItem C:\\LabFiles -Filter *.log -Recurse).FullName -join ','"), "C:\\LabFiles\\app.log,C:\\LabFiles\\Archive\\old-app.log");
            Eq("-Directory", t.T("(Get-ChildItem -Directory).Name -join ','"), "Archive,Reports");
            Eq("-File", t.T("(Get-ChildItem -File).Count"), "6");
            Eq("-Depth 0 stays in the folder", t.T("(Get-ChildItem -Depth 0).Count"), "8");
            Eq("hidden items need -Force", t.T("(dir C:\\Users\\Administrator).Name -contains 'AppData'"), "False");
            Eq("-Force shows them", t.T("(dir C:\\Users\\Administrator -Force).Name -contains 'AppData'"), "True");
            Eq("-Hidden shows only them", t.T("(dir C:\\Users\\Administrator -Hidden).Name -join ','"), "AppData,NTUSER.DAT");
            Eq("Mode of a hidden file", t.T("(Get-Item C:\\Users\\Administrator\\NTUSER.DAT -Force).Mode"), "-a-h--");
            Eq("brackets in -Path are a wildcard", t.T("(Get-ChildItem 'Reports\\summary [old].txt' | Measure-Object).Count"), "0");
            Eq("-LiteralPath takes them literally", t.T("(Get-Item -LiteralPath 'Reports\\summary [old].txt').Name"), "summary [old].txt");
            Eq("backticks escape brackets", t.T("(Get-Item 'Reports\\summary `[old`].txt').Length"), "61");
            Eq("missing path error", t.Run("Get-Item nope.txt"), "Get-Item : Cannot find path 'C:\\LabFiles\\nope.txt' because it does not exist.\nAt line:1 char:1\n+ Get-Item nope.txt\n+ ~~~~~~~~~~~~~~~~~\n    + CategoryInfo          : ObjectNotFound: (C:\\LabFiles\\nope.txt:String) [Get-Item], ItemNotFoundException\n    + FullyQualifiedErrorId : PathNotFound,Microsoft.PowerShell.Commands.GetItemCommand\n\n");
            Eq("missing drive error", Has(t.Run("dir Q:\\"), "Cannot find drive. A drive with the name 'Q' does not exist.") && Has(t.Last, "DriveNotFound,Microsoft.PowerShell.Commands.GetChildItemCommand") ? "ok" : t.Last, "ok");
            Eq("FileInfo type", t.T("(Get-Item app.log).GetType().FullName"), "System.IO.FileInfo");
            Eq("DirectoryInfo type", t.T("(Get-Item Reports).GetType().FullName"), "System.IO.DirectoryInfo");
            Eq("FileInfo properties", t.T("$f = Get-Item app.log; $f.BaseName, $f.Extension, $f.DirectoryName, $f.Directory.Name, $f.Length -join '|'"), "app|.log|C:\\LabFiles|LabFiles|593");
            Eq("DirectoryInfo Parent and Root", t.T("$d = Get-Item Reports; $d.Parent.FullName, $d.Root.FullName -join '|'"), "C:\\LabFiles|C:\\");
            Eq("PSPath and friends", t.T("$f = Get-Item app.log; $f.PSPath, $f.PSParentPath, $f.PSChildName, $f.PSDrive, $f.PSProvider, $f.PSIsContainer -join '|'"), "Microsoft.PowerShell.Core\\FileSystem::C:\\LabFiles\\app.log|Microsoft.PowerShell.Core\\FileSystem::C:\\LabFiles|app.log|C|Microsoft.PowerShell.Core\\FileSystem|False");
            Eq("ToString: full path from Get-Item", t.T("\"$(Get-Item app.log)\""), "C:\\LabFiles\\app.log");
            Eq("ToString: only the name from a listing (5.1)", t.T("dir | Select-Object -First 1 | ForEach-Object { \"$_\" }"), "Archive");
            Eq("GetFiles method", t.T("(Get-Item Reports).GetFiles('*.csv').Count"), "2");
            Eq("cd .. and cd\\", t.T("cd Reports; cd ..; (Get-Location).Path; cd\\; (Get-Location).Path; cd C:\\LabFiles"), "C:\\LabFiles\nC:\\");
            Eq("cd.. function", t.T("cd Reports; cd..; (Get-Location).Path"), "C:\\LabFiles");
            Eq("cd ~", t.T("cd ~; (Get-Location).Path; cd C:\\LabFiles"), "C:\\Users\\Administrator");
            Eq("Set-Location to a missing folder", Has(t.Run("cd C:\\nope"), "PathNotFound,Microsoft.PowerShell.Commands.SetLocationCommand") ? "ok" : t.Last, "ok");
            Eq("Push / Pop-Location", t.T("Push-Location HKLM:\\SOFTWARE; (Get-Location).Path; Pop-Location; (Get-Location).Path"), "HKLM:\\SOFTWARE\nC:\\LabFiles");
            Eq("New-Item directory output", t.Run("New-Item -ItemType Directory Test"), "\n\n    Directory: C:\\LabFiles\n\n\nMode                LastWriteTime         Length Name\n----                -------------         ------ ----\nd-----         3/2/2026   9:00 AM                Test\n\n\n");
            Eq("New-Item directory that exists", t.Run("New-Item -ItemType Directory Test"), "New-Item : An item with the specified name C:\\LabFiles\\Test already exists.\nAt line:1 char:1\n+ New-Item -ItemType Directory Test\n+ ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~\n    + CategoryInfo          : ResourceExists: (C:\\LabFiles\\Test:String) [New-Item], IOException\n    + FullyQualifiedErrorId : DirectoryExist,Microsoft.PowerShell.Commands.NewItemCommand\n\n");
            Eq("New-Item file with -Value", t.T("New-Item Test\\a.txt -Value 'hello' | Out-Null; Get-Content Test\\a.txt"), "hello");
            Eq("New-Item file that exists", Has(t.Run("New-Item Test\\a.txt"), "The file 'C:\\LabFiles\\Test\\a.txt' already exists.") ? "ok" : t.Last, "ok");
            Eq("New-Item -Force overwrites", t.T("New-Item Test\\a.txt -Value 'x' -Force | Out-Null; Get-Content Test\\a.txt"), "x");
            Eq("New-Item in a missing folder", Has(t.Run("New-Item C:\\nope\\b.txt"), "Could not find a part of the path 'C:\\nope\\b.txt'.") ? "ok" : t.Last, "ok");
            Eq("mkdir", t.T("mkdir Test2 | Out-Null; Test-Path Test2 -PathType Container"), "True");
            Eq("piped items bind to Remove-Item", t.T("New-Item Test\\b.log | Out-Null; Get-ChildItem Test\\*.log | Remove-Item; (dir Test).Name -join ','"), "a.txt");
            Eq("non-empty folder asks", Has(t.Run("Remove-Item Test"), "The item at C:\\LabFiles\\Test has children and the Recurse parameter was not specified. If you continue, all children will be removed with the item. Are you sure you want to continue?") ? "ok" : t.Last, "ok");
            t.Run("N");
            Eq("No keeps the folder", t.T("Test-Path Test"), "True");
            Eq("-Recurse removes it", t.T("Remove-Item Test -Recurse; Test-Path Test"), "False");
            Eq("Remove-Item -WhatIf", t.T("Remove-Item app.log -WhatIf"), "What if: Performing the operation \"Remove File\" on target \"C:\\LabFiles\\app.log\".");
            t.Run("Set-ItemProperty notes.txt -Name IsReadOnly -Value $true");
            Eq("read-only needs -Force", t.Run("Remove-Item notes.txt"), "Remove-Item : Cannot remove item C:\\LabFiles\\notes.txt: You do not have sufficient access rights to perform this operation.\nAt line:1 char:1\n+ Remove-Item notes.txt\n+ ~~~~~~~~~~~~~~~~~~~~~\n    + CategoryInfo          : PermissionDenied: (C:\\LabFiles\\notes.txt:FileInfo) [Remove-Item], IOException\n    + FullyQualifiedErrorId : RemoveFileSystemItemUnAuthorizedAccess,Microsoft.PowerShell.Commands.RemoveItemCommand\n\n");
            Eq("Mode shows read-only", t.T("(Get-Item notes.txt).Mode"), "-ar---");
            Eq("-Force removes it", t.T("Remove-Item notes.txt -Force; Test-Path notes.txt"), "False");
            Eq("Copy-Item -PassThru", t.T("(Copy-Item app.log app2.log -PassThru).Name"), "app2.log");
            Eq("Rename-Item", t.T("Rename-Item app2.log app3.log; Test-Path app2.log, app3.log"), "False\nTrue");
            Eq("Move-Item into a folder", t.T("Move-Item app3.log Archive; (dir Archive).Name -join ','"), "app3.log,old-app.log,readme.txt");
            Eq("Copy-Item -Recurse", t.T("Copy-Item Reports C:\\Backup -Recurse; (dir C:\\Backup).Count"), "3");
            Eq("Copy-Item without -Recurse copies the folder only", t.T("Copy-Item Reports C:\\Empty; (dir C:\\Empty | Measure-Object).Count"), "0");
            Eq("piped items bind to Copy-Item", t.T("Get-ChildItem *.csv | Copy-Item -Destination C:\\Backup; (dir C:\\Backup -Filter *.csv).Count"), "4");
            Eq("Rename-Item -NewName with a script block", t.T("New-Item C:\\Ren -ItemType Directory | Out-Null; 'a','b' | ForEach-Object { New-Item C:\\Ren\\$_.txt | Out-Null }; Get-ChildItem C:\\Ren | Rename-Item -NewName { $_.BaseName + '.bak' }; (dir C:\\Ren).Name -join ','"), "a.bak,b.bak");
            Eq("Rename-Item with a path", Has(t.Run("Rename-Item app.log C:\\x.log"), "Cannot rename the specified target, because it represents a path or device name.") ? "ok" : t.Last, "ok");
            Eq("Rename-Item with brackets in -Path says it does not exist", Has(t.Run("New-Item 'C:\\Ren\\x [1].txt' | Out-Null; Rename-Item 'C:\\Ren\\x [1].txt' y.txt"), "Cannot rename because item at 'C:\\Ren\\x [1].txt' does not exist.") ? "ok" : t.Last, "ok");
            Eq("a folder has no Length property, but .Length is 1", t.T("(Get-Item C:\\Ren | Select-Object Length).Length -eq $null; (Get-Item C:\\Ren).Length"), "True\n1");
            Eq("Select-Object output of folders and files is one table", t.Run("Get-ChildItem C:\\Ren | Select-Object Name, Length | Sort-Object Name -Descending | Select-Object -First 1; Get-Item C:\\Ren | Select-Object Name, Length"), "\nName      Length\n----      ------\nx [1].txt      0\nRen\n\n\n");
            Eq("Group-Object -NoElement has no Group column", t.Run("'a','b','a' | Group-Object -NoElement"), "\nCount Name\n----- ----\n    2 a\n    1 b\n\n\n");
            Eq("$null.Count is 0", t.T("$null.Count; $nothing.Length; (Get-ChildItem C:\\Ren -Filter *.zzz).Count"), "0\n0\n0");
            Eq("LastWriteTime changes when written", t.T("Set-Content shares.csv 'x'; (Get-Item shares.csv).LastWriteTime.ToString('M/d/yyyy H:mm')"), "3/2/2026 9:00");
            Eq("Get-Item -Stream *", t.T("(Get-Item C:\\Users\\Administrator\\Downloads\\Get-Inventory.ps1 -Stream *).Stream -join ','"), ":$DATA,Zone.Identifier");
            Eq("Remove-Item -Stream", t.T("Remove-Item C:\\Users\\Administrator\\Downloads\\Get-Inventory.ps1 -Stream Zone.Identifier; (Get-Item C:\\Users\\Administrator\\Downloads\\Get-Inventory.ps1 -Stream *).Count"), "1");
            t.Dispose();
        }

        static void PsProvRegTests()
        {
            var t = new P();
            t.Run("cd HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion");
            Eq("prompt in the registry", t.C.Prompt, "PS HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion> ");
            Eq("registry listing", t.Run("dir"), "\n\n    Hive: HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\n\n\n" +
                "Name                           Property\n----                           --------\nPolicies\nRun                            SecurityHealth : C:\\Windows\\system32\\SecurityHealthSystray.exe\nUninstall\n\n\n");
            Eq("Get-ItemProperty", t.Run("Get-ItemProperty .\\Run"), "\nSecurityHealth : C:\\Windows\\system32\\SecurityHealthSystray.exe\n" +
                "PSPath         : Microsoft.PowerShell.Core\\Registry::HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run\n" +
                "PSParentPath   : Microsoft.PowerShell.Core\\Registry::HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\n" +
                "PSChildName    : Run\nPSDrive        : HKLM\nPSProvider     : Microsoft.PowerShell.Core\\Registry\n\n\n");
            Eq("RegistryKey object", t.T("$k = Get-Item .\\Run; $k.Name; $k.ValueCount; $k.GetValue('SecurityHealth'); $k.GetValueKind('SecurityHealth')"), "HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run\n1\nC:\\Windows\\system32\\SecurityHealthSystray.exe\nExpandString");
            Eq("Get-ItemPropertyValue", t.T("Get-ItemPropertyValue 'HKLM:\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion' -Name CurrentBuild"), "20348");
            Eq("DWord comes back as an int", t.T("(Get-ItemProperty HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System).EnableLUA.GetType().Name"), "Int32");
            Eq("missing value", t.Run("Get-ItemProperty .\\Run -Name Nope"), "Get-ItemProperty : Property Nope does not exist at path HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run.\nAt line:1 char:1\n+ Get-ItemProperty .\\Run -Name Nope\n+ ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~\n    + CategoryInfo          : InvalidArgument: (Nope:String) [Get-ItemProperty], PSArgumentException\n    + FullyQualifiedErrorId : System.Management.Automation.PSArgumentException,Microsoft.PowerShell.Commands.GetItemPropertyCommand\n\n");
            t.Run("cd C:\\");
            Eq("New-Item key", t.Run("New-Item HKCU:\\Software\\Lab"), "\n\n    Hive: HKEY_CURRENT_USER\\Software\n\n\nName                           Property\n----                           --------\nLab\n\n\n");
            Eq("New-Item key that exists", Has(t.Run("New-Item HKCU:\\Software\\Lab"), "A key at this path already exists.") ? "ok" : t.Last, "ok");
            Eq("New-ItemProperty DWord", t.T("(New-ItemProperty HKCU:\\Software\\Lab -Name Count -PropertyType DWord -Value 5).Count"), "5");
            Eq("New-ItemProperty that exists", Has(t.Run("New-ItemProperty HKCU:\\Software\\Lab -Name Count -PropertyType DWord -Value 6"), "The property already exists.") ? "ok" : t.Last, "ok");
            Eq("bad -PropertyType", Has(t.Run("New-ItemProperty HKCU:\\Software\\Lab -Name X -PropertyType Word -Value 1"), "Microsoft.Win32.RegistryValueKind") ? "ok" : t.Last, "ok");
            t.Run("Set-ItemProperty HKCU:\\Software\\Lab -Name Mode -Value 'Fast'");
            t.Run("New-ItemProperty HKCU:\\Software\\Lab -Name Servers -PropertyType MultiString -Value 'a','b' | Out-Null");
            t.Run("New-ItemProperty HKCU:\\Software\\Lab -Name Big -PropertyType QWord -Value 10 | Out-Null");
            Eq("value kinds", t.T("$k = Get-Item HKCU:\\Software\\Lab; ($k.Property | ForEach-Object { $k.GetValueKind($_) }) -join ','"), "DWord,String,MultiString,QWord");
            Eq("Property column of a key", t.Run("Get-Item HKCU:\\Software\\Lab"), "\n\n    Hive: HKEY_CURRENT_USER\\Software\n\n\nName                           Property\n----                           --------\n" +
                "Lab                            Count   : 5\n                               Mode    : Fast\n                               Servers : {a, b}\n                               Big     : 10\n\n\n");
            Eq("Set-ItemProperty keeps the kind", t.T("Set-ItemProperty HKCU:\\Software\\Lab -Name Count -Value '7'; (Get-ItemProperty HKCU:\\Software\\Lab).Count.GetType().Name"), "Int32");
            Eq("Rename-ItemProperty", t.T("Rename-ItemProperty HKCU:\\Software\\Lab -Name Mode -NewName Speed; (Get-Item HKCU:\\Software\\Lab).Property -join ','"), "Count,Speed,Servers,Big");
            Eq("Remove-ItemProperty", t.T("Remove-ItemProperty HKCU:\\Software\\Lab -Name Big; (Get-Item HKCU:\\Software\\Lab).ValueCount"), "3");
            Eq("Remove-ItemProperty missing", Has(t.Run("Remove-ItemProperty HKCU:\\Software\\Lab -Name Nope"), "Property Nope does not exist at path HKEY_CURRENT_USER\\Software\\Lab.") ? "ok" : t.Last, "ok");
            Eq("Test-Path on keys", t.T("Test-Path HKCU:\\Software\\Lab; Test-Path HKCU:\\Software\\Nope"), "True\nFalse");
            Eq("New-Item -Force makes parents", t.T("New-Item HKCU:\\Software\\A\\B\\C -Force | Out-Null; Test-Path HKCU:\\Software\\A\\B"), "True");
            Eq("Remove-Item key with subkeys asks", Has(t.Run("Remove-Item HKCU:\\Software\\A"), "has children and the Recurse parameter was not specified") ? "ok" : t.Last, "ok");
            t.Run("Y");
            Eq("Yes removes it", t.T("Test-Path HKCU:\\Software\\A"), "False");
            Eq("Remove-Item key", t.T("Remove-Item HKCU:\\Software\\Lab; Test-Path HKCU:\\Software\\Lab"), "False");
            Eq("provider-qualified path", t.T("(Get-Item Registry::HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft).PSChildName"), "Microsoft");
            Eq("Convert-Path", t.T("Convert-Path HKLM:\\SOFTWARE"), "HKEY_LOCAL_MACHINE\\SOFTWARE");
            Eq("New-PSDrive HKCR", t.T("New-PSDrive -Name HKCR -PSProvider Registry -Root HKEY_CLASSES_ROOT | Out-Null; Test-Path HKCR:\\"), "True");
            Eq("Get-ChildItem of an empty key", t.T("New-Item HKCU:\\Software\\Empty | Out-Null; (Get-ChildItem HKCU:\\Software\\Empty | Measure-Object).Count"), "0");
            t.Dispose();
        }

        static void PsProvContentTests()
        {
            var t = new P();
            t.Run("cd C:\\LabFiles");
            Eq("Get-Content lines", t.T("Get-Content servers.txt"), "WIN-SRV1\nWIN-CL1\nDC01\nWEB01");
            Eq("-TotalCount", t.T("Get-Content servers.txt -TotalCount 2"), "WIN-SRV1\nWIN-CL1");
            Eq("-Head alias", t.T("Get-Content servers.txt -Head 1"), "WIN-SRV1");
            Eq("-Tail", t.T("Get-Content servers.txt -Tail 1"), "WEB01");
            Eq("-ReadCount 0 gives one array", t.T("(Get-Content servers.txt -ReadCount 0 | Measure-Object).Count"), "1");
            Eq("-ReadCount 2", t.T("(Get-Content servers.txt -ReadCount 2 | Measure-Object).Count"), "2");
            Eq("-Raw is one string", t.T("$r = Get-Content servers.txt -Raw; $r.GetType().Name; $r.Length"), "String\n32");
            Eq("lines carry PSPath and ReadCount", t.T("$l = Get-Content servers.txt; $l[1].PSPath; $l[1].ReadCount"), "Microsoft.PowerShell.Core\\FileSystem::C:\\LabFiles\\servers.txt\n2");
            Eq("brackets with -Path fail for content", t.Run("Get-Content 'Reports\\summary [old].txt'"), "Get-Content : An object at the specified path C:\\LabFiles\\Reports\\summary [old].txt does not exist, or has been filtered by the -Include or -Exclude parameter.\nAt line:1 char:1\n+ Get-Content 'Reports\\summary [old].txt'\n+ ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~\n    + CategoryInfo          : ObjectNotFound: (C:\\LabFiles\\Reports\\summary [old].txt:String) [Get-Content], ItemNotFoundException\n    + FullyQualifiedErrorId : ItemNotFound,Microsoft.PowerShell.Commands.GetContentCommand\n\n");
            Eq("-LiteralPath reads it", t.T("Get-Content -LiteralPath 'Reports\\summary [old].txt'"), "Summary of last year (kept for the lesson on -LiteralPath).");
            Eq("missing file", Has(t.Run("Get-Content nope.txt"), "PathNotFound,Microsoft.PowerShell.Commands.GetContentCommand") ? "ok" : t.Last, "ok");
            Eq("a folder is denied", Has(t.Run("Get-Content Reports"), "Access to the path 'C:\\LabFiles\\Reports' is denied.") && Has(t.Last, "GetContentReaderUnauthorizedAccessError") ? "ok" : t.Last, "ok");
            Eq("> writes UTF-16 (10 bytes for 'hi')", t.T("'hi' > f.txt; (Get-Item f.txt).Length"), "10");
            Eq(">> keeps UTF-16", t.T("'yo' >> f.txt; (Get-Item f.txt).Length; Get-Content f.txt"), "18\nhi\nyo");
            Eq("Set-Content writes ANSI", t.T("Set-Content g.txt 'hi'; (Get-Item g.txt).Length"), "4");
            Eq("Set-Content -Encoding UTF8 adds a BOM", t.T("Set-Content g.txt 'hi' -Encoding UTF8; (Get-Item g.txt).Length"), "7");
            Eq("Set-Content -Encoding Unicode", t.T("Set-Content g.txt 'hi' -Encoding Unicode; (Get-Item g.txt).Length"), "10");
            Eq("-NoNewline", t.T("Set-Content g.txt 'hi' -NoNewline; (Get-Item g.txt).Length"), "2");
            Eq("bad -Encoding", Has(t.Run("Set-Content g.txt 'hi' -Encoding Latin1"), "Unable to match the identifier name Latin1") ? "ok" : t.Last, "ok");
            Eq("Set-Content from the pipeline", t.T("1..3 | Set-Content n.txt; Get-Content n.txt"), "1\n2\n3");
            Eq("Add-Content", t.T("Add-Content n.txt 4; (Get-Content n.txt).Count"), "4");
            Eq("Add-Content keeps UTF-16", t.T("Add-Content f.txt 'z'; (Get-Item f.txt).Length"), "24");
            Eq("Clear-Content empties the file", t.T("Clear-Content n.txt; (Get-Item n.txt).Length; Test-Path n.txt"), "0\nTrue");
            Eq("Set-Content to a missing folder", Has(t.Run("Set-Content C:\\nope\\x.txt 'a'"), "GetContentWriterDirectoryNotFoundError,Microsoft.PowerShell.Commands.SetContentCommand") ? "ok" : t.Last, "ok");
            t.Run("Set-ItemProperty g.txt -Name IsReadOnly -Value $true");
            Eq("Set-Content on read-only", Has(t.Run("Set-Content g.txt 'b'"), "Access to the path 'C:\\LabFiles\\g.txt' is denied.") ? "ok" : t.Last, "ok");
            Eq("-Force writes it", t.T("Set-Content g.txt 'b' -Force; Get-Content g.txt; (Get-Item g.txt).IsReadOnly"), "b\nTrue");
            Eq("UTF-8 without BOM reads as ANSI (5.1)", t.T("[System.IO.File]::WriteAllText('C:\\LabFiles\\u.txt', 'caf' + [char]233); Get-Content u.txt"), "caf\u00c3\u00a9");
            Eq("-Encoding UTF8 reads it right", t.T("(Get-Content u.txt -Encoding UTF8) -eq ('caf' + [char]233)"), "True");
            Eq("-Stream reads an alternate stream", t.T("Get-Content C:\\Users\\Administrator\\Downloads\\Get-Inventory.ps1 -Stream Zone.Identifier | Select-Object -Index 1"), "ZoneId=3");
            Eq("Env: content", t.T("Get-Content Env:COMPUTERNAME"), "WIN-SRV1");
            Eq("Select-String file output", t.Run("Select-String -Path *.log -Pattern ERROR"), "\napp.log:5:2026-03-01 09:02:41 ERROR Login failed for user 'svc_backup'\napp.log:6:2026-03-01 09:02:44 ERROR Login failed for user 'svc_backup'\napp.log:9:2026-03-01 11:20:58 ERROR Timeout talking to WEB01 after 30 seconds\n\n\n");
            Eq("Select-String paths are relative to the location", t.T("cd C:\\; (Select-String -Path C:\\LabFiles\\app.log -Pattern Disk | Out-String).Trim(); cd C:\\LabFiles"), "LabFiles\\app.log:8:2026-03-01 10:47:03 WARN  Disk C: is 85 percent full");
            Eq("Select-String properties", t.T("$m = Select-String -Path app.log -Pattern 'user (\\S+)' | Select-Object -First 1; $m.LineNumber, $m.Filename, $m.Path, $m.Pattern, $m.Matches[0].Groups[1].Value -join '|'"), "5|app.log|C:\\LabFiles\\app.log|user (\\S+)|'svc_backup'");
            Eq("piped strings show only the line", t.T("'alpha','beta','gamma' | Select-String a$"), "alpha\nbeta\ngamma");
            Eq("piped strings: InputStream", t.T("('x','y' | Select-String y).Path"), "InputStream");
            Eq("-NotMatch", t.T("('a1','b2','a3' | Select-String a -NotMatch).Line"), "b2");
            Eq("-CaseSensitive", t.T("('Error','error' | Select-String -Pattern error -CaseSensitive).Line"), "error");
            Eq("-SimpleMatch", t.T("('a.b','axb' | Select-String -Pattern . -SimpleMatch).Line"), "a.b");
            Eq("-AllMatches", t.T("('a1 a2 a3' | Select-String 'a\\d' -AllMatches).Matches.Count"), "3");
            Eq("-Quiet", t.T("Select-String -Path app.log -Pattern WARN -Quiet; Select-String -Path app.log -Pattern nothing -Quiet"), "True\nFalse");
            Eq("-Context", t.T("Get-Content servers.txt | Select-String DC01 -Context 1"), "  WIN-CL1\n> DC01\n  WEB01");
            Eq("files piped to Select-String", t.T("Get-ChildItem *.log | Select-String Disk"), "app.log:8:2026-03-01 10:47:03 WARN  Disk C: is 85 percent full");
            Eq("Get-FileHash table", t.Run("Get-FileHash servers.txt"), "\nAlgorithm       Hash                                                                   Path\n---------       ----                                                                   ----\nSHA256          2C70D160C160ED1B7851B18424F610399D155A5055B30FF8E30AE2B0C7367289       C:\\LabFiles\\servers.txt\n\n\n");
            Eq("Get-FileHash MD5", t.T("(Get-FileHash servers.txt -Algorithm MD5).Hash.Length"), "32");
            Eq("encoding changes the hash", t.T("Set-Content h1.txt 'x'; 'x' > h2.txt; (Get-FileHash h1.txt).Hash -eq (Get-FileHash h2.txt).Hash"), "False");
            t.Dispose();
        }

        static void PsProvMiscTests()
        {
            var t = new P();
            Eq("Get-PSDrive names", t.T("(Get-PSDrive).Name -join ','"), "Alias,C,Cert,Env,Function,HKCU,HKLM,Variable,WSMan");
            Eq("Get-PSDrive C", t.T("$d = Get-PSDrive C; $d.Root; $d.Provider.Name; $d.CurrentLocation"), "C:\\\nFileSystem\nWindows\\system32");
            Eq("Get-PSDrive missing", Has(t.Run("Get-PSDrive Q"), "Cannot find drive. A drive with the name 'Q' does not exist.") ? "ok" : t.Last, "ok");
            Eq("Get-PSProvider table", t.Run("Get-PSProvider FileSystem, Registry"), "\nName                 Capabilities                                      Drives\n----                 ------------                                      ------\nRegistry             ShouldProcess, Transactions                       {HKCU, HKLM}\nFileSystem           Filter, ShouldProcess, Credentials                {C}\n\n\n");
            Eq("New-PSDrive FileSystem", t.T("New-PSDrive -Name Lab -PSProvider FileSystem -Root C:\\LabFiles | Out-Null; cd Lab:; (Get-Location).Path; (Get-Item servers.txt).FullName"), "Lab:\\\nC:\\LabFiles\\servers.txt");
            Eq("prompt on a New-PSDrive drive", t.C.Prompt, "PS Lab:\\> ");
            Eq("relative files on a custom drive", t.T("Set-Content t.txt 'x'; Test-Path C:\\LabFiles\\t.txt"), "True");
            Eq("New-PSDrive that exists", Has(t.Run("New-PSDrive -Name C -PSProvider FileSystem -Root C:\\"), "A drive with the name 'C' already exists.") ? "ok" : t.Last, "ok");
            Eq("New-PSDrive with a missing root", Has(t.Run("New-PSDrive -Name X -PSProvider FileSystem -Root C:\\nope"), "The specified drive root \"C:\\nope\" either does not exist, or it is not a folder.") ? "ok" : t.Last, "ok");
            Eq("Remove-PSDrive in use", Has(t.Run("Remove-PSDrive Lab"), "Cannot remove drive 'Lab' because it is in use.") ? "ok" : t.Last, "ok");
            Eq("Remove-PSDrive", t.T("cd C:\\; Remove-PSDrive Lab; Test-Path Lab:\\"), "False");
            Eq("Env: listing", t.T("dir Env:USERNAME"), "Name                           Value\n----                           -----\nUSERNAME                       Administrator");
            Eq("Env: is the session's environment", t.T("New-Item Env:LABVAR -Value one | Out-Null; $env:LABVAR; Set-Item Env:LABVAR two; $env:LABVAR"), "one\ntwo");
            Eq("Rename-Item on Env:", t.T("Rename-Item Env:LABVAR LABVAR2; $env:LABVAR2"), "two");
            Eq("Remove-Item Env:", t.T("Remove-Item Env:LABVAR2; Test-Path Env:LABVAR2"), "False");
            Eq("cd Env:", t.T("cd Env:; (Get-Location).Path; cd C:\\"), "Env:\\");
            Eq("Variable: drive", t.T("$lab = 42; (Get-Item Variable:lab).Value; Remove-Item Variable:lab; Test-Path Variable:lab"), "42\nFalse");
            Eq("Alias: drive", t.T("(Get-Item Alias:dir).Definition; New-Item Alias:np -Value Get-Process | Out-Null; (Get-Alias np).Definition"), "Get-ChildItem\nGet-Process");
            Eq("Function: drive", t.T("function Get-Lab { 'lab' }; Test-Path Function:Get-Lab; Remove-Item Function:Get-Lab; Test-Path Function:Get-Lab"), "True\nFalse");
            Eq("Cert: locations", t.T("(dir Cert:\\).Location -join ','"), "CurrentUser,LocalMachine");
            Eq("Cert: listing", t.Run("dir Cert:\\LocalMachine\\My"), "\n\n   PSParentPath: Microsoft.PowerShell.Security\\Certificate::LocalMachine\\My\n\nThumbprint                                Subject\n----------                                -------\nA3C5E7F90B1D3F5A7C9E1B3D5F7A9C1E3B5D7F90  CN=WIN-SRV1.lab.example.com\n\n\n");
            Eq("a certificate's properties", t.T("$c = Get-Item Cert:\\LocalMachine\\Root\\8F43288AD272F3103B6FB1428485EA3014C0BCFE; $c.Subject; $c.HasPrivateKey; $c.NotAfter.Year"), "CN=Microsoft Root Certificate Authority 2011, O=Microsoft Corporation, L=Redmond, S=Washington, C=US\nFalse\n2036");
            Eq("Test-Path -PathType", t.T("Test-Path C:\\LabFiles -PathType Leaf; Test-Path C:\\LabFiles\\app.log -PathType Leaf; Test-Path C:\\LabFiles -PathType Container"), "False\nTrue\nTrue");
            Eq("Test-Path wildcards", t.T("Test-Path C:\\LabFiles\\*.csv; Test-Path C:\\LabFiles\\*.zip"), "True\nFalse");
            Eq("Test-Path missing drive is False", t.T("Test-Path Q:\\x"), "False");
            Eq("Test-Path -IsValid", t.T("Test-Path C:\\new\\x.txt -IsValid; Test-Path 'C:\\a|b' -IsValid"), "True\nFalse");
            Eq("Test-Path $null", t.Run("Test-Path $null"), "Test-Path : Cannot bind argument to parameter 'Path' because it is null.\nAt line:1 char:1\n+ Test-Path $null\n+ ~~~~~~~~~~~~~~~\n    + CategoryInfo          : InvalidData: (:) [Test-Path], ParameterBindingValidationException\n    + FullyQualifiedErrorId : ParameterArgumentValidationErrorNullNotAllowed,Microsoft.PowerShell.Commands.TestPathCommand\n\n");
            Eq("Split-Path", t.T("Split-Path C:\\LabFiles\\app.log; Split-Path C:\\LabFiles\\app.log -Leaf; Split-Path C:\\LabFiles\\app.log -Qualifier; Split-Path C:\\LabFiles\\app.log -NoQualifier; Split-Path C:\\LabFiles"), "C:\\LabFiles\napp.log\nC:\n\\LabFiles\\app.log\nC:\\");
            Eq("Split-Path -Extension is PowerShell 6+", Has(t.Run("Split-Path x.txt -Extension"), "A parameter cannot be found that matches parameter name 'Extension'.") ? "ok" : t.Last, "ok");
            Eq("Join-Path", t.T("Join-Path C:\\LabFiles Reports; Join-Path C:\\LabFiles\\ \\Reports; Join-Path HKLM: SOFTWARE"), "C:\\LabFiles\\Reports\nC:\\LabFiles\\Reports\nHKLM:\\SOFTWARE");
            Eq("Join-Path takes one child in 5.1", Has(t.Run("Join-Path C:\\ a b"), "A positional parameter cannot be found that accepts argument 'b'.") ? "ok" : t.Last, "ok");
            Eq("Join-Path missing drive", Has(t.Run("Join-Path Q:\\ x"), "DriveNotFound,Microsoft.PowerShell.Commands.JoinPathCommand") ? "ok" : t.Last, "ok");
            Eq("Resolve-Path", t.T("cd C:\\LabFiles; (Resolve-Path *.csv).Path"), "C:\\LabFiles\\employees.csv\nC:\\LabFiles\\shares.csv");
            Eq("Resolve-Path -Relative", t.T("Resolve-Path app.log -Relative; Resolve-Path ..\\Windows -Relative"), ".\\app.log\n..\\Windows");
            Eq("Get-Acl table", t.Run("Get-Acl C:\\LabFiles"), "\n\n    Directory: C:\\\n\n\nPath     Owner                  Access\n----     -----                  ------\nLabFiles BUILTIN\\Administrators NT AUTHORITY\\SYSTEM Allow  FullControl...\n\n\n");
            Eq("Get-Acl rules", t.T("((Get-Acl C:\\LabFiles\\app.log).Access | Where-Object IdentityReference -eq 'BUILTIN\\Users').FileSystemRights"), "ReadAndExecute, Synchronize");
            Eq("Get-Acl on a key", t.T("(Get-Acl HKLM:\\SOFTWARE).Owner"), "BUILTIN\\Administrators");
            Eq("Invoke-Item explains", Has(t.Run("Invoke-Item C:\\LabFiles\\notes.txt"), "nothing opens here") ? "ok" : t.Last, "ok");
            Eq("Clear-Item on a variable", t.T("$v = 5; Clear-Item Variable:v; $null -eq $v"), "True");
            Eq("Clear-Item on a file is not supported", Has(t.Run("Clear-Item C:\\LabFiles\\app.log"), "Provider execution stopped because the provider does not support this operation.") ? "ok" : t.Last, "ok");
            Eq("Get-Location -Stack", t.T("Push-Location C:\\Windows; Push-Location C:\\; (Get-Location -Stack).Path; Pop-Location; Pop-Location"), "C:\\Windows\nC:\\LabFiles");
            Eq("drive functions", t.T("cd HKLM:\\SOFTWARE; C:; (Get-Location).Path"), "C:\\LabFiles");
            Eq("Get-ItemProperty of a file", t.T("(Get-ItemProperty C:\\LabFiles\\app.log -Name Length).Length"), "593");
            Eq("Set-ItemProperty Attributes", t.T("Set-ItemProperty C:\\LabFiles\\app.log -Name Attributes -Value 'Hidden'; (Get-Item C:\\LabFiles\\app.log -Force).Mode; (dir C:\\LabFiles -File).Count"), "---h--\n6");
            t.Dispose();
        }
    }
}
