# Civil 3D capability matrix and evidence

Evidence grades:

| Grade | Meaning |
|---|---|
| **L** | Verified live in that Civil 3D year |
| **B** | Compiled against that year's actual Autodesk DLLs (installed or signed reference packages), so every API member exists with that signature; provenance must be recorded |
| **T** | Unit or end-to-end tests without Civil 3D |
| **S** | Source only |

A capability is "supported" only with **L** for that year.

## v0.9.1 native acceptance — 2026-10-05

**Current v0.9.2 update (2026-10-06):** engineering rerun passes78/78 on Civil2025.
Automatic `undo_last` is disabled and returns unsupported/committed=false before
native execution; three request variants and independent unchanged-entity reads
pass. The historical UNDO results below do not certify the current automatic action.
See [public readiness](PUBLIC_READINESS.md). Native2024/2026 evidence remains absent.

Installed and automatically loaded on Civil 2025 after the explicit, backed-up
registration of the plugin directory in existing Civil trusted paths. Native
security and scripting permissions remain unchanged. Codex stdio registration
points to the verified installed executable. See [the acceptance report](ACCEPTANCE_20261005.md).

| Scope | Current evidence | Remaining gate |
|---|---|---|
| Surface comparison | L2025: known 0.02 m deviation/RMSE, passing full coverage and explicit partial-domain/tolerance failure | Other host years and project-specific survey controls |
| Corridor targets/geometry/estimated quantities/split/merge | L2025: 64 generated-fixture checks with real imported assembly and independent section integration | Offset/Elevation multi-target option setters and additional real geometries; other years |
| Editable COGO CSV | L2025: export/edit/apply/reread, stale file and changed-source refusal | Additional project data and other years |
| Alignment viewport/refresh | L2025: 22 final acceptance checks; original layout/TileMode restored; saved DWG reopened with locked/on viewport | Automatic event-driven refresh is not implemented |
| Full DWG copy | L2025: 8/8 final controls, unchanged source SHA/GUID/dirty state, copy reopened read-only with independent elevations | External dependencies and exhaustive individual design values |
| Terrain to Revit | L: Revit 2025.4 rollback/apply/save, rotated shared controls, typed reread and clean-project receiver | Other Revit versions and actual project CRS; Toposolid thickness/volume is not transferred |
| UNDO created surface | L2025: one undo, independent not_found afterward | Generic modified-container restoration remains explicitly partial |

552 Core tests; 391 overlapping runtime tests per net48/net8/net10; 14 Python
receiver tests. Full builds pass for 2024/net48, 2025/net8 and 2026/net8/net10.
Absent 2024/2026 hosts have B/T only. The sections below retain earlier evidence
and preparation history; they do not supersede this acceptance.

## Engineering expansion — v0.9.0 prepared, 2026-10-05

Deployment update: v0.9.0 release installed for the available 2025 host. All 59
installed files match the staged SHA-256 manifest. Installed server reports the
expected contract and 28 tools/163 actions. Invalid tolerance/handle and FullWrite
under safe_write refusals verified over actual installed stdio. Fixed autoloader
RuntimeRequirements scope inside each ComponentEntry, with backup and 59-file
hash reread. A fresh Civil2025 instance now publishes the v0.9.0 bridge; live
health and four surface writes pass with postcommit verification. Paired 100m2
TIN planes give 3/3 valid samples, signed deviation/RMSE 0.02m and tolerance
0.05m. Evidence is limited to this fixture; no Revit transfer has run.

| Scope | Evidence | Remaining gate |
|---|---|---|
| Surface `compare_design`: signed deviations, inclusive tolerance, min/max/mean/MAE/RMSE, sample coverage | L2025: paired planar TIN fixture, 3/3 samples, deviation/RMSE 0.02m within 0.05m; T: seven analytic cases | Missing domain/stale reference fixtures and other target years |
| Corridor targets, applied geometry, estimated region/code/material quantities, guarded native split/merge | T: 15 validation/integration cases; probes 2024/2025/2026 | Real assemblies, targets, parameter preservation and UNDO |
| COGO export_editable_csv/apply_csv | T: 13 parser/snapshot cases; guarded writes compiled | Rehearsal/apply/reread/UNDO in generated DWG |
| Alignment-linked paper viewport and explicit refresh | T: nine camera/validation cases; sampled local chord orientation declared | Camera placement/scale and updated alignment in each host |
| Exact OBJ in terrain ZIP, separate Revit DirectShape receiver | T: four OBJ cases and seven Python guard cases; Revit 2025 signatures | Actual rollback rehearsal, shared controls, face/position reread and apply |

