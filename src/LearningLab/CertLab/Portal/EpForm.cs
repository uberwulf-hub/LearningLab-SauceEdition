// Cert Lab - the practice portal's forms (App Hub's UiEpForms / UiEpFormSubmit / UiEpEditors / UiEpCatalog), drawn from
// the kind's schema (EpSchemas): a wizard for new objects (Basics, the settings by section, Assignments, Review +
// create), one page for editing, and the tenant settings parts. Fields appear and disappear as choices change
// (EpField.Shows); validation messages come from EpValidate / EpOps and stand under their field. The step buttons
// wrap on a phone and every field takes the whole width there. Nothing here changes the tenant except through EpOps.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using LearningLab.Views;
using CertLab;

namespace LearningLab.CertLab.Portal;

public partial class EpForm : StackPanel
{
    public readonly EpWorkspace W;
    public readonly EpKind K;
    public readonly EpObj Existing;
    public readonly Dictionary<string, string> Values = new(StringComparer.OrdinalIgnoreCase);
    public readonly HashSet<string> Fixed = new(StringComparer.OrdinalIgnoreCase);
    public readonly List<EpAssign> Pending = new();
    public readonly Dictionary<string, string> FieldErrors = new(StringComparer.OrdinalIgnoreCase);
    public readonly List<string> TopErrors = new();
    public readonly Dictionary<string, Action<string>> Setters = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, RuleBox> Rules = new(StringComparer.OrdinalIgnoreCase);
    public int Step;
    public EpResult Result;
    readonly string onlySection, onlyKey;
    bool rendering;
    public bool Wizard => Existing == null;
    public bool TenantPart => K.Kind == "tenant" && (onlySection != null || onlyKey != null);
    EpTenant T => W.T;
    bool Narrow => W.Narrow;

    public EpForm(EpWorkspace w, string kind, EpObj existing, string presets, string section, string key)
    {
        W = w; K = EpSchemas.Get(kind); Existing = existing; onlySection = section; onlyKey = key;
        if (existing != null)
        {
            Values["name"] = existing.Name;
            foreach (var kv in existing.P) Values[kv.Key] = kv.Value;
        }
        foreach (var f in K.Fields) if (!Values.ContainsKey(f.Key) && f.Default.Length > 0) Values[f.Key] = f.Default;
        if (!string.IsNullOrEmpty(presets))
            foreach (var p in presets.Split(';'))
            {
                int eq = p.IndexOf('=');
                if (eq > 0) { Values[p.Substring(0, eq)] = p.Substring(eq + 1); Fixed.Add(p.Substring(0, eq)); }
            }
        Render();
    }

    public string Get(string key) => Values.TryGetValue(key, out var v) ? v : "";
    bool ShowsField(EpField f) => f.Shows(Get) && (onlyKey == null || EpText.Eq(f.Key, onlyKey)) && (onlySection == null || f.Section == onlySection);
    public bool CatalogStep => K.Kind == "config" && Get("type") == "Settings catalog";

    /// <summary>The wizard's steps for the choices made so far.</summary>
    public List<string> Steps()
    {
        var l = new List<string> { "Basics" };
        foreach (var f in K.Fields) if (f.Section != "Basics" && ShowsField(f) && !l.Contains(f.Section)) l.Add(f.Section);
        if (CatalogStep && !l.Contains("Configuration settings")) l.Add("Configuration settings");
        if (K.Assignable && !l.Contains("Assignments")) l.Add("Assignments");
        if (Wizard) l.Add("Review + create");
        return l;
    }

    /// <summary>Sets a field as a person would (through its control, so the form reacts the same way).</summary>
    public void Set(string key, string value)
    {
        if (Setters.TryGetValue(key, out var s)) s(value);
        else { if (string.IsNullOrEmpty(value)) Values.Remove(key); else Values[key] = value; Render(); }
    }

    public void Render()
    {
        rendering = true;
        try { RenderCore(); }
        finally { rendering = false; }
    }

