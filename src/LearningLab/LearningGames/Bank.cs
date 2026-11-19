// Learning Games - the question banks for History, English, US Citizenship, Spanish, Coding and Science
// (Content\LearningGames\<subject>.json, format in FORMAT.md beside them), their seven levels, and choosing the
// questions for a round: what is due for review first, then new ones, then the rest.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Avalonia.Platform;

namespace LearningLab.LearningGames;

public sealed class BankFile
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("levels")] public Dictionary<string, List<Topic>> Levels { get; set; } = new();
}

public sealed class Topic
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("questions")] public List<Question> Questions { get; set; } = new();
}

public sealed class Question
{
    [JsonPropertyName("q")] public string Q { get; set; } = "";
    [JsonPropertyName("choices")] public List<string> Choices { get; set; } = new();
    [JsonPropertyName("answer")] public int Answer { get; set; }
    [JsonPropertyName("explain")] public string Explain { get; set; } = "";
    [JsonPropertyName("tip")] public string Tip { get; set; } = "";
    [JsonPropertyName("code")] public string Code { get; set; } = "";
    [JsonPropertyName("say")] public string Say { get; set; } = "";
    [JsonPropertyName("passage")] public string Passage { get; set; } = "";
    [JsonPropertyName("passageTitle")] public string PassageTitle { get; set; } = "";
    [JsonPropertyName("lang")] public string Lang { get; set; } = "";

    public string Right => Answer >= 0 && Answer < Choices.Count ? Choices[Answer] : "";

    string id;
    /// <summary>The question's identity for spaced review: the same short hash App Hub's Learning Games used (text, code,
    /// right answer, and the story's title for reading questions), so review boxes carry across.</summary>
    public string Id
    {
        get
        {
            if (id != null) return id;
            string key = Q + "|" + Code + "|" + Right;
            if (!string.IsNullOrEmpty(Passage)) key += "|" + PassageTitle;
            byte[] h = MD5.HashData(Encoding.UTF8.GetBytes(key));
            var sb = new StringBuilder();
            for (int i = 0; i < 6; i++) sb.Append(h[i].ToString("x2"));
            return id = sb.ToString();
        }
    }
}

/// <summary>A question in a round, with the topic it came from.</summary>
public sealed record RoundItem(Question Q, string Topic);

public static class Bank
{
    public static readonly string[] LevelKeys = { "prek", "k2", "g35", "g68", "g912", "college", "adult" };
    public static readonly Dictionary<string, string> LevelNames = new()
    {
        ["prek"] = "Pre-K", ["k2"] = "Kindergarten - 2nd grade", ["g35"] = "3rd - 5th grade", ["g68"] = "6th - 8th grade",
        ["g912"] = "High school", ["college"] = "College", ["adult"] = "Graduate / grown-up refresher",
    };

    public record SubjectInfo(string Id, string Name, string Glyph, string Color, string Text);
    public static readonly SubjectInfo[] Subjects =
    {
        new("math", "Math", "➕", "#3B82F6", "Games with numbers: blasters, races, pizza and more"),
        new("history", "History", "⏳", "#F59E0B", "People, places and what happened when"),
        new("english", "English", "✎", "#22C55E", "Letters, words, grammar and great books"),
        new("civics", "US Citizenship", "⚖", "#EF4444", "The Constitution, government and the citizenship test"),
        new("spanish", "Spanish", "¡", "#EC4899", "Words, phrases and grammar en español"),
        new("coding", "Coding", "</>", "#A855F7", "Think like a programmer, then write real code"),
        new("science", "Science", "⚛", "#14B8A6", "Animals, plants, weather, matter and space"),
    };

    static readonly Dictionary<string, BankFile> cache = new();

