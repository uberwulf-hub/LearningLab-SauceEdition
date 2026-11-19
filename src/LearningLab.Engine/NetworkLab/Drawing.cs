// Network Lab - drawings on the Logical and Physical views: shapes, lines and arrows, text, sticky notes, areas,
// pictures and clip-art objects, labels attached to devices and cables, the map under a view, and the layers.
// Decoration only: nothing here changes the simulation or a lab's goals.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;


namespace NetworkLab
{
    /// <summary>One drawn thing. Positions are in the view's units: pixels (at 100%) on the Logical view, metres on
    /// the Campus map and the floor plans. Rotation turns the box around its centre (lines keep their points).</summary>
    public class Shape
    {
        public string Id = "";
        /// <summary>rect, roundrect, ellipse, line, arrow, polyline, freehand, text, note, area, image, object</summary>
        public string Kind = "rect";
        /// <summary>logical, campus, or a floor key (building/floor)</summary>
        public string View = "logical";
        /// <summary>map, drawings or labels</summary>
        public string Layer = "drawings";
        public double X, Y, W = 100, H = 60, Rotation;
        public List<Point2> Points = new List<Point2>();
        public bool Closed;
        public string Fill = "#1E3A5F", Stroke = "#93C5FD", Dash = "solid";
        public double StrokeWidth = 2, Opacity = 1, Corner;
        public string Text = "", Font = "Segoe UI", TextColor = "#F1F5F9", Align = "center";
        public double FontSize = 14;
        public bool Bold, Italic, ArrowStart, ArrowEnd, Locked, Above, FromStyle;
        public string Group = "", Art = "", Picture = "";

        public bool IsLine { get { return Kind == "line" || Kind == "arrow" || Kind == "polyline" || Kind == "freehand"; } }
        public bool CanFill { get { return !IsLine || Closed; } }
        public double CX { get { return X + W / 2; } }
        public double CY { get { return Y + H / 2; } }

        /// <summary>Lines: the box around the points.</summary>
        public void FitPoints()
        {
            if (!IsLine || Points.Count == 0) return;
            double x0 = Points.Min(p => p.X), y0 = Points.Min(p => p.Y), x1 = Points.Max(p => p.X), y1 = Points.Max(p => p.Y);
            X = x0; Y = y0; W = x1 - x0; H = y1 - y0;
        }

        public void MoveBy(double dx, double dy)
        {
            X += dx; Y += dy;
            for (int i = 0; i < Points.Count; i++) Points[i] = new Point2(Points[i].X + dx, Points[i].Y + dy);
        }

        /// <summary>The upright box around the shape, rotation included.</summary>
        public Rect Bounds
        {
            get
            {
                if (IsLine || Math.Abs(Rotation) < 0.01) return new Rect(X, Y, Math.Max(0, W), Math.Max(0, H));
                double a = Rotation * Math.PI / 180, c = Math.Abs(Math.Cos(a)), s = Math.Abs(Math.Sin(a));
                double bw = W * c + H * s, bh = W * s + H * c;
                return new Rect(CX - bw / 2, CY - bh / 2, bw, bh);
            }
        }

        public Shape Clone()
        {
            var s = (Shape)MemberwiseClone();
            s.Points = new List<Point2>(Points);
            return s;
        }

        public void CopyFrom(Shape o)
        {
            foreach (var f in typeof(Shape).GetFields())
            {
                if (f.IsInitOnly || f.IsLiteral) continue;
                var v = f.GetValue(o);
                if (v is List<Point2>) v = new List<Point2>((List<Point2>)v);
                f.SetValue(this, v);
            }
        }
    }

    /// <summary>A label stuck to a device or a cable ("Reception PC", "Uplink to ISP"): it moves with it.</summary>
    public class Callout
    {
        public string Id = "";
        /// <summary>A device id or a cable id.</summary>
        public string Target = "";
        /// <summary>logical or physical (the floor plan)</summary>
        public string View = "logical";
        public string Text = "";
        /// <summary>Where the label sits from the device or the middle of the cable (view units).</summary>
        public double DX, DY = -48;
        public string Color = "#F8FAFC", Fill = "#1E293B";
        public double FontSize = 12;

        public Callout Clone() { return (Callout)MemberwiseClone(); }
        public void CopyFrom(Callout o)
        {
            Id = o.Id; Target = o.Target; View = o.View; Text = o.Text; DX = o.DX; DY = o.DY; Color = o.Color; Fill = o.Fill; FontSize = o.FontSize;
        }
    }

    /// <summary>The map under a view (Logical, Campus): a drawn style and / or a picture.</summary>
    public class MapInfo
    {
        /// <summary>none, grid, streets, park, island</summary>
        public string Style = "none";
        public string Color = "";
        /// <summary>A picture (base64 PNG / JPEG) and where it lies, in view units.</summary>
        public string Picture = "";
        public double PX, PY, PW, PH, Opacity = 0.9;
        /// <summary>Logical view only: metres per pixel once the scale was set (0 = not set).</summary>
        public double MetresPerUnit;
        public MapInfo Clone() { return (MapInfo)MemberwiseClone(); }
    }

    /// <summary>Which layers of a view are hidden or locked.</summary>
    public class LayerSet
    {
        public readonly HashSet<string> Hidden = new HashSet<string>();
        public readonly HashSet<string> Locked = new HashSet<string>();
        public bool Shows(string layer) { return !Hidden.Contains(layer); }
        public bool IsLocked(string layer) { return Locked.Contains(layer); }
        public LayerSet Clone() { var l = new LayerSet(); l.Hidden.UnionWith(Hidden); l.Locked.UnionWith(Locked); return l; }
    }

    /// <summary>A clip-art object in the library.</summary>
    public class ArtInfo
    {
        public string Kind = "", Name = "", Category = "", Words = "";
        /// <summary>Size on the Logical view (pixels) and in the physical views (metres).</summary>
        public double PxW, PxH, MW, MH;
        public string Layer = "drawings";
    }

    public static class ObjectCatalog
    {
        public static readonly string[] Categories = { "Buildings", "Map", "Furniture", "Outdoor", "Telecom", "Fun" };
        public static readonly List<ArtInfo> All = new List<ArtInfo>();

