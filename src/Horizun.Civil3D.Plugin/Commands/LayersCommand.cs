// -----------------------------------------------------------------------------
// horizun_c3d_layers - layers and native layer states (block C).
// -----------------------------------------------------------------------------
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Horizun.Civil3D.Core;
using Horizun.Civil3D.Plugin.Civil;

namespace Horizun.Civil3D.Plugin.Commands;

internal sealed class LayersCommand : ICommand
{
    public string Name => "layers";

    public CommandResult Execute(CommandContext ctx)
    {
        if (ToolRules.Validate(ctx.Tool.Name, ctx.Args) is { } why) throw new HzRefusal(ErrorCodes.InvalidInput, why + " Nothing ran.");
        return ctx.Action switch
        {
            "list" => WriteFlow.Read(ctx, (doc, tr, data) => List(ctx, doc, tr, data)),
            "create" => Create(ctx),
            "set" => Set(ctx),
            "set_current" => SetCurrent(ctx),
            "states_list" => WriteFlow.Read(ctx, States),
            "state_save" => StateSave(ctx),
            _ => StateRestore(ctx),
        };
    }

    internal static Regex Wildcard(string pattern) =>
        new("^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$", RegexOptions.IgnoreCase);

    internal static JsonObject Describe(LayerTableRecord l, Transaction tr, Database db, int? objects = null)
    {
        var o = new JsonObject
        {
            ["name"] = l.Name, ["color"] = Cad.ColorJson(l.Color),
            ["linetype"] = ((LinetypeTableRecord)tr.GetObject(l.LinetypeObjectId, OpenMode.ForRead)).Name,
            ["lineweight"] = Cad.LineweightJson(l.LineWeight), ["description"] = l.Description, ["plot"] = l.IsPlottable,
            ["frozen"] = l.IsFrozen, ["locked"] = l.IsLocked, ["off"] = l.IsOff, ["transparency_pct"] = Cad.TransparencyPct(l.Transparency),
            ["current"] = db.Clayer == l.ObjectId, ["xref_dependent"] = l.IsDependent,
        };
        if (objects != null) o["objects"] = objects;
        return o;
    }

    private static void List(CommandContext ctx, Document doc, Transaction tr, JsonObject data)
    {
        var db = doc.Database;
        var rx = Hz.Str(ctx.Args, "pattern") is { } p ? Wildcard(p) : null;
        var limit = (int)(Hz.Num(ctx.Args, "limit") ?? 2000);
        var counts = new Dictionary<ObjectId, int>();
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        foreach (ObjectId bid in bt)
        {
            var btr = (BlockTableRecord)tr.GetObject(bid, OpenMode.ForRead);
            if (!btr.IsLayout) continue;
            foreach (ObjectId eid in btr)
                if (tr.GetObject(eid, OpenMode.ForRead) is Autodesk.AutoCAD.DatabaseServices.Entity e)
                    counts[e.LayerId] = counts.TryGetValue(e.LayerId, out var n) ? n + 1 : 1;
        }
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        var rows = new JsonArray();
        var total = 0;
        foreach (ObjectId id in lt)
        {
            var l = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
            if (rx != null && !rx.IsMatch(l.Name)) continue;
            total++;
            if (rows.Count < limit) rows.Add(Describe(l, tr, db, counts.TryGetValue(id, out var c) ? c : 0));
        }
        data["current"] = ((LayerTableRecord)tr.GetObject(db.Clayer, OpenMode.ForRead)).Name;
        data["count"] = total;
        data["layers"] = rows;
        data["objects_note"] = "objects = entities directly in model/paper space (not inside block definitions).";
    }

    // ---- property plan/apply/verify shared by create and set --------------------

