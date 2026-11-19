// Cert Lab - security in the simulated Windows PowerShell 5.1: execution policies (Get-/Set-ExecutionPolicy and the
// check before a script, module or profile runs: Restricted, AllSigned, RemoteSigned with the Zone.Identifier of
// downloaded files, Unrestricted, Bypass), Unblock-File, Authenticode signing with self-signed code signing
// certificates (New-SelfSignedCertificate, Export-/Import-Certificate, Set-/Get-AuthenticodeSignature), SecureString
// and credentials, and the JEA files and endpoints (New-PSSessionConfigurationFile, New-PSRoleCapabilityFile,
// Register-/Get-/Unregister-PSSessionConfiguration, Get-PSSessionCapability).
//
// Nothing here is real cryptography and nothing touches the real PC: a "signature" is a marked text block holding the
// signer's thumbprint and a simple hash of the script, an "encrypted" SecureString is the text mixed with a
// keystream made from the machine and user names (or the -Key bytes). They behave like the real ones in the course
// (a change breaks the signature, another user cannot read the string) but protect nothing.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    /// <summary>A registered PowerShell endpoint (session configuration), the built-in ones and JEA endpoints.</summary>
    public class PsEndpoint
    {
        public string Name = "", Path = "", SessionType = "Default", Permission = "", TranscriptDirectory = "", LanguageMode = "", RunAsUser = "";
        public bool RunAsVirtualAccount, Builtin;
        public IDictionary RoleDefinitions;      // principal -> @{ RoleCapabilities = ...; VisibleCmdlets = ... }
        public IDictionary File;                 // the .pssc as it was registered
    }

    /// <summary>One command a JEA role lets a user run: its name and, if limited, the allowed parameters and values.</summary>
    public class JeaRule
    {
        public string Name = "";
        public Dictionary<string, List<string>> Parameters;   // null: every parameter; a parameter -> null: any value
        public PsScriptBlock ScriptBlock;                      // FunctionDefinitions: the function's body
    }

    public partial class PsMachine
    {
        List<PsEndpoint> endpoints;
        public List<PsEndpoint> Endpoints
        {
            get
            {
                if (endpoints != null) return endpoints;
                endpoints = new List<PsEndpoint>();
                const string std = "NT AUTHORITY\\INTERACTIVE AccessAllowed, BUILTIN\\Administrators AccessAllowed, BUILTIN\\Remote Management Users AccessAllowed";
                endpoints.Add(new PsEndpoint { Name = "microsoft.powershell", Permission = std, Builtin = true });
                endpoints.Add(new PsEndpoint { Name = "microsoft.powershell.workflow", Permission = "BUILTIN\\Administrators AccessAllowed, BUILTIN\\Remote Management Users AccessAllowed", Builtin = true });
                endpoints.Add(new PsEndpoint { Name = "microsoft.powershell32", Permission = std, Builtin = true });
                if (IsServer) endpoints.Add(new PsEndpoint { Name = "microsoft.windows.servermanagerworkflows", Permission = "NT AUTHORITY\\INTERACTIVE AccessAllowed, BUILTIN\\Administrators AccessAllowed", Builtin = true });
                return endpoints;
            }
        }
        public PsEndpoint Endpoint(string name) { return Endpoints.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase)); }

        /// <summary>Is a user (LAB\alice or alice) a member of a group? Filled in by the part that models users and groups.</summary>
        public static Func<PsMachine, string, string, bool> IsMemberHook;

        static readonly string[] JeaAlways = { "Clear-Host", "Exit-PSSession", "Get-Command", "Get-FormatData", "Get-Help", "Measure-Object", "Out-Default", "Select-Object" };

        bool PrincipalMatches(string principal, string user)
        {
            string p = principal.Trim(), u = user.Trim();
            Func<string, string> bare = s => s.Contains("\\") ? s.Substring(s.IndexOf('\\') + 1) : s;
            if (p.Equals(u, StringComparison.OrdinalIgnoreCase) || bare(p).Equals(bare(u), StringComparison.OrdinalIgnoreCase) && (!p.Contains("\\") || !u.Contains("\\"))) return true;
            string g = bare(p);
            if (g.Equals("Everyone", StringComparison.OrdinalIgnoreCase) || g.Equals("Authenticated Users", StringComparison.OrdinalIgnoreCase) || g.Equals("Domain Users", StringComparison.OrdinalIgnoreCase) || p.Equals("BUILTIN\\Users", StringComparison.OrdinalIgnoreCase)) return true;
            return IsMemberHook != null && IsMemberHook(this, u, p);
        }

        /// <summary>
        /// The commands a user may run in a JEA endpoint: the RestrictedRemoteServer basics plus what the user's roles
        /// (role capability files in a module's RoleCapabilities folder) make visible. Null when the endpoint does not
        /// exist or gives this user no role (the connection is refused). Wildcards (Get-*) are kept as written.
        /// </summary>
        public List<string> EndpointCommands(string name, string user)
        {
            var rules = EndpointCommandRules(name, user);
            return rules == null ? null : rules.Select(r => r.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        public List<JeaRule> EndpointCommandRules(string name, string user)
        {
            var ep = Endpoint(name);
            if (ep == null) return null;
            var res = new List<JeaRule>();
            if (ep.Builtin || ep.RoleDefinitions == null)
            {
                if (ep.SessionType == "RestrictedRemoteServer") foreach (var c in JeaAlways) res.Add(new JeaRule { Name = c });
                return ep.Builtin || ep.SessionType != "RestrictedRemoteServer" ? null : res;
            }
            bool any = false;
            foreach (DictionaryEntry e in ep.RoleDefinitions)
            {
                if (!PrincipalMatches(PsConv.ToStr(e.Key), user)) continue;
                any = true;
                var def = PsConv.Base(e.Value) as IDictionary;
                if (def == null) continue;
                AddRules(res, def);
                var roles = PsModMgr.KeyList(def, "RoleCapabilities");
                if (roles != null) foreach (var role in roles) { var f = RoleCapabilityFile(role); if (f != null) AddRules(res, f); }
            }
            if (!any) return null;
            if (ep.SessionType == "RestrictedRemoteServer") res.InsertRange(0, JeaAlways.Select(c => new JeaRule { Name = c }));
            return res;
        }
        static void AddRules(List<JeaRule> res, IDictionary def)
        {
            foreach (var key in new[] { "VisibleCmdlets", "VisibleFunctions", "VisibleExternalCommands", "VisibleAliases" })
            {
                var v = PsModMgr.Key(def, key);
                if (v == null) continue;
                foreach (var x in PsConv.Items(v))
                {
                    var h = PsConv.Base(x) as IDictionary;
                    if (h == null) { res.Add(new JeaRule { Name = PsConv.ToStr(x) }); continue; }
                    var r = new JeaRule { Name = PsConv.ToStr(PsModMgr.Key(h, "Name")), Parameters = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase) };
                    foreach (var p in PsConv.Items(PsModMgr.Key(h, "Parameters")))
                    {
                        var ph = PsConv.Base(p) as IDictionary;
                        if (ph == null) continue;
                        var vs = PsModMgr.Key(ph, "ValidateSet");
                        r.Parameters[PsConv.ToStr(PsModMgr.Key(ph, "Name"))] = vs == null ? null : PsConv.Items(vs).Select(y => PsConv.ToStr(y)).ToList();
                    }
                    res.Add(r);
                }
            }
            var fd = PsModMgr.Key(def, "FunctionDefinitions");
            if (fd != null)
                foreach (var x in PsConv.Items(fd))
                {
                    var h = PsConv.Base(x) as IDictionary;
                    if (h != null) res.Add(new JeaRule { Name = PsConv.ToStr(PsModMgr.Key(h, "Name")), ScriptBlock = PsConv.Base(PsModMgr.Key(h, "ScriptBlock")) as PsScriptBlock });
                }
        }

        /// <summary>A role capability file: ROLE.psrc in the RoleCapabilities folder of a module in the machine's module path.</summary>
        public IDictionary RoleCapabilityFile(string role)
        {
            string mp; EnvMachine.TryGetValue("PSModulePath", out mp);
            foreach (var dir in (mp ?? "").Split(';').Where(x => x.Length > 0))
            {
                var d = FindFile(dir);
                if (d == null || !d.IsDir) continue;
                foreach (var mod in d.Kids.Values.Where(k => k.IsDir))
                {
                    var rc = mod.Kid("RoleCapabilities");
                    var f = rc == null ? null : rc.Kid(role + ".psrc");
                    if (f == null) foreach (var v in mod.Kids.Values.Where(k => k.IsDir)) { var rc2 = v.Kid("RoleCapabilities"); if (rc2 != null && rc2.Kid(role + ".psrc") != null) { f = rc2.Kid(role + ".psrc"); break; } }
                    if (f == null) continue;
                    var r = Sessions.FirstOrDefault() ?? new PsRunspace(this, new NullHost(), "Administrator");
                    bool temp = !Sessions.Contains(r);
                    try { string err; return PsModMgr.ReadData(r, f.FullPath, out err); }
                    finally { if (temp) Sessions.Remove(r); }
                }
            }
            return null;
        }
    }

    public static class PsSec
    {
        public const string SigBegin = "# SIG # Begin signature block", SigEnd = "# SIG # End signature block";
        public static readonly WinCert MicrosoftWindows = new WinCert { Store = "", Subject = "CN=Microsoft Windows, O=Microsoft Corporation, L=Redmond, S=Washington, C=US", Issuer = "CN=Microsoft Windows Production PCA 2011, O=Microsoft Corporation, L=Redmond, S=Washington, C=US", Thumbprint = "58FD671E2D4D200CE92D6E799EC70DF96E6D2664", CodeSigning = true, NotBefore = new DateTime(2025, 9, 1), NotAfter = new DateTime(2026, 9, 1) };

        public static ulong Fnv(string s)
        {
            ulong h = 14695981039346656037UL;
            foreach (char ch in s ?? "") { h ^= ch; h *= 1099511628211UL; }
            return h;
        }

        /// <summary>The signature block Set-AuthenticodeSignature appends (a stand-in: thumbprint, subject and a hash of the script).</summary>
        public static string Block(WinCert c, string body)
        {
            string payload = "CertLab simulated Authenticode signature|" + c.Thumbprint + "|" + c.Subject + "|" + Fnv(body).ToString("X16", CultureInfo.InvariantCulture);
            string b64 = Convert.ToBase64String(Encoding.ASCII.GetBytes(payload));
            var sb = new StringBuilder(SigBegin + "\r\n");
            for (int i = 0; i < b64.Length; i += 64) sb.Append("# ").Append(b64.Substring(i, Math.Min(64, b64.Length - i))).Append("\r\n");
            sb.Append(SigEnd + "\r\n");
            return sb.ToString();
        }

        /// <summary>A file's signature: status (Valid, NotSigned, HashMismatch, UnknownError, NotSupportedFileFormat) and signer.</summary>
        public static string Status(PsMachine m, WinFile f, out WinCert signer, out string message)
        {
            signer = null; message = "";
            string path = f.FullPath;
            if (f.Bytes != null) { signer = MicrosoftWindows; message = "Signature verified."; return "Valid"; }
            string ext = f.Name.Contains(".") ? f.Name.Substring(f.Name.LastIndexOf('.')).ToLowerInvariant() : "";
            if (ext != ".ps1" && ext != ".psm1" && ext != ".psd1" && ext != ".ps1xml" && ext != ".psc1" && ext != ".cdxml" && ext != ".mof")
            {
                message = "The form specified for the subject is not one supported or known by the specified trust provider";
                return "UnknownError";
            }
            int i = f.Text.IndexOf(SigBegin, StringComparison.Ordinal);
            if (i < 0) { message = "The file " + path + " is not digitally signed. You cannot run this script on the current system. For more information about running scripts and setting execution policy, see about_Execution_Policies at https:/go.microsoft.com/fwlink/?LinkID=135170."; return "NotSigned"; }
            string body = f.Text.Substring(0, i);
            int j = f.Text.IndexOf(SigEnd, i, StringComparison.Ordinal);
            string[] parts = null;
            try
            {
                var b64 = string.Concat(f.Text.Substring(i + SigBegin.Length, (j < 0 ? f.Text.Length : j) - i - SigBegin.Length).Replace("\r", "").Split('\n').Select(l => l.Trim().TrimStart('#').Trim()).ToArray());
                parts = Encoding.ASCII.GetString(Convert.FromBase64String(b64)).Split('|');
            }
            catch (FormatException) { }
            string mismatch = "The contents of file " + path + " might have been changed by an unauthorized user or process, because the hash of the file does not match the hash stored in the digital signature. The script cannot run on the specified system. For more information, run Get-Help about_Signing.";
            if (parts == null || parts.Length < 4 || j < 0) { message = mismatch; return "HashMismatch"; }
            signer = m.Certs.FirstOrDefault(c => c.Thumbprint == parts[1]) ?? new WinCert { Thumbprint = parts[1], Subject = parts[2], Issuer = parts[2], CodeSigning = true };
            string after = f.Text.Substring(j + SigEnd.Length).Trim();
            if (after.Length > 0 || parts[3] != Fnv(body).ToString("X16", CultureInfo.InvariantCulture)) { message = mismatch; return "HashMismatch"; }
            string thumb = parts[1];
            if (!m.Certs.Any(c => c.Thumbprint == thumb && c.Store.EndsWith("\\Root", StringComparison.OrdinalIgnoreCase)))
            {
                message = "A certificate chain processed, but terminated in a root certificate which is not trusted by the trust provider.";
                return "UnknownError";
            }
            message = "Signature verified.";
            return "Valid";
        }

        static int Zone(WinFile f)
        {
            string z;
            if (!f.Streams.TryGetValue("Zone.Identifier", out z)) return 0;
            foreach (var l in z.Replace("\r", "").Split('\n')) if (l.Trim().StartsWith("ZoneId=", StringComparison.OrdinalIgnoreCase)) { int n; if (int.TryParse(l.Trim().Substring(7), out n)) return n; }
            return 0;
        }

        /// <summary>The execution policy check before a script, module or profile runs (null: it may run).</summary>
        public static string Check(PsRunspace R, string path)
        {
            var m = R.M;
            string pol = m.EffectivePolicy(R.ProcessPolicy);
            if (pol == "Bypass" || pol == "Unrestricted") return null;
            if (pol == "Restricted") return "File " + path + " cannot be loaded because running scripts is disabled on this system. For more information, see about_Execution_Policies at https:/go.microsoft.com/fwlink/?LinkID=135170.";
            var f = m.FindFile(path);
            if (f == null) return null;
            if (pol == "RemoteSigned" && Zone(f) < 3) return null;
            WinCert signer; string msg;
            string st = Status(m, f, out signer, out msg);
            if (st != "Valid") return "File " + path + " cannot be loaded. " + msg;
            if (f.Bytes != null) return null;
            if (m.Certs.Any(c => c.Thumbprint == signer.Thumbprint && c.Store.EndsWith("\\Disallowed", StringComparison.OrdinalIgnoreCase)))
                return "File " + path + " cannot be loaded. The file " + path + " is published by " + signer.Subject + ". This publisher is explicitly not trusted on your system. The script will not run on the system. For more information, run the command \"get-help about_signing\".";
            if (m.Certs.Any(c => c.Thumbprint == signer.Thumbprint && c.Store.EndsWith("\\TrustedPublisher", StringComparison.OrdinalIgnoreCase))) return null;
            // a valid signature from a publisher this computer does not know yet: ask
            string answer = "D";
            if (R.Host != null && R.Host.Interactive)
            {
                R.HostWrite("\nDo you want to run software from this untrusted publisher?\nFile " + path + " is published by " + signer.Subject + " and is not trusted on your system. Only run scripts from trusted publishers.\n");
                for (int i = 0; i < 5; i++)
                {
                    var a = R.Host.ReadLine("[V] Never run  [D] Do not run  [R] Run once  [A] Always run  [?] Help (default is \"D\"): ", false);
                    a = (a ?? "").Trim().ToUpperInvariant();
                    if (a.Length == 0) a = "D";
                    if (a == "V" || a == "D" || a == "R" || a == "A") { answer = a; break; }
                    R.HostWrite("V - Never run: the publisher is added to the Untrusted Certificates store and no script it signs runs.\nD - Do not run this time.\nR - Run this script once.\nA - Always run: the publisher is added to the Trusted Publishers store.\n");
                }
            }
            if (answer == "R") return null;
            if (answer == "A" || answer == "V")
            {
                var src = m.Certs.FirstOrDefault(c => c.Thumbprint == signer.Thumbprint) ?? signer;
                m.Certs.Add(CopyCert(src, "CurrentUser\\" + (answer == "A" ? "TrustedPublisher" : "Disallowed")));
                if (answer == "A") return null;
            }
            return "File " + path + " cannot be loaded. The file " + path + " is published by " + signer.Subject + " and is not trusted on your system. Only run scripts from trusted publishers.";
        }

        public static WinCert CopyCert(WinCert c, string store)
        {
            return new WinCert { Store = store, Subject = c.Subject, Issuer = c.Issuer, Thumbprint = c.Thumbprint, FriendlyName = c.FriendlyName, NotBefore = c.NotBefore, NotAfter = c.NotAfter, HasPrivateKey = false, CodeSigning = c.CodeSigning, EnhancedKeyUsage = c.EnhancedKeyUsage.ToList() };
        }

        // ------------------------------------------------------------ SecureString stand-ins
        public const string DpapiHeader = "01000000d08c9ddf0115d1118c7a00c04fc297eb01000000";
        public const string AesHeader = "76492d1116743f0423413b16050a5345";

        static byte[] Stream(ulong seed, int n)
        {
            var b = new byte[n];
            ulong x = seed | 1;
            for (int i = 0; i < n; i++) { x ^= x << 13; x ^= x >> 7; x ^= x << 17; b[i] = (byte)(x >> 24); }
            return b;
        }
        static byte[] Mix(byte[] data, ulong seed) { var k = Stream(seed, data.Length); var r = new byte[data.Length]; for (int i = 0; i < data.Length; i++) r[i] = (byte)(data[i] ^ k[i]); return r; }
        static string Hex(byte[] b) { var sb = new StringBuilder(); foreach (var x in b) sb.Append(x.ToString("x2", CultureInfo.InvariantCulture)); return sb.ToString(); }
        static byte[] FromHex(string s)
        {
            if (s.Length % 2 != 0) throw new FormatException();
            var b = new byte[s.Length / 2];
            for (int i = 0; i < b.Length; i++) b[i] = byte.Parse(s.Substring(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return b;
        }
        static byte[] Payload(string plain) { var t = Encoding.Unicode.GetBytes(plain); var h = BitConverter.GetBytes((uint)Fnv(plain)); return h.Concat(t).ToArray(); }
        static string Unpayload(byte[] p)
        {
            if (p.Length < 4) return null;
            var t = Encoding.Unicode.GetString(p, 4, p.Length - 4);
            return BitConverter.ToUInt32(p, 0) == (uint)Fnv(t) ? t : null;
        }

        /// <summary>ConvertFrom-SecureString without a key: only the same user on the same machine can read it back.</summary>
        public static string ToDpapi(PsRunspace R, string plain)
        {
            var salt = new byte[16]; R.M.Rng.NextBytes(salt);
            ulong seed = Fnv(R.M.Name.ToUpperInvariant() + "\\" + R.User.ToUpperInvariant()) ^ BitConverter.ToUInt64(salt, 0);
            return DpapiHeader + "000000" + Hex(salt) + Hex(Mix(Payload(plain), seed)) + Hex(Stream(seed ^ 0x5bd1e995, 20));
        }
        public static string FromDpapi(PsRunspace R, string s)
        {
            string body = s.Substring(DpapiHeader.Length + 6);
            if (body.Length < 72) return null;
            var salt = FromHex(body.Substring(0, 32));
            var data = FromHex(body.Substring(32, body.Length - 32 - 40));
            ulong seed = Fnv(R.M.Name.ToUpperInvariant() + "\\" + R.User.ToUpperInvariant()) ^ BitConverter.ToUInt64(salt, 0);
            return Unpayload(Mix(data, seed));
        }
        public static string ToKeyed(PsRunspace R, string plain, byte[] key)
        {
            var iv = new byte[16]; R.M.Rng.NextBytes(iv);
            ulong seed = Fnv(Hex(key)) ^ BitConverter.ToUInt64(iv, 0);
            string inner = "2|" + Convert.ToBase64String(iv) + "|" + Hex(Mix(Payload(plain), seed));
            return AesHeader + Convert.ToBase64String(Encoding.Unicode.GetBytes(inner));
        }
        public static string FromKeyed(string s, byte[] key)
        {
            var inner = Encoding.Unicode.GetString(Convert.FromBase64String(s.Substring(AesHeader.Length))).Split('|');
            if (inner.Length != 3) throw new FormatException();
            var iv = Convert.FromBase64String(inner[1]);
            ulong seed = Fnv(Hex(key)) ^ BitConverter.ToUInt64(iv, 0);
            return Unpayload(Mix(FromHex(inner[2]), seed));
        }
    }

    public partial class PsCmdlets
    {
        public const string SecMod = "Microsoft.PowerShell.Security", PkiMod = "PKI";
        const string X509 = "System.Security.Cryptography.X509Certificates.X509Certificate2";

        public static PsObj CertObj(WinCert c)
        {
            var o = new PsObj(X509, "System.Security.Cryptography.X509Certificates.X509Certificate", "System.Object");
            string parent = "Microsoft.PowerShell.Security\\Certificate::" + c.Store;
            o.Val("PSPath", "string", parent + "\\" + c.Thumbprint);
            o.Val("PSParentPath", "string", parent);
            o.Val("PSChildName", "string", c.Thumbprint);
            o.Val("PSIsContainer", "bool", false);
            o.Val("EnhancedKeyUsageList", "object[]", c.EnhancedKeyUsage.Cast<object>().ToArray());
            o.Val("DnsNameList", "object[]", new object[] { c.Subject.StartsWith("CN=", StringComparison.Ordinal) ? c.Subject.Substring(3).Split(',')[0] : c.Subject });
            o.Val("Archived", "bool", false);
            o.Val("FriendlyName", "string", c.FriendlyName);
            o.Val("HasPrivateKey", "bool", c.HasPrivateKey);
            o.Val("Issuer", "string", c.Issuer);
            o.Val("NotAfter", "datetime", c.NotAfter);
            o.Val("NotBefore", "datetime", c.NotBefore);
            o.Val("SerialNumber", "string", c.Thumbprint.Substring(0, 32));
            o.Val("Subject", "string", c.Subject);
            o.Val("Thumbprint", "string", c.Thumbprint);
            o.Val("Version", "int", 3);
            o.Val("SignatureAlgorithm", "string", "sha256RSA");
            o.ToStr = x => "[Subject]\n  " + c.Subject + "\n\n[Issuer]\n  " + c.Issuer + "\n\n[Serial Number]\n  " + c.Thumbprint.Substring(0, 32) + "\n\n[Not Before]\n  " + c.NotBefore.ToString(PsConv.Us) + "\n\n[Not After]\n  " + c.NotAfter.ToString(PsConv.Us) + "\n\n[Thumbprint]\n  " + c.Thumbprint + "\n";
            o.Base = c;
            return o;
        }

        static WinCert CertOf(CmdCtx c, object v)
        {
            var po = PsConv.Base(v) as PsObj;
            if (po != null && po.Base is WinCert) return (WinCert)po.Base;
            string thumb = po != null ? PsConv.ToStr(po.Get("Thumbprint")) : PsConv.ToStr(v);
            return c.M.Certs.FirstOrDefault(x => x.Thumbprint.Equals(thumb ?? "", StringComparison.OrdinalIgnoreCase));
        }

        static PsObj SignatureObj(PsMachine m, WinFile f)
        {
            WinCert signer; string msg;
            string st = PsSec.Status(m, f, out signer, out msg);
            var o = new PsObj("System.Management.Automation.Signature", "System.Object");
            o.Val("SignerCertificate", X509, signer == null ? null : CertObj(signer));
            o.Val("TimeStamperCertificate", X509, null);
            o.Val("Status", "System.Management.Automation.SignatureStatus", PsEnums.V("System.Management.Automation.SignatureStatus", st));
            o.Val("StatusMessage", "string", msg);
            o.Val("Path", "string", f.FullPath);
            o.Val("SignatureType", "System.Management.Automation.SignatureType", PsEnums.V("System.Management.Automation.SignatureType", signer == null ? "None" : f.Bytes != null ? "Catalog" : "Authenticode"));
            o.Val("IsOSBinary", "bool", f.Bytes != null);
            o.ToStr = x => f.FullPath;
            return o;
        }

        public static PsObj FileInfoObj(WinFile f)
        {
            var o = new PsObj("System.IO.FileInfo", "System.IO.FileSystemInfo", "System.MarshalByRefObject", "System.Object");
            string dir = f.Parent == null ? "C:\\" : f.Parent.FullPath;
            o.Val("PSPath", "string", "Microsoft.PowerShell.Core\\FileSystem::" + f.FullPath);
            o.Val("PSParentPath", "string", "Microsoft.PowerShell.Core\\FileSystem::" + dir);
            o.Val("PSChildName", "string", f.Name);
            o.Val("PSIsContainer", "bool", false);
            o.Val("Mode", "string", f.Mode);
            o.Val("Name", "string", f.Name);
            o.Val("Length", "long", f.Length);
            o.Val("DirectoryName", "string", dir);
            o.Val("FullName", "string", f.FullPath);
            o.Val("Extension", "string", f.Name.Contains(".") ? f.Name.Substring(f.Name.LastIndexOf('.')) : "");
            o.Val("CreationTime", "datetime", f.Created);
            o.Val("LastWriteTime", "datetime", f.Modified);
            o.Val("LastAccessTime", "datetime", f.Accessed);
            o.ToStr = x => f.FullPath;
            o.Base = f;
            return o;
        }

        static string StoreOf(string loc, out string err)
        {
            err = null;
            string s = (loc ?? "Cert:\\LocalMachine\\My").Trim();
            if (s.StartsWith("Cert:", StringComparison.OrdinalIgnoreCase)) s = s.Substring(5);
            s = s.Trim('\\');
            var p = s.Split('\\');
            if (p.Length != 2 || !(p[0].Equals("CurrentUser", StringComparison.OrdinalIgnoreCase) || p[0].Equals("LocalMachine", StringComparison.OrdinalIgnoreCase))) { err = "Cannot find path '" + loc + "' because it does not exist."; return null; }
            string root = p[0].Equals("CurrentUser", StringComparison.OrdinalIgnoreCase) ? "CurrentUser" : "LocalMachine";
            var known = new[] { "My", "Root", "TrustedPublisher", "CA", "Disallowed", "TrustedPeople", "AuthRoot", "Remote Desktop", "WebHosting" };
            var st = known.FirstOrDefault(k => k.Equals(p[1], StringComparison.OrdinalIgnoreCase));
            if (st == null) { err = "Cannot find path '" + loc + "' because it does not exist."; return null; }
            return root + "\\" + st;
        }

        static IEnumerable<WinFile> FilesFor(CmdCtx c, string pathParam, object o)
        {
            var R = c.R;
            var paths = new List<string>();
            if (c.HasInput && o != null && !(PsConv.Base(o) is string))
            {
                var po = PsConv.Base(o) as PsObj;
                if (po != null && po.Base is WinFile) { yield return (WinFile)po.Base; yield break; }
            }
            foreach (var p in c.Strs(pathParam) ?? c.Strs("LiteralPath") ?? new string[0]) paths.Add(p);
            foreach (var p in paths)
            {
                string full = R.FullFsPath(p, R.CurrentFsDir);
                if (PsConv.HasWildcard(full.Substring(full.LastIndexOf('\\') + 1)) && pathParam != "LiteralPath")
                {
                    var d = R.FsNode(full.Substring(0, full.LastIndexOf('\\')));
                    var leaf = full.Substring(full.LastIndexOf('\\') + 1);
                    var hits = d == null ? new List<WinFile>() : d.Kids.Values.Where(k => !k.IsDir && PsConv.Like(k.Name, leaf, false)).OrderBy(k => k.Name, StringComparer.OrdinalIgnoreCase).ToList();
                    foreach (var h in hits) yield return h;
                    continue;
                }
                var f = R.FsNode(full);
                if (f == null || f.IsDir)
                {
                    var r = c.MakeError("Cannot find path '" + full + "' because it does not exist.", "FileNotFound", "ObjectNotFound", full, "System.Management.Automation.ItemNotFoundException");
                    c.WriteError(r);
                    continue;
                }
                yield return f;
            }
        }

        static void WriteDataFile(CmdCtx c, string path, string text, string what)
        {
            string full = c.R.FullFsPath(path, c.R.CurrentFsDir);
            if (!c.ShouldProcess(full, what)) return;
            string why = c.R.WriteFile(full, text, "unicode", false);
            if (why != null) c.WriteError(c.MakeError(why, "System.IO.DirectoryNotFoundException", "NotSpecified", full, "System.IO.DirectoryNotFoundException"));
        }

        /// <summary>A value as PowerShell source text, for the files New-PSSessionConfigurationFile and friends write.</summary>
        public static string Lit(object v, string indent)
        {
            v = PsConv.Base(v);
            if (v == null) return "$null";
            if (v is bool) return (bool)v ? "$true" : "$false";
            if (v is PsSwitch) return PsConv.ToBool(v) ? "$true" : "$false";
            if (PsConv.IsNumber(v)) return PsConv.ToStr(v);
            if (v is PsScriptBlock) return "{" + ((PsScriptBlock)v).Text + "}";
            var d = v as IDictionary;
            if (d != null)
            {
                var sb = new StringBuilder("@{\r\n");
                var parts = new List<string>();
                foreach (DictionaryEntry e in d) parts.Add(indent + "    '" + PsConv.ToStr(e.Key).Replace("'", "''") + "' = " + Lit(e.Value, indent + "    "));
                sb.Append(string.Join("\r\n", parts.ToArray())).Append(" }");
                return sb.ToString();
            }
            if (PsConv.IsCollection(v))
            {
                var l = PsConv.List(v);
                if (l.Count == 0) return "@()";
                return string.Join(", ", l.Select(x => Lit(x, indent)).ToArray());
            }
            return "'" + PsConv.ToStr(v).Replace("'", "''") + "'";
        }

        static PsErrorRecord WinRmDown(CmdCtx c)
        {
            return c.MakeError("The client cannot connect to the destination specified in the request. Verify that the service on the destination is running and is accepting requests. Consult the logs and documentation for the WS-Management service running on the destination, most commonly IIS or WinRM. If the destination is the WinRM service, run the following command on the destination to analyze and configure the WinRM service: \"winrm quickconfig\".", "WinRMOperationFailed", "InvalidOperation", null, "System.InvalidOperationException");
        }

        static PsObj EndpointObj(PsEndpoint e)
        {
            var o = new PsObj("Microsoft.PowerShell.Commands.PSSessionConfigurationCommands#PSSessionConfiguration", "System.Management.Automation.PSCustomObject", "System.Object");
            o.Val("Name", "string", e.Name);
            o.Val("PSVersion", "string", "5.1");
            o.Val("StartupScript", "string", "");
            o.Val("RunAsUser", "string", e.RunAsUser);
            o.Val("Permission", "string", e.Permission);
            o.Val("SessionType", "string", e.SessionType);
            o.Val("RunAsVirtualAccount", "string", e.RunAsVirtualAccount ? "true" : "false");
            o.Val("TranscriptDirectory", "string", e.TranscriptDirectory);
            o.Val("RoleDefinitions", "object", e.RoleDefinitions);
            o.Val("ConfigFilePath", "string", e.Path);
            o.Val("Enabled", "string", "true");
            o.Val("Architecture", "string", e.Name == "microsoft.powershell32" ? "32" : "64");
            o.ToStr = x => e.Name;
            o.Base = e;
            return o;
        }

        static partial void RegisterSecurity()
        {
            PsEnums.Define("Microsoft.PowerShell.ExecutionPolicy", "Unrestricted=0,RemoteSigned=1,AllSigned=2,Restricted=3,Default=6,Bypass=4,Undefined=5");
            PsEnums.Define("System.Management.Automation.SignatureStatus", "Valid=0,UnknownError=1,NotSigned=2,HashMismatch=3,NotTrusted=4,NotSupportedFileFormat=5,Incompatible=6");
            PsEnums.Define("System.Management.Automation.SignatureType", "None=0,Authenticode=1,Catalog=2");
            PsSecurityHook.Check = PsSec.Check;
            PsFormat.Table("Microsoft.PowerShell.ExecutionPolicyList", PsFormat.PR("Scope", 13), PsFormat.PR("ExecutionPolicy", 15));
            // The certificate view (Thumbprint / Subject grouped by PSParentPath) is registered with the Cert: drive (PsCmdItems.cs).
            var sig = new FmtView
            {
                Cols = new List<FmtCol> { PsFormat.Col("SignerCertificate", 41, false, o => Gv(Gv(o, "SignerCertificate"), "Thumbprint")), PsFormat.P("Status", 38), PsFormat.Col("Path", 0, false, o => { var p = PsConv.ToStr(Gv(o, "Path")); return p.Substring(p.LastIndexOf('\\') + 1); }) },
                GroupKey = o => { var p = PsConv.ToStr(Gv(o, "Path")); return p.Substring(0, Math.Max(0, p.LastIndexOf('\\'))); },
                GroupHeader = o => { var p = PsConv.ToStr(Gv(o, "Path")); return "    Directory: " + p.Substring(0, Math.Max(0, p.LastIndexOf('\\'))); }
            };
            PsFormat.View("System.Management.Automation.Signature", sig);
            ListView("Microsoft.PowerShell.Commands.PSSessionConfigurationCommands#PSSessionConfiguration", PsFormat.P("Name", 0), PsFormat.P("PSVersion", 0), PsFormat.P("StartupScript", 0), PsFormat.P("RunAsUser", 0), PsFormat.P("Permission", 0));
            PsFormat.View("Microsoft.WSMan.Management.WSManConfigContainerElement", new FmtView { Cols = new List<FmtCol> { PsFormat.P("Type", 15), PsFormat.Col("Keys", 35, false, o => "{" + string.Join(", ", PsConv.Items(Gv(o, "Keys")).Select(x => PsConv.ToStr(x)).ToArray()) + "}"), PsFormat.P("Name", 0) }, GroupKey = o => Gv(o, "PSParentPath") as string, GroupHeader = o => "   WSManConfig: " + PsConv.ToStr(Gv(o, "PSParentPath")) });

            // ---------------------------------------------------- execution policy
            Add("Get-ExecutionPolicy", SecMod, "Scope:ExecutionPolicyScope@0; List:switch", (c, o) =>
            {
                var m = c.M;
                Func<string, object> pol = v => PsEnums.V("Microsoft.PowerShell.ExecutionPolicy", string.IsNullOrEmpty(v) ? "Undefined" : v);
                if (c.Sw("List"))
                {
                    foreach (var kv in new[] { new[] { "MachinePolicy", m.ExecPolicyGpo }, new[] { "UserPolicy", null }, new[] { "Process", c.R.ProcessPolicy }, new[] { "CurrentUser", m.ExecPolicyUser }, new[] { "LocalMachine", m.ExecPolicyMachine } })
                    {
                        var x = PsObj.Custom();
                        x.TypeNames.Insert(1, "Microsoft.PowerShell.ExecutionPolicyList");
                        x.Add("Scope", PsEnums.V("Microsoft.PowerShell.ExecutionPolicyScope", kv[0]));
                        x.Add("ExecutionPolicy", pol(kv[1]));
                        c.Out(x);
                    }
                    return;
                }
                if (c.Has("Scope"))
                {
                    switch (c.Str("Scope"))
                    {
                        case "MachinePolicy": c.Out(pol(m.ExecPolicyGpo)); return;
                        case "UserPolicy": c.Out(pol(null)); return;
                        case "Process": c.Out(pol(c.R.ProcessPolicy)); return;
                        case "CurrentUser": c.Out(pol(m.ExecPolicyUser)); return;
                        case "LocalMachine": c.Out(pol(m.ExecPolicyMachine)); return;
                    }
                }
                c.Out(pol(m.EffectivePolicy(c.R.ProcessPolicy)));
            });
            var sep = Add("Set-ExecutionPolicy", SecMod, "ExecutionPolicy:ExecutionPolicy@0 ! prop; Scope:ExecutionPolicyScope@1 prop; Force:switch", (c, o) =>
            {
                var m = c.M;
                string pol = c.Str("ExecutionPolicy");
                if (pol == "Default") pol = m.IsServer ? "RemoteSigned" : "Restricted";
                string scope = c.Str("Scope", "LocalMachine");
                if (scope == "MachinePolicy" || scope == "UserPolicy")
                {
                    throw c.Fail("Cannot set execution policy. Execution policies at the MachinePolicy or UserPolicy scopes must be set through Group Policy.", "CantSetGroupPolicy", "InvalidOperation", null, "System.ArgumentException");
                }
                if (c.WhatIf) { c.Host("What if: Performing the operation \"Set-ExecutionPolicy\" on target \"" + pol + "\"."); return; }
                if (!c.Sw("Force") && !c.Data.ContainsKey("ConfirmFalse"))
                {
                    var a = c.R.AskChoice("Execution Policy Change", "The execution policy helps protect you from scripts that you do not trust. Changing the execution policy might expose you to the security risks described in the about_Execution_Policies help topic at https:/go.microsoft.com/fwlink/?LinkID=135170. Do you want to change the execution policy?", c.R.Host != null && c.R.Host.Interactive ? "N" : "Y");
                    if (a != "Y" && a != "A") return;
                }
                switch (scope)
                {
                    case "Process": c.R.ProcessPolicy = pol; break;
                    case "CurrentUser": m.ExecPolicyUser = pol; break;
                    default: m.ExecPolicyMachine = pol; break;
                }
                // a more specific scope still wins
                string eff = m.EffectivePolicy(c.R.ProcessPolicy);
                if (pol != "Undefined" && eff != pol)
                {
                    var r = c.MakeError("Windows PowerShell updated your execution policy successfully, but the setting is overridden by a policy defined at a more specific scope.  Due to the override, your shell will retain its current effective execution policy of " + eff + ". Type \"Get-ExecutionPolicy -List\" to view your execution policy settings. For more information please see \"Get-Help Set-ExecutionPolicy\".", "ExecutionPolicyOverride", "PermissionDenied", null, "System.Security.SecurityException");
                    c.WriteError(r);
                }
            });
            sep.ShouldProcess = true;
            Add("Unblock-File", Util, "Path:string[]@0 ! ps=ByPath; LiteralPath:string[] ! pipe prop alias=PSPath,LP ps=ByLiteralPath", (c, o) =>
            {
                foreach (var f in FilesFor(c, c.Has("Path") ? "Path" : "LiteralPath", o).ToList())
                    if (c.ShouldProcess(f.FullPath, "Unblock-File")) f.Streams.Remove("Zone.Identifier");
            }).ShouldProcess = true;

            // ---------------------------------------------------- certificates and signing
            Add("New-SelfSignedCertificate", PkiMod, "Subject:string; DnsName:string[]; Type:string set=Custom|CodeSigningCert|DocumentEncryptionCert|SSLServerAuthentication|DocumentEncryptionCertLegacyCsp; CertStoreLocation:string; FriendlyName:string; NotAfter:datetime; NotBefore:datetime; KeyUsage:string[]; KeyAlgorithm:string; KeyLength:int; KeyExportPolicy:string[]; HashAlgorithm:string; TextExtension:string[]; KeySpec:string; Provider:string; Signer:object", (c, o) =>
            {
                var m = c.M;
                if (!c.Has("Subject") && !c.Has("DnsName")) throw c.Fail("At least one of the Subject or DnsName parameters must be given.", "System.ArgumentException,Microsoft.CertificateServices.Commands.NewSelfSignedCertificateCommand", "InvalidArgument", null, "System.ArgumentException");
                string err;
                string store = StoreOf(c.Str("CertStoreLocation", "Cert:\\LocalMachine\\My"), out err);
                if (store == null) throw c.Fail(err, "PathNotFound", "ObjectNotFound", c.Str("CertStoreLocation"), "System.Management.Automation.ItemNotFoundException");
                if (!store.EndsWith("\\My", StringComparison.Ordinal)) throw c.Fail("New-SelfSignedCertificate creates certificates in a personal store only: use Cert:\\CurrentUser\\My or Cert:\\LocalMachine\\My.", "System.ArgumentException,Microsoft.CertificateServices.Commands.NewSelfSignedCertificateCommand", "InvalidArgument", null, "System.ArgumentException");
                string subj = c.Str("Subject") ?? "CN=" + c.Strs("DnsName")[0];
                if (!subj.Contains("=")) subj = "CN=" + subj;
                string type = c.Str("Type", "SSLServerAuthentication");
                var cert = new WinCert { Store = store, Subject = subj, Issuer = subj, Thumbprint = m.RandomHex(40).ToUpperInvariant(), FriendlyName = c.Str("FriendlyName") ?? "", HasPrivateKey = true, NotBefore = c.Has("NotBefore") ? (DateTime)c.Get("NotBefore") : m.Now.AddMinutes(-10), NotAfter = c.Has("NotAfter") ? (DateTime)c.Get("NotAfter") : m.Now.AddYears(1).AddMinutes(-10) };
                if (type == "CodeSigningCert") { cert.CodeSigning = true; cert.EnhancedKeyUsage.Add("Code Signing (1.3.6.1.5.5.7.3.3)"); }
                else if (type.StartsWith("DocumentEncryption", StringComparison.Ordinal)) cert.EnhancedKeyUsage.Add("Document Encryption (1.3.6.1.4.1.311.80.1)");
                else if (type == "SSLServerAuthentication") { cert.EnhancedKeyUsage.Add("Client Authentication (1.3.6.1.5.5.7.3.2)"); cert.EnhancedKeyUsage.Add("Server Authentication (1.3.6.1.5.5.7.3.1)"); }
                if (c.Has("TextExtension") && c.Strs("TextExtension").Any(t => t.Contains("1.3.6.1.5.5.7.3.3"))) { cert.CodeSigning = true; if (!cert.EnhancedKeyUsage.Any(e => e.Contains("3.3)"))) cert.EnhancedKeyUsage.Add("Code Signing (1.3.6.1.5.5.7.3.3)"); }
                m.Certs.Add(cert);
                c.Out(CertObj(cert));
            }).FqClass = "Microsoft.CertificateServices.Commands.NewSelfSignedCertificateCommand";
            Add("Export-Certificate", PkiMod, "Cert:object@0 ! pipe; FilePath:string@1 !; Type:string set=CERT|P7B|SST; NoClobber:switch; Force:switch", (c, o) =>
            {
                var w = CertOf(c, c.HasInput ? o : c.Get("Cert"));
                if (w == null) throw c.Fail("The certificate was not found.", "System.ArgumentException,Microsoft.CertificateServices.Commands.ExportCertificateCommand", "InvalidArgument", null, "System.ArgumentException");
                string full = c.R.FullFsPath(c.Str("FilePath"), c.R.CurrentFsDir);
                string text = "CertLab simulated certificate file (a real .cer file is binary)\r\n" + Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join("|", new[] { w.Thumbprint, w.Subject, w.Issuer, w.NotBefore.ToString("o", CultureInfo.InvariantCulture), w.NotAfter.ToString("o", CultureInfo.InvariantCulture), w.CodeSigning ? "1" : "0", string.Join(";", w.EnhancedKeyUsage.ToArray()) }))) + "\r\n";
                string why = c.R.WriteFile(full, text, "ascii", false);
                if (why != null) throw c.Fail(why, "System.IO.DirectoryNotFoundException,Microsoft.CertificateServices.Commands.ExportCertificateCommand", "NotSpecified", null, "System.IO.DirectoryNotFoundException");
                c.Out(FileInfoObj(c.R.FsNode(full)));
            });
            Add("Import-Certificate", PkiMod, "FilePath:string@0 ! pipe prop alias=FullName; CertStoreLocation:string", (c, o) =>
            {
                var m = c.M;
                string full = c.R.FullFsPath(c.Str("FilePath"), c.R.CurrentFsDir);
                var f = c.R.FsNode(full);
                if (f == null) throw c.Fail("Cannot find path '" + full + "' because it does not exist.", "PathNotFound", "ObjectNotFound", full, "System.Management.Automation.ItemNotFoundException");
                string err;
                string store = StoreOf(c.Str("CertStoreLocation", "Cert:\\CurrentUser\\My"), out err);
                if (store == null) throw c.Fail(err, "PathNotFound", "ObjectNotFound", c.Str("CertStoreLocation"), "System.Management.Automation.ItemNotFoundException");
                string[] p = null;
                try { var lines = f.Text.Replace("\r", "").Split('\n'); p = Encoding.UTF8.GetString(Convert.FromBase64String(lines[1].Trim())).Split('|'); } catch (Exception) { }
                if (p == null || p.Length < 7 || !f.Text.StartsWith("CertLab simulated certificate", StringComparison.Ordinal)) throw c.Fail("The data is invalid. 0x8007000d (WIN32: 13 ERROR_INVALID_DATA)", "System.Exception,Microsoft.CertificateServices.Commands.ImportCertificateCommand", "NotSpecified", null, "System.Exception");
                var w = m.Certs.FirstOrDefault(x => x.Thumbprint == p[0] && x.Store == store);
                if (w == null)
                {
                    w = new WinCert { Store = store, Thumbprint = p[0], Subject = p[1], Issuer = p[2], NotBefore = DateTime.Parse(p[3], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), NotAfter = DateTime.Parse(p[4], CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), CodeSigning = p[5] == "1", HasPrivateKey = false };
                    w.EnhancedKeyUsage.AddRange(p[6].Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
                    m.Certs.Add(w);
                }
                c.Out(CertObj(w));
            });
            Add("Get-AuthenticodeSignature", SecMod, "FilePath:string[]@0 ! pipe prop alias=Path ps=ByPath; LiteralPath:string[] ! prop alias=PSPath ps=ByLiteralPath", (c, o) =>
            {
                foreach (var f in FilesFor(c, c.Has("FilePath") ? "FilePath" : "LiteralPath", o).ToList()) c.Out(SignatureObj(c.M, f));
            });
            Add("Set-AuthenticodeSignature", SecMod, "FilePath:string[]@0 ! pipe prop alias=Path ps=ByPath; LiteralPath:string[] ! prop alias=PSPath ps=ByLiteralPath; Certificate:object@1 !; TimestampServer:string; HashAlgorithm:string; IncludeChain:string; Force:switch", (c, o) =>
            {
                var m = c.M;
                var cert = CertOf(c, c.Get("Certificate"));
                if (cert == null || !cert.CodeSigning || !cert.HasPrivateKey)
                    throw c.Fail("Cannot sign code. The specified certificate is not suitable for code signing.", "Argument", "InvalidArgument", null, "System.Management.Automation.PSArgumentException");
                if (c.Has("TimestampServer")) c.Warn("Cert Lab: the time stamp server " + c.Str("TimestampServer") + " cannot be reached (no internet); the script is signed without a time stamp.");
                foreach (var f in FilesFor(c, c.Has("FilePath") ? "FilePath" : "LiteralPath", o).ToList())
                {
                    if (!c.ShouldProcess(f.FullPath, "Set-AuthenticodeSignature")) continue;
                    if (f.ReadOnly && !c.Sw("Force")) { c.Error("Cannot sign the file because it is read-only: " + f.FullPath, "ForceArgumentException", "PermissionDenied", f.FullPath, "System.Management.Automation.PSArgumentException"); continue; }
                    string text = f.Text;
                    int i = text.IndexOf(PsSec.SigBegin, StringComparison.Ordinal);
                    if (i >= 0) text = text.Substring(0, i);
                    if (i < 0) text += text.EndsWith("\n", StringComparison.Ordinal) ? "\r\n" : "\r\n\r\n";
                    f.Text = text + PsSec.Block(cert, text);
                    f.Signer = cert.Thumbprint; f.SignedText = text;
                    f.Modified = m.Now;
                    c.Out(SignatureObj(m, f));
                }
            }).ShouldProcess = true;

            // ---------------------------------------------------- SecureString and credentials
            Add("ConvertTo-SecureString", SecMod, "String:string@0 ! pipe; AsPlainText:switch@1; Force:switch@2; SecureKey:securestring; Key:byte[]", (c, o) =>
            {
                string s = c.HasInput ? PsConv.ToStr(o) : c.Str("String");
                if (c.Sw("AsPlainText"))
                {
                    if (!c.Sw("Force")) throw c.Fail("The system cannot protect plain text input. To suppress this warning and convert the plain text to a SecureString, reissue the command specifying the Force parameter. For more information ,type: get-help ConvertTo-SecureString.", "ImportSecureString_InvalidArgument", "InvalidArgument", null, "System.ArgumentException");
                    c.Out(new PsSecureString { Plain = s ?? "" });
                    return;
                }
                string plain = null;
                byte[] key = c.Has("Key") ? c.Objs("Key").Select(x => (byte)PsConv.ToInt(x)).ToArray() : c.Has("SecureKey") ? Encoding.Unicode.GetBytes(((PsSecureString)c.Get("SecureKey")).Plain) : null;
                try
                {
                    if (key != null)
                    {
                        if (key.Length != 16 && key.Length != 24 && key.Length != 32) throw c.Fail("The specified key is not valid. Valid key length settings are either 128 bits, 192 bits, or 256 bits.", "ImportSecureString_InvalidArgument", "InvalidArgument", null, "System.ArgumentException");
                        if (!s.StartsWith(PsSec.AesHeader, StringComparison.Ordinal)) throw new FormatException();
                        plain = PsSec.FromKeyed(s, key);
                        if (plain == null) throw c.Fail("Padding is invalid and cannot be removed.", "ImportSecureString_InvalidArgument_CryptographicError", "InvalidArgument", null, "System.Security.Cryptography.CryptographicException");
                    }
                    else
                    {
                        if (s.StartsWith(PsSec.AesHeader, StringComparison.Ordinal)) throw c.Fail("Key not valid for use in specified state.", "ImportSecureString_InvalidArgument_CryptographicError", "InvalidArgument", null, "System.Security.Cryptography.CryptographicException");
                        if (!s.StartsWith(PsSec.DpapiHeader, StringComparison.Ordinal)) throw new FormatException();
                        plain = PsSec.FromDpapi(c.R, s);
                        if (plain == null) throw c.Fail("Key not valid for use in specified state.", "ImportSecureString_InvalidArgument_CryptographicError", "InvalidArgument", null, "System.Security.Cryptography.CryptographicException");
                    }
                }
                catch (FormatException) { throw c.Fail("Input string was not in a correct format.", "ImportSecureString_InvalidArgument", "InvalidArgument", null, "System.FormatException"); }
                catch (ArgumentException) { throw c.Fail("Input string was not in a correct format.", "ImportSecureString_InvalidArgument", "InvalidArgument", null, "System.FormatException"); }
                c.Out(new PsSecureString { Plain = plain });
            });
            Add("ConvertFrom-SecureString", SecMod, "SecureString:securestring@0 ! pipe; SecureKey:securestring; Key:byte[]", (c, o) =>
            {
                var ss = (c.HasInput ? PsConv.Base(o) : c.Get("SecureString")) as PsSecureString;
                if (ss == null) return;
                byte[] key = c.Has("Key") ? c.Objs("Key").Select(x => (byte)PsConv.ToInt(x)).ToArray() : c.Has("SecureKey") ? Encoding.Unicode.GetBytes(((PsSecureString)c.Get("SecureKey")).Plain) : null;
                if (key != null)
                {
                    if (key.Length != 16 && key.Length != 24 && key.Length != 32) throw c.Fail("The specified key is not valid. Valid key length settings are either 128 bits, 192 bits, or 256 bits.", "Argument", "InvalidArgument", null, "System.ArgumentException");
                    c.Out(PsSec.ToKeyed(c.R, ss.Plain, key));
                    return;
                }
                c.Out(PsSec.ToDpapi(c.R, ss.Plain));
            });
            Add("Get-Credential", SecMod, "Credential:object@0; UserName:string; Message:string; Title:string", (c, o) =>
            {
                var R = c.R;
                var given = PsConv.Base(c.Get("Credential"));
                var gp = given as PsObj;
                if (gp != null && gp.TypeName == "System.Management.Automation.PSCredential") { c.Out(gp); return; }
                string user = c.Str("UserName") ?? (given != null ? PsConv.ToStr(given) : null);
                if (R.Host == null || !R.Host.Interactive)
                    throw c.Fail("PowerShell is in NonInteractive mode. Read and Prompt functionality is not available.", "CouldNotPromptForCredential", "InvalidOperation", null, "System.Management.Automation.PSInvalidOperationException");
                R.HostWrite("\n" + (c.Str("Title") ?? "Windows PowerShell credential request") + "\n" + (c.Str("Message") ?? "Enter your credentials.") + "\n");
                if (string.IsNullOrEmpty(user)) user = R.Host.ReadLine("User: ", false) ?? "";
                if (user.Length == 0) throw c.Fail("Cannot process command because of one or more missing mandatory parameters: Credential.", "CouldNotPromptForCredential", "InvalidArgument", null, "System.Management.Automation.ParameterBindingException");
                var pw = R.Host.ReadLine("Password for user " + user + ": ", true) ?? "";
                R.HostWrite("\n");
                c.Out(PsCred.Make(user, new PsSecureString { Plain = pw }));
            });

            // ---------------------------------------------------- JEA: session configuration and role capability files
            Add("New-PSSessionConfigurationFile", Core, "Path:string@0 !; SchemaVersion:version; Guid:guid; Author:string; CompanyName:string; Copyright:string; Description:string; SessionType:string set=Default|Empty|RestrictedRemoteServer; TranscriptDirectory:string; RunAsVirtualAccount:switch; RunAsVirtualAccountGroups:string[]; GroupManagedServiceAccount:string; MountUserDrive:switch; UserDriveMaximumSize:long; RoleDefinitions:hashtable; RequiredGroups:hashtable; LanguageMode:string set=FullLanguage|ConstrainedLanguage|RestrictedLanguage|NoLanguage; ExecutionPolicy:ExecutionPolicy; ModulesToImport:object[]; VisibleAliases:string[]; VisibleCmdlets:object[]; VisibleFunctions:object[]; VisibleExternalCommands:string[]; VisibleProviders:string[]; EnvironmentVariables:hashtable; Full:switch", (c, o) =>
            {
                string path = c.Str("Path");
                if (!path.EndsWith(".pssc", StringComparison.OrdinalIgnoreCase)) throw c.Fail("The path " + path + " does not end in .pssc: a session configuration file must have the .pssc extension.", "InvalidPSSessionConfigurationFilePath", "InvalidArgument", path, "System.Management.Automation.PSArgumentException");
                var sb = new StringBuilder("@{\r\n\r\n");
                Action<string, string, object> key = (comment, k, v) => { sb.Append("# ").Append(comment).Append("\r\n").Append(v == null ? "# " + k + " = " : k + " = " + Lit(v, "")).Append("\r\n\r\n"); };
                key("Version of the format of this file", "SchemaVersion", c.Str("SchemaVersion") ?? "2.0.0.0");
                key("A unique ID for this file", "GUID", c.Has("Guid") ? c.Str("Guid") : PsConv.ToStr(c.M.NewGuid()));
                key("Who wrote this file", "Author", c.Str("Author") ?? c.R.User);
                key("What this endpoint is for", "Description", c.Get("Description"));
                key("Session type: RestrictedRemoteServer (for JEA: only a few basic commands plus the roles' commands), Empty or Default", "SessionType", c.Str("SessionType") ?? "Default");
                key("Folder that receives a transcript of every session", "TranscriptDirectory", c.Get("TranscriptDirectory"));
                if (c.Sw("RunAsVirtualAccount")) key("Run commands as a temporary administrator account of this computer", "RunAsVirtualAccount", true); else key("Run commands as a temporary administrator account of this computer", "RunAsVirtualAccount", null);
                key("Groups the virtual account is a member of", "RunAsVirtualAccountGroups", c.Get("RunAsVirtualAccountGroups"));
                key("Group managed service account to run commands as", "GroupManagedServiceAccount", c.Get("GroupManagedServiceAccount"));
                key("Users or groups and the role capabilities they get", "RoleDefinitions", c.Get("RoleDefinitions"));
                key("Groups a user must belong to before connecting", "RequiredGroups", c.Get("RequiredGroups"));
                key("Language mode of the session", "LanguageMode", c.Get("LanguageMode"));
                key("Execution policy of the session", "ExecutionPolicy", c.Has("ExecutionPolicy") ? c.Str("ExecutionPolicy") : null);
                key("Modules to import when the session starts", "ModulesToImport", c.Get("ModulesToImport"));
                key("Cmdlets every user of the endpoint can run", "VisibleCmdlets", c.Get("VisibleCmdlets"));
                key("Functions every user of the endpoint can run", "VisibleFunctions", c.Get("VisibleFunctions"));
                key("Programs every user of the endpoint can run", "VisibleExternalCommands", c.Get("VisibleExternalCommands"));
                key("Providers (drives) visible in the session", "VisibleProviders", c.Get("VisibleProviders"));
                if (c.Sw("MountUserDrive")) key("Give each user a User: drive for copying files", "MountUserDrive", true);
                sb.Append("}\r\n\r\n");
                WriteDataFile(c, path, sb.ToString(), "New-PSSessionConfigurationFile");
            }).ShouldProcess = true;
            Add("New-PSRoleCapabilityFile", Core, "Path:string@0 !; Guid:guid; Author:string; Description:string; CompanyName:string; Copyright:string; ModulesToImport:object[]; VisibleAliases:string[]; VisibleCmdlets:object[]; VisibleFunctions:object[]; VisibleExternalCommands:string[]; VisibleProviders:string[]; ScriptsToProcess:string[]; AliasDefinitions:hashtable[]; FunctionDefinitions:hashtable[]; VariableDefinitions:object; EnvironmentVariables:hashtable; TypesToProcess:string[]; FormatsToProcess:string[]; AssembliesToLoad:string[]", (c, o) =>
            {
                string path = c.Str("Path");
                if (!path.EndsWith(".psrc", StringComparison.OrdinalIgnoreCase)) throw c.Fail("The path " + path + " does not end in .psrc: a role capability file must have the .psrc extension.", "InvalidRoleCapabilityFilePath", "InvalidArgument", path, "System.Management.Automation.PSArgumentException");
                var sb = new StringBuilder("@{\r\n\r\n");
                Action<string, string, object> key = (comment, k, v) => { sb.Append("# ").Append(comment).Append("\r\n").Append(v == null ? "# " + k + " = " : k + " = " + Lit(v, "")).Append("\r\n\r\n"); };
                key("A unique ID for this file", "GUID", c.Has("Guid") ? c.Str("Guid") : PsConv.ToStr(c.M.NewGuid()));
                key("Who wrote this file", "Author", c.Str("Author") ?? c.R.User);
                key("What this role is for", "Description", c.Get("Description"));
                key("Company that wrote this file", "CompanyName", c.Str("CompanyName") ?? "Unknown");
                key("Copyright statement", "Copyright", c.Str("Copyright") ?? "(c) " + c.M.Now.Year + " " + c.R.User + ". All rights reserved.");
                key("Modules to import when this role is used", "ModulesToImport", c.Get("ModulesToImport"));
                key("Aliases the role can use", "VisibleAliases", c.Get("VisibleAliases"));
                key("Cmdlets the role can run: names, wildcards, or @{ Name = ...; Parameters = @{ Name = ...; ValidateSet = ... } }", "VisibleCmdlets", c.Get("VisibleCmdlets"));
                key("Functions the role can run", "VisibleFunctions", c.Get("VisibleFunctions"));
                key("Programs the role can run (full paths)", "VisibleExternalCommands", c.Get("VisibleExternalCommands"));
                key("Providers (drives) the role can use", "VisibleProviders", c.Get("VisibleProviders"));
                key("Scripts that run when the role is used", "ScriptsToProcess", c.Get("ScriptsToProcess"));
                key("Aliases to define", "AliasDefinitions", c.Get("AliasDefinitions"));
                key("Functions to define: @{ Name = ...; ScriptBlock = { ... } }", "FunctionDefinitions", c.Get("FunctionDefinitions"));
                key("Variables to define", "VariableDefinitions", c.Get("VariableDefinitions"));
                key("Environment variables to define", "EnvironmentVariables", c.Get("EnvironmentVariables"));
                sb.Append("}\r\n\r\n");
                WriteDataFile(c, path, sb.ToString(), "New-PSRoleCapabilityFile");
            }).ShouldProcess = true;
            Add("Test-PSSessionConfigurationFile", Core, "Path:string@0 ! pipe prop", (c, o) =>
            {
                string full = c.R.FullFsPath(c.Str("Path"), c.R.CurrentFsDir);
                if (c.R.FsNode(full) == null) throw c.Fail("Cannot find path '" + full + "' because it does not exist.", "PSSessionConfigurationFileNotFound", "ResourceUnavailable", full, "System.Management.Automation.ItemNotFoundException");
                string err;
                var h = PsModMgr.ReadData(c.R, full, out err);
                if (h == null) { c.Verbose(err); c.Out(false); return; }
                var known = new[] { "SchemaVersion", "GUID", "Author", "Description", "CompanyName", "Copyright", "SessionType", "TranscriptDirectory", "RunAsVirtualAccount", "RunAsVirtualAccountGroups", "GroupManagedServiceAccount", "MountUserDrive", "UserDriveMaximumSize", "RoleDefinitions", "RequiredGroups", "LanguageMode", "ExecutionPolicy", "ModulesToImport", "VisibleAliases", "VisibleCmdlets", "VisibleFunctions", "VisibleExternalCommands", "VisibleProviders", "EnvironmentVariables", "FunctionDefinitions", "AliasDefinitions", "VariableDefinitions", "ScriptsToProcess", "TypesToProcess", "FormatsToProcess", "AssembliesToLoad", "PowerShellVersion", "RoleCapabilities", "RoleCapabilityFiles" };
                foreach (DictionaryEntry e in h)
                    if (!known.Any(k => k.Equals(PsConv.ToStr(e.Key), StringComparison.OrdinalIgnoreCase))) { c.Verbose("The member '" + e.Key + "' is not valid in the session configuration file."); c.Out(false); return; }
                var st = PsConv.ToStr(PsModMgr.Key(h, "SessionType") ?? "Default");
                if (!new[] { "Default", "Empty", "RestrictedRemoteServer" }.Any(x => x.Equals(st, StringComparison.OrdinalIgnoreCase))) { c.Verbose("The SessionType '" + st + "' is not valid."); c.Out(false); return; }
                c.Out(true);
            });
            var reg = Add("Register-PSSessionConfiguration", Core, "Name:string@0 !; Path:string; AssemblyName:string; ConfigurationTypeName:string; RunAsCredential:pscredential; ShowSecurityDescriptorUI:switch; SecurityDescriptorSddl:string; StartupScript:string; NoServiceRestart:switch; Force:switch; AccessMode:string set=Disabled|Local|Remote", (c, o) =>
            {
                var m = c.M;
                if (!m.WinRmListening) throw new PsRuntimeException(WinRmDown(c), false);
                string name = c.Str("Name");
                var old = m.Endpoint(name);
                if (old != null && (old.Builtin || !c.Sw("Force")))
                    throw c.Fail("Cannot register the session configuration " + name + " because a session configuration with that name is already registered. Use Unregister-PSSessionConfiguration first, or -Force.", "PSSessionConfigurationExists", "InvalidOperation", name, "System.InvalidOperationException");
                IDictionary file = null; string full = null;
                if (c.Has("Path"))
                {
                    full = c.R.FullFsPath(c.Str("Path"), c.R.CurrentFsDir);
                    string err;
                    if (c.R.FsNode(full) == null) throw c.Fail("Cannot find path '" + full + "' because it does not exist.", "PSSessionConfigurationFileNotFound", "ObjectNotFound", full, "System.Management.Automation.ItemNotFoundException");
                    file = PsModMgr.ReadData(c.R, full, out err);
                    if (file == null) throw c.Fail("The session configuration file " + full + " is not valid: " + err, "InvalidPSSessionConfigurationFile", "InvalidData", full, "System.Management.Automation.PSArgumentException");
                }
                if (!c.Sw("Force") && !c.ShouldProcess("Name: " + name + ". This will allow administrators to remotely run Windows PowerShell commands on this computer.", "Register-PSSessionConfiguration")) return;
                if (old != null) m.Endpoints.Remove(old);
                var ep = new PsEndpoint { Name = name, Path = full ?? "", File = file };
                if (file != null)
                {
                    ep.SessionType = PsConv.ToStr(PsModMgr.Key(file, "SessionType") ?? "Default");
                    ep.RunAsVirtualAccount = PsConv.ToBool(PsModMgr.Key(file, "RunAsVirtualAccount") ?? false);
                    ep.TranscriptDirectory = PsConv.ToStr(PsModMgr.Key(file, "TranscriptDirectory") ?? "");
                    ep.LanguageMode = PsConv.ToStr(PsModMgr.Key(file, "LanguageMode") ?? "");
                    ep.RoleDefinitions = PsModMgr.Key(file, "RoleDefinitions") as IDictionary;
                    if (ep.RunAsVirtualAccount) ep.RunAsUser = "";
                }
                ep.Permission = ep.RoleDefinitions != null && ep.RoleDefinitions.Count > 0 ? string.Join(", ", ep.RoleDefinitions.Keys.Cast<object>().Select(k => PsConv.ToStr(k) + " AccessAllowed").ToArray()) : "NT AUTHORITY\\INTERACTIVE AccessAllowed, BUILTIN\\Administrators AccessAllowed, BUILTIN\\Remote Management Users AccessAllowed";
                m.Endpoints.Add(ep);
                if (!c.Sw("NoServiceRestart")) c.Warn("Register-PSSessionConfiguration may need to restart the WinRM service if a configuration using this name has recently been unregistered, certain system data structures may still be cached. In that case, a restart of WinRM may be required.");
                var x = new PsObj("Microsoft.WSMan.Management.WSManConfigContainerElement", "Microsoft.WSMan.Management.WSManConfigElement", "System.Object");
                x.Val("PSParentPath", "string", "Microsoft.WSMan.Management\\WSMan::localhost\\Plugin");
                x.Val("Type", "string", "Container");
                x.Val("Keys", "string[]", new object[] { "Name=" + name });
                x.Val("Name", "string", name);
                x.ToStr = y => name;
                c.Out(x);
            });
            reg.ShouldProcess = true;
            Add("Get-PSSessionConfiguration", Core, "Name:string[]@0; Force:switch", (c, o) =>
            {
                var m = c.M;
                if (!m.WinRmListening) throw new PsRuntimeException(WinRmDown(c), false);
                if (c.Has("Name"))
                {
                    foreach (var n in c.Strs("Name"))
                    {
                        var hit = m.Endpoints.Where(e => PsConv.Like(e.Name, n, false)).ToList();
                        if (hit.Count == 0 && !PsConv.HasWildcard(n)) c.WriteError(c.MakeError("No session configuration matches criteria \"" + n + "\".", "Microsoft.PowerShell.Commands.WriteErrorException", "NotSpecified", null, "Microsoft.PowerShell.Commands.WriteErrorException"));
                        foreach (var e in hit) c.Out(EndpointObj(e));
                    }
                    return;
                }
                foreach (var e in m.Endpoints) c.Out(EndpointObj(e));
            });
            var unreg = Add("Unregister-PSSessionConfiguration", Core, "Name:string@0 ! pipe prop; Force:switch; NoServiceRestart:switch", (c, o) =>
            {
                var m = c.M;
                if (!m.WinRmListening) throw new PsRuntimeException(WinRmDown(c), false);
                string n = c.HasInput && PsConv.Base(o) is PsObj ? PsConv.ToStr(((PsObj)PsConv.Base(o)).Get("Name")) : c.Str("Name");
                var hit = m.Endpoints.Where(e => PsConv.Like(e.Name, n, false)).ToList();
                if (hit.Count == 0) { c.WriteError(c.MakeError("No session configuration matches criteria \"" + n + "\".", "Microsoft.PowerShell.Commands.WriteErrorException", "NotSpecified", null, "Microsoft.PowerShell.Commands.WriteErrorException")); return; }
                foreach (var e in hit)
                {
                    if (!c.Sw("Force") && !c.ShouldProcess("Name: " + e.Name, "Unregister-PSSessionConfiguration")) continue;
                    m.Endpoints.Remove(e);
                }
            });
            unreg.ShouldProcess = true; unreg.ConfirmImpact = "Low";
            Add("Get-PSSessionCapability", Core, "ConfigurationName:string@0 !; Username:string@1 !; Full:switch", (c, o) =>
            {
                var m = c.M;
                if (m.Endpoint(c.Str("ConfigurationName")) == null) throw c.Fail("No session configuration matches criteria \"" + c.Str("ConfigurationName") + "\".", "PSSessionConfigurationNotFound", "ObjectNotFound", c.Str("ConfigurationName"), "System.ArgumentException");
                var rules = m.EndpointCommandRules(c.Str("ConfigurationName"), c.Str("Username"));
                if (rules == null) throw c.Fail("The user " + c.Str("Username") + " has no role in the session configuration " + c.Str("ConfigurationName") + ", so the connection would be refused.", "NoRoleForUser", "PermissionDenied", c.Str("Username"), "System.UnauthorizedAccessException");
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var r in rules)
                {
                    var names = new List<string>();
                    if (PsConv.HasWildcard(r.Name)) names.AddRange(All.Values.Where(d => PsConv.Like(d.Name, r.Name, false)).Select(d => d.Name).OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
                    else names.Add(r.Name);
                    foreach (var n in names)
                    {
                        if (!seen.Add(n)) continue;
                        PsCommandInfo ci;
                        if (r.ScriptBlock != null || (PsCmdlets.Get(n) == null && !n.Contains("\\"))) ci = new PsCommandInfo { Kind = "Function", Name = n, Definition = r.ScriptBlock == null ? "" : r.ScriptBlock.Text };
                        else ci = c.R.Resolve(n, c.R.Global, false) ?? new PsCommandInfo { Kind = "Application", Name = n.Substring(n.LastIndexOf('\\') + 1), Path = n, Definition = n };
                        c.Out(CommandObj(c.R, ci));
                    }
                }
            });
        }
    }
}
