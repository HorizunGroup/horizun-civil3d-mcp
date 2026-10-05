// -----------------------------------------------------------------------------
// Horizun Civil 3D MCP - the Civil 3D object catalogue: how each queryable type
// is enumerated, resolved and described.
//
// Enumeration uses the Civil 3D collections where they are complete, and a
// model-space scan by runtime class where they are not (feature lines owned by
// gradings do NOT appear in Site.GetFeatureLineIds()).
//
// Every description says whether the object can be edited from this drawing:
// a data-shortcut reference or an object on a locked layer is reported
// not_editable with the reason, so writes can refuse before any transaction.
// -----------------------------------------------------------------------------
using System.Text.Json.Nodes;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Horizun.Civil3D.Core;
using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;
using CivilEntity = Autodesk.Civil.DatabaseServices.Entity;
using DBObject = Autodesk.AutoCAD.DatabaseServices.DBObject;
using Surface = Autodesk.Civil.DatabaseServices.Surface;

namespace Horizun.Civil3D.Plugin.Civil;

internal static class Catalog
{
    public static readonly string[] Types =
    {
        "surface", "alignment", "profile", "profile_view", "corridor", "feature_line", "site", "parcel",
        "pipe_network", "pipe", "structure", "cogo_point", "point_group", "sample_line_group", "assembly",
    };

    // ---- enumeration ------------------------------------------------------

    public static List<ObjectId> Ids(string type, Database db, CivilDocument civil, Transaction tr) => type switch
    {
        "surface" => List(civil.GetSurfaceIds()),
        "alignment" => List(civil.GetAlignmentIds()),
        "profile" => FromAlignments(civil, tr, a => a.GetProfileIds()),
        "profile_view" => FromAlignments(civil, tr, a => a.GetProfileViewIds()),
        "sample_line_group" => FromAlignments(civil, tr, a => a.GetSampleLineGroupIds()),
        "corridor" => Enumerate(civil.CorridorCollection),
        "feature_line" => ModelSpace(db, tr, RXObject.GetClass(typeof(FeatureLine))),
        "site" => List(civil.GetSiteIds()),
        "parcel" => FromSites(civil, tr, s => s.GetParcelIds()),
        "pipe_network" => List(civil.GetPipeNetworkIds()),
        "pipe" => FromNetworks(civil, tr, n => n.GetPipeIds()),
        "structure" => FromNetworks(civil, tr, n => n.GetStructureIds()),
        "cogo_point" => List(civil.GetAllPointIds()),
        "point_group" => Enumerate(civil.PointGroups),
        "assembly" => Enumerate(civil.AssemblyCollection),
        _ => throw new HzRefusal(ErrorCodes.InvalidInput, "Unknown type '" + type + "'. Use one of: " + string.Join(", ", Types) + "."),
    };

    private static List<ObjectId> List(ObjectIdCollection ids)
    {
        var l = new List<ObjectId>(ids.Count);
        foreach (ObjectId id in ids) l.Add(id);
        return l;
    }

    private static List<ObjectId> Enumerate(System.Collections.IEnumerable items)
    {
        var l = new List<ObjectId>();
        foreach (var o in items) if (o is ObjectId id) l.Add(id);
        return l;
    }

    private static List<ObjectId> FromAlignments(CivilDocument civil, Transaction tr, Func<Alignment, ObjectIdCollection> f)
    {
        var l = new List<ObjectId>();
        foreach (ObjectId a in civil.GetAlignmentIds())
            if (tr.GetObject(a, OpenMode.ForRead) is Alignment al) l.AddRange(List(f(al)));
        return l;
    }

    private static List<ObjectId> FromSites(CivilDocument civil, Transaction tr, Func<Site, ObjectIdCollection> f)
    {
        var l = new List<ObjectId>();
        foreach (ObjectId s in civil.GetSiteIds())
            if (tr.GetObject(s, OpenMode.ForRead) is Site site) l.AddRange(List(f(site)));
        return l;
    }

    private static List<ObjectId> FromNetworks(CivilDocument civil, Transaction tr, Func<Network, ObjectIdCollection> f)
    {
        var l = new List<ObjectId>();
        foreach (ObjectId n in civil.GetPipeNetworkIds())
            if (tr.GetObject(n, OpenMode.ForRead) is Network net) l.AddRange(List(f(net)));
        return l;
    }

    public static List<ObjectId> ModelSpace(Database db, Transaction tr, RXClass cls)
    {
        var l = new List<ObjectId>();
        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
        foreach (ObjectId id in ms)
            if (!id.IsErased && id.ObjectClass.IsDerivedFrom(cls)) l.Add(id);
        return l;
    }

