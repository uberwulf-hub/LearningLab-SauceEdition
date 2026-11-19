// Learning Lab - Languages: what this device has learned. Saved in <data>\Languages\progress.json through the safe-save
// helper (backups, a file that can't be read is kept aside and never overwritten without asking). Unknown fields are
// kept and written back, so a newer Learning Lab's additions survive an older one.
//   { "format": 1,
//     "languages": { "es": { "items": { "<id>": { "box": 2, "due": "2026-11-12", "seen": 3, "right": 2, "wrong": 1 } },
//                            "units": { "<unit id>": { "quiz": 9, "heard": 2 } }, "listened": 125.5, "days": ["2026-11-10"],
//                            (since 2026-11-11, still format 1: older files simply lack them)
//                            "lessons": { "<lesson id>": { "done": 1, "best": 90, "last": "..." } }, "points": { "2026-11-11": 14 },
//                            "stories": { "<id>": { "done": 1, "best": 3 } }, "verbs": { "<infinitive>": { "right": 5, "wrong": 1 } } } },
//     "settings": { "voice-es": "...", "voice-en": "...", "rate": 0.9, "style": "learn", "english": true, "goal": 20 } }
// Review boxes: right moves an item up one box, wrong back to box 1; box n is due again after Days[n] days. An item in
// box 3 or more counts as learned.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LearningLab.Saves;

namespace LearningLab.Languages
{
    public class LangProgress
    {
        public static readonly int[] Days = { 0, 1, 2, 4, 8, 16, 32 };
        public const int LearnedBox = 3;

        JsonObject root = new JsonObject();
        public string File { get; private set; }
        /// <summary>A sentence for the person when the file couldn't be read (null when all is well).</summary>
        public string Message { get; private set; }
        /// <summary>True when the saved file couldn't be read and no backup could: nothing is written until the person agrees.</summary>
        public bool Blocked { get; private set; }
        /// <summary>The clock (the checks pin it).</summary>
        public static Func<DateTime> Now { get; set; } = () => DateTime.Now;
        static string Today => Now().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        public static LangProgress Load(string file)
        {
            var p = new LangProgress { File = file };
            if (!System.IO.File.Exists(file) && SafeFile.Backups(file).Count == 0) { p.root["format"] = 1; return p; }
            try
            {
                SaveLoad info;
                var r = SafeFile.Load(file, b => Parse(Encoding.UTF8.GetString(b)), out info);
                if (r != null) p.root = r;
                else { p.Blocked = true; p.root["format"] = 1; }
                p.Message = info != null ? info.Message : null;
            }
            catch (Exception ex) { p.Blocked = true; p.Message = "Your language progress couldn't be read (" + ex.Message + "). It is kept as it was."; }
            return p;
        }

        static JsonObject Parse(string text)
        {
            var n = JsonNode.Parse(text.TrimStart('﻿')) as JsonObject;
            if (n == null) throw new FormatException("not a progress file");
            return n;
        }

        /// <summary>Starts fresh over a file that couldn't be read (only when the person chose to).</summary>
        public void StartFresh() { root = new JsonObject { ["format"] = 1 }; Blocked = false; Message = null; }

