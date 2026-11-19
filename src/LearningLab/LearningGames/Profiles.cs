// Learning Games - the players and their progress: profiles.json, the same file App Hub's Learning Games wrote (a JSON
// array of players). Every field is kept in a JsonObject, so fields this version doesn't know (a later version's, or
// App Hub's) are written back untouched; missing ones are filled in where used. Every past format loads: AMath
// (2026-10-13), ALearn (2026-10-14) and Learning Games (2026-10-24); fixtures in tests\LearningLab.Tests\Fixtures.
//
// Saved with LearningLab.Saves.SafeFile: a temporary file swapped in, the five versions before it kept
// (profiles.json.bak1..bak5); a file that can't be read is copied aside and the newest readable backup is used. When
// nothing can be read, nothing is saved over it (State = Locked).
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LearningLab.Saves;

namespace LearningLab.LearningGames;

public enum ProfilesState { Ok, Backup, Locked }

public static class Profiles
{
    public static readonly string[] Colors = { "#3B82F6", "#22C55E", "#F97316", "#EC4899", "#A855F7", "#06B6D4", "#EAB308", "#EF4444" };
    public const int DailyGoal = 20;

    public static List<Player> All { get; private set; } = new();
    public static ProfilesState State { get; private set; } = ProfilesState.Ok;
    /// <summary>What went wrong reading the file, in words for the person (empty when it read fine).</summary>
    public static string Message { get; private set; } = "";
    public static string LastSaveError { get; private set; } = "";
    static int unsaved;
    static string file;

    public static string File => file ??= Path.Combine(Data.For("LearningGames"), "profiles.json");

    /// <summary>For the checks: read players from another file.</summary>
    public static void UseFile(string path) { file = path; All = new(); State = ProfilesState.Ok; Message = ""; }

    /// <summary>Reads profiles.json (on Windows the first time, App Hub's copy is brought across).</summary>
    public static void Load()
    {
        BringAcrossFromAppHub();
        All = new();
        State = ProfilesState.Ok;
        Message = "";
        if (!System.IO.File.Exists(File) && SafeFile.Backups(File).Count == 0) return;
        try
        {
            var list = SafeFile.Load(File, bytes => Parse(Encoding.UTF8.GetString(bytes)), out SaveLoad info);
            if (info != null && info.UsedBackup) State = ProfilesState.Backup;
            Message = info?.Message ?? "";
            All = list ?? new();
        }
        catch (Exception e)
        {
            State = ProfilesState.Locked;
            Message = e.Message;
        }
        foreach (var p in All) p.Upgrade();
    }

