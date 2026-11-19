New-LocalUser -Name '{{ROGUE}}' -Password (ConvertTo-SecureString '{{ROGUEPW}}' -AsPlainText -Force) -Description 'Service account' | Out-Null
Add-LocalGroupMember -Group Administrators -Member '{{ROGUE}}'
New-Item -ItemType Directory -Path 'C:\Shares\Plans' -Force | Out-Null
Set-Content -Path 'C:\Shares\Plans\floorplan.txt' -Value 'New shop floor plan'
$su = 'C:\ProgramData\Microsoft\Windows\Start Menu\Programs\StartUp'
New-Item -ItemType Directory -Path $su -Force | Out-Null
Set-Content -Path "$su\{{START}}" -Value @'
@echo off
rem starts for everyone who signs in: copies the plans share for pickup
xcopy C:\Shares\Plans C:\Users\Public\Libraries\cache /e /y
'@
