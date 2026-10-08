using System.Text.Json.Nodes;
using Horizun.Civil3D.Core;

namespace Horizun.Civil3D.Core.Tests;

/// <summary>
/// MCP clients may fill the defaults a tool schema declares. Found live (v0.8.0): entities query and layouts list
/// were refused with "Field 'dry_run' is not used by action ..." although the user never sent dry_run.
/// A field that only carries its schema default must never change the validation outcome.
/// </summary>
public class SchemaDefaultTests
{
    private static string? Validate(string tool, JsonObject args) => tool switch
    {
        "horizun_c3d_surface" => SurfaceInputs.Validate(args),
        "horizun_c3d_feature_line" => FeatureLineInputs.Validate(args),
        "horizun_c3d_grading" => GradingInputs.Validate(args),
        _ => ToolRules.Validate(tool, args),
    };

    private static IEnumerable<string> Actions(ToolContract c) =>
        ((c.InputSchema["properties"] as JsonObject)?["action"]?["enum"] as JsonArray)?.Select(n => n!.GetValue<string>()) ?? Enumerable.Empty<string>();

    public static IEnumerable<object[]> ToolActions() =>
        Contract.All.Where(c => ToolRules.Specs(c.Name) != null || c.Name is "horizun_c3d_surface" or "horizun_c3d_feature_line" or "horizun_c3d_grading")
            .SelectMany(c => Actions(c).Select(a => new object[] { c.Name, a }));

    private static JsonObject Defaults(string tool)
    {
        var o = new JsonObject();
        foreach (var (k, p) in (JsonObject)Contract.Find(tool)!.InputSchema["properties"]!)
            if (p is JsonObject po && po.TryGetPropertyValue("default", out var d)) o[k] = d!.DeepClone();
        return o;
    }

    private static JsonObject Base(string tool, string action)
    {
        var a = new JsonObject { ["action"] = action };
        if (ToolRules.Specs(tool)?[action] is { } spec) foreach (var r in spec.Required) a[r] = "x";
        return a;
    }

    [Theory]
    [MemberData(nameof(ToolActions))]
    public void Filling_every_schema_default_never_changes_the_outcome(string tool, string action)
    {
        var plain = Base(tool, action);
        var filled = Base(tool, action);
        foreach (var (k, v) in Defaults(tool)) if (filled[k] == null) filled[k] = v!.DeepClone();
        Assert.Equal(Validate(tool, plain), Validate(tool, filled));
    }

    [Theory]
    [MemberData(nameof(ToolActions))]
    public void Default_dry_run_is_accepted_on_every_action(string tool, string action)
    {
        var plain = Base(tool, action);
        var withDry = Base(tool, action);
        withDry["dry_run"] = true;
        Assert.Equal(Validate(tool, plain), Validate(tool, withDry));
    }

    [Theory]
    [InlineData("horizun_c3d_entities", "{\"action\":\"query\",\"dry_run\":true}")]
    [InlineData("horizun_c3d_entities", "{\"action\":\"query\",\"dry_run\":true,\"limit\":50,\"types\":[\"LINE\"]}")]
    [InlineData("horizun_c3d_layouts", "{\"action\":\"list\",\"dry_run\":true}")]
    [InlineData("horizun_c3d_layers", "{\"action\":\"list\",\"dry_run\":true}")]
    [InlineData("horizun_c3d_surface", "{\"action\":\"list\",\"dry_run\":true}")]
    // Live (v0.8.0): create_from_polyline refused "Field 'insert_intermediate' is not used" (schema default false).
    [InlineData("horizun_c3d_feature_line", "{\"action\":\"create_from_polyline\",\"target_document\":\"a.dwg\",\"handles\":[\"1A\"],\"names\":[\"FL1\"],\"insert_intermediate\":false,\"dry_run\":true}")]
    // The same default must not trip the cross-field rule "insert_intermediate applies to mode=from_surface only".
    [InlineData("horizun_c3d_feature_line", "{\"action\":\"set_elevations\",\"target_document\":\"a.dwg\",\"name\":\"FL1\",\"mode\":\"constant\",\"elevation\":100,\"insert_intermediate\":false}")]
    [InlineData("horizun_c3d_surface", "{\"action\":\"get\",\"name\":\"EG\",\"rebuild\":true,\"view\":\"plan\",\"allow_shared_style\":false,\"break_at\":0,\"offset\":0}")]
    public void Live_regression_reads_with_default_dry_run_pass(string tool, string json) =>
        Assert.Null(Validate(tool, JsonNode.Parse(json)!.AsObject()));

    [Theory]
    [InlineData("horizun_c3d_entities", "{\"action\":\"query\",\"dry_run\":false}", "dry_run")]
    [InlineData("horizun_c3d_layouts", "{\"action\":\"list\",\"confirmation_token\":\"t\"}", "confirmation_token")]
    [InlineData("horizun_c3d_surface", "{\"action\":\"list\",\"dry_run\":false}", "dry_run")]
    [InlineData("horizun_c3d_feature_line", "{\"action\":\"create_from_polyline\",\"target_document\":\"a.dwg\",\"handles\":[\"1A\"],\"names\":[\"FL1\"],\"insert_intermediate\":true}", "insert_intermediate")]
    public void A_non_default_value_on_an_unused_field_is_still_refused(string tool, string json, string field) =>
        Assert.Contains("'" + field + "'", Validate(tool, JsonNode.Parse(json)!.AsObject()));

    [Fact]
    public void Validation_does_not_mutate_the_caller_arguments()
    {
        var args = JsonNode.Parse("{\"action\":\"query\",\"dry_run\":true}")!.AsObject();
        Assert.Null(ToolRules.Validate("horizun_c3d_entities", args));
        Assert.True(args.ContainsKey("dry_run"));
    }
}
