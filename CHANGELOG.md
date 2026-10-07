# Changelog

## Unreleased - 2026-10-06 - Multi-agent review fixes (branch `phase-10/review-fixes`)

Fixes for the 32 findings that survived a five-dimension multi-agent review with adversarial verification.
Contract unchanged (`e36ee390efbb5d34ee0fe86c`), so the server and the plug-in stay compatible with v0.9.2.
Plug-in fixes are built (B) and not live-verified; Core/Server fixes are unit-tested (T).

- **Points:** `create`/`import` with `group` no longer replace a point group's custom query (which dropped its
  members). A group whose query is not a standard query is refused by name; `_All Points` is left untouched; the
  plan reports the group's previous point count and verify checks that the previous members are still in it.
  Explicitly numbered rows are added before auto-numbered rows, so a mixed request cannot collide after confirmation.
- **Sections:** plan, read and verify transactions open sample lines, groups and sections for write with the
  locked-layer override (Civil 3D aborts on read-only access to pending sections; a locked layer no longer turns a
  read into a failure). `create_sample_lines` into an existing group and `create_section_views` refuse a
  non-editable group in the plan.
- **Wildcards:** `Hz.Like` is an iterative O(n·m) matcher; a pattern such as `*-*-*-…Z` can no longer backtrack
  exponentially on the Civil 3D thread.
- **Pipe security:** the pipe name has a random part published only in the user-only discovery file; the first
  pipe instance must create the pipe (`FirstPipeInstance`; on net48 a pre-existing pipe is refused) and the bridge
  stops listening rather than join a pipe it did not create. The server connects with identification-level
  impersonation and sends the token only after `GetNamedPipeServerProcessId` equals the discovered acad.exe pid.
- **Transport errors:** a request larger than the pipe limit once encoded is refused before sending; truncated or
  malformed replies and wrapped read faults are tool-level Transport errors that keep the "check the drawing" warning.
- **Permissions:** an `allowed_tools`/`denied_tools` entry that is not an exact tool name fails closed to `read_only`.
- **Tokens:** spent confirmation tokens are kept until they expire, so a replay reports `already_used`, not `unknown`.
- **Verification:** layout and pressure-network renames compare case-sensitively; an unreadable structure sump is a
  failed check with its reason, not a skipped one; `add_data` with `rebuild=false` does not check breakline
  elevations against the stale TIN and says so. Editability fails closed when `is_reference` or the layer cannot be read.
- **Inputs:** absolute-path checks use `IsPathFullyQualified` (drive- or root-relative paths such as `C:x.csv` are
  refused); `set_cells` refuses a repeated (row, col); `split_region` refuses a split station that coincides with an
  additional applied station; `blocks define` refuses source entities from different spaces in the plan.
- **Data shortcuts:** the publish manager is disposed on failure.
- **Installer:** in package mode, years chosen by default skip (and report) a year the package cannot serve instead of
  blocking the others; a running installed server is refused before files are touched; plug-in install attempts keep
  only their log on failure and the three newest attempts; installs keep the five newest backups.
- **CI and privacy:** GitHub actions pinned to commit SHAs; trust-registration tests run in CI; package privacy tests
  use a generic user-profile pattern instead of a personal account name.
- **Docs:** README surface/undo/profile text, capability review counts, regenerated capability catalog hash, STATUS
  heading, the shipped workflow skill (`undo_last` disabled) and the Core test that now pins the contract hash.
- **API probes:** `acdbmgd.review-fixes.txt` (Transaction.GetObject overloads, Hatch, Curve, Entity, DBText,
  Dimension, MLeaderStyle, AttributeReference, XrefGraph, Database) and `AeccDbMgd.review-fixes-part.txt` (Part).
- Evidence: 569 Core/Server tests; 406 runtime tests on each of net48/net8/net10; Python, plug-in bootstrap, runtime
  build, metadata, trust and rollback script tests pass. Plug-in builds 2024/net48, 2025/net8, 2026/net8 and
  2026/net10: 0 errors.

## v0.9.2 - 2026-10-06 - Public readiness

- Published v0.9.2 and made the repository public after explicit authorization and successful PR#1 CI. Four packages plus SHA256SUMS match GitHub digests. Withdrew the backed-up0.8.0 installer containing private build paths and enabled private vulnerability reporting.
- Normalized a Windows short-path assertion in the acceptance-client test after CI exposed runner path aliases; eight local client tests and subsequent remote CI pass. Production packages remain unchanged.
- Automatic `undo_last` is disabled before native execution after a new fixture demonstrated unreliable native attribution/restoration. The reserved action returns unsupported/committed=false; writes no longer advertise an automatic inverse. Manual Civil UNDO is available to the operator.
- Experimental bounded native-field snapshots were removed after the failed acceptance, without retrying the committed UNDO. No restoration claim is made.
- Native2025 engineering rerun: 78 controls pass; three automatic-UNDO request variants are refused and independent entity reads stay unchanged.
- Added SECURITY.md, public readiness scope, complete-history/source secret scanning in CI and compiler path mapping for distributable binaries. A historical docstring false positive has a single exact exception.
- Updated public docs to distinguish native2025/Revit2025 evidence from build-only2024/2026 compatibility. Contract is `e36ee390efbb5d34ee0fe86c`; 28 tools/163 declared actions remain.

## v0.9.1 - 2026-10-05 - Native acceptance fixes (local)

- DWG export uses the full native database save rather than Wblock, preserves unreferenced blocks, and verifies source identity, dirty state, GUIDs and source file hash before publishing.
- Source hashing tolerates the native file writer's shared handle. DBMOD preservation spans the transaction commit; final live export passes 8/8 checks, followed by independent read-only elevation checks on the copy.
- Explicit trusted-path registration backs up existing Civil profiles, confines trust to the declared plugin year directory, checks concurrent edits and rolls back failures without changing SECURELOAD. Bootstrap installation plans describe this registration before confirmation.
- Corridor target getters respect native target type and target count constraints; unavailable fields return null with a reason. Option setters validate the native enumeration for each target type.
- Native corridor acceptance passes 64 checks. Alignment viewport activation/restoration fixes eNotInPaperspace; 22 final checks pass and the saved viewport survives reopening. Created-surface UNDO passes independent absence checks, while generic container restoration remains explicitly partial.
- Revit mesh placement validates the transform direction against native shared-coordinate controls. Explicit mesh tolerances account for measured Revit storage precision; repeated placement of the same package is refused.
- Civil terrain transferred to a generated Revit 2025 project as editable Toposolid and DirectShape. Independent shared-coordinate controls and typed element rereads pass; the model was saved. DirectShape maximum measured residual: 0.002994 mm with an explicitly requested 0.01 mm tolerance.
- 552 Core tests and 391 runtime tests in each of net48/net8/net10 pass. Civil 2024/net48, 2025/net8 and 2026/net8/net10 builds pass. Live evidence for absent Civil 2024/2026 remains pending.
- Version remains local; no GitHub release or push.

## v0.9.0 - 2026-10-05 - Engineering and interoperability (prepared)

Deployment: installed release on the available Civil 2025 machine; 44 bundle +15
server files independently verified by SHA-256. Actual installed stdio catalog,
schema and safe_write refusals pass. Corrected RuntimeRequirements scope per
ComponentEntry and canonical .Net AppType; packaging rejects parent scope,
wrong-year range and wrong AppType. Installed XML backed up and hashes reread.
Fresh Civil2025 startup publishes the bridge. Four surface writes verified;
paired TIN comparison passes 3/3 samples with RMSE 0.02m. No GitHub publication
or live Revit transfer.

