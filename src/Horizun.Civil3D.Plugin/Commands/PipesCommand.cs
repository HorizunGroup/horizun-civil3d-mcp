// -----------------------------------------------------------------------------
// horizun_c3d_pipes - gravity pipe networks (block D).
// API: docs/api-probes/2025/AeccDbMgd.phase3-pipes.txt.
//
// Pipes are created between named structures with explicit inverts; the
// re-read checks inverts (centre Z - inner height / 2), slope, the 2D
// centre-to-centre length and both connections.
// -----------------------------------------------------------------------------
using System.Text.Json.Nodes;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.DatabaseServices.Styles;
using Horizun.Civil3D.Core;
using Horizun.Civil3D.Plugin.Civil;
using CivilSurface = Autodesk.Civil.DatabaseServices.Surface;

namespace Horizun.Civil3D.Plugin.Commands;

internal sealed class PipesCommand : ICommand
{
    public string Name => "pipes";

    public CommandResult Execute(CommandContext ctx)
    {
        if (ToolRules.Validate(ctx.Tool.Name, ctx.Args) is { } why) throw new HzRefusal(ErrorCodes.InvalidInput, why + " Nothing ran.");
        if (ctx.Action?.StartsWith("pressure_", StringComparison.Ordinal) == true) return PressureNetworks.Execute(ctx);
        _units = ctx.Document(forWrite: false).Database.Insunits;
        return ctx.Action switch
        {
            "catalog" => WriteFlow.Read(ctx, (doc, tr, data) => Catalog_(ctx, doc, tr, data)),
            "list" => WriteFlow.Read(ctx, (doc, tr, data) => List(ctx, doc, tr, data)),
            "create_network" => CreateNetwork(ctx),
            "add_structures" => AddStructures(ctx),
            "add_pipes" => AddPipes(ctx),
            _ => WriteFlow.Read(ctx, (doc, tr, data) => Validate(ctx, doc, tr, data)),
        };
    }

    private static CivilDocument Civ(Document doc) => CommandContext.Civil(doc);

    // ---- catalog ------------------------------------------------------------------

    private static string SizeName(PartSize s)
    {
        try { if (s.SizeDataRecord.GetDataFieldBy("PrtSN")?.Value is { } v) return v.ToString() ?? ""; } catch (System.Exception) { }
        return s.Handle.ToString();
    }

    /// <summary>Drawing units per catalog unit. Live finding: the metric catalog stores diameters in mm.</summary>
    private static double UnitFactor(string? units, UnitsValue drawing)
    {
        var u = (units ?? "").Trim().ToLowerInvariant();
        var metres = u switch { "mm" or "millimeter" or "millimeters" => 0.001, "cm" => 0.01, "m" or "meter" or "meters" => 1.0,
            "in" or "inch" or "inches" or "\"" => 0.0254, "ft" or "foot" or "feet" or "'" => 0.3048, _ => double.NaN };
        var drawingMetres = drawing switch { UnitsValue.Millimeters => 0.001, UnitsValue.Centimeters => 0.01, UnitsValue.Feet => 0.3048, UnitsValue.Inches => 0.0254, _ => 1.0 };
        if (double.IsNaN(metres))
            throw new HzRefusal(ErrorCodes.Unsupported, "Catalog unit '" + units + "' is not recognised; cannot convert part dimensions to drawing units. Nothing changed.");
        return metres / drawingMetres;
    }

    [ThreadStatic] private static UnitsValue _units;

    private static List<string> SizeTexts(PartSize s)
    {
        var l = new List<string>();
        try { foreach (var f in s.SizeDataRecord.GetAllDataFields()) if (f.Value is string t && t.Length > 0) l.Add(t); } catch (System.Exception) { }
        return l;
    }

