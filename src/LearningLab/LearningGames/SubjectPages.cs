// Learning Games - a subject (History, English, US Citizenship, Spanish, Coding, Science): the level, the four ways to
// play and the topics; then Quiz Quest (10 questions, combos, why + a memory trick after each), Lightning Round (60
// seconds), Flash Cards (think, flip, "got it" / "not yet") and Memory Match (pair questions with answers), and the
// round's end with what to remember. Spaced review happens in Bank.Record.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using LearningLab.Views;
using Snd = LearningGames.Sound;

namespace LearningLab.LearningGames;

public class SubjectPage : IPage
{
    public readonly string Id;
    readonly Bank.SubjectInfo info;
    public string Title => info.Name;
    public Control View { get; }
    public Control Extras => Lg.TopExtras();
    readonly StackPanel body = new();
    bool filling;

    public record ModeInfo(string Id, string Name, string Glyph, string Color, string Text);
    public static readonly ModeInfo[] Modes =
    {
        new("quiz", "Quiz Quest", "?", "#3B82F6", "10 questions. Build combos, earn stars."),
        new("cards", "Flash Cards", "↻", "#22C55E", "Flip, remember, repeat. Best for memorizing."),
        new("lightning", "Lightning Round", "⚡", "#F97316", "60 seconds. How many can you get?"),
        new("match", "Memory Match", "▣", "#EC4899", "Flip cards to pair questions and answers."),
    };

    public SubjectPage(string id)
    {
        Id = id;
        info = Bank.Subjects.First(s => s.Id == id);
        View = Ui.Page(body);
        Fill();
        View.AttachedToVisualTree += (s, e) => Fill();
    }

    /// <summary>The English page's Spelling Bee card (the player is already chosen here).</summary>
    public static void OpenSpellingBee() => MainView.Current.Push(new LearningLab.SpellingBee.BeeHomePage());

