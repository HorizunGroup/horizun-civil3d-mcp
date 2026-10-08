using System.Text.Json.Nodes;
using Horizun.Civil3D.Core;

namespace Horizun.Civil3D.Core.Tests;

/// <summary>
/// Live bug (v0.8.0, "Primavera 2"): a 4-vertex flat base graded to the surface came out TILTED in Civil 3D
/// (389 of 2153 base samples at 42.50, the rest down to 40.51) because no TIN vertex lay inside the base and the
/// daylight almost touched it. The floor fill puts vertices on the base plane and re-reads off-vertex points.
/// </summary>
public class GradingFloorTests
{
    private static List<P3> Rect(double w, double h, double z) => new() { new(0, 0, z), new(w, 0, z), new(w, h, z), new(0, h, z) };

    private static JsonObject S(string json) => (JsonObject)JsonNode.Parse(json)!;

    [Fact]
    public void Flat_base_whose_daylight_touches_it_gets_interior_vertices_at_grade()
    {
        // Base at 42.50; terrain rises from 42.49 at the south edge (daylight there almost ON the base, like T5)
        // to well above it on the north side.
        Func<double, double, double?> ground = (_, y) => 42.49 + 0.4 * Math.Max(0, y);
        var r = GradingEngine.Run(Rect(40, 20, 42.5), new[] { S("""{"type":"grade_to_surface","slope":1}""") }, Array.Empty<JsonObject>(), 0.5, ground);
        var f = r.Floor!;
        Assert.Equal("base", f.Tag);
        Assert.True(f.Planar);
        Assert.NotEmpty(f.Vertices);
        Assert.NotEmpty(f.Checks);
        var basePoly = r.Lines[0].Points;
        Assert.All(f.Vertices, v =>
        {
            Assert.Equal(42.5, v.Z, 9);
            Assert.True(GradingEngine.Inside(v.X, v.Y, basePoly));
            Assert.True(GradingEngine.DistanceToBoundary(v, basePoly) >= f.Spacing / 2 - 1e-9);
        });
        Assert.All(f.Checks, c => Assert.Equal(42.5, c.Z, 9));
        // Checks are not TIN vertices: they test the triangles, not the inserted points.
        Assert.DoesNotContain(f.Checks, c => f.Vertices.Any(v => v.Dist2D(c) < 1e-6));
        // The daylight line really does come within centimetres of the base on the south side.
        var day = r.Lines.Last().Points;
        Assert.Contains(day, p => GradingEngine.DistanceToBoundary(p, basePoly) < 0.05);
    }

    [Fact]
    public void Sloped_planar_base_follows_its_plane()
    {
        var b = new List<P3> { new(0, 0, 10), new(30, 0, 13), new(30, 10, 14), new(0, 10, 11) }; // z = 10 + 0.1x + 0.1y
        var f = GradingEngine.Floor(new GradingLine("base", "base", b), 0.5);
        Assert.True(f.Planar);
        Assert.All(f.Vertices, v => Assert.Equal(10 + 0.1 * v.X + 0.1 * v.Y, v.Z, 6));
    }

    [Fact]
    public void Non_planar_base_adds_nothing_and_says_so()
    {
        var b = new List<P3> { new(0, 0, 10), new(30, 0, 10), new(30, 10, 12), new(0, 10, 10) };
        var f = GradingEngine.Floor(new GradingLine("base", "base", b), 0.5);
        Assert.False(f.Planar);
        Assert.Empty(f.Vertices);
        Assert.Empty(f.Checks);
        Assert.Contains("Not planar", f.Note);
    }

    [Fact]
    public void With_inner_steps_the_floor_is_the_innermost_line()
    {
        var r = GradingEngine.Run(Rect(40, 40, 100), Array.Empty<JsonObject>(),
            new[] { S("""{"type":"grade_to_elevation","elevation":97,"slope":2}""") }, 0.5, null);
        Assert.Equal(r.Lines.Last().Tag, r.Floor!.Tag);
        Assert.All(r.Floor.Vertices, v => Assert.Equal(97, v.Z, 9));
        var inner = r.Lines.Last().Points;
        Assert.All(r.Floor.Vertices, v => Assert.True(GradingEngine.Inside(v.X, v.Y, inner)));
    }

    [Fact]
    public void Large_floor_stays_under_the_vertex_budget_and_narrow_floor_is_not_left_empty()
    {
        var big = GradingEngine.Floor(new GradingLine("base", "base", Rect(1000, 1000, 5)), 0.5);
        Assert.InRange(big.Vertices.Count, 1, GradingEngine.MaxFloorVertices);
        var narrow = GradingEngine.Floor(new GradingLine("base", "base", Rect(50, 0.8, 5)), 0.5);
        Assert.NotEmpty(narrow.Vertices);
    }

    [Fact]
    public void Floor_summary_serializes_without_reflection()
    {
        var f = GradingEngine.Floor(new GradingLine("base", "base", Rect(10, 10, 1)), 0.5);
        var json = new JsonObject { ["floor"] = f.Summary() }.ToJsonString(Hz.Compact);
        Assert.Contains("\"planar\":true", json);
    }
}
