// Cert Lab - curl and wget, and the web servers they talk to: the utility server (repositories and practice
// files), Apache (httpd) and nginx on the lab machines, and web servers in containers with published ports.
// Apache answers like the real one: 404 when a file is missing, 403 when file permissions or SELinux say no.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CertLab
{
    public class HttpReply
    {
        public int Code; public string Reason = "OK"; public string Body = ""; public long BinSize = -1;
        public string Type = "text/html; charset=UTF-8"; public string Server = "Apache/2.4.62 (Red Hat Enterprise Linux)";
        public string Error;   // a connection error (curl style text without "curl: ")
        public int CurlCode;
        public string Location;
        public string Headers = "";   // extra response headers, one per line (for example a Set-Cookie line)
        public long Length { get { return BinSize >= 0 ? BinSize : Encoding.UTF8.GetByteCount(Body); } }
    }

    public static partial class Cmd
    {
        static void RegHttp() { P("curl", Curl); P("wget", Wget); }
        /// <summary>The form data curl -d sends with the request being made (read by Cyber Range's pretend web apps).</summary>
        [ThreadStatic] public static string PostData;
        /// <summary>The cookies curl -b / --cookie sends with the request (read by Cyber Range's pretend web apps).</summary>
        [ThreadStatic] public static string CookieData;

        static string Page(int code, string reason, string text)
        {
            return "<!DOCTYPE HTML PUBLIC \"-//IETF//DTD HTML 2.0//EN\">\n<html><head>\n<title>" + code + " " + reason + "</title>\n</head><body>\n<h1>" + reason + "</h1>\n<p>" + text + "</p>\n</body></html>\n";
        }

        static HttpReply Fetch(Sh sh, string url) { return Fetch(sh.M, sh.P, url); }
        static HttpReply Fetch(Machine mach, PCtx pc, string url)
        {
            var r = new HttpReply();
            var m = Regex.Match(url, "^(?:(https?|ftp|file)://)?([^/:]*)(?::(\\d+))?(/.*)?$", RegexOptions.IgnoreCase);
            if (!m.Success) { r.Error = "URL rejected: Malformed input to a URL function"; r.CurlCode = 3; return r; }
            string scheme = m.Groups[1].Success && m.Groups[1].Value.Length > 0 ? m.Groups[1].Value.ToLowerInvariant() : "http";
            string host = m.Groups[2].Value;
            string path = m.Groups[4].Success && m.Groups[4].Value.Length > 0 ? m.Groups[4].Value : "/";
            int q = path.IndexOf('?'); string query = q >= 0 ? path.Substring(q + 1) : ""; if (q >= 0) path = path.Substring(0, q);
            path = Uri.UnescapeDataString(path);
            if (scheme == "file")
            {
                try { var l = mach.V.Resolve(pc, path, true); if (!l.Exists) { r.Error = "Couldn't open file " + path; r.CurlCode = 37; return r; } r.Body = mach.V.ReadNode(pc, l); r.Code = 200; return r; }
                catch (VfsEx) { r.Error = "Couldn't open file " + path; r.CurlCode = 37; return r; }
            }
            if (scheme == "ftp") { r.Error = "Failed to connect to " + host + " port 21 after 0 ms: Couldn't connect to server"; r.CurlCode = 7; return r; }
            int port = m.Groups[3].Success && m.Groups[3].Value.Length > 0 ? int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) : scheme == "https" ? 443 : 80;
            if (host.Length == 0) { r.Error = "URL rejected: No host part in the URL"; r.CurlCode = 3; return r; }
            object target; string ip;
            string err = mach.Connect(host, port, out target, out ip);
            if (err == "resolve") { r.Error = "Could not resolve host: " + host; r.CurlCode = 6; return r; }
            if (err == "refused") { r.Error = "Failed to connect to " + host + " port " + port + " after 0 ms: Couldn't connect to server"; r.CurlCode = 7; return r; }
            if (err == "noroute") { r.Error = "Failed to connect to " + host + " port " + port + " after 3071 ms: No route to host"; r.CurlCode = 7; return r; }
            if (err == "timeout") { r.Error = "Failed to connect to " + host + " port " + port + " after 129834 ms: Connection timed out"; r.CurlCode = 28; return r; }
            if (scheme == "https" && !(target is Machine && ((Machine)target).Listener(port, "tcp") == "httpd.service" && ((Machine)target).PackageInstalled("mod_ssl")))
            {
                r.Error = "(35) OpenSSL/3.2.2: error:0A00010B:SSL routines::wrong version number"; r.CurlCode = 35; return r;
            }
            if (target is string) return Utility(mach.Net, path);
            // Cyber Range: the pretend web apps of a scenario (RangeWeb.cs), and pretend Windows hosts
            if (mach.Net.Range != null)
            {
                var rr = mach.Net.Range.Web(mach, target, port, path, query, PostData);
                if (rr != null) return rr;
                if (target is RangeHost) { r.Error = "Empty reply from server"; r.CurlCode = 52; return r; }
            }
            var tm = (Machine)target;
            string who = tm.Listener(port, "tcp");
            if (who == "httpd.service") return Apache(tm, host, port, path);
            if (who == "nginx.service") return Serve(tm, new PCtx { Cr = new Cred { Uid = 0 }, Domain = "httpd_t" }, "/usr/share/nginx/html", path, "nginx/1.20.1", false);
            if (who != null && who.StartsWith("container:", StringComparison.Ordinal))
            {
                var c = tm.Containers.FirstOrDefault(x => x.Name == who.Substring(10));
                var cp = c == null ? null : c.Ports.FirstOrDefault(x => x.Host == port);
                string root = c == null ? null : c.Kind == "httpd" ? "/var/www/html" : c.Kind == "nginx" ? "/usr/share/nginx/html" : null;
                int listens = c == null ? 0 : c.Kind == "httpd" ? 8080 : c.Kind == "nginx" ? 80 : -1;
                if (c == null || root == null || cp == null || cp.Cont != listens) { r.Error = "Recv failure: Connection reset by peer"; r.CurlCode = 56; return r; }
                var ctx = new PCtx { Cr = new Cred { Uid = 0 }, Domain = "container_t" };
                return Serve(tm, ctx, c.Mount + root, path, c.Kind == "nginx" ? "nginx/1.25.3" : "Apache/2.4.62 (Red Hat Enterprise Linux) OpenSSL/3.2.2", false);
            }
            r.Error = "Empty reply from server"; r.CurlCode = 52;
            return r;
        }

        static HttpReply Utility(LabNet net, string path)
        {
            var r = new HttpReply { Code = 200 };
            FileSys fs; string rel = path;
            if (path.StartsWith("/BaseOS", StringComparison.Ordinal) || path.StartsWith("/AppStream", StringComparison.Ordinal) || path == "/media.repo" || path.StartsWith("/EFI", StringComparison.Ordinal) || path.StartsWith("/images", StringComparison.Ordinal)) fs = net.NfsFs["dvd"];
            else fs = net.NfsFs["www"];
            var parts = rel.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            var n = fs.Root;
            foreach (var p in parts)
            {
                int ino;
                if (n.T != 'd' || !n.Kids.TryGetValue(p, out ino)) { r.Code = 404; r.Reason = "Not Found"; r.Body = Page(404, "Not Found", "The requested URL was not found on this server."); return r; }
                n = fs.Node(ino);
            }
            if (n.T == 'd')
            {
                if (!path.EndsWith("/", StringComparison.Ordinal)) { r.Code = 301; r.Reason = "Moved Permanently"; r.Location = "http://utility.lab.example.com" + path + "/"; r.Body = Page(301, "Moved Permanently", "The document has moved <a href=\"" + r.Location + "\">here</a>."); return r; }
                int idx;
                if (n.Kids.TryGetValue("index.html", out idx)) { r.Body = fs.Node(idx).Text ?? ""; return r; }
                r.Body = Listing(path, n.Kids.Keys.Select(k => k + (fs.Node(n.Kids[k]).T == 'd' ? "/" : "")));
                return r;
            }
            Fill(r, n, Vfs.Base(path));
            return r;
        }

        static void Fill(HttpReply r, Inode n, string name)
        {
            if (n.Text == null && n.BinSize >= 0) { r.BinSize = n.BinSize; r.Body = ""; } else r.Body = n.Text ?? "";
            string ext = name.Contains(".") ? name.Substring(name.LastIndexOf('.') + 1).ToLowerInvariant() : "";
            r.Type = ext == "html" || ext == "htm" ? "text/html; charset=UTF-8" : ext == "rpm" ? "application/x-rpm" : ext == "gz" ? "application/x-gzip" : ext == "xml" ? "text/xml" : ext == "json" ? "application/json" : ext == "css" ? "text/css" : ext == "png" ? "image/png" : ext == "tar" ? "application/x-tar" : "text/plain; charset=UTF-8";
        }

        static string Listing(string path, IEnumerable<string> names)
        {
            var sb = new StringBuilder("<!DOCTYPE HTML PUBLIC \"-//W3C//DTD HTML 3.2 Final//EN\">\n<html>\n <head>\n  <title>Index of " + path + "</title>\n </head>\n <body>\n<h1>Index of " + path + "</h1>\n<ul>");
            sb.Append("<li><a href=\"../\"> Parent Directory</a></li>\n");
            foreach (var k in names.OrderBy(x => x, StringComparer.Ordinal)) sb.Append("<li><a href=\"" + k + "\"> " + k + "</a></li>\n");
            sb.Append("</ul>\n</body></html>\n");
            return sb.ToString();
        }

        static HttpReply Apache(Machine tm, string host, int port, string path)
        {
            var conf = Httpd.Load(tm);
            string docroot = conf.DocRoot;
            var vh = conf.VHosts.Where(v => v[0] == port.ToString(CultureInfo.InvariantCulture) || v[0] == "*").ToList();
            var named = vh.FirstOrDefault(v => v[1].Length > 0 && (v[1] == host || v[1].Split('.')[0] == host.Split('.')[0] && !LabNet.IsIp(host)));
            if (named != null) docroot = named[2]; else if (vh.Count > 0) docroot = vh[0][2];
            foreach (var al in conf.Aliases)
            {
                var p = al.Split(new[] { ' ' }, 2);
                if (path == p[0] || path.StartsWith(p[0].TrimEnd('/') + "/", StringComparison.Ordinal)) { docroot = p[1]; path = "/" + path.Substring(p[0].Length).TrimStart('/'); break; }
            }
            var um = Regex.Match(path, "^/~([^/]+)(/.*)?$");
            if (um.Success && conf.UserDir != null)
            {
                var pw = tm.User(um.Groups[1].Value);
                if (pw == null) { var nf = new HttpReply { Code = 404, Reason = "Not Found" }; nf.Body = Page(404, "Not Found", "The requested URL was not found on this server."); return nf; }
                docroot = pw.Home.TrimEnd('/') + "/" + conf.UserDir;
                path = um.Groups[2].Success && um.Groups[2].Value.Length > 0 ? um.Groups[2].Value : "/";
            }
            var apache = tm.User("apache");
            var cr = apache == null ? new Cred { Uid = 48, Gid = 48, User = "apache" } : tm.CredFor("apache");
            return Serve(tm, new PCtx { Cr = cr, Domain = "httpd_t" }, docroot, path, "Apache/2.4.62 (Red Hat Enterprise Linux)", true);
        }

        /// <summary>Serves a path from a document root as a server process (its user and SELinux domain).</summary>
        static HttpReply Serve(Machine tm, PCtx ctx, string docroot, string path, string server, bool testPage)
        {
            var r = new HttpReply { Code = 200, Server = server };
            Action forbidden = () => { r.Code = 403; r.Reason = "Forbidden"; r.Body = Page(403, "Forbidden", "You don't have permission to access this resource."); r.Type = "text/html; charset=iso-8859-1"; };
            Action notFound = () => { r.Code = 404; r.Reason = "Not Found"; r.Body = Page(404, "Not Found", "The requested URL was not found on this server."); r.Type = "text/html; charset=iso-8859-1"; };
            string full = docroot.TrimEnd('/') + (path.StartsWith("/", StringComparison.Ordinal) ? path : "/" + path);
            Loc l;
            try
            {
                // every folder on the way: search permission and SELinux
                string cur = "";
                foreach (var part in full.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries).Take(full.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries).Length))
                {
                    cur += "/" + part;
                    var dl = tm.V.Resolve(ctx, cur, true);
                    if (!dl.Exists) break;
                    if (dl.Node.T == 'd' && cur.Length >= docroot.TrimEnd('/').Length && !Selinux.Allow(tm, ctx, dl, "read")) { forbidden(); return r; }
                }
                l = tm.V.Resolve(ctx, full, true);
            }
            catch (VfsEx e)
            {
                if (e.Errno == "EACCES") forbidden(); else notFound();
                if (e.Errno == "EACCES") tm.Log("httpd", tm.ServicePid("httpd.service"), 3, "[core:error] (13)Permission denied: [client 192.168.50.11] AH00035: access to " + path + " denied (filesystem path '" + full + "') because search permissions are missing on a component of the path", "httpd.service");
                return r;
            }
            if (!l.Exists)
            {
                if (path == "/" && testPage) { r.Code = 403; r.Reason = "Forbidden"; r.Body = TestPage(); return r; }
                notFound(); return r;
            }
            if (l.IsDir)
            {
                if (!path.EndsWith("/", StringComparison.Ordinal)) { r.Code = 301; r.Reason = "Moved Permanently"; r.Location = path + "/"; r.Body = Page(301, "Moved Permanently", "The document has moved <a href=\"" + path + "/\">here</a>."); return r; }
                int idx;
                if (l.Node.Kids.TryGetValue("index.html", out idx))
                {
                    try { l = tm.V.Resolve(ctx, full.TrimEnd('/') + "/index.html", true); }
                    catch (VfsEx) { forbidden(); return r; }
                }
                else
                {
                    if (path == "/" && testPage) { r.Code = 403; r.Reason = "Forbidden"; r.Body = TestPage(); return r; }
                    if (!tm.V.Can(ctx.Cr, l.Node, 5)) { forbidden(); return r; }
                    r.Body = Listing(path, l.Node.Kids.Keys.Where(k => !k.StartsWith(".", StringComparison.Ordinal)));
                    return r;
                }
            }
            if (!tm.V.Can(ctx.Cr, l.Node, 4))
            {
                forbidden();
                tm.Log("httpd", tm.ServicePid("httpd.service"), 3, "[core:error] (13)Permission denied: [client 192.168.50.11] AH00132: file permissions deny server access: " + l.GPath, "httpd.service");
                return r;
            }
            if (!Selinux.Allow(tm, ctx, l, "read"))
            {
                forbidden();
                tm.Log("httpd", tm.ServicePid("httpd.service"), 3, "[core:error] (13)Permission denied: [client 192.168.50.11] AH00132: file permissions deny server access: " + l.GPath, "httpd.service");
                return r;
            }
            Fill(r, l.Node, l.Name);
            return r;
        }

        static string TestPage()
        {
            return "<!doctype html>\n<html>\n<head>\n<title>HTTP Server Test Page powered by: Red Hat Enterprise Linux</title>\n</head>\n<body>\n<h1>HTTP Server Test Page</h1>\n<p>This page is used to test the proper operation of an HTTP server after it has been installed on a Red Hat Enterprise Linux system. If you can read this page, it means that the software is working correctly.</p>\n<p>(Cert Lab: the server shows this page with status 403 because " + "the document root has no index.html yet.)</p>\n</body>\n</html>\n";
        }

        static string Headers(HttpReply r, Machine m)
        {
            var sb = new StringBuilder();
            sb.Append("HTTP/1.1 " + r.Code + " " + r.Reason + "\n");
            sb.Append("Date: " + DateTime.SpecifyKind(new DateTime(1970, 1, 1).AddSeconds(m.Now), DateTimeKind.Utc).ToString("ddd, dd MMM yyyy HH:mm:ss", CultureInfo.InvariantCulture) + " GMT\n");
            sb.Append("Server: " + r.Server + "\n");
            if (r.Headers.Length > 0) foreach (var h in r.Headers.Replace("\r", "").Split('\n')) if (h.Trim().Length > 0) sb.Append(h.Trim() + "\n");
            if (r.Location != null) sb.Append("Location: " + r.Location + "\n");
            if (r.Code == 200) sb.Append("Last-Modified: " + DateTime.SpecifyKind(new DateTime(1970, 1, 1).AddSeconds(m.Now - 3600), DateTimeKind.Utc).ToString("ddd, dd MMM yyyy HH:mm:ss", CultureInfo.InvariantCulture) + " GMT\nAccept-Ranges: bytes\n");
            sb.Append("Content-Length: " + r.Length + "\n");
            sb.Append("Content-Type: " + r.Type + "\n");
            return sb.ToString();
        }

        static Inode SaveReply(Sh sh, string file, HttpReply r)
        {
            var n = sh.M.V.WriteText(sh.P, file, r.BinSize >= 0 ? "" : r.Body, false);
            if (r.BinSize >= 0) { n.Text = null; n.BinSize = r.BinSize; }
            return n;
        }

        static int Curl(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "sSo:OIiLfvkw:u:H:X:d:m:b:#", "silent|s,show-error|S,output=|o,remote-name|O,head|I,include|i,location|L,fail|f,verbose|v,insecure|k,write-out=|w,user=|u,header=|H,request=|X,data=|d,cookie=|b,max-time=|m,connect-timeout=,retry=,progress-bar|#,compressed,http1.1", "curl");
            if (o.Error != null) { io.E(o.Error.Replace("Try 'curl --help'", "curl: try 'curl --help' or 'curl --manual'")); return 2; }
            if (o.Args.Count == 0) { io.E("curl: try 'curl --help' or 'curl --manual' for more information"); return 2; }
            int st = 0;
            foreach (var url0 in o.Args)
            {
                string url = url0;
                HttpReply r = null;
                for (int hops = 0; hops < 5; hops++)
                {
                    PostData = o.Has("d") ? o.Get("d") : null;
                    CookieData = o.Has("b") && o.Get("b").Contains("=") ? o.Get("b") : null;
                    try { r = Fetch(sh, url); } finally { PostData = null; CookieData = null; }
                    if (r.Error == null && o.Has("L") && r.Location != null && (r.Code == 301 || r.Code == 302)) { url = r.Location.StartsWith("/", StringComparison.Ordinal) ? Regex.Replace(url, "^((?:https?://)?[^/]+).*$", "$1") + r.Location : r.Location; continue; }
                    break;
                }
                if (r.Error != null)
                {
                    if (!o.Has("s") || o.Has("S")) io.E("curl: (" + (r.CurlCode) + ") " + Regex.Replace(r.Error, "^\\(\\d+\\) ", ""));
                    st = r.CurlCode;
                    continue;
                }
                if (o.Has("f") && r.Code >= 400)
                {
                    if (!o.Has("s") || o.Has("S")) io.E("curl: (22) The requested URL returned error: " + r.Code);
                    st = 22; continue;
                }
                string outText = o.Has("I") ? Headers(r, sh.M) : (o.Has("i") ? Headers(r, sh.M) + "\n" : "") + (r.BinSize >= 0 && !o.Has("o") && !o.Has("O") ? "" : r.Body);
                if (r.BinSize >= 0 && !o.Has("o") && !o.Has("O") && !o.Has("I") && io.Out.IsTty)
                    io.E("Warning: Binary output can mess up your terminal. Use \"--output -\" to tell \nWarning: curl to output it to your terminal anyway, or consider \"--output \nWarning: <FILE>\" to save to a file.");
                else if (o.Has("o") || o.Has("O"))
                {
                    string file = o.Has("o") ? o.Get("o") : Vfs.Base(Regex.Replace(url, "[?#].*$", ""));
                    if (file == "-") io.P(outText);
                    else
                    {
                        if (file.Length == 0 || file.Contains(":")) { io.E("curl: Remote file name has no length!"); st = 23; continue; }
                        try { if (o.Has("I")) sh.M.V.WriteText(sh.P, file, outText, false); else SaveReply(sh, file, r); }
                        catch (VfsEx e) { io.E("Warning: Failed to open the file " + file + ": " + VfsEx.Msg(e.Errno) + "\ncurl: (23) Failure writing output to destination"); st = 23; continue; }
                        if (!o.Has("s") && io.Err.IsTty)
                            io.E("  % Total    % Received % Xferd  Average Speed   Time    Time     Time  Current\n                                 Dload  Upload   Total   Spent    Left  Speed\n100 " + r.Length.ToString().PadLeft(5) + "  100 " + r.Length.ToString().PadLeft(5) + "    0     0   1234k      0 --:--:-- --:--:-- --:--:-- 1234k");
                    }
                }
                else io.P(outText);
                if (o.Has("w"))
                {
                    string w = o.Get("w").Replace("%{http_code}", r.Code.ToString(CultureInfo.InvariantCulture)).Replace("%{response_code}", r.Code.ToString(CultureInfo.InvariantCulture)).Replace("%{size_download}", r.Length.ToString(CultureInfo.InvariantCulture)).Replace("%{content_type}", r.Type).Replace("\\n", "\n");
                    io.P(w);
                }
            }
            return st;
        }

        static int Wget(Sh sh, List<string> a, Io io)
        {
            var o = Opts.Parse(a, "O:qP:cnNv", "output-document=|O,quiet|q,directory-prefix=|P,continue|c,no-verbose|n,timestamping|N,no-check-certificate,tries=", "wget");
            if (o.Error != null) { io.E(o.Error.Replace("Try 'wget --help'", "Usage: wget [OPTION]... [URL]...\n\nTry `wget --help'")); return 2; }
            if (o.Args.Count == 0) { io.E("wget: missing URL\nUsage: wget [OPTION]... [URL]...\n\nTry `wget --help' for more options."); return 1; }
            bool quiet = o.Has("q");
            int st = 0;
            foreach (var url0 in o.Args)
            {
                string url = url0.Contains("://") ? url0 : "http://" + url0;
                var um = Regex.Match(url, "^\\w+://([^/:]+)(?::(\\d+))?");
                string host = um.Groups[1].Value;
                string stamp = sh.M.LocalTime(sh.M.Now).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                if (!quiet) io.E("--" + stamp + "--  " + url);
                HttpReply r = null;
                for (int hops = 0; hops < 5; hops++)
                {
                    r = Fetch(sh, url);
                    if (r.Error == null && r.Location != null && (r.Code == 301 || r.Code == 302))
                    {
                        if (!quiet) io.E("HTTP request sent, awaiting response... " + r.Code + " " + r.Reason + "\nLocation: " + r.Location + " [following]");
                        url = r.Location.StartsWith("/", StringComparison.Ordinal) ? Regex.Replace(url, "^(\\w+://[^/]+).*$", "$1") + r.Location : r.Location;
                        if (!quiet) io.E("--" + stamp + "--  " + url);
                        continue;
                    }
                    break;
                }
                string ip = sh.M.Net.Resolve(sh.M, host);
                if (r.CurlCode == 6) { io.E("Resolving " + host + " (" + host + ")... failed: Name or service not known.\nwget: unable to resolve host address '" + host + "'"); st = 4; continue; }
                if (!quiet && !LabNet.IsIp(host)) io.E("Resolving " + host + " (" + host + ")... " + ip);
                string port = um.Groups[2].Success && um.Groups[2].Value.Length > 0 ? um.Groups[2].Value : (url.StartsWith("https", StringComparison.Ordinal) ? "443" : "80");
                if (r.Error != null)
                {
                    io.E("Connecting to " + host + " (" + host + ")|" + ip + "|:" + port + "... failed: " + (r.Error.Contains("No route") ? "No route to host." : r.Error.Contains("timed out") ? "Connection timed out." : r.Error.Contains("reset") ? "Connection reset by peer." : "Connection refused."));
                    st = 4; continue;
                }
                if (!quiet) io.E("Connecting to " + host + " (" + host + ")|" + ip + "|:" + port + "... connected.\nHTTP request sent, awaiting response... " + r.Code + " " + r.Reason);
                if (r.Code >= 400) { io.E(stamp + " ERROR " + r.Code + ": " + r.Reason + "."); st = 8; continue; }
                string path = Regex.Replace(url, "^\\w+://[^/]+", "");
                string name = o.Get("O") ?? (path.EndsWith("/", StringComparison.Ordinal) || path.Length == 0 ? "index.html" : Vfs.Base(Regex.Replace(path, "[?#].*$", "")));
                if (o.Has("P") && !o.Has("O")) name = o.Get("P").TrimEnd('/') + "/" + name;
                if (!o.Has("O"))
                {
                    // wget never overwrites: it adds .1, .2 ...
                    string baseName = name; int k = 0;
                    while (sh.M.V.Exists(Vfs.Global(sh.P, Vfs.Norm(Vfs.Join(sh.P.Cwd, name))))) name = baseName + "." + (++k);
                }
                if (!quiet) io.E("Length: " + r.Length + " [" + r.Type.Split(';')[0] + "]\nSaving to: \u2018" + name + "\u2019\n");
                try
                {
                    if (o.Has("P")) { var dl = sh.M.V.Resolve(sh.P, o.Get("P"), true); if (!dl.Exists) sh.M.V.Mkdir(sh.P, o.Get("P"), 0x1FF, true); }
                    if (name == "-") io.P(r.Body); else SaveReply(sh, name, r);
                }
                catch (VfsEx e) { io.E(name + ": " + VfsEx.Msg(e.Errno)); st = 3; continue; }
                if (!quiet) io.E(Vfs.Base(name).PadRight(20) + "100%[===================>] " + r.Length.ToString().PadLeft(7) + "  --.-KB/s    in 0s      \n\n" + stamp + " (12.3 MB/s) - \u2018" + name + "\u2019 saved [" + r.Length + "/" + r.Length + "]\n");
            }
            return st;
        }
    }
}
