using System.Text.Json.Nodes;
using Horizun.Civil3D.Core;

namespace Horizun.Civil3D.Core.Tests;

public class GradingEngineTests
{
    private static List<P3> Square(double x0, double y0, double size, double z) => new()
    {
        new(x0, y0, z), new(x0 + size, y0, z), new(x0 + size, y0 + size, z), new(x0, y0 + size, z),
    };

    private static JsonObject S(string json) => (JsonObject)JsonNode.Parse(json)!;

    /// <summary>Horizontal distance from p to the axis-aligned square [x0,x0+s] x [y0,y0+s] (0 inside).</summary>
    private static double OutsideDistance(P3 p, double x0, double y0, double s)
    {
        var dx = Math.Max(Math.Max(x0 - p.X, 0), p.X - (x0 + s));
        var dy = Math.Max(Math.Max(y0 - p.Y, 0), p.Y - (y0 + s));
        return Math.Sqrt(dx * dx + dy * dy);
    }

    [Fact]
    public void Fill_platform_on_flat_ground_daylights_exactly_slope_times_height_away()
    {
        // platform 2 m above flat ground, fill slope 2:1 -> daylight 4 m from every edge (corners: 4 m fan)
        var r = GradingEngine.Run(Square(0, 0, 20, 102), new[] { S("""{"type":"grade_to_surface","slope":2}""") }, Array.Empty<JsonObject>(), 0.5, (_, _) => 100.0);
        var day = r.Lines.Single(l => l.Tag.Contains("daylight")).Points;
        Assert.All(day, p => Assert.InRange(OutsideDistance(p, 0, 0, 20), 3.999, 4.001));
        Assert.All(day, p => Assert.InRange(p.Z, 99.999, 100.001));
        Assert.Equal(0, r.FailedRays);
        var expected = 400 + 4 * 20 * 4 + Math.PI * 16; // square + side strips + quarter-circle corners
        Assert.InRange(Math.Abs(GradingEngine.Area(day)), expected * 0.99, expected * 1.001);
        Assert.Same(r.Lines.Last(), r.BoundaryLine);
    }

    [Fact]
    public void Cut_uses_the_cut_slope()
    {
        // platform 2 m BELOW ground, cut slope 1:1 (fill slope irrelevant) -> daylight 2 m away, on the ground
        var r = GradingEngine.Run(Square(0, 0, 10, 98), new[] { S("""{"type":"grade_to_surface","cut_slope":1,"fill_slope":3}""") }, Array.Empty<JsonObject>(), 0.5, (_, _) => 100.0);
        var day = r.Lines.Last().Points;
        Assert.All(day, p => Assert.InRange(OutsideDistance(p, 0, 0, 10), 1.999, 2.001));
        Assert.All(day, p => Assert.InRange(p.Z, 99.999, 100.001));
    }

    [Fact]
    public void Daylight_lands_on_a_sloped_plane()
    {
        static double Eg(double x, double y) => 100 + 0.02 * x + 0.01 * y;
        var r = GradingEngine.Run(Square(30, 30, 40, 104), new[] { S("""{"type":"grade_to_surface","slope":2}""") }, Array.Empty<JsonObject>(), 0.5,
            (x, y) => x is >= -50 and <= 150 && y is >= -50 and <= 150 ? Eg(x, y) : null);
        var day = r.Lines.Last().Points;
        Assert.All(day, p => Assert.InRange(p.Z - Eg(p.X, p.Y), -1e-3, 1e-3));
        // slope check: drop from the 104 platform over the horizontal distance is 1:2
        Assert.All(day, p => Assert.InRange((104 - p.Z) * 2 - OutsideDistance(p, 30, 30, 40), -0.01, 0.01));
    }

    [Fact]
    public void Offset_then_grade_and_inner_depth()
    {
        var r = GradingEngine.Run(Square(0, 0, 20, 102),
            new[] { S("""{"type":"offset","dist":1}"""), S("""{"type":"grade_to_surface","slope":1}""") },
            new[] { S("""{"type":"grade_to_depth","depth":1,"slope":0.5}""") }, 0.5, (_, _) => 100.0);
        // a mitred 1 m offset of a 20 m square is the 22 m square (vertices sqrt(2) m from the original corners)
        var offset = r.Lines.Single(l => l.Tag.StartsWith("outer1_offset")).Points;
        Assert.InRange(Math.Abs(GradingEngine.Area(offset)), 483.999, 484.001);
        Assert.All(offset, p => Assert.Equal(1.0, Math.Max(Math.Abs(p.X - 10), Math.Abs(p.Y - 10)) - 10, 9));
        // daylight 2 m (2 m drop at 1:1) beyond the 22 m square, corners fanned around ITS corners
        var day = r.Lines.Single(l => l.Tag.Contains("daylight")).Points;
        Assert.All(day, p => Assert.InRange(OutsideDistance(p, -1, -1, 22), 1.999, 2.001));
        var bottom = r.Lines.Single(l => l.Side == "inner").Points;
        Assert.InRange(Math.Abs(GradingEngine.Area(bottom)), 360.99, 361.01); // 20 - 2*0.5 = 19 m side
        Assert.All(bottom, p => Assert.Equal(101, p.Z, 9));
        Assert.Contains("daylight", r.BoundaryLine.Tag);
    }

