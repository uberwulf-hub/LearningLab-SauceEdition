Set-Content -Path 'C:\Shares\{{SHARE}}\visitors.txt' -Value 'Visitor sign-in sheets go in this folder. Thank you!'
Set-Content -Path 'C:\Shares\{{SHARE}}\{{NOTE}}' -Value @'
Welcome to {{CO}}, {{STAFF_FIRST}}!

You will be on the front desk. Your PC is {{HOST}}.
  user: {{STAFF}}
  password: {{STAFFPW}}

Please change your password on the first day. - IT
'@
{{if WHERE=autologon}}
$p = 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon'
New-Item -Path $p -Force | Out-Null
Set-ItemProperty -Path $p -Name AutoAdminLogon -Value '1'
Set-ItemProperty -Path $p -Name DefaultUserName -Value '{{ADMUSER}}'
Set-ItemProperty -Path $p -Name DefaultPassword -Value '{{ADMPW}}'
{{end}}
{{if WHERE=history}}
$h = 'C:\Users\{{STAFF}}\AppData\Roaming\Microsoft\Windows\PowerShell\PSReadLine\ConsoleHost_history.txt'
New-Item -ItemType Directory -Path (Split-Path $h) -Force | Out-Null
Set-Content -Path $h -Value @('Get-Printer', 'Restart-Service Spooler', 'net user {{ADMUSER}} {{ADMPW}}', 'Add-LocalGroupMember -Group Administrators -Member {{ADMUSER}}', 'Clear-Host')
{{end}}
{{if WHERE=unattend}}
New-Item -ItemType Directory -Path 'C:\Windows\Panther' -Force | Out-Null
Set-Content -Path 'C:\Windows\Panther\unattend.xml' -Value @'
<?xml version="1.0" encoding="utf-8"?>
<unattend xmlns="urn:schemas-microsoft-com:unattend">
  <settings pass="oobeSystem">
    <component name="Microsoft-Windows-Shell-Setup">
      <UserAccounts>
        <LocalAccounts>
          <LocalAccount>
            <Name>{{ADMUSER}}</Name>
            <Group>Administrators</Group>
            <Password>
              <Value>{{ADMPW}}</Value>
              <PlainText>true</PlainText>
            </Password>
          </LocalAccount>
        </LocalAccounts>
      </UserAccounts>
    </component>
  </settings>
</unattend>
'@
{{end}}
