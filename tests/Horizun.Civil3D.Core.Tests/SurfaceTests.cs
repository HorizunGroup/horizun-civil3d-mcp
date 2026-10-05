using System.Text.Json.Nodes;
using Horizun.Civil3D.Core;
using Horizun.Civil3D.Server;

namespace Horizun.Civil3D.Core.Tests;

public class SurfaceInputTests
{
    private static JsonObject Parse(string json)=>(JsonObject)JsonNode.Parse(json)!;
    [Theory]
    [InlineData("{\"action\":\"list\"}")]
    [InlineData("{\"action\":\"get\",\"names\":[\"EG\",\"FG\"]}")]
    [InlineData("{\"action\":\"volumes_report\",\"base\":\"EG\",\"comparison\":\"FG\",\"cut_factor\":1.2}")]
    [InlineData("{\"action\":\"volumes_report\",\"handle\":\"AB\"}")]
    [InlineData("{\"action\":\"sample_elevation\",\"name\":\"EG\",\"points\":[{\"x\":0,\"y\":0}]}")]
    [InlineData("{\"action\":\"sample_elevation\",\"name\":\"EG\",\"line\":{\"start\":{\"x\":0,\"y\":0},\"end\":{\"x\":3,\"y\":4}},\"step\":2}")]
    [InlineData("{\"action\":\"rename\",\"name\":\"EG\",\"new_name\":\"EG_ANT\",\"target_document\":\"Fixture.dwg\"}")]
    [InlineData("{\"action\":\"set_style\",\"names\":[\"EG\",\"FG\"],\"style\":\"Contours\",\"target_document\":\"Fixture.dwg\"}")]
    [InlineData("{\"action\":\"duplicate_style\",\"style\":\"Contours\",\"new_name\":\"Contours_Copy\",\"target_document\":\"Fixture.dwg\"}")]
    [InlineData("{\"action\":\"create_tin\",\"style\":\"Contours\",\"new_name\":\"EG\",\"target_document\":\"Fixture.dwg\"}")]
    [InlineData("{\"action\":\"create_volume\",\"base\":\"EG\",\"comparison\":\"FG\",\"style\":\"Volumes\",\"new_name\":\"EG_FG\",\"target_document\":\"Fixture.dwg\"}")]
    [InlineData("{\"action\":\"rebuild\",\"name\":\"EG\",\"target_document\":\"Fixture.dwg\",\"dry_run\":false,\"confirmation_token\":\"token\"}")]
    public void Complete_requests_pass_both_schema_and_semantics(string json)
    {
        var args=Parse(json);
        Assert.Null(SchemaCheck.Validate(Contract.Find("horizun_c3d_surface")!.InputSchema,args));
        Assert.Null(SurfaceInputs.Validate(args));
    }
    [Theory]
    [InlineData("{\"action\":\"get\"}")]
    [InlineData("{\"action\":\"get\",\"name\":\"EG\",\"handle\":\"A\"}")]
    [InlineData("{\"action\":\"get\",\"names\":[]}")]
    [InlineData("{\"action\":\"get\",\"names\":[\"EG\",\"eg\"]}")]
    [InlineData("{\"action\":\"get\",\"names\":[\"EG\",\" \"]}")]
    [InlineData("{\"action\":\"volumes_report\",\"base\":\"EG\"}")]
    [InlineData("{\"action\":\"volumes_report\",\"name\":\"VOL\",\"base\":\"EG\",\"comparison\":\"FG\"}")]
    [InlineData("{\"action\":\"volumes_report\",\"base\":\"EG\",\"comparison\":\"eg\"}")]
    [InlineData("{\"action\":\"rename\",\"name\":\"EG\",\"new_name\":\"EG_ANT\"}")]
    [InlineData("{\"action\":\"rename\",\"names\":[\"EG\",\"FG\"],\"new_name\":\"NEW\",\"target_document\":\"Fixture.dwg\"}")]
    [InlineData("{\"action\":\"set_style\",\"name\":\"EG\",\"target_document\":\"Fixture.dwg\"}")]
    [InlineData("{\"action\":\"create_tin\",\"name\":\"EG\",\"new_name\":\"NEW\",\"style\":\"Contours\",\"target_document\":\"Fixture.dwg\"}")]
    [InlineData("{\"action\":\"get\",\"name\":\"EG\",\"grid_spacing\":0}")]
    [InlineData("{\"action\":\"get\",\"name\":\"EG\",\"max_samples\":0}")]
    [InlineData("{\"action\":\"volumes_report\",\"name\":\"VOL\",\"cut_factor\":-1}")]
    [InlineData("{\"action\":\"sample_elevation\",\"name\":\"EG\",\"points\":[]}")]
    [InlineData("{\"action\":\"sample_elevation\",\"name\":\"EG\",\"points\":[{\"x\":0}]}")]
    [InlineData("{\"action\":\"sample_elevation\",\"name\":\"EG\",\"line\":{\"start\":{\"x\":0,\"y\":0},\"end\":{\"x\":3,\"y\":4}}}")]
    [InlineData("{\"action\":\"get\",\"name\":\"EG\",\"dry_run\":false}")]
    [InlineData("{\"action\":\"rename\",\"name\":\"EG\",\"new_name\":\"NEW\",\"target_document\":\"Fixture.dwg\",\"cut_factor\":2}")]
    [InlineData("{\"action\":\"get\",\"name\":12}")]
    public void Incomplete_ambiguous_or_irrelevant_requests_are_refused(string json)=>Assert.NotNull(SurfaceInputs.Validate(Parse(json)));

