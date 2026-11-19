# Cert Lab course files

A course is a folder under `Courses\` with a `course.json` and text files. Everything is plain UTF-8 text (write
ASCII where you can). `CertLab.ps1 -Test` loads every file and checks it (see "Tests" at the end).

## course.json

```json
{
  "id": "rhcsa", "title": "...", "short": "RHCSA", "exam": "EX200", "simulator": "rhel",
  "machines": 2, "examMinutes": 180, "examPoints": 300, "passPercent": 70,
  "description": "...", "disclaimer": "...",
  "objectives": ["Essential tools", "Shell scripts", "..."],
  "units": [ { "id": "u01", "title": "1. Essential tools", "objective": "Understand and use essential tools", "file": "lessons/01-essential-tools.txt" } ]
}
```

`objectives` are the short names every deck, sheet and exam task uses. `simulator` picks the simulator behind the
course (`rhel` today). A later course (PowerShell, Windows administration, Security+) brings its own folder,
objectives and simulator; exam items can already be multiple choice (`@choice`).

## Lessons (`lessons\NN-name.txt`, one unit per file)

```
# Unit: u05 | 5. Local storage | Configure local storage

Intro paragraph shown at the top of the unit.

## Lesson: l0501 | Partitions with fdisk and parted

A paragraph. `inline code` is shown in the code font. Blank lines end paragraphs.

### A heading inside the lesson

- a bullet
- another bullet, which may continue
  on an indented line

> A note (tip or warning) in a box.

```terminal
[root@server1 ~]# lsblk /dev/vdb
NAME MAJ:MIN RM SIZE RO TYPE MOUNTPOINTS
vdb  252:16   0   5G  0 disk
```

@try machines=1
(a small task set: same syntax as labs, below; it opens the simulator with a task panel)
@end

@quiz
? The question
- a wrong answer
+ the right answer
- another wrong answer
! Why the right answer is right (shown after answering).
@end
```

Terminal examples must be what the simulator really prints for that command (run it in the simulator and copy
the output). Lines that start with a prompt `[user@host dir]# ` or `$ ` are commands; the test checks that every
command they use exists in the simulator.

## Flash cards (`cards\NAME.txt`)

```
# Deck: storage | Local storage | Local storage
Front of the card :: Back of the card with `the command` in backticks
```

The third field is the objective's short name. A card whose back has a command in backticks can be studied in
"type the answer" mode: the typed command must match it (extra spaces are ignored).

## Cheat sheets (`cheats\NAME.txt`)

```
# Sheet: storage | Local storage | Local storage
## Partitions
lsblk -f :: disks, partitions, file systems and where they are mounted
parted -s /dev/vdb mklabel gpt :: a new GPT partition table
```

## Labs (`labs\NAME.txt`) and practice exams (`exams\NAME.txt`)

```
# Lab: lab-05 | Partitions, LVM and swap | u05 | 40
# Exam: exam-1 | Practice exam 1 | 180
Intro text shown above the tasks.

@vars
vg = vgdata | vgapp | vgstore
size = 300-600
pw = Fl0wer7 | Tr0mbone5
@endvars

@nologin server2

@setup server2
echo 'Unkn0wn#{size}' | passwd --stdin root

@task t01 | Local storage | 20 | server1
The task as the person reads it. {vg} is replaced by the picked value.
@check server1 | what the check proves, in plain words
vgs {vg}
@check server1 | another check (all checks must pass for the points)
test "$(vgs --noheadings -o vg_extent_size {vg} | tr -d ' ')" = "16.00m"
@hint A hint (labs show hints; exams do not).
@solution server1
pvcreate /dev/vdb1
vgcreate -s 16M {vg} /dev/vdb1
@explain
Why this works, what the person should remember, the trap the task sets.
@endtask
```

- `@vars`: `name = a | b | c` picks one value per attempt; `name = 100-200` picks a number. A later variable may use
  an earlier one (`{name}`). Exams use them so every attempt is a little different.
- `@setup MACHINE`: shell lines run as root before the person starts (consecutive lines for the same machine run
  as one script). Setup output is not shown.
- `@nologin MACHINE`: that machine's console starts at the login prompt (for example when a setup line changed
  root's password and a task asks to reset it).
- `@task id | objective | points | machine`: the objective is one of course.json's short names. Exams must total
  the course's `examPoints` (300 for RHCSA).
- `@check MACHINE | description`: a shell script run as root on that machine after the work (for exams after the
  graders reboot both machines). Exit status 0 means passed. `PATH` includes `/usr/libexec/certlab`, which has
  `certlab-check`: `certlab-check password USER PASS`, `certlab-check booted`, `certlab-check login USER 'cmd'`
  (runs a command in the user's login shell), `certlab-check sshkey FROMUSER USER@HOST` (key login without a
  password), `certlab-check umask USER`.
