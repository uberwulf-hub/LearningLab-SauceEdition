// Learning Lab - Languages: a course is a JSON file (Content\Languages\<language>.json, format in Content\Languages\
// FORMAT.md): units of short phrases with their English. An item's id is the first 6 bytes of MD5("es|en") as hex, so
// progress survives reordering and fixing a typo only in a note.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LearningLab.Languages
{
    public class LangItem
    {
        [JsonPropertyName("es")] public string Target { get; set; }
        [JsonPropertyName("en")] public string English { get; set; }
        [JsonPropertyName("note")] public string Note { get; set; }
        [JsonPropertyName("lit")] public string Literal { get; set; }
        [JsonPropertyName("kind")] public string Kind { get; set; }
        [JsonIgnore] public string Id { get; set; }
        [JsonIgnore] public LangUnit Unit { get; set; }
        public bool IsWord => Kind == "word";
    }

    public class LangUnit
    {
        [JsonPropertyName("id")] public string Id { get; set; }
        [JsonPropertyName("level")] public int Level { get; set; }
        [JsonPropertyName("title")] public string Title { get; set; }
        [JsonPropertyName("intro")] public string Intro { get; set; }
        [JsonPropertyName("items")] public List<LangItem> Items { get; set; } = new List<LangItem>();
        [JsonPropertyName("tips")] public List<string> Tips { get; set; } = new List<string>();
        [JsonIgnore] public int Number { get; set; }
    }

    public class LangCourse
    {
        [JsonPropertyName("format")] public int Format { get; set; }
        [JsonPropertyName("language")] public string Language { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; }
        [JsonPropertyName("variant")] public string Variant { get; set; }
        [JsonPropertyName("voice")] public string Voice { get; set; }
        [JsonPropertyName("units")] public List<LangUnit> Units { get; set; } = new List<LangUnit>();

        public static readonly string[] LevelNames = { "", "Starter", "Beginner", "Getting around", "Building sentences" };
        public static string LevelName(int level) { return level >= 1 && level < LevelNames.Length ? LevelNames[level] : "Level " + level; }

        [JsonIgnore] public IEnumerable<LangItem> AllItems => Units.SelectMany(u => u.Items);
        [JsonIgnore] public Dictionary<string, LangItem> ById { get; private set; } = new Dictionary<string, LangItem>();

        public LangUnit Unit(string id) { return Units.FirstOrDefault(u => u.Id == id); }
        public LangItem Item(string id) { LangItem i; return id != null && ById.TryGetValue(id, out i) ? i : null; }

        /// <summary>Reads a course; throws with a plain message when the file is wrong.</summary>
        public static LangCourse Parse(string json)
        {
            var c = JsonSerializer.Deserialize<LangCourse>(json, new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (c == null || c.Units == null || c.Units.Count == 0) throw new FormatException("The course has no units.");
            int n = 0;
            foreach (var u in c.Units)
            {
                u.Number = ++n;
                if (u.Items == null) u.Items = new List<LangItem>();
                if (u.Tips == null) u.Tips = new List<string>();
                foreach (var i in u.Items)
                {
                    i.Unit = u;
                    i.Id = ItemId(i.Target, i.English);
                    if (!c.ById.ContainsKey(i.Id)) c.ById[i.Id] = i;
                }
            }
            return c;
        }

        public static string ItemId(string target, string english)
        {
            using (var md5 = MD5.Create())
            {
                var h = md5.ComputeHash(Encoding.UTF8.GetBytes((target ?? "") + "|" + (english ?? "")));
                var sb = new StringBuilder(12);
                for (int k = 0; k < 6; k++) sb.Append(h[k].ToString("x2"));
                return sb.ToString();
            }
        }

        /// <summary>Problems with the course for the checks (empty when it is fine).</summary>
        public List<string> Problems()
        {
            var p = new List<string>();
            var unitIds = new HashSet<string>();
            var seen = new Dictionary<string, string>(StringComparer.Ordinal);
            if (Format != 1) p.Add("format is not 1");
            if (string.IsNullOrWhiteSpace(Language) || string.IsNullOrWhiteSpace(Name)) p.Add("language or name missing");
            foreach (var u in Units)
            {
                if (string.IsNullOrWhiteSpace(u.Id) || !unitIds.Add(u.Id)) p.Add("unit id missing or repeated: " + u.Id);
                if (u.Level < 1 || u.Level >= LevelNames.Length) p.Add(u.Id + ": level " + u.Level);
                if (string.IsNullOrWhiteSpace(u.Title) || string.IsNullOrWhiteSpace(u.Intro)) p.Add(u.Id + ": title or intro missing");
                if (u.Items.Count < 6) p.Add(u.Id + ": only " + u.Items.Count + " items");
                foreach (var i in u.Items)
                {
                    if (string.IsNullOrWhiteSpace(i.Target) || string.IsNullOrWhiteSpace(i.English)) { p.Add(u.Id + ": an item without text"); continue; }
                    if (i.Target != i.Target.Trim() || i.English != i.English.Trim()) p.Add(u.Id + ": spaces around \"" + i.Target + "\"");
                    if (i.Kind != null && i.Kind != "word" && i.Kind != "phrase") p.Add(u.Id + ": kind " + i.Kind);
                    string where;
                    if (seen.TryGetValue(i.Target, out where)) p.Add("\"" + i.Target + "\" is in both " + where + " and " + u.Id);
                    else seen[i.Target] = u.Id;
                    if (i.Target.Length > 90) p.Add(u.Id + ": too long to say in the car: " + i.Target);
                }
            }
            return p;
        }
    }
}
