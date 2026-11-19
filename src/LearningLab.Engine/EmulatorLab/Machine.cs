// Emulator Lab - the machine: a RISC-V RV32IM + Zicsr processor (machine mode only) and its memory map.
//
// Everything here follows "The RISC-V Instruction Set Manual" (Volume I: unprivileged ISA, Volume II: privileged
// architecture, machine mode only). The interpreter is written for speed: registers live in an array, RAM is an
// array of 32-bit words, and nothing is allocated while instructions run.
using System;
using System.Collections.Generic;
using System.Threading;

namespace EmulatorLab
{
    /// <summary>The memory map: where each part of the computer sits in the 32-bit address space.</summary>
    public static class Map
    {
        public const uint RomBase = 0x00001000, RomSize = 0x1000;         // boot ROM (4 KB, read-only): os\boot.s
        public const uint ClintBase = 0x02000000, ClintSize = 0x10000;    // timer: msip, mtimecmp, mtime
        public const uint UartBase = 0x10000000;                          // 16550-style serial text console
        public const uint KbdBase = 0x10001000;                           // keyboard
        public const uint DiskBase = 0x10002000;                          // block disk (512-byte sectors, DMA)
        public const uint RtcBase = 0x10003000;                           // real-time clock
        public const uint PowerBase = 0x10004000;                         // power off / reset
        public const uint VideoBase = 0x10005000;                         // video mode and palette
        public const uint DeviceSize = 0x1000;
        public const uint FlashBase = 0x20000000, FlashSize = 0x100000;   // 1 MB read-only: LabOS and the demo programs
        public const uint FbBase = 0x11000000, FbSize = 64000;            // 320 x 200 pixels, one byte (palette index) each
        public const uint RamBase = 0x80000000, RamSize = 0x1000000;      // 16 MB RAM
        public const int ScreenW = 320, ScreenH = 200;
        public const ulong TimerHz = 1000000;                             // mtime counts microseconds
    }

    /// <summary>One entry of the trap log: an exception or an interrupt the processor took.</summary>
    public struct TrapRecord
    {
        public ulong Instret;
        public uint Cause;     // mcause (bit 31 set for interrupts)
        public uint Pc;        // mepc
        public uint Tval;      // mtval
        public long Seq;
    }

    public sealed partial class Machine
    {
        // ------------------------------------------------------------------ processor state
        public readonly uint[] X = new uint[32];
        public uint Pc;
        public readonly uint[] Ram = new uint[Map.RamSize / 4];
        public readonly byte[] Rom = new byte[Map.RomSize];
        public readonly byte[] Flash = new byte[Map.FlashSize];

        // machine-mode CSRs
        public uint Mstatus, Mie, Mip, Mtvec, Mepc, Mcause, Mtval, Mscratch;
        public ulong Cycle, Instret;
        public ulong Mtime, Mtimecmp = ulong.MaxValue;
        public uint Msip;

        public const uint MSTATUS_MIE = 1u << 3, MSTATUS_MPIE = 1u << 7, MSTATUS_MPP = 3u << 11;
        public const uint MIP_MSIP = 1u << 3, MIP_MTIP = 1u << 7, MIP_MEIP = 1u << 11;
        public const uint MISA = 0x40001100;   // RV32 (MXL=1), extensions I and M

        /// <summary>Virtual time: mtime goes up by one for every instruction (deterministic). Otherwise the
        /// host adds real microseconds with <see cref="AdvanceTime"/>.</summary>
        public bool VirtualTime = true;
        /// <summary>The processor executed WFI and sleeps until an interrupt is pending.</summary>
        public bool Waiting;
        public bool PoweredOff, ResetRequested;
        public uint PowerCode;               // the value written to the power register
        public volatile bool ExternalDirty;  // a device changed from another thread (a key was pressed)
        bool stop;                           // leave Run() after this instruction (power off / reset)
        bool suppressInstret;

        // devices
        public readonly Uart Uart;
        public readonly Keyboard Keyboard;
        public readonly Disk Disk;
        public readonly Video Video;
        public readonly Rtc Rtc;

        // breakpoints (addresses); checked only when there are any
        uint[] breaks = new uint[0];
        public bool BreakHit;
        public bool SkipBreakOnce;