    void Fill()
    {
        var p = Lg.Me; if (p == null) return;
        var st = p.Subject(Id);
        body.Children.Clear();
        var head = new WrapPanel { Margin = new Thickness(0, 0, 0, 16) };
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Margin = new Thickness(0, 0, 24, 8) };
        title.Children.Add(Lg.Badge(info.Glyph, info.Color, 60));
        int right = st.Answered > 0 ? 100 * st.Correct / st.Answered : 0;
        title.Children.Add(new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children = { Ui.Text(info.Name, 28, Ui.TextC, true), Ui.Text(st.Answered > 0 ? st.Answered + " answered, " + right + "% right  |  " + st.DueCount + " ready for review" : info.Text, 14, Ui.Muted) },
        });
        head.Children.Add(title);
        var levels = Bank.LevelsOf(Id);
        var combo = new ComboBox { Width = 240, FontSize = 14, ItemsSource = levels.Select(k => Bank.LevelNames[k]).ToList() };
        filling = true;
        combo.SelectedIndex = Array.IndexOf(levels, st.Level);
        filling = false;
        combo.SelectionChanged += (s, e) =>
        {
            if (filling || combo.SelectedIndex < 0) return;
            var ss = Lg.Me.Subject(Id);
            ss.Level = levels[combo.SelectedIndex]; ss.Recent = new();
            Profiles.Save();
            Dispatcher.UIThread.Post(Fill);
        };
        head.Children.Add(new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { Ui.Text("Level", 13, Ui.Muted), combo } });
        body.Children.Add(head);

        if (Id == "english")
        {
            // the Spelling Bee lives under English (Andrew, 2026-11-12): its own card above the question games
            body.Children.Add(Lg.H2("Spelling Bee"));
            var bee = Lg.Tile("\U0001F41D", "#FBBF24", "Spelling Bee practice", "Hear a word, spell it out loud or type it, and practice the tricky ones until they stick. Five word lists are ready, and you can add your school's.", "", Ui.Yellow, OpenSpellingBee, 140);
            var bg = Ui.Tiles(new List<Control> { bee }, 260); bg.Margin = new Thickness(-6, 0, -6, 10);
            body.Children.Add(bg);
        }

        body.Children.Add(Lg.H2("Play"));
        var modes = new List<Control>();
        foreach (var m in Modes)
        {
            int best = st.Best(m.Id);
            string mid = m.Id;
            modes.Add(Lg.Tile(m.Glyph, m.Color, m.Name, m.Text, best > 0 ? "Best: " + best : "", Ui.Yellow, () => Start(mid, -1), 160));
        }
        var mg = Ui.Tiles(modes, 200); mg.Margin = new Thickness(-6, 0, -6, 10);
        body.Children.Add(mg);

        var th = Lg.H2("Topics"); th.Margin = new Thickness(0, 10, 0, 10); body.Children.Add(th);
        var topics = Bank.Topics(Id, st.Level);
        var tl = new List<Control>();
        for (int t = 0; t < topics.Count; t++)
        {
            int known = 0;
            foreach (var q in topics[t].Questions) { var (box, _) = st.Card(q.Id); if (box >= 3) known++; }
            int count = topics[t].Questions.Count;
            var row = new DockPanel();
            var lv = Ui.Text(known + " / " + count + " known", 12.5, count > 0 && known >= count ? Ui.Green : Ui.AccentText);
            lv.VerticalAlignment = VerticalAlignment.Center; lv.Margin = new Thickness(8, 0, 0, 0);
            DockPanel.SetDock(lv, Dock.Right);
            row.Children.Add(lv);
            var inner = new StackPanel();
            var n = Ui.Text(topics[t].Name, 14.5); n.TextWrapping = TextWrapping.NoWrap; n.TextTrimming = TextTrimming.CharacterEllipsis;
            inner.Children.Add(n);
            var bar = Lg.Bar(count > 0 ? (double)known / count : 0, "#22C55E"); bar.Margin = new Thickness(0, 7, 10, 0);
            inner.Children.Add(bar);
            row.Children.Add(inner);
            int ti = t;
            var b = new Button { Content = row, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(14, 10), MinHeight = 48, Background = Ui.Brush(Ui.Surface2), CornerRadius = new CornerRadius(10) };
            ToolTip.SetTip(b, "Quiz on this topic");
            b.Click += (s, e) => Start("quiz", ti);
            tl.Add(b);
        }
        var tg = Ui.Tiles(tl, 320, 8); tg.Margin = new Thickness(-4, 0, -4, 0);
        body.Children.Add(tg);
    }

    /// <summary>Starts a way to play (quiz, cards, lightning, match), on one topic or all (-1).</summary>
    public void Start(string mode, int topic)
    {
        var p = Lg.Me;
        var st = p.Subject(Id);
        int count = mode switch { "quiz" => 10, "cards" => 15, "lightning" => 60, _ => 30 };
        var items = Bank.Round(p, Id, st.Level, topic, count, Lg.Rand);
        if (mode == "match") items = items.Where(x => x.Q.Choices[0] != "True" && string.IsNullOrEmpty(x.Q.Passage) && x.Q.Q.Length <= 90 && x.Q.Right.Length <= 40).Take(6).ToList();
        if (items.Count < (mode == "match" ? 3 : 1)) { MainView.Current.Message("", "There are not enough questions here yet. Try another level or topic."); return; }
        var r = new SubjectRound { Subject = Id, Mode = mode, Topic = topic, Items = items };
        MainView.Current.Push(mode switch { "cards" => new CardsPage(r), "match" => new MatchPage(r), _ => new QuizPage(r) });
    }
}

/// <summary>The round being played.</summary>
public sealed class SubjectRound
{
    public string Subject = "", Mode = "";
    public int Topic = -1, I = -1, Right, Score, Combo, BestCombo, Pairs, Found, Moves;
    public List<RoundItem> Items = new();
    public readonly List<RoundItem> Missed = new();
    public bool Answered;
    public string LevelChange = "";
    public DateTime Ends;
    public string Name => Bank.Subjects.First(s => s.Id == Subject).Name;
    public bool ReadAloud { get { var l = Lg.Me.Subject(Subject).Level; return l == "prek" || l == "k2"; } }
    public void Record(RoundItem item, bool ok) { string c = Bank.Record(Lg.Me, Subject, item, ok); if (c.Length > 0) LevelChange = c; }
}

