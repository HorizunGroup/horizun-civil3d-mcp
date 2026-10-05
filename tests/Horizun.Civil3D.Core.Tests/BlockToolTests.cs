using System.Text.Json.Nodes;
using Horizun.Civil3D.Core;

namespace Horizun.Civil3D.Core.Tests;

/// <summary>Consistency of every action-based tool (blocks A-D) and the road rules.</summary>
public class BlockToolTests
{
    private static readonly string[] CommonFields = { "action", "target_document", "dry_run", "confirmation_token" };

    public static IEnumerable<object[]> BlockToolNames() =>
        Contract.All.Where(c => ToolRules.Specs(c.Name) != null).Select(c => new object[] { c.Name });

    private static JsonObject Props(ToolContract c) => (JsonObject)c.InputSchema["properties"]!;

    [Theory]
    [MemberData(nameof(BlockToolNames))]
    public void Schema_action_enum_matches_the_specs(string tool)
    {
        var c = Contract.Find(tool)!;
        var specs = ToolRules.Specs(tool)!;
        var actions = ((JsonArray)Props(c)["action"]!["enum"]!).Select(n => n!.GetValue<string>()).ToList();
        Assert.Equal(specs.Keys.OrderBy(k => k), actions.OrderBy(k => k));
    }

    [Theory]
    [MemberData(nameof(BlockToolNames))]
    public void Every_spec_field_is_in_the_schema_and_every_schema_field_is_used(string tool)
    {
        var c = Contract.Find(tool)!;
        var specs = ToolRules.Specs(tool)!;
        var props = Props(c).Select(kv => kv.Key).ToHashSet();
        var used = specs.Values.SelectMany(s => s.Required.Concat(s.Optional)).ToHashSet();
        foreach (var f in used) Assert.True(props.Contains(f), tool + ": field '" + f + "' is missing from the schema.");
        foreach (var p in props.Where(p => !CommonFields.Contains(p))) Assert.True(used.Contains(p), tool + ": schema field '" + p + "' is used by no action.");
    }

    [Theory]
    [MemberData(nameof(BlockToolNames))]
    public void Effects_come_from_the_specs_and_reads_are_reads(string tool)
    {
        var c = Contract.Find(tool)!;
        foreach (var (action, spec) in ToolRules.Specs(tool)!)
        {
            var args = new JsonObject { ["action"] = action };
            Assert.Equal(spec.Effect, c.EffectFor(args));
        }
    }

    [Theory]
    [MemberData(nameof(BlockToolNames))]
    public void Writes_without_target_document_are_refused(string tool)
    {
        foreach (var (action, spec) in ToolRules.Specs(tool)!.Where(kv => kv.Value.IsWrite))
        {
            var args = new JsonObject { ["action"] = action };
            foreach (var r in spec.Required) args[r] = "x";
            var why = ToolRules.Validate(tool, args);
            Assert.NotNull(why);
        }
    }

    [Theory]
    [MemberData(nameof(BlockToolNames))]
    public void Unknown_fields_and_actions_are_refused(string tool)
    {
        Assert.Contains("action must be one of", ToolRules.Validate(tool, new JsonObject { ["action"] = "nope" }));
        var first = ToolRules.Specs(tool)!.First();
        var args = new JsonObject { ["action"] = first.Key, ["bogus_field"] = 1 };
        Assert.Contains("bogus_field", ToolRules.Validate(tool, args));
    }

    [Fact]
    public void Read_actions_refuse_dry_run_fields()
    {
        foreach (var c in Contract.All.Where(c => ToolRules.Specs(c.Name) != null))
            foreach (var (action, spec) in ToolRules.Specs(c.Name)!.Where(kv => !kv.Value.IsWrite))
                Assert.NotNull(ToolRules.Validate(c.Name, new JsonObject { ["action"] = action, ["dry_run"] = false }));
    }

    private static string? V(string tool, string json) => ToolRules.Validate(tool, JsonNode.Parse(json)!.AsObject());