        // trap log: a ring of the most recent traps
        public readonly TrapRecord[] TrapRing = new TrapRecord[512];
        public long TrapSeq;
        public long ExceptionCount, InterruptCount;
        /// <summary>Exceptions other than ecall: things going wrong (illegal instructions, bad addresses...).</summary>
        public long FaultCount;

        public Machine()
        {
            Uart = new Uart(this);
            Keyboard = new Keyboard(this);
            Disk = new Disk(this);
            Video = new Video();
            Rtc = new Rtc();
            Reset();
        }

        /// <summary>Like pressing the reset button: the processor and devices start over at the boot ROM. RAM keeps
        /// whatever was in it (as on a real computer), unless <paramref name="clearRam"/>.</summary>
        public void Reset(bool clearRam = false)
        {
            Array.Clear(X, 0, 32);
            if (clearRam) Array.Clear(Ram, 0, Ram.Length);
            Pc = Map.RomBase;
            Mstatus = MSTATUS_MPP; Mie = 0; Mip = 0; Mtvec = 0; Mepc = 0; Mcause = 0; Mtval = 0; Mscratch = 0;
            Cycle = 0; Instret = 0; Mtime = 0; Mtimecmp = ulong.MaxValue; Msip = 0;
            Waiting = false; PoweredOff = false; ResetRequested = false; PowerCode = 0; stop = false; BreakHit = false;
            ExceptionCount = 0; InterruptCount = 0; FaultCount = 0;
            Uart.Reset(); Keyboard.Reset(); Disk.Reset(); Video.Reset();
            UpdateExternal();
        }

        public void SetBreakpoints(IEnumerable<uint> addrs)
        {
            var l = new List<uint>(addrs);
            breaks = l.ToArray();
        }
        public uint[] Breakpoints { get { return (uint[])breaks.Clone(); } }

        // ------------------------------------------------------------------ time and interrupts
        public void AdvanceTime(ulong us)
        {
            Mtime += us;
            UpdateTimer();
        }
        public void UpdateTimer()
        {
            if (Mtime >= Mtimecmp) Mip |= MIP_MTIP; else Mip &= ~MIP_MTIP;
        }
        /// <summary>Recomputes the interrupt lines from the devices (keyboard, serial port) and the timer.</summary>
        public void UpdateExternal()
        {
            ExternalDirty = false;
            bool ext = Keyboard.IrqLine || Uart.IrqLine;
            if (ext) Mip |= MIP_MEIP; else Mip &= ~MIP_MEIP;
            if ((Msip & 1) != 0) Mip |= MIP_MSIP; else Mip &= ~MIP_MSIP;
            UpdateTimer();
        }
        /// <summary>True when an interrupt is waiting and enabled in mie (WFI wakes up then, whatever mstatus.MIE says).</summary>
        public bool InterruptPending { get { return (Mip & Mie) != 0; } }
        /// <summary>For virtual time: microseconds until the timer interrupt (ulong.MaxValue if it can't come).</summary>
        public ulong TimeToTimer()
        {
            if ((Mie & MIP_MTIP) == 0 || Mtimecmp == ulong.MaxValue) return ulong.MaxValue;
            return Mtimecmp > Mtime ? Mtimecmp - Mtime : 0;
        }

        void Record(uint cause, uint pc, uint tval)
        {
            long s = ++TrapSeq;
            int i = (int)(s % TrapRing.Length);
            TrapRing[i].Instret = Instret; TrapRing[i].Cause = cause; TrapRing[i].Pc = pc; TrapRing[i].Tval = tval; TrapRing[i].Seq = s;
        }

        /// <summary>Takes a synchronous exception for the instruction at <paramref name="pc"/>.</summary>
        void Exception(uint cause, uint tval, uint pc)
        {
            ExceptionCount++;
            if (cause != 11) FaultCount++;
            Mepc = pc; Mcause = cause; Mtval = tval;
            EnterTrap();
            Record(cause, pc, tval);
            Pc = Mtvec & ~3u;
        }

