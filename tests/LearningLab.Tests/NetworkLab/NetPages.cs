// Network Lab - the screens, drawn off screen with Avalonia's headless platform (group "network"): the page, the canvas
// and its tools, a PC's settings and Command Prompt, a router's command line (?, Tab, history), a lab from start to
// "Do it for me", Simulation mode stepping an envelope, the autosave, and the same pages phone-sized.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using LearningLab.NetworkLab;
using LearningLab.Views;
using NetworkLab;

namespace LearningLab.Tests;

static class NetPages
{
    static void Check(string what, bool ok, string detail = null) => Program.Check("network pages: " + what, ok, detail);
    static Window win;
    static void Pump() => Dispatcher.UIThread.RunJobs();
    static void Snap(string name) => LearningLab.Tests.Snap.Save(win, name);

    public static void Run()
    {
        string data = Data.For("NetworkLab");
        foreach (var f in Directory.GetFiles(data)) File.Delete(f);
        win = new Window { Width = 1180, Height = 800, Content = new MainView() };
        win.Show();
        var mv = MainView.Current;
        try { Pages(mv); }
        finally { win.Close(); }
    }

    static Avalonia.Point At(NetLabPage pg, Device d) => pg.Canvas.Center(d);

    static void Pages(MainView mv)
    {
        var pg = new NetLabPage();
        mv.Push(pg); Pump();
        pg.Sim.Stop();   // the checks move the clock themselves
        Snap("net-empty");
        Check("a new, empty network to start", pg.P.Devices.Count == 0);

        // ---- building: two PCs and a switch, cabled with the Cable tool (taps on the canvas)
        var pc1 = pg.AddDevice("pc"); var pc2 = pg.AddDevice("pc"); var sw = pg.AddDevice("switch");
        sw.X = 300; sw.Y = 100; pc1.X = 150; pc1.Y = 260; pc2.X = 450; pc2.Y = 260;
        pg.Canvas.Fit(); Pump();
        pg.SetTool("connect");
        pg.Canvas.ConnectTap(At(pg, pc1)); pg.Canvas.ConnectTap(At(pg, sw));
        pg.Canvas.ConnectTap(At(pg, pc2)); pg.Canvas.ConnectTap(At(pg, sw));
        Check("the Cable tool connects two devices (two taps), ports picked", pg.P.Links.Count == 2 && pc1.Ports.Any(p => p.Links.Count > 0) && sw.Ports.Count(p => p.Links.Count > 0) == 2, pg.StatusText);
        pg.SetTool("select");
        pg.P.Sim.RunFor(3000);
        Pump(); Snap("net-built");
        Check("Undo takes the last cable away, Redo puts it back", Undo(pg, () => pg.P.Links.Count == 1, () => pg.P.Links.Count == 2));
        Check("hitting things on the canvas: devices and cables", pg.Canvas.HitDevice(At(pg, sw)) == sw && pg.Canvas.HitLink(new Point((At(pg, pc1).X + At(pg, sw).X) / 2, (At(pg, pc1).Y + At(pg, sw).Y) / 2)) != null);

        // ---- a PC: Config (static address) and its Command Prompt
        var dp = new DevicePage(pc1, "Config"); mv.Push(dp); Pump();
        Check("a PC's page: Command Prompt, PowerShell or Terminal, Config, Physical", dp.Tabs.Contains("Config") && dp.Tabs.Contains("Physical") && dp.Tabs.Count >= 3, string.Join(",", dp.Tabs));
        var f1 = pc1.Host.Adapters.First(a => !a.Port.IsWireless);
        string msg = dp.IpForms[f1]("192.168.1.10", "255.255.255.0", "", "");
        Check("the IP form sets a static address", f1.Ip == Ip.Parse("192.168.1.10") && !f1.Dhcp, msg);
        Pump(); Snap("net-pc-config");
        mv.Back(); Pump();
        var f2 = pc2.Host.Adapters.First(a => !a.Port.IsWireless);
        pc2.Host.SetStatic(f2, "192.168.1.11", "255.255.255.0", "", "", "");
        pg.P.Sim.RunFor(2000);
        var shellTab = new DevicePage(pc1).Tabs.First(t => t is "Command Prompt" or "Command line" or "Terminal");
        dp = new DevicePage(pc1, shellTab); mv.Push(dp); Pump();
        dp.Shell.TypeLine("ping 192.168.1.11");
        for (int i = 0; i < 12; i++) { pg.P.Sim.RunFor(1000); dp.Shell.Poll(); }
        Pump(); Snap("net-pc-ping");
        Check("pinging from the PC's command line works through the switch", dp.Shell.Text.Contains("Reply from 192.168.1.11") || dp.Shell.Text.Contains("bytes from 192.168.1.11"), dp.Shell.Text.Substring(Math.Max(0, dp.Shell.Text.Length - 400)));
        mv.Back(); Pump();

        // ---- a router: the CLI with ?, Tab and history
        var r = pg.AddDevice("router");
        var rp = new DevicePage(r); mv.Push(rp); Pump();
        Check("a router's page opens on its CLI", rp.Tab == "CLI" && rp.Cli != null);
        rp.Cli.TypeLine("");
        rp.Cli.TypeLine("enable");
        rp.Cli.Typed = "conf"; rp.Cli.Tab(false);
        Check("Tab finishes a word on the router", rp.Cli.Typed.StartsWith("configure"), rp.Cli.Typed);
        rp.Cli.TypeLine("configure terminal");
        rp.Cli.TypeLine("hostname EDGE");
        rp.Cli.Poll(); Pump(); Snap("net-router-cli");
        Check("typing commands changes the router (hostname)", rp.Cli.Text.Contains("EDGE(config)#"), rp.Cli.Text.Substring(Math.Max(0, rp.Cli.Text.Length - 300)));
        rp.Cli.Typed = "zzz"; rp.Cli.Tab(false);
        Check("Tab with no match changes nothing", rp.Cli.Typed == "zzz" && rp.Cli.Flashes == 1);
        mv.Back(); Pump();

        // ---- Simulation mode: one envelope, step by step
        pg.SetSimulation(true); Pump();
        string pdu = null;
        pg.Sim.SimplePdu(pc1, pc2, m => pdu = m);
        int steps = 0;
        while (steps < 30 && pg.Sim.Forward()) { steps++; if (steps == 2) { pg.Sim.FinishAnimation(); Pump(); Snap("net-simulation"); } }
        Check("Simulation mode steps the ping across (envelopes, events, details)", steps >= 3 && pg.Sim.Events.Count >= 3 && NetSim.Details(pg.Sim.Events[0]).Length > 20, steps + " steps, " + pdu);
        Check("the ping arrives in Simulation mode", pg.Sim.Events.Any(e => e.Type == "ICMP" && e.At == pc2.Name));
        pg.SetSimulation(false);

        // ---- the autosave: closing keeps the network, a new page opens it again
        int devices = pg.P.Devices.Count;
        mv.Back(); Pump();
        Check("closing keeps the network in the autosave", File.Exists(Path.Combine(Data.For("NetworkLab"), "autosave.netlab")));
        var pg2 = new NetLabPage(); mv.Push(pg2); Pump(); pg2.Sim.Stop();
        Check("the network from last time is back", pg2.P.Devices.Count == devices && pg2.StatusText.Contains("last time"), pg2.StatusText);

        // ---- a lab from start to Do it for me
        var lab = Labs.Get("two-pcs");
        pg2.StartLab(lab); Pump();
        pg2.CheckGoals(); Pump(); Snap("net-lab-start");
        Check("a lab starts with its network and goals not done yet", pg2.Lab == lab && pg2.P.Devices.Count > 0 && pg2.Goals.Length == lab.Goals.Count && !pg2.Goals.All(x => x));
        Check("a hint", pg2.NextHint().Length > 0);
        var errs = pg2.DoAnswer(); Pump(); Snap("net-lab-done");
        Check("Do it for me solves the lab: every goal ticked", errs.Count == 0 && pg2.Goals.All(x => x), string.Join("; ", errs));
        mv.Back(); Pump();

        // ---- phone-sized
        win.Width = 400; win.Height = 840; Pump();
        var ph = new NetLabPage(); mv.Push(ph); Pump(); ph.Sim.Stop();
        Snap("net-phone");
        ph.StartLab(Labs.Get("switch-four")); Pump(); Snap("net-phone-lab");
        Check("on a phone the lab panel takes the screen; Back closes it", ph.PanelOpen && ph.OnBack() && !ph.PanelOpen);
        Pump(); Snap("net-phone-network");
        var pcp = new DevicePage(ph.P.Devices.First(d => d.Host != null), "Config"); mv.Push(pcp); Pump(); Snap("net-phone-pc-config");
        mv.Back(); Pump();
        var swp = ph.P.Devices.FirstOrDefault(d => d.Ios != null);
        if (swp != null) { var sp = new DevicePage(swp); mv.Push(sp); Pump(); sp.Cli.TypeLine(""); sp.Cli.TypeLine("show version"); sp.Cli.Poll(); Pump(); Snap("net-phone-cli"); mv.Back(); Pump(); }
        mv.Back(); Pump();
        win.Width = 1180; win.Height = 800;
    }

    static bool Undo(NetLabPage pg, Func<bool> afterUndo, Func<bool> afterRedo)
    {
        pg.Undo.Undo(); bool a = afterUndo();
        pg.Undo.Redo(); bool b = afterRedo();
        return a && b;
    }
}
