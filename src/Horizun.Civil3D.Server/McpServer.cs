// -----------------------------------------------------------------------------
// Horizun Civil 3D MCP - the Model Context Protocol endpoint (JSON-RPC 2.0 over
// stdio, newline-delimited).
//
// Implements: initialize, notifications/initialized, ping, tools/list,
// tools/call, notifications/cancelled. Tool schemas come ONLY from the shared
// Contract; nothing is fetched from the plug-in except its discovery record.
//
// Every tools/call reply carries structuredContent (the full result object)
// and the same JSON as text for clients that only read text. Errors are tool
// results with isError=true, a machine code and a sentence saying whether
// anything ran.
// -----------------------------------------------------------------------------
using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Horizun.Civil3D.Core;

namespace Horizun.Civil3D.Server;

internal sealed class McpServer
{
    public static readonly string[] SupportedProtocols = { "2025-06-18", "2025-03-26", "2024-11-05" };
    public const string ServerName = "horizun-civil3d";

    private readonly TargetSelection _target = TargetSelection.FromEnvironment();
    private readonly ConcurrentDictionary<string, (DiscoveryRecord Rec, string WireId)> _inflight = new();
    private readonly string _version;

    public McpServer(string version) => _version = version;

    public TargetSelection Target => _target;

    /// <summary>Handle one inbound message. Null = no response (notification).</summary>
    public JsonObject? Handle(JsonObject msg)
    {
        var method = Hz.Str(msg, "method");
        var hasId = msg.TryGetPropertyValue("id", out var idNode) && idNode != null;
        var p = msg["params"] as JsonObject;

        if (method == null) return hasId ? Error(idNode, -32600, "Invalid request: no method.") : null;
        if (!hasId)
        {
            if (method == "notifications/cancelled") Cancel(p);
            return null; // notifications/initialized and any other notification
        }

        try
        {
            return method switch
            {
                "initialize" => Result(idNode, Initialize(p)),
                "ping" => Result(idNode, new JsonObject()),
                "tools/list" => Result(idNode, ToolsList()),
                "tools/call" => Result(idNode, ToolsCall(idNode!.ToJsonString(), p)),
                "resources/list" => Result(idNode, new JsonObject { ["resources"] = new JsonArray() }),
                "prompts/list" => Result(idNode, new JsonObject { ["prompts"] = new JsonArray() }),
                _ => Error(idNode, -32601, "Method not found: " + method),
            };
        }
        catch (Exception e)
        {
            Log.Error("handling " + method, e);
            return Error(idNode, -32603, "Internal error: " + e.Message);
        }
    }

    private JsonObject Initialize(JsonObject? p)
    {
        var requested = Hz.Str(p, "protocolVersion");
        var version = requested != null && SupportedProtocols.Contains(requested) ? requested : SupportedProtocols[0];
        return new JsonObject
        {
            ["protocolVersion"] = version,
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
            ["serverInfo"] = new JsonObject { ["name"] = ServerName, ["title"] = "Horizun Civil 3D MCP", ["version"] = _version },
            ["instructions"] = Instructions.Text,
        };
    }

    public JsonObject ToolsList()
    {
        var settings = Settings.Load();
        var tools = new JsonArray();
        foreach (var c in Contract.All)
        {
            // denied_tools / allowlist hide a tool; the profile does not (the refusal explains how to enable it).
            if (settings.DeniedTools.Contains(c.Name)) continue;
            if (settings.AllowedTools.Count > 0 && !settings.AllowedTools.Contains(c.Name) && c.Name != "horizun_c3d_health") continue;
            var max = c.MaxEffect;
            tools.Add(new JsonObject
            {
                ["name"] = c.Name,
                ["title"] = c.Title,
                ["description"] = c.Description,
                ["inputSchema"] = c.InputSchema.DeepClone(),
                ["annotations"] = new JsonObject
                {
                    ["title"] = c.Title,
                    ["readOnlyHint"] = max <= ToolEffect.HostState,
                    ["destructiveHint"] = c.Destructive,
                    ["idempotentHint"] = max <= ToolEffect.HostState,
                    ["openWorldHint"] = false,
                },
            });
        }
        return new JsonObject { ["tools"] = tools };
    }

