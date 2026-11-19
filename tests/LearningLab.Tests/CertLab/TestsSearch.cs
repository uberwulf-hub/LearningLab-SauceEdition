// Cert Lab - checks of the course search: known questions land on the right lesson section, names such as rd.break
// and /etc/fstab, word endings, quoted phrases, highlighting, exam answers hidden until the exam was taken, and the
// time it takes to build the index.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace CertLab
{
    public static partial class Tests
    {
        static string Top(List<SearchHit> hits, int n)
        {
            return string.Join("\n", hits.Take(n).Select(h => h.Doc.Kind + " " + h.Doc.LessonId + h.Doc.SetId + h.Doc.CardId + h.Doc.SheetId + h.Doc.Man + " [" + h.Doc.Context + "] " + h.Score.ToString("0.00")).ToArray());
        }

        static void SearchTests()
        {
            if (course == null) { Check("the course loads", false); return; }
            var sw = Stopwatch.StartNew();
            var ix = SearchIndex.Build(course);
            long first = sw.ElapsedMilliseconds;
            // warm: the best of three (.NET 10 compiles hot code again in the background and a collection can land in
            // any one build, which App Hub's .NET Framework didn't do)
            long second = long.MaxValue;
            for (int i = 0; i < 3; i++)
            {
                sw.Restart();
                ix = SearchIndex.Build(course);
                second = Math.Min(second, sw.ElapsedMilliseconds);
            }
            Check("the index builds quickly (first " + first + " ms, again " + second + " ms, " + ix.Docs.Count + " chunks)", first < 600 && second < 200, first + " / " + second + " ms");
            Check("every kind is indexed", SearchIndex.KindOrder.All(k => ix.Docs.Any(d => d.Kind == k)), string.Join(", ", SearchIndex.KindOrder.Where(k => !ix.Docs.Any(d => d.Kind == k)).ToArray()));

            Func<SearchDoc, bool> none = d => !d.Secret;
            Func<string, string> topLesson = q => { var h = ix.Find(q, none, 50).FirstOrDefault(x => x.Doc.Kind == "lesson" || x.Doc.Kind == "example"); return h == null ? "" : h.Doc.LessonId; };
            Func<string, SearchHit> topLessonHit = q => ix.Find(q, none, 50).FirstOrDefault(x => x.Doc.Kind == "lesson" || x.Doc.Kind == "example");
            var expect = new[]
            {
                new[] { "rd.break", "l0303" },
                new[] { "autofs", "l0604" },
                new[] { "ACL mask", "l0606" },
                new[] { "fstab UUID", "l0506" },
                new[] { "firewall rich rule", "l0806" },
                new[] { "reset the root password", "l0303" },
            };
            foreach (var e in expect)
                Check("\"" + e[0] + "\" lands on " + e[1], topLesson(e[0]) == e[1], Top(ix.Find(e[0], none, 8), 8));
            var mask = topLessonHit("ACL mask");
            Check("\"ACL mask\" lands on the section about the mask", mask != null && mask.Doc.Context.EndsWith("The mask"), mask == null ? "" : mask.Doc.Context);
            var rich = topLessonHit("firewall rich rule");
            Check("\"firewall rich rule\" lands on the rich rules section", rich != null && rich.Doc.Context.EndsWith("Rich rules"), rich == null ? "" : rich.Doc.Context);
            var fst = topLessonHit("fstab UUID");
            Check("\"fstab UUID\" lands on the fstab section", fst != null && (fst.Doc.Context.EndsWith("/etc/fstab") || fst.Doc.Context.EndsWith("Why UUID or LABEL?")), fst == null ? "" : fst.Doc.Context);

            // words and names
            Check("stems: \"mounting\" finds \"mount\"", SearchIndex.Stem("mounting") == SearchIndex.Stem("mounts") && SearchIndex.Stem("mounts") == "mount");
            Check("stems: rules, policies, running", SearchIndex.Stem("rules") == "rule" && SearchIndex.Stem("policies") == "policy" && SearchIndex.Stem("running") == "run");
            var terms = SearchIndex.Terms("Edit /etc/fstab, then rd.break and Get-ChildItem.");
            Check("names stay whole, with their parts", terms.Contains("/etc/fstab") && terms.Contains("fstab") && terms.Contains("rd.break") && terms.Contains("get-childitem") && terms.Contains("childitem") && terms.Contains("break"), string.Join(" ", terms.ToArray()));
            Check("case does not matter", Top(ix.Find("AUTOFS", none, 5), 5) == Top(ix.Find("autofs", none, 5), 5));
            var fh = ix.Find("/etc/fstab", none, 10);
            Check("/etc/fstab is found", fh.Count > 0 && fh[0].Doc.Raw.Contains("/etc/fstab"), Top(fh, 5));
            var rdb = ix.Find("rd.break", none, 300);
            Check("a name finds only places with the name (not every \"break\")", rdb.Count > 0 && rdb.All(h => h.Doc.Raw.Contains("rd.break")), Top(rdb.Where(h => !h.Doc.Raw.Contains("rd.break")).ToList(), 5));
            var su = ix.Find("systemd.unit", none, 50);
            Check("systemd.unit finds systemd.unit=emergency.target", su.Any(h => h.Doc.Raw.Contains("systemd.unit=emergency.target")), Top(su, 5));
            Check("an unknown name falls back to its parts", ix.Find("rd.brake", none, 10).Count > 0);
            var cmd = ix.Find("grubby", none, 30);
            Check("a command's name finds its cheat row, card and manual page", cmd.Any(h => h.Doc.Kind == "cheat") && cmd.Any(h => h.Doc.Kind == "card") && cmd.Any(h => h.Doc.Kind == "man" && h.Doc.Man == "grubby.8"), Top(cmd, 30));
            var manTop = cmd.FirstOrDefault(h => h.Doc.Kind == "man");
            Check("the manual page's own name ranks its page first among pages", manTop != null && manTop.Doc.Man == "grubby.8", manTop == null ? "" : manTop.Doc.Man);

            // phrases
            var ph = ix.Find("\"remount it read-write\"", none, 50);
            Check("a quoted phrase must appear as written", ph.Count > 0 && ph.All(h => h.Doc.Raw.Contains("remount it read-write")), Top(ph, 5));
            var loose = ix.Find("remount it read-write", none, 200);
            Check("without quotes more places match", loose.Count > ph.Count, loose.Count + " vs " + ph.Count);
            Check("a phrase that is nowhere finds nothing", ix.Find("\"autofs rich rule\"", none, 10).Count == 0);

            // highlighting
            var hl = ix.Find("rd.break", none, 5).First();
            Check("the snippet marks the match", hl.Marks.Count > 0 && hl.Marks.All(m => hl.Snippet.Substring(m[0], m[1]).ToLowerInvariant() == "rd.break"), hl.Snippet + " " + string.Join(",", hl.Marks.Select(m => m[0] + ":" + m[1]).ToArray()));
            var hl2 = ix.Find("mounting", none, 5).First();
            Check("word endings are marked too", hl2.Marks.Any(m => hl2.Snippet.Substring(m[0], m[1]).ToLowerInvariant().StartsWith("mount")), hl2.Snippet);
            var hl3 = ix.Find("\"root password\"", none, 50).First(h => h.Doc.Text.ToLowerInvariant().Contains("root password"));
            Check("phrases are marked whole", hl3.Marks.Any(m => hl3.Snippet.Substring(m[0], m[1]).ToLowerInvariant() == "root password"), hl3.Snippet);
            Check("snippets are short", ix.Find("the", none, 50).All(h => h.Snippet.Length <= 230));

            // exam answers stay hidden until the exam was taken
            var ex1 = course.Exams.First();
            var secretWords = ex1.Items.Where(i => i.Explain.Length > 0).Select(i => i.Explain).First();
            string probe = "\"" + string.Join(" ", secretWords.ToLowerInvariant().Replace("`", "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Take(5).ToArray()) + "\"";
            Func<SearchDoc, bool> notTaken = d => !d.Secret;
            Func<SearchDoc, bool> taken = d => !d.Secret || d.SetId == ex1.Id;
            Check("an untaken exam's review is never found", ix.Find(probe, notTaken, 100).All(h => !h.Doc.Secret) && ix.Docs.Any(d => d.Secret), probe);
            Check("after the exam its review is found", ix.Find(probe, taken, 100).Any(h => h.Doc.Secret && h.Doc.SetId == ex1.Id), probe + "\n" + Top(ix.Find(probe, taken, 5), 5));
            Check("exam tasks themselves are found before", ix.Find("interrupt the boot process", notTaken, 50).Any(h => h.Doc.Kind == "exam" && !h.Doc.Secret));
            Check("model solutions of exams are only in hidden chunks", ix.Docs.Where(d => d.Kind == "exam" && !d.Secret).All(d => !d.Text.Contains("passwd --stdin")));
            Check("the search page's filter hides untaken exams", SearchIndex.Allows(new HashSet<string>())(ix.Docs.First(d => d.Secret)) == false && SearchIndex.Allows(new HashSet<string> { ex1.Id })(ix.Docs.First(d => d.Secret && d.SetId == ex1.Id)));
            Check("nothing for an empty query", ix.Find("  ", none, 10).Count == 0 && ix.Find("\"\"", none, 10).Count == 0);
            Check("nothing for nonsense", ix.Find("zzqxv", none, 10).Count == 0);

        }

    }
}