Contract `171049b89c39afc5120da6ff`: 28 tools, 163 actions. Core/Server 544 tests;
runtime suite 383 overlapping cases per net48/net8/net10. No new L evidence.
Civil 2024 lacks UseSameSideTarget: read returns null and explicit setting refuses.

## Civil 3D 2024/2026 compatibility preparation — 2026-10-05

| Scope | Evidence | Remaining gate |
|---|---|---|
| Core on Framework 4.8, .NET 8 and .NET 10 | T: 335 overlapping tests on each runtime; contract identical | Autodesk-dependent operations are outside this suite |
| Actual add-in pipe/authentication/ACL construction | T on all three runtimes; production transport with test-only dispatcher | Real Civil main-thread dispatch in each target host |
| 2024 add-in net48; 2026 net8/net10 selection | B against signed references; API dumps under docs/api-probes/2024 and 2026 | Actual matching host deployment and live fixture; net10 Civil reference library update matching |
| Installer PE runtime/year detection and package metadata | T; measured signed 2024/2026 and installed 2025 references, 496 Core/Server tests | Actual deployment and startup |
| Current 2025 add-in | B: zero warnings/errors | New prepared runtime guards and other v0.8.1 changes need L regression |

The owner confirms neither 2024 nor 2026 is installed. Their B evidence comes
from complete builds against signed Autodesk references, not Core tests. Packages
include 2024/2025/2026 net8 and a separate 2026 net10 variant. [COMPATIBILITY.md](COMPATIBILITY.md) records both
2026 runtime families, Autodesk sources and reproducible build/live acceptance.

## Terrain interoperability — v0.8.1 prepared, 2026-10-05

Current contract `b5cfe04b65b9b303d9241d4b`: 28 tools, 152 named actions.
See [INTEROPERABILITY.md](INTEROPERABILITY.md) for format, coordinate and fidelity limits.

| Change | Evidence | Pending live acceptance |
|---|---|---|
| `exchange export_revit`, FullWrite | T (validation, units, exact payload comparison, footprint flags, geometry hashing); B 2025 | Read actual visible TIN, stale-plan/no-overwrite/refusal paths, source unchanged and all exported coordinates/faces reread |
| Package -> Revit request helper | T: PS 5.1 21 checks; PS 7 29 checks including actual Core writer and local Revit LandXML reader | Client invocation against an idle Revit model; exact model/type/level and native Toposolid creation |
| LandXML round-trip double precision | T, B 2025 | Real fixture and third-party consumer acceptance |
| Civil -> Revit physical placement/fidelity | No L; parser compatibility only | Independent survey controls, rotation/elevation/nonzero level, metric/foot variants and interior surface deviations |

490 Core/Server tests pass; plugin 2025 compiles with zero errors/warnings.
Revit health did not start because a modal dialog was open. No model was modified.
The previous expansion below retains its historical 473-test evidence and contract.

## External benchmark — 2026-10-05

[BENCHMARK.md](BENCHMARK.md) compares seven automation references using pinned
public source and primary documentation. Its S/C/D labels describe source,
conditional implementations and publisher documentation, not this matrix's
year-specific live support. No competitors were executed and no new T/B/L
results were produced in this documentation block. It identifies workflow gaps
and defines an unexecuted acceptance/performance protocol for generated fixtures.

## Capability expansion — v0.8.1 prepared, 2026-10-05

Current contract `60ab4ce4eb7756190fa7f4bf`: 28 tools and 151 declared actions.
The full inventory and ordered gaps are in `CAPABILITY_CATALOG.md` and `CAPABILITY_REVIEW.md`.

