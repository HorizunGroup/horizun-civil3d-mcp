# Releasing Horizun Civil 3D MCP

**Una aprobación por versión: conecta SimplySign Desktop → Approve en `firma-certum` → listo.**

Every public release carries binaries signed with the **HORIZUN GROUP S.A.S. (Certum)** certificate.
Published GitHub releases are immutable: a correction is always a new patch version.
This repository has no automatic build workflow: the add-in needs the Autodesk DLLs, so the assets are
built on the owner's PC; CI only signs and publishes them.

## Cycle

1. **Version and PR.** Bump the version everywhere it is declared (`Directory.Build.props`,
   `.claude-plugin/plugin.json`, `.codex-plugin/plugin.json`, `packaging/claude-desktop/manifest.json`,
   `runtime-release.json`, README) and add a `CHANGELOG.md` entry. Open a PR, wait for CI, merge.
2. **Build locally from the merged commit** (clean `main`, Autodesk 2025 installed; 2024 and 2026 use the
   reference trees under `.local/autodesk-references`). Build into an empty folder, never into the repo:

   ```powershell
   $v = '0.9.3'; $out = "$env:TEMP\c3d-release-$v"; New-Item -ItemType Directory $out -Force
   $refs = "$PWD\.local\autodesk-references"
   # Canonical ZIP: 2024 (net48), 2025 (net8), 2026 (net8)
   pwsh -NoProfile -File scripts/install.ps1 -Years 2024,2025,2026 -Edition release `
     -AcadRoots @{2024="$refs\2024"; 2026="$refs\2026"} -PackageOut "$out\horizun-civil3d-mcp-$v.zip"
   # Standalone 2026 net10 ZIP
   pwsh -NoProfile -File scripts/install.ps1 -Years 2026 -Edition release `
     -AcadRoots @{2026="$refs\2026-net10"} -PackageOut "$out\horizun-civil3d-mcp-$v-2026-net10.zip"
   # Plugin ZIP, MCPB and their SHA256SUMS
   powershell -NoProfile -File scripts/build-plugin.ps1 -ReleasePackage "$out\horizun-civil3d-mcp-$v.zip" -OutDirectory $out
   powershell -NoProfile -File scripts/verify-public-package.ps1 -Package (Get-ChildItem $out\*.zip,$out\*.mcpb)
   ```

   For 0.9.5, confirm PowerShell 7 (`pwsh`) built both packages; Windows PowerShell 5.1 is refused for packaging.
   Then add the net10 line to `SHA256SUMS`. The release has exactly five assets:
   `horizun-civil3d-mcp-X.zip`, `-X-plugin.zip`, `-X-2026-net10.zip`, `-X.mcpb` and `SHA256SUMS`.
   The `.local` reference trees are not versioned; see `docs/COMPATIBILITY.md`.
3. **Create the DRAFT** release targeting the merge commit, with the assets and the notes
   (the notes must include the line *Binarios firmados con el certificado de HORIZUN GROUP S.A.S. (Certum)*):

   ```powershell
   gh release create vX.Y.Z --draft --target <sha-del-merge> --title "Horizun Civil 3D MCP X.Y.Z" --notes-file notes.md <5 assets>
   ```

4. **Before signing**, confirm the scheduled signing service task is `Running`:

   ```powershell
   Get-ScheduledTask -TaskName 'Horizun Servicio de Firma' | Select State
   ```

   **Start the signing job:**

   ```powershell
   gh workflow run firmar-release.yml -R HorizunGroup/horizun-civil3d-mcp -f tag=vX.Y.Z
   ```

5. **The CI leaves the draft, a notice goes out, and Pablo approves.** Pablo connects *SimplySign Desktop*,
   opens the run in the *Actions* tab and approves the `firma-certum` environment. The `firmar-y-publicar`
   job (self-hosted runner `horizun-sign`) then signs, verifies and publishes. If anything fails it does
   not publish.

## What the job does (`scripts/firmar-release.ps1`)

- Checks it can sign (canary file) before touching anything.
- Downloads the draft's assets and signs the `.exe`/`.dll`/`.msi` that are ours (`Horizun.Civil3D*.dll`,
  `horizun-civil3d-mcp.exe`), also inside the `.zip`/`.mcpb`, including the nested payload ZIP embedded in
  the plugin ZIP and the MCPB. Third-party DLLs are left untouched.
- Rewrites every hash that changed: `SHA256SUMS`, and the `sha256` in the `runtime-release.json` embedded in
  the plugin ZIP and the MCPB. Verifies each signature (valid, Certum issuer, Certum timestamp), uploads with
  `--clobber`, re-downloads and compares, and only then publishes the draft.

### Hashes pinned in the repository

The only hash that refers to a release asset and lives in the repository was `runtime-release.json`
(`sha256` of the canonical ZIP). The signed ZIP does not exist when the tag is cut, so the file versioned in
the repo carries `"sha256": null`: when the plugin is installed from a git checkout (no embedded payload),
the bootstrap reads the digest of the published asset from the GitHub API, which is the signed one.
The copy inside the plugin ZIP and MCPB is pinned and is rewritten by the script to the signed ZIP.
Do not put a SHA-256 back into the versioned file. Documents and CHANGELOG never quote asset hashes.

## The self-hosted runner

- Location: `C:\Users\pablo\.horizun\sign-runners\horizun-civil3d-mcp` (one runner per repository, label
  `horizun-sign`, running as user `pablo` so it sees `Cert:\CurrentUser\My`).
- Start all runners: `C:\Users\pablo\.horizun\sign-runners\iniciar-runners.ps1`, or run the scheduled task
  **Horizun Sign Runners**. Check it in *Settings > Actions > Runners* (must be *Idle*/*Online*).
- Do not run anything else on it; it only executes jobs that wait for the `firma-certum` approval
  (required reviewer: `pablo-horizun`).

## If the job fails because SimplySign is disconnected

The job fails at the first step with "No se puede firmar" and the draft is left untouched. Reconnect
*SimplySign Desktop* (enter the code on the PC, never in a script) and use **Re-run failed jobs** on the run,
then approve again. If the draft has to be rebuilt, delete the draft and start from step 3; a published
release can never be replaced.

El runner no llama a signtool: scripts/firmar-release.ps1 envía cada binario al servicio local mediante cola/pendiente/*.json y espera cola/hecho/*.json. La firma, huella y sello se verifican antes de publicar.

