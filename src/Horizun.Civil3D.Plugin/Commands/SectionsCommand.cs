// -----------------------------------------------------------------------------
// horizun_c3d_sections - list, get_section, create_sample_lines, create_section_views.
// API: docs/api-probes/2025/AeccDbMgd.phase3-roads-detail.txt, AeccDbMgd.phase3-sections.txt.
//
// Sample lines are created from explicit end points (station, -left) and
// (station, +right), so widths are exact; the re-read measures each vertex's
// station/offset back on the alignment.
// -----------------------------------------------------------------------------
using System.Text.Json.Nodes;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.DatabaseServices;
using Horizun.Civil3D.Core;
using Horizun.Civil3D.Plugin.Civil;
using Section = Autodesk.Civil.DatabaseServices.Section;

namespace Horizun.Civil3D.Plugin.Commands;

internal sealed class SectionsCommand : ICommand
{
    public string Name => "sections";

    public CommandResult Execute(CommandContext ctx)
    {
        if (ToolRules.Validate(ctx.Tool.Name, ctx.Args) is { } why) throw new HzRefusal(ErrorCodes.InvalidInput, why + " Nothing ran.");
        return ctx.Action switch
        {
            "list" => WriteFlow.Read(ctx, (doc, tr, data) => List(ctx, doc, tr, data)),
            "get_section" => WriteFlow.Read(ctx, (doc, tr, data) => GetSection(ctx, doc, tr, data)),
            "create_sample_lines" => CreateLines(ctx),
            _ => CreateViews(ctx),
        };
    }

    private static Alignment Al(CommandContext ctx, Document doc, Transaction tr) =>
        Resolve.Open<Alignment>(tr, Resolve.Named(doc, tr, "alignment", Hz.Str(ctx.Args, "alignment")!));

    private static ObjectId? Group(Alignment al, Transaction tr, string name)
    {
        foreach (ObjectId id in al.GetSampleLineGroupIds())
            if (string.Equals(Catalog.NameOf(id, tr), name, StringComparison.OrdinalIgnoreCase)) return id;
        return null;
    }

    private static ObjectId RequireGroup(Alignment al, Transaction tr, string name)
    {
        if (Group(al, tr, name) is { } id) return id;
        var names = new List<string>();
        foreach (ObjectId g in al.GetSampleLineGroupIds()) names.Add(Catalog.NameOf(g, tr) ?? "");
        throw new HzRefusal(ErrorCodes.NotFound, "Alignment '" + al.Name + "' has no sample line group '" + name + "'. Nothing ran.", new JsonObject { ["candidates"] = Hz.Strings(names) });
    }

    private static JsonObject LineJson(SampleLine sl, Alignment al)
    {
        var verts = new JsonArray();
        foreach (SampleLineVertex v in sl.Vertices)
        {
            double st = 0, off = 0;
            al.StationOffset(v.Location.X, v.Location.Y, ref st, ref off);
            verts.Add(new JsonObject { ["x"] = Hz.Finite(v.Location.X, 6), ["y"] = Hz.Finite(v.Location.Y, 6), ["offset"] = Hz.Finite(off, 6), ["side"] = v.Side.ToString() });
        }
        return new JsonObject { ["name"] = sl.Name, ["handle"] = sl.Handle.ToString(), ["station"] = Hz.Finite(sl.Station, 6), ["number"] = sl.Number, ["vertices"] = verts };
    }

    // Reads open sample lines, groups and sections FOR WRITE inside the always-aborted read transaction:
    // Civil 3D updates pending sections on first access and aborts if they are only open for read (live finding).
    private static void List(CommandContext ctx, Document doc, Transaction tr, JsonObject data)
    {
        var al = Al(ctx, doc, tr);
        var groups = new JsonArray();
        foreach (ObjectId gid in al.GetSampleLineGroupIds())
        {
            var g = (SampleLineGroup)tr.GetObject(gid, OpenMode.ForWrite);
            var lines = new JsonArray();
            foreach (ObjectId lid in g.GetSampleLineIds()) lines.Add(LineJson((SampleLine)tr.GetObject(lid, OpenMode.ForWrite), al));
            var sources = new JsonArray();
            foreach (SectionSource s in g.GetSectionSources())
                sources.Add(new JsonObject { ["name"] = s.SourceName, ["type"] = s.SourceType.ToString(), ["sampled"] = s.IsSampled });
            groups.Add(new JsonObject { ["name"] = g.Name, ["handle"] = g.Handle.ToString(), ["sample_lines"] = lines, ["sources"] = sources, ["section_view_groups"] = g.SectionViewGroups.Count });
        }
        data["alignment"] = al.Name;
        data["groups"] = groups;
    }

