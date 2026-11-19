New-Item -ItemType Directory -Path 'C:\Shares\Front' -Force | Out-Null
Set-Content -Path 'C:\Shares\Front\visitors.csv' -Value 'name,company,date'
Set-Content -Path 'C:\Shares\Front\door-codes.txt' -Value 'Side door: changes monthly - ask the office manager.'
New-Item -ItemType Directory -Path 'C:\ProgramData\{{VAL}}' -Force | Out-Null
Set-Content -Path 'C:\ProgramData\{{VAL}}\run.ps1' -Value @'
# copies the front desk share for pickup
New-Item -ItemType Directory -Path "$PSScriptRoot\out" -Force | Out-Null
Copy-Item C:\Shares\Front\* "$PSScriptRoot\out"
'@
$run = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run'
New-Item -Path $run -Force | Out-Null
Set-ItemProperty -Path $run -Name 'SecurityHealth' -Value '%windir%\system32\SecurityHealthSystray.exe'
Set-ItemProperty -Path $run -Name '{{VAL}}' -Value 'powershell.exe -WindowStyle Hidden -ExecutionPolicy Bypass -File C:\ProgramData\{{VAL}}\run.ps1'