    /// <summary>Parses the text of profiles.json; throws when it isn't a list of players.</summary>
    public static List<Player> Parse(string text)
    {
        if (text.Length > 0 && text[0] == '﻿') text = text.Substring(1);
        if (string.IsNullOrWhiteSpace(text)) throw new InvalidDataException("The file is empty");
        var node = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true });
        var arr = node as JsonArray;
        if (arr == null && node is JsonObject single) arr = new JsonArray(single.DeepClone());   // one player, unrolled by PowerShell
        if (arr == null) throw new InvalidDataException("It isn't a list of players");
        var list = new List<Player>();
        int nonNull = 0;
        foreach (var n in arr)
        {
            if (n == null) continue;
            nonNull++;
            if (n is JsonObject o && !string.IsNullOrEmpty(Player.Str(o["Id"]))) list.Add(new Player((JsonObject)o.DeepClone()));
        }
        if (nonNull > 0 && list.Count == 0) throw new InvalidDataException("It has no players in it");
        return list;
    }

    public static string ToJson()
    {
        var arr = new JsonArray();
        foreach (var p in All) arr.Add(p.Json.DeepClone());
        return arr.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    /// <summary>Saves now. False (and LastSaveError) when it couldn't; never saves over a file that couldn't be read.</summary>
    public static bool Save()
    {
        if (State == ProfilesState.Locked) { LastSaveError = "Learning Games could not read the saved players, so it is not saving anything over that file."; return false; }
        try
        {
            SafeFile.WriteText(File, ToJson());
            State = ProfilesState.Ok;
            unsaved = 0;
            LastSaveError = "";
            return true;
        }
        catch (Exception e) { LastSaveError = "Learning Games could not save progress: " + e.Message; return false; }
    }

    /// <summary>Counts an answer; saves every few, so a crash or power cut loses at most a few.</summary>
    public static void Answered() { if (++unsaved >= 5) Save(); }

    public static Player Find(string id) => All.FirstOrDefault(p => p.Id == id);

    public static Player Add(string name, int grade, int age, string color)
    {
        var o = new JsonObject
        {
            ["Id"] = Guid.NewGuid().ToString("n").Substring(0, 8),
            ["Name"] = name,
            ["Grade"] = grade,
            ["Age"] = age,
            ["Color"] = color,
            ["Stars"] = 0,
            ["Skills"] = new JsonObject(),
            ["Days"] = new JsonObject(),
            ["Mistakes"] = new JsonArray(),
            ["Best"] = new JsonObject(),
            ["Created"] = Today,
        };
        var p = new Player(o);
        p.Upgrade();
        All.Add(p);
        return p;
    }

    public static void Remove(Player p) { All.Remove(p); }

    /// <summary>On Windows, the first start copies App Hub's Learning Games players (App Hub's file stays where it is).</summary>
    static void BringAcrossFromAppHub()
    {
        if (System.IO.File.Exists(File) || SafeFile.Backups(File).Count > 0) return;
        string d = Data.AppHubFolder("LearningGames");
        if (d == null) return;
        string old = Path.Combine(d, "profiles.json");
        if (!System.IO.File.Exists(old)) return;
        try
        {
            var bytes = System.IO.File.ReadAllBytes(old);
            Parse(Encoding.UTF8.GetString(bytes));   // only a readable file is brought across
            SafeFile.Write(File, bytes);
        }
        catch (Exception) { }
    }

    public static string Today => DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static int AgeLevelIndex(int age) => Array.IndexOf(Bank.LevelKeys, AgeLevel(age));
    public static string AgeLevel(int age)
    {
        if (age <= 0) return "adult";
        if (age <= 4) return "prek";
        if (age <= 7) return "k2";
        if (age <= 10) return "g35";
        if (age <= 13) return "g68";
        if (age <= 17) return "g912";
        if (age <= 22) return "college";
        return "adult";
    }

    public static string GradeName(int grade) => grade switch
    {
        1 => "1st grade", 2 => "2nd grade", 3 => "3rd grade", 0 => "parent", _ => grade + "th grade",
    };

    static readonly Dictionary<string, string> extraSkills = new() { ["line"] = "Number line", ["fractions2"] = "Halves, thirds and fourths" };
    public static string SkillName(string id)
    {
        var i = global::LearningGames.Problems.Info(id);
        if (i != null) return i.Name;
        return extraSkills.TryGetValue(id, out var n) ? n : id;
    }
}

/// <summary>One player: a view over their JsonObject in profiles.json.</summary>
public sealed class Player
{
    public JsonObject Json { get; }
    public Player(JsonObject o) { Json = o; }