    [Theory]
    [InlineData("{\"action\":\"get\",\"name\":\"A\"}", true)]
    [InlineData("{\"action\":\"get\",\"name\":\"A\",\"handle\":\"1F\"}", false)]
    [InlineData("{\"action\":\"get\"}", false)]
    [InlineData("{\"action\":\"get\",\"handle\":\"XYZ\"}", false)]
    [InlineData("{\"action\":\"station_offset\",\"name\":\"A\",\"points\":[{\"x\":1,\"y\":2}]}", true)]
    [InlineData("{\"action\":\"station_offset\",\"name\":\"A\",\"stations\":[{\"station\":10}]}", true)]
    [InlineData("{\"action\":\"station_offset\",\"name\":\"A\"}", false)]
    [InlineData("{\"action\":\"create_by_pis\",\"target_document\":\"d\",\"new_name\":\"C\",\"pis\":[{\"x\":10,\"y\":10},{\"x\":50,\"y\":10},{\"x\":90,\"y\":40}],\"radii\":[20]}", true)]
    [InlineData("{\"action\":\"create_by_pis\",\"target_document\":\"d\",\"new_name\":\"C\",\"pis\":[{\"x\":10,\"y\":10},{\"x\":50,\"y\":10},{\"x\":90,\"y\":40}],\"radii\":[20,30]}", false)]
    [InlineData("{\"action\":\"create_by_pis\",\"target_document\":\"d\",\"new_name\":\"C\",\"pis\":[{\"x\":10,\"y\":10},{\"x\":50,\"y\":10},{\"x\":90,\"y\":40}],\"radii\":[-1]}", false)]
    [InlineData("{\"action\":\"create_by_pis\",\"new_name\":\"C\",\"pis\":[{\"x\":10,\"y\":10},{\"x\":50,\"y\":10}]}", false)]
    [InlineData("{\"action\":\"create_offset\",\"target_document\":\"d\",\"new_name\":\"O\",\"name\":\"A\",\"offset\":5}", true)]
    [InlineData("{\"action\":\"create_offset\",\"target_document\":\"d\",\"new_name\":\"O\",\"name\":\"A\",\"offset\":0}", false)]
    [InlineData("{\"action\":\"create_from_polyline\",\"target_document\":\"d\",\"new_name\":\"P\",\"polyline\":\"2A\"}", true)]
    [InlineData("{\"action\":\"create_from_polyline\",\"target_document\":\"d\",\"new_name\":\"P\",\"polyline\":\"zz\"}", false)]
    public void Alignment_rules(string json, bool ok) => Assert.Equal(ok, V("horizun_c3d_alignment", json) == null);

    [Theory]
    [InlineData("{\"action\":\"create_layout\",\"target_document\":\"d\",\"alignment\":\"A\",\"new_name\":\"L\",\"pvis\":[{\"station\":0,\"elevation\":101},{\"station\":45,\"elevation\":102.5,\"curve_length\":20},{\"station\":90,\"elevation\":101}]}", true)]
    [InlineData("{\"action\":\"create_layout\",\"target_document\":\"d\",\"alignment\":\"A\",\"new_name\":\"L\",\"pvis\":[{\"station\":0,\"elevation\":101,\"curve_length\":20},{\"station\":90,\"elevation\":101}]}", false)]
    [InlineData("{\"action\":\"create_layout\",\"target_document\":\"d\",\"alignment\":\"A\",\"new_name\":\"L\",\"pvis\":[{\"station\":10,\"elevation\":101},{\"station\":10,\"elevation\":101}]}", false)]
    [InlineData("{\"action\":\"create_layout\",\"target_document\":\"d\",\"alignment\":\"A\",\"new_name\":\"L\",\"pvis\":[{\"station\":0,\"elevation\":101},{\"station\":45,\"elevation\":102,\"curve_length\":-2},{\"station\":90,\"elevation\":101}]}", false)]
    [InlineData("{\"action\":\"check_k\",\"name\":\"P\"}", false)]
    [InlineData("{\"action\":\"check_k\",\"name\":\"P\",\"min_k_crest\":17}", true)]
    [InlineData("{\"action\":\"elevation_at\",\"name\":\"P\",\"stations\":[0,10]}", true)]
    [InlineData("{\"action\":\"create_view\",\"target_document\":\"d\",\"alignment\":\"A\",\"insert\":{\"x\":0,\"y\":-100}}", true)]
    public void Profile_rules(string json, bool ok) => Assert.Equal(ok, V("horizun_c3d_profile", json) == null);

