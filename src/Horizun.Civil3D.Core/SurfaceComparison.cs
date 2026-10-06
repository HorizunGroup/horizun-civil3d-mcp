using System.Text.Json.Nodes;

namespace Horizun.Civil3D.Core;

/// <summary>Equal-weight vertical deviations at explicit XY locations. No area or volume inference.</summary>
public static class SurfaceComparison
{
    public const int MaximumSamples = 10000;
    public static string? Validate(JsonObject args)
    {
        var allowed = new[] { "action", "design", "built", "points", "tolerance", "target_document" };
        if (args.Any(p => !allowed.Contains(p.Key))) return "compare_design received an unused field.";
        if (Hz.Str(args, "action") != "compare_design") return "Expected compare_design.";
        foreach (var key in new[] { "design", "built" })
            if (string.IsNullOrWhiteSpace(Hz.Str(args, key))) return key + " must be an exact surface name.";
        if (args["target_document"] != null && Hz.Str(args, "target_document") == null) return "target_document must be a string.";
        if (string.Equals(Hz.Str(args, "design"), Hz.Str(args, "built"), StringComparison.OrdinalIgnoreCase)) return "design and built must be distinct surfaces.";
        var tolerance = Hz.Num(args, "tolerance");
        if (tolerance == null || !Hz.IsFinite(tolerance.Value) || tolerance < 0) return "tolerance must be a finite nonnegative vertical distance in drawing units.";
        if (args["points"] is not JsonArray points || points.Count < 1 || points.Count > MaximumSamples) return "points must contain 1..10000 explicit XY locations.";
        foreach (var point in points)
        {
            if (point is not JsonObject p || p.Count != 2 || p["x"] == null || p["y"] == null) return "Each point must contain exactly x and y.";
            foreach (var key in new[] { "x", "y" })
                if (Hz.Num(p, key) is not { } n || !Hz.IsFinite(n)) return "Sample coordinates must be finite numbers.";
        }
        return null;
    }

    public sealed record Elevation(double? Value, string? Reason = null);

    public static JsonObject Evaluate(JsonArray points, double tolerance, Func<double, double, Elevation> design, Func<double, double, Elevation> built)
    {
        if (!Hz.IsFinite(tolerance) || tolerance < 0 || points.Count < 1 || points.Count > MaximumSamples) throw new ArgumentException("Invalid comparison budget or tolerance.");
        var rows = new JsonArray();
        var deviations = new List<double>();
        var passed = 0;
        foreach (var node in points)
        {
            var p = node as JsonObject ?? throw new ArgumentException("Invalid sample point.");
            var x = Hz.Num(p, "x") ?? double.NaN; var y = Hz.Num(p, "y") ?? double.NaN;
            if (!Hz.IsFinite(x) || !Hz.IsFinite(y)) throw new ArgumentException("Non-finite XY.");
            var d = design(x, y); var b = built(x, y);
            var dv = d.Value is { } dn && Hz.IsFinite(dn) ? d.Value : null;
            var bv = b.Value is { } bn && Hz.IsFinite(bn) ? b.Value : null;
            double? delta = dv.HasValue && bv.HasValue && Hz.IsFinite(bv.Value - dv.Value) ? bv.Value - dv.Value : null;
            bool? pass = delta.HasValue ? Math.Abs(delta.Value) <= tolerance : null;
            var row = new JsonObject { ["x"] = x, ["y"] = y, ["design_elevation"] = dv, ["built_elevation"] = bv,
                ["deviation"] = delta, ["within_tolerance"] = pass };
            if (!dv.HasValue) row["design_missing_reason"] = d.Reason ?? "Elevation is missing or non-finite.";
            if (!bv.HasValue) row["built_missing_reason"] = b.Reason ?? "Elevation is missing or non-finite.";
            if (dv.HasValue && bv.HasValue && !delta.HasValue) row["deviation_missing_reason"] = "Vertical subtraction exceeded finite numeric range.";
            if (delta.HasValue) { deviations.Add(delta.Value); if (pass == true) passed++; }
            rows.Add(row);
        }
        // Normalize before summing/squaring: valid enormous elevations must not overflow RMSE.
        var scale = deviations.Count == 0 ? 0 : deviations.Max(v => Math.Abs(v));
        double? mean = null, mae = null, rmse = null;
        if (deviations.Count > 0)
        {
            var n = deviations.Count;
            mean = scale == 0 ? 0 : RuntimeCompat.Clamp(deviations.Sum(v => v / scale) / n, -1, 1) * scale;
            mae = scale == 0 ? 0 : RuntimeCompat.Clamp(deviations.Sum(v => Math.Abs(v / scale)) / n, 0, 1) * scale;
            rmse = scale == 0 ? 0 : Math.Sqrt(RuntimeCompat.Clamp(deviations.Sum(v => (v / scale) * (v / scale)) / n, 0, 1)) * scale;
        }
        return new JsonObject { ["samples"] = rows, ["sampling"] = "explicit_xy_equal_weight", ["deviation_convention"] = "built_minus_design",
            ["tolerance"] = tolerance, ["tolerance_boundary"] = "inclusive", ["requested_samples"] = points.Count, ["valid_samples"] = deviations.Count,
            ["missing_samples"] = points.Count - deviations.Count, ["sample_coverage"] = (double)deviations.Count / points.Count,
            ["passed_samples"] = passed, ["failed_samples"] = deviations.Count - passed,
            ["all_requested_samples_within_tolerance"] = deviations.Count == points.Count ? passed == deviations.Count : (bool?)null,
            ["minimum_deviation"] = deviations.Count == 0 ? (double?)null : deviations.Min(), ["maximum_deviation"] = deviations.Count == 0 ? (double?)null : deviations.Max(),
            ["mean_deviation"] = mean, ["mean_absolute_error"] = mae, ["rmse"] = rmse,
            ["statistics_missing_reason"] = deviations.Count == 0 ? "No finite paired elevations were available." : null,
            ["note"] = "Statistics describe the supplied samples only; coverage is a sample fraction, not surface-area coverage. Duplicate XY samples retain their explicit weight. Positive deviations are above design." };
    }
}
