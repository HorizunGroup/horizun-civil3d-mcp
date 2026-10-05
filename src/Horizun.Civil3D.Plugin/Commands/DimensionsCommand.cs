// -----------------------------------------------------------------------------
// horizun_c3d_dimensions - AutoCAD dimensions and multileaders (block C).
//
// The plan computes the analytic measurement from the caller's points; the
// re-read compares Dimension.Measurement (after RecomputeDimensionBlock) with it.
// -----------------------------------------------------------------------------
using System.Text.Json.Nodes;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Horizun.Civil3D.Core;
using Horizun.Civil3D.Plugin.Civil;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace Horizun.Civil3D.Plugin.Commands;

internal sealed class DimensionsCommand : ICommand
{
    public string Name => "dimensions";

    public CommandResult Execute(CommandContext ctx)
    {
        if (ToolRules.Validate(ctx.Tool.Name, ctx.Args) is { } why) throw new HzRefusal(ErrorCodes.InvalidInput, why + " Nothing ran.");
        return ctx.Action switch
        {
            "list" => WriteFlow.Read(ctx, (doc, tr, data) => List(ctx, doc, tr, data)),
            "mleader" => MLeaderCreate(ctx),
            "set_text" => SetText(ctx),
            "chain" => Chain(ctx),
            _ => Single(ctx),
        };
    }

    private static ObjectId DimStyle(Database db, Transaction tr, string? name) =>
        name == null ? db.Dimstyle : Cad.Symbol(tr, db.DimStyleTableId, name, "dimension style");

    private static JsonObject Describe(Dimension d, Transaction tr)
    {
        var o = Cad.Describe(d, tr, false);
        o["dim_type"] = d.GetType().Name;
        o["measurement"] = Hz.Finite(d.Measurement, 9);
        if (d is Point3AngularDimension or LineAngularDimension2) o["measurement_deg"] = Hz.Finite(d.Measurement * 180 / Math.PI, 9);
        return o;
    }

    private static void List(CommandContext ctx, Document doc, Transaction tr, JsonObject data)
    {
        var db = doc.Database;
        var style = Hz.Str(ctx.Args, "style");
        var limit = (int)(Hz.Num(ctx.Args, "limit") ?? 500);
        IEnumerable<ObjectId> ids = ctx.Args["handles"] != null
            ? Resolve.Strings(ctx.Args["handles"]).Select(h => Catalog.FromHandle(db, h))
            : Cad.ModelSpace(db, tr).Cast<ObjectId>();
        var rows = new JsonArray();
        var total = 0;
        foreach (var id in ids)
        {
            if (tr.GetObject(id, OpenMode.ForRead) is not Dimension d) continue;
            if (style != null && !string.Equals(d.DimensionStyleName, style, StringComparison.OrdinalIgnoreCase)) continue;
            total++;
            if (rows.Count < limit) rows.Add(Describe(d, tr));
        }
        data["count"] = total;
        data["dimensions"] = rows;
        data["current_style"] = ((DimStyleTableRecord)tr.GetObject(db.Dimstyle, OpenMode.ForRead)).Name;
    }

    // ---- single dimensions -------------------------------------------------------

    private static Point3d P(CommandContext ctx, string k) => Resolve.P(ctx.Args[k]);

    private static double Angle2d(Point3d from, Point3d to) => Math.Atan2(to.Y - from.Y, to.X - from.X);
    private static double Norm(double a) { a %= 2 * Math.PI; return a < 0 ? a + 2 * Math.PI : a; }

    private sealed record Spec(Func<Database, Dimension> Make, double Expected, double Tol, string Unit, JsonObject Plan);

