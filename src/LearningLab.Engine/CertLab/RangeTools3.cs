// Cyber Range - more programs for the range's own boxes (each has a manual page in Courses\rhcsa\man):
//   catcher   the practice listening page catcher.range.example: shows what pretend browsers and mails sent there
//   browse    a tiny text-only pretend web browser (shows a page the way a person sees it, with its links and forms)
//   nc        connects to a pretend service on a port and sends it lines (the Linux Ladder's port step)
//   siem      the SOC Analyst's pretend SIEM: alerts and a small search language over the logs it collected
//   cloudctl  the command line for Skyloft Cloud, a pretend cloud provider (buckets, keys, roles, secrets, groups)
//   toyvm     a toy virtual machine of our own (assemble, disassemble, run) for reverse-engineering puzzles
// They only ever read and change the simulator's own pretend data: nothing reaches this PC or a real network.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CertLab
{
    public static partial class Cmd
    {
        static void RegRange3()
        {
            P("catcher", CatcherCmd); P("browse", BrowseCmd); P("nc", NcCmd); P("siem", SiemCmd); P("cloudctl", CloudCmd); P("toyvm", ToyVmCmd);
        }
        static PCtx RootCtx() { return new PCtx { Cr = new Cred { Uid = 0, Gid = 0, User = "root" }, Cwd = "/", Root = "/" }; }

        // ------------------------------------------------------------ catcher
        static int CatcherCmd(Sh sh, List<string> a, Io io)
        {
            var net = RangeOf(sh, io, "catcher"); if (net == null) return 1;
            var o = Opts.Parse(a, "c", "clear|c", "catcher");
            if (o.Error != null) { io.E(o.Error); return 1; }
            if (o.Has("c")) { net.Caught.Clear(); io.L("catcher: cleared."); return 0; }
            io.L("catcher - your practice listening page http://" + RangeShop.Catcher + "/ (Cyber Range, pretend)");
            if (net.Caught.Count == 0) { io.L("Nothing has arrived yet. Pretend browsers and mails that reach " + RangeShop.Catcher + " show up here."); return 0; }
            foreach (var c in net.Caught) io.L(c);
            return 0;
        }

        // ------------------------------------------------------------ browse
        static int BrowseCmd(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "b:d:", "cookie=|b,data=|d", "browse");
            if (o.Error != null || o.Args.Count == 0) { io.E(o.Error ?? "Usage: browse [-b \"name=value\"] [-d \"field=value&...\"] URL"); return 1; }
            string url = o.Args[0]; if (!url.Contains("://")) url = "http://" + url;
            HttpReply r;
            PostData = o.Get("d"); CookieData = o.Get("b");
            try { r = Fetch(sh, url); } finally { PostData = null; CookieData = null; }
            if (r.Error != null) { io.E("browse: " + r.Error); return 1; }
            string html = r.Body ?? "";
            var title = Regex.Match(html, "<title>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            io.L("=== " + (title.Success ? title.Groups[1].Value.Trim() : url) + "   [" + r.Code + " " + r.Reason + "]");
            string body = Regex.Replace(html, "<head>.*?</head>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            var links = new List<string>();
            body = Regex.Replace(body, "<a[^>]*href\\s*=\\s*[\"']?([^\"'\\s>]*)[\"']?[^>]*>(.*?)</a>", m => { links.Add(m.Groups[1].Value); return m.Groups[2].Value + "[" + links.Count + "]"; }, RegexOptions.IgnoreCase | RegexOptions.Singleline);
            var forms = new List<string>();
            foreach (Match f in Regex.Matches(body, "<form([^>]*)>(.*?)</form>", RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                var act = Regex.Match(f.Groups[1].Value, "action\\s*=\\s*[\"']?([^\"'\\s>]*)", RegexOptions.IgnoreCase);
                var met = Regex.Match(f.Groups[1].Value, "method\\s*=\\s*[\"']?(\\w+)", RegexOptions.IgnoreCase);
                var names = Regex.Matches(f.Groups[2].Value, "<(?:input|textarea|select)[^>]*name\\s*=\\s*[\"']?([^\"'\\s>]+)", RegexOptions.IgnoreCase).Cast<Match>().Select(m => m.Groups[1].Value).ToList();
                forms.Add((met.Success ? met.Groups[1].Value.ToUpperInvariant() : "GET") + " " + (act.Success && act.Groups[1].Value.Length > 0 ? act.Groups[1].Value : "(this page)") + "   fields: " + (names.Count == 0 ? "none" : string.Join(", ", names.ToArray())));
            }
            body = Regex.Replace(body, "<form[^>]*>.*?</form>", m => "[form " + (forms.Count == 0 ? "" : "") + "]", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            io.P(RangeShop.StripTags(body));
            if (links.Count > 0) { io.L("Links:"); for (int i = 0; i < links.Count; i++) io.L("  [" + (i + 1) + "] " + links[i]); }
            for (int i = 0; i < forms.Count; i++) io.L("Form " + (i + 1) + ": " + forms[i]);
            io.L("(browse shows a page the way a person sees it. Hidden parts - comments, scripts - only show in the source: curl " + url + ")");
            return r.Code >= 400 ? 1 : 0;
        }

        // ------------------------------------------------------------ nc
        static int NcCmd(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "vzw:", "verbose|v,zero|z,wait=|w", "nc");
            if (o.Error != null) { io.E(o.Error); return 2; }
            if (o.Args.Count < 2) { io.E("Usage: nc [-v] [-z] HOST PORT    (lines from standard input are sent to the service)"); return 2; }
            string host = o.Args[0]; int port;
            if (!int.TryParse(o.Args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out port) || port < 1 || port > 65535) { io.E("Ncat: Invalid port number \"" + o.Args[1] + "\". QUITTING."); return 2; }
            object target; string ip;
            string err = sh.M.Connect(host, port, out target, out ip);
            if (err != null)
            {
                io.E(err == "resolve" ? "Ncat: Could not resolve hostname \"" + host + "\": Name or service not known. QUITTING." : err == "refused" ? "Ncat: Connection refused." : err == "timeout" ? "Ncat: TIMEOUT." : "Ncat: No route to host.");
                return 1;
            }
            if (o.Has("v")) io.E("Ncat: Version 7.92 ( https://nmap.org/ncat )\nNcat: Connected to " + ip + ":" + port + ".");
            if (o.Has("z")) return 0;
            var net = sh.M.Net.Range;
            var svc = net == null ? null : net.LineService(target, port);
            var lines = new List<string>();
            if (io.In != null && !io.In.IsTty) { string l; while ((l = io.In.ReadLine()) != null) lines.Add(l); }
            else { var l = Ask(sh, io, "", false); if (l != null) lines.Add(l); }
            if (svc == null) { if (o.Has("v")) io.E("Ncat: 0 bytes sent, 0 bytes received."); return 0; }
            if (svc.Body.Length > 0) io.L(svc.Body);
            foreach (var l in lines)
            {
                var hit = svc.Logins.FirstOrDefault(x => x.Length > 1 && x[0] == l.Trim());
                if (net != null) net.Noise(sh.M, ip, "connect", 1, "nc to port " + port);
                io.L(hit != null ? hit[1] : (svc.Fail.Length > 0 ? svc.Fail : "Wrong. Try again."));
            }
            return 0;
        }

        // ------------------------------------------------------------ siem
        public const string SiemFile = "/var/lib/siem/events.log";

        /// <summary>One event: field=value pairs (values with spaces in "double quotes").</summary>
        public static Dictionary<string, string> SiemParse(string line)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in Regex.Matches(line, "([A-Za-z_][A-Za-z0-9_.]*)=(\"((?:[^\"\\\\]|\\\\.)*)\"|\\S*)"))
                d[m.Groups[1].Value] = m.Groups[3].Success && m.Groups[2].Value.StartsWith("\"", StringComparison.Ordinal) ? m.Groups[3].Value.Replace("\\\"", "\"") : m.Groups[2].Value;
            return d;
        }
        static List<Dictionary<string, string>> SiemEvents(Sh sh)
        {
            var l = new List<Dictionary<string, string>>();
            string t = sh.M.V.Cat(SiemFile);
            if (t == null) return l;
            int n = 0;
            foreach (var raw in t.Replace("\r", "").Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                var e = SiemParse(line);
                n++;
                if (!e.ContainsKey("id")) e["id"] = n.ToString(CultureInfo.InvariantCulture);
                l.Add(e);
            }
            return l;
        }
        static bool SiemLike(string value, string pattern)
        {
            if (pattern.Contains("*")) return Regex.IsMatch(value ?? "", "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            return string.Equals(value ?? "", pattern, StringComparison.OrdinalIgnoreCase);
        }
        static string SiemLine(Dictionary<string, string> e)
        {
            var sb = new StringBuilder();
            string v;
            sb.Append("#" + e["id"].PadRight(4) + " " + (e.TryGetValue("time", out v) ? v : "").PadRight(20) + " " + (e.TryGetValue("host", out v) ? v : "-").PadRight(10) + " " + (e.TryGetValue("source", out v) ? v : "") + (e.TryGetValue("event", out v) ? "/" + v : ""));
            foreach (var kv in e)
            {
                if (kv.Key == "id" || kv.Key == "time" || kv.Key == "host" || kv.Key == "source" || kv.Key == "event") continue;
                sb.Append("  " + kv.Key + "=" + (kv.Value.Contains(" ") ? "\"" + kv.Value + "\"" : kv.Value));
            }
            return sb.ToString();
        }
        /// <summary>A --since / --until value: HH:MM becomes HH:MM:00; a whole time is kept (T or a space between).</summary>
        static string SiemWhen(string s)
        {
            string t = (s ?? "").Trim().Replace(' ', 'T');
            if (Regex.IsMatch(t, "^\\d{1,2}:\\d{2}$")) t += ":00";
            if (Regex.IsMatch(t, "^\\d:")) t = "0" + t;
            return t;
        }
        /// <summary>Compares an event's time with a --since / --until value (a time of day compares the clock part only).</summary>
        static int SiemAt(string time, string when)
        {
            string v = (time ?? "").Replace(' ', 'T');
            if (!when.Contains("T") && v.Length >= 19) v = v.Substring(11, 8);
            return string.CompareOrdinal(v.Length > when.Length ? v.Substring(0, when.Length) : v, when);
        }

        static int SiemCmd(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "f:s:c:n:rS:U:", "fields=|f,sort=|s,count-by=|c,limit=|n,reverse|r,since=|S,until=|U", "siem");
            if (o.Error != null) { io.E(o.Error); return 1; }
            if (o.Args.Count == 0 || o.Args[0] == "help")
            {
                io.L("siem - the Cyber Range practice SIEM (pretend). It holds the logs the computers sent it.\n  siem alerts                      the alerts its rules raised\n  siem search [TERMS...]           events that match every term:  field=value  field!=value  field~text  word\n         --fields a,b,c  --sort FIELD  --count-by FIELD  --limit N  --reverse\n         --since HH:MM  --until HH:MM   only the events from (or up to) that time\n  siem show ID                     every field of one event\n  siem fields                      the fields and how many events have each\nSee man siem for examples.");
                return o.Args.Count == 0 ? 1 : 0;
            }
            var all = SiemEvents(sh);
            if (all.Count == 0) { io.E("siem: no events collected on this machine (" + SiemFile + ")"); return 1; }
            string verb = o.Args[0];
            var terms = o.Args.Skip(1).ToList();
            if (verb == "alerts") { terms = new List<string> { "source=alert" }; if (!o.Has("f")) o.V["f"] = new List<string> { "id,time,severity,rule,host,note" }; }
            else if (verb == "show")
            {
                if (terms.Count == 0) { io.E("Usage: siem show ID"); return 1; }
                var e = all.FirstOrDefault(x => x["id"] == terms[0].TrimStart('#'));
                if (e == null) { io.E("siem: no event #" + terms[0].TrimStart('#')); return 1; }
                foreach (var kv in e) io.L(kv.Key.PadRight(12) + " " + kv.Value);
                return 0;
            }
            else if (verb == "fields")
            {
                var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var e in all) foreach (var k in e.Keys) { int c; counts.TryGetValue(k, out c); counts[k] = c + 1; }
                io.L(all.Count + " events. Fields (and how many events have them):");
                foreach (var kv in counts.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)) io.L("  " + kv.Key.PadRight(14) + kv.Value);
                io.L("Sources: " + string.Join(", ", all.Select(x => { string s; return x.TryGetValue("source", out s) ? s : ""; }).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()));
                return 0;
            }
            else if (verb != "search") { io.E("siem: unknown command '" + verb + "' (alerts, search, show, fields)"); return 1; }
            IEnumerable<Dictionary<string, string>> hits = all;
            foreach (var t0 in terms)
            {
                string t = t0;
                var m = Regex.Match(t, "^([A-Za-z_][A-Za-z0-9_.]*)(!=|=|~)(.*)$");
                if (m.Success)
                {
                    string f = m.Groups[1].Value, op = m.Groups[2].Value, val = m.Groups[3].Value;
                    if (op == "=") hits = hits.Where(e => { string v; return e.TryGetValue(f, out v) && SiemLike(v, val); }).ToList();
                    else if (op == "!=") hits = hits.Where(e => { string v; return !e.TryGetValue(f, out v) || !SiemLike(v, val); }).ToList();
                    else hits = hits.Where(e => { string v; return e.TryGetValue(f, out v) && v.IndexOf(val, StringComparison.OrdinalIgnoreCase) >= 0; }).ToList();
                }
                else hits = hits.Where(e => e.Values.Any(v => v.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            }
            var list = hits.ToList();
            // --since / --until: a time of day (HH:MM or HH:MM:SS) or a whole time (2026-10-20T09:15:00)
            if (o.Has("S")) { string w = SiemWhen(o.Get("S")); list = list.Where(e => { string v; return e.TryGetValue("time", out v) && SiemAt(v, w) >= 0; }).ToList(); }
            if (o.Has("U")) { string w = SiemWhen(o.Get("U")); list = list.Where(e => { string v; return e.TryGetValue("time", out v) && SiemAt(v, w) <= 0; }).ToList(); }
            string sortBy = o.Get("s", "time");
            list = list.OrderBy(e => { string v; return e.TryGetValue(sortBy, out v) ? v : ""; }, StringComparer.Ordinal).ThenBy(e => int.Parse(e["id"], CultureInfo.InvariantCulture)).ToList();
            if (o.Has("r")) list.Reverse();
            if (o.Has("c"))
            {
                string by = o.Get("c");
                io.L("count  " + by);
                foreach (var g in list.GroupBy(e => { string v; return e.TryGetValue(by, out v) ? v : "(none)"; }, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
                    io.L(g.Count().ToString(CultureInfo.InvariantCulture).PadLeft(5) + "  " + g.Key);
                io.L(list.Count + " event" + (list.Count == 1 ? "" : "s") + " matched");
                return 0;
            }
            int limit; if (o.Has("n") && int.TryParse(o.Get("n"), out limit) && limit >= 0 && list.Count > limit) list = list.Take(limit).ToList();
            if (o.Has("f"))
            {
                var cols = o.Get("f").Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
                var rows = list.Select(e => cols.Select(c => { string v; return e.TryGetValue(c, out v) ? v : ""; }).ToArray()).ToList();
                var w = cols.Select((c, i) => Math.Min(48, Math.Max(c.Length, rows.Count == 0 ? 0 : rows.Max(r => r[i].Length)))).ToArray();
                io.L(string.Join("  ", cols.Select((c, i) => c.PadRight(w[i])).ToArray()).TrimEnd());
                foreach (var r in rows) io.L(string.Join("  ", r.Select((c, i) => c.PadRight(w[i])).ToArray()).TrimEnd());
            }
            else foreach (var e in list) io.L(SiemLine(e));
            io.L(list.Count + " event" + (list.Count == 1 ? "" : "s") + (verb == "alerts" ? " (alerts)" : " matched"));
            return 0;
        }

        // ------------------------------------------------------------ cloudctl (Skyloft Cloud)
        const string CloudRoot = "/var/lib/skyloft";
        static string CloudCat(Sh sh, string rel) { return sh.M.V.Cat(CloudRoot + "/" + rel.TrimStart('/')); }
        static List<string[]> CloudLines(Sh sh, string rel)
        {
            return (CloudCat(sh, rel) ?? "").Replace("\r", "").Split('\n').Select(x => x.Trim()).Where(x => x.Length > 0 && !x.StartsWith("#", StringComparison.Ordinal)).Select(x => Regex.Split(x, "\\s+")).ToList();
        }
        static bool CloudGlob(string pattern, string value)
        {
            if (pattern == "*") return true;
            return Regex.IsMatch(value ?? "", "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$", RegexOptions.IgnoreCase);
        }
        /// <summary>The identity the saved credentials belong to, or null (anonymous).</summary>
        static string CloudWho(Sh sh, bool anonymous, out string problem)
        {
            problem = null;
            if (anonymous) return null;
            string home = (sh.GetVar("HOME") ?? "/root");
            string cred;
            try { cred = sh.M.V.ReadText(sh.P, home + "/.skyloft/credentials"); }
            catch (VfsEx) { return null; }
            var key = Regex.Match(cred, "(?m)^\\s*key\\s*=\\s*(\\S+)"); var sec = Regex.Match(cred, "(?m)^\\s*secret\\s*=\\s*(\\S+)");
            if (!key.Success || !sec.Success) { problem = "the credentials file is missing key= or secret="; return null; }
            foreach (var k in CloudLines(sh, "keys").Concat(CloudLines(sh, "roles").Select(r => r.Length >= 3 ? new[] { r[1], r[2], r[0] } : r)))
                if (k.Length >= 3 && k[0] == key.Groups[1].Value) { if (k[1] != sec.Groups[1].Value) { problem = "the secret doesn't match that key (InvalidSignature)"; return null; } return k[2]; }
            problem = "that access key doesn't exist (InvalidAccessKeyId)";
            return null;
        }
        static List<string[]> CloudPerms(Sh sh, string identity) { return identity == null ? new List<string[]>() : CloudLines(sh, "identities/" + identity).Where(x => x.Length >= 3 && x[0] == "allow").ToList(); }
        static bool CloudCan(Sh sh, string identity, string action, string resource)
        {
            return CloudPerms(sh, identity).Any(p => CloudGlob(p[1], action) && CloudGlob(p[2], resource));
        }
        static bool CloudPublic(Sh sh, string bucket) { return sh.M.V.Exists(CloudRoot + "/buckets/" + bucket + ".public"); }
        static int CloudDenied(Io io, string who, string action, string resource)
        {
            io.E("cloudctl: AccessDenied: " + (who == null ? "anonymous visitors" : "'" + who + "'") + " may not " + action + " on " + resource + (who == null ? " (no credentials: run cloudctl configure KEY SECRET)" : ""));
            return 3;
        }

        static int CloudCmd(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "ao:", "anonymous|a,output=|o", "cloudctl");
            if (o.Error != null) { io.E(o.Error); return 1; }
            var w = o.Args;
            if (w.Count == 0 || w[0] == "help")
            {
                io.L("cloudctl - the command line for Skyloft Cloud (a pretend cloud inside Cyber Range)\n  cloudctl configure KEY SECRET        save credentials (in ~/.skyloft/credentials)\n  cloudctl whoami                      who your credentials belong to, and what they may do\n  cloudctl storage ls [BUCKET[/PATH]]  buckets, or the files in one\n  cloudctl storage get BUCKET/PATH     print a file (-o FILE saves it)\n  cloudctl storage policy BUCKET       who may read a bucket\n  cloudctl iam show [NAME]             an identity's permissions\n  cloudctl iam roles                   the roles\n  cloudctl iam assume ROLE             temporary credentials for a role\n  cloudctl secrets ls | get NAME       the secret store\n  cloudctl network groups              security groups (firewall rules)\n  cloudctl compute ls                  the virtual machines\n  --anonymous                          ask without any credentials\nSee man cloudctl.");
                return w.Count == 0 ? 1 : 0;
            }
            if (!sh.M.V.IsDirG(CloudRoot)) { io.E("cloudctl: could not connect to the endpoint (there is no Skyloft Cloud in this challenge)"); return 2; }
            var net = sh.M.Net == null ? null : sh.M.Net.Range;
            if (w[0] == "configure")
            {
                if (w.Count < 3) { io.E("Usage: cloudctl configure KEY SECRET"); return 1; }
                string home = (sh.GetVar("HOME") ?? "/root");
                try
                {
                    if (!sh.M.V.Resolve(sh.P, home + "/.skyloft", true).Exists) sh.M.V.Mkdir(sh.P, home + "/.skyloft", 0x1C0, false);
                    sh.M.V.WriteText(sh.P, home + "/.skyloft/credentials", "[default]\nkey = " + w[1] + "\nsecret = " + w[2] + "\nregion = sky-west-2\n", false);
                }
                catch (VfsEx e) { io.E("cloudctl: " + e.Message); return 1; }
                io.L("Saved. cloudctl now signs its requests with key " + w[1] + ". Check who that is with: cloudctl whoami");
                return 0;
            }
            string problem;
            string who = CloudWho(sh, o.Has("a"), out problem);
            if (problem != null) { io.E("cloudctl: " + problem); return 3; }
            if (net != null) net.Noise(sh.M, "skyloft", "cloud-api", 1, string.Join(" ", w.ToArray()) + " as " + (who ?? "anonymous"));
            string sub = w.Count > 1 ? w[1] : "";
            switch (w[0])
            {
                case "whoami":
                {
                    if (who == null) { io.L("You are anonymous (no credentials saved). You can only read public buckets."); return 0; }
                    var desc = (CloudCat(sh, "identities/" + who) ?? "").Split('\n').FirstOrDefault(x => x.StartsWith("#", StringComparison.Ordinal));
                    io.L("identity: " + who + (desc != null ? "   (" + desc.TrimStart('#').Trim() + ")" : ""));
                    io.L("account:  " + ((CloudCat(sh, "account") ?? "").Trim().Length > 0 ? CloudCat(sh, "account").Trim() : "sky-000000"));
                    io.L("may:");
                    foreach (var p in CloudPerms(sh, who)) io.L("  " + p[1].PadRight(16) + " " + p[2]);
                    return 0;
                }
                case "storage":
                {
                    string target = w.Count > 2 ? w[2] : "";
                    string bucket = target.Split('/')[0], rest = target.Contains("/") ? target.Substring(target.IndexOf('/') + 1) : "";
                    if (sub == "ls")
                    {
                        if (bucket.Length == 0)
                        {
                            var names = sh.M.V.IsDirG(CloudRoot + "/buckets") ? sh.M.V.G(CloudRoot + "/buckets").Node.Kids.Keys.Where(k => !k.EndsWith(".public", StringComparison.Ordinal)).OrderBy(k => k, StringComparer.Ordinal).ToList() : new List<string>();
                            if (who == null) { io.E("cloudctl: anonymous visitors can't list buckets; give a bucket's name (cloudctl storage ls NAME)"); return 3; }
                            var mine = names.Where(n => CloudCan(sh, who, "storage:list", n)).ToList();
                            if (mine.Count == 0) return CloudDenied(io, who, "storage:list", "any bucket");
                            foreach (var n in mine) io.L(n + (CloudPublic(sh, n) ? "   (public)" : ""));
                            return 0;
                        }
                        if (!sh.M.V.IsDirG(CloudRoot + "/buckets/" + bucket)) { io.E("cloudctl: NoSuchBucket: " + bucket); return 4; }
                        if (!CloudPublic(sh, bucket) && !CloudCan(sh, who, "storage:list", bucket)) return CloudDenied(io, who, "storage:list", bucket);
                        string dir = CloudRoot + "/buckets/" + bucket + (rest.Length > 0 ? "/" + rest.TrimEnd('/') : "");
                        if (!sh.M.V.IsDirG(dir)) { io.E("cloudctl: no such folder in " + bucket + ": " + rest); return 4; }
                        var files = new List<string>();
                        Action<string, string> walk = null;
                        walk = (g, rel) =>
                        {
                            var l = sh.M.V.G(g);
                            foreach (var k in l.Node.Kids.Keys.OrderBy(x => x, StringComparer.Ordinal))
                            {
                                var n = l.Fs.Node(l.Node.Kids[k]);
                                if (n.T == 'd') walk(g + "/" + k, rel + k + "/");
                                else files.Add((n.Text ?? "").Length.ToString(CultureInfo.InvariantCulture).PadLeft(8) + "  " + rel + k);
                            }
                        };
                        walk(dir, rest.Length > 0 ? rest.TrimEnd('/') + "/" : "");
                        foreach (var f in files) io.L(f);
                        io.L(files.Count + " file" + (files.Count == 1 ? "" : "s") + " in " + bucket);
                        return 0;
                    }
                    if (sub == "get")
                    {
                        if (rest.Length == 0) { io.E("Usage: cloudctl storage get BUCKET/PATH"); return 1; }
                        if (!sh.M.V.IsDirG(CloudRoot + "/buckets/" + bucket)) { io.E("cloudctl: NoSuchBucket: " + bucket); return 4; }
                        if (!CloudPublic(sh, bucket) && !CloudCan(sh, who, "storage:read", bucket)) return CloudDenied(io, who, "storage:read", bucket);
                        var t = sh.M.V.Cat(CloudRoot + "/buckets/" + bucket + "/" + rest);
                        if (t == null) { io.E("cloudctl: NoSuchKey: " + bucket + "/" + rest); return 4; }
                        if (o.Has("o")) { try { sh.M.V.WriteText(sh.P, o.Get("o"), t, false); } catch (VfsEx e) { io.E("cloudctl: " + e.Message); return 1; } io.L("saved " + o.Get("o")); }
                        else io.P(t.EndsWith("\n", StringComparison.Ordinal) ? t : t + "\n");
                        return 0;
                    }
                    if (sub == "policy")
                    {
                        if (bucket.Length == 0) { io.E("Usage: cloudctl storage policy BUCKET"); return 1; }
                        if (!sh.M.V.IsDirG(CloudRoot + "/buckets/" + bucket)) { io.E("cloudctl: NoSuchBucket: " + bucket); return 4; }
                        io.L("bucket: " + bucket);
                        io.L("public read: " + (CloudPublic(sh, bucket) ? "YES - anyone on the internet can list and read every file" : "no"));
                        return 0;
                    }
                    io.E("cloudctl storage: ls, get or policy"); return 1;
                }
                case "iam":
                {
                    if (sub == "show")
                    {
                        string name = w.Count > 2 ? w[2] : who;
                        if (name == null) { io.E("cloudctl: sign in first (cloudctl configure KEY SECRET)"); return 3; }
                        if (name != who && !CloudCan(sh, who, "iam:read", name)) return CloudDenied(io, who, "iam:read", name);
                        if (CloudCat(sh, "identities/" + name) == null) { io.E("cloudctl: NoSuchEntity: " + name); return 4; }
                        io.L("identity: " + name);
                        foreach (var p in CloudPerms(sh, name)) io.L("  allow " + p[1].PadRight(16) + " " + p[2]);
                        return 0;
                    }
                    if (sub == "roles")
                    {
                        if (!CloudCan(sh, who, "iam:list", "roles")) return CloudDenied(io, who, "iam:list", "roles");
                        foreach (var r in CloudLines(sh, "roles")) io.L(r[0]);
                        return 0;
                    }
                    if (sub == "assume")
                    {
                        if (w.Count < 3) { io.E("Usage: cloudctl iam assume ROLE"); return 1; }
                        var role = CloudLines(sh, "roles").FirstOrDefault(r => r[0] == w[2]);
                        if (role == null || role.Length < 3) { io.E("cloudctl: NoSuchEntity: role " + w[2]); return 4; }
                        if (!CloudCan(sh, who, "iam:assume", w[2])) return CloudDenied(io, who, "iam:assume", w[2]);
                        io.L("Temporary credentials for role " + w[2] + " (good for one hour):\n  key    " + role[1] + "\n  secret " + role[2] + "\nUse them with: cloudctl configure " + role[1] + " " + role[2]);
                        return 0;
                    }
                    io.E("cloudctl iam: show, roles or assume"); return 1;
                }
                case "secrets":
                {
                    if (sub == "ls")
                    {
                        if (!CloudCan(sh, who, "secrets:list", "*") && !CloudPerms(sh, who).Any(p => p[1] == "secrets:list")) return CloudDenied(io, who, "secrets:list", "the secret store");
                        var names = new List<string>();
                        Action<string, string> walk = null;
                        walk = (g, rel) => { if (!sh.M.V.IsDirG(g)) return; var l = sh.M.V.G(g); foreach (var k in l.Node.Kids.Keys.OrderBy(x => x, StringComparer.Ordinal)) { var n = l.Fs.Node(l.Node.Kids[k]); if (n.T == 'd') walk(g + "/" + k, rel + k + "/"); else names.Add(rel + k); } };
                        walk(CloudRoot + "/secrets", "");
                        foreach (var n in names.Where(n => CloudCan(sh, who, "secrets:list", n))) io.L(n);
                        return 0;
                    }
                    if (sub == "get")
                    {
                        if (w.Count < 3) { io.E("Usage: cloudctl secrets get NAME"); return 1; }
                        var t = CloudCat(sh, "secrets/" + w[2]);
                        if (t == null) { io.E("cloudctl: ResourceNotFound: secret " + w[2]); return 4; }
                        if (!CloudCan(sh, who, "secrets:read", w[2])) return CloudDenied(io, who, "secrets:read", w[2]);
                        io.P(t.EndsWith("\n", StringComparison.Ordinal) ? t : t + "\n");
                        return 0;
                    }
                    io.E("cloudctl secrets: ls or get"); return 1;
                }
                case "network":
                {
                    if (sub != "groups") { io.E("cloudctl network: groups"); return 1; }
                    if (!CloudCan(sh, who, "network:list", "*")) return CloudDenied(io, who, "network:list", "security groups");
                    io.L("GROUP        NAME              PORT   ALLOWED FROM        NOTE");
                    foreach (var g in CloudLines(sh, "groups")) if (g.Length >= 4) io.L(g[0].PadRight(12) + " " + g[1].PadRight(17) + " " + g[2].PadRight(6) + " " + g[3].PadRight(19) + " " + string.Join(" ", g.Skip(4).ToArray()));
                    return 0;
                }
                case "compute":
                {
                    if (sub != "ls") { io.E("cloudctl compute: ls"); return 1; }
                    if (!CloudCan(sh, who, "compute:list", "*")) return CloudDenied(io, who, "compute:list", "virtual machines");
                    io.L("INSTANCE        NAME            ADDRESS          GROUPS              ROLE");
                    foreach (var g in CloudLines(sh, "instances")) if (g.Length >= 4) io.L(g[0].PadRight(15) + " " + g[1].PadRight(15) + " " + g[2].PadRight(16) + " " + g[3].PadRight(19) + " " + (g.Length > 4 ? g[4] : "-"));
                    return 0;
                }
            }
            io.E("cloudctl: unknown command '" + w[0] + "' (try cloudctl help)");
            return 1;
        }

        // ------------------------------------------------------------ toyvm
        static int ToyVmCmd(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "o:", "output=|o", "toyvm");
            if (o.Error != null) { io.E(o.Error); return 1; }
            if (o.Args.Count < 2) { io.E("Usage: toyvm asm SOURCE -o PROGRAM | toyvm disasm PROGRAM | toyvm run PROGRAM [INPUT]"); return 1; }
            string verb = o.Args[0], file = o.Args[1];
            string text;
            try { text = sh.M.V.ReadText(sh.P, file); }
            catch (VfsEx e) { io.E("toyvm: " + file + ": " + e.Message); return 1; }
            if (verb == "asm")
            {
                string err; var code = ToyVm.Assemble(text, out err);
                if (err != null) { io.E("toyvm: " + err); return 1; }
                string outFile = o.Get("o", Regex.Replace(file, "\\.[^./]*$", "") + ".tvm");
                try { sh.M.V.WriteText(sh.P, outFile, ToyVm.Write(code), false); } catch (VfsEx e) { io.E("toyvm: " + outFile + ": " + e.Message); return 1; }
                io.L("toyvm: wrote " + code.Count + " bytes to " + outFile);
                return 0;
            }
            string perr; var prog = ToyVm.Read(text, out perr);
            if (perr != null) { io.E("toyvm: " + file + ": " + perr); return 1; }
            if (verb == "disasm") { foreach (var l in ToyVm.Disassemble(prog)) io.L(l); return 0; }
            if (verb == "run")
            {
                string input = o.Args.Count > 2 ? o.Args[2] : (io.In != null && !io.In.IsTty ? (io.In.ReadLine() ?? "") : (Ask(sh, io, "input: ", false) ?? ""));
                string err; string output = ToyVm.Run(prog, input, out err);
                io.P(output);
                if (output.Length > 0 && !output.EndsWith("\n", StringComparison.Ordinal)) io.P("\n");
                if (err != null) { io.E("toyvm: " + err); return 1; }
                return 0;
            }
            io.E("toyvm: unknown command '" + verb + "' (asm, disasm, run)");
            return 1;
        }
    }


    /// <summary>A toy virtual machine of Cyber Range's own (not a real processor): a byte program and a stack of
    /// numbers 0-255. Instructions: 00 HALT, 01 n PUSH n, 02 ADD, 03 SUB, 04 XOR, 05 OUT (print a character), 06 IN
    /// (the next input character, 0 at the end), 07 DUP, 08 hi lo JMP (jump to address hi*256+lo), 09 hi lo JZ (jump
    /// when the top is 0), 0A EQ (1 when the two top numbers are equal), 0B DROP, 0C SWAP. A program file is the text
    /// "TOYVM 1" and the bytes in hex.</summary>
    public static class ToyVm
    {
        static readonly string[] Names = { "HALT", "PUSH", "ADD", "SUB", "XOR", "OUT", "IN", "DUP", "JMP", "JZ", "EQ", "DROP", "SWAP" };
        static int ArgLen(int op) { return op == 1 ? 1 : op == 8 || op == 9 ? 2 : 0; }

        public static string Write(List<byte> code)
        {
            var sb = new StringBuilder("TOYVM 1\n");
            for (int i = 0; i < code.Count; i++) { sb.Append(code[i].ToString("x2")); sb.Append(i % 16 == 15 ? "\n" : " "); }
            return sb.ToString().TrimEnd() + "\n";
        }
        public static List<byte> Read(string text, out string err)
        {
            err = null;
            var lines = (text ?? "").Replace("\r", "").Split('\n').ToList();
            if (lines.Count == 0 || lines[0].Trim() != "TOYVM 1") { err = "not a toyvm program (it should start with the line TOYVM 1)"; return null; }
            var b = RangeCodes.FromHex(string.Join(" ", lines.Skip(1).ToArray()));
            if (b == null) { err = "the program's bytes aren't hex"; return null; }
            return b.ToList();
        }

        /// <summary>Assembly: one instruction per line (PUSH 65, JZ fail), labels "name:", "; comments", numbers in
        /// decimal, 0x hex or 'c' characters, and three helpers: .text "abc" (prints it), .xortext KEY "abc" (prints
        /// it, kept XORed with KEY in the program) and .check KEY "abc" LABEL (reads input and jumps to LABEL unless it
        /// is abc; the expected letters are kept XORed with KEY).</summary>
        public static List<byte> Assemble(string src, out string err)
        {
            err = null;
            var code = new List<byte>();
            var labels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var fix = new List<KeyValuePair<int, string>>();   // where a 2-byte address goes, the label
            int ln = 0;
            Func<string, int> num = s =>
            {
                s = s.Trim();
                if (s.Length == 3 && s[0] == '\'' && s[2] == '\'') return s[1];
                int v;
                if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && int.TryParse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v)) return v;
                if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return v;
                return -1;
            };
            Action<string> addr = label => { fix.Add(new KeyValuePair<int, string>(code.Count, label)); code.Add(0); code.Add(0); };
            foreach (var raw in (src ?? "").Replace("\r", "").Split('\n'))
            {
                ln++;
                string line = raw;
                int sc = -1; bool q = false;
                for (int i = 0; i < line.Length; i++) { if (line[i] == '"') q = !q; if (line[i] == ';' && !q) { sc = i; break; } }
                if (sc >= 0) line = line.Substring(0, sc);
                line = line.Trim();
                if (line.Length == 0) continue;
                var lab = Regex.Match(line, "^([A-Za-z_][A-Za-z0-9_]*):\\s*(.*)$");
                if (lab.Success) { labels[lab.Groups[1].Value] = code.Count; line = lab.Groups[2].Value.Trim(); if (line.Length == 0) continue; }
                var str = Regex.Match(line, "\"((?:[^\"\\\\]|\\\\.)*)\"");
                string s = str.Success ? Regex.Unescape(str.Groups[1].Value) : "";
                var parts = Regex.Split(str.Success ? line.Replace(str.Value, " ") : line, "\\s+").Where(x => x.Length > 0).ToList();
                string op = parts[0].ToUpperInvariant();
                if (op == ".TEXT") { foreach (var c in s) { code.Add(1); code.Add((byte)c); code.Add(5); } continue; }
                if (op == ".XORTEXT" || op == ".CHECK")
                {
                    int key = parts.Count > 1 ? num(parts[1]) : -1;
                    if (key < 0 || key > 255) { err = "line " + ln + ": " + op.ToLowerInvariant() + " needs a key from 0 to 255"; return null; }
                    if (op == ".CHECK" && parts.Count < 3) { err = "line " + ln + ": .check KEY \"text\" LABEL"; return null; }
                    foreach (var c in s)
                    {
                        if (op == ".XORTEXT") { code.Add(1); code.Add((byte)(c ^ key)); code.Add(1); code.Add((byte)key); code.Add(4); code.Add(5); }
                        else { code.Add(6); code.Add(1); code.Add((byte)key); code.Add(4); code.Add(1); code.Add((byte)(c ^ key)); code.Add(10); code.Add(9); addr(parts[2]); }
                    }
                    continue;
                }
                int opc = Array.IndexOf(Names, op);
                if (opc < 0) { err = "line " + ln + ": unknown instruction " + parts[0]; return null; }
                code.Add((byte)opc);
                if (ArgLen(opc) > 0 && parts.Count < 2) { err = "line " + ln + ": " + op + " needs a value"; return null; }
                if (opc == 1) { int v = num(parts[1]); if (v < 0 || v > 255) { err = "line " + ln + ": PUSH takes a number from 0 to 255"; return null; } code.Add((byte)v); }
                else if (ArgLen(opc) == 2) addr(parts[1]);
            }
            foreach (var f in fix)
            {
                int at;
                if (!labels.TryGetValue(f.Value, out at)) { at = num(f.Value); if (at < 0) { err = "unknown label " + f.Value; return null; } }
                code[f.Key] = (byte)(at >> 8); code[f.Key + 1] = (byte)(at & 255);
            }
            if (code.Count > 4096) { err = "the program is too long"; return null; }
            return code;
        }

        public static List<string> Disassemble(List<byte> code)
        {
            var targets = new HashSet<int>();
            for (int i = 0; i < code.Count; i++) { int op = code[i]; int n = op < Names.Length ? ArgLen(op) : 0; if (n == 2 && i + 2 < code.Count) targets.Add(code[i + 1] * 256 + code[i + 2]); i += n; }
            var l = new List<string>();
            for (int i = 0; i < code.Count; i++)
            {
                int start = i, op = code[i];
                int n = op < Names.Length ? ArgLen(op) : 0;
                string name = op < Names.Length ? Names[op] : "DB 0x" + op.ToString("x2");
                var bytes = new StringBuilder(op.ToString("x2"));
                string arg = "", note = "";
                if (n > 0 && i + n < code.Count)
                {
                    for (int k = 1; k <= n; k++) bytes.Append(" " + code[i + k].ToString("x2"));
                    if (n == 1) { int v = code[i + 1]; arg = "0x" + v.ToString("x2"); if (v >= 32 && v < 127) note = "   ; '" + (char)v + "'"; }
                    else arg = "L" + (code[i + 1] * 256 + code[i + 2]).ToString("x4");
                    i += n;
                }
                string lab = targets.Contains(start) ? "L" + start.ToString("x4") + ":" : "";
                l.Add((lab.PadRight(7) + start.ToString("x4") + ":  " + bytes.ToString().PadRight(10) + name + (arg.Length > 0 ? " " + arg : "") + note).TrimEnd());
            }
            return l;
        }

        public static string Run(List<byte> code, string input, out string err)
        {
            err = null;
            var st = new Stack<int>(); var outText = new StringBuilder();
            int pc = 0, inPos = 0;
            input = input ?? "";
            Func<int> addr = () => { int a = (pc + 1 < code.Count ? code[pc] * 256 + code[pc + 1] : code.Count); pc += 2; return a; };
            for (int steps = 0; steps < 200000; steps++)
            {
                if (pc < 0 || pc >= code.Count) return outText.ToString();
                int at = pc, op = code[pc++];
                Func<int> pop = () => { if (st.Count == 0) throw new InvalidOperationException("the stack is empty at " + at.ToString("x4")); return st.Pop(); };
                try
                {
                    switch (op)
                    {
                        case 0: return outText.ToString();
                        case 1: st.Push(pc < code.Count ? code[pc++] : 0); break;
                        case 2: { int b = pop(), x = pop(); st.Push((x + b) & 255); break; }
                        case 3: { int b = pop(), x = pop(); st.Push((x - b) & 255); break; }
                        case 4: { int b = pop(), x = pop(); st.Push((x ^ b) & 255); break; }
                        case 5: outText.Append((char)pop()); break;
                        case 6: st.Push(inPos < input.Length ? input[inPos++] & 255 : 0); break;
                        case 7: { int x = pop(); st.Push(x); st.Push(x); break; }
                        case 8: pc = addr(); break;
                        case 9: { int t = addr(); if (pop() == 0) pc = t; break; }
                        case 10: { int b = pop(), x = pop(); st.Push(x == b ? 1 : 0); break; }
                        case 11: pop(); break;
                        case 12: { int b = pop(), x = pop(); st.Push(b); st.Push(x); break; }
                        default: err = "unknown instruction 0x" + op.ToString("x2") + " at " + at.ToString("x4"); return outText.ToString();
                    }
                }
                catch (InvalidOperationException e) { err = e.Message; return outText.ToString(); }
                if (st.Count > 1000) { err = "the stack grew too big"; return outText.ToString(); }
            }
            err = "the program ran for too long (stopped)";
            return outText.ToString();
        }
    }
}
