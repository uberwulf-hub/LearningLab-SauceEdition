// Cert Lab - a PowerShell console on a pretend Windows machine: a worker thread runs the read-run loop and asks for
// each line (Read-Host, prompts for mandatory parameters and confirmations ask the same way); only one thread runs at
// a time, the caller waits until the worker asks again, so everything stays deterministic. Enter-PSSession pushes a
// remote session; exit pops it.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace CertLab
{
    public class PsConsole : ITerm, IPsHost
    {
        public PsMachine M;
        public PsRunspace Local;
        public readonly Stack<PsRunspace> RemoteStack = new Stack<PsRunspace>();
        public PsRunspace Top { get { return RemoteStack.Count > 0 ? RemoteStack.Peek() : Local; } }
        readonly StringBuilder outBuf = new StringBuilder();
        readonly object outLock = new object();
        Thread worker;
        readonly SemaphoreSlim toWorker = new SemaphoreSlim(0), toCaller = new SemaphoreSlim(0);
        volatile bool waiting, finished, aborting, started;
        string prompt = "", provided;
        bool secret, brk;
        public int Width = 120;
        public bool ClearFlag;
        public EditRequest Edit;

        public PsConsole(PsMachine m) { M = m; NewLocal(); }

        void NewLocal()
        {
            Local = new PsRunspace(M, this, "Administrator");
            Local.ClearRequested = () => { ClearFlag = true; };
            Local.Console = this;
        }

        // ------------------------------------------------------------ ITerm
        public string Prompt { get { return waiting ? prompt : ""; } }
        public bool Secret { get { return waiting && secret; } }
        public bool Waiting { get { return waiting; } }
        public bool Busy { get { return started && !waiting && !finished; } }
        public bool Finished { get { return finished; } }
        public bool WaitingForBreak { get { return false; } }
        public GrubScreen Grub { get { return null; } }     // a PowerShell console has no boot menu
        public bool GrubKey(string key) { return false; }
        public void GrubType(string text) { }
        public void GrubTick() { }
        public bool ClearRequested { get { return ClearFlag; } set { ClearFlag = value; } }
        public EditRequest PendingEdit { get { return Edit; } }
        public int Columns { set { Width = Math.Max(60, Math.Min(200, value)); } }
        int IPsHost.Width { get { return Width; } }
        public bool Interactive { get { return true; } }
        public void AfterSubmit() { }
        public Completion Candidates(string line) { return PsComplete.Candidates(Top, line); }

        public void Start()
        {
            if (started) return;
            started = true;
            worker = new Thread(Main) { IsBackground = true, Name = "certlab-ps-" + M.Name };
            worker.Start();
            toCaller.Wait();
        }
        public void Execute(string line)
        {
            if (!started) Start();
            if (!waiting) return;
            provided = line;
            Resume();
        }
        public void Break() { if (started && !finished && waiting) { brk = true; Resume(); } }
        public void Eof() { }
        public void EditDone(string result, bool saved)
        {
            if (Edit == null) return;
            Edit.Result = result; Edit.Saved = saved; Edit.Done = true;
            provided = "";
            if (waiting) Resume();
        }
        public string TakeOutput() { lock (outLock) { var s = outBuf.ToString(); outBuf.Clear(); return s; } }
        public void Dispose()
        {
            aborting = true;
            if (waiting) { try { toWorker.Release(); } catch (Exception) { } }
            M.Sessions.Remove(Local);
        }

        // ------------------------------------------------------------ IPsHost (called on the worker)
        public void Write(string s) { lock (outLock) outBuf.Append(s); }
        public string ReadLine(string p, bool sec)
        {
            if (Thread.CurrentThread != worker) return null;
            prompt = p ?? ""; secret = sec;
            Hand();
            if (brk) { brk = false; throw new PsBreakSignal(); }
            var r = provided ?? ""; provided = null;
            return r;
        }
        /// <summary>Opens the window's editor (notepad) and waits until it closes.</summary>
        public string EditFile(string path, string text)
        {
            if (Thread.CurrentThread != worker || EditorHook != null) return EditorHook == null ? null : EditorHook(path, text);
            Edit = new EditRequest { Path = path, Text = text, Kind = "nano" };
            try
            {
                for (;;) { prompt = ""; Hand(); if (brk) { brk = false; return null; } if (Edit.Done) return Edit.Saved ? Edit.Result : null; }
            }
            finally { Edit = null; }
        }
        public Func<string, string, string> EditorHook;

        void Hand()
        {
            waiting = true;
            toCaller.Release();
            toWorker.Wait();
            waiting = false;
            if (aborting) throw new PsSessionEnd();
        }
        void Resume()
        {
            if (!started || finished) return;
            toWorker.Release();
            toCaller.Wait();
        }

        void Main()
        {
            try { Loop(); }
            catch (PsSessionEnd) { }
            catch (Exception e) { Write("\n[Cert Lab: the console stopped because of a problem: " + e.GetType().Name + ": " + e.Message + "]\n"); }
            finished = true; waiting = false;
            toCaller.Release();
        }

        void Loop()
        {
            Write("Windows PowerShell (Cert Lab simulated console on " + M.Name + ")\nNothing typed here touches this PC. Type Get-Help to start.\n\n");
            Local.RunProfiles();
            var buf = new StringBuilder();
            for (;;)
            {
                if (Local.Closed) { RemoteStack.Clear(); NewLocal(); Write("\n[" + M.Name + " restarted. A new PowerShell window opened.]\n\n"); Local.RunProfiles(); buf.Clear(); }
                while (RemoteStack.Count > 0 && RemoteStack.Peek().Closed) { RemoteStack.Pop(); Write("[The remote session ended.]\n"); }
                string p = buf.Length == 0 ? Top.PromptText() : ">> ";
                string line;
                try { line = ReadLine(p, false); }
                catch (PsBreakSignal) { buf.Clear(); Write("\n"); continue; }
                if (buf.Length > 0 && line.Trim().Length == 0 && Incomplete(buf.ToString())) { buf.Clear(); Write("\n"); continue; }
                if (buf.Length > 0) buf.Append('\n');
                buf.Append(line);
                var top = Top;
                bool done;
                try { done = top.ExecuteInteractive(buf.ToString()); }
                catch (PsBreakSignal) { done = true; Write("\n"); }
                if (!done) continue;
                buf.Clear();
                if (top.ExitRequested)
                {
                    top.ExitRequested = false;
                    if (RemoteStack.Count > 0 && RemoteStack.Peek() == top) { RemoteStack.Pop(); top.Closed = true; }
                    else { Local.Closed = true; }
                }
            }
        }
        static bool Incomplete(string s) { try { PsParser.Parse(s, ""); return false; } catch (PsParseError e) { return e.Incomplete; } }

        /// <summary>The prompt the terminal shows before the line being typed.</summary>
        public string CurrentPrompt { get { return prompt; } }
    }

    public class PsBreakSignal : Exception { }
    public class PsSessionEnd : Exception { }
}