    // ---- reading JSON written by PowerShell: numbers may be strings, arrays may be single values
    public static string Str(JsonNode n)
    {
        if (n == null) return "";
        if (n is JsonValue v)
        {
            if (v.TryGetValue(out string s)) return s;
            return v.ToJsonString().Trim('"');
        }
        return n.ToJsonString();
    }
    public static int Int(JsonNode n, int def = 0)
    {
        if (n is JsonValue v)
        {
            if (v.TryGetValue(out int i)) return i;
            if (v.TryGetValue(out long l)) return (int)Math.Clamp(l, int.MinValue, int.MaxValue);
            if (v.TryGetValue(out double d)) return (int)d;
            if (v.TryGetValue(out bool b)) return b ? 1 : 0;
            if (v.TryGetValue(out string s) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int p)) return p;
        }
        return def;
    }
    public static double Dbl(JsonNode n)
    {
        if (n is JsonValue v)
        {
            if (v.TryGetValue(out double d)) return d;
            if (v.TryGetValue(out string s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double p)) return p;
        }
        return 0;
    }
    /// <summary>A list of numbers (or one number that PowerShell unrolled).</summary>
    public static List<int> Ints(JsonNode n)
    {
        var l = new List<int>();
        if (n is JsonArray a) { foreach (var x in a) if (x != null) l.Add(Int(x)); }
        else if (n is JsonValue) l.Add(Int(n));
        return l;
    }
    public static JsonArray IntArray(IEnumerable<int> l) { var a = new JsonArray(); foreach (int i in l) a.Add(i); return a; }

    public static JsonObject Obj(JsonObject parent, string key)
    {
        if (parent[key] is JsonObject o) return o;
        o = new JsonObject();
        parent[key] = o;
        return o;
    }
    public static JsonArray Arr(JsonObject parent, string key)
    {
        if (parent[key] is JsonArray a) return a;
        var old = parent[key];
        a = new JsonArray();
        if (old is JsonObject jo) a.Add(jo.DeepClone());   // one item, unrolled by PowerShell
        parent[key] = a;
        return a;
    }

    public string Id => Str(Json["Id"]);
    public string Name { get => Str(Json["Name"]); set => Json["Name"] = value; }
    /// <summary>2 or 6 for math (older players 1-12 too), 0 for a grown-up.</summary>
    public int Grade { get => Int(Json["Grade"]); set => Json["Grade"] = value; }
    public int Age { get => Int(Json["Age"]); set => Json["Age"] = value; }
    public string Color
    {
        get { string c = Str(Json["Color"]); return System.Text.RegularExpressions.Regex.IsMatch(c, "^#[0-9A-Fa-f]{6}$") ? c : "#3B82F6"; }
        set => Json["Color"] = value;
    }
    public int Stars { get => Int(Json["Stars"]); set => Json["Stars"] = value; }
    public bool GrownUp => Grade <= 0;
    /// <summary>The grade math problems are made for (a grown-up plays 6th-grade math).</summary>
    public int MathGrade => Grade <= 0 ? 6 : Grade;

    /// <summary>Fills in what older versions didn't save (an age for players from before ages, reward points).</summary>
    public void Upgrade()
    {
        if (Age <= 0) Age = Grade switch { 0 => 35, 2 => 7, 6 => 11, _ => Grade + 6 };
        if (Grade > 0) RewardPoints.ToString();
    }

    // ---- math skills
    public SkillStat Skill(string id)
    {
        var skills = Obj(Json, "Skills");
        if (skills[id] is not JsonObject o)
        {
            o = new JsonObject { ["Level"] = 1, ["Attempts"] = 0, ["Correct"] = 0, ["Streak"] = 0, ["Misses"] = 0, ["Recent"] = new JsonArray() };
            skills[id] = o;
        }
        return new SkillStat(o);
    }
    public SkillStat SkillIfAny(string id) => Json["Skills"] is JsonObject s && s[id] is JsonObject o ? new SkillStat(o) : null;

    // ---- the mastery score per skill (2026-11-11): "Mastery": { "<skill>": { "Score", "Answered", "Right", "Seconds",
    // "Last", "MasteredOn" } }. Players from before have no "Mastery" (every score 0); fields this version doesn't know
    // stay in the object and are written back.
    public MasteryStat Mastery(string id)
    {
        var all = Obj(Json, "Mastery");
        if (all[id] is not JsonObject o) { o = new JsonObject { ["Score"] = 0, ["Answered"] = 0, ["Right"] = 0, ["Seconds"] = 0 }; all[id] = o; }
        return new MasteryStat(o);
    }
    public MasteryStat MasteryIfAny(string id) => Json["Mastery"] is JsonObject m && m[id] is JsonObject o ? new MasteryStat(o) : null;
    public int MasteryScore(string id) => MasteryIfAny(id)?.Score ?? 0;
    public int MasteryAnswered(string id) => MasteryIfAny(id)?.Answered ?? 0;

    /// <summary>One answer in mastery practice: moves the score (LearningGames.Mastery), counts it and its time.
    /// Returns the score before and after. The answer is also recorded as before (RecordAnswer: levels, mistakes, days,
    /// reward points), so the progress report and the games keep working.</summary>
    public (int Before, int After) RecordMastery(string skill, bool correct, int seconds, string text, string answer, string given)
    {
        var m = Mastery(skill);
        int before = m.Score, after = global::LearningGames.Mastery.Next(before, correct);
        m.Score = after;
        m.Answered++;
        if (correct) m.Right++;
        m.Seconds += Math.Clamp(seconds, 0, 300);
        m.Last = Profiles.Today;
        if (after >= global::LearningGames.Mastery.Max && before < global::LearningGames.Mastery.Max) m.MasteredOn = Profiles.Today;
        RecordAnswer(skill, correct, text, answer, given);
        return (before, after);
    }

    public int RewardPoints
    {
        get
        {
            if (Json["Rewards"] is not JsonObject r)
            {
                // players from before 2026-10-24: credit for the math they already got right
                int sum = 0;
                if (Json["Skills"] is JsonObject sk) foreach (var kv in sk) if (kv.Value is JsonObject so) sum += Int(so["Correct"]);
                r = new JsonObject { ["Points"] = sum };
                Json["Rewards"] = r;
            }
            return Int(r["Points"]);
        }
    }
    public void AddRewardPoints(int n) { int now = RewardPoints; ((JsonObject)Json["Rewards"])["Points"] = now + n; }

    /// <summary>A math answer: level up after 4 right in a row, down after 2 wrong; returns "up", "down" or "".</summary>
    public string RecordAnswer(string skill, bool correct, string text, string answer, string given)
    {
        var st = Skill(skill);
        st.Attempts++;
        var recent = st.Recent; recent.Add(correct ? 1 : 0); if (recent.Count > 20) recent.RemoveRange(0, recent.Count - 20);
        st.Recent = recent;
        st.Last = Profiles.Today;
        if (correct && Grade > 0) AddRewardPoints(1);
        string change = "";
        if (correct)
        {
            st.Correct++; st.Streak++; st.Misses = 0;
            if (st.Streak >= 4 && st.Level < 5) { st.Level++; st.Streak = 0; change = "up"; }
        }
        else
        {
            st.Streak = 0; st.Misses++;
            if (st.Misses >= 2 && st.Level > 1) { st.Level--; st.Misses = 0; change = "down"; }
            AddMistake(skill, text, answer, given);
        }
        CountDay(correct);
        Profiles.Answered();
        return change;
    }

    public void AddMistake(string skill, string text, string answer, string given)
    {
        var m = Arr(Json, "Mistakes");
        m.Add(new JsonObject { ["When"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), ["Skill"] = skill, ["Text"] = text, ["Answer"] = answer, ["Given"] = given });
        while (m.Count > 40) m.RemoveAt(0);
    }

    public void CountDay(bool correct)
    {
        var days = Obj(Json, "Days");
        var d = Obj(days, Profiles.Today);
        d["Done"] = Int(d["Done"]) + 1;
        if (correct) d["Right"] = Int(d["Right"]) + 1;
        else if (d["Right"] == null) d["Right"] = 0;
    }

    public (int Done, int Right) Day(string date)
    {
        if (Json["Days"] is JsonObject days && days[date] is JsonObject d) return (Int(d["Done"]), Int(d["Right"]));
        return (0, 0);
    }
    public (int Done, int Right) TodayCount => Day(Profiles.Today);

    public int DayStreak
    {
        get
        {
            int n = 0; var d = DateTime.Now.Date;
            if (Day(Key(d)).Done == 0) d = d.AddDays(-1);
            while (Day(Key(d)).Done > 0) { n++; d = d.AddDays(-1); }
            return n;
        }
    }
    static string Key(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public List<Mistake> Mistakes
    {
        get
        {
            var l = new List<Mistake>();
            static Mistake M(JsonObject o) => new(Str(o["When"]), Str(o["Skill"]), Str(o["Text"]), Str(o["Answer"]), Str(o["Given"]));
            if (Json["Mistakes"] is JsonArray a) { foreach (var x in a) if (x is JsonObject o) l.Add(M(o)); }
            else if (Json["Mistakes"] is JsonObject one) l.Add(M(one));
            return l;
        }
    }

    public int Best(string game) => Json["Best"] is JsonObject b ? Int(b[game]) : 0;
    public void SetBest(string game, int score) { Obj(Json, "Best")[game] = score; }

    // ---- subjects (History, English, ...)
    public SubjectState Subject(string id)
    {
        var subs = Obj(Json, "Subjects");
        if (subs[id] is not JsonObject o)
        {
            o = new JsonObject { ["Level"] = Profiles.AgeLevel(Age), ["Recent"] = new JsonArray(), ["Answered"] = 0, ["Correct"] = 0, ["Cards"] = new JsonObject(), ["Best"] = new JsonObject() };
            subs[id] = o;
        }
        var s = new SubjectState(o);
        if (Array.IndexOf(Bank.LevelKeys, s.Level) < 0) s.Level = Profiles.AgeLevel(Age);
        s.Level = Bank.Nearest(s.Level, Bank.LevelsOf(id));
        return s;
    }

    /// <summary>A new age restarts every subject's level there.</summary>
    public void ResetSubjectLevels()
    {
        if (Json["Subjects"] is not JsonObject subs) return;
        foreach (var kv in subs) if (kv.Value is JsonObject o) { o["Level"] = Profiles.AgeLevel(Age); o["Recent"] = new JsonArray(); }
    }
}

