// -----------------------------------------------------------------------------
// horizun_c3d_labels - Civil 3D object labels (block B).
// API: docs/api-probes/2025/AeccDbMgd.phase3-labels.txt, phase3-labelstyles.txt,
// phase3-labelmembers.txt.
//
// Every created label is re-read in a new transaction: it exists, it is attached
// to the requested feature (LabelBase.FeatureId), it carries the requested style,
// and its anchor (location / station / elevation / ratio) is the one requested.
// Creating a label does not modify its feature, so data-shortcut references and
// other non-editable features can be labelled (the usual production workflow).
// -----------------------------------------------------------------------------
using System.Text.Json.Nodes;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.DatabaseServices.Styles;
using Horizun.Civil3D.Core;
using Horizun.Civil3D.Plugin.Civil;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using Label = Autodesk.Civil.DatabaseServices.Label;

namespace Horizun.Civil3D.Plugin.Commands;

internal sealed class LabelsCommand : ICommand
{
    public string Name => "labels";

    public CommandResult Execute(CommandContext ctx)
    {
        if (ToolRules.Validate(ctx.Tool.Name, ctx.Args) is { } why) throw new HzRefusal(ErrorCodes.InvalidInput, why + " Nothing ran.");
        return ctx.Action switch
        {
            "list_styles" => WriteFlow.Read(ctx, (doc, tr, data) => ListStyles(ctx, doc, tr, data)),
            "list" => WriteFlow.Read(ctx, (doc, tr, data) => List(ctx, doc, tr, data)),
            "get" => WriteFlow.Read(ctx, (doc, tr, data) =>
            {
                var id = Catalog.FromHandle(doc.Database, Hz.Str(ctx.Args, "handle")!);
                data["label"] = tr.GetObject(id, OpenMode.ForRead) is LabelBase lb ? Describe(lb, tr, true)
                    : throw new HzRefusal(ErrorCodes.InvalidInput, "Handle " + id.Handle + " is not a Civil 3D label.");
            }),
            "alignment_stations" => AlignmentStations(ctx),
            "alignment_geometry" => AlignmentGeometry(ctx),
            "station_offset" => PointLabels(ctx, "alignment", "alignment_station_offset", true,
                (f, p, s, m) => StationOffsetLabel.Create(f, s, m, p), (l, p) => ((StationOffsetLabel)l).Location),
            "surface_spot" => PointLabels(ctx, "surface", "surface_spot_elevation", true,
                (f, p, s, m) => SurfaceElevationLabel.Create(f, p, s, m), (l, p) => ((SurfaceElevationLabel)l).Location),
            "surface_slope" => SurfaceSlope(ctx),
            "contour_labels" => ContourLabels(ctx),
            "profile_pvis" => ProfilePvis(ctx),
            "station_elevation" => StationElevation(ctx),
            "note" => Note(ctx),
            "segment" => Segment(ctx),
            "set_text" => SetText(ctx),
            _ => Erase(ctx),
        };
    }

    // ---- styles -------------------------------------------------------------

    internal static StyleCollectionBase StyleCollection(CivilDocument civil, string kind)
    {
        var ls = civil.Styles.LabelStyles;
        return kind switch
        {
            "alignment_major_station" => ls.AlignmentLabelStyles.MajorStationLabelStyles,
            "alignment_minor_station" => ls.AlignmentLabelStyles.MinorStationLabelStyles,
            "alignment_line" => ls.AlignmentLabelStyles.LineLabelStyles,
            "alignment_curve" => ls.AlignmentLabelStyles.CurveLabelStyles,
            "alignment_station_offset" => ls.AlignmentLabelStyles.StationOffsetLabelStyles,
            "surface_spot_elevation" => ls.SurfaceLabelStyles.SpotElevationLabelStyles,
            "surface_slope" => ls.SurfaceLabelStyles.SlopeLabelStyles,
            "surface_contour" => ls.SurfaceLabelStyles.ContourLabelStyles,
            "profile_grade_break" => ls.ProfileLabelStyles.GradeBreakLabelStyles,
            "profile_view_station_elevation" => ls.ProfileViewLabelStyles.StationElevationLabelStyles,
            "general_note" => ls.GeneralNoteLabelStyles,
            "general_line" => ls.GeneralLineLabelStyles,
            "general_curve" => ls.GeneralCurveLabelStyles,
            "point" => ls.PointLabelStyles.LabelStyles,
            "marker" => civil.Styles.MarkerStyles,
            _ => throw new HzRefusal(ErrorCodes.InvalidInput, "Unknown label style kind '" + kind + "'."),
        };
    }

