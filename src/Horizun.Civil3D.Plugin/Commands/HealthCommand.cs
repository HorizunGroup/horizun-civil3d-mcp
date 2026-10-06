// -----------------------------------------------------------------------------
// horizun_c3d_health - the first call of every session.
// -----------------------------------------------------------------------------
using System.Diagnostics;
using System.Text.Json.Nodes;
using Horizun.Civil3D.Core;
using Horizun.Civil3D.Plugin.Civil;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace Horizun.Civil3D.Plugin.Commands;

internal sealed class HealthCommand : ICommand
{
    public string Name => "health";

    public CommandResult Execute(CommandContext ctx)
    {
        var data = new JsonObject
        {
            ["host"] = "civil3d",
            ["product"] = "Horizun Civil 3D MCP",
            ["plugin_version"] = App.PluginVersion,
            ["contract_hash"] = Contract.Hash,
            ["protocol_version"] = Contract.ProtocolVersion,
        };

        var civil3d = new JsonObject { ["year"] = ctx.Year, ["pid"] = Horizun.Civil3D.Core.RuntimeCompat.ProcessId, ["build_runtime"] = App.BuildRuntime };
        try { civil3d["acadver"] = Convert.ToString(AcApp.GetSystemVariable("ACADVER")); } catch { civil3d["acadver"] = null; }
        try { civil3d["product"] = Convert.ToString(AcApp.GetSystemVariable("PRODUCT")); } catch { civil3d["product"] = null; }
        var aecc = ApiProbe.FindAssembly("AeccDbMgd");
        civil3d["aeccdbmgd_version"] = aecc?.GetName().Version?.ToString();
        try
        {
            var file = aecc?.Location;
            civil3d["aeccdbmgd_file_version"] = string.IsNullOrEmpty(file) ? null : FileVersionInfo.GetVersionInfo(file).FileVersion;
        }
        catch { civil3d["aeccdbmgd_file_version"] = null; }
        data["civil3d"] = civil3d;

        var dm = AcApp.DocumentManager;
        data["open_documents"] = dm.Count;
        var doc = dm.MdiActiveDocument;
        if (doc == null)
        {
            data["document"] = null;
            data["document_note"] = "No drawing is open.";
        }
        else
        {
            var d = DrawingInfo.File(doc, true);
            ctx.Read(doc, tr =>
            {
                var civil = CommandContext.Civil(doc);
                d["units"] = DrawingInfo.Units(civil, doc.Database);
                d["coordinate_system"] = DrawingInfo.CoordinateSystem(civil);
                return 0;
            });
            data["document"] = d;
        }

        var snap = UiPump.State;
        data["state"] = new JsonObject
        {
            ["busy"] = !snap.Quiescent,
            ["cmdactive"] = snap.CmdActive,
            ["cmdnames"] = snap.CmdNames,
            ["note"] = "Measured at the moment this health call ran on Civil 3D's main thread.",
        };
        data["bridge"] = new JsonObject
        {
            ["queue"] = App.Bridge?.Gate.Observe(),
            ["permissions"] = ctx.Settings.Describe(),
            ["data_root"] = HorizunPaths.DataRoot(),
            ["log"] = Log.Path,
            ["commands"] = Hz.Strings(Contract.PluginCommands),
        };
        return CommandResult.Ok(data);
    }
}
