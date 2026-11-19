// Cert Lab - GroupPolicy module reports: Get-GPResultantSetOfPolicy (what a computer and user last applied, written as
// an HTML or XML file) and Get-GPOReport (a GPO's settings and links). The files are Cert Lab's short versions.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public partial class PsCmdlets
    {
        public static string RsopText(RsopResult r)
        {
            var sb = new StringBuilder();
            sb.Append("Computer: " + r.Computer + (r.User.Length > 0 ? "   User: " + r.User : "") + "\r\nDomain controller: " + r.Dc + "   Site: " + (r.Site ?? "(none)") + "   Loopback: " + (r.Loopback.Length == 0 ? "not configured" : r.Loopback) + "\r\n\r\n");
            Action<string, List<RsopGpo>, List<RsopGpo>, Dictionary<string, RsopValue>> part = (title, ap, den, set) =>
            {
                sb.Append(title + "\r\n  Applied GPOs (in the order they were applied):\r\n");
                foreach (var g in ap) sb.Append("    " + g.Name + "  (linked to " + g.LinkedTo + (g.Enforced ? ", enforced" : "") + ")\r\n");
                sb.Append("  Not applied:\r\n");
                foreach (var g in den) sb.Append("    " + g.Name + ": " + g.Reason + "\r\n");
                sb.Append("  Settings:\r\n");
                foreach (var v in set.Values.OrderBy(x => x.Id)) sb.Append("    " + (v.Def != null ? v.Def.Name : v.Id) + " = " + v.State + (v.Value.Length > 0 ? " (" + v.Value + ")" : "") + "   [winning GPO: " + v.Gpo + "]\r\n");
                sb.Append("\r\n");
            };
            part("COMPUTER CONFIGURATION", r.AppliedC, r.DeniedC, r.C);
            if (r.User.Length > 0) part("USER CONFIGURATION", r.AppliedU, r.DeniedU, r.U);
            return sb.ToString();
        }

        static partial void RegWsGpMore()
        {
            GpCmd("Get-GPResultantSetOfPolicy", "Computer:string; User:string; ReportType:string ! set=Html|Xml; Path:string !", (c, o) =>
            {
                var lab = Lab(c); string comp = c.Str("Computer") ?? c.M.Name; var s = lab.Server(comp);
                if (s == null || !s.Running) throw c.Fail("The computer " + comp + " can't be reached.", "RsopFailed", "ResourceUnavailable", comp, "System.Runtime.InteropServices.COMException");
                RsopResult r;
                string user = c.Str("User"); if (user != null && user.Contains("\\")) user = user.Substring(user.IndexOf('\\') + 1);
                if (user != null ? !lab.LastUser.TryGetValue(s.Name + "|" + user, out r) : !lab.LastComputer.TryGetValue(s.Name, out r))
                    throw c.Fail("The user " + user + " does not have RSOP data on " + s.Name + " (they haven't signed in there).", "RsopNoData", "ObjectNotFound", user, "System.ArgumentException");
                string text = RsopText(r);
                string path = c.R.FullFsPath(c.Str("Path"), c.R.CurrentFsDir);
                string body = WsText.Eq(c.Str("ReportType"), "Xml") ? "<Rsop>\r\n<![CDATA[\r\n" + text + "]]>\r\n</Rsop>\r\n" : "<html><body><h1>Group Policy Results</h1><pre>\r\n" + text + "</pre></body></html>\r\n";
                var why = c.R.WriteFile(path, body, "unicode", false);
                if (why != null) throw c.Fail(why, "WriteFailed", "WriteError", path, "System.IO.IOException");
                var p = new PsObj("Microsoft.GroupPolicy.Rsop", "System.Object");
                p.Val("Computer", "string", s.Name); p.Val("User", "string", user ?? ""); p.Val("Path", "string", path); p.Val("ReportType", "string", c.Str("ReportType"));
                c.Out(p);
            });
            GpCmd("Get-GPOReport", GpId + "; All:switch; ReportType:string ! set=Html|Xml; Path:string", (c, o) =>
            {
                var lab = Lab(c); string dc = GpDc(c); var d = lab.Dir(dc);
                var list = c.Sw("All") ? lab.Gpos(d).ToList() : new List<WsAdObj> { GpFind(c, d) };
                var sb = new StringBuilder();
                bool xml = WsText.Eq(c.Str("ReportType"), "Xml");
                foreach (var g in list)
                {
                    sb.Append(xml ? "<GPO>\r\n  <Name>" + g.Get("displayName") + "</Name>\r\n  <Identifier>{" + g.Guid.ToUpperInvariant() + "}</Identifier>\r\n" : "<h2>" + g.Get("displayName") + "</h2>\r\n");
                    foreach (var kv in lab.WhereLinked(d, g)) sb.Append(xml ? "  <LinksTo><SOMPath>" + Dn.Canonical(kv.Key.Dn) + "</SOMPath><Enabled>" + (kv.Value[1] == "1" ? "true" : "false") + "</Enabled><NoOverride>" + (kv.Value[2] == "1" ? "true" : "false") + "</NoOverride></LinksTo>\r\n" : "<p>Linked to " + Dn.Canonical(kv.Key.Dn) + "</p>\r\n");
                    foreach (var sc in new[] { "C", "U" })
                        foreach (var kv in WsLab.Settings(g, sc))
                        {
                            var def = GpCatalog.Get(kv.Key); var p = kv.Value.Split(new[] { '|' }, 2);
                            string name = def != null ? def.Name : kv.Key;
                            sb.Append(xml ? "  <" + (sc == "C" ? "Computer" : "User") + "Setting><Name>" + name + "</Name><State>" + p[0] + "</State><Value>" + (p.Length > 1 ? p[1] : "") + "</Value></" + (sc == "C" ? "Computer" : "User") + "Setting>\r\n" : "<p>" + (sc == "C" ? "Computer" : "User") + ": " + name + " = " + p[0] + "</p>\r\n");
                        }
                    if (xml) sb.Append("</GPO>\r\n");
                }
                string text = xml ? "<?xml version=\"1.0\" encoding=\"utf-16\"?>\r\n" + sb : "<html><body>\r\n" + sb + "</body></html>\r\n";
                if (c.Has("Path")) { var why = c.R.WriteFile(c.R.FullFsPath(c.Str("Path"), c.R.CurrentFsDir), text, "unicode", false); if (why != null) throw c.Fail(why, "WriteFailed", "WriteError", c.Str("Path"), "System.IO.IOException"); }
                else c.Out(text);
            });
        }
    }
}
