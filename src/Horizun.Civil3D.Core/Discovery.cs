// -----------------------------------------------------------------------------
// Horizun Civil 3D MCP - how the server finds a running Civil 3D.
//
// Each plug-in publishes discovery\civil3d-<year>-<pid>.json when its pipe is
// listening and deletes it on shutdown. The file carries the pipe name, a
// 256-bit auth token, the contract hash and the command list. It is written
// atomically (temp + move) and ACL'd to the current user only, because the
// token is the only thing standing between another local process and the
// drawing.
//
// A Civil 3D that crashed leaves its file behind; readers treat a file whose
// process is gone (or is no longer acad.exe) as stale and the server sweeps it.
// -----------------------------------------------------------------------------
using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json.Nodes;

namespace Horizun.Civil3D.Core;

public sealed class DiscoveryRecord
{
    public const int Schema = 1;

    public int Year { get; init; }
    public int Pid { get; init; }
    public string PipeName { get; init; } = "";
    public string AuthToken { get; init; } = "";
    public string InstanceId { get; init; } = "";
    public DateTime StartedUtc { get; init; }
    public int ProtocolVersion { get; init; }
    public string ContractHash { get; init; } = "";
    public string PluginVersion { get; init; } = "";
    public string AcadVersion { get; init; } = "";
    public IReadOnlyList<string> Commands { get; init; } = Array.Empty<string>();
    public string FilePath { get; init; } = "";
    public int SchemaVersion { get; init; } = Schema;

    public JsonObject ToJson() => new()
    {
        ["schema"] = SchemaVersion,
        ["host"] = "civil3d",
        ["civil3d_year"] = Year,
        ["acadver"] = AcadVersion,
        ["pid"] = Pid,
        ["pipe_name"] = PipeName,
        ["auth_token"] = AuthToken,
        ["instance_id"] = InstanceId,
        ["started_utc"] = StartedUtc.ToString("o"),
        ["protocol_version"] = ProtocolVersion,
        ["contract_hash"] = ContractHash,
        ["plugin_version"] = PluginVersion,
        ["commands"] = Hz.Strings(Commands),
    };

    public static DiscoveryRecord? FromJson(JsonObject o, string path)
    {
        if (Hz.Str(o, "host") != "civil3d") return null;
        var pid = Hz.Int(o, "pid");
        var year = Hz.Int(o, "civil3d_year");
        var pipe = Hz.Str(o, "pipe_name");
        var token = Hz.Str(o, "auth_token");
        if (pid == null || year == null || string.IsNullOrEmpty(pipe) || string.IsNullOrEmpty(token)) return null;
        DateTime.TryParse(Hz.Str(o, "started_utc"), null, System.Globalization.DateTimeStyles.RoundtripKind, out var started);
        var cmds = (o["commands"] as JsonArray)?.Select(n => n?.GetValue<string>() ?? "").Where(s => s.Length > 0).ToList()
                   ?? new List<string>();
        return new DiscoveryRecord
        {
            SchemaVersion = Hz.Int(o, "schema") ?? 0,
            Year = year.Value,
            Pid = pid.Value,
            PipeName = pipe!,
            AuthToken = token!,
            InstanceId = Hz.Str(o, "instance_id") ?? "",
            StartedUtc = started,
            ProtocolVersion = Hz.Int(o, "protocol_version") ?? 0,
            ContractHash = Hz.Str(o, "contract_hash") ?? "",
            PluginVersion = Hz.Str(o, "plugin_version") ?? "",
            AcadVersion = Hz.Str(o, "acadver") ?? "",
            Commands = cmds,
            FilePath = path,
        };
    }
}

public static class Discovery
{
    public const string FilePrefix = "civil3d-";

    public static string FileName(int year, int pid) => $"{FilePrefix}{year}-{pid}.json";

    /// <summary>True for exactly "civil3d-&lt;4-digit year&gt;-&lt;pid&gt;.json" - nothing else is ever swept.</summary>
    public static bool IsDiscoveryFileName(string fileName)
    {
        if (!fileName.StartsWith(FilePrefix, StringComparison.Ordinal) || !fileName.EndsWith(".json", StringComparison.Ordinal))
            return false;
        var core = fileName.Substring(FilePrefix.Length, fileName.Length - FilePrefix.Length - 5);
        var parts = core.Split('-');
        return parts.Length == 2 && parts[0].Length == 4 && parts[0].All(char.IsDigit)
               && parts[1].Length > 0 && parts[1].All(char.IsDigit);
    }

    public static string Write(DiscoveryRecord r, string? dir = null)
    {
        dir ??= HorizunPaths.DiscoveryDir();
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, FileName(r.Year, r.Pid));
        var tmp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(tmp, r.ToJson().ToJsonString(Hz.Indented));
        RestrictToCurrentUser(tmp);
        RuntimeCompat.MoveReplacing(tmp, path);
        return path;
    }

    public static void Delete(int year, int pid, string? dir = null)
    {
        dir ??= HorizunPaths.DiscoveryDir();
        try { File.Delete(Path.Combine(dir, FileName(year, pid))); } catch { /* best effort on shutdown */ }
    }

    public static List<DiscoveryRecord> ReadAll(string? dir = null)
    {
        dir ??= HorizunPaths.DiscoveryDir();
        var list = new List<DiscoveryRecord>();
        if (!Directory.Exists(dir)) return list;
        foreach (var f in Directory.GetFiles(dir, FilePrefix + "*.json"))
        {
            if (!IsDiscoveryFileName(Path.GetFileName(f))) continue;
            try
            {
                if (JsonNode.Parse(File.ReadAllText(f)) is JsonObject o && DiscoveryRecord.FromJson(o, f) is { } r)
                    list.Add(r);
            }
            catch { /* half-written or foreign file: ignored, never trusted */ }
        }
        return list;
    }

    /// <summary>The process still exists, is acad.exe, and started before the file was written.</summary>
    public static bool IsAlive(DiscoveryRecord r)
    {
        try
        {
            using var p = Process.GetProcessById(r.Pid);
            if (!p.ProcessName.Equals("acad", StringComparison.OrdinalIgnoreCase)) return false;
            if (r.StartedUtc != default && p.StartTime.ToUniversalTime() > r.StartedUtc.AddSeconds(5)) return false; // pid reused
            return !p.HasExited;
        }
        catch { return false; }
    }

    /// <summary>Delete discovery files whose Civil 3D is gone. Returns how many were removed.</summary>
    public static int SweepStale(string? dir = null)
    {
        var n = 0;
        foreach (var r in ReadAll(dir))
        {
            if (IsAlive(r)) continue;
            try { File.Delete(r.FilePath); n++; } catch { }
        }
        return n;
    }

    private static void RestrictToCurrentUser(string path)
    {
#if NET48
        if (!RuntimeCompat.IsWindows) return;
#else
        if (!OperatingSystem.IsWindows()) return;
#endif
        try
        {
            var fi = new FileInfo(path);
            var acl = new FileSecurity();
            acl.SetAccessRuleProtection(true, false);
            var me = WindowsIdentity.GetCurrent().User!;
            acl.AddAccessRule(new FileSystemAccessRule(me, FileSystemRights.FullControl, AccessControlType.Allow));
            fi.SetAccessControl(acl);
        }
        catch
        {
            // The file still lives under the user's own profile; the token is the guard.
        }
    }
}
