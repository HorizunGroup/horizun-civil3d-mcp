using System.Text.Json.Nodes;

namespace Horizun.Civil3D.Core;

/// <summary>Action-specific validation shared by the MCP server and Civil host.</summary>
public static class SurfaceInputs
{
    public static readonly string[] ReadActions = { "list", "get", "volumes_report", "sample_elevation" };
    public static readonly string[] WriteActions = { "rename", "set_style", "duplicate_style", "create_tin", "create_volume", "rebuild", "add_data", "paste",
                                                     "apply_elevation_analysis", "apply_slope_analysis", "style_display" };
    public static readonly string[] AnalysisActions = { "apply_elevation_analysis", "apply_slope_analysis" };
    /// <summary>style_display component names -> SurfaceDisplayStyleType member names (2025 enum).</summary>
    public static readonly IReadOnlyDictionary<string, string> DisplayComponents = new Dictionary<string, string>
    {
        ["points"] = "Points", ["triangles"] = "Triangles", ["border"] = "Boundary", ["major_contour"] = "MajorContour",
        ["minor_contour"] = "MinorContour", ["user_contours"] = "UserContours", ["gridded"] = "Gridded",
        ["directions"] = "Directions", ["elevations"] = "Elevations", ["slopes"] = "Slopes",
        ["slope_arrows"] = "SlopeArrows", ["watersheds"] = "Watersheds",
    };
    public const int MaxPoints = 10000;
    public const int MaxGridSamples = 100000;
    public const int MaxEntities = 500;
    public const int MaxPasteSources = 50;
    public static readonly string[] BreaklineKinds = { "standard", "proximity", "non_destructive" };
    public static readonly string[] BoundaryKinds = { "outer", "hide", "show", "data_clip" };

