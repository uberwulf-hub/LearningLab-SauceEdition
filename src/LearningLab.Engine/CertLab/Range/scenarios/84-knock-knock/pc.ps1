New-Item -ItemType Directory -Path 'C:\Shares\Accounts' -Force | Out-Null
Set-Content -Path 'C:\Shares\Accounts\invoices-2026.csv' -Value 'number,customer,amount'
New-Item -ItemType Directory -Path 'C:\ProgramData\{{TASK}}' -Force | Out-Null
Set-Content -Path 'C:\ProgramData\{{TASK}}\{{SCRIPT}}' -Value @'
# zips the accounts share for pickup
New-Item -ItemType Directory -Path "$PSScriptRoot\out" -Force | Out-Null
Copy-Item C:\Shares\Accounts\* "$PSScriptRoot\out"
'@
$a = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument '-WindowStyle Hidden -ExecutionPolicy Bypass -File C:\ProgramData\{{TASK}}\{{SCRIPT}}'
$t = New-ScheduledTaskTrigger -Daily -At '02:30'
Register-ScheduledTask -TaskName '{{TASK}}' -Action $a -Trigger $t -Description '{{TASK_DESC}}' | Out-Null
