// Learning Lab - the math mastery checks (group "math-mastery", 2026-11-11): the score's curve tuned by simulation, the
// worked steps of every 2nd and 6th grade skill at every level for thousands of seeds (every sum, difference, product
// and quotient written in them recomputed, the last line stating the computed answer), saving the scores in
// profiles.json (older files load, unknown fields stay), and the skill map and practice played through off screen:
// a right answer, a wrong one with its explanation, and mastering a skill. -Snapshot saves the pages wide and phone-sized.
using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Threading;
using LearningGames;
using LearningLab.LearningGames;
using LearningLab.Views;

namespace LearningLab.Tests;

static class MasteryChecks
{
    static void Check(string what, bool ok, string detail = null) => Program.Check("math-mastery: " + what, ok, detail);
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static void Run()
    {
        Sound.Enabled = false;
        Curve();
        StepsForEverySkill();
        Saves();
        Pages();
    }

    // ---------------------------------------------------------------- the curve, by simulation
    /// <summary>Questions until 100 for a player right with probability p (500 at most).</summary>
    static int ToMaster(Random r, double p)
    {
        int s = 0, n = 0;
        while (s < Mastery.Max && n < 500) { s = Mastery.Next(s, r.NextDouble() < p); n++; }
        return n;
    }

    public static string CurveReport = "";

    static void Curve()
    {
        // the basic shape
        bool upAlways = true, downAlways = true, bounded = true;
        for (int s = 0; s <= 100; s++)
        {
            int up = Mastery.Next(s, true), down = Mastery.Next(s, false);
            if (s < 100 && up <= s) upAlways = false;
            if (s > 0 && s < 100 && down >= s) downAlways = false;
            if (up < 0 || up > 100 || down < 0 || down > 100) bounded = false;
        }
        Check("a right answer always raises the score below 100", upAlways);
        Check("a miss always lowers the score between 1 and 99", downAlways);
        Check("the score stays within 0-100", bounded);
        Check("rises quickly at first, slowly near the top", Mastery.Next(0, true) - 0 > Mastery.Next(75, true) - 75 && Mastery.Next(75, true) - 75 > Mastery.Next(95, true) - 95);
        Check("a miss costs little at low scores and more in the Challenge zone", 10 - Mastery.Next(10, false) < 50 - Mastery.Next(50, false) && 50 - Mastery.Next(50, false) < 92 - Mastery.Next(92, false));
        Check("Mastered stays Mastered", Mastery.Next(100, false) == 100 && Mastery.Next(100, true) == 100);
        bool three = true;
        for (int s = 90; s < 100; s++) { int x = s; for (int k = 0; k < 3; k++) x = Mastery.Next(x, false); if (x >= 90) three = false; }
        Check("three misses in a row in the Challenge zone drop below 90", three);
        // zones, names, difficulty
        Check("zone names", Mastery.ZoneName(0) == "Getting started" && Mastery.ZoneName(39) == "Getting started" && Mastery.ZoneName(40) == "Building" && Mastery.ZoneName(69) == "Building"
            && Mastery.ZoneName(70) == "Strong" && Mastery.ZoneName(89) == "Strong" && Mastery.ZoneName(90) == "Challenge zone" && Mastery.ZoneName(99) == "Challenge zone" && Mastery.ZoneName(100) == "Mastered");
        bool levels = Mastery.LevelFor(0) == 1 && Mastery.LevelFor(100) == 5;
        for (int s = 1; s <= 100; s++) if (Mastery.LevelFor(s) < Mastery.LevelFor(s - 1)) levels = false;
        Check("question difficulty follows the score (1 at 0 up to 5 in the Challenge zone)", levels && Mastery.LevelFor(90) == 5 && Mastery.LevelFor(89) == 4 && Mastery.LevelFor(45) == 3);

        // a player right 9 times in 10 masters a skill in about 15-25 questions
        var r = new Random(20261111);
        var q90 = Enumerable.Range(0, 4000).Select(_ => ToMaster(r, 0.9)).OrderBy(x => x).ToList();
        int median = q90[q90.Count / 2];
        Check("right 90% of the time: mastered in about 15-25 questions (median)", median >= 15 && median <= 25, "median " + median);
        // right 7 times in 10: reaches Strong, rarely Mastered
        int strong = 0, m30 = 0, m40 = 0, sims = 4000;
        for (int i = 0; i < sims; i++)
        {
            int s = 0, best = 0, first = 999;
            for (int n = 1; n <= 40; n++) { s = Mastery.Next(s, r.NextDouble() < 0.7); best = Math.Max(best, s); if (s >= 100) { first = n; break; } }
            if (best >= 70) strong++;
            if (first <= 30) m30++;
            if (first <= 40) m40++;
        }
        Check("right 70% of the time: reaches Strong within 40 questions", strong >= sims * 0.95, strong + " of " + sims);
        Check("right 70% of the time: rarely Mastered (at most 1 in 5 within 30 questions)", m30 <= sims * 0.2, m30 + " of " + sims);
        // and a player right half the time stays below the Challenge zone most of the time
        int m50 = 0;
        for (int i = 0; i < sims; i++) { if (ToMaster(r, 0.5) <= 40) m50++; }
        Check("right 50% of the time: almost never Mastered within 40 questions", m50 <= sims * 0.02, m50 + " of " + sims);
        CurveReport = "90% right: median " + median + " questions to master (10th-90th percentile " + q90[q90.Count / 10] + "-" + q90[q90.Count * 9 / 10] + "); 70% right: Strong "
            + (100 * strong / sims) + "% within 40, Mastered " + (100 * m30 / sims) + "% within 30 and " + (100 * m40 / sims) + "% within 40; 50% right: Mastered " + (100.0 * m50 / sims).ToString("0.0", Inv) + "% within 40";
        Console.WriteLine("  " + CurveReport);

        // Up next: started-not-mastered first (closest to done first), then new skills in standard order
        var order = new[] { "a", "b", "c", "d", "e" };
        var score = new Dictionary<string, int> { ["a"] = 100, ["b"] = 40, ["c"] = 0, ["d"] = 80, ["e"] = 0 };
        var ans = new Dictionary<string, int> { ["a"] = 20, ["b"] = 5, ["c"] = 0, ["d"] = 9, ["e"] = 0 };
        var next = Mastery.UpNext(order, x => score[x], x => ans[x], 3);
        Check("Up next: started skills first (highest score first), then new ones in order, never a mastered one", string.Join(",", next) == "d,b,c", string.Join(",", next));
    }

