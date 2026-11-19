// Cert Lab (Learning Lab) - performance-based questions (App Hub's UiPbq.cs, UiPbq2.cs, UiPbqPractice.cs), touch first:
// exhibits and logs (tap the log lines that show the problem), sub-questions, number boxes for calculations, matching
// (tap a card, then the item it belongs to), the firewall rule table (one card per rule with its drop-downs, Up / Down /
// Clear) and network placement (tap a device, then its zone). Every control writes "key=value" answers into the item's
// answer list (PbqGrade.cs reads them); Load(answers) puts an answer back. PBQ practice: the sets of a course and a
// set's page, one scenario at a time with Check, Show the answer and Try again.
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using CertLab;
using LearningLab.Views;

namespace LearningLab.CertLab;

public sealed class PbqView : ContentControl
{
    public readonly TaskItem It;
    public readonly List<string> Ans;
    readonly Action changed;
    string picked;   // the card / device tapped first (matching and placement)

    public PbqView(TaskItem it, List<string> ans, Action onChanged)
    {
        It = it; Ans = ans ?? new List<string>(); changed = onChanged;
        Rebuild();
    }
    public void Load(List<string> answer) { Ans.Clear(); if (answer != null) Ans.AddRange(answer); Rebuild(); changed?.Invoke(); }
    void Put(string key, string value) { PbqGrade.Set(Ans, key, value); changed?.Invoke(); }

    public static string KindLabel(string kind) => kind switch
    {
        "match" => "PBQ: matching",
        "firewall" => "PBQ: firewall rules",
        "logs" => "PBQ: log analysis",
        "place" => "PBQ: network placement",
        "cert" => "PBQ: certificates",
        "risk" => "PBQ: risk calculation",
        _ => "PBQ",
    };

    void Rebuild()
    {
        var s = It.Pbq;
        var sp = new StackPanel();
        if (s == null) { Content = sp; return; }
        sp.Children.Add(CertUi.QuestionView(s.Prompt));
        for (int e = 0; e < s.Exhibits.Count; e++) sp.Children.Add(Exhibit(s, e));
        if (s.Pairs.Count > 0) sp.Children.Add(MatchView(s));
        if (s.Tests.Count > 0) sp.Children.Add(FirewallView(s));
        if (s.Devices.Count > 0) sp.Children.Add(PlaceView(s));
        foreach (var c in s.Calcs) sp.Children.Add(CalcView(c));
        foreach (var q in s.Questions) sp.Children.Add(QuestionView(q));
        sp.Children.Add(CertUi.Muted("Partial credit: each part is worth " + (PbqGrade.PartCount(s) == 1 ? "all" : "1/" + PbqGrade.PartCount(s)) + " of this question's points."));
        Content = sp;
    }

    Control Exhibit(PbqSpec s, int e)
    {
        var ex = s.Exhibits[e];
        var sp = new StackPanel { Margin = new Thickness(0, 8, 0, 4) };
        sp.Children.Add(Ui.Text((ex.IsLog ? "Log: " : "Exhibit: ") + ex.Title, 15, Ui.AccentText, true));
        bool marking = ex.IsLog && s.IndicatorCount > 0;
        if (marking) sp.Children.Add(CertUi.Muted("Tap the lines that show the problem (tap again to unmark)."));
        var lines = new StackPanel { Background = Ui.Brush(CertUi.TermBg) };
        if (!marking) lines.Children.Add(new SelectableTextBlock { Text = string.Join("\n", ex.Lines), FontFamily = CertUi.Mono, FontSize = 12.5, Foreground = Ui.Brush("#D6DEEB"), Padding = new Thickness(8, 4) });
        else
            for (int i = 0; i < ex.Lines.Count; i++)
            {
                int li = i;
                var tb = new TextBlock { Text = ex.Lines[i].Length == 0 ? " " : ex.Lines[i], FontFamily = CertUi.Mono, FontSize = 12.5, Foreground = Ui.Brush("#D6DEEB"), Padding = new Thickness(8, 2), VerticalAlignment = VerticalAlignment.Center };
                var row = new Button { VerticalContentAlignment = VerticalAlignment.Center, Content = tb, MinHeight = 36, Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(0), BorderThickness = new Thickness(3, 0, 0, 0), Tag = "line:" + e + ":" + i };
                void Paint() { bool on = PbqGrade.Marked(Ans, e, li); row.Background = on ? Ui.Brush(Ui.AccentSoft) : Brushes.Transparent; row.BorderBrush = on ? Ui.Brush(Ui.Accent) : Brushes.Transparent; }
                Paint();
                row.Click += (o, a) => CertUi.Safe(() =>
                {
                    string key = "l:" + e + ":" + li;
                    if (Ans.Contains(key)) Ans.Remove(key); else Ans.Add(key);
                    Paint(); changed?.Invoke();
                });
                lines.Children.Add(row);
            }
        var sv = new ScrollViewer { Content = lines, MaxHeight = 320, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        sp.Children.Add(new Border { Child = sv, BorderBrush = Ui.Brush(Ui.BorderC), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4) });
        return sp;
    }

