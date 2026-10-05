// -----------------------------------------------------------------------------
// horizun_c3d_alignment - get, station_offset, create_from_polyline,
// create_by_pis, create_offset. API: docs/api-probes/2025/AeccDbMgd.phase3-roads*.txt.
//
// create_by_pis computes the expected geometry analytically (tangent length
// T = R tan(delta/2) must fit on both adjacent tangents; total length =
// sum(tangents) - sum(2T - arc)) and the re-read must match it.
// -----------------------------------------------------------------------------
using System.Text.Json.Nodes;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.DatabaseServices;
using Horizun.Civil3D.Core;
using Horizun.Civil3D.Plugin.Civil;

namespace Horizun.Civil3D.Plugin.Commands;

internal sealed class AlignmentCommand : ICommand
{
    public string Name => "alignment";
    private const double Tol = 1e-6;

    public CommandResult Execute(CommandContext ctx)
    {
        if (ToolRules.Validate(ctx.Tool.Name, ctx.Args) is { } why) throw new HzRefusal(ErrorCodes.InvalidInput, why + " Nothing ran.");
        return ctx.Action switch
        {
            "get" => WriteFlow.Read(ctx, (doc, tr, data) =>
            {
                var al = Resolve.Open<Alignment>(tr, Resolve.Target(doc, tr, "alignment", ctx.Args));
                data["alignment"] = Describe(al, tr);
            }),
            "station_offset" => WriteFlow.Read(ctx, (doc, tr, data) => StationOffset(ctx, doc, tr, data)),
            "create_from_polyline" => FromPolyline(ctx),
            "create_by_pis" => ByPis(ctx),
            _ => Offset(ctx),
        };
    }

    internal static JsonObject Describe(Alignment al, Transaction tr)
    {
        var o = Catalog.Describe(al, tr, new Catalog.Lookup(tr), true);
        o["alignment_type"] = al.AlignmentType.ToString();
        var ents = new JsonArray();
        for (var i = 0; i < al.Entities.Count; i++) ents.Add(Entity(al.Entities.GetEntityByOrder(i)));
        o["entities"] = ents;
        return o;
    }

    private static JsonObject Entity(AlignmentEntity e)
    {
        var o = new JsonObject { ["entity_id"] = e.EntityId, ["type"] = e.EntityType.ToString() };
        if (e is AlignmentCurve c)
        {
            o["start_station"] = Hz.Finite(c.StartStation, 6);
            o["end_station"] = Hz.Finite(c.EndStation, 6);
            o["length"] = Hz.Finite(c.Length, 6);
            o["start_point"] = Resolve.Json(c.StartPoint);
            o["end_point"] = Resolve.Json(c.EndPoint);
        }
        switch (e)
        {
            case AlignmentLine l:
                o["direction_rad"] = Hz.Finite(l.Direction, 9);
                break;
            case AlignmentArc a:
                o["radius"] = Hz.Finite(a.Radius, 6);
                o["center"] = Resolve.Json(a.CenterPoint);
                o["clockwise"] = a.Clockwise;
                o["delta_rad"] = Hz.Finite(a.Delta, 9);
                o["pi_station"] = Hz.Finite(a.PIStation, 6);
                break;
        }
        return o;
    }

    private static void StationOffset(CommandContext ctx, Document doc, Transaction tr, JsonObject data)
    {
        var al = Resolve.Open<Alignment>(tr, Resolve.Target(doc, tr, "alignment", ctx.Args));
        var rows = new JsonArray();
        if (ctx.Args["points"] is JsonArray pts)
            foreach (var p in pts.Cast<JsonObject>())
            {
                double st = 0, off = 0; var outOfRange = false;
                var x = Hz.Num(p, "x")!.Value; var y = Hz.Num(p, "y")!.Value;
                al.StationOffsetAcceptOutOfRange(x, y, ref st, ref off, ref outOfRange);
                rows.Add(new JsonObject { ["x"] = x, ["y"] = y, ["station"] = Hz.Finite(st, 6), ["offset"] = Hz.Finite(off, 6), ["out_of_range"] = outOfRange });
            }
        else
            foreach (var s in ((JsonArray)ctx.Args["stations"]!).Cast<JsonObject>())
            {
                var st = Hz.Num(s, "station")!.Value; var off = Hz.Num(s, "offset") ?? 0;
                if (st < al.StartingStation - Tol || st > al.EndingStation + Tol)
                {
                    rows.Add(new JsonObject { ["station"] = st, ["offset"] = off, ["x"] = null, ["y"] = null, ["reason"] = "station outside " + al.StartingStation + "-" + al.EndingStation });
                    continue;
                }
                double e = 0, n = 0;
                al.PointLocation(st, off, ref e, ref n);
                rows.Add(new JsonObject { ["station"] = st, ["offset"] = off, ["x"] = Hz.Finite(e, 6), ["y"] = Hz.Finite(n, 6) });
            }
        data["alignment"] = al.Name;
        data["offset_convention"] = "Civil 3D: negative offset = left of the alignment direction.";
        data["rows"] = rows;
    }

