# Security

Please report suspected vulnerabilities privately through GitHub's **Security → Report a vulnerability** for this repository. Do not place credentials, customer drawings, discovery tokens, or local configuration in a public issue.

The default profile is `safe_write`; arbitrary C# execution and full writes are disabled unless the owner explicitly enables them. Writes require a rehearsal and a single-use confirmation bound to the drawing and resolved plan. Installed files are checked by SHA-256. The local pipe is restricted to the current Windows user.

Automatic `undo_last` is disabled in v0.9.2: every request is refused **before native UNDO** with `committed=false`. Native attribution/restoration did not pass a new fixture test. Use Civil 3D's manual UNDO and inspect its effect. File exports and process settings cannot be reverted by drawing UNDO.

## Validation scope

Civil 3D 2025 and Revit 2025 have native fixture evidence. Civil 3D 2024 and 2026 have build and host-independent test evidence; native acceptance is pending. See [compatibility](docs/COMPATIBILITY.md) and [evidence](docs/CIVIL3D.md). An installation must match its Autodesk year, DLL identities and runtime.

The repository excludes local configuration, credentials, customer data, Autodesk binaries and acceptance DWG/RVT files. CI scans both Git history and source for secrets. A narrowly identified historical docstring false positive is recorded in `.gitleaksignore`.
