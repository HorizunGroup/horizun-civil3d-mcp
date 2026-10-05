// -----------------------------------------------------------------------------
// horizun_c3d_entities - AutoCAD entities: query, get, draw, set_properties,
// transform, offset, explode, join, erase (block C).
//
// Every write re-reads geometry in a new transaction: drawn items against the
// requested coordinates, transforms against the transformed anchor point and
// the length/area invariants, offsets against the offset distance, joins
// against the summed length.
// -----------------------------------------------------------------------------
using System.Text.Json.Nodes;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Horizun.Civil3D.Core;
using Horizun.Civil3D.Plugin.Civil;
using DBObject = Autodesk.AutoCAD.DatabaseServices.DBObject;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace Horizun.Civil3D.Plugin.Commands;

internal sealed class EntitiesCommand : ICommand
{
    public string Name => "entities";
    private const double Tol = 1e-6;

    public CommandResult Execute(CommandContext ctx)
    {
        if (ToolRules.Validate(ctx.Tool.Name, ctx.Args) is { } why) throw new HzRefusal(ErrorCodes.InvalidInput, why + " Nothing ran.");
        return ctx.Action switch
        {
            "query" => WriteFlow.Read(ctx, (doc, tr, data) => Query(ctx, doc, tr, data)),
            "get" => WriteFlow.Read(ctx, (doc, tr, data) =>
                data["entities"] = new JsonArray(Resolve.Strings(ctx.Args["handles"]).Select(h => (JsonNode)Cad.Describe(Cad.Entity(doc.Database, tr, h), tr, true)).ToArray())),
            "set_properties" => SetProperties(ctx),
            "transform" => Transform(ctx),
            "offset" => Offset(ctx),
            "explode" => Explode(ctx),
            "join" => Join(ctx),
            "erase" => Erase(ctx),
            _ => Draw(ctx),
        };
    }

    // ---- query ---------------------------------------------------------------

    private static void Query(CommandContext ctx, Document doc, Transaction tr, JsonObject data)
    {
        var db = doc.Database;
        var types = Resolve.Strings(ctx.Args["types"]).Select(t => t.ToUpperInvariant()).ToHashSet();
        var layers = Resolve.Strings(ctx.Args["layers"]).Select(LayersCommand.Wildcard).ToList();
        var block = Hz.Str(ctx.Args, "block");
        var limit = (int)(Hz.Num(ctx.Args, "limit") ?? 500);
        var crossing = Hz.Bool(ctx.Args, "crossing") ?? false;
        Point3d? wmin = null, wmax = null;
        if (ctx.Args["window"] is JsonObject w) { wmin = Resolve.P(w["min"]); wmax = Resolve.P(w["max"]); }
        var rows = new JsonArray();
        var byType = new SortedDictionary<string, int>();
        var total = 0;
        foreach (ObjectId id in Cad.ModelSpace(db, tr))
        {
            var dxf = id.ObjectClass.DxfName;
            if (types.Count > 0 && !types.Contains(dxf)) continue;
            var e = (Entity)tr.GetObject(id, OpenMode.ForRead);
            if (layers.Count > 0 && !layers.Any(r => r.IsMatch(e.Layer))) continue;
            if (block != null && (e is not BlockReference br || !string.Equals(Cad.BlockName(br, tr), block, StringComparison.OrdinalIgnoreCase))) continue;
            if (wmin is { } a && wmax is { } b)
            {
                Extents3d x;
                try { x = e.GeometricExtents; } catch (Autodesk.AutoCAD.Runtime.Exception) { continue; }
                var inside = x.MinPoint.X >= a.X && x.MinPoint.Y >= a.Y && x.MaxPoint.X <= b.X && x.MaxPoint.Y <= b.Y;
                var touches = x.MaxPoint.X >= a.X && x.MaxPoint.Y >= a.Y && x.MinPoint.X <= b.X && x.MinPoint.Y <= b.Y;
                if (crossing ? !touches : !inside) continue;
            }
            total++;
            byType[dxf] = byType.TryGetValue(dxf, out var n) ? n + 1 : 1;
            if (rows.Count < limit) rows.Add(Cad.Describe(e, tr, false));
        }
        data["count"] = total;
        var bt = new JsonObject();
        foreach (var kv in byType) bt[kv.Key] = kv.Value;
        data["by_type"] = bt;
        data["entities"] = rows;
        data["truncated"] = total > rows.Count;
        if (wmin != null) data["window_mode"] = crossing ? "crossing (touching the window)" : "window (fully inside, by extents)";
    }

    // ---- common property handling (draw items and set_properties) --------------