    public JsonObject ToolsCall(string mcpRequestId, JsonObject? p)
    {
        var name = Hz.Str(p, "name") ?? "";
        var args = p?["arguments"] as JsonObject ?? new JsonObject();
        var contract = Contract.Find(name);
        if (contract == null)
            return ToolError(ErrorCodes.Unsupported, "Unknown tool '" + name + "'. Nothing ran.");

        var settings = Settings.Load();
        var refusal = settings.Refusal(name, contract.EffectFor(args));
        if (refusal != null) return ToolError(ErrorCodes.PermissionDenied, refusal);

        var validation = SchemaCheck.Validate(contract.InputSchema, args);
        if (validation != null) return ToolError(ErrorCodes.InvalidInput, validation + " Nothing ran.");
        var semantic = contract.Name switch
        {
            "horizun_c3d_surface" => SurfaceInputs.Validate(args),
            "horizun_c3d_grading" => GradingInputs.Validate(args),
            "horizun_c3d_feature_line" => FeatureLineInputs.Validate(args),
            _ => ToolRules.Validate(contract.Name, args),
        };
        if (semantic != null) return ToolError(ErrorCodes.InvalidInput, semantic + " Nothing ran.");

        if (contract.Command == null) return ServerTool(name, args);

        DiscoveryRecord rec;
        try
        {
            rec = PluginClient.Resolve(_target);
            PluginClient.CheckCompatible(rec, contract.Command);
        }
        catch (TargetException e) { return ToolError(e.Code, e.Message, e.Detail); }

        var wireId = Guid.NewGuid().ToString("N");
        _inflight[mcpRequestId] = (rec, wireId);
        try
        {
            var timeout = name == "horizun_c3d_health" ? 30_000 : 600_000;
            var reply = PluginClient.Send(rec, wireId, contract.Command, args, timeout);
            return FromReply(reply, rec);
        }
        catch (TargetException e) { return ToolError(e.Code, e.Message, e.Detail); }
        finally { _inflight.TryRemove(mcpRequestId, out _); }
    }

    private static JsonObject FromReply(JsonObject reply, DiscoveryRecord rec)
    {
        var target = new JsonObject { ["civil3d_year"] = rec.Year, ["pid"] = rec.Pid };
        if (reply["success"]?.GetValue<bool>() == true)
        {
            var data = reply["data"] as JsonObject ?? new JsonObject();
            var structured = (JsonObject)data.DeepClone();
            if (reply["host_messages"] is JsonArray hm) structured["host_messages"] = hm.DeepClone();
            if (reply["bridge_queue"] is JsonObject q) structured["bridge_queue"] = q.DeepClone();
            structured["target"] = target;
            return ToolOk(structured);
        }
        var detail = reply["detail"] as JsonObject;
        var err = ToolError(Hz.Str(reply, "code") ?? ErrorCodes.Internal, Hz.Str(reply, "error") ?? "Civil 3D reported a failure.", detail);
        var sc = (JsonObject)err["structuredContent"]!;
        if (reply["host_messages"] is JsonArray hm2) sc["host_messages"] = hm2.DeepClone();
        if (reply["bridge_queue"] is JsonObject q2) sc["bridge_queue"] = q2.DeepClone();
        sc["target"] = target;
        err["content"] = Text(sc);
        return err;
    }

    private JsonObject ServerTool(string name, JsonObject args) => name switch
    {
        "horizun_c3d_target" => TargetTool.Run(_target, args),
        _ => ToolError(ErrorCodes.Unsupported, "Server tool '" + name + "' is not implemented."),
    };

    private void Cancel(JsonObject? p)
    {
        var id = p?["requestId"]?.ToJsonString();
        if (id == null || !_inflight.TryGetValue(id, out var x)) return;
        Task.Run(() => PluginClient.CancelQueued(x.Rec, x.WireId));
    }

    // ---- result helpers ---------------------------------------------------

    public static JsonObject ToolOk(JsonObject structured) => new()
    {
        ["content"] = Text(structured),
        ["structuredContent"] = structured,
        ["isError"] = false,
    };

    public static JsonObject ToolError(string code, string message, JsonObject? detail = null)
    {
        var sc = new JsonObject { ["error"] = message, ["code"] = code };
        if (detail != null)
            foreach (var kv in detail) sc[kv.Key] = kv.Value?.DeepClone();
        return new JsonObject
        {
            ["content"] = Text(sc),
            ["structuredContent"] = sc,
            ["isError"] = true,
        };
    }

    private static JsonArray Text(JsonObject o) => new()
    {
        new JsonObject { ["type"] = "text", ["text"] = o.ToJsonString(Hz.Indented) },
    };

    private static JsonObject Result(JsonNode? id, JsonObject result) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id?.DeepClone(),
        ["result"] = result,
    };

    public static JsonObject Error(JsonNode? id, int code, string message) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id?.DeepClone(),
        ["error"] = new JsonObject { ["code"] = code, ["message"] = message },
    };
}