    private static Spec Build(CommandContext ctx, Database db, Transaction tr)
    {
        var text = Hz.Str(ctx.Args, "text") ?? "";
        var plan = new JsonObject();
        switch (ctx.Action)
        {
            case "linear":
            {
                var p1 = P(ctx, "p1"); var p2 = P(ctx, "p2"); var dl = P(ctx, "dim_line_point");
                var r = Cad.Rad(Hz.Num(ctx.Args, "rotation") ?? 0);
                var m = Math.Abs((p2.X - p1.X) * Math.Cos(r) + (p2.Y - p1.Y) * Math.Sin(r));
                plan["rotation_deg"] = Hz.Num(ctx.Args, "rotation") ?? 0;
                return new Spec(_ => new RotatedDimension(r, p1, p2, dl, text, ObjectId.Null), m, 1e-6, "drawing units", plan);
            }
            case "aligned":
            {
                var p1 = P(ctx, "p1"); var p2 = P(ctx, "p2"); var dl = P(ctx, "dim_line_point");
                var m = new Point2d(p1.X, p1.Y).GetDistanceTo(new Point2d(p2.X, p2.Y));
                if (m < 1e-9) throw new HzRefusal(ErrorCodes.InvalidInput, "p1 and p2 coincide. Nothing changed.");
                return new Spec(_ => new AlignedDimension(p1, p2, dl, text, ObjectId.Null), m, 1e-6, "drawing units", plan);
            }
            case "angular":
            {
                var c = P(ctx, "center"); var p1 = P(ctx, "p1"); var p2 = P(ctx, "p2"); var ap = P(ctx, "arc_point");
                var a1 = Norm(Angle2d(c, p1)); var a2 = Norm(Angle2d(c, p2)); var aa = Norm(Angle2d(c, ap));
                var sweep = Norm(a2 - a1);
                var inside = Norm(aa - a1) <= sweep;
                var m = inside ? sweep : 2 * Math.PI - sweep;
                plan["expected_deg"] = Math.Round(m * 180 / Math.PI, 9);
                return new Spec(_ => new Point3AngularDimension(c, p1, p2, ap, text, ObjectId.Null), m, 1e-9, "radians", plan);
            }
            case "radial":
            case "diameter":
            {
                var e = Cad.Entity(db, tr, Hz.Str(ctx.Args, "entity")!);
                var (center, radius) = e switch
                {
                    Circle ci => (ci.Center, ci.Radius),
                    Arc ar => (ar.Center, ar.Radius),
                    _ => throw new HzRefusal(ErrorCodes.InvalidInput, Cad.Dxf(e) + " " + e.Handle + " is not a circle or arc. Nothing changed."),
                };
                var a = Cad.Rad(Hz.Num(ctx.Args, "angle") ?? (e is Arc arc ? (arc.StartAngle + Norm(arc.EndAngle - arc.StartAngle) / 2) * 180 / Math.PI : 45));
                var ll = Hz.Num(ctx.Args, "leader_length") ?? 0;
                var chord = new Point3d(center.X + radius * Math.Cos(a), center.Y + radius * Math.Sin(a), center.Z);
                plan["entity"] = Cad.Describe(e, tr, false);
                if (ctx.Action == "radial")
                    return new Spec(_ => new RadialDimension(center, chord, ll, text, ObjectId.Null), radius, 1e-6, "drawing units", plan);
                var far = new Point3d(center.X - radius * Math.Cos(a), center.Y - radius * Math.Sin(a), center.Z);
                return new Spec(_ => new DiametricDimension(chord, far, ll, text, ObjectId.Null), 2 * radius, 1e-6, "drawing units", plan);
            }
            default:
            {
                var fp = P(ctx, "feature_point"); var le = P(ctx, "leader_end");
                var x = Hz.Str(ctx.Args, "axis") == "x";
                plan["datum"] = "WCS origin (0,0)";
                return new Spec(_ => new OrdinateDimension(x, fp, le, text, ObjectId.Null), Math.Abs(x ? fp.X : fp.Y), 1e-6, "drawing units", plan);
            }
        }
    }

    private static CommandResult Single(CommandContext ctx)
    {
        Spec? spec = null;
        ObjectId styleId = ObjectId.Null, layerId = ObjectId.Null, id = ObjectId.Null;
        var text = Hz.Str(ctx.Args, "text");
        return WriteFlow.Run(ctx, "HZ_DIMENSIONS",
            (doc, tr, plan) =>
            {
                spec = Build(ctx, doc.Database, tr);
                styleId = DimStyle(doc.Database, tr, Hz.Str(ctx.Args, "style"));
                layerId = Resolve.Layer(doc.Database, tr, Hz.Str(ctx.Args, "layer"));
                foreach (var kv in spec.Plan) plan[kv.Key] = kv.Value?.DeepClone();
                plan["kind"] = ctx.Action; plan["expected_measurement"] = Hz.Finite(spec.Expected, 9); plan["unit"] = spec.Unit;
                plan["style"] = ((DimStyleTableRecord)tr.GetObject(styleId, OpenMode.ForRead)).Name; plan["text"] = text ?? "<measurement>";
            },
            (doc, tr) =>
            {
                var d = Cad.New(doc.Database, spec!.Make(doc.Database));
                d.DimensionStyle = styleId;
                if (text != null) d.DimensionText = text;
                id = Cad.Append(doc.Database, tr, d, layerId);
                d.RecomputeDimensionBlock(true);
            },
            (doc, tr, v, after) =>
            {
                var d = (Dimension)tr.GetObject(id, OpenMode.ForRead);
                v.Number("measurement", spec!.Expected, d.Measurement, spec.Tol);
                v.Flag("dimension style", true, d.DimensionStyle == styleId);
                if (text != null) v.Text("text override", text, d.DimensionText, false);
                after["dimension"] = Describe(d, tr);
            });
    }

