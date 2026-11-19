// Emulator Lab - the engine checks App Hub ran with "EmulatorLab.ps1 -Test" (copied unchanged; Learning Lab runs them in
// the group "emulator", EmuChecks.cs): processor conformance programs (tests\*.s), the assembler and disassembler,
// LabOS booting, its shell and file system, the demos, the background runner and the speed.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace EmulatorLab
{
    public static partial class Tests
    {
        static List<string> log;
        static int passed, failed;

        static void Check(bool ok, string what, string detail = null)
        {
            if (ok) { passed++; log.Add("  ok    " + what); }
            else { failed++; log.Add("  FAIL  " + what + (detail != null ? ": " + detail : "")); }
        }

        public static List<string> RunAll(string appRoot, string tempDir)
        {
            log = new List<string>();
            passed = 0; failed = 0;
            var total = Stopwatch.StartNew();
            Directory.CreateDirectory(tempDir);
            try { Processor(appRoot); } catch (Exception e) { Check(false, "processor tests", e.ToString()); }
            try { AssemblerChecks(appRoot); } catch (Exception e) { Check(false, "assembler tests", e.ToString()); }
            try { LabOs(appRoot, tempDir); } catch (Exception e) { Check(false, "LabOS tests", e.ToString()); }
            try { RunnerChecks(appRoot, tempDir); } catch (Exception e) { Check(false, "runner tests", e.ToString()); }
            try { DiskSaves(appRoot, tempDir); } catch (Exception e) { Check(false, "disk saves", e.ToString()); }
            try { Speed(appRoot); } catch (Exception e) { Check(false, "speed test", e.ToString()); }
            log.Add(string.Format("{0} in {1:0.0} s", failed == 0 ? "ALL PASSED (" + passed + " checks)" : failed + " FAILED, " + passed + " passed", total.Elapsed.TotalSeconds));
            return log;
        }

        // ------------------------------------------------------------------ the processor
        static void Processor(string appRoot)
        {
            log.Add("Processor (tests\\*.s, bare metal):");
            string dir = Path.Combine(appRoot, "tests");
            var files = new List<string>(Directory.GetFiles(dir, "*.s"));
            files.Sort(StringComparer.OrdinalIgnoreCase);
            using (var c = new Computer(Path.Combine(appRoot, "os")))
            {
                c.StopWhenWaitingForKeys = false;
                foreach (var f in files)
                {
                    string name = Path.GetFileNameWithoutExtension(f);
                    if (name.StartsWith("_")) continue;
                    var a = Computer.NewAssembler(dir).Assemble(File.ReadAllText(f), f, Map.RamBase);
                    if (!a.Ok) { Check(false, name, "doesn't assemble: " + a.ErrorText()); continue; }
                    c.PowerOnBare(a.Image);
                    long n = c.RunVirtual(5000000);
                    uint code = c.M.PowerCode;
                    if (!c.M.PoweredOff) Check(false, name, "didn't finish (pc 0x" + c.M.Pc.ToString("x8") + " after " + n + " instructions)");
                    else if ((code & 0xffff) == 0x5555) Check(true, name + " (" + n + " instructions)");
                    else Check(false, name, "check " + (code >> 16) + " failed (gp = test number; see the file)");
                }
                // deterministic: the same program stepped one instruction at a time ends in the same state
                var t = Computer.NewAssembler(dir).Assemble(File.ReadAllText(Path.Combine(dir, "timer.s")), "timer.s", Map.RamBase);
                c.PowerOnBare(t.Image);
                long n1 = c.RunVirtual(5000000);
                ulong time1 = c.M.Mtime, inst1 = c.M.Instret;
                c.PowerOnBare(t.Image);
                long n2 = 0;
                lock (c.Sync)
                {
                    c.M.VirtualTime = true;
                    while (!c.M.PoweredOff && n2 < 5000000)
                    {
                        if (c.M.Waiting && !c.M.InterruptPending) c.M.AdvanceTime(Math.Max(1, c.M.TimeToTimer()));
                        c.M.Step(); n2++;
                    }
                }
                Check(c.M.PoweredOff && c.M.Instret == inst1 && c.M.Mtime == time1, "stepping one at a time gives the same result as running (deterministic)",
                    "instret " + inst1 + " / " + c.M.Instret + ", mtime " + time1 + " / " + c.M.Mtime);
            }
        }

        // ------------------------------------------------------------------ assembler and disassembler
        static uint[] Words(AsmResult r)
        {
            var w = new uint[r.Image.Length / 4];
            for (int i = 0; i < w.Length; i++) w[i] = BitConverter.ToUInt32(r.Image, i * 4);
            return w;
        }

        static void Enc(string src, params uint[] expect)
        {
            var r = new Assembler().Assemble(src, "test", 0x80000000);
            if (!r.Ok) { Check(false, "encode \"" + src + "\"", r.ErrorText().Trim()); return; }
            var w = Words(r);
            bool ok = w.Length == expect.Length;
            for (int i = 0; ok && i < w.Length; i++) ok = w[i] == expect[i];
            var got = new StringBuilder();
            foreach (var x in w) got.Append("0x" + x.ToString("x8") + " ");
            Check(ok, "encode \"" + src.Replace("\n", "; ") + "\"", "got " + got);
        }

        static void Err(string src, int line, string fragment)
        {
            var a = new Assembler { ReadFile = p => null, BaseDir = "" };
            var r = a.Assemble(src, "test.s", 0x80000000);
            bool ok = !r.Ok && r.Errors[0].Line == line && r.Errors[0].Message.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;
            Check(ok, "error for \"" + src.Replace("\n", "; ") + "\"", r.Ok ? "no error" : r.Errors[0].ToString());
        }

        static void AssemblerChecks(string appRoot)
        {
            log.Add("Assembler:");
            Enc("addi a0, a0, 1", 0x00150513);
            Enc("add a0, a1, a2", 0x00c58533);
            Enc("sub t0, t1, t2", 0x407302b3);
            Enc("lw a0, 8(sp)", 0x00812503);
            Enc("sw ra, 12(sp)", 0x00112623);
            Enc("sb a1, -1(a0)", 0xfeb50fa3);
            Enc("beq a0, a1, .", 0x00b50063);
            Enc("bne a0, zero, .-4", 0xfe051ee3);
            Enc("jal ra, .", 0x000000ef);
            Enc("jalr ra, 0(t0)", 0x000280e7);
            Enc("lui a0, 0x12345", 0x12345537);
            Enc("auipc t0, 1", 0x00001297);
            Enc("srai a0, a0, 3", 0x40355513);
            Enc("ecall\nebreak\nmret\nwfi", 0x00000073, 0x00100073, 0x30200073, 0x10500073);
            Enc("csrrw t0, mscratch, t1", 0x340312f3);
            Enc("csrrsi zero, mstatus, 8", 0x30046073);
            Enc("mul a0, a1, a2\ndivu a0, a1, a2\nremu a0, a1, a2", 0x02c58533, 0x02c5d533, 0x02c5f533);
            Enc("li a0, 5", 0x00500513);
            Enc("li a0, 0x12345678", 0x12345537, 0x67850513);
            Enc("li a0, 0x12345fff", 0x12346537, 0xfff50513);
            Enc("li a0, -1", 0xfff00513);
            Enc("li a0, 0x80000000", 0x80000537);
            Enc("lui a0, %hi(0x12345fff)\naddi a0, a0, %lo(0x12345fff)", 0x12346537, 0xfff50513);
            Enc("la a0, x\nx: nop", 0x00000517, 0x00850513, 0x00000013);
            Enc("mv a0, a1\nnot a0, a1\nneg a0, a1\nseqz a0, a1\nsnez a0, a1", 0x00058513, 0xfff5c513, 0x40b00533, 0x0015b513, 0x00b03533);
            Enc("j .\ncall .\nret\njr t0", 0x0000006f, 0x000000ef, 0x00008067, 0x00028067);
            Enc("beqz a0, .\nbgt a0, a1, .\nble a0, a1, .", 0x00050063, 0x00a5c063, 0x00a5d063);
            Enc("csrr a0, mcause\ncsrw mtvec, t0", 0x34202573, 0x30529073);
            Enc(".equ N, 3\naddi a0, a0, N * 4 + 1", 0x00d50513);
            Enc("1: j 1f\n1: j 1b", 0x0040006f, 0x0000006f);
            Enc(".macro TWICE r\naddi \\r, \\r, 1\naddi \\r, \\r, 1\n.endm\nTWICE a0", 0x00150513, 0x00150513);
            Enc("addi a0, a0, 'A'", 0x04150513);
            {
                var r = new Assembler().Assemble(".data\n.byte 1, 2\n.half 0x304\n.word 0x8070605\n.ascii \"ab\"\n.asciz \"c\\n\"\n.align 2\n.space 3, 9", "t", 0x80000000);
                string hex = BitConverter.ToString(r.Image);
                Check(r.Ok && hex == "01-02-04-03-05-06-07-08-61-62-63-0A-00-00-00-00-09-09-09", "data directives", hex + " " + r.ErrorText());
            }
            {
                var r = new Assembler().Assemble("nop\n.data\nv: .word v", "t", 0x80000000);
                Check(r.Ok && r.Symbols["v"] == 0x80000010 && BitConverter.ToUInt32(r.Image, 16) == 0x80000010, ".data goes after .text (16-byte aligned) and labels in it work", r.ErrorText());
            }
            // errors, with the right line numbers
            Err("nop\naddi a0, a0", 2, "needs 3 operands");
            Err("addi a0, a0, 5000", 1, "between -2048 and 2047");
            Err("nop\n\nfoo a0", 3, "unknown instruction");
            Err("lwz a0, 0(a1)", 1, "did you mean \"lw\"");
            Err("j nowhere", 1, "unknown label");
            Err("x:\nx:", 2, "defined twice");
            Err("addi a0, q7, 1", 1, "not a register");
            Err(".include \"missing.s\"", 1, "can't find");
            Err("beq a0, a1, far\n.space 8000\nfar: nop", 1, "too far");
            Err("slli a0, a0, 32", 1, "0 to 31");
            Err(".byte 300", 1, "doesn't fit");
            Err("lw a0, 4(a9)", 1, "");
            Err(".asciz \"open", 1, "double quotes");

            // disassembler round trip: every instruction of LabOS disassembles to text that assembles back to the same word
            var c = new Computer(Path.Combine(appRoot, "os"));
            bool built = c.Build();
            Check(built, "LabOS, the boot ROM and the demos assemble", c.BuildError);
            if (!built) return;
            int count = 0, bad = 0;
            string firstBad = null;
            var words = Words(c.KernelAsm);
            var asm = new Assembler();
            for (int i = 0; i < c.KernelAsm.TextSize / 4; i++)
            {
                uint pc = Map.RamBase + (uint)i * 4;
                string text = Disassembler.Disassemble(words[i], pc);
                if (text.StartsWith(".word")) continue;   // data inside the code (none expected)
                var back = asm.Assemble(text, "dis", pc);
                count++;
                if (!back.Ok || back.Image.Length != 4 || BitConverter.ToUInt32(back.Image, 0) != words[i])
                {
                    bad++;
                    if (firstBad == null) firstBad = "0x" + words[i].ToString("x8") + " -> \"" + text + "\" -> " + (back.Ok ? "0x" + BitConverter.ToUInt32(back.Image, 0).ToString("x8") : back.ErrorText());
                }
            }
            Check(bad == 0 && count > 1000, "disassembling LabOS and assembling it again gives the same " + count + " instructions", firstBad);
            Check(Disassembler.Disassemble(0x00000000, 0).StartsWith(".word"), "0x00000000 is not an instruction");
        }

        // ------------------------------------------------------------------ LabOS
        static void LabOs(string appRoot, string tempDir)
        {
            log.Add("LabOS:");
            string disk = Path.Combine(tempDir, "test-disk.img");
            if (File.Exists(disk)) File.Delete(disk);
            using (var c = new Computer(Path.Combine(appRoot, "os")))
            {
                if (!c.Build()) { Check(false, "build", c.BuildError); return; }
                c.M.Rtc.Fixed = new DateTime(2026, 10, 15, 9, 41, 7);
                c.AttachDisk(disk);
                c.PowerOn();
                long n = c.RunVirtual(60000000);
                string t = c.Term.Transcript;
                Check(t.EndsWith("lab> "), "boots to the prompt in " + n.ToString("N0") + " instructions (limit 60 000 000)", Tail(t));
                Check(t.Contains("Emulator Lab boot ROM") && t.Contains("Testing memory  ############### OK"), "boot ROM message and memory test");
                Check(t.Contains("formatting it for LabFS"), "a new disk is formatted on first boot");
                Check(c.M.FaultCount == 0, "no exceptions while booting");

                string o = c.TypeAndRun("help\n");
                Check(o.Contains("run      start a program") && o.Contains("halt     switch the computer off"), "help lists the commands", Tail(o));
                o = c.TypeAndRun("echo Hello, world!\n");
                Check(o.Contains("\nHello, world!\n"), "echo", Tail(o));
                o = c.TypeAndRun("date\n");
                Check(o.Contains("It is Thursday 15 October 2026, 09:41:07"), "date (fixed clock, 15 Oct 2026 09:41:07)", Tail(o));
                o = c.TypeAndRun("clock\n");
                Check(Regex.IsMatch(o, @"running for 0:00:\d\d \(\d+ timer ticks\)"), "clock", Tail(o));
                o = c.TypeAndRun("mem\n");
                Check(o.Contains("RAM: 16 MB, tested OK") && o.Contains("15360 KB"), "mem", Tail(o));
                o = c.TypeAndRun("peek 0x20000000\n");
                Check(o.Contains("0x20000000: 4c 41 42 46"), "peek shows the flash header (LABF)", Tail(o));
                o = c.TypeAndRun("frobnicate\n");
                Check(o.Contains("I don't know the command \"frobnicate\""), "unknown command", Tail(o));
                o = c.TypeAndRun("color yellow blue\n");
                Check(o.Contains("New colours.") && c.Term.Color == 0x1E, "color yellow blue", "attr 0x" + c.Term.Color.ToString("x2"));
                c.TypeAndRun("color grey black\n");

                // the line editor: backspace, Esc, history
                o = c.TypeAndRun("echo abX\bc\n");
                Check(o.Contains("\nabc\n"), "backspace", Tail(o));
                o = c.TypeAndRun("echo junk\x1b" + "echo fresh\n");
                Check(o.Contains("\nfresh\n") && !o.Contains("junkecho"), "Esc clears the line", Tail(o));
                c.M.Keyboard.Push(0, 0x48);   // Up: the last line again
                o = c.TypeAndRun("\n");
                Check(o.Contains("\nfresh\n"), "Up brings back the last line", Tail(o));

                // the file system
                o = c.TypeAndRun("write notes Buy milk and eggs\n");
                Check(o.Contains("Saved notes"), "write", Tail(o));
                o = c.TypeAndRun("cat notes\n");
                Check(o.Contains("\nBuy milk and eggs\n"), "cat", Tail(o));
                o = c.TypeAndRun("write poem\nRoses are red,\nviolets are blue.\n.\n");
                o = c.TypeAndRun("cat poem\n");
                Check(o.Contains("Roses are red,\nviolets are blue.\n"), "write several lines, then cat", Tail(o));
                o = c.TypeAndRun("ls\n");
                Check(o.Contains("notes") && o.Contains("18 bytes") && o.Contains("poem") && o.Contains("bounce") && o.Contains("snake"), "ls shows files and built-in programs", Tail(o));
                var fs = c.Fs;
                Check(Encoding.ASCII.GetString(fs.Read("notes") ?? new byte[0]) == "Buy milk and eggs\n", "the host reads the same file from the disk image");
                fs.Write("fromhost", Encoding.ASCII.GetBytes("Written by the host\n"));
                o = c.TypeAndRun("cat fromhost\n");
                Check(o.Contains("Written by the host"), "LabOS reads a file the host wrote", Tail(o));
                o = c.TypeAndRun("rm notes\n");
                o = c.TypeAndRun("cat notes\n");
                Check(o.Contains("no file called \"notes\""), "rm", Tail(o));
                // survives a reboot (the disk is kept, memory is tested again)
                o = c.TypeAndRun("reboot\n", 80000000);
                Check(o.Contains("Restarting") && o.Contains("boot ROM") && c.Term.Transcript.EndsWith("lab> ") && !o.Contains("formatting"), "reboot (the disk is not formatted again)", Tail(o));
                o = c.TypeAndRun("cat poem\n");
                Check(o.Contains("violets are blue"), "files are still there after a reboot", Tail(o));

                // programs
                o = c.TypeAndRun("run hello\n");
                Check(o.Contains("Hello! This message was printed by a RISC-V program"), "run hello", Tail(o));
                long faults = c.M.FaultCount;
                c.M.Keyboard.Type("run bounce\n");
                long ran = c.RunVirtual(8000000);
                Check(c.M.FaultCount == faults && c.M.Video.Mode == 2 && ran == 8000000, "bounce runs 8 million instructions without a fault", "faults " + (c.M.FaultCount - faults) + ", mode " + c.M.Video.Mode);
                int colours = CountColours(c.M.Video.Fb);
                Check(colours > 20, "bounce draws (" + colours + " colours on screen)");
                o = c.TypeAndRun("\x03");
                Check(o.Contains("^C") && c.Term.Transcript.EndsWith("lab> ") && c.M.Video.Mode == 0, "Ctrl+C stops a program", Tail(o));
                c.M.Keyboard.Type("run snake demo\n");
                ran = c.RunVirtual(20000000);
                string snake = c.Term.Transcript;
                Check(c.M.FaultCount == faults && ran == 20000000 && snake.Contains("Score: 5"), "snake (the computer playing) runs 20 million instructions without a fault and scores", Tail(snake));
                o = c.TypeAndRun("\x1b");
                Check(c.Term.Transcript.EndsWith("lab> "), "Esc ends snake");

                // guess the number, played by halving
                o = c.TypeAndRun("run guess\n");
                int lo = 1, hi = 100, tries = 0;
                bool won = false;
                while (tries < 10)
                {
                    int g = (lo + hi) / 2;
                    tries++;
                    o = c.TypeAndRun(g + "\n");
                    if (o.Contains("You got it")) { won = true; break; }
                    if (o.Contains("Too high")) hi = g - 1; else if (o.Contains("Too low")) lo = g + 1; else break;
                }
                Check(won && tries <= 7, "guess the number: found it in " + tries + " guesses", Tail(o));
                o = c.TypeAndRun("n\n");
                Check(o.Contains("Thanks for playing") && c.Term.Transcript.EndsWith("lab> "), "guess ends", Tail(o));

                // a program that crashes goes back to the shell; one that writes outside RAM too
                var bad = new Assembler().Assemble("li a0, 1\n.word 0\n", "bad", Computer.ProgramBase);
                fs.Write("crash", bad.Image);
                o = c.TypeAndRun("run crash\n");
                Check(o.Contains("The program crashed: Illegal instruction at 0x80100004") && c.Term.Transcript.EndsWith("lab> "), "a crashing program returns to the shell", Tail(o));
                var bad2 = new Assembler().Assemble("li t0, 0x40000000\nsw zero, 0(t0)\n", "bad", Computer.ProgramBase);
                fs.Write("crash2", bad2.Image);
                o = c.TypeAndRun("run crash2\n");
                Check(o.Contains("Memory write to an address that can't be written") && o.Contains("address 0x40000000"), "a bad store is reported with its address", Tail(o));
                foreach (var exf in Directory.GetFiles(Path.Combine(appRoot, "os", "examples"), "*.s"))
                {
                    var exr = c.AssembleUser(File.ReadAllText(exf), exf, Path.GetFileName(exf).StartsWith("bare"));
                    Check(exr.Ok, "example " + Path.GetFileName(exf) + " assembles", exr.ErrorText());
                }
                // the Assembler tab's "Assemble & run": save to the disk, then type run
                var cnt = c.AssembleUser(File.ReadAllText(Path.Combine(appRoot, "os", "examples", "count.s")), null, false);
                fs.Write("count", cnt.Image);
                o = c.TypeAndRun("run count\n");
                Check(o.Contains("1 2 3 4 5 6 7 8 9 10 \n"), "example count.s runs from the disk", Tail(o));
                var fib = c.AssembleUser(File.ReadAllText(Path.Combine(appRoot, "os", "examples", "fibonacci.s")), null, false);
                fs.Write("fib", fib.Image);
                o = c.TypeAndRun("run fib\n");
                Check(o.Contains("2584 4181"), "example fibonacci.s", Tail(o));

                o = c.TypeAndRun("halt\n");
                Check(c.M.PoweredOff && (c.M.PowerCode & 0xffff) == 0x5555, "halt switches off");

                // "Run without LabOS"
                var bare = c.AssembleUser(File.ReadAllText(Path.Combine(appRoot, "os", "examples", "bare_metal.s")), null, true);
                c.PowerOnBare(bare.Image);
                c.RunVirtual(1000000);
                Check(c.M.PoweredOff && c.Term.Transcript.Contains("ABCDEFGHIJKLMNOPQRSTUVWXYZ"), "bare_metal.s runs without LabOS and switches off", Tail(c.Term.Transcript));
            }
            try { File.Delete(disk); } catch (Exception) { }
        }

        static int CountColours(byte[] fb)
        {
            var seen = new bool[256]; int n = 0;
            foreach (byte b in fb) if (!seen[b]) { seen[b] = true; n++; }
            return n;
        }

        static string Tail(string s) { s = s ?? ""; s = s.Length > 300 ? "..." + s.Substring(s.Length - 300) : s; return s.Replace("\n", "\\n"); }

        // ------------------------------------------------------------------ the background thread (what the window uses)
        static bool WaitFor(Func<bool> ok, int ms)
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms) { if (ok()) return true; System.Threading.Thread.Sleep(20); }
            return ok();
        }

        static void RunnerChecks(string appRoot, string tempDir)
        {
            log.Add("Background runner (real time, as in the window):");
            string disk = Path.Combine(tempDir, "runner-disk.img");
            if (File.Exists(disk)) File.Delete(disk);
            using (var c = new Computer(Path.Combine(appRoot, "os")))
            {
                if (!c.Build()) { Check(false, "build", c.BuildError); return; }
                c.AttachDisk(disk);
                c.PowerOn();
                using (var r = new Runner(c))
                {
                    var sw = Stopwatch.StartNew();
                    r.Speed = 0;
                    r.Resume();
                    bool prompt = WaitFor(() => c.Term.Transcript.EndsWith("lab> "), 15000);
                    Check(prompt, string.Format("boots to the prompt in {0:0.0} s of real time", sw.Elapsed.TotalSeconds), Tail(c.Term.Transcript));
                    System.Threading.Thread.Sleep(300);
                    ulong i0 = c.M.Instret;
                    uint ticksAt = c.KernelAsm.Symbols["ticks"];
                    uint t0 = c.M.ReadWord(ticksAt);
                    System.Threading.Thread.Sleep(1000);
                    ulong idle = c.M.Instret - i0;
                    uint ticks = c.M.ReadWord(ticksAt) - t0;
                    Check(idle < 3000000, "waiting for a key, the processor rests (" + idle.ToString("N0") + " instructions in a second)");
                    Check(ticks >= 70 && ticks <= 130, "the LabOS clock ticks about 100 times a second of real time (" + ticks + ")");

                    c.M.Keyboard.Type("run bounce\n");
                    System.Threading.Thread.Sleep(800);
                    byte[] f1 = (byte[])c.M.Video.Fb.Clone();
                    System.Threading.Thread.Sleep(300);
                    bool moved = false;
                    for (int i = 0; i < f1.Length && !moved; i++) moved = f1[i] != c.M.Video.Fb[i];
                    Check(moved && c.M.Video.Mode == 2, "bounce animates on the background thread");

                    r.Pause();
                    ulong a = c.M.Instret;
                    System.Threading.Thread.Sleep(200);
                    Check(c.M.Instret == a && !r.Running, "Pause stops the processor");
                    r.StepOne();
                    Check(c.M.Instret == a + 1, "Step runs one instruction", (c.M.Instret - a).ToString());

                    uint rect = c.KernelAsm.Symbols["gfx_rect"];
                    r.ToggleBreak(rect);
                    r.Resume();
                    Check(WaitFor(() => !r.Running, 5000) && c.M.Pc == rect && r.StopReason.StartsWith("Breakpoint"), "a breakpoint stops at gfx_rect", r.StopReason);
                    r.ClearBreaks();

                    // step over a call: stop at a call in the shell, then step over it
                    uint loop = c.KernelAsm.Symbols["shell_loop"], call = 0;
                    for (uint p = loop; p < loop + 64; p += 4) if ((c.M.ReadWord(p) & 0xfff) == 0x0ef) { call = p; break; }   // jal ra, ...
                    r.ToggleBreak(call);
                    r.Resume();
                    c.M.Keyboard.Type("x");   // any key ends bounce; back at the prompt, shell_loop runs
                    bool atCall = WaitFor(() => !r.Running, 5000) && c.M.Pc == call;
                    r.ClearBreaks();
                    r.StepOver();
                    bool over = WaitFor(() => !r.Running, 5000) && c.M.Pc == call + 4;
                    Check(atCall && over, "Step over runs a whole call and stops after it", "pc 0x" + c.M.Pc.ToString("x8") + ", " + r.StopReason);

                    // slow motion: about 1000 instructions a second
                    c.M.Keyboard.Type("run bounce\n");
                    r.Speed = 1000;
                    r.Resume();
                    System.Threading.Thread.Sleep(400);
                    ulong s0 = c.M.Instret;
                    System.Threading.Thread.Sleep(2000);
                    double rate = (c.M.Instret - s0) / 2.0;
                    Check(rate > 500 && rate < 1500, "speed 1000: about 1000 instructions a second (" + rate.ToString("0") + ")", "running " + r.Running + ", " + r.StopReason + " " + r.LastError);
                    r.Pause();
                }
            }
            try { File.Delete(disk); } catch (Exception) { }
        }

        // ------------------------------------------------------------------ speed
        static void Speed(string appRoot)
        {
            log.Add("Speed:");
            using (var c = new Computer(Path.Combine(appRoot, "os")))
            {
                c.StopWhenWaitingForKeys = false;
                // a loop with a mix of arithmetic, memory and branches
                string src = "li sp, 0x80800000\nli s0, 0\nli s1, 0\nloop: addi s0, s0, 1\nsw s0, -4(sp)\nlw t0, -4(sp)\nxor s1, s1, t0\nslli t1, s1, 3\nadd s1, s1, t1\nandi t2, s0, 7\nbnez t2, 1f\nmul s1, s1, s0\n1: j loop\n";
                var a = new Assembler().Assemble(src, "speed", Map.RamBase);
                c.PowerOnBare(a.Image);
                c.RunVirtual(2000000);   // warm up the JIT
                var sw = Stopwatch.StartNew();
                long n = c.RunVirtual(100000000);
                double mips = n / sw.Elapsed.TotalSeconds / 1e6;
                log.Add(string.Format("  {0:0.0} million instructions per second (virtual time, one thread)", mips));
                Check(mips > 10, "faster than 10 MIPS");
                // the same program on the background thread, in real time with the timer, as the window runs it
                c.PowerOnBare(a.Image);
                using (var r = new Runner(c))
                {
                    r.Resume();
                    System.Threading.Thread.Sleep(1500);
                    double ips = r.Ips;
                    r.Pause();
                    log.Add(string.Format("  {0:0.0} million instructions per second on the background thread (real time)", ips / 1e6));
                    Check(ips > 10e6, "the background thread runs faster than 10 MIPS");
                }
            }
        }
    }
}