    private static ObjectId Style(Document doc, Transaction tr, string kind, string? name) =>
        Resolve.FromCollection(StyleCollection(CommandContext.Civil(doc), kind), tr, name, kind.Replace('_', ' ') + " label style");

    private static ObjectId Marker(Document doc, Transaction tr, string? name) =>
        Resolve.FromCollection(CommandContext.Civil(doc).Styles.MarkerStyles, tr, name, "marker style");

    private static void ListStyles(CommandContext ctx, Document doc, Transaction tr, JsonObject data)
    {
        var kind = Hz.Str(ctx.Args, "kind")!;
        var names = new JsonArray();
        foreach (ObjectId id in StyleCollection(CommandContext.Civil(doc), kind))
        {
            var s = (StyleBase)tr.GetObject(id, OpenMode.ForRead);
            names.Add(new JsonObject { ["name"] = s.Name, ["handle"] = s.Handle.ToString() });
        }
        data["kind"] = kind;
        data["styles"] = names;
        data["default_when_omitted"] = names.Count > 0 ? names[0]!["name"]!.GetValue<string>() : null;
    }

    // ---- reads --------------------------------------------------------------

    private static string? StyleName(ObjectId id, Transaction tr)
    {
        if (id.IsNull) return null;
        try { return (tr.GetObject(id, OpenMode.ForRead) as StyleBase)?.Name ?? Catalog.NameOf(id, tr); } catch (System.Exception) { return null; }
    }

    internal static JsonObject Describe(LabelBase lb, Transaction tr, bool texts)
    {
        var o = new JsonObject
        {
            ["handle"] = lb.Handle.ToString(), ["class"] = lb.GetType().Name, ["label_type"] = lb.LabelType.ToString(),
            ["layer"] = lb.Layer,
            ["feature_handle"] = lb.FeatureId.IsNull ? null : lb.FeatureId.Handle.ToString(),
            ["feature_name"] = lb.FeatureId.IsNull ? null : Catalog.NameOf(lb.FeatureId, tr),
        };
        switch (lb)
        {
            case Label l:
                o["style"] = StyleName(l.StyleId, tr);
                o["location"] = Resolve.Json(l.LabelLocation);
                o["anchor"] = Resolve.Json(l.AnchorInfo.Location);
                if (texts)
                {
                    var comps = new JsonArray();
                    var i = 0;
                    foreach (ObjectId cid in l.GetTextComponentIds())
                    {
                        string? ov = null;
                        try { if (l.IsTextComponentOverriden(cid)) ov = l.GetTextComponentOverride(cid); } catch (Autodesk.AutoCAD.Runtime.Exception) { }
                        comps.Add(new JsonObject { ["index"] = i++, ["name"] = Catalog.NameOf(cid, tr), ["override"] = ov });
                    }
                    o["text_components"] = comps;
                }
                break;
            case LabelGroup g:
                o["style"] = StyleName(g.StyleId, tr);
                o["sub_labels"] = (long)g.SubEntityCount;
                // Live finding: SurfaceContourLabelGroup throws "This property is not supported" for the range.
                if (g is AutoFeatureLabelGroup a)
                    try { o["range_start"] = Hz.Finite(a.RangeStart, 6); o["range_end"] = Hz.Finite(a.RangeEnd, 6); } catch (InvalidOperationException) { }
                break;
        }
        switch (lb)
        {
            case StationElevationLabel se: o["station"] = Hz.Finite(se.Station, 6); o["elevation"] = Hz.Finite(se.Elevation, 6); break;
            case GeneralSegmentLabel gs: o["ratio"] = Hz.Finite(gs.Ratio, 9); break;
            case SurfaceSlopeLabel sl: o["slope_label_type"] = sl.SlopeLabelType.ToString(); break;
        }
        return o;
    }

