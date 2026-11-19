// Cert Lab - what each kind of object in the pretend tenant looks like: its fields (label, type, choices,
// required, ranges, the section of the form, when a field shows), where it lives in the practice portal and how it
// is assigned. The portal draws its forms from these, the checks read the same keys, and EpOps validates every
// create and edit with them (plus the rules in EpValidate). Field specs are compact text:
//   key | Label | type[:choices] | flags     (flags: req, min=N, max=N, sec=Section, if=key=a/b, def=X, help=...)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CertLab
{
    public class EpField
    {
        public string Key = "", Label = "", Type = "text", Section = "Basics", Default = "", Help = "", If = "";
        public string[] Options = new string[0];
        public bool Required;
        public int Min = int.MinValue, Max = int.MaxValue;
        /// <summary>Does the field show for this object (If = "platform=Windows 10 and later/iOS/iPadOS")?</summary>
        public bool Shows(Func<string, string> get)
        {
            if (If.Length == 0) return true;
            int eq = If.IndexOf('=');
            string v = get(If.Substring(0, eq));
            return If.Substring(eq + 1).Split('/').Any(x => EpText.Eq(x, v));
        }
    }

    public class EpKind
    {
        public string Kind = "", Title = "", Plural = "", Area = "", Blade = "", Sub = "";   // Sub: the field that picks the platform / type
        public bool Assignable;
        public string[] Intents = { "include", "exclude" };
        public string[] Columns = new string[0];
        public readonly List<EpField> Fields = new List<EpField>();
        public EpField Field(string key) { return Fields.FirstOrDefault(f => EpText.Eq(f.Key, key)); }
    }

    public static partial class EpSchemas
    {
        public static readonly Dictionary<string, EpKind> Kinds = new Dictionary<string, EpKind>(StringComparer.OrdinalIgnoreCase);
        public static EpKind Get(string kind) { Init(); EpKind k; return Kinds.TryGetValue(kind ?? "", out k) ? k : null; }
        public static IEnumerable<EpKind> AllKinds { get { Init(); return Kinds.Values; } }
        static bool ready;

        public const string Win = "Windows 10 and later", Ios = "iOS/iPadOS", Android = "Android Enterprise", Mac = "macOS";

        static EpKind K(string kind, string title, string plural, string area, string blade, string sub, bool assign, string[] cols, params string[] fields)
        {
            var k = new EpKind { Kind = kind, Title = title, Plural = plural, Area = area, Blade = blade, Sub = sub, Assignable = assign, Columns = cols };
            k.Fields.Add(new EpField { Key = "name", Label = "Name", Required = true, Section = "Basics" });
            foreach (var f in fields) k.Fields.Add(ParseField(f));
            Kinds[kind] = k;
            return k;
        }
        static EpField ParseField(string spec)
        {
            var p = spec.Split('|').Select(x => x.Trim()).ToArray();
            var f = new EpField { Key = p[0], Label = p.Length > 1 ? p[1] : p[0] };
            if (p.Length > 2)
            {
                string t = p[2]; int c = t.IndexOf(':');
                f.Type = c < 0 ? t : t.Substring(0, c);
                if (c >= 0) f.Options = t.Substring(c + 1).Split(',').Select(x => x.Trim().Replace("&#44;", ",")).ToArray();
                if (f.Type == "choice" && f.Options.Length > 0) f.Default = f.Options[0];
            }
            string first = f.Default;
            for (int i = 3; i < p.Length; i++) Flags(f, p[i]);
            // a required choice starts empty (the person must pick), unless the spec gives def=
            if (f.Required && f.Type == "choice" && f.Default == first && !p.Skip(3).Any(x => x.StartsWith("def=", StringComparison.Ordinal))) f.Default = "";
            return f;
        }
        static void Flags(EpField f, string s)
        {
            var raw = s.Trim();
            {
                if (raw == "req") f.Required = true;
                else if (raw.StartsWith("min=", StringComparison.Ordinal)) f.Min = int.Parse(raw.Substring(4), CultureInfo.InvariantCulture);
                else if (raw.StartsWith("max=", StringComparison.Ordinal)) f.Max = int.Parse(raw.Substring(4), CultureInfo.InvariantCulture);
                else if (raw.StartsWith("sec=", StringComparison.Ordinal)) f.Section = raw.Substring(4);
                else if (raw.StartsWith("if=", StringComparison.Ordinal)) f.If = raw.Substring(3);
                else if (raw.StartsWith("def=", StringComparison.Ordinal)) f.Default = raw.Substring(4);
                else if (raw.StartsWith("help=", StringComparison.Ordinal)) f.Help = raw.Substring(5);
            }
        }

        public static void Init()
        {
            if (ready) return;
            ready = true;
            InitIdentity();
            InitDevices();
            InitUpdates();
            InitApps();
            InitSecurity();
            InitAccess();
        }

        /// <summary>The field's value, or its default when the object doesn't set it.</summary>
        public static string Value(EpObj o, string key)
        {
            var v = o.Get(key);
            if (v.Length > 0) return v;
            var k = Get(o.Kind);
            var f = k == null ? null : k.Field(key);
            return f == null ? "" : f.Default;
        }
        public static string Label(string kind, string key)
        {
            var k = Get(kind);
            var f = k == null ? null : k.Field(key);
            if (f != null) return f.Label;
            if (key.StartsWith("s:", StringComparison.Ordinal)) { var s = EpCatalog.Find(key.Substring(2)); return s == null ? key.Substring(2) : s.Name; }
            if (key.StartsWith("asr:", StringComparison.Ordinal)) { var r = EpCatalog.Asr(key.Substring(4)); return r == null ? key.Substring(4) : r.Name; }
            return key;
        }
    }
}
