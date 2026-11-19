// Cyber Range - the simulated defender of the "Red vs Defender" mode. It watches the scenario's noise log (the same
// NoiseEvents a red-team run already raises: scans, logins, failed logins, web tricks, password guessing) on the
// clock. When a source makes too much noise, it raises an alert; a little later it reacts: it blocks that source's
// address on the watched machines (so its connections time out), and - at higher levels - resets a password it saw
// used and notes that it would cut the machine off. Everything is simulated: no real firewall, no real network.
//
// Levels: Beginner is slow and forgiving and explains in plain words what it noticed; Expert is quick and strict.
using System;
using System.Collections.Generic;
using System.Linq;

namespace CertLab
{
    public class RangeDefender : IDefender
    {
        readonly RangeRun run;
        readonly RangeNet net;
        readonly int lvl;                // 0 Beginner .. 3 Expert
        readonly int budget;             // loudness a source may make before an alert
        readonly long window;            // seconds the running loudness counts over
        readonly long reactDelay;        // seconds between the alert and the block
        readonly bool canReset, canIsolate;
        readonly Dictionary<string, int> count;   // the scenario's own list of what counts (null: loudness 2 and up)
        readonly HashSet<string> watched = new HashSet<string>(StringComparer.Ordinal);
        readonly long start;

        class Src { public int Loud; public long Since = -1; public bool Alerted; public long BlockAt = -1; public bool Blocked; }
        readonly Dictionary<string, Src> sources = new Dictionary<string, Src>(StringComparer.Ordinal);
        public readonly List<string> Report = new List<string>();   // plain-words notices for the end report
        public int Alerts, Blocks;
        public bool EverActed { get { return Alerts > 0; } }

        public RangeDefender(RangeRun r)
        {
            run = r; net = r.Net; start = net.Lab.Now;
            lvl = RangeData.LevelIndex(r.Level);
            // Only clearly suspicious noise (loud >= 2: big scans, failed sign-ins, loud guessing, web tricks) counts
            // toward the budget; quiet recon and a few logins do not. Lower budget + quicker reaction at higher levels.
            switch (lvl)
            {
                case 0: budget = 10; window = 1800; reactDelay = 180; canReset = false; canIsolate = false; break;
                case 1: budget = 9; window = 1500; reactDelay = 120; canReset = false; canIsolate = false; break;
                case 2: budget = 6; window = 1200; reactDelay = 45; canReset = true; canIsolate = false; break;
                default: budget = 8; window = 900; reactDelay = 10; canReset = true; canIsolate = true; break;
            }
            var rules = r.Sc.DefenderRules;
            if (rules != null)
            {
                if (rules.Budget > 0) budget = rules.Budget;
                if (rules.Window > 0) window = rules.Window;
                if (rules.React >= 0) reactDelay = rules.React;
                if (rules.Reset.HasValue) canReset = rules.Reset.Value;
                if (rules.Isolate.HasValue) canIsolate = rules.Isolate.Value;
                if (rules.Count.Count > 0) count = rules.Count;
            }
            // which machines it watches: the "watch" list, else everything but the start box
            if (r.Sc.Watch.Count > 0) foreach (var w in r.Sc.Watch) { var ip = IpOfName(w); if (ip != null) watched.Add(ip); }
            else foreach (var m in r.Sc.Machines) if (m.Name != r.Sc.Start) { var ip = IpOfName(m.Name); if (ip != null) watched.Add(ip); }
            net.Defender = this;
        }

        string IpOfName(string name)
        {
            var m = net.Lab.Get(name); if (m != null) return m.FirstV4();
            var h = net.HostByName(name); if (h != null) return h.Ip;
            return null;
        }

        public string Name { get { return RangeData.Levels[lvl] + " defender"; } }
        public void Attach(RangeNet n) { }

