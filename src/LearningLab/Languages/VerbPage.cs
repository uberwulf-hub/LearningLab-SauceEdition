// Learning Lab - Languages: the verb trainer. Pick a tense, how far (the verbs of levels up to N) and type or pick;
// then ten prompts like "nosotros · comer · preterite". Answers come from the Conjugator (worked out, never typed by
// hand); after each answer the whole tense is shown with the irregular forms marked.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using LearningLab.Services;
using LearningLab.Views;

namespace LearningLab.Languages;

public class VerbPage : IPage, IKeyPage
{
    public const int Count = 10;
    readonly LangCourse c;
    readonly LangContent content;
    readonly Random rnd;
    readonly StackPanel body = new() { Spacing = 12 };
    public string Title => "Verb trainer";
    public Control View { get; }

    // choices
    public string TenseChoice { get; set; } = "present";
    public int LevelChoice { get; set; } = 1;
    public bool Typing { get; set; } = true;

    VerbQuiz quiz;
    public VerbQuestion Current { get; private set; }
    public List<string> Choices { get; private set; } = new();
    public int Asked { get; private set; }
    public int Right { get; private set; }
    public bool Answered { get; private set; }
    public bool Started => quiz != null;
    public bool Finished => !Started && LastScore >= 0;
    public AnswerResult Last { get; private set; }
    readonly List<VerbQuestion> missed = new();
    string given;
    TextBox box;

    public VerbPage(LangCourse c, LangContent content, int seed = -1)
    {
        this.c = c; this.content = content;
        rnd = seed < 0 ? new Random() : new Random(seed);
        TenseChoice = Lx.Progress.Setting("verb-tense") ?? "present";
        if (int.TryParse(Lx.Progress.Setting("verb-level"), out int lv)) LevelChoice = Math.Clamp(lv, 1, 4);
        Typing = Lx.Progress.Setting("verb-mode") != "pick";
        View = Ui.Page(body, 720);
        Show();
    }

    IEnumerable<Tense> Tenses => TenseChoice switch
    {
        "preterite" => new[] { Tense.Preterite },
        "near" => new[] { Tense.NearFuture },
        "mixed" => new[] { Tense.Present, Tense.Preterite },
        _ => new[] { Tense.Present },
    };

    public List<LangVerb> ChosenVerbs => content.Verbs.Where(v => v.Level <= LevelChoice).ToList();

    public void Start()
    {
        Lx.Progress.SetSetting("verb-tense", TenseChoice);
        Lx.Progress.SetSetting("verb-level", LevelChoice.ToString());
        Lx.Progress.SetSetting("verb-mode", Typing ? "type" : "pick");
        Lx.Save();
        quiz = new VerbQuiz(ChosenVerbs, Tenses, rnd);
        Asked = 0; Right = 0; missed.Clear();
        NextQuestion();
    }

    void NextQuestion()
    {
        Answered = false; Last = null; given = null;
        if (Asked >= Count) { Current = null; Done(); return; }
        Current = quiz.Next();
        if (Current == null) { Show(); return; }
        Asked++;
        Choices = Typing ? new List<string>() : VerbQuiz.Choices(Current, rnd);
        Show();
    }

    /// <summary>Answers (typed or picked).</summary>
    public void Answer(string text)
    {
        if (Current == null || Answered || string.IsNullOrWhiteSpace(text)) return;
        given = text;
        Last = Typing ? VerbQuiz.Check(Current, text) : new AnswerResult { Right = Answers.Same(text, Current.Answer.Text), Expected = Current.Answer.Text };
        Answered = true;
        if (Last.Right) { Right++; Lx.Progress.AddPoints(c.Language, 1); } else missed.Add(Current);
        Lx.Progress.VerbAnswer(c.Language, Current.Verb.Infinitive, Conjugator.TenseName(Current.Tense).Replace(' ', '-'), Last.Right);
        Lx.Save();
        Services.Feel.Right(Last.Right);
        Lx.Say(c, Current.Answer.Text);
        Show();
    }

    public void Continue() { if (Answered) NextQuestion(); }