    private static (ObjectId Site, ObjectId Layer, ObjectId Style, ObjectId LabelSet) Common(CommandContext ctx, Document doc, Transaction tr, JsonObject plan)
    {
        var civil = CommandContext.Civil(doc);
        var site = Resolve.Site(doc, tr, Hz.Str(ctx.Args, "site"));
        var layer = Resolve.Layer(doc.Database, tr, Hz.Str(ctx.Args, "layer"));
        var style = Resolve.Style(civil, tr, "alignment", Hz.Str(ctx.Args, "style"));
        var labels = Resolve.AlignmentLabelSet(civil, tr, Hz.Str(ctx.Args, "label_set"));
        plan["site"] = site.IsNull ? "(siteless)" : Catalog.NameOf(site, tr);
        plan["layer"] = ((LayerTableRecord)tr.GetObject(layer, OpenMode.ForRead)).Name;
        plan["style"] = ((Autodesk.Civil.DatabaseServices.Styles.StyleBase)tr.GetObject(style, OpenMode.ForRead)).Name;
        plan["label_set"] = ((Autodesk.Civil.DatabaseServices.Styles.StyleBase)tr.GetObject(labels, OpenMode.ForRead)).Name;
        return (site, layer, style, labels);
    }

    private static void CheckCommon(VerificationSet v, Alignment al, string name, (ObjectId Site, ObjectId Layer, ObjectId Style, ObjectId LabelSet) c)
    {
        v.Text("name", name, al.Name, false);
        v.Text("style handle", c.Style.Handle.ToString(), al.StyleId.Handle.ToString(), false);
        v.Text("layer handle", c.Layer.Handle.ToString(), al.LayerId.Handle.ToString(), false);
    }

    private static CommandResult FromPolyline(CommandContext ctx)
    {
        var name = Hz.Str(ctx.Args, "new_name")!;
        var addCurves = Hz.Bool(ctx.Args, "add_curves") ?? false;
        var erase = Hz.Bool(ctx.Args, "erase_polyline") ?? false;
        ObjectId pl = ObjectId.Null, created = ObjectId.Null;
        (ObjectId Site, ObjectId Layer, ObjectId Style, ObjectId LabelSet) c = default;
        double plLength = 0; Point3d plStart = default; int segments = 0;
        return WriteFlow.Run(ctx, "HZ_ALIGNMENT",
            (doc, tr, plan) =>
            {
                Resolve.Unique(doc, tr, "alignment", name);
                pl = Catalog.FromHandle(doc.Database, Hz.Str(ctx.Args, "polyline")!);
                var poly = tr.GetObject(pl, OpenMode.ForRead) as Polyline
                           ?? throw new HzRefusal(ErrorCodes.InvalidInput, "polyline must be a (lightweight) polyline. Nothing changed.");
                plLength = poly.Length; plStart = poly.StartPoint;
                segments = poly.Closed ? poly.NumberOfVertices : poly.NumberOfVertices - 1;
                c = Common(ctx, doc, tr, plan);
                plan["polyline"] = new JsonObject { ["handle"] = poly.Handle.ToString(), ["vertices"] = poly.NumberOfVertices, ["length"] = Hz.Finite(plLength, 6), ["closed"] = poly.Closed };
                plan["new_name"] = name; plan["add_curves"] = addCurves; plan["erase_polyline"] = erase;
            },
            (doc, tr) =>
            {
                var opts = new PolylineOptions { PlineId = pl, AddCurvesBetweenTangents = addCurves, EraseExistingEntities = erase };
                created = Alignment.Create(CommandContext.Civil(doc), opts, name, c.Site, c.Layer, c.Style, c.LabelSet);
            },
            (doc, tr, v, after) =>
            {
                var al = Resolve.Open<Alignment>(tr, created);
                CheckCommon(v, al, name, c);
                if (!addCurves)
                {
                    v.Number("length = polyline length", plLength, al.Length, Math.Max(1e-6, plLength * 1e-9));
                    v.Check("entities = polyline segments", segments, al.Entities.Count, al.Entities.Count == segments);
                }
                double st = 0, off = 0;
                al.StationOffset(plStart.X, plStart.Y, ref st, ref off);
                v.Number("starts at the polyline start (station)", al.StartingStation, st, 1e-6);
                v.Number("starts at the polyline start (offset)", 0, off, 1e-6);
                if (erase) v.Flag("source polyline erased", true, pl.IsErased);
                after["alignment"] = Describe(al, tr);
            });
    }

