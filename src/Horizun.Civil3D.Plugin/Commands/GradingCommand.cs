// -----------------------------------------------------------------------------
// horizun_c3d_grading create_geometric - the geometric grading engine in Civil 3D.
//
// GradingEngine (Core) computes every line from the footprint and a sampler of
// the target surface; this command turns them into closed 3D polylines and a
// NEW TIN (all lines as standard breaklines, the outermost as outer boundary).
// Dry run returns the computed lines; the plan fingerprint includes them, so a
// terrain that moved between dry run and apply makes the token stale.
//
// Re-read after commit: the TIN, its breakline group (one breakline per line),
// its outer boundary, and the TIN elevation at the line vertices.
// -----------------------------------------------------------------------------
using System.Text.Json.Nodes;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.DatabaseServices.Styles;
using Horizun.Civil3D.Core;
using Horizun.Civil3D.Plugin.Civil;
using Surface = Autodesk.Civil.DatabaseServices.Surface;

namespace Horizun.Civil3D.Plugin.Commands;

internal sealed class GradingCommand : ICommand
{
    public string Name => "grading";
    private const string DefaultLayer = "HZ-GRADING";

    public CommandResult Execute(CommandContext ctx)
    {
        if (GradingInputs.Validate(ctx.Args) is { } why) throw new HzRefusal(ErrorCodes.InvalidInput, why + " Nothing ran.");
        var doc = ctx.Document(true);
        if (doc.IsReadOnly) throw new HzRefusal(ErrorCodes.ReadOnlyDocument, "The drawing is read-only. Nothing changed.");
        using var operationLock = doc.LockDocument(DocumentLockMode.Write, "HZ_GRADING", "HZ_GRADING", false);
        var name = Hz.Str(ctx.Args, "name")!;
        var layer = Hz.Str(ctx.Args, "layer") ?? DefaultLayer;
        var outer = (ctx.Args["outer"] as JsonArray)?.Cast<JsonObject>().ToList() ?? new List<JsonObject>();
        var inner = (ctx.Args["inner"] as JsonArray)?.Cast<JsonObject>().ToList() ?? new List<JsonObject>();
        var densify = Hz.Num(ctx.Args, "densify") ?? 0.5;
        var data = new JsonObject
        {
            ["tool"] = ctx.Tool.Name, ["action"] = "create_geometric", ["document"] = doc.Name,
            ["units"] = DrawingInfo.Units(CommandContext.Civil(doc), doc.Database),
            ["method"] = "GEOMETRIC grading (3D polylines + TIN breaklines); native Civil 3D gradings have no public API.",
        };
        var plan = new JsonObject { ["action"] = "create_geometric", ["drawing_revision"] = DrawingRevision.Capture(doc) };
        GradingResult result = null!;
        ObjectId styleId = ObjectId.Null, againstId = ObjectId.Null;
        var layerExists = false;

        ctx.Read(doc, tr =>
        {
            var civil = CommandContext.Civil(doc);
            var srcId = Catalog.FromHandle(doc.Database, Hz.Str(ctx.Args, "source")!);
            var src = tr.GetObject(srcId, OpenMode.ForRead);
            var pts = SurfaceCommand.LinearVertices(src, tr, out var closed, out var zKnown)
                      ?? throw new HzRefusal(ErrorCodes.InvalidInput, "source is a " + src.GetType().Name + "; use a closed polyline, 3D polyline or feature line. Nothing changed.");
            if (!closed) throw new HzRefusal(ErrorCodes.InvalidInput, "source " + src.Handle + " is not closed. A grading footprint must be closed. Nothing changed.");
            if (!zKnown) throw new HzRefusal(ErrorCodes.InvalidInput, "source is a 2D polyline whose vertex elevations are in OCS; use a polyline, 3D polyline or feature line. Nothing changed.");
            SurfaceCommand.UniqueSurface(doc, tr, name);

            Func<double, double, double?>? sampler = null;
            ObjectId targetId = ObjectId.Null;
            if (Hz.Str(ctx.Args, "surface") is { } surfName)
            {
                targetId = Catalog.ByName("surface", surfName, doc.Database, civil, tr);
                var target = (Surface)tr.GetObject(targetId, OpenMode.ForRead);
                sampler = (x, y) =>
                {
                    try { var z = target.FindElevationAtXY(x, y); return Hz.IsFinite(z) ? z : null; }
                    catch (PointNotOnEntityException) { return null; }
                };
                plan["surface"] = new JsonObject { ["name"] = target.Name, ["handle"] = target.Handle.ToString(), ["fingerprint"] = target.ComputeFingerPrint().ToString() };
            }

            result = GradingEngine.Run(pts.Select(p => new P3(p.X, p.Y, p.Z)), outer, inner, densify, sampler);

            if (Hz.Str(ctx.Args, "style") is { } styleName) styleId = SurfaceCommand.Style(doc, tr, styleName);
            else if (!targetId.IsNull) styleId = ((Surface)tr.GetObject(targetId, OpenMode.ForRead)).StyleId;
            else styleId = civil.Styles.SurfaceStyles[0];
            var styleObj = (StyleBase)tr.GetObject(styleId, OpenMode.ForRead);
            if (Hz.Str(ctx.Args, "volume_against") is { } against) againstId = Catalog.ByName("surface", against, doc.Database, civil, tr);

            var lt = (LayerTable)tr.GetObject(doc.Database.LayerTableId, OpenMode.ForRead);
            layerExists = lt.Has(layer);
            if (layerExists && ((LayerTableRecord)tr.GetObject(lt[layer], OpenMode.ForRead)).IsLocked)
                throw new HzRefusal(ErrorCodes.NotEditable, "Layer '" + layer + "' is locked. Nothing changed.");

            plan["source"] = new JsonObject { ["handle"] = src.Handle.ToString(), ["class"] = src.GetType().Name, ["vertices"] = pts.Count };
            plan["name"] = name;
            plan["layer"] = new JsonObject { ["name"] = layer, ["will_be_created"] = !layerExists };
            plan["style"] = styleObj.Name;
            plan["densify"] = densify;
            plan["outer"] = new JsonArray(outer.Select(o => (JsonNode?)o.DeepClone()).ToArray());
            plan["inner"] = new JsonArray(inner.Select(o => (JsonNode?)o.DeepClone()).ToArray());
            plan["lines"] = result.Summary();
            plan["boundary_line"] = result.BoundaryLine.Tag;
            plan["daylight_rays"] = new JsonObject { ["total"] = result.TotalRays, ["failed"] = result.FailedRays };
            if (result.FailedRays > 0)
                plan["warning"] = result.FailedRays + " daylight rays did not meet the target surface (it ends before the slope reaches it). " +
                                  "The daylight line skips those directions; check the plan before applying.";
            if (!againstId.IsNull) plan["volume_against"] = Catalog.NameOf(againstId, tr);
            plan["log"] = Hz.Strings(result.Log);
            return 0;
        });

        if (ctx.DryRun) return CommandResult.Ok(ctx.Rehearse(doc, data, plan));
        ctx.RequireConfirmation(doc, plan);

        ObjectId tinId = ObjectId.Null;
        var lineIds = new List<(string Tag, ObjectId Id)>();
        ctx.Write(doc, "HZ_GRADING", tr =>
        {
            var db = doc.Database;
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (!lt.Has(layer))
            {
                lt.UpgradeOpen();
                var ltr = new LayerTableRecord { Name = layer };
                lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
            }
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
            var ids = new ObjectIdCollection();
            foreach (var line in result.Lines)
            {
                var pc = new Point3dCollection();
                foreach (var p in line.Points) pc.Add(new Point3d(p.X, p.Y, p.Z));
                var pl = new Polyline3d(Poly3dType.SimplePoly, pc, true) { Layer = layer };
                var id = ms.AppendEntity(pl);
                tr.AddNewlyCreatedDBObject(pl, true);
                ids.Add(id);
                lineIds.Add((line.Tag, id));
            }
            tinId = TinSurface.Create(name, styleId);
            var tin = (TinSurface)tr.GetObject(tinId, OpenMode.ForWrite);
            tin.Description = "Horizun geometric grading";
            var op = tin.BreaklinesDefinition.AddStandardBreaklines(ids, 1.0, 0, 0, 0);
            op.Description = "Horizun geometric grading lines";
            var boundaryId = lineIds.Last(l => l.Tag == result.BoundaryLine.Tag).Id;
            tin.BoundariesDefinition.AddBoundaries(new ObjectIdCollection { boundaryId }, 1.0, SurfaceBoundaryType.Outer, true);
            tin.Rebuild();
            return 0;
        });

        var checks = new VerificationSet();
        var after = new JsonObject();
        try
        {
            ctx.Verify(doc, tr =>
            {
                var tin = (TinSurface)tr.GetObject(tinId, OpenMode.ForRead);
                checks.Text("TIN name", name, tin.Name, false);
                checks.Check("breakline groups", 1, tin.BreaklinesDefinition.Count, tin.BreaklinesDefinition.Count == 1);
                if (tin.BreaklinesDefinition.Count == 1)
                    checks.Check("breaklines (one per line)", result.Lines.Count, tin.BreaklinesDefinition[0].Count, tin.BreaklinesDefinition[0].Count == result.Lines.Count);
                checks.Check("outer boundary", "Outer", tin.BoundariesDefinition.Count == 1 ? tin.BoundariesDefinition[0].BoundaryType.ToString() : null,
                    tin.BoundariesDefinition.Count == 1 && tin.BoundariesDefinition[0].BoundaryType == SurfaceBoundaryType.Outer);
                foreach (var line in result.Lines)
                    SurfaceCommand.ElevationCheck(checks, tin, "line " + line.Tag + " vertices", line.Points.Select(p => new Point3d(p.X, p.Y, p.Z)).ToList(),
                        boundaryInCall: line.Tag == result.BoundaryLine.Tag);
                checks.Flag("is_out_of_date after rebuild", false, tin.IsOutOfDate);
                after["surface"] = new JsonObject { ["name"] = tin.Name, ["handle"] = tin.Handle.ToString(), ["counts"] = SurfaceCommand.TinCounts(tin) };
                after["polylines"] = Hz.Arr(lineIds.Select(l => (JsonNode?)new JsonObject { ["tag"] = l.Tag, ["handle"] = l.Id.Handle.ToString() }));
                return 0;
            });
        }
        catch (Exception e) { checks.Check("post-commit re-read", true, null, false, e.GetType().Name + ": " + e.Message); }

        if (!againstId.IsNull)
        {
            try
            {
                after["volume_against"] = ctx.Read(doc, tr =>
                {
                    var id = TinVolumeSurface.Create("HZ_TRANSIENT_" + Guid.NewGuid().ToString("N"), againstId, tinId);
                    var v = SurfaceCommand.Volume((Surface)tr.GetObject(id, OpenMode.ForWrite), tr, new JsonObject());
                    v.Remove("name"); v.Remove("handle");
                    v["transient"] = "Computed in a transaction that is always aborted: no volume surface was added.";
                    return v;
                });
            }
            catch (Exception e) { after["volume_against"] = null; after["volume_against_unreadable_reason"] = e.GetType().Name + ": " + e.Message; }
        }

        data["dry_run"] = false;
        data["committed"] = true;
        data["plan"] = plan;
        data["after"] = after;
        data["verified"] = checks.ToJson();
        data["undo"] = new JsonObject { ["available"] = false, ["label"] = "HZ_GRADING", ["instruction"] = "Automatic undo_last is disabled. Use Civil 3D native UNDO manually and inspect the result; no drawing was saved." };
        return checks.AllVerified ? CommandResult.Ok(data)
            : CommandResult.Fail(ErrorCodes.VerificationFailed, "The grading committed but the re-read did not verify every item. Inspect after/verified.", data);
    }
}