    private static void GetSection(CommandContext ctx, Document doc, Transaction tr, JsonObject data)
    {
        var al = Al(ctx, doc, tr);
        var g = (SampleLineGroup)tr.GetObject(RequireGroup(al, tr, Hz.Str(ctx.Args, "group")!), OpenMode.ForWrite);
        var station = Hz.Num(ctx.Args, "station")!.Value;
        SampleLine? line = null;
        var stations = new List<double>();
        foreach (ObjectId lid in g.GetSampleLineIds())
        {
            var sl = (SampleLine)tr.GetObject(lid, OpenMode.ForWrite);
            stations.Add(sl.Station);
            if (Math.Abs(sl.Station - station) < 1e-6) line = sl;
        }
        if (line == null)
            throw new HzRefusal(ErrorCodes.NotFound, "No sample line at station " + station + " in group '" + g.Name + "'.", new JsonObject { ["stations"] = Hz.Arr(stations.Select(s => (JsonNode?)JsonValue.Create(s))) });
        var sections = new JsonArray();
        foreach (SectionSource src in g.GetSectionSources())
        {
            if (!src.IsSampled) continue;
            var o = new JsonObject { ["source"] = src.SourceName, ["type"] = src.SourceType.ToString() };
            try
            {
                var sec = (Section)tr.GetObject(line.GetSectionId(src.SourceId), OpenMode.ForWrite);
                var pts = new JsonArray();
                var ys = new List<double>();
                foreach (SectionPoint p in sec.SectionPoints)
                {
                    pts.Add(new JsonObject { ["x"] = Hz.Finite(p.Location.X, 6), ["y"] = Hz.Finite(p.Location.Y, 6), ["z"] = Hz.Finite(p.Location.Z, 6) });
                    ys.Add(p.Location.Y);
                }
                o["points"] = pts;
                // Live finding: surface sections throw "LeftOffset is not supported by current Section"; each
                // property is optional, and the elevation range falls back to the points (Y = elevation).
                static JsonNode? Opt(Func<double> f) { try { return Hz.Finite(f(), 6); } catch (InvalidOperationException) { return null; } }
                o["left_offset"] = Opt(() => sec.LeftOffset);
                o["right_offset"] = Opt(() => sec.RightOffset);
                var min = Opt(() => sec.MinmumElevation); var max = Opt(() => sec.MaximumElevation);
                o["elevation_source"] = min != null && max != null ? "Section.Minimum/MaximumElevation" : "derived from the section points (Y)";
                o["min_elevation"] = min ?? (ys.Count > 0 ? Hz.Finite(ys.Min(), 6) : null);
                o["max_elevation"] = max ?? (ys.Count > 0 ? Hz.Finite(ys.Max(), 6) : null);
                o["points_note"] = "Raw SectionPoint.Location as Civil 3D returns it (section coordinates: X = offset, Y = elevation is the expected convention; Z kept for transparency).";
            }
            catch (Exception e) { o["points"] = null; o["unreadable_reason"] = e.GetType().Name + ": " + e.Message; }
            sections.Add(o);
        }
        data["alignment"] = al.Name;
        data["group"] = g.Name;
        data["sample_line"] = LineJson(line, al);
        data["sections"] = sections;
        if (sections.Count == 0) data["note"] = "No source is sampled in this group: create_sample_lines with sources, or sample them in Civil 3D.";
    }

