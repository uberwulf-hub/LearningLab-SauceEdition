// Network Lab - the Physical view, the port picker and the drawings, drawn off screen with Avalonia's headless platform
// (group "network-physical"): each layout's floor plans and campus, placing, moving and cabling devices with the port
// picker (cable lengths follow the plan), rooms, walls, doors, labels and racks, every kind of drawing on both views,
// handles that resize and turn, groups, lining up, copy and paste, undo, maps, the scale, layers, the panels and the
// objects library, saving and opening again; wide and phone-sized. Also App Hub's TestsDrawing.cs checks of the view
// parts (labels following devices, VLANs on links, layers, maps, the scale, saving, the picture of a view).
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using LearningLab.NetworkLab;
using LearningLab.Views;
using NetworkLab;
using Rect = Avalonia.Rect;

namespace LearningLab.Tests;

static class NetPhysicalPages
{
    static void Check(string what, bool ok, string detail = null) => Program.Check("network physical: " + what, ok, detail);
    static Window win;
    static void Pump() { Dispatcher.UIThread.RunJobs(); }
    static void Snap(string name) => LearningLab.Tests.Snap.Save(win, name);
    static readonly Queue<string> answers = new();

    public static void Run()
    {
        string data = Data.For("NetworkLab");
        foreach (var f in Directory.GetFiles(data)) File.Delete(f);
        NetAsk.Auto = (title, value) => answers.Count > 0 ? answers.Dequeue() : value;
        PortPanel.AutoModule = p => p.Sfp == "" ? "mmf" : "";
        win = new Window { Width = 1180, Height = 800, Content = new MainView() };
        win.Show();
        var mv = MainView.Current;
        try
        {
            ModelChecks();
            Pages(mv, false);
            win.Width = 400; win.Height = 840; Pump();
            Pages(mv, true);
        }
        finally
        {
            NetAsk.Auto = null; PortPanel.AutoModule = null;
            mv.PopTo(1);
            win.Close();
        }
    }

    static Project Build(string script)
    {
        var p = new Project(7);
        var errs = Script.Run(p, script);
        if (errs.Count > 0) throw new Exception(string.Join("; ", errs));
        p.Sim.RunFor(500);
        return p;
    }

    static byte[] TinyPng(byte r = 255, byte g = 0, byte b = 0)
    {
        using var wb = new WriteableBitmap(new PixelSize(4, 3), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var fb = wb.Lock())
        {
            var px = new byte[fb.RowBytes * 3];
            for (int y = 0; y < 3; y++) for (int x = 0; x < 4; x++) { int i = y * fb.RowBytes + x * 4; px[i] = b; px[i + 1] = g; px[i + 2] = r; px[i + 3] = 255; }
            System.Runtime.InteropServices.Marshal.Copy(px, 0, fb.Address, px.Length);
        }
        using var ms = new MemoryStream();
        wb.Save(ms);
        return ms.ToArray();
    }