    // ---- chain / baseline -------------------------------------------------------------

    private static CommandResult Chain(CommandContext ctx)
    {
        var pts = ((JsonArray)ctx.Args["points"]!).Select(n => Resolve.P(n)).ToList();
        var dl = P(ctx, "dim_line_point");
        var rot = Cad.Rad(Hz.Num(ctx.Args, "rotation") ?? 0);
        var baseline = Hz.Str(ctx.Args, "mode") == "baseline";
        var dir = new Vector3d(Math.Cos(rot), Math.Sin(rot), 0);
        ObjectId styleId = ObjectId.Null, layerId = ObjectId.Null;
        var segs = new List<(Point3d A, Point3d B, Point3d Line, double Expected)>();
        var ids = new List<ObjectId>();
        return WriteFlow.Run(ctx, "HZ_DIMENSIONS",
            (doc, tr, plan) =>
            {
                styleId = DimStyle(doc.Database, tr, Hz.Str(ctx.Args, "style"));
                layerId = Resolve.Layer(doc.Database, tr, Hz.Str(ctx.Args, "layer"));
                var ds = (DimStyleTableRecord)tr.GetObject(styleId, OpenMode.ForRead);
                var spacing = Hz.Num(ctx.Args, "spacing") ?? (ds.Dimdli > 0 ? ds.Dimdli * Math.Max(ds.Dimscale, 1) : 3.75);
                // Side of the dimension line relative to the first point, perpendicular to the dimension direction.
                var perp = new Vector3d(-dir.Y, dir.X, 0);
                var side = Math.Sign((dl - pts[0]).DotProduct(perp));
                if (side == 0) side = 1;
                for (var i = 1; i < pts.Count; i++)
                {
                    var a = baseline ? pts[0] : pts[i - 1];
                    var b = pts[i];
                    var line = baseline ? dl + perp * side * spacing * (i - 1) : dl;
                    var m = Math.Abs((b - a).DotProduct(dir));
                    if (m < 1e-9) throw new HzRefusal(ErrorCodes.InvalidInput, "points " + (baseline ? 0 : i - 1) + " and " + i + " have no extent along the dimension direction. Nothing changed.");
                    segs.Add((a, b, line, m));
                }
                plan["mode"] = baseline ? "baseline" : "continue"; plan["dimensions"] = segs.Count; plan["rotation_deg"] = Hz.Num(ctx.Args, "rotation") ?? 0;
                plan["style"] = ds.Name; if (baseline) plan["spacing"] = spacing;
                plan["expected_measurements"] = new JsonArray(segs.Select(s => (JsonNode?)JsonValue.Create(Math.Round(s.Expected, 9))).ToArray());
                if (!baseline) plan["expected_total"] = Math.Round(segs.Sum(s => s.Expected), 9);
            },
            (doc, tr) =>
            {
                foreach (var s in segs)
                {
                    var d = Cad.New(doc.Database, new RotatedDimension(rot, s.A, s.B, s.Line, "", ObjectId.Null));
                    d.DimensionStyle = styleId;
                    ids.Add(Cad.Append(doc.Database, tr, d, layerId));
                    d.RecomputeDimensionBlock(true);
                }
            },
            (doc, tr, v, after) =>
            {
                var rows = new JsonArray();
                for (var i = 0; i < ids.Count; i++)
                {
                    var d = (Dimension)tr.GetObject(ids[i], OpenMode.ForRead);
                    v.Number("dimension " + i + " measurement", segs[i].Expected, d.Measurement, 1e-6);
                    rows.Add(Describe(d, tr));
                }
                after["dimensions"] = rows;
            });
    }

    // ---- multileader / text ----------------------------------------------------------