        void TakeInterrupt(uint pc)
        {
            uint p = Mip & Mie;
            uint code = (p & MIP_MEIP) != 0 ? 11u : (p & MIP_MSIP) != 0 ? 3u : 7u;   // priority: external, software, timer
            InterruptCount++;
            Mepc = pc; Mcause = 0x80000000u | code; Mtval = 0;
            EnterTrap();
            Record(Mcause, pc, 0);
            uint b = Mtvec & ~3u;
            Pc = (Mtvec & 3) == 1 ? b + 4 * code : b;
        }

        void EnterTrap()
        {
            // mstatus: MPIE = MIE, MIE = 0, MPP = machine
            uint mie = (Mstatus & MSTATUS_MIE) != 0 ? MSTATUS_MPIE : 0;
            Mstatus = (Mstatus & ~(MSTATUS_MIE | MSTATUS_MPIE)) | mie | MSTATUS_MPP;
            Waiting = false;
        }

        // ------------------------------------------------------------------ the interpreter
        /// <summary>Executes one instruction (or takes one pending interrupt and executes the first handler
        /// instruction). Ignores breakpoints.</summary>
        public void Step()
        {
            if (Waiting && !InterruptPending) return;
            var b = breaks; breaks = new uint[0];
            try { Run(1); } finally { breaks = b; }
        }