    // ---------------------------------------------------------------- worked steps
    /// <summary>Every "a + b = c" (and -, x, /, chains, parentheses, "q R r") written in a line, recomputed. Returns how many
    /// were checked; bad gets the first one that's wrong.</summary>
    public static int CheckArithmetic(string line, out string bad)
    {
        bad = null;
        string s = line.Replace("$", "").Replace("¢", "").Replace("−", "-").Replace("×", "*").Replace("÷", "/");
        s = Regex.Replace(s, @"(?<=\d),(?=\d{3}(?!\d))", "");
        int count = 0;
        for (int i = s.IndexOf('='); i >= 0; i = s.IndexOf('=', i + 1))
        {
            // the expression on the left: digits, operators, points, parentheses and spaces, back to anything else
            int j = i - 1;
            while (j >= 0)
            {
                char c = s[j];
                if (c == '.' && j + 1 < s.Length && s[j + 1] == ' ') break;   // the end of a sentence
                if ("0123456789.+-*/() ".IndexOf(c) < 0) break;
                j--;
            }
            string left = s.Substring(j + 1, i - j - 1).Trim();
            // the number on the right, and what follows it
            var m = Regex.Match(s.Substring(i + 1), @"^\s*(-?\d+(?:\.\d+)?)");
            if (!m.Success || left.Length == 0) continue;
            double right = double.Parse(m.Groups[1].Value, Inv);
            string after = s.Substring(i + 1 + m.Length);
            string afterT = after.TrimStart();
            if (afterT.Length > 0 && ("+-*/^0123456789".IndexOf(afterT[0]) >= 0 || afterT.StartsWith(":"))) continue;   // the right side goes on: not a single number
            if (afterT.StartsWith("%")) right /= 100;
            var rm = Regex.Match(afterT, @"^R\s*(\d+)");
            if (!TryEval(left, out double value, out bool hasOp) || !hasOp) continue;
            count++;
            if (rm.Success)
            {
                // a quotient and a remainder: left must be "n / d"
                var dm = Regex.Match(left, @"^(\d+)\s*/\s*(\d+)$");
                if (!dm.Success) { count--; continue; }
                long n = long.Parse(dm.Groups[1].Value, Inv), d = long.Parse(dm.Groups[2].Value, Inv), r = long.Parse(rm.Groups[1].Value, Inv);
                if (n / d != (long)right || n % d != r) { bad ??= left + " = " + right + " R " + r; }
                continue;
            }
            if (afterT.StartsWith("%") == false && Math.Abs(value - right) > 1e-6 * Math.Max(1, Math.Abs(right))) bad ??= left + " = " + m.Groups[1].Value + " (it is " + value.ToString(Inv) + ")";
            else if (afterT.StartsWith("%") && Math.Abs(value - right) > 1e-6 && Math.Abs(value - right * 100) > 1e-6) bad ??= left + " = " + m.Groups[1].Value + "%";
        }
        return count;
    }

