using System.Text.Json.Nodes;
using Horizun.Civil3D.Core;

namespace Horizun.Civil3D.Core.Tests;

public class GeoMathTests
{
    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(-1, 0, 90)]     // west of +Y is counter-clockwise
    [InlineData(1, 0, -90)]
    [InlineData(0, -1, 180)]
    [InlineData(-1, 1, 45)]
    public void Angle_from_Y_is_counter_clockwise(double x, double y, double expected) =>
        Assert.Equal(expected, GeoMath.FromYCcwDeg(x, y)!.Value, 9);

    [Fact]
    public void Degenerate_vectors_are_null_not_zero()
    {
        Assert.Null(GeoMath.FromYCcwDeg(0, 0));
        Assert.Null(GeoMath.FromYCcwDeg(double.NaN, 1));
        var d = GeoMath.Direction(0, 0);
        Assert.Null(d["from_y_ccw_deg"]);
        Assert.NotNull(Hz.Str(d, "reason"));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(90, 270)]
    [InlineData(-90, 90)]
    [InlineData(180, 180)]
    public void Azimuth_is_the_clockwise_twin(double ccw, double az) => Assert.Equal(az, GeoMath.AzimuthDeg(ccw), 9);

    [Theory]
    [InlineData(190, -170)]
    [InlineData(-180, 180)]
    [InlineData(540, 180)]
    [InlineData(-190, 170)]
    public void Wrap_keeps_minus_180_exclusive(double input, double expected) => Assert.Equal(expected, GeoMath.Wrap180(input), 9);

    [Fact]
    public void Turn_crosses_the_south_seam_the_short_way()
    {
        // From 179 deg to -179 deg is +2 deg, not -358.
        var a = Math.PI * 179 / 180;
        var b = -Math.PI * 179 / 180;
        Assert.Equal(2, GeoMath.TurnDeg(-Math.Sin(a), Math.Cos(a), -Math.Sin(b), Math.Cos(b))!.Value, 9);
        Assert.Equal(-0.5, GeoMath.TurnDeg(0, 1, Math.Sin(Math.PI * 0.5 / 180), Math.Cos(Math.PI * 0.5 / 180))!.Value, 9);
    }

    [Fact]
    public void Angle_reports_radians_and_degrees_and_serializes_without_reflection()
    {
        var o = GeoMath.Angle(Math.PI / 2);
        Assert.Equal(90, Hz.Num(o, "deg")!.Value, 9);
        var json = new JsonObject { ["a"] = o, ["d"] = GeoMath.Direction(1, 1) }.ToJsonString(Hz.Compact);
        Assert.Contains("\"azimuth_deg\":45", json);
    }
}
