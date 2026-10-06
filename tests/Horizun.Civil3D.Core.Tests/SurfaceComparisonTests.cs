using System.Text.Json.Nodes;
using Horizun.Civil3D.Core;

namespace Horizun.Civil3D.Core.Tests;

public class SurfaceComparisonTests
{
    private static JsonArray Points(params double[] x) => new(x.Select(v => (JsonNode)new JsonObject { ["x"] = v, ["y"] = 0 }).ToArray());
    private static JsonObject Request() => new() { ["action"] = "compare_design", ["design"] = "Design", ["built"] = "Built", ["tolerance"] = 2, ["points"] = Points(0) };

    [Fact]
    public void Signed_deviations_and_inclusive_tolerance_match_known_plane_offsets()
    {
        var r = SurfaceComparison.Evaluate(Points(-2, 0, 4), 2, (x,y) => new(100), (x,y) => new(100+x));
        Assert.Equal(-2, Hz.Num(r, "minimum_deviation")); Assert.Equal(4, Hz.Num(r, "maximum_deviation"));
        Assert.Equal(2.0/3, Hz.Num(r, "mean_deviation")!.Value, 12);
        Assert.Equal(2, Hz.Num(r, "mean_absolute_error")); Assert.Equal(Math.Sqrt(20.0/3), Hz.Num(r, "rmse")!.Value, 12);
        Assert.Equal(2, Hz.Int(r, "passed_samples")); Assert.Equal(1, Hz.Int(r, "failed_samples"));
        Assert.False(Hz.Bool(r, "all_requested_samples_within_tolerance"));
        Assert.Contains("built_minus_design", r.ToJsonString(Hz.Compact));
    }

    [Fact]
    public void Missing_domains_never_count_as_zero_or_prove_complete_acceptance()
    {
        var r = SurfaceComparison.Evaluate(Points(0,1), 0, (x,y) => new(10), (x,y) => x==0 ? new(10) : new(null,"outside_surface_domain"));
        Assert.Equal(0.5, Hz.Num(r, "sample_coverage")); Assert.Equal(1, Hz.Int(r, "valid_samples"));
        Assert.Null(Hz.Bool(r, "all_requested_samples_within_tolerance"));
        var missing = (JsonObject)((JsonArray)r["samples"]!)[1]!;
        Assert.Null(missing["deviation"]); Assert.Null(missing["within_tolerance"]);
        Assert.Equal("outside_surface_domain", Hz.Str(missing, "built_missing_reason"));
    }

    [Fact]
    public void No_valid_pair_has_null_statistics_and_numeric_overflow_is_explicit()
    {
        var r = SurfaceComparison.Evaluate(Points(0), 1, (x,y)=>new(-double.MaxValue), (x,y)=>new(double.MaxValue));
        Assert.Null(r["rmse"]); Assert.Null(r["mean_deviation"]); Assert.Equal(0, Hz.Int(r,"valid_samples"));
        Assert.NotNull(((JsonArray)r["samples"]!)[0]!["deviation_missing_reason"]);
        Assert.NotNull(r["statistics_missing_reason"]);
    }

    [Fact]
    public void Scaled_statistics_do_not_overflow_on_large_finite_deviations()
    {
        var r = SurfaceComparison.Evaluate(Points(-1,1), double.MaxValue, (x,y)=>new(0), (x,y)=>new(x*double.MaxValue));
        Assert.Equal(0, Hz.Num(r,"mean_deviation")); Assert.Equal(double.MaxValue, Hz.Num(r,"rmse"));
        Assert.Equal(double.MaxValue, Hz.Num(r,"mean_absolute_error")); Assert.True(Hz.Bool(r,"all_requested_samples_within_tolerance"));
    }

    [Fact]
    public void Nonfinite_values_become_missing_and_report_serializes_without_reflection()
    {
        var r = SurfaceComparison.Evaluate(Points(0), 0, (x,y)=>new(double.NaN), (x,y)=>new(double.PositiveInfinity));
        Assert.Equal(1, Hz.Int(r,"missing_samples")); Assert.NotNull(r.ToJsonString(Hz.Compact));
    }

    [Fact]
    public void Explicit_duplicate_samples_retain_weight()
    {
        var r = SurfaceComparison.Evaluate(Points(0,0,3), 1, (x,y)=>new(0), (x,y)=>new(x));
        Assert.Equal(1, Hz.Num(r,"mean_deviation")); Assert.Equal(3, Hz.Int(r,"requested_samples"));
    }

    [Fact]
    public void Input_rejects_invalid_budget_coordinates_tolerance_and_surface_pair()
    {
        Assert.Null(SurfaceComparison.Validate(Request()));
        var a=Request(); a["tolerance"]=-1; Assert.NotNull(SurfaceComparison.Validate(a));
        a=Request(); a["built"]="design"; Assert.NotNull(SurfaceComparison.Validate(a));
        a=Request(); a["points"]=new JsonArray(); Assert.NotNull(SurfaceComparison.Validate(a));
        a=Request(); a["points"]=Points(new double[10001]); Assert.NotNull(SurfaceComparison.Validate(a));
        a=Request(); ((JsonObject)((JsonArray)a["points"]!)[0]!)["z"]=0; Assert.NotNull(SurfaceComparison.Validate(a));
        a=Request(); a["points"]=new JsonArray(new JsonObject { ["x"]="0",["y"]=0 }); Assert.NotNull(SurfaceComparison.Validate(a));
        a=Request(); a["dry_run"]=true; Assert.NotNull(SurfaceComparison.Validate(a));
    }
}
