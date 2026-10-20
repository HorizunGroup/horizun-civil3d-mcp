# Roadmap

## Owner priority — 2026-10-05

Terrain export and placement in Revit now leads the interoperability work.
The Civil ZIP/manifest exporter and Revit rehearsal preparer are implemented;
see [INTEROPERABILITY.md](INTEROPERABILITY.md) for current fidelity, coordinate
requirements and live acceptance. Next: prove the transfer, investigate an exact
TIN mesh route, then applied corridor geometry/targets and material quantities.
As-built deviations/RMSE, definition-preserving corridor split/merge, dynamic
sheets and controlled spreadsheet write-back remain selected pending work.

Each phase is done only when its capabilities have **live** evidence in `docs/CIVIL3D.md`.

## Phase 0 - foundation (v0.1.0, built)

Phase 0 delivers:

- the server, the plug-in, the pipe, discovery and the contract hash;
- the queue and busy detection;
- the permission profiles, plus the confirmation and verification core;
- health, target, document, query, styles and probe;
- the installer with hash verification and rollback.

Exit criterion: Claude lists the surfaces and styles of a real drawing in Civil 3D 2025.

## Phase 1 - surfaces (highest priority)

Phase 1 ports the capabilities that were already validated in production with the patched connector. They are
**rewritten under Horizun names**, and every write follows the dry-run, token, commit and re-read contract.

| New Horizun tool / action | Validated predecessor (do not reuse its name) | Notes |
|---|---|---|
| `horizun_c3d_surface` `list` / `get` | getSurfaceInfo | Adds isopach statistics: max cut and fill, area-weighted means, areas |
| `horizun_c3d_surface` `create_tin` | createSurface | name, style and layer re-read |
| `horizun_c3d_surface` `add_data` | addBreaklines, addPoints, boundaries | Breaklines from 3D polylines or feature lines (dynamic), outer/hide/show boundaries; reports the change in point and triangle counts |
| `horizun_c3d_surface` `paste` | pasteSurface | Ordered paste into a target |
| `horizun_c3d_surface` `create_volume` / `volumes_report` | TinVolumeSurface script, computeSurfaceVolume | Cut, fill and net are read back **from the created object**, adjusted or unadjusted is declared, and an optional bounded polygon is supported |
| `horizun_c3d_surface` `apply_elevation_analysis` | get/setSurfaceElevationAnalysis | Modes: equal, step, explicit ranges and recolor. Real area and volume per band, verified so that the sum of the bands matches the Civil 3D volume. Legends enter as parameters (the Horizun isopach legend is a client-side preset, not compiled) |
| `horizun_c3d_surface` `apply_slope_analysis` | (new) | `Get/SetSlopeData`, ranges in %, area per range |
| `horizun_c3d_surface` `set_style` / `duplicate_style` / `style_display` | set/duplicate style, setSurfaceStyleDisplay | The dry run lists every other surface that shares the style |
| `horizun_c3d_surface` `rename` | renameSurface | |
| `horizun_c3d_surface` `sample_elevation` | sampleSurfaceElevations, getSurfaceElevationsAlong | Points outside the surface return `null`, never 0 |
| `horizun_c3d_surface` `rebuild` | Rebuild | Re-reads `IsOutOfDate` |
| `horizun_c3d_audit` (basic) | (new) | Built in prepared v0.8.1: out-of-date surfaces/corridors, stale/invalid references, xrefs, units and coordinate system; unreadable/unknown states explicit. Live pending |
| `horizun_c3d_execute_csharp` | executeScript (Roslyn) | Off by default. Needs `unsafe_code` plus `enable_execute_csharp` and owner approval in Civil 3D. Query aborts only the supplied transaction; arbitrary scripts can persist other effects. Results are self-reported |

Phase 1 also adds the `HZ_BUILD_FIXTURE` command: a deterministic test drawing (EG with known relief, FG
platform, an alignment and a boundary) with analytically known volumes, and `scripts/verify-live.ps1`.

Acceptance case: build a volume surface between EG and FG and report cut, fill and net. Then apply slope
analysis to FG with ranges 0-2, 2-5, 5-10, 10-30 and >30 %, and switch FG to a style that shows slopes.

## Phase 2 - design objects (historical 2025 live evidence through v0.7.3; execute_plan pending)

- `horizun_c3d_feature_line`: create from a polyline (with an explicit site), set elevations per vertex or from
  a surface, rename, move to a site, export as a 3D polyline.
- `horizun_c3d_grading`: the **geometric grading engine**, ported from the validated `grading_engine`. It
  supports offsets, grade to surface (cut and fill slopes, with fans at convex corners), grade to depth and
  grade to elevation, and builds a TIN from breaklines with an outer boundary. It is validated against a
  native grading (median dz 0.000 m, volume -0.09 %). It is labelled as geometric. Native gradings stay refused
  by name, because there is no public API.
- `horizun_c3d_alignment` and `horizun_c3d_profile`.
- `horizun_c3d_corridor`: read, rebuild, volumes against EG, and creation where the probe confirms the API.
- `horizun_c3d_execute_plan`: up to 100 typed writes with `${key.path}` references, all or nothing.

## Phase 3 - networks, documentation and data

**Historical 2025 evidence:** 294/294 fixture checks on v0.7.0, plus data-shortcut reference/publication checks on v0.7.3. These results do not live-verify the prepared v0.8.1 changes.
- Built: gravity pipe networks (catalog, create, structures, pipes, validate), sections and sample lines, COGO points and groups, data shortcuts (status, publish, reference), PDF plotting of layouts, and our own verified LandXML 1.2 export.
- AutoCAD tooling: layers, entities, dimensions, styles, blocks, tables, layouts and cleanup.
- Civil 3D labels.
- Prepared v0.8.1: capability catalog, drawing audit, current-state DWG export and pressure-network list/get/empty creation/rename; locally tested and compiled for 2025, live pending.
- Still open: pressure part catalog/creation/connections, `capture_view`, earthwork/material quantities, Excel/Power BI reporting, spirals, atomic `execute_plan`, complete modified-object UNDO verification, and the 2026/2027 live matrix. See `CAPABILITY_REVIEW.md` for priorities and acceptance criteria.

Phase 3 adds:

- pipe and pressure networks (read and validate first);
- sections and sample lines;
- COGO points and point groups;
- data shortcuts;
- export (PDF and DWG; LandXML only if it can be verified);
- `capture_view`;
- earthwork procedures (zone process, final terrain from implantation breaklines only, conformation with
  control row) and Excel and Power BI reporting;
- a live matrix for 2025, 2026 and 2027, plus 2023 and 2024 (net48) if they are needed.

## Benchmark

`docs/BENCHMARK.md` will be created from task outcomes, not tool counts. A task scores only if three things
hold: the schema is typed, invalid input is refused before anything changes, and the result is measured after
the operation.