    // ---- identification ---------------------------------------------------

    public static string TypeOf(DBObject o) => o switch
    {
        Surface => "surface",
        Alignment => "alignment",
        Profile => "profile",
        ProfileView => "profile_view",
        Corridor => "corridor",
        FeatureLine => "feature_line",
        Site => "site",
        Parcel => "parcel",
        Network => "pipe_network",
        Pipe => "pipe",
        Structure => "structure",
        CogoPoint => "cogo_point",
        PointGroup => "point_group",
        SampleLineGroup => "sample_line_group",
        Assembly => "assembly",
        _ => "other",
    };

    public static string? NameOf(DBObject o) => o switch
    {
        CivilEntity ce => ce.Name,
        CogoPoint cp => cp.PointName,
        PointGroup pg => pg.Name,
        _ => null,
    };

    public static string? NameOf(ObjectId id, Transaction tr)
    {
        if (id.IsNull || id.IsErased) return null;
        try { return NameOf(tr.GetObject(id, OpenMode.ForRead)); } catch { return null; }
    }

    public static ObjectId FromHandle(Database db, string hex)
    {
        long value;
        try { value = Convert.ToInt64(hex.Trim(), 16); }
        catch { throw new HzRefusal(ErrorCodes.InvalidInput, "'" + hex + "' is not a hexadecimal handle."); }
        if (!db.TryGetObjectId(new Handle(value), out var id) || id.IsNull || id.IsErased)
            throw new HzRefusal(ErrorCodes.NotFound, "No object with handle " + hex.ToUpperInvariant() + " exists in this drawing.");
        return id;
    }

