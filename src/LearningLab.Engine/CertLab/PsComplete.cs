// Cert Lab - Tab in the PowerShell console: command names (cmdlets, functions, aliases), parameter names of the
// command being typed (-Na -> -Name), variables ($Err -> $Error) and paths on the pretend disk.
using System;
using System.Collections.Generic;
using System.Linq;

namespace CertLab
{
    public static class PsComplete
    {
        public static Completion Candidates(PsRunspace R, string line)
        {
            var c = new Completion();
            if (R == null) return c;
            int start = line.Length;
            while (start > 0 && !char.IsWhiteSpace(line[start - 1]) && "|;({,=".IndexOf(line[start - 1]) < 0) start--;
            string word = line.Substring(start);
            c.Start = start;
            string before = line.Substring(0, start).TrimEnd();
            bool commandPos = before.Length == 0 || before.EndsWith("|", StringComparison.Ordinal) || before.EndsWith(";", StringComparison.Ordinal) || before.EndsWith("(", StringComparison.Ordinal) || before.EndsWith("{", StringComparison.Ordinal) || before.EndsWith("&", StringComparison.Ordinal);
            var words = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            if (word.StartsWith("$", StringComparison.Ordinal))
            {
                string v = word.Substring(1);
                for (var s = R.Global; s != null; s = s.Parent) foreach (var k in s.Vars.Keys) if (k.StartsWith(v, StringComparison.OrdinalIgnoreCase)) words.Add("$" + k);
                if (v.StartsWith("env:", StringComparison.OrdinalIgnoreCase)) foreach (var k in R.Env.Keys) if (k.StartsWith(v.Substring(4), StringComparison.OrdinalIgnoreCase)) words.Add("$env:" + k);
            }
            else if (word.StartsWith("-", StringComparison.Ordinal) && !commandPos)
            {
                string cmd = CommandOf(before);
                var ci = cmd == null ? null : R.Resolve(cmd, R.Global, false);
                var names = new List<string>();
                if (ci != null && ci.Def != null) names.AddRange(ci.Def.Params.Select(p => p.Name));
                else if (ci != null && ci.Func != null && ci.Func.Ast.Params != null) names.AddRange(ci.Func.Ast.Params.Select(p => p.Name));
                if (ci != null && (ci.Def != null || ci.Func != null && ci.Func.Ast.Advanced)) names.AddRange(PsBinder.CommonNames);
                foreach (var n in names) if (("-" + n).StartsWith(word, StringComparison.OrdinalIgnoreCase)) words.Add("-" + n);
            }
            else if (commandPos && !word.Contains("\\") && !word.Contains("/"))
            {
                foreach (var d in PsCmdlets.All.Values) if (d.Name.StartsWith(word, StringComparison.OrdinalIgnoreCase) && (d.Available == null || d.Available(R.M))) words.Add(d.Name);
                foreach (var a in R.Aliases.Keys) if (a.StartsWith(word, StringComparison.OrdinalIgnoreCase) && word.Length > 0) words.Add(a);
                for (var s = R.Global; s != null; s = s.Parent) foreach (var f in s.Funcs.Keys) if (f.StartsWith(word, StringComparison.OrdinalIgnoreCase)) words.Add(f);
                foreach (var m in R.LoadedModules) foreach (var f in m.Exported.Keys) if (f.StartsWith(word, StringComparison.OrdinalIgnoreCase)) words.Add(f);
            }
            if (words.Count == 0 && !word.StartsWith("$", StringComparison.Ordinal) && !(word.StartsWith("-", StringComparison.Ordinal) && !commandPos)) Paths(R, word, words, c);
            c.Words.AddRange(words);
            return c;
        }

        static string CommandOf(string before)
        {
            int i = Math.Max(before.LastIndexOf('|'), Math.Max(before.LastIndexOf(';'), Math.Max(before.LastIndexOf('('), before.LastIndexOf('{'))));
            var seg = before.Substring(i + 1).Trim();
            var w = seg.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return w.Length == 0 ? null : w[0];
        }

        static void Paths(PsRunspace R, string word, SortedSet<string> words, Completion c)
        {
            string w = word.Trim('\'', '"');
            int slash = w.LastIndexOf('\\');
            string dirPart = slash >= 0 ? w.Substring(0, slash + 1) : "";
            string namePart = slash >= 0 ? w.Substring(slash + 1) : w;
            string full = R.FullFsPath(dirPart.Length == 0 ? "." : dirPart, R.CurrentFsDir);
            var dir = R.FsNode(full);
            if (dir == null || !dir.IsDir) return;
            foreach (var k in dir.Kids.Values.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (!k.Name.StartsWith(namePart, StringComparison.OrdinalIgnoreCase)) continue;
                string cand = (dirPart.Length == 0 && !w.StartsWith(".", StringComparison.Ordinal) ? ".\\" : dirPart) + k.Name + (k.IsDir ? "\\" : "");
                if (cand.Contains(" ")) cand = "'" + cand + "'";
                words.Add(cand);
                if (k.IsDir) c.NoSpace.Add(cand);
            }
        }
    }
}
