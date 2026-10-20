// APIs already probed in AeccDbMgd.phase1-surfaces.txt, export-units.txt and
// collections-units.txt. No Revit runtime dependency is introduced into Civil 3D.
using System.Text.Json.Nodes;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.Settings;
using Horizun.Civil3D.Core;
using Horizun.Civil3D.Plugin.Civil;

namespace Horizun.Civil3D.Plugin.Commands;

internal static class RevitTerrainExport
{
    private static LxSurface VisibleMesh(TinSurface surface)
    {
        var points = new List<(double X, double Y, double Z)>();
        var index = new Dictionary<(double X, double Y, double Z), int>();
        var faces = new List<(int, int, int)>();
        int Vertex(TinSurfaceVertex v)
        {
            var loc = v.Location; var key = (loc.X, loc.Y, loc.Z);
            if (index.TryGetValue(key, out var i)) return i;
            if (points.Count >= RevitTerrainPackage.MaxPoints)
                throw new HzRefusal(ErrorCodes.Unsupported, "The visible TIN exceeds the current Revit reader's 20000-point guard. Export LandXML separately or choose a prepared smaller surface; no automatic thinning occurred.");
            index[key] = points.Count; points.Add(key); return points.Count - 1;
        }
        foreach (TinSurfaceTriangle triangle in surface.GetTriangles(false))
        {
            if (!triangle.IsVisible) continue;
            if (faces.Count >= RevitTerrainPackage.MaxFaces)
                throw new HzRefusal(ErrorCodes.Unsupported, "The TIN exceeds this package's face guard. Nothing written.");
            faces.Add((Vertex(triangle.Vertex1), Vertex(triangle.Vertex2), Vertex(triangle.Vertex3)));
        }
        return new(surface.Name, surface.Description ?? "", points, faces);
    }

    public static CommandResult Run(CommandContext ctx)
    {
        var output = Path.GetFullPath(Hz.Str(ctx.Args, "output")!);
        RevitTerrainPackage.Prepared? package = null;
        string sourceName = "";
        return WriteFlow.Run(ctx, "HZ_EXPORT_REVIT_TERRAIN",
            (doc, tr, plan) =>
            {
                if (File.Exists(output) || Directory.Exists(output))
                    throw new HzRefusal(ErrorCodes.InvalidInput, "The destination exists. Nothing written.");
                if (!Directory.Exists(Path.GetDirectoryName(output)))
                    throw new HzRefusal(ErrorCodes.NotFound, "The destination folder does not exist. Nothing written.");
                var id = Resolve.Named(doc, tr, "surface", Hz.Str(ctx.Args, "surface")!);
                if (tr.GetObject(id, OpenMode.ForRead) is not TinSurface surface)
                    throw new HzRefusal(ErrorCodes.Unsupported, "export_revit requires a TIN terrain surface, not a grid or volume surface. Nothing written.");
                var civil = CommandContext.Civil(doc);
                var zone = civil.Settings.DrawingSettings.UnitZoneSettings;
                var unit = zone.DrawingUnits switch
                {
                    DrawingUnitType.Meters => "meter",
                    DrawingUnitType.Feet => zone.ImperialToMetricConversion switch
                    {
                        ImperialToMetricConversionType.InternationalFoot => "foot",
                        ImperialToMetricConversionType.UsSurveyFoot => "USSurveyFoot",
                        _ => throw new HzRefusal(ErrorCodes.Unsupported, "Unknown Civil foot definition."),
                    },
                    _ => throw new HzRefusal(ErrorCodes.Unsupported, "Unknown Civil drawing units."),
                };
                sourceName = doc.Name;
                var source = new JsonObject { ["drawing"] = doc.Name, ["surface_handle"] = id.Handle.ToString(),
                    ["contract"] = Contract.Hash, ["plugin_version"] = App.PluginVersion,
                    ["drawing_revision"] = DrawingRevision.Capture(doc), ["saved_source_required"] = false };
                try { package = RevitTerrainPackage.Prepare(VisibleMesh(surface), unit, source, DrawingInfo.CoordinateSystem(civil), App.PluginVersion); }
                catch (ArgumentException e) { throw new HzRefusal(ErrorCodes.InvalidInput, e.Message + " Nothing written."); }
                plan["output"] = output; plan["terrain"] = package.Summary.DeepClone();
                // Bind the full geometry, including faces and round-trip coordinates,
                // to rehearsal; counts alone cannot detect changed terrain.
                plan["landxml_sha256"] = RevitTerrainPackage.Hash(package.LandXml);
                plan["manifest_sha256"] = RevitTerrainPackage.Hash(package.Manifest);
                plan["mesh_obj_sha256"] = RevitTerrainPackage.Hash(package.MeshObj!);
                plan["source_saved"] = false;
            },
            (doc, tr) => AtomicOutput.Write(output, false,
                stage => RevitTerrainPackage.Write(stage, package!), stage => RevitTerrainPackage.Matches(stage, package!)),
            (doc, tr, checks, after) =>
            {
                checks.Flag("terrain ZIP exists", true, File.Exists(output));
                if (!File.Exists(output)) return;
                checks.Flag("all ZIP payload bytes reread equal prepared terrain", true, RevitTerrainPackage.Matches(output, package!));
                checks.Text("source identity unchanged", sourceName, doc.Name);
                after["terrain"] = package!.Summary.DeepClone();
                after["file"] = new JsonObject { ["path"] = output, ["bytes"] = new FileInfo(output).Length,
                    ["sha256"] = AtomicOutput.Capture(output).Sha256 };
                after["revit_import_verified"] = false;
                after["next_step"] = "Prepare the separate Revit dry-run request with scripts/prepare-revit-terrain.ps1. Inspect placement and retriangulation before applying; export alone created no Revit element.";
            });
    }
}
