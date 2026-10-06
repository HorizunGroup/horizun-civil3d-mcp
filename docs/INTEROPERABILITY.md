# Civil 3D → Revit terrain interoperability

Prepared v0.9.0, 2026-10-05. Contract `171049b89c39afc5120da6ff`.
The owner has prioritized terrain export and placement in Revit. This block
implements the Civil-side package and receiver request preparation. It does
not yet prove a live transfer between the two applications.

## Executable route implemented

```text
Named Civil TIN → exchange export_revit → ZIP
                                       ├─ terrain.xml (visible TIN)
                                       ├─ manifest.json (units/source/geometry)
                                       └─ terrain.obj (same visible triangles, metric coordinates)
                 ↓ validate and extract to a new directory
prepare-revit-terrain.ps1 → revit-request.json (dry run)
                 ↓ existing, separate Horizun Revit MCP
horizun_create_elements(kind=toposolid, landxml_path=...)
                 ↓ inspect rehearsal and shared-coordinate controls
confirmed Revit apply → reread element AND compare placement/top surface
```

Civil and Revit retain separate servers, contracts, installations and permissions.
Exporting a package never starts a Revit transaction. A client orchestrates the
two tool calls; the Civil add-in does not embed the Revit API or silently select
a destination model, type or level.

### 1. Export the named surface

Call `horizun_c3d_exchange` with the exact active drawing, a named TIN and a new
absolute ZIP destination:

```json
{
  "action": "export_revit",
  "target_document": "C:\\GeneratedFixtures\\terrain.dwg",
  "surface": "EG",
  "output": "C:\\GeneratedFixtures\\terrain-to-revit.zip",
  "dry_run": true
}
```

The file effect requires **FullWrite**. Inspect the returned plan, then send the
same arguments with `dry_run=false` and the returned Civil confirmation token.
Existing destinations are refused. This writes the current TIN without saving
or renaming its source drawing. Read-only source drawings follow the bridge's
existing write refusal policy even though the effect is a file.

The exporter reads only visible triangles and their used vertices. It retains
exact XYZ coordinates and connectivity in LandXML, with north/east/elevation
order, round-trip double precision and explicit metric/international-foot/US
survey-foot units from Civil settings. It rejects conflicting XY elevations,
invalid/degenerate/duplicate faces, non-manifold edges and unused points.
No automatic thinning or coordinate reprojection occurs.

The current receiver guard is **20,000 visible vertices**; this is an application
guard, not an Autodesk capacity claim. Larger surfaces require an explicitly
prepared smaller surface or a future tiled/exact-mesh workflow. Existing
`export_landxml` remains available for larger archival exports.

The manifest includes source drawing/handle/revision/contract, CRS metadata
(null when unavailable), bounds, plan area, minimum/maximum/mean vertex
elevation, three non-collinear source controls, footprint review flags and the
LandXML SHA-256. Source metadata does not establish CRS equivalence with Revit.
The XML date/time is a deterministic placeholder, not the time of export;
stable payloads allow the dry-run geometry hash to be compared on apply.

Staged ZIP validation reopens the archive and compares **every payload byte**
against the prepared terrain before promotion and again after writing. The
confirmation plan binds both payload hashes. This verifies export fidelity;
`revit_import_verified` remains false until the destination is actually checked.

### 2. Prepare the Revit request

Determine the target model, a real Toposolid type id and a real level id through
the Revit MCP. Do not guess those ids or reuse ids from another model.

```powershell
pwsh scripts/prepare-revit-terrain.ps1 `
  -Package C:\GeneratedFixtures\terrain-to-revit.zip `
  -Destination C:\GeneratedFixtures\terrain-revit-input `
  -TargetDocument C:\GeneratedFixtures\site.rvt `
  -TypeId 12345 -LevelId 67890
```

The helper also runs on PowerShell 5.1. It is included in plugin/MCPB scripts
and the runtime's `server/client-tools` directory. It creates a **new** directory
containing `terrain.xml`, `manifest.json` and `revit-request.json`. It validates
entry names/counts, bounded sizes, hash, XML units/surface/counts; refuses ZIP
path/duplicate/extra entries and DTD expansion; and rechecks extracted bytes.
It generates a `revit_rollback` rehearsal request, without calling either host.

The current point-only native importer can fill concavities/holes/islands
differently. The helper refuses incomplete convex coverage by default.
`-AllowRetriangulation` permits preparing a rehearsal after explicit review; it
does not claim preservation of boundaries or authorize applying the result.
The area-versus-hull flag is a footprint screen, not a complete topology proof.

### 3. Place and verify in Revit

The inspected local Revit connector already implements
`horizun_create_elements` with `kind=toposolid`, `type_id`, `level_id` and
`landxml_path` (Revit 2024+). The file's declared units govern its points; the
request's `units=m` does not rescale LandXML a second time. This import mode
treats the source coordinates as **Revit shared coordinates** and converts them
through the active project position. It binds the source hash and project
position to its own rehearsal plan.

Before applying, check the model and project location, survey elevation and
rotation against independently known project controls. No export or helper
sets, resets, acquires or publishes shared coordinates. An assigned Civil CRS
does not automatically establish a Revit project position or vertical datum.

The inspected Revit code explicitly records two unmeasured host behaviors:
rotation sign for shared-to-internal placement and absolute versus level-relative
Toposolid point Z. Its post-commit top-surface samples prove elevations at
converted positions, not the correctness of the coordinate conversion itself.
Live acceptance must therefore include nonzero rotation/elevation and a nonzero
level, plus an independent reverse coordinate check.

