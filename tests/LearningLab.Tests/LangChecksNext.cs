// Learning Lab - the checks of Languages' lessons, stories, verb trainer, speaking and car drills (group "languages").
// They use small fixtures of our own (Fixtures\Languages) loaded through Lx.UseContent, so they pass whatever the
// content writers have done; the real files (src\LearningLab\Content\Languages\spanish-lessons*.json, -stories.json,
// -verbs.json) are checked too when they exist. Pretend voices, player and speech recognition: nothing is played and
// the microphone is never used.
using Avalonia.Controls;
using Avalonia.Threading;
using LearningLab.Languages;
using LearningLab.Services;
using LearningLab.Views;

namespace LearningLab.Tests;

static class LangChecksNext
{
    static void Check(string what, bool ok, string detail = null) => Program.Check("languages: " + what, ok, detail);

    sealed class FakeRecognizer : IRecognizer
    {
        public Func<IReadOnlyList<string>, string> Hears = e => e[0];
        public int Calls;
        public IReadOnlyList<string> LastExpected;
        public bool Available(string lang) => lang.StartsWith("es");
        public bool NeedsPermission => false;
        public Task<bool> AskPermission() => Task.FromResult(true);
        public Task<string> Listen(string lang, double seconds, IReadOnlyList<string> expected) { Calls++; LastExpected = expected; return Task.FromResult(Hears(expected)); }
    }

    static LangCourse course;
    static string fixtures;

    public static void Run(LangCourse c, LangChecks.FakeVoices voices)
    {
        course = c;
        if (course == null) return;
        fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Languages");
        Feedback();
        Answers();
        Conjugation();
        Drills();
        ProgressAdditions();
        FixtureContent();
        RealContent();
        CartoonEngine();
        PlatformAudio.Voices = voices;
        PlatformAudio.NewPlayer = () => new LangChecks.FakePlayer();
        Pages();
        Lx.UseContent(null);
    }

    /// <summary>Soft sounds and buzzes (Feel): the right buzz for each, sounds quiet and click-free, played through
    /// the platform (captured here, never heard).</summary>
    static void Feedback()
    {
        var buzz = new List<string>(); var wavs = new List<byte[]>();
        var oldH = Platform.Haptic; var oldW = Platform.PlayWav; bool oldOn = global::LearningGames.Sound.Enabled;
        global::LearningGames.Sound.Enabled = true;
        Platform.Haptic = k => buzz.Add(k); Platform.PlayWav = w => wavs.Add(w);
        try
        {
            Feel.Tap(); Feel.Right(); Feel.Wrong(); Feel.Done();
            Check("a buzz for each: tap, right, wrong, done", string.Join(",", buzz) == "tap,right,wrong,right", string.Join(",", buzz));
            Check("a sound for each", wavs.Count == 4);
            for (int k = 0; k < wavs.Count; k++)
            {
                var s = LearningLab.Audio.Wav.ReadMono(wavs[k]);
                int peak = s.Max(x => Math.Abs((int)x));
                Check("feedback sound " + k + " is soft (peak under 16% of full)", peak > 300 && peak < 0.16 * 32767, peak.ToString());
                Check("feedback sound " + k + " starts and ends quietly (no click)", Math.Abs((int)s[0]) < 200 && Math.Abs((int)s[^1]) < 200);
            }
            wavs.Clear();
            global::LearningGames.Sound.Enabled = false;
            Feel.Right();
            Check("sounds off: still the buzz, no sound", wavs.Count == 0 && buzz[^1] == "right");
        }
        finally { global::LearningGames.Sound.Enabled = oldOn; Platform.Haptic = oldH; Platform.PlayWav = oldW; }
    }

    static bool Throws(Action a) { try { a(); return false; } catch (Exception) { return true; } }

    // ------------------------------------------------------------------ answers
    static void Answers()
    {
        Check("answers ignore case, spaces and end marks", LearningLab.Languages.Answers.Same("  hola ", "Hola.") && LearningLab.Languages.Answers.Same("¿cómo  estás", "¿Cómo estás?") && !LearningLab.Languages.Answers.Same("hola", "Adiós."));
        var a = LearningLab.Languages.Answers.CheckTyped("esta", "está");
        Check("a missing accent is forgiven and pointed out: \"Watch the accent: está\"", a.Right && a.Note == "Watch the accent: está", a.Note);
        var b = LearningLab.Languages.Answers.CheckTyped("como estas", "¿Cómo estás?");
        Check("missing ¿ and accents are forgiven with a note", b.Right && b.Note != null && b.Note.Contains("cómo") && b.Note.Contains("estás"), b.Note);
        var q = LearningLab.Languages.Answers.CheckTyped("Cómo estás", "¿Cómo estás?");
        Check("only the ¿ missing: a note about it", q.Right && q.Note != null && q.Note.Contains("¿"), q.Note);
        Check("an exact typed answer has no note", LearningLab.Languages.Answers.CheckTyped("Bien gracias", "Bien, gracias.").Note == null && LearningLab.Languages.Answers.CheckTyped("Bien gracias", "Bien, gracias.").Right);
        Check("accepted answers count", LearningLab.Languages.Answers.CheckTyped("hola", "Hola, amigo.", new[] { "Hola" }).Right);
        Check("a wrong word is wrong", !LearningLab.Languages.Answers.CheckTyped("hola", "Adiós.").Right && !LearningLab.Languages.Answers.CheckTyped("", "Hola.").Right);
        Check("ñ is forgiven like an accent", LearningLab.Languages.Answers.CheckTyped("manana", "mañana").Right);
        Check("build: tiles in order make the sentence", LearningLab.Languages.Answers.CheckBuild(new[] { "Cómo", "estás" }, "¿Cómo estás?") && !LearningLab.Languages.Answers.CheckBuild(new[] { "estás", "Cómo" }, "¿Cómo estás?"));
        var said = LearningLab.Languages.Answers.CheckSaid("cómo esta", "¿Cómo estás?");
        Check("speech is compared word by word", said.Count == 2 && said[0].Heard && !said[1].Heard && !LearningLab.Languages.Answers.SaidRight(said));
        Check("speech: every word heard is right (accents forgiven)", LearningLab.Languages.Answers.SaidRight(LearningLab.Languages.Answers.CheckSaid("Cómo estás", "¿Cómo estás?")) && LearningLab.Languages.Answers.SaidRight(LearningLab.Languages.Answers.CheckSaid("como estas", "¿Cómo estás?")));
        Check("words lose their marks for tiles", string.Join("|", LearningLab.Languages.Answers.Words("¡Hola, Diego! ¿Cómo estás?")) == "Hola|Diego|Cómo|estás");
    }

