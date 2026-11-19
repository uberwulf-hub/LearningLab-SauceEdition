// Cert Lab - checks of the GRUB screen: the menu and its countdown, the entry editor's keys, booting what was
// edited (rd.break, init=/bin/bash, systemd.unit=...), the command line, and the same keys sent through the
// terminal window's real key events (PreviewKeyDown / TextInput), including Andrew's keystrokes from 2026-10-17.
using System;
using System.Collections.Generic;
using System.Linq;

namespace CertLab
{
    public static partial class Tests
    {
        /// <summary>The root password reset's keys, as the solutions type them.</summary>
        static void GrubResetKeys(T t, string machine)
        {
            Type(t, machine, "#key e");
            Type(t, machine, "#key Down Down Down End");
            Type(t, machine, "#type  rd.break");
            Type(t, machine, "#key Ctrl+X");
        }

        static T AtGrub()
        {
            var t = new T(1);
            Type(t, "server1", "systemctl reboot");
            return t;
        }

        static void GrubTests()
        {
            // ---- the menu
            var t = AtGrub();
            var s = t.S;
            var g = s.Grub;
            Check("reboot shows the GRUB screen", g != null && g.Mode == "menu" && t.M.State == "grub", t.M.State);
            if (g == null) { t.Dispose(); return; }
            string scr = g.Text;
            Check("the menu lists both kernels and the rescue entry", g.Entries.Count == 3 && scr.Contains("Red Hat Enterprise Linux (" + Machine.Kernels[0] + ") 9.5 (Plow)") && scr.Contains("(" + Machine.Kernels[1] + ")") && scr.Contains("0-rescue-"), scr);
            Check("the default entry is highlighted", g.Sel == 0 && g.HighlightRow == GrubScreen.BoxTop + 1, g.HighlightRow.ToString());
            Check("the countdown starts at GRUB_TIMEOUT (5)", g.Seconds == 5 && scr.Contains("start by itself in 5s"), scr);
            Check("the help text names the keys", scr.Contains("Up and Down keys") && scr.Contains("e to edit") && scr.Contains("c for a command line"), scr);
            Check("no command prompt on the menu", !scr.Contains("grub>") && s.Prompt == "", s.Prompt);
            Check("the screen is 80 x 25", g.Rows().Length == 25 && g.Rows().All(r => r.Length == 80));
            s.GrubTick(); s.GrubTick();
            Check("the countdown counts down", g.Seconds == 3 && g.Text.Contains("in 3s"), g.Text);
            s.GrubKey("Down");
            Check("Down highlights the next entry", g.Sel == 1 && g.HighlightRow == GrubScreen.BoxTop + 2);
            Check("a key stops the countdown", g.Seconds == -1 && !g.Text.Contains("start by itself"), g.Text);
            for (int i = 0; i < 10; i++) s.GrubTick();
            Check("a stopped countdown never boots", s.Grub == g && t.M.State == "grub", t.M.State);
            s.GrubKey("Down"); s.GrubKey("Down");
            Check("Down stops at the last entry", g.Sel == 2);
            s.GrubKey("Up"); s.GrubKey("Up"); s.GrubKey("Up");
            Check("Up stops at the first entry", g.Sel == 0);
            s.GrubType("x"); s.GrubType("7");
            Check("other keys do nothing on the menu", s.Grub == g && g.Mode == "menu");
            s.GrubKey("Enter");
            Check("Enter boots the highlighted entry", s.Grub == null && t.M.State == "running" && !t.M.Cmdline.Contains("rd.break"), t.M.State + " " + t.M.Cmdline);
            t.Dispose();

            // ---- left alone, the countdown boots the default entry
            t = AtGrub();
            for (int i = 0; i < 4; i++) t.S.GrubTick();
            Check("4 seconds: still at the menu", t.S.Grub != null && t.M.State == "grub");
            t.S.GrubTick();
            Check("5 seconds: the default entry boots", t.S.Grub == null && t.M.State == "running", t.M.State);
            t.Dispose();

            // ---- the editor
            t = AtGrub(); s = t.S; g = s.Grub;
            s.GrubType("e");
            Check("e opens the editor at once (no Enter)", g.Mode == "edit" && s.Grub == g && t.M.State == "grub", g.Mode);
            string k0 = Machine.Kernels[0];
            Check("the editor shows the entry's commands", g.Lines.Count == 5 && g.Lines[0] == "load_video" && g.Lines[1] == "set gfxpayload=keep" && g.Lines[2] == "insmod gzio"
                && g.Lines[3] == "linux ($root)/vmlinuz-" + k0 + " " + t.M.DefaultArgs() && g.Lines[4] == "initrd ($root)/initramfs-" + k0 + ".img $tuned_initrd", string.Join("\n", g.Lines.ToArray()));
            scr = g.Text;
            Check("the cursor starts at the top left", g.Row == 0 && g.Col == 0 && g.CursorRow == GrubScreen.BoxTop + 1 && g.CursorCol == 2);
            Check("long lines wrap with a \\ at the edge", scr.Split('\n').Any(r => r.Length > 77 && r[77] == '\\' && r.Contains("linux ($root)")), scr);
            Check("the editor's help names Ctrl+X and Esc", scr.Contains("Ctrl+X or F10 to boot") && scr.Contains("Esc to throw away") && scr.Contains("Cert Lab: Ctrl+X boots"), scr);
            s.GrubKey("Right"); s.GrubKey("Right"); s.GrubKey("Right");
            Check("Right moves the cursor", g.Col == 3);
            s.GrubKey("End");
            Check("End goes to the end of the line", g.Col == "load_video".Length);
            s.GrubKey("Ctrl+A");
            Check("Ctrl+A goes to the start", g.Col == 0);
            s.GrubKey("Ctrl+E");
            Check("Ctrl+E goes to the end", g.Col == 10);
            s.GrubKey("Right");
            Check("Right at the end of a line goes to the next one", g.Row == 1 && g.Col == 0);
            s.GrubKey("Left");
            Check("Left at the start goes back to the end of the line above", g.Row == 0 && g.Col == 10);
            s.GrubKey("Down");
            Check("Down keeps the column", g.Row == 1 && g.Col == 10);
            s.GrubKey("End"); s.GrubKey("Up");
            Check("Up onto a shorter line stops at its end", g.Row == 0 && g.Col == 10);
            s.GrubKey("Down"); s.GrubKey("Down");
            Check("Down again", g.Row == 2 && g.Col == 10);
            s.GrubKey("Home"); s.GrubType("XY");
            Check("typing inserts", g.Lines[2] == "XYinsmod gzio" && g.Col == 2, g.Lines[2]);
            s.GrubKey("Backspace");
            Check("Backspace deletes before the cursor", g.Lines[2] == "Xinsmod gzio" && g.Col == 1, g.Lines[2]);
            s.GrubKey("Left"); s.GrubKey("Delete");
            Check("Delete deletes at the cursor", g.Lines[2] == "insmod gzio" && g.Col == 0, g.Lines[2]);
            s.GrubKey("Backspace");
            Check("Backspace at the start joins the line to the one above", g.Lines.Count == 4 && g.Lines[1] == "set gfxpayload=keepinsmod gzio" && g.Row == 1 && g.Col == 19, string.Join("|", g.Lines.ToArray()));
            s.GrubKey("Enter");
            Check("Enter splits the line", g.Lines.Count == 5 && g.Lines[1] == "set gfxpayload=keep" && g.Lines[2] == "insmod gzio" && g.Row == 2 && g.Col == 0, string.Join("|", g.Lines.ToArray()));
            s.GrubKey("Down"); s.GrubKey("End");
            Check("arrows reach the end of the wrapped linux line", g.Row == 3 && g.Col == g.Lines[3].Length && g.CursorRow > GrubScreen.BoxTop + 4, g.CursorRow + "," + g.CursorCol);
            s.GrubType(" rd.break");
            s.GrubKey("Esc");
            Check("Esc goes back to the menu", g.Mode == "menu" && s.Grub == g && t.M.State == "grub");
            s.GrubType("e");
            Check("Esc threw the changes away", !g.Lines[3].Contains("rd.break") && g.Lines[3] == "linux ($root)/vmlinuz-" + k0 + " " + t.M.DefaultArgs(), g.Lines[3]);
            s.GrubKey("Ctrl+C");
            Check("Ctrl+C opens the command line", g.Mode == "cmd" && g.Text.Contains("grub>"), g.Text);
            s.GrubType("ls"); s.GrubKey("Enter");
            Check("ls lists the disks", g.CmdOut.Any(x => x.Contains("(hd0,msdos1)")), string.Join("\n", g.CmdOut.ToArray()));
            s.GrubType("frobnicate"); s.GrubKey("Enter");
            Check("an unknown command is an error", g.CmdOut.Last().Contains("can't find command"), g.CmdOut.Last());
            s.GrubKey("Esc");
            Check("Esc goes back to the editor", g.Mode == "edit");
            s.GrubKey("Down"); s.GrubKey("Down"); s.GrubKey("Down"); s.GrubKey("Ctrl+K");
            s.GrubKey("Ctrl+X");
            Check("no linux command: GRUB says so", g.Mode == "message" && g.Text.Contains("you need to load the kernel first") && g.Text.Contains("Press any key"), g.Text);
            s.GrubType("q");
            Check("any key goes back to the editor", g.Mode == "edit" && t.M.State == "grub");
            s.GrubKey("Ctrl+Y");
            Check("Ctrl+Y puts the cut text back", g.Lines[3].StartsWith("linux ($root)/vmlinuz-"), g.Lines[3]);
            s.GrubKey("Ctrl+A"); s.GrubKey("Right"); s.GrubKey("Right"); s.GrubKey("Right"); s.GrubKey("Right"); s.GrubKey("Right");
            for (int i = 0; i < 9; i++) s.GrubKey("Right");
            s.GrubType("x");
            s.GrubKey("F10");
            Check("a wrong kernel file: GRUB says it is not found", g.Mode == "message" && g.Text.Contains("not found"), g.Text);
            s.GrubKey("Enter"); s.GrubKey("Backspace");
            s.GrubKey("End"); s.GrubType(" rd.break"); s.GrubKey("F10");
            Check("F10 boots the edited entry", s.Grub == null && t.M.State == "rdbreak", t.M.State);
            Check("the kernel got the edited line word for word", t.M.Cmdline == "BOOT_IMAGE=(hd0,msdos1)/vmlinuz-" + k0 + " " + t.M.DefaultArgs() + " rd.break", t.M.Cmdline);
            t.Dispose();

            // ---- the reset as the solutions type it, and the other one-boot changes
            t = AtGrub();
            GrubResetKeys(t, "server1");
            Check("#key / #type: rd.break stops in the initramfs", t.M.State == "rdbreak" && t.M.Cmdline.EndsWith("rhgb quiet rd.break"), t.M.State + " " + t.M.Cmdline);
            string o = Type(t, "server1", "mount -o remount,rw /sysroot");
            Check("the switch_root shell", t.S.Prompt == "switch_root:/# ", t.S.Prompt);
            t.Dispose();

            t = AtGrub();
            Type(t, "server1", "#key e");
            Type(t, "server1", "#key Down Down Down End");
            for (int i = 0; i < "rhgb quiet".Length; i++) Type(t, "server1", "#key Backspace");
            Type(t, "server1", "#type init=/bin/bash");
            Type(t, "server1", "#key Ctrl+X");
            Check("init=/bin/bash (rhgb quiet removed)", t.M.State == "initbash" && !t.M.Cmdline.Contains("rhgb") && t.M.Cmdline.EndsWith(" init=/bin/bash"), t.M.State + " " + t.M.Cmdline);
            t.Dispose();

            t = AtGrub();
            Type(t, "server1", "#key e");
            Type(t, "server1", "#key Down Down Down Ctrl+E");
            Type(t, "server1", "#type  systemd.unit=emergency.target");
            Type(t, "server1", "#key Ctrl+X");
            Check("systemd.unit=emergency.target", t.M.State == "emergency", t.M.State);
            t.Dispose();

            t = AtGrub();
            Type(t, "server1", "#key End e Ctrl+X");
            Check("the rescue entry boots its own kernel", t.M.Cmdline.Contains("vmlinuz-0-rescue-") && t.M.State == "running", t.M.Cmdline);
            t.Dispose();

            t = AtGrub();
            Type(t, "server1", "#key c");
            Type(t, "server1", "#type reboot");
            Type(t, "server1", "#key Enter");
            Check("reboot on the command line: the menu again", t.S.Grub != null && t.S.Grub.Mode == "menu" && t.S.Grub.Seconds == 5);
            Type(t, "server1", "hostname");
            Check("a typed line at the menu boots the default entry first", t.M.State == "running" && t.S.Grub == null, t.M.State);
            t.Dispose();

            // ---- GRUB_TIMEOUT
            t = new T(1);
            t.Run("sed -i 's/^GRUB_TIMEOUT=.*/GRUB_TIMEOUT=12/' /etc/default/grub");
            Type(t, "server1", "systemctl reboot");
            Check("GRUB does not read /etc/default/grub", t.S.Grub != null && t.S.Grub.Seconds == 5, t.S.Grub == null ? "no menu" : t.S.Grub.Seconds.ToString());
            Type(t, "server1", "grub2-mkconfig -o /boot/grub2/grub.cfg 2>/dev/null");
            Type(t, "server1", "systemctl reboot");
            Check("after grub2-mkconfig the countdown uses GRUB_TIMEOUT", t.S.Grub != null && t.S.Grub.Seconds == 12, t.S.Grub == null ? "no menu" : t.S.Grub.Seconds.ToString());
            t.Dispose();

        }

    }
}