    /// <summary>Evaluates + - * / with parentheses and signs; false when it isn't a plain expression.</summary>
    static bool TryEval(string text, out double value, out bool hasOp)
    {
        value = 0; hasOp = false;
        var tokens = new List<string>();
        foreach (Match t in Regex.Matches(text, @"\d+(?:\.\d+)?|[+\-*/()]|\S"))
        {
            if (Regex.IsMatch(t.Value, @"^[\d.+\-*/()]+$") == false) return false;
            tokens.Add(t.Value);
        }
        if (tokens.Count < 3) return false;
        if ("+*/)".Contains(tokens[0])) return false;
        int pos = 0; bool op = false, ok = true;
        double Expr()
        {
            double v = Term();
            while (ok && pos < tokens.Count && (tokens[pos] == "+" || tokens[pos] == "-")) { string o = tokens[pos++]; op = true; double w = Term(); v = o == "+" ? v + w : v - w; }
            return v;
        }
        double Term()
        {
            double v = Factor();
            while (ok && pos < tokens.Count && (tokens[pos] == "*" || tokens[pos] == "/")) { string o = tokens[pos++]; op = true; double w = Factor(); if (o == "/" && w == 0) { ok = false; return 0; } v = o == "*" ? v * w : v / w; }
            return v;
        }
        double Factor()
        {
            if (pos >= tokens.Count) { ok = false; return 0; }
            string t = tokens[pos++];
            if (t == "-") return -Factor();
            if (t == "(") { double v = Expr(); if (pos >= tokens.Count || tokens[pos] != ")") { ok = false; return 0; } pos++; return v; }
            if (double.TryParse(t, NumberStyles.Float, Inv, out double d)) return d;
            ok = false; return 0;
        }
        value = Expr();
        hasOp = op;
        return ok && pos == tokens.Count;
    }

    /// <summary>Skills whose every explanation must contain sums or products we can recompute.</summary>
    static readonly HashSet<string> MustAddUp = new() {
        "add20", "sub20", "add100", "sub100", "add1000", "regroup", "longdiv", "arrays", "exponents", "evaluate", "equations", "area", "volume", "rate", "ratio",
        "skip", "place", "placeblocks", "fracdiv", "fracmul", "fracadd", "gcflcm", "decimals", "distrib", "pctwhole", "polyarea", "mixed" };

