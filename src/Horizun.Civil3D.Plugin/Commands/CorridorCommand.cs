// -----------------------------------------------------------------------------
// horizun_c3d_corridor - get, assembly_list, assembly_create, assembly_import,
// create, add_region, rebuild, create_surface.
// API: docs/api-probes/2025/AeccDbMgd.phase3-roads*.txt (CorridorCollection,
// BaselineCollection, BaselineRegionCollection, AppliedAssemblySetting,
// CorridorSurfaceCollection, AssemblyCollection).
//
// Subassemblies are NOT authored here (Civil 3D builds them from .NET/PKT
// catalogues with tool palettes); assembly_import copies a complete assembly
// from another drawing, which is the reliable route to a real section.
// -----------------------------------------------------------------------------
using System.Text.Json.Nodes;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Horizun.Civil3D.Core;
using Horizun.Civil3D.Plugin.Civil;

namespace Horizun.Civil3D.Plugin.Commands;

internal sealed partial class CorridorCommand : ICommand
{
    public string Name => "corridor";

    public CommandResult Execute(CommandContext ctx)
    {
        if (ToolRules.Validate(ctx.Tool.Name, ctx.Args) is { } why) throw new HzRefusal(ErrorCodes.InvalidInput, why + " Nothing ran.");
        return ctx.Action switch
        {
            "get" => WriteFlow.Read(ctx, (doc, tr, data) => data["corridor"] = Describe(Resolve.Open<Corridor>(tr, Resolve.Target(doc, tr, "corridor", ctx.Args)), tr)),
            "assembly_list" => WriteFlow.Read(ctx, AssemblyList),
            "assembly_create" => AssemblyCreate(ctx),
            "assembly_import" => AssemblyImport(ctx),
            "create" => Create(ctx),
            "add_region" => AddRegion(ctx),
            "rebuild" => Rebuild(ctx),
            "get_targets" => GetTargets(ctx),
            "set_targets" => SetTargets(ctx),
            "applied_geometry" => AppliedGeometry(ctx, false),
            "region_quantities" => AppliedGeometry(ctx, true),
            "split_region" => RestructureRegion(ctx, false),
            "merge_regions" => RestructureRegion(ctx, true),
            _ => CreateSurface(ctx),
        };
    }

    private static CivilDocument Civ(Document doc) => CommandContext.Civil(doc);

    // ---- read ---------------------------------------------------------------

    internal static JsonObject Describe(Corridor c, Transaction tr)
    {
        var baselines = new JsonArray();
        foreach (Baseline b in c.Baselines)
        {
            var regions = new JsonArray();
            foreach (BaselineRegion r in b.BaselineRegions)
            {
                var s = r.AppliedAssemblySetting;
                double[] stations;
                try { stations = r.SortedStations(); } catch { stations = Array.Empty<double>(); }
                regions.Add(new JsonObject
                {
                    ["name"] = r.Name,
                    ["assembly"] = Catalog.NameOf(r.AssemblyId, tr),
                    ["start_station"] = Hz.Finite(r.StartStation, 6),
                    ["end_station"] = Hz.Finite(r.EndStation, 6),
                    ["frequency"] = new JsonObject
                    {
                        ["tangents"] = Hz.Finite(s.FrequencyAlongTangents, 6), ["curves"] = Hz.Finite(s.FrequencyAlongCurves, 6),
                        ["spirals"] = Hz.Finite(s.FrequencyAlongSpirals, 6), ["profile_curves"] = Hz.Finite(s.FrequencyAlongProfileCurves, 6),
                    },
                    ["applied_stations"] = stations.Length,
                });
            }
            baselines.Add(new JsonObject
            {
                ["name"] = b.Name,
                ["alignment"] = b.AlignmentId.IsNull ? null : Catalog.NameOf(b.AlignmentId, tr),
                ["profile"] = b.ProfileId.IsNull ? null : Catalog.NameOf(b.ProfileId, tr),
                ["regions"] = regions,
            });
        }
        var surfaces = new JsonArray();
        foreach (CorridorSurface s in c.CorridorSurfaces)
            surfaces.Add(new JsonObject
            {
                ["name"] = s.Name, ["surface_handle"] = s.SurfaceId.IsNull ? null : s.SurfaceId.Handle.ToString(),
                ["link_codes"] = Hz.Strings(s.LinkCodes()), ["feature_line_codes"] = Hz.Strings(s.FeatureLineCodes()),
            });
        string[] Codes(Func<string[]> f) { try { return f(); } catch { return Array.Empty<string>(); } }
        return new JsonObject
        {
            ["name"] = c.Name, ["handle"] = c.Handle.ToString(), ["out_of_date"] = c.IsOutOfDate, ["rebuild_automatic"] = c.RebuildAutomatic,
            ["baselines"] = baselines, ["surfaces"] = surfaces,
            ["link_codes"] = Hz.Strings(Codes(c.GetLinkCodes)), ["point_codes"] = Hz.Strings(Codes(c.GetPointCodes)), ["shape_codes"] = Hz.Strings(Codes(c.GetShapeCodes)),
        };
    }

