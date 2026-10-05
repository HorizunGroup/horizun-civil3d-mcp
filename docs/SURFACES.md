# Surface operations — v0.2.0 first phase

Status: implemented, compiled against installed Civil 3D 2025, covered by host-independent tests. **Live verification pending**: these operations are a preview for a disposable test drawing. No new L grade is claimed.

Tool: `horizun_c3d_surface`. Ten actions keep the MCP catalogue compact (seven tools total).

| Action | Behavior | Effect |
|---|---|---|
| list | Name/layer/style wildcards, pagination, metadata/native statistics; grid statistics opt-in | Read |
| get | Exact name/handle or names batch; volume depths, native volumes, explicitly estimated grid areas and weighted statistics | Read |
| sample_elevation | XY point array or straight line with step; exact endpoint included; outside/hole/unreadable elevations are null with reason | Read |
| volumes_report | Existing TIN/grid volume surface, or transient volume between base/comparison; native unadjusted/adjusted volumes and computed factors | Read, transaction always aborted |
| rename | One surface to a unique destination name | SafeWrite |
| set_style | Entire resolved batch to an existing uniquely resolved style; plan includes all style users | SafeWrite |
| duplicate_style | CopyAsSibling to unique name; verify created handle/name and every plan/model display component | SafeWrite |
| create_tin | Empty TIN with unique name, existing style, optional existing unlocked layer/description | SafeWrite |
| create_volume | Unique TIN volume surface from distinct base/comparison, with existing style and optional layer/description | SafeWrite |
| rebuild | Explicit rebuild of a resolved batch, followed by IsOutOfDate re-read | SafeWrite |
| add_data | One TIN: vertices, ONE breakline group (standard/proximity/non_destructive), ONE boundary group (outer/hide/show/data_clip, closed polylines). Re-reads vertex and standard-breakline elevations, group counts, kind, name/description | SafeWrite |
| paste | One TIN target, ordered `sources` (1-50). Refuses self, volume sources, cycles. Re-reads paste operations in order | SafeWrite |
| apply_elevation_analysis | Batch. Modes equal/step(break_at)/ranges/recolor; ACI or #RRGGBB; re-reads stored bands; grid area per band (+ volume for volume surfaces, reconciled with native net); warns if the style hides Elevations | SafeWrite |
| apply_slope_analysis | TIN only, values in PERCENT (API stores ratios); same modes; re-reads bands; grid area per band; warns if the style hides Slopes | SafeWrite |
| style_display | Component visible/colour/layer, plan/model/both; refused for shared styles unless allow_shared_style=true; re-reads every property | SafeWrite |

## Examples (synthetic names, no project data)

```json
{"action":"list","limit":20}
{"action":"get","names":["EG_FG"],"max_samples":10000}
{"action":"sample_elevation","name":"EG","points":[{"x":0,"y":0},{"x":10,"y":20}]}
{"action":"sample_elevation","name":"EG","line":{"start":{"x":0,"y":0},"end":{"x":30,"y":40}},"step":2}
{"action":"volumes_report","base":"EG","comparison":"FG","cut_factor":1.2,"fill_factor":1.1}
{"action":"rename","name":"EG","new_name":"EG_ANT","target_document":"Fixture.dwg"}
{"action":"set_style","names":["EG","FG"],"style":"Contours","target_document":"Fixture.dwg"}
{"action":"duplicate_style","style":"Contours","new_name":"Contours_Copy","target_document":"Fixture.dwg"}
{"action":"create_tin","new_name":"EG_NEW","style":"Contours","target_document":"Fixture.dwg"}
{"action":"create_volume","new_name":"EG_FG","base":"EG","comparison":"FG","style":"Volumes","target_document":"Fixture.dwg"}
{"action":"rebuild","name":"EG","target_document":"Fixture.dwg"}
{"action":"add_data","name":"SCRATCH","target_document":"Fixture.dwg","vertices":[{"x":0,"y":0,"z":100}],"breaklines":{"handles":["2B1"],"kind":"standard"},"boundaries":{"handles":["2B2"],"kind":"outer","name":"Limit"}}
{"action":"paste","name":"COMBINED","target_document":"Fixture.dwg","sources":["EG","PATCH"]}
```

Each JSON object is the arguments of horizun_c3d_surface. Writes above are dry runs by default. Review the returned plan, then repeat the **same arguments** with `dry_run:false` and the issued `confirmation_token`. Tokens are single-use, expire after ten minutes and bind to the active drawing, its revision, the request and the resolved plan. One lock spans plan capture, confirmation, commit and re-read.

All batch targets resolve before work; one invalid target rejects the operation. Edits refuse data-shortcut/read-only/cloud/subobject references, native surface locks and locked layers. Destination names are never overwritten. The server rejects fields irrelevant to the chosen action instead of silently ignoring them.

## Numbers and limits

