// Cert Lab (Learning Lab) - search (App Hub's UiSearch.cs): the results page (grouped by kind, each with where it is and
// a snippet with the matches marked) and opening a result at its exact spot: the lesson block lit up, the flash card,
// the cheat sheet row, a lab's or exam's task (TaskSetReviewPage), or a manual page at its section (ManViewPage).
// Exam explanations and model solutions are secret until the person has taken that exam (SearchIndex.Allows).
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using CertLab;
using LearningLab.Views;

namespace LearningLab.CertLab;

public sealed class SearchPage : CertPage
{
    public List<SearchHit> Hits = new();
    public string Query { get; private set; } = "";
    readonly HashSet<string> expanded = new();
    readonly TextBox box;
    readonly ContentControl results = new();
    const int PerGroup = 6;
    public override string Title => "Search";
    public readonly List<Button> Rows = new();

    public SearchPage(CertApp app, string q) : base(app)
    {
        box = new TextBox { MinHeight = 44, PlaceholderText = "Search the course", Text = q ?? "", VerticalContentAlignment = VerticalAlignment.Center };
        box.KeyDown += (s, e) => { if (e.Key == Key.Enter) { e.Handled = true; Run(box.Text); } };
        var go = CertUi.Btn("Search", () => Run(box.Text)); go.Margin = new Thickness(8, 0, 0, 0);
        var row = new DockPanel { Margin = new Thickness(16, 10, 16, 0) };
        DockPanel.SetDock(go, Dock.Right);
        row.Children.Add(go); row.Children.Add(box);
        var dock = new DockPanel();
        DockPanel.SetDock(row, Dock.Top);
        dock.Children.Add(row);
        dock.Children.Add(results);
        Body = dock;
        Run(q);
    }

    public void Run(string q)
    {
        if (q != Query) expanded.Clear();
        Query = q ?? "";
        if (box.Text != Query) box.Text = Query;
        Rows.Clear();
        var root = new StackPanel();
        var ix = A.SearchIdx;
        root.Children.Add(CertUi.H1(Query.Trim().Length == 0 ? "Search" : "Search: " + Query.Trim()));
        Hits = Query.Trim().Length == 0 || ix == null ? new List<SearchHit>() : ix.Find(Query, SearchIndex.Allows(A.TakenExams), 300);
        if (Query.Trim().Length == 0)
        {
            root.Children.Add(CertUi.Muted("Search looks through the lessons, worked examples, try-its, quizzes, labs, practice exams, flash cards, cheat sheets and manual pages of " + A.Course.Short + "."));
            root.Children.Add(CertUi.Muted("Tips: \"quoted words\" must appear together; names such as rd.break or /etc/fstab work as they are; endings do not matter (mount finds mounting). Explanations and model solutions of a practice exam show up once you have taken it."));
        }
        else if (Hits.Count == 0) root.Children.Add(CertUi.P("Nothing in the course matches that. Try fewer or different words."));
        else
        {
            root.Children.Add(CertUi.Muted(Hits.Count + (Hits.Count == 300 ? "+" : "") + " places, best first in each group. Tap one to open it there."));
            foreach (var kind in SearchIndex.KindOrder)
            {
                var hs = Hits.Where(h => h.Doc.Kind == kind).ToList();
                if (hs.Count == 0) continue;
                root.Children.Add(CertUi.H2(SearchIndex.KindTitle(kind) + "  (" + hs.Count + ")"));
                bool all = expanded.Contains(kind);
                foreach (var h in all ? hs : hs.Take(PerGroup)) root.Children.Add(Row(h));
                if (!all && hs.Count > PerGroup)
                {
                    var k = kind;
                    root.Children.Add(CertUi.Btn("Show " + (hs.Count - PerGroup) + " more", () => { expanded.Add(k); Run(Query); }));
                }
            }
        }
        results.Content = Ui.Page(root, 1000);
    }

