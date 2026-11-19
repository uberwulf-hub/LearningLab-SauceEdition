// Learning Lab - Languages: Spanish verb forms, worked out in code (like the math in Learning Games), never typed by
// hand. Persons as spoken in Latin America: yo, tú, él/ella/usted, nosotros, ellos/ustedes (no vosotros).
//   Present: regular -ar / -er / -ir; stem changes e>ie, o>ue, e>i, u>ue (jugar) in yo, tú, él and ellos; yo spelling
//            changes -ger/-gir (escojo), -guir (sigo), vowel + -cer/-cir (conozco), consonant + -cer (venzo); -uir puts
//            a y in (construyo).
//   Preterite: regular; yo -car/-gar/-zar (busqué, llegué, empecé); -ir stem changers in él and ellos (pidió, durmió);
//            -er/-ir after a vowel (leyó, leíste; construyó).
//   Near future: ir a + infinitive (voy a comer).
// Irregular forms come from the verb list (spanish-verbs.json): a list of 5 per tense where null means "use the rule".
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LearningLab.Languages
{
    public enum Tense { Present, Preterite, NearFuture }
    public enum StemChange { None, EIe, OUe, EI, UUe }

    public sealed class VerbForm
    {
        public string Text;
        /// <summary>True when the verb list gives this form (it doesn't follow a rule).</summary>
        public bool Irregular;
        /// <summary>A rule that changes the regular form (a stem or spelling change), in plain words, else null.</summary>
        public string Change;
        public override string ToString() { return Text; }
    }

    public sealed class LangVerb
    {
        public string Infinitive;
        public string English;
        public int Level = 1;
        public string Group;
        public StemChange Stem;
        public List<Tense> Tenses = new List<Tense>();
        /// <summary>Per tense, 5 forms (yo, tú, él, nosotros, ellos); null = use the rule.</summary>
        public Dictionary<Tense, string[]> Irregular = new Dictionary<Tense, string[]>();
        // what the checks report
        public Dictionary<string, int> IrregularLengths = new Dictionary<string, int>();
        public List<string> UnknownTenses = new List<string>();
        public string StemProblem;
        /// <summary>The persons the trainer asks (0 yo .. 4 ellos): all five, except weather verbs (only él: llueve) and
        /// gustar-type verbs (él and ellos: gusta, gustan). From the optional "persons" field, else <see cref="OnlyThird"/>.</summary>
        public int[] Persons = { 0, 1, 2, 3, 4 };

        /// <summary>Verbs used (almost) only in the third person, when the list doesn't say: weather (only él) and
        /// verbs like gustar (él and ellos).</summary>
        public static readonly Dictionary<string, int[]> OnlyThird = new Dictionary<string, int[]>
        {
            ["llover"] = new[] { 2 }, ["nevar"] = new[] { 2 }, ["lloviznar"] = new[] { 2 }, ["tronar"] = new[] { 2 }, ["granizar"] = new[] { 2 },
            ["gustar"] = new[] { 2, 4 }, ["encantar"] = new[] { 2, 4 }, ["doler"] = new[] { 2, 4 }, ["importar"] = new[] { 2, 4 },
            ["interesar"] = new[] { 2, 4 }, ["faltar"] = new[] { 2, 4 }, ["costar"] = new[] { 2, 4 }, ["molestar"] = new[] { 2, 4 }, ["fascinar"] = new[] { 2, 4 },
        };

        public bool HasIrregular(Tense t) { string[] a; return Irregular.TryGetValue(t, out a) && a.Any(x => x != null); }

        static string S(JsonNode n) { try { string s; return n != null && n.GetValueKind() == JsonValueKind.String && n.AsValue().TryGetValue(out s) ? s : null; } catch (InvalidOperationException) { return null; } }
        static JsonNode Any(JsonObject o, params string[] names) { foreach (var n in names) if (o.ContainsKey(n)) return o[n]; return null; }

        /// <summary>"yo" 0, "tú" 1, "él" (ella, usted) 2, "nosotros" 3, "ellos" (ellas, ustedes) 4, else -1.</summary>
        public static int PersonOf(string s)
        {
            switch ((s ?? "").Trim().ToLowerInvariant())
            {
                case "yo": return 0;
                case "t\u00FA": case "tu": return 1;
                case "\u00E9l": case "el": case "ella": case "usted": case "\u00E9l/ella/usted": return 2;
                case "nosotros": case "nosotras": return 3;
                case "ellos": case "ellas": case "ustedes": case "ellos/ustedes": return 4;
                default: return -1;
            }
        }

        public static Tense? TenseOf(string name)
        {
            string s = (name ?? "").Trim().ToLowerInvariant().Replace("_", "-").Replace(" ", "-");
            switch (s)
            {
                case "present": case "presente": return Tense.Present;
                case "preterite": case "preterit": case "past": case "pretérito": case "preterito": return Tense.Preterite;
                case "near-future": case "nearfuture": case "ir-a": case "future": return Tense.NearFuture;
                default: return null;
            }
        }

        public static StemChange? StemOf(string s)
        {
            s = (s ?? "").Trim().ToLowerInvariant().Replace(" ", "").Replace("→", ">").Replace("->", ">").Replace("-", ">").Replace("to", ">");
            if (s == "" || s == "none" || s == "no" || s == "regular") return StemChange.None;
            if (s.Contains("u>ue") || s == "uue") return StemChange.UUe;
            if (s.Contains("ue")) return StemChange.OUe;
            if (s.Contains("ie")) return StemChange.EIe;
            if (s.Contains("e>i") || s == "i" || s == "ei") return StemChange.EI;
            return null;
        }

        /// <summary>Reads spanish-verbs.json: { "format": 1, "verbs": [ { "verb": "tener", "en": "to have", "level": 1,
        /// "stem": "ie", "tenses": ["present", "preterite"], "irregular": { "present": ["tengo", null, null, null, null],
        /// "preterite": [...] } } ] }. "infinitive" for "verb", "english" for "en", "forms" for "irregular" also work.</summary>
        public static List<LangVerb> ParseFile(string text)
        {
            var root = JsonNode.Parse(text, null, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            JsonArray arr = root as JsonArray ?? (root as JsonObject)?["verbs"] as JsonArray;
            if (arr == null) throw new FormatException("no \"verbs\" list in it");
            var list = new List<LangVerb>();
            foreach (var n in arr)
            {
                var o = n as JsonObject;
                if (o == null) continue;
                var v = new LangVerb
                {
                    Infinitive = S(Any(o, "verb", "infinitive", "inf", "es"))?.Trim(),
                    English = S(Any(o, "en", "english", "meaning"))?.Trim(),
                    Group = S(Any(o, "group")),
                };
                var lv = Any(o, "level");
                if (lv != null) { try { int i; if (lv.AsValue().TryGetValue(out i)) v.Level = i; else { double d; if (lv.AsValue().TryGetValue(out d)) v.Level = (int)d; } } catch (InvalidOperationException) { } }
                string stem = S(Any(o, "stem", "stemChange", "change"));
                if (stem == null && v.Group != null) { var g = StemOf(v.Group); if (g != null && g != StemChange.None) stem = v.Group; }
                var sc = StemOf(stem);
                if (sc == null) { v.StemProblem = "stem change \"" + stem + "\" not understood (ie, ue, i or u-ue)"; sc = StemChange.None; }
                v.Stem = sc.Value;
                if (Any(o, "tenses") is JsonArray ts)
                {
                    foreach (var t in ts) { var tt = TenseOf(S(t)); if (tt == null) v.UnknownTenses.Add(S(t) ?? "?"); else if (!v.Tenses.Contains(tt.Value)) v.Tenses.Add(tt.Value); }
                }
                else { v.Tenses.Add(Tense.Present); v.Tenses.Add(Tense.Preterite); }
                if (Any(o, "persons") is JsonArray ps)
                {
                    var l = new List<int>();
                    foreach (var x in ps)
                    {
                        int k = -1;
                        try { int i; string s; if (x.AsValue().TryGetValue(out i)) k = i; else if (x.AsValue().TryGetValue(out s)) k = PersonOf(s); } catch (InvalidOperationException) { }
                        if (k < 0 || k > 4) v.StemProblem = (v.StemProblem == null ? "" : v.StemProblem + "; ") + "person \"" + x + "\" not understood";
                        else if (!l.Contains(k)) l.Add(k);
                    }
                    if (l.Count > 0) v.Persons = l.OrderBy(k => k).ToArray();
                }
                else if (v.Infinitive != null && OnlyThird.TryGetValue(v.Infinitive.ToLowerInvariant(), out var only)) v.Persons = only;
                if (Any(o, "irregular", "forms", "overrides") is JsonObject irr)
                {
                    foreach (var kv in irr)
                    {
                        var tt = TenseOf(kv.Key);
                        var a = kv.Value as JsonArray;
                        if (tt == null) { v.UnknownTenses.Add(kv.Key); continue; }
                        if (a == null) { v.IrregularLengths[kv.Key] = -1; continue; }
                        v.IrregularLengths[kv.Key] = a.Count;
                        var forms = new string[5];
                        for (int k = 0; k < 5 && k < a.Count; k++) { var s = S(a[k]); forms[k] = string.IsNullOrWhiteSpace(s) ? null : s.Trim(); }
                        v.Irregular[tt.Value] = forms;
                    }
                }
                list.Add(v);
            }
            return list;
        }
    }

    public static class Conjugator
    {
        public static readonly string[] Persons = { "yo", "tú", "él/ella/usted", "nosotros", "ellos/ustedes" };
        public static readonly string[] Reflexive = { "me", "te", "se", "nos", "se" };
        public static readonly Tense[] AllTenses = { Tense.Present, Tense.Preterite, Tense.NearFuture };

        public static string TenseName(Tense t)
        {
            switch (t) { case Tense.Preterite: return "preterite"; case Tense.NearFuture: return "near future"; default: return "present"; }
        }
        public static string TenseHelp(Tense t)
        {
            switch (t)
            {
                case Tense.Preterite: return "what happened (finished in the past)";
                case Tense.NearFuture: return "what is going to happen (ir a + verb)";
                default: return "what happens now or usually";
            }
        }

        /// <summary>"ar", "er" or "ir" (oír counts as -ir; a reflexive verb by its own ending), else null.</summary>
        public static string Ending(string inf)
        {
            if (string.IsNullOrWhiteSpace(inf)) return null;
            string s = Base(inf);
            if (s.Length < 3) return s == "ir" ? "ir" : null;
            if (s.EndsWith("ar")) return "ar";
            if (s.EndsWith("er")) return "er";
            if (s.EndsWith("ir") || s.EndsWith("ír")) return "ir";
            return null;
        }

        public static bool IsReflexive(string inf)
        {
            string s = (inf ?? "").Trim().ToLowerInvariant();
            return s.Length > 4 && s.EndsWith("se") && (s.EndsWith("arse") || s.EndsWith("erse") || s.EndsWith("irse") || s.EndsWith("írse"));
        }

        static string Base(string inf)
        {
            string s = inf.Trim().ToLowerInvariant();
            return IsReflexive(s) ? s.Substring(0, s.Length - 2) : s;
        }

        static bool Vowel(char c) { return "aeiouáéíóú".IndexOf(c) >= 0; }

        static string ReplaceLast(string s, char what, string with)
        {
            int k = s.LastIndexOf(what);
            return k < 0 ? s : s.Substring(0, k) + with + s.Substring(k + 1);
        }

        static string StemChanged(string stem, StemChange c)
        {
            switch (c)
            {
                case StemChange.EIe: return ReplaceLast(stem, 'e', "ie");
                case StemChange.OUe: return ReplaceLast(stem, 'o', "ue");
                case StemChange.EI: return ReplaceLast(stem, 'e', "i");
                case StemChange.UUe: return ReplaceLast(stem, 'u', "ue");
                default: return stem;
            }
        }

        static string ChangeName(StemChange c)
        {
            switch (c)
            {
                case StemChange.EIe: return "stem change e → ie";
                case StemChange.OUe: return "stem change o → ue";
                case StemChange.EI: return "stem change e → i";
                case StemChange.UUe: return "stem change u → ue";
                default: return null;
            }
        }

        /// <summary>The form by the rules (no list of irregular forms); change says which rule changed it, if any.</summary>
        public static string ByRule(string infinitive, StemChange stemChange, Tense t, int person, out string change)
        {
            change = null;
            string inf = Base(infinitive);
            string end = Ending(infinitive);
            if (end == null) return null;
            if (t == Tense.NearFuture)
            {
                string[] ir = { "voy", "vas", "va", "vamos", "van" };
                string target = IsReflexive(infinitive) ? inf + Reflexive[person] : inf;
                return ir[person] + " a " + target;
            }
            string stem = inf.Substring(0, inf.Length - 2);
            string form;
            if (t == Tense.Present)
            {
                string[] e = end == "ar" ? new[] { "o", "as", "a", "amos", "an" } : end == "er" ? new[] { "o", "es", "e", "emos", "en" } : new[] { "o", "es", "e", "imos", "en" };
                string s = stem;
                bool boot = person != 3;
                if (boot && stemChange != StemChange.None) { s = StemChanged(stem, stemChange); change = ChangeName(stemChange); }
                if (end == "ir" && stem.EndsWith("u") && !stem.EndsWith("gu") && !stem.EndsWith("qu") && boot)
                {
                    s += "y"; change = "a y goes in (construyo)";
                }
                if (person == 0)
                {
                    if (end != "ar" && s.EndsWith("g") && !s.EndsWith("gu")) { s = s.Substring(0, s.Length - 1) + "j"; change = Join(change, "spelling: g → j before o"); }
                    else if (end == "ir" && s.EndsWith("gu")) { s = s.Substring(0, s.Length - 1); change = Join(change, "spelling: gu → g before o"); }
                    else if (end != "ar" && s.EndsWith("c") && s.Length >= 2)
                    {
                        if (Vowel(s[s.Length - 2])) { s = s.Substring(0, s.Length - 1) + "zc"; change = Join(change, "spelling: c → zc before o"); }
                        else { s = s.Substring(0, s.Length - 1) + "z"; change = Join(change, "spelling: c → z before o"); }
                    }
                }
                form = s + e[person];
            }
            else
            {
                string[] e = end == "ar" ? new[] { "é", "aste", "ó", "amos", "aron" } : new[] { "í", "iste", "ió", "imos", "ieron" };
                string s = stem;
                if (end == "ar" && person == 0)
                {
                    if (s.EndsWith("c")) { s = s.Substring(0, s.Length - 1) + "qu"; change = "spelling: c → qu before é"; }
                    else if (s.EndsWith("g")) { s = s + "u"; change = "spelling: g → gu before é"; }
                    else if (s.EndsWith("z")) { s = s.Substring(0, s.Length - 1) + "c"; change = "spelling: z → c before é"; }
                }
                if (end == "ir" && (person == 2 || person == 4) && stemChange != StemChange.None && stemChange != StemChange.UUe)
                {
                    if (stemChange == StemChange.OUe) s = ReplaceLast(s, 'o', "u"); else s = ReplaceLast(s, 'e', "i");
                    change = stemChange == StemChange.OUe ? "stem change o → u (in él and ellos)" : "stem change e → i (in él and ellos)";
                }
                if (end != "ar" && s.Length > 0 && Vowel(s[s.Length - 1]) && !s.EndsWith("gu") && !s.EndsWith("qu"))
                {
                    bool u = s.EndsWith("u");
                    if (person == 2) { form = s + "yó"; change = "an i between vowels becomes y"; return Pron(infinitive, person, form); }
                    if (person == 4) { form = s + "yeron"; change = "an i between vowels becomes y"; return Pron(infinitive, person, form); }
                    if (!u && (person == 1 || person == 3)) { form = s + (person == 1 ? "íste" : "ímos"); change = "an accent keeps the i apart"; return Pron(infinitive, person, form); }
                }
                form = s + e[person];
            }
            return Pron(infinitive, person, form);
        }

        static string Join(string a, string b) { return a == null ? b : a + "; " + b; }
        static string Pron(string infinitive, int person, string form) { return IsReflexive(infinitive) ? Reflexive[person] + " " + form : form; }

        /// <summary>A verb's form: the verb list's irregular form when it gives one, else the rule.</summary>
        public static VerbForm Form(LangVerb v, Tense t, int person)
        {
            string[] list;
            if (t != Tense.NearFuture && v.Irregular.TryGetValue(t, out list) && person < list.Length && list[person] != null)
            {
                string text = list[person];
                if (IsReflexive(v.Infinitive) && !text.Contains(' ')) text = Reflexive[person] + " " + text;
                return new VerbForm { Text = text, Irregular = true };
            }
            string change;
            string f = ByRule(v.Infinitive, v.Stem, t, person, out change);
            return new VerbForm { Text = f, Change = change };
        }

        /// <summary>The prompt's words: "nosotros · comer · preterite".</summary>
        public static string Prompt(LangVerb v, Tense t, int person)
        {
            return Persons[person] + " · " + v.Infinitive + " · " + TenseName(t);
        }
    }
}
