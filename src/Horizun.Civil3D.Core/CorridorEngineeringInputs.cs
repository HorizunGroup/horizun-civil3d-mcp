using System.Text.Json.Nodes;

namespace Horizun.Civil3D.Core;

public static class CorridorEngineeringInputs
{
    // Deliberately bounded bridge support; other native option families are unverified.
    public static string? TargetOptionReadability(string targetType, int targetCount) =>
        targetType is not ("Offset" or "Elevation") ? "unsupported_target_type" : targetCount < 2 ? "insufficient_target_count" : null;
    public static string? TargetOptionRefusal(string targetType, int targetCount, string? option) =>
        option == null ? null : TargetOptionReadability(targetType, targetCount) ??
        (option is "Nearest" or "Farthest" || targetType == "Elevation" && option is "Flattest" or "Steepest" ? null : "unsupported_option_for_target_type");
    private static string? S(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
    public static string? Validate(string action, JsonObject a)
    {
        if ((a["name"] != null) == (a["handle"] != null)) return "Give exactly one corridor name or handle.";
        foreach (var k in new[] { "baseline_index", "region_index", "last_region_index", "max_stations" })
            if (a[k] != null && (Hz.Num(a, k) is not { } n || !Hz.IsFinite(n) || n != Math.Truncate(n) || n < (k == "max_stations" ? 1 : 0) || n > (k == "max_stations" ? 2000 : 100000))) return k + " must be a bounded integer.";
        foreach (var k in new[] { "start_station", "end_station", "split_station" })
            if (a[k] != null && (Hz.Num(a, k) is not { } n || !Hz.IsFinite(n))) return k + " must be finite.";
        if (Hz.Num(a, "start_station") is { } s && Hz.Num(a, "end_station") is { } e && e <= s) return "end_station must exceed start_station.";
        if (a["shape_codes"] != null && (a["shape_codes"] is not JsonArray codes || codes.Count == 0 || codes.Count > 100 || codes.Any(n => S(n) is not { } c || string.IsNullOrWhiteSpace(c)) || codes.Select(S).Distinct(StringComparer.Ordinal).Count() != codes.Count)) return "shape_codes must list 1 to 100 distinct nonempty codes.";
        if (a["material_map"] != null && (a["material_map"] is not JsonObject map || map.Count > 100 || map.Any(k => string.IsNullOrWhiteSpace(k.Key) || S(k.Value) is not { } m || string.IsNullOrWhiteSpace(m)))) return "material_map maps shape codes to nonempty material names.";
        if (action == "region_quantities" && a["shape_codes"] == null) return "shape_codes is required for estimated shape quantities.";
        if (action == "split_region" && (Hz.Num(a, "split_station") == null || string.IsNullOrWhiteSpace(Hz.Str(a, "new_region_name")))) return "split_station and new_region_name are required.";
        if (action == "merge_regions" && Hz.Num(a, "last_region_index") == null) return "last_region_index is required.";
        if (action != "set_targets") return null;
        if (a["targets"] is not JsonArray rows || rows.Count == 0 || rows.Count > 1000) return "targets must contain 1 to 1000 target updates.";
        var seen = new HashSet<int>();
        foreach (var row in rows)
        {
            if (row is not JsonObject o || o.Any(k => k.Key is not ("target_index" or "handles" or "target_to_option" or "use_same_side_target"))) return "Each target contains target_index, handles and optional targeting options.";
            if (Hz.Num(o, "target_index") is not { } i || !Hz.IsFinite(i) || i < 0 || i > 100000 || i != Math.Truncate(i) || !seen.Add((int)i)) return "Target indices must be distinct nonnegative integers.";
            if (o["handles"] is not JsonArray handles || handles.Count > 1000 || handles.Any(n => S(n) is not { } h || h.Length == 0 || h.Length > 16 || h.Any(c => !Uri.IsHexDigit(c))) || handles.Select(S).Distinct(StringComparer.OrdinalIgnoreCase).Count() != handles.Count) return "handles must contain distinct hexadecimal object handles; [] explicitly clears a target.";
            if (o["target_to_option"] != null && Hz.Str(o, "target_to_option") is not ("Farthest" or "Flattest" or "Nearest" or "Steepest")) return "target_to_option is Farthest, Flattest, Nearest or Steepest.";
            if (o["target_to_option"] != null && handles.Count < 2) return "target_to_option requires at least two target handles.";
            if (o["use_same_side_target"] != null && Hz.Bool(o, "use_same_side_target") == null) return "use_same_side_target must be boolean.";
        }
        return null;
    }

    // Average-end-area estimate. Missing stations are never silently connected.
    public static JsonObject Integrate(IReadOnlyList<double> stations, IReadOnlyList<double?> areas)
    {
        if (stations.Count != areas.Count) throw new ArgumentException("Station/area counts differ.");
        double volume = 0, covered = 0, missing = 0;
        for (var i = 0; i < stations.Count; i++)
        {
            if (!Hz.IsFinite(stations[i]) || (i > 0 && stations[i] <= stations[i - 1]) || (areas[i] is { } area && (!Hz.IsFinite(area) || area < 0))) throw new ArgumentException("Stations must increase and areas must be finite and nonnegative.");
            if (i == 0) continue;
            var d = stations[i] - stations[i - 1];
            if (!Hz.IsFinite(d)) throw new ArgumentException("Station interval overflows.");
            if (areas[i - 1] is { } a && areas[i] is { } b) { volume += d * (a / 2 + b / 2); covered += d; }
            else missing += d;
            if (!Hz.IsFinite(volume) || !Hz.IsFinite(covered) || !Hz.IsFinite(missing)) throw new ArgumentException("Quantity or coverage overflows.");
        }
        var complete = stations.Count >= 2 && missing == 0;
        return new JsonObject { ["method"] = "average_end_area_estimate", ["estimated_volume"] = complete && Hz.IsFinite(volume) ? JsonValue.Create(volume) : null,
            ["covered_volume_estimate"] = covered > 0 && Hz.IsFinite(volume) ? JsonValue.Create(volume) : null, ["covered_length"] = covered, ["missing_length"] = missing,
            ["complete"] = complete, ["reason"] = complete ? null : "Fewer than two stations or missing shape geometry; no full-region quantity is reported." };
    }
}