    private sealed class EntProps
    {
        public string? Layer, Linetype; public JsonNode? Color, Lineweight; public double? LtScale, Transparency;
        public ObjectId LayerId = ObjectId.Null, LinetypeId = ObjectId.Null;
        public static EntProps From(JsonObject a, string? defaultLayer = null) => new()
        {
            Layer = Hz.Str(a, "layer") ?? defaultLayer, Linetype = Hz.Str(a, "linetype"), Color = a["color"], Lineweight = a["lineweight"],
            LtScale = Hz.Num(a, "linetype_scale"), Transparency = Hz.Num(a, "transparency"),
        };
        public void Plan(Database db, Transaction tr)
        {
            if (Layer != null) LayerId = Resolve.Layer(db, tr, Layer);
            if (Linetype != null) Cad.LinetypeAvailability(db, tr, Linetype);
        }
        public void Apply(Database db, Transaction tr, Entity e)
        {
            if (!LayerId.IsNull) e.LayerId = LayerId;
            if (Color != null) e.Color = Cad.Color(Color);
            if (Linetype != null) { if (LinetypeId.IsNull) LinetypeId = Cad.Linetype(db, tr, Linetype, true); e.LinetypeId = LinetypeId; }
            if (Lineweight != null) e.LineWeight = Cad.Lineweight(Lineweight);
            if (LtScale is { } s) e.LinetypeScale = s;
            if (Transparency is { } t) e.Transparency = Cad.Transparency(t);
        }
        public void Verify(VerificationSet v, string what, Entity e)
        {
            if (Layer != null) v.Text(what + " layer", Layer, e.Layer);
            if (Color != null) v.Check(what + " color", Color.DeepClone(), Cad.ColorJson(e.Color), Cad.SameColor(Cad.Color(Color), e.Color));
            if (Linetype != null) v.Text(what + " linetype", Linetype, e.Linetype);
            if (Lineweight != null) v.Check(what + " lineweight", Lineweight.DeepClone(), Cad.LineweightJson(e.LineWeight), Cad.Lineweight(Lineweight) == e.LineWeight);
            if (LtScale is { } s) v.Number(what + " linetype scale", s, e.LinetypeScale, 1e-9);
            if (Transparency is { } t) v.Check(what + " transparency", t, Cad.TransparencyPct(e.Transparency), Cad.TransparencyPct(e.Transparency) == (int)t);
        }
    }

    private static List<Entity> Editables(Document doc, Transaction tr, JsonNode? handles)
    {
        var list = new List<Entity>();
        foreach (var h in Resolve.Strings(handles))
        {
            var e = Cad.Entity(doc.Database, tr, h);
            Cad.Editable(e, tr);
            list.Add(e);
        }
        return list;
    }

    private static CommandResult SetProperties(CommandContext ctx)
    {
        var props = EntProps.From(ctx.Args);
        var ids = new List<ObjectId>();
        return WriteFlow.Run(ctx, "HZ_ENTITIES",
            (doc, tr, plan) =>
            {
                var es = Editables(doc, tr, ctx.Args["handles"]);
                ids = es.Select(e => e.ObjectId).ToList();
                props.Plan(doc.Database, tr);
                plan["entities"] = es.Count; plan["changes"] = Hz.Without(ctx.Args, "action", "target_document", "dry_run", "confirmation_token", "handles");
                plan["sample_before"] = new JsonArray(es.Take(20).Select(e => (JsonNode)Cad.Describe(e, tr, true)).ToArray());
            },
            (doc, tr) => { foreach (var id in ids) props.Apply(doc.Database, tr, (Entity)tr.GetObject(id, OpenMode.ForWrite)); },
            (doc, tr, v, after) =>
            {
                foreach (var id in ids) props.Verify(v, id.Handle.ToString(), (Entity)tr.GetObject(id, OpenMode.ForRead));
                after["entities"] = ids.Count;
            });
    }

    // ---- transform -------------------------------------------------------------

    private static Point3d Anchor(Entity e) => e switch
    {
        DBText t => t.Position,
        MText m => m.Location,
        BlockReference b => b.Position,
        Circle c => c.Center,
        Arc a => a.Center,
        Ellipse el => el.Center,
        DBPoint p => p.Position,
        Curve c => c.StartPoint,
        _ => Ext(e) is { } x ? new Point3d((x.MinPoint.X + x.MaxPoint.X) / 2, (x.MinPoint.Y + x.MaxPoint.Y) / 2, (x.MinPoint.Z + x.MaxPoint.Z) / 2) : Point3d.Origin,
    };

    private static Extents3d? Ext(Entity e) { try { return e.GeometricExtents; } catch (Autodesk.AutoCAD.Runtime.Exception) { return null; } }

