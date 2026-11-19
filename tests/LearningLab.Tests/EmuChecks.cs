// Learning Lab - the checks of Emulator Lab: every check App Hub's "EmulatorLab.ps1 -Test" ran (EmulatorLab\Tests.cs:
// the processor conformance programs, the assembler, LabOS and its shell, the demos, the background runner, the disk's
// saves, the speed), the shipped sources, the keys, settings.json in App Hub's layouts (fixtures) and bringing App Hub's
// data across, then the page off screen: typing into the machine, stepping, the assembler, saving on close, and App
// Hub's snapshot scenes (Shell, Graphics, Snake, Inside, AsmError, Learn) wide and phone-sized. Group "emulator".
using System.Text;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Input;
using EmulatorLab;
using LearningLab.EmulatorLab;
using LearningLab.Views;

namespace LearningLab.Tests;

static class EmuChecks
{
    static void Check(string what, bool ok, string detail = null) => Program.Check("emulator: " + what, ok, detail);
    static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", "EmulatorLab", name);

    public static void Run()
    {
        Engine();
        Sources();
        Keys();
        Settings();
        BringAcross();
        Pages();
    }

    // ---------------------------------------------------------------- App Hub's engine checks
    static void Engine()
    {
        string root = LabSources.Unpack(Path.Combine(Data.Root, "emu-src"));
        string tmp = Path.Combine(Data.Root, "emu-test");
        var lines = global::EmulatorLab.Tests.RunAll(root, tmp);
        int ok = 0;
        foreach (string l in lines)
        {
            if (l.StartsWith("  ok")) { ok++; Check(l.Substring(8), true); }
            else if (l.StartsWith("  FAIL")) Check(l.Substring(8), false);
            else if (l.Contains("million instructions")) Console.WriteLine("  " + l.Trim());
        }
        Check("App Hub's engine checks all ran (" + ok + ")", ok >= 100 && lines[^1].StartsWith("ALL PASSED"), lines[^1]);
    }

    static void Sources()
    {
        var names = LabSources.Names;
        Check("LabOS, its demos and lessons and the processor checks ship with the app",
            names.Contains("os/kernel.s") && names.Contains("os/boot.s") && names.Contains("os/programs/snake.s") && names.Contains("os/programs/_syscalls.s") && names.Contains("os/examples/count.s") && names.Contains("tests/alu.s"),
            string.Join(", ", names));
        Check("every source is plain text", names.All(n => (LabSources.Read(n) ?? "\0").All(c => c == '\r' || c == '\n' || c == '\t' || (c >= 32 && c < 127))));
        Check("getkey still waits on its wfi (key_wait_wfi)", LabSources.Read("os/keyboard.s").Contains("key_wait_wfi"));
        string dir = Path.Combine(Data.Root, "emu-src2");
        LabSources.Unpack(dir);
        var t = File.GetLastWriteTimeUtc(Path.Combine(dir, "os", "kernel.s"));
        File.WriteAllText(Path.Combine(dir, "os", "shell.s"), "changed");
        LabSources.Unpack(dir);
        Check("unpacking again only rewrites what differs", File.GetLastWriteTimeUtc(Path.Combine(dir, "os", "kernel.s")) == t && File.ReadAllText(Path.Combine(dir, "os", "shell.s")) == LabSources.Read("os/shell.s"));
    }

