// Cyber Range - the pretend web applications of a scenario or puzzle, answered when curl / wget ask a range machine
// for a page. Kinds: "page" (a fixed page), "login" (a form that shows a page after the right user and password),
// "cmd" (a page that builds a command line from a parameter without checking it: the classic command-injection
// mistake, run inside the simulated machine as the web server's account), "file" (a download page that joins a
// parameter to its folder without checking it: path traversal, read as the web server's account), "idor" (a page
// that shows record number N from its list with no check: pick another number), and "cookie" (a page that trusts a
// cookie the browser sends: set it and the page lets you in). Nothing runs on this PC: a command line runs in the
// simulator's own shell on the pretend server.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public class RangeWebApp
    {
        public string Machine = "", Path = "/", Kind = "page", Param = "", RunAs = "apache", Template = "", Base = "", Title = "", Body = "", Server = "";
        public int Port = 80;
        public List<string[]> Logins = new List<string[]>();   // user, password
        public List<string[]> Records = new List<string[]>();  // idor: rows (the first column is the id)
        public string Fail = "";                                // the page after a wrong login
        public string Cookie = "";                              // cookie kind: "name=value" the page trusts (shows Template)
        public string Want = "";                                // idor/cookie: the row id (or cookie value) that reveals the secret
        public string Header = "";                              // an extra response header (for example "Set-Cookie: ...")
        public Dictionary<string, string> Settings = new Dictionary<string, string>(StringComparer.Ordinal);   // shop / fetch / line kinds
        public RangeShop Shop;                                  // the shop kind's state for this run (accounts, reviews, sessions)
    }

    public partial class RangeNet
    {
        /// <summary>What the practice listening page (catcher.range.example) received: requests a pretend browser made
        /// and mails sent there. Read with the catcher tool.</summary>
        public readonly List<string> Caught = new List<string>();
        public void Catch(string what)
        {
            var t = DateTime.SpecifyKind(new DateTime(1970, 1, 1).AddSeconds(Lab.Now), DateTimeKind.Utc);
            Caught.Add(t.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "  " + what);
            if (Caught.Count > 200) Caught.RemoveAt(0);
        }

        /// <summary>A range app that listens on a port of its own (anything but 80 and 443, which belong to the web
        /// server): a "line" service (nc), or a pretend web app on a port such as 8080. Null when there is none.</summary>
        public string RangeListener(Machine m, int port)
        {
            if (port == 80 || port == 443) return null;
            return WebApps.Any(a => a.Port == port && a.Machine.Equals(m.Name, StringComparison.OrdinalIgnoreCase)) ? "range-app:" + port : null;
        }

        /// <summary>A "line" service (kind line) at a machine and port: it answers each line sent to it (nc). Logins
        /// are pairs [line, reply]; Fail is the reply to anything else. Null when no such service listens.</summary>
        public RangeWebApp LineService(object target, int port)
        {
            string name = target is Machine ? ((Machine)target).Name : target is RangeHost ? ((RangeHost)target).Name : null;
            if (name == null) return null;
            return WebApps.FirstOrDefault(a => a.Kind == "line" && a.Port == port && a.Machine.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        static string Decode(string s)
        {
            try { return Uri.UnescapeDataString((s ?? "").Replace('+', ' ')); } catch (Exception) { return s ?? ""; }
        }
        public static Dictionary<string, string> Form(string query)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var part in (query ?? "").Split('&'))
            {
                if (part.Length == 0) continue;
                int eq = part.IndexOf('=');
                string k = Decode(eq < 0 ? part : part.Substring(0, eq)), v = eq < 0 ? "" : Decode(part.Substring(eq + 1));
                d[k] = v;
            }
            return d;
        }
        /// <summary>The cookies curl --cookie sent with this request.</summary>
        public static Dictionary<string, string> Cookies(string header)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var part in (header ?? "").Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq < 0) continue;
                d[part.Substring(0, eq).Trim()] = part.Substring(eq + 1).Trim();
            }
            return d;
        }
        static string Esc(string s) { return (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;"); }
        static string Html(string title, string body) { return "<!DOCTYPE html>\n<html><head><title>" + Esc(title) + "</title></head>\n<body>\n" + body + "\n</body></html>\n"; }

        /// <summary>A page from a scenario's web app, or null (the normal web server answers).</summary>
        public HttpReply Web(Machine from, object target, int port, string path, string query, string post)
        {
            string name = target is Machine ? ((Machine)target).Name : target is RangeHost ? ((RangeHost)target).Name : null;
            if (name == null) return null;
            // the Web Security Lab's shop answers every path under its own (RangeShop.cs); paths it doesn't own fall through
            var shopApp = target is Machine ? WebApps.FirstOrDefault(a => a.Kind == "shop" && a.Machine.Equals(name, StringComparison.OrdinalIgnoreCase) && a.Port == port && (a.Path == "/" || path == a.Path || path.StartsWith(a.Path.TrimEnd('/') + "/", StringComparison.Ordinal))) : null;
            if (shopApp != null)
            {
                if (shopApp.Shop == null) shopApp.Shop = new RangeShop(this, shopApp);
                var sr = shopApp.Shop.Handle(from, (Machine)target, shopApp.Path == "/" ? path : "/" + path.Substring(shopApp.Path.TrimEnd('/').Length).TrimStart('/'), query ?? "", post, Cmd.CookieData, false);
                if (sr != null) return sr;
            }
            var app = WebApps.FirstOrDefault(a => a.Machine.Equals(name, StringComparison.OrdinalIgnoreCase) && a.Port == port && a.Path == path && a.Kind != "shop" && a.Kind != "line");
            var any = WebApps.FirstOrDefault(a => a.Machine.Equals(name, StringComparison.OrdinalIgnoreCase) && a.Port == port);
            string ip = (target is Machine) ? IpOf((Machine)target) : ((RangeHost)target).Ip;
            if (app == null)
            {
                if (target is RangeHost && any != null)
                {
                    var nf = new HttpReply { Code = 404, Reason = "Not Found", Server = any.Server.Length > 0 ? any.Server : "Microsoft-IIS/10.0", Body = Html("404 - File or directory not found.", "<h2>404 - File or directory not found.</h2>") };
                    return nf;
                }
                if (any != null && any.Server.Length > 0 && target is Machine)
                {
                    // the machine's own pages, with the app's Server header
                    return null;
                }
                return null;
            }
            var r = new HttpReply { Code = 200, Server = app.Server.Length > 0 ? app.Server : (target is RangeHost ? "Microsoft-IIS/10.0" : "Apache/2.4.62 (Red Hat Enterprise Linux)") };
            if (app.Header.Length > 0) r.Headers = app.Header;
            var form = Form(query);
            if (!string.IsNullOrEmpty(post)) foreach (var kv in Form(post)) form[kv.Key] = kv.Value;
            string title = app.Title.Length > 0 ? app.Title : app.Path;
            string v; form.TryGetValue(app.Param, out v);
            switch (app.Kind)
            {
                case "login":
                {
                    string u, p; form.TryGetValue("user", out u); form.TryGetValue("pass", out p);
                    if (u == null && p == null) { r.Body = Html(title, app.Body.Length > 0 ? app.Body : "<h1>" + Esc(title) + "</h1>\n<form method=\"post\" action=\"" + app.Path + "\">\n  User: <input name=\"user\"> Password: <input name=\"pass\" type=\"password\">\n  <button>Sign in</button>\n</form>"); return r; }
                    var ok = app.Logins.FirstOrDefault(l => l[0] == u && l[1] == p);
                    if (ok == null)
                    {
                        Noise(from, ip, "web-login-failed", 2, path + " as " + (u ?? ""));
                        r.Body = Html(title, "<p>" + Esc(app.Fail.Length > 0 ? app.Fail : "The user name or password is not right.") + "</p>");
                        return r;
                    }
                    Noise(from, ip, "web-login", 1, path + " as " + u);
                    if (!Worked.Any(w => w.Host == ip && w.User == u && w.Secret == p)) Worked.Add(new LootItem { Host = ip, User = u, Secret = p, Where = "signed in to " + path, Auto = true });
                    var c = Changed; if (c != null) c();
                    r.Body = Html(title, app.Template);
                    return r;
                }
                case "cookie":
                {
                    // a page that trusts a cookie. With no cookie (or the wrong value) it shows the Body; with the
                    // name=value in Cookie it shows the Template. The classic "the client decides who it is" bug.
                    var cookies = Cookies(Cmd.CookieData);
                    var want = app.Cookie.Split('=');
                    string got; cookies.TryGetValue(want[0], out got);
                    bool admin = want.Length > 1 && got == want[1];
                    Noise(from, ip, admin ? "web" : "web", 1, path + (admin ? " (cookie " + app.Cookie + ")" : ""));
                    r.Body = Html(title, admin ? app.Template : (app.Body.Length > 0 ? app.Body : "<h1>" + Esc(title) + "</h1><p>Please sign in.</p>"));
                    return r;
                }
                case "idor":
                {
                    // shows record number v from the list, with no check that it is yours
                    if (v == null) { r.Body = Html(title, app.Body.Length > 0 ? app.Body : "<h1>" + Esc(title) + "</h1><p>Your record: " + app.Path + "?" + app.Param + "=1</p>"); return r; }
                    var row = app.Records.FirstOrDefault(x => x.Length > 0 && x[0] == v.Trim());
                    if (row == null) { r.Code = 404; r.Reason = "Not Found"; r.Body = Html("404 Not Found", "<h1>No such record.</h1>"); return r; }
                    Noise(from, ip, v.Trim() == app.Want && app.Want.Length > 0 ? "web" : "web", 1, path + "?" + app.Param + "=" + v);
                    var sb = new StringBuilder("<h1>" + Esc(title) + "</h1>\n<table>\n");
                    foreach (var cell in row) sb.Append("<tr><td>" + Esc(cell) + "</td></tr>\n");
                    sb.Append("</table>");
                    r.Body = Html(title, sb.ToString());
                    return r;
                }
                case "cmd":
                {
                    var m = target as Machine;
                    if (m == null) return null;
                    if (v == null) { r.Body = Html(title, app.Body.Length > 0 ? app.Body : "<h1>" + Esc(title) + "</h1>\n<form action=\"" + app.Path + "\"><input name=\"" + app.Param + "\"> <button>Go</button></form>"); return r; }
                    string line = app.Template.Replace("{}", v);
                    bool odd = v.IndexOfAny(new[] { ';', '|', '&', '$', '`', '\n', '>' , '<' }) >= 0;
                    Noise(from, ip, odd ? "web-command" : "web", odd ? 3 : 1, path + "?" + app.Param + "=" + v);
                    var res = m.RunCommand(m.User(app.RunAs) != null ? app.RunAs : "root", line, null, 50000, null);
                    r.Body = Html(title, "<h1>" + Esc(title) + "</h1>\n<pre>\n" + Esc(res.Output) + "</pre>");
                    return r;
                }
                case "file":
                {
                    var m = target as Machine;
                    if (m == null) return null;
                    if (v == null) { r.Body = Html(title, app.Body.Length > 0 ? app.Body : "<h1>" + Esc(title) + "</h1><p>Ask for a file: " + app.Path + "?" + app.Param + "=NAME</p>"); return r; }
                    bool odd = v.Contains("..") || v.StartsWith("/", StringComparison.Ordinal);
                    Noise(from, ip, odd ? "web-traversal" : "web", odd ? 3 : 1, path + "?" + app.Param + "=" + v);
                    string full = Vfs.Norm(app.Base.TrimEnd('/') + "/" + v);
                    var pw = m.User(app.RunAs) ?? m.User("root");
                    var ctx = new PCtx { Cr = m.CredFor(pw.Name), Cwd = "/", Root = "/" };
                    try
                    {
                        string text = m.V.ReadText(ctx, full);
                        r.Type = "application/octet-stream"; r.Body = text;
                    }
                    catch (VfsEx e)
                    {
                        if (e.Errno == "EACCES") { r.Code = 403; r.Reason = "Forbidden"; r.Body = Html("403 Forbidden", "<h1>Forbidden</h1><p>You don't have permission to read that file.</p>"); }
                        else { r.Code = 404; r.Reason = "Not Found"; r.Body = Html("404 Not Found", "<h1>Not Found</h1><p>No such file.</p>"); }
                    }
                    return r;
                }
                case "fetch":
                {
                    // a link preview / "fetch this address for me" page: the server makes the request itself
                    // (server-side request forgery when it doesn't check where the address points)
                    var m = target as Machine;
                    if (m == null) return null;
                    if (v == null) { r.Body = Html(title, app.Body.Length > 0 ? app.Body : "<h1>" + Esc(title) + "</h1><p>Give an address: " + app.Path + "?" + app.Param + "=http://...</p>"); return r; }
                    string host = RangeServerFetch.HostOf(v);
                    Noise(from, ip, host == "169.254.169.254" || RangeServerFetch.IsLoopback(host) ? "web-command" : "web", 2, path + "?" + app.Param + "=" + v);
                    string err; var got = RangeServerFetch.Get(this, m, v, null, out err);
                    if (got == null) { r.Code = 502; r.Reason = "Bad Gateway"; r.Body = Html(title, "<h1>" + Esc(title) + "</h1><p>Couldn't fetch that address: " + Esc(err) + "</p>"); return r; }
                    r.Body = Html(title, "<h1>" + Esc(title) + "</h1>\n<p>Preview of " + Esc(v) + ":</p>\n<pre>\n" + Esc(RangeShop.StripTags(got.Body)) + "</pre>");
                    return r;
                }
                default:
                    Noise(from, ip, "web", 1, path);
                    r.Body = app.Body.StartsWith("<", StringComparison.Ordinal) ? app.Body : Html(title, app.Body);
                    return r;
            }
        }
    }
}