    void Show()
    {
        body.Children.Clear();
        if (!Started) { Setup(); return; }
        if (Current == null)
        {
            body.Children.Add(LUi.Head("No verbs to practise", "Choose another tense or more levels."));
            var back = Ui.Button("Change the choices");
            back.HorizontalAlignment = HorizontalAlignment.Left;
            back.Click += (s, e) => { quiz = null; Show(); };
            body.Children.Add(back);
            return;
        }
        var q = Current;
        body.Children.Add(Ui.Text("Question " + Asked + " of " + Count + "  ·  " + Right + " right", 13.5, Ui.Muted));
        body.Children.Add(LUi.Bar((Asked - 1) / (double)Count, LUi.Amber, 6));
        var card = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
        var who = LUi.Wrap(Ui.Text(Conjugator.Persons[q.Person] + "  ·  " + q.Verb.Infinitive, 28, Ui.TextC, true)); who.TextAlignment = TextAlignment.Center;
        card.Children.Add(who);
        var tense = Ui.Text(Conjugator.TenseName(q.Tense), 18, LUi.AmberText, true); tense.TextAlignment = TextAlignment.Center; tense.HorizontalAlignment = HorizontalAlignment.Center;
        card.Children.Add(tense);
        var en = LUi.Wrap(Ui.Text(q.Verb.English + "  ·  " + Conjugator.TenseHelp(q.Tense), 13.5, Ui.Muted)); en.TextAlignment = TextAlignment.Center;
        card.Children.Add(en);
        body.Children.Add(Ui.Card(card, Ui.Surface, 18));

        if (Typing)
        {
            box = new TextBox { FontSize = 20, PlaceholderText = "Type the form", Text = given ?? "", IsEnabled = !Answered, MinHeight = 44 };
            box.KeyDown += (s, e) => { if (e.Key == Key.Enter) { e.Handled = true; if (Answered) Continue(); else Answer(box.Text); } };
            body.Children.Add(box);
            if (!Answered)
            {
                var keys = new WrapPanel();
                foreach (var ch in new[] { "á", "é", "í", "ó", "ú", "ñ" })
                {
                    var b = Ui.Button(ch, false, 16);
                    b.MinWidth = 44; b.Padding = new Thickness(8, 4); b.Margin = new Thickness(0, 0, 6, 6);
                    b.Click += (s, e) => { int at = box.CaretIndex; box.Text = (box.Text ?? "").Insert(Math.Min(at, (box.Text ?? "").Length), ch); box.CaretIndex = at + 1; box.Focus(); };
                    keys.Children.Add(b);
                }
                body.Children.Add(keys);
                var check = LUi.Primary("Check", 17);
                check.HorizontalAlignment = HorizontalAlignment.Stretch;
                check.Click += (s, e) => Answer(box.Text);
                body.Children.Add(check);
                Avalonia.Threading.Dispatcher.UIThread.Post(() => box?.Focus());
            }
        }
        else
        {
            var list = new StackPanel { Spacing = 8 };
            for (int k = 0; k < Choices.Count; k++)
            {
                string ch = Choices[k];
                var b = LUi.Choice((Platform.Touch ? "" : (k + 1) + "   ") + ch, 18);
                if (Answered) { if (Answers.Same(ch, q.Answer.Text)) LUi.Mark(b, true); else if (ch == given) LUi.Mark(b, false); }
                b.Click += (s, e) => Answer(ch);
                list.Children.Add(b);
            }
            body.Children.Add(list);
        }

        if (Answered)
        {
            var fb = new StackPanel { Spacing = 6 };
            fb.Children.Add(LUi.Wrap(Ui.Text(Last.Right ? "Right! " + q.Answer.Text + (Last.Note != null ? ". " + Last.Note : "") : "Not quite. It's " + q.Answer.Text + (Last.Note != null ? ". " + Last.Note : ""), 16, Last.Right ? Ui.Green : LUi.AmberText, true)));
            string why = VerbQuiz.Why(q.Answer);
            if (why != null) fb.Children.Add(LUi.Wrap(Ui.Text(why, 13.5, Ui.TextC)));
            fb.Children.Add(Table(q));
            var cardFb = Ui.Card(fb, Ui.Surface2, 12);
            cardFb.BorderBrush = Ui.Brush(Last.Right ? Ui.Green : LUi.Amber, 140);
            body.Children.Add(cardFb);
            var next = LUi.Primary(Asked >= Count ? "See how you did" : "Next", 17);
            next.HorizontalAlignment = HorizontalAlignment.Stretch;
            next.Click += (s, e) => Continue();
            body.Children.Add(next);
        }
    }

