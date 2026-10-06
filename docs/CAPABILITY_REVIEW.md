# Capability review and next executable improvements

Updated v0.9.0, 2026-10-05. Contract `171049b89c39afc5120da6ff`.
The selected engineering additions are implemented with offline tests and real
reference builds: surface tolerance/RMSE reports, corridor targets/applied
geometry/estimated quantities/guarded split-merge, editable COGO CSV and
explicitly refreshed alignment viewports. Exact OBJ and a Revit rollback mesh
receiver are prepared. Earlier backlog descriptions below are historical;
live acceptance remains pending. See CIVIL3D.md and the dedicated guides.
The branch remains `phase-0/cross-product-parity-review`; no deployment or publication occurred.

The subsequent [external benchmark](BENCHMARK.md) compares seven Civil 3D
automation references using pinned public source and publisher documentation.
It refines the next engineering package to corridor targets/applied geometry
and traceable quantities (P1), followed by complete pressure construction.
Execution/UNDO safeguards and live regression remain prerequisites. Competitor
performance and accuracy were not measured.

The owner's subsequent priority is [terrain interoperability with Revit](INTEROPERABILITY.md).
`exchange export_revit` and its receiver request helper are now implemented;
native creation/placement remains live-pending. Exact mesh transfer, corridor
geometry/targets, quantities, sheets and spreadsheet write-back remain pending.

## Actual inventory

The compiled contract declares **28 tools and 152 named actions**. Six tools do
not use an `action` enum, giving 158 catalog rows. These counts describe the
contract, not 158 independently live-verified workflows. Other parameter modes
and combinations are not inflated into separate actions.

| Effect | Catalog rows | Execution requirement |
|---|---:|---|
| Read | 51 | Most need an available, idle Civil 3D drawing; capabilities is server-side |
| Host state | 1 | Instance selection only |
| Safe write | 93 | Explicit active target, dry run, token and post-commit re-read |
| Full write | 12 | Full-write permission plus the typed confirmation workflow |
| Unsafe code | 1 | Unsafe-code permission and separate C# enable flag; self-reported results |

The full generated list is [CAPABILITY_CATALOG.md](CAPABILITY_CATALOG.md).
Regenerate it after rebuilding Core:

```powershell
dotnet build src/Horizun.Civil3D.Core -c Release
pwsh scripts/export-capability-catalog.ps1 -OutputPath docs/CAPABILITY_CATALOG.md
```

At runtime, `horizun_c3d_capabilities` derives the same information from the
shared contract. Filters are `tool`, `effect`, `query` and `include_schema`.
Permission results describe current settings only, not host readiness. Required
fields are action-specific where `ToolRules` has a rule; otherwise the response
explicitly labels them `tool_schema`, which is not an exhaustive action plan.

## Improvements implemented in this block

| Capability | Implementation and practical limit | Evidence |
|---|---|---|
| Discoverable execution catalog | Actions, effect, current permission checks, required/optional fields and optional JSON schemas | Core/Server tests; no host needed |
| Drawing audit | Units/coordinate-system/xref snapshot, stale or invalid Civil references, out-of-date surfaces/corridors; per-type unknown/unreadable counts and limited problem examples | API probe + 2025 build; live pending |
| DWG delivery | `exchange export_dwg`: clones the current database, writes staging, reopens it and compares local block/entity class counts and insertion units before promotion; refuses existing destination | Path/permission/helper tests + 2025 build; live pending |
| Pressure networks | `pipes pressure_list`, `pressure_get`, `pressure_create_network`, `pressure_rename`; reads pipes/fittings/appurtenances and connectivity, creates an empty network and verifies writes in a new transaction | Installed pressure API probe + validation tests + 2025 build; live pending |
| Correct file permissions | CSV and LandXML exports now require FullWrite, consistent with the declared file-effect policy and PDF/DWG | Permission regression tests |
| Correct LandXML unit source | Civil drawing settings determine meters/feet and international versus US survey foot; insertion units are no longer guessed as design units | Unit-definition serialization tests + 2025 build; live pending |
| Revit terrain delivery | One visible TIN → LandXML/manifest ZIP, full-payload comparison, source geometry hash and explicit units/controls; helper emits a separate native Toposolid dry-run request | 490 current Core/Server tests; clean 2025 build; actual local Revit reader tested without a host; full transfer live pending |
| Updated fixture regression | Audits, empty-pressure-network creation/rename and DWG export; CSV/LandXML respect FullWrite/refusal instead of assuming safe-write permission | Python syntax check; fixture execution pending |