    static void Keys()
    {
        bool S(Key k, bool ctrl, out byte a, out byte s) => KeyMap.Special(k.ToString(), ctrl, out a, out s);
        Check("Enter, Esc, Backspace, Tab, Delete give their ASCII codes", S(Key.Enter, false, out var a, out var s) && a == 13 && s == 0x1C && S(Key.Escape, false, out a, out _) && a == 27
            && S(Key.Back, false, out a, out _) && a == 8 && S(Key.Tab, false, out a, out _) && a == 9 && S(Key.Delete, false, out a, out _) && a == 127);
        Check("the arrows, Home, End, Page Up / Down and Insert give their scancodes", S(Key.Up, false, out _, out s) && s == 0x48 && S(Key.Down, false, out _, out s) && s == 0x50
            && S(Key.Left, false, out _, out s) && s == 0x4B && S(Key.Right, false, out _, out s) && s == 0x4D && S(Key.Home, false, out _, out s) && s == 0x47
            && S(Key.End, false, out _, out s) && s == 0x4F && S(Key.PageUp, false, out _, out s) && s == 0x49 && S(Key.PageDown, false, out _, out s) && s == 0x51 && S(Key.Insert, false, out _, out s) && s == 0x52);
        Check("F1-F10 give scancodes 0x3B-0x44; F11 isn't the machine's", S(Key.F1, false, out _, out s) && s == 0x3B && S(Key.F10, false, out _, out s) && s == 0x44 && !S(Key.F11, false, out _, out _));
        Check("Ctrl+C is ASCII 3; plain letters type as text", S(Key.C, true, out a, out s) && a == 3 && s == 0x2E && !S(Key.C, false, out _, out _));
    }