    Control QuestionView(PbqQuestion q)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 10, 0, 2) };
        var head = CertUi.RichText(q.Text + (q.Multi && q.Text.IndexOf("hoose", StringComparison.Ordinal) < 0 && q.Text.IndexOf("pick", StringComparison.OrdinalIgnoreCase) < 0 ? "  (pick " + q.Right.Count + ")" : ""));
        head.FontWeight = FontWeight.SemiBold;
        sp.Children.Add(head);
        string key = "q:" + q.Id;
        var cur = (PbqGrade.Get(Ans, key) ?? "").Split(',').Where(x => x.Length > 0).ToList();
        var boxes = new List<(ToggleButton B, string Index)>();
        for (int i = 0; i < q.Choices.Count; i++)
        {
            string idx = i.ToString(CultureInfo.InvariantCulture);
            ToggleButton b = q.Multi ? new CheckBox() : new RadioButton { GroupName = "pbq-" + It.Id + "-" + q.Id };
            b.Content = CertUi.PlainRich(q.Choices[i]); b.MinHeight = 44; b.Margin = new Thickness(4, 2, 0, 0); b.VerticalContentAlignment = VerticalAlignment.Center;
            b.IsChecked = cur.Contains(idx);
            b.IsCheckedChanged += (o, a) => CertUi.Safe(() =>
            {
                var pickedNow = boxes.Where(x => x.B.IsChecked == true).Select(x => x.Index).ToArray();
                Put(key, pickedNow.Length == 0 ? null : string.Join(",", pickedNow));
            });
            boxes.Add((b, idx));
            sp.Children.Add(b);
        }
        return sp;
    }

    Control CalcView(PbqCalc c)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 8, 0, 2), Spacing = 4 };
        sp.Children.Add(Ui.Text(c.Label, 15, Ui.TextC));
        var box = new TextBox { Width = 200, MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Left, Text = PbqGrade.Get(Ans, "c:" + c.Id) ?? "", FontFamily = CertUi.Mono, FontSize = 15, VerticalContentAlignment = VerticalAlignment.Center, Tag = "calc:" + c.Id };
        box.PropertyChanged += (o, a) => { if (a.Property == TextBox.TextProperty) CertUi.Safe(() => Put("c:" + c.Id, (box.Text ?? "").Trim().Length == 0 ? null : box.Text.Trim())); };
        sp.Children.Add(box);
        return sp;
    }

    // ---- matching: tap a card, then the item it belongs to (tap an item's answer to clear it)
    Control MatchView(PbqSpec s)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 8, 0, 4) };
        sp.Children.Add(CertUi.Muted("Tap a card, then the item it belongs to. A card may be used more than once. Tap an item's answer again to clear it."));
        var pool = new WrapPanel { Margin = new Thickness(0, 2, 0, 8) };
        var chips = new List<Button>();
        foreach (var t in s.Targets)
        {
            var t0 = t;
            var chip = Chip(t);
            chip.Click += (o, a) => CertUi.Safe(() => { picked = picked == t0 ? null : t0; foreach (var c in chips) c.BorderBrush = Ui.Brush((string)c.Tag == picked ? Ui.Accent : Ui.BorderC); });
            chips.Add(chip); pool.Children.Add(chip);
        }
        sp.Children.Add(pool);
        sp.Children.Add(Ui.Text(s.LeftTitle + "  →  " + s.RightTitle, 13, Ui.Muted));
        for (int i = 0; i < s.Pairs.Count; i++)
        {
            int k = i;
            var row = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            row.Children.Add(Ui.Text(s.Pairs[i].Left.Replace("`", ""), 14, Ui.TextC));
            var slot = new Button { VerticalContentAlignment = VerticalAlignment.Center, MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 6), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Tag = "slot:" + i };
            void Paint()
            {
                string v = PbqGrade.Get(Ans, "m:" + k);
                slot.Content = new TextBlock { Text = v ?? "tap a card, then here", TextWrapping = TextWrapping.Wrap, Foreground = Ui.Brush(v == null ? Ui.Muted : Ui.TextC) };
                slot.Background = Ui.Brush(v == null ? Ui.Surface2 : Ui.Surface3); slot.BorderBrush = Ui.Brush(v == null ? Ui.BorderC : Ui.Accent);
            }
            Paint();
            slot.Click += (o, a) => CertUi.Safe(() =>
            {
                if (picked != null) Put("m:" + k, picked);
                else if (PbqGrade.Get(Ans, "m:" + k) != null) Put("m:" + k, null);
                Paint();
            });
            row.Children.Add(slot);
            sp.Children.Add(row);
        }
        return sp;
    }

    static Button Chip(string text) => new()
    {
        Content = new TextBlock { Text = text.Replace("`", ""), TextWrapping = TextWrapping.Wrap, MaxWidth = 260 },
        Tag = text, Background = Ui.Brush(Ui.Surface3), BorderBrush = Ui.Brush(Ui.BorderC), BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(18),
        Padding = new Thickness(12, 6), Margin = new Thickness(0, 0, 6, 6), MinHeight = 44,
    };

    // ---- the firewall rule table
    int RowCount { get { var s = It.Pbq; return Math.Max(s.Rows, s.Start.Count); } }
    string[] RowValues(int i)
    {
        var s = It.Pbq;
        string v = PbqGrade.Get(Ans, "r:" + i) ?? (i < s.Start.Count ? PbqGrade.Row(s.Start[i]) : "");
        var p = v.Split('|').ToList();
        while (p.Count < 5) p.Add("");
        if (p[4] == "ANY") p[4] = "Any";
        return p.Take(5).ToArray();
    }

    /// <summary>The drop-downs of the firewall table (checks).</summary>
    public readonly List<ComboBox[]> RuleBoxes = new();

    Control FirewallView(PbqSpec s)
    {
        RuleBoxes.Clear();
        var sp = new StackPanel { Margin = new Thickness(0, 8, 0, 4) };
        sp.Children.Add(Ui.Text("What the rules must do", 15, Ui.AccentText, true));
        foreach (var t in s.Tests.Where(x => x.Why.Length > 0)) sp.Children.Add(Ui.Text((t.Allow ? "  Allow: " : "  Block: ") + t.Why, 14, Ui.TextC));
        if (s.Addrs.Count > 0) sp.Children.Add(CertUi.Muted("Addresses: " + string.Join("; ", s.Addrs.Select(a => a.Key + " = " + a.Value))));
        var opts = new List<string[]>
        {
            new[] { "", "ALLOW", "DENY" },
            new[] { "", "Any" }.Concat(s.Addrs.Select(a => a.Key)).ToArray(),
            new[] { "", "Any" }.Concat(s.Addrs.Select(a => a.Key)).ToArray(),
            new[] { "", "Any" }.Concat(s.Ports).ToArray(),
            new[] { "", "TCP", "UDP", "ICMP", "Any" },
        };
        string[] names = { "Action", "Source", "Destination", "Port", "Protocol" };
        for (int i = 0; i < RowCount; i++)
        {
            int row = i;
            var vals = RowValues(i);
            var boxes = new ComboBox[5];
            var fields = new WrapPanel();
            for (int c = 0; c < 5; c++)
            {
                var cb = new ComboBox { MinHeight = 44, MinWidth = 130, Margin = new Thickness(0, 0, 6, 6), PlaceholderText = names[c] };
                foreach (var o in opts[c]) cb.Items.Add(o);
                cb.SelectedItem = opts[c].Contains(vals[c]) ? vals[c] : "";
                ToolTip.SetTip(cb, "Rule " + (i + 1) + ": " + names[c]);
                boxes[c] = cb;
                fields.Children.Add(new StackPanel { Children = { Ui.Text(names[c], 12, Ui.Muted), cb } });
            }
            foreach (var cb in boxes) cb.SelectionChanged += (o, a) => CertUi.Safe(() => Put("r:" + row, string.Join("|", boxes.Select(b => (string)b.SelectedItem ?? ""))));
            RuleBoxes.Add(boxes);
            var tools = new WrapPanel();
            tools.Children.Add(CertUi.Btn("Up", () => Swap(row, row - 1)));
            tools.Children.Add(CertUi.Btn("Down", () => Swap(row, row + 1)));
            tools.Children.Add(CertUi.Btn("Clear", () => { Put("r:" + row, ""); Rebuild(); }));
            var card = new StackPanel { Children = { Ui.Text("Rule " + (i + 1), 14, Ui.TextC, true), fields, tools } };
            sp.Children.Add(new Border { Child = card, Background = Ui.Brush(Ui.Surface2), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 6), Margin = new Thickness(0, 6, 0, 0) });
        }
        sp.Children.Add(CertUi.Muted("Rules are read from the top. After the last rule, anything no rule matched is denied (implicit deny). Rows left incomplete are ignored."));
        return sp;
    }
    void Swap(int a, int b)
    {
        if (b < 0 || b >= RowCount) return;
        var va = string.Join("|", RowValues(a)); var vb = string.Join("|", RowValues(b));
        PbqGrade.Set(Ans, "r:" + a, vb); PbqGrade.Set(Ans, "r:" + b, va);
        Rebuild(); changed?.Invoke();
    }

    // ---- network placement: tap a device, then its zone (tap a placed device to take it out)
    Control PlaceView(PbqSpec s)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 8, 0, 4) };
        sp.Children.Add(CertUi.Muted("Tap a device, then the zone it belongs in. Tap a placed device to take it out again."));
        var palette = new WrapPanel { Margin = new Thickness(0, 2, 0, 8) };
        var chips = new List<Button>();
        Button ChipFor(int i, bool placed)
        {
            var chip = Chip(s.Devices[i].Name);
            string key = i.ToString(CultureInfo.InvariantCulture);
            chip.Tag = key;
            chip.Click += (o, a) => CertUi.Safe(() =>
            {
                if (placed) { Put("d:" + key, null); picked = null; Rebuild(); return; }
                picked = picked == key ? null : key;
                foreach (var c in chips) c.BorderBrush = Ui.Brush((string)c.Tag == picked ? Ui.Accent : Ui.BorderC);
            });
            chips.Add(chip);
            return chip;
        }
        for (int i = 0; i < s.Devices.Count; i++) if (!s.Zones.Any(z => z.Id == PbqGrade.Get(Ans, "d:" + i))) palette.Children.Add(ChipFor(i, false));
        if (palette.Children.Count == 0) palette.Children.Add(CertUi.Muted("Every device is placed."));
        sp.Children.Add(palette);
        for (int z = 0; z < s.Zones.Count; z++)
        {
            var zone = s.Zones[z];
            if (z > 0 && zone.Link.Length > 0) sp.Children.Add(new Border { Background = Ui.Brush(Ui.Yellow), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 2), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2), Child = new TextBlock { Text = "↕ " + zone.Link, FontSize = 12, Foreground = Ui.Brush("#111827") } });
            var inside = new WrapPanel();
            for (int i = 0; i < s.Devices.Count; i++) if (PbqGrade.Get(Ans, "d:" + i) == zone.Id) inside.Children.Add(ChipFor(i, true));
            string zid = zone.Id;
            var put = CertUi.Btn("Put it here", () => { if (picked == null) return; Put("d:" + picked, zid); picked = null; Rebuild(); });
            put.Tag = "zone:" + zid;
            var head = new DockPanel();
            DockPanel.SetDock(put, Dock.Right);
            head.Children.Add(put);
            head.Children.Add(Ui.Text(zone.Title, 15, Ui.AccentText, true));
            var box = new StackPanel { Children = { head, inside } };
            sp.Children.Add(new Border { Child = box, MinHeight = 90, Padding = new Thickness(8), Background = Ui.Brush(Ui.Surface2), BorderBrush = Ui.Brush(Ui.BorderC), BorderThickness = new Thickness(1.5), CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 4, 0, 0) });
        }
        return sp;
    }

    /// <summary>The review of a PBQ: every part right or wrong, with the person's answer and the right one.</summary>
    public static Control Review(TaskItem it, PbqResult r)
    {
        var sp = new StackPanel();
        if (r == null) return sp;
        sp.Children.Add(CertUi.Muted(r.Parts.Count(p => p.Pass) + " of " + r.Parts.Count + " parts right: " + r.Points + " of " + it.Points + " points."));
        foreach (var p in r.Parts)
            sp.Children.Add(Ui.Text((p.Pass ? Mark.Yes : p.Got > 0 ? Mark.Half : Mark.No) + p.Label.Replace("`", "") + "  -  yours: " + p.Yours.Replace("`", "") + (p.Pass ? "" : "; right: " + p.Right.Replace("`", "")), 14, p.Pass ? Ui.Muted : p.Got > 0 ? Ui.Yellow : Ui.Red));
        var s = it.Pbq;
        if (s != null && s.Model.Count > 0)
        {
            sp.Children.Add(CertUi.Muted("A rule table that works (others can too):"));
            sp.Children.Add(CertUi.TermBlock(string.Join("\n", s.Model.Select((m, i) => (i + 1) + ". " + PbqSpec.RuleText(m))) + "\n   (then the implicit deny)"));
        }
        if (s != null && s.IndicatorCount > 0)
        {
            var lines = new List<string>();
            foreach (var ex in s.Exhibits) for (int i = 0; i < ex.Lines.Count; i++) if (ex.Marked[i]) lines.Add(ex.Lines[i]);
            sp.Children.Add(CertUi.Muted("The lines that show it:"));
            sp.Children.Add(CertUi.TermBlock(string.Join("\n", lines)));
        }
        return sp;
    }
}