    [Fact]
    public void Grade_to_elevation_sets_a_flat_inner_line()
    {
        var r = GradingEngine.Run(Square(0, 0, 20, 102), Array.Empty<JsonObject>(), new[] { S("""{"type":"grade_to_elevation","elevation":100,"slope":1}""") }, 0.5, null);
        var inner = r.Lines.Last().Points;
        Assert.All(inner, p => Assert.Equal(100, p.Z, 9));
        Assert.InRange(Math.Abs(GradingEngine.Area(inner)), 255.99, 256.01); // set in 2 m (2 m drop at 1:1) -> 16 m side
        Assert.Equal("base", r.BoundaryLine.Tag); // no outer steps: the footprint is the boundary
    }

    [Fact]
    public void Clockwise_input_is_reoriented_and_duplicates_removed()
    {
        var cw = Square(0, 0, 10, 100); cw.Reverse(); cw.Add(cw[0]);
        var n = GradingEngine.Normalize(cw);
        Assert.Equal(4, n.Count);
        Assert.True(GradingEngine.Area(n) > 0);
    }

    [Fact]
    public void Concave_offset_has_no_self_intersection()
    {
        // L-shape: a large outward offset folds at the concave corner; RemoveLoops must clean it
        var l = new List<P3> { new(0, 0, 0), new(20, 0, 0), new(20, 5, 0), new(5, 5, 0), new(5, 20, 0), new(0, 20, 0) };
        var off = GradingEngine.RemoveLoops(GradingEngine.Offset(GradingEngine.Normalize(l), -2, 0));
        for (var i = 0; i < off.Count; i++)
            for (var j = i + 2; j < off.Count; j++)
            {
                if (i == 0 && j == off.Count - 1) continue;
                Assert.False(Cross(off[i], off[(i + 1) % off.Count], off[j], off[(j + 1) % off.Count]));
            }
    }

    private static bool Cross(P3 a, P3 b, P3 c, P3 d)
    {
        var den = (b.X - a.X) * (d.Y - c.Y) - (b.Y - a.Y) * (d.X - c.X);
        if (Math.Abs(den) < 1e-12) return false;
        var t = ((c.X - a.X) * (d.Y - c.Y) - (c.Y - a.Y) * (d.X - c.X)) / den;
        var u = ((c.X - a.X) * (b.Y - a.Y) - (c.Y - a.Y) * (b.X - a.X)) / den;
        return t > 1e-6 && t < 1 - 1e-6 && u > 1e-6 && u < 1 - 1e-6;
    }

    [Fact]
    public void Collapsing_inner_offset_is_refused() =>
        Assert.Throws<HzRefusal>(() => GradingEngine.Run(Square(0, 0, 20, 100), Array.Empty<JsonObject>(), new[] { S("""{"type":"offset","dist":15}""") }, 0.5, null));

    [Fact]
    public void Daylight_off_the_surface_is_refused_not_invented() =>
        Assert.Throws<HzRefusal>(() => GradingEngine.Run(Square(0, 0, 20, 102), new[] { S("""{"type":"grade_to_surface","slope":2}""") }, Array.Empty<JsonObject>(), 0.5,
            (x, y) => x is >= -1 and <= 21 && y is >= -1 and <= 21 ? 100.0 : null));

    [Theory]
    [InlineData("""[{"type":"offset","dist":1}]""", true, null)]
    [InlineData("""[{"type":"grade_to_surface","cut_slope":1,"fill_slope":2}]""", true, null)]
    [InlineData("""[{"type":"grade_to_depth","depth":0.9,"slope":0.001}]""", false, null)]
    [InlineData("""[{"type":"grade_to_depth","depth":0.9}]""", true, "type must be one of")]
    [InlineData("""[{"type":"offset","dist":0}]""", true, "dist must be > 0")]
    [InlineData("""[{"type":"offset","dist":1,"slope":2}]""", true, "does not apply")]
    [InlineData("""[{"type":"grade_to_surface","slope":-1}]""", true, "must be > 0")]
    [InlineData("""[{"type":"grade_to_elevation","slope":1}]""", false, "elevation must be")]
    public void Step_validation(string json, bool outer, string? expected)
    {
        var r = GradingEngine.ValidateSteps(JsonNode.Parse(json), outer);
        if (expected == null) Assert.Null(r); else Assert.Contains(expected, r);
    }
}

