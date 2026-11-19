// Network Lab - the physical side: a campus with buildings, floors, rooms and walls (in metres), where each device
// sits, how long each cable run is, and how far Wi-Fi reaches through walls.
using System;
using System.Collections.Generic;
using System.Linq;

namespace NetworkLab
{
    public class Room
    {
        public string Name = "";
        public double X, Y, W, H;
        public string Kind = "room";   // room, closet, server, hall, outside
    }
    public class Wall { public double X1, Y1, X2, Y2; }
    public class Door { public double X, Y, W = 0.9; public bool Vertical; }
    public class Label { public double X, Y; public string Text = ""; }

    public class Floor
    {
        public string Id = "f1", Name = "Ground floor";
        public int Level;
        public double W = 30, H = 20;
        public readonly List<Room> Rooms = new List<Room>();
        public readonly List<Wall> Walls = new List<Wall>();
        public readonly List<Door> Doors = new List<Door>();
        public readonly List<Label> Labels = new List<Label>();
        public string Background = "";     // a picture (base64 PNG / JPEG) under the plan
        public double BgW, BgH;
        public Building B;
        public string Key { get { return B.Id + "/" + Id; } }

        /// <summary>Every wall: the room outlines and the free walls.</summary>
        public IEnumerable<Wall> AllWalls()
        {
            foreach (var r in Rooms)
            {
                if (r.Kind == "outside") continue;
                yield return new Wall { X1 = r.X, Y1 = r.Y, X2 = r.X + r.W, Y2 = r.Y };
                yield return new Wall { X1 = r.X, Y1 = r.Y + r.H, X2 = r.X + r.W, Y2 = r.Y + r.H };
                yield return new Wall { X1 = r.X, Y1 = r.Y, X2 = r.X, Y2 = r.Y + r.H };
                yield return new Wall { X1 = r.X + r.W, Y1 = r.Y, X2 = r.X + r.W, Y2 = r.Y + r.H };
            }
            foreach (var w in Walls) yield return w;
        }

        public Room RoomAt(double x, double y)
        {
            Room best = null;
            foreach (var r in Rooms) if (x >= r.X && x <= r.X + r.W && y >= r.Y && y <= r.Y + r.H) { if (best == null || r.W * r.H < best.W * best.H) best = r; }
            return best;
        }
    }

    public class Building
    {
        public string Id = "b1", Name = "Building";
        public double X, Y, W = 30, H = 20;
        public readonly List<Floor> Floors = new List<Floor>();
        public Floor AddFloor(string id, string name, int level)
        {
            var f = new Floor { Id = id, Name = name, Level = level, W = W, H = H, B = this };
            Floors.Add(f);
            return f;
        }
    }

    public class Layout
    {
        public string Id = "office", Name = "Small office";
        public double CampusW = 200, CampusH = 120;
        public readonly List<Building> Buildings = new List<Building>();
        public readonly List<Label> CampusLabels = new List<Label>();
        public readonly List<Wall> Paths = new List<Wall>();   // drawn paths / roads on the campus map
        public string ActiveFloor = "";

        public Floor Floor(string key)
        {
            foreach (var b in Buildings) foreach (var f in b.Floors) if (f.Key == key) return f;
            return null;
        }
        public IEnumerable<Floor> AllFloors() { foreach (var b in Buildings) foreach (var f in b.Floors) yield return f; }
        public Floor DefaultFloor
        {
            get
            {
                var f = Floor(ActiveFloor);
                if (f != null) return f;
                return Buildings.Count > 0 && Buildings[0].Floors.Count > 0 ? Buildings[0].Floors[0] : null;
            }
        }
    }

    public static class Physical
    {
        public const double FloorHeight = 3.5;

        /// <summary>Where a device is on the campus (metres) and its height.</summary>
        public static bool Where(Project p, Device d, out double x, out double y, out double z, out Floor fl)
        {
            x = y = z = 0;
            fl = null;
            double px = d.PX, py = d.PY;
            string place = d.Place;
            if (d.Rack.Length > 0)
            {
                var r = p.Racks.FirstOrDefault(k => k.Id == d.Rack);
                if (r != null) { px = r.PX; py = r.PY; place = r.Place; }
            }
            fl = p.Layout.Floor(place);
            if (fl == null) return false;
            x = fl.B.X + px; y = fl.B.Y + py;
            z = fl.Level * FloorHeight + (d.Rack.Length > 0 ? 1.5 : 0.8);
            return true;
        }