    private static CommandResult MLeaderCreate(CommandContext ctx)
    {
        var arrow = P(ctx, "arrow_point");
        var landing = P(ctx, "landing_point");
        var text = Hz.Str(ctx.Args, "text")!;
        var height = Hz.Num(ctx.Args, "text_height");
        ObjectId styleId = ObjectId.Null, layerId = ObjectId.Null, id = ObjectId.Null;
        var lineIdx = -1;
        return WriteFlow.Run(ctx, "HZ_DIMENSIONS",
            (doc, tr, plan) =>
            {
                var db = doc.Database;
                if (arrow.DistanceTo(landing) < 1e-9) throw new HzRefusal(ErrorCodes.InvalidInput, "arrow_point and landing_point coincide. Nothing changed.");
                if (Hz.Str(ctx.Args, "style") is { } sn)
                {
                    var dict = (DBDictionary)tr.GetObject(db.MLeaderStyleDictionaryId, OpenMode.ForRead);
                    if (!dict.Contains(sn)) throw new HzRefusal(ErrorCodes.NotFound, "No multileader style '" + sn + "'. Nothing changed.", new JsonObject { ["candidates"] = Hz.Strings(Cad.Entries(dict).Select(x => x.Key)) });
                    styleId = dict.GetAt(sn);
                }
                else styleId = db.MLeaderstyle;
                layerId = Resolve.Layer(db, tr, Hz.Str(ctx.Args, "layer"));
                plan["arrow_point"] = Resolve.Json(arrow); plan["landing_point"] = Resolve.Json(landing); plan["text"] = text;
                plan["style"] = ((MLeaderStyle)tr.GetObject(styleId, OpenMode.ForRead)).Name;
            },
            (doc, tr) =>
            {
                var db = doc.Database;
                var ml = Cad.New(db, new MLeader());
                ml.MLeaderStyle = styleId;
                ml.ContentType = ContentType.MTextContent;
                var mt = new MText();
                mt.SetDatabaseDefaults(db);
                mt.Contents = text;
                mt.Location = landing;
                if (height is { } h) mt.TextHeight = h;
                ml.MText = mt;
                if (height is { } h2) ml.TextHeight = h2;
                lineIdx = ml.AddLeaderLine(landing);
                ml.AddFirstVertex(lineIdx, arrow);
                id = Cad.Append(db, tr, ml, layerId);
            },
            (doc, tr, v, after) =>
            {
                var ml = (MLeader)tr.GetObject(id, OpenMode.ForRead);
                v.Check("leader lines", 1, ml.LeaderLineCount, ml.LeaderLineCount == 1);
                if (lineIdx >= 0 && ml.LeaderLineCount > 0)
                {
                    var first = ml.GetFirstVertex(lineIdx);
                    v.Check("arrow head", Resolve.Json(arrow), Resolve.Json(first), first.DistanceTo(arrow) <= 1e-6);
                    after["last_vertex"] = Resolve.Json(ml.GetLastVertex(lineIdx));
                }
                v.Text("text", text, ml.MText?.Contents, false);
                v.Flag("multileader style", true, ml.MLeaderStyle == styleId);
                after["mleader"] = Cad.Describe(ml, tr, false);
            });
    }

    private static CommandResult SetText(CommandContext ctx)
    {
        var text = Hz.Str(ctx.Args, "text")!;
        var ids = new List<ObjectId>();
        return WriteFlow.Run(ctx, "HZ_DIMENSIONS",
            (doc, tr, plan) =>
            {
                var rows = new JsonArray();
                foreach (var h in Resolve.Strings(ctx.Args["handles"]))
                {
                    var e = Cad.Entity(doc.Database, tr, h);
                    if (e is not Dimension d) throw new HzRefusal(ErrorCodes.InvalidInput, Cad.Dxf(e) + " " + e.Handle + " is not a dimension. Nothing changed.");
                    Cad.Editable(e, tr);
                    ids.Add(e.ObjectId);
                    rows.Add(new JsonObject { ["handle"] = d.Handle.ToString(), ["before"] = d.DimensionText, ["measurement"] = Hz.Finite(d.Measurement, 9) });
                }
                plan["dimensions"] = rows; plan["text"] = text; plan["note"] = "\"<>\" inside the text is replaced by the measured value; \"\" restores the plain measurement.";
            },
            (doc, tr) => { foreach (var id in ids) { var d = (Dimension)tr.GetObject(id, OpenMode.ForWrite); d.DimensionText = text; d.RecomputeDimensionBlock(true); } },
            (doc, tr, v, after) =>
            {
                foreach (var id in ids) v.Text(id.Handle + " text", text, ((Dimension)tr.GetObject(id, OpenMode.ForRead)).DimensionText, false);
                after["updated"] = ids.Count;
            });
    }
}
