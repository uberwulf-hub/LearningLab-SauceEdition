// Learning Lab - the Learning Games engine checks (what App Hub's LearningGames.ps1 -Test ran: MathTests.cs and
// NetRoundTests.cs, about 3 million checks) and pictures of the math visuals, the rewards and the Long Division Workshop.
// Groups: games-math, games-net, games-pictures. -Seed <n> picks other problems (default 20261024, as in App Hub).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using LearningGames;

namespace LearningLab.Tests;

static partial class Program
{
    /// <summary>Adds the counts of a checker that counts its own checks (MathTests) and prints its report.</summary>
    public static void AddCounts(int count, int failed, string report)
    {
        checks += count;
        fails += failed;
        foreach (string line in (report ?? "").Split('\n'))
        {
            string l = line.TrimEnd('\r');
            if (l.Length == 0) continue;
            Console.WriteLine(l);
            if (l.TrimStart().StartsWith("FAIL", StringComparison.Ordinal)) failures.Add(l.Trim());
        }
    }
}

static class GamesChecks
{
    static int Seed()
    {
        string[] a = Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length - 1; i++) if (a[i] == "-Seed" && int.TryParse(a[i + 1], out int s)) return s;
        return 20261024;
    }

    public static void RunMath()
    {
        Sound.Enabled = false;
        string report = MathTests.Run(Seed(), true, false);
        Program.AddCounts(MathTests.Checks, MathTests.Failures, report);
    }

    public static void RunNet()
    {
        string report = MathTests.Run(Seed(), false, true);
        Program.AddCounts(MathTests.Checks, MathTests.Failures, report);
    }

    // ---------------------------------------------------------------- pictures
    static readonly (string name, string visual)[] Pics =
    {
        ("blocks", "blocks:347"),
        ("regroup", "regroup:47,38,+"),
        ("cash", "cash:2000,500,100,25,10,5,1"),
        ("graph-bar", "graph:bar;Favorite fruit;Apples,Pears,Grapes,Plums;7,3,5,2"),
        ("graph-pic", "graph:pic;Pets at school;Dogs,Cats,Fish;5,3,6"),
        ("fracbars", "fracbars:1/2,2/4,3/8"),
        ("fracdiv", "fracdiv:3/2,1/4"),
        ("tape", "tape:25,$12"),
        ("grid", "grid:3,4;-5,-2"),
        ("boxplot", "boxplot:Test scores;2,4.5,6,8,11"),
        ("dots", "dots:Books read;3,4,4,5,5,5,6,7"),
        ("net", "net:4,3,2"),
        ("element", "element:92,U,uranium,235"),
        ("bracket", "bracket:4567,34"),
    };

    static Window Frame(Control pic, double w, double h)
    {
        var win = new Window { Width = w, Height = h, Background = Visuals.B("#13161D"), Content = new Border { Padding = new Thickness(20), Child = pic } };
        win.Show();
        return win;
    }

    public static void RunPictures()
    {
        Sound.Enabled = false;
        foreach (var (name, visual) in Pics)
        {
            Control pic = Visuals.Draw(visual);
            Program.Check("pictures: " + visual + " draws", pic != null);
            if (pic == null) continue;
            Program.Check("pictures: " + visual + " has a size", pic.Width > 20 && pic.Height > 20, pic.Width + " x " + pic.Height);
            var win = Frame(pic, pic.Width + 40, pic.Height + 40);
            Snap.Save(win, "pic-" + name);
            win.Close();
        }
        Program.Check("pictures: a broken visual gives no picture", Visuals.Draw("blocks:x") == null && Visuals.Draw("nothing:1") == null && Visuals.Draw("") == null);

        foreach (var (grade, pts, big) in new[] { (2, 141, true), (2, 30, false), (6, 54, true), (6, 150, false) })
        {
            Control pic = Rewards.Picture(grade, pts, big);
            Program.Check("pictures: reward " + grade + "/" + pts, pic != null && pic.Width > 50 && pic.Height > 50, pic == null ? "" : pic.Width + " x " + pic.Height);
            var win = Frame(pic, pic.Width + 40, pic.Height + 40);
            Snap.Save(win, "reward-grade" + grade + "-" + pts + (big ? "-big" : "-small"));
            win.Close();
        }

        // the Long Division Workshop as App Hub's -Screen LongDiv:mid draws it: 4567 / 34, guided, a slip on the second multiply
        foreach (var (name, w, h) in new[] { ("longdiv-mid", 1164.0, 647.0), ("longdiv-mid-phone", 400.0, 820.0) })
        {
            var view = new LongDivisionView();
            var win = new Window { Width = w, Height = h, Content = view };
            win.Show();
            var ld = LongDivision.For(4567, 34, 0, true, true, true, true);
            view.Load(ld, true); view.Advance(6); view.Submit("112"); view.Type("10");
            Program.Check("workshop " + name + ": on step 7 of 17 after a slip", ld.Current == 6 && ld.StepCount == 17 && ld.Mistakes == 1, ld.Current + " / " + ld.StepCount + ", " + ld.Mistakes + " slips");
            Program.Check("workshop " + name + ": kind words after the slip", view.FeedbackText.Contains("3 \u00D7 34"), view.FeedbackText);
            Program.Check("workshop " + name + ": what was typed stays in the box", view.Typed == "10");
            Snap.Save(win, name);
            bool phone = w < LongDivisionView.WideMin;
            Program.Check("workshop " + name + ": layout " + (phone ? "narrow with the number pad" : "side by side"), view.IsNarrow == phone);
            foreach (var b in view.GetLogicalDescendantsOfType<Button>().Where(b => b.IsEffectivelyVisible))
                Program.Check("workshop " + name + ": button '" + b.Content + "' is big enough to tap", b.Bounds.Height >= 40, b.Bounds.Height.ToString("0"));
            if (phone)
            {
                // finish the problem by touch: the number pad and Check, then Next problem
                bool finished = false; view.Finished += (s, e) => finished = true;
                view.PadKey("\u232B"); view.PadKey("\u232B");
                for (int guard = 0; guard < 40 && !ld.Done; guard++)
                {
                    foreach (char ch in ld.Step.Expected) view.PadKey(ch == ' ' ? "space" : ch.ToString());
                    Press(view, "Check");
                }
                Program.Check("workshop by touch: the number pad and Check finish the problem", ld.Done && ld.Mistakes == 1, ld.Current + " of " + ld.StepCount);
                Snap.Save(win, "longdiv-done-phone");
                Press(view, "Next problem");
                Program.Check("workshop by touch: Next problem asks for the next one", finished);
            }
            win.Close();
        }
    }

    static void Press(Control root, string text)
    {
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var b = root.GetLogicalDescendantsOfType<Button>().FirstOrDefault(x => (x.Content as string) == text && x.IsEffectivelyVisible);
        if (b == null) { Program.Check("workshop: a visible '" + text + "' button", false); return; }
        b.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
    }

    static IEnumerable<T> GetLogicalDescendantsOfType<T>(this Control c) where T : class
    {
        foreach (var d in Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(c)) if (d is T t) yield return t;
    }
}