    private static JsonObject AssemblyJson(Assembly a, Transaction tr)
    {
        var groups = new JsonArray();
        foreach (AssemblyGroup g in a.Groups)
        {
            var subs = new JsonArray();
            foreach (ObjectId sid in g.GetSubassemblyIds()) subs.Add(JsonValue.Create(Catalog.NameOf(sid, tr) ?? sid.Handle.ToString()));
            groups.Add(new JsonObject { ["name"] = g.Name, ["subassemblies"] = subs });
        }
        return new JsonObject { ["name"] = a.Name, ["handle"] = a.Handle.ToString(), ["type"] = a.Type.ToString(), ["location"] = Resolve.Json(a.Location), ["groups"] = groups };
    }

    private static void AssemblyList(Document doc, Transaction tr, JsonObject data)
    {
        var list = new JsonArray();
        foreach (ObjectId id in Civ(doc).AssemblyCollection) list.Add(AssemblyJson((Assembly)tr.GetObject(id, OpenMode.ForRead), tr));
        data["assemblies"] = list;
        var subs = new JsonArray();
        foreach (ObjectId id in Civ(doc).SubassemblyCollection) subs.Add(JsonValue.Create(Catalog.NameOf(id, tr) ?? id.Handle.ToString()));
        data["subassemblies"] = subs;
    }

    // ---- assemblies ---------------------------------------------------------

    private static CommandResult AssemblyCreate(CommandContext ctx)
    {
        var name = Hz.Str(ctx.Args, "new_name")!;
        var insert = Resolve.P(ctx.Args["insert"]);
        var type = (AssemblyType)Enum.Parse(typeof(AssemblyType), Hz.Str(ctx.Args, "assembly_type") ?? "UndividedCrownedRoad");
        var id = ObjectId.Null;
        return WriteFlow.Run(ctx, "HZ_CORRIDOR",
            (doc, tr, plan) =>
            {
                Resolve.Unique(doc, tr, "assembly", name);
                plan["new_name"] = name; plan["assembly_type"] = type.ToString(); plan["insert"] = Resolve.Json(insert, false);
                plan["note"] = "Creates an empty assembly (baseline marker only). Add subassemblies in Civil 3D or use assembly_import for a complete section.";
            },
            (doc, tr) => id = Civ(doc).AssemblyCollection.Add(name, type, insert),
            (doc, tr, v, after) =>
            {
                var a = (Assembly)tr.GetObject(id, OpenMode.ForRead);
                v.Text("name", name, a.Name);
                v.Text("type", type.ToString(), a.Type.ToString());
                v.Number("location x", insert.X, a.Location.X, 1e-6);
                v.Number("location y", insert.Y, a.Location.Y, 1e-6);
                after["assembly"] = AssemblyJson(a, tr);
            });
    }