public sealed class PbqListPage : CertPage
{
    public override string Title => "PBQ practice";
    public PbqListPage(CertApp app) : base(app)
    {
        var root = new StackPanel();
        root.Children.Add(CertUi.H1("PBQ practice"));
        root.Children.Add(CertUi.P("Performance-based questions (PBQs) ask you to do something rather than pick an answer: write firewall rules, read logs, match attacks to controls, place devices on a network, read a certificate, work out a risk. On the real exam they come first and take longer; each part counts, so partial answers still earn points. Practise each kind here as often as you like: Check marks every part and explains it."));
        var c = A.Course;
        if (c.Practice.Count == 0) root.Children.Add(CertUi.Muted("This course has no PBQ practice."));
        foreach (var set in c.Practice)
        {
            var s0 = set;
            var sp = new StackPanel();
            sp.Children.Add(Ui.Text(set.Title, 17, Ui.TextC, true));
            sp.Children.Add(CertUi.Muted(set.Items.Count + " scenarios  -  " + string.Join(", ", set.Items.Select(i => PbqView.KindLabel(i.Kind)).Distinct())));
            if (set.Intro.Length > 0) sp.Children.Add(CertUi.Muted(set.Intro));
            sp.Children.Add(CertUi.Accent("Practise", () => Mv.Push(new PbqPracticePage(A, s0, null))));
            root.Children.Add(CertUi.Pane(sp, 16));
        }
        Body = Ui.Page(root, 1000);
    }
}