| Capability/change | 2025 evidence | Pending live verification |
|---|---|---|
| `horizun_c3d_capabilities` | T; server-side counts, action schemas, effect filters and permission checks | Client discovery; no host needed for the catalog |
| `horizun_c3d_audit` | T (count semantics), B | Fixture units/xrefs/references and stale surface/corridor; preserve partial readings |
| `exchange export_dwg` | T (path/permission and atomic file helper), B | Reopen actual DWG, check original unsaved state/name, local block/entity counts, destination conflict and failure cleanup |
| Pressure list/get/create_network/rename | T (validation/permissions), B | Empty fixture network creation/rename, duplicate/reference refusal, existing-part connections and catalog-unit interpretation |
| CSV/LandXML require FullWrite | T, B | Refuse under safe_write; confirm and apply under full_write |
| LandXML Civil units and foot definition | T, B | Metric, international foot and US survey foot fixture exports with deliberate INSUNITS mismatch |

473 Core/Server tests pass. Plugin 2025 builds with zero warnings/errors.
The fixture script is updated but was not executed against a host. No new L
evidence was produced, and no 2026/2027 support is inferred from the 2025 build.

## Cross-product review — v0.8.1 prepared, 2026-10-05

| Capability/change | Evidence | Remaining host check |
|---|---|---|
| Bounded transport and 32 concurrent server calls | T; 445-test Core/Server suite includes message-boundary regressions | Optional stress regression on fixture |
| Codex/Claude bootstrap and prebuilt-runtime selection | T; PowerShell 5.1/7, temporary installation paths, native stdio forwarding to real published server | Actual client plugin installation/restart |
| Portable plugin ZIP and MCPB | T; metadata, payload, version and SHA-256 gates | Import in Claude Desktop |
| Atomic Claude registration and rollback | T; isolated configuration fixtures | Deploy with clients closed |
| Duplicate drawing-name refusal | B (2025) | Two drawings with the same basename in different folders |
| C# query/execute honest persistence reporting | B (2025) | Fixture-only script, re-read any effect |
| External effects excluded from `undo_last`; incomplete modified-object verification | B (2025) | Create-only, modification and external-export cases |
| Staged PDF/CSV/LandXML; backup on PDF replacement | T (file helper), B (2025) | Plot to an existing PDF, induce plot failure and verify previous bytes |

No new **L** evidence was produced. That initial preparation used contract `a9b1dd257fd965fa8893bde3`;
the current prepared contract is listed above. Server and add-in must be installed together. Historical live
results below do not imply these new changes are live-verified.

## Phase 0 (v0.1.0)

| Capability | 2025 | 2026 | 2027 | Notes |
|---|---|---|---|---|
| Plug-in loads from bundle, pipe + discovery published | L | - | - | 2026-10-01: auto-loaded at startup, `HZ_STATUS` shows the pipe published, clean log |
| Server: initialize, tools/list, schema validation, refusals | T | T | T | stdio smoke test + 47 tests |
| Server <-> plug-in wire, contract-hash refusal, targeting | T | T | T | end-to-end against a fake plug-in on a real named pipe |
| Queue: FIFO, capacity 16, cancel-before-start, shutdown | L (FIFO) / T | T | T | 6 concurrent calls served in admission order live |
| Busy detection (CMDACTIVE, modal window) -> `busy`, nothing ran | L (command) | - | - | `LINE` waiting for a point: `busy` in ~2 s, CMDNAMES=LINE, user's command untouched. Modal-dialog case still to test live |
| `horizun_c3d_health` | L | - | - | ACADVER 25.0s, units m/m2/m3, coordinate system code, profile |
| `horizun_c3d_document` info / list_open / object_census | L | - | - | Real production drawing: 32 surfaces (17 TIN, 15 volume), 26 feature lines incl. grading-owned, 2 sites |
| `horizun_c3d_document` save (dry run + token + file verification) | L | - | - | 40 MB production drawing on OneDrive: file written and verified on disk, DBMOD reset to 0; token reuse -> `already_used`; no token -> `confirmation_required` |
| `horizun_c3d_query` list / get (15 types) | L (surface, feature_line) | - | - | TIN stats (points, triangles, 2D/3D area); volume-surface depths; missing name refused with candidates. Other types: B |
| `horizun_c3d_styles` list / get with usage | L | - | - | Usage computed for 11 style kinds; others report `null` (not 0) |
| `horizun_c3d_probe` live API dump | L | - | - | Live: no managed GradingGroup type in 2025 |
| Civil 3D 2023 / 2024 (net48), phase 0 history | n/a | | | At phase 0 both were refused. Current 2024 platform port is described above; 2023 remains outside the targets |

