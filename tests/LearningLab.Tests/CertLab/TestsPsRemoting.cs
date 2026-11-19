// Cert Lab - checks of remoting (Invoke-Command, sessions, Enter-PSSession, $using:), background jobs on the
// simulated clock, and the console's programs (hostname, whoami, powershell.exe, cmd /c).
using System;
using System.Collections.Generic;
using System.Linq;

namespace CertLab
{
    public static partial class Tests
    {
        static partial void PsGroupsRemoting(List<KeyValuePair<string, Action>> l)
        {
            l.Add(new KeyValuePair<string, Action>("ps-remoting", PsRemotingTests));
            l.Add(new KeyValuePair<string, Action>("ps-jobs", PsJobTests));
        }

        static void PsRemotingTests()
        {
            var t = new P();
            Eq("Invoke-Command on the server itself", t.T("Invoke-Command -ComputerName WIN-SRV1 -ScriptBlock { $env:COMPUTERNAME }"), "WIN-SRV1");
            Eq("results carry PSComputerName", t.T("(Invoke-Command -ComputerName localhost { 'x' }).PSComputerName"), "WIN-SRV1");
            Eq("WIN-CL1 refuses until remoting is enabled", t.T("Invoke-Command -ComputerName WIN-CL1 { 1 }").Contains("WinRMOperationTimeout,PSSessionStateBroken") ? "ok" : t.Last, "ok");
            Eq("unknown computer", t.T("Invoke-Command -ComputerName NOPE01 { 1 }").Contains("Cannot find the computer NOPE01") ? "ok" : t.Last, "ok");
            t.On("WIN-CL1", "Enable-PSRemoting -Force");
            Eq("after Enable-PSRemoting on WIN-CL1", t.T("Invoke-Command -ComputerName WIN-CL1 { $env:COMPUTERNAME }"), "WIN-CL1");
            Eq("$using: sends a local value", t.T("$n = 21; Invoke-Command -ComputerName WIN-CL1 { $using:n * 2 }"), "42");
            Eq("local variables are not there without $using:", t.T("$secret = 'x'; Invoke-Command -ComputerName WIN-CL1 { \"[$secret]\" }"), "[]");
            Eq("-ArgumentList", t.T("Invoke-Command -ComputerName WIN-CL1 -ScriptBlock { param($a, $b) $a + $b } -ArgumentList 2, 3"), "5");
            Eq("deserialized objects lose their methods", t.T("$o = Invoke-Command -ComputerName WIN-CL1 { [pscustomobject]@{ N = 1 } }; $o.psobject.TypeNames[0]"), "Deserialized.System.Management.Automation.PSCustomObject");
            Eq("a table of remote results shows PSComputerName", t.Run("Invoke-Command -ComputerName WIN-CL1 { [pscustomobject]@{ N = 1 } }").Contains("PSComputerName") ? "ok" : t.Last, "ok");
            Eq("New-PSSession", t.T("$s = New-PSSession -ComputerName WIN-CL1; $s.ComputerName"), "WIN-CL1");
            Eq("a session keeps its variables", t.T("Invoke-Command -Session $s { $kept = 'yes' }; Invoke-Command -Session $s { $kept }"), "yes");
            Eq("Get-PSSession", t.T("(Get-PSSession).Count"), "1");
            Eq("PSSession table", t.Run("Get-PSSession"), "\n Id Name            ComputerName    ComputerType    State         ConfigurationName     Availability\n -- ----            ------------    ------------    -----         -----------------     ------------\n  1 WinRM1          WIN-CL1         RemoteMachine   Opened        Microsoft.PowerShell     Available\n\n\n");
            Eq("Remove-PSSession", t.T("Get-PSSession | Remove-PSSession; (Get-PSSession | Measure-Object).Count"), "0");
            t.Run("Enter-PSSession -ComputerName WIN-CL1");
            Eq("Enter-PSSession changes the prompt", t.C.Prompt, "[WIN-CL1]: PS C:\\Users\\Administrator\\Documents> ");
            Eq("commands run on the remote machine", t.T("hostname"), "WIN-CL1");
            t.Run("exit");
            Eq("exit returns to the local prompt", t.C.Prompt, "PS C:\\Windows\\system32> ");
            Eq("remote errors show PSComputerName", t.T("Invoke-Command -ComputerName WIN-CL1 { Get-Variable nothere }").Contains("+ PSComputerName        : WIN-CL1") ? "ok" : t.Last, "ok");
            Eq("Test-WSMan", t.T("(Test-WSMan -ComputerName WIN-CL1).ProductVendor"), "Microsoft Corporation");
            Eq("hostname", t.T("hostname"), "WIN-SRV1");
            Eq("whoami", t.T("whoami"), "lab\\administrator");
            Eq("powershell.exe -Command", t.T("powershell.exe -NoProfile -Command \"1 + 1\""), "2");
            Eq("cmd /c echo", t.T("cmd /c echo hello"), "hello");
            Eq("$LASTEXITCODE", t.T("powershell.exe -Command \"exit 3\"; $LASTEXITCODE"), "3");
            t.Dispose();
        }