    Control Row(SearchHit h)
    {
        var sp = new StackPanel();
        sp.Children.Add(Ui.Text(h.Doc.Context, 13.5, Ui.AccentText, true));
        var snip = new TextBlock { FontSize = 14, Foreground = Ui.Brush(Ui.TextC), TextWrapping = TextWrapping.Wrap, Inlines = new InlineCollection() };
        if (h.Doc.Kind == "example" || h.Doc.Kind == "man") snip.FontFamily = CertUi.Mono;
        int at = 0;
        foreach (var m in h.Marks.OrderBy(x => x[0]))
        {
            if (m[0] < at || m[0] + m[1] > h.Snippet.Length) continue;
            if (m[0] > at) snip.Inlines.Add(new Run(h.Snippet.Substring(at, m[0] - at)));
            snip.Inlines.Add(new Run(h.Snippet.Substring(m[0], m[1])) { Background = Ui.Brush(Ui.Accent), Foreground = Brushes.White, FontWeight = FontWeight.SemiBold });
            at = m[0] + m[1];
        }
        if (at < h.Snippet.Length) snip.Inlines.Add(new Run(h.Snippet.Substring(at)));
        sp.Children.Add(snip);
        var b = new Button { VerticalContentAlignment = VerticalAlignment.Center, Content = sp, MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Background = Ui.Brush(Ui.Surface), BorderBrush = Ui.Brush(Ui.BorderC), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 8), Margin = new Thickness(0, 0, 0, 8), Tag = h };
        b.Click += (s, e) => CertUi.Safe(() => Open(h.Doc));
        Rows.Add(b);
        return b;
    }

    /// <summary>Opens a result at its spot; returns the page it opened.</summary>
    public IPage Open(SearchDoc d)
    {
        var c = A.Course;
        switch (d.Kind)
        {
            case "lesson": case "example": case "try": case "quiz":
                {
                    var l = c.Lesson(d.LessonId);
                    if (l == null) return null;
                    var lp = new LessonPage(A, l);
                    Mv.Push(lp);
                    lp.Reveal(d.Part, d.Index);
                    return lp;
                }
            case "lab": case "exam":
                {
                    var pset = d.Kind == "lab" ? c.Practice.FirstOrDefault(x => x.Id == d.SetId) : null;
                    if (pset != null) { var pp = new PbqPracticePage(A, pset, d.ItemId); Mv.Push(pp); return pp; }
                    var set = (d.Kind == "lab" ? c.Labs : c.Exams).FirstOrDefault(x => x.Id == d.SetId);
                    if (set == null) return null;
                    bool review = d.Kind == "lab" || A.TakenExams.Contains(set.Id);
                    var page = new TaskSetReviewPage(A, set, d.Kind == "exam", review, Query);
                    Mv.Push(page);
                    page.Reveal(d.ItemId);
                    return page;
                }
            case "card":
                {
                    var deck = c.Decks.FirstOrDefault(x => x.Id == d.DeckId);
                    var card = deck?.Cards.FirstOrDefault(x => x.Id == d.CardId);
                    if (card == null) return null;
                    var cp = new CardsPage(A);
                    Mv.Push(cp);
                    cp.ShowAt(deck, card);
                    return cp;
                }
            case "cheat":
                {
                    var sh = c.Sheets.FirstOrDefault(x => x.Id == d.SheetId);
                    if (sh == null) return null;
                    var cp = new CheatPage(A);
                    Mv.Push(cp);
                    cp.Reveal(sh, d.Section, d.Row);
                    return cp;
                }
            case "man":
                {
                    string f = Path.Combine(c.Dir, "man", d.Man);
                    if (!File.Exists(f)) return null;
                    var page = new ManViewPage(A, f, Query);
                    Mv.Push(page);
                    page.Reveal(d.Section);
                    return page;
                }
        }
        return null;
    }

    /// <summary>Text with the matches of rx marked.</summary>
    public static SelectableTextBlock Highlighted(string text, Regex rx, double size, bool mono)
    {
        var tb = new SelectableTextBlock { FontSize = size, Foreground = Ui.Brush(Ui.TextC), TextWrapping = TextWrapping.Wrap, Inlines = new InlineCollection() };
        if (mono) tb.FontFamily = CertUi.Mono;
        int at = 0;
        if (rx != null)
            foreach (Match m in rx.Matches(text))
            {
                if (m.Length == 0) continue;
                if (m.Index > at) tb.Inlines.Add(new Run(text.Substring(at, m.Index - at)));
                tb.Inlines.Add(new Run(m.Value) { Background = Ui.Brush(Ui.Accent), Foreground = Brushes.White, FontWeight = FontWeight.SemiBold });
                at = m.Index + m.Length;
            }
        if (at < text.Length) tb.Inlines.Add(new Run(text.Substring(at)));
        return tb;
    }
}

/// <summary>A lab's or exam's tasks, read-only (search opens them here). An exam's explanations and model solutions show
/// only once the person has taken it.</summary>
public sealed class TaskSetReviewPage : CertPage
{
    public readonly bool Review;
    readonly Dictionary<string, Control> items = new();
    readonly string title;
    public override string Title => title;
    public string AllText { get; }

