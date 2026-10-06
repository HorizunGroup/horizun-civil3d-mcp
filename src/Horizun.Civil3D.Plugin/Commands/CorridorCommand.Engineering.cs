// Native corridor API signatures: docs/api-probes/{2024,2025,2026}/AeccDbMgd.corridor-engineering.txt.
using System.Text.Json.Nodes;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.DatabaseServices;
using Horizun.Civil3D.Core;
using Horizun.Civil3D.Plugin.Civil;

namespace Horizun.Civil3D.Plugin.Commands;

internal sealed partial class CorridorCommand
{
    private static int Index(JsonObject a, string key) => (int)(Hz.Num(a, key) ?? 0);
    private static bool? SameSideTarget(SubassemblyTargetInfo t, out string? reason)
    {
#if C3D2024
        reason = "unsupported_year_api"; return null; // No 2024 member.
#else
        if (t.TargetType.ToString() != "Offset") { reason = "unsupported_target_type"; return null; }
        try { reason = null; return t.UseSameSideTarget; }
        catch (InvalidOperationException e) { reason = "native_getter_unavailable: " + e.Message; return null; }
#endif
    }
    private static (string? Value, string? Reason) TargetOption(SubassemblyTargetInfo t, int count)
    {
        var reason = CorridorEngineeringInputs.TargetOptionReadability(t.TargetType.ToString(), count);
        if (reason != null) return (null, reason);
        try { return (t.TargetToOption.ToString(), null); }
        catch (InvalidOperationException e) { return (null, "native_getter_unavailable: " + e.Message); }
    }
    private static BaselineRegion Region(Corridor c, JsonObject a)
    {
        var b = Index(a, "baseline_index"); var r = Index(a, "region_index");
        if (b < 0 || b >= c.Baselines.Count) throw new HzRefusal(ErrorCodes.InvalidInput, "baseline_index is out of range. Nothing changed.");
        if (r < 0 || r >= c.Baselines[b].BaselineRegions.Count) throw new HzRefusal(ErrorCodes.InvalidInput, "region_index is out of range. Nothing changed.");
        return c.Baselines[b].BaselineRegions[r];
    }
    private static JsonArray TargetsJson(BaselineRegion r)
    {
        var result = new JsonArray(); var targets = r.GetTargets();
        for (var i = 0; i < targets.Count; i++)
        {
            var t = targets[i]; var handles = new List<string>();
            foreach (ObjectId id in t.TargetIds) handles.Add(id.Handle.ToString());
            var option = TargetOption(t, handles.Count); var same = SameSideTarget(t, out var sameReason);
            result.Add(new JsonObject { ["target_index"] = i, ["logical_name"] = t.LogicalName, ["display_name"] = t.DisplayName,
                ["subassembly"] = t.SubassemblyName, ["assembly_group"] = t.AssemblyGroupName, ["target_type"] = t.TargetType.ToString(),
                ["handles"] = Hz.Strings(handles), ["target_to_option"] = option.Value,
                ["target_to_option_reason"] = option.Reason, ["use_same_side_target"] = same, ["use_same_side_target_reason"] = sameReason });
        }
        return result;
    }
    private static CommandResult GetTargets(CommandContext ctx) => WriteFlow.Read(ctx, (doc, tr, data) =>
    {
        var c = Resolve.Open<Corridor>(tr, Resolve.Target(doc, tr, "corridor", ctx.Args)); var r = Region(c, ctx.Args);
        data["corridor"] = c.Name; data["region"] = r.Name; data["targets"] = TargetsJson(r);
    });
    private static void RequireTargetType(SubassemblyTargetInfo target, Autodesk.AutoCAD.DatabaseServices.DBObject obj)
    {
        var valid = target.TargetType.ToString() switch
        {
            "Surface" => obj is Autodesk.Civil.DatabaseServices.Surface,
            "Alignment" => obj is Alignment,
            "Profile" => obj is Profile,
            "Offset" => obj is Alignment || obj is FeatureLine || obj is Polyline || obj is Polyline2d || obj is Polyline3d,
            "Elevation" => obj is Profile || obj is FeatureLine || obj is Polyline3d,
            _ => false,
        };
        if (!valid) throw new HzRefusal(ErrorCodes.Unsupported, "Target '" + target.DisplayName + "' of type " + target.TargetType + " does not support the supplied " + obj.GetType().Name + " in this bridge. Nothing changed.");
    }
    private static CommandResult SetTargets(CommandContext ctx)
    {
        var id = ObjectId.Null; JsonArray expected = new();
        var updates = new List<(int Index, ObjectId[] Ids, string? Option, bool? SameSide)>();
        return WriteFlow.Run(ctx, "HZ_CORRIDOR_TARGETS", (doc, tr, plan) =>
        {
            id = Resolve.Target(doc, tr, "corridor", ctx.Args); var c = Resolve.Open<Corridor>(tr, id); Resolve.Editable(c, tr);
            var r = Region(c, ctx.Args); var current = r.GetTargets(); expected = TargetsJson(r); updates.Clear();
            foreach (var node in (JsonArray)ctx.Args["targets"]!)
            {
                var row = node!.AsObject();
                var i = Index(row, "target_index"); if (i >= current.Count) throw new HzRefusal(ErrorCodes.InvalidInput, "target_index " + i + " is out of range. Nothing changed.");
                var ids = new List<ObjectId>();
                foreach (var h in Resolve.Strings(row["handles"]))
                { var tid = Catalog.FromHandle(doc.Database, h); RequireTargetType(current[i], tr.GetObject(tid, OpenMode.ForRead)); ids.Add(tid); }
                var option = Hz.Str(row, "target_to_option"); var same = Hz.Bool(row, "use_same_side_target");
                var targetType = current[i].TargetType.ToString();
                if (CorridorEngineeringInputs.TargetOptionRefusal(targetType, ids.Count, option) is { } refusal) throw new HzRefusal(ErrorCodes.Unsupported, "Cannot set target_to_option: " + refusal + ". Nothing changed.");
                var optionReason = CorridorEngineeringInputs.TargetOptionReadability(targetType, ids.Count);
                if (optionReason == null && option == null && expected[i]!["target_to_option"] == null) throw new HzRefusal(ErrorCodes.InvalidInput, "Specify target_to_option when assigning multiple handles to a target whose previous option was not readable. Nothing changed.");
                if (same.HasValue && expected[i]!["use_same_side_target_reason"] is { } sameReason) throw new HzRefusal(ErrorCodes.Unsupported, "Cannot set use_same_side_target: " + sameReason.ToJsonString(Hz.Compact) + ". Nothing changed.");
#if C3D2024
                if(same.HasValue) throw new HzRefusal(ErrorCodes.Unsupported,"Civil 3D 2024 has no UseSameSideTarget API. Omit use_same_side_target; nothing changed.");
#endif
                updates.Add((i, ids.ToArray(), option, same));
                var item = (JsonObject)expected[i]!; item["handles"] = Hz.Strings(ids.Select(x => x.Handle.ToString()));
                if (optionReason != null) { item["target_to_option"] = null; item["target_to_option_reason"] = optionReason; }
                else { if (option != null) item["target_to_option"] = option; item["target_to_option_reason"] = null; }
                if (same != null) item["use_same_side_target"] = same.Value;
            }
            plan["corridor"] = c.Name; plan["region_guid"] = r.RegionGUID.ToString(); plan["before_targets"] = TargetsJson(r); plan["after_targets"] = expected.DeepClone();
            plan["rebuild"] = Hz.Bool(ctx.Args, "rebuild") ?? true;
        }, (doc, tr) =>
        {
            var c = Resolve.Open<Corridor>(tr, id, OpenMode.ForWrite); var r = Region(c, ctx.Args); var targets = r.GetTargets();
            foreach (var update in updates)
            {
                var t = targets[update.Index]; t.TargetIds = new ObjectIdCollection(update.Ids);
                if (update.Option != null) t.TargetToOption = (SubassemblyTargetToOption)Enum.Parse(typeof(SubassemblyTargetToOption), update.Option);
#if !C3D2024
                if (update.SameSide != null) t.UseSameSideTarget = update.SameSide.Value;
#endif
            }
            r.SetTargets(targets); if (Hz.Bool(ctx.Args, "rebuild") ?? true) c.Rebuild();
        }, (doc, tr, v, after) =>
        {
            var c = Resolve.Open<Corridor>(tr, id); var actual = TargetsJson(Region(c, ctx.Args));
            v.Text("all target definitions", expected.ToJsonString(Hz.Compact), actual.ToJsonString(Hz.Compact));
            if (Hz.Bool(ctx.Args, "rebuild") ?? true) v.Flag("rebuilt", true, !c.IsOutOfDate);
            after["targets"] = actual;
        });
    }
    private static CommandResult AppliedGeometry(CommandContext ctx, bool quantities) => WriteFlow.Read(ctx, (doc, tr, data) =>
    {
        var c = Resolve.Open<Corridor>(tr, Resolve.Target(doc, tr, "corridor", ctx.Args)); var r = Region(c, ctx.Args);
        if (c.IsOutOfDate || r.AppliedAssemblies.Count == 0) throw new HzRefusal(ErrorCodes.InvalidInput, "Corridor geometry is stale or has no applied assemblies; rebuild first. No quantity is reported.");
        var codes = (ctx.Args["shape_codes"] == null ? (IEnumerable<string>)c.GetShapeCodes() : Resolve.Strings(ctx.Args["shape_codes"])).ToArray();
        foreach (var code in codes) if (!c.GetShapeCodes().Contains(code)) throw new HzRefusal(ErrorCodes.NotFound, "Unknown shape code '" + code + "'.");
        var lo = Hz.Num(ctx.Args, "start_station") ?? r.StartStation; var hi = Hz.Num(ctx.Args, "end_station") ?? r.EndStation;
        if (lo < r.StartStation || hi > r.EndStation || hi <= lo) throw new HzRefusal(ErrorCodes.InvalidInput, "Requested stations must lie within the region.");
        var stations = r.AppliedAssemblies.Stations().Where(s => s >= lo && s <= hi).OrderBy(s => s).ToArray();
        if (stations.Length > (Hz.Num(ctx.Args, "max_stations") ?? 2000)) throw new HzRefusal(ErrorCodes.InvalidInput, "Applied geometry exceeds max_stations; request a smaller station range.");
        var rows = new JsonArray(); var byCode = codes.ToDictionary(code => code, _ => new List<double?>()); var vertexCount = 0;
        foreach (var station in stations)
        {
            var aa = r.AppliedAssemblies.GetItemAt(station); var shapes = new JsonArray();
            foreach (var code in codes)
            {
                double area = 0; var count = 0;
                foreach (CalculatedShape shape in aa.GetShapesByCode(code))
                {
                    if (!Hz.IsFinite(shape.Area) || shape.Area < 0) throw new HzRefusal(ErrorCodes.InvalidInput, "A calculated shape has invalid area; no estimate reported.");
                    area += shape.Area; count++;
                    if (!Hz.IsFinite(area)) throw new HzRefusal(ErrorCodes.InvalidInput, "Summed shape area overflows.");
                    if (!quantities)
                    {
                        var links = new JsonArray();
                        foreach (CalculatedLink link in shape.CalculatedLinks)
                        { var points = new JsonArray(); foreach (CalculatedPoint p in link.CalculatedPoints) { if (++vertexCount > 100000) throw new HzRefusal(ErrorCodes.InvalidInput, "Applied geometry exceeds 100000 vertices; request a smaller station range."); points.Add(new JsonObject { ["xyz"] = Resolve.Json(p.XYZ, false), ["station_offset_elevation"] = Resolve.Json(p.StationOffsetElevationToBaseline, false) }); } links.Add(new JsonObject { ["codes"] = Hz.Strings(link.CorridorCodes), ["points"] = points }); }
                        shapes.Add(new JsonObject { ["code"] = code, ["area"] = shape.Area, ["links"] = links });
                    }
                }
                // No shape at a station is missing geometry, not an invented zero area.
                byCode[code].Add(count == 0 ? null : area);
            }
            var areas = new JsonObject(); foreach (var code in codes) areas[code] = byCode[code].Last() is { } a ? JsonValue.Create(a) : null;
            rows.Add(new JsonObject { ["station"] = station, ["areas_by_shape_code"] = areas, ["shapes"] = quantities ? null : shapes });
        }
        data["corridor"] = c.Name; data["region"] = r.Name; data["region_guid"] = r.RegionGUID.ToString(); data["stations"] = rows;
        data["requested_start"] = lo; data["requested_end"] = hi; data["boundary_coverage_complete"] = stations.Length >= 2 && Math.Abs(stations[0] - lo) < 1e-6 && Math.Abs(stations[stations.Length - 1] - hi) < 1e-6;
        if (!quantities) return;
        var reports = new JsonArray();
        foreach (var code in codes)
        {
            var report = CorridorEngineeringInputs.Integrate(stations, byCode[code]); report["shape_code"] = code;
            report["material"] = (ctx.Args["material_map"] as JsonObject)?[code]?.DeepClone();
            if (Hz.Bool(data, "boundary_coverage_complete") != true) { report["complete"] = false; report["estimated_volume"] = null; report["reason"] = "Requested range boundaries have no applied stations; covered sample estimate is partial."; }
            reports.Add(report);
        }
        data["quantities"] = reports; data["quantity_kind"] = "estimated_shape_volume_in_drawing_units_cubed";
        data["note"] = "Average end area on rebuilt applied shapes; not Civil 3D native QTO/material-list quantities. Shape codes may overlap; do not sum codes without confirming disjoint materials.";
    });