- `@solution MACHINE`: what a person types, line by line, into that machine's console. Answers to prompts are
  just the next line. Special lines: `#reset` (press the machine's reset button), `#key K [K...]` (press keys,
  as on the real GRUB screen: `e`, `c`, `Up`, `Down`, `Left`, `Right`, `Home`, `End`, `Enter`, `Esc`, `Backspace`,
  `Delete`, `F2`, `F10`, `Ctrl+A`...`Ctrl+Z`; outside GRUB `Ctrl+C`, `Ctrl+D`, `Enter`), `#password X` (the root
  password is now X for logging in again), `#wait N` (N seconds pass on the simulated clock), `#type TEXT` (type
  TEXT where a line starting with # would be taken as a directive; on the GRUB screen it types TEXT, everything
  after `#type `, spaces included, without pressing Enter). The root password reset: `#reset`, `#key e`,
  `#key Down Down Down End`, `#type  rd.break` (two spaces: the second one is typed), `#key Ctrl+X`. A line that
  is not a directive, typed while the GRUB menu shows, first presses Enter there (the default entry boots).
- `@choice id | objective | points` (multiple choice, for later courses): the question text, then `- wrong`,
  `+ right`, `! explanation` lines.

## Manual pages (`man\NAME.SECTION`)

Plain text, written for Cert Lab, in the usual layout (NAME, SYNOPSIS, DESCRIPTION, OPTIONS, EXAMPLES, SEE ALSO,
section headings in capitals, text indented by 7 spaces). The NAME line is `name - what it does`; `man -k`
searches it, and `PROGRAM --help` shows SYNOPSIS and OPTIONS. Describe only options the simulator supports, and
describe them correctly for RHEL 9.

## Tests

`CertLab.ps1 -Test` checks that every file parses; every command used by a card, sheet, example or solution exists
in the simulator and has a manual page; every lesson's try-it, lab and exam task fails before the work and passes
once the model solution is typed in (exams: from a score of 0 to 100%, after the graders' reboot, with two
different seeds). `-Only labs:lab-05`, `-Only exams:exam-1`, `-Only tries:l05` run one file.

## The PowerShell course (`simulator: powershell`)

The same files, with these differences:

- **Machines**: `WIN-SRV1` (Windows Server 2022) and `WIN-CL1` (Windows 11). Every `@task`, `@check`, `@setup` and
  `@solution` names one of them. The consoles start as the administrator in `C:\Windows\system32`.
- **Setup and checks are PowerShell**, run as the administrator on that machine. A check passes when it ends without
  an error and its last output is true (`(Get-Service Spooler).Status -eq 'Stopped'`; `Test-Path C:\Reports\a.csv`;
  `(& C:\Scripts\Get-Report.ps1 | Out-String).Trim() -eq 'expected'`). Checks of try-its and labs see the console's
  session (its variables and functions) plus `$CertLabTranscript` (everything the console showed, as one string) and
  `$CertLabHistory` (the lines typed). Exams restart both machines first, so their checks start a new session: tasks
  must leave something that survives (files, registry, services, tasks, users, profiles, modules).
- **Solutions** are typed line by line into the console; a multi-line statement (a function, a here-string) is typed
  one line after the other as a person would. `#wait N` lets N seconds pass on the machines' clock.
- **Questions** in exams: `@choice id | objective | points`, the question text (use `` `code` `` for code; a code
  block can be written as indented lines), `- wrong`, `+ right`, `! explanation`. "What does this print?" questions
  must be answered by what the simulator prints. Exams total the course's `examPoints` (100), mix tasks on both
  machines with questions, and cover every objective.