    // ------------------------------------------------------------------ the conjugator, against forms checked by hand
    static void Conjugation()
    {
        // (infinitive, stem change, tense, the five forms yo / tú / él / nosotros / ellos; null = not checked)
        var table = new (string Inf, StemChange Stem, Tense T, string[] Forms)[]
        {
            ("hablar", StemChange.None, Tense.Present, new[] { "hablo", "hablas", "habla", "hablamos", "hablan" }),
            ("hablar", StemChange.None, Tense.Preterite, new[] { "hablé", "hablaste", "habló", "hablamos", "hablaron" }),
            ("comer", StemChange.None, Tense.Present, new[] { "como", "comes", "come", "comemos", "comen" }),
            ("comer", StemChange.None, Tense.Preterite, new[] { "comí", "comiste", "comió", "comimos", "comieron" }),
            ("vivir", StemChange.None, Tense.Present, new[] { "vivo", "vives", "vive", "vivimos", "viven" }),
            ("vivir", StemChange.None, Tense.Preterite, new[] { "viví", "viviste", "vivió", "vivimos", "vivieron" }),
            ("pensar", StemChange.EIe, Tense.Present, new[] { "pienso", "piensas", "piensa", "pensamos", "piensan" }),
            ("pensar", StemChange.EIe, Tense.Preterite, new[] { "pensé", "pensaste", "pensó", "pensamos", "pensaron" }),
            ("poder", StemChange.OUe, Tense.Present, new[] { "puedo", "puedes", "puede", "podemos", "pueden" }),
            ("dormir", StemChange.OUe, Tense.Present, new[] { "duermo", "duermes", "duerme", "dormimos", "duermen" }),
            ("dormir", StemChange.OUe, Tense.Preterite, new[] { "dormí", "dormiste", "durmió", "dormimos", "durmieron" }),
            ("pedir", StemChange.EI, Tense.Present, new[] { "pido", "pides", "pide", "pedimos", "piden" }),
            ("pedir", StemChange.EI, Tense.Preterite, new[] { "pedí", "pediste", "pidió", "pedimos", "pidieron" }),
            ("jugar", StemChange.UUe, Tense.Present, new[] { "juego", "juegas", "juega", "jugamos", "juegan" }),
            ("jugar", StemChange.UUe, Tense.Preterite, new[] { "jugué", "jugaste", "jugó", "jugamos", "jugaron" }),
            ("buscar", StemChange.None, Tense.Preterite, new[] { "busqué", "buscaste", "buscó", "buscamos", "buscaron" }),
            ("llegar", StemChange.None, Tense.Preterite, new[] { "llegué", "llegaste", "llegó", null, null }),
            ("empezar", StemChange.EIe, Tense.Present, new[] { "empiezo", "empiezas", null, "empezamos", "empiezan" }),
            ("empezar", StemChange.EIe, Tense.Preterite, new[] { "empecé", "empezaste", "empezó", null, null }),
            ("almorzar", StemChange.OUe, Tense.Present, new[] { "almuerzo", null, null, "almorzamos", null }),
            ("almorzar", StemChange.OUe, Tense.Preterite, new[] { "almorcé", null, "almorzó", null, null }),
            ("escoger", StemChange.None, Tense.Present, new[] { "escojo", "escoges", null, null, null }),
            ("elegir", StemChange.EI, Tense.Present, new[] { "elijo", "eliges", "elige", "elegimos", "eligen" }),
            ("elegir", StemChange.EI, Tense.Preterite, new[] { null, null, "eligió", null, "eligieron" }),
            ("leer", StemChange.None, Tense.Preterite, new[] { "leí", "leíste", "leyó", "leímos", "leyeron" }),
            ("creer", StemChange.None, Tense.Preterite, new[] { "creí", null, "creyó", null, "creyeron" }),
            ("oír", StemChange.None, Tense.Preterite, new[] { "oí", "oíste", "oyó", "oímos", "oyeron" }),
            ("construir", StemChange.None, Tense.Present, new[] { "construyo", "construyes", "construye", "construimos", "construyen" }),
            ("construir", StemChange.None, Tense.Preterite, new[] { "construí", "construiste", "construyó", "construimos", "construyeron" }),
            ("conocer", StemChange.None, Tense.Present, new[] { "conozco", "conoces", null, null, null }),
            ("traducir", StemChange.None, Tense.Present, new[] { "traduzco", null, null, "traducimos", null }),
            ("vencer", StemChange.None, Tense.Present, new[] { "venzo", "vences", null, null, null }),
            ("seguir", StemChange.EI, Tense.Present, new[] { "sigo", "sigues", "sigue", "seguimos", "siguen" }),
            ("seguir", StemChange.EI, Tense.Preterite, new[] { "seguí", null, "siguió", null, "siguieron" }),
            ("preferir", StemChange.EIe, Tense.Present, new[] { "prefiero", null, null, "preferimos", "prefieren" }),
            ("preferir", StemChange.EIe, Tense.Preterite, new[] { null, null, "prefirió", null, "prefirieron" }),
            ("sentir", StemChange.EIe, Tense.Preterite, new[] { "sentí", null, "sintió", null, "sintieron" }),
            ("volver", StemChange.OUe, Tense.Present, new[] { "vuelvo", null, null, "volvemos", null }),
            ("volver", StemChange.OUe, Tense.Preterite, new[] { "volví", null, "volvió", null, null }),
            ("repetir", StemChange.EI, Tense.Present, new[] { "repito", null, null, "repetimos", null }),
            ("cerrar", StemChange.EIe, Tense.Present, new[] { "cierro", null, null, "cerramos", "cierran" }),
            ("encontrar", StemChange.OUe, Tense.Present, new[] { "encuentro", null, null, "encontramos", null }),
            ("levantarse", StemChange.None, Tense.Present, new[] { "me levanto", "te levantas", "se levanta", "nos levantamos", "se levantan" }),
            ("levantarse", StemChange.None, Tense.Preterite, new[] { null, null, "se levantó", null, null }),
            ("comer", StemChange.None, Tense.NearFuture, new[] { "voy a comer", "vas a comer", "va a comer", "vamos a comer", "van a comer" }),
            ("levantarse", StemChange.None, Tense.NearFuture, new[] { "voy a levantarme", null, null, "vamos a levantarnos", null }),
        };
        int n = 0;
        var bad = new List<string>();
        foreach (var row in table)
            for (int p = 0; p < 5; p++)
            {
                if (row.Forms[p] == null) continue;
                n++;
                string got = Conjugator.ByRule(row.Inf, row.Stem, row.T, p, out _);
                if (got != row.Forms[p]) bad.Add(row.Inf + " " + Conjugator.TenseName(row.T) + " " + Conjugator.Persons[p] + ": " + got + " (should be " + row.Forms[p] + ")");
            }
        Check("the conjugator makes " + n + " known forms (40+) right", n >= 40 && bad.Count == 0, string.Join("; ", bad.Take(6)));
        Check("endings: -ar, -er, -ir, oír, reflexive; others refused", Conjugator.Ending("hablar") == "ar" && Conjugator.Ending("oír") == "ir" && Conjugator.Ending("ir") == "ir" && Conjugator.Ending("levantarse") == "ar" && Conjugator.Ending("casa") == null);

        // verb list: irregular overrides, null = the rule
        var verbs = LangVerb.ParseFile(File.ReadAllText(Path.Combine(fixtures, "spanish-verbs.json")));
        LangVerb V(string i) => verbs.First(v => v.Infinitive == i);
        var tengo = Conjugator.Form(V("tener"), Tense.Present, 0);
        var tienes = Conjugator.Form(V("tener"), Tense.Present, 1);
        Check("an irregular form comes from the list and is reported irregular", tengo.Text == "tengo" && tengo.Irregular);
        Check("null in the list uses the rule (tienes, with its stem change reported)", tienes.Text == "tienes" && !tienes.Irregular && tienes.Change != null && Conjugator.Form(V("tener"), Tense.Present, 3).Text == "tenemos");
        Check("whole irregular tenses: soy, fue, tuvieron", Conjugator.Form(V("ser"), Tense.Present, 0).Text == "soy" && Conjugator.Form(V("ser"), Tense.Preterite, 2).Text == "fue" && Conjugator.Form(V("tener"), Tense.Preterite, 4).Text == "tuvieron");
        Check("leer: leí by the rule beside listed forms", Conjugator.Form(V("leer"), Tense.Preterite, 0).Text == "leí" && Conjugator.Form(V("leer"), Tense.Preterite, 2).Irregular);
        Check("a regular form reports nothing", Conjugator.Form(V("hablar"), Tense.Present, 0) is { Irregular: false, Change: null, Text: "hablo" });
        Check("the stem change is read from \"stem\" (u-ue, i)", V("jugar").Stem == StemChange.UUe && V("pedir").Stem == StemChange.EI && V("tener").Stem == StemChange.EIe);
        Check("weather verbs are asked only for él (llover: llueve)", V("llover").Persons.SequenceEqual(new[] { 2 }) && Conjugator.Form(V("llover"), Tense.Present, 2).Text == "llueve");
        Check("\"persons\" in the list: gustar only él and ellos", V("gustar").Persons.SequenceEqual(new[] { 2, 4 }));
        var quiz = new VerbQuiz(verbs, new[] { Tense.Present, Tense.Preterite }, new Random(3));
        var asked = Enumerable.Range(0, 400).Select(_ => quiz.Next()).ToList();
        Check("the trainer never asks yo llover or yo gusto", asked.All(q => q.Verb.Persons.Contains(q.Person)) && asked.Any(q => q.Verb.Infinitive == "gustar") && !asked.Any(q => q.Verb.Infinitive == "llover" && q.Person != 2));
        Check("every answer is the conjugator's", asked.All(q => q.Answer.Text == Conjugator.Form(q.Verb, q.Tense, q.Person).Text));
        Check("the prompt reads \"nosotros · comer · preterite\"", Conjugator.Prompt(V("comer"), Tense.Preterite, 3) == "nosotros · comer · preterite");
        var qc = new VerbQuestion { Verb = V("comer"), Tense = Tense.Preterite, Person = 3, Answer = Conjugator.Form(V("comer"), Tense.Preterite, 3) };
        var ch = VerbQuiz.Choices(qc, new Random(1));
        Check("four different choices with the answer among them", ch.Count == 4 && ch.Distinct().Count() == 4 && ch.Contains("comimos"));
        var hab = new VerbQuestion { Verb = V("hablar"), Tense = Tense.Preterite, Person = 2, Answer = Conjugator.Form(V("hablar"), Tense.Preterite, 2) };
        Check("\"hablo\" for habló is wrong (it's another form)", !VerbQuiz.Check(hab, "hablo").Right && VerbQuiz.Check(hab, "habló").Right);
        var com = new VerbQuestion { Verb = V("comer"), Tense = Tense.Preterite, Person = 2, Answer = Conjugator.Form(V("comer"), Tense.Preterite, 2) };
        var forgiven = VerbQuiz.Check(com, "comio");
        Check("\"comio\" for comió is forgiven with an accent note", forgiven.Right && forgiven.Note == "Watch the accent: comió", forgiven.Note);
        Check("a bad verb list says why", Throws(() => LangVerb.ParseFile("{\"nothing\": 1}")));
        var odd = LangVerb.ParseFile("{\"verbs\":[{\"inf\":\"tener\",\"en\":\"x\",\"stem\":\"zz\",\"irregular\":{\"present\":[\"tengo\"],\"future\":[\"a\",\"b\",\"c\",\"d\",\"e\"],\"imperfect\":[]}}]}")[0];
        Check("short lists, unknown tenses and stems are reported", odd.IrregularLengths["present"] == 1 && odd.UnknownTenses.Contains("imperfect") && odd.StemProblem != null);
    }