static class Paper
{
    /// <summary>A reading question's story, on paper like a page of a book (null when there is none).</summary>
    public static Border Passage(Question q, double size)
    {
        if (string.IsNullOrEmpty(q.Passage)) return null;
        var sp = new StackPanel();
        if (!string.IsNullOrEmpty(q.PassageTitle)) { var t = Ui.Text(q.PassageTitle.ToUpperInvariant(), 13, "#7A5C00", true); t.Margin = new Thickness(0, 0, 0, 6); sp.Children.Add(t); }
        var body = Ui.Text(q.Passage, size, "#2A2112"); body.LineHeight = size * 1.48; sp.Children.Add(body);
        return new Border { Child = sp, Background = Ui.Brush("#FFF7DB"), BorderBrush = Ui.Brush("#E0B84A"), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(12), Padding = new Thickness(20, 12), Margin = new Thickness(0, 0, 0, 16) };
    }

    public static Border Code(string code, double size)
    {
        if (string.IsNullOrEmpty(code)) return null;
        return new Border
        {
            Child = new TextBlock { Text = code, FontFamily = new FontFamily("Consolas, Cascadia Mono, Menlo, monospace"), FontSize = size, Foreground = Ui.Brush("#E2E8F0"), TextWrapping = TextWrapping.Wrap },
            Background = Ui.Brush("#0F172A"), BorderBrush = Ui.Brush(Ui.BorderStrong), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 10), Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Center,
        };
    }

    public static Border Tip(string tip, bool center = false)
    {
        var t = Ui.Text(tip, 16, "#FDE68A"); t.FontStyle = FontStyle.Italic; t.VerticalAlignment = VerticalAlignment.Center;
        if (center) t.TextAlignment = TextAlignment.Center;
        var d = new DockPanel();
        var bulb = Ui.Text("\U0001F4A1", 20, Ui.Yellow); bulb.Margin = new Thickness(0, 0, 10, 0); bulb.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(bulb, Dock.Left);
        d.Children.Add(bulb); d.Children.Add(t);
        return new Border { Child = d, Background = Ui.Brush("#33280E"), BorderBrush = Ui.Brush(Ui.Yellow), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 10), Margin = new Thickness(0, 12, 0, 0) };
    }
}