public sealed record Mistake(string When, string Skill, string Text, string Answer, string Given);

public sealed class SkillStat
{
    readonly JsonObject o;
    public SkillStat(JsonObject o) { this.o = o; }
    public int Level { get => Math.Clamp(Player.Int(o["Level"], 1), 1, 5); set => o["Level"] = value; }
    public int Attempts { get => Player.Int(o["Attempts"]); set => o["Attempts"] = value; }
    public int Correct { get => Player.Int(o["Correct"]); set => o["Correct"] = value; }
    public int Streak { get => Player.Int(o["Streak"]); set => o["Streak"] = value; }
    public int Misses { get => Player.Int(o["Misses"]); set => o["Misses"] = value; }
    public string Last { get => Player.Str(o["Last"]); set => o["Last"] = value; }
    public List<int> Recent { get => Player.Ints(o["Recent"]); set => o["Recent"] = Player.IntArray(value); }
    public double Accuracy { get { var r = Recent; return r.Count == 0 ? 0 : r.Average(); } }
    public bool Mastered => Level >= 4 && Recent.Count >= 10 && Accuracy >= 0.85;

    /// <summary>"not tried", "mastered", "needs work" or "getting there" (the progress page's words).</summary>
    public static string State(SkillStat st)
    {
        if (st == null || st.Attempts == 0) return "not tried";
        if (st.Mastered) return "mastered";
        if (st.Recent.Count >= 5 && st.Accuracy < 0.7) return "needs work";
        return "getting there";
    }
}

