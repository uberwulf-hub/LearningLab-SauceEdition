// Cert Lab - saving and loading the simulated machines: every public field of the model classes goes to JSON
// (through Json.cs) and back. Fields marked [NonSerialized] are runtime only (sessions, caches).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;

namespace CertLab
{
    public static class Ser
    {
        static readonly Dictionary<Type, FieldInfo[]> fieldCache = new Dictionary<Type, FieldInfo[]>();

        static FieldInfo[] Fields(Type t)
        {
            FieldInfo[] f;
            lock (fieldCache)
            {
                if (!fieldCache.TryGetValue(t, out f))
                {
                    var l = new List<FieldInfo>();
                    foreach (var fi in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
                        if (!fi.IsNotSerialized && !fi.IsInitOnly) l.Add(fi);
                    f = l.ToArray();
                    fieldCache[t] = f;
                }
            }
            return f;
        }

        public static object ToJson(object o)
        {
            if (o == null) return null;
            var t = o.GetType();
            if (o is string || o is bool) return o;
            if (o is int || o is long || o is double || o is float || o is short || o is byte || o is uint) return Convert.ToDouble(o, CultureInfo.InvariantCulture);
            if (o is char) return ((char)o).ToString();
            if (t.IsEnum) return o.ToString();
            var dict = o as IDictionary;
            if (dict != null)
            {
                var j = new JObj();
                foreach (DictionaryEntry kv in dict) j[Convert.ToString(kv.Key, CultureInfo.InvariantCulture)] = ToJson(kv.Value);
                return j;
            }
            var list = o as IList;
            if (list != null)
            {
                var l = new List<object>();
                foreach (var x in list) l.Add(ToJson(x));
                return l;
            }
            var obj = new JObj();
            foreach (var fi in Fields(t))
            {
                var v = fi.GetValue(o);
                if (v == null) continue;
                obj[fi.Name] = ToJson(v);
            }
            return obj;
        }

        public static T FromJson<T>(object j) { return (T)FromJson(j, typeof(T)); }

        public static object FromJson(object j, Type t)
        {
            if (j == null) return t.IsValueType ? Activator.CreateInstance(t) : null;
            if (t == typeof(string)) return Convert.ToString(j, CultureInfo.InvariantCulture);
            if (t == typeof(bool)) return j is bool ? (bool)j : false;
            if (t == typeof(int)) return Convert.ToInt32(j, CultureInfo.InvariantCulture);
            if (t == typeof(long)) return Convert.ToInt64(j, CultureInfo.InvariantCulture);
            if (t == typeof(double)) return Convert.ToDouble(j, CultureInfo.InvariantCulture);
            if (t == typeof(char)) { var s = Convert.ToString(j, CultureInfo.InvariantCulture); return s.Length > 0 ? s[0] : '\0'; }
            if (t.IsEnum) return Enum.Parse(t, Convert.ToString(j, CultureInfo.InvariantCulture));
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                var args = t.GetGenericArguments();
                var d = (IDictionary)Activator.CreateInstance(t);
                var jo = j as JObj;
                if (jo != null)
                    foreach (var kv in jo)
                    {
                        object key = args[0] == typeof(int) ? (object)int.Parse(kv.Key, CultureInfo.InvariantCulture) : kv.Key;
                        d[key] = FromJson(kv.Value, args[1]);
                    }
                return d;
            }
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
            {
                var et = t.GetGenericArguments()[0];
                var l = (IList)Activator.CreateInstance(t);
                var jl = j as List<object>;
                if (jl != null) foreach (var x in jl) l.Add(FromJson(x, et));
                return l;
            }
            var o = Activator.CreateInstance(t);
            var job = j as JObj;
            if (job == null) return o;
            foreach (var fi in Fields(t))
            {
                object v;
                if (job.TryGetValue(fi.Name, out v)) fi.SetValue(o, FromJson(v, fi.FieldType));
            }
            return o;
        }

        /// <summary>A deep copy through the same path as saving (what is saved is what is copied).</summary>
        public static T Clone<T>(T o) { return FromJson<T>(Json.Parse(Json.Write(ToJson(o)))); }
    }
}