    private static CommandResult Transform(CommandContext ctx)
    {
        var op = Hz.Str(ctx.Args, "operation")!;
        var keep = op == "copy" || (op == "mirror" && (Hz.Bool(ctx.Args, "keep_source") ?? true));
        Matrix3d m;
        double lenFactor = 1, areaFactor = 1;
        switch (op)
        {
            case "move": case "copy": { var d = Resolve.P(ctx.Args["displacement"]); m = Matrix3d.Displacement(new Vector3d(d.X, d.Y, d.Z)); break; }
            case "rotate": m = Matrix3d.Rotation(Cad.Rad(Hz.Num(ctx.Args, "angle")!.Value), Vector3d.ZAxis, Resolve.P(ctx.Args["base"])); break;
            case "scale": { var f = Hz.Num(ctx.Args, "factor")!.Value; m = Matrix3d.Scaling(f, Resolve.P(ctx.Args["base"])); lenFactor = f; areaFactor = f * f; break; }
            default:
            {
                var ml = (JsonObject)ctx.Args["mirror_line"]!;
                m = Matrix3d.Mirroring(new Line3d(Resolve.P(ml["from"]), Resolve.P(ml["to"])));
                break;
            }
        }
        var src = new List<(ObjectId Id, Point3d Anchor, double? Len, double? Area)>();
        var results = new List<ObjectId>();
        return WriteFlow.Run(ctx, "HZ_ENTITIES",
            (doc, tr, plan) =>
            {
                var es = Editables(doc, tr, ctx.Args["handles"]);
                foreach (var e in es)
                {
                    if (op == "scale" && e is BlockReference { IsDynamicBlock: false } br && Math.Abs(br.ScaleFactors.X - br.ScaleFactors.Y) > 1e-9)
                        throw new HzRefusal(ErrorCodes.InvalidInput, "Block reference " + e.Handle + " is non-uniformly scaled. Nothing changed.");
                    src.Add((e.ObjectId, Anchor(e), Cad.Length(e), Cad.Area(e)));
                }
                plan["operation"] = op; plan["entities"] = es.Count; plan["source_kept"] = keep;
                plan["matrix"] = new JsonArray(m.ToArray().Select(x => (JsonNode?)JsonValue.Create(Math.Round(x, 12))).ToArray());
            },
            (doc, tr) =>
            {
                foreach (var s in src)
                {
                    var e = (Entity)tr.GetObject(s.Id, OpenMode.ForWrite);
                    if (keep)
                    {
                        var c = e.GetTransformedCopy(m);
                        var owner = (BlockTableRecord)tr.GetObject(e.OwnerId, OpenMode.ForWrite);
                        results.Add(owner.AppendEntity(c));
                        tr.AddNewlyCreatedDBObject(c, true);
                    }
                    else { e.TransformBy(m); results.Add(s.Id); }
                }
            },
            (doc, tr, v, after) =>
            {
                var rows = new JsonArray();
                for (var i = 0; i < src.Count; i++)
                {
                    var e = (Entity)tr.GetObject(results[i], OpenMode.ForRead);
                    var want = src[i].Anchor.TransformBy(m);
                    var got = Anchor(e);
                    v.Check(e.Handle + " anchor", Resolve.Json(want), Resolve.Json(got), want.DistanceTo(got) <= Tol);
                    if (src[i].Len is { } l && Cad.Length(e) is { } nl) v.Number(e.Handle + " length", l * lenFactor, nl, Math.Max(Tol, l * 1e-9));
                    if (src[i].Area is { } a && Cad.Area(e) is { } na) v.Number(e.Handle + " area", a * areaFactor, na, Math.Max(Tol, a * 1e-9));
                    if (keep) v.Flag(src[i].Id.Handle + " source kept", true, !src[i].Id.IsErased);
                    rows.Add(Cad.Describe(e, tr, false));
                }
                after["results"] = rows;
            });
    }

    // ---- offset / explode / join / erase --------------------------------------

