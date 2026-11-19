# this box collects a copy of the PC's Security log as it is written ("log forwarding")
mkdir -p /var/log/forwarded
cat > /var/log/forwarded/{{LOWER:PC}}-security.log <<LOG
2026-03-01 08:01:12 {{PC}} 4624 An account was successfully logged on. Account Name: {{STAFF}} Source Network Address: 10.10.28.30 Logon Type: 2
2026-03-01 17:30:40 {{PC}} 4634 An account was logged off. Account Name: {{STAFF}}
2026-03-02 {{T1}} {{PC}} 4625 An account failed to log on. Account Name: {{VIC}} Source Network Address: {{ATKIP}}
2026-03-02 {{T2}} {{PC}} 4624 An account was successfully logged on. Account Name: {{VIC}} Source Network Address: {{ATKIP}} Logon Type: 10
2026-03-02 {{T3}} {{PC}} 4720 A user account was created. Subject: Account Name: {{VIC}} New Account: Account Name: {{ROGUE}}
2026-03-02 {{T3}} {{PC}} 4732 A member was added to a security-enabled local group. Subject: Account Name: {{VIC}} Member: {{ROGUE}} Group Name: Administrators
2026-03-02 {{T4}} {{PC}} 4688 A new process has been created. Account Name: {{VIC}} New Process Name: {{CLEARPROG}} Process Command Line: {{CLEARCMD}}
2026-03-02 {{T4}} {{PC}} 1102 The audit log was cleared. Account Name: {{VIC}}
LOG