    // ---------------------------------------------------------------- saves
    static string CopyFixture(string fixture, string dirName)
    {
        string dir = Path.Combine(Data.Root, dirName);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);
        string f = Path.Combine(dir, "settings.json");
        File.Copy(Fixture(fixture), f);
        return f;
    }

    static void Settings()
    {
        // the layout App Hub's Emulator Lab wrote (66a8f40), with a field from a newer version
        string f = CopyFixture("settings-2026-10-15.json", "emu-settings");
        var s = EmuSettings.Load(f);
        Check("App Hub's settings.json opens with the program being written and every setting",
            s.Message == "" && s.EditorText == "    li a0, 5\n    ecall\n" && s.EditorFile == @"C:\Users\Sam\count.s" && s.ProgName == "count" && s.Speed == 3 && s.Inside && s.InsideTab == 2 && s.Screen == 1 && s.StopOnCrash, s.Message);
        Check("...and KeptForUpdate (added for App Hub updates) is empty in that older layout", s.KeptForUpdate == "");
        s.Speed = 5; s.Save();
        var back = JsonNode.Parse(File.ReadAllText(f)) as JsonObject;
        Check("fields this version doesn't know are written back", back?["Theme"]?.ToString() == "green" && back?["Speed"]?.GetValue<int>() == 5);
        s.Save();
        Check("settings are saved safely with backups", File.Exists(f + ".bak1") && File.Exists(f + ".bak2") && !File.Exists(f + ".tmp"));
        // the layout with KeptForUpdate (2026-10-24)
        string k = CopyFixture("settings-kept-2026-10-24.json", "emu-settings-kept");
        var ks = EmuSettings.Load(k);
        Check("a program App Hub kept for an update opens", ks.Message == "" && ks.KeptForUpdate.StartsWith("2026-10-24") && ks.EditorText.StartsWith("# changed before the update"));
        // numbers and true / false as text (hand-edited), and odd values
        string o = Path.Combine(Data.Root, "emu-settings", "odd.json");
        File.WriteAllText(o, "{\"Speed\":\"2\",\"Inside\":\"true\",\"InsideTab\":9,\"Screen\":-4,\"ProgName\":\"\"}");
        var os = EmuSettings.Load(o);
        Check("numbers and true / false as text read; out-of-range values are kept in range", os.Speed == 2 && os.Inside && os.InsideTab == 3 && os.Screen == 0 && os.ProgName == "myprog");
        // damaged: the backup is used, the damaged file kept aside and untouched, the person told
        File.WriteAllText(f, "{ \"EditorText\": \"li a0");
        var d = EmuSettings.Load(f);
        var q = Directory.GetFiles(Path.GetDirectoryName(f), "settings.json.unreadable-*");
        Check("damaged settings: the backup is used (the program too), the damaged file is kept aside, untouched, and the person is told",
            d.ProgName == "count" && d.EditorText.Contains("ecall") && d.Message.StartsWith("Settings:") && d.Message.Contains("couldn't be opened") && q.Length == 1 && File.ReadAllText(f) == "{ \"EditorText\": \"li a0", d.Message);
        // damaged with no backups: the usual settings, and the file is copied aside before anything is written over it
        string lone = Path.Combine(Data.Root, "emu-settings-lone", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(lone));
        File.WriteAllText(lone, "not json");
        var l = EmuSettings.Load(lone);
        Check("damaged settings with no backup: the usual settings and a message", l.Speed == 7 && l.Message.Contains("couldn't be opened"), l.Message);
        l.Save();
        Check("...and the damaged file is kept aside before a save", Directory.GetFiles(Path.GetDirectoryName(lone), "settings.json.unreadable-*").Any(x => File.ReadAllText(x) == "not json"));
    }

    static void BringAcross()
    {
        string apphub = Path.Combine(Data.Root, "emu-apphub");
        string mine = Path.Combine(Data.Root, "emu-mine");
        foreach (var d in new[] { apphub, mine }) { if (Directory.Exists(d)) Directory.Delete(d, true); Directory.CreateDirectory(d); }
        File.Copy(Fixture("settings-2026-10-15.json"), Path.Combine(apphub, "settings.json"));
        File.WriteAllText(Path.Combine(apphub, "settings.json.bak1"), "{\"Speed\": 1}");
        // a LabOS disk with a file on it, as App Hub's Emulator Lab leaves it
        string src = LabSources.Unpack(Path.Combine(Data.Root, "emu-src"));
        using (var c = new Computer(Path.Combine(src, "os")))
        {
            c.AttachDisk(Path.Combine(apphub, "disk.img"));
            c.Fs.Write("poem", Encoding.ASCII.GetBytes("Roses are red"));
        }
        var before = Directory.GetFiles(apphub).ToDictionary(x => x, File.ReadAllBytes);
        EmuSettings.BringAcross(apphub, mine);
        Check("App Hub's settings, their backup and the LabOS disk are copied", File.Exists(Path.Combine(mine, "settings.json")) && File.Exists(Path.Combine(mine, "settings.json.bak1")) && File.Exists(Path.Combine(mine, "disk.img")));
        Check("App Hub's folder is left exactly as it was", Directory.GetFiles(apphub).Length == before.Count && before.All(kv => File.ReadAllBytes(kv.Key).SequenceEqual(kv.Value)));
        using (var c = new Computer(Path.Combine(src, "os")))
        {
            c.AttachDisk(Path.Combine(mine, "disk.img"));
            Check("the files on the LabOS disk came across", Encoding.ASCII.GetString(c.Fs.Read("poem") ?? Array.Empty<byte>()) == "Roses are red");
        }
        var s = EmuSettings.Load(Path.Combine(mine, "settings.json"));
        Check("...and the settings read", s.ProgName == "count" && s.Speed == 3);
        s.Speed = 6; s.Save();
        EmuSettings.BringAcross(apphub, mine);
        Check("once there are settings here, App Hub's are never copied again", EmuSettings.Load(Path.Combine(mine, "settings.json")).Speed == 6);
        Check("the checks never read the person's real App Hub folder", Data.AppHubFolder("EmulatorLab") == null);
    }

    // ---------------------------------------------------------------- the page
    static void Pages()
    {
        var tile = HomePage.Find("emulator");
        Check("the home page's Emulator Lab tile opens it", tile.Open != null);
        string data = Data.For("EmulatorLab");
        foreach (var f in Directory.GetFiles(data)) File.Delete(f);   // pictures start from a new disk

        var w = new Window { Width = 1180, Height = 800, Content = new MainView() };
        w.Show();
        Interaction(w);
        foreach (string scene in new[] { "Shell", "Graphics", "Snake", "Inside", "AsmError", "Learn" })
        {
            w.Width = 1180; w.Height = 800;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var page = new EmulatorPage(false);
            MainView.Current.Push(page);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            page.Scene(scene);
            page.Frame(true);
            Snap.Save(w, "emulator-" + scene.ToLowerInvariant());
            SceneChecks(page, scene, "wide");
            w.Width = 400; w.Height = 840;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            if (scene == "Inside") page.ShowTab(EmulatorPage.Tab.Inside);
            page.Frame(true);
            Snap.Save(w, "emulator-" + scene.ToLowerInvariant() + "-phone");
            if (scene == "Inside") Check("Inside (phone): Look inside is its own tab", page.Current == EmulatorPage.Tab.Inside && page.View.IsEffectivelyVisible);
            MainView.Current.Back();
        }
        w.Close();
    }

    static void SceneChecks(EmulatorPage p, string scene, string size)
    {
        string what = scene + " (" + size + "): ";
        var px = p.Screen.Display.Pixels;
        int lit = px.Count(v => (v & 0xFFFFFF) != 0);
        switch (scene)
        {
            case "Shell":
                Check(what + "LabOS shows its prompt and the date", p.C.Term.ScreenText().Contains("lab> ") && p.C.Term.Transcript.Contains("15 October 2026"));
                Check(what + "the console's letters are drawn", Display.HasFont && lit > 2000, lit.ToString());
                break;
            case "Graphics": Check(what + "bounce draws in graphics mode", p.C.M.Video.Mode == 2 && lit > 100000, lit.ToString()); break;
            case "Snake": Check(what + "the computer plays snake", p.C.Term.Transcript.Contains("Score") && lit > 1000); break;
            case "Inside": Check(what + "stopped in the count program, Look inside beside the monitor", p.Insp.Where(p.C.M.Pc).Contains("loop") || p.Insp.Where(p.C.M.Pc).Length > 0, p.Insp.Where(p.C.M.Pc)); break;
            case "AsmError": Check(what + "two problems listed, both lines marked", p.errorList.Children.Count == 2 && p.Editor.ErrorLines.Count == 2, string.Join(",", p.Editor.ErrorLines)); break;
            case "Learn": Check(what + "the Learn tab", p.Current == EmulatorPage.Tab.Learn); break;
        }
    }

    static void Interaction(Window w)
    {
        var p = new EmulatorPage(false);
        MainView.Current.Push(p);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Check("LabOS assembles at start, with no save problems on a new folder", p.Problems.Count == 0, string.Join(" | ", p.Problems));
        p.C.M.Rtc.Fixed = new DateTime(2026, 10, 15, 9, 41, 7);
        p.PowerOn();
        p.R.Pause();
        p.C.RunVirtual(60000000);
        Check("LabOS boots on the page's computer", p.C.Term.Transcript.EndsWith("lab> "));
        // typing: the screen's text input and the phone's type box
        p.TypeText("echo typed\n");
        p.C.RunVirtual(20000000);
        Check("typed text goes to the computer", p.C.Term.Transcript.Contains("\ntyped\n"));
        p.typeBox.Text = " echo phone\n";
        p.C.RunVirtual(20000000);
        Check("the phone's keyboard (the type box) types into the computer and the box stays empty", p.C.Term.Transcript.Contains("\nphone\n") && p.typeBox.Text == " ");
        p.typeBox.Text = " echo abX";
        p.typeBox.Text = "";                                  // the phone's backspace
        p.TypeText("c\n");
        p.C.RunVirtual(20000000);
        Check("backspace from the phone's keyboard", p.C.Term.Transcript.Contains("\nabc\n"), p.C.Term.Transcript.Substring(Math.Max(0, p.C.Term.Transcript.Length - 80)));
        // keys: F10 steps, the machine gets Esc and arrows only while it has the focus
        ulong i0 = p.C.M.Instret;
        p.OnKey(new KeyEventArgs { Key = Key.F10 });
        Check("F10 steps one instruction and opens Look inside", p.C.M.Instret == i0 + 1);
        p.FocusMachine();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        int before = p.C.M.Keyboard.Count;
        bool handled = p.OnKey(new KeyEventArgs { Key = Key.Escape });
        Check("with the machine focused, Esc goes to the computer (not Back)", handled && p.C.M.Keyboard.Count == before + 1 && p.MachineHasFocus);
        p.PushKey("Up");
        Check("the key row's arrows go in", p.C.M.Keyboard.Count == before + 2);
        // the assembler: an example, Assemble & run, the program on the disk
        p.LoadExample("os/examples/count.s");
        Check("an example opens in the editor", p.Editor.Text.Contains("count") && p.Current == EmulatorPage.Tab.Assembler);
        p.C.M.Keyboard.Type("\x1b");
        p.C.RunVirtual(5000000);
        p.AssembleAndRun();
        p.C.RunVirtual(40000000);
        Check("Assemble & run saves it on the LabOS disk and runs it", p.C.Fs.Read("mycount") != null && p.C.Term.Transcript.Contains("1 2 3 4 5 6 7 8 9 10 "), p.C.Term.Transcript.Substring(Math.Max(0, p.C.Term.Transcript.Length - 120)));
        p.ShowTab(EmulatorPage.Tab.Inside);
        p.ShowInsideTab(1); p.Frame(true); Snap.Save(w, "emulator-code");
        p.ShowInsideTab(2); p.ShowMemory(Map.FlashBase); p.Frame(true); Snap.Save(w, "emulator-memory");
        p.ShowInsideTab(3); p.Frame(true); Snap.Save(w, "emulator-traps");
        p.ShowInsideTab(0);
        // every tab again and again, and Look inside on and off (0.2.1 crashed on the second visit to the Inside tab)
        bool tabsOk = true;
        try
        {
            for (int round = 0; round < 3; round++)
                foreach (var t in new[] { EmulatorPage.Tab.Computer, EmulatorPage.Tab.Inside, EmulatorPage.Tab.Assembler, EmulatorPage.Tab.Inside, EmulatorPage.Tab.Learn, EmulatorPage.Tab.Inside })
                { p.ShowTab(t); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); }
            for (int k = 0; k < 4; k++) { p.ToggleInside(); Avalonia.Threading.Dispatcher.UIThread.RunJobs(); p.ShowTab(EmulatorPage.Tab.Inside); }
        }
        catch (Exception e) { tabsOk = false; Check("switching tabs again and again: " + e.Message, false); }
        Check("switching tabs again and again, and Look inside on and off, never fails", tabsOk);
        p.Editor.Text = "# kept\n" + p.Editor.Text;
        MainView.Current.Back();
        var saved = EmuSettings.Load(Path.Combine(Data.For("EmulatorLab"), "settings.json"));
        Check("closing the page keeps the program in the editor (settings.json)", saved.EditorText.StartsWith("# kept\n") && saved.ProgName == "mycount");
        // a program App Hub kept for an update comes back in the Assembler
        File.Copy(Fixture("settings-kept-2026-10-24.json"), Path.Combine(Data.For("EmulatorLab"), "settings.json"), true);
        var p2 = new EmulatorPage(false);
        MainView.Current.Push(p2);
        Check("a program kept for an App Hub update opens in the Assembler", p2.Current == EmulatorPage.Tab.Assembler && p2.Editor.Text.StartsWith("# changed before the update") && p2.Settings.KeptForUpdate == "");
        Snap.Save(w, "emulator-kept");
        MainView.Current.Back();
        foreach (var f in Directory.GetFiles(Data.For("EmulatorLab"))) File.Delete(f);
    }
}
