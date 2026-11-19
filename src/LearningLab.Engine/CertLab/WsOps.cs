// Cert Lab - the AZ-800 practice consoles' actions as one-line commands (machine "admin" in setups and solutions): what
// a click in Active Directory Users and Computers, Sites and Services, Group Policy Management, DNS Manager, Windows Admin
// Center or the Azure portal does. The consoles build these lines and run them, so a lab's model solution and a person's
// clicks go through the same checks. Words: VERB [noun] "Name" key=value ...; AD and GPO actions take server=DC to
// talk to a particular DC (default: the PDC emulator, as Group Policy Management does; any running DC if it is down).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public class WsResult { public bool Ok = true; public string Text = "Done."; public static WsResult Fail(string t) { return new WsResult { Ok = false, Text = t }; } public static WsResult Done(string t) { return new WsResult { Text = t }; } }

    public static partial class WsOps
    {
        public class Args
        {
            public readonly List<string> Pos = new List<string>();
            public readonly Dictionary<string, string> Kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public string this[string k] { get { string v; return Kv.TryGetValue(k, out v) ? v : null; } }
            public string P(int i) { return i < Pos.Count ? Pos[i] : null; }
            public bool Has(string k) { return Kv.ContainsKey(k); }
            public bool Yes(string k, bool def) { var v = this[k]; return v == null ? def : WsText.True(v); }
            public int Int(string k, int def) { int v; return int.TryParse(this[k], out v) ? v : def; }
        }
        public static Args Parse(List<string> tok, int from)
        {
            var a = new Args();
            for (int i = from; i < tok.Count; i++)
            {
                var t = tok[i]; int eq = t.IndexOf('=');
                if (eq > 0 && !t.StartsWith("OU=", StringComparison.OrdinalIgnoreCase) && !t.StartsWith("CN=", StringComparison.OrdinalIgnoreCase) && !t.StartsWith("DC=", StringComparison.OrdinalIgnoreCase) && t.Substring(0, eq).All(c => char.IsLetterOrDigit(c) || c == '+' || c == '-')) a.Kv[t.Substring(0, eq)] = t.Substring(eq + 1);
                else a.Pos.Add(t);
            }
            return a;
        }

        public static WsResult Run(WsLab lab, string line)
        {
            var tok = EpOps.Tokens(line.Trim());
            if (tok.Count == 0) return WsResult.Fail("Nothing to do.");
            string v = tok[0].ToLowerInvariant();
            string noun = tok.Count > 1 ? tok[1].ToLowerInvariant() : "";
            try
            {
                WsResult r = null;
                RunAd(lab, v, noun, tok, ref r);
                if (r == null) RunGp(lab, v, noun, tok, ref r);
                if (r == null) RunNet(lab, v, noun, tok, ref r);
                if (r == null) RunServer(lab, v, noun, tok, ref r);
                if (r == null) RunAzure(lab, v, noun, tok, ref r);
                return r ?? WsResult.Fail("Unknown console action: " + tok[0]);
            }
            catch (Exception e) { return WsResult.Fail("That didn't work: " + e.Message); }
        }
        static partial void RunGp(WsLab lab, string v, string noun, List<string> tok, ref WsResult r);
        static partial void RunNet(WsLab lab, string v, string noun, List<string> tok, ref WsResult r);
        static partial void RunServer(WsLab lab, string v, string noun, List<string> tok, ref WsResult r);
        static partial void RunAzure(WsLab lab, string v, string noun, List<string> tok, ref WsResult r);

        static WsResult R(string err, string ok) { return err == null ? WsResult.Done(ok) : WsResult.Fail(err); }

        /// <summary>The DC an action talks to.</summary>
        public static string Dc(WsLab lab, Args a, bool gpo)
        {
            string want = a["server"] ?? lab.Fsmo["PDCEmulator"];
            if (lab.DcUp(want)) return lab.Server(want).Name;
            return lab.DcNames.FirstOrDefault(lab.DcUp);
        }
        public static string ResolvePath(WsLab lab, string p)
        {
            if (string.IsNullOrEmpty(p)) return null;
            if (p.Contains("=")) return Dn.Norm(p);
            // corp.example/Staff/Sales or Staff/Sales
            var parts = p.Split('/').Where(x => x.Length > 0).ToList();
            if (parts.Count > 0 && WsText.Eq(parts[0], lab.DomainDns)) parts.RemoveAt(0);
            string dn = lab.DomainDn;
            foreach (var x in parts) dn = (WsText.Eq(x, "Users") || WsText.Eq(x, "Computers") || WsText.Eq(x, "Builtin") ? "CN=" : "OU=") + x + "," + dn;
            return dn;
        }

        static void RunAd(WsLab lab, string v, string noun, List<string> tok, ref WsResult r)
        {
            var a = Parse(tok, 2);
            string dc;
            switch (v)
            {
                case "new":
                    {
                        if (noun != "ou" && noun != "user" && noun != "group" && noun != "computer") break;
                        dc = Dc(lab, a, false);
                        string name = a["name"] ?? a.P(0), path = ResolvePath(lab, a["path"]);
                        string err = null; WsAdObj o = null;
                        if (noun == "ou") o = lab.NewOu(dc, path ?? lab.DomainDn, name, a.Yes("protect", true), out err);
                        if (noun == "user")
                        {
                            o = lab.NewUser(dc, path, name, a["sam"], a["upn"], a.Yes("enabled", a["password"] != null), a["password"], out err);
                            if (o != null) { foreach (var k in new[] { "givenName|given", "sn|surname", "department|department", "title|title", "description|description", "displayName|display", "l|city", "company|company" }) { var p = k.Split('|'); if (a[p[1]] != null) o.Set(p[0], a[p[1]]); } if (a.Yes("mustchange", false)) o.Set("pwdLastSet", "0"); lab.Touch(dc, o); }
                        }
                        if (noun == "group") o = lab.NewGroup(dc, path, name, a["sam"], a["scope"] ?? "Global", a["category"], out err);
                        if (noun == "computer") o = lab.NewComputer(dc, path, name, out err);
                        if (o != null && a["description"] != null && noun != "user") { o.Set("description", a["description"]); lab.Touch(dc, o); }
                        r = R(err, "Created " + noun + " " + name + (o == null ? "" : " (" + Dn.Canonical(o.Dn) + ")") + " on " + dc + ".");
                        return;
                    }
                case "set":
                    {
                        if (noun != "user" && noun != "group" && noun != "computer" && noun != "ou") break;
                        dc = Dc(lab, a, false);
                        var o = lab.Dir(dc).Find(a.P(0), noun == "ou" ? "organizationalUnit" : noun);
                        if (o == null && noun == "ou") o = lab.Dir(dc).ByDn(ResolvePath(lab, a.P(0)));
                        if (o == null) { r = WsResult.Fail("There is no " + noun + " " + a.P(0) + "."); return; }
                        foreach (var kv in a.Kv)
                        {
                            if (kv.Key == "server") continue;
                            string key = kv.Key.ToLowerInvariant();
                            if (key == "scope") { var e = lab.SetScope(dc, o, kv.Value); if (e != null) { r = WsResult.Fail(e); return; } continue; }
                            if (key == "enabled") { o.Set("enabled", WsText.True(kv.Value) ? "TRUE" : "FALSE"); continue; }
                            if (key == "protect") { o.Set("protectedFromAccidentalDeletion", WsText.True(kv.Value) ? "TRUE" : null); continue; }
                            if (key == "upn") { o.Set("userPrincipalName", kv.Value); continue; }
                            string attr = key == "city" ? "l" : key == "surname" ? "sn" : key == "given" ? "givenName" : key == "display" ? "displayName" : kv.Key;
                            o.Set(attr, kv.Value);
                        }
                        lab.Touch(dc, o);
                        r = WsResult.Done("Saved " + o.Name + " on " + dc + ".");
                        return;
                    }
                case "move": case "rename": case "delete": case "restore":
                    {
                        if (noun != "user" && noun != "group" && noun != "computer" && noun != "ou" && noun != "object") break;
                        dc = Dc(lab, a, false);
                        var d = lab.Dir(dc);
                        WsAdObj o = v == "restore" ? d.Objs.Values.FirstOrDefault(x => x.Deleted && (WsText.Eq(x.Sam, a.P(0)) || WsText.Eq(x.Name, a.P(0)))) : (noun == "ou" ? d.ByDn(ResolvePath(lab, a.P(0))) : d.Find(a.P(0), noun == "object" ? null : noun));
                        if (o == null) { r = WsResult.Fail("There is no " + noun + " " + a.P(0) + (v == "restore" ? " among the deleted objects." : ".")); return; }
                        string err = v == "move" ? lab.Move(dc, o, ResolvePath(lab, a.P(1))) : v == "rename" ? lab.Rename(dc, o, a.P(1)) : v == "delete" ? lab.Delete(dc, o, a.Yes("recursive", false)) : lab.Restore(dc, o);
                        r = R(err, (v == "move" ? "Moved " : v == "rename" ? "Renamed " : v == "delete" ? "Deleted " : "Restored ") + a.P(0) + ".");
                        return;
                    }
                case "members":
                    {
                        if (noun != "add" && noun != "remove") break;
                        dc = Dc(lab, a, false);
                        var d = lab.Dir(dc);
                        var g = d.Find(a.P(0), "group");
                        if (g == null) { r = WsResult.Fail("There is no group " + a.P(0) + "."); return; }
                        foreach (var m in a.Pos.Skip(1))
                        {
                            var o = d.Find(m, "principal");
                            if (o == null) { r = WsResult.Fail("There is no user, group or computer " + m + "."); return; }
                            var e = noun == "add" ? lab.AddMember(dc, g, o) : lab.RemoveMember(dc, g, o);
                            if (e != null) { r = WsResult.Fail(e); return; }
                        }
                        r = WsResult.Done((noun == "add" ? "Added to " : "Removed from ") + g.Name + ".");
                        return;
                    }
                case "password":
                    {
                        dc = Dc(lab, tokArgs(tok), false);
                        var a1 = tokArgs(tok);
                        var u = lab.Dir(dc).Find(a1.P(0), "user");
                        if (u == null) { r = WsResult.Fail("There is no user " + a1.P(0) + "."); return; }
                        var e = lab.PasswordProblem(lab.Dir(dc), u, a1.P(1) ?? "");
                        if (e != null) { r = WsResult.Fail(e); return; }
                        u.Set("password", a1.P(1)).Set("pwdLastSet", a1.Yes("mustchange", false) ? "0" : WsText.Date(lab.Now)).Set("lockedOut", a1.Yes("unlock", true) ? null : u.Get("lockedOut"));
                        lab.Touch(dc, u);
                        r = WsResult.Done("The password of " + u.Name + " was reset.");
                        return;
                    }
                case "unlock":
                    {
                        dc = Dc(lab, tokArgs(tok), false);
                        var u = lab.Dir(dc).Find(tokArgs(tok).P(0), "user");
                        if (u == null) { r = WsResult.Fail("There is no user " + tokArgs(tok).P(0) + "."); return; }
                        u.Set("lockedOut", null); lab.Touch(dc, u);
                        r = WsResult.Done(u.Name + " is unlocked.");
                        return;
                    }
            }
            RunAd2(lab, v, noun, tok, ref r);
        }
        static Args tokArgs(List<string> tok) { return Parse(tok, 1); }
        static partial void RunAd2(WsLab lab, string v, string noun, List<string> tok, ref WsResult r);
    }
}
