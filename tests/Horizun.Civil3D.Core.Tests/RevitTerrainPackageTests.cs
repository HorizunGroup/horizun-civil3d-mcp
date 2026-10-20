using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Horizun.Civil3D.Core;

namespace Horizun.Civil3D.Core.Tests;

public class RevitTerrainPackageTests
{
    private static LxSurface Tin(double east = 1_000_000, double north = 2_000_000) => new("EG", "Generated terrain",
        new() { (east, north, 100), (east + 10, north, 101), (east + 10, north + 10, 102), (east, north + 10, 101) },
        new() { (0, 1, 2), (0, 2, 3) });

    private static RevitTerrainPackage.Prepared Prepare(LxSurface? tin = null, string unit = "meter") =>
        RevitTerrainPackage.Prepare(tin ?? Tin(), unit, new JsonObject { ["drawing"] = "fixture.dwg" },
            new JsonObject { ["code"] = null }, "test");

    [Fact]
    public void Obj_preserves_indices_of_hole_without_filling_or_translating_survey_coordinates()
    {
        var ring = new LxSurface("Ring", "", new() {
            (1000000, 2000000, 10), (1000003, 2000000, 11), (1000003, 2000003, 12), (1000000, 2000003, 13),
            (1000001, 2000001, 10), (1000002, 2000001, 11), (1000002, 2000002, 12), (1000001, 2000002, 13) },
            new() { (0, 1, 5), (0, 5, 4), (1, 2, 6), (1, 6, 5), (2, 3, 7), (2, 7, 6), (3, 0, 4), (3, 4, 7) });
        var p = Prepare(ring);
        var lines = Encoding.UTF8.GetString(p.MeshObj!).Split('\n');
        Assert.Equal(8, lines.Count(l => l.StartsWith("v ", StringComparison.Ordinal)));
        Assert.Equal(new[] { "f 1 2 6", "f 1 6 5", "f 2 3 7", "f 2 7 6", "f 3 4 8", "f 3 8 7", "f 4 1 5", "f 4 5 8" },
            lines.Where(l => l.StartsWith("f ", StringComparison.Ordinal)));
        Assert.Contains("v 1000000 2000000 10", lines);
        Assert.False(Hz.Bool(p.Summary, "convex_coverage"));
        Assert.Equal(8, Hz.Num(p.Summary, "plan_area_m2"));
        Assert.Equal(RevitTerrainPackage.Hash(p.MeshObj!), p.Summary["exact_mesh"]!["sha256"]!.GetValue<string>());
        Assert.False(p.Summary["exact_mesh"]!["native_revit_receiver_available"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData("foot", 0.3048)]
    [InlineData("USSurveyFoot", 1200d / 3937d)]
    public void Obj_has_explicit_metric_conversion_with_original_face_winding(string unit, double factor)
    {
        var p = Prepare(Tin(0, 0), unit);
        var lines = Encoding.UTF8.GetString(p.MeshObj!).Split('\n');
        var xyz = lines.First(l => l.StartsWith("v ", StringComparison.Ordinal)).Split(' ');
        Assert.Equal(100 * factor, double.Parse(xyz[3], System.Globalization.CultureInfo.InvariantCulture));
        Assert.Contains("f 1 2 3", lines);
        Assert.Equal("meter", p.Summary["exact_mesh"]!["linear_unit"]!.GetValue<string>());
    }

    [Fact]
    public void Mesh_payload_tampering_is_detected_independently_from_LandXml()
    {
        var folder = TestDirectories.CreateTempSubdirectory("hz-terrain-mesh-");
        try
        {
            var path = Path.Combine(folder.FullName, "terrain.zip"); var p = Prepare();
            RevitTerrainPackage.Write(path, p);
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
            {
                zip.GetEntry("terrain.obj")!.Delete();
                using var writer = new StreamWriter(zip.CreateEntry("terrain.obj").Open());
                writer.Write("v 0 0 0\nf 1 1 1\n");
            }
            Assert.False(RevitTerrainPackage.Matches(path, p));
        }
        finally { folder.Delete(true); }
    }

    [Theory]
    [InlineData("meter", 1d)]
    [InlineData("foot", 0.3048)]
    [InlineData("USSurveyFoot", 0.3048006096012192)]
    public void Preserves_NEZ_order_faces_and_explicit_physical_units(string unit, double factor)
    {
        var p = Prepare(unit: unit);
        XNamespace ns = LandXmlWriter.Ns;
        var xml = XDocument.Parse(Encoding.UTF8.GetString(p.LandXml));
        Assert.Equal("2000000 1000000 100", xml.Descendants(ns + "P").First().Value);
        Assert.Equal(new[] { "1 2 3", "1 3 4" }, xml.Descendants(ns + "F").Select(f => f.Value));
        Assert.Equal(unit, xml.Descendants().First(e => e.Name.LocalName is "Metric" or "Imperial").Attribute("linearUnit")!.Value);
        Assert.Equal(100 * factor * factor, Hz.Num(p.Summary, "plan_area_m2")!.Value, 9);
        Assert.Equal(100 * factor, p.Summary["elevation_m"]!["min"]!.GetValue<double>(), 9);
        Assert.Equal(101 * factor, p.Summary["elevation_m"]!["mean_of_vertices"]!.GetValue<double>(), 9);
        Assert.Equal(3, p.Summary["placement_controls"]!.AsArray().Count);
        Assert.True(Hz.Bool(p.Summary, "convex_coverage"));
        Assert.False(Hz.Bool(p.Summary, "shared_coordinates_verified"));
        Assert.False(Hz.Bool(p.Summary, "revit_import_verified"));
    }

    [Fact]
    public void Binds_every_coordinate_and_face_without_clock_dependent_stale_plans()
    {
        var before = Prepare(); var again = Prepare();
        Assert.Equal(before.LandXml, again.LandXml);
        Assert.Equal(before.Manifest, again.Manifest);
        var tin = Tin(); tin.Points[1] = (1_000_010, 2_000_000, 101.00000000000001);
        var changed = Prepare(tin);
        Assert.NotEqual(Hz.Str(before.Summary, "landxml_sha256"), Hz.Str(changed.Summary, "landxml_sha256"));
        Assert.Contains("101.00000000000001", Encoding.UTF8.GetString(changed.LandXml));
    }

    [Fact]
    public void Concave_footprint_is_retained_and_marked_for_native_import_review()
    {
        var tin = new LxSurface("L", "", new() { (0, 0, 0), (2, 0, 0), (2, 1, 0), (1, 1, 0), (1, 2, 0), (0, 2, 0) },
            new() { (0, 1, 3), (1, 2, 3), (0, 3, 5), (3, 4, 5) });
        var p = Prepare(tin);
        Assert.False(Hz.Bool(p.Summary, "convex_coverage"));
        Assert.Equal(3, Hz.Num(p.Summary, "plan_area_m2"));
        Assert.Equal(3.5, Hz.Num(p.Summary, "convex_hull_area_m2"));
        Assert.True(p.Summary["receiver"]!["nonconvex_native_import_requires_review"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("nonfinite")]
    [InlineData("conflicting_xy")]
    [InlineData("bad_index")]
    [InlineData("degenerate")]
    [InlineData("duplicate_face")]
    [InlineData("unused")]
    [InlineData("too_many")]
    public void Refuses_invalid_or_unbounded_TIN_without_thinning(string issue)
    {
        var tin = Tin();
        switch (issue)
        {
            case "empty": tin.Faces.Clear(); break;
            case "nonfinite": tin.Points[0] = (double.NaN, 0, 0); break;
            case "conflicting_xy": tin.Points[1] = (1_000_000, 2_000_000, 101); break;
            case "bad_index": tin.Faces[0] = (0, 1, 99); break;
            case "degenerate": tin.Faces[0] = (0, 0, 2); break;
            case "duplicate_face": tin.Faces[1] = (2, 1, 0); break;
            case "unused": tin.Points.Add((9, 9, 9)); break;
            case "too_many": while (tin.Points.Count <= RevitTerrainPackage.MaxPoints) tin.Points.Add((tin.Points.Count, 0, 0)); break;
        }
        Assert.Throws<ArgumentException>(() => Prepare(tin));
    }

    [Fact]
    public void Reopens_package_checks_every_byte_and_never_overwrites()
    {
        var folder = Path.Combine(Path.GetTempPath(), "hz-terrain-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "terrain.zip"); var p = Prepare();
            RevitTerrainPackage.Write(path, p);
            Assert.True(RevitTerrainPackage.Matches(path, p));
            Assert.Throws<IOException>(() => RevitTerrainPackage.Write(path, p));
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Update))
            {
                zip.GetEntry("terrain.xml")!.Delete();
                using var writer = new StreamWriter(zip.CreateEntry("terrain.xml").Open());
                writer.Write(Encoding.UTF8.GetString(p.LandXml).Replace("1000000", "1000001"));
            }
            Assert.False(RevitTerrainPackage.Matches(path, p));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Theory]
    [InlineData("C:\\fixture.zip", true)]
    [InlineData("fixture.zip", false)]
    [InlineData("C:\\fixture.xml", false)]
    public void Export_requires_named_surface_absolute_zip_and_FullWrite(string output, bool valid)
    {
        var args = new JsonObject { ["action"] = "export_revit", ["target_document"] = "fixture.dwg", ["surface"] = "EG", ["output"] = output };
        Assert.Equal(valid, ToolRules.Validate("horizun_c3d_exchange", args) == null);
        Assert.Equal(ToolEffect.FullWrite, Contract.Find("horizun_c3d_exchange")!.EffectFor(args));
        args.Remove("surface");
        Assert.NotNull(ToolRules.Validate("horizun_c3d_exchange", args));
    }
}