// ================================================================ Quiz Quest and Lightning Round
public class QuizPage : IPage, IKeyPage
{
    readonly SubjectRound R;
    public string Title => R.Name;
    public Control View { get; }
    public Control Extras => Lg.TopExtras();
    readonly TextBlock mode = Ui.Text("", 14, Ui.Muted), score = Ui.Text("0", 20, Ui.TextC, true), combo = Ui.Text("", 16, "#F97316", true);
    readonly Grid barHost = new() { Height = 10, Margin = new Thickness(0, 8, 20, 0) };
    readonly StackPanel center = new() { MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 10) };
    readonly UniformGrid choices = new() { Columns = 2, Margin = new Thickness(0, 22, 0, 0) };
    readonly ContentControl feedback = new();
    readonly Button next = Ui.Button("Next", true, 18);
    readonly TextBlock hint = Ui.Text("", 13, Ui.Muted);
    readonly List<(Button B, int Index)> choiceButtons = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    DateTime? nextAt;
    bool done;

    public QuizPage(SubjectRound r)
    {
        R = r;
        var top = new DockPanel();
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Spacing = 6, Children = { combo, Ui.Text("★", 18, Ui.Yellow), score } };
        combo.Margin = new Thickness(0, 0, 12, 0);
        DockPanel.SetDock(right, Dock.Right);
        top.Children.Add(right);
        top.Children.Add(new StackPanel { Children = { mode, barHost } });
        next.IsVisible = false; next.Padding = new Thickness(30, 10);
        next.Click += (s, e) => Next();
        var bottom = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(next, Dock.Right);
        bottom.Children.Add(next);
        hint.VerticalAlignment = VerticalAlignment.Center;
        bottom.Children.Add(hint);
        var scroll = new ScrollViewer { Content = center, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var g = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(16, 12, 16, 16) };
        g.Children.Add(top);
        Grid.SetRow(scroll, 1); g.Children.Add(scroll);
        Grid.SetRow(bottom, 2); g.Children.Add(bottom);
        View = g;
        timer.Tick += (s, e) => Tick();
        if (R.Mode == "lightning") { R.Ends = DateTime.Now.AddSeconds(60); timer.Start(); }
        Next();
    }

    public void Closed() { timer.Stop(); Lg.StopSpeaking(); Profiles.Save(); }

    void Bar()
    {
        double frac = R.Mode == "lightning" ? Math.Max(0, (R.Ends - DateTime.Now).TotalSeconds) / 60 : (R.I + (R.Answered ? 1 : 0)) / (double)Math.Max(1, R.Items.Count);
        barHost.Children.Clear();
        barHost.Children.Add(Lg.Bar(Math.Clamp(frac, 0, 1), Ui.Accent, 10));
        score.Text = R.Score.ToString();
        combo.Text = R.Combo >= 2 ? "Combo x" + R.Combo : "";
    }

    public void Next()
    {
        if (done) return;
        R.I++;
        if (R.I >= R.Items.Count) { Finish(); return; }
        var item = R.Items[R.I]; var q = item.Q;
        R.Answered = false;
        mode.Text = (R.Mode == "lightning" ? "Lightning Round" : "Quiz Quest  |  question " + (R.I + 1) + " of " + R.Items.Count) + "  |  " + item.Topic;
        center.Children.Clear();
        var passage = Paper.Passage(q, 20); if (passage != null) center.Children.Add(passage);
        var qrow = new DockPanel { HorizontalAlignment = HorizontalAlignment.Center };
        var speak = Ui.IconButton("\U0001F50A", "Read it to me (R)"); speak.VerticalAlignment = VerticalAlignment.Top; speak.Margin = new Thickness(10, 0, 0, 0);
        speak.Click += (s, e) => ReadQuestion(true);
        DockPanel.SetDock(speak, Dock.Right);
        qrow.Children.Add(speak);
        var qt = Ui.Text(q.Q, q.Q.Length > 120 ? 22 : 28, Ui.TextC, true); qt.TextAlignment = TextAlignment.Center;
        qrow.Children.Add(qt);
        center.Children.Add(qrow);
        var code = Paper.Code(q.Code, 18); if (code != null) center.Children.Add(code);
        // choices, shuffled (true / false stays in order)
        var order = Enumerable.Range(0, q.Choices.Count).ToList();
        if (!(q.Choices.Count == 2 && q.Choices[0] == "True")) order = order.OrderBy(_ => Lg.Rand.Next()).ToList();
        choices.Children.Clear(); choiceButtons.Clear();
        choices.Columns = center.Bounds.Width > 0 && center.Bounds.Width < 520 ? 1 : 2;
        int k = 1;
        foreach (int i in order)
        {
            var tb = Ui.Text(k + ".  " + q.Choices[i], q.Choices[i].Length > 28 ? 17 : 20, Ui.TextC, true);
            var b = new Button { Content = tb, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(16, 12), Margin = new Thickness(6), MinHeight = 60, Background = Ui.Brush(Ui.Surface3), BorderBrush = Ui.Brush(Ui.BorderStrong), BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(12) };
            int idx = i;
            b.Click += (s, e) => Answer(idx);
            choices.Children.Add(b); choiceButtons.Add((b, i));
            k++;
        }
        center.Children.Add(choices);
        center.Children.Add(feedback);
        feedback.Content = null;
        next.IsVisible = false;
        hint.Text = Services.Platform.Touch ? "Tap an answer." : "Press 1-4 or click an answer.  R reads the question aloud.";
        Bar();
        if (R.ReadAloud) Lg.Speak(!string.IsNullOrEmpty(q.Say) ? q.Say : q.Q + ". " + string.Join(". ", q.Choices), q.Lang);
    }

    void ReadQuestion(bool withStory)
    {
        if (R.I < 0 || R.I >= R.Items.Count) return;
        var q = R.Items[R.I].Q;
        Lg.Speak((withStory && !string.IsNullOrEmpty(q.Passage) ? q.Passage + " " : "") + (!string.IsNullOrEmpty(q.Say) ? q.Say : q.Q), q.Lang);
    }

    public void Answer(int choice)
    {
        if (R.Answered || R.I < 0 || R.I >= R.Items.Count) return;
        R.Answered = true;
        var item = R.Items[R.I]; var q = item.Q; int right = q.Answer;
        bool ok = choice == right;
        foreach (var (b, i) in choiceButtons)
        {
            b.IsHitTestVisible = false;
            if (i == right) { b.Background = Ui.Brush("#14532D"); b.BorderBrush = Ui.Brush("#22C55E"); }
            else if (i == choice) { b.Background = Ui.Brush("#4C1D1D"); b.BorderBrush = Ui.Brush("#EF4444"); }
        }
        if (ok) { R.Right++; R.Combo++; R.BestCombo = Math.Max(R.BestCombo, R.Combo); R.Score += 10 + Math.Min(20, (R.Combo - 1) * 2); Snd.Correct(); }
        else { R.Combo = 0; R.Missed.Add(item); Snd.Wrong(); }
        R.Record(item, ok);
        Bar();
        MainView.Current.RefreshBar();
        if (R.Mode == "lightning")
        {
            // no explanations against the clock: a quick flash, then the next one (missed ones come back in review)
            nextAt = DateTime.Now.AddMilliseconds(ok ? 350 : 1100);
            return;
        }
        string[] praise = { "Correct!", "Nice!", "You got it!", "Great job!", "Exactly!", "Nailed it!" };
        string verdict = ok ? praise[Lg.Rand.Next(praise.Length)] + (R.Combo >= 3 ? "  " + R.Combo + " in a row!" : "") : "Not quite. It's: " + q.Right;
        var fb = new StackPanel();
        fb.Children.Add(Ui.Text(verdict, 21, ok ? "#4ADE80" : Ui.Red, true));
        if (!string.IsNullOrEmpty(q.Explain)) { var ex = Ui.Text(q.Explain, 16); ex.Margin = new Thickness(0, 6, 0, 0); fb.Children.Add(ex); }
        if (!string.IsNullOrEmpty(q.Tip)) fb.Children.Add(Paper.Tip(q.Tip));
        var card = Ui.Card(fb, Ui.Surface, 16); card.Margin = new Thickness(6, 12, 6, 0);
        feedback.Content = card;
        next.IsVisible = true;
        next.Content = R.I >= R.Items.Count - 1 ? "Finish" : "Next";
        hint.Text = Services.Platform.Touch ? "" : "Enter for the next question.";
        if (R.ReadAloud) Lg.Speak(verdict + " " + q.Explain + " " + q.Tip, q.Lang);
    }

    void Tick()
    {
        if (R.Mode != "lightning" || done) { timer.Stop(); return; }
        Bar();
        if (DateTime.Now >= R.Ends) { timer.Stop(); Finish(); return; }
        if (nextAt.HasValue && DateTime.Now >= nextAt.Value)
        {
            nextAt = null;
            // lightning keeps going: when the deck runs out, start over with a fresh shuffle
            if (R.I >= R.Items.Count - 1) { R.Items = R.Items.OrderBy(_ => Lg.Rand.Next()).ToList(); R.I = -1; }
            Next();
        }
    }

    /// <summary>For the checks: the lightning clock runs out now.</summary>
    public void EndNowForTest() { R.Ends = DateTime.Now.AddSeconds(-1); Tick(); }

    void Finish()
    {
        if (done) return;
        done = true;
        timer.Stop(); Lg.StopSpeaking();
        MainView.Current.Replace(new DonePage(R));
    }

    public bool OnKey(KeyEventArgs e)
    {
        int num = KeyNum(e.Key);
        if (num >= 0 && !R.Answered && num < choiceButtons.Count) { Answer(choiceButtons[num].Index); return true; }
        if ((e.Key == Key.Enter || e.Key == Key.Space) && R.Answered && R.Mode != "lightning") { Next(); return true; }
        if (e.Key == Key.R) { ReadQuestion(true); return true; }
        return false;
    }

    public static int KeyNum(Key k)
    {
        if (k >= Key.D1 && k <= Key.D4) return k - Key.D1;
        if (k >= Key.NumPad1 && k <= Key.NumPad4) return k - Key.NumPad1;
        return -1;
    }
}

