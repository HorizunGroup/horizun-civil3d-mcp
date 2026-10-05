// -----------------------------------------------------------------------------
// horizun_c3d_points - COGO points and point groups (block D).
// API: docs/api-probes/2025/AeccDbMgd.phase3-points.txt.
// Point files are parsed/written by Core.PointFile (unit tested), not by the
// Civil 3D importer, so bad lines are reported instead of guessed.
// -----------------------------------------------------------------------------
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Horizun.Civil3D.Core;
using Horizun.Civil3D.Plugin.Civil;
using CivilSurface = Autodesk.Civil.DatabaseServices.Surface;

namespace Horizun.Civil3D.Plugin.Commands;

internal sealed class PointsCommand : ICommand
{
    public string Name => "points";

    public CommandResult Execute(CommandContext ctx)
    {
        if (ToolRules.Validate(ctx.Tool.Name, ctx.Args) is { } why) throw new HzRefusal(ErrorCodes.InvalidInput, why + " Nothing ran.");
        return ctx.Action switch
        {
            "list" => WriteFlow.Read(ctx, (doc, tr, data) => List(ctx, doc, tr, data)),
            "groups" => WriteFlow.Read(ctx, Groups),
            "create" => Create(ctx, null),
            "import" => Create(ctx, ReadFile(ctx)),
            "export_csv" => ExportCsv(ctx),
            "elevations_from_surface" => FromSurface(ctx),
            "group_create" => GroupCreate(ctx),
            _ => Erase(ctx),
        };
    }

    private static CivilDocument Civ(Document doc) => CommandContext.Civil(doc);

    private static JsonObject PointJson(CogoPoint p) => new()
    {
        ["number"] = (long)p.PointNumber, ["name"] = p.PointName, ["x"] = Hz.Finite(p.Easting, 6), ["y"] = Hz.Finite(p.Northing, 6),
        ["z"] = Hz.Finite(p.Elevation, 6), ["raw_description"] = p.RawDescription, ["full_description"] = p.FullDescription, ["handle"] = p.Handle.ToString(),
    };