- Contract `171049b89c39afc5120da6ff`: 28 tools / 163 actions; server and add-in must be updated together.
- Surface compare_design returns signed vertical deviations, inclusive tolerances, min/max/mean/MAE/RMSE and honest missing sample coverage.
- Corridor target reads/writes, applied geometry, average-end-area region/code/material estimates and guarded native split/merge with full supported definition snapshots. Unsupported offsets/overrides and incompatible merges refuse.
- Editable COGO CSV roundtrip binds units, source geometry/descriptions and point identities; apply edits uses plan/token and new-transaction verification. File export is FullWrite and excluded from DWG UNDO.
- Alignment-linked layout viewports persist station metadata and refresh explicitly; local chord camera orientation is declared as an estimate, not native view frames.
- Terrain package adds exact triangle OBJ. Separate authorized Revit Python receiver rehearses DirectShape with geometric reread and rollback, without enabling its channel. Helper supports -ExactMesh and old Toposolid packages.
- 544 Core/Server tests; 383 overlapping tests per runtime; seven Python receiver guards. Synthetic engineering fixture generator added. Actual Autodesk builds/package gates recorded in handoff/SESSION_LOG.md; no live transfer or deployment inferred from tests.

## v0.8.1 - 2026-10-05 - Cross-product parity review (prepared, not deployed)

Current prepared contract `b5cfe04b65b9b303d9241d4b`: install server and add-in together and restart the MCP client.
The earlier cross-product preparation used `a9b1dd257fd965fa8893bde3`; it was not published.
The subsequent capability expansion used `60ab4ce4eb7756190fa7f4bf`, also not published.

### Civil 3D 2024 and 2026 completed reference builds
- Complete add-in builds now pass for 2024/net48 and 2026/net8/net10 against signed Autodesk references. Fixed cross-year obj source inclusion, Framework enum/Zip/encoding overloads and absent 2024 cloud-reference flags; reference surfaces remain protected.
- Host metadata accepts official Framework 4.7 SDK references for net48 and net8 Civil libraries under net10 AutoCAD; four added selection/rejection tests bring Core/Server to 496. Offline API signatures captured for 2024 and 2026.
- Release packages now include 2024/2025/2026 net8, with a separate 2026 net10 ZIP. No new live evidence, deployment or publication. Reference provenance and update matching limits are in docs/COMPATIBILITY.md.

### Earlier runtime preparation (superseded build status)
- Core now targets net48/net8/net10; compatible finite checks, hashing/random bytes, paths, file replacement and BCL/compiler bridges. The out-of-process server remains self-contained .NET 8.
- Add-in platform port for 2024, explicit 2026 net8/net10 variants, CLR/year startup refusal and Framework pipe ACL constructor. These complete Autodesk builds remain pending because the owner does not have either target version installed.
- Installer selects runtime from PE metadata without executing Autodesk code; checks year/managed versions, records host-builds.json and refuses mismatched prebuilt packages. Custom reference roots and 2024 R24.3 bundle range added. Offline probes select Framework mscorlib or matching .NET 8/10 BCL.
- 492 Core/Server tests and 335 overlapping host-independent/production-pipe tests on each of Framework 4.8, .NET 8 and .NET 10. Contract remains b5cfe04b65b9b303d9241d4b. Plugin 2025 builds with zero warnings/errors; wrong-year/missing references are refused. CI runtime matrix added.
- No 2024/2026 Autodesk API probes, full add-in binaries or new live evidence. Current prepared packages contain only 2025. See docs/COMPATIBILITY.md for the acceptance gates.

### Terrain interoperability with Revit
- `exchange export_revit` (FullWrite): one visible TIN -> new ZIP containing precise LandXML and provenance/units/coordinate/geometry manifest; stage and post-write byte comparison. No source save or automatic thinning.
- `prepare-revit-terrain.ps1`: guarded extraction, hash/count/unit checks and a separate existing Revit Toposolid rollback-rehearsal request; explicit model/type/level. Included in runtime helpers and plugin/MCPB; PowerShell 5.1 CI gate added.
- Native Revit point import retriangulates; non-convex coverage requires explicit review. Shared coordinates/rotation/elevation and resulting native surface are not established by export or parser tests.
- LandXML doubles now use round-trip precision. 28 tools / 152 actions; 490 Core/Server tests pass; clean plugin 2025 build; 21 helper checks in PS 5.1 and 29 in PS 7 including actual Core -> helper -> local Revit reader.
- Interoperability workflow, selected remaining engineering priorities and live fixture acceptance recorded. No actual transfer, installation or new L evidence; Revit health was blocked by a modal dialog.

### External capability benchmark
- `docs/BENCHMARK.md`: three pinned public Civil 3D MCP repositories, Dynamo, Camber, CTC CIM Project Suite and Autodesk Grading Optimization; source implementations, conditional paths and publisher claims distinguished.
- Prioritized gaps: corridor targets/applied geometry, material QTO, pressure parts/connections, drainage, advanced roads, sheet production and as-built reports, alongside execution/UNDO recovery.
- Reproducible analytical/live-fixture protocol recorded. No competitor installation, performance measurements, product-code changes or new live support evidence in this review.

### Capability expansion
- 28 tools, 151 declared actions; generated catalog and ordered capability gap review.
- `horizun_c3d_capabilities`: server-side action/schema/effect/permission discovery, with explicit non-live evidence scope.
- `horizun_c3d_audit`: read-only units/xref/reference/currency audit, preserving unreadable and unknown counts.
- `exchange export_dwg`: whole-database copy, staging, reopening and structural comparison; no source Save As or destination overwrite.
- Four pressure-network actions on `horizun_c3d_pipes`: paginated list/get including parts and connectivity, verified empty-network creation and rename. Catalog sizing and part placement remain pending.
- CSV/LandXML file exports now require FullWrite, as do PDF/DWG. Fixture tests respect this policy.
- LandXML reads Civil design units and distinguishes international/US survey foot instead of inferring units from INSUNITS.
- 473 Core/Server tests pass and the plugin builds for 2025 with no warnings/errors. New Autodesk paths have no live evidence yet.

### Fixed
- Pipe reads enforce the byte limit even when the final block contains the newline. MCP stdin is bounded and tool-call admission is limited to 32 concurrent calls. Plugin replies are measured in UTF-8 bytes.
- Duplicate drawing basenames/stems are refused with candidates instead of silently accepting the active drawing.
- The C# channel no longer claims query mode is read-only or guarantees rollback of independent transactions/files. Script execution invalidates prior confirmation and undo state; results remain unverified.
- UNDO registration excludes external effects, arbitrary scripts and empty DWG transactions. Modified objects without a prior snapshot produce an explicit incomplete-verification result after UNDO, with a warning against automatic retry.
- PDF output is staged and checked before atomic replacement, retains the previous file as a named backup, and refuses promotion if the destination changes during plotting. CSV and LandXML also stage and validate before publishing.
- Claude registration is prevalidated, refuses an active Claude process, detects concurrent configuration edits, writes atomically and participates in installation rollback together with the installation manifest. Restart state is recorded explicitly.
- Installation rollback attempts every restoration and reports `rollback_incomplete` with errors if any action fails, instead of claiming restoration unconditionally.

