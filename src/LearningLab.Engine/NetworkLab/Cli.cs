// Network Lab - the command line of the routers and switches, in the style of the classic router operating
// systems learners practise on: modes (> # (config)# ...), abbreviations, ? help, Tab completion, "no" and "do",
// and the familiar error messages. Commands themselves are in CliCommands.cs, the show outputs in CliShow.cs.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace NetworkLab
{
    public class LineCfg { public string Password = ""; public bool Encrypted; public bool Login; public int ExecTimeout = 10; public bool LoggingSync; public string Transport = ""; }

    /// <summary>The settings of a router or switch that aren't on an interface (and its saved configuration).</summary>
    public class Ios
    {
        public readonly Device D;
        public string Hostname;
        public string EnableSecret = "";     // "$1$salt$hash"
        public string EnablePassword = "";
        public bool PasswordEncryption;
        public string Banner = "";
        public bool DomainLookup = true;
        public string DomainName = "";
        public readonly List<uint> NameServers = new List<uint>();
        public readonly Dictionary<string, uint> HostTable = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        public readonly LineCfg Con = new LineCfg();
        public readonly LineCfg Vty = new LineCfg { Login = false };
        public bool VtyDefined;
        public string Startup;               // null: never saved
        public long BootTime;
        public string StpMode = "pvst";
        public bool Cdp = true;
        public readonly List<CliSession> Sessions = new List<CliSession>();

        public Ios(Device d)
        {
            D = d;
            Hostname = d.Kind == "switch" ? "Switch" : "Router";
        }

        public static readonly string[] IfTypes = { "FastEthernet", "GigabitEthernet", "Vlan", "Loopback", "Ethernet" };

        /// <summary>g0/0 -> GigabitEthernet0/0, fa0/1 -> FastEthernet0/1, vlan 10 -> Vlan10, g0/0.10 -> GigabitEthernet0/0.10.</summary>
        public static string NormalizeIfName(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            s = s.Replace(" ", "");
            int i = 0;
            while (i < s.Length && char.IsLetter(s[i])) i++;
            if (i == 0 || i == s.Length) return null;
            string t = s.Substring(0, i).ToLowerInvariant(), rest = s.Substring(i);
            if (!char.IsDigit(rest[0])) return null;
            string type = null;
            foreach (var ty in IfTypes) if (ty.ToLowerInvariant().StartsWith(t)) { if (type != null && t.Length < 2 && ty == "Ethernet") continue; type = type ?? ty; }
            if (t == "e" || t == "et" || t == "eth") type = "Ethernet";
            if (type == null) return null;
            foreach (char c in rest) if (!char.IsDigit(c) && c != '/' && c != '.') return null;
            return type + rest;
        }

        public static string ShortIf(string full)
        {
            if (full.StartsWith("GigabitEthernet")) return "Gig" + full.Substring(15);
            if (full.StartsWith("FastEthernet")) return "Fa" + full.Substring(12);
            return full;
        }

        // ---------------------------------------------------------------- passwords
        static readonly string Xlat = "dsfd;kfoA,.iyewrkldJKDHSUBsgvca69834ncxv9873254k;fg87";
        public static string Type7(string plain)
        {
            int seed = 2;
            var sb = new StringBuilder(seed.ToString("00"));
            for (int i = 0; i < plain.Length; i++) sb.Append(((int)plain[i] ^ Xlat[(seed + i) % Xlat.Length]).ToString("X2"));
            return sb.ToString();
        }
        public static string FromType7(string enc)
        {
            try
            {
                int seed = int.Parse(enc.Substring(0, 2));
                var sb = new StringBuilder();
                for (int i = 2, k = 0; i + 1 < enc.Length; i += 2, k++) sb.Append((char)(Convert.ToInt32(enc.Substring(i, 2), 16) ^ Xlat[(seed + k) % Xlat.Length]));
                return sb.ToString();
            }
            catch (Exception) { return null; }
        }
        public static string Secret(string plain, string salt)
        {
            if (salt == null)
            {
                var r = new Random(plain.GetHashCode());
                const string abc = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
                var s = new char[4]; for (int i = 0; i < 4; i++) s[i] = abc[r.Next(abc.Length)];
                salt = new string(s);
            }
            using (var md5 = MD5.Create())
            {
                var h = md5.ComputeHash(Encoding.UTF8.GetBytes(salt + "$" + plain));
                string b = Convert.ToBase64String(h).Replace("+", ".").Replace("=", "").Replace("/", "x");
                return "$1$" + salt + "$" + b.Substring(0, Math.Min(22, b.Length));
            }
        }
        public static bool CheckSecret(string hash, string plain)
        {
            if (string.IsNullOrEmpty(hash) || !hash.StartsWith("$1$")) return false;
            string[] p = hash.Split('$');
            if (p.Length < 4) return false;
            return Secret(plain, p[2]) == hash;
        }
        public bool NeedsEnablePassword { get { return EnableSecret.Length > 0 || EnablePassword.Length > 0; } }
        public bool EnableOk(string typed)
        {
            if (EnableSecret.Length > 0) return CheckSecret(EnableSecret, typed);
            return EnablePassword == typed;
        }

        public string Uptime
        {
            get
            {
                long ms = D.P.Sim.Now - BootTime;
                long m = ms / 60000;
                return string.Format("{0} days, {1} hours, {2} minutes", m / 1440, (m / 60) % 24, m % 60);
            }
        }
    }

    // ==================================================================== the parser
    public class CliNode
    {
        public string Word;          // a keyword (lower case), or null for an argument
        public string Arg;           // ip, mask, wild, num, word, line, iface, ifrange, vlanlist, port
        public int Min, Max;
        public string Help = "";
        public string ArgHelp = "";  // e.g. A.B.C.D, <1-4094>, WORD
        public readonly List<CliNode> Kids = new List<CliNode>();
        public Action<CliCtx> Run;
        public bool Hidden;
        /// <summary>For a LINE argument: the ? help for what has been typed so far.</summary>
        public Func<CliSession, string, string> HelpFn;

        public bool Accepts(string tok)
        {
            switch (Arg)
            {
                case "ip": case "mask": case "wild": uint x; return Ip.TryParse(tok, out x);
                case "num": long n; return long.TryParse(tok, NumberStyles.None, CultureInfo.InvariantCulture, out n) && n >= Min && n <= Max;
                case "word": case "line": return tok.Length > 0;
                case "iface": return Ios.NormalizeIfName(tok) != null;
                case "port": return Ports.Parse(tok) >= 0;
                case "vlanlist": return VlanList.IsValid(tok);
                case "prefixmask": uint m; return Ip.TryParseMask(tok, out m);
            }
            return false;
        }
    }

    public class CliCtx
    {
        public CliSession S;
        public bool No;
        public readonly List<string> Args = new List<string>();
        public readonly List<string> Words = new List<string>();
        public string Line = "";
        public string Arg(int i) { return i < Args.Count ? Args[i] : ""; }
        public bool Has(string kw) { return Words.Contains(kw); }
        public void Out(string s) { S.Print(s); }
        public Device D { get { return S.D; } }
        public Ios Ios { get { return S.D.Ios; } }
        public IpStack IpS { get { return S.D.Ip; } }
    }

    public class CliTree
    {
        public readonly CliNode Root = new CliNode();
        public static readonly Dictionary<string, string> Helps = new Dictionary<string, string>();

        /// <summary>Adds a command from a pattern: keywords, and arguments in angle brackets: &lt;ip&gt; &lt;mask&gt;
        /// &lt;wild&gt; &lt;num:1-4094&gt; &lt;word&gt; &lt;line&gt; &lt;iface&gt; &lt;port&gt; &lt;vlanlist&gt;.
        /// Help for a keyword may follow it: keyword=help text|.</summary>
        public CliNode Add(string pattern, Action<CliCtx> run, params string[] helps)
        {
            var node = Root;
            var toks = pattern.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < toks.Length; i++)
            {
                string t = toks[i];
                string help = i < helps.Length ? helps[i] : null;
                CliNode next = null;
                if (t.StartsWith("<"))
                {
                    string a = t.Trim('<', '>');
                    int min = 0, max = 0;
                    if (a.StartsWith("num:")) { var r = a.Substring(4).Split('-'); min = int.Parse(r[0]); max = int.Parse(r[1]); a = "num"; }
                    next = node.Kids.FirstOrDefault(k => k.Word == null && k.Arg == a && k.Min == min && k.Max == max);
                    if (next == null)
                    {
                        next = new CliNode { Arg = a, Min = min, Max = max };
                        next.ArgHelp = ArgName(a, min, max);
                        node.Kids.Add(next);
                    }
                }
                else
                {
                    next = node.Kids.FirstOrDefault(k => k.Word == t);
                    if (next == null) { next = new CliNode { Word = t }; node.Kids.Add(next); }
                    if (string.IsNullOrEmpty(next.Help)) { string h; if (Helps.TryGetValue(t, out h)) next.Help = h; }
                }
                if (!string.IsNullOrEmpty(help)) next.Help = help;
                node = next;
            }
            node.Run = run;
            return node;
        }

        static string ArgName(string a, int min, int max)
        {
            switch (a)
            {
                case "ip": return "A.B.C.D";
                case "mask": return "A.B.C.D";
                case "wild": return "A.B.C.D";
                case "num": return "<" + min + "-" + max + ">";
                case "word": return "WORD";
                case "line": return "LINE";
                case "iface": return "WORD";
                case "port": return "<0-65535>";
                case "vlanlist": return "WORD";
                case "prefixmask": return "A.B.C.D";
            }
            return a;
        }
    }

    /// <summary>One person typing at a router or switch (the CLI tab, a console cable, or a lab's answer script).</summary>
    public class CliSession
    {
        public readonly Device D;
        public string Mode = "user";
        public IpIf If;                      // config-if / config-subif
        public List<Port> Range;             // config-if-range
        public int VlanId;                   // config-vlan
        public string Pool = "";             // dhcp-config
        public Acl Acl;                      // config-std-nacl / config-ext-nacl
        public string LineKind = "con";
        public readonly StringBuilder Output = new StringBuilder();
        public readonly List<string> History = new List<string>();
        public Func<string, bool> Pending;   // the next line answers a question (password, confirm...)
        public bool PendingSecret;
        public string PendingPrompt = "";
        public object Busy;                  // a running ping / traceroute
        public bool Loading;                 // replaying a saved configuration: no chatter
        public bool Started;
        public int Errors;
        public int Version;
        public bool Exited;

        public CliSession(Device d) { D = d; d.Ios.Sessions.Add(this); }

        public void Print(string s)
        {
            if (Loading) return;
            Output.Append(s.Replace("\r\n", "\n"));
            if (!s.EndsWith("\n")) Output.Append('\n');
            Version++;
        }
        public void Write(string s) { if (Loading) return; Output.Append(s); Version++; }
        public string TakeOutput() { string s = Output.ToString(); Output.Clear(); return s; }

        public string Prompt
        {
            get
            {
                if (Pending != null) return PendingPrompt;
                string h = D.Ios.Hostname;
                switch (Mode)
                {
                    case "user": return h + ">";
                    case "priv": return h + "#";
                    case "config": return h + "(config)#";
                    case "config-if": return h + "(config-if)#";
                    case "config-subif": return h + "(config-subif)#";
                    case "config-if-range": return h + "(config-if-range)#";
                    case "config-router": return h + "(config-router)#";
                    case "config-line": return h + "(config-line)#";
                    case "config-vlan": return h + "(config-vlan)#";
                    case "dhcp-config": return h + "(dhcp-config)#";
                    case "config-std-nacl": return h + "(config-std-nacl)#";
                    case "config-ext-nacl": return h + "(config-ext-nacl)#";
                }
                return h + ">";
            }
        }

        public bool InConfig { get { return Mode != "user" && Mode != "priv"; } }

        /// <summary>The opening text when someone connects (banner, login).</summary>
        public void Start()
        {
            if (Started) return;
            Started = true;
            var ios = D.Ios;
            Print("");
            if (ios.Banner.Length > 0) Print(ios.Banner);
            if (ios.Con.Login && ios.Con.Password.Length > 0)
            {
                Print("User Access Verification\n");
                AskPassword("Password: ", typed =>
                {
                    if (typed == ios.Con.Password) { return true; }
                    Print("% Login invalid\n");
                    return false;
                }, true);
            }
        }

        void AskPassword(string prompt, Func<string, bool> check, bool repeatUntilOk)
        {
            PendingPrompt = prompt;
            PendingSecret = true;
            int tries = 0;
            Pending = typed =>
            {
                tries++;
                if (check(typed)) { Pending = null; PendingSecret = false; return true; }
                if (repeatUntilOk || tries < 3) return false;   // ask again
                Pending = null; PendingSecret = false;
                Print("% Bad secrets\n");
                return true;
            };
        }

        public void Ask(string prompt, Func<string, bool> answer)
        {
            PendingPrompt = prompt;
            PendingSecret = false;
            Pending = typed => { Pending = null; answer(typed); return true; };
        }

        // ---------------------------------------------------------------- running a line
        /// <summary>Runs one typed line (the console has already shown the prompt and the text).</summary>
        public void Execute(string line)
        {
            if (Busy != null) return;
            if (Exited) { Exited = false; Started = false; Start(); return; }
            if (Pending != null) { var p = Pending; p(line); return; }
            if (line.Trim().Length > 0) { History.Add(line); if (History.Count > 50) History.RemoveAt(0); }
            if (line.TrimEnd().EndsWith("?")) { Help(line); return; }
            string t = line.Trim();
            if (t.Length == 0) return;
            if (t.StartsWith("!")) return;
            int offset = line.Length - line.TrimStart().Length;
            RunIn(Mode, t, offset);
        }

        void RunIn(string mode, string text, int offset)
        {
            var toks = Tokenize(text, offset);
            if (toks.Count == 0) return;
            var tree = CliCommands.Tree(mode, D);
            var ctx = new CliCtx { S = this, Line = text };
            int start = 0;
            // "no" and "do"
            if (InConfig && toks.Count > 0 && "no".StartsWith(toks[0].Item1.ToLowerInvariant()) && toks[0].Item1.Length >= 1 && Matches(tree.Root, toks[0].Item1).Count == 0)
            {
                ctx.No = true; start = 1;
            }
            else if (InConfig && toks[0].Item1.ToLowerInvariant() == "no") { ctx.No = true; start = 1; }
            if (InConfig && !ctx.No && toks[0].Item1.ToLowerInvariant() == "do")
            {
                if (toks.Count == 1) { Print("% Incomplete command.\n"); return; }
                string rest = text.Substring(toks[1].Item2 - offset);
                string saved = Mode;
                Mode = "priv";
                try { RunIn("priv", rest, toks[1].Item2); }
                finally { if (Mode == "priv") Mode = saved; }
                return;
            }
            if (ctx.No && start >= toks.Count) { Print("% Incomplete command.\n"); return; }
            // a global command typed in a sub-mode (interface, router...) leaves that mode, as on the real thing
            if (mode != "config" && InConfig && Matches(tree.Root, toks[start].Item1).Count == 0)
            {
                var g = CliCommands.Tree("config", D);
                if (Matches(g.Root, toks[start].Item1).Count > 0)
                {
                    Mode = "config"; If = null; Range = null; Acl = null;
                    RunIn("config", text, offset);
                    return;
                }
            }
            var node = tree.Root;
            for (int i = start; i < toks.Count; i++)
            {
                string tok = toks[i].Item1;
                // "interface g 0/0": a type and a number as two words
                var m = Matches(node, tok);
                if (m.Count == 0 && i + 1 < toks.Count && tok.All(char.IsLetter))
                {
                    string joined = tok + toks[i + 1].Item1;
                    var m2 = node.Kids.Where(k => k.Word == null && (k.Arg == "iface" || k.Arg == "ifrange") && k.Accepts(joined)).ToList();
                    if (m2.Count > 0) { m = m2; tok = joined; i++; }
                }
                if (m.Count == 0)
                {
                    if (mode == "user" || mode == "priv")
                    {
                        if (i == 0 && toks.Count == 1) { UnknownWord(tok); return; }
                    }
                    Caret(toks[i].Item2);
                    return;
                }
                if (m.Count > 1)
                {
                    Print("% Ambiguous command:  \"" + text + "\"\n");
                    return;
                }
                var n = m[0];
                if (n.Word != null) ctx.Words.Add(n.Word);
                else if (n.Arg == "line")
                {
                    ctx.Args.Add(text.Substring(toks[i].Item2 - offset));
                    node = n;
                    break;
                }
                else ctx.Args.Add(tok);
                node = n;
            }
            if (node.Run == null)
            {
                Print("% Incomplete command.\n");
                return;
            }
            try { node.Run(ctx); }
            catch (Exception ex) { Print("% " + ex.Message + "\n"); Errors++; }
            D.P.Touch();
        }

        void UnknownWord(string tok)
        {
            if (D.Ios.DomainLookup)
            {
                Print("Translating \"" + tok + "\"...domain server (255.255.255.255)\n% Unknown command or computer name, or unable to find computer address\n");
            }
            else Print("% Unknown command or computer name, or unable to find computer address\n");
            Errors++;
        }

        void Caret(int pos)
        {
            Print(new string(' ', Prompt.Length + pos) + "^\n% Invalid input detected at '^' marker.\n");
            Errors++;
        }

        static List<CliNode> Matches(CliNode node, string tok)
        {
            string low = tok.ToLowerInvariant();
            var kw = node.Kids.Where(k => k.Word != null && !k.Hidden && k.Word.StartsWith(low)).ToList();
            var exact = node.Kids.FirstOrDefault(k => k.Word == low);
            if (exact != null) return new List<CliNode> { exact };
            if (kw.Count > 0) return kw;
            var hiddenExact = node.Kids.Where(k => k.Word != null && k.Hidden && k.Word.StartsWith(low)).ToList();
            if (hiddenExact.Count == 1) return hiddenExact;
            return node.Kids.Where(k => k.Word == null && k.Accepts(tok)).Take(1).ToList();
        }

        static List<Tuple<string, int>> Tokenize(string text, int offset)
        {
            var r = new List<Tuple<string, int>>();
            int i = 0;
            while (i < text.Length)
            {
                while (i < text.Length && text[i] == ' ') i++;
                if (i >= text.Length) break;
                int st = i;
                while (i < text.Length && text[i] != ' ') i++;
                r.Add(Tuple.Create(text.Substring(st, i - st), st + offset));
            }
            return r;
        }

        // ---------------------------------------------------------------- ? and Tab
        /// <summary>The answer to "?": the words that can come next (or that start with what was typed).</summary>
        public void Help(string line)
        {
            string text = line.TrimEnd();
            text = text.Substring(0, text.Length - 1);
            bool partial = text.Length > 0 && text[text.Length - 1] != ' ';
            var toks = Tokenize(text, 0);
            string mode = Mode;
            var tree = CliCommands.Tree(mode, D);
            int start = 0;
            if (InConfig && toks.Count > 0 && toks[0].Item1.ToLowerInvariant() == "do" && (toks.Count > 1 || !partial)) { tree = CliCommands.Tree("priv", D); start = 1; }
            else if (InConfig && toks.Count > 0 && toks[0].Item1.ToLowerInvariant() == "no" && (toks.Count > 1 || !partial)) start = 1;
            var node = tree.Root;
            int upto = partial ? toks.Count - 1 : toks.Count;
            string lineRest = null;
            for (int i = start; i < upto; i++)
            {
                var m = Matches(node, toks[i].Item1);
                if (m.Count != 1) { Print("% Unrecognized command\n"); return; }
                node = m[0];
                if (node.Arg == "line") { lineRest = text.Substring(toks[i].Item2); break; }
            }
            CliNode lineNode = node.Arg == "line" ? node : (node.Kids.Count == 1 && node.Kids[0].Arg == "line" ? node.Kids[0] : null);
            if (lineNode != null && lineNode.HelpFn != null)
            {
                if (lineRest == null) lineRest = partial ? toks[toks.Count - 1].Item1 : "";
                else if (!partial && !lineRest.EndsWith(" ")) lineRest += " ";
                Print(lineNode.HelpFn(this, lineRest));
                return;
            }
            if (node.Arg == "line") { Print("  LINE  Text\n  <cr>\n"); return; }
            var sb = new StringBuilder();
            if (partial)
            {
                string pre = toks[toks.Count - 1].Item1.ToLowerInvariant();
                var words = node.Kids.Where(k => k.Word != null && !k.Hidden && k.Word.StartsWith(pre)).Select(k => k.Word).ToList();
                if (words.Count == 0 && node.Kids.Any(k => k.Word == null && k.Accepts(toks[toks.Count - 1].Item1))) words.Add(node.Kids.First(k => k.Word == null).ArgHelp);
                if (words.Count == 0) { Print("% Unrecognized command\n"); return; }
                Print(string.Join("  ", words.ToArray()) + "\n");
                return;
            }
            var kids = node.Kids.Where(k => !k.Hidden).ToList();
            int w = 0;
            foreach (var k in kids) w = Math.Max(w, (k.Word ?? k.ArgHelp).Length);
            w = Math.Max(w + 2, 14);
            foreach (var k in kids.OrderBy(k => k.Word == null ? "~" + k.ArgHelp : k.Word))
                sb.Append("  " + (k.Word ?? k.ArgHelp).PadRight(w) + (k.Help.Length > 0 ? k.Help : DefaultArgHelp(k)) + "\n");
            if (node.Run != null && node != tree.Root) sb.Append("  <cr>\n");
            if (sb.Length == 0) { Print("% Unrecognized command\n"); return; }
            Print(sb.ToString());
        }

        static string DefaultArgHelp(CliNode k)
        {
            switch (k.Arg)
            {
                case "ip": return "IP address";
                case "mask": return "Network mask";
                case "wild": return "Wildcard bits";
                case "iface": return "Interface name (for example g0/0 or fa0/1)";
                case "word": return "A name or word";
                case "line": return "Text";
                case "port": return "Port number or name (www, domain...)";
                case "vlanlist": return "VLAN numbers, for example 10,20 or 10-20";
            }
            return "";
        }

        /// <summary>Tab: completes the last word when only one command fits. Returns the new line.</summary>
        public string Complete(string line)
        {
            if (line.Length == 0 || line.EndsWith(" ")) return line;
            var toks = Tokenize(line, 0);
            var tree = CliCommands.Tree(Mode, D);
            int start = 0;
            if (InConfig && toks.Count > 1 && (toks[0].Item1.ToLowerInvariant() == "do")) { tree = CliCommands.Tree("priv", D); start = 1; }
            else if (InConfig && toks.Count > 1 && toks[0].Item1.ToLowerInvariant() == "no") start = 1;
            var node = tree.Root;
            for (int i = start; i < toks.Count - 1; i++)
            {
                var m = Matches(node, toks[i].Item1);
                if (m.Count != 1) return line;
                node = m[0];
            }
            string last = toks[toks.Count - 1].Item1.ToLowerInvariant();
            var c = node.Kids.Where(k => k.Word != null && !k.Hidden && k.Word.StartsWith(last)).ToList();
            var exact = c.FirstOrDefault(k => k.Word == last);
            if (c.Count != 1 && exact == null)
            {
                // interface names: complete the type (fa -> FastEthernet)
                if (node.Kids.Any(k => k.Arg == "iface" || k.Arg == "ifrange"))
                {
                    string n = Ios.NormalizeIfName(toks[toks.Count - 1].Item1);
                    if (n != null) return line.Substring(0, toks[toks.Count - 1].Item2) + n;
                    foreach (var ty in Ios.IfTypes) if (ty.ToLowerInvariant().StartsWith(last) && last.Length > 0) return line.Substring(0, toks[toks.Count - 1].Item2) + ty;
                }
                return line;
            }
            var pick = exact ?? c[0];
            return line.Substring(0, toks[toks.Count - 1].Item2) + pick.Word + " ";
        }

        /// <summary>Everything Tab could put in place of the word being typed (keywords, interface names), for the
        /// terminal: one match completes it, several complete their common start and a second Tab lists them.</summary>
        public Completion Candidates(string line)
        {
            var r = new Completion { Start = line.Length };
            bool atSpace = line.Length == 0 || line.EndsWith(" ");
            var toks = Tokenize(line, 0);
            var tree = CliCommands.Tree(Mode, D);
            int start = 0;
            if (InConfig && toks.Count > (atSpace ? 0 : 1) && toks[0].Item1.ToLowerInvariant() == "do") { tree = CliCommands.Tree("priv", D); start = 1; }
            else if (InConfig && toks.Count > (atSpace ? 0 : 1) && toks[0].Item1.ToLowerInvariant() == "no") start = 1;
            var node = tree.Root;
            int upto = atSpace ? toks.Count : toks.Count - 1;
            for (int i = start; i < upto; i++)
            {
                var m = Matches(node, toks[i].Item1);
                if (m.Count != 1) return r;
                node = m[0];
                if (node.Arg == "line") return r;
            }
            string typed = atSpace ? "" : toks[toks.Count - 1].Item1;
            if (!atSpace) r.Start = toks[toks.Count - 1].Item2;
            string low = typed.ToLowerInvariant();
            foreach (var k in node.Kids.Where(k => k.Word != null && !k.Hidden && k.Word.StartsWith(low))) r.Words.Add(k.Word);
            if (typed.Length > 0 && node.Kids.Any(k => k.Arg == "iface" || k.Arg == "ifrange"))
            {
                string n = Ios.NormalizeIfName(typed);
                var names = D.Ports.Where(p => !p.IsWireless && !p.IsSerial).Select(p => p.Name).Concat(D.Ip != null ? D.Ip.Ifs.Select(f => f.Name) : Enumerable.Empty<string>()).Distinct().ToList();
                string real = n == null ? null : names.FirstOrDefault(x => string.Equals(x, n, StringComparison.OrdinalIgnoreCase));
                if (real != null) { r.Words.Clear(); r.Words.Add(real); return r; }
                // g0/ or gi: the names that start like that (the short form counts as its full name)
                string longForm = typed;
                foreach (var ty in Ios.IfTypes)
                {
                    int k = 0;
                    while (k < typed.Length && char.IsLetter(typed[k])) k++;
                    if (k > 0 && ty.StartsWith(typed.Substring(0, k), StringComparison.OrdinalIgnoreCase)) { longForm = ty + typed.Substring(k); break; }
                }
                foreach (var nm in names) if (nm.StartsWith(typed, StringComparison.OrdinalIgnoreCase) || nm.StartsWith(longForm, StringComparison.OrdinalIgnoreCase)) r.Words.Add(nm);
                if (r.Words.Count == 0 && n != null) r.Words.Add(n);
                if (r.Words.Count == 0) foreach (var ty in Ios.IfTypes) if (ty.StartsWith(typed, StringComparison.OrdinalIgnoreCase)) { r.Words.Add(ty); r.NoSpace.Add(ty); }
            }
            r.Words = r.Words.Distinct().ToList();
            return r;
        }

        /// <summary>Ctrl+Z or "end": back to privileged mode.</summary>
        public void End()
        {
            if (InConfig) { Mode = "priv"; If = null; Range = null; Acl = null; }
        }

        /// <summary>Ctrl+C / Ctrl+Shift+6: stops a ping or traceroute.</summary>
        public void Break()
        {
            var p = Busy as PingJob; if (p != null) p.Stop();
            var t = Busy as TraceJob; if (t != null) t.Stop();
            if (Pending != null) { Pending = null; PendingSecret = false; }
        }
    }
}