- **Lesson examples** (` ```terminal ` blocks) are checked against the simulator: each block is typed into a fresh
  WIN-SRV1 console at 120 columns, and the text must be exactly what the console shows (prompt lines
  `PS C:\Windows\system32> command`, continuation lines `>> ...`, then the output; trailing spaces and the final
  blank lines do not matter). A line `#setup COMMAND` (not shown to the learner) runs a setup command first;
  `#setup WIN-CL1:COMMAND` runs it on WIN-CL1; `#machine WIN-CL1` types the example on WIN-CL1.
- **Help pages** (`help\NAME.txt`, `help\about_TOPIC.txt`) replace manual pages; the format is at the top of
  `Engine\PsHelp.cs`. Every cmdlet the simulator has, and every command the course uses, needs one.
- Tests: `CertLab.ps1 -Test -Only ps` runs everything for this course; `-Only ps-tries:l0302`, `-Only ps-labs:lab-03`,
  `-Only ps-exams:exam-1`, `-Only ps-content:l03` (the examples of unit 3's lessons) run one file.

## The MD-102 course (`simulator: endpoint`)

The same files again, with a pretend tenant (Riverbend Outfitters, riverbend.example) instead of consoles:

- **Machines**: `portal` is the practice admin portal. The Windows devices are `RB-LT-001`, `RB-LT-002`, `RB-LT-003`
  (still in the out-of-box experience), `RB-DT-010` (hybrid joined), `RB-DT-007` (TPM 1.2), `RB-KIOSK-01` and
  `SAM-HOME-PC` (personal, Entra registered); each has the simulated PowerShell console. The phones `Jordan's iPhone`
  and `Priya's Pixel` are in the tenant only (name them in quotes in portal lines). People: alex (you), chen, jordan,
  priya, sam, nils, ava, breakglass. Groups: IT Admins, Sales, Finance, Intune pilot users, Pilot devices, Kiosk
  devices, Windows devices (dynamic), Corporate Windows devices (dynamic). Everything else a lab needs, its setup
  creates.
- **Portal lines** (`@setup portal`, `@solution portal`): one portal action per line, the same thing the person
  clicks (the form validates both the same way):
  `create KIND key=value ...` (the keys are the field keys in `Engine\EpSchemaDefs*.cs`; settings catalog settings
  are `s:Area/Name=Value`, attack surface reduction rules `asr:key=Mode`), `set KIND "Name" key=value` (`+key=v` adds
  to a list, `-key=v` removes), `assign KIND "Name" "Group" intent=include|exclude|required|available|uninstall
  filter="F" mode=include|exclude`, `unassign KIND "Name" "Group"`, `delete KIND "Name"`, `members add|remove "Group"
  member ...`, `remote ACTION "Device" [argument]` (sync, restart, retire, wipe, autopilot-reset, fresh-start, rename,
  rotate-bitlocker, rotate-laps, quick-scan, full-scan, update-defender, collect-diagnostics, remote-lock, delete),
  `import autopilot "Device" "C:\HWID\file.csv" [groupTag=X]`, `tenant key=value` (tenant settings: mdmScope,
  joinScope, lapsEnabled, noPolicyMark, mdeConnector, windowsData, adConnector, enrollLimit, winPersonal...). Kinds:
  user, group, location, filter, scopeTag, roleAssignment, autopilot, autopilotProfile, esp, restriction, config,
  compliance, ring, featureUpdate, qualityUpdate, driverUpdate, script, remediation, app, appProtection, appConfig,
  av, wsx, diskEncryption, firewall, firewallRules, asr, laps, edr, baseline, ca.
  Setup only: `device NAME key=value` puts a device into a starting state (bitlocker=On|Off, tpm=2.0|1.2|None,
  riskAfterOnboard=Medium, sigAgeDays=9, osVersion=..., patch=2026-01, compliance=..., lastSyncDaysAgo=40,
  userIsAdmin=Yes, jailbroken=Yes, rooted=Yes, state=oobe...).
- **Device lines** are PowerShell typed into that device's console, or these directives: `#settings join USER`
  (Settings > Accounts > Access work or school > Join this device to Microsoft Entra ID), `#settings register USER`
  (add a work account), `#settings enroll USER` (Enroll only in device management), `#settings disconnect`,
  `#settings sync`, `#oobe [USER]` (set the device up: Autopilot if a profile is assigned), `#portal install "App"`
  (Company Portal), `#laps use` (someone signs in with the LAPS password). `#wait N` (any machine) moves the clock on.
- **Time**: policies reach a device at its next check-in (about every 8 hours). Solutions use `remote sync "Device"`
  (or `#settings sync` on the device) after assigning; BitLocker takes 2 hours to encrypt (`#wait 7200`, then sync
  again so compliance sees it); Autopilot profile assignment takes 15 minutes (`#wait 900`). The clock starts on
  Monday 2 March 2026 09:00; monthly updates come out on the second Tuesday.
- **Checks** on `portal` are assertions on the tenant, one per line, all must hold:
  `compliance["Name"].bitlocker == Require`, `compliance["Name"].include has "Sales"`, `device["RB-LT-001"].compliance
  == Compliant`, `device["RB-LT-001"].app["7-Zip"] == Installed`, `device["RB-LT-001"].setting["Camera/AllowCamera"]
  == "Not allowed"`, `device["RB-LT-001"].policy["Name"] == Succeeded`, `group["Name"].members has RB-LT-001`,
  `signin(user=jordan, device=RB-LT-001, app="Office 365", mfa=yes, client=browser|mobile|eas,
  clientApp="Microsoft Outlook", location="Head office") == Blocked|Allowed|"Allowed after MFA"`,
  `count(compliance) >= 1`, `canmanage(chen, RB-LT-001) == yes`, `tenant.lapsEnabled == Yes`, `not config["Old"]`.
  Operators: `==`, `!=`, `has`, `!has`, `like` (`*` wildcards), `contains`, `>=`, `<=`, `>`, `<`; a line with no
  operator holds when the value exists. Property names are the schema keys plus `include`, `exclude`, `required`,
  `available`, `uninstall`, `assigned`, `intent["Group"]`, `filter["Group"]`, `filtermode["Group"]`, `members`,
  `groups`, and for devices `compliance`, `reasons`, `app[...]`, `policy[...]`, `setting[...]`, `bitlocker`,
  `recoveryKeyIn`, `laps`, `laps.password`, `ring`, `patch`, `featureVersion`, `osVersion`, `join`, `managed`,
  `ownership`, `user`, `enrollmentProfile`, `mde`, `risk`. Checks on a device are PowerShell, as in the PowerShell
  course.
- **Exam items** (Microsoft's kinds): `@choice id | objective | points [| case=ID]` (one answer: `- wrong`, `+ right`),
  `@multi` (several `+` lines; all or nothing), `@order` (lines `1 first action`, `2 ...` in the right order and
  `x distractor`; the person drags them into the answer area; all or nothing), `@yesno` (lines `Y statement` /
  `N statement`; each statement is worth its share of the points), `! explanation` on all of them, and case studies:
  `@case ID | Title`, sections starting with `## Heading`, `@endcase`; questions with `case=ID` follow it together.
  Exam scores are shown from 1 to 1000 (`examPoints: 1000`, `scoreMin: 1`) with 700 to pass.
- **Lesson examples** (` ```terminal ` blocks) are typed into a fresh device (`RB-LT-001` unless `#machine NAME`);
  `#setup portal:LINE`, `#setup DEVICE:POWERSHELL` and `#setup wait:SECONDS` prepare the tenant first. Fill them with
  `[CertLab.Tests]::EpFillExamples(text)` rather than typing the output.
- One `@try` per lesson (a second one replaces the first): put several `@task` blocks in it instead.
- Tests: `-Only ep` (engine and content), `-Only ep-tries:l0301`, `-Only ep-labs:lab-03`, `-Only ep-exams:exam-1`,
  `-Only ep-content`.

## The AZ-800 course (`simulator: winserver`)

The same files, on a pretend Windows Server network (Harborline Logistics, domain `corp.example`, NetBIOS `CORP`):

- **Machines**: `DC1` (10.10.0.10) and `DC2` (10.20.0.11) are domain controllers and DNS servers (all five FSMO roles
  on DC1; both in the site `HQ` at the start; only `DEFAULTIPSITELINK`, 180 minutes, with HQ in it), `FS1` (file
  server: D: with the shares Sales, Finance, HR, Public, IT under `D:\Shares`; E: an archive full of copies; disks 3-6
  empty for Storage Spaces), `HV1` (Hyper-V: switch `LAN` on "Ethernet 2", VMs `APP-OLD` running, `WEB-TEST`,
  `LAB-HOST` with dynamic memory; Containers and a pretend Docker engine), `CORE1` (Server Core, nothing installed),
  `SRV1` (member server: DHCP with scope `10.10.0.0` "HQ clients" 10.10.0.100-199, authorized; Windows Admin Center;
  RSAT for AD, GP and DHCP), `CL1` (Windows 11, a DHCP client in `OU=Workstations`). `admin` is the practice consoles.
  People (OU `Staff/<dept>`): arivera (IT, Domain Admins), jprice and hbrandt (Helpdesk), bcole, dpatel, imoreau (Sales;
  imoreau also Sales Managers), ewalsh, fhaddad (Finance), gkim, knovak (HR); svc-backup in Users. Groups (OU
  `Groups`): Sales, Finance, HR, IT Admins, Helpdesk, Sales Managers (global); FS-Sales-Modify, FS-Finance-Modify,
  FS-Public-Read (domain local, AGDLP). GPOs: Default Domain Policy, Default Domain Controllers Policy, Workstation
  baseline (Workstations), Staff desktop (Staff). The password of every seeded account is `Harbor#2026`.
  Outside: the ISP's DNS 203.0.113.53, the partner forest `tidewater.example` (DNS 192.168.50.10, reachable over a VPN),
  the pretend Azure subscription (VNet 10.50.0.0/16, VMs `AZ-APP1` and `AZ-DC3`, storage account `stharborfiles` with
  file shares `salesarchive` and `hrarchive`), the tenant `harborline.onmicrosoft.example` (verified domain
  `harborline.example`).
- **Lines for a machine** are Windows PowerShell typed into its console as `CORP\Administrator` (setups: consecutive lines
  of one machine run as one script). Directives: `#wait N` (the clock moves N seconds), `#power off`, `#power on`,
  `#reboot`, `#renew` (a DHCP client asks for its lease again). The machines have the modules they would have:
  ActiveDirectory and GroupPolicy (DC1, DC2, SRV1), ADDSDeployment (where AD-Domain-Services is installed), DnsServer
  (DCs), DhcpServer (SRV1, and CORE1 once DHCP is installed), Hyper-V (HV1), SmbShare / Storage everywhere,
  Deduplication, DFSN, DFSR once their features are installed. Programs: gpupdate, gpresult, repadmin, dcdiag, nltest,
  netdom, icacls, docker (HV1), sconfig, azcmagent (after Arc onboarding).
- **Lines for `admin`** are what a click in a practice console does (the consoles build the same lines):
  - Users and Computers: `new ou|user|group|computer name="X" path=Staff/Sales [sam= upn= password= enabled=yes
    department= title= scope=Global|DomainLocal|Universal category= protect=yes|no]`, `set user|group|computer|ou "X"
    key=value` (attributes, `enabled=`, `protect=`, `upn=`, `scope=`), `move user|group|computer|ou|object "X" "PATH"`,
    `rename ...`, `delete ... [recursive=yes]`, `restore user "X"` (Recycle Bin), `members add|remove "Group" m1 m2`,
    `password USER "NewPw" [mustchange=yes]`, `unlock USER`. Paths are a DN or `Staff/Sales` (under the domain).
  - Sites and Services / forest: `new site name=Branch [link=DEFAULTIPSITELINK]` (the GUI makes you pick a link;
    New-ADReplicationSite doesn't), `new subnet name=10.20.0.0/24 site=Branch`, `new sitelink name=X sites=HQ,Branch
    cost=100 interval=15`, `set sitelink "X" interval=15 cost=50 +sites=Branch -sites=Lab`, `move dc DC2 Branch`,
    `replicate DC2 from=DC1` (Replicate Now), `replicate all` (repadmin /syncall), `fsmo transfer|seize ROLE DC`,
    `enable recyclebin`, `upnsuffix add|remove harborline.example`, `trust create tidewater.example type=Forest
    direction=Bidirectional auth=Selective|Forest`.
  - Group Policy Management: `gpo new "Name" [comment=]`, `gpo delete|rename|copy|backup|restore "Name" ...`,
    `gpo link|setlink|unlink "Name" "TARGET" [enabled=no enforced=yes order=1]` (TARGET: a DN, `Staff/Sales`, or a
    site name), `gpo set "Name" setting=ID state=Enabled|Disabled|"Not configured" [value=...]` (IDs in
    `Engine\WsGpoCatalog.cs`: LockScreenCamera, Loopback (Merge/Replace), RefreshInterval, PsExecution, DnsSuffixList,
    MinPwdLength, NoControlPanel, Wallpaper, NoCmd, DriveMap...), `gpo status "Name" UserSettingsDisabled|...`, `gpo
    filter "Name" add|remove "Group"` (security filtering = GpoApply), `gpo perm "Name" "Who" GpoRead|GpoApply|GpoEdit|
    GpoEditDeleteModifySecurity|None`, `gpo wmi "Name" "Filter"|none`, `wmi new name="X" query="SELECT * FROM ..."`,
    `inheritance block|unblock "TARGET"`, `gpupdate CL1 [target=computer|user]`, `centralstore create`.
  - DNS Manager: `dns zone add server=DC1 name=X type=Primary|Secondary|Stub|Forwarder ad=Domain|Forest|Legacy|no
    [masters=IP,IP dynamic=None|Secure|NonsecureAndSecure]`, `dns zone set|remove|sign|unsign|transfer server= name=
    [dynamic= transfers=NoTransfer|TransferAnyServer|TransferToZoneNameServer|TransferToSecureServers secondaries=IPs
    aging=yes]`, `dns record add|remove server= zone= name= type=A|CNAME|MX|PTR|SRV|TXT data= [ttl= ptr=yes scope=]`,
    `dns forwarders server=DC1 set=IP,IP [roothints=no]`, `dns recursion server=DC1 on|off`.
  - Windows Admin Center: `wac add|remove SERVER`, `wac register`, `feature install|remove SERVER FEATURE`, `service
    start|stop SERVER NAME`, `dhcp authorize server=X [remove]`, `dhcp scope add server= name= start= end= mask=
    [days= active=no]`, `dhcp scope set|remove|exclude|reserve|option server= id=SCOPE [start= end= ip= mac= name=
    option=6 value=...]`, `dhcp option server= option= value=`, `dhcp failover add|replicate|remove server= partner=
    name= scopes= mode=LoadBalance|HotStandby percent= reserve= mclt=MIN secret=`, `share new|remove|grant|revoke|deny|
    set server=FS1 name= [path= access="CORP\HR:Change;Everyone:Read" abe=yes encrypt=yes account= right=]`, `ntfs
    grant|deny|remove|inheritance|owner server=FS1 path=D:\... account=CORP\X rights=Modify [apply=this,subfolders,files]`
    (inheritance: `enable`, `convert`, `remove`), `vm new|start|stop|checkpoint|restore|deletecheckpoint|set|delete NAME
    ...` (`set NAME nested=yes dynamic=no spoofing=on switch= cpus= memorygb= checkpointtype=`), `vswitch new name= type=
    adapter=`, `storage pool|vdisk|dedup|optimize server=FS1 ...`, `dfsn root|folder path= target=`, `dfsr group name=
    members=A,B folder=F`, `dfsr membership group= server= folder= path= primary=yes`, `dfsr poll SERVER`.
  - Azure portal (practice): `arc onboard|disconnect SERVER [rg=]`, `arc extension SERVER NAME`, `update assess|install
    SERVER [classes=Security]`, `azure netadapter SERVER` (the gateway takes about 25 minutes), `azure vpn on|off`,
    `azure vnetdns IP,IP|default`, `azure vm NAME [disk name= size= caching=None] [ipallocation=Static]`, `connect
    install server=SRV1 signin=PHS|PTA|Federation sso=yes ous="DN;DN" staging=no`, `connect sync`, `connect filter
    "DN;DN"`, `connect ptaagent SERVER`, `connect scheduler on|off`, `cloudsync install server= scope=DN`, `filesync
    service name=`, `filesync register SERVER`, `filesync group name= account=stharborfiles share=salesarchive`,
    `filesync endpoint group= server= path=E:\... tiering=yes days=60 free=20`, `filesync now GROUP`.
- **Checks on `admin`** are assertions, one per line (all must hold), with the MD-102 operators (`==`, `!=`, `has`,
  `!has`, `like`, `contains`, `>=`, `<=`, `>`, `<`; a line without one holds when the value exists; `not LINE`).
  `@DC2 LINE` reads DC2's copy of the directory (default: the PDC emulator's). Roots and members:
  `user["bcole"]` / `group[..]` / `computer[..]` / `ou["Staff/Sales"]` / `object[..]` / `deleted[..]` / `gmsa[..]`
  (`.department`, any attribute, `.ou`, `.path`, `.enabled`, `.members`, `.allmembers`, `.memberof`, `.allgroups`,
  `.scope`, `.protected`, `.blocked`, `.links`, `.upn`, `.mustchange`, `.lockedout`, `.principals`),
  `site[..]` (`.servers`), `subnet[..]` (`.site`), `sitelink[..]` (`.sites`, `.interval`, `.cost`), `dc["DC2"]`
  (`.site`, `.roles`, `.gc`, `.readonly`), `fsmo.PDCEmulator`, `domain.minPwdLength`, `forest.upnsuffixes`,
  `forest.recyclebin`, `trust[..]` (`.type`, `.direction`, `.selective`), `pso[..]` (`.appliesto`, `.minPwdLength`),
  `passwordpolicy(user=x).minlength`, `replicated(DC1, DC2)`, `gpo["Name"]` (`.links`, `.enforced["TARGET"]`,
  `.linkenabled["TARGET"]`, `.linkorder["TARGET"]`, `.setting["ID"]` (+`.value`), `.filter`, `.perm["Who"]`,
  `.wmifilter`, `.status`), `wmifilter[..]`, `rsop(computer=CL1, user=bcole[, site=X])` (`.setting["ID"]`, `.value`,
  `.winner`, `.applied`, `.usergpos`, `.computergpos`, `.denied`, `.reason["GPO"]`, `.loopback`), `applied(CL1[,
  user=bcole])` (what the computer last applied), `centralstore(DC1)`; `dns(DC1)` (`.zone["x"]` with `.type`,
  `.adintegrated`, `.scope`, `.dynamic`, `.transfers`, `.signed`, `.loaded`, `.record["www/A"]`; `.forwarders`,
  `.recursion`, `.conditional["x"]`, `.subnet["x"]`, `.policy["x"].action`), `resolve(client=CL1, name=x[, type=MX,
  server=DC2])` (the address(es), or NXDOMAIN / FAILED), `dhcp(SRV1)` (`.authorized`, `.scope["10.10.0.0"]` with
  `.state`, `.option["6"]`, `.exclusions`, `.reservation["ip"].mac`; `.failover["x"].mode|partner|percent`),
  `lease(CL1)` (`.ip`, `.server`, `.dns`, `.apipa`), `vm["X"]` (`.state`, `.generation`, `.memorygb`, `.dynamic`,
  `.nested`, `.cpus`, `.switch`, `.spoofing`, `.checkpoints`, `.checkpointtype`), `vswitch[..]` (`.type`),
  `vhd["D:\..."]` (`.type`, `.format`, `.sizegb`, `.parent`), `container[..]` (`.isolation`, `.status`),
  `images`, `share["FS1\Sales"]` (`.path`, `.access["Everyone"]`, `.abe`, `.encrypt`), `ntfs(FS1, "D:\...")`
  (`.aces`, `.inherited`, `.protected`, `.owner`), `access(user=bcole, path="\\FS1\Sales")` (the effective access in
  words: Full control, Modify, Read and execute, Read, No access; `.rights` has Read / Write / Delete...; `.ntfs`,
  `.share`; `path="FS1:D:\..."` for local NTFS access), `file(FS1, "path")` (`.tiered`), `pool[..]`, `vdisk[..]`
  (`.resiliency`, `.copies`, `.provisioning`), `volume(FS1, F)` (`.fs`), `dedup(FS1, E)` (`.enabled`, `.savedgb`),
  `feature(SRV1, "DHCP")` (Installed...), `service(SRV1, "DHCPServer")` (Running...), `namespace["\\corp.example\X"]`
  (`.folders`, `.targets["F"]`), `dfsresolve("\\corp.example\X\F", Branch)`, `dfsr["G"]` (`.members`, `.primary`,
  `.contentpath["FS1"]`, `.synced`), `hotfix(SRV1)`, `arc["SRV1"].status`, `updates(SRV1).missing`,
  `azure.vm["AZ-DC3"].ipallocation` / `.disk["ntds"].caching`, `azure.vnetdns`, `azure.reach["SRV1"]`,
  `connect.signin|server|ous|ptaagents|sso`, `cloud.user["bcole"].upn`, `signin(user=bcole[, password="..."])`
  (Allowed / Wrong password / ...), `filesync["G"]` (`.servers`, `.tiering["FS1"]`, `.cloudfiles`), `wac.connections`.
  Checks on a machine are PowerShell, as in the PowerShell course (last output true).
- **Time**: the clock starts on Monday 2 March 2026 09:00. Changes on a DC reach the other DC of the same site about
  15 seconds later and other sites on their site link's schedule (every replInterval minutes, counted from Monday
  00:00); computers refresh Group Policy every 90 minutes or so (DCs every 5), at startup and with gpupdate.
  Microsoft Entra Connect runs a delta sync every 30 minutes and password hash sync every 2 minutes.
- **Exams**, item kinds and scoring are as in MD-102 (1-1000, 700 to pass). Exams restart every machine before
  grading (DCs replicate, computers refresh Group Policy): tasks must leave lasting state.
- **Lesson examples** (` ```terminal ` blocks) are typed into a fresh network on `DC1` unless `#machine NAME`;
  `#setup admin:LINE`, `#setup MACHINE:POWERSHELL` and `#setup wait:SECONDS` prepare it. Fill them with
  `[CertLab.Tests]::WsFillExamples(text)` rather than typing the output.
- Tests: `-Only ws` (engine and content), `-Only ws-tries:l0301`, `-Only ws-labs:lab-03`, `-Only ws-exams:exam-1`,
  `-Only ws-content`.

## The Security+ course (`secplus`, simulator `rhel`)

The same lesson, card and sheet files. Practice exams are questions only (no hands-on tasks), scored from 100 to 900
with 750 to pass (`scoreMin: 100`, `examPoints: 900`, `passScore: 750`: the score is 100 plus the share of the points
times 800; CompTIA doesn't publish its scaling). `weights` in course.json gives each objective's published share in
percent; every exam's points per objective must stay close to it. Labs and try-its are tasks on the RHCSA course's
machines (server1, server2), written exactly as RHCSA labs (bash checks, typed solutions). `openssl` (x509, verify,
dgst, rand) and `nmap` are there for the security labs; setups make certificates with `certlab-mkcert FILE
subject="CN=x" [issuer="CN=y"] [days=N | notbefore=YYYY-MM-DD notafter=YYYY-MM-DD] [san=a,b] [ca=yes] [eku=serverAuth]
[sig=sha256WithRSAEncryption] [bits=2048] [serial=HEX]` (a pretend certificate: the PEM body is Cert Lab's own
encoding, readable only by the simulator's openssl).

Exam items are `@choice`, `@multi` (the existing kinds) and **performance-based questions** (PBQs), which give
partial credit: every part (a matched item, a test packet, a placed device, a sub-question, a calculation, the log
indicators as one part) is worth an equal share of the item's points, rounded down. The header is
`@KIND id | objective | points`; then the prompt (prose lines, up to the first directive), then the parts, `! `
explanation lines; the next item's `@...` line ends it. Directives start a line, in lowercase, so prose lines must
not start with a lowercase `calc`, `zone`, `device`, `addr`, `ports`, `rows`, `start`, `rule`, `test` or `columns`.

```
@match m1 | Threats, vulnerabilities and mitigations | 5
Drag each attack to the control that would best have stopped it.
columns Attack | Best mitigation
= Phishing email with a fake sign-in page -> Security awareness training
= SQL injection in the search box -> Input validation
x Full-disk encryption
! Phishing works on people, so training (with MFA) is the best answer; ...
```
`= LEFT -> RIGHT` pairs (the right sides, with `x` distractors, are the cards to drag; a card may fit several items).

```
@firewall f1 | Security operations | 6
Write the rules for the screened subnet's firewall. Rules are read top to bottom; the first that matches wins;
anything no rule matches is denied.
rows 4
addr Internet = 0.0.0.0/0
addr LAN = 10.0.1.0/24
addr Web server = 10.0.2.10
ports 22, 53, 80, 443, 1433, 3389
start DENY | Any | Any | Any | Any
test allow | 203.0.113.9 -> 10.0.2.10 | 443/TCP | customers reach the shop over HTTPS
test deny | 203.0.113.9 -> 10.0.2.10 | 22/TCP | no SSH from the internet
rule ALLOW | Any | Web server | 443 | TCP
rule ALLOW | LAN | Web server | 22 | TCP
! ...
```
`rows` is the number of rows in the table; `addr NAME = CIDR` the addresses offered (plus Any); `ports` the ports
offered (plus Any); protocols are TCP, UDP, ICMP or Any; `start` rows are filled in when the question opens (for "fix
the broken rules" questions); `rule` rows are the model answer; `test allow|deny | SRC -> DST | PORT/PROTO | why` are
the packets the grader sends through the person's rules (each test a part). A packet that must be blocked earns its
share only when the table lets at least one wanted packet through (an empty table earns nothing). Write tests that
tell right from wrong rule orders.

```
@logs l1 | Threats, vulnerabilities and mitigations | 6
The help desk reports that the shop's search page is slow. Read the logs.
@log Web server access log (www1)
  192.0.2.14 - - [02/Mar/2026:09:14:01 +0000] "GET /search?q=boots HTTP/1.1" 200 5120
* 203.0.113.50 - - [02/Mar/2026:09:15:22 +0000] "GET /search?q=1'%20OR%20'1'='1 HTTP/1.1" 500 312
@endlog
? attack | What kind of attack do the marked lines show?
- Cross-site scripting
+ SQL injection
?? response | Which two actions should come first? (pick two)
+ Block the source address at the WAF
+ Fix the query with parameterised statements
- Reboot the web server
! ...
```
`@log TITLE` ... `@endlog`: every line starts with `* ` (an indicator: the person should mark it) or two spaces.
Marking: each indicator found counts, each wrong mark takes one away, as one part. `? id | text` is a sub-question
with one answer (`+` once), `?? id | text` one with several (all of them, nothing else). `@exhibit TITLE` ...
`@endexhibit` shows text as it is (a certificate, an email header, a scan report) with nothing to mark.

```
@place p1 | Security architecture | 5
Place each device where it belongs.
zone internet | Internet
zone dmz | Screened subnet | Edge firewall
zone lan | Internal network | Internal firewall
zone mgmt | Management network | Internal firewall
device Web application firewall -> dmz
device Jump server -> mgmt
device Database server -> lan
device Honeypot -> dmz
device Network IDS sensor -> lan, dmz
```
Zones are drawn left to right in this order; the third field is what sits on the link from the previous zone.
`device NAME -> zone[, zone]` lists the zones that are right for it.

`@cert` is an `@exhibit` (a certificate as `openssl x509 -text` prints it) plus sub-questions; `@risk` is a scenario
plus `calc id | label | = expression` parts (numbers, `+ - * /`, parentheses, `{vars}`; the person's number is right
within 0.2% or 50 cents; `$` and commas are ignored) and sub-questions. Any PBQ may mix parts of the others.

PBQ practice (`pbq\*.txt`, `# Practice: pbq-firewall | Firewall rule tables`, then PBQ items) opens from the PBQ
practice page: one item at a time; Check shows every part right or wrong, with the explanation.

Tests: `-Only sec` (groups sec-pbq, sec-content, sec-search, sec-tries, sec-labs, sec-exams, sec-ui),
`-Only sec-exams:exam-1`, `-Only sec-labs:lab-03`.
