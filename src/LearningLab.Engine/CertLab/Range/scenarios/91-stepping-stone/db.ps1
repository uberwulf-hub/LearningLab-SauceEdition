New-Item -ItemType Directory -Path 'C:\Data\Orders' -Force | Out-Null
Set-Content -Path 'C:\Data\Orders\orders-2026.csv' -Value 'order,customer,total'
New-Item -ItemType Directory -Path 'C:\ProgramData\{{SVC}}' -Force | Out-Null
Set-Content -Path 'C:\ProgramData\{{SVC}}\svc.ps1' -Value '# exports the orders for pickup (pretend)'
New-Service -Name '{{SVC}}' -BinaryPathName 'powershell.exe -WindowStyle Hidden -File C:\ProgramData\{{SVC}}\svc.ps1' -DisplayName '{{SVC_DISP}}' -StartupType Automatic | Out-Null
