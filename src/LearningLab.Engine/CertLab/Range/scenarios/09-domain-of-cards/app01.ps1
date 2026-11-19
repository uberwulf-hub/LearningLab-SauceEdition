Set-Content -Path 'C:\Shares\Share\welcome.txt' -Value 'Castoak Supply company share. Be tidy.'
Set-Content -Path 'C:\Shares\Share\onboarding.txt' -Value @'
New starter sign-in (domain CASTOAK)
------------------------------------
  user: jwade
  password: {{JPW}}

Please change it after your first sign-in.
'@
Set-Content -Path 'C:\Users\jwade\Desktop\flag.txt' -Value '{{FLAG:user}}'
