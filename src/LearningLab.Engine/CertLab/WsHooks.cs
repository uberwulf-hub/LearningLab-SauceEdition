// Cert Lab - where the AZ-800 course's world plugs into the simulated PowerShell machines: PsMachine.Ws points to the
// WsLab a machine belongs to (null on the PowerShell and MD-102 courses' machines), so the DNS client, routing and the
// Windows Server cmdlets (PsWs*.cs) use the lab's directory, DNS servers and roles.
namespace CertLab
{
    public partial class PsMachine
    {
        public WsLab Ws;
        /// <summary>Data volumes besides C: (letter -> root folder), for the AZ-800 file server and Hyper-V host.</summary>
        public readonly System.Collections.Generic.Dictionary<string, WinFile> ExtraDrives = new System.Collections.Generic.Dictionary<string, WinFile>(System.StringComparer.OrdinalIgnoreCase);
        public WsServer WsSrv { get { return Ws == null ? null : Ws.Server(Name); } }
    }
}