    static void StepsForEverySkill()
    {
        var skills = Problems.Skills.Where(s => s.Grade == 2 || s.Grade == 6).Select(s => s.Id).ToList();
        Check("there are 2nd and 6th grade skills", skills.Count >= 50, skills.Count.ToString());
        int made = 0, equations = 0;
        var fails = new Dictionary<string, string>();
        var withEq = new Dictionary<string, (int Yes, int All)>();
        const int seeds = 2000;
        for (int seed = 0; seed < seeds; seed++)
        {
            Problems.Seed(70000 + seed);
            foreach (string id in skills)
            {
                int lv = 1 + (seed + id.Length) % 5;   // each skill meets every level about seeds / 5 times
                Problem p = Problems.Make(id, lv);
                made++;
                string[] st = Worked.Steps(p);
                string key = id + " level " + lv;
                void Fail(string why) { if (!fails.ContainsKey(key)) fails[key] = why + "  [" + p.Text + " -> " + p.Answer + "]  " + string.Join(" | ", st); }
                if (st.Length < 2) Fail("fewer than two steps");
                if (st.Length > 0 && st[^1] != Worked.Final(p)) Fail("the last line is not the answer line");
                if (st.Length > 0 && !st[^1].Contains(p.Answer)) Fail("the last line doesn't state the answer");
                if (st.Any(x => Regex.IsMatch(x, @"=\s*\?"))) Fail("a step still has \"= ?\"");
                if (p.Kind != "choice" && !st.Take(st.Length - 1).Any(x => Regex.IsMatch(x, @"\d"))) Fail("no step uses the question's numbers");
                int n = 0;
                foreach (string line in st)
                {
                    n += CheckArithmetic(line, out string bad);
                    if (bad != null) Fail("arithmetic: " + bad);
                }
                equations += n;
                var w = withEq.TryGetValue(id, out var t) ? t : (Yes: 0, All: 0);
                withEq[id] = (w.Yes + (n > 0 ? 1 : 0), w.All + 1);
            }
        }
        foreach (var kv in fails.Take(25)) Check("steps: " + kv.Key, false, kv.Value);
        Check("worked steps for every skill at every level (" + made + " problems, " + equations + " sums recomputed)", fails.Count == 0, fails.Count + " skill/level pairs failed");
        foreach (string id in MustAddUp)
        {
            if (!withEq.TryGetValue(id, out var t)) { Check(id + ": has problems", false); continue; }
            bool every = id is "add20" or "sub20" or "add100" or "sub100" or "add1000" or "regroup" or "longdiv" or "arrays" or "exponents" or "evaluate" or "equations" or "area" or "volume" or "rate" or "ratio";
            Check(id + ": the steps' arithmetic adds up and is recomputed" + (every ? " in every explanation" : ""), every ? t.Yes == t.All : t.Yes > 0, t.Yes + " of " + t.All);
        }
        // the column methods themselves, on many numbers (their last lines say the right answer)
        var rr = new Random(5);
        bool cols = true; string colBad = "";
        for (int i = 0; i < 5000 && cols; i++)
        {
            long a = rr.Next(10, 100000), b = rr.Next(1, (int)a);
            var add = Worked.ColumnAdd(a, b); var sub = Worked.ColumnSub(a, b);
            if (!add[^1].Contains(" = " + (a + b) + ".") || !sub.Any(l => l.Contains(" = " + (a - b) + "."))) { cols = false; colBad = a + ", " + b; }
            foreach (var l in add.Concat(sub)) { CheckArithmetic(l, out string bad); if (bad != null) { cols = false; colBad = a + ", " + b + ": " + bad; } }
        }
        Check("column adding and subtracting: 5,000 pairs add up", cols, colBad);
        bool lds = true; string ldBad = "";
        for (int i = 0; i < 2000 && lds; i++)
        {
            long n = rr.Next(20, 99999); int d = rr.Next(2, 99);
            var l = Worked.LongDiv(n, d);
            string want = n % d == 0 ? (n / d).ToString(Inv) : (n / d) + " R " + (n % d);
            if (!l[^1].EndsWith(" = " + want + ".")) { lds = false; ldBad = n + " / " + d; }
            foreach (var x in l) { CheckArithmetic(x, out string bad); if (bad != null) { lds = false; ldBad = n + " / " + d + ": " + bad; } }
        }
        Check("long division steps: 2,000 divisions add up", lds, ldBad);
        // the checker itself catches a wrong sum
        CheckArithmetic("Tens: 4 + 3 + 1 = 9.", out string caught);
        CheckArithmetic("So 1234 ÷ 7 = 176 R 3.", out string caughtR);
        CheckArithmetic("So 1,234 ÷ 7 = 176 R 2.", out string fineR);
        Check("the arithmetic checker catches mistakes", caught != null && caughtR != null && fineR == null, caught + " / " + caughtR + " / " + fineR);
    }