        static void A(string cat, string kind, string name, double pw, double ph, double mw, double mh, string words)
        {
            All.Add(new ArtInfo { Category = cat, Kind = kind, Name = name, PxW = pw, PxH = ph, MW = mw, MH = mh, Words = words });
        }

        static ObjectCatalog()
        {
            A("Buildings", "house", "House", 76, 66, 12, 10, "home family");
            A("Buildings", "office", "Office block", 64, 90, 24, 18, "work company");
            A("Buildings", "school", "School", 100, 70, 40, 25, "classroom college");
            A("Buildings", "warehouse", "Warehouse", 100, 62, 40, 25, "storage factory shed");
            A("Buildings", "datacentre", "Data centre", 92, 66, 30, 20, "datacenter servers cloud");
            A("Buildings", "tower", "Tower", 46, 104, 15, 15, "skyscraper high-rise");
            A("Map", "road", "Road", 180, 30, 40, 8, "street straight");
            A("Map", "roadcurve", "Road bend", 90, 90, 20, 20, "street curve corner");
            A("Map", "junction", "Junction", 90, 90, 18, 18, "crossroads intersection street");
            A("Map", "roundabout", "Roundabout", 96, 96, 26, 26, "circle traffic");
            A("Map", "river", "River", 200, 46, 60, 12, "water stream");
            A("Map", "park", "Park", 130, 90, 30, 20, "trees grass garden green");
            A("Map", "carpark", "Car park", 110, 76, 30, 20, "parking lot");
            A("Map", "bridge", "Bridge", 90, 50, 16, 10, "river crossing");
            A("Furniture", "desk", "Desk", 76, 40, 1.6, 0.8, "table computer workstation");
            A("Furniture", "chair", "Chair", 32, 32, 0.6, 0.6, "seat");
            A("Furniture", "table", "Meeting table", 100, 56, 2.4, 1.2, "conference");
            A("Furniture", "sofa", "Sofa", 86, 40, 2, 0.9, "couch settee");
            A("Furniture", "bookshelf", "Bookshelf", 70, 26, 1.2, 0.4, "books library");
            A("Furniture", "rackcab", "Server rack", 36, 60, 0.6, 1, "cabinet servers 19 inch");
            A("Furniture", "cabinet", "Filing cabinet", 34, 42, 0.5, 0.6, "drawers cupboard");
            A("Outdoor", "tree", "Tree", 48, 54, 5, 5, "plant oak");
            A("Outdoor", "bush", "Bush", 40, 28, 2, 2, "hedge shrub");
            A("Outdoor", "car", "Car", 70, 36, 4.5, 2, "vehicle");
            A("Outdoor", "bus", "Bus", 110, 40, 12, 2.6, "coach vehicle");
            A("Outdoor", "bicycle", "Bicycle", 56, 34, 1.8, 0.7, "bike cycle");
            A("Outdoor", "person", "Person", 26, 56, 0.7, 0.7, "people user human");
            A("Outdoor", "streetlight", "Street light", 26, 70, 1, 1, "lamp post");
            A("Telecom", "celltower", "Cell tower", 46, 90, 5, 5, "mobile mast 4g 5g phone");
            A("Telecom", "dish", "Satellite dish", 54, 54, 2, 2, "satellite");
            A("Telecom", "antenna", "Antenna", 34, 60, 1, 1, "aerial radio wireless");
            A("Telecom", "pole", "Utility pole", 40, 90, 1.5, 1.5, "telegraph wires cable");
            A("Telecom", "manhole", "Manhole / duct", 46, 46, 1, 1, "underground cable duct pit");
            A("Fun", "coffee", "Coffee machine", 40, 50, 0.5, 0.5, "kitchen drink");
            A("Fun", "plant", "Plant", 34, 50, 0.6, 0.6, "pot flower");
        }

        public static ArtInfo Get(string kind) { return All.FirstOrDefault(a => a.Kind == kind); }

        /// <summary>The objects in a category that match the search words.</summary>
        public static List<ArtInfo> Find(string category, string search)
        {
            var words = (search ?? "").ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            return All.Where(a => (category == null || a.Category == category) && words.All(w => (a.Name + " " + a.Words + " " + a.Category).ToLowerInvariant().Contains(w))).ToList();
        }
    }

    /// <summary>Everything about the drawings at one moment (for undo).</summary>
    public class DrawState
    {
        internal List<Shape> Shapes;
        internal List<Shape> ShapeCopies;
        internal List<Callout> Callouts;
        internal List<Callout> CalloutCopies;
        internal Dictionary<string, MapInfo> Maps;
        internal Dictionary<string, LayerSet> Layers;
        internal string Sig;
    }

    public static class Drawings
    {
        public static readonly string[] LayerIds = { "map", "drawings", "devices", "cables", "labels" };
        public static readonly string[] LayerNames = { "Map", "Drawings", "Devices", "Cables", "Labels" };
        /// <summary>Snap new and moved drawings to the grid.</summary>
        public static bool SnapOn = true;

        /// <summary>Screen pixels per view unit at 100%: 1 on the Logical view, 22 per metre on a floor, 3.2 on the campus.</summary>
        public static double BaseOf(string view) { return view == "logical" ? 1 : view == "campus" ? 3.2 : 22; }
        public static double GridStep(string view) { return view == "logical" ? 10 : view == "campus" ? 5 : 0.5; }
        public static double SnapV(string view, double v) { if (!SnapOn) return v; double g = GridStep(view); return Math.Round(v / g) * g; }

        public static LayerSet LayersOf(Project p, string key)
        {
            LayerSet l;
            if (!p.Layers.TryGetValue(key, out l)) { l = new LayerSet(); p.Layers[key] = l; }
            return l;
        }
        public static MapInfo MapOf(Project p, string key)
        {
            MapInfo m;
            if (!p.Maps.TryGetValue(key, out m)) { m = new MapInfo(); p.Maps[key] = m; }
            return m;
        }
        /// <summary>The layers key of a shape view: logical, campus or floor (all floors share one set).</summary>
        public static string LayerKeyOf(string view) { return view == "logical" || view == "campus" ? view : "floor"; }

