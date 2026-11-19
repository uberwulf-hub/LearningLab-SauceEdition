// Cert Lab - the GRUB boot menu as it looks on a RHEL 9 console: a full screen of 80 x 25 characters with the boot
// entries in a box, a countdown that any key stops, the entry editor (a small text editor with a cursor: arrows,
// Home / End, Ctrl+A / Ctrl+E, Backspace / Delete, long lines wrapped with a \ at the edge; Ctrl+X or F10 boots
// the edited commands, Esc throws the changes away) and a minimal command line (c, or Ctrl+C in the editor).
// Keys act at once, as on the real menu: there is no line to type and no Enter after e.
// The screen is a model only: the console's worker thread waits while the window (or a test, or RhelSim.Type)
// sends keys from the caller's thread through Session.GrubKey / GrubType / GrubTick, so it stays deterministic.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CertLab
{
    public class GrubScreen
    {
        public const int W = 80, H = 25, BoxTop = 3, BoxRows = 12, Inner = 76, Wrap = 75;
        static readonly char TL = (char)0x250C, TR = (char)0x2510, BL = (char)0x2514, BR = (char)0x2518, HZ = (char)0x2500, VT = (char)0x2502, ArUp = (char)0x2191, ArDown = (char)0x2193;

        readonly Machine M;
        public readonly List<BootEntry> Entries;
        public int Sel, MenuTop;
        /// <summary>menu, edit, cmd or message.</summary>
        public string Mode = "menu";
        /// <summary>Seconds left before the highlighted entry starts by itself; -1 once a key stopped it.</summary>
        public int Seconds = -1;
        public bool Done;
        /// <summary>What happens when Done: boot, reboot or halt.</summary>
        public string Action = "";
        public string BootKernel, BootArgs;
        public bool BootRescue;
        // the editor
        public List<string> Lines = new List<string>();
        public int Row, Col, EditTop;
        public int EditedEntry = -1;
        string killed = "";
        // the command line
        public readonly List<string> CmdOut = new List<string>();
        public string CmdLine = "";
        int cmdCaret;
        string cmdBack = "menu", cmdKernel, cmdArgs;
        // a message ("Press any key to continue...")
        string msg = "", msgBack = "edit";
        // what Rows() worked out for the window
        public int HighlightRow = -1, CursorRow = -1, CursorCol = -1, HintRow = -1;

        public GrubScreen(Machine m)
        {
            M = m;
            Entries = m.BootEntries();
            Sel = Math.Max(0, Math.Min(Entries.Count - 1, m.DefaultEntryIndex()));
            Seconds = m.MenuTimeout();
            Rows();
        }

        public bool TimerRunning { get { return Mode == "menu" && Seconds > 0; } }

        // ------------------------------------------------------------ input
        /// <summary>One second passed (the window's clock): the countdown, then the highlighted entry starts.</summary>
        public void Tick()
        {
            if (Done || !TimerRunning) return;
            Seconds--;
            if (Seconds == 0) BootEntry(Sel);
        }

        /// <summary>Printable characters, as typed.</summary>
        public void Type(string text)
        {
            if (Done || string.IsNullOrEmpty(text)) return;
            Seconds = -1;
            foreach (char ch in text)
            {
                if (ch < 32 || ch == 127 || Done) continue;
                switch (Mode)
                {
                    case "menu":
                        if (ch == 'e') OpenEditor();
                        else if (ch == 'c') OpenCmd("menu");
                        break;
                    case "edit":
                        Lines[Row] = Lines[Row].Insert(Col, ch.ToString()); Col++;
                        break;
                    case "cmd":
                        CmdLine = CmdLine.Insert(cmdCaret, ch.ToString()); cmdCaret++;
                        break;
                    case "message":
                        Mode = msgBack;
                        break;
                }
            }
            Rows();
        }

        /// <summary>A key by name: Up, Down, Left, Right, Home, End, PageUp, PageDown, Enter, Esc, Backspace, Delete,
        /// Tab, F2, F10, Ctrl+A ... Ctrl+Z. Returns false for keys that do nothing here.</summary>
        public bool Key(string k)
        {
            if (Done || k == null) return false;
            Seconds = -1;
            bool r = false;
            switch (Mode)
            {
                case "menu": r = MenuKey(k); break;
                case "edit": r = EditKey(k); break;
                case "cmd": r = CmdKey(k); break;
                case "message": Mode = msgBack; r = true; break;
            }
            Rows();   // the cursor, highlight and scrolling follow at once
            return r;
        }

        bool MenuKey(string k)
        {
            switch (k)
            {
                case "Up": case "Ctrl+P": if (Sel > 0) Sel--; break;
                case "Down": case "Ctrl+N": if (Sel < Entries.Count - 1) Sel++; break;
                case "Home": case "PageUp": Sel = 0; break;
                case "End": case "PageDown": Sel = Entries.Count - 1; break;
                case "Enter": case "Right": case "Ctrl+F": case "Ctrl+X": case "F10": BootEntry(Sel); break;
                default: return false;
            }
            if (Sel < MenuTop) MenuTop = Sel;
            if (Sel >= MenuTop + BoxRows) MenuTop = Sel - BoxRows + 1;
            return true;
        }

        void OpenEditor()
        {
            var en = Entries[Sel];
            EditedEntry = Sel;
            Lines = new List<string>
            {
                "load_video",
                "set gfxpayload=keep",
                "insmod gzio",
                "linux ($root)/vmlinuz-" + en.Kernel + " " + en.Args,
                "initrd ($root)/initramfs-" + en.Kernel + ".img $tuned_initrd"
            };
            Row = 0; Col = 0; EditTop = 0;
            Mode = "edit";
        }

        bool EditKey(string k)
        {
            string l = Lines[Row];
            switch (k)
            {
                case "Left": case "Ctrl+B":
                    if (Col > 0) Col--; else if (Row > 0) { Row--; Col = Lines[Row].Length; }
                    break;
                case "Right": case "Ctrl+F":
                    if (Col < l.Length) Col++; else if (Row < Lines.Count - 1) { Row++; Col = 0; }
                    break;
                case "Up": case "Ctrl+P": if (Row > 0) { Row--; Col = Math.Min(Col, Lines[Row].Length); } break;
                case "Down": case "Ctrl+N": if (Row < Lines.Count - 1) { Row++; Col = Math.Min(Col, Lines[Row].Length); } break;
                case "Home": case "Ctrl+A": Col = 0; break;
                case "End": case "Ctrl+E": Col = l.Length; break;
                case "Backspace": case "Ctrl+H":
                    if (Col > 0) { Lines[Row] = l.Remove(Col - 1, 1); Col--; }
                    else if (Row > 0) { Col = Lines[Row - 1].Length; Lines[Row - 1] += l; Lines.RemoveAt(Row); Row--; }
                    break;
                case "Delete": case "Ctrl+D":
                    if (Col < l.Length) Lines[Row] = l.Remove(Col, 1);
                    else if (Row < Lines.Count - 1) { Lines[Row] = l + Lines[Row + 1]; Lines.RemoveAt(Row + 1); }
                    break;
                case "Enter": case "Ctrl+J": case "Ctrl+M":
                    Lines[Row] = l.Substring(0, Col); Lines.Insert(Row + 1, l.Substring(Col)); Row++; Col = 0;
                    break;
                case "Ctrl+O":
                    Lines[Row] = l.Substring(0, Col); Lines.Insert(Row + 1, l.Substring(Col));
                    break;
                case "Ctrl+K":
                    if (Col < l.Length) { killed = l.Substring(Col); Lines[Row] = l.Substring(0, Col); }
                    else if (Row < Lines.Count - 1) { killed = "\n"; Lines[Row] = l + Lines[Row + 1]; Lines.RemoveAt(Row + 1); }
                    break;
                case "Ctrl+Y":
                    if (killed == "\n") { Lines[Row] = l.Substring(0, Col); Lines.Insert(Row + 1, l.Substring(Col)); Row++; Col = 0; }
                    else if (killed.Length > 0) { Lines[Row] = l.Insert(Col, killed); Col += killed.Length; }
                    break;
                case "Ctrl+X": case "F10": BootEdited(); break;
                case "Ctrl+C": case "F2": OpenCmd("edit"); break;
                case "Esc": Mode = "menu"; Lines = new List<string>(); EditedEntry = -1; break;
                case "Tab": case "Ctrl+L": break;
                default: return false;
            }
            return true;
        }

        void OpenCmd(string back)
        {
            cmdBack = back; Mode = "cmd"; CmdLine = ""; cmdCaret = 0;
            CmdOut.Clear();
        }

        bool CmdKey(string k)
        {
            switch (k)
            {
                case "Esc": Mode = cmdBack; break;
                case "Left": case "Ctrl+B": if (cmdCaret > 0) cmdCaret--; break;
                case "Right": case "Ctrl+F": if (cmdCaret < CmdLine.Length) cmdCaret++; break;
                case "Home": case "Ctrl+A": cmdCaret = 0; break;
                case "End": case "Ctrl+E": cmdCaret = CmdLine.Length; break;
                case "Backspace": case "Ctrl+H": if (cmdCaret > 0) { CmdLine = CmdLine.Remove(cmdCaret - 1, 1); cmdCaret--; } break;
                case "Delete": case "Ctrl+D": if (cmdCaret < CmdLine.Length) CmdLine = CmdLine.Remove(cmdCaret, 1); break;
                case "Ctrl+K": CmdLine = CmdLine.Substring(0, cmdCaret); break;
                case "Ctrl+U": CmdLine = CmdLine.Substring(cmdCaret); cmdCaret = 0; break;
                case "Enter": case "Ctrl+J": case "Ctrl+M":
                    {
                        string line = CmdLine; CmdLine = ""; cmdCaret = 0;
                        CmdOut.Add("grub> " + line);
                        RunCmd(line.Trim());
                        break;
                    }
                case "Tab": break;
                default: return false;
            }
            return true;
        }

        void RunCmd(string line)
        {
            if (line.Length == 0) return;
            var a = Regex.Split(line, "\\s+");
            switch (a[0])
            {
                case "help":
                    CmdOut.Add("Commands in Cert Lab's GRUB: boot, echo, halt, help, initrd, linux, ls, normal, reboot.");
                    CmdOut.Add("Esc goes back.");
                    break;
                case "ls":
                    if (a.Length == 1) CmdOut.Add("(hd0) (hd0,msdos2) (hd0,msdos1)");
                    else CmdOut.Add("error: file `" + a[1] + "' not found.");
                    break;
                case "echo": CmdOut.Add(line.Length > 5 ? line.Substring(5) : ""); break;
                case "normal": case "exit": Mode = "menu"; break;
                case "reboot": Action = "reboot"; Done = true; break;
                case "halt": Action = "halt"; Done = true; break;
                case "linux": case "linux16": case "linuxefi":
                    {
                        string err; string k = KernelOf(a.Length > 1 ? a[1] : null, out err);
                        if (k == null) { CmdOut.Add(err); cmdKernel = null; break; }
                        cmdKernel = k; cmdArgs = string.Join(" ", a.Skip(2).ToArray());
                        break;
                    }
                case "initrd": case "initrd16": case "initrdefi":
                    if (cmdKernel == null) CmdOut.Add("error: you need to load the kernel first.");
                    break;
                case "boot":
                    if (cmdKernel == null) { CmdOut.Add("error: you need to load the kernel first."); break; }
                    Boot(cmdKernel, cmdArgs);
                    break;
                default: CmdOut.Add("error: can't find command `" + a[0] + "'."); break;
            }
            while (CmdOut.Count > 200) CmdOut.RemoveAt(0);
        }

        // ------------------------------------------------------------ booting
        void BootEntry(int i)
        {
            var en = Entries[i];
            BootKernel = en.Kernel; BootArgs = en.Args; BootRescue = en.Rescue;
            Action = "boot"; Done = true;
        }

        /// <summary>Ctrl+X in the editor: runs the commands as they are now. The linux line decides the kernel and
        /// its arguments, word for word.</summary>
        void BootEdited()
        {
            string linux = null;
            foreach (var raw in Lines)
            {
                var t = raw.Trim();
                var w = t.Length == 0 ? "" : Regex.Split(t, "\\s+")[0];
                if (w == "linux" || w == "linux16" || w == "linuxefi") linux = t;
            }
            if (linux == null) { Message("error: you need to load the kernel first."); return; }
            var a = Regex.Split(linux, "\\s+");
            string err;
            string k = KernelOf(a.Length > 1 ? a[1] : null, out err);
            if (k == null) { Message(err); return; }
            Boot(k, string.Join(" ", a.Skip(2).ToArray()));
        }

        void Boot(string kernel, string args)
        {
            BootKernel = kernel; BootArgs = args;
            BootRescue = kernel.Contains("rescue");
            Action = "boot"; Done = true;
        }

        /// <summary>The kernel version from "($root)/vmlinuz-VERSION", or null with GRUB's error.</summary>
        string KernelOf(string path, out string err)
        {
            err = null;
            if (string.IsNullOrEmpty(path)) { err = "error: filename expected."; return null; }
            string p = path;
            if (p.StartsWith("($root)", StringComparison.Ordinal)) p = p.Substring(7);
            else if (p.StartsWith("(hd0,msdos1)", StringComparison.Ordinal)) p = p.Substring(12);
            else if (p.StartsWith("(", StringComparison.Ordinal)) { err = "error: disk `" + p.Substring(1, Math.Max(0, p.IndexOf(')') - 1)) + "' not found."; return null; }
            if (p.StartsWith("/vmlinuz-", StringComparison.Ordinal))
            {
                string k = p.Substring(9);
                if (Entries.Any(e => e.Kernel == k) || Machine.Kernels.Contains(k)) return k;
            }
            err = "error: file `" + p + "' not found.";
            return null;
        }

        void Message(string text) { msg = text; msgBack = Mode; Mode = "message"; }

        // ------------------------------------------------------------ the screen
        /// <summary>The 25 rows of 80 characters on the screen now (also sets HighlightRow, CursorRow / Col, HintRow).</summary>
        public string[] Rows()
        {
            var r = new char[H][];
            for (int i = 0; i < H; i++) { r[i] = new char[W]; for (int j = 0; j < W; j++) r[i][j] = ' '; }
            HighlightRow = -1; CursorRow = -1; CursorCol = -1; HintRow = -1;
            Action<int, int, string> put = (row, col, s) => { for (int j = 0; j < s.Length && col + j < W; j++) if (col + j >= 0) r[row][col + j] = s[j]; };
            if (Mode == "cmd")
            {
                put(0, 1, "A small GRUB command line: type a command and press Enter (help lists them).");
                put(1, 1, "Esc goes back" + (cmdBack == "edit" ? " to the editor." : " to the menu."));
                var all = new List<string>(CmdOut);
                string pr = "grub> " + CmdLine;
                var shown = all.Skip(Math.Max(0, all.Count - (H - 6))).ToList();
                int row = 3;
                foreach (var s in shown) put(row++, 0, s);
                put(row, 0, pr);
                CursorRow = row; CursorCol = Math.Min(W - 1, 6 + cmdCaret);
                HintRow = H - 1;
                put(HintRow, 0, "Cert Lab: Esc goes back.");
                return r.Select(x => new string(x)).ToArray();
            }
            if (Mode == "message")
            {
                put(1, 0, msg);
                put(3, 0, "Press any key to continue...");
                return r.Select(x => new string(x)).ToArray();
            }
            string head = "GRUB  version 2.06";
            put(1, (W - head.Length) / 2, head);
            put(BoxTop, 1, TL + new string(HZ, Inner) + TR);
            for (int i = 1; i <= BoxRows; i++) { r[BoxTop + i][1] = VT; r[BoxTop + i][W - 2] = VT; }
            put(BoxTop + BoxRows + 1, 1, BL + new string(HZ, Inner) + BR);
            int help = BoxTop + BoxRows + 3;
            if (Mode == "menu")
            {
                for (int i = 0; i < BoxRows && MenuTop + i < Entries.Count; i++)
                {
                    string t = " " + (Entries[MenuTop + i].Title ?? "");
                    // a title too long for the box ends in an arrow, as GRUB draws it
                    put(BoxTop + 1 + i, 2, t.Length > Inner ? t.Substring(0, Inner - 1) + (char)0x2192 : t);
                }
                HighlightRow = BoxTop + 1 + (Sel - MenuTop);
                if (MenuTop > 0) r[BoxTop + 1][W - 2] = ArUp;
                if (MenuTop + BoxRows < Entries.Count) r[BoxTop + BoxRows][W - 2] = ArDown;
                put(help, 6, "Use the Up and Down keys to choose which entry is highlighted.");
                put(help + 1, 6, "Press Enter to start the highlighted entry, e to edit its commands");
                put(help + 2, 6, "before starting it, or c for a command line.");
                if (Seconds > 0) put(help + 3, 3, "The highlighted entry will start by itself in " + Seconds + "s.");
                HintRow = H - 1;
                put(HintRow, 0, "Cert Lab: keys act at once - Up/Down, Enter starts, e edits, c for commands.");
            }
            else
            {
                // the editor: every line in rows of 75 characters, a \ at the edge where it goes on
                var rows = new List<string>();
                int curRow = 0, curCol = 0;
                for (int i = 0; i < Lines.Count; i++)
                {
                    string l = Lines[i];
                    int n = l.Length / Wrap + 1;
                    for (int c = 0; c < n; c++)
                    {
                        string part = l.Substring(Math.Min(l.Length, c * Wrap), Math.Min(Wrap, Math.Max(0, l.Length - c * Wrap)));
                        rows.Add(c < n - 1 ? part + "\\" : part);
                    }
                    if (i == Row) { curRow = rows.Count - n + Col / Wrap; curCol = Col % Wrap; }
                }
                if (curRow < EditTop) EditTop = curRow;
                if (curRow >= EditTop + BoxRows) EditTop = curRow - BoxRows + 1;
                for (int i = 0; i < BoxRows && EditTop + i < rows.Count; i++) put(BoxTop + 1 + i, 2, rows[EditTop + i]);
                if (EditTop > 0) r[BoxTop + 1][W - 2] = ArUp;
                if (EditTop + BoxRows < rows.Count) r[BoxTop + BoxRows][W - 2] = ArDown;
                CursorRow = BoxTop + 1 + curRow - EditTop; CursorCol = 2 + curCol;
                put(help, 6, "Edit with the arrows, Home / End, Ctrl+A / Ctrl+E, Backspace and Delete.");
                put(help + 1, 6, "Press Ctrl+X or F10 to boot these commands, Ctrl+C or F2 for a command");
                put(help + 2, 6, "line, or Esc to throw away your changes and go back to the menu.");
                HintRow = H - 1;
                put(HintRow, 0, "Cert Lab: Ctrl+X boots what you see here; Esc goes back to the menu.");
            }
            return r.Select(x => new string(x)).ToArray();
        }

        /// <summary>The screen as text (tests, TermView.Text).</summary>
        public string Text { get { return string.Join("\n", Rows().Select(x => x.TrimEnd()).ToArray()); } }
    }
}
