// Cert Lab - security tools for the Security+ labs: openssl (x509, verify, dgst, rand, version) and nmap (a TCP
// connect scan of the lab network that asks the same rules ssh and curl do: routes, the target's firewalld, who
// listens). Certificates are pretend: certlab-mkcert (graders and setups only) writes a PEM file whose body is Cert
// Lab's own encoding of the fields (no real keys or signatures); openssl reads and prints them in OpenSSL 3's layout.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public static partial class Cmd
    {
        static void RegSec() { P("openssl", Openssl); P("nmap", Nmap); P("certlab-mkcert", MkCert); }

        const string CertMagic = "CERTLAB-X509 1";

        /// <summary>certlab-mkcert FILE key=value...: writes a pretend certificate (setups of the Security+ labs).</summary>
        static int MkCert(Sh sh, List<string> a, Io io)
        {
            if (sh.P.Cr.Uid != 0) { io.E("certlab-mkcert: only the graders (root) use this"); return 2; }
            if (a.Count < 2) { io.E("usage: certlab-mkcert FILE subject=... [issuer=...] [days=N] [san=a,b] [ca=yes] ..."); return 2; }
            var f = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var kv in a.Skip(2)) { int eq = kv.IndexOf('='); if (eq > 0) f[kv.Substring(0, eq)] = kv.Substring(eq + 1); }
            string v;
            long now = sh.M.Now;
            long nb = f.TryGetValue("notbefore", out v) ? Unix(v) : now - 86400;
            long na = f.TryGetValue("notafter", out v) ? Unix(v) : nb + 86400L * (f.TryGetValue("days", out v) ? long.Parse(v, CultureInfo.InvariantCulture) : 365);
            string subject = f.TryGetValue("subject", out v) ? v : "CN=localhost";
            var sb = new StringBuilder(CertMagic + "\n");
            sb.Append("subject=" + subject + "\n");
            sb.Append("issuer=" + (f.TryGetValue("issuer", out v) ? v : subject) + "\n");
            sb.Append("serial=" + (f.TryGetValue("serial", out v) ? v : (Math.Abs(FwHash.Of((subject + nb))) % 0xFFFFFF).ToString("x6") + "a1c3") + "\n");
            sb.Append("notbefore=" + nb + "\nnotafter=" + na + "\n");
            foreach (var k in new[] { "san", "ca", "keyusage", "eku", "sig", "bits", "keytype", "crl", "ocsp" }) if (f.TryGetValue(k, out v)) sb.Append(k + "=" + v + "\n");
            string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(sb.ToString()));
            var pem = new StringBuilder("-----BEGIN CERTIFICATE-----\n");
            for (int i = 0; i < b64.Length; i += 64) pem.Append(b64.Substring(i, Math.Min(64, b64.Length - i))).Append('\n');
            pem.Append("-----END CERTIFICATE-----\n");
            try { sh.M.V.WriteText(sh.P, a[1], pem.ToString(), false); } catch (VfsEx e) { io.E("certlab-mkcert: " + a[1] + ": " + e.Message); return 1; }
            return 0;
        }
        static long Unix(string ymd)
        {
            var d = DateTime.ParseExact(ymd.Length == 10 ? ymd + " 00:00:00" : ymd, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            return (long)(d - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }

        class PCert
        {
            public Dictionary<string, string> F = new Dictionary<string, string>(StringComparer.Ordinal);
            public string Body = "";
            public string Get(string k, string d) { string v; return F.TryGetValue(k, out v) ? v : d; }
            public long NotBefore { get { return long.Parse(Get("notbefore", "0"), CultureInfo.InvariantCulture); } }
            public long NotAfter { get { return long.Parse(Get("notafter", "0"), CultureInfo.InvariantCulture); } }
            public bool Ca { get { return Get("ca", "no") == "yes"; } }
        }

        /// <summary>The certificates in a PEM file (null: not a certificate this simulator can read).</summary>
        static List<PCert> ReadCerts(string text)
        {
            var l = new List<PCert>();
            int at = 0;
            while (true)
            {
                int b = text.IndexOf("-----BEGIN CERTIFICATE-----", at, StringComparison.Ordinal);
                if (b < 0) break;
                int e = text.IndexOf("-----END CERTIFICATE-----", b, StringComparison.Ordinal);
                if (e < 0) return null;
                string body = text.Substring(b + 27, e - b - 27).Replace("\n", "").Replace("\r", "").Trim();
                at = e + 25;
                string dec;
                try { dec = Encoding.UTF8.GetString(Convert.FromBase64String(body)); } catch (FormatException) { return null; }
                if (!dec.StartsWith(CertMagic, StringComparison.Ordinal)) return null;
                var c = new PCert { Body = body };
                foreach (var line in dec.Split('\n').Skip(1)) { int eq = line.IndexOf('='); if (eq > 0) c.F[line.Substring(0, eq)] = line.Substring(eq + 1); }
                l.Add(c);
            }
            return l.Count == 0 ? null : l;
        }

        static string DnText(string dn) { return string.Join(", ", dn.Split(',').Select(p => { var kv = p.Split(new[] { '=' }, 2); return kv.Length == 2 ? kv[0].Trim() + " = " + kv[1].Trim() : p.Trim(); }).ToArray()); }
        static string GmtText(long t)
        {
            var d = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(t);
            return d.ToString("MMM", CultureInfo.InvariantCulture) + " " + d.Day.ToString(CultureInfo.InvariantCulture).PadLeft(2) + " " + d.ToString("HH:mm:ss yyyy", CultureInfo.InvariantCulture) + " GMT";
        }
        static string Colons(string hex) { var sb = new StringBuilder(); for (int i = 0; i < hex.Length; i += 2) { if (sb.Length > 0) sb.Append(':'); sb.Append(hex.Substring(i, Math.Min(2, hex.Length - i))); } return sb.ToString(); }
        static readonly Dictionary<string, string> Eku = new Dictionary<string, string> { { "serverAuth", "TLS Web Server Authentication" }, { "clientAuth", "TLS Web Client Authentication" }, { "codeSigning", "Code Signing" }, { "emailProtection", "E-mail Protection" }, { "timeStamping", "Time Stamping" }, { "OCSPSigning", "OCSP Signing" } };
        static readonly Dictionary<string, string> Ku = new Dictionary<string, string> { { "digitalSignature", "Digital Signature" }, { "keyEncipherment", "Key Encipherment" }, { "keyCertSign", "Certificate Sign" }, { "cRLSign", "CRL Sign" }, { "nonRepudiation", "Non Repudiation" }, { "keyAgreement", "Key Agreement" } };
        static string Names(string list, Dictionary<string, string> map) { return string.Join(", ", list.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).Select(x => map.ContainsKey(x) ? map[x] : x).ToArray()); }
        static string Fingerprint(PCert c, string kind)
        {
            return Colons(HashOf(c.Body, kind).ToUpperInvariant());
        }

        static void CertText(PCert c, Io io)
        {
            string sig = c.Get("sig", "sha256WithRSAEncryption"), kt = c.Get("keytype", "RSA");
            io.L("Certificate:");
            io.L("    Data:");
            io.L("        Version: 3 (0x2)");
            io.L("        Serial Number:");
            io.L("            " + Colons(c.Get("serial", "01")));
            io.L("        Signature Algorithm: " + sig);
            io.L("        Issuer: " + DnText(c.Get("issuer", "")));
            io.L("        Validity");
            io.L("            Not Before: " + GmtText(c.NotBefore));
            io.L("            Not After : " + GmtText(c.NotAfter));
            io.L("        Subject: " + DnText(c.Get("subject", "")));
            io.L("        Subject Public Key Info:");
            io.L("            Public Key Algorithm: " + (kt == "EC" ? "id-ecPublicKey" : "rsaEncryption"));
            io.L("                Public-Key: (" + c.Get("bits", kt == "EC" ? "256" : "2048") + " bit)");
            if (kt == "EC") io.L("                ASN1 OID: prime256v1");
            io.L("        X509v3 extensions:");
            if (c.F.ContainsKey("san")) { io.L("            X509v3 Subject Alternative Name: "); io.L("                " + string.Join(", ", c.Get("san", "").Split(',').Select(x => (x.Trim().Length > 0 && char.IsDigit(x.Trim()[0]) ? "IP Address:" : "DNS:") + x.Trim()).ToArray())); }
            io.L("            X509v3 Key Usage: critical");
            io.L("                " + Names(c.Get("keyusage", c.Ca ? "keyCertSign,cRLSign" : "digitalSignature,keyEncipherment"), Ku));
            if (c.F.ContainsKey("eku") || !c.Ca) { io.L("            X509v3 Extended Key Usage: "); io.L("                " + Names(c.Get("eku", "serverAuth"), Eku)); }
            io.L("            X509v3 Basic Constraints: critical");
            io.L("                CA:" + (c.Ca ? "TRUE" : "FALSE"));
            if (c.F.ContainsKey("crl")) { io.L("            X509v3 CRL Distribution Points: "); io.L("                Full Name:"); io.L("                  URI:" + c.Get("crl", "")); }
            if (c.F.ContainsKey("ocsp")) { io.L("            Authority Information Access: "); io.L("                OCSP - URI:" + c.Get("ocsp", "")); }
            io.L("    Signature Algorithm: " + sig);
            io.L("    Signature Value:");
            string h = HashOf(c.Body + "sig", "sha256");
            for (int i = 0; i < 4; i++) io.L("        " + Colons(h.Substring(i * 16, 16)) + ":");
            io.L("        (Cert Lab: a pretend signature)");
        }

        static int Openssl(Sh sh, List<string> a, Io io)
        {
            string sub = a.Count > 1 ? a[1] : "help";
            var rest = new List<string> { "openssl " + sub }; rest.AddRange(a.Skip(2));
            switch (sub)
            {
                case "version": io.L("OpenSSL 3.0.7 1 Nov 2022 (Library: OpenSSL 3.0.7 1 Nov 2022)"); return 0;
                case "x509": return OsslX509(sh, rest, io);
                case "verify": return OsslVerify(sh, rest, io);
                case "dgst": case "sha256": case "sha1": case "md5": case "sha512": return OsslDgst(sh, a, io);
                case "rand":
                    {
                        bool hex = a.Contains("-hex"), b64 = a.Contains("-base64");
                        int n; if (!int.TryParse(a.LastOrDefault(), out n) || n <= 0 || n > 4096 || !(hex || b64)) { io.E("Usage: openssl rand [-hex | -base64] NUM"); return 1; }
                        var r = new Random((int)(sh.M.Now % int.MaxValue) ^ FwHash.Of(sh.M.Name) ^ (randCount++ * 7919));
                        var bytes = new byte[n]; r.NextBytes(bytes);
                        io.L(hex ? string.Concat(bytes.Select(x => x.ToString("x2")).ToArray()) : Convert.ToBase64String(bytes));
                        return 0;
                    }
                case "help": case "-h": case "--help":
                    io.L("help:\n\nStandard commands\ndgst              rand              verify            version\nx509\n\n(Cert Lab: only these commands are simulated; see man openssl)");
                    return 0;
            }
            io.E("Invalid command '" + sub + "'; type \"help\" for a list.");
            io.E("(Cert Lab simulates openssl x509, verify, dgst, rand and version.)");
            return 1;
        }
        static int randCount;

        static PCert LoadOne(Sh sh, string file, Io io, string prog)
        {
            string text;
            try { text = Read(sh, file); }
            catch (VfsEx e) { io.E("Could not open file or uri for loading certificate from " + file + "\n" + prog + ": " + e.Message); return null; }
            var certs = ReadCerts(text ?? "");
            if (certs == null) { io.E("Could not read certificate from " + file + "\nUnable to load certificate"); return null; }
            return certs[0];
        }

        static int OsslX509(Sh sh, List<string> a, Io io)
        {
            string inFile = null; var wants = new List<string>(); string ext = null, dg = "sha1"; long checkEnd = -1;
            for (int i = 1; i < a.Count; i++)
            {
                string s = a[i];
                if (s == "-in" && i + 1 < a.Count) inFile = a[++i];
                else if (s == "-ext" && i + 1 < a.Count) { ext = a[++i]; wants.Add("ext"); }
                else if (s == "-checkend" && i + 1 < a.Count) checkEnd = long.Parse(a[++i], CultureInfo.InvariantCulture);
                else if (s == "-sha256" || s == "-sha1" || s == "-md5") dg = s.Substring(1);
                else if (s == "-noout" || s == "-nocert") { }
                else if (new[] { "-text", "-subject", "-issuer", "-dates", "-startdate", "-enddate", "-serial", "-fingerprint" }.Contains(s)) wants.Add(s);
                else { io.E("x509: Unknown option or message digest: " + s.TrimStart('-') + "\nx509: Use -help for summary."); return 1; }
            }
            if (inFile == null) { io.E("x509: Cert Lab needs -in FILE (reading a certificate from standard input is not simulated)"); return 1; }
            var c = LoadOne(sh, inFile, io, "x509");
            if (c == null) return 1;
            foreach (var w in wants)
                switch (w)
                {
                    case "-text": CertText(c, io); break;
                    case "-subject": io.L("subject=" + DnText(c.Get("subject", ""))); break;
                    case "-issuer": io.L("issuer=" + DnText(c.Get("issuer", ""))); break;
                    case "-dates": io.L("notBefore=" + GmtText(c.NotBefore)); io.L("notAfter=" + GmtText(c.NotAfter)); break;
                    case "-startdate": io.L("notBefore=" + GmtText(c.NotBefore)); break;
                    case "-enddate": io.L("notAfter=" + GmtText(c.NotAfter)); break;
                    case "-serial": io.L("serial=" + c.Get("serial", "01").ToUpperInvariant()); break;
                    case "-fingerprint": io.L(dg + " Fingerprint=" + Fingerprint(c, dg)); break;
                    case "ext":
                        if (ext == "subjectAltName" && c.F.ContainsKey("san")) { io.L("X509v3 Subject Alternative Name: "); io.L("    " + string.Join(", ", c.Get("san", "").Split(',').Select(x => (x.Trim().Length > 0 && char.IsDigit(x.Trim()[0]) ? "IP Address:" : "DNS:") + x.Trim()).ToArray())); }
                        else if (ext == "basicConstraints") { io.L("X509v3 Basic Constraints: critical"); io.L("    CA:" + (c.Ca ? "TRUE" : "FALSE")); }
                        else if (ext == "extendedKeyUsage") { io.L("X509v3 Extended Key Usage: "); io.L("    " + Names(c.Get("eku", "serverAuth"), Eku)); }
                        else io.L("No extensions in certificate");
                        break;
                }
            if (checkEnd >= 0)
            {
                bool will = sh.M.Now + checkEnd >= c.NotAfter;
                io.L(will ? "Certificate will expire" : "Certificate will not expire");
                return will ? 1 : 0;
            }
            return 0;
        }

        static int OsslVerify(Sh sh, List<string> a, Io io)
        {
            string caFile = null; var files = new List<string>();
            for (int i = 1; i < a.Count; i++) { if (a[i] == "-CAfile" && i + 1 < a.Count) caFile = a[++i]; else if (!a[i].StartsWith("-", StringComparison.Ordinal)) files.Add(a[i]); }
            var trusted = new List<PCert>();
            if (caFile != null)
            {
                string t; try { t = Read(sh, caFile); } catch (VfsEx e) { io.E("Error loading file " + caFile + ": " + e.Message); return 2; }
                var l = ReadCerts(t ?? ""); if (l == null) { io.E("Error loading file " + caFile); return 2; }
                trusted.AddRange(l);
            }
            int st = 0;
            foreach (var f in files)
            {
                var c = LoadOne(sh, f, io, "verify");
                if (c == null) { st = 2; continue; }
                string subj = c.Get("subject", ""), iss = c.Get("issuer", "");
                long now = sh.M.Now;
                int code = 0; string why = ""; int depth = 0;
                var ca = trusted.FirstOrDefault(x => x.Get("subject", "") == iss);
                if (now < c.NotBefore) { code = 9; why = "certificate is not yet valid"; }
                else if (now > c.NotAfter) { code = 10; why = "certificate has expired"; }
                else if (subj == iss && !trusted.Any(x => x.Body == c.Body)) { code = 18; why = "self-signed certificate"; }
                else if (subj != iss && ca == null) { code = 20; why = "unable to get local issuer certificate"; }
                else if (ca != null && subj != iss && !ca.Ca) { code = 24; why = "invalid CA certificate"; depth = 1; }
                else if (ca != null && subj != iss && (now > ca.NotAfter || now < ca.NotBefore)) { code = 10; why = "certificate has expired"; depth = 1; }
                if (code == 0) { io.L(f + ": OK"); continue; }
                io.E(DnText(depth == 1 && ca != null ? ca.Get("subject", "") : subj));
                io.E("error " + code + " at " + depth + " depth lookup: " + why);
                io.E("error " + f + ": verification failed");
                st = 2;
            }
            return st;
        }

        static int OsslDgst(Sh sh, List<string> a, Io io)
        {
            string kind = a[1] == "dgst" ? "sha256" : a[1];
            var files = new List<string>();
            foreach (var s in a.Skip(2)) { if (s == "-sha256" || s == "-sha1" || s == "-md5" || s == "-sha512") kind = s.Substring(1); else if (s.StartsWith("-", StringComparison.Ordinal)) { io.E("dgst: Unknown option or message digest: " + s.TrimStart('-')); return 1; } else files.Add(s); }
            string label = kind == "sha256" ? "SHA2-256" : kind == "sha512" ? "SHA2-512" : kind == "sha1" ? "SHA1" : "MD5";
            int st = 0;
            foreach (var kv in Inputs(sh, files, io, "dgst", ref st)) io.L((kv.Key == "-" ? label + "(stdin)= " : label + "(" + kv.Key + ")= ") + HashOf(kv.Value, kind));
            return st;
        }
    }
}
