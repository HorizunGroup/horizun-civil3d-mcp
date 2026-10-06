// -----------------------------------------------------------------------------
// Horizun Civil 3D MCP - Block C (AutoCAD inside the Civil 3D MCP): layers,
// entities, dimensions, cad_styles, blocks, tables, layouts, cleanup.
// API confirmed in docs/api-probes/2025/acdbmgd.phase3-annotation.txt and
// acdbmgd.phase3-cad.txt.
// -----------------------------------------------------------------------------
using System.Text.Json.Nodes;

namespace Horizun.Civil3D.Core;

public static class CadInputs
{
    private const ToolEffect W = ToolEffect.SafeWrite;
    private const ToolEffect F = ToolEffect.FullWrite;

    /// <summary>Lineweights AutoCAD accepts, in mm.</summary>
    public static readonly double[] Lineweights =
        { 0, 0.05, 0.09, 0.13, 0.15, 0.18, 0.2, 0.25, 0.3, 0.35, 0.4, 0.5, 0.53, 0.6, 0.7, 0.8, 0.9, 1.0, 1.06, 1.2, 1.4, 1.58, 2.0, 2.11 };

    private static readonly string[] LayerProps = { "color", "linetype", "lineweight", "description", "plot", "frozen", "locked", "off", "transparency" };
    private static readonly string[] EntProps = { "layer", "color", "linetype", "lineweight", "linetype_scale", "transparency" };

    // ---- shared field checks --------------------------------------------------

    public static string? Color(JsonObject a, string key)
    {
        var n = a[key];
        if (n == null) return null;
        if (n is JsonValue v && v.TryGetValue<string>(out var s))
        {
            if (s.Equals("bylayer", StringComparison.OrdinalIgnoreCase) || s.Equals("byblock", StringComparison.OrdinalIgnoreCase)) return null;
            if (s.Length == 7 && s[0] == '#' && s.Skip(1).All(Uri.IsHexDigit)) return null;
            return key + " must be an ACI number 1-255, \"#RRGGBB\", \"bylayer\" or \"byblock\".";
        }
        return Hz.AsDouble(n) is { } d && d == Math.Floor(d) && d is >= 1 and <= 255 ? null : key + " must be an ACI number 1-255, \"#RRGGBB\", \"bylayer\" or \"byblock\".";
    }

    public static string? Lineweight(JsonObject a, string key)
    {
        var n = a[key];
        if (n == null) return null;
        if (n is JsonValue v && v.TryGetValue<string>(out var s) && s is "default" or "bylayer" or "byblock") return null;
        return Hz.AsDouble(n) is { } d && Lineweights.Any(w => Math.Abs(w - d) < 1e-9) ? null
            : key + " must be one of " + string.Join(", ", Lineweights) + " (mm), \"default\", \"bylayer\" or \"byblock\".";
    }

    private static string? Transparency(JsonObject a) =>
        a["transparency"] != null && (Hz.Num(a, "transparency") is not { } t || t < 0 || t > 90 || t != Math.Floor(t)) ? "transparency is an integer 0-90 (%)." : null;

    private static string? Name(JsonObject a, string key) =>
        Hz.Str(a, key) is { } s && (s.Length > 255 || s.IndexOfAny("<>/\\\":;?*|,=`".ToCharArray()) >= 0) ? key + " contains characters AutoCAD does not allow in symbol names (<>/\\\":;?*|,=`)." : null;

    private static string? Props(JsonObject a) => V.First(Color(a, "color"), Lineweight(a, "lineweight"), Transparency(a), V.Pos(a, "linetype_scale"));

    private static string? Angle(JsonObject a, string key) => V.Fin(a, key);

    // ---- layers ------------------------------------------------------------