    private static CommandResult Offset(CommandContext ctx)
    {
        var dist = Hz.Num(ctx.Args, "distance")!.Value;
        ObjectId srcId = ObjectId.Null, layerId = ObjectId.Null;
        var created = new List<ObjectId>();
        return WriteFlow.Run(ctx, "HZ_ENTITIES",
            (doc, tr, plan) =>
            {
                var e = Cad.Entity(doc.Database, tr, Hz.Str(ctx.Args, "handle")!);
                Cad.Editable(e, tr);
                if (e is not Curve c) throw new HzRefusal(ErrorCodes.InvalidInput, Cad.Dxf(e) + " " + e.Handle + " is not a curve. Nothing changed.");
                srcId = e.ObjectId;
                layerId = Hz.Str(ctx.Args, "layer") is { } ln ? Resolve.Layer(doc.Database, tr, ln) : e.LayerId;
                DBObjectCollection test;
                try { test = c.GetOffsetCurves(dist); }
                catch (Autodesk.AutoCAD.Runtime.Exception ex) { throw new HzRefusal(ErrorCodes.InvalidInput, "AutoCAD cannot offset this curve by " + dist + ": " + ex.Message + ". Nothing changed."); }
                plan["source"] = Cad.Describe(e, tr, false); plan["distance"] = dist; plan["result_curves"] = test.Count;
                foreach (DBObject o in test) o.Dispose();
                if (test.Count == 0) throw new HzRefusal(ErrorCodes.InvalidInput, "The offset produces no curve (distance too large for this shape?). Nothing changed.");
            },
            (doc, tr) =>
            {
                var c = (Curve)tr.GetObject(srcId, OpenMode.ForRead);
                var owner = (BlockTableRecord)tr.GetObject(c.OwnerId, OpenMode.ForWrite);
                foreach (Entity n in c.GetOffsetCurves(dist))
                {
                    n.LayerId = layerId;
                    created.Add(owner.AppendEntity(n));
                    tr.AddNewlyCreatedDBObject(n, true);
                }
            },
            (doc, tr, v, after) =>
            {
                var c = (Curve)tr.GetObject(srcId, OpenMode.ForRead);
                var rows = new JsonArray();
                foreach (var id in created)
                {
                    var n = (Curve)tr.GetObject(id, OpenMode.ForRead);
                    // Live finding: offset polylines have mitred corners (sqrt(2) d at a square corner), so measure
                    // at parameter midpoints, which for polylines are segment midpoints.
                    var span = n.EndParam - n.StartParam;
                    var segs = n is Polyline pl ? (pl.Closed ? pl.NumberOfVertices : pl.NumberOfVertices - 1) : 4;
                    for (var k = 0; k < Math.Max(1, Math.Min(segs, 8)); k++)
                    {
                        var p = n.GetPointAtParameter(n.StartParam + (k + 0.5) * span / Math.Max(1, segs));
                        v.Number(n.Handle + " distance to source at mid " + k, Math.Abs(dist), c.GetClosestPointTo(p, false).DistanceTo(p), 1e-6);
                    }
                    rows.Add(Cad.Describe(n, tr, false));
                }
                v.Check("offset curves", "> 0", created.Count, created.Count > 0);
                after["created"] = rows;
            });
    }

    private static CommandResult Explode(CommandContext ctx)
    {
        var keep = Hz.Bool(ctx.Args, "keep_source") ?? false;
        var src = new List<(ObjectId Id, double? Len)>();
        var created = new Dictionary<ObjectId, List<ObjectId>>();
        return WriteFlow.Run(ctx, "HZ_ENTITIES",
            (doc, tr, plan) =>
            {
                var rows = new JsonArray();
                foreach (var e in Editables(doc, tr, ctx.Args["handles"]))
                {
                    var parts = new DBObjectCollection();
                    try { e.Explode(parts); }
                    catch (Autodesk.AutoCAD.Runtime.Exception ex) { throw new HzRefusal(ErrorCodes.InvalidInput, Cad.Dxf(e) + " " + e.Handle + " cannot be exploded: " + ex.Message + ". Nothing changed."); }
                    rows.Add(new JsonObject { ["handle"] = e.Handle.ToString(), ["type"] = Cad.Dxf(e), ["parts"] = parts.Count });
                    foreach (DBObject o in parts) o.Dispose();
                    src.Add((e.ObjectId, Cad.Length(e)));
                }
                plan["explode"] = rows; plan["keep_source"] = keep;
            },
            (doc, tr) =>
            {
                foreach (var s in src)
                {
                    var e = (Entity)tr.GetObject(s.Id, OpenMode.ForWrite);
                    var owner = (BlockTableRecord)tr.GetObject(e.OwnerId, OpenMode.ForWrite);
                    var parts = new DBObjectCollection();
                    e.Explode(parts);
                    var list = new List<ObjectId>();
                    foreach (Entity p in parts) { list.Add(owner.AppendEntity(p)); tr.AddNewlyCreatedDBObject(p, true); }
                    created[s.Id] = list;
                    if (!keep) e.Erase();
                }
            },
            (doc, tr, v, after) =>
            {
                var rows = new JsonArray();
                foreach (var s in src)
                {
                    var parts = created[s.Id].Select(id => (Entity)tr.GetObject(id, OpenMode.ForRead)).ToList();
                    v.Check(s.Id.Handle + " parts", "> 0", parts.Count, parts.Count > 0);
                    v.Flag(s.Id.Handle + " source erased", !keep, s.Id.IsErased);
                    if (s.Len is { } l && parts.All(p => Cad.Length(p) != null))
                        v.Number(s.Id.Handle + " summed length of parts", l, parts.Sum(p => Cad.Length(p)!.Value), Math.Max(1e-6, l * 1e-9));
                    rows.Add(new JsonObject { ["source"] = s.Id.Handle.ToString(), ["parts"] = new JsonArray(parts.Take(200).Select(p => (JsonNode)Cad.Describe(p, tr, false)).ToArray()) });
                }
                after["exploded"] = rows;
            });
    }