        // ---------------------------------------------------------------- new shapes
        /// <summary>A new shape with the usual look for its kind (not added yet).</summary>
        public static Shape Make(Project p, string kind, string view, double x, double y, double w, double h)
        {
            var s = new Shape { Id = p.NewId("s"), Kind = kind, View = view, X = x, Y = y, W = w, H = h };
            switch (kind)
            {
                case "roundrect": s.Corner = 14; break;
                case "ellipse": s.Fill = "#1E3A5F"; break;
                case "line": s.Fill = "none"; s.Stroke = "#E2E8F0"; s.StrokeWidth = 2.5; break;
                case "arrow": s.Fill = "none"; s.Stroke = "#FBBF24"; s.StrokeWidth = 3; s.ArrowEnd = true; break;
                case "polyline": s.Fill = "none"; s.Stroke = "#A5B4FC"; s.StrokeWidth = 2.5; break;
                case "freehand": s.Fill = "none"; s.Stroke = "#F472B6"; s.StrokeWidth = 3; break;
                case "text": s.Fill = "none"; s.Stroke = "none"; s.StrokeWidth = 0; s.Text = "Text"; s.FontSize = 18; s.Bold = true; s.Align = "left"; s.Layer = "labels"; break;
                case "note": s.Fill = "#FDE68A"; s.Stroke = "#CA8A04"; s.StrokeWidth = 1; s.Text = "Note"; s.TextColor = "#1F2937"; s.FontSize = 13; s.Align = "left"; s.Rotation = 0; break;
                case "area": s.Fill = "#22C55E"; s.Stroke = "#22C55E"; s.Dash = "dash"; s.StrokeWidth = 2; s.Text = "Area"; s.TextColor = "#BBF7D0"; s.FontSize = 15; s.Bold = true; s.Align = "left"; s.Corner = 10; break;
                case "image": s.Fill = "none"; s.Stroke = "none"; s.StrokeWidth = 0; break;
                case "object": s.Fill = "none"; s.Stroke = "none"; s.StrokeWidth = 0; s.TextColor = "#E2E8F0"; s.FontSize = 12; break;
            }
            return s;
        }

        /// <summary>A clip-art object of the library, at its usual size for the view, centred on (x, y).</summary>
        public static Shape MakeObject(Project p, string art, string view, double x, double y)
        {
            var a = ObjectCatalog.Get(art);
            if (a == null) throw new InvalidOperationException("There is no object called " + art + ".");
            bool metres = view != "logical";
            double w = metres ? a.MW : a.PxW, h = metres ? a.MH : a.PxH;
            var s = Make(p, "object", view, x - w / 2, y - h / 2, w, h);
            s.Art = art;
            s.Layer = a.Layer;
            return s;
        }

        public static void Add(Project p, UndoStack u, Shape s, string name)
        {
            var before = Capture(p);
            p.Shapes.Add(s);
            Commit(p, u, name ?? ("add " + KindName(s).ToLowerInvariant()), before);
        }

        public static string KindName(Shape s)
        {
            switch (s.Kind)
            {
                case "rect": return Math.Abs(s.W - s.H) < 0.01 ? "Square" : "Rectangle";
                case "roundrect": return "Rounded rectangle";
                case "ellipse": return Math.Abs(s.W - s.H) < 0.01 ? "Circle" : "Ellipse";
                case "line": return "Line";
                case "arrow": return "Arrow";
                case "polyline": return "Lines";
                case "freehand": return "Freehand drawing";
                case "text": return "Text";
                case "note": return "Sticky note";
                case "area": return "Area";
                case "image": return "Picture";
                case "object": { var a = ObjectCatalog.Get(s.Art); return a != null ? a.Name : "Object"; }
            }
            return s.Kind;
        }

        // ---------------------------------------------------------------- undo
        public static DrawState Capture(Project p)
        {
            var st = new DrawState
            {
                Shapes = new List<Shape>(p.Shapes),
                ShapeCopies = p.Shapes.Select(s => s.Clone()).ToList(),
                Callouts = new List<Callout>(p.Callouts),
                CalloutCopies = p.Callouts.Select(c => c.Clone()).ToList(),
                Maps = p.Maps.ToDictionary(kv => kv.Key, kv => kv.Value.Clone()),
                Layers = p.Layers.ToDictionary(kv => kv.Key, kv => kv.Value.Clone()),
            };
            st.Sig = Json.Write(SaveJson(st.ShapeCopies, st.CalloutCopies, st.Maps, st.Layers, false));
            return st;
        }

        public static void Restore(Project p, DrawState st)
        {
            p.Shapes.Clear();
            for (int i = 0; i < st.Shapes.Count; i++) { st.Shapes[i].CopyFrom(st.ShapeCopies[i]); p.Shapes.Add(st.Shapes[i]); }
            p.Callouts.Clear();
            for (int i = 0; i < st.Callouts.Count; i++) { st.Callouts[i].CopyFrom(st.CalloutCopies[i]); p.Callouts.Add(st.Callouts[i]); }
            p.Maps.Clear(); foreach (var kv in st.Maps) p.Maps[kv.Key] = kv.Value.Clone();
            p.Layers.Clear(); foreach (var kv in st.Layers) p.Layers[kv.Key] = kv.Value.Clone();
            p.Touch();
        }

        /// <summary>Remembers the change since "before" as one undo step (nothing if nothing changed).</summary>
        public static bool Commit(Project p, UndoStack u, string name, DrawState before)
        {
            var after = Capture(p);
            if (after.Sig == before.Sig && after.Shapes.SequenceEqual(before.Shapes) && after.Callouts.SequenceEqual(before.Callouts)) return false;
            p.Touch();
            if (u != null) u.Push(name, () => Restore(p, after), () => Restore(p, before));
            return true;
        }

        public static void Edit(Project p, UndoStack u, string name, Action change)
        {
            var before = Capture(p);
            change();
            Commit(p, u, name, before);
        }

