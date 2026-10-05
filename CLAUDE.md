# Working rules for this repository (humans and AI agents)

> **START OF EVERY SESSION:** read `docs/handoff/00_START_HERE.md`, then `docs/handoff/STATUS.md` ("QUÉ SIGUE"),
> and follow `docs/handoff/WORKFLOW.md` (mandatory: log the block in `SESSION_LOG.md` BEFORE coding, update
> `STATUS.md` + `SESSION_LOG.md` + `CHANGELOG.md` + `docs/CIVIL3D.md` AFTER, and always report to the owner what
> comes next). The handoff docs are in Spanish on purpose: the owner reads them.

The owner writes in Spanish: explain work to them in Spanish, briefly. Code, identifiers, commits and the
technical docs in this repo are in English.

## What this product is
- Horizun Civil 3D MCP is a **separate product** from Horizun Revit MCP: its own server, plug-in, installer
  and data root (`%USERPROFILE%\.horizun\civil3d\`). It reuses the Revit product's *contract and patterns*,
  but no code is shared at runtime.
- Every tool is named `horizun_c3d_*`. Never reuse tool or command names from other MCPs (Sacred-G
  Civil3D-mcp `civil3d_*`, the Revit MCP's `horizun_*`), even when a tool does the same job.
- The goal is the best Civil 3D MCP there is, and that means **writing** to the drawing reliably, not just
  reading it.

## Non-negotiable rules
1. **Never report unverified work.** After the commit, re-read every write in a NEW transaction
   (`CommandContext.Verify`, `VerificationSet`). An empty verification is never a pass.
2. **Dry run by default** for every write. Use `CommandContext.Rehearse` and `RequireConfirmation`. Build the
   plan from the RESOLVED objects and values, so that a drawing that changed afterwards is caught.
3. **Resolve before the transaction.** Missing or ambiguous names are refused with candidates
   (`Catalog.ByName`).
4. **Refuse honestly, by name.** Never approximate silently. Unreadable values are `null` with a reason,
   never `0`.
5. **No invented APIs.** Before using any Civil 3D member, confirm it in `docs/api-probes/<year>/`, or run
   `tools/Horizun.Civil3D.ApiProbe` (offline) or `horizun_c3d_probe` (live), and commit the dump.
   Compiling against the installed DLLs is the second gate.
6. **Never drive the user's screen** (no mouse, keyboard or computer use). Never send ESC and never cancel the
   user's command; reply `busy`. Never kill acad.exe.
7. **Organisation-neutral.** No Horizun or client standards, names, colours or legends are compiled into the
   bridge. They enter as parameters.
8. **Nothing private in the repo.** No client or project names, paths, handles or drawings. Use the generated
   fixture only.
9. Before asking the user to close Civil 3D (to deploy), always tell them to **save first**. Afterwards,
   verify the deployed DLL by its hash or date and by a new command.

10. **JSON inside acad.exe: reflection-based serialization is DISABLED** (found live, v0.3.0). Never call
    `JsonArray.Add(<string/number/bool>)` or `JsonValue.Create<T>` generically. Write
    `array.Add(JsonValue.Create(x))`, `Hz.Strings(...)` or indexer assignments, and serialize only with
    `Hz.Compact` / `Hz.Indented`. The test project runs with the same switch
    (`runtimeconfig.template.json`), so a violation reachable from Core fails the tests. Plug-in code
    cannot be tested that way, so review every `.Add(` on a `JsonArray`.
11. **Edit files with Write/Edit, not python or sed inside a bash heredoc.** Backslash sequences (`\n`,
    `\f`, `\v`) get corrupted into control characters. This happened three times.

## Layout
- `src/Horizun.Civil3D.Core` holds the host-agnostic rules: contract and hash, settings, discovery, queue,
  confirmation and verification. It has NO Autodesk references and is fully unit-tested.
- `src/Horizun.Civil3D.Server` is the MCP stdio server, `horizun-civil3d-mcp.exe`.
- `src/Horizun.Civil3D.Plugin` is the Civil 3D add-in, `Horizun.Civil3D.dll`, with one build per year
  (`-p:Civil3DYear=`).
- `tests/Horizun.Civil3D.Core.Tests` runs without Civil 3D, including an end-to-end pipe test against a fake
  plug-in.
- `tools/Horizun.Civil3D.ApiProbe` dumps the offline API signatures of the installed Civil 3D.
- `scripts/install.ps1` builds, installs and verifies, and rolls back on failure.

## Adding a tool or action
1. Probe the API and commit the dump under `docs/api-probes/<year>/`.
2. Declare the tool in `Contract.cs`, with its schema, its effect and any per-action effects. The contract hash
   changes, so the server and plug-in must be released together.
3. Implement an `ICommand` in the plug-in and register it in `App.CreateDispatcher` (startup refuses any
   mismatch).
4. For writes: dry run, then token, then `Write()`, then `Verify()`, then `VerificationSet`, and report `undo`.
5. Add tests for everything that does not need Civil 3D. Write the live verification step into
   `docs/CIVIL3D.md`, and mark the capability "supported" only after live evidence.
6. Update `CHANGELOG.md`.

## Git
- Do not commit or push unless the owner asks. Use one branch per phase (`phase-N/<topic>`).
