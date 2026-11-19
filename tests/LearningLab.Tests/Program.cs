// Learning Lab - the checks. Each app's group runs its engine checks (what App Hub's -Test ran) and draws its pages
// off screen with Avalonia's headless platform. -Snapshot <dir> also saves the pages as PNGs.
using Avalonia;
using Avalonia.Headless;

namespace LearningLab.Tests;

static partial class Program
{
    static int checks, fails;
    static readonly List<string> failures = new();
    public static string SnapshotDir;
    static HashSet<string> only;

    public static void Check(string what, bool ok, string detail = null)
    {
        checks++;
        if (ok) return;
        fails++;
        string line = "FAIL " + what + (string.IsNullOrEmpty(detail) ? "" : "  (" + detail + ")");
        failures.Add(line);
        Console.WriteLine(line);
    }

    static bool Wanted(string group) => only == null || only.Contains(group);

    static int Main(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-Only" && i + 1 < args.Length) only = new HashSet<string>(args[++i].Split(','), StringComparer.OrdinalIgnoreCase);
            else if (args[i] == "-Snapshot" && i + 1 < args.Length) SnapshotDir = Path.GetFullPath(args[++i]);
        }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        AppBuilder.Configure<App>().UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        Data.UseTestFolder();
        RunGroups();
        Console.WriteLine(fails == 0 ? "ALL PASSED (" + checks + " checks, " + sw.Elapsed.TotalSeconds.ToString("0.0") + " s)" : "FAILED " + fails + " of " + checks + " checks:");
        foreach (var f in failures.Take(50)) Console.WriteLine("  " + f);
        return fails == 0 ? 0 : 1;
    }

    static void RunGroups()
    {
        if (Wanted("shell")) Group("shell", ShellChecks.Run);
        if (Wanted("games-math")) Group("games-math", GamesChecks.RunMath);
        if (Wanted("games-net")) Group("games-net", GamesChecks.RunNet);
        if (Wanted("games-pictures")) Group("games-pictures", GamesChecks.RunPictures);
        if (Wanted("games-pages")) Group("games-pages", LgPagesChecks.Run);
        if (Wanted("languages")) Group("languages", LangChecks.Run);
        if (Wanted("emulator")) Group("emulator", EmuChecks.Run);
        if (Wanted("spelling")) Group("spelling", SpellingChecks.Run);
        if (Wanted("math-mastery")) Group("math-mastery", MasteryChecks.Run);
        if (Wanted("network")) Group("network", global::NetworkLab.NetChecks.Run);
        if (Wanted("network-desktops")) Group("network-desktops", NetDesktopPages.Run);
        if (Wanted("network-physical")) Group("network-physical", NetPhysicalPages.Run);
        if (Wanted("certlab-portal")) Group("certlab-portal", PortalScreens.Run);   // the MD-102 portal and AZ-800 consoles (CertLab\Portal)
        // Cert Lab and Cyber Range: "certlab" = App Hub's -Test -Quick (one seed per challenge), "certlab-full" = every
        // seed; -Only certlab:<group>[:file] runs one group of the full set (certlab:labs:lab-05, certlab:range-puzzles)
        if (only != null && only.Contains("certlab-full")) Group("certlab-full", () => global::CertLab.Tests.Run(false, null));
        else if (Wanted("certlab")) Group("certlab", () => global::CertLab.Tests.Run(true, null));
        if (Wanted("certlab-screens")) Group("certlab-screens", CertScreens.Run);
        if (Wanted("range-screens")) Group("range-screens", RangeScreens.Run);   // Cyber Range's screens (CyberRange\)
        foreach (string o in only ?? new HashSet<string>())
            if (o.StartsWith("certlab:", StringComparison.OrdinalIgnoreCase)) Group(o, () => global::CertLab.Tests.Run(false, o.Substring(8)));
    }

    static void Group(string name, Action run)
    {
        Console.WriteLine("-- " + name);
        try { run(); }
        catch (Exception e) { Check(name + ": ran without an error", false, e.ToString()); }
    }
}