        // ---------------------------------------------------------------- arranging
        /// <summary>The selected shapes plus the rest of their groups.</summary>
        public static List<Shape> WithGroups(Project p, IEnumerable<Shape> sel)
        {
            var set = new List<Shape>();
            foreach (var s in sel)
            {
                if (s.Group.Length > 0) { foreach (var g in p.Shapes.Where(x => x.Group == s.Group)) if (!set.Contains(g)) set.Add(g); }
                else if (!set.Contains(s)) set.Add(s);
            }
            return set;
        }

        /// <summary>front, back, forward, backward</summary>
        public static void Order(Project p, UndoStack u, IEnumerable<Shape> sel, string how)
        {
            var list = WithGroups(p, sel);
            if (list.Count == 0) return;
            Edit(p, u, how == "front" ? "bring to front" : how == "back" ? "send to back" : how == "forward" ? "bring forward" : "send backward", () =>
            {
                var ordered = p.Shapes.Where(list.Contains).ToList();
                if (how == "front") { foreach (var s in ordered) p.Shapes.Remove(s); p.Shapes.AddRange(ordered); }
                else if (how == "back") { foreach (var s in ordered) p.Shapes.Remove(s); p.Shapes.InsertRange(0, ordered); }
                else if (how == "forward")
                {
                    for (int i = p.Shapes.Count - 2; i >= 0; i--)
                        if (list.Contains(p.Shapes[i]) && !list.Contains(p.Shapes[i + 1])) { var t = p.Shapes[i]; p.Shapes[i] = p.Shapes[i + 1]; p.Shapes[i + 1] = t; }
                }
                else
                {
                    for (int i = 1; i < p.Shapes.Count; i++)
                        if (list.Contains(p.Shapes[i]) && !list.Contains(p.Shapes[i - 1])) { var t = p.Shapes[i]; p.Shapes[i] = p.Shapes[i - 1]; p.Shapes[i - 1] = t; }
                }
            });
        }

        /// <summary>Groups count as one thing when aligning and distributing.</summary>
        static List<List<Shape>> Units(Project p, IEnumerable<Shape> sel)
        {
            var units = new List<List<Shape>>();
            var seen = new HashSet<string>();
            foreach (var s in sel)
            {
                if (s.Group.Length > 0) { if (seen.Add(s.Group)) units.Add(p.Shapes.Where(x => x.Group == s.Group).ToList()); }
                else units.Add(new List<Shape> { s });
            }
            return units;
        }
        static Rect UnitBounds(List<Shape> u) { var r = u[0].Bounds; foreach (var s in u.Skip(1)) r.Union(s.Bounds); return r; }

        /// <summary>left, center, right, top, middle, bottom</summary>
        public static void Align(Project p, UndoStack u, IEnumerable<Shape> sel, string how)
        {
            var units = Units(p, sel).Where(x => !x.Any(s => s.Locked)).ToList();
            if (units.Count < 2) return;
            var all = UnitBounds(units[0]); foreach (var x in units.Skip(1)) all.Union(UnitBounds(x));
            Edit(p, u, "align " + how, () =>
            {
                foreach (var un in units)
                {
                    var r = UnitBounds(un);
                    double dx = 0, dy = 0;
                    switch (how)
                    {
                        case "left": dx = all.Left - r.Left; break;
                        case "center": dx = (all.Left + all.Width / 2) - (r.Left + r.Width / 2); break;
                        case "right": dx = all.Right - r.Right; break;
                        case "top": dy = all.Top - r.Top; break;
                        case "middle": dy = (all.Top + all.Height / 2) - (r.Top + r.Height / 2); break;
                        case "bottom": dy = all.Bottom - r.Bottom; break;
                    }
                    foreach (var s in un) s.MoveBy(dx, dy);
                }
            });
        }

        /// <summary>Equal gaps between the selected things, across (h) or down (v).</summary>
        public static void Distribute(Project p, UndoStack u, IEnumerable<Shape> sel, bool across)
        {
            var units = Units(p, sel).Where(x => !x.Any(s => s.Locked)).ToList();
            if (units.Count < 3) return;
            units = units.OrderBy(x => across ? UnitBounds(x).Left : UnitBounds(x).Top).ToList();
            double first = across ? UnitBounds(units[0]).Left : UnitBounds(units[0]).Top;
            var lastR = UnitBounds(units[units.Count - 1]);
            double last = across ? lastR.Right : lastR.Bottom;
            double total = units.Sum(x => across ? UnitBounds(x).Width : UnitBounds(x).Height);
            double gap = (last - first - total) / (units.Count - 1);
            Edit(p, u, across ? "distribute across" : "distribute down", () =>
            {
                double at = first;
                foreach (var un in units)
                {
                    var r = UnitBounds(un);
                    double d = at - (across ? r.Left : r.Top);
                    foreach (var s in un) s.MoveBy(across ? d : 0, across ? 0 : d);
                    at += (across ? r.Width : r.Height) + gap;
                }
            });
        }

        public static string GroupShapes(Project p, UndoStack u, IEnumerable<Shape> sel)
        {
            var list = WithGroups(p, sel);
            if (list.Count < 2) return "";
            string g = p.NewId("g");
            Edit(p, u, "group", () => { foreach (var s in list) s.Group = g; });
            return g;
        }

        public static void Ungroup(Project p, UndoStack u, IEnumerable<Shape> sel)
        {
            var list = WithGroups(p, sel).Where(s => s.Group.Length > 0).ToList();
            if (list.Count == 0) return;
            Edit(p, u, "ungroup", () => { foreach (var s in list) s.Group = ""; });
        }

        /// <summary>Copies of the shapes, moved a little (groups stay groups, with new ids).</summary>
        public static List<Shape> Duplicate(Project p, UndoStack u, IEnumerable<Shape> sel, double dx, double dy)
        {
            var list = WithGroups(p, sel);
            var copies = CopyShapes(p, list, dx, dy, null);
            if (copies.Count == 0) return copies;
            Edit(p, u, "duplicate", () => p.Shapes.AddRange(copies));
            return copies;
        }

        static List<Shape> CopyShapes(Project p, IEnumerable<Shape> list, double dx, double dy, string view)
        {
            var groups = new Dictionary<string, string>();
            var copies = new List<Shape>();
            foreach (var s in list)
            {
                var c = s.Clone();
                c.Id = p.NewId("s");
                c.FromStyle = false;
                if (view != null) c.View = view;
                if (c.Group.Length > 0) { string ng; if (!groups.TryGetValue(c.Group, out ng)) { ng = p.NewId("g"); groups[c.Group] = ng; } c.Group = ng; }
                c.MoveBy(dx, dy);
                copies.Add(c);
            }
            return copies;
        }