    public static readonly IReadOnlyDictionary<string, ActionSpec> Layers = ToolRules.Register("horizun_c3d_layers", new Dictionary<string, ActionSpec>
    {
        ["list"] = new(Array.Empty<string>(), new[] { "pattern", "limit" }, Extra: a => V.Pos(a, "limit")),
        ["create"] = new(new[] { "new_name" }, LayerProps, W, a => V.First(Name(a, "new_name"), Props(a))),
        ["set"] = new(new[] { "name" }, LayerProps.Concat(new[] { "new_name" }).ToArray(), W, a => V.First(Name(a, "new_name"), Props(a),
            LayerProps.Concat(new[] { "new_name" }).All(k => a[k] == null) ? "Give at least one property to change." : null)),
        ["set_current"] = new(new[] { "name" }, Array.Empty<string>(), W),
        ["states_list"] = new(Array.Empty<string>(), Array.Empty<string>()),
        ["state_save"] = new(new[] { "state_name" }, new[] { "description" }, W),
        ["state_restore"] = new(new[] { "state_name" }, Array.Empty<string>(), W),
    });

    // ---- entities ----------------------------------------------------------

    public static readonly string[] DrawTypes = { "line", "polyline", "polyline3d", "circle", "arc", "text", "mtext", "point", "hatch" };

    public static readonly IReadOnlyDictionary<string, ActionSpec> Entities = ToolRules.Register("horizun_c3d_entities", new Dictionary<string, ActionSpec>
    {
        ["query"] = new(Array.Empty<string>(), new[] { "types", "layers", "block", "window", "crossing", "limit" }, Extra: a => V.First(
            V.Strings(a, "types", 50), V.Strings(a, "layers", 500), Window(a), V.Pos(a, "limit"))),
        ["get"] = new(new[] { "handles" }, Array.Empty<string>(), Extra: a => V.HexList(a, "handles", 500)),
        ["set_properties"] = new(new[] { "handles" }, EntProps, W, a => V.First(V.HexList(a, "handles", 5000), Props(a),
            EntProps.All(k => a[k] == null) ? "Give at least one property to change." : null)),
        ["transform"] = new(new[] { "handles", "operation" }, new[] { "displacement", "base", "angle", "factor", "mirror_line", "keep_source" }, W, Transform),
        ["offset"] = new(new[] { "handle", "distance" }, new[] { "layer" }, W, a => V.First(V.Hex(a, "handle"), V.Fin(a, "distance"),
            Hz.Num(a, "distance") == 0 ? "distance must not be 0 (sign chooses the side, as in GetOffsetCurves)." : null)),
        ["explode"] = new(new[] { "handles" }, new[] { "keep_source" }, W, a => V.HexList(a, "handles", 1000)),
        ["join"] = new(new[] { "handles" }, Array.Empty<string>(), W, a => V.First(V.HexList(a, "handles", 1000),
            (a["handles"] as JsonArray)?.Count < 2 ? "join needs the base curve and at least one more." : null)),
        ["erase"] = new(new[] { "handles" }, Array.Empty<string>(), F, a => V.HexList(a, "handles", 5000)),
        ["draw"] = new(new[] { "items" }, new[] { "layer" }, W, Items),
    });

    private static string? Window(JsonObject a)
    {
        if (a["window"] == null) return null;
        if (a["window"] is not JsonObject w || V.Point(w, "min") is { } || V.Point(w, "max") is { } || w["min"] == null || w["max"] == null)
            return "window is {min:{x,y}, max:{x,y}}.";
        return Hz.Num((JsonObject)w["min"]!, "x") >= Hz.Num((JsonObject)w["max"]!, "x") || Hz.Num((JsonObject)w["min"]!, "y") >= Hz.Num((JsonObject)w["max"]!, "y")
            ? "window.min must be below and left of window.max." : null;
    }

