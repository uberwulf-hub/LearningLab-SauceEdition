// Cyber Range (Learning Lab) - the report at the end of a run (App Hub's RangeResultsPage): score breakdown, rank
// progress, new badges, the flags / questions / fixes, the attacker's timeline (Blue Team), what the defender noticed,
// the hints revealed, and the walkthrough laid out by RangeWalk (numbered steps, code boxes, the lesson; commands, paths
// and addresses in the prose as code). The walkthrough is shown only here, once the run has ended (won or given up).
using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using CertLab;
using LearningLab.CertLab;
using LearningLab.Views;

namespace LearningLab.CyberRange;

/// <summary>Lays out a walkthrough: numbered steps, code lines in boxes, the lesson in a callout, and commands, paths and
/// addresses in the prose shown as code.</summary>
public static class RangeWalk
{
    static readonly HashSet<string> cmds = new(StringComparer.Ordinal)
    {
        "nmap", "ssh", "ftp", "sudo", "cat", "ls", "grep", "curl", "base64", "chmod", "chown", "systemctl", "rm", "echo", "crontab", "cp", "mv", "mysql",
        "smbclient", "hashcrack", "pwguess", "webscan", "vulnlookup", "winrm-shell", "ticket-request", "journalctl", "xxd", "strings", "awk", "vim", "vi",
        "wget", "openssl", "chage", "usermod", "passwd", "userdel", "useradd", "lastb", "getfacl", "setfacl", "ausearch", "nano", "tcpdump", "netstat",
        "ss", "ps", "kill", "pkill", "stat", "exiftool", "steghide", "binwalk", "base32", "xorcat", "vigenere", "morse", "rot13", "sha256sum", "md5sum",
        "Get-ChildItem", "Get-Content", "Get-ItemProperty", "Get-WinEvent", "Get-LocalUser", "Get-Service", "Get-ScheduledTask", "Remove-Item",
        "Set-ItemProperty", "Unregister-ScheduledTask", "Disable-LocalUser", "Stop-Service", "Get-Acl", "net", "whoami", "sqlmap", "hydra",
    };
    static readonly HashSet<string> soft = new(StringComparer.Ordinal) { "find", "last", "file", "less", "id", "rev", "tr", "tar", "head", "tail", "sort", "cut", "which", "su", "env", "wc", "uniq", "nc", "dig", "host" };
    static readonly Regex ip = new(@"^\d{1,3}(\.\d{1,3}){3}(/\d+)?(:\d+)?$");
    static readonly Regex fileLike = new(@"^[\w.~-]+\.(txt|sh|php|conf|log|json|ps1|bak|zip|py|pem|key|service|cfg|ini|html|sql|db|csv|xml|gz|tar|evtx|jpg|png)$", RegexOptions.IgnoreCase);

    static bool Strong(string w)
    {
        if (w.Length == 0) return false;
        if (cmds.Contains(w)) return true;
        if (w.StartsWith('/') || w.StartsWith("~/", StringComparison.Ordinal) || w.StartsWith("./", StringComparison.Ordinal) || w.StartsWith('$') || w.StartsWith("C:\\", StringComparison.Ordinal) || w.StartsWith("HKLM:", StringComparison.Ordinal) || w.StartsWith("\\\\", StringComparison.Ordinal)) return true;
        if (w.Length > 1 && w[0] == '-' && char.IsLetter(w[1]) && !w.Contains("--", StringComparison.Ordinal)) return true;
        if (w.StartsWith("--", StringComparison.Ordinal) && w.Length > 3) return true;
        if (ip.IsMatch(w)) return true;
        if (w.Contains('@') && w.Length > 3) return true;
        if (w.Contains('{') || w.Contains('|') || w.Contains('=') && !w.EndsWith('=')) return true;
        return fileLike.IsMatch(w);
    }

