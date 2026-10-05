// -----------------------------------------------------------------------------
// Horizun Civil 3D MCP - input validation for horizun_c3d_grading and
// horizun_c3d_feature_line (shared by the MCP server and the plug-in).
// -----------------------------------------------------------------------------
using System.Text.Json.Nodes;

namespace Horizun.Civil3D.Core;

public static class GradingInputs
{
    public static readonly string[] Actions = { "create_geometric" };

    public static string? Validate(JsonObject args)
    {
        var action = Hz.Str(args, "action");
        if (action == null || !Actions.Contains(action)) return "action must be create_geometric.";
        var allowed = new[] { "action", "source", "surface", "name", "outer", "inner", "densify", "layer", "style", "volume_against",
                              "target_document", "dry_run", "confirmation_token" };
        if (args.FirstOrDefault(kv => kv.Value != null && !allowed.Contains(kv.Key)) is { Key: { } extra }) return "Field '" + extra + "' is not used by create_geometric.";
        if (string.IsNullOrWhiteSpace(Hz.Str(args, "target_document"))) return "Writes require target_document.";
        if (Hz.Str(args, "source") is not { } src || !IsHex(src)) return "source must be the hexadecimal handle of a closed polyline, 3D polyline or feature line.";
        if (string.IsNullOrWhiteSpace(Hz.Str(args, "name"))) return "name (the new TIN surface) is required; existing surfaces are never overwritten.";
        if (GradingEngine.ValidateSteps(args["outer"], true) is { } o) return o;
        if (GradingEngine.ValidateSteps(args["inner"], false) is { } i) return i;
        var outer = args["outer"] as JsonArray;
        var inner = args["inner"] as JsonArray;
        if ((outer?.Count ?? 0) + (inner?.Count ?? 0) == 0) return "Give at least one outer or inner step.";
        var needsSurface = outer?.Any(s => Hz.Str(s as JsonObject, "type") == "grade_to_surface") == true;
        if (needsSurface && string.IsNullOrWhiteSpace(Hz.Str(args, "surface"))) return "grade_to_surface needs surface (the target terrain name).";
        if (!needsSurface && args["surface"] != null) return "surface is only used by grade_to_surface steps.";
        if (args["densify"] != null && (Hz.Num(args, "densify") is not { } d || !double.IsFinite(d) || d < 0.05 || d > 10)) return "densify must be between 0.05 and 10 drawing units.";
        foreach (var k in new[] { "layer", "style", "volume_against", "surface", "name" })
            if (args[k] != null && string.IsNullOrWhiteSpace(Hz.Str(args, k))) return k + " must be a non-empty string.";
        return null;
    }

    internal static bool IsHex(string h) => h.Length is > 0 and <= 16 && h.All(Uri.IsHexDigit);
}

public static class FeatureLineInputs
{
    public static readonly string[] Actions = { "create_from_polyline", "set_elevations", "rename", "export_polyline3d" };
    public static readonly string[] ElevationModes = { "from_surface", "constant", "points" };
    public const int MaxCreate = 100;

