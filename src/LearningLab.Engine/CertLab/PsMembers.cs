// Cert Lab - members of values in the simulated PowerShell: properties and methods of simulated objects, errors,
// types and enums, and of the few real .NET types it allows (strings, numbers, dates, collections, Math, Regex,
// StringBuilder...) through reflection; static members ([DateTime]::Now is the machines' clock, [Environment] and
// [System.IO.File] are the simulated machine's). Nothing here reaches the real PC: every other .NET type is refused.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace CertLab
{
    /// <summary>[ref]$x: a reference to a variable (for TryParse and friends).</summary>
    public class PsRef
    {
        public Func<object> Getter; public Action<object> Setter;
        public object Value { get { return Getter(); } set { Setter(value); } }
        public override string ToString() { return "System.Management.Automation.PSReference"; }
    }

    public class PsMemberInfo { public string Name = "", MemberType = "Property", Definition = "", TypeName = ""; }

    public static class PsMembers
    {
        const BindingFlags Inst = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;
        const BindingFlags Stat = BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase;

        public static object Wrap(object r)
        {
            if (r == null) return null;
            if (r is Enum)
            {
                var et = r.GetType();
                var pe = PsEnums.Get(et.FullName);
                if (pe == null)
                {
                    var names = Enum.GetNames(et);
                    var vals = Enum.GetValues(et).Cast<object>().Select(x => Convert.ToInt64(x, CultureInfo.InvariantCulture)).ToArray();
                    pe = PsEnums.Define(et.FullName, string.Join(",", names.Select((n, i) => n + "=" + vals[i]).ToArray()), et.GetCustomAttributes(typeof(FlagsAttribute), false).Length > 0);
                }
                return pe.Of(Convert.ToInt64(r, CultureInfo.InvariantCulture));
            }
            if (r is PsObj || r is PsEnum || r is PsScriptBlock || r is PsErrorRecord || r is PsException || r is PsTypeInfo || r is PsSecureString || r is PsSwitch || r is PsRef || r is AutoNull) return r;
            if (r is DictionaryEntry) return r;
            var t = r.GetType();
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(KeyValuePair<,>)) return r;
            if (PsTypes.IsAllowedClr(t)) return r;
            if (r is IEnumerable && !(r is string)) return ((IEnumerable)r).Cast<object>().Select(Wrap).ToArray();
            return r.ToString();
        }

        static bool Allowed(object target) { return target != null && PsTypes.IsAllowedClr(target.GetType()); }

        public static object Get(PsRunspace R, object target, string name, out bool found)
        {
            found = true;
            var o = target as PsObj;
            if (o != null)
            {
                var p = o.Find(name);
                if (p != null) return o.ValueOf(p);
                if (name.Equals("psobject", StringComparison.OrdinalIgnoreCase)) return PsObjectView(o);
                PsMethod m;
                if (o.Methods.TryGetValue(name, out m)) return MethodInfo(name, new[] { m.Definition });
                if (name.Equals("PSComputerName", StringComparison.OrdinalIgnoreCase) && o.Computer != null) return o.Computer;
                found = false; return null;
            }
            if (name.Equals("psobject", StringComparison.OrdinalIgnoreCase)) return PsObjectView(target);
            var d = target as IDictionary;
            if (d != null)
            {
                if (d.Contains(name)) return d[name];
                switch (name.ToLowerInvariant())
                {
                    case "count": return d.Count;
                    case "keys": return d.Keys.Cast<object>().ToArray();
                    case "values": return d.Values.Cast<object>().ToArray();
                    case "isreadonly": return d.IsReadOnly;
                    case "isfixedsize": return d.IsFixedSize;
                    case "issynchronized": return false;
                    case "syncroot": return d;
                }
                found = false; return null;
            }
            if (target is DictionaryEntry)
            {
                var e = (DictionaryEntry)target;
                if (name.Equals("Key", StringComparison.OrdinalIgnoreCase) || name.Equals("Name", StringComparison.OrdinalIgnoreCase)) return e.Key;
                if (name.Equals("Value", StringComparison.OrdinalIgnoreCase)) return e.Value;
                found = false; return null;
            }
            var en = target as PsEnum;
            if (en != null) { if (name.Equals("value__", StringComparison.OrdinalIgnoreCase)) return (int)en.Value; found = false; return null; }
            var er = target as PsErrorRecord;
            if (er != null) return ErrorMember(R, er, name, out found);
            var ex = target as PsException;
            if (ex != null)
            {
                switch (name.ToLowerInvariant())
                {
                    case "message": return ex.Message;
                    case "innerexception": return ex.Inner;
                    case "source": return "System.Management.Automation";
                    case "hresult": return -2146233087;
                    case "stacktrace": return "";
                    case "data": return PsConv.NewHashtable();
                    case "helplink": return null;
                    case "targetsite": return null;
                    case "itemname": return null;
                    case "wasthrownfromthrowstatement": return false;
                }
                found = false; return null;
            }
            var ti = target as PsTypeInfo;
            if (ti != null)
            {
                switch (name.ToLowerInvariant())
                {
                    case "name": return ti.Name;
                    case "fullname": return ti.FullName;
                    case "namespace": return ti.Namespace;
                    case "basetype": return new PsTypeInfo { FullName = ti.BaseType };
                    case "ispublic": return true;
                    case "isserializable": case "isserial": return !ti.FullName.Contains("ScriptBlock") && !ti.FullName.Contains("ServiceController");
                    case "isarray": return ti.FullName.EndsWith("[]", StringComparison.Ordinal);
                    case "isenum": return PsEnums.Get(ti.FullName) != null;
                    case "isvaluetype": return ti.BaseType == "System.ValueType" || PsEnums.Get(ti.FullName) != null;
                    case "isclass": return ti.BaseType != "System.ValueType";
                    case "assemblyqualifiedname": return ti.FullName;
                    case "underlyingsystemtype": return ti;
                }
                found = false; return null;
            }
            var sb = target as PsScriptBlock;
            if (sb != null)
            {
                switch (name.ToLowerInvariant())
                {
                    case "file": return sb.Ast != null && sb.Ast.Src != null && sb.Ast.Src.File.Length > 0 ? sb.Ast.Src.File : null;
                    case "isfilter": return false;
                    case "module": return sb.Module;
                    case "attributes": return new object[0];
                    case "ast": return sb.Text;
                }
                found = false; return null;
            }
            var ss = target as PsSecureString;
            if (ss != null) { if (name.Equals("Length", StringComparison.OrdinalIgnoreCase)) return ss.Length; found = false; return null; }
            var sw = target as PsSwitch;
            if (sw != null) { if (name.Equals("IsPresent", StringComparison.OrdinalIgnoreCase)) return sw.IsPresent; found = false; return null; }
            var rf = target as PsRef;
            if (rf != null) { if (name.Equals("Value", StringComparison.OrdinalIgnoreCase)) return rf.Value; found = false; return null; }
            if (target is PsModule) return ((PsModule)target).Member(name, out found);
            if (target is DateTime)
            {
                if (name.Equals("DateTime", StringComparison.OrdinalIgnoreCase)) return ((DateTime)target).ToString("dddd, MMMM d, yyyy h:mm:ss tt", PsConv.Us);
                if (name.Equals("DisplayHint", StringComparison.OrdinalIgnoreCase)) return "DateTime";
            }
            if (Allowed(target))
            {
                var t = target.GetType();
                var pi = t.GetProperty(name, Inst);
                if (pi != null && pi.GetIndexParameters().Length == 0)
                {
                    try { return Wrap(pi.GetValue(target, null)); }
                    catch (TargetInvocationException e) { throw R.RtError(null, "Exception getting \"" + name + "\": \"" + e.InnerException.Message + "\"", "ExceptionWhenGetting", "NotSpecified", "System.Management.Automation.GetValueInvocationException", null); }
                }
                var fi = t.GetField(name, Inst);
                if (fi != null) return Wrap(fi.GetValue(target));
                if (target is Array && name.Equals("Count", StringComparison.OrdinalIgnoreCase)) return ((Array)target).Length;
                var ms = t.GetMethods(Inst).Where(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && NetFxSurface.Has(m)).ToList();
                if (ms.Count > 0) return MethodInfo(ms[0].Name, ms.Select(Definition).ToArray());
            }
            found = false; return null;
        }

        public static PsObj MethodInfo(string name, string[] overloads)
        {
            var m = new PsObj("System.Management.Automation.PSMethod", "System.Management.Automation.PSMethodInfo", "System.Management.Automation.PSMemberInfo", "System.Object");
            m.Val("OverloadDefinitions", "System.Collections.ObjectModel.Collection[string]", overloads.Cast<object>().ToArray());
            m.Val("TypeNameOfValue", "string", "System.Management.Automation.PSMethod");
            m.Val("MemberType", "System.Management.Automation.PSMemberTypes", PsEnums.V("System.Management.Automation.PSMemberTypes", "Method"));
            m.Val("Value", "System.Management.Automation.PSMethod", string.Join(", ", overloads));
            m.Val("Name", "string", name);
            m.Val("IsInstance", "bool", true);
            m.DefaultDisplay = new[] { "OverloadDefinitions" };
            m.ToStr = x => string.Join(", ", overloads);
            return m;
        }

        /// <summary>$obj.psobject: Properties, Methods, Members, TypeNames, BaseObject.</summary>
        public static PsObj PsObjectView(object target)
        {
            var v = new PsObj("System.Management.Automation.PSObject", "System.Object");
            v.Prop("Properties", "System.Management.Automation.PSMemberInfoCollection[psproperty]", () => Properties(target).Select(kv => (object)PropInfo(target, kv.Key, kv.Value)).ToArray());
            v.Prop("Methods", "System.Management.Automation.PSMemberInfoCollection[psmethodinfo]", () => ListMembers(target).Where(m => m.MemberType.EndsWith("Method", StringComparison.Ordinal)).Select(m => (object)MethodInfo(m.Name, m.Definition.Split(new[] { ", " }, StringSplitOptions.None))).ToArray());
            v.Prop("Members", "System.Management.Automation.PSMemberInfoCollection[psmemberinfo]", () => ListMembers(target).Select(m => { var o = new PsObj("System.Management.Automation.PSMemberInfo"); o.Val("Name", "string", m.Name); o.Val("MemberType", "string", m.MemberType); o.Val("Definition", "string", m.Definition); return (object)o; }).ToArray());
            v.Prop("TypeNames", "System.Collections.ObjectModel.Collection[string]", () => TypeNames(target).Cast<object>().ToArray());
            v.Prop("BaseObject", "System.Object", () => target);
            v.Prop("ImmediateBaseObject", "System.Object", () => target);
            v.ToStr = x => PsConv.ToStr(target);
            return v;
        }
        static PsObj PropInfo(object target, string name, object value)
        {
            var o = new PsObj("System.Management.Automation.PSNoteProperty", "System.Management.Automation.PSPropertyInfo", "System.Object");
            var po = target as PsObj;
            var p = po == null ? null : po.Find(name);
            o.Val("Value", "System.Object", value);
            o.Val("MemberType", "System.Management.Automation.PSMemberTypes", PsEnums.V("System.Management.Automation.PSMemberTypes", p == null ? "Property" : p.Kind));
            o.Val("IsSettable", "bool", p == null || !p.ReadOnly);
            o.Val("IsGettable", "bool", true);
            o.Val("TypeNameOfValue", "string", value == null ? "System.Object" : PsTypes.TypeNameOf(value));
            o.Val("Name", "string", name);
            o.Val("IsInstance", "bool", true);
            return o;
        }

        public static List<string> TypeNames(object target)
        {
            target = PsConv.Base(target);
            var o = target as PsObj;
            if (o != null) return o.TypeNames.ToList();
            var l = new List<string>();
            if (target == null) return l;
            string n = PsTypes.TypeNameOf(target);
            for (int i = 0; i < 10 && n != null; i++) { l.Add(n); if (n == "System.Object") break; n = PsTypes.BaseOf(n); }
            if (target is PsException) { l.Clear(); l.AddRange(((PsException)target).Chain()); }
            return l;
        }

        static object ErrorMember(PsRunspace R, PsErrorRecord er, string name, out bool found)
        {
            found = true;
            switch (name.ToLowerInvariant())
            {
                case "exception": return er.Exception;
                case "targetobject": return er.TargetObject;
                case "fullyqualifiederrorid": return er.FqId;
                case "errordetails": return er.ErrorDetails == null ? null : (object)ErrorDetailsObj(er.ErrorDetails);
                case "categoryinfo": return CategoryObj(er);
                case "invocationinfo": return InvocationObj(er);
                case "scriptstacktrace": return er.StackTrace;
                case "pipelineiterationinfo": return new object[0];
                case "psmessagedetails": return null;
            }
            found = false; return null;
        }
        static PsObj ErrorDetailsObj(string m) { var o = new PsObj("System.Management.Automation.ErrorDetails", "System.Object"); o.Val("Message", "string", m); o.Val("RecommendedAction", "string", null); o.ToStr = x => m; return o; }
        public static PsObj CategoryObj(PsErrorRecord er)
        {
            var o = new PsObj("System.Management.Automation.ErrorCategoryInfo", "System.Object");
            o.Val("Category", "System.Management.Automation.ErrorCategory", er.Category);
            o.Val("Activity", "string", er.Activity.Length > 0 ? er.Activity : er.CommandName);
            o.Val("Reason", "string", er.Reason.Length > 0 ? er.Reason : er.Exception.ShortName);
            o.Val("TargetName", "string", er.TargetName);
            o.Val("TargetType", "string", er.TargetType);
            o.ToStr = x => er.CategoryText;
            return o;
        }
        public static PsObj InvocationObj(PsErrorRecord er)
        {
            var o = new PsObj("System.Management.Automation.InvocationInfo", "System.Object");
            o.Val("MyCommand", "System.Management.Automation.CommandInfo", er.CommandName.Length > 0 ? er.CommandName : null);
            o.Val("BoundParameters", "System.Collections.Generic.Dictionary[string,System.Object]", PsConv.NewHashtable());
            o.Val("UnboundArguments", "System.Collections.Generic.List[System.Object]", new object[0]);
            o.Val("ScriptLineNumber", "int", er.LineNumber);
            o.Val("OffsetInLine", "int", er.Column);
            o.Val("HistoryId", "long", 1L);
            o.Val("ScriptName", "string", er.ScriptName);
            o.Val("Line", "string", er.Line);
            o.Val("PositionMessage", "string", (er.ScriptName.Length > 0 ? "At " + er.ScriptName + ":" + er.LineNumber : "At line:" + er.LineNumber) + " char:" + er.Column + "\n+ " + er.Line + "\n+ " + new string(' ', Math.Max(0, er.Column - 1)) + new string('~', Math.Max(1, er.Length)));
            o.Val("PSScriptRoot", "string", er.ScriptName.Length > 0 ? er.ScriptName.Substring(0, Math.Max(0, er.ScriptName.LastIndexOf('\\'))) : "");
            o.Val("PSCommandPath", "string", er.ScriptName.Length > 0 ? er.ScriptName : null);
            o.Val("InvocationName", "string", er.CommandName);
            o.Val("PipelineLength", "int", 0);
            o.Val("PipelinePosition", "int", 0);
            o.Val("ExpectingInput", "bool", false);
            o.Val("CommandOrigin", "System.Management.Automation.CommandOrigin", "Internal");
            return o;
        }

        // ------------------------------------------------------------ methods
        public static bool ClrHasMethod(object target, string name)
        {
            if (!Allowed(target)) return false;
            return target.GetType().GetMethods(Inst).Any(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        public static object Call(PsRunspace R, object target, string name, object[] args, PsAst at, PsScope sc, out bool found)
        {
            found = true;
            string ln = name.ToLowerInvariant();
            var o = target as PsObj;
            if (o != null)
            {
                PsMethod m;
                if (o.Methods.TryGetValue(name, out m))
                {
                    try { return R.RunMethod(m, o, args, sc, o); }
                    catch (PsRuntimeException) { throw; }
                    catch (PsMethodError e) { throw MethodError(R, at, name, args.Length, e.Message, e.ExceptionType); }
                    catch (PsConvertError e) { throw MethodError(R, at, name, args.Length, e.Message, "System.Management.Automation.PSInvalidCastException"); }
                }
                if (ln == "tostring" && args.Length == 0) return o.ToString();
                if (ln == "equals" && args.Length == 1) return ReferenceEquals(o, PsConv.Base(args[0]));
                if (ln == "gethashcode") return o.GetHashCode();
                found = false; return null;
            }
            if (ln == "tostring" && args.Length == 0 && !(target is IFormattable)) return PsConv.ToStr(target);
            var er = target as PsErrorRecord;
            if (er != null) { if (ln == "tostring") return er.ToString(); found = false; return null; }
            var ex = target as PsException;
            if (ex != null) { if (ln == "tostring") return ex.ToString(); if (ln == "getbaseexception") { var b = ex; while (b.Inner != null) b = b.Inner; return b; } found = false; return null; }
            var en = target as PsEnum;
            if (en != null)
            {
                if (ln == "tostring") { if (args.Length == 1 && PsConv.ToStr(args[0]).Equals("d", StringComparison.OrdinalIgnoreCase)) return en.Value.ToString(CultureInfo.InvariantCulture); return en.Name; }
                if (ln == "hasflag") { var f = PsConv.To(args[0], new PsType { FullName = en.Type.FullName, Enum = en.Type }) as PsEnum; return f != null && (en.Value & f.Value) == f.Value; }
                if (ln == "compareto") return en.CompareTo(PsConv.To(args[0], new PsType { FullName = en.Type.FullName, Enum = en.Type }));
                if (ln == "equals") return PsConv.Eq(en, args[0], false);
                if (ln == "gethashcode") return (int)en.Value;
                if (ln == "gettypecode") return "Int32";
                found = false; return null;
            }
            var ti = target as PsTypeInfo;
            if (ti != null)
            {
                var e = PsEnums.Get(ti.FullName);
                if (ln == "getenumnames" && e != null) return e.Values.Select(v => (object)v.Key).ToArray();
                if (ln == "getenumvalues" && e != null) return e.Values.Select(v => (object)e.Of(v.Value)).ToArray();
                if (ln == "tostring") return ti.FullName;
                if (ln == "isassignablefrom") return false;
                found = false; return null;
            }
            var sb = target as PsScriptBlock;
            if (sb != null)
            {
                if (ln == "invoke" || ln == "invokereturnasis") { var l = R.InvokeBlock(sb, sc, null, args, false); return ln == "invoke" ? (object)new ArrayList(l) : PsConv.Collapse(l); }
                if (ln == "getnewclosure") return sb;
                if (ln == "tostring") return sb.Text;
                found = false; return null;
            }
            var ss = target as PsSecureString;
            if (ss != null)
            {
                if (ln == "appendchar") { ss.Plain += PsConv.ToStr(args[0]); return AutoNull.Value; }
                if (ln == "clear") { ss.Plain = ""; return AutoNull.Value; }
                if (ln == "dispose" || ln == "makereadonly") return AutoNull.Value;
                if (ln == "copy") return new PsSecureString { Plain = ss.Plain };
                if (ln == "isreadonly") return false;
                found = false; return null;
            }
            var sw = target as PsSwitch;
            if (sw != null) { if (ln == "tobool") return sw.IsPresent; found = false; return null; }
            if (target is PsModule) { found = false; return null; }
            if (target is DictionaryEntry) { if (ln == "tostring") return "System.Collections.DictionaryEntry"; found = false; return null; }
            // arrays: PowerShell exposes IList's members
            if (target is Array && !(target is IList && false))
            {
                var arr = (Array)target;
                if (ln == "contains" && args.Length == 1) return arr.Cast<object>().Any(x => PsConv.Eq(x, args[0], true) || Equals(x, args[0]));
                if (ln == "indexof" && args.Length == 1) { int i = 0; foreach (var x in arr) { if (Equals(x, args[0]) || PsConv.Eq(x, args[0], true)) return i; i++; } return -1; }
                if (ln == "add" || ln == "remove" || ln == "removeat" || ln == "insert" || ln == "clear" && false)
                    throw MethodError(R, at, name, args.Length, "Collection was of a fixed size.", "System.NotSupportedException");
            }
            if (Allowed(target)) return ClrCall(R, target.GetType(), target, name, args, at, out found);
            found = false; return null;
        }

        public static PsRuntimeException MethodError(PsRunspace R, PsAst at, string name, int n, string inner, string exType)
        {
            var e = R.RtError(at, "Exception calling \"" + name + "\" with \"" + n + "\" argument(s): \"" + inner + "\"", ShortEx(exType), "NotSpecified", "System.Management.Automation.MethodInvocationException", null);
            e.Rec.Exception.Inner = new PsException { TypeFullName = exType ?? "System.Exception", Message = inner };
            return e;
        }
        static string ShortEx(string t) { if (t == null) return "MethodInvocationException"; int i = t.LastIndexOf('.'); return i < 0 ? t : t.Substring(i + 1); }

        static readonly CultureInfo Us = PsConv.Us;

        /// <summary>Calls a method of a whitelisted .NET type, picking the overload like PowerShell (argument count, then conversions).</summary>
        public static object ClrCall(PsRunspace R, Type t, object target, string name, object[] args, PsAst at, out bool found)
        {
            found = true;
            var flags = target == null ? Stat : Inst;
            var all = t.GetMethods(flags).Where(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && !m.IsGenericMethodDefinition).ToList();
            if (all.Count == 0) { found = false; return null; }
            // culture: ToString(format) on dates and numbers uses en-US like the pretend machines
            if (target is IFormattable && name.Equals("ToString", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length == 0) return target is DateTime ? ((DateTime)target).ToString(Us) : target is double ? PsConv.DoubleStr((double)target) : ((IFormattable)target).ToString(null, Us);
                if (args.Length == 1 && (PsConv.Base(args[0]) is string || args[0] == null)) { try { return ((IFormattable)PsConv.FwNum(target)).ToString(PsConv.ToStr(args[0]), Us); } catch (FormatException e) { throw MethodError(R, at, name, 1, e.Message, "System.FormatException"); } }
            }
            MethodBase best = null; object[] bestArgs = null; int bestScore = -1;
            foreach (var m in all)
            {
                object[] conv; int score;
                if (TryArgs(m.GetParameters(), args, out conv, out score) && score > bestScore) { best = m; bestArgs = conv; bestScore = score; }
            }
            if (best == null)
            {
                if (!all.Any(m => m.GetParameters().Length == args.Length || m.GetParameters().Length > 0 && m.GetParameters().Last().GetCustomAttributes(typeof(ParamArrayAttribute), false).Length > 0))
                    throw R.RtError(at, "Cannot find an overload for \"" + all[0].Name + "\" and the argument count: \"" + args.Length + "\".", "MethodCountCouldNotFindBest", "NotSpecified", "System.Management.Automation.MethodException", null);
                throw R.RtError(at, "Cannot convert argument for \"" + all[0].Name + "\": no overload of " + all[0].Name + " accepts these arguments.", "MethodArgumentConversionInvalidCastArgument", "NotSpecified", "System.Management.Automation.MethodException", null);
            }
            try
            {
                var res = ((MethodInfo)best).Invoke(target, bestArgs);
                // [ref] arguments
                var ps = best.GetParameters();
                for (int i = 0; i < ps.Length && i < args.Length; i++) if (ps[i].ParameterType.IsByRef && args[i] is PsRef) ((PsRef)args[i]).Value = Wrap(bestArgs[i]);
                if (((MethodInfo)best).ReturnType == typeof(void)) return AutoNull.Value;
                return Wrap(res);
            }
            catch (TargetInvocationException e)
            {
                var ie = e.InnerException;
                throw MethodError(R, at, best.Name, args.Length, ie.Message, ie.GetType().FullName);
            }
        }

        public static object ClrNew(PsRunspace R, Type t, object[] args, PsAst at)
        {
            var ctors = t.GetConstructors();
            ConstructorInfo best = null; object[] bestArgs = null; int bestScore = -1;
            foreach (var c in ctors)
            {
                object[] conv; int score;
                if (TryArgs(c.GetParameters(), args, out conv, out score) && score > bestScore) { best = c; bestArgs = conv; bestScore = score; }
            }
            if (best == null)
            {
                if (args.Length == 0 && t.IsValueType) return Wrap(Activator.CreateInstance(t));
                string tn = t.IsGenericType ? t.Name.Substring(0, t.Name.IndexOf('`')) : t.Name;
                throw R.RtError(at, "Cannot find an overload for \"" + tn + "\" and the argument count: \"" + args.Length + "\".", "ConstructorInvokedThrowException", "InvalidOperation", "System.Management.Automation.MethodException", null);
            }
            try { return Wrap(best.Invoke(bestArgs)); }
            catch (TargetInvocationException e) { throw R.RtError(at, "Exception calling \".ctor\" with \"" + args.Length + "\" argument(s): \"" + e.InnerException.Message + "\"", "ConstructorInvokedThrowException", "InvalidOperation", "System.Management.Automation.MethodInvocationException", null); }
        }

        static bool TryArgs(ParameterInfo[] ps, object[] args, out object[] conv, out int score)
        {
            conv = null; score = 0;
            bool paramsArr = ps.Length > 0 && ps[ps.Length - 1].GetCustomAttributes(typeof(ParamArrayAttribute), false).Length > 0;
            int fixedN = paramsArr ? ps.Length - 1 : ps.Length;
            if (!paramsArr && args.Length != ps.Length) return false;
            if (paramsArr && args.Length < fixedN) return false;
            var res = new object[ps.Length];
            for (int i = 0; i < fixedN; i++)
            {
                object c; int sc;
                if (!ConvArg(args[i], ps[i].ParameterType, out c, out sc)) return false;
                res[i] = c; score += sc;
            }
            if (paramsArr)
            {
                var et = ps[ps.Length - 1].ParameterType.GetElementType();
                // one argument that already is an array goes in as the array
                if (args.Length == ps.Length && (PsConv.Base(args[args.Length - 1]) is Array || PsConv.Base(args[args.Length - 1]) is string && et == typeof(char)))
                {
                    object c; int sc;
                    if (ConvArg(args[args.Length - 1], ps[ps.Length - 1].ParameterType, out c, out sc)) { res[ps.Length - 1] = c; score += sc + 1; conv = res; return true; }
                }
                var rest = Array.CreateInstance(et, args.Length - fixedN);
                for (int i = fixedN; i < args.Length; i++)
                {
                    object c; int sc;
                    if (!ConvArg(args[i], et, out c, out sc)) return false;
                    rest.SetValue(c, i - fixedN); score += sc;
                }
                res[ps.Length - 1] = rest;
                score -= 1;
            }
            conv = res;
            return true;
        }

        /// <summary>One argument for a .NET parameter. Score 3 exact, 2 close, 1 converted. $null becomes "" for string (as in PowerShell).</summary>
        static bool ConvArg(object a, Type pt, out object c, out int score)
        {
            c = null; score = 1;
            if (pt.IsByRef) { pt = pt.GetElementType(); if (a is PsRef) { var v = ((PsRef)a).Value; c = v == null ? (pt.IsValueType ? Activator.CreateInstance(pt) : null) : (pt.IsInstanceOfType(v) ? v : null); if (c == null && pt.IsValueType) c = Activator.CreateInstance(pt); score = 2; return true; } }
            a = PsConv.Base(a);
            if (a is PsEnum && pt.IsEnum) { try { c = Enum.ToObject(pt, ((PsEnum)a).Value); score = 3; return true; } catch (ArgumentException) { return false; } }
            if (a is PsEnum && pt != typeof(object) && pt != typeof(string)) a = ((PsEnum)a).Value;
            if (a == null)
            {
                if (pt == typeof(string)) { c = ""; score = 2; return true; }   // the $null-to-string trap: .NET gets ""
                if (pt.IsValueType) return false;
                c = null; score = 2; return true;
            }
            if (pt.IsInstanceOfType(a) && !(a is PsObj && pt == typeof(object) && false)) { c = a; score = pt == a.GetType() ? 3 : 2; return true; }
            if (pt == typeof(object)) { c = a; score = 1; return true; }
            if (pt == typeof(Type)) return false;
            try
            {
                if (pt == typeof(string)) { if (a is PsObj || PsConv.IsCollection(a)) { c = PsConv.ToStr(a); score = 0; return true; } c = PsConv.ToStr(a); score = 1; return true; }
                if (pt == typeof(char[]) && a is string) { c = ((string)a).ToCharArray(); score = 1; return true; }
                if (pt == typeof(char)) { if (a is string && ((string)a).Length == 1) { c = ((string)a)[0]; score = 2; return true; } if (PsConv.IsNumber(a)) { c = (char)PsConv.ToInt(a); return true; } return false; }
                if (pt.IsEnum)
                {
                    if (PsConv.IsNumber(a)) { c = Enum.ToObject(pt, PsConv.ToLong(a)); return true; }
                    c = Enum.Parse(pt, PsConv.ToStr(a), true); return true;
                }
                if (pt == typeof(int) || pt == typeof(long) || pt == typeof(double) || pt == typeof(decimal) || pt == typeof(float) || pt == typeof(short) || pt == typeof(byte) || pt == typeof(uint) || pt == typeof(ulong))
                {
                    if (a is PsObj || a is IDictionary) return false;
                    if (a is string) { object n; if (!PsConv.TryNumber((string)a, out n)) return false; a = n; score = 0; }
                    else if (!(PsConv.IsNumber(a) || a is bool || a is char)) return false;
                    if ((pt == typeof(int) || pt == typeof(long)) && (a is double || a is decimal) && score > 0) score = 0;
                    var t = PsConv.Resolve(pt);
                    c = PsConv.To(a, t);
                    if (pt == typeof(float)) c = Convert.ToSingle(c, CultureInfo.InvariantCulture);
                    if (PsConv.IsNumber(a) && a.GetType() == pt) score = 3; else if (PsConv.IsNumber(a) && score > 0) score = 2;
                    return true;
                }
                if (pt == typeof(bool)) { c = PsConv.ToBool(a); return true; }
                if (pt == typeof(DateTime)) { c = PsConv.To(a, PsTypes.Resolve("datetime")); return true; }
                if (pt == typeof(TimeSpan)) { c = PsConv.To(a, PsTypes.Resolve("timespan")); return true; }
                if (pt.IsArray)
                {
                    var et = pt.GetElementType();
                    var items = PsConv.Items(a).ToList();
                    var arr = Array.CreateInstance(et, items.Count);
                    for (int i = 0; i < items.Count; i++) { object ci; int si; if (!ConvArg(items[i], et, out ci, out si)) return false; arr.SetValue(ci, i); }
                    c = arr; score = 1; return true;
                }
                if (pt.IsGenericType && (pt.GetGenericTypeDefinition() == typeof(IEnumerable<>)))
                {
                    var et = pt.GetGenericArguments()[0];
                    var items = PsConv.Items(a).ToList();
                    var arr = Array.CreateInstance(et, items.Count);
                    for (int i = 0; i < items.Count; i++) { object ci; int si; if (!ConvArg(items[i], et, out ci, out si)) return false; arr.SetValue(ci, i); }
                    c = arr; score = 0; return true;
                }
                if (pt == typeof(IEnumerable) || pt == typeof(ICollection) || pt == typeof(IList)) { if (PsConv.IsCollection(a)) { c = PsConv.List(a).ToArray(); return true; } return false; }
                if (pt == typeof(Regex)) { c = new Regex(PsConv.ToStr(a)); return true; }
                if (pt == typeof(CultureInfo) || pt == typeof(IFormatProvider)) { c = Us; return true; }
            }
            catch (PsConvertError) { return false; }
            catch (FormatException) { return false; }
            catch (ArgumentException) { return false; }
            catch (OverflowException) { return false; }
            catch (InvalidCastException) { return false; }
            return false;
        }

        public static bool SetClr(PsRunspace R, object target, string name, object v)
        {
            if (!Allowed(target) || target.GetType().IsValueType) return false;
            var pi = target.GetType().GetProperty(name, Inst);
            if (pi == null || !pi.CanWrite) return false;
            object c; int sc;
            if (!ConvArg(v, pi.PropertyType, out c, out sc)) throw R.RtError(null, "Exception setting \"" + name + "\": \"Cannot convert the value.\"", "ExceptionWhenSetting", "NotSpecified", "System.Management.Automation.SetValueInvocationException", null);
            try { pi.SetValue(target, c, null); }
            catch (TargetInvocationException e) { throw R.RtError(null, "Exception setting \"" + name + "\": \"" + e.InnerException.Message + "\"", "ExceptionWhenSetting", "NotSpecified", "System.Management.Automation.SetValueInvocationException", null); }
            return true;
        }

        // ------------------------------------------------------------ listing members (Get-Member, Format-List *, Select-Object *)
        static string TypeShort(Type t)
        {
            if (t == null) return "void";
            if (t.IsByRef) return "[ref] " + TypeShort(t.GetElementType());
            if (t == typeof(int)) return "int"; if (t == typeof(string)) return "string"; if (t == typeof(bool)) return "bool"; if (t == typeof(char)) return "char";
            if (t == typeof(long)) return "long"; if (t == typeof(double)) return "double"; if (t == typeof(void)) return "void"; if (t == typeof(object)) return "System.Object";
            if (t == typeof(byte)) return "byte"; if (t == typeof(decimal)) return "decimal"; if (t == typeof(float)) return "float"; if (t == typeof(short)) return "int16";
            if (t == typeof(DateTime)) return "datetime"; if (t == typeof(TimeSpan)) return "timespan";
            if (t == typeof(System.Globalization.CultureInfo)) return "cultureinfo"; if (t == typeof(System.Text.RegularExpressions.Regex)) return "regex"; if (t == typeof(Version)) return "version"; if (t == typeof(System.Collections.Hashtable)) return "hashtable"; if (t == typeof(Type)) return "type";
            if (t.IsArray) return TypeShort(t.GetElementType()) + "[]";
            if (t.IsGenericType) return t.FullName == null ? t.Name : t.Name.Substring(0, t.Name.IndexOf('`')) + "[" + string.Join(",", t.GetGenericArguments().Select(TypeShort).ToArray()) + "]";
            return t.FullName ?? t.Name;
        }
        public static string Definition(MethodInfo m) { return TypeShort(m.ReturnType) + " " + m.Name + "(" + string.Join(", ", m.GetParameters().Select(p => (p.GetCustomAttributes(typeof(ParamArrayAttribute), false).Length > 0 ? "Params " : "") + TypeShort(p.ParameterType) + " " + p.Name).ToArray()) + ")"; }

        public static List<PsMemberInfo> ListMembers(object target)
        {
            target = PsConv.Base(target);
            var l = new List<PsMemberInfo>();
            if (target == null) return l;
            var o = target as PsObj;
            if (o != null)
            {
                foreach (var m in o.Methods.Values) l.Add(new PsMemberInfo { Name = m.Name, MemberType = m.Script != null ? "ScriptMethod" : "Method", Definition = m.Definition.Length > 0 ? m.Definition : m.Name + "()" });
                if (!o.Methods.ContainsKey("Equals")) l.Add(new PsMemberInfo { Name = "Equals", MemberType = "Method", Definition = "bool Equals(System.Object obj)" });
                if (!o.Methods.ContainsKey("GetHashCode")) l.Add(new PsMemberInfo { Name = "GetHashCode", MemberType = "Method", Definition = "int GetHashCode()" });
                if (!o.Methods.ContainsKey("GetType")) l.Add(new PsMemberInfo { Name = "GetType", MemberType = "Method", Definition = "type GetType()" });
                if (!o.Methods.ContainsKey("ToString")) l.Add(new PsMemberInfo { Name = "ToString", MemberType = "Method", Definition = "string ToString()" });
                foreach (var p in o.Props)
                {
                    string def;
                    var val = p.Kind == "ScriptProperty" || p.Script != null ? null : o.ValueOf(p);
                    string tn = p.Kind == "NoteProperty" ? TypeWord(val, p.TypeName) : TypeWordName(p.TypeName);
                    switch (p.Kind)
                    {
                        case "AliasProperty": def = p.Name + " = " + p.AliasOf; break;
                        case "ScriptProperty": def = "System.Object " + p.Name + " {get=" + (p.Script == null ? "" : p.Script.Text.Trim()) + ";}"; break;
                        case "NoteProperty": def = tn + " " + p.Name + "=" + NoteText(val); break;
                        default: def = tn + " " + p.Name + " {get;" + (p.ReadOnly && p.Setter == null ? "" : "set;") + "}"; break;
                    }
                    l.Add(new PsMemberInfo { Name = p.Name, MemberType = p.Script != null ? "ScriptProperty" : p.Kind, Definition = def });
                }
                return l.OrderBy(m => Rank(m.MemberType)).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
            }
            var notes = PsNotes.Get(target);
            if (notes != null) foreach (var p in notes.Props) l.Add(new PsMemberInfo { Name = p.Name, MemberType = p.Kind, Definition = TypeWord(notes.ValueOf(p), p.TypeName) + " " + p.Name + "=" + NoteText(notes.ValueOf(p)) });
            if (target is PsErrorRecord)
            {
                foreach (var n in new[] { "Equals|bool Equals(System.Object obj)", "GetHashCode|int GetHashCode()", "GetObjectData|void GetObjectData(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context), void ISerializable.GetObjectData(System.Runtime.Serialization.SerializationInfo info, System.Runtime.Serialization.StreamingContext context)", "GetType|type GetType()", "ToString|string ToString()" })
                { var p = n.Split('|'); l.Add(new PsMemberInfo { Name = p[0], MemberType = "Method", Definition = p[1] }); }
                foreach (var n in new[] { "CategoryInfo|System.Management.Automation.ErrorCategoryInfo CategoryInfo {get;}", "ErrorDetails|System.Management.Automation.ErrorDetails ErrorDetails {get;set;}", "Exception|System.Exception Exception {get;}", "FullyQualifiedErrorId|string FullyQualifiedErrorId {get;}", "InvocationInfo|System.Management.Automation.InvocationInfo InvocationInfo {get;}", "PipelineIterationInfo|System.Collections.ObjectModel.ReadOnlyCollection[int] PipelineIterationInfo {get;}", "ScriptStackTrace|string ScriptStackTrace {get;}", "TargetObject|System.Object TargetObject {get;}" })
                { var p = n.Split('|'); l.Add(new PsMemberInfo { Name = p[0], MemberType = "Property", Definition = p[1] }); }
                l.Add(new PsMemberInfo { Name = "PSMessageDetails", MemberType = "ScriptProperty", Definition = "System.Object PSMessageDetails {get=& { Set-StrictMode -Version 1; $this.Exception.InnerException.PSMessageDetails };}" });
                return l;
            }
            if (target is PsException)
            {
                foreach (var n in new[] { "Equals|bool Equals(System.Object obj)", "GetBaseException|System.Exception GetBaseException()", "GetHashCode|int GetHashCode()", "GetType|type GetType()", "ToString|string ToString()" }) { var p = n.Split('|'); l.Add(new PsMemberInfo { Name = p[0], MemberType = "Method", Definition = p[1] }); }
                foreach (var n in new[] { "Data|System.Collections.IDictionary Data {get;}", "HelpLink|string HelpLink {get;set;}", "HResult|int HResult {get;}", "InnerException|System.Exception InnerException {get;}", "Message|string Message {get;}", "Source|string Source {get;set;}", "StackTrace|string StackTrace {get;}", "TargetSite|System.Reflection.MethodBase TargetSite {get;}" }) { var p = n.Split('|'); l.Add(new PsMemberInfo { Name = p[0], MemberType = "Property", Definition = p[1] }); }
                return l;
            }
            if (target is PsEnum)
            {
                foreach (var n in new[] { "CompareTo|int CompareTo(System.Object target)", "Equals|bool Equals(System.Object obj)", "GetHashCode|int GetHashCode()", "GetType|type GetType()", "GetTypeCode|System.TypeCode GetTypeCode()", "HasFlag|bool HasFlag(System.Enum flag)", "ToString|string ToString(), string ToString(string format)" }) { var p = n.Split('|'); l.Add(new PsMemberInfo { Name = p[0], MemberType = "Method", Definition = p[1] }); }
                l.Add(new PsMemberInfo { Name = "value__", MemberType = "Property", Definition = "int value__ {get;set;}" });
                return l;
            }
            if (target is PsScriptBlock)
            {
                foreach (var n in new[] { "GetNewClosure|scriptblock GetNewClosure()", "Invoke|System.Collections.ObjectModel.Collection[psobject] Invoke(Params System.Object[] args)", "InvokeReturnAsIs|System.Object InvokeReturnAsIs(Params System.Object[] args)", "ToString|string ToString()" }) { var p = n.Split('|'); l.Add(new PsMemberInfo { Name = p[0], MemberType = "Method", Definition = p[1] }); }
                foreach (var n in new[] { "Ast|System.Management.Automation.Language.Ast Ast {get;}", "File|string File {get;}", "IsFilter|bool IsFilter {get;set;}", "Module|psmoduleinfo Module {get;}" }) { var p = n.Split('|'); l.Add(new PsMemberInfo { Name = p[0], MemberType = "Property", Definition = p[1] }); }
                return l;
            }
            if (target is PsSecureString)
            {
                foreach (var n in new[] { "AppendChar|void AppendChar(char c)", "Clear|void Clear()", "Copy|securestring Copy()", "Dispose|void Dispose(), void IDisposable.Dispose()", "GetType|type GetType()", "MakeReadOnly|void MakeReadOnly()", "ToString|string ToString()" }) { var p = n.Split('|'); l.Add(new PsMemberInfo { Name = p[0], MemberType = "Method", Definition = p[1] }); }
                l.Add(new PsMemberInfo { Name = "Length", MemberType = "Property", Definition = "int Length {get;}" });
                return l;
            }
            if (target is PsTypeInfo)
            {
                foreach (var n in new[] { "BaseType|type BaseType {get;}", "FullName|string FullName {get;}", "IsArray|bool IsArray {get;}", "IsEnum|bool IsEnum {get;}", "IsPublic|bool IsPublic {get;}", "IsSerializable|bool IsSerializable {get;}", "IsValueType|bool IsValueType {get;}", "Name|string Name {get;}", "Namespace|string Namespace {get;}" }) { var p = n.Split('|'); l.Add(new PsMemberInfo { Name = p[0], MemberType = "Property", Definition = p[1] }); }
                l.Add(new PsMemberInfo { Name = "GetEnumNames", MemberType = "Method", Definition = "string[] GetEnumNames()" });
                return l;
            }
            if (target is DictionaryEntry)
            {
                l.Add(new PsMemberInfo { Name = "Name", MemberType = "AliasProperty", Definition = "Name = Key" });
                foreach (var n in new[] { "Equals|bool Equals(System.Object obj)", "GetHashCode|int GetHashCode()", "GetType|type GetType()", "ToString|string ToString()" }) { var p = n.Split('|'); l.Add(new PsMemberInfo { Name = p[0], MemberType = "Method", Definition = p[1] }); }
                l.Add(new PsMemberInfo { Name = "Key", MemberType = "Property", Definition = "System.Object Key {get;set;}" });
                l.Add(new PsMemberInfo { Name = "Value", MemberType = "Property", Definition = "System.Object Value {get;set;}" });
                return l;
            }
            if (Allowed(target))
            {
                var t = target.GetType();
                foreach (var g in t.GetMethods(BindingFlags.Public | BindingFlags.Instance).Where(m => !m.IsSpecialName && NetFxSurface.Has(m)).GroupBy(m => m.Name).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
                    l.Add(new PsMemberInfo { Name = g.Key, MemberType = "Method", Definition = string.Join(", ", g.Select(Definition).ToArray()) });
                foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(NetFxSurface.Has).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
                {
                    bool param = p.GetIndexParameters().Length > 0;
                    l.Add(new PsMemberInfo { Name = p.Name, MemberType = param ? "ParameterizedProperty" : "Property", Definition = TypeShort(p.PropertyType) + " " + p.Name + (param ? "(" + string.Join(", ", p.GetIndexParameters().Select(x => TypeShort(x.ParameterType) + " " + x.Name).ToArray()) + ")" : "") + " {get;" + (p.CanWrite ? "set;" : "") + "}" });
                }
                if (target is DateTime)
                {
                    l.Add(new PsMemberInfo { Name = "DisplayHint", MemberType = "NoteProperty", Definition = "DisplayHintType DisplayHint=DateTime" });
                    l.Add(new PsMemberInfo { Name = "DateTime", MemberType = "ScriptProperty", Definition = "System.Object DateTime {get=if ((& { Set-StrictMode -Version 1; $this.DisplayHint }) -ieq  \"Date\")..." });
                }
                if (target is Array) l.Add(new PsMemberInfo { Name = "Count", MemberType = "AliasProperty", Definition = "Count = Length" });
                return l.OrderBy(m => Rank(m.MemberType)).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
            }
            return l;
        }
        static int Rank(string mt)
        {
            switch (mt) { case "AliasProperty": return 0; case "CodeProperty": return 1; case "Event": return 2; case "Method": return 3; case "ScriptMethod": return 4; case "NoteProperty": return 5; case "ParameterizedProperty": return 6; case "Property": return 7; case "PropertySet": return 8; case "ScriptProperty": return 9; }
            return 10;
        }
        static string TypeWordName(string tn)
        {
            if (string.IsNullOrEmpty(tn)) return "System.Object";
            switch (tn) { case "System.String": return "string"; case "System.Int32": return "int"; case "System.Boolean": return "bool"; case "System.Int64": return "long"; case "System.Double": return "double"; case "System.DateTime": return "datetime"; }
            return tn;
        }
        static string TypeWord(object v, string tn)
        {
            v = PsConv.Base(v);
            if (v == null) return string.IsNullOrEmpty(tn) || tn == "System.Object" ? "object" : TypeWordName(tn);
            if (v is string) return "string"; if (v is int) return "int"; if (v is bool) return "bool"; if (v is long) return "long"; if (v is double) return "double";
            if (v is DateTime) return "datetime"; if (v is object[]) return "Object[]"; if (v is Hashtable) return "hashtable";
            if (v is PsObj) return ((PsObj)v).IsCustom ? "System.Management.Automation.PSCustomObject" : ((PsObj)v).TypeName;
            return PsTypes.TypeNameOf(v);
        }
        static string NoteText(object v)
        {
            v = PsConv.Base(v);
            if (v == null) return "null";
            if (PsConv.IsCollection(v)) return string.Join(" ", PsConv.Items(v).Select(x => PsConv.ToStr(x)).ToArray());
            return PsConv.ToStr(v);
        }

        /// <summary>The properties of a value in order (what Format-List *, Select-Object * and ConvertTo-Json see).</summary>
        public static List<KeyValuePair<string, object>> Properties(object target)
        {
            target = PsConv.Base(target);
            var l = new List<KeyValuePair<string, object>>();
            if (target == null) return l;
            var o = target as PsObj;
            if (o != null)
            {
                foreach (var p in o.Props) l.Add(new KeyValuePair<string, object>(p.Name, SafeValue(o, p)));
                return l;
            }
            var notes = PsNotes.Get(target);
            if (notes != null) foreach (var p in notes.Props) l.Add(new KeyValuePair<string, object>(p.Name, notes.ValueOf(p)));
            if (target is PsErrorRecord)
            {
                var er = (PsErrorRecord)target;
                foreach (var n in new[] { "PSMessageDetails", "Exception", "TargetObject", "CategoryInfo", "FullyQualifiedErrorId", "ErrorDetails", "InvocationInfo", "ScriptStackTrace", "PipelineIterationInfo" })
                { bool f; l.Add(new KeyValuePair<string, object>(n, ErrorMember(null, er, n, out f))); }
                return l;
            }
            if (target is PsException)
            {
                var ex = (PsException)target;
                l.Add(new KeyValuePair<string, object>("Message", ex.Message));
                l.Add(new KeyValuePair<string, object>("Data", "{}"));
                l.Add(new KeyValuePair<string, object>("InnerException", ex.Inner));
                l.Add(new KeyValuePair<string, object>("TargetSite", null));
                l.Add(new KeyValuePair<string, object>("StackTrace", null));
                l.Add(new KeyValuePair<string, object>("HelpLink", null));
                l.Add(new KeyValuePair<string, object>("Source", null));
                l.Add(new KeyValuePair<string, object>("HResult", -2146233087));
                return l;
            }
            if (target is PsTypeInfo)
            {
                var ti = (PsTypeInfo)target;
                l.Add(new KeyValuePair<string, object>("IsPublic", true));
                l.Add(new KeyValuePair<string, object>("IsSerial", true));
                l.Add(new KeyValuePair<string, object>("Name", ti.Name));
                l.Add(new KeyValuePair<string, object>("BaseType", ti.BaseType));
                return l;
            }
            if (target is DictionaryEntry) { var e = (DictionaryEntry)target; l.Add(new KeyValuePair<string, object>("Name", e.Key)); l.Add(new KeyValuePair<string, object>("Key", e.Key)); l.Add(new KeyValuePair<string, object>("Value", e.Value)); return l; }
            if (target is PsEnum) { return l; }
            if (target is PsSecureString) { l.Add(new KeyValuePair<string, object>("Length", ((PsSecureString)target).Length)); return l; }
            if (target is PsScriptBlock) { var sb = (PsScriptBlock)target; l.Add(new KeyValuePair<string, object>("Attributes", new object[0])); l.Add(new KeyValuePair<string, object>("File", null)); l.Add(new KeyValuePair<string, object>("IsFilter", false)); l.Add(new KeyValuePair<string, object>("IsConfiguration", false)); l.Add(new KeyValuePair<string, object>("Module", null)); l.Add(new KeyValuePair<string, object>("StartPosition", null)); return l; }
            if (target is PsModule) return ((PsModule)target).Properties();
            if (target is DateTime)
            {
                var d = (DateTime)target;
                l.Add(new KeyValuePair<string, object>("DisplayHint", "DateTime"));
                l.Add(new KeyValuePair<string, object>("DateTime", d.ToString("dddd, MMMM d, yyyy h:mm:ss tt", PsConv.Us)));
                foreach (var n in new[] { "Date", "Day", "DayOfWeek", "DayOfYear", "Hour", "Kind", "Millisecond", "Minute", "Month", "Second", "Ticks", "TimeOfDay", "Year" })
                    l.Add(new KeyValuePair<string, object>(n, Wrap(typeof(DateTime).GetProperty(n).GetValue(d, null))));
                return l;
            }
            if (target is string) { l.Add(new KeyValuePair<string, object>("Length", ((string)target).Length)); return l; }
            if (PsConv.IsNumber(target) || target is bool || target is char) return l;
            if (target is IDictionary && !(target is PsObj))
            {
                var d = (IDictionary)target;
                l.Add(new KeyValuePair<string, object>("IsReadOnly", d.IsReadOnly));
                l.Add(new KeyValuePair<string, object>("IsFixedSize", d.IsFixedSize));
                l.Add(new KeyValuePair<string, object>("IsSynchronized", false));
                l.Add(new KeyValuePair<string, object>("Keys", d.Keys.Cast<object>().ToArray()));
                l.Add(new KeyValuePair<string, object>("Values", d.Values.Cast<object>().ToArray()));
                l.Add(new KeyValuePair<string, object>("SyncRoot", "System.Object"));
                l.Add(new KeyValuePair<string, object>("Count", d.Count));
                return l;
            }
            if (Allowed(target))
            {
                foreach (var p in target.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (p.GetIndexParameters().Length > 0) continue;
                    object v;
                    try { v = Wrap(p.GetValue(target, null)); } catch (Exception) { v = null; }
                    l.Add(new KeyValuePair<string, object>(p.Name, v));
                }
            }
            return l;
        }
        static object SafeValue(PsObj o, PsProp p)
        {
            try { return o.ValueOf(p); }
            catch (PsRuntimeException) { return null; }
        }
    }

    public class PsMethodError : Exception { public string ExceptionType; public PsMethodError(string m, string t) : base(m) { ExceptionType = t; } }

    /// <summary>Static members: [DateTime]::Now (the machines' clock), [Environment], [System.IO.Path / File / Directory],
    /// [Console], [Math], [string], [regex], [int]::Parse... and [type]::new().</summary>
    public static class PsStatics
    {
        public static object Get(PsRunspace R, PsType t, string name, out bool found)
        {
            found = true;
            string f = t.FullName, n = name.ToLowerInvariant();
            if (t.Enum != null)
            {
                var e = t.Enum.Of(name);
                if (e != null) return e;
                found = false; return null;
            }
            switch (f)
            {
                case "System.DateTime":
                    if (n == "now") return R.M.Now;
                    if (n == "today") return R.M.Now.Date;
                    if (n == "utcnow") return R.M.Now.AddHours(-R.M.UtcOffsetHours);
                    break;
                case "System.Environment":
                    switch (n)
                    {
                        case "machinename": return R.M.Name;
                        case "username": return R.User;
                        case "userdomainname": return R.M.DomainShort;
                        case "newline": return "\r\n";
                        case "processorcount": return R.M.Cpus;
                        case "is64bitoperatingsystem": case "is64bitprocess": return true;
                        case "currentdirectory": return R.ProcessDirectory;
                        case "systemdirectory": return "C:\\Windows\\system32";
                        case "osversion": return R.M.OsVersionObj();
                        case "version": return new Version(4, 0, 30319, 42000);
                        case "tickcount": return (int)((R.M.Now - R.M.BootTime).TotalMilliseconds);
                        case "userinteractive": return true;
                        case "commandline": return "\"C:\\Windows\\System32\\WindowsPowerShell\\v1.0\\powershell.exe\"";
                    }
                    break;
                case "System.IO.Path":
                    switch (n)
                    {
                        case "directoryseparatorchar": return '\\';
                        case "altdirectoryseparatorchar": return '/';
                        case "pathseparator": return ';';
                        case "volumeseparatorchar": return ':';
                        case "invalidpathchars": return new[] { '"', '<', '>', '|' };
                    }
                    break;
                case "System.Console":
                    switch (n) { case "title": return "Administrator: Windows PowerShell"; case "windowwidth": case "bufferwidth": return R.Width; case "windowheight": return 30; case "foregroundcolor": return PsEnums.V("System.ConsoleColor", "Gray"); case "backgroundcolor": return PsEnums.V("System.ConsoleColor", "DarkBlue"); case "keyavailable": return false; }
                    break;
                case "System.Management.Automation.PSObject": break;
                case "System.String": if (n == "empty") return ""; break;
            }
            if (t.Clr != null && PsTypes.IsAllowedClr(t.Clr))
            {
                var pi = t.Clr.GetProperty(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);
                if (pi != null) return PsMembers.Wrap(pi.GetValue(null, null));
                var fi = t.Clr.GetField(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);
                if (fi != null) return PsMembers.Wrap(fi.GetValue(null));
            }
            if (t.Clr == null && !Simulated(f)) throw NotAllowed(R, t);
            found = false; return null;
        }

        static bool Simulated(string f)
        {
            return f == "System.Environment" || f == "System.IO.Path" || f == "System.IO.File" || f == "System.IO.Directory" || f == "System.Console" || f == "System.Management.Automation.PSObject" || f == "System.Management.Automation.PSCredential"
                || f == "System.Security.SecureString" || f == "System.Management.Automation.ScriptBlock" || f == "System.Net.IPAddress" || f == "System.Net.Dns" || f == "System.Diagnostics.Stopwatch" || PsException.IsExceptionType(f) || f == "System.Management.Automation.PSCustomObject"
                || f == "System.Management.Automation.WildcardPattern" || f == "System.Type" || f == "System.Management.Automation.SwitchParameter";
        }

        public static PsRuntimeException NotAllowed(PsRunspace R, PsType t)
        {
            return R.RtError(null, "Cert Lab's practice PowerShell does not let scripts use the .NET type [" + t.FullName + "]: it would reach the real computer. Use PowerShell's cmdlets for this instead.", "CertLabTypeNotAllowed", "PermissionDenied", "System.Management.Automation.PSSecurityException", t.FullName);
        }

        public static object Call(PsRunspace R, PsType t, string name, object[] args, PsAst at, PsScope sc, out bool found)
        {
            found = true;
            string f = t.FullName, n = name.ToLowerInvariant();
            if (n == "new") return New(R, t, args, at);
            switch (f)
            {
                case "System.DateTime":
                    if (n == "now" || n == "today") break;
                    break;
                case "System.Environment":
                    switch (n)
                    {
                        case "getenvironmentvariable":
                            {
                                string target = args.Length > 1 ? PsConv.ToStr(args[1]) : "Process";
                                return R.EnvFor(target, PsConv.ToStr(args[0]));
                            }
                        case "setenvironmentvariable":
                            {
                                string target = args.Length > 2 ? PsConv.ToStr(args[2]) : "Process";
                                R.SetEnvFor(target, PsConv.ToStr(args[0]), args.Length > 1 && args[1] != null && PsConv.ToStr(args[1]).Length > 0 ? PsConv.ToStr(args[1]) : null);
                                return AutoNull.Value;
                            }
                        case "getenvironmentvariables":
                            {
                                var h = PsConv.NewHashtable();
                                foreach (var kv in R.EnvAll(args.Length > 0 ? PsConv.ToStr(args[0]) : "Process")) h[kv.Key] = kv.Value;
                                return h;
                            }
                        case "getfolderpath":
                            {
                                string which = PsConv.ToStr(args[0]).ToLowerInvariant();
                                switch (which)
                                {
                                    case "desktop": case "desktopdirectory": return R.Home + "\\Desktop";
                                    case "mydocuments": case "personal": return R.Home + "\\Documents";
                                    case "programfiles": return "C:\\Program Files";
                                    case "programfilesx86": return "C:\\Program Files (x86)";
                                    case "system": return "C:\\Windows\\system32";
                                    case "windows": return "C:\\Windows";
                                    case "applicationdata": return R.Home + "\\AppData\\Roaming";
                                    case "localapplicationdata": return R.Home + "\\AppData\\Local";
                                    case "commonapplicationdata": return "C:\\ProgramData";
                                    case "userprofile": return R.Home;
                                }
                                return "";
                            }
                        case "getlogicaldrives": return new object[] { "C:\\" };
                        case "exit": throw R.RtError(at, "Cert Lab does not let a script end the practice console's process. Use exit instead.", "NotSimulated", "PermissionDenied", null, null);
                        case "expandenvironmentvariables": return R.ExpandEnv(PsConv.ToStr(args[0]));
                    }
                    break;
                case "System.IO.Path": return PathCall(R, n, args, at, out found);
                case "System.IO.File": case "System.IO.Directory": return R.FileStatic(f, n, args, at, out found);
                case "System.Console":
                    switch (n)
                    {
                        case "writeline": R.HostWrite((args.Length == 0 ? "" : args.Length > 1 ? string.Format(PsConv.Us, PsConv.ToStr(args[0]), args.Skip(1).Select(x => PsConv.Base(x) is PsObj ? PsConv.ToStr(x) : PsConv.FwNum(PsConv.Base(x))).ToArray()) : PsConv.ToStr(args[0])) + "\n"); return AutoNull.Value;
                        case "write": R.HostWrite(PsConv.ToStr(args.Length > 0 ? args[0] : "")); return AutoNull.Value;
                        case "readline": return R.Host.ReadLine("", false) ?? "";
                        case "clear": R.ClearHost(); return AutoNull.Value;
                        case "beep": case "resetcolor": return AutoNull.Value;
                        case "readkey": return AutoNull.Value;
                    }
                    break;
                case "System.Guid":
                    if (n == "newguid") return R.M.NewGuid();
                    break;
                case "System.Net.Dns": return PsNetHook.DnsStatic(R, n, args, at, out found);
                case "System.Diagnostics.Stopwatch":
                    if (n == "startnew") return R.NewStopwatch();
                    break;
                case "System.Management.Automation.WildcardPattern":
                    if (n == "escape") return Regex.Replace(PsConv.ToStr(args[0]), "([\\*\\?\\[\\]`])", "`$1");
                    break;
                case "System.String":
                    if (n == "isnullorempty") return args.Length > 0 && (args[0] == null || PsConv.ToStr(args[0]).Length == 0);
                    if (n == "isnullorwhitespace") return args.Length > 0 && (args[0] == null || PsConv.ToStr(args[0]).Trim().Length == 0);
                    break;
                case "System.Enum":
                    if (n == "getnames" || n == "getvalues")
                    {
                        var ti = PsConv.Base(args[0]) as PsTypeInfo;
                        var e = ti == null ? null : PsEnums.Get(ti.FullName);
                        if (e == null) throw R.RtError(at, "Type provided must be an Enum.", "ArgumentException", "NotSpecified", "System.ArgumentException", null);
                        return n == "getnames" ? e.Values.Select(v => (object)v.Key).ToArray() : e.Values.Select(v => (object)e.Of(v.Value)).ToArray();
                    }
                    if (n == "parse") { var ti = PsConv.Base(args[0]) as PsTypeInfo; var e = ti == null ? null : PsEnums.Get(ti.FullName); if (e != null) { var r = e.Parse(PsConv.ToStr(args[1])); if (r != null) return r; } }
                    break;
            }
            if (t.Enum != null) { found = false; return null; }
            if (t.Clr != null && PsTypes.IsAllowedClr(t.Clr))
            {
                if (t.Clr == typeof(Random)) { }
                return PsMembers.ClrCall(R, t.Clr, null, name, args, at, out found);
            }
            if (!Simulated(f)) throw NotAllowed(R, t);
            found = false; return null;
        }

        static object PathCall(PsRunspace R, string n, object[] args, PsAst at, out bool found)
        {
            found = true;
            string a0 = args.Length > 0 ? PsConv.ToStr(args[0]) : "";
            switch (n)
            {
                case "combine":
                    {
                        string res = "";
                        foreach (var a in args.SelectMany(x => PsConv.Items(x)))
                        {
                            string p = PsConv.ToStr(a);
                            if (p.Length == 0) continue;
                            if (p.Length >= 2 && p[1] == ':' || p.StartsWith("\\", StringComparison.Ordinal)) res = p;
                            else res = res.Length == 0 ? p : res.TrimEnd('\\') + "\\" + p;
                        }
                        return res;
                    }
                case "getfilename": { int i = a0.LastIndexOfAny(new[] { '\\', '/' }); return i < 0 ? a0 : a0.Substring(i + 1); }
                case "getfilenamewithoutextension": { int i = a0.LastIndexOfAny(new[] { '\\', '/' }); string fn = i < 0 ? a0 : a0.Substring(i + 1); int d = fn.LastIndexOf('.'); return d <= 0 ? fn : fn.Substring(0, d); }
                case "getextension": { int i = a0.LastIndexOfAny(new[] { '\\', '/' }); string fn = i < 0 ? a0 : a0.Substring(i + 1); int d = fn.LastIndexOf('.'); return d < 0 ? "" : fn.Substring(d); }
                case "getdirectoryname": { int i = a0.LastIndexOfAny(new[] { '\\', '/' }); if (i < 0) return ""; if (i == 2 && a0[1] == ':') return a0.Length == 3 ? null : a0.Substring(0, 3); return a0.Substring(0, i); }
                case "changeextension": { string ext = args.Length > 1 ? PsConv.ToStr(args[1]) : ""; int i = a0.LastIndexOfAny(new[] { '\\', '/' }); int d = a0.LastIndexOf('.'); string b = d > i ? a0.Substring(0, d) : a0; return ext.Length == 0 ? b : b + (ext.StartsWith(".", StringComparison.Ordinal) ? ext : "." + ext); }
                case "ispathrooted": return a0.Length >= 2 && a0[1] == ':' || a0.StartsWith("\\", StringComparison.Ordinal) || a0.StartsWith("/", StringComparison.Ordinal);
                case "getpathroot": return a0.Length >= 2 && a0[1] == ':' ? a0.Substring(0, Math.Min(3, a0.Length)) : a0.StartsWith("\\", StringComparison.Ordinal) ? "\\" : "";
                case "gettemppath": return R.Home + "\\AppData\\Local\\Temp\\";
                case "getfullpath": return R.FullFsPath(a0, R.ProcessDirectory);
                case "getrandomfilename": return R.M.RandomName(8) + "." + R.M.RandomName(3);
                case "gettempfilename":
                    {
                        string p = R.Home + "\\AppData\\Local\\Temp\\tmp" + R.M.RandomHex(4).ToUpperInvariant() + ".tmp";
                        R.FileStatic("System.IO.File", "writealltext", new object[] { p, "" }, at, out found);
                        found = true;
                        return p;
                    }
                case "getinvalidfilenamechars": return new object[] { '"', '<', '>', '|', ':', '*', '?', '\\', '/' };
            }
            found = false; return null;
        }

        /// <summary>Types other parts build (the AZ-800 course's FileSystemAccessRule): full name, arguments -> object or null.</summary>
        public static Func<PsRunspace, string, object[], object> NewHook;

        /// <summary>[type]::new(...) and New-Object TYPE args.</summary>
        public static object New(PsRunspace R, PsType t, object[] args, PsAst at)
        {
            string f = t.FullName;
            for (int i = 0; i < args.Length; i++) if (args[i] is AutoNull) args[i] = null;
            if (f == "System.Management.Automation.PSObject" || f == "System.Object" || f == "System.Management.Automation.PSCustomObject")
            {
                if (args.Length == 0) return PsObj.Custom();
                return args[0];
            }
            if (f == "System.Management.Automation.PSCredential")
            {
                if (args.Length != 2) throw R.RtError(at, "Cannot find an overload for \"PSCredential\" and the argument count: \"" + args.Length + "\".", "ConstructorInvokedThrowException", "InvalidOperation", "System.Management.Automation.MethodException", null);
                var ss = PsConv.Base(args[1]) as PsSecureString;
                if (ss == null) throw R.RtError(at, "Cannot convert argument \"password\", with value: \"" + PsConv.ToStr(args[1]) + "\", for \"PSCredential\" to type \"System.Security.SecureString\": \"Cannot convert the \"" + PsConv.ToStr(args[1]) + "\" value of type \"System.String\" to type \"System.Security.SecureString\".\"", "ConstructorInvokedThrowException", "InvalidOperation", "System.Management.Automation.MethodException", null);
                return PsCred.Make(PsConv.ToStr(args[0]), ss);
            }
            if (f == "System.Security.SecureString") return new PsSecureString();
            if (PsException.IsExceptionType(f) && t.Clr == null)
            {
                var e = new PsException { TypeFullName = f, Message = args.Length > 0 ? PsConv.ToStr(args[0]) : DefaultExMessage(f) };
                if (args.Length > 1 && PsConv.Base(args[1]) is PsException) e.Inner = (PsException)PsConv.Base(args[1]);
                return e;
            }
            if (f == "System.Random") return args.Length > 0 ? new Random(PsConv.ToInt(args[0])) : new Random(R.M.Rng.Next());
            if (f == "System.Diagnostics.Stopwatch") return R.NewStopwatch();
            if (f == "System.Net.IPAddress" && args.Length == 1) return PsConv.To(args[0], t);
            if (t.Clr != null && PsTypes.IsAllowedClr(t.Clr) && !t.Clr.IsAbstract) return PsMembers.ClrNew(R, t.Clr, args, at);
            if (NewHook != null) { var made = NewHook(R, f, args); if (made != null) return made; }
            if (t.Enum != null) throw R.RtError(at, "Cannot create an instance of the enum type " + f + ".", "ConstructorInvokedThrowException", "InvalidOperation", null, null);
            throw NotAllowed(R, t);
        }
        static string DefaultExMessage(string f)
        {
            if (f == "System.Exception") return "Exception of type 'System.Exception' was thrown.";
            return "Exception of type '" + f + "' was thrown.";
        }
    }

    public static class PsCred
    {
        public static PsObj Make(string user, PsSecureString pw)
        {
            var c = new PsObj("System.Management.Automation.PSCredential", "System.Object");
            c.Val("UserName", "string", user);
            c.Val("Password", "securestring", pw);
            c.Method("GetNetworkCredential", "System.Net.NetworkCredential GetNetworkCredential()", (self, a) =>
            {
                var n = new PsObj("System.Net.NetworkCredential", "System.Object");
                string u = user, dom = "";
                int bs = user.IndexOf('\\');
                if (bs > 0) { dom = user.Substring(0, bs); u = user.Substring(bs + 1); }
                n.Val("UserName", "string", u);
                n.Val("Password", "string", pw.Plain);
                n.Val("SecurePassword", "securestring", pw);
                n.Val("Domain", "string", dom);
                n.DefaultDisplay = new[] { "UserName", "Domain" };
                return n;
            });
            c.DefaultDisplay = new[] { "UserName", "Password" };
            c.Base = pw;
            return c;
        }
    }

    /// <summary>Hooks the networking part fills in ([System.Net.Dns]).</summary>
    public static class PsNetHook
    {
        public static Func<PsRunspace, string, object[], PsAst, object> Dns;
        public static object DnsStatic(PsRunspace R, string n, object[] args, PsAst at, out bool found)
        {
            found = true;
            if (Dns != null) { var r = Dns(R, n, args, at); if (r != null) return r; }
            found = false; return null;
        }
    }
}
