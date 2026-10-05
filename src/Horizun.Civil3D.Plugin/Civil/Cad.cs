// -----------------------------------------------------------------------------
// Horizun Civil 3D MCP - shared AutoCAD helpers for block C: colours,
// lineweights, linetypes, entity description, editability and properties.
// -----------------------------------------------------------------------------
using System.Text.Json.Nodes;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Horizun.Civil3D.Core;
using AcColor = Autodesk.AutoCAD.Colors.Color;
using AcTransparency = Autodesk.AutoCAD.Colors.Transparency;
using ColorMethod = Autodesk.AutoCAD.Colors.ColorMethod;
using DBObject = Autodesk.AutoCAD.DatabaseServices.DBObject;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;
using Region = Autodesk.AutoCAD.DatabaseServices.Region;

namespace Horizun.Civil3D.Plugin.Civil;

internal static class Cad
{
    // ---- colours / lineweights / transparency --------------------------------

    public static AcColor Color(JsonNode n)
    {
        if (n is JsonValue v && v.TryGetValue<string>(out var s))
        {
            if (s.Equals("bylayer", StringComparison.OrdinalIgnoreCase)) return AcColor.FromColorIndex(ColorMethod.ByLayer, 256);
            if (s.Equals("byblock", StringComparison.OrdinalIgnoreCase)) return AcColor.FromColorIndex(ColorMethod.ByBlock, 0);
            return AcColor.FromRgb(Convert.ToByte(s.Substring(1, 2), 16), Convert.ToByte(s.Substring(3, 2), 16), Convert.ToByte(s.Substring(5, 2), 16));
        }
        return AcColor.FromColorIndex(ColorMethod.ByAci, (short)Hz.AsDouble(n)!.Value);
    }

    public static JsonNode ColorJson(AcColor c) => c.ColorMethod switch
    {
        ColorMethod.ByLayer => JsonValue.Create("bylayer"),
        ColorMethod.ByBlock => JsonValue.Create("byblock"),
        ColorMethod.ByColor => JsonValue.Create("#" + c.Red.ToString("X2") + c.Green.ToString("X2") + c.Blue.ToString("X2")),
        _ => JsonValue.Create((int)c.ColorIndex),
    };

    /// <summary>Same colour as requested? (true colour compared by RGB, ACI by index).</summary>
    public static bool SameColor(AcColor want, AcColor got) =>
        want.ColorMethod == got.ColorMethod && (want.ColorMethod != ColorMethod.ByColor ? want.ColorIndex == got.ColorIndex
            : want.Red == got.Red && want.Green == got.Green && want.Blue == got.Blue);

    public static LineWeight Lineweight(JsonNode n)
    {
        if (n is JsonValue v && v.TryGetValue<string>(out var s))
            return s switch { "bylayer" => LineWeight.ByLayer, "byblock" => LineWeight.ByBlock, _ => LineWeight.ByLineWeightDefault };
        return (LineWeight)(int)Math.Round(Hz.AsDouble(n)!.Value * 100);
    }

    public static JsonNode LineweightJson(LineWeight w) => w switch
    {
        LineWeight.ByLayer => JsonValue.Create("bylayer"),
        LineWeight.ByBlock => JsonValue.Create("byblock"),
        LineWeight.ByLineWeightDefault => JsonValue.Create("default"),
        _ => JsonValue.Create((int)w / 100.0),
    };

    public static AcTransparency Transparency(double pct) => new((byte)Math.Round(255 * (1 - pct / 100.0)));

    public static int? TransparencyPct(AcTransparency t) =>
        t.IsByAlpha ? (int)Math.Round((1 - t.Alpha / 255.0) * 100) : null;

    // ---- symbol tables ---------------------------------------------------------

    /// <summary>Linetype id by name; loads it from acadiso.lin (then acad.lin) when missing. Null name = Continuous.</summary>
    public static ObjectId Linetype(Database db, Transaction tr, string name, bool allowLoad, List<string>? loaded = null)
    {
        var lt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
        if (lt.Has(name)) return lt[name];
        if (name.Equals("bylayer", StringComparison.OrdinalIgnoreCase)) return db.ByLayerLinetype;
        if (name.Equals("byblock", StringComparison.OrdinalIgnoreCase)) return db.ByBlockLinetype;
        if (!allowLoad) throw new HzRefusal(ErrorCodes.NotFound, "Linetype '" + name + "' is not loaded; it will be loaded from acadiso.lin on apply if it exists there.");
        foreach (var file in new[] { "acadiso.lin", "acad.lin" })
        {
            try { db.LoadLineTypeFile(name, file); } catch (Autodesk.AutoCAD.Runtime.Exception) { continue; }
            lt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
            if (lt.Has(name)) { loaded?.Add(name + " (" + file + ")"); return lt[name]; }
        }
        throw new HzRefusal(ErrorCodes.NotFound, "Linetype '" + name + "' is not in the drawing, acadiso.lin or acad.lin. Nothing changed.");
    }

