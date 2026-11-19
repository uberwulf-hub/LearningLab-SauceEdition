# Cyber Range scenario and puzzle files

Cyber Range lives inside Cert Lab (`Apps\CertLab`) and shares its engine. Its content is data under
`Apps\CertLab\Range\`:

```
Range\
  wordlist.txt            the built-in weak-password list (used by hashcrack and to pick scenario passwords)
  pools.txt               named pools of made-up details (first/last names, companies, server names...)
  vulnnotes.txt           the made-up vulnerability notes shown by the vulnlookup tool
  scenarios\<id>\         one folder per scenario
    scenario.json         the scenario (below)
    <machine>.sh          Linux setup scripts named by the machine's "setup" field
    <machine>.ps1         Windows setup scripts (PowerShell, run on the pretend host)
    solution.txt          the model solution, typed into the player's console (default name; "solution" overrides)
    walkthrough.txt       the intended path, shown only in the end report (default name; "walkthrough" overrides)
    siem.txt              SOC Analyst only: the scenario's own SIEM events (default name; "siem.file" overrides)
  puzzles\<id>.txt        one file per puzzle (below)
```

Everything is simulated: no real network, no real commands, nothing downloaded. Scenarios and puzzles are built and
checked by `CertLab.ps1 -Test -Only range` (see "Testing" at the end).

## Categories and the home page

The home page shows top-level **categories**; clicking one shows the four **difficulty levels** (Beginner,
Intermediate, Advanced, Expert); clicking a level lists its challenges. The categories are fixed in code
(`RangeData.Categories`):

| category id | name | what it is |
|-------------|------|-----------|
| `red` | Red Team | attack an unknown pretend network solo and capture its flags |
| `defender` | Red vs Defender | the same, but a simulated defender watches the logs and reacts |
| `blue` | Blue Team | you are the defender: investigate what an attacker did and clean it up |
| `soc` | SOC Analyst | alert triage in a pretend SIEM: real attack or false alarm, then what happened (scenarios) |
| `web` | Web Security Lab | one pretend online shop, Copperleaf Market, a classic web mistake per challenge (puzzles) |
| `ladder` | Linux Ladder | twenty small Linux steps in order; each answer is the next step's password (puzzles) |
| `ctf` | CTF Puzzles | short standalone puzzles, grouped by topic (`RangeData.Topics`) |

A scenario's `category` is `red`, `defender`, `blue` or `soc` (default `red`, or `defender` when `defender` isn't
`none`). `blue` and `soc` put the player on defence (`RangeData.BlueLike`: questions and fixes in the Findings tab).
A puzzle's `category:` is `ctf` (the default), `web` or `ladder` (`RangeData.IsPuzzleCategory`;
`RangeItem.PuzzleCategory` reads it); its `topic` places it in a group on the level page, from
`RangeData.TopicsOf(category)` (the CTF topics, the Web Security Lab's own six, the ladder's single "Climb in
order"). A category with one topic fills it in by itself.

**How a category looks** (accent colours, drawn icon, the "In plain words" text) comes from `RangeLook` in
`Engine\RangeStyle.cs`. The ids above have hand-picked values (`soc`: a bell in orange; `ladder`: a terminal in
green); any other id gets an icon from words in its id or
title (`web` / `site` / `http` -> globe, `linux` / `ladder` / `shell` -> terminal, `soc` / `alert` / `triage` -> bell,
`cloud` / `azure` / `aws` -> cloud, `crypt` / `cipher` -> key, ...), the matching colours, and a generic plain-words
line - so a new category added to `RangeData.Categories` looks right without code changes. Add it to `known` in
`RangeLook` only to choose its colours and words by hand. The four levels' colours and one-line purposes are
`RangeLook.Levels`; a category's own `LevelLines` are shown first when it has them.

**Two optional fields help the cards and beginners** (scenarios and puzzles alike): `minutes` (a time guide; default by
level: puzzles 5 / 10 / 15 / 25, scenarios 20 / 35 / 50 / 75 for Beginner / Intermediate / Advanced / Expert) and
`simple` (the challenge in words a middle schooler follows, shown by **Explain it simply** on Beginner; without it,
the category's plain-words text is shown).

## The glossary (`glossary.txt`)

The words Beginner pages dot and explain (hover: a tip; click: a small card), and the Glossary page. Each entry is
`== term | other spellings, ...` followed by one or two sentences, plain enough for a middle schooler and still
accurate for a professional. Spellings match whole words, ignoring case; the longest spelling wins; each term is
linked once per text; a spelling may belong to only one term (`range-glossary` checks this, and that the file is
ASCII). Avoid spellings that are everyday words in other senses.

## Flags, seeds and variables

Each run is built from a seed, so the flags and details are different every time (answers can't simply be shared).
A flag is `FLAG{<id-slug>-<10 hex>}`, unique per scenario run and per flag id. A challenge is kept as a **template**;
one run **instantiates** it by filling every `{{...}}` from the seed, so the setup scripts, hints, solution and
walkthrough all use the same details.

A scenario or puzzle declares **variables** that the seed fills (`vars` in JSON, `var: NAME = spec` in a puzzle):

| spec | meaning |
|------|---------|
| `word` | a word from `wordlist.txt` (a weak password) |
| `pick:a\|b\|c` | one of the listed values |
| `num:LOW-HIGH` | a number in the range |
| `hex:N` | N hex digits |
| `ip` | a documentation address (198.51.100.x or 203.0.113.x) |
| `name` | a lower-case first name |
| `pool:NAME` | one line of the `@NAME` list in `pools.txt` |
| `person` | a made-up person: also sets `NAME_FIRST`, `NAME_LAST`, `NAME_FULL` (the value is their first name; `person:flast` = jsmith, `person:first.last` = jo.smith) |
| `company` | a made-up company: also sets `NAME_SHORT`, `NAME_SLUG`, `NAME_DOMAIN` (`<slug>.example`) |
| `subnet` / `subnet:10\|172\|192` | the first three numbers of a /24 (`{{NET}}.20` is a host, `{{NET}}.0/24` the network) |
| `time:H-H` | a clock time HH:MM:SS between the two hours |
| anything else | that literal text (which may contain other `{{...}}`) |

The same seed always gives the same values. A value is reused only when it hasn't already been handed to another
variable of the same challenge, so names and passwords don't clash.

### Variants (which weakness is present)

`variants` lets one variable's value **pick extra variables**, so each run can have a different (but equivalent)
weakness. In JSON:

```json
"vars": { "WEAK": "pick:find|awk|tar" },
"variants": { "WEAK": {
  "find": { "SUDOBIN": "/usr/bin/find", "SUDORUN": "sudo find /root -name flag.txt -exec cat {} \\;" },
  "awk":  { "SUDOBIN": "/usr/bin/awk",  "SUDORUN": "sudo awk '{print}' /root/flag.txt" },
  "tar":  { "SUDOBIN": "/usr/bin/tar",  "SUDORUN": "sudo tar -cf /dev/null /root/flag.txt --checkpoint=1 --checkpoint-action=exec='cat /root/flag.txt'" } } }