    private static CommandResult CreateLines(CommandContext ctx)
    {
        var groupName = Hz.Str(ctx.Args, "group")!;
        var left = Hz.Num(ctx.Args, "left_width")!.Value;
        var right = Hz.Num(ctx.Args, "right_width")!.Value;
        var sources = Resolve.Strings(ctx.Args["sources"]);
        ObjectId alId = ObjectId.Null, groupId = ObjectId.Null;
        var createGroup = false;
        var stations = new List<double>();
        var ends = new List<(Point2d L, Point2d R)>();
        var created = new List<ObjectId>();
        return WriteFlow.Run(ctx, "HZ_SECTIONS",
            (doc, tr, plan) =>
            {
                var al = Al(ctx, doc, tr);
                alId = al.ObjectId;
                if (Group(al, tr, groupName) is { } existing) groupId = existing; else createGroup = true;
                if (ctx.Args["stations"] != null) stations = Resolve.Numbers(ctx.Args["stations"]).OrderBy(s => s).ToList();
                else
                {
                    var iv = Hz.Num(ctx.Args, "interval")!.Value;
                    var s0 = Hz.Num(ctx.Args, "start_station") ?? al.StartingStation;
                    var s1 = Hz.Num(ctx.Args, "end_station") ?? al.EndingStation;
                    for (var s = s0; s <= s1 + 1e-9 && stations.Count <= 2000; s += iv) stations.Add(Math.Min(s, s1));
                    if (stations.Count > 2000) throw new HzRefusal(ErrorCodes.InvalidInput, "interval gives more than 2000 sample lines. Nothing changed.");
                }
                if (stations.Distinct().Count() != stations.Count) throw new HzRefusal(ErrorCodes.InvalidInput, "stations repeat. Nothing changed.");
                var bad = stations.Where(s => s < al.StartingStation - 1e-9 || s > al.EndingStation + 1e-9).ToList();
                if (bad.Count > 0) throw new HzRefusal(ErrorCodes.InvalidInput, "Stations " + string.Join(", ", bad) + " are outside the alignment " + al.StartingStation + "-" + al.EndingStation + ". Nothing changed.");
                if (!createGroup)
                {
                    var g = (SampleLineGroup)tr.GetObject(groupId, OpenMode.ForRead);
                    foreach (ObjectId lid in g.GetSampleLineIds())
                    {
                        var st = ((SampleLine)tr.GetObject(lid, OpenMode.ForRead)).Station;
                        if (stations.Any(s => Math.Abs(s - st) < 1e-6)) throw new HzRefusal(ErrorCodes.InvalidInput, "Group '" + groupName + "' already has a sample line at station " + st + ". Nothing changed.");
                    }
                }
                foreach (var s in sources) Resolve.Named(doc, tr, "surface", s);
                ends.Clear();
                foreach (var s in stations)
                {
                    double x1 = 0, y1 = 0, x2 = 0, y2 = 0;
                    al.PointLocation(s, -left, ref x1, ref y1);
                    al.PointLocation(s, right, ref x2, ref y2);
                    ends.Add((new Point2d(x1, y1), new Point2d(x2, y2)));
                }
                plan["alignment"] = al.Name; plan["group"] = groupName; plan["group_will_be_created"] = createGroup;
                plan["stations"] = Hz.Arr(stations.Select(s => (JsonNode?)JsonValue.Create(Math.Round(s, 9))));
                plan["left_width"] = left; plan["right_width"] = right; plan["sources"] = Hz.Strings(sources);
            },
            (doc, tr) =>
            {
                // Live finding (v0.6.1): holding the alignment open (even for read) while SampleLineGroup.Create /
                // SampleLine.Create modify it made Civil 3D abort with a fatal eNotOpenForWrite. The end points are
                // computed in the plan; here only the native create calls run, with nothing of ours open.
                // Live finding (v0.6.2, second fatal abort): SampleLine.Create also needs its SampleLineGroup OPEN
                // FOR WRITE while it adds the line; otherwise Civil 3D aborts (eNotOpenForWrite, not catchable).
                if (createGroup) { Log.Info("sections: SampleLineGroup.Create"); groupId = SampleLineGroup.Create(groupName, alId); }
                var g = (SampleLineGroup)tr.GetObject(groupId, OpenMode.ForWrite);
                // Live finding (v0.6.7): sampling the sources AFTER creating the lines left every section pending
                // forever (0 points; any later read-only access aborted Civil 3D). Sample first, as the Civil 3D UI
                // does, with dynamic updates, then create the lines so each one builds its sections.
                if (sources.Count > 0)
                    foreach (SectionSource src in g.GetSectionSources())
                        if (sources.Contains(src.SourceName, StringComparer.OrdinalIgnoreCase))
                        {
                            Log.Info("sections: IsSampled " + src.SourceName);
                            src.IsSampled = true;
                            try { src.UpdateMode = SectionUpdateType.Dynamic; } catch (System.Exception e) { Log.Warn("section update mode: " + e.Message); }
                        }
                for (var i = 0; i < stations.Count; i++)
                {
                    Log.Info("sections: SampleLine.Create " + i);
                    created.Add(SampleLine.Create(groupName + " - " + stations[i].ToString("0.###"), groupId, new Point2dCollection { ends[i].L, ends[i].R }));
                }
                // Live finding (v0.6.6): Civil 3D computes section points on first access, and the always-aborted
                // read transactions rolled that computation back, so sections read empty forever. Compute them here,
                // inside the committed write, with every section open for write.
                foreach (var id in created)
                {
                    var sl = (SampleLine)tr.GetObject(id, OpenMode.ForWrite);
                    foreach (ObjectId secId in sl.GetSectionIds())
                    {
                        Log.Info("sections: compute section " + secId.Handle);
                        _ = ((Section)tr.GetObject(secId, OpenMode.ForWrite)).SectionPoints.Count;
                    }
                }
            },
            (doc, tr, v, after) =>
            {
                // Live finding (v0.6.4 stage log): apply and commit succeed, then reading the new sample lines in the
                // verify transaction aborted Civil 3D (eNotOpenForWrite): Civil updates the pending sections on first
                // access. Open them for write here; the verify transaction is always aborted, so nothing is changed.
                var al = (Alignment)tr.GetObject(alId, OpenMode.ForRead);
                var g = (SampleLineGroup)tr.GetObject(groupId, OpenMode.ForWrite);
                v.Check("sample lines created", stations.Count, created.Count(id => !id.IsNull && !id.IsErased), created.All(id => !id.IsNull && !id.IsErased) && created.Count == stations.Count);
                var lines = new JsonArray();
                for (var i = 0; i < created.Count; i++)
                {
                    var sl = (SampleLine)tr.GetObject(created[i], OpenMode.ForWrite);
                    v.Number("line " + i + " station", stations[i], sl.Station, 1e-6);
                    var offs = new List<double>();
                    foreach (SampleLineVertex vx in sl.Vertices) { double st = 0, off = 0; al.StationOffset(vx.Location.X, vx.Location.Y, ref st, ref off); offs.Add(off); }
                    v.Number("line " + i + " left width", left, offs.Count > 0 ? -offs.Min() : double.NaN, 1e-4);
                    v.Number("line " + i + " right width", right, offs.Count > 0 ? offs.Max() : double.NaN, 1e-4);
                    lines.Add(LineJson(sl, al));
                }
                foreach (var s in sources)
                {
                    var sampled = false;
                    foreach (SectionSource src in g.GetSectionSources()) if (string.Equals(src.SourceName, s, StringComparison.OrdinalIgnoreCase)) sampled = src.IsSampled;
                    v.Flag("source " + s + " sampled", true, sampled);
                }
                after["group"] = g.Name; after["sample_lines"] = lines;
            });
    }

