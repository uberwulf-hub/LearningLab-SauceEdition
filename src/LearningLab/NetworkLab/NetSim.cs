// Network Lab - runs the simulation for the page (App Hub's SimDriver): Realtime mode (the clock follows real time,
// things just work) and Simulation mode (time stops; step forward and back, envelopes move along the cables, an event
// list and what each device decided). The timer only moves the simulation on the UI thread.
using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Threading;
using NetworkLab;

namespace LearningLab.NetworkLab;

public sealed class NetSim
{
    public Project P;
    public NetCanvas View;
    public bool Simulation { get; private set; }
    public bool Playing;
    /// <summary>The animation speed in Simulation mode.</summary>
    public double Speed = 1;
    public readonly HashSet<string> Visible = new() { "ICMP", "ARP", "DHCP", "DNS", "HTTP", "TCP", "SMB", "PRINT", "SSH", "LLMNR", "MDNS", "RIP", "UDP" };
    public readonly ObservableCollection<SimEvent> Events = new();
    readonly List<List<SimEvent>> slots = new();
    public int Cursor = -1;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    readonly Stopwatch clock = Stopwatch.StartNew();
    long lastTick, nextAutoStep;
    int lastVersion = -1;
    double anim = 1;
    public event Action Stepped, Ticked;
    public string LastMessage = "";

    public NetSim() { timer.Tick += (s, e) => { try { Tick(); } catch (Exception ex) { LastMessage = ex.Message; } }; }

    public void Start() { lastTick = clock.ElapsedMilliseconds; timer.Start(); }
    public void Stop() => timer.Stop();
    public bool Running => timer.IsEnabled;

    public void Attach(Project p)
    {
        P = p;
        slots.Clear(); Events.Clear(); Cursor = -1;
        if (View != null) { View.P = p; View.Envelopes = new List<SimEvent>(); View.InvalidateVisual(); }
        P.Sim.Capture = Simulation;
    }

    public void SetSimulation(bool on)
    {
        Simulation = on;
        Playing = false;
        P.Sim.Capture = on;
        if (!on) { View.Envelopes = new List<SimEvent>(); View.Picked = null; View.InvalidateVisual(); }
        if (on) { slots.Clear(); Events.Clear(); Cursor = -1; P.Sim.Log.Clear(); }
        Stepped?.Invoke();
    }

    bool IsVisible(SimEvent e) => Visible.Contains(e.Type) || (Visible.Contains("UDP") && !Sim.Protocols.Contains(e.Type));

    void Tick()
    {
        long now = clock.ElapsedMilliseconds;
        long dt = Math.Min(250, now - lastTick);
        lastTick = now;
        if (P == null) return;
        if (!Simulation) P.Sim.RunFor(dt);
        else if (anim < 1)
        {
            anim = Math.Min(1, anim + dt / (700.0 / Speed));
            View.EnvProgress = anim;
            View.InvalidateVisual();
            if (anim >= 1) nextAutoStep = now + (long)(250 / Speed);
        }
        else if (Playing && now >= nextAutoStep && !Forward()) { Playing = false; Stepped?.Invoke(); }
        if (P.Version != lastVersion || now % 500 < 40) { lastVersion = P.Version; View?.InvalidateVisual(); }
        Ticked?.Invoke();
    }

    /// <summary>The next step: replays a later step already seen, or runs the network to the next visible moment.</summary>
    public bool Forward()
    {
        if (!Simulation) return false;
        if (Cursor < slots.Count - 1) { Cursor++; Show(true); return true; }
        var evs = P.Sim.StepSlot(IsVisible, 120000);
        if (evs.Count == 0) { LastMessage = "Nothing more is happening in the network. Send something (a ping from a PC, the envelope, a web page) and press Play or Forward."; Stepped?.Invoke(); return false; }
        slots.Add(evs);
        foreach (var e in evs) Events.Add(e);
        Cursor = slots.Count - 1;
        Show(true);
        return true;
    }

    public void Back() { if (Cursor <= 0) return; Cursor--; Show(false); }

    void Show(bool animate)
    {
        var evs = Current;
        View.Envelopes = evs;
        View.Picked = evs.FirstOrDefault();
        anim = animate ? 0 : 1;
        View.EnvProgress = anim;
        View.InvalidateVisual();
        LastMessage = evs.Count == 0 ? "" : "Time " + evs[0].TimeText + " s: " + evs.Count + " event" + (evs.Count == 1 ? "" : "s") + " (step " + (Cursor + 1) + " of " + slots.Count + ").";
        Stepped?.Invoke();
    }

    /// <summary>The checks: finishes the envelopes' movement at once.</summary>
    public void FinishAnimation() { anim = 1; if (View != null) { View.EnvProgress = 1; View.InvalidateVisual(); } }

    public List<SimEvent> Current => Cursor >= 0 && Cursor < slots.Count ? slots[Cursor] : new List<SimEvent>();

    /// <summary>Forgets the recorded steps (the network itself carries on as it is).</summary>
    public void Reset()
    {
        slots.Clear(); Events.Clear(); Cursor = -1; P.Sim.Log.Clear();
        View.Envelopes = new List<SimEvent>(); View.Picked = null;
        View.InvalidateVisual();
        Stepped?.Invoke();
    }

    /// <summary>The envelope: one ping from a device to another (shown step by step in Simulation mode).</summary>
    public void SimplePdu(Device from, Device to, Action<string> done)
    {
        var f = to.Ip?.Ifs.FirstOrDefault(x => x.Ip != 0 && x.Up && !x.IsLoopback);
        if (to.Kind == "homerouter") f = HomeRouter.Lan(to);
        if (f == null || f.Ip == 0) { done(to.Name + " has no IP address yet, so there is nothing to send to."); return; }
        if (from.Ip == null || from.Ip.Ifs.All(x => x.Ip == 0)) { done(from.Name + " has no IP address yet."); return; }
        PingJob.Start(from, Ip.Str(f.Ip), 1, j =>
        {
            j.TimeoutMs = 2000;
            j.OnDone = jj => done(jj.Received > 0 ? "Ping " + from.Name + " -> " + to.Name + " (" + Ip.Str(f.Ip) + "): successful." : "Ping " + from.Name + " -> " + to.Name + " (" + Ip.Str(f.Ip) + "): failed. (The first ping through a router often fails while the router finds the next MAC address with ARP: try again. Simulation mode shows where it stops.)");
        });
        if (Simulation && !Playing) Forward();
    }

    /// <summary>The explanation of one event for the details box.</summary>
    public static string Details(SimEvent e)
    {
        if (e == null) return "";
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("At " + e.At + (e.Origin ? " (it starts here)" : ", from " + e.Last) + ", time " + e.TimeText + " s");
        sb.AppendLine();
        if (e.Notes.Count > 0)
        {
            sb.AppendLine("What " + e.At + " does:");
            foreach (var n in e.Notes) sb.AppendLine("  - " + n);
            sb.AppendLine();
        }
        if (e.Frame != null)
        {
            sb.AppendLine("Inside the envelope (outside layer first):");
            foreach (var layer in e.Frame.Layers())
            {
                sb.AppendLine("  " + layer[0]);
                for (int i = 1; i < layer.Length; i++) sb.AppendLine("      " + layer[i]);
            }
        }
        return sb.ToString();
    }
}
