// Cert Lab - Security+ checks, part 2: search over the course, the security tools on the RHEL machines (openssl,
// nmap, certlab-mkcert), every lab and try-it (fails at the start, passes with its solution), every exam from 100 to
// 900 with the model answers on two seeds, and the PBQ views built off screen.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace CertLab
{
    public static partial class Tests
    {
        static void SecSearchTests()
        {
            var c = Sec();
            var ix = SearchIndex.Build(c);
            Func<SearchDoc, bool> open = d => !d.Secret;
            Check("every lesson is indexed", c.AllLessons.All(l => ix.Docs.Any(d => d.LessonId == l.Id)), string.Join(", ", c.AllLessons.Where(l => !ix.Docs.Any(d => d.LessonId == l.Id)).Select(l => l.Id).ToArray()));
            Check("every exam item is indexed", c.Exams.All(e => e.Items.All(i => ix.Docs.Any(d => d.SetId == e.Id && d.ItemId == i.Id && !d.Secret))));
            Check("every PBQ practice scenario is indexed", c.Practice.All(p => p.Items.All(i => ix.Docs.Any(d => d.SetId == p.Id && d.ItemId == i.Id))));
            Check("every card and cheat row is indexed", ix.Docs.Count(d => d.Kind == "card") == c.Decks.Sum(d => d.Cards.Count) && ix.Docs.Count(d => d.Kind == "cheat") == c.Sheets.Sum(s => s.Sections.Sum(x => x.Rows.Count)));
            foreach (var q in new[] { "CVSS", "DMARC", "tabletop exercise", "annualized loss expectancy", "order of volatility", "zero trust", "salting", "screened subnet", "tokenization", "OCSP stapling" })
            {
                var h = ix.Find(q, open, 10);
                Check("search finds '" + q + "'", h.Count > 0, q);
            }
            var hit = ix.Find("CVSS", open, 50).FirstOrDefault(x => x.Doc.Kind == "card");
            Check("the CVSS acronym card is found", hit != null);
            Check("exam answers stay secret until taken", c.Exams.All(e => e.Items.Where(i => i.Pbq != null).All(i => ix.Docs.Any(d => d.SetId == e.Id && d.ItemId == i.Id && d.Secret))));
        }

        static void SecToolTests()
        {
            var t = new T(2);
            t.Run("cd /root");
            t.Run("/usr/libexec/certlab/certlab-mkcert web.pem subject=\"CN=shop.example.com,O=Lakeside\" issuer=\"CN=Lakeside CA\" days=90 san=shop.example.com,www.shop.example.com eku=serverAuth");
            t.Run("/usr/libexec/certlab/certlab-mkcert ca.pem subject=\"CN=Lakeside CA\" ca=yes days=3650");
            t.Run("/usr/libexec/certlab/certlab-mkcert old.pem subject=CN=old.example.com issuer=\"CN=Lakeside CA\" notbefore=2024-01-01 notafter=2025-01-01");
            t.Run("/usr/libexec/certlab/certlab-mkcert self.pem subject=CN=nas.lan");
            Check("x509 -subject", t.Run("openssl x509 -in web.pem -noout -subject") == "subject=CN = shop.example.com, O = Lakeside\n", t.Last);
            Check("x509 -text shows the SAN and EKU", t.Run("openssl x509 -in web.pem -noout -text").Contains("DNS:shop.example.com, DNS:www.shop.example.com") && t.Last.Contains("TLS Web Server Authentication") && t.Last.Contains("CA:FALSE"), t.Last);
            Check("x509 -enddate in OpenSSL's form", System.Text.RegularExpressions.Regex.IsMatch(t.Run("openssl x509 -in web.pem -noout -enddate"), "^notAfter=[A-Z][a-z]{2} [ \\d]\\d \\d\\d:\\d\\d:\\d\\d 20\\d\\d GMT\n$"), t.Last);
            Check("verify: OK with the CA", t.Run("openssl verify -CAfile ca.pem web.pem") == "web.pem: OK\n", t.Last);
            Check("verify: no CA file is an unknown issuer", t.Run("openssl verify web.pem 2>&1").Contains("error 20 at 0 depth lookup: unable to get local issuer certificate"), t.Last);
            Check("verify: expired", t.Run("openssl verify -CAfile ca.pem old.pem 2>&1").Contains("certificate has expired") && t.Run("openssl verify -CAfile ca.pem old.pem >/dev/null 2>&1; echo $?") == "2\n", t.Last);
            Check("verify: self-signed", t.Run("openssl verify self.pem 2>&1").Contains("self-signed certificate"), t.Last);
            Check("x509 -checkend", t.Run("openssl x509 -in web.pem -noout -checkend 864000; echo $?") == "Certificate will not expire\n0\n" && t.Run("openssl x509 -in web.pem -noout -checkend 8640000; echo $?") == "Certificate will expire\n1\n", t.Last);
            Check("a file that isn't a certificate", t.Run("openssl x509 -in /etc/hosts -noout -subject 2>&1").Contains("Unable to load certificate"), t.Last);
            t.Run("echo hello > f.txt");
            string sum = t.Run("sha256sum f.txt").Split(' ')[0];
            Check("dgst matches sha256sum", t.Run("openssl dgst -sha256 f.txt") == "SHA2-256(f.txt)= " + sum + "\n", t.Last);
            Check("rand -hex 16 gives 32 hex digits", System.Text.RegularExpressions.Regex.IsMatch(t.Run("openssl rand -hex 16"), "^[0-9a-f]{32}\n$"), t.Last);
            Check("certlab-mkcert is for root only", t.Run("su - student -c '/usr/libexec/certlab/certlab-mkcert /tmp/x.pem subject=CN=x' 2>&1").Contains("only the graders") || t.Last.Contains("Permission denied"), t.Last);
            // nmap: not installed at first; after dnf install it sees server2's ssh, and the firewall's effect
            Check("nmap needs its package", t.Run("nmap server2 2>&1").Contains("command not found"), t.Last);
            t.Run("printf '[app]\\nname=AppStream\\nbaseurl=http://repo.lab.example.com/AppStream\\ngpgcheck=0\\n' > /etc/yum.repos.d/app.repo");
            t.Run("dnf install -y nmap >/dev/null 2>&1");
            string scan = t.Run("nmap server2");
            Check("nmap: ssh open on server2", scan.Contains("22/tcp ") && scan.Contains("open") && scan.Contains("Nmap scan report for server2") && scan.Contains("1 host up"), scan);
            Check("nmap: a closed port with -p", t.Run("nmap -p 2222 server2").Contains("2222/tcp closed") || t.Last.Contains("2222/tcp filtered"), t.Last);
            t.On("server2", "firewall-cmd --remove-service=ssh");
            Check("nmap: ssh filtered after the firewall drops it", t.Run("nmap -p 22 server2").Contains("22/tcp filtered"), t.Last);
            t.On("server2", "firewall-cmd --add-service=ssh");
            Check("nmap -sV names OpenSSH", t.Run("nmap -sV -p 22 server2").Contains("OpenSSH"), t.Last);
            Check("nmap -sn: host up", t.Run("nmap -sn server2").Contains("Host is up"), t.Last);
            t.Dispose();
        }

        static void SecSetTests(string which)
        {
            var c = Sec();
            if (which == "tries")
            {
                foreach (var l in c.AllLessons.Where(x => x.Try != null)) { if (!string.IsNullOrEmpty(OnlyFile) && !l.Id.StartsWith(OnlyFile)) continue; l.Try.Id = l.Id; RunSet(l.Try, 3, false, "try " + l.Id); }
                return;
            }
            var noMan = new SortedSet<string>();
            foreach (var lab in c.Labs.Where(x => Wanted(x.File)))
            {
                foreach (var it in lab.Items) foreach (var st in SolutionCommands(it.Solution)) foreach (var w in CommandsIn(st.Line))
                        { Check(lab.Id + " " + it.Id + ": '" + w + "' exists", Known(w), st.Line); if (Sh.Programs.ContainsKey(w) && Man.Find(w, null) == null) noMan.Add(w); }
                Check(lab.Id + ": unit known", c.Unit(lab.Unit) != null, lab.Unit);
                Check(lab.Id + ": every task has checks, a solution, a hint and an explanation", lab.Items.All(i => i.Checks.Count > 0 && i.Solution.Count > 0 && i.Hints.Count > 0 && i.Explain.Length > 0));
                RunSet(lab, 5, false, "lab " + lab.Id);
            }
            Check("every command the labs use has a manual page", noMan.Count == 0, string.Join(", ", noMan.ToArray()));
        }

        static void SecExamTests()
        {
            var c = Sec();
            foreach (var ex in c.Exams.Where(x => Wanted(x.File)))
                foreach (var seed in new[] { 11, 29 })
                {
                    var inst = ex.Instance(seed);
                    var none = TaskRunner.Grade(null, inst, true, new Dictionary<string, int>(), new Dictionary<string, List<string>>());
                    Check(ex.Id + " (seed " + seed + "): no answers score 0, shown as 100", none.Score == 0 && c.Scale(none.Score, none.Max) == 100, none.Score.ToString());
                    var ch = new Dictionary<string, int>(); var more = new Dictionary<string, List<string>>();
                    TaskRunner.ModelAnswers(inst, ch, more);
                    var full = TaskRunner.Grade(null, inst, true, ch, more);
                    Check(ex.Id + " (seed " + seed + "): the model answers score everything, shown as 900", full.Score == full.Max && c.Scale(full.Score, full.Max) == 900, full.Score + "/" + full.Max + " " + string.Join(", ", full.Items.Where(i => !i.Pass).Select(i => i.Item.Id).ToArray()));
                    Check(ex.Id + " (seed " + seed + "): results for every domain", c.Objectives.All(o => full.PerObjective.ContainsKey(o)));
                    // about 80% right is just under the mark, about 85% a pass
                    var part = new Dictionary<string, int>(ch);
                    var qs = inst.Items.Where(i => i.Kind == "choice").ToList();
                    int drop = (int)Math.Ceiling(full.Max * 0.20);
                    foreach (var it in qs.Take(drop)) part.Remove(it.Id);
                    var low = TaskRunner.Grade(null, inst, false, part, more);
                    Check(ex.Id + " (seed " + seed + "): 80% right is below 750", c.Scale(low.Score, low.Max) < 750, c.Scale(low.Score, low.Max).ToString());
                    part = new Dictionary<string, int>(ch);
                    foreach (var it in qs.Take((int)Math.Floor(full.Max * 0.15))) part.Remove(it.Id);
                    var hi = TaskRunner.Grade(null, inst, false, part, more);
                    Check(ex.Id + " (seed " + seed + "): 85% right passes", c.Scale(hi.Score, hi.Max) >= 750, c.Scale(hi.Score, hi.Max).ToString());
                }
        }

    }
}
