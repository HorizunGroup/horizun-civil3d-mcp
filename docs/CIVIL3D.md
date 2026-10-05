# Civil 3D capability matrix and evidence

Evidence grades:

| Grade | Meaning |
|---|---|
| **L** | Verified live in that Civil 3D year |
| **B** | Compiled against that year's installed DLLs, so every API member exists with that signature |
| **T** | Unit or end-to-end tests without Civil 3D |
| **S** | Source only |

A capability is "supported" only with **L** for that year.

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
| Civil 3D 2023 / 2024 (net48) | n/a | | | Not built yet: the build refuses these years |

## Live findings

- A drawing with no coordinate system reports `CoordinateSystemCode = "."`; it is reported as `null` with a note (fixed after v0.1.0 live run).
- `AeccDbVAlignment` objects can exist in model space with zero alignments in the drawing (79 in a grading-heavy drawing); the `profile` type only enumerates profiles owned by alignments. To investigate before counting them.

- A zoom/pan alone sets `DBMOD=16` (view modified). "Unsaved changes" is now qualified by DBMOD bits (`objects_changed`, `view_only`) so a view change is never mistaken for a model change.
- `Database.SaveAs` on the active drawing from application context works and clears DBMOD.

## Threading decision (brief section 3.2)

Work runs on Civil 3D's **main thread in application context** under `Document.LockDocument`. It is driven by a
hidden WinForms control (`BeginInvoke`), a 150 ms timer and `Application.Idle`. It never runs from the pipe
thread.

`ExecuteInCommandContextAsync` is **not** the primary path:

- It only exists to run commands such as `Editor.Command`.
- A command context interferes with the user's own command.
- Locking the document from application context is the standard pattern for modeless writes and gives one
  undo step per lock.

Command-context execution will be added only for tools that must run native commands, and only after live
evidence.

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