    private static double? SizeDiameter(PartSize s, DomainType domain)
    {
        try
        {
            var f = s.SizeDataRecord.GetDataFieldBy(domain == DomainType.Pipe ? PartContextType.PipeInnerDiameter : PartContextType.StructInnerDiameter);
            return f?.Value is { } v ? Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture) * UnitFactor(f.Units, _units) : null;
        }
        catch (HzRefusal) { throw; }
        catch (System.Exception) { return null; }
    }

    private static IEnumerable<PartFamily> Families(PartsList pl, Transaction tr, DomainType domain) =>
        pl.GetPartFamilyIdsByDomain(domain).Cast<ObjectId>().Select(id => (PartFamily)tr.GetObject(id, OpenMode.ForRead));

    private static IEnumerable<PartSize> Sizes(PartFamily f, Transaction tr)
    {
        for (var i = 0; i < f.PartSizeCount; i++) yield return (PartSize)tr.GetObject(f[i], OpenMode.ForRead);
    }

    private static PartsList PartsListByName(Document doc, Transaction tr, string name) =>
        (PartsList)tr.GetObject(Resolve.FromCollection(Civ(doc).Styles.PartsListSet, tr, name, "parts list"), OpenMode.ForRead);

    private static void Catalog_(CommandContext ctx, Document doc, Transaction tr, JsonObject data)
    {
        var rows = new JsonArray();
        foreach (ObjectId id in Civ(doc).Styles.PartsListSet)
        {
            var pl = (PartsList)tr.GetObject(id, OpenMode.ForRead);
            if (Hz.Str(ctx.Args, "parts_list") is { } want && !pl.Name.Equals(want, StringComparison.OrdinalIgnoreCase)) continue;
            JsonArray Fam(DomainType d) => new(Families(pl, tr, d).Select(f => (JsonNode)new JsonObject
            {
                ["family"] = f.Description, ["part_type"] = f.PartType.ToString(), ["shape"] = f.SweptShape.ToString(),
                ["sizes"] = new JsonArray(Sizes(f, tr).Select(s => (JsonNode)new JsonObject { ["size"] = SizeName(s), ["inner_diameter"] = SizeDiameter(s, d) is { } dd ? Hz.Finite(dd, 6) : null }).ToArray()),
            }).ToArray());
            rows.Add(new JsonObject { ["parts_list"] = pl.Name, ["pipe_families"] = Fam(DomainType.Pipe), ["structure_families"] = Fam(DomainType.Structure) });
        }
        data["parts_lists"] = rows;
        data["note"] = "Use family and size exactly as listed; size may also be the inner diameter (closest match within 1 mm or 0.001 drawing units).";
    }

    /// <summary>Family by description and size by name or inner diameter, inside the network's parts list.</summary>
    private static (ObjectId Family, ObjectId Size, string SizeName) Part(PartsList pl, Transaction tr, DomainType domain, string family, string size)
    {
        var fams = Families(pl, tr, domain).ToList();
        var f = fams.FirstOrDefault(x => x.Description.Equals(family, StringComparison.OrdinalIgnoreCase))
                ?? throw new HzRefusal(ErrorCodes.NotFound, "Parts list '" + pl.Name + "' has no " + domain.ToString().ToLowerInvariant() + " family '" + family + "'. Nothing changed.",
                    new JsonObject { ["families"] = Hz.Strings(fams.Select(x => x.Description)) });
        var sizes = Sizes(f, tr).ToList();
        // Live finding: list shows Civil's size name ("100 mm Concrete Pipe") while the catalog key (PrtSN) can
        // differ; accept either, and any other text value of the size record.
        var s = sizes.FirstOrDefault(x => SizeName(x).Equals(size, StringComparison.OrdinalIgnoreCase)) ?? sizes.FirstOrDefault(x => SizeTexts(x).Contains(size, StringComparer.OrdinalIgnoreCase));
        if (s == null && double.TryParse(size, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d))
            s = sizes.Where(x => SizeDiameter(x, domain) is { } sd && Math.Abs(sd - d) <= Math.Max(1e-3, d * 1e-3)).FirstOrDefault();
        if (s == null) throw new HzRefusal(ErrorCodes.NotFound, "Family '" + f.Description + "' has no size '" + size + "'. Nothing changed.", new JsonObject { ["sizes"] = Hz.Strings(sizes.Select(SizeName)) });
        return (f.ObjectId, s.ObjectId, SizeName(s));
    }

    // ---- reads --------------------------------------------------------------------

    private static double Invert(Pipe p, bool start) => (start ? p.StartPoint.Z : p.EndPoint.Z) - p.InnerHeight / 2;

    private static JsonObject PipeJson(Pipe p, Transaction tr)
    {
        var o = new JsonObject
        {
            ["name"] = p.Name, ["handle"] = p.Handle.ToString(), ["family"] = p.PartFamilyName, ["size"] = p.PartSizeName,
            ["inner_diameter"] = Hz.Finite(p.InnerDiameterOrWidth, 6), ["start"] = Resolve.Json(p.StartPoint), ["end"] = Resolve.Json(p.EndPoint),
            ["start_invert"] = Hz.Finite(Invert(p, true), 6), ["end_invert"] = Hz.Finite(Invert(p, false), 6),
            ["slope_pct"] = Hz.Finite(p.Slope * 100, 6), ["length_2d"] = Hz.Finite(p.Length2D, 6), ["length_2d_center_to_center"] = Hz.Finite(p.Length2DCenterToCenter, 6),
            ["start_structure"] = p.StartStructureId.IsNull ? null : Catalog.NameOf(p.StartStructureId, tr),
            ["end_structure"] = p.EndStructureId.IsNull ? null : Catalog.NameOf(p.EndStructureId, tr),
        };
        if (!p.RefSurfaceId.IsNull)
            try { o["min_cover"] = Hz.Finite(p.MinimumCover, 6); o["max_cover"] = Hz.Finite(p.MaximumCover, 6); } catch (System.Exception) { }
        return o;
    }

    private static JsonObject StructJson(Structure s, Transaction tr) => new()
    {
        ["name"] = SafeText(() => s.Name), ["handle"] = s.Handle.ToString(), ["family"] = SafeText(() => s.PartFamilyName), ["size"] = SafeText(() => s.PartSizeName),
        ["position"] = SafePoint(() => s.Position), ["rim"] = Safe(() => s.RimElevation), ["sump"] = Safe(() => s.SumpElevation), ["sump_depth"] = Safe(() => s.SumpDepth),
        ["connected_pipes"] = SafeNames(s),
    };

    /// <summary>Live finding: some structure properties throw "Retrieve attribute failed" (e.g. with no pipes connected).</summary>
    private static JsonNode? Safe(Func<double> f) { try { return Hz.Finite(f(), 6); } catch (System.Exception) { return null; } }
    private static JsonNode? SafeText(Func<string> f) { try { return f(); } catch (System.Exception) { return null; } }
    private static JsonNode? SafePoint(Func<Point3d> f) { try { return Resolve.Json(f()); } catch (System.Exception) { return null; } }
    private static JsonNode SafeNames(Structure s) { try { return Hz.Strings(s.GetConnectedPipeNames() ?? Array.Empty<string>()); } catch (System.Exception) { return new JsonArray(); } }

    private static Network Net(Document doc, Transaction tr, string name) => (Network)tr.GetObject(Resolve.Named(doc, tr, "pipe_network", name), OpenMode.ForRead);

    private static void List(CommandContext ctx, Document doc, Transaction tr, JsonObject data)
    {
        var limit = (int)(Hz.Num(ctx.Args, "limit") ?? 1000);
        if (Hz.Str(ctx.Args, "network") is not { } name)
        {
            data["networks"] = new JsonArray(Civ(doc).GetPipeNetworkIds().Cast<ObjectId>().Select(id =>
            {
                var n = (Network)tr.GetObject(id, OpenMode.ForRead);
                return (JsonNode)new JsonObject
                {
                    ["name"] = n.Name, ["handle"] = n.Handle.ToString(), ["pipes"] = n.GetPipeIds().Count, ["structures"] = n.GetStructureIds().Count,
                    ["parts_list"] = n.PartsListId.IsNull ? null : ((PartsList)tr.GetObject(n.PartsListId, OpenMode.ForRead)).Name, ["reference_surface"] = n.ReferenceSurfaceName,
                };
            }).ToArray());
            return;
        }
        var net = Net(doc, tr, name);
        data["network"] = net.Name;
        data["reference_surface"] = net.ReferenceSurfaceName;
        data["pipes"] = new JsonArray(net.GetPipeIds().Cast<ObjectId>().Take(limit).Select(id => (JsonNode)PipeJson((Pipe)tr.GetObject(id, OpenMode.ForRead), tr)).ToArray());
        data["structures"] = new JsonArray(net.GetStructureIds().Cast<ObjectId>().Take(limit).Select(id => (JsonNode)StructJson((Structure)tr.GetObject(id, OpenMode.ForRead), tr)).ToArray());
        data["slope_note"] = "slope_pct as Civil 3D reports Pipe.Slope (x100).";
    }

    // ---- writes -------------------------------------------------------------------

    private static CommandResult CreateNetwork(CommandContext ctx)
    {
        var name = Hz.Str(ctx.Args, "new_name")!;
        ObjectId plId = ObjectId.Null, sfId = ObjectId.Null, layerId = ObjectId.Null, id = ObjectId.Null;
        return WriteFlow.Run(ctx, "HZ_PIPES",
            (doc, tr, plan) =>
            {
                Resolve.Unique(doc, tr, "pipe_network", name);
                plId = PartsListByName(doc, tr, Hz.Str(ctx.Args, "parts_list")!).ObjectId;
                if (Hz.Str(ctx.Args, "surface") is { } s) sfId = Resolve.Named(doc, tr, "surface", s);
                if (Hz.Str(ctx.Args, "layer") is { } l) layerId = Resolve.Layer(doc.Database, tr, l);
                plan["new_name"] = name; plan["parts_list"] = Hz.Str(ctx.Args, "parts_list"); plan["surface"] = Hz.Str(ctx.Args, "surface");
            },
            (doc, tr) =>
            {
                var n = name;
                id = Network.Create(Civ(doc), ref n);
                var net = (Network)tr.GetObject(id, OpenMode.ForWrite);
                if (n != name) net.Name = name;
                net.PartsListId = plId;
                if (!sfId.IsNull) net.ReferenceSurfaceId = sfId;
                if (!layerId.IsNull) net.LayerId = layerId;
            },
            (doc, tr, v, after) =>
            {
                var net = (Network)tr.GetObject(id, OpenMode.ForRead);
                v.Text("name", name, net.Name);
                v.Flag("parts list", true, net.PartsListId == plId);
                if (!sfId.IsNull) v.Flag("reference surface", true, net.ReferenceSurfaceId == sfId);
                after["network"] = new JsonObject { ["name"] = net.Name, ["handle"] = net.Handle.ToString() };
            });
    }

    private sealed record PlannedStructure(string? Name, Point3d Pos, ObjectId Family, ObjectId Size, string SizeName, double Rim, double? Sump, double Rotation);

    private static CommandResult AddStructures(CommandContext ctx)
    {
        var items = ((JsonArray)ctx.Args["structures"]!).Select(n => (JsonObject)n!).ToList();
        var planned = new List<PlannedStructure>();
        var created = new List<ObjectId>();
        var netId = ObjectId.Null;
        return WriteFlow.Run(ctx, "HZ_PIPES",
            (doc, tr, plan) =>
            {
                var net = Net(doc, tr, Hz.Str(ctx.Args, "network")!);
                Resolve.Editable(net, tr);
                netId = net.ObjectId;
                if (net.PartsListId.IsNull) throw new HzRefusal(ErrorCodes.InvalidInput, "Network '" + net.Name + "' has no parts list. Nothing changed.");
                var pl = (PartsList)tr.GetObject(net.PartsListId, OpenMode.ForRead);
                var taken = net.GetStructureIds().Cast<ObjectId>().Select(i => ((Structure)tr.GetObject(i, OpenMode.ForRead)).Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var surf = net.ReferenceSurfaceId.IsNull ? null : (CivilSurface)tr.GetObject(net.ReferenceSurfaceId, OpenMode.ForRead);
                var rows = new JsonArray();
                foreach (var it in items)
                {
                    var p = Resolve.P(it["position"]);
                    if (Hz.Str(it, "name") is { } nm && taken.Contains(nm)) throw new HzRefusal(ErrorCodes.InvalidInput, "Network '" + net.Name + "' already has a structure '" + nm + "'. Nothing changed.");
                    var (fam, size, sizeName) = Part(pl, tr, DomainType.Structure, Hz.Str(it, "family")!, Hz.Str(it, "size")!);
                    double rim;
                    if (Hz.Num(it, "rim") is { } r) rim = r;
                    else if (surf != null)
                    {
                        try { rim = surf.FindElevationAtXY(p.X, p.Y); }
                        catch (System.Exception) { throw new HzRefusal(ErrorCodes.InvalidInput, "Structure at (" + p.X + ", " + p.Y + ") is outside the reference surface; give rim. Nothing changed."); }
                    }
                    else throw new HzRefusal(ErrorCodes.InvalidInput, "Network '" + net.Name + "' has no reference surface; give rim for every structure. Nothing changed.");
                    planned.Add(new PlannedStructure(Hz.Str(it, "name"), new Point3d(p.X, p.Y, rim), fam, size, sizeName, rim, Hz.Num(it, "sump_depth"), Cad.Rad(Hz.Num(it, "rotation") ?? 0)));
                    rows.Add(new JsonObject { ["name"] = Hz.Str(it, "name"), ["position"] = Resolve.Json(p, false), ["size"] = sizeName, ["rim"] = Hz.Finite(rim, 4) });
                }
                plan["network"] = net.Name; plan["structures"] = rows;
            },
            (doc, tr) =>
            {
                var net = (Network)tr.GetObject(netId, OpenMode.ForWrite);
                foreach (var s in planned)
                {
                    var id = ObjectId.Null;
                    net.AddStructure(s.Family, s.Size, s.Pos, s.Rotation, ref id, false);
                    var st = (Structure)tr.GetObject(id, OpenMode.ForWrite);
                    if (s.Name != null) st.Name = s.Name;
                    st.RimElevation = s.Rim;
                    // Live finding: Civil 3D measures the sump depth below the LOWEST CONNECTED PIPE invert; with no
                    // pipes the sump stays at the rim. The depth is stored now and takes effect when pipes connect.
                    if (s.Sump is { } sd) try { st.SumpDepth = sd; } catch (System.Exception e) { Log.Warn("sump depth not settable: " + e.Message); }
                    created.Add(id);
                }
            },
            (doc, tr, v, after) =>
            {
                // Live finding: on a structure without pipes some properties throw "Retrieve attribute failed";
                // every read is guarded, and an unreadable value is reported as unverified, never invented.
                static T? Try<T>(Func<T> f) where T : struct { try { return f(); } catch (System.Exception) { return null; } }
                var rows = new JsonArray();
                var unverified = new JsonArray();
                for (var i = 0; i < created.Count; i++)
                {
                    var st = (Structure)tr.GetObject(created[i], OpenMode.ForRead);
                    var p = planned[i];
                    var label = p.Name ?? created[i].Handle.ToString();
                    var pos = Try(() => st.Position);
                    if (pos is { } q) v.Check(label + " position", Resolve.Json(p.Pos, false), Resolve.Json(q, false), Math.Abs(q.X - p.Pos.X) <= 1e-6 && Math.Abs(q.Y - p.Pos.Y) <= 1e-6);
                    else unverified.Add(JsonValue.Create(label + " position"));
                    if (Try(() => st.RimElevation) is { } rim) v.Number(label + " rim", p.Rim, rim, 1e-6);
                    else unverified.Add(JsonValue.Create(label + " rim"));
                    if (p.Sump is { } sd) unverified.Add(JsonValue.Create(label + " sump depth " + sd + ": Civil 3D applies it below the lowest connected pipe invert, so it is checked by add_pipes"));
                    if (p.Name != null) v.Text(label + " name", p.Name, SafeText(() => st.Name)?.ToString());
                    rows.Add(StructJson(st, tr));
                }
                after["structures"] = rows;
                if (unverified.Count > 0) after["unverified_unreadable"] = unverified;
            });
    }

    private sealed record PlannedPipe(string? Name, ObjectId From, ObjectId To, Point3d A, Point3d B, ObjectId Family, ObjectId Size, double StartInv, double EndInv, double Inner);

    private static CommandResult AddPipes(CommandContext ctx)
    {
        var items = ((JsonArray)ctx.Args["pipes"]!).Select(n => (JsonObject)n!).ToList();
        var planned = new List<PlannedPipe>();
        var created = new List<ObjectId>();
        var netId = ObjectId.Null;
        return WriteFlow.Run(ctx, "HZ_PIPES",
            (doc, tr, plan) =>
            {
                var net = Net(doc, tr, Hz.Str(ctx.Args, "network")!);
                Resolve.Editable(net, tr);
                netId = net.ObjectId;
                if (net.PartsListId.IsNull) throw new HzRefusal(ErrorCodes.InvalidInput, "Network '" + net.Name + "' has no parts list. Nothing changed.");
                var pl = (PartsList)tr.GetObject(net.PartsListId, OpenMode.ForRead);
                var structs = net.GetStructureIds().Cast<ObjectId>().Select(i => (Structure)tr.GetObject(i, OpenMode.ForRead)).ToList();
                Structure S(string n) => structs.FirstOrDefault(s => s.Name.Equals(n, StringComparison.OrdinalIgnoreCase))
                    ?? throw new HzRefusal(ErrorCodes.NotFound, "Network '" + net.Name + "' has no structure '" + n + "'. Nothing changed.", new JsonObject { ["structures"] = Hz.Strings(structs.Select(s => s.Name)) });
                var rows = new JsonArray();
                foreach (var it in items)
                {
                    var a = S(Hz.Str(it, "from")!); var b = S(Hz.Str(it, "to")!);
                    var (fam, size, sizeName) = Part(pl, tr, DomainType.Pipe, Hz.Str(it, "family")!, Hz.Str(it, "size")!);
                    var inner = SizeDiameter((PartSize)tr.GetObject(size, OpenMode.ForRead), DomainType.Pipe)
                                ?? throw new HzRefusal(ErrorCodes.InvalidInput, "Size '" + sizeName + "' has no inner diameter in the catalog. Nothing changed.");
                    var l2d = new Point2d(a.Position.X, a.Position.Y).GetDistanceTo(new Point2d(b.Position.X, b.Position.Y));
                    if (l2d < 1e-6) throw new HzRefusal(ErrorCodes.InvalidInput, "Structures '" + a.Name + "' and '" + b.Name + "' are at the same XY. Nothing changed.");
                    var si = Hz.Num(it, "start_invert")!.Value;
                    var ei = Hz.Num(it, "end_invert") ?? si - Hz.Num(it, "slope_pct")!.Value / 100 * l2d;
                    foreach (var (st, inv, end) in new[] { (a, si, "start"), (b, ei, "end") })
                    {
                        if (inv + inner > st.RimElevation + 1e-9) throw new HzRefusal(ErrorCodes.InvalidInput, "Pipe " + end + " crown " + Math.Round(inv + inner, 4) + " is above the rim of '" + st.Name + "' (" + Math.Round(st.RimElevation, 4) + "). Nothing changed.");
                        // The sump of a structure without pipes is not reliable (live finding), so it is not a refusal reason.
                    }
                    planned.Add(new PlannedPipe(Hz.Str(it, "name"), a.ObjectId, b.ObjectId,
                        new Point3d(a.Position.X, a.Position.Y, si + inner / 2), new Point3d(b.Position.X, b.Position.Y, ei + inner / 2), fam, size, si, ei, inner));
                    rows.Add(new JsonObject
                    {
                        ["from"] = a.Name, ["to"] = b.Name, ["size"] = sizeName, ["start_invert"] = Hz.Finite(si, 4), ["end_invert"] = Hz.Finite(ei, 4),
                        ["length_2d"] = Hz.Finite(l2d, 4), ["slope_pct"] = Hz.Finite((si - ei) / l2d * 100, 4),
                    });
                }
                plan["network"] = net.Name; plan["pipes"] = rows;
            },
            (doc, tr) =>
            {
                var net = (Network)tr.GetObject(netId, OpenMode.ForWrite);
                foreach (var p in planned)
                {
                    var id = ObjectId.Null;
                    net.AddLinePipe(p.Family, p.Size, new LineSegment3d(p.A, p.B), ref id, false);
                    var pipe = (Pipe)tr.GetObject(id, OpenMode.ForWrite);
                    if (p.Name != null) pipe.Name = p.Name;
                    pipe.ConnectToStructure(ConnectorPositionType.Start, p.From, true);
                    pipe.ConnectToStructure(ConnectorPositionType.End, p.To, true);
                    created.Add(id);
                }
                // Live finding (v0.6.6): after connecting, Civil 3D left the sumps ABOVE the incoming inverts (98.73 for
                // an invert of 98.6) and ignored the stored sump depth. Set each touched structure's sump explicitly to
                // its lowest connected invert minus its sump depth (0 when unreadable); add_pipes verifies it.
                var lowest = new Dictionary<ObjectId, double>();
                foreach (var p in planned)
                {
                    lowest[p.From] = Math.Min(lowest.TryGetValue(p.From, out var a1) ? a1 : double.MaxValue, p.StartInv);
                    lowest[p.To] = Math.Min(lowest.TryGetValue(p.To, out var a2) ? a2 : double.MaxValue, p.EndInv);
                }
                foreach (var (sid, low) in lowest)
                {
                    var st = (Structure)tr.GetObject(sid, OpenMode.ForWrite);
                    // Live finding: a "Null Structure" (PartType StructNull) is a virtual connection point; its rim and
                    // sump follow the pipe, so no sump is set or checked on it.
                    if (st.PartType == PartType.StructNull) continue;
                    double depth = 0;
                    try { depth = Math.Max(0, st.SumpDepth); } catch (System.Exception) { }
                    double current;
                    try { current = st.SumpElevation; } catch (System.Exception) { current = double.MaxValue; }
                    if (current > low - depth + 1e-9)
                        try { st.SumpElevation = low - depth; } catch (System.Exception e) { Log.Warn("sump not settable on " + sid.Handle + ": " + e.Message); }
                }
            },
            (doc, tr, v, after) =>
            {
                var rows = new JsonArray();
                for (var i = 0; i < created.Count; i++)
                {
                    var pipe = (Pipe)tr.GetObject(created[i], OpenMode.ForRead);
                    var p = planned[i];
                    var w = pipe.Name;
                    v.Number(w + " start invert", p.StartInv, Invert(pipe, true), 1e-6);
                    v.Number(w + " end invert", p.EndInv, Invert(pipe, false), 1e-6);
                    var l2d = new Point2d(p.A.X, p.A.Y).GetDistanceTo(new Point2d(p.B.X, p.B.Y));
                    v.Number(w + " 2D length centre to centre", l2d, pipe.Length2DCenterToCenter, 1e-3);
                    v.Number(w + " |slope|", Math.Abs((p.StartInv - p.EndInv) / l2d), Math.Abs(pipe.Slope), 1e-6);
                    v.Flag(w + " start connected", true, pipe.StartStructureId == p.From);
                    v.Flag(w + " end connected", true, pipe.EndStructureId == p.To);
                    rows.Add(PipeJson(pipe, tr));
                }
                after["pipes"] = rows;
                // Sumps of the connected structures: never above the lowest pipe invert they receive.
                var lowest = new Dictionary<ObjectId, double>();
                foreach (var p in planned)
                {
                    lowest[p.From] = Math.Min(lowest.TryGetValue(p.From, out var a1) ? a1 : double.MaxValue, p.StartInv);
                    lowest[p.To] = Math.Min(lowest.TryGetValue(p.To, out var a2) ? a2 : double.MaxValue, p.EndInv);
                }
                var sumps = new JsonArray();
                foreach (var (sid, low) in lowest)
                {
                    var st = (Structure)tr.GetObject(sid, OpenMode.ForRead);
                    if (st.PartType == PartType.StructNull)
                    {
                        sumps.Add(new JsonObject { ["structure"] = SafeText(() => st.Name)?.ToString(), ["null_structure"] = true, ["note"] = "virtual connection point: rim and sump follow the pipe" });
                        continue;
                    }
                    double? sump = null;
                    try { sump = st.SumpElevation; } catch (System.Exception) { }
                    var nm = SafeText(() => st.Name)?.ToString() ?? sid.Handle.ToString();
                    if (sump is { } sv) v.Check(nm + " sump at or below the lowest connected invert", "<= " + Math.Round(low, 6), Hz.Finite(sv, 6), sv <= low + 1e-6);
                    sumps.Add(new JsonObject { ["structure"] = nm, ["sump"] = sump is { } s2 ? Hz.Finite(s2, 6) : null, ["lowest_invert"] = Hz.Finite(low, 6) });
                }
                after["structure_sumps"] = sumps;
            });
    }

    // ---- validate -------------------------------------------------------------------

    private static void Validate(CommandContext ctx, Document doc, Transaction tr, JsonObject data)
    {
        var net = Net(doc, tr, Hz.Str(ctx.Args, "network")!);
        var minC = Hz.Num(ctx.Args, "min_cover"); var maxC = Hz.Num(ctx.Args, "max_cover");
        var minS = Hz.Num(ctx.Args, "min_slope_pct"); var maxS = Hz.Num(ctx.Args, "max_slope_pct");
        var issues = new JsonArray();
        void Issue(string kind, string part, string detail) => issues.Add(new JsonObject { ["kind"] = kind, ["part"] = part, ["detail"] = detail });
        var pipes = net.GetPipeIds().Cast<ObjectId>().Select(i => (Pipe)tr.GetObject(i, OpenMode.ForRead)).ToList();
        foreach (var p in pipes)
        {
            if (p.StartStructureId.IsNull) Issue("disconnected", p.Name, "start end has no structure");
            if (p.EndStructureId.IsNull) Issue("disconnected", p.Name, "end has no structure");
            var s = Math.Abs(p.Slope * 100);
            if (minS is { } a && s < a - 1e-9) Issue("slope_low", p.Name, Math.Round(s, 4) + " % < " + a + " %");
            if (maxS is { } b && s > b + 1e-9) Issue("slope_high", p.Name, Math.Round(s, 4) + " % > " + b + " %");
            if ((minC != null || maxC != null) && p.RefSurfaceId.IsNull) { Issue("no_surface", p.Name, "cover cannot be checked: no reference surface"); continue; }
            try
            {
                if (minC is { } c && p.MinimumCover < c - 1e-9) Issue("cover_low", p.Name, "minimum cover " + Math.Round(p.MinimumCover, 4) + " < " + c);
                if (maxC is { } d && p.MaximumCover > d + 1e-9) Issue("cover_high", p.Name, "maximum cover " + Math.Round(p.MaximumCover, 4) + " > " + d);
            }
            catch (System.Exception e) { if (minC != null || maxC != null) Issue("cover_unreadable", p.Name, e.Message); }
        }
        foreach (var st in net.GetStructureIds().Cast<ObjectId>().Select(i => (Structure)tr.GetObject(i, OpenMode.ForRead)))
            if (st.ConnectedPipesCount == 0) Issue("structure_unconnected", st.Name, "no pipes connected");
        data["network"] = net.Name;
        data["pipes"] = pipes.Count;
        data["criteria"] = new JsonObject { ["min_cover"] = minC, ["max_cover"] = maxC, ["min_slope_pct"] = minS, ["max_slope_pct"] = maxS, ["source"] = "caller-supplied" };
        data["issues"] = issues;
        data["passed"] = issues.Count == 0;
    }
}
