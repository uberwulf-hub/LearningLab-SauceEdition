// Learning Lab - Spelling Bee: words and word lists, and reading lists from files. A word is written as in Andrew's
// spelling-bee.html: "a|b" = both spellings are right (the first is the main one), "_" = a space ("a_cappella").
// Lists come from three places: Learning Lab's own bundled lists (Content\SpellingBee\*.js, the same `const LISTS = [...]`
// format as the web page), lists imported from a file (.html like the web page, or .txt / .csv), and pasted words.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LearningLab.SpellingBee
{
    public sealed class BeeWord
    {
        /// <summary>The main spelling (spaces, not underscores).</summary>
        public string Word { get; }
        /// <summary>Every accepted spelling, the main one first.</summary>
        public IReadOnlyList<string> Alts { get; }
        /// <summary>The list it came from ("tricky" for My Tricky Words).</summary>
        public string ListId { get; set; } = "";

        public BeeWord(IEnumerable<string> alts)
        {
            var a = alts.Select(x => (x ?? "").Trim()).Where(x => x.Length > 0).ToList();
            if (a.Count == 0) throw new ArgumentException("a word needs a spelling");
            Alts = a;
            Word = a[0];
        }

        /// <summary>Reads "a|b" with "_" for spaces; null when there is no spelling in it.</summary>
        public static BeeWord Parse(string token, string listId = "")
        {
            if (token == null) return null;
            var alts = token.Replace('_', ' ').Split('|').Select(x => Regex.Replace(x.Trim(), @"\s+", " ")).Where(x => x.Length > 0 && x.Length <= 60 && x.Any(char.IsLetter)).ToList();
            if (alts.Count == 0) return null;
            return new BeeWord(alts) { ListId = listId };
        }

        /// <summary>Back to "a|b" (spaces kept), for saving.</summary>
        public string Token => string.Join("|", Alts);

        public override string ToString() => Word;
    }

    public sealed class BeeList
    {
        public string Id = "", Name = "", Emoji = "", Note = "";
        public List<BeeWord> Words = new List<BeeWord>();
        /// <summary>True for the lists that come with the app (they can't be renamed or deleted).</summary>
        public bool BuiltIn;
        /// <summary>The saved file's fields, unknown ones included (written back as they were).</summary>
        public System.Text.Json.Nodes.JsonObject Json;
        /// <summary>The file it was read from (null: not saved yet).</summary>
        public string SavedFile;
    }

    public static class BeeImport
    {
        public const int MaxWords = 3000;

        /// <summary>The lists in a page like spelling-bee.html (its `const LISTS = [ {id, name, emoji, note, words} ]`).
        /// Empty when there is no such block.</summary>
        public static List<BeeList> ParseHtml(string text)
        {
            var result = new List<BeeList>();
            if (string.IsNullOrEmpty(text)) return result;
            var m = Regex.Match(text, @"(?:const|let|var)\s+LISTS\s*=\s*\[");
            if (!m.Success) return result;
            int start = m.Index + m.Length;
            // the array ends at the first "]" outside a string at depth 0
            int i = start, depth = 0; char quote = '\0';
            for (; i < text.Length; i++)
            {
                char c = text[i];
                if (quote != '\0') { if (c == '\\' && quote != '`') { i++; continue; } if (c == quote) quote = '\0'; continue; }
                if (c == '"' || c == '\'' || c == '`') { quote = c; continue; }
                if (c == '{' || c == '[') depth++;
                else if (c == '}' || (c == ']' && depth > 0)) depth--;
                else if (c == ']') break;
            }
            string body = text.Substring(start, i - start);
            var prop = new Regex(@"(\w+)\s*:\s*(""(?:[^""\\]|\\.)*""|'(?:[^'\\]|\\.)*'|`[^`]*`)", RegexOptions.Singleline);
            foreach (Match e in Regex.Matches(body, @"\{(?:[^{}""'`]|""(?:[^""\\]|\\.)*""|'(?:[^'\\]|\\.)*'|`[^`]*`)*\}", RegexOptions.Singleline))
            {
                var l = new BeeList();
                string words = null;
                foreach (Match p in prop.Matches(e.Value))
                {
                    string v = p.Groups[2].Value;
                    v = v.Substring(1, v.Length - 2);
                    if (p.Groups[2].Value[0] != '`') v = Regex.Unescape(v);
                    switch (p.Groups[1].Value)
                    {
                        case "id": l.Id = v.Trim(); break;
                        case "name": l.Name = v.Trim(); break;
                        case "emoji": l.Emoji = v.Trim(); break;
                        case "note": l.Note = v.Trim(); break;
                        case "words": words = v; break;
                    }
                }
                if (words == null) continue;
                l.Words = Distinct(Regex.Split(words.Trim(), @"\s+").Select(t => BeeWord.Parse(t, l.Id)));
                if (l.Words.Count == 0) continue;
                if (l.Name.Length == 0) l.Name = "Word list " + (result.Count + 1);
                if (l.Id.Length == 0) l.Id = "list" + (result.Count + 1);
                foreach (var w in l.Words) w.ListId = l.Id;
                result.Add(l);
            }
            return result;
        }

        /// <summary>Words typed or pasted, or a .txt / .csv file: one word per line, or several split by spaces (or by
        /// commas, semicolons and tabs, which keep two-word entries together). A line "# Name" starts a new list.</summary>
        public static List<BeeList> ParseText(string text, string defaultName)
        {
            var result = new List<BeeList>();
            var cur = new BeeList { Name = string.IsNullOrWhiteSpace(defaultName) ? "My words" : defaultName.Trim() };
            var tokens = new List<string>();
            void Close()
            {
                cur.Words = Distinct(tokens.Select(t => BeeWord.Parse(t)));
                if (cur.Words.Count > 0) result.Add(cur);
                tokens.Clear();
            }
            foreach (string raw in (text ?? "").Replace("\r", "").Split('\n'))
            {
                string line = raw.Trim().TrimStart('﻿');
                if (line.Length == 0) continue;
                if (line.StartsWith("#"))
                {
                    string name = line.TrimStart('#').Trim();
                    if (name.Length == 0) continue;
                    if (tokens.Count > 0) Close();
                    else if (result.Count == 0 && cur.Words.Count == 0) { cur.Name = name; continue; }
                    cur = new BeeList { Name = name };
                    continue;
                }
                IEnumerable<string> parts = Regex.IsMatch(line, "[,;\t]") ? Regex.Split(line, "[,;\t]") : Regex.Split(line, @"\s+");
                foreach (string p in parts)
                {
                    string t = p.Trim().Trim('"', '“', '”').Trim();
                    if (t.Length > 0) tokens.Add(t);
                }
            }
            Close();
            return result;
        }

        static List<BeeWord> Distinct(IEnumerable<BeeWord> words)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var list = new List<BeeWord>();
            foreach (var w in words)
                if (w != null && seen.Add(w.Word) && list.Count < MaxWords) list.Add(w);
            return list;
        }

        /// <summary>A fresh id for an imported list.</summary>
        public static string NewId() => "my-" + Guid.NewGuid().ToString("N").Substring(0, 10);
    }
}
