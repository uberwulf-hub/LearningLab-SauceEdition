New-Item -ItemType Directory -Path 'C:\Payroll' -Force | Out-Null
Set-Content -Path 'C:\Payroll\march.csv' -Value 'name,amount'
New-LocalUser -Name '{{ROGUE}}' -FullName '{{ROGUE_FULL}}' -Description '{{ROGUE_DESC}}' -Password (ConvertTo-SecureString '{{ROGUEPW}}' -AsPlainText -Force) | Out-Null
Add-LocalGroupMember -Group Administrators -Member '{{ROGUE}}'
New-Item -ItemType Directory -Path 'C:\Users\{{ROGUE}}\Desktop' -Force | Out-Null