    [Theory]
    [InlineData("{\"action\":\"create_sample_lines\",\"target_document\":\"d\",\"alignment\":\"A\",\"group\":\"G\",\"left_width\":10,\"right_width\":10,\"interval\":10}", true)]
    [InlineData("{\"action\":\"create_sample_lines\",\"target_document\":\"d\",\"alignment\":\"A\",\"group\":\"G\",\"left_width\":10,\"right_width\":10,\"interval\":10,\"stations\":[5]}", false)]
    [InlineData("{\"action\":\"create_sample_lines\",\"target_document\":\"d\",\"alignment\":\"A\",\"group\":\"G\",\"left_width\":0,\"right_width\":10,\"stations\":[5]}", false)]
    [InlineData("{\"action\":\"create_sample_lines\",\"target_document\":\"d\",\"alignment\":\"A\",\"group\":\"G\",\"left_width\":10,\"right_width\":10,\"stations\":[5],\"sources\":[\"HZ_EG\"]}", true)]
    [InlineData("{\"action\":\"get_section\",\"alignment\":\"A\",\"group\":\"G\",\"station\":10}", true)]
    public void Sections_rules(string json, bool ok) => Assert.Equal(ok, V("horizun_c3d_sections", json) == null);

    [Theory]
    [InlineData("{\"action\":\"create\",\"target_document\":\"d\",\"new_name\":\"C\",\"alignment\":\"A\",\"profile\":\"P\",\"assembly\":\"S\",\"frequency\":{\"tangents\":10}}", true)]
    [InlineData("{\"action\":\"create\",\"target_document\":\"d\",\"new_name\":\"C\",\"alignment\":\"A\",\"profile\":\"P\",\"assembly\":\"S\",\"frequency\":{\"tangents\":0}}", false)]
    [InlineData("{\"action\":\"create\",\"target_document\":\"d\",\"new_name\":\"C\",\"alignment\":\"A\",\"profile\":\"P\",\"assembly\":\"S\",\"frequency\":{\"along\":5}}", false)]
    [InlineData("{\"action\":\"add_region\",\"target_document\":\"d\",\"name\":\"C\",\"assembly\":\"S\",\"start_station\":50,\"end_station\":40}", false)]
    [InlineData("{\"action\":\"assembly_create\",\"target_document\":\"d\",\"new_name\":\"S\",\"insert\":{\"x\":0,\"y\":0},\"assembly_type\":\"Railway\"}", true)]
    [InlineData("{\"action\":\"assembly_create\",\"target_document\":\"d\",\"new_name\":\"S\",\"insert\":{\"x\":0,\"y\":0},\"assembly_type\":\"Bridge\"}", false)]
    [InlineData("{\"action\":\"create_surface\",\"target_document\":\"d\",\"name\":\"C\",\"surface_name\":\"T\"}", false)]
    [InlineData("{\"action\":\"create_surface\",\"target_document\":\"d\",\"name\":\"C\",\"surface_name\":\"T\",\"link_codes\":[\"Top\"]}", true)]
    public void Corridor_rules(string json, bool ok) => Assert.Equal(ok, V("horizun_c3d_corridor", json) == null);

