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
        yield return Tool("horizun_c3d_pipes", "pipes", "Gravity networks and pressure-network inspection/creation", DataInputs.Pipes,
            "Gravity pipe networks. catalog (parts lists -> pipe/structure families -> sizes with inner diameter), list (networks; " +
            "with network: pipes with inverts/slope/length/cover and structures with rim/sump/connections). Writes (dry run + token + " +
            "re-read): create_network (parts list, reference surface), add_structures (family + size from the catalog, rim from the " +
            "surface unless given, sump depth), add_pipes (between named structures, start invert + end invert or slope %; re-reads " +
            "inverts, slope, 2D length and both connections). validate: cover (min/max against the reference surface), slope range, " +
            "disconnected pipe ends and structures without pipes - read-only, YOUR criteria. " +
            "Pressure networks: pressure_list (paginated names/counts), pressure_get (named network; paginated pipes, fittings, " +
            "appurtenances, native dimensions and port connectivity, null plus reason when unreadable), pressure_create_network " +
            "(SAFE WRITE, empty network, optional existing surface/layer), pressure_rename (SAFE WRITE). These writes use dry run, " +
            "confirmation and post-commit re-read. Pressure part creation, sizing, hydraulics and connection editing are not implemented.",
            P(("parts_list", Str), ("network", Str), ("limit", Num), ("offset", Num), ("new_name", Str), ("surface", Str), ("layer", Str),
              ("structures", "{\"type\":\"array\",\"items\":" + structure + "}"), ("pipes", "{\"type\":\"array\",\"items\":" + pipe + "}"),
              ("min_cover", Num), ("max_cover", Num), ("min_slope_pct", Num), ("max_slope_pct", Num)));

        var point = "{\"type\":\"object\",\"properties\":{" + P(("x", Num), ("y", Num), ("z", Num), ("description", Str), ("name", Str),
            ("number", "{\"type\":\"integer\",\"minimum\":1}")) + "},\"required\":[\"x\",\"y\"],\"additionalProperties\":false}";
        yield return Tool("horizun_c3d_points", "points", "COGO points and point groups", DataInputs.Points,
            "COGO points. list (by group or number ranges \"1-100,205\"), groups. Writes (dry run + token + re-read of number, E, N, Z " +
            "and description): create (explicit numbers are refused when taken; otherwise next number), import (our own reader for " +
            "PNEZD/PENZD/PNEZ/PENZ/NEZD/ENZD/NEZ/ENZ, comma/semicolon/tab/space; bad lines are reported, not guessed), export_csv " +
            "(FULL WRITE, new file only), elevations_from_surface (points outside the surface are refused), group_create (standard query: " +
            "numbers, raw/full descriptions, names with wildcards; the re-read compares the group with our own evaluation of the " +
            "query), erase (FULL WRITE). export_editable_csv (FULL WRITE, new file only) carries source/unit fingerprints for Excel edits. " +
            "apply_csv updates existing COGO coordinates/descriptions after rejecting changed source, units or row identities; dry run/token/new-transaction reread. " +
            "Optional group adds created/imported points to a group by number.",
            P(("group", Str), ("numbers", "{\"type\":\"string\",\"description\":\"e.g. 1-100,205\"}"), ("limit", Num),
              ("points", "{\"type\":\"array\",\"items\":" + point + "}"), ("file", Str),
              ("format", "{\"type\":\"string\",\"enum\":[" + Enum(DataInputs.PointFormats) + "]}"), ("skip_header", Bool), ("output", Str), ("surface", Str),
              ("new_name", Str), ("include_numbers", Str), ("include_raw_descriptions", Str), ("include_full_descriptions", Str), ("include_names", Str),
              ("exclude_numbers", Str), ("description", Str)),
            destructive: true);

        yield return Tool("horizun_c3d_exchange", "exchange", "Data shortcuts, Revit terrain, DWG and LandXML export", DataInputs.Exchange,
            "Data exchange. shortcuts_status (working folder, current project, published shortcuts with broken state, items of this " +
            "drawing that can be published). shortcuts_project (FULL WRITE: sets Civil 3D's data-shortcut working folder and creates or " +
            "selects a project in it - this changes the user's Civil 3D setting; the plan returns the previous folder and project so " +
            "they can be restored with the same action). shortcuts_publish (FULL WRITE: writes the shared project's shortcut files; the drawing " +
            "must be saved inside the project). shortcuts_reference (creates a data reference in this drawing from a published " +
            "shortcut or a source DWG; re-reads the reference object). export_landxml (LandXML 1.2 written by Horizun - Civil 3D " +
            "has no .NET export: TIN surfaces with points and faces, tangent/arc alignments with their layout profiles; never " +
            "overwrites; the file is re-read and its point/face counts and lengths compared with the drawing). " +
            "export_dwg (FULL WRITE: whole-database DWG copy of the CURRENT unsaved state, not a Save As of the source; " +
            "new destination only, staged and reopened before publication; verifies local block/entity class counts and insertion " +
            "units, not individual Civil design values. Xrefs and data shortcuts remain external references, not bundled files). " +
            "export_revit (FULL WRITE: one named TIN surface -> new ZIP containing terrain.xml and a provenance/units/coordinates " +
            "manifest; 20000 visible vertices maximum, no thinning. Payload is reopened and compared byte-for-byte. Use " +
            "scripts/prepare-revit-terrain.ps1 to generate a separate Revit dry-run request for horizun_create_elements kind=toposolid; " +
            "explicit model/type/level and shared-coordinate verification are required. Revit retriangulates points: triangle connectivity, " +
            "holes and concave boundaries are not guaranteed by that importer. Export does not connect to or modify Revit. " +
            "All file exports require FULL WRITE and explicit drawing targeting.",
            P(("names", StrArr), ("name", Str), ("working_folder", Str), ("description", Str), ("type", "{\"type\":\"string\",\"enum\":[" + Enum(DataInputs.ShortcutTypes) + "]}"), ("source_dwg", Str),
              ("output", Str), ("surface", Str), ("surfaces", StrArr), ("alignments", StrArr), ("include_profiles", "{\"type\":\"boolean\",\"default\":true}")));
    }
}
