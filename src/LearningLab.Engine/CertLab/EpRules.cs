// Cert Lab - rules in the pretend tenant: dynamic group membership rules (Microsoft Entra ID style:
// (user.department -eq "Sales") -and (user.accountEnabled -eq true)) and Intune assignment filters
// ((device.osVersion -startsWith "10.0.22") and (device.deviceOwnership -eq "Corporate")). One small parser
// for both: comparisons, -not, -and / -or (filters also take and / or), parentheses, -in [..], and
// -any / -all over multi-valued properties such as device.devicePhysicalIds. Values compare without regard to case.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CertLab
{
    public class EpRuleError : Exception { public EpRuleError(string m) : base(m) { } }

    public abstract class EpRuleNode
    {
        public abstract bool Eval(Func<string, List<string>> get);
        public abstract void Props(List<string> into);
    }

    class EpRuleAnd : EpRuleNode
    {
        public EpRuleNode A, B; public bool Or;
        public override bool Eval(Func<string, List<string>> get) { return Or ? A.Eval(get) || B.Eval(get) : A.Eval(get) && B.Eval(get); }
        public override void Props(List<string> into) { A.Props(into); B.Props(into); }
    }
    class EpRuleNot : EpRuleNode
    {
        public EpRuleNode A;
        public override bool Eval(Func<string, List<string>> get) { return !A.Eval(get); }
        public override void Props(List<string> into) { A.Props(into); }
    }
    class EpRuleCmp : EpRuleNode
    {
        public string Prop = "", Op = "", Value = ""; public List<string> Values = new List<string>();
        public EpRuleCmp Inner;   // -any / -all (_ -op "value")
        public override void Props(List<string> into) { into.Add(Prop); }
        public override bool Eval(Func<string, List<string>> get)
        {
            var vals = get(Prop) ?? new List<string>();
            if (Op == "-any") return vals.Any(v => Inner.Test(v));
            if (Op == "-all") return vals.Count > 0 && vals.All(v => Inner.Test(v));
            string one = vals.Count == 0 ? "" : vals[0];
            if (vals.Count > 1 && (Op == "-contains" || Op == "-eq")) return vals.Any(v => Test(v));
            return Test(one);
        }
        public bool Test(string v)
        {
            v = v ?? "";
            var c = StringComparison.OrdinalIgnoreCase;
            switch (Op)
            {
                case "-eq": return Value == "null" ? v.Length == 0 : string.Equals(v, Value, c);
                case "-ne": return Value == "null" ? v.Length > 0 : !string.Equals(v, Value, c);
                case "-startswith": return v.StartsWith(Value, c);
                case "-notstartswith": return !v.StartsWith(Value, c);
                case "-endswith": return v.EndsWith(Value, c);
                case "-notendswith": return !v.EndsWith(Value, c);
                case "-contains": return v.IndexOf(Value, c) >= 0;
                case "-notcontains": return v.IndexOf(Value, c) < 0;
                case "-match": try { return Regex.IsMatch(v, Value, RegexOptions.IgnoreCase); } catch (ArgumentException) { return false; }
                case "-notmatch": try { return !Regex.IsMatch(v, Value, RegexOptions.IgnoreCase); } catch (ArgumentException) { return true; }
                case "-in": return Values.Any(x => string.Equals(v, x, c));
                case "-notin": return !Values.Any(x => string.Equals(v, x, c));
            }
            return false;
        }
    }

    /// <summary>Parses a rule; Kind "group" (dynamic membership) or "filter" (assignment filter).</summary>
    public static class EpRules
    {
        static readonly string[] Ops = { "-eq", "-ne", "-startswith", "-notstartswith", "-endswith", "-notendswith", "-contains", "-notcontains", "-match", "-notmatch", "-in", "-notin", "-any", "-all" };

        public static readonly string[] UserProps = { "accountEnabled", "city", "companyName", "country", "department", "displayName", "employeeId", "jobTitle", "mail", "officeLocation", "state", "usageLocation", "userPrincipalName", "userType" };
        public static readonly string[] DeviceProps = { "accountEnabled", "deviceCategory", "deviceId", "deviceManufacturer", "deviceModel", "deviceOSType", "deviceOSVersion", "deviceOwnership", "devicePhysicalIds", "displayName", "enrollmentProfileName", "isRooted", "managementType", "profileType" };
        public static readonly string[] FilterProps = { "deviceName", "manufacturer", "model", "osVersion", "deviceOwnership", "enrollmentProfileName", "operatingSystemSKU", "deviceCategory", "deviceTrustType", "isRooted" };

        public static EpRuleNode Parse(string text, string kind)
        {
            var toks = Tokens(text ?? "");
            if (toks.Count == 0) throw new EpRuleError("The rule is empty.");
            int i = 0;
            var n = Or(toks, ref i, kind);
            if (i < toks.Count) throw new EpRuleError("The rule has something extra at '" + toks[i] + "'.");
            var props = new List<string>(); n.Props(props);
            if (kind == "group")
            {
                bool u = props.Any(p => p.StartsWith("user.", StringComparison.OrdinalIgnoreCase)), d = props.Any(p => p.StartsWith("device.", StringComparison.OrdinalIgnoreCase));
                if (u && d) throw new EpRuleError("A rule can use user properties or device properties, not both.");
            }
            return n;
        }
        /// <summary>"user", "device" or "" for the properties a rule uses.</summary>
        public static string Subject(EpRuleNode n) { var props = new List<string>(); n.Props(props); return props.Count == 0 ? "" : props[0].Substring(0, props[0].IndexOf('.')).ToLowerInvariant(); }
        /// <summary>Null when the rule is good, else what is wrong in plain words.</summary>
        public static string Check(string text, string kind) { try { Parse(text, kind); return null; } catch (EpRuleError e) { return e.Message; } }

        static List<string> Tokens(string s)
        {
            var l = new List<string>();
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (c == '(' || c == ')' || c == '[' || c == ']' || c == ',') { l.Add(c.ToString()); i++; continue; }
                if (c == '"' || c == '\u201C' || c == '\u201D' || c == '\'')
                {
                    char q = c == '\'' ? '\'' : '"';
                    var sb = new StringBuilder("\"");
                    i++;
                    while (i < s.Length && s[i] != q && s[i] != '\u201D') sb.Append(s[i++]);
                    if (i >= s.Length) throw new EpRuleError("A value in quotes isn't closed.");
                    i++;
                    l.Add(sb.ToString());
                    continue;
                }
                int st = i;
                while (i < s.Length && !char.IsWhiteSpace(s[i]) && "()[],\"".IndexOf(s[i]) < 0) i++;
                l.Add(s.Substring(st, i - st));
            }
            return l;
        }

        static EpRuleNode Or(List<string> t, ref int i, string kind)
        {
            var a = And(t, ref i, kind);
            while (i < t.Count && (Low(t[i]) == "-or" || Low(t[i]) == "or")) { i++; a = new EpRuleAnd { A = a, B = And(t, ref i, kind), Or = true }; }
            return a;
        }
        static EpRuleNode And(List<string> t, ref int i, string kind)
        {
            var a = Unary(t, ref i, kind);
            while (i < t.Count && (Low(t[i]) == "-and" || Low(t[i]) == "and")) { i++; a = new EpRuleAnd { A = a, B = Unary(t, ref i, kind) }; }
            return a;
        }
        static EpRuleNode Unary(List<string> t, ref int i, string kind)
        {
            if (i >= t.Count) throw new EpRuleError("The rule ends too early.");
            if (Low(t[i]) == "-not" || Low(t[i]) == "not") { i++; return new EpRuleNot { A = Unary(t, ref i, kind) }; }
            if (t[i] == "(")
            {
                i++;
                var n = Or(t, ref i, kind);
                if (i >= t.Count || t[i] != ")") throw new EpRuleError("A bracket is opened but not closed.");
                i++;
                return n;
            }
            return Cmp(t, ref i, kind, false);
        }
        static string Low(string s) { return s.ToLowerInvariant(); }

        static EpRuleCmp Cmp(List<string> t, ref int i, string kind, bool inner)
        {
            string prop = t[i++];
            if (inner) { if (prop != "_") throw new EpRuleError("Inside -any or -all, compare _ (each value), for example (_ -startsWith \"[ZTDid]\")."); }
            else prop = Prop(prop, kind);
            if (i >= t.Count) throw new EpRuleError("'" + prop + "' needs an operator such as -eq.");
            string op = Low(t[i++]);
            if (!Ops.Contains(op)) throw new EpRuleError("'" + t[i - 1] + "' isn't an operator the rule builder knows (try -eq, -ne, -startsWith, -contains, -match, -in).");
            var c = new EpRuleCmp { Prop = prop, Op = op };
            if (op == "-any" || op == "-all")
            {
                if (inner) throw new EpRuleError("-any and -all can't be nested.");
                if (i >= t.Count || t[i] != "(") throw new EpRuleError(op + " needs a condition in brackets: (_ -eq \"value\").");
                i++;
                c.Inner = Cmp(t, ref i, kind, true);
                if (i >= t.Count || t[i] != ")") throw new EpRuleError("A bracket is opened but not closed.");
                i++;
                return c;
            }
            if (i >= t.Count) throw new EpRuleError(op + " needs a value.");
            if (op == "-in" || op == "-notin")
            {
                if (t[i] != "[") throw new EpRuleError(op + " needs a list in square brackets: [\"a\",\"b\"].");
                i++;
                while (i < t.Count && t[i] != "]") { if (t[i] != ",") c.Values.Add(Val(t[i])); i++; }
                if (i >= t.Count) throw new EpRuleError("A list is opened with [ but not closed.");
                i++;
                return c;
            }
            string v = t[i++];
            if (!v.StartsWith("\"", StringComparison.Ordinal) && v != "true" && v != "false" && v != "null" && !Regex.IsMatch(v, "^-?\\d+$")) throw new EpRuleError("The value " + v + " must be in double quotes.");
            c.Value = Val(v);
            return c;
        }
        static string Val(string tok) { return tok.StartsWith("\"", StringComparison.Ordinal) ? tok.Substring(1) : tok; }

        static string Prop(string p, string kind)
        {
            int dot = p.IndexOf('.');
            if (dot < 0) throw new EpRuleError("'" + p + "' isn't a property: write user.department or device.deviceOSType.");
            string who = p.Substring(0, dot).ToLowerInvariant(), name = p.Substring(dot + 1);
            string[] known = kind == "filter" ? (who == "device" ? FilterProps : null) : who == "user" ? UserProps : who == "device" ? DeviceProps : null;
            if (known == null) throw new EpRuleError(kind == "filter" ? "Filters use device properties (device.osVersion, device.model...)." : "Rules start with user. or device.");
            var k = known.FirstOrDefault(x => x.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (k == null) throw new EpRuleError("The property '" + p + "' isn't supported" + (kind == "filter" ? " in filters." : " in membership rules."));
            return who + "." + k;
        }

        /// <summary>Does the rule match? get returns a property's values (one for most, several for devicePhysicalIds).</summary>
        public static bool Matches(string rule, string kind, Func<string, List<string>> get)
        {
            try { return Parse(rule, kind).Eval(get); } catch (EpRuleError) { return false; }
        }
    }
}
