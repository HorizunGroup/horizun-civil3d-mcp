# Revit mesh receiver

`scripts/import-revit-terrain-mesh.py` is an explicit Python-channel receiver for the exported Civil TIN package. It creates a Generic Models DirectShape mesh. It is separate from editable Toposolid import.

The script is not executed by export or preparation and does not enable Revit's Python channel. The receiver expects the Revit MCP's injected `doc`, `uiapp`, and `HORIZUN_ARGS_JSON`. The operator must authorize and enable that channel under its existing controls.

Script arguments:

Prepare the request with `prepare-revit-terrain.ps1 -ExactMesh -Package <absolute-zip> -Destination <new-absolute-folder> -TargetDocument <exact-model>`.
Preparation neither executes Python nor grants its channel. Supply a new idempotency key before authorized execution.

```json
{
  "package": "C:\\transfer\\terrain.zip",
  "expected_package_sha256": "SHA-256 of the reviewed ZIP",
  "target_document": "Exact project title or saved absolute path",
  "position_tolerance_mm": 0.01,
  "dry_run": true
}
```

A rollback rehearsal returns `plan_hash`. Apply requires `dry_run: false` and `confirmed_plan_hash` equal to that rehearsal hash. The hash binds package bytes, project identity/path, active project location identity and transform, triangle counts and verification tolerance. `position_tolerance_mm` is optional, finite and bounded from 0.000001 to 1 mm. If omitted, the strict default remains `1e-7` feet (0.00003048 mm). The helper explicitly requests 0.01 mm; the receiver never automatically enlarges tolerance. This supplemental binding is not a substitute for the Revit MCP's own channel permissions and confirmations.

## Safety and coordinate treatment

- ZIP and its three members are size-bounded. Duplicate/extra entries, incompatible axes/units, payload hash mismatch and invalid OBJ faces are refused.
- OBJ coordinates are metres along Civil drawing axes. The receiver interprets them as Revit shared coordinates and converts to feet. Both directions of the active project location total transform are checked: each candidate internal point is independently read through `GetProjectPosition`, which must reproduce the source shared XYZ. Only a direction passing those native checks is used. It never changes shared coordinates or projects between coordinate reference systems.
- The current document must be a writable project with no existing open transaction. Input coordinates must remain within Revit design limits after transformation.
- A package already placed by this receiver is refused before creation, using the stored DirectShape application identity and package digest. Repeating an apply cannot silently create a second copy.
- Creation commits within a transaction group. A new transaction rereads stored element geometry; failure rolls back the entire group. Dry run rolls back the group after verification. Only explicit apply assimilates it.
- Reread checks geometric triangle count and positions against the declared tolerance. Centroid bins and their neighbouring bins select candidate faces; final matching compares all vertex permutations against the unrounded tolerance. It never moves vertices to pass. A bounded diagnostic for small meshes reports actual residuals when Revit mesh precision exceeds the requested fidelity.
- The receiver checks geometric triangles, not original OBJ vertex identity for coincident vertices or triangle winding. Geometry repair, deleted faces or changed coordinates cannot silently count as success.
- Survey controls must independently establish the Civil-to-Revit shared-coordinate relationship. A correct numerical transform does not prove that relationship.

Autodesk only supports Mesh/Salvage for forced mesh construction; the script therefore rejects invalid-data flags and changed stored geometry before accepting a result. See the [official tessellation API documentation](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/3b67078d-f8fd-83f4-ee2e-b83e8ec23a23.htm).

## Evidence

Actual Revit 2025 API signatures were dumped offline from local RevitAPI `25.4.40.0` into `docs/api-probes/revit2025/terrain-directshape.txt`. Pure Python tests validate parsing and triangle comparison. Live Revit 2025.4 acceptance subsequently rehearsed with rollback, applied the mesh, independently reread its element and measured shared-coordinate controls at a 37 degree project rotation. Maximum native shared-coordinate control residual was 6.13e-12 mm; maximum stored mesh vertex residual was 0.002994 mm under the explicit 0.01 mm tolerance. Duplicate placement was refused and the generated project was saved. This evidence covers the generated four-vertex/two-triangle fixture, not arbitrary terrain or another Revit version.

An independent typed bounding-box comparison exposed an incorrect assumption in the initial receiver: the inverse project-location transform placed the mesh on the wrong side of the origin despite verifying the requested internal mesh coordinates. The receiver now validates transform direction through native shared-coordinate readback before opening any transaction. `scripts/probe-revit-terrain-coordinates.py` supplies a read-only comparison of both candidates. Checking geometry against the receiver's own converted points alone is insufficient placement evidence.

Initial live rollback rehearsal on Revit 2025.4 measured mesh single-precision quantization: a maximum vertex residual of approximately `9.04956e-6` feet (`0.00275831` mm). For example, an expected Z of `334.64566929133855` feet reread as `334.64566040039062` feet. The strict default correctly refused and rolled back that mesh. Explicitly requesting 0.01 mm permits evaluating that measured precision without claiming numerically exact coordinates; the source OBJ retains its original coordinates/connectivity.