        /// <summary>Runs up to <paramref name="max"/> instructions. Stops early at a breakpoint (before executing
        /// that instruction; never at the first one when <see cref="SkipBreakOnce"/> is set), in WFI with nothing
        /// pending, or on power off / reset. Returns the number of instructions executed.</summary>
        public int Run(int max)
        {
            if (ExternalDirty) UpdateExternal();
            uint[] x = X;
            uint[] ram = Ram;
            uint[] bps = breaks;
            bool skipBreak = SkipBreakOnce; SkipBreakOnce = false;
            BreakHit = false;
            int n = 0;
            stop = false;
            while (n < max)
            {
                if (Waiting)
                {
                    if ((Mip & Mie) != 0) Waiting = false;
                    else break;
                }
                if ((Mip & Mie) != 0 && (Mstatus & MSTATUS_MIE) != 0) TakeInterrupt(Pc);
                uint pc = Pc;
                if (bps.Length != 0)
                {
                    if (skipBreak) skipBreak = false;
                    else
                    {
                        bool hit = false;
                        for (int i = 0; i < bps.Length; i++) if (bps[i] == pc) { hit = true; break; }
                        if (hit) { BreakHit = true; break; }
                    }
                }
                n++;
                Cycle++;
                if (VirtualTime) { Mtime++; if (Mtime >= Mtimecmp) Mip |= MIP_MTIP; }

                uint inst;
                uint off = pc - Map.RamBase;
                if (off < Map.RamSize) inst = ram[off >> 2];
                else if (!FetchSlow(pc, out inst)) { Exception(1, pc, pc); continue; }

                uint rd = (inst >> 7) & 31, rs1 = (inst >> 15) & 31, rs2 = (inst >> 20) & 31, f3 = (inst >> 12) & 7;
                uint npc = pc + 4;
                switch (inst & 0x7f)
                {
                    case 0x37: // LUI
                        if (rd != 0) x[rd] = inst & 0xfffff000u;
                        break;
                    case 0x17: // AUIPC
                        if (rd != 0) x[rd] = pc + (inst & 0xfffff000u);
                        break;
                    case 0x6f: // JAL
                        {
                            uint imm = ((inst >> 31) != 0 ? 0xfff00000u : 0) | (inst & 0x000ff000u) | ((inst >> 9) & 0x800u) | ((inst >> 20) & 0x7feu);
                            uint t = pc + imm;
                            if ((t & 3) != 0) { Exception(0, t, pc); continue; }
                            if (rd != 0) x[rd] = npc;
                            npc = t;
                            break;
                        }
                    case 0x67: // JALR
                        {
                            if (f3 != 0) goto illegal;
                            uint t = (x[rs1] + (uint)((int)inst >> 20)) & ~1u;
                            if ((t & 3) != 0) { Exception(0, t, pc); continue; }
                            if (rd != 0) x[rd] = npc;
                            npc = t;
                            break;
                        }
                    case 0x63: // branches
                        {
                            uint a = x[rs1], b = x[rs2];
                            bool take;
                            switch (f3)
                            {
                                case 0: take = a == b; break;                    // BEQ
                                case 1: take = a != b; break;                    // BNE
                                case 4: take = (int)a < (int)b; break;           // BLT
                                case 5: take = (int)a >= (int)b; break;          // BGE
                                case 6: take = a < b; break;                     // BLTU
                                case 7: take = a >= b; break;                    // BGEU
                                default: goto illegal;
                            }
                            if (take)
                            {
                                uint imm = ((inst >> 31) != 0 ? 0xfffff000u : 0) | ((inst << 4) & 0x800u) | ((inst >> 20) & 0x7e0u) | ((inst >> 7) & 0x1eu);
                                uint t = pc + imm;
                                if ((t & 3) != 0) { Exception(0, t, pc); continue; }
                                npc = t;
                            }
                            break;
                        }
                    case 0x03: // loads
                        {
                            uint a = x[rs1] + (uint)((int)inst >> 20);
                            uint v;
                            uint o = a - Map.RamBase;
                            switch (f3)
                            {
                                case 0: // LB
                                case 4: // LBU
                                    if (o < Map.RamSize) v = (ram[o >> 2] >> (int)((o & 3) * 8)) & 0xff;
                                    else if (!BusRead(a, 1, out v)) { Exception(5, a, pc); continue; }
                                    if (f3 == 0) v = (uint)(sbyte)(byte)v;
                                    break;
                                case 1: // LH
                                case 5: // LHU
                                    if ((a & 1) != 0) { Exception(4, a, pc); continue; }
                                    if (o < Map.RamSize) v = (ram[o >> 2] >> (int)((o & 2) * 8)) & 0xffff;
                                    else if (!BusRead(a, 2, out v)) { Exception(5, a, pc); continue; }
                                    if (f3 == 1) v = (uint)(short)(ushort)v;
                                    break;
                                case 2: // LW
                                    if ((a & 3) != 0) { Exception(4, a, pc); continue; }
                                    if (o < Map.RamSize) v = ram[o >> 2];
                                    else if (!BusRead(a, 4, out v)) { Exception(5, a, pc); continue; }
                                    break;
                                default: goto illegal;
                            }
                            if (rd != 0) x[rd] = v;
                            break;
                        }
                    case 0x23: // stores
                        {
                            uint a = x[rs1] + (uint)(((int)(inst & 0xfe000000u) >> 20) | (int)((inst >> 7) & 31));
                            uint v = x[rs2];
                            uint o = a - Map.RamBase;
                            switch (f3)
                            {
                                case 0: // SB
                                    if (o < Map.RamSize) { int sh = (int)((o & 3) * 8); ram[o >> 2] = (ram[o >> 2] & ~(0xffu << sh)) | ((v & 0xff) << sh); }
                                    else if (!BusWrite(a, 1, v & 0xff)) { Exception(7, a, pc); continue; }
                                    break;
                                case 1: // SH
                                    if ((a & 1) != 0) { Exception(6, a, pc); continue; }
                                    if (o < Map.RamSize) { int sh = (int)((o & 2) * 8); ram[o >> 2] = (ram[o >> 2] & ~(0xffffu << sh)) | ((v & 0xffff) << sh); }
                                    else if (!BusWrite(a, 2, v & 0xffff)) { Exception(7, a, pc); continue; }
                                    break;
                                case 2: // SW
                                    if ((a & 3) != 0) { Exception(6, a, pc); continue; }
                                    if (o < Map.RamSize) ram[o >> 2] = v;
                                    else if (!BusWrite(a, 4, v)) { Exception(7, a, pc); continue; }
                                    break;
                                default: goto illegal;
                            }
                            break;
                        }
                    case 0x13: // OP-IMM
                        {
                            uint a = x[rs1];
                            uint imm = (uint)((int)inst >> 20);
                            uint v;
                            switch (f3)
                            {
                                case 0: v = a + imm; break;                                  // ADDI
                                case 2: v = (int)a < (int)imm ? 1u : 0u; break;              // SLTI
                                case 3: v = a < imm ? 1u : 0u; break;                        // SLTIU (imm sign-extended, compared unsigned)
                                case 4: v = a ^ imm; break;                                  // XORI
                                case 6: v = a | imm; break;                                  // ORI
                                case 7: v = a & imm; break;                                  // ANDI
                                case 1:                                                      // SLLI
                                    if ((inst >> 25) != 0) goto illegal;
                                    v = a << (int)rs2; break;
                                case 5:
                                    if ((inst >> 25) == 0) v = a >> (int)rs2;                // SRLI
                                    else if ((inst >> 25) == 0x20) v = (uint)((int)a >> (int)rs2);   // SRAI
                                    else goto illegal;
                                    break;
                                default: goto illegal;
                            }
                            if (rd != 0) x[rd] = v;
                            break;
                        }
                    case 0x33: // OP
                        {
                            uint a = x[rs1], b = x[rs2];
                            uint f7 = inst >> 25;
                            uint v;
                            if (f7 == 0)
                            {
                                switch (f3)
                                {
                                    case 0: v = a + b; break;                                // ADD
                                    case 1: v = a << (int)(b & 31); break;                   // SLL
                                    case 2: v = (int)a < (int)b ? 1u : 0u; break;            // SLT
                                    case 3: v = a < b ? 1u : 0u; break;                      // SLTU
                                    case 4: v = a ^ b; break;                                // XOR
                                    case 5: v = a >> (int)(b & 31); break;                   // SRL
                                    case 6: v = a | b; break;                                // OR
                                    default: v = a & b; break;                               // AND
                                }
                            }
                            else if (f7 == 0x20)
                            {
                                if (f3 == 0) v = a - b;                                       // SUB
                                else if (f3 == 5) v = (uint)((int)a >> (int)(b & 31));        // SRA
                                else goto illegal;
                            }
                            else if (f7 == 1)
                            {
                                switch (f3)
                                {
                                    case 0: v = a * b; break;                                                // MUL
                                    case 1: v = (uint)(((long)(int)a * (long)(int)b) >> 32); break;          // MULH
                                    case 2: v = (uint)(((long)(int)a * (long)b) >> 32); break;               // MULHSU
                                    case 3: v = (uint)(((ulong)a * (ulong)b) >> 32); break;                  // MULHU
                                    case 4:                                                                  // DIV
                                        if (b == 0) v = 0xffffffffu;
                                        else if (a == 0x80000000u && b == 0xffffffffu) v = a;
                                        else v = (uint)((int)a / (int)b);
                                        break;
                                    case 5: v = b == 0 ? 0xffffffffu : a / b; break;                         // DIVU
                                    case 6:                                                                  // REM
                                        if (b == 0) v = a;
                                        else if (a == 0x80000000u && b == 0xffffffffu) v = 0;
                                        else v = (uint)((int)a % (int)b);
                                        break;
                                    default: v = b == 0 ? a : a % b; break;                                  // REMU
                                }
                            }
                            else goto illegal;
                            if (rd != 0) x[rd] = v;
                            break;
                        }
                    case 0x0f: // FENCE, FENCE.I: one hart and no caches, so nothing to do
                        if (f3 > 1) goto illegal;
                        break;
                    case 0x73: // SYSTEM
                        if (f3 == 0)
                        {
                            if (inst == 0x00000073) { Exception(11, 0, pc); continue; }        // ECALL from machine mode
                            if (inst == 0x00100073) { Exception(3, pc, pc); continue; }        // EBREAK
                            if (inst == 0x30200073)                                            // MRET
                            {
                                uint mpie = (Mstatus & MSTATUS_MPIE) != 0 ? MSTATUS_MIE : 0;
                                Mstatus = (Mstatus & ~(MSTATUS_MIE)) | mpie | MSTATUS_MPIE | MSTATUS_MPP;
                                npc = Mepc;
                                break;
                            }
                            if (inst == 0x10500073)                                            // WFI
                            {
                                if ((Mip & Mie) == 0) Waiting = true;
                                break;
                            }
                            goto illegal;
                        }
                        else if (f3 != 4)
                        {
                            uint csr = inst >> 20;
                            uint src = (f3 & 4) != 0 ? rs1 : x[rs1];       // CSRR*I use the 5-bit rs1 field as the value
                            uint op = f3 & 3;                              // 1 = write, 2 = set, 3 = clear
                            bool write = op == 1 || rs1 != 0;              // CSRRS/CSRRC with x0 (or uimm 0) don't write
                            bool read = op != 1 || rd != 0;                // CSRRW with rd = x0 doesn't read
                            uint old;
                            if (!CsrRead(csr, out old)) goto illegal;
                            if (write)
                            {
                                if ((csr >> 10) == 3) goto illegal;        // read-only CSR
                                uint nv = op == 1 ? src : op == 2 ? (old | src) : (old & ~src);
                                if (!CsrWrite(csr, nv)) goto illegal;
                            }
                            if (read && rd != 0) x[rd] = old;
                            break;
                        }
                        goto illegal;
                    default:
                        goto illegal;
                }
                Pc = npc;
                if (suppressInstret) suppressInstret = false; else Instret++;
                if (stop) break;
                continue;
            illegal:
                Exception(2, inst, pc);
            }
            return n;
        }

