// -----------------------------------------------------------------------------
// horizun_c3d_surface apply_elevation_analysis / apply_slope_analysis / style_display.
// API confirmed in docs/api-probes/2025/AeccDbMgd.phase1-analysis.txt.
//
// Verification: the stored bands are RE-READ (Get*Data) and compared one by one
// (count, min, max, colour); per-band areas (and volumes for volume surfaces)
// are reported as grid estimates, with a reconciliation against the native
// volume. style_display re-reads every property it set.
//
// Slope unit: the API stores slope analysis values as decimal ratios (0.05 = 5 %)
// - evidence: GetTerrainProperties().MeanGradeOrSlope = 0.2525 on a hilly
// production terrain. Callers always speak PERCENT; the conversion is here and
// the fixture plane (exactly 2.236 %) checks it live.
// -----------------------------------------------------------------------------
using System.Text.Json.Nodes;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.DatabaseServices.Styles;
using Horizun.Civil3D.Core;
using Horizun.Civil3D.Plugin.Civil;
using Surface = Autodesk.Civil.DatabaseServices.Surface;
using Color = Autodesk.AutoCAD.Colors.Color;

namespace Horizun.Civil3D.Plugin.Commands;

internal sealed partial class SurfaceCommand
{
    private const double SlopeApiFactor = 100.0; // API ratio -> percent

    private static Color ToAcad(HzColor c) => c.IsAci ? Color.FromColorIndex(ColorMethod.ByAci, c.Aci) : Color.FromRgb(c.R, c.G, c.B);

    private static HzColor FromAcad(Color c) => c.ColorMethod == ColorMethod.ByColor
        ? HzColor.FromRgb(c.Red, c.Green, c.Blue)
        : HzColor.FromAci(c.ColorIndex);

    private static List<AnalysisRange> CurrentBands(Surface s, bool slope) => slope
        ? s.Analysis.GetSlopeData().Select(d => new AnalysisRange(d.MinimumSlope * SlopeApiFactor, d.MaximumSlope * SlopeApiFactor, FromAcad(d.Scheme))).ToList()
        : s.Analysis.GetElevationData().Select(d => new AnalysisRange(d.MinimumElevation, d.MaximumElevation, FromAcad(d.Scheme))).ToList();

    private static JsonArray BandsJson(IEnumerable<AnalysisRange> bands) =>
        Hz.Arr(bands.Select(b => (JsonNode?)new JsonObject { ["min"] = Hz.Finite(b.Min, 9), ["max"] = Hz.Finite(b.Max, 9), ["color"] = b.Color.ToJson() }));

    private static List<AnalysisRange> PlanBands(CommandContext ctx, Surface s, bool slope, List<AnalysisRange> current)
    {
        var mode = Hz.Str(ctx.Args, "mode")!;
        List<(double Min, double Max)> spans;
        if (mode == "ranges")
            return ((JsonArray)ctx.Args["ranges"]!).Cast<JsonObject>()
                .Select(r => new AnalysisRange(Hz.Num(r, "min")!.Value, Hz.Num(r, "max")!.Value, HzColor.Parse(r["color"])!.Value)).ToList();
        if (mode == "recolor")
        {
            if (current.Count == 0)
                throw new HzRefusal(ErrorCodes.InvalidInput, "'" + s.Name + "' has no current " + (slope ? "slope" : "elevation") + " bands to recolour. Use equal, step or ranges. Nothing changed.");
            spans = current.Select(b => (b.Min, b.Max)).ToList();
        }
        else
        {
            double min, max;
            if (slope)
            {
                var t = ((TinSurface)s).GetTerrainProperties();
                min = t.MinimumGradeOrSlope * SlopeApiFactor;
                max = t.MaximumGradeOrSlope * SlopeApiFactor;
            }
            else
            {
                var g = s.GetGeneralProperties();
                min = g.MinimumElevation;
                max = g.MaximumElevation;
            }
            if (Math.Abs(max - min) < 1e-9) max = min + 1e-6; // a perfectly flat/uniform surface still gets one valid band
            spans = mode == "equal"
                ? SurfaceAnalysisMath.Equal(min, max, Hz.Int(ctx.Args, "number_of_ranges")!.Value)
                : SurfaceAnalysisMath.Step(min, max, Hz.Num(ctx.Args, "interval")!.Value, Hz.Num(ctx.Args, "break_at") ?? 0);
        }
        List<HzColor> colors;
        if (ctx.Args["colors"] is JsonArray cs)
        {
            if (cs.Count != spans.Count)
                throw new HzRefusal(ErrorCodes.InvalidInput, "'" + s.Name + "' gets " + spans.Count + " bands but " + cs.Count + " colors were given. Nothing changed.",
                    new JsonObject { ["bands"] = Hz.Arr(spans.Select(x => (JsonNode?)new JsonObject { ["min"] = Hz.Finite(x.Min, 9), ["max"] = Hz.Finite(x.Max, 9) })) });
            colors = cs.Select(c => HzColor.Parse(c)!.Value).ToList();
        }
        else colors = SurfaceAnalysisMath.Palette(Hz.Str(ctx.Args, "color_scheme") ?? "rainbow", spans, Hz.Num(ctx.Args, "break_at") ?? 0);
        return spans.Select((x, i) => new AnalysisRange(x.Min, x.Max, colors[i])).ToList();
    }

