// -----------------------------------------------------------------------------
// Horizun Civil 3D MCP - small JSON and hashing helpers shared by both halves.
//
// Canonical JSON (object keys sorted, arrays kept in order) is what every hash
// in the bridge is computed over: the contract hash, plan hashes for
// confirmation tokens. Array order is deliberately NOT normalised - a list of
// operations applied in another order is another plan.
// -----------------------------------------------------------------------------
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Horizun.Civil3D.Core;

public static class Hz
{
    // Live finding (Civil 3D 2025, v0.3.0): inside acad.exe reflection-based JSON serialization is
    // DISABLED by default, so a JsonValue built by JsonArray.Add<T>/JsonValue.Create<T> ("customized"
    // value) throws "must specify a TypeInfoResolver" at serialization time. Every serialization in the
    // bridge goes through these options, which name the resolver explicitly - that works whatever the
    // host's default is.
    public static readonly JsonSerializerOptions Compact = new()
    {
        WriteIndented = false,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
    };
    public static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
    };

    public static string Canonical(JsonNode? node)
    {
        var sb = new StringBuilder();
        WriteCanonical(node, sb);
        return sb.ToString();
    }

    private static void WriteCanonical(JsonNode? node, StringBuilder sb)
    {
        switch (node)
        {
            case null:
                sb.Append("null");
                break;
            case JsonObject o:
                sb.Append('{');
                var first = true;
                foreach (var kv in o.OrderBy(k => k.Key, StringComparer.Ordinal))
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append(JsonSerializer.Serialize(kv.Key, Compact)).Append(':');
                    WriteCanonical(kv.Value, sb);
                }
                sb.Append('}');
                break;
            case JsonArray a:
                sb.Append('[');
                for (var i = 0; i < a.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    WriteCanonical(a[i], sb);
                }
                sb.Append(']');
                break;
            default:
                sb.Append(node.ToJsonString(Compact));
                break;
        }
    }

    public static string Sha256Hex(string text)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string NewToken(int bytes = 32)
    {
        var buf = RandomNumberGenerator.GetBytes(bytes);
        return Convert.ToHexString(buf).ToLowerInvariant();
    }

    /// <summary>Constant-time comparison for secrets (pipe auth token).</summary>
    public static bool SecretEquals(string? a, string? b)
    {
        if (a == null || b == null) return false;
        var x = Encoding.UTF8.GetBytes(a);
        var y = Encoding.UTF8.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(x, y);
    }

    public static JsonObject Obj(params (string Key, JsonNode? Value)[] items)
    {
        var o = new JsonObject();
        foreach (var (k, v) in items) o[k] = v;
        return o;
    }

    public static JsonArray Arr(IEnumerable<JsonNode?> items)
    {
        var a = new JsonArray();
        foreach (var i in items) a.Add(i);
        return a;
    }

    /// <summary>Deep copy of an object without the given keys (e.g. the request minus its envelope fields).</summary>
    public static JsonObject Without(JsonObject o, params string[] keys)
    {
        var r = new JsonObject();
        foreach (var kv in o)
            if (!keys.Contains(kv.Key)) r[kv.Key] = kv.Value?.DeepClone();
        return r;
    }

    public static JsonArray Strings(IEnumerable<string> items) => Arr(items.Select(s => (JsonNode?)JsonValue.Create(s)));

    public static string? Str(JsonObject? o, string key)
    {
        if (o == null || !o.TryGetPropertyValue(key, out var v) || v == null) return null;
        return v is JsonValue jv && jv.TryGetValue<string>(out var s) ? s : v.ToJsonString(Compact);
    }

    public static bool? Bool(JsonObject? o, string key)
    {
        if (o == null || !o.TryGetPropertyValue(key, out var v) || v == null) return null;
        return v is JsonValue jv && jv.TryGetValue<bool>(out var b) ? b : null;
    }

    public static int? Int(JsonObject? o, string key)
    {
        if (o == null || !o.TryGetPropertyValue(key, out var v) || v == null) return null;
        if (v is JsonValue jv)
        {
            if (jv.TryGetValue<int>(out var i)) return i;
            if (jv.TryGetValue<long>(out var l) && l is >= int.MinValue and <= int.MaxValue) return (int)l;
            if (AsDouble(jv) is { } d && Math.Abs(d - Math.Round(d)) < 1e-9 && Math.Abs(d) <= int.MaxValue) return (int)Math.Round(d);
        }
        return null;
    }

    public static double? Num(JsonObject? o, string key)
    {
        if (o == null || !o.TryGetPropertyValue(key, out var v) || v == null) return null;
        return AsDouble(v);
    }

    /// <summary>
    /// A JSON number as double, whatever CLR type backs the node (parsed JsonElement,
    /// or an in-memory int/long/decimal - TryGetValue&lt;double&gt; fails on those).
    /// </summary>
    public static double? AsDouble(JsonNode? v)
    {
        if (v is not JsonValue jv || jv.GetValueKind() != System.Text.Json.JsonValueKind.Number) return null;
        return double.TryParse(jv.ToJsonString(Compact), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    /// <summary>
    /// A finite double as JSON, or null. NaN/Infinity are never emitted as numbers
    /// and never silently turned into 0: absent is absent.
    /// </summary>
    public static JsonNode? Finite(double value, int? round = null)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return null;
        return JsonValue.Create(round.HasValue ? Math.Round(value, round.Value) : value);
    }

    /// <summary>Wildcard match (* and ?), case-insensitive. Null/empty pattern matches all.</summary>
    public static bool Like(string? text, string? pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return true;
        text ??= "";
        return LikeAt(text.ToUpperInvariant(), 0, pattern.ToUpperInvariant(), 0);
    }

    private static bool LikeAt(string t, int ti, string p, int pi)
    {
        while (pi < p.Length)
        {
            var c = p[pi];
            if (c == '*')
            {
                while (pi < p.Length && p[pi] == '*') pi++;
                if (pi == p.Length) return true;
                for (var k = ti; k <= t.Length; k++)
                    if (LikeAt(t, k, p, pi)) return true;
                return false;
            }
            if (ti >= t.Length) return false;
            if (c != '?' && c != t[ti]) return false;
            ti++;
            pi++;
        }
        return ti == t.Length;
    }
}