Use the prepared request for a dry run, inspect its rollback rehearsal and
placement, and apply the **identical approved request** using the Revit token and
a new idempotency key. Read the result and its spatial checks before reporting
success. A timeout or failed verification requires inspection before replay.
If the two connectors cannot access the same path, transfer and validate the
package on the receiving machine before preparing that machine's request.

## Fidelity and format roadmap

| Format/route | Current status | Intended use / limit |
|---|---|---|
| LandXML TIN + manifest ZIP | Implemented T/B in this block | Retains source visible connectivity in the file; the existing native Revit importer uses points and retriangulates |
| Editable Revit Toposolid | Receiver exists; full live transfer pending | Native terrain tools; source edges/breaklines/holes are not guaranteed by point import |
| Whole-drawing DWG | Existing prepared T/B export | Civil design archive; external references remain external; Civil objects are not converted into native Revit terrain |
| Clean terrain mesh DWG / exact Revit mesh | Planned P1 feasibility block | Preserve visible triangles/holes as a reference; probe clean 3DFACE/mesh and tessellated DirectShape APIs; separate reference geometry from an editable Toposolid |
| IFC / corridor solids | Planned | Interoperable geometry, classifications/materials and coordinate provenance; verify exporter/version/mapping and resulting geometry before declaring coverage |
| CSV/JSON engineering exchange | COGO CSV and generic JSON exist; extended schemas planned | Quantities, applied corridor geometry, network data and as-built comparisons with explicit units/nulls/source ids |
| Editable spreadsheet exchange | Planned | Measured row plans, unchanged-source check and verified write-back; no automatic edits from arbitrary spreadsheet formulas |
| Autodesk published/linked topography | Documented alternative; not automated here | Separate Desktop Connector/cloud permissions and version compatibility |

Autodesk documents linked topography workflows between Civil 3D and Revit,
including Desktop Connector requirements. That route is distinct from the local
LandXML/Toposolid implementation above.
[Sharing topography](https://help.autodesk.com/cloudhelp/2023/ENU/Revit-Model/files/GUID-3419F2BF-98F5-4988-BA51-9C23FE23D37D.htm),
[Link topography](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-Model/files/GUID-8921768C-0D0E-4554-90BC-02524896250C.htm).
Autodesk's Toposolid API offers boundary/point creation; support for that richer
boundary mode in the local connector must be implemented and verified separately.
[Toposolid API](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API/files/Revit_API_Developers_Guide/Discipline_Specific_Functionality/Revit_API_Revit_API_Developers_Guide_Discipline_Specific_Functionality_Site_html.html).

## Verification and next work

Local gates: 490 Core/Server tests; plugin 2025 build with zero errors/warnings;
21 PowerShell 5.1 helper checks and 29 PowerShell 7 checks including the actual
compiled Core package → helper → local Revit LandXML reader. These checks prove
file/schema/parser interoperability, not creation in a running Revit model.
No Revit repository files were changed. Revit health was blocked by a persistent
modal dialog, so no live model call or coordinate check completed.

Live fixture acceptance:

1. Export a generated planar TIN with distinct east/north values; reread every
   vertex/face and prove the source drawing name/unsaved state is unchanged.
2. Run equivalent metric, international-foot and US-survey-foot fixtures with
   INSUNITS deliberately different from Civil units; compare physical placement.
3. Rehearse/create the native Toposolid with known ids, nonzero level and known
   shared origin/rotation/elevation; independently check at least three plan
   controls and the elevations.
4. Compare the Revit top surface against the Civil TIN at vertices AND interior
   locations, reporting min/max/mean deviation, RMSE, exclusions and the
   requested tolerance. Vertex-only agreement is insufficient for retriangulation.
5. Exercise concavity/hole/island, stale geometry/token/file, ambiguous surface,
   existing ZIP/directory and oversized terrain refusals. Test Revit failure
   without reporting the source export as a completed model transfer.

## Exact mesh receiver and engineering additions

`prepare-revit-terrain.ps1 -ExactMesh` prepares a request for the separately
authorized Revit Python channel, without requiring a Toposolid type/level.
`import-revit-terrain-mesh.py` rehearses a DirectShape mesh, transforms shared
coordinates and rereads stored triangles after commit, then rolls back by default.
See [REVIT_MESH_RECEIVER.md](REVIT_MESH_RECEIVER.md). This receiver has real Revit
2025 API signature evidence and pure guard tests; native execution remains pending.
Neither script grants Python access or changes shared coordinates. DirectShape
geometry and editable Toposolid are distinct outputs.

The selected engineering blocks now have implementations and offline tests:
[surface comparisons](SURFACE_COMPARISON.md), [corridor geometry/targets and region
quantities](CORRIDOR_ENGINEERING.md), [editable COGO CSV](EDITABLE_CSV.md), and
alignment-linked layout viewports with explicit refresh. They require live
acceptance before claiming support. Native QTO and automatic native view-frame
production are not implied by the estimated quantities or viewport links.

## Synthetic fixture files

`dotnet run --project tools/Horizun.Civil3D.EngineeringFixture -c Release -- <new-absolute-directory>`
creates a 100 m² plane TIN package, OBJ/XML, parser-example CSV, comparison result
with expected 0.02 m RMSE, and dry-run Civil/Revit requests. It refuses existing
directories. It creates interchange files, not native DWG/RVT without hosts.
