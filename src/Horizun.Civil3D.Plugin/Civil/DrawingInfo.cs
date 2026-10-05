// -----------------------------------------------------------------------------
// Horizun Civil 3D MCP - what a drawing is: file, units, coordinate system.
//
// Units are explicit everywhere in the bridge: every numeric answer is in the
// drawing units reported here. A value that cannot be read is null with the
// reason in "unreadable", never a default.
// -----------------------------------------------------------------------------
using System.Text.Json.Nodes;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.Settings;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace Horizun.Civil3D.Plugin.Civil;

internal static class DrawingInfo
{
    public static string LinearSymbol(DrawingUnitType u) => u == DrawingUnitType.Meters ? "m" : "ft";

    public static string Symbol(LinearUnitType u) => u switch
    {
        LinearUnitType.Meter => "m",
        LinearUnitType.Foot => "ft",
        LinearUnitType.Millimeter => "mm",
        LinearUnitType.Centimeter => "cm",
        LinearUnitType.Decimeter => "dm",
        LinearUnitType.Kilometer => "km",
        LinearUnitType.Inch => "in",
        LinearUnitType.Yard => "yd",
        LinearUnitType.Mile => "mi",
        _ => u.ToString(),
    };

    public static string Symbol(AreaUnitType u) => u switch
    {
        AreaUnitType.SquareMeter => "m2",
        AreaUnitType.SquareFoot => "ft2",
        AreaUnitType.Hectare => "ha",
        AreaUnitType.Acre => "ac",
        AreaUnitType.SquareKilometer => "km2",
        AreaUnitType.SquareMile => "mi2",
        AreaUnitType.SquareYard => "yd2",
        _ => u.ToString(),
    };

    public static string Symbol(VolumeUnitType u) => u switch
    {
        VolumeUnitType.CubicMeter => "m3",
        VolumeUnitType.CubicFoot => "ft3",
        VolumeUnitType.CubicYard => "yd3",
        _ => u.ToString(),
    };

    /// <summary>Units block: base drawing units plus the ambient display units.</summary>
    public static JsonObject Units(CivilDocument civil, Database db)
    {
        var o = new JsonObject();
        var unreadable = new JsonObject();
        Safe.Str(o, unreadable, "linear", () => LinearSymbol(civil.Settings.DrawingSettings.UnitZoneSettings.DrawingUnits));
        Safe.Str(o, unreadable, "angular", () => civil.Settings.DrawingSettings.UnitZoneSettings.AngularUnits.ToString().ToLowerInvariant());
        Safe.Str(o, unreadable, "area_display", () => Symbol(civil.Settings.DrawingSettings.AmbientSettings.Area.Unit.Value));
        Safe.Str(o, unreadable, "volume_display", () => Symbol(civil.Settings.DrawingSettings.AmbientSettings.Volume.Unit.Value));
        Safe.Str(o, unreadable, "distance_display", () => Symbol(civil.Settings.DrawingSettings.AmbientSettings.Distance.Unit.Value));
        Safe.Str(o, unreadable, "elevation_display", () => Symbol(civil.Settings.DrawingSettings.AmbientSettings.Elevation.Unit.Value));
        Safe.Str(o, unreadable, "insunits", () => db.Insunits.ToString());
        Safe.Num(o, unreadable, "drawing_scale", () => civil.Settings.DrawingSettings.UnitZoneSettings.DrawingScale);
        var lin = o["linear"]?.GetValue<string>();
        if (lin != null)
        {
            o["volume"] = lin == "m" ? "m3" : "ft3";
            o["area"] = lin == "m" ? "m2" : "ft2";
            o["note"] = "Raw API values (lengths, elevations, areas, volumes) are in drawing units: " + lin + ", " +
                        o["area"] + ", " + o["volume"] + ". *_display are Civil 3D's label/report display settings.";
        }
        if (unreadable.Count > 0) o["unreadable"] = unreadable;
        return o;
    }

