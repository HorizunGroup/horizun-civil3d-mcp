// -----------------------------------------------------------------------------
// Horizun Civil 3D MCP - declarative per-action validation for the action-based
// tools (shared by the MCP server and the plug-in).
//
// Each tool declares, per action: required fields, optional fields, effect, and
// an optional extra check. The generic rule then enforces: known action, no
// unknown field for that action, required fields present, writes name
// target_document, dry_run/confirmation_token only on writes.
// -----------------------------------------------------------------------------
using System.Text.Json.Nodes;

namespace Horizun.Civil3D.Core;

public sealed record ActionSpec(string[] Required, string[] Optional, ToolEffect Effect = ToolEffect.Read, Func<JsonObject, string?>? Extra = null)
{
    public bool IsWrite => Effect >= ToolEffect.SafeWrite;
}

public static class ToolRules
{
    private static readonly Dictionary<string, IReadOnlyDictionary<string, ActionSpec>> Tools = new(StringComparer.Ordinal);

    public static IReadOnlyDictionary<string, ActionSpec> Register(string tool, IReadOnlyDictionary<string, ActionSpec> specs)
    {
        lock (Tools) Tools[tool] = specs;
        return specs;
    }

    public static IReadOnlyDictionary<string, ActionSpec>? Specs(string tool)
    {
        Contract.EnsureRules();
        lock (Tools) return Tools.TryGetValue(tool, out var s) ? s : null;
    }

    public static Dictionary<string, ToolEffect> Effects(IReadOnlyDictionary<string, ActionSpec> specs) =>
        specs.Where(kv => kv.Value.Effect != ToolEffect.Read).ToDictionary(kv => kv.Key, kv => kv.Value.Effect);

    public static string? Validate(string tool, JsonObject args)
    {
        var specs = Specs(tool);
        if (specs == null) return null;
        var action = Hz.Str(args, "action");
        if (action == null || !specs.TryGetValue(action, out var spec))
            return "action must be one of: " + string.Join(", ", specs.Keys) + ".";
        var allowed = new HashSet<string>(spec.Required.Concat(spec.Optional)) { "action", "target_document" };
        if (spec.IsWrite) { allowed.Add("dry_run"); allowed.Add("confirmation_token"); }
        if (args.FirstOrDefault(kv => kv.Value != null && !allowed.Contains(kv.Key)) is { Key: { } extra })
            return "Field '" + extra + "' is not used by action " + action + ". Fields for " + action + ": " +
                   string.Join(", ", spec.Required.Concat(spec.Optional)) + ".";
        foreach (var r in spec.Required)
            if (args[r] == null || (args[r] is JsonValue v && v.TryGetValue<string>(out var s) && string.IsNullOrWhiteSpace(s)))
                return action + " requires " + r + ".";
        if (spec.IsWrite && string.IsNullOrWhiteSpace(Hz.Str(args, "target_document"))) return "Writes require target_document.";
        return spec.Extra?.Invoke(args);
    }
}

/// <summary>Small reusable field checks returning a refusal sentence or null.</summary>
public static class V
{
    public static bool IsHex(string? h) => h != null && h.Length is > 0 and <= 16 && h.All(Uri.IsHexDigit);

    public static string? Hex(JsonObject a, string key) =>
        a[key] != null && !IsHex(Hz.Str(a, key)) ? key + " must be a hexadecimal handle." : null;

    public static string? HexList(JsonObject a, string key, int max = 1000)
    {
        if (a[key] == null) return null;
        if (a[key] is not JsonArray arr || arr.Count == 0 || arr.Count > max) return key + " must list 1 to " + max + " handles.";
        if (arr.Any(n => !IsHex(n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null))) return key + " must contain hexadecimal handles.";
        if (arr.Select(n => n!.GetValue<string>().ToUpperInvariant()).Distinct().Count() != arr.Count) return key + " must not repeat.";
        return null;
    }

    public static string? Pos(JsonObject a, string key) =>
        a[key] != null && (Hz.Num(a, key) is not { } v || !double.IsFinite(v) || v <= 0) ? key + " must be finite and > 0." : null;

    public static string? NonNeg(JsonObject a, string key) =>
        a[key] != null && (Hz.Num(a, key) is not { } v || !double.IsFinite(v) || v < 0) ? key + " must be finite and >= 0." : null;

    public static string? Fin(JsonObject a, string key) =>
        a[key] != null && (Hz.Num(a, key) is not { } v || !double.IsFinite(v)) ? key + " must be a finite number." : null;

    public static string? Point(JsonObject a, string key, bool z = false)
    {
        if (a[key] == null) return null;
        if (a[key] is not JsonObject p || Hz.Num(p, "x") is not { } x || Hz.Num(p, "y") is not { } y || !double.IsFinite(x) || !double.IsFinite(y)
            || (z && p["z"] != null && (Hz.Num(p, "z") is not { } zz || !double.IsFinite(zz))))
            return key + " must be {x, y" + (z ? "[, z]" : "") + "} with finite drawing coordinates.";
        return null;
    }

    public static string? Points(JsonObject a, string key, int min, int max, bool z = false)
    {
        if (a[key] == null) return null;
        if (a[key] is not JsonArray arr || arr.Count < min || arr.Count > max) return key + " must contain " + min + " to " + max + " points.";
        for (var i = 0; i < arr.Count; i++)
            if (arr[i] is not JsonObject p || Hz.Num(p, "x") is not { } x || Hz.Num(p, "y") is not { } y || !double.IsFinite(x) || !double.IsFinite(y)
                || (p["z"] != null && (!z || Hz.Num(p, "z") is not { } zz || !double.IsFinite(zz))))
                return key + "[" + i + "] must be {x, y" + (z ? "[, z]" : "") + "} with finite coordinates.";
        return null;
    }

    public static string? Strings(JsonObject a, string key, int max = 1000)
    {
        if (a[key] == null) return null;
        if (a[key] is not JsonArray arr || arr.Count == 0 || arr.Count > max) return key + " must list 1 to " + max + " names.";
        if (arr.Any(n => n is not JsonValue v || !v.TryGetValue<string>(out var s) || string.IsNullOrWhiteSpace(s))) return key + " must contain non-empty strings.";
        return null;
    }

    public static string? OneOf(JsonObject a, string key, params string[] values) =>
        a[key] != null && !values.Contains(Hz.Str(a, key)) ? key + " must be one of " + string.Join(", ", values) + "." : null;

    public static string? Exactly1(JsonObject a, params string[] keys) =>
        keys.Count(k => a[k] != null) == 1 ? null : "Give exactly one of " + string.Join(", ", keys) + ".";

    public static string? First(params string?[] checks) => checks.FirstOrDefault(c => c != null);
}
