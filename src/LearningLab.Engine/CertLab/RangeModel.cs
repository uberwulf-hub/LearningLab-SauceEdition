// Cyber Range - scenarios and puzzles are data (Range\scenarios\<id>\scenario.json with setup scripts, a model
// solution and a walkthrough; Range\puzzles\<id>.txt), described in Range\FORMAT.md. This file reads them and makes
// a run's details from its seed: the flags (FLAG{...}, different in every run so answers can't simply be shared) and
// the variables (people, companies, addresses, passwords from the built-in word list, which of several equivalent
// weaknesses is present...). A challenge is kept as a template; Instantiate fills it for one run, so setup scripts,
// hints, solutions and walkthroughs all use the same details.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CertLab
{
    public class RangeMachineDef
    {
        public string Name = "", Os = "linux", Hostname = "", Role = "", Setup = "", SetupFile = "", Motd = "", Banner = "";
        public bool Server = true, Firewall;
        public List<string> Nics = new List<string>();
        public List<string> Packages = new List<string>();
        public List<string> Tools = new List<string>();
        public List<int> Ports = new List<int>();
        public Dictionary<int, string> Versions = new Dictionary<int, string>();
        public Dictionary<int, string> PortNames = new Dictionary<int, string>();
        public List<RangeAccount> Accounts = new List<RangeAccount>();
        public List<RangeShare> Shares = new List<RangeShare>();
        public List<string> Private = new List<string>(), Writable = new List<string>(), DomainAdmins = new List<string>();
        public List<RangeWinEvent> Events = new List<RangeWinEvent>();
        public bool DomainJoined;
        public string FirstIp { get { return Nics.Count == 0 ? "" : Nics[0].Split('/')[0]; } }
    }

    /// <summary>A Windows event a scenario writes into a pretend host's log before the run starts (blue team).</summary>
    public class RangeWinEvent
    {
        public string Log = "Security", Provider = "Microsoft-Windows-Security-Auditing", Message = "", User = "";
        public int Id, Level = 4, Repeat = 1, Every = 5;
        public long Ago;     // seconds before the run starts
    }

    /// <summary>Red vs Defender: a scenario's own defender settings (all optional). Count: only these noise kinds count,
    /// each event worth that many points (else every event of loudness 2 or more counts its loudness). Budget, window
    /// and react (seconds from the alert to the block) override the level's; reset / isolate turn those reactions on
    /// or off; watches is a plain sentence for the briefing ("failed sign-ins and new processes").</summary>
    public class RangeDefenderRules
    {
        public Dictionary<string, int> Count = new Dictionary<string, int>(StringComparer.Ordinal);
        public int Budget, Window, React = -1;
        public bool? Reset, Isolate;
        public string Watches = "";
    }

    public class RangeFlagDef { public string Id = "", Name = "", Machine = "", Path = "", Owner = "root", Mode = "600", Level = "user"; public int Points = 100; }
    public class RangeHint { public string Text = ""; public int Cost = 10; }

    /// <summary>Blue team: a question about what happened; answered in the Findings tab.</summary>
    public class RangeQuestion
    {
        public string Id = "", Ask = "", Answer = "", Explain = "";
        public List<string> Accept = new List<string>();
        public int Points = 50;
        public int Tries;                     // answers allowed; when they run out the question closes with no points (0: any number)
        public bool Derived;                  // worked out from what the searches show (an interval, a total), not printed as such
    }
    /// <summary>Blue team: a fix the player must make, checked by a bash script run as root on a machine (exit 0 = done).</summary>
    public class RangeTask { public string Id = "", Name = "", Machine = "", Check = "", Explain = ""; public int Points = 50; }
    /// <summary>Blue team: one step of the simulated attacker, run on the clock unless the player stopped it.</summary>
    public class RangeStep
    {
        public long At;                       // seconds after the run starts
        public string Machine = "", Run = "", Unless = "", Note = "";
        public bool Goal, Optional;
    }

    public class RangeScenario
    {
        public string Id = "", Title = "", Level = "Beginner", Summary = "", Story = "", Goal = "", Defender = "none", Start = "", Dir = "", Walkthrough = "", Skills = "";
        public string Category = "red";       // red | defender | blue
        public string Simple = "";            // optional: the challenge in words a middle schooler follows ("Explain it simply")
        public int Order, Minutes = 30;
        public List<RangeMachineDef> Machines = new List<RangeMachineDef>();
        public List<RangeRoute> Routes = new List<RangeRoute>();
        public List<RangeFlagDef> Flags = new List<RangeFlagDef>();
        public List<RangeHint> Hints = new List<RangeHint>();
        public List<string> Solution = new List<string>();
        public Dictionary<string, string> Vars = new Dictionary<string, string>();
        public JObj Variants;
        public List<RangeWebApp> Web = new List<RangeWebApp>();
        public List<RangeAccount> DomainAccounts = new List<RangeAccount>();
        public string DomainName = "", DomainNetbios = "", DcName = "";
        public List<string> Hosts = new List<string>();      // lines for the start box's /etc/hosts
        // blue team
        public List<RangeQuestion> Questions = new List<RangeQuestion>();
        public List<RangeTask> Tasks = new List<RangeTask>();
        public List<RangeStep> Attacker = new List<RangeStep>();
        public string AttackerIp = "", GoalName = "";
        public int GoalPoints;                // points for stopping the attacker before its goal step
        // red vs defender
        public int QuietPoints;               // the quiet bonus (lost a share per alert)
        public List<string> Watch = new List<string>();      // machines the defender watches (empty: all but the start box)
        public List<string> NoisySolution = new List<string>(); // a loud approach the tests check the defender catches
        public RangeDefenderRules DefenderRules;              // optional: what this defender counts and how it reacts
        // SOC Analyst: the pretend SIEM's events (RangeSoc.cs)
        public string SiemText = "", SiemDay = "", SiemStart = "";
        public JObj SiemNoise;
        // the template
        public JObj Raw;
        public string BaseDir = "";
        public int MaxPoints { get { return Flags.Sum(f => f.Points) + Questions.Sum(q => q.Points) + Tasks.Sum(t => t.Points) + GoalPoints + QuietPoints; } }
        public int ObjectiveCount { get { return Flags.Count + Questions.Count + Tasks.Count; } }
        public string Teaser { get { return Summary; } }
    }

    public class RangePuzzle
    {
        public string Id = "", Title = "", Level = "Beginner", Kind = "", Topic = "", Intro = "", Setup = "", Walkthrough = "", Answer = "flag", File = "", Teaser = "";
        public string Simple = "";            // optional: the puzzle in plain words for beginners ("Explain it simply")
        public int Points = 50, Order, Minutes;   // Minutes: a guide (0 in the file = the level's default)
        public string Category = "ctf";        // a puzzle category of RangeData.Categories: ctf, web or ladder
        public string Login = "player";        // the account the console signs in as (the Linux Ladder: that step's account)
        public List<RangeHint> Hints = new List<RangeHint>();
        public List<string> Solution = new List<string>();
        public List<string> Packages = new List<string>();
        public Dictionary<string, string> Vars = new Dictionary<string, string>();
        public JObj Variants;
        public string WebJson = "";
        public List<RangeWebApp> Web = new List<RangeWebApp>();
        public string Raw = "";
        // a Windows puzzle (host: windows): a pretend Windows box WINBOX the console opens a remote shell to
        public string Host = "linux", WinSetup = "", WinEventsJson = "";
        public bool WinAdmin;
        public List<RangeWinEvent> WinEvents = new List<RangeWinEvent>();
        public bool IsWindows { get { return Host == "windows"; } }
    }

    /// <summary>A top-level group on the home page.</summary>
    public class RangeCategory
    {
        public string Id = "", Title = "", Icon = "", Line = "";
        public string[] LevelLines = new string[4];
        public bool Puzzles;                  // its challenges are puzzle files ("category:" in their header), not scenarios
        public bool Defends;                  // its scenarios put the player on defence (questions, fixes, the Findings tab)
        public string[] Topics;               // a puzzle category's groups on a level page (null: RangeData.Topics)
    }

    public static class RangeData
    {
        public static readonly string[] Levels = { "Beginner", "Intermediate", "Advanced", "Expert" };
        public static List<string> Words = new List<string>();
        public static string WordText = "";
        public static List<string[]> Vulns = new List<string[]>();   // product, versions, title, text
        public static Dictionary<string, List<string>> Pools = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        public static string Dir;

        public static readonly RangeCategory[] Categories =
        {
            new RangeCategory { Id = "red", Title = "Red Team", Icon = "\uE7EF", Line = "Break into an unknown pretend network on your own and capture its flags.",
                LevelLines = new[] { "One machine, one clear path. Every hint explains the why.", "Two or three steps: a share, a web tool, a key left lying around.", "Several machines, chained mistakes, a network you can't reach directly.", "Windows domains and long chains, with only short nudges." } },
            new RangeCategory { Id = "defender", Title = "Red vs Defender", Icon = "\uE83D", Line = "The same kind of break-in, but a pretend defender watches the logs. Stay quiet.",
                LevelLines = new[] { "A slow, forgiving defender who explains what it noticed.", "A defender who blocks noisy addresses after a few minutes.", "A quick defender who also changes passwords and ends sessions.", "A sharp defender who can cut a machine off the network." } },
            new RangeCategory { Id = "blue", Title = "Blue Team", Icon = "\uE72E", Line = "You are the defender: read the logs, find what an attacker did, and clean it up.",
                LevelLines = new[] { "One machine, one clear trail. Hints teach each log and command.", "More noise in the logs and a fix to make before time runs out.", "An attacker still at work, persistence to find and remove.", "Several machines, Windows and Linux, and very little help." }, Defends = true },
            new RangeCategory { Id = "soc", Title = "SOC Analyst", Icon = ((char)0xE9D9).ToString(), Line = "Sit at the security team's desk: the alerts are in, the logs are collected. Real attack or false alarm? Work out what happened.", Defends = true,
                LevelLines = new[] { "One or two alerts and a short trail. Hints explain each log and search.", "A chain across a few events, and a look-alike that is harmless.", "Several machines and sources: follow the trail from start to finish.", "Lots of noise, several alerts, very little help." } },
            new RangeCategory { Id = "web", Title = "Web Security Lab", Icon = ((char)0xE719).ToString(), Line = "One pretend online shop, Copperleaf Market, with a different classic web mistake in every challenge.", Puzzles = true,
                Topics = new[] { "Access control", "Injection", "Sign-ins and sessions", "Server mistakes", "Tricking the browser", "Server-side requests" },
                LevelLines = new[] { "Pages the shop forgot to hide, and numbers you can change.", "Cookies, logins and forms the shop trusts too much.", "Tokens, scripts and requests that go further than they should.", "The shop tried to fix it: get past its filters." } },
            new RangeCategory { Id = "ladder", Title = "Linux Ladder", Icon = ((char)0xE756).ToString(), Line = "Twenty small steps on a pretend Linux box, one command idea each. Every step's answer is the password to the next.", Puzzles = true,
                Topics = new[] { "Climb in order" },
                LevelLines = new[] { "Steps 1-5: reading files, even awkward ones. Hints explain every command.", "Steps 6-10: finding and filtering: find, grep, sort, uniq, head and tail.", "Steps 11-15: hidden text, codes, archives and comparing files.", "Steps 16-20: settings, manuals, keys, scheduled jobs and other people's programs." } },
            new RangeCategory { Id = "ctf", Title = "CTF Puzzles", Icon = "\uE8FB", Line = "Short puzzles: codes, hidden files, web pages, logs, passwords, and more.",
                LevelLines = new[] { "Single steps, with hints that explain every command.", "Two steps, or a tool you may not know yet.", "Several steps chained together.", "Little to go on: the tricky ones." }, Puzzles = true },
        };

        public static readonly string[] Topics = { "Crypto & Encoding", "Steganography", "Forensics", "Log Hunting", "Web", "Linux", "Windows", "Privilege Escalation", "Password Cracking", "Network", "OSINT" };

        /// <summary>A guide to how long a challenge takes when its file doesn't say ("minutes").</summary>
        public static int DefaultMinutes(string level, bool puzzle)
        {
            int i = LevelIndex(level);
            return puzzle ? new[] { 5, 10, 15, 25 }[i] : new[] { 20, 35, 50, 75 }[i];
        }
        /// <summary>A category whose challenges are puzzle files (CTF Puzzles, Web Security Lab, Linux Ladder).</summary>
        public static bool IsPuzzleCategory(string id) { var c = CategoryById(id); return c != null && c.Puzzles; }
        /// <summary>A category whose scenarios put the player on defence (Blue Team, SOC Analyst): questions, fixes, Findings.</summary>
        public static bool BlueLike(string id) { var c = CategoryById(id); return c != null && c.Defends; }
        /// <summary>The topics a puzzle category groups its challenges by.</summary>
        public static string[] TopicsOf(string category) { var c = CategoryById(category); return c == null || c.Topics == null ? Topics : c.Topics; }

        public static int LevelIndex(string l) { int i = Array.IndexOf(Levels, l); return i < 0 ? 0 : i; }
        public static RangeCategory CategoryById(string id) { return Categories.FirstOrDefault(c => c.Id == id); }

        /// <summary>Reads the word list, the pools, the pretend vulnerability notes and every scenario and puzzle.</summary>
        public static void Load(string rangeDir, List<RangeScenario> scenarios, List<RangePuzzle> puzzles, List<string> problems)
        {
            Dir = rangeDir;
            string wl = Path.Combine(rangeDir, "wordlist.txt");
            WordText = File.Exists(wl) ? File.ReadAllText(wl).Replace("\r", "") : "";
            Words = WordText.Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0 && !x.StartsWith("#", StringComparison.Ordinal)).ToList();
            if (!WordText.EndsWith("\n", StringComparison.Ordinal)) WordText += "\n";
            Vulns.Clear();
            string vf = Path.Combine(rangeDir, "vulnnotes.txt");
            if (File.Exists(vf)) Vulns.AddRange(ParseVulns(File.ReadAllText(vf)));
            Pools.Clear();
            string pf = Path.Combine(rangeDir, "pools.txt");
            if (File.Exists(pf))
            {
                List<string> cur = null;
                foreach (var raw in File.ReadAllText(pf).Replace("\r", "").Split('\n'))
                {
                    var l = raw.Trim();
                    if (l.Length == 0 || l.StartsWith("#", StringComparison.Ordinal)) continue;
                    if (l.StartsWith("@", StringComparison.Ordinal)) { cur = new List<string>(); Pools[l.Substring(1)] = cur; continue; }
                    if (cur != null) cur.Add(l);
                }
            }
            string sd = Path.Combine(rangeDir, "scenarios");
            if (Directory.Exists(sd))
                foreach (var d in Directory.GetDirectories(sd).OrderBy(x => x, StringComparer.Ordinal))
                {
                    try { var s = LoadScenario(d); if (s != null) scenarios.Add(s); }
                    catch (Exception e) { if (problems != null) problems.Add(Path.GetFileName(d) + ": " + e.Message); }
                }
            string pd = Path.Combine(rangeDir, "puzzles");
            if (Directory.Exists(pd))
                foreach (var f in Directory.GetFiles(pd, "*.txt").OrderBy(x => x, StringComparer.Ordinal))
                {
                    try { puzzles.Add(LoadPuzzle(f)); }
                    catch (Exception e) { if (problems != null) problems.Add(Path.GetFileName(f) + ": " + e.Message); }
                }
            scenarios.Sort((a, b) => LevelIndex(a.Level) != LevelIndex(b.Level) ? LevelIndex(a.Level).CompareTo(LevelIndex(b.Level)) : a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.CompareOrdinal(a.Id, b.Id));
            puzzles.Sort((a, b) => LevelIndex(a.Level) != LevelIndex(b.Level) ? LevelIndex(a.Level).CompareTo(LevelIndex(b.Level)) : a.Order != b.Order ? a.Order.CompareTo(b.Order) : string.CompareOrdinal(a.Id, b.Id));
        }

        static List<string[]> ParseVulns(string text)
        {
            // blocks: "@ product | versions | title" followed by text lines
            var l = new List<string[]>();
            string[] cur = null; var sb = new StringBuilder();
            foreach (var raw in text.Replace("\r", "").Split('\n'))
            {
                if (raw.StartsWith("@ ", StringComparison.Ordinal))
                {
                    if (cur != null) { cur[3] = sb.ToString().Trim(); l.Add(cur); }
                    var p = raw.Substring(2).Split('|');
                    cur = new[] { p[0].Trim(), p.Length > 1 ? p[1].Trim() : "", p.Length > 2 ? p[2].Trim() : "", "" };
                    sb.Clear();
                    continue;
                }
                if (raw.StartsWith("#", StringComparison.Ordinal) && cur == null) continue;
                if (cur != null) sb.Append(raw).Append('\n');
            }
            if (cur != null) { cur[3] = sb.ToString().Trim(); l.Add(cur); }
            return l;
        }

        static List<int> Ints(List<object> l)
        {
            var r = new List<int>();
            foreach (var x in l)
            {
                double d;
                if (x is double) r.Add((int)Math.Round((double)x));
                else if (double.TryParse(Convert.ToString(x, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) r.Add((int)Math.Round(d));
            }
            return r;
        }
        static List<string> Strs(List<object> l) { return l.Select(x => Convert.ToString(x, CultureInfo.InvariantCulture)).ToList(); }
        static Dictionary<int, string> IntMap(JObj o)
        {
            var d = new Dictionary<int, string>();
            if (o == null) return d;
            foreach (var kv in o) { int k; if (int.TryParse(kv.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out k)) d[k] = Convert.ToString(kv.Value, CultureInfo.InvariantCulture); }
            return d;
        }
        static List<RangeAccount> Accounts(List<object> l)
        {
            return l.OfType<JObj>().Select(a => new RangeAccount { User = a.Str("user"), Password = a.Str("password"), Admin = a.Bool("admin", false), Remote = a.Bool("remote", true), Spn = a.Str("spn") }).ToList();
        }
        static List<RangeHint> Hints(List<object> l)
        {
            var r = new List<RangeHint>();
            foreach (var o in l)
            {
                var j = o as JObj;
                if (j != null) r.Add(new RangeHint { Text = j.Str("text"), Cost = j.Int("cost", 10) });
                else if (o is string) r.Add(new RangeHint { Text = (string)o, Cost = 10 });
            }
            return r;
        }
        static string ReadRel(string dir, string baseDir, string file)
        {
            if (file.Length == 0) return "";
            string p = Path.Combine(dir, file);
            if (!File.Exists(p) && baseDir.Length > 0) p = Path.Combine(baseDir, file);
            return File.ReadAllText(p).Replace("\r", "");
        }
        static long Seconds(JObj o, string k)
        {
            string s = o.Str(k).Trim();
            if (s.Length == 0) return 0;
            long mul = 1;
            if (s.EndsWith("m", StringComparison.Ordinal)) { mul = 60; s = s.Substring(0, s.Length - 1); }
            else if (s.EndsWith("h", StringComparison.Ordinal)) { mul = 3600; s = s.Substring(0, s.Length - 1); }
            else if (s.EndsWith("d", StringComparison.Ordinal)) { mul = 86400; s = s.Substring(0, s.Length - 1); }
            else if (s.EndsWith("s", StringComparison.Ordinal)) s = s.Substring(0, s.Length - 1);
            double d; return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? (long)(d * mul) : 0;
        }

        static List<RangeWinEvent> WinEvents(List<object> l)
        {
            return l.OfType<JObj>().Select(ev => new RangeWinEvent { Log = ev.Str("log", "Security"), Provider = ev.Str("provider", "Microsoft-Windows-Security-Auditing"), Id = ev.Int("id", 0), Level = ev.Int("level", 4), Message = ev.Str("message"), User = ev.Str("user"), Ago = Seconds(ev, "ago"), Repeat = ev.Int("repeat", 1), Every = ev.Int("every", 5) }).ToList();
        }

        /// <summary>Reads a scenario folder as a template (its {{...}} are filled per run by Instantiate).</summary>
        public static RangeScenario LoadScenario(string dir)
        {
            string f = Path.Combine(dir, "scenario.json");
            if (!File.Exists(f)) return null;
            var j = Json.ParseObj(File.ReadAllText(f));
            if (j == null) throw new InvalidDataException("scenario.json is not a JSON object");
            string baseDir = "";
            if (j.Str("base").Length > 0)
            {
                // a scenario built on another one's network: its keys first, this file's on top
                baseDir = Path.Combine(Path.GetDirectoryName(dir), j.Str("base"));
                var bj = Json.ParseObj(File.ReadAllText(Path.Combine(baseDir, "scenario.json")));
                foreach (var kv in j) bj[kv.Key] = kv.Value;
                if (!j.ContainsKey("vars") || j.Obj("vars") == null) { }
                else
                {
                    var bv = Json.ParseObj(File.ReadAllText(Path.Combine(baseDir, "scenario.json"))).Obj("vars") ?? new JObj();
                    foreach (var kv in j.Obj("vars")) bv[kv.Key] = kv.Value;
                    bj["vars"] = bv;
                }
                bj.Remove("base");
                j = bj;
            }
            var s = Map(j, dir, baseDir, null);
            s.Raw = j; s.BaseDir = baseDir;
            if (Array.IndexOf(Levels, s.Level) < 0) throw new InvalidDataException("unknown level " + s.Level);
            if (s.Start.Length == 0 || s.Machines.All(m => m.Name != s.Start)) throw new InvalidDataException("the start machine is missing");
            if (CategoryById(s.Category) == null) throw new InvalidDataException("unknown category " + s.Category);
            return s;
        }

        /// <summary>Turns a (filled or template) scenario object into a RangeScenario; fill (may be null) prepares the
        /// text of the setup scripts, the solution and the walkthrough.</summary>
        static RangeScenario Map(JObj j, string dir, string baseDir, Func<string, string> fill)
        {
            if (fill == null) fill = x => x;
            string defender = j.Str("defender", "none");
            var s = new RangeScenario
            {
                Id = j.Str("id", Path.GetFileName(dir)), Title = j.Str("title"), Level = j.Str("level", "Beginner"), Order = j.Int("order", 0), Summary = j.Str("summary"),
                Story = j.Str("story"), Goal = j.Str("goal"), Defender = defender, Start = j.Str("start"), Minutes = j.Int("minutes", 0), Dir = dir, Skills = j.Str("skills"), Simple = j.Str("simple"),
                DomainName = j.Str("domain"), DomainNetbios = j.Str("netbios"), DcName = j.Str("dc"),
                Category = j.Str("category", defender != "none" ? "defender" : "red"),
                AttackerIp = j.Str("attackerIp"), GoalName = j.Str("goalName"), GoalPoints = j.Int("goalPoints", 0), QuietPoints = j.Int("quietPoints", defender != "none" ? 100 : 0)
            };
            s.BaseDir = baseDir;
            if (s.Minutes <= 0) s.Minutes = DefaultMinutes(s.Level, false);
            foreach (var o in j.Arr("machines").OfType<JObj>())
            {
                var m = new RangeMachineDef
                {
                    Name = o.Str("name"), Os = o.Str("os", "linux"), Hostname = o.Str("hostname"), Role = o.Str("role"), Server = o.Bool("server", true), Firewall = o.Bool("firewall", false),
                    SetupFile = o.Str("setup"), DomainJoined = o.Bool("domainJoined", false), Motd = o.Str("motd"), Banner = o.Str("banner")
                };
                m.Nics = Strs(o.Arr("nics")); m.Packages = Strs(o.Arr("packages")); m.Tools = Strs(o.Arr("tools")); m.Ports = Ints(o.Arr("ports"));
                m.Versions = IntMap(o.Obj("versions")); m.PortNames = IntMap(o.Obj("portNames"));
                m.Accounts = Accounts(o.Arr("accounts"));
                m.Private = Strs(o.Arr("private")); m.Writable = Strs(o.Arr("writable")); m.DomainAdmins = Strs(o.Arr("domainAdmins"));
                foreach (var sh in o.Arr("shares").OfType<JObj>()) m.Shares.Add(new RangeShare { Name = sh.Str("name"), Path = sh.Str("path"), Comment = sh.Str("comment"), Anonymous = sh.Bool("anonymous", false), Users = Strs(sh.Arr("users")) });
                m.Events.AddRange(WinEvents(o.Arr("events")));
                m.Setup = fill(ReadRel(dir, baseDir, m.SetupFile));
                if (m.Name.Length == 0 || m.Nics.Count == 0) throw new InvalidDataException("a machine needs a name and an address");
                s.Machines.Add(m);
            }
            foreach (var o in j.Arr("routes").OfType<JObj>()) s.Routes.Add(new RangeRoute { From = o.Str("from"), To = o.Str("to"), Ports = Ints(o.Arr("ports")) });
            foreach (var o in j.Arr("flags").OfType<JObj>()) s.Flags.Add(new RangeFlagDef { Id = o.Str("id"), Name = o.Str("name"), Machine = o.Str("machine"), Path = o.Str("path"), Owner = o.Str("owner", "root"), Mode = o.Str("mode", "600"), Points = o.Int("points", 100), Level = o.Str("level", "user") });
            s.Hints = Hints(j.Arr("hints"));
            var vars = j.Obj("vars"); if (vars != null) foreach (var kv in vars) s.Vars[kv.Key] = Convert.ToString(kv.Value, CultureInfo.InvariantCulture);
            s.Variants = j.Obj("variants");
            s.Hosts = Strs(j.Arr("hosts"));
            s.Watch = Strs(j.Arr("watch"));
            var dr = j.Obj("defenderRules");
            if (dr != null)
            {
                s.DefenderRules = new RangeDefenderRules { Budget = dr.Int("budget", 0), Window = dr.Int("window", 0), React = dr.Int("react", -1), Reset = dr.ContainsKey("reset") ? (bool?)dr.Bool("reset", false) : null, Isolate = dr.ContainsKey("isolate") ? (bool?)dr.Bool("isolate", false) : null, Watches = dr.Str("watches") };
                var cnt = dr.Obj("count");
                if (cnt != null) foreach (var kv in cnt) { double d; if (double.TryParse(Convert.ToString(kv.Value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) s.DefenderRules.Count[kv.Key] = (int)Math.Round(d); }
            }
            s.DomainAccounts = Accounts(j.Arr("domainAccounts"));
            foreach (var o in j.Arr("web").OfType<JObj>()) s.Web.Add(WebApp(o, dir, baseDir, fill));
            foreach (var o in j.Arr("questions").OfType<JObj>())
                s.Questions.Add(new RangeQuestion { Id = o.Str("id"), Ask = o.Str("ask"), Answer = o.Str("answer"), Accept = Strs(o.Arr("accept")), Points = o.Int("points", 50), Explain = o.Str("explain"), Tries = o.Int("tries", 0), Derived = o.Bool("derived", false) });
            // SOC Analyst: the SIEM's events (a file of lines with offsets from the start time) and its background noise
            var siem = j.Obj("siem");
            if (siem != null)
            {
                s.SiemText = fill(ReadRel(dir, baseDir, siem.Str("file", "siem.txt")));
                s.SiemDay = siem.Str("day", "2026-10-20"); s.SiemStart = siem.Str("start", "09:00:00");
                s.SiemNoise = siem.Obj("noise");
            }
            foreach (var o in j.Arr("tasks").OfType<JObj>())
                s.Tasks.Add(new RangeTask { Id = o.Str("id"), Name = o.Str("name"), Machine = o.Str("machine"), Check = o.Str("check"), Points = o.Int("points", 50), Explain = o.Str("explain") });
            foreach (var o in j.Arr("attacker").OfType<JObj>())
                s.Attacker.Add(new RangeStep { At = Seconds(o, "at"), Machine = o.Str("machine"), Run = o.Str("run"), Unless = o.Str("unless"), Note = o.Str("note"), Goal = o.Bool("goal", false), Optional = o.Bool("optional", false) });
            s.Solution = SolutionLines(fill(ReadRel(dir, baseDir, j.Str("solution", "solution.txt"))));
            string noisy = j.Str("noisySolution");
            if (noisy.Length > 0) s.NoisySolution = SolutionLines(fill(ReadRel(dir, baseDir, noisy)));
            s.Walkthrough = fill(ReadRel(dir, baseDir, j.Str("walkthrough", "walkthrough.txt")));
            return s;
        }

        static RangeWebApp WebApp(JObj o, string dir, string baseDir, Func<string, string> fill)
        {
            var w = new RangeWebApp { Machine = o.Str("machine", "puzzlebox"), Port = o.Int("port", 80), Path = o.Str("path", "/"), Kind = o.Str("kind", "page"), Param = o.Str("param"), RunAs = o.Str("runAs", "apache"), Template = o.Str("template"), Base = o.Str("base"), Title = o.Str("title"), Server = o.Str("server"), Fail = o.Str("fail"), Cookie = o.Str("cookie"), Want = o.Str("want"), Header = o.Str("header") };
            w.Body = o.Str("body"); if (o.Str("bodyFile").Length > 0) w.Body = fill(ReadRel(dir, baseDir, o.Str("bodyFile")));
            if (o.Str("templateFile").Length > 0) w.Template = fill(ReadRel(dir, baseDir, o.Str("templateFile")));
            foreach (var lg in o.Arr("logins").OfType<List<object>>()) w.Logins.Add(Strs(lg).ToArray());
            foreach (var r in o.Arr("records").OfType<List<object>>()) w.Records.Add(Strs(r).ToArray());
            var st = o.Obj("settings");
            if (st != null) foreach (var kv in st) w.Settings[kv.Key] = Convert.ToString(kv.Value, CultureInfo.InvariantCulture);
            return w;
        }

        /// <summary>The scenario for one run: the variables and flags made from the seed, every text filled.</summary>
        public static RangeScenario Instantiate(RangeScenario t, int seed, out Dictionary<string, string> vars, out Dictionary<string, string> flags)
        {
            var v = MakeVars(t.Vars, t.Variants, seed, t.Id);
            var fl = new Dictionary<string, string>();
            foreach (var f in t.Flags) fl[f.Id] = FlagValue(seed, t.Id, f.Id);
            vars = v; flags = fl;
            var filled = (JObj)FillTree(t.Raw, v, fl);
            var s = Map(filled, t.Dir, t.BaseDir, x => Process(x, v, fl));
            s.Raw = t.Raw;
            return s;
        }

        /// <summary>Fills every string in a JSON tree and drops array items whose "if" doesn't hold.</summary>
        static object FillTree(object o, Dictionary<string, string> vars, Dictionary<string, string> flags)
        {
            var j = o as JObj;
            if (j != null)
            {
                var n = new JObj();
                foreach (var kv in j)
                {
                    if (kv.Key == "variants" || kv.Key == "vars") { n[kv.Key] = kv.Value; continue; }
                    n[kv.Key] = FillTree(kv.Value, vars, flags);
                }
                return n;
            }
            var l = o as List<object>;
            if (l != null)
            {
                var n = new List<object>();
                foreach (var x in l)
                {
                    var xj = x as JObj;
                    if (xj != null && xj.ContainsKey("if") && !Holds(xj.Str("if"), vars)) continue;
                    n.Add(FillTree(x, vars, flags));
                }
                return n;
            }
            var s = o as string;
            if (s != null) return Fill(s, vars, flags);
            return o;
        }

        /// <summary>A condition: NAME=a|b (one of), NAME!=a|b (none of); several joined with " and ".</summary>
        public static bool Holds(string cond, Dictionary<string, string> vars)
        {
            foreach (var part in Regex.Split(cond.Trim(), "\\s+and\\s+"))
            {
                var m = Regex.Match(part.Trim(), "^([A-Za-z0-9_]+)\\s*(!?=)\\s*(.*)$");
                if (!m.Success) return false;
                string v; vars.TryGetValue(m.Groups[1].Value, out v); v = v ?? "";
                bool any = m.Groups[3].Value.Split('|').Any(x => x.Trim() == v);
                if (m.Groups[2].Value == "=" ? !any : any) return false;
            }
            return true;
        }

        /// <summary>Text for one run: {{if COND}} ... {{else}} ... {{end}} blocks (each on its own line), then the
        /// placeholders.</summary>
        public static string Process(string text, Dictionary<string, string> vars, Dictionary<string, string> flags)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            if (text.Contains("{{if "))
            {
                var sb = new StringBuilder();
                var keep = new Stack<bool[]>();   // [this block's condition, the outer state]
                bool on = true;
                foreach (var line in text.Split('\n'))
                {
                    string t = line.Trim();
                    var m = Regex.Match(t, "^\\{\\{if (.+)\\}\\}$");
                    if (m.Success) { keep.Push(new[] { Holds(m.Groups[1].Value, vars), on }); on = on && keep.Peek()[0]; continue; }
                    if (t == "{{else}}" && keep.Count > 0) { var k = keep.Peek(); on = k[1] && !k[0]; continue; }
                    if (t == "{{end}}" && keep.Count > 0) { on = keep.Pop()[1]; continue; }
                    if (on) sb.Append(line).Append('\n');
                }
                text = sb.ToString();
                if (text.EndsWith("\n", StringComparison.Ordinal)) text = text.Substring(0, text.Length - 1);
            }
            return Fill(text, vars, flags);
        }

        /// <summary>Solution lines: everything except "##" comments (blank lines are kept: they answer prompts with Enter).</summary>
        public static List<string> SolutionLines(string text)
        {
            var l = text.Replace("\r", "").Split('\n').ToList();
            while (l.Count > 0 && l[l.Count - 1].Length == 0) l.RemoveAt(l.Count - 1);
            return l.Where(x => !x.StartsWith("##", StringComparison.Ordinal)).ToList();
        }

        public static RangePuzzle LoadPuzzle(string file)
        {
            var p = ParsePuzzle(File.ReadAllText(file).Replace("\r", ""), Path.GetFileNameWithoutExtension(file), null);
            p.File = file;
            if (Array.IndexOf(Levels, p.Level) < 0) throw new InvalidDataException("unknown level " + p.Level);
            if (p.Host != "linux" && p.Host != "windows") throw new InvalidDataException("host must be linux or windows");
            if (p.Title.Length == 0 || (p.Setup.Length == 0 && p.WinSetup.Length == 0)) throw new InvalidDataException("a puzzle needs a title and a setup");
            if (!IsPuzzleCategory(p.Category)) throw new InvalidDataException("unknown puzzle category " + p.Category);
            if (Array.IndexOf(TopicsOf(p.Category), p.Topic) < 0) throw new InvalidDataException("unknown topic " + p.Topic);
            if (!Regex.IsMatch(p.Login, "^[a-z_][a-z0-9_-]{0,30}$")) throw new InvalidDataException("login must be a plain account name");
            return p;
        }

        /// <summary>For tests: a puzzle from text (no file, nothing filled).</summary>
        public static RangePuzzle ParsePuzzleForTests(string text) { return ParsePuzzle(text, "test", null); }

        /// <summary>A puzzle file: a header (key: value), then @sections. With fill, every section is prepared for a run.</summary>
        static RangePuzzle ParsePuzzle(string text, string id, Func<string, string> fill)
        {
            var p = new RangePuzzle { Id = id, Raw = text };
            if (fill == null) fill = x => x;
            string section = null, arg = ""; var sb = new StringBuilder(); int hintCost = 0;
            var variants = new JObj();
            Action flush = () =>
            {
                string t = sb.ToString(); sb.Clear();
                if (section == null) return;
                if (section == "intro") p.Intro = fill(t).Trim();
                else if (section == "setup") p.Setup = fill(t);
                else if (section == "solution") p.Solution = SolutionLines(fill(t));
                else if (section == "walkthrough") p.Walkthrough = fill(t).Trim();
                else if (section == "hint") p.Hints.Add(new RangeHint { Text = fill(t).Trim(), Cost = hintCost });
                else if (section == "web") p.WebJson = t;
                else if (section == "winsetup") p.WinSetup = fill(t);
                else if (section == "winevents") p.WinEventsJson = t;
                else if (section == "vars")
                {
                    // @vars NAME=value: extra variables when NAME has that value
                    int eq = arg.IndexOf('=');
                    if (eq > 0)
                    {
                        string name = arg.Substring(0, eq).Trim(), val = arg.Substring(eq + 1).Trim();
                        var byVal = variants.Obj(name); if (byVal == null) { byVal = new JObj(); variants[name] = byVal; }
                        var set = new JObj();
                        foreach (var l in t.Split('\n')) { int e = l.IndexOf('='); if (e > 0) set[l.Substring(0, e).Trim()] = l.Substring(e + 1).Trim(); }
                        byVal[val] = set;
                    }
                }
            };
            foreach (var raw in text.Split('\n'))
            {
                if (raw.StartsWith("@", StringComparison.Ordinal))
                {
                    flush();
                    var w = raw.Substring(1).Trim().Split(new[] { ' ' }, 2);
                    section = w[0]; arg = w.Length > 1 ? w[1].Trim() : "";
                    if (section == "hint") { int c; hintCost = int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out c) ? c : 5; }
                    continue;
                }
                if (section == null)
                {
                    int c = raw.IndexOf(':');
                    if (raw.Trim().Length == 0 || raw.StartsWith("#", StringComparison.Ordinal) || c < 0) continue;
                    string k = raw.Substring(0, c).Trim().ToLowerInvariant(), v = raw.Substring(c + 1).Trim();
                    switch (k)
                    {
                        case "id": p.Id = v; break;
                        case "title": p.Title = v; break;
                        case "level": p.Level = v; break;
                        case "kind": p.Kind = v; break;
                        case "topic": p.Topic = v; break;
                        case "category": p.Category = v.ToLowerInvariant(); break;
                        case "login": p.Login = v; break;
                        case "teaser": p.Teaser = v; break;
                        case "simple": p.Simple = v; break;
                        case "minutes": { int mn; if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out mn)) p.Minutes = mn; break; }
                        case "points": p.Points = int.Parse(v, CultureInfo.InvariantCulture); break;
                        case "order": p.Order = int.Parse(v, CultureInfo.InvariantCulture); break;
                        case "answer": p.Answer = v; break;
                        case "host": p.Host = v.ToLowerInvariant(); break;
                        case "winadmin": p.WinAdmin = v.ToLowerInvariant() == "yes" || v.ToLowerInvariant() == "true"; break;
                        case "packages": p.Packages = v.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries).ToList(); break;
                        case "var": { int eq = v.IndexOf('='); p.Vars[v.Substring(0, eq).Trim()] = v.Substring(eq + 1).Trim(); break; }
                    }
                    continue;
                }
                sb.Append(raw).Append('\n');
            }
            flush();
            if (variants.Count > 0) p.Variants = variants;
            if (p.Topic.Length == 0 && p.Category == "ctf") p.Topic = TopicFromKind(p.Kind);
            if (p.Topic.Length == 0 && p.Category != "ctf") { var tl = TopicsOf(p.Category); if (tl.Length == 1) p.Topic = tl[0]; }
            if (p.Teaser.Length == 0) p.Teaser = p.Kind;
            if (p.Minutes <= 0) p.Minutes = DefaultMinutes(p.Level, true);
            if (p.WinEventsJson.Trim().Length > 0)
            {
                var ev = Json.Parse(fill(p.WinEventsJson)) as List<object>;
                if (ev == null) throw new InvalidDataException("@winevents must be a JSON list of events");
                p.WinEvents = WinEvents(ev);
            }
            if (p.WebJson.Trim().Length > 0)
            {
                var arr = Json.Parse(fill(p.WebJson)) as List<object>;
                if (arr == null) throw new InvalidDataException("@web must be a JSON list of web apps");
                foreach (var o in arr.OfType<JObj>()) p.Web.Add(WebApp(o, Path.GetDirectoryName(Dir ?? "."), "", fill));
            }
            return p;
        }

        static string TopicFromKind(string kind)
        {
            switch ((kind ?? "").ToLowerInvariant())
            {
                case "decoding": return "Crypto & Encoding";
                case "file forensics": return "Forensics";
                case "log hunting": return "Log Hunting";
                case "privilege escalation": return "Privilege Escalation";
                case "password cracking": return "Password Cracking";
                case "steganography": return "Steganography";
            }
            return "";
        }

        /// <summary>The puzzle for one run (variables, the flag, every section filled).</summary>
        public static RangePuzzle InstantiatePuzzle(RangePuzzle t, int seed, out Dictionary<string, string> vars, out Dictionary<string, string> flags)
        {
            var v = MakeVars(t.Vars, t.Variants, seed, t.Id);
            var fl = new Dictionary<string, string> { { "", FlagValue(seed, t.Id, "flag") } };
            vars = v; flags = fl;
            var p = ParsePuzzle(t.Raw, t.Id, x => Process(x, v, fl));
            p.File = t.File;
            return p;
        }

        // ------------------------------------------------------------ a run's secrets
        public static string Hex(string s, int n)
        {
            using (var h = SHA256.Create())
            {
                var b = h.ComputeHash(Encoding.UTF8.GetBytes(s));
                var sb = new StringBuilder(); foreach (var x in b) sb.Append(x.ToString("x2"));
                return sb.ToString().Substring(0, n);
            }
        }
        public static string FlagValue(int seed, string owner, string flagId)
        {
            string slug = Regex.Replace((owner + "-" + flagId).ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
            return "FLAG{" + slug + "-" + Hex("range-flag|" + seed + "|" + owner + "|" + flagId, 10) + "}";
        }

        static readonly string[] FirstNames = { "avery", "blake", "casey", "devon", "emery", "finley", "harper", "jordan", "kendall", "logan", "morgan", "parker", "quinn", "reese", "riley", "rowan", "sawyer", "skyler", "taylor", "wren" };

        static List<string> Pool(string name)
        {
            List<string> l;
            if (Pools.TryGetValue(name, out l) && l.Count > 0) return l;
            if (name == "first") return FirstNames.Select(x => char.ToUpperInvariant(x[0]) + x.Substring(1)).ToList();
            return new List<string> { name };
        }

        public static Dictionary<string, string> MakeVars(Dictionary<string, string> spec, int seed, string owner) { return MakeVars(spec, null, seed, owner); }

        /// <summary>The run's variables (specs in Range\FORMAT.md): word, pick:a|b|c, num:LOW-HIGH, hex:N, ip, name,
        /// pool:NAME, person, company, subnet, time:H-H, or a fixed text (which may use other variables). Variants add
        /// variables by the value another one got. The same seed always gives the same values.</summary>
        public static Dictionary<string, string> MakeVars(Dictionary<string, string> spec, JObj variants, int seed, string owner)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            var used = new HashSet<string>();
            foreach (var kv in spec.OrderBy(x => x.Key, StringComparer.Ordinal))
            {
                var rnd = new Random(Math.Abs((seed + "|" + owner + "|" + kv.Key).GetHashCodeStable()));
                string v = kv.Value, val;
                Func<List<string>, string> one = list =>
                {
                    string x = list[rnd.Next(list.Count)];
                    for (int i = 0; i < 30 && used.Contains(x.ToLowerInvariant()) && list.Count > 1; i++) x = list[rnd.Next(list.Count)];
                    return x;
                };
                if (v == "word") val = Words.Count == 0 ? "sunshine" : one(Words);
                else if (v.StartsWith("pick:", StringComparison.Ordinal)) { var o = v.Substring(5).Split('|'); val = o[rnd.Next(o.Length)]; }
                else if (v.StartsWith("num:", StringComparison.Ordinal)) { var r = v.Substring(4).Split('-'); int lo = int.Parse(r[0], CultureInfo.InvariantCulture), hi = int.Parse(r[1], CultureInfo.InvariantCulture); val = rnd.Next(lo, hi + 1).ToString(CultureInfo.InvariantCulture); }
                else if (v.StartsWith("hex:", StringComparison.Ordinal)) val = Hex(seed + "|" + owner + "|" + kv.Key, int.Parse(v.Substring(4), CultureInfo.InvariantCulture));
                else if (v == "ip") val = (rnd.Next(2) == 0 ? "198.51.100." : "203.0.113.") + rnd.Next(10, 250);
                else if (v == "name") val = one(Pool("first")).ToLowerInvariant();
                else if (v.StartsWith("pool:", StringComparison.Ordinal)) val = one(Pool(v.Substring(5)));
                else if (v == "subnet" || v.StartsWith("subnet:", StringComparison.Ordinal))
                {
                    // the first three numbers of a /24: {{NET}}.20 is a host, {{NET}}.0/24 the network
                    string kind = v.Length > 7 ? v.Substring(7) : "10";
                    val = null;
                    for (int i = 0; i < 40 && (val == null || used.Contains(val)); i++)
                        val = kind == "192" ? "192.168." + rnd.Next(10, 250) : kind == "172" ? "172." + rnd.Next(16, 32) + "." + rnd.Next(1, 250) : "10." + rnd.Next(10, 250) + "." + rnd.Next(1, 250);
                }
                else if (v.StartsWith("time:", StringComparison.Ordinal))
                {
                    var r = v.Substring(5).Split('-');
                    int lo = int.Parse(r[0], CultureInfo.InvariantCulture), hi = int.Parse(r[1], CultureInfo.InvariantCulture);
                    int secs = rnd.Next(lo * 3600, hi * 3600);
                    val = (secs / 3600).ToString("00") + ":" + (secs / 60 % 60).ToString("00") + ":" + (secs % 60).ToString("00");
                }
                else if (v == "person" || v.StartsWith("person:", StringComparison.Ordinal))
                {
                    string first = one(Pool("first")), last = Pool("last")[rnd.Next(Pool("last").Count)];
                    string style = v.Length > 7 ? v.Substring(7) : "first";
                    val = style == "flast" ? (first.Substring(0, 1) + last).ToLowerInvariant() : style == "first.last" ? (first + "." + last).ToLowerInvariant() : first.ToLowerInvariant();
                    d[kv.Key + "_FIRST"] = first; d[kv.Key + "_LAST"] = last; d[kv.Key + "_FULL"] = first + " " + last;
                    used.Add(first.ToLowerInvariant());
                }
                else if (v == "company")
                {
                    val = one(Pool("company"));
                    string shortName = val.Split(' ')[0];
                    d[kv.Key + "_SHORT"] = shortName; d[kv.Key + "_SLUG"] = shortName.ToLowerInvariant(); d[kv.Key + "_DOMAIN"] = shortName.ToLowerInvariant() + ".example";
                }
                else val = v;
                used.Add(val.ToLowerInvariant());
                d[kv.Key] = val;
            }
            // variants: more variables chosen by the value of another
            if (variants != null)
                foreach (var kv in variants.OrderBy(x => x.Key, StringComparer.Ordinal))
                {
                    string have; if (!d.TryGetValue(kv.Key, out have)) continue;
                    var byVal = kv.Value as JObj; if (byVal == null) continue;
                    var set = byVal.Obj(have); if (set == null) continue;
                    foreach (var s in set) d[s.Key] = Convert.ToString(s.Value, CultureInfo.InvariantCulture);
                }
            // values may use other variables ({{NET}}.20)
            for (int pass = 0; pass < 4; pass++)
            {
                bool changed = false;
                foreach (var k in d.Keys.ToList())
                {
                    if (!d[k].Contains("{{")) continue;
                    string n = Fill(d[k], d, null);
                    if (n != d[k]) { d[k] = n; changed = true; }
                }
                if (!changed) break;
            }
            return d;
        }

        /// <summary>Fills {{NAME}}, {{FLAG}}, {{FLAG:id}}, {{SIMHASH:NAME}}, {{MD5:NAME}}, {{SHA1:NAME}}, {{SHA256:NAME}},
        /// {{B64:NAME}}, {{HEX:NAME}}, {{ROT13:NAME}}, {{REV:NAME}}, {{UPPER:NAME}}, {{LOWER:NAME}},
        /// {{TICKET:user:NAME}} and {{WORDLIST}} (the word list's text).</summary>
        public static string Fill(string text, Dictionary<string, string> vars, Dictionary<string, string> flags)
        {
            if (string.IsNullOrEmpty(text) || !text.Contains("{{")) return text ?? "";
            return Regex.Replace(text, "\\{\\{([A-Za-z0-9_:\\-]+)\\}\\}", m =>
            {
                var p = m.Groups[1].Value.Split(':');
                string v;
                Func<string, string> val = k =>
                {
                    if (k == "FLAG" && flags != null && flags.TryGetValue("", out v)) return v;
                    if (k.StartsWith("FLAG-", StringComparison.Ordinal) && flags != null && flags.TryGetValue(k.Substring(5), out v)) return v;
                    return vars.TryGetValue(k, out v) ? v : k;
                };
                switch (p[0])
                {
                    case "FLAG": if (flags == null) return m.Value; return flags.TryGetValue(p.Length > 1 ? p[1] : "", out v) ? v : m.Value;
                    case "SIMHASH": return RangeHash.Nt(val(p[1]));
                    case "MD5": return RangeHash.Md5(val(p[1]));
                    case "SHA1": return RangeHash.Sha1(val(p[1]));
                    case "SHA256": return RangeHash.Sha256(val(p[1]));
                    case "B64": return Convert.ToBase64String(Encoding.UTF8.GetBytes(val(p[1])));
                    case "HEX": return string.Concat(Encoding.UTF8.GetBytes(val(p[1])).Select(b => b.ToString("x2")).ToArray());
                    case "ROT13": return Rot13(val(p[1]));
                    case "REV": { var a = val(p[1]).ToCharArray(); Array.Reverse(a); return new string(a); }
                    case "UPPER": return val(p[1]).ToUpperInvariant();
                    case "LOWER": return val(p[1]).ToLowerInvariant();
                    case "TICKET": return RangeHash.Ticket(p[1], val(p[2]));
                    case "B32": return RangeCodes.Base32(Encoding.UTF8.GetBytes(val(p[1])));
                    case "XOR": if (p.Length < 3) return m.Value; return RangeCodes.XorHex(val(p[2]), RangeCodes.Key(val(p[1])) & 255);
                    case "VIG": if (p.Length < 3) return m.Value; return RangeCodes.Vigenere(val(p[2]), val(p[1]), false);
                    case "CAESAR": { if (p.Length < 3) return m.Value; int n = RangeCodes.Key(val(p[1])); return RangeCodes.Caesar(val(p[2]), n < 0 ? 0 : n); }
                    case "MORSE": return RangeCodes.Morse(val(p[1]));
                    case "WORDLIST": return WordText;
                }
                return vars.TryGetValue(p[0], out v) ? v : m.Value;
            });
        }

        public static string Rot13(string s)
        {
            var c = s.ToCharArray();
            for (int i = 0; i < c.Length; i++)
            {
                if (c[i] >= 'a' && c[i] <= 'z') c[i] = (char)('a' + (c[i] - 'a' + 13) % 26);
                else if (c[i] >= 'A' && c[i] <= 'Z') c[i] = (char)('A' + (c[i] - 'A' + 13) % 26);
            }
            return new string(c);
        }

        /// <summary>A blue-team answer as it is compared: lower case, no quotes or trailing slash, single spaces.</summary>
        public static string NormalAnswer(string s)
        {
            string t = (s ?? "").Trim().Trim('"', '\'', '`').Trim().ToLowerInvariant();
            t = Regex.Replace(t, "\\s+", " ");
            if (t.Length > 1 && t.EndsWith("/", StringComparison.Ordinal)) t = t.TrimEnd('/');
            return t;
        }

        /// <summary>A SOC verdict: "real" or "false alarm" for the usual ways of saying them, else null.</summary>
        public static string Verdict(string s)
        {
            string t = NormalAnswer(s).Replace("-", " ").Trim('.', '!', ' ');
            switch (t)
            {
                case "real": case "real attack": case "a real attack": case "attack": case "true positive": case "tp": case "malicious": case "real alert": case "real alarm": case "compromise":
                    return "real";
                case "false alarm": case "a false alarm": case "false": case "false positive": case "fp": case "benign": case "harmless": case "not real": case "not an attack": case "normal":
                    return "false alarm";
            }
            return null;
        }
    }

    public static class RangeStableHash
    {
        /// <summary>A string hash that is the same in every process (string.GetHashCode is not guaranteed to be).</summary>
        public static int GetHashCodeStable(this string s)
        {
            unchecked
            {
                int h = 23;
                foreach (char c in s ?? "") h = h * 31 + c;
                return h == int.MinValue ? 0 : h;
            }
        }
    }
}
