# Civil 3D build and runtime compatibility

## Target versions and current evidence

The owner requires Civil 3D **2024 and 2026**. Both are build targets. The local
machine has 2025 installed and signed Autodesk SDK/reference assemblies for
2024 and both 2026 runtime families. Complete add-ins compile against those
references; runtime behavior in 2024/2026 remains unverified.

| Host | Add-in runtime | Current evidence | Missing gate |
|---|---|---|---|
| 2024 | .NET Framework 4.8 (`net48`) | Complete add-in B against signed 2024 references; offline API probes; 391 runtime tests | Live fixture in the matching installation |
| 2026 through 2026.2.1 | .NET 8 (`net8.0-windows`) | Complete add-in B against signed 2026 references; offline API probes; 391 runtime tests | Live fixture in the matching installation |
| 2026.2.2 and later | .NET 10 (`net10.0-windows`) | Complete add-in B against AutoCAD.NET 25.1.1/net10 and Civil3D.NET 13.8.1516/net8 libraries; 391 runtime tests | Verify these Civil libraries against the actual updated installation, then live fixture |
| Installed 2025 | .NET 8, measured from local DLL metadata | Complete add-in B; 552 Core/Server tests; native engineering/export/Revit fixture acceptance | Representative production-project pilot; see acceptance report |
| 2027 | .NET 10 | Build target retained; no new Autodesk evidence | Own Autodesk DLLs and live fixture |

These suites overlap: the runtime suite links existing Core tests and adds
protocol/transport regressions. They are not distinct cases across runtimes. Its production
pipe and logging code are compiled unchanged; only Autodesk main-thread dispatch
is replaced with a test dispatcher. No drawing API runs in these tests.

The out-of-process MCP server stays self-contained .NET 8, independent of the
CLR inside `acad.exe`. Core targets `net48;net8.0;net10.0`. The existing terrain
package, validation, permissions, single-use tokens and contract apply to all
Core targets; contract `171049b89c39afc5120da6ff` is asserted on each runtime.
Civil-to-Revit native placement passed the separate Revit2025 fixture acceptance gate;
project-specific shared coordinates still require independent controls.

## Building on a machine with the required Autodesk DLLs

```powershell
# Metadata only: does not load Autodesk assemblies or start Civil 3D.
dotnet run --project src/Horizun.Civil3D.Server -c Release -- --inspect-host 'C:\Program Files\Autodesk\AutoCAD 2026' 2026

dotnet build src/Horizun.Civil3D.Plugin -c Release -p:Civil3DYear=2024
dotnet build src/Horizun.Civil3D.Plugin -c Release -p:Civil3DYear=2026 -p:Civil3DRuntime=net8
# Use the following instead for a .NET 10 update:
dotnet build src/Horizun.Civil3D.Plugin -c Release -p:Civil3DYear=2026 -p:Civil3DRuntime=net10

dotnet test tests/Horizun.Civil3D.Core.Tests -c Release
dotnet test tests/Horizun.Civil3D.Runtime.Tests -c Release

# Prepare only: automatically select each add-in runtime from PE metadata.
pwsh scripts/install.ps1 -Years 2024,2026 -DryRun
pwsh scripts/install.ps1 -Years 2024,2026 -Edition release -PackageOut artifacts/civil3d-2024-2026.zip
```

Build-only reference trees may be supplied with `-AcadRoots @{2024='D:\SDK\AutoCAD 2024';2026='D:\SDK\AutoCAD 2026'}`;
direct `dotnet build` accepts `-p:AcadDir=...`. These must contain the actual
matching Autodesk assemblies, including `C3D`, `ACA` and root dependencies.
AutoCAD alone or an Object Enabler is not the complete Civil 3D development host.
Never relabel 2025 DLLs as 2024/2026 or substitute stub Autodesk types to claim B.

Direct 2026 builds require `Civil3DRuntime` explicitly. Inspect the references
first and use the reported runtime. The installer performs this selection
automatically. Modern 2025 updates are also selected from metadata, rather than
assuming that the marketing year permanently determines the CLR.

