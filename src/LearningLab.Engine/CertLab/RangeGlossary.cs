// Cyber Range - the glossary: Range\glossary.txt (format at its top) loaded into terms with their spellings, the
// matcher that finds them in a challenge's text, and the text builder that turns them into dotted, hoverable words
// on Beginner pages. Clicking one shows a small card inside the window (RangeView.ShowTerm), not a separate popup
// window, so it also shows in -Snapshot pictures.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CertLab
{
    public class GlossTerm
    {
        public string Term = "", Text = "";
        public List<string> Names = new List<string>();
    }

    /// <summary>Where a glossary word was found in a text.</summary>
    public class GlossHit
    {
        public int Start, Length;
        public GlossTerm Term;
    }

    public static partial class RangeGlossary
    {
        public static readonly List<GlossTerm> Terms = new List<GlossTerm>();
        public static readonly List<string> Problems = new List<string>();
        static List<KeyValuePair<string, GlossTerm>> names = new List<KeyValuePair<string, GlossTerm>>();

        /// <summary>Reads the glossary. A missing or broken file leaves it empty (the pages work without it).</summary>
        public static void Load(string file)
        {
            Terms.Clear(); Problems.Clear();
            if (!File.Exists(file)) { Problems.Add("glossary.txt is missing"); Index(); return; }
            Parse(File.ReadAllText(file));
            Index();
        }
        public static void Parse(string text)
        {
            GlossTerm cur = null;
            var body = new StringBuilder();
            Action flush = () =>
            {
                if (cur == null) return;
                cur.Text = body.ToString().Trim();
                if (cur.Term.Length == 0 || cur.Text.Length == 0) Problems.Add("an entry without a term or an explanation near \"" + cur.Term + "\"");
                else Terms.Add(cur);
                body.Clear(); cur = null;
            };
            foreach (var raw in text.Replace("\r", "").Split('\n'))
            {
                string l = raw.Trim();
                if (l.StartsWith("#", StringComparison.Ordinal)) continue;
                if (l.StartsWith("==", StringComparison.Ordinal))
                {
                    flush();
                    var parts = l.Substring(2).Split('|');
                    cur = new GlossTerm { Term = parts[0].Trim() };
                    cur.Names.Add(cur.Term);
                    if (parts.Length > 1) foreach (var n in parts[1].Split(',')) { var t = n.Trim(); if (t.Length > 0 && !cur.Names.Contains(t)) cur.Names.Add(t); }
                    continue;
                }
                if (cur == null) { if (l.Length > 0) Problems.Add("text before the first entry: " + l); continue; }
                if (l.Length > 0) body.Append(body.Length > 0 ? " " : "").Append(l);
            }
            flush();
        }
        static void Index()
        {
            names = new List<KeyValuePair<string, GlossTerm>>();
            foreach (var t in Terms) foreach (var n in t.Names) names.Add(new KeyValuePair<string, GlossTerm>(n, t));
            names = names.OrderByDescending(k => k.Key.Length).ToList();
        }

        public static GlossTerm Find(string word)
        {
            string w = (word ?? "").Trim();
            foreach (var k in names) if (string.Equals(k.Key, w, StringComparison.OrdinalIgnoreCase)) return k.Value;
            return null;
        }

        static bool WordChar(char c) { return char.IsLetterOrDigit(c) || c == '_'; }

        /// <summary>The first place each term appears in the text (whole words, any case; longer spellings first;
        /// no overlaps), in text order.</summary>
        public static List<GlossHit> Matches(string text)
        {
            var hits = new List<GlossHit>();
            if (string.IsNullOrEmpty(text)) return hits;
            var taken = new HashSet<GlossTerm>();
            foreach (var k in names)
            {
                if (taken.Contains(k.Value)) continue;
                int from = 0;
                while (from < text.Length)
                {
                    int i = text.IndexOf(k.Key, from, StringComparison.OrdinalIgnoreCase);
                    if (i < 0) break;
                    int e = i + k.Key.Length;
                    bool edges = (i == 0 || !WordChar(text[i - 1]) || !WordChar(k.Key[0])) && (e >= text.Length || !WordChar(text[e]) || !WordChar(k.Key[k.Key.Length - 1]));
                    // not inside addresses, paths or file names (http://..., /var/log, access.log)
                    if (edges && ((i > 0 && "/.-_~".IndexOf(text[i - 1]) >= 0) || (e < text.Length && "/-_".IndexOf(text[e]) >= 0) || (e + 1 < text.Length && text[e] == '.' && char.IsLetterOrDigit(text[e + 1])) || (e < text.Length && text[e] == ':' && e + 1 < text.Length && text[e + 1] == '/'))) edges = false;
                    bool free = edges && hits.All(h => e <= h.Start || i >= h.Start + h.Length);
                    if (free) { hits.Add(new GlossHit { Start = i, Length = k.Key.Length, Term = k.Value }); taken.Add(k.Value); break; }
                    from = i + 1;
                }
            }
            return hits.OrderBy(h => h.Start).ToList();
        }

        /// <summary>The terms that appear in any of the texts, in the order they first appear.</summary>
        public static List<GlossTerm> TermsIn(params string[] texts)
        {
            var l = new List<GlossTerm>();
            foreach (var t in texts) foreach (var h in Matches(t)) if (!l.Contains(h.Term)) l.Add(h.Term);
            return l;
        }
    }
}
