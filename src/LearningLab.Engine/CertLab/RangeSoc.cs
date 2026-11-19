// Cyber Range - the SOC Analyst's pretend SIEM data. A SOC scenario lists what happened (the attack, and the harmless
// look-alikes that also raised alerts) as event lines with an offset from the incident's start time (siem.txt, filled
// from the seed like every other scenario file); this file adds a day's worth of ordinary background events made from
// the seed (people signing in, programs starting, web pages, name lookups, firewall lines), sorts everything by time
// and writes it where the siem tool reads it (/var/lib/siem/events.log on the analyst's box). Nothing here reaches a
// real computer: the events are text the simulator made up.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CertLab
{
    public static class RangeSoc
    {
        /// <summary>The time format of every event (sorts as text in time order).</summary>
        public const string TimeFormat = "yyyy-MM-dd'T'HH:mm:ss";

        /// <summary>An offset such as +0, +95s, -20m, +2h (seconds when there is no letter).</summary>
        public static bool TryOffset(string s, out long seconds)
        {
            seconds = 0;
            var m = Regex.Match(s ?? "", "^([+-]?)(\\d+)([smhd]?)$");
            if (!m.Success) return false;
            long n = long.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            long mul = m.Groups[3].Value == "m" ? 60 : m.Groups[3].Value == "h" ? 3600 : m.Groups[3].Value == "d" ? 86400 : 1;
            seconds = n * mul * (m.Groups[1].Value == "-" ? -1 : 1);
            return true;
        }

        /// <summary>The incident's start (the day and the time of "+0").</summary>
        public static DateTime StartOf(RangeScenario s)
        {
            DateTime day;
            if (!DateTime.TryParseExact(s.SiemDay, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day)) day = new DateTime(2026, 10, 20);
            TimeSpan t;
            if (!TimeSpan.TryParseExact(s.SiemStart, "hh\\:mm\\:ss", CultureInfo.InvariantCulture, out t)) t = new TimeSpan(9, 0, 0);
            return day + t;
        }

        /// <summary>The whole log for one run: the scenario's events and the background noise, in time order.</summary>
        public static string BuildLog(RangeScenario s, int seed)
        {
            var t0 = StartOf(s);
            var evs = new List<KeyValuePair<DateTime, string>>();
            foreach (var raw in (s.SiemText ?? "").Replace("\r", "").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                int sp = line.IndexOf(' ');
                long off;
                if (sp > 0 && TryOffset(line.Substring(0, sp), out off))
                {
                    string rest = line.Substring(sp + 1).Trim();
                    // a repeated line: "xN/STEP" (N copies, STEP apart) or "x[a|b|c]/STEP" (one per item); %N% is the
                    // copy's number (from 1) and %ITEM% its item
                    var rep = Regex.Match(rest, "^x(\\d+|\\[[^\\]]*\\])/(\\S+)\\s+(.*)$");
                    long step;
                    if (rep.Success && TryOffset(rep.Groups[2].Value, out step))
                    {
                        string spec = rep.Groups[1].Value, body = rep.Groups[3].Value;
                        var items = spec.StartsWith("[", StringComparison.Ordinal) ? spec.Trim('[', ']').Split('|').Select(x => x.Trim()).Where(x => x.Length > 0).ToList() : null;
                        int count = items != null ? items.Count : int.Parse(spec, CultureInfo.InvariantCulture);
                        for (int i = 0; i < count; i++)
                            evs.Add(new KeyValuePair<DateTime, string>(t0.AddSeconds(off + step * i), body.Replace("%N%", (i + 1).ToString(CultureInfo.InvariantCulture)).Replace("%ITEM%", items != null ? items[i] : "")));
                    }
                    else evs.Add(new KeyValuePair<DateTime, string>(t0.AddSeconds(off), rest));
                }
                else evs.Add(new KeyValuePair<DateTime, string>(t0, line));
            }
            if (s.SiemNoise != null) evs.AddRange(Noise(s.SiemNoise, t0, seed, s.Id, s.SiemText ?? ""));
            var sb = new StringBuilder();
            sb.Append("# Cyber Range practice SIEM - pretend events collected from the company's computers\n");
            foreach (var e in evs.OrderBy(x => x.Key))   // OrderBy is stable: at the same second, the scenario's own lines keep their order
                sb.Append("time=").Append(e.Key.ToString(TimeFormat, CultureInfo.InvariantCulture)).Append(' ').Append(e.Value).Append('\n');
            return sb.ToString();
        }

        static readonly string[] Sites = { "news.dailybrief.example", "weather.skycast.example", "cdn.pagefast.example", "maps.waypoint.example", "updates.osvendor.example", "time.clockwork.example", "search.findit.example", "video.streamly.example", "shop.bargainbin.example", "docs.paperless.example", "fonts.letterbox.example", "chat.officetalk.example" };
        static readonly string[] Docs = { "budget", "minutes", "rota", "quarterly-report", "price-list", "notes", "plan", "letter", "timesheet", "inventory", "agenda", "proposal" };
        static readonly string[] Pages = { "/", "/index.html", "/about", "/contact", "/products", "/products/12", "/products/31", "/css/site.css", "/js/site.js", "/images/logo.png", "/news", "/help", "/careers" };

        /// <summary>Ordinary background events from the noise settings (count, from, to, hosts, users, ips, net,
        /// servers, dc, web, linux, domain, firewall, public, kinds, quiet). Never failed sign-ins, new accounts, services or PowerShell: those
        /// belong to the scenario's own lines, so counting them stays exact. Addresses written in the scenario's lines
        /// are never reused for noise.</summary>
        static List<KeyValuePair<DateTime, string>> Noise(JObj n, DateTime t0, int seed, string id, string scenarioText)
        {
            var rnd = new Random(Math.Abs(("siem-noise|" + seed + "|" + id).GetHashCodeStable()));
            var l = new List<KeyValuePair<DateTime, string>>();
            long from, to;
            if (!TryOffset(n.Str("from", "-3h"), out from)) from = -10800;
            if (!TryOffset(n.Str("to", "+3h"), out to)) to = 10800;
            if (to <= from) to = from + 3600;
            int count = n.Int("count", 150);
            var hosts = n.Arr("hosts").Select(x => Convert.ToString(x, CultureInfo.InvariantCulture)).Where(x => x.Length > 0).ToList();
            var users = n.Arr("users").Select(x => Convert.ToString(x, CultureInfo.InvariantCulture)).Where(x => x.Length > 0).ToList();
            var ips = n.Arr("ips").Select(x => Convert.ToString(x, CultureInfo.InvariantCulture)).ToList();
            var servers = n.Arr("servers").Select(x => Convert.ToString(x, CultureInfo.InvariantCulture)).Where(x => x.Length > 0).ToList();
            string net = n.Str("net", "10.20.30"), dc = n.Str("dc"), web = n.Str("web"), linux = n.Str("linux"), domain = n.Str("domain", "corp.example"), fw = n.Str("firewall", "FW01");
            string pub = n.Str("public", "192.0.2.10");
            if (hosts.Count == 0) hosts.Add("WS-01");
            if (users.Count == 0) users.Add("user");
            while (ips.Count < hosts.Count) ips.Add(net + "." + (20 + ips.Count));
            var kinds = n.Arr("kinds").Select(x => Convert.ToString(x, CultureInfo.InvariantCulture)).ToList();
            if (kinds.Count == 0) { kinds.AddRange(new[] { "logon", "process", "process", "dns", "firewall" }); if (web.Length > 0) kinds.Add("web"); if (linux.Length > 0) kinds.Add("ssh"); if (servers.Count > 0 || dc.Length > 0) kinds.Add("logon"); }
            var quiet = new HashSet<string>(n.Arr("quiet").Select(x => Convert.ToString(x, CultureInfo.InvariantCulture)), StringComparer.OrdinalIgnoreCase);
            var avoid = new HashSet<string>(Regex.Matches(scenarioText, "\\b\\d{1,3}\\.\\d{1,3}\\.\\d{1,3}\\.\\d{1,3}\\b").Cast<Match>().Select(m => m.Value));
            avoid.Add(pub);
            Func<string> outside = () =>
            {
                for (int i = 0; i < 50; i++)
                {
                    int r = rnd.Next(3);
                    string ip = (r == 0 ? "192.0.2." : r == 1 ? "198.51.100." : "203.0.113.") + rnd.Next(2, 254);
                    if (!avoid.Contains(ip)) return ip;
                }
                return "192.0.2.250";
            };
            var siteIp = new Dictionary<string, string>();
            foreach (var site in Sites) { string ip = outside(); avoid.Add(ip); siteIp[site] = ip; }
            Func<DateTime> when = () => t0.AddSeconds(from + (long)(rnd.NextDouble() * (to - from)));
            Func<string, string> q = v => v.Contains(" ") ? "\"" + v + "\"" : v;
            for (int i = 0; i < count; i++)
            {
                int w = rnd.Next(hosts.Count);
                string host = hosts[w], user = users[w % users.Count], ip = ips[w];
                string kind = kinds[rnd.Next(kinds.Count)];
                var t = when();
                // "quiet" accounts and hosts: no background sign-ins for them, so the scenario's own sign-ins tell the story
                if ((kind == "logon" || kind == "ssh") && (quiet.Contains(user) || quiet.Contains(host)))
                {
                    var others = Enumerable.Range(0, hosts.Count).Where(x => !quiet.Contains(users[x % users.Count]) && !quiet.Contains(hosts[x])).ToList();
                    if (others.Count == 0) kind = "process";
                    else { w = others[rnd.Next(others.Count)]; host = hosts[w]; user = users[w % users.Count]; ip = ips[w]; }
                }
                string line;
                switch (kind)
                {
                    case "logon":
                        if ((servers.Count > 0 || dc.Length > 0) && rnd.Next(2) == 0)
                        {
                            string srv = servers.Count > 0 && (dc.Length == 0 || rnd.Next(2) == 0) ? servers[rnd.Next(servers.Count)] : dc;
                            line = "host=" + srv + " source=winsec event=4624 user=" + user + " logon_type=3 src_ip=" + ip + " workstation=" + host + " note=\"An account signed in over the network\"";
                        }
                        else
                        {
                            bool unlock = rnd.Next(3) == 0;
                            line = "host=" + host + " source=winsec event=4624 user=" + user + " logon_type=" + (unlock ? "7" : "2") + " src_ip=127.0.0.1 note=\"" + (unlock ? "The computer was unlocked" : "An account signed in at the keyboard") + "\"";
                        }
                        break;
                    case "process":
                    {
                        int k = rnd.Next(9);
                        string doc = Docs[rnd.Next(Docs.Length)];
                        string[] p =
                            k == 0 ? new[] { "C:\\Windows\\explorer.exe", "C:\\Program Files\\Edge\\msedge.exe", "msedge.exe" } :
                            k == 1 ? new[] { "C:\\Windows\\explorer.exe", "C:\\Program Files\\Office\\outlook.exe", "outlook.exe" } :
                            k == 2 ? new[] { "C:\\Windows\\explorer.exe", "C:\\Program Files\\Office\\winword.exe", "winword.exe C:\\Users\\" + user + "\\Documents\\" + doc + ".docx" } :
                            k == 3 ? new[] { "C:\\Windows\\explorer.exe", "C:\\Program Files\\Office\\excel.exe", "excel.exe C:\\Users\\" + user + "\\Documents\\" + doc + ".xlsx" } :
                            k == 4 ? new[] { "C:\\Windows\\explorer.exe", "C:\\Windows\\System32\\notepad.exe", "notepad.exe C:\\Users\\" + user + "\\Desktop\\" + doc + ".txt" } :
                            k == 5 ? new[] { "C:\\Windows\\System32\\svchost.exe", "C:\\Windows\\System32\\taskhostw.exe", "taskhostw.exe" } :
                            k == 6 ? new[] { "C:\\Program Files\\Edge\\msedge.exe", "C:\\Program Files\\Edge\\msedge.exe", "msedge.exe --type=renderer" } :
                            k == 7 ? new[] { "C:\\Windows\\System32\\svchost.exe", "C:\\Windows\\System32\\SearchProtocolHost.exe", "SearchProtocolHost.exe" } :
                                     new[] { "C:\\Windows\\explorer.exe", "C:\\Windows\\System32\\calc.exe", "calc.exe" };
                        string who = k == 5 || k == 7 ? "SYSTEM" : user;
                        line = "host=" + host + " source=proclog event=1 user=" + who + " image=" + q(p[1]) + " parent=" + q(p[0]) + " cmdline=\"" + p[2] + "\"";
                        break;
                    }
                    case "dns":
                    {
                        bool inside = rnd.Next(3) == 0;
                        string site = inside ? (new[] { "intranet.", "mail.", "files.", "print." })[rnd.Next(4)] + domain : Sites[rnd.Next(Sites.Length)];
                        string ans = inside ? net + "." + (5 + Math.Abs(site.GetHashCodeStable()) % 10) : siteIp[site];
                        line = "host=" + host + " source=dns query=" + site + " type=A answer=" + ans + " src_ip=" + ip;
                        break;
                    }
                    case "firewall":
                        if (rnd.Next(4) == 0)
                        {
                            int[] ports = { 22, 23, 445, 3389, 1433, 5900 };
                            line = "host=" + fw + " source=firewall action=deny direction=in src=" + outside() + " dst=" + pub + " dport=" + ports[rnd.Next(ports.Length)] + " proto=tcp bytes=60";
                        }
                        else
                        {
                            string site = Sites[rnd.Next(Sites.Length)];
                            line = "host=" + fw + " source=firewall action=allow direction=out src=" + ip + " dst=" + siteIp[site] + " dport=443 proto=tcp bytes=" + rnd.Next(2000, 900000);
                        }
                        break;
                    case "web":
                    {
                        if (web.Length == 0) goto case "dns";
                        string page = Pages[rnd.Next(Pages.Length)];
                        int status = rnd.Next(12) == 0 ? 304 : 200;
                        line = "host=" + web + " source=web client=" + outside() + " method=GET path=" + page + " status=" + status + " bytes=" + (status == 304 ? 0 : rnd.Next(400, 40000)) + " agent=\"Mozilla/5.0 (Windows NT 10.0)\"";
                        break;
                    }
                    case "ssh":
                        if (linux.Length == 0) goto case "logon";
                        line = "host=" + linux + " source=auth event=sshd result=accepted method=publickey user=" + user + " src_ip=" + ip;
                        break;
                    default:
                        goto case "logon";
                }
                l.Add(new KeyValuePair<DateTime, string>(t, line));
            }
            return l;
        }
    }
}
