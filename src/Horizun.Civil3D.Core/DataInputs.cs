// -----------------------------------------------------------------------------
// Horizun Civil 3D MCP - Block D (networks and data): pipes, points, exchange.
// API confirmed in docs/api-probes/2025/AeccDbMgd.phase3-pipes.txt,
// phase3-points.txt, phase3-datashortcuts.txt.
// -----------------------------------------------------------------------------
using System.Text.Json.Nodes;

namespace Horizun.Civil3D.Core;

public static class DataInputs
{
    private const ToolEffect W = ToolEffect.SafeWrite;
    private const ToolEffect F = ToolEffect.FullWrite;

    // ---- pipes ---------------------------------------------------------------

    public static readonly IReadOnlyDictionary<string, ActionSpec> Pipes = ToolRules.Register("horizun_c3d_pipes", new Dictionary<string, ActionSpec>
    {
        ["catalog"] = new(Array.Empty<string>(), new[] { "parts_list" }),
        ["list"] = new(Array.Empty<string>(), new[] { "network", "limit" }, Extra: a => V.Pos(a, "limit")),
        ["create_network"] = new(new[] { "new_name", "parts_list" }, new[] { "surface", "layer" }, W),
        ["add_structures"] = new(new[] { "network", "structures" }, Array.Empty<string>(), W, Structures),
        ["add_pipes"] = new(new[] { "network", "pipes" }, Array.Empty<string>(), W, PipeItems),
        ["validate"] = new(new[] { "network" }, new[] { "min_cover", "max_cover", "min_slope_pct", "max_slope_pct" }, Extra: a => V.First(
            V.NonNeg(a, "min_cover"), V.Pos(a, "max_cover"), V.NonNeg(a, "min_slope_pct"), V.Pos(a, "max_slope_pct"))),
    });