        // ------------------------------------------------------------------ CSRs
        public bool CsrRead(uint csr, out uint v)
        {
            switch (csr)
            {
                case 0x300: v = Mstatus; return true;
                case 0x301: v = MISA; return true;
                case 0x304: v = Mie; return true;
                case 0x305: v = Mtvec; return true;
                case 0x340: v = Mscratch; return true;
                case 0x341: v = Mepc; return true;
                case 0x342: v = Mcause; return true;
                case 0x343: v = Mtval; return true;
                case 0x344: v = Mip; return true;
                case 0xB00: case 0xC00: v = (uint)Cycle; return true;
                case 0xB80: case 0xC80: v = (uint)(Cycle >> 32); return true;
                case 0xB02: case 0xC02: v = (uint)Instret; return true;
                case 0xB82: case 0xC82: v = (uint)(Instret >> 32); return true;
                case 0xC01: v = (uint)Mtime; return true;
                case 0xC81: v = (uint)(Mtime >> 32); return true;
                case 0xF11: case 0xF12: case 0xF13: case 0xF14: case 0xF15: v = 0; return true;   // mvendorid, marchid, mimpid, mhartid, mconfigptr
                case 0x310: v = 0; return true;   // mstatush (little-endian, nothing else)
                case 0x320: v = 0; return true;   // mcountinhibit: counters always run
                default: v = 0; return false;
            }
        }

