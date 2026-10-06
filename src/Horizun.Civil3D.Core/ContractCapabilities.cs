using System.Text.Json.Nodes;

namespace Horizun.Civil3D.Core;

/// <summary>A read-only view of the declared contract. No Civil 3D connection or live claim is made.</summary>
public static class ContractCapabilities
{
    public static JsonObject Build(Settings settings, JsonObject? filters = null)
    {
        filters ??= new JsonObject();
        var toolFilter = Hz.Str(filters, "tool");
        var effectFilter = Hz.Str(filters, "effect");
        var query = Hz.Str(filters, "query");
        var includeSchema = Hz.Bool(filters, "include_schema") == true;
        var output = new JsonArray();
        var returnedActions = 0;
        var totalActions = Contract.All.Sum(c => Actions(c).Count);
        var standaloneTools = Contract.All.Count(c => Actions(c).Count == 0);
        var returnedStandaloneTools = 0;

        foreach (var tool in Contract.All.OrderBy(c => c.Name, StringComparer.Ordinal))
        {
            if (toolFilter != null && !string.Equals(toolFilter, tool.Name, StringComparison.Ordinal)) continue;
            var schema = tool.InputSchema;
            var names = Actions(tool);
            var toolMatchesQuery = string.IsNullOrWhiteSpace(query) ||
                tool.Name.Contains(query!, StringComparison.OrdinalIgnoreCase) ||
                tool.Title.Contains(query!, StringComparison.OrdinalIgnoreCase) ||
                tool.Description.Contains(query!, StringComparison.OrdinalIgnoreCase);
            var specs = ToolRules.Specs(tool.Name);
            var actions = new JsonArray();
            foreach (var name in names)
            {
                var effect = tool.EffectFor(new JsonObject { ["action"] = name });
                if (effectFilter != null && !string.Equals(effectFilter, EffectName(effect), StringComparison.Ordinal)) continue;
                if (!toolMatchesQuery && !name.Contains(query!, StringComparison.OrdinalIgnoreCase)) continue;

                var spec = specs != null && specs.TryGetValue(name, out var found) ? found : null;
                var required = spec == null ? Required(schema) :
                    new[] { "action" }.Concat(spec.Required).Concat(spec.IsWrite ? new[] { "target_document" } : Array.Empty<string>())
                        .Distinct(StringComparer.Ordinal).ToArray();
                var optional = spec == null ? Optional(schema) :
                    spec.Optional.Concat(spec.IsWrite ? new[] { "dry_run", "confirmation_token" } : Array.Empty<string>())
                        .Concat(spec.IsWrite ? Array.Empty<string>() : new[] { "target_document" })
                        .Where(field => !required.Contains(field, StringComparer.Ordinal)).Distinct(StringComparer.Ordinal).ToArray();
                var refusal = settings.Refusal(tool.Name, effect);
                actions.Add(new JsonObject
                {
                    ["name"] = name,
                    ["effect"] = EffectName(effect),
                    ["permission_allowed"] = refusal == null,
                    ["permission_refusal"] = refusal,
                    ["required_fields"] = Hz.Strings(required),
                    ["optional_fields"] = Hz.Strings(optional),
                    ["fields_scope"] = spec == null ? "tool_schema" : "action_rule",
                });
                returnedActions++;
            }
            if (names.Count > 0 && actions.Count == 0) continue;
            if (names.Count == 0 && effectFilter != null && effectFilter != EffectName(tool.Effect)) continue;
            if (names.Count == 0 && !toolMatchesQuery) continue;
            if (names.Count == 0) returnedStandaloneTools++;

            var baseRefusal = settings.Refusal(tool.Name, tool.Effect);
            var item = new JsonObject
            {
                ["name"] = tool.Name,
                ["title"] = tool.Title,
                ["description"] = tool.Description,
                ["server_side"] = tool.Command == null,
                ["effect"] = EffectName(tool.Effect),
                ["max_effect"] = EffectName(tool.MaxEffect),
                ["permission_allowed"] = baseRefusal == null,
                ["permission_refusal"] = baseRefusal,
                ["action_count"] = names.Count,
                ["actions"] = actions,
                ["required_fields"] = Hz.Strings(Required(schema)),
                ["optional_fields"] = Hz.Strings(Optional(schema)),
            };
            if (includeSchema) item["input_schema"] = schema.DeepClone();
            output.Add(item);
        }

        return new JsonObject
        {
            ["source"] = "declared_contract",
            ["live_verified"] = false,
            ["host_checked"] = false,
            ["permission_scope"] = "settings_only",
            ["contract_hash"] = Contract.Hash,
            ["permission_profile"] = Settings.ProfileName(settings.Profile),
            ["paused"] = settings.Paused,
            ["failed_closed"] = settings.FailedClosed,
            ["total_tool_count"] = Contract.All.Count,
            ["total_action_count"] = totalActions,
            ["total_standalone_tool_count"] = standaloneTools,
            ["total_operation_count"] = totalActions + standaloneTools,
            ["returned_tool_count"] = output.Count,
            ["returned_action_count"] = returnedActions,
            ["returned_operation_count"] = returnedActions + returnedStandaloneTools,
            ["tools"] = output,
        };
    }

    private static List<string> Actions(ToolContract tool) =>
        (tool.InputSchema["properties"]?["action"]?["enum"] as JsonArray)?
            .Select(n => n?.GetValue<string>() ?? "").Where(n => n.Length > 0).ToList() ?? new List<string>();

    private static string[] Required(JsonObject schema) =>
        (schema["required"] as JsonArray)?.Select(n => n?.GetValue<string>() ?? "").Where(n => n.Length > 0).ToArray()
        ?? Array.Empty<string>();

    private static string[] Optional(JsonObject schema)
    {
        var required = Required(schema);
        return (schema["properties"] as JsonObject)?.Select(kv => kv.Key)
            .Where(name => !required.Contains(name, StringComparer.Ordinal)).ToArray() ?? Array.Empty<string>();
    }

    public static string EffectName(ToolEffect effect) => effect switch
    {
        ToolEffect.Read => "read",
        ToolEffect.HostState => "host_state",
        ToolEffect.SafeWrite => "safe_write",
        ToolEffect.FullWrite => "full_write",
        ToolEffect.UnsafeCode => "unsafe_code",
        _ => "unknown",
    };
}
