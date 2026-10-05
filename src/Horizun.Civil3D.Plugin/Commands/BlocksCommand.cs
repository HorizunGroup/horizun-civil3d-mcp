// -----------------------------------------------------------------------------
// horizun_c3d_blocks - block definitions, inserts with attributes, batch
// attribute values (title blocks), dynamic properties, import from a DWG.
// -----------------------------------------------------------------------------
using System.Text.Json.Nodes;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Horizun.Civil3D.Core;
using Horizun.Civil3D.Plugin.Civil;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace Horizun.Civil3D.Plugin.Commands;

internal sealed class BlocksCommand : ICommand
{
    public string Name => "blocks";

    public CommandResult Execute(CommandContext ctx)
    {
        if (ToolRules.Validate(ctx.Tool.Name, ctx.Args) is { } why) throw new HzRefusal(ErrorCodes.InvalidInput, why + " Nothing ran.");
        return ctx.Action switch
        {
            "list" => WriteFlow.Read(ctx, (doc, tr, data) => List(ctx, doc, tr, data)),
            "references" => WriteFlow.Read(ctx, (doc, tr, data) => References(ctx, doc, tr, data)),
            "define" => Define(ctx),
            "insert" => Insert(ctx),
            "set_attributes" => SetAttributes(ctx),
            "set_dynamic" => SetDynamic(ctx),
            _ => Import(ctx),
        };
    }

    // ---- helpers -----------------------------------------------------------------

    private static List<AttributeDefinition> AttDefs(BlockTableRecord btr, Transaction tr)
    {
        var list = new List<AttributeDefinition>();
        if (!btr.HasAttributeDefinitions) return list;
        foreach (ObjectId id in btr)
            if (id.ObjectClass.DxfName == "ATTDEF") list.Add((AttributeDefinition)tr.GetObject(id, OpenMode.ForRead));
        return list;
    }

    /// <summary>Every reference of a block, including the anonymous copies of a dynamic block.</summary>
    private static List<ObjectId> RefIds(BlockTableRecord btr, Transaction tr)
    {
        var ids = btr.GetBlockReferenceIds(true, false).Cast<ObjectId>().ToList();
        if (btr.IsDynamicBlock)
            foreach (ObjectId anon in btr.GetAnonymousBlockIds())
                ids.AddRange(((BlockTableRecord)tr.GetObject(anon, OpenMode.ForRead)).GetBlockReferenceIds(true, false).Cast<ObjectId>());
        return ids;
    }

    private static JsonObject Attributes(BlockReference br, Transaction tr)
    {
        var o = new JsonObject();
        foreach (ObjectId id in br.AttributeCollection)
        {
            var a = (AttributeReference)tr.GetObject(id, OpenMode.ForRead);
            o[a.Tag] = a.TextString;
        }
        return o;
    }

    private static JsonNode? DynValue(object? v) => v switch
    {
        null => null, double d => Hz.Finite(d, 9), short s => (int)s, int i => i, long l => l, bool b => b, string s => s, _ => v.ToString(),
    };

    private static JsonObject Dynamic(BlockReference br)
    {
        var o = new JsonObject();
        if (!br.IsDynamicBlock) return o;
        foreach (DynamicBlockReferenceProperty p in br.DynamicBlockReferencePropertyCollection)
            if (p.PropertyName != "Origin" && p.Show) o[p.PropertyName] = DynValue(p.Value);
        return o;
    }

    private static JsonObject RefJson(BlockReference br, Transaction tr)
    {
        var o = Cad.Describe(br, tr, false);
        o["owner"] = ((BlockTableRecord)tr.GetObject(br.OwnerId, OpenMode.ForRead)).Name;
        o["attributes"] = Attributes(br, tr);
        if (br.IsDynamicBlock) o["dynamic"] = Dynamic(br);
        return o;
    }

    private static BlockTableRecord Btr(Database db, Transaction tr, string name)
    {
        var id = Cad.Symbol(tr, db.BlockTableId, name, "block");
        var b = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
        if (b.IsLayout) throw new HzRefusal(ErrorCodes.InvalidInput, "'" + name + "' is a layout block, not a block definition. Nothing changed.");
        if (b.IsFromExternalReference) throw new HzRefusal(ErrorCodes.InvalidInput, "'" + name + "' is an external reference. Nothing changed.");
        return b;
    }

