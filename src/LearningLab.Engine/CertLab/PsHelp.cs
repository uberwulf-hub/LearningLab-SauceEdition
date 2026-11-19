// Cert Lab - help in the simulated PowerShell: help pages written for Cert Lab (Courses\powershell\help\NAME.txt and
// about_*.txt), the SYNTAX section built from each cmdlet's parameters, comment-based help of functions, and the
// command list behind Get-Command.
//
// Help file format (our own):
//   SYNOPSIS: one line
//   DESCRIPTION:            (paragraphs until the next heading)
//   PARAMETER Name:         (text for one parameter)
//   EXAMPLE: the command    (then the explanation)
//   NOTES:
//   RELATED: Cmd-One, Cmd-Two
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CertLab
{
    public class HelpPage
    {
        public string Name = "", Synopsis = "", Description = "", Notes = "";
        public readonly List<KeyValuePair<string, string>> Parameters = new List<KeyValuePair<string, string>>();
        public readonly List<KeyValuePair<string, string>> Examples = new List<KeyValuePair<string, string>>();
        public readonly List<string> Related = new List<string>();
    }

    public static class PsHelp
    {
        public static string Dir;
        static Dictionary<string, string> files;
        static readonly object gate = new object();

        static Dictionary<string, string> Files
        {
            get
            {
                lock (gate)
                {
                    if (files != null) return files;
                    files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    if (Dir != null && Directory.Exists(Dir))
                        foreach (var f in Directory.GetFiles(Dir, "*.txt")) files[Path.GetFileNameWithoutExtension(f)] = File.ReadAllText(f).Replace("\r\n", "\n");
                    return files;
                }
            }
        }
        public static void Reload() { lock (gate) files = null; }
        public static IEnumerable<string> Names { get { return Files.Keys; } }
        public static string Raw(string name) { string t; return Files.TryGetValue(name, out t) ? t : null; }

        public static HelpPage Page(string name)
        {
            var t = Raw(name);
            if (t == null) return null;
            var p = new HelpPage { Name = name };
            string section = null, key = null; var sb = new StringBuilder();
            Action flush = () =>
            {
                string body = sb.ToString().Trim('\n'); sb.Clear();
                if (section == null) return;
                switch (section)
                {
                    case "DESCRIPTION": p.Description = body; break;
                    case "NOTES": p.Notes = body; break;
                    case "PARAMETER": p.Parameters.Add(new KeyValuePair<string, string>(key, body)); break;
                    case "EXAMPLE": p.Examples.Add(new KeyValuePair<string, string>(key, body)); break;
                }
            };
            foreach (var line in t.Split('\n'))
            {
                var m = Regex.Match(line, "^(SYNOPSIS|DESCRIPTION|PARAMETER|EXAMPLE|NOTES|RELATED)( [^:]+)?:\\s?(.*)$");
                if (m.Success)
                {
                    flush();
                    section = m.Groups[1].Value; key = m.Groups[2].Value.Trim();
                    string rest = m.Groups[3].Value;
                    if (section == "SYNOPSIS") { p.Synopsis = rest.Trim(); section = null; continue; }
                    if (section == "RELATED") { p.Related.AddRange(rest.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0)); section = null; continue; }
                    if (section == "EXAMPLE") key = rest.Trim();
                    else if (rest.Trim().Length > 0) sb.Append(rest.Trim()).Append('\n');
                    continue;
                }
                if (section != null) sb.Append(line).Append('\n');
            }
            flush();
            return p;
        }

        static string TypeWord(ParamDef p)
        {
            string t = p.Type; bool arr = t.EndsWith("[]", StringComparison.Ordinal); if (arr) t = t.Substring(0, t.Length - 2);
            string lower = t.ToLowerInvariant();
            string w;
            switch (lower)
            {
                case "string": case "int": case "long": case "double": case "bool": case "scriptblock": case "hashtable": case "datetime": case "timespan": case "securestring": case "pscredential": case "char": case "byte": case "version": case "object": case "psobject": case "uint32": case "decimal":
                    w = lower == "psobject" ? "psobject" : lower == "object" ? "Object" : lower; break;
                default: { var f = PsCmdlets.TypeFull(t); int i = f.LastIndexOf('.'); w = i < 0 ? f : f.Substring(i + 1); int h = w.IndexOf('#'); if (h >= 0) w = w.Substring(h + 1); break; }
            }
            return "<" + w + (arr ? "[]" : "") + ">";
        }

        public static List<string> Syntax(CmdletDef d)
        {
            var sets = d.Params.Where(p => p.Sets != null).SelectMany(p => p.Sets).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (sets.Count == 0) sets.Add(null);
            var lines = new List<string>();
            foreach (var s in sets)
            {
                var ps = d.Params.Where(p => p.InSet(s)).ToList();
                var ordered = ps.Where(p => p.Position >= 0).OrderBy(p => p.Position).Concat(ps.Where(p => p.Position < 0 && p.Mandatory)).Concat(ps.Where(p => p.Position < 0 && !p.Mandatory)).ToList();
                var sb = new StringBuilder(d.Name);
                foreach (var p in ordered)
                {
                    sb.Append(' ');
                    if (p.IsSwitch) { sb.Append(p.Mandatory ? "-" + p.Name : "[-" + p.Name + "]"); continue; }
                    string name = p.Position >= 0 ? "[-" + p.Name + "]" : "-" + p.Name;
                    string part = name + " " + TypeWord(p);
                    sb.Append(p.Mandatory ? part : "[" + part + "]");
                }
                if (d.ShouldProcess) sb.Append(" [-WhatIf] [-Confirm]");
                sb.Append(" [<CommonParameters>]");
                lines.Add(sb.ToString());
            }
            return lines;
        }

        static void Para(StringBuilder sb, string text, int indent)
        {
            string pad = new string(' ', indent);
            foreach (var l in text.Split('\n')) sb.Append(l.Trim().Length == 0 ? "" : pad + l.TrimEnd()).Append('\n');
        }

        /// <summary>Get-Help NAME [-Examples | -Detailed | -Full | -Parameter X] as text.</summary>
        public static string Render(PsRunspace R, string name, string mode, string param)
        {
            var about = Raw(name);
            if (name.StartsWith("about_", StringComparison.OrdinalIgnoreCase) && about != null) return about.TrimEnd('\n') + "\n";
            var ci = R.Resolve(name, R.Global, false);
            CmdletDef d = ci == null ? null : ci.Def;
            PsFunction f = ci == null ? null : ci.Func;
            string realName = ci == null ? name : (ci.Def != null ? ci.Def.Name : ci.Func != null ? ci.Func.Name : ci.Name);
            var page = Page(realName);
            if (page == null && f != null && f.Help != null) page = FromComment(realName, f.Help, f.Ast);
            if (page == null && ci != null && ci.Kind == "ExternalScript")
            {
                try
                {
                    var sast = PsParser.Parse(R.ReadFileText(ci.Path) ?? "", ci.Path);
                    realName = ci.Path;
                    if (sast.Help != null) page = FromComment(ci.Path, sast.Help, sast);
                    f = new PsFunction { Name = ci.Path, Ast = sast };
                }
                catch (PsParseError) { }
            }
            var sb = new StringBuilder();
            List<string> syntax;
            if (d != null) syntax = Syntax(d);
            else if (f != null) syntax = new List<string> { FunctionSyntax(f) };
            else syntax = new List<string>();
            if (param != null)
            {
                var ps = d != null ? d.Params : (f != null ? PsScriptParams.Defs(f.Ast, R, R.Global) : new List<ParamDef>());
                foreach (var p in ps.Where(x => PsConv.Like(x.Name, param, false)))
                {
                    sb.Append("\n-").Append(p.Name).Append(" ").Append(p.IsSwitch ? "[<SwitchParameter>]" : TypeWord(p)).Append('\n');
                    var txt = page == null ? null : page.Parameters.FirstOrDefault(x => x.Key.Equals(p.Name, StringComparison.OrdinalIgnoreCase)).Value;
                    if (txt != null) Para(sb, txt, 4);
                    ParamFacts(sb, p, 4, txt);
                }
                return sb.ToString();
            }
            sb.Append("\nNAME\n    ").Append(realName).Append("\n\n");
            sb.Append("SYNOPSIS\n");
            if (page != null && page.Synopsis.Length > 0) sb.Append("    ").Append(page.Synopsis).Append("\n");
            else sb.Append("    (Cert Lab has no help page for this command yet.)\n");
            if (mode == "examples")
            {
                int n = 0;
                if (page != null)
                    foreach (var ex in page.Examples)
                    {
                        n++;
                        sb.Append("\n    -------------------------- EXAMPLE ").Append(n).Append(" --------------------------\n\n");
                        sb.Append("    PS C:\\>").Append(ex.Key).Append("\n\n");
                        Para(sb, ex.Value, 4);
                    }
                if (n == 0) sb.Append("\n    (No examples for this command.)\n");
                return sb.ToString();
            }
            sb.Append("\n\nSYNTAX\n");
            foreach (var s in syntax) sb.Append("    ").Append(s).Append("\n\n");
            sb.Append("\nDESCRIPTION\n");
            if (page != null && page.Description.Length > 0) Para(sb, page.Description, 4);
            if (mode == "detailed" || mode == "full")
            {
                sb.Append("\n\nPARAMETERS\n");
                var ps = d != null ? d.Params : (f != null ? PsScriptParams.Defs(f.Ast, R, R.Global) : new List<ParamDef>());
                foreach (var p in ps)
                {
                    sb.Append("    -").Append(p.Name).Append(" ").Append(p.IsSwitch ? "[<SwitchParameter>]" : TypeWord(p)).Append('\n');
                    var txt = page == null ? null : page.Parameters.FirstOrDefault(x => x.Key.Equals(p.Name, StringComparison.OrdinalIgnoreCase)).Value;
                    if (txt != null) Para(sb, txt, 8);
                    if (mode == "full") ParamFacts(sb, p, 8, txt);
                    sb.Append('\n');
                }
                sb.Append("    <CommonParameters>\n        This cmdlet supports the common parameters: Verbose, Debug,\n        ErrorAction, ErrorVariable, WarningAction, WarningVariable,\n        OutBuffer, PipelineVariable, and OutVariable. For more information, see\n        about_CommonParameters.\n\n");
                if (page != null && page.Examples.Count > 0)
                {
                    int n = 0;
                    foreach (var ex in page.Examples) { n++; sb.Append("    -------------------------- EXAMPLE ").Append(n).Append(" --------------------------\n\n    PS C:\\>").Append(ex.Key).Append("\n\n"); Para(sb, ex.Value, 4); sb.Append('\n'); }
                }
                if (page != null && page.Notes.Length > 0) { sb.Append("NOTES\n"); Para(sb, page.Notes, 4); }
            }
            sb.Append("\n\nRELATED LINKS\n");
            if (page != null) foreach (var r in page.Related) sb.Append("    ").Append(r).Append('\n');
            sb.Append("\nREMARKS\n");
            sb.Append("    To see the examples, type: \"get-help ").Append(realName).Append(" -examples\".\n");
            sb.Append("    For more information, type: \"get-help ").Append(realName).Append(" -detailed\".\n");
            sb.Append("    For technical information, type: \"get-help ").Append(realName).Append(" -full\".\n");
            return sb.ToString();
        }

        static void ParamFacts(StringBuilder sb, ParamDef p, int indent, string text)
        {
            string pad = new string(' ', indent);
            sb.Append("\n" + pad + "Required?                    ").Append(p.Mandatory ? "true" : "false").Append('\n');
            sb.Append(pad + "Position?                    ").Append(p.Position >= 0 ? (p.Position).ToString() : "named").Append('\n');
            sb.Append(pad + "Default value                \n");
            sb.Append(pad + "Accept pipeline input?       ").Append(p.Pipe && p.PipeProp ? "True (ByPropertyName, ByValue)" : p.Pipe ? "True (ByValue)" : p.PipeProp ? "True (ByPropertyName)" : "False").Append('\n');
            // our help pages say so in the parameter text ("Wildcards are allowed")
            bool wild = text != null && Regex.IsMatch(text, "(?i)wildcards? (are |is )?(allowed|accepted|work)") && !Regex.IsMatch(text, "(?i)(no|not|without) wildcards");
            sb.Append(pad + "Accept wildcard characters?  ").Append(wild ? "true" : "false").Append('\n');
            if (p.Aliases.Count > 0) sb.Append(pad + "Aliases                      ").Append(string.Join(", ", p.Aliases.ToArray())).Append('\n');
        }

        public static string FunctionSyntax(PsFunction f)
        {
            if (f.Ast.Params != null && f.Ast.Advanced)
            {
                try
                {
                    var tmpNet = PsNet.Create(1, 1);
                    var r = new PsRunspace(tmpNet.Machines[0], new NullHost(), "Administrator");
                    var d = new CmdletDef { Name = f.Name, Params = PsScriptParams.Defs(f.Ast, r, r.Global) };
                    foreach (var p in d.Params) if (p.Type.StartsWith("System.", StringComparison.Ordinal)) { var full = p.Type; bool arr = full.EndsWith("[]", StringComparison.Ordinal); if (arr) full = full.Substring(0, full.Length - 2); var w = full.Substring(full.LastIndexOf('.') + 1).ToLowerInvariant(); if (w == "int32") w = "int"; if (w == "boolean") w = "bool"; p.Type = w + (arr ? "[]" : ""); }
                    d.ShouldProcess = f.Ast.BlockAttrs.Any(a => a.Named.Any(n => n.Key.Equals("SupportsShouldProcess", StringComparison.OrdinalIgnoreCase)));
                    return Syntax(d)[0];
                }
                catch (Exception) { }
            }
            var sb = new StringBuilder(f.Name);
            if (f.Ast.Params != null)
                foreach (var p in f.Ast.Params)
                {
                    bool mand = p.Attrs.Any(a => a.Name.Equals("Parameter", StringComparison.OrdinalIgnoreCase) && a.Named.Any(n => n.Key.Equals("Mandatory", StringComparison.OrdinalIgnoreCase)));
                    string ty = p.Type == null ? "Object" : p.Type;
                    bool sw = ty.Equals("switch", StringComparison.OrdinalIgnoreCase);
                    string part = sw ? "-" + p.Name : "-" + p.Name + " <" + ty + ">";
                    sb.Append(' ').Append(mand && !sw ? part : "[" + part + "]");
                }
            if (f.Ast.Advanced) sb.Append(" [<CommonParameters>]");
            return sb.ToString();
        }

        /// <summary>Comment-based help (.SYNOPSIS, .DESCRIPTION, .PARAMETER x, .EXAMPLE, .NOTES).</summary>
        public static HelpPage FromComment(string name, string text, ScriptBlockAst ast)
        {
            var p = new HelpPage { Name = name };
            string key = null, arg = null; var sb = new StringBuilder();
            Action flush = () =>
            {
                string body = string.Join("\n", sb.ToString().Split('\n').Select(l => l.Trim()).ToArray()).Trim('\n'); sb.Clear();
                if (key == null) return;
                switch (key)
                {
                    case "SYNOPSIS": p.Synopsis = body.Replace("\n", " "); break;
                    case "DESCRIPTION": p.Description = body; break;
                    case "PARAMETER": p.Parameters.Add(new KeyValuePair<string, string>(arg, body)); break;
                    case "EXAMPLE": { var lines = body.Split('\n'); p.Examples.Add(new KeyValuePair<string, string>(lines[0].Replace("PS C:\\>", "").Trim(), string.Join("\n", lines.Skip(1).ToArray()))); break; }
                    case "NOTES": p.Notes = body; break;
                    case "LINK": p.Related.Add(body); break;
                }
            };
            foreach (var raw in text.Replace("\r", "").Split('\n'))
            {
                var m = Regex.Match(raw.Trim(), "^\\.(SYNOPSIS|DESCRIPTION|PARAMETER|EXAMPLE|NOTES|LINK|INPUTS|OUTPUTS)\\s*(.*)$", RegexOptions.IgnoreCase);
                if (m.Success) { flush(); key = m.Groups[1].Value.ToUpperInvariant(); arg = m.Groups[2].Value.Trim(); continue; }
                sb.Append(raw).Append('\n');
            }
            flush();
            return p;
        }
    }
}