    private static CommandResult Analysis(CommandContext ctx)
    {
        if (ctx.Action == "style_display") return StyleDisplay(ctx);
        var slope = ctx.Action == "apply_slope_analysis";
        var doc = ctx.Document(true);
        if (doc.IsReadOnly) throw new HzRefusal(ErrorCodes.ReadOnlyDocument, "The drawing is read-only. Nothing changed.");
        using var operationLock = doc.LockDocument(DocumentLockMode.Write, "HZ_SURFACE", "HZ_SURFACE", false);
        var data = Data(ctx, doc);
        var plan = new JsonObject { ["action"] = ctx.Action, ["drawing_revision"] = DrawingRevision.Capture(doc) };
        if (slope) plan["slope_unit"] = "percent";
        var targets = new List<(ObjectId Id, List<AnalysisRange> Bands)>();
        var component = slope ? SurfaceDisplayStyleType.Slopes : SurfaceDisplayStyleType.Elevations;

        ctx.Read(doc, tr =>
        {
            var rows = new JsonArray();
            foreach (var id in Resolve(ctx, doc, tr))
            {
                var s = Open(tr, id);
                Editable(s, tr);
                if (slope && s is not TinSurface)
                    throw new HzRefusal(ErrorCodes.InvalidInput, "'" + s.Name + "' is not a TIN elevation surface; slope analysis applies to TIN surfaces. Nothing changed.");
                var current = CurrentBands(s, slope);
                var bands = PlanBands(ctx, s, slope, current);
                targets.Add((id, bands));
                var style = (SurfaceStyle)tr.GetObject(s.StyleId, OpenMode.ForRead);
                var shows = style.GetDisplayStylePlan(component).Visible;
                var row = new JsonObject
                {
                    ["name"] = s.Name, ["handle"] = s.Handle.ToString(), ["surface_kind"] = Hz.Str(Describe(s, tr, false), "surface_kind"),
                    ["current_bands"] = BandsJson(current), ["new_bands"] = BandsJson(bands),
                    ["style"] = style.Name, ["style_displays_analysis_in_plan"] = shows,
                };
                if (!shows)
                    row["warning"] = "Style '" + style.Name + "' does not display " + component + " in plan view: the bands are stored but not visible. " +
                                     "Use style_display on a style that only this surface uses (duplicate_style first if shared), or set_style.";
                rows.Add(row);
            }
            plan["surfaces"] = rows;
            return 0;
        });

        if (ctx.DryRun) return CommandResult.Ok(ctx.Rehearse(doc, data, plan));
        ctx.RequireConfirmation(doc, plan);

        ctx.Write(doc, "HZ_SURFACE", tr =>
        {
            foreach (var (id, bands) in targets)
            {
                var s = Open(tr, id, OpenMode.ForWrite);
                if (slope)
                    s.Analysis.SetSlopeData(bands.Select(b => new SurfaceAnalysisSlopeData(b.Min / SlopeApiFactor, b.Max / SlopeApiFactor, ToAcad(b.Color))).ToArray());
                else
                    s.Analysis.SetElevationData(bands.Select(b => new SurfaceAnalysisElevationData(b.Min, b.Max, ToAcad(b.Color))).ToArray());
            }
            return 0;
        });

        var checks = new VerificationSet();
        var actual = new JsonArray();
        try
        {
            ctx.Verify(doc, tr =>
            {
                var budget = Math.Max(1, (Hz.Int(ctx.Args, "max_samples") ?? 10000) / Math.Max(1, targets.Count));
                foreach (var (id, bands) in targets)
                {
                    var s = Open(tr, id);
                    var stored = CurrentBands(s, slope);
                    var same = stored.Count == bands.Count && stored.Zip(bands).All(p =>
                        Math.Abs(p.First.Min - p.Second.Min) <= 1e-6 * Math.Max(1, Math.Abs(p.Second.Min)) &&
                        Math.Abs(p.First.Max - p.Second.Max) <= 1e-6 * Math.Max(1, Math.Abs(p.Second.Max)) &&
                        p.First.Color == p.Second.Color);
                    checks.Check(s.Name + " stored bands (count, min, max, colour)", BandsJson(bands), BandsJson(stored), same,
                        "The bands Civil 3D holds after commit differ from the requested ones.");
                    var row = new JsonObject { ["name"] = s.Name, ["handle"] = s.Handle.ToString(), ["stored_bands"] = BandsJson(stored) };
                    try { row["band_statistics"] = BandStatistics(ctx, s, tr, bands, slope, budget); }
                    catch (Exception e) { row["band_statistics"] = null; row["band_statistics_unreadable_reason"] = e.GetType().Name + ": " + e.Message; }
                    actual.Add(row);
                }
                return 0;
            });
        }
        catch (Exception e) { checks.Check("post-commit re-read", true, null, false, e.GetType().Name + ": " + e.Message); }

        data["dry_run"] = false;
        data["committed"] = true;
        data["plan"] = plan;
        data["actual"] = actual;
        data["verified"] = checks.ToJson();
        data["undo"] = new JsonObject { ["label"] = "HZ_SURFACE", ["instruction"] = "One UNDO in Civil 3D reverts this analysis change; no drawing was saved." };
        return checks.AllVerified ? CommandResult.Ok(data)
            : CommandResult.Fail(ErrorCodes.VerificationFailed, "The analysis committed but the re-read did not match every requested band. Inspect actual/verified.", data);
    }