    // ---- reads ---------------------------------------------------------------------

    private static void List(CommandContext ctx, Document doc, Transaction tr, JsonObject data)
    {
        var db = doc.Database;
        var anon = Hz.Bool(ctx.Args, "include_anonymous") ?? false;
        var limit = (int)(Hz.Num(ctx.Args, "limit") ?? 1000);
        var rows = new JsonArray();
        var total = 0;
        foreach (ObjectId id in (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead))
        {
            var b = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
            if (b.IsLayout || (b.IsAnonymous && !anon)) continue;
            total++;
            if (rows.Count >= limit) continue;
            var atts = new JsonArray(AttDefs(b, tr).Select(a => (JsonNode)new JsonObject
            {
                ["tag"] = a.Tag, ["prompt"] = a.Prompt, ["default"] = a.TextString, ["constant"] = a.Constant, ["invisible"] = a.Invisible,
            }).ToArray());
            rows.Add(new JsonObject
            {
                ["name"] = b.Name, ["handle"] = b.Handle.ToString(), ["origin"] = Resolve.Json(b.Origin), ["description"] = b.Comments,
                ["dynamic"] = b.IsDynamicBlock, ["xref"] = b.IsFromExternalReference, ["anonymous"] = b.IsAnonymous,
                ["references"] = b.IsFromExternalReference ? b.GetBlockReferenceIds(true, false).Count : RefIds(b, tr).Count,
                ["attribute_definitions"] = atts,
            });
        }
        data["count"] = total;
        data["blocks"] = rows;
    }

    private static void References(CommandContext ctx, Document doc, Transaction tr, JsonObject data)
    {
        var b = Btr(doc.Database, tr, Hz.Str(ctx.Args, "name")!);
        var limit = (int)(Hz.Num(ctx.Args, "limit") ?? 500);
        var ids = RefIds(b, tr);
        data["block"] = b.Name;
        data["count"] = ids.Count;
        data["references"] = new JsonArray(ids.Take(limit).Select(id => (JsonNode)RefJson((BlockReference)tr.GetObject(id, OpenMode.ForRead), tr)).ToArray());
    }

    // ---- define ---------------------------------------------------------------------

