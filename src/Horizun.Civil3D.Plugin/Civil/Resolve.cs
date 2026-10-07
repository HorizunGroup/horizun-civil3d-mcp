// -----------------------------------------------------------------------------
// Horizun Civil 3D MCP - resolve names/handles to objects BEFORE any transaction
// writes, refusing missing/ambiguous names with candidates. Shared by all the
// action-based tools.
// -----------------------------------------------------------------------------
using System.Text.Json.Nodes;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices.Styles;
using Horizun.Civil3D.Core;
using Horizun.Civil3D.Plugin.Commands;
using DBObject = Autodesk.AutoCAD.DatabaseServices.DBObject;

namespace Horizun.Civil3D.Plugin.Civil;

internal static class Resolve
{
    /// <summary>Object of a catalogue type from a handle field or an exact-name field.</summary>
    public static ObjectId Target(Document doc, Transaction tr, string type, JsonObject a, string nameKey = "name", string handleKey = "handle")
    {
        if (Hz.Str(a, handleKey) is { } h && h.Length > 0)
        {
            var id = Catalog.FromHandle(doc.Database, h);
            var actual = Catalog.TypeOf(tr.GetObject(id, OpenMode.ForRead));
            if (actual != type) throw new HzRefusal(ErrorCodes.InvalidInput, "Handle " + h.ToUpperInvariant() + " is a " + actual + ", not a " + type + ". Nothing ran.");
            return id;
        }
        var name = Hz.Str(a, nameKey) ?? throw new HzRefusal(ErrorCodes.InvalidInput, "Give " + nameKey + " or " + handleKey + ". Nothing ran.");
        return Catalog.ByName(type, name, doc.Database, CommandContext.Civil(doc), tr);
    }

    public static ObjectId Named(Document doc, Transaction tr, string type, string name) =>
        Catalog.ByName(type, name, doc.Database, CommandContext.Civil(doc), tr);

    public static T Open<T>(Transaction tr, ObjectId id, OpenMode mode = OpenMode.ForRead) where T : DBObject =>
        tr.GetObject(id, mode) as T ?? throw new HzRefusal(ErrorCodes.InvalidInput, "Object " + id.Handle + " is not a " + typeof(T).Name + ". Nothing ran.");

    /// <summary>Refuse references, objects on locked layers and Civil objects reported not editable.</summary>
    public static void Editable(DBObject obj, Transaction tr)
    {
        var d = Catalog.Describe(obj, tr, new Catalog.Lookup(tr), false);
        if (Hz.Bool(d, "editable") != true)
            throw new HzRefusal(ErrorCodes.NotEditable, "'" + (Hz.Str(d, "name") ?? obj.Handle.ToString()) + "' is not editable here: " +
                d["not_editable_because"]?.ToJsonString(Hz.Compact) + ". Nothing changed.");
    }

    /// <summary>Name must not exist yet among objects of the type (names are never overwritten).</summary>
    public static void Unique(Document doc, Transaction tr, string type, string name)
    {
        foreach (var id in Catalog.Ids(type, doc.Database, CommandContext.Civil(doc), tr))
            if (string.Equals(Catalog.NameOf(id, tr), name, StringComparison.OrdinalIgnoreCase))
                throw new HzRefusal(ErrorCodes.InvalidInput, "A " + type + " named '" + name + "' already exists. Existing objects are never overwritten. Nothing changed.");
    }

    /// <summary>Existing, unlocked layer by name; null name = current layer (CLAYER).</summary>
    public static ObjectId Layer(Database db, Transaction tr, string? name, bool mustBeUnlocked = true)
    {
        if (string.IsNullOrWhiteSpace(name)) return db.Clayer;
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (!lt.Has(name)) throw new HzRefusal(ErrorCodes.NotFound, "Layer '" + name + "' does not exist. Nothing changed.");
        var id = lt[name];
        if (mustBeUnlocked && ((LayerTableRecord)tr.GetObject(id, OpenMode.ForRead)).IsLocked)
            throw new HzRefusal(ErrorCodes.NotEditable, "Layer '" + name + "' is locked. Nothing changed.");
        return id;
    }

    /// <summary>Style from a collection by exact name, or the first style when name is null.</summary>
    public static ObjectId FromCollection(StyleCollectionBase coll, Transaction tr, string? name, string what)
    {
        var names = new List<string>();
        foreach (ObjectId id in coll)
        {
            var s = (StyleBase)tr.GetObject(id, OpenMode.ForRead);
            if (name == null) return id;
            names.Add(s.Name);
            if (string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)) return id;
        }
        if (name == null) throw new HzRefusal(ErrorCodes.NotFound, "The drawing has no " + what + ". Create one from a Civil 3D template. Nothing changed.");
        throw new HzRefusal(ErrorCodes.NotFound, "No " + what + " named '" + name + "'. Nothing changed.", new JsonObject { ["candidates"] = Hz.Strings(names) });
    }

    public static ObjectId Style(CivilDocument civil, Transaction tr, string kind, string? name) =>
        FromCollection(Commands.StylesCommand.Collection(civil, kind), tr, name, kind.Replace('_', ' ') + " style");

    public static ObjectId AlignmentLabelSet(CivilDocument civil, Transaction tr, string? name) =>
        FromCollection(civil.Styles.LabelSetStyles.AlignmentLabelSetStyles, tr, name, "alignment label set");

    public static ObjectId ProfileLabelSet(CivilDocument civil, Transaction tr, string? name) =>
        FromCollection(civil.Styles.LabelSetStyles.ProfileLabelSetStyles, tr, name, "profile label set");

    public static ObjectId BandSet(CivilDocument civil, Transaction tr, string? name) =>
        FromCollection(civil.Styles.ProfileViewBandSetStyles, tr, name, "profile view band set");

    /// <summary>Existing site by name, or Null (siteless) when name is null.</summary>
    public static ObjectId Site(Document doc, Transaction tr, string? name) =>
        string.IsNullOrWhiteSpace(name) ? ObjectId.Null : Named(doc, tr, "site", name);

    public static Point3d P(JsonNode? n, double defaultZ = 0) =>
        n is JsonObject o ? new Point3d(Hz.Num(o, "x")!.Value, Hz.Num(o, "y")!.Value, Hz.Num(o, "z") ?? defaultZ)
            : throw new HzRefusal(ErrorCodes.InvalidInput, "A point {x, y} is required.");

    public static JsonObject Json(Point3d p, bool z = true)
    {
        var o = new JsonObject { ["x"] = Hz.Finite(p.X, 6), ["y"] = Hz.Finite(p.Y, 6) };
        if (z) o["z"] = Hz.Finite(p.Z, 6);
        return o;
    }

    public static JsonObject Json(Point2d p) => new() { ["x"] = Hz.Finite(p.X, 6), ["y"] = Hz.Finite(p.Y, 6) };

    public static List<double> Numbers(JsonNode? n) => (n as JsonArray)?.Select(x => Hz.AsDouble(x)!.Value).ToList() ?? new List<double>();

    public static List<string> Strings(JsonNode? n) => (n as JsonArray)?.Select(x => x!.GetValue<string>()).ToList() ?? new List<string>();
}