    /// <summary>Renders a control alone and reads its pixels (BGRA).</summary>
    static (byte[] Px, int W, int H) Picture(Control c, int w, int h)
    {
        c.Measure(new Size(w, h)); c.Arrange(new Rect(0, 0, w, h));
        using var rtb = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96, 96));
        rtb.Render(c);
        var px = new byte[w * h * 4];
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(px, System.Runtime.InteropServices.GCHandleType.Pinned);
        try { rtb.CopyPixels(new PixelRect(0, 0, w, h), handle.AddrOfPinnedObject(), px.Length, w * 4); }
        finally { handle.Free(); }
        return (px, w, h);
    }
    static (byte R, byte G, byte B, byte A) PixelAt((byte[] Px, int W, int H) p, int x, int y) { int i = (y * p.W + x) * 4; return (p.Px[i + 2], p.Px[i + 1], p.Px[i], p.Px[i + 3]); }

    // ================================================================= the views' parts of App Hub's DrawingChecks
    static void ModelChecks()
    {
        // rotated shapes are hit where they are drawn
        var p = new Project(11);
        var u = new UndoStack();
        var view = new NetCanvas { P = p, Undo = u, Zoom = 1, Pan = new Vector(0, 0) };
        var tall = Drawings.Make(p, "rect", "logical", 500, 500, 200, 20);
        p.Shapes.Add(tall);
        Check("hit testing an upright shape", view.Draw.HitShape(tall, new Point(650, 510)) && !view.Draw.HitShape(tall, new Point(600, 580)));
        tall.Rotation = 90;
        Check("hit testing a turned shape", !view.Draw.HitShape(tall, new Point(680, 510)) && view.Draw.HitShape(tall, new Point(600, 580)));
        p.Shapes.Remove(tall);

        // labels on devices and cables follow them
        var lp = Build("add pc PC1 100 100\nadd switch SW1 300 100\ncable PC1 Fa0 SW1 Fa0/1");
        var lu = new UndoStack();
        var pc = lp.Find("PC1");
        var devLabel = Drawings.AddCallout(lp, lu, pc.Id, "logical", "Reception PC");
        var cab = Drawings.AddCallout(lp, lu, lp.Links[0].Id, "logical", "Uplink");
        IDrawHost lv = new NetCanvas { P = lp, Undo = lu, Zoom = 1, Pan = new Vector(0, 0) };
        lv.CalloutAnchor(devLabel, out var at0);
        lv.CalloutAnchor(cab, out var c0);
        var before = new Dictionary<object, Point2> { { pc, Edits.Pos(pc) } };
        Edits.Set(pc, new Point2(160, 220));
        Edits.Moved(lp, lu, before);
        lv.CalloutAnchor(devLabel, out var at1);
        lv.CalloutAnchor(cab, out var c1);
        Check("a device's label moves with it (its offset stays)", at1.X - at0.X == 60 && at1.Y - at0.Y == 120 && devLabel.DY == -48);
        Check("a cable's label stays at the middle of the cable", Math.Abs(c1.X - c0.X - 30) < 1e-9 && Math.Abs(c1.Y - c0.Y - 60) < 1e-9);
        Edits.Delete(lp, lu, new object[] { pc });
        Check("deleting a device takes its labels (and its cable's) with it", !lp.Callouts.Contains(devLabel) && !lp.Callouts.Contains(cab));
        lu.Undo();
        Check("undo brings the device and its labels back", lp.Callouts.Contains(devLabel) && lp.Callouts.Contains(cab) && lp.Devices.Contains(pc));
        var vp = Build(Labs.Get("vlans").Start + "\n" + Labs.Get("vlans").Answer);
        vp.Sim.RunFor(3000);
        var l1 = vp.Links.First(x => x.A.Dev.Name == "PC1" || x.B.Dev.Name == "PC1");
        var lr = vp.Links.First(x => x.A.Dev.Name == "R1" || x.B.Dev.Name == "R1");
        Check("Show VLANs on links: access VLAN and the router's tagged VLANs", Drawings.VlanText(l1) == "VLAN 10" && Drawings.VlanText(lr).Contains("802.1Q 10,20"), Drawings.VlanText(l1) + " | " + Drawings.VlanText(lr));

        // layers: hide and lock
        var yp = Build("add pc PC1 100 100");
        var yv = new NetCanvas { P = yp, Undo = new UndoStack(), Zoom = 1, Pan = new Vector(0, 0) };
        var under = Drawings.Make(yp, "rect", "logical", 300, 300, 100, 100); yp.Shapes.Add(under);
        Check("drawings and devices can be tapped", yv.Draw.HitBelow(new Point(350, 350)) == under && yv.HitDevice(new Point(100, 100)) != null);
        Drawings.LayersOf(yp, "logical").Locked.Add("drawings");
        Check("a locked layer lets taps through (its drawings can't be picked or moved)", yv.Draw.HitBelow(new Point(350, 350)) == null);
        Drawings.LayersOf(yp, "logical").Locked.Clear();
        Drawings.LayersOf(yp, "logical").Hidden.Add("drawings");
        Check("a hidden layer isn't drawn or tapped", yv.Draw.HitBelow(new Point(350, 350)) == null && !yv.Draw.LayerShows("drawings"));
        Drawings.LayersOf(yp, "logical").Hidden.Add("devices");
        Check("hiding the devices layer", yv.HitDevice(new Point(100, 100)) == null);
        Drawings.LayersOf(yp, "logical").Hidden.Clear();
        Drawings.LayersOf(yp, "logical").Locked.Add("devices");
        Check("a locked devices layer still opens devices when tapped", yv.HitDevice(new Point(100, 100)) != null && !yv.CanChange(yp.Find("PC1")));

        // maps: styles, and the scale feeding cable lengths on the campus
        var mp = new Project(14);
        var mu = new UndoStack();
        Drawings.ApplyMapStyle(mp, mu, "logical", "streets", new global::NetworkLab.Rect(0, 0, 1200, 800));
        int pieces = mp.Shapes.Count(s => s.FromStyle && s.Layer == "map");
        Check("a street map: its pieces are on the Map layer, locked", pieces > 10 && Drawings.MapOf(mp, "logical").Style == "streets" && Drawings.LayersOf(mp, "logical").IsLocked("map"), pieces.ToString());
        Drawings.ApplyMapStyle(mp, mu, "logical", "island", new global::NetworkLab.Rect(0, 0, 1200, 800));
        mu.Undo();
        Check("undo brings the old map back", Drawings.MapOf(mp, "logical").Style == "streets" && mp.Shapes.Count(s => s.FromStyle) == pieces);
        foreach (var st in new[] { "grid", "park", "none" }) Drawings.ApplyMapStyle(mp, mu, "campus", st, new global::NetworkLab.Rect(0, 0, 200, 120));
        Check("No map takes the drawn map away", !mp.Shapes.Any(s => s.View == "campus" && s.FromStyle) && Drawings.MapOf(mp, "campus").Style == "none");
        var fl = Labs.Get("fibre").Build();
        Script.Run(fl, Labs.Get("fibre").Answer);
        var fibre = fl.Links.First(l => l.IsFiber);
        double len0 = fibre.Length;
        double gx0 = fl.Layout.Buildings.First(b => b.Id == "gym").X;
        var fu = new UndoStack();
        Drawings.Calibrate(fl, fu, "campus", 100, 50);
        double len1 = fibre.Length;
        Check("setting the map's scale moves the buildings and changes the fibre's length", fl.Layout.Buildings.First(b => b.Id == "gym").X == gx0 / 2 && len1 < len0 * 0.6 && len1 > 80, len0 + " -> " + len1);
        fu.Undo();
        Check("undo puts the old scale and length back", Math.Abs(fibre.Length - len0) < 1e-6);

        // saving: everything round-trips; older files still open
        var sp = Labs.Get("vlans").Build();
        var su = new UndoStack();
        var img = Drawings.Make(sp, "image", "logical", 0, 0, 40, 30);
        img.Picture = Convert.ToBase64String(TinyPng());
        sp.Shapes.Add(img);
        var poly = Drawings.Make(sp, "freehand", "main/f1", 0, 0, 0, 0); poly.Points.AddRange(new[] { new Point2(1, 1), new Point2(2, 3.5), new Point2(4, 2) }); poly.Closed = true; poly.FitPoints(); sp.Shapes.Add(poly);
        sp.Shapes.Add(Drawings.MakeObject(sp, "desk", "main/f1", 5, 5));
        Drawings.ApplyMapStyle(sp, su, "logical", "park", new global::NetworkLab.Rect(0, 0, 800, 600));
        Drawings.LayersOf(sp, "campus").Hidden.Add("labels");
        Drawings.Calibrate(sp, su, "logical", 100, 20);
        string f1 = Persist.Save(sp);
        var sq = Persist.Load(f1);
        string f2 = Persist.Save(sq);
        Check("save -> open -> save gives the same file with drawings, labels, maps and layers", f1 == f2);
        Check("every kind of drawing comes back", sq.Shapes.Count == sp.Shapes.Count && sq.Callouts.Count == sp.Callouts.Count && sq.Shapes.Any(s => s.Kind == "image" && s.Picture == img.Picture) && sq.Shapes.First(s => s.Kind == "freehand").Points.Count == 3
           && Drawings.LayersOf(sq, "campus").Hidden.Contains("labels") && Drawings.MapOf(sq, "logical").Style == "park");
        var old = Json.ParseObj(f1);
        foreach (var k in new[] { "drawings", "labels", "maps", "layers" }) old.Remove(k);
        old["version"] = 1.0;
        var op = Persist.Load(Json.Write(old));
        Check("a version 1 file (before drawings) still opens", op.Shapes.Count == 0 && op.Callouts.Count == 0 && op.Devices.Count == sp.Devices.Count && op.Links.Count == sp.Links.Count);

        // decoration only: drawings never change the network or a lab's goals
        var lab = Labs.Get("vlans");
        var withD = lab.Build();
        var noD = lab.Build();
        noD.Shapes.Clear(); noD.Callouts.Clear();
        Script.Run(withD, lab.Answer); Script.Run(noD, lab.Answer);
        Check("drawings don't change a lab's goals", withD.Shapes.Count >= 2 && lab.Check(withD).SequenceEqual(lab.Check(noD)) && lab.Check(withD).All(x => x));
        Drawings.ApplyMapStyle(withD, null, "logical", "streets", new global::NetworkLab.Rect(0, 0, 900, 600));
        Check("...or the simulation (a street map under the network)", global::NetworkLab.Check.CanPing(withD, "PC1", "PC4"));

        // the picture of a view includes the drawings
        var pp = Build("add pc PC1 400 300");
        var pv = new NetCanvas { P = pp, Undo = new UndoStack(), Zoom = 1, Pan = new Vector(0, 0) };
        var mag = Drawings.Make(pp, "rect", "logical", 40, 40, 100, 60); mag.Fill = "#FF00FF"; mag.Stroke = "none"; pp.Shapes.Add(mag);
        var top = Drawings.Make(pp, "ellipse", "logical", 360, 260, 80, 80); top.Fill = "#00FF00"; top.Stroke = "none"; top.Above = true; pp.Shapes.Add(top);
        var pic = Picture(pv, 640, 480);
        var c = PixelAt(pic, 90, 70); var cTop = PixelAt(pic, 400, 300);
        Check("the picture of the view shows the drawings", c.R == 255 && c.G == 0 && c.B == 255, c.ToString());
        Check("a drawing above the devices covers them", cTop.G == 255 && cTop.R == 0, cTop.ToString());
        Drawings.SetMapPicture(pp, null, "logical", TinyPng(0, 0, 255), new global::NetworkLab.Rect(0, 0, 640, 480), 4, 3);
        var mpic = Picture(pv, 640, 480);
        var cm = PixelAt(mpic, 600, 440);
        Check("a picture as the map is drawn under the view", cm.B > 200 && cm.R < 60, cm.ToString());

        // the objects library: every object draws (some pixels) and is found by its words
        int drawn = 0;
        foreach (var a in ObjectCatalog.All)
        {
            var ip = Picture(new ObjectIcon(a.Kind, 40), 40, 40);
            int n = 0; for (int i = 3; i < ip.Px.Length; i += 4) if (ip.Px[i] > 0) n++;
            if (n > 40) drawn++; else Check("the object " + a.Kind + " draws", false, n + " pixels");
        }
        Check("the objects library: " + ObjectCatalog.All.Count + " objects in " + ObjectCatalog.Categories.Length + " groups, each drawn", drawn == ObjectCatalog.All.Count && ObjectCatalog.All.Count >= 35 && ObjectCatalog.Categories.All(cat => ObjectCatalog.All.Any(x => x.Category == cat)));
        var lib = new ObjectLibrary();
        int all = lib.Shown;
        lib.Search = "tree"; int trees = lib.Shown;
        lib.Search = "zzzz"; int none = lib.Shown;
        Check("the library's search box", all == ObjectCatalog.All.Count && trees >= 1 && trees < all && none == 0, all + " " + trees + " " + none);
    }

    // ================================================================= the pages
    static void Pages(MainView mv, bool phone)
    {
        string px = phone ? "netphys-phone-" : "netphys-";
        var pg = new NetLabPage();
        mv.Push(pg); Pump();
        pg.Sim.Stop();
        pg.Use(new Project(5), "");
        pg.ShowPhysical(true); Pump();
        Check("the Physical view is shown " + (phone ? "(phone)" : "(wide)"), pg.Physical && pg.Phys.IsVisible && !pg.Canvas.IsVisible && pg.Phys.Bounds.Width > 100, pg.Phys.Bounds.ToString());

        // ---- each layout: its floors and the campus
        foreach (var l in Layouts.List)
        {
            pg.ChangeLayout(l[0]); Pump();
            var floors = pg.P.Layout.AllFloors().ToList();
            Check(l[1] + ": the first floor's plan is shown", pg.Phys.Level == "floor" && pg.Phys.FloorKey == floors[0].Key && pg.Phys.CurrentFloor != null);
            Snap(px + "layout-" + l[0]);
            if (!phone || l[0] == "school")
            {
                pg.Phys.ShowCampus(); Pump(); Snap(px + "campus-" + l[0]);
                var b = pg.P.Layout.Buildings.Last();
                pg.Phys.Tap(pg.Phys.BuildingPoint(b));
                Check(l[1] + ": a tap on a building on the campus goes inside", pg.Phys.Level == "floor" && pg.Phys.CurrentFloor.B == b);
            }
            pg.Phys.ShowFloor(floors[0].Key);
        }
        pg.Undo.Undo(); Pump();
        Check("Undo brings the site from before back", pg.P.Layout.Id == "school", pg.P.Layout.Id);
        pg.ChangeLayout("office"); Pump();
        var floor = pg.P.Layout.Floor("main/f1");

        // ---- placing devices on the plan (the palette) and moving them with a finger
        var sw = pg.AddDevice("switch"); var pc1 = pg.AddDevice("pc"); var pc2 = pg.AddDevice("pc"); var ap = pg.AddDevice("ap");
        Pump();
        Check("devices added in the Physical view go on the floor shown", new[] { sw, pc1, pc2, ap }.All(d => d.Place == "main/f1" && d.PX > 0 && d.PY > 0));
        foreach (var (d, x, y) in new[] { (sw, 30.5, 12.0), (pc1, 4.0, 4.0), (pc2, 10.0, 20.0), (ap, 14.0, 12.0) }) { d.PX = x; d.PY = y; }
        pg.P.Sim.TopologyChanged();
        pg.Phys.Fit(); Pump();
        var start = pg.Phys.DevPoint(pc2);
        pg.Phys.DragFinger(start, new Point(start.X + 2 * pg.Phys.Scale, start.Y - 2 * pg.Phys.Scale));
        Check("dragging a device moves it on the plan (2 m right, 2 m up)", Math.Abs(pc2.PX - 12) < 0.05 && Math.Abs(pc2.PY - 18) < 0.05, pc2.PX + "," + pc2.PY);
        pg.Undo.Undo();
        Check("Undo puts it back", Math.Abs(pc2.PX - 10) < 0.01 && Math.Abs(pc2.PY - 20) < 0.01);
        bool opened = false;
        pg.Phys.DeviceOpened += d => opened |= d == pc1;
        int depth = mv.Depth;
        pg.Phys.Tap(pg.Phys.DevPoint(pc1)); Pump();
        Check("a tap on a device opens it", opened && mv.Page is DevicePage, mv.Page?.Title);
        if (mv.Depth > depth) { mv.Back(); Pump(); }

        // ---- cabling with the port picker
        pg.SetTool("connect");
        pg.Phys.ConnectTap(pg.Phys.DevPoint(pc1));
        var pick = pg.Phys.Picker;
        Check("the Cable tool shows the port picker for the device tapped", pick != null && pick.D == pc1 && pick.Picking);
        pick.TapAt(pick.PortCenter(pc1.Port("Fa0")));
        Check("tapping a port starts the cable there", pg.Phys.ConnectFrom == pc1.Port("Fa0"));
        pg.Phys.ConnectTap(pg.Phys.DevPoint(sw));
        pick = pg.Phys.Picker;
        Check("the picker for a switch shows its ports, the SFP slots and the console", pick.D == sw && sw.Ports.Where(p => !p.IsWireless).All(p => pick.PortCenter(p).X > 0));
        pick.TapAt(pick.PortCenter(sw.Port("Fa0/1")));
        var link = pg.P.Links.FirstOrDefault(l => l.A.Dev == pc1 || l.B.Dev == pc1);
        double expect = Math.Round(Math.Abs(30.5 - 4) + Math.Abs(12 - 4) + 1, 1);
        Check("the second port makes the cable", link != null && (link.A == sw.Port("Fa0/1") || link.B == sw.Port("Fa0/1")), pg.StatusText);
        Check("the cable's length follows the plan (along the walls + slack)", link != null && Math.Abs(link.Length - expect) < 0.01, link?.Length + " vs " + expect);
        pg.Phys.ConnectTap(pg.Phys.DevPoint(pc2)); pg.Phys.Picker.TapAt(pg.Phys.Picker.PortCenter(pc2.Port("Fa0")));
        pg.Phys.ConnectTap(pg.Phys.DevPoint(sw));
        var busy = pg.Phys.Picker.TapAt(pg.Phys.Picker.PortCenter(sw.Port("Fa0/1")));
        Check("a port in use can't be picked", busy == null && pg.Phys.ConnectFrom != null);
        pg.Phys.Picker.TapAt(pg.Phys.Picker.PortCenter(sw.Port("Fa0/2")));
        Check("two cables now", pg.P.Links.Count == 2);
        pg.SetTool("select");
        // moving a device to another floor lengthens its cable by the rise
        var link2 = pc2.Ports[0].Link;
        double flat = link2.Length;
        var gym = pg.P.Layout.AllFloors().First(f => f.B.Id == "warehouse");
        var mi = pg.Phys.MenuItems(pg.Phys.DevPoint(pc2)).FirstOrDefault(x => x.Label.StartsWith("Move to Warehouse"));
        Check("a device's menu moves it to another building", mi.Act != null);
        mi.Act?.Invoke();
        Check("...and its cable runs across the campus now", pc2.Place == gym.Key && link2.Length > flat + 100 && link2.Status != "up", flat + " -> " + link2.Length + " " + link2.Status);
        pg.Undo.Undo();
        Check("Undo brings it back (and the cable's length)", pc2.Place == "main/f1" && Math.Abs(link2.Length - flat) < 0.01);

        // ---- the plan: a room, a wall, a door, a label, a rack, and mounting the switch
        pg.SetTool("room");
        answers.Enqueue("Comms closet");
        pg.Phys.DragFinger(pg.Phys.S(1, 1), pg.Phys.S(5, 4));
        var room = floor.Rooms.LastOrDefault();
        Check("Room: dragging a box adds a named room (a closet by its name)", room != null && room.Name == "Comms closet" && room.Kind == "closet" && Math.Abs(room.W - 4) < 0.6, room?.Name + " " + room?.W);
        pg.SetTool("wall");
        int walls = floor.Walls.Count;
        pg.Phys.DragFinger(pg.Phys.S(8, 2), pg.Phys.S(8.2, 8));
        Check("Wall: dragging adds a straight wall", floor.Walls.Count == walls + 1 && floor.Walls[^1].X1 == floor.Walls[^1].X2);
        pg.SetTool("door");
        int doors = floor.Doors.Count;
        pg.Phys.Tap(pg.Phys.S(8, 5));
        Check("Door: a tap on a wall puts a door in it", floor.Doors.Count == doors + 1 && floor.Doors[^1].Vertical);
        pg.SetTool("label");
        answers.Enqueue("Printer corner");
        pg.Phys.Tap(pg.Phys.S(20, 22));
        Check("Label: a tap puts a label on the plan", floor.Labels.Any(x => x.Text == "Printer corner"));
        pg.SetTool("rack");
        pg.Phys.Tap(pg.Phys.S(31, 10.5));
        var rack = pg.P.Racks.LastOrDefault();
        Check("Rack: a tap stands a rack", rack != null && rack.Place == "main/f1");
        var mount = pg.Phys.MenuItems(pg.Phys.DevPoint(sw)).FirstOrDefault(x => x.Label == "Mount in " + rack?.Name);
        mount.Act?.Invoke();
        Check("holding the switch offers Mount in the rack, and it goes in", sw.Rack == rack?.Id && sw.RackU > 0);
        pg.Phys.InvalidateVisual(); Pump(); Snap(px + "floor-built");
        pg.Phys.ShowRack(rack.Id); Pump(); Snap(px + "rack");
        opened = false;
        pg.Phys.DeviceOpened += d => opened |= d == sw;
        depth = mv.Depth;
        pg.Phys.Tap(pg.Phys.RackDevicePoint(sw)); Pump();
        Check("the rack's front: a tap on the switch opens it", opened && mv.Page is DevicePage);
        if (mv.Depth > depth) { mv.Back(); Pump(); }
        pg.OnBack();
        Check("Back from the rack goes to the floor", pg.Phys.Level == "floor");

        // ---- the port picker as a device's Physical tab picture: tap an SFP slot to plug in a module
        var dp = new DevicePage(sw, "Physical"); mv.Push(dp); Pump();
        var slot = sw.Ports.First(p => p.Media == "sfp");
        Check("the Physical tab shows the front panel", dp.Panel != null && dp.Panel.Bounds.Width > 100);
        dp.Panel.TapAt(dp.Panel.PortCenter(slot)); dp.Refresh(); Pump();
        Check("tapping an SFP slot plugs in a module", slot.Sfp == "mmf");
        Snap(px + "device-physical");
        mv.Back(); Pump();

        // ---- drawings on both views
        DrawEverything(pg, false, px);
        DrawEverything(pg, true, px);

        // ---- saving and opening again: the drawings come back
        int shapes = pg.P.Shapes.Count, callouts = pg.P.Callouts.Count;
        var reopened = Persist.Load(Persist.Save(pg.P));
        Check("save and open: every drawing and label comes back", reopened.Shapes.Count == shapes && reopened.Callouts.Count == callouts && reopened.Racks.Count == pg.P.Racks.Count
            && reopened.Shapes.Count(s => s.View == "main/f1") == pg.P.Shapes.Count(s => s.View == "main/f1") && reopened.Layout.Floor("main/f1").Rooms.Any(r => r.Name == "Comms closet"), shapes + " / " + reopened.Shapes.Count);
        pg.Autosave();
        mv.Back(); Pump();
        var pg2 = new NetLabPage(); mv.Push(pg2); Pump(); pg2.Sim.Stop();
        Check("the autosave brings the drawings and the plan back", pg2.P.Shapes.Count == shapes && pg2.P.Callouts.Count == callouts && pg2.P.Layout.Id == "office" && pg2.P.Find(sw.Name)?.Rack.Length > 0, pg2.P.Shapes.Count + " shapes");
        pg2.ShowPhysical(true); Pump(); Snap(px + "reopened");
        mv.Back(); Pump();
        foreach (var f in Directory.GetFiles(Data.For("NetworkLab"))) File.Delete(f);
    }

    /// <summary>Draws one of each kind, then resizes, turns, edits, groups, lines up, copies, pastes, undoes, and
    /// tries the map, the scale and the layers, on the Logical view or the Physical view's floor.</summary>
    static void DrawEverything(NetLabPage pg, bool physical, string px)
    {
        string where = physical ? "floor plan" : "Logical view";
        pg.ShowPhysical(physical); Pump();
        bool small = pg.View.Bounds.Width < 820;
        if (physical)
        {
            var floorDevs = pg.P.Devices.Where(d => d.Place == "main/f1" && d.Rack.Length == 0).ToList();
            for (int i = 0; i < floorDevs.Count; i++) { floorDevs[i].PX = 24 + i * 4; floorDevs[i].PY = 22; }
            pg.P.Sim.TopologyChanged();
            pg.Phys.ShowFloor("main/f1"); Pump();
        }
        else
        {
            for (int i = 0; i < pg.P.Devices.Count; i++) { pg.P.Devices[i].X = 60 + i * 90; pg.P.Devices[i].Y = 380; }
            pg.Canvas.Zoom = small ? 0.55 : 1; pg.Canvas.Pan = new Vector(30, 20); pg.Canvas.InvalidateVisual(); Pump();
        }
        pg.ShowDrawBar(true); Pump();
        var draw = pg.CurrentDraw;
        string view = draw.Host.DrawView;
        Point S(double x, double y) => physical ? pg.Phys.S(x, y) : pg.Canvas.ToScreen(x, y);
        double u = physical ? 1 : 20;   // a unit: a metre on the plan, 20 px on the Logical view
        double ox = physical ? 2 : 10, oy = physical ? 4 : 10;
        void Drag(Point a, Point b) { if (physical) pg.Phys.DragFinger(a, b); else pg.Canvas.DragFinger(a, b); }
        void Tap(Point a, int clicks = 1) { if (physical) pg.Phys.Tap(a, clicks); else pg.Canvas.Tap(a, clicks); }
        int before = pg.P.Shapes.Count(s => s.View == view);
        var made = new Dictionary<string, Shape>();
        string[] boxes = { "rect", "roundrect", "ellipse", "line", "arrow", "freehand", "area" };
        for (int i = 0; i < boxes.Length; i++)
        {
            pg.SetTool("draw:" + boxes[i]);
            double x = ox + (i % 4) * 5 * u, y = oy + (i / 4) * 4 * u;
            if (boxes[i] == "freehand")
            {
                var fp = physical ? (Action<Point>)(p => pg.Phys.PressAt(p)) : p => pg.Canvas.PressAt(p);
                fp(S(x, y));
                for (int k = 1; k <= 12; k++) { var q = S(x + k * 0.3 * u, y + Math.Sin(k / 2.0) * u); if (physical) pg.Phys.MoveTo(q); else pg.Canvas.MoveTo(q); }
                if (physical) pg.Phys.ReleaseAt(S(x + 3.6 * u, y)); else pg.Canvas.ReleaseAt(S(x + 3.6 * u, y));
            }
            else
            {
                if (boxes[i] == "area") answers.Enqueue("Sales VLAN 20");
                Drag(S(x, y), S(x + 4 * u, y + 3 * u));
            }
            var sh = pg.P.Shapes.LastOrDefault(s => s.View == view);
            made[boxes[i]] = sh;
            Check(where + ": dragging draws a " + boxes[i], sh != null && sh.Kind == boxes[i] && (sh.W > 0.5 * u || sh.H > 0.5 * u), sh?.Kind + " " + sh?.W);
            Check(where + ": the tool goes back to Move after a " + boxes[i], (physical ? pg.Phys.Tool : pg.Canvas.Tool) == "select");
        }
        Check(where + ": an area gets its title", made["area"]?.Text == "Sales VLAN 20", made["area"]?.Text);
        // lines: corner after corner, then tap the last corner again
        pg.SetTool("draw:polyline");
        Tap(S(ox, oy + 9 * u)); Tap(S(ox + 3 * u, oy + 11 * u)); Tap(S(ox + 6 * u, oy + 9 * u)); Tap(S(ox + 6 * u, oy + 9 * u));
        var pl = pg.P.Shapes.LastOrDefault(s => s.View == view);
        Check(where + ": Lines: tapping corners draws them", pl?.Kind == "polyline" && pl.Points.Count == 3, pl?.Kind + " " + pl?.Points.Count);
        // text, sticky note, object, picture and a label on a device
        answers.Enqueue("Main office");
        pg.SetTool("draw:text"); Tap(S(ox + 10 * u, oy + 9 * u));
        var tx = pg.P.Shapes.LastOrDefault(s => s.View == view);
        Check(where + ": Text: a tap asks for the text", tx?.Kind == "text" && tx.Text == "Main office");
        answers.Enqueue("Ask Sam for the Wi-Fi password");
        pg.SetTool("draw:note"); Tap(S(ox + 15 * u, oy + 9 * u));
        var note = pg.P.Shapes.LastOrDefault(s => s.View == view);
        Check(where + ": Sticky note", note?.Kind == "note" && note.Text.StartsWith("Ask Sam"));
        pg.SetTool("obj:desk"); Tap(S(ox + 20 * u, oy + 2 * u));
        var desk = pg.P.Shapes.LastOrDefault(s => s.View == view);
        Check(where + ": an object from the library goes where it is tapped", desk?.Kind == "object" && desk.Art == "desk");
        var picShape = draw.AddPicture(TinyPng(), ox + 20 * u, oy + 7 * u);
        Check(where + ": a picture", picShape?.Kind == "image" && picShape.Picture.Length > 0);
        var target = pg.P.Devices.First(d => d.Place == "main/f1" && d.Rack.Length == 0);
        var tp = physical ? pg.Phys.DevPoint(target) : pg.Canvas.Center(target);
        answers.Enqueue(physical ? "Reception" : "Front desk PC");
        int calls = pg.P.Callouts.Count;
        pg.SetTool("draw:label"); Tap(tp);
        Check(where + ": Label: tap a device to stick a label on it", pg.P.Callouts.Count == calls + 1 && pg.P.Callouts[^1].Target == target.Id && pg.P.Callouts[^1].View == (physical ? "physical" : "logical"));
        int kinds = pg.P.Shapes.Where(s => s.View == view).Select(s => s.Kind).Distinct().Count();
        Check(where + ": every kind of drawing", kinds >= 12, kinds.ToString());
        Pump(); Snap(px + (physical ? "draw-floor" : "draw-logical"));

        // select, resize with a handle, turn with the rotate handle, edit the text
        var r = made["rect"];
        pg.SetTool("select");
        Tap(S(r.CX, r.CY));
        Check(where + ": a tap selects a drawing", pg.CurrentDraw.SelectedShapes().SequenceEqual(new[] { r }));
        double w0 = r.W;
        var se = draw.HandleAt("se");
        Drag(se, new Point(se.X + 2 * u * (physical ? pg.Phys.Scale : pg.Canvas.Zoom), se.Y));
        Check(where + ": dragging a corner handle resizes it", r.W > w0 + 1.5 * u && r.W < w0 + 2.5 * u, w0 + " -> " + r.W);
        var rot = draw.HandleAt("rot");
        var ctr = S(r.CX, r.CY);
        Drag(rot, new Point(ctr.X + (rot.Y < ctr.Y ? ctr.Y - rot.Y : 50), ctr.Y));
        Check(where + ": dragging the round handle turns it", Math.Abs(r.Rotation - 90) < 2, r.Rotation.ToString());
        // a point inside the turned shape (but outside it when upright) hits it
        Point2 inside = new(r.CX, r.CY + r.W / 2 * 0.8);
        bool hitsTurned = draw.HitShape(r, S(inside.X, inside.Y));
        double rot0 = r.Rotation; r.Rotation = 0;
        bool hitsUpright = draw.HitShape(r, S(inside.X, inside.Y));
        r.Rotation = rot0;
        Check(where + ": a turned shape is hit where it is drawn", hitsTurned && !hitsUpright);
        pg.Undo.Undo();
        Check(where + ": Undo takes the turn back", Math.Abs(r.Rotation) < 0.01);
        answers.Enqueue("Server room");
        draw.EditText(r);
        Check(where + ": editing a shape's text", r.Text == "Server room");
        pg.ShowDrawPanel(); Pump();
        Check(where + ": the properties panel shows the selection", pg.PanelOpen && pg.SideMode == "draw");
        Snap(px + (physical ? "draw-panel-floor" : "draw-panel-logical"));
        draw.Apply("fill colour", x => x.Fill = "#EF4444");
        Check(where + ": the panel's colour change", r.Fill == "#EF4444");
        pg.Undo.Undo();
        Check(where + ": ...and Undo", r.Fill != "#EF4444");
        if (phone(pg)) { pg.OnBack(); Pump(); }

        // group, line up, duplicate, copy and paste, delete, undo
        var sel = pg.Physical ? pg.Phys.Selection : pg.Canvas.Selection;
        var a = made["roundrect"]; var b = made["ellipse"];
        sel.Clear(); sel.Add(a); sel.Add(b);
        draw.Align("top");
        Check(where + ": line up the tops", Math.Abs(a.Y - b.Y) < 1e-9);
        draw.GroupSelection();
        Check(where + ": group", a.Group.Length > 0 && a.Group == b.Group);
        sel.Clear();
        Tap(S(a.CX, a.CY));
        Check(where + ": tapping one of a group picks the whole group", sel.Contains(a) && sel.Contains(b));
        double ax = a.X;
        Drag(S(a.CX, a.CY), S(a.CX + 2 * u, a.CY));
        Check(where + ": the group moves together", Math.Abs(a.X - ax - 2 * u) < 0.6 * u && Math.Abs(b.X - a.X - (made["ellipse"].X - made["roundrect"].X)) < 1e-6, ax + " -> " + a.X);
        draw.Copy();
        int n0 = pg.P.Shapes.Count;
        var pasted = draw.Paste();
        Check(where + ": copy and paste (the group as a new group)", pasted.Count == 2 && pg.P.Shapes.Count == n0 + 2 && pasted[0].Group != a.Group && pasted[0].Group == pasted[1].Group);
        draw.DeleteSelection();
        Check(where + ": delete the pasted ones", pg.P.Shapes.Count == n0);
        pg.Undo.Undo();
        Check(where + ": undo brings them back", pg.P.Shapes.Count == n0 + 2);
        pg.Undo.Undo();
        Check(where + ": undo the paste", pg.P.Shapes.Count == n0);

        // the map and the scale
        if (!physical)
        {
            draw.ApplyMapStyle("streets");
            Check("Logical view: a street map", draw.HasMap && Drawings.MapOf(pg.P, "logical").Style == "streets");
            Pump(); Snap(px + "map-streets");
            draw.ApplyMapStyle("none");
            pg.SetTool("draw:calibrate");
            answers.Enqueue("50");
            Drag(S(0, 400), S(200, 400));
            Check("Logical view: Set the scale (a line of 200 px is 50 m)", Math.Abs(Drawings.MapOf(pg.P, "logical").MetresPerUnit - 0.25) < 1e-9, Drawings.MapOf(pg.P, "logical").MetresPerUnit.ToString());
            draw.SetMapPicture(TinyPng(0, 128, 0));
            Check("Logical view: a picture as the map", Drawings.MapOf(pg.P, "logical").Picture.Length > 0);
            draw.RemoveMapPicture();
            pg.ShowPhysical(true); pg.Phys.ShowCampus(); Pump();
            pg.CurrentDraw.ApplyMapStyle("park");
            Check("Campus: a park map", Drawings.MapOf(pg.P, "campus").Style == "park");
            Pump(); Snap(px + "campus-park");
            pg.ShowPhysical(false);
        }
        else
        {
            var f = pg.Phys.CurrentFloor;
            draw.SetMapPicture(TinyPng(0, 0, 200));
            Check("Floor plan: a picture of the plan underneath", f.Background.Length > 0 && Math.Abs(f.BgW - f.W) < 1e-9);
            pg.SetTool("draw:calibrate");
            answers.Enqueue("20");
            Drag(S(0, 1), S(10, 1));
            Check("Floor plan: Set the scale stretches the picture (10 m drawn are really 20 m)", Math.Abs(f.BgW - f.W * 2) < 0.01, f.BgW + " vs " + f.W);
            Pump(); Snap(px + "plan-picture");
            draw.RemoveMapPicture();
            Check("Floor plan: remove the picture", f.Background.Length == 0);
        }

        // layers: hide the devices, lock the drawings
        draw = pg.CurrentDraw;
        var dev = pg.P.Devices.First(d => d.Place == "main/f1" && d.Rack.Length == 0);
        Point dpt() => pg.Physical ? pg.Phys.DevPoint(dev) : pg.Canvas.Center(dev);
        Func<Point, Device> hit = pg.Physical ? pg.Phys.HitDevice : pg.Canvas.HitDevice;
        draw.SetLayer("devices", true, false);
        Check(where + ": hiding the Devices layer", hit(dpt()) == null && !draw.LayerShows("devices"));
        Pump(); Snap(px + (physical ? "layers-floor" : "layers-logical"));
        draw.SetLayer("devices", true, true);
        Check(where + ": showing it again", hit(dpt()) == dev);
        var desk2 = made["rect"];
        draw.SetLayer("drawings", false, true);
        Check(where + ": a locked Drawings layer lets taps through", draw.HitBelow(S(desk2.CX, desk2.CY)) == null);
        pg.Undo.Undo();
        Check(where + ": Undo unlocks it", !draw.LayerLocked("drawings"));
        pg.ShowDrawBar(false); Pump();
        if (pg.PanelOpen) pg.OnBack();
        pg.SetTool("select");
    }

    static bool phone(NetLabPage pg) => pg.View.Bounds.Width < 820;
}