    private static CommandResult CreateViews(CommandContext ctx)
    {
        var insert = Resolve.P(ctx.Args["insert"]);
        ObjectId groupId = ObjectId.Null;
        int before = 0, lineCount = 0;
        return WriteFlow.Run(ctx, "HZ_SECTIONS",
            (doc, tr, plan) =>
            {
                var al = Al(ctx, doc, tr);
                groupId = RequireGroup(al, tr, Hz.Str(ctx.Args, "group")!);
                var g = (SampleLineGroup)tr.GetObject(groupId, OpenMode.ForRead);
                before = g.SectionViewGroups.Count;
                lineCount = g.GetSampleLineIds().Count;
                if (lineCount == 0) throw new HzRefusal(ErrorCodes.InvalidInput, "Group '" + g.Name + "' has no sample lines. Nothing changed.");
                plan["alignment"] = al.Name; plan["group"] = g.Name; plan["sample_lines"] = lineCount; plan["insert"] = Resolve.Json(insert, false);
                plan["section_view_groups_before"] = before;
            },
            (doc, tr) => ((SampleLineGroup)tr.GetObject(groupId, OpenMode.ForWrite)).SectionViewGroups.Add(insert),
            (doc, tr, v, after) =>
            {
                var g = (SampleLineGroup)tr.GetObject(groupId, OpenMode.ForWrite);
                v.Check("section view groups", before + 1, g.SectionViewGroups.Count, g.SectionViewGroups.Count == before + 1);
                if (g.SectionViewGroups.Count == before + 1)
                {
                    var views = g.SectionViewGroups[g.SectionViewGroups.Count - 1].GetSectionViewIds().Count;
                    v.Check("one section view per sample line", lineCount, views, views == lineCount);
                    after["section_views"] = views;
                }
            });
    }
}