    /// <summary>Adds a sentence to a text block with code-looking words shown as code.</summary>
    public static void Prose(InlineCollection inl, string text, double size)
    {
        var words = text.Split(' ');
        var core = new string[words.Length]; var pre = new string[words.Length]; var post = new string[words.Length];
        var mark = new bool[words.Length];
        for (int i = 0; i < words.Length; i++)
        {
            string w = words[i]; int a = 0, b = w.Length;
            while (a < b && "(\"'".IndexOf(w[a]) >= 0) a++;
            while (b > a && ".,;:)\"'".IndexOf(w[b - 1]) >= 0) b--;
            pre[i] = w.Substring(0, a); core[i] = w.Substring(a, b - a); post[i] = w.Substring(b);
            mark[i] = Strong(core[i]);
        }
        for (int i = words.Length - 2; i >= 0; i--) if (!mark[i] && soft.Contains(core[i]) && mark[i + 1] && pre[i + 1].Length == 0) mark[i] = true;
        int k = 0;
        while (k < words.Length)
        {
            if (!mark[k]) { inl.Add(new Run(words[k] + (k < words.Length - 1 ? " " : ""))); k++; continue; }
            // marked words that follow each other with no punctuation between them become one code run
            int e = k;
            while (e + 1 < words.Length && mark[e + 1] && post[e].Length == 0 && pre[e + 1].Length == 0) e++;
            if (pre[k].Length > 0) inl.Add(new Run(pre[k]));
            var parts = new List<string>();
            for (int j = k; j <= e; j++) parts.Add(core[j]);
            inl.Add(new Run("\u2009" + string.Join(" ", parts) + "\u2009") { FontFamily = Rg.Mono, FontSize = size - 1, Background = Rg.Soft("#FFFFFF", 0.07), Foreground = Rg.B("#E2E8F0") });
            inl.Add(new Run(post[e] + (e < words.Length - 1 ? " " : "")));
            k = e + 1;
        }
    }

    static SelectableTextBlock Text(double size = 14.5)
        => new() { FontSize = size, Foreground = Rg.B(Ui.TextC), TextWrapping = TextWrapping.Wrap, LineHeight = Math.Round(size * 1.55), SelectionBrush = Rg.B("#2F5A8A"), Inlines = new InlineCollection() };

    public static StackPanel Render(string text)
    {
        var sp = new StackPanel();
        var paras = Regex.Split((text ?? "").Replace("\r", ""), @"\n\s*\n");
        foreach (var p0 in paras)
        {
            string p = p0.TrimEnd();
            if (p.Trim().Length == 0) continue;
            var lines = p.Split('\n');
            if (lines.All(l => l.StartsWith("    ", StringComparison.Ordinal) || l.StartsWith('\t') || l.Trim().Length == 0))
            {
                var code = string.Join("\n", lines.Select(l => l.StartsWith('\t') ? l.Substring(1) : l.Length >= 4 ? l.Substring(4) : l.TrimStart()));
                var block = CertUi.TermBlock(code);
                block.Margin = new Thickness(30, 0, 0, 12);
                sp.Children.Add(block);
                continue;
            }
            string joined = string.Join(" ", lines.Select(l => l.Trim()));
            var m = Regex.Match(joined, @"^(\d+)[.)]\s+(.*)$");
            if (m.Success)
            {
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("34,*"), Margin = new Thickness(0, 0, 0, 12) };
                g.Children.Add(new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13), Background = Rg.Soft("#60A5FA", 0.16), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left, Child = new TextBlock { Text = m.Groups[1].Value, FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Rg.B(Ui.AccentText), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
                var tb = Text();
                tb.Margin = new Thickness(0, 1, 0, 0);
                string rest = m.Groups[2].Value;
                int dot = rest.IndexOf(". ", StringComparison.Ordinal);
                if (dot > 0 && dot < 70) { tb.Inlines.Add(new Run(rest.Substring(0, dot + 1)) { FontWeight = FontWeight.SemiBold }); tb.Inlines.Add(new Run(" ")); rest = rest.Substring(dot + 2); }
                Prose(tb.Inlines, rest, 14.5);
                Grid.SetColumn(tb, 1); g.Children.Add(tb);
                sp.Children.Add(g);
                continue;
            }
            var lesson = Regex.Match(joined, @"^(How to fix it|For real|The lesson|Lesson|In real life|Why it matters|The fix|Fix)\s*[:.-]\s*(.*)$", RegexOptions.IgnoreCase);
            if (lesson.Success)
            {
                var tb = Text();
                tb.Inlines.Add(new Run(lesson.Groups[1].Value + ": ") { FontWeight = FontWeight.SemiBold, Foreground = Rg.B("#6EE7B7") });
                Prose(tb.Inlines, lesson.Groups[2].Value, 14.5);
                var c = Rg.Callout(tb, "#34D399");
                c.Margin = new Thickness(0, 4, 0, 12);
                sp.Children.Add(c);
                continue;
            }
            var pt = Text();
            pt.Margin = new Thickness(0, 0, 0, 12);
            Prose(pt.Inlines, joined, 14.5);
            sp.Children.Add(pt);
        }
        return sp;
    }
}

