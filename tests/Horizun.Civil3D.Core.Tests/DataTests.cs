using System.Text.Json.Nodes;
using Horizun.Civil3D.Core;

namespace Horizun.Civil3D.Core.Tests;

/// <summary>Block D: our own exchange formats and the pipes/points/exchange rules.</summary>
public class DataTests
{
    private static LandXmlModel Model()
    {
        var m = new LandXmlModel { AppVersion = "test" };
        m.Surfaces.Add(new LxSurface("EG", "plane", new() { (0, 0, 100), (10, 0, 100.2), (10, 10, 100.3), (0, 10, 100.1) }, new() { (0, 1, 2), (0, 2, 3) }));
        m.Alignments.Add(new LxAlignment("ROAD", "", 0, 30 + Math.PI * 5, new()
        {
            new LxElement("Line", 0, 30, (0, 0), (30, 0)),
            new LxElement("Curve", 30, Math.PI * 5, (30, 0), (30, 10), (30, 5), 5, false),
        }, new() { new LxProfile("FG", new() { new(0, 101, 0), new(20, 102, 10), new(30 + Math.PI * 5, 101.5, 0) }) }));
        return m;
    }

    [Fact]
    public void LandXml_round_trip_keeps_counts_and_lengths()
    {
        var xml = LandXmlWriter.Write(Model(), new DateTime(2026, 10, 2, 3, 0, 0));
        var s = LandXmlSummary.Read(xml);
        Assert.Equal((4, 2), (s.Surfaces["EG"].Points, s.Surfaces["EG"].Faces));
        Assert.Equal(100.0, s.Surfaces["EG"].MinZ, 9);
        Assert.Equal(100.3, s.Surfaces["EG"].MaxZ, 9);
        var a = s.Alignments["ROAD"];
        Assert.Equal(30 + Math.PI * 5, a.Length, 6);
        Assert.Equal(a.Length, a.ElementLength, 6);
        Assert.Equal(2, a.Elements);
        Assert.Equal(3, a.ProfilePvis["FG"]);
    }