    private static JsonObject BandStatistics(CommandContext ctx, Surface s, Transaction tr, List<AnalysisRange> bands, bool slope, int budget)
    {
        var g = s.GetGeneralProperties();
        var (cells, spacing) = SurfaceMath.Grid(g.MinimumCoordinateX, g.MinimumCoordinateY, g.MaximumCoordinateX, g.MaximumCoordinateY,
            Hz.Num(ctx.Args, "grid_spacing"), budget);
        var samples = new List<(double, double)>();
        int outside = 0, unreadable = 0;
        foreach (var c in cells)
        {
            try
            {
                var v = slope ? s.FindSlopeAtXY(c.X, c.Y) * SlopeApiFactor : s.FindElevationAtXY(c.X, c.Y);
                if (double.IsFinite(v)) samples.Add((v, c.Area)); else unreadable++;
            }
            catch (PointNotOnEntityException) { outside++; }
            catch (Exception) { unreadable++; }
        }
        var isVolume = s is TinVolumeSurface or GridVolumeSurface;
        var stats = SurfaceAnalysisMath.Bands(bands, samples, withVolume: !slope && isVolume, slope ? "percent" : "drawing units");
        stats["grid_spacing"] = spacing;
        stats["outside_samples"] = outside;
        stats["unreadable_samples"] = unreadable;
        if (!slope && isVolume)
        {
            try
            {
                var native = Volume(s, tr, new JsonObject());
                if (Hz.Num(native, "unadjusted_fill") is { } f && Hz.Num(native, "unadjusted_cut") is { } cu)
                {
                    var banded = ((JsonArray)stats["bands"]!).Sum(b => Hz.Num(b as JsonObject, "volume_estimated") ?? 0);
                    stats["reconciliation"] = Reconcile.Compare("net volume (fill - cut) inside the bands", f - cu, "civil3d", banded, "bands_estimate", 0.5);
                    stats["reconciliation_note"] = "Agreement needs the bands to cover the whole depth range; area outside all bands is reported above.";
                }
            }
            catch (Exception e) { stats["reconciliation_unreadable_reason"] = e.GetType().Name + ": " + e.Message; }
        }
        return stats;
    }