    /// <summary>The whole tense of the verb, irregular forms in amber, the asked one underlined.</summary>
    Control Table(VerbQuestion q)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(0, 4, 0, 0) };
        int row = 0;
        foreach (int p in q.Verb.Persons)
        {
            g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var f = Conjugator.Form(q.Verb, q.Tense, p);
            var a = Ui.Text(Conjugator.Persons[p], 13.5, Ui.Muted); a.Margin = new Thickness(0, 2, 14, 2);
            var b = Ui.Text(f.Text + (f.Irregular ? "  (irregular)" : f.Change != null ? "  (pattern)" : ""), 14.5, f.Irregular ? LUi.AmberText : f.Change != null ? Ui.AccentText : Ui.TextC, p == q.Person);
            if (p == q.Person) b.TextDecorations = TextDecorations.Underline;
            Grid.SetRow(a, row); Grid.SetRow(b, row); Grid.SetColumn(b, 1);
            g.Children.Add(a); g.Children.Add(b);
            row++;
        }
        return g;
    }

    void Setup()
    {
        body.Children.Add(LUi.Head("Verb trainer", "Practise verb forms: who does it, which verb, and when. Ten at a time."));
        body.Children.Add(Ui.Text("When", 15, Ui.TextC, true));
        body.Children.Add(LUi.Segmented(new List<(string, string)> { ("Present", "present"), ("Preterite (past)", "preterite"), ("Near future (ir a)", "near"), ("Present and past mixed", "mixed") }, TenseChoice, v => { TenseChoice = v; Show(); }));
        body.Children.Add(Ui.Text("Which verbs", 15, Ui.TextC, true));
        var levels = new List<(string, int)>();
        for (int l = 1; l <= 4; l++) if (content.Verbs.Any(v => v.Level == l)) levels.Add((l == 1 ? "Level 1" : "Levels 1-" + l, l));
        body.Children.Add(LUi.Segmented(levels, LevelChoice, v => { LevelChoice = v; Show(); }));
        var chosen = ChosenVerbs;
        int irr = chosen.Count(v => Tenses.Any(t => v.HasIrregular(t)));
        body.Children.Add(LUi.Wrap(Ui.Text(chosen.Count + " verbs" + (irr > 0 ? ", " + irr + " with irregular forms" : "") + ": " + string.Join(", ", chosen.Take(14).Select(v => v.Infinitive)) + (chosen.Count > 14 ? "..." : ""), 13.5, Ui.Muted)));
        body.Children.Add(Ui.Text("How to answer", 15, Ui.TextC, true));
        body.Children.Add(LUi.Segmented(new List<(string, bool)> { ("Type it", true), ("Pick from four", false) }, Typing, v => Typing = v));
        var go = LUi.Primary("Start", 18);
        go.HorizontalAlignment = HorizontalAlignment.Stretch;
        go.Click += (s, e) => Start();
        body.Children.Add(go);
        var hard = content.Verbs.Select(v => (v, s: Lx.Progress.VerbStats(c.Language, v.Infinitive))).Where(x => x.s.Wrong > 0).OrderByDescending(x => x.s.Wrong - x.s.Right).Take(6).ToList();
        if (hard.Count > 0) body.Children.Add(LUi.Wrap(Ui.Text("To keep practising: " + string.Join(", ", hard.Select(x => x.v.Infinitive)), 13.5, Ui.Muted)));
    }

    void Done()
    {
        quiz = null;
        body.Children.Clear();
        string praise = Right == Count ? "Perfect!" : Right >= 8 ? "Great job!" : Right >= 5 ? "Good going!" : "Keep practising!";
        body.Children.Add(LUi.Head(praise, Right + " right out of " + Count + ". +" + Right + " points."));
        if (missed.Count > 0)
        {
            body.Children.Add(Ui.Text("To look at again", 15, Ui.TextC, true));
            foreach (var q in missed.Distinct()) body.Children.Add(LUi.Wrap(Ui.Text(q.Prompt + "  →  " + q.Answer.Text + (q.Answer.Irregular ? "  (irregular)" : ""), 14, Ui.Muted)));
        }
        var row = new WrapPanel();
        var again = LUi.Primary("Ten more", 16); again.Margin = new Thickness(0, 0, 10, 10);
        again.Click += (s, e) => Start();
        var change = Ui.Button("Change the choices", false, 16); change.Margin = new Thickness(0, 0, 10, 10);
        change.Click += (s, e) => { quiz = null; Current = null; Asked = 0; Show(); };
        row.Children.Add(again); row.Children.Add(change);
        body.Children.Add(row);
        LastScore = Right;
    }

    public int LastScore { get; private set; } = -1;

    public bool OnKey(KeyEventArgs e)
    {
        if (!Typing && Current != null && !Answered)
        {
            int k = e.Key switch { Key.D1 or Key.NumPad1 => 0, Key.D2 or Key.NumPad2 => 1, Key.D3 or Key.NumPad3 => 2, Key.D4 or Key.NumPad4 => 3, _ => -1 };
            if (k >= 0 && k < Choices.Count) { Answer(Choices[k]); return true; }
        }
        if (e.Key == Key.Enter && Answered && !(e.Source is TextBox)) { Continue(); return true; }
        return false;
    }

    public void Closed() { LessonAudio.StopSaying(); }
}