    [Fact]
    public void LandXml_uses_northing_easting_order_and_one_based_faces()
    {
        var xml = LandXmlWriter.Write(Model(), DateTime.Now);
        Assert.Contains("<P id=\"2\">0 10 100.2</P>", xml);
        Assert.Contains("<F>1 2 3</F>", xml);
        Assert.Contains("<Start>0 30</Start>", xml);
        Assert.Contains("rot=\"ccw\"", xml);
        Assert.Contains("linearUnit=\"meter\"", xml);
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"UTF-8\"?>", xml);
    }

    [Fact]
    public void LandXml_reader_rejects_faces_pointing_to_missing_points()
    {
        var m = Model();
        m.Surfaces[0].Faces.Add((0, 1, 9));
        Assert.Throws<FormatException>(() => LandXmlSummary.Read(LandXmlWriter.Write(m, DateTime.Now)));
    }

    [Theory]
    [InlineData("1,1000.5,2000.25,100.1,TREE OAK\n2,1001,2001,100.2,POST", "PNEZD", 2)]
    [InlineData("1;2000.25;1000.5;100.1;TREE\n", "PENZD", 1)]
    [InlineData("1000.5 2000.25 100.1\n1001 2001 100.2\n", "NEZ", 2)]
    [InlineData("P,N,E,Z,D\n1,1,2,3,X\n", "PNEZD", 1)]
    public void Point_files_parse(string text, string format, int rows)
    {
        var r = PointFile.Parse(text, format, text.StartsWith("P,"), out var errors);
        Assert.Empty(errors);
        Assert.Equal(rows, r.Count);
    }

    [Fact]
    public void Point_file_maps_northing_to_y_and_reports_bad_lines()
    {
        var r = PointFile.Parse("7,2000,1000,99.5,MH 1\nX,1,2,3,bad\n", "PNEZD", false, out var errors);
        Assert.Single(r);
        Assert.Equal((7u, 1000.0, 2000.0, 99.5, "MH 1"), (r[0].Number!.Value, r[0].X, r[0].Y, r[0].Z, r[0].Description));
        Assert.Single(errors);
    }

    [Fact]
    public void Point_file_write_then_parse_round_trips_descriptions_with_commas()
    {
        var rows = new List<PointFile.Row> { new(1, 1000.12345, 2000.5, 100, "TREE, OAK"), new(2, 1, 2, 3, "") };
        var back = PointFile.Parse(PointFile.Write(rows, "PNEZD"), "PNEZD", false, out var errors);
        Assert.Empty(errors);
        Assert.Equal("TREE, OAK", back[0].Description);
        Assert.Equal(1000.1235, back[0].X, 9);
    }

    [Theory]
    [InlineData("1-100,205,300-310", true)]
    [InlineData("5", true)]
    [InlineData("10-5", false)]
    [InlineData("a-b", false)]
    [InlineData("", false)]
    public void Number_sets(string text, bool ok) => Assert.Equal(ok, NumberSet.Parse(text) != null);

    [Fact]
    public void Number_set_membership()
    {
        var s = NumberSet.Parse("1-100,205")!;
        Assert.True(NumberSet.Contains(s, 205));
        Assert.True(NumberSet.Contains(s, 1));
        Assert.False(NumberSet.Contains(s, 101));
    }

    private static string? V(string tool, string json) => ToolRules.Validate(tool, JsonNode.Parse(json)!.AsObject());
    private const string T = "\"target_document\":\"d\",";

    [Theory]
    [InlineData("{\"action\":\"add_pipes\"," + T + "\"network\":\"N\",\"pipes\":[{\"from\":\"A\",\"to\":\"B\",\"family\":\"PVC\",\"size\":\"300\",\"start_invert\":98,\"slope_pct\":1}]}", true)]
    [InlineData("{\"action\":\"add_pipes\"," + T + "\"network\":\"N\",\"pipes\":[{\"from\":\"A\",\"to\":\"B\",\"family\":\"PVC\",\"size\":\"300\",\"start_invert\":98}]}", false)]
    [InlineData("{\"action\":\"add_pipes\"," + T + "\"network\":\"N\",\"pipes\":[{\"from\":\"A\",\"to\":\"B\",\"family\":\"PVC\",\"size\":\"300\",\"start_invert\":98,\"end_invert\":97,\"slope_pct\":1}]}", false)]
    [InlineData("{\"action\":\"add_pipes\"," + T + "\"network\":\"N\",\"pipes\":[{\"from\":\"A\",\"to\":\"a\",\"family\":\"PVC\",\"size\":\"300\",\"start_invert\":98,\"end_invert\":97}]}", false)]
    [InlineData("{\"action\":\"add_structures\"," + T + "\"network\":\"N\",\"structures\":[{\"name\":\"A\",\"position\":{\"x\":0,\"y\":0},\"family\":\"MH\",\"size\":\"1200\"},{\"name\":\"a\",\"position\":{\"x\":1,\"y\":0},\"family\":\"MH\",\"size\":\"1200\"}]}", false)]
    [InlineData("{\"action\":\"add_structures\"," + T + "\"network\":\"N\",\"structures\":[{\"position\":{\"x\":0,\"y\":0},\"family\":\"MH\"}]}", false)]
    [InlineData("{\"action\":\"validate\",\"network\":\"N\",\"min_cover\":1.2,\"min_slope_pct\":0.5}", true)]
    public void Pipe_rules(string json, bool ok) => Assert.Equal(ok, V("horizun_c3d_pipes", json) == null);

    [Theory]
    [InlineData("{\"action\":\"create\"," + T + "\"points\":[{\"x\":1,\"y\":2,\"z\":3,\"description\":\"TREE\",\"number\":10}]}", true)]
    [InlineData("{\"action\":\"create\"," + T + "\"points\":[{\"x\":1,\"y\":2,\"number\":0}]}", false)]
    [InlineData("{\"action\":\"create\"," + T + "\"points\":[{\"x\":1,\"y\":2,\"code\":\"X\"}]}", false)]
    [InlineData("{\"action\":\"import\"," + T + "\"file\":\"C:\\\\p.csv\",\"format\":\"PNEZD\"}", true)]
    [InlineData("{\"action\":\"import\"," + T + "\"file\":\"p.csv\",\"format\":\"PNEZD\"}", false)]
    [InlineData("{\"action\":\"import\"," + T + "\"file\":\"C:\\\\p.csv\",\"format\":\"XYZ\"}", false)]
    [InlineData("{\"action\":\"group_create\"," + T + "\"new_name\":\"G\"}", false)]
    [InlineData("{\"action\":\"group_create\"," + T + "\"new_name\":\"G\",\"include_numbers\":\"1-x\"}", false)]
    [InlineData("{\"action\":\"group_create\"," + T + "\"new_name\":\"G\",\"include_raw_descriptions\":\"TREE*\"}", true)]
    [InlineData("{\"action\":\"elevations_from_surface\"," + T + "\"surface\":\"EG\"}", false)]
    public void Point_rules(string json, bool ok) => Assert.Equal(ok, V("horizun_c3d_points", json) == null);

    [Theory]
    [InlineData("{\"action\":\"export_landxml\"," + T + "\"output\":\"C:\\\\x.xml\",\"surfaces\":[\"EG\"]}", true)]
    [InlineData("{\"action\":\"export_landxml\"," + T + "\"output\":\"C:\\\\x.xml\"}", false)]
    [InlineData("{\"action\":\"export_landxml\"," + T + "\"output\":\"x.xml\",\"surfaces\":[\"EG\"]}", false)]
    [InlineData("{\"action\":\"shortcuts_reference\"," + T + "\"name\":\"EG\",\"type\":\"Surface\"}", true)]
    [InlineData("{\"action\":\"shortcuts_reference\"," + T + "\"name\":\"EG\",\"type\":\"Parcel\"}", false)]
    public void Exchange_rules(string json, bool ok) => Assert.Equal(ok, V("horizun_c3d_exchange", json) == null);

    [Fact]
    public void Publishing_shortcuts_and_erasing_points_need_full_write()
    {
        Assert.Equal(ToolEffect.FullWrite, Contract.Find("horizun_c3d_exchange")!.EffectFor(new JsonObject { ["action"] = "shortcuts_publish" }));
        Assert.Equal(ToolEffect.FullWrite, Contract.Find("horizun_c3d_points")!.EffectFor(new JsonObject { ["action"] = "erase" }));
        Assert.Equal(ToolEffect.SafeWrite, Contract.Find("horizun_c3d_exchange")!.EffectFor(new JsonObject { ["action"] = "export_landxml" }));
    }

    [Fact]
    public void Undo_last_is_a_safe_write_and_info_stays_a_read()
    {
        var doc = Contract.Find("horizun_c3d_document")!;
        Assert.Equal(ToolEffect.SafeWrite, doc.EffectFor(new JsonObject { ["action"] = "undo_last" }));
        Assert.Equal(ToolEffect.FullWrite, doc.EffectFor(new JsonObject { ["action"] = "save" }));
        Assert.Equal(ToolEffect.Read, doc.EffectFor(new JsonObject { ["action"] = "info" }));
        Assert.Null(Settings.Parse("{}").Refusal("horizun_c3d_document", ToolEffect.SafeWrite));
    }

    [Fact]
    public void Catalogue_stays_under_30_tools() => Assert.True(Contract.All.Count < 30, "tools: " + Contract.All.Count);
}
