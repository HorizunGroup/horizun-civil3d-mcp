// -----------------------------------------------------------------------------
// Horizun Civil 3D MCP - contract entries for block C (AutoCAD tools).
// Per-action validation: CadInputs.cs.
// -----------------------------------------------------------------------------
namespace Horizun.Civil3D.Core;

public static partial class Contract
{
    private const string Str = "{\"type\":\"string\"}";
    private const string StrArr = "{\"type\":\"array\",\"items\":{\"type\":\"string\"}}";
    private const string Num = "{\"type\":\"number\"}";
    private const string Bool = "{\"type\":\"boolean\"}";
    private const string ColorSchema = "{\"type\":[\"integer\",\"string\"],\"description\":\"ACI 1-255, \\\"#RRGGBB\\\", \\\"bylayer\\\" or \\\"byblock\\\".\"}";
    private const string LwSchema = "{\"type\":[\"number\",\"string\"],\"description\":\"mm (0.25, 0.35...), \\\"default\\\", \\\"bylayer\\\" or \\\"byblock\\\".\"}";

    private static string P(params (string Name, string Schema)[] props) => string.Join(",", props.Select(p => "\"" + p.Name + "\":" + p.Schema));

    private static IEnumerable<ToolContract> CadTools()
    {
        yield return Tool("horizun_c3d_layers", "layers", "AutoCAD layers and layer states", CadInputs.Layers,
            "Layers. list (properties, object counts, current; optional wildcard pattern). Writes (dry run + token + re-read): create " +
            "(colour, linetype - loaded from acadiso.lin/acad.lin when missing -, lineweight, description, plot, frozen/locked/off, " +
            "transparency), set (same properties, new_name; the current layer cannot be frozen), set_current, state_save / " +
            "state_restore (native layer states, all properties) and states_list.",
            P(("pattern", "{\"type\":\"string\",\"description\":\"Wildcard, e.g. C-ROAD*\"}"), ("limit", Num), ("new_name", Str), ("name", Str),
              ("color", ColorSchema), ("linetype", Str), ("lineweight", LwSchema), ("description", Str), ("plot", Bool), ("frozen", Bool), ("locked", Bool),
              ("off", Bool), ("transparency", "{\"type\":\"integer\",\"minimum\":0,\"maximum\":90}"), ("state_name", Str)));

        var item = "{\"type\":\"object\",\"required\":[\"type\"],\"properties\":{" + P(("type", "{\"type\":\"string\",\"enum\":[" + Enum(CadInputs.DrawTypes) + "]}"),
            ("from", Pt), ("to", Pt), ("points", "{\"type\":\"array\",\"items\":" + Pt + "}"), ("bulges", "{\"type\":\"array\",\"items\":{\"type\":\"number\"}}"),
            ("closed", Bool), ("elevation", Num), ("center", Pt), ("radius", Num), ("start_angle", "{\"type\":\"number\",\"description\":\"degrees CCW from +X\"}"),
            ("end_angle", Num), ("position", Pt), ("text", Str), ("height", Num), ("width", Num), ("rotation", Num), ("style", Str),
            ("justify", Str), ("attachment", Str), ("boundaries", "{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"description\":\"hatch: closed boundary handles\"}"),
            ("pattern", "{\"type\":\"string\",\"default\":\"SOLID\"}"), ("scale", Num), ("angle", Num), ("layer", Str), ("color", ColorSchema),
            ("linetype", Str), ("lineweight", LwSchema)) + "},\"additionalProperties\":false}";
        yield return Tool("horizun_c3d_entities", "entities", "AutoCAD entities: query, draw, edit, transform", CadInputs.Entities,
            "Model-space entities. query (by DXF type, layer, block name, window/crossing; returns handle, type, layer, real curve " +
            "length/area, extents) and get (full properties). Writes (dry run + token + re-read): draw (line, polyline with bulges, " +
            "3D polyline, circle, arc, text, mtext, point, hatch from closed boundaries; every item re-read: length/area/position/" +
            "text), set_properties (layer, colour, linetype, lineweight, linetype scale, transparency), transform (move, copy, " +
            "rotate, scale, mirror; re-reads extents), offset (signed distance), explode, join, erase (FULL WRITE). Locked-layer " +
            "objects and references are refused.",
            P(("types", "{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"description\":\"DXF names: LINE, LWPOLYLINE, POLYLINE, CIRCLE, ARC, TEXT, MTEXT, INSERT, HATCH, DIMENSION...\"}"),
              ("layers", StrArr), ("block", Str), ("window", "{\"type\":\"object\",\"properties\":{\"min\":" + Pt + ",\"max\":" + Pt + "},\"required\":[\"min\",\"max\"]}"),
              ("crossing", "{\"type\":\"boolean\",\"default\":false}"), ("limit", Num), ("handles", StrArr), ("handle", Str),
              ("layer", Str), ("color", ColorSchema), ("linetype", Str), ("lineweight", LwSchema), ("linetype_scale", Num),
              ("transparency", "{\"type\":\"integer\",\"minimum\":0,\"maximum\":90}"),
              ("operation", "{\"type\":\"string\",\"enum\":[\"move\",\"copy\",\"rotate\",\"scale\",\"mirror\"]}"), ("displacement", Pt), ("base", Pt),
              ("angle", "{\"type\":\"number\",\"description\":\"degrees CCW\"}"), ("factor", Num),
              ("mirror_line", "{\"type\":\"object\",\"properties\":{\"from\":" + Pt + ",\"to\":" + Pt + "},\"required\":[\"from\",\"to\"]}"),
              ("keep_source", Bool), ("distance", Num), ("items", "{\"type\":\"array\",\"maxItems\":1000,\"items\":" + item + "}")),
            destructive: true);

        yield return Tool("horizun_c3d_dimensions", "dimensions", "AutoCAD dimensions and multileaders", CadInputs.Dimensions,
            "Dimensions. list (measurement, style, text override). Writes (dry run + token + re-read of the MEASUREMENT against the " +
            "analytic value of your points): linear (rotation in degrees, 0 = horizontal), aligned, angular (3-point), radial and " +
            "diameter (on a circle/arc handle), ordinate (x or y datum from the WCS origin), chain (continue or baseline from 3+ " +
            "points), mleader (arrow, landing, MText), set_text (\"<>\" keeps the measured value, e.g. \"<> m\"). Style = dimension " +
            "style name (default: current).",
            P(("handles", StrArr), ("style", Str), ("limit", Num), ("p1", Pt), ("p2", Pt), ("dim_line_point", Pt), ("rotation", Num),
              ("text", Str), ("layer", Str), ("center", Pt), ("arc_point", Pt), ("entity", Str), ("angle", Num), ("leader_length", Num),
              ("feature_point", Pt), ("leader_end", Pt), ("axis", "{\"type\":\"string\",\"enum\":[\"x\",\"y\"]}"),
              ("points", "{\"type\":\"array\",\"items\":" + Pt + "}"), ("mode", "{\"type\":\"string\",\"enum\":[\"continue\",\"baseline\"],\"default\":\"continue\"}"),
              ("spacing", Num), ("arrow_point", Pt), ("landing_point", Pt), ("text_height", Num)));

        yield return Tool("horizun_c3d_cad_styles", "cad_styles", "Text, dimension and multileader styles, annotation scales, linetypes", CadInputs.CadStyles,
            "Drafting standards. list (text, dim, mleader, linetype, scale, table). Writes (dry run + token + re-read): text_create / " +
            "text_set (font file .ttf or .shx, height 0 = variable, width factor, oblique, annotative), dim_create (optionally copied " +
            "from another style) / dim_set with a whitelist of dimension variables (" + string.Join(", ", CadInputs.DimVars.Keys) + "), " +
            "mleader_create, set_current (text/dim/mleader/scale), scale_add (annotation scale paper:drawing units), linetype_load " +
            "(from acadiso.lin by default).",
            P(("kind", Str), ("new_name", Str), ("name", Str), ("font", Str), ("height", Num), ("width_factor", Num), ("oblique", Num),
              ("annotative", Bool), ("from", Str), ("properties", "{\"type\":\"object\",\"description\":\"Dimension variables, e.g. {\\\"dimtxt\\\":2.5,\\\"dimasz\\\":2}\"}"),
              ("text_style", Str), ("text_height", Num), ("arrow_size", Num), ("landing_gap", Num), ("paper_units", Num), ("drawing_units", Num),
              ("names", StrArr), ("file", Str)));

        var attr = "{\"type\":\"object\",\"properties\":{" + P(("tag", Str), ("prompt", Str), ("default", Str), ("position", Pt), ("height", Num), ("invisible", Bool), ("constant", Bool)) + "},\"required\":[\"tag\",\"position\"],\"additionalProperties\":false}";
        yield return Tool("horizun_c3d_blocks", "blocks", "Blocks: define, insert, attributes, dynamic properties, library import", CadInputs.Blocks,
            "Blocks. list (definitions, attribute definitions, dynamic flag, reference count), references (inserts of a block with " +
            "position, scale, rotation, attribute values and dynamic properties). Writes (dry run + token + re-read): define (copy " +
            "geometry by handle into a new block and/or add attribute definitions), insert (creates the attribute references with " +
            "your values, sets dynamic properties), set_attributes (batch, e.g. title blocks: by handles or every reference of a " +
            "block), set_dynamic, import (copy block definitions from another DWG).",
            P(("include_anonymous", Bool), ("limit", Num), ("name", Str), ("new_name", Str), ("base_point", Pt), ("handles", StrArr),
              ("attributes", "{\"type\":[\"array\",\"object\"],\"description\":\"define: attribute definitions; insert: {TAG: value}\",\"items\":" + attr + "}"),
              ("erase_source", Bool), ("description", Str), ("position", Pt), ("scale", Num), ("rotation", Num), ("layer", Str),
              ("dynamic", "{\"type\":\"object\"}"), ("values", "{\"type\":\"object\",\"description\":\"{TAG: value}\"}"), ("handle", Str),
              ("properties", "{\"type\":\"object\"}"), ("source_dwg", Str), ("names", StrArr)));

        yield return Tool("horizun_c3d_tables", "tables", "AutoCAD tables", CadInputs.Tables,
            "Tables. list, get (every cell). Writes (dry run + token + re-read of every cell): create (from rows or a CSV file; " +
            "optional title row, column widths, row height, text height, table style) and set_cells ({row, col, value}, 0-based).",
            P(("limit", Num), ("handle", Str), ("position", Pt), ("rows", "{\"type\":\"array\",\"items\":{\"type\":\"array\"}}"), ("csv", Str),
              ("title", Str), ("column_widths", "{\"type\":\"array\",\"items\":{\"type\":\"number\"}}"), ("row_height", Num), ("text_height", Num),
              ("style", Str), ("layer", Str), ("cells", "{\"type\":\"array\",\"items\":{\"type\":\"object\",\"properties\":{\"row\":{\"type\":\"integer\"},\"col\":{\"type\":\"integer\"},\"value\":{}},\"required\":[\"row\",\"col\",\"value\"]}}")));

        yield return Tool("horizun_c3d_layouts", "layouts", "Layouts, viewports, page setups and PDF", CadInputs.Layouts,
            "Paper space. list (layouts, viewports with scale/centre/lock, plot device and media), devices (plot devices; with device: " +
            "its media). Writes (dry run + token + re-read): create (empty, copy of a layout, or from a template DWG layout), rename, " +
            "delete (FULL WRITE), viewport (paper centre/size, model view centre, scale as paper/model e.g. 0.002 = 1:500, lock, " +
            "frozen layers), page_setup (device, media, plot style, area, fit or scale, rotation). plot_pdf (FULL WRITE) plots layouts " +
            "to one PDF (foreground plot) and verifies the file exists, starts with %PDF and has the expected page count. This is " +
            "the alternative to plan production sheets, which have no public API.",
            P(("device", Str), ("new_name", Str), ("name", Str), ("copy_from", Str), ("template_dwg", Str), ("template_layout", Str),
              ("layout", Str), ("center", Pt), ("width", Num), ("height", Num), ("view_center", Pt), ("scale", Num), ("locked", Bool),
              ("frozen_layers", StrArr), ("layer", Str), ("media", Str), ("plot_style", Str), ("area", "{\"type\":\"string\",\"enum\":[\"layout\",\"extents\",\"display\"]}"),
              ("fit", Bool), ("centered", Bool), ("rotation", "{\"type\":\"string\",\"enum\":[\"0\",\"90\",\"180\",\"270\"]}"),
              ("layouts", StrArr), ("output", Str), ("overwrite", Bool)),
            destructive: true);

        yield return Tool("horizun_c3d_cleanup", "cleanup", "Purge, drawing report, xrefs, standards check", CadInputs.Cleanup,
            "Drawing maintenance. purge_preview (what is unused, by kind), purge (FULL WRITE; repeats until nothing more is purgeable; " +
            "re-reads that every purged name is gone), drawing_report (entity counts by type and layer, objects on layer 0/Defpoints, " +
            "zero-length curves, empty texts, proxies, frozen/locked layers with objects), xrefs (status, found path) and " +
            "xref_reload, standards_check (layers with colour/linetype/lineweight, required text/dim styles, forbidden layer " +
            "patterns; read-only report - fix with layers.set / cad_styles).",
            P(("kinds", "{\"type\":\"array\",\"items\":{\"type\":\"string\",\"enum\":[" + Enum(CadInputs.PurgeKinds) + "]}}"), ("names", StrArr), ("limit", Num),
              ("standard", "{\"type\":\"object\"}"), ("standard_path", Str)),
            destructive: true);
    }
}