    [Theory]
    [InlineData("{\"action\":\"list_styles\",\"kind\":\"surface_spot_elevation\"}", true)]
    [InlineData("{\"action\":\"list_styles\",\"kind\":\"bridge\"}", false)]
    [InlineData("{\"action\":\"list\"}", true)]
    [InlineData("{\"action\":\"list\",\"alignment\":\"A\",\"surface\":\"S\"}", false)]
    [InlineData("{\"action\":\"surface_spot\",\"target_document\":\"d\",\"surface\":\"S\",\"points\":[{\"x\":1,\"y\":2}]}", true)]
    [InlineData("{\"action\":\"surface_spot\",\"target_document\":\"d\",\"surface\":\"S\",\"points\":[]}", false)]
    [InlineData("{\"action\":\"surface_slope\",\"target_document\":\"d\",\"surface\":\"S\"}", false)]
    [InlineData("{\"action\":\"surface_slope\",\"target_document\":\"d\",\"surface\":\"S\",\"segments\":[{\"from\":{\"x\":0,\"y\":0},\"to\":{\"x\":5,\"y\":0}}]}", true)]
    [InlineData("{\"action\":\"surface_slope\",\"target_document\":\"d\",\"surface\":\"S\",\"segments\":[{\"from\":{\"x\":0,\"y\":0}}]}", false)]
    [InlineData("{\"action\":\"contour_labels\",\"target_document\":\"d\",\"surface\":\"S\",\"line\":[{\"x\":0,\"y\":0}]}", false)]
    [InlineData("{\"action\":\"station_elevation\",\"target_document\":\"d\",\"profile_view\":\"PV\",\"items\":[{\"station\":10,\"elevation\":101}]}", true)]
    [InlineData("{\"action\":\"station_elevation\",\"target_document\":\"d\",\"profile_view\":\"PV\",\"items\":[{\"station\":10}]}", false)]
    [InlineData("{\"action\":\"segment\",\"target_document\":\"d\",\"entity\":\"2A\",\"ratio\":1.5}", false)]
    [InlineData("{\"action\":\"note\",\"target_document\":\"d\",\"location\":{\"x\":0,\"y\":0},\"text\":\"HZ\"}", true)]
    [InlineData("{\"action\":\"set_text\",\"target_document\":\"d\",\"handle\":\"2A\",\"text\":\"x\",\"component\":1.5}", false)]
    [InlineData("{\"action\":\"erase\",\"target_document\":\"d\",\"handles\":[\"2A\",\"2a\"]}", false)]
    [InlineData("{\"action\":\"alignment_stations\",\"target_document\":\"d\",\"alignment\":\"A\",\"increment\":0}", false)]
    public void Label_rules(string json, bool ok) => Assert.Equal(ok, V("horizun_c3d_labels", json) == null);

    private const string T = "\"target_document\":\"d\",";

    [Theory]
    [InlineData("{\"action\":\"create\"," + T + "\"new_name\":\"C-ROAD\",\"color\":3,\"lineweight\":0.35,\"linetype\":\"DASHED\"}", true)]
    [InlineData("{\"action\":\"create\"," + T + "\"new_name\":\"C-ROAD\",\"color\":300}", false)]
    [InlineData("{\"action\":\"create\"," + T + "\"new_name\":\"C-ROAD\",\"color\":\"#00FF00\"}", true)]
    [InlineData("{\"action\":\"create\"," + T + "\"new_name\":\"C-ROAD\",\"lineweight\":0.33}", false)]
    [InlineData("{\"action\":\"create\"," + T + "\"new_name\":\"BAD<NAME\"}", false)]
    [InlineData("{\"action\":\"set\"," + T + "\"name\":\"C-ROAD\"}", false)]
    [InlineData("{\"action\":\"set\"," + T + "\"name\":\"C-ROAD\",\"transparency\":95}", false)]
    [InlineData("{\"action\":\"list\",\"pattern\":\"C-*\"}", true)]
    public void Layer_rules(string json, bool ok) => Assert.Equal(ok, V("horizun_c3d_layers", json) == null);

    [Theory]
    [InlineData("{\"action\":\"draw\"," + T + "\"items\":[{\"type\":\"line\",\"from\":{\"x\":0,\"y\":0},\"to\":{\"x\":10,\"y\":0}}]}", true)]
    [InlineData("{\"action\":\"draw\"," + T + "\"items\":[{\"type\":\"line\",\"from\":{\"x\":0,\"y\":0}}]}", false)]
    [InlineData("{\"action\":\"draw\"," + T + "\"items\":[{\"type\":\"circle\",\"center\":{\"x\":0,\"y\":0},\"radius\":0}]}", false)]
    [InlineData("{\"action\":\"draw\"," + T + "\"items\":[{\"type\":\"polyline\",\"points\":[{\"x\":0,\"y\":0},{\"x\":1,\"y\":0}],\"bulges\":[0]}]}", false)]
    [InlineData("{\"action\":\"draw\"," + T + "\"items\":[{\"type\":\"text\",\"position\":{\"x\":0,\"y\":0},\"text\":\"A\",\"height\":2.5,\"justify\":\"middle_center\"}]}", true)]
    [InlineData("{\"action\":\"draw\"," + T + "\"items\":[{\"type\":\"text\",\"position\":{\"x\":0,\"y\":0},\"text\":\"A\",\"height\":2.5,\"radius\":3}]}", false)]
    [InlineData("{\"action\":\"draw\"," + T + "\"items\":[{\"type\":\"hatch\",\"boundaries\":[\"2A\"],\"pattern\":\"ANSI31\"}]}", true)]
    [InlineData("{\"action\":\"transform\"," + T + "\"handles\":[\"2A\"],\"operation\":\"move\",\"displacement\":{\"x\":5,\"y\":0}}", true)]
    [InlineData("{\"action\":\"transform\"," + T + "\"handles\":[\"2A\"],\"operation\":\"move\",\"angle\":5,\"displacement\":{\"x\":5,\"y\":0}}", false)]
    [InlineData("{\"action\":\"transform\"," + T + "\"handles\":[\"2A\"],\"operation\":\"rotate\",\"angle\":90}", false)]
    [InlineData("{\"action\":\"transform\"," + T + "\"handles\":[\"2A\"],\"operation\":\"mirror\",\"mirror_line\":{\"from\":{\"x\":0,\"y\":0},\"to\":{\"x\":0,\"y\":0}}}", false)]
    [InlineData("{\"action\":\"offset\"," + T + "\"handle\":\"2A\",\"distance\":0}", false)]
    [InlineData("{\"action\":\"join\"," + T + "\"handles\":[\"2A\"]}", false)]
    [InlineData("{\"action\":\"query\",\"window\":{\"min\":{\"x\":10,\"y\":10},\"max\":{\"x\":0,\"y\":0}}}", false)]
    public void Entity_rules(string json, bool ok) => Assert.Equal(ok, V("horizun_c3d_entities", json) == null);