    void RenderCore()
    {
        Children.Clear(); Setters.Clear(); Rules.Clear();
        var steps = Steps();
        if (Step >= steps.Count) Step = steps.Count - 1;
        if (TenantPart) Children.Add(new TextBlock { Text = onlyKey != null ? "Setting" : onlySection, FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = Pk.B(Ui.AccentText), Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap });
        else
        {
            string title = Wizard ? "Create " + K.Title.ToLowerInvariant() : K.Kind == "tenant" ? "Tenant settings" : "Edit " + K.Title.ToLowerInvariant() + ": " + Existing.Name;
            var t = Pk.Txt(title, 20, true); t.Margin = new Thickness(0, 4, 0, 2);
            Children.Add(t);
            if (Fixed.Count > 0) Children.Add(Pk.Small(string.Join("   |   ", Fixed.Select(x => EpSchemas.Label(K.Kind, x) + ": " + Get(x)))));
        }
        if (Wizard)
        {
            // the steps: wrapped on a wide screen, one row to swipe on a phone
            Panel bar = Narrow ? new StackPanel() : new WrapPanel();
            bar.Margin = new Thickness(0, 6, 0, 8);
            for (int i = 0; i < steps.Count; i++)
            {
                int si = i;
                bool err = steps[i] != "Review + create" && StepHasError(steps[i]);
                var b = Pk.Small((i + 1) + "  " + steps[i], () => { if (si <= Step || CheckStep()) { Step = si; Render(); } });
                Pk.Picked(b, i == Step);
                if (err) b.Foreground = Pk.Bad;
                bar.Children.Add(b);
            }
            if (Narrow) { Children.Add(Pk.Txt("Step " + (Step + 1) + " of " + steps.Count + ": " + steps[Step], 13.5, false, Pk.LabelC)); Children.Add(Pk.Strip((StackPanel)bar)); }
            else Children.Add(bar);
        }
        if (TopErrors.Count > 0 || FieldErrors.Count > 0 && Wizard)
        {
            var sp = new StackPanel();
            foreach (var e in TopErrors) sp.Children.Add(Pk.Error(e));
            if (FieldErrors.Count > 0) sp.Children.Add(Pk.Error("Some fields need attention: see the messages under them."));
            Children.Add(new Border { Child = sp, Background = Pk.B(Pk.BadSoftC), CornerRadius = new CornerRadius(8), Padding = new Thickness(12, 6), Margin = new Thickness(0, 0, 0, 8) });
        }
        var body = new StackPanel();
        if (Wizard) BuildStep(body, steps[Step]);
        else foreach (var sec in steps)
            {
                if (!TenantPart && steps.Count > 1) body.Children.Add(Pk.H(sec));
                BuildStep(body, sec);
            }
        Children.Add(body);
        Children.Add(Buttons(steps));
    }

    bool StepHasError(string sec) => FieldErrors.Keys.Any(k => { var f = K.Field(k); return f != null ? f.Section == sec : sec == "Configuration settings"; });

    void BuildStep(Panel body, string sec)
    {
        if (sec == "Review + create") { Review(body); return; }
        foreach (var f in K.Fields.Where(x => x.Section == sec && ShowsField(x))) body.Children.Add(FieldView(f));
        if (sec == "Configuration settings" && CatalogStep) body.Children.Add(CatalogEditor());
        if (sec == "Assignments" && K.Assignable) body.Children.Add(new AssignEditor(W, Existing, K, Wizard ? Pending : null));
    }

