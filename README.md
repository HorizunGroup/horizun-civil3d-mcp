# Horizun Civil 3D MCP

The bridge between Claude (or any MCP client) and a running **Autodesk Civil 3D**, by Horizun Group.

It is a product of its own, separate from Horizun Revit MCP, but it keeps the same contract:

- **It never reports work it did not verify.** Every write is re-read from the drawing in a new transaction
  after the commit. If the drawing does not hold what was asked for, the call is an error, even when nothing threw.
- **Writes are dry runs by default.** A dry run returns the plan and a single-use `confirmation_token`. The token
  is bound to the drawing, to the exact request and to the plan it resolved. You apply the write with
  `dry_run=false` plus that token.
- **Everything is resolved before the transaction.** A missing or ambiguous name is refused, and the refusal
  lists the candidates. Nothing is guessed.
- **One Civil 3D operation at a time.** Requests go through a bounded FIFO queue. If Civil 3D is busy (a command
  or a modal dialog is open), the call is refused with "busy" and nothing runs. The bridge never sends ESC.
- **Units are explicit.** Every answer states the drawing's linear, area and volume units.
- **No network listener.** The bridge only uses a local named pipe, restricted to the current user, plus a
  256-bit token.

```
Claude / MCP client
      |  MCP over stdio
horizun-civil3d-mcp.exe          (src/Horizun.Civil3D.Server)
      |  named pipe "Horizun.Civil3D-<pid>" + token
Horizun.Civil3D.dll in acad.exe  (src/Horizun.Civil3D.Plugin)
      |  RequestGate (FIFO, 16) -> main thread, application context -> DocumentLock -> Transaction
Civil 3D .NET API (AeccDbMgd) + AutoCAD .NET API
```

## Tools (v0.7.4, 26 tools)

| Tool | Actions | Effect |
|---|---|---|
| `horizun_c3d_health` | n/a | read |
| `horizun_c3d_target` | list / select instance | session |
| `horizun_c3d_document` | `info`, `list_open`, `object_census`, `save` | read; `save` = full_write |
| `horizun_c3d_query` | `list`, `get` over 15 Civil 3D object types | read |
| `horizun_c3d_styles` | `list`, `get` (with the objects using each style) | read |
| `horizun_c3d_surface` | 15 actions: reads, sampling, volumes, edits, add_data, paste, elevation/slope analysis, style_display | read / safe_write |
| `horizun_c3d_grading` | `create_geometric`: geometric grading engine (native gradings have no API) | safe_write |
| `horizun_c3d_feature_line` | create from polylines, set elevations, rename, export 3D polyline | safe_write |
| `horizun_c3d_execute_csharp` | Roslyn escape hatch, off by default, self-reported | unsafe_code |
| `horizun_c3d_probe` | live API signatures | read |
| `horizun_c3d_alignment` | get, station_offset, create_from_polyline, create_by_pis, create_offset | read / safe_write |
| `horizun_c3d_profile` | get, elevation_at, check_k, create_from_surface, create_layout, create_view | read / safe_write |
| `horizun_c3d_sections` | list, get_section, create_sample_lines, create_section_views | read / safe_write |
| `horizun_c3d_corridor` | get, assemblies (create/import), create, add_region, rebuild, create_surface | read / safe_write |
| `horizun_c3d_labels` | 15 actions: alignment, surface, profile, notes, segments, set_text, erase | read / safe_write |
| `horizun_c3d_layers` | list, create, set, set_current, layer states | read / safe_write |
| `horizun_c3d_entities` | query, get, draw (9 types), set_properties, transform, offset, explode, join, erase | read / safe_write; erase = full_write |
| `horizun_c3d_dimensions` | linear, aligned, angular, radial, diameter, ordinate, chain/baseline, mleader, set_text | read / safe_write |
| `horizun_c3d_cad_styles` | text/dim/mleader styles, current, annotation scales, linetypes | read / safe_write |
| `horizun_c3d_blocks` | list, references, define, insert, set_attributes, set_dynamic, import | read / safe_write |
| `horizun_c3d_tables` | list, get, create (rows/CSV), set_cells | read / safe_write |
| `horizun_c3d_layouts` | list, devices, create, rename, viewport, page_setup; delete and plot_pdf = full_write | read / safe_write / full_write |
| `horizun_c3d_cleanup` | purge_preview, drawing_report, xrefs, standards_check; purge = full_write | read / full_write |
| `horizun_c3d_pipes` | catalog, list, create_network, add_structures, add_pipes, validate | read / safe_write |
| `horizun_c3d_points` | list, groups, create, import, export_csv, elevations_from_surface, group_create; erase = full_write | read / safe_write |
| `horizun_c3d_exchange` | shortcuts_status, shortcuts_reference, export_landxml; shortcuts_publish = full_write | read / safe_write |

