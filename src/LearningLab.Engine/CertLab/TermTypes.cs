// Cert Lab - what a terminal talks to (moved from App Hub's TermView.cs, which stays with the screens): ITerm, a
// RHEL console (Session) or a PowerShell console (PsConsole), and Completion, what Tab offers.
using System;
using System.Collections.Generic;

namespace CertLab
{
    public class Completion
    {
        public int Start;
        public List<string> Words = new List<string>();
        public readonly HashSet<string> NoSpace = new HashSet<string>();
    }

    /// <summary>What a terminal talks to: a RHEL console (Session) or a PowerShell console (PsConsole).</summary>
    public interface ITerm
    {
        string Prompt { get; }
        bool Secret { get; }
        bool Waiting { get; }
        bool Busy { get; }
        bool Finished { get; }
        bool WaitingForBreak { get; }
        GrubScreen Grub { get; }        // the GRUB menu / editor while it is on the screen (RHEL consoles only), else null
        bool GrubKey(string key);
        void GrubType(string text);
        void GrubTick();
        bool ClearRequested { get; set; }
        EditRequest PendingEdit { get; }
        int Columns { set; }
        void Start();
        void Execute(string line);
        void Break();
        void Eof();
        string TakeOutput();
        Completion Candidates(string line);
        void EditDone(string result, bool saved);
        void AfterSubmit();
        void Dispose();
    }
}
