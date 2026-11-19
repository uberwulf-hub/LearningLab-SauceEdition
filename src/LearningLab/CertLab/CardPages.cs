// Cert Lab (Learning Lab) - flash cards (App Hub's UiCards.cs: decks per objective, Leitner boxes, flip or type the
// answer; read aloud: the front, a moment to think, the back) and cheat sheets (one per objective or all in one,
// searchable, Copy). Printing stayed behind: Copy puts the sheet on the clipboard as text.
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using CertLab;
using LearningLab.Views;

namespace LearningLab.CertLab;

public sealed class CardsPage : CertPage, IReadable
{
    List<Card> session;
    int pos;
    bool typeMode, revealed, showingCard;
    Deck deck;
    Control frontView, answerView;
    public override string Title => "Flash cards";
    public TextBox AnswerBox { get; private set; }

    public CardsPage(CertApp app) : base(app) { ShowDecks(); AddReadBar(this); }

    public void ShowDecks()
    {
        showingCard = false;
        var root = new StackPanel();
        root.Children.Add(CertUi.H1("Flash cards"));
        root.Children.Add(CertUi.Muted("Cards you know move up a box and come back after longer and longer (1, 2, 4, 8, then 16 days); a card you miss goes back to box 1. Flip shows the answer; Type it asks you to type the command."));
        var c = A.Course;
        var pr = A.Progress;
        long today = Leitner.Today;
        var all = new Deck { Id = "all", Title = "All decks", Cards = c.Decks.SelectMany(d => d.Cards).ToList() };
        foreach (var d in new[] { all }.Concat(c.Decks))
        {
            var d0 = d;
            int due = Leitner.Session(d.Cards, pr, today, 0).Count, fresh = d.Cards.Count(x => !pr.Cards.ContainsKey(x.Id));
            int mastered = d.Cards.Count(x => pr.Cards.TryGetValue(x.Id, out var st) && st.Box >= 4);
            var sp = new StackPanel();
            sp.Children.Add(Ui.Text(d.Title, 16, Ui.TextC, true));
            sp.Children.Add(CertUi.Muted(d.Cards.Count + " cards  -  " + due + " due, " + fresh + " new, " + mastered + " well known" + (d.Objective.Length > 0 ? "  -  " + d.Objective : "")));
            sp.Children.Add(CertUi.Row(CertUi.Accent("Flip", () => Start(d0, false)), CertUi.Btn("Type it", () => Start(d0, true))));
            root.Children.Add(CertUi.Pane(sp, 12));
        }
        Body = Ui.Page(root, 1000);
    }

    public void Start(Deck d, bool type)
    {
        deck = d; typeMode = type;
        var pr = A.Progress;
        session = Leitner.Session(d.Cards, pr, Leitner.Today, 20);
        if (session.Count == 0) session = d.Cards.OrderBy(x => pr.Cards.TryGetValue(x.Id, out var st) ? st.Box : 0).Take(10).ToList();
        pos = 0;
        ShowCard();
    }

    /// <summary>The deck, starting at this card (search opens a card this way).</summary>
    public void ShowAt(Deck d, Card c)
    {
        deck = d; typeMode = false;
        int i = Math.Max(0, d.Cards.IndexOf(c));
        session = d.Cards.Skip(i).Concat(d.Cards.Take(i)).ToList();
        pos = 0;
        ShowCard();
    }
    public Card CurrentCard => session != null && pos < session.Count ? session[pos] : null;
    public bool Revealed => revealed;

    public ReadDoc ReadContent(bool readCode)
    {
        var card = CurrentCard;
        var d = new ReadDoc { Title = deck?.Title ?? "Flash cards" };
        if (!showingCard || card == null)
        {
            d.Add("Flash cards. " + A.Course.Decks.Count + " decks.", null, "h");
            return d;
        }
        d.Add(CertUi.Plain(card.Front), frontView, "front");
        d.Units.Add(new ReadUnit { Text = ReadAloud.Pause(A.Speech.CardPauseMs), Element = frontView, Kind = "pause" });
        d.Add(CertUi.Plain(card.Back), answerView, "back");
        return d;
    }

