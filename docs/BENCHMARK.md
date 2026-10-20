# Civil 3D automation benchmark and capability gaps

Review date: **2026-10-05**. Horizun baseline: prepared **v0.8.1**, shared
contract `60ab4ce4eb7756190fa7f4bf`, **28 tools / 151 named actions**.

This is the baseline at the time of the benchmark. The subsequent owner-selected
[interoperability block](INTEROPERABILITY.md) adds `exchange export_revit` and
receiver request preparation: current contract `b5cfe04b65b9b303d9241d4b`,
28 tools / 152 actions. Exact mesh transfer and live Revit placement remain pending.

## Scope and method

This is a **capability benchmark based on source inspection and primary
documentation**. Competitors were not installed, compiled or run. There are no
measured latency, success-rate, numerical-accuracy or usability rankings.
Publisher claims and source handlers do not establish live operation.

Horizun's action inventory comes from [CAPABILITY_CATALOG.md](CAPABILITY_CATALOG.md)
and the shared contract. Six additional tools have no action enum: there are
157 catalog rows, not 157 independently verified engineering workflows.
[CIVIL3D.md](CIVIL3D.md) separates source, tests, compilation and live evidence.
The last local gate recorded 473 passing Core/Server tests and a clean 2025
plugin build; it was not rerun for this documentation-only review. New v0.8.1
host features are still awaiting live regression and deployment.

Evidence labels used below:

| Label | Meaning |
|---|---|
| S | A relevant source implementation was inspected; no competitor execution |
| C | Conditional implementation, reflection or a known unsupported path |
| D | Publisher documentation; implementation not inspected |
| P | Partial coverage in Horizun's current typed contract |
| Gap | No dedicated typed workflow in the reviewed Horizun contract |

Generic C# execution can potentially reach additional APIs. It does not count
as a typed workflow with validated inputs, permission checks and independent
verification. Generic object queries and client-side calculations are credited
where relevant, without treating them as completed design/report workflows.

## Reference products and reproducible snapshots

Public source files were cached under ignored `.local/benchmark/` for read-only
inspection. Only links and conclusions are included here; no competitor code
was incorporated into Horizun.

| Reference | Snapshot | Relevant evidence and limit |
|---|---|---|
| Sacred-G / Civil3D-mcp | `9e437a3bab6df4ba13fabc2e909e138e8ac15896`, commit 2026-08-16 | Domain/action MCP architecture, hydrology, corridor editing, quantity aggregation and jobs; several advertised operations have unsupported/conditional handlers |
| DaniGhosy / civil3d-mcp-civil3d-mcp | `10b19005b49a053ee4c3c93c768eb46539d9016f`, commit 2026-10-04 | Typed pressure catalogs and part creation; planned operations and reflection-based sheet handling are distinguished |
| xuantinhnbs-rgb / civil3d-mcp | `ca2f6a336e80e2745a217c77eed8f768378e751b`, commit 2026-09-19 | COM-based geometry extraction and documented as-built comparisons; COM target/surface limitations are explicit |
| Dynamo for Civil 3D | Autodesk 2026/2026.2 documentation, accessed 2026-10-05 | Native nodes provide a functional reference; 2026 functionality is not evidence of the same 2025 API |
| Camber | `edb69d2e76474536f7c51b5ad22be0536d1a005d`, commit 2025-06-02 | Pressure-network/catalog source patterns; author states active maintenance stopped |
| CTC CIM Project Suite | Publisher product page and 2026 user guide, accessed 2026-10-05 | Commercial workflow reference; performance claims and API availability were not tested |
| Autodesk Grading Optimization | Official optimization documentation, accessed 2026-10-05 | Constraint/objective solver reference; no public automation integration established here |