## Live findings

- A drawing with no coordinate system reports `CoordinateSystemCode = "."`; it is reported as `null` with a note (fixed after v0.1.0 live run).
- `AeccDbVAlignment` objects can exist in model space with zero alignments in the drawing (79 in a grading-heavy drawing); the `profile` type only enumerates profiles owned by alignments. To investigate before counting them.

- A zoom/pan alone sets `DBMOD=16` (view modified). "Unsaved changes" is now qualified by DBMOD bits (`objects_changed`, `view_only`) so a view change is never mistaken for a model change.
- `Database.SaveAs` on the active drawing from application context works and clears DBMOD.

## Threading decision (brief section 3.2)

Work runs on Civil 3D's **main thread** under `Document.LockDocument`. It is driven by a
hidden WinForms control (`BeginInvoke`), a 150 ms timer and `Application.Idle`. It never runs from the pipe
thread.

Reads and dry runs execute in application context. Applied typed writes and
arbitrary scripts enter `ExecuteInCommandContextAsync` only while the UI is
quiescent. The v0.3.3 live finding showed that an application-context lock alone
did not put edits on the UNDO stack; the earlier claim in this section was
incorrect. External file/settings effects are not covered by DWG UNDO.

## Known limits (honest refusals)

- **Native gradings cannot be created through any public API** in Civil 3D 2025. There is no managed
  `GradingGroup`. Phase 2 provides a geometric grading engine (3D polylines plus TIN breaklines) and labels it
  as such. It never presents that output as native gradings.
- Volume-surface cut and fill (`TinVolumeSurface.GetVolumeProperties`) needs a write transaction, so
  `query get` reports elevations or depths only. Volumes come from the surface tool in phase 1.
- Command-line messages that Civil 3D prints are not captured yet. `host_messages` carries only what the bridge
  itself observes.

## Live verification checklist (run after `scripts/install.ps1`)

1. Open Civil 3D with a drawing. `HZ_STATUS` should report the pipe as published and the profile as
   `safe_write`.
2. In Claude, `horizun_c3d_health` should return the year, ACADVER, units and coordinate system.
3. `horizun_c3d_document action=object_census`: the counts should match Prospector.
4. `horizun_c3d_query action=list type=surface`, then `get` one surface: check the statistics against Surface
   Properties.
5. `horizun_c3d_styles action=get object_type=surface name=<style>`: `used_by` should match the drawing.
6. Busy test: start `LINE` in Civil 3D and leave it waiting for a point, then call `horizun_c3d_query`. It
   should return `busy` with `LINE` and nothing should run. Repeat with the Options dialog open.
7. Save test, with the `full_write` profile:
   1. Run `document save` as a dry run, which returns a token.
   2. Apply it with the token. Expect `verified.status=match` and a new file timestamp.
   3. Reuse the same token. It should be refused with `already_used`.

## Review validation (Codex, 2026-10-01)

- Re-ran all 48 Core/Server tests in a temporary source copy: passed. Two xUnit1031 warnings remain in server tests.
- Built the 2025 plug-in against the installed DLLs: zero errors and warnings.
- Installed-server health and surface-list calls passed on Civil 3D 2025; no drawing writes or saves were performed.
- Query replies currently omit units. Malformed permission-field types can retain SafeWrite (reproduced against the built Core DLL).
- Save plan staleness for additional in-memory edits with unchanged DBMOD flags remains a source-review finding, pending fixture validation.
- This review adds no live evidence for new capabilities or for 2026/2027.

## v0.1.1 review fixes (2026-10-01; not yet deployed)

| Correction | Evidence | Live status |
|---|---|---|
| Strict permission-field validation and fail-closed file loading | T: invalid/null/duplicate/unknown fields, malformed lists, invalid file path, valid controls | Installed 0.1.1; live regression pending |
| Save invalidation with unchanged DBMOD/file timestamp | T: revision + real ConfirmationStore reject stale plans; unchanged single use and undo/reopen covered. B: database object/variable and document view event subscriptions compile against installed 2025 DLLs | Pending event/rehearsal/application verification in a disposable drawing |
| Query list/get, census and save units | B: uses existing DrawingInfo.Units against installed 2025 API | Pending live response checks |
| Build and contract version | T/B: 0.1.1, contract 6b2a8fe91890a5452113b9af | Installed 0.1.1 |