    /// <summary>Plan-time check: loaded, or present in a standard .lin file (so apply can load it).</summary>
    public static string LinetypeAvailability(Database db, Transaction tr, string name)
    {
        var lt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
        if (lt.Has(name) || name.Equals("bylayer", StringComparison.OrdinalIgnoreCase) || name.Equals("byblock", StringComparison.OrdinalIgnoreCase)) return "loaded";
        foreach (var file in new[] { "acadiso.lin", "acad.lin" })
        {
            var path = HostApplicationServices.Current.FindFile(file, db, FindFileHint.Default);
            if (!string.IsNullOrEmpty(path) && File.Exists(path) &&
                File.ReadLines(path).Any(l => l.StartsWith("*" + name + ",", StringComparison.OrdinalIgnoreCase) || l.Equals("*" + name, StringComparison.OrdinalIgnoreCase)))
                return "will load from " + file;
        }
        throw new HzRefusal(ErrorCodes.NotFound, "Linetype '" + name + "' is not in the drawing, acadiso.lin or acad.lin. Nothing changed.");
    }

    public static ObjectId Symbol(Transaction tr, ObjectId tableId, string name, string what)
    {
        var t = (SymbolTable)tr.GetObject(tableId, OpenMode.ForRead);
        if (t.Has(name)) return t[name];
        var names = new List<string>();
        foreach (ObjectId id in t) names.Add(((SymbolTableRecord)tr.GetObject(id, OpenMode.ForRead)).Name);
        throw new HzRefusal(ErrorCodes.NotFound, "No " + what + " named '" + name + "'. Nothing changed.", new JsonObject { ["candidates"] = Hz.Strings(names.Take(200)) });
    }

    public static void NewSymbol(Transaction tr, ObjectId tableId, string name, string what)
    {
        if (((SymbolTable)tr.GetObject(tableId, OpenMode.ForRead)).Has(name))
            throw new HzRefusal(ErrorCodes.InvalidInput, "A " + what + " named '" + name + "' already exists. Existing objects are never overwritten. Nothing changed.");
        SymbolUtilityServices.ValidateSymbolName(name, false);
    }

    // ---- entities --------------------------------------------------------------

    public static string Dxf(DBObject o) => o.GetRXClass().DxfName;

    /// <summary>Entity by handle, opened for read; refuses non-entities, objects outside model/paper space blocks of this drawing.</summary>
    public static Entity Entity(Database db, Transaction tr, string handle)
    {
        var id = Catalog.FromHandle(db, handle);
        return tr.GetObject(id, OpenMode.ForRead) as Entity
               ?? throw new HzRefusal(ErrorCodes.InvalidInput, "Handle " + handle.ToUpperInvariant() + " is not a drawing entity. Nothing changed.");
    }

    /// <summary>Refuse locked-layer objects, xref/dependent objects and objects owned by something other than a layout block.</summary>
    public static void Editable(Entity e, Transaction tr)
    {
        var layer = (LayerTableRecord)tr.GetObject(e.LayerId, OpenMode.ForRead);
        if (layer.IsLocked) throw new HzRefusal(ErrorCodes.NotEditable, Dxf(e) + " " + e.Handle + " is on locked layer '" + layer.Name + "'. Nothing changed.");
        if (tr.GetObject(e.OwnerId, OpenMode.ForRead) is not BlockTableRecord owner)
            throw new HzRefusal(ErrorCodes.NotEditable, Dxf(e) + " " + e.Handle + " is not owned by a block (it is part of another object). Nothing changed.");
        if (owner.IsFromExternalReference || owner.IsDependent)
            throw new HzRefusal(ErrorCodes.NotEditable, Dxf(e) + " " + e.Handle + " belongs to an external reference. Nothing changed.");
        if (!owner.IsLayout)
            throw new HzRefusal(ErrorCodes.NotEditable, Dxf(e) + " " + e.Handle + " is inside block definition '" + owner.Name + "', not in a layout. Nothing changed.");
    }

    public static JsonObject Extents(Entity e)
    {
        try
        {
            var x = e.GeometricExtents;
            return new JsonObject { ["min"] = Resolve.Json(x.MinPoint), ["max"] = Resolve.Json(x.MaxPoint) };
        }
        catch (Autodesk.AutoCAD.Runtime.Exception) { return new JsonObject { ["unavailable"] = true }; }
    }

    public static double? Length(Entity e)
    {
        if (e is not Curve c) return null;
        try { return c.GetDistanceAtParameter(c.EndParam) - c.GetDistanceAtParameter(c.StartParam); }
        catch (Autodesk.AutoCAD.Runtime.Exception) { return null; }
    }

    public static double? Area(Entity e)
    {
        try
        {
            return e switch
            {
                Hatch h => h.Area,
                Region r => r.Area,
                Curve c when c.Closed => c.Area,
                _ => null,
            };
        }
        catch (Autodesk.AutoCAD.Runtime.Exception) { return null; }
    }

