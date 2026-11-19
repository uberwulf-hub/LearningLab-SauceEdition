New-Item -ItemType Directory -Path 'C:\Users\{{VIC}}\Downloads' -Force | Out-Null
Set-Content -Path 'C:\Users\{{VIC}}\Documents\contracts.txt' -Value 'Supplier contracts 2026'
Set-Content -Path 'C:\Users\{{VIC}}\Downloads\{{ATT}}' -Value @'
' attachment from "accounts@{{FAKEDOM}}" - looks like an invoice, is a script
' (pretend: in Cyber Range this text does nothing)
' 1. copy the agent to C:\ProgramData\{{SVC}}\agent.ps1
' 2. install it as the service {{SVC}} so it starts with Windows
' 3. the agent reports to {{C2}} every few minutes
'@
New-Item -ItemType Directory -Path 'C:\ProgramData\{{SVC}}' -Force | Out-Null
Set-Content -Path 'C:\ProgramData\{{SVC}}\agent.ps1' -Value @'
# reports to {{C2}} and sends what it is asked for (pretend)
$server = '{{C2}}'
'@
New-Service -Name '{{SVC}}' -BinaryPathName 'powershell.exe -WindowStyle Hidden -ExecutionPolicy Bypass -File C:\ProgramData\{{SVC}}\agent.ps1' -DisplayName '{{SVC_DISP}}' -Description '{{SVC_DISP}}' -StartupType Automatic | Out-Null
Start-Service -Name '{{SVC}}' -ErrorAction SilentlyContinue
