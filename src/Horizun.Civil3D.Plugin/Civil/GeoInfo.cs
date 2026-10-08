// -----------------------------------------------------------------------------
// horizun_c3d_document action=geo - georeference and orientation of the active
// drawing, read-only: AutoCAD GeoLocation, the north / view / UCS system
// variables, the active viewport, Civil 3D's transformation settings and the
// grid convergence at the design point.
//
// Every member used is in docs/api-probes/2025/acdbmgd.geo.txt and
// AeccDbMgd.geo.txt. Nothing here opens an object for write; the transaction is
// always aborted (CommandContext.Read). Unreadable values are null with a reason.
// -----------------------------------------------------------------------------
using System.Text.Json.Nodes;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.Settings;
using Horizun.Civil3D.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace Horizun.Civil3D.Plugin.Civil;

internal static class GeoInfo
{
    // Step to the north used for the numeric convergence: 1e-4 degree of latitude (about 11 m).
    private const double NorthStepDeg = 1e-4;

    private const string AssumedRadians =
        "The API returns this angle as a bare double; *_deg assumes radians, as every AutoCAD / Civil 3D API angle.";

    public static JsonObject Read(Document doc, Transaction tr)
    {
        var db = doc.Database;
        var civil = CivilDocument.GetCivilDocument(db);
        var notes = new JsonArray();
        var o = new JsonObject { ["convention"] = GeoMath.Convention };

        var geo = GeoLocation(db, tr, out var g);
        o["geolocation"] = geo;
        o["grid_convergence"] = g == null
            ? new JsonObject { ["value"] = null, ["reason"] = "The drawing has no GeoLocation, so there is no geographic transform to compare against." }
            : Convergence(g);
        // GetSystemVariable and the Editor describe the ACTIVE window only; another open drawing is read from its database.
        var isActive = doc == AcApp.DocumentManager.MdiActiveDocument;
        o["system_variables"] = isActive ? SystemVariables(db) : DatabaseVariables(db, tr);
        o["active_view"] = isActive ? ActiveView(doc, tr)
            : new JsonObject { ["value"] = null, ["reason"] = "View, view twist and viewport belong to the drawing's window; this drawing is open but not active." };
        o["civil3d"] = Civil3D(civil);

        Observations(o, notes);
        o["observations"] = notes;
        return o;
    }

    // ---- AutoCAD GeoLocation ---------------------------------------------

    private static JsonObject GeoLocation(Database db, Transaction tr, out GeoLocationData? g)
    {
        g = null;
        var o = new JsonObject();
        ObjectId id;
        try { id = db.GeoDataObject; }
        catch (Exception e)
        {
            // AutoCAD throws when the drawing has no geographic location (that is the normal "absent" signal).
            o["present"] = false;
            o["reason"] = "Database.GeoDataObject: " + e.Message;
            return o;
        }
        if (id.IsNull || !id.IsValid || id.IsErased)
        {
            o["present"] = false;
            o["reason"] = "Database.GeoDataObject is null.";
            return o;
        }
        g = (GeoLocationData)tr.GetObject(id, OpenMode.ForRead);
        var geo = g;
        o["present"] = true;
        o["handle"] = id.Handle.ToString();
        var bad = new JsonObject();
        Safe.Set(o, bad, "coordinate_system", () => JsonValue.Create(Cap(geo.CoordinateSystem)));
        Safe.Set(o, bad, "type_of_coordinates", () => JsonValue.Create(geo.TypeOfCoordinates.ToString()));
        Safe.Set(o, bad, "design_point", () => P3(geo.DesignPoint));
        Safe.Set(o, bad, "reference_point", () => P3(geo.ReferencePoint));
        Safe.Set(o, bad, "north_direction", () => GeoMath.Angle(geo.NorthDirection));
        Safe.Set(o, bad, "north_direction_vector", () => GeoMath.Direction(geo.NorthDirectionVector.X, geo.NorthDirectionVector.Y));
        Safe.Num(o, bad, "scale_factor", () => geo.ScaleFactor);
        Safe.Set(o, bad, "scale_estimation_method", () => JsonValue.Create(geo.ScaleEstimationMethod.ToString()));
        Safe.Set(o, bad, "horizontal_units", () => JsonValue.Create(geo.HorizontalUnits.ToString()));
        Safe.Num(o, bad, "horizontal_units_scale", () => geo.HorizontalUnitsScale);
        Safe.Set(o, bad, "vertical_units", () => JsonValue.Create(geo.VerticalUnits.ToString()));
        Safe.Num(o, bad, "vertical_units_scale", () => geo.VerticalUnitsScale);
        Safe.Flag(o, bad, "sea_level_correction", () => geo.DoSeaLevelCorrection);
        Safe.Num(o, bad, "sea_level_elevation", () => geo.SeaLevelElevation);
        Safe.Num(o, bad, "coordinate_projection_radius", () => geo.CoordinateProjectionRadius);
        Safe.Set(o, bad, "up_direction", () => V3(geo.UpDirection));
        Safe.Int(o, bad, "mesh_points", () => geo.NumMeshPoints);
        Safe.Set(o, bad, "design_point_lon_lat", () =>
        {
            var ll = geo.TransformToLonLatAlt(geo.DesignPoint);
            return new JsonObject
            {
                ["longitude_deg"] = Hz.Finite(ll.X, 9), ["latitude_deg"] = Hz.Finite(ll.Y, 9), ["altitude"] = Hz.Finite(ll.Z, 6),
                ["source"] = "GeoLocationData.TransformToLonLatAlt(DesignPoint): X = longitude, Y = latitude.",
            };
        });
        if (bad.Count > 0) o["unreadable"] = bad;
        return o;
    }