    private static CommandResult Join(CommandContext ctx)
    {
        ObjectId baseId = ObjectId.Null;
        var others = new List<ObjectId>();
        double expected = 0;
        return WriteFlow.Run(ctx, "HZ_ENTITIES",
            (doc, tr, plan) =>
            {
                var es = Editables(doc, tr, ctx.Args["handles"]);
                if (es.Any(e => e is not Curve)) throw new HzRefusal(ErrorCodes.InvalidInput, "join works on curves only. Nothing changed.");
                baseId = es[0].ObjectId;
                others = es.Skip(1).Select(e => e.ObjectId).ToList();
                expected = es.Sum(e => Cad.Length(e) ?? 0);
                using var clone = (Entity)es[0].Clone();
                List<int> joined;
                try { joined = clone.JoinEntities(es.Skip(1).ToArray()).Cast<int>().ToList(); }
                catch (Autodesk.AutoCAD.Runtime.Exception ex) { throw new HzRefusal(ErrorCodes.InvalidInput, "AutoCAD cannot join these curves: " + ex.Message + ". Nothing changed."); }
                if (joined.Count != others.Count)
                {
                    var missing = others.Where((_, i) => !joined.Contains(i)).Select(id => id.Handle.ToString());
                    throw new HzRefusal(ErrorCodes.InvalidInput, "Rehearsal on a copy could not join " + string.Join(", ", missing) +
                        " to " + es[0].Handle + " (they must touch end to end; lines join to lines only when collinear - use a polyline as base). Nothing changed.");
                }
                plan["base"] = Cad.Describe(es[0], tr, false); plan["joined"] = others.Count; plan["expected_length"] = Hz.Finite(expected, 6);
            },
            (doc, tr) =>
            {
                var b = (Entity)tr.GetObject(baseId, OpenMode.ForWrite);
                var os = others.Select(id => (Entity)tr.GetObject(id, OpenMode.ForWrite)).ToArray();
                foreach (int i in b.JoinEntities(os)) os[i].Erase();
            },
            (doc, tr, v, after) =>
            {
                var b = (Entity)tr.GetObject(baseId, OpenMode.ForRead);
                v.Number("joined length", expected, Cad.Length(b) ?? double.NaN, Math.Max(1e-6, expected * 1e-9));
                foreach (var id in others) v.Flag(id.Handle + " merged and erased", true, id.IsErased);
                after["result"] = Cad.Describe(b, tr, true);
            });
    }

    private static CommandResult Erase(CommandContext ctx)
    {
        var ids = new List<ObjectId>();
        return WriteFlow.Run(ctx, "HZ_ENTITIES",
            (doc, tr, plan) =>
            {
                var es = Editables(doc, tr, ctx.Args["handles"]);
                ids = es.Select(e => e.ObjectId).ToList();
                plan["erase"] = new JsonArray(es.Take(200).Select(e => (JsonNode)Cad.Describe(e, tr, false)).ToArray());
                plan["count"] = es.Count;
            },
            (doc, tr) => { foreach (var id in ids) tr.GetObject(id, OpenMode.ForWrite).Erase(); },
            (doc, tr, v, after) =>
            {
                foreach (var id in ids) v.Flag(id.Handle + " erased", true, id.IsErased);
                after["erased"] = ids.Count(i => i.IsErased);
            });
    }

    // ---- draw -------------------------------------------------------------------

