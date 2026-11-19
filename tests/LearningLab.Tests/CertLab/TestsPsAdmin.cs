// Cert Lab - checks of the simulated Windows administration: services, processes, event logs, scheduled tasks on
// the clock, local accounts, CIM / WMI, the computer (restart, rename, features) and the admin programs.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CertLab
{
    public static partial class Tests
    {
        static partial void PsGroupsAdmin(List<KeyValuePair<string, Action>> l)
        {
            l.Add(new KeyValuePair<string, Action>("ps-admin-services", PsAdminServices));
            l.Add(new KeyValuePair<string, Action>("ps-admin-processes", PsAdminProcesses));
            l.Add(new KeyValuePair<string, Action>("ps-admin-events", PsAdminEvents));
            l.Add(new KeyValuePair<string, Action>("ps-admin-tasks", PsAdminTasks));
            l.Add(new KeyValuePair<string, Action>("ps-admin-accounts", PsAdminAccounts));
            l.Add(new KeyValuePair<string, Action>("ps-admin-cim", PsAdminCim));
            l.Add(new KeyValuePair<string, Action>("ps-admin-computer", PsAdminComputer));
            l.Add(new KeyValuePair<string, Action>("ps-admin-natives", PsAdminNatives));
        }

        static void PsAdminServices()
        {
            var t = new P();
            Eq("Get-Service table", t.T("Get-Service Spooler, W32Time"), "Status   Name               DisplayName\n------   ----               -----------\nRunning  Spooler            Print Spooler\nRunning  W32Time            Windows Time");
            Eq("service not found", t.Run("Get-Service nope"), "Get-Service : Cannot find any service with service name 'nope'.\nAt line:1 char:1\n+ Get-Service nope\n+ ~~~~~~~~~~~~~~~~\n    + CategoryInfo          : ObjectNotFound: (nope:String) [Get-Service], ServiceCommandException\n    + FullyQualifiedErrorId : NoServiceFoundForGivenName,Microsoft.PowerShell.Commands.GetServiceCommand\n\n");
            Eq("wildcards find nothing quietly", t.T("Get-Service zz*"), "");
            Eq("display names work", t.T("(Get-Service -DisplayName 'Print Spooler').Name"), "Spooler");
            Eq("a display name as -Name", t.T("(Get-Service 'Windows Time').Name"), "W32Time");
            Eq("StartType property", t.T("(Get-Service WinRM).StartType"), "Automatic");
            Eq("server: WinRM runs", t.T("(Get-Service WinRM).Status"), "Running");
            Eq("client: WinRM stopped and manual", t.On("WIN-CL1", "$s = Get-Service WinRM; \"$($s.Status) $($s.StartType)\"").Trim(), "Stopped Manual");
            Eq("RequiredServices", t.T("(Get-Service Spooler -RequiredServices).Name -join ','"), "RpcSs,http");
            Has("DependentServices of LanmanWorkstation", t.T("(Get-Service LanmanWorkstation -DependentServices).Name -join ','"), "Netlogon");
            Eq("Include / Exclude", t.T("(Get-Service -Name W* -Exclude Win*).Name -join ','"), "W32Time,WdiServiceHost,wuauserv");
            Eq("pipeline by value", t.T("('Spooler' | Get-Service).DisplayName"), "Print Spooler");
            Eq("pipeline by property name", t.T("([pscustomobject]@{Name='W32Time'} | Get-Service).Status"), "Running");
            Eq("ServiceController type", t.T("(Get-Service Spooler).GetType().FullName"), "System.ServiceProcess.ServiceController");
            Eq("Stop-Service writes nothing", t.T("Stop-Service Spooler"), "");
            Eq("stopped", t.T("(Get-Service Spooler).Status"), "Stopped");
            Check("7036 event written", t.M.EvLog("System").Events.Any(e => e.Id == 7036 && e.Message == "The Print Spooler service entered the stopped state."), "");
            Eq("the old object keeps its status until Refresh", t.T("$s = Get-Service W32Time; Stop-Service W32Time; $a = $s.Status; $s.Refresh(); \"$a $($s.Status)\""), "Running Stopped");
            Eq("Start-Service -PassThru", t.T("Start-Service Spooler, W32Time -PassThru | ForEach-Object Status"), "Running\nRunning");
            Eq("dependents block Stop-Service", t.Run("Stop-Service LanmanWorkstation").Contains("Stop-Service : Cannot stop service 'Workstation (LanmanWorkstation)' because it has dependent services. It can only be stopped if the Force flag is set.") && t.Last.Contains("InvalidOperation: (System.ServiceProcess.ServiceController:ServiceController) [Stop-Service], ServiceCommandException") && t.Last.Contains("ServiceHasDependentServices,Microsoft.PowerShell.Commands.StopServiceCommand") ? "ok" : t.Last, "ok");
            Eq("-Force stops the dependents too", t.T("Stop-Service LanmanWorkstation -Force; (Get-Service Netlogon).Status"), "Stopped");
            Eq("Start-Service starts what it needs", t.T("Start-Service Netlogon; (Get-Service LanmanWorkstation).Status"), "Running");
            Eq("pipeline Get-Service | Stop-Service", t.T("Get-Service Spooler | Stop-Service -PassThru | ForEach-Object Status"), "Stopped");
            Eq("Restart-Service", t.T("Restart-Service Spooler -PassThru | ForEach-Object Status"), "Running");
            Eq("Set-Service -StartupType", t.T("Set-Service BITS -StartupType Disabled; (Get-Service BITS).StartType"), "Disabled");
            Check("7040 event written", t.M.EvLog("System").Events.Any(e => e.Id == 7040 && e.Message.Contains("Background Intelligent Transfer Service service was changed from demand start to disabled.")), "");
            Eq("a disabled service does not start", t.Run("Start-Service BITS").Contains("Start-Service : Service 'Background Intelligent Transfer Service (BITS)' cannot be started due to the following error: Cannot start service BITS on computer '.'.") && t.Last.Contains("CouldNotStartService,Microsoft.PowerShell.Commands.StartServiceCommand") ? "ok" : t.Last, "ok");
            Eq("AutomaticDelayedStart is not in 5.1", t.Run("Set-Service BITS -StartupType AutomaticDelayedStart").Contains("Unable to match the identifier name AutomaticDelayedStart") ? "ok" : t.Last, "ok");
            Eq("Set-Service -Status", t.T("Set-Service W32Time -Status Stopped -PassThru | ForEach-Object Status"), "Stopped");
            Eq("Suspend-Service on a service that cannot pause", t.Run("Suspend-Service Spooler").Contains("cannot be suspended because the service does not support being suspended or resumed.") ? "ok" : t.Last, "ok");
            Eq("Suspend / Resume LanmanServer", t.T("Suspend-Service LanmanServer; $a = (Get-Service LanmanServer).Status; Resume-Service LanmanServer; \"$a $((Get-Service LanmanServer).Status)\""), "Paused Running");
            Eq("Stop-Service -WhatIf", t.T("Stop-Service Spooler -WhatIf"), "What if: Performing the operation \"Stop-Service\" on target \"Print Spooler (Spooler)\".");
            Eq("New-Service", t.T("New-Service -Name LabSvc -BinaryPathName 'C:\\Windows\\System32\\notepad.exe' -DisplayName 'Lab Service' -StartupType Manual | Select-Object -ExpandProperty Status"), "Stopped");
            Eq("New-Service twice", t.Run("New-Service LabSvc C:\\x.exe").Contains("Service 'LabSvc' cannot be created due to the following error: The specified service already exists") ? "ok" : t.Last, "ok");
            Eq("start the new service", t.T("Start-Service LabSvc; (Get-Service LabSvc).Status"), "Running");
            Has("Get-Service -ComputerName: blocked by WIN-CL1's firewall", t.Run("Get-Service WinRM -ComputerName WIN-CL1"), "Cannot open Service Control Manager on computer 'WIN-CL1'");
            t.On("WIN-CL1", "Enable-NetFirewallRule -Name WMI-RPCSS-In-TCP");
            Eq("Get-Service -ComputerName once RPC is allowed", t.T("(Get-Service WinRM -ComputerName WIN-CL1).MachineName"), "WIN-CL1");
            Eq("services after a restart: automatic ones run again", t.T("Stop-Service W32Time; Set-Service Spooler -StartupType Manual; Stop-Service Spooler; Start-Service BITS -ErrorAction SilentlyContinue") + "|" + Reboot(t, "\"$((Get-Service W32Time).Status) $((Get-Service Spooler).Status)\""), "|Running Stopped");
            Eq("start type kept after restart", t.T("(Get-Service BITS).StartType"), "Disabled");
            Eq("Get-Service in a list", t.Run("Get-Service Spooler | Format-List").Replace("\r", ""), "\nName                : Spooler\nDisplayName         : Print Spooler\nStatus              : Stopped\nDependentServices   : {}\nServicesDependedOn  : {RpcSs, http}\nCanPauseAndContinue : False\nCanShutdown         : False\nCanStop             : False\nServiceType         : Win32OwnProcess, InteractiveProcess\n\n\n");
            t.Dispose();
        }
        static string Reboot(P t, string after) { t.M.Reboot(); t.C.Execute(""); t.C.TakeOutput(); return t.T(after); }

        static void PsAdminProcesses()
        {
            var t = new P();
            Eq("Get-Process header", t.T("Get-Process -Name lsass").Split('\n')[0], "Handles  NPM(K)    PM(K)      WS(K)     CPU(s)     Id  SI ProcessName");
            Eq("own console", t.T("(Get-Process -Id $PID).ProcessName"), "powershell");
            Eq("process type", t.T("(Get-Process -Id $PID).GetType().FullName"), "System.Diagnostics.Process");
            Eq("not found by name", t.Run("Get-Process nope").Contains("Cannot find a process with the name \"nope\". Verify the process name and call the cmdlet again.") && t.Last.Contains("NoProcessFoundForGivenName,Microsoft.PowerShell.Commands.GetProcessCommand") ? "ok" : t.Last, "ok");
            Eq("not found by id", t.Run("Get-Process -Id 99999").Contains("Cannot find a process with the process identifier 99999.") && t.Last.Contains("ObjectNotFound: (99999:Int32)") ? "ok" : t.Last, "ok");
            Eq("many svchost", t.T("(Get-Process svchost).Count -gt 20"), "True");
            Eq("System and Idle", t.T("(Get-Process -Id 0,4).ProcessName -join ','"), "Idle,System");
            Eq("Start-Process notepad -PassThru", t.T("$n = Start-Process notepad -PassThru; $n.ProcessName"), "notepad");
            Eq("it runs", t.T("(Get-Process notepad).Id -eq $n.Id"), "True");
            Eq("IncludeUserName", t.T("Get-Process notepad -IncludeUserName").Split('\n')[0], "Handles      WS(K)   CPU(s)     Id UserName               ProcessName");
            Has("user name", t.T("Get-Process notepad -IncludeUserName"), "LAB\\Administrator");
            Eq("Stop-Process -WhatIf", t.T("Stop-Process -Name notepad -WhatIf"), "What if: Performing the operation \"Stop-Process\" on target \"notepad (" + t.T("$n.Id") + ")\".");
            Eq("Stop-Process by id", t.T("Stop-Process $n.Id; Get-Process notepad -ErrorAction SilentlyContinue | Measure-Object | ForEach-Object Count"), "0");
            Eq("pipeline into Stop-Process", t.T("Start-Process notepad; Start-Process notepad; Get-Process notepad | Stop-Process -PassThru | Measure-Object | ForEach-Object Count"), "2");
            Eq("protected processes", t.Run("Stop-Process -Name csrss -Force").Contains("because of the following error: Access is denied") && t.Last.Contains("CouldNotStopProcess") ? "ok" : t.Last, "ok");
            Eq("killing spoolsv stops the Spooler", t.T("Stop-Process -Name spoolsv -Force; (Get-Service Spooler).Status"), "Stopped");
            Check("7034 written", t.M.EvLog("System").Events.Any(e => e.Id == 7034), "");
            Eq("a service's process id", t.T("Start-Service Spooler; (Get-CimInstance Win32_Service -Filter \"Name='Spooler'\").ProcessId -eq (Get-Process spoolsv).Id"), "True");
            Eq("Start-Process a script with powershell.exe", t.T("Set-Variable x 1; Start-Process powershell.exe -ArgumentList '-Command','exit 3' -PassThru -Wait | ForEach-Object ExitCode"), "3");
            Eq("Start-Process missing file", t.Run("Start-Process nothere.exe").Contains("This command cannot be run due to the error: The system cannot find the file specified.") ? "ok" : t.Last, "ok");
            Eq("Wait-Process -Timeout", t.Run("Start-Process notepad; Wait-Process notepad -Timeout 2").Contains("is not stopped in the specified time-out.") ? "ok" : t.Last, "ok");
            Eq("Kill method", t.T("(Get-Process notepad).Kill(); Get-Process notepad -ErrorAction SilentlyContinue"), "");
            Eq("processes reset at restart", Reboot(t, "Get-Process notepad -ErrorAction SilentlyContinue | Measure-Object | ForEach-Object Count"), "0");
            t.On("WIN-CL1", "Enable-NetFirewallRule -Name WMI-RPCSS-In-TCP");
            Eq("Get-Process -ComputerName", t.T("(Get-Process -ComputerName WIN-CL1 -Name explorer).MachineName"), "WIN-CL1");
            t.Dispose();
        }

        static void PsAdminEvents()
        {
            var t = new P();
            Eq("logs have history", t.T("(Get-WinEvent -LogName System).Count -gt 100"), "True");
            Has("Get-WinEvent groups by provider", t.Run("Get-WinEvent -LogName System -MaxEvents 1"), "   ProviderName: ");
            Has("Get-WinEvent header", t.Last, "\nTimeCreated                      Id LevelDisplayName Message\n-----------                      -- ---------------- -------\n");
            Eq("record type", t.T("(Get-WinEvent -LogName System -MaxEvents 1).GetType().FullName"), "System.Diagnostics.Eventing.Reader.EventLogRecord");
            Eq("newest first", t.T("$e = Get-WinEvent -LogName System -MaxEvents 2; $e[0].TimeCreated -ge $e[1].TimeCreated"), "True");
            Eq("-Oldest", t.T("$e = Get-WinEvent -LogName System -MaxEvents 2 -Oldest; $e[0].TimeCreated -le $e[1].TimeCreated"), "True");
            Eq("FilterHashtable Id", t.T("(Get-WinEvent -FilterHashtable @{LogName='System'; Id=6005} | Select-Object -ExpandProperty Id -Unique)"), "6005");
            Eq("FilterHashtable Level", t.T("@(Get-WinEvent -FilterHashtable @{LogName='System'; Level=3} | Where-Object Level -ne 3).Count"), "0");
            Eq("FilterHashtable StartTime", t.T("@(Get-WinEvent -FilterHashtable @{LogName='System'; StartTime=(Get-Date).AddHours(-2)} | Where-Object { $_.TimeCreated -lt (Get-Date).AddHours(-2) }).Count"), "0");
            Eq("ProviderName only", t.T("(Get-WinEvent -ProviderName 'Service Control Manager' -MaxEvents 5 | Select-Object -ExpandProperty LogName -Unique)"), "System");
            Eq("FilterXPath", t.T("(Get-WinEvent -LogName System -FilterXPath '*[System[(EventID=7036)]]' -MaxEvents 3).Id -join ','"), "7036,7036,7036");
            Eq("no match", t.Run("Get-WinEvent -FilterHashtable @{LogName='System'; Id=55555}").Contains("No events were found that match the specified selection criteria.") && t.Last.Contains("NoMatchingEventsFound,Microsoft.PowerShell.Commands.GetWinEventCommand") ? "ok" : t.Last, "ok");
            Eq("no such log", t.Run("Get-WinEvent -LogName Nope").Contains("There is not an event log on the localhost computer that matches \"Nope\".") ? "ok" : t.Last, "ok");
            Has("ListLog table", t.T("Get-WinEvent -ListLog Application"), "LogMode   MaximumSizeInBytes RecordCount LogName\n-------   ------------------ ----------- -------\nCircular            20971520");
            Eq("ListLog wildcard", t.T("(Get-WinEvent -ListLog *PowerShell*).LogName -join ','"), "Microsoft-Windows-PowerShell/Operational,Windows PowerShell");
            Eq("ListProvider", t.T("(Get-WinEvent -ListProvider 'Service Control Manager').LogLinks"), "System");
            Eq("LevelDisplayName of a warning", t.T("(Get-WinEvent -FilterHashtable @{LogName='System'; Level=3} -MaxEvents 1).LevelDisplayName"), "Warning");
            Eq("security logons", t.T("(Get-WinEvent -FilterHashtable @{LogName='Security'; Id=4624}).Count -gt 5"), "True");
            Eq("failed logons", t.T("(Get-WinEvent -FilterHashtable @{LogName='Security'; Id=4625}).Count -gt 3"), "True");
            Has("Get-EventLog table", t.T("Get-EventLog -LogName System -Newest 2"), "   Index Time          EntryType   Source                 InstanceID Message\n   ----- ----          ---------   ------                 ---------- -------\n");
            Eq("one table for several entries", t.T("Get-EventLog -LogName System -Newest 3").Split('\n').Length.ToString(), "5");
            Eq("InstanceId of 7036", t.T("(Get-EventLog -LogName System -InstanceId 1073748860 -Newest 1).EventID"), "7036");
            Eq("-EntryType", t.T("@(Get-EventLog -LogName Application -EntryType Error | Where-Object EntryType -ne 'Error').Count"), "0");
            Eq("-Source wildcard", t.T("(Get-EventLog -LogName System -Source Service* -Newest 1).Source"), "Service Control Manager");
            Eq("-List", t.T("Get-EventLog -List").Split('\n')[0], "  Max(K) Retain OverflowAction        Entries Log");
            Has("-List row", t.Last, "  20,480      0 OverwriteAsNeeded");
            Eq("Get-EventLog of a log it cannot read", t.Run("Get-EventLog -LogName Microsoft-Windows-PowerShell/Operational").Contains("The event log 'Microsoft-Windows-PowerShell/Operational' on computer '.' does not exist.") ? "ok" : t.Last, "ok");
            Eq("Write-EventLog without a source", t.Run("Write-EventLog -LogName Application -Source LabApp -EventId 100 -Message hi").Contains("The source name \"LabApp\" does not exist on computer \"localhost\".") ? "ok" : t.Last, "ok");
            Eq("New-EventLog + Write-EventLog", t.T("New-EventLog -LogName Application -Source LabApp; Write-EventLog -LogName Application -Source LabApp -EventId 100 -EntryType Warning -Message 'Disk almost full'; $e = Get-EventLog Application -Source LabApp -Newest 1; \"$($e.EventID) $($e.EntryType) $($e.Message)\""), "100 Warning Disk almost full");
            Eq("Get-WinEvent sees it too", t.T("(Get-WinEvent -FilterHashtable @{LogName='Application'; ProviderName='LabApp'}).LevelDisplayName"), "Warning");
            Eq("wrong log for the source", t.Run("Write-EventLog -LogName System -Source LabApp -EventId 1 -Message x").Contains("is not registered in log 'System'. (It is registered in log 'Application'.)") ? "ok" : t.Last, "ok");
            Eq("New-EventLog makes a log", t.T("New-EventLog -LogName LabLog -Source LabTool; (Get-EventLog -List).Log -contains 'LabLog'"), "True");
            Eq("Limit-EventLog", t.T("Limit-EventLog -LogName LabLog -MaximumSize 1MB -OverflowAction OverwriteAsNeeded; (Get-EventLog -List | Where-Object Log -eq LabLog).MaximumKilobytes"), "1024");
            Eq("Clear-EventLog", t.T("Write-EventLog LabLog LabTool 7 -Message x; Clear-EventLog LabLog; (Get-EventLog -List | Where-Object Log -eq LabLog).Entries.Count"), "0");
            Eq("Remove-EventLog", t.T("Remove-EventLog -LogName LabLog; (Get-EventLog -List).Log -contains 'LabLog'"), "False");
            Eq("a restart writes 6006 and 6005", t.T("$before = Get-Date") + Reboot(t, "(Get-WinEvent -FilterHashtable @{LogName='System'; Id=6005,6006,1074} | Select-Object -First 3 | Sort-Object Id).Id -join ','"), "1074,6005,6006");
            Eq("PowerShell logs a new console", t.T("(Get-WinEvent -LogName 'Windows PowerShell' -MaxEvents 1).Id -in 400,600"), "True");
            t.Dispose();
        }

        static void PsAdminTasks()
        {
            var t = new P();
            Eq("Get-ScheduledTask header", t.T("Get-ScheduledTask -TaskName ScheduledDefrag"), "TaskPath                                       TaskName                          State\n--------                                       --------                          -----\n\\Microsoft\\Windows\\Defrag\\                     ScheduledDefrag                   Ready");
            Eq("by path", t.T("(Get-ScheduledTask -TaskPath '\\Microsoft\\Windows\\Time Synchronization\\').TaskName -join ','"), "ForceSynchronizeTime,SynchronizeTime");
            Eq("not found", t.Run("Get-ScheduledTask -TaskName Nope").Contains("No MSFT_ScheduledTask objects found with property 'TaskName' equal to 'Nope'.  Verify the value of the property and retry.") && t.Last.Contains("CmdletizationQuery_NotFound_TaskName,Get-ScheduledTask") ? "ok" : t.Last, "ok");
            t.Run("$a = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument '-NoProfile -Command \"Set-Variable -Name z -Value 1; exit 0\"'");
            Eq("action object", t.T("$a.Execute"), "powershell.exe");
            Eq("daily trigger at 3am today", t.T("$tr = New-ScheduledTaskTrigger -Daily -At 3am; $tr.StartBoundary"), "2026-03-02T03:00:00");
            Eq("trigger class", t.T("$tr.CimClass.ToString()"), "Root/Microsoft/Windows/TaskScheduler:MSFT_TaskDailyTrigger");
            Eq("Register-ScheduledTask", t.T("Register-ScheduledTask -TaskName Nightly -Action $a -Trigger $tr -User SYSTEM | ForEach-Object State"), "Ready");
            Eq("registered twice", t.Run("Register-ScheduledTask -TaskName Nightly -Action $a -Trigger $tr").Contains("ResourceExists: (PS_ScheduledTask:Root/Microsoft/...S_ScheduledTask) [Register-ScheduledTask], CimException") && t.Last.Contains("HRESULT 0x800700b7,Register-ScheduledTask") ? "ok" : t.Last, "ok");
            Eq("-Force replaces it", t.T("(Register-ScheduledTask -TaskName Nightly -Action $a -Trigger $tr -User SYSTEM -Force).TaskName"), "Nightly");
            Eq("never run yet", t.T("(Get-ScheduledTaskInfo Nightly).LastTaskResult"), "267011");
            Eq("next run", t.T("(Get-ScheduledTaskInfo Nightly).NextRunTime.ToString('yyyy-MM-dd HH:mm')"), "2026-03-03 03:00");
            Eq("principal", t.T("$x = (Get-ScheduledTask Nightly).Principal; \"$($x.UserId) $($x.LogonType)\""), "SYSTEM ServiceAccount");
            t.M.TaskOutput = "";
            t.Run("$a2 = New-ScheduledTaskAction -Execute powershell.exe -Argument '-File C:\\LabFiles\\job.ps1'");
            t.M.File("C:\\LabFiles\\job.ps1", "Write-Output 'job ran'\r\nWrite-EventLog -LogName Application -Source MsiInstaller -EventId 4242 -Message 'job ran'\r\n", t.M.Now);
            t.Run("Register-ScheduledTask -TaskName Job -Action $a2 -Trigger (New-ScheduledTaskTrigger -Once -At '9:30 AM') -User SYSTEM");
            Eq("not yet", t.T("(Get-ScheduledTaskInfo Job).LastTaskResult"), "267011");
            t.Sim.Advance(1900);
            Eq("ran on the clock", t.T("(Get-ScheduledTaskInfo Job).LastTaskResult"), "0");
            Eq("its script ran", t.T("(Get-EventLog Application -InstanceId 4242).Count"), "1");
            Eq("once: no next run", t.T("$null -eq (Get-ScheduledTaskInfo Job).NextRunTime"), "True");
            Eq("last run time", t.T("(Get-ScheduledTaskInfo Job).LastRunTime.ToString('HH:mm')"), "09:30");
            t.Run("Register-ScheduledTask -TaskName Rep -Action $a2 -Trigger (New-ScheduledTaskTrigger -Once -At '10:00 AM' -RepetitionInterval (New-TimeSpan -Minutes 15) -RepetitionDuration (New-TimeSpan -Hours 1)) -User SYSTEM");
            Eq("repetition", t.T("(Get-ScheduledTask Rep).Triggers[0].Repetition.Interval"), "PT15M");
            t.Sim.Advance(7200);
            Eq("ran every 15 minutes for an hour", t.T("(Get-EventLog Application -InstanceId 4242).Count"), "5");
            Eq("Start-ScheduledTask", t.T("Start-ScheduledTask -TaskName Job; (Get-EventLog Application -InstanceId 4242).Count"), "6");
            Eq("Disable-ScheduledTask", t.T("(Disable-ScheduledTask -TaskName Rep).State"), "Disabled");
            Eq("disabled task cannot start", t.Run("Start-ScheduledTask Rep").Contains("The task is disabled.") ? "ok" : t.Last, "ok");
            Eq("Enable-ScheduledTask", t.T("(Enable-ScheduledTask -TaskName Rep).State"), "Ready");
            Eq("a missing script gives 0xFFFD0000", t.T("Register-ScheduledTask -TaskName Bad -Action (New-ScheduledTaskAction -Execute powershell.exe -Argument '-File C:\\nothere.ps1') -User SYSTEM | Out-Null; Start-ScheduledTask Bad; (Get-ScheduledTaskInfo Bad).LastTaskResult"), "4294770688");
            Eq("Set-ScheduledTask a new trigger", t.T("Set-ScheduledTask -TaskName Job -Trigger (New-ScheduledTaskTrigger -Weekly -DaysOfWeek Monday,Friday -At 6pm) | Out-Null; (Get-ScheduledTask Job).Triggers[0].DaysOfWeek"), "34");
            Eq("weekly next run", t.T("(Get-ScheduledTaskInfo Job).NextRunTime.ToString('ddd HH:mm')"), "Mon 18:00");
            Eq("Unregister -Confirm:$false", t.T("Unregister-ScheduledTask -TaskName Bad -Confirm:$false; (Get-ScheduledTask -TaskName Bad -ErrorAction SilentlyContinue) -eq $null"), "True");
            Eq("pipeline into Unregister", t.T("Get-ScheduledTask -TaskName Rep | Unregister-ScheduledTask -Confirm:$false; @(Get-ScheduledTask | Where-Object TaskName -eq Rep).Count"), "0");
            Eq("startup trigger runs at restart", t.T("Register-ScheduledTask -TaskName Boot -Action $a2 -Trigger (New-ScheduledTaskTrigger -AtStartup) -User SYSTEM | Out-Null") + Reboot(t, "(Get-EventLog Application -InstanceId 4242).Count"), "7");
            Eq("tasks stay after restart", t.T("(Get-ScheduledTask Nightly).TaskName"), "Nightly");
            Eq("schtasks sees them", t.T("schtasks /query /tn Nightly").Contains("Nightly                                  3/3/2026 3:00:00 AM    Ready") ? "ok" : t.Last, "ok");
            Eq("New-ScheduledTaskPrincipal", t.T("$p = New-ScheduledTaskPrincipal -UserId SYSTEM -RunLevel Highest; \"$($p.LogonType) $($p.RunLevel)\""), "ServiceAccount Highest");
            Eq("New-ScheduledTaskSettingsSet", t.T("(New-ScheduledTaskSettingsSet -StartWhenAvailable).StartWhenAvailable"), "True");
            Eq("New-ScheduledTask then register", t.T("$a = New-ScheduledTaskAction -Execute x.exe; $tr = New-ScheduledTaskTrigger -Daily -At 3am; $task = New-ScheduledTask -Action $a -Trigger $tr -Principal $p -Settings (New-ScheduledTaskSettingsSet); (Register-ScheduledTask -TaskName FromObj -InputObject $task).Principal.RunLevel"), "Highest");
            t.Dispose();
        }

        static void PsAdminAccounts()
        {
            var t = new P();
            Eq("Get-LocalUser table", t.T("Get-LocalUser Administrator, Guest"), "Name          Enabled Description\n----          ------- -----------\nAdministrator True    Built-in account for administering the computer/domain\nGuest         False   Built-in account for guest access to the computer/domain");
            Eq("user not found", t.Run("Get-LocalUser nope"), "Get-LocalUser : User nope was not found.\nAt line:1 char:1\n+ Get-LocalUser nope\n+ ~~~~~~~~~~~~~~~~~~\n    + CategoryInfo          : ObjectNotFound: (nope:String) [Get-LocalUser], UserNotFoundException\n    + FullyQualifiedErrorId : UserNotFound,Microsoft.PowerShell.Commands.GetLocalUserCommand\n\n");
            Eq("SID", t.T("(Get-LocalUser Administrator).SID.Value -like 'S-1-5-21-*-500'"), "True");
            t.C.Local.SetVar("pw", new PsSecureString { Plain = "Pa55w.rd!" }, t.C.Local.Global);
            t.C.Local.SetVar("weak", new PsSecureString { Plain = "abc" }, t.C.Local.Global);
            Eq("New-LocalUser", t.T("New-LocalUser -Name alice -Password $pw -FullName 'Alice Admin' -Description 'Lab admin'"), "Name  Enabled Description\n----  ------- -----------\nalice True    Lab admin");
            Check("4720 written", t.M.EvLog("Security").Events.Any(e => e.Id == 4720 && e.Message.Contains("alice")), "");
            Eq("password policy on the server", t.Run("New-LocalUser -Name weakling -Password $weak").Contains("Unable to update the password. The value provided for the new password does not meet the length, complexity, or history requirements of the domain.") ? "ok" : t.Last, "ok");
            Eq("weak password is fine on the client", t.On("WIN-CL1", "New-LocalUser -Name kid -NoPassword | ForEach-Object Name").Trim(), "kid");
            Eq("user exists", t.Run("New-LocalUser -Name alice -NoPassword").Contains("User alice already exists.") && t.Last.Contains("ResourceExists: (alice:String) [New-LocalUser], UserExistsException") ? "ok" : t.Last, "ok");
            Eq("name too long", t.Run("New-LocalUser -Name abcdefghijklmnopqrstuvwxyz -NoPassword").Contains("The character length of the 26 argument is too long") ? "ok" : t.Last, "ok");
            Eq("password expires in 42 days", t.T("((Get-LocalUser alice).PasswordExpires - (Get-LocalUser alice).PasswordLastSet).Days"), "42");
            Eq("Set-LocalUser", t.T("Set-LocalUser -Name alice -PasswordNeverExpires $true -Description 'Changed'; $u = Get-LocalUser alice; \"$($u.Description) $($null -eq $u.PasswordExpires)\""), "Changed True");
            Eq("Disable / Enable", t.T("Disable-LocalUser alice; $a = (Get-LocalUser alice).Enabled; Get-LocalUser alice | Enable-LocalUser; \"$a $((Get-LocalUser alice).Enabled)\""), "False True");
            Eq("ByPropertyName from objects", t.T("[pscustomobject]@{Name='csv1'; FullName='From CSV'; Description='imported'}, [pscustomobject]@{Name='csv2'; FullName='Two'; Description='imported'} | New-LocalUser -NoPassword | ForEach-Object Name"), "csv1\ncsv2");
            Eq("FullName came along", t.T("(Get-LocalUser csv1).FullName"), "From CSV");
            Eq("Add-LocalGroupMember", t.T("Add-LocalGroupMember -Group Administrators -Member alice, 'LAB\\Helpdesk'; (Get-LocalGroupMember Administrators).Name -join ','"), "WIN-SRV1\\Administrator,LAB\\Domain Admins,WIN-SRV1\\alice,LAB\\Helpdesk");
            Eq("Get-LocalGroupMember table", t.T("Get-LocalGroupMember -Name Administrators -Member alice"), "ObjectClass Name           PrincipalSource\n----------- ----           ---------------\nUser        WIN-SRV1\\alice Local");
            Eq("already a member", t.Run("Add-LocalGroupMember Administrators alice").Contains("WIN-SRV1\\alice is already a member of group Administrators.") && t.Last.Contains("MemberExists,Microsoft.PowerShell.Commands.AddLocalGroupMemberCommand") ? "ok" : t.Last, "ok");
            Eq("principal not found", t.Run("Add-LocalGroupMember Administrators nobody").Contains("Principal nobody was not found.") ? "ok" : t.Last, "ok");
            Eq("group not found", t.Run("Get-LocalGroupMember Nope").Contains("Group Nope was not found.") ? "ok" : t.Last, "ok");
            Check("4732 written", t.M.EvLog("Security").Events.Any(e => e.Id == 4732), "");
            Eq("Remove-LocalGroupMember", t.T("Remove-LocalGroupMember Administrators -Member alice; (Get-LocalGroupMember Administrators).Name -contains 'WIN-SRV1\\alice'"), "False");
            Eq("New-LocalGroup", t.T("New-LocalGroup -Name WebAdmins -Description 'Web site admins'"), "Name      Description\n----      -----------\nWebAdmins Web site admins");
            Eq("Get-LocalGroup has the built-ins", t.T("(Get-LocalGroup 'Remote Management Users', 'Event Log Readers').Count"), "2");
            Eq("Rename-LocalGroup", t.T("Rename-LocalGroup WebAdmins -NewName SiteAdmins; (Get-LocalGroup SiteAdmins).Name"), "SiteAdmins");
            Eq("Rename-LocalUser", t.T("Rename-LocalUser -Name csv2 -NewName csv3; (Get-LocalUser csv3).FullName"), "Two");
            Eq("Remove-LocalUser", t.T("Remove-LocalUser csv1, csv3; @(Get-LocalUser csv*).Count"), "0");
            Eq("built-in accounts stay", t.Run("Remove-LocalUser Administrator").Contains("Cannot perform this operation on built-in accounts.") ? "ok" : t.Last, "ok");
            Eq("accounts survive a restart", Reboot(t, "(Get-LocalUser alice).Name"), "alice");
            Eq("net user shows them", t.T("net user").Contains("alice") ? "ok" : t.Last, "ok");
            Eq("client admins include LabUser", t.On("WIN-CL1", "(Get-LocalGroupMember Administrators).Name -join ','").Trim(), "WIN-CL1\\Administrator,WIN-CL1\\LabUser,LAB\\Domain Admins");
            t.Dispose();
        }

        static void PsAdminCim()
        {
            var t = new P();
            Eq("Win32_OperatingSystem table", t.T("Get-CimInstance Win32_OperatingSystem").Split('\n')[0], "SystemDirectory     Organization BuildNumber RegisteredUser    SerialNumber            Version");
            Eq("CIM type name", t.T("(Get-CimInstance Win32_OperatingSystem).PSObject.TypeNames[0]"), "Microsoft.Management.Infrastructure.CimInstance#root/cimv2/Win32_OperatingSystem");
            Eq("LastBootUpTime is a DateTime", t.T("(Get-CimInstance Win32_OperatingSystem).LastBootUpTime.GetType().Name"), "DateTime");
            Eq("Caption", t.T("(Get-CimInstance -ClassName Win32_OperatingSystem).Caption"), "Microsoft Windows Server 2022 Datacenter");
            Eq("client caption", t.On("WIN-CL1", "(Get-CimInstance Win32_OperatingSystem).Caption").Trim(), "Microsoft Windows 11 Enterprise");
            Eq("free memory below total", t.T("$o = Get-CimInstance Win32_OperatingSystem; $o.FreePhysicalMemory -lt $o.TotalVisibleMemorySize"), "True");
            Eq("Win32_ComputerSystem", t.T("$c = Get-CimInstance Win32_ComputerSystem; \"$($c.Domain) $($c.Model) $($c.PartOfDomain)\""), "lab.example.com Virtual Machine True");
            Has("Win32_BIOS list", t.Run("Get-CimInstance Win32_BIOS"), "\nSMBIOSBIOSVersion : Hyper-V UEFI Release v4.1\nManufacturer      : Microsoft Corporation\nName              : Hyper-V UEFI Release v4.1\n");
            Eq("Win32_LogicalDisk -Filter", t.T("(Get-CimInstance Win32_LogicalDisk -Filter 'DriveType=3').DeviceID"), "C:");
            Eq("disk free space in GB", t.T("[math]::Round((Get-CimInstance Win32_LogicalDisk -Filter \"DeviceID='C:'\").FreeSpace / 1GB)"), "98");
            Eq("-Query", t.T("@(Get-CimInstance -Query \"SELECT * FROM Win32_Service WHERE StartMode='Manual'\").Count -ge 1"), "True");
            Eq("WQL LIKE", t.T("(Get-CimInstance Win32_Service -Filter \"Name LIKE 'Win%'\").Name -join ','"), "WinDefend,WinHttpAutoProxySvc,Winmgmt,WinRM");
            Eq("WQL AND / OR", t.T("(Get-CimInstance Win32_Service -Filter \"(Name='Spooler' OR Name='W32Time') AND State='Running'\").Count"), "2");
            Eq("-Property leaves the rest empty", t.T("$s = Get-CimInstance Win32_Service -Filter \"Name='Spooler'\" -Property Name; \"$($s.Name)|$($s.State)\""), "Spooler|");
            Eq("invalid class", t.Run("Get-CimInstance Win32_Nope").Contains("Get-CimInstance : Invalid class") && t.Last.Contains("MetadataError: (root/cimv2:Win32_Nope:String) [Get-CimInstance], CimException") && t.Last.Contains("HRESULT 0x80041010,Microsoft.Management.Infrastructure.CimCmdlets.GetCimInstanceCommand") ? "ok" : t.Last, "ok");
            Eq("invalid query", t.Run("Get-CimInstance Win32_Service -Filter \"Name = \"").Contains("Invalid query") ? "ok" : t.Last, "ok");
            Eq("Win32_Process", t.T("(Get-CimInstance Win32_Process -Filter \"Name='lsass.exe'\").ProcessId -eq (Get-Process lsass).Id"), "True");
            Eq("Win32_Process Create", t.T("$r = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{CommandLine='notepad.exe'}; $r.ReturnValue; (Get-Process -Id $r.ProcessId).ProcessName"), "0\nnotepad");
            Eq("Terminate", t.T("Get-CimInstance Win32_Process -Filter \"Name='notepad.exe'\" | Invoke-CimMethod -MethodName Terminate | ForEach-Object ReturnValue; @(Get-Process notepad -ErrorAction SilentlyContinue).Count"), "0\n0");
            Eq("Win32_Service StopService", t.T("(Get-CimInstance Win32_Service -Filter \"Name='Spooler'\" | Invoke-CimMethod -MethodName StopService).ReturnValue; (Get-Service Spooler).Status"), "0\nStopped");
            Eq("StopService again: 5", t.T("(Invoke-CimMethod -Query \"SELECT * FROM Win32_Service WHERE Name='Spooler'\" -MethodName StopService).ReturnValue"), "5");
            Eq("StartService", t.T("(Get-CimInstance Win32_Service -Filter \"Name='Spooler'\" | Invoke-CimMethod -MethodName StartService).ReturnValue"), "0");
            Eq("ChangeStartMode", t.T("Get-CimInstance Win32_Service -Filter \"Name='Spooler'\" | Invoke-CimMethod -MethodName ChangeStartMode -Arguments @{StartMode='Manual'} | Out-Null; (Get-Service Spooler).StartType"), "Manual");
            Eq("Win32_QuickFixEngineering", t.T("(Get-CimInstance Win32_QuickFixEngineering).Count"), "4");
            Eq("Win32_UserAccount LocalAccount", t.T("(Get-CimInstance Win32_UserAccount -Filter 'LocalAccount=True').Name -join ','"), "Administrator,DefaultAccount,Guest,WDAGUtilityAccount");
            Eq("Win32_Group", t.T("(Get-CimInstance Win32_Group -Filter \"Name='Administrators'\").SID"), "S-1-5-32-544");
            Eq("Win32_Share", t.T("(Get-CimInstance Win32_Share).Name -join ','"), "ADMIN$,C$,IPC$");
            Eq("Win32_NetworkAdapterConfiguration", t.T("(Get-CimInstance Win32_NetworkAdapterConfiguration -Filter 'IPEnabled=True').IPAddress[0]"), "10.0.10.11");
            Eq("Win32_StartupCommand reads the Run key", t.T("(Get-CimInstance Win32_StartupCommand).Caption"), "SecurityHealth");
            Eq("Win32_PhysicalMemory total", t.T("(Get-CimInstance Win32_PhysicalMemory | Measure-Object Capacity -Sum).Sum / 1GB"), "8");
            Eq("Get-CimClass", t.T("(Get-CimClass -ClassName Win32_Ser*).CimClassName"), "Win32_Service");
            Has("Get-CimClass view", t.Run("Get-CimClass Win32_BIOS"), "   NameSpace: ROOT/cimv2\n\nCimClassName                        CimClassMethods      CimClassProperties\n");
            Eq("remote CIM to the client fails without WinRM", t.Run("Get-CimInstance Win32_BIOS -ComputerName WIN-CL1").Contains("WinRM cannot complete the operation.") && t.Last.Contains("HRESULT 0x80338126") ? "ok" : t.Last, "ok");
            Eq("remote CIM to the server works", t.On("WIN-CL1", "(Get-CimInstance Win32_OperatingSystem -ComputerName WIN-SRV1).CSName").Trim(), "WIN-SRV1");
            Eq("PSComputerName on remote objects", t.On("WIN-CL1", "(Get-CimInstance Win32_BIOS -ComputerName WIN-SRV1).PSComputerName").Trim(), "WIN-SRV1");
            Eq("New-CimSession", t.On("WIN-CL1", "$cs = New-CimSession -ComputerName WIN-SRV1; \"$($cs.Id) $($cs.Name) $($cs.Protocol)\"").Trim(), "1 CimSession1 WSMAN");
            Eq("Get-CimInstance -CimSession", t.On("WIN-CL1", "(Get-CimInstance -CimSession $cs -ClassName Win32_ComputerSystem).Name").Trim(), "WIN-SRV1");
            Eq("Get-ScheduledTask -CimSession", t.On("WIN-CL1", "(Get-ScheduledTask -CimSession $cs -TaskName ServerManager).PSComputerName").Trim(), "WIN-SRV1");
            Eq("Get-CimSession / Remove-CimSession", t.On("WIN-CL1", "(Get-CimSession).Count; Get-CimSession | Remove-CimSession; @(Get-CimSession).Count").Trim().Replace("\r", ""), "1\n0");
            Eq("Get-WmiObject", t.T("(Get-WmiObject Win32_OperatingSystem).__CLASS"), "Win32_OperatingSystem");
            Eq("WMI dates are strings", t.T("(Get-WmiObject -Class Win32_OperatingSystem).LastBootUpTime -match '^\\d{14}\\.\\d{6}-480$'"), "True");
            Eq("ConvertToDateTime", t.T("$o = Get-WmiObject Win32_OperatingSystem; $o.ConvertToDateTime($o.LastBootUpTime).GetType().Name"), "DateTime");
            Has("WMI list view", t.Run("Get-WmiObject Win32_Service -Filter \"Name='W32Time'\""), "\nExitCode  : 0\nName      : W32Time\nProcessId : ");
            Eq("WMI method", t.T("(Get-WmiObject Win32_Service -Filter \"Name='Spooler'\").StopService().ReturnValue"), "0");
            Eq("gwmi alias", t.T("(gwmi Win32_BIOS).Manufacturer"), "Microsoft Corporation");
            Eq("Get-WmiObject -Query", t.T("(Get-WmiObject -Query \"select Name from Win32_Service where Name='W32Time'\").__PROPERTY_COUNT"), "1");
            Eq("invalid WMI class", t.Run("Get-WmiObject Win32_Nope").Contains("Invalid class \"Win32_Nope\"") ? "ok" : t.Last, "ok");
            Eq("Invoke-WmiMethod", t.T("(Invoke-WmiMethod -Class Win32_Process -Name Create -ArgumentList notepad.exe).ReturnValue"), "0");
            t.Dispose();
        }

        static void PsAdminComputer()
        {
            var t = new P();
            Has("Get-HotFix table", t.T("Get-HotFix"), "Source        Description      HotFixID      InstalledBy          InstalledOn\n------        -----------      --------      -----------          -----------\nWIN-SRV1      Update           KB5050575     NT AUTHORITY\\SYSTEM  1/12/2026 12:00:00 AM");
            Eq("Get-HotFix -Id", t.T("(Get-HotFix -Id KB5012170).Description"), "Security Update");
            Eq("hotfix not found", t.Run("Get-HotFix -Id KB1").Contains("Cannot find the requested hotfix on the 'localhost' computer.") ? "ok" : t.Last, "ok");
            Eq("InstalledOn is a date", t.T("(Get-HotFix -Id KB5012170).InstalledOn.GetType().Name"), "DateTime");
            Eq("Get-ComputerInfo -Property", t.T("(Get-ComputerInfo -Property Os*).OsName"), "Microsoft Windows Server 2022 Datacenter");
            Eq("Get-ComputerInfo CsDomain", t.T("(Get-ComputerInfo).CsDomain"), "lab.example.com");
            Has("Get-WindowsFeature view", t.T("Get-WindowsFeature -Name FS-FileServer"), "Display Name                                            Name                       Install State\n------------                                            ----                       -------------\n        [X] File Server                                 FS-FileServer                  Installed");
            Eq("Install-WindowsFeature", t.T("Install-WindowsFeature -Name Web-Server -IncludeManagementTools"), "Success Restart Needed Exit Code      Feature Result\n------- -------------- ---------      --------------\nTrue    No             Success        {Web Server (IIS), Management Tools, IIS Management Console, Web Server...}");
            Eq("Web-Server installed", t.T("(Get-WindowsFeature Web-Server).InstallState"), "Installed");
            Eq("W3SVC runs", t.T("(Get-Service W3SVC).Status"), "Running");
            Check("wwwroot exists", t.M.FindFile("C:\\inetpub\\wwwroot\\iisstart.htm") != null, "");
            Eq("default sub-features", t.T("(Get-WindowsFeature Web-Static-Content, Web-Ftp-Server).Installed -join ','"), "True,False");
            Eq("twice: no change", t.T("(Install-WindowsFeature Web-Server).ExitCode"), "NoChangeNeeded");
            Eq("unknown feature", t.Run("Install-WindowsFeature Nope").Contains("ArgumentNotValid: The role, role service, or feature name is not valid: 'Nope'. The name was not found.") ? "ok" : t.Last, "ok");
            Eq("a feature that needs a restart", t.T("$r = Install-WindowsFeature Hyper-V -WarningAction SilentlyContinue; \"$($r.RestartNeeded) $((Get-WindowsFeature Hyper-V).InstallState)\""), "Yes InstallPending");
            Eq("installed after restart", Reboot(t, "(Get-WindowsFeature Hyper-V).InstallState"), "Installed");
            Eq("features stay after restart", t.T("(Get-Service W3SVC).Status"), "Running");
            Eq("Uninstall-WindowsFeature", t.T("Uninstall-WindowsFeature Web-Server -WarningAction SilentlyContinue | Out-Null; (Get-WindowsFeature Web-Server).Installed; Get-Service W3SVC -ErrorAction SilentlyContinue"), "False");
            Eq("not on the client", t.On("WIN-CL1", "Get-WindowsFeature").Contains("The term 'Get-WindowsFeature' is not recognized") ? "ok" : t.Last, "ok");
            Eq("Rename-Computer warns", t.T("Rename-Computer -NewName WEB-SRV9"), "WARNING: The changes will take effect after you restart the computer WIN-SRV1.");
            Eq("the name changes at restart", t.T("$env:COMPUTERNAME") + "|" + Reboot(t, "$env:COMPUTERNAME"), "WIN-SRV1|WEB-SRV9");
            Check("DNS follows", t.Sim.Net.FindHost("web-srv9.lab.example.com") != null, "");
            Eq("Restart-Computer restarts the machine", t.T("$b = (Get-CimInstance Win32_OperatingSystem).LastBootUpTime; Restart-Computer -Force"), "[WEB-SRV9 restarted. A new PowerShell window opened.]");
            t.C.Execute(""); t.C.TakeOutput();
            Eq("a new boot time", t.T("(Get-CimInstance Win32_OperatingSystem).LastBootUpTime -gt (Get-Date).AddMinutes(-2)"), "True");
            Eq("1074 names the restart", t.T("(Get-WinEvent -FilterHashtable @{LogName='System'; Id=1074} -MaxEvents 1).Message -like '*powershell.exe*restart*'"), "True");
            Eq("Restart-Computer -WhatIf", t.T("Restart-Computer -WhatIf"), "What if: Performing the operation \"Restart-Computer\" on target \"WEB-SRV9\".");
            Eq("Stop-Computer on the client", t.On("WIN-CL1", "Stop-Computer -ComputerName WIN-CL1 -Force").Contains("was shut down") ? "ok" : t.Last, "ok");
            t.Dispose();
        }

        static void PsAdminNatives()
        {
            var t = new P();
            Eq("sc is Set-Content: sc.exe is the program", t.T("sc.exe query Spooler").Split('\n')[0], "SERVICE_NAME: Spooler ");
            Has("sc.exe query state", t.Last, "        STATE              : 4  RUNNING ");
            Has("sc.exe queryex has the PID", t.T("sc.exe queryex Spooler"), "        PID                : " + t.T("(Get-Process spoolsv).Id"));
            Eq("sc.exe unknown service", t.T("sc.exe query nope; $LASTEXITCODE"), "[SC] EnumQueryServicesStatus:OpenService FAILED 1060:\n\nThe specified service does not exist as an installed service.\n\n1060");
            Eq("sc.exe stop", t.T("sc.exe stop Spooler | Select-Object -Index 3"), "        STATE              : 3  STOP_PENDING ");
            Eq("sc.exe config start= disabled", t.T("sc.exe config Spooler start= disabled; (Get-Service Spooler).StartType"), "[SC] ChangeServiceConfig SUCCESS\nDisabled");
            Eq("sc.exe start disabled", t.T("sc.exe start Spooler; $LASTEXITCODE").EndsWith("1058") ? "ok" : t.Last, "ok");
            Has("sc.exe qc", t.T("sc.exe qc Spooler"), "        START_TYPE         : 4   DISABLED");
            Eq("sc.exe create / delete", t.T("sc.exe create LabSvc2 binPath= C:\\Windows\\System32\\notepad.exe start= demand; (Get-Service LabSvc2).StartType; sc.exe delete LabSvc2; Get-Service LabSvc2 -ErrorAction SilentlyContinue"), "[SC] CreateService SUCCESS\nManual\n[SC] DeleteService SUCCESS");
            Eq("net start a service", t.T("sc.exe config Spooler start= auto | Out-Null; net start spooler"), "The Print Spooler service is starting.\nThe Print Spooler service was started successfully.");
            Eq("net start again", t.T("net start spooler; $LASTEXITCODE"), "The requested service has already been started.\n\nMore help is available by typing NET HELPMSG 2182.\n\n2");
            Has("net start lists", t.T("net start"), "These Windows services are started:\n\n   Application Information\n   Background Tasks Infrastructure Service\n");
            Eq("net stop", t.T("net stop \"Print Spooler\""), "The Print Spooler service is stopping.\nThe Print Spooler service was stopped successfully.");
            Eq("net user add", t.T("net user dave Pa55w.rd! /add; (Get-LocalUser dave).Enabled"), "The command completed successfully.\n\nTrue");
            Has("net user details", t.T("net user dave"), "User name                    dave\n");
            Has("net user shows groups", t.Last, "Local Group Memberships      *Users");
            Eq("net localgroup add", t.T("net localgroup \"Remote Desktop Users\" dave /add; (Get-LocalGroupMember 'Remote Desktop Users').Name"), "The command completed successfully.\n\nWIN-SRV1\\dave");
            Has("tasklist header", t.T("tasklist"), "Image Name                     PID Session Name        Session#    Mem Usage\n========================= ======== ================ =========== ============\nSystem Idle Process              0 Services                   0          8 K\nSystem                           4 Services                   0        144 K");
            Has("tasklist /svc", t.T("tasklist /svc /fi \"imagename eq lsass.exe\""), "Image Name                     PID Services");
            Eq("tasklist /fo csv /nh", t.T("tasklist /fo csv /nh /fi \"pid eq 4\""), "\"System\",\"4\",\"Services\",\"0\",\"144 K\"");
            Eq("tasklist no match", t.T("tasklist /fi \"imagename eq zz.exe\""), "INFO: No tasks are running which match the specified criteria.");
            Eq("taskkill /im", t.T("Start-Process notepad; taskkill /im notepad.exe").StartsWith("SUCCESS: Sent termination signal to the process \"notepad.exe\" with PID ") ? "ok" : t.Last, "ok");
            Eq("taskkill /f /pid", t.T("$p = Start-Process notepad -PassThru; taskkill /f /pid $p.Id"), "SUCCESS: The process \"notepad.exe\" with PID " + t.T("$p.Id") + " has been terminated.");
            Eq("taskkill missing", t.Run("taskkill /im zz.exe; $LASTEXITCODE").Replace("\r", "").Contains("ERROR: The process \"zz.exe\" not found.") && t.Last.TrimEnd().EndsWith("128") ? "ok" : t.Last, "ok");
            Eq("schtasks /create", t.T("schtasks /create /tn LabJob /tr \"powershell.exe -Command exit 0\" /sc daily /st 02:00 /ru SYSTEM"), "SUCCESS: The scheduled task \"LabJob\" has successfully been created.");
            Eq("visible to Get-ScheduledTask", t.T("(Get-ScheduledTask LabJob).Triggers[0].StartBoundary"), "2026-03-02T02:00:00");
            Has("schtasks /query folder table", t.T("schtasks /query /tn LabJob"), "Folder: \\\nTaskName                                 Next Run Time          Status\n======================================== ====================== ===============\nLabJob                                   3/3/2026 2:00:00 AM    Ready");
            Eq("schtasks /run", t.T("schtasks /run /tn LabJob; (Get-ScheduledTaskInfo LabJob).LastTaskResult"), "SUCCESS: Attempted to run the scheduled task \"LabJob\".\n0");
            Eq("schtasks /change /disable", t.T("schtasks /change /tn LabJob /disable; (Get-ScheduledTask LabJob).State"), "SUCCESS: The parameters of scheduled task \"LabJob\" have been changed.\nDisabled");
            Eq("schtasks /delete /f", t.T("schtasks /delete /tn LabJob /f"), "SUCCESS: The scheduled task \"LabJob\" was successfully deleted.");
            Has("systeminfo", t.T("systeminfo"), "Host Name:                 WIN-SRV1\nOS Name:                   Microsoft Windows Server 2022 Datacenter\nOS Version:                10.0.20348 N/A Build 20348\n");
            Has("systeminfo hotfixes", t.Last, "Hotfix(s):                 4 Hotfix(s) Installed.");
            Has("systeminfo on the client", t.On("WIN-CL1", "systeminfo"), "OS Configuration:          Member Workstation");
            t.Dispose();
        }
    }
}