    private static CommandResult Define(CommandContext ctx)
    {
        var name = Hz.Str(ctx.Args, "new_name")!;
        var basePt = Resolve.P(ctx.Args["base_point"]);
        var erase = Hz.Bool(ctx.Args, "erase_source") ?? false;
        var atts = (ctx.Args["attributes"] as JsonArray)?.Select(n => (JsonObject)n!).ToList() ?? new();
        var src = new List<ObjectId>();
        var id = ObjectId.Null;
        return WriteFlow.Run(ctx, "HZ_BLOCKS",
            (doc, tr, plan) =>
            {
                var db = doc.Database;
                Cad.NewSymbol(tr, db.BlockTableId, name, "block");
                foreach (var h in Resolve.Strings(ctx.Args["handles"]))
                {
                    var e = Cad.Entity(db, tr, h);
                    if (erase) Cad.Editable(e, tr);
                    else if (tr.GetObject(e.OwnerId, OpenMode.ForRead) is not BlockTableRecord { IsLayout: true })
                        throw new HzRefusal(ErrorCodes.InvalidInput, Cad.Dxf(e) + " " + e.Handle + " is not in model or paper space. Nothing changed.");
                    src.Add(e.ObjectId);
                }
                plan["new_name"] = name; plan["base_point"] = Resolve.Json(basePt); plan["entities"] = src.Count; plan["attributes"] = atts.Count;
                plan["erase_source"] = erase;
            },
            (doc, tr) =>
            {
                var db = doc.Database;
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                var btr = new BlockTableRecord { Name = name, Origin = basePt };
                if (Hz.Str(ctx.Args, "description") is { } d) btr.Comments = d;
                id = bt.Add(btr);
                tr.AddNewlyCreatedDBObject(btr, true);
                if (src.Count > 0)
                {
                    var map = new IdMapping();
                    db.DeepCloneObjects(Cad.Ids(src), id, map, false);
                }
                foreach (var a in atts)
                {
                    var rel = Resolve.P(a["position"]);
                    var def = new AttributeDefinition();
                    def.SetDatabaseDefaults(db);
                    def.Position = new Point3d(basePt.X + rel.X, basePt.Y + rel.Y, basePt.Z + rel.Z);
                    def.Tag = Hz.Str(a, "tag")!;
                    def.Prompt = Hz.Str(a, "prompt") ?? def.Tag;
                    def.TextString = Hz.Str(a, "default") ?? "";
                    def.Height = Hz.Num(a, "height") ?? db.Textsize;
                    def.Invisible = Hz.Bool(a, "invisible") ?? false;
                    def.Constant = Hz.Bool(a, "constant") ?? false;
                    btr.AppendEntity(def);
                    tr.AddNewlyCreatedDBObject(def, true);
                }
                if (erase) foreach (var s in src) tr.GetObject(s, OpenMode.ForWrite).Erase();
            },
            (doc, tr, v, after) =>
            {
                var b = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
                var count = b.Cast<ObjectId>().Count();
                v.Check("entities in definition", src.Count + atts.Count, count, count == src.Count + atts.Count);
                var defs = AttDefs(b, tr);
                foreach (var a in atts) v.Flag("attribute " + Hz.Str(a, "tag"), true, defs.Any(d => d.Tag.Equals(Hz.Str(a, "tag"), StringComparison.OrdinalIgnoreCase)));
                v.Check("base point", Resolve.Json(basePt), Resolve.Json(b.Origin), b.Origin.DistanceTo(basePt) <= 1e-9);
                if (erase) foreach (var s in src) v.Flag(s.Handle + " source erased", true, s.IsErased);
                after["block"] = new JsonObject { ["name"] = b.Name, ["handle"] = b.Handle.ToString(), ["entities"] = count, ["attribute_definitions"] = defs.Count };
            });
    }

    // ---- insert ----------------------------------------------------------------------

    private static void SetDyn(BlockReference br, string prop, JsonNode value)
    {
        foreach (DynamicBlockReferenceProperty p in br.DynamicBlockReferencePropertyCollection)
        {
            if (!p.PropertyName.Equals(prop, StringComparison.OrdinalIgnoreCase)) continue;
            if (p.ReadOnly) throw new HzRefusal(ErrorCodes.InvalidInput, "Dynamic property '" + prop + "' is read-only. Nothing changed.");
            object v = p.Value switch
            {
                double => Hz.AsDouble(value) ?? throw new HzRefusal(ErrorCodes.InvalidInput, "Dynamic property '" + prop + "' needs a number. Nothing changed."),
                short => (short)(Hz.AsDouble(value) ?? throw new HzRefusal(ErrorCodes.InvalidInput, "Dynamic property '" + prop + "' needs an integer. Nothing changed.")),
                int => (int)(Hz.AsDouble(value) ?? throw new HzRefusal(ErrorCodes.InvalidInput, "Dynamic property '" + prop + "' needs an integer. Nothing changed.")),
                _ => value.ToString(),
            };
            var allowed = p.GetAllowedValues();
            if (allowed.Length > 0 && !allowed.Any(a => Equals(a, v) || (a is double ad && v is double vd && Math.Abs(ad - vd) < 1e-9)))
                throw new HzRefusal(ErrorCodes.InvalidInput, "Value " + value.ToJsonString() + " is not allowed for '" + prop + "'. Allowed: " + string.Join(", ", allowed.Select(a => a?.ToString())) + ". Nothing changed.");
            p.Value = v;
            return;
        }
        var names = br.DynamicBlockReferencePropertyCollection.Cast<DynamicBlockReferenceProperty>().Where(p => p.Show).Select(p => p.PropertyName);
        throw new HzRefusal(ErrorCodes.InvalidInput, "The block has no dynamic property '" + prop + "'. Properties: " + string.Join(", ", names) + ". Nothing changed.");
    }