    public static BankFile Get(string id)
    {
        lock (cache)
        {
            if (cache.TryGetValue(id, out var b)) return b;
            b = null;
            try
            {
                var uri = new Uri("avares://LearningLab/Content/LearningGames/" + id + ".json");
                if (AssetLoader.Exists(uri))
                {
                    using var s = AssetLoader.Open(uri);
                    b = JsonSerializer.Deserialize<BankFile>(s);
                }
            }
            catch (Exception) { b = null; }
            cache[id] = b;
            return b;
        }
    }

    public static List<Topic> Topics(string id, string level)
    {
        var b = Get(id);
        if (b == null || !b.Levels.TryGetValue(level, out var t) || t == null) return new();
        return t;
    }

    /// <summary>The levels a bank has questions for (all seven when it has none at all).</summary>
    public static string[] LevelsOf(string id)
    {
        var b = Get(id);
        var have = new List<string>();
        if (b != null) foreach (var k in LevelKeys) if (b.Levels.TryGetValue(k, out var t) && t != null && t.Count > 0) have.Add(k);
        return have.Count > 0 ? have.ToArray() : LevelKeys;
    }

    /// <summary>The nearest level a bank has (the lower one on a tie).</summary>
    public static string Nearest(string level, string[] have)
    {
        if (have.Contains(level)) return level;
        int i = Array.IndexOf(LevelKeys, level), dist = 99;
        string best = have[0];
        foreach (var k in have) { int d = Math.Abs(Array.IndexOf(LevelKeys, k) - i); if (d < dist) { dist = d; best = k; } }
        return best;
    }

    /// <summary>The questions for a round: due first (up to half), then new, then the rest of the due, then the rest.</summary>
    public static List<RoundItem> Round(Player p, string id, string level, int topic, int count, Random rand)
    {
        var pool = new List<RoundItem>();
        var topics = Topics(id, level);
        for (int t = 0; t < topics.Count; t++)
        {
            if (topic >= 0 && t != topic) continue;
            foreach (var q in topics[t].Questions) if (q != null && !string.IsNullOrEmpty(q.Q) && q.Choices.Count >= 2) pool.Add(new RoundItem(q, topics[t].Name));
        }
        var s = p.Subject(id);
        string today = Profiles.Today;
        var due = new List<RoundItem>(); var fresh = new List<RoundItem>(); var rest = new List<RoundItem>();
        foreach (var x in pool.OrderBy(_ => rand.Next()))
        {
            var (box, d) = s.Card(x.Q.Id);
            if (box < 0) fresh.Add(x);
            else if (string.CompareOrdinal(d, today) <= 0) due.Add(x);
            else rest.Add(x);
        }
        int half = (int)Math.Ceiling(count * 0.5);
        return due.Take(half).Concat(fresh).Concat(due.Skip(half)).Concat(rest).Take(count).ToList();
    }

    /// <summary>One subject answer: the review box, the subject's results, a star, today's count; may move the level
    /// (up after 10 of the last 12 right, down after only 4 of the last 10). Returns "up", "down" or "".</summary>
    public static string Record(Player p, string id, RoundItem item, bool correct)
    {
        var s = p.Subject(id);
        s.RecordCard(item.Q.Id, correct);
        s.Answered++;
        if (correct) { s.Correct++; p.Stars++; }
        var r = s.Recent; r.Add(correct ? 1 : 0); if (r.Count > 12) r.RemoveRange(0, r.Count - 12); s.Recent = r;
        p.CountDay(correct);
        if (!correct) p.AddMistake(id, item.Q.Q, item.Q.Right, "");
        Profiles.Answered();
        double avg = r.Count > 0 ? r.Average() : 0;
        var have = LevelsOf(id);
        int i = Array.IndexOf(have, s.Level);
        if (r.Count >= 12 && avg >= 0.83 && i < have.Length - 1) { s.Level = have[i + 1]; s.Recent = new(); return "up"; }
        if (r.Count >= 10 && avg <= 0.4 && i > 0) { s.Level = have[i - 1]; s.Recent = new(); return "down"; }
        return "";
    }
}