```

In a puzzle, use `@vars NAME=value` sections:

```
var: WEAK = pick:find|awk|tar
@vars WEAK=find
SUDOBIN = /usr/bin/find
@vars WEAK=awk
SUDOBIN = /usr/bin/awk
```

### Conditionals

- In JSON, an array item with `"if": "WEAK=find|awk"` (or `WEAK!=tar`, several joined with ` and `) is dropped when
  the condition fails.
- In any filled text (setup scripts, solutions, walkthroughs), whole-line blocks
  `{{if WEAK=find}} ... {{else}} ... {{end}}` keep only the matching lines.

### Placeholders

- `{{NAME}}` - a variable's value
- `{{FLAG}}` / `{{FLAG:id}}` - a flag (a puzzle has one, `{{FLAG}}`; a scenario names flags by id)
- `{{SIMHASH:NAME}}` - the simulated Windows (NT-style) hash of a value
- `{{MD5:NAME}}`, `{{SHA1:NAME}}`, `{{SHA256:NAME}}` - a hash of a value
- `{{B64:NAME}}`, `{{HEX:NAME}}`, `{{ROT13:NAME}}`, `{{REV:NAME}}`, `{{UPPER:NAME}}`, `{{LOWER:NAME}}` - transforms
- `{{TICKET:user:NAME}}` - a simulated Kerberos service ticket (crackable with hashcrack)
- `{{B32:NAME}}` - base32 (decoded by `base32 -d`); `{{MORSE:NAME}}` - Morse code (`morse -d`)
- `{{XOR:KEY:NAME}}` - NAME XORed with the byte in variable KEY (42 or 0x2a), as hex (`xorcat -k`, `xorcat --brute`)
- `{{VIG:KEY:NAME}}` - NAME Vigenere-encrypted with the key word in variable KEY (`vigenere -d -k`)
- `{{CAESAR:N:NAME}}` - letters shifted by N (a number or a variable)
- `{{WORDLIST}}` - the whole word list's text

The hash and ticket formats are Cyber Range's own simulated formats: they only mean something inside the range.

## scenario.json

```json
{
  "id": "open-door",
  "title": "Open Door",
  "category": "red",                // red | defender | blue (default red, or defender when "defender" != "none")
  "level": "Beginner",              // Beginner | Intermediate | Advanced | Expert
  "order": 1,
  "summary": "one line for the challenge list",
  "story": "the briefing",
  "goal": "what to capture",
  "skills": "comma list shown on the briefing",
  "defender": "none",               // "none" for red/blue; "watch" turns on the defender for the defender category
  "start": "scout",                 // the machine the player sits at (a Linux machine in "machines")
  "minutes": 25,                    // optional: a time guide for the cards and briefing (default by level)
  "simple": "...",                  // optional: the challenge in plain words for "Explain it simply" (Beginner)
  "vars": { "PW1": "word" },
  "variants": { },                  // see above
  "machines": [ ... ],              // see below
  "routes": [ { "from": "10.10.1.0/24", "to": "10.10.2.0/24", "ports": [80] } ],
  "web": [ ... ],                   // pretend web apps (see below)
  "domain": "corp.example", "netbios": "CORP", "dc": "WIN-DC01",   // a pretend Windows domain (optional)
  "domainAccounts": [ { "user": "svc_sql", "password": "{{SVCPW}}", "admin": true, "spn": "MSSQLSvc/..." } ],
  "hosts": [ "10.10.1.20 target.corp.example" ],   // lines added to the start box's /etc/hosts
  "flags": [ { "id": "user", "name": "...", "points": 100, "machine": "files01", "path": "/home/dana/flag.txt",
              "owner": "dana", "mode": "600", "level": "user" } ],   // level: user | admin
  "hints": [ { "text": "...", "cost": 10 } ]   // 3-5; Beginner ones longer and gentler; Expert short nudges
}
```

A scenario may set `"base": "<other scenario folder>"`: the other scenario's keys are read first and this file's keys
override them (and its `vars` are merged). Red vs Defender and some Blue scenarios reuse a red network this way.

### Red vs Defender (`category: "defender"`)

```json
  "defender": "watch",
  "quietPoints": 120,               // the quiet bonus, lost per alert the defender raises
  "watch": [ "files01" ],           // machines the defender watches (default: all but the start box)
  "noisySolution": "noisy.txt",     // a loud approach the test checks the defender catches
  "defenderRules": {                // optional: this defender's own habits
    "count": { "privilege": 4, "login-failed": 3 },   // only these noise kinds count, each event worth that much
    "budget": 8, "window": 1200, "react": 60,          // alert threshold, seconds counted over, seconds to the block
    "reset": true, "isolate": false,                   // reactions on/off (default: by level)
    "watches": "people using sudo and failed sign-ins" // shown on the briefing
  }