    [Fact]
    public void Erase_delete_plot_and_purge_need_full_write()
    {
        Assert.Equal(ToolEffect.FullWrite, Contract.Find("horizun_c3d_entities")!.EffectFor(new JsonObject { ["action"] = "erase" }));
        Assert.Equal(ToolEffect.FullWrite, Contract.Find("horizun_c3d_layouts")!.EffectFor(new JsonObject { ["action"] = "delete" }));
        Assert.Equal(ToolEffect.FullWrite, Contract.Find("horizun_c3d_layouts")!.EffectFor(new JsonObject { ["action"] = "plot_pdf" }));
        Assert.Equal(ToolEffect.FullWrite, Contract.Find("horizun_c3d_cleanup")!.EffectFor(new JsonObject { ["action"] = "purge" }));
        Assert.NotNull(Settings.Parse("{}").Refusal("horizun_c3d_entities", ToolEffect.FullWrite));
    }

    [Theory]
    [InlineData("{\"action\":\"chain\"," + T + "\"points\":[{\"x\":0,\"y\":0},{\"x\":10,\"y\":0},{\"x\":25,\"y\":0}],\"dim_line_point\":{\"x\":0,\"y\":5}}", true)]
    [InlineData("{\"action\":\"chain\"," + T + "\"points\":[{\"x\":0,\"y\":0},{\"x\":10,\"y\":0}],\"dim_line_point\":{\"x\":0,\"y\":5}}", false)]
    [InlineData("{\"action\":\"ordinate\"," + T + "\"feature_point\":{\"x\":5,\"y\":5},\"leader_end\":{\"x\":5,\"y\":9},\"axis\":\"z\"}", false)]
    [InlineData("{\"action\":\"mleader\"," + T + "\"arrow_point\":{\"x\":0,\"y\":0},\"landing_point\":{\"x\":5,\"y\":5},\"text\":\"\"}", false)]
    public void Dimension_rules(string json, bool ok) => Assert.Equal(ok, V("horizun_c3d_dimensions", json) == null);

    [Theory]
    [InlineData("{\"action\":\"dim_create\"," + T + "\"new_name\":\"HZ\",\"properties\":{\"dimtxt\":2.5,\"dimdec\":2,\"dimtih\":false}}", true)]
    [InlineData("{\"action\":\"dim_create\"," + T + "\"new_name\":\"HZ\",\"properties\":{\"dimfoo\":1}}", false)]
    [InlineData("{\"action\":\"dim_create\"," + T + "\"new_name\":\"HZ\",\"properties\":{\"dimdec\":2.5}}", false)]
    [InlineData("{\"action\":\"dim_create\"," + T + "\"new_name\":\"HZ\",\"properties\":{\"dimtih\":1}}", false)]
    [InlineData("{\"action\":\"text_create\"," + T + "\"new_name\":\"HZ\",\"font\":\"arial.ttf\",\"oblique\":90}", false)]
    [InlineData("{\"action\":\"set_current\"," + T + "\"kind\":\"layer\",\"name\":\"0\"}", false)]
    public void Cad_style_rules(string json, bool ok) => Assert.Equal(ok, V("horizun_c3d_cad_styles", json) == null);