        public void OnNoise(NoiseEvent e)
        {
            // only clearly suspicious noise counts; quiet recon (loud 1) and ordinary logins do not - unless the scenario
            // says exactly which kinds this defender counts (and how much each is worth)
            int worth = e.Loud;
            if (count != null) { if (!count.TryGetValue(e.Kind, out worth)) worth = 0; }
            else if (e.Loud < 2) worth = 0;
            if (worth <= 0 || string.IsNullOrEmpty(e.Source)) return;
            // only noise aimed at (or from a login to) a watched machine counts
            if (!watched.Contains(e.Target) && e.Target.Length > 0) return;
            Src s;
            if (!sources.TryGetValue(e.Source, out s)) { s = new Src(); sources[e.Source] = s; }
            if (s.Since < 0 || net.Lab.Now - s.Since > window) { s.Loud = 0; s.Since = net.Lab.Now; }
            s.Loud += worth;
            if (!s.Alerted && s.Loud >= budget)
            {
                s.Alerted = true; s.BlockAt = net.Lab.Now + reactDelay; Alerts++;
                Report.Add("At " + RangeRun.TimeText(net.Lab.Now - start) + " I noticed a lot of activity from " + e.Source + " (" + Describe(e) + "). That is the kind of noise that gets you caught.");
            }
        }

        static string Describe(NoiseEvent e)
        {
            switch (e.Kind)
            {
                case "port-scan": case "ping-sweep": return "a port scan";
                case "password-guessing": return "many password guesses";
                case "login-failed": case "web-login-failed": return "failed sign-ins";
                case "web-scan": return "a sweep of web addresses";
                case "web-command": return "a web request with shell characters in it";
                case "web-traversal": return "a web request trying to climb out of a folder";
                case "ticket-request": return "a request for service tickets";
                case "privilege": return "someone using administrator rights (sudo or su)";
                case "new-process": return "a new program, task or service being started";
                case "login": return "sign-ins";
                case "smb-read": case "ftp-read": case "database-read": return "files being copied";
            }
            return e.Kind.Replace('-', ' ');
        }

        public void Tick(long now)
        {
            foreach (var kv in sources)
            {
                var s = kv.Value;
                if (!s.Alerted || s.Blocked || s.BlockAt < 0 || now < s.BlockAt) continue;
                s.Blocked = true; Blocks++;
                Report.Add("At " + RangeRun.TimeText(now - start) + " I blocked " + kv.Key + " on the firewall, so its connections stop getting through.");
                if (canReset) ResetUsedPasswords(kv.Key);
                if (canIsolate) Report.Add("I also moved to cut the affected machine off the network. Quiet, patient work avoids all of this.");
            }
        }

        /// <summary>Change a password the noisy source was seen using, so the stolen credential stops working.</summary>
        void ResetUsedPasswords(string source)
        {
            foreach (var w in net.Worked.ToList())
            {
                var lm = net.Lab.Owner(w.Host) as Machine;
                if (lm == null || string.IsNullOrEmpty(w.User) || lm.User(w.User) == null) continue;
                string np = "Reset!" + RangeData.Hex(source + w.User + net.Lab.Now, 8);
                lm.RunCommand("root", "echo '" + w.User + ":" + np + "' | chpasswd 2>/dev/null; true", null, 100000, null);
                Report.Add("I reset the password for " + w.User + " on " + w.Host + ", so the one you found no longer works.");
            }
        }

        /// <summary>Does the defender's block stop this machine reaching that address? (Never its own addresses.)</summary>
        public bool BlocksReach(Machine from, string ip)
        {
            string src = from.FirstV4();
            if (src == null) return false;
            Src s;
            if (!sources.TryGetValue(src, out s) || !s.Blocked) return false;
            if (!watched.Contains(ip)) return false;
            foreach (var nif in from.Ifs) foreach (var a in nif.V4) if (a.Split('/')[0] == ip) return false;
            return true;
        }

        public int QuietBonus(int max)
        {
            if (Alerts == 0) return max;
            int per = Math.Max(10, max / 2);
            return Math.Max(0, max - Alerts * per);
        }
    }
}
