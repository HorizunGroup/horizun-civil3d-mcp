# Cross-product review — 2026-10-05

The review compared Civil 3D v0.8.0 (`131b142`) with the locally available
Horizun products. Three agents reviewed transport/security, host writes and
installation/distribution. Reference repositories were not changed.

## Reference versions and scope

| Product | Reference | Relevant source |
|---|---|---|
| Revit | `e204f310`, development checkout | `src/Horizun.Revit/Core/Settings.cs`, `BoundedLineReader.cs`, `PipeServer.cs`, `scripts/build-mcpb.ps1`, `scripts/complete-install.ps1` |
| Navisworks | NavisCoord 1.1.1, checkout `e19e49d` | `scripts/Start-Mcp.ps1`, `.codex-plugin/plugin.json`, `server/naviscoord/bridge.py`, `addin/NavisCoord.Addin/AppearanceLedger.cs` |
| Power BI | Release 2.1.2, `9c64386` | `.codex-plugin/plugin.json`, `scripts/plugin_launcher.py`, `scripts/plugin_bootstrap.py`, `scripts/instalar.ps1`, `packaging/claude-desktop/manifest.json` |
| Microsoft Project | `af99478`, development checkout, plugin 1.6.0 | `.codex-plugin/plugin.json`, `packaging/claude-desktop/manifest.json`, `Writes/WriteEngine.cs`, `InteropTools.cs` |

The older NavisCoord 0.3.1 checkout was not used as the current distribution
baseline. Power BI's separate working checkout contains unrelated local edits;
the clean release and the installed 2.1.2 manifest were used instead. NavisCoord's
installed plugin manifest also matches 1.1.1. Revit and Project checkout evidence
does not establish the version of their live installed runtimes.

## Operational parity

| Concern | Reference behavior | Civil 3D before | Civil 3D changes |
|---|---|---|---|
| Host integration | Revit and Navisworks use an add-in; PBI connects to Desktop/local projects; Project uses a file engine | Own Autodesk add-in, authenticated pipe, shared Core contract | Retained the Civil-specific host architecture |
| User runtime | Prebuilt/isolate runtime; development SDK is separate from ordinary installation | Prebuilt ZIP already existed | Plugin bootstrap installs the matching prebuilt ZIP; no Python or SDK required |
| Codex/Claude plugins | Metadata points to a launcher; PBI/Navis include skills | No plugin manifests or skills | Added both manifests, marketplace metadata, setup and workflow skills |
| Claude Desktop bundle | Revit/PBI/Project distribute MCPB | Only install ZIP | Added MCPB and portable plugin ZIP builder with embedded release and SHA-256 |
| Missing runtime | PBI bootstrap exposes installation/status; Navis reports launch failures | Missing executable prevented server startup | Minimal diagnostic MCP exposes `horizun_c3d_install_status` and `horizun_c3d_install_runtime` |
| Safe client registration | Revit avoids overwriting configuration of a running client | Claude registration happened after the installation rollback boundary | Claude preflight, running-client guard, atomic registration, concurrent-edit detection, rollback and durable restart state; incomplete rollback reports errors honestly |
| Input/output bounds | Revit bounds stdin and measures UTF-8 bytes | Unbounded stdin/task admission and incorrect pipe boundaries | Bounded stdin, 32 in-flight calls, exact pipe limits and UTF-8 response sizing |
| Drawing identity | Navis checks expected document fingerprint; Project resolves a document handle | Same filename in two folders could resolve to the active drawing | Ambiguous basename/stem refused; full path accepted |
| Verified writes | Project applies against a clone and validates operation results; PBI backs up/re-reads files | Typed dry run/token/new-transaction re-read already present | Retained the contract; corrected external-effect and UNDO claims |
| File delivery | PBI separates file transactions from host state and preserves backups | PDF destination deleted before plotting completed | Stage, validate and atomically promote PDF/CSV/LandXML; replacing a PDF retains a named backup |
| Permission scope | Revit settings and Python consent are global per Windows user | Same global settings, plus session ribbon switches | Retained parity; dialogs and instructions now disclose cross-instance scope |
| Automated gates | PBI tests installation/package behavior as well as engine code | CI ran only .NET tests | Added isolated PowerShell registration/bootstrap and tunnel suites |

## Corrected findings and practical limits

The initial review classified shared permissions between Civil 3D instances as
a security defect. Revit intentionally has the same user-wide scope. That
classification was withdrawn for this parity review. Civil 3D now makes the
scope explicit when the owner enables either ribbon switch. A new Civil 3D
start still resets switch-managed elevated permissions; manually configured
profiles retain their existing semantics.

Roslyn C# runs inside the host with the owner's privileges. Aborting the supplied
transaction is not a sandbox: scripts can write files or commit separate
transactions. Both modes now state this, remain `unsafe_code`, and invalidate
previous confirmation/undo state after execution. The MCP annotation marks
arbitrary code as open-world.

`undo_last` excludes scripts, file exports and shortcut environment changes.
It requires evidence of a DWG object change. Created objects can be checked
after UNDO; modified objects have no captured prior-state snapshot. In that
case the response explicitly reports that UNDO already ran, returns incomplete
verification, and prohibits automatic retry. Full value-by-value verification
of modified objects remains a separate implementation task.

The plugin's `ready` status verifies installed files and their manifest hashes;
live host readiness still requires `horizun_c3d_health` after Civil 3D starts.
The repository bootstrap resolves only the exact release version and checks
its GitHub asset digest. Packaged plugins carry their release ZIP and pinned
SHA-256, so they need no network download. Unpublished/private releases may
not be available through the repository bootstrap; no release publication was
performed during this review.

## Verification and remaining gates

The delivery is a prepared v0.8.1 change, not a new live installation. Local
verification includes Core/Server tests, plugin compilation for 2025, isolated
PowerShell lifecycle tests, release-ZIP building, plugin/MCPB payload validation
and skill validation. Exact results are recorded in `handoff/SESSION_LOG.md`.

Before marking the host corrections live-verified:

1. Install server and add-in together from the prepared package after saving
   drawings and confirming all Civil 3D windows are closed.
2. In a fixture, test two drawings with identical filenames, C# query reporting,
   external exports, failed PDF overwrite, and `undo_last` for creations and
   modifications.
3. Verify actual plugin loading/restart behavior in the target MCP clients.
4. Publish the approved release and its SHA-256 assets. This requires the owner's
   instruction to commit/push/publish; it has not happened.

Civil 3D 2026/2027 host execution, a modal-dialog busy test, complete modified
object UNDO snapshots and a public registry publication are not claimed here.
Domain-specific features have a separate review in `CAPABILITY_REVIEW.md`.
The follow-up adds the capabilities catalog, drawing audit, DWG copy and basic
pressure-network inspection/empty creation/rename. Atomic `execute_plan`, pressure
part placement and spirals remain pending, with acceptance criteria in that review.