public sealed class RangeResultsPage : RangePage
{
    public readonly RangeRun Done;
    public readonly RangeOutcome Outcome;
    public override string Title => Done.Title + ": report";
    static readonly string[] cheers = { "Clean work.", "That's how it's done.", "Textbook.", "Nicely done.", "Sharp work." };

    public RangeResultsPage(RangeApp app, RangeRun run, RangeOutcome o) : base(app)
    {
        Done = run;
        Outcome = o ??= new RangeOutcome { PointsBefore = RangeProgress.TotalPoints(A.Store), PointsAfter = RangeProgress.TotalPoints(A.Store) };
        var st = RangeLook.ForId(run.Category);
        bool win = run.AllFound;
        var root = new StackPanel();
        root.Children.Add(Hero(run, st, win));
        var cols = new FlowGrid { MinCell = 440, MaxCols = 2 };
        cols.Children.Add(Breakdown(run));
        cols.Children.Add(Progress(run, o));
        root.Children.Add(cols);
        var details = Details(run);
        if (details != null) root.Children.Add(details);

        var wsp = new StackPanel();
        wsp.Children.Add(Rg.PaneHead("map", "The intended path"));
        var walk = RangeWalk.Render(run.Walkthrough);
        walk.Tag = "walkthrough";
        wsp.Children.Add(walk);
        root.Children.Add(Rg.Panel(wsp, 18));

        var nav = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        nav.Children.Add(RangeUi.Primary(win ? "Play again" : "Try again", () => { if (run.Pz != null) A.StartPuzzle(run.PzTemplate); else A.StartScenario(run.Template); }));
        var next = A.Next(out _);
        if (next != null && next.Id != run.Id) nav.Children.Add(RangeUi.Secondary("Next: " + next.Title, () => { Mv.Back(); A.Open(next); }));
        nav.Children.Add(RangeUi.Secondary("More like this", MoreLikeThis));
        nav.Children.Add(RangeUi.Secondary("Cyber Range home", () => { if (Mv.Has<RangeHomePage>()) { Mv.BackTo<RangeHomePage>(); (Mv.Page as RangeHomePage)?.Refresh(); } else Mv.Replace(new RangeHomePage(A)); }));
        root.Children.Add(nav);
        Body = RangeUi.Page(root, 1180);
    }

    void MoreLikeThis()
    {
        string cat = Done.Pz != null ? RangeItem.PuzzleCategory(Done.Pz) : Done.Sc.Category;
        string level = Done.Level;
        if (Mv.Has<RangeLevelPage>()) { Mv.BackTo<RangeLevelPage>(); if (Mv.Page is RangeLevelPage lp) lp.Refresh(); return; }
        var c = RangeData.CategoryById(cat);
        if (c != null) Mv.Replace(new RangeLevelPage(A, c, level));
    }