    private static CommandResult ByPis(CommandContext ctx)
    {
        var name = Hz.Str(ctx.Args, "new_name")!;
        var pis = ((JsonArray)ctx.Args["pis"]!).Select(p => Resolve.P(p)).ToList();
        var radii = Resolve.Numbers(ctx.Args["radii"]);
        while (radii.Count < pis.Count - 2) radii.Add(0);
        (ObjectId Site, ObjectId Layer, ObjectId Style, ObjectId LabelSet) c = default;
        ObjectId created = ObjectId.Null;
        double expectedLength = 0;
        return WriteFlow.Run(ctx, "HZ_ALIGNMENT",
            (doc, tr, plan) =>
            {
                Resolve.Unique(doc, tr, "alignment", name);
                c = Common(ctx, doc, tr, plan);
                var segLen = new List<double>();
                for (var i = 0; i + 1 < pis.Count; i++)
                {
                    var d = pis[i].DistanceTo(pis[i + 1]);
                    if (d < 1e-6) throw new HzRefusal(ErrorCodes.InvalidInput, "PIs " + i + " and " + (i + 1) + " coincide. Nothing changed.");
                    segLen.Add(d);
                }
                var tangents = new double[pis.Count];
                var curves = new JsonArray();
                expectedLength = segLen.Sum();
                for (var i = 1; i + 1 < pis.Count; i++)
                {
                    var r = radii[i - 1];
                    var a = pis[i] - pis[i - 1]; var b = pis[i + 1] - pis[i];
                    var delta = a.GetAngleTo(b);
                    if (r <= 0) { curves.Add(new JsonObject { ["pi"] = i, ["radius"] = 0, ["note"] = "no curve" }); continue; }
                    if (delta < 1e-9) throw new HzRefusal(ErrorCodes.InvalidInput, "PI " + i + " has no deflection; a curve there is impossible. Use radius 0. Nothing changed.");
                    var t = r * Math.Tan(delta / 2);
                    tangents[i] = t;
                    var arc = r * delta;
                    expectedLength -= 2 * t - arc;
                    curves.Add(new JsonObject { ["pi"] = i, ["radius"] = r, ["deflection_deg"] = Hz.Finite(delta * 180 / Math.PI, 6), ["tangent_length"] = Hz.Finite(t, 6), ["arc_length"] = Hz.Finite(arc, 6) });
                }
                for (var i = 0; i + 1 < pis.Count; i++)
                    if (tangents[i] + tangents[i + 1] > segLen[i] + 1e-9)
                        throw new HzRefusal(ErrorCodes.InvalidInput, "Segment " + i + "-" + (i + 1) + " (" + segLen[i].ToString("F3") + ") is shorter than the tangent lengths of its curves (" +
                            (tangents[i] + tangents[i + 1]).ToString("F3") + "). Reduce the radii. Nothing changed.");
                plan["new_name"] = name;
                plan["pis"] = Hz.Arr(pis.Select(p => (JsonNode?)Resolve.Json(p, false)));
                plan["curves"] = curves;
                plan["expected_length"] = Hz.Finite(expectedLength, 6);
            },
            (doc, tr) =>
            {
                created = Alignment.Create(CommandContext.Civil(doc), name, c.Site, c.Layer, c.Style, c.LabelSet);
                var al = (Alignment)tr.GetObject(created, OpenMode.ForWrite);
                var lineIds = new List<int>();
                for (var i = 0; i + 1 < pis.Count; i++) lineIds.Add(al.Entities.AddFixedLine(pis[i], pis[i + 1]).EntityId);
                for (var i = 1; i + 1 < pis.Count; i++)
                    if (radii[i - 1] > 0)
                        al.Entities.AddFreeCurve(lineIds[i - 1], lineIds[i], radii[i - 1], CurveParamType.Radius, false, CurveType.Compound);
            },
            (doc, tr, v, after) =>
            {
                var al = Resolve.Open<Alignment>(tr, created);
                CheckCommon(v, al, name, c);
                var arcs = new List<AlignmentArc>();
                var lines = 0;
                for (var i = 0; i < al.Entities.Count; i++)
                {
                    var e = al.Entities.GetEntityByOrder(i);
                    if (e is AlignmentArc a) arcs.Add(a); else if (e is AlignmentLine) lines++;
                }
                var wanted = radii.Where(r => r > 0).ToList();
                v.Check("tangents", pis.Count - 1, lines, lines == pis.Count - 1);
                v.Check("curves", wanted.Count, arcs.Count, arcs.Count == wanted.Count);
                for (var i = 0; i < Math.Min(arcs.Count, wanted.Count); i++) v.Number("curve " + (i + 1) + " radius", wanted[i], arcs[i].Radius, 1e-6);
                v.Number("total length (analytic)", expectedLength, al.Length, 1e-6);
                after["alignment"] = Describe(al, tr);
            });
    }

