// Cert Lab - portal command lines in plain words (the model solutions and the exam review show these), and the
// setup-only "device NAME key=value" line that puts a pretend device into a starting state.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CertLab
{
    public static partial class EpOps
    {
        static string Show(string kind, string key, string v)
        {
            string label = EpText.Eq(key, "name") ? "Name" : EpSchemas.Label(kind, key.TrimStart('+', '-'));
            if (key.StartsWith("+", StringComparison.Ordinal)) return "add " + v.Replace("\n", ", ") + " to " + label;
            if (key.StartsWith("-", StringComparison.Ordinal)) return "remove " + v.Replace("\n", ", ") + " from " + label;
            if (key.StartsWith("s:", StringComparison.Ordinal)) return "setting " + label + " = " + v;
            return label + " = " + v.Replace("\n", ", ");
        }

        /// <summary>A portal command as the clicks a person makes.</summary>
        public static string Describe(string line)
        {
            if (line.StartsWith("#wait ", StringComparison.Ordinal)) return EpSim.SolutionText("x", line);
            var tok = Tokens(line.Trim());
            if (tok.Count == 0) return "";
            string verb = tok[0].ToLowerInvariant();
            Func<string, EpKind> K = s => EpSchemas.Get(Kind(s));
            try
            {
                switch (verb)
                {
                    case "create":
                        {
                            var k = K(tok[1]); var p = Pairs(tok, 2, null);
                            string blade = k == null ? tok[1] : k.Blade;
                            if (k != null && k.Kind == "autopilot") blade += " > Import";
                            var sb = new StringBuilder(blade + " > " + (k != null && (k.Kind == "user" || k.Kind == "group") ? "New " + k.Title.ToLowerInvariant() : "Create") + ": ");
                            sb.Append(string.Join("; ", p.Select(kv => Show(k == null ? tok[1] : k.Kind, kv.Key, kv.Value)).ToArray()));
                            return sb.ToString();
                        }
                    case "set":
                        {
                            var k = K(tok[1]);
                            if (k != null && k.Kind == "tenant") return Describe("tenant " + string.Join(" ", tok.Skip(IsPairTok(tok, 2) ? 2 : 3).Select(Quote).ToArray()));
                            var p = Pairs(tok, 3, null);
                            return (k == null ? tok[1] : k.Blade) + " > " + tok[2] + " > Properties > Edit: " + string.Join("; ", p.Select(kv => Show(k == null ? tok[1] : k.Kind, kv.Key, kv.Value)).ToArray());
                        }
                    case "tenant":
                        {
                            var p = Pairs(tok, 1, null);
                            var k = EpSchemas.Get("tenant");
                            return string.Join("; ", p.Select(kv => { var f = k.Field(kv.Key); return (f == null ? kv.Key : f.Section + " > " + f.Label) + " = " + kv.Value; }).ToArray());
                        }
                    case "assign":
                        {
                            var k = K(tok[1]); var p = Pairs(tok, 4, null);
                            string v, intent = p.TryGetValue("intent", out v) ? v : (k != null && k.Kind == "app" ? "required" : "include");
                            string how = intent == "exclude" ? "Excluded groups: add " + tok[3] : intent == "include" ? "Included groups: add " + tok[3] : IntentName(intent) + ": add group " + tok[3];
                            if (p.TryGetValue("filter", out v)) how += ", filter " + v + " (" + (p.ContainsKey("mode") ? p["mode"] : "include") + " filtered devices in assignment)";
                            return (k == null ? tok[1] : k.Blade) + " > " + tok[2] + " > Assignments > Edit: " + how;
                        }
                    case "unassign": { var k = K(tok[1]); return (k == null ? tok[1] : k.Blade) + " > " + tok[2] + " > Assignments > Edit: remove " + tok[3]; }
                    case "delete": { var k = K(tok[1]); return (k == null ? tok[1] : k.Blade) + " > " + tok[2] + " > Delete"; }
                    case "members": return "Users and groups > Groups > " + tok[2] + " > Members > " + (tok[1] == "add" ? "Add members: " : "Remove: ") + string.Join(", ", tok.Skip(3).ToArray());
                    case "remote": return "Devices > All devices > " + tok[2] + " > " + EpTenant.RemoteLabel(tok[1].ToLowerInvariant()) + (tok.Count > 3 ? " (" + string.Join(", ", tok.Skip(3).ToArray()) + ")" : "");
                    case "import": return "Devices > Enrollment > Windows Autopilot > Devices > Import: the file " + tok[3] + " from " + tok[2] + (tok.Count > 4 ? " (" + string.Join(", ", tok.Skip(4).ToArray()) + ")" : "");
                    case "device": return null;
                }
            }
            catch (ArgumentOutOfRangeException) { }
            return line;
        }
        static string Quote(string s) { int eq = s.IndexOf('='); return eq > 0 && s.Substring(eq + 1).Contains(" ") ? s.Substring(0, eq + 1) + "\"" + s.Substring(eq + 1) + "\"" : s; }
        public static string IntentName(string intent)
        {
            switch (intent) { case "required": return "Required"; case "available": return "Available for enrolled devices"; case "uninstall": return "Uninstall"; case "exclude": return "Excluded"; default: return "Included"; }
        }

        /// <summary>Setup only: "device NAME key=value ..." sets a pretend device's state (BitLocker, TPM, risk...).</summary>
        static EpResult DeviceState(EpTenant t, List<string> tok)
        {
            if (tok.Count < 2) return EpResult.Fail("device NAME key=value");
            var d = t.Device(tok[1]);
            if (d == null) return EpResult.Fail("no device " + tok[1]);
            foreach (var kv in Pairs(tok, 2, null))
            {
                if (EpText.Eq(kv.Key, "compliance")) { d.Compliance = kv.Value; continue; }
                if (EpText.Eq(kv.Key, "sync")) { t.Sync(d); continue; }
                if (EpText.Eq(kv.Key, "lastSyncDaysAgo")) { d.LastSync = t.Now.AddDays(-int.Parse(kv.Value)); d.NextSync = t.Now.AddDays(30); continue; }
                d[kv.Key] = kv.Value;
            }
            return new EpResult { Message = "ok" };
        }
    }
}