All 69 Core/Server tests pass. The 2025 plug-in build has zero warnings/errors.
The 2025 event signatures are in acdbmgd.revision.txt and accoremgd.revision.txt.
The two existing xUnit1031 warnings in server tests remain when those files are rebuilt.
No new L grade is claimed. No drawing edits, saves, permission changes or installation were performed.

Post-install regression: use a disposable drawing and approved full_write profile.
Rehearse save while DBMOD already marks objects changed; edit another object without
saving and confirm with the old token. Expect stale_plan and no save. Repeat for a
database-variable/view change and undo; unchanged rehearsal/application must succeed.
Check query list/get units match health. Restore the permission settings after testing.

Installer dry run for 2025 passed from the repository after applying the fixes: 69 tests, clean plug-in build and self-contained server publication. Bundle XML version and 2025 runtime requirements verified. No installation performed.


## v0.1.1 local deployment (Codex, 2026-10-01)

Installed after the owner confirmed Civil 3D was closed. The installer passed 69 tests,
built the 2025 plug-in cleanly, and verified all 8 bundle and 3 server files by SHA-256.
An independent post-install manifest hash check passed. Installed MCP initialize reports
0.1.1 and tools/list returns six tools. Health correctly reports no_civil3d_instance
while acad.exe is closed. Previous installation backed up; no permission or client-config changes.
The corrected plug-in behavior still awaits live verification after reopening Civil 3D.
These deployment checks do not add L grades for the new units or save-change observers.


## ChatGPT transport integration (Codex, 2026-10-01)

| Check | Evidence | Result |
|---|---|---|
| Core/Server regressions | T | 69/69 |
| Tunnel credential/lifecycle/identity/profile/environment regressions | T (local stand-in) | 111/111 in PowerShell 5.1 and 111/111 in PowerShell 7.6.5 |
| Full installer with helper package | B | DryRun/SkipTests passed; suites ran separately |
| Official tunnel-client 0.0.15 full Windows bundle | Local CLI | ZIP checksum verified; init/help/profile checked, no account key/traffic |
| Helper-only installation | Local deployment | Ten helpers verified against source/manifest; original eleven server/plugin hashes intact |
| Installed stdio initialize/tools/list | Local MCP | Civil 3D identity and six tools |
| Installed stdio health | Local MCP | no_civil3d_instance; Civil 3D closed |
| OpenAI account contact / real ChatGPT tool call | Pending | No account tunnel/key or ChatGPT app connected; no L evidence claimed |

Next: account setup in docs/CHATGPT.md, then open Civil 3D and call health from ChatGPT.


## v0.2.0 surface preview and local deployment (Codex, 2026-10-01)

| Capability | 2025 | Evidence / limits |
|---|---|---|
| Surface list/get, XY/line sampling, native/transient volume report | B / T | DLL signatures compiled; numeric/validation tests. Transaction rollback, native values and reference behavior await live tests. Grid areas/statistics explicitly estimated. |
| Surface rename/set_style/duplicate_style/create_tin/create_volume/rebuild | B / T | Contract/token/revision tests plus API compilation. Native mutation, fresh verification, display copy and undo await live tests. |
| Installed server initialize/tools/list and input rejection | T | Actual deployed 0.2.0 executable; seven tools / ten surface actions. Invalid rename without target_document rejected before host discovery. |
| Installed package integrity | T | Independent SHA-256 verification of eight bundle and thirteen server files; backup 20261001-124001 exists. |
| ChatGPT helper with expanded catalogue | T | Installed status accepts seven tools and retains pending_user_action. No tunnel daemon, key or real ChatGPT call. |

No new **L** grade. Civil 3D was closed throughout this block. Only 2025 was probed/built for these new operations; previous builds for other years do not establish surface compatibility. No permission/client configuration or drawing changes. Complete installer DryRun: 119 Core/Server tests and 111 PowerShell helper checks; final plugin build zero warnings/errors. Surface guide: SURFACES.md. Next: geometry input, deterministic fixture and authorized live regression, then analyses and feature lines.

