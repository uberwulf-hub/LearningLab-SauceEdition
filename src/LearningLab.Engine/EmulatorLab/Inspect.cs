// Emulator Lab - what the "Look inside" panel shows (registers, code, memory, stack, traps), the keyboard
// mapping, and the plain-language "What's happening?" texts.
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace EmulatorLab
{
    public sealed class RegRow
    {
        public string Name { get; set; }
        public string Hex { get; set; }
        public string Dec { get; set; }
        public string Tip { get; set; }
        public bool Changed { get; set; }
    }

    public sealed class CodeRow
    {
        public uint Address { get; set; }
        public string Marker { get; set; }
        public string AddrText { get; set; }
        public string Label { get; set; }
        public string Code { get; set; }
        public string Source { get; set; }
        public bool IsPc { get; set; }
        public bool IsBreak { get; set; }
        public bool HasLabel { get { return !string.IsNullOrEmpty(Label); } }
    }

    public sealed class Inspector
    {
        public readonly Computer C;
        public readonly Runner R;
        public readonly ObservableCollection<RegRow> Regs = new ObservableCollection<RegRow>();
        public readonly ObservableCollection<RegRow> Csrs = new ObservableCollection<RegRow>();
        public readonly ObservableCollection<CodeRow> Code = new ObservableCollection<CodeRow>();
        public readonly ObservableCollection<string> Traps = new ObservableCollection<string>();
        /// <summary>The program last assembled in the Assembler tab (for labels and source lines).</summary>
        public AsmResult UserProgram;
        readonly uint[] prev = new uint[33];
        long lastTrapSeq = -1;
        public uint? CodeAt;   // show code here instead of around the PC

        public Inspector(Computer c, Runner r) { C = c; R = r; }

        static string H(uint v) { return "0x" + v.ToString("x8"); }

        static readonly string[] RegTips = {
            "always 0: writes are thrown away", "return address: where a function goes back to", "stack pointer: the top of the stack",
            "global pointer", "thread pointer", "temporary", "temporary", "temporary", "saved (also the frame pointer)", "saved",
            "argument 0 / answer", "argument 1 / second answer", "argument 2", "argument 3", "argument 4", "argument 5", "argument 6",
            "argument 7: the system call number for ecall", "saved", "saved", "saved", "saved", "saved", "saved", "saved", "saved",
            "saved", "saved", "temporary", "temporary", "temporary", "temporary" };

        /// <summary>Reads the registers; <paramref name="markChanges"/> highlights those that changed since last time.</summary>
        public void RefreshRegs(bool markChanges)
        {
            var m = C.M;
            var rows = new List<RegRow>();
            rows.Add(new RegRow { Name = "pc", Hex = H(m.Pc), Dec = Where(m.Pc), Tip = "program counter: the address of the next instruction", Changed = markChanges && m.Pc != prev[32] });
            for (int i = 1; i < 32; i++)
            {
                uint v = m.X[i];
                rows.Add(new RegRow
                {
                    Name = Machine.AbiNames[i] + " (x" + i + ")",
                    Hex = H(v),
                    // small negative numbers as negative; big values (addresses) as positive
                    Dec = ((int)v < 0 && (int)v > -1000000 ? (int)v : (long)v).ToString(CultureInfo.InvariantCulture),
                    Tip = RegTips[i],
                    Changed = markChanges && v != prev[i]
                });
            }
            Sync(Regs, rows);
            for (int i = 1; i < 32; i++) prev[i] = m.X[i];
            prev[32] = m.Pc;

            var c = new List<RegRow>();
            Action<string, ulong, string> add = (n, v, t) => c.Add(new RegRow { Name = n, Hex = v > uint.MaxValue ? "0x" + v.ToString("x") : H((uint)v), Dec = v.ToString("N0", CultureInfo.InvariantCulture), Tip = t });
            add("mstatus", m.Mstatus, "machine status: bit 3 MIE = interrupts on, bit 7 MPIE = what MIE was before the trap");
            add("mie", m.Mie, "which interrupts may happen: bit 3 software, bit 7 timer, bit 11 external (keyboard)");
            add("mip", m.Mip, "which interrupts are waiting");
            add("mtvec", m.Mtvec, "where traps go: the trap handler's address");
            add("mepc", m.Mepc, "where the last trap happened (the trap handler returns there)");
            add("mcause", m.Mcause, "why: " + Machine.CauseName(m.Mcause));
            add("mtval", m.Mtval, "extra detail: the bad address or instruction");
            add("mscratch", m.Mscratch, "a spare register for the trap handler (LabOS keeps its trap stack here)");
            add("mcycle", m.Cycle, "clock cycles since power on");
            add("minstret", m.Instret, "instructions finished since power on");
            add("mtime", m.Mtime, "the timer: microseconds since power on");
            add("mtimecmp", m.Mtimecmp, "the alarm: a timer interrupt when mtime reaches this");
            Sync(Csrs, c);
        }

        static void Sync<T>(ObservableCollection<T> target, List<T> rows)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (i < target.Count) target[i] = rows[i]; else target.Add(rows[i]);
            }
            while (target.Count > rows.Count) target.RemoveAt(target.Count - 1);
        }

        // ------------------------------------------------------------------ symbols
        AsmResult ProgramAt(uint pc)
        {
            if (pc - Map.RamBase < Map.RamSize && pc >= Computer.ProgramBase)
            {
                // which program is loaded? compare the first bytes with the ones we know
                uint w0 = C.M.ReadWord(Computer.ProgramBase), w1 = C.M.ReadWord(Computer.ProgramBase + 4), w2 = C.M.ReadWord(Computer.ProgramBase + 8);
                Func<AsmResult, bool> same = a => a != null && a.Origin == Computer.ProgramBase && a.Image.Length >= 12 && BitConverter.ToUInt32(a.Image, 0) == w0 && BitConverter.ToUInt32(a.Image, 4) == w1 && BitConverter.ToUInt32(a.Image, 8) == w2;
                if (same(UserProgram)) return UserProgram;
                foreach (var p in C.Programs) if (same(p.Asm)) return p.Asm;
                return null;
            }
            if (UserProgram != null && UserProgram.Origin == Map.RamBase && pc - Map.RamBase < (uint)UserProgram.Image.Length) return UserProgram;
            if (pc - Map.RomBase < Map.RomSize) return C.BootAsm;
            if (pc - Map.RamBase < Map.RamSize) return C.KernelAsm;
            return null;
        }

        static string FileName(string f) { return string.IsNullOrEmpty(f) ? "" : System.IO.Path.GetFileName(f); }

        /// <summary>"getkey + 12 (keyboard.s, line 88)" for an address.</summary>
        public string Where(uint pc)
        {
            var a = ProgramAt(pc);
            if (a == null) return "";
            string best = null; uint bestA = 0;
            foreach (var kv in a.Symbols)
            {
                if (kv.Value > pc || kv.Value < a.Origin || kv.Value >= a.Origin + a.TextSize) continue;
                if (kv.Key.StartsWith(".") || char.IsDigit(kv.Key[0]) || kv.Key.ToUpperInvariant() == kv.Key) continue;   // skip .equ-style names
                if (best == null || kv.Value > bestA) { best = kv.Key; bestA = kv.Value; }
            }
            var sb = new StringBuilder();
            if (best != null) sb.Append(best).Append(pc != bestA ? " + " + (pc - bestA) : "");
            SourceLine sl;
            if (a.Lines.TryGetValue(pc, out sl))
            {
                string f = FileName(sl.File);
                sb.Append(sb.Length > 0 ? "  " : "").Append("(" + (f.Length > 0 ? f + ", line " : "line ") + sl.Line + ")");
            }
            return sb.ToString();
        }

        string LabelExact(uint a)
        {
            var p = ProgramAt(a);
            if (p == null) return null;
            foreach (var kv in p.Symbols)
                if (kv.Value == a && !kv.Key.StartsWith(".") && !char.IsDigit(kv.Key[0]) && kv.Key.ToUpperInvariant() != kv.Key) return kv.Key;
            return null;
        }

        // ------------------------------------------------------------------ code
        public void RefreshCode()
        {
            var m = C.M;
            uint pc = m.Pc;
            uint centre = CodeAt ?? pc;
            uint start = (centre & ~3u) - 10 * 4;
            var rows = new List<CodeRow>();
            for (int i = 0; i < 36; i++)
            {
                uint a = start + (uint)i * 4;
                if (!Machine.IsMemory(a)) continue;
                uint w = m.ReadWord(a);
                var p = ProgramAt(a);
                SourceLine sl = null;
                if (p != null) p.Lines.TryGetValue(a, out sl);
                bool br = R != null && R.IsBreak(a);
                rows.Add(new CodeRow
                {
                    Address = a,
                    AddrText = a.ToString("x8"),
                    Marker = (a == pc ? "\u25B6" : " ") + (br ? "\u25CF" : " "),
                    Label = LabelExact(a),
                    Code = Disassembler.Disassemble(w, a, LabelExact),
                    Source = sl != null ? FileName(sl.File) + ":" + sl.Line : "",
                    IsPc = a == pc,
                    IsBreak = br
                });
            }
            Sync(Code, rows);
        }

        // ------------------------------------------------------------------ memory and stack
        public string HexDump(uint addr, int rows)
        {
            var m = C.M;
            var sb = new StringBuilder();
            addr &= ~15u;
            for (int r = 0; r < rows; r++)
            {
                uint a = addr + (uint)r * 16;
                sb.Append(a.ToString("x8")).Append("  ");
                var txt = new StringBuilder();
                for (int i = 0; i < 16; i++)
                {
                    uint b = a + (uint)i;
                    if (Machine.IsMemory(b))
                    {
                        byte v = m.ReadByte(b);
                        sb.Append(v.ToString("x2"));
                        txt.Append(v >= 32 && v < 127 ? (char)v : '.');
                    }
                    else { sb.Append("--"); txt.Append(' '); }
                    sb.Append(i == 7 ? "  " : " ");
                }
                sb.Append(' ').Append(txt).Append('\n');
            }
            return sb.ToString();
        }

        public static string Region(uint a)
        {
            if (a - Map.RomBase < Map.RomSize) return "boot ROM";
            if (a - Map.FlashBase < Map.FlashSize) return "flash";
            if (a - Map.FbBase < Map.FbSize) return "screen (framebuffer)";
            if (a - Map.ClintBase < Map.ClintSize) return "timer (CLINT)";
            if (a >= 0x10000000 && a < 0x10006000) return "devices";
            if (a - Map.RamBase < Map.RamSize)
            {
                if (a < 0x80080000) return "RAM: LabOS";
                if (a < 0x80090000) return "RAM: LabOS stacks";
                if (a < 0x80100000) return "RAM: disk buffers";
                if (a >= 0x80F00000) return "RAM: program stack";
                return "RAM: programs";
            }
            return "nothing here";
        }

        public string Stack(int words)
        {
            var m = C.M;
            uint sp = m.X[2];
            var sb = new StringBuilder();
            sb.Append("sp = ").Append(H(sp)).Append("  (").Append(Region(sp)).Append(")\n");
            if ((sp & 3) != 0 || !Machine.IsMemory(sp)) return sb.Append("The stack pointer doesn't point at memory.\n").ToString();
            for (int i = 0; i < words; i++)
            {
                uint a = sp + (uint)i * 4;
                if (!Machine.IsMemory(a)) break;
                uint v = m.ReadWord(a);
                string note = "";
                if (Machine.IsMemory(v) && (v & 3) == 0) { string w = Where(v); if (w.Length > 0) note = "  -> " + w; }
                sb.Append(i == 0 ? "sp -> " : "      ").Append(a.ToString("x8")).Append("  ").Append(v.ToString("x8")).Append(note).Append('\n');
            }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ traps
        /// <summary>Newest first; runs of the same interrupt are shown as one line with a count.</summary>
        public bool RefreshTraps()
        {
            var m = C.M;
            long seq = m.TrapSeq;
            if (seq == lastTrapSeq) return false;
            lastTrapSeq = seq;
            var lines = new List<string>();
            int ring = m.TrapRing.Length;
            string pending = null; int count = 0; uint lastCause = 0xffffffff;
            for (long s = seq; s > 0 && s > seq - ring && lines.Count < 150; s--)
            {
                var r = m.TrapRing[s % ring];
                if (r.Seq != s) break;
                bool intr = (r.Cause & 0x80000000u) != 0;
                if (intr && r.Cause == lastCause && pending != null) { count++; continue; }
                if (pending != null) lines.Add(pending + (count > 1 ? "   (x" + count + ")" : ""));
                string what = Machine.CauseName(r.Cause);
                if (r.Cause == 11) what = "ecall (system call)";
                pending = string.Format("{0,14}  {1}  at {2}{3}", r.Instret.ToString("N0", CultureInfo.InvariantCulture), what, r.Pc.ToString("x8"),
                    !intr && r.Cause != 11 && r.Cause != 3 ? "  (mtval " + r.Tval.ToString("x8") + ")" : "");
                count = 1; lastCause = intr ? r.Cause : 0xffffffff;
            }
            if (pending != null) lines.Add(pending + (count > 1 ? "   (x" + count + ")" : ""));
            Sync(Traps, lines);
            return true;
        }

        // ------------------------------------------------------------------ plain language
        /// <summary>One or two sentences about what the computer is doing right now.</summary>
        public string Narrate()
        {
            var m = C.M;
            if (m.PoweredOff) return "The computer is switched off. Press Power: the processor starts at the boot ROM (address 0x00001000), which copies LabOS from the flash memory into RAM and starts it.";
            uint pc = m.Pc;
            string where = Where(pc);
            bool running = R != null && R.Running;
            string place;
            if (pc - Map.RomBase < Map.RomSize) place = "the boot ROM, the first program after power on";
            else if (pc >= Computer.ProgramBase && pc - Map.RamBase < Map.RamSize) place = "a program that LabOS loaded with run";
            else if (pc - Map.RamBase < Map.RamSize) place = "LabOS, the operating system";
            else place = "an address with no memory";
            if (running)
            {
                if (m.Waiting) return "The processor is resting (wfi, \"wait for interrupt\"). It wakes up when a key is pressed or when the timer ticks (100 times a second), does a little work, then rests again. That's why an idle computer uses almost no power.";
                return "Running " + place + (where.Length > 0 ? ", now in " + where : "") + ". Pause to look at one instruction at a time.";
            }
            uint inst = m.ReadWord(pc);
            string s = "Paused in " + place + (where.Length > 0 ? " at " + where : "") + ". The next instruction is \"" + Disassembler.Disassemble(inst, pc, LabelExact) + "\". ";
            s += Disassembler.Explain(inst, pc, m.X);
            if (m.Waiting) s = "Paused while the processor rests (wfi). Step lets time pass until the next interrupt. " + s;
            return s;
        }
    }

    /// <summary>Keys: the ASCII code and the PC (set 1) scancode the machine's keyboard reports.</summary>
    public static class KeyMap
    {
        const string Row1 = "1234567890-=", Row2 = "qwertyuiop[]", Row3 = "asdfghjkl;'`", Row4 = "\\zxcvbnm,./";
        const string Row1S = "!@#$%^&*()_+", Row2S = "QWERTYUIOP{}", Row3S = "ASDFGHJKL:\"~", Row4S = "|ZXCVBNM<>?";

        public static byte ScanForChar(char c)
        {
            int i;
            if ((i = Row1.IndexOf(c)) >= 0 || (i = Row1S.IndexOf(c)) >= 0) return (byte)(0x02 + i);
            if ((i = Row2.IndexOf(c)) >= 0 || (i = Row2S.IndexOf(c)) >= 0) return (byte)(0x10 + i);
            if ((i = Row3.IndexOf(c)) >= 0 || (i = Row3S.IndexOf(c)) >= 0) return (byte)(0x1E + i);
            if ((i = Row4.IndexOf(c)) >= 0 || (i = Row4S.IndexOf(c)) >= 0) return (byte)(0x2B + i);
            if (c == ' ') return 0x39;
            return 0;
        }

        /// <summary>Keys that don't type a character (or need special handling). False for ordinary typing keys.
        /// (Learning Lab: the key by its name, as the UI's key enum spells it: Escape, Back, Tab, Enter / Return, Up,
        /// Down, Left, Right, Home, End, PageUp / Prior, PageDown / Next, Insert, Delete, F1-F10, A-Z.)</summary>
        public static bool Special(string k, bool ctrl, out byte ascii, out byte scan)
        {
            ascii = 0; scan = 0;
            switch (k)
            {
                case "Escape": ascii = 27; scan = 0x01; return true;
                case "Back": case "Backspace": ascii = 8; scan = 0x0E; return true;
                case "Tab": ascii = 9; scan = 0x0F; return true;
                case "Enter": case "Return": ascii = 13; scan = 0x1C; return true;
                case "Up": scan = 0x48; return true;
                case "Down": scan = 0x50; return true;
                case "Left": scan = 0x4B; return true;
                case "Right": scan = 0x4D; return true;
                case "Home": scan = 0x47; return true;
                case "End": scan = 0x4F; return true;
                case "PageUp": case "Prior": scan = 0x49; return true;
                case "PageDown": case "Next": scan = 0x51; return true;
                case "Insert": scan = 0x52; return true;
                case "Delete": ascii = 127; scan = 0x53; return true;
            }
            int f;
            if (k != null && k.Length >= 2 && k[0] == 'F' && int.TryParse(k.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out f) && f >= 1 && f <= 10)
            {
                scan = (byte)(0x3B + (f - 1));
                return true;
            }
            if (ctrl && k != null && k.Length == 1 && k[0] >= 'A' && k[0] <= 'Z')
            {
                // Ctrl + letter: ASCII 1-26 (Ctrl+C = 3 stops a LabOS program)
                ascii = (byte)(1 + (k[0] - 'A'));
                scan = ScanForChar((char)('a' + (k[0] - 'A')));
                return true;
            }
            return false;
        }
    }

    /// <summary>The "What's happening?" tab: short explanations for a curious family.</summary>
    public static class Learn
    {
        public static readonly string[][] Topics = {
            new[] { "The computer",
                "Emulator Lab is a whole computer made of software. Everything a real computer has is here: a processor, memory, a screen, a keyboard, a disk and a clock - all imitated (\"emulated\") by this app.\n\n" +
                "The processor is a RISC-V. RISC-V is a real, open design: the same instructions run on tiny chips in headphones, on single-board computers and on big servers. Ours runs the 32-bit version with whole-number maths (RV32IM).\n\n" +
                "Every byte this computer runs was written in Emulator Lab: the boot ROM, the operating system (LabOS) and the games. The demos and lessons are in the Assembler's Examples: read them, change them and see what happens." },
            new[] { "The processor (CPU)",
                "The processor does one simple thing, very fast, over and over:\n\n" +
                "  1. Fetch: read the instruction at the address in the program counter (pc).\n" +
                "  2. Decode: work out what it means (add? jump? read memory?).\n" +
                "  3. Execute: do it, and move pc on to the next instruction.\n\n" +
                "That's all. Every game, every program, is just millions of these little steps. On a modern PC this emulator does over 100 million of them every second (a real RISC-V chip can do billions).\n\n" +
                "Try it: open Look inside, press Pause, then Step. Each press runs exactly one instruction, and you can watch the registers change." },
            new[] { "Registers",
                "Registers are 32 tiny boxes inside the processor, each holding one 32-bit number. They are much faster than memory, so the processor does all its sums in them.\n\n" +
                "They are called x0 to x31, but each also has a nickname that says what programs use it for:\n\n" +
                "  zero  always 0 (writing to it does nothing)\n" +
                "  ra    the return address: where to go back to after a function\n" +
                "  sp    the stack pointer: the top of the stack (a pile of saved values in memory)\n" +
                "  a0-a7 arguments: values given to a function, and its answer (a0)\n" +
                "  t0-t6 temporaries: scratch space\n" +
                "  s0-s11 saved: values a function must put back as it found them\n\n" +
                "And pc, the program counter: the address of the next instruction. In Look inside, a register that just changed lights up." },
            new[] { "Memory",
                "Memory is a long row of bytes, each with its own number: its address. Our computer counts addresses in hexadecimal (base 16), like 0x80000000.\n\n" +
                "Not every address is RAM. The memory map says what lives where:\n\n" +
                "  0x00001000  the boot ROM: the first program, which never changes\n" +
                "  0x02000000  the timer\n" +
                "  0x10000000  devices: serial port, keyboard, disk, clock, power, video\n" +
                "  0x11000000  the screen: one byte per pixel\n" +
                "  0x20000000  flash: LabOS and the built-in programs\n" +
                "  0x80000000  RAM: 16 MB for LabOS, programs and their data\n\n" +
                "Writing a number to the screen's addresses colours a pixel. Writing to the serial port prints a letter. That's how a processor talks to everything: by reading and writing addresses (\"memory-mapped I/O\").\n\n" +
                "Look inside > Memory shows any part of memory as hexadecimal bytes. Try the Screen button while a game runs." },
            new[] { "Instructions",
                "Each instruction is 4 bytes, and does one small thing. A few you'll see a lot:\n\n" +
                "  addi a0, a0, 1     add 1 to register a0\n" +
                "  add  a0, a1, a2    a0 = a1 + a2\n" +
                "  lw   a0, 8(sp)     load a word (4 bytes) from memory at sp + 8\n" +
                "  sw   a0, 8(sp)     store a word to memory\n" +
                "  beq  a0, a1, done  if a0 equals a1, jump to the label done\n" +
                "  call print         jump to a function and remember where to come back\n" +
                "  ret                go back\n" +
                "  ecall              ask the operating system for help\n\n" +
                "Writing these words is called assembly language. The assembler turns each line into the 4 bytes the processor reads; the disassembler turns bytes back into words. Try the Assembler tab: pick an example and press Assemble & run." },
            new[] { "Traps and interrupts",
                "Sometimes the processor has to stop what it is doing and jump somewhere else. That's a trap. There are three kinds:\n\n" +
                "  Interrupts: something outside needs attention. The timer goes off 100 times a second; the keyboard says a key was pressed.\n" +
                "  System calls: a program runs ecall to ask LabOS to do something, like print text or wait for a key.\n" +
                "  Exceptions: something impossible happened, like an instruction that doesn't exist or reading an address with nothing there.\n\n" +
                "On a trap the processor saves where it was (mepc) and why (mcause), then jumps to the trap handler (mtvec). When the handler is done, mret goes back as if nothing happened.\n\n" +
                "Look inside > Traps lists them as they happen. When a program crashes, LabOS's trap handler tells you why and goes back to the shell." },
            new[] { "LabOS, the operating system",
                "An operating system is the program that looks after the computer so other programs don't have to. Windows, macOS and Linux are big ones. LabOS is a tiny one (about 13 KB), written in RISC-V assembly for this app.\n\n" +
                "LabOS:\n" +
                "  - tests the memory when it starts\n" +
                "  - counts timer ticks to keep the time\n" +
                "  - collects key presses into a buffer\n" +
                "  - keeps files on the disk (ls, cat, write, rm)\n" +
                "  - loads programs and runs them (run snake)\n" +
                "  - gives programs system calls: print, read a key, draw, sleep...\n\n" +
                "Type help in LabOS to see its commands." },
            new[] { "Try this",
                "  - Type  run snake  and play. Then  run snake demo  to watch the computer play.\n" +
                "  - Type  write hi Hello there  then  ls  and  cat hi.\n" +
                "  - Type  peek 0x80000000  to see the first bytes of LabOS in memory.\n" +
                "  - Open Look inside, Pause, and Step through a few instructions.\n" +
                "  - Slow the speed right down and watch the registers flicker.\n" +
                "  - In the Assembler tab, change the count example to count to 100 and run it.\n" +
                "  - Make a mistake on purpose (type  addi a0, a0) and see the error message.\n" +
                "  - Set a breakpoint by double-clicking (or double-tapping) a line of code in Look inside > Code." }
        };
    }
}
