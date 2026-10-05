// -----------------------------------------------------------------------------
// Horizun Civil 3D MCP - Block A (roads): alignment, profile, sections, corridor.
// Action specs for ToolRules; API confirmed in docs/api-probes/2025/AeccDbMgd.phase3-roads*.txt.
// -----------------------------------------------------------------------------
using System.Text.Json.Nodes;

namespace Horizun.Civil3D.Core;

public static class RoadInputs
{
    private const ToolEffect W = ToolEffect.SafeWrite;
    private static readonly string[] Sel = { "name", "handle" };
    private static readonly string[] Create = { "site", "layer", "style", "label_set", "description" };

    private static string? OneTarget(JsonObject a) => V.First(V.Exactly1(a, "name", "handle"), V.Hex(a, "handle"));

    public static readonly IReadOnlyDictionary<string, ActionSpec> Alignment = ToolRules.Register("horizun_c3d_alignment", new Dictionary<string, ActionSpec>
    {
        ["get"] = new(Array.Empty<string>(), Sel, Extra: OneTarget),
        ["station_offset"] = new(Array.Empty<string>(), new[] { "name", "handle", "points", "stations" }, Extra: a => V.First(OneTarget(a),
            V.Exactly1(a, "points", "stations"), V.Points(a, "points", 1, 10000), StationList(a))),
        ["create_from_polyline"] = new(new[] { "polyline", "new_name" }, Create.Concat(new[] { "add_curves", "erase_polyline" }).ToArray(), W,
            a => V.Hex(a, "polyline")),
        ["create_by_pis"] = new(new[] { "pis", "new_name" }, Create.Concat(new[] { "radii" }).ToArray(), W, a => V.First(V.Points(a, "pis", 2, 500), Radii(a))),
        ["create_offset"] = new(new[] { "offset", "new_name" }, new[] { "name", "handle", "style", "start_station", "end_station" }, W,
            a => V.First(OneTarget(a), V.Fin(a, "offset"), Hz.Num(a, "offset") == 0 ? "offset must not be 0." : null, V.Fin(a, "start_station"), V.Fin(a, "end_station"))),
    });

    public static readonly IReadOnlyDictionary<string, ActionSpec> Profile = ToolRules.Register("horizun_c3d_profile", new Dictionary<string, ActionSpec>
    {
        ["get"] = new(Array.Empty<string>(), new[] { "name", "handle", "alignment" }, Extra: OneTarget),
        ["elevation_at"] = new(new[] { "stations" }, new[] { "name", "handle", "alignment" }, Extra: a => V.First(OneTarget(a), NumList(a, "stations", 10000))),
        ["check_k"] = new(Array.Empty<string>(), new[] { "name", "handle", "alignment", "min_k_crest", "min_k_sag" }, Extra: a => V.First(OneTarget(a),
            V.Pos(a, "min_k_crest"), V.Pos(a, "min_k_sag"), a["min_k_crest"] == null && a["min_k_sag"] == null ? "Give min_k_crest and/or min_k_sag (your design criteria)." : null)),
        ["create_from_surface"] = new(new[] { "alignment", "surface", "new_name" }, new[] { "layer", "style", "label_set", "offset" }, W, a => V.Fin(a, "offset")),
        ["create_layout"] = new(new[] { "alignment", "pvis", "new_name" }, new[] { "layer", "style", "label_set" }, W, Pvis),
        ["create_view"] = new(new[] { "alignment", "insert" }, new[] { "new_name", "style", "band_set" }, W, a => V.Point(a, "insert")),
    });

    public static readonly IReadOnlyDictionary<string, ActionSpec> Sections = ToolRules.Register("horizun_c3d_sections", new Dictionary<string, ActionSpec>
    {
        ["list"] = new(new[] { "alignment" }, Array.Empty<string>()),
        ["get_section"] = new(new[] { "group", "station" }, new[] { "alignment" }, Extra: a => V.Fin(a, "station")),
        ["create_sample_lines"] = new(new[] { "alignment", "group", "left_width", "right_width" },
            new[] { "stations", "interval", "start_station", "end_station", "sources" }, W, a => V.First(
                V.Exactly1(a, "stations", "interval"), NumList(a, "stations", 2000), V.Pos(a, "interval"), V.Pos(a, "left_width"), V.Pos(a, "right_width"),
                V.Fin(a, "start_station"), V.Fin(a, "end_station"), V.Strings(a, "sources", 50))),
        ["create_section_views"] = new(new[] { "alignment", "group", "insert" }, Array.Empty<string>(), W, a => V.Point(a, "insert")),
    });

