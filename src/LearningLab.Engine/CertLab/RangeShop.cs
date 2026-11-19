// Cyber Range - Copperleaf Market, the Web Security Lab's pretend online shop (web app kind "shop"), and the pretend
// requests a server makes for itself (kind "fetch", the shop's stock checker and the staff browser). The shop is one
// app whose classic mistakes are switched on per challenge with its "weak" setting (Range\FORMAT.md lists them):
// order numbers anyone can change, cookies and tokens the browser can rewrite, a login and a search that glue what
// you type into a pretend database query (RangeSql: a tiny made-up query language, not a real database), scripts in
// reviews and search results that a pretend staff browser "runs" (it only looks for the textbook signs and records a
// visit to the practice listening page, catcher), a picture page that joins ../ to its folder, a stock checker that
// fetches any address (server-side request forgery), an avatar upload that runs pretend server pages, guessable
// reset codes, forms with no anti-forgery check, sign-up forms that copy every field, a debug page left on. Nothing
// here runs a real script, query or request: everything is text inside the simulator.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CertLab
{
    public class RangeShopAccount { public string User = "", Pass = "", Role = "customer", Email = ""; public Dictionary<string, string> Extra = new Dictionary<string, string>(StringComparer.Ordinal); }

    public class RangeShop
    {
        public readonly RangeNet Net;
        public readonly RangeWebApp App;
        public readonly List<RangeShopAccount> Accounts = new List<RangeShopAccount>();
        public readonly List<string[]> Orders = new List<string[]>();      // id, owner, items, note
        public readonly List<string[]> Products = new List<string[]>();    // id, name, price, description
        public readonly List<string[]> Reviews = new List<string[]>();     // product id, author, text
        public readonly Dictionary<string, string> Sessions = new Dictionary<string, string>(StringComparer.Ordinal);   // opaque token -> user
        public readonly Dictionary<string, string> ResetCodes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);  // user -> code
        public readonly List<string[]> Inbox = new List<string[]>();       // to user, subject, body
        public readonly HashSet<string> Weak = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int nextSeq;
        string secret;
        int depth;

        public const string Brand = "Copperleaf Market";
        public const string ServerName = "Copperleaf/2.3 (a pretend shop - Cyber Range)";
        public const string Catcher = "catcher.range.example";

        public string S(string k) { string v; return App.Settings.TryGetValue(k, out v) ? v : ""; }
        public string S(string k, string def) { var v = S(k); return v.Length == 0 ? def : v; }

        public RangeShop(RangeNet net, RangeWebApp app)
        {
            Net = net; App = app;
            foreach (var w in S("weak").Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)) Weak.Add(w.Trim());
            secret = RangeData.Hex("shop|" + string.Join("|", app.Settings.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "=" + kv.Value).ToArray()), 16);
            if (S("me").Length > 0) Accounts.Add(new RangeShopAccount { User = S("me"), Pass = S("mypass"), Role = "customer", Email = S("me") + "@mail.example" });
            foreach (var rec in S("accounts").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var f = rec.Split('|');
                if (f.Length < 2) continue;
                Accounts.Add(new RangeShopAccount { User = f[0].Trim(), Pass = f[1].Trim(), Role = f.Length > 2 && f[2].Trim().Length > 0 ? f[2].Trim() : "customer", Email = f.Length > 3 ? f[3].Trim() : f[0].Trim() + "@copperleaf.example" });
            }
            foreach (var rec in S("orders").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var f = rec.Split('|');
                if (f.Length >= 3) Orders.Add(new[] { f[0].Trim(), f[1].Trim(), f[2].Trim(), f.Length > 3 ? f[3].Trim() : "" });
            }
            string[][] stock = {
                new[] { "1", "Trail tent", "89.00", "A two-person tent that packs small." },
                new[] { "2", "Camp kettle", "24.50", "Boils a litre in four minutes." },
                new[] { "3", "Wool socks", "12.00", "Warm, soft, and they never itch." },
                new[] { "4", "Folding lantern", "18.75", "Bright light, folds flat." },
                new[] { "5", "Map case", "9.99", "Keeps your maps dry in the rain." },
                new[] { "6", "Enamel mug", "7.25", "Speckled blue, holds a big cup of cocoa." } };
            Products.AddRange(stock);
            Reviews.Add(new[] { "2", "harper", "Boils quickly. Lid rattles a bit." });
            Reviews.Add(new[] { "3", "quinn", "Best socks I own." });
            int seq; nextSeq = int.TryParse(S("seqstart"), NumberStyles.Integer, CultureInfo.InvariantCulture, out seq) ? seq : 4100;
            // the staff asked for a reset code earlier today (the sequential style shows it as the number before yours)
            var staff = StaffAccount();
            if (staff != null && S("reset").Length > 0) ResetCodes[staff.User] = Code(staff.User);
        }

        public RangeShopAccount Find(string user) { return Accounts.FirstOrDefault(a => a.User.Equals(user ?? "", StringComparison.OrdinalIgnoreCase)); }
        public RangeShopAccount StaffAccount() { return Accounts.FirstOrDefault(a => IsStaff(a.Role)); }
        static bool IsStaff(string role) { var r = (role ?? "").ToLowerInvariant(); return r == "admin" || r == "staff" || r == "manager"; }

        // ------------------------------------------------------------ sessions and tokens
        static string B64(string s) { return Convert.ToBase64String(Encoding.UTF8.GetBytes(s)); }
        static string B64Url(string s) { return B64(s).TrimEnd('=').Replace('+', '-').Replace('/', '_'); }
        static string UnB64(string s)
        {
            try
            {
                string t = (s ?? "").Trim().Replace('-', '+').Replace('_', '/');
                t = Uri.UnescapeDataString(t);
                while (t.Length % 4 != 0) t += "=";
                return Encoding.UTF8.GetString(Convert.FromBase64String(t));
            }
            catch (Exception) { return null; }
        }
        string Sign(string data) { return RangeData.Hex("sig|" + secret + "|" + data, 22); }

        /// <summary>The cookie a signed-in account gets: name=value.</summary>
        public string SessionCookie(RangeShopAccount a)
        {
            if (Weak.Contains("b64cookie")) return "session=" + B64("user=" + a.User + ";role=" + a.Role);
            if (Weak.Contains("tokennone"))
            {
                string head = B64Url("{\"alg\":\"HS256\",\"typ\":\"CLT\"}"), body = B64Url("{\"user\":\"" + a.User + "\",\"role\":\"" + a.Role + "\"}");
                return "token=" + head + "." + body + "." + Sign(head + "." + body);
            }
            string tok = "s" + RangeData.Hex("sess|" + secret + "|" + a.User.ToLowerInvariant(), 24);
            Sessions[tok] = a.User;
            return "session=" + tok;
        }

        /// <summary>Who the request's cookies say is signed in (user, role), or null.</summary>
        public string[] Who(string cookieHeader)
        {
            var c = RangeNet.Cookies(cookieHeader);
            string v;
            if (Weak.Contains("b64cookie") && c.TryGetValue("session", out v))
            {
                // the mistake: the cookie is only base64, and the shop believes what it says
                var t = UnB64(v); if (t == null) return null;
                string user = null, role = "customer";
                foreach (var part in t.Split(';', '&'))
                {
                    int eq = part.IndexOf('='); if (eq < 0) continue;
                    string k = part.Substring(0, eq).Trim().ToLowerInvariant(), val = part.Substring(eq + 1).Trim();
                    if (k == "user") user = val; else if (k == "role") role = val;
                }
                return user == null ? null : new[] { user, role };
            }
            if (Weak.Contains("tokennone") && c.TryGetValue("token", out v))
            {
                var p = v.Split('.');
                if (p.Length < 2) return null;
                string head = UnB64(p[0]), body = UnB64(p[1]);
                if (head == null || body == null) return null;
                var alg = Regex.Match(head, "\"alg\"\\s*:\\s*\"([^\"]*)\"");
                string a = alg.Success ? alg.Groups[1].Value : "";
                // the mistake: "none" means "no signature to check", and the shop accepts that
                bool ok = a.Equals("none", StringComparison.OrdinalIgnoreCase) || (a == "HS256" && p.Length == 3 && p[2] == Sign(p[0] + "." + p[1]));
                if (!ok) return null;
                var u = Regex.Match(body, "\"user\"\\s*:\\s*\"([^\"]*)\""); var r = Regex.Match(body, "\"role\"\\s*:\\s*\"([^\"]*)\"");
                return u.Success ? new[] { u.Groups[1].Value, r.Success ? r.Groups[1].Value : "customer" } : null;
            }
            if (c.TryGetValue("session", out v))
            {
                string user;
                if (Sessions.TryGetValue(v, out user)) { var acc = Find(user); if (acc != null) return new[] { acc.User, acc.Role }; }
                // a session made before this request (the staff browser's) is the same token every time
                foreach (var acc in Accounts) if ("s" + RangeData.Hex("sess|" + secret + "|" + acc.User.ToLowerInvariant(), 24) == v) return new[] { acc.User, acc.Role };
            }
            return null;
        }

        string Code(string user)
        {
            switch (S("reset"))
            {
                case "b64": return B64(user);
                case "hex": return string.Concat(Encoding.UTF8.GetBytes(user).Select(b => b.ToString("x2")).ToArray());
                case "rev": { var a = user.ToCharArray(); Array.Reverse(a); return new string(a) + "-" + user.Length; }
                case "seq": return (nextSeq++).ToString(CultureInfo.InvariantCulture);
            }
            return RangeData.Hex("reset|" + secret + "|" + user + "|" + nextSeq++, 20);
        }

        // ------------------------------------------------------------ pages
        static string Esc(string s) { return (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;"); }
        string Page(string title, string body)
        {
            string name = S("name", Brand);
            return "<!DOCTYPE html>\n<html><head><title>" + Esc(title) + " - " + Esc(name) + "</title></head>\n<body>\n<header><b>" + Esc(name) + "</b> | <a href=\"/\">Home</a> | <a href=\"/products\">Products</a> | <a href=\"/search\">Search</a> | <a href=\"/login\">Sign in</a> | <a href=\"/account\">My account</a></header>\n<main>\n" + body + "\n</main>\n<footer>" + Esc(name) + " is a pretend shop inside Cyber Range. Nothing here is real.</footer>\n</body></html>\n";
        }
        HttpReply Reply(int code, string title, string body)
        {
            var r = new HttpReply { Code = code, Server = ServerName, Body = Page(title, body) };
            r.Reason = code == 200 ? "OK" : code == 302 ? "Found" : code == 400 ? "Bad Request" : code == 401 ? "Unauthorized" : code == 403 ? "Forbidden" : code == 404 ? "Not Found" : code == 500 ? "Internal Server Error" : "OK";
            return r;
        }
        HttpReply Text(int code, string type, string text) { var r = Reply(code, "", ""); r.Body = text; r.Type = type; return r; }

        /// <summary>A request to the shop. internalCall: made by the server itself (the stock checker, the staff
        /// browser), which may see the inside pages. Returns null for paths the shop doesn't own (the web server's
        /// own files under /var/www/html then answer).</summary>
        public HttpReply Handle(Machine from, Machine server, string path, string query, string post, string cookies, bool internalCall)
        {
            var form = RangeNet.Form(query);
            if (!string.IsNullOrEmpty(post)) foreach (var kv in RangeNet.Form(post)) form[kv.Key] = kv.Value;
            string ip = Net.IpOf(server);
            Func<string, string> F = k => { string v; return form.TryGetValue(k, out v) ? v : null; };
            var who = Who(cookies);
            string p = path.Length > 1 ? path.TrimEnd('/') : path;
            if (from != null) Net.Noise(from, ip, "web", 1, path + (query.Length > 0 ? "?" + query : ""));
            string hidden = S("hidden");
            if (hidden.Length > 0 && p.Equals("/" + hidden.Trim('/'), StringComparison.OrdinalIgnoreCase))
                return Reply(200, "Staff corner", "<h1>Staff corner</h1>\n<p>Not linked from anywhere, so nobody will find it. Right?</p>\n<p>Staff code: " + Esc(S("flag")) + "</p>");
            // the inside page: only the server's own requests may open it (the stock checker can be talked into it)
            if (p.Equals("/" + S("internal", "internal/admin").Trim('/'), StringComparison.OrdinalIgnoreCase))
            {
                if (!internalCall) return Reply(403, "Forbidden", "<h1>Forbidden</h1><p>This page only answers requests from the shop's own server.</p>");
                return Reply(200, "Internal admin", "<h1>Internal admin</h1><p>Only the server itself can open this page.</p><p>Staff code: " + Esc(S("flag")) + "</p>");
            }
            switch (p.ToLowerInvariant())
            {
                case "/": case "/index.html":
                {
                    var sb = new StringBuilder("<h1>Welcome to " + Esc(S("name", Brand)) + "</h1>\n<p>Outdoor gear for families, shipped to your door.</p>\n<ul>\n");
                    foreach (var pr in Products) sb.Append("  <li><a href=\"/product?id=" + pr[0] + "\">" + Esc(pr[1]) + "</a> - $" + pr[2] + "</li>\n");
                    sb.Append("</ul>\n<p>New here? <a href=\"/register\">Make an account</a>. Forgot your password? <a href=\"/reset\">Reset it</a>.</p>\n");
                    if (S("comment").Length > 0) sb.Append("<!-- " + S("comment") + " -->\n");
                    if (S("appjs").Length > 0) sb.Append("<script src=\"/static/app.js\"></script>\n");
                    return Reply(200, "Home", sb.ToString());
                }
                case "/robots.txt":
                    if (S("robots").Length == 0) return null;
                    return Text(200, "text/plain; charset=UTF-8", S("robots").Replace("\\n", "\n") + "\n");
                case "/static/app.js":
                    if (S("appjs").Length == 0) return null;
                    return Text(200, "application/javascript", S("appjs").Replace("\\n", "\n") + "\n");
                case "/products":
                {
                    var sb = new StringBuilder("<h1>Products</h1>\n<table>\n");
                    foreach (var pr in Products) sb.Append("<tr><td>" + pr[0] + "</td><td><a href=\"/product?id=" + pr[0] + "\">" + Esc(pr[1]) + "</a></td><td>$" + pr[2] + "</td></tr>\n");
                    return Reply(200, "Products", sb.Append("</table>").ToString());
                }
                case "/product": return ProductPage(F("id"));
                case "/search": return Search(F("q"), from, ip);
                case "/login": return Login(F("user"), F("pass"), from, ip);
                case "/logout": { var r = Reply(200, "Signed out", "<p>You are signed out.</p>"); r.Headers = "Set-Cookie: session=; Max-Age=0"; return r; }
                case "/register": return Register(form);
                case "/account": return Account(who);
                case "/api/me": return ApiMe(who);
                case "/admin": return Admin(who);
                case "/orders": return Order(F("id"), F("ref"), who);
                case "/review": return Review(F("product"), F("text"), who, server);
                case "/report": return ReportLink(F("url"), server);
                case "/account/email": return ChangeEmail(F("email"), who);
                case "/inbox": return InboxPage(who);
                case "/reset": return Reset(F("user"), who);
                case "/reset/confirm": return ResetConfirm(F("user"), F("code"), F("new"));
                case "/image": return Image(F("file"), server, from, ip);
                case "/stock": return Stock(F("url"), server, from, ip);
                case "/upload": return Upload(F("name"), F("content"), F("type"), who, server, from, ip);
                case "/debug":
                    if (!Weak.Contains("debug")) return null;
                    return Reply(200, "Debug", "<h1>Debug mode is ON</h1>\n<pre>\nSHOP_ENV=development\nSHOP_DB=/var/lib/copperleaf/shop.db\nSHOP_ADMIN_USER=" + Esc(StaffAccount() == null ? "" : StaffAccount().User) + "\nSHOP_ADMIN_PASSWORD=" + Esc(StaffAccount() == null ? "" : StaffAccount().Pass) + "\nSHOP_MAIL=mail.copperleaf.example\n</pre>\n<p>Remember to switch this off before going live!</p>");
                case "/docs":
                    if (S("docs").Length == 0) return null;
                    return Reply(200, "Setup guide", "<h1>Shop setup guide</h1>\n<p>" + Esc(S("docs")) + "</p>");
            }
            if (p.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase)) return RunUpload(p, server, from, ip);
            return null;
        }

        HttpReply ProductPage(string id)
        {
            var pr = Products.FirstOrDefault(x => x[0] == (id ?? "").Trim());
            if (pr == null) return Reply(404, "Not found", "<h1>No such product.</h1>");
            var sb = new StringBuilder("<h1>" + Esc(pr[1]) + "</h1>\n<p>" + Esc(pr[3]) + "</p>\n<p>Price: $" + pr[2] + "</p>\n");
            sb.Append("<p><a href=\"/stock?url=http://stock.copperleaf.internal/check%3Fitem%3D" + pr[0] + "\">Check stock at our warehouse</a></p>\n<h2>Reviews</h2>\n");
            foreach (var rv in Reviews.Where(x => x[0] == pr[0]))
                sb.Append("<div class=\"review\"><b>" + Esc(rv[1]) + "</b>: " + (Weak.Contains("xss-stored") ? rv[2] : Esc(rv[2])) + "</div>\n");
            sb.Append("<form method=\"post\" action=\"/review\"><input type=\"hidden\" name=\"product\" value=\"" + pr[0] + "\"><textarea name=\"text\"></textarea><button>Post a review</button></form>");
            return Reply(200, pr[1], sb.ToString());
        }

        // ------------------------------------------------------------ the pretend database
        public RangeSql Db()
        {
            var db = new RangeSql();
            db.Add("customers", new[] { "user", "pass", "role", "email" }, Accounts.Select(a => new[] { a.User, a.Pass, a.Role, a.Email }));
            db.Add("products", new[] { "name", "price", "description" }, Products.Select(x => new[] { x[1], x[2], x[3] }));
            db.Add("orders", new[] { "id", "owner", "items", "note" }, Orders);
            if (S("sqltable").Length > 0)
            {
                var cols = S("sqlcols", "title,body").Split(',').Select(x => x.Trim()).ToArray();
                db.Add(S("sqltable"), cols, S("sqlrows").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(r => r.Split('|').Select(x => x.Trim()).ToArray()));
            }
            db.AddCatalog();
            return db;
        }

        HttpReply Search(string q, Machine from, string ip)
        {
            if (q == null) return Reply(200, "Search", "<h1>Search the shop</h1>\n<form action=\"/search\"><input name=\"q\"> <button>Search</button></form>");
            string shown = q;
            if (Weak.Contains("xss-reflected"))
            {
                string filter = S("xssfilter");
                if (filter == "lower") shown = shown.Replace("<script>", "").Replace("</script>", "");
                else if (filter == "anycase") shown = Regex.Replace(shown, "<\\s*/?\\s*script[^>]*>", "", RegexOptions.IgnoreCase);
            }
            string echo = Weak.Contains("xss-reflected") ? shown : Esc(q);
            var sb = new StringBuilder("<h1>Search</h1>\n<p>Results for: " + echo + "</p>\n");
            if (Weak.Contains("sqli-search"))
            {
                string query = "SELECT name, price FROM products WHERE name LIKE '%" + q + "%'";
                if (from != null && (q.Contains("'") || q.ToUpperInvariant().Contains("UNION"))) Net.Noise(from, ip, "web-command", 3, "search " + q);
                string err; var rows = Db().Run(query, out err);
                if (err != null) return Reply(500, "Error", "<h1>Something went wrong</h1>\n<pre>Database error: " + Esc(err) + "\nQuery: " + Esc(query) + "</pre>");
                sb.Append("<table>\n");
                foreach (var r in rows) sb.Append("<tr>" + string.Concat(r.Select(c => "<td>" + Esc(c) + "</td>").ToArray()) + "</tr>\n");
                sb.Append("</table>\n<p>" + rows.Count + " result" + (rows.Count == 1 ? "" : "s") + "</p>");
            }
            else
            {
                var hits = Products.Where(x => x[1].IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                sb.Append("<ul>\n");
                foreach (var h in hits) sb.Append("<li><a href=\"/product?id=" + h[0] + "\">" + Esc(h[1]) + "</a> - $" + h[2] + "</li>\n");
                sb.Append("</ul>\n<p>" + hits.Count + " result" + (hits.Count == 1 ? "" : "s") + "</p>");
            }
            return Reply(200, "Search", sb.ToString());
        }

        HttpReply Login(string user, string pass, Machine from, string ip)
        {
            if (user == null && pass == null) return Reply(200, "Sign in", "<h1>Sign in</h1>\n<form method=\"post\" action=\"/login\">User: <input name=\"user\"> Password: <input name=\"pass\" type=\"password\"> <button>Sign in</button></form>\n<p>(With curl: curl -d \"user=NAME&amp;pass=PASSWORD\" " + "http://" + S("host", "copperleaf.example") + "/login)</p>");
            user = user ?? ""; pass = pass ?? "";
            RangeShopAccount acc = null;
            if (Weak.Contains("sqli-login"))
            {
                // the mistake: what you type becomes part of the query text
                string query = "SELECT user, role FROM customers WHERE user = '" + user + "' AND pass = '" + pass + "'";
                if (from != null && (user + pass).Contains("'")) Net.Noise(from, ip, "web-command", 3, "login " + user);
                string err; var rows = Db().Run(query, out err);
                if (err != null) return Reply(500, "Error", "<h1>Something went wrong</h1>\n<pre>Database error: " + Esc(err) + "\nQuery: " + Esc(query) + "</pre>");
                if (rows.Count > 0) acc = Find(rows[0][0]) ?? new RangeShopAccount { User = rows[0][0], Role = rows[0].Length > 1 ? rows[0][1] : "customer" };
            }
            else
            {
                var a = Find(user);
                if (a != null && a.Pass == pass) acc = a;
            }
            if (acc == null)
            {
                if (from != null) Net.Noise(from, ip, "web-login-failed", 2, "/login as " + user);
                return Reply(401, "Sign in", "<h1>Sign in</h1><p>The user name or password is not right.</p>");
            }
            if (from != null) Net.Noise(from, ip, "web-login", 1, "/login as " + acc.User);
            string cookie = SessionCookie(acc);
            var r = Reply(200, "Signed in", "<h1>Hello, " + Esc(acc.User) + "!</h1>\n<p>You are signed in. Your browser keeps this cookie (send it back with curl -b):</p>\n<pre>" + Esc(cookie) + "</pre>\n<p><a href=\"/account\">My account</a>" + (IsStaff(acc.Role) ? " | <a href=\"/admin\">Staff dashboard</a>" : "") + "</p>");
            r.Headers = "Set-Cookie: " + cookie + "; Path=/";
            return r;
        }

        HttpReply Register(Dictionary<string, string> form)
        {
            string u, pw, em;
            form.TryGetValue("user", out u); form.TryGetValue("pass", out pw); form.TryGetValue("email", out em);
            if (u == null) return Reply(200, "Make an account", "<h1>Make an account</h1>\n<form method=\"post\" action=\"/register\">User: <input name=\"user\"> Password: <input name=\"pass\" type=\"password\"> Email: <input name=\"email\"> <button>Sign up</button></form>");
            if (!Regex.IsMatch(u, "^[A-Za-z0-9_.-]{3,24}$")) return Reply(400, "Make an account", "<p>Pick a user name of 3 to 24 letters or digits.</p>");
            if (Find(u) != null) return Reply(400, "Make an account", "<p>That user name is taken.</p>");
            if (string.IsNullOrEmpty(pw)) return Reply(400, "Make an account", "<p>Pick a password.</p>");
            var a = new RangeShopAccount { User = u, Pass = pw, Email = em ?? "" };
            if (Weak.Contains("mass"))
            {
                // the mistake: every field in the form is copied onto the new account, including ones the form never shows
                string field = S("field", "isAdmin");
                foreach (var kv in form)
                {
                    if (kv.Key == "user" || kv.Key == "pass" || kv.Key == "email") continue;
                    a.Extra[kv.Key] = kv.Value;
                    if (kv.Key.Equals(field, StringComparison.OrdinalIgnoreCase))
                    {
                        string v = kv.Value.Trim().ToLowerInvariant();
                        if (field.Equals("role", StringComparison.OrdinalIgnoreCase)) a.Role = kv.Value.Trim();
                        else if (v == "true" || v == "1" || v == "yes") a.Role = "admin";
                    }
                }
            }
            Accounts.Add(a);
            return Reply(200, "Welcome", "<h1>Welcome, " + Esc(u) + "!</h1><p>Your account is ready. <a href=\"/login\">Sign in</a>.</p>");
        }

        HttpReply Account(string[] who)
        {
            if (who == null) return Reply(401, "My account", "<h1>My account</h1><p>Please <a href=\"/login\">sign in</a> first (send your cookie with curl -b).</p>");
            var a = Find(who[0]);
            var sb = new StringBuilder("<h1>My account</h1>\n<p>Signed in as <b>" + Esc(who[0]) + "</b> (" + Esc(who[1]) + ")</p>\n");
            if (a != null) sb.Append("<p>Email: " + Esc(a.Email) + "</p>\n<form method=\"post\" action=\"/account/email\">New email: <input name=\"email\"> <button>Change email</button></form>\n");
            var mine = Orders.Where(o => o[1].Equals(who[0], StringComparison.OrdinalIgnoreCase)).ToList();
            if (mine.Count > 0)
            {
                sb.Append("<h2>Your orders</h2>\n<ul>\n");
                foreach (var o in mine) sb.Append("<li><a href=\"/orders?" + (S("idor") == "b64" ? "ref=" + B64(o[0]) : "id=" + o[0]) + "\">Order " + Esc(o[0]) + "</a></li>\n");
                sb.Append("</ul>\n");
            }
            sb.Append("<p><a href=\"/inbox\">Your messages</a> | <a href=\"/api/me\">Your details as data</a>" + (IsStaff(who[1]) ? " | <a href=\"/admin\">Staff dashboard</a>" : "") + "</p>");
            return Reply(200, "My account", sb.ToString());
        }

        HttpReply ApiMe(string[] who)
        {
            if (who == null) return Text(401, "application/json", "{\"error\": \"sign in first\"}\n");
            var a = Find(who[0]);
            string field = S("field", "isAdmin");
            var sb = new StringBuilder("{\n  \"user\": \"" + who[0] + "\",\n  \"email\": \"" + (a == null ? "" : a.Email) + "\",\n");
            if (field.Equals("role", StringComparison.OrdinalIgnoreCase)) sb.Append("  \"role\": \"" + who[1] + "\"\n}\n");
            else sb.Append("  \"" + field + "\": " + (IsStaff(who[1]) ? "true" : "false") + "\n}\n");
            return Text(200, "application/json", sb.ToString());
        }

        HttpReply Admin(string[] who)
        {
            if (who == null || !IsStaff(who[1])) return Reply(403, "Staff only", "<h1>Staff only</h1><p>This dashboard is for shop staff. You are " + (who == null ? "not signed in" : "signed in as " + Esc(who[0]) + ", a " + Esc(who[1])) + ".</p>");
            return Reply(200, "Staff dashboard", "<h1>Staff dashboard</h1>\n<p>Welcome back, " + Esc(who[0]) + ".</p>\n<p>Orders today: 14. Reviews waiting: " + Reviews.Count + ".</p>\n<p>Staff code: " + Esc(S("flag")) + "</p>");
        }

        HttpReply Order(string id, string reference, string[] who)
        {
            if (reference != null && id == null) { id = UnB64(reference); if (id == null) return Reply(400, "Order", "<p>That order reference isn't valid.</p>"); }
            if (id == null) return Reply(200, "Orders", "<h1>Orders</h1><p>Open one of your orders from <a href=\"/account\">My account</a>.</p>");
            var o = Orders.FirstOrDefault(x => x[0] == id.Trim());
            if (o == null) return Reply(404, "Order", "<h1>No such order.</h1>");
            // the mistake (weak "idor"): the shop never checks the order is yours
            if (!Weak.Contains("idor") && (who == null || !who[0].Equals(o[1], StringComparison.OrdinalIgnoreCase) && !IsStaff(who[1])))
                return Reply(403, "Order", "<h1>That isn't your order.</h1>");
            return Reply(200, "Order " + o[0], "<h1>Order " + Esc(o[0]) + "</h1>\n<table>\n<tr><td>Customer</td><td>" + Esc(o[1]) + "</td></tr>\n<tr><td>Items</td><td>" + Esc(o[2]) + "</td></tr>\n<tr><td>Note</td><td>" + Esc(o[3]) + "</td></tr>\n</table>");
        }

        HttpReply Review(string product, string text, string[] who, Machine server)
        {
            if (product == null || text == null) return Reply(400, "Review", "<p>Send product=N and text=... (curl -d).</p>");
            if (Products.All(x => x[0] != product.Trim())) return Reply(404, "Review", "<h1>No such product.</h1>");
            Reviews.Add(new[] { product.Trim(), who == null ? "guest" : who[0], text });
            string note = "";
            if (Weak.Contains("xss-stored"))
            {
                // a member of staff reads every new review in their own (pretend) browser, signed in as staff
                note = StaffReads("http://localhost/product?id=" + product.Trim(), server);
            }
            return Reply(200, "Thanks", "<h1>Thanks for your review!</h1><p>A member of staff reads every new review within a minute.</p>" + (note.Length > 0 ? "\n<!-- " + note + " -->" : ""));
        }

        HttpReply ReportLink(string url, Machine server)
        {
            if (url == null) return Reply(200, "Report a problem", "<h1>Report a problem</h1><p>Send us a link (url=...) and a member of staff will look at it.</p>");
            if (!Weak.Contains("xss-reflected") && !Weak.Contains("csrf")) return Reply(200, "Thanks", "<p>Thanks, we'll look into it next week.</p>");
            StaffReads(url, server);
            return Reply(200, "Thanks", "<h1>Thanks!</h1><p>A member of staff opened your link just now.</p>");
        }

        HttpReply ChangeEmail(string email, string[] who)
        {
            if (who == null) return Reply(401, "Change email", "<p>Please sign in first.</p>");
            if (email == null) return Reply(400, "Change email", "<p>Send email=... to change it.</p>");
            var a = Find(who[0]);
            if (a == null) return Reply(404, "Change email", "<p>No such account.</p>");
            a.Email = email.Trim();
            if (IsStaff(a.Role)) Mail(a.Email, "Your staff email was changed", "Hello " + a.User + ", your Copperleaf staff account now uses this address. Your staff recovery code is " + S("flag") + ".");
            return Reply(200, "Change email", "<p>Your email is now " + Esc(a.Email) + ".</p>");
        }

        void Mail(string to, string subject, string body)
        {
            Inbox.Add(new[] { to, subject, body });
            if (to.EndsWith("@" + Catcher, StringComparison.OrdinalIgnoreCase)) Net.Catch("MAIL to " + to + " | " + subject + " | " + body);
        }

        HttpReply InboxPage(string[] who)
        {
            if (who == null) return Reply(401, "Messages", "<p>Please sign in first.</p>");
            var a = Find(who[0]);
            var sb = new StringBuilder("<h1>Your messages</h1>\n");
            foreach (var m in Inbox.Where(x => a != null && x[0].Equals(a.Email, StringComparison.OrdinalIgnoreCase)))
                sb.Append("<div class=\"mail\"><b>" + Esc(m[1]) + "</b><p>" + Esc(m[2]) + "</p></div>\n");
            return Reply(200, "Messages", sb.ToString());
        }

        HttpReply Reset(string user, string[] who)
        {
            if (user == null) return Reply(200, "Reset your password", "<h1>Forgot your password?</h1><form method=\"post\" action=\"/reset\">User: <input name=\"user\"> <button>Send me a reset code</button></form>");
            var a = Find(user);
            if (a == null) return Reply(200, "Reset", "<p>If that account exists, we sent it a reset code.</p>");
            string code = Code(a.User);
            ResetCodes[a.User] = code;
            Mail(a.Email, "Your reset code", "Use code " + code + " at /reset/confirm?user=" + a.User + "&code=" + code + "&new=NEWPASSWORD");
            return Reply(200, "Reset", "<p>If that account exists, we sent it a reset code. (Your messages: /inbox)</p>");
        }

        HttpReply ResetConfirm(string user, string code, string pw)
        {
            if (user == null || code == null || string.IsNullOrEmpty(pw)) return Reply(400, "Reset", "<p>Send user=, code= and new= (the new password).</p>");
            var a = Find(user);
            string want;
            if (a == null || !ResetCodes.TryGetValue(a.User, out want) || want != code.Trim()) return Reply(403, "Reset", "<h1>That code isn't right.</h1>");
            a.Pass = pw; ResetCodes.Remove(a.User);
            return Reply(200, "Reset", "<h1>Done.</h1><p>The password for " + Esc(a.User) + " is changed. <a href=\"/login\">Sign in</a>.</p>");
        }

        HttpReply Image(string file, Machine server, Machine from, string ip)
        {
            if (file == null) return Reply(200, "Pictures", "<p>Ask for a picture: /image?file=tent.png</p>");
            string f = file;
            if (S("traversalfilter") == "once") f = f.Replace("../", "");   // the "fix": take out ../ - but only once
            bool odd = file.Contains("..") || file.StartsWith("/", StringComparison.Ordinal);
            if (from != null) Net.Noise(from, ip, odd ? "web-traversal" : "web", odd ? 3 : 1, "/image?file=" + file);
            if (!Weak.Contains("traversal") && (f.Contains("..") || f.StartsWith("/", StringComparison.Ordinal))) return Reply(400, "Pictures", "<p>That isn't a picture name.</p>");
            string full = Vfs.Norm("/var/www/shop/images/" + f);
            var pw = server.User("apache") != null ? "apache" : "root";
            var ctx = new PCtx { Cr = server.CredFor(pw), Cwd = "/", Root = "/" };
            try { return Text(200, "application/octet-stream", server.V.ReadText(ctx, full)); }
            catch (VfsEx e)
            {
                if (e.Errno == "EACCES") return Reply(403, "Forbidden", "<h1>Forbidden</h1><p>The shop can't read that file.</p>");
                return Reply(404, "Not found", "<h1>No such picture.</h1><p>(looked for " + Esc(full) + ")</p>");
            }
        }

        HttpReply Stock(string url, Machine server, Machine from, string ip)
        {
            if (url == null) return Reply(200, "Stock", "<p>Our product pages ask the warehouse: /stock?url=http://stock.copperleaf.internal/check?item=1</p>");
            if (!Weak.Contains("ssrf")) { if (!url.StartsWith("http://stock.copperleaf.internal/", StringComparison.OrdinalIgnoreCase)) return Reply(400, "Stock", "<p>Only the warehouse can be asked.</p>"); }
            string host = RangeServerFetch.HostOf(url).ToLowerInvariant();
            foreach (var b in S("ssrfblock").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                if (host == b.Trim().ToLowerInvariant()) return Reply(400, "Stock", "<h1>Blocked</h1><p>For safety the stock checker won't visit " + Esc(host) + ".</p>");
            if (from != null) Net.Noise(from, ip, RangeServerFetch.IsLoopback(host) ? "web-command" : "web", 2, "/stock?url=" + url);
            string err; var r = RangeServerFetch.Get(Net, server, url, null, out err);
            if (r == null) return Reply(502, "Stock", "<p>The stock checker couldn't reach that address: " + Esc(err) + "</p>");
            return Reply(200, "Stock", "<h1>Stock check</h1>\n<p>The warehouse answered:</p>\n<pre>" + Esc(StripTags(r.Body)) + "</pre>");
        }
        public static string StripTags(string html)
        {
            var t = Regex.Replace(html ?? "", "<(script|style)[^>]*>.*?</\\1>", "", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            t = Regex.Replace(t, "<!--.*?-->", "", RegexOptions.Singleline);
            t = Regex.Replace(t, "(<br\\s*/?>|</p>|</h\\d>|</tr>|</li>|</div>|</header>|</footer>|</pre>|</table>)[ \t]*\n?", "\n", RegexOptions.IgnoreCase);
            t = Regex.Replace(t, "<[^>]+>", "");
            t = t.Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"").Replace("&amp;", "&");
            t = Regex.Replace(t, "[ \t]+\n", "\n");
            t = Regex.Replace(t, "\n{3,}", "\n\n");
            return t.Trim() + "\n";
        }

        HttpReply Upload(string name, string content, string type, string[] who, Machine server, Machine from, string ip)
        {
            if (name == null || content == null) return Reply(200, "Upload a picture", "<h1>Your picture</h1><p>Send name=, type= and content= (curl -d) to upload your avatar. We keep it at /uploads/NAME.</p>");
            if (!Weak.Contains("upload")) return Reply(403, "Upload", "<p>Avatar uploads are switched off this week.</p>");
            if (!Regex.IsMatch(name, "^[A-Za-z0-9_.-]{1,40}$")) return Reply(400, "Upload", "<p>Use a simple file name.</p>");
            if (S("uploadcheck") == "type" && !(type ?? "").StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                return Reply(400, "Upload", "<h1>Pictures only</h1><p>The type you sent (" + Esc(type ?? "none") + ") isn't a picture type such as image/png.</p>");
            var dir = "/var/www/html/uploads";
            var ap = server.User("apache");
            server.V.EnsureDir(dir, 0x1ED, null);
            var n = server.V.Put(dir + "/" + name, content, 0x1A4, ap == null ? 0 : ap.Uid, ap == null ? 0 : ap.Gid, null);
            if (from != null) Net.Noise(from, ip, name.EndsWith(".spx", StringComparison.OrdinalIgnoreCase) ? "web-command" : "web", 2, "upload " + name);
            return Reply(200, "Upload", "<h1>Saved</h1><p>Your picture is at <a href=\"/uploads/" + Esc(name) + "\">/uploads/" + Esc(name) + "</a>.</p>");
        }

        /// <summary>The shop's own pretend server pages (.spx): the server runs every &lt;?run COMMAND ?&gt; in them as
        /// its own account and puts the output in the page. That is why letting people upload one is dangerous.</summary>
        HttpReply RunUpload(string path, Machine server, Machine from, string ip)
        {
            if (!path.EndsWith(".spx", StringComparison.OrdinalIgnoreCase)) return null;
            string text = server.V.Cat("/var/www/html" + path);
            if (text == null) return Reply(404, "Not found", "<h1>Not found</h1>");
            var user = server.User("apache") != null ? "apache" : "root";
            string outText = Regex.Replace(text, "<\\?run\\s+(.*?)\\s*\\?>", m =>
            {
                var res = server.RunCommand(user, "PATH=/usr/local/bin:/usr/bin\n" + m.Groups[1].Value, null, 50000, null);
                return res.Output;
            }, RegexOptions.Singleline);
            if (from != null) Net.Noise(from, ip, "web-command", 3, path);
            return Text(200, "text/html; charset=UTF-8", outText);
        }

        // ------------------------------------------------------------ the staff browser
        /// <summary>A member of staff opens a page in their pretend browser, signed in as staff. It doesn't run any
        /// script: it only looks for the textbook signs. A script (or an onerror / onload attribute) that reads
        /// document.cookie and sends it to the practice listening page (catcher) gets the staff cookie recorded there;
        /// a page with a form that posts to /account/email and submits itself changes the staff email. Returns a note.</summary>
        public string StaffReads(string url, Machine server)
        {
            var staff = StaffAccount();
            if (staff == null || depth > 2) return "";
            depth++;
            try
            {
                string cookie = SessionCookie(staff);
                string err; var r = RangeServerFetch.Get(Net, server, url, cookie, out err);
                if (r == null) return "the staff browser couldn't open the link";
                string page = r.Body ?? "";
                string note = "";
                // scripts that read the cookie and send it to catcher
                foreach (Match m in Regex.Matches(page, "<script[^>]*>(.*?)</script\\s*>|\\son[a-z]+\\s*=\\s*(\"[^\"]*\"|[^>]+)", RegexOptions.IgnoreCase | RegexOptions.Singleline))
                {
                    string code = m.Groups[1].Success && m.Groups[1].Length > 0 ? m.Groups[1].Value : m.Groups[2].Value;
                    if (code.IndexOf("document.cookie", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    var to = Regex.Match(code, "(?:https?:)?//" + Regex.Escape(Catcher) + "(/[^'\"\\s+)]*)?", RegexOptions.IgnoreCase);
                    if (!to.Success) continue;
                    string where = to.Groups[1].Success && to.Groups[1].Length > 0 ? to.Groups[1].Value : "/";
                    Net.Catch("GET " + where + cookie + "   (from the staff browser at " + Net.IpOf(server) + ", reading " + url + ")");
                    note = "the staff browser ran a script";
                    break;
                }
                // a form that posts itself to the shop's change-email page (cross-site request forgery)
                var form = Regex.Match(page, "<form[^>]*action\\s*=\\s*[\"']?([^\"'\\s>]*account/email)[\"']?[^>]*>(.*?)</form>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (form.Success && Weak.Contains("csrf") && Regex.IsMatch(page, "\\.submit\\(\\)|onload\\s*=", RegexOptions.IgnoreCase))
                {
                    var em = Regex.Match(form.Groups[2].Value, "<input[^>]*name\\s*=\\s*[\"']?email[\"']?[^>]*value\\s*=\\s*[\"']?([^\"'\\s>]+)", RegexOptions.IgnoreCase);
                    if (!em.Success) em = Regex.Match(form.Groups[2].Value, "<input[^>]*value\\s*=\\s*[\"']?([^\"'\\s>]+)[\"']?[^>]*name\\s*=\\s*[\"']?email", RegexOptions.IgnoreCase);
                    if (em.Success) { ChangeEmail(em.Groups[1].Value, new[] { staff.User, staff.Role }); note = "the staff browser sent a form"; }
                }
                return note;
            }
            finally { depth--; }
        }
    }

    /// <summary>Requests a pretend server makes for itself: its stock checker, a link preview, the staff browser.
    /// From the server they can reach things the player can't (the server's own inside pages, the pretend cloud's
    /// metadata address 169.254.169.254, machines behind it). Server-side request forgery in one class.</summary>
    public static class RangeServerFetch
    {
        public static string HostOf(string url)
        {
            var m = Regex.Match(url ?? "", "^(?:[a-zA-Z]+://)?(\\[[^\\]]*\\]|[^/:?#]*)", RegexOptions.None);
            return m.Success ? m.Groups[1].Value : "";
        }
        /// <summary>The ways of writing "this computer" a careless filter forgets: 127.x.x.x, 127.1, localhost, 0.0.0.0,
        /// 0, [::1], 2130706433 (the number), 0x7f000001, 017700000001.</summary>
        public static bool IsLoopback(string host)
        {
            string h = (host ?? "").Trim().ToLowerInvariant();
            if (h == "localhost" || h == "0.0.0.0" || h == "0" || h == "[::1]" || h == "::1" || h == "[0:0:0:0:0:0:0:1]" || h == "2130706433" || h == "0x7f000001" || h == "017700000001" || h == "0x7f.0.0.1" || h == "0177.0.0.1") return true;
            if (Regex.IsMatch(h, "^127(\\.\\d{1,3}){0,3}$")) return true;
            return false;
        }

        public static HttpReply Get(RangeNet net, Machine server, string url, string cookie, out string err)
        {
            err = null;
            var m = Regex.Match(url ?? "", "^(https?)://(\\[[^\\]]*\\]|[^/:?#]+)(?::(\\d+))?(/[^?#]*)?(?:\\?([^#]*))?", RegexOptions.IgnoreCase);
            if (!m.Success) { err = "only http:// addresses work here"; return null; }
            string host = m.Groups[2].Value.ToLowerInvariant();
            int port = m.Groups[3].Success && m.Groups[3].Length > 0 ? int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) : 80;
            string path = m.Groups[4].Success && m.Groups[4].Length > 0 ? m.Groups[4].Value : "/";
            string query = m.Groups[5].Success ? m.Groups[5].Value : "";
            try { path = Uri.UnescapeDataString(path); } catch (Exception) { }
            if (host == "stock.copperleaf.internal")
            {
                var f = RangeNet.Form(query); string item; f.TryGetValue("item", out item);
                return new HttpReply { Code = 200, Type = "text/plain", Body = "warehouse: item " + (item ?? "?") + " - " + (item == null ? 0 : 3 + Math.Abs(item.GetHashCodeStable()) % 20) + " in stock\n" };
            }
            if (host == "169.254.169.254") return Metadata(server, path, out err);
            if (IsLoopback(host) || host == (net.IpOf(server) ?? "") || server.V.Cat("/etc/hosts") != null && Regex.IsMatch(server.V.Cat("/etc/hosts"), "(?m)^127\\.0\\.0\\.1\\s.*\\b" + Regex.Escape(host) + "\\b"))
            {
                var shop = net.WebApps.FirstOrDefault(a => a.Kind == "shop" && a.Machine.Equals(server.Name, StringComparison.OrdinalIgnoreCase) && a.Port == port);
                if (shop != null)
                {
                    if (shop.Shop == null) shop.Shop = new RangeShop(net, shop);
                    var r = shop.Shop.Handle(null, server, path, query, null, cookie, true);
                    if (r != null) return r;
                }
            }
            string old = Cmd.CookieData;
            try
            {
                Cmd.CookieData = cookie;
                var r = Cmd.RangeFetch(server, "http://" + host + (port == 80 ? "" : ":" + port) + path + (query.Length > 0 ? "?" + query : ""));
                if (r.Error != null) { err = r.Error; return null; }
                return r;
            }
            finally { Cmd.CookieData = old; }
        }

        /// <summary>The pretend cloud's instance metadata (Skyloft Cloud): the files under /var/lib/skyloft/metadata on
        /// the server; a folder lists its names, one per line.</summary>
        static HttpReply Metadata(Machine server, string path, out string err)
        {
            err = null;
            string root = "/var/lib/skyloft/metadata";
            if (!server.V.IsDirG(root)) { err = "Connection timed out (no metadata service here)"; return null; }
            string full = Vfs.Norm(root + "/" + path.Trim('/'));
            if (!full.StartsWith(root, StringComparison.Ordinal)) full = root;
            var r = new HttpReply { Code = 200, Server = "SkyloftMetadata/1.0", Type = "text/plain" };
            if (server.V.IsDirG(full))
            {
                var l = server.V.G(full);
                r.Body = string.Join("\n", l.Node.Kids.Keys.OrderBy(k => k, StringComparer.Ordinal).Select(k => k + (l.Fs.Node(l.Node.Kids[k]).T == 'd' ? "/" : "")).ToArray()) + "\n";
                return r;
            }
            var t = server.V.Cat(full);
            if (t == null) { r.Code = 404; r.Reason = "Not Found"; r.Body = "404 - Not Found\n"; return r; }
            r.Body = t;
            return r;
        }
    }

    /// <summary>The shop's pretend database: a tiny made-up query language with the shape of the textbook one
    /// (SELECT cols FROM table [WHERE ...] [UNION SELECT ...] [LIMIT n], quotes, AND / OR / NOT, = != LIKE, -- and #
    /// comments). Only enough to show why gluing text into a query is a mistake.</summary>
    public class RangeSql
    {
        class Table { public string Name; public string[] Cols; public List<string[]> Rows = new List<string[]>(); }
        readonly List<Table> tables = new List<Table>();
        public void Add(string name, string[] cols, IEnumerable<string[]> rows)
        {
            var t = new Table { Name = name, Cols = cols };
            foreach (var r in rows) { var row = new string[cols.Length]; for (int i = 0; i < cols.Length; i++) row[i] = i < r.Length ? r[i] : ""; t.Rows.Add(row); }
            tables.Add(t);
        }
        /// <summary>The catalog: a table "tables" (name, columns) listing every table, like a real database's own list.</summary>
        public void AddCatalog() { Add("tables", new[] { "name", "columns" }, tables.Select(t => new[] { t.Name, string.Join(",", t.Cols) }).ToList().Concat(new[] { new[] { "tables", "name,columns" } })); }

        // tokens: kind w (word), s (string), n (number), p (punctuation)
        List<string[]> toks; int pos;
        string Peek(int k = 0) { return pos + k < toks.Count ? toks[pos + k][1] : null; }
        string Kind(int k = 0) { return pos + k < toks.Count ? toks[pos + k][0] : ""; }
        bool IsWord(string w, int k = 0) { return Kind(k) == "w" && string.Equals(Peek(k), w, StringComparison.OrdinalIgnoreCase); }
        void Expect(string w) { if (!IsWord(w) && Peek() != w) throw new FormatException("expected " + w + " near " + Near()); pos++; }
        string Near() { return Peek() == null ? "the end" : "\"" + Peek() + "\""; }

        void Lex(string q)
        {
            toks = new List<string[]>(); pos = 0;
            int i = 0;
            while (i < q.Length)
            {
                char c = q[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (c == '-' && i + 1 < q.Length && q[i + 1] == '-') break;
                if (c == '#') break;
                if (c == ';') break;
                if (c == '\'')
                {
                    var sb = new StringBuilder(); i++;
                    bool closed = false;
                    while (i < q.Length)
                    {
                        if (q[i] == '\'') { if (i + 1 < q.Length && q[i + 1] == '\'') { sb.Append('\''); i += 2; continue; } closed = true; i++; break; }
                        sb.Append(q[i++]);
                    }
                    if (!closed) throw new FormatException("unclosed quotation mark near '" + (sb.Length > 20 ? sb.ToString().Substring(0, 20) : sb.ToString()));
                    toks.Add(new[] { "s", sb.ToString() }); continue;
                }
                if (char.IsDigit(c)) { int st = i; while (i < q.Length && char.IsDigit(q[i])) i++; toks.Add(new[] { "n", q.Substring(st, i - st) }); continue; }
                if (char.IsLetter(c) || c == '_') { int st = i; while (i < q.Length && (char.IsLetterOrDigit(q[i]) || q[i] == '_' || q[i] == '.')) i++; toks.Add(new[] { "w", q.Substring(st, i - st) }); continue; }
                if ((c == '!' || c == '<') && i + 1 < q.Length && (q[i + 1] == '=' || q[i + 1] == '>')) { toks.Add(new[] { "p", "!=" }); i += 2; continue; }
                if ("=(),*".IndexOf(c) >= 0) { toks.Add(new[] { "p", c.ToString() }); i++; continue; }
                throw new FormatException("unexpected character '" + c + "'");
            }
        }

        /// <summary>Runs a query; null rows and an error sentence when it doesn't parse.</summary>
        public List<string[]> Run(string query, out string error)
        {
            error = null;
            try
            {
                Lex(query);
                var rows = Select();
                int firstCols = lastCols;
                while (IsWord("UNION"))
                {
                    pos++; if (IsWord("ALL")) pos++;
                    var more = Select();
                    if (lastCols != firstCols)
                        throw new FormatException("the SELECT statements of a UNION must have the same number of columns (" + firstCols + " and " + lastCols + ")");
                    rows.AddRange(more);
                }
                if (IsWord("ORDER")) { pos++; Expect("BY"); pos++; if (IsWord("ASC") || IsWord("DESC")) pos++; }
                if (IsWord("LIMIT")) { pos++; int n; if (Kind() == "n" && int.TryParse(Peek(), out n)) { pos++; if (rows.Count > n) rows = rows.Take(n).ToList(); } else throw new FormatException("LIMIT needs a number"); }
                if (Peek() != null) throw new FormatException("syntax error near " + Near());
                return rows;
            }
            catch (FormatException e) { error = e.Message; return null; }
        }
        int lastCols = -1;

        List<string[]> Select()
        {
            Expect("SELECT");
            var cols = new List<string[]>();   // kind, text
            do
            {
                if (Peek() == ",") pos++;
                if (Peek() == "*") { cols.Add(new[] { "*", "*" }); pos++; }
                else if (Kind() == "w" || Kind() == "s" || Kind() == "n") { cols.Add(new[] { Kind(), Peek() }); pos++; }
                else throw new FormatException("syntax error near " + Near());
            } while (Peek() == ",");
            Expect("FROM");
            if (Kind() != "w") throw new FormatException("expected a table name near " + Near());
            string tname = Peek(); pos++;
            var t = tables.FirstOrDefault(x => x.Name.Equals(tname, StringComparison.OrdinalIgnoreCase));
            if (t == null) throw new FormatException("no such table: " + tname);
            Func<string[], bool> where = r => true;
            if (IsWord("WHERE")) { pos++; var e = Expr(t); where = e; }
            int n = cols.Sum(c => c[0] == "*" ? t.Cols.Length : 1);
            lastCols = n;
            foreach (var c in cols) if (c[0] == "w" && !c[1].Equals("NULL", StringComparison.OrdinalIgnoreCase) && Col(t, c[1]) < 0) throw new FormatException("no such column: " + c[1]);
            var outRows = new List<string[]>();
            foreach (var r in t.Rows.Where(where))
            {
                var o = new List<string>();
                foreach (var c in cols)
                {
                    if (c[0] == "*") o.AddRange(r);
                    else if (c[0] == "w") o.Add(c[1].Equals("NULL", StringComparison.OrdinalIgnoreCase) ? "" : r[Col(t, c[1])]);
                    else o.Add(c[1]);
                }
                outRows.Add(o.ToArray());
            }
            return outRows;
        }
        static int Col(Table t, string name) { return Array.FindIndex(t.Cols, x => x.Equals(name, StringComparison.OrdinalIgnoreCase)); }

        Func<string[], bool> Expr(Table t)
        {
            var left = And(t);
            while (IsWord("OR")) { pos++; var l = left; var r = And(t); left = row => l(row) || r(row); }
            return left;
        }
        Func<string[], bool> And(Table t)
        {
            var left = Not(t);
            while (IsWord("AND")) { pos++; var l = left; var r = Not(t); left = row => l(row) && r(row); }
            return left;
        }
        Func<string[], bool> Not(Table t)
        {
            if (IsWord("NOT")) { pos++; var e = Not(t); return row => !e(row); }
            if (Peek() == "(") { pos++; var e = Expr(t); Expect(")"); return e; }
            var a = Operand(t);
            if (Peek() == "=" || Peek() == "!=" || IsWord("LIKE"))
            {
                string op = Peek().ToUpperInvariant(); pos++;
                var b = Operand(t);
                if (op == "=") return row => string.Equals(a(row), b(row), StringComparison.OrdinalIgnoreCase);
                if (op == "!=") return row => !string.Equals(a(row), b(row), StringComparison.OrdinalIgnoreCase);
                return row => Like(a(row), b(row));
            }
            return row => { var v = a(row); return v.Length > 0 && v != "0"; };
        }
        Func<string[], string> Operand(Table t)
        {
            string k = Kind(), v = Peek();
            if (v == null) throw new FormatException("syntax error near the end");
            pos++;
            if (k == "s" || k == "n") return row => v;
            if (k == "w")
            {
                if (v.Equals("NULL", StringComparison.OrdinalIgnoreCase)) return row => "";
                int i = Col(t, v);
                if (i < 0) throw new FormatException("no such column: " + v);
                return row => row[i];
            }
            throw new FormatException("syntax error near \"" + v + "\"");
        }
        static bool Like(string s, string pattern)
        {
            var re = "^" + Regex.Escape(pattern ?? "").Replace("%", ".*").Replace("_", ".") + "$";
            return Regex.IsMatch(s ?? "", re, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        }
    }

    public static partial class Cmd
    {
        /// <summary>curl's request as a server would make it for itself (root, no cookie jar).</summary>
        public static HttpReply RangeFetch(Machine m, string url) { return Fetch(m, new PCtx { Cr = new Cred { Uid = 0 } }, url); }
    }
}