    /// <summary>
    /// True north at the design point, found numerically through the drawing's own transform: design point to
    /// lon/lat, a small step north in latitude, back to drawing coordinates. Convergence is the turn from the
    /// GeoLocation north (grid north for a projected system) onto that true north.
    /// </summary>
    private static JsonObject Convergence(GeoLocationData g)
    {
        var o = new JsonObject
        {
            ["method"] = "Design point -> lon/lat -> +" + NorthStepDeg + " deg latitude -> drawing coordinates (GeoLocationData transforms).",
            ["definition"] = "Counter-clockwise turn from the GeoLocation north direction onto true north at the design point.",
        };
        try
        {
            var p = g.DesignPoint;
            var ll = g.TransformToLonLatAlt(p);
            if (!(Math.Abs(ll.Y) <= 90 && Math.Abs(ll.X) <= 360))
                return Fail(o, "TransformToLonLatAlt returned a non-geographic value (" + ll.X + ", " + ll.Y + ").");
            var back = g.TransformFromLonLatAlt(ll);
            var roundTrip = Math.Sqrt(Math.Pow(back.X - p.X, 2) + Math.Pow(back.Y - p.Y, 2));
            o["round_trip_error"] = Hz.Finite(roundTrip, 9);
            if (!double.IsFinite(roundTrip) || roundTrip > 1e-3)
                return Fail(o, "The lon/lat round trip of the design point does not return to it (error " + roundTrip + " drawing units).");
            if (Math.Abs(ll.Y) + NorthStepDeg >= 90) return Fail(o, "The design point is at a pole.");
            var q = g.TransformFromLonLatAlt(new Point3d(ll.X, ll.Y + NorthStepDeg, ll.Z));
            double tx = q.X - p.X, ty = q.Y - p.Y;
            var north = g.NorthDirectionVector;
            o["true_north_in_drawing"] = GeoMath.Direction(tx, ty);
            o["geolocation_north_in_drawing"] = GeoMath.Direction(north.X, north.Y);
            var turn = GeoMath.TurnDeg(north.X, north.Y, tx, ty);
            o["value_deg"] = turn is { } t ? Hz.Finite(t, 9) : null;
            if (turn == null) o["reason"] = "A direction was degenerate.";
            o["step_length_drawing_units"] = Hz.Finite(Math.Sqrt(tx * tx + ty * ty), 6);
        }
        catch (Exception e) { return Fail(o, e.GetType().Name + ": " + e.Message); }
        return o;
    }

    private static JsonObject Fail(JsonObject o, string reason)
    {
        o["value_deg"] = null;
        o["reason"] = reason;
        return o;
    }

    // ---- system variables ------------------------------------------------

    private static readonly string[] Vars =
        { "NORTHDIRECTION", "VIEWTWIST", "WORLDUCS", "UCSNAME", "UCSORG", "UCSXDIR", "UCSYDIR", "UCSFOLLOW", "UCSVP", "TILEMODE", "CVPORT", "ANGBASE", "ANGDIR", "GEOMARKERVISIBILITY" };

