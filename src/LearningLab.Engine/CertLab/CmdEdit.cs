// Cert Lab - editing files: vi, vim, view, nano, vipw, vigr, visudo and sudoedit open the window's editor over the
// terminal; :w / ^O write the file at once with the person's permissions (the file keeps its inode, owner and
// SELinux label, as vim and nano do on RHEL).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CertLab
{
    public static partial class Cmd
    {
        static void RegEdit()
        {
            P("vi", (sh, a, io) => EditCmd(sh, a, io, "vi")); P("vim", (sh, a, io) => EditCmd(sh, a, io, "vi")); P("rvi", (sh, a, io) => EditCmd(sh, a, io, "vi")); P("rvim", (sh, a, io) => EditCmd(sh, a, io, "vi"));
            P("view", (sh, a, io) => { var b = new List<string>(a); b.Insert(1, "-R"); return EditCmd(sh, b, io, "vi"); });
            P("nano", (sh, a, io) => EditCmd(sh, a, io, "nano"));
            P("vimtutor", (sh, a, io) => { io.L("(Cert Lab: vimtutor is not included; the Course has a lesson on vi)"); return 0; });
            P("vipw", (sh, a, io) => { if (sh.P.Cr.Uid != 0) { io.E("vipw: Permission denied."); return 1; } return EditCmd(sh, new List<string> { "vi", a.Contains("-s") ? "/etc/shadow" : "/etc/passwd" }, io, "vi"); });
            P("vigr", (sh, a, io) => { if (sh.P.Cr.Uid != 0) { io.E("vigr: Permission denied."); return 1; } return EditCmd(sh, new List<string> { "vi", a.Contains("-s") ? "/etc/gshadow" : "/etc/group" }, io, "vi"); });
            P("visudo", Visudo);
            P("xxd", (sh, a, io) => { int st = 0; foreach (var kv in Inputs(sh, a.Skip(1).Where(x => !x.StartsWith("-", StringComparison.Ordinal)).ToList(), io, "xxd", ref st)) { var b = Encoding.UTF8.GetBytes(kv.Value); for (int i = 0; i < b.Length; i += 16) { var seg = b.Skip(i).Take(16).ToArray(); io.L(i.ToString("x8") + ": " + string.Join(" ", Enumerable.Range(0, (seg.Length + 1) / 2).Select(k => seg[k * 2].ToString("x2") + (k * 2 + 1 < seg.Length ? seg[k * 2 + 1].ToString("x2") : "  ")).ToArray()).PadRight(40) + "  " + new string(seg.Select(x => x >= 32 && x < 127 ? (char)x : '.').ToArray())); } } return st; });
        }

        static string EditorKind(Sh sh, string prog)
        {
            if (prog == "nano") return "nano";
            return "vi";
        }

        static int EditCmd(Sh sh, List<string> a, Io io, string kind)
        {
            var m = sh.M;
            bool ro = false; string file = null; int line = 0;
            foreach (var x in a.Skip(1))
            {
                if (x == "-R") { ro = true; continue; }
                if (x.StartsWith("+", StringComparison.Ordinal) && x.Length > 1) { int.TryParse(x.Substring(1), out line); continue; }
                if (x.StartsWith("-", StringComparison.Ordinal) && file == null) continue;
                if (file == null) file = x;
            }
            if (sh.S == null) { io.E("Vim: Warning: Output is not to a terminal"); return 1; }
            if (!io.Out.IsTty) io.E("Vim: Warning: Output is not to a terminal");
            string text = null;
            string path = file ?? "[No Name]";
            if (file != null)
            {
                Loc l;
                try { l = Find(sh, file, true); }
                catch (VfsEx e) { io.E("\"" + file + "\" " + e.Message); return 1; }
                if (l.Exists)
                {
                    if (l.Node.T == 'd') { io.E("\"" + file + "\" is a directory"); text = null; }
                    else
                    {
                        try { text = m.V.ReadNode(sh.P, l); }
                        catch (VfsEx) { io.E("\"" + file + "\" [Permission Denied]"); text = ""; ro = true; }
                        if (!m.V.Can(sh.P.Cr, l.Node, 2) && sh.P.Cr.Uid != 0) ro = true;
                        if (l.Mnt != null && l.Mnt.Ro) ro = true;
                    }
                }
            }
            Action<string, string> save = (p, t) =>
            {
                var lp = Find(sh, p, true);
                if (lp.Exists && lp.Node.T == 'd') throw new VfsEx("EISDIR");
                m.V.WriteLoc(sh.P, lp, t, false, 0x1B6);
            };
            var res = sh.S.Edit(path, text, ro, EditorKind(sh, kind), save);
            return 0;
        }

        static int Visudo(Sh sh, List<string> a, Io io)
        {
            var m = sh.M;
            var o = Opts.Parse(a, "cf:qs", "check|c,file=|f,quiet|q,strict|s", "visudo");
            if (o.Error != null) return Fail(io, o);
            if (sh.P.Cr.Uid != 0) { io.E("visudo: /etc/sudoers: Permission denied"); return 1; }
            string file = o.Get("f") ?? (o.Args.Count > 0 ? o.Args[0] : "/etc/sudoers");
            if (o.Has("c"))
            {
                string err;
                var files = new List<string> { file };
                if (file == "/etc/sudoers") try { var d = m.V.G("/etc/sudoers.d"); if (d.Exists) foreach (var k in d.Node.Kids.Keys.OrderBy(x => x, StringComparer.Ordinal)) files.Add("/etc/sudoers.d/" + k); } catch (VfsEx) { }
                int st = 0;
                foreach (var f in files)
                {
                    err = SudoersError(m, f, m.V.Cat(f));
                    if (err != null) { io.E(err.Replace(">>> ", "").Replace(" <<<", "") + "\n" + f + ": bad permissions, should be mode 0440"); io.L(f + ": parse error"); st = 1; }
                    else io.L(f + ": parsed OK");
                }
                return st;
            }
            if (sh.S == null) return 1;
            string text = m.V.Cat(file) ?? "";
            string current = text;
            for (;;)
            {
                string res = sh.S.Edit(file + ".tmp", current, false, (sh.GetVar("EDITOR") ?? "vi").Contains("nano") ? "nano" : "vi");
                if (res == null || res == text) return 0;
                string err = SudoersError(m, file, res);
                if (err == null)
                {
                    m.SysWrite(file, res, file.StartsWith("/etc/sudoers", StringComparison.Ordinal) ? 0x120 : -1, null);
                    return 0;
                }
                io.L(err.Replace(file, file + ".tmp"));
                for (;;)
                {
                    string ans = sh.S.ReadLine("What now? ", false);
                    if (ans == null) return 1;
                    ans = ans.Trim();
                    if (ans == "e") { current = res; break; }
                    if (ans == "x") return 0;
                    if (ans == "Q") { m.SysWrite(file, res, 0x120, null); return 0; }
                    io.L("Options are:\n  (e)dit sudoers file again\n  e(x)it without saving changes to sudoers file\n  (Q)uit and save changes to sudoers file (DANGER!)\n");
                }
            }
        }

        static string SudoersError(Machine m, string file, string text)
        {
            if (text == null) return null;
            // check this file's text on its own
            int n = 0;
            foreach (var raw0 in text.Replace("\\\n", " ").Split('\n'))
            {
                n++;
                string raw = raw0.Trim();
                if (raw.StartsWith("#include", StringComparison.Ordinal) || raw.StartsWith("@include", StringComparison.Ordinal)) continue;
                int hash = raw.IndexOf('#'); if (hash >= 0) raw = raw.Substring(0, hash).Trim();
                if (raw.Length == 0 || raw.StartsWith("Defaults", StringComparison.Ordinal)) continue;
                if (Regex.IsMatch(raw, "^(User_Alias|Cmnd_Alias|Host_Alias|Runas_Alias)\\s+[A-Z][A-Z0-9_]*\\s*=")) continue;
                var rm = Regex.Match(raw, "^(\\S+)\\s+([^=\\s]+)\\s*=\\s*(\\(([^)]*)\\))?\\s*((NOPASSWD|PASSWD|NOEXEC|EXEC|SETENV|NOSETENV)\\s*:\\s*)*(.+)$");
                if (!rm.Success) return ">>> " + file + ": syntax error near line " + n + " <<<";
                foreach (var c in rm.Groups[7].Value.Split(','))
                {
                    var cc = Regex.Replace(c.Trim(), "^(NOPASSWD|PASSWD)\\s*:\\s*", "");
                    if (cc.Length == 0 || cc == "ALL" || cc.StartsWith("/", StringComparison.Ordinal) || cc.StartsWith("!", StringComparison.Ordinal) || Regex.IsMatch(cc, "^[A-Z][A-Z0-9_]*$")) continue;
                    return ">>> " + file + ": syntax error near line " + n + " <<<";
                }
            }
            return null;
        }
    }
}