    private static void CheckDyn(VerificationSet v, BlockReference br, JsonObject? dyn)
    {
        if (dyn == null) return;
        var now = Dynamic(br);
        foreach (var (k, want) in dyn)
        {
            var key = now.Select(kv => kv.Key).FirstOrDefault(x => x.Equals(k, StringComparison.OrdinalIgnoreCase));
            var got = key == null ? null : now[key];
            var same = Hz.AsDouble(want) is { } a && Hz.AsDouble(got) is { } b ? Math.Abs(a - b) <= 1e-6 : string.Equals(want?.ToString(), got?.ToString(), StringComparison.OrdinalIgnoreCase);
            v.Check("dynamic " + k, want?.DeepClone(), got?.DeepClone(), same);
        }
    }

    private static CommandResult Insert(CommandContext ctx)
    {
        var pos = Resolve.P(ctx.Args["position"]);
        var scale = Hz.Num(ctx.Args, "scale") ?? 1;
        var rot = Cad.Rad(Hz.Num(ctx.Args, "rotation") ?? 0);
        var values = (ctx.Args["attributes"] as JsonObject)?.ToDictionary(kv => kv.Key.ToUpperInvariant(), kv => kv.Value!.GetValue<string>()) ?? new();
        var dyn = ctx.Args["dynamic"] as JsonObject;
        ObjectId btrId = ObjectId.Null, layerId = ObjectId.Null, id = ObjectId.Null;
        var expected = new Dictionary<string, string>();
        return WriteFlow.Run(ctx, "HZ_BLOCKS",
            (doc, tr, plan) =>
            {
                var b = Btr(doc.Database, tr, Hz.Str(ctx.Args, "name")!);
                btrId = b.ObjectId;
                layerId = Resolve.Layer(doc.Database, tr, Hz.Str(ctx.Args, "layer"));
                var defs = AttDefs(b, tr).Where(d => !d.Constant).ToList();
                var unknown = values.Keys.Where(k => !defs.Any(d => d.Tag.Equals(k, StringComparison.OrdinalIgnoreCase))).ToList();
                if (unknown.Count > 0)
                    throw new HzRefusal(ErrorCodes.InvalidInput, "Block '" + b.Name + "' has no (non-constant) attribute " + string.Join(", ", unknown) + ". Nothing changed.", new JsonObject { ["tags"] = Hz.Strings(defs.Select(d => d.Tag)) });
                foreach (var d in defs) expected[d.Tag.ToUpperInvariant()] = values.TryGetValue(d.Tag.ToUpperInvariant(), out var val) ? val : d.TextString;
                if (dyn != null && !b.IsDynamicBlock) throw new HzRefusal(ErrorCodes.InvalidInput, "Block '" + b.Name + "' is not dynamic; remove dynamic. Nothing changed.");
                plan["block"] = b.Name; plan["position"] = Resolve.Json(pos); plan["scale"] = scale; plan["rotation_deg"] = Hz.Num(ctx.Args, "rotation") ?? 0;
                var att = new JsonObject();
                foreach (var (k, val) in expected) att[k] = val;
                plan["attributes"] = att;
                if (dyn != null) plan["dynamic"] = dyn.DeepClone();
            },
            (doc, tr) =>
            {
                var db = doc.Database;
                var br = Cad.New(db, new BlockReference(pos, btrId));
                br.ScaleFactors = new Scale3d(scale);
                br.Rotation = rot;
                id = Cad.Append(db, tr, br, layerId);
                var b = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                foreach (var d in AttDefs(b, tr).Where(d => !d.Constant))
                {
                    var ar = new AttributeReference();
                    ar.SetAttributeFromBlock(d, br.BlockTransform);
                    ar.TextString = expected[d.Tag.ToUpperInvariant()];
                    br.AttributeCollection.AppendAttribute(ar);
                    tr.AddNewlyCreatedDBObject(ar, true);
                }
                if (dyn != null) foreach (var (k, val) in dyn) SetDyn(br, k, val!);
            },
            (doc, tr, v, after) =>
            {
                var br = (BlockReference)tr.GetObject(id, OpenMode.ForRead);
                v.Check("position", Resolve.Json(pos), Resolve.Json(br.Position), br.Position.DistanceTo(pos) <= 1e-9);
                v.Number("scale", scale, br.ScaleFactors.X, 1e-12);
                v.Number("rotation", rot, br.Rotation, 1e-12);
                var atts = Attributes(br, tr);
                foreach (var (k, val) in expected)
                {
                    var got = atts.Select(kv => kv).FirstOrDefault(kv => kv.Key.Equals(k, StringComparison.OrdinalIgnoreCase)).Value?.ToString();
                    v.Text("attribute " + k, val, got, false);
                }
                CheckDyn(v, br, dyn);
                if (expected.Count == 0 && dyn == null) v.Flag("reference created", true, !id.IsErased);
                after["reference"] = RefJson(br, tr);
            });
    }