    private static void List(CommandContext ctx, Document doc, Transaction tr, JsonObject data)
    {
        var db = doc.Database;
        ObjectId feature = ObjectId.Null;
        if (Hz.Str(ctx.Args, "alignment") is { } al) feature = Resolve.Named(doc, tr, "alignment", al);
        else if (Hz.Str(ctx.Args, "surface") is { } sf) feature = Resolve.Named(doc, tr, "surface", sf);
        else if (Hz.Str(ctx.Args, "profile_view") is { } pv) feature = Resolve.Named(doc, tr, "profile_view", pv);
        else if (Hz.Str(ctx.Args, "entity") is { } h) feature = Catalog.FromHandle(db, h);
        var limit = (int)(Hz.Num(ctx.Args, "limit") ?? 500);
        var cls = RXObject.GetClass(typeof(LabelBase));
        var rows = new JsonArray();
        var byType = new SortedDictionary<string, int>();
        var total = 0;
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
        foreach (ObjectId id in ms)
        {
            // Live finding: RXObject.GetClass(typeof(LabelBase)) is broader than labels (a TinSurface passed
            // IsDerivedFrom); the class test only skips plain AutoCAD entities, the type test decides.
            if (!id.ObjectClass.IsDerivedFrom(cls) || tr.GetObject(id, OpenMode.ForRead) is not LabelBase lb) continue;
            // Profile view labels are attached to a profile; ViewId names the view.
            if (!feature.IsNull && lb.FeatureId != feature && lb.ViewId != feature) continue;
            total++;
            var t = lb.GetType().Name;
            byType[t] = byType.TryGetValue(t, out var n) ? n + 1 : 1;
            if (rows.Count < limit) rows.Add(Describe(lb, tr, false));
        }
        data["filter"] = feature.IsNull ? "all labels in model space" : Catalog.NameOf(feature, tr) ?? feature.Handle.ToString();
        data["count"] = total;
        var bt = new JsonObject();
        foreach (var kv in byType) bt[kv.Key] = kv.Value;
        data["by_type"] = bt;
        data["labels"] = rows;
        data["truncated"] = total > rows.Count;
    }

    // ---- shared verification ------------------------------------------------

    private static void CheckLabel(VerificationSet v, Transaction tr, string what, ObjectId id, ObjectId feature, ObjectId style, JsonArray after)
    {
        var ok = !id.IsNull && !id.IsErased;
        v.Flag(what + " exists", true, ok);
        if (!ok) return;
        var lb = (LabelBase)tr.GetObject(id, OpenMode.ForRead);
        if (!feature.IsNull) v.Flag(what + " attached to the feature", true, lb.FeatureId == feature || lb.ViewId == feature);
        if (!style.IsNull)
        {
            var actual = lb is Label l ? l.StyleId : lb is LabelGroup g ? g.StyleId : ObjectId.Null;
            v.Check(what + " style", Catalog.NameOf(style, tr), Catalog.NameOf(actual, tr), actual == style);
        }
        after.Add(Describe(lb, tr, false));
    }

    private static List<Point2d> Points2d(JsonNode? n) =>
        (n as JsonArray)?.Select(p => { var q = Resolve.P(p); return new Point2d(q.X, q.Y); }).ToList() ?? new List<Point2d>();

    // ---- alignment ----------------------------------------------------------

    private static CommandResult AlignmentStations(CommandContext ctx)
    {
        var inc = Hz.Num(ctx.Args, "increment")!.Value;
        var major = Hz.Bool(ctx.Args, "major") ?? true;
        ObjectId alId = ObjectId.Null, styleId = ObjectId.Null, id = ObjectId.Null;
        var expected = 0;
        return WriteFlow.Run(ctx, "HZ_LABELS",
            (doc, tr, plan) =>
            {
                alId = Resolve.Named(doc, tr, "alignment", Hz.Str(ctx.Args, "alignment")!);
                var al = (Alignment)tr.GetObject(alId, OpenMode.ForRead);
                styleId = Style(doc, tr, major ? "alignment_major_station" : "alignment_minor_station", Hz.Str(ctx.Args, "style"));
                var n = (al.EndingStation - al.StartingStation) / inc;
                if (n > 5000) throw new HzRefusal(ErrorCodes.InvalidInput, "increment gives more than 5000 labels. Nothing changed.");
                for (var k = Math.Ceiling(al.StartingStation / inc - 1e-9); k * inc <= al.EndingStation + 1e-9; k++) expected++;
                // Live finding: Civil 3D also labels the end station when it is not a multiple of the increment.
                if (Math.Abs(al.EndingStation / inc - Math.Round(al.EndingStation / inc)) > 1e-9) expected++;
                plan["alignment"] = al.Name; plan["major"] = major; plan["increment"] = inc; plan["style"] = Catalog.NameOf(styleId, tr);
                plan["expected_labels"] = expected;
            },
#pragma warning disable CS0618 // Create (minor stations) is marked obsolete in favour of CreateMajor, but it is the only minor-station API in 2025.
            (doc, tr) => id = major ? AlignmentStationLabelGroup.CreateMajor(styleId, alId, inc) : AlignmentStationLabelGroup.Create(styleId, alId, inc),
#pragma warning restore CS0618
            (doc, tr, v, after) =>
            {
                var arr = new JsonArray();
                CheckLabel(v, tr, "station label group", id, alId, styleId, arr);
                if (!id.IsNull && !id.IsErased && tr.GetObject(id, OpenMode.ForRead) is LabelGroup lg)
                    v.Check("station labels (multiples of the increment + end station)", expected, (long)lg.SubEntityCount, lg.SubEntityCount == expected);
                after["labels"] = arr;
                after["expected_labels"] = expected;
            });
    }

