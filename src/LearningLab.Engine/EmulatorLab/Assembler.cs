// Emulator Lab - a two-pass RISC-V (RV32IM + Zicsr) assembler.
//
// Supports labels (also numeric local labels: "1:" with "1b" / "1f"), the common pseudo-instructions, the
// directives .text .data .section .byte .half .word .ascii .asciz .string .align .balign .space .zero .equ .set
// .include .macro/.endm, ABI register names, expressions with labels, and %hi() / %lo().
// Pass 1 works out where every label is; pass 2 writes the machine code.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace EmulatorLab
{
    public sealed class AsmError
    {
        public string File;
        public int Line;
        public string Message;
        public override string ToString() { return (string.IsNullOrEmpty(File) ? "" : File + ", ") + "line " + Line + ": " + Message; }
    }

    public sealed class SourceLine
    {
        public string File;
        public int Line;
        public string Text;
    }

    public sealed class AsmResult
    {
        public byte[] Image = new byte[0];
        public uint Origin;
        public uint DataStart;
        public uint TextSize, DataSize;
        public readonly Dictionary<string, uint> Symbols = new Dictionary<string, uint>(StringComparer.Ordinal);
        public readonly List<AsmError> Errors = new List<AsmError>();
        /// <summary>For each instruction address, the source line it came from.</summary>
        public readonly Dictionary<uint, SourceLine> Lines = new Dictionary<uint, SourceLine>();
        public bool Ok { get { return Errors.Count == 0; } }
        public uint Entry { get { uint e; return Symbols.TryGetValue("_start", out e) ? e : Origin; } }
        public string ErrorText()
        {
            var sb = new StringBuilder();
            foreach (var e in Errors) sb.AppendLine(e.ToString());
            return sb.ToString();
        }
        /// <summary>The label at an address (the first one found), or null.</summary>
        public string LabelAt(uint a)
        {
            foreach (var kv in Symbols) if (kv.Value == a && !kv.Key.StartsWith(".") && kv.Key.IndexOf('$') < 0) return kv.Key;
            return null;
        }
    }

    sealed class AsmException : Exception { public AsmException(string m) : base(m) { } }

    public sealed class Assembler
    {
        /// <summary>Reads an included file: (path relative to the including file's folder or the base folder) -> text, or null.</summary>
        public Func<string, string> ReadFile;
        /// <summary>Folder that .include paths are relative to when the source has no folder of its own.</summary>
        public string BaseDir;

        // ------------------------------------------------------------------ tables
        static readonly Dictionary<string, int> Regs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, uint> Csrs = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
        {
            { "mstatus", 0x300 }, { "misa", 0x301 }, { "mie", 0x304 }, { "mtvec", 0x305 }, { "mstatush", 0x310 },
            { "mcountinhibit", 0x320 }, { "mscratch", 0x340 }, { "mepc", 0x341 }, { "mcause", 0x342 }, { "mtval", 0x343 },
            { "mip", 0x344 }, { "mcycle", 0xB00 }, { "minstret", 0xB02 }, { "mcycleh", 0xB80 }, { "minstreth", 0xB82 },
            { "cycle", 0xC00 }, { "time", 0xC01 }, { "instret", 0xC02 }, { "cycleh", 0xC80 }, { "timeh", 0xC81 },
            { "instreth", 0xC82 }, { "mvendorid", 0xF11 }, { "marchid", 0xF12 }, { "mimpid", 0xF13 }, { "mhartid", 0xF14 },
        };
        public static IDictionary<string, uint> CsrNames { get { return Csrs; } }

        static readonly HashSet<string> Mnemonics = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "lui", "auipc", "jal", "jalr", "beq", "bne", "blt", "bge", "bltu", "bgeu", "lb", "lh", "lw", "lbu", "lhu",
            "sb", "sh", "sw", "addi", "slti", "sltiu", "xori", "ori", "andi", "slli", "srli", "srai", "add", "sub", "sll",
            "slt", "sltu", "xor", "srl", "sra", "or", "and", "fence", "fence.i", "ecall", "ebreak", "mret", "wfi",
            "csrrw", "csrrs", "csrrc", "csrrwi", "csrrsi", "csrrci", "mul", "mulh", "mulhsu", "mulhu", "div", "divu",
            "rem", "remu",
            // pseudo-instructions
            "nop", "li", "la", "lla", "mv", "not", "neg", "seqz", "snez", "sltz", "sgtz", "beqz", "bnez", "blez", "bgez",
            "bltz", "bgtz", "bgt", "ble", "bgtu", "bleu", "j", "jr", "ret", "call", "tail", "csrr", "csrw", "csrs",
            "csrc", "csrwi", "csrsi", "csrci", "rdcycle", "rdcycleh", "rdinstret", "rdinstreth", "rdtime", "rdtimeh" };
        public static ICollection<string> InstructionNames { get { return Mnemonics; } }

        static Assembler()
        {
            for (int i = 0; i < 32; i++) { Regs["x" + i] = i; Regs[Machine.AbiNames[i]] = i; }
            Regs["fp"] = 8;
        }
        public static bool IsRegister(string s) { return Regs.ContainsKey(s); }

        // ------------------------------------------------------------------ preprocessing: .include and .macro
        sealed class Line { public string File; public int No; public string Text; }
        sealed class Macro { public string Name; public string[] Params; public List<Line> Body = new List<Line>(); }

        List<Line> lines;
        Dictionary<string, Macro> macros;
        int macroCounter;
        AsmResult res;

        void Error(Line l, string msg)
        {
            if (res.Errors.Count >= 100) return;
            foreach (var e in res.Errors) if (e.Line == l.No && e.File == l.File) return;   // one message per line is plenty
            res.Errors.Add(new AsmError { File = l.File, Line = l.No, Message = msg });
        }

        void Preprocess(string text, string file, int depth)
        {
            if (depth > 16) throw new AsmException(".include goes too deep (a file including itself?)");
            string[] src = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            Macro collecting = null;
            for (int i = 0; i < src.Length; i++)
            {
                var l = new Line { File = file, No = i + 1, Text = src[i] };
                string code = StripComment(l.Text).Trim();
                string first = FirstWord(code);
                if (collecting != null)
                {
                    if (first.Equals(".endm", StringComparison.OrdinalIgnoreCase)) { macros[collecting.Name.ToLowerInvariant()] = collecting; collecting = null; }
                    else collecting.Body.Add(l);
                    continue;
                }
                if (first.Equals(".macro", StringComparison.OrdinalIgnoreCase))
                {
                    string rest = code.Substring(6).Trim();
                    string name = FirstWord(rest);
                    if (name.Length == 0) { Error(l, ".macro needs a name"); continue; }
                    var ps = new List<string>();
                    foreach (var p in SplitOperands(rest.Substring(name.Length).Trim().Replace(' ', ',')))
                        if (p.Trim().Length > 0) ps.Add(p.Trim());
                    collecting = new Macro { Name = name, Params = ps.ToArray() };
                    continue;
                }
                if (first.Equals(".include", StringComparison.OrdinalIgnoreCase))
                {
                    string arg = code.Substring(8).Trim();
                    if (arg.Length >= 2 && arg[0] == '"' && arg[arg.Length - 1] == '"') arg = arg.Substring(1, arg.Length - 2);
                    string inc = null, path = arg;
                    try
                    {
                        if (ReadFile != null)
                        {
                            string dir = !string.IsNullOrEmpty(file) && Path.IsPathRooted(file) ? Path.GetDirectoryName(file) : BaseDir;
                            if (!string.IsNullOrEmpty(dir)) path = Path.Combine(dir, arg);
                            inc = ReadFile(path);
                        }
                    }
                    catch (Exception) { inc = null; }
                    if (inc == null) { Error(l, "Can't find the file to include: \"" + arg + "\""); continue; }
                    try { Preprocess(inc, path, depth + 1); }
                    catch (AsmException e) { Error(l, e.Message); }
                    continue;
                }
                ExpandLine(l, 0);
            }
            if (collecting != null) Error(new Line { File = file, No = src.Length }, ".macro " + collecting.Name + " has no .endm");
        }

        void ExpandLine(Line l, int depth)
        {
            string code = StripComment(l.Text);
            string rest;
            List<string> labels = SplitLabels(code, out rest);
            string word = FirstWord(rest.Trim());
            Macro m;
            if (word.Length == 0 || !macros.TryGetValue(word.ToLowerInvariant(), out m)) { lines.Add(l); return; }
            if (depth > 20) { Error(l, "Macro " + word + " calls itself too many times"); return; }
            if (labels.Count > 0)
            {
                var sb = new StringBuilder();
                foreach (var lb in labels) sb.Append(lb).Append(": ");
                lines.Add(new Line { File = l.File, No = l.No, Text = sb.ToString() });
            }
            string argText = rest.Trim().Substring(word.Length).Trim();
            List<string> args = SplitOperands(argText);
            if (args.Count == 1 && args[0].Trim().Length == 0) args.Clear();
            if (args.Count > m.Params.Length) { Error(l, "Macro " + m.Name + " takes " + m.Params.Length + " values but got " + args.Count); return; }
            int id = ++macroCounter;
            foreach (var bl in m.Body)
            {
                string t = bl.Text;
                // longest names first, so \ab is not replaced by \a
                var order = new List<int>();
                for (int i = 0; i < m.Params.Length; i++) order.Add(i);
                order.Sort((a, b) => m.Params[b].Length.CompareTo(m.Params[a].Length));
                foreach (int i in order) t = t.Replace("\\" + m.Params[i], i < args.Count ? args[i].Trim() : "");
                t = t.Replace("\\@", id.ToString(CultureInfo.InvariantCulture));
                ExpandLine(new Line { File = l.File, No = l.No, Text = t }, depth + 1);
            }
        }

        // ------------------------------------------------------------------ text helpers
        public static string StripComment(string s)
        {
            bool inStr = false, inChr = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (inStr) { if (c == '\\') i++; else if (c == '"') inStr = false; continue; }
                if (inChr) { if (c == '\\') i++; else if (c == '\'') inChr = false; continue; }
                if (c == '"') inStr = true;
                else if (c == '\'') inChr = true;
                else if (c == '#') return s.Substring(0, i);
                else if (c == '/' && i + 1 < s.Length && s[i + 1] == '/') return s.Substring(0, i);
                else if (c == ';') return s.Substring(0, i);
            }
            return s;
        }

        static string FirstWord(string s)
        {
            int i = 0;
            while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] != ',') i++;
            return s.Substring(0, i);
        }

        static bool IsIdentStart(char c) { return char.IsLetter(c) || c == '_' || c == '.' || c == '$'; }
        static bool IsIdentChar(char c) { return char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '$'; }

        /// <summary>Splits "a: b: addi ..." into the labels and the rest.</summary>
        static List<string> SplitLabels(string code, out string rest)
        {
            var labels = new List<string>();
            int i = 0;
            while (true)
            {
                int j = i;
                while (j < code.Length && char.IsWhiteSpace(code[j])) j++;
                int start = j;
                if (j < code.Length && (IsIdentStart(code[j]) || char.IsDigit(code[j])))
                {
                    bool digits = char.IsDigit(code[j]);
                    while (j < code.Length && (digits ? char.IsDigit(code[j]) : IsIdentChar(code[j]))) j++;
                    int k = j;
                    while (k < code.Length && (code[k] == ' ' || code[k] == '\t')) k++;
                    if (k < code.Length && code[k] == ':')
                    {
                        labels.Add(code.Substring(start, j - start));
                        i = k + 1;
                        continue;
                    }
                }
                break;
            }
            rest = code.Substring(i);
            return labels;
        }

        /// <summary>Splits operands at commas that are not inside brackets, strings or character literals.</summary>
        public static List<string> SplitOperands(string s)
        {
            var parts = new List<string>();
            int depth = 0, start = 0;
            bool inStr = false, inChr = false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (inStr) { if (c == '\\') i++; else if (c == '"') inStr = false; continue; }
                if (inChr) { if (c == '\\') i++; else if (c == '\'') inChr = false; continue; }
                if (c == '"') inStr = true;
                else if (c == '\'') inChr = true;
                else if (c == '(') depth++;
                else if (c == ')') depth--;
                else if (c == ',' && depth == 0) { parts.Add(s.Substring(start, i - start).Trim()); start = i + 1; }
            }
            parts.Add(s.Substring(start).Trim());
            return parts;
        }

        // ------------------------------------------------------------------ symbols and expressions
        sealed class LabelDef { public int Index; public int Section; public uint Offset; }
        Dictionary<string, LabelDef> labelDefs;                // normal labels
        Dictionary<string, long> equs;                         // .equ / .set
        Dictionary<string, List<LabelDef>> localDefs;          // numeric local labels
        uint[] sectionBase = new uint[2];
        bool pass2;
        int curIndex;
        uint curPc;

        bool TryLabel(string name, out long v)
        {
            LabelDef d;
            if (labelDefs.TryGetValue(name, out d)) { v = sectionBase[d.Section] + d.Offset; return pass2 || d.Section == 0; }
            v = 0; return false;
        }

        bool TryLocal(string num, char dir, out long v)
        {
            v = 0;
            List<LabelDef> defs;
            if (!localDefs.TryGetValue(num, out defs)) return false;
            LabelDef best = null;
            if (dir == 'b') { foreach (var d in defs) if (d.Index <= curIndex) best = d; }
            else { foreach (var d in defs) if (d.Index > curIndex) { best = d; break; } }
            if (best == null) return false;
            v = sectionBase[best.Section] + best.Offset;
            return pass2 || best.Section == 0;
        }

        sealed class Expr
        {
            readonly Assembler a; readonly string s; int p;
            public bool Unknown;          // a label we don't know yet (pass 1)
            public string UnknownName;
            public Expr(Assembler asm, string text) { a = asm; s = text; }

            public long Parse()
            {
                long v = Or();
                Skip();
                if (p < s.Length) throw new AsmException("Can't understand \"" + s.Substring(p) + "\" in \"" + s + "\"");
                return v;
            }
            void Skip() { while (p < s.Length && char.IsWhiteSpace(s[p])) p++; }
            bool Eat(string op)
            {
                Skip();
                if (string.CompareOrdinal(s, p, op, 0, op.Length) == 0) { p += op.Length; return true; }
                return false;
            }
            bool Peek(string op) { Skip(); return string.CompareOrdinal(s, p, op, 0, op.Length) == 0; }
            long Or() { long v = Xor(); while (!Peek("||") && Eat("|")) v |= Xor(); return v; }
            long Xor() { long v = And(); while (Eat("^")) v ^= And(); return v; }
            long And() { long v = Shift(); while (!Peek("&&") && Eat("&")) v &= Shift(); return v; }
            long Shift()
            {
                long v = Sum();
                while (true)
                {
                    if (Eat("<<")) v = (long)((ulong)v << (int)(Sum() & 63));
                    else if (Eat(">>")) v = v >> (int)(Sum() & 63);
                    else return v;
                }
            }
            long Sum()
            {
                long v = Term();
                while (true)
                {
                    if (Eat("+")) v += Term();
                    else if (Eat("-")) v -= Term();
                    else return v;
                }
            }
            long Term()
            {
                long v = Unary();
                while (true)
                {
                    if (Eat("*")) v *= Unary();
                    else if (Eat("/")) { long d = Unary(); if (d == 0) { if (Unknown) d = 1; else throw new AsmException("Division by zero in \"" + s + "\""); } v /= d; }
                    else if (!Peek("%hi") && !Peek("%lo") && Eat("%")) { long d = Unary(); if (d == 0) { if (Unknown) d = 1; else throw new AsmException("Division by zero in \"" + s + "\""); } v %= d; }
                    else return v;
                }
            }
            long Unary()
            {
                if (Eat("-")) return -Unary();
                if (Eat("+")) return Unary();
                if (Eat("~")) return ~Unary();
                return Atom();
            }
            long Atom()
            {
                Skip();
                if (p >= s.Length) throw new AsmException("A number or label is missing in \"" + s + "\"");
                if (Eat("%hi(")) { long v = Or(); if (!Eat(")")) throw new AsmException("Missing ) after %hi("); return ((v + 0x800) >> 12) & 0xFFFFF; }
                if (Eat("%lo(")) { long v = Or(); if (!Eat(")")) throw new AsmException("Missing ) after %lo("); return SignExtend12(v); }
                if (Eat("(")) { long v = Or(); if (!Eat(")")) throw new AsmException("Missing ) in \"" + s + "\""); return v; }
                char c = s[p];
                if (c == '\'')
                {
                    int q = p + 1;
                    int ch = ReadChar(s, ref q);
                    if (q >= s.Length || s[q] != '\'') throw new AsmException("Character literal not closed: " + s.Substring(p));
                    p = q + 1;
                    return ch;
                }
                if (char.IsDigit(c))
                {
                    int q = p;
                    while (q < s.Length && IsIdentChar(s[q])) q++;
                    string tok = s.Substring(p, q - p);
                    p = q;
                    // numeric local label reference: 1b / 1f
                    if (tok.Length >= 2 && (tok[tok.Length - 1] == 'b' || tok[tok.Length - 1] == 'f') && AllDigits(tok.Substring(0, tok.Length - 1)))
                    {
                        long lv;
                        if (a.TryLocal(tok.Substring(0, tok.Length - 1), tok[tok.Length - 1], out lv)) return lv;
                        if (!a.pass2) { Unknown = true; UnknownName = tok; return 0; }
                        throw new AsmException("Can't find local label " + tok + " (" + (tok.EndsWith("b") ? "no \"" + tok.Substring(0, tok.Length - 1) + ":\" before this line" : "no \"" + tok.Substring(0, tok.Length - 1) + ":\" after this line") + ")");
                    }
                    long n;
                    if (!ParseNumber(tok, out n)) throw new AsmException("\"" + tok + "\" is not a number");
                    return n;
                }
                if (IsIdentStart(c))
                {
                    int q = p;
                    while (q < s.Length && IsIdentChar(s[q])) q++;
                    string name = s.Substring(p, q - p);
                    p = q;
                    if (name == ".") return a.curPc;
                    long v;
                    if (a.equs.TryGetValue(name, out v)) return v;
                    if (a.TryLabel(name, out v)) return v;
                    if (!a.pass2) { Unknown = true; UnknownName = name; return 0; }
                    if (Regs.ContainsKey(name)) throw new AsmException("Expected a number or label here, but \"" + name + "\" is a register");
                    throw new AsmException("Unknown label or name \"" + name + "\"");
                }
                throw new AsmException("Unexpected \"" + c + "\" in \"" + s + "\"");
            }
        }

        static bool AllDigits(string s) { if (s.Length == 0) return false; foreach (char c in s) if (!char.IsDigit(c)) return false; return true; }

        public static bool ParseNumber(string tok, out long n)
        {
            n = 0;
            tok = tok.Replace("_", "");
            try
            {
                if (tok.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) { n = (long)ulong.Parse(tok.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture); return true; }
                if (tok.StartsWith("0b", StringComparison.OrdinalIgnoreCase)) { n = (long)Convert.ToUInt64(tok.Substring(2), 2); return true; }
                if (tok.Length > 1 && tok[0] == '0' && AllDigits(tok)) { n = (long)Convert.ToUInt64(tok.Substring(1), 8); return true; }
                return long.TryParse(tok, NumberStyles.None, CultureInfo.InvariantCulture, out n);
            }
            catch (Exception) { return false; }
        }

        static long SignExtend12(long v) { v &= 0xFFF; return v >= 0x800 ? v - 0x1000 : v; }

        /// <summary>Reads one (possibly escaped) character of a string or character literal at s[i].</summary>
        static int ReadChar(string s, ref int i)
        {
            if (i >= s.Length) throw new AsmException("Text ends too early");
            char c = s[i++];
            if (c != '\\') return c;
            if (i >= s.Length) throw new AsmException("A \\ at the end of the text");
            char e = s[i++];
            switch (e)
            {
                case 'n': return 10;
                case 'r': return 13;
                case 't': return 9;
                case '0': return 0;
                case 'a': return 7;
                case 'b': return 8;
                case 'f': return 12;
                case 'e': return 27;
                case 'v': return 11;
                case '\\': return '\\';
                case '\'': return '\'';
                case '"': return '"';
                case 'x':
                    {
                        int v = 0, k = 0;
                        while (k < 2 && i < s.Length && Uri.IsHexDigit(s[i])) { v = v * 16 + Convert.ToInt32(s[i].ToString(), 16); i++; k++; }
                        if (k == 0) throw new AsmException("\\x needs hex digits");
                        return v;
                    }
                default: throw new AsmException("Unknown escape \\" + e);
            }
        }

        long Eval(string text, out bool unknown)
        {
            var e = new Expr(this, text.Trim());
            long v = e.Parse();
            unknown = e.Unknown;
            return v;
        }
        long Eval(string text) { bool u; return Eval(text, out u); }

        static int Reg(string s)
        {
            int r;
            s = s.Trim();
            if (Regs.TryGetValue(s, out r)) return r;
            if (s.Length == 0) throw new AsmException("A register is missing");
            throw new AsmException("\"" + s + "\" is not a register (use x0-x31 or names like a0, t1, sp)");
        }

        uint Csr(string s)
        {
            uint c;
            s = s.Trim();
            if (Csrs.TryGetValue(s, out c)) return c;
            long v = Eval(s);
            if (v < 0 || v > 0xFFF) throw new AsmException("CSR number " + v + " is out of range (0-4095)");
            return (uint)v;
        }

        /// <summary>"off(reg)" -> offset expression and register. The offset may be empty or contain brackets itself (%lo(x)(a0)).</summary>
        static bool SplitMem(string op, out string off, out int reg)
        {
            off = null; reg = 0;
            op = op.Trim();
            if (!op.EndsWith(")")) return false;
            int depth = 0;
            for (int i = op.Length - 1; i >= 0; i--)
            {
                if (op[i] == ')') depth++;
                else if (op[i] == '(')
                {
                    depth--;
                    if (depth == 0)
                    {
                        string inner = op.Substring(i + 1, op.Length - i - 2).Trim();
                        if (!Regs.TryGetValue(inner, out reg)) return false;
                        off = op.Substring(0, i).Trim();
                        return true;
                    }
                }
            }
            return false;
        }

        // ------------------------------------------------------------------ encoding
        static uint R(uint f7, int rs2, int rs1, uint f3, int rd, uint op) { return (f7 << 25) | ((uint)rs2 << 20) | ((uint)rs1 << 15) | (f3 << 12) | ((uint)rd << 7) | op; }
        static uint I(long imm, int rs1, uint f3, int rd, uint op) { return ((uint)(imm & 0xFFF) << 20) | ((uint)rs1 << 15) | (f3 << 12) | ((uint)rd << 7) | op; }
        static uint S(long imm, int rs2, int rs1, uint f3, uint op)
        {
            uint u = (uint)(imm & 0xFFF);
            return ((u >> 5) << 25) | ((uint)rs2 << 20) | ((uint)rs1 << 15) | (f3 << 12) | ((u & 31) << 7) | op;
        }
        static uint B(long off, int rs2, int rs1, uint f3)
        {
            uint u = (uint)off;
            return (((u >> 12) & 1) << 31) | (((u >> 5) & 0x3F) << 25) | ((uint)rs2 << 20) | ((uint)rs1 << 15) | (f3 << 12) | (((u >> 1) & 0xF) << 8) | (((u >> 11) & 1) << 7) | 0x63;
        }
        static uint U(long imm20, int rd, uint op) { return ((uint)(imm20 & 0xFFFFF) << 12) | ((uint)rd << 7) | op; }
        static uint J(long off, int rd)
        {
            uint u = (uint)off;
            return (((u >> 20) & 1) << 31) | (((u >> 1) & 0x3FF) << 21) | (((u >> 11) & 1) << 20) | (((u >> 12) & 0xFF) << 12) | ((uint)rd << 7) | 0x6F;
        }

        static void CheckImm12(long v, string what)
        {
            if (v < -2048 || v > 2047) throw new AsmException(what + " " + v + " doesn't fit: it must be between -2048 and 2047");
        }

        // ------------------------------------------------------------------ assembling
        int section;                       // 0 = text, 1 = data
        uint[] sectionPc = new uint[2];
        List<byte>[] output;
        Dictionary<int, int> sizeHint;      // li: the size chosen in pass 1, per line index

        public AsmResult Assemble(string text, string fileName, uint origin)
        {
            res = new AsmResult { Origin = origin };
            lines = new List<Line>();
            macros = new Dictionary<string, Macro>();
            macroCounter = 0;
            labelDefs = new Dictionary<string, LabelDef>(StringComparer.Ordinal);
            equs = new Dictionary<string, long>(StringComparer.Ordinal);
            localDefs = new Dictionary<string, List<LabelDef>>();
            sizeHint = new Dictionary<int, int>();
            try { Preprocess(text ?? "", fileName ?? "", 0); }
            catch (AsmException e) { res.Errors.Add(new AsmError { File = fileName, Line = 0, Message = e.Message }); }
            if (res.Errors.Count > 0) return res;

            // pass 1: sizes and label positions
            pass2 = false;
            sectionBase[0] = origin; sectionBase[1] = 0;
            RunPass();
            uint textSize = sectionPc[0], dataSize = sectionPc[1];
            uint dataBase = origin + ((textSize + 15) & ~15u);
            bool pass1Errors = res.Errors.Count > 0;

            // pass 2: machine code (after errors in pass 1 it still runs, to find more problems in one go)
            pass2 = true;
            sectionBase[1] = dataBase;
            output = new[] { new List<byte>((int)textSize), new List<byte>((int)dataSize) };
            RunPass();   // .equ values from pass 1 stay, so a name can be used above its .equ line
            if (pass1Errors || res.Errors.Count > 0)
            {
                // in line order, file by file
                var order = new Dictionary<string, int>();
                foreach (var e in res.Errors) if (!order.ContainsKey(e.File ?? "")) order[e.File ?? ""] = order.Count;
                var sorted = new List<AsmError>(res.Errors);
                sorted.Sort((a, b) => { int c = order[a.File ?? ""].CompareTo(order[b.File ?? ""]); return c != 0 ? c : a.Line.CompareTo(b.Line); });
                res.Errors.Clear();
                res.Errors.AddRange(sorted);
                return res;
            }
            if (sectionPc[0] != textSize || sectionPc[1] != dataSize)
                res.Errors.Add(new AsmError { File = fileName, Line = 0, Message = "Internal: pass sizes differ (" + textSize + "/" + sectionPc[0] + ")" });

            var img = new byte[(dataSize > 0 ? dataBase - origin : textSize) + dataSize];
            output[0].CopyTo(img, 0);
            if (dataSize > 0) output[1].CopyTo(img, (int)(dataBase - origin));
            res.Image = img;
            res.TextSize = textSize; res.DataSize = dataSize; res.DataStart = dataBase;
            foreach (var kv in labelDefs) res.Symbols[kv.Key] = sectionBase[kv.Value.Section] + kv.Value.Offset;
            foreach (var kv in equs) if (!res.Symbols.ContainsKey(kv.Key)) res.Symbols[kv.Key] = (uint)kv.Value;
            return res;
        }

        void RunPass()
        {
            section = 0; sectionPc[0] = 0; sectionPc[1] = 0;
            for (int i = 0; i < lines.Count; i++)
            {
                curIndex = i;
                var l = lines[i];
                try { Statement(l, i); }
                catch (AsmException e) { Error(l, e.Message); }
                if (res.Errors.Count >= 100) return;
            }
        }

        void Emit(uint word)
        {
            if (pass2) { var o = output[section]; o.Add((byte)word); o.Add((byte)(word >> 8)); o.Add((byte)(word >> 16)); o.Add((byte)(word >> 24)); }
            sectionPc[section] += 4;
        }
        void EmitByte(long b)
        {
            if (pass2) output[section].Add((byte)b);
            sectionPc[section]++;
        }

        void Statement(Line l, int index)
        {
            string code = StripComment(l.Text);
            string rest;
            List<string> labels = SplitLabels(code, out rest);
            foreach (var lb in labels)
            {
                if (pass2) continue;
                var d = new LabelDef { Index = index, Section = section, Offset = sectionPc[section] };
                if (AllDigits(lb))
                {
                    List<LabelDef> list;
                    if (!localDefs.TryGetValue(lb, out list)) localDefs[lb] = list = new List<LabelDef>();
                    list.Add(d);
                }
                else if (labelDefs.ContainsKey(lb) || equs.ContainsKey(lb)) Error(l, "The label \"" + lb + "\" is defined twice");
                else if (Regs.ContainsKey(lb) || Mnemonics.Contains(lb)) Error(l, "\"" + lb + "\" can't be a label: it is a register or instruction name");
                else labelDefs[lb] = d;
            }
            rest = rest.Trim();
            if (rest.Length == 0) return;
            string op = FirstWord(rest);
            string args = rest.Substring(op.Length).Trim();
            curPc = sectionBase[section] + sectionPc[section];
            if (op.StartsWith(".")) { Directive(l, op.ToLowerInvariant(), args, index); return; }
            // "name = value" works like .equ
            if (args.StartsWith("=") && !args.StartsWith("=="))
            {
                Directive(l, ".equ", op + "," + args.Substring(1), index);
                return;
            }
            if ((sectionPc[section] & 3) != 0) throw new AsmException("This instruction is not on a 4-byte boundary: put \".align 2\" before it");
            uint startPc = curPc;
            if (pass2 && section == 0 && !res.Lines.ContainsKey(startPc)) res.Lines[startPc] = new SourceLine { File = l.File, Line = l.No, Text = l.Text.Trim() };
            Instruction(op.ToLowerInvariant(), args, index);
        }

        // ------------------------------------------------------------------ directives
        void Directive(Line l, string d, string args, int index)
        {
            switch (d)
            {
                case ".text": section = 0; return;
                case ".data": case ".rodata": case ".bss": section = 1; return;
                case ".section":
                    {
                        string n = FirstWord(args).ToLowerInvariant();
                        if (n.StartsWith(".text")) section = 0;
                        else if (n.StartsWith(".data") || n.StartsWith(".rodata") || n.StartsWith(".bss") || n.StartsWith(".sdata")) section = 1;
                        else throw new AsmException("Unknown section \"" + n + "\" (use .text or .data)");
                        return;
                    }
                case ".globl": case ".global": case ".local": case ".type": case ".size": case ".file": case ".option":
                case ".attribute": case ".ident": case ".p2align_ignored":
                    return;
                case ".equ": case ".set":
                    {
                        var parts = SplitOperands(args);
                        if (parts.Count != 2 || parts[0].Length == 0) throw new AsmException(d + " needs a name and a value, like: .equ SIZE, 64");
                        string name = parts[0];
                        if (!IsIdentStart(name[0])) throw new AsmException("\"" + name + "\" is not a valid name");
                        if (!pass2 && labelDefs.ContainsKey(name)) throw new AsmException("\"" + name + "\" is already a label");
                        bool unknown;
                        long v = Eval(parts[1], out unknown);
                        if (!unknown) equs[name] = v;
                        else if (pass2) throw new AsmException("Unknown name in " + parts[1]);
                        return;
                    }
                case ".byte": case ".half": case ".short": case ".2byte": case ".word": case ".long": case ".4byte":
                    {
                        int size = d == ".byte" ? 1 : (d == ".word" || d == ".long" || d == ".4byte") ? 4 : 2;
                        if (args.Length == 0) throw new AsmException(d + " needs at least one value");
                        foreach (var p in SplitOperands(args))
                        {
                            if (p.Length == 0) throw new AsmException("An empty value in " + d);
                            long v = 0;
                            if (pass2)
                            {
                                v = Eval(p);
                                long lo = size == 4 ? int.MinValue : size == 2 ? short.MinValue : sbyte.MinValue;
                                long hi = size == 4 ? uint.MaxValue : size == 2 ? ushort.MaxValue : byte.MaxValue;
                                if (v < lo || v > hi) throw new AsmException("The value " + v + " doesn't fit in " + (size == 1 ? "a byte" : size == 2 ? "a half-word (2 bytes)" : "a word (4 bytes)"));
                            }
                            for (int i = 0; i < size; i++) EmitByte(v >> (8 * i));
                        }
                        return;
                    }
                case ".ascii": case ".asciz": case ".string":
                    {
                        foreach (var p in SplitOperands(args))
                        {
                            string s = p.Trim();
                            if (s.Length < 2 || s[0] != '"' || s[s.Length - 1] != '"') throw new AsmException(d + " needs text in double quotes, like: " + d + " \"Hello\"");
                            int i = 1;
                            while (i < s.Length - 1)
                            {
                                if (s[i] == '"') throw new AsmException("A \" inside the text must be written as \\\"");
                                int c = ReadChar(s, ref i);
                                if (c > 255) c = '?';
                                EmitByte(c);
                            }
                            if (d != ".ascii") EmitByte(0);
                        }
                        return;
                    }
                case ".align": case ".p2align": case ".balign":
                    {
                        var parts = SplitOperands(args);
                        long n = Eval(parts[0]);
                        long bytes = d == ".balign" ? n : (1L << (int)Math.Min(Math.Max(n, 0), 12));
                        if (bytes <= 0 || (bytes & (bytes - 1)) != 0 || bytes > 4096) throw new AsmException(d + ": the alignment must be a power of two up to 4096");
                        long fill = parts.Count > 1 ? Eval(parts[1]) : 0;
                        while ((sectionPc[section] & (bytes - 1)) != 0)
                        {
                            // in code, pad with nop instructions when possible
                            if (section == 0 && fill == 0 && (sectionPc[section] & 3) == 0 && bytes >= 4) Emit(0x00000013);
                            else EmitByte(fill);
                        }
                        return;
                    }
                case ".space": case ".zero": case ".skip":
                    {
                        var parts = SplitOperands(args);
                        bool unknown;
                        long n = Eval(parts[0], out unknown);
                        if (unknown) throw new AsmException(d + " needs a size that is known here (numbers or names defined above)");
                        if (n < 0 || n > 16 * 1024 * 1024) throw new AsmException(d + " size " + n + " is too big");
                        long fill = parts.Count > 1 ? Eval(parts[1]) : 0;
                        for (long i = 0; i < n; i++) EmitByte(fill);
                        return;
                    }
                case ".include":
                case ".macro":
                case ".endm":
                    throw new AsmException(d + " is not allowed here");
                default:
                    throw new AsmException("Unknown directive \"" + d + "\"");
            }
        }

        // ------------------------------------------------------------------ instructions
        static void Need(List<string> ops, int n, string op, string form)
        {
            if (ops.Count != n || (n > 0 && ops[n - 1].Length == 0))
                throw new AsmException("\"" + op + "\" needs " + n + " operand" + (n == 1 ? "" : "s") + " (" + form + ") but got " + (ops.Count == 1 && ops[0].Length == 0 ? 0 : ops.Count));
        }

        long Target(string expr, out bool unknown)
        {
            long t = Eval(expr, out unknown);
            return t;
        }

        void Branch(uint f3, int rs1, int rs2, string target)
        {
            bool unknown;
            long t = Target(target, out unknown);
            long off = (long)(uint)t - (long)curPc;
            if (pass2)
            {
                if ((off & 1) != 0) throw new AsmException("Branch target " + target + " is not on an even address");
                if (off < -4096 || off > 4094) throw new AsmException("Branch target " + target + " is too far away (" + off + " bytes; branches reach about 4 KB). Use a jump instead");
            }
            Emit(B(off, rs2, rs1, f3));
        }

        void Jal(int rd, string target)
        {
            bool unknown;
            long t = Target(target, out unknown);
            long off = (long)(uint)t - (long)curPc;
            if (pass2 && (off < -(1 << 20) || off >= (1 << 20))) throw new AsmException("Jump target " + target + " is too far away (jal reaches 1 MB)");
            Emit(J(off, rd));
        }

        static readonly Dictionary<string, uint> LoadF3 = new Dictionary<string, uint> { { "lb", 0 }, { "lh", 1 }, { "lw", 2 }, { "lbu", 4 }, { "lhu", 5 } };
        static readonly Dictionary<string, uint> StoreF3 = new Dictionary<string, uint> { { "sb", 0 }, { "sh", 1 }, { "sw", 2 } };
        static readonly Dictionary<string, uint> BranchF3 = new Dictionary<string, uint> { { "beq", 0 }, { "bne", 1 }, { "blt", 4 }, { "bge", 5 }, { "bltu", 6 }, { "bgeu", 7 } };
        static readonly Dictionary<string, uint> ImmF3 = new Dictionary<string, uint> { { "addi", 0 }, { "slti", 2 }, { "sltiu", 3 }, { "xori", 4 }, { "ori", 6 }, { "andi", 7 } };
        static readonly Dictionary<string, uint[]> RegOps = new Dictionary<string, uint[]>
        {
            { "add", new uint[] { 0, 0 } }, { "sub", new uint[] { 0x20, 0 } }, { "sll", new uint[] { 0, 1 } }, { "slt", new uint[] { 0, 2 } },
            { "sltu", new uint[] { 0, 3 } }, { "xor", new uint[] { 0, 4 } }, { "srl", new uint[] { 0, 5 } }, { "sra", new uint[] { 0x20, 5 } },
            { "or", new uint[] { 0, 6 } }, { "and", new uint[] { 0, 7 } },
            { "mul", new uint[] { 1, 0 } }, { "mulh", new uint[] { 1, 1 } }, { "mulhsu", new uint[] { 1, 2 } }, { "mulhu", new uint[] { 1, 3 } },
            { "div", new uint[] { 1, 4 } }, { "divu", new uint[] { 1, 5 } }, { "rem", new uint[] { 1, 6 } }, { "remu", new uint[] { 1, 7 } },
        };
        static readonly Dictionary<string, uint> CsrF3 = new Dictionary<string, uint> { { "csrrw", 1 }, { "csrrs", 2 }, { "csrrc", 3 }, { "csrrwi", 5 }, { "csrrsi", 6 }, { "csrrci", 7 } };

        void Li(int rd, string expr, int index)
        {
            bool unknown;
            long v = Eval(expr, out unknown);
            if (!unknown && (v < int.MinValue || v > uint.MaxValue)) throw new AsmException("The number " + v + " doesn't fit in 32 bits");
            int sv = (int)(uint)v;
            int size;
            if (!pass2)
            {
                if (unknown) size = 8;
                else if (sv >= -2048 && sv <= 2047) size = 4;
                else if ((sv & 0xFFF) == 0) size = 4;
                else size = 8;
                sizeHint[index] = size;
            }
            else if (!sizeHint.TryGetValue(index, out size)) size = 8;   // (only after an error on this line in pass 1)
            if (size == 4)
            {
                if (sv >= -2048 && sv <= 2047) Emit(I(sv, 0, 0, rd, 0x13));
                else if ((sv & 0xFFF) == 0) Emit(U((uint)sv >> 12, rd, 0x37));
                else throw new AsmException("Internal: li size changed between passes for " + expr);
            }
            else
            {
                long hi = (((long)(uint)sv + 0x800) >> 12) & 0xFFFFF;
                long lo = SignExtend12(sv);
                Emit(U(hi, rd, 0x37));
                Emit(I(lo, rd, 0, rd, 0x13));
            }
        }

        /// <summary>auipc + (addi / load / store) with a pc-relative address.</summary>
        void PcRel(int rdAuipc, string expr, Func<long, uint> second)
        {
            bool unknown;
            long t = Eval(expr, out unknown);
            long off = (long)(uint)t - (long)curPc;
            long hi = ((off + 0x800) >> 12) & 0xFFFFF;
            long lo = SignExtend12(off);
            Emit(U(hi, rdAuipc, 0x17));
            curPc += 4;
            Emit(second(lo));
        }

        void Instruction(string op, string args, int index)
        {
            List<string> o = args.Length == 0 ? new List<string>() : SplitOperands(args);
            uint f3;
            uint[] rf;
            if (RegOps.TryGetValue(op, out rf))
            {
                Need(o, 3, op, "rd, rs1, rs2");
                Emit(R(rf[0], Reg(o[2]), Reg(o[1]), rf[1], Reg(o[0]), 0x33));
                return;
            }
            if (ImmF3.TryGetValue(op, out f3))
            {
                Need(o, 3, op, "rd, rs1, number");
                long imm = 0;
                bool unknown = false;
                imm = Eval(o[2], out unknown);
                if (pass2) CheckImm12(imm, "The number");
                Emit(I(imm, Reg(o[1]), f3, Reg(o[0]), 0x13));
                return;
            }
            if (BranchF3.TryGetValue(op, out f3))
            {
                Need(o, 3, op, "rs1, rs2, label");
                Branch(f3, Reg(o[0]), Reg(o[1]), o[2]);
                return;
            }
            if (LoadF3.TryGetValue(op, out f3))
            {
                Need(o, 2, op, "rd, offset(rs1)");
                int rd = Reg(o[0]);
                string off; int rs1;
                if (SplitMem(o[1], out off, out rs1))
                {
                    long imm = off.Length == 0 ? 0 : Eval(off);
                    if (pass2) CheckImm12(imm, "The offset");
                    Emit(I(imm, rs1, f3, rd, 0x03));
                }
                else PcRel(rd, o[1], lo => I(lo, rd, f3, rd, 0x03));   // lw rd, label
                return;
            }
            if (StoreF3.TryGetValue(op, out f3))
            {
                if (o.Count == 3)
                {
                    // sw rs2, label, temp
                    int rs2v = Reg(o[0]), tmp = Reg(o[2]);
                    PcRel(tmp, o[1], lo => S(lo, rs2v, tmp, f3, 0x23));
                    return;
                }
                Need(o, 2, op, "rs2, offset(rs1)");
                string off; int rs1;
                if (!SplitMem(o[1], out off, out rs1)) throw new AsmException("\"" + op + "\" needs an address like 8(sp) or (a0)");
                long imm = off.Length == 0 ? 0 : Eval(off);
                if (pass2) CheckImm12(imm, "The offset");
                Emit(S(imm, Reg(o[0]), rs1, f3, 0x23));
                return;
            }
            if (CsrF3.TryGetValue(op, out f3))
            {
                Need(o, 3, op, f3 >= 5 ? "rd, csr, number 0-31" : "rd, csr, rs1");
                int rs1;
                if (f3 >= 5)
                {
                    long u = Eval(o[2]);
                    if (pass2 && (u < 0 || u > 31)) throw new AsmException("The value for " + op + " must be 0 to 31");
                    rs1 = (int)(u & 31);
                }
                else rs1 = Reg(o[2]);
                Emit(I(Csr(o[1]), rs1, f3, Reg(o[0]), 0x73));
                return;
            }
            switch (op)
            {
                case "lui":
                case "auipc":
                    {
                        Need(o, 2, op, "rd, number 0-1048575");
                        long v = Eval(o[1]);
                        if (pass2 && (v < -524288 || v > 0xFFFFF)) throw new AsmException("The number for " + op + " must be 0 to 1048575 (20 bits)");
                        Emit(U(v, Reg(o[0]), op == "lui" ? 0x37u : 0x17u));
                        return;
                    }
                case "slli": case "srli": case "srai":
                    {
                        Need(o, 3, op, "rd, rs1, shift 0-31");
                        long sh = Eval(o[2]);
                        if (pass2 && (sh < 0 || sh > 31)) throw new AsmException("A shift amount must be 0 to 31");
                        uint fn = op == "slli" ? 1u : 5u;
                        uint hi = op == "srai" ? 0x400u : 0u;
                        Emit(I((long)(hi | (uint)(sh & 31)), Reg(o[1]), fn, Reg(o[0]), 0x13));
                        return;
                    }
                case "jal":
                    if (o.Count == 1) { Jal(1, o[0]); return; }
                    Need(o, 2, op, "rd, label");
                    Jal(Reg(o[0]), o[1]);
                    return;
                case "jalr":
                    {
                        if (o.Count == 1)
                        {
                            string off1; int r1;
                            if (SplitMem(o[0], out off1, out r1)) Emit(I(off1.Length == 0 ? 0 : Eval(off1), r1, 0, 1, 0x67));
                            else Emit(I(0, Reg(o[0]), 0, 1, 0x67));
                            return;
                        }
                        if (o.Count == 2)
                        {
                            string off2; int r2;
                            if (SplitMem(o[1], out off2, out r2)) { long im = off2.Length == 0 ? 0 : Eval(off2); if (pass2) CheckImm12(im, "The offset"); Emit(I(im, r2, 0, Reg(o[0]), 0x67)); }
                            else Emit(I(0, Reg(o[1]), 0, Reg(o[0]), 0x67));
                            return;
                        }
                        Need(o, 3, op, "rd, rs1, offset");
                        long imm = Eval(o[2]);
                        if (pass2) CheckImm12(imm, "The offset");
                        Emit(I(imm, Reg(o[1]), 0, Reg(o[0]), 0x67));
                        return;
                    }
                case "ecall": Need(o, 0, op, "nothing"); Emit(0x00000073); return;
                case "ebreak": Need(o, 0, op, "nothing"); Emit(0x00100073); return;
                case "mret": Need(o, 0, op, "nothing"); Emit(0x30200073); return;
                case "wfi": Need(o, 0, op, "nothing"); Emit(0x10500073); return;
                case "fence.i": Emit(0x0000100F); return;
                case "fence":
                    {
                        if (o.Count == 0 || (o.Count == 1 && o[0].Length == 0)) { Emit(0x0FF0000F); return; }
                        Need(o, 2, op, "pred, succ like iorw, iorw");
                        Emit((FenceBits(o[0]) << 24) | (FenceBits(o[1]) << 20) | 0x0F);
                        return;
                    }
                // ---------------- pseudo-instructions
                case "nop": Need(o, 0, op, "nothing"); Emit(0x00000013); return;
                case "li": Need(o, 2, op, "rd, number"); Li(Reg(o[0]), o[1], index); return;
                case "la": case "lla":
                    {
                        Need(o, 2, op, "rd, label");
                        int rd = Reg(o[0]);
                        PcRel(rd, o[1], lo => I(lo, rd, 0, rd, 0x13));
                        return;
                    }
                case "mv": Need(o, 2, op, "rd, rs"); Emit(I(0, Reg(o[1]), 0, Reg(o[0]), 0x13)); return;
                case "not": Need(o, 2, op, "rd, rs"); Emit(I(-1, Reg(o[1]), 4, Reg(o[0]), 0x13)); return;
                case "neg": Need(o, 2, op, "rd, rs"); Emit(R(0x20, Reg(o[1]), 0, 0, Reg(o[0]), 0x33)); return;
                case "seqz": Need(o, 2, op, "rd, rs"); Emit(I(1, Reg(o[1]), 3, Reg(o[0]), 0x13)); return;
                case "snez": Need(o, 2, op, "rd, rs"); Emit(R(0, Reg(o[1]), 0, 3, Reg(o[0]), 0x33)); return;
                case "sltz": Need(o, 2, op, "rd, rs"); Emit(R(0, 0, Reg(o[1]), 2, Reg(o[0]), 0x33)); return;
                case "sgtz": Need(o, 2, op, "rd, rs"); Emit(R(0, Reg(o[1]), 0, 2, Reg(o[0]), 0x33)); return;
                case "beqz": Need(o, 2, op, "rs, label"); Branch(0, Reg(o[0]), 0, o[1]); return;
                case "bnez": Need(o, 2, op, "rs, label"); Branch(1, Reg(o[0]), 0, o[1]); return;
                case "blez": Need(o, 2, op, "rs, label"); Branch(5, 0, Reg(o[0]), o[1]); return;
                case "bgez": Need(o, 2, op, "rs, label"); Branch(5, Reg(o[0]), 0, o[1]); return;
                case "bltz": Need(o, 2, op, "rs, label"); Branch(4, Reg(o[0]), 0, o[1]); return;
                case "bgtz": Need(o, 2, op, "rs, label"); Branch(4, 0, Reg(o[0]), o[1]); return;
                case "bgt": Need(o, 3, op, "rs1, rs2, label"); Branch(4, Reg(o[1]), Reg(o[0]), o[2]); return;
                case "ble": Need(o, 3, op, "rs1, rs2, label"); Branch(5, Reg(o[1]), Reg(o[0]), o[2]); return;
                case "bgtu": Need(o, 3, op, "rs1, rs2, label"); Branch(6, Reg(o[1]), Reg(o[0]), o[2]); return;
                case "bleu": Need(o, 3, op, "rs1, rs2, label"); Branch(7, Reg(o[1]), Reg(o[0]), o[2]); return;
                case "j": Need(o, 1, op, "label"); Jal(0, o[0]); return;
                case "jr": Need(o, 1, op, "rs"); Emit(I(0, Reg(o[0]), 0, 0, 0x67)); return;
                case "ret": Need(o, 0, op, "nothing"); Emit(0x00008067); return;
                case "call": Need(o, 1, op, "label"); Jal(1, o[0]); return;
                case "tail": Need(o, 1, op, "label"); Jal(0, o[0]); return;
                case "csrr": Need(o, 2, op, "rd, csr"); Emit(I(Csr(o[1]), 0, 2, Reg(o[0]), 0x73)); return;
                case "csrw": Need(o, 2, op, "csr, rs"); Emit(I(Csr(o[0]), Reg(o[1]), 1, 0, 0x73)); return;
                case "csrs": Need(o, 2, op, "csr, rs"); Emit(I(Csr(o[0]), Reg(o[1]), 2, 0, 0x73)); return;
                case "csrc": Need(o, 2, op, "csr, rs"); Emit(I(Csr(o[0]), Reg(o[1]), 3, 0, 0x73)); return;
                case "csrwi": case "csrsi": case "csrci":
                    {
                        Need(o, 2, op, "csr, number 0-31");
                        long u = Eval(o[1]);
                        if (pass2 && (u < 0 || u > 31)) throw new AsmException("The value for " + op + " must be 0 to 31");
                        uint fn = op == "csrwi" ? 5u : op == "csrsi" ? 6u : 7u;
                        Emit(I(Csr(o[0]), (int)(u & 31), fn, 0, 0x73));
                        return;
                    }
                case "rdcycle": Need(o, 1, op, "rd"); Emit(I(0xC00, 0, 2, Reg(o[0]), 0x73)); return;
                case "rdcycleh": Need(o, 1, op, "rd"); Emit(I(0xC80, 0, 2, Reg(o[0]), 0x73)); return;
                case "rdtime": Need(o, 1, op, "rd"); Emit(I(0xC01, 0, 2, Reg(o[0]), 0x73)); return;
                case "rdtimeh": Need(o, 1, op, "rd"); Emit(I(0xC81, 0, 2, Reg(o[0]), 0x73)); return;
                case "rdinstret": Need(o, 1, op, "rd"); Emit(I(0xC02, 0, 2, Reg(o[0]), 0x73)); return;
                case "rdinstreth": Need(o, 1, op, "rd"); Emit(I(0xC82, 0, 2, Reg(o[0]), 0x73)); return;
            }
            string hint = Suggest(op);
            throw new AsmException("Unknown instruction \"" + op + "\"" + (hint != null ? " (did you mean \"" + hint + "\"?)" : ""));
        }

        static uint FenceBits(string s)
        {
            uint v = 0;
            foreach (char c in s.Trim().ToLowerInvariant())
            {
                if (c == 'i') v |= 8; else if (c == 'o') v |= 4; else if (c == 'r') v |= 2; else if (c == 'w') v |= 1;
                else throw new AsmException("fence takes letters i, o, r, w");
            }
            return v;
        }

        static string Suggest(string op)
        {
            string best = null; int bestD = 3;
            var names = new List<string>(Mnemonics);
            names.Sort(StringComparer.Ordinal);
            foreach (var m in names)
            {
                int d = Distance(op, m);
                if (d < bestD) { bestD = d; best = m; }
            }
            return best;
        }
        static int Distance(string a, string b)
        {
            var d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
                for (int j = 1; j <= b.Length; j++)
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            return d[a.Length, b.Length];
        }
    }
}
