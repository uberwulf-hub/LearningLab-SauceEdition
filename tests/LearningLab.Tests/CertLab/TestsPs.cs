// Cert Lab - checks of the simulated PowerShell against Windows PowerShell 5.1's behaviour: the language (array
// unrolling, $null on the left, -eq on arrays filtering, scoping, string expansion, errors and their streams), the
// pipeline (ByValue / ByPropertyName binding), formatting, and the cmdlets of each area (more in TestsPs*.cs).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CertLab
{
    public static partial class Tests
    {
        /// <summary>A PowerShell console on a fresh pretend network: Run returns what one typed line printed.</summary>
        public class P
        {
            public PsSim Sim; public PsConsole C; public string Last = "";
            public P() : this(1) { }
            public P(int seed)
            {
                Sim = PsSim.Create(2, seed);
                C = Sim.Console("WIN-SRV1");
                C.Width = 120;
                C.Start();
                C.TakeOutput();
            }
            public string Run(string line) { C.Execute(line); Last = C.TakeOutput(); return Last; }
            /// <summary>Output with the trailing blank lines and spaces trimmed (for comparing tables).</summary>
            public string T(string line) { return Run(line).Replace("\r", "").Trim('\n'); }
            public string On(string machine, string line) { var c = Sim.Console(machine); if (!c.Waiting) { c.Start(); c.TakeOutput(); } c.Execute(line); Last = c.TakeOutput(); return Last; }
            public bool Ok(string check) { string o; return Sim.Check("WIN-SRV1", check, out o); }
            public PsMachine M { get { return Sim.M("WIN-SRV1"); } }
            public void Dispose() { Sim.Dispose(); }
        }

        static IEnumerable<KeyValuePair<string, Action>> PsGroups()
        {
            yield return new KeyValuePair<string, Action>("ps-lang", PsLangTests);
            yield return new KeyValuePair<string, Action>("ps-format", PsFormatTests);
            yield return new KeyValuePair<string, Action>("ps-scripts", PsScriptTests);
            foreach (var g in PsMoreGroups()) yield return g;
        }
        static partial void PsGroupsProviders(List<KeyValuePair<string, Action>> l);
        static partial void PsGroupsAdmin(List<KeyValuePair<string, Action>> l);
        static partial void PsGroupsNet(List<KeyValuePair<string, Action>> l);
        static partial void PsGroupsRemoting(List<KeyValuePair<string, Action>> l);
        static partial void PsGroupsContent(List<KeyValuePair<string, Action>> l);
        static IEnumerable<KeyValuePair<string, Action>> PsMoreGroups()
        {
            var l = new List<KeyValuePair<string, Action>>();
            PsGroupsProviders(l); PsGroupsAdmin(l); PsGroupsNet(l); PsGroupsRemoting(l); PsGroupsContent(l);
            return l;
        }

        static void Eq(string name, string got, string want) { Check(name, got == want, "got:\n" + got + "\nwant:\n" + want); }

        static void PsLangTests()
        {
            var t = new P();
            Eq("addition", t.T("1 + 2"), "3");
            Eq("string on the left concatenates", t.T("\"10\" + 5"), "105");
            Eq("number on the left adds", t.T("5 + \"10\""), "15");
            Eq("-eq on an array filters", t.T("1,2,3,2 -eq 2"), "2\n2");
            Eq("$null on the left", t.T("$a = 1,2,$null; $null -eq $a"), "False");
            Eq("$null on the right filters", t.T("$a = 1,2,$null; ($a -eq $null).Count"), "1");
            Eq("integer division gives a double", t.T("5 / 2"), "2.5");
            Eq("banker's rounding in [int]", t.T("[int]2.5; [int]3.5"), "2\n4");
            Eq("string expansion does not expand properties", t.T("$s = 'abc'; \"$s.Length\""), "abc.Length");
            Eq("subexpression expands properties", t.T("$s = 'abc'; \"$($s.Length)\""), "3");
            Eq("single quotes are literal", t.T("$x = 5; '$x'"), "$x");
            Eq("arrays unroll in the pipeline", t.T("function f { return 1,2,3 }; (f | Measure-Object).Count"), "3");
            Eq("one item comes back as a scalar", t.T("function f { ,@(1) }; $r = f; $r.GetType().Name"), "Object[]");
            Eq("unary comma keeps an array", t.T("function g { return ,(1,2,3) }; (g | Measure-Object).Count"), "1");
            Eq("all output is returned, not only return", t.T("function hh { 'a'; return 'b' }; (hh).Count"), "2");
            Eq("functions see the caller's variables", t.T("$v = 'outer'; function r { $v }; r"), "outer");
            Eq("assignment in a function stays local", t.T("$w = 1; function s { $w = 2 }; s; $w"), "1");
            Eq("$script: scope writes outside", t.T("$q = 1; function u { $script:q = 2 }; u; $q"), "2");
            Eq("ForEach-Object runs in the caller's scope", t.T("$sum = 0; 1..4 | ForEach-Object { $sum += $_ }; $sum"), "10");
            Eq("-match sets $Matches", t.T("'user=alice' -match 'user=(\\w+)' | Out-Null; $Matches[1]"), "alice");
            Eq("-replace uses regular expressions", t.T("'a.b.c' -replace '\\.', '-'"), "a-b-c");
            Eq("-split", t.T("('a,b,,c' -split ',').Count"), "4");
            Eq("-join", t.T("1,2,3 -join '+'"), "1+2+3");
            Eq("-f", t.T("'{0,5}|{1:N1}' -f 7, 2.25"), "    7|2.3");
            Eq("-like", t.T("'Get-Service' -like 'get-*'"), "True");
            Eq("-contains and -in", t.T("(1,2,3 -contains 2), (2 -in 1,2,3) -join ' '"), "True True");
            Eq("hashtable keys ignore case", t.T("$h = @{Name='x'}; $h.NAME"), "x");
            Eq("[ordered] keeps the order", t.T("$o = [ordered]@{b=1; a=2}; $o.Keys -join ','"), "b,a");
            Eq("[pscustomobject] keeps the order", t.T("([pscustomobject]@{z=1; y=2}).psobject.Properties.Name -join ','"), "z,y");
            Eq("switch runs every matching clause", t.T("switch (3) { 3 { 'a' } {$_ -gt 2} { 'b' } default { 'c' } }"), "a\nb");
            Eq("switch -wildcard", t.T("switch -Wildcard ('report.csv') { '*.txt' { 'text' } '*.csv' { 'csv' } }"), "csv");
            Eq("foreach over $null does nothing", t.T("$n = $null; foreach ($i in $n) { 'x' }; 'done'"), "done");
            Eq("$null piped runs once", t.T("$null | ForEach-Object { 'once' }"), "once");
            Eq("empty output piped runs never", t.T("$e = Get-Variable nothere -ErrorAction SilentlyContinue; $e | ForEach-Object { 'x' }; 'end'"), "end");
            Eq("try / catch / finally", t.T("try { throw 'boom' } catch { \"caught $_\" } finally { 'cleanup' }"), "caught boom\ncleanup");
            Eq("non-terminating errors are not caught", t.T("try { Get-Variable nothere } catch { 'caught' }; 'after'").Contains("after") && !t.Last.Contains("\ncaught") ? "ok" : t.Last, "ok");
            Eq("-ErrorAction Stop makes it catchable", t.T("try { Get-Variable nothere -ErrorAction Stop } catch { 'caught' }"), "caught");
            Eq("catch by type", t.T("try { Get-Variable nothere -ErrorAction Stop } catch [System.Management.Automation.ItemNotFoundException] { 'typed' } catch { 'other' }"), "typed");
            Eq("$? after an error", t.T("Get-Variable nothere -ErrorAction SilentlyContinue; $?"), "False");
            Eq("$Error collects errors", t.T("$Error.Clear(); Get-Variable nothere -ErrorAction SilentlyContinue; $Error.Count"), "1");
            Eq("statement-terminating error: the next statement runs", t.T("$z = $null; $z.Foo(); 'next'").EndsWith("next") ? "ok" : t.Last, "ok");
            Eq("throw stops the script", t.T("throw 'stop'; 'never'").Contains("\nnever") ? t.Last : "ok", "ok");
            Eq("ArrayList.Add returns the index", t.T("$l = New-Object System.Collections.ArrayList; $l.Add('a'); $l.Add('b')"), "0\n1");
            Eq("adding to a fixed array fails", t.T("$f = 1,2; $f.Add(3)").Contains("Collection was of a fixed size.") ? "ok" : t.Last, "ok");
            Eq("New-Object with an array argument splits it", t.T("$arr = 1,2,3; New-Object System.Collections.ArrayList $arr").Contains("Cannot find an overload for \"ArrayList\" and the argument count: \"3\"") ? "ok" : t.Last, "ok");
            Eq("$null to a .NET string parameter becomes empty", t.T("[string]::Concat('[', $null, ']')"), "[]");
            Eq("a parameter named $Input loses its value", t.T("function k($Input) { \"[$Input]\" }; k 'hello'"), "[]");
            Eq("pipeline ByValue", t.T("'one','two' | ForEach-Object { $_.ToUpper() }"), "ONE\nTWO");
            Eq("advanced function, pipeline by property name", t.T("function pn { [CmdletBinding()] param([Parameter(ValueFromPipelineByPropertyName)]$Name) process { \"n=$Name\" } }; [pscustomobject]@{Name='x'} | pn"), "n=x");
            Eq("begin / process / end order", t.T("function bpe { begin { 'b' } process { \"p$_\" } end { 'e' } }; 1,2 | bpe"), "b\np1\np2\ne");
            Eq("ValidateSet", t.T("function vs { param([ValidateSet('a','b')]$x) $x }; vs c").Contains("does not belong to the set \"a,b\"") ? "ok" : t.Last, "ok");
            Eq("here-string", t.T("$n = 'W'\n$t = @\"\nHi $n\nBye\n\"@\n$t"), "Hi W\nBye");
            Eq("splatting", t.T("$p = @{Name='ErrorActionPreference'; ValueOnly=$true}; Get-Variable @p"), "Continue");
            Eq("break in ForEach-Object leaves the loop around it", t.T("foreach ($o in 1,2) { 1..3 | ForEach-Object { if ($_ -eq 2) { break }; \"$o-$_\" } }; 'after'"), "1-1\nafter");
            Eq("ranges", t.T("(1..4) -join ''"), "1234");
            Eq("character ranges fail in 5.1", t.T("'a'..'c'").Contains("Cannot convert value \"a\" to type \"System.Int32\"") ? "ok" : t.Last, "ok");
            Eq("Split with a string splits on characters in 5.1", t.T("'a--b'.Split('--').Count"), "3");
            Eq("GetType", t.T("(5).GetType().FullName"), "System.Int32");
            Eq("casts", t.T("[int]'42' + 1"), "43");
            Eq("-as returns null on failure", t.T("$null -eq ('abc' -as [int])"), "True");
            Eq("member enumeration", t.T("@([pscustomobject]@{n=1},[pscustomobject]@{n=2}).n -join ','"), "1,2");
            Eq("incomplete input waits for more", t.Run("if ($true) {").Length == 0 && t.C.Prompt == ">> " ? "ok" : t.C.Prompt, "ok");
            t.Run("'inside' }");
            Eq("the block ran after the closing brace", t.Last.Replace("\r", "").Trim(), "inside");
            t.Dispose();
        }

        static void PsScriptTests()
        {
            var t = new P();
            string fn = "function Get-Greeting {\n<#\n.SYNOPSIS\nSays hello.\n#>\n[CmdletBinding(SupportsShouldProcess)]\nparam(\n  [Parameter(Mandatory, ValueFromPipeline)]\n  [string[]]$Name,\n  [ValidateRange(1,3)][int]$Times = 1\n)\nprocess { foreach ($n in $Name) { if ($PSCmdlet.ShouldProcess($n, 'Greet')) { for ($i = 0; $i -lt $Times; $i++) { \"Hello, $n\" } } } }\n}";
            foreach (var line in fn.Split('\n')) t.Run(line);
            Eq("typed line by line, the function is defined", t.T("(Get-Command Get-Greeting).CommandType"), "Function");
            Eq("an advanced function", t.T("Get-Greeting -Name Ada, Bob -Times 2"), "Hello, Ada\nHello, Ada\nHello, Bob\nHello, Bob");
            Eq("pipeline input by value", t.T("'Cy' | Get-Greeting"), "Hello, Cy");
            Eq("-WhatIf", t.T("Get-Greeting -Name Dee -WhatIf"), "What if: Performing the operation \"Greet\" on target \"Dee\".");
            Eq("ValidateRange", t.Run("Get-Greeting -Name Eve -Times 5").Replace("\r", ""), "Get-Greeting : Cannot validate argument on parameter 'Times'. The 5 argument is greater than the maximum allowed range of 3. Supply an argument that is less than or equal to 3 and then try the command again.\nAt line:1 char:31\n+ Get-Greeting -Name Eve -Times 5\n+                               ~\n    + CategoryInfo          : InvalidArgument: (:) [Get-Greeting], ParameterBindingValidationException\n    + FullyQualifiedErrorId : ParameterArgumentValidationError,Get-Greeting\n\n");
            Eq("help from comments and the syntax", t.T("Get-Help Get-Greeting").Contains("Says hello.") && t.Last.Contains("Get-Greeting [-Name] <string[]> [[-Times] <int>] [-WhatIf] [-Confirm] [<CommonParameters>]") ? "ok" : t.Last, "ok");
            t.Run("'param($Who = \"world\") \"Hello, $Who from $PSScriptRoot\"' > C:\\LabFiles\\hello.ps1");
            Eq("a script by its path", t.T("C:\\LabFiles\\hello.ps1 -Who you"), "Hello, you from C:\\LabFiles");
            Eq("scripts run in their own scope", t.T("'$inner = 1' > C:\\LabFiles\\scope.ps1; C:\\LabFiles\\scope.ps1; $null -eq $inner"), "True");
            Eq("dot-sourcing keeps the variables", t.T(". C:\\LabFiles\\scope.ps1; $inner"), "1");
            Eq("Restricted policy on WIN-CL1", t.On("WIN-CL1", "'1' > C:\\LabFiles\\x.ps1; C:\\LabFiles\\x.ps1").Contains("cannot be loaded because running scripts is disabled on this system") ? "ok" : t.Last, "ok");
            Eq("a mandatory parameter prompts at the console", t.Run("function Need { param([Parameter(Mandatory)]$Thing) \"got $Thing\" }; Need").Contains("Supply values for the following parameters:") && t.C.Prompt == "Thing: " ? "ok" : t.Last + "|" + t.C.Prompt, "ok");
            Eq("the answer is used", t.T("box"), "got box");
            Eq("Read-Host", t.Run("$a = Read-Host 'Your name'").Length == 0 && t.C.Prompt == "Your name: " ? "ok" : t.C.Prompt, "ok");
            t.Run("Ada");
            Eq("Read-Host returns the line", t.T("$a"), "Ada");
            Eq("$MyInvocation in a function", t.T("function mi { $MyInvocation.MyCommand }; mi"), "mi");
            Eq("$PSBoundParameters", t.T("function pb { param($a, $b) $PSBoundParameters.ContainsKey('b') }; pb -a 1"), "False");
            Eq("filter", t.T("filter Double { $_ * 2 }; 1, 2 | Double"), "2\n4");
            Eq("return leaves only the function", t.T("function rt { foreach ($i in 1..5) { if ($i -eq 3) { return } ; $i } }; rt"), "1\n2");
            Eq("labels on loops", t.T(":outer foreach ($a in 1..3) { foreach ($b in 1..3) { if ($b -eq 2) { continue outer }; \"$a$b\" } }"), "11\n21\n31");
            Eq("do until", t.T("$i = 0; do { $i++ } until ($i -ge 3); $i"), "3");
            Eq("switch -regex sets $Matches", t.T("switch -Regex ('id=42') { 'id=(\\d+)' { $Matches[1] } }"), "42");
            Eq("try / catch inside a function sees errors from cmdlets with Stop", t.T("function tc { try { Get-Variable nope -ErrorAction Stop; 'no' } catch { 'yes' } }; tc"), "yes");
            Eq("finally runs on return", t.T("function fr { try { return 'r' } finally { 'f' } }; fr"), "r\nf");
            Eq("$Error[0] is the newest", t.T("$Error.Clear(); Get-Variable a1 -ea 0; Get-Variable a2 -ea 0; $Error[0].TargetObject"), "a2");
            Eq("-ErrorVariable", t.T("Get-Variable zz -ErrorAction SilentlyContinue -ErrorVariable ev; $ev.Count"), "1");
            Eq("throw an exception type and catch it by type", t.T("try { throw [System.IO.FileNotFoundException]::new('gone') } catch [System.IO.IOException] { $_.Exception.GetType().Name }"), "FileNotFoundException");
            Eq("$_ in catch is the ErrorRecord", t.T("try { throw 'x' } catch { $_.GetType().FullName }"), "System.Management.Automation.ErrorRecord");
            Eq("Write-Error makes a non-terminating error", t.T("function we2 { Write-Error 'w'; 'after' }; we2 2>$null"), "after");
            Eq("2>&1 merges errors into output", t.T("(Get-Variable none 2>&1).GetType().Name"), "ErrorRecord");
            Eq("Set-StrictMode catches unset variables", t.T("Set-StrictMode -Version Latest; $undefined1").Contains("cannot be retrieved because it has not been set") ? "ok" : t.Last, "ok");
            t.Run("Set-StrictMode -Off");
            t.Dispose();
        }

        static void PsFormatTests()
        {
            var t = new P();
            Eq("custom objects: compact table", t.Run("[pscustomobject]@{Name='a'; Value=1}, [pscustomobject]@{Name='bb'; Value=22}"), "\nName Value\n---- -----\na        1\nbb      22\n\n\n");
            Eq("hashtable: Name (30) Value", t.Run("@{a=1}"), "\nName                           Value\n----                           -----\na                              1\n\n\n");
            Eq("5 properties: a list", t.Run("[pscustomobject]@{a=1;b=2;c=3;d=4;e=5}"), "\na : 1\nb : 2\nc : 3\nd : 4\ne : 5\n\n\n");
            Eq("collections in cells", t.T("[pscustomobject]@{n=1..6}").Contains("{1, 2, 3, 4...}") ? "ok" : t.Last, "ok");
            Eq("Format-List", t.Run("[pscustomobject]@{Name='x'; Size=10} | Format-List"), "\nName : x\nSize : 10\n\n\n");
            Eq("Format-Table output is not data", t.T("[pscustomobject]@{a=1} | Format-Table | Select-Object -First 1 | ForEach-Object { $_.GetType().Name }"), "FormatStartData");
            Eq("ConvertTo-Json 5.1 layout", t.T("[pscustomobject]@{Name='x'; Inner=[pscustomobject]@{A=1}} | ConvertTo-Json"), "{\n    \"Name\":  \"x\",\n    \"Inner\":  {\n                  \"A\":  1\n              }\n}");
            Eq("ConvertTo-Csv writes #TYPE", t.T("[pscustomobject]@{a=1;b='x'} | ConvertTo-Csv"), "#TYPE System.Management.Automation.PSCustomObject\n\"a\",\"b\"\n\"1\",\"x\"");
            Eq("ConvertFrom-Json array stays one object", t.T("('[1,2,3]' | ConvertFrom-Json | Measure-Object).Count"), "1");
            Eq("Out-String keeps the blank lines", t.T("(@{a=1} | Out-String).Length -gt 60"), "True");
            Eq("Measure-Object", t.Run("1..3 | Measure-Object -Sum"), "\nCount    : 3\nAverage  :\nSum      : 6\nMaximum  :\nMinimum  :\nProperty :\n\n\n");
            Eq("Group-Object", t.T("'a','b','a' | Group-Object | Sort-Object Count -Descending | Select-Object -First 1 -ExpandProperty Name"), "a");
            Eq("Sort-Object -Descending", t.T("3,1,2 | Sort-Object -Descending"), "3\n2\n1");
            Eq("Select-Object makes Selected. objects", t.T("([pscustomobject]@{a=1;b=2} | Select-Object a).psobject.TypeNames[0]"), "Selected.System.Management.Automation.PSCustomObject");
            Eq("calculated properties", t.T("[pscustomobject]@{a=2} | Select-Object @{Name='Double'; Expression={ $_.a * 2 }} | ForEach-Object Double"), "4");
            Eq("DateTime on its own line", t.T("Get-Date -Year 2026 -Month 3 -Day 2 -Hour 9 -Minute 0 -Second 0"), "Monday, March 2, 2026 9:00:00 AM");
            Eq("GetType table", t.Run("'x'.GetType()"), "\nIsPublic IsSerial Name                                     BaseType\n-------- -------- ----                                     --------\nTrue     True     String                                   System.Object\n\n\n");
            Eq("command not found", t.Run("foo"), "foo : The term 'foo' is not recognized as the name of a cmdlet, function, script file, or operable program. Check the spelling of the name, or if a path was included, verify that the path is correct and try again.\nAt line:1 char:1\n+ foo\n+ ~~~\n    + CategoryInfo          : ObjectNotFound: (foo:String) [], CommandNotFoundException\n    + FullyQualifiedErrorId : CommandNotFoundException\n\n");
            Eq("Write-Error in a function", t.Run("function we { Write-Error 'bad' }; we").StartsWith("we : bad\nAt line:1 char:36") ? "ok" : t.Last, "ok");
            Eq("Get-Member groups by type", t.Run("'x' | Get-Member -Name Length").Replace("\r", ""), "\n\n   TypeName: System.String\n\nName   MemberType Definition\n----   ---------- ----------\nLength Property   int Length {get;}\n\n\n");
            t.Dispose();
        }
    }
}