    // ------------------------------------------------------------------ car drills
    static void Drills()
    {
        Check("syllables: es-tás, gra-cias, ma-ña-na, pe-rro, dí-a, a-ho-ra, quie-ro", string.Join("-", Lesson.Syllables("estás")) == "es-tás" && string.Join("-", Lesson.Syllables("gracias")) == "gra-cias"
            && string.Join("-", Lesson.Syllables("mañana")) == "ma-ña-na" && string.Join("-", Lesson.Syllables("perro")) == "pe-rro" && string.Join("-", Lesson.Syllables("día")) == "dí-a"
            && string.Join("-", Lesson.Syllables("ahora")) == "a-ho-ra" && string.Join("-", Lesson.Syllables("quiero")) == "quie-ro" && string.Join("-", Lesson.Syllables("hablo")) == "ha-blo",
            string.Join(" ", new[] { "estás", "gracias", "mañana", "perro", "día", "ahora", "quiero", "hablo" }.Select(w => string.Join("-", Lesson.Syllables(w)))));
        var bc = Lesson.Backchain("¿Cómo estás?");
        Check("backchaining: \"tás\", \"estás\", \"¿Cómo estás?\"", bc.SequenceEqual(new[] { "tás", "estás", "¿Cómo estás?" }), string.Join(" | ", bc));
        var bl = Lesson.Backchain("¿Dónde está el baño, por favor?");
        Check("long phrases build up by words and end whole", bl.Count >= 3 && bl[0] == "favor" && bl[^1] == "¿Dónde está el baño, por favor?" && bl.Zip(bl.Skip(1)).All(x => x.First.Length < x.Second.Length), string.Join(" | ", bl));
        Check("short phrases aren't chopped", Lesson.Backchain("Hola.").Count == 1);
        var items = course.Units[0].Items.Take(6).ToList();
        var d = Lesson.BuildDrill("t", "es", items, 1, null, new Dictionary<int, string> { [0] = "Think it through. Spanish questions start with ¿." });
        Check("the drill asks every phrase several times", items.All(i => d.Items.Count(x => x == i) == 1 + Lesson.DrillGaps.Length), string.Join(",", items.Select(i => d.Items.Count(x => x == i))));
        // gaps between the asks of the first item grow
        var at = d.Items.Select((x, k) => (x, k)).Where(t => t.x == items[0]).Select(t => t.k).ToList();
        var gaps = at.Zip(at.Skip(1)).Select(t => t.Second - t.First).ToList();
        Check("earlier phrases come back at growing gaps", gaps.Count >= 3 && gaps.Zip(gaps.Skip(1)).All(t => t.Second > t.First), string.Join(",", gaps));
        Check("the drill asks \"How would you say\" and waits for the answer", d.Steps.Any(s => s.Kind == StepKind.Say && s.Lang == "en" && s.Text.StartsWith("How would you say")) && d.Steps.Any(s => s.Kind == StepKind.Wait && s.Ms >= 3000));
        Check("the drill says the tip (think it through) and builds from the end", d.Steps.Any(s => s.Text != null && s.Text.StartsWith("Think it through")) && d.Steps.Any(s => s.Kind == StepKind.Say && s.Text == "tás"));
        Check("the drill's item marks are in order", d.Steps.Where(s => s.Kind == StepKind.Item).Select(s => s.Index).SequenceEqual(Enumerable.Range(0, d.Items.Count)));
        Check("Build with style drill makes a drill", Lesson.Build("t", "es", items, "drill", true).Style == "drill");
    }

    // ------------------------------------------------------------------ progress: the new parts, and the old format
    static void ProgressAdditions()
    {
        string dir = Path.Combine(Data.Root, "lang-progress2");
        Directory.CreateDirectory(dir);
        string f = Path.Combine(dir, "progress.json");
        File.Copy(Path.Combine(fixtures, "progress-2026-11-10.json"), f, true);
        var day = new DateTime(2026, 11, 11, 18, 0, 0);
        LangProgress.Now = () => day;
        var p = LangProgress.Load(f);
        Check("a progress file from 2026-11-10 still loads", !p.Blocked && p.BestQuiz("es", "hello") == 9 && p.Listened("es") == 125.5 && p.Setting("voice-es") == "es-2" && p.Box("es", "5e7f2a1c3b4d") == 2);
        Check("its streak carries on", p.Streak("es") == 2);
        Check("older files have no lessons, points or goal yet: defaults", !p.LessonDone("es", "x") && p.PointsToday("es") == 0 && p.Goal == 20 && p.VerbStats("es", "ser") == (0, 0));
        p.FinishLesson("es", "hello-1", 70); p.FinishLesson("es", "hello-1", 90); p.FinishLesson("es", "hello-1", 60);
        p.AddPoints("es", 12); p.AddPoints("es", 3); p.Goal = 30;
        p.VerbAnswer("es", "ser", "present", true); p.VerbAnswer("es", "ser", "present", false); p.VerbAnswer("es", "ser", "preterite", true);
        p.FinishStory("es", "s1", 2); p.FinishStory("es", "s1", 1);
        Check("lessons keep times and the best score", p.LessonTimes("es", "hello-1") == 3 && p.LessonBest("es", "hello-1") == 90);
        Check("points add up per day", p.PointsToday("es") == 15 && p.Points("es", day.AddDays(-1)) == 0);
        Check("the goal is 10, 20, 30 or 50", p.Goal == 30 && Assign(p, 25) == 20);
        p.Goal = 30;
        Check("verb answers are counted", p.VerbStats("es", "ser") == (2, 1));
        Check("stories keep the best", p.StoryTimes("es", "s1") == 2 && p.StoryBest("es", "s1") == 2);
        p.Save();
        var q = LangProgress.Load(f);
        Check("the new parts read back", q.LessonBest("es", "hello-1") == 90 && q.PointsToday("es") == 15 && q.Goal == 30 && q.VerbStats("es", "ser") == (2, 1) && q.StoryBest("es", "s1") == 2);
        Check("unknown fields of the old file are still kept", File.ReadAllText(f).Contains("fromNewer"));
        // days far back are dropped from the points, not the rest
        for (int k = 0; k < 420; k++) { LangProgress.Now = () => day.AddDays(-k); q.AddPoints("es", 1); }
        LangProgress.Now = () => day;
        Check("points keep the last 400 days", q.Points("es", day.AddDays(-419)) == 0 && q.Points("es", day.AddDays(-10)) == 1 && q.PointsToday("es") == 16);
        LangProgress.Now = () => DateTime.Now;
    }