```

The defender (`RangeDefender.cs`) watches the noise log on the clock. Over a window it adds up the loudness from each
source; past a budget it raises an **alert**, and after a delay it **blocks** that source (its connections to watched
machines then time out). Advanced and Expert defenders also reset a password they saw used; Expert is quick and can
isolate a host. Noise kinds include `port-scan`, `ping-sweep`, `login`, `login-failed`, `password-guessing`, `web-scan`, `web-command`, `web-traversal`, `web-login-failed`, `ticket-request`, `smb-read` / `ftp-read` / `database-read`, `privilege` (sudo or su on a range machine) and `new-process` (a remote Windows shell starting a process, task or service). Beginner defenders are slow and forgiving and explain in plain words what they noticed (the end
report). Score keeps `quietPoints` only while the alarm never rings, so the **model solution must win by a quiet
path** (few, low-loudness actions) and the **`noisySolution` must get caught**.

### Blue Team (`category: "blue"`)

The player defends. The start box is still a Linux box they investigate from, but the scenario also carries the
attacker's trail and what to find/fix:

```json
  "attackerIp": "{{ATKIP}}", "goalName": "copy the customer database out", "goalPoints": 100,
  "attacker": [
    { "at": "0s",  "machine": "web01", "run": "...setup already-happened steps...", "note": "A web shell was dropped." },
    { "at": "5m",  "machine": "web01", "run": "...", "unless": "test ! -f /opt/evil", "note": "It copied data out.", "goal": true }
  ],
  "questions": [ { "id": "acct", "ask": "Which account did the attacker use?", "answer": "{{USER}}",
                   "accept": [ "{{USER}}@..." ], "points": 50, "explain": "seen in /var/log/secure" } ],
  "tasks": [ { "id": "kill", "name": "Remove the web shell", "machine": "web01",
               "check": "test ! -f /var/www/html/up.php", "points": 50, "explain": "delete the dropped file" } ]
