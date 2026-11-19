// Cert Lab - search over a course: lessons (one chunk per paragraph, list, example or note, under its section
// heading), try-it tasks, quiz questions, labs, practice exams (their explanations and model solutions only once the
// person has taken that exam), flash cards, cheat sheet rows and manual pages (one chunk per section). Ranked with
// BM25 (headings and titles count more, a command's own name more still), all terms preferred over some; matching
// ignores case, strips simple English endings (mounts -> mount), keeps names such as rd.break, /etc/fstab and
// Get-ChildItem whole (and their parts), and "quoted phrases" must appear as written. Built from the course's own
// data files, so it works for every course.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CertLab
{
    public class SearchDoc
    {
        /// <summary>lesson, example, try, quiz, lab, exam, card, cheat, man.</summary>
        public string Kind = "";
        public string Title = "", Sub = "", Context = "", Text = "", Command = "";
        /// <summary>An exam's explanation and model solution: shown only after that exam was taken.</summary>
        public bool Secret;
        // where it is
        public string LessonId = "", Part = "", SetId = "", ItemId = "", DeckId = "", CardId = "", SheetId = "", Man = "";
        public int Index = -1, Section = -1, Row = -1;
        internal Dictionary<string, int> Tf;   // term -> weighted count (title terms count more)
        internal int Len, Ord;
        string raw;
        /// <summary>The lower case text, for phrases (made when a phrase is first searched for).</summary>
        public string Raw { get { if (raw == null) raw = SearchIndex.Norm(Title + "\n" + Sub + "\n" + Text); return raw; } }
    }

    public class SearchHit
    {
        public SearchDoc Doc;
        public double Score;
        public string Snippet = "";
        /// <summary>Highlighted parts of Snippet: start and length.</summary>
        public List<int[]> Marks = new List<int[]>();
    }

    public class SearchIndex
    {
        public readonly List<SearchDoc> Docs = new List<SearchDoc>();
        readonly Dictionary<string, List<SearchDoc>> postings = new Dictionary<string, List<SearchDoc>>();
        double avgLen = 1;
        public long BuildMs;
        /// <summary>Where the build time went (milliseconds per part).</summary>
        public string Timing = "";
        public string CourseId = "";

        public static readonly string[] KindOrder = { "lesson", "example", "try", "quiz", "lab", "exam", "card", "cheat", "man" };
        public static string KindTitle(string k)
        {
            switch (k)
            {
                case "lesson": return "Lessons";
                case "example": return "Worked examples";
                case "try": return "Try it on the practice machines";
                case "quiz": return "Quiz questions";
                case "lab": return "Labs";
                case "exam": return "Practice exams";
                case "card": return "Flash cards";
                case "cheat": return "Cheat sheets";
                case "man": return "Manual pages";
            }
            return k;
        }

        // ------------------------------------------------------------ building
        /// <summary>What a search may show: everything but the explanations and model solutions of practice exams the
        /// person hasn't taken yet (taken = the exam ids in their progress). Was App Hub's MainView.SearchAllows.</summary>
        public static Func<SearchDoc, bool> Allows(HashSet<string> taken) { return d => !d.Secret || taken.Contains(d.SetId); }

        public static SearchIndex Build(CourseInfo c)
        {
            var sw = Stopwatch.StartNew();
            var ix = new SearchIndex { CourseId = c.Id };
            foreach (var u in c.Units)
            {
                if (u.Intro.Length > 0 && u.Lessons.Count > 0)
                    ix.Add(new SearchDoc { Kind = "lesson", Title = u.Title, Context = u.Title, Text = u.Intro, LessonId = u.Lessons[0].Id, Part = "top" });
                foreach (var l in u.Lessons)
                {
                    string heading = "";
                    for (int i = 0; i < l.Blocks.Count; i++)
                    {
                        var b = l.Blocks[i];
                        if (b.Kind == "h") { heading = b.Text; continue; }
                        string text = b.Kind == "ul" ? string.Join("\n", b.Items.ToArray()) : b.Text;
                        bool ex = b.Kind == "terminal" || b.Kind == "file";
                        ix.Add(new SearchDoc
                        {
                            Kind = ex ? "example" : "lesson",
                            Title = heading.Length > 0 ? heading : l.Title, Sub = heading.Length > 0 ? l.Title : "",
                            Context = u.Title + "  >  " + l.Title + (heading.Length > 0 ? "  >  " + heading : ""),
                            Text = text, LessonId = l.Id, Part = "block", Index = i
                        });
                    }
                    if (l.Try != null)
                        for (int i = 0; i < l.Try.Items.Count; i++)
                        {
                            var it = l.Try.Items[i];
                            ix.Add(new SearchDoc { Kind = "try", Title = l.Title, Context = u.Title + "  >  " + l.Title + "  >  Try it", Text = Join(Fill(l.Try, it.Text), Join(it.Hints.Select(h => Fill(l.Try, h))), Fill(l.Try, it.Explain), Solution(l.Try, it)), LessonId = l.Id, Part = "try", Index = i });
                        }
                    for (int i = 0; i < l.Quiz.Count; i++)
                    {
                        var q = l.Quiz[i];
                        ix.Add(new SearchDoc { Kind = "quiz", Title = l.Title, Context = u.Title + "  >  " + l.Title + "  >  Quiz, question " + (i + 1), Text = Join(q.Q, Join(q.Choices), q.Why), LessonId = l.Id, Part = "quiz", Index = i });
                    }
                }
            }
            long tLessons = sw.ElapsedMilliseconds;
            foreach (var lab in c.Labs)
                foreach (var it in lab.Items)
                    ix.Add(new SearchDoc { Kind = "lab", Title = lab.Title, Context = lab.Title + "  >  task " + (lab.Items.IndexOf(it) + 1), Text = Join(Fill(lab, it.Text), Join(it.Hints.Select(h => Fill(lab, h))), Fill(lab, it.Explain), Solution(lab, it)), SetId = lab.Id, ItemId = it.Id });
            foreach (var exam in c.Exams)
                foreach (var cs in exam.Cases)
                {
                    var first = exam.Items.FirstOrDefault(i => i.Case == cs.Id);
                    foreach (var sec in cs.Sections)
                        ix.Add(new SearchDoc { Kind = "exam", Title = exam.Title, Context = exam.Title + "  >  case study " + cs.Title + (sec.Key.Length > 0 ? "  >  " + sec.Key : ""), Text = Join(Fill(exam, cs.Title), Fill(exam, sec.Value)), SetId = exam.Id, ItemId = first == null ? "" : first.Id });
                }
            foreach (var exam in c.Exams)
                foreach (var it in exam.Items)
                {
                    string ctx = exam.Title + "  >  " + (it.IsQuestion ? "question " : "task ") + (exam.Items.IndexOf(it) + 1);
                    ix.Add(new SearchDoc { Kind = "exam", Title = exam.Title, Context = ctx, Text = Join(Fill(exam, it.Pbq != null ? it.Pbq.PublicText() : it.Text), Join(it.Choices)), SetId = exam.Id, ItemId = it.Id });
                    string secret = Join(Join(it.Hints.Select(h => Fill(exam, h))), Fill(exam, it.Pbq != null ? it.Pbq.AnswerText() : it.Explain), Solution(exam, it));
                    if (secret.Length > 0) ix.Add(new SearchDoc { Kind = "exam", Title = exam.Title, Context = ctx + "  >  review", Text = secret, SetId = exam.Id, ItemId = it.Id, Secret = true });
                }
            foreach (var ps in c.Practice)
                foreach (var it in ps.Instance(0).Items.Where(x => x.Pbq != null))
                    ix.Add(new SearchDoc { Kind = "lab", Title = "PBQ practice: " + ps.Title, Context = "PBQ practice  >  " + ps.Title + "  >  scenario " + (ps.Items.FindIndex(x => x.Id == it.Id) + 1), Text = Join(it.Pbq.PublicText(), it.Pbq.AnswerText()), SetId = ps.Id, ItemId = it.Id });
            long tSets = sw.ElapsedMilliseconds;
            foreach (var d in c.Decks)
                foreach (var card in d.Cards)
                    ix.Add(new SearchDoc { Kind = "card", Title = d.Title, Context = "Flash cards  >  " + d.Title, Text = card.Front + "\n" + card.Back, Command = FirstWord(card.Command), DeckId = d.Id, CardId = card.Id });
            foreach (var sh in c.Sheets)
                for (int s = 0; s < sh.Sections.Count; s++)
                    for (int r = 0; r < sh.Sections[s].Rows.Count; r++)
                    {
                        var row = sh.Sections[s].Rows[r];
                        ix.Add(new SearchDoc { Kind = "cheat", Title = sh.Sections[s].Title + " " + row.Cmd, Context = "Cheat sheets  >  " + sh.Title + (sh.Sections[s].Title.Length > 0 ? "  >  " + sh.Sections[s].Title : ""), Text = row.Cmd + "  -  " + row.Meaning, Command = FirstWord(row.Cmd), SheetId = sh.Id, Section = s, Row = r });
                    }
            long tCards = sw.ElapsedMilliseconds;
            string manDir = c.Dir.Length > 0 ? Path.Combine(c.Dir, "man") : "";
            if (manDir.Length > 0 && Directory.Exists(manDir))
                foreach (var f in Directory.GetFiles(manDir).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    string name = Path.GetFileName(f);
                    int dot = name.LastIndexOf('.');
                    string cmd = dot > 0 ? name.Substring(0, dot) : name, sec = dot > 0 ? name.Substring(dot + 1) : "";
                    var secs = ManSections(File.ReadAllText(f));
                    for (int i = 0; i < secs.Count; i++)
                        ix.Add(new SearchDoc { Kind = "man", Title = cmd + " " + secs[i].Key, Context = "Manual page  >  " + cmd + "(" + sec + ")  >  " + secs[i].Key, Text = secs[i].Value, Command = cmd.ToLowerInvariant(), Man = name, Section = i });
                }
            long tMan = sw.ElapsedMilliseconds;
            ix.Finish();
            ix.BuildMs = sw.ElapsedMilliseconds;
            ix.Timing = "lessons " + tLessons + ", labs and exams " + (tSets - tLessons) + ", cards and sheets " + (tCards - tSets) + ", manual pages " + (tMan - tCards) + ", postings " + (ix.BuildMs - tMan);
            return ix;
        }

        /// <summary>A manual page's sections: heading (NAME, SYNOPSIS...) and its text.</summary>
        public static List<KeyValuePair<string, string>> ManSections(string text)
        {
            var l = new List<KeyValuePair<string, string>>();
            string head = "";
            var body = new StringBuilder();
            foreach (var raw in text.Replace("\r", "").Split('\n'))
            {
                bool heading = raw.Length > 0 && raw[0] >= 'A' && raw[0] <= 'Z';
                if (heading) foreach (char ch in raw) if (char.IsLower(ch)) { heading = false; break; }
                if (heading)
                {
                    if (head.Length > 0 || body.ToString().Trim().Length > 0) l.Add(new KeyValuePair<string, string>(head, body.ToString().Trim('\n')));
                    head = raw.Trim(); body.Clear();
                    continue;
                }
                body.Append(raw).Append('\n');
            }
            if (head.Length > 0 || body.ToString().Trim().Length > 0) l.Add(new KeyValuePair<string, string>(head, body.ToString().Trim('\n')));
            return l;
        }

        static string Join(params string[] parts) { return string.Join("\n", parts.Where(p => !string.IsNullOrEmpty(p)).ToArray()); }
        static string Join(IEnumerable<string> parts) { return string.Join("\n", parts.Where(p => !string.IsNullOrEmpty(p)).ToArray()); }
        static string FirstWord(string cmd) { if (string.IsNullOrEmpty(cmd)) return ""; var w = cmd.Trim().Split(' ')[0].Trim('`'); return w.ToLowerInvariant(); }
        /// <summary>{name} placeholders read as their first value (the exam's details change per attempt).</summary>
        public static string Fill(TaskSet t, string s)
        {
            if (string.IsNullOrEmpty(s) || t.Vars.Count == 0) return s ?? "";
            return Regex.Replace(s, "\\{(\\w+)\\}", m => { List<string> v; return t.Vars.TryGetValue(m.Groups[1].Value, out v) && v.Count > 0 ? v[0] : m.Value; });
        }
        static string Solution(TaskSet t, TaskItem it) { return Fill(t, string.Join("\n", it.Solution.Select(TaskRunner.StepText).Where(x => x != null).ToArray())); }

        void Add(SearchDoc d)
        {
            d.Tf = new Dictionary<string, int>();
            foreach (var t in Terms(d.Title)) { int n; d.Tf.TryGetValue(t, out n); d.Tf[t] = n + 3; }
            foreach (var t in Terms(d.Sub)) { int n; d.Tf.TryGetValue(t, out n); d.Tf[t] = n + 1; }
            foreach (var t in Terms(d.Text)) { int n; d.Tf.TryGetValue(t, out n); d.Tf[t] = n + 1; d.Len++; }
            if (d.Command.Length > 0) { int n; d.Tf.TryGetValue(d.Command, out n); d.Tf[d.Command] = n + 4; }
            d.Ord = Docs.Count;
            Docs.Add(d);
        }

        void Finish()
        {
            foreach (var d in Docs)
                foreach (var t in d.Tf.Keys)
                {
                    List<SearchDoc> l;
                    if (!postings.TryGetValue(t, out l)) { l = new List<SearchDoc>(); postings[t] = l; }
                    l.Add(d);
                }
            avgLen = Docs.Count == 0 ? 1 : Math.Max(1, Docs.Average(d => (double)d.Len));
        }

        static readonly Regex Spaces = new Regex("\\s+", RegexOptions.Compiled);
        internal static string Norm(string s) { return Spaces.Replace(s.ToLowerInvariant().Replace("`", "").Replace("**", ""), " "); }

        // ------------------------------------------------------------ words
        static readonly Regex WordRx = new Regex("[\\w./\\-+:=@~$]+", RegexOptions.Compiled);

        /// <summary>The terms of a text: names with dots, slashes and dashes whole (rd.break, /etc/fstab,
        /// get-childitem) and their parts, every word stemmed.</summary>
        public static List<string> Terms(string text)
        {
            var r = new List<string>();
            if (string.IsNullOrEmpty(text)) return r;
            int n = text.Length, i = 0;
            var sb = new StringBuilder();
            while (i < n)
            {
                while (i < n && !IsWordChar(text[i])) i++;
                sb.Clear();
                while (i < n && IsWordChar(text[i])) { sb.Append(char.ToLowerInvariant(text[i])); i++; }
                if (sb.Length == 0) continue;
                string w = sb.ToString().Trim('.', ':', ',', '=', '+', '-');
                if (w.Length == 0) continue;
                bool compound = false;
                foreach (char ch in w) if (!char.IsLetterOrDigit(ch) && ch != '_') { compound = true; break; }
                if (!compound) { r.Add(Stem(w)); continue; }
                r.Add(w);
                if (w[0] == '/') r.Add(w.Substring(1));
                // systemd.unit=rescue.target: both sides of = and : count as names too
                if (w.IndexOf('=') > 0 || w.IndexOf(':') > 0)
                    foreach (var side in w.Split('=', ':'))
                    {
                        string sd = side.Trim('.', '-', '+', '\'', '"');
                        if (sd.Length > 0 && sd != w) foreach (char ch in sd) if (!char.IsLetterOrDigit(ch) && ch != '_') { r.Add(sd); break; }
                    }
                int a = 0;
                for (int j = 0; j <= w.Length; j++)
                {
                    if (j < w.Length && (char.IsLetterOrDigit(w[j]) || w[j] == '_')) continue;
                    if (j > a) r.Add(Stem(w.Substring(a, j - a)));
                    a = j + 1;
                }
            }
            return r;
        }
        static bool IsWordChar(char c) { return char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '/' || c == '-' || c == '+' || c == ':' || c == '=' || c == '@' || c == '~' || c == '$'; }
        static bool Ends(string w, string s) { return w.EndsWith(s, StringComparison.Ordinal); }
        /// <summary>A light English stemmer: plurals and -ing / -ed / -ly endings.</summary>
        public static string Stem(string w)
        {
            if (w.Length <= 3 || !char.IsLetter(w[w.Length - 1])) return w;
            if (Ends(w, "ies") && w.Length > 4) return w.Substring(0, w.Length - 3) + "y";
            if (Ends(w, "sses")) return w.Substring(0, w.Length - 2);
            if (Ends(w, "ches") || Ends(w, "shes") || Ends(w, "xes") || Ends(w, "zes")) return w.Substring(0, w.Length - 2);
            if (Ends(w, "s") && !Ends(w, "ss") && !Ends(w, "us") && !Ends(w, "is") && !Ends(w, "os")) w = w.Substring(0, w.Length - 1);
            if (Ends(w, "ing") && w.Length > 5) { w = w.Substring(0, w.Length - 3); if (w.Length > 3 && w[w.Length - 1] == w[w.Length - 2] && "lsz".IndexOf(w[w.Length - 1]) < 0) w = w.Substring(0, w.Length - 1); return w; }
            if (Ends(w, "ed") && w.Length > 4) { w = w.Substring(0, w.Length - 2); if (w.Length > 3 && w[w.Length - 1] == w[w.Length - 2] && "lsz".IndexOf(w[w.Length - 1]) < 0) w = w.Substring(0, w.Length - 1); return w; }
            if (Ends(w, "ly") && w.Length > 5) return w.Substring(0, w.Length - 2);
            return w;
        }

        // ------------------------------------------------------------ searching
        public class Query
        {
            public List<string> Terms = new List<string>();     // stemmed, for scoring
            public List<string> Phrases = new List<string>();   // lower case, must appear
            public List<string> Words = new List<string>();     // as typed, for highlighting
            public Dictionary<string, List<string>> Parts = new Dictionary<string, List<string>>();   // a name's parts, if the name is nowhere
        }

        public static Query Parse(string q)
        {
            var r = new Query();
            q = q ?? "";
            foreach (Match m in Regex.Matches(q, "\"([^\"]*)\"?"))
            {
                string p = Norm(m.Groups[1].Value).Trim();
                if (p.Length > 0) { r.Phrases.Add(p); r.Words.AddRange(p.Split(' ')); }
            }
            string rest = Regex.Replace(q, "\"([^\"]*)\"?", " ");
            foreach (Match m in WordRx.Matches(rest)) { string w = m.Value.Trim('.', ':', ',', '='); if (w.Length > 0) r.Words.Add(w.ToLowerInvariant()); }
            // a name such as rd.break counts whole (its parts alone would find every "break")
            foreach (var w in r.Words)
            {
                var ts = Terms(w);
                if (ts.Count == 0) continue;
                var pick = ts.Count > 1 && ts[0].IndexOfAny(".-/:=@~+$".ToCharArray()) >= 0 ? new List<string> { ts[0] } : ts;
                foreach (var t in pick) if (!r.Terms.Contains(t)) r.Terms.Add(t);
                if (pick.Count == 1 && ts.Count > 1) r.Parts[ts[0]] = ts.Skip(1).Where(x => x != ts[0]).ToList();
            }
            return r;
        }

        /// <summary>The best matches, best first. allow: which documents may be shown (exam reviews only after
        /// the exam was taken).</summary>
        public List<SearchHit> Find(string query, Func<SearchDoc, bool> allow, int max)
        {
            var q = Parse(query);
            var hits = new List<SearchHit>();
            if (q.Terms.Count == 0) return hits;
            // a name nobody uses (rd.brake): look for its parts instead
            foreach (var kv in q.Parts)
                if (!postings.ContainsKey(kv.Key)) { q.Terms.Remove(kv.Key); foreach (var p in kv.Value) if (!q.Terms.Contains(p)) q.Terms.Add(p); }
            if (q.Terms.Count == 0) return hits;
            const double k1 = 1.2, b = 0.75;
            int n = Docs.Count;
            var scores = new Dictionary<SearchDoc, double>();
            var matched = new Dictionary<SearchDoc, int>();
            foreach (var t in q.Terms)
            {
                List<SearchDoc> l;
                if (!postings.TryGetValue(t, out l)) continue;
                double idf = Math.Log(1 + (n - l.Count + 0.5) / (l.Count + 0.5));
                foreach (var d in l)
                {
                    double tf = d.Tf[t];
                    double s = idf * tf * (k1 + 1) / (tf + k1 * (1 - b + b * d.Len / avgLen));
                    double old; scores.TryGetValue(d, out old); scores[d] = old + s;
                    int m; matched.TryGetValue(d, out m); matched[d] = m + 1;
                }
            }
            foreach (var kv in scores)
            {
                var d = kv.Key;
                if (allow != null && !allow(d)) continue;
                if (q.Phrases.Count > 0 && !q.Phrases.All(p => d.Raw.Contains(p))) continue;
                double cover = matched[d] / (double)q.Terms.Count;
                double s = kv.Value * cover * cover;   // all the words count far more than some of them
                hits.Add(new SearchHit { Doc = d, Score = s });
            }
            hits = hits.OrderByDescending(h => h.Score).ThenBy(h => h.Doc.Ord).Take(max).ToList();
            foreach (var h in hits) MakeSnippet(h, q);
            return hits;
        }

        /// <summary>About 200 characters of the text around the first match, with the matches marked.</summary>
        static void MakeSnippet(SearchHit h, Query q)
        {
            string text = Regex.Replace(h.Doc.Text.Replace("`", "").Replace("**", ""), "\\s+", " ").Trim();
            var rx = HighlightRegex(q);
            int first = -1;
            if (rx != null) { var m = rx.Match(text); if (m.Success) first = m.Index; }
            int start = 0;
            if (first > 60) { start = first - 60; int sp = text.IndexOf(' ', start); if (sp > 0 && sp < first) start = sp + 1; }
            int len = Math.Min(text.Length - start, 220);
            string snip = text.Substring(start, len);
            if (start + len < text.Length) { int sp = snip.LastIndexOf(' '); if (sp > 150) snip = snip.Substring(0, sp); snip += " ..."; }
            if (start > 0) snip = "... " + snip;
            h.Snippet = snip;
            if (rx != null) foreach (Match m in rx.Matches(snip)) if (m.Length > 0) h.Marks.Add(new[] { m.Index, m.Length });
        }

        /// <summary>Matches the query's words in a text: phrases as written, words with any ending.</summary>
        public static Regex HighlightRegex(Query q)
        {
            var phrases = new List<string>();
            foreach (var p in q.Phrases) phrases.Add(string.Join("\\s+", p.Split(' ').Select(Regex.Escape).ToArray()));
            var alts = new List<string>();
            foreach (var w in q.Words.Distinct())
            {
                if (w.Length == 0) continue;
                bool compound = w.IndexOfAny(".-/:=@~+$".ToCharArray()) >= 0;
                string stem = compound ? w : Stem(w);
                alts.Add("(?<![\\w])" + Regex.Escape(stem) + (compound ? "" : "\\w*"));
            }
            if (alts.Count + phrases.Count == 0) return null;
            // phrases first (marked whole), then the longest words
            return new Regex(string.Join("|", phrases.OrderByDescending(a => a.Length).Concat(alts.OrderByDescending(a => a.Length)).ToArray()), RegexOptions.IgnoreCase);
        }
        public static Regex HighlightRegex(string query) { return HighlightRegex(Parse(query)); }
    }
}