        // ---------------------------------------------------------------- copy and paste (also between projects)
        public const string ClipFormat = "NetworkLab.Drawings";

        public static string ToClipText(IEnumerable<Shape> shapes)
        {
            var o = new JObj();
            o["format"] = "netlab-drawings";
            o["shapes"] = shapes.Select(s => (object)ShapeJson(s)).ToList();
            return Json.Write(o);
        }

        /// <summary>Shapes from copied text, with new ids, for the view, moved by (dx, dy); null if it isn't ours.</summary>
        public static List<Shape> FromClipText(Project p, string text, string view, double dx, double dy)
        {
            if (string.IsNullOrEmpty(text) || !text.Contains("netlab-drawings")) return null;
            JObj o;
            try { o = Json.ParseObj(text); } catch (Exception) { return null; }
            if (o == null || o.Str("format") != "netlab-drawings") return null;
            var list = o.Arr("shapes").OfType<JObj>().Select(ShapeFrom).ToList();
            bool fromMetres = list.Count > 0 && list[0].View != "logical", toMetres = view != "logical";
            if (fromMetres != toMetres)
            {
                // pixels <-> metres: keep the look about the same size
                double k = toMetres ? 1 / 22.0 : 22.0;
                if (view == "campus" || (list.Count > 0 && list[0].View == "campus")) k = toMetres ? 1 / 3.2 : 3.2;
                foreach (var s in list) ScaleShape(s, k, 0, 0, false);
            }
            return CopyShapes(p, list, dx, dy, view);
        }

        // ---------------------------------------------------------------- labels on devices and cables
        public static Callout AddCallout(Project p, UndoStack u, string target, string view, string text)
        {
            var c = new Callout { Id = p.NewId("c"), Target = target, View = view, Text = text ?? "" };
            if (view != "logical") c.DY = -1.8;
            bool cable = p.Links.Any(l => l.Id == target);
            if (cable) c.DY = view == "logical" ? -18 : -0.8;
            Edit(p, u, "add label", () => p.Callouts.Add(c));
            return c;
        }

        /// <summary>The VLANs a cable carries, in a few words ("VLAN 10", "Trunk: 10,20"), or "".</summary>
        public static string VlanText(Link l)
        {
            if (l.IsWireless || l.IsConsole) return "";
            var parts = new List<string>();
            foreach (var pt in new[] { l.A, l.B })
            {
                if (pt.Dev.Bridge != null && !pt.IsSerial)
                {
                    if (pt.Trunk) parts.Add("Trunk" + (pt.Allowed != "all" ? " " + pt.Allowed : "") + (pt.NativeVlan != 1 ? " (native " + pt.NativeVlan + ")" : ""));
                    else parts.Add("VLAN " + pt.AccessVlan);
                }
                else if (pt.Dev.Ip != null && pt.Dev.Ios != null)
                {
                    var subs = pt.Dev.Ip.Ifs.Where(f => f.IsSub && f.Port == pt && f.Vlan != 0).Select(f => f.Vlan).OrderBy(v => v).ToList();
                    if (subs.Count > 0) parts.Add("802.1Q " + string.Join(",", subs.Select(v => v.ToString()).ToArray()));
                }
            }
            parts = parts.Distinct().ToList();
            if (parts.Count == 2 && parts.Any(x => x.StartsWith("Trunk")) && parts.Any(x => x.StartsWith("802.1Q"))) parts = parts.Where(x => x.StartsWith("802.1Q")).ToList();
            return string.Join(" / ", parts.ToArray());
        }

        // ---------------------------------------------------------------- maps
        public static readonly string[][] MapStyles =
        {
            new[] { "none", "No map", "Just the plain background." },
            new[] { "grid", "Blank grid", "A plain map with grid lines, to draw on." },
            new[] { "streets", "Street map", "Streets, blocks, parks and a few buildings." },
            new[] { "park", "Park / campus", "Lawns, paths, a pond and trees." },
            new[] { "island", "Island", "An island in the sea with a beach, trees and a jetty." },
        };

