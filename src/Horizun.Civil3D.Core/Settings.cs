// -----------------------------------------------------------------------------
// Horizun Civil 3D MCP - permission profiles.
//
// %USERPROFILE%\.horizun\civil3d\settings.json, read by BOTH the server and the
// plug-in (defence in depth: a server that forgot a check is still stopped by
// the plug-in):
//
//   {
//     "permission_profile": "safe_write",   // read_only | safe_write | full_write | unsafe_code
//     "allowed_tools": [],                  // non-empty = allowlist
//     "denied_tools": [],                   // always wins
//     "enable_execute_csharp": false,       // second key for the C# escape hatch
//     "paused": false,                      // owner kill switch: only health answers
//     "csharp_session_restore_profile": ""  // written ONLY by the "Canal C#" switch (CSharpChannel)
//   }
//
// No file = safe_write. A file that exists but cannot be parsed, or names an
// unknown profile, FAILS CLOSED to read_only: a typo must never widen access.
// -----------------------------------------------------------------------------
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Horizun.Civil3D.Core;

public enum PermissionProfile
{
    ReadOnly,
    SafeWrite,
    FullWrite,
    UnsafeCode,
}

public sealed class Settings
{
    public PermissionProfile Profile { get; init; } = PermissionProfile.SafeWrite;
    public IReadOnlyList<string> AllowedTools { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> DeniedTools { get; init; } = Array.Empty<string>();
    public bool EnableExecuteCSharp { get; init; }
    public bool Paused { get; init; }
    /// <summary>Why the effective settings are what they are (e.g. "file unreadable: read_only").</summary>
    public string Source { get; init; } = "defaults (no settings.json)";
    public bool FailedClosed { get; init; }

    public static string ProfileName(PermissionProfile p) => p switch
    {
        PermissionProfile.ReadOnly => "read_only",
        PermissionProfile.SafeWrite => "safe_write",
        PermissionProfile.FullWrite => "full_write",
        PermissionProfile.UnsafeCode => "unsafe_code",
        _ => "read_only",
    };

    public static PermissionProfile? ParseProfile(string? s) => s?.Trim().ToLowerInvariant() switch
    {
        "read_only" => PermissionProfile.ReadOnly,
        "safe_write" => PermissionProfile.SafeWrite,
        "full_write" => PermissionProfile.FullWrite,
        "unsafe_code" => PermissionProfile.UnsafeCode,
        _ => null,
    };

    public static Settings Load() => Load(HorizunPaths.SettingsFile());

    public static Settings Load(string path)
    {
        if (Directory.Exists(path)) return Closed(path, "is a directory, not a settings file");
        string text;
        try { text = File.ReadAllText(path); }
        catch (FileNotFoundException) { return new Settings(); }
        catch (DirectoryNotFoundException) { return new Settings(); }
        catch (Exception e) { return Closed(path, "could not be read (" + e.GetType().Name + ")"); }
        return Parse(text, path);
    }

    public static Settings Parse(string text, string origin = "settings.json")
    {
        JsonObject? o;
        try
        {
            using var json = JsonDocument.Parse(text);
            var problem = ValidateFields(json.RootElement);
            if (problem != null) return Closed(origin, problem);
            o = JsonNode.Parse(text) as JsonObject;
        }
        catch (Exception) { return Closed(origin, "is not valid JSON"); }
        if (o == null) return Closed(origin, "is not a JSON object");

        var profile = PermissionProfile.SafeWrite;
        var profileText = Hz.Str(o, "permission_profile");
        if (profileText != null)
        {
            var parsed = ParseProfile(profileText);
            if (parsed == null) return Closed(origin, "names an unknown permission_profile '" + profileText + "'");
            profile = parsed.Value;
        }

        return new Settings
        {
            Profile = profile,
            AllowedTools = ReadList(o, "allowed_tools"),
            DeniedTools = ReadList(o, "denied_tools"),
            EnableExecuteCSharp = Hz.Bool(o, "enable_execute_csharp") ?? false,
            Paused = Hz.Bool(o, "paused") ?? false,
            Source = origin,
        };
    }

    // A syntactically valid file can still disable an owner's intended controls
    // through a wrong type, null, duplicate key or spelling mistake. Reject it
    // before applying ANY defaults, including the default SafeWrite profile.
    private static string? ValidateFields(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return "is not a JSON object";
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in root.EnumerateObject())
        {
            if (!keys.Add(field.Name)) return "repeats field '" + field.Name + "'";
            switch (field.Name)
            {
                case "permission_profile":
                    if (field.Value.ValueKind != JsonValueKind.String)
                        return "permission_profile must be a string";
                    break;
                case "paused":
                case "enable_execute_csharp":
                    if (field.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                        return field.Name + " must be a boolean";
                    break;
                case CSharpChannel.RestoreKey:
                    if (field.Value.ValueKind != JsonValueKind.String || ParseProfile(field.Value.GetString()) == null)
                        return CSharpChannel.RestoreKey + " must name a permission profile";
                    break;
                case "allowed_tools":
                case "denied_tools":
                    if (field.Value.ValueKind != JsonValueKind.Array)
                        return field.Name + " must be an array of non-empty strings";
                    foreach (var item in field.Value.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
                            return field.Name + " must contain only non-empty strings";
                        // A misspelled denied_tools entry would block nothing, so unknown names are refused.
                        var tool = item.GetString()!.Trim();
                        if (!Contract.All.Any(c => string.Equals(c.Name, tool, StringComparison.Ordinal)))
                            return field.Name + " names '" + tool + "', which is not a tool (names are exact and lower-case, e.g. horizun_c3d_cleanup)";
                    }
                    break;
                default:
                    return "contains unknown field '" + field.Name + "'";
            }
        }
        return null;
    }

    private static Settings Closed(string origin, string why) => new()
    {
        Profile = PermissionProfile.ReadOnly,
        Source = origin + " " + why + "; failing closed to read_only",
        FailedClosed = true,
    };

    private static IReadOnlyList<string> ReadList(JsonObject o, string key)
    {
        if (o[key] is not JsonArray a) return Array.Empty<string>();
        return a.Select(n => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null)
                .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()).ToList();
    }