    private static CommandResult Offset(CommandContext ctx)
    {
        var name = Hz.Str(ctx.Args, "new_name")!;
        var offset = Hz.Num(ctx.Args, "offset")!.Value;
        ObjectId parent = ObjectId.Null, style = ObjectId.Null, created = ObjectId.Null;
        double start = 0, end = 0;
        return WriteFlow.Run(ctx, "HZ_ALIGNMENT",
            (doc, tr, plan) =>
            {
                Resolve.Unique(doc, tr, "alignment", name);
                parent = Resolve.Target(doc, tr, "alignment", ctx.Args);
                var al = Resolve.Open<Alignment>(tr, parent);
                start = Hz.Num(ctx.Args, "start_station") ?? al.StartingStation;
                end = Hz.Num(ctx.Args, "end_station") ?? al.EndingStation;
                if (start < al.StartingStation - Tol || end > al.EndingStation + Tol || end <= start)
                    throw new HzRefusal(ErrorCodes.InvalidInput, "Station range " + start + "-" + end + " is outside the parent " + al.StartingStation + "-" + al.EndingStation + ". Nothing changed.");
                style = Resolve.Style(CommandContext.Civil(doc), tr, "alignment", Hz.Str(ctx.Args, "style"));
                plan["parent"] = al.Name; plan["offset"] = offset; plan["start_station"] = start; plan["end_station"] = end; plan["new_name"] = name;
            },
            (doc, tr) => created = Alignment.CreateOffsetAlignment(name, parent, offset, style, start, end),
            (doc, tr, v, after) =>
            {
                var off = Resolve.Open<Alignment>(tr, created);
                var par = Resolve.Open<Alignment>(tr, parent);
                v.Text("name", name, off.Name, false);
                foreach (var f in new[] { 0.1, 0.5, 0.9 })
                {
                    var s = start + (end - start) * f;
                    double x = 0, y = 0, st = 0, o2 = 0;
                    par.PointLocation(s, offset, ref x, ref y);
                    off.StationOffset(x, y, ref st, ref o2);
                    v.Number("parent station " + s.ToString("F3") + " at offset " + offset + " lies on the new alignment", 0, o2, 1e-3);
                }
                after["alignment"] = Describe(off, tr);
            });
    }
}
