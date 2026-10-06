using System.Text.Json.Nodes;
using Autodesk.Civil;
using Autodesk.Civil.DatabaseServices;
using Horizun.Civil3D.Core;
using Horizun.Civil3D.Plugin.Civil;
using Surface = Autodesk.Civil.DatabaseServices.Surface;

namespace Horizun.Civil3D.Plugin.Commands;

internal sealed partial class SurfaceCommand
{
    // API evidence: AeccDbMgd.dll.compatibility.txt (2024/2026), audit-2025.txt (2025).
    private static CommandResult CompareDesign(CommandContext ctx)
    {
        var doc = ctx.Document(false);
        return ctx.Read(doc, tr =>
        {
            var civil = CommandContext.Civil(doc);
            var designId = Catalog.ByName("surface", Hz.Str(ctx.Args, "design")!, doc.Database, civil, tr);
            var builtId = Catalog.ByName("surface", Hz.Str(ctx.Args, "built")!, doc.Database, civil, tr);
            if (designId == builtId) throw new HzRefusal(ErrorCodes.InvalidInput, "design and built resolve to the same surface.");
            var design = Open(tr, designId); var built = Open(tr, builtId);
            foreach (var surface in new[] { design, built })
            {
                if (surface is TinVolumeSurface or GridVolumeSurface) throw new HzRefusal(ErrorCodes.InvalidInput, "compare_design requires elevation surfaces, not volume surfaces.");
                if (surface.IsOutOfDate || (surface.IsReferenceObject && (!surface.IsReferenceValid || surface.IsReferenceStale)))
                    throw new HzRefusal(ErrorCodes.InvalidInput, "Surface '" + surface.Name + "' is out of date or its reference is invalid/stale. Refresh it explicitly before comparing.");
            }
            var report = SurfaceComparison.Evaluate((JsonArray)ctx.Args["points"]!, Hz.Num(ctx.Args, "tolerance")!.Value,
                (x, y) => ComparisonElevation(design, x, y), (x, y) => ComparisonElevation(built, x, y));
            var data = Data(ctx, doc);
            data["design"] = new JsonObject { ["name"] = design.Name, ["handle"] = design.Handle.ToString() };
            data["built"] = new JsonObject { ["name"] = built.Name, ["handle"] = built.Handle.ToString() };
            data["comparison"] = report;
            data["distance_units"] = "Civil drawing units; coordinates, elevations, tolerance and statistics use the same units reported above.";
            data["transaction"] = new JsonObject { ["committed"] = false, ["disposition"] = "always_abort" };
            return CommandResult.Ok(data);
        });
    }

    private static SurfaceComparison.Elevation ComparisonElevation(Surface surface, double x, double y)
    {
        try { return new(surface.FindElevationAtXY(x, y)); }
        catch (PointNotOnEntityException) { return new(null, "outside_surface_domain"); }
        catch (Exception e) { return new(null, "unreadable: " + e.GetType().Name + ": " + e.Message); }
    }
}