    private static CommandResult StyleDisplay(CommandContext ctx)
    {
        var doc = ctx.Document(true);
        if (doc.IsReadOnly) throw new HzRefusal(ErrorCodes.ReadOnlyDocument, "The drawing is read-only. Nothing changed.");
        using var operationLock = doc.LockDocument(DocumentLockMode.Write, "HZ_SURFACE", "HZ_SURFACE", false);
        var data = Data(ctx, doc);
        var plan = new JsonObject { ["action"] = ctx.Action, ["drawing_revision"] = DrawingRevision.Capture(doc) };
        var view = Hz.Str(ctx.Args, "view") ?? "plan";
        var views = view == "both" ? new[] { "plan", "model" } : new[] { view };
        var comps = (JsonObject)ctx.Args["components"]!;
        ObjectId styleId = ObjectId.Null;

        ctx.Read(doc, tr =>
        {
            styleId = Style(doc, tr, Hz.Str(ctx.Args, "style")!);
            var style = (SurfaceStyle)tr.GetObject(styleId, OpenMode.ForRead);
            var usage = StylesCommand.Usage("surface", doc.Database, CommandContext.Civil(doc), tr, out _)!;
            var users = usage.TryGetValue(styleId, out var l) ? l : new List<JsonObject>();
            plan["style"] = new JsonObject { ["name"] = style.Name, ["handle"] = style.Handle.ToString() };
            plan["style_used_by"] = Hz.Arr(users.Select(u => (JsonNode?)u.DeepClone()));
            if (users.Count > 1 && Hz.Bool(ctx.Args, "allow_shared_style") != true)
                throw new HzRefusal(ErrorCodes.InvalidInput,
                    "Style '" + style.Name + "' is used by " + users.Count + " surfaces: editing it changes ALL of them. Duplicate it (duplicate_style) and " +
                    "assign the copy (set_style) to change only some, or pass allow_shared_style=true to change all. Nothing changed.",
                    new JsonObject { ["style_used_by"] = Hz.Arr(users.Select(u => (JsonNode?)u.DeepClone())) });
            var layers = (LayerTable)tr.GetObject(doc.Database.LayerTableId, OpenMode.ForRead);
            var changes = new JsonArray();
            foreach (var (key, value) in comps)
            {
                var c = (JsonObject)value!;
                if (Hz.Str(c, "layer") is { } layer && !layers.Has(layer))
                    throw new HzRefusal(ErrorCodes.NotFound, "Layer '" + layer + "' does not exist. Nothing changed.");
                var type = Enum.Parse<SurfaceDisplayStyleType>(SurfaceInputs.DisplayComponents[key]);
                foreach (var v in views)
                {
                    var d = v == "plan" ? style.GetDisplayStylePlan(type) : style.GetDisplayStyleModel(type);
                    changes.Add(new JsonObject
                    {
                        ["component"] = key, ["view"] = v,
                        ["before"] = new JsonObject { ["visible"] = d.Visible, ["color"] = FromAcad(d.Color).ToJson(), ["layer"] = d.Layer },
                        ["requested"] = c.DeepClone(),
                    });
                }
            }
            plan["changes"] = changes;
            if (users.Count > 1) plan["shared_style_acknowledged"] = true;
            return 0;
        });

        if (ctx.DryRun) return CommandResult.Ok(ctx.Rehearse(doc, data, plan));
        ctx.RequireConfirmation(doc, plan);

        ctx.Write(doc, "HZ_SURFACE", tr =>
        {
            var style = (SurfaceStyle)tr.GetObject(styleId, OpenMode.ForWrite);
            foreach (var (key, value) in comps)
            {
                var c = (JsonObject)value!;
                var type = Enum.Parse<SurfaceDisplayStyleType>(SurfaceInputs.DisplayComponents[key]);
                foreach (var v in views)
                {
                    var d = v == "plan" ? style.GetDisplayStylePlan(type) : style.GetDisplayStyleModel(type);
                    if (Hz.Bool(c, "visible") is { } vis) d.Visible = vis;
                    if (c["color"] != null) d.Color = ToAcad(HzColor.Parse(c["color"])!.Value);
                    if (Hz.Str(c, "layer") is { } layer) d.Layer = layer;
                }
            }
            return 0;
        });

        var checks = new VerificationSet();
        var after = new JsonArray();
        try
        {
            ctx.Verify(doc, tr =>
            {
                var style = (SurfaceStyle)tr.GetObject(styleId, OpenMode.ForRead);
                foreach (var (key, value) in comps)
                {
                    var c = (JsonObject)value!;
                    var type = Enum.Parse<SurfaceDisplayStyleType>(SurfaceInputs.DisplayComponents[key]);
                    foreach (var v in views)
                    {
                        var d = v == "plan" ? style.GetDisplayStylePlan(type) : style.GetDisplayStyleModel(type);
                        if (Hz.Bool(c, "visible") is { } vis) checks.Flag(key + " " + v + " visible", vis, d.Visible);
                        if (c["color"] != null)
                        {
                            var want = HzColor.Parse(c["color"])!.Value;
                            var got = FromAcad(d.Color);
                            checks.Check(key + " " + v + " color", want.ToJson(), got.ToJson(), want == got);
                        }
                        if (Hz.Str(c, "layer") is { } layer) checks.Text(key + " " + v + " layer", layer, d.Layer);
                        after.Add(new JsonObject { ["component"] = key, ["view"] = v, ["visible"] = d.Visible, ["color"] = FromAcad(d.Color).ToJson(), ["layer"] = d.Layer });
                    }
                }
                return 0;
            });
        }
        catch (Exception e) { checks.Check("post-commit re-read", true, null, false, e.GetType().Name + ": " + e.Message); }

        data["dry_run"] = false;
        data["committed"] = true;
        data["plan"] = plan;
        data["after"] = after;
        data["verified"] = checks.ToJson();
        data["undo"] = new JsonObject { ["label"] = "HZ_SURFACE", ["instruction"] = "One UNDO in Civil 3D reverts this style change; no drawing was saved." };
        return checks.AllVerified ? CommandResult.Ok(data)
            : CommandResult.Fail(ErrorCodes.VerificationFailed, "The style change committed but the re-read did not match every requested property. Inspect after/verified.", data);
    }
}
