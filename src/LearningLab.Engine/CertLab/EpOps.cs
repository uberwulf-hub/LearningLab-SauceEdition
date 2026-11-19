// Cert Lab - every change to the pretend tenant goes through here, from the portal's forms and from the course's
// setup lines and model solutions (one line each, so the tests can type them like a person clicks):
//   create KIND key=value ...                  set KIND "Name" key=value +listkey=add -listkey=remove
//   assign KIND "Name" "Group" [intent=required|available|uninstall|exclude] [filter="F"] [mode=include|exclude]
//   unassign KIND "Name" "Group"               delete KIND "Name"
//   members add|remove "Group" member ...      remote ACTION "Device" [arg] [keep-enrollment] [keep-data]
//   import autopilot "Device" "C:\path.csv" [groupTag=X] [user=Y]     tenant key=value ...
// Validation (EpValidate.cs) gives the same messages the forms show.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public class EpResult
    {
        public bool Ok = true;
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();
        public string Message = "";
        public EpObj Obj;
        public static EpResult Fail(string why) { var r = new EpResult { Ok = false }; r.Errors.Add(why); return r; }
        public string Text { get { return Ok ? Message + (Warnings.Count > 0 ? " " + string.Join(" ", Warnings.ToArray()) : "") : string.Join(" ", Errors.ToArray()); } }
    }

    public static partial class EpOps
    {
        public static List<string> Tokens(string line)
        {
            var l = new List<string>();
            var sb = new StringBuilder();
            bool q = false, any = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (q)
                {
                    if (c == '\\' && i + 1 < line.Length && (line[i + 1] == '"' || line[i + 1] == '\\')) { sb.Append(line[++i]); continue; }
                    if (c == '"') { q = false; continue; }
                    sb.Append(c); continue;
                }
                if (c == '"') { q = true; any = true; continue; }
                if (char.IsWhiteSpace(c)) { if (sb.Length > 0 || any) { l.Add(sb.ToString()); sb.Clear(); any = false; } continue; }
                sb.Append(c);
            }
            if (sb.Length > 0 || any) l.Add(sb.ToString());
            return l;
        }
        static bool IsPair(string tok, out string key, out string value)
        {
            key = value = null;
            int eq = tok.IndexOf('=');
            if (eq <= 0) return false;
            key = tok.Substring(0, eq); value = tok.Substring(eq + 1);
            return key.IndexOf(' ') < 0 || key.StartsWith("s:", StringComparison.OrdinalIgnoreCase);
        }
        static Dictionary<string, string> Pairs(List<string> t, int from, List<string> loose)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = from; i < t.Count; i++)
            {
                string k, v;
                if (IsPair(t[i], out k, out v)) d[k] = v; else if (loose != null) loose.Add(t[i]);
            }
            return d;
        }

        /// <summary>Runs one command line against the tenant.</summary>
        public static EpResult Run(EpTenant t, string line)
        {
            var r = RunCore(t, line);
            if (r.Ok) t.Tick();
            return r;
        }
        static EpResult RunCore(EpTenant t, string line)
        {
            var tok = Tokens(line.Trim());
            if (tok.Count == 0) return new EpResult();
            string verb = tok[0].ToLowerInvariant();
            try
            {
                switch (verb)
                {
                    case "create": { if (tok.Count < 2) return EpResult.Fail("create needs a kind."); return Create(t, Kind(tok[1]), Pairs(tok, 2, null)); }
                    case "tenant": return Update(t, t.Settings, Pairs(tok, 1, null));
                    case "device": return DeviceState(t, tok);
                    case "set":
                        {
                            if (tok.Count >= 2 && Kind(tok[1]) == "tenant") return Update(t, t.Settings, Pairs(tok, IsPairTok(tok, 2) ? 2 : 3, null));
                            if (tok.Count < 3) return EpResult.Fail("set needs a kind and a name.");
                            var o = t.Find(Kind(tok[1]), tok[2]);
                            if (o == null) return EpResult.Fail("There is no " + KindTitle(tok[1]) + " called '" + tok[2] + "'.");
                            return Update(t, o, Pairs(tok, 3, null));
                        }
                    case "assign": case "unassign":
                        {
                            if (tok.Count < 4) return EpResult.Fail(verb + " needs a kind, a name and a group.");
                            var o = t.Find(Kind(tok[1]), tok[2]);
                            if (o == null) return EpResult.Fail("There is no " + KindTitle(tok[1]) + " called '" + tok[2] + "'.");
                            if (verb == "unassign") return Unassign(t, o, tok[3]);
                            var p = Pairs(tok, 4, null);
                            string v;
                            var a = new EpAssign { Group = tok[3], Intent = p.TryGetValue("intent", out v) ? v.ToLowerInvariant() : (o.Kind == "app" ? "required" : "include"), Filter = p.TryGetValue("filter", out v) ? v : "", FilterMode = p.TryGetValue("mode", out v) ? v.ToLowerInvariant() : "" };
                            if (a.Filter.Length > 0 && a.FilterMode.Length == 0) a.FilterMode = "include";
                            return Assign(t, o, a);
                        }
                    case "delete":
                        {
                            if (tok.Count < 3) return EpResult.Fail("delete needs a kind and a name.");
                            var o = t.Find(Kind(tok[1]), tok[2]);
                            if (o == null) return EpResult.Fail("There is no " + KindTitle(tok[1]) + " called '" + tok[2] + "'.");
                            return Delete(t, o);
                        }
                    case "members":
                        {
                            if (tok.Count < 4) return EpResult.Fail("members add|remove GROUP MEMBER...");
                            var g = t.Group(tok[2]);
                            if (g == null) return EpResult.Fail("There is no group called '" + tok[2] + "'.");
                            return Members(t, g, tok[1].ToLowerInvariant() == "add", tok.Skip(3).ToList());
                        }
                    case "remote":
                        {
                            if (tok.Count < 3) return EpResult.Fail("remote ACTION DEVICE");
                            var d = t.Device(tok[2]);
                            if (d == null) return EpResult.Fail("There is no device called '" + tok[2] + "'.");
                            var rest = tok.Skip(3).ToList();
                            string arg = rest.FirstOrDefault(x => !x.StartsWith("keep-", StringComparison.Ordinal));
                            string why = t.Remote(d, tok[1].ToLowerInvariant(), arg, rest.Contains("keep-enrollment"), rest.Contains("keep-data"));
                            return why == null ? new EpResult { Message = EpTenant.RemoteLabel(tok[1].ToLowerInvariant()) + " sent to " + d.Name + "." } : EpResult.Fail(why);
                        }
                    case "import":
                        {
                            if (tok.Count < 4 || Kind(tok[1]) != "autopilot") return EpResult.Fail("import autopilot DEVICE CSVFILE");
                            return ImportCsv(t, tok[2], tok[3], Pairs(tok, 4, null));
                        }
                }
            }
            catch (Exception e) { return EpResult.Fail("Cert Lab couldn't do that: " + e.Message); }
            return EpResult.Fail("Unknown portal command: " + tok[0]);
        }
        static bool IsPairTok(List<string> t, int i) { string k, v; return i < t.Count && IsPair(t[i], out k, out v); }

        /// <summary>A kind from its id or a friendly name (compliance, "compliance policy", ring...).</summary>
        public static string Kind(string s)
        {
            var k = EpSchemas.Get(s);
            if (k != null) return k.Kind;
            foreach (var x in EpSchemas.AllKinds) if (EpText.Eq(x.Title, s) || EpText.Eq(x.Plural, s)) return x.Kind;
            return s;
        }
        static string KindTitle(string s) { var k = EpSchemas.Get(Kind(s)); return k == null ? s : k.Title.ToLowerInvariant(); }
    }
}
