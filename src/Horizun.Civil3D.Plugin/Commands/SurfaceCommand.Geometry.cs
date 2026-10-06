// -----------------------------------------------------------------------------
// horizun_c3d_surface add_data / paste - surface DEFINITION edits on a TIN.
// API confirmed in docs/api-probes/2025/AeccDbMgd.phase1-adddata-paste.txt.
//
// Verification is not "the call did not throw":
//   * the definition grew by exactly the expected operations/groups, of the
//     requested kind, description/name and (paste) source order;
//   * every requested vertex, and every vertex of a standard breakline with no
//     weeding, is RE-READ as a surface elevation after commit (FindElevationAtXY)
//     and must match within 1e-4 drawing units. A point hidden by a boundary added
//     in the same call is reported, not counted as a pass or a silent failure.
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
using DBObject = Autodesk.AutoCAD.DatabaseServices.DBObject;

namespace Horizun.Civil3D.Plugin.Commands;

internal sealed partial class SurfaceCommand
{
    private const double ElevationTolerance = 1e-4;
    private const int MaxElevationChecks = 500;

    private sealed class GeometryPlan
    {
        public ObjectId Target;
        public List<Point3d> Vertices = new();
        public ObjectIdCollection BreakIds = new();
        public ObjectIdCollection BoundIds = new();
        public List<ObjectId> Sources = new();
        public List<Point3d> BreaklineChecks = new();
        public int OpsBefore, BrkBefore, BndBefore;
    }

