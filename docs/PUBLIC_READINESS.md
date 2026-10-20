# Public release readiness — 2026-10-06

## Scope

v0.9.2 retains 28 tools and 163 declared actions. `undo_last` remains in the schema
for compatibility but is disabled: it returns `unsupported`, `committed=false` and
`undo.available=false` before issuing any native command. Drawing writes retain
rehearsal, single-use confirmation and independent post-commit verification.

## UNDO finding

A bounded pre-write native-field snapshot experiment compiled on all target years.
The new Civil2025 fixture exposed a native UNDO attribution failure: a newly drawn
line remained after the command. No retry was issued for that committed outcome.
Surface-container coverage also proved incomplete. The experimental snapshot code
was removed from the release, and automatic UNDO was disabled. This closes the
unsafe execution path; it does **not** claim automatic restoration has been fixed.
Manual Civil 3D UNDO remains available to the operator.

## Evidence carried forward

The [2026-10-05 acceptance](ACCEPTANCE_20261005.md) contains native corridor targets,
geometry, guarded split/merge, estimated quantities, editable CSV, linked viewports,
surface deviation/tolerance/RMSE, full DWG copy and Civil-to-Revit placement tests.
Toposolid and DirectShape were saved in new Revit2025 models with independent
coordinate controls. Quantities remain estimates; viewport refresh is explicit;
Toposolid can retriangulate and does not preserve TIN volume/thickness.

Civil2024 and2026 compile against authenticated Autodesk references but have no
native fixture acceptance here. They must not be described as production-certified.
The [compatibility table](COMPATIBILITY.md) distinguishes build/test/native evidence.

## Privacy and distribution

Gitleaks8.30.1 was downloaded from its official release and verified against its
published SHA-256. Reachable Git history and the publishable source tree were scanned.
The sole historical finding was an explanatory docstring, not a credential;
`.gitleaksignore` records its exact commit/file/line fingerprint. Current comment
false positives were reworded. Raw reports, configurations and test drawings stay
in ignored `.local/`. CI repeats both scans with a pinned binary SHA-256.

Apache-2.0 LICENSE and NOTICE are present. Autodesk binaries, DWG/RVT acceptance
files, credentials and local configurations are excluded from the source and release
payloads. Releases contain our bridge and redistributable runtime dependencies.

Compiler paths are mapped in binaries, and portable PDBs are excluded from public
packages because their compiler-input metadata retained private paths. The existing
v0.8.0 ZIP failed the same privacy gate; it is backed up locally with its SHA-256
before its GitHub asset is withdrawn. The historical tag/source remain available.

Final v0.9.2 native engineering rerun passes78/78; all three automatic-UNDO request
variants refuse without changing independent entity reads. The generated DWG was
saved and temporary permissions restored. 552 Core tests,391 per runtime,14 receiver
tests and8 acceptance-client tests pass; bootstrap/runtime gates also pass.

## Publication gate

Before changing visibility: tests/builds and package checks must pass, the current
source and history scans must be clean, documentation must describe current limits,
and the reviewed source/CI must be synchronized with GitHub. Public source availability
does not certify every declared action or uninstalled Autodesk year.

Publication completed on2026-10-06 after owner authorization and execution approval
became available. [PR#1](https://github.com/HorizunGroup/horizun-civil3d-mcp/pull/1)
passed [CI](https://github.com/HorizunGroup/horizun-civil3d-mcp/actions/runs/37476925655)
and merged at `2c02c2d6122c9904df15e03aa8a6ca7c819998df`. A Windows short-path
test assertion was normalized after the first CI run failed; production payloads
were unchanged. The [v0.9.2 release](https://github.com/HorizunGroup/horizun-civil3d-mcp/releases/tag/v0.9.2)
contains all four sealed packages plus SHA256SUMS; all five GitHub digests match
local files. The old0.8.0 asset was removed after its backup hash was verified;
historical source/tag remain. Repository visibility is PUBLIC and private
vulnerability reporting is enabled. Earlier approval refusals remain in the log.

Autodesk add-in binaries were built and tested locally from the preparation0.9.2
working tree. GitHub CI validates host-independent code and auxiliaries; it does
not build the add-in against Autodesk DLLs or establish native2024/2026 evidence.
