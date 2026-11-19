// Cert Lab - Format-Table, Format-List, Format-Wide and the Out-* cmdlets of the simulated PowerShell. Format-*
// output format objects (only Out-* understand them, as in real PowerShell); Out-File and > write UTF-16 in 5.1.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CertLab
{
    public partial class PsCmdlets
    {
        const string FormatClass = "Microsoft.PowerShell.Commands.Internal.Format.";

        static PsObj FormatRecord(string cls, PsFormatPayload payload)
        {
            var o = new PsObj(FormatClass + cls, "System.Object");
            o.Val("ClassId2e4f51ef21dd47e99d3c952918aff9cd", "string", cls == "FormatStartData" ? "033ecb2bc07a4d43b5ef94ed5a35d280" : cls == "FormatEntryData" ? "27c87ef9bbda4f709f6b4002fa4af63c" : "cf522b78d86c486691226b40aa69e95c");
            o.Base = payload;
            o.ToStr = x => FormatClass + cls;
            return o;
        }

        static void FormatEnd(CmdCtx c, string kind)
        {
            var opt = new FmtOptions
            {
                Props = c.Has("Property") ? c.Objs("Property") : null,
                AutoSize = c.Sw("AutoSize"), HideHeaders = c.Sw("HideTableHeaders"), Wrap = c.Sw("Wrap"), Force = c.Sw("Force"),
                GroupBy = c.Str("GroupBy"), Columns = c.Int("Column", 2)
            };
            var lines = PsRender.Render(c.R, c.Scope, c.Items, kind, opt, c.R.Width);
            if (lines.Count == 0) return;
            c.Out(FormatRecord("FormatStartData", new PsFormatPayload { Lines = lines }));
            foreach (var x in c.Items) c.Out(FormatRecord("FormatEntryData", null));
            c.Out(FormatRecord("FormatEndData", null));
        }

        static List<object> Unformat(List<object> items) { return items; }

        static string EncodingName(string e)
        {
            switch ((e ?? "").ToLowerInvariant())
            {
                case "": case "unicode": return "unicode";
                case "utf8": return "utf8bom";
                case "ascii": case "default": case "oem": case "string": return "ascii";
                case "bigendianunicode": return "bigendianunicode";
                case "utf32": return "utf32";
                case "utf7": return "ascii";
            }
            return "unicode";
        }

        static partial void RegisterFormat()
        {
            string fmtSpec = "Property:object[]@0; GroupBy:string; InputObject:psobject pipe; Force:switch; Expand:string; View:string; DisplayError:switch; ShowError:switch";
            Add("Format-Table", Util, fmtSpec + "; AutoSize:switch alias=a; HideTableHeaders:switch; Wrap:switch", (c, o) => { if (c.HasInput) c.Items.Add(o); else if (c.Has("InputObject")) c.Items.Add(c.Get("InputObject")); }).End = c => FormatEnd(c, "table");
            Add("Format-List", Util, fmtSpec, (c, o) => { if (c.HasInput) c.Items.Add(o); else if (c.Has("InputObject")) c.Items.Add(c.Get("InputObject")); }).End = c => FormatEnd(c, "list");
            Add("Format-Wide", Util, "Property:object@0; Column:int; AutoSize:switch; GroupBy:string; InputObject:psobject pipe; Force:switch", (c, o) => { if (c.HasInput) c.Items.Add(o); }).End = c => FormatEnd(c, "wide");
            Add("Format-Custom", Util, fmtSpec + "; Depth:int", (c, o) => { if (c.HasInput) c.Items.Add(o); }).End = c => FormatEnd(c, "list");

            Add("Out-Default", Core, "InputObject:psobject pipe; Transcript:switch", (c, o) => { if (c.HasInput) c.Items.Add(o); }).End = c => WriteHost(c);
            Add("Out-Host", Core, "InputObject:psobject pipe; Paging:switch", (c, o) => { if (c.HasInput) c.Items.Add(o); else if (c.Has("InputObject")) c.Items.Add(c.Get("InputObject")); }).End = c => WriteHost(c);
            Add("Out-Null", Core, "InputObject:psobject pipe", (c, o) => { });
            Add("Out-String", Util, "InputObject:psobject pipe; Stream:switch; Width:int", (c, o) => { if (c.HasInput) c.Items.Add(o); else if (c.Has("InputObject")) c.Items.Add(c.Get("InputObject")); }).End = c =>
            {
                var lines = PsFormat.ToLines(c.R, c.Items, c.Int("Width", c.R.Width));
                if (c.Sw("Stream")) { foreach (var l in lines) c.Out(l); return; }
                var sb = new StringBuilder();
                foreach (var l in lines) sb.Append(l).Append("\r\n");
                c.Out(sb.ToString());
            };
            Add("Out-File", Util, "FilePath:string@0 ! alias=Path; Encoding:string@1 set=Unknown|String|Unicode|BigEndianUnicode|UTF8|UTF7|UTF32|ASCII|Default|OEM; Append:switch; Force:switch; NoClobber:switch; Width:int; NoNewline:switch; InputObject:psobject pipe; LiteralPath:string alias=PSPath", (c, o) => { if (c.HasInput) c.Items.Add(o); else if (c.Has("InputObject")) c.Items.Add(c.Get("InputObject")); }).End = c =>
            {
                string path = c.R.FullFsPath(c.Str("LiteralPath") ?? c.Str("FilePath"), c.R.CurrentFsDir);
                var existing = c.R.FsNode(path);
                if (existing != null && c.Sw("NoClobber") && !c.Sw("Append")) throw c.Fail("The file '" + path + "' already exists.", "NoClobber", "ResourceExists", path, "System.IO.IOException");
                if (!c.ShouldProcess(path, "Output to File")) return;
                var lines = PsFormat.ToLines(c.R, c.Items, c.Int("Width", 80 > c.R.Width ? c.R.Width : c.R.Width));
                var sb = new StringBuilder();
                for (int i = 0; i < lines.Count; i++) { sb.Append(lines[i]); if (!c.Sw("NoNewline") || i < lines.Count - 1 && false) sb.Append(c.Sw("NoNewline") ? "" : "\r\n"); }
                string enc = c.Sw("Append") && existing != null ? existing.Encoding : EncodingName(c.Str("Encoding"));
                var why = c.R.WriteFile(path, sb.ToString(), enc, c.Sw("Append"));
                if (why != null) throw c.Fail(why, "FileOpenFailure", "OpenError", path, "System.IO.DirectoryNotFoundException");
            };
            Add("Out-GridView", Util, "InputObject:psobject pipe; Title:string; PassThru:switch; Wait:switch; OutputMode:string", (c, o) => { }).End = c =>
                c.Warn("Out-GridView opens a window on a real PC. Cert Lab's practice console cannot show it; use Format-Table or Out-Host instead.");
            Add("Out-Printer", Util, "InputObject:psobject pipe; Name:string@0", (c, o) => { }).End = c => c.Warn("There is no printer on the practice machines.");
        }

        static void WriteHost(CmdCtx c)
        {
            foreach (var l in PsFormat.ToLines(c.R, c.Items, c.R.Width)) c.Host(l);
        }
    }
}