```

- `attacker` steps run on the clock (`at` seconds after the run starts) unless the player has stopped them (`unless`
  is a bash/PowerShell test that returns 0 when already blocked). A `goal` step reaching its time means the attacker
  won (the `goalPoints` bonus is lost, but the run can still be finished). Setup-time (`at: 0s`) steps stand in for
  things that already happened before the player arrived.
- `questions` are answered in the Findings tab (checked case-insensitively against `answer` and each `accept`).
- `tasks` are fixes the player makes in the terminal; the Check button runs the `check` script as root on `machine`
  (exit 0 = done). A Windows check script must end with `exit 0` / `exit 1`.
- A Blue scenario can also hide a `flags` entry (for example a flag inside the attacker's dropped file).
- Pretend Windows event-log history is seeded with a machine's `events` (below); `ago` takes one unit only (`90s`,
  `20m`, `5h`, `2d`; write `5h59m` as `21540s`), and a seeded event 1102 ("the audit log was cleared") empties that
  log first, so only it and the events seeded after it remain; the player reads it with
  Get-WinEvent / Get-EventLog in the winrm shell, or the Linux logs with journalctl, `last`, etc.

Question fields used by both defending categories: `tries` (how many answers are allowed; when they run out the
question closes with no points and shows its answer - `1` makes a verdict a real decision; 0 / missing = any number)
and `derived` (`true` when the answer is worked out from what the searches show, such as the seconds between two
events, rather than printed as such: the test then doesn't look for it in the solution's output). An answer of
`real` or `false alarm` is a **verdict**: the player may also type "true positive", "malicious", "false positive",
"benign" and the like (`RangeData.Verdict`), and any other text is refused without using up a try.

### SOC Analyst (`category: "soc"`)

The player is the analyst on shift at a pretend SIEM. Nothing happens on the clock: everything already happened, and
the work is reading the events. The start box (the analyst's Linux workstation, usually the scenario's only machine)
gets `/var/lib/siem/events.log`, which the `siem` tool searches (`man siem`), built by `RangeSoc.cs` from a `siem`
section and a file of event lines:

```json
  "siem": {
    "file": "siem.txt",             // the scenario's own events (default siem.txt), filled like every other file
    "day": "2026-10-20",            // the date (default 2026-10-20)
    "start": "{{T0}}",              // the clock time of offset +0 (a time:H-H variable, so it changes per run)
    "noise": { "count": 180, "from": "-3h", "to": "+40m",            // background events and their time window
               "hosts": [ "WS-{{H1}}", ... ], "users": [ "{{U1}}", ... ], "ips": [ "{{NET}}.{{H1}}", ... ],
               "net": "{{NET}}", "servers": [ "FILE01" ], "dc": "DC01", "web": "WEB01", "linux": "WEB02",
               "domain": "{{CO_DOMAIN}}", "firewall": "FW01", "public": "192.0.2.10",
               "kinds": [ "logon", "process", "dns", "firewall", "web", "ssh" ],   // weights by repetition
               "quiet": [ "{{U}}" ] } }  // accounts / hosts that get no background sign-ins (their story is exact)
