New-Item -ItemType Directory -Path 'C:\Finance' -Force | Out-Null
Set-Content -Path 'C:\Finance\budget.xlsx.txt' -Value 'Budget 2026 (pretend)'
New-Item -ItemType Directory -Path 'C:\Users\{{USER}}\AppData\Local\{{HIDEWIN}}' -Force | Out-Null
Set-Content -Path 'C:\Users\{{USER}}\AppData\Local\{{HIDEWIN}}\pull.ps1' -Value 'Copy-Item C:\Finance\* $PSScriptRoot'
$a = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument '-WindowStyle Hidden -File C:\Users\{{USER}}\AppData\Local\{{HIDEWIN}}\pull.ps1'
$t = New-ScheduledTaskTrigger -Daily -At '03:15'
Register-ScheduledTask -TaskName '{{TASK}}' -Action $a -Trigger $t | Out-Null