    private static CommandResult AlignmentGeometry(CommandContext ctx)
    {
        ObjectId alId = ObjectId.Null, lineStyle = ObjectId.Null, curveStyle = ObjectId.Null;
        var created = new List<(ObjectId Id, bool Curve)>();
        int lines = 0, curves = 0, other = 0;
        return WriteFlow.Run(ctx, "HZ_LABELS",
            (doc, tr, plan) =>
            {
                alId = Resolve.Named(doc, tr, "alignment", Hz.Str(ctx.Args, "alignment")!);
                var al = (Alignment)tr.GetObject(alId, OpenMode.ForRead);
                lineStyle = Style(doc, tr, "alignment_line", Hz.Str(ctx.Args, "line_style"));
                curveStyle = Style(doc, tr, "alignment_curve", Hz.Str(ctx.Args, "curve_style"));
                for (var i = 0; i < al.Entities.Count; i++)
                    switch (al.Entities.GetEntityByOrder(i)) { case AlignmentLine: lines++; break; case AlignmentArc: curves++; break; default: other++; break; }
                if (lines + curves == 0) throw new HzRefusal(ErrorCodes.InvalidInput, "Alignment '" + al.Name + "' has no simple tangents or arcs to label. Nothing changed.");
                plan["alignment"] = al.Name; plan["tangent_labels"] = lines; plan["curve_labels"] = curves;
                plan["line_style"] = Catalog.NameOf(lineStyle, tr); plan["curve_style"] = Catalog.NameOf(curveStyle, tr);
                if (other > 0) plan["skipped_entities"] = other + " (spirals and compound groups are not labelled by this action)";
            },
            (doc, tr) =>
            {
                var al = (Alignment)tr.GetObject(alId, OpenMode.ForRead);
                for (var i = 0; i < al.Entities.Count; i++)
                    switch (al.Entities.GetEntityByOrder(i))
                    {
                        case AlignmentLine l: created.Add((AlignmentTangentLabel.Create(l, lineStyle), false)); break;
                        case AlignmentArc a: created.Add((AlignmentCurveLabel.Create(a, curveStyle), true)); break;
                    }
            },
            (doc, tr, v, after) =>
            {
                v.Check("labels created", lines + curves, created.Count, created.Count == lines + curves);
                var arr = new JsonArray();
                for (var i = 0; i < created.Count; i++)
                    CheckLabel(v, tr, (created[i].Curve ? "curve" : "tangent") + " label " + i, created[i].Id, alId, created[i].Curve ? curveStyle : lineStyle, arr);
                after["labels"] = arr;
            });
    }

    // ---- point-anchored feature labels --------------------------------------

