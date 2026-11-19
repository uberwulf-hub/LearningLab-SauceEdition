// Cert Lab - checks of the AZ-800 course's pretend network: the directory and replication between DCs and sites,
// Group Policy processing (LSDOU, link order, Block Inheritance, Enforced, security and WMI filtering, loopback), the
// console actions and the check language. Later files add DNS / DHCP, files and permissions, storage, Hyper-V, the
// Azure side, the cmdlets and the course content.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CertLab
{
    public static partial class Tests
    {
        static IEnumerable<KeyValuePair<string, Action>> WsGroups()
        {
            yield return new KeyValuePair<string, Action>("ws-ad", WsAdTests);
            yield return new KeyValuePair<string, Action>("ws-repl", WsReplTests);
            yield return new KeyValuePair<string, Action>("ws-gpo", WsGpoTests);
            foreach (var g in WsMoreGroups()) yield return g;
        }
        static partial void WsGroupsMore(List<KeyValuePair<string, Action>> l);
        static IEnumerable<KeyValuePair<string, Action>> WsMoreGroups() { var l = new List<KeyValuePair<string, Action>>(); WsGroupsMore(l); return l; }

        /// <summary>A fresh AZ-800 network: Do runs a console action that must work, Bad one that must fail, Yes / No check lines,
        /// Ps runs PowerShell on a machine's console and returns what it printed.</summary>
        class W
        {
            public WsSim S = WsSim.Create(5);
            public WsLab L { get { return S.Lab; } }
            public WsResult Last;
            public WsResult Do(string line) { Last = WsOps.Run(L, line); Check("console: " + line, Last.Ok, Last.Text); return Last; }
            public bool Bad(string line, string expect)
            {
                Last = WsOps.Run(L, line);
                bool ok = !Last.Ok && (expect == null || Last.Text.IndexOf(expect, StringComparison.OrdinalIgnoreCase) >= 0);
                Check("refused: " + line + (expect == null ? "" : " (" + expect + ")"), ok, Last.Text);
                return ok;
            }
            public bool Is(string a) { string o; return WsCheck.Run(L, a, out o); }
            public void Yes(string a) { string o; Check(a, WsCheck.Run(L, a, out o), o); }
            public void No(string a) { string o; Check("not: " + a, !WsCheck.Run(L, a, out o), "it holds"); }
            public string Ps(string machine, string line) { var c = S.Console(machine); if (!c.Waiting) { c.Start(); c.TakeOutput(); } c.Execute(line); return c.TakeOutput(); }
            public void Wait(long s) { L.Advance(s); }
            public void Dispose() { S.Dispose(); }
        }

        static void WsAdTests()
        {
            var w = new W();
            Check("the lab starts with seven machines", w.S.MachineNames.Count == 7, string.Join(",", w.S.MachineNames.ToArray()));
            Check("both DCs hold the directory", w.L.Dirs.Count == 2);
            w.Yes("user[\"bcole\"].department == Sales");
            w.Yes("user[\"bcole\"].ou == \"OU=Sales,OU=Staff,DC=corp,DC=example\"");
            w.Yes("group[\"Sales\"].members has bcole");
            w.Yes("group[\"FS-Sales-Modify\"].allmembers has bcole");
            w.Yes("user[\"bcole\"].allgroups has FS-Sales-Modify");
            w.Yes("ou[\"Staff/Sales\"].protected == Yes");
            w.Yes("fsmo.PDCEmulator == DC1");
            w.Do("new user name=\"Lena Ortiz\" sam=lortiz path=Staff/Sales password=\"Harbor#2027\" department=Sales");
            w.Yes("user[\"lortiz\"].enabled == Yes");
            w.Bad("new user name=\"Weak Pass\" sam=weak path=Staff/Sales password=abc", "length");
            w.Do("new group name=\"Project Atlas\" scope=Global path=Groups");
            w.Do("members add \"Project Atlas\" lortiz bcole");
            w.Yes("group[\"Project Atlas\"].members has lortiz");
            w.Bad("members add \"Sales\" FS-Sales-Modify", "global group");
            w.Do("new group name=\"Atlas Readers\" scope=DomainLocal path=Groups");
            w.Do("members add \"Atlas Readers\" \"Project Atlas\"");
            w.Bad("set group \"Atlas Readers\" scope=Global", "Universal first");
            w.Bad("delete ou Staff/Sales", "protected");
            w.Do("set ou Staff/Sales protect=no");
            w.Bad("delete ou Staff/Sales", "child objects");
            w.Do("move user lortiz Staff/Finance");
            w.Yes("user[\"lortiz\"].path == corp.example/Staff/Finance");
            w.Yes("group[\"Project Atlas\"].members has lortiz");
            w.Do("delete user lortiz");
            w.No("user[\"lortiz\"]");
            w.Bad("restore user lortiz", "Recycle Bin");
            w.Do("enable recyclebin");
            w.Do("delete user dpatel");
            w.Do("restore user dpatel");
            w.Yes("group[\"Sales\"].members has dpatel");
            w.Do("fsmo transfer PDCEmulator DC2");
            w.Yes("fsmo.PDCEmulator == DC2");
            w.S.Directive("DC2", "#power off");
            w.Bad("fsmo transfer RIDMaster DC2", "isn't running");
            w.S.Directive("DC2", "#power on");
            w.S.Directive("DC1", "#power off");
            w.Bad("fsmo transfer RIDMaster DC2", "seize");
            w.Do("fsmo seize RIDMaster DC2");
            w.Yes("dc[\"DC2\"].roles has RIDMaster");
            w.Dispose();
        }

        static void WsReplTests()
        {
            var w = new W();
            w.Do("new user name=\"Rita Stone\" sam=rstone path=Staff/HR password=\"Harbor#2027\" department=HR server=DC1");
            w.Yes("@DC1 user[\"rstone\"]");
            w.No("@DC2 user[\"rstone\"]");
            w.Wait(20);
            w.Yes("@DC2 user[\"rstone\"]");
            // a branch site whose link replicates every 15 minutes
            w.Do("new site name=Branch link=DEFAULTIPSITELINK");
            w.Do("new subnet name=10.20.0.0/24 site=Branch");
            w.Do("move dc DC2 Branch");
            w.Do("replicate all");
            w.Yes("dc[\"DC2\"].site == Branch");
            var gs = w.Ps("DC2", "nltest /dsgetsite");
            Check("nltest /dsgetsite names the DC's site", gs.Trim().StartsWith("Branch"), gs);
            w.Do("set user rstone department=Payroll server=DC1");
            w.Wait(20);
            w.Yes("@DC2 user[\"rstone\"].department == HR");
            var due = w.L.NextReplTime("DC1", "DC2");
            Check("between sites the change waits for the link's schedule (180 minutes)", due.HasValue && (due.Value - w.L.Now).TotalMinutes > 1 && (due.Value - w.L.Now).TotalMinutes <= 180, due.ToString());
            w.Wait(3 * 3600);
            w.Yes("@DC2 user[\"rstone\"].department == Payroll");
            w.Do("set sitelink DEFAULTIPSITELINK interval=15");
            w.Bad("set sitelink DEFAULTIPSITELINK interval=5", "15 minutes");
            w.Do("set user rstone title=Clerk server=DC1");
            w.Wait(16 * 60);
            w.Yes("@DC2 user[\"rstone\"].title == Clerk");
            // a site in no link gets nothing
            w.Do("new site name=Lab");
            w.Do("set sitelink DEFAULTIPSITELINK -sites=Branch");
            w.Do("set user rstone title=Lead server=DC1");
            w.Wait(4 * 3600);
            w.Yes("@DC2 user[\"rstone\"].title == Clerk");
            w.Do("replicate DC2 from=DC1");
            w.Yes("@DC2 user[\"rstone\"].title == Lead");
            w.Yes("replicated(DC1, DC2) == Yes");
            w.Do("set user rstone title=Boss server=DC1");
            var rr = w.Ps("DC1", "repadmin /replicate DC2 DC1 DC=corp,DC=example");
            Check("repadmin /replicate pulls the change", rr.Contains("completed successfully") && w.Is("@DC2 user[\"rstone\"].title == Boss"), rr);
            w.Dispose();
        }
    }
}