    private static readonly Dictionary<string, (TextHorizontalMode H, TextVerticalMode V)> Justify = new()
    {
        ["left"] = (TextHorizontalMode.TextLeft, TextVerticalMode.TextBase), ["center"] = (TextHorizontalMode.TextCenter, TextVerticalMode.TextBase),
        ["right"] = (TextHorizontalMode.TextRight, TextVerticalMode.TextBase), ["middle"] = (TextHorizontalMode.TextMid, TextVerticalMode.TextBase),
        ["top_left"] = (TextHorizontalMode.TextLeft, TextVerticalMode.TextTop), ["top_center"] = (TextHorizontalMode.TextCenter, TextVerticalMode.TextTop),
        ["top_right"] = (TextHorizontalMode.TextRight, TextVerticalMode.TextTop), ["middle_left"] = (TextHorizontalMode.TextLeft, TextVerticalMode.TextVerticalMid),
        ["middle_center"] = (TextHorizontalMode.TextCenter, TextVerticalMode.TextVerticalMid), ["middle_right"] = (TextHorizontalMode.TextRight, TextVerticalMode.TextVerticalMid),
        ["bottom_left"] = (TextHorizontalMode.TextLeft, TextVerticalMode.TextBottom), ["bottom_center"] = (TextHorizontalMode.TextCenter, TextVerticalMode.TextBottom),
        ["bottom_right"] = (TextHorizontalMode.TextRight, TextVerticalMode.TextBottom),
    };

    private static readonly Dictionary<string, AttachmentPoint> Attach = new()
    {
        ["top_left"] = AttachmentPoint.TopLeft, ["top_center"] = AttachmentPoint.TopCenter, ["top_right"] = AttachmentPoint.TopRight,
        ["middle_left"] = AttachmentPoint.MiddleLeft, ["middle_center"] = AttachmentPoint.MiddleCenter, ["middle_right"] = AttachmentPoint.MiddleRight,
        ["bottom_left"] = AttachmentPoint.BottomLeft, ["bottom_center"] = AttachmentPoint.BottomCenter, ["bottom_right"] = AttachmentPoint.BottomRight,
    };

    private static double NormAngle(double rad) { var a = rad % (2 * Math.PI); return a < 0 ? a + 2 * Math.PI : a; }

    private static ObjectId TextStyle(Database db, Transaction tr, string? name) =>
        name == null ? db.Textstyle : Cad.Symbol(tr, db.TextStyleTableId, name, "text style");

    private sealed record Planned(JsonObject Item, EntProps Props, ObjectId Style, List<ObjectId> Boundaries, double ExpectedArea);

    private static CommandResult Draw(CommandContext ctx)
    {
        var items = ((JsonArray)ctx.Args["items"]!).Select(n => (JsonObject)n!).ToList();
        var defaultLayer = Hz.Str(ctx.Args, "layer");
        var planned = new List<Planned>();
        var created = new List<ObjectId>();
        return WriteFlow.Run(ctx, "HZ_ENTITIES",
            (doc, tr, plan) =>
            {
                var db = doc.Database;
                var counts = new SortedDictionary<string, int>();
                foreach (var it in items)
                {
                    var type = Hz.Str(it, "type")!;
                    var props = EntProps.From(it, defaultLayer);
                    props.Plan(db, tr);
                    var style = type is "text" or "mtext" ? TextStyle(db, tr, Hz.Str(it, "style")) : ObjectId.Null;
                    var bounds = new List<ObjectId>();
                    double area = 0;
                    if (type == "hatch")
                        foreach (var h in Resolve.Strings(it["boundaries"]))
                        {
                            var b = Cad.Entity(db, tr, h);
                            if (b is not Curve { Closed: true } c) throw new HzRefusal(ErrorCodes.InvalidInput, "Hatch boundary " + h.ToUpperInvariant() + " is not a closed curve. Nothing changed.");
                            bounds.Add(b.ObjectId);
                            area += c.Area;
                        }
                    if (type == "hatch")
                    {
                        var pat = Hz.Str(it, "pattern") ?? "SOLID";
                        using var probe = new Hatch();
                        try { probe.SetHatchPattern(HatchPatternType.PreDefined, pat); }
                        catch (Autodesk.AutoCAD.Runtime.Exception) { throw new HzRefusal(ErrorCodes.NotFound, "Hatch pattern '" + pat + "' is not a predefined pattern (acadiso.pat). Nothing changed."); }
                    }
                    planned.Add(new Planned(it, props, style, bounds, area));
                    counts[type] = counts.TryGetValue(type, out var n) ? n + 1 : 1;
                }
                var c2 = new JsonObject();
                foreach (var kv in counts) c2[kv.Key] = kv.Value;
                plan["items"] = items.Count; plan["by_type"] = c2; plan["default_layer"] = defaultLayer ?? "current layer";
            },
            (doc, tr) =>
            {
                var db = doc.Database;
                foreach (var p in planned)
                {
                    var e = Build(db, p);
                    var id = Cad.Append(db, tr, e);
                    p.Props.Apply(db, tr, e);
                    if (e is DBText t && Hz.Str(p.Item, "justify") is { } j && j != "left") t.AdjustAlignment(db);
                    if (e is Hatch h)
                    {
                        h.SetHatchPattern(HatchPatternType.PreDefined, Hz.Str(p.Item, "pattern") ?? "SOLID");
                        if (Hz.Num(p.Item, "scale") is { } sc) h.PatternScale = sc;
                        if (Hz.Num(p.Item, "angle") is { } an) h.PatternAngle = Cad.Rad(an);
                        h.Associative = false;
                        foreach (var b in p.Boundaries) h.AppendLoop(HatchLoopTypes.External, new ObjectIdCollection { b });
                        h.EvaluateHatch(true);
                    }
                    created.Add(id);
                }
            },
            (doc, tr, v, after) =>
            {
                var rows = new JsonArray();
                for (var i = 0; i < created.Count; i++)
                {
                    var e = (Entity)tr.GetObject(created[i], OpenMode.ForRead);
                    VerifyItem(v, "item " + i, planned[i], e);
                    planned[i].Props.Verify(v, "item " + i, e);
                    rows.Add(Cad.Describe(e, tr, false));
                }
                after["created"] = rows;
            });
    }