    private static CommandResult AssemblyImport(CommandContext ctx)
    {
        var name = Hz.Str(ctx.Args, "new_name")!;
        var path = Hz.Str(ctx.Args, "source_dwg")!;
        var source = Hz.Str(ctx.Args, "source_assembly")!;
        var insert = Resolve.P(ctx.Args["insert"]);
        var id = ObjectId.Null;
        return WriteFlow.Run(ctx, "HZ_CORRIDOR",
            (doc, tr, plan) =>
            {
                Resolve.Unique(doc, tr, "assembly", name);
                if (!Path.IsPathRooted(path) || !File.Exists(path)) throw new HzRefusal(ErrorCodes.NotFound, "source_dwg must be an existing absolute .dwg path: " + path + ". Nothing changed.");
                if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(doc.Name), StringComparison.OrdinalIgnoreCase))
                    throw new HzRefusal(ErrorCodes.InvalidInput, "source_dwg is the target drawing itself. Nothing changed.");
                using (var src = new Database(false, true))
                {
                    src.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, true, null);
                    var found = false;
                    var names = new List<string>();
                    using (var str = src.TransactionManager.StartTransaction())
                    {
                        foreach (ObjectId aid in CivilDocument.GetCivilDocument(src).AssemblyCollection)
                        {
                            var n = ((Assembly)str.GetObject(aid, OpenMode.ForRead)).Name;
                            names.Add(n);
                            if (string.Equals(n, source, StringComparison.OrdinalIgnoreCase)) found = true;
                        }
                        str.Commit();
                    }
                    if (!found) throw new HzRefusal(ErrorCodes.NotFound, "'" + Path.GetFileName(path) + "' has no assembly '" + source + "'. Nothing changed.", new JsonObject { ["candidates"] = Hz.Strings(names) });
                }
                plan["new_name"] = name; plan["source_dwg"] = path; plan["source_assembly"] = source; plan["insert"] = Resolve.Json(insert, false);
            },
            (doc, tr) =>
            {
                using var src = new Database(false, true);
                src.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, true, null);
                id = Civ(doc).AssemblyCollection.ImportAssembly(name, src, source, insert);
            },
            (doc, tr, v, after) =>
            {
                var a = (Assembly)tr.GetObject(id, OpenMode.ForRead);
                v.Text("name", name, a.Name);
                var subs = 0;
                foreach (AssemblyGroup g in a.Groups) subs += g.GetSubassemblyIds().Count;
                v.Check("subassemblies imported", "> 0", subs, subs > 0, "The source assembly arrived without subassemblies.");
                after["assembly"] = AssemblyJson(a, tr);
            });
    }

    // ---- corridors ----------------------------------------------------------

    private static void ApplyFrequency(BaselineRegion r, JsonObject? f)
    {
        if (f == null) return;
        var s = r.AppliedAssemblySetting;
        if (Hz.Num(f, "tangents") is { } t) s.FrequencyAlongTangents = t;
        if (Hz.Num(f, "curves") is { } c) s.FrequencyAlongCurves = c;
        if (Hz.Num(f, "spirals") is { } sp) s.FrequencyAlongSpirals = sp;
        if (Hz.Num(f, "profile_curves") is { } pc) s.FrequencyAlongProfileCurves = pc;
    }

    private static void CheckFrequency(VerificationSet v, BaselineRegion r, JsonObject? f)
    {
        if (f == null) return;
        var s = r.AppliedAssemblySetting;
        if (Hz.Num(f, "tangents") is { } t) v.Number("frequency tangents", t, s.FrequencyAlongTangents, 1e-9);
        if (Hz.Num(f, "curves") is { } c) v.Number("frequency curves", c, s.FrequencyAlongCurves, 1e-9);
        if (Hz.Num(f, "spirals") is { } sp) v.Number("frequency spirals", sp, s.FrequencyAlongSpirals, 1e-9);
        if (Hz.Num(f, "profile_curves") is { } pc) v.Number("frequency profile curves", pc, s.FrequencyAlongProfileCurves, 1e-9);
    }

    private static CommandResult Create(CommandContext ctx)
    {
        var name = Hz.Str(ctx.Args, "new_name")!;
        var freq = ctx.Args["frequency"] as JsonObject;
        var rebuild = Hz.Bool(ctx.Args, "rebuild") ?? true;
        ObjectId alId = ObjectId.Null, prId = ObjectId.Null, asId = ObjectId.Null, id = ObjectId.Null;
        double s0 = 0, s1 = 0;
        return WriteFlow.Run(ctx, "HZ_CORRIDOR",
            (doc, tr, plan) =>
            {
                Resolve.Unique(doc, tr, "corridor", name);
                alId = Resolve.Named(doc, tr, "alignment", Hz.Str(ctx.Args, "alignment")!);
                var al = (Alignment)tr.GetObject(alId, OpenMode.ForRead);
                prId = ObjectId.Null;
                foreach (ObjectId pid in al.GetProfileIds())
                    if (string.Equals(Catalog.NameOf(pid, tr), Hz.Str(ctx.Args, "profile"), StringComparison.OrdinalIgnoreCase)) prId = pid;
                if (prId.IsNull) throw new HzRefusal(ErrorCodes.NotFound, "Alignment '" + al.Name + "' has no profile '" + Hz.Str(ctx.Args, "profile") + "'. Nothing changed.");
                var pr = (Profile)tr.GetObject(prId, OpenMode.ForRead);
                if (pr.ProfileType == ProfileType.EG) plan["warning"] = "The baseline profile is a surface profile; a design (layout) profile is the usual baseline.";
                asId = Resolve.Named(doc, tr, "assembly", Hz.Str(ctx.Args, "assembly")!);
                s0 = Hz.Num(ctx.Args, "start_station") ?? Math.Max(al.StartingStation, pr.StartingStation);
                s1 = Hz.Num(ctx.Args, "end_station") ?? Math.Min(al.EndingStation, pr.EndingStation);
                if (s1 <= s0) throw new HzRefusal(ErrorCodes.InvalidInput, "end_station must be greater than start_station. Nothing changed.");
                if (s0 < pr.StartingStation - 1e-6 || s1 > pr.EndingStation + 1e-6)
                    throw new HzRefusal(ErrorCodes.InvalidInput, "Region " + s0 + "-" + s1 + " goes outside the profile " + pr.StartingStation + "-" + pr.EndingStation + ". Nothing changed.");
                plan["new_name"] = name; plan["alignment"] = al.Name; plan["profile"] = pr.Name; plan["assembly"] = Catalog.NameOf(asId, tr);
                plan["start_station"] = s0; plan["end_station"] = s1; plan["rebuild"] = rebuild;
                if (freq != null) plan["frequency"] = freq.DeepClone();
            },
            (doc, tr) =>
            {
                id = Civ(doc).CorridorCollection.Add(name, "Baseline (1)", alId, prId, "Region (1)", asId);
                var c = (Corridor)tr.GetObject(id, OpenMode.ForWrite);
                var r = c.Baselines[0].BaselineRegions[0];
                r.StartStation = s0;
                r.EndStation = s1;
                ApplyFrequency(r, freq);
                if (rebuild) c.Rebuild();
            },
            (doc, tr, v, after) =>
            {
                var c = (Corridor)tr.GetObject(id, OpenMode.ForRead);
                v.Text("name", name, c.Name);
                v.Check("baselines", 1, c.Baselines.Count, c.Baselines.Count == 1);
                if (c.Baselines.Count == 1)
                {
                    var b = c.Baselines[0];
                    v.Flag("baseline alignment", true, b.AlignmentId == alId);
                    v.Flag("baseline profile", true, b.ProfileId == prId);
                    v.Check("regions", 1, b.BaselineRegions.Count, b.BaselineRegions.Count == 1);
                    if (b.BaselineRegions.Count == 1)
                    {
                        var r = b.BaselineRegions[0];
                        v.Flag("region assembly", true, r.AssemblyId == asId);
                        v.Number("region start", s0, r.StartStation, 1e-6);
                        v.Number("region end", s1, r.EndStation, 1e-6);
                        CheckFrequency(v, r, freq);
                    }
                }
                if (rebuild) v.Flag("rebuilt (not out of date)", true, !c.IsOutOfDate);
                after["corridor"] = Describe(c, tr);
            });
    }

    private static CommandResult AddRegion(CommandContext ctx)
    {
        var freq = ctx.Args["frequency"] as JsonObject;
        var rebuild = Hz.Bool(ctx.Args, "rebuild") ?? true;
        var index = (int)(Hz.Num(ctx.Args, "baseline_index") ?? 0);
        var s0 = Hz.Num(ctx.Args, "start_station")!.Value;
        var s1 = Hz.Num(ctx.Args, "end_station")!.Value;
        ObjectId id = ObjectId.Null, asId = ObjectId.Null;
        int before = 0;
        var regionName = "";
        return WriteFlow.Run(ctx, "HZ_CORRIDOR",
            (doc, tr, plan) =>
            {
                id = Resolve.Target(doc, tr, "corridor", ctx.Args);
                var c = (Corridor)tr.GetObject(id, OpenMode.ForRead);
                Resolve.Editable(c, tr);
                if (index < 0 || index >= c.Baselines.Count) throw new HzRefusal(ErrorCodes.InvalidInput, "baseline_index " + index + " is out of range (0-" + (c.Baselines.Count - 1) + "). Nothing changed.");
                var b = c.Baselines[index];
                asId = Resolve.Named(doc, tr, "assembly", Hz.Str(ctx.Args, "assembly")!);
                foreach (BaselineRegion r in b.BaselineRegions)
                    if (s0 < r.EndStation - 1e-6 && s1 > r.StartStation + 1e-6)
                        throw new HzRefusal(ErrorCodes.InvalidInput, "Stations " + s0 + "-" + s1 + " overlap region '" + r.Name + "' (" + r.StartStation + "-" + r.EndStation + "). Nothing changed.");
                if (!b.ProfileId.IsNull)
                {
                    var pr = (Profile)tr.GetObject(b.ProfileId, OpenMode.ForRead);
                    if (s0 < pr.StartingStation - 1e-6 || s1 > pr.EndingStation + 1e-6)
                        throw new HzRefusal(ErrorCodes.InvalidInput, "Region goes outside the baseline profile " + pr.StartingStation + "-" + pr.EndingStation + ". Nothing changed.");
                }
                before = b.BaselineRegions.Count;
                regionName = "Region (" + (before + 1) + ")";
                plan["corridor"] = c.Name; plan["baseline"] = b.Name; plan["region_name"] = regionName; plan["assembly"] = Catalog.NameOf(asId, tr);
                plan["start_station"] = s0; plan["end_station"] = s1; plan["rebuild"] = rebuild;
            },
            (doc, tr) =>
            {
                var c = (Corridor)tr.GetObject(id, OpenMode.ForWrite);
                var r = c.Baselines[index].BaselineRegions.Add(regionName, asId, s0, s1);
                ApplyFrequency(r, freq);
                if (rebuild) c.Rebuild();
            },
            (doc, tr, v, after) =>
            {
                var c = (Corridor)tr.GetObject(id, OpenMode.ForRead);
                var b = c.Baselines[index];
                v.Check("regions", before + 1, b.BaselineRegions.Count, b.BaselineRegions.Count == before + 1);
                BaselineRegion? added = null;
                foreach (BaselineRegion r in b.BaselineRegions) if (r.Name == regionName) added = r;
                v.Flag("new region found", true, added != null);
                if (added != null)
                {
                    v.Number("region start", s0, added.StartStation, 1e-6);
                    v.Number("region end", s1, added.EndStation, 1e-6);
                    v.Flag("region assembly", true, added.AssemblyId == asId);
                    CheckFrequency(v, added, freq);
                }
                after["corridor"] = Describe(c, tr);
            });
    }

    private static CommandResult Rebuild(CommandContext ctx)
    {
        var id = ObjectId.Null;
        return WriteFlow.Run(ctx, "HZ_CORRIDOR",
            (doc, tr, plan) =>
            {
                id = Resolve.Target(doc, tr, "corridor", ctx.Args);
                var c = (Corridor)tr.GetObject(id, OpenMode.ForRead);
                Resolve.Editable(c, tr);
                plan["corridor"] = c.Name; plan["out_of_date_before"] = c.IsOutOfDate;
            },
            (doc, tr) => ((Corridor)tr.GetObject(id, OpenMode.ForWrite)).Rebuild(),
            (doc, tr, v, after) =>
            {
                var c = (Corridor)tr.GetObject(id, OpenMode.ForRead);
                v.Flag("not out of date", true, !c.IsOutOfDate);
                after["corridor"] = Describe(c, tr);
            });
    }

    private static CommandResult CreateSurface(CommandContext ctx)
    {
        var sname = Hz.Str(ctx.Args, "surface_name")!;
        var links = Resolve.Strings(ctx.Args["link_codes"]);
        var fls = Resolve.Strings(ctx.Args["feature_line_codes"]);
        var asBreak = Hz.Bool(ctx.Args, "breaklines") ?? true;
        var styleName = Hz.Str(ctx.Args, "style");
        ObjectId id = ObjectId.Null, styleId = ObjectId.Null;
        return WriteFlow.Run(ctx, "HZ_CORRIDOR",
            (doc, tr, plan) =>
            {
                id = Resolve.Target(doc, tr, "corridor", ctx.Args);
                var c = (Corridor)tr.GetObject(id, OpenMode.ForRead);
                Resolve.Editable(c, tr);
                Resolve.Unique(doc, tr, "surface", sname);
                foreach (CorridorSurface s in c.CorridorSurfaces)
                    if (string.Equals(s.Name, sname, StringComparison.OrdinalIgnoreCase)) throw new HzRefusal(ErrorCodes.InvalidInput, "Corridor surface '" + sname + "' exists. Nothing changed.");
                var known = new HashSet<string>(c.GetLinkCodes(), StringComparer.OrdinalIgnoreCase);
                var missing = links.Where(l => !known.Contains(l)).ToList();
                if (missing.Count > 0)
                    throw new HzRefusal(ErrorCodes.InvalidInput, "Corridor '" + c.Name + "' has no link codes " + string.Join(", ", missing) + " (rebuild it, or check its assemblies). Nothing changed.", new JsonObject { ["link_codes"] = Hz.Strings(known) });
                var knownPts = new HashSet<string>(c.GetPointCodes(), StringComparer.OrdinalIgnoreCase);
                var missingFl = fls.Where(f => !knownPts.Contains(f)).ToList();
                if (missingFl.Count > 0)
                    throw new HzRefusal(ErrorCodes.InvalidInput, "Corridor '" + c.Name + "' has no feature line (point) codes " + string.Join(", ", missingFl) + ". Nothing changed.", new JsonObject { ["point_codes"] = Hz.Strings(knownPts) });
                if (styleName != null) styleId = Resolve.Style(Civ(doc), tr, "surface", styleName);
                plan["corridor"] = c.Name; plan["surface_name"] = sname; plan["link_codes"] = Hz.Strings(links); plan["feature_line_codes"] = Hz.Strings(fls);
                plan["links_as_breaklines"] = asBreak; plan["style"] = styleName;
            },
            (doc, tr) =>
            {
                var c = (Corridor)tr.GetObject(id, OpenMode.ForWrite);
                var s = styleId.IsNull ? c.CorridorSurfaces.Add(sname) : c.CorridorSurfaces.Add(sname, styleId);
                foreach (var l in links) s.AddLinkCode(l, asBreak);
                foreach (var f in fls) s.AddFeatureLineCode(f);
                c.Rebuild();
            },
            (doc, tr, v, after) =>
            {
                var c = (Corridor)tr.GetObject(id, OpenMode.ForRead);
                CorridorSurface? s = null;
                foreach (CorridorSurface x in c.CorridorSurfaces) if (string.Equals(x.Name, sname, StringComparison.OrdinalIgnoreCase)) s = x;
                v.Flag("corridor surface exists", true, s != null);
                if (s == null) return;
                var lc = new HashSet<string>(s.LinkCodes(), StringComparer.OrdinalIgnoreCase);
                foreach (var l in links) v.Flag("link code " + l, true, lc.Contains(l));
                var fc = new HashSet<string>(s.FeatureLineCodes(), StringComparer.OrdinalIgnoreCase);
                foreach (var f in fls) v.Flag("feature line code " + f, true, fc.Contains(f));
                v.Flag("surface object created", true, !s.SurfaceId.IsNull && !s.SurfaceId.IsErased);
                if (!s.SurfaceId.IsNull)
                {
                    if (tr.GetObject(s.SurfaceId, OpenMode.ForRead) is not Autodesk.Civil.DatabaseServices.TinSurface surf) return;
                    var p = surf.GetGeneralProperties();
                    after["surface"] = new JsonObject { ["name"] = surf.Name, ["handle"] = surf.Handle.ToString(), ["points"] = p.NumberOfPoints, ["min_z"] = Hz.Finite(p.MinimumElevation, 4), ["max_z"] = Hz.Finite(p.MaximumElevation, 4) };
                    if (p.NumberOfPoints == 0) after["note"] = "The corridor surface has no points: an empty assembly produces no links. Import a real assembly (assembly_import) for a usable surface.";
                }
            });
    }
}