    private static string? Transform(JsonObject a)
    {
        var op = Hz.Str(a, "operation");
        var e = V.First(V.HexList(a, "handles", 5000), V.OneOf(a, "operation", "move", "copy", "rotate", "scale", "mirror"),
            V.Point(a, "displacement", true), V.Point(a, "base", true), Angle(a, "angle"), V.Pos(a, "factor"));
        if (e != null) return e;
        string? Need(params string[] k) => k.FirstOrDefault(x => a[x] == null) is { } m ? op + " needs " + m + "." : null;
        string? Not(params string[] k) => k.FirstOrDefault(x => a[x] != null) is { } m ? m + " is not used by " + op + "." : null;
        return op switch
        {
            "move" or "copy" => V.First(Need("displacement"), Not("base", "angle", "factor", "mirror_line"), op == "move" ? Not("keep_source") : null),
            "rotate" => V.First(Need("base", "angle"), Not("displacement", "factor", "mirror_line", "keep_source")),
            "scale" => V.First(Need("base", "factor"), Not("displacement", "angle", "mirror_line", "keep_source")),
            _ => V.First(Need("mirror_line"), Not("displacement", "base", "angle", "factor"), MirrorLine(a)),
        };
    }

    private static string? MirrorLine(JsonObject a)
    {
        if (a["mirror_line"] is not JsonObject m || m["from"] == null || m["to"] == null || (V.Point(m, "from") ?? V.Point(m, "to")) != null)
            return "mirror_line is {from:{x,y}, to:{x,y}}.";
        var f = (JsonObject)m["from"]!; var t = (JsonObject)m["to"]!;
        return Hz.Num(f, "x") == Hz.Num(t, "x") && Hz.Num(f, "y") == Hz.Num(t, "y") ? "mirror_line has zero length." : null;
    }

    private static string? Items(JsonObject a)
    {
        if (a["items"] is not JsonArray items || items.Count == 0 || items.Count > 1000) return "items must list 1 to 1000 entities to draw.";
        for (var i = 0; i < items.Count; i++)
            if (DrawItem(items[i] as JsonObject) is { } e) return "items[" + i + "]: " + e;
        return null;
    }

    private static readonly Dictionary<string, (string[] Req, string[] Opt)> DrawFields = new()
    {
        ["line"] = (new[] { "from", "to" }, Array.Empty<string>()),
        ["polyline"] = (new[] { "points" }, new[] { "closed", "elevation", "bulges" }),
        ["polyline3d"] = (new[] { "points" }, new[] { "closed" }),
        ["circle"] = (new[] { "center", "radius" }, Array.Empty<string>()),
        ["arc"] = (new[] { "center", "radius", "start_angle", "end_angle" }, Array.Empty<string>()),
        ["text"] = (new[] { "position", "text", "height" }, new[] { "rotation", "style", "justify" }),
        ["mtext"] = (new[] { "position", "text", "height" }, new[] { "width", "rotation", "style", "attachment" }),
        ["point"] = (new[] { "position" }, Array.Empty<string>()),
        ["hatch"] = (new[] { "boundaries" }, new[] { "pattern", "scale", "angle" }),
    };

    private static readonly string[] ItemCommon = { "type", "layer", "color", "linetype", "lineweight" };