    private static ObjectId GroupId(Document doc, Transaction tr, string name)
    {
        var pg = Civ(doc).PointGroups;
        var names = new List<string>();
        foreach (ObjectId id in pg)
        {
            var g = (PointGroup)tr.GetObject(id, OpenMode.ForRead);
            names.Add(g.Name);
            if (g.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return id;
        }
        throw new HzRefusal(ErrorCodes.NotFound, "No point group '" + name + "'. Nothing changed.", new JsonObject { ["candidates"] = Hz.Strings(names) });
    }

    /// <summary>Points selected by group or number set (or all).</summary>
    private static List<CogoPoint> Select(CommandContext ctx, Document doc, Transaction tr)
    {
        var civ = Civ(doc);
        if (Hz.Str(ctx.Args, "group") is { } g && ctx.Action is "list" or "elevations_from_surface" or "export_csv")
        {
            var grp = (PointGroup)tr.GetObject(GroupId(doc, tr, g), OpenMode.ForRead);
            return grp.GetPointNumbers().Where(civ.CogoPoints.Contains).Select(n => (CogoPoint)tr.GetObject(civ.CogoPoints.GetPointByPointNumber(n), OpenMode.ForRead)).ToList();
        }
        var set = Hz.Str(ctx.Args, "numbers") is { } s ? NumberSet.Parse(s) : null;
        var list = new List<CogoPoint>();
        foreach (ObjectId id in civ.CogoPoints)
        {
            var p = (CogoPoint)tr.GetObject(id, OpenMode.ForRead);
            if (set == null || NumberSet.Contains(set, p.PointNumber)) list.Add(p);
        }
        return list;
    }

    private static void List(CommandContext ctx, Document doc, Transaction tr, JsonObject data)
    {
        var limit = (int)(Hz.Num(ctx.Args, "limit") ?? 1000);
        var pts = Select(ctx, doc, tr);
        data["count"] = pts.Count;
        data["points"] = new JsonArray(pts.OrderBy(p => p.PointNumber).Take(limit).Select(p => (JsonNode)PointJson(p)).ToArray());
        data["truncated"] = pts.Count > limit;
    }

    private static void Groups(Document doc, Transaction tr, JsonObject data)
    {
        var rows = new JsonArray();
        foreach (ObjectId id in Civ(doc).PointGroups)
        {
            var g = (PointGroup)tr.GetObject(id, OpenMode.ForRead);
            rows.Add(new JsonObject { ["name"] = g.Name, ["points"] = (long)g.PointsCount, ["description"] = g.Description, ["handle"] = g.Handle.ToString() });
        }
        data["groups"] = rows;
        data["total_points"] = (long)Civ(doc).CogoPoints.Count;
    }

    // ---- create / import -----------------------------------------------------------

    private static List<PointFile.Row> ReadFile(CommandContext ctx)
    {
        var file = Hz.Str(ctx.Args, "file")!;
        if (!File.Exists(file)) throw new HzRefusal(ErrorCodes.NotFound, "Point file not found: " + file + ". Nothing changed.");
        var rows = PointFile.Parse(File.ReadAllText(file, Encoding.UTF8), Hz.Str(ctx.Args, "format")!, Hz.Bool(ctx.Args, "skip_header") ?? false, out var errors);
        if (errors.Count > 0)
            throw new HzRefusal(ErrorCodes.InvalidInput, errors.Count + " line(s) do not match " + Hz.Str(ctx.Args, "format") + "; fix the file or the format. Nothing changed.", new JsonObject { ["errors"] = Hz.Strings(errors) });
        if (rows.Count == 0) throw new HzRefusal(ErrorCodes.InvalidInput, "The file has no points. Nothing changed.");
        if (rows.Count > 100000) throw new HzRefusal(ErrorCodes.InvalidInput, "More than 100000 points. Nothing changed.");
        return rows;
    }

    private static CommandResult Create(CommandContext ctx, List<PointFile.Row>? fileRows)
    {
        var rows = fileRows ?? ((JsonArray)ctx.Args["points"]!).Select(n =>
        {
            var o = (JsonObject)n!;
            return (Row: new PointFile.Row(Hz.Num(o, "number") is { } nn ? (uint)nn : null, Hz.Num(o, "x")!.Value, Hz.Num(o, "y")!.Value, Hz.Num(o, "z") ?? 0, Hz.Str(o, "description") ?? ""), Name: Hz.Str(o, "name"));
        }).Select(t => t.Row with { }).ToList();
        var names = fileRows == null ? ((JsonArray)ctx.Args["points"]!).Select(n => Hz.Str((JsonObject)n!, "name")).ToList() : rows.Select(_ => (string?)null).ToList();
        var groupName = Hz.Str(ctx.Args, "group");
        ObjectId groupId = ObjectId.Null;
        var created = new List<ObjectId>();
        return WriteFlow.Run(ctx, "HZ_POINTS",
            (doc, tr, plan) =>
            {
                var cp = Civ(doc).CogoPoints;
                var nums = rows.Where(r => r.Number != null).Select(r => r.Number!.Value).ToList();
                var dup = nums.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
                if (dup.Count > 0) throw new HzRefusal(ErrorCodes.InvalidInput, "Point numbers repeat in the request: " + string.Join(", ", dup.Take(20)) + ". Nothing changed.");
                var taken = nums.Where(cp.Contains).ToList();
                if (taken.Count > 0) throw new HzRefusal(ErrorCodes.InvalidInput, taken.Count + " point number(s) already exist (e.g. " + string.Join(", ", taken.Take(10)) + "); existing points are never overwritten. Nothing changed.");
                if (groupName != null) groupId = GroupId(doc, tr, groupName);
                plan["points"] = rows.Count; plan["with_numbers"] = nums.Count; plan["group"] = groupName;
                plan["first"] = new JsonArray(rows.Take(5).Select(r => (JsonNode)new JsonObject { ["number"] = r.Number is { } n ? (long)n : null, ["x"] = r.X, ["y"] = r.Y, ["z"] = r.Z, ["description"] = r.Description }).ToArray());
                if (fileRows != null) plan["file"] = Hz.Str(ctx.Args, "file");
            },
            (doc, tr) =>
            {
                var cp = Civ(doc).CogoPoints;
                for (var i = 0; i < rows.Count; i++)
                {
                    var r = rows[i];
                    var id = cp.Add(new Point3d(r.X, r.Y, r.Z), r.Description, true);
                    var p = (CogoPoint)tr.GetObject(id, OpenMode.ForWrite);
                    if (r.Number is { } n) p.PointNumber = n;
                    if (names[i] is { } nm) p.PointName = nm;
                    created.Add(id);
                }
                if (!groupId.IsNull)
                {
                    var g = (PointGroup)tr.GetObject(groupId, OpenMode.ForWrite);
                    var q = g.GetQuery() as StandardPointGroupQuery ?? new StandardPointGroupQuery();
                    var added = string.Join(",", created.Select(id => ((CogoPoint)tr.GetObject(id, OpenMode.ForRead)).PointNumber));
                    q.IncludeNumbers = string.IsNullOrWhiteSpace(q.IncludeNumbers) ? added : q.IncludeNumbers + "," + added;
                    g.SetQuery(q);
                    g.Update();
                }
            },
            (doc, tr, v, after) =>
            {
                v.Check("points created", rows.Count, created.Count(id => !id.IsErased), created.Count == rows.Count);
                var bad = 0;
                for (var i = 0; i < created.Count; i++)
                {
                    var p = (CogoPoint)tr.GetObject(created[i], OpenMode.ForRead);
                    var r = rows[i];
                    var ok = Math.Abs(p.Easting - r.X) <= 1e-6 && Math.Abs(p.Northing - r.Y) <= 1e-6 && Math.Abs(p.Elevation - r.Z) <= 1e-6
                             && p.RawDescription == r.Description && (r.Number == null || p.PointNumber == r.Number);
                    if (!ok && bad++ < 20) v.Check("point " + p.PointNumber, new JsonObject { ["x"] = r.X, ["y"] = r.Y, ["z"] = r.Z, ["description"] = r.Description }, PointJson(p), false);
                }
                v.Check("points matching E/N/Z/description/number", created.Count, created.Count - bad, bad == 0);
                if (!groupId.IsNull)
                {
                    var g = (PointGroup)tr.GetObject(groupId, OpenMode.ForRead);
                    var missing = created.Count(id => !g.ContainsPoint(((CogoPoint)tr.GetObject(id, OpenMode.ForRead)).PointNumber));
                    v.Check("points in group " + g.Name, created.Count, created.Count - missing, missing == 0);
                }
                after["points"] = new JsonArray(created.Take(50).Select(id => (JsonNode)PointJson((CogoPoint)tr.GetObject(id, OpenMode.ForRead))).ToArray());
            });
    }

    // ---- export / surface / groups / erase -------------------------------------------

    private static CommandResult ExportCsv(CommandContext ctx)
    {
        var output = Hz.Str(ctx.Args, "output")!;
        var format = Hz.Str(ctx.Args, "format") ?? "PNEZD";
        var rows = new List<PointFile.Row>();
        return WriteFlow.Run(ctx, "HZ_POINTS",
            (doc, tr, plan) =>
            {
                if (File.Exists(output)) throw new HzRefusal(ErrorCodes.InvalidInput, "The output file exists; this action never overwrites. Nothing written.");
                if (Path.GetDirectoryName(output) is not { } dir || !Directory.Exists(dir)) throw new HzRefusal(ErrorCodes.NotFound, "The output folder does not exist. Nothing written.");
                rows = Select(ctx, doc, tr).OrderBy(p => p.PointNumber).Select(p => new PointFile.Row(p.PointNumber, p.Easting, p.Northing, p.Elevation, p.RawDescription)).ToList();
                if (rows.Count == 0) throw new HzRefusal(ErrorCodes.InvalidInput, "No points to export. Nothing written.");
                plan["output"] = output; plan["format"] = format; plan["points"] = rows.Count;
            },
            (doc, tr) => File.WriteAllText(output, PointFile.Write(rows, format), new UTF8Encoding(false)),
            (doc, tr, v, after) =>
            {
                v.Flag("file written", true, File.Exists(output));
                if (!File.Exists(output)) return;
                var back = PointFile.Parse(File.ReadAllText(output), format, false, out var errors);
                v.Check("rows read back", rows.Count, back.Count, back.Count == rows.Count && errors.Count == 0);
                var worst = back.Zip(rows).Select(z => Math.Max(Math.Abs(z.First.X - z.Second.X), Math.Max(Math.Abs(z.First.Y - z.Second.Y), Math.Abs(z.First.Z - z.Second.Z)))).DefaultIfEmpty(0).Max();
                v.Check("max coordinate difference (4 decimals written)", "<= 0.00005", Hz.Finite(worst, 8), worst <= 0.00005 + 1e-12);
                after["file"] = new JsonObject { ["path"] = output, ["bytes"] = new FileInfo(output).Length, ["points"] = back.Count };
            });
    }

    private static CommandResult FromSurface(CommandContext ctx)
    {
        var ids = new List<ObjectId>();
        var expected = new List<double>();
        var sfId = ObjectId.Null;
        return WriteFlow.Run(ctx, "HZ_POINTS",
            (doc, tr, plan) =>
            {
                sfId = Resolve.Named(doc, tr, "surface", Hz.Str(ctx.Args, "surface")!);
                var s = (CivilSurface)tr.GetObject(sfId, OpenMode.ForRead);
                var outside = new List<uint>();
                foreach (var p in Select(ctx, doc, tr))
                {
                    try { expected.Add(s.FindElevationAtXY(p.Easting, p.Northing)); ids.Add(p.ObjectId); }
                    catch (System.Exception) { outside.Add(p.PointNumber); }
                }
                if (outside.Count > 0) throw new HzRefusal(ErrorCodes.InvalidInput, outside.Count + " point(s) are outside the surface (e.g. " + string.Join(", ", outside.Take(10)) + "). Nothing changed.");
                if (ids.Count == 0) throw new HzRefusal(ErrorCodes.InvalidInput, "No points selected. Nothing changed.");
                plan["surface"] = s.Name; plan["points"] = ids.Count;
            },
            (doc, tr) => { foreach (var id in ids) Civ(doc).CogoPoints.SetElevationBySurface(id, sfId); },
            (doc, tr, v, after) =>
            {
                var bad = 0;
                for (var i = 0; i < ids.Count; i++)
                {
                    var p = (CogoPoint)tr.GetObject(ids[i], OpenMode.ForRead);
                    if (Math.Abs(p.Elevation - expected[i]) > 1e-6 && bad++ < 20) v.Number("point " + p.PointNumber + " elevation", expected[i], p.Elevation, 1e-6);
                }
                v.Check("points on the surface", ids.Count, ids.Count - bad, bad == 0);
                after["updated"] = ids.Count;
            });
    }

    private static Regex Like(string patterns) =>
        new("^(" + string.Join("|", patterns.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => Regex.Escape(p).Replace("\\*", ".*").Replace("\\?", "."))) + ")$", RegexOptions.IgnoreCase);

