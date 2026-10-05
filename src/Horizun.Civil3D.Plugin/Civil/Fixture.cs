// -----------------------------------------------------------------------------
// Horizun Civil 3D MCP - deterministic test drawing (HZ_BUILD_FIXTURE).
//
// Live verification must never depend on a client drawing. This builds, in a
// NEW untitled drawing with no surfaces (refused otherwise), objects whose
// answers are known ANALYTICALLY:
//
//   HZ_EG          TIN plane z = 100 + 0.02x + 0.01y over [0,100]^2 (grid 10 m)
//   HZ_FG          TIN plane z = 101 over the same square
//   HZ_EG_FG_VOL   TIN volume, base HZ_EG, comparison HZ_FG
//                    d = FG - EG = 1 - 0.02x - 0.01y
//                    fill = integral of max(d,0) = 833.333.. m3 (region 2x + y < 100)
//                    cut  = integral of max(-d,0) = 5833.333.. m3
//                    net magnitude = |integral of d| = 5000 m3
//   HZ_LOCKED      TIN on a LOCKED layer (refusal tests)
//   breakline      3D polyline (20,50,110)-(80,50,110)
//   outer limit    closed polyline square (10,10)-(90,90): area 6400 m2
//
// The expected values and handles are written to
// %USERPROFILE%\.horizun\civil3d\fixtures\fixture-expected.json for
// scripts/verify_live.py. Nothing is saved: the drawing stays untitled.
// -----------------------------------------------------------------------------
using System.Text.Json.Nodes;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Horizun.Civil3D.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace Horizun.Civil3D.Plugin.Civil;

internal static class Fixture
{
    public const string LayerSurfaces = "HZ-FIXTURE";
    public const string LayerData = "HZ-FIXTURE-DATA";
    public const string LayerLocked = "HZ-FIXTURE-LOCKED";

    public static double Eg(double x, double y) => 100 + 0.02 * x + 0.01 * y;