DWG verification is structural. It does not prove every Civil design value,
relink data shortcuts or include xref source files. Pressure diameter values
marked `api_raw` retain their catalog-unit uncertainty. The audit does not
establish design-code compliance and currently covers its declared gravity and
design-object types, not a complete pressure-network health assessment.

## Comparison with the other local MCP products

The baseline references remain Revit `e204f310`, NavisCoord 1.1.1 `e19e49d`,
Power BI 2.1.2 `9c64386`, and Project `af99478`, inspected read-only. See
[CROSS_PRODUCT_REVIEW.md](CROSS_PRODUCT_REVIEW.md) for the architecture and
installation comparison and source locations.

| Applicable pattern | Civil 3D outcome |
|---|---|
| Revit: shared contract, bounded transport, explicit permission profiles | Preserved; transport/concurrency fixes and discoverable action/effect catalog |
| Navisworks: document identity and explicit measured write evidence | Duplicate drawing names refused; stale plan checked; audit records unknowns explicitly |
| Project: dry-run plan and independent state verification | Typed writes retained; pressure writes reread; atomic multi-operation plans remain a gap |
| Power BI/Project: plugin runtime lifecycle, repair and packaged distribution | Launcher, status/install flow, portable plugin/MCPB and package integrity gates added in the preceding block |
| All: meaningful workflows across several actions | Civil coverage includes surfaces, grading, roads, sections, labels, gravity networks, COGO points, drawing sheets and deliverable exports |

Tools for DAX, Navisworks clash sets, Revit families or Project scheduling do not
map directly to Civil 3D. The applicable part is the execution and verification
pattern, with Civil 3D-specific commands and units.

## Remaining work, ordered by value

| Priority | Gap | Concrete completion criterion |
|---|---|---|
| P1 | Live regression and deployment | Install matching server/add-in after saving and closing Civil 3D; exercise new actions on the generated fixture, including refusal/failure cases and source unchanged after DWG export |
| P1 | Complete modified-object UNDO evidence | Capture pre-edit values for eligible typed commands and compare them after UNDO; avoid declaring value restoration from the command return alone |
| P1 | `execute_plan` for multi-step design | Resolve references and dependencies, validate every step, execute one drawing transaction and verify each output; distinguish external-file effects that cannot be rolled back |
| P1 | Pressure part catalog/creation/connections | Probe sizes and units; explicit family/size selection; reread coordinates, dimensions and both port connections; test with an authorized catalog fixture |
| P1 | Corridor targets/applied geometry and material quantities | Probe target mappings, computed coded points/shapes, sample-line material lists, criteria/import and quantity reports; validate volumes against analytic fixtures before CSV/Power BI output; see BENCHMARK.md |
| P2 | Spiral alignments and LandXML | Confirm supported curve families and all required export attributes; round-trip lengths, endpoints and directions; refuse unsupported types explicitly |
| P2 | Feature-line site transfer | Probe site-transfer behavior, validate dependencies and reject unsafe site interactions; reread geometry/site |
| P2 | `capture_view` | Probe a non-UI AutoCAD graphics API, return a real image and prove dimensions/format; keep screenshot evidence distinct from numeric verification |
| P2 | Excel/Power BI integration | Stable data schema with units, null reasons, handles and drawing identity; reconcile totals with the drawing and preserve model/report boundaries |
| P2 | Coverage by year | Compile and then test actual 2026/2027 hosts; no cross-year support claim from a 2025 build |
| P3 | Other documented host gaps | Busy modal dialogs, proximity breaklines, orphan vertical-alignment census and point-index edge cases |

`execute_plan` is not implemented by repeatedly calling current write tools:
each command owns its transaction and verification, and some change files or
process settings. A loop would allow partial commits and would not satisfy the
promised all-or-nothing behavior. Its transaction adapters and effect boundaries
need implementation and live failure-injection evidence.

Native grading, automatic intersection/view-frame creation and stock
subassembly creation remain documented API gaps. New support needs a verified
public API; typed geometric grading, layouts/viewports and assembly import
provide the current practical alternatives.

## Verification

490 current Core/Server tests pass (473 before the interoperability block); plugin 2025 builds with zero warnings/errors. New
Autodesk signatures are saved under `api-probes/2025/`. Exact package and client
gates are recorded in `handoff/SESSION_LOG.md`. Historical live evidence belongs
to earlier versions; this block adds no L evidence.

The Civil unit distinction is also described by [Autodesk's Units and Zone
documentation](https://help.autodesk.com/cloudhelp/2024/ENG/Civil3D-UserGuide/files/GUID-A2C0D8F7-0BCF-46F6-8A0E-217834AF136B.htm).