## v0.3.0 surface geometry + fixture (Claude Code, 2026-10-01)

| Capability | 2025 | Evidence |
|---|---|---|
| surface `add_data` (vertices, breakline group, boundary group) | B + T | API in `AeccDbMgd.phase1-adddata-paste.txt`; 16 validation tests; post-commit elevation re-read implemented |
| surface `paste` (ordered, cycle/self/volume refusals) | B + T | Order re-read from `SurfaceOperationPasteSurface.SurfaceId` |
| `HZ_BUILD_FIXTURE` deterministic drawing | B | Analytic expectations in fixture-expected.json |
| `scripts/verify_live.py` | S | Ready; first run pending (needs Civil 3D open + fixture) |

Still open live checks: UNDO as a single step per write (manual), the stored type of a proximity breakline group, and "busy" with a modal dialog.

## Live fixture evidence (Civil 3D 2025, 2026-10-01, v0.3.3: 41/43)

| Capability | 2025 | Evidence (HZ_BUILD_FIXTURE + scripts/verify_live.py) |
|---|---|---|
| surface list/get statistics | L | 121 points, area 10000, z 100..103 (analytic) |
| surface sample_elevation | L | plane values exact; outside point null + outside_surface |
| surface volumes_report (volume surface and transient) | L | cut 5833.333 / fill 833.333 (analytic 17500/3, 2500/3); transient leaves no surface |
| surface rename / duplicate_style / set_style / create_tin / rebuild | L | dry run + token + apply + verified=match |
| surface add_data vertices / standard breakline / outer boundary | L | breakline z re-read 110; boundary area 6400 exact; open polyline refused |
| surface paste | L | pasted surface returns EG elevation at (25,75) |
| surface create_volume | L | volumes read back from the new object; stale right after a paste (Civil background update), passes when re-rehearsed |
| confirmation: token reuse, stale plan after an edit | L | refused |
| refusals: locked layer, existing name, missing name, wrong drawing | L | refused with codes |
| **UNDO of an MCP write** | **FAILED (v0.3.3)** | application-context edits are not on the undo stack; fixed in v0.3.4 (command context), live re-test pending |

## Live fixture evidence, final (Civil 3D 2025, 2026-10-01, v0.3.4): 46/46 PASS

Report: `%USERPROFILE%\.horizun\civil3d\fixtures\verify-20261001-201419.json`.

| Capability | 2025 |
|---|---|
| Every surface read (stats, sampling, volumes, transient volume) against analytic values | **L** |
| Every surface write: rename, duplicate_style, set_style, create_tin, add_data (vertices, breakline, boundary), paste, create_volume, rebuild | **L** |
| Confirmation (token reuse, stale plan) and refusals (locked, existing, missing, wrong drawing, open boundary) | **L** |
| **UNDO: one native UNDO reverts the last MCP write** (applied writes run in command context since v0.3.4) | **L** |

Still not live-tested: busy with a modal dialog; proximity breakline kind; 2026/2027.

## v0.4.0 + v0.5.0 (built, live run pending)

| Capability | 2025 | Evidence |
|---|---|---|
| surface apply_elevation_analysis / apply_slope_analysis / style_display | B + T | analysis API dump; 24 tests; fixture steps ready |
| grading create_geometric (geometric engine) | B + T | analytic engine tests (fill/cut daylight distances exact, sloped plane, collapse refusal) |
| feature_line create / set_elevations / rename / export_polyline3d | B + T | feature-line API dump; validation tests |
| execute_csharp (off by default) | B + T | permission gate tests; Roslyn 4.8.0 bundled |

## v0.6.0 phase 3 (built 2026-10-02, live run pending)

Evidence level for every v0.6.0 capability is **C** (compiled against the 2025 DLLs, rules unit-tested, live verifier written). It is **not L yet**. APIs were confirmed by offline probes in `docs/api-probes/2025/` (phase3-*).

