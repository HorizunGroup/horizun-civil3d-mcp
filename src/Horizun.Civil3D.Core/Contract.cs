// -----------------------------------------------------------------------------
// Horizun Civil 3D MCP - THE contract between the MCP server and the plug-in.
//
// One source file, compiled into both halves. Every tool the server advertises
// is declared here with its input schema and its effect class; the plug-in
// registers one command per non-server tool and refuses to start if the two
// lists differ. Contract.Hash is published in the discovery file and checked by
// the server before every call, so a server and a plug-in from different builds
// can never talk past each other.
//
// Catalogue rule: few tools with actions, not hundreds of single-purpose tools.
// Model tool selection degrades with large catalogues.
// -----------------------------------------------------------------------------
using System.Text.Json.Nodes;

namespace Horizun.Civil3D.Core;

/// <summary>What a call can change. Permission profiles are expressed over these.</summary>
public enum ToolEffect
{
    /// <summary>Reads the drawing; never modifies it.</summary>
    Read,
    /// <summary>Changes bridge/session state only (which Civil 3D is targeted).</summary>
    HostState,
    /// <summary>Typed, reversible drawing edits (one UNDO step each).</summary>
    SafeWrite,
    /// <summary>Saving, exporting, opening documents, data shortcuts.</summary>
    FullWrite,
    /// <summary>Arbitrary code inside Civil 3D.</summary>
    UnsafeCode,
}

public sealed class ToolContract
{
    public required string Name { get; init; }
    /// <summary>Plug-in command name; null when the MCP server answers the tool itself.</summary>
    public string? Command { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string InputSchemaJson { get; init; }
    public ToolEffect Effect { get; init; }
    /// <summary>Per-action effect overrides (e.g. document.save is FullWrite).</summary>
    public IReadOnlyDictionary<string, ToolEffect>? ActionEffects { get; init; }
    public bool Destructive { get; init; }

    private JsonObject? _schema;
    public JsonObject InputSchema => _schema ??= (JsonObject)JsonNode.Parse(InputSchemaJson)!;

    public ToolEffect EffectFor(JsonObject? args)
    {
        var action = Hz.Str(args, "action");
        if (action != null && ActionEffects != null && ActionEffects.TryGetValue(action, out var e)) return e;
        return Effect;
    }

    /// <summary>The highest effect any action of this tool can have (for tools/list annotations).</summary>
    public ToolEffect MaxEffect =>
        ActionEffects == null || ActionEffects.Count == 0 ? Effect : (ToolEffect)Math.Max((int)Effect, ActionEffects.Values.Max(v => (int)v));
}

public static partial class Contract
{
    public const int ProtocolVersion = 1;
    public const int MaxRequestBytes = 4 * 1024 * 1024;
    public const int MaxReplyBytes = 32 * 1024 * 1024;
    public const string Prefix = "horizun_c3d_";

    private const string TargetDocumentProp =
        "\"target_document\": {\"type\": \"string\", \"description\": \"Drawing name (e.g. 'Site.dwg') or full path. Defaults to the ACTIVE drawing for reads; REQUIRED for writes and must equal the active drawing.\"}";