    public static ToolEffect MaxEffect(PermissionProfile p) => p switch
    {
        PermissionProfile.ReadOnly => ToolEffect.HostState,
        PermissionProfile.SafeWrite => ToolEffect.SafeWrite,
        PermissionProfile.FullWrite => ToolEffect.FullWrite,
        PermissionProfile.UnsafeCode => ToolEffect.UnsafeCode,
        _ => ToolEffect.HostState,
    };

    /// <summary>
    /// The single permission rule. Order: pause, denied list, allowlist, profile vs
    /// effect, C# second key. Returns null when allowed, else the refusal sentence.
    /// </summary>
    public string? Refusal(string toolName, ToolEffect effect)
    {
        if (Paused && toolName != "horizun_c3d_health")
            return "The Horizun Civil 3D bridge is PAUSED by its owner (settings.json \"paused\": true). Only " +
                   "horizun_c3d_health answers. Nothing ran.";
        if (DeniedTools.Contains(toolName, StringComparer.Ordinal))
            return "'" + toolName + "' is listed in denied_tools in " + HorizunPaths.SettingsFile() + ". Nothing ran.";
        if (AllowedTools.Count > 0 && !AllowedTools.Contains(toolName, StringComparer.Ordinal) && toolName != "horizun_c3d_health")
            return "'" + toolName + "' is not in the allowed_tools allowlist in " + HorizunPaths.SettingsFile() + ". Nothing ran.";
        if (effect > MaxEffect(Profile))
            return "The permission profile is '" + ProfileName(Profile) + "' (" + Source + "), which does not allow " +
                   EffectWords(effect) + ". Nothing ran. The drawing owner can raise it by setting \"permission_profile\": \"" +
                   ProfileName(RequiredProfile(effect)) + "\" in " + HorizunPaths.SettingsFile() + ".";
        if (effect == ToolEffect.UnsafeCode && !EnableExecuteCSharp)
            return "Arbitrary C# is off: it needs both \"permission_profile\": \"unsafe_code\" and \"enable_execute_csharp\": true " +
                   "in " + HorizunPaths.SettingsFile() + ", set by the drawing owner. Nothing ran.";
        return null;
    }

    public static PermissionProfile RequiredProfile(ToolEffect e) => e switch
    {
        ToolEffect.Read or ToolEffect.HostState => PermissionProfile.ReadOnly,
        ToolEffect.SafeWrite => PermissionProfile.SafeWrite,
        ToolEffect.FullWrite => PermissionProfile.FullWrite,
        _ => PermissionProfile.UnsafeCode,
    };

    private static string EffectWords(ToolEffect e) => e switch
    {
        ToolEffect.SafeWrite => "drawing edits",
        ToolEffect.FullWrite => "full writes (saving or opening drawings, erasing objects, deleting layouts, purging, plotting to PDF, publishing data shortcuts)",
        ToolEffect.UnsafeCode => "arbitrary code",
        _ => "this call",
    };

    public JsonObject Describe() => new()
    {
        ["permission_profile"] = ProfileName(Profile),
        ["paused"] = Paused,
        ["enable_execute_csharp"] = EnableExecuteCSharp,
        ["allowed_tools"] = Hz.Strings(AllowedTools),
        ["denied_tools"] = Hz.Strings(DeniedTools),
        ["source"] = Source,
        ["failed_closed"] = FailedClosed,
        ["settings_path"] = HorizunPaths.SettingsFile(),
    };
}