    public static string? Validate(JsonObject args)
    {
        var action = Hz.Str(args, "action");
        if (action == null || !Actions.Contains(action)) return "action must be one of " + string.Join(", ", Actions) + ".";
        var allowed = new HashSet<string> { "action", "target_document", "dry_run", "confirmation_token" };
        switch (action)
        {
            case "create_from_polyline": allowed.UnionWith(new[] { "handles", "names", "site" }); break;
            case "set_elevations": allowed.UnionWith(new[] { "name", "handle", "mode", "surface", "insert_intermediate", "elevation", "points" }); break;
            case "rename": allowed.UnionWith(new[] { "name", "handle", "new_name" }); break;
            case "export_polyline3d": allowed.UnionWith(new[] { "name", "handle", "layer" }); break;
        }
        if (args.FirstOrDefault(kv => kv.Value != null && !allowed.Contains(kv.Key)) is { Key: { } extra }) return "Field '" + extra + "' is not used by action " + action + ".";
        if (string.IsNullOrWhiteSpace(Hz.Str(args, "target_document"))) return "Writes require target_document.";

        if (action == "create_from_polyline")
        {
            if (args["handles"] is not JsonArray hs || hs.Count == 0 || hs.Count > MaxCreate) return "handles must list 1 to " + MaxCreate + " polyline / 3D polyline handles.";
            if (hs.Any(h => h is not JsonValue v || !v.TryGetValue<string>(out var s) || !GradingInputs.IsHex(s))) return "handles must be hexadecimal handles.";
            if (hs.Select(h => h!.GetValue<string>().ToUpperInvariant()).Distinct().Count() != hs.Count) return "handles must not repeat.";
            if (args["names"] is not JsonArray ns || ns.Count != hs.Count) return "names must give one unique feature-line name per handle (same order).";
            if (ns.Any(n => n is not JsonValue v || !v.TryGetValue<string>(out var s) || string.IsNullOrWhiteSpace(s))) return "names must be non-empty strings.";
            if (ns.Select(n => n!.GetValue<string>().ToUpperInvariant()).Distinct().Count() != ns.Count) return "names must not repeat (Civil 3D requires unique feature-line names).";
            if (args["site"] != null && string.IsNullOrWhiteSpace(Hz.Str(args, "site"))) return "site must be an existing site name (omit for siteless).";
            return null;
        }

        var selectors = (string.IsNullOrWhiteSpace(Hz.Str(args, "name")) ? 0 : 1) + (string.IsNullOrWhiteSpace(Hz.Str(args, "handle")) ? 0 : 1);
        if (selectors != 1) return "Select the feature line with exactly one of name or handle.";
        if (Hz.Str(args, "handle") is { } hh && !GradingInputs.IsHex(hh)) return "handle must be hexadecimal.";
        if (action == "rename" && string.IsNullOrWhiteSpace(Hz.Str(args, "new_name"))) return "new_name is required.";
        if (action == "export_polyline3d" && args["layer"] != null && string.IsNullOrWhiteSpace(Hz.Str(args, "layer"))) return "layer must be an existing layer name.";
        if (action == "set_elevations")
        {
            var mode = Hz.Str(args, "mode");
            if (mode == null || !ElevationModes.Contains(mode)) return "mode must be from_surface (surface), constant (elevation) or points ([{index, z}]).";
            if (mode != "from_surface" && (args["surface"] != null || args["insert_intermediate"] != null)) return "surface/insert_intermediate apply to mode=from_surface only.";
            if (mode != "constant" && args["elevation"] != null) return "elevation applies to mode=constant only.";
            if (mode != "points" && args["points"] != null) return "points apply to mode=points only.";
            if (mode == "from_surface" && string.IsNullOrWhiteSpace(Hz.Str(args, "surface"))) return "mode=from_surface needs surface.";
            if (args["insert_intermediate"] != null && Hz.Bool(args, "insert_intermediate") == null) return "insert_intermediate must be true or false.";
            if (mode == "constant" && (Hz.Num(args, "elevation") is not { } e || !double.IsFinite(e))) return "mode=constant needs a finite elevation.";
            if (mode == "points")
            {
                if (args["points"] is not JsonArray ps || ps.Count == 0 || ps.Count > 10000) return "mode=points needs points: 1 to 10000 {index, z}.";
                if (ps.Any(p => p is not JsonObject o || o.Count != 2 || Hz.Int(o, "index") is not { } i || i < 0 || Hz.Num(o, "z") is not { } z || !double.IsFinite(z)))
                    return "Each point is {index (>= 0, position in the feature line's points), z (finite)}.";
                if (ps.Select(p => Hz.Int((JsonObject)p!, "index")).Distinct().Count() != ps.Count) return "point indices must not repeat.";
            }
        }
        return null;
    }
}