    public static readonly IReadOnlyList<ToolContract> All = new List<ToolContract>
    {
        new()
        {
            Name = "horizun_c3d_health",
            Command = "health",
            Title = "Civil 3D bridge health",
            Effect = ToolEffect.Read,
            Description =
                "START HERE. Reports whether a Civil 3D instance is connected, its year and ACADVER, the plug-in " +
                "version and contract hash, the ACTIVE drawing (name, path, read-only, unsaved changes), its units " +
                "(linear, area, volume, angular) and coordinate system, whether Civil 3D is busy (active command or " +
                "modal dialog), the queue state and the permission profile in force. Read-only.",
            InputSchemaJson = """
            {"type":"object","properties":{},"additionalProperties":false}
            """,
        },
        new()
        {
            Name = "horizun_c3d_target",
            Command = null,
            Title = "Choose the Civil 3D instance",
            Effect = ToolEffect.HostState,
            Description =
                "Lists every running Civil 3D that published a Horizun bridge (year, process id, plug-in version, " +
                "contract match) and optionally selects which one this session talks to. With no arguments it only " +
                "lists. Pass year (e.g. 2025) or pid to select; year='auto' clears the selection. Never touches a drawing.",
            InputSchemaJson = """
            {"type":"object","properties":{
              "year":{"type":["integer","string"],"description":"Civil 3D year to target, or 'auto'."},
              "pid":{"type":"integer","description":"Exact acad.exe process id to target."}
            },"additionalProperties":false}
            """,
        },
        new()
        {
            Name = "horizun_c3d_document",
            Command = "document",
            Title = "Drawing information, census and save",
            Effect = ToolEffect.Read,
            ActionEffects = new Dictionary<string, ToolEffect> { ["save"] = ToolEffect.FullWrite, ["undo_last"] = ToolEffect.SafeWrite },
            Description =
                "Drawing-level operations. action=info: name, path, read-only, unsaved changes, units, coordinate " +
                "system, external references. action=list_open: every open drawing and which is active. " +
                "action=object_census: count of every Civil 3D object type (surfaces by kind, alignments, profiles, " +
                "corridors, feature lines, sites, parcels, pipe networks, COGO points, point groups, sample line " +
                "groups...), how many are data-shortcut references (not editable here) and xrefs. action=geo (read-only): " +
                "georeference and orientation of the ACTIVE drawing - AutoCAD GeoLocation (coordinate system, design and " +
                "reference points, north direction and vector, scale factor, units, type of coordinates, design point in " +
                "lon/lat), the system variables NORTHDIRECTION, VIEWTWIST, WORLDUCS, UCSNAME, UCSORG/UCSXDIR/UCSYDIR, the " +
                "current UCS, view twist and active viewport, Civil 3D's coordinate system and transformation settings " +
                "(rotation to grid north, grid scale factor, reference/rotation points) and the grid convergence at the " +
                "design point, computed through the drawing's own transform. Use it when north, the ViewCube or bearings " +
                "do not agree. action=save: saves " +
                "the active drawing to its own path; dry_run defaults to true and returns the plan plus a single-use " +
                "confirmation_token; apply with dry_run=false and that token. Any subsequent drawing edit, variable " +
                "or view change invalidates the save plan, even when DBMOD flags are unchanged. Save requires the full_write profile. " +
                "action=undo_last: undoes the LAST Horizun write as one Civil 3D UNDO step, only while the drawing has not changed " +
                "since that write (any user edit, other write or save makes it refuse - use U in Civil 3D then); dry run + token; " +
                "re-checks that objects the write created are gone.",
            InputSchemaJson = """
            {"type":"object","properties":{
              "action":{"type":"string","enum":["info","list_open","object_census","geo","save","undo_last"]},
              __TARGET__,
              "dry_run":{"type":"boolean","default":true,"description":"save / undo_last. true = return the plan and a confirmation_token, change nothing."},
              "confirmation_token":{"type":"string","description":"save / undo_last. Token from the dry run of exactly this request."}
            },"required":["action"],"additionalProperties":false}
            """.Replace("__TARGET__", TargetDocumentProp),
        },
        new()
        {
            Name = "horizun_c3d_query",
            Command = "query",
            Title = "Find and read Civil 3D objects",
            Effect = ToolEffect.Read,
            Description =
                "Generic paginated query over Civil 3D objects of one type. action=list filters by name (wildcards * ?), " +
                "layer and style and returns handle, name, layer, style, whether it is a data-shortcut reference or " +
                "otherwise not editable, plus type-specific fields (surface kind and out-of-date, alignment length and " +
                "stations, profile type, feature-line site and vertex count...). action=get returns one object in full " +
                "by handle, or by name within the type; an ambiguous or missing name is refused with the candidates. " +
                "Surfaces include statistics (elevation min/max/mean, points, triangles, 2D/3D area). Values that cannot " +
                "be read are null with a reason, never 0. Both list and get include a units block for raw drawing " +
                "values and Civil 3D display units. Read-only.",
            InputSchemaJson = """
            {"type":"object","properties":{
              "action":{"type":"string","enum":["list","get"]},
              "type":{"type":"string","enum":["surface","alignment","profile","profile_view","corridor","feature_line","site","parcel","pipe_network","pipe","structure","cogo_point","point_group","sample_line_group","assembly"]},
              "name":{"type":"string","description":"list: wildcard filter. get: exact name (case-insensitive)."},
              "handle":{"type":"string","description":"get: object handle (hex), e.g. '2A3F'."},
              "layer":{"type":"string","description":"list: layer wildcard filter."},
              "style":{"type":"string","description":"list: style-name wildcard filter."},
              "fields":{"type":"array","items":{"type":"string"},"description":"list: return only these fields (handle and name always included)."},
              "offset":{"type":"integer","minimum":0,"default":0},
              "limit":{"type":"integer","minimum":1,"maximum":1000,"default":100},
              __TARGET__
            },"required":["action"],"additionalProperties":false}
            """.Replace("__TARGET__", TargetDocumentProp),
        },
        new()
        {
            Name = "horizun_c3d_styles",
            Command = "styles",
            Title = "List and inspect object styles",
            Effect = ToolEffect.Read,
            Description =
                "Styles available in the drawing for one object type. action=list returns every style with how many " +
                "objects use it. action=get returns one style by name with its metadata and the exact objects that use " +
                "it - check this before changing a style, because editing a style changes EVERY object that uses it. " +
                "Read-only.",
            InputSchemaJson = """
            {"type":"object","properties":{
              "action":{"type":"string","enum":["list","get"]},
              "object_type":{"type":"string","enum":["surface","alignment","profile","profile_view","feature_line","corridor","pipe","structure","point","parcel","assembly","sample_line","section","section_view","grading","code_set","marker","link","shape","slope_pattern","view_frame","match_line","label_surface_contour","label_surface_spot_elevation","label_surface_slope","label_surface_watershed","label_general_note","label_general_line","label_general_curve"]},
              "name":{"type":"string","description":"get: exact style name (case-insensitive)."},
              __TARGET__
            },"required":["action","object_type"],"additionalProperties":false}
            """.Replace("__TARGET__", TargetDocumentProp),
        },
        new()
        {
            Name = "horizun_c3d_surface",
            Command = "surface",
            Title = "Surface information, sampling, volumes and verified edits",
            Effect = ToolEffect.Read,
            ActionEffects = SurfaceInputs.WriteActions.ToDictionary(a => a, _ => ToolEffect.SafeWrite),
            Description =
                "Surface actions. list/get return metadata and native statistics. get defaults to volume-surface " +
                "isopaca statistics: native maximum cut/fill and explicitly estimated grid areas, area-weighted " +
                "means, median and P90, reconciled with Civil volumes. list defaults to no grid statistics. " +
                "sample_elevation samples XY points or a line (WCS drawing coordinates); outside/hole points " +
                "return null with reason, never zero. volumes_report reads a volume surface, or computes base vs " +
                "comparison in a temporary ALWAYS-ABORTED transaction; factors are calculated without editing " +
                "the surface. Out-of-date surfaces are reported, never silently rebuilt. Writes: rename, set_style, " +
                "duplicate_style, create_tin, create_volume, rebuild, add_data (TIN vertices, one breakline group, one " +
                "boundary group; vertices and standard-breakline vertices are re-read as surface elevations), paste " +
                "(ordered sources, re-read as paste operations), apply_elevation_analysis / apply_slope_analysis " +
                "(bands by equal count, fixed interval with break_at, explicit ranges or recolor; slope in PERCENT; " +
                "re-reads the stored bands and reports grid-estimated area - and volume for volume surfaces - per " +
                "band; warns when the surface style does not display the analysis), style_display (component " +
                "visibility/colour/layer of a surface style; refused for a shared style unless " +
                "allow_shared_style=true; duplicate it first to change one surface). Writes require target_document, default " +
                "dry_run=true, a single-use drawing/revision/request/plan-bound confirmation token, one batch " +
                "commit and a new-transaction re-read. Missing/ambiguous targets, references and locks are " +
                "refused before changing anything. Existing names are never overwritten. Style plans show " +
                "all current users. No drawing is saved by these actions.",
            InputSchemaJson = """
            {"type":"object","properties":{
              "action":{"type":"string","enum":["list","get","volumes_report","sample_elevation","rename","set_style","duplicate_style","create_tin","create_volume","rebuild","add_data","paste","apply_elevation_analysis","apply_slope_analysis","style_display"]},
              "name":{"type":"string","description":"list: wildcard. Other reads/edits: one exact surface name."},
              "handle":{"type":"string","description":"One exact surface handle; mutually exclusive with name/names."},
              "names":{"type":"array","items":{"type":"string"},"minItems":1,"maxItems":100,"description":"Exact batch selection; all names resolve before work. No duplicates."},
              "style":{"type":"string","description":"list: wildcard filter; writes: exact surface-style name (source style for duplicate_style)."},
              "layer":{"type":"string","description":"list: wildcard; create_tin/create_volume: existing unlocked destination layer."},
              "new_name":{"type":"string","description":"rename or creation: unique destination name; existing surfaces/styles are never overwritten."},
              "description":{"type":"string","description":"create_tin/create_volume only."},
              "base":{"type":"string","description":"Exact base surface name for volumes_report/create_volume."},
              "comparison":{"type":"string","description":"Exact comparison surface name; must differ from base."},
              "cut_factor":{"type":"number","description":"volumes_report only, finite >0; calculated without changing native factors."},
              "fill_factor":{"type":"number","description":"volumes_report only, finite >0."},
              "include_isopaca_statistics":{"type":"boolean","description":"get defaults true, list false; only volume surfaces have cut/fill statistics."},
              "grid_spacing":{"type":"number","description":"Optional finite >0 midpoint-grid spacing in drawing units; rejects grids exceeding max_samples."},
              "max_samples":{"type":"integer","minimum":1,"maximum":100000,"default":10000,"description":"Grid-evaluation budget shared across the entire list/get call."},
              "points":{"type":"array","minItems":1,"maxItems":10000,"items":{"type":"object","properties":{"x":{"type":"number"},"y":{"type":"number"}},"required":["x","y"],"additionalProperties":false}},
              "line":{"type":"object","properties":{
                "start":{"type":"object","properties":{"x":{"type":"number"},"y":{"type":"number"}},"required":["x","y"],"additionalProperties":false},
                "end":{"type":"object","properties":{"x":{"type":"number"},"y":{"type":"number"}},"required":["x","y"],"additionalProperties":false}
              },"required":["start","end"],"additionalProperties":false},
              "step":{"type":"number","description":"sample_elevation line mode: finite >0, at most 10000 points including both ends."},
              "vertices":{"type":"array","minItems":1,"maxItems":10000,"description":"add_data: TIN vertices in drawing WCS; each is re-read as an exact surface elevation after commit.","items":{"type":"object","properties":{"x":{"type":"number"},"y":{"type":"number"},"z":{"type":"number"}},"required":["x","y","z"],"additionalProperties":false}},
              "breaklines":{"type":"object","description":"add_data: ONE breakline group from existing drawing objects (3D polylines, polylines, feature lines).","properties":{
                "handles":{"type":"array","minItems":1,"maxItems":500,"items":{"type":"string"}},
                "kind":{"type":"string","enum":["standard","proximity","non_destructive"],"default":"standard"},
                "description":{"type":"string"},
                "mid_ordinate":{"type":"number","default":1.0,"description":"Arc tessellation distance, >0."},
                "max_distance":{"type":"number","default":0,"description":"standard only: supplementing distance, 0 = none."},
                "weeding_distance":{"type":"number","default":0,"description":"standard only: 0 = keep every vertex."},
                "weeding_angle":{"type":"number","default":0,"description":"standard only: 0 = keep every vertex."}
              },"required":["handles"],"additionalProperties":false},
              "boundaries":{"type":"object","description":"add_data: ONE boundary group from existing CLOSED drawing objects.","properties":{
                "handles":{"type":"array","minItems":1,"maxItems":500,"items":{"type":"string"}},
                "kind":{"type":"string","enum":["outer","hide","show","data_clip"]},
                "name":{"type":"string"},
                "non_destructive":{"type":"boolean","default":true},
                "mid_ordinate":{"type":"number","default":1.0}
              },"required":["handles","kind"],"additionalProperties":false},
              "sources":{"type":"array","minItems":1,"maxItems":50,"items":{"type":"string"},"description":"paste: exact source surface names, pasted IN THIS ORDER (later ones win where they overlap)."},
              "rebuild":{"type":"boolean","default":true,"description":"add_data/paste: rebuild the target after adding so the re-read sees the new definition."},
              "mode":{"type":"string","enum":["equal","step","ranges","recolor"],"description":"apply_*_analysis: equal (number_of_ranges), step (interval + break_at), ranges (explicit list), recolor (keep ranges, new colours)."},
              "number_of_ranges":{"type":"integer","minimum":1,"maximum":64,"description":"mode=equal."},
              "interval":{"type":"number","description":"mode=step: band width (elevation: drawing units; slope: percent)."},
              "break_at":{"type":"number","default":0,"description":"mode=step: a band boundary falls exactly here (0 keeps cut and fill apart)."},
              "ranges":{"type":"array","minItems":1,"maxItems":64,"description":"mode=ranges: ascending, non-overlapping bands. Elevation analysis in drawing units (depths for volume surfaces); slope analysis in PERCENT.","items":{"type":"object","properties":{"min":{"type":"number"},"max":{"type":"number"},"color":{"type":["integer","string"],"description":"ACI 1-255 or #RRGGBB"}},"required":["min","max","color"],"additionalProperties":false}},
              "colors":{"type":"array","minItems":1,"maxItems":64,"items":{"type":["integer","string"]},"description":"equal/step/recolor: one colour per band (ACI 1-255 or #RRGGBB)."},
              "color_scheme":{"type":"string","enum":["rainbow","cutfill","reds","blues","greens","grays","land"],"description":"equal/step/recolor: generic ramp when no colors are given (default rainbow; cutfill splits at break_at)."},
              "view":{"type":"string","enum":["plan","model","both"],"default":"plan","description":"style_display: which display view to edit."},
              "components":{"type":"object","description":"style_display: {component: {visible?, color?, layer?}}. Components: points, triangles, border, major_contour, minor_contour, user_contours, gridded, directions, elevations, slopes, slope_arrows, watersheds."},
              "allow_shared_style":{"type":"boolean","default":false,"description":"style_display: required true when the style is used by more than one object (the edit changes all of them)."},
              "offset":{"type":"integer","minimum":0,"default":0},
              "limit":{"type":"integer","minimum":1,"maximum":1000,"default":100},
              __TARGET__,
              "dry_run":{"type":"boolean","default":true},
              "confirmation_token":{"type":"string"}
            },"required":["action"],"additionalProperties":false}
            """.Replace("__TARGET__", TargetDocumentProp),
        },
        new()
        {
            Name = "horizun_c3d_grading",
            Command = "grading",
            Title = "Geometric grading (3D polylines + TIN breaklines)",
            Effect = ToolEffect.SafeWrite,
            Description =
                "Civil 3D has NO public API to create native gradings; this builds the equivalent GEOMETRICALLY and says " +
                "so (validated against a native grading: median dz 0.000 m, volume -0.09 %). action=create_geometric: from " +
                "a closed polyline / 3D polyline / feature line (source handle), apply outer steps in order (offset " +
                "{dist, dz}; grade_to_surface {slope | cut_slope, fill_slope} as H:V toward the target surface, with fans " +
                "at convex corners) and inner steps (offset; grade_to_depth {depth, slope}; grade_to_elevation " +
                "{elevation, slope}). Creates one closed 3D polyline per line on layer (default HZ-GRADING) and a NEW TIN " +
                "surface named name with every line as a standard breakline and the outermost line as outer boundary. " +
                "Dry run returns every computed line (points, z range, area) and failed daylight rays. Re-read after " +
                "commit: breakline count, boundary, and the TIN elevation at the line vertices. volume_against (surface " +
                "name) adds cut/fill of the new TIN against that surface from a never-committed transient volume. " +
                "Native grading creation is refused by name: use this.",
            InputSchemaJson = """
            {"type":"object","properties":{
              "action":{"type":"string","enum":["create_geometric"]},
              "source":{"type":"string","description":"Handle of a CLOSED polyline, 3D polyline or feature line (the footprint)."},
              "surface":{"type":"string","description":"Target terrain for grade_to_surface steps."},
              "name":{"type":"string","description":"New TIN surface name (never overwrites)."},
              "outer":{"type":"array","maxItems":20,"items":{"type":"object","properties":{"type":{"type":"string","enum":["offset","grade_to_surface"]},"dist":{"type":"number"},"dz":{"type":"number"},"slope":{"type":"number"},"cut_slope":{"type":"number"},"fill_slope":{"type":"number"}},"required":["type"],"additionalProperties":false}},
              "inner":{"type":"array","maxItems":20,"items":{"type":"object","properties":{"type":{"type":"string","enum":["offset","grade_to_depth","grade_to_elevation"]},"dist":{"type":"number"},"dz":{"type":"number"},"depth":{"type":"number"},"slope":{"type":"number"},"elevation":{"type":"number"}},"required":["type"],"additionalProperties":false}},
              "densify":{"type":"number","default":0.5,"description":"Ray spacing along the footprint, 0.05-10 drawing units."},
              "layer":{"type":"string","default":"HZ-GRADING","description":"Layer for the 3D polylines (created if missing)."},
              "style":{"type":"string","description":"Surface style for the new TIN (default: the target surface's style, else the first style)."},
              "volume_against":{"type":"string","description":"Optional surface to report cut/fill against (transient, never committed)."},
              __TARGET__,
              "dry_run":{"type":"boolean","default":true},
              "confirmation_token":{"type":"string"}
            },"required":["action","source","name"],"additionalProperties":false}
            """.Replace("__TARGET__", TargetDocumentProp),
        },
        new()
        {
            Name = "horizun_c3d_feature_line",
            Command = "feature_line",
            Title = "Create and edit feature lines",
            Effect = ToolEffect.SafeWrite,
            Description =
                "Feature-line edits with dry run, single-use token and re-read. action=create_from_polyline: one feature " +
                "line per polyline / 3D polyline handle with the given unique names, in an existing site or siteless; " +
                "re-reads vertex XY (and Z for 3D sources). action=set_elevations: mode=from_surface (surface, optional " +
                "insert_intermediate grade breaks; every vertex re-read against the surface), constant (elevation) or " +
                "points ([{index, z}] by position in the feature line's points). action=rename. action=export_polyline3d: " +
                "a 3D polyline with the feature line's points on an existing layer. Feature lines that are references, on " +
                "locked layers or owned by gradings are refused or flagged before any change.",
            InputSchemaJson = """
            {"type":"object","properties":{
              "action":{"type":"string","enum":["create_from_polyline","set_elevations","rename","export_polyline3d"]},
              "handles":{"type":"array","minItems":1,"maxItems":100,"items":{"type":"string"},"description":"create_from_polyline: source polyline handles."},
              "names":{"type":"array","minItems":1,"maxItems":100,"items":{"type":"string"},"description":"create_from_polyline: one unique name per handle."},
              "site":{"type":"string","description":"create_from_polyline: existing site name; omit for siteless."},
              "name":{"type":"string","description":"Target feature line name."},
              "handle":{"type":"string","description":"Target feature line handle."},
              "mode":{"type":"string","enum":["from_surface","constant","points"]},
              "surface":{"type":"string","description":"set_elevations from_surface."},
              "insert_intermediate":{"type":"boolean","default":false,"description":"from_surface: insert intermediate grade-break points."},
              "elevation":{"type":"number","description":"set_elevations constant."},
              "points":{"type":"array","minItems":1,"maxItems":10000,"items":{"type":"object","properties":{"index":{"type":"integer","minimum":0},"z":{"type":"number"}},"required":["index","z"],"additionalProperties":false}},
              "new_name":{"type":"string","description":"rename."},
              "layer":{"type":"string","description":"export_polyline3d: existing layer (default: the feature line's layer)."},
              __TARGET__,
              "dry_run":{"type":"boolean","default":true},
              "confirmation_token":{"type":"string"}
            },"required":["action"],"additionalProperties":false}
            """.Replace("__TARGET__", TargetDocumentProp),
        },
        new()
        {
            Name = "horizun_c3d_execute_csharp",
            Command = "execute_csharp",
            Title = "Run C# inside Civil 3D (escape hatch, off by default)",
            Effect = ToolEffect.UnsafeCode,
            Destructive = true,
            Description =
                "ESCAPE HATCH - prefer a typed tool whenever one covers the job. Compiles and runs C# (Roslyn script) inside " +
                "Civil 3D. OFF by default: needs \"permission_profile\": \"unsafe_code\" AND \"enable_execute_csharp\": true " +
                "in the owner's settings.json. mode=query (default): the transaction is ALWAYS aborted - nothing persists. " +
                "mode=execute: the transaction commits as one UNDO step. Globals: doc, civilDoc, db, tr (open " +
                "transaction), args (JsonObject), Print(object), Surface(name), Open<T>(ObjectId). The script's return " +
                "value is serialised to JSON. There is NO dry run and NO host verification: results are SELF-REPORTED - " +
                "re-read with typed tools (horizun_c3d_query / horizun_c3d_surface) before trusting a write.",
            InputSchemaJson = """
            {"type":"object","properties":{
              "code":{"type":"string","description":"C# script body; use 'return value;' to return a result."},
              "mode":{"type":"string","enum":["query","execute"],"default":"query"},
              "args":{"type":"object","description":"Passed to the script as the global 'args'."},
              __TARGET__
            },"required":["code"],"additionalProperties":false}
            """.Replace("__TARGET__", TargetDocumentProp),
        },
        new()
        {
            Name = "horizun_c3d_probe",
            Command = "probe",
            Title = "Probe the live Civil 3D API",
            Effect = ToolEffect.Read,
            Description =
                "Developer tool. Dumps, by reflection inside the running Civil 3D, the REAL public signatures " +
                "(methods, properties, constructors, enum values) of the named types from AeccDbMgd, AecBaseMgd, " +
                "acdbmgd, acmgd or accoremgd, or finds types by name fragment. Use before writing code against an API " +
                "you have not confirmed in this Civil 3D year. The dump is also saved under the Horizun data folder. " +
                "Does not touch the drawing.",
            InputSchemaJson = """
            {"type":"object","properties":{
              "types":{"type":"array","items":{"type":"string"},"description":"Full type names, e.g. 'Autodesk.Civil.DatabaseServices.TinSurface'."},
              "find":{"type":"string","description":"Name fragment to search for instead of dumping types."},
              "assembly":{"type":"string","enum":["AeccDbMgd","AecBaseMgd","acdbmgd","acmgd","accoremgd","AeccPressurePipesMgd"],"default":"AeccDbMgd"}
            },"additionalProperties":false}
            """,
        },
    }.Concat(BlockTools()).ToList();