## Packaging and update matching

`host-builds.json` in the bundle records year, runtime, target framework and
the AutoCAD/Civil managed assembly versions used for each build. Package-mode
installation compares those versions with the local Autodesk DLL metadata,
rejecting mismatched updates before copying installed files. Bundle ranges
include `R24.3` (2024), `R25.0` (2025) and `R25.1` (2026).

Each prepared package contains **one built runtime variant per included year**.
A package produced with 2026.2.2+ references is not the package for an earlier
2026 .NET 8 installation. Build the matching package for each update family;
no unbuilt variant is included. The canonical release includes **2024/2025/2026 net8**;
the separate `horizun-civil3d-mcp-0.9.0-2026-net10.zip` contains the 2026 net10 build.
After changing Autodesk runtime/update, prepare and install the matching build.

## Reference provenance (2026-10-05)

Civil references come from Autodesk's `Civil3D.NET` NuGet packages 13.6.1781
(2024) and 13.8.1516 (2026). AutoCAD base references come from
`Speckle.AutoCAD.API` 2024.0.0/2026.0.0, with valid Autodesk Authenticode signatures.
The 2026 net10 AutoCAD references come from the official `AutoCAD.NET`,
`AutoCAD.NET.Core` and `AutoCAD.NET.Model` packages 25.1.1.
Only DLLs were extracted; package targets were not executed. Reference downloads
stay in ignored `.local`, and Autodesk DLLs are not redistributed in releases.
SDK libraries targeting Framework 4.7 are consumed by the net48 add-in.
The net10 build can reference net8 Civil libraries; this compilation proves
member signatures, not binary compatibility with an unmeasured Civil update.
The add-in also refuses a different AutoCAD year/CLR at startup before publishing
its pipe or resetting the C# session setting.

## Offline probes and live acceptance

The offline probe accepts custom Autodesk roots and selects Framework's
`mscorlib` for 2024 or the .NET 8/10 BCL for modern hosts:

```powershell
dotnet run --project tools/Horizun.Civil3D.ApiProbe -- --year 2024 --acad-dir 'D:\SDK\AutoCAD 2024' Autodesk.Civil.DatabaseServices.TinSurface
```

Use `--runtime-dir` to provide the matching framework directory if needed.
Save real year-specific signatures under `docs/api-probes/<year>/`. Those 2024/
2026 dumps remain pending. The modified resolver has only been exercised against
the installed 2025 assemblies.

On each target installation: save drawings, close Civil 3D and confirm before
deployment; install server/add-in together; reopen and verify `health` year,
`build_runtime`, contract, idle/busy/authentication and the generated fixture.
Exercise reads, rehearsals, confirmed writes, rereads, stale tokens, UNDO and
CSV/LandXML/DWG/terrain ZIP export. Repeat both 2026 runtime families; preserve
client drawings. Only that evidence earns L for that year/update.

## Primary Autodesk references (checked 2026-10-05)

- [AutoCAD 2024 managed development requirements](https://help.autodesk.com/cloudhelp/2024/ITA/OARX-DevGuide-Managed/files/GUID-450FD531-B6F6-4BAE-9A8C-8230AAC48CB4.htm): .NET Framework 4.8 for AutoCAD 2024-based products.
- [Civil 3D 2026 library references](https://help.autodesk.com/cloudhelp/2026/ENU/Civil3D-DevGuide/files/GUID-267E68C8-AD2D-4F7F-87DF-831018D56CDB.htm): .NET 8 through 2026.2.1; .NET 10 from 2026.2.2. Direct content verified by HTTP when the browsing reader initially failed.
- [Modern Civil .NET migration guidance](https://help.autodesk.com/cloudhelp/2026/ENU/Civil3D-DevGuide/files/GUID-E6657034-71E5-4753-8AFD-139DC612B86D.htm): migrate API dependencies and test in the actual runtime.