    [Fact]
    public void Excessive_line_and_batch_points_are_refused_before_dispatch()
    {
        var line=Parse("{\"action\":\"sample_elevation\",\"name\":\"EG\",\"line\":{\"start\":{\"x\":0,\"y\":0},\"end\":{\"x\":10000,\"y\":0}},\"step\":0.001}");
        Assert.Contains("10000",SurfaceInputs.Validate(line));
        var args=new JsonObject{["action"]="sample_elevation",["names"]=Hz.Strings(Enumerable.Range(0,100).Select(i=>"S"+i)),["points"]=Hz.Arr(Enumerable.Range(0,1001).Select(_=>(JsonNode?)new JsonObject{["x"]=0,["y"]=0}))};
        Assert.Contains("100000",SurfaceInputs.Validate(args));
    }
    [Fact]
    public void Read_only_profile_allows_reads_and_refuses_each_write()
    {
        var c=Contract.Find("horizun_c3d_surface")!;var settings=Settings.Parse("{\"permission_profile\":\"read_only\"}");
        foreach(var a in SurfaceInputs.ReadActions)Assert.Null(settings.Refusal(c.Name,c.EffectFor(new JsonObject{["action"]=a})));
        foreach(var a in SurfaceInputs.WriteActions)Assert.NotNull(settings.Refusal(c.Name,c.EffectFor(new JsonObject{["action"]=a})));
    }
}