    public static string? DrawItem(JsonObject? o)
    {
        if (o == null) return "must be an object with type.";
        var type = Hz.Str(o, "type");
        if (type == null || !DrawFields.TryGetValue(type, out var f)) return "type must be one of " + string.Join(", ", DrawTypes) + ".";
        var allowed = f.Req.Concat(f.Opt).Concat(ItemCommon).ToHashSet();
        if (o.FirstOrDefault(kv => !allowed.Contains(kv.Key)) is { Key: { } extra }) return "field '" + extra + "' is not used by " + type + ".";
        if (f.Req.FirstOrDefault(r => o[r] == null) is { } miss) return type + " requires " + miss + ".";
        var e = V.First(Color(o, "color"), Lineweight(o, "lineweight"), V.Point(o, "from", true), V.Point(o, "to", true), V.Point(o, "center", true),
            V.Point(o, "position", true), V.Pos(o, "radius"), V.Pos(o, "height"), V.Pos(o, "width"), V.Pos(o, "scale"),
            V.Fin(o, "start_angle"), V.Fin(o, "end_angle"), V.Fin(o, "rotation"), V.Fin(o, "angle"), V.Fin(o, "elevation"),
            V.HexList(o, "boundaries", 100),
            V.OneOf(o, "justify", "left", "center", "right", "middle", "top_left", "top_center", "top_right", "middle_left", "middle_center", "middle_right", "bottom_left", "bottom_center", "bottom_right"),
            V.OneOf(o, "attachment", "top_left", "top_center", "top_right", "middle_left", "middle_center", "middle_right", "bottom_left", "bottom_center", "bottom_right"));
        if (e != null) return e;
        if (type is "polyline" or "polyline3d")
        {
            if (V.Points(o, "points", 2, 100000, type == "polyline3d") is { } pe) return pe;
            if (o["bulges"] != null && (o["bulges"] is not JsonArray b || b.Count != ((JsonArray)o["points"]!).Count || b.Any(x => Hz.AsDouble(x) is not { } d || !Hz.IsFinite(d))))
                return "bulges must give one finite bulge per point (0 = straight).";
        }
        if (type is "text" or "mtext" && (Hz.Str(o, "text") is not { } t || t.Length == 0 || t.Length > 10000)) return "text must be 1 to 10000 characters.";
        if (type == "arc" && Hz.Num(o, "start_angle") == Hz.Num(o, "end_angle")) return "start_angle and end_angle must differ.";
        return null;
    }

    // ---- dimensions --------------------------------------------------------

    private static readonly string[] DimCommon = { "style", "text", "layer" };

    public static readonly IReadOnlyDictionary<string, ActionSpec> Dimensions = ToolRules.Register("horizun_c3d_dimensions", new Dictionary<string, ActionSpec>
    {
        ["list"] = new(Array.Empty<string>(), new[] { "handles", "style", "limit" }, Extra: a => V.First(V.HexList(a, "handles", 2000), V.Pos(a, "limit"))),
        ["linear"] = new(new[] { "p1", "p2", "dim_line_point" }, DimCommon.Concat(new[] { "rotation" }).ToArray(), W, a => Pts(a, "p1", "p2", "dim_line_point")),
        ["aligned"] = new(new[] { "p1", "p2", "dim_line_point" }, DimCommon, W, a => Pts(a, "p1", "p2", "dim_line_point")),
        ["angular"] = new(new[] { "center", "p1", "p2", "arc_point" }, DimCommon, W, a => Pts(a, "center", "p1", "p2", "arc_point")),
        ["radial"] = new(new[] { "entity" }, DimCommon.Concat(new[] { "angle", "leader_length" }).ToArray(), W, a => V.First(V.Hex(a, "entity"), V.Fin(a, "angle"), V.NonNeg(a, "leader_length"))),
        ["diameter"] = new(new[] { "entity" }, DimCommon.Concat(new[] { "angle", "leader_length" }).ToArray(), W, a => V.First(V.Hex(a, "entity"), V.Fin(a, "angle"), V.NonNeg(a, "leader_length"))),
        ["ordinate"] = new(new[] { "feature_point", "leader_end", "axis" }, DimCommon, W, a => V.First(Pts(a, "feature_point", "leader_end"), V.OneOf(a, "axis", "x", "y"))),
        ["chain"] = new(new[] { "points", "dim_line_point" }, new[] { "style", "layer", "rotation", "mode", "spacing" }, W, a => V.First(
            V.Points(a, "points", 3, 500), V.Point(a, "dim_line_point"), V.OneOf(a, "mode", "continue", "baseline"), V.Pos(a, "spacing"))),
        ["mleader"] = new(new[] { "arrow_point", "landing_point", "text" }, new[] { "style", "layer", "text_height" }, W, a => V.First(
            Pts(a, "arrow_point", "landing_point"), V.Pos(a, "text_height"), Hz.Str(a, "text") is { Length: > 0 } ? null : "text must not be empty.")),
        ["set_text"] = new(new[] { "handles", "text" }, Array.Empty<string>(), W, a => V.HexList(a, "handles", 2000)),
    });

