// -----------------------------------------------------------------------------
// Horizun Civil 3D MCP - input validation BEFORE anything reaches Civil 3D.
//
// The subset of JSON Schema the contract uses: object/properties/required,
// additionalProperties=false, type (single or union), enum, minimum/maximum,
// array items. A request that does not fit its schema is refused here with the
// exact field, so a typo never becomes a half-understood command.
// -----------------------------------------------------------------------------
using System.Text.Json;
using System.Text.Json.Nodes;
using Horizun.Civil3D.Core;

namespace Horizun.Civil3D.Server;

internal static class SchemaCheck
{
    /// <summary>Null when valid; otherwise the first problem found.</summary>
    public static string? Validate(JsonObject schema, JsonNode? value, string path = "arguments")
    {
        var types = Types(schema);
        if (types.Count > 0 && !types.Any(t => Is(value, t)))
            return path + " must be " + string.Join(" or ", types) + " (got " + Kind(value) + ").";

        if (schema["enum"] is JsonArray en && value != null)
        {
            var text = value.ToJsonString();
            if (!en.Any(e => e?.ToJsonString() == text))
                return path + " must be one of " + string.Join(", ", en.Select(e => e?.ToJsonString())) + " (got " + text + ").";
        }

        if (Hz.AsDouble(value) is { } num)
        {
            if (Hz.Num(schema, "minimum") is { } min && num < min) return path + " must be >= " + min + ".";
            if (Hz.Num(schema, "maximum") is { } max && num > max) return path + " must be <= " + max + ".";
        }

        if (value is JsonObject obj)
        {
            var props = schema["properties"] as JsonObject;
            if (schema["required"] is JsonArray req)
                foreach (var r in req)
                {
                    var key = r?.GetValue<string>();
                    if (key != null && (!obj.TryGetPropertyValue(key, out var v) || v == null))
                        return path + "." + key + " is required.";
                }
            foreach (var kv in obj)
            {
                if (props != null && props[kv.Key] is JsonObject sub)
                {
                    if (kv.Value == null) continue; // explicit null = absent
                    var err = Validate(sub, kv.Value, path + "." + kv.Key);
                    if (err != null) return err;
                }
                else if (schema["additionalProperties"] is JsonValue ap && ap.TryGetValue<bool>(out var allowed) && !allowed)
                {
                    var known = props == null ? "" : " Known fields: " + string.Join(", ", props.Select(p => p.Key)) + ".";
                    return path + "." + kv.Key + " is not a recognised field." + known;
                }
            }
        }

        if (value is JsonArray arr && schema["items"] is JsonObject items)
            for (var i = 0; i < arr.Count; i++)
            {
                var err = Validate(items, arr[i], path + "[" + i + "]");
                if (err != null) return err;
            }
        return null;
    }

    private static List<string> Types(JsonObject schema) => schema["type"] switch
    {
        JsonValue v when v.TryGetValue<string>(out var s) => new List<string> { s },
        JsonArray a => a.Select(x => x?.GetValue<string>() ?? "").Where(s => s.Length > 0).ToList(),
        _ => new List<string>(),
    };

    private static bool Is(JsonNode? v, string type) => type switch
    {
        "object" => v is JsonObject,
        "array" => v is JsonArray,
        "string" => v is JsonValue s && s.GetValueKind() == JsonValueKind.String,
        "boolean" => v is JsonValue b && (b.GetValueKind() == JsonValueKind.True || b.GetValueKind() == JsonValueKind.False),
        "number" => v is JsonValue n && n.GetValueKind() == JsonValueKind.Number,
        "integer" => Hz.AsDouble(v) is { } d && Math.Abs(d - Math.Round(d)) < 1e-9,
        "null" => v == null,
        _ => true,
    };

    private static string Kind(JsonNode? v) => v switch
    {
        null => "null",
        JsonObject => "object",
        JsonArray => "array",
        JsonValue x => x.GetValueKind().ToString().ToLowerInvariant(),
        _ => "unknown",
    };
}