    // ---- attributes / dynamic ---------------------------------------------------------

    private static CommandResult SetAttributes(CommandContext ctx)
    {
        var values = ((JsonObject)ctx.Args["values"]!).ToDictionary(kv => kv.Key.ToUpperInvariant(), kv => kv.Value!.GetValue<string>());
        var targets = new List<ObjectId>();
        return WriteFlow.Run(ctx, "HZ_BLOCKS",
            (doc, tr, plan) =>
            {
                var db = doc.Database;
                if (Hz.Str(ctx.Args, "name") is { } n) targets = RefIds(Btr(db, tr, n), tr);
                else
                    foreach (var h in Resolve.Strings(ctx.Args["handles"]))
                    {
                        var e = Cad.Entity(db, tr, h);
                        if (e is not BlockReference) throw new HzRefusal(ErrorCodes.InvalidInput, Cad.Dxf(e) + " " + e.Handle + " is not a block reference. Nothing changed.");
                        targets.Add(e.ObjectId);
                    }
                if (targets.Count == 0) throw new HzRefusal(ErrorCodes.NotFound, "No block references to update. Nothing changed.");
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var t in targets)
                {
                    var br = (BlockReference)tr.GetObject(t, OpenMode.ForRead);
                    Cad.Editable(br, tr);
                    foreach (var k in Attributes(br, tr).Select(kv => kv.Key)) seen.Add(k);
                }
                var unknown = values.Keys.Where(k => !seen.Contains(k)).ToList();
                if (unknown.Count > 0) throw new HzRefusal(ErrorCodes.InvalidInput, "No target reference has attribute " + string.Join(", ", unknown) + ". Nothing changed.", new JsonObject { ["tags"] = Hz.Strings(seen) });
                plan["references"] = targets.Count; plan["values"] = ctx.Args["values"]!.DeepClone();
                plan["sample_before"] = new JsonArray(targets.Take(10).Select(t => (JsonNode)RefJson((BlockReference)tr.GetObject(t, OpenMode.ForRead), tr)).ToArray());
            },
            (doc, tr) =>
            {
                foreach (var t in targets)
                    foreach (ObjectId aid in ((BlockReference)tr.GetObject(t, OpenMode.ForRead)).AttributeCollection)
                    {
                        var a = (AttributeReference)tr.GetObject(aid, OpenMode.ForRead);
                        if (values.TryGetValue(a.Tag.ToUpperInvariant(), out var val)) { a.UpgradeOpen(); a.TextString = val; }
                    }
            },
            (doc, tr, v, after) =>
            {
                var changed = 0;
                foreach (var t in targets)
                    foreach (ObjectId aid in ((BlockReference)tr.GetObject(t, OpenMode.ForRead)).AttributeCollection)
                    {
                        var a = (AttributeReference)tr.GetObject(aid, OpenMode.ForRead);
                        if (!values.TryGetValue(a.Tag.ToUpperInvariant(), out var val)) continue;
                        changed++;
                        if (a.TextString != val) v.Text(t.Handle + " " + a.Tag, val, a.TextString, false);
                    }
                v.Check("attribute values set", "> 0", changed, changed > 0);
                after["attributes_written"] = changed;
                after["references"] = targets.Count;
            });
    }

    private static CommandResult SetDynamic(CommandContext ctx)
    {
        var props = (JsonObject)ctx.Args["properties"]!;
        var id = ObjectId.Null;
        return WriteFlow.Run(ctx, "HZ_BLOCKS",
            (doc, tr, plan) =>
            {
                var e = Cad.Entity(doc.Database, tr, Hz.Str(ctx.Args, "handle")!);
                if (e is not BlockReference { IsDynamicBlock: true } br) throw new HzRefusal(ErrorCodes.InvalidInput, Cad.Dxf(e) + " " + e.Handle + " is not a dynamic block reference. Nothing changed.");
                Cad.Editable(e, tr);
                id = e.ObjectId;
                plan["reference"] = RefJson(br, tr); plan["properties"] = props.DeepClone();
            },
            (doc, tr) =>
            {
                var br = (BlockReference)tr.GetObject(id, OpenMode.ForWrite);
                foreach (var (k, val) in props) SetDyn(br, k, val!);
            },
            (doc, tr, v, after) =>
            {
                var br = (BlockReference)tr.GetObject(id, OpenMode.ForRead);
                CheckDyn(v, br, props);
                after["reference"] = RefJson(br, tr);
            });
    }

    // ---- import -------------------------------------------------------------------------

    private static CommandResult Import(CommandContext ctx)
    {
        var path = Hz.Str(ctx.Args, "source_dwg")!;
        var names = Resolve.Strings(ctx.Args["names"]);
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        return WriteFlow.Run(ctx, "HZ_BLOCKS",
            (doc, tr, plan) =>
            {
                if (!Path.IsPathRooted(path) || !File.Exists(path)) throw new HzRefusal(ErrorCodes.NotFound, "source_dwg must be an existing absolute .dwg path. Nothing changed.");
                var bt = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
                var clash = names.Where(n => bt.Has(n)).ToList();
                if (clash.Count > 0) throw new HzRefusal(ErrorCodes.InvalidInput, "The drawing already has block(s) " + string.Join(", ", clash) + "; existing definitions are never overwritten. Nothing changed.");
                using var src = new Database(false, true);
                src.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, true, null);
                using var str = src.TransactionManager.StartTransaction();
                var sbt = (BlockTable)str.GetObject(src.BlockTableId, OpenMode.ForRead);
                foreach (var n in names)
                {
                    if (!sbt.Has(n)) throw new HzRefusal(ErrorCodes.NotFound, Path.GetFileName(path) + " has no block '" + n + "'. Nothing changed.");
                    var b = (BlockTableRecord)str.GetObject(sbt[n], OpenMode.ForRead);
                    if (b.IsLayout || b.IsFromExternalReference) throw new HzRefusal(ErrorCodes.InvalidInput, "'" + n + "' in the source is a layout or xref. Nothing changed.");
                    counts[n] = b.Cast<ObjectId>().Count();
                }
                str.Commit();
                plan["source_dwg"] = path; plan["blocks"] = Hz.Strings(names);
            },
            (doc, tr) =>
            {
                using var src = new Database(false, true);
                src.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, true, null);
                var ids = new ObjectIdCollection();
                using (var str = src.TransactionManager.StartTransaction())
                {
                    var sbt = (BlockTable)str.GetObject(src.BlockTableId, OpenMode.ForRead);
                    foreach (var n in names) ids.Add(sbt[n]);
                    str.Commit();
                }
                src.WblockCloneObjects(ids, doc.Database.BlockTableId, new IdMapping(), DuplicateRecordCloning.Ignore, false);
            },
            (doc, tr, v, after) =>
            {
                var bt = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
                var rows = new JsonArray();
                foreach (var n in names)
                {
                    v.Flag(n + " imported", true, bt.Has(n));
                    if (!bt.Has(n)) continue;
                    var cnt = ((BlockTableRecord)tr.GetObject(bt[n], OpenMode.ForRead)).Cast<ObjectId>().Count();
                    v.Check(n + " entities", counts[n], cnt, cnt == counts[n]);
                    rows.Add(new JsonObject { ["name"] = n, ["entities"] = cnt });
                }
                after["imported"] = rows;
            });
    }
}