public class SurfaceMathTests
{
    [Fact]
    public void Plane_grid_integrates_analytic_cut_fill_and_partial_edge_cells()
    {
        // z=x-2 over [0,4] x [0,3]: cut=fill=6, each area=6.
        var (cells,_)=SurfaceMath.Grid(0,0,4,3,.5,100);
        var samples=new SurfaceSamples();foreach(var c in cells)samples.Add(c.X-2,c.Area);
        var result=samples.ToJson();Assert.Equal(6,Hz.Num(result,"cut_volume_estimated"));Assert.Equal(6,Hz.Num(result,"fill_volume_estimated"));
        Assert.Equal(6,Hz.Num(result,"cut_area_estimated"));Assert.Equal(6,Hz.Num(result,"fill_area_estimated"));Assert.Equal(0,Hz.Num(result,"net_volume_estimated"));
        var (partial,_2)=SurfaceMath.Grid(0,0,2.2,1.3,1,10);Assert.Equal(2.86,partial.Sum(c=>c.Area),12);
        var flat=new SurfaceSamples();foreach(var c in partial)flat.Add(3,c.Area);
        Assert.Equal(8.58,Hz.Num(flat.ToJson(),"fill_volume_estimated")!.Value,12);
    }
    [Fact]
    public void Weighted_statistics_use_area_rather_than_vertex_average()
    {
        var samples=new SurfaceSamples();samples.Add(-1,9);samples.Add(-10,1);samples.Add(0,2);
        var result=samples.ToJson();var cut=(JsonObject)result["cut"]!;
        Assert.Equal(1.9,Hz.Num(cut,"mean"));Assert.Equal(1,Hz.Num(cut,"median"));Assert.Equal(1,Hz.Num(cut,"p90"));
        Assert.Equal(1,Hz.Num(cut,"min"));Assert.Equal(10,Hz.Num(cut,"max"));Assert.Equal(2,Hz.Num(result,"zero_area_estimated"));
        Assert.Null(result["fill"]!["mean"]);Assert.NotNull(result["fill"]!["unreadable_reason"]);
    }
    [Fact]
    public void Empty_sampling_has_null_measurements_and_reason()
    {
        var result=new SurfaceSamples().ToJson();Assert.Null(result["area_weighted_signed_mean"]);Assert.Null(result["cut"]!["min"]);Assert.NotNull(result["cut"]!["unreadable_reason"]);
        Assert.Null(result["cut_volume_estimated"]);Assert.Null(result["area_2d_estimated"]);Assert.NotNull(result["unreadable_reason"]);
    }
    [Theory]
    [InlineData(double.NaN,1)]
    [InlineData(double.PositiveInfinity,1)]
    [InlineData(1,0)]
    [InlineData(1,-1)]
    public void Unreadable_samples_never_become_zero(double z,double area)=>Assert.Throws<HzRefusal>(()=>new SurfaceSamples().Add(z,area));
    [Fact]
    public void Overflow_of_totals_is_reported_as_unreadable()
    {
        var samples=new SurfaceSamples();samples.Add(1e308,1);samples.Add(1e308,1);
        Assert.Throws<HzRefusal>(()=>samples.ToJson());
    }
    [Fact]
    public void Automatic_grid_honours_budget_and_explicit_grid_refuses_overflow()
    {
        var (cells,_)=SurfaceMath.Grid(0,0,1000,1,null,50);Assert.InRange(cells.Count,1,50);Assert.Equal(1000,cells.Sum(c=>c.Area),9);
        Assert.Throws<HzRefusal>(()=>SurfaceMath.Grid(0,0,100,100,.01,100));
        Assert.Throws<HzRefusal>(()=>SurfaceMath.Grid(0,0,0,1,null,100));
        Assert.Throws<HzRefusal>(()=>SurfaceMath.Grid(0,0,double.PositiveInfinity,1,null,100));
    }
    [Fact]
    public void Line_sampling_includes_both_ends_and_preserves_WCS_coordinates()
    {
        var line=(JsonObject)JsonNode.Parse("{\"start\":{\"x\":100,\"y\":200},\"end\":{\"x\":103,\"y\":204}}")!;
        var samples=SurfaceMath.AlongLine(line,2);Assert.Equal(4,samples.Count);Assert.Equal(new(100,200),samples[0]);Assert.Equal(new SurfaceXY(103,204),samples[^1]);
        Assert.Equal(101.2,samples[1].X,12);Assert.Equal(201.6,samples[1].Y,12);
        Assert.Equal(6,SurfaceMath.AlongLine(line,1).Count);
        line["end"]=line["start"]!.DeepClone();Assert.Single(SurfaceMath.AlongLine(line,1));
    }
    [Fact]
    public void Surface_plan_and_revision_changes_refuse_confirmation()
    {
        var store=new ConfirmationStore();var revision=new RevisionClock();
        var args=new JsonObject{["action"]="rename",["name"]="EG",["new_name"]="EG_ANT",["target_document"]="Fixture.dwg"};
        var plan=new JsonObject{["drawing_revision"]=revision.Snapshot,["handle"]="ABC",["name_before"]="EG",["name_after"]="EG_ANT"};
        var (token,_)=store.Issue("horizun_c3d_surface:rename","fixture",ConfirmationStore.RequestHash(args),ConfirmationStore.PlanFingerprint(plan));
        revision.Advance();plan["drawing_revision"]=revision.Snapshot;
        var check=store.Validate(token,"horizun_c3d_surface:rename","fixture",ConfirmationStore.RequestHash(args),ConfirmationStore.PlanFingerprint(plan));
        Assert.Equal(ConfirmationState.StalePlan,check.State);
    }
}