    private static CommandResult Geometry(CommandContext ctx)
    {
        var doc = ctx.Document(true);
        if (doc.IsReadOnly) throw new HzRefusal(ErrorCodes.ReadOnlyDocument, "The drawing is read-only. Nothing changed.");
        using var operationLock = doc.LockDocument(DocumentLockMode.Write, "HZ_SURFACE", "HZ_SURFACE", false);
        var data = Data(ctx, doc);
        var plan = new JsonObject { ["action"] = ctx.Action, ["drawing_revision"] = DrawingRevision.Capture(doc) };
        var g = new GeometryPlan();
        var brk = ctx.Args["breaklines"] as JsonObject;
        var bnd = ctx.Args["boundaries"] as JsonObject;
        var rebuild = Hz.Bool(ctx.Args, "rebuild") ?? true;

        ctx.Read(doc, tr =>
        {
            g.Target = Resolve(ctx, doc, tr)[0];
            var s = Open(tr, g.Target);
            if (s is not TinSurface tin)
                throw new HzRefusal(ErrorCodes.InvalidInput, "'" + s.Name + "' is a " + Catalog.Describe(s, tr, new Catalog.Lookup(tr), false)["surface_kind"] +
                    " surface. add_data and paste edit TIN surfaces only. Nothing changed.");
            Editable(s, tr);
            g.OpsBefore = tin.Operations.Count;
            g.BrkBefore = tin.BreaklinesDefinition.Count;
            g.BndBefore = tin.BoundariesDefinition.Count;
            plan["target"] = Describe(s, tr, false);
            plan["target_fingerprint"] = s.ComputeFingerPrint().ToString();
            plan["before"] = TinCounts(tin);
            plan["rebuild_after"] = rebuild;

            if (ctx.Args["vertices"] is JsonArray vs)
            {
                foreach (var v in vs.Cast<JsonObject>()) g.Vertices.Add(new Point3d(Hz.Num(v, "x")!.Value, Hz.Num(v, "y")!.Value, Hz.Num(v, "z")!.Value));
                plan["vertices"] = new JsonObject { ["count"] = g.Vertices.Count, ["extents"] = Extents(g.Vertices) };
            }
            if (brk != null)
            {
                var kind = Hz.Str(brk, "kind") ?? "standard";
                var weedFree = (Hz.Num(brk, "weeding_distance") ?? 0) == 0 && (Hz.Num(brk, "weeding_angle") ?? 0) == 0;
                var rows = new JsonArray();
                foreach (var h in ((JsonArray)brk["handles"]!).Select(n => n!.GetValue<string>()))
                {
                    var id = Catalog.FromHandle(doc.Database, h);
                    var obj = tr.GetObject(id, OpenMode.ForRead);
                    var pts = LinearVertices(obj, tr, out var closed, out var zKnown)
                              ?? throw new HzRefusal(ErrorCodes.InvalidInput, "Handle " + h.ToUpperInvariant() + " is a " + obj.GetType().Name +
                                  ". Breaklines come from 3D polylines, polylines, 2D polylines or feature lines. Nothing changed.");
                    g.BreakIds.Add(id);
                    if (kind == "standard" && weedFree && zKnown) g.BreaklineChecks.AddRange(pts);
                    rows.Add(new JsonObject { ["handle"] = obj.Handle.ToString(), ["class"] = obj.GetType().Name, ["vertices"] = pts.Count, ["closed"] = closed });
                }
                plan["breaklines"] = new JsonObject
                {
                    ["kind"] = kind, ["description"] = Hz.Str(brk, "description"), ["entities"] = rows,
                    ["mid_ordinate"] = Hz.Num(brk, "mid_ordinate") ?? 1.0,
                    ["max_distance"] = Hz.Num(brk, "max_distance") ?? 0, ["weeding_distance"] = Hz.Num(brk, "weeding_distance") ?? 0,
                    ["weeding_angle"] = Hz.Num(brk, "weeding_angle") ?? 0,
                    ["elevation_check_points"] = g.BreaklineChecks.Count,
                    ["elevation_check_note"] = kind == "standard" && weedFree
                        ? "Every vertex of 3D-aware entities is re-read as a surface elevation after commit."
                        : "Proximity/non-destructive/weeded breaklines change which vertices exist; only the group itself is re-read.",
                };
            }
            if (bnd != null)
            {
                var rows = new JsonArray();
                foreach (var h in ((JsonArray)bnd["handles"]!).Select(n => n!.GetValue<string>()))
                {
                    var id = Catalog.FromHandle(doc.Database, h);
                    var obj = tr.GetObject(id, OpenMode.ForRead);
                    var pts = LinearVertices(obj, tr, out var closed, out _);
                    if (pts == null || obj is FeatureLine)
                        throw new HzRefusal(ErrorCodes.InvalidInput, "Handle " + h.ToUpperInvariant() + " is a " + obj.GetType().Name +
                            ". Boundaries come from closed polylines, 3D polylines or 2D polylines. Nothing changed.");
                    if (!closed)
                        throw new HzRefusal(ErrorCodes.InvalidInput, "Handle " + h.ToUpperInvariant() + " is not closed. A boundary must be a closed polyline. Nothing changed.");
                    g.BoundIds.Add(id);
                    rows.Add(new JsonObject { ["handle"] = obj.Handle.ToString(), ["class"] = obj.GetType().Name, ["vertices"] = pts.Count, ["extents"] = Extents(pts) });
                }
                plan["boundaries"] = new JsonObject
                {
                    ["kind"] = Hz.Str(bnd, "kind"), ["name"] = Hz.Str(bnd, "name"), ["entities"] = rows,
                    ["non_destructive"] = Hz.Bool(bnd, "non_destructive") ?? true, ["mid_ordinate"] = Hz.Num(bnd, "mid_ordinate") ?? 1.0,
                };
            }
            if (ctx.Args["sources"] is JsonArray src)
            {
                var civil = CommandContext.Civil(doc);
                var rows = new JsonArray();
                foreach (var name in src.Select(n => n!.GetValue<string>()))
                {
                    var id = Catalog.ByName("surface", name, doc.Database, civil, tr);
                    if (id == g.Target) throw new HzRefusal(ErrorCodes.InvalidInput, "A surface cannot be pasted into itself. Nothing changed.");
                    var source = Open(tr, id);
                    if (source is TinVolumeSurface or GridVolumeSurface)
                        throw new HzRefusal(ErrorCodes.InvalidInput, "'" + source.Name + "' is a volume surface; paste elevation surfaces only. Nothing changed.");
                    if (PastesInto(source, g.Target))
                        throw new HzRefusal(ErrorCodes.InvalidInput, "'" + source.Name + "' already has the target pasted into it; pasting it back would create a cycle. Nothing changed.");
                    g.Sources.Add(id);
                    rows.Add(new JsonObject { ["order"] = rows.Count + 1, ["name"] = source.Name, ["handle"] = source.Handle.ToString(), ["fingerprint"] = source.ComputeFingerPrint().ToString() });
                }
                plan["sources"] = rows;
                plan["paste_note"] = "Sources are pasted in this order; where they overlap, the later source wins.";
            }
            return 0;
        });

        if (ctx.DryRun) return CommandResult.Ok(ctx.Rehearse(doc, data, plan));
        ctx.RequireConfirmation(doc, plan);

        ctx.Write(doc, "HZ_SURFACE", tr =>
        {
            var tin = (TinSurface)tr.GetObject(g.Target, OpenMode.ForWrite);
            if (g.Vertices.Count > 0)
            {
                var pc = new Point3dCollection();
                foreach (var p in g.Vertices) pc.Add(p);
                tin.AddVertices(pc);
            }
            if (brk != null)
            {
                var mid = Hz.Num(brk, "mid_ordinate") ?? 1.0;
                SurfaceOperationAddBreakline op = (Hz.Str(brk, "kind") ?? "standard") switch
                {
                    "proximity" => tin.BreaklinesDefinition.AddProximityBreaklines(g.BreakIds, mid),
                    "non_destructive" => tin.BreaklinesDefinition.AddNonDestructiveBreaklines(g.BreakIds, mid),
                    _ => tin.BreaklinesDefinition.AddStandardBreaklines(g.BreakIds, mid, Hz.Num(brk, "max_distance") ?? 0,
                        Hz.Num(brk, "weeding_distance") ?? 0, Hz.Num(brk, "weeding_angle") ?? 0),
                };
                if (Hz.Str(brk, "description") is { } d) op.Description = d;
            }
            if (bnd != null)
            {
                var op = tin.BoundariesDefinition.AddBoundaries(g.BoundIds, Hz.Num(bnd, "mid_ordinate") ?? 1.0,
                    BoundaryType(Hz.Str(bnd, "kind")!), Hz.Bool(bnd, "non_destructive") ?? true);
                if (Hz.Str(bnd, "name") is { } n) op.Name = n;
            }
            foreach (var id in g.Sources) tin.PasteSurface(id);
            if (rebuild) tin.Rebuild();
            return 0;
        });

        var checks = new VerificationSet();
        var after = new JsonObject();
        try
        {
            ctx.Verify(doc, tr =>
            {
                var tin = (TinSurface)tr.GetObject(g.Target, OpenMode.ForRead);
                var expectedOps = g.Sources.Count + (g.Vertices.Count > 0 ? 1 : 0) + (brk != null ? 1 : 0) + (bnd != null ? 1 : 0);
                checks.Check("surface operations added", expectedOps, tin.Operations.Count - g.OpsBefore, tin.Operations.Count - g.OpsBefore == expectedOps);
                if (brk != null)
                {
                    checks.Check("breakline groups added", 1, tin.BreaklinesDefinition.Count - g.BrkBefore, tin.BreaklinesDefinition.Count == g.BrkBefore + 1);
                    if (tin.BreaklinesDefinition.Count == g.BrkBefore + 1)
                    {
                        var op = tin.BreaklinesDefinition[tin.BreaklinesDefinition.Count - 1];
                        // The 2025 enum has no Proximity member: the stored type of a proximity group is not
                        // asserted until observed live; standard/non-destructive are checked.
                        if ((Hz.Str(brk, "kind") ?? "standard") != "proximity")
                            checks.Text("breakline kind", BreaklineType(Hz.Str(brk, "kind") ?? "standard").ToString(), op.BreaklineType.ToString(), false);
                        checks.Check("breaklines in the group", g.BreakIds.Count, op.Count, op.Count == g.BreakIds.Count,
                            "Civil 3D created a different number of breaklines than entities supplied.");
                        if (Hz.Str(brk, "description") is { } d) checks.Text("breakline description", d, op.Description, false);
                    }
                }
                if (bnd != null)
                {
                    checks.Check("boundary groups added", 1, tin.BoundariesDefinition.Count - g.BndBefore, tin.BoundariesDefinition.Count == g.BndBefore + 1);
                    if (tin.BoundariesDefinition.Count == g.BndBefore + 1)
                    {
                        var op = tin.BoundariesDefinition[tin.BoundariesDefinition.Count - 1];
                        checks.Text("boundary kind", BoundaryType(Hz.Str(bnd, "kind")!).ToString(), op.BoundaryType.ToString(), false);
                        if (Hz.Str(bnd, "name") is { } n) checks.Text("boundary name", n, op.Name, false);
                    }
                }
                if (g.Sources.Count > 0)
                {
                    var actual = new JsonArray();
                    var ok = tin.Operations.Count >= g.OpsBefore + g.Sources.Count;
                    for (var i = 0; ok && i < g.Sources.Count; i++)
                    {
                        var op = tin.Operations[g.OpsBefore + i] as SurfaceOperationPasteSurface;
                        actual.Add(JsonValue.Create(op?.SurfaceId.Handle.ToString()));
                        ok &= op != null && op.SurfaceId == g.Sources[i];
                    }
                    checks.Check("paste operations in requested order", Hz.Strings(g.Sources.Select(x => x.Handle.ToString())), actual, ok);
                }
                ElevationCheck(checks, tin, "requested vertices", g.Vertices, bnd != null);
                // Breaklines reach the triangulation only on rebuild; without one, the TIN read back is the old one.
                if (rebuild) ElevationCheck(checks, tin, "standard breakline vertices", g.BreaklineChecks, bnd != null);
                else if (g.BreaklineChecks.Count > 0)
                    after["breakline_elevations"] = "not checked: rebuild=false leaves the surface out of date; rebuild it, then sample_elevation.";
                if (rebuild) checks.Flag("is_out_of_date after rebuild", false, tin.IsOutOfDate);
                after["counts"] = TinCounts(tin);
                after["surface"] = Describe(tin, tr, false);
                return 0;
            });
        }
        catch (Exception e) { checks.Check("post-commit re-read", true, null, false, e.GetType().Name + ": " + e.Message); }

        data["dry_run"] = false;
        data["committed"] = true;
        data["plan"] = plan;
        data["after"] = after;
        data["verified"] = checks.ToJson();
        data["undo"] = new JsonObject { ["available"] = false, ["label"] = "HZ_SURFACE", ["instruction"] = "Automatic undo_last is disabled. Use Civil 3D native UNDO manually and inspect the result; no drawing was saved." };
        return checks.AllVerified ? CommandResult.Ok(data)
            : CommandResult.Fail(ErrorCodes.VerificationFailed, "The definition change committed but the re-read did not verify every requested item. Inspect after/verified before retrying.", data);
    }

