// -----------------------------------------------------------------------------
// Horizun Civil 3D MCP - contract entries for the action-based tools of
// PHASE3_PLAN (blocks A-D). Per-action validation lives in *Inputs.cs
// (ToolRules); effects per action are derived from those specs.
// -----------------------------------------------------------------------------
namespace Horizun.Civil3D.Core;

public static partial class Contract
{
    private const string Common =
        "\"target_document\":{\"type\":\"string\",\"description\":\"Drawing name or path. Reads default to the active drawing; the reads listed in horizun_c3d_health bridge.non_active_reads may name another OPEN drawing (read in place, the window is never changed). Required for writes and must be the active drawing.\"}," +
        "\"dry_run\":{\"type\":\"boolean\",\"default\":true,\"description\":\"Writes: true = plan + confirmation_token, nothing changes.\"}," +
        "\"confirmation_token\":{\"type\":\"string\",\"description\":\"Writes: token from the dry run of exactly this request.\"}";

    private const string Pt = "{\"type\":\"object\",\"properties\":{\"x\":{\"type\":\"number\"},\"y\":{\"type\":\"number\"},\"z\":{\"type\":\"number\"}},\"required\":[\"x\",\"y\"],\"additionalProperties\":false}";

    private static string Schema(string actions, string props) =>
        "{\"type\":\"object\",\"properties\":{\"action\":{\"type\":\"string\",\"enum\":[" + actions + "]}," + props + "," + Common +
        "},\"required\":[\"action\"],\"additionalProperties\":false}";

    private static string Enum(IEnumerable<string> keys) => string.Join(",", keys.Select(k => "\"" + k + "\""));

    /// <summary>Touch every *Inputs class so their specs are registered with ToolRules.</summary>
    internal static void EnsureRules()
    {
        _ = RoadInputs.Alignment;
        _ = RoadInputs.Profile;
        _ = RoadInputs.Sections;
        _ = RoadInputs.Corridor;
        _ = AnnotationInputs.Labels;
        _ = CadInputs.Layers;
        _ = CadInputs.Entities;
        _ = CadInputs.Dimensions;
        _ = CadInputs.CadStyles;
        _ = CadInputs.Blocks;
        _ = CadInputs.Tables;
        _ = CadInputs.Layouts;
        _ = CadInputs.Cleanup;
        _ = DataInputs.Pipes;
        _ = DataInputs.Points;
        _ = DataInputs.Exchange;
    }

    private static IEnumerable<ToolContract> BlockTools()
    {
        EnsureRules();
        foreach (var t in RoadTools()) yield return t;
        yield return LabelTool();
        foreach (var t in CadTools()) yield return t;
        foreach (var t in DataTools()) yield return t;
    }

    private static ToolContract Tool(string name, string command, string title, IReadOnlyDictionary<string, ActionSpec> specs, string description, string props,
                                     bool destructive = false) => new()
    {
        Name = name,
        Command = command,
        Title = title,
        Effect = ToolEffect.Read,
        ActionEffects = ToolRules.Effects(specs),
        Destructive = destructive,
        Description = description,
        InputSchemaJson = Schema(Enum(specs.Keys), props),
    };

