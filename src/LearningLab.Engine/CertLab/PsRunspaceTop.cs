// Cert Lab - running a whole input: a line typed at the console (output formatted by Out-Default, errors in the
// 5.1 layout, $? and history), or a script run quietly (lab setup and checks) whose output is collected.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;

namespace CertLab
{
    public partial class PsRunspace
    {
        public int HistoryId;
        public readonly List<KeyValuePair<string, DateTime>> History = new List<KeyValuePair<string, DateTime>>();
        public readonly StringBuilder Transcript = new StringBuilder();   // all text this console showed (checks can read it)

        /// <summary>Text for the host that also goes to the transcript.</summary>
        public void Show(string s) { HostWrite(s); Transcript.Append(s); if (Transcript.Length > 400000) Transcript.Remove(0, 200000); }

        public Streams HostStreams(OutDefault od)
        {
            var st = new Streams();
            st.Out = o => od.Push(o);
            st.Err = e => { od.Interrupt(); Show(ErrorText(e)); };
            st.Warn = w => { od.Interrupt(); Show("WARNING: " + w + "\n"); };
            st.Verbose = w => { od.Interrupt(); Show("VERBOSE: " + w + "\n"); };
            st.Debug = w => { od.Interrupt(); Show("DEBUG: " + w + "\n"); };
            st.Info = o => { od.Interrupt(); var h = o as PsHostText; if (h != null) Show(h.Text + (h.NoNewline ? "" : "\n")); else Show(PsConv.ToStr(o) + "\n"); };
            return st;
        }

        /// <summary>Runs what was typed at the prompt. Returns false if the text is not complete yet (show >>).</summary>
        public bool ExecuteInteractive(string text)
        {
            ScriptBlockAst ast;
            try { ast = PsParser.Parse(text, ""); }
            catch (PsParseError e)
            {
                if (e.Incomplete) return false;
                var r = ParseErrorRecord(e);
                AddError(r);
                Show(ErrorText(r));
                LastOk = false;
                return true;
            }
            if (text.Trim().Length > 0) { HistoryId++; History.Add(new KeyValuePair<string, DateTime>(text, M.Now)); }
            InvocationText = text;
            var od = new OutDefault(this, s => Show(s));
            var st = HostStreams(od);
            CurrentStreams = st;
            Host_Suggest = null;
            RunTop(ast, st, () => od.EndStatement());
            od.Finish();
            if (Host_Suggest != null)
            {
                Show("\nSuggestion [3,General]: The command " + Host_Suggest + " was not found, but does exist in the current location. Windows PowerShell does not load commands from the current location by default. If you trust this command, instead type: \".\\" + Host_Suggest + "\". See \"get-help about_Command_Precedence\" for more details.\n");
                Host_Suggest = null;
            }
            return true;
        }

        /// <summary>A script run without a console (setup, checks, scheduled tasks, jobs): returns its output.</summary>
        public List<object> RunScript(string text, string file, Streams st)
        {
            var outp = new List<object>();
            var st2 = (st ?? new Streams()).WithOut(o => outp.Add(o));
            ScriptBlockAst ast;
            try { ast = PsParser.Parse(text, file); }
            catch (PsParseError e) { var r = ParseErrorRecord(e); AddError(r); st2.Err(r); LastOk = false; return outp; }
            CurrentStreams = st2;
            RunTop(ast, st2, null);
            return outp;
        }
        public int LastExit;
        public bool Terminated;     // the last run ended with an error that stopped it

        void RunTop(ScriptBlockAst ast, Streams st, Action afterStatement)
        {
            var oldCulture = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = PsConv.Us;
            Terminated = false;
            Steps = 0;
            try
            {
                var blocks = new[] { ast.Begin, ast.Process, ast.EndBlock ?? ast.Body };
                foreach (var b in blocks)
                {
                    if (b == null) continue;
                    foreach (var s in b.Stmts)
                    {
                        try { Exec(s, Global, st); }
                        catch (PsRuntimeException e)
                        {
                            if (e.Terminating) { AddErrorOnceTop(e.Rec); st.Err(e.Rec); Terminated = true; LastOk = false; return; }
                            AddErrorOnceTop(e.Rec); st.Err(e.Rec); LastOk = false;
                        }
                        if (afterStatement != null) afterStatement();
                    }
                }
            }
            catch (PsExitEx e) { LastExit = e.Code; SetVar("LASTEXITCODE", e.Code, Global); ExitRequested = true; }
            catch (PsFlowEx) { }
            finally { TryDepth = 0; CallDepth = 0; Thread.CurrentThread.CurrentCulture = oldCulture; }
        }
        public bool ExitRequested;
        void AddErrorOnceTop(PsErrorRecord r) { if (Errors.Count == 0 || Errors[0] != r) AddError(r); }

        /// <summary>The prompt: the session's prompt function if it has one, else "PS C:\path> ".</summary>
        public string PromptText()
        {
            string p = "PS " + LocationDisplay + "> ";
            var f = FindFunction("prompt", Global);
            if (f != null && f.Module == null && !IsDefaultPrompt(f))
            {
                try
                {
                    var l = new List<object>();
                    var sb = new PsScriptBlock { Ast = f.Ast };
                    l = InvokeBlock(sb, Global, null, null, false);
                    p = string.Concat(l.Select(x => PsConv.ToStr(x)).ToArray());
                    if (p.Length == 0) p = "PS>";
                }
                catch (PsRuntimeException) { p = "PS>"; }
            }
            if (RemoteFrom != null) p = "[" + M.Name + "]: " + p;
            return p;
        }
        static bool IsDefaultPrompt(PsFunction f) { return false; }
        public string LocationDisplay { get { return Location ?? "C:\\"; } }
    }
}
