// Cert Lab - Out-Default: turns the objects a command line outputs into console text. Objects of one shape share a
// table (fixed-width views print each row as it arrives; other tables wait for the end of the statement to size
// their columns from the data, like Windows PowerShell 5.1's short wait); a change of type starts a new table or list.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CertLab
{
    /// <summary>What Format-Table / Format-List / Format-Wide output: rendered lines in format objects.</summary>
    public class PsFormatPayload { public List<string> Lines = new List<string>(); }

    public class FmtOptions
    {
        public List<object> Props;      // names or calculated property hashtables
        public bool AutoSize, HideHeaders, Wrap, Force;
        public string GroupBy; public int Columns = 2;
    }

    public class OutDefault
    {
        readonly PsRunspace R; readonly Action<string> write;
        string key, kind, groupVal;
        FmtView view; List<FmtCol> cols; int[] widths; bool headerDone, any;
        readonly List<object> pending = new List<object>();
        public int Width;

        public OutDefault(PsRunspace r, Action<string> w) { R = r; write = w; Width = r == null ? 120 : r.Width; }

        public void Interrupt() { }

        public void Push(object o)
        {
            o = PsConv.Base(o);
            if (o is AutoNull) return;
            var po = o as PsObj;
            if (po != null && po.Base is PsFormatPayload)
            {
                Close();
                foreach (var l in ((PsFormatPayload)po.Base).Lines) write(l + "\n");
                return;
            }
            if (po != null && po.TypeName.StartsWith("Microsoft.PowerShell.Commands.Internal.Format.", StringComparison.Ordinal)) return;
            if (o is PsErrorRecord) { Close(); write(PsRunspace.ErrorText((PsErrorRecord)o)); return; }
            if (po != null && (po.TypeName == "System.Management.Automation.WarningRecord" || po.TypeName == "System.Management.Automation.VerboseRecord")) { Close(); write((po.TypeName.Contains("Warning") ? "WARNING: " : "VERBOSE: ") + po.ToString() + "\n"); return; }
            if (o is PsHostText) { Close(); write(((PsHostText)o).Text + "\n"); return; }
            if (o == null) return;
            if (o is IDictionary) { foreach (DictionaryEntry e in (IDictionary)o) Push(e); return; }
            if (PsFormat.IsScalar(o)) { Close(); write(PsFormat.ScalarText(o) + "\n"); return; }
            string k = KeyOf(o);
            if (k != key) { Close(); Open(o, k); }
            Add(o);
        }

        static string KeyOf(object o)
        {
            // types that share a view (FileInfo and DirectoryInfo, aliases and cmdlets) share one table
            var v = PsFormat.ViewFor(o);
            if (v != null) return "view#" + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(v) + (o is PsObj && ((PsObj)o).Computer != null ? "#remote" : "");
            var po = o as PsObj;
            // Objects without a view made by Select-Object or [pscustomobject] continue the table the first one started
            // (5.1 keeps the first object's columns: Get-ChildItem -Recurse | Select-Object FullName, Length is one table).
            if (po != null && (po.TypeName.StartsWith("Selected.", StringComparison.Ordinal) || po.TypeName == "System.Management.Automation.PSCustomObject")) return "custom-object";
            if (po != null) return po.TypeName;
            if (o is DictionaryEntry) return "DictionaryEntry";
            return PsTypes.TypeNameOf(o);
        }

        void Open(object o, string k)
        {
            key = k; any = false; headerDone = false; groupVal = null; pending.Clear(); widths = null;
            view = PsFormat.ViewFor(o);
            if (view != null) { kind = view.Kind; cols = view.Cols; if (o is PsObj && ((PsObj)o).Computer != null && kind == "table") { cols = cols.ToList(); cols.Add(PsFormat.P("PSComputerName", 0)); } return; }
            var props = PsFormat.DefaultProps(o);
            var lp = PsFormat.ListPropsFor(o);
            if (props.Count > 4 || props.Count == 0 && lp != null) { kind = "list"; cols = (lp != null && !(o is PsObj && ((PsObj)o).DefaultDisplay != null) ? lp.ToList() : props).Select(p => PsFormat.P(p, 0)).ToList(); return; }
            kind = "autotable";
            cols = props.Select(p => PsFormat.P(p, 0)).ToList();
            if (props.Count == 0) { kind = "list"; }
        }

        void Add(object o)
        {
            if (kind == "list")
            {
                if (!any) write("\n");
                any = true;
                foreach (var l in PsRender.ListBlock(R, o, cols, Width)) write(l + "\n");
                write("\n");
                return;
            }
            if (kind == "wide") { pending.Add(o); any = true; return; }
            if (kind == "custom") { if (!any) write("\n"); any = true; write(view.Custom(o) + "\n"); return; }
            bool fixedW = view != null && !view.Auto;
            if (view != null && view.GroupKey != null)
            {
                string g = view.GroupKey(o);
                if (g != groupVal)
                {
                    if (any) write("\n");
                    groupVal = g;
                    write("\n\n" + view.GroupHeader(o) + "\n" + (view.GroupGap > 0 ? new string('\n', view.GroupGap) : view.GroupOneBlank ? "\n" : view.Cols.Count > 0 && view.Cols[0].Header == "Name" ? "\n" : "\n\n"));
                    headerDone = false;
                }
            }
            any = true;
            if (fixedW)
            {
                if (widths == null) widths = cols.Select(c => c.Width).ToArray();
                if (!headerDone) { if (view.GroupKey == null || groupVal == null) write("\n"); foreach (var l in PsRender.Header(cols, widths, Width)) write(l + "\n"); headerDone = true; }
                write(PsRender.Row(R, o, cols, widths, Width) + "\n");
                return;
            }
            if (widths != null) { write(PsRender.Row(R, o, cols, widths, Width) + "\n"); return; }
            pending.Add(o);
        }

        public void EndStatement()
        {
            if (pending.Count == 0) return;
            if (kind == "wide") return;
            if (widths == null)
            {
                widths = PsRender.AutoWidths(R, pending, cols, Width);
                if (view == null || view.GroupKey == null) write("\n");
                foreach (var l in PsRender.Header(cols, widths, Width)) write(l + "\n");
                headerDone = true;
            }
            foreach (var o in pending) write(PsRender.Row(R, o, cols, widths, Width) + "\n");
            pending.Clear();
        }

        void Close()
        {
            if (key == null) return;
            EndStatement();
            if (kind == "wide" && pending.Count > 0) { foreach (var l in PsRender.Wide(R, pending, view.WideProp, 2, Width)) write(l + "\n"); pending.Clear(); }
            if (any) write(kind == "list" ? "\n" : "\n\n");
            key = null; any = false; widths = null;
        }

        public void Finish() { Close(); }
    }
}