### Added
- Codex/Claude plugin manifests, marketplace metadata and setup/project-workflow skills.
- Windows PowerShell bootstrap exposes runtime status and a planned installation tool if the matching prebuilt runtime is missing. Installed server/add-in hashes are checked before forwarding stdio. Packaged distributions carry their release ZIP and pinned SHA-256.
- Portable plugin ZIP and Claude Desktop MCPB builder; no SDK/Python requirement for prebuilt client installation.
- Isolated PowerShell bootstrap/registration/package gates and CI coverage alongside the ChatGPT tunnel suite.
- Cross-product review with Revit, Navisworks, Power BI and Microsoft Project in `docs/CROSS_PRODUCT_REVIEW.md`.

### Evidence and limits
- Initial parity gate: 445/445 Core/Server tests; capability expansion: 473/473; current interoperability block: 490/490. Plugin 2025 release build: zero errors/warnings. PowerShell 5.1/7 bootstrap tests include forwarding and capability discovery against the published server. Registration and 111 ChatGPT checks passed in the preceding block; rebuilt distribution gates and updated workflow skill validation pass.
- Packaging default output directory now resolves after parameter binding, fixing an observed PowerShell 5.1 `-File` failure when OutDirectory was omitted. Default invocation and package gates pass.
- Permission profiles retain Revit-compatible global scope per Windows user; owner dialogs and instructions now disclose it. The earlier classification of shared permissions as a defect is withdrawn.
- No installation, real drawing mutation, commit, push or publication was performed. Host behavior of these fixes still requires a live fixture regression. Complete modified-object UNDO verification needs captured prior values.

## v0.8.0 - 2026-10-04 - undo_last; previous connector retired; first GitHub release (Claude Code)

Contract `550ff5cb0f7cdce48d7194d5` (restart the MCP client).

### Added
- **`horizun_c3d_document` `undo_last`** (safe_write, dry run + token): undoes the LAST Horizun write as one Civil 3D UNDO step.
  - It runs **only while the drawing has not changed since that write**. A user edit, another write, a save or a background update by Civil 3D makes it refuse, so it can never revert someone else's work.
  - Every committed write now records what it created and the drawing state right after it (`CommandContext.Write`).
  - After the UNDO the tool re-checks that the created objects are gone. One level only: a second `undo_last` is refused.
  - This is the only command the bridge ever runs, inside its own command context and only on an explicit, confirmed request.
  - `verify_live.py` uses it for its UNDO step, which is automatic again.
  - **Built and unit-tested; not yet live-verified.**

### Removed
- **Previous connector** (patched Sacred-G, `civil3d-mcp`) retired at the owner's request. Its Claude Desktop registration, its Civil 3D plug-in (`Civil3DMcp.bundle`), its code and its handoff notes were removed from the owner's machine, after a verified backup zip outside the repo. It exposed an unprotected C# channel over TCP 8080.

### Distribution
- First GitHub release: the release edition package (see v0.7.4 "Distribution").

## v0.7.4 - 2026-10-04 - publish associates the drawing; two instances; shortcut test closed (Claude Code)

**Live evidence (v0.7.3, Civil 3D 2025, 2026-10-04):**
- Data shortcuts work end to end. The MCP published HZ_ROAD; HZ_EG had been published earlier over the C# channel.
- From a NEW drawing in a second Civil 3D window, the MCP referenced HZ_ROAD with automatic association (verified: 90 m, not editable) and HZ_EG (verified: z(25,75) = 101.25, 121 points, rename refused).
- Two simultaneous Civil 3D instances: without a choice every call is refused as `ambiguous` with both pids. `horizun_c3d_target {pid}` switches between them inside one session.
- The test project lived on the owner's Desktop. Afterwards the original working folder (`C:\Users\Public\Documents\Autodesk\Civil 3D Projects`, no project) was restored with `shortcuts_project`, and the profile went back to safe_write.

### Fixed (found live; built, publish path not yet re-run live)
- After a Civil 3D restart, a drawing not associated with the project cannot read the published list and reports every item as unpublished. The publish worked on disk, but the verification failed. `shortcuts_publish` now associates the drawing in the apply, publishes only what is not published yet (`already_published`), and verifies through the published list, with a fallback note when the shortcut file is on disk only.

### Noted
- The session safety net also runs when a SECOND Civil 3D window opens: its plug-in start lowers an elevated profile. This is the conservative side, but the owner may need to press the switch again after opening another window.

### Distribution (GitHub)
- **Two editions.** `development` is the default from source and adds a "Desarrollo" ribbon panel with Sondeo API and Dibujo de ensayo. `release` (`-p:HorizunEdition=release`) is the edition people use, without that panel. `HZ_STATUS` shows the edition.
- **Installable release package.** `install.ps1 -Edition release -PackageOut <zip>` writes a zip with the prebuilt bundle, the server, install.ps1 and LEEME.txt. Run inside an extracted package, install.ps1 installs the prebuilt files: no SDK and no compile on the user's machine. The usual SHA-256 verification and rollback still apply.
- **Repository hygiene.**
  - Personal paths and client drawing names were removed from the published handoff docs; the unsanitized originals stay in `.local/`, which git ignores.
  - The retired connector's still-valid API findings are now `docs/API_NOTES_CIVIL3D_2025.md`.
  - GitHub Actions runs the Civil-3D-free tests on `develop`, `main` and pull requests.
- **Previous connector retired.** `verify_live.py` no longer uses its TCP 8080 command channel; the UNDO check is manual (the bridge never sends commands). The docs point to the own C# channel, switched on by the owner.

### Tooling
- `scripts/mcp_call.py` gains `Session`: one live server for dependent calls, the way an MCP client works. It keeps the instance chosen with `horizun_c3d_target`.

## v0.7.3 - 2026-10-04 - data references live (Claude Code)

**Live evidence:** a data reference of HZ_EG into a new drawing was created and verified through the MCP. The referenced surface answers z(25,75) = 101.25 exactly, has 121 points, is reported not editable ("edit it in its source drawing") and refuses a rename. The plug-in also turned the elevated profile back to safe_write at Civil 3D startup ("turned off at startup"), which is live evidence of the session safety net.

### Fixed (found live)
- `CreateReference` and the published list fail ("Can't get data shortcuts from active project") until the HOST drawing is associated with the current project; the Civil 3D UI does this implicitly. `shortcuts_reference` now associates the drawing in the apply, says so in the plan, verifies the association, and resolves the source from the published shortcuts after associating. A drawing associated with ANOTHER project is refused, never re-associated.
- `shortcuts_status` explains why the published list is unavailable: no shortcuts yet, or the drawing is not associated.
- `shortcuts_project` `name` is now optional (working folder only), to restore a previous setting that had no project.

## v0.7.2 - 2026-10-04 - data shortcuts: live findings (Claude Code)