```

`siem.txt` has one event per line: an offset from the start (`+0`, `+95s`, `-20m`, `+2h`; one unit only), then
`field=value` pairs (values with spaces in double quotes, `\"` inside them). Every event needs `host=` and `source=`
(`alert`, `winsec`, `proclog`, `dns`, `firewall`, `auth`, `web`; `man siem` explains their fields). An alert is
`source=alert` with `severity`, `rule`, `note`, and its `host=` is the computer it is about. Repeats:

```
+0 x{{NFAIL}}/10s host=FILE01 source=winsec event=4625 user={{U}} ...       N copies, 10 s apart (%N% = 1, 2, ...)
-6m x[/admin|/.env|/backup.tar]/15s host=WEB01 source=web path=%ITEM% ...    one per item (%ITEM%)
```

`{{if VAR=x}} ... {{else}} ... {{end}}` blocks work as in setup scripts. The engine adds the noise (ordinary
sign-ins, programs, name lookups, firewall lines, web visits; never failed sign-ins, new accounts, services or
PowerShell, so counts in the scenario stay exact; addresses written in siem.txt are never reused for noise), sorts
everything by time and writes `time=yyyy-MM-ddTHH:mm:ss` in front of each line. A SOC scenario has five or more
`questions`, at least one a one-try verdict about an alert that is randomly the real one or a **harmless
look-alike** (an old saved password, IT's script, a crawler, a backup, a planned install...), usually chosen by a
variant (`"QV": "pick:a|b"` naming which alert the verdict asks about, or `"REAL": "pick:a|b"` swapping which host is
attacked). No `attacker`, `tasks` or `goalPoints` are needed. Ask for times as HH:MM:SS of the `+0` event
(`"answer": "{{T0}}", "accept": [ "2026-10-20T{{T0}}" ]`).

A machine:

```json
{ "name": "files01", "os": "linux",      // linux (default) or windows
  "hostname": "files01.northwind.example", "role": "shown on the map",
  "nics": [ "10.10.1.20/24" ],           // one or more addresses (a machine on two subnets is a router/pivot)
  "server": true, "firewall": false,     // firewall false (default) turns firewalld off so ports answer
  "packages": [ "vsftpd" ],              // dnf packages installed before setup
  "tools": [ "nmap" ],                   // extra programs (a package name, or a range tool)
  "setup": "files01.sh",                 // a script file in the scenario folder
  "motd": "Authorized users only...",    // Linux: /etc/motd, shown on ssh login (a target's scenario banner)
  "banner": "...",                       // Linux: /etc/issue, shown at the machine's own console
  "ports": [ 135, 445, 5985 ],           // Windows only: the ports the host answers on
  "versions": { "21": "vsftpd 3.0.5" },  // what nmap -sV reports for a port
  "accounts": [ { "user": "kim", "password": "...", "admin": false, "remote": true } ],   // Windows local accounts
  "shares": [ { "name": "Public", "path": "C:\\Shares\\Public", "anonymous": true } ],    // Windows SMB shares
  "events": [ { "log": "Security", "provider": "Microsoft-Windows-Security-Auditing", "id": 4625, "level": 0,
                "message": "Failed sign-in for {{USER}}...", "ago": "6m", "repeat": 12, "every": 8 } ],  // blue team
  "domainJoined": true, "domainAdmins": [ "svc_sql" ],   // Windows: in the domain; which domain accounts are admins
  "private": [ "C:\\Secrets" ], "writable": [ "C:\\Jobs" ] }   // Windows: folders a standard user can't read / can write
```

Place flags with the `flags` list, not by hand (the engine writes them after setup so `grep -r FLAG` on the start box
finds nothing). The **attack box** (the player's own start machine) shows its own made-up distro banner (RangeKit
Linux - never "Kali"); **target machines** show the scenario's `motd` on ssh login.

A web app (answered when curl/wget reach a range machine):

```json
{ "machine": "app01", "port": 80, "path": "/tools/ping", "title": "...", "server": "AcmeCMS/2.1",
  "kind": "cmd",                         // page | login | cmd | file | idor | cookie
  "param": "host", "runAs": "apache",
  "template": "ping -c1 {}",             // cmd: the command, {} replaced by the parameter, NOT checked (injection)
  "base": "/var/www/docs",               // file: the folder; the parameter is joined without removing ../ (traversal)
  "logins": [ ["admin","printpal"] ],    // login: user/password pairs; shows "template" HTML on success
  "records": [ ["1","Avery","hidden"] ], // idor: rows; ?param=N shows row N with no check that it is yours
  "cookie": "role=admin", "want": "42",  // cookie: the cookie the page trusts (shows "template"); idor: the telling id
  "header": "Set-Cookie: role=user",     // an extra response header (seen with curl -i)
  "body": "<h1>...</h1>", "bodyFile": "...", "templateFile": "..." }
```

`curl -b "name=value"` sends a cookie (read by the `cookie` kind); `curl -i` shows the `header`.

More kinds:

- `fetch` - a "fetch this address for me" page (`?param=http://...`): the server makes the request itself, wherever
  the address points (server-side request forgery when nothing checks it).
- `line` - not a web page: a plain service on `port` that `nc` talks to. `body` is its greeting, each `logins` pair
  is [line sent, reply], and `fail` is the reply to anything else.
- `shop` - Copperleaf Market, the Web Security Lab's whole pretend online shop (`RangeShop.cs`), on `path` "/" of the
  puzzle box (`copperleaf.example` in its /etc/hosts). It is configured by `settings` (all text, filled per run):

| setting | meaning |
|---------|---------|
| `flag` | the secret the challenge hides (staff page, an order, a file, a token...) |
| `weak` | the mistakes switched on, comma-separated (below) |
| `me`, `mypass` | the player's own customer account |
| `accounts` | more accounts: user, password, role and email joined by bars, records joined by `;` (role `admin`/`staff`/`manager` is staff) |
| `orders` | id, owner, items and note joined by bars, records joined by `;` |
| `seqstart` | the first order / reset-code number (default 4100) |
| `name`, `host` | the shop's name and host name |
| `comment`, `robots`, `docs`, `appjs`, `hidden`, `internal` | page source comment, robots.txt text (`\n` for lines), a docs page, a /static/app.js, a page left unlinked, the internal admin path (default `internal/admin`) |
| `idor` | `b64` writes order links as `ref=<base64 id>` instead of `id=N` |
| `field` | the field a sign-up form copies blindly (`mass`; default `isAdmin`) |
| `reset` | the staff asked for a reset code earlier (`reset` weakness) |
| `sqltable`, `sqlcols`, `sqlrows` | an extra table the search's pretend query language can reach (`sqli-search`) |
| `xssfilter`, `traversalfilter` (`once`), `ssrfblock`, `uploadcheck` (`type`) | half-fixes Advanced/Expert challenges get past |

`weak` values: `idor` (order numbers anyone can change), `b64cookie` (a cookie that is only base64 and believed),
`tokennone` (a signed token whose check can be skipped), `sqli-login` / `sqli-search` (typing glued into the pretend
query: `RangeSql`, a tiny made-up query language, not a real database), `xss-stored` / `xss-reflected` (a review or
search shown unescaped; the pretend staff browser "visits" `catcher`), `traversal` (the picture page joins `../`),
`ssrf` (the stock checker fetches any address), `upload` (the avatar upload runs pretend server pages), `csrf`
(forms with no anti-forgery check), `mass` (sign-up copies every field), `debug` (a debug page left on). Nothing in the
shop runs a real script, query or request.

## Puzzle files (`puzzles\<id>.txt`)

A small key/value header, then `@`-sections:

```
title: Base 64, base case
level: Beginner
kind: Decoding
topic: Crypto & Encoding   # one of RangeData.TopicsOf(category); inferred from kind (ctf) or the only topic
category: ctf              # ctf (default) | web (Web Security Lab) | ladder (Linux Ladder)
login: player              # the account the console signs in as (default player; the ladder: step1, step2...)
teaser: short line for the list
points: 40
minutes: 5                 # optional time guide (default by level: 5 / 10 / 15 / 25)
simple: In plain words...  # optional, one line, for "Explain it simply" on Beginner
order: 1
packages: nmap             # dnf packages the puzzle box needs (optional)
answer: {{SECRET}}         # "flag" (default) means the FLAG{...}; or an expression (case-insensitive compare)
var: SECRET = word         # one per line, same specs as scenario vars

@intro
Shown to the player (plain text).

@vars SECRET=...           # extra variables when another variable took a value (variants)

@setup
A bash script, run as root; the player then logs in as the user "player" (uid 1000, home /home/player), or as the
`login:` account (its home is /home/<login>).

@web
[ { "path": "/", "kind": "idor", ... } ]   # optional JSON list of pretend web apps on the puzzle box (10.99.0.10)

@solution
The model solution, typed into the player's console. "#wait N", "#reset" and "#key ..." work as RhelSim.Type.

@walkthrough
The intended path, shown only after the player finishes or gives up.

@hint 5
A hint. The number after @hint is its point cost. Several @hint sections, revealed one at a time, hidden until asked.
Beginner puzzles want at least two, from "what to think about" to near-step-by-step with the why; Expert ones short.
```

### Windows puzzles (`host: windows`)

A header line `host: windows` adds a pretend Windows box, WINBOX (10.99.0.20, backed by the PowerShell course's
simulator), next to the puzzle box. Its account `player` is a standard user (`winadmin: yes` makes it an
administrator). The console opens a remote PowerShell session to it straight away (`winrm-shell -i winbox -u player
...`; the password is in `~/winbox.txt` on the puzzle box if the player exits). Sections:

```
@winsetup
PowerShell run as the administrator on WINBOX (registry, files, streams, services, scheduled tasks...)

@winevents
[ { "log": "Security", "id": 4625, "ago": "20m", "repeat": 15, "every": 20, "message": "..." } ]   # as a machine's "events"
```

`@setup` may then be empty. The solution's lines are typed into the PowerShell session.

### Linux Ladder (`category: ladder`)

Twenty steps (`ladder-01.txt` ... `ladder-20.txt`), climbed in order: `order:` is the step number, the title starts
`Step N:`, `login: stepN`, and steps 1-5 are Beginner, 6-10 Intermediate, 11-15 Advanced, 16-20 Expert (the level
list shows them in step order). Each step's answer is a password made from the seed (`answer: {{PW}}`, `var: PW =
hex:10`), and its `@setup` creates the next account with it (`useradd -m step(N+1)` and `chpasswd`), so the
password really opens the next account (`su - stepN+1`). One idea per step, aimed at complete beginners: the intro
says exactly what to look for, and the Beginner hints explain every command for someone who has never used one. The
password must never appear in the intro, teaser or hints (the tests check, for three seeds). Progress is kept per
step like every puzzle (the same progress.json, version 2). The box shows a ladder banner instead of the attack box's.

## Tools the scenarios can use

Besides the RHEL commands Cert Lab already simulates (nmap, ssh, scp, curl, openssl, hashers...), the range adds, on
the player's box, simulated programs with manual pages: `smbclient`, `winrm-shell`, `ftp`, `mysql`, `hashcrack`,
`pwguess`, `webscan`, `vulnlookup`, `ticket-request`, for code puzzles `xorcat`, `vigenere`, `morse`, and:

- `catcher` - the practice listening page catcher.range.example: shows what pretend browsers and mails sent there
  (stolen cookies in the Web Security Lab); `catcher --clear` forgets them
- `browse` - a tiny text-only pretend browser: a page the way a person sees it, with its links and forms
- `nc` - connects to a port and sends it lines (a `line` service answers them)
- `siem` - the SOC Analyst's pretend SIEM: `alerts`, `search` with `field=value` / `field!=value` / `field~text`,
  `--fields`, `--count-by`, `--since` / `--until`, `show`, `fields`
- `cloudctl` - the command line of Skyloft Cloud, a pretend cloud provider (data under /var/lib/skyloft)
- `toyvm` - a toy virtual machine of our own (assemble, disassemble, run) for reverse-engineering puzzles

The Linux Ladder also has `ladder-vault` (a shell script its step 17 writes, with a manual page). See each `man` page;
`base32` is an ordinary coreutils program on every machine. Each tool only ever reads and changes the simulator's own
pretend data and talks only to the simulated range network.

## Hints

Every challenge has tiered hints. None are shown until the player presses **Show hint**; each press reveals the next
one only, with its point cost on the button (`Show hint 2 of 4 (-10 points)`). Beginner hints are longer and explain
*why* each command works (for someone brand new); costs are lower, and a **Learning mode** toggle makes them free.
Revealed hints stay visible for the run and are listed in the end report. The tests check the counts: Beginner 4-5 (starting with what to think about, explaining each tool for a newcomer, the last near step by step), Intermediate 3-4, Advanced 2-3, Expert 1-2 short nudges; costs never fall from one hint to the next.

## Testing

`CertLab.ps1 -Test -Only range` builds every scenario and puzzle from several seeds and checks:

- the setup scripts run with no "command not found" and the like;
- no flag is readable from the start box (`grep -r FLAG /` finds nothing; a puzzle's plain flag can't be grepped);
- the model solution captures every flag; blue-team questions are answered by their model answer and fixes pass;
- a defender scenario's quiet solution never raises an alert and scores full points, and its `noisySolution` is caught;
- a blue scenario's attacker reaches its goal when the player does nothing;
- a SOC scenario's SIEM has 100 or more events in time order with nothing left to fill, at least one alert (two above
  Beginner), the model solution's searches print every answer that isn't a verdict or `derived`, no question gives
  its answer away, verdicts have one try, and a wrong verdict closes its question with no points while the rest of
  the run still finishes;
- the Linux Ladder has its twenty steps in order (login, title, level by step number) and the password never shows
  in an intro, teaser or hint; a ladder step's progress is saved and reopens on its own;
- each challenge is played with three seeds (scenarios 11, 4242, 777; puzzles 7, 9001, 31337) plus the first further
  seeds that reach a value of a `variants` key not played yet, so **every variant value is played through**; the seeds
  must give different details;
- the counts per category and level stay at least what the library has (`range-content`);
- every command a solution uses has a manual page; the counts and levels are in range.

Groups: `range-net`, `range-tools`, `range-content`, `range-scenarios`, `range-puzzles`, `range-save`, `range-ui`.
`-Only range-scenarios:<id>` or `range-puzzles:<id>` runs one.
Snapshots: `CertLab.ps1 -Range -Snapshot f.png -Scene RangeHome|RangeCategory|RangeLevel|RangeScenario|RangeTerminal|
RangeMap|RangeDefender|RangeBlue|RangePuzzle|RangeHints|RangeAllHints|RangeWindows|RangeLevelNarrow|RangeNoHints|RangeResults`.
Add `-Course <id>` to pick what the scene shows: a category id for RangeCategory / RangeLevel, a scenario id for
RangeScenario / RangeDefender / RangeBlue (a SOC scenario id works with RangeBlue: it types the first solution lines
and opens the Findings tab), a puzzle id for RangePuzzle (it types the first solution lines; `ladder-08` shows a
ladder step).
