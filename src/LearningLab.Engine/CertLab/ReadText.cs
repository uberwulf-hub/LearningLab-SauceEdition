// Cert Lab - what reading aloud reads: the text of a page as units (a paragraph, a list item, a question, an answer
// choice, a cheat sheet row, a side of a flash card), each with the element that shows it, so the window can light it
// up. Pages that know better say so (IReadable: a lesson, a flash card, a cheat sheet); everything else is read from
// its text in order (ReadText.FromTree, with the screens): headings and paragraphs, answer choices, task text, search results, a man
// page; never buttons, lists of links, text boxes you type in, terminals, or a running exam's clock. Code blocks are
// skipped, or read as "Code: ..." (the setting). Pronounce makes tech words sayable from a list per course
// (Courses\<course>\speech.txt). SpeechSettings keeps the speed, voice and code setting in speech.json.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace CertLab
{
    public sealed class ReadUnit
    {
        public string Text = "";             // what is shown (sentences are split from this); may be a ReadAloud pause
        public object Element;           // what shows it (lit up while it is read), or null
        public string Kind = "p";            // p, h, code, choice, question, row, front, back, pause
    }

    public sealed class ReadDoc
    {
        public string Title = "";
        public readonly List<ReadUnit> Units = new List<ReadUnit>();
        public ReadUnit Add(string text, object el, string kind)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return null;
            var u = new ReadUnit { Text = text, Element = el, Kind = kind ?? "p" };
            Units.Add(u);
            return u;
        }
        public List<string> Texts { get { return Units.Select(u => u.Text).ToList(); } }
        /// <summary>The unit an element belongs to (the element itself, or one inside it, or the one it is inside).</summary>
        public int IndexOf(object el)
        {
            if (el == null) return -1;
            for (int i = 0; i < Units.Count; i++) if (Units[i].Element == el) return i;
            for (var p = Parent(el); p != null; p = Parent(p)) for (int i = 0; i < Units.Count; i++) if (Units[i].Element == p) return i;
            for (int i = 0; i < Units.Count; i++) if (Units[i].Element != null && IsInside(Units[i].Element, el)) return i;
            return -1;
        }
        /// <summary>The element an element sits in (the screens set it for their UI toolkit; null = no parents known).</summary>
        public static Func<object, object> ParentOf;
        static object Parent(object d)
        {
            if (ParentOf == null) return null;
            try { return ParentOf(d); } catch (Exception) { return null; }
        }
        static bool IsInside(object child, object ancestor)
        {
            for (var p = Parent(child); p != null; p = Parent(p)) if (p == ancestor) return true;
            return false;
        }
    }

    /// <summary>A page that says itself what reading aloud reads (otherwise its text is read in order).</summary>
    public interface IReadable { ReadDoc ReadContent(bool readCode); }


    /// <summary>Saying tech text aloud. Rules come from Courses\<course>\speech.txt (one a line):
    ///   written => said          e.g. /etc/fstab => slash etc slash f-s-tab (whole words; case matters)
    ///   spell: NMCLI nmcli       said letter by letter: N M C L I
    ///   word: NAT RAID           all capitals but said as a word (not spelt)
    /// Then, for everything else: paths (/etc/hosts -> slash etc slash hosts), PowerShell names (Get-ChildItem -> Get Child
    /// Item), and words in capitals of 2 to 6 letters spelt out (the acronyms; the Security+ acronym decks add theirs).</summary>
    public sealed class Pronounce
    {
        readonly List<KeyValuePair<string, string>> rules = new List<KeyValuePair<string, string>>();
        readonly HashSet<string> words = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> spells = new HashSet<string>(StringComparer.Ordinal);
        Regex table;
        readonly Dictionary<string, string> said = new Dictionary<string, string>(StringComparer.Ordinal);
        public int Count { get { return rules.Count + spells.Count; } }

        public static Pronounce Load(string file)
        {
            var p = new Pronounce();
            try { if (File.Exists(file)) p.AddText(File.ReadAllText(file)); } catch (Exception) { }
            return p;
        }

        public void AddText(string text)
        {
            foreach (var raw in (text ?? "").Replace("\r", "").Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                if (line.StartsWith("spell:", StringComparison.OrdinalIgnoreCase)) { foreach (var w in line.Substring(6).Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)) Spell(w); continue; }
                if (line.StartsWith("word:", StringComparison.OrdinalIgnoreCase)) { foreach (var w in line.Substring(5).Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)) words.Add(w); continue; }
                int a = line.IndexOf("=>", StringComparison.Ordinal);
                if (a <= 0) continue;
                Add(line.Substring(0, a).Trim(), line.Substring(a + 2).Trim());
            }
        }

        public void Add(string written, string spoken) { if (written.Length > 0) { rules.Add(new KeyValuePair<string, string>(written, spoken)); said[written] = spoken; table = null; } }
        public void Spell(string w) { if (w.Length > 0 && !said.ContainsKey(w)) { spells.Add(w); said[w] = Letters(w); table = null; } }

        /// <summary>The acronyms of a course's acronym decks (Security+): each card's front, spelt unless it is a "word:".</summary>
        public void AddAcronyms(IEnumerable<Card> cards)
        {
            foreach (var c in cards)
            {
                var front = Regex.Replace(c.Front ?? "", "\\s*\\(.*\\)\\s*$", "").Trim();
                if (front.Length < 2 || front.Length > 8 || said.ContainsKey(front) || words.Contains(front)) continue;
                if (!Regex.IsMatch(front, "^[A-Z0-9&/-]*[A-Z][A-Z0-9&/-]*$")) continue;
                if (Regex.IsMatch(front, "^[A-Z]+$")) Spell(front);
                else said[front] = Letters(front);
                table = null;
            }
        }

        public static string Letters(string w)
        {
            var sb = new StringBuilder();
            foreach (char c in w)
            {
                if (char.IsLetter(c)) { if (sb.Length > 0) sb.Append(' '); sb.Append(char.ToUpperInvariant(c)); }
                else if (char.IsDigit(c)) { if (sb.Length > 0) sb.Append(' '); sb.Append(c); }
                else if (c == '&') sb.Append(" and");
                else if (c == '-' || c == '/') sb.Append(' ');
            }
            return Regex.Replace(sb.ToString(), "\\s+", " ").Trim();
        }

        static readonly Regex PathRx = new Regex(@"(?<![\w.])(~?/[A-Za-z0-9._\-/<>*]+)", RegexOptions.CultureInvariant);
        static readonly Regex CmdletRx = new Regex(@"\b([A-Z][a-z]+)-([A-Z][A-Za-z0-9]+)\b", RegexOptions.CultureInvariant);
        static readonly Regex CapsRx = new Regex(@"(?<![\w-])([A-Z]{2,6})(s?)(?![\w-])", RegexOptions.CultureInvariant);
        static readonly HashSet<string> Said = new HashSet<string>(StringComparer.Ordinal) { "OK", "NOT", "AND", "OR", "THE", "ALL", "USB", "LAN", "WAN", "RAM", "RAID", "NAT", "GUI", "DOS", "PIN", "SIM", "LED", "ZIP", "JSON", "YAML", "CSV", "README", "TODO", "FAQ" };

        /// <summary>A sentence as it is said.</summary>
        public string Say(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            string s = text;
            // 1. the course's own list (longest first, whole words)
            if (said.Count > 0)
            {
                if (table == null)
                {
                    var keys = said.Keys.OrderByDescending(k => k.Length).Select(k => (char.IsLetterOrDigit(k[0]) ? "(?<![\\w-])" : "(?<![\\w])") + Regex.Escape(k) + (char.IsLetterOrDigit(k[k.Length - 1]) ? "(?![\\w])" : "(?![\\w/])"));
                    table = new Regex(string.Join("|", keys.ToArray()), RegexOptions.CultureInvariant);
                }
                s = table.Replace(s, m => " " + said[m.Value] + " ");
            }
            // 2. paths: /etc/sysconfig/network -> slash etc slash sysconfig slash network
            s = PathRx.Replace(s, m => { string p = m.Groups[1].Value, end = ""; while (p.Length > 1 && (p.EndsWith(".", StringComparison.Ordinal) || p.EndsWith(",", StringComparison.Ordinal))) { end = p.Substring(p.Length - 1) + end; p = p.Substring(0, p.Length - 1); } return " " + SayPath(p) + end + " "; });
            // 3. PowerShell Verb-Noun: Get-ChildItem -> Get Child Item
            s = CmdletRx.Replace(s, m => m.Groups[1].Value + " " + SplitCamel(m.Groups[2].Value));
            // 4. other words in capitals: spelt (DNS -> D N S), unless they are said as words
            s = CapsRx.Replace(s, m => words.Contains(m.Groups[1].Value) || Said.Contains(m.Groups[1].Value) ? m.Value : Letters(m.Groups[1].Value) + (m.Groups[2].Value.Length > 0 ? "s" : ""));
            s = Regex.Replace(s, "\\s{2,}", " ");
            return Regex.Replace(s, " +([.,;:!?])(?=\\s|$)", "$1").Trim();
        }

        static string SayPath(string p)
        {
            var sb = new StringBuilder();
            if (p.StartsWith("~", StringComparison.Ordinal)) { sb.Append("tilde "); p = p.Substring(1); }
            var parts = p.Split('/');
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0) sb.Append(" slash ");
                sb.Append(parts[i].Replace(".", " dot ").Replace("*", " star ").Replace("<", " ").Replace(">", " "));
            }
            return Regex.Replace(sb.ToString(), "\\s+", " ").Trim();
        }

        public static string SplitCamel(string w) { return Regex.Replace(w, "(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " "); }
    }

    /// <summary>The read-aloud settings: speed, voice, code blocks, the pause between a card's sides. Kept in speech.json
    /// in the data folder (not in the progress file).</summary>
    public sealed class SpeechSettings
    {
        public int Rate;                 // -5..5
        public string Voice = "";
        public bool ReadCode;            // code blocks: false = skipped, true = read as "Code: ..."
        public int CardPauseMs = 2500;   // between the front and the back of a flash card
        [NonSerialized] public string File;
        [NonSerialized] public bool ReadOnly;

        public static SpeechSettings Load(string file, bool readOnly)
        {
            SpeechSettings s = null;
            try { if (file != null && System.IO.File.Exists(file)) s = Ser.FromJson<SpeechSettings>(Json.Parse(System.IO.File.ReadAllText(file))); } catch (Exception) { s = null; }
            if (s == null) s = new SpeechSettings();
            s.Rate = Math.Max(-5, Math.Min(5, s.Rate));
            s.CardPauseMs = Math.Max(500, Math.Min(10000, s.CardPauseMs));
            if (s.Voice == null) s.Voice = "";
            s.File = file; s.ReadOnly = readOnly;
            return s;
        }

        public void Save()
        {
            if (ReadOnly || File == null) return;
            try
            {
                var dir = Path.GetDirectoryName(File);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string tmp = File + ".tmp";
                System.IO.File.WriteAllText(tmp, Json.Write(Ser.ToJson(this)), new UTF8Encoding(false));
                if (System.IO.File.Exists(File)) System.IO.File.Replace(tmp, File, null);
                else System.IO.File.Move(tmp, File);
            }
            catch (Exception) { }
        }
    }
}