    /// <summary>The same keys, from the drawing's own Database (non-active drawing); window-only variables are null with a reason.</summary>
    private static JsonObject DatabaseVariables(Database db, Transaction tr)
    {
        var o = new JsonObject();
        var bad = new JsonObject();
        Safe.Num(o, bad, "NORTHDIRECTION", () => db.NorthDirection);
        Safe.Str(o, bad, "UCSNAME", () => db.Ucsname.IsNull ? "" : ((SymbolTableRecord)tr.GetObject(db.Ucsname, OpenMode.ForRead)).Name);
        Safe.Set(o, bad, "UCSORG", () => P3(db.Ucsorg));
        Safe.Set(o, bad, "UCSXDIR", () => P3(new Point3d(db.Ucsxdir.X, db.Ucsxdir.Y, db.Ucsxdir.Z)));
        Safe.Set(o, bad, "UCSYDIR", () => P3(new Point3d(db.Ucsydir.X, db.Ucsydir.Y, db.Ucsydir.Z)));
        Safe.Int(o, bad, "TILEMODE", () => db.TileMode ? 1 : 0);
        Safe.Set(o, bad, "northdirection_api", () => GeoMath.Angle(db.NorthDirection));
        foreach (var name in new[] { "VIEWTWIST", "WORLDUCS", "UCSFOLLOW", "UCSVP", "CVPORT", "ANGBASE", "ANGDIR", "GEOMARKERVISIBILITY" })
            o[name] = null;
        o["notes"] = "Non-active drawing: values come from its Database (Ucsname/Ucsorg/Ucsxdir/Ucsydir/NorthDirection/TileMode, " +
                     "the model-space UCS saved in the drawing). VIEWTWIST and the other window variables are only readable for " +
                     "the active drawing (GetSystemVariable reads the active window) and are null.";
        if (bad.Count > 0) o["unreadable"] = bad;
        return o;
    }

    private static JsonObject SystemVariables(Database db)
    {
        var o = new JsonObject();
        var bad = new JsonObject();
        foreach (var name in Vars) Safe.Set(o, bad, name, () => SysVar(name));
        Safe.Set(o, bad, "northdirection_api", () => GeoMath.Angle(db.NorthDirection));
        o["notes"] = "Raw values from GetSystemVariable; angle variables are radians (NORTHDIRECTION is also read as Database.NorthDirection). " +
                     "UCS* describe the current UCS of the active viewport.";
        if (bad.Count > 0) o["unreadable"] = bad;
        return o;
    }

    private static JsonNode? SysVar(string name)
    {
        var v = AcApp.GetSystemVariable(name);
        return v switch
        {
            null => null,
            double d => Hz.Finite(d),
            short s => JsonValue.Create((long)s),
            int i => JsonValue.Create((long)i),
            long l => JsonValue.Create(l),
            bool b => JsonValue.Create(b),
            string str => JsonValue.Create(str),
            Point3d p => P3(p),
            Point2d p2 => new JsonObject { ["x"] = Hz.Finite(p2.X, 12), ["y"] = Hz.Finite(p2.Y, 12) },
            _ => JsonValue.Create(v.GetType().Name + ": " + v),
        };
    }

    // ---- active view / viewport ------------------------------------------

    private static JsonObject ActiveView(Document doc, Transaction tr)
    {
        var o = new JsonObject();
        var bad = new JsonObject();
        var ed = doc.Editor;
        Safe.Flag(o, bad, "model_space_tiled", () => doc.Database.TileMode);
        Safe.Set(o, bad, "current_ucs", () => Cs(ed.CurrentUserCoordinateSystem.CoordinateSystem3d));
        Safe.Set(o, bad, "current_view", () =>
        {
            using var view = ed.GetCurrentView();
            return new JsonObject
            {
                ["view_twist"] = GeoMath.Angle(view.ViewTwist),
                ["view_direction"] = V3(view.ViewDirection),
                ["center"] = new JsonObject { ["x"] = Hz.Finite(view.CenterPoint.X, 6), ["y"] = Hz.Finite(view.CenterPoint.Y, 6) },
                ["height"] = Hz.Finite(view.Height, 6),
            };
        });
        Safe.Set(o, bad, "viewport", () =>
        {
            // Live evidence (2025): in tiled model space CurrentViewportObjectId is null; the active tiled viewport
            // (the *Active ViewportTableRecord) is Editor.ActiveViewportId.
            var id = ed.CurrentViewportObjectId;
            if (id.IsNull) id = ed.ActiveViewportId;
            if (id.IsNull) return new JsonObject { ["kind"] = null, ["reason"] = "Editor.CurrentViewportObjectId and ActiveViewportId are null." };
            var obj = tr.GetObject(id, OpenMode.ForRead);
            var v = new JsonObject { ["handle"] = id.Handle.ToString() };
            switch (obj)
            {
                case Viewport vp:
                    v["kind"] = "layout_viewport";
                    v["number"] = vp.Number;
                    v["twist_angle"] = GeoMath.Angle(vp.TwistAngle);
                    v["view_direction"] = V3(vp.ViewDirection);
                    v["ucs"] = Cs(vp.Ucs);
                    v["ucs_per_viewport"] = vp.UcsPerViewport;
                    v["custom_scale"] = Hz.Finite(vp.CustomScale, 9);
                    break;
                case ViewportTableRecord vtr:
                    v["kind"] = "model_tiled_viewport";
                    v["name"] = vtr.Name;
                    v["view_twist"] = GeoMath.Angle(vtr.ViewTwist);
                    v["view_direction"] = V3(vtr.ViewDirection);
                    v["ucs"] = Cs(vtr.Ucs);
                    v["ucs_saved_with_viewport"] = vtr.UcsSavedWithViewport;
                    break;
                default:
                    v["kind"] = obj.GetType().Name;
                    break;
            }
            return v;
        });
        if (bad.Count > 0) o["unreadable"] = bad;
        return o;
    }