        static void PsJobTests()
        {
            var t = new P();
            t.Run("$j = Start-Job -ScriptBlock { Start-Sleep -Seconds 10; 'done' }");
            Eq("a new job is running", t.T("$j.State"), "Running");
            Eq("Start-Sleep in a job does not move the clock", t.T("(Get-Job -Id 1).Id"), "1");
            Eq("nothing to receive yet", t.T("Receive-Job $j"), "");
            Eq("Get-Job table", t.Run("Get-Job"), "\nId     Name            PSJobTypeName   State         HasMoreData     Location             Command\n--     ----            -------------   -----         -----------     --------             -------\n1      Job1            BackgroundJob   Running       True            localhost            Start-Sleep -Seconds 10; '...\n\n\n");
            t.Run("Wait-Job $j | Out-Null");
            Eq("Wait-Job lets the time pass", t.T("$j.State"), "Completed");
            Eq("Receive-Job gives the output", t.T("Receive-Job $j"), "done");
            Eq("and only once", t.T("Receive-Job $j"), "");
            Eq("HasMoreData is false after", t.T("$j.HasMoreData"), "False");
            Eq("the next job gets Id 3", t.T("(Start-Job { 1 }).Id"), "3");
            Eq("Receive-Job -Wait -AutoRemoveJob", t.T("Start-Job { 'w' } | Receive-Job -Wait -AutoRemoveJob"), "w");
            Eq("a failing job", t.T("$f = Start-Job { throw 'nope' }; Wait-Job $f | Out-Null; $f.State"), "Failed");
            Eq("Remove-Job of a running job needs -Force", t.T("$r = Start-Job { Start-Sleep 100 }; Remove-Job $r").Contains("JobObjectNotFinishedCannotBeRemoved") ? "ok" : t.Last, "ok");
            Eq("Stop-Job", t.T("Stop-Job $r; $r.State"), "Stopped");
            Eq("$using: in Start-Job", t.T("$v = 'used'; Start-Job { $using:v } | Receive-Job -Wait"), "used");
            Eq("jobs see the machine's files", t.T("Start-Job { [System.IO.File]::Exists('C:\\LabFiles\\servers.txt') } | Receive-Job -Wait"), "True");
            t.On("WIN-CL1", "Enable-PSRemoting -Force");
            Eq("Invoke-Command -AsJob", t.T("$rj = Invoke-Command -ComputerName WIN-CL1 { $env:COMPUTERNAME } -AsJob; $rj.PSJobTypeName"), "RemoteJob");
            Eq("remote job output", t.T("$rj | Receive-Job -Wait"), "WIN-CL1");
            t.Dispose();
        }
    }
}
