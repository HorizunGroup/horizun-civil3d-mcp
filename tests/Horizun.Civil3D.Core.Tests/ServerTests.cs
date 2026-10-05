using System.IO.Pipes;
using System.Text.Json.Nodes;
using Horizun.Civil3D.Core;
using Horizun.Civil3D.Server;

namespace Horizun.Civil3D.Core.Tests;

public class SchemaCheckTests
{
    private static JsonObject Schema(string tool) => Contract.Find(tool)!.InputSchema;

    [Fact]
    public void Valid_request_passes() =>
        Assert.Null(SchemaCheck.Validate(Schema("horizun_c3d_query"), new JsonObject { ["action"] = "list", ["type"] = "surface", ["limit"] = 50 }));

    [Fact]
    public void Missing_required_is_named() =>
        Assert.Contains("action is required", SchemaCheck.Validate(Schema("horizun_c3d_query"), new JsonObject()));

    [Fact]
    public void Unknown_field_is_refused() =>
        Assert.Contains("not a recognised field", SchemaCheck.Validate(Schema("horizun_c3d_query"), new JsonObject { ["action"] = "list", ["typo"] = 1 }));

    [Fact]
    public void Enum_and_range_are_checked()
    {
        Assert.Contains("must be one of", SchemaCheck.Validate(Schema("horizun_c3d_query"), new JsonObject { ["action"] = "drop" }));
        Assert.Contains("<= 1000", SchemaCheck.Validate(Schema("horizun_c3d_query"), new JsonObject { ["action"] = "list", ["limit"] = 5000 }));
        Assert.Contains("must be integer", SchemaCheck.Validate(Schema("horizun_c3d_query"), new JsonObject { ["action"] = "list", ["limit"] = 2.5 }));
    }

    [Fact]
    public void Union_types_accept_either()
    {
        Assert.Null(SchemaCheck.Validate(Schema("horizun_c3d_target"), new JsonObject { ["year"] = 2025 }));
        Assert.Null(SchemaCheck.Validate(Schema("horizun_c3d_target"), new JsonObject { ["year"] = "auto" }));
        Assert.NotNull(SchemaCheck.Validate(Schema("horizun_c3d_target"), new JsonObject { ["year"] = true }));
    }
}

/// <summary>
/// End to end WITHOUT Civil 3D: a fake plug-in answers on a real named pipe with
/// the same wire format, and the real MCP server code calls it.
/// </summary>
[Collection("pipe")]
public class WireEndToEndTests
{
    private static DiscoveryRecord FakeRecord(string pipe, string token, string? hash = null, int pid = 4242, int year = 2025) => new()
    {
        Year = year, Pid = pid, PipeName = pipe, AuthToken = token, StartedUtc = DateTime.UtcNow,
        ProtocolVersion = Contract.ProtocolVersion, ContractHash = hash ?? Contract.Hash, PluginVersion = "test",
        Commands = Contract.PluginCommands.ToList(),
    };