[Collection("pipe")]
public class SurfaceServerTests
{
    [Theory]
    [InlineData("{\"action\":\"rename\",\"name\":\"EG\",\"new_name\":\"NEW\"}")]
    [InlineData("{\"action\":\"sample_elevation\",\"name\":\"EG\",\"points\":[]}")]
    [InlineData("{\"action\":\"volumes_report\",\"base\":\"EG\"}")]
    public void Semantic_errors_are_returned_before_discovery_or_pipe_access(string json)
    {
        var server=new McpServer("test");var result=server.ToolsCall("surface-invalid",new JsonObject{["name"]="horizun_c3d_surface",["arguments"]=JsonNode.Parse(json)});
        Assert.True(result["isError"]!.GetValue<bool>());Assert.Equal(ErrorCodes.InvalidInput,Hz.Str((JsonObject)result["structuredContent"]!,"code"));
    }
    [Fact]
    public void Surface_tool_is_advertised_with_write_hint_and_all_actions()
    {
        var tool=((JsonArray)new McpServer("test").ToolsList()["tools"]!).First(n=>Hz.Str((JsonObject)n!,"name")=="horizun_c3d_surface")!;
        Assert.False(tool["annotations"]!["readOnlyHint"]!.GetValue<bool>());
        Assert.Equal(SurfaceInputs.ReadActions.Length+SurfaceInputs.WriteActions.Length,((JsonArray)tool["inputSchema"]!["properties"]!["action"]!["enum"]!).Count);
    }
}

public class SurfaceGeometryInputTests
{
    private static JsonObject A(string json) => (JsonObject)JsonNode.Parse(json)!;
    private static string? V(string json) => SurfaceInputs.Validate(A(json));

    [Fact]
    public void Add_data_and_paste_are_safe_writes()
    {
        var c = Contract.Find("horizun_c3d_surface")!;
        Assert.Equal(ToolEffect.SafeWrite, c.EffectFor(A("""{"action":"add_data"}""")));
        Assert.Equal(ToolEffect.SafeWrite, c.EffectFor(A("""{"action":"paste"}""")));
    }

    [Fact]
    public void Valid_add_data_requests_pass()
    {
        Assert.Null(V("""{"action":"add_data","name":"EG","target_document":"F.dwg","vertices":[{"x":0,"y":0,"z":1}]}"""));
        Assert.Null(V("""{"action":"add_data","handle":"2A3F","target_document":"F.dwg","breaklines":{"handles":["1A","1B"],"kind":"standard","weeding_distance":0}}"""));
        Assert.Null(V("""{"action":"add_data","name":"EG","target_document":"F.dwg","boundaries":{"handles":["1C"],"kind":"outer","name":"Limit","non_destructive":true},"rebuild":false}"""));
    }

    [Theory]
    [InlineData("""{"action":"add_data","name":"EG","target_document":"F.dwg"}""", "at least one")]
    [InlineData("""{"action":"add_data","name":"EG","vertices":[{"x":0,"y":0,"z":1}]}""", "target_document")]
    [InlineData("""{"action":"add_data","names":["EG"],"target_document":"F.dwg","vertices":[{"x":0,"y":0,"z":1}]}""", "not used by action")]
    [InlineData("""{"action":"add_data","name":"EG","target_document":"F.dwg","vertices":[{"x":0,"y":0}]}""", "x, y and z")]
    [InlineData("""{"action":"add_data","name":"EG","target_document":"F.dwg","vertices":[{"x":0,"y":0,"z":1,"w":2}]}""", "x, y and z")]
    [InlineData("""{"action":"add_data","name":"EG","target_document":"F.dwg","breaklines":{"handles":["ZZ"]}}""", "hexadecimal")]
    [InlineData("""{"action":"add_data","name":"EG","target_document":"F.dwg","breaklines":{"handles":["1A","1a"]}}""", "must not repeat")]
    [InlineData("""{"action":"add_data","name":"EG","target_document":"F.dwg","breaklines":{"handles":["1A"],"kind":"proximity","weeding_angle":5}}""", "kind=standard only")]
    [InlineData("""{"action":"add_data","name":"EG","target_document":"F.dwg","breaklines":{"handles":["1A"],"mid_ordinate":0}}""", "> 0")]
    [InlineData("""{"action":"add_data","name":"EG","target_document":"F.dwg","breaklines":{"handles":["1A"],"color":"red"}}""", "not a recognised field")]
    [InlineData("""{"action":"add_data","name":"EG","target_document":"F.dwg","boundaries":{"handles":["1A"]}}""", "boundaries.kind is required")]
    [InlineData("""{"action":"add_data","name":"EG","target_document":"F.dwg","boundaries":{"handles":["1A"],"kind":"outer","non_destructive":"yes"}}""", "true or false")]
    public void Bad_add_data_requests_are_refused(string json, string expected) => Assert.Contains(expected, V(json));