    private static string? Pts(JsonObject a, params string[] keys) => V.First(keys.Select(k => V.Point(a, k, true)).ToArray());

    // ---- cad styles --------------------------------------------------------

    /// <summary>Dimension style properties the tool sets (dimvar name -> meaning). Everything else is refused by name.</summary>
    public static readonly Dictionary<string, string> DimVars = new()
    {
        ["dimtxt"] = "text height", ["dimasz"] = "arrow size", ["dimexe"] = "extension beyond dimension line", ["dimexo"] = "extension line origin offset",
        ["dimgap"] = "text gap", ["dimdec"] = "decimal places (integer)", ["dimlfac"] = "linear scale factor", ["dimscale"] = "overall scale",
        ["dimrnd"] = "rounding", ["dimtad"] = "text vertical position (0 centred, 1 above)", ["dimtih"] = "text inside horizontal (bool)",
        ["dimtoh"] = "text outside horizontal (bool)", ["dimclrd"] = "dimension line colour (ACI)", ["dimclre"] = "extension line colour (ACI)",
        ["dimclrt"] = "text colour (ACI)", ["dimtxsty"] = "text style name", ["dimblk"] = "arrow block name (\"\" = closed filled)",
        ["dimpost"] = "prefix/suffix (\"<>\" = measurement)", ["dimdsep"] = "decimal separator (\".\" or \",\")", ["dimzin"] = "zero suppression (integer)",
        ["dimadec"] = "angular decimal places (integer)", ["dimcen"] = "centre mark size",
    };

    public static readonly IReadOnlyDictionary<string, ActionSpec> CadStyles = ToolRules.Register("horizun_c3d_cad_styles", new Dictionary<string, ActionSpec>
    {
        ["list"] = new(new[] { "kind" }, Array.Empty<string>(), Extra: a => V.OneOf(a, "kind", "text", "dim", "mleader", "linetype", "scale", "table")),
        ["text_create"] = new(new[] { "new_name", "font" }, new[] { "height", "width_factor", "oblique", "annotative" }, W, TextStyle),
        ["text_set"] = new(new[] { "name" }, new[] { "font", "height", "width_factor", "oblique", "annotative" }, W, TextStyle),
        ["dim_create"] = new(new[] { "new_name" }, new[] { "from", "properties", "annotative" }, W, a => V.First(Name(a, "new_name"), DimProps(a))),
        ["dim_set"] = new(new[] { "name", "properties" }, Array.Empty<string>(), W, DimProps),
        ["mleader_create"] = new(new[] { "new_name" }, new[] { "from", "text_style", "text_height", "arrow_size", "landing_gap", "annotative" }, W, a => V.First(
            Name(a, "new_name"), V.Pos(a, "text_height"), V.Pos(a, "arrow_size"), V.NonNeg(a, "landing_gap"))),
        ["set_current"] = new(new[] { "kind", "name" }, Array.Empty<string>(), W, a => V.OneOf(a, "kind", "text", "dim", "mleader", "scale")),
        ["scale_add"] = new(new[] { "new_name", "paper_units", "drawing_units" }, Array.Empty<string>(), W, a => V.First(V.Pos(a, "paper_units"), V.Pos(a, "drawing_units"))),
        ["linetype_load"] = new(new[] { "names" }, new[] { "file" }, W, a => V.Strings(a, "names", 100)),
    });

    private static string? TextStyle(JsonObject a) => V.First(Name(a, "new_name"), V.NonNeg(a, "height"), V.Pos(a, "width_factor"),
        a["oblique"] != null && (Hz.Num(a, "oblique") is not { } o || Math.Abs(o) > 85) ? "oblique is an angle in degrees between -85 and 85." : null);

