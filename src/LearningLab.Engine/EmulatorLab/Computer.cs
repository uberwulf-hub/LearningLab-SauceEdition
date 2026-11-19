// Emulator Lab - the whole computer: builds LabOS from its assembly sources, powers on, runs headless (tests,
// pictures) or on a background thread (the window), and reads / writes the LabOS disk from the host.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace EmulatorLab
{
    public sealed class ProgramImage
    {
        public string Name;
        public byte[] Bytes;
        public AsmResult Asm;
    }

    public sealed class Computer : IDisposable
    {
        public readonly Machine M = new Machine();
        public readonly Terminal Term = new Terminal();
        public readonly object Sync = new object();
        public readonly string OsDir;
        public AsmResult BootAsm, KernelAsm;
        public readonly List<ProgramImage> Programs = new List<ProgramImage>();
        public string BuildError;
        public const uint ProgramBase = 0x80100000;   // where LabOS loads programs (`run`)
        public const int DiskSectors = 8 + 64 * 128;
        public const uint FlashMagic = 0x4642414C;     // "LABF"

        public Computer(string osDir)
        {
            OsDir = osDir;
            M.Uart.Output = Term.Put;
        }

        public void Dispose() { M.Disk.Detach(); }

        /// <summary>An assembler that finds .include files next to the source, then in <paramref name="fallbackDir"/>
        /// (os\programs, where _syscalls.s is).</summary>
        public static Assembler NewAssembler(string baseDir, string fallbackDir = null)
        {
            return new Assembler
            {
                BaseDir = baseDir,
                ReadFile = p =>
                {
                    if (File.Exists(p)) return File.ReadAllText(p);
                    if (fallbackDir != null)
                    {
                        string f = Path.Combine(fallbackDir, Path.GetFileName(p));
                        if (File.Exists(f)) return File.ReadAllText(f);
                    }
                    return null;
                }
            };
        }

        /// <summary>Assembles a program for LabOS (loaded at 0x80100000) or for bare metal (0x80000000).</summary>
        public AsmResult AssembleUser(string text, string fileName, bool bare)
        {
            string programs = Path.Combine(OsDir, "programs");
            string dir = !string.IsNullOrEmpty(fileName) && Path.IsPathRooted(fileName) ? Path.GetDirectoryName(fileName) : programs;
            return NewAssembler(dir, programs).Assemble(text, fileName, bare ? Map.RamBase : ProgramBase);
        }

        /// <summary>Assembles a source file from the os folder.</summary>
        public AsmResult AssembleFile(string relPath, uint origin)
        {
            string path = Path.Combine(OsDir, relPath);
            var a = NewAssembler(Path.GetDirectoryName(path));
            return a.Assemble(File.ReadAllText(path), path, origin);
        }

        /// <summary>Assembles the boot ROM, LabOS and the demo programs, and lays them out in ROM and flash.</summary>
        public bool Build()
        {
            BuildError = null;
            var sb = new StringBuilder();
            BootAsm = AssembleFile("boot.s", Map.RomBase);
            if (!BootAsm.Ok) sb.Append("boot.s:\n" + BootAsm.ErrorText());
            KernelAsm = AssembleFile("kernel.s", Map.RamBase);
            if (!KernelAsm.Ok) sb.Append("kernel.s:\n" + KernelAsm.ErrorText());
            Programs.Clear();
            string pdir = Path.Combine(OsDir, "programs");
            if (Directory.Exists(pdir))
            {
                var files = new List<string>(Directory.GetFiles(pdir, "*.s"));
                files.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (var f in files)
                {
                    string name = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                    if (name.StartsWith("_")) continue;   // shared include files
                    var r = AssembleFile(Path.Combine("programs", Path.GetFileName(f)), ProgramBase);
                    if (!r.Ok) { sb.Append(Path.GetFileName(f) + ":\n" + r.ErrorText()); continue; }
                    Programs.Add(new ProgramImage { Name = name, Bytes = r.Image, Asm = r });
                }
            }
            if (sb.Length > 0) { BuildError = sb.ToString(); return false; }
            if (BootAsm.Image.Length > Map.RomSize) { BuildError = "The boot ROM program is bigger than 4 KB"; return false; }
            return true;
        }

        /// <summary>The flash image: a header, a program directory, LabOS, then the programs.</summary>
        public byte[] FlashImage()
        {
            var flash = new byte[Map.FlashSize];
            Action<int, uint> put = (o, v) => { flash[o] = (byte)v; flash[o + 1] = (byte)(v >> 8); flash[o + 2] = (byte)(v >> 16); flash[o + 3] = (byte)(v >> 24); };
            int kernelOff = 0x400;
            put(0, FlashMagic);
            put(4, (uint)kernelOff);
            put(8, (uint)KernelAsm.Image.Length);
            put(12, Map.RamBase);
            put(16, (uint)Programs.Count);
            Array.Copy(KernelAsm.Image, 0, flash, kernelOff, KernelAsm.Image.Length);
            int off = (kernelOff + KernelAsm.Image.Length + 15) & ~15;
            for (int i = 0; i < Programs.Count && i < 31; i++)
            {
                var p = Programs[i];
                int e = 0x20 + i * 32;
                byte[] nm = Encoding.ASCII.GetBytes(p.Name);
                Array.Copy(nm, 0, flash, e, Math.Min(15, nm.Length));
                put(e + 16, (uint)off);
                put(e + 20, (uint)p.Bytes.Length);
                if (off + p.Bytes.Length > flash.Length) throw new InvalidOperationException("The programs don't fit in the flash memory");
                Array.Copy(p.Bytes, 0, flash, off, p.Bytes.Length);
                off = (off + p.Bytes.Length + 15) & ~15;
            }
            return flash;
        }

        public void AttachDisk(string path) { M.Disk.Attach(path, DiskSectors); }

        /// <summary>Looks after the person's LabOS disk before it is attached (LearningLab.Saves.SafeFile). A LabFS disk that changed
        /// since the newest backup is copied to disk.img.bak1 (five kept), so programs and notes saved on it survive a
        /// crash in the middle of a write. A disk that was used but isn't LabFS any more (LabOS would format it) is
        /// copied to disk.img.unreadable-time first, and the newest LabFS backup is put back if there is one. Returns a
        /// sentence for the person, empty when all is well. A new or empty disk is left alone.</summary>
        public static string PrepareDisk(string path)
        {
            if (!File.Exists(path)) return "";
            byte[] data = File.ReadAllBytes(path);
            if (IsLabFs(data))
            {
                var backups = LearningLab.Saves.SafeFile.Backups(path);
                bool same = backups.Count > 0 && Same(File.ReadAllBytes(backups[0]), data);
                if (!same) LearningLab.Saves.SafeFile.Write(path, data, true);   // the same bytes again: the previous one becomes .bak1
                return "";
            }
            bool used = false;
            for (int i = 0; i < Math.Min(data.Length, 5 * 512) && !used; i++) used = data[i] != 0;
            if (!used) return "";   // never formatted yet: LabOS formats it at start
            string kept = LearningLab.Saves.SafeFile.Quarantine(path);
            foreach (string b in LearningLab.Saves.SafeFile.Backups(path))
            {
                byte[] bd;
                try { bd = File.ReadAllBytes(b); } catch (Exception) { continue; }
                if (!IsLabFs(bd)) continue;
                LearningLab.Saves.SafeFile.Write(path, bd, false);
                return "The LabOS disk couldn't be read, so the copy from " + File.GetLastWriteTime(b).ToString("d MMM yyyy, HH:mm", System.Globalization.CultureInfo.InvariantCulture) +
                    " was put back. The damaged disk is kept as " + Path.GetFileName(kept) + ".";
            }
            return "The LabOS disk couldn't be read, so LabOS will start with a new, empty disk. The old one is kept as " + Path.GetFileName(kept) + ".";
        }
        static bool IsLabFs(byte[] d) { return d.Length >= 512 && BitConverter.ToUInt32(d, 0) == LabFs.Magic; }
        static bool Same(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        /// <summary>Power on: clear memory, put the boot program in ROM and LabOS in flash, start at the ROM.</summary>
        public void PowerOn()
        {
            lock (Sync)
            {
                M.Reset(true);
                Array.Clear(M.Rom, 0, M.Rom.Length);
                Array.Copy(BootAsm.Image, M.Rom, BootAsm.Image.Length);
                byte[] fl = FlashImage();
                Array.Copy(fl, M.Flash, fl.Length);
                Term.Reset();
            }
        }

        /// <summary>Like a bare-metal board: no LabOS, the program goes straight into RAM and runs from there.</summary>
        public void PowerOnBare(byte[] image)
        {
            lock (Sync)
            {
                M.Reset(true);
                Array.Clear(M.Rom, 0, M.Rom.Length);
                // a two-instruction boot ROM: jump to RAM
                var a = NewAssembler(OsDir).Assemble("li t0, 0x80000000\njr t0\n", "bare", Map.RomBase);
                Array.Copy(a.Image, M.Rom, a.Image.Length);
                Array.Clear(M.Flash, 0, M.Flash.Length);
                M.LoadBytes(Map.RamBase, image);
                Term.Reset();
            }
        }

        /// <summary>The reset button (or LabOS's reboot): start again from the boot ROM; memory is kept.</summary>
        public void Reset()
        {
            lock (Sync) { M.Reset(false); Term.Reset(); }
        }

        // ------------------------------------------------------------------ headless running (tests, pictures)
        /// <summary>Runs in virtual time (deterministic) for up to <paramref name="max"/> instructions. When the
        /// machine waits for an interrupt, time jumps ahead to the next timer tick. Stops early when it waits for a
        /// key and none are queued, when it powers off, or when <paramref name="until"/> returns true.</summary>
        public long RunVirtual(long max, Func<bool> until = null)
        {
            long done = 0;
            lock (Sync)
            {
                M.VirtualTime = true;
                while (done < max)
                {
                    if (M.PoweredOff) break;
                    if (M.ResetRequested) { M.Reset(false); Term.Reset(); }
                    int chunk = (int)Math.Min(max - done, 20000);
                    int n = M.Run(chunk);
                    done += n;
                    if (until != null && until()) break;
                    if (M.Waiting && !M.InterruptPending)
                    {
                        if (M.Keyboard.Count > 0) { M.UpdateExternal(); if (M.InterruptPending) continue; }
                        ulong t = M.TimeToTimer();
                        if (t == ulong.MaxValue) break;            // nothing will ever wake it
                        if (KeyWaitIdle()) break;                  // only the clock ticking while it waits for a key
                        M.AdvanceTime(Math.Max(1, t));
                    }
                    else if (n == 0 && !M.Waiting) break;          // breakpoint
                }
            }
            return done;
        }

        /// <summary>Set by tests: stop RunVirtual when LabOS sits at its key wait (symbol "wait_key_idle" executed
        /// recently) and no keys are queued.</summary>
        public bool StopWhenWaitingForKeys = true;
        int idleSpins;
        bool KeyWaitIdle()
        {
            if (!StopWhenWaitingForKeys) return false;
            if (M.Keyboard.Count > 0) { idleSpins = 0; return false; }
            uint a;
            if (KernelAsm != null && KernelAsm.Symbols.TryGetValue("key_wait_wfi", out a) && M.Pc == a + 4)
            {
                // the keyboard wait: let a few timer ticks pass (for the clock), then call it idle
                if (++idleSpins > 3) { idleSpins = 0; return true; }
            }
            else idleSpins = 0;
            return false;
        }

        /// <summary>Types text, then runs until the console shows <paramref name="expect"/> (after this call) or
        /// the machine waits for keys again.</summary>
        public string TypeAndRun(string keys, long max = 50000000)
        {
            int start = Term.Transcript.Length;
            M.Keyboard.Type(keys);
            RunVirtual(max);
            string t = Term.Transcript;
            return start <= t.Length ? t.Substring(start) : t;
        }

        // ------------------------------------------------------------------ the LabOS disk, from the host
        public LabFs Fs { get { return new LabFs(M.Disk); } }
    }

    /// <summary>LabOS's tiny flat file system, read and written from the host (the same format as os\fs.s):
    /// sector 0 = superblock, sectors 1-4 = directory (64 entries of 32 bytes: name[24], size, flags),
    /// file i's data = 128 sectors (64 KB) starting at sector 8 + i * 128.</summary>
    public sealed class LabFs
    {
        public const uint Magic = 0x5346424C;   // "LBFS"
        public const int Slots = 64, SlotSectors = 128, DataStart = 8, NameMax = 23;
        readonly Disk disk;
        public LabFs(Disk d) { disk = d; }

        public bool IsFormatted
        {
            get
            {
                var b = new byte[512];
                if (!disk.ReadSectors(0, 1, b, 0)) return false;
                return BitConverter.ToUInt32(b, 0) == Magic;
            }
        }

        public void Format()
        {
            var b = new byte[512 * 5];
            Action<int, uint> put = (o, v) => { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); b[o + 2] = (byte)(v >> 16); b[o + 3] = (byte)(v >> 24); };
            put(0, Magic); put(4, 1); put(8, Slots); put(12, SlotSectors); put(16, DataStart); put(20, 4);
            disk.WriteSectors(0, 5, b, 0);
        }

        byte[] Dir() { var d = new byte[2048]; if (!disk.ReadSectors(1, 4, d, 0)) throw new IOException("The disk can't be read"); return d; }

        static string NameAt(byte[] d, int e)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 24 && d[e + i] != 0; i++) sb.Append((char)d[e + i]);
            return sb.ToString();
        }

        public List<string> List()
        {
            var l = new List<string>();
            if (!IsFormatted) return l;
            var d = Dir();
            for (int i = 0; i < Slots; i++) if ((d[i * 32 + 28] & 1) != 0) l.Add(NameAt(d, i * 32) + "\t" + BitConverter.ToUInt32(d, i * 32 + 24));
            return l;
        }

        int Find(byte[] d, string name)
        {
            for (int i = 0; i < Slots; i++) if ((d[i * 32 + 28] & 1) != 0 && NameAt(d, i * 32) == name) return i;
            return -1;
        }

        public byte[] Read(string name)
        {
            var d = Dir();
            int i = Find(d, name);
            if (i < 0) return null;
            int size = (int)BitConverter.ToUInt32(d, i * 32 + 24);
            int secs = (size + 511) / 512;
            var b = new byte[secs * 512];
            if (secs > 0) disk.ReadSectors((uint)(DataStart + i * SlotSectors), secs, b, 0);
            Array.Resize(ref b, size);
            return b;
        }

        public static string CheckName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "The file needs a name";
            if (name.Length > NameMax) return "File names can be at most " + NameMax + " letters";
            foreach (char c in name) if (c <= 32 || c >= 127) return "File names can only use letters, digits and simple symbols (no spaces)";
            return null;
        }

        public void Write(string name, byte[] data)
        {
            string bad = CheckName(name);
            if (bad != null) throw new ArgumentException(bad);
            if (data.Length > SlotSectors * 512) throw new ArgumentException("Files on the LabOS disk can be at most 64 KB");
            if (!IsFormatted) Format();
            var d = Dir();
            int i = Find(d, name);
            if (i < 0) for (int k = 0; k < Slots; k++) if ((d[k * 32 + 28] & 1) == 0) { i = k; break; }
            if (i < 0) throw new IOException("The LabOS disk is full (64 files)");
            int secs = (data.Length + 511) / 512;
            var b = new byte[secs * 512];
            Array.Copy(data, b, data.Length);
            if (secs > 0) disk.WriteSectors((uint)(DataStart + i * SlotSectors), secs, b, 0);
            Array.Clear(d, i * 32, 32);
            byte[] nm = Encoding.ASCII.GetBytes(name);
            Array.Copy(nm, 0, d, i * 32, nm.Length);
            byte[] sz = BitConverter.GetBytes((uint)data.Length);
            Array.Copy(sz, 0, d, i * 32 + 24, 4);
            d[i * 32 + 28] = 1;
            disk.WriteSectors(1, 4, d, 0);
        }

        public bool Delete(string name)
        {
            var d = Dir();
            int i = Find(d, name);
            if (i < 0) return false;
            Array.Clear(d, i * 32, 32);
            disk.WriteSectors(1, 4, d, 0);
            return true;
        }
    }

    /// <summary>Runs the computer on a background thread in time slices. The window only reads its state on a
    /// timer: nothing here calls back into PowerShell or WPF.</summary>
    public sealed class Runner : IDisposable
    {
        public readonly Computer C;
        readonly Thread thread;
        volatile bool quit, running;
        readonly AutoResetEvent wake = new AutoResetEvent(false);
        readonly HashSet<uint> userBreaks = new HashSet<uint>();
        uint tempBreak; bool hasTemp;
        /// <summary>Instructions per second; 0 = as fast as possible (real time).</summary>
        public volatile int Speed;
        public volatile bool StopOnCrash;
        public double Ips;
        public long TotalRun;
        public volatile string StopReason = "";
        public volatile int StateVersion;

        public Runner(Computer c)
        {
            C = c;
            thread = new Thread(Loop) { IsBackground = true, Name = "Emulator Lab CPU", Priority = ThreadPriority.BelowNormal };
            thread.Start();
        }

        public bool Running { get { return running; } }

        public void Dispose() { quit = true; wake.Set(); thread.Join(500); }

        public void Resume()
        {
            if (C.M.PoweredOff) return;
            lock (C.Sync) { C.M.SkipBreakOnce = true; C.M.BreakHit = false; ApplyBreaks(); }
            StopReason = "";
            running = true; StateVersion++; wake.Set();
        }
        public void Pause(string reason = "Paused")
        {
            if (!running) return;
            running = false;
            lock (C.Sync) { }     // wait for the current slice to finish
            StopReason = reason; StateVersion++;
        }

        public void StepOne()
        {
            Pause();
            lock (C.Sync)
            {
                if (C.M.PoweredOff) return;
                C.M.VirtualTime = true;
                if (C.M.Waiting && !C.M.InterruptPending)
                {
                    // nothing to do until an interrupt: let time pass to the next timer tick
                    ulong t = C.M.TimeToTimer();
                    if (t != ulong.MaxValue) C.M.AdvanceTime(Math.Max(1, t));
                    C.M.UpdateExternal();
                }
                C.M.Step();
                if (C.M.ResetRequested) C.Reset();
            }
            StopReason = C.M.Waiting ? "Waiting for an interrupt (wfi)" : "Stepped";
            StateVersion++;
        }

        /// <summary>Step over: a call runs until it returns; anything else is a single step.</summary>
        public void StepOver()
        {
            Pause();
            uint pc = C.M.Pc;
            uint inst = C.M.ReadWord(pc);
            uint op = inst & 0x7f, rd = (inst >> 7) & 31;
            if ((op == 0x6f || op == 0x67) && rd != 0)
            {
                lock (C.Sync) { tempBreak = pc + 4; hasTemp = true; }
                Resume();
            }
            else StepOne();
        }

        public void ToggleBreak(uint a) { lock (C.Sync) { if (!userBreaks.Remove(a)) userBreaks.Add(a); ApplyBreaks(); } StateVersion++; }
        public void ClearBreaks() { lock (C.Sync) { userBreaks.Clear(); ApplyBreaks(); } StateVersion++; }
        public uint[] UserBreaks { get { lock (C.Sync) { var a = new uint[userBreaks.Count]; userBreaks.CopyTo(a); Array.Sort(a); return a; } } }
        public bool IsBreak(uint a) { lock (C.Sync) return userBreaks.Contains(a); }

        void ApplyBreaks()
        {
            var l = new List<uint>(userBreaks);
            if (hasTemp) l.Add(tempBreak);
            C.M.SetBreakpoints(l);
        }

        // real time, in whole microseconds, without losing the fractions between calls
        Stopwatch clock;
        ulong accountedUs;
        ulong TakeMicroseconds()
        {
            ulong total = (ulong)(clock.ElapsedTicks * 1000000.0 / Stopwatch.Frequency);
            ulong d = total > accountedUs ? total - accountedUs : 0;
            accountedUs = total;
            return d;
        }

        public string LastError;

        void Loop()
        {
            try { LoopInner(); }
            catch (Exception e) { LastError = e.ToString(); running = false; StopReason = "The emulator stopped: " + e.Message; StateVersion++; }
        }

        void LoopInner()
        {
            var sw = Stopwatch.StartNew();
            clock = sw;
            long ipsCount = 0; long ipsStart = sw.ElapsedMilliseconds;
            double owed = 0;
            long lastException = 0;
            while (!quit)
            {
                if (!running)
                {
                    wake.WaitOne(50);
                    TakeMicroseconds();   // time while paused doesn't count
                    Ips = 0; ipsCount = 0; ipsStart = sw.ElapsedMilliseconds;
                    owed = 0;
                    continue;
                }
                var m = C.M;
                ulong dtUs = TakeMicroseconds();
                int speed = Speed;
                string stop = null;
                lock (C.Sync)
                {
                    if (m.ResetRequested) { C.Reset(); }
                    if (m.PoweredOff) { stop = "Powered off"; }
                    else if (speed <= 0)
                    {
                        // full speed: real time, about 8 ms of work per slice, in small chunks so the timer is on time
                        m.VirtualTime = false;
                        m.AdvanceTime(dtUs);
                        long sliceEnd = sw.ElapsedTicks + Stopwatch.Frequency / 125;
                        while (true)
                        {
                            int n = m.Run(20000);
                            ipsCount += n; TotalRun += n;
                            m.AdvanceTime(TakeMicroseconds());
                            if (m.BreakHit || m.PoweredOff || m.ResetRequested || (m.Waiting && !m.InterruptPending) || sw.ElapsedTicks >= sliceEnd) break;
                            if (StopOnCrash && m.ExceptionCount != lastException && NewCrash(ref lastException)) { stop = crashText; break; }
                        }
                    }
                    else
                    {
                        // slow motion: virtual time, a budget of instructions per real second
                        m.VirtualTime = true;
                        if (m.Waiting && !m.InterruptPending) m.AdvanceTime(dtUs);
                        owed = Math.Min(owed + speed * (dtUs / 1000000.0), Math.Max(1, speed / 10.0));
                        int n = (int)owed;
                        if (n > 0)
                        {
                            int done = m.Run(n);
                            owed -= Math.Max(done, n);
                            ipsCount += done; TotalRun += done;
                        }
                    }
                    if (stop == null && m.BreakHit)
                    {
                        stop = "Breakpoint at 0x" + m.Pc.ToString("x8");
                        if (hasTemp && m.Pc == tempBreak) { hasTemp = false; stop = "Stepped over the call"; }
                        ApplyBreaks();
                    }
                    if (stop == null && StopOnCrash && m.ExceptionCount != lastException && NewCrash(ref lastException)) stop = crashText;
                    lastException = m.ExceptionCount;
                }
                if (stop != null)
                {
                    running = false; StopReason = stop; StateVersion++;
                    continue;
                }
                long ms = sw.ElapsedMilliseconds;
                if (ms - ipsStart >= 500) { Ips = ipsCount * 1000.0 / (ms - ipsStart); ipsCount = 0; ipsStart = ms; }
                if (m.Waiting && !m.InterruptPending)
                {
                    ulong t = m.TimeToTimer();
                    int waitMs = t == ulong.MaxValue ? 20 : (int)Math.Max(1, Math.Min(20, t / 1000));
                    m.Keyboard.Arrived.WaitOne(waitMs);
                }
                else if (speed > 0) Thread.Sleep(speed >= 2000 ? 2 : 10);
            }
        }

        string crashText;
        bool NewCrash(ref long last)
        {
            // look at the newest traps for an exception that isn't an ecall
            var m = C.M;
            long seq = m.TrapSeq;
            for (long s = seq; s > seq - 8 && s > 0; s--)
            {
                var r = m.TrapRing[s % m.TrapRing.Length];
                if (r.Seq != s) break;
                if ((r.Cause & 0x80000000u) == 0 && r.Cause != 11)
                {
                    crashText = Machine.CauseName(r.Cause) + " at 0x" + r.Pc.ToString("x8");
                    last = m.ExceptionCount;
                    return true;
                }
            }
            last = m.ExceptionCount;
            return false;
        }
    }
}