    private sealed class Props
    {
        public JsonNode? Color, Lineweight; public string? Linetype, Description; public bool? Plot, Frozen, Locked, Off; public double? Transparency;
        public static Props From(JsonObject a) => new()
        {
            Color = a["color"], Lineweight = a["lineweight"], Linetype = Hz.Str(a, "linetype"), Description = Hz.Str(a, "description"),
            Plot = Hz.Bool(a, "plot"), Frozen = Hz.Bool(a, "frozen"), Locked = Hz.Bool(a, "locked"), Off = Hz.Bool(a, "off"), Transparency = Hz.Num(a, "transparency"),
        };

        public void Plan(Database db, Transaction tr, JsonObject plan, bool isCurrent)
        {
            if (Linetype != null) plan["linetype_status"] = Cad.LinetypeAvailability(db, tr, Linetype);
            if (Frozen == true && isCurrent) throw new HzRefusal(ErrorCodes.InvalidInput, "The current layer cannot be frozen. Set another layer current first. Nothing changed.");
        }

        public void Apply(Database db, Transaction tr, LayerTableRecord l, List<string> loaded)
        {
            if (Color != null) l.Color = Cad.Color(Color);
            if (Lineweight != null) l.LineWeight = Cad.Lineweight(Lineweight);
            if (Linetype != null) l.LinetypeObjectId = Cad.Linetype(db, tr, Linetype, true, loaded);
            if (Description != null) l.Description = Description;
            if (Plot is { } p) l.IsPlottable = p;
            if (Frozen is { } f) l.IsFrozen = f;
            if (Locked is { } k) l.IsLocked = k;
            if (Off is { } o) l.IsOff = o;
            if (Transparency is { } t) l.Transparency = Cad.Transparency(t);
        }

        public void Verify(VerificationSet v, Transaction tr, LayerTableRecord l)
        {
            if (Color != null) v.Check("color", Color.DeepClone(), Cad.ColorJson(l.Color), Cad.SameColor(Cad.Color(Color), l.Color));
            if (Lineweight != null) v.Check("lineweight", Lineweight.DeepClone(), Cad.LineweightJson(l.LineWeight), Cad.Lineweight(Lineweight) == l.LineWeight);
            if (Linetype != null) v.Text("linetype", Linetype, ((LinetypeTableRecord)tr.GetObject(l.LinetypeObjectId, OpenMode.ForRead)).Name);
            if (Description != null) v.Text("description", Description, l.Description, false);
            if (Plot is { } p) v.Flag("plot", p, l.IsPlottable);
            if (Frozen is { } f) v.Flag("frozen", f, l.IsFrozen);
            if (Locked is { } k) v.Flag("locked", k, l.IsLocked);
            if (Off is { } o) v.Flag("off", o, l.IsOff);
            if (Transparency is { } t) v.Check("transparency_pct", t, Cad.TransparencyPct(l.Transparency), Cad.TransparencyPct(l.Transparency) == (int)t);
        }
    }

    private static CommandResult Create(CommandContext ctx)
    {
        var name = Hz.Str(ctx.Args, "new_name")!;
        var props = Props.From(ctx.Args);
        var loaded = new List<string>();
        var id = ObjectId.Null;
        return WriteFlow.Run(ctx, "HZ_LAYERS",
            (doc, tr, plan) =>
            {
                Cad.NewSymbol(tr, doc.Database.LayerTableId, name, "layer");
                props.Plan(doc.Database, tr, plan, false);
                plan["new_name"] = name; plan["properties"] = Hz.Without(ctx.Args, "action", "target_document", "dry_run", "confirmation_token", "new_name");
            },
            (doc, tr) =>
            {
                var lt = (LayerTable)tr.GetObject(doc.Database.LayerTableId, OpenMode.ForWrite);
                var l = new LayerTableRecord { Name = name };
                id = lt.Add(l);
                tr.AddNewlyCreatedDBObject(l, true);
                props.Apply(doc.Database, tr, l, loaded);
            },
            (doc, tr, v, after) =>
            {
                var l = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
                v.Text("name", name, l.Name, false);
                props.Verify(v, tr, l);
                after["layer"] = Describe(l, tr, doc.Database);
                if (loaded.Count > 0) after["linetypes_loaded"] = Hz.Strings(loaded);
            });
    }