    private static CommandResult PointLabels(CommandContext ctx, string featureType, string kind, bool hasMarker,
                                             Func<ObjectId, Point2d, ObjectId, ObjectId, ObjectId> create, Func<LabelBase, Point2d, Point2d> location)
    {
        var pts = Points2d(ctx.Args["points"]);
        ObjectId fid = ObjectId.Null, styleId = ObjectId.Null, markerId = ObjectId.Null;
        var created = new List<ObjectId>();
        return WriteFlow.Run(ctx, "HZ_LABELS",
            (doc, tr, plan) =>
            {
                fid = Resolve.Named(doc, tr, featureType, Hz.Str(ctx.Args, featureType)!);
                styleId = Style(doc, tr, kind, Hz.Str(ctx.Args, "style"));
                if (hasMarker) markerId = Marker(doc, tr, Hz.Str(ctx.Args, "marker"));
                if (featureType == "surface")
                {
                    var s = (Autodesk.Civil.DatabaseServices.Surface)tr.GetObject(fid, OpenMode.ForRead);
                    var outside = new JsonArray();
                    foreach (var p in pts)
                        try { s.FindElevationAtXY(p.X, p.Y); } catch (System.Exception) { outside.Add(Resolve.Json(p)); }
                    if (outside.Count > 0) throw new HzRefusal(ErrorCodes.InvalidInput, outside.Count + " point(s) fall outside the surface; a label there would show no elevation. Nothing changed.", new JsonObject { ["outside"] = outside });
                }
                plan[featureType] = Catalog.NameOf(fid, tr); plan["labels"] = pts.Count; plan["style"] = Catalog.NameOf(styleId, tr);
                if (hasMarker) plan["marker"] = Catalog.NameOf(markerId, tr);
                plan["points"] = new JsonArray(pts.Select(p => (JsonNode)Resolve.Json(p)).ToArray());
            },
            (doc, tr) => { foreach (var p in pts) created.Add(create(fid, p, styleId, markerId)); },
            (doc, tr, v, after) =>
            {
                var arr = new JsonArray();
                for (var i = 0; i < created.Count; i++)
                {
                    CheckLabel(v, tr, "label " + i, created[i], fid, styleId, arr);
                    if (created[i].IsNull || created[i].IsErased) continue;
                    var at = location((LabelBase)tr.GetObject(created[i], OpenMode.ForRead), pts[i]);
                    v.Number("label " + i + " anchor x", pts[i].X, at.X, 1e-6);
                    v.Number("label " + i + " anchor y", pts[i].Y, at.Y, 1e-6);
                }
                after["labels"] = arr;
            });
    }

    private static CommandResult SurfaceSlope(CommandContext ctx)
    {
        var one = Points2d(ctx.Args["points"]);
        var two = (ctx.Args["segments"] as JsonArray)?.Select(s =>
        {
            var a = Resolve.P(s!["from"]); var b = Resolve.P(s["to"]);
            return (A: new Point2d(a.X, a.Y), B: new Point2d(b.X, b.Y));
        }).ToList() ?? new();
        ObjectId fid = ObjectId.Null, styleId = ObjectId.Null;
        var created = new List<ObjectId>();
        return WriteFlow.Run(ctx, "HZ_LABELS",
            (doc, tr, plan) =>
            {
                fid = Resolve.Named(doc, tr, "surface", Hz.Str(ctx.Args, "surface")!);
                styleId = Style(doc, tr, "surface_slope", Hz.Str(ctx.Args, "style"));
                if (two.Any(s => s.A.GetDistanceTo(s.B) < 1e-6)) throw new HzRefusal(ErrorCodes.InvalidInput, "A two-point slope segment has zero length. Nothing changed.");
                plan["surface"] = Catalog.NameOf(fid, tr); plan["one_point"] = one.Count; plan["two_point"] = two.Count; plan["style"] = Catalog.NameOf(styleId, tr);
            },
            (doc, tr) =>
            {
                foreach (var p in one) created.Add(SurfaceSlopeLabel.Create(fid, p, styleId));
                foreach (var s in two) created.Add(SurfaceSlopeLabel.Create(fid, s.A, s.B, styleId));
            },
            (doc, tr, v, after) =>
            {
                var arr = new JsonArray();
                for (var i = 0; i < created.Count; i++)
                {
                    CheckLabel(v, tr, "slope label " + i, created[i], fid, styleId, arr);
                    if (created[i].IsNull || created[i].IsErased) continue;
                    var l = (SurfaceSlopeLabel)tr.GetObject(created[i], OpenMode.ForRead);
                    var want = i < one.Count ? one[i] : two[i - one.Count].A;
                    v.Number("slope label " + i + " location x", want.X, l.Location.X, 1e-6);
                    v.Number("slope label " + i + " location y", want.Y, l.Location.Y, 1e-6);
                    if (i >= one.Count)
                    {
                        var b = two[i - one.Count].B;
                        v.Number("slope label " + i + " second point x", b.X, l.Location2.X, 1e-6);
                        v.Number("slope label " + i + " second point y", b.Y, l.Location2.Y, 1e-6);
                    }
                }
                after["labels"] = arr;
            });
    }