        public bool CsrWrite(uint csr, uint v)
        {
            switch (csr)
            {
                case 0x300: Mstatus = (v & (MSTATUS_MIE | MSTATUS_MPIE)) | MSTATUS_MPP; return true;   // MPP is always machine
                case 0x301: return true;                                                                 // misa: writes ignored
                case 0x304: Mie = v & (MIP_MSIP | MIP_MTIP | MIP_MEIP); return true;
                case 0x305: Mtvec = (v & 1) != 0 ? (v & ~2u) : (v & ~3u); return true;                   // mode 0 (direct) or 1 (vectored)
                case 0x340: Mscratch = v; return true;
                case 0x341: Mepc = v & ~3u; return true;
                case 0x342: Mcause = v; return true;
                case 0x343: Mtval = v; return true;
                case 0x344: return true;                                                                 // mip: these bits are read-only here
                case 0xB00: Cycle = (Cycle & 0xffffffff00000000UL) | v; return true;
                case 0xB80: Cycle = (Cycle & 0xffffffffUL) | ((ulong)v << 32); return true;
                case 0xB02: Instret = (Instret & 0xffffffff00000000UL) | v; suppressInstret = true; return true;
                case 0xB82: Instret = (Instret & 0xffffffffUL) | ((ulong)v << 32); suppressInstret = true; return true;
                case 0x310: case 0x320: return true;
                default: return false;
            }
        }

        // ------------------------------------------------------------------ the bus (everything outside RAM)
        bool FetchSlow(uint a, out uint v)
        {
            if (a - Map.RomBase < Map.RomSize) { v = Word(Rom, a - Map.RomBase); return true; }
            if (a - Map.FlashBase < Map.FlashSize) { v = Word(Flash, a - Map.FlashBase); return true; }
            v = 0; return false;
        }