    private static CommandResult Set(CommandContext ctx)
    {
        var name = Hz.Str(ctx.Args, "name")!;
        var newName = Hz.Str(ctx.Args, "new_name");
        var props = Props.From(ctx.Args);
        var loaded = new List<string>();
        var id = ObjectId.Null;
        return WriteFlow.Run(ctx, "HZ_LAYERS",
            (doc, tr, plan) =>
            {
                id = Cad.Symbol(tr, doc.Database.LayerTableId, name, "layer");
                var l = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
                if (l.IsDependent) throw new HzRefusal(ErrorCodes.NotEditable, "Layer '" + name + "' belongs to an external reference. Nothing changed.");
                if (newName != null)
                {
                    if (name is "0" || name.Equals("Defpoints", StringComparison.OrdinalIgnoreCase)) throw new HzRefusal(ErrorCodes.InvalidInput, "Layer '" + name + "' cannot be renamed. Nothing changed.");
                    if (!newName.Equals(name, StringComparison.OrdinalIgnoreCase)) Cad.NewSymbol(tr, doc.Database.LayerTableId, newName, "layer");
                }
                props.Plan(doc.Database, tr, plan, doc.Database.Clayer == id);
                plan["before"] = Describe(l, tr, doc.Database);
                plan["changes"] = Hz.Without(ctx.Args, "action", "target_document", "dry_run", "confirmation_token", "name");
            },
            (doc, tr) =>
            {
                var l = (LayerTableRecord)tr.GetObject(id, OpenMode.ForWrite);
                if (newName != null) l.Name = newName;
                props.Apply(doc.Database, tr, l, loaded);
            },
            (doc, tr, v, after) =>
            {
                var l = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
                if (newName != null) v.Text("name", newName, l.Name, false);
                props.Verify(v, tr, l);
                after["layer"] = Describe(l, tr, doc.Database);
                if (loaded.Count > 0) after["linetypes_loaded"] = Hz.Strings(loaded);
            });
    }

    private static CommandResult SetCurrent(CommandContext ctx)
    {
        var name = Hz.Str(ctx.Args, "name")!;
        var id = ObjectId.Null;
        return WriteFlow.Run(ctx, "HZ_LAYERS",
            (doc, tr, plan) =>
            {
                id = Cad.Symbol(tr, doc.Database.LayerTableId, name, "layer");
                var l = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
                if (l.IsFrozen) throw new HzRefusal(ErrorCodes.InvalidInput, "Layer '" + name + "' is frozen; a frozen layer cannot be current. Nothing changed.");
                plan["current_before"] = ((LayerTableRecord)tr.GetObject(doc.Database.Clayer, OpenMode.ForRead)).Name; plan["current_after"] = l.Name;
            },
            (doc, tr) => doc.Database.Clayer = id,
            (doc, tr, v, after) =>
            {
                v.Flag("current layer", true, doc.Database.Clayer == id);
                after["current"] = ((LayerTableRecord)tr.GetObject(doc.Database.Clayer, OpenMode.ForRead)).Name;
            });
    }

    private static void States(Document doc, Transaction tr, JsonObject data)
    {
        var m = doc.Database.LayerStateManager;
        var rows = new JsonArray();
        foreach (var n in m.GetLayerStateNames(false, false))
        {
            var name = n?.ToString() ?? "";
            rows.Add(new JsonObject { ["name"] = name, ["description"] = m.GetLayerStateDescription(name) });
        }
        data["states"] = rows;
        data["last_restored"] = m.LastRestoredLayerState;
    }