    private static Entity Build(Database db, Planned p) => Cad.New(db, BuildRaw(db, p)) is var e && e is DBText or MText ? Restyle(e, p) : e;

    /// <summary>Re-apply text style/height/rotation after SetDatabaseDefaults (which resets them for text).</summary>
    private static Entity Restyle(Entity e, Planned p)
    {
        var it = p.Item;
        if (e is DBText t) { t.TextStyleId = p.Style; t.Height = Hz.Num(it, "height")!.Value; t.Rotation = Cad.Rad(Hz.Num(it, "rotation") ?? 0); }
        if (e is MText m) { m.TextStyleId = p.Style; m.TextHeight = Hz.Num(it, "height")!.Value; m.Rotation = Cad.Rad(Hz.Num(it, "rotation") ?? 0); }
        return e;
    }

    private static Entity BuildRaw(Database db, Planned p)
    {
        var it = p.Item;
        switch (Hz.Str(it, "type"))
        {
            case "line": return new Line(Resolve.P(it["from"]), Resolve.P(it["to"]));
            case "polyline":
            {
                var pl = new Polyline();
                var pts = (JsonArray)it["points"]!;
                var bulges = Resolve.Numbers(it["bulges"]);
                for (var i = 0; i < pts.Count; i++)
                {
                    var q = Resolve.P(pts[i]);
                    pl.AddVertexAt(i, new Point2d(q.X, q.Y), bulges.Count > 0 ? bulges[i] : 0, 0, 0);
                }
                pl.Closed = Hz.Bool(it, "closed") ?? false;
                pl.Elevation = Hz.Num(it, "elevation") ?? 0;
                return pl;
            }
            case "polyline3d":
            {
                var pc = new Point3dCollection();
                foreach (var q in (JsonArray)it["points"]!) pc.Add(Resolve.P(q));
                return new Polyline3d(Poly3dType.SimplePoly, pc, Hz.Bool(it, "closed") ?? false);
            }
            case "circle": return new Circle(Resolve.P(it["center"]), Vector3d.ZAxis, Hz.Num(it, "radius")!.Value);
            case "arc": return new Arc(Resolve.P(it["center"]), Hz.Num(it, "radius")!.Value, Cad.Rad(Hz.Num(it, "start_angle")!.Value), Cad.Rad(Hz.Num(it, "end_angle")!.Value));
            case "text":
            {
                var t = new DBText { TextString = Hz.Str(it, "text")!, Height = Hz.Num(it, "height")!.Value, Rotation = Cad.Rad(Hz.Num(it, "rotation") ?? 0), TextStyleId = p.Style };
                var pos = Resolve.P(it["position"]);
                var j = Justify[Hz.Str(it, "justify") ?? "left"];
                t.Position = pos;
                t.HorizontalMode = j.H; t.VerticalMode = j.V;
                if (j.H != TextHorizontalMode.TextLeft || j.V != TextVerticalMode.TextBase) t.AlignmentPoint = pos;
                return t;
            }
            case "mtext":
                return new MText
                {
                    Contents = Hz.Str(it, "text")!, Location = Resolve.P(it["position"]), TextHeight = Hz.Num(it, "height")!.Value,
                    Width = Hz.Num(it, "width") ?? 0, Rotation = Cad.Rad(Hz.Num(it, "rotation") ?? 0), TextStyleId = p.Style,
                    Attachment = Attach[Hz.Str(it, "attachment") ?? "top_left"],
                };
            case "point": return new DBPoint(Resolve.P(it["position"]));
            default: return new Hatch();
        }
    }

