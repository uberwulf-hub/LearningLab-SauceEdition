$share = 'C:\Shares\Team'
New-Item -ItemType Directory -Path $share -Force | Out-Null
# the files were renamed (only renamed: in Cyber Range nothing is scrambled), plus a note
foreach ($f in 'rota.txt', 'minutes-march.txt', 'price-list.txt', 'holiday-plan.txt') { Set-Content -Path "$share\$f.{{EXT}}" -Value "($f - still readable, only renamed)" }
Set-Content -Path "$share\new-staff.txt" -Value 'Welcome pack (not renamed yet)'
Set-Content -Path "$share\{{NOTE}}" -Value @'
Your team files have been renamed and you can't open them.
To get them back, write to unlock@{{FAKEDOM}} within 3 days.
'@
New-Item -ItemType Directory -Path 'C:\ProgramData\{{TASK}}' -Force | Out-Null
Set-Content -Path 'C:\ProgramData\{{TASK}}\run.ps1' -Value @'
# renames every file in the team share that isn't renamed yet (pretend)
Get-ChildItem C:\Shares\Team -File | Where-Object { $_.Extension -ne '.{{EXT}}' -and $_.Name -ne '{{NOTE}}' } | ForEach-Object { Rename-Item $_.FullName ($_.Name + '.{{EXT}}') }
'@
$a = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument '-WindowStyle Hidden -File C:\ProgramData\{{TASK}}\run.ps1'
$t = New-ScheduledTaskTrigger -Daily -At '00:30'
Register-ScheduledTask -TaskName '{{TASK}}' -Action $a -Trigger $t | Out-Null