        static uint Word(byte[] b, uint o) { return (uint)(b[o] | (b[o + 1] << 8) | (b[o + 2] << 16) | (b[o + 3] << 24)); }

        static uint Part(byte[] b, uint o, int size)
        {
            if (size == 1) return b[o];
            if (size == 2) return (uint)(b[o] | (b[o + 1] << 8));
            return Word(b, o);
        }

        /// <summary>A read outside RAM. Returns false for an access fault (nothing there).</summary>
        public bool BusRead(uint a, int size, out uint v)
        {
            v = 0;
            uint o;
            if ((o = a - Map.RomBase) < Map.RomSize) { v = Part(Rom, o, size); return true; }
            if ((o = a - Map.FlashBase) < Map.FlashSize) { v = Part(Flash, o, size); return true; }
            if ((o = a - Map.FbBase) < Map.FbSize) { v = Part(Video.Fb, o, size); return true; }
            if ((o = a - Map.ClintBase) < Map.ClintSize)
            {
                uint w;
                if (o == 0) w = Msip;
                else if (o == 0x4000) w = (uint)Mtimecmp;
                else if (o == 0x4004) w = (uint)(Mtimecmp >> 32);
                else if (o == 0xBFF8) w = (uint)Mtime;
                else if (o == 0xBFFC) w = (uint)(Mtime >> 32);
                else w = 0;
                v = w; return true;
            }
            if ((o = a - Map.UartBase) < Map.DeviceSize) { v = Uart.Read(o); UpdateExternal(); return true; }
            if ((o = a - Map.KbdBase) < Map.DeviceSize) { v = Keyboard.Read(o); UpdateExternal(); return true; }
            if ((o = a - Map.DiskBase) < Map.DeviceSize) { v = Disk.Read(o); return true; }
            if ((o = a - Map.RtcBase) < Map.DeviceSize) { v = Rtc.Read(o); return true; }
            if ((o = a - Map.PowerBase) < Map.DeviceSize) { v = 0; return true; }
            if ((o = a - Map.VideoBase) < Map.DeviceSize) { v = Video.Read(o); return true; }
            return false;
        }