    private static CommandResult GroupCreate(CommandContext ctx)
    {
        var name = Hz.Str(ctx.Args, "new_name")!;
        var inNum = Hz.Str(ctx.Args, "include_numbers"); var exNum = Hz.Str(ctx.Args, "exclude_numbers");
        var inRaw = Hz.Str(ctx.Args, "include_raw_descriptions"); var inFull = Hz.Str(ctx.Args, "include_full_descriptions"); var inName = Hz.Str(ctx.Args, "include_names");
        var expected = new HashSet<uint>();
        var id = ObjectId.Null;
        return WriteFlow.Run(ctx, "HZ_POINTS",
            (doc, tr, plan) =>
            {
                foreach (ObjectId gid in Civ(doc).PointGroups)
                    if (((PointGroup)tr.GetObject(gid, OpenMode.ForRead)).Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                        throw new HzRefusal(ErrorCodes.InvalidInput, "Point group '" + name + "' exists. Nothing changed.");
                var nIn = inNum != null ? NumberSet.Parse(inNum) : null; var nEx = exNum != null ? NumberSet.Parse(exNum) : null;
                var rRaw = inRaw != null ? Like(inRaw) : null; var rFull = inFull != null ? Like(inFull) : null; var rName = inName != null ? Like(inName) : null;
                foreach (ObjectId pid in Civ(doc).CogoPoints)
                {
                    var p = (CogoPoint)tr.GetObject(pid, OpenMode.ForRead);
                    var inc = (nIn != null && NumberSet.Contains(nIn, p.PointNumber)) || (rRaw?.IsMatch(p.RawDescription ?? "") ?? false)
                              || (rFull?.IsMatch(p.FullDescription ?? "") ?? false) || (rName?.IsMatch(p.PointName ?? "") ?? false);
                    if (inc && !(nEx != null && NumberSet.Contains(nEx, p.PointNumber))) expected.Add(p.PointNumber);
                }
                plan["new_name"] = name; plan["expected_points"] = expected.Count;
                plan["query"] = Hz.Without(ctx.Args, "action", "target_document", "dry_run", "confirmation_token", "new_name", "description");
            },
            (doc, tr) =>
            {
                id = Civ(doc).PointGroups.Add(name);
                var g = (PointGroup)tr.GetObject(id, OpenMode.ForWrite);
                if (Hz.Str(ctx.Args, "description") is { } d) g.Description = d;
                var q = new StandardPointGroupQuery();
                if (inNum != null) q.IncludeNumbers = inNum;
                if (exNum != null) q.ExcludeNumbers = exNum;
                if (inRaw != null) q.IncludeRawDescriptions = inRaw;
                if (inFull != null) q.IncludeFullDescriptions = inFull;
                if (inName != null) q.IncludeNames = inName;
                g.SetQuery(q);
                g.Update();
            },
            (doc, tr, v, after) =>
            {
                var g = (PointGroup)tr.GetObject(id, OpenMode.ForRead);
                var got = g.GetPointNumbers().ToHashSet();
                v.Check("group membership equals our evaluation of the query", expected.Count, got.Count, got.SetEquals(expected),
                    got.SetEquals(expected) ? null : "Civil 3D query semantics differ: only in group " + string.Join(",", got.Except(expected).Take(10)) + "; only expected " + string.Join(",", expected.Except(got).Take(10)));
                after["group"] = new JsonObject { ["name"] = g.Name, ["points"] = got.Count };
            });
    }

    private static CommandResult Erase(CommandContext ctx)
    {
        var ids = new List<(ObjectId Id, uint Number)>();
        return WriteFlow.Run(ctx, "HZ_POINTS",
            (doc, tr, plan) =>
            {
                foreach (var p in Select(ctx, doc, tr)) { Resolve.Editable(p, tr); ids.Add((p.ObjectId, p.PointNumber)); }
                if (ids.Count == 0) throw new HzRefusal(ErrorCodes.NotFound, "No points with those numbers. Nothing changed.");
                plan["erase"] = ids.Count; plan["numbers"] = Hz.Str(ctx.Args, "numbers");
            },
            (doc, tr) => { foreach (var (id, _) in ids) Civ(doc).CogoPoints.Remove(id); },
            (doc, tr, v, after) =>
            {
                var left = ids.Count(x => Civ(doc).CogoPoints.Contains(x.Number));
                v.Check("points removed", ids.Count, ids.Count - left, left == 0);
                after["erased"] = ids.Count - left;
            });
    }
}