    // ---- Civil 3D drawing settings ---------------------------------------

    private static JsonObject Civil3D(CivilDocument civil)
    {
        var o = new JsonObject();
        var bad = new JsonObject();
        var ds = civil.Settings.DrawingSettings;
        Safe.Set(o, bad, "coordinate_system", () =>
        {
            var cs = DrawingInfo.CoordinateSystem(civil);
            if (Hz.Str(cs, "code") is { } code)
            {
                try
                {
                    var def = SettingsUnitZone.GetCoordinateSystemByCode(code);
                    cs["description"] = def?.Description;
                    cs["projection"] = def?.Projection;
                    cs["datum"] = def?.Datum;
                    cs["unit"] = def?.Unit;
                    cs["category"] = def?.Category;
                }
                catch (Exception e) { cs["definition_unreadable"] = e.GetType().Name + ": " + e.Message; }
            }
            return cs;
        });
        Safe.Flag(o, bad, "apply_transform_settings", () => ds.ApplyTransformSettings);
        Safe.Set(o, bad, "transformation", () =>
        {
            var t = ds.TransformationSettings;
            var x = new JsonObject();
            var tb = new JsonObject();
            Safe.Set(x, tb, "rotation_to_grid_north", () => GeoMath.Angle(t.RotationToGridNorth));
            Safe.Set(x, tb, "rotation_to_grid_azimuth", () => GeoMath.Angle(t.RotationToGridAzimuth));
            Safe.Set(x, tb, "specify_rotation_type", () => JsonValue.Create(t.SpecifyRotationType.ToString()));
            Safe.Num(x, tb, "grid_scale_factor", () => t.GridScaleFactor);
            Safe.Set(x, tb, "grid_scale_factor_computation", () => JsonValue.Create(t.GridScaleFactorComputation.ToString()));
            Safe.Set(x, tb, "local_reference_point", () => P2(t.LocalReferencePoint));
            Safe.Set(x, tb, "grid_reference_point", () => P2(t.GridReferencePoint));
            Safe.Set(x, tb, "local_rotation_point", () => P2(t.LocalRotationPoint));
            Safe.Set(x, tb, "grid_rotation_point", () => P2(t.GridRotationPoint));
            Safe.Flag(x, tb, "apply_sea_level_scale_factor", () => t.ApplySeaLevelScaleFactor);
            Safe.Num(x, tb, "sea_level_scale_elevation", () => t.SeaLevelScaleElevation);
            Safe.Num(x, tb, "spheroid_radius", () => t.SpheroidRadius);
            x["angle_note"] = AssumedRadians;
            if (tb.Count > 0) x["unreadable"] = tb;
            return x;
        });
        // Live evidence (2025): with ApplyTransformSettings=false the TransformationSettings getter itself throws
        // InvalidOperationException, so the block is null with that reason - never invented defaults.
        if (o["transformation"] == null && o["apply_transform_settings"]?.GetValue<bool>() == false)
            o["transformation_note"] = "Civil 3D refuses to expose the transformation settings while they are not applied " +
                                       "(Drawing Settings > Transformation > Apply transformation settings is off).";
        if (bad.Count > 0) o["unreadable"] = bad;
        return o;
    }

    // ---- observations (facts that matter when north and the ViewCube disagree) ----