    /// <summary>Resolve one object of a type by exact name (case-insensitive). Missing/ambiguous -> refusal with candidates.</summary>
    public static ObjectId ByName(string type, string name, Database db, CivilDocument civil, Transaction tr)
    {
        var all = Ids(type, db, civil, tr);
        var hits = new List<(ObjectId Id, string Name)>();
        var names = new List<string>();
        foreach (var id in all)
        {
            var n = NameOf(id, tr);
            if (n == null) continue;
            names.Add(n);
            if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) hits.Add((id, n));
        }
        if (hits.Count == 1) return hits[0].Id;
        if (hits.Count > 1)
            throw new HzRefusal(ErrorCodes.Ambiguous,
                hits.Count + " " + type + " objects are named '" + name + "'. Use the handle. Nothing ran.",
                new JsonObject
                {
                    ["candidates"] = Hz.Arr(hits.Select(h => (JsonNode?)new JsonObject { ["handle"] = h.Id.Handle.ToString(), ["name"] = h.Name })),
                });
        var similar = names.Where(n => n.Contains(name, StringComparison.OrdinalIgnoreCase) || name.Contains(n, StringComparison.OrdinalIgnoreCase))
                           .Distinct().Take(20).ToList();
        if (similar.Count == 0) similar = names.Distinct().Take(20).ToList();
        throw new HzRefusal(ErrorCodes.NotFound,
            "No " + type + " named '" + name + "' (" + names.Count + " exist). Nothing ran.",
            new JsonObject { ["candidates"] = Hz.Strings(similar) });
    }

    // ---- description ------------------------------------------------------

    /// <summary>Caches layer lock state and names during one call.</summary>
    internal sealed class Lookup
    {
        private readonly Transaction _tr;
        private readonly Dictionary<ObjectId, (string Name, bool Locked, bool Frozen)> _layers = new();
        private readonly Dictionary<ObjectId, string?> _names = new();
        public Lookup(Transaction tr) => _tr = tr;

        public (string Name, bool Locked, bool Frozen) Layer(ObjectId id)
        {
            if (_layers.TryGetValue(id, out var v)) return v;
            var ltr = (LayerTableRecord)_tr.GetObject(id, OpenMode.ForRead);
            v = (ltr.Name, ltr.IsLocked, ltr.IsFrozen);
            _layers[id] = v;
            return v;
        }

        public string? Name(ObjectId id)
        {
            if (_names.TryGetValue(id, out var n)) return n;
            n = NameOf(id, _tr);
            _names[id] = n;
            return n;
        }
    }

    public static JsonObject Describe(DBObject obj, Transaction tr, Lookup lk, bool full)
    {
        var o = new JsonObject
        {
            ["handle"] = obj.Handle.ToString(),
            ["type"] = TypeOf(obj),
            ["class"] = obj.GetType().Name,
        };
        var u = new JsonObject();
        var notEditable = new JsonArray();

        switch (obj)
        {
            case CivilEntity ce:
                Safe.Str(o, u, "name", () => ce.Name);
                Safe.Str(o, u, "description", () => ce.Description);
                Safe.Str(o, u, "style", () => ce.StyleName);
                Safe.Flag(o, u, "is_reference", () => ce.IsReferenceObject);
                if (o["is_reference"]?.GetValue<bool>() == true)
                {
                    Safe.Flag(o, u, "reference_stale", () => ce.IsReferenceStale);
                    Safe.Flag(o, u, "reference_valid", () => ce.IsReferenceValid);
                    notEditable.Add(JsonValue.Create("data-shortcut reference: edit it in its source drawing (or promote the reference)"));
                }
                break;
            case CogoPoint cp:
                Safe.Int(o, u, "number", () => cp.PointNumber);
                Safe.Str(o, u, "name", () => cp.PointName);
                Safe.Str(o, u, "raw_description", () => cp.RawDescription);
                Safe.Num(o, u, "easting", () => cp.Easting);
                Safe.Num(o, u, "northing", () => cp.Northing);
                Safe.Num(o, u, "elevation", () => cp.Elevation);
                Safe.Str(o, u, "style", () => lk.Name(cp.StyleId) ?? StyleName(cp.StyleId, tr));
                break;
            case PointGroup pg:
                Safe.Str(o, u, "name", () => pg.Name);
                Safe.Int(o, u, "points_count", () => pg.PointsCount);
                Safe.Flag(o, u, "is_out_of_date", () => pg.IsOutOfDate);
                break;
        }

        if (obj is AcEntity e)
        {
            try
            {
                var layer = lk.Layer(e.LayerId);
                o["layer"] = layer.Name;
                if (layer.Locked) notEditable.Add(JsonValue.Create("on locked layer '" + layer.Name + "'"));
                if (layer.Frozen) o["layer_frozen"] = true;
            }
            catch (System.Exception ex) { o["layer"] = null; u["layer"] = ex.Message; }
        }

        try { Extras(obj, o, u, tr, lk, full); }
        catch (System.Exception ex) { u["extras"] = ex.GetType().Name + ": " + ex.Message; }

        o["editable"] = notEditable.Count == 0;
        if (notEditable.Count > 0) o["not_editable_because"] = notEditable;
        if (u.Count > 0) o["unreadable"] = u;
        return o;
    }

    private static string? StyleName(ObjectId id, Transaction tr)
    {
        if (id.IsNull) return null;
        return tr.GetObject(id, OpenMode.ForRead) is Autodesk.Civil.DatabaseServices.Styles.StyleBase sb ? sb.Name : null;
    }

    private static void Extras(DBObject obj, JsonObject o, JsonObject u, Transaction tr, Lookup lk, bool full)
    {
        switch (obj)
        {
            case Surface s:
                o["surface_kind"] = s switch
                {
                    TinVolumeSurface => "tin_volume",
                    TinSurface => "tin",
                    GridVolumeSurface => "grid_volume",
                    GridSurface => "grid",
                    _ => s.GetType().Name,
                };
                Safe.Flag(o, u, "is_out_of_date", () => s.IsOutOfDate);
                Safe.Flag(o, u, "auto_rebuild", () => s.AutoRebuild);
                if (full) o["statistics"] = SurfaceStatistics(s);
                break;
            case Alignment a:
                Safe.Num(o, u, "length", () => a.Length);
                Safe.Num(o, u, "start_station", () => a.StartingStation);
                Safe.Num(o, u, "end_station", () => a.EndingStation);
                Safe.Str(o, u, "site", () => a.SiteName);
                Safe.Int(o, u, "profiles", () => a.GetProfileIds().Count);
                if (full)
                {
                    Safe.Int(o, u, "profile_views", () => a.GetProfileViewIds().Count);
                    Safe.Int(o, u, "sample_line_groups", () => a.GetSampleLineGroupIds().Count);
                }
                break;
            case Profile p:
                Safe.Str(o, u, "alignment", () => lk.Name(p.AlignmentId));
                Safe.Str(o, u, "profile_type", () => p.ProfileType.ToString());
                Safe.Num(o, u, "start_station", () => p.StartingStation);
                Safe.Num(o, u, "end_station", () => p.EndingStation);
                Safe.Num(o, u, "length", () => p.Length);
                break;
            case ProfileView pv:
                Safe.Str(o, u, "alignment", () => pv.AlignmentName);
                break;
            case Corridor c:
                Safe.Flag(o, u, "is_out_of_date", () => c.IsOutOfDate);
                Safe.Int(o, u, "baselines", () => c.Baselines.Count);
                break;
            case FeatureLine fl:
                Safe.Str(o, u, "site", () => fl.SiteId.IsNull ? "(siteless)" : lk.Name(fl.SiteId));
                Safe.Int(o, u, "points", () => fl.PointsCount);
                Safe.Int(o, u, "pi_points", () => fl.PIPointsCount);
                Safe.Int(o, u, "elevation_points", () => fl.ElevationPointsCount);
                if (full)
                {
                    Safe.Num(o, u, "length_2d", () => fl.Length2D);
                    Safe.Num(o, u, "length_3d", () => fl.Length3D);
                    Safe.Num(o, u, "min_elevation", () => fl.MinElevation);
                    Safe.Num(o, u, "max_elevation", () => fl.MaxElevation);
                }
                break;
            case Site site:
                Safe.Int(o, u, "alignments", () => site.GetAlignmentIds().Count);
                Safe.Int(o, u, "feature_lines", () => site.GetFeatureLineIds().Count);
                Safe.Int(o, u, "parcels", () => site.GetParcelIds().Count);
                if (full) o["feature_lines_note"] = "Feature lines owned by gradings are not counted by Site.GetFeatureLineIds().";
                break;
            case Parcel pa:
                Safe.Int(o, u, "number", () => pa.Number);
                break;
            case Network n:
                Safe.Str(o, u, "parts_list", () => n.PartsListName);
                Safe.Int(o, u, "pipes", () => n.GetPipeIds().Count);
                Safe.Int(o, u, "structures", () => n.GetStructureIds().Count);
                Safe.Str(o, u, "reference_surface", () => n.ReferenceSurfaceName);
                Safe.Str(o, u, "reference_alignment", () => n.ReferenceAlignmentName);
                break;
            case SampleLineGroup g:
                Safe.Str(o, u, "parent_alignment", () => lk.Name(g.ParentAlignmentId));
                Safe.Int(o, u, "sample_lines", () => g.GetSampleLineIds().Count);
                break;
        }
    }

    /// <summary>
    /// Surface statistics in drawing units. mean_elevation is Civil 3D's vertex mean,
    /// NOT area-weighted - stated in the reply so nobody reports it as an average depth.
    /// </summary>
    public static JsonObject SurfaceStatistics(Surface s)
    {
        var o = new JsonObject();
        var u = new JsonObject();
        if (s is TinVolumeSurface || s is GridVolumeSurface)
        {
            o["note"] = "Volume surface: elevations below are cut/fill DEPTHS (comparison minus base). Cut/fill " +
                        "volumes need a write transaction in Civil 3D and are reported by the surface tool, not by query.";
        }
        try
        {
            var g = s.GetGeneralProperties();
            o["min_elevation"] = Hz.Finite(g.MinimumElevation);
            o["max_elevation"] = Hz.Finite(g.MaximumElevation);
            o["mean_elevation"] = Hz.Finite(g.MeanElevation);
            o["mean_elevation_note"] = "Mean of surface vertices (Civil 3D GeneralProperties), not area-weighted.";
            o["number_of_points"] = g.NumberOfPoints;
            o["extents"] = new JsonObject
            {
                ["min_x"] = Hz.Finite(g.MinimumCoordinateX),
                ["min_y"] = Hz.Finite(g.MinimumCoordinateY),
                ["max_x"] = Hz.Finite(g.MaximumCoordinateX),
                ["max_y"] = Hz.Finite(g.MaximumCoordinateY),
            };
        }
        catch (System.Exception e) { u["general"] = e.GetType().Name + ": " + e.Message; }

        if (s is TinSurface tin)
        {
            try
            {
                var t = tin.GetTinProperties();
                o["number_of_triangles"] = t.NumberOfTriangles;
                o["max_triangle_length"] = Hz.Finite(t.MaximumTriangleLength);
            }
            catch (System.Exception e) { u["tin"] = e.GetType().Name + ": " + e.Message; }
            try
            {
                var t = tin.GetTerrainProperties();
                o["area_2d"] = Hz.Finite(t.SurfaceArea2D);
                o["area_3d"] = Hz.Finite(t.SurfaceArea3D);
                o["mean_grade_or_slope"] = Hz.Finite(t.MeanGradeOrSlope);
            }
            catch (System.Exception e) { u["terrain"] = e.GetType().Name + ": " + e.Message; }
        }
        else if (s is not TinVolumeSurface)
        {
            u["area"] = "2D/3D area is read for TIN surfaces only in this version.";
        }
        if (u.Count > 0) o["unreadable"] = u;
        return o;
    }
}