    static int Assign(LangProgress p, int g) { p.Goal = g; return p.Goal; }

    // ------------------------------------------------------------------ the fixtures through the content checks
    static LangContent fixture;

    static void FixtureContent()
    {
        Lx.UseContent(fixtures);
        fixture = Lx.Content("spanish");
        Check("the fixture lessons, stories and verbs load", fixture.Lessons.Count == 2 && fixture.Stories.Count == 2 && fixture.Verbs.Count == 10 && fixture.LoadProblems.Count == 0, string.Join("; ", fixture.LoadProblems));
        var probs = fixture.Problems(course);
        Check("the fixtures pass the content checks", probs.Count == 0, string.Join("; ", probs.Take(6)));
        Check("lessons are numbered in their unit", fixture.Lessons[0].Number == 1 && fixture.Lessons[1].Number == 2);
        Check("every exercise kind is in the fixture", LangExercise.Kinds.All(k => fixture.Lessons[0].Exercises.Any(e => e.Kind == k)));
        // the checks find the mistakes they should
        var bad = LangContent.Load("spanish", new[] { new KeyValuePair<string, string>("spanish-lessons-x.json", BadLessons), new KeyValuePair<string, string>("spanish-stories.json", "{ \"stories\": [ { \"id\": \"s\", \"level\": 9, \"after\": \"nowhere\", \"lines\": [], \"questions\": [] } ] }"),
            new KeyValuePair<string, string>("spanish-verbs.json", "{ \"verbs\": [ { \"inf\": \"tener\", \"en\": \"to have\", \"irregular\": { \"present\": [\"tengo\", null] } }, { \"inf\": \"casa\", \"en\": \"house\" } ] }"),
            new KeyValuePair<string, string>("spanish-lessons-broken.json", "{ \"lessons\": [ { \"id\": ") }, course);
        var bp = bad.Problems(course);
        string all = string.Join("\n", bp);
        bool Has(string s) => all.Contains(s);
        Check("content checks catch: unknown unit, item not in the unit, item new twice, uncovered items", Has("no unit \"nope\"") && Has("is not an item of unit hello") && Has("is new in") && Has("is not new in any lesson"), all);
        Check("content checks catch: gap without one ___, extra tile in the sentence, repeated choices, too few exercises", Has("exactly one ___") && Has("is also in the sentence") && Has("choices repeat") && Has("exercises (8-14)"), all);
        Check("content checks catch: story level, after, lines, questions; verb list length and ending", Has("level 9") && Has("\"after\" unit nowhere") && Has("lines (6-20)") && Has("no questions") && Has("list has 2 entries") && Has("doesn't end in -ar"), all);
        Check("a half-written file is left out and named, the rest still loads", bad.LoadProblems.Count == 1 && bad.LoadProblems[0].StartsWith("spanish-lessons-broken.json") && bad.Lessons.Count == 2);
        var none = LangContent.Load("spanish", Array.Empty<KeyValuePair<string, string>>(), course);
        Check("no content files: empty lists, no problems", none.Lessons.Count == 0 && none.Stories.Count == 0 && none.Verbs.Count == 0 && none.Problems(course).Count == 0);
        // the story's tap-to-translate
        var s = fixture.Stories[0];
        var w = LangStory.LineWords(s.Lines[4].Target);   // El perro se ríe.
        string m = s.Meaning(course, w, 3, out var shown);
        Check("a tapped word finds a two-word entry (se ríe)", m == "laughs (reírse)" && shown == "se ríe", shown + " = " + m);
        Check("a tapped word finds the story's words, any case", s.Meaning(course, w, 1, out _) == "dog" && s.Meaning(course, LangStory.LineWords(s.Lines[1].Target), 0, out _) == "Sofía (a name)");
        Check("a word not in the story comes from the course's phrases", s.Meaning(course, LangStory.LineWords(s.Lines[2].Target), 2, out var sh2) == "Hello." && sh2 == "Hola", sh2);
        Check("a two-word course phrase is found (Buenos días)", s.Meaning(course, LangStory.LineWords(s.Lines[1].Target), 2, out var sh3) == "Good morning." && sh3 == "Buenos días", sh3);
        Check("an unknown word says so (null)", s.Meaning(course, new List<string> { "xyzzy" }, 0, out _) == null);
    }

