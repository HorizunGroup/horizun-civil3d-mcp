// -----------------------------------------------------------------------------
// horizun_c3d_target - answered by the server; never touches a drawing.
// -----------------------------------------------------------------------------
using System.Text.Json.Nodes;
using Horizun.Civil3D.Core;

namespace Horizun.Civil3D.Server;

internal static class TargetTool
{
    public static JsonObject Run(TargetSelection sel, JsonObject args)
    {
        var all = PluginClient.ReadAll();
        var hasYear = args["year"] != null;
        var hasPid = args["pid"] != null;
        if (hasYear && hasPid)
            return McpServer.ToolError(ErrorCodes.InvalidInput, "Pass year OR pid, not both. Nothing changed.");

        if (hasPid)
        {
            var pid = Hz.Int(args, "pid")!.Value;
            if (!all.Any(r => r.Pid == pid))
                return McpServer.ToolError(ErrorCodes.NoInstance, "No Civil 3D with pid " + pid + " has published a Horizun bridge. Selection unchanged.",
                    Listing(sel, all));
            sel.SetPid(pid);
        }
        else if (hasYear)
        {
            var raw = args["year"]!.ToJsonString().Trim('"');
            if (raw.Equals("auto", StringComparison.OrdinalIgnoreCase)) sel.SetAuto();
            else if (int.TryParse(raw, out var y) && y >= 2000 && y <= 2100)
            {
                if (!all.Any(r => r.Year == y))
                    return McpServer.ToolError(ErrorCodes.NoInstance, "No Civil 3D " + y + " has published a Horizun bridge. Selection unchanged.",
                        Listing(sel, all));
                sel.SetYear(y);
            }
            else return McpServer.ToolError(ErrorCodes.InvalidInput, "year must be a four-digit year or 'auto'. Selection unchanged.");
        }
        return McpServer.ToolOk(Listing(sel, all));
    }

    private static JsonObject Listing(TargetSelection sel, List<DiscoveryRecord> all)
    {
        DiscoveryRecord? resolved = null;
        string? resolveNote = null;
        try { resolved = PluginClient.Resolve(sel); }
        catch (TargetException e) { resolveNote = e.Message; }

        var targets = new JsonArray();
        foreach (var r in all.OrderBy(r => r.Year).ThenBy(r => r.Pid))
        {
            var alive = PluginClient.IsAlive(r);
            targets.Add(new JsonObject
            {
                ["civil3d_year"] = r.Year,
                ["acadver"] = r.AcadVersion,
                ["pid"] = r.Pid,
                ["running"] = alive,
                ["plugin_version"] = r.PluginVersion,
                ["contract_match"] = r.ContractHash == Contract.Hash && r.ProtocolVersion == Contract.ProtocolVersion,
                ["started_utc"] = r.StartedUtc.ToString("o"),
                ["selected"] = resolved != null && resolved.Pid == r.Pid,
                ["discovery_file"] = r.FilePath,
            });
        }
        var o = new JsonObject
        {
            ["selection"] = sel.Describe(),
            ["targets"] = targets,
            ["server_contract_hash"] = Contract.Hash,
            ["data_root"] = HorizunPaths.DataRoot(),
        };
        if (resolved != null) o["will_talk_to"] = new JsonObject { ["civil3d_year"] = resolved.Year, ["pid"] = resolved.Pid };
        if (resolveNote != null) o["note"] = resolveNote;
        return o;
    }
}