/// <summary>A player's mastery of one math skill (Player.Mastery).</summary>
public sealed class MasteryStat
{
    readonly JsonObject o;
    public MasteryStat(JsonObject o) { this.o = o; }
    public int Score { get => global::LearningGames.Mastery.Clamp(Player.Int(o["Score"])); set => o["Score"] = global::LearningGames.Mastery.Clamp(value); }
    public int Answered { get => Player.Int(o["Answered"]); set => o["Answered"] = value; }
    public int Right { get => Player.Int(o["Right"]); set => o["Right"] = value; }
    public int Seconds { get => Player.Int(o["Seconds"]); set => o["Seconds"] = value; }
    public string Last { get => Player.Str(o["Last"]); set => o["Last"] = value; }
    public string MasteredOn { get => Player.Str(o["MasteredOn"]); set => o["MasteredOn"] = value; }
    public bool Mastered => Score >= global::LearningGames.Mastery.Max;
}

public sealed class SubjectState
{
    readonly JsonObject o;
    public SubjectState(JsonObject o) { this.o = o; }
    public string Level { get => Player.Str(o["Level"]); set => o["Level"] = value; }
    public int Answered { get => Player.Int(o["Answered"]); set => o["Answered"] = value; }
    public int Correct { get => Player.Int(o["Correct"]); set => o["Correct"] = value; }
    public List<int> Recent { get => Player.Ints(o["Recent"]); set => o["Recent"] = Player.IntArray(value); }
    public JsonObject Cards => Player.Obj(o, "Cards");
    public int Best(string mode) => o["Best"] is JsonObject b ? Player.Int(b[mode]) : 0;
    public void SetBest(string mode, int v) { Player.Obj(o, "Best")[mode] = v; }

    public (int Box, string Due) Card(string qid)
    {
        if (Cards[qid] is JsonObject c) return (Player.Int(c["Box"]), Player.Str(c["Due"]));
        return (-1, null);
    }

    public int DueCount
    {
        get
        {
            string today = Profiles.Today; int n = 0;
            foreach (var kv in Cards) if (kv.Value is JsonObject c && string.CompareOrdinal(Player.Str(c["Due"]), today) <= 0) n++;
            return n;
        }
    }

    /// <summary>One answer: the spaced-review box (a miss back to box 1, tomorrow; right moves up: 2, 4, 8, 16 days).</summary>
    public void RecordCard(string qid, bool correct)
    {
        var c = Cards[qid] as JsonObject;
        if (c == null) { c = new JsonObject { ["Box"] = 0, ["Due"] = "" }; Cards[qid] = c; }
        int box = correct ? Math.Min(5, Player.Int(c["Box"]) + 1) : 1;
        int[] days = { 1, 1, 2, 4, 8, 16 };
        c["Box"] = box;
        c["Due"] = DateTime.Now.AddDays(correct ? days[box] : 1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
