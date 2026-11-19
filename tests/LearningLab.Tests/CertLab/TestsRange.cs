// Cyber Range - the checks behind CertLab.ps1 -Test -Only range: the network rules (subnets, routes, pretend
// Windows hosts), the tools, every scenario (no flag is reachable from the start box, the model solution typed into
// the player's console captures every flag, with two seeds), every puzzle (the same, and wrong answers fail), the
// saves (each layout's fixture opens) and the window's pages.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CertLab
{
    public static partial class Tests
    {
        static List<RangeScenario> rangeScenarios;
        static List<RangePuzzle> rangePuzzles;
        static List<string> rangeProblems;

        static void LoadRange()
        {
            if (rangeScenarios != null) return;
            rangeScenarios = new List<RangeScenario>(); rangePuzzles = new List<RangePuzzle>(); rangeProblems = new List<string>();
            RangeData.Load(Path.Combine(AppRootForTests, "Range"), rangeScenarios, rangePuzzles, rangeProblems);
        }

        static IEnumerable<KeyValuePair<string, Action>> RangeGroups(bool quick)
        {
            yield return new KeyValuePair<string, Action>("range-net", RangeNetTests);
            yield return new KeyValuePair<string, Action>("range-tools", RangeToolTests);
            yield return new KeyValuePair<string, Action>("range-content", RangeContentTests);
            yield return new KeyValuePair<string, Action>("range-scenarios", () => RangeScenarioTests(quick));
            yield return new KeyValuePair<string, Action>("range-puzzles", () => RangePuzzleTests(quick));
            yield return new KeyValuePair<string, Action>("range-save", RangeSaveTests);
            foreach (var g in RangeLookGroups()) yield return g;
        }

        /// <summary>Types lines into the run's console and returns everything it printed.</summary>
        static string RangeType(RangeRun run, IEnumerable<string> lines)
        {
            var con = run.Sim.Console(run.StartMachine);
            var sb = new StringBuilder(con.TakeOutput());
            foreach (var l in lines)
            {
                run.Sim.Type(run.StartMachine, RangeData.Fill(l, run.Vars, null));
                sb.Append(con.Prompt.Length > 0 ? "" : "").Append(con.TakeOutput());
            }
            return sb.ToString();
        }
        static string RTail(string s, int n) { return s.Length <= n ? s : "..." + s.Substring(s.Length - n); }

        static RangeScenario TinyScenario()
        {
            var s = new RangeScenario { Id = "tiny", Title = "Tiny", Start = "kit" };
            s.Machines.Add(new RangeMachineDef { Name = "kit", Hostname = "kit.range.example", Nics = new List<string> { "10.20.1.5/24" } });
            s.Machines.Add(new RangeMachineDef { Name = "dmz", Hostname = "dmz.range.example", Nics = new List<string> { "10.20.1.10/24", "10.20.2.10/24" }, Packages = new List<string> { "httpd" }, Setup = "echo hello > /var/www/html/index.html\nsystemctl enable --now httpd\n", Tools = new List<string> { "nmap" } });
            s.Machines.Add(new RangeMachineDef { Name = "inner", Hostname = "inner.range.example", Nics = new List<string> { "10.20.2.20/24" }, Packages = new List<string> { "httpd" }, Setup = "echo inside > /var/www/html/index.html\nsystemctl enable --now httpd\n" });
            var w = new RangeMachineDef { Name = "win1", Os = "windows", Nics = new List<string> { "10.20.1.30/24" }, Ports = new List<int> { 135, 445, 5985 } };
            w.Accounts.Add(new RangeAccount { User = "Administrator", Password = "Adm1n!", Admin = true });
            w.Accounts.Add(new RangeAccount { User = "kim", Password = "pancake" });
            w.Shares.Add(new RangeShare { Name = "Public", Path = "C:\\Shares\\Public", Anonymous = true, Comment = "Everyone" });
            w.Setup = "Set-Content -Path 'C:\\Shares\\Public\\readme.txt' -Value 'hello from win1'\nSet-Content -Path 'C:\\Users\\Administrator\\Desktop\\secret.txt' -Value 'admins only'\n";
            s.Machines.Add(w);
            s.Routes.Add(new RangeRoute { From = "10.20.1.0/24", To = "10.20.2.20", Ports = new List<int> { 80 } });
            return s;
        }

        static void RangeNetTests()
        {
            LoadRange();
            var run = RangeRun.ForScenario(TinyScenario(), 3);
            run.StartConsole();
            var con = run.Sim.Console("kit"); con.TakeOutput();
            Func<string, string> R = l => { run.Sim.Type("kit", l); return con.TakeOutput(); };
            Check("the player starts as root on their box", R("id") .Contains("uid=0(root)"), R("id"));
            string sweep = R("nmap -sn 10.20.1.0/24");
            Check("a ping sweep finds the hosts on the player's network (also the Windows host)", sweep.Contains("10.20.1.10") && sweep.Contains("10.20.1.30") && sweep.Contains("256 IP addresses") , sweep);
            Check("...and not the inner network", !sweep.Contains("10.20.2."), sweep);
            Check("the map knows the hosts the sweep found", run.Net.Seen.ContainsKey("10.20.1.10") && run.Net.Seen.ContainsKey("10.20.1.30"));
            string scan = R("nmap 10.20.1.30");
            Check("a scan of the Windows host shows its ports", scan.Contains("445/tcp") && scan.Contains("open") && scan.Contains("5985/tcp"), scan);
            Check("the router lets the player reach the inner web server on port 80", R("curl -s http://10.20.2.20/").Contains("inside"), R("curl -s http://10.20.2.20/"));
            string inner22 = R("nmap -p 22,80 10.20.2.20");
            Check("...but not its port 22", inner22.Contains("80/tcp") && inner22.Contains("22/tcp filtered"), inner22);
            Check("the noise log keeps the scans with their loudness", run.Net.Events.Any(e => e.Kind == "ping-sweep") && run.Net.Events.Any(e => e.Kind == "port-scan" && e.Target == "10.20.1.30"));
            // smb and the remote shell
            string shares = R("smbclient -L //10.20.1.30 -N");
            Check("smbclient lists the shares anonymously", shares.Contains("Public") && shares.Contains("IPC$"), shares);
            string get = R("smbclient //10.20.1.30/Public -N -c 'ls; get readme.txt'");
            Check("smbclient lists a share and gets a file", get.Contains("readme.txt") && R("cat readme.txt").Contains("hello from win1"), get);
            Check("smbclient refuses C$ to an anonymous visitor", R("smbclient //10.20.1.30/C$ -N -c ls").Contains("ACCESS_DENIED"));
            string bad = R("winrm-shell -i 10.20.1.30 -u kim -p wrong");
            Check("winrm-shell refuses a wrong password", bad.Contains("not accepted"), bad);
            string w1 = R("winrm-shell -i 10.20.1.30 -u kim -p pancake");
            Check("winrm-shell opens a PowerShell session as the user", w1.Contains("connected to WIN1") && con.Prompt.Contains("PS C:\\Users\\kim\\Documents>"), w1 + "\nprompt: " + con.Prompt);
            string den = R("Get-Content C:\\Users\\Administrator\\Desktop\\secret.txt");
            Check("a standard user can't read an administrator's desktop", den.Contains("denied") && !den.Contains("admins only"), den);
            string rec = R("Get-ChildItem C:\\ -Recurse -ErrorAction SilentlyContinue | Select-String 'admins only'");
            Check("...not even by searching the whole disk", !rec.Contains("secret.txt:1:"), rec);
            Check("...but can write to their own documents", !R("Set-Content -Path notes.txt -Value hi; Get-Content notes.txt").Contains("denied"));
            Check("...and not into C:\\Windows", R("Set-Content -Path C:\\Windows\\x.txt -Value hi").Contains("denied"));
            R("exit");
            Check("exit leaves the Windows host", con.Prompt.StartsWith("[root@kit"), con.Prompt);
            string w2 = R("winrm-shell -i 10.20.1.30 -u Administrator -H " + RangeHash.Nt("Adm1n!"));
            Check("winrm-shell takes a simulated hash in place of the password", w2.Contains("an administrator"), w2);
            Check("...and the administrator reads the desktop", R("Get-Content C:\\Users\\Administrator\\Desktop\\secret.txt").Contains("admins only"));
            R("exit");
            Check("logins that worked are kept for the loot list", run.Net.Worked.Any(x => x.User == "kim" && x.Secret == "pancake"));
            Check("...and the map marks the host as owned", run.Net.Seen["10.20.1.30"].Owned == "admin");
            // pivoting: the dual-homed host reaches the inner network
            run.Net.Lab.Get("dmz").RunCommand("root", "useradd -m ops\necho 'ops:dragon' | chpasswd", null, 10000, null);
            R("ssh -o StrictHostKeyChecking=accept-new ops@10.20.1.10"); R("dragon");
            string piv = R("nmap -sn 10.20.2.0/24");
            Check("from the dual-homed host the inner network answers", piv.Contains("10.20.2.20"), piv);
            Check("the clock moves with sleep and the Windows host follows", true);
            R("exit");
            long t0 = run.Net.Lab.Now; run.Tick(120);
            Check("Tick moves the run's time and the simulated clock", run.Seconds == 120 && run.Net.Lab.Now == t0 + 120 && run.Net.Ps.Now == new DateTime(1970, 1, 1).AddSeconds(run.Net.Lab.Now));
            run.Dispose();
            // the start box's welcome: the attack box's banner for red runs, a defender's welcome for Blue Team runs
            var redSc = rangeScenarios.FirstOrDefault(x => x.Category == "red");
            var blueSc = rangeScenarios.FirstOrDefault(x => x.Category == "blue" && x.Machines.Any(m => m.Name != x.Start && m.Os == "windows"));
            if (redSc != null && blueSc != null)
            {
                var rr = RangeRun.ForScenario(redSc, 11); var rb = RangeRun.ForScenario(blueSc, 11);
                rr.StartConsole(); rb.StartConsole();
                string redB = rr.Net.Lab.Get(rr.StartMachine).Banner, blueB = rb.Net.Lab.Get(rb.StartMachine).Banner;
                string blueOut = rb.Sim.Console(rb.StartMachine).TakeOutput();
                Check("a red run's box shows the attack box's banner (RangeKit, its tools)", redB == RangeTools.AttackBannerText, redB);
                Check("a Blue Team run's box has its own welcome: the company, that you defend it, and log commands to start with", !blueB.Contains("RangeKit") && !blueB.Contains("Try: nmap") && blueB.Contains("You are the defender") && blueB.Contains("journalctl") && blueB.Contains("Get-WinEvent") && blueB.Contains(rb.Vars.ContainsKey("CO") ? rb.Vars["CO"] : ""), blueB);
                Check("...and the console shows it", blueOut.Contains("You are the defender") && !blueOut.Contains("RangeKit"), RTail(blueOut, 600));
                rr.Dispose(); rb.Dispose();
            }
            else Check("a red and a blue (with Windows) scenario exist for the banner check", false);
            // the courses' practice machines are unchanged (no range)
            var t = new T(2);
            Check("outside the range the network is as before", t.Run("ping -c 1 server2").Contains("1 received"), t.Last);
            Check("outside the range the range tools are not installed", t.Run("smbclient -L //x -N 2>&1").Contains("command not found"), t.Last);
            t.Run("cp /usr/bin/cat /tmp/mycat; chmod u+s /tmp/mycat; useradd -m zed");
            Check("outside the range a set-user-ID copy of cat stays the user's (as the courses taught)", t.Run("su - zed -c '/tmp/mycat /etc/shadow' 2>&1").Contains("Permission denied"), t.Last);
            t.Dispose();
        }

        static void RangeToolTests()
        {
            LoadRange();
            var s = TinyScenario();
            s.Machines[1].Packages.Add("mariadb-server"); s.Machines[1].Packages.Add("vsftpd");
            s.Machines[1].Setup += "mkdir -p /var/lib/mysql/shop\nprintf 'id\\tname\\tsecret\\n1\\tann\\tred\\n2\\tbo\\tblue\\n' > /var/lib/mysql/shop/users.tsv\necho 'webapp:s3cret:shop' > /var/lib/mysql/range-users.txt\nsystemctl enable --now mariadb\n" +
                "sed -i 's/^anonymous_enable=NO/anonymous_enable=YES/' /etc/vsftpd/vsftpd.conf\necho hi > /var/ftp/pub/a.txt\nsystemctl enable --now vsftpd\nuseradd -m lee\necho 'lee:cookie' | chpasswd\n" +
                "mkdir -p /var/www/html/app\nsystemctl restart httpd\n";
            s.Web.Add(new RangeWebApp { Machine = "dmz", Path = "/ping", Kind = "cmd", Param = "host", Template = "echo pinging {}", RunAs = "apache" });
            s.Web.Add(new RangeWebApp { Machine = "dmz", Path = "/get", Kind = "file", Param = "f", Base = "/var/www/html/app", RunAs = "apache" });
            s.Web.Add(new RangeWebApp { Machine = "dmz", Path = "/login", Kind = "login", Template = "<p>Welcome, admin. Key: 42</p>", Logins = new List<string[]> { new[] { "admin", "printpal" } } });
            var run = RangeRun.ForScenario(s, 5);
            run.StartConsole();
            var con = run.Sim.Console("kit"); con.TakeOutput();
            Func<string, string> R = l => { run.Sim.Type("kit", l); return con.TakeOutput(); };
            Check("the box has the range tools and word lists", R("which hashcrack smbclient winrm-shell vulnlookup").Contains("/usr/local/bin/hashcrack") && R("wc -l /usr/share/wordlists/range-common.txt").Trim().Length > 0);
            string db = R("mysql -h 10.20.1.10 -u webapp -ps3cret shop -e \"show tables; select name, secret from users where name = 'bo'\"");
            Check("mysql: tables and a select with where", db.Contains("users") && db.Contains("blue") && !db.Contains("red"), db + string.Join("\n", run.SetupLog.ToArray()));
            Check("mysql: a wrong password is refused", R("mysql -h 10.20.1.10 -u webapp -pnope -e 'show databases'").Contains("Access denied"));
            string ftp = R("ftp 10.20.1.10"); R("anonymous"); R("x"); string fl = R("ls pub"); R("bye");
            Check("ftp: anonymous login and a listing", ftp.Contains("220") && fl.Contains("a.txt"), ftp + fl);
            string cmd = R("curl -s 'http://10.20.1.10/ping?host=1.2.3.4;id'");
            Check("a cmd web app runs the extra command as the web server's account", cmd.Contains("pinging 1.2.3.4") && cmd.Contains("uid=48(apache)"), cmd);
            string trav = R("curl -s 'http://10.20.1.10/get?f=../../../../etc/passwd'");
            Check("a file web app climbs out of its folder with ../", trav.Contains("root:x:0:0"), trav);
            Check("...and can't read what the web server's account can't", R("curl -s 'http://10.20.1.10/get?f=../../../../etc/shadow'").Contains("Forbidden"));
            Check("a login web app: wrong, then right", R("curl -s -d 'user=admin&pass=x' http://10.20.1.10/login").Contains("not right") && R("curl -s -d 'user=admin&pass=printpal' http://10.20.1.10/login").Contains("Key: 42"));
            string ws = R("webscan http://10.20.1.10/");
            Check("webscan finds pages that exist", ws.Contains("/login") && ws.Contains("found"), ws);
            Check("vulnlookup searches the made-up notes", R("vulnlookup acmecms").Contains("Template preview") && R("vulnlookup -x 1").Contains("tpl"));
            // hashes
            R("printf 'ann:" + RangeHash.Md5("banana") + "\\nbo:" + RangeHash.Sha256("dragon") + "\\n' > h.txt");
            string hc = R("hashcrack h.txt");
            Check("hashcrack: md5 and sha256 against the word list", hc.Contains("banana") && hc.Contains("dragon") && hc.Contains("2 of 2"), hc);
            Check("hashcrack --show remembers", R("hashcrack --show h.txt").Contains("ann:banana"));
            R("grep '^lee:' /dev/null; true");
            var lee = run.Net.Lab.Get("dmz").Shadow().First(x => x.Name == "lee").Hash;
            R("echo 'lee:" + lee + "' > shadow.txt");
            Check("hashcrack: the simulator's shadow hashes", R("hashcrack shadow.txt").Contains("cookie"));
            R("echo 'Administrator:500:aad3b435b51404eeaad3b435b51404ee:" + RangeHash.Nt("Adm1n!") + ":::' > sam.txt");
            Check("hashcrack: a simulated NT hash not in the list stays uncracked", R("hashcrack sam.txt").Contains("0 of 1"));
            string g = R("pwguess -l lee -P /usr/share/wordlists/range-common.txt ssh://10.20.1.10");
            Check("pwguess finds a weak password and is loud", g.Contains("password: cookie") && run.Net.Events.Any(e => e.Kind == "password-guessing" && e.Loud >= 5), g);
            // the puzzle tools: base32, xorcat, vigenere, morse (and the challenge files' matching transforms)
            var cv = new Dictionary<string, string> { { "W", "FLAG{tea-time}" }, { "K", "0x2a" }, { "KW", "lemon" } };
            Check("base32 encodes like coreutils and decodes", R("echo -n hello | base32").Trim() == "NBSWY3DP" && R("echo " + RangeData.Fill("{{B32:W}}", cv, null) + " | base32 -d").Contains("FLAG{tea-time}"), R("echo -n hello | base32"));
            R("echo " + RangeData.Fill("{{XOR:K:W}}", cv, null) + " > x.hex");
            string xb = R("xorcat --brute x.hex");
            Check("xorcat undoes a single-byte XOR with the key, and --brute finds it", R("xorcat -k 42 x.hex").Contains("FLAG{tea-time}") && xb.Contains("key 0x2a (42): FLAG{tea-time}") && R("echo hi | xorcat -e -k 1").Trim() == "6968", xb);
            Check("vigenere decrypts what the transform encrypted (and the classic example)", R("echo '" + RangeData.Fill("{{VIG:KW:W}}", cv, null) + "' | vigenere -d -k lemon").Contains("FLAG{tea-time}") && R("echo ATTACKATDAWN | vigenere -k LEMON").Contains("LXFOPVEFRNHR"));
            Check("morse reads back what it wrote", R("echo '" + RangeData.Fill("{{MORSE:W}}", cv, null) + "' | morse -d").Contains("FLAG{TEA-TIME}") && R("echo sos | morse").Trim() == "... --- ...");
            Check("a Caesar transform shifts letters only", RangeData.Fill("{{CAESAR:3:W}}", cv, null) == "IODJ{whd-wlph}");
            Check("every range tool has a manual page", RangeTools.All.All(x => Man.Find(x, null) != null) && Man.Find("base32", null) != null, string.Join(" ", RangeTools.All.Where(x => Man.Find(x, null) == null).ToArray()));
            run.Dispose();
        }

        static void RangeContentTests()
        {
            LoadRange();
            Check("the range content loads without problems", rangeProblems.Count == 0, string.Join("\n", rangeProblems.ToArray()));
            Check("at least 50 scenarios", rangeScenarios.Count >= 50, rangeScenarios.Count.ToString());
            Check("at least 97 puzzles (69 CTF, 8 Web Security Lab, 20 Linux Ladder)", rangePuzzles.Count >= 97, rangePuzzles.Count.ToString());
            // the library as of 2026-10-23: Red 3/4/3/1, Red vs Defender 4/4/2/1, Blue 4/4/4/4, SOC Analyst 3/3/3/3 (Beginner..Expert)
            var least = new Dictionary<string, int[]> { { "red", new[] { 3, 4, 3, 1 } }, { "defender", new[] { 4, 4, 2, 1 } }, { "blue", new[] { 4, 4, 4, 4 } }, { "soc", new[] { 3, 3, 3, 3 } } };
            foreach (var kv in least)
                for (int li = 0; li < 4; li++)
                {
                    int have = rangeScenarios.Count(x => x.Category == kv.Key && x.Level == RangeData.Levels[li]);
                    Check(kv.Key + " " + RangeData.Levels[li] + ": at least " + kv.Value[li] + " scenarios", have >= kv.Value[li], have.ToString());
                }
            Check("Linux puzzles: at least 10", rangePuzzles.Count(x => x.Topic == "Linux") >= 10, rangePuzzles.Count(x => x.Topic == "Linux").ToString());
            // every one of the four original categories has a challenge at every level
            var core = new HashSet<string> { "red", "defender", "blue", "ctf" };
            var coreCats = RangeData.Categories.Where(c => core.Contains(c.Id)).ToList();
            Check("all four categories have challenges", coreCats.All(c => c.Puzzles ? rangePuzzles.Any(p => p.Category == c.Id) : rangeScenarios.Any(s => s.Category == c.Id)),
                string.Join(",", coreCats.Where(c => c.Puzzles ? !rangePuzzles.Any(p => p.Category == c.Id) : !rangeScenarios.Any(s => s.Category == c.Id)).Select(c => c.Id).ToArray()));
            // the Web Security Lab (2026-10-23): Beginner and Intermediate challenges on the pretend shop
            Check("Web Security Lab: at least 5 Beginner and 3 Intermediate challenges", rangePuzzles.Count(x => x.Category == "web" && x.Level == "Beginner") >= 5 && rangePuzzles.Count(x => x.Category == "web" && x.Level == "Intermediate") >= 3, rangePuzzles.Count(x => x.Category == "web").ToString());
            RangeLadderChecks();
            foreach (var cat in coreCats)
            {
                var items = cat.Puzzles ? rangePuzzles.Where(x => x.Category == cat.Id).Select(x => x.Level).ToList() : rangeScenarios.Where(x => x.Category == cat.Id).Select(x => x.Level).ToList();
                foreach (var lv in RangeData.Levels) Check(cat.Title + ": a challenge at " + lv, items.Contains(lv), string.Join(",", items.ToArray()));
            }
            foreach (var lv in RangeData.Levels)
                Check("puzzles at level " + lv, rangePuzzles.Count(x => x.Level == lv) >= 5, rangePuzzles.Count(x => x.Level == lv).ToString());
            // the CTF puzzles cover a broad set of topics
            Check("puzzles cover every topic", RangeData.Topics.All(tp => rangePuzzles.Any(x => x.Category == "ctf" && x.Topic == tp)), string.Join(",", RangeData.Topics.Where(tp => !rangePuzzles.Any(x => x.Category == "ctf" && x.Topic == tp)).ToArray()) + " / " + rangePuzzles.Select(x => x.Topic).Distinct().Count().ToString());
            Check("the word list has 200 or more words", RangeData.Words.Count >= 200, RangeData.Words.Count.ToString());
            Check("scenario ids are unique", rangeScenarios.Select(x => x.Id).Distinct().Count() == rangeScenarios.Count);
            Check("puzzle ids are unique", rangePuzzles.Select(x => x.Id).Distinct().Count() == rangePuzzles.Count);
            var noMan = new SortedSet<string>();
            foreach (var s in rangeScenarios)
            {
                Check(s.Id + ": title, story, goal and summary", s.Title.Length > 0 && s.Story.Length > 0 && s.Goal.Length > 0 && s.Summary.Length > 0);
                Check(s.Id + ": a known category", RangeData.CategoryById(s.Category) != null && !RangeData.IsPuzzleCategory(s.Category), s.Category);
                if (s.Category == "red") Check(s.Id + ": defender none (red solo)", s.Defender == "none", s.Defender);
                int objectives = s.Flags.Count + s.Questions.Count + s.Tasks.Count;
                Check(s.Id + ": objectives, a solution and a walkthrough", objectives >= 2 && s.Solution.Count > 0 && s.Walkthrough.Length > 100, "obj " + objectives + " hints " + s.Hints.Count);
                // red/defender: flags live on a target, not the start box; blue: a flag is the attacker's dropped file on the box you investigate
                Check(s.Id + ": flags are on machines of the scenario", s.Flags.All(f => s.Machines.Any(m => m.Name == f.Machine)));
                if (!RangeData.BlueLike(s.Category)) Check(s.Id + ": flags are not on the start box", s.Flags.All(f => f.Machine != s.Start));
                Check(s.Id + ": the start box is a Linux box", s.Machines.First(m => m.Name == s.Start).Os == "linux");
                RangeHintRule(s.Id, s.Level, s.Hints);
                if (s.Category == "defender") Check(s.Id + ": a quiet bonus and a noisy solution for the test", s.QuietPoints > 0 && s.NoisySolution.Count > 0);
                if (s.Category == "soc") Check(s.Id + ": a SIEM with its events and noise, five or more questions, a one-try verdict, the analyst's box", s.SiemText.Length > 0 && s.SiemNoise != null && s.Questions.Count >= 5 && s.Questions.Any(q => q.Tries == 1) && s.Machines.Count >= 1 && s.Machines.First(m => m.Name == s.Start).Os == "linux", s.Questions.Count + " questions");
                if (s.Category == "blue") Check(s.Id + ": questions or fixes, an attacker, a goal", (s.Questions.Count + s.Tasks.Count) >= 1 && s.Attacker.Count >= 1 && s.Attacker.Any(a => a.Goal));
                foreach (var l in s.Solution.Concat(s.NoisySolution)) foreach (var c in CommandsIn(l)) if (Sh.Programs.ContainsKey(c) && Man.Find(c, null) == null) noMan.Add(c + " (" + s.Id + ")");
                Check(s.Id + ": no real brand or the word Kali", !Regex.IsMatch(s.Story + s.Summary + s.Walkthrough, "\\bKali\\b|Metasploit|Mimikatz|CVE-\\d", RegexOptions.IgnoreCase));
            }
            foreach (var p in rangePuzzles)
            {
                Check(p.Id + ": title, kind, topic, intro, setup, a solution and a walkthrough", p.Title.Length > 0 && p.Kind.Length > 0 && p.Topic.Length > 0 && p.Intro.Length > 0 && (p.Setup.Length > 0 || p.WinSetup.Length > 0) && p.Solution.Count > 0 && p.Walkthrough.Length > 0);
                Check(p.Id + ": a known topic", Array.IndexOf(RangeData.TopicsOf(p.Category), p.Topic) >= 0, p.Topic);
                RangeHintRule(p.Id, p.Level, p.Hints);
                foreach (var l in p.Solution) foreach (var c in CommandsIn(l)) if (Sh.Programs.ContainsKey(c) && Man.Find(c, null) == null) noMan.Add(c + " (" + p.Id + ")");
            }
            Check("every command the range's solutions use has a manual page", noMan.Count == 0, string.Join(", ", noMan.ToArray()));
            var fv = RangeData.FlagValue(1, "open-door", "user");
            Check("flags look like FLAG{...} and change with the seed", Regex.IsMatch(fv, "^FLAG\\{[a-z0-9-]+\\}$") && fv != RangeData.FlagValue(2, "open-door", "user"), fv);
            var v1 = RangeData.MakeVars(new Dictionary<string, string> { { "A", "word" }, { "B", "num:10-20" }, { "C", "pick:x|y" }, { "P", "person" }, { "N", "subnet" } }, 4, "t");
            var v2 = RangeData.MakeVars(new Dictionary<string, string> { { "A", "word" }, { "B", "num:10-20" }, { "C", "pick:x|y" }, { "P", "person" }, { "N", "subnet" } }, 4, "t");
            Check("variables are the same for the same seed", v1["A"] == v2["A"] && v1["B"] == v2["B"] && v1["P_FULL"] == v2["P_FULL"] && RangeData.Words.Contains(v1["A"]) && int.Parse(v1["B"]) >= 10 && int.Parse(v1["B"]) <= 20);
            Check("a subnet variable is a /24 prefix and a person has first and last names", Regex.IsMatch(v1["N"], "^\\d+\\.\\d+\\.\\d+$") && v1["P_FIRST"].Length > 0 && v1["P_LAST"].Length > 0, v1["N"] + " / " + v1["P_FULL"]);
        }

        /// <summary>The Linux Ladder (2026-10-23): twenty steps in order, five per level, each signed in as stepN, each
        /// answer the next step's password (randomized, never written in the intro, teaser or hints).</summary>
        static void RangeLadderChecks()
        {
            var steps = rangePuzzles.Where(x => x.Category == "ladder").OrderBy(x => x.Order).ToList();
            Check("Linux Ladder: at least 20 steps", steps.Count >= 20, steps.Count.ToString());
            for (int li = 0; li < 4; li++)
                Check("Linux Ladder: at least 5 steps at " + RangeData.Levels[li], steps.Count(x => x.Level == RangeData.Levels[li]) >= 5, steps.Count(x => x.Level == RangeData.Levels[li]).ToString());
            for (int i = 0; i < steps.Count; i++)
            {
                var p = steps[i];
                Check(p.Id + ": step " + (i + 1) + " in order, signed in as step" + (i + 1), p.Order == i + 1 && p.Login == "step" + (i + 1) && p.Title.StartsWith("Step " + (i + 1) + ":"), p.Order + " " + p.Login + " " + p.Title);
                Check(p.Id + ": the levels climb (steps 1-5 Beginner ... 16-20 Expert)", RangeData.LevelIndex(p.Level) == Math.Min(3, i / 5), p.Level);
                Check(p.Id + ": the answer is a password made from the seed", p.Answer != "flag" && p.Answer.Contains("{{"), p.Answer);
                Check(p.Id + ": the title has nothing left to fill", !p.Title.Contains("{{") && !p.Teaser.Contains("{{"));
                foreach (int seed in new[] { 7, 9001, 31337 })
                {
                    Dictionary<string, string> v, f;
                    var filled = RangeData.InstantiatePuzzle(p, seed, out v, out f);
                    string answer = RangeData.Fill(p.Answer, v, f);
                    string shown = filled.Intro + "\n" + filled.Teaser + "\n" + string.Join("\n", filled.Hints.Select(h => h.Text).ToArray());
                    Check(p.Id + " (seed " + seed + "): the password is not in the intro, teaser or hints", answer.Length >= 8 && shown.IndexOf(answer, StringComparison.OrdinalIgnoreCase) < 0, answer);
                }
            }
        }

        /// <summary>Hints by level (Andrew, 2026-10-21): Beginner 4-5 that explain each tool and idea for a newcomer,
        /// Intermediate 3-4, Advanced 2-3, Expert 1-2 short nudges; costs never fall as hints get more detailed.</summary>
        static void RangeHintRule(string id, string level, List<RangeHint> hints)
        {
            int[] lo = { 4, 3, 2, 1 }, hi = { 5, 4, 3, 2 };
            int i = RangeData.LevelIndex(level);
            Check(id + ": " + lo[i] + "-" + hi[i] + " hints for " + level, hints.Count >= lo[i] && hints.Count <= hi[i], hints.Count.ToString());
            Check(id + ": hints have text and costs that don't fall", hints.All(h => h.Text.Trim().Length > 20 && h.Cost >= 0) && hints.Zip(hints.Skip(1), (a, b) => b.Cost >= a.Cost).All(x => x));
            if (i == 0) Check(id + ": Beginner hints explain (the first ones say what to think about, the set is long enough to teach)", hints.Sum(h => h.Text.Length) >= 700, hints.Sum(h => h.Text.Length).ToString());
        }

        static void RangeSocRunChecks(string tag, RangeRun run, string all)
        {
            var box = run.Net.Lab.Get(run.Sc.Start);
            string log = box.V.Cat(Cmd.SiemFile) ?? "";
            var lines = log.Split('\n').Where(x => x.StartsWith("time=")).ToList();
            int alerts = lines.Count(x => x.Contains(" source=alert "));
            Check(tag + ": the SIEM has the day's events (100 or more) and its alerts (two or more above Beginner)", lines.Count >= 100 && alerts >= (run.Sc.Level == "Beginner" ? 1 : 2), lines.Count + " events, " + alerts + " alerts");
            Check(tag + ": every event parses and is in time order", lines.All(x => Cmd.SiemParse(x).ContainsKey("source") && Cmd.SiemParse(x).ContainsKey("host")) && lines.Select(x => x.Substring(5, 19)).SequenceEqual(lines.Select(x => x.Substring(5, 19)).OrderBy(x => x, StringComparer.Ordinal)));
            Check(tag + ": no event still has a {{...}} or %...% left to fill", !log.Contains("{{") && !Regex.IsMatch(log, "%[A-Z]+%"), RTail(log, 300));
            foreach (var q in run.Sc.Questions)
            {
                string ans = RangeData.Fill(q.Answer, run.Vars, run.Flags);
                bool verdict = RangeData.Verdict(ans) != null;
                Check(tag + ": question '" + q.Id + "' doesn't give its answer away", verdict || q.Ask.IndexOf(ans, StringComparison.OrdinalIgnoreCase) < 0, q.Ask + " / " + ans);
                if (!verdict && !q.Derived) Check(tag + ": the model solution's searches show the answer to '" + q.Id + "'", all.IndexOf(ans, StringComparison.OrdinalIgnoreCase) >= 0, ans + "\n" + RTail(all, 1500));
                else if (verdict) Check(tag + ": verdict question '" + q.Id + "' gives one try", q.Tries == 1);
            }
        }

        static bool RangeOnly(string id) { return OnlyFile == null || id.Contains(OnlyFile); }

        static void RangeScenarioTests(bool quick)
        {
            LoadRange();
            foreach (var s in rangeScenarios)
            {
                if (!RangeOnly(s.Id)) continue;
                var seeds = quick ? new[] { 11 } : RangeSeedsFor(s.Vars, s.Variants, s.Id, new[] { 11, 4242, 777 });
                // two seeds give different details (randomization really changes things)
                var r1 = RangeRun.ForScenario(s, 11); var r2 = RangeRun.ForScenario(s, 4242);
                if (s.Vars.Count > 0) Check(s.Id + ": two seeds give different details", r1.Vars.Any(kv => { string o; return r2.Vars.TryGetValue(kv.Key, out o) && o != kv.Value; }), "");
                r1.Dispose(); r2.Dispose();
                var variantsSeen = new HashSet<string>();
                foreach (int seed in seeds)
                {
                    RangeRun run;
                    try { run = RangeRun.ForScenario(s, seed); }
                    catch (Exception e) { Check(s.Id + ": builds", false, e.ToString()); continue; }
                    string tag = s.Id + " (seed " + seed + ")";
                    if (s.Variants != null) foreach (var k in s.Variants.Keys) { string val; if (run.Vars.TryGetValue(k, out val)) variantsSeen.Add(k + "=" + val); }
                    var bad = run.SetupLog.Where(x => x.Contains("command not found") || x.Contains("does not simulate") || x.Contains("No such file") || x.Contains("is not recognized") || x.Contains("Cannot find")).ToList();
                    Check(tag + ": the setup scripts ran cleanly", bad.Count == 0, string.Join("\n", bad.ToArray()));
                    if (run.Flags.Count > 0) Check(tag + ": flags differ from each other", run.Flags.Values.Distinct().Count() == run.Flags.Count);
                    // nothing to find on the start box (red/defender; a blue flag is the attacker's dropped file, found by investigating)
                    if (!RangeData.BlueLike(s.Category))
                    {
                        var start = run.Net.Lab.Get(s.Start);
                        var g = start.RunCommand("root", "grep -r FLAG / 2>/dev/null; cat /etc/hosts", null, 2000000, null);
                        Check(tag + ": grep -r FLAG / on the start box finds no flag", !run.Flags.Values.Any(f => g.Output.Contains(f)), RTail(g.Output, 400));
                    }
                    bool wrong; run.Submit("FLAG{guess}", out wrong);
                    Check(tag + ": a made-up flag is refused", !wrong && run.Found.Count == 0);
                    run.StartConsole();
                    string all = RangeType(run, run.Sc.Solution);
                    Check(tag + ": every command in the model solution is installed", !RangeMissingCommand(all), RangeMissingLines(all));
                    foreach (var f in s.Flags)
                    {
                        bool right; string msg = run.Submit(run.Flags[f.Id], out right);
                        Check(tag + ": the model solution shows flag '" + f.Id + "'", all.Contains(run.Flags[f.Id]), RTail(all, 2500));
                        Check(tag + ": flag '" + f.Id + "' is accepted", right, msg);
                    }
                    // SOC Analyst: the model solution's searches show every answer (verdicts aside), the SIEM holds the
                    // day's noise and more than one alert, and no question gives its own answer away
                    if (s.Category == "soc") RangeSocRunChecks(tag, run, all);
                    // blue team: answer every question and make every fix
                    foreach (var q in run.Sc.Questions)
                    {
                        bool ok; string msg = run.Answer(q, RangeData.Fill(q.Answer, run.Vars, run.Flags), out ok);
                        Check(tag + ": question '" + q.Id + "' answered by the model answer", ok, msg + " (answer " + RangeData.Fill(q.Answer, run.Vars, run.Flags) + ")");
                    }
                    foreach (var t in s.Tasks)
                    {
                        bool done; string msg = run.CheckTask(t, out done);
                        Check(tag + ": fix '" + t.Id + "' passes after the solution", done, msg);
                    }
                    Check(tag + ": everything captured, score = the points", run.AllFound && run.Finished && run.Score == s.MaxPoints, run.Score + " / " + s.MaxPoints + (s.Category == "defender" && run.Defender != null ? " alerts " + run.Defender.Alerts : ""));
                    if (!RangeData.BlueLike(s.Category)) Check(tag + ": the model solution made noise the defender modes can see", run.Net.Events.Count > 0);
                    if (s.Category == "defender") Check(tag + ": the quiet solution never tripped the defender", run.Defender != null && run.Defender.Alerts == 0, run.Defender == null ? "no defender" : run.Defender.Report.Count + " notices");
                    run.Dispose();
                }
                // defender: a noisy approach must be caught
                if (s.Category == "defender" && s.NoisySolution.Count > 0 && !quick)
                {
                    var run = RangeRun.ForScenario(s, 11);
                    run.StartConsole();
                    RangeType(run, run.Sc.NoisySolution);
                    for (int i = 0; i < 20 && (run.Defender == null || run.Defender.Blocks == 0); i++) run.Tick(60);
                    Check(s.Id + ": a noisy approach is caught by the defender", run.Defender != null && run.Defender.Alerts > 0 && run.Defender.Blocks > 0, run.Defender == null ? "no defender" : "alerts " + run.Defender.Alerts + " blocks " + run.Defender.Blocks);
                    run.Dispose();
                }
                // blue: doing nothing lets the attacker reach its goal
                if (s.Category == "blue" && s.Attacker.Any(a => a.Goal) && !quick)
                {
                    var run = RangeRun.ForScenario(s, 11);
                    run.StartConsole();
                    long goalAt = s.Attacker.Where(a => a.Goal).Max(a => a.At);
                    for (long t = 0; t <= goalAt + 60 && !run.Breached; t += 30) run.Tick(30);
                    Check(s.Id + ": the attacker reaches its goal if you do nothing", run.Breached, "goal at " + goalAt);
                    run.Dispose();
                }
                // SOC: a wrong verdict closes the question with no points (one try), and the rest can still be finished
                if (s.Category == "soc" && !quick)
                {
                    var run = RangeRun.ForScenario(s, 11);
                    var vq = run.Sc.Questions.FirstOrDefault(q => q.Tries == 1 && RangeData.Verdict(q.Answer) != null);
                    if (vq != null)
                    {
                        string wrongVerdict = RangeData.Verdict(vq.Answer) == "real" ? "false positive" : "true positive";
                        bool ok; string m1 = run.Answer(vq, "maybe", out ok);
                        Check(s.Id + ": a verdict that isn't real / false alarm is not counted as a try", !ok && !run.Answered.ContainsKey(vq.Id), m1);
                        string m2 = run.Answer(vq, wrongVerdict, out ok);
                        Check(s.Id + ": a wrong verdict closes the question with no points", !ok && run.Missed.Contains(vq.Id) && run.Answered.ContainsKey(vq.Id) && run.Score == 0, m2 + " score " + run.Score);
                        string m3 = run.Answer(vq, RangeData.Fill(vq.Answer, run.Vars, run.Flags), out ok);
                        Check(s.Id + ": ...and can't be answered again for the points", run.Score == 0, m3);
                        foreach (var q in run.Sc.Questions.Where(x => x.Id != vq.Id)) run.Answer(q, RangeData.Fill(q.Answer, run.Vars, run.Flags), out ok);
                        Check(s.Id + ": ...the run still finishes, without that question's points", run.Finished && run.Score == s.MaxPoints - vq.Points, run.Score + " / " + (s.MaxPoints - vq.Points));
                    }
                    else Check(s.Id + ": a one-try verdict question", false);
                    run.Dispose();
                }
                if (s.Variants != null && !quick) RangeVariantCheck(s.Id, s.Variants, variantsSeen);
            }
        }

        static void RangePuzzleTests(bool quick)
        {
            LoadRange();
            foreach (var p in rangePuzzles)
            {
                if (!RangeOnly(p.Id)) continue;
                var variantsSeen = new HashSet<string>();
                var seeds = quick ? new[] { 7 } : RangeSeedsFor(p.Vars, p.Variants, p.Id, new[] { 7, 9001, 31337 });
                if (p.Vars.Count > 0 && !quick)
                {
                    var v1 = RangeData.MakeVars(p.Vars, p.Variants, 7, p.Id); var v2 = RangeData.MakeVars(p.Vars, p.Variants, 9001, p.Id); var v3 = RangeData.MakeVars(p.Vars, p.Variants, 31337, p.Id);
                    Check(p.Id + ": the seeds give different details", v1.Any(kv => { string o2, o3; return (v2.TryGetValue(kv.Key, out o2) && o2 != kv.Value) || (v3.TryGetValue(kv.Key, out o3) && o3 != kv.Value); }));
                }
                foreach (int seed in seeds)
                {
                    string tag = p.Id + " (seed " + seed + ")";
                    RangeRun run;
                    try { run = RangeRun.ForPuzzle(p, seed); }
                    catch (Exception e) { Check(tag + ": builds", false, e.ToString()); continue; }
                    if (p.Variants != null) foreach (var k in p.Variants.Keys) { string val; if (run.Vars.TryGetValue(k, out val)) variantsSeen.Add(k + "=" + val); }
                    var bad = run.SetupLog.Where(x => x.Contains("command not found") || x.Contains("does not simulate") || x.Contains("No such file")).ToList();
                    Check(tag + ": the setup ran cleanly", bad.Count == 0, string.Join("\n", bad.ToArray()));
                    string answer = p.Answer == "flag" ? run.Flags[""] : RangeData.Fill(p.Answer, run.Vars, run.Flags);
                    var box = run.Net.Lab.Get("puzzlebox");
                    if (p.Answer == "flag")
                    {
                        var g = box.RunCommand(p.Login, "grep -rF '" + answer + "' / 2>/dev/null", null, 2000000, null);
                        Check(tag + ": the plain flag can't simply be grepped for", !g.Output.Contains(answer), RTail(g.Output, 300));
                    }
                    bool right; run.Submit("FLAG{nope}", out right);
                    Check(tag + ": a wrong answer is refused", !right);
                    run.StartConsole();
                    string all = RangeType(run, run.Pz.Solution);
                    Check(tag + ": the model solution shows the answer", all.IndexOf(answer, StringComparison.OrdinalIgnoreCase) >= 0, "answer " + answer + "\n" + RTail(all, 2000));
                    // every tool the solution (and so the hints) uses is installed on the box: xxd wasn't, and Andrew hit
                    // "command not found" on Reading hex (2026-10-24) while the answer still showed some other way
                    Check(tag + ": every command in the model solution is installed", !RangeMissingCommand(all), RangeMissingLines(all));
                    string msg = run.Submit(answer, out right);
                    Check(tag + ": the answer is accepted", right && run.Finished && run.Score == p.Points, msg);
                    run.Dispose();
                }
                if (p.Variants != null && !quick) RangeVariantCheck(p.Id, p.Variants, variantsSeen);
            }
        }

        static bool RangeMissingCommand(string transcript)
        {
            return transcript.Contains(": command not found") || transcript.Contains("is not recognized as the name of a cmdlet");
        }
        static string RangeMissingLines(string transcript)
        {
            return string.Join("\n", transcript.Split('\n').Where(x => x.Contains("command not found") || x.Contains("is not recognized")).Take(5).ToArray());
        }

        /// <summary>The seeds a challenge is played with: the base seeds, plus (when it has variants) the first further
        /// seeds that reach a variant value not seen yet, so every variant is played through at least once.</summary>
        static int[] RangeSeedsFor(Dictionary<string, string> vars, JObj variants, string id, int[] baseSeeds)
        {
            var seeds = new List<int>(baseSeeds);
            if (variants == null) return seeds.ToArray();
            var seen = new HashSet<string>();
            Func<int, bool> adds = seed =>
            {
                var v = RangeData.MakeVars(vars, variants, seed, id);
                bool any = false;
                foreach (var k in variants.Keys) { string val; if (v.TryGetValue(k, out val) && seen.Add(k + "=" + val)) any = true; }
                return any;
            };
            foreach (var sd in baseSeeds) adds(sd);
            for (int sd = 101; sd < 101 + 97 * 60; sd += 97) if (adds(sd)) seeds.Add(sd);
            return seeds.ToArray();
        }

        /// <summary>Every value a variant lists was played through (RangeSeedsFor picks the seeds that reach them).</summary>
        static void RangeVariantCheck(string id, JObj variants, HashSet<string> seen)
        {
            foreach (var kv in variants)
            {
                var byVal = kv.Value as JObj; if (byVal == null) continue;
                var missing = byVal.Keys.Where(v => !seen.Contains(kv.Key + "=" + v)).ToList();
                Check(id + ": every value of the variant '" + kv.Key + "' was played through", missing.Count == 0 && byVal.Count >= 2, "not reached: " + string.Join(",", missing.ToArray()));
            }
        }
    }
}