    private static IEnumerable<ToolContract> RoadTools()
    {
        yield return Tool("horizun_c3d_alignment", "alignment", "Alignments: geometry, station/offset and creation", RoadInputs.Alignment,
            "Alignments. get: geometry entity by entity (type, start/end station and point, length, radius/centre/direction). " +
            "station_offset: XY -> station/offset (points) or station/offset -> XY (stations). Writes (dry run + token + " +
            "re-read): create_from_polyline (polyline handle; add_curves inserts curves between tangents; the polyline is kept " +
            "unless erase_polyline), create_by_pis (PI list with one radius per interior PI, 0 = no curve; re-reads every " +
            "radius), create_offset (parent + signed offset; re-reads the offset distance at sample stations). Site, layer, " +
            "style and label set are resolved by exact name or default to the first available; unknown names are refused.",
            "\"name\":{\"type\":\"string\"},\"handle\":{\"type\":\"string\"},\"new_name\":{\"type\":\"string\"},\"polyline\":{\"type\":\"string\",\"description\":\"Source polyline handle.\"}," +
            "\"add_curves\":{\"type\":\"boolean\",\"default\":false},\"erase_polyline\":{\"type\":\"boolean\",\"default\":false}," +
            "\"pis\":{\"type\":\"array\",\"minItems\":2,\"maxItems\":500,\"items\":" + Pt + "},\"radii\":{\"type\":\"array\",\"items\":{\"type\":\"number\"}}," +
            "\"offset\":{\"type\":\"number\",\"description\":\"create_offset: signed offset (Civil 3D convention: negative = left).\"}," +
            "\"start_station\":{\"type\":\"number\"},\"end_station\":{\"type\":\"number\"}," +
            "\"points\":{\"type\":\"array\",\"maxItems\":10000,\"items\":" + Pt + "}," +
            "\"stations\":{\"type\":\"array\",\"maxItems\":10000,\"items\":{\"type\":\"object\",\"properties\":{\"station\":{\"type\":\"number\"},\"offset\":{\"type\":\"number\"}},\"required\":[\"station\"],\"additionalProperties\":false}}," +
            "\"site\":{\"type\":\"string\"},\"layer\":{\"type\":\"string\"},\"style\":{\"type\":\"string\"},\"label_set\":{\"type\":\"string\"},\"description\":{\"type\":\"string\"}");

        yield return Tool("horizun_c3d_profile", "profile", "Profiles: PVIs, elevations, K checks, creation and profile views", RoadInputs.Profile,
            "Profiles. get: PVIs (station, elevation, grades, curve type/length, K) and entities. elevation_at: elevation and grade at " +
            "stations. check_k: compares each vertical curve's K with YOUR criteria (min_k_crest / min_k_sag) - no design code is " +
            "built in. Writes: create_from_surface (alignment + surface; re-reads elevations against the surface at sample stations), " +
            "create_layout (PVIs with optional symmetric parabola curve_length; re-reads every PVI and curve), create_view (profile " +
            "view at an insert point). Profile names are only unique per alignment: pass alignment to disambiguate.",
            "\"name\":{\"type\":\"string\"},\"handle\":{\"type\":\"string\"},\"alignment\":{\"type\":\"string\"},\"surface\":{\"type\":\"string\"},\"new_name\":{\"type\":\"string\"}," +
            "\"stations\":{\"type\":\"array\",\"maxItems\":10000,\"items\":{\"type\":\"number\"}},\"min_k_crest\":{\"type\":\"number\"},\"min_k_sag\":{\"type\":\"number\"}," +
            "\"offset\":{\"type\":\"number\"},\"pvis\":{\"type\":\"array\",\"minItems\":2,\"maxItems\":500,\"items\":{\"type\":\"object\",\"properties\":{\"station\":{\"type\":\"number\"},\"elevation\":{\"type\":\"number\"},\"curve_length\":{\"type\":\"number\"}},\"required\":[\"station\",\"elevation\"],\"additionalProperties\":false}}," +
            "\"insert\":" + Pt + ",\"band_set\":{\"type\":\"string\"},\"layer\":{\"type\":\"string\"},\"style\":{\"type\":\"string\"},\"label_set\":{\"type\":\"string\"}");

        yield return Tool("horizun_c3d_sections", "sections", "Sample lines, sections and section views", RoadInputs.Sections,
            "Sections. list: sample line groups of an alignment with stations and sampled sources. get_section: the section points " +
            "(offset, elevation) of every sampled source at a station. Writes: create_sample_lines (group, explicit stations or " +
            "interval, left/right widths, surfaces to sample; re-reads stations and widths), create_section_views (a section view " +
            "group for every sample line of the group at an insert point).",
            "\"alignment\":{\"type\":\"string\"},\"group\":{\"type\":\"string\"},\"station\":{\"type\":\"number\"},\"stations\":{\"type\":\"array\",\"maxItems\":2000,\"items\":{\"type\":\"number\"}}," +
            "\"interval\":{\"type\":\"number\"},\"start_station\":{\"type\":\"number\"},\"end_station\":{\"type\":\"number\"},\"left_width\":{\"type\":\"number\"},\"right_width\":{\"type\":\"number\"}," +
            "\"sources\":{\"type\":\"array\",\"maxItems\":50,\"items\":{\"type\":\"string\"},\"description\":\"Surface names to sample.\"},\"insert\":" + Pt);

        yield return Tool("horizun_c3d_corridor", "corridor", "Corridors and assemblies", RoadInputs.Corridor,
            "Corridors. get: baselines (alignment, profile, stations), regions (assembly, stations, frequencies), corridor surfaces. " +
            "assembly_list. Writes: assembly_create (empty assembly at a point), assembly_import (copy an assembly from another DWG - " +
            "the way to bring catalogue/stock assemblies; stock subassembly creation has no public API in 2025 and is refused), " +
            "create (alignment + profile + assembly, station range, frequencies; rebuilds), add_region, rebuild (reports " +
            "out-of-date after), create_surface (link codes and/or feature-line codes). Every change is re-read.",
            "\"name\":{\"type\":\"string\"},\"handle\":{\"type\":\"string\"},\"new_name\":{\"type\":\"string\"},\"alignment\":{\"type\":\"string\"},\"profile\":{\"type\":\"string\"},\"assembly\":{\"type\":\"string\"}," +
            "\"start_station\":{\"type\":\"number\"},\"end_station\":{\"type\":\"number\"},\"baseline_index\":{\"type\":\"integer\",\"minimum\":0}," +
            "\"frequency\":{\"type\":\"object\",\"properties\":{\"tangents\":{\"type\":\"number\"},\"curves\":{\"type\":\"number\"},\"spirals\":{\"type\":\"number\"},\"profile_curves\":{\"type\":\"number\"}},\"additionalProperties\":false}," +
            "\"rebuild\":{\"type\":\"boolean\",\"default\":true},\"insert\":" + Pt + ",\"assembly_type\":{\"type\":\"string\"}," +
            "\"source_dwg\":{\"type\":\"string\"},\"source_assembly\":{\"type\":\"string\"},\"surface_name\":{\"type\":\"string\"}," +
            "\"link_codes\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}},\"feature_line_codes\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}}," +
            "\"breaklines\":{\"type\":\"boolean\",\"default\":true,\"description\":\"create_surface: add link codes as breaklines.\"},\"style\":{\"type\":\"string\"}");
    }