The live shortcut test (test project `HZ_PRUEBA` on the owner's Desktop, created with `shortcuts_project`) showed:
- `GetCurrentProjectFolder` returns the project NAME, not its path, so the publish check "drawing inside the project" always refused. The path is now working folder + name.
- `GetPublishedItemsCount` throws "Can't get data shortcuts from active project" while the project has no shortcut yet, and the status lost the publishable list along with it. The two lists are now read independently (`published_unavailable_reason`), and every published-list read is guarded.
- Publishing does NOT require associating the drawing to the project. This was checked live by publishing HZ_EG over the C# channel: `SaveDataShortcutManager` → published=1, XML in `_Shortcuts\Surfaces`.

## v0.7.1 - 2026-10-03 - data shortcut project action + live shortcut test (Claude Code)

### Added
- **`horizun_c3d_exchange` `shortcuts_project`** (FULL WRITE): sets Civil 3D's data-shortcut working folder and creates or selects a project in it. This changes the user's Civil 3D setting, so the plan returns the previous folder and project, and the same action restores them.
- `scripts/verify_shortcuts.py`: phased live test of data shortcuts (project → owner SAVE AS → publish → owner NEW → reference → restore), with analytic checks on the referenced EG and alignment.

## v0.7.0 - 2026-10-02 - "Escritura completa" switch (Claude Code)

**Live evidence (2026-10-02 12:45, Civil 3D 2025, fixture, profile temporarily raised to full_write with the owner's authorisation and restored afterwards): 294/294 PASS** (`verify-20261002-124521.json`). The FULL WRITE actions now pass APPLIED, not only refused: entity erase, plot_pdf (valid PDF, 1 page), layout delete, purge and point erase.

### Added (owner request)
- **"Escritura completa" button** on the Horizun Hub ribbon, plus the `HZ_FULLWRITE` command. It raises the permission profile to `full_write`: erase, purge, layout delete, plot_pdf, point erase, shortcut publish and save.
  - Turning it on asks for confirmation; turning it off lowers the profile again.
  - Same session rules as "Canal C#": the previous profile is remembered in the shared `csharp_session_restore_profile` key and restored when the switch is turned off or at the next Civil 3D start.
  - It never lowers an unsafe_code profile when turned on. Turning it off also closes the C# channel.
  - Icon: an open padlock. The button shows its state.
  - Same model as the Horizun Revit MCP, which uses the same four profiles and has an owner consent button for its Python channel.
- `CSharpChannel.EnableFullWrite` / `DisableFullWrite` in Core, with 2 new tests (434/434).

## v0.6.9 - 2026-10-02 - ninth live run 283/284; "Canal C#" switch (Claude Code)

**Live evidence (tenth run, 2026-10-02 12:31, Civil 3D 2025, fixture): 284/284 PASS** with no aborts; report `verify-20261002-123122.json`. This covers every tool from v0.4.0 to v0.6.9, plus UNDO.

The ninth live run of v0.6.8 gave **283/284**, with no aborts.
- Sections are computed and correct live: EG at station 40 between 101.3 and 101.5.
- Real structures pass, with their sumps checked against the incoming inverts.
- Whole-drawing reads over a drawing with sections and a corridor pass.
- The only failure was test data: a crown exactly AT the rim (100.6 = 100.6) is not "above" it. The verifier now uses 100.58.

### Added (owner request)
- **"Canal C#" button** on the Horizun Hub ribbon, plus the `HZ_CSHARP` command. It turns `horizun_c3d_execute_csharp` on or off.
  - Only a person can use it, inside Civil 3D. Turning it on asks for confirmation and explains that C# results are self-reported.
  - It writes `unsafe_code` + `enable_execute_csharp` and remembers the previous profile in `csharp_session_restore_profile`.
  - Turning it off, or the **next Civil 3D start**, restores that profile.
  - An invalid settings.json is never rewritten, so it keeps failing closed.
  - The button shows its state ("encendido"/"apagado") and has its own icon (braces with a keyhole).
  - Logic lives in `Core/CSharpChannel.cs`, with 7 tests.

### Fixed (found live)
- `add_pipes` refused the size name shown by `list` ("100 mm Concrete Pipe") because only the catalog key (PrtSN) was matched. It now accepts the catalog key or any text value of the size record.

## v0.6.8 - 2026-10-02 - eighth live run: 278/280 + live inspection (Claude Code)

The eighth live run of v0.6.7 gave 278/280. The two remaining failures were inspected live, by reflection over the legacy connector's C# channel.

### Findings and fixes
- **The "structures" were Null Structures.** The verifier took the first structure family, which is "Null Structure", a virtual connection point whose rim and sump follow the pipe (rim = sump = 98.73). This was not a tool defect. `add_pipes` now skips sump logic on `PartType.StructNull` and says so, and the verifier picks a real structure family.
- **Sections stayed pending forever.** Sampling the sources AFTER creating the lines left every section uncomputed (0 points). A later read-only access aborted Civil 3D: the live inspection script itself aborted it this way. Sources are now sampled first, with `UpdateMode = Dynamic`, as the Civil 3D UI does, and then the lines are created.
- The verifier now ends the sections block with whole-drawing reads (drawing_report, entity query, layer list) to prove that a drawing with sections and a corridor survives them.

## v0.6.7 - 2026-10-02 - seventh live run: 278/280 (Claude Code)

The seventh live run of v0.6.6 gave **278/280**, and the structures now pass. The pipe itself is exact live: inverts 98.6 → 98.2, 1 %, 40 m, both connections, cover 1.875 to 3.075.

### Fixed (found live)
- **Sections read empty:** Civil 3D computes section points on first access, and the always-aborted read transactions rolled that computation back every time ("LeftOffset is not supported", 0 points). `create_sample_lines` now computes every section inside the committed write.
- **Sumps after connecting pipes:** Civil 3D left the sumps above the incoming inverts (98.73 for 98.6) and ignored the stored depth. `add_pipes` now sets each touched structure's sump to its lowest connected invert minus its sump depth, and verifies it.

## v0.6.6 - 2026-10-02 - sixth live run: 278/280, no aborts (Claude Code)

The sixth live run of v0.6.5 gave **278/280**. No Civil 3D abort: sample lines, section views, assemblies, corridor, regions and rebuild all pass live, and so does UNDO.

### Fixed (found live)
- **`get_section`:** surface sections throw "LeftOffset is not supported", and that exception discarded the points already read. Points are now read first. Every property is optional, and the elevation range falls back to the point Y values (`elevation_source` says which was used).
- **Pipes, sump semantics:** Civil 3D measures sump depth below the LOWEST CONNECTED PIPE invert, so with no pipes the sump stays at the rim.
  - `add_structures` now stores SumpDepth, reports it as pending in `unverified_unreadable`, and no longer fails on it.
  - `add_pipes` now checks that each connected structure's sump is at or below the lowest invert it receives.
  - Name, family and size reads are guarded (they threw "Retrieve attribute failed").
  - The contract text for `sump_depth` was updated, so the contract hash changes: restart the MCP client.

## v0.6.5 - 2026-10-02 - fifth live run: 259/275; sample-line abort located; ribbon icons (Claude Code)

The fifth live run of v0.6.4 gave **259/275**, including the UNDO check. The new stage log located the sample-line abort exactly: the group, the 10 lines and the EG sampling were all created and committed, and Civil 3D aborted in the VERIFY read.

### Fixed (found live)
- **Sections:** reading freshly created sample lines makes Civil 3D update their pending sections, which aborts the process if they are open only for read. The verify of `create_sample_lines` and `create_section_views` and the `list`/`get_section` reads now open groups, lines and sections FOR WRITE, inside the always-aborted read transaction, so nothing changes.
- **Pipes:** on a structure without pipes, SumpDepth does not take (the sump read back as the rim) and some reads throw "Retrieve attribute failed". The sump is now set as an elevation (rim - depth). Every verify read is guarded, and unreadable values are listed as `unverified_unreadable`, never invented. `add_pipes` no longer refuses on the sump of an unconnected structure (unreliable); the crown-above-rim refusal stays.

### Added
- **Icons on the Horizun Hub ribbon buttons** (owner request), drawn in code with no binary assets:
  - Horizun navy tile with the horizon line;
  - glyphs: a pulse for "Estado del puente", a magnifier with code for "Sondeo API", and terrain with a TIN for "Dibujo de ensayo";
  - 32 px and 16 px. Icon failures are logged and never cost the button.

## v0.6.4 - 2026-10-02 - fourth live run: 252/264 (Claude Code)

The fourth live run of v0.6.3 gave **252/264**. Roads up to the profile view, labels, all of block C (layers, entities, dimensions, styles, blocks, tables, layouts, cleanup), points and LandXML pass live. `create_sample_lines` still aborts Civil 3D. It now runs last in the verifier, and the UNDO check was moved before the blocks.

### Fixed (found live)
- **Pipes:** the metric catalog stores inner diameters in **mm**, which gave a "crown 198.6" for an invert of 98.6. Catalog values are now converted with `PartDataField.Units` to drawing units, and unknown units are refused.
- **Pipes:** structure properties can throw "Retrieve attribute failed", for example SumpDepth with no pipes connected. Reads are now safe, and the sump check falls back to rim - sump elevation.
- **Labels:** `SurfaceContourLabelGroup` throws on RangeStart/RangeEnd. This also broke `labels list`, which is fixed too. Label style names are now read from StyleBase (they were null).
- **Labels:** Civil 3D also labels the end station when it is not a multiple of the increment. The expected count includes it and is now verified: 6 labels for 0-90 every 20.
- **Entities offset:** offset polylines have mitred corners, √2·d at a square corner, so the distance is now measured at segment midpoints.
- **Exchange:** `GetPublishedItemsCount` also throws with no active project; status answers with `unavailable_reason`.

### Diagnostics
- `WriteFlow` logs each stage (apply start, body done, committed, verify done), and `create_sample_lines` logs each native call. The next fatal abort will show the exact call in `logs\plugin-2025-<pid>.log`.

## v0.6.3 - 2026-10-02 - third live run: second fatal abort in create_sample_lines (Claude Code)

The third live run of v0.6.2 passed the whole road block up to the sample lines: both profiles now verify, plus the profile view and the K check. Civil 3D aborted again (`eNotOpenForWrite`) when applying `create_sample_lines`.

### Fixed (found live)
- `SampleLine.Create` needs its SampleLineGroup **open for write** while it adds each line. The group is now opened ForWrite before the creates, and IsSampled is set on that same open group. This is a fatal, uncatchable abort in acad.exe, so it cannot be guarded by try/catch.

## v0.6.2 - 2026-10-02 - second live run: fatal crash fixed (Claude Code)

The second live run of v0.6.1 confirmed that the stale-token fix works: alignments, offset, profiles, profile view and K check all passed. Then Civil 3D aborted with `INTERNAL ERROR: !dbobji.cpp@8703: eNotOpenForWrite` while applying `sections create_sample_lines`.

### Fixed (found live)
- **Fatal crash in create_sample_lines:** the apply held the alignment open (for read) while `SampleLineGroup.Create` / `SampleLine.Create` modified it. The end points are now computed in the plan, and the apply only calls the native create methods, with nothing of ours open.
- **Profile describe:** `ProfilePVI.GradeIn` throws on the first PVI (and `GradeOut` on the last). Those grades are now null. Both profiles were created correctly, but the final re-read had failed on this.

## v0.6.1 - 2026-10-02 - first live run of v0.6.0: fixes (Claude Code)

First live run on the fixture (Civil 3D 2025) gave 139/226. Almost every failure came from one cause.

### Fixed (found live)
- **Every token went stale once a dynamic offset alignment existed.** A probe of database events with the drawing idle found `AcDbAssocNetwork` modified about once per second: the AutoCAD associative framework re-evaluates on idle. `DrawingRevision` now ignores `AcDbAssoc*` bookkeeping objects. Real edits still modify real entities, so tokens still go stale on real changes.
- `purge_preview`, `purge` and `drawing_report` threw IndexOutOfRange because `new ObjectIdCollection(emptyArray)` throws in AutoCAD. They now use `Cad.Ids()`, which builds the collection by Add.
- Layout and mleader refusals threw InvalidCastException: LINQ `Cast<DBDictionaryEntry>()` over a DBDictionary yields `DictionaryEntry`. They now use `Cad.Entries()`, the typed enumerator.
- `labels list` threw InvalidCastException: `RXObject.GetClass(typeof(LabelBase))` also matched a TinSurface. The type test now decides.
- `shortcuts_status` with no active shortcut project threw "Can't get data shortcuts from active project". It now answers with `unavailable_reason`.
- `points export_csv` now accepts `numbers`.

## v0.6.0 - 2026-10-02 - phase 3: roads, labels, AutoCAD, networks and data (Claude Code, overnight block)

Phase 3 was built in one long block (owner instruction: build every block, then test them all in one live session). Contract `994cb37f0c96babb3de87993`, 26 tools (restart the MCP client). **No live evidence yet for v0.4.0, v0.5.0 or v0.6.0.**

### Infrastructure
- `ToolRules` / `ActionSpec` in Core: per-action required/optional fields, effect and extra checks.
  - It refuses unknown actions, unknown fields, writes without `target_document`, and `dry_run`/token on reads.
  - The same rules run in the server and in the plug-in.
- Effects per action are derived from the specs, so read actions stay reads.
- `WriteFlow` in the plug-in: plan (read transaction) → dry run + token → apply in command context (one UNDO) → verify in a new transaction. `Resolve` gives a shared name/handle/style/layer resolution that refuses with candidates.
- Generic `BlockToolTests` keep every action tool consistent with its schema: action enum, every field used, effects, writes need `target_document`.

### Added (block A, roads)
- **`horizun_c3d_alignment`** (get, station_offset both ways, create_from_polyline, create_by_pis, create_offset):
  - create_by_pis takes radii and checks that each curve fits before writing;
  - create_offset re-reads the offset distance.
- **`horizun_c3d_profile`** (get, elevation_at, check_k, create_from_surface, create_layout, create_view):
  - check_k uses YOUR minimum K;
  - create_from_surface is checked against the surface at 11 stations;
  - create_layout takes PVIs with symmetric parabolas and re-reads every PVI and curve.
- **`horizun_c3d_sections`** (list, get_section, create_sample_lines, create_section_views): sample lines are built from exact end points, and widths and stations are re-read.
- **`horizun_c3d_corridor`** (get, assembly_list, assembly_create, assembly_import, create, add_region, rebuild, create_surface):
  - assembly_import copies a complete assembly from a DWG; stock subassemblies have no API;
  - add_region refuses overlapping regions;
  - create_surface refuses link codes the corridor does not have.

### Added (block B, Civil labels)
- **`horizun_c3d_labels`**, with 15 actions:
  - list_styles over 15 families; list; get;
  - alignment station and geometry labels; station/offset; surface spot, slope and contour labels;
  - profile PVI labels; station/elevation labels;
  - note, segment, set_text, erase (labels only).
- Each label is re-read: feature, style, anchor, station/elevation, ratio, text.
- Creating a label does not require the feature to be editable, so data-shortcut references can be labelled.

### Added (block C, AutoCAD inside Civil 3D)
- **`horizun_c3d_layers`**: create and set, including linetypes loaded from acadiso.lin, true colour, lineweight and transparency. set_current, plus native layer states (save, restore, list).
- **`horizun_c3d_entities`**:
  - query by type, layer, block or window; get;
  - draw 9 types (line, polyline with bulges, 3D polyline, circle, arc, text with 13 justifications, mtext, point, hatch), with geometry re-read;
  - set_properties;
  - transform (move, copy, rotate, scale, mirror), checked by the transformed anchor point and the length/area invariants;
  - offset, checked by distance; explode, checked by summed length;
  - join, rehearsed on an in-memory clone first;
  - erase (FULL WRITE).
- **`horizun_c3d_dimensions`**: linear, aligned, angular, radial, diameter, ordinate, chain/baseline, mleader and set_text. The Measurement is compared with the analytic value of the caller's points.
- **`horizun_c3d_cad_styles`**: text styles, dimension styles (22 whitelisted dimvars), mleader styles, current styles, annotation scales and linetype loading.
- **`horizun_c3d_blocks`**: define (geometry plus attribute definitions), insert (creates the AttributeReferences, sets dynamic properties), set_attributes in batch for title blocks, set_dynamic, and import from a DWG (never overwrites).
- **`horizun_c3d_tables`**: create from rows or CSV, with every cell re-read; set_cells.
- **`horizun_c3d_layouts`**: list, devices/media, create (empty, copy, or from a template DWG layout), rename, delete (FULL WRITE), viewport (scale, view centre, frozen layers, lock), page_setup, and plot_pdf (FULL WRITE, foreground plot; verifies the %PDF header and the page count).
- **`horizun_c3d_cleanup`**: purge_preview; purge (FULL WRITE, multi-pass); drawing_report; xrefs and xref_reload; standards_check against a JSON standard.

### Added (block D, networks and data)
- **`horizun_c3d_pipes`**:
  - catalog: parts lists → families → sizes with inner diameter; list;
  - create_network;
  - add_structures (rim from the reference surface);
  - add_pipes (explicit inverts; refuses a crown above the rim or an invert below the sump; re-reads inverts, |slope|, centre-to-centre length and both connections);
  - validate against YOUR cover and slope criteria.
- **`horizun_c3d_points`**:
  - list and groups;
  - create (taken numbers are refused);
  - import and export_csv, using our own PNEZD-family reader and writer;
  - elevations_from_surface;
  - group_create: membership is compared with our own evaluation of the query;
  - erase (FULL WRITE).
- **`horizun_c3d_exchange`**:
  - shortcuts_status;
  - shortcuts_publish (FULL WRITE; needs the drawing saved inside the project);
  - shortcuts_reference;
  - export_landxml: LandXML 1.2 **written by Horizun** (Civil 3D has no .NET export) with TIN points and faces, tangent/arc alignments and layout profiles. It never overwrites, and the file is re-read and compared.
- `AeccDataShortcutMgd.dll` is referenced and loaded on demand.

### Changed
- The full-write refusal message now lists every full-write family. The server instructions describe blocks A to D and what has no API.
- `ProfilePVI.Station` is replaced by `RawStation`, because Station is obsolete in 2025.

### Fixture and live verifier
- New fixture entity `road_centerline` (5,50)-(95,50), 90 m. The EG profile is analytic: 100.6 + 0.02s.
- `scripts/verify_blocks.py` runs blocks road, labels, cad and data with analytic checks:
  - crest K = 3.0;
  - PIs alignment length 89.537;
  - EG section at station 40 between 101.3 and 101.5;
  - square area 400; dimensions 30, 50, 90° and 10/15/20;
  - pipe 98.6 → 98.2 over 40 m;
  - point 9001 at z 101.3;
  - LandXML with 121 points.
- FULL WRITE steps pass as "refused under safe_write", or are verified when the owner enables full_write.
- `verify_live.py` calls it before the UNDO step. It can also run alone: `python scripts/verify_blocks.py road labels`.
- API dumps: phase3 roads, sections, labels, label styles, label members, cad, plot, pipes, points, data shortcuts.

### Measured
- 425/425 tests pass. The solution builds with 0 errors and 0 warnings in the product projects; the 2 xUnit analyzer warnings in ServerTests predate this work.
- The server smoke test lists 26 tools and refuses a FULL WRITE under the default profile.
- All 24 plug-in commands match `Contract.PluginCommands`.

## v0.5.0 - 2026-10-01 - phase 2 core: geometric grading, feature lines, C# escape hatch (Claude Code)

This is a long build block on top of v0.4.0 analysis. Both ship together in ONE live test session. Contract `1b170c074fb05925d78a2db6` (restart the MCP client).

### Added
- **`horizun_c3d_grading` `create_geometric`**: the geometric grading engine, ported from the validated `grading_engine.csx` into Core (`GradingEngine`) and covered by analytic tests.
  - Inputs: a closed polyline / 3D polyline / feature line footprint, outer steps (offset, grade_to_surface with cut and fill H:V slopes and fans at convex corners) and inner steps (offset, grade_to_depth, grade_to_elevation).
  - Creates closed 3D polylines plus a NEW TIN with every line as a breakline and the outermost line as the outer boundary.
  - The dry run lists every line and every failed daylight ray. After commit it re-reads the breakline count, the boundary and the TIN elevation at the line vertices.
  - Optional `volume_against` gives transient cut and fill.
  - Native grading creation is refused by name.
- **Engine hardening beyond the original script:** an inner step whose offset is larger than the shape (the footprint turns inside out and keeps a CCW area) is detected by vertex inset distance and refused. Daylight that leaves the target surface is refused, never invented.
- **`horizun_c3d_feature_line`**:
  - create_from_polyline: unique names, existing site or siteless; re-reads source vertices as XYZ, or XY for 2D sources.
  - set_elevations: from_surface (re-reads every point against the surface), constant, or points by index.
  - rename, and export_polyline3d.
  - Feature lines that are not editable (references, locked layers, Civil `IsEditable=false`) are refused.
- **`horizun_c3d_execute_csharp`**: Roslyn 4.8.0, the same version as the legacy connector so they do not clash in acad.exe. OFF by default: it needs `unsafe_code` and `enable_execute_csharp`.
  - query mode always aborts the transaction; execute mode commits as one UNDO step (UnsafeCode runs in command context).
  - Results are labelled `self_reported` with `host_verified=false`. Compiles are cached by hash.
- **Fixture:** a 20x20 platform at elevation 104 on the sloped EG.
- **verify_live.py now has about 80 steps:** feature lines (EG z from 100.9 to 102.1), grading (no failed rays, verified TIN, all fill > 1000 m3, 104 inside the platform), refusals (open source, collapsing inner offset), and C# off by default.
- API dump: `docs/api-probes/2025/AeccDbMgd.phase2-featureline.txt`.

### Measured
- 200/200 tests pass (Core and Server, under acad's JSON condition). The plug-in for 2025 builds with 0 warnings. `install.ps1 -DryRun` passes; the bundle now carries the Roslyn DLLs.
- No live evidence yet for v0.4.0 or v0.5.0.

## v0.4.0 - 2026-10-01 - surface analysis and style display (Claude Code)

### Added
- `horizun_c3d_surface` now has `apply_elevation_analysis`, `apply_slope_analysis` and `style_display`, for 15 actions in total. The contract changed, so restart the MCP client.
  - **Modes:** equal (N ranges), step (interval with a boundary exactly on `break_at`, so cut and fill never share a band), ranges (explicit `{min,max,color}`) and recolor.
  - **Colours:** ACI 1-255 or `#RRGGBB`. Generic schemes are rainbow, cutfill, reds, blues, greens, grays and land. Legends always come in as parameters.
  - **Slope:** always in PERCENT. The API stores decimal ratios, and the fixture plane at 2.236 % checks this live.
- **Verification:**
  - stored bands are re-read and compared band by band (min, max, colour);
  - grid-estimated area per band is reported, plus volume per band for volume surfaces, reconciled with the native net volume;
  - a warning appears when the surface style does not display the analysis.
- `style_display` sets component visibility, colour and layer in plan, model or both. It is **refused on a style shared by several surfaces unless `allow_shared_style=true`**, and every property set is re-read.
- `SurfaceAnalysisMath` (Core) holds the pure range, palette, colour and band logic. 24 new tests; 162/162 pass.
- verify_live.py adds 15 analysis steps:
  - band layout from -2 to 1;
  - the sum of band volumes equals -5000 m3;
  - recolor;
  - a colour-count mismatch is refused;
  - a shared-style refusal;
  - duplicate, display and assign the slope style;
  - the slope analysis puts 100 % of the EG area in the 2-5 % band.
- API dump: `docs/api-probes/2025/AeccDbMgd.phase1-analysis.txt`.

## v0.3.4 - 2026-10-01 - applied writes are undoable (found live)
- **Final live fixture run: 46/46 PASS on Civil 3D 2025**, including UNDO of an MCP write as a single step. create_volume now passes without re-rehearsal.
- Live result from the third fixture run (v0.3.3): **41/43 PASS**. Every surface write passed dry run, apply and re-read: rename, duplicate_style, set_style, create_tin, add_data with vertices, breakline (z re-read) and outer boundary (area 6400 exact), paste and rebuild. Stale-plan and token-reuse refusals also passed.
  - create_volume was stale only right after a paste, because Civil 3D updated dependent objects in the background. Re-rehearsed, it created the volume and read back cut 5833.333 / fill 833.333 from the new object.
- **Live finding: edits made from APPLICATION context are not on Civil 3D's undo stack.** A native `UNDO 1` did not revert an MCP rename, so the reply "one UNDO reverts this" was false.
  - Applied writes (dry_run=false) now run inside `ExecuteInCommandContextAsync`, which is recorded as one undoable command. Reads and dry runs stay in application context.
- The stale-plan message now names the background-update cause. verify_live.py re-rehearses once in that case (and reports it) and checks UNDO automatically via the native command channel when it is available.

## v0.3.3 - 2026-10-01 - revision observer identity fix (found live)
- The second fixture run was still 18/36. Diffing two identical dry-run plans showed the clock was at 0 (the v0.3.2 Quiet fix works), but the **observer lifetime id changed on every call**.
  - `doc.Database` returns a new managed wrapper on each access, and the `ConditionalWeakTable` keyed by that wrapper created a fresh observer each time, also piling up event subscriptions.
  - Fix: observers are keyed by `Database.UnmanagedObject` and removed when the database is destroyed.

## v0.3.2 - 2026-10-01 - first live fixture run: stale-plan fix

### Measured live (fixture, Civil 3D 2025): 18/36 PASS
- Reads match the analytic values: EG with 121 points, area 10000 and z from 100 to 103; exact plane samples; outside point returned as null; cut 5833.333 and fill 833.333 m3, both from the volume surface and from a transient comparison; no persistent leftover.
- Refusals work: locked layer, existing name, missing name with candidates, wrong drawing, token reuse.
- **Every write was refused as `stale_plan`** between dry run and apply, and nothing was changed.

### Fixed
- The drawing-revision observer counted events fired by the bridge's OWN reads (fingerprints, transient volume surfaces) as edits. Events are now ignored while a bridge command runs (`DrawingRevision.Quiet`), because the user cannot edit while the pump runs. Committed bridge writes and saves advance the clock explicitly (`Bump`).

## v0.3.1 - 2026-10-01 - host JSON fix (found live)

### Fixed
- **Live bug, Civil 3D 2025:** `horizun_c3d_health` failed with "JsonSerializerOptions instance must specify a TypeInfoResolver" and the pipe closed with no reply.
  - Cause: acad.exe runs with reflection-based System.Text.Json serialization disabled, and a `JsonArray.Add("...")` call (the DBMOD kinds) creates a value that needs it.
  - Fix: 14 call sites now add explicit `JsonValue.Create(...)`, and `Hz.Compact`/`Hz.Indented` name `DefaultJsonTypeInfoResolver`.
  - The same latent bug affected references, locked layers, editability reasons, the document-mismatch list and paste verification.
- The test project now runs with `System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault=false`, the same as acad.exe. 138/138 tests pass.

## v0.3.0 - 2026-10-01 - surface geometry and deterministic fixture (Claude Code)

### Added
- `horizun_c3d_surface` now has `add_data` and `paste`, for 12 actions in total. The contract hash is `9c47573fa498bb2cbcd22d8a`.
  - `add_data` adds TIN vertices, ONE breakline group (standard, proximity or non-destructive, from 3D polylines, polylines, 2D polylines or feature lines) and ONE boundary group (outer, hide, show or data_clip, from closed polylines only).
  - `paste` pastes ordered sources and refuses self-paste, volume sources and paste cycles.
  - Both actions use dry run, token, commit, optional rebuild and a new-transaction re-read.
- **Verification is geometric, not "did not throw":**
  - every requested vertex, and every vertex of an unweeded standard breakline, is re-read as a surface elevation (tolerance 1e-4);
  - group and operation counts, the breakline or boundary kind and the description or name are checked;
  - paste operations are checked in the requested order;
  - points hidden by a boundary added in the same call are reported, never counted as a match.
- `HZ_BUILD_FIXTURE` (also a ribbon button) builds, only in a NEW untitled drawing that has no surfaces, a deterministic test drawing whose answers are known analytically:
  - HZ_EG is the plane 100+0.02x+0.01y;
  - HZ_FG is flat at 101;
  - HZ_EG_FG_VOL has cut 17500/3 m3 and fill 2500/3 m3;
  - HZ_LOCKED sits on a locked layer;
  - the drawing also has a 3D breakline, a closed limit and an open polyline.
  - The expected values go to `%USERPROFILE%\.horizun\civil3d\fixtures\fixture-expected.json`.
- `scripts/verify_live.py` drives about 40 PASS/FAIL steps against the fixture. It covers reads against the analytic values, every write (dry run, apply, verified), token reuse, stale plan, refusals and paste/boundary geometry, and writes a JSON report. It refuses to run if the active drawing is not the fixture.
- API dump: `docs/api-probes/2025/AeccDbMgd.phase1-adddata-paste.txt`.

### Measured
- 135/135 Core/Server tests pass. The plug-in for 2025 builds with 0 errors and 0 warnings.
- Installed with `install.ps1 -Years 2025`; 8 bundle files and 13 server files are SHA-256 identical to the build. The installed server reports 0.3.0, 7 tools and 12 surface actions, and refuses a vertex without z before reaching Civil 3D.
- **No live (L) evidence yet**: Civil 3D was closed. The next step is the fixture run.

## Unreleased (v0.2.0) — first surface block

- Add horizun_c3d_surface with ten actions: list/get, elevation sampling, native volume reports, rename, set_style, duplicate_style, create_tin, create_volume and rebuild. Seven MCP tools total; contract 176bfc22a49d728636cc039a.
- Validate action-specific inputs before host discovery; bound batches and sampling. Distinguish measured zero from unavailable data, label estimated grid statistics and report native/grid volume reconciliation.
- Safe writes use default dry run, single-use confirmation bound to drawing revision and resolved plan, all-target resolution under one lock, and fresh post-commit verification. Reject locked/reference targets and name collisions. Verify duplicate style plan/model display settings.
- Read native/transient volume properties in always-aborted transactions. Transient creation may conservatively invalidate earlier confirmation tokens; verify rollback semantics live before production use.
- Record installed-2025 API probes; plugin builds with zero warnings/errors. 119 Core/Server tests pass; complete-installer DryRun passes 111 PowerShell helper checks. Installed with backup and independently verified SHA-256 for all 21 package files.
- Installed initialize/tools/list report version 0.2.0 and seven tools; invalid surface request is rejected before connection. Valid health/list report no_civil3d_instance with Civil closed. **New surface behavior is preview: live validation pending**; no client drawings modified.
- Geometry input, paste, bounded polygon volumes, analyses, style_display and deterministic live fixture remain planned. ChatGPT helper recognizes the expanded catalogue; account credentials and real ChatGPT call remain pending.

## Unreleased (v0.1.1)

### ChatGPT integration helpers
- Add isolated Civil 3D Secure MCP Tunnel helpers, DPAPI CurrentUser credential storage, live poll freshness and process-identity checks; reject foreign MCP identities/namespaces, mismatched profile commands and inherited tunnel overrides.
- Bundle ten helpers with the installer and add helper-only installation with backup, rollback and SHA-256 verification. Add official full tunnel-client downloader with checksum validation and an interactive account-completion command.
- Installed locally with OpenAI tunnel-client 0.0.15. Original eleven server/plugin files remain identical to the preceding deployment. Server/plugin version and contract remain 0.1.1 / 6b2a8fe91890a5452113b9af.
- Validation: 69 Core/Server tests; 111 tunnel checks under Windows PowerShell 5.1 and 111 under PowerShell 7.6.5. Full installer DryRun/SkipTests passed build/publication/helper staging; suites ran separately. Official client init tested only with a temporary fictitious identifier, without account traffic.
- Account tunnel/key, ChatGPT app connection and real ChatGPT tool call remain pending. Installed server answers initialize/tools/list; health correctly reports no_civil3d_instance while Civil 3D is closed.

### Review fixes (implemented; live verification pending)
- Validate every permission-control field before applying defaults. Invalid types, null, duplicate/unknown keys and invalid list entries fail closed to read_only; unreadable settings files are not treated as missing.
- Bind save plans to a lifetime-specific in-memory drawing revision. Object edits (including undo), database-variable changes and view changes invalidate old tokens even if DBMOD and the on-disk timestamp stay unchanged. Keep one document lock across plan capture, confirmation and SaveAs.
- Include drawing/display units in query list/get, object census and save replies.
- Align the build version to 0.1.1 and update contract descriptions: hash 6b2a8fe91890a5452113b9af. Server and plug-in must be deployed together.
- Extend the offline API probe to record event signatures; save the installed 2025 revision-event probes.
- Validation: 69/69 Core/Server tests pass; 2025 plug-in builds with zero errors/warnings. Installer DryRun for 2025 passes tests, build and self-contained publication from the repository; staged bundle XML verified. Installed locally on 2026-10-01 with backup and independent SHA-256 verification of all 11 files; installed-server initialize/tools/list passed. Corrected plug-in behavior awaits live verification after reopening Civil 3D.

### Measured live (Civil 3D 2025, 2026-10-01)

| Check | Result |
|---|---|
| Bundle auto-load, pipe and discovery | pass |
| health, target, document (info / list_open / object_census), query (surface, feature_line), styles, probe | pass on a production drawing |
| FIFO queue with 6 concurrent calls | pass |
| Busy with `LINE` active | `busy`, nothing ran |
| Save with dry run, token and on-disk verification | pass; token reuse and missing token refused |

### Fixed
- A coordinate system code of `"."` is now reported as "no coordinate system".
- The failure note of a verification check is no longer shown when the check passes.
- Verification says *how* it verified: the save is verified on the file on disk.

### Added
- Unsaved changes are now qualified by DBMOD bits (`objects_changed`, `view_only`).

## v0.1.0 - 2026-10-01 - Phase 0: foundation

This is the first build of Horizun Civil 3D MCP as a standalone product.

### Added
- **Server** `horizun-civil3d-mcp.exe`: an MCP server over stdio that supports protocols 2025-06-18,
  2025-03-26 and 2024-11-05.
  - Tool schemas come only from the shared contract.
  - Input is validated before anything reaches Civil 3D.
  - Replies carry `structuredContent`.
- **Plug-in** `Horizun.Civil3D.dll` for Civil 3D 2025, built per year.
  - It runs work on the main thread in application context, under a document lock.
  - It does not start inside a plain AutoCAD.
- **Pipe** `Horizun.Civil3D-<pid>`: restricted to the current user, with a 256-bit token compared in constant
  time.
  - Discovery files are written atomically and restricted to the current user.
  - The server sweeps stale discovery files.
- **Contract hash**: the server refuses a plug-in from a different build.
- **Queue**: bounded FIFO with capacity 16 and cancel-before-start.
  - Busy detection (CMDACTIVE and a disabled main window) refuses with `busy`. Nothing runs, and the bridge
    never sends ESC.
- **Permission profiles**: read_only, safe_write, full_write and unsafe_code, plus allow and deny lists and a
  pause switch. Bad settings fail closed to read_only.
- **Confirmation tokens**: single use and 10-minute TTL, bound to the operation, the drawing, the request hash
  and the resolved-plan fingerprint.
- **`VerificationSet`** for post-commit re-reads. An empty verification is never a pass.
- **Tools**: `horizun_c3d_health`, `horizun_c3d_target`, `horizun_c3d_document` (info, list_open,
  object_census, save), `horizun_c3d_query` (list and get over 15 types), `horizun_c3d_styles` (list and get
  with usage) and `horizun_c3d_probe`.
- **AutoCAD commands** `HZ_STATUS` and `HZ_PROBE`, plus a "Horizun Hub" ribbon panel.
- **`scripts/install.ps1`**: detects years, refuses while acad.exe runs, runs the tests, builds and publishes,
  verifies SHA-256 after install, rolls back on failure and can register in Claude Desktop.
- **`tools/Horizun.Civil3D.ApiProbe`**: offline API dumps, with the 2025 dumps under `docs/api-probes/2025`.

### Measured

| What | Result |
|---|---|
| Unit and end-to-end tests (no Civil 3D) | 47 / 47 pass |
| Plug-in build against the Civil 3D 2025 DLLs (AeccDbMgd 13.7.0.145) | 0 errors |
| `install.ps1 -DryRun` (test, build, publish, stage) | pass |

### Still open
- Live verification in Civil 3D 2025, following the checklist in `docs/CIVIL3D.md`.
- Civil 3D 2026 and 2027 builds, which need those installations; 2023 and 2024 (net48) are not built.