    [Fact]
    public void Paste_rules()
    {
        Assert.Null(V("""{"action":"paste","name":"TARGET","target_document":"F.dwg","sources":["EG","FG"]}"""));
        Assert.Contains("1 to 50", V("""{"action":"paste","name":"TARGET","target_document":"F.dwg"}"""));
        Assert.Contains("duplicates", V("""{"action":"paste","name":"TARGET","target_document":"F.dwg","sources":["EG","eg"]}"""));
        Assert.Contains("into itself", V("""{"action":"paste","name":"EG","target_document":"F.dwg","sources":["EG"]}"""));
        Assert.Contains("not used by action", V("""{"action":"paste","name":"T","target_document":"F.dwg","sources":["EG"],"vertices":[{"x":0,"y":0,"z":0}]}"""));
    }

    [Fact]
    public void Server_schema_accepts_the_new_fields()
    {
        var schema = Contract.Find("horizun_c3d_surface")!.InputSchema;
        Assert.Null(Horizun.Civil3D.Server.SchemaCheck.Validate(schema, A("""{"action":"add_data","name":"EG","target_document":"F.dwg","vertices":[{"x":0,"y":0,"z":1}],"breaklines":{"handles":["1A"]},"boundaries":{"handles":["1B"],"kind":"hide"}}""")));
        Assert.Null(Horizun.Civil3D.Server.SchemaCheck.Validate(schema, A("""{"action":"paste","name":"T","target_document":"F.dwg","sources":["EG"],"rebuild":true}""")));
        Assert.NotNull(Horizun.Civil3D.Server.SchemaCheck.Validate(schema, A("""{"action":"add_data","name":"EG","target_document":"F.dwg","boundaries":{"handles":["1B"],"kind":"window"}}""")));
    }
}

public class SurfaceAnalysisMathTests
{
    private static JsonObject A(string json) => (JsonObject)JsonNode.Parse(json)!;

    [Fact]
    public void Equal_splits_exactly()
    {
        var r = SurfaceAnalysisMath.Equal(100, 103, 3);
        Assert.Equal(3, r.Count);
        Assert.Equal((100.0, 101.0), r[0]);
        Assert.Equal(103.0, r[2].Max);
    }

    [Fact]
    public void Step_puts_a_boundary_on_break_at_so_cut_and_fill_never_share_a_band()
    {
        var r = SurfaceAnalysisMath.Step(-2, 1, 0.5, 0);
        Assert.Equal(6, r.Count);
        Assert.Equal(-2.0, r[0].Min);
        Assert.Equal(1.0, r[^1].Max);
        Assert.Contains(r, b => Math.Abs(b.Max) < 1e-12);
        Assert.DoesNotContain(r, b => b.Min < 0 && b.Max > 0);
        var odd = SurfaceAnalysisMath.Step(-1.3, 0.7, 1, 0);
        Assert.Equal((-2.0, -1.0), odd[0]);
        Assert.Equal((0.0, 1.0), odd[^1]);
    }

    [Fact]
    public void Step_refuses_too_many_bands() =>
        Assert.Throws<HzRefusal>(() => SurfaceAnalysisMath.Step(0, 1000, 1, 0));

    [Fact]
    public void Colors_parse_aci_and_rgb_only()
    {
        Assert.Equal(HzColor.FromAci(170), HzColor.Parse(JsonValue.Create(170)));
        Assert.Equal(HzColor.FromRgb(255, 0, 204), HzColor.Parse(JsonValue.Create("#FF00CC")));
        Assert.Null(HzColor.Parse(JsonValue.Create(0)));
        Assert.Null(HzColor.Parse(JsonValue.Create(256)));
        Assert.Null(HzColor.Parse(JsonValue.Create("red")));
        Assert.Null(HzColor.Parse(JsonValue.Create("#FF00C")));
        Assert.Equal("#FF00CC", HzColor.FromRgb(255, 0, 204).ToString());
    }

    [Fact]
    public void Palettes_have_one_colour_per_band()
    {
        var spans = SurfaceAnalysisMath.Step(-2, 1, 0.5, 0);
        foreach (var scheme in SurfaceAnalysisMath.Schemes)
            Assert.Equal(spans.Count, SurfaceAnalysisMath.Palette(scheme, spans, 0).Count);
    }