        /// <summary>Puts a drawn map under a view. The map's pieces are shapes on the Map layer (editable once the
        /// layer is unlocked); a new style replaces the pieces of the old one.</summary>
        public static void ApplyMapStyle(Project p, UndoStack u, string view, string style, Rect extent)
        {
            Edit(p, u, "map: " + style, () =>
            {
                p.Shapes.RemoveAll(s => s.View == view && s.FromStyle);
                var m = MapOf(p, view);
                m.Style = style;
                bool metres = view != "logical";
                var rng = new Random(1234 + style.Length * 97);
                Func<string, double, double, double, double, Shape> add = (kind, x, y, w, h) =>
                {
                    var s = Make(p, kind, view, x, y, w, h);
                    s.Layer = "map"; s.FromStyle = true; s.Text = ""; s.Locked = false;
                    p.Shapes.Add(s);
                    return s;
                };
                Func<string, double, double, double, Shape> obj = (art, x, y, size) =>
                {
                    var s = MakeObject(p, art, view, x, y);
                    double k = size / Math.Max(s.W, s.H);
                    s.X = x - s.W * k / 2; s.Y = y - s.H * k / 2; s.W *= k; s.H *= k;
                    s.Layer = "map"; s.FromStyle = true;
                    p.Shapes.Add(s);
                    return s;
                };
                double x0 = extent.X, y0 = extent.Y, W = extent.Width, H = extent.Height;
                double unit = Math.Min(W, H);
                switch (style)
                {
                    case "grid": m.Color = "#121C2B"; break;
                    case "streets":
                        {
                            m.Color = "#1A2230";
                            int nx = Math.Max(2, (int)Math.Round(W / (unit / 2.2))), ny = Math.Max(2, (int)Math.Round(H / (unit / 2.2)));
                            double road = unit * 0.06;
                            double cw = W / nx, ch = H / ny;
                            // blocks between the roads: houses, parks, a car park
                            for (int i = 0; i < nx; i++)
                                for (int j = 0; j < ny; j++)
                                {
                                    double bx = x0 + i * cw + road * 0.9, by = y0 + j * ch + road * 0.9, bw = cw - road * 1.8, bh = ch - road * 1.8;
                                    int pick = rng.Next(6);
                                    var blk = add("rect", bx, by, bw, bh);
                                    blk.Fill = pick == 0 ? "#1F3B2A" : "#242E3E"; blk.Stroke = "none"; blk.StrokeWidth = 0; blk.Corner = 4;
                                    if (pick == 0) { for (int t = 0; t < 4; t++) obj("tree", bx + bw * (0.2 + 0.6 * rng.NextDouble()), by + bh * (0.2 + 0.6 * rng.NextDouble()), Math.Min(bw, bh) * 0.28); }
                                    else if (pick == 1) obj("carpark", bx + bw / 2, by + bh / 2, Math.Min(bw, bh) * 0.9);
                                    else
                                    {
                                        int n = 2 + rng.Next(3);
                                        for (int t = 0; t < n; t++)
                                        {
                                            double hw = bw / (n + 0.6), hh = bh * (0.28 + 0.1 * rng.NextDouble());
                                            var hs = add("rect", bx + bw * 0.06 + t * (bw * 0.88 / n), by + bh * 0.1, hw * 0.82, hh);
                                            hs.Fill = t % 2 == 0 ? "#334155" : "#3B4A60"; hs.Stroke = "#475569"; hs.StrokeWidth = 1; hs.Corner = 2;
                                            var h2 = add("rect", bx + bw * 0.06 + t * (bw * 0.88 / n), by + bh * 0.58, hw * 0.82, hh);
                                            h2.Fill = t % 2 == 1 ? "#334155" : "#3B4A60"; h2.Stroke = "#475569"; h2.StrokeWidth = 1; h2.Corner = 2;
                                        }
                                    }
                                }
                            // the roads, with a dashed middle line
                            for (int i = 0; i <= nx; i++)
                            {
                                double rx = x0 + i * cw;
                                var r = add("line", 0, 0, 0, 0); r.Points.Add(new Point2(rx, y0)); r.Points.Add(new Point2(rx, y0 + H)); r.Stroke = "#3F4B5E"; r.StrokeWidth = road * BaseOf(view); r.FitPoints();
                                var c = add("line", 0, 0, 0, 0); c.Points.Add(new Point2(rx, y0)); c.Points.Add(new Point2(rx, y0 + H)); c.Stroke = "#94A3B8"; c.StrokeWidth = 1; c.Dash = "dash"; c.FitPoints();
                            }
                            for (int j = 0; j <= ny; j++)
                            {
                                double ry = y0 + j * ch;
                                var r = add("line", 0, 0, 0, 0); r.Points.Add(new Point2(x0, ry)); r.Points.Add(new Point2(x0 + W, ry)); r.Stroke = "#3F4B5E"; r.StrokeWidth = road * BaseOf(view); r.FitPoints();
                                var c = add("line", 0, 0, 0, 0); c.Points.Add(new Point2(x0, ry)); c.Points.Add(new Point2(x0 + W, ry)); c.Stroke = "#94A3B8"; c.StrokeWidth = 1; c.Dash = "dash"; c.FitPoints();
                            }
                            break;
                        }
                    case "park":
                        {
                            m.Color = "#173222";
                            var pond = add("ellipse", x0 + W * 0.62, y0 + H * 0.55, W * 0.24, H * 0.26); pond.Fill = "#1E4A6B"; pond.Stroke = "#2563EB"; pond.StrokeWidth = 2;
                            var path = add("polyline", 0, 0, 0, 0); path.Stroke = "#8B7D5B"; path.StrokeWidth = 6;
                            for (int i = 0; i <= 8; i++) path.Points.Add(new Point2(x0 + W * i / 8.0, y0 + H * (0.5 + 0.22 * Math.Sin(i * 0.9))));
                            path.FitPoints();
                            var path2 = add("polyline", 0, 0, 0, 0); path2.Stroke = "#8B7D5B"; path2.StrokeWidth = 5;
                            for (int i = 0; i <= 6; i++) path2.Points.Add(new Point2(x0 + W * (0.3 + 0.08 * Math.Sin(i * 1.3)), y0 + H * i / 6.0));
                            path2.FitPoints();
                            for (int t = 0; t < 26; t++)
                            {
                                double tx = x0 + W * (0.04 + 0.92 * rng.NextDouble()), ty = y0 + H * (0.04 + 0.92 * rng.NextDouble());
                                if (tx > x0 + W * 0.58 && tx < x0 + W * 0.9 && ty > y0 + H * 0.5 && ty < y0 + H * 0.85) continue;
                                obj(rng.Next(4) == 0 ? "bush" : "tree", tx, ty, unit * (0.05 + 0.04 * rng.NextDouble()));
                            }
                            break;
                        }
                    case "island":
                        {
                            m.Color = "#0E2A45";
                            var sand = add("freehand", 0, 0, 0, 0); sand.Closed = true; sand.Fill = "#C9B27C"; sand.Stroke = "#E5D3A1"; sand.StrokeWidth = 2;
                            var grass = add("freehand", 0, 0, 0, 0); grass.Closed = true; grass.Fill = "#2F6B3A"; grass.Stroke = "none"; grass.StrokeWidth = 0;
                            double cx = x0 + W / 2, cy = y0 + H / 2, rx = W * 0.42, ry = H * 0.4;
                            for (int i = 0; i < 36; i++)
                            {
                                double a = i * Math.PI * 2 / 36, wob = 1 + 0.08 * Math.Sin(i * 1.7) + 0.05 * Math.Cos(i * 3.1);
                                sand.Points.Add(new Point2(cx + Math.Cos(a) * rx * wob, cy + Math.Sin(a) * ry * wob));
                                grass.Points.Add(new Point2(cx + Math.Cos(a) * rx * wob * 0.84, cy + Math.Sin(a) * ry * wob * 0.8));
                            }
                            sand.FitPoints(); grass.FitPoints();
                            var jetty = add("rect", cx + rx * 0.9, cy - unit * 0.015, rx * 0.35, unit * 0.03); jetty.Fill = "#8B5E34"; jetty.Stroke = "#5B3A1E"; jetty.StrokeWidth = 1;
                            for (int t = 0; t < 18; t++)
                            {
                                double a = rng.NextDouble() * Math.PI * 2, d = Math.Sqrt(rng.NextDouble()) * 0.7;
                                obj("tree", cx + Math.Cos(a) * rx * d, cy + Math.Sin(a) * ry * d, unit * (0.05 + 0.03 * rng.NextDouble()));
                            }
                            break;
                        }
                    default: m.Style = "none"; m.Color = ""; break;
                }
                LayersOf(p, LayerKeyOf(view)).Locked.Add("map");
                if (style == "none") LayersOf(p, LayerKeyOf(view)).Locked.Remove("map");
            });
        }

