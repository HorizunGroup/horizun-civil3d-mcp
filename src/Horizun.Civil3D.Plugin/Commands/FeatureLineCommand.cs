// -----------------------------------------------------------------------------
// horizun_c3d_feature_line - create_from_polyline, set_elevations, rename,
// export_polyline3d. API confirmed in docs/api-probes/2025/AeccDbMgd.phase2-featureline.txt.
//
// Verification re-reads the feature line's points (GetPoints(AllPoints)):
// created lines must contain every source vertex (XY, and Z for 3D sources);
// from_surface elevations must equal the surface at each point; constant and
// per-point elevations must hold exactly.
// -----------------------------------------------------------------------------
using System.Text.Json.Nodes;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil;
using Autodesk.Civil.DatabaseServices;
using Horizun.Civil3D.Core;
using Horizun.Civil3D.Plugin.Civil;
using Surface = Autodesk.Civil.DatabaseServices.Surface;

namespace Horizun.Civil3D.Plugin.Commands;

internal sealed class FeatureLineCommand : ICommand
{
    public string Name => "feature_line";
    private const double Tol = 1e-4;

    public CommandResult Execute(CommandContext ctx)
    {
        if (FeatureLineInputs.Validate(ctx.Args) is { } why) throw new HzRefusal(ErrorCodes.InvalidInput, why + " Nothing ran.");
        var doc = ctx.Document(true);
        if (doc.IsReadOnly) throw new HzRefusal(ErrorCodes.ReadOnlyDocument, "The drawing is read-only. Nothing changed.");
        using var operationLock = doc.LockDocument(DocumentLockMode.Write, "HZ_FEATURELINE", "HZ_FEATURELINE", false);
        var data = new JsonObject
        {
            ["tool"] = ctx.Tool.Name, ["action"] = ctx.Action, ["document"] = doc.Name,
            ["units"] = DrawingInfo.Units(CommandContext.Civil(doc), doc.Database),
        };
        var plan = new JsonObject { ["action"] = ctx.Action, ["drawing_revision"] = DrawingRevision.Capture(doc) };
        return ctx.Action == "create_from_polyline" ? Create(ctx, doc, data, plan) : Edit(ctx, doc, data, plan);
    }

    private static List<Point3d> Points(FeatureLine fl)
    {
        var l = new List<Point3d>();
        foreach (Point3d p in fl.GetPoints(FeatureLinePointType.AllPoints)) l.Add(p);
        return l;
    }

    private static JsonObject PointsSummary(List<Point3d> pts) => new()
    {
        ["count"] = pts.Count,
        ["min_z"] = pts.Count > 0 ? Hz.Finite(pts.Min(p => p.Z), 4) : null,
        ["max_z"] = pts.Count > 0 ? Hz.Finite(pts.Max(p => p.Z), 4) : null,
    };