    private static string? DimProps(JsonObject a)
    {
        if (a["properties"] == null) return null;
        if (a["properties"] is not JsonObject p || p.Count == 0) return "properties is an object of dimension variables, e.g. {\"dimtxt\": 2.5}.";
        foreach (var (k, v) in p)
        {
            var key = k.ToLowerInvariant();
            if (!DimVars.ContainsKey(key)) return "properties." + k + " is not supported. Supported: " + string.Join(", ", DimVars.Keys) + ".";
            var isText = key is "dimtxsty" or "dimblk" or "dimpost" or "dimdsep";
            var isBool = key is "dimtih" or "dimtoh";
            if (isText && (v is not JsonValue sv || !sv.TryGetValue<string>(out _))) return "properties." + k + " must be a string.";
            if (isBool && (v is not JsonValue bv || !bv.TryGetValue<bool>(out _))) return "properties." + k + " must be true or false.";
            if (!isText && !isBool && (Hz.AsDouble(v) is not { } d || !Hz.IsFinite(d) || d < 0)) return "properties." + k + " must be a number >= 0.";
            if (key is "dimdec" or "dimadec" or "dimzin" or "dimtad" or "dimclrd" or "dimclre" or "dimclrt" && Hz.AsDouble(v) is { } i && i != Math.Floor(i)) return "properties." + k + " must be an integer.";
        }
        return null;
    }

    // ---- blocks ------------------------------------------------------------

    public static readonly IReadOnlyDictionary<string, ActionSpec> Blocks = ToolRules.Register("horizun_c3d_blocks", new Dictionary<string, ActionSpec>
    {
        ["list"] = new(Array.Empty<string>(), new[] { "include_anonymous", "limit" }, Extra: a => V.Pos(a, "limit")),
        ["references"] = new(new[] { "name" }, new[] { "limit" }, Extra: a => V.Pos(a, "limit")),
        ["define"] = new(new[] { "new_name", "base_point" }, new[] { "handles", "attributes", "erase_source", "description" }, W, a => V.First(
            Name(a, "new_name"), V.Point(a, "base_point", true), V.HexList(a, "handles", 10000), Attributes(a),
            a["handles"] == null && a["attributes"] == null ? "Give handles (geometry to copy) and/or attributes." : null)),
        ["insert"] = new(new[] { "name", "position" }, new[] { "scale", "rotation", "layer", "attributes", "dynamic" }, W, a => V.First(
            V.Point(a, "position", true), V.Pos(a, "scale"), V.Fin(a, "rotation"), StringMap(a, "attributes"), ValueMap(a, "dynamic"))),
        ["set_attributes"] = new(new[] { "values" }, new[] { "handles", "name" }, W, a => V.First(V.Exactly1(a, "handles", "name"), V.HexList(a, "handles", 5000), StringMap(a, "values"))),
        ["set_dynamic"] = new(new[] { "handle", "properties" }, Array.Empty<string>(), W, a => V.First(V.Hex(a, "handle"), ValueMap(a, "properties"))),
        ["import"] = new(new[] { "source_dwg", "names" }, Array.Empty<string>(), W, a => V.Strings(a, "names", 500)),
    });