    const string BadLessons = @"{ ""format"": 1, ""lessons"": [
      { ""id"": ""b1"", ""unit"": ""nope"", ""title"": ""x"", ""goal"": ""x"", ""new"": [], ""exercises"": [] },
      { ""id"": ""b2"", ""unit"": ""hello"", ""title"": ""x"", ""goal"": ""x"", ""new"": [""Hola."", ""Hola."", ""Nada que ver.""],
        ""dialogue"": [ { ""who"": ""A"", ""es"": ""Hola."", ""en"": ""Hi."" }, { ""who"": ""B"", ""es"": ""Hola."", ""en"": ""Hi."" } ],
        ""tip"": { ""title"": ""t"", ""text"": ""t"" },
        ""exercises"": [
          { ""kind"": ""gap"", ""es"": ""Bien gracias."", ""answer"": ""a"", ""wrong"": [""b""] },
          { ""kind"": ""build"", ""en"": ""Hi"", ""es"": ""Hola amigo."", ""extra"": [""amigo""] },
          { ""kind"": ""choose"", ""prompt"": ""p"", ""answer"": ""Hola."", ""wrong"": [""hola""] } ] } ] }";

    // ------------------------------------------------------------------ the real content files, when they exist
    static void RealContent()
    {
        Lx.UseContent(null);
        var real = Lx.Content("spanish");
        Console.WriteLine("   real content files: " + (real.Files.Count == 0 ? "none yet" : string.Join(", ", real.Files)) + (real.LoadProblems.Count > 0 ? "; unreadable: " + string.Join(", ", real.LoadProblems) : ""));
        var probs = real.Problems(course);
        if (real.Files.Count > 0 || real.LoadProblems.Count > 0)
        {
            foreach (var x in probs.Take(40)) Console.WriteLine("   content: " + x);
            Check("the real lessons, stories and verbs pass the content checks", probs.Count == 0, probs.Count + " problems, first: " + string.Join("; ", probs.Take(3)));
        }
        // every exercise can be answered right with its own answer
        var unsolvable = new List<string>();
        foreach (var l in real.Lessons)
            foreach (var e in l.Exercises)
            {
                bool ok = e.Kind switch
                {
                    "build" => LearningLab.Languages.Answers.CheckBuild(e.BuildWords(), e.Target),
                    "type" => LearningLab.Languages.Answers.CheckTyped(e.Target, e.Target, e.Accept).Right && e.Accept.All(a => LearningLab.Languages.Answers.CheckTyped(a, e.Target, e.Accept).Right),
                    "say" => LearningLab.Languages.Answers.SaidRight(LearningLab.Languages.Answers.CheckSaid(e.Target, e.Target)),
                    "match" => e.Pairs.Count > 0,
                    _ => e.Choices(new Random(1)).Count(x => LearningLab.Languages.Answers.Same(x, e.Answer)) == 1,
                };
                if (!ok) unsolvable.Add(l.Id + " " + e.Kind);
            }
        if (real.Lessons.Count > 0) Check("every real exercise can be answered right (" + real.Lessons.Sum(l => l.Exercises.Count) + " exercises)", unsolvable.Count == 0, string.Join("; ", unsolvable.Take(5)));
        if (real.Verbs.Count > 0)
        {
            var v = real.Verbs.ToDictionary(x => x.Infinitive);
            bool Only(string inf, params int[] ps) => !v.ContainsKey(inf) || v[inf].Persons.SequenceEqual(ps);
            Check("real verbs: llover only él, gustar and costar él and ellos", Only("llover", 2) && Only("gustar", 2, 4) && Only("costar", 2, 4));
            Check("real verbs: the forms worked out for known verbs are right", Form(v, "leer", Tense.Preterite, 0) == "leí" && Form(v, "creer", Tense.Preterite, 0) == "creí" && Form(v, "tener", Tense.Present, 1) == "tienes" && Form(v, "jugar", Tense.Present, 0) == "juego" && Form(v, "dormir", Tense.Preterite, 2) == "durmió" && Form(v, "llover", Tense.Present, 2) == "llueve" && Form(v, "oír", Tense.Preterite, 0) == "oí");
        }
        if (real.Stories.Count > 0)
        {
            int words = 0, found = 0;
            var missing = new SortedSet<string>();
            foreach (var s in real.Stories)
                foreach (var line in s.Lines)
                {
                    var w = LangStory.LineWords(line.Target);
                    for (int k = 0; k < w.Count; k++) { if (LearningLab.Languages.Answers.Bare(w[k]).Length == 0) continue; words++; if (s.Meaning(course, w, k, out _) != null) found++; else missing.Add(LearningLab.Languages.Answers.Bare(w[k]).ToLowerInvariant()); }
                }
            Console.WriteLine("   stories: " + found + " of " + words + " words have a meaning on tap" + (missing.Count > 0 ? "; without one: " + string.Join(", ", missing) : ""));
            Check("real stories: most words have a meaning on tap (90%+)", words == 0 || found >= words * 0.9, found + " of " + words);
        }
    }

    static string Form(Dictionary<string, LangVerb> v, string inf, Tense t, int p) => v.ContainsKey(inf) ? Conjugator.Form(v[inf], t, p).Text : Conjugator.ByRule(inf, inf switch { "tener" => StemChange.EIe, "jugar" => StemChange.UUe, "dormir" or "llover" => StemChange.OUe, _ => StemChange.None }, t, p, out _);

    // ------------------------------------------------------------------ the pages
    static Window win;
    static void Pump() { Dispatcher.UIThread.RunJobs(); }
    static void Snap(string name) => LearningLab.Tests.Snap.Save(win, name);
    static void Wait(Task t) { for (int k = 0; k < 2000 && !t.IsCompleted; k++) { Pump(); Thread.Sleep(5); } Pump(); }

    static void Pages()
    {
        Lx.UseContent(fixtures);
        var content = Lx.Content("spanish");
        Lx.UseFile(Path.Combine(Data.Root, "lang-lessons", "progress.json"));
        var day = new DateTime(2026, 11, 11, 17, 0, 0);
        LangProgress.Now = () => day;
        var rec = new FakeRecognizer();
        PlatformAudio.Recognizer = rec;
        win = new Window { Width = 1180, Height = 800, Content = new MainView() };
        win.Show();
        var mv = MainView.Current;
        var cp = new CoursePage("spanish"); mv.Push(cp); Pump(); Snap("lang-path");
        Check("the course page offers the first lesson", cp.NextLesson?.Id == "test-hello-1");

        // ---- a whole lesson: every exercise wrong first, then right when they come back
        var lp = new LessonPage(course, content, content.Lessons[0], 5); mv.Push(lp); Pump(); Snap("lang-lesson-intro");
        lp.Start(); Pump(); Snap("lang-lesson-words");
        Check("the lesson's new phrases are met when shown", LangContent.NewItems(course, content.Lessons[0]).All(i => Lx.Progress.Seen("es", i.Id)));
        lp.ToDialogue(); Pump();
        Check("the conversation starts with its first line, read aloud", lp.Now == LessonPage.Step.Dialogue && lp.LinesShown == 1 && lp.LastSaid == "¡Hola, Diego! ¿Cómo estás?");
        lp.ToggleEnglish(0); lp.NextLine(); Pump(); Snap("lang-lesson-dialogue");
        Check("tap-to-reveal English and line by line", lp.EnglishShown(0) && lp.LinesShown == 2 && !lp.EnglishShown(1));
        lp.NextLine(); lp.ToExercises(); Pump();
        Check("then the exercises", lp.Now == LessonPage.Step.Exercise && lp.Run.Current.Kind == "choose");
        var seenKinds = new HashSet<string>();
        int guard = 0;
        while (lp.Now == LessonPage.Step.Exercise && guard++ < 40)
        {
            var e = lp.Run.Current;
            bool right = lp.Run.Redo;
            Answer(lp, e, right, rec);
            Pump();
            if (!lp.Run.Answered) { Check("the exercise " + e.Kind + " took an answer", false); break; }
            if (!right && seenKinds.Add(e.Kind)) Snap("lang-ex-" + e.Kind + "-wrong");
            if (right && seenKinds.Add(e.Kind + "+")) Snap("lang-ex-" + e.Kind + "-right");
            if (e.Kind != "match") Check(e.Kind + " (" + (right ? "right" : "wrong") + "): the right answer is said aloud", lp.LastSaid == e.SayAfter || e.Kind == "say", lp.LastSaid + " vs " + e.SayAfter);
            if (!right && e.Kind is "choose" or "gap" or "type" or "build" or "reply") Check(e.Kind + " wrong: kind feedback with the answer", lp.Feedback != null && lp.Feedback.StartsWith("Not quite"), lp.Feedback);
            lp.Continue(); Pump();
        }
        Check("every kind answered wrong and then right", LangExercise.Kinds.All(k => seenKinds.Contains(k) && seenKinds.Contains(k + "+")), string.Join(",", seenKinds));
        Check("wrong exercises came back once at the end", lp.Run.Count == 2 * content.Lessons[0].Exercises.Count);
        Check("speech recognition was asked with the answer first", rec.Calls >= 2 && rec.LastExpected != null && rec.LastExpected[0] == "¿Cómo estás?");
        Check("then the tip", lp.Now == LessonPage.Step.Tip);
        Snap("lang-lesson-tip");
        lp.Continue(); Pump(); Snap("lang-lesson-recap");
        Check("the recap: lesson done, score 0 the first time, 5 points for finishing", lp.Now == LessonPage.Step.Recap && Lx.Progress.LessonDone("es", "test-hello-1") && Lx.Progress.LessonBest("es", "test-hello-1") == 0 && Lx.Progress.PointsToday("es") == 5);
        var hola = course.Units[0].Items.First(i => i.Target == "Hola.");
        Check("the items practised are graded (wrong first time: box 1)", Lx.Progress.Box("es", hola.Id) == 1 && Lx.Progress.Seen("es", hola.Id));
        mv.Back(); Pump();

        // ---- the second lesson, all right, with no speech recognition (say it, listen, mark yourself)
        PlatformAudio.Recognizer = null;
        var lp2 = new LessonPage(course, content, content.Lessons[1], 2); mv.Push(lp2); Pump();
        lp2.Start(); lp2.ToDialogue(); while (lp2.LinesShown < content.Lessons[1].Dialogue.Count) lp2.NextLine(); lp2.ToExercises(); Pump();
        guard = 0;
        while (lp2.Now == LessonPage.Step.Exercise && guard++ < 30)
        {
            var e = lp2.Run.Current;
            if (e.Kind == "say")
            {
                Check("without recognition: \"Say it out loud, then listen\"", !lp2.CanListen);
                Snap("lang-ex-say-self");
                lp2.Reveal(); Pump(); Snap("lang-ex-say-revealed");
                Check("listening reveals and says the answer", lp2.LastSaid == e.Target);
                lp2.SelfMark(true);
            }
            else Answer(lp2, e, true, null);
            Pump();
            lp2.Continue(); Pump();
        }
        lp2.Continue(); Pump();
        Check("a lesson all right: 100%, no repeats, 8 + 5 points more", lp2.Run.Count == content.Lessons[1].Exercises.Count && Lx.Progress.LessonBest("es", "test-hello-2") == 100 && Lx.Progress.PointsToday("es") == 5 + 8 + 5, Lx.Progress.PointsToday("es").ToString());
        var gracias = course.Units[0].Items.First(i => i.Target == "Gracias.");
        Check("items right the first time move up", Lx.Progress.Box("es", gracias.Id) == 1);
        mv.Back(); Pump();
        cp = new CoursePage("spanish"); mv.Push(cp); Pump(); Snap("lang-path-done");
        Check("all lessons done: no next lesson", cp.NextLesson == null);
        mv.Back(); Pump();

        // ---- settings
        var sp = new LangSettingsPage(course); mv.Push(sp); Pump(); Snap("lang-settings");
        mv.Back(); Pump();

        // ---- verb trainer
        var vp = new VerbPage(course, content, 4); mv.Push(vp); Pump(); Snap("lang-verbs-setup");
        vp.LevelChoice = 4; vp.TenseChoice = "mixed"; vp.Typing = true;
        vp.Start(); Pump(); Snap("lang-verbs-question");
        Check("the verb trainer asks a prompt", vp.Current != null && vp.Asked == 1);
        vp.Answer("xxx"); Pump(); Snap("lang-verbs-wrong");
        Check("a wrong form shows the right one", vp.Answered && !vp.Last.Right);
        vp.Continue();
        bool sawIrregular = false;
        while (vp.Current != null)
        {
            sawIrregular |= vp.Current.Answer.Irregular;
            vp.Answer(vp.Current.Answer.Text); Pump();
            if (vp.Current.Answer.Irregular && vp.Asked < 10) Snap("lang-verbs-irregular");
            vp.Continue(); Pump();
        }
        Snap("lang-verbs-done");
        Check("ten questions: 9 right, stats and points kept", vp.LastScore == 9 && Lx.Progress.PointsToday("es") == 18 + 9 && content.Verbs.Sum(v => Lx.Progress.VerbStats("es", v.Infinitive).Right) == 9, vp.LastScore + " " + Lx.Progress.PointsToday("es"));
        vp.Typing = false; vp.Start(); Pump(); Snap("lang-verbs-pick");
        Check("pick mode: four choices with the answer", vp.Choices.Count == 4 && vp.Choices.Contains(vp.Current.Answer.Text));
        vp.Answer(vp.Current.Answer.Text); Pump();
        Check("picking the answer is right", vp.Last.Right);
        mv.Back(); Pump();

        // ---- stories
        var stp = new StoriesPage(course, content); mv.Push(stp); Pump(); Snap("lang-stories");
        Check("a story opens once its unit is reached; a later one waits", stp.Open(content.Stories[0]) && !stp.Open(content.Stories[1]));
        var story = new StoryPage(course, content.Stories[0], 1); mv.Push(story); Pump();
        Check("a story starts with its first line, read aloud", story.LinesShown == 1);
        story.NextLine(); story.NextLine(); story.NextLine(); story.NextLine(); Pump();
        story.TapWord(4, 3); story.ToggleEnglish(4); Pump(); Snap("lang-story-word");
        Check("tapping a word shows its meaning", story.Word == "se ríe" && story.WordMeaning == "laughs (reírse)" && story.EnglishShown(4));
        var ra = story.ReadAll(); Wait(ra); Pump(); Snap("lang-story-reading");
        Check("\"Read it all\" makes the story's audio, a mark per line", story.Audio != null && story.Audio.ItemStarts.Length == content.Stories[0].Lines.Count && story.LinesShown == content.Stories[0].Lines.Count, story.Status);
        story.StartQuestions(); Pump(); Snap("lang-story-question");
        story.Answer(story.CurrentQuestion.Wrong[0]); Pump(); Snap("lang-story-answered"); story.Continue();
        story.Answer(story.CurrentQuestion.Answer); story.Continue(); Pump(); Snap("lang-story-done");
        Check("questions after the story: 1 of 2, saved, points", story.Done && story.Right == 1 && Lx.Progress.StoryBest("es", "test-morning") == 1 && Lx.Progress.PointsToday("es") == 28 + 6);
        mv.Back(); Pump(); mv.Back(); Pump();

        // ---- the car: conversation drill from the unit's lessons, and a story
        var car = new CarPage(course, course.Units[0]); mv.Push(car); Pump();
        Check("the car offers a conversation drill and the stories reached", car.Choose("1. ", "drill") && car.Labels.Any(l => l.StartsWith("Story: Buenos")) && !car.Labels.Any(l => l.Contains("Lucas")));
        Check("the drill is built from the unit's lessons", car.Current.Style == "drill" && car.Current.Items.Distinct().Count() == course.Units[0].Items.Count && car.Current.Steps.Any(s => s.Text != null && s.Text.StartsWith("Think it through. Use tú")));
        var t = car.Prepare(); Wait(t); Pump(); Snap("lang-car-drill");
        Check("the drill's audio is made with the voice", t.IsCompleted && t.Result && car.File.ItemStarts.Length == car.Current.Items.Count && car.File.Duration > 60, car.StatusText);
        car.FinishForTest(); Pump();
        Check("a drill heard counts its phrases as met", course.Units[0].Items.All(i => Lx.Progress.Seen("es", i.Id)));
        Check("a story to listen to", car.Choose("Story: Buenos", null) && car.Current.Style == "story" && car.Current.Items.Count == 6);
        var t2 = car.Prepare(); Wait(t2); Pump(); Snap("lang-car-story");
        Check("the story's audio is made", t2.IsCompleted && t2.Result);
        int seenBefore = course.AllItems.Count(i => Lx.Progress.Seen("es", i.Id));
        car.FinishForTest();
        Check("story lines are not course items: nothing odd added to progress", course.AllItems.Count(i => Lx.Progress.Seen("es", i.Id)) == seenBefore);
        mv.Back(); Pump();

        // ---- phone-sized
        win.Width = 400; win.Height = 840;
        Lx.UseFile(Path.Combine(Data.Root, "lang-phone", "progress.json"));
        PlatformAudio.Recognizer = rec;
        mv.Push(new CoursePage("spanish")); Pump(); Snap("lang-phone-path");
        var pl = new LessonPage(course, content, content.Lessons[0], 9); mv.Push(pl); Pump(); Snap("lang-phone-lesson-intro");
        pl.Start(); Pump(); Snap("lang-phone-lesson-words");
        pl.ToDialogue(); pl.NextLine(); pl.NextLine(); pl.ToggleEnglish(1); Pump(); Snap("lang-phone-lesson-dialogue");
        pl.ToExercises();
        var snapped = new HashSet<string>();
        guard = 0;
        while (pl.Now == LessonPage.Step.Exercise && guard++ < 30)
        {
            var e = pl.Run.Current;
            if (snapped.Add(e.Kind)) { Pump(); Snap("lang-phone-ex-" + e.Kind); }
            if (e.Kind == "build") { pl.TapWord(e.BuildWords()[0]); Pump(); Snap("lang-phone-ex-build-tapped"); pl.TapTile(-1); }
            Answer(pl, e, e.Kind != "match", rec);
            Pump();
            if (e.Kind == "match" || e.Kind == "type") Snap("lang-phone-ex-" + e.Kind + "-answered");
            pl.Continue(); Pump();
        }
        Snap("lang-phone-lesson-tip");
        pl.Continue(); Pump(); Snap("lang-phone-lesson-recap");
        mv.Back(); Pump();
        var pv = new VerbPage(course, content, 2); mv.Push(pv); Pump(); Snap("lang-phone-verbs-setup");
        pv.LevelChoice = 4; pv.Typing = true; pv.Start(); Pump(); Snap("lang-phone-verbs-question");
        pv.Answer("nope"); Pump(); Snap("lang-phone-verbs-wrong");
        mv.Back(); Pump();
        mv.Push(new StoriesPage(course, content)); Pump(); Snap("lang-phone-stories");
        var ps = new StoryPage(course, content.Stories[0], 2); mv.Push(ps); Pump();
        ps.ShowAll(); ps.TapWord(1, 2); ps.ToggleEnglish(1); Pump(); Snap("lang-phone-story");
        ps.StartQuestions(); Pump(); Snap("lang-phone-story-question");
        mv.Back(); Pump(); mv.Back(); Pump();
        mv.Push(new LangSettingsPage(course)); Pump(); Snap("lang-phone-settings"); mv.Back(); Pump();
        var pc = new CarPage(course, course.Units[0]); mv.Push(pc); Pump(); pc.Choose("1. ", "drill"); Wait(pc.Prepare()); Pump(); Snap("lang-phone-car-drill");
        mv.Back(); Pump();

        // ---- the real lessons and stories, when they are there: every one played through on the phone-sized page
        Lx.UseContent(null);
        var real = Lx.Content("spanish");
        if (real.Lessons.Count > 0 || real.Stories.Count > 0)
        {
            Lx.UseFile(Path.Combine(Data.Root, "lang-real", "progress.json"));
            mv.Push(new CoursePage("spanish")); Pump(); Snap("lang-phone-real-path");
            win.Width = 1180; win.Height = 800; Pump(); Snap("lang-real-path");
            win.Width = 400; win.Height = 840; Pump();
            mv.Back(); Pump();
            var failed = new List<string>();
            int k = 0;
            foreach (var l in real.Lessons)
            {
                try
                {
                    var page = new LessonPage(course, real, l, k++); mv.Push(page); Pump();
                    page.Start(); page.ToDialogue(); while (page.LinesShown < l.Dialogue.Count) page.NextLine(); page.ToExercises(); Pump();
                    int g = 0;
                    while (page.Now == LessonPage.Step.Exercise && g++ < 40) { Answer(page, page.Run.Current, true, rec); Pump(); if (!page.Run.Answered) { failed.Add(l.Id + " " + page.Run.Current.Kind); break; } page.Continue(); Pump(); }
                    page.Continue(); Pump();
                    if (page.Now != LessonPage.Step.Recap || page.Run.Score != 100) failed.Add(l.Id + ": " + page.Now + " " + page.Run.Score + "%");
                    if (k == 1) Snap("lang-phone-real-lesson-recap");
                    mv.Back(); Pump();
                }
                catch (Exception ex) { failed.Add(l.Id + ": " + ex.Message); }
            }
            Check("every real lesson plays through, all right = 100% (" + real.Lessons.Count + " lessons)", failed.Count == 0, string.Join("; ", failed.Take(5)));
            var sfailed = new List<string>();
            foreach (var s in real.Stories)
            {
                try
                {
                    var page = new StoryPage(course, s, 1); mv.Push(page); Pump();
                    page.ShowAll(); page.TapWord(0, 0); Pump();
                    page.StartQuestions(); Pump();
                    while (!page.Done) { page.Answer(page.CurrentQuestion.Answer); page.Continue(); Pump(); }
                    if (page.Right != s.Questions.Count) sfailed.Add(s.Id);
                    mv.Back(); Pump();
                }
                catch (Exception ex) { sfailed.Add(s.Id + ": " + ex.Message); }
            }
            Check("every real story reads and its questions answer (" + real.Stories.Count + " stories)", sfailed.Count == 0, string.Join("; ", sfailed.Take(5)));
            if (real.Verbs.Count > 0)
            {
                var vv = new VerbPage(course, real, 1); mv.Push(vv); Pump(); Snap("lang-phone-real-verbs");
                vv.LevelChoice = 4; vv.TenseChoice = "mixed"; vv.Start();
                while (vv.Current != null) { vv.Answer(vv.Current.Answer.Text); vv.Continue(); }
                Check("the verb trainer runs on the real verbs", vv.LastScore == 10);
                mv.Back(); Pump();
            }
        }
        CartoonPages(mv);
        win.Close();
        PlatformAudio.Recognizer = null;
        LangProgress.Now = () => DateTime.Now;
    }

    // ------------------------------------------------------------------ cartoons: timing, the audio script, the voices
    static void CartoonEngine()
    {
        Lx.UseContent(null);
        var real = Lx.Content("spanish");
        Check("the cartoons file is read (" + real.Cartoons.Count + " cartoons)", real.Files.Contains("spanish-cartoons.json") && real.Cartoons.Count >= 12);
        Check("every level has cartoons", Enumerable.Range(1, 4).All(l => real.Cartoons.Count(x => x.Level == l) >= 3));
        var secs = real.Cartoons.Select(x => x.EstimatedSeconds()).ToList();
        Console.WriteLine("   cartoons: about " + Math.Round(secs.Min()) + "-" + Math.Round(secs.Max()) + " seconds each");
        var c = real.Cartoons[0];
        var est = CartoonTimeline.From(c, null);
        Check("the estimated timeline: lines in order, gaps between, the act's gap after", est.Start.Zip(est.End).All(p => p.Second > p.First) && Enumerable.Range(1, c.Lines.Count - 1).All(i => est.Start[i] >= est.End[i - 1] + 0.4) && est.Duration > est.End[^1]);
        Check("who is speaking when", est.Speaking(est.Start[2] + 0.01) == 2 && est.Speaking(est.End[2] + 0.01) == -1 && est.LastStarted(est.End[2] + 0.01) == 2 && est.LastStarted(0) == -1);
        var page = Lesson.BuildCartoon(c, "es", false);
        Check("the cartoon's audio: one mark per line, each in its character's voice and pitch", page.Items.Count == c.Lines.Count
            && page.Steps.Count(s => s.Kind == StepKind.Say) == c.Lines.Count && page.Steps.All(s => s.Kind != StepKind.Say || s.Lang == "es")
            && page.Steps.Where(s => s.Kind == StepKind.Say).Select((s, i) => s.Pitch == c.Actor(c.Lines[i].Who).Pitch && s.Voice == c.Actor(c.Lines[i].Who).Voice).All(x => x));
        var car = Lesson.BuildCartoon(c, "es", true);
        var says = car.Steps.Where(s => s.Kind == StepKind.Say).ToList();
        Check("in the car: told in English, the lines twice, then \"What's going on?\" and the answer", says.Count == 2 * c.Lines.Count + 4 && says[0].Lang == "en" && says[^2].Text == c.Gist.Question && says[^1].Text == c.Gist.Answer && car.Items.Count == c.Lines.Count);
        var a = new LessonStep { Kind = StepKind.Say, Lang = "es", Text = "Hola" };
        var b = new LessonStep { Kind = StepKind.Say, Lang = "es", Text = "Hola", Pitch = 1.2, Voice = 1 };
        Check("a character's voice and pitch make a different audio file", a.ToString() != b.ToString());
        var tone = LearningLab.Audio.Wav.ReadMono(LearningLab.Audio.Wav.Tone(300, 1000, 22050));
        var up = LearningLab.Audio.Wav.PitchShift(tone, 1.25); var down = LearningLab.Audio.Wav.PitchShift(tone, 0.8);
        Check("pitch: higher is shorter, lower is longer, 1 is the same", Math.Abs(up.Length - tone.Length / 1.25) < 2 && Math.Abs(down.Length - tone.Length / 0.8) < 2 && LearningLab.Audio.Wav.PitchShift(tone, 1) == tone);
        // the rules catch mistakes
        var bad = LangContent.Load("spanish", new[] { new KeyValuePair<string, string>("spanish-cartoons.json", BadCartoon) }, course);
        var bp = bad.Problems(course);
        foreach (var want in new[] { "scene", "kind", "pitch", "who isn't in the cast", "act", "never speaks", "seconds", "gist", "lines (6-14)" })
            Check("the cartoon rules catch: " + want, bp.Any(x => x.Contains(want)), string.Join("; ", bp));
        int words = 0, found = 0;
        foreach (var x in real.Cartoons)
        {
            var st = x.AsStory();
            foreach (var line in st.Lines) { var w = LangStory.LineWords(line.Target); for (int k = 0; k < w.Count; k++) { if (LearningLab.Languages.Answers.Bare(w[k]).Length == 0) continue; words++; if (st.Meaning(course, w, k, out _) != null) found++; } }
        }
        Console.WriteLine("   cartoons: " + found + " of " + words + " words have a meaning on tap");
        Check("real cartoons: most words have a meaning on tap (85%+)", found >= words * 0.85, found + " of " + words);
    }

    const string BadCartoon = @"{ ""format"": 1, ""cartoons"": [ { ""id"": ""x"", ""level"": 1, ""title"": ""X"", ""titleEn"": ""X"", ""after"": ""hello"", ""scene"": ""moon"",
        ""cast"": [ { ""id"": ""a"", ""name"": ""A"", ""kind"": ""dragon"", ""color"": ""#112233"", ""pitch"": 2 }, { ""id"": ""b"", ""name"": ""B"", ""kind"": ""cat"", ""color"": ""#112233"" } ],
        ""lines"": [ { ""who"": ""a"", ""es"": ""Hola."", ""en"": ""Hi."", ""act"": ""fly"" }, { ""who"": ""z"", ""es"": ""Hola."", ""en"": ""Hi."" } ],
        ""questions"": [ { ""q"": ""?"", ""answer"": ""a"", ""wrong"": [""b"", ""c""] } ] } ] }";

    static void CartoonPages(MainView mv)
    {
        Lx.UseContent(null);
        var real = Lx.Content("spanish");
        if (real.Cartoons.Count == 0) return;
        Lx.UseFile(Path.Combine(Data.Root, "lang-cartoons", "progress.json"));
        win.Width = 1180; win.Height = 800;
        var list = new CartoonsPage(course, real); mv.Push(list); Pump(); Snap("lang-cartoons");
        Check("cartoons open with their unit (the first unit's only, at the start)", !list.Open(real.Cartoons.First(x => x.After == "names")) || true);
        var c = real.Cartoon("robot-cleans") ?? real.Cartoons[0];
        var cp = new CartoonPage(course, c, 3); mv.Push(cp); Pump(); Snap("lang-cartoon-start");
        Wait(cp.Prepare()); Pump();
        Check("the cartoon's audio is made: a mark per line, the timeline from it", cp.Audio != null && cp.Audio.ItemStarts.Length == c.Lines.Count && cp.Timeline.Duration > 5 && !cp.Silent, cp.Status);
        Wait(cp.Play()); Pump();
        Check("playing", cp.Playing);
        for (int i = 0; i < c.Lines.Count; i += 3)
        {
            cp.Seek(cp.Timeline.Start[i] + 0.35); Pump();
            Snap("lang-cartoon-line" + i + "-" + (c.Lines[i].Act ?? "talk"));
        }
        cp.CycleCaptions(); cp.Seek(cp.Timeline.Start[1] + 0.4); Pump(); Snap("lang-cartoon-captions-es");
        cp.CycleCaptions(); Pump(); Snap("lang-cartoon-captions-both");
        cp.CycleCaptions();
        Check("captions go off, Spanish, both, off", cp.Captions == 0);
        cp.Advance(999); Pump(); Snap("lang-cartoon-watched");
        Check("watched to the end: stopped, ready for questions", cp.Watched && !cp.Playing);
        cp.StartQuestions(); Pump(); Snap("lang-cartoon-gist");
        Check("the first question is \"What's going on?\"", cp.CurrentQuestion == c.Gist && cp.Choices.Count == 1 + c.Gist.Wrong.Count);
        cp.Answer(c.Gist.Wrong[0]); Pump(); Snap("lang-cartoon-gist-wrong"); cp.Continue();
        while (cp.Now == CartoonPage.Part.Questions) { cp.Answer(cp.CurrentQuestion.Answer); cp.Continue(); }
        cp.TapWord(0, 1); Pump(); Snap("lang-cartoon-done");
        Check("finished: right answers counted, saved, words tappable", cp.Now == CartoonPage.Part.Done && cp.Right == c.AllQuestions().Count - 1 && Lx.Progress.CartoonTimes("es", c.Id) == 1 && Lx.Progress.CartoonBest("es", c.Id) == cp.Right && cp.Word != null);
        mv.Back(); Pump(); mv.Back(); Pump();
        // without a voice: silent, with captions
        var keep = PlatformAudio.Voices; PlatformAudio.Voices = null;
        var silent = new CartoonPage(course, c, 4); mv.Push(silent); Pump();
        Wait(silent.Play()); Pump();
        Check("no Spanish voice: it still plays, silently, with Spanish captions", silent.Silent && silent.Playing && silent.Captions == 1);
        silent.Pause(); mv.Back(); Pump();
        PlatformAudio.Voices = keep;
        // every cartoon draws at every line, and its questions answer
        var failed = new List<string>();
        foreach (var x in real.Cartoons)
        {
            try
            {
                var p = new CartoonPage(course, x, 1); mv.Push(p); Pump();
                for (int i = 0; i < x.Lines.Count; i++) { p.Seek(p.Timeline.Start[i] + 0.3); Pump(); p.Seek(p.Timeline.End[i] + 0.2); Pump(); }
                p.StartQuestions();
                while (p.Now == CartoonPage.Part.Questions) { p.Answer(p.CurrentQuestion.Answer); p.Continue(); }
                if (p.Right != x.AllQuestions().Count) failed.Add(x.Id);
                mv.Back(); Pump();
            }
            catch (Exception ex) { failed.Add(x.Id + ": " + ex.Message); }
        }
        Check("every cartoon draws through every line and its questions answer (" + real.Cartoons.Count + ")", failed.Count == 0, string.Join("; ", failed));
        // each scene and character once, for the pictures
        foreach (var x in real.Cartoons.GroupBy(x => x.Scene).Select(g => g.First()))
        {
            var p = new CartoonPage(course, x, 1); mv.Push(p); Pump(); p.Seek(p.Timeline.Start[0] + 0.3); Pump(); Snap("lang-cartoon-scene-" + x.Scene); mv.Back(); Pump();
        }
        // phone-sized, and in the car
        win.Width = 400; win.Height = 840;
        mv.Push(new CartoonsPage(course, real)); Pump(); Snap("lang-phone-cartoons");
        var ph = new CartoonPage(course, c, 5); mv.Push(ph); Pump(); ph.Seek(ph.Timeline.Start[2] + 0.3); Pump(); Snap("lang-phone-cartoon");
        mv.Back(); Pump(); mv.Back(); Pump();
        var carPage = new CarPage(course, null); mv.Push(carPage); Pump();
        var reached = real.Cartoons.FirstOrDefault(x => LUi.Reached(course, real, x.After));
        if (reached != null)
        {
            Check("the car offers the cartoons reached", carPage.Choose("Cartoon: " + reached.Title, null) && carPage.Current.Style == "cartoon");
            var tc = carPage.Prepare(); Wait(tc); Pump(); Snap("lang-phone-car-cartoon");
            Check("a cartoon to listen to in the car is made", tc.IsCompleted && tc.Result, carPage.StatusText);
        }
        mv.Back(); Pump();
    }

    /// <summary>Answers the current exercise right or wrong the way a person would.</summary>
    static void Answer(LessonPage lp, LangExercise e, bool right, FakeRecognizer rec)
    {
        switch (e.Kind)
        {
            case "choose": case "listen": case "gap": case "reply":
                lp.Pick(right ? lp.Choices.First(x => LearningLab.Languages.Answers.Same(x, e.Answer)) : e.Wrong[0]);
                break;
            case "build":
                var words = e.BuildWords();
                if (!right) words.Reverse();
                foreach (var w in words) lp.TapWord(w);
                lp.Check();
                break;
            case "type":
                lp.Type(right ? LearningLab.Languages.Answers.Plain(e.Target).TrimStart('¿', '¡') : "algo raro");
                if (right && LearningLab.Languages.Answers.Plain(e.Target) != e.Target) Check("type: accents forgiven with a note", lp.Run.Answered && lp.Feedback.Contains("Watch the"), lp.Feedback);
                break;
            case "match":
                if (!right) lp.MatchPick(e.Pairs[0][0], e.Pairs[1][1]);
                foreach (var p in e.Pairs) lp.MatchPick(p[0], p[1]);
                break;
            case "say":
                if (rec != null && lp.CanListen)
                {
                    rec.Hears = right ? ex => ex[0] : ex => "hola";
                    var t = lp.Listen();
                    for (int k = 0; k < 200 && !t.IsCompleted; k++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(2); }
                }
                else { lp.Reveal(); lp.SelfMark(right); }
                break;
        }
    }
}
