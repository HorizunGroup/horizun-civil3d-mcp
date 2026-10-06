// -----------------------------------------------------------------------------
// Horizun Civil 3D MCP - what the client model is told at initialize.
// -----------------------------------------------------------------------------
namespace Horizun.Civil3D.Server;

internal static class Instructions
{
    public const string Text =
        "Horizun Civil 3D MCP - the bridge between this client and a running Autodesk Civil 3D.\n\n" +
        "THE CONTRACT: typed commands never report work they did not verify. Every typed write is re-read from the drawing in a new " +
        "transaction after the commit; if what is read back is not what was requested, the call is an error even though " +
        "nothing threw. Values that cannot be read are null with a reason, never 0.\n\n" +
        "HOW TO WORK:\n" +
        "1. Start with horizun_c3d_health: it names the active drawing, its units (linear, area, volume) and coordinate " +
        "system, and whether Civil 3D is busy. Report numbers WITH those units. Use horizun_c3d_capabilities to " +
        "discover declared actions, schemas and current permission checks; this catalog does not prove live host support. " +
        "Use horizun_c3d_audit for units, xrefs, stale/invalid references and out-of-date surfaces/corridors.\n" +
        "2. Resolve objects with horizun_c3d_query and styles with horizun_c3d_styles before any write. Never guess a " +
        "name: a missing or ambiguous name is refused with the candidates - pick from them or ask the user.\n" +
        "3. Writes default to dry_run=true: read the plan, then apply with dry_run=false and the confirmation_token " +
        "(single use, 10 minutes, bound to this drawing, this request and this plan). Writes must name target_document.\n" +
        "4. Objects that are data-shortcut references or on locked layers are reported not editable; edit them in their " +
        "source drawing.\n" +
        "5. If Civil 3D is busy (a command or dialog is open), the call is refused and NOTHING RAN. The bridge never " +
        "cancels the user's work; ask the user to finish it.\n" +
        "6. Remind the user to save after a block of changes (horizun_c3d_document action=save needs the full_write " +
        "profile).\n" +
        "7. Never drive the user's screen, mouse or keyboard to work around a missing tool. If a capability has no tool, " +
        "say so plainly.\n" +
        "8. Use horizun_c3d_surface for typed surface operations. Grid areas, means and percentiles are explicitly " +
        "estimated: keep their sampling/reconciliation notes. Native volume reads use an always-aborted transaction. " +
        "Surface reads and writes are live-verified on Civil 3D 2025 against a deterministic test drawing (46/46). " +
        "Automatic undo_last is disabled; use Civil 3D native UNDO manually and inspect the result. Analyses take legends as parameters (slope always in %); a " +
        "style used by several surfaces is not edited unless allow_shared_style=true - duplicate it to change one.\n" +
        "9. Gradings: native Civil 3D gradings have NO public API. horizun_c3d_grading create_geometric builds the " +
        "equivalent with 3D polylines + TIN breaklines (validated against native). Say it is geometric. Read the dry-run " +
        "lines and failed daylight rays before applying; use volume_against for cut/fill.\n" +
        "10. Feature lines: horizun_c3d_feature_line (create from polylines with unique names, elevations from a surface / " +
        "constant / per point, rename, export to 3D polyline).\n" +
        "11. horizun_c3d_execute_csharp is an escape hatch, off unless the owner enables it. Prefer typed tools; its " +
        "results are self-reported - re-read them with typed tools before reporting a change as done. Neither query nor execute " +
        "is sandboxed: query aborts only the supplied transaction and may still persist other effects. Permission profiles are " +
        "shared by every Civil 3D instance of this Windows user; selecting an instance does not isolate those permissions.\n" +
        "12. Roads: horizun_c3d_alignment (from polyline, by PIs with radii, offset; station/offset both ways), " +
        "horizun_c3d_profile (from surface, layout PVIs with curves, K check against YOUR criteria, profile views), " +
        "horizun_c3d_sections (sample lines, sections, section views), horizun_c3d_corridor (assemblies - import complete " +
        "ones from a DWG, stock subassemblies have no API -, corridor, regions, rebuild, corridor surfaces). Typical order: " +
        "alignment -> profile -> assembly -> corridor -> corridor surface -> sample lines -> section views.\n" +
        "13. Civil labels: horizun_c3d_labels (station, geometry, station/offset, spot and slope, contours, PVIs, " +
        "station/elevation, notes, segment labels; labels may go on data-shortcut references). Call list_styles first " +
        "when the user names a style.\n" +
        "14. AutoCAD inside Civil 3D: horizun_c3d_layers, horizun_c3d_entities (query/draw/edit/transform), " +
        "horizun_c3d_dimensions (re-reads the measurement), horizun_c3d_cad_styles, horizun_c3d_blocks (attributes for " +
        "title blocks), horizun_c3d_tables, horizun_c3d_layouts (viewports, page setups, PDF), horizun_c3d_cleanup " +
        "(purge, drawing report, standards check). Erase, layout delete, plot_pdf, purge, point erase and shortcut publish " +
        "as well as CSV, LandXML and DWG file exports are FULL WRITE: under the default profile they are refused - tell " +
        "the user how to enable full_write.\n" +
        "15. Networks and data: horizun_c3d_pipes (gravity networks from the parts-list catalog; inverts are explicit), " +
        "horizun_c3d_points (COGO points, groups, PNEZD-style import/export), horizun_c3d_exchange (data shortcuts; " +
        "LandXML 1.2 written by Horizun because Civil 3D has no .NET export; current-state DWG copy without saving " +
        "the source). The pipes tool also inspects pressure networks and their parts and creates/renames empty networks; " +
        "pressure part placement and hydraulic calculations are not implemented. New audit/pressure/DWG paths are " +
        "compiled against actual references but need live regression. compare_design reports explicit-sample deviations/tolerances/RMSE; " +
        "corridor targets and native guarded split/merge are available; region_quantities are estimated, not native QTO. " +
        "Editable point CSV binds source fingerprints and units. Alignment viewport links refresh explicitly. " +
        "Terrain OBJ/DirectShape preserves geometric triangles separately from editable Toposolid retriangulation. Plan production sheets, view frames, " +
        "intersections and native gradings have no public API: offer layouts/viewports or the geometric grading instead.";
}