    private static Task FakePlugin(string pipe, string token, Func<JsonObject, JsonObject> answer)
    {
        var server = new NamedPipeServerStream(pipe, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        return Task.Run(() =>
        {
            using (server)
            {
                server.WaitForConnection();
                var req = (JsonObject)JsonNode.Parse(Wire.ReadLine(server, Contract.MaxRequestBytes)!)!;
                var ok = Hz.SecretEquals(Hz.Str(req, "token"), token);
                var reply = ok
                    ? answer(req)
                    : Wire.Reply(Hz.Str(req, "id"), CommandResult.Fail(ErrorCodes.PermissionDenied, "bad token"));
                Wire.WriteLine(server, reply);
            }
        });
    }

    private static McpServer WithInstances(params DiscoveryRecord[] recs)
    {
        PluginClient.ReadAll = () => recs.ToList();
        PluginClient.IsAlive = _ => true;
        return new McpServer("test");
    }

    [Fact]
    public void Successful_call_carries_structured_content_and_target()
    {
        var pipe = "hz-test-" + Guid.NewGuid().ToString("N");
        var fake = FakePlugin(pipe, "tok", req => Wire.Reply(Hz.Str(req, "id"),
            CommandResult.Ok(new JsonObject { ["host"] = "civil3d", ["echo"] = Hz.Str(req, "command") }),
            null, new JsonObject { ["queued"] = false, ["waited_ms"] = 3 }));
        var server = WithInstances(FakeRecord(pipe, "tok"));

        var r = server.ToolsCall("1", new JsonObject { ["name"] = "horizun_c3d_health", ["arguments"] = new JsonObject() });
        fake.Wait(5000);
        Assert.False(r["isError"]!.GetValue<bool>());
        var sc = (JsonObject)r["structuredContent"]!;
        Assert.Equal("health", sc["echo"]!.GetValue<string>());
        Assert.Equal(2025, sc["target"]!["civil3d_year"]!.GetValue<int>());
        Assert.NotNull(sc["bridge_queue"]);
    }

    [Fact]
    public void Plugin_failure_becomes_tool_error_with_code()
    {
        var pipe = "hz-test-" + Guid.NewGuid().ToString("N");
        var fake = FakePlugin(pipe, "tok", req => Wire.Reply(Hz.Str(req, "id"),
            CommandResult.Fail(ErrorCodes.Busy, "Civil 3D is busy: a command is active. NOTHING RAN.", new JsonObject { ["cmdactive"] = 1 })));
        var server = WithInstances(FakeRecord(pipe, "tok"));

        var r = server.ToolsCall("2", new JsonObject { ["name"] = "horizun_c3d_query", ["arguments"] = new JsonObject { ["action"] = "list", ["type"] = "surface" } });
        fake.Wait(5000);
        Assert.True(r["isError"]!.GetValue<bool>());
        Assert.Equal("busy", r["structuredContent"]!["code"]!.GetValue<string>());
        Assert.Equal(1, r["structuredContent"]!["cmdactive"]!.GetValue<int>());
    }

    [Fact]
    public void Contract_mismatch_is_refused_before_sending()
    {
        var server = WithInstances(FakeRecord("never-connected", "tok", hash: "000000000000000000000000"));
        var r = server.ToolsCall("3", new JsonObject { ["name"] = "horizun_c3d_health", ["arguments"] = new JsonObject() });
        Assert.True(r["isError"]!.GetValue<bool>());
        Assert.Equal(ErrorCodes.ContractMismatch, r["structuredContent"]!["code"]!.GetValue<string>());
    }

    [Fact]
    public void Two_instances_are_ambiguous_until_targeted()
    {
        var server = WithInstances(FakeRecord("a", "t", pid: 1, year: 2025), FakeRecord("b", "t", pid: 2, year: 2026));
        var r = server.ToolsCall("4", new JsonObject { ["name"] = "horizun_c3d_health", ["arguments"] = new JsonObject() });
        Assert.Equal(ErrorCodes.Ambiguous, r["structuredContent"]!["code"]!.GetValue<string>());

        var t = server.ToolsCall("5", new JsonObject { ["name"] = "horizun_c3d_target", ["arguments"] = new JsonObject { ["year"] = 2026 } });
        Assert.False(t["isError"]!.GetValue<bool>());
        Assert.Equal(2, t["structuredContent"]!["will_talk_to"]!["pid"]!.GetValue<int>());
    }

    [Fact]
    public void Tools_list_exposes_contract_with_annotations()
    {
        var server = WithInstances();
        var tools = (JsonArray)server.ToolsList()["tools"]!;
        var names = tools.Select(t => t!["name"]!.GetValue<string>()).ToList();
        Assert.Contains("horizun_c3d_health", names);
        var query = tools.First(t => t!["name"]!.GetValue<string>() == "horizun_c3d_query")!;
        Assert.True(query["annotations"]!["readOnlyHint"]!.GetValue<bool>());
        var doc = tools.First(t => t!["name"]!.GetValue<string>() == "horizun_c3d_document")!;
        Assert.False(doc["annotations"]!["readOnlyHint"]!.GetValue<bool>());
    }
}