    private static JsonObject SettingsJson(BaselineRegion r)
    {
        var s = r.AppliedAssemblySetting;
        return new JsonObject { ["tangents"] = s.FrequencyAlongTangents, ["curves"] = s.FrequencyAlongCurves, ["spirals"] = s.FrequencyAlongSpirals, ["profile_curves"] = s.FrequencyAlongProfileCurves,
            ["target_curves"] = s.FrequencyAlongTargetCurves, ["mod_curves"] = s.MODAlongCurves, ["mod_target_curves"] = s.MODAlongTargetCurves, ["curve_option"] = s.CorridorAlongCurvesOption.ToString(), ["target_curve_option"] = s.TargetCurveOption.ToString(),
            ["adjacent_offset"] = s.AppliedAdjacentToOffsetTargetStartEnd, ["horizontal_geometry"] = s.AppliedAtHorizontalGeometryPoints, ["offset_geometry"] = s.AppliedAtOffsetTargetGeometryPoints,
            ["profile_geometry"] = s.AppliedAtProfileGeometryPoints, ["profile_high_low"] = s.AppliedAtProfileHighLowPoints, ["superelevation"] = s.AppliedAtSuperelevationCriticalPoints };
    }
    private static JsonArray AdditionalJson(BaselineRegion r)
    {
        var a = new JsonArray(); foreach (var x in r.AppliedAssemblySetting.AdditionalAppliedAssemblies.OrderBy(x => x.Station)) a.Add(new JsonObject { ["station"] = x.Station, ["description"] = x.Description }); return a;
    }
    private static JsonObject Definition(BaselineRegion r) => new() { ["assembly"] = r.AssemblyId.Handle.ToString(), ["settings"] = SettingsJson(r), ["targets"] = TargetsJson(r) };
    private static void RestructureAllowed(BaselineRegion r)
    {
        if (r.OffsetBaselines.Count != 0 || r.GetOverriddenStations().Length != 0) throw new HzRefusal(ErrorCodes.Unsupported, "Region '" + r.Name + "' contains offset baselines or overridden stations. Their full definitions cannot be preserved by this bridge; nothing changed.");
        var stationValues = r.AdditionalStations().OrderBy(x => x).ToArray();
        var descriptors = r.AppliedAssemblySetting.AdditionalAppliedAssemblies.OrderBy(x => x.Station).ToArray();
        if (stationValues.Length != descriptors.Length || stationValues.Where((s, i) => Math.Abs(s - descriptors[i].Station) > 1e-6).Any()) throw new HzRefusal(ErrorCodes.Unsupported, "Additional station descriptions cannot be reconciled with the region station list. Nothing changed.");
    }
    private static CommandResult RestructureRegion(CommandContext ctx, bool merge)
    {
        var id = ObjectId.Null; JsonObject definition = new(); JsonArray additional = new(); var start = 0d; var end = 0d; var beforeCount = 0;
        var ri = Index(ctx.Args, "region_index"); var bi = Index(ctx.Args, "baseline_index"); var last = Index(ctx.Args, "last_region_index");
        var split = Hz.Num(ctx.Args, "split_station") ?? 0; var newName = Hz.Str(ctx.Args, "new_region_name") ?? ""; var retainedName = "";
        return WriteFlow.Run(ctx, "HZ_CORRIDOR_REGIONS", (doc, tr, plan) =>
        {
            id = Resolve.Target(doc, tr, "corridor", ctx.Args); var c = Resolve.Open<Corridor>(tr, id); Resolve.Editable(c, tr); var r = Region(c, ctx.Args);
            RestructureAllowed(r); definition = Definition(r); additional = AdditionalJson(r); start = r.StartStation; end = r.EndStation; retainedName = r.Name;
            var regions = c.Baselines[bi].BaselineRegions; beforeCount = regions.Count;
            if (!merge)
            {
                if (split <= start + 1e-6 || split >= end - 1e-6) throw new HzRefusal(ErrorCodes.InvalidInput, "split_station must lie strictly inside the region.");
                // Both new regions include the split station, so an additional station there would be duplicated.
                if (additional.OfType<JsonObject>().Any(x => Hz.Num(x, "station") is { } s && Math.Abs(s - split) <= 1e-6))
                    throw new HzRefusal(ErrorCodes.InvalidInput, "split_station " + split + " coincides with an additional applied station; both new regions would get it. Split at another station. Nothing changed.");
                foreach (BaselineRegion other in regions) if (string.Equals(other.Name, newName, StringComparison.OrdinalIgnoreCase)) throw new HzRefusal(ErrorCodes.InvalidInput, "new_region_name already exists.");
            }
            else
            {
                if (last <= ri || last >= regions.Count) throw new HzRefusal(ErrorCodes.InvalidInput, "last_region_index must be after region_index within the same baseline.");
                for (var i = ri + 1; i <= last; i++)
                {
                    var next = regions[i]; RestructureAllowed(next);
                    if (Math.Abs(next.StartStation - end) > 1e-6 || Definition(next).ToJsonString(Hz.Compact) != definition.ToJsonString(Hz.Compact)) throw new HzRefusal(ErrorCodes.Unsupported, "Only adjacent regions with identical assembly, every frequency flag and target definition can be merged. Nothing changed.");
                    foreach (var node in AdditionalJson(next)) additional.Add(node!.DeepClone()); end = next.EndStation;
                }
                // Boundary duplicates may carry conflicting descriptions; never discard them silently.
                if (additional.OfType<JsonObject>().GroupBy(x => Hz.Num(x, "station")).Any(g => g.Count() > 1)) throw new HzRefusal(ErrorCodes.Unsupported, "Regions have duplicate additional stations. Resolve their definitions before merging.");
            }
            plan["corridor"] = c.Name; plan["before_definition"] = definition.DeepClone(); plan["additional_stations"] = additional.DeepClone(); plan["start_station"] = start; plan["end_station"] = end;
            plan["region_guid"] = r.RegionGUID.ToString(); plan["retained_region_name"] = retainedName; plan["baseline_index"] = bi; plan["region_index"] = ri;
            plan["new_region_name"] = merge ? null : newName; plan["last_region_index"] = merge ? JsonValue.Create(last) : null;
            plan["split_station"] = merge ? null : JsonValue.Create(split); plan["rebuild"] = Hz.Bool(ctx.Args, "rebuild") ?? true;
        }, (doc, tr) =>
        {
            var c = Resolve.Open<Corridor>(tr, id, OpenMode.ForWrite); var regions = c.Baselines[bi].BaselineRegions; var r = regions[ri];
            if (merge) r.Merge(r, regions[last]); else { var right = r.Split(split); right.Name = newName; }
            // Native operations must conserve all publicly readable definitions. Abort before commit otherwise.
            var expectedCount = beforeCount + (merge ? -(last - ri) : 1);
            if (regions.Count != expectedCount) throw new HzRefusal(ErrorCodes.TransactionFailed, "Native region restructuring did not produce the expected region count; transaction aborted.");
            var affected = new List<BaselineRegion> { regions[ri] }; if (!merge) affected.Add(regions[ri + 1]);
            for (var i = 0; i < affected.Count; i++)
            {
                var region = affected[i];
                if (Math.Abs(region.StartStation - (i == 0 ? start : split)) > 1e-6 || Math.Abs(region.EndStation - (!merge && i == 0 ? split : end)) > 1e-6 || region.Name != (i == 0 ? retainedName : newName)) throw new HzRefusal(ErrorCodes.TransactionFailed, "Native region restructuring changed station limits or names unexpectedly; transaction aborted.");
                var extras = additional.OfType<JsonObject>().Where(x => Hz.Num(x, "station") >= region.StartStation && Hz.Num(x, "station") <= region.EndStation).ToArray();
                region.AppliedAssemblySetting.AdditionalAppliedAssemblies = extras.Select(x => new AdditionalAppliedAssemblyInfo(Hz.Num(x, "station")!.Value, Hz.Str(x, "description") ?? "")).ToList();
                if (Definition(region).ToJsonString(Hz.Compact) != definition.ToJsonString(Hz.Compact)) throw new HzRefusal(ErrorCodes.TransactionFailed, "Native region restructuring changed a frequency or target definition; transaction aborted.");
            }
            if (Hz.Bool(ctx.Args, "rebuild") ?? true) c.Rebuild();
        }, (doc, tr, v, after) =>
        {
            var c = Resolve.Open<Corridor>(tr, id); var regions = c.Baselines[bi].BaselineRegions;
            v.Check("region count", beforeCount + (merge ? -(last - ri) : 1), regions.Count, regions.Count == beforeCount + (merge ? -(last - ri) : 1));
            v.Text("retained region name", retainedName, regions[ri].Name);
            var affected = new List<BaselineRegion> { regions[ri] }; if (!merge) affected.Add(regions[ri + 1]);
            for (var i = 0; i < affected.Count; i++)
            {
                var r = affected[i]; RestructureAllowed(r); v.Text("complete definition " + i, definition.ToJsonString(Hz.Compact), Definition(r).ToJsonString(Hz.Compact));
                v.Number("start " + i, i == 0 ? start : split, r.StartStation, 1e-6); v.Number("end " + i, !merge && i == 0 ? split : end, r.EndStation, 1e-6);
                var expectedExtras = new JsonArray(); foreach (var node in additional.OfType<JsonObject>().Where(x => Hz.Num(x, "station") >= r.StartStation && Hz.Num(x, "station") <= r.EndStation).OrderBy(x => Hz.Num(x, "station"))) expectedExtras.Add(node.DeepClone());
                v.Text("additional station definitions " + i, expectedExtras.ToJsonString(Hz.Compact), AdditionalJson(r).ToJsonString(Hz.Compact));
            }
            if (!merge) v.Text("new region name", newName, affected[1].Name);
            if (Hz.Bool(ctx.Args, "rebuild") ?? true) v.Flag("rebuilt", true, !c.IsOutOfDate);
            after["corridor"] = Describe(c, tr);
        });
    }
}
