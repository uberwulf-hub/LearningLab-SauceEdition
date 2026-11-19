// Learning Lab - the checks of Languages: the course file, WAV handling, lessons, progress and saves, the lesson audio
// with a pretend voice (tones, never played), and every page wide and phone-sized. Group "languages".
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using LearningLab.Audio;
using LearningLab.Languages;
using LearningLab.Services;
using LearningLab.Views;

namespace LearningLab.Tests;

static class LangChecks
{
    static void Check(string what, bool ok, string detail = null) => Program.Check("languages: " + what, ok, detail);

    /// <summary>A pretend voice: a tone as long as the text, so lessons have real lengths; counts what it was asked.</summary>
    public sealed class FakeVoices : IVoices
    {
        public int Calls;
        public IReadOnlyList<VoiceInfo> All { get; set; } = new List<VoiceInfo>
        {
            new("en-1", "Pretend English", "en-US", ""),
            new("es-1", "Pretend Spain", "es-ES", ""),
            new("es-2", "Pretend Mexico", "es-MX", "high"),
        };
        public Task<byte[]> MakeWav(string text, string voiceId, double rate)
        {
            Calls++;
            int ms = (int)((150 + 45 * text.Length) / rate);
            // a little silence each side, which Trim takes off
            var tone = Wav.ReadMono(Wav.Tone(voiceId.StartsWith("es") ? 330 : 220, ms, 22050));
            var s = new List<short>(new short[Wav.Samples(120)]); s.AddRange(tone); s.AddRange(new short[Wav.Samples(200)]);
            return Task.FromResult(Wav.Write(s));
        }
        public string HowToGetVoices => "Pretend help.";
        public bool OpenVoiceSettings() => false;
    }

    public sealed class FakePlayer : IPlayer
    {
        public bool Open(string file) => File.Exists(file);
        public void Play() { Playing = true; }
        public void Pause() { Playing = false; }
        public bool Playing { get; private set; }
        public double Position { get; set; }
        public double Duration => 0;
        public event Action Ended { add { } remove { } }
        public void Dispose() { }
    }

    public static void Run()
    {
        CourseFile();
        Wavs();
        Lessons();
        Progress();
        Audio();
        Pages();
        LangChecksNext.Run(course, voices);
    }

    static LangCourse course;