        /// <summary>A write outside RAM. Returns false for an access fault (read-only or nothing there).</summary>
        public bool BusWrite(uint a, int size, uint v)
        {
            uint o;
            if ((o = a - Map.FbBase) < Map.FbSize)
            {
                byte[] fb = Video.Fb;
                fb[o] = (byte)v;
                if (size >= 2 && o + 1 < Map.FbSize) fb[o + 1] = (byte)(v >> 8);
                if (size == 4 && o + 3 < Map.FbSize) { fb[o + 2] = (byte)(v >> 16); fb[o + 3] = (byte)(v >> 24); }
                Video.Dirty = true;
                return true;
            }
            if ((o = a - Map.UartBase) < Map.DeviceSize) { Uart.Write(o, v); UpdateExternal(); return true; }
            if ((o = a - Map.ClintBase) < Map.ClintSize)
            {
                if (o == 0) Msip = v & 1;
                else if (o == 0x4000) Mtimecmp = (Mtimecmp & 0xffffffff00000000UL) | v;
                else if (o == 0x4004) Mtimecmp = (Mtimecmp & 0xffffffffUL) | ((ulong)v << 32);
                else if (o == 0xBFF8) Mtime = (Mtime & 0xffffffff00000000UL) | v;
                else if (o == 0xBFFC) Mtime = (Mtime & 0xffffffffUL) | ((ulong)v << 32);
                UpdateExternal();
                return true;
            }
            if ((o = a - Map.KbdBase) < Map.DeviceSize) { Keyboard.Write(o, v); UpdateExternal(); return true; }
            if ((o = a - Map.DiskBase) < Map.DeviceSize) { Disk.Write(o, v); return true; }
            if ((o = a - Map.RtcBase) < Map.DeviceSize) return true;
            if ((o = a - Map.VideoBase) < Map.DeviceSize) { Video.Write(o, v); return true; }
            if ((o = a - Map.PowerBase) < Map.DeviceSize)
            {
                if (o != 0) return true;
                uint code = v & 0xffff;
                if (code == 0x5555 || code == 0x3333) { PoweredOff = true; PowerCode = v; stop = true; }
                else if (code == 0x7777) { ResetRequested = true; stop = true; }
                return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ memory helpers for the host (any address)
        public byte ReadByte(uint a)
        {
            uint o = a - Map.RamBase;
            if (o < Map.RamSize) return (byte)(Ram[o >> 2] >> (int)((o & 3) * 8));
            uint v;
            if ((a - Map.RomBase) < Map.RomSize || (a - Map.FlashBase) < Map.FlashSize || (a - Map.FbBase) < Map.FbSize)
                if (BusRead(a, 1, out v)) return (byte)v;
            return 0;
        }
        /// <summary>True when the address is memory (RAM, ROM, flash, framebuffer) that can be shown without side effects.</summary>
        public static bool IsMemory(uint a)
        {
            return (a - Map.RamBase) < Map.RamSize || (a - Map.RomBase) < Map.RomSize || (a - Map.FlashBase) < Map.FlashSize || (a - Map.FbBase) < Map.FbSize;
        }
        public void WriteByte(uint a, byte b)
        {
            uint o = a - Map.RamBase;
            if (o < Map.RamSize) { int sh = (int)((o & 3) * 8); Ram[o >> 2] = (Ram[o >> 2] & ~(0xffu << sh)) | ((uint)b << sh); return; }
            if ((o = a - Map.FbBase) < Map.FbSize) { Video.Fb[o] = b; Video.Dirty = true; }
        }
        public uint ReadWord(uint a)
        {
            uint o = a - Map.RamBase;
            if (o < Map.RamSize && (o & 3) == 0) return Ram[o >> 2];
            return (uint)(ReadByte(a) | (ReadByte(a + 1) << 8) | (ReadByte(a + 2) << 16) | (ReadByte(a + 3) << 24));
        }
        public void WriteWord(uint a, uint v)
        {
            uint o = a - Map.RamBase;
            if (o < Map.RamSize && (o & 3) == 0) { Ram[o >> 2] = v; return; }
            for (int i = 0; i < 4; i++) WriteByte(a + (uint)i, (byte)(v >> (8 * i)));
        }
        public void LoadBytes(uint a, byte[] data)
        {
            for (int i = 0; i < data.Length; i++) WriteByte(a + (uint)i, data[i]);
        }
        public byte[] ReadBytes(uint a, int count)
        {
            var b = new byte[count];
            for (int i = 0; i < count; i++) b[i] = ReadByte(a + (uint)i);
            return b;
        }
        /// <summary>Reads a zero-terminated string from memory (for the trap log and tests).</summary>
        public string ReadString(uint a, int max = 256)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < max; i++) { byte b = ReadByte(a + (uint)i); if (b == 0) break; sb.Append((char)b); }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ names
        public static readonly string[] AbiNames = {
            "zero", "ra", "sp", "gp", "tp", "t0", "t1", "t2", "s0", "s1", "a0", "a1", "a2", "a3", "a4", "a5",
            "a6", "a7", "s2", "s3", "s4", "s5", "s6", "s7", "s8", "s9", "s10", "s11", "t3", "t4", "t5", "t6" };

        public static string CauseName(uint cause)
        {
            if ((cause & 0x80000000u) != 0)
            {
                switch (cause & 0x7fffffff)
                {
                    case 3: return "Software interrupt";
                    case 7: return "Timer interrupt";
                    case 11: return "External interrupt (keyboard / serial)";
                    default: return "Interrupt " + (cause & 0x7fffffff);
                }
            }
            switch (cause)
            {
                case 0: return "Instruction address misaligned";
                case 1: return "Instruction access fault";
                case 2: return "Illegal instruction";
                case 3: return "Breakpoint (ebreak)";
                case 4: return "Load address misaligned";
                case 5: return "Load access fault";
                case 6: return "Store address misaligned";
                case 7: return "Store access fault";
                case 11: return "Environment call (ecall)";
                default: return "Exception " + cause;
            }
        }
    }
}