    private static CommandResult ContourLabels(CommandContext ctx)
    {
        var line = Points2d(ctx.Args["line"]);
        ObjectId fid = ObjectId.Null, styleId = ObjectId.Null, id = ObjectId.Null;
        var styled = Hz.Str(ctx.Args, "style") != null;
        return WriteFlow.Run(ctx, "HZ_LABELS",
            (doc, tr, plan) =>
            {
                fid = Resolve.Named(doc, tr, "surface", Hz.Str(ctx.Args, "surface")!);
                if (styled) styleId = Style(doc, tr, "surface_contour", Hz.Str(ctx.Args, "style"));
                plan["surface"] = Catalog.NameOf(fid, tr); plan["label_line_vertices"] = line.Count;
                plan["style"] = styled ? Catalog.NameOf(styleId, tr) : "drawing defaults (major/minor/user contour label styles)";
                plan["note"] = "Contour labels appear only where the surface style shows contours.";
            },
            (doc, tr) =>
            {
                var pc = new Point2dCollection(line.ToArray());
                id = styled ? SurfaceContourLabelGroup.Create(fid, pc, styleId, styleId, styleId) : SurfaceContourLabelGroup.Create(fid, pc);
            },
            (doc, tr, v, after) =>
            {
                var arr = new JsonArray();
                CheckLabel(v, tr, "contour label line", id, fid, ObjectId.Null, arr);
                after["labels"] = arr;
            });
    }

    // ---- profile views ------------------------------------------------------

    private static (ObjectId View, ObjectId Profile) ViewAndProfile(Document doc, Transaction tr, CommandContext ctx)
    {
        var pvId = Resolve.Named(doc, tr, "profile_view", Hz.Str(ctx.Args, "profile_view")!);
        var pv = (ProfileView)tr.GetObject(pvId, OpenMode.ForRead);
        if (Hz.Str(ctx.Args, "profile") is not { } pname) return (pvId, ObjectId.Null);
        var al = (Alignment)tr.GetObject(pv.AlignmentId, OpenMode.ForRead);
        var names = new List<string>();
        foreach (ObjectId pid in al.GetProfileIds())
        {
            var n = Catalog.NameOf(pid, tr) ?? "";
            names.Add(n);
            if (string.Equals(n, pname, StringComparison.OrdinalIgnoreCase)) return (pvId, pid);
        }
        throw new HzRefusal(ErrorCodes.NotFound, "Alignment '" + al.Name + "' of profile view '" + pv.Name + "' has no profile '" + pname + "'. Nothing changed.", new JsonObject { ["candidates"] = Hz.Strings(names) });
    }

    private static CommandResult ProfilePvis(CommandContext ctx)
    {
        ObjectId pvId = ObjectId.Null, prId = ObjectId.Null, styleId = ObjectId.Null, id = ObjectId.Null;
        return WriteFlow.Run(ctx, "HZ_LABELS",
            (doc, tr, plan) =>
            {
                (pvId, prId) = ViewAndProfile(doc, tr, ctx);
                styleId = Style(doc, tr, "profile_grade_break", Hz.Str(ctx.Args, "style"));
                plan["profile_view"] = Catalog.NameOf(pvId, tr); plan["profile"] = Catalog.NameOf(prId, tr); plan["style"] = Catalog.NameOf(styleId, tr);
                plan["pvis"] = ((Profile)tr.GetObject(prId, OpenMode.ForRead)).PVIs.Count;
            },
            (doc, tr) => id = ProfilePVILabelGroup.Create(pvId, prId, styleId),
            (doc, tr, v, after) =>
            {
                var arr = new JsonArray();
                CheckLabel(v, tr, "PVI label group", id, prId, styleId, arr);
                if (!id.IsNull && !id.IsErased) v.Flag("label group drawn in the profile view", true, ((LabelBase)tr.GetObject(id, OpenMode.ForRead)).ViewId == pvId);
                after["labels"] = arr;
            });
    }