    /// <summary>Builds the fixture in the active drawing. Returns the expected-values document.</summary>
    public static JsonObject Build(Document doc)
    {
        if (Convert.ToInt32(AcApp.GetSystemVariable("DWGTITLED")) != 0)
            throw new HzRefusal(ErrorCodes.InvalidInput,
                "HZ_BUILD_FIXTURE solo trabaja en un dibujo NUEVO sin guardar (NEW con la plantilla de Civil 3D). " +
                "Este dibujo ya tiene nombre: no se toco nada.");
        var civil = CivilDocument.GetCivilDocument(doc.Database);
        if (civil.GetSurfaceIds().Count != 0)
            throw new HzRefusal(ErrorCodes.InvalidInput, "El dibujo ya tiene superficies. Usa un dibujo nuevo y vacio: no se toco nada.");
        if (civil.Styles.SurfaceStyles.Count == 0)
            throw new HzRefusal(ErrorCodes.NotFound, "El dibujo no tiene estilos de superficie: crealo desde una plantilla de Civil 3D.");

        var expected = new JsonObject
        {
            ["schema"] = 1,
            ["document"] = doc.Name,
            ["built_utc"] = DateTime.UtcNow.ToString("o"),
            ["plugin_version"] = App.PluginVersion,
            ["units_note"] = "Values are in drawing units (the Civil 3D metric template: m, m2, m3).",
        };

        using var _ = doc.LockDocument();
        var db = doc.Database;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForWrite);
            ObjectId Layer(string name, short aci)
            {
                if (layers.Has(name)) return layers[name];
                var l = new LayerTableRecord { Name = name, Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, aci) };
                var id = layers.Add(l);
                tr.AddNewlyCreatedDBObject(l, true);
                return id;
            }
            var surfLayer = Layer(LayerSurfaces, 3);
            var dataLayer = Layer(LayerData, 1);
            var lockedLayer = Layer(LayerLocked, 8);

            var style = civil.Styles.SurfaceStyles[0];
            var styleName = ((Autodesk.Civil.DatabaseServices.Styles.StyleBase)tr.GetObject(style, OpenMode.ForRead)).Name;

            ObjectId Tin(string name, ObjectId layer, Func<double, double, double> z, double step)
            {
                var id = TinSurface.Create(name, style);
                var tin = (TinSurface)tr.GetObject(id, OpenMode.ForWrite);
                tin.LayerId = layer;
                tin.Description = "Horizun fixture";
                var pts = new Point3dCollection();
                for (var x = 0.0; x <= 100 + 1e-9; x += step)
                    for (var y = 0.0; y <= 100 + 1e-9; y += step)
                        pts.Add(new Point3d(x, y, z(x, y)));
                tin.AddVertices(pts);
                tin.Rebuild();
                return id;
            }

            var eg = Tin("HZ_EG", surfLayer, Eg, 10);
            var fg = Tin("HZ_FG", surfLayer, (_, _) => 101, 25);
            var locked = Tin("HZ_LOCKED", lockedLayer, (_, _) => 100, 100);
            var vol = TinVolumeSurface.Create("HZ_EG_FG_VOL", eg, fg, style);
            ((TinVolumeSurface)tr.GetObject(vol, OpenMode.ForWrite)).LayerId = surfLayer;

            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
            var brkPts = new Point3dCollection { new Point3d(20, 50, 110), new Point3d(50, 50, 110), new Point3d(80, 50, 110) };
            var brk = new Polyline3d(Poly3dType.SimplePoly, brkPts, false) { LayerId = dataLayer };
            var brkId = ms.AppendEntity(brk);
            tr.AddNewlyCreatedDBObject(brk, true);

            var limit = new Polyline { LayerId = dataLayer, Closed = true };
            limit.AddVertexAt(0, new Point2d(10, 10), 0, 0, 0);
            limit.AddVertexAt(1, new Point2d(90, 10), 0, 0, 0);
            limit.AddVertexAt(2, new Point2d(90, 90), 0, 0, 0);
            limit.AddVertexAt(3, new Point2d(10, 90), 0, 0, 0);
            var limitId = ms.AppendEntity(limit);
            tr.AddNewlyCreatedDBObject(limit, true);

            // Grading platform: closed 20 x 20 m square at elevation 104, centred on (50,50).
            var platform = new Polyline { LayerId = dataLayer, Closed = true, Elevation = 104 };
            platform.AddVertexAt(0, new Point2d(40, 40), 0, 0, 0);
            platform.AddVertexAt(1, new Point2d(60, 40), 0, 0, 0);
            platform.AddVertexAt(2, new Point2d(60, 60), 0, 0, 0);
            platform.AddVertexAt(3, new Point2d(40, 60), 0, 0, 0);
            var platformId = ms.AppendEntity(platform);
            tr.AddNewlyCreatedDBObject(platform, true);

            var open = new Polyline { LayerId = dataLayer, Closed = false };
            open.AddVertexAt(0, new Point2d(0, 0), 0, 0, 0);
            open.AddVertexAt(1, new Point2d(100, 100), 0, 0, 0);
            var openId = ms.AppendEntity(open);
            tr.AddNewlyCreatedDBObject(open, true);

            // Road centreline for block A (alignment/profile/sections/corridor): straight, 90 m, along y = 50.
            var road = new Polyline { LayerId = dataLayer, Closed = false };
            road.AddVertexAt(0, new Point2d(5, 50), 0, 0, 0);
            road.AddVertexAt(1, new Point2d(95, 50), 0, 0, 0);
            var roadId = ms.AppendEntity(road);
            tr.AddNewlyCreatedDBObject(road, true);

            ((LayerTableRecord)tr.GetObject(lockedLayer, OpenMode.ForWrite)).IsLocked = true;
            tr.Commit();

            expected["style"] = styleName;
            expected["surfaces"] = new JsonObject
            {
                ["HZ_EG"] = new JsonObject
                {
                    ["handle"] = eg.Handle.ToString(), ["kind"] = "tin", ["points"] = 121, ["area_2d"] = 10000.0,
                    ["min_elevation"] = 100.0, ["max_elevation"] = 103.0,
                    ["samples"] = new JsonArray(
                        new JsonObject { ["x"] = 25.0, ["y"] = 75.0, ["z"] = Eg(25, 75) },
                        new JsonObject { ["x"] = 63.0, ["y"] = 17.0, ["z"] = Eg(63, 17) }),
                    ["outside_sample"] = new JsonObject { ["x"] = 150.0, ["y"] = 150.0 },
                },
                ["HZ_FG"] = new JsonObject { ["handle"] = fg.Handle.ToString(), ["kind"] = "tin", ["elevation"] = 101.0 },
                ["HZ_LOCKED"] = new JsonObject { ["handle"] = locked.Handle.ToString(), ["layer"] = LayerLocked, ["layer_locked"] = true },
                ["HZ_EG_FG_VOL"] = new JsonObject
                {
                    ["handle"] = vol.Handle.ToString(), ["kind"] = "tin_volume", ["base"] = "HZ_EG", ["comparison"] = "HZ_FG",
                    ["cut"] = 17500.0 / 3.0, ["fill"] = 2500.0 / 3.0, ["net_abs"] = 5000.0,
                    ["derivation"] = "d = FG - EG = 1 - 0.02x - 0.01y on [0,100]^2; fill = 0.005*100^3/6; cut = fill + 5000.",
                },
            };
            expected["entities"] = new JsonObject
            {
                ["breakline_3d"] = new JsonObject { ["handle"] = brkId.Handle.ToString(), ["vertices"] = 3, ["z"] = 110.0, ["from"] = "(20,50)", ["to"] = "(80,50)" },
                ["outer_limit"] = new JsonObject { ["handle"] = limitId.Handle.ToString(), ["closed"] = true, ["area"] = 6400.0 },
                ["open_polyline"] = new JsonObject { ["handle"] = openId.Handle.ToString(), ["closed"] = false, ["use"] = "must be refused as a boundary" },
                ["road_centerline"] = new JsonObject
                {
                    ["handle"] = roadId.Handle.ToString(), ["from"] = "(5,50)", ["to"] = "(95,50)", ["length"] = 90.0,
                    ["eg_profile"] = "z(s) = 100 + 0.02(5 + s) + 0.01*50 = 100.6 + 0.02 s",
                },
                ["platform"] = new JsonObject
                {
                    ["handle"] = platformId.Handle.ToString(), ["closed"] = true, ["z"] = 104.0, ["square"] = "(40,40)-(60,60)",
                    ["fill_slope_2_1_north_daylight_distance_at_x50"] = 2.4 / 0.51,
                    ["z_inside_and_on_edge"] = 104.0,
                    ["fill_under_platform"] = 1000.0,
                    ["derivation"] = "All fill (EG <= 102.2 under the platform). Under the platform alone: 400 m2 x (104 - EG(50,50)=101.5) = 1000 m3, so total fill > 1000 and cut ~ 0. North daylight at x=50: 104 - d/2 = 101.6 + 0.01d -> d = 2.4/0.51 = 4.706 m.",
                },
            };
        }

        var dir = HorizunPaths.FixturesDir();
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "fixture-expected.json"), expected.ToJsonString(Hz.Indented));
        return expected;
    }
}
