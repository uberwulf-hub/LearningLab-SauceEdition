// Emulator Lab - the disassembler (machine code back to assembly text) and plain-language explanations.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace EmulatorLab
{
    public static class Disassembler
    {
        static string Rn(uint r) { return Machine.AbiNames[r & 31]; }
        static string Hex(uint v) { return "0x" + v.ToString("x", CultureInfo.InvariantCulture); }
        static string Num(int v) { return v.ToString(CultureInfo.InvariantCulture); }

        static string CsrName(uint c)
        {
            foreach (var kv in Assembler.CsrNames) if (kv.Value == c && !kv.Key.StartsWith("cycle") && !kv.Key.StartsWith("instret") && kv.Key != "time" && kv.Key != "timeh") return kv.Key;
            foreach (var kv in Assembler.CsrNames) if (kv.Value == c) return kv.Key;
            return Hex(c);
        }

        /// <summary>Decoded fields of an instruction.</summary>
        public struct Fields
        {
            public uint Op, Rd, Rs1, Rs2, F3, F7;
            public int ImmI, ImmS, ImmB, ImmJ;
            public uint ImmU;
        }
        public static Fields Decode(uint i)
        {
            var f = new Fields();
            f.Op = i & 0x7f; f.Rd = (i >> 7) & 31; f.Rs1 = (i >> 15) & 31; f.Rs2 = (i >> 20) & 31; f.F3 = (i >> 12) & 7; f.F7 = i >> 25;
            f.ImmI = (int)i >> 20;
            f.ImmS = ((int)(i & 0xfe000000u) >> 20) | (int)((i >> 7) & 31);
            f.ImmB = (int)(((i >> 31) != 0 ? 0xfffff000u : 0) | ((i << 4) & 0x800u) | ((i >> 20) & 0x7e0u) | ((i >> 7) & 0x1eu));
            f.ImmJ = (int)(((i >> 31) != 0 ? 0xfff00000u : 0) | (i & 0x000ff000u) | ((i >> 9) & 0x800u) | ((i >> 20) & 0x7feu));
            f.ImmU = i & 0xfffff000u;
            return f;
        }

        /// <summary>Turns one instruction word into assembly text. <paramref name="label"/> (optional) names addresses.</summary>
        public static string Disassemble(uint i, uint pc, Func<uint, string> label = null)
        {
            var f = Decode(i);
            Func<uint, string> tgt = a => { string n = label != null ? label(a) : null; return n != null ? n : Hex(a); };
            switch (f.Op)
            {
                case 0x37: return "lui " + Rn(f.Rd) + ", " + Hex(f.ImmU >> 12);
                case 0x17: return "auipc " + Rn(f.Rd) + ", " + Hex(f.ImmU >> 12);
                case 0x6f:
                    {
                        uint t = pc + (uint)f.ImmJ;
                        if (f.Rd == 0) return "j " + tgt(t);
                        if (f.Rd == 1) return "call " + tgt(t);
                        return "jal " + Rn(f.Rd) + ", " + tgt(t);
                    }
                case 0x67:
                    if (f.F3 != 0) break;
                    if (f.Rd == 0 && f.Rs1 == 1 && f.ImmI == 0) return "ret";
                    if (f.Rd == 0 && f.ImmI == 0) return "jr " + Rn(f.Rs1);
                    if (f.Rd == 1 && f.ImmI == 0) return "jalr " + Rn(f.Rs1);
                    return "jalr " + Rn(f.Rd) + ", " + Num(f.ImmI) + "(" + Rn(f.Rs1) + ")";
                case 0x63:
                    {
                        string[] n = { "beq", "bne", null, null, "blt", "bge", "bltu", "bgeu" };
                        if (n[f.F3] == null) break;
                        string t = tgt(pc + (uint)f.ImmB);
                        if (f.Rs2 == 0)
                        {
                            if (f.F3 == 0) return "beqz " + Rn(f.Rs1) + ", " + t;
                            if (f.F3 == 1) return "bnez " + Rn(f.Rs1) + ", " + t;
                            if (f.F3 == 4) return "bltz " + Rn(f.Rs1) + ", " + t;
                            if (f.F3 == 5) return "bgez " + Rn(f.Rs1) + ", " + t;
                        }
                        if (f.Rs1 == 0)
                        {
                            if (f.F3 == 4) return "bgtz " + Rn(f.Rs2) + ", " + t;
                            if (f.F3 == 5) return "blez " + Rn(f.Rs2) + ", " + t;
                        }
                        return n[f.F3] + " " + Rn(f.Rs1) + ", " + Rn(f.Rs2) + ", " + t;
                    }
                case 0x03:
                    {
                        string[] n = { "lb", "lh", "lw", null, "lbu", "lhu", null, null };
                        if (n[f.F3] == null) break;
                        return n[f.F3] + " " + Rn(f.Rd) + ", " + Num(f.ImmI) + "(" + Rn(f.Rs1) + ")";
                    }
                case 0x23:
                    {
                        string[] n = { "sb", "sh", "sw", null, null, null, null, null };
                        if (n[f.F3] == null) break;
                        return n[f.F3] + " " + Rn(f.Rs2) + ", " + Num(f.ImmS) + "(" + Rn(f.Rs1) + ")";
                    }
                case 0x13:
                    switch (f.F3)
                    {
                        case 0:
                            if (i == 0x00000013) return "nop";
                            if (f.Rs1 == 0) return "li " + Rn(f.Rd) + ", " + Num(f.ImmI);
                            if (f.ImmI == 0) return "mv " + Rn(f.Rd) + ", " + Rn(f.Rs1);
                            return "addi " + Rn(f.Rd) + ", " + Rn(f.Rs1) + ", " + Num(f.ImmI);
                        case 2: return "slti " + Rn(f.Rd) + ", " + Rn(f.Rs1) + ", " + Num(f.ImmI);
                        case 3: return f.ImmI == 1 ? "seqz " + Rn(f.Rd) + ", " + Rn(f.Rs1) : "sltiu " + Rn(f.Rd) + ", " + Rn(f.Rs1) + ", " + Num(f.ImmI);
                        case 4: return f.ImmI == -1 ? "not " + Rn(f.Rd) + ", " + Rn(f.Rs1) : "xori " + Rn(f.Rd) + ", " + Rn(f.Rs1) + ", " + Num(f.ImmI);
                        case 6: return "ori " + Rn(f.Rd) + ", " + Rn(f.Rs1) + ", " + Num(f.ImmI);
                        case 7: return "andi " + Rn(f.Rd) + ", " + Rn(f.Rs1) + ", " + Num(f.ImmI);
                        case 1: if (f.F7 != 0) break; return "slli " + Rn(f.Rd) + ", " + Rn(f.Rs1) + ", " + f.Rs2;
                        case 5:
                            if (f.F7 == 0) return "srli " + Rn(f.Rd) + ", " + Rn(f.Rs1) + ", " + f.Rs2;
                            if (f.F7 == 0x20) return "srai " + Rn(f.Rd) + ", " + Rn(f.Rs1) + ", " + f.Rs2;
                            break;
                    }
                    break;
                case 0x33:
                    {
                        string n = null;
                        if (f.F7 == 0) n = new[] { "add", "sll", "slt", "sltu", "xor", "srl", "or", "and" }[f.F3];
                        else if (f.F7 == 0x20) n = f.F3 == 0 ? "sub" : f.F3 == 5 ? "sra" : null;
                        else if (f.F7 == 1) n = new[] { "mul", "mulh", "mulhsu", "mulhu", "div", "divu", "rem", "remu" }[f.F3];
                        if (n == null) break;
                        if (n == "sub" && f.Rs1 == 0) return "neg " + Rn(f.Rd) + ", " + Rn(f.Rs2);
                        if (n == "sltu" && f.Rs1 == 0) return "snez " + Rn(f.Rd) + ", " + Rn(f.Rs2);
                        return n + " " + Rn(f.Rd) + ", " + Rn(f.Rs1) + ", " + Rn(f.Rs2);
                    }
                case 0x0f:
                    if (f.F3 == 0) return i == 0x0FF0000F ? "fence" : "fence " + FenceSet((i >> 24) & 15) + ", " + FenceSet((i >> 20) & 15);
                    if (f.F3 == 1) return "fence.i";
                    break;
                case 0x73:
                    if (i == 0x00000073) return "ecall";
                    if (i == 0x00100073) return "ebreak";
                    if (i == 0x30200073) return "mret";
                    if (i == 0x10500073) return "wfi";
                    if (f.F3 != 0 && f.F3 != 4)
                    {
                        uint csr = i >> 20;
                        string c = CsrName(csr);
                        string[] n = { null, "csrrw", "csrrs", "csrrc", null, "csrrwi", "csrrsi", "csrrci" };
                        string src = f.F3 >= 5 ? f.Rs1.ToString(CultureInfo.InvariantCulture) : Rn(f.Rs1);
                        if (f.F3 == 2 && f.Rs1 == 0) return "csrr " + Rn(f.Rd) + ", " + c;
                        if (f.Rd == 0)
                        {
                            if (f.F3 == 1) return "csrw " + c + ", " + src;
                            if (f.F3 == 2) return "csrs " + c + ", " + src;
                            if (f.F3 == 3) return "csrc " + c + ", " + src;
                            if (f.F3 == 5) return "csrwi " + c + ", " + src;
                            if (f.F3 == 6) return "csrsi " + c + ", " + src;
                            if (f.F3 == 7) return "csrci " + c + ", " + src;
                        }
                        return n[f.F3] + " " + Rn(f.Rd) + ", " + c + ", " + src;
                    }
                    break;
            }
            return ".word " + Hex(i) + "   # not an instruction";
        }

        static string FenceSet(uint b)
        {
            string s = "";
            if ((b & 8) != 0) s += "i";
            if ((b & 4) != 0) s += "o";
            if ((b & 2) != 0) s += "r";
            if ((b & 1) != 0) s += "w";
            return s.Length == 0 ? "0" : s;
        }

        // ------------------------------------------------------------------ plain language
        static string R(uint r) { return r == 0 ? "zero (always 0)" : Rn(r); }

        /// <summary>A sentence about what the instruction does, with the current register values.</summary>
        public static string Explain(uint i, uint pc, uint[] x)
        {
            var f = Decode(i);
            Func<uint, string> v = r => Rn(r) + " (" + (int)x[r] + ")";
            switch (f.Op)
            {
                case 0x37: return "Load upper immediate: puts " + Hex(f.ImmU) + " into " + Rn(f.Rd) + ". Big numbers are built in two steps: the top 20 bits first, then the rest with addi.";
                case 0x17: return "Add upper immediate to PC: " + Rn(f.Rd) + " = this instruction's address + " + Hex(f.ImmU) + " = " + Hex(pc + f.ImmU) + ". Programs use it to find their own data wherever they are in memory.";
                case 0x6f:
                    if (f.Rd == 0) return "Jump: the program continues at " + Hex(pc + (uint)f.ImmJ) + " instead of the next instruction.";
                    return "Call: jump to the function at " + Hex(pc + (uint)f.ImmJ) + " and remember where to come back (" + Hex(pc + 4) + ") in " + Rn(f.Rd) + ".";
                case 0x67:
                    if (f.Rd == 0 && f.Rs1 == 1) return "Return: go back to the address in ra (" + Hex(x[1]) + "), where this function was called from.";
                    return "Jump to the address in a register: " + Rn(f.Rs1) + " + " + f.ImmI + " = " + Hex(x[f.Rs1] + (uint)f.ImmI) + (f.Rd != 0 ? ", and remember the return address in " + Rn(f.Rd) : "") + ".";
                case 0x63:
                    {
                        string[] cmp = { "equal to", "not equal to", "", "", "less than", "greater than or equal to", "less than (unsigned)", "greater than or equal to (unsigned)" };
                        uint a = x[f.Rs1], b = x[f.Rs2];
                        bool take;
                        switch (f.F3) { case 0: take = a == b; break; case 1: take = a != b; break; case 4: take = (int)a < (int)b; break; case 5: take = (int)a >= (int)b; break; case 6: take = a < b; break; default: take = a >= b; break; }
                        return "Branch: if " + v(f.Rs1) + " is " + cmp[f.F3] + " " + v(f.Rs2) + ", jump to " + Hex(pc + (uint)f.ImmB) + ". Right now that is " + (take ? "true, so it will jump." : "false, so it carries on with the next instruction.");
                    }
                case 0x03:
                    {
                        string[] sz = { "1 byte (sign-extended)", "2 bytes (sign-extended)", "4 bytes (a word)", "", "1 byte", "2 bytes", "", "" };
                        return "Load: read " + sz[f.F3] + " from memory at " + Rn(f.Rs1) + " + " + f.ImmI + " = " + Hex(x[f.Rs1] + (uint)f.ImmI) + " into " + Rn(f.Rd) + ".";
                    }
                case 0x23:
                    {
                        string[] sz = { "the lowest byte", "the lowest 2 bytes", "all 4 bytes", "", "", "", "", "" };
                        return "Store: write " + sz[f.F3 & 3] + " of " + v(f.Rs2) + " to memory at " + Rn(f.Rs1) + " + " + f.ImmS + " = " + Hex(x[f.Rs1] + (uint)f.ImmS) + ".";
                    }
                case 0x13:
                    switch (f.F3)
                    {
                        case 0:
                            if (i == 0x13) return "No operation: does nothing (it is really addi zero, zero, 0).";
                            if (f.Rs1 == 0) return "Load immediate: put the number " + f.ImmI + " into " + Rn(f.Rd) + ".";
                            if (f.ImmI == 0) return "Copy: " + Rn(f.Rd) + " gets the value of " + v(f.Rs1) + ".";
                            return "Add a number: " + Rn(f.Rd) + " = " + v(f.Rs1) + " + " + f.ImmI + " = " + (int)(x[f.Rs1] + (uint)f.ImmI) + ".";
                        case 1: return "Shift left: move the bits of " + v(f.Rs1) + " " + f.Rs2 + " places to the left (multiply by " + (1L << (int)f.Rs2) + ") into " + Rn(f.Rd) + ".";
                        case 5: return "Shift right: move the bits of " + v(f.Rs1) + " " + f.Rs2 + " places to the right" + (f.F7 == 0x20 ? ", keeping the sign" : "") + " into " + Rn(f.Rd) + ".";
                        case 2: case 3: return "Set if less than: " + Rn(f.Rd) + " becomes 1 if " + v(f.Rs1) + " is less than " + f.ImmI + ", otherwise 0.";
                        case 4: return "Exclusive or: flip the bits of " + v(f.Rs1) + " where " + f.ImmI + " has a 1, into " + Rn(f.Rd) + ".";
                        case 6: return "Or: " + Rn(f.Rd) + " = " + v(f.Rs1) + " with the bits of " + f.ImmI + " switched on.";
                        case 7: return "And: " + Rn(f.Rd) + " = only the bits of " + v(f.Rs1) + " that are also in " + f.ImmI + " (a mask).";
                    }
                    break;
                case 0x33:
                    {
                        if (f.F7 == 1)
                        {
                            string[] mn = { "multiply", "multiply (top half, signed)", "multiply (top half, signed x unsigned)", "multiply (top half, unsigned)", "divide", "divide (unsigned)", "remainder", "remainder (unsigned)" };
                            return "Maths: " + Rn(f.Rd) + " = " + mn[f.F3] + " " + v(f.Rs1) + " by " + v(f.Rs2) + (f.F3 >= 4 && x[f.Rs2] == 0 ? ". Dividing by zero doesn't crash a RISC-V processor: it gives a fixed answer." : ".");
                        }
                        string[] n = { "+", "<<", "<", "< (unsigned)", "xor", ">>", "or", "and" };
                        if (f.F7 == 0x20 && f.F3 == 0) return "Subtract: " + Rn(f.Rd) + " = " + v(f.Rs1) + " - " + v(f.Rs2) + " = " + (int)(x[f.Rs1] - x[f.Rs2]) + ".";
                        if (f.F3 == 0) return "Add: " + Rn(f.Rd) + " = " + v(f.Rs1) + " + " + v(f.Rs2) + " = " + (int)(x[f.Rs1] + x[f.Rs2]) + ".";
                        if (f.F3 == 2 || f.F3 == 3) return "Set if less than: " + Rn(f.Rd) + " becomes 1 if " + v(f.Rs1) + " is less than " + v(f.Rs2) + ", otherwise 0.";
                        return "Calculate: " + Rn(f.Rd) + " = " + v(f.Rs1) + " " + n[f.F3] + " " + v(f.Rs2) + ".";
                    }
                case 0x0f: return "Fence: makes sure memory reads and writes happen in order. This simple computer always does them in order, so nothing happens.";
                case 0x73:
                    if (i == 0x73) return "Environment call: asks the operating system for help (a7 = " + x[17] + " says what for). The processor jumps to the trap handler.";
                    if (i == 0x00100073) return "Breakpoint: stops the program and jumps to the trap handler (debuggers use this).";
                    if (i == 0x30200073) return "Return from trap: the trap handler is finished, so go back to where the program was (mepc) and turn interrupts back on.";
                    if (i == 0x10500073) return "Wait for interrupt: the processor rests until something happens, like a key press or the timer. This saves power.";
                    return "Control and status register: read or change the processor setting " + CsrName(i >> 20) + ".";
            }
            return "This is not a valid instruction. Running it causes an 'illegal instruction' trap.";
        }
    }
}