public sealed class PbqPracticePage : CertPage
{
    readonly TaskSet set;
    public int Index;
    public PbqView View2;
    public TaskSet Inst;
    StackPanel result;
    public override string Title => "PBQ practice";

    public PbqPracticePage(CertApp app, TaskSet s, string itemId) : base(app)
    {
        set = s;
        A.EnsureCourseOf(s);
        int seed = A.Quiet ? 1 : Environment.TickCount & 0xFFFF;
        Inst = set.Instance(seed);
        Index = Math.Max(0, Inst.Items.FindIndex(i => i.Id == itemId));
        Render();
    }

    public void Go(int i) { if (i < 0 || i >= Inst.Items.Count) return; Index = i; Render(); }

    void Render()
    {
        var it = Inst.Items[Index];
        var root = new StackPanel();
        root.Children.Add(CertUi.Muted(set.Title));
        root.Children.Add(CertUi.H1("Scenario " + (Index + 1) + " of " + Inst.Items.Count));
        root.Children.Add(CertUi.Row(CertUi.Btn("Previous", () => Go(Index - 1)), CertUi.Btn("Next", () => Go(Index + 1))));
        var sp = new StackPanel();
        sp.Children.Add(CertUi.Muted(PbqView.KindLabel(it.Kind) + "  -  " + it.Objective + "  -  " + it.Points + " points"));
        View2 = new PbqView(it, new List<string>(), null);
        sp.Children.Add(View2);
        root.Children.Add(CertUi.Pane(sp, 14));
        root.Children.Add(CertUi.Row(CertUi.Accent("Check", () => Check()), CertUi.Btn("Show the answer", () => { View2.Load(PbqGrade.ModelAnswer(it.Pbq)); Check(); }), CertUi.Btn("Try again", () => { View2.Load(new List<string>()); result.Children.Clear(); })));
        result = new StackPanel();
        root.Children.Add(result);
        Body = Ui.Page(root, 1100);
    }

    public PbqResult Check()
    {
        var it = Inst.Items[Index];
        var r = PbqGrade.Grade(it.Pbq, it.Points, View2.Ans);
        result.Children.Clear();
        var sp = new StackPanel();
        sp.Children.Add(Ui.Text(r.Fraction >= 0.999 ? "All right: " + r.Points + " of " + it.Points + " points." : r.Points + " of " + it.Points + " points.", 17, r.Fraction >= 0.999 ? Ui.Green : Ui.Yellow, true));
        sp.Children.Add(PbqView.Review(it, r));
        if (it.Explain.Length > 0) sp.Children.Add(CertUi.P(it.Explain));
        result.Children.Add(CertUi.Pane(sp, 14));
        return r;
    }
}