    private static CommandResult StationElevation(CommandContext ctx)
    {
        var items = ((JsonArray)ctx.Args["items"]!).Select(n => (St: Hz.Num((JsonObject)n!, "station")!.Value, El: Hz.Num((JsonObject)n!, "elevation")!.Value)).ToList();
        ObjectId pvId = ObjectId.Null, styleId = ObjectId.Null, markerId = ObjectId.Null;
        var created = new List<ObjectId>();
        return WriteFlow.Run(ctx, "HZ_LABELS",
            (doc, tr, plan) =>
            {
                (pvId, _) = ViewAndProfile(doc, tr, ctx);
                var pv = (ProfileView)tr.GetObject(pvId, OpenMode.ForRead);
                var bad = items.Where(i => i.St < pv.StationStart - 1e-6 || i.St > pv.StationEnd + 1e-6 || i.El < pv.ElevationMin - 1e-6 || i.El > pv.ElevationMax + 1e-6).ToList();
                if (bad.Count > 0)
                    throw new HzRefusal(ErrorCodes.InvalidInput, bad.Count + " item(s) fall outside the profile view (stations " + pv.StationStart + "-" + pv.StationEnd +
                        ", elevations " + Math.Round(pv.ElevationMin, 3) + "-" + Math.Round(pv.ElevationMax, 3) + "). Nothing changed.");
                styleId = Style(doc, tr, "profile_view_station_elevation", Hz.Str(ctx.Args, "style"));
                markerId = Marker(doc, tr, Hz.Str(ctx.Args, "marker"));
                plan["profile_view"] = pv.Name; plan["labels"] = items.Count; plan["style"] = Catalog.NameOf(styleId, tr); plan["marker"] = Catalog.NameOf(markerId, tr);
            },
            (doc, tr) => { foreach (var i in items) created.Add(StationElevationLabel.Create(pvId, styleId, markerId, i.St, i.El)); },
            (doc, tr, v, after) =>
            {
                var arr = new JsonArray();
                for (var k = 0; k < created.Count; k++)
                {
                    CheckLabel(v, tr, "label " + k, created[k], pvId, styleId, arr);
                    if (created[k].IsNull || created[k].IsErased) continue;
                    var l = (StationElevationLabel)tr.GetObject(created[k], OpenMode.ForRead);
                    v.Number("label " + k + " station", items[k].St, l.Station, 1e-6);
                    v.Number("label " + k + " elevation", items[k].El, l.Elevation, 1e-6);
                }
                after["labels"] = arr;
            });
    }

    // ---- notes, segments, text, erase ----------------------------------------

    private static CommandResult Note(CommandContext ctx)
    {
        var at = Resolve.P(ctx.Args["location"]);
        var text = Hz.Str(ctx.Args, "text")!;
        ObjectId styleId = ObjectId.Null, markerId = ObjectId.Null, id = ObjectId.Null;
        return WriteFlow.Run(ctx, "HZ_LABELS",
            (doc, tr, plan) =>
            {
                styleId = Style(doc, tr, "general_note", Hz.Str(ctx.Args, "style"));
                markerId = Marker(doc, tr, Hz.Str(ctx.Args, "marker"));
                Resolve.Layer(doc.Database, tr, null);
                plan["location"] = Resolve.Json(at); plan["text"] = text; plan["style"] = Catalog.NameOf(styleId, tr); plan["marker"] = Catalog.NameOf(markerId, tr);
            },
            (doc, tr) =>
            {
                id = NoteLabel.Create(at, styleId, markerId);
                var l = (NoteLabel)tr.GetObject(id, OpenMode.ForWrite);
                var comps = l.GetTextComponentIds();
                if (comps.Count == 0) throw new HzRefusal(ErrorCodes.InvalidInput, "The note label style has no text component to carry the text. Nothing changed.");
                l.SetTextComponentOverride(comps[0], text);
            },
            (doc, tr, v, after) =>
            {
                var arr = new JsonArray();
                CheckLabel(v, tr, "note label", id, ObjectId.Null, styleId, arr);
                if (!id.IsNull && !id.IsErased)
                {
                    var l = (NoteLabel)tr.GetObject(id, OpenMode.ForRead);
                    var comps = l.GetTextComponentIds();
                    v.Text("note text", text, comps.Count > 0 ? l.GetTextComponentOverride(comps[0]) : null, false);
                    v.Number("note anchor x", at.X, l.AnchorInfo.Location.X, 1e-6);
                    v.Number("note anchor y", at.Y, l.AnchorInfo.Location.Y, 1e-6);
                    arr[arr.Count - 1] = Describe(l, tr, true);
                }
                after["labels"] = arr;
            });
    }

