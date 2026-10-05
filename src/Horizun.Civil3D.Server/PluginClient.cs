// -----------------------------------------------------------------------------
// Horizun Civil 3D MCP - the server end of the pipe: find, pick and call a
// running Civil 3D.
//
// Targeting never guesses. With one live Civil 3D it is used; with several the
// call is refused as ambiguous until horizun_c3d_target picks one; a selected
// instance that has gone away is reported as gone, not silently replaced.
// Before every call the plug-in's contract hash and protocol must equal this
// server's: a mismatched pair (different builds) is refused, never "tried".
// -----------------------------------------------------------------------------
using System.IO.Pipes;
using System.Text.Json.Nodes;
using Horizun.Civil3D.Core;

namespace Horizun.Civil3D.Server;

internal enum TargetMode { Automatic, ByYear, ByPid }

internal sealed class TargetSelection
{
    public TargetMode Mode { get; private set; } = TargetMode.Automatic;
    public int? Year { get; private set; }
    public int? Pid { get; private set; }

    public static TargetSelection FromEnvironment()
    {
        var s = new TargetSelection();
        if (int.TryParse(Environment.GetEnvironmentVariable("HORIZUN_C3D_YEAR"), out var y)) s.SetYear(y);
        return s;
    }

    public void SetAuto() { Mode = TargetMode.Automatic; Year = null; Pid = null; }
    public void SetYear(int y) { Mode = TargetMode.ByYear; Year = y; Pid = null; }
    public void SetPid(int p) { Mode = TargetMode.ByPid; Pid = p; Year = null; }

    public string Describe() => Mode switch
    {
        TargetMode.ByYear => "year " + Year,
        TargetMode.ByPid => "pid " + Pid,
        _ => "automatic",
    };
}

internal sealed class TargetException : Exception
{
    public string Code { get; }
    public JsonObject? Detail { get; }
    public TargetException(string code, string message, JsonObject? detail = null) : base(message) { Code = code; Detail = detail; }
}

internal static class PluginClient
{
    public static Func<List<DiscoveryRecord>> ReadAll = () => Discovery.ReadAll();
    public static Func<DiscoveryRecord, bool> IsAlive = Discovery.IsAlive;

    public static DiscoveryRecord Resolve(TargetSelection sel)
    {
        var all = ReadAll();
        var alive = all.Where(IsAlive).ToList();
        JsonObject Listing() => new()
        {
            ["running_civil3d"] = Hz.Arr(alive.Select(r => (JsonNode?)new JsonObject { ["year"] = r.Year, ["pid"] = r.Pid })),
            ["selection"] = sel.Describe(),
        };

        switch (sel.Mode)
        {
            case TargetMode.ByPid:
                var byPid = all.FirstOrDefault(r => r.Pid == sel.Pid);
                if (byPid == null)
                    throw new TargetException(ErrorCodes.NoInstance, "The selected Civil 3D (pid " + sel.Pid + ") has not published a " +
                        "Horizun bridge. Call horizun_c3d_target to see what is running. Nothing ran.", Listing());
                if (!IsAlive(byPid))
                    throw new TargetException(ErrorCodes.NoInstance, "The selected Civil 3D (pid " + sel.Pid + ") is no longer running. " +
                        "It was NOT silently replaced by another instance: call horizun_c3d_target to choose. Nothing ran.", Listing());
                return byPid;
            case TargetMode.ByYear:
                var ofYear = alive.Where(r => r.Year == sel.Year).ToList();
                if (ofYear.Count == 1) return ofYear[0];
                if (ofYear.Count == 0)
                    throw new TargetException(ErrorCodes.NoInstance, "No running Civil 3D " + sel.Year + " has a Horizun bridge. Nothing ran.", Listing());
                throw new TargetException(ErrorCodes.Ambiguous, ofYear.Count + " Civil 3D " + sel.Year + " instances are running. " +
                    "Pick one with horizun_c3d_target pid=<pid>. Nothing ran.", Listing());
            default:
                if (alive.Count == 1) return alive[0];
                if (alive.Count == 0)
                    throw new TargetException(ErrorCodes.NoInstance,
                        "No running Civil 3D has published a Horizun bridge. Open Civil 3D (the Horizun.Civil3D bundle loads at " +
                        "startup). If Civil 3D is open, run HZ_STATUS in its command line to see why the bridge did not start. Nothing ran.",
                        Listing());
                throw new TargetException(ErrorCodes.Ambiguous, alive.Count + " Civil 3D instances are running. Pick one with " +
                    "horizun_c3d_target (year or pid). Nothing ran.", Listing());
        }
    }