        /// <summary>A picture as the map of the Logical or Campus view, laid over the extent (keeping its shape).</summary>
        public static void SetMapPicture(Project p, UndoStack u, string view, byte[] bytes, Rect extent, double pixelW, double pixelH)
        {
            if (bytes.Length > 6 * 1024 * 1024) throw new InvalidOperationException("That picture is bigger than 6 MB: please use a smaller one.");
            string b64 = Convert.ToBase64String(bytes);
            Edit(p, u, "map picture", () =>
            {
                var m = MapOf(p, view);
                m.Picture = b64;
                double w = extent.Width, h = pixelW > 0 ? w * pixelH / pixelW : extent.Height;
                m.PX = extent.X; m.PY = extent.Y; m.PW = w; m.PH = h;
            });
        }

        public static void RemoveMapPicture(Project p, UndoStack u, string view)
        {
            Edit(p, u, "remove map picture", () => { var m = MapOf(p, view); m.Picture = ""; });
        }

        /// <summary>Sets the scale: a line of <paramref name="units"/> view units is really <paramref name="metres"/> long.
        /// On the Campus map everything (picture, buildings, roads, drawings) is stretched so distances in metres are
        /// right, which changes the cable lengths between buildings. On a floor plan the picture under it is stretched.
        /// On the Logical view it only sets the scale bar.</summary>
        public static void Calibrate(Project p, UndoStack u, string view, double units, double metres)
        {
            if (units <= 0 || metres <= 0) throw new InvalidOperationException("Draw a line and give its real length in metres.");
            if (view == "logical") { Edit(p, u, "map scale", () => MapOf(p, "logical").MetresPerUnit = metres / units); return; }
            double k = metres / units;
            if (view == "campus")
            {
                string layoutBefore = Json.Write(Physical.SaveLayout(p.Layout));
                var before = Capture(p);
                var L = p.Layout;
                L.CampusW *= k; L.CampusH *= k;
                foreach (var b in L.Buildings) { b.X *= k; b.Y *= k; }
                foreach (var w in L.Paths) { w.X1 *= k; w.Y1 *= k; w.X2 *= k; w.Y2 *= k; }
                foreach (var lb in L.CampusLabels) { lb.X *= k; lb.Y *= k; }
                var m = MapOf(p, "campus");
                m.PX *= k; m.PY *= k; m.PW *= k; m.PH *= k;
                foreach (var s in p.Shapes.Where(x => x.View == "campus")) ScaleShape(s, k, 0, 0, true);
                string layoutAfter = Json.Write(Physical.SaveLayout(p.Layout));
                p.Sim.TopologyChanged();
                var after = Capture(p);
                p.Touch();
                if (u != null)
                    u.Push("map scale", () => { p.Layout = Physical.LoadLayout(Json.ParseObj(layoutAfter)); Restore(p, after); p.Sim.TopologyChanged(); },
                                        () => { p.Layout = Physical.LoadLayout(Json.ParseObj(layoutBefore)); Restore(p, before); p.Sim.TopologyChanged(); });
                return;
            }
            var f = p.Layout.Floor(view);
            if (f == null) return;
            double bw0 = f.BgW, bh0 = f.BgH;
            Action doIt = () => { f.BgW = (bw0 > 0 ? bw0 : f.W) * k; f.BgH = (bh0 > 0 ? bh0 : f.H) * k; p.Touch(); };
            doIt();
            if (u != null) u.Push("plan scale", doIt, () => { f.BgW = bw0; f.BgH = bh0; p.Touch(); });
        }

        /// <summary>Stretches a shape by k around (ox, oy); with sizes, line widths stay as they are.</summary>
        public static void ScaleShape(Shape s, double k, double ox, double oy, bool keepLook)
        {
            s.X = ox + (s.X - ox) * k; s.Y = oy + (s.Y - oy) * k; s.W *= k; s.H *= k;
            for (int i = 0; i < s.Points.Count; i++) s.Points[i] = new Point2(ox + (s.Points[i].X - ox) * k, oy + (s.Points[i].Y - oy) * k);
        }

        // ---------------------------------------------------------------- saving


        public static JObj ShapeJson(Shape s)
        {
            var o = new JObj();
            o["id"] = s.Id; o["kind"] = s.Kind; o["view"] = s.View; o["layer"] = s.Layer;
            o["x"] = s.X; o["y"] = s.Y; o["w"] = s.W; o["h"] = s.H;
            if (s.Rotation != 0) o["rotation"] = s.Rotation;
            if (s.Points.Count > 0) o["points"] = s.Points.Select(pt => (object)new List<object> { pt.X, pt.Y }).ToList();
            if (s.Closed) o["closed"] = true;
            o["fill"] = s.Fill; o["stroke"] = s.Stroke; o["strokeWidth"] = s.StrokeWidth;
            if (s.Dash != "solid") o["dash"] = s.Dash;
            if (s.Opacity != 1) o["opacity"] = s.Opacity;
            if (s.Corner != 0) o["corner"] = s.Corner;
            if (s.Text.Length > 0) o["text"] = s.Text;
            o["font"] = s.Font; o["fontSize"] = s.FontSize; o["textColor"] = s.TextColor; o["align"] = s.Align;
            if (s.Bold) o["bold"] = true;
            if (s.Italic) o["italic"] = true;
            if (s.ArrowStart) o["arrowStart"] = true;
            if (s.ArrowEnd) o["arrowEnd"] = true;
            if (s.Locked) o["locked"] = true;
            if (s.Above) o["above"] = true;
            if (s.FromStyle) o["fromStyle"] = true;
            if (s.Group.Length > 0) o["group"] = s.Group;
            if (s.Art.Length > 0) o["art"] = s.Art;
            if (s.Picture.Length > 0) o["picture"] = s.Picture;
            return o;
        }

