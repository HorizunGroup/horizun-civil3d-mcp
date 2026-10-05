// -----------------------------------------------------------------------------
// horizun_c3d_probe - live API signatures from the running Civil 3D.
// -----------------------------------------------------------------------------
using System.Text.Json.Nodes;
using Horizun.Civil3D.Core;
using Horizun.Civil3D.Plugin.Civil;

namespace Horizun.Civil3D.Plugin.Commands;

internal sealed class ProbeCommand : ICommand
{
    private const int MaxInlineChars = 200_000;

    public string Name => "probe";

    public CommandResult Execute(CommandContext ctx)
    {
        var asmName = Hz.Str(ctx.Args, "assembly") ?? "AeccDbMgd";
        var asm = ApiProbe.FindAssembly(asmName)
                  ?? throw new HzRefusal(ErrorCodes.NotFound, "Assembly '" + asmName + "' is not loaded in this Civil 3D.");

        var find = Hz.Str(ctx.Args, "find");
        var types = (ctx.Args["types"] as JsonArray)?.Select(n => n?.GetValue<string>()).Where(s => !string.IsNullOrWhiteSpace(s))
                        .Select(s => s!).ToList() ?? new List<string>();
        string text;
        if (!string.IsNullOrWhiteSpace(find)) text = ApiProbe.Find(asm, find);
        else text = ApiProbe.Dump(asm, types.Count > 0 ? types : ApiProbe.DefaultTypes.ToList());

        string? saved = null;
        try
        {
            var dir = HorizunPaths.ProbesDir(ctx.Year);
            Directory.CreateDirectory(dir);
            saved = Path.Combine(dir, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + asmName + ".txt");
            File.WriteAllText(saved, text);
        }
        catch (Exception e) { ctx.HostMessages.Add(JsonValue.Create("Probe not saved to disk: " + e.Message)); }

        var truncated = text.Length > MaxInlineChars;
        return CommandResult.Ok(new JsonObject
        {
            ["assembly"] = asm.GetName().Name,
            ["assembly_version"] = asm.GetName().Version?.ToString(),
            ["civil3d_year"] = ctx.Year,
            ["saved_to"] = saved,
            ["truncated"] = truncated,
            ["signatures"] = truncated ? text[..MaxInlineChars] : text,
        });
    }
}