    /// <summary>Refuse a plug-in from a different build before sending anything.</summary>
    public static void CheckCompatible(DiscoveryRecord r, string command)
    {
        if (r.ProtocolVersion != Contract.ProtocolVersion || !string.Equals(r.ContractHash, Contract.Hash, StringComparison.Ordinal))
            throw new TargetException(ErrorCodes.ContractMismatch,
                "The Civil 3D plug-in (v" + r.PluginVersion + ", contract " + r.ContractHash + ", protocol " + r.ProtocolVersion +
                ") and this server (contract " + Contract.Hash + ", protocol " + Contract.ProtocolVersion + ") come from " +
                "different builds. Reinstall both from the same release (scripts\\install.ps1), then restart Civil 3D and the " +
                "MCP client. Nothing ran.");
        if (!r.Commands.Contains(command))
            throw new TargetException(ErrorCodes.Unsupported,
                "The Civil 3D plug-in does not implement '" + command + "'. Nothing ran.");
    }

    public static JsonObject Send(DiscoveryRecord r, string wireId, string command, JsonObject parameters, int timeoutMs)
    {
        var req = Wire.Request(wireId, command, parameters, r.AuthToken);
        req["timeout_ms"] = timeoutMs;
        try
        {
            using var pipe = new NamedPipeClientStream(".", r.PipeName, PipeDirection.InOut, PipeOptions.None);
            pipe.Connect(5000);
            Wire.WriteLine(pipe, req);
            var readTask = Task.Run(() => Wire.ReadLine(pipe, Contract.MaxReplyBytes));
            if (!readTask.Wait(timeoutMs + 30_000))
                throw new TargetException(ErrorCodes.Timeout,
                    "No reply from Civil 3D within " + (timeoutMs / 1000 + 30) + " s. The command may still be running: check the " +
                    "drawing before repeating any write.");
            var line = readTask.Result ?? throw new TargetException(ErrorCodes.Transport,
                "Civil 3D closed the connection without replying. If this was a write, check the drawing before repeating it.");
            return JsonNode.Parse(line) as JsonObject
                   ?? throw new TargetException(ErrorCodes.Transport, "Civil 3D sent a reply that is not a JSON object.");
        }
        catch (TimeoutException)
        {
            throw new TargetException(ErrorCodes.Transport,
                "Could not connect to Civil 3D's bridge pipe within 5 s (Civil 3D " + r.Year + ", pid " + r.Pid + "). Nothing ran.");
        }
        catch (IOException e)
        {
            throw new TargetException(ErrorCodes.Transport,
                "The pipe to Civil 3D failed: " + e.Message + ". If this was a write, check the drawing before repeating it.");
        }
        catch (AggregateException e) when (e.InnerException is InvalidDataException ide)
        {
            throw new TargetException(ErrorCodes.Transport, ide.Message);
        }
    }

    /// <summary>Best-effort: remove a still-queued request (MCP notifications/cancelled).</summary>
    public static void CancelQueued(DiscoveryRecord r, string wireId)
    {
        try { Send(r, Guid.NewGuid().ToString("N"), Wire.CancelVerb, new JsonObject { ["wire_id"] = wireId }, 5000); }
        catch { }
    }
}
