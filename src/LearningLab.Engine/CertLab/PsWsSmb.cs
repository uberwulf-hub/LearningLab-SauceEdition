// Cert Lab - the SmbShare module on the AZ-800 machines: shares, their permissions (share-level: Full, Change, Read,
// or a deny), access-based enumeration, SMB encryption, and the SMB server's configuration (SMB 1.0 off).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public partial class PsCmdlets
    {
        const string SmbMod = "SmbShare";
        static Func<PsMachine, bool> HasWs = m => m.Ws != null;
        static CmdletDef SmbCmd(string name, string spec, Action<CmdCtx, object> run) { var d = Cdxml(Add(name, SmbMod, spec + "; CimSession:object; Force:switch", run)); d.Available = HasWs; return d; }
        static WsShare ShareOf(CmdCtx c)
        {
            var s = c.M.WsSrv; string n = c.Str("Name");
            var sh = s == null ? null : s.Share(n);
            if (sh == null) throw c.Fail("No MSFT_SMBShare objects found with property 'Name' equal to '" + n + "'. Verify the value of the property and retry.", "CmdletizationQuery_NotFound_Name,Get-SmbShare", "ObjectNotFound", n, "Microsoft.Management.Infrastructure.CimException");
            return sh;
        }
        static PsObj ShareObj(WsServer s, WsShare sh)
        {
            var p = new PsObj("Microsoft.Management.Infrastructure.CimInstance#ROOT/Microsoft/Windows/SMB/MSFT_SmbShare", "Microsoft.Management.Infrastructure.CimInstance");
            p.Val("Name", "string", sh.Name); p.Val("ScopeName", "string", "*"); p.Val("Path", "string", sh.Path); p.Val("Description", "string", sh.Description);
            p.Val("FolderEnumerationMode", "string", sh.FolderEnumerationMode); p.Val("EncryptData", "bool", sh.EncryptData); p.Val("CachingMode", "string", sh.CachingMode); p.Val("Special", "bool", sh.Special);
            p.Val("ShareState", "string", "Online"); p.Val("ConcurrentUserLimit", "uint32", sh.ConcurrentUserLimit); p.Val("CurrentUsers", "uint32", 0u);
            p.DefaultDisplay = new[] { "Name", "ScopeName", "Path", "Description" };
            return p;
        }
        static PsObj ShareAccessObj(WsShare sh, WsShareAce a)
        {
            var p = new PsObj("Microsoft.Management.Infrastructure.CimInstance#ROOT/Microsoft/Windows/SMB/MSFT_SmbShareAccessControlEntry", "Microsoft.Management.Infrastructure.CimInstance");
            p.Val("Name", "string", sh.Name); p.Val("ScopeName", "string", "*"); p.Val("AccountName", "string", a.Trustee.Contains("\\") || a.Trustee == "Everyone" ? a.Trustee : a.Trustee); p.Val("AccessControlType", "string", a.Allow ? "Allow" : "Deny"); p.Val("AccessRight", "string", a.Right);
            return p;
        }

        static void RegWsSmb()
        {
            PsFormat.Table("Microsoft.Management.Infrastructure.CimInstance#ROOT/Microsoft/Windows/SMB/MSFT_SmbShare", PsFormat.P("Name", 10), PsFormat.P("ScopeName", 9), PsFormat.P("Path", 28), PsFormat.P("Description", 20));
            PsFormat.Table("Microsoft.Management.Infrastructure.CimInstance#ROOT/Microsoft/Windows/SMB/MSFT_SmbShareAccessControlEntry", PsFormat.P("Name", 10), PsFormat.P("ScopeName", 9), PsFormat.P("AccountName", 26), PsFormat.P("AccessControlType", 17), PsFormat.P("AccessRight", 11));
            SmbCmd("Get-SmbShare", "Name:string[]@0; Special:bool", (c, o) =>
            {
                var s = c.M.WsSrv;
                foreach (var sh in s.Shares.Where(x => (!c.Has("Name") || c.Strs("Name").Any(n => WsText.Like(x.Name, n))) && (!c.Has("Special") || x.Special == PsConv.ToBool(c.Get("Special")))).OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)) c.Out(ShareObj(s, sh));
                if (c.Has("Name") && !c.Strs("Name").Any(n => n.Contains("*")) && !s.Shares.Any(x => WsText.Eq(x.Name, c.Strs("Name")[0]))) ShareOf(c);
            });
            SmbCmd("New-SmbShare", "Name:string@0 !; Path:string@1 !; Description:string; FullAccess:string[]; ChangeAccess:string[]; ReadAccess:string[]; NoAccess:string[]; FolderEnumerationMode:string set=AccessBased|Unrestricted; EncryptData:bool; CachingMode:string set=None|Manual|Documents|Programs|BranchCache; ConcurrentUserLimit:uint32; Temporary:switch", (c, o) =>
            {
                var s = c.M.WsSrv; string n = c.Str("Name"), path = c.R.FullFsPath(c.Str("Path"), c.R.CurrentFsDir);
                if (s.Share(n) != null) throw c.Fail("The name has already been shared.", "Windows System Error 2118,New-SmbShare", "NotSpecified", n, "Microsoft.Management.Infrastructure.CimException");
                var f = c.M.FindFile(path);
                if (f == null || !f.IsDir) throw c.Fail("The specified path is invalid: " + path, "Windows System Error 2116,New-SmbShare", "NotSpecified", path, "Microsoft.Management.Infrastructure.CimException");
                var sh = new WsShare { Name = n, Path = path, Description = c.Str("Description") ?? "", FolderEnumerationMode = c.Str("FolderEnumerationMode") ?? "Unrestricted", EncryptData = c.Has("EncryptData") && PsConv.ToBool(c.Get("EncryptData")), CachingMode = c.Str("CachingMode") ?? "Manual" };
                foreach (var kv in new[] { "FullAccess|Full", "ChangeAccess|Change", "ReadAccess|Read" }) { var p = kv.Split('|'); if (c.Has(p[0])) foreach (var a in c.Strs(p[0])) sh.Access.Add(new WsShareAce { Trustee = a, Right = p[1] }); }
                if (c.Has("NoAccess")) foreach (var a in c.Strs("NoAccess")) sh.Access.Add(new WsShareAce { Trustee = a, Right = "Full", Allow = false });
                if (sh.Access.Count == 0) sh.Access.Add(new WsShareAce { Trustee = "Everyone", Right = "Read" });
                s.Shares.Add(sh);
                c.Out(ShareObj(s, sh));
            });
            SmbCmd("Set-SmbShare", "Name:string[]@0 !; Description:string; FolderEnumerationMode:string set=AccessBased|Unrestricted; EncryptData:bool; CachingMode:string set=None|Manual|Documents|Programs|BranchCache; ConcurrentUserLimit:uint32; PassThru:switch", (c, o) =>
            {
                var sh = ShareOf(c);
                if (c.Has("Description")) sh.Description = c.Str("Description");
                if (c.Has("FolderEnumerationMode")) sh.FolderEnumerationMode = c.Str("FolderEnumerationMode");
                if (c.Has("EncryptData")) sh.EncryptData = PsConv.ToBool(c.Get("EncryptData"));
                if (c.Has("CachingMode")) sh.CachingMode = c.Str("CachingMode");
                if (c.Sw("PassThru")) c.Out(ShareObj(c.M.WsSrv, sh));
            });
            var rm = SmbCmd("Remove-SmbShare", "Name:string[]@0 !", (c, o) => { var sh = ShareOf(c); if (!c.Sw("Force") && !c.ShouldProcess(sh.Name, "Remove share")) return; c.M.WsSrv.Shares.Remove(sh); });
            rm.ShouldProcess = true; rm.ConfirmImpact = "High";
            foreach (var verb in new[] { "Grant", "Revoke", "Block", "Unblock" })
            {
                string v = verb;
                var d = SmbCmd(verb + "-SmbShareAccess", "Name:string[]@0 !; AccountName:string[] !" + (v == "Grant" ? "; AccessRight:string ! set=Full|Change|Read|Custom" : ""), (c, o) =>
                {
                    var sh = ShareOf(c);
                    if (!c.Sw("Force") && !c.ShouldProcess(sh.Name, v + " share access")) return;
                    foreach (var a in c.Strs("AccountName"))
                    {
                        bool allow = v == "Grant" || v == "Revoke";
                        sh.Access.RemoveAll(x => WsText.Eq(x.Trustee, a) && (v == "Revoke" ? x.Allow : v == "Grant" ? x.Allow : !x.Allow));
                        if (v == "Grant") sh.Access.Add(new WsShareAce { Trustee = a, Right = c.Str("AccessRight") });
                        if (v == "Block") sh.Access.Add(new WsShareAce { Trustee = a, Right = "Full", Allow = false });
                        GC.KeepAlive(allow);
                    }
                    foreach (var x in sh.Access) c.Out(ShareAccessObj(sh, x));
                });
                d.ShouldProcess = true;
            }
            SmbCmd("Get-SmbShareAccess", "Name:string[]@0 !", (c, o) => { var sh = ShareOf(c); foreach (var x in sh.Access) c.Out(ShareAccessObj(sh, x)); });
            SmbCmd("Get-SmbServerConfiguration", "", (c, o) =>
            {
                var s = c.M.WsSrv; var p = new PsObj("Microsoft.Management.Infrastructure.CimInstance#ROOT/Microsoft/Windows/SMB/MSFT_SmbServerConfiguration", "Microsoft.Management.Infrastructure.CimInstance");
                p.Val("EnableSMB1Protocol", "bool", s.Smb1); p.Val("EnableSMB2Protocol", "bool", true); p.Val("EncryptData", "bool", s.SmbEncrypt); p.Val("RejectUnencryptedAccess", "bool", true); p.Val("RequireSecuritySignature", "bool", s.IsDc); p.Val("EnableSecuritySignature", "bool", true); p.Val("AuditSmb1Access", "bool", false);
                c.Out(p);
            });
            SmbCmd("Set-SmbServerConfiguration", "EnableSMB1Protocol:bool; EncryptData:bool; RejectUnencryptedAccess:bool; RequireSecuritySignature:bool; AuditSmb1Access:bool", (c, o) =>
            {
                var s = c.M.WsSrv;
                if (!c.Sw("Force") && !c.ShouldProcess(s.Name, "Modify SMB server configuration")) return;
                if (c.Has("EnableSMB1Protocol")) s.Smb1 = PsConv.ToBool(c.Get("EnableSMB1Protocol"));
                if (c.Has("EncryptData")) s.SmbEncrypt = PsConv.ToBool(c.Get("EncryptData"));
            }).ShouldProcess = true;
        }
    }
}
