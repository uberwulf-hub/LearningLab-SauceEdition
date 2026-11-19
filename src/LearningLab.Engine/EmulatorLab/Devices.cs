// Emulator Lab - the devices on the bus: serial console (16550 subset), keyboard, disk, real-time clock, video.
// Register maps are documented in README.md and os\memmap.s.
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace EmulatorLab
{
    /// <summary>A small part of the classic 16550 serial chip: send and receive bytes, the line status register and
    /// the receive interrupt. Bytes sent go to the text console.</summary>
    public sealed class Uart
    {
        readonly Machine m;
        readonly Queue<byte> rx = new Queue<byte>();
        byte ier, lcr, mcr, scr, dll = 1, dlm;
        public Action<byte> Output;
        public long BytesSent;

        public Uart(Machine machine) { m = machine; }
        public void Reset() { lock (rx) rx.Clear(); ier = 0; lcr = 0; mcr = 0; scr = 0; dll = 1; dlm = 0; }

        /// <summary>The host sends a byte to the machine (it arrives in the receive register).</summary>
        public void Receive(byte b) { lock (rx) rx.Enqueue(b); m.ExternalDirty = true; }

        public bool IrqLine { get { lock (rx) return (ier & 1) != 0 && rx.Count > 0; } }

        public uint Read(uint off)
        {
            bool dlab = (lcr & 0x80) != 0;
            switch (off)
            {
                case 0: if (dlab) return dll; lock (rx) return rx.Count > 0 ? rx.Dequeue() : (byte)0;    // RBR
                case 1: return dlab ? dlm : ier;                                                        // IER
                case 2: lock (rx) return (uint)(0xC0 | (((ier & 1) != 0 && rx.Count > 0) ? 0x04 : 0x01)); // IIR (FIFOs on)
                case 3: return lcr;
                case 4: return mcr;
                case 5: lock (rx) return (uint)(0x60 | (rx.Count > 0 ? 1 : 0));                         // LSR: THR empty, data ready
                case 6: return 0xB0;                                                                    // MSR: CTS, DSR, DCD
                case 7: return scr;
                default: return 0;
            }
        }

        public void Write(uint off, uint v)
        {
            bool dlab = (lcr & 0x80) != 0;
            byte b = (byte)v;
            switch (off)
            {
                case 0:
                    if (dlab) { dll = b; break; }
                    BytesSent++;
                    var o = Output; if (o != null) o(b);
                    break;
                case 1: if (dlab) dlm = b; else ier = (byte)(b & 0x0F); break;
                case 2: if ((b & 2) != 0) lock (rx) rx.Clear(); break;                                  // FCR: clear receive FIFO
                case 3: lcr = b; break;
                case 4: mcr = b; break;
                case 7: scr = b; break;
            }
        }
    }

    /// <summary>The keyboard: a queue of key presses, each with its ASCII code and its PC scancode (set 1).</summary>
    public sealed class Keyboard
    {
        readonly Machine m;
        readonly Queue<uint> keys = new Queue<uint>();
        uint ctrl;
        /// <summary>Signalled when a key arrives, so a sleeping (WFI) machine thread wakes up.</summary>
        public readonly AutoResetEvent Arrived = new AutoResetEvent(false);

        public Keyboard(Machine machine) { m = machine; }
        public void Reset() { lock (keys) keys.Clear(); ctrl = 0; }

        public void Push(byte ascii, byte scancode)
        {
            lock (keys) { if (keys.Count < 64) keys.Enqueue((uint)ascii | ((uint)scancode << 8)); }
            m.ExternalDirty = true;
            Arrived.Set();
        }
        /// <summary>Types text (as if each character was pressed). Newlines become Enter.</summary>
        public void Type(string s)
        {
            foreach (char c in s)
            {
                if (c == '\n') Push(13, 0x1C);
                else if (c == '\b') Push(8, 0x0E);
                else if (c == '\x1b') Push(27, 0x01);
                else Push((byte)c, 0);
            }
        }
        public int Count { get { lock (keys) return keys.Count; } }

        public bool IrqLine { get { lock (keys) return (ctrl & 1) != 0 && keys.Count > 0; } }

        public uint Read(uint off)
        {
            switch (off)
            {
                case 0: lock (keys) return keys.Count > 0 ? 1u : 0u;                       // STATUS
                case 4: lock (keys) return keys.Count > 0 ? keys.Dequeue() : 0u;             // DATA (reading takes the key)
                case 8: return ctrl;                                                         // CONTROL
                case 12: lock (keys) return (uint)keys.Count;                                // COUNT
                default: return 0;
            }
        }
        public void Write(uint off, uint v)
        {
            if (off == 8) ctrl = v & 1;
            if (off == 16) lock (keys) keys.Clear();
        }
    }

    /// <summary>A block disk of 512-byte sectors, kept in a file. The machine gives a sector number, a RAM address
    /// and a count, then a command, and the disk copies the sectors straight to or from memory (DMA).</summary>
    public sealed class Disk : IDisposable
    {
        readonly Machine m;
        public const int SectorSize = 512;
        public readonly object Sync = new object();
        FileStream fs;
        uint sector, addr, count, status;
        public string Path { get; private set; }
        public long Reads, Writes;

        public Disk(Machine machine) { m = machine; }
        public void Reset() { sector = 0; addr = 0; count = 0; status = 0; }

        /// <summary>Opens (or creates, with <paramref name="sectors"/> empty sectors) the disk file.</summary>
        public void Attach(string path, int sectors)
        {
            lock (Sync)
            {
                Detach();
                string dir = System.IO.Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
                if (fs.Length < (long)sectors * SectorSize) fs.SetLength((long)sectors * SectorSize);
                Path = path;
            }
        }
        public void Detach() { lock (Sync) { if (fs != null) { fs.Flush(); fs.Dispose(); fs = null; } Path = null; } }
        public void Dispose() { Detach(); }
        public uint Sectors { get { lock (Sync) return fs == null ? 0 : (uint)(fs.Length / SectorSize); } }

        public bool ReadSectors(uint first, int n, byte[] buf, int offset)
        {
            lock (Sync)
            {
                if (fs == null || (long)(first + n) * SectorSize > fs.Length) return false;
                fs.Position = (long)first * SectorSize;
                int got = 0, want = n * SectorSize;
                while (got < want) { int r = fs.Read(buf, offset + got, want - got); if (r <= 0) return false; got += r; }
                Reads += n;
                return true;
            }
        }
        public bool WriteSectors(uint first, int n, byte[] buf, int offset)
        {
            lock (Sync)
            {
                if (fs == null || (long)(first + n) * SectorSize > fs.Length) return false;
                fs.Position = (long)first * SectorSize;
                fs.Write(buf, offset, n * SectorSize);
                fs.Flush();
                Writes += n;
                return true;
            }
        }

        public uint Read(uint off)
        {
            switch (off)
            {
                case 0x00: return sector;
                case 0x04: return addr;
                case 0x08: return count;
                case 0x10: return status;
                case 0x14: return Sectors;
                default: return 0;
            }
        }
        public void Write(uint off, uint v)
        {
            switch (off)
            {
                case 0x00: sector = v; break;
                case 0x04: addr = v; break;
                case 0x08: count = v; break;
                case 0x0C: status = Command(v); break;
            }
        }

        uint Command(uint cmd)
        {
            if (fs == null) return 2;
            if (count == 0) return 0;
            if (count > 4096) return 1;
            int bytes = (int)count * SectorSize;
            // DMA works on RAM only
            if (addr - Map.RamBase >= Map.RamSize || addr + (uint)bytes - Map.RamBase > Map.RamSize) return 1;
            var buf = new byte[bytes];
            if (cmd == 1)
            {
                if (!ReadSectors(sector, (int)count, buf, 0)) return 1;
                m.LoadBytes(addr, buf);
                return 0;
            }
            if (cmd == 2)
            {
                for (int i = 0; i < bytes; i++) buf[i] = m.ReadByte(addr + (uint)i);
                return WriteSectors(sector, (int)count, buf, 0) ? 0u : 1u;
            }
            return 1;
        }
    }

    /// <summary>The real-time clock: seconds since 1 January 1970 in local time (the host's clock).</summary>
    public sealed class Rtc
    {
        uint latchedHigh;
        /// <summary>Tests set a fixed time so results don't depend on the day.</summary>
        public DateTime? Fixed;
        public long Seconds()
        {
            DateTime now = Fixed ?? DateTime.Now;
            return (long)(now - new DateTime(1970, 1, 1)).TotalSeconds;
        }
        public uint Read(uint off)
        {
            if (off == 0) { long s = Seconds(); latchedHigh = (uint)(s >> 32); return (uint)s; }
            if (off == 4) return latchedHigh;
            return 0;
        }
    }

    /// <summary>Video: the 320 x 200 framebuffer (one palette index per pixel), 256 palette entries (0x00RRGGBB)
    /// and the screen mode (0 = text console, 1 = graphics, 2 = graphics with the text on top).</summary>
    public sealed class Video
    {
        public readonly byte[] Fb = new byte[Map.FbSize];
        public readonly uint[] Palette = new uint[256];
        public uint Mode;
        public uint Frames;
        public volatile bool Dirty = true;

        public void Reset()
        {
            Array.Clear(Fb, 0, Fb.Length);
            Mode = 0;
            DefaultPalette(Palette);
            Dirty = true;
        }

        /// <summary>The start-up palette: 16 classic PC colours, a 6 x 6 x 6 colour cube and 24 greys.</summary>
        public static void DefaultPalette(uint[] p)
        {
            uint[] pc16 = { 0x000000, 0x0000AA, 0x00AA00, 0x00AAAA, 0xAA0000, 0xAA00AA, 0xAA5500, 0xAAAAAA,
                            0x555555, 0x5555FF, 0x55FF55, 0x55FFFF, 0xFF5555, 0xFF55FF, 0xFFFF55, 0xFFFFFF };
            for (int i = 0; i < 16; i++) p[i] = pc16[i];
            int k = 16;
            for (int r = 0; r < 6; r++) for (int g = 0; g < 6; g++) for (int b = 0; b < 6; b++)
                p[k++] = (uint)((r * 51) << 16 | (g * 51) << 8 | (b * 51));
            for (int i = 0; i < 24; i++) { uint v = (uint)(8 + i * 10); p[k++] = v << 16 | v << 8 | v; }
        }

        public uint Read(uint off)
        {
            if (off == 0) return Mode;
            if (off == 4) return Frames;
            if (off >= 0x400 && off < 0x800) return Palette[(off - 0x400) >> 2];
            return 0;
        }
        public void Write(uint off, uint v)
        {
            if (off == 0) { Mode = v > 2 ? 0 : v; Dirty = true; }
            else if (off >= 0x400 && off < 0x800) { Palette[(off - 0x400) >> 2] = v & 0xffffff; Dirty = true; }
        }
    }
}