    static void CourseFile()
    {
        course = Lx.Course("spanish");
        Check("the Spanish course loads", course != null);
        if (course == null) return;
        var probs = course.Problems();
        Check("the course has no problems", probs.Count == 0, string.Join("; ", probs.Take(5)));
        int items = course.AllItems.Count();
        Check("at least 20 units and 400 phrases", course.Units.Count >= 20 && items >= 400, course.Units.Count + " units, " + items + " items");
        Check("every level 1-4 has units", Enumerable.Range(1, 4).All(l => course.Units.Count(u => u.Level == l) >= 4));
        Check("item ids are unique", course.ById.Count == items);
        string md5 = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes("¿Qué tal?|How's it going?"))).ToLowerInvariant().Substring(0, 12);
        Check("ids are the first 6 bytes of MD5(es|en)", LangCourse.ItemId("¿Qué tal?", "How's it going?") == md5);
        Check("the course is Latin American Spanish", course.Language == "es" && course.Voice == "es-MX");
        // Spanish punctuation: a question that ends with ? starts with an opening mark somewhere before it
        var bad = course.AllItems.Where(i => (i.Target.EndsWith("?") && !i.Target.Contains('¿')) || (i.Target.EndsWith("!") && !i.Target.Contains('¡'))).Select(i => i.Target).ToList();
        Check("questions and exclamations have their opening marks", bad.Count == 0, string.Join(" | ", bad.Take(5)));
        Check("a broken course says why", Throws(() => LangCourse.Parse("{\"units\": []}")));
    }

    static bool Throws(Action a) { try { a(); return false; } catch (Exception) { return true; } }

    static void Wavs()
    {
        var tone = Wav.Tone(440, 500, 22050);
        var s = Wav.ReadMono(tone);
        Check("a 22.05 kHz WAV becomes 24 kHz", s != null && Math.Abs(s.Length - 12000) <= 2, s?.Length.ToString());
        // stereo 8-bit and float WAVs read too
        byte[] Make(short fmt, short ch, int rate, short bits, byte[] data)
        {
            var ms = new MemoryStream(); var w = new BinaryWriter(ms);
            w.Write("RIFF"u8.ToArray()); w.Write(36 + data.Length); w.Write("WAVE"u8.ToArray());
            w.Write("fmt "u8.ToArray()); w.Write(16); w.Write(fmt); w.Write(ch); w.Write(rate); w.Write(rate * ch * bits / 8); w.Write((short)(ch * bits / 8)); w.Write(bits);
            w.Write("data"u8.ToArray()); w.Write(data.Length); w.Write(data); w.Flush(); return ms.ToArray();
        }
        var eight = Make(1, 2, 24000, 8, new byte[] { 255, 255, 0, 0, 128, 128 });
        var e = Wav.ReadMono(eight);
        Check("8-bit stereo reads", e != null && e.Length == 3 && e[0] > 32000 && e[1] < -32000 && e[2] == 0, e == null ? "null" : string.Join(",", e));
        var fl = new MemoryStream(); var fw = new BinaryWriter(fl); fw.Write(0.5f); fw.Write(-0.5f); fw.Flush();
        var f = Wav.ReadMono(Make(3, 1, 24000, 32, fl.ToArray()));
        Check("float WAVs read", f != null && f.Length == 2 && Math.Abs(f[0] - 16384) < 3 && Math.Abs(f[1] + 16384) < 3);
        Check("not a WAV: null", Wav.ReadMono(new byte[100]) == null && Wav.ReadMono(null) == null);
        var padded = new List<short>(new short[2400]); padded.AddRange(s); padded.AddRange(new short[4800]);
        var t = Wav.Trim(padded.ToArray());
        Check("silence is trimmed (with a short margin)", t.Length < s.Length + Wav.Samples(100) && t.Length >= s.Length - Wav.Samples(30), t.Length + " vs " + s.Length);
        var back = Wav.ReadMono(Wav.Write(s));
        Check("writing and reading back keeps the samples", back.SequenceEqual(s));
    }

    static void Lessons()
    {
        var items = course.Units[0].Items.Take(3).ToList();
        var learn = Lesson.Build("Unit 1", "es", items, "learn", true);
        Check("learn: English, Spanish, Spanish slower, with pauses to repeat", learn.Steps.Count(x => x.Kind == StepKind.Say && x.Lang == "es") == 6 && learn.Steps.Count(x => x.Kind == StepKind.Echo) == 6 && learn.Steps.Any(x => x.Kind == StepKind.Say && x.Rate < 1));
        Check("each item is marked once, in order", learn.Steps.Where(x => x.Kind == StepKind.Item).Select(x => x.Index).SequenceEqual(new[] { 0, 1, 2 }));
        var recall = Lesson.Build("Review", "es", items, "recall", false);
        var firstSay = recall.Steps.First(x => x.Kind == StepKind.Say);
        Check("recall starts each item with the English and a pause to try", firstSay.Lang == "en" && recall.Steps[recall.Steps.IndexOf(firstSay) + 1].Kind == StepKind.Wait && recall.Steps[recall.Steps.IndexOf(firstSay) + 1].Ms >= 3000);
        var listen = Lesson.Build("x", "es", items, "listen", false);
        Check("listen starts each item with the Spanish", listen.Steps.First(x => x.Kind == StepKind.Say).Lang == "es");
        var longer = Lesson.Build("x", "es", items, "recall", false, 2);
        Check("longer pauses double the waits", longer.Steps.First(x => x.Kind == StepKind.Wait).Ms == 2 * recall.Steps.First(x => x.Kind == StepKind.Wait).Ms);
        Check("numbers are read once: \"25 (twenty-five)\" -> \"twenty-five\"", Lesson.Spoken("25 (twenty-five)") == "twenty-five" && Lesson.Spoken("the dog (male)") == "the dog (male)");
    }

    static void Progress()
    {
        string dir = Path.Combine(Data.Root, "lang-progress");
        Directory.CreateDirectory(dir);
        string f = Path.Combine(dir, "progress.json");
        var day = new DateTime(2026, 11, 10, 9, 0, 0);
        LangProgress.Now = () => day;
        var p = LangProgress.Load(f);
        var it = course.Units[0].Items[0];
        Check("a new file starts empty", !p.Blocked && !p.Seen("es", it.Id) && p.DueItems(course).Count == 0);
        p.Meet("es", it.Id);
        Check("met items are due for review", p.Seen("es", it.Id) && p.Due("es", it.Id) && p.Box("es", it.Id) == 0);
        p.Grade("es", it.Id, true); p.Grade("es", it.Id, true); p.Grade("es", it.Id, true);
        Check("three right answers: learned, due in 4 days", p.Learned("es", it.Id) && !p.Due("es", it.Id));
        LangProgress.Now = () => day.AddDays(4);
        Check("due again after 4 days", p.Due("es", it.Id));
        p.Grade("es", it.Id, false);
        Check("a wrong answer goes back to box 1, due today", p.Box("es", it.Id) == 1 && p.Due("es", it.Id) && !p.Learned("es", it.Id));
        LangProgress.Now = () => day;
        Check("streak counts today", p.Streak("es") == 1);
        p.SetQuiz("es", "hello", 7); p.SetQuiz("es", "hello", 5);
        p.AddListened("es", 2.5); p.Rate = 0.75; p.SetSetting("voice-es", "es-2");
        p.Save();
        var text = File.ReadAllText(f);
        text = text.Replace("\"format\": 1", "\"format\": 1, \"fromNewer\": {\"x\": 1}");
        File.WriteAllText(f, text);
        var q = LangProgress.Load(f);
        Check("progress reads back", q.BestQuiz("es", "hello") == 7 && q.Listened("es") == 2.5 && Math.Abs(q.Rate - 0.75) < 0.001 && q.Setting("voice-es") == "es-2" && q.Seen("es", it.Id));
        q.Save();
        Check("unknown fields are kept", File.ReadAllText(f).Contains("fromNewer"));
        Check("a backup is kept", File.Exists(f + ".bak1"));
        // numbers written as text by an older or hand-edited file still read
        File.WriteAllText(Path.Combine(dir, "odd.json"), "{\"format\":1,\"languages\":{\"es\":{\"items\":{\"" + it.Id + "\":{\"box\":\"4\",\"due\":\"2030-01-01\"}},\"listened\":\"3.5\"}},\"settings\":{\"rate\":\"1.2\"}}");
        var o = LangProgress.Load(Path.Combine(dir, "odd.json"));
        Check("numbers as text read", o.Box("es", it.Id) == 4 && o.Listened("es") == 3.5 && Math.Abs(o.Rate - 1.2) < 0.001);
        // damaged: the backup is used and the person is told
        File.WriteAllText(f, "{ not json");
        var d = LangProgress.Load(f);
        Check("a damaged file: the backup is used, with a message", !d.Blocked && d.Seen("es", it.Id) && !string.IsNullOrEmpty(d.Message));
        // damaged with no backups: blocked, nothing written until the person agrees
        string lone = Path.Combine(dir, "lone.json");
        File.WriteAllText(lone, "{ broken");
        var b = LangProgress.Load(lone);
        b.Meet("es", it.Id); b.Save();
        Check("a damaged file with no backup is never overwritten", b.Blocked && File.ReadAllText(lone) == "{ broken" && !string.IsNullOrEmpty(b.Message));
        b.StartFresh(); b.Meet("es", it.Id); b.Save();
        Check("start fresh keeps the old file aside", !b.Blocked && Directory.GetFiles(dir, "lone.json.unreadable-*").Length >= 1 && File.ReadAllText(lone).Contains(it.Id));
        LangProgress.Now = () => DateTime.Now;
    }

    static FakeVoices voices;

    static void Audio()
    {
        voices = new FakeVoices();
        PlatformAudio.Voices = voices;
        PlatformAudio.NewPlayer = () => new FakePlayer();
        Lx.UseFile(Path.Combine(Data.Root, "lang-audio", "progress.json"));
        Check("the course's own region is preferred", Lx.Target(course)?.Id == "es-2");
        Lx.Progress.SetSetting("voice-es", "es-1");
        Check("a chosen voice wins", Lx.Target(course)?.Id == "es-1");
        Lx.Progress.SetSetting("voice-es", "gone");
        Check("a chosen voice that's gone falls back", Lx.Target(course)?.Id == "es-2");
        var items = course.Units[1].Items.Take(4).ToList();
        var l = Lesson.Build("t", "es", items, "learn", true);
        var made = LessonAudio.Make(l, Lx.Target(course), Lx.English(), 0.9, null, CancellationToken.None).GetAwaiter().GetResult();
        Check("the lesson's audio is made", File.Exists(made.File) && made.Duration > 5);
        var back = Wav.ReadMono(File.ReadAllBytes(made.File));
        Check("its length matches", back != null && Math.Abs(Wav.Seconds(back.Length) - made.Duration) < 0.01);
        Check("items start in order, inside the lesson", made.ItemStarts.Length == 4 && made.ItemStarts.Zip(made.ItemStarts.Skip(1)).All(x => x.First < x.Second) && made.ItemStarts[^1] < made.Duration);
        Check("ItemAt finds the item playing", made.ItemAt(made.ItemStarts[2] + 0.5) == 2 && made.ItemAt(0) == -1);
        int calls = voices.Calls;
        var again = LessonAudio.Make(l, Lx.Target(course), Lx.English(), 0.9, null, CancellationToken.None).GetAwaiter().GetResult();
        Check("the same lesson again is found, not made", voices.Calls == calls && again.File == made.File && again.ItemStarts.SequenceEqual(made.ItemStarts));
        var r = Lesson.Build("t", "es", items, "recall", true);
        LessonAudio.Make(r, Lx.Target(course), Lx.English(), 0.9, null, CancellationToken.None).GetAwaiter().GetResult();
        Check("another style reuses the spoken phrases", voices.Calls == calls);
        // the "echo" pause is as long as the phrase plus a little
        var one = Lesson.Build("t", "es", items.Take(1).ToList(), "recall", false);
        var a1 = LessonAudio.Make(one, Lx.Target(course), Lx.English(), 0.9, null, CancellationToken.None).GetAwaiter().GetResult();
        var piece = LessonAudio.Piece(items[0].Target, Lx.Target(course), 0.9).GetAwaiter().GetResult();
        Check("there is time to repeat after the Spanish", a1.Duration > Wav.Seconds(piece.Length) * 2.2 + 3);
        Check("no Spanish voice: a plain sentence", Throws(() => LessonAudio.Make(l, null, null, 0.9, null, CancellationToken.None).GetAwaiter().GetResult()));
    }

    static Window win;
    static void Pump() { Dispatcher.UIThread.RunJobs(); }
    static void Snap(string name) => LearningLab.Tests.Snap.Save(win, name);
    static void Wait(Task t) { for (int k = 0; k < 2000 && !t.IsCompleted; k++) { Pump(); Thread.Sleep(5); } Pump(); }

    static void Pages()
    {
        Lx.UseFile(Path.Combine(Data.Root, "lang-pages", "progress.json"));
        LangProgress.Now = () => new DateTime(2026, 11, 10, 17, 0, 0);
        win = new Window { Width = 1180, Height = 800, Content = new MainView() };
        win.Show();
        var mv = MainView.Current;
        mv.Push(new HomePage()); Pump(); Snap("home");
        mv.Push(new LanguagesPage()); Pump(); Snap("lang-languages");
        var cp = new CoursePage("spanish"); mv.Push(cp); Pump(); Snap("lang-course");
        Check("the course page opens", mv.Page is CoursePage);
        var u = course.Units[0];
        var up = new UnitPage(course, u); mv.Push(up); Pump(); Snap("lang-unit");

        var cards = new CardsPage(course, u.Items.Take(5).ToList(), u.Title); mv.Push(cards); Pump(); Snap("lang-cards");
        cards.Flip(); Pump(); Snap("lang-cards-flipped");
        Check("a card turns over", cards.Shown);
        cards.Grade(true);
        for (int k = 0; k < 3; k++) { cards.Flip(); cards.Grade(true); }
        cards.Flip(); cards.Grade(false);   // comes back at the end
        Check("a card not known yet comes back", cards.At == 5 && !cards.Shown);
        cards.Flip(); cards.Grade(true); Pump(); Snap("lang-cards-done");
        Check("flashcards grade the items", Lx.Progress.Box("es", u.Items[0].Id) == 1 && Lx.Progress.Box("es", u.Items[4].Id) == 2 && File.Exists(Lx.Progress.File));
        mv.Back(); Pump();

        var quiz = new QuizPage(course, u, 7); mv.Push(quiz); Pump(); Snap("lang-quiz-say");
        Check("the first question is \"how do you say it\"", !quiz.Hearing && quiz.Choices.Count == 4 && quiz.Choices.Contains(quiz.Current));
        quiz.Answer(quiz.Choices.ToList().IndexOf(quiz.Current)); Pump(); Snap("lang-quiz-answered");
        quiz.Next(); Pump(); Snap("lang-quiz-hear");
        Check("the second is \"what does this mean\"", quiz.Hearing);
        bool wrongOnce = false;
        while (!quiz.Finished)
        {
            int right = quiz.Choices.ToList().IndexOf(quiz.Current);
            quiz.Answer(!wrongOnce ? (right + 1) % 4 : right); wrongOnce = true;
            quiz.Next();
        }
        Pump(); Snap("lang-quiz-done");
        Check("the quiz scores 9 of 10 and keeps the best", quiz.Score == 9 && Lx.Progress.BestQuiz("es", u.Id) == 9);
        mv.Back(); Pump();

        var car = new CarPage(course, u); mv.Push(car); Pump(); Snap("lang-car");
        var t = car.Prepare(); Wait(t);
        Check("the car lesson is made", t.IsCompleted && t.Result && car.File != null && car.File.ItemStarts.Length == u.Items.Count, car.StatusText);
        Snap("lang-car-ready");
        car.FinishForTest(); Pump(); Snap("lang-car-done");
        Check("a whole lesson counts: every item met, the unit heard, minutes added", u.Items.All(i => Lx.Progress.Seen("es", i.Id)) && Lx.Progress.TimesHeard("es", u.Id) == 1 && Lx.Progress.Listened("es") > 1);
        mv.Back(); Pump();
        mv.Push(new CarPage(course, null)); Pump();
        Check("in the car without a unit picks a lesson", (mv.Page as CarPage).Current.Items.Count > 0);
        mv.Back(); Pump();
        mv.Push(new VoicesPage(course)); Pump(); Snap("lang-voices");
        mv.Back(); Pump();
        var rev = new CardsPage(course, Lesson.ReviewMix(course, Lx.Progress, 20, new Random(1)), "Review"); mv.Push(rev); Pump();
        Check("review mixes what was met", rev.At == 0 && !rev.Shown);
        mv.Back(); Pump();

        // no Spanish voice on the device: the pages say how to get one, nothing breaks
        voices.All = voices.All.Where(v => !v.Lang.StartsWith("es")).ToList();
        mv.Back(); Pump();
        cp = new CoursePage("spanish"); mv.Push(cp); Pump(); Snap("lang-course-novoice");
        var car2 = new CarPage(course, u); mv.Push(car2); Pump();
        var t2 = car2.Prepare(); Wait(t2);
        Check("without a voice the car page explains", t2.IsCompleted && !t2.Result && car2.StatusText.Contains("voice"), car2.StatusText);
        mv.Back(); Pump();
        voices.All = new FakeVoices().All;

        // phone-sized
        win.Width = 400; win.Height = 840;
        mv.Back(); Pump();
        mv.Push(new CoursePage("spanish")); Pump(); Snap("lang-phone-course");
        mv.Push(new UnitPage(course, course.Units[5])); Pump(); Snap("lang-phone-unit");
        mv.Push(new CardsPage(course, course.Units[5].Items.Take(3).ToList(), "Family")); Pump(); Snap("lang-phone-cards"); mv.Back();
        mv.Push(new QuizPage(course, course.Units[5], 3)); Pump(); Snap("lang-phone-quiz"); mv.Back();
        var pc = new CarPage(course, course.Units[5]); mv.Push(pc); Pump(); Wait(pc.Prepare()); Snap("lang-phone-car"); mv.Back();
        mv.Push(new VoicesPage(course)); Pump(); Snap("lang-phone-voices");
        win.Close();
        LangProgress.Now = () => DateTime.Now;
    }
}