    private static void VerifyItem(VerificationSet v, string w, Planned p, Entity e)
    {
        var it = p.Item;
        void Pt(string what, JsonNode? want, Point3d got) { var q = Resolve.P(want); v.Check(w + " " + what, Resolve.Json(q), Resolve.Json(got), q.DistanceTo(got) <= Tol); }
        switch (Hz.Str(it, "type"))
        {
            case "line": { var l = (Line)e; Pt("start", it["from"], l.StartPoint); Pt("end", it["to"], l.EndPoint); break; }
            case "polyline":
            {
                var pl = (Polyline)e;
                var pts = (JsonArray)it["points"]!;
                var bulges = Resolve.Numbers(it["bulges"]);
                v.Check(w + " vertices", pts.Count, pl.NumberOfVertices, pts.Count == pl.NumberOfVertices);
                for (var i = 0; i < Math.Min(pts.Count, pl.NumberOfVertices); i++)
                {
                    var q = Resolve.P(pts[i]);
                    var g = pl.GetPoint2dAt(i);
                    if (Math.Abs(q.X - g.X) > Tol || Math.Abs(q.Y - g.Y) > Tol) v.Check(w + " vertex " + i, Resolve.Json(q, false), Resolve.Json(g), false);
                    if (bulges.Count > 0 && Math.Abs(bulges[i] - pl.GetBulgeAt(i)) > 1e-9) v.Check(w + " bulge " + i, bulges[i], pl.GetBulgeAt(i), false);
                }
                v.Flag(w + " closed", Hz.Bool(it, "closed") ?? false, pl.Closed);
                v.Number(w + " elevation", Hz.Num(it, "elevation") ?? 0, pl.Elevation, Tol);
                break;
            }
            case "polyline3d":
            {
                var pts = ((JsonArray)it["points"]!).Select(n => Resolve.P(n)).ToList();
                var got = new List<Point3d>();
                foreach (ObjectId vid in (Polyline3d)e) got.Add(((PolylineVertex3d)vid.GetObject(OpenMode.ForRead)).Position);
                v.Check(w + " vertices", pts.Count, got.Count, pts.Count == got.Count);
                v.Flag(w + " vertices at the requested XYZ", true, pts.Count == got.Count && pts.Zip(got).All(z => z.First.DistanceTo(z.Second) <= Tol));
                break;
            }
            case "circle": { var c = (Circle)e; Pt("center", it["center"], c.Center); v.Number(w + " radius", Hz.Num(it, "radius")!.Value, c.Radius, Tol); break; }
            case "arc":
            {
                var a = (Arc)e;
                Pt("center", it["center"], a.Center);
                v.Number(w + " radius", Hz.Num(it, "radius")!.Value, a.Radius, Tol);
                v.Number(w + " start angle", NormAngle(Cad.Rad(Hz.Num(it, "start_angle")!.Value)), NormAngle(a.StartAngle), 1e-9);
                v.Number(w + " end angle", NormAngle(Cad.Rad(Hz.Num(it, "end_angle")!.Value)), NormAngle(a.EndAngle), 1e-9);
                break;
            }
            case "text":
            {
                var t = (DBText)e;
                v.Text(w + " text", Hz.Str(it, "text"), t.TextString, false);
                v.Number(w + " height", Hz.Num(it, "height")!.Value, t.Height, Tol);
                var j = Hz.Str(it, "justify") ?? "left";
                Pt(j == "left" ? "insertion point" : "alignment point", it["position"], j == "left" ? t.Position : t.AlignmentPoint);
                v.Flag(w + " style", true, t.TextStyleId == p.Style);
                break;
            }
            case "mtext":
            {
                var m = (MText)e;
                v.Text(w + " contents", Hz.Str(it, "text"), m.Contents, false);
                v.Number(w + " height", Hz.Num(it, "height")!.Value, m.TextHeight, Tol);
                Pt("location", it["position"], m.Location);
                break;
            }
            case "point": Pt("position", it["position"], ((DBPoint)e).Position); break;
            default:
            {
                var h = (Hatch)e;
                v.Check(w + " loops", p.Boundaries.Count, h.NumberOfLoops, h.NumberOfLoops == p.Boundaries.Count);
                double area;
                try { area = h.Area; } catch (Autodesk.AutoCAD.Runtime.Exception) { area = double.NaN; }
                v.Check(w + " hatch area", "> 0 (boundaries sum " + Math.Round(p.ExpectedArea, 6) + ")", Hz.Finite(area, 6), area > 0);
                v.Text(w + " pattern", Hz.Str(it, "pattern") ?? "SOLID", h.PatternName);
                break;
            }
        }
    }
}