    private static void Observations(JsonObject o, JsonArray notes)
    {
        var sv = o["system_variables"] as JsonObject;
        var geo = o["geolocation"] as JsonObject;
        if (geo?["present"]?.GetValue<bool>() != true)
            notes.Add(JsonValue.Create("The drawing has no GeoLocation (Geographic Location)."));
        if (Hz.Num(sv, "VIEWTWIST") is { } twist && Math.Abs(twist) > 1e-9)
            notes.Add(JsonValue.Create("VIEWTWIST is " + Math.Round(GeoMath.Deg(twist), 6) + " deg: the current view is rotated in the screen relative to its UCS."));
        if (Hz.Int(sv, "WORLDUCS") == 0)
            notes.Add(JsonValue.Create("WORLDUCS = 0: the current UCS is not the WCS (UCSNAME '" + (Hz.Str(sv, "UCSNAME") ?? "") + "')."));
        if (sv?["UCSYDIR"] is JsonObject ydir && Hz.Num(ydir, "x") is { } yx && Hz.Num(ydir, "y") is { } yy &&
            GeoMath.FromYCcwDeg(yx, yy) is { } ucsRot && Math.Abs(ucsRot) > 1e-9)
        {
            notes.Add(JsonValue.Create("The current UCS is rotated " + Math.Round(ucsRot, 6) + " deg about Z from the WCS (counter-clockwise)."));
            if (Hz.Num(sv, "VIEWTWIST") is { } tw && Math.Abs(GeoMath.Wrap180(GeoMath.Deg(tw) + ucsRot)) < 1e-6)
                notes.Add(JsonValue.Create("VIEWTWIST is exactly minus the UCS rotation: the view is PLAN to that rotated UCS, so screen-up is " +
                                           "the UCS Y axis (" + Math.Round(ucsRot, 6) + " deg from drawing +Y), not north."));
        }
        var geoNorth = Hz.Num(geo?["north_direction"] as JsonObject, "deg");
        var varNorth = Hz.Num(sv?["northdirection_api"] as JsonObject, "deg");
        if (geoNorth is { } a && varNorth is { } b && Math.Abs(GeoMath.Wrap180(a - b)) > 1e-6)
            notes.Add(JsonValue.Create("GeoLocation north (" + Math.Round(a, 6) + " deg) and NORTHDIRECTION (" + Math.Round(b, 6) + " deg) differ."));
        if (geoNorth is { } gn && Math.Abs(gn) > 1e-9)
            notes.Add(JsonValue.Create("GeoLocation north is rotated " + Math.Round(gn, 6) + " deg from the drawing +Y axis."));
        var rot = Hz.Num((o["civil3d"]?["transformation"] as JsonObject)?["rotation_to_grid_north"] as JsonObject, "deg");
        if (rot is { } r && Math.Abs(r) > 1e-9)
            notes.Add(JsonValue.Create("Civil 3D rotation to grid north is " + Math.Round(r, 6) + " deg (assuming radians)."));
    }

    // ---- small converters -------------------------------------------------

    private static string? Cap(string? s) => s == null || s.Length <= 4000 ? s : s.Substring(0, 4000) + "... (" + s.Length + " characters, truncated)";

    private static JsonObject P3(Point3d p) => new() { ["x"] = Hz.Finite(p.X, 9), ["y"] = Hz.Finite(p.Y, 9), ["z"] = Hz.Finite(p.Z, 9) };

    private static JsonObject P2(Point2d p) => new() { ["x"] = Hz.Finite(p.X, 9), ["y"] = Hz.Finite(p.Y, 9) };

    private static JsonObject V3(Vector3d v) => new() { ["x"] = Hz.Finite(v.X, 12), ["y"] = Hz.Finite(v.Y, 12), ["z"] = Hz.Finite(v.Z, 12) };

    private static JsonObject Cs(CoordinateSystem3d cs) => new()
    {
        ["origin"] = P3(cs.Origin),
        ["x_axis"] = V3(cs.Xaxis),
        ["y_axis"] = V3(cs.Yaxis),
        // The UCS Y axis measured from the WCS +Y axis is the UCS rotation about Z (counter-clockwise).
        ["y_axis_direction"] = GeoMath.Direction(cs.Yaxis.X, cs.Yaxis.Y),
        ["is_world"] = cs.Origin.IsEqualTo(Point3d.Origin) && cs.Xaxis.IsEqualTo(Vector3d.XAxis) && cs.Yaxis.IsEqualTo(Vector3d.YAxis),
    };
}
