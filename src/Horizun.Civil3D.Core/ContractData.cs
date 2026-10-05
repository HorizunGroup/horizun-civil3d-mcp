// -----------------------------------------------------------------------------
// Horizun Civil 3D MCP - contract entries for block D (pipes, points, exchange).
// Per-action validation: DataInputs.cs.
// -----------------------------------------------------------------------------
namespace Horizun.Civil3D.Core;

public static partial class Contract
{
    private static IEnumerable<ToolContract> DataTools()
    {
        var structure = "{\"type\":\"object\",\"properties\":{" + P(("name", Str), ("position", Pt), ("family", Str), ("size", Str),
            ("rim", "{\"type\":\"number\",\"description\":\"Rim elevation; default = reference surface at the position.\"}"),
            ("sump_depth", "{\"type\":\"number\",\"description\":\"Civil 3D sump depth below the LOWEST CONNECTED PIPE invert; takes effect once pipes are connected.\"}"), ("rotation", Num)) + "},\"required\":[\"position\",\"family\",\"size\"],\"additionalProperties\":false}";
        var pipe = "{\"type\":\"object\",\"properties\":{" + P(("name", Str), ("from", Str), ("to", Str), ("family", Str), ("size", Str),
            ("start_invert", Num), ("end_invert", Num), ("slope_pct", "{\"type\":\"number\",\"description\":\"Positive = falling from start to end.\"}")) +
            "},\"required\":[\"from\",\"to\",\"family\",\"size\",\"start_invert\"],\"additionalProperties\":false}";
        yield return Tool("horizun_c3d_pipes", "pipes", "Gravity pipe networks: catalog, create, structures, pipes, validation", DataInputs.Pipes,
            "Gravity pipe networks. catalog (parts lists -> pipe/structure families -> sizes with inner diameter), list (networks; " +
            "with network: pipes with inverts/slope/length/cover and structures with rim/sump/connections). Writes (dry run + token + " +
            "re-read): create_network (parts list, reference surface), add_structures (family + size from the catalog, rim from the " +
            "surface unless given, sump depth), add_pipes (between named structures, start invert + end invert or slope %; re-reads " +
            "inverts, slope, 2D length and both connections). validate: cover (min/max against the reference surface), slope range, " +
            "disconnected pipe ends and structures without pipes - read-only, YOUR criteria.",
            P(("parts_list", Str), ("network", Str), ("limit", Num), ("new_name", Str), ("surface", Str), ("layer", Str),
              ("structures", "{\"type\":\"array\",\"items\":" + structure + "}"), ("pipes", "{\"type\":\"array\",\"items\":" + pipe + "}"),
              ("min_cover", Num), ("max_cover", Num), ("min_slope_pct", Num), ("max_slope_pct", Num)));

        var point = "{\"type\":\"object\",\"properties\":{" + P(("x", Num), ("y", Num), ("z", Num), ("description", Str), ("name", Str),
            ("number", "{\"type\":\"integer\",\"minimum\":1}")) + "},\"required\":[\"x\",\"y\"],\"additionalProperties\":false}";
        yield return Tool("horizun_c3d_points", "points", "COGO points and point groups", DataInputs.Points,
            "COGO points. list (by group or number ranges \"1-100,205\"), groups. Writes (dry run + token + re-read of number, E, N, Z " +
            "and description): create (explicit numbers are refused when taken; otherwise next number), import (our own reader for " +
            "PNEZD/PENZD/PNEZ/PENZ/NEZD/ENZD/NEZ/ENZ, comma/semicolon/tab/space; bad lines are reported, not guessed), export_csv " +
            "(new file only), elevations_from_surface (points outside the surface are refused), group_create (standard query: " +
            "numbers, raw/full descriptions, names with wildcards; the re-read compares the group with our own evaluation of the " +
            "query), erase (FULL WRITE). Optional group adds created/imported points to a group by number.",
            P(("group", Str), ("numbers", "{\"type\":\"string\",\"description\":\"e.g. 1-100,205\"}"), ("limit", Num),
              ("points", "{\"type\":\"array\",\"items\":" + point + "}"), ("file", Str),
              ("format", "{\"type\":\"string\",\"enum\":[" + Enum(DataInputs.PointFormats) + "]}"), ("skip_header", Bool), ("output", Str), ("surface", Str),
              ("new_name", Str), ("include_numbers", Str), ("include_raw_descriptions", Str), ("include_full_descriptions", Str), ("include_names", Str),
              ("exclude_numbers", Str), ("description", Str)),
            destructive: true);

        yield return Tool("horizun_c3d_exchange", "exchange", "Data shortcuts and LandXML export", DataInputs.Exchange,
            "Data exchange. shortcuts_status (working folder, current project, published shortcuts with broken state, items of this " +
            "drawing that can be published). shortcuts_project (FULL WRITE: sets Civil 3D's data-shortcut working folder and creates or " +
            "selects a project in it - this changes the user's Civil 3D setting; the plan returns the previous folder and project so " +
            "they can be restored with the same action). shortcuts_publish (FULL WRITE: writes the shared project's shortcut files; the drawing " +
            "must be saved inside the project). shortcuts_reference (creates a data reference in this drawing from a published " +
            "shortcut or a source DWG; re-reads the reference object). export_landxml (LandXML 1.2 written by Horizun - Civil 3D " +
            "has no .NET export: TIN surfaces with points and faces, tangent/arc alignments with their layout profiles; never " +
            "overwrites; the file is re-read and its point/face counts and lengths compared with the drawing).",
            P(("names", StrArr), ("name", Str), ("working_folder", Str), ("description", Str), ("type", "{\"type\":\"string\",\"enum\":[" + Enum(DataInputs.ShortcutTypes) + "]}"), ("source_dwg", Str),
              ("output", Str), ("surfaces", StrArr), ("alignments", StrArr), ("include_profiles", "{\"type\":\"boolean\",\"default\":true}")));
    }
}
