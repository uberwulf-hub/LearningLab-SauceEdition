// Cert Lab - the members Windows PowerShell 5.1 shows for a .NET type. The pretend PowerShell lists the members of a
// few whitelisted .NET types (string, DateTime, TimeSpan, numbers...) by reflection, as App Hub did on .NET Framework
// 4.8. .NET 10 has many more (spans, Microsecond, Replace with a StringComparison...), which 5.1 never shows, so they
// are left out here: by parameter types that .NET Framework doesn't have, and a list of the newer members of the
// types the courses show.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace CertLab
{
    public static class NetFxSurface
    {
        // parameter or return types that .NET Framework 4.8 doesn't have
        static readonly HashSet<string> newTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "Span`1", "ReadOnlySpan`1", "Memory`1", "ReadOnlyMemory`1", "Rune", "Index", "Range", "CompositeFormat",
            "StringRuneEnumerator", "SpanSplitEnumerator`1", "DateOnly", "TimeOnly", "Half", "Int128", "UInt128",
            "MidpointRounding+ToZero", "IUtf8SpanFormattable", "ISpanFormattable", "SearchValues`1", "Vector`1",
        };
        // newer members by type: "Name" (every overload) or "Name(param types)" (one overload)
        static readonly Dictionary<string, HashSet<string>> newMembers = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            { "System.String", new HashSet<string>(StringComparer.Ordinal) {
                "Replace(String,String,StringComparison)", "Replace(String,String,Boolean,CultureInfo)", "Contains(Char)",
                "Contains(Char,StringComparison)", "Contains(String,StringComparison)", "Split(Char,StringSplitOptions)",
                "Split(Char,Int32,StringSplitOptions)", "Split(String,StringSplitOptions)", "Split(String,Int32,StringSplitOptions)",
                "StartsWith(Char)", "EndsWith(Char)", "GetHashCode(StringComparison)", "IndexOf(Char,StringComparison)",
                "Trim(Char)", "TrimStart()", "TrimStart(Char)", "TrimEnd()", "TrimEnd(Char)", "ReplaceLineEndings",
                "EnumerateRunes", "GetPinnableReference", "TryCopyTo", "CopyTo(Span`1)" } },
            { "System.DateTime", new HashSet<string>(StringComparer.Ordinal) {
                "Microsecond", "Nanosecond", "AddMicroseconds", "Deconstruct", "TryFormat" } },
            { "System.TimeSpan", new HashSet<string>(StringComparer.Ordinal) {
                "TotalMicroseconds", "TotalNanoseconds", "Microseconds", "Nanoseconds", "Multiply", "Divide", "TryFormat" } },
        };

        static string Sig(MethodInfo m) { return m.Name + "(" + string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name).ToArray()) + ")"; }

        static bool NewType(Type t)
        {
            if (t.IsByRef || t.IsArray || t.IsPointer) t = t.GetElementType();
            return newTypes.Contains(t.Name) || t.IsGenericType && t.GetGenericArguments().Any(NewType);
        }

        /// <summary>True when Windows PowerShell 5.1 (.NET Framework 4.8) has this method overload.</summary>
        public static bool Has(MethodInfo m)
        {
            if (m.IsGenericMethodDefinition && m.DeclaringType == typeof(string)) return false;
            if (NewType(m.ReturnType) || m.GetParameters().Any(p => NewType(p.ParameterType))) return false;
            if (m.Name == "TryFormat") return false;
            HashSet<string> n;
            var dt = m.ReflectedType ?? m.DeclaringType;
            if (dt != null && newMembers.TryGetValue(dt.FullName ?? "", out n) && (n.Contains(m.Name) || n.Contains(Sig(m)))) return false;
            return true;
        }

        /// <summary>True when Windows PowerShell 5.1 has this property.</summary>
        public static bool Has(PropertyInfo p)
        {
            if (NewType(p.PropertyType)) return false;
            HashSet<string> n;
            var dt = p.ReflectedType ?? p.DeclaringType;
            return !(dt != null && newMembers.TryGetValue(dt.FullName ?? "", out n) && n.Contains(p.Name));
        }
    }
}
