// Cert Lab - the RHEL console as a terminal (ITerm), so TermView can show it or a PowerShell console.
namespace CertLab
{
    public partial class Session : ITerm
    {
        GrubScreen ITerm.Grub { get { return Grub; } }
        bool ITerm.ClearRequested { get { return ClearRequested; } set { ClearRequested = value; } }
        EditRequest ITerm.PendingEdit { get { return PendingEdit; } }
        int ITerm.Columns { set { } }
        void ITerm.AfterSubmit() { if (M.Net != null) M.Net.PumpOthers(this); }
    }
}