    public void ShowCard()
    {
        revealed = false;
        AnswerBox = null;
        if (pos >= session.Count) { ShowDecks(); A.Status("That's all for now. Well done."); return; }
        showingCard = true;
        var card = session[pos];
        var pr = A.Progress;
        pr.Cards.TryGetValue(card.Id, out var st);
        var root = new StackPanel();
        root.Children.Add(CertUi.Muted(deck.Title + "  -  card " + (pos + 1) + " of " + session.Count + "  -  " + (st == null ? "new" : "box " + st.Box)));
        var front = CertUi.Rich(card.Front, 20);
        var answer = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        var face = new StackPanel { Children = { front, answer } };
        frontView = front; answerView = answer;
        root.Children.Add(CertUi.Pane(face, 22));
        var buttons = new WrapPanel();
        void Grade(bool right)
        {
            if (!pr.Cards.TryGetValue(card.Id, out var s)) { s = new CardState(); pr.Cards[card.Id] = s; }
            Leitner.Answer(s, right, Leitner.Today);
            A.SaveProgress();
            pos++;
            ShowCard();
        }
        void Reveal()
        {
            if (revealed) return;
            revealed = true;
            answer.Children.Add(CertUi.Rich(card.Back, 16));
            buttons.Children.Clear();
            if (typeMode && AnswerBox != null)
            {
                bool ok = Leitner.Matches(AnswerBox.Text ?? "", card);
                var r = Ui.Text(ok ? "✔ Right." : "✘ You typed: " + AnswerBox.Text, 15, ok ? Ui.Green : Ui.Yellow); r.Margin = new Thickness(0, 8, 0, 0);
                answer.Children.Add(r);
                buttons.Children.Add(CertUi.Accent("Next card", () => Grade(ok)));
                if (!ok) buttons.Children.Add(CertUi.Btn("I was right really", () => Grade(true)));
                return;
            }
            buttons.Children.Add(CertUi.Accent("I knew it", () => Grade(true)));
            buttons.Children.Add(CertUi.Btn("Not yet", () => Grade(false)));
        }
        revealAction = Reveal;
        if (typeMode)
        {
            AnswerBox = new TextBox { FontFamily = CertUi.Mono, FontSize = 16, MinHeight = 44, Margin = new Thickness(0, 0, 0, 8), PlaceholderText = "Your answer", VerticalContentAlignment = VerticalAlignment.Center };
            Avalonia.Input.TextInput.TextInputOptions.SetAutoCapitalization(AnswerBox, false);
            AnswerBox.KeyDown += (s, e) => { if (e.Key == Key.Enter) { CertUi.Safe(Reveal); e.Handled = true; } };
            root.Children.Add(AnswerBox);
            buttons.Children.Add(CertUi.Accent("Check", Reveal));
        }
        else buttons.Children.Add(CertUi.Accent("Show the answer", Reveal));
        buttons.Children.Add(CertUi.Btn("Stop", ShowDecks));
        root.Children.Add(buttons);
        Body = Ui.Page(root, 760);
    }
    Action revealAction;
    public void Reveal() => revealAction?.Invoke();
}

public sealed class CheatPage : CertPage, IReadable
{
    readonly TextBox search;
    readonly ComboBox pick;
    readonly ContentControl body = new();
    Sheet current;
    readonly Dictionary<SheetRow, Control> rowViews = new();
    public override string Title => "Cheat sheets";

    public CheatPage(CertApp app) : base(app)
    {
        var top = new StackPanel { Margin = new Thickness(16, 10, 16, 0), Spacing = 6 };
        pick = new ComboBox { MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Stretch };
        pick.Items.Add(new ComboBoxItem { Content = "All in one (quick reference)", MinHeight = 44 });
        foreach (var sh in A.Course.Sheets) pick.Items.Add(new ComboBoxItem { Content = sh.Title, Tag = sh, MinHeight = 44 });
        pick.SelectedIndex = 0;
        pick.SelectionChanged += (s, e) => CertUi.Safe(() => { current = (pick.SelectedItem as ComboBoxItem)?.Tag as Sheet; Render(); });
        search = new TextBox { MinHeight = 44, PlaceholderText = "Search every sheet", VerticalContentAlignment = VerticalAlignment.Center };
        search.PropertyChanged += (s, e) => { if (e.Property == TextBox.TextProperty) CertUi.Safe(Render); };
        top.Children.Add(pick);
        var row = new DockPanel();
        var copy = CertUi.Btn("Copy", () => A.Status(ClipText.SetText(PlainText(Query)) ? "Copied the sheet." : "The clipboard is busy. Try again."));
        copy.Margin = new Thickness(8, 0, 0, 0);
        DockPanel.SetDock(copy, Dock.Right);
        row.Children.Add(copy); row.Children.Add(search);
        top.Children.Add(row);
        var dock = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        dock.Children.Add(top);
        dock.Children.Add(body);
        Body = dock;
        Render();
        AddReadBar(this);
    }