        public void Save()
        {
            if (Blocked || File == null) return;
            Directory.CreateDirectory(Path.GetDirectoryName(File));
            SafeFile.WriteText(File, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }

        JsonObject Obj(JsonObject parent, string name)
        {
            var o = parent[name] as JsonObject;
            if (o == null) { o = new JsonObject(); parent[name] = o; }
            return o;
        }

        JsonObject Lang(string lang) { return Obj(Obj(root, "languages"), lang); }
        JsonObject ItemNode(string lang, string id) { return Obj(Obj(Lang(lang), "items"), id); }

        static int Int(JsonNode n, int dflt = 0)
        {
            if (n == null) return dflt;
            try { var v = n.AsValue(); int i; if (v.TryGetValue(out i)) return i; double d; if (v.TryGetValue(out d)) return (int)d; string s; if (v.TryGetValue(out s) && int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out i)) return i; }
            catch (InvalidOperationException) { }
            return dflt;
        }
        static double Dbl(JsonNode n, double dflt)
        {
            if (n == null) return dflt;
            try { var v = n.AsValue(); double d; if (v.TryGetValue(out d)) return d; string s; if (v.TryGetValue(out s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d; }
            catch (InvalidOperationException) { }
            return dflt;
        }
        static string Str(JsonNode n) { try { string s; return n != null && n.AsValue().TryGetValue(out s) ? s : null; } catch (InvalidOperationException) { return null; } }

        public int Box(string lang, string id) { var o = Obj(Lang(lang), "items")[id] as JsonObject; return o == null ? 0 : Math.Clamp(Int(o["box"]), 0, Days.Length - 1); }
        public bool Seen(string lang, string id) { return Obj(Lang(lang), "items")[id] is JsonObject; }
        public bool Learned(string lang, string id) { return Box(lang, id) >= LearnedBox; }

        public bool Due(string lang, string id)
        {
            var o = Obj(Lang(lang), "items")[id] as JsonObject;
            if (o == null) return false;
            string due = Str(o["due"]);
            return due == null || string.CompareOrdinal(due, Today) <= 0;
        }

        /// <summary>Marks an item as met (heard in a lesson or shown on a card) without grading it.</summary>
        public void Meet(string lang, string id)
        {
            var o = ItemNode(lang, id);
            o["seen"] = Int(o["seen"]) + 1;
            if (o["box"] == null) { o["box"] = 0; o["due"] = Today; }
            Day(lang);
        }

        /// <summary>Records an answer: right moves the item up a box, wrong back to box 1.</summary>
        public void Grade(string lang, string id, bool right)
        {
            var o = ItemNode(lang, id);
            int box = Math.Clamp(Int(o["box"]), 0, Days.Length - 1);
            box = right ? Math.Min(box + 1, Days.Length - 1) : 1;
            o["box"] = box;
            o["due"] = Now().AddDays(right ? Days[box] : 0).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            o["seen"] = Int(o["seen"]) + 1;
            if (right) o["right"] = Int(o["right"]) + 1; else o["wrong"] = Int(o["wrong"]) + 1;
            Day(lang);
        }

        void Day(string lang)
        {
            var l = Lang(lang);
            var days = l["days"] as JsonArray;
            if (days == null) { days = new JsonArray(); l["days"] = days; }
            string t = Today;
            if (!days.Any(d => Str(d) == t)) days.Add(t);
            while (days.Count > 400) days.RemoveAt(0);
        }

        /// <summary>Days in a row ending today or yesterday.</summary>
        public int Streak(string lang)
        {
            var days = Lang(lang)["days"] as JsonArray;
            if (days == null) return 0;
            var set = new HashSet<string>(days.Select(Str).Where(s => s != null));
            var d = Now().Date;
            if (!set.Contains(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))) d = d.AddDays(-1);
            int n = 0;
            while (set.Contains(d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))) { n++; d = d.AddDays(-1); }
            return n;
        }

        public int BestQuiz(string lang, string unit) { var u = Obj(Lang(lang), "units")[unit] as JsonObject; return u == null ? 0 : Int(u["quiz"]); }
        public void SetQuiz(string lang, string unit, int score) { var u = Obj(Obj(Lang(lang), "units"), unit); if (score > Int(u["quiz"])) u["quiz"] = score; }
        public void Heard(string lang, string unit) { var u = Obj(Obj(Lang(lang), "units"), unit); u["heard"] = Int(u["heard"]) + 1; }
        public int TimesHeard(string lang, string unit) { var u = Obj(Lang(lang), "units")[unit] as JsonObject; return u == null ? 0 : Int(u["heard"]); }

        public double Listened(string lang) { return Dbl(Lang(lang)["listened"], 0); }
        public void AddListened(string lang, double minutes) { if (minutes > 0) Lang(lang)["listened"] = Math.Round(Listened(lang) + minutes, 1); }

        // settings
        public string Setting(string name) { return Str(Obj(root, "settings")[name]); }
        public void SetSetting(string name, string value) { var s = Obj(root, "settings"); if (value == null) s.Remove(name); else s[name] = value; }
        public double Rate { get { double r = Dbl(Obj(root, "settings")["rate"], 0.9); return Math.Clamp(r, 0.5, 1.5); } set { Obj(root, "settings")["rate"] = Math.Round(Math.Clamp(value, 0.5, 1.5), 2); } }

        // lessons (since 2026-11-11): "lessons": { "<lesson id>": { "done": 2, "best": 90, "last": "2026-11-11" } }
        public int LessonTimes(string lang, string id) { var o = Obj(Lang(lang), "lessons")[id] as JsonObject; return o == null ? 0 : Int(o["done"]); }
        public bool LessonDone(string lang, string id) { return LessonTimes(lang, id) > 0; }
        public int LessonBest(string lang, string id) { var o = Obj(Lang(lang), "lessons")[id] as JsonObject; return o == null ? 0 : Int(o["best"]); }
        /// <summary>A lesson finished with a score of 0-100.</summary>
        public void FinishLesson(string lang, string id, int score)
        {
            var o = Obj(Obj(Lang(lang), "lessons"), id);
            o["done"] = Int(o["done"]) + 1;
            if (o["best"] == null || score > Int(o["best"])) o["best"] = Math.Clamp(score, 0, 100);
            o["last"] = Today;
            Day(lang);
        }

