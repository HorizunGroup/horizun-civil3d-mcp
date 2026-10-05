// -----------------------------------------------------------------------------
// Horizun Civil 3D MCP - the C# channel switch used by the "Canal C#" ribbon
// button / HZ_CSHARP command (owner request, 2026-10-02).
//
// Only a PERSON turns it on (ribbon button or typed command inside Civil 3D, with
// a confirmation dialog); no MCP tool can call this. Turning it on writes the two
// keys execute_csharp needs and remembers the previous profile in
// "csharp_session_restore_profile"; turning it off - or the next Civil 3D start
// (EndSession) - restores that profile. An unreadable settings.json is never
// rewritten: the switch refuses and the file keeps failing closed.
// -----------------------------------------------------------------------------
using System.Text;
using System.Text.Json.Nodes;

namespace Horizun.Civil3D.Core;

public static class CSharpChannel
{
    public const string RestoreKey = "csharp_session_restore_profile";

    public static bool IsOn(Settings s) => !s.FailedClosed && !s.Paused && s.Profile == PermissionProfile.UnsafeCode && s.EnableExecuteCSharp;

    private static JsonObject ReadObject(string path)
    {
        if (!File.Exists(path)) return new JsonObject();
        var text = File.ReadAllText(path);
        var s = Settings.Parse(text, path);
        if (s.FailedClosed)
            throw new InvalidOperationException("settings.json is not valid (" + s.Source + "). Fix it by hand; the switch never rewrites an invalid file.");
        return (JsonObject)JsonNode.Parse(text)!;
    }

    private static void WriteObject(string path, JsonObject o)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, o.ToJsonString(Hz.Indented), new UTF8Encoding(false));
        if (File.Exists(path)) File.Replace(tmp, path, null); else File.Move(tmp, path);
    }

    /// <summary>Turn the channel on; returns the resulting settings.</summary>
    public static Settings Enable(string path)
    {
        var o = ReadObject(path);
        if (IsOn(Settings.Parse(o.ToJsonString(), path))) return Settings.Load(path);
        if (o[RestoreKey] == null) o[RestoreKey] = Hz.Str(o, "permission_profile") ?? Settings.ProfileName(PermissionProfile.SafeWrite);
        o["permission_profile"] = Settings.ProfileName(PermissionProfile.UnsafeCode);
        o["enable_execute_csharp"] = true;
        WriteObject(path, o);
        return Settings.Load(path);
    }

    /// <summary>Turn the channel off, restoring the profile saved by Enable (if any).</summary>
    public static Settings Disable(string path)
    {
        if (!File.Exists(path)) return Settings.Load(path);
        var o = ReadObject(path);
        o["enable_execute_csharp"] = false;
        if (Hz.Str(o, RestoreKey) is { } restore)
        {
            o["permission_profile"] = restore;
            o.Remove(RestoreKey);
        }
        WriteObject(path, o);
        return Settings.Load(path);
    }

    // ---- "Escritura completa" switch (HZ_FULLWRITE): same session rules, one shared restore key ----

    public static bool IsFullWriteOn(Settings s) => !s.FailedClosed && !s.Paused && s.Profile >= PermissionProfile.FullWrite;

    /// <summary>Raise the profile to full_write (never lowers an unsafe_code profile); remembers the previous profile.</summary>
    public static Settings EnableFullWrite(string path)
    {
        var o = ReadObject(path);
        if (IsFullWriteOn(Settings.Parse(o.ToJsonString(), path))) return Settings.Load(path);
        if (o[RestoreKey] == null) o[RestoreKey] = Hz.Str(o, "permission_profile") ?? Settings.ProfileName(PermissionProfile.SafeWrite);
        o["permission_profile"] = Settings.ProfileName(PermissionProfile.FullWrite);
        WriteObject(path, o);
        return Settings.Load(path);
    }

    /// <summary>Back below full_write: the remembered profile, else safe_write. Also turns the C# channel off.</summary>
    public static Settings DisableFullWrite(string path)
    {
        if (!File.Exists(path)) return Settings.Load(path);
        var o = ReadObject(path);
        var current = Settings.ParseProfile(Hz.Str(o, "permission_profile")) ?? PermissionProfile.SafeWrite;
        var restore = Settings.ParseProfile(Hz.Str(o, RestoreKey));
        var target = restore is { } r && r < PermissionProfile.FullWrite ? r
            : current >= PermissionProfile.FullWrite ? PermissionProfile.SafeWrite : current;
        o["permission_profile"] = Settings.ProfileName(target);
        o["enable_execute_csharp"] = false;
        o.Remove(RestoreKey);
        WriteObject(path, o);
        return Settings.Load(path);
    }

    /// <summary>At plug-in start: a channel opened by the switch does not survive the Civil 3D session.</summary>
    public static bool EndSession(string path)
    {
        if (!File.Exists(path)) return false;
        JsonObject o;
        try { o = ReadObject(path); } catch (InvalidOperationException) { return false; }
        if (o[RestoreKey] == null) return false;
        Disable(path);
        return true;
    }
}