**Live-verified on Civil 3D 2025 (v0.6.9, 2026-10-02): 284/284** with `scripts/verify_live.py` + `scripts/verify_blocks.py` on the fixture drawing. FULL WRITE actions are verified as refused under safe_write. The Horizun Hub ribbon has a **"Canal C#"** button (`HZ_CSHARP`) that turns the C# channel on with a confirmation; it turns itself off at the next Civil 3D start.

`docs/CIVIL3D.md` holds the capability matrix and the evidence for each capability. `docs/ROADMAP.md` holds the
plan for later phases.

## Install (people who use it)

Download the latest `horizun-civil3d-mcp-<version>.zip` from this repository's **Releases** page. Nothing is compiled
on your machine and you do not need the source or the .NET SDK.

1. Save your drawings and close every Civil 3D window.
2. Extract the zip and, inside the extracted folder, run:

```powershell
powershell -ExecutionPolicy Bypass -File .\install.ps1 -RegisterClaudeDesktop
```

3. Restart Claude Desktop and open Civil 3D. The **Horizun Hub** ribbon tab shows "Estado del puente", plus the two
   owner switches: **Canal C#** and **Escritura completa**. Both ask for confirmation and turn themselves off the
   next time Civil 3D starts.

Every installed file is checked against the package (SHA-256), and a failed install rolls back.

## Develop (Horizun developers)

You need Civil 3D 2025, 2026 or 2027 and the .NET 10 SDK (it also builds the net8 targets).

- Branches: `develop` is where work happens; `main` only receives tested versions, and every version on `main` is
  tagged and published as a Release.
- `pwsh scripts/install.ps1 -RegisterClaudeDesktop` builds from source and installs the **development edition**. It
  adds a "Desarrollo" ribbon panel with **Sondeo API** and **Dibujo de ensayo** (the deterministic test drawing that
  `scripts/verify_live.py` checks against).
- `pwsh scripts/install.ps1 -Edition release -PackageOut horizun-civil3d-mcp-<version>.zip` builds the **release
  edition** (no developer panel) as an installable zip for a GitHub Release.
- `-DryRun` builds and stages everything without installing anything.
- AI assistants: start with `CLAUDE.md` / `AGENTS.md` and `docs/handoff/00_START_HERE.md`. Personal working notes go
  in `.local/`, which git ignores.

## Surface tool (live-verified, v0.3.4)

`horizun_c3d_surface` has twelve actions for reads, sampling, volumes and confirmed, verified surface edits, including add_data and paste. All of them are live-verified on Civil 3D 2025 against the deterministic fixture (`HZ_BUILD_FIXTURE` + `scripts/verify_live.py`: 46/46, UNDO included). See [surface operations](docs/SURFACES.md).

## ChatGPT

ChatGPT can use this stdio server through OpenAI Secure MCP Tunnel. The installer includes isolated Civil 3D helpers; account setup and a real ChatGPT tool call complete the connection. See [ChatGPT setup and validation](docs/CHATGPT.md).

For an existing Civil 3D MCP installation, add only the helpers with `pwsh scripts/install-client-tools.ps1`; Civil 3D can remain open.

## Permission profiles

Profiles live in `%USERPROFILE%\.horizun\civil3d\settings.json`. Both the server and the plug-in read them:

```json
{ "permission_profile": "safe_write", "allowed_tools": [], "denied_tools": [], "enable_execute_csharp": false, "paused": false }
```

| Profile | What it allows |
|---|---|
| `read_only` | Reading only |
| `safe_write` | Default. Typed, undoable drawing edits |
| `full_write` | Also save, export and data shortcuts |
| `unsafe_code` | Also the C# escape hatch, together with `enable_execute_csharp` |

A settings file that cannot be parsed, or that names an unknown profile, fails closed to `read_only`.

## Develop

```bash
dotnet test tests/Horizun.Civil3D.Core.Tests            # no Civil 3D needed
dotnet build src/Horizun.Civil3D.Plugin -p:Civil3DYear=2025
dotnet run --project tools/Horizun.Civil3D.ApiProbe -- --year 2025 Autodesk.Civil.DatabaseServices.TinSurface
```

Read `CLAUDE.md` before contributing.

Licensed under Apache 2.0. See `NOTICE` for Autodesk trademarks and for the work this project derives from.