    private static HashSet<string> ExistingNames(Document doc, Transaction tr)
    {
        var civil = CommandContext.Civil(doc);
        return Catalog.Ids("feature_line", doc.Database, civil, tr).Select(id => Catalog.NameOf(id, tr) ?? "")
            .Where(n => n.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private CommandResult Create(CommandContext ctx, Document doc, JsonObject data, JsonObject plan)
    {
        var handles = ((JsonArray)ctx.Args["handles"]!).Select(n => n!.GetValue<string>()).ToList();
        var names = ((JsonArray)ctx.Args["names"]!).Select(n => n!.GetValue<string>()).ToList();
        var sources = new List<(ObjectId Id, List<Point3d> Pts, bool ZKnown)>();
        var siteId = ObjectId.Null;

        ctx.Read(doc, tr =>
        {
            var civil = CommandContext.Civil(doc);
            var existing = ExistingNames(doc, tr);
            var items = new JsonArray();
            for (var i = 0; i < handles.Count; i++)
            {
                if (existing.Contains(names[i]))
                    throw new HzRefusal(ErrorCodes.InvalidInput, "A feature line named '" + names[i] + "' already exists (names must be unique). Nothing changed.");
                var id = Catalog.FromHandle(doc.Database, handles[i]);
                var obj = tr.GetObject(id, OpenMode.ForRead);
                if (obj is FeatureLine || obj is not Curve)
                    throw new HzRefusal(ErrorCodes.InvalidInput, "Handle " + handles[i].ToUpperInvariant() + " is a " + obj.GetType().Name + "; use a polyline or 3D polyline. Nothing changed.");
                var pts = SurfaceCommand.LinearVertices(obj, tr, out var closed, out var zKnown)
                          ?? throw new HzRefusal(ErrorCodes.InvalidInput, "Handle " + handles[i].ToUpperInvariant() + " is a " + obj.GetType().Name + "; use a polyline or 3D polyline. Nothing changed.");
                sources.Add((id, pts, zKnown));
                items.Add(new JsonObject { ["handle"] = obj.Handle.ToString(), ["class"] = obj.GetType().Name, ["vertices"] = pts.Count, ["closed"] = closed, ["name"] = names[i] });
            }
            if (Hz.Str(ctx.Args, "site") is { } site)
            {
                siteId = Catalog.ByName("site", site, doc.Database, civil, tr);
                plan["site"] = site;
            }
            else plan["site"] = "(siteless)";
            plan["items"] = items;
            plan["note"] = "Source polylines are kept; each feature line is created from its polyline.";
            return 0;
        });

        if (ctx.DryRun) return CommandResult.Ok(ctx.Rehearse(doc, data, plan));
        ctx.RequireConfirmation(doc, plan);

        var created = new List<ObjectId>();
        ctx.Write(doc, "HZ_FEATURELINE", tr =>
        {
            for (var i = 0; i < sources.Count; i++)
                created.Add(siteId.IsNull ? FeatureLine.Create(names[i], sources[i].Id) : FeatureLine.Create(names[i], sources[i].Id, siteId));
            return 0;
        });

        var checks = new VerificationSet();
        var after = new JsonArray();
        try
        {
            ctx.Verify(doc, tr =>
            {
                for (var i = 0; i < created.Count; i++)
                {
                    var fl = (FeatureLine)tr.GetObject(created[i], OpenMode.ForRead);
                    checks.Text(names[i] + " name", names[i], fl.Name, false);
                    checks.Check(names[i] + " site", siteId.IsNull ? null : siteId.Handle.ToString(), fl.SiteId.IsNull ? null : fl.SiteId.Handle.ToString(), fl.SiteId == siteId);
                    var pts = Points(fl);
                    var src = sources[i];
                    var missing = src.Pts.Count(s => !pts.Any(p => Math.Abs(p.X - s.X) <= Tol && Math.Abs(p.Y - s.Y) <= Tol && (!src.ZKnown || Math.Abs(p.Z - s.Z) <= Tol)));
                    checks.Check(names[i] + " contains every source vertex" + (src.ZKnown ? " (XYZ)" : " (XY)"), src.Pts.Count, src.Pts.Count - missing, missing == 0,
                        missing + " source vertices are not in the feature line.");
                    after.Add(new JsonObject { ["name"] = fl.Name, ["handle"] = fl.Handle.ToString(), ["points"] = PointsSummary(pts) });
                }
                return 0;
            });
        }
        catch (Exception e) { checks.Check("post-commit re-read", true, null, false, e.GetType().Name + ": " + e.Message); }
        return Finish(data, plan, after, checks);
    }

    private CommandResult Edit(CommandContext ctx, Document doc, JsonObject data, JsonObject plan)
    {
        ObjectId flId = ObjectId.Null, surfId = ObjectId.Null, layerId = ObjectId.Null;
        var newName = Hz.Str(ctx.Args, "new_name");

        ctx.Read(doc, tr =>
        {
            var civil = CommandContext.Civil(doc);
            flId = Hz.Str(ctx.Args, "handle") is { } h ? Catalog.FromHandle(doc.Database, h) : Catalog.ByName("feature_line", Hz.Str(ctx.Args, "name")!, doc.Database, civil, tr);
            if (tr.GetObject(flId, OpenMode.ForRead) is not FeatureLine fl)
                throw new HzRefusal(ErrorCodes.InvalidInput, "The selected object is not a feature line. Nothing changed.");
            var d = Catalog.Describe(fl, tr, new Catalog.Lookup(tr), false);
            if (ctx.Action != "export_polyline3d")
            {
                if (Hz.Bool(d, "editable") != true)
                    throw new HzRefusal(ErrorCodes.NotEditable, "Feature line '" + fl.Name + "' is not editable here: " + d["not_editable_because"]?.ToJsonString(Hz.Compact) + ". Nothing changed.");
                if (!fl.IsEditable)
                    throw new HzRefusal(ErrorCodes.NotEditable, "Civil 3D reports feature line '" + fl.Name + "' as not editable (e.g. owned by a corridor or grading). Nothing changed.");
            }
            var pts = Points(fl);
            plan["feature_line"] = new JsonObject { ["name"] = fl.Name, ["handle"] = fl.Handle.ToString(), ["fingerprint"] = fl.ComputeFingerPrint().ToString(), ["points_before"] = PointsSummary(pts) };
            switch (ctx.Action)
            {
                case "set_elevations":
                    plan["mode"] = Hz.Str(ctx.Args, "mode");
                    if (Hz.Str(ctx.Args, "mode") == "from_surface")
                    {
                        surfId = Catalog.ByName("surface", Hz.Str(ctx.Args, "surface")!, doc.Database, civil, tr);
                        plan["surface"] = Catalog.NameOf(surfId, tr);
                        plan["insert_intermediate"] = Hz.Bool(ctx.Args, "insert_intermediate") ?? false;
                    }
                    else if (Hz.Str(ctx.Args, "mode") == "constant") plan["elevation"] = Hz.Num(ctx.Args, "elevation");
                    else
                    {
                        var bad = ((JsonArray)ctx.Args["points"]!).Select(p => Hz.Int((JsonObject)p!, "index")!.Value).Where(i => i >= pts.Count).ToList();
                        if (bad.Count > 0)
                            throw new HzRefusal(ErrorCodes.InvalidInput, "Point indices " + string.Join(", ", bad) + " do not exist: the feature line has " + pts.Count + " points (0-" + (pts.Count - 1) + "). Nothing changed.");
                        plan["points"] = ctx.Args["points"]!.DeepClone();
                    }
                    break;
                case "rename":
                    if (ExistingNames(doc, tr).Contains(newName!) && !string.Equals(fl.Name, newName, StringComparison.OrdinalIgnoreCase))
                        throw new HzRefusal(ErrorCodes.InvalidInput, "A feature line named '" + newName + "' already exists. Nothing changed.");
                    plan["new_name"] = newName;
                    break;
                case "export_polyline3d":
                    var lt = (LayerTable)tr.GetObject(doc.Database.LayerTableId, OpenMode.ForRead);
                    var layer = Hz.Str(ctx.Args, "layer") ?? fl.Layer;
                    if (!lt.Has(layer)) throw new HzRefusal(ErrorCodes.NotFound, "Layer '" + layer + "' does not exist. Nothing changed.");
                    layerId = lt[layer];
                    if (((LayerTableRecord)tr.GetObject(layerId, OpenMode.ForRead)).IsLocked) throw new HzRefusal(ErrorCodes.NotEditable, "Layer '" + layer + "' is locked. Nothing changed.");
                    plan["layer"] = layer;
                    break;
            }
            return 0;
        });

        if (ctx.DryRun) return CommandResult.Ok(ctx.Rehearse(doc, data, plan));
        ctx.RequireConfirmation(doc, plan);

        ObjectId exported = ObjectId.Null;
        var closedExport = false;
        ctx.Write(doc, "HZ_FEATURELINE", tr =>
        {
            var fl = (FeatureLine)tr.GetObject(flId, ctx.Action == "export_polyline3d" ? OpenMode.ForRead : OpenMode.ForWrite);
            switch (ctx.Action)
            {
                case "set_elevations":
                    var mode = Hz.Str(ctx.Args, "mode");
                    if (mode == "from_surface") fl.AssignElevationsFromSurface(surfId, Hz.Bool(ctx.Args, "insert_intermediate") ?? false);
                    else if (mode == "constant")
                    {
                        var z = Hz.Num(ctx.Args, "elevation")!.Value;
                        var n = Points(fl).Count;
                        for (var i = 0; i < n; i++) fl.SetPointElevation(i, z);
                    }
                    else
                        foreach (var p in ((JsonArray)ctx.Args["points"]!).Cast<JsonObject>())
                            fl.SetPointElevation(Hz.Int(p, "index")!.Value, Hz.Num(p, "z")!.Value);
                    break;
                case "rename":
                    fl.Name = newName!;
                    break;
                case "export_polyline3d":
                    var pts = Points(fl);
                    closedExport = pts.Count > 2 && pts[0].DistanceTo(pts[^1]) < 1e-9;
                    var pc = new Point3dCollection();
                    foreach (var p in closedExport ? pts.Take(pts.Count - 1) : pts) pc.Add(p);
                    var pl = new Polyline3d(Poly3dType.SimplePoly, pc, closedExport) { LayerId = layerId };
                    var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(doc.Database), OpenMode.ForWrite);
                    exported = ms.AppendEntity(pl);
                    tr.AddNewlyCreatedDBObject(pl, true);
                    break;
            }
            return 0;
        });

        var checks = new VerificationSet();
        var after = new JsonArray();
        try
        {
            ctx.Verify(doc, tr =>
            {
                var fl = (FeatureLine)tr.GetObject(flId, OpenMode.ForRead);
                var pts = Points(fl);
                switch (ctx.Action)
                {
                    case "set_elevations":
                        var mode = Hz.Str(ctx.Args, "mode");
                        if (mode == "from_surface")
                        {
                            var s = (Surface)tr.GetObject(surfId, OpenMode.ForRead);
                            int ok = 0, outside = 0;
                            var worst = new JsonArray();
                            foreach (var p in pts)
                            {
                                try
                                {
                                    var z = s.FindElevationAtXY(p.X, p.Y);
                                    if (Math.Abs(z - p.Z) <= 1e-3) ok++;
                                    else if (worst.Count < 10) worst.Add(new JsonObject { ["x"] = p.X, ["y"] = p.Y, ["feature_line_z"] = p.Z, ["surface_z"] = Hz.Finite(z) });
                                }
                                catch (PointNotOnEntityException) { outside++; }
                            }
                            var actual = new JsonObject { ["points"] = pts.Count, ["match"] = ok, ["outside_surface"] = outside };
                            if (worst.Count > 0) actual["mismatches"] = worst;
                            checks.Check("every point equals the surface elevation (tol 1e-3)", pts.Count, actual, ok == pts.Count,
                                "Some feature-line points do not hold the surface elevation (or lie outside the surface).");
                        }
                        else if (mode == "constant")
                        {
                            var z = Hz.Num(ctx.Args, "elevation")!.Value;
                            var bad = pts.Count(p => Math.Abs(p.Z - z) > Tol);
                            checks.Check("every point at elevation " + z, pts.Count, pts.Count - bad, bad == 0, bad + " points are not at the requested elevation.");
                        }
                        else
                            foreach (var p in ((JsonArray)ctx.Args["points"]!).Cast<JsonObject>())
                            {
                                var i = Hz.Int(p, "index")!.Value;
                                checks.Number("point " + i + " z", Hz.Num(p, "z")!.Value, i < pts.Count ? pts[i].Z : double.NaN, Tol);
                            }
                        break;
                    case "rename":
                        checks.Text("name", newName, fl.Name, false);
                        break;
                    case "export_polyline3d":
                        var pl = (Polyline3d)tr.GetObject(exported, OpenMode.ForRead);
                        var verts = new List<Point3d>();
                        foreach (ObjectId v in pl) verts.Add(((PolylineVertex3d)tr.GetObject(v, OpenMode.ForRead)).Position);
                        var expected = closedExport ? pts.Take(pts.Count - 1).ToList() : pts;
                        var same = verts.Count == expected.Count && verts.Zip(expected).All(q => q.First.DistanceTo(q.Second) <= Tol);
                        checks.Check("3D polyline vertices equal the feature-line points", expected.Count, verts.Count, same);
                        checks.Flag("3D polyline closed", closedExport, pl.Closed);
                        after.Add(new JsonObject { ["polyline3d_handle"] = pl.Handle.ToString(), ["vertices"] = verts.Count, ["closed"] = pl.Closed });
                        break;
                }
                after.Add(new JsonObject { ["name"] = fl.Name, ["handle"] = fl.Handle.ToString(), ["points"] = PointsSummary(pts) });
                return 0;
            });
        }
        catch (Exception e) { checks.Check("post-commit re-read", true, null, false, e.GetType().Name + ": " + e.Message); }
        return Finish(data, plan, after, checks);
    }

    private static CommandResult Finish(JsonObject data, JsonObject plan, JsonArray after, VerificationSet checks)
    {
        data["dry_run"] = false;
        data["committed"] = true;
        data["plan"] = plan;
        data["after"] = after;
        data["verified"] = checks.ToJson();
        data["undo"] = new JsonObject { ["available"] = false, ["label"] = "HZ_FEATURELINE", ["instruction"] = "Automatic undo_last is disabled. Use Civil 3D native UNDO manually and inspect the result; no drawing was saved." };
        return checks.AllVerified ? CommandResult.Ok(data)
            : CommandResult.Fail(ErrorCodes.VerificationFailed, "The change committed but the re-read did not verify every item. Inspect after/verified.", data);
    }
}