    private static string? Structures(JsonObject a)
    {
        if (a["structures"] is not JsonArray s || s.Count == 0 || s.Count > 500) return "structures must list 1 to 500 structures.";
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < s.Count; i++)
        {
            if (s[i] is not JsonObject o) return "structures[" + i + "] must be an object.";
            if (o.FirstOrDefault(kv => kv.Key is not ("name" or "position" or "family" or "size" or "rim" or "sump_depth" or "rotation")) is { Key: { } x }) return "structures[" + i + "]." + x + " is not recognised.";
            if (o["position"] == null || V.Point(o, "position") is { } || Hz.Str(o, "family") is null || Hz.Str(o, "size") is null)
                return "structures[" + i + "] needs position {x,y}, family and size (see catalog).";
            if (V.First(V.Fin(o, "rim"), V.NonNeg(o, "sump_depth"), V.Fin(o, "rotation")) is { } e) return "structures[" + i + "]." + e;
            if (Hz.Str(o, "name") is { } n && !names.Add(n)) return "structure name '" + n + "' repeats.";
        }
        return null;
    }

    private static string? PipeItems(JsonObject a)
    {
        if (a["pipes"] is not JsonArray p || p.Count == 0 || p.Count > 1000) return "pipes must list 1 to 1000 pipes.";
        for (var i = 0; i < p.Count; i++)
        {
            if (p[i] is not JsonObject o) return "pipes[" + i + "] must be an object.";
            if (o.FirstOrDefault(kv => kv.Key is not ("name" or "from" or "to" or "family" or "size" or "start_invert" or "end_invert" or "slope_pct")) is { Key: { } x }) return "pipes[" + i + "]." + x + " is not recognised.";
            if (Hz.Str(o, "from") is null || Hz.Str(o, "to") is null || Hz.Str(o, "family") is null || Hz.Str(o, "size") is null)
                return "pipes[" + i + "] needs from and to (structure names), family and size.";
            if (string.Equals(Hz.Str(o, "from"), Hz.Str(o, "to"), StringComparison.OrdinalIgnoreCase)) return "pipes[" + i + "] connects a structure to itself.";
            if (o["start_invert"] == null) return "pipes[" + i + "] needs start_invert (and end_invert or slope_pct).";
            if ((o["end_invert"] == null) == (o["slope_pct"] == null)) return "pipes[" + i + "]: give end_invert OR slope_pct (positive = falling from start to end).";
            if (V.First(V.Fin(o, "start_invert"), V.Fin(o, "end_invert"), V.Fin(o, "slope_pct")) is { } e) return "pipes[" + i + "]." + e;
        }
        return null;
    }

    // ---- points --------------------------------------------------------------

    public static readonly string[] PointFormats = { "PNEZD", "PENZD", "PNEZ", "PENZ", "NEZD", "ENZD", "NEZ", "ENZ" };

    public static readonly IReadOnlyDictionary<string, ActionSpec> Points = ToolRules.Register("horizun_c3d_points", new Dictionary<string, ActionSpec>
    {
        ["list"] = new(Array.Empty<string>(), new[] { "group", "numbers", "limit" }, Extra: a => V.First(NumberRanges(a), V.Pos(a, "limit"))),
        ["create"] = new(new[] { "points" }, new[] { "group" }, W, PointItems),
        ["import"] = new(new[] { "file", "format" }, new[] { "group", "skip_header" }, W, a => V.First(V.OneOf(a, "format", PointFormats),
            Hz.Str(a, "file") is { } f && Path.IsPathRooted(f) ? null : "file must be an absolute path.")),
        ["export_csv"] = new(new[] { "output" }, new[] { "group", "numbers", "format" }, W, a => V.First(V.OneOf(a, "format", PointFormats),
            Hz.Str(a, "output") is { } f && Path.IsPathRooted(f) ? null : "output must be an absolute path.")),
        ["elevations_from_surface"] = new(new[] { "surface" }, new[] { "group", "numbers" }, W, a => V.First(V.Exactly1(a, "group", "numbers"), NumberRanges(a))),
        ["groups"] = new(Array.Empty<string>(), Array.Empty<string>()),
        ["group_create"] = new(new[] { "new_name" }, new[] { "include_numbers", "include_raw_descriptions", "include_full_descriptions", "include_names", "exclude_numbers", "description" }, W,
            a => new[] { "include_numbers", "include_raw_descriptions", "include_full_descriptions", "include_names" }.All(k => a[k] == null)
                ? "Give at least one include_* filter (numbers like \"1-100,205\", descriptions like \"TREE*\")." : RangeText(a, "include_numbers") ?? RangeText(a, "exclude_numbers")),
        ["erase"] = new(new[] { "numbers" }, Array.Empty<string>(), F, NumberRanges),
    });

    private static string? RangeText(JsonObject a, string key) =>
        Hz.Str(a, key) is { } s && NumberSet.Parse(s) == null ? key + " must look like \"1-100,205,300-310\"." : null;

    private static string? NumberRanges(JsonObject a) =>
        a["numbers"] != null && (Hz.Str(a, "numbers") is not { } s || NumberSet.Parse(s) == null) ? "numbers must look like \"1-100,205,300-310\"." : null;

    private static string? PointItems(JsonObject a)
    {
        if (a["points"] is not JsonArray p || p.Count == 0 || p.Count > 100000) return "points must list 1 to 100000 points.";
        for (var i = 0; i < p.Count; i++)
        {
            if (p[i] is not JsonObject o) return "points[" + i + "] must be an object.";
            if (o.FirstOrDefault(kv => kv.Key is not ("x" or "y" or "z" or "description" or "name" or "number")) is { Key: { } x }) return "points[" + i + "]." + x + " is not recognised.";
            if (Hz.Num(o, "x") is not { } px || Hz.Num(o, "y") is not { } py || !double.IsFinite(px) || !double.IsFinite(py) || (o["z"] != null && V.Fin(o, "z") is { }))
                return "points[" + i + "] needs finite x, y (and optional z).";
            if (o["number"] != null && (Hz.Num(o, "number") is not { } n || n < 1 || n != Math.Floor(n) || n > uint.MaxValue)) return "points[" + i + "].number must be a positive integer.";
        }
        return null;
    }

    // ---- exchange --------------------------------------------------------------

    public static readonly string[] ShortcutTypes = { "Surface", "Alignment", "Profile", "PipeNetwork", "PressurePipeNetwork", "Corridor", "SampleLineGroup", "ViewFrameGroup" };

    public static readonly IReadOnlyDictionary<string, ActionSpec> Exchange = ToolRules.Register("horizun_c3d_exchange", new Dictionary<string, ActionSpec>
    {
        ["shortcuts_status"] = new(Array.Empty<string>(), Array.Empty<string>()),
        ["shortcuts_project"] = new(new[] { "working_folder" }, new[] { "name", "description" }, F, a => V.First(
            Hz.Str(a, "working_folder") is { } w && Path.IsPathRooted(w) ? null : "working_folder must be an absolute folder path.",
            a["name"] == null || (Hz.Str(a, "name") is { } n && n.Length <= 120 && n.IndexOfAny(Path.GetInvalidFileNameChars()) < 0) ? null : "name must be a valid folder name.")),
        ["shortcuts_publish"] = new(new[] { "names" }, Array.Empty<string>(), F, a => V.Strings(a, "names", 500)),
        ["shortcuts_reference"] = new(new[] { "name", "type" }, new[] { "source_dwg" }, W, a => V.First(V.OneOf(a, "type", ShortcutTypes),
            a["source_dwg"] != null && !(Hz.Str(a, "source_dwg") is { } s && Path.IsPathRooted(s)) ? "source_dwg must be an absolute path." : null)),
        ["export_landxml"] = new(new[] { "output" }, new[] { "surfaces", "alignments", "include_profiles" }, W, a => V.First(
            Hz.Str(a, "output") is { } o && Path.IsPathRooted(o) && o.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ? null : "output must be an absolute .xml path.",
            V.Strings(a, "surfaces", 100), V.Strings(a, "alignments", 500),
            a["surfaces"] == null && a["alignments"] == null ? "Give surfaces and/or alignments to export." : null)),
    });
}

/// <summary>Point number sets like "1-100,205,300-310".</summary>
public static class NumberSet
{
    public static List<(uint From, uint To)>? Parse(string text)
    {
        var list = new List<(uint, uint)>();
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var ends = part.Split('-', StringSplitOptions.TrimEntries);
            if (ends.Length is < 1 or > 2 || !uint.TryParse(ends[0], out var a)) return null;
            var b = a;
            if (ends.Length == 2 && !uint.TryParse(ends[1], out b)) return null;
            if (b < a) return null;
            list.Add((a, b));
        }
        return list.Count == 0 ? null : list;
    }

    public static bool Contains(List<(uint From, uint To)> set, uint n) => set.Any(r => n >= r.From && n <= r.To);
}