    private static ToolContract LabelTool() => Tool("horizun_c3d_labels", "labels", "Civil 3D labels: alignment, surface, profile, notes, segments", AnnotationInputs.Labels,
        "Civil 3D object labels (they stay live with the object). Reads: list_styles (label/marker styles of a kind), list (labels " +
        "attached to an alignment/surface/profile view/entity, or every label in model space), get (one label: type, style, " +
        "feature, location, text components and overrides). Writes (dry run + token + re-read of each label's feature, style and " +
        "position): alignment_stations (major or minor station labels every increment), alignment_geometry (a label on every " +
        "tangent and curve), station_offset, surface_spot (spot elevations), surface_slope (one-point or two-point), " +
        "contour_labels (label line across contours), profile_pvis (grade-break labels in a profile view), station_elevation " +
        "(profile view), note (note label with text), segment (general line/curve label on a polyline, line or arc at a ratio), " +
        "set_text (text override of a label component), erase (labels only; any other object is refused). Styles are " +
        "resolved by exact name per kind or default to the first available.",
        "\"kind\":{\"type\":\"string\",\"enum\":[" + Enum(AnnotationInputs.StyleKinds) + "]},\"alignment\":{\"type\":\"string\"},\"surface\":{\"type\":\"string\"}," +
        "\"profile_view\":{\"type\":\"string\"},\"profile\":{\"type\":\"string\"},\"entity\":{\"type\":\"string\",\"description\":\"Handle of a polyline/line/arc (segment) or of the labelled object (list).\"}," +
        "\"limit\":{\"type\":\"integer\",\"minimum\":1,\"default\":500},\"handle\":{\"type\":\"string\"},\"handles\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}}," +
        "\"increment\":{\"type\":\"number\"},\"major\":{\"type\":\"boolean\",\"default\":true},\"style\":{\"type\":\"string\"},\"line_style\":{\"type\":\"string\"},\"curve_style\":{\"type\":\"string\"}," +
        "\"marker\":{\"type\":\"string\",\"description\":\"Marker style name.\"},\"points\":{\"type\":\"array\",\"maxItems\":500,\"items\":" + Pt + "}," +
        "\"segments\":{\"type\":\"array\",\"maxItems\":500,\"items\":{\"type\":\"object\",\"properties\":{\"from\":" + Pt + ",\"to\":" + Pt + "},\"required\":[\"from\",\"to\"],\"additionalProperties\":false}}," +
        "\"line\":{\"type\":\"array\",\"minItems\":2,\"maxItems\":100,\"items\":" + Pt + "}," +
        "\"items\":{\"type\":\"array\",\"maxItems\":500,\"items\":{\"type\":\"object\",\"properties\":{\"station\":{\"type\":\"number\"},\"elevation\":{\"type\":\"number\"}},\"required\":[\"station\",\"elevation\"],\"additionalProperties\":false}}," +
        "\"location\":" + Pt + ",\"text\":{\"type\":\"string\"},\"ratio\":{\"type\":\"number\",\"minimum\":0,\"maximum\":1,\"default\":0.5},\"component\":{\"type\":\"integer\",\"minimum\":0,\"default\":0}",
        destructive: true);
}