    public static JsonObject Describe(Entity e, Transaction tr, bool full)
    {
        var o = new JsonObject
        {
            ["handle"] = e.Handle.ToString(), ["type"] = Dxf(e), ["layer"] = e.Layer, ["color"] = ColorJson(e.Color),
        };
        if (Length(e) is { } len) o["length"] = Hz.Finite(len, 6);
        if (Area(e) is { } area) o["area"] = Hz.Finite(area, 6);
        if (e is Curve cc) o["closed"] = cc.Closed;
        switch (e)
        {
            case DBText t: o["text"] = t.TextString; o["position"] = Resolve.Json(t.Position); o["height"] = Hz.Finite(t.Height, 6); break;
            case MText m: o["text"] = m.Contents; o["position"] = Resolve.Json(m.Location); o["height"] = Hz.Finite(m.TextHeight, 6); break;
            case BlockReference br when e is not Table:
                o["block"] = BlockName(br, tr); o["position"] = Resolve.Json(br.Position);
                o["rotation_deg"] = Hz.Finite(br.Rotation * 180 / Math.PI, 9); o["scale"] = Hz.Finite(br.ScaleFactors.X, 9); break;
            case Circle c: o["center"] = Resolve.Json(c.Center); o["radius"] = Hz.Finite(c.Radius, 9); break;
            case Arc a: o["center"] = Resolve.Json(a.Center); o["radius"] = Hz.Finite(a.Radius, 9);
                o["start_angle_deg"] = Hz.Finite(a.StartAngle * 180 / Math.PI, 9); o["end_angle_deg"] = Hz.Finite(a.EndAngle * 180 / Math.PI, 9); break;
            case Line l: o["from"] = Resolve.Json(l.StartPoint); o["to"] = Resolve.Json(l.EndPoint); break;
            case DBPoint p: o["position"] = Resolve.Json(p.Position); break;
            case Dimension d: o["measurement"] = Hz.Finite(d.Measurement, 9); o["dimension_style"] = d.DimensionStyleName; o["text_override"] = d.DimensionText; break;
        }
        if (full)
        {
            o["linetype"] = e.Linetype;
            o["lineweight"] = LineweightJson(e.LineWeight);
            o["linetype_scale"] = Hz.Finite(e.LinetypeScale, 9);
            o["transparency_pct"] = TransparencyPct(e.Transparency);
            o["extents"] = Extents(e);
            o["owner"] = tr.GetObject(e.OwnerId, OpenMode.ForRead) is BlockTableRecord b ? b.Name : null;
            if (e is Polyline pl)
            {
                var pts = new JsonArray();
                for (var i = 0; i < pl.NumberOfVertices; i++)
                {
                    var p = pl.GetPoint2dAt(i);
                    pts.Add(new JsonObject { ["x"] = Hz.Finite(p.X, 6), ["y"] = Hz.Finite(p.Y, 6), ["bulge"] = Hz.Finite(pl.GetBulgeAt(i), 9) });
                }
                o["elevation"] = Hz.Finite(pl.Elevation, 6);
                o["vertices"] = pts;
            }
        }
        return o;
    }

    public static string BlockName(BlockReference br, Transaction tr)
    {
        var id = br.IsDynamicBlock ? br.DynamicBlockTableRecord : br.BlockTableRecord;
        return ((BlockTableRecord)tr.GetObject(id, OpenMode.ForRead)).Name;
    }

    public static BlockTableRecord ModelSpace(Database db, Transaction tr, OpenMode mode = OpenMode.ForRead) =>
        (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), mode);

    /// <summary>New entity with the drawing defaults applied FIRST (SetDatabaseDefaults would otherwise reset style/height set later).</summary>
    public static T New<T>(Database db, T e) where T : Entity { e.SetDatabaseDefaults(db); return e; }

    /// <summary>Append to model space. Call New() before setting properties.</summary>
    public static ObjectId Append(Database db, Transaction tr, Entity e, ObjectId? layer = null)
    {
        if (layer is { } l && !l.IsNull) e.LayerId = l;
        var ms = ModelSpace(db, tr, OpenMode.ForWrite);
        var id = ms.AppendEntity(e);
        tr.AddNewlyCreatedDBObject(e, true);
        return id;
    }

    /// <summary>Dictionary entries via the typed enumerator (LINQ Cast over a DBDictionary yields DictionaryEntry and throws).</summary>
    public static List<DBDictionaryEntry> Entries(DBDictionary d)
    {
        var l = new List<DBDictionaryEntry>();
        foreach (DBDictionaryEntry e in d) l.Add(e);
        return l;
    }

    /// <summary>ObjectIdCollection built by Add (the array constructor throws on an empty array).</summary>
    public static ObjectIdCollection Ids(IEnumerable<ObjectId> ids)
    {
        var c = new ObjectIdCollection();
        foreach (var id in ids) c.Add(id);
        return c;
    }

    public static double Rad(double deg) => deg * Math.PI / 180.0;

    public static Point3d Pt(JsonNode? n, double z = 0) => Resolve.P(n, z);
}