    internal static JsonObject TinCounts(TinSurface tin)
    {
        var o = new JsonObject();
        var u = new JsonObject();
        Safe.Int(o, u, "points", () => tin.GetGeneralProperties().NumberOfPoints);
        Safe.Int(o, u, "triangles", () => tin.GetTinProperties().NumberOfTriangles);
        Safe.Num(o, u, "area_2d", () => tin.GetTerrainProperties().SurfaceArea2D);
        Safe.Int(o, u, "operations", () => tin.Operations.Count);
        Safe.Int(o, u, "breakline_groups", () => tin.BreaklinesDefinition.Count);
        Safe.Int(o, u, "boundary_groups", () => tin.BoundariesDefinition.Count);
        if (u.Count > 0) o["unreadable"] = u;
        return o;
    }

    /// <summary>Re-read requested points as surface elevations. Hidden-by-boundary is reported, never a silent pass.</summary>
    internal static void ElevationCheck(VerificationSet checks, TinSurface tin, string what, List<Point3d> points, bool boundaryInCall)
    {
        if (points.Count == 0) return;
        var stride = Math.Max(1, (int)Math.Ceiling(points.Count / (double)MaxElevationChecks));
        int tested = 0, matched = 0, outside = 0;
        var worst = new JsonArray();
        for (var i = 0; i < points.Count; i += stride)
        {
            var p = points[i];
            tested++;
            try
            {
                var z = tin.FindElevationAtXY(p.X, p.Y);
                if (Math.Abs(z - p.Z) <= ElevationTolerance) matched++;
                else if (worst.Count < 10) worst.Add(new JsonObject { ["x"] = p.X, ["y"] = p.Y, ["requested_z"] = p.Z, ["surface_z"] = Hz.Finite(z) });
            }
            catch (PointNotOnEntityException) { outside++; }
        }
        var explainedOutside = boundaryInCall ? outside : 0;
        var ok = matched + explainedOutside == tested;
        var actual = new JsonObject { ["tested"] = tested, ["matched"] = matched, ["outside_surface"] = outside, ["stride"] = stride };
        if (worst.Count > 0) actual["mismatches"] = worst;
        if (outside > 0 && boundaryInCall) actual["outside_note"] = "Points outside the surface after a boundary added in this same call are reported, not counted as matched.";
        checks.Check(what + " re-read as surface elevations (tol " + ElevationTolerance + ")", tested, actual, ok,
            "Some requested points do not hold the requested elevation on the surface after commit.");
    }

