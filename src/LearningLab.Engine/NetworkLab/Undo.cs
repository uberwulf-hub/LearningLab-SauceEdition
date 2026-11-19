// Network Lab - undo and redo for changes to the workspace (devices, cables, notes, positions, names).
using System;
using System.Collections.Generic;
using System.Linq;

namespace NetworkLab
{
    public class UndoStack
    {
        class Item { public string Name; public Action Do, Undo; }
        readonly List<Item> undo = new List<Item>();
        readonly List<Item> redo = new List<Item>();
        public event EventHandler Changed;
        public int Max = 100;

        /// <summary>Runs an action and remembers how to take it back.</summary>
        public void Do(string name, Action doIt, Action undoIt)
        {
            doIt();
            Push(name, doIt, undoIt);
        }
        /// <summary>Remembers an action that has already happened.</summary>
        public void Push(string name, Action doIt, Action undoIt)
        {
            undo.Add(new Item { Name = name, Do = doIt, Undo = undoIt });
            if (undo.Count > Max) undo.RemoveAt(0);
            redo.Clear();
            Fire();
        }
        public bool CanUndo { get { return undo.Count > 0; } }
        public bool CanRedo { get { return redo.Count > 0; } }
        public string UndoName { get { return undo.Count > 0 ? undo[undo.Count - 1].Name : ""; } }
        public string RedoName { get { return redo.Count > 0 ? redo[redo.Count - 1].Name : ""; } }
        public void Undo()
        {
            if (undo.Count == 0) return;
            var i = undo[undo.Count - 1]; undo.RemoveAt(undo.Count - 1);
            i.Undo(); redo.Add(i); Fire();
        }
        public void Redo()
        {
            if (redo.Count == 0) return;
            var i = redo[redo.Count - 1]; redo.RemoveAt(redo.Count - 1);
            i.Do(); undo.Add(i); Fire();
        }
        public void Clear() { undo.Clear(); redo.Clear(); Fire(); }
        /// <summary>Makes the last two steps one (a drag that moved devices and drawings together).</summary>
        public void JoinLast(string name)
        {
            if (undo.Count < 2) return;
            var b = undo[undo.Count - 1]; var a = undo[undo.Count - 2];
            undo.RemoveRange(undo.Count - 2, 2);
            undo.Add(new Item { Name = name ?? a.Name, Do = () => { a.Do(); b.Do(); }, Undo = () => { b.Undo(); a.Undo(); } });
            Fire();
        }
        public int Depth { get { return undo.Count; } }
        void Fire() { var h = Changed; if (h != null) h(this, EventArgs.Empty); }
    }

    /// <summary>Workspace edits that can be undone (used by both views).</summary>
    public static class Edits
    {
        public static Device AddDevice(Project p, UndoStack u, string kind, double x, double y)
        {
            var d = p.Add(kind, x, y, null);
            u.Push("add " + d.Name, () => Restore(p, d, new List<Link>()), () => p.Remove(d));
            p.Touch();
            return d;
        }

        public static void Restore(Project p, Device d, List<Link> links)
        {
            if (!p.Devices.Contains(d)) p.Devices.Add(d);
            foreach (var l in links) Relink(p, l);
            p.Sim.TopologyChanged();
        }

        static void Relink(Project p, Link l)
        {
            if (p.Links.Contains(l)) return;
            if (!p.Devices.Contains(l.A.Dev) || !p.Devices.Contains(l.B.Dev)) return;
            if (l.A.Links.Count > 0 || l.B.Links.Count > 0) return;
            l.A.Links.Add(l); l.B.Links.Add(l);
            p.Links.Add(l);
        }

        public static Link Connect(Project p, UndoStack u, Port a, Port b, string cable)
        {
            var l = p.Connect(a, b, cable);
            u.Push("cable " + a.FullName + " - " + b.FullName, () => { Relink(p, l); p.Sim.TopologyChanged(); }, () => p.Disconnect(l));
            return l;
        }