        public static Shape ShapeFrom(JObj o)
        {
            var s = new Shape
            {
                Id = o.Str("id"), Kind = o.Str("kind", "rect"), View = o.Str("view", "logical"), Layer = o.Str("layer", "drawings"),
                X = o.Num("x", 0), Y = o.Num("y", 0), W = o.Num("w", 100), H = o.Num("h", 60), Rotation = o.Num("rotation", 0),
                Closed = o.Bool("closed", false), Fill = o.Str("fill", "none"), Stroke = o.Str("stroke", "none"), StrokeWidth = o.Num("strokeWidth", 0),
                Dash = o.Str("dash", "solid"), Opacity = o.Num("opacity", 1), Corner = o.Num("corner", 0), Text = o.Str("text"),
                Font = o.Str("font", "Segoe UI"), FontSize = o.Num("fontSize", 14), TextColor = o.Str("textColor", "#F1F5F9"), Align = o.Str("align", "center"),
                Bold = o.Bool("bold", false), Italic = o.Bool("italic", false), ArrowStart = o.Bool("arrowStart", false), ArrowEnd = o.Bool("arrowEnd", false),
                Locked = o.Bool("locked", false), Above = o.Bool("above", false), FromStyle = o.Bool("fromStyle", false),
                Group = o.Str("group"), Art = o.Str("art"), Picture = o.Str("picture"),
            };
            foreach (var x in o.Arr("points")) { var a = x as List<object>; if (a != null && a.Count == 2 && a[0] is double && a[1] is double) s.Points.Add(new Point2((double)a[0], (double)a[1])); }
            return s;
        }

        static JObj SaveJson(List<Shape> shapes, List<Callout> callouts, Dictionary<string, MapInfo> maps, Dictionary<string, LayerSet> layers, bool full)
        {
            var o = new JObj();
            o["shapes"] = shapes.Select(s => (object)ShapeJson(s)).ToList();
            o["labels"] = callouts.Select(c => (object)new JObj().Set("id", c.Id).Set("target", c.Target).Set("view", c.View).Set("text", c.Text).Set("dx", c.DX).Set("dy", c.DY)
                .Set("color", c.Color).Set("fill", c.Fill).Set("fontSize", c.FontSize)).ToList();
            var mo = new JObj();
            foreach (var kv in maps.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                var m = kv.Value;
                if (m.Style == "none" && m.Picture.Length == 0 && m.MetresPerUnit == 0) continue;
                var x = new JObj().Set("style", m.Style).Set("color", m.Color);
                if (m.Picture.Length > 0) { x["picture"] = m.Picture; x["px"] = m.PX; x["py"] = m.PY; x["pw"] = m.PW; x["ph"] = m.PH; x["opacity"] = m.Opacity; }
                if (m.MetresPerUnit != 0) x["metresPerUnit"] = m.MetresPerUnit;
                mo[kv.Key] = x;
            }
            o["maps"] = mo;
            var lo = new JObj();
            foreach (var kv in layers.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                if (kv.Value.Hidden.Count == 0 && kv.Value.Locked.Count == 0) continue;
                lo[kv.Key] = new JObj().Set("hidden", kv.Value.Hidden.OrderBy(x => x, StringComparer.Ordinal).Select(x => (object)x).ToList())
                                       .Set("locked", kv.Value.Locked.OrderBy(x => x, StringComparer.Ordinal).Select(x => (object)x).ToList());
            }
            o["layers"] = lo;
            return o;
        }

        /// <summary>Adds the drawings to a project file (format version 2).</summary>
        public static void Save(Project p, JObj root)
        {
            var o = SaveJson(p.Shapes, p.Callouts, p.Maps, p.Layers, true);
            root["drawings"] = o["shapes"];
            root["labels"] = o["labels"];
            root["maps"] = o["maps"];
            root["layers"] = o["layers"];
        }

        /// <summary>Reads the drawings of a project file (older files have none).</summary>
        public static void Load(Project p, JObj root)
        {
            foreach (JObj o in root.Arr("drawings").OfType<JObj>()) p.Shapes.Add(ShapeFrom(o));
            foreach (JObj o in root.Arr("labels").OfType<JObj>())
                p.Callouts.Add(new Callout { Id = o.Str("id"), Target = o.Str("target"), View = o.Str("view", "logical"), Text = o.Str("text"), DX = o.Num("dx", 0), DY = o.Num("dy", -48),
                                             Color = o.Str("color", "#F8FAFC"), Fill = o.Str("fill", "#1E293B"), FontSize = o.Num("fontSize", 12) });
            var maps = root.Obj("maps");
            if (maps != null)
                foreach (var kv in maps)
                {
                    var x = kv.Value as JObj;
                    if (x == null) continue;
                    p.Maps[kv.Key] = new MapInfo { Style = x.Str("style", "none"), Color = x.Str("color"), Picture = x.Str("picture"), PX = x.Num("px", 0), PY = x.Num("py", 0), PW = x.Num("pw", 0), PH = x.Num("ph", 0),
                                                   Opacity = x.Num("opacity", 0.9), MetresPerUnit = x.Num("metresPerUnit", 0) };
                }
            var layers = root.Obj("layers");
            if (layers != null)
                foreach (var kv in layers)
                {
                    var x = kv.Value as JObj;
                    if (x == null) continue;
                    var l = new LayerSet();
                    foreach (var h in x.Arr("hidden")) l.Hidden.Add(Convert.ToString(h, CultureInfo.InvariantCulture));
                    foreach (var h in x.Arr("locked")) l.Locked.Add(Convert.ToString(h, CultureInfo.InvariantCulture));
                    p.Layers[kv.Key] = l;
                }
        }
    }
}