        // stories: "stories": { "<story id>": { "done": 1, "best": 3 } } (best = questions right)
        public int StoryTimes(string lang, string id) { var o = Obj(Lang(lang), "stories")[id] as JsonObject; return o == null ? 0 : Int(o["done"]); }
        public int StoryBest(string lang, string id) { var o = Obj(Lang(lang), "stories")[id] as JsonObject; return o == null ? 0 : Int(o["best"]); }
        public void FinishStory(string lang, string id, int right)
        {
            var o = Obj(Obj(Lang(lang), "stories"), id);
            o["done"] = Int(o["done"]) + 1;
            if (o["best"] == null || right > Int(o["best"])) o["best"] = right;
            Day(lang);
        }

        // cartoons: "cartoons": { "<cartoon id>": { "done": 1, "best": 3 } } (best = questions right)
        public int CartoonTimes(string lang, string id) { var o = Obj(Lang(lang), "cartoons")[id] as JsonObject; return o == null ? 0 : Int(o["done"]); }
        public int CartoonBest(string lang, string id) { var o = Obj(Lang(lang), "cartoons")[id] as JsonObject; return o == null ? 0 : Int(o["best"]); }
        public void FinishCartoon(string lang, string id, int right)
        {
            var o = Obj(Obj(Lang(lang), "cartoons"), id);
            o["done"] = Int(o["done"]) + 1;
            if (o["best"] == null || right > Int(o["best"])) o["best"] = right;
            Day(lang);
        }

        // daily points: "points": { "2026-11-11": 14 } (the last 400 days); the goal is settings "goal"
        public static readonly int[] Goals = { 10, 20, 30, 50 };
        public int Goal
        {
            get { int g = Int(Obj(root, "settings")["goal"], 20); return Goals.Contains(g) ? g : 20; }
            set { Obj(root, "settings")["goal"] = Goals.Contains(value) ? value : 20; }
        }
        public int Points(string lang, DateTime day) { return Int(Obj(Lang(lang), "points")[day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)]); }
        public int PointsToday(string lang) { return Points(lang, Now()); }
        public void AddPoints(string lang, int n)
        {
            if (n <= 0) return;
            var pts = Obj(Lang(lang), "points");
            string t = Today;
            pts[t] = Int(pts[t]) + n;
            if (pts.Count > 400)
            {
                var old = pts.Select(kv => kv.Key).OrderBy(k => k, StringComparer.Ordinal).Take(pts.Count - 400).ToList();
                foreach (var k in old) pts.Remove(k);
            }
            Day(lang);
        }

        // verb trainer: "verbs": { "tener": { "right": 5, "wrong": 1, "present-right": 3, ... } }
        public void VerbAnswer(string lang, string verb, string tense, bool right)
        {
            var o = Obj(Obj(Lang(lang), "verbs"), verb);
            string k = right ? "right" : "wrong";
            o[k] = Int(o[k]) + 1;
            o[tense + "-" + k] = Int(o[tense + "-" + k]) + 1;
            Day(lang);
        }
        public (int Right, int Wrong) VerbStats(string lang, string verb)
        {
            var o = Obj(Lang(lang), "verbs")[verb] as JsonObject;
            return o == null ? (0, 0) : (Int(o["right"]), Int(o["wrong"]));
        }

        /// <summary>How far a unit is: (learned, met, total).</summary>
        public (int Learned, int Met, int Total) UnitState(string lang, LangUnit u)
        {
            int l = 0, m = 0;
            foreach (var i in u.Items) { if (Seen(lang, i.Id)) m++; if (Learned(lang, i.Id)) l++; }
            return (l, m, u.Items.Count);
        }

        /// <summary>Items due for review today, oldest box first (only items met before).</summary>
        public List<LangItem> DueItems(LangCourse c)
        {
            return c.AllItems.Where(i => Seen(c.Language, i.Id) && Due(c.Language, i.Id)).OrderBy(i => Box(c.Language, i.Id)).ThenBy(i => i.Unit.Number).ToList();
        }
    }
}