// ================================================================ Flash Cards
public class CardsPage : IPage, IKeyPage
{
    readonly SubjectRound R;
    public string Title => R.Name;
    public Control View { get; }
    public Control Extras => Lg.TopExtras();
    readonly TextBlock top = Ui.Text("", 14, Ui.Muted);
    readonly StackPanel face = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly Border card;
    readonly Button flip = Ui.Button("Show answer", true, 17), notYet = Ui.Button("Not yet", false, 17), gotIt = Ui.Button("Got it!", true, 17);
    bool shown, done;

    public CardsPage(SubjectRound r)
    {
        R = r;
        card = new Border
        {
            Child = face, MaxWidth = 760, MinHeight = 300, Margin = new Thickness(0, 14), CornerRadius = new CornerRadius(22),
            Background = Ui.Brush(Ui.Surface), BorderBrush = Ui.Brush(Ui.BorderStrong), BorderThickness = new Thickness(2), Padding = new Thickness(28, 22),
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        card.PointerReleased += (s, e) => Flip();
        if (!Services.Platform.Touch) { flip.Content = "Show answer  (Space)"; notYet.Content = "Not yet  (1)"; gotIt.Content = "Got it!  (2)"; }
        flip.Click += (s, e) => Flip();
        notYet.Click += (s, e) => Grade(false);
        gotIt.Click += (s, e) => Grade(true);
        notYet.Margin = new Thickness(0, 0, 12, 0);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Children = { flip, notYet, gotIt } };
        var g = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(16, 12, 16, 16) };
        g.Children.Add(top);
        var sc = new ScrollViewer { Content = card, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(sc, 1); g.Children.Add(sc);
        Grid.SetRow(buttons, 2); g.Children.Add(buttons);
        View = g;
        Next();
    }