        /// <summary>A new device goes on the floor shown in the physical view (or the first one), near the middle,
        /// in a little grid so devices don't pile up.</summary>
        public static void PlaceNew(Project p, Device d)
        {
            var f = p.Layout.DefaultFloor;
            if (f == null) return;
            d.Place = f.Key;
            int n = p.Devices.Count(x => x != d && x.Place == f.Key && x.Rack.Length == 0);
            double cx = f.W / 2, cy = f.H / 2;
            d.PX = Math.Max(0.5, Math.Min(f.W - 0.5, cx + ((n % 5) - 2) * 1.6));
            d.PY = Math.Max(0.5, Math.Min(f.H - 0.5, cy + (((n / 5) % 5) - 2) * 1.6));
        }

        /// <summary>The length of a cable run: along walls (Manhattan distance), up and down between floors, plus a
        /// little slack at each end. Wireless and console links don't count.</summary>
        public static double RunLength(Project p, Device a, Device b)
        {
            double ax, ay, az, bx, by, bz; Floor fa, fb;
            if (!Where(p, a, out ax, out ay, out az, out fa) || !Where(p, b, out bx, out by, out bz, out fb)) return 2;
            double run = Math.Abs(ax - bx) + Math.Abs(ay - by) + Math.Abs(az - bz);
            if (fa != fb) run += 3;             // into the riser and out again
            if (fa.B != fb.B) run += 6;         // out of one building and into the other
            return Math.Round(run + 1, 1);
        }

        public static void UpdateLength(Project p, Link l)
        {
            if (l.ManualLength || l.IsWireless) return;
            if (l.IsConsole) { l.Length = 2; return; }
            l.Length = RunLength(p, l.A.Dev, l.B.Dev);
        }