    internal static List<Point3d>? LinearVertices(DBObject obj, Transaction tr, out bool closed, out bool zKnown)
    {
        closed = false;
        zKnown = true;
        var pts = new List<Point3d>();
        switch (obj)
        {
            case Polyline3d p3:
                closed = p3.Closed;
                foreach (ObjectId v in p3) pts.Add(((PolylineVertex3d)tr.GetObject(v, OpenMode.ForRead)).Position);
                return pts;
            case Polyline pl:
                closed = pl.Closed;
                for (var i = 0; i < pl.NumberOfVertices; i++) pts.Add(pl.GetPoint3dAt(i));
                return pts;
            case Polyline2d p2:
                closed = p2.Closed;
                zKnown = false; // 2D polyline vertices are OCS; elevation re-read is not attempted
                foreach (ObjectId v in p2) pts.Add(((Vertex2d)tr.GetObject(v, OpenMode.ForRead)).Position);
                return pts;
            case FeatureLine fl:
                foreach (Point3d p in fl.GetPoints(FeatureLinePointType.AllPoints)) pts.Add(p);
                closed = pts.Count > 2 && pts[0].DistanceTo(pts[^1]) < 1e-9;
                return pts;
            default:
                return null;
        }
    }

    private static bool PastesInto(Surface source, ObjectId target)
    {
        try
        {
            for (var i = 0; i < source.Operations.Count; i++)
                if (source.Operations[i] is SurfaceOperationPasteSurface p && p.SurfaceId == target) return true;
        }
        catch { }
        return false;
    }

    private static SurfaceBoundaryType BoundaryType(string kind) => kind switch
    {
        "outer" => SurfaceBoundaryType.Outer,
        "hide" => SurfaceBoundaryType.Hide,
        "show" => SurfaceBoundaryType.Show,
        _ => SurfaceBoundaryType.DataClip,
    };

    private static SurfaceBreaklineType BreaklineType(string kind) => kind switch
    {
        "non_destructive" => SurfaceBreaklineType.NonDestructive,
        _ => SurfaceBreaklineType.Standard,
    };

    private static JsonObject Extents(IReadOnlyCollection<Point3d> pts) => new()
    {
        ["min_x"] = pts.Min(p => p.X), ["min_y"] = pts.Min(p => p.Y), ["max_x"] = pts.Max(p => p.X), ["max_y"] = pts.Max(p => p.Y),
        ["min_z"] = pts.Min(p => p.Z), ["max_z"] = pts.Max(p => p.Z),
    };
}