The Sacred-G README advertises 34 public tools and 219 extended entries, while
its checked-in generated tool document lists 206 entries. The inspected manifest
selects canonical domain exposures by default and enables aliases separately.
These figures have different scopes and include overlapping operations; they
are not a valid score against Horizun's 28 tools and 151 actions.
[README](https://github.com/Sacred-G/Civil3D-mcp/blob/9e437a3bab6df4ba13fabc2e909e138e8ac15896/README.md),
[generated catalog](https://github.com/Sacred-G/Civil3D-mcp/blob/9e437a3bab6df4ba13fabc2e909e138e8ac15896/docs/tools.generated.md),
[manifest](https://github.com/Sacred-G/Civil3D-mcp/blob/9e437a3bab6df4ba13fabc2e909e138e8ac15896/src/tools/toolManifest.ts).

## Capability comparison

This matrix describes specific workflow coverage, not product-wide superiority.
Existing Horizun capabilities may have historical live evidence; additions in
the prepared version retain their explicit T/B status.

| Workflow | Horizun today | External reference | Missing completion |
|---|---|---|---|
| Surface modeling and total cut/fill | Existing TIN creation, data/breaklines/boundaries, paste, sampling, volume surfaces and reports | Sacred-G volume implementation, S | Region/material/station accounting; editable definition history and triangle operations |
| Corridor construction | P: assemblies/import, create, add region, rebuild, generate surface | Sacred-G corridor editing, S; CTC corridor management, D | Read/set target mappings, edit existing frequencies/regions/baselines, coded applied geometry, preserve definitions during split/merge |
| Material quantities | P: surface volumes and section geometry; no complete material-QTO workflow | CTC earthwork, D; Xuan corridor shape extraction, S | Material criteria/list mapping, quantities by station/layer, topsoil/subgrade, traceable exports |
| Pressure-network design | P: list/get, empty-network creation and rename; read connectivity | Dani pressure catalog/parts, S; Camber source, S | Enumerate families/sizes, place pipes/fittings/appurtenances, connect ports, edit/verify dimensions and cover |
| Hydrology and hydraulic analysis | Gap: surface sampling and pipe geometry are available building blocks | Dynamo drainage, D; Sacred-G sampled tracing/runoff, S | Catchments, flow paths/Tc, runoff methods, capacity/HGL, storage/detention workflows with declared assumptions |
| Advanced road geometry | P: PI/polyline/offset alignments and layout/surface profiles | Other MCP road/superelevation domains, documented/catalogued | Spirals, station equations, superelevation and design-speed/criteria workflows; validate each API before implementation |
| Sheet production | P: layouts, viewports, title-block attributes, tables, PDF | CTC dynamic sheets, D; Dani sheet methods, C | Repeatable plan/profile series, numbering/matchlines, update behavior, verified sheet-set integration |
| As-built quality reports | P: samples, units, object audit and profile checks | Xuan comparisons, D; shape extraction, S | Independent survey-point RMSE/deviations, profile comparison by station, per-object tolerance results and report provenance |
| Grading design | Existing geometric feature-line/grading workflows; no optimization solver | Grading Optimization, D | Constraint validation, reusable dependency updates and optional optimization integration after API feasibility study |
| Reference/project lifecycle | P: shortcut project/publish/reference operations and stale-reference audit | Other MCP project/reference domains, documented/catalogued | Typed synchronization/repair/promotion with dependency and source validation; delivery reference manifest |
| Multi-step execution | P: individual confirmation tokens, queue, verified writes | Sacred-G jobs and idempotency source, S | Transactional drawing plans, inspectable operation status, timeout reconciliation and complete modified-object UNDO evidence |
| Engineering data exchange | P: COGO CSV, LandXML export, DWG copy and PDF; JSON reads | CTC spreadsheet pipe workflow, D; Xuan report exports, D | Stable quantities/network/report schemas, controlled spreadsheet updates, Power BI reconciliation and delivery packaging |

### Evidence behind the largest differences

**Pressure.** Dani's inspected handler enumerates pressure part lists and uses
typed `AddLinePipe`, `AddFitting` and `AddAppurtenance` calls. Its README explicitly
marks cover editing, network connections and resizing as planned. These planned
items are not credited as implemented competitor advantages.
[Pressure source](https://github.com/DaniGhosy/civil3d-mcp-civil3d-mcp/blob/10b19005b49a053ee4c3c93c768eb46539d9016f/plugin/Civil3dMcpPlugin/PressurePipeCommands.cs),
[scope](https://github.com/DaniGhosy/civil3d-mcp-civil3d-mcp/blob/10b19005b49a053ee4c3c93c768eb46539d9016f/README.md).
Camber is useful as an API reference, but its maintenance notice prevents an
assumption that its historical binary is a current supported deployment.
[Camber](https://github.com/mzjensen/Camber/blob/edb69d2e76474536f7c51b5ad22be0536d1a005d/README.md).

**Quantities.** Sacred-G implements aggregations, but `QtyCorridorVolumesAsync`
and `QtyMaterialListGetAsync` explicitly throw API errors. Its earthwork summary
also refuses station-clipped quantities. A catalog entry therefore does not
establish complete corridor-material QTO support.
[Quantity source](https://github.com/Sacred-G/Civil3D-mcp/blob/9e437a3bab6df4ba13fabc2e909e138e8ac15896/Civil3D-MCP-Plugin/QuantityCommands.cs).
Xuan's inspected corridor code reads applied assemblies and coded shape areas;
its README documents quantity exports and as-built/check-point/profile reports.
It also records COM limitations for assigning corridor targets and creating
corridor surfaces. Report accuracy was not independently measured here.
[Corridor source](https://github.com/xuantinhnbs-rgb/civil3d-mcp/blob/ca2f6a336e80e2745a217c77eed8f768378e751b/civil3d_mcp/corridors.py),
[reports and limits](https://github.com/xuantinhnbs-rgb/civil3d-mcp/blob/ca2f6a336e80e2745a217c77eed8f768378e751b/README.md).

**Drainage.** Autodesk documents catchment/flow-path nodes in 2026 and over 75
drainage nodes in 2026.2, including ponds, channels and storage curves. Version
gating is required. Sacred-G's inspected terrain tracing uses samples and labels
watershed area approximate; Rational runoff uses supplied engineering inputs.
These are different evidence scopes from exact native catchment modeling.
[Autodesk](https://help.autodesk.com/cloudhelp/2026/ENG/Civil3D-WhatsNew/files/GUID-FC66011C-0DED-4332-B345-AD299D7D0245.htm),
[Hydrology source](https://github.com/Sacred-G/Civil3D-mcp/blob/9e437a3bab6df4ba13fabc2e909e138e8ac15896/Civil3D-MCP-Plugin/HydrologyCommands.cs).

**Production workflows.** CTC documents target mapping, corridor split/merge,
dynamic plan/profile sheets, region-based earthwork with stripping/subgrades,
and spreadsheet pipe updates. These establish useful workflow requirements;
they do not prove the same public APIs are available to Horizun.
[CTC product](https://ctcsoftware.com/product/cim-project-suite/),
[2026 guide](https://ctcsoftware.com/help/2026/CTC%20CIM%20Project%20Suite%20User%20Guide.pdf).
Dani's sheet handler tries alternative member names through reflection and
searches drawing objects; the inspection did not establish a working native
Sheet Set Manager integration. Keep this conditional despite its README wording.
[Sheet source](https://github.com/DaniGhosy/civil3d-mcp-civil3d-mcp/blob/10b19005b49a053ee4c3c93c768eb46539d9016f/plugin/Civil3dMcpPlugin/SheetProductionCommands.cs).

**Reliability and optimization.** Sacred-G has job/status machinery and an
in-memory idempotency store. The latter has a short TTL, retains no durable
receipt across process restarts, and removes failed executions. It is an execution
pattern to study, not proof of exactly-once recovery.
[Idempotency source](https://github.com/Sacred-G/Civil3D-mcp/blob/9e437a3bab6df4ba13fabc2e909e138e8ac15896/src/tools/idempotencyStore.ts).
Autodesk's grading solver evaluates slopes/drainage constraints and weighted
cut/fill, earthwork and smoothing objectives. Horizun's geometric grading does
not currently provide that optimization layer.
[Optimization options](https://help.autodesk.com/cloudhelp/ENG/C3DGO-Help/files/dialog_box_reference/C3DGO_Help_dialog_box_reference_optimization_options_dialog_html.html).

## Recommended backlog

Priorities are engineering judgments based on the local gaps, workflow utility
and dependencies. They are not measured competitor scores. Each feature needs
an offline API probe, validation tests, year-specific compilation and live
fixture evidence before being declared supported. New tools/actions must keep
the `horizun_c3d_*` namespace; names below are feature proposals, not current tools.

| Order | Priority and proposed block | Completion criterion |
|---:|---|---|
| 0 | P1 — Prove the prepared runtime | Deploy matching server/plugin through the existing owner-assisted process; run new v0.8.1 positive/refusal/failure cases on a generated fixture |
| 1 | P1 — Execution and recovery | Capture pre-edit values for UNDO; build drawing-transaction adapters for plans; expose operation state/receipts so a timeout can be reconciled without blindly replaying a write |
| 2 | P1 — Corridor targets and applied geometry | Enumerate/assign compatible targets; reread mappings and resulting geometry after rebuild; extract coded points/shapes by baseline/region/station; edit frequencies without dropping settings |
| 3 | P1 — Quantities and earthwork | Start with surface/native volume and network-length aggregations; then material/sample-line QTO and shape integration, keeping approximation methods distinct; include units, boundaries, factors, station intervals, source handles and reconciliation totals |
| 4 | P1 — Complete pressure construction | Inspect catalogs independently of networks; select explicit families/sizes with known units; place parts, connect ports and verify geometry/connectivity; reject unavailable catalog sizes |
| 5 | P2 — Hydrology/hydraulics | Separate terrain analysis, runoff calculation and native drainage objects; return methods/inputs/limits; verify Rational/Manning calculations analytically and gate native objects by host year |
| 6 | P2 — Road geometry and design checks | Implement supported spirals, station equations and superelevation; preserve lengths/directions and report parameterized criteria violations, without hard-coded client standards |
| 7 | P2 — Production and delivery | Generate/update plan/profile sheet series using supported layouts/viewports first; add numbering/matchlines and a reference manifest; investigate native sheet-set/view-frame APIs separately |
| 8 | P2 — As-built and engineering reports | Surface/check-point and profile comparisons with mean/min/max/RMSE, declared tolerance, exclusions/null reasons and drawing/source identity; stable CSV/JSON schemas for Excel/Power BI |
| 9 | P2 — References and grading dependencies | Synchronize/repair/promote supported references with verification; retain explicit feature-line dependency graphs; study optimization integration without promising an unverified API |
| 10 | P3 — Remaining specialist domains | Parcel subdivision/editing, survey adjustments/description keys, coordinate transforms/GIS, property sets/XDATA, contour/TIN editing and versioned API help; scope each against verified APIs and actual project needs |

The recommended first engineering package is **corridor targets + applied
geometry + traceable quantities**, after execution safeguards and prepared-runtime
validation. It connects design, checking, budget data and downstream reporting.
Pressure catalogs/parts follow as a separate bounded package. This refines the
earlier generic material-QTO priority in [CAPABILITY_REVIEW.md](CAPABILITY_REVIEW.md).

## Reproducible live benchmark protocol — not executed

Use generated fixtures, explicit catalogs/templates and identical host versions.
Record tool/server/add-in versions, contract hash, drawing revision, units and
input/output hashes. Reset from the same fixture before each product/task. If a
product cannot express a task, record `unsupported`, not a fabricated numerical
result. A 2026-only feature is evaluated on 2026 separately from 2025.

| Task | Reference result / acceptance |
|---|---|
| Surface volumes | Coincident 100 m × 100 m domains with a 2 m offset: magnitude 20,000 m³; declared cut/fill sign convention |
| Unit conversion | Equivalent metric/international-foot/US-survey-foot fixtures yield equal physical lengths/areas/volumes after explicit conversion |
| Material quantities | A constant 10 m² coded section over 100 m yields 1,000 m³; changing area tests the declared integration method and sampling error |
| Corridor target change | Reread the requested mappings and independently check computed geometry; preserve unaffected regions/frequencies |
| Pressure construction | Known catalog straight pipe plus fitting: verify family/size units, endpoints and connected ports; unavailable size must refuse |
| Gravity pipe edit | A 10 m horizontal run at 1% slope has a 0.1 m invert drop; preserve the requested direction and references |
| Runoff calculation | Rational inputs C=0.5, i=100 mm/h, A=1 ha produce approximately 0.138889 m³/s; no terrain-derived assumptions hidden in the input |
| As-built report | Check points with a constant +0.05 m deviation yield mean/RMSE +0.05/0.05 m and standard deviation 0; include an out-of-domain exclusion |
| Sheet series | Five sheets from a declared template/scale/station range: correct numbering, coverage, attributes and valid PDF pages; rerun/update must not silently duplicate sheets |
| Atomic drawing plan | Inject a failure in a dependent step: no partial drawing commit for a plan advertised as atomic; external-file effects reported separately |
| Timeout/retry | Inspect the same operation's state before retry; count actual objects/commits and record unresolved status explicitly |
| Modified-object UNDO | Compare all captured pre-edit values in a fresh transaction; command completion alone does not pass |
| Reference lifecycle | Change the generated source, detect staleness, update a supported reference and reread the new data; missing source must refuse |
| Delivery | Reopen outputs; verify declared object/design coverage, units and missing references; DWG structural counts alone do not prove every design value |

Predeclare tolerances for each method/domain. For every task report correctness,
refusal behavior, manual interventions, calls, and end-to-end duration. Collect
at least five independent reset runs where execution is available, report
median/range and failures, and describe the small sample. Do not label an error
response as successful execution or infer throughput from tool counts.

## What follows

1. Review this prioritized backlog alongside the current action catalog.
2. Complete prepared v0.8.1 deployment/live regression using the handoff process.
3. Probe corridor target/applied-geometry/QTO APIs and design the first bounded
   implementation package with the acceptance fixtures above.

This review changes documentation and priorities only. It adds no tool, live
evidence or supported host version.