        /// <summary>Wi-Fi strength from an access point to a device, 0 (none) to 1. Each wall in between costs about
        /// 6 m of range, each floor 12 m, another building 30 m.</summary>
        public static double Signal(Project p, Device ap, Device d)
        {
            double ax, ay, az, bx, by, bz; Floor fa, fb;
            if (!Where(p, ap, out ax, out ay, out az, out fa) || !Where(p, d, out bx, out by, out bz, out fb)) return 1;
            double dist = Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by) + (az - bz) * (az - bz));
            double eff = dist;
            if (fa.B != fb.B) eff += 30;
            else
            {
                eff += Math.Abs(fa.Level - fb.Level) * 12;
                if (fa == fb) eff += WallsBetween(fa, ax - fa.B.X, ay - fa.B.Y, bx - fb.B.X, by - fb.B.Y) * 6;
            }
            double range = ap.Ap != null ? ap.Ap.Range : 35;
            double s = 1 - eff / range;
            return s <= 0 ? 0 : Math.Round(s, 3);
        }

        public static int WallsBetween(Floor f, double x1, double y1, double x2, double y2)
        {
            int n = 0;
            var seen = new HashSet<string>();
            foreach (var w in f.AllWalls())
            {
                // shared walls between two rooms count once
                string key = Math.Round(Math.Min(w.X1, w.X2), 2) + "," + Math.Round(Math.Min(w.Y1, w.Y2), 2) + "," + Math.Round(Math.Max(w.X1, w.X2), 2) + "," + Math.Round(Math.Max(w.Y1, w.Y2), 2);
                if (!Crosses(x1, y1, x2, y2, w.X1, w.Y1, w.X2, w.Y2)) continue;
                // overlapping segments on the same line (two rooms sharing a wall) count once
                bool dup = false;
                foreach (var s in seen) { if (s == key) { dup = true; break; } }
                if (dup) continue;
                seen.Add(key);
                n++;
            }
            return Math.Min(n, 6);
        }

        static bool Crosses(double ax, double ay, double bx, double by, double cx, double cy, double dx, double dy)
        {
            double d1 = Cross(cx, cy, dx, dy, ax, ay), d2 = Cross(cx, cy, dx, dy, bx, by);
            double d3 = Cross(ax, ay, bx, by, cx, cy), d4 = Cross(ax, ay, bx, by, dx, dy);
            return ((d1 > 1e-9 && d2 < -1e-9) || (d1 < -1e-9 && d2 > 1e-9)) && ((d3 > 1e-9 && d4 < -1e-9) || (d3 < -1e-9 && d4 > 1e-9));
        }
        static double Cross(double ax, double ay, double bx, double by, double px, double py) { return (bx - ax) * (py - ay) - (by - ay) * (px - ax); }

        // ---------------------------------------------------------------- saving the layout
        public static JObj SaveLayout(Layout l)
        {
            var o = new JObj();
            o["id"] = l.Id; o["name"] = l.Name; o["w"] = l.CampusW; o["h"] = l.CampusH; o["active"] = l.ActiveFloor;
            o["labels"] = l.CampusLabels.Select(x => (object)new JObj().Set("x", x.X).Set("y", x.Y).Set("text", x.Text)).ToList();
            o["paths"] = l.Paths.Select(w => (object)new List<object> { w.X1, w.Y1, w.X2, w.Y2 }).ToList();
            var bs = new List<object>();
            foreach (var b in l.Buildings)
            {
                var bo = new JObj().Set("id", b.Id).Set("name", b.Name).Set("x", b.X).Set("y", b.Y).Set("w", b.W).Set("h", b.H);
                var fs = new List<object>();
                foreach (var f in b.Floors)
                {
                    var fo = new JObj().Set("id", f.Id).Set("name", f.Name).Set("level", (double)f.Level);
                    fo["rooms"] = f.Rooms.Select(r => (object)new JObj().Set("name", r.Name).Set("x", r.X).Set("y", r.Y).Set("w", r.W).Set("h", r.H).Set("kind", r.Kind)).ToList();
                    fo["walls"] = f.Walls.Select(w => (object)new List<object> { w.X1, w.Y1, w.X2, w.Y2 }).ToList();
                    fo["doors"] = f.Doors.Select(d => (object)new JObj().Set("x", d.X).Set("y", d.Y).Set("w", d.W).Set("vertical", d.Vertical)).ToList();
                    fo["labels"] = f.Labels.Select(x => (object)new JObj().Set("x", x.X).Set("y", x.Y).Set("text", x.Text)).ToList();
                    if (f.Background.Length > 0) { fo["background"] = f.Background; fo["bgW"] = f.BgW; fo["bgH"] = f.BgH; }
                    fs.Add(fo);
                }
                bo["floors"] = fs;
                bs.Add(bo);
            }
            o["buildings"] = bs;
            return o;
        }

        public static Layout LoadLayout(JObj o)
        {
            var l = new Layout { Id = o.Str("id", "custom"), Name = o.Str("name", "Floor plan"), CampusW = o.Num("w", 200), CampusH = o.Num("h", 120), ActiveFloor = o.Str("active") };
            foreach (JObj x in o.Arr("labels")) l.CampusLabels.Add(new Label { X = x.Num("x", 0), Y = x.Num("y", 0), Text = x.Str("text") });
            foreach (var w in o.Arr("paths")) { var a = w as List<object>; if (a != null && a.Count == 4) l.Paths.Add(new Wall { X1 = (double)a[0], Y1 = (double)a[1], X2 = (double)a[2], Y2 = (double)a[3] }); }
            foreach (JObj bo in o.Arr("buildings"))
            {
                var b = new Building { Id = bo.Str("id"), Name = bo.Str("name"), X = bo.Num("x", 0), Y = bo.Num("y", 0), W = bo.Num("w", 30), H = bo.Num("h", 20) };
                foreach (JObj fo in bo.Arr("floors"))
                {
                    var f = b.AddFloor(fo.Str("id"), fo.Str("name"), fo.Int("level", 0));
                    foreach (JObj r in fo.Arr("rooms")) f.Rooms.Add(new Room { Name = r.Str("name"), X = r.Num("x", 0), Y = r.Num("y", 0), W = r.Num("w", 1), H = r.Num("h", 1), Kind = r.Str("kind", "room") });
                    foreach (var w in fo.Arr("walls")) { var a = w as List<object>; if (a != null && a.Count == 4) f.Walls.Add(new Wall { X1 = (double)a[0], Y1 = (double)a[1], X2 = (double)a[2], Y2 = (double)a[3] }); }
                    foreach (JObj d in fo.Arr("doors")) f.Doors.Add(new Door { X = d.Num("x", 0), Y = d.Num("y", 0), W = d.Num("w", 0.9), Vertical = d.Bool("vertical", false) });
                    foreach (JObj x in fo.Arr("labels")) f.Labels.Add(new Label { X = x.Num("x", 0), Y = x.Num("y", 0), Text = x.Str("text") });
                    f.Background = fo.Str("background"); f.BgW = fo.Num("bgW", 0); f.BgH = fo.Num("bgH", 0);
                }
                l.Buildings.Add(b);
            }
            return l;
        }
    }

    /// <summary>The floor plans that come with the app (all drawn here, in metres).</summary>
    public static class Layouts
    {
        public static readonly string[][] List =
        {
            new[] { "house", "House", "A family home with a living room, kitchen, bedrooms and a study." },
            new[] { "office", "Small office", "An open-plan office with a server room and a wiring closet, and a warehouse across the car park." },
            new[] { "school", "School", "A two-floor main building and a sports hall 200 m away, linked across the campus." },
            new[] { "blank", "Blank floor plan", "An empty floor: draw your own rooms and walls, or put a picture of a plan underneath." },
        };

        public static Layout Create(string id)
        {
            var l = new Layout { Id = id };
            switch (id)
            {
                case "house":
                    {
                        l.Name = "House"; l.CampusW = 40; l.CampusH = 30;
                        var b = new Building { Id = "home", Name = "House", X = 8, Y = 6, W = 16, H = 12 };
                        var f = b.AddFloor("f1", "Ground floor", 0);
                        Room(f, "Living room", 0, 0, 9, 7); Room(f, "Kitchen", 9, 0, 7, 5); Room(f, "Study", 9, 5, 7, 7);
                        Room(f, "Hall", 5, 7, 4, 5, "hall"); Room(f, "Bedroom", 0, 7, 5, 5);
                        f.Doors.Add(new Door { X = 6.5, Y = 7 }); f.Doors.Add(new Door { X = 9, Y = 9, Vertical = true }); f.Doors.Add(new Door { X = 5, Y = 9.5, Vertical = true }); f.Doors.Add(new Door { X = 12, Y = 5 });
                        var up = b.AddFloor("f2", "Upstairs", 1);
                        Room(up, "Bedroom 2", 0, 0, 8, 6); Room(up, "Bedroom 3", 8, 0, 8, 6); Room(up, "Landing", 0, 6, 6, 6, "hall"); Room(up, "Bathroom", 6, 6, 4, 6); Room(up, "Games room", 10, 6, 6, 6);
                        l.Buildings.Add(b);
                        l.CampusLabels.Add(new Label { X = 2, Y = 2, Text = "Garden" });
                        l.Paths.Add(new Wall { X1 = 0, Y1 = 24, X2 = 40, Y2 = 24 });
                        break;
                    }
                case "office":
                    {
                        l.Name = "Small office"; l.CampusW = 230; l.CampusH = 90;
                        var b = new Building { Id = "main", Name = "Office", X = 10, Y = 10, W = 40, H = 25 };
                        var f = b.AddFloor("f1", "Ground floor", 0);
                        Room(f, "Open-plan office", 0, 0, 28, 25); Room(f, "Server room", 28, 0, 12, 9, "server"); Room(f, "Wiring closet", 28, 9, 6, 7, "closet");
                        Room(f, "Meeting room", 34, 9, 6, 9); Room(f, "Kitchen", 28, 16, 6, 9); Room(f, "Reception", 34, 18, 6, 7);
                        f.Doors.Add(new Door { X = 28, Y = 4, Vertical = true }); f.Doors.Add(new Door { X = 28, Y = 12, Vertical = true }); f.Doors.Add(new Door { X = 28, Y = 20, Vertical = true }); f.Doors.Add(new Door { X = 37, Y = 18 });
                        for (int i = 0; i < 4; i++) for (int j = 0; j < 3; j++) f.Labels.Add(new Label { X = 3 + i * 6.5, Y = 4 + j * 7.5, Text = "Desk " + (j * 4 + i + 1) });
                        l.Buildings.Add(b);
                        var w = new Building { Id = "warehouse", Name = "Warehouse", X = 170, Y = 20, W = 40, H = 30 };
                        var wf = w.AddFloor("f1", "Ground floor", 0);
                        Room(wf, "Storage", 0, 0, 40, 22); Room(wf, "Office", 0, 22, 12, 8); Room(wf, "Closet", 12, 22, 5, 8, "closet"); Room(wf, "Loading bay", 17, 22, 23, 8);
                        l.Buildings.Add(w);
                        l.CampusLabels.Add(new Label { X = 90, Y = 45, Text = "Car park" });
                        l.Paths.Add(new Wall { X1 = 50, Y1 = 30, X2 = 170, Y2 = 30 });
                        break;
                    }
                case "school":
                    {
                        l.Name = "School"; l.CampusW = 300; l.CampusH = 110;
                        var b = new Building { Id = "main", Name = "Main building", X = 10, Y = 20, W = 50, H = 30 };
                        var g = b.AddFloor("f1", "Ground floor", 0);
                        Room(g, "Classroom 1", 0, 0, 15, 12); Room(g, "Classroom 2", 15, 0, 15, 12); Room(g, "Library", 30, 0, 20, 12);
                        Room(g, "Corridor", 0, 12, 50, 4, "hall"); Room(g, "Office", 0, 16, 12, 14); Room(g, "Wiring closet", 12, 16, 6, 6, "closet"); Room(g, "Server room", 12, 22, 6, 8, "server"); Room(g, "Hall", 18, 16, 32, 14);
                        var up = b.AddFloor("f2", "First floor", 1);
                        Room(up, "Classroom 3", 0, 0, 15, 12); Room(up, "Classroom 4", 15, 0, 15, 12); Room(up, "New classroom", 30, 0, 20, 12);
                        Room(up, "Corridor", 0, 12, 50, 4, "hall"); Room(up, "Wiring closet", 12, 16, 6, 6, "closet"); Room(up, "Science lab", 18, 16, 32, 14); Room(up, "Staff room", 0, 16, 12, 14);
                        for (int i = 0; i < 4; i++) for (int j = 0; j < 2; j++) up.Labels.Add(new Label { X = 32 + i * 4.5, Y = 3 + j * 5, Text = "Desk " + (j * 4 + i + 1) });
                        l.Buildings.Add(b);
                        var gym = new Building { Id = "gym", Name = "Sports hall", X = 240, Y = 30, W = 40, H = 30 };
                        var gf = gym.AddFloor("f1", "Ground floor", 0);
                        Room(gf, "Sports hall", 0, 0, 40, 22); Room(gf, "Changing rooms", 0, 22, 25, 8); Room(gf, "Closet", 25, 22, 5, 8, "closet"); Room(gf, "Office", 30, 22, 10, 8);
                        l.Buildings.Add(gym);
                        l.CampusLabels.Add(new Label { X = 120, Y = 60, Text = "Playing field" });
                        l.Paths.Add(new Wall { X1 = 60, Y1 = 40, X2 = 240, Y2 = 40 });
                        break;
                    }
                default:
                    {
                        l.Id = "blank"; l.Name = "Blank floor plan"; l.CampusW = 60; l.CampusH = 40;
                        var b = new Building { Id = "b1", Name = "Building", X = 10, Y = 8, W = 30, H = 20 };
                        b.AddFloor("f1", "Ground floor", 0);
                        l.Buildings.Add(b);
                        break;
                    }
            }
            return l;
        }

        static void Room(Floor f, string name, double x, double y, double w, double h, string kind = "room")
        {
            f.Rooms.Add(new Room { Name = name, X = x, Y = y, W = w, H = h, Kind = kind });
        }
    }
}