    public void Closed() { Lg.StopSpeaking(); Profiles.Save(); }

    void Next()
    {
        R.I++;
        if (R.I >= R.Items.Count) { Finish(); return; }
        var item = R.Items[R.I]; var q = item.Q;
        shown = false;
        top.Text = "Flash Cards  |  card " + (R.I + 1) + " of " + R.Items.Count + "  |  " + item.Topic + "  |  " + R.Right + " known";
        face.Children.Clear();
        var passage = Paper.Passage(q, 18); if (passage != null) face.Children.Add(passage);
        var side = Ui.Text("QUESTION", 15, Ui.Muted); side.HorizontalAlignment = HorizontalAlignment.Center; side.TextAlignment = TextAlignment.Center; side.Margin = new Thickness(0, 0, 0, 12);
        face.Children.Add(side);
        var txt = Ui.Text(q.Q, 26, Ui.TextC, true); txt.TextAlignment = TextAlignment.Center; face.Children.Add(txt);
        var code = Paper.Code(q.Code, 16); if (code != null) face.Children.Add(code);
        var ex = Ui.Text("Think of the answer, then flip the card.", 16, Ui.Muted); ex.TextAlignment = TextAlignment.Center; ex.Margin = new Thickness(0, 14, 0, 0); face.Children.Add(ex);
        card.BorderBrush = Ui.Brush(Ui.BorderStrong);
        flip.IsVisible = true; notYet.IsVisible = false; gotIt.IsVisible = false;
        if (R.ReadAloud) Lg.Speak(!string.IsNullOrEmpty(q.Say) ? q.Say : q.Q, q.Lang);
    }

    public void Flip()
    {
        if (shown || done || R.I >= R.Items.Count) return;
        shown = true;
        var q = R.Items[R.I].Q;
        face.Children.Clear();
        var side = Ui.Text(q.Q, 15, Ui.Muted); side.HorizontalAlignment = HorizontalAlignment.Center; side.TextAlignment = TextAlignment.Center; side.Margin = new Thickness(0, 0, 0, 12);   // the question stays in view above its answer
        face.Children.Add(side);
        var txt = Ui.Text(q.Right, 26, Ui.TextC, true); txt.TextAlignment = TextAlignment.Center; face.Children.Add(txt);
        if (!string.IsNullOrEmpty(q.Explain)) { var ex = Ui.Text(q.Explain, 16, Ui.Muted); ex.TextAlignment = TextAlignment.Center; ex.Margin = new Thickness(0, 14, 0, 0); face.Children.Add(ex); }
        if (!string.IsNullOrEmpty(q.Tip)) face.Children.Add(Paper.Tip(q.Tip, true));
        card.BorderBrush = Ui.Brush("#22C55E");
        flip.IsVisible = false; notYet.IsVisible = true; gotIt.IsVisible = true;
        if (R.ReadAloud) Lg.Speak(q.Right + ". " + q.Tip, q.Lang);
    }

    public void Grade(bool known)
    {
        if (!shown || done) return;
        var item = R.Items[R.I];
        if (known) { R.Right++; R.Score += 10; Snd.Pop(); } else R.Missed.Add(item);
        R.Record(item, known);
        MainView.Current.RefreshBar();
        Next();
    }

    void Finish() { if (done) return; done = true; Lg.StopSpeaking(); MainView.Current.Replace(new DonePage(R)); }