    public static string? Validate(JsonObject args)
    {
        var action = Hz.Str(args, "action");
        if (action == null || !ReadActions.Concat(WriteActions).Contains(action)) return "Unknown surface action.";
        foreach (var key in new[] { "action", "name", "handle", "target_document", "new_name", "style", "layer", "description", "base", "comparison", "confirmation_token" })
            if (args[key] != null && (args[key] is not JsonValue v || !v.TryGetValue<string>(out _))) return key+" must be a string.";
        var allowed = new HashSet<string> { "action", "target_document" };
        if (WriteActions.Contains(action)) allowed.UnionWith(new[] { "dry_run", "confirmation_token" });
        if (action is "list" or "get" or "volumes_report" or "sample_elevation" or "rename" or "set_style" or "rebuild") allowed.UnionWith(new[] { "name", "names", "handle" });
        if (action is "add_data" or "paste") allowed.UnionWith(new[] { "name", "handle", "rebuild" });
        if (action == "add_data") allowed.UnionWith(new[] { "vertices", "breaklines", "boundaries" });
        if (action == "paste") allowed.Add("sources");
        if (AnalysisActions.Contains(action))
            allowed.UnionWith(new[] { "name", "names", "handle", "mode", "number_of_ranges", "interval", "break_at", "ranges", "colors", "color_scheme", "grid_spacing", "max_samples" });
        if (action == "style_display") allowed.UnionWith(new[] { "style", "view", "components", "allow_shared_style" });
        if (action is "list" or "get") allowed.UnionWith(new[] { "include_isopaca_statistics", "grid_spacing", "max_samples" });
        if (action == "list") allowed.UnionWith(new[] { "style", "layer", "offset", "limit" });
        if (action is "rename" or "duplicate_style" or "create_tin" or "create_volume") allowed.Add("new_name");
        if (action is "set_style" or "duplicate_style" or "create_tin" or "create_volume") allowed.Add("style");
        if (action is "create_tin" or "create_volume") allowed.UnionWith(new[] { "description", "layer" });
        if (action is "volumes_report" or "create_volume") allowed.UnionWith(new[] { "base", "comparison" });
        if (action == "volumes_report") allowed.UnionWith(new[] { "cut_factor", "fill_factor" });
        if (action == "sample_elevation") allowed.UnionWith(new[] { "points", "line", "step" });
        args = ToolRules.WithoutUnusedDefaults("horizun_c3d_surface", args, allowed);
        if (args.Any(kv => kv.Value != null && !allowed.Contains(kv.Key))) return "Field '"+args.First(kv => kv.Value != null && !allowed.Contains(kv.Key)).Key+"' is not used by action "+action+".";
        bool Has(string key) => !string.IsNullOrWhiteSpace(Hz.Str(args, key));
        var names = args["names"] as JsonArray;
        if (names != null && (names.Count == 0 || names.Count > 100 || names.Any(n => n is not JsonValue v || !v.TryGetValue<string>(out var s) || string.IsNullOrWhiteSpace(s))))
            return "names must contain between 1 and 100 non-empty exact surface names.";
        if (names != null && names.Select(n => n!.GetValue<string>()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Count)
            return "names must not contain duplicates.";
        var selectors = (Has("name") ? 1 : 0) + (Has("handle") ? 1 : 0) + (names != null ? 1 : 0);
        if (action == "list" && (Has("handle") || names != null)) return "list accepts a name wildcard; handle/names are for exact selection.";
        if (AnalysisActions.Contains(action))
        {
            if (selectors != 1) return "Select surfaces with exactly one of name, handle or names.";
            if (ValidateAnalysis(args, action) is { } bad) return bad;
        }
        if (action == "style_display" && ValidateStyleDisplay(args) is { } badDisplay) return badDisplay;
        if (action is "add_data" or "paste")
        {
            if (selectors != 1 || names != null) return action + " targets exactly one TIN surface: give name or handle.";
            if (args["rebuild"] != null && Hz.Bool(args, "rebuild") == null) return "rebuild must be true or false.";
            if ((action == "add_data" ? ValidateAddData(args) : ValidatePaste(args)) is { } bad) return bad;
        }
        if (action is "get" or "sample_elevation" or "rename" or "set_style" or "rebuild")
        {
            if (selectors != 1) return "Select surfaces with exactly one of name, handle or names.";
            if (action == "rename" && names?.Count > 1) return "rename selects exactly one surface.";
        }
        if (action == "volumes_report")
        {
            var pair = Has("base") || Has("comparison");
            if (pair ? (!Has("base") || !Has("comparison") || selectors != 0) : selectors != 1)
                return "volumes_report needs an exact surface selector OR both base and comparison names.";
        }
        if (WriteActions.Contains(action) && !Has("target_document")) return "Writes require target_document.";
        if (action is "rename" or "duplicate_style" or "create_tin" or "create_volume")
            if (!Has("new_name")) return "new_name is required for this action.";
        if (action is "set_style" or "duplicate_style" or "create_tin" or "create_volume")
            if (!Has("style")) return "style is required for this action (exact surface-style name).";
        if (action is "create_tin" or "create_volume" or "duplicate_style")
            if (selectors != 0) return "This creation action does not accept existing surface selectors.";
        if (action == "create_volume" && (!Has("base") || !Has("comparison"))) return "create_volume requires base and comparison names.";
        if (Has("base") && Has("comparison") && string.Equals(Hz.Str(args,"base"),Hz.Str(args,"comparison"),StringComparison.OrdinalIgnoreCase))
            return "base and comparison must be distinct surfaces.";
        foreach (var key in new[] { "grid_spacing", "step" })
            if (args[key] != null && (Hz.Num(args,key) is not { } v || !double.IsFinite(v) || v <= 0)) return key + " must be finite and > 0.";
        foreach (var key in new[] { "cut_factor", "fill_factor" })
            if (args[key] != null && (Hz.Num(args,key) is not { } v || !double.IsFinite(v) || v <= 0)) return key + " must be finite and > 0.";
        if (args["max_samples"] != null && (Hz.Int(args,"max_samples") is not { } count || count < 1 || count > MaxGridSamples))
            return "max_samples must be between 1 and 100000.";
        if (action == "sample_elevation")
        {
            var points = args["points"] as JsonArray;
            var line = args["line"] as JsonObject;
            if ((points == null) == (line == null)) return "sample_elevation requires points OR line.";
            if (points != null)
            {
                if (points.Count == 0 || points.Count > MaxPoints) return "points must contain between 1 and 10000 XY objects.";
                if (points.Any(p => !ValidPoint(p as JsonObject))) return "Each point requires finite x and y drawing coordinates.";
                if (points.Count*(names?.Count ?? 1)>MaxGridSamples) return "Batch point sampling exceeds 100000 evaluations.";
            }
            if (line != null)
            {
                if (!ValidPoint(line["start"] as JsonObject) || !ValidPoint(line["end"] as JsonObject)) return "line requires finite start/end XY objects.";
                if (Hz.Num(args,"step") is not { } step || !double.IsFinite(step) || step <= 0) return "line sampling requires step > 0.";
                try { if(SurfaceMath.AlongLine(line, step).Count*(names?.Count ?? 1)>MaxGridSamples) return "Batch line sampling exceeds 100000 evaluations."; } catch (HzRefusal e) { return e.Message; }
            }
        }
        return null;
    }

    private static string? ValidateAddData(JsonObject args)
    {
        var vertices = args["vertices"] as JsonArray;
        var breaklines = args["breaklines"] as JsonObject;
        var boundaries = args["boundaries"] as JsonObject;
        if (args["vertices"] != null && vertices == null) return "vertices must be an array of {x,y,z}.";
        if (args["breaklines"] != null && breaklines == null) return "breaklines must be an object.";
        if (args["boundaries"] != null && boundaries == null) return "boundaries must be an object.";
        if (vertices == null && breaklines == null && boundaries == null)
            return "add_data needs at least one of vertices, breaklines or boundaries.";
        if (vertices != null)
        {
            if (vertices.Count == 0 || vertices.Count > MaxPoints) return "vertices must contain between 1 and 10000 points.";
            if (vertices.Any(p => !ValidPoint3(p as JsonObject))) return "Each vertex requires finite x, y and z drawing coordinates (and nothing else).";
        }
        if (breaklines != null)
        {
            var allowedKeys = new[] { "handles", "kind", "description", "mid_ordinate", "max_distance", "weeding_distance", "weeding_angle" };
            if (breaklines.FirstOrDefault(kv => !allowedKeys.Contains(kv.Key)) is { Key: { } extra }) return "breaklines." + extra + " is not a recognised field.";
            if (Handles(breaklines, "breaklines") is { } bad) return bad;
            var kind = Hz.Str(breaklines, "kind") ?? "standard";
            if (!BreaklineKinds.Contains(kind)) return "breaklines.kind must be one of " + string.Join(", ", BreaklineKinds) + ".";
            if (breaklines["description"] != null && Hz.Str(breaklines, "description") == null) return "breaklines.description must be a string.";
            if (NonNegative(breaklines, "mid_ordinate", strict: true) is { } m) return m;
            foreach (var k in new[] { "max_distance", "weeding_distance", "weeding_angle" })
            {
                if (NonNegative(breaklines, k, strict: false) is { } e) return e;
                if (kind != "standard" && breaklines[k] != null) return "breaklines." + k + " applies to kind=standard only.";
            }
        }
        if (boundaries != null)
        {
            var allowedKeys = new[] { "handles", "kind", "name", "non_destructive", "mid_ordinate" };
            if (boundaries.FirstOrDefault(kv => !allowedKeys.Contains(kv.Key)) is { Key: { } extra }) return "boundaries." + extra + " is not a recognised field.";
            if (Handles(boundaries, "boundaries") is { } bad) return bad;
            var kind = Hz.Str(boundaries, "kind");
            if (kind == null || !BoundaryKinds.Contains(kind)) return "boundaries.kind is required: one of " + string.Join(", ", BoundaryKinds) + ".";
            if (boundaries["name"] != null && Hz.Str(boundaries, "name") == null) return "boundaries.name must be a string.";
            if (boundaries["non_destructive"] != null && Hz.Bool(boundaries, "non_destructive") == null) return "boundaries.non_destructive must be true or false.";
            if (NonNegative(boundaries, "mid_ordinate", strict: true) is { } m) return m;
        }
        return null;
    }

    private static string? ValidateAnalysis(JsonObject args, string action)
    {
        var mode = Hz.Str(args, "mode");
        if (mode == null || !SurfaceAnalysisMath.Modes.Contains(mode))
            return "mode is required: equal (number_of_ranges), step (interval, break_at), ranges (explicit list) or recolor (colors only).";
        var unit = action == "apply_slope_analysis" ? "percent" : "drawing units";
        bool Present(string k) => args[k] != null;
        string? Only(params string[] keys)
        {
            foreach (var k in new[] { "number_of_ranges", "interval", "break_at", "ranges" })
                if (Present(k) && !keys.Contains(k)) return k + " does not apply to mode=" + mode + ".";
            return null;
        }
        switch (mode)
        {
            case "equal":
                if (Only("number_of_ranges") is { } e1) return e1;
                if (Hz.Int(args, "number_of_ranges") is not { } n || n < 1 || n > SurfaceAnalysisMath.MaxRanges)
                    return "mode=equal needs number_of_ranges between 1 and " + SurfaceAnalysisMath.MaxRanges + ".";
                break;
            case "step":
                if (Only("interval", "break_at") is { } e2) return e2;
                if (Hz.Num(args, "interval") is not { } iv || !double.IsFinite(iv) || iv <= 0) return "mode=step needs interval > 0 (" + unit + ").";
                if (Present("break_at") && (Hz.Num(args, "break_at") is not { } b || !double.IsFinite(b))) return "break_at must be a finite number.";
                break;
            case "ranges":
                if (Only("ranges") is { } e3) return e3;
                if (args["ranges"] is not JsonArray rs || rs.Count == 0 || rs.Count > SurfaceAnalysisMath.MaxRanges)
                    return "mode=ranges needs ranges: 1 to " + SurfaceAnalysisMath.MaxRanges + " objects {min, max, color} in " + unit + ".";
                double? prev = null;
                foreach (var node in rs)
                {
                    if (node is not JsonObject r || r.Any(kv => kv.Key is not ("min" or "max" or "color"))) return "Each range is {min, max, color} and nothing else.";
                    if (Hz.Num(r, "min") is not { } lo || Hz.Num(r, "max") is not { } hi || !double.IsFinite(lo) || !double.IsFinite(hi) || hi <= lo)
                        return "Each range needs finite min < max.";
                    if (HzColor.Parse(r["color"]) == null) return "Each range needs a color: ACI 1-255 or \"#RRGGBB\".";
                    if (prev is { } p && lo < p - 1e-9) return "ranges must be ascending and must not overlap.";
                    prev = hi;
                }
                if (Present("colors") || Present("color_scheme")) return "mode=ranges takes each colour inside its range; colors/color_scheme do not apply.";
                break;
            case "recolor":
                if (Only() is { } e4) return e4;
                if (!Present("colors") && !Present("color_scheme")) return "mode=recolor needs colors or color_scheme.";
                break;
        }
        if (Present("colors"))
        {
            if (args["colors"] is not JsonArray cs || cs.Count == 0 || cs.Count > SurfaceAnalysisMath.MaxRanges) return "colors must list 1 to 64 colours.";
            if (cs.Any(c => HzColor.Parse(c) == null)) return "colors accept ACI 1-255 or \"#RRGGBB\".";
            if (Present("color_scheme")) return "Give colors OR color_scheme, not both.";
            if (mode == "equal" && Hz.Int(args, "number_of_ranges") is { } n2 && cs.Count != n2) return "colors must have exactly number_of_ranges entries.";
        }
        if (Present("color_scheme") && (Hz.Str(args, "color_scheme") is not { } sch || !SurfaceAnalysisMath.Schemes.Contains(sch)))
            return "color_scheme must be one of " + string.Join(", ", SurfaceAnalysisMath.Schemes) + ".";
        if (args["grid_spacing"] != null && (Hz.Num(args, "grid_spacing") is not { } g || !double.IsFinite(g) || g <= 0)) return "grid_spacing must be finite and > 0.";
        if (args["max_samples"] != null && (Hz.Int(args, "max_samples") is not { } m || m < 1 || m > MaxGridSamples)) return "max_samples must be between 1 and 100000.";
        return null;
    }

    private static string? ValidateStyleDisplay(JsonObject args)
    {
        if (string.IsNullOrWhiteSpace(Hz.Str(args, "style"))) return "style_display needs style (exact surface-style name).";
        var view = Hz.Str(args, "view") ?? "plan";
        if (args["view"] != null && view is not ("plan" or "model" or "both")) return "view must be plan, model or both.";
        if (args["allow_shared_style"] != null && Hz.Bool(args, "allow_shared_style") == null) return "allow_shared_style must be true or false.";
        if (args["components"] is not JsonObject comps || comps.Count == 0)
            return "components is required: e.g. {\"slopes\":{\"visible\":true},\"border\":{\"visible\":true,\"color\":\"#FF0000\"}}.";
        foreach (var (key, value) in comps)
        {
            if (!DisplayComponents.ContainsKey(key)) return "Unknown component '" + key + "'. Use: " + string.Join(", ", DisplayComponents.Keys) + ".";
            if (value is not JsonObject c || c.Count == 0) return "components." + key + " must set at least one of visible, color, layer.";
            foreach (var (prop, v) in c)
            {
                if (prop == "visible" && Hz.Bool(c, "visible") == null) return "components." + key + ".visible must be true or false.";
                else if (prop == "color" && HzColor.Parse(v) == null) return "components." + key + ".color must be ACI 1-255 or \"#RRGGBB\".";
                else if (prop == "layer" && string.IsNullOrWhiteSpace(Hz.Str(c, "layer"))) return "components." + key + ".layer must be a layer name.";
                else if (prop is not ("visible" or "color" or "layer")) return "components." + key + "." + prop + " is not recognised (visible, color, layer).";
            }
        }
        return null;
    }

    private static string? ValidatePaste(JsonObject args)
    {
        if (args["sources"] is not JsonArray sources || sources.Count == 0 || sources.Count > MaxPasteSources)
            return "paste needs sources: 1 to 50 exact surface names, pasted in the given order.";
        if (sources.Any(n => n is not JsonValue v || !v.TryGetValue<string>(out var t) || string.IsNullOrWhiteSpace(t)))
            return "sources must be non-empty surface names.";
        var list = sources.Select(n => n!.GetValue<string>()).ToList();
        if (list.Distinct(StringComparer.OrdinalIgnoreCase).Count() != list.Count) return "sources must not contain duplicates.";
        if (Hz.Str(args, "name") is { } target && list.Contains(target, StringComparer.OrdinalIgnoreCase)) return "A surface cannot be pasted into itself.";
        return null;
    }

    private static string? Handles(JsonObject o, string what)
    {
        if (o["handles"] is not JsonArray a || a.Count == 0 || a.Count > MaxEntities) return what + ".handles must list 1 to 500 drawing-object handles.";
        if (a.Any(n => n is not JsonValue v || !v.TryGetValue<string>(out var h) || !IsHex(h))) return what + ".handles must be hexadecimal handles.";
        if (a.Select(n => n!.GetValue<string>().ToUpperInvariant()).Distinct().Count() != a.Count) return what + ".handles must not repeat.";
        return null;
    }

    private static bool IsHex(string h) => h.Length is > 0 and <= 16 && h.All(Uri.IsHexDigit);

    private static string? NonNegative(JsonObject o, string key, bool strict)
    {
        if (o[key] == null) return null;
        if (Hz.Num(o, key) is not { } v || !double.IsFinite(v) || (strict ? v <= 0 : v < 0))
            return key + (strict ? " must be finite and > 0." : " must be finite and >= 0.");
        return null;
    }

    private static bool ValidPoint3(JsonObject? p) =>
        p != null && p.Count == 3 && ValidPoint(p) && Hz.Num(p, "z") is { } z && double.IsFinite(z);

    private static bool ValidPoint(JsonObject? p) => p != null && Hz.Num(p,"x") is { } x && double.IsFinite(x) && Hz.Num(p,"y") is { } y && double.IsFinite(y);
}
