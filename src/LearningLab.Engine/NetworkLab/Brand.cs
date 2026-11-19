// Network Lab - the made-up maker of the routers and switches. Everything that names the brand asks here; the
// names come from brand.json (shipped) and the person's Settings ("Device brand"). Projects never store it.
using System;
using System.Collections.Generic;
using System.IO;

namespace NetworkLab
{
    public static class Brand
    {
        static string name = "Generic";
        static string osTemplate = "{brand} Internetwork Operating System";
        static string aboutTemplate = "{brand} is a made-up brand for this simulator.";
        static string aboutExtra = "";
        static readonly Dictionary<string, string> models = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static string shippedName = "Generic";

        /// <summary>Reads brand.json; a non-empty override (the person's setting) replaces the brand name.</summary>
        public static void Load(string brandJsonPath, string overrideName)
        {
            try
            {
                if (!string.IsNullOrEmpty(brandJsonPath) && File.Exists(brandJsonPath))
                {
                    var j = Json.ParseObj(File.ReadAllText(brandJsonPath));
                    if (j != null)
                    {
                        name = j.Str("brand", name);
                        shippedName = name;
                        osTemplate = j.Str("os", osTemplate);
                        aboutTemplate = j.Str("about", aboutTemplate);
                        aboutExtra = j.Str("aboutExtra", "");
                        var m = j.Obj("models");
                        if (m != null) foreach (var kv in m) models[kv.Key] = Convert.ToString(kv.Value);
                    }
                }
            }
            catch (Exception) { }
            SetName(overrideName);
        }

        /// <summary>Learning Lab: reads the brand.json built into the engine (App Hub read the file next to the app).</summary>
        public static void LoadBuiltIn(string overrideName)
        {
            try
            {
                using (var s = typeof(Brand).Assembly.GetManifestResourceStream("NetworkLab/brand.json"))
                using (var r = new StreamReader(s)) ReadJson(r.ReadToEnd());
            }
            catch (Exception) { }
            SetName(overrideName);
        }

        static void ReadJson(string text)
        {
            var j = Json.ParseObj(text);
            if (j == null) return;
            name = j.Str("brand", name);
            shippedName = name;
            osTemplate = j.Str("os", osTemplate);
            aboutTemplate = j.Str("about", aboutTemplate);
            aboutExtra = j.Str("aboutExtra", "");
            var m = j.Obj("models");
            if (m != null) foreach (var kv in m) models[kv.Key] = Convert.ToString(kv.Value);
        }

        public static void SetName(string overrideName)
        {
            if (!string.IsNullOrEmpty(overrideName) && overrideName.Trim().Length > 0) name = overrideName.Trim();
            else name = shippedName;
        }

        public static string Name { get { return name; } }
        public static string ShippedName { get { return shippedName; } }
        public static string Os { get { return osTemplate.Replace("{brand}", name); } }
        public static string About { get { return aboutTemplate.Replace("{aboutExtra}", name == shippedName ? aboutExtra : "").Replace("{brand}", name); } }

        /// <summary>The model name of a kind of device (router, switch, homerouter, ap, patch), or "".</summary>
        public static string ModelOf(string kind)
        {
            string m;
            if (models.TryGetValue(kind, out m)) return m.Replace("{brand}", name);
            return "";
        }
        /// <summary>"Brand Model" for branded kinds, "" for the others.</summary>
        public static string FullModel(string kind)
        {
            string m = ModelOf(kind);
            return m.Length == 0 ? "" : name + " " + m;
        }
    }
}