    // ---------------------------------------------------------------- saves
    static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "LearningGames", name);
    static string Copy(string fixture)
    {
        string dir = Path.Combine(Data.Root, "mastery-" + Path.GetFileNameWithoutExtension(fixture));
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);
        string f = Path.Combine(dir, "profiles.json");
        File.Copy(Fixture(fixture), f);
        return f;
    }

    static void Saves()
    {
        // a file from before mastery: every score 0, and recording one adds "Mastery" without touching the rest
        string old = Copy("learninggames-2026-10-24.json");
        Profiles.UseFile(old); Profiles.Load();
        var theo = Profiles.All.First(p => p.Name == "Theo");
        Check("an older file loads with no mastery scores", Profiles.State == ProfilesState.Ok && theo.MasteryScore("ratio") == 0 && theo.MasteryAnswered("ratio") == 0 && theo.MasteryIfAny("ratio") == null);
        int attempts = theo.Skill("ratio").Attempts;
        var (b0, a0) = theo.RecordMastery("ratio", true, 12, "1 : 2 = 3 : ?", "6", "6");
        Check("a right answer moves the score and still counts in Skills", b0 == 0 && a0 == Mastery.Next(0, true) && theo.Skill("ratio").Attempts == attempts + 1);
        Check("it saves", Profiles.Save());
        var back = JsonNode.Parse(File.ReadAllText(old)) as JsonArray;
        var theoJ = back.OfType<JsonObject>().First(o => o["Name"]?.ToString() == "Theo");
        Check("the new fields are in profiles.json, the old ones kept", theoJ["Mastery"]?["ratio"]?["Score"]?.GetValue<int>() == a0 && theoJ["Skills"]?["longdiv"] != null && theoJ["Best"]?["longdiv"]?.GetValue<int>() == 90);

        // a file with mastery: read, unknown fields written back
        string f = Copy("mastery-2026-11-11.json");
        Profiles.UseFile(f); Profiles.Load();
        var sam = Profiles.All.First(p => p.Name == "Sam");
        var riley = Profiles.All.First(p => p.Name == "Riley");
        Check("mastery scores load", Profiles.State == ProfilesState.Ok && sam.MasteryScore("add20") == 85 && sam.MasteryIfAny("evenodd").Mastered && riley.MasteryScore("percent") == 85 && riley.MasteryAnswered("ratio") == 6);
        sam.RecordMastery("add20", false, 5, "8 + 7 = ?", "15", "14");
        sam.RecordMastery("evenodd", false, 5, "Is 7 even or odd?", "odd", "even");
        Check("a miss in Strong costs points; a mastered skill stays mastered", sam.MasteryScore("add20") == Mastery.Next(85, false) && sam.MasteryScore("evenodd") == 100);
        Profiles.Save();
        var j = JsonNode.Parse(File.ReadAllText(f)) as JsonArray;
        var samJ = j.OfType<JsonObject>().First(o => o["Name"]?.ToString() == "Sam");
        var rileyJ = j.OfType<JsonObject>().First(o => o["Name"]?.ToString() == "Riley");
        Check("unknown fields inside a skill's mastery are kept", samJ["Mastery"]?["add20"]?["Medal"]?.ToString() == "silver" && samJ["Mastery"]?["add20"]?["Answered"]?.GetValue<int>() == 21);
        Check("unknown fields of a player are kept", rileyJ["Avatar"]?["Kind"]?.ToString() == "fox" && rileyJ["Mastery"]?["fracdiv"]?["MasteredOn"]?.ToString() == "2026-11-08");
        // a score out of range in the file is read as 0-100
        rileyJ["Mastery"]["ratio"]["Score"] = 250;
        File.WriteAllText(f, j.ToJsonString());
        Profiles.Load();
        Check("a score out of range reads as 100 at most", Profiles.All.First(p => p.Name == "Riley").MasteryScore("ratio") == 100);
    }

    // ---------------------------------------------------------------- the pages, off screen
    static Window win;
    static void Snap(string name) { LearningLab.Tests.Snap.Save(win, name); }
    static void Pump() { Dispatcher.UIThread.RunJobs(); }

    static void Pages()
    {
        string f = Copy("mastery-2026-11-11.json");
        Lg.Reset();
        Profiles.UseFile(f);
        Lg.EnsureLoaded();
        var riley = Profiles.All.First(p => p.Name == "Riley");
        var sam = Profiles.All.First(p => p.Name == "Sam");
        Lg.Me = riley;
        win = new Window { Width = 1180, Height = 800, Content = new MainView() };
        win.Show();
        var mv = MainView.Current;
        Problems.Seed(424242);

        // the Math subject: the skill map first, and Up next for Riley
        var math = HomePage.Categories.First(c => c.Id == "math");
        mv.Push(new CategoryPage(math)); Pump();
        Snap("math-subject");
        var cp = (CategoryPage)mv.Page;
        string pageText = string.Join(" ", cp.View.GetVisualDescendantsText());
        Check("the Math page shows Up next for the player", pageText.Contains("Up next for Riley"));

        // the skill map
        var map = new SkillMapPage(); mv.Push(map); Pump();
        Check("the map opens on the player's grade", map.Grade == 6);
        var six = Problems.SkillsFor(6);
        var tiles = map.SkillTiles;
        Check("every 6th grade skill has a tile (plus Up next)", six.All(id => tiles.Any(t => (string)t.Tag == id)) && tiles.Count >= six.Length, tiles.Count + " tiles");
        var upNext = MasteryUi.UpNext(riley, 6, 3);
        var firstNew = six.First(id => riley.MasteryAnswered(id) == 0);
        Check("Up next: percent (85), ratio (55), then the first new skill in order; never fracdiv (mastered)", string.Join(",", upNext) == "percent,ratio," + firstNew, string.Join(",", upNext));
        string mapText = string.Join(" ", map.View.GetVisualDescendantsText());
        Check("the map shows the standards, zones and counts", mapText.Contains("6.RP.A.3c") && mapText.Contains("Strong") && mapText.Contains("Building") && mapText.Contains("1 of " + six.Length + " skills mastered"), "");
        Snap("mastery-map");
        map.ShowGrade(3); Pump();
        Check("a grade without skills says they're coming", map.SkillTiles.Count <= 3 && string.Join(" ", map.View.GetVisualDescendantsText()).Contains("coming"));
        Snap("mastery-map-coming");
        map.ShowGrade(6); Pump();

        // practice: the level follows the score
        tiles = map.SkillTiles;
        tiles.First(t => (string)t.Tag == "ratio").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Pump();
        var pp = mv.Page as MasteryPracticePage;
        Check("practice opens with a question at the score's level", pp != null && pp.Current != null && pp.Current.Level == Mastery.LevelFor(55), pp?.Current?.Level.ToString());
        int pts = riley.RewardPoints;
        pp.Submit(pp.Current.Answer); Pump();
        int s1 = riley.MasteryScore("ratio");
        Check("a right answer raises the score and moves on", s1 == Mastery.Next(55, true) && pp.Answered == 1 && !pp.Learning && riley.RewardPoints == pts + 1);
        Snap("mastery-practice");
        // scratch paper: draw a little long division, undo, rub out, and a new question wipes it
        var sp = pp.Scratch;
        Check("scratch paper is there under the question", sp.IsVisible && sp.Strokes == 0);
        void Line(params (double X, double Y)[] pts) { sp.Begin(new Avalonia.Point(pts[0].X, pts[0].Y)); foreach (var q in pts.Skip(1)) sp.Extend(new Avalonia.Point(q.X, q.Y)); sp.End(); }
        Line((60, 40), (60, 120)); Line((60, 40), (220, 40)); Line((30, 70), (40, 60), (40, 110)); Line((80, 70), (110, 70), (80, 110), (110, 110)); Line((140, 70), (165, 70), (150, 110));
        Pump(); Snap("mastery-scratch");
        Check("drawing on the scratch paper", sp.Strokes == 5);
        sp.Undo(); Check("Undo takes back the last line", sp.Strokes == 4);
        sp.UseEraser(true); sp.Begin(new Avalonia.Point(31, 70)); sp.End(); sp.UseEraser(false);
        Check("the eraser rubs out what it touches", sp.Strokes == 3);
        var asked = pp.Current;
        pp.Submit("987654"); Pump();
        Check("a wrong answer: Not quite, the answer and the worked steps of that question", pp.Learning && pp.ShownSteps.Length >= 2 && pp.ShownSteps[^1].Contains(asked.Answer) && riley.MasteryScore("ratio") == Mastery.Next(s1, false));
        string learnText = string.Join(" ", pp.View.GetVisualDescendantsText());
        Check("the explanation says Not quite and Learn from it", learnText.Contains("Not quite") && learnText.Contains("Learn from it") && learnText.Contains("The answer is " + asked.Answer));
        Check("the mistake is kept for the parent view", riley.Mistakes.Last().Given == "987654");
        Snap("mastery-learn");
        pp.Submit(asked.Answer);
        Check("answers wait while the explanation shows", pp.Answered == 2);
        pp.GotIt(); Pump();
        Check("Got it: the next question", !pp.Learning && pp.Current != asked);
        Check("a new question starts with clean scratch paper", pp.Scratch.Strokes == 0);
        // keep answering right until 100
        int guard = 0;
        while (!pp.ShowingMastered && guard++ < 80) pp.Submit(pp.Current.Answer);
        Pump();
        Check("right answers reach 100 and celebrate", pp.ShowingMastered && riley.MasteryScore("ratio") == 100 && riley.MasteryIfAny("ratio").MasteredOn == Profiles.Today, guard + " answers");
        Check("mastering gives bonus reward points", riley.RewardPoints >= pts + MasteryPracticePage.MasteryReward);
        Snap("mastery-mastered");
        pp.KeepPracticing(); Pump();
        Check("Keep practicing: another question", !pp.ShowingMastered && pp.Current != null);
        pp.Submit("987654"); Pump();
        Check("a miss after mastering keeps 100", riley.MasteryScore("ratio") == 100 && pp.Learning);
        pp.GotIt();
        mv.Back(); Pump();
        Check("back to the map, which shows the new score", mv.Page is SkillMapPage && string.Join(" ", mv.Page.View.GetVisualDescendantsText()).Contains("2 of " + six.Length + " skills mastered"));

        // a choice question with a picture for a 2nd grader
        Lg.Me = sam;
        Problems.Seed(77);
        MasteryPracticePage.Open("money"); Pump();
        pp = (MasteryPracticePage)mv.Page;
        Snap("mastery-practice-picture");
        pp.Submit("1¢"); Pump();
        if (!pp.Learning) { pp.Submit("2¢"); Pump(); }
        Check("a 2nd grader's wrong money answer explains it with the coins", pp.Learning && (pp.ShownSteps.Any(l => l.Contains("¢ + ")) || pp.ShownSteps.Any(l => l.Contains("100¢"))), string.Join(" | ", pp.ShownSteps));
        Snap("mastery-learn-picture");
        MasteryPracticePage.Open("compare"); Pump();
        pp = (MasteryPracticePage)mv.Page;
        string wrong = pp.Current.Choices.First(c => c != pp.Current.Answer);
        pp.Submit(wrong); Pump();
        Check("a choice question: the wrong choice explains place by place", pp.Learning && pp.ShownSteps[^1].StartsWith("So "), string.Join(" | ", pp.ShownSteps));
        Snap("mastery-learn-choice");
        mv.Back(); mv.Back();

        // phone-sized
        win.Width = 400; win.Height = 840;
        Lg.Me = riley;
        Pump();
        mv.BackTo<CategoryPage>(); Pump(); Snap("phone-math-subject");
        var map2 = new SkillMapPage(); mv.Push(map2); Pump(); Snap("phone-mastery-map");
        Problems.Seed(31);
        MasteryPracticePage.Open("percent"); Pump(); Snap("phone-mastery-practice");
        pp = (MasteryPracticePage)mv.Page;
        pp.Submit("987654"); Pump(); Snap("phone-mastery-learn");
        pp.GotIt();
        guard = 0; while (!pp.ShowingMastered && guard++ < 80) pp.Submit(pp.Current.Answer);
        Pump(); Snap("phone-mastery-mastered");
        Lg.Me = sam;
        Problems.Seed(78);
        MasteryPracticePage.Open("money"); Pump(); Snap("phone-mastery-picture");
        pp = (MasteryPracticePage)mv.Page; pp.Submit("1¢"); Pump(); Snap("phone-mastery-learn-picture");
        Check("everything was saved", File.ReadAllText(f).Contains("\"MasteredOn\"") && Profiles.State == ProfilesState.Ok);
        win.Close();
        Lg.Reset();
    }

    /// <summary>The text of every TextBlock under a control.</summary>
    static IEnumerable<string> GetVisualDescendantsText(this Control c)
        => Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(c).OfType<TextBlock>().Select(t => t.Text ?? "");
}