    private const LayerStateMasks AllMasks = LayerStateMasks.On | LayerStateMasks.Frozen | LayerStateMasks.Locked | LayerStateMasks.Plot |
        LayerStateMasks.Color | LayerStateMasks.LineType | LayerStateMasks.LineWeight | LayerStateMasks.NewViewport | LayerStateMasks.Transparency;

    private static Dictionary<string, (bool Off, bool Frozen, bool Locked)> Snapshot(Database db, Transaction tr)
    {
        var d = new Dictionary<string, (bool, bool, bool)>(StringComparer.OrdinalIgnoreCase);
        foreach (ObjectId id in (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead))
        {
            var l = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
            if (!l.IsDependent) d[l.Name] = (l.IsOff, l.IsFrozen, l.IsLocked);
        }
        return d;
    }

    private static CommandResult StateSave(CommandContext ctx)
    {
        var name = Hz.Str(ctx.Args, "state_name")!;
        var desc = Hz.Str(ctx.Args, "description");
        Dictionary<string, (bool Off, bool Frozen, bool Locked)> snap = new();
        return WriteFlow.Run(ctx, "HZ_LAYERS",
            (doc, tr, plan) =>
            {
                if (doc.Database.LayerStateManager.HasLayerState(name))
                    throw new HzRefusal(ErrorCodes.InvalidInput, "Layer state '" + name + "' exists. Existing states are never overwritten. Nothing changed.");
                snap = Snapshot(doc.Database, tr);
                plan["state_name"] = name; plan["layers"] = snap.Count; plan["description"] = desc;
            },
            (doc, tr) =>
            {
                var m = doc.Database.LayerStateManager;
                m.SaveLayerState(name, AllMasks, ObjectId.Null);
                if (desc != null) m.SetLayerStateDescription(name, desc);
            },
            (doc, tr, v, after) =>
            {
                var m = doc.Database.LayerStateManager;
                v.Flag("state exists", true, m.HasLayerState(name));
                if (!m.HasLayerState(name)) return;
                var layers = m.GetLayerStateLayers(name, false);
                v.Check("layers in state", snap.Count, layers.Count, layers.Count >= snap.Count);
                if (desc != null) v.Text("description", desc, m.GetLayerStateDescription(name), false);
                after["state"] = new JsonObject { ["name"] = name, ["layers"] = layers.Count };
            });
    }

    private static CommandResult StateRestore(CommandContext ctx)
    {
        var name = Hz.Str(ctx.Args, "state_name")!;
        Dictionary<string, (bool Off, bool Frozen, bool Locked)> before = new();
        return WriteFlow.Run(ctx, "HZ_LAYERS",
            (doc, tr, plan) =>
            {
                var m = doc.Database.LayerStateManager;
                if (!m.HasLayerState(name))
                {
                    var names = new List<string>();
                    foreach (var n in m.GetLayerStateNames(false, false)) names.Add(n?.ToString() ?? "");
                    throw new HzRefusal(ErrorCodes.NotFound, "No layer state '" + name + "'. Nothing changed.", new JsonObject { ["candidates"] = Hz.Strings(names) });
                }
                before = Snapshot(doc.Database, tr);
                plan["state_name"] = name; plan["layers"] = before.Count;
            },
            (doc, tr) => doc.Database.LayerStateManager.RestoreLayerState(name, ObjectId.Null, 0, AllMasks),
            (doc, tr, v, after) =>
            {
                var m = doc.Database.LayerStateManager;
                v.Text("last restored state", name, m.LastRestoredLayerState);
                v.Flag("drawing matches the state", true, m.CompareLayerStateToDb(name, ObjectId.Null));
                var now = Snapshot(doc.Database, tr);
                var changed = new JsonArray();
                foreach (var (k, b) in before)
                    if (now.TryGetValue(k, out var a) && a != b)
                        changed.Add(new JsonObject { ["layer"] = k, ["off"] = a.Off, ["frozen"] = a.Frozen, ["locked"] = a.Locked });
                after["layers_changed"] = changed;
            });
    }
}