    Control FieldView(EpField f)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        var lab = Pk.Txt(f.Label + (f.Required ? " *" : ""), 14, false, Pk.LabelC);
        lab.Margin = new Thickness(0, 0, 0, 4);
        sp.Children.Add(lab);
        if (Fixed.Contains(f.Key) && Wizard) sp.Children.Add(Pk.Txt(Get(f.Key), 14, true));
        else sp.Children.Add(Editor(f));
        if (f.Help.Length > 0) sp.Children.Add(Pk.Small(f.Help));
        if (FieldErrors.TryGetValue(f.Key, out var err)) sp.Children.Add(Pk.Error(err));
        return Pk.Capped(sp, 640);
    }

    Control Buttons(List<string> steps)
    {
        var row = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        if (Wizard)
        {
            if (Step > 0) row.Children.Add(Pk.Btn("Back", () => { Step--; Render(); }));
            if (Step < steps.Count - 1) row.Children.Add(Pk.Accent("Next", Next));
            else row.Children.Add(Pk.Accent("Create", () => Submit()));
        }
        else row.Children.Add(Pk.Accent("Save", () => Submit()));
        if (!TenantPart) row.Children.Add(Pk.Btn("Cancel", W.GoBack));
        return row;
    }

    /// <summary>Next: the step's fields are checked first; the form stays on the step while one is wrong.</summary>
    public void Next()
    {
        if (!CheckStep()) { Render(); return; }
        Step++;
        Render();
    }

    // ------------------------------------------------------------ checking and Create / Save
    /// <summary>The object as the form has it now (not in the tenant), with defaults like Create applies.</summary>
    EpObj Draft()
    {
        var o = new EpObj { Kind = K.Kind, Name = Get("name").Trim(), T = T, Id = Existing == null ? "" : Existing.Id };
        foreach (var kv in Values) if (!EpText.Eq(kv.Key, "name") && kv.Value.Length > 0) o.P[kv.Key] = kv.Value;
        foreach (var f in K.Fields) if (f.Default.Length > 0 && o.Get(f.Key).Length == 0 && f.Shows(x => o.Get(x))) o.P[f.Key] = f.Default;
        return o;
    }

    /// <summary>Checks the current step's fields; false (and the messages shown) when one is wrong.</summary>
    public bool CheckStep()
    {
        if (!Wizard) return true;
        var steps = Steps();
        string sec = steps[Math.Min(Step, steps.Count - 1)];
        if (sec == "Review + create" || sec == "Assignments" && !K.Fields.Any(f => f.Section == "Assignments")) return true;
        MapErrors(EpValidate.Check(T, Draft(), Existing));
        foreach (var k in FieldErrors.Keys.ToList())
        {
            var f = K.Field(k);
            string fs = f != null ? f.Section : "Configuration settings";
            if (fs != sec) FieldErrors.Remove(k);
        }
        TopErrors.Clear();
        return FieldErrors.Count == 0;
    }

    /// <summary>What Create / Save sends: every field that shows (empty clears it when editing), plus catalog settings.</summary>
    Dictionary<string, string> Collect()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (K.Kind != "tenant") d["name"] = Get("name").Trim();
        foreach (var f in K.Fields)
        {
            if (f.Key == "name" || !ShowsField(f)) continue;
            string v = Get(f.Key);
            if (v.Length > 0 || Existing != null) d[f.Key] = v;
        }
        if (!TenantPart)
        {
            foreach (var kv in Values) if (kv.Key.StartsWith("s:", StringComparison.OrdinalIgnoreCase)) d[kv.Key] = kv.Value;
            if (Existing != null) foreach (var kv in Existing.P) if (kv.Key.StartsWith("s:", StringComparison.OrdinalIgnoreCase) && !Values.ContainsKey(kv.Key)) d[kv.Key] = "";
        }
        return d;
    }

    /// <summary>Create (wizard) or Save (edit): through EpOps, then the tenant ticks so devices see it at check-in.</summary>
    public EpResult Submit()
    {
        var vals = Collect();
        var r = Existing == null ? EpOps.Create(T, K.Kind, vals) : EpOps.Update(T, Existing, vals);
        Result = r;
        if (!r.Ok)
        {
            MapErrors(r.Errors);
            if (Wizard)
            {
                var steps = Steps();
                Step = FieldErrors.Keys.Select(k => { var f = K.Field(k); return steps.IndexOf(f != null ? f.Section : "Configuration settings"); }).Where(i => i >= 0).DefaultIfEmpty(Step).Min();
            }
            Render();
            W.Say((Existing == null ? "Not created" : "Not saved") + ": " + string.Join(" ", r.Errors), false);
            return r;
        }
        var notes = new List<string> { r.Message };
        bool ok = true;
        if (Existing == null)
            foreach (var a in Pending)
            {
                var ar = EpOps.Assign(T, r.Obj, a.Copy());
                if (!ar.Ok) { ok = false; notes.Add("Assignment to " + a.Group + " failed: " + ar.Text); }
                else notes.AddRange(ar.Warnings);
            }
        if (Existing == null && Pending.Count > 0 && ok) notes.Add("Assigned to " + string.Join(", ", Pending.Select(a => a.Group)) + ". Devices get it at their next check-in (or Sync).");
        T.Tick();
        FieldErrors.Clear(); TopErrors.Clear();
        W.Say(string.Join(" ", notes), ok);
        if (TenantPart || K.Kind == "tenant") Render();
        else W.ReplaceBlade("obj|" + r.Obj.Id);
        return r;
    }

    /// <summary>Each message goes under the field whose label starts it; the rest go to the top.</summary>
    public void MapErrors(IEnumerable<string> errors)
    {
        FieldErrors.Clear(); TopErrors.Clear();
        var fields = K.Fields.Where(ShowsField).OrderByDescending(f => f.Label.Length).ToList();
        foreach (var e in errors)
        {
            string key = null;
            foreach (var f in fields)
                if (e.StartsWith(f.Label + " ", StringComparison.Ordinal) || e.StartsWith(f.Label + ":", StringComparison.Ordinal) || e.StartsWith(f.Label + ".", StringComparison.Ordinal)) { key = f.Key; break; }
            if (key == null && e.StartsWith("Another " + K.Title.ToLowerInvariant() + " is already called", StringComparison.Ordinal)) key = "name";
            if (key == null)
                foreach (var kv in Values.Where(x => x.Key.StartsWith("s:", StringComparison.OrdinalIgnoreCase)))
                {
                    var s = EpCatalog.Find(kv.Key.Substring(2));
                    if (s != null && (e.StartsWith(s.Name + ":", StringComparison.Ordinal) || e.StartsWith(s.Name + " ", StringComparison.Ordinal))) { key = kv.Key; break; }
                }
            if (key == null) { TopErrors.Add(e); continue; }
            FieldErrors[key] = FieldErrors.TryGetValue(key, out var old) ? old + " " + e : e;
        }
    }

    void Review(Panel body)
    {
        body.Children.Add(Pk.Small("Check what you picked, then Create. You can change it later from the object's page."));
        foreach (var sec in Steps().Where(s => s != "Review + create"))
        {
            var fs = K.Fields.Where(f => f.Section == sec && ShowsField(f) && Get(f.Key).Length > 0).ToList();
            var cat = sec == "Configuration settings" ? Values.Where(x => x.Key.StartsWith("s:", StringComparison.OrdinalIgnoreCase)).ToList() : new List<KeyValuePair<string, string>>();
            if (fs.Count == 0 && cat.Count == 0 && sec != "Assignments") continue;
            body.Children.Add(Pk.H(sec));
            foreach (var f in fs) body.Children.Add(Pk.Pair(f.Label, Get(f.Key).Replace("\n", ", "), Narrow));
            foreach (var kv in cat) body.Children.Add(Pk.Pair(EpSchemas.Label("config", kv.Key), kv.Value, Narrow));
            if (sec == "Assignments")
            {
                if (Pending.Count == 0) body.Children.Add(Pk.Small("Not assigned yet: it reaches no one until you assign it."));
                foreach (var a in Pending) body.Children.Add(Pk.Pair(EpOps.IntentName(a.Intent), a.Group + (a.Filter.Length > 0 ? " (filter " + a.Filter + ", " + a.FilterMode + ")" : ""), Narrow));
            }
        }
    }

    // ------------------------------------------------------------ one editor per field type
    bool Dependent(string key) => K.Fields.Any(f => f.If.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase)) || key == "type" && K.Kind == "config";
    void Put(string key, string v) { if (string.IsNullOrEmpty(v)) Values.Remove(key); else Values[key] = v; }

    Control Editor(EpField f)
    {
        string key = f.Key, v = Get(key);
        switch (f.Type)
        {
            case "choice": return ChoiceBox(key, f.Options, v);
            case "group": return ChoiceBox(key, new[] { "" }.Concat(T.GroupsList.Select(g => g.Name).OrderBy(x => x)).ToArray(), v);
            case "user": return ChoiceBox(key, new[] { "" }.Concat(T.Users.Select(u => u["alias"])).ToArray(), v);
            case "app": return ChoiceBox(key, new[] { "" }.Concat(T.Of("app").Where(a => Existing == null || a != Existing).Select(a => a.Name)).ToArray(), v);
            case "multi": return Checks(key, f.Options, v);
            case "cloudapps": return Checks(key, new[] { "All cloud apps" }.Concat(EpSchemas.CloudApps).ToArray(), v);
            case "apps": return Checks(key, T.Of("app").Where(a => Existing == null || a != Existing).Select(a => a.Name).ToArray(), v);
            case "groups": return Picker(key, v, new[] { EpTenant.AllUsers, EpTenant.AllDevices }.Concat(T.GroupsList.Select(g => g.Name)));
            case "people": return Picker(key, v, new[] { EpTenant.AllUsers }.Concat(T.Users.Select(u => u["alias"])).Concat(T.GroupsList.Select(g => g.Name)));
            case "members": return Picker(key, v, T.Users.Select(u => u["alias"]).Concat(T.Devices.Where(d => d.InDirectory).Select(d => d.Name)).Concat(T.GroupsList.Where(g => Existing == null || g != Existing).Select(g => g.Name)));
            case "rule": case "filterrule":
                {
                    var rb = new RuleBox(T, f.Type == "rule" ? "group" : "filter", v, x => Put(key, x), () => f.Type == "rule" ? Get("membership") : Get("platform"));
                    Rules[key] = rb; Setters[key] = x => rb.Box.Text = x;
                    return rb;
                }
            case "package": return PackagePicker(key, v);
            case "storeapp": return StorePicker(key, v);
        }
        bool multi = f.Type == "longtext" || f.Type == "script";
        var t = multi ? Pk.Multi(v, f.Type == "script" ? 180 : 90, f.Type == "script") : Pk.Box(v, f.Type == "number" ? 120 : 300);
        if (f.Type == "number") { t.HorizontalAlignment = HorizontalAlignment.Left; t.Width = 140; }
        else { t.Width = double.NaN; t.HorizontalAlignment = HorizontalAlignment.Stretch; }
        Pk.OnText(t, () => Put(key, t.Text ?? ""));
        Setters[key] = x => t.Text = x ?? "";
        return t;
    }

    Control ChoiceBox(string key, string[] options, string v)
    {
        var c = Pk.Combo(options, v, 260);
        c.SelectionChanged += (s, e) => { if (rendering) return; Pk.Safe(() => { Put(key, Pk.Val(c)); if (Dependent(key)) Render(); }); };
        Setters[key] = x => Pk.Select(c, x);
        return c;
    }

    Control Checks(string key, string[] options, string v)
    {
        if (options.Length == 0) return Pk.Small("Nothing to pick from yet.");
        var cur = EpText.Lines(v);
        var wp = new WrapPanel();
        var boxes = new List<CheckBox>();
        void Sync() { if (!rendering) Put(key, EpText.Join(boxes.Where(b => b.IsChecked == true).Select(b => (string)b.Tag))); }
        foreach (var o in options)
        {
            var cb = Pk.Check(o, cur.Any(x => EpText.Eq(x, o)));
            cb.Tag = o;
            cb.IsCheckedChanged += (s, e) => Sync();
            boxes.Add(cb); wp.Children.Add(cb);
        }
        Setters[key] = x => { var l = EpText.Lines(x); foreach (var b in boxes) b.IsChecked = l.Any(y => EpText.Eq(y, (string)b.Tag)); };
        return wp;
    }

    /// <summary>Names one per line, with a list to add from.</summary>
    Control Picker(string key, string v, IEnumerable<string> candidates)
    {
        var sp = new StackPanel();
        var t = Pk.Multi(v, 80, false);
        Pk.OnText(t, () => Put(key, t.Text ?? ""));
        var add = Pk.Combo(new[] { "" }.Concat(candidates), "", 240);
        Pk.FirstLabel(add, "Add...");
        add.SelectedIndex = 0;
        add.SelectionChanged += (s, e) =>
        {
            if (rendering) return;
            string pick = Pk.Val(add);
            if (pick.Length == 0) return;
            var l = EpText.Lines(t.Text);
            if (!l.Any(x => EpText.Eq(x, pick))) l.Add(pick);
            t.Text = EpText.Join(l);
            add.SelectedIndex = 0;
        };
        sp.Children.Add(t); sp.Children.Add(add);
        Setters[key] = x => t.Text = (x ?? "").Replace(",", "\n");
        return sp;
    }

    Control PackagePicker(string key, string v)
    {
        var sp = new StackPanel();
        var c = Pk.Combo(new[] { "" }.Concat(EpCatalog.Packages.Select(p => p.File)), v, 300);
        sp.Children.Add(c);
        var p0 = EpCatalog.Package(v);
        if (p0 != null) sp.Children.Add(Pk.Small("Inside: " + p0.Setup + "  |  " + p0.App + " " + p0.Version + " by " + p0.Publisher));
        c.SelectionChanged += (s, e) =>
        {
            if (rendering) return;
            Pk.Safe(() =>
            {
                var p = EpCatalog.Package(Pk.Val(c));
                Put(key, Pk.Val(c));
                if (p != null)
                {
                    if (Get("name").Length == 0) Values["name"] = p.App;
                    if (Get("publisher").Length == 0) Values["publisher"] = p.Publisher;
                    if (Get("version").Length == 0) Values["version"] = p.Version;
                }
                Render();
            });
        };
        Setters[key] = x => Pk.Select(c, x);
        return sp;
    }

    Control StorePicker(string key, string v)
    {
        var sp = new StackPanel();
        var cur = EpCatalog.StoreApp(v);
        sp.Children.Add(Pk.Txt(cur == null ? "Nothing picked yet." : "Picked: " + cur.Name + " (" + cur.Id + ")", 14, cur != null));
        var q = Pk.Box("", 280);
        sp.Children.Add(Pk.Small("Search the store:")); sp.Children.Add(q);
        var res = new StackPanel();
        sp.Children.Add(res);
        void Fill()
        {
            res.Children.Clear();
            string qt = (q.Text ?? "").Trim();
            foreach (var a in EpCatalog.Store.Where(x => qt.Length == 0 || x.Name.IndexOf(qt, StringComparison.OrdinalIgnoreCase) >= 0))
            {
                var a0 = a;
                res.Children.Add(Pk.Link(a.Name + "  -  " + a.Publisher + "  (" + a.Id + ")", () =>
                {
                    Put(key, a0.Id);
                    if (Get("name").Length == 0) Values["name"] = a0.Name;
                    if (Get("publisher").Length == 0) Values["publisher"] = a0.Publisher;
                    Render();
                }));
            }
        }
        Pk.OnText(q, () => Fill());
        Fill();
        Setters[key] = x => { var a = EpCatalog.StoreApp(x); Put(key, a == null ? x : a.Id); Render(); };
        return sp;
    }

    // ------------------------------------------------------------ the settings catalog of a configuration profile
    public TextBox CatalogSearch;
    string catalogQuery = "";

    Control CatalogEditor()
    {
        var sp = new StackPanel();
        var added = Values.Where(x => x.Key.StartsWith("s:", StringComparison.OrdinalIgnoreCase)).Select(x => x.Key).ToList();
        sp.Children.Add(Pk.Txt(added.Count == 0 ? "No settings yet. Search the catalog below and add the ones you need." : added.Count + " setting" + (added.Count == 1 ? "" : "s") + " in this profile:", 14));
        foreach (var key in added)
        {
            var s = EpCatalog.Find(key.Substring(2));
            var k0 = key;
            var box = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };
            var head = new DockPanel();
            var rm = Pk.Small("Remove", () => { Values.Remove(k0); FieldErrors.Remove(k0); Render(); });
            DockPanel.SetDock(rm, Dock.Right); head.Children.Add(rm);
            var ht = Pk.Txt(s == null ? key.Substring(2) : s.Category + "  >  " + s.Name, 14, true); ht.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(ht);
            box.Children.Add(head);
            if (s != null && s.Description.Length > 0) box.Children.Add(Pk.Small(s.Description));
            if (s != null && s.Type == "choice")
            {
                var c = Pk.Combo(s.Choices, Get(key), 240);
                c.SelectionChanged += (o, e) => { if (!rendering) Put(k0, Pk.Val(c)); };
                Setters[key] = x => Pk.Select(c, x);
                box.Children.Add(c);
            }
            else
            {
                var t = Pk.Box(Get(key), s != null && s.Type == "number" ? 120 : 280);
                t.HorizontalAlignment = HorizontalAlignment.Left;
                Pk.OnText(t, () => Values[k0] = t.Text ?? "");
                Setters[key] = x => t.Text = x;
                box.Children.Add(t);
                if (s != null && s.Type == "number") box.Children.Add(Pk.Small("A number from " + s.Min + " to " + s.Max + "."));
            }
            if (FieldErrors.TryGetValue(key, out var err)) box.Children.Add(Pk.Error(err));
            sp.Children.Add(new Border { Child = box, BorderBrush = Pk.B(Ui.BorderC), BorderThickness = new Thickness(0, 0, 0, 1) });
        }
        sp.Children.Add(Pk.H("Add settings"));
        CatalogSearch = Pk.Box(catalogQuery, 280);
        sp.Children.Add(Pk.Small("Search by name or category (camera, bluetooth, password...):"));
        sp.Children.Add(CatalogSearch);
        var res = new StackPanel();
        sp.Children.Add(res);
        void Fill()
        {
            res.Children.Clear();
            foreach (var s in CatalogMatches(CatalogSearch.Text).Take(14))
            {
                var s0 = s;
                bool has = Values.ContainsKey("s:" + s.Key);
                var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                var add = Pk.Small(has ? "Added" : "Add", () => AddSetting(s0.Key));
                add.IsEnabled = !has;
                DockPanel.SetDock(add, Dock.Right); row.Children.Add(add);
                // the description shows under the name (App Hub had it as a hover tip; phones have no hover)
                var tx = Pk.Col(Pk.Txt(s.Category + "  >  " + s.Name, 13.5), s.Description.Length > 0 ? Pk.Small(s.Description) : null);
                tx.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(tx);
                res.Children.Add(new Border { Child = row, BorderBrush = Pk.B(Ui.BorderC), BorderThickness = new Thickness(0, 0, 0, 1) });
            }
        }
        Pk.OnText(CatalogSearch, () => { catalogQuery = CatalogSearch.Text ?? ""; Fill(); });
        Fill();
        return sp;
    }

    public static IEnumerable<EpSetting> CatalogMatches(string q)
    {
        q = (q ?? "").Trim();
        return EpCatalog.Settings.Where(s => q.Length == 0 || s.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || s.Category.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || s.Description.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderBy(s => s.Category).ThenBy(s => s.Name);
    }

    /// <summary>Adds a catalog setting with its first choice (a person then picks the value).</summary>
    public void AddSetting(string key)
    {
        var s = EpCatalog.Find(key);
        if (s == null) return;
        Values["s:" + s.Key] = s.Type == "choice" ? s.Choices.First() : s.Type == "number" ? s.Min.ToString() : "";
        Render();
    }
}