    string Query => (search.Text ?? "").Trim();
    public void Search(string s) { search.Text = s; Render(); }
    public int Hits { get; private set; }

    public ReadDoc ReadContent(bool readCode)
    {
        var d = new ReadDoc();
        d.Add(current != null ? current.Title : "Quick reference", null, "h");
        foreach (var kv in rowViews) d.Add(kv.Key.Cmd + ": " + CertUi.Plain(kv.Key.Meaning), kv.Value, "row");
        return d;
    }

    /// <summary>Shows a sheet and lights up one of its rows (search opens a cheat row this way).</summary>
    public void Reveal(Sheet sh, int section, int row)
    {
        search.Text = "";
        for (int i = 0; i < pick.Items.Count; i++) if ((pick.Items[i] as ComboBoxItem)?.Tag == sh) pick.SelectedIndex = i;
        if (current != sh) { current = sh; }
        Render();
        if (section < 0 || section >= sh.Sections.Count || row < 0 || row >= sh.Sections[section].Rows.Count) return;
        if (rowViews.TryGetValue(sh.Sections[section].Rows[row], out var el)) RevealUi.Reveal(el, A.Quiet);
    }

    IEnumerable<Sheet> Sheets => current != null ? new[] { current } : A.Course.Sheets.ToArray();

    void Render()
    {
        string q = Query;
        var root = new StackPanel();
        root.Children.Add(CertUi.H1(q.Length > 0 ? "Search: " + q : current != null ? current.Title : "Quick reference"));
        rowViews.Clear();
        int hits = 0;
        var sheets = q.Length > 0 ? A.Course.Sheets.ToArray() : Sheets;
        foreach (var sh in sheets)
        {
            bool headerShown = false;
            foreach (var sec in sh.Sections)
            {
                var rows = sec.Rows.Where(r => q.Length == 0 || r.Cmd.Contains(q, StringComparison.OrdinalIgnoreCase) || r.Meaning.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
                if (rows.Count == 0) continue;
                if (!headerShown && (current == null || q.Length > 0)) { root.Children.Add(CertUi.H2(sh.Title)); headerShown = true; }
                var sp = new StackPanel();
                if (sec.Title.Length > 0) sp.Children.Add(Ui.Text(sec.Title, 15, Ui.AccentText, true));
                foreach (var r in rows)
                {
                    hits++;
                    var cmd = new SelectableTextBlock { Text = r.Cmd, FontFamily = CertUi.Mono, FontSize = 14, Foreground = Ui.Brush(CertUi.Code), TextWrapping = TextWrapping.Wrap };
                    var mean = CertUi.RichText(r.Meaning, 14);
                    mean.Foreground = Ui.Brush(Ui.Muted);
                    var g = new StackPanel { Margin = new Thickness(0, 4, 0, 4), Children = { cmd, mean } };
                    var b = new Border { Child = g, Padding = new Thickness(6, 2), CornerRadius = new CornerRadius(6) };
                    sp.Children.Add(b);
                    rowViews[r] = b;
                }
                root.Children.Add(CertUi.Pane(sp, 12));
            }
        }
        Hits = hits;
        if (hits == 0) root.Children.Add(CertUi.P(q.Length > 0 ? "Nothing matches \"" + q + "\"." : "This sheet is empty."));
        body.Content = Ui.Page(root, 1000);
    }

    public string PlainText(string q)
    {
        var sb = new StringBuilder();
        foreach (var sh in q.Length > 0 ? A.Course.Sheets.ToArray() : Sheets)
        {
            sb.Append(sh.Title).Append('\n').Append(new string('=', sh.Title.Length)).Append('\n');
            foreach (var sec in sh.Sections)
            {
                var rows = sec.Rows.Where(r => q.Length == 0 || r.Cmd.Contains(q, StringComparison.OrdinalIgnoreCase) || r.Meaning.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
                if (rows.Count == 0) continue;
                if (sec.Title.Length > 0) sb.Append('\n').Append(sec.Title).Append('\n');
                int w = Math.Min(50, rows.Max(r => r.Cmd.Length) + 2);
                foreach (var r in rows) sb.Append("  ").Append(r.Cmd.PadRight(w)).Append(r.Meaning.Replace("`", "")).Append('\n');
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }
}