- Drawing WCS XY, no coordinate conversion. Responses include drawing/display units.
- Native GetGeneralProperties maximum cut/fill and vertex mean are separate from grid statistics; the vertex mean is not an area-weighted mean.
- Midpoint quadrature uses actual edge-cell rectangular areas. Areas, side minima, weighted means, empirical area-weighted median/P90 and sampled volumes remain **estimates**, especially around holes and boundaries. Cut depth is positive for negative volume-surface elevation; fill height is positive for positive elevation.
- Reconciliation compares estimated cut/fill volumes with native volumes using 0.5% tolerance and reports agreement/disagreement. It never converts a disagreement into a verified exact statistic.
- Native volume / sampled area is also reported as an estimated mean; source and denominator remain explicit.
- No valid samples means null area/volume with reason, never fabricated zero. A measured zero remains zero. Exceptions other than PointNotOnEntity are counted separately as unreadable, not silently treated as outside.
- No exact volume-surface 2D area is exposed by the probed properties API: volumes_report returns null area with reason; get offers estimated cut/fill areas.
- `max_samples` defaults to 10000, maximum 100000, shared across the call. An explicit spacing that exceeds the allocated budget is refused. Up to 100 names, no duplicates. Point/line mode: up to 10000 points and 100000 evaluations across a batch. No silent resampling or truncation.
- Factors in volumes_report only calculate returned quantities; native factors are unchanged. Native net retains Autodesk's returned value; calculated net is explicitly fill minus cut.
- Out-of-date surfaces are reported; reads do not silently rebuild.

Volume properties require ForWrite access. Read transactions always abort, including a temporary surface created to compare base/comparison. A rolled-back temporary object can still advance the conservative in-memory revision observer, invalidating an old write confirmation; redo the dry run. No persistent surface is created by volumes_report.

## Verification and evidence

- 119 Core/Server tests pass (50 added): semantic/schema validation, permission effects, analytical plane integration, boundary-cell area, weighted statistics, null/overflow handling, line endpoints, sample budgets, stale confirmation and server preflight.
- Plug-in 2025 builds with zero errors/warnings. API dumps: AeccDbMgd.phase1-surfaces.txt, phase1-properties.txt, phase1-display.txt, acdbmgd.phase1-surfaces.txt and phase1-color.txt. The first probe recorded unsuccessful alternate type names; the follow-up confirms GeneralSurfaceProperties/TerrainSurfaceProperties/VolumeSurfaceProperties. No code uses the unsuccessful names.
- Complete installer DryRun passed 119 tests, 111 tunnel checks, build/publication/staging. Final deployment runs with SkipTests because those suites passed separately; it builds the final source, backs up and verifies installed SHA-256.
- Writes re-read requested names, handles, styles, layer/description, created kind, base/comparison and readable created volumes as applicable. Rebuild re-reads IsOutOfDate. Duplicate_style also re-reads all public plan/model display properties; other internal style settings rely on native CopyAsSibling and are not claimed individually verified.
- Post-commit failure preserves committed=true and reports verification_failed with actual/checks; it does not claim rollback after a committed edit. Transactions that fail before commit abort.
- All new host actions still need a live run. Host-independent tests and compile evidence do not prove native undo, reference handling or full runtime behavior.

## Live checklist — disposable drawing only

1. Reopen Civil 3D and restart the MCP client for the new contract. HZ_STATUS/health must report v0.2.0 and matching contract.
2. In a disposable drawing with synthetic EG/FG surfaces and known geometry: list/get, native volume report vs Civil properties, point/line elevations (including a hole/outside point), sampling reconciliation and bounded-budget refusal.
3. Confirm volumes_report between base/comparison leaves no new persistent surface; current style users are correct.
4. Exercise each write: default dry run changes nothing; exact confirmation commits; re-read yields match; token reuse is refused; drawing edit after rehearsal yields stale_plan.
5. Test batch invalid-name refusal before any edit, destination-name collisions, wrong target drawing, read-only/reference/locked surface/layer. Use one UNDO to check the whole committed batch returns to its previous state.
6. Test duplicate_style display settings and source style preservation; confirm create_volume volumes are read from the created object.
7. Do not create/change settings.json or modify customer drawings as part of this checklist without the owner's explicit permission. safe_write is sufficient for these actions; no save is performed.

## Next implementation block

Geometry input (`add_data`: vertices, breaklines and boundaries), deterministic fixture and live verifier; then elevation/slope analysis, style display and surface paste. Feature lines, alignments/profiles and the geometric grading engine follow. The ChatGPT account setup remains independent of this work.

Primary references:
- https://help.autodesk.com/cloudhelp/2025/ENU/Civil3D-DevGuide/files/GUID-E5899496-9E00-4CA8-A321-76268124CD62.htm
- https://help.autodesk.com/cloudhelp/2022/ENU/Civil3D-API/files/html/d280ff4f-4c4b-f910-7a27-0e787a43448f.htm
- Installed 2025 DLL signature dumps and patched SurfaceManageCommands/SurfaceCommands (read-only reference).

## Deterministic fixture and live verifier (v0.3.0)

1. In Civil 3D, start a NEW drawing from a Civil 3D template and run `HZ_BUILD_FIXTURE` (or click Horizun Hub > Dibujo de ensayo).
2. Run `python scripts/verify_live.py`. It prints PASS/FAIL per step and writes `fixtures\verify-<time>.json`.
3. Do one UNDO in Civil 3D and confirm the last write reverts as a single step (manual).
