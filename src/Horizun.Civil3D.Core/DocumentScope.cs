// -----------------------------------------------------------------------------
// Horizun Civil 3D MCP - which calls may target an OPEN drawing that is not the
// active window.
//
// A read of another open drawing locks THAT document and reads its own
// database in an always-aborted transaction; the active window is never
// changed. Only reads whose code path is bound to the target database (no
// per-document system variables, no Editor, no LayoutManager, no aborted write
// transactions) are listed. Every write still requires the ACTIVE drawing:
// writes depend on the document's undo group, the working database and the
// revision tracking of the active document.
// -----------------------------------------------------------------------------
namespace Horizun.Civil3D.Core;

public static class DocumentScope
{
    private static readonly Dictionary<string, string[]> NonActiveReads = new(StringComparer.Ordinal)
    {
        ["horizun_c3d_document"] = new[] { "info", "object_census", "geo" },
        ["horizun_c3d_query"] = new[] { "list", "get" },
        ["horizun_c3d_styles"] = new[] { "list", "get" },
        // volumes_report is excluded: native volumes use an aborted WRITE transaction.
        ["horizun_c3d_surface"] = new[] { "list", "get", "sample_elevation" },
        ["horizun_c3d_layers"] = new[] { "list", "states_list" },
        ["horizun_c3d_entities"] = new[] { "query", "get" },
        ["horizun_c3d_dimensions"] = new[] { "list" },
        ["horizun_c3d_cad_styles"] = new[] { "list" },
        ["horizun_c3d_blocks"] = new[] { "list", "references" },
        ["horizun_c3d_tables"] = new[] { "list", "get" },
        // list reads the layout dictionary; "current" is null unless the drawing is the working database.
        ["horizun_c3d_layouts"] = new[] { "list" },
        ["horizun_c3d_alignment"] = new[] { "get", "station_offset" },
        ["horizun_c3d_profile"] = new[] { "get", "elevation_at", "check_k" },
        ["horizun_c3d_corridor"] = new[] { "get", "assembly_list" },
        ["horizun_c3d_labels"] = new[] { "list_styles", "list", "get" },
        ["horizun_c3d_pipes"] = new[] { "list" },
        ["horizun_c3d_points"] = new[] { "list", "groups" },
        ["horizun_c3d_cleanup"] = new[] { "drawing_report", "xrefs" },
        // Not listed on purpose: sections (a pending section must never be read read-only - it aborted Civil 3D once),
        // exchange (data shortcuts follow the working project), execute_csharp (escape hatch; scripts can reach any
        // open drawing through DocumentManager themselves) and every write.
    };

    /// <summary>
    /// True when this call may read an open drawing other than the active one. Double gate: the pair is listed
    /// AND the contract says the action is a pure read, so a write can never slip through a list edit.
    /// </summary>
    public static bool AllowsNonActive(string tool, string? action)
    {
        if (action == null || !NonActiveReads.TryGetValue(tool, out var actions) || !actions.Contains(action)) return false;
        var c = Contract.Find(tool);
        return c != null && c.EffectFor(new System.Text.Json.Nodes.JsonObject { ["action"] = action }) == ToolEffect.Read;
    }

    /// <summary>The actions of a tool that may read a non-active drawing (empty when none).</summary>
    public static IReadOnlyList<string> NonActiveActions(string tool) =>
        NonActiveReads.TryGetValue(tool, out var a) ? a.Where(x => AllowsNonActive(tool, x)).ToArray() : Array.Empty<string>();

    /// <summary>Every tool -> actions pair that may read a non-active drawing (published by health).</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> All() =>
        NonActiveReads.Keys.ToDictionary(t => t, NonActiveActions, StringComparer.Ordinal);
}
