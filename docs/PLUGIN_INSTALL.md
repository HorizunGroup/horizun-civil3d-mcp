# Plugin installation and packaging

Horizun Civil 3D MCP has the same plugin entry structure as the other Horizun
desktop products: Codex/Claude manifests, domain skills and a launcher that
reports missing-runtime problems through MCP.

## People using the plugin

Install the GitHub plugin from the repository/marketplace in the target client,
or open the prepared `horizun-civil3d-mcp-<version>.mcpb` in Claude Desktop.
The repository currently requires the GitHub access granted by its owner.
Plugin artifacts are local until the release is published.

If only installation tools appear, call `horizun_c3d_install_status`, then
`horizun_c3d_install_runtime` to see the plan. Save drawings and close every
Civil 3D/AutoCAD window before applying with `confirm=true`. Restart the MCP
client when runtime status becomes `ready`, open Civil 3D and check
`horizun_c3d_health`.

Windows PowerShell 5.1 is the bootstrap host; neither Python nor the .NET SDK is
required for the prebuilt package. The package must contain a build matching the
installed Civil 3D year **and update runtime**. Build targets now include 2024
and both 2026 runtime families; the canonical package includes 2024/2025/2026 net8,
with a separate standalone 2026 net10 package. Only
2025 host behavior has live evidence. See [COMPATIBILITY.md](COMPATIBILITY.md)
for pending 2024/2026 live gates and `host-builds.json` update matching.

The standalone ZIP remains available. Extract it and run `install.ps1`.
For this unsigned AppData deployment, explicitly pass `-RegisterTrustedPath`
to register only the installed plugin year directories in existing Civil
profiles. The helper backs up `TRUSTEDPATHS`, preserves its other entries and
does not change `SECURELOAD` or enable any scripting channel. Run
`scripts/register-plugin-trust.ps1 -Years 2025` for a read-only trust report;
add `-Register` only when installing the reviewed plugin. Restart Civil after
changing the profile; an already waiting security prompt is not automatically
dismissed. Autodesk documents the AppData trust requirement in its
[plugin installation guide](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-Customization/files/GUID-5E50A846-C80B-4FFD-8DD3-C20B22098008.htm).
`-RegisterClaudeDesktop` is optional and requires Claude closed. Plugin users
already have their server registered through the plugin manifest and do not
need that switch.

## Build a reviewable distribution

On a development machine with Autodesk DLLs and the repository SDK:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/install.ps1 -Years 2025 -Edition release -PackageOut artifacts/horizun-civil3d-mcp-0.9.1.zip
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build-plugin.ps1 -ReleasePackage artifacts/horizun-civil3d-mcp-0.9.1.zip -OutDirectory artifacts
```

The first command tests, compiles and packages without installing. The second
validates the embedded bundle/server version and builds:

- `horizun-civil3d-mcp-0.8.1.zip`: prebuilt Autodesk bundle and MCP server.
- `horizun-civil3d-mcp-0.8.1-plugin.zip`: portable plugin, skills and embedded release.
- `horizun-civil3d-mcp-0.8.1.mcpb`: Claude Desktop extension.
- `SHA256SUMS`: SHA-256 of the release ZIP, plugin ZIP and MCPB.

The embedded ZIP is pinned by its actual SHA-256 in `runtime-release.json`.
The source metadata has no invented digest: when there is no embedded ZIP,
the bootstrap resolves the exact version's asset digest from GitHub before
executing it. Failure preserves diagnostics and does not fall back to source
compilation.

Run `scripts/plugin.tests.ps1` for the isolated bootstrap tests and
`scripts/build-plugin.tests.ps1 -ReleasePackage <zip>` for distribution checks.
No source test changes the real client's configuration or installs an Autodesk
add-in. Publishing these files is a separate approved release action.