    [Theory]
    [InlineData("{\"action\":\"define\"," + T + "\"new_name\":\"HZ_TB\",\"base_point\":{\"x\":0,\"y\":0},\"attributes\":[{\"tag\":\"TITLE\",\"position\":{\"x\":0,\"y\":0}}]}", true)]
    [InlineData("{\"action\":\"define\"," + T + "\"new_name\":\"HZ_TB\",\"base_point\":{\"x\":0,\"y\":0}}", false)]
    [InlineData("{\"action\":\"define\"," + T + "\"new_name\":\"HZ_TB\",\"base_point\":{\"x\":0,\"y\":0},\"attributes\":[{\"tag\":\"A B\",\"position\":{\"x\":0,\"y\":0}}]}", false)]
    [InlineData("{\"action\":\"define\"," + T + "\"new_name\":\"HZ_TB\",\"base_point\":{\"x\":0,\"y\":0},\"attributes\":[{\"tag\":\"A\",\"position\":{\"x\":0,\"y\":0}},{\"tag\":\"a\",\"position\":{\"x\":0,\"y\":0}}]}", false)]
    [InlineData("{\"action\":\"insert\"," + T + "\"name\":\"HZ_TB\",\"position\":{\"x\":0,\"y\":0},\"attributes\":{\"TITLE\":\"X\"}}", true)]
    [InlineData("{\"action\":\"insert\"," + T + "\"name\":\"HZ_TB\",\"position\":{\"x\":0,\"y\":0},\"attributes\":{\"TITLE\":5}}", false)]
    [InlineData("{\"action\":\"set_attributes\"," + T + "\"values\":{\"A\":\"1\"}}", false)]
    public void Block_rules(string json, bool ok) => Assert.Equal(ok, V("horizun_c3d_blocks", json) == null);

    [Theory]
    [InlineData("{\"action\":\"create\"," + T + "\"position\":{\"x\":0,\"y\":0},\"rows\":[[\"a\",\"b\"],[\"1\",2]]}", true)]
    [InlineData("{\"action\":\"create\"," + T + "\"position\":{\"x\":0,\"y\":0},\"rows\":[[\"a\",\"b\"],[\"1\"]]}", false)]
    [InlineData("{\"action\":\"set_cells\"," + T + "\"handle\":\"2A\",\"cells\":[{\"row\":-1,\"col\":0,\"value\":\"x\"}]}", false)]
    public void Table_rules(string json, bool ok) => Assert.Equal(ok, V("horizun_c3d_tables", json) == null);

    [Theory]
    [InlineData("{\"action\":\"plot_pdf\"," + T + "\"layouts\":[\"L1\"],\"output\":\"C:\\\\tmp\\\\a.pdf\"}", true)]
    [InlineData("{\"action\":\"plot_pdf\"," + T + "\"layouts\":[\"L1\"],\"output\":\"a.pdf\"}", false)]
    [InlineData("{\"action\":\"page_setup\"," + T + "\"layout\":\"L1\",\"device\":\"DWG To PDF.pc3\",\"fit\":true,\"scale\":1}", false)]
    [InlineData("{\"action\":\"create\"," + T + "\"new_name\":\"L2\",\"template_dwg\":\"C:\\\\t.dwt\"}", false)]
    [InlineData("{\"action\":\"viewport\"," + T + "\"layout\":\"L1\",\"center\":{\"x\":200,\"y\":150},\"width\":300,\"height\":200,\"view_center\":{\"x\":50,\"y\":50},\"scale\":0.002}", true)]
    public void Layout_rules(string json, bool ok) => Assert.Equal(ok, V("horizun_c3d_layouts", json) == null);

    [Theory]
    [InlineData("{\"action\":\"purge_preview\",\"kinds\":[\"layers\",\"blocks\"]}", true)]
    [InlineData("{\"action\":\"purge_preview\",\"kinds\":[\"views\"]}", false)]
    [InlineData("{\"action\":\"standards_check\"}", false)]
    [InlineData("{\"action\":\"standards_check\",\"standard\":{\"layers\":[]}}", true)]
    public void Cleanup_rules(string json, bool ok) => Assert.Equal(ok, V("horizun_c3d_cleanup", json) == null);
}