    public TaskSetReviewPage(CertApp app, TaskSet set, bool exam, bool review, string query) : base(app)
    {
        Review = review; title = set.Title;
        var rx = string.IsNullOrEmpty(query) ? null : SearchIndex.HighlightRegex(query);
        var all = new System.Text.StringBuilder();
        var root = new StackPanel();
        root.Children.Add(CertUi.Muted(exam ? "Practice exam" : "Lab"));
        root.Children.Add(CertUi.H1(set.Title));
        if (exam) root.Children.Add(CertUi.Accent("Go to the practice exams", () => Mv.Push(new ExamsPage(A))));
        else root.Children.Add(CertUi.Accent("Start this lab", () => LabsPage.StartLab(A, set)));
        if (set.Intro.Length > 0) root.Children.Add(CertUi.Muted(SearchIndex.Fill(set, set.Intro)));
        if (exam && !review) root.Children.Add(CertUi.Pane(CertUi.P("The explanations and model solutions of this exam show here once you have taken it, so they cannot spoil it."), 12));
        if (exam) root.Children.Add(CertUi.Muted("Names and numbers change from one attempt to the next; these are one example."));
        int n = 0;
        string lastCase = "";
        foreach (var it in set.Items)
        {
            n++;
            var sp = new StackPanel();
            if (it.Case.Length > 0 && it.Case != lastCase && set.CaseOf(it) != null) root.Children.Add(CertUi.Pane(ExamItems.CaseView(set.CaseOf(it)), 14));
            lastCase = it.Case;
            sp.Children.Add(CertUi.Muted((it.IsQuestion ? "Question " + n + "  -  " + ExamItems.KindLabel(it) : "Task " + n + "  -  " + TaskRunner.MachineLabel(it.Machine)) + "  -  " + it.Objective + (exam ? "  -  " + it.Points + " points" : "")));
            string text = SearchIndex.Fill(set, it.Text).Replace("`", "");
            all.Append(text).Append('\n');
            sp.Children.Add(SearchPage.Highlighted(text, rx, 15, false));
            if (it.IsQuestion)
            {
                foreach (var ch in it.Choices) { var t = SearchPage.Highlighted("  -  " + SearchIndex.Fill(set, ch).Replace("`", ""), rx, 14, false); t.Margin = new Thickness(0, 3, 0, 0); sp.Children.Add(t); }
                if (review)
                {
                    if (it.Kind == "choice" && it.Answer >= 0) sp.Children.Add(CertUi.Muted("Right answer: " + it.Choices[it.Answer].Replace("`", "")));
                    else sp.Children.Add(ExamReview.Answer(it, null));
                }
            }
            if (review)
            {
                foreach (var h in it.Hints) { var t = SearchPage.Highlighted("Hint: " + SearchIndex.Fill(set, h), rx, 13.5, false); t.Foreground = Ui.Brush(Ui.Muted); t.Margin = new Thickness(0, 6, 0, 0); sp.Children.Add(t); all.Append(h).Append('\n'); }
                if (it.Explain.Length > 0) { var ex = SearchIndex.Fill(set, it.Explain).Replace("`", ""); var t = SearchPage.Highlighted(ex, rx, 14, false); t.Margin = new Thickness(0, 8, 0, 0); sp.Children.Add(t); all.Append(ex).Append('\n'); }
                var sol = it.Solution.GroupBy(x => x.Machine).Select(g => (set.Machines > 1 || exam ? TaskRunner.Where(g.Key) + "\n" : "") + TaskRunner.SolutionBlock(g)).ToArray();
                if (sol.Length > 0) { var st = SearchIndex.Fill(set, string.Join("\n\n", sol)); sp.Children.Add(CertUi.TermBlock(st)); all.Append(st).Append('\n'); }
            }
            var pane = CertUi.Pane(sp, 14);
            items[it.Id] = pane;
            root.Children.Add(pane);
        }
        AllText = all.ToString();
        Body = Ui.Page(root, 1000);
    }

    public void Reveal(string itemId) { if (items.TryGetValue(itemId, out var el)) RevealUi.Reveal(el, A.Quiet); }
}

/// <summary>A manual page (as `man` shows it on the practice machines), opened at a section.</summary>
public sealed class ManViewPage : CertPage
{
    readonly List<Control> sections = new();
    readonly string title;
    public override string Title => title;

    public ManViewPage(CertApp app, string file, string query) : base(app)
    {
        var rx = string.IsNullOrEmpty(query) ? null : SearchIndex.HighlightRegex(query);
        string name = Path.GetFileName(file);
        int dot = name.LastIndexOf('.');
        title = dot > 0 ? name.Substring(0, dot) + "(" + name.Substring(dot + 1) + ")" : name;
        var root = new StackPanel();
        root.Children.Add(CertUi.Muted("Manual page (on the practice machines: man " + (dot > 0 ? name.Substring(dot + 1) + " " + name.Substring(0, dot) : name) + ")"));
        root.Children.Add(CertUi.H1(title));
        foreach (var s in SearchIndex.ManSections(File.ReadAllText(file)))
        {
            var sp = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            if (s.Key.Length > 0) sp.Children.Add(Ui.Text(s.Key, 15, Ui.AccentText, true));
            sp.Children.Add(SearchPage.Highlighted(s.Value, rx, 13.5, true));
            var b = new Border { Child = sp, Padding = new Thickness(8, 4, 8, 6), CornerRadius = new CornerRadius(6) };
            sections.Add(b);
            root.Children.Add(b);
        }
        Body = Ui.Page(root, 1000);
    }

    public void Reveal(int section) { if (section >= 0 && section < sections.Count) RevealUi.Reveal(sections[section], A.Quiet); }
}
