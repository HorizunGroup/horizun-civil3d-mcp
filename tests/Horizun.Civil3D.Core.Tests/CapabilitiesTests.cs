using System.Text.Json.Nodes;
using Horizun.Civil3D.Core;
using Horizun.Civil3D.Server;

namespace Horizun.Civil3D.Core.Tests;

public class CapabilitiesTests
{
    [Fact]
    public void Catalog_counts_come_from_the_contract_and_do_not_claim_live_verification()
    {
        var result = ContractCapabilities.Build(Settings.Parse("{}"));
        var tools = (JsonArray)result["tools"]!;
        var expectedActions = Contract.All.Sum(tool =>
            (tool.InputSchema["properties"]?["action"]?["enum"] as JsonArray)?.Count ?? 0);

        Assert.Equal("declared_contract", result["source"]!.GetValue<string>());
        Assert.False(result["live_verified"]!.GetValue<bool>());
        Assert.False(result["host_checked"]!.GetValue<bool>());
        Assert.Equal(Contract.Hash, result["contract_hash"]!.GetValue<string>());
        Assert.Equal(Contract.All.Count, result["total_tool_count"]!.GetValue<int>());
        Assert.Equal(expectedActions, result["total_action_count"]!.GetValue<int>());
        Assert.Equal(expectedActions + Contract.All.Count(t =>
            (t.InputSchema["properties"]?["action"]?["enum"] as JsonArray) == null),
            result["total_operation_count"]!.GetValue<int>());
        Assert.Equal(Contract.All.Count, tools.Count);
        Assert.Equal(expectedActions, result["returned_action_count"]!.GetValue<int>());
    }

    [Fact]
    public void Catalog_reports_per_action_effects_and_exact_rules()
    {
        var result = ContractCapabilities.Build(Settings.Parse("{\"permission_profile\":\"read_only\"}"),
            new JsonObject { ["tool"] = "horizun_c3d_document" });
        var tool = (JsonObject)((JsonArray)result["tools"]!)[0]!;
        var actions = ((JsonArray)tool["actions"]!).Cast<JsonObject>().ToDictionary(a => a["name"]!.GetValue<string>());

        Assert.Equal("read", actions["info"]["effect"]!.GetValue<string>());
        Assert.Equal("full_write", actions["save"]["effect"]!.GetValue<string>());
        Assert.True(actions["info"]["permission_allowed"]!.GetValue<bool>());
        Assert.False(actions["save"]["permission_allowed"]!.GetValue<bool>());
        Assert.Contains("action", ((JsonArray)tool["required_fields"]!).Select(n => n!.GetValue<string>()));
    }

    [Fact]
    public void Action_rules_are_named_when_available_and_other_fields_remain_tool_scoped()
    {
        var result = ContractCapabilities.Build(Settings.Parse("{}"),
            new JsonObject { ["tool"] = "horizun_c3d_alignment", ["include_schema"] = true });
        var tool = (JsonObject)((JsonArray)result["tools"]!)[0]!;
        var action = ((JsonArray)tool["actions"]!).Cast<JsonObject>()
            .Single(a => a["name"]!.GetValue<string>() == "create_from_polyline");

        Assert.Equal("create_from_polyline", action["name"]!.GetValue<string>());
        Assert.Equal("action_rule", action["fields_scope"]!.GetValue<string>());
        Assert.Contains("target_document", ((JsonArray)action["required_fields"]!).Select(n => n!.GetValue<string>()));
        Assert.NotNull(tool["input_schema"]);
    }

    [Fact]
    public void Effect_filter_selects_matching_actions_without_changing_global_counts()
    {
        var result = ContractCapabilities.Build(Settings.Parse("{}"),
            new JsonObject { ["tool"] = "horizun_c3d_document", ["effect"] = "full_write" });
        var tool = (JsonObject)((JsonArray)result["tools"]!)[0]!;
        var action = (JsonObject)((JsonArray)tool["actions"]!)[0]!;

        Assert.Equal("save", action["name"]!.GetValue<string>());
        Assert.Equal(1, result["returned_action_count"]!.GetValue<int>());
        Assert.True(result["total_action_count"]!.GetValue<int>() > 1);
    }

    [Fact]
    public void Mcp_tool_works_without_a_civil3d_instance()
    {
        var server = new McpServer("test");
        var reply = server.ToolsCall("catalog", new JsonObject
        {
            ["name"] = "horizun_c3d_capabilities",
            ["arguments"] = new JsonObject { ["tool"] = "horizun_c3d_capabilities" },
        });
        Assert.False(reply["isError"]!.GetValue<bool>());
        Assert.Equal(1, reply["structuredContent"]!["returned_tool_count"]!.GetValue<int>());
        Assert.False(reply["structuredContent"]!["live_verified"]!.GetValue<bool>());
    }
}