public class Phase2InputTests
{
    private static JsonObject A(string json) => (JsonObject)JsonNode.Parse(json)!;

    [Theory]
    [InlineData("""{"action":"create_geometric","source":"2A3F","surface":"EG","name":"G1","outer":[{"type":"grade_to_surface","slope":2}],"target_document":"F.dwg"}""", null)]
    [InlineData("""{"action":"create_geometric","source":"2A3F","name":"G1","inner":[{"type":"grade_to_depth","depth":0.9,"slope":0.001}],"target_document":"F.dwg"}""", null)]
    [InlineData("""{"action":"create_geometric","source":"2A3F","name":"G1","outer":[{"type":"grade_to_surface","slope":2}],"target_document":"F.dwg"}""", "needs surface")]
    [InlineData("""{"action":"create_geometric","source":"2A3F","surface":"EG","name":"G1","inner":[{"type":"offset","dist":1}],"target_document":"F.dwg"}""", "only used by grade_to_surface")]
    [InlineData("""{"action":"create_geometric","source":"2A3F","name":"G1","target_document":"F.dwg"}""", "at least one")]
    [InlineData("""{"action":"create_geometric","source":"XYZ","name":"G1","inner":[{"type":"offset","dist":1}],"target_document":"F.dwg"}""", "hexadecimal")]
    [InlineData("""{"action":"create_geometric","source":"2A3F","name":"G1","inner":[{"type":"offset","dist":1}]}""", "target_document")]
    [InlineData("""{"action":"create_geometric","source":"2A3F","name":"G1","inner":[{"type":"offset","dist":1}],"densify":0.01,"target_document":"F.dwg"}""", "densify")]
    [InlineData("""{"action":"create_native","source":"2A3F","name":"G1","target_document":"F.dwg"}""", "create_geometric")]
    public void Grading_validation(string json, string? expected)
    {
        var r = GradingInputs.Validate(A(json));
        if (expected == null)
        {
            Assert.Null(r);
            Assert.Null(Horizun.Civil3D.Server.SchemaCheck.Validate(Contract.Find("horizun_c3d_grading")!.InputSchema, A(json)));
        }
        else Assert.Contains(expected, r);
    }

    [Theory]
    [InlineData("""{"action":"create_from_polyline","handles":["1A","1B"],"names":["FL1","FL2"],"site":"S1","target_document":"F.dwg"}""", null)]
    [InlineData("""{"action":"set_elevations","name":"FL1","mode":"from_surface","surface":"EG","insert_intermediate":true,"target_document":"F.dwg"}""", null)]
    [InlineData("""{"action":"set_elevations","handle":"1A","mode":"points","points":[{"index":0,"z":10}],"target_document":"F.dwg"}""", null)]
    [InlineData("""{"action":"rename","name":"FL1","new_name":"FL2","target_document":"F.dwg"}""", null)]
    [InlineData("""{"action":"export_polyline3d","name":"FL1","target_document":"F.dwg"}""", null)]
    [InlineData("""{"action":"create_from_polyline","handles":["1A","1B"],"names":["FL1"],"target_document":"F.dwg"}""", "one unique")]
    [InlineData("""{"action":"create_from_polyline","handles":["1A","1B"],"names":["FL1","fl1"],"target_document":"F.dwg"}""", "must not repeat")]
    [InlineData("""{"action":"set_elevations","name":"FL1","mode":"constant","target_document":"F.dwg"}""", "finite elevation")]
    [InlineData("""{"action":"set_elevations","name":"FL1","mode":"constant","elevation":5,"surface":"EG","target_document":"F.dwg"}""", "from_surface only")]
    [InlineData("""{"action":"set_elevations","name":"FL1","mode":"points","points":[{"index":0,"z":1},{"index":0,"z":2}],"target_document":"F.dwg"}""", "must not repeat")]
    [InlineData("""{"action":"rename","name":"FL1","handle":"1A","new_name":"X","target_document":"F.dwg"}""", "exactly one")]
    [InlineData("""{"action":"rename","name":"FL1","target_document":"F.dwg"}""", "new_name")]
    public void Feature_line_validation(string json, string? expected)
    {
        var r = FeatureLineInputs.Validate(A(json));
        if (expected == null)
        {
            Assert.Null(r);
            Assert.Null(Horizun.Civil3D.Server.SchemaCheck.Validate(Contract.Find("horizun_c3d_feature_line")!.InputSchema, A(json)));
        }
        else Assert.Contains(expected, r);
    }
}