    public static ToolContract? Find(string name) => All.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.Ordinal));

    public static ToolContract? FindByCommand(string command) =>
        All.FirstOrDefault(c => c.Command != null && string.Equals(c.Command, command, StringComparison.Ordinal));

    public static IEnumerable<string> PluginCommands => All.Where(c => c.Command != null).Select(c => c.Command!);

    private static string? _hash;

    /// <summary>
    /// SHA-256 (first 12 bytes, hex) over the protocol version, the size limits and
    /// every contract sorted by name: name, command, description, effects, canonical
    /// input schema. Any change to what a tool means changes the hash.
    /// </summary>
    public static string Hash => _hash ??= ComputeHash();

    private static string ComputeHash()
    {
        var root = new JsonObject
        {
            ["protocol"] = ProtocolVersion,
            ["max_request"] = MaxRequestBytes,
            ["max_reply"] = MaxReplyBytes,
        };
        var tools = new JsonArray();
        foreach (var c in All.OrderBy(c => c.Name, StringComparer.Ordinal))
        {
            var effects = new JsonObject();
            if (c.ActionEffects != null)
                foreach (var kv in c.ActionEffects.OrderBy(k => k.Key, StringComparer.Ordinal))
                    effects[kv.Key] = kv.Value.ToString();
            tools.Add(new JsonObject
            {
                ["name"] = c.Name,
                ["command"] = c.Command,
                ["description"] = c.Description,
                ["effect"] = c.Effect.ToString(),
                ["action_effects"] = effects,
                ["destructive"] = c.Destructive,
                ["schema"] = JsonNode.Parse(Hz.Canonical(c.InputSchema)),
            });
        }
        root["tools"] = tools;
        return Hz.Sha256Hex(Hz.Canonical(root))[..24];
    }
}