    Control Hero(RangeRun run, RangeCatStyle st, bool win)
    {
        string colour = win ? "#34D399" : "#94A3B8";
        var g = new Grid();
        g.Children.Add(new Border { CornerRadius = new CornerRadius(16), Background = win ? Rg.Gradient("#10251F", "#0E141B", 15) : Rg.Gradient("#151A24", "#0E121B", 15) });
        g.Children.Add(new Border { CornerRadius = new CornerRadius(16), Background = Rg.Glow(colour, (byte)(win ? 60 : 25), 0.08, 0.5, 0.6) });
        g.Children.Add(new Border { CornerRadius = new CornerRadius(16), BorderBrush = Rg.Soft(colour, 0.3), BorderThickness = new Thickness(1) });
        var inner = new WrapPanel { Margin = new Thickness(20) };
        var ring = Rg.Ring(run.MaxPoints == 0 ? 0 : run.Score / (double)run.MaxPoints, colour, 104, run.Score.ToString(CultureInfo.InvariantCulture), "of " + run.MaxPoints);
        ring.Margin = new Thickness(0, 0, 22, 12);
        inner.Children.Add(ring);
        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, MaxWidth = 760 };
        sp.Children.Add(Rg.Eyebrow(win ? (run.IsBlue ? "Incident closed" : run.Pz != null ? "Puzzle solved" : "Mission complete") : "Run ended", win ? "#6EE7B7" : "#CBD5E1"));
        sp.Children.Add(Rg.Title(run.Title, 26));
        string line = win ? (run.HintsUsed.Count == 0 ? "Everything captured without a single hint. " : "Everything captured. ") + cheers[(Math.Abs(FwHash.Of(run.Id)) & 0x7fffffff) % cheers.Length]
            : "Not everything this time. The intended path is below - read it, then try again: the details change every run.";
        var lt = Rg.Body(line, 15, win ? "#A7F3D0" : Ui.TextC); lt.Margin = new Thickness(0, 4, 0, 10);
        sp.Children.Add(lt);
        var chips = new WrapPanel();
        chips.Children.Add(Rg.Chip(RangeApp.CatTitle(run.Category), st.Accent, 0.12));
        chips.Children.Add(Rg.LevelChip(run.Level));
        chips.Children.Add(Rg.Tag("clock", RangeRun.TimeText(run.Seconds)));
        chips.Children.Add(Rg.Tag("bulb", run.HintsUsed.Count == 0 ? "no hints" : run.HintsUsed.Count + (run.HintsUsed.Count == 1 ? " hint" : " hints")));
        sp.Children.Add(chips);
        inner.Children.Add(sp);
        g.Children.Add(inner);
        return new Border { Child = g, Margin = new Thickness(0, 4, 0, 16) };
    }

    static Control Line(string label, string sub, int points, bool shown, string colour)
    {
        var dp = new DockPanel { Margin = new Thickness(0, 0, 0, 9) };
        var val = new TextBlock { Text = shown ? (points > 0 ? "+" : points < 0 ? "\u2212" : "") + Math.Abs(points) : "-", FontSize = 15, FontWeight = FontWeight.SemiBold, Foreground = Rg.B(points == 0 ? Ui.Muted : colour), VerticalAlignment = VerticalAlignment.Center, MinWidth = 50, TextAlignment = TextAlignment.Right };
        DockPanel.SetDock(val, Dock.Right); dp.Children.Add(val);
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = label, FontSize = 14, Foreground = Rg.B(Ui.TextC), TextWrapping = TextWrapping.Wrap });
        if (sub != null) sp.Children.Add(Rg.Body(sub, 12.5));
        dp.Children.Add(sp);
        return dp;
    }

    /// <summary>The checks: the breakdown's total line.</summary>
    public string TotalText { get; private set; } = "";

    Control Breakdown(RangeRun run)
    {
        var sp = new StackPanel();
        sp.Children.Add(Rg.PaneHead("star", "Score breakdown"));
        if (run.Pz != null) sp.Children.Add(Line("Puzzle solved", null, run.Found.Count > 0 ? run.Pz.Points : 0, true, run.Found.Count > 0 ? "#6EE7B7" : Ui.Muted));
        else
        {
            var s = run.Sc;
            if (s.Flags.Count > 0) sp.Children.Add(Line("Flags captured", run.Found.Count + " of " + s.Flags.Count, s.Flags.Where(f => run.Found.Contains(f.Id)).Sum(f => f.Points), true, "#6EE7B7"));
            if (s.Questions.Count > 0) sp.Children.Add(Line("Questions answered", run.Answered.Count + " of " + s.Questions.Count, s.Questions.Where(q => run.Answered.ContainsKey(q.Id) && !run.Missed.Contains(q.Id)).Sum(q => q.Points), true, "#6EE7B7"));
            if (s.Tasks.Count > 0) sp.Children.Add(Line("Fixes made", run.TasksDone.Count + " of " + s.Tasks.Count, s.Tasks.Where(t => run.TasksDone.Contains(t.Id)).Sum(t => t.Points), true, "#6EE7B7"));
            if (run.IsBlue && s.GoalPoints > 0) sp.Children.Add(Line("Attacker contained", run.Breached ? "it reached its goal first" : "stopped before its goal", run.Breached ? 0 : s.GoalPoints, true, run.Breached ? Ui.Muted : "#6EE7B7"));
            if (run.IsDefender && s.QuietPoints > 0)
            {
                int q = run.Defender != null ? run.Defender.QuietBonus(s.QuietPoints) : s.QuietPoints;
                int alerts = run.Defender != null ? run.Defender.Alerts : 0;
                sp.Children.Add(Line("Quiet bonus", alerts == 0 ? "the defender never noticed you" : alerts + (alerts == 1 ? " alert" : " alerts") + " raised", q, true, q > 0 ? "#6EE7B7" : Ui.Muted));
            }
        }
        if (run.HintsUsed.Count > 0) sp.Children.Add(Line("Hints", run.LearningMode ? run.HintsUsed.Count + " shown, free in Learning mode" : run.HintsUsed.Count + " shown", -run.HintCost, true, run.HintCost > 0 ? "#FCA5A5" : Ui.Muted));
        else sp.Children.Add(Line("Hints", "none used", 0, true, Ui.Muted));
        sp.Children.Add(Line("Time", RangeRun.TimeText(run.Seconds) + " - shown for you, not scored", 0, false, Ui.Muted));
        TotalText = run.Score + " / " + run.MaxPoints;
        var total = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        var tv = new TextBlock { Text = TotalText, FontSize = 17, FontWeight = FontWeight.SemiBold, Foreground = Rg.B(Ui.TextC) };
        DockPanel.SetDock(tv, Dock.Right); total.Children.Add(tv);
        total.Children.Add(new TextBlock { Text = "Total", FontSize = 15.5, FontWeight = FontWeight.SemiBold, Foreground = Rg.B(Ui.TextC), VerticalAlignment = VerticalAlignment.Center });
        sp.Children.Add(new Border { BorderBrush = Rg.B(Ui.BorderC), BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 10, 0, 0), Child = total });
        return Rg.Panel(sp, 18);
    }

    Control Progress(RangeRun run, RangeOutcome o)
    {
        var sp = new StackPanel();
        sp.Children.Add(Rg.PaneHead("trophy", "Your progress"));
        var before = RangeProgress.RankFor(o.PointsBefore); var rk = RangeProgress.RankFor(o.PointsAfter); var next = RangeProgress.NextRank(o.PointsAfter);
        var top = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 0, 0, 10) };
        top.Children.Add(Rg.Insignia(rk.Index, 40, rk.Colour));
        var nm = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        nm.Children.Add(new TextBlock { Text = rk.Index > before.Index ? "Promoted to " + rk.Name + "!" : rk.Name, FontSize = 18, FontWeight = FontWeight.SemiBold, Foreground = Rg.B(rk.Colour) });
        int gain = o.PointsAfter - o.PointsBefore;
        nm.Children.Add(Rg.Body(Rg.N(o.PointsAfter) + " points" + (gain > 0 ? "  (+" + gain + " this run)" : o.BestBefore > 0 ? "  (your best here stays " + Math.Max(o.BestBefore, run.Score) + ")" : ""), 13));
        top.Children.Add(nm);
        sp.Children.Add(top);
        sp.Children.Add(Rg.Bar(RangeProgress.RankShare(o.PointsAfter), Rg.Gradient(rk.Colour, next != null ? next.Colour : rk.Colour, 0), 7));
        var nt = Rg.Body(next != null ? Rg.N(next.Min - o.PointsAfter) + " points to " + next.Name : "Top rank reached.", 12.5); nt.Margin = new Thickness(0, 6, 0, 14);
        sp.Children.Add(nt);
        sp.Children.Add(new TextBlock { Text = o.NewBadges.Count > 0 ? (o.NewBadges.Count == 1 ? "New badge" : "New badges") : "Badges", FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = o.NewBadges.Count > 0 ? Rg.B("#FCD34D") : Rg.B(Ui.TextC), Margin = new Thickness(0, 0, 0, 8) });
        if (o.NewBadges.Count > 0)
        {
            foreach (var id in o.NewBadges)
            {
                var b = RangeProgress.Badge(id); if (b == null) continue;
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(0, 0, 0, 8) };
                var med = Rg.Medal(b.Icon, b.Colour, true, 44);
                RangeFx.FadeIn(med, 400);
                row.Children.Add(med);
                var bt = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                bt.Children.Add(new TextBlock { Text = b.Name, FontSize = 14.5, FontWeight = FontWeight.SemiBold, Foreground = Rg.B(Ui.TextC) });
                bt.Children.Add(Rg.Body(b.How, 12.5));
                Grid.SetColumn(bt, 1); row.Children.Add(bt);
                sp.Children.Add(row);
            }
        }
        else
        {
            var aim = RangeProgress.Badges.FirstOrDefault(b => !A.Store.Badges.ContainsKey(b.Id));
            sp.Children.Add(Rg.Body(aim != null ? "No new badge this time. Next to aim for: " + aim.Name + " - " + aim.How : "You hold every badge.", 13));
        }
        return Rg.Panel(sp, 18);
    }

    static Control Details(RangeRun run)
    {
        var cols = new FlowGrid { MinCell = 440, MaxCols = 2 };
        if (run.Pz == null && run.Sc.Flags.Count > 0)
        {
            var fl = new StackPanel();
            fl.Children.Add(Rg.PaneHead("flag", "Flags"));
            foreach (var f in run.Sc.Flags) { bool had = run.Found.Contains(f.Id); fl.Children.Add(Row(had, f.Name + "  (" + f.Points + ")", had ? run.Flags[f.Id] : "not captured", had)); }
            cols.Children.Add(Rg.Panel(fl, 16));
        }
        if (run.Pz == null && run.Sc.Questions.Count > 0)
        {
            var q = new StackPanel();
            q.Children.Add(Rg.PaneHead("search", "Questions"));
            foreach (var it in run.Sc.Questions)
            {
                bool had = run.Answered.ContainsKey(it.Id) && !run.Missed.Contains(it.Id);
                string ans = RangeData.Fill(it.Answer, run.Vars, run.Flags);
                q.Children.Add(Row(had, it.Ask, "Answer: " + ans + (it.Explain.Length > 0 ? "  -  " + RangeData.Fill(it.Explain, run.Vars, run.Flags) : ""), false));
            }
            cols.Children.Add(Rg.Panel(q, 16));
        }
        if (run.Pz == null && run.Sc.Tasks.Count > 0)
        {
            var tp = new StackPanel();
            tp.Children.Add(Rg.PaneHead("shield", "Fixes"));
            foreach (var t in run.Sc.Tasks) tp.Children.Add(Row(run.TasksDone.Contains(t.Id), t.Name, t.Explain.Length > 0 ? RangeData.Fill(t.Explain, run.Vars, run.Flags) : null, false));
            cols.Children.Add(Rg.Panel(tp, 16));
        }
        if (run.IsBlue)
        {
            var ap = new StackPanel();
            ap.Children.Add(Rg.PaneHead("clock", run.Breached ? "The attacker reached its goal" : "You contained the attacker"));
            var at = Rg.Body(run.Breached ? "The simulated attacker reached " + (run.Sc.GoalName.Length > 0 ? run.Sc.GoalName : "its goal") + " before you stopped it. The " + Rg.Pts(run.Sc.GoalPoints) + " for containing it were not earned." : "You stopped the attacker before it could " + (run.Sc.GoalName.Length > 0 ? run.Sc.GoalName : "reach its goal") + ".", 13.5, run.Breached ? "#FCA5A5" : "#6EE7B7");
            at.Margin = new Thickness(0, 0, 0, 8);
            ap.Children.Add(at);
            foreach (var l in run.AttackerLog) ap.Children.Add(Timeline(l, l.StartsWith("[blocked]", StringComparison.Ordinal) ? "#6EE7B7" : "#FCA5A5"));
            cols.Children.Add(Rg.Panel(ap, 16));
        }
        if (run.Defender != null && run.Defender.Report.Count > 0)
        {
            var dp = new StackPanel();
            dp.Children.Add(Rg.PaneHead("eye", "What the defender noticed"));
            foreach (var l in run.Defender.Report) dp.Children.Add(Timeline(l, "#FCD34D"));
            cols.Children.Add(Rg.Panel(dp, 16));
        }
        else if (run.IsDefender)
        {
            var dp = new StackPanel();
            dp.Children.Add(Rg.PaneHead("ghost", "The defender"));
            dp.Children.Add(Rg.Body("You stayed quiet enough that the defender never raised the alarm. Full quiet bonus.", 13.5, "#6EE7B7"));
            cols.Children.Add(Rg.Panel(dp, 16));
        }
        if (run.HintsUsed.Count > 0)
        {
            var hp = new StackPanel();
            hp.Children.Add(Rg.PaneHead("bulb", "Hints you revealed"));
            foreach (var i in run.HintsUsed.OrderBy(x => x)) if (i < run.Hints.Count) { var t = Rg.Sel("Hint " + (i + 1) + ": " + run.Hints[i].Text, 13.5); t.Margin = new Thickness(0, 0, 0, 6); hp.Children.Add(t); }
            cols.Children.Add(Rg.Panel(hp, 16));
        }
        return cols.Children.Count > 0 ? cols : null;
    }

    static Control Row(bool got, string text, string sub, bool subMono)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("24,*"), Margin = new Thickness(0, 2, 0, 8) };
        var ic = got ? Rg.Icon("check", 14, "#34D399", 3) : Rg.Icon("lock", 14, Ui.Muted, 2);
        ic.VerticalAlignment = VerticalAlignment.Top; ic.Margin = new Thickness(0, 3, 0, 0);
        g.Children.Add(ic);
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = text, FontSize = 14, Foreground = Rg.B(Ui.TextC), TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrEmpty(sub))
        {
            var s = Rg.Sel(sub, 13, subMono ? "#A7F3D0" : Ui.Muted);
            if (subMono) s.FontFamily = Rg.Mono;
            s.Margin = new Thickness(0, 1, 0, 0);
            sp.Children.Add(s);
        }
        Grid.SetColumn(sp, 1); g.Children.Add(sp);
        return g;
    }
    static Control Timeline(string text, string colour)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("18,*"), Margin = new Thickness(0, 0, 0, 6) };
        g.Children.Add(new Ellipse { Width = 7, Height = 7, Fill = Rg.B(colour), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(1, 7, 0, 0) });
        var t = Rg.Body(text, 13, Ui.TextC);
        Grid.SetColumn(t, 1); g.Children.Add(t);
        return g;
    }
}
