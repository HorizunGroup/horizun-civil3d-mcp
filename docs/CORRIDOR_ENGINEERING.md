# Corridor engineering

Civil 3D 2024 has no `UseSameSideTarget` API: queries return null for this
option; explicitly supplying it refuses before writing.

These `horizun_c3d_corridor` actions use measured managed API signatures from
Civil 3D 2024, 2025 and 2026. The fixture below was measured live in 2025;
2024/2026 host acceptance remains required. Compilation and Core tests alone
do not constitute live geometry evidence.

## Select a region

Provide exactly one corridor `name` or `handle`. `baseline_index` and
`region_index` are zero based and default to zero. Supply `target_document`
according to the shared tool contract.

| Action | Additional inputs | Result |
|---|---|---|
| `get_targets` | none | Every target's index, logical/display name, subassembly/group, type, handles and targeting options |
| `set_targets` | `targets`; optional `rebuild` | Verified target assignments; empty handles explicitly clear a target |
| `applied_geometry` | optional `shape_codes`, station bounds, `max_stations` | Applied shape areas, links and points in XYZ and station/offset/elevation coordinates |
| `region_quantities` | required `shape_codes`; optional bounds, `max_stations`, `material_map` | Per-code estimated volumes, coverage and individual station areas |
| `split_region` | `split_station`, `new_region_name`; optional `rebuild` | Native region split preserving readable definitions |
| `merge_regions` | `last_region_index`; optional `rebuild` | Native merge of inclusive region range on one baseline |

## Targets

Read `get_targets` first. A target update is, for example:

```json
{"target_index":0,"handles":["A1B"],"target_to_option":"Nearest","use_same_side_target":true}
```

Handles must resolve in the target drawing. Surface, alignment, profile,
offset and elevation targets accept explicitly checked object families.
Pipe targets and other unsupported combinations are refused by name.
Unspecified targets and options remain unchanged. Resolved before/after
definitions enter the confirmation plan.

`target_to_option` is applicable in this bridge to Offset/Elevation targets with
at least two assigned handles. Offset permits Nearest/Farthest; Elevation also
permits Flattest/Steepest. `use_same_side_target` applies only to Offset and is
unavailable in the measured 2024 API. Inapplicable/unreadable getter values are
null with an explicit per-field reason. Assigning multiple handles from an
unreadable zero/single-target option state requires an explicit option; the
bridge never guesses a hidden default.

## Applied geometry and quantities

The corridor must be rebuilt and the region must have applied assemblies.
Read requests do not rebuild or alter it. Up to 2000 applied stations and
100000 geometry vertices are returned; narrow the range for larger corridors.

`region_quantities` uses **average end area** between successive native applied
station areas: distance times the mean of the two areas. Results are estimates
in drawing units cubed, not Civil 3D native QTO or material-list quantities.
`material_map` attaches caller-supplied material names to codes; it does not
query Civil material lists or change the corridor.

An absent shape at a station is missing geometry. Integration never connects
across missing stations. Full requested-range volume is null when a boundary
has no applied station, an area is missing, or there are fewer than two stations.
Covered sample volume and missing length remain traceable. Nonfinite values,
overflow and invalid station order are refused. Shape codes can overlap:
adding their quantities may double count.

## Native split and merge safeguards

Native split retains the starting region and creates the ending region.
Merge calls `BaselineRegion.Merge(first,last)` on the first region. Autodesk
requires continuous regions on the same baseline; see
[Autodesk Merge documentation](https://help.autodesk.com/cloudhelp/2022/ENU/Civil3D-API/files/html/b8716411-f733-f93f-1e67-69c36b598e8d.htm).

Regions with offset baselines or overridden stations are refused because their
definitions cannot be fully preserved. Additional station values must reconcile
with their descriptors. Merge additionally requires identical assemblies,
every readable frequency value/option/flag and target definition, contiguous
limits and no duplicate additional stations. Different definitions are never
silently discarded.

Public applied-assembly frequency settings and target definitions are
snapshotted. Additional stations retain station and description, partitioned
between split regions; a station exactly at the split belongs to both.
Native operations are checked before commit and abort if definitions change
unexpectedly. A separate transaction verifies counts, limits, definitions,
additional station descriptions and the new split name.

Writes default to dry run and require a single-use confirmation token.
Rebuilding defaults to true. The operation is one drawing undo step and saves
no file. Internal definitions unavailable through the public API cannot be
independently certified. Test target behavior and downstream surfaces in a
generated fixture before applying these operations to production data.

## Fixture acceptance procedure

On a generated fixture with a real imported assembly: read targets, dry run,
apply and re-read; test wrong types, clearing and unchanged targets. Compare
applied shape coordinates and known constant/variable section volumes. Test
split then merge, boundary additional stations, every frequency flag, target
options, refusal of overrides/offset baselines, token invalidation and UNDO.

## Live 2025 evidence (2026-10-05)

Using a new generated metric drawing and the Autodesk tutorial assembly
`Primary Road Full Section`, the typed MCP acceptance passed target metadata,
two Surface target assignments with complete re-read, wrong object-family
refusal, applied XYZ/SOE shape geometry, independent average-end-area checks
for Pave1/Pave2/Base/SubBase/Curb/Sidewalk, unknown code and boundary refusals,
native split/merge and preservation of readable definitions/targets/station areas.
There were 64 successful checks before an inactive-paper-layout viewport bug
was observed and corrected.

A further 22 checks passed after reloading: alignment-linked viewport creation,
explicit refresh, WCS target (50,50,0), camera/scale/locked persistence, native
layout state restoration and out-of-range refusal. An additional generated
TIN was undone once; a fresh typed read independently confirmed its absence.
The generic `undo_last` result remains partial because it has no before-value
snapshots for modified containers; no complete restoration claim is made.

Run `scripts/verify_engineering_live.py` with an explicit PID, target drawing
and new absolute output directory. It defaults to read-only preflight;
`--execute` creates disposable fixture objects. `--resume-prefix` and
`--phase sheets` support reviewed continuation after a stopped run.
No screen input, arbitrary C# execution or saves are performed. Source tutorial
DWG is opened read-only and remains unchanged. Complete response evidence is
saved locally; no private drawing paths/handles are tracked in Git.

Offset/Elevation multi-target selection options, regions with overridden stations
or offset baselines, and 2024/2026 live hosts still require separate acceptance.