| Area | Tool | How the write is verified (re-read in a new transaction) |
|---|---|---|
| Alignments | `horizun_c3d_alignment` | length/segments/start; radius and analytic length (PIs); offset distance at sample stations |
| Profiles | `horizun_c3d_profile` | elevations vs surface at 11 stations; every PVI and curve length; profile view stations |
| Sections | `horizun_c3d_sections` | station and left/right width of each sample line; sampled sources; one section view per sample line |
| Corridors | `horizun_c3d_corridor` | baseline alignment/profile, region stations, frequencies, out-of-date after rebuild, corridor surface codes |
| Labels | `horizun_c3d_labels` | feature (or view), style, anchor, station/elevation, ratio, text override |
| Layers | `horizun_c3d_layers` | every property; layer state compared with the drawing |
| Entities | `horizun_c3d_entities` | drawn geometry; transformed anchor and length/area invariants; offset distance; summed length after explode/join |
| Dimensions | `horizun_c3d_dimensions` | Measurement vs analytic value from the caller's points |
| Styles | `horizun_c3d_cad_styles` | every set property (whitelisted dimvars), current style, scale units |
| Blocks | `horizun_c3d_blocks` | entity count, attribute definitions, position/scale/rotation, attribute values, dynamic values |
| Tables | `horizun_c3d_tables` | every cell text |
| Layouts / PDF | `horizun_c3d_layouts` | viewport geometry and scale, page setup fields; PDF exists, %PDF header, page count |
| Cleanup | `horizun_c3d_cleanup` | each purged name is gone |
| Pipes | `horizun_c3d_pipes` | inverts, abs(slope), centre-to-centre 2D length, both connections; rim and sump |
| Points | `horizun_c3d_points` | number, E, N, Z, description; group membership vs our own query evaluation; CSV read back |
| Exchange | `horizun_c3d_exchange` | LandXML re-read (points, faces, lengths, PVIs); data reference object and name |

Honest refusals added: stock subassembly authoring, plan production sheets and view frames (alternative: layouts and viewports), spirals in LandXML export, a pipe crown above the rim or invert below the sump, and overwriting existing files or definitions.

## Live fixture evidence, phase 3 (Civil 3D 2025, 2026-10-02, v0.6.9): 284/284 PASS

`scripts/verify_live.py` (+ `verify_blocks.py`) on a fresh fixture drawing: **284/284**, no Civil 3D abort, UNDO included (report `verify-20261002-123122.json`). Every v0.4.0 to v0.6.9 capability in the table above is now evidence level **L** on 2025. FULL WRITE actions were verified as refused under safe_write; their applied path is not yet live-run.

Bring-up took 10 live runs, from 139/226 to 284/284. Findings, each fixed and logged in CHANGELOG v0.6.1 to v0.6.9:
- the AcDbAssocNetwork idle churn invalidated tokens;
- three fatal aborts around sample lines: the group must be open for write; sections update on first access, so sample the sources first and never read pending sections for read;
- catalog diameters are in mm;
- Null Structures;
- Civil sump semantics;
- empty-array ObjectIdCollection;
- DBDictionary + LINQ Cast;
- LabelBase RX class too broad;
- unsupported label-group and section properties;
- GradeIn on the first PVI;
- data shortcuts with no project.

## FULL WRITE applied live (v0.7.0, 2026-10-02 12:45): 294/294 PASS

The profile was temporarily raised to `full_write` with the owner's authorisation and restored to the default afterwards (`verify-20261002-124521.json`). These now pass APPLIED and re-read: entity erase, `plot_pdf` (%PDF-1.7, 1 page), layout delete, purge (linetype) and point erase. Still not live: `shortcuts_publish`/`shortcuts_reference` (they need a data shortcut project) and the two ribbon switches pressed by hand.

## Data shortcuts and two instances live (v0.7.3, 2026-10-04)

Test project `HZ_PRUEBA` on the owner's Desktop, created with `shortcuts_project`:
- **publish:** HZ_ROAD by the MCP, file written.
- **reference into a new drawing (with automatic association):** HZ_ROAD (90 m, not editable) and HZ_EG (z = 101.25, 121 points, rename refused).
- **two simultaneous Civil 3D windows:** `ambiguous` refusal without a choice; `horizun_c3d_target {pid}` switches between them.
- **restore:** the original working folder was restored afterwards.

Publish verification after a restart needs the drawing associated; v0.7.4 associates it, and that path is not yet re-run live.