    private static CommandResult Segment(CommandContext ctx)
    {
        var ratio = Hz.Num(ctx.Args, "ratio") ?? 0.5;
        ObjectId fid = ObjectId.Null, lineStyle = ObjectId.Null, curveStyle = ObjectId.Null, id = ObjectId.Null;
        return WriteFlow.Run(ctx, "HZ_LABELS",
            (doc, tr, plan) =>
            {
                fid = Catalog.FromHandle(doc.Database, Hz.Str(ctx.Args, "entity")!);
                var e = tr.GetObject(fid, OpenMode.ForRead) as Entity;
                if (e is not (Polyline or Line or Arc or Polyline2d or Polyline3d or Parcel or FeatureLine))
                    throw new HzRefusal(ErrorCodes.InvalidInput, "General segment labels go on polylines, lines, arcs, parcels or feature lines; " + fid.Handle + " is a " + e?.GetType().Name + ". Nothing changed.");
                lineStyle = Style(doc, tr, "general_line", Hz.Str(ctx.Args, "line_style"));
                curveStyle = Style(doc, tr, "general_curve", Hz.Str(ctx.Args, "curve_style"));
                plan["entity"] = new JsonObject { ["handle"] = fid.Handle.ToString(), ["type"] = e.GetType().Name }; plan["ratio"] = ratio;
                plan["line_style"] = Catalog.NameOf(lineStyle, tr); plan["curve_style"] = Catalog.NameOf(curveStyle, tr);
            },
            (doc, tr) => id = GeneralSegmentLabel.Create(fid, ratio, lineStyle, curveStyle),
            (doc, tr, v, after) =>
            {
                var arr = new JsonArray();
                CheckLabel(v, tr, "segment label", id, fid, ObjectId.Null, arr);
                if (!id.IsNull && !id.IsErased)
                {
                    var l = (GeneralSegmentLabel)tr.GetObject(id, OpenMode.ForRead);
                    v.Number("ratio", ratio, l.Ratio, 1e-9);
                    v.Flag("line style", true, l.LineLabelStyleId == lineStyle);
                    v.Flag("curve style", true, l.CurveLabelStyleId == curveStyle);
                }
                after["labels"] = arr;
            });
    }

    private static CommandResult SetText(CommandContext ctx)
    {
        var text = Hz.Str(ctx.Args, "text")!;
        var index = (int)(Hz.Num(ctx.Args, "component") ?? 0);
        ObjectId id = ObjectId.Null, comp = ObjectId.Null;
        return WriteFlow.Run(ctx, "HZ_LABELS",
            (doc, tr, plan) =>
            {
                id = Catalog.FromHandle(doc.Database, Hz.Str(ctx.Args, "handle")!);
                if (tr.GetObject(id, OpenMode.ForRead) is not Label l)
                    throw new HzRefusal(ErrorCodes.InvalidInput, "Handle " + id.Handle + " is not a single Civil 3D label (label groups are edited per sub-label in Civil 3D). Nothing changed.");
                Resolve.Editable(l, tr);
                var comps = l.GetTextComponentIds();
                if (index >= comps.Count) throw new HzRefusal(ErrorCodes.InvalidInput, "The label has " + comps.Count + " text component(s); component " + index + " does not exist. Nothing changed.");
                comp = comps[index];
                string? before = null;
                try { if (l.IsTextComponentOverriden(comp)) before = l.GetTextComponentOverride(comp); } catch (Autodesk.AutoCAD.Runtime.Exception) { }
                plan["label"] = Describe(l, tr, true); plan["component"] = index; plan["override_before"] = before; plan["override_after"] = text;
            },
            (doc, tr) => ((Label)tr.GetObject(id, OpenMode.ForWrite)).SetTextComponentOverride(comp, text),
            (doc, tr, v, after) =>
            {
                var l = (Label)tr.GetObject(id, OpenMode.ForRead);
                v.Text("text override", text, l.GetTextComponentOverride(comp), false);
                after["label"] = Describe(l, tr, true);
            });
    }

    private static CommandResult Erase(CommandContext ctx)
    {
        var handles = Resolve.Strings(ctx.Args["handles"]);
        var ids = new List<ObjectId>();
        return WriteFlow.Run(ctx, "HZ_LABELS",
            (doc, tr, plan) =>
            {
                var rows = new JsonArray();
                foreach (var h in handles)
                {
                    var id = Catalog.FromHandle(doc.Database, h);
                    if (tr.GetObject(id, OpenMode.ForRead) is not LabelBase lb)
                        throw new HzRefusal(ErrorCodes.InvalidInput, "Handle " + h.ToUpperInvariant() + " is not a Civil 3D label; erase only removes labels. Nothing changed.");
                    Resolve.Editable(lb, tr);
                    ids.Add(id);
                    rows.Add(Describe(lb, tr, false));
                }
                plan["erase"] = rows;
            },
            (doc, tr) => { foreach (var id in ids) tr.GetObject(id, OpenMode.ForWrite).Erase(); },
            (doc, tr, v, after) =>
            {
                foreach (var id in ids) v.Flag("label " + id.Handle + " erased", true, id.IsErased);
                after["erased"] = ids.Count(i => i.IsErased);
            });
    }
}