    public static JsonObject CoordinateSystem(CivilDocument civil)
    {
        var o = new JsonObject();
        try
        {
            var code = civil.Settings.DrawingSettings.UnitZoneSettings.CoordinateSystemCode;
            // Live evidence (Civil 3D 2025): a drawing with no coordinate system reports "." - not a code.
            var none = string.IsNullOrWhiteSpace(code) || code.Trim() == ".";
            o["code"] = none ? null : code;
            if (none) o["note"] = "No coordinate system is assigned to this drawing (Civil 3D reports '" + code + "').";
        }
        catch (Exception e)
        {
            o["code"] = null;
            o["unreadable"] = e.Message;
        }
        return o;
    }

    /// <summary>
    /// What DBMOD says changed. Live evidence (2026-10-01): a zoom/pan alone sets DBMOD=16, so
    /// "unsaved changes" must be qualified - a view-only change is not a model change.
    /// </summary>
    public static JsonObject DbmodBits(int dbmod)
    {
        var kinds = new JsonArray();
        if ((dbmod & 1) != 0) kinds.Add(JsonValue.Create("objects"));
        if ((dbmod & 4) != 0) kinds.Add(JsonValue.Create("drawing_variables"));
        if ((dbmod & 8) != 0) kinds.Add(JsonValue.Create("window"));
        if ((dbmod & 16) != 0) kinds.Add(JsonValue.Create("view"));
        if ((dbmod & 32) != 0) kinds.Add(JsonValue.Create("fields"));
        return new JsonObject
        {
            ["dbmod"] = dbmod,
            ["kinds"] = kinds,
            ["objects_changed"] = (dbmod & 1) != 0,
            ["view_only"] = dbmod != 0 && (dbmod & ~(8 | 16)) == 0,
        };
    }

    /// <summary>File-level facts. DBMOD/DWGTITLED are per-document system variables: only exact for the active drawing.</summary>
    public static JsonObject File(Document doc, bool isActive)
    {
        var o = new JsonObject
        {
            ["name"] = Path.GetFileName(doc.Name),
            ["path"] = doc.Name,
            ["active"] = isActive,
            ["read_only"] = doc.IsReadOnly,
        };
        if (isActive)
        {
            try
            {
                var dbmod = Convert.ToInt32(AcApp.GetSystemVariable("DBMOD"));
                o["has_unsaved_changes"] = dbmod != 0;
                if (dbmod != 0) o["unsaved_changes"] = DbmodBits(dbmod);
            }
            catch { o["has_unsaved_changes"] = null; }
            try { o["ever_saved"] = Convert.ToInt32(AcApp.GetSystemVariable("DWGTITLED")) != 0; }
            catch { o["ever_saved"] = null; }
        }
        else
        {
            o["has_unsaved_changes"] = null;
            o["has_unsaved_changes_note"] = "Only readable for the active drawing.";
        }
        return o;
    }

    public static JsonArray Xrefs(Database db, Transaction tr)
    {
        var a = new JsonArray();
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        foreach (ObjectId id in bt)
        {
            var btr = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
            if (!btr.IsFromExternalReference) continue;
            a.Add(new JsonObject
            {
                ["name"] = btr.Name,
                ["path"] = btr.PathName,
                ["status"] = btr.XrefStatus.ToString(),
                ["overlay"] = btr.IsFromOverlayReference,
            });
        }
        return a;
    }
}

/// <summary>Read one value; on failure store null and the reason. Never a fake default.</summary>
internal static class Safe
{
    public static void Set(JsonObject o, JsonObject unreadable, string key, Func<JsonNode?> read)
    {
        try { o[key] = read(); }
        catch (Exception e)
        {
            o[key] = null;
            unreadable[key] = e.GetType().Name + ": " + e.Message;
        }
    }

    public static void Str(JsonObject o, JsonObject unreadable, string key, Func<string?> read) =>
        Set(o, unreadable, key, () => (JsonNode?)JsonValue.Create(read()));

    public static void Num(JsonObject o, JsonObject unreadable, string key, Func<double> read) =>
        Set(o, unreadable, key, () => Horizun.Civil3D.Core.Hz.Finite(read()));

    public static void Flag(JsonObject o, JsonObject unreadable, string key, Func<bool> read) =>
        Set(o, unreadable, key, () => (JsonNode?)JsonValue.Create(read()));

    public static void Int(JsonObject o, JsonObject unreadable, string key, Func<long> read) =>
        Set(o, unreadable, key, () => (JsonNode?)JsonValue.Create(read()));
}