    [Fact]
    public void Bands_sum_area_and_signed_volume_and_count_outside()
    {
        var c = HzColor.FromAci(1);
        var bands = new List<AnalysisRange> { new(-2, 0, c), new(0, 1, c) };
        var samples = new List<(double, double)> { (-1.5, 2), (-0.5, 2), (0.5, 4), (1.0, 1), (5, 3) };
        var o = SurfaceAnalysisMath.Bands(bands, samples, withVolume: true, "m");
        var rows = (JsonArray)o["bands"]!;
        Assert.Equal(4, rows[0]!["area_estimated"]!.GetValue<double>());
        Assert.Equal(-4, rows[0]!["volume_estimated"]!.GetValue<double>());
        Assert.Equal(5, rows[1]!["area_estimated"]!.GetValue<double>()); // max of the LAST band is inclusive
        Assert.Equal(3, o["area_outside_all_bands"]!.GetValue<double>());
    }

    [Fact]
    public void No_samples_means_unknown_not_zero()
    {
        var o = SurfaceAnalysisMath.Bands(new List<AnalysisRange> { new(0, 1, HzColor.FromAci(1)) }, new List<(double, double)>(), false, "m");
        Assert.Null(((JsonArray)o["bands"]!)[0]!["area_estimated"]);
        Assert.NotNull(o["unreadable_reason"]);
    }

    [Theory]
    [InlineData("""{"action":"apply_elevation_analysis","name":"V","target_document":"F.dwg","mode":"step","interval":0.5,"break_at":0,"color_scheme":"cutfill"}""", null)]
    [InlineData("""{"action":"apply_slope_analysis","names":["EG"],"target_document":"F.dwg","mode":"ranges","ranges":[{"min":0,"max":2,"color":"#00FF00"},{"min":2,"max":5,"color":2}]}""", null)]
    [InlineData("""{"action":"apply_elevation_analysis","name":"V","target_document":"F.dwg","mode":"equal","number_of_ranges":3,"colors":[1,2,3]}""", null)]
    [InlineData("""{"action":"apply_elevation_analysis","name":"V","target_document":"F.dwg","mode":"recolor","colors":[1,2]}""", null)]
    [InlineData("""{"action":"apply_elevation_analysis","name":"V","target_document":"F.dwg"}""", "mode is required")]
    [InlineData("""{"action":"apply_elevation_analysis","name":"V","target_document":"F.dwg","mode":"equal","number_of_ranges":3,"colors":[1,2]}""", "exactly number_of_ranges")]
    [InlineData("""{"action":"apply_elevation_analysis","name":"V","target_document":"F.dwg","mode":"equal","number_of_ranges":3,"interval":1}""", "does not apply")]
    [InlineData("""{"action":"apply_elevation_analysis","name":"V","target_document":"F.dwg","mode":"ranges","ranges":[{"min":0,"max":2,"color":1},{"min":1,"max":3,"color":2}]}""", "must not overlap")]
    [InlineData("""{"action":"apply_elevation_analysis","name":"V","target_document":"F.dwg","mode":"ranges","ranges":[{"min":0,"max":2,"color":"red"}]}""", "needs a color")]
    [InlineData("""{"action":"apply_elevation_analysis","name":"V","target_document":"F.dwg","mode":"recolor"}""", "needs colors")]
    [InlineData("""{"action":"apply_elevation_analysis","name":"V","target_document":"F.dwg","mode":"step","interval":0}""", "interval > 0")]
    [InlineData("""{"action":"style_display","style":"S","target_document":"F.dwg","components":{"slopes":{"visible":true}}}""", null)]
    [InlineData("""{"action":"style_display","style":"S","target_document":"F.dwg","components":{"slope":{"visible":true}}}""", "Unknown component")]
    [InlineData("""{"action":"style_display","style":"S","target_document":"F.dwg","components":{"slopes":{}}}""", "at least one")]
    [InlineData("""{"action":"style_display","style":"S","target_document":"F.dwg","components":{"slopes":{"width":2}}}""", "not recognised")]
    [InlineData("""{"action":"style_display","style":"S","target_document":"F.dwg","view":"side","components":{"slopes":{"visible":true}}}""", "plan, model or both")]
    [InlineData("""{"action":"style_display","target_document":"F.dwg","components":{"slopes":{"visible":true}}}""", "needs style")]
    public void Analysis_and_display_validation(string json, string? expected)
    {
        var r = SurfaceInputs.Validate(A(json));
        if (expected == null) Assert.Null(r); else Assert.Contains(expected, r);
        if (expected == null)
            Assert.Null(SchemaCheck.Validate(Contract.Find("horizun_c3d_surface")!.InputSchema, A(json)));
    }
}
