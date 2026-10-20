# Built versus design surface comparison

`horizun_c3d_surface`, action `compare_design`, reads two elevation surfaces at an explicit set of drawing XY locations. It does not create a volume surface, rebuild source surfaces, or commit a transaction.

```json
{
  "action": "compare_design",
  "design": "Design surface",
  "built": "Built surface",
  "points": [{"x": 100, "y": 200}, {"x": 110, "y": 200}],
  "tolerance": 0.02
}
```

Names resolve exactly. The two selected objects must differ. Volume surfaces and out-of-date surfaces are refused; reference surfaces must be valid and current. Coordinates, elevations, tolerance, deviations, MAE and RMSE use Civil drawing distance units, included in the response. No coordinate transformation is applied.

## Interpretation

- Deviation = built elevation minus design elevation. Positive means above design.
- A sample passes when the absolute deviation is less than or equal to the supplied tolerance.
- Minimum, maximum and mean retain signed deviations; MAE and RMSE are nonnegative.
- Every explicit location has equal weight. Repeated locations retain their explicit repeated weight.
- `sample_coverage` is valid paired samples / requested samples. It is not a fraction of surface area.
- Missing elevations, points outside either surface domain, non-finite values and subtraction overflow have null deviation and null tolerance result, with reasons.
- `all_requested_samples_within_tolerance` is null when any requested sample is missing. Valid samples still report individual pass/fail counts.
- With no paired values, all statistics are null with `statistics_missing_reason`.

At most 10,000 locations are accepted. Explicit sampling does not prove conformance between locations and does not integrate area or volume. No implicit grid, nearest-point substitution or extrapolation is performed. Scaled summation and squaring prevent overflow in statistics for finite deviations.

## Evidence and next verification

Host-independent tests cover known signed offsets, inclusive tolerance, missing domains, duplicate weighting, non-finite values, overflow and input limits. API signatures are present in `docs/api-probes/2024/AeccDbMgd.dll.compatibility.txt`, the corresponding 2026 file, and `docs/api-probes/2025/AeccDbMgd.audit-2025.txt`.

Live acceptance remains required: compare generated planar fixture surfaces with known elevation offsets and exact XY samples, include points outside the domain, then repeat with an out-of-date surface. No live Civil 3D result is claimed by these tests.