    public bool OnKey(KeyEventArgs e)
    {
        int num = QuizPage.KeyNum(e.Key);
        if (e.Key == Key.Space || e.Key == Key.Enter) { if (!shown) Flip(); else Grade(true); return true; }
        if (shown && num == 0) { Grade(false); return true; }
        if (shown && num == 1) { Grade(true); return true; }
        return false;
    }
}

// ================================================================ Memory Match
public class MatchPage : IPage
{
    readonly SubjectRound R;
    public string Title => R.Name;
    public Control View { get; }
    public Control Extras => Lg.TopExtras();
    readonly TextBlock moves = Ui.Text("Moves: 0", 18, Ui.TextC, true);
    readonly UniformGrid grid = new() { Columns = 4, Margin = new Thickness(0, 14, 0, 0), VerticalAlignment = VerticalAlignment.Top };
    sealed class Card { public int Pair; public string Text = "", Kind = ""; public RoundItem Item; public bool Done; public Button B; public TextBlock T; }
    readonly List<Card> cards = new();
    Card first;
    bool busy, finished;

    public MatchPage(SubjectRound r)
    {
        R = r;
        int pair = 0;
        foreach (var it in R.Items)
        {
            cards.Add(new Card { Pair = pair, Text = it.Q.Q, Kind = "q", Item = it });
            cards.Add(new Card { Pair = pair, Text = it.Q.Right, Kind = "a", Item = it });
            pair++;
        }
        R.Pairs = pair; R.Found = 0; R.Moves = 0;
        foreach (var c in cards.OrderBy(_ => Lg.Rand.Next()).ToList())
        {
            c.T = new TextBlock { Text = "?", FontSize = 34, FontWeight = FontWeight.Bold, Foreground = Ui.Brush(Ui.AccentText), TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            c.B = new Button { Content = c.T, Height = 120, Margin = new Thickness(5), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, CornerRadius = new CornerRadius(14), Background = Ui.Brush(Ui.Surface), BorderBrush = Ui.Brush(Ui.BorderStrong), BorderThickness = new Thickness(1.5) };
            var cc = c;
            c.B.Click += (s, e) => Flip(cc);
            grid.Children.Add(c.B);
        }
        var head = new DockPanel();
        DockPanel.SetDock(moves, Dock.Right);
        head.Children.Add(moves);
        head.Children.Add(Ui.Text("Flip two cards: find each question and its answer.", 15, Ui.Muted));
        var sp = new StackPanel { Children = { head, grid } };
        View = Ui.Page(sp);
        View.SizeChanged += (s, e) => grid.Columns = e.NewSize.Width < 560 ? 3 : 4;
    }

    public void Closed() { Profiles.Save(); }

    void Face(Card c, bool up)
    {
        if (up)
        {
            c.T.Text = c.Text;
            c.T.FontSize = c.Text.Length > 50 ? 13 : c.Text.Length > 22 ? 15 : 19;
            c.T.Foreground = Ui.Brush(c.Kind == "q" ? Ui.TextC : "#FDE68A");
            c.T.FontWeight = FontWeight.SemiBold;
        }
        else { c.T.Text = "?"; c.T.FontSize = 34; c.T.Foreground = Ui.Brush(Ui.AccentText); c.T.FontWeight = FontWeight.Bold; }
    }

    void Flip(Card c)
    {
        if (busy || c.Done || c == first || finished) return;
        Face(c, true); Snd.Pop();
        if (first == null) { first = c; return; }
        R.Moves++; moves.Text = "Moves: " + R.Moves;
        var a = first; first = null;
        if (a.Pair == c.Pair)
        {
            foreach (var x in new[] { a, c }) { x.Done = true; x.B.BorderBrush = Ui.Brush("#22C55E"); x.B.Background = Ui.Brush("#14532D"); x.B.IsHitTestVisible = false; }
            R.Found++; R.Right++; Snd.Correct();
            R.Record(a.Item, true);
            if (R.Found >= R.Pairs)
            {
                R.Score = Math.Max(10, 100 - 5 * (R.Moves - R.Pairs));
                finished = true;
                MainView.Current.Replace(new DonePage(R));
            }
        }
        else
        {
            busy = true;
            DispatcherTimer.RunOnce(() => { Face(a, false); Face(c, false); busy = false; }, TimeSpan.FromMilliseconds(1100));
        }
    }

    /// <summary>For the checks: finds every pair in as few moves as possible.</summary>
    public void SolveForTest()
    {
        foreach (var g in cards.GroupBy(c => c.Pair).ToList())
        {
            var two = g.ToList();
            Flip(two[0]); Flip(two[1]);
            if (finished) break;
        }
    }
}

// ================================================================ the end of a round
public class DonePage : IPage, IKeyPage
{
    readonly SubjectRound R;
    public string Title => R.Name;
    public Control View { get; }
    public Control Extras => Lg.TopExtras();
    public int Stars { get; }