    public static readonly IReadOnlyDictionary<string, ActionSpec> Corridor = ToolRules.Register("horizun_c3d_corridor", new Dictionary<string, ActionSpec>
    {
        ["get"] = new(Array.Empty<string>(), Sel, Extra: OneTarget),
        ["assembly_list"] = new(Array.Empty<string>(), Array.Empty<string>()),
        ["assembly_create"] = new(new[] { "new_name", "insert" }, new[] { "assembly_type" }, W,
            a => V.First(V.Point(a, "insert"), V.OneOf(a, "assembly_type", "UndividedCrownedRoad", "UndividedPlanarRoad", "DividedCrownedRoad", "DividedPlanarRoad", "Railway", "Other"))),
        ["assembly_import"] = new(new[] { "source_dwg", "source_assembly", "new_name", "insert" }, Array.Empty<string>(), W, a => V.Point(a, "insert")),
        ["create"] = new(new[] { "new_name", "alignment", "profile", "assembly" }, new[] { "start_station", "end_station", "frequency", "rebuild" }, W,
            a => V.First(V.Fin(a, "start_station"), V.Fin(a, "end_station"), Frequency(a))),
        ["add_region"] = new(new[] { "assembly", "start_station", "end_station" }, new[] { "name", "handle", "baseline_index", "frequency", "rebuild" }, W,
            a => V.First(OneTarget(a), V.Fin(a, "start_station"), V.Fin(a, "end_station"), Frequency(a),
                Hz.Num(a, "end_station") <= Hz.Num(a, "start_station") ? "end_station must be greater than start_station." : null)),
        ["rebuild"] = new(Array.Empty<string>(), Sel, W, OneTarget),
        ["create_surface"] = new(new[] { "surface_name" }, new[] { "name", "handle", "link_codes", "feature_line_codes", "style", "breaklines" }, W,
            a => V.First(OneTarget(a), V.Strings(a, "link_codes", 100), V.Strings(a, "feature_line_codes", 100),
                a["link_codes"] == null && a["feature_line_codes"] == null ? "Give link_codes and/or feature_line_codes (e.g. [\"Top\",\"Datum\"])." : null)),
    });

    private static string? StationList(JsonObject a)
    {
        if (a["stations"] == null) return null;
        if (a["stations"] is not JsonArray s || s.Count == 0 || s.Count > 10000) return "stations must list 1 to 10000 {station, offset}.";
        return s.Any(n => n is not JsonObject o || Hz.Num(o, "station") is not { } st || !double.IsFinite(st) || (o["offset"] != null && (Hz.Num(o, "offset") is not { } of || !double.IsFinite(of))))
            ? "Each station is {station, offset?} with finite numbers." : null;
    }

    public static string? NumList(JsonObject a, string key, int max)
    {
        if (a[key] == null) return null;
        if (a[key] is not JsonArray s || s.Count == 0 || s.Count > max) return key + " must list 1 to " + max + " numbers.";
        return s.Any(n => Hz.AsDouble(n) is not { } d || !double.IsFinite(d)) ? key + " must contain finite numbers." : null;
    }

    private static string? Radii(JsonObject a)
    {
        if (a["radii"] == null) return null;
        var pis = (a["pis"] as JsonArray)?.Count ?? 0;
        if (a["radii"] is not JsonArray r || r.Count != Math.Max(0, pis - 2)) return "radii must give one radius per interior PI (" + Math.Max(0, pis - 2) + "); 0 = no curve.";
        return r.Any(n => Hz.AsDouble(n) is not { } d || !double.IsFinite(d) || d < 0) ? "radii must be >= 0." : null;
    }

    private static string? Pvis(JsonObject a)
    {
        if (a["pvis"] is not JsonArray p || p.Count < 2 || p.Count > 500) return "pvis must list 2 to 500 {station, elevation, curve_length?}.";
        double prev = double.NegativeInfinity;
        for (var i = 0; i < p.Count; i++)
        {
            if (p[i] is not JsonObject o || o.Any(kv => kv.Key is not ("station" or "elevation" or "curve_length"))) return "pvis[" + i + "] is {station, elevation, curve_length?}.";
            if (Hz.Num(o, "station") is not { } st || !double.IsFinite(st) || Hz.Num(o, "elevation") is not { } el || !double.IsFinite(el)) return "pvis[" + i + "] needs finite station and elevation.";
            if (st <= prev) return "pvis must have strictly increasing stations.";
            prev = st;
            if (o["curve_length"] != null)
            {
                if (i == 0 || i == p.Count - 1) return "The first and last PVI cannot have a vertical curve.";
                if (Hz.Num(o, "curve_length") is not { } cl || !double.IsFinite(cl) || cl <= 0) return "pvis[" + i + "].curve_length must be > 0.";
            }
        }
        return null;
    }

    private static string? Frequency(JsonObject a)
    {
        if (a["frequency"] == null) return null;
        if (a["frequency"] is not JsonObject f || f.Count == 0) return "frequency is {tangents?, curves?, spirals?, profile_curves?} in drawing units.";
        foreach (var (k, _) in f)
        {
            if (k is not ("tangents" or "curves" or "spirals" or "profile_curves")) return "frequency." + k + " is not recognised.";
            if (V.Pos(f, k) is { } e) return "frequency." + e;
        }
        return null;
    }
}