    private static string? Attributes(JsonObject a)
    {
        if (a["attributes"] == null) return null;
        if (a["attributes"] is not JsonArray arr || arr.Count == 0 || arr.Count > 200) return "attributes must list 1 to 200 attribute definitions.";
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < arr.Count; i++)
        {
            if (arr[i] is not JsonObject o) return "attributes[" + i + "] must be an object.";
            if (o.FirstOrDefault(kv => kv.Key is not ("tag" or "prompt" or "default" or "position" or "height" or "invisible" or "constant")) is { Key: { } x }) return "attributes[" + i + "]." + x + " is not recognised.";
            var tag = Hz.Str(o, "tag");
            if (string.IsNullOrWhiteSpace(tag) || tag.Contains(' ')) return "attributes[" + i + "].tag must be a non-empty tag without spaces.";
            if (!tags.Add(tag!)) return "attribute tag '" + tag + "' repeats.";
            if (o["position"] == null || V.Point(o, "position", true) is { }) return "attributes[" + i + "].position must be {x, y} relative to the base point.";
            if (V.Pos(o, "height") is { } h) return "attributes[" + i + "]." + h;
        }
        return null;
    }

    private static string? StringMap(JsonObject a, string key)
    {
        if (a[key] == null) return null;
        if (a[key] is not JsonObject m || m.Count == 0) return key + " is an object {TAG: \"value\"}.";
        return m.Any(kv => kv.Value is not JsonValue v || !v.TryGetValue<string>(out _)) ? key + " values must be strings." : null;
    }

    private static string? ValueMap(JsonObject a, string key)
    {
        if (a[key] == null) return null;
        if (a[key] is not JsonObject m || m.Count == 0) return key + " is an object {\"Property\": value}.";
        return m.Any(kv => kv.Value is not JsonValue) ? key + " values must be numbers, strings or booleans." : null;
    }

    // ---- tables ------------------------------------------------------------

    public static readonly IReadOnlyDictionary<string, ActionSpec> Tables = ToolRules.Register("horizun_c3d_tables", new Dictionary<string, ActionSpec>
    {
        ["list"] = new(Array.Empty<string>(), new[] { "limit" }, Extra: a => V.Pos(a, "limit")),
        ["get"] = new(new[] { "handle" }, Array.Empty<string>(), Extra: a => V.Hex(a, "handle")),
        ["create"] = new(new[] { "position" }, new[] { "rows", "csv", "title", "column_widths", "row_height", "text_height", "style", "layer" }, W, a => V.First(
            V.Point(a, "position", true), V.Exactly1(a, "rows", "csv"), Rows(a), V.Pos(a, "row_height"), V.Pos(a, "text_height"),
            a["column_widths"] != null && (a["column_widths"] is not JsonArray w || w.Count == 0 || w.Any(x => Hz.AsDouble(x) is not { } d || d <= 0)) ? "column_widths must list positive widths." : null)),
        ["set_cells"] = new(new[] { "handle", "cells" }, Array.Empty<string>(), W, a => V.First(V.Hex(a, "handle"), Cells(a))),
    });

    private static string? Rows(JsonObject a)
    {
        if (a["rows"] == null) return null;
        if (a["rows"] is not JsonArray rows || rows.Count == 0 || rows.Count > 5000) return "rows must list 1 to 5000 rows.";
        var n = -1;
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i] is not JsonArray r || r.Count == 0 || r.Count > 100) return "rows[" + i + "] must list 1 to 100 cells.";
            if (n >= 0 && r.Count != n) return "every row must have the same number of cells (" + n + ").";
            n = r.Count;
            if (r.Any(c => c is not JsonValue)) return "rows[" + i + "] cells must be strings or numbers.";
        }
        return null;
    }

    private static string? Cells(JsonObject a)
    {
        if (a["cells"] is not JsonArray c || c.Count == 0 || c.Count > 10000) return "cells must list 1 to 10000 {row, col, value}.";
        return c.Any(n => n is not JsonObject o || Hz.Num(o, "row") is not { } r || r < 0 || r != Math.Floor(r) || Hz.Num(o, "col") is not { } k || k < 0 || k != Math.Floor(k) || o["value"] is not JsonValue)
            ? "Each cell is {row, col, value} with 0-based integer row/col." : null;
    }

    // ---- layouts -----------------------------------------------------------

    public static readonly IReadOnlyDictionary<string, ActionSpec> Layouts = ToolRules.Register("horizun_c3d_layouts", new Dictionary<string, ActionSpec>
    {
        ["list"] = new(Array.Empty<string>(), Array.Empty<string>()),
        ["devices"] = new(Array.Empty<string>(), new[] { "device" }),
        ["create"] = new(new[] { "new_name" }, new[] { "copy_from", "template_dwg", "template_layout" }, W, a => V.First(Name(a, "new_name"),
            a["copy_from"] != null && (a["template_dwg"] != null || a["template_layout"] != null) ? "Give copy_from OR template_dwg + template_layout." : null,
            (a["template_dwg"] == null) != (a["template_layout"] == null) ? "template_dwg and template_layout go together." : null)),
        ["rename"] = new(new[] { "name", "new_name" }, Array.Empty<string>(), W, a => Name(a, "new_name")),
        ["delete"] = new(new[] { "name" }, Array.Empty<string>(), F),
        ["viewport"] = new(new[] { "layout", "center", "width", "height", "view_center", "scale" }, new[] { "locked", "frozen_layers", "layer" }, W, a => V.First(
            V.Point(a, "center"), V.Point(a, "view_center"), V.Pos(a, "width"), V.Pos(a, "height"), V.Pos(a, "scale"), V.Strings(a, "frozen_layers", 1000))),
        ["alignment_viewport"] = new(new[] { "layout", "center", "width", "height", "scale", "alignment", "station" }, new[] { "offset", "layer" }, W, SheetInputs.ValidateCreate),
        ["refresh_alignment_viewport"] = new(new[] { "handle" }, Array.Empty<string>(), W, a => V.Hex(a, "handle")),
        ["page_setup"] = new(new[] { "layout", "device" }, new[] { "media", "plot_style", "area", "fit", "scale", "centered", "rotation" }, W, a => V.First(
            V.OneOf(a, "area", "layout", "extents", "display"), V.Pos(a, "scale"), V.OneOf(a, "rotation", "0", "90", "180", "270"),
            Hz.Bool(a, "fit") == true && a["scale"] != null ? "Give fit OR scale." : null)),
        ["plot_pdf"] = new(new[] { "layouts", "output" }, new[] { "device", "overwrite" }, F, a => V.First(V.Strings(a, "layouts", 200),
            Hz.Str(a, "output") is { } o && Path.IsPathRooted(o) && o.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? null : "output must be an absolute .pdf path.")),
    });

    // ---- cleanup -----------------------------------------------------------

    public static readonly string[] PurgeKinds = { "layers", "linetypes", "text_styles", "dim_styles", "blocks", "mleader_styles", "table_styles", "regapps" };

    public static readonly IReadOnlyDictionary<string, ActionSpec> Cleanup = ToolRules.Register("horizun_c3d_cleanup", new Dictionary<string, ActionSpec>
    {
        ["purge_preview"] = new(Array.Empty<string>(), new[] { "kinds" }, Extra: Kinds),
        ["purge"] = new(Array.Empty<string>(), new[] { "kinds", "names" }, F, a => V.First(Kinds(a), V.Strings(a, "names", 5000))),
        ["drawing_report"] = new(Array.Empty<string>(), new[] { "limit" }, Extra: a => V.Pos(a, "limit")),
        ["xrefs"] = new(Array.Empty<string>(), Array.Empty<string>()),
        ["xref_reload"] = new(new[] { "names" }, Array.Empty<string>(), W, a => V.Strings(a, "names", 200)),
        ["standards_check"] = new(Array.Empty<string>(), new[] { "standard", "standard_path" }, Extra: a => V.First(V.Exactly1(a, "standard", "standard_path"),
            a["standard"] != null && a["standard"] is not JsonObject ? "standard is an object {layers:[...], text_styles:[...], dim_styles:[...], forbidden_layers:[...]}" : null)),
    });

    private static string? Kinds(JsonObject a)
    {
        if (a["kinds"] == null) return null;
        if (V.Strings(a, "kinds", 20) is { } e) return e;
        return ((JsonArray)a["kinds"]!).Select(n => n!.GetValue<string>()).FirstOrDefault(k => !PurgeKinds.Contains(k)) is { } bad
            ? "kinds: '" + bad + "' is not one of " + string.Join(", ", PurgeKinds) + "." : null;
    }
}
