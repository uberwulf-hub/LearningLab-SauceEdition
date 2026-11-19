Set-Content -Path 'C:\Shares\Public\welcome.txt' -Value 'Welcome to the Pinecone shared drive. Keep it tidy!'
Set-Content -Path 'C:\Shares\Public\new-staff.txt' -Value @'
Welcome aboard!

Your sign-in for WIN-FS01:
  user: kim
  password: {{KIMPW}}

Please change it after your first sign-in. - IT
'@
Set-Content -Path 'C:\Users\kim\Desktop\flag.txt' -Value '{{FLAG:user}}'
Set-Content -Path 'C:\Users\kim\Documents\reset-local-admin.ps1' -Value @'
# IT helper: resets the local Administrator password. Keep private!
$pw = "{{ADMPW}}"
net user Administrator $pw
'@
Set-Content -Path 'C:\Users\Administrator\Desktop\flag.txt' -Value '{{FLAG:admin}}'
