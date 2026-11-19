// Network Lab - the base of the end devices' command lines (Command Prompt, PowerShell, the Linux and macOS
// terminals): output buffer, busy jobs, questions (passwords), history, the little text editor (nano).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NetworkLab
{
    public abstract class Shell
    {
        public readonly Device D;
        readonly StringBuilder output = new StringBuilder();
        public int Version;
        public object Busy;
        public Func<string, bool> Pending;
        public bool PendingSecret;
        public string PendingPrompt = "";
        public readonly List<string> History = new List<string>();
        public bool Closed;
        public bool ClearRequested;
        public string Title = "Terminal";
        bool capturing;
        readonly StringBuilder captured = new StringBuilder();
        // the text editor (nano): when EditorFile is set, the window shows the editor instead of the terminal
        public string EditorFile;
        public string EditorText;
        public bool EditorReadOnly;
        public Action<Shell, string> EditorSaved;

        protected Shell(Device d) { D = d; }

        public abstract string Prompt { get; }
        protected abstract void Run(string line);
        public virtual string Complete(string line) { return line; }
        /// <summary>What Tab could put in place of the word being typed (see Completion).</summary>
        public virtual Completion Candidates(string line) { return new Completion { Start = line.Length }; }
        /// <summary>bash (complete, a second Tab lists) or cycle (each Tab shows the next match, as in Windows).</summary>
        public virtual string TabStyle { get { return "bash"; } }

        /// <summary>The word being typed (after the last space) and where it starts.</summary>
        protected static string LastWord(string line, out int start)
        {
            int sp = line.LastIndexOf(' ');
            start = sp + 1;
            return line.Substring(start);
        }
        public virtual string Banner { get { return ""; } }

        public void Print(string s)
        {
            if (capturing) { captured.Append(s); if (!s.EndsWith("\n")) captured.Append('\n'); return; }
            output.Append(s.Replace("\r\n", "\n"));
            if (!s.EndsWith("\n")) output.Append('\n');
            Version++;
        }
        public void Write(string s) { if (capturing) { captured.Append(s); return; } output.Append(s); Version++; }
        public string TakeOutput() { string s = output.ToString(); output.Clear(); return s; }

        public string CurrentPrompt { get { return Pending != null ? PendingPrompt : Prompt; } }

        public void Execute(string line)
        {
            if (Busy != null) return;
            if (Pending != null) { var p = Pending; Pending = null; PendingSecret = false; p(line); return; }
            if (line.Trim().Length > 0) { History.Add(line); if (History.Count > 100) History.RemoveAt(0); }
            try { Run(line); }
            catch (Exception ex) { Print("error: " + ex.Message); }
            D.P.Touch();
        }

        /// <summary>Runs one command and returns what it printed (used by SSH from another computer).</summary>
        public string RunRemote(string line)
        {
            capturing = true;
            captured.Clear();
            try { Run(line); }
            catch (Exception ex) { captured.Append("error: " + ex.Message + "\n"); }
            finally { capturing = false; }
            if (Busy != null) { Break(); captured.Append("(this command keeps running; over SSH only quick commands are shown)\n"); }
            return captured.ToString();
        }

        public void Ask(string prompt, bool secret, Func<string, bool> answer) { PendingPrompt = prompt; PendingSecret = secret; Pending = answer; }

        /// <summary>Ctrl+C: stop what is running.</summary>
        public virtual void Break()
        {
            var p = Busy as PingJob; if (p != null) p.Stop();
            var t = Busy as TraceJob; if (t != null) t.Stop();
            if (Pending != null) { Pending = null; PendingSecret = false; Print("^C"); }
        }

        public void OpenEditor(string file, string text, bool readOnly, Action<Shell, string> saved)
        {
            EditorFile = file; EditorText = text; EditorReadOnly = readOnly; EditorSaved = saved;
            Version++;
        }
        public void CloseEditor(string text, bool save)
        {
            if (save && EditorSaved != null && !EditorReadOnly) EditorSaved(this, text);
            EditorFile = null; EditorText = null; EditorSaved = null;
            Version++;
        }

        // ---------------------------------------------------------------- helpers
        /// <summary>Splits a command line into words; "quoted words" stay together.</summary>
        public static List<string> Split(string line)
        {
            var r = new List<string>();
            var sb = new StringBuilder();
            bool q = false, any = false;
            foreach (char c in line)
            {
                if (c == '"') { q = !q; any = true; continue; }
                if (c == ' ' && !q) { if (sb.Length > 0 || any) { r.Add(sb.ToString()); sb.Clear(); any = false; } continue; }
                sb.Append(c);
            }
            if (sb.Length > 0 || any) r.Add(sb.ToString());
            return r;
        }

        protected Host H { get { return D.Host; } }
        protected Personality Pers { get { return Personalities.Get(D.Os); } }

        /// <summary>Waits for a job; Busy is cleared when it ends.</summary>
        protected void Hold(object job) { Busy = job; }
        protected void Release() { Busy = null; Version++; }

        public static string Ms(long ms, bool windows)
        {
            if (windows) return ms < 1 ? "time<1ms" : "time=" + ms + "ms";
            return "time=" + (ms == 0 ? "0.4" : ms.ToString() + ".0") + " ms";
        }
    }
}