    public DonePage(SubjectRound r)
    {
        R = r;
        var st = Lg.Me.Subject(R.Subject);
        int total = R.Mode == "match" ? R.Pairs : Math.Max(1, Math.Min(R.Items.Count, R.I + 1));
        double frac = R.Mode == "lightning" ? Math.Min(1.0, R.Right / 15.0) : R.Mode == "match" ? Math.Min(1.0, R.Pairs / (double)Math.Max(1, R.Moves)) * 1.15 : R.Right / (double)total;
        Stars = frac >= 0.9 ? 3 : frac >= 0.6 ? 2 : frac > 0 ? 1 : 0;
        var sp = new StackPanel();
        var stars = new TextBlock { Text = new string('★', Stars) + new string('☆', 3 - Stars), FontSize = 54, Foreground = Ui.Brush(Ui.Yellow), HorizontalAlignment = HorizontalAlignment.Center };
        sp.Children.Add(stars);
        var title = Ui.Text(Stars switch { 3 => "Amazing!", 2 => "Great work!", 1 => "Good practice!", _ => "Keep going!" }, 30, Ui.TextC, true);
        title.HorizontalAlignment = HorizontalAlignment.Center; title.Margin = new Thickness(0, 10, 0, 0);
        sp.Children.Add(title);
        string txt = R.Mode switch
        {
            "lightning" => R.Right + " right in 60 seconds  |  best combo " + R.BestCombo,
            "match" => "All " + R.Pairs + " pairs in " + R.Moves + " moves",
            "cards" => "You knew " + R.Right + " of " + total + " cards. The others come back soon for another look.",
            _ => R.Right + " of " + total + " right  |  score " + R.Score + "  |  best combo " + R.BestCombo,
        };
        if (R.LevelChange == "up") txt += "\n\nLevel up! Next time: " + Bank.LevelNames[st.Level] + ".";
        else if (R.LevelChange == "down") txt += "\n\nNext time we'll practice at " + Bank.LevelNames[st.Level] + " to build up.";
        var t = Ui.Text(txt, 16, Ui.Muted); t.TextAlignment = TextAlignment.Center; t.Margin = new Thickness(0, 8, 0, 0);
        sp.Children.Add(t);
        // what to remember: the missed questions with their memory tricks
        var missed = R.Missed.Take(4).ToList();
        if (missed.Count > 0 && R.Mode != "match")
        {
            var h = Ui.Text("Remember these:", 15, Ui.TextC, true); h.Margin = new Thickness(0, 16, 0, 6); sp.Children.Add(h);
            foreach (var m in missed)
            {
                var l = Ui.Text(m.Q.Q + "  ->  " + m.Q.Right + (string.IsNullOrEmpty(m.Q.Tip) ? "" : "\n    " + m.Q.Tip), 13.5, "#C7CDDA");
                l.Margin = new Thickness(0, 0, 0, 8);
                sp.Children.Add(l);
            }
        }
        int prev = st.Best(R.Mode);
        int val = R.Mode == "lightning" ? R.Right : R.Score;
        if (val > prev) st.SetBest(R.Mode, val);
        if (Stars >= 2) Snd.Win();
        Profiles.Save();
        var back = Ui.Button("Back", false, 16); back.Margin = new Thickness(0, 0, 10, 0);
        back.Click += (s, e) => MainView.Current.Back();
        var again = Ui.Button("Play again", true, 16);
        again.Click += (s, e) => Again();
        sp.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 22, 0, 0), Children = { back, again } });
        var card = Ui.Card(sp, Ui.Surface, 28);
        card.MaxWidth = 720;
        View = Ui.Page(card, 760);
    }

    void Again()
    {
        MainView.Current.Back();
        if (MainView.Current.Page is SubjectPage sp) sp.Start(R.Mode, R.Topic);
    }

    public bool OnKey(KeyEventArgs e) { if (e.Key == Key.Enter) { Again(); return true; } return false; }
}
