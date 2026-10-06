// -----------------------------------------------------------------------------
// Horizun Civil 3D MCP - Block B (Civil labels): horizun_c3d_labels.
// API confirmed in docs/api-probes/2025/AeccDbMgd.phase3-labels.txt,
// phase3-labelstyles.txt, phase3-labelmembers.txt.
// -----------------------------------------------------------------------------
using System.Text.Json.Nodes;

namespace Horizun.Civil3D.Core;

public static class AnnotationInputs
{
    private const ToolEffect W = ToolEffect.SafeWrite;

    /// <summary>Label style families the tool can resolve (kind -> Civil 3D collection, see LabelsCommand.StyleCollection).</summary>
    public static readonly string[] StyleKinds =
    {
        "alignment_major_station", "alignment_minor_station", "alignment_line", "alignment_curve", "alignment_station_offset",
        "surface_spot_elevation", "surface_slope", "surface_contour", "profile_grade_break", "profile_view_station_elevation",
        "general_note", "general_line", "general_curve", "point", "marker",
    };

    public static readonly IReadOnlyDictionary<string, ActionSpec> Labels = ToolRules.Register("horizun_c3d_labels", new Dictionary<string, ActionSpec>
    {
        ["list_styles"] = new(new[] { "kind" }, Array.Empty<string>(), Extra: a => V.OneOf(a, "kind", StyleKinds)),
        ["list"] = new(Array.Empty<string>(), new[] { "alignment", "surface", "profile_view", "entity", "limit" }, Extra: a => V.First(
            new[] { "alignment", "surface", "profile_view", "entity" }.Count(k => a[k] != null) > 1 ? "Give at most one of alignment, surface, profile_view, entity." : null,
            V.Hex(a, "entity"), V.Pos(a, "limit"))),
        ["get"] = new(new[] { "handle" }, Array.Empty<string>(), Extra: a => V.Hex(a, "handle")),
        ["alignment_stations"] = new(new[] { "alignment", "increment" }, new[] { "major", "style" }, W, a => V.Pos(a, "increment")),
        ["alignment_geometry"] = new(new[] { "alignment" }, new[] { "line_style", "curve_style" }, W),
        ["station_offset"] = new(new[] { "alignment", "points" }, new[] { "style", "marker" }, W, a => V.Points(a, "points", 1, 500)),
        ["surface_spot"] = new(new[] { "surface", "points" }, new[] { "style", "marker" }, W, a => V.Points(a, "points", 1, 500)),
        ["surface_slope"] = new(new[] { "surface" }, new[] { "points", "segments", "style" }, W, a => V.First(
            V.Exactly1(a, "points", "segments"), V.Points(a, "points", 1, 500), Segments(a))),
        ["contour_labels"] = new(new[] { "surface", "line" }, new[] { "style" }, W, a => V.Points(a, "line", 2, 100)),
        ["profile_pvis"] = new(new[] { "profile_view", "profile" }, new[] { "style" }, W),
        ["station_elevation"] = new(new[] { "profile_view", "items" }, new[] { "style", "marker" }, W, Items),
        ["note"] = new(new[] { "location", "text" }, new[] { "style", "marker" }, W, a => V.First(V.Point(a, "location"), Text(a))),
        ["segment"] = new(new[] { "entity" }, new[] { "ratio", "line_style", "curve_style" }, W, a => V.First(V.Hex(a, "entity"),
            a["ratio"] != null && (Hz.Num(a, "ratio") is not { } r || r < 0 || r > 1) ? "ratio must be between 0 and 1." : null)),
        ["set_text"] = new(new[] { "handle", "text" }, new[] { "component" }, W, a => V.First(V.Hex(a, "handle"), Text(a),
            a["component"] != null && (Hz.Num(a, "component") is not { } c || c < 0 || c != Math.Floor(c)) ? "component is a 0-based text component index." : null)),
        ["erase"] = new(new[] { "handles" }, Array.Empty<string>(), W, a => V.HexList(a, "handles", 2000)),
    });

    private static string? Text(JsonObject a) =>
        Hz.Str(a, "text") is { } t && t.Length <= 2000 ? null : "text must be a string of at most 2000 characters.";

    private static string? Segments(JsonObject a)
    {
        if (a["segments"] == null) return null;
        if (a["segments"] is not JsonArray s || s.Count == 0 || s.Count > 500) return "segments must list 1 to 500 {from, to}.";
        for (var i = 0; i < s.Count; i++)
        {
            if (s[i] is not JsonObject o) return "segments[" + i + "] must be {from, to}.";
            if ((V.Point(o, "from") ?? V.Point(o, "to")) is { } e) return "segments[" + i + "]." + e;
            if (o["from"] == null || o["to"] == null) return "segments[" + i + "] needs from and to.";
        }
        return null;
    }

    private static string? Items(JsonObject a)
    {
        if (a["items"] is not JsonArray s || s.Count == 0 || s.Count > 500) return "items must list 1 to 500 {station, elevation}.";
        return s.Any(n => n is not JsonObject o || Hz.Num(o, "station") is not { } st || !Hz.IsFinite(st) || Hz.Num(o, "elevation") is not { } el || !Hz.IsFinite(el))
            ? "Each item is {station, elevation} with finite numbers." : null;
    }
}