        public static void Delete(Project p, UndoStack u, IEnumerable<object> items)
        {
            var devs = items.OfType<Device>().ToList();
            var notes = items.OfType<Note>().ToList();
            var links = items.OfType<Link>().Where(l => !l.IsWireless).ToList();
            foreach (var d in devs) foreach (var pt in d.Ports) foreach (var l in pt.Links) if (!l.IsWireless && !links.Contains(l)) links.Add(l);
            // drawings (a group goes as a whole) and labels, also the labels of deleted devices and cables
            var shapes = Drawings.WithGroups(p, items.OfType<Shape>());
            var ids = new HashSet<string>(devs.Select(d => d.Id).Concat(links.Select(l => l.Id)));
            var callouts = items.OfType<Callout>().Concat(p.Callouts.Where(c => ids.Contains(c.Target))).Distinct().ToList();
            if (devs.Count == 0 && notes.Count == 0 && links.Count == 0 && shapes.Count == 0 && callouts.Count == 0) return;
            DrawState drawBefore = shapes.Count > 0 || callouts.Count > 0 ? Drawings.Capture(p) : null;
            Action doIt = () =>
            {
                foreach (var l in links) if (p.Links.Contains(l)) p.Disconnect(l, false);
                foreach (var d in devs) p.Remove(d);
                foreach (var n in notes) p.Notes.Remove(n);
                foreach (var s in shapes) p.Shapes.Remove(s);
                foreach (var c in callouts) p.Callouts.Remove(c);
                p.Sim.TopologyChanged();
                p.Touch();
            };
            Action undoIt = () =>
            {
                foreach (var d in devs) if (!p.Devices.Contains(d)) p.Devices.Add(d);
                foreach (var l in links) Relink(p, l);
                foreach (var n in notes) if (!p.Notes.Contains(n)) p.Notes.Add(n);
                if (drawBefore != null) Drawings.Restore(p, drawBefore);
                p.Sim.TopologyChanged();
                p.Touch();
            };
            u.Do("delete", doIt, undoIt);
        }

        public static void Moved(Project p, UndoStack u, Dictionary<object, Point2> before)
        {
            var after = new Dictionary<object, Point2>();
            foreach (var k in before.Keys) after[k] = Pos(k);
            if (before.All(kv => kv.Value.Equals(after[kv.Key]))) return;
            u.Push("move", () => { foreach (var kv in after) Set(kv.Key, kv.Value); p.Touch(); }, () => { foreach (var kv in before) Set(kv.Key, kv.Value); p.Touch(); });
        }

        /// <summary>Physical-view moves (metres on a floor).</summary>
        public static void MovedPhysical(Project p, UndoStack u, Dictionary<Device, Point2> before)
        {
            var after = new Dictionary<Device, Point2>();
            foreach (var k in before.Keys) after[k] = new Point2(k.PX, k.PY);
            if (before.All(kv => kv.Value.Equals(after[kv.Key]))) return;
            u.Push("move", () => { foreach (var kv in after) { kv.Key.PX = kv.Value.X; kv.Key.PY = kv.Value.Y; } p.Sim.TopologyChanged(); }, () => { foreach (var kv in before) { kv.Key.PX = kv.Value.X; kv.Key.PY = kv.Value.Y; } p.Sim.TopologyChanged(); });
        }

        public static Point2 Pos(object o)
        {
            var d = o as Device; if (d != null) return new Point2(d.X, d.Y);
            var n = o as Note; if (n != null) return new Point2(n.X, n.Y);
            var sh = o as Shape; if (sh != null) return new Point2(sh.X, sh.Y);
            var c = o as Callout; if (c != null) return new Point2(c.DX, c.DY);
            return new Point2(0, 0);
        }
        public static void Set(object o, Point2 v)
        {
            var d = o as Device; if (d != null) { d.X = v.X; d.Y = v.Y; }
            var n = o as Note; if (n != null) { n.X = v.X; n.Y = v.Y; }
            var sh = o as Shape; if (sh != null) sh.MoveBy(v.X - sh.X, v.Y - sh.Y);
            var c = o as Callout; if (c != null) { c.DX = v.X; c.DY = v.Y; }
        }

        public static void Rename(Project p, UndoStack u, Device d, string name)
        {
            string old = d.Name;
            name = name.Trim();
            if (name.Length == 0 || name == old) return;
            if (p.Find(name) != null && p.Find(name) != d) throw new InvalidOperationException("There is already a device called " + name + ".");
            u.Do("rename", () => { d.Name = name; p.Touch(); }, () => { d.Name = old; p.Touch(); });
        }

        public static Note AddNote(Project p, UndoStack u, double x, double y, string view)
        {
            var n = new Note { Id = p.NewId("n"), X = x, Y = y, Text = "Note", View = view };
            u.Do("add note", () => { if (!p.Notes.Contains(n)) p.Notes.Add(n); p.Touch(); }, () => { p.Notes.Remove(n); p.Touch(); });
            return n;
        }

        public static void EditNote(Project p, UndoStack u, Note n, string text)
        {
            string old = n.Text;
            if (text == old) return;
            u.Do("edit note", () => { n.Text = text; p.Touch(); }, () => { n.Text = old; p.Touch(); });
        }
    }

    public struct Point2
    {
        public double X, Y;
        public Point2(double x, double y) { X = x; Y = y; }
        public override bool Equals(object o) { return o is Point2 && ((Point2)o).X == X && ((Point2)o).Y == Y; }
        public override int GetHashCode() { return X.GetHashCode() ^ Y.GetHashCode(); }
    }
}
