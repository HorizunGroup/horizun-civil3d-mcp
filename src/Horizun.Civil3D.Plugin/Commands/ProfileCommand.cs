// -----------------------------------------------------------------------------
// horizun_c3d_profile - get, elevation_at, check_k, create_from_surface,
// create_layout, create_view.
// API: docs/api-probes/2025/AeccDbMgd.phase3-roads.txt, AeccDbMgd.phase3-profile-entities.txt.
//
// create_layout builds fixed tangents between consecutive PVIs, then a
// symmetric parabola (curve_length) at each requested PVI; the re-read checks
// every PVI station/elevation and every curve length.
// -----------------------------------------------------------------------------
using System.Text.Json.Nodes;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.DatabaseServices;
using Horizun.Civil3D.Core;
using Horizun.Civil3D.Plugin.Civil;
using Surface = Autodesk.Civil.DatabaseServices.Surface;

namespace Horizun.Civil3D.Plugin.Commands;

internal sealed class ProfileCommand : ICommand
{
    public string Name => "profile";

    public CommandResult Execute(CommandContext ctx)
    {
        if (ToolRules.Validate(ctx.Tool.Name, ctx.Args) is { } why) throw new HzRefusal(ErrorCodes.InvalidInput, why + " Nothing ran.");
        return ctx.Action switch
        {
            "get" => WriteFlow.Read(ctx, (doc, tr, data) => data["profile"] = Describe(Resolve.Open<Profile>(tr, Find(ctx, doc, tr)), tr)),
            "elevation_at" => WriteFlow.Read(ctx, (doc, tr, data) => ElevationAt(ctx, doc, tr, data)),
            "check_k" => WriteFlow.Read(ctx, (doc, tr, data) => CheckK(ctx, doc, tr, data)),
            "create_from_surface" => FromSurface(ctx),
            "create_layout" => Layout(ctx),
            _ => View(ctx),
        };
    }

    /// <summary>Profile by handle, or by name - within the given alignment when names repeat across alignments.</summary>
    private static ObjectId Find(CommandContext ctx, Document doc, Transaction tr)
    {
        if (Hz.Str(ctx.Args, "handle") != null || Hz.Str(ctx.Args, "alignment") == null) return Resolve.Target(doc, tr, "profile", ctx.Args);
        var al = Resolve.Open<Alignment>(tr, Resolve.Named(doc, tr, "alignment", Hz.Str(ctx.Args, "alignment")!));
        var name = Hz.Str(ctx.Args, "name")!;
        var names = new List<string>();
        foreach (ObjectId id in al.GetProfileIds())
        {
            var n = Catalog.NameOf(id, tr) ?? "";
            names.Add(n);
            if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase)) return id;
        }
        throw new HzRefusal(ErrorCodes.NotFound, "Alignment '" + al.Name + "' has no profile named '" + name + "'. Nothing ran.", new JsonObject { ["candidates"] = Hz.Strings(names) });
    }

    internal static JsonObject Curve(ProfileEntity? e)
    {
        if (e == null) return new JsonObject { ["type"] = "none" };
        var o = new JsonObject { ["type"] = e.EntityType.ToString(), ["length"] = Hz.Finite(e.Length, 6), ["start_station"] = Hz.Finite(e.StartStation, 6), ["end_station"] = Hz.Finite(e.EndStation, 6) };
        switch (e)
        {
            case ProfileParabolaSymmetric p:
                o["curve_type"] = p.CurveType.ToString(); o["k"] = Hz.Finite(p.K, 6); o["grade_in"] = Hz.Finite(p.GradeIn, 9); o["grade_out"] = Hz.Finite(p.GradeOut, 9);
                break;
            case ProfileCircular c:
                o["curve_type"] = c.CurveType.ToString(); o["k"] = Hz.Finite(c.K, 6); o["radius"] = Hz.Finite(c.Radius, 6);
                break;
        }
        return o;
    }

    /// <summary>Live finding: GradeIn throws on the first PVI (and GradeOut on the last); no grade there = null.</summary>
    private static JsonNode? Grade(Func<double> g)
    {
        try { return Hz.Finite(g(), 9); } catch (InvalidOperationException) { return null; }
    }

    internal static JsonObject Describe(Profile p, Transaction tr)
    {
        var o = Catalog.Describe(p, tr, new Catalog.Lookup(tr), true);
        var pvis = new JsonArray();
        for (var i = 0; i < p.PVIs.Count; i++)
        {
            var v = p.PVIs[i];
            pvis.Add(new JsonObject
            {
                ["station"] = Hz.Finite(v.RawStation, 6), ["elevation"] = Hz.Finite(v.Elevation, 6),
                ["grade_in"] = Grade(() => v.GradeIn), ["grade_out"] = Grade(() => v.GradeOut),
                ["curve"] = Curve(v.VerticalCurve),
            });
        }
        o["grade_unit"] = "decimal ratio (0.03 = 3 %)";
        o["pvis"] = pvis;
        o["elevation_min"] = Hz.Finite(p.ElevationMin, 6);
        o["elevation_max"] = Hz.Finite(p.ElevationMax, 6);
        return o;
    }

    private static void ElevationAt(CommandContext ctx, Document doc, Transaction tr, JsonObject data)
    {
        var p = Resolve.Open<Profile>(tr, Find(ctx, doc, tr));
        var rows = new JsonArray();
        foreach (var s in Resolve.Numbers(ctx.Args["stations"]))
        {
            if (s < p.StartingStation - 1e-9 || s > p.EndingStation + 1e-9)
            {
                rows.Add(new JsonObject { ["station"] = s, ["elevation"] = null, ["grade"] = null, ["reason"] = "outside the profile " + p.StartingStation + "-" + p.EndingStation });
                continue;
            }
            rows.Add(new JsonObject { ["station"] = s, ["elevation"] = Hz.Finite(p.ElevationAt(s), 6), ["grade"] = Hz.Finite(p.GradeAt(s), 9) });
        }
        data["profile"] = p.Name;
        data["grade_unit"] = "decimal ratio";
        data["rows"] = rows;
    }

    private static void CheckK(CommandContext ctx, Document doc, Transaction tr, JsonObject data)
    {
        var p = Resolve.Open<Profile>(tr, Find(ctx, doc, tr));
        var crest = Hz.Num(ctx.Args, "min_k_crest");
        var sag = Hz.Num(ctx.Args, "min_k_sag");
        var rows = new JsonArray();
        int fails = 0, unknown = 0;
        for (var i = 0; i < p.PVIs.Count; i++)
        {
            var v = p.PVIs[i];
            if (v.VerticalCurve == null) continue;
            var isCrest = v.GradeIn > v.GradeOut;
            var a = Math.Abs(v.GradeOut - v.GradeIn) * 100;
            var k = a > 0 ? v.VerticalCurve.Length / a : double.NaN;
            var min = isCrest ? crest : sag;
            var row = new JsonObject
            {
                ["pvi_station"] = Hz.Finite(v.RawStation, 6), ["type"] = isCrest ? "crest" : "sag", ["length"] = Hz.Finite(v.VerticalCurve.Length, 6),
                ["grade_change_pct"] = Hz.Finite(a, 6), ["k"] = Hz.Finite(k, 6), ["min_k"] = min,
            };
            if (min == null) { row["result"] = "not_checked"; row["reason"] = "No criterion given for " + (isCrest ? "crest" : "sag") + " curves."; unknown++; }
            else if (!Hz.IsFinite(k)) { row["result"] = "not_checked"; row["reason"] = "No grade change."; unknown++; }
            else { var ok = k >= min.Value; row["result"] = ok ? "pass" : "fail"; if (!ok) fails++; }
            rows.Add(row);
        }
        data["profile"] = p.Name;
        data["k_definition"] = "K = curve length / |grade change in %| (drawing units per %).";
        data["criteria"] = new JsonObject { ["min_k_crest"] = crest, ["min_k_sag"] = sag, ["source"] = "caller-supplied; no design code is built in" };
        data["curves"] = rows;
        data["summary"] = new JsonObject { ["curves"] = rows.Count, ["failing"] = fails, ["not_checked"] = unknown };
    }

    private static (ObjectId Layer, ObjectId Style, ObjectId Labels) Common(CommandContext ctx, Document doc, Transaction tr, JsonObject plan)
    {
        var civil = CommandContext.Civil(doc);
        var layer = Resolve.Layer(doc.Database, tr, Hz.Str(ctx.Args, "layer"));
        var style = Resolve.Style(civil, tr, "profile", Hz.Str(ctx.Args, "style"));
        var labels = Resolve.ProfileLabelSet(civil, tr, Hz.Str(ctx.Args, "label_set"));
        plan["layer"] = ((LayerTableRecord)tr.GetObject(layer, OpenMode.ForRead)).Name;
        plan["style"] = Catalog.NameOf(style, tr) ?? ((Autodesk.Civil.DatabaseServices.Styles.StyleBase)tr.GetObject(style, OpenMode.ForRead)).Name;
        plan["label_set"] = ((Autodesk.Civil.DatabaseServices.Styles.StyleBase)tr.GetObject(labels, OpenMode.ForRead)).Name;
        return (layer, style, labels);
    }

    private static void UniqueInAlignment(Alignment al, Transaction tr, string name)
    {
        foreach (ObjectId id in al.GetProfileIds())
            if (string.Equals(Catalog.NameOf(id, tr), name, StringComparison.OrdinalIgnoreCase))
                throw new HzRefusal(ErrorCodes.InvalidInput, "Alignment '" + al.Name + "' already has a profile named '" + name + "'. Nothing changed.");
    }

    private static CommandResult FromSurface(CommandContext ctx)
    {
        var name = Hz.Str(ctx.Args, "new_name")!;
        var offset = Hz.Num(ctx.Args, "offset") ?? 0;
        ObjectId alId = ObjectId.Null, sfId = ObjectId.Null, created = ObjectId.Null;
        (ObjectId Layer, ObjectId Style, ObjectId Labels) c = default;
        double start = 0, end = 0;
        return WriteFlow.Run(ctx, "HZ_PROFILE",
            (doc, tr, plan) =>
            {
                alId = Resolve.Named(doc, tr, "alignment", Hz.Str(ctx.Args, "alignment")!);
                sfId = Resolve.Named(doc, tr, "surface", Hz.Str(ctx.Args, "surface")!);
                var al = Resolve.Open<Alignment>(tr, alId);
                UniqueInAlignment(al, tr, name);
                start = al.StartingStation; end = al.EndingStation;
                c = Common(ctx, doc, tr, plan);
                plan["alignment"] = al.Name; plan["surface"] = Catalog.NameOf(sfId, tr); plan["offset"] = offset; plan["new_name"] = name;
                plan["surface_fingerprint"] = ((Surface)tr.GetObject(sfId, OpenMode.ForRead)).ComputeFingerPrint().ToString();
            },
            (doc, tr) => created = Profile.CreateFromSurface(name, alId, sfId, c.Layer, c.Style, c.Labels, offset, start, end),
            (doc, tr, v, after) =>
            {
                var p = Resolve.Open<Profile>(tr, created);
                var al = Resolve.Open<Alignment>(tr, alId);
                var sf = (Surface)tr.GetObject(sfId, OpenMode.ForRead);
                v.Text("name", name, p.Name, false);
                v.Text("alignment", al.Handle.ToString(), p.AlignmentId.Handle.ToString(), false);
                int ok = 0, tested = 0, outside = 0;
                var worst = new JsonArray();
                for (var i = 0; i <= 10; i++)
                {
                    var s = p.StartingStation + (p.EndingStation - p.StartingStation) * i / 10.0;
                    double x = 0, y = 0;
                    al.PointLocation(s, offset, ref x, ref y);
                    double zs;
                    try { zs = sf.FindElevationAtXY(x, y); } catch (Autodesk.Civil.PointNotOnEntityException) { outside++; continue; }
                    tested++;
                    var zp = p.ElevationAt(s);
                    if (Math.Abs(zp - zs) <= 1e-3) ok++;
                    else if (worst.Count < 5) worst.Add(new JsonObject { ["station"] = s, ["profile_z"] = zp, ["surface_z"] = zs });
                }
                var actual = new JsonObject { ["tested"] = tested, ["matched"] = ok, ["outside_surface"] = outside };
                if (worst.Count > 0) actual["mismatches"] = worst;
                v.Check("profile = surface elevation at 11 sample stations (tol 1e-3)", tested, actual, tested > 0 && ok == tested);
                after["profile"] = Describe(p, tr);
            });
    }

    private static CommandResult Layout(CommandContext ctx)
    {
        var name = Hz.Str(ctx.Args, "new_name")!;
        var pvis = ((JsonArray)ctx.Args["pvis"]!).Cast<JsonObject>()
            .Select(o => (St: Hz.Num(o, "station")!.Value, El: Hz.Num(o, "elevation")!.Value, L: Hz.Num(o, "curve_length"))).ToList();
        ObjectId alId = ObjectId.Null, created = ObjectId.Null;
        (ObjectId Layer, ObjectId Style, ObjectId Labels) c = default;
        return WriteFlow.Run(ctx, "HZ_PROFILE",
            (doc, tr, plan) =>
            {
                alId = Resolve.Named(doc, tr, "alignment", Hz.Str(ctx.Args, "alignment")!);
                var al = Resolve.Open<Alignment>(tr, alId);
                UniqueInAlignment(al, tr, name);
                if (pvis[0].St < al.StartingStation - 1e-6 || pvis[^1].St > al.EndingStation + 1e-6)
                    throw new HzRefusal(ErrorCodes.InvalidInput, "PVI stations " + pvis[0].St + "-" + pvis[^1].St + " exceed the alignment " + al.StartingStation + "-" + al.EndingStation + ". Nothing changed.");
                for (var i = 1; i + 1 < pvis.Count; i++)
                    if (pvis[i].L is { } L && (L / 2 > pvis[i].St - pvis[i - 1].St + 1e-9 || L / 2 > pvis[i + 1].St - pvis[i].St + 1e-9))
                        throw new HzRefusal(ErrorCodes.InvalidInput, "The vertical curve at station " + pvis[i].St + " (L=" + L + ") does not fit between its neighbouring PVIs. Nothing changed.");
                c = Common(ctx, doc, tr, plan);
                var rows = new JsonArray();
                for (var i = 0; i < pvis.Count; i++)
                {
                    var r = new JsonObject { ["station"] = pvis[i].St, ["elevation"] = pvis[i].El, ["curve_length"] = pvis[i].L };
                    if (i > 0) r["grade_in_pct"] = Hz.Finite((pvis[i].El - pvis[i - 1].El) / (pvis[i].St - pvis[i - 1].St) * 100, 6);
                    if (i + 1 < pvis.Count) r["grade_out_pct"] = Hz.Finite((pvis[i + 1].El - pvis[i].El) / (pvis[i + 1].St - pvis[i].St) * 100, 6);
                    if (pvis[i].L is { } L && i > 0 && i + 1 < pvis.Count)
                    {
                        var a = Math.Abs((pvis[i + 1].El - pvis[i].El) / (pvis[i + 1].St - pvis[i].St) - (pvis[i].El - pvis[i - 1].El) / (pvis[i].St - pvis[i - 1].St)) * 100;
                        r["expected_k"] = a > 0 ? Hz.Finite(L / a, 6) : null;
                    }
                    rows.Add(r);
                }
                plan["alignment"] = al.Name; plan["new_name"] = name; plan["pvis"] = rows;
            },
            (doc, tr) =>
            {
                created = Profile.CreateByLayout(name, alId, c.Layer, c.Style, c.Labels);
                var p = (Profile)tr.GetObject(created, OpenMode.ForWrite);
                for (var i = 0; i + 1 < pvis.Count; i++)
                    p.Entities.AddFixedTangent(new Point2d(pvis[i].St, pvis[i].El), new Point2d(pvis[i + 1].St, pvis[i + 1].El));
                for (var i = 1; i + 1 < pvis.Count; i++)
                    if (pvis[i].L is { } L)
                        p.Entities.AddFreeSymmetricParabolaByPVIAndCurveLength(p.PVIs.GetPVIAt(pvis[i].St, pvis[i].El), L);
            },
            (doc, tr, v, after) =>
            {
                var p = Resolve.Open<Profile>(tr, created);
                v.Text("name", name, p.Name, false);
                v.Check("PVI count", pvis.Count, p.PVIs.Count, p.PVIs.Count == pvis.Count);
                for (var i = 0; i < Math.Min(p.PVIs.Count, pvis.Count); i++)
                {
                    var got = p.PVIs[i];
                    v.Number("PVI " + i + " station", pvis[i].St, got.RawStation, 1e-6);
                    v.Number("PVI " + i + " elevation", pvis[i].El, got.Elevation, 1e-6);
                    if (pvis[i].L is { } L) v.Number("PVI " + i + " curve length", L, got.VerticalCurve?.Length ?? double.NaN, 1e-6);
                }
                after["profile"] = Describe(p, tr);
            });
    }

    private static CommandResult View(CommandContext ctx)
    {
        var insert = Resolve.P(ctx.Args["insert"]);
        var name = Hz.Str(ctx.Args, "new_name");
        ObjectId alId = ObjectId.Null, style = ObjectId.Null, bands = ObjectId.Null, created = ObjectId.Null;
        return WriteFlow.Run(ctx, "HZ_PROFILE",
            (doc, tr, plan) =>
            {
                var civil = CommandContext.Civil(doc);
                alId = Resolve.Named(doc, tr, "alignment", Hz.Str(ctx.Args, "alignment")!);
                if (name != null) Resolve.Unique(doc, tr, "profile_view", name);
                style = Resolve.Style(civil, tr, "profile_view", Hz.Str(ctx.Args, "style"));
                bands = Resolve.BandSet(civil, tr, Hz.Str(ctx.Args, "band_set"));
                plan["alignment"] = Catalog.NameOf(alId, tr); plan["insert"] = Resolve.Json(insert, false); plan["new_name"] = name;
                plan["style"] = ((Autodesk.Civil.DatabaseServices.Styles.StyleBase)tr.GetObject(style, OpenMode.ForRead)).Name;
                plan["band_set"] = ((Autodesk.Civil.DatabaseServices.Styles.StyleBase)tr.GetObject(bands, OpenMode.ForRead)).Name;
            },
            (doc, tr) => created = ProfileView.Create(alId, insert, name ?? "", bands, style),
            (doc, tr, v, after) =>
            {
                var pv = Resolve.Open<ProfileView>(tr, created);
                v.Text("profile view alignment", alId.Handle.ToString(), pv.AlignmentId.Handle.ToString(), false);
                if (name != null) v.Text("name", name, pv.Name, false);
                v.Text("style handle", style.Handle.ToString(), pv.StyleId.Handle.ToString(), false);
                after["profile_view"] = new JsonObject { ["name"] = pv.Name, ["handle"] = pv.Handle.ToString(), ["station_start"] = Hz.Finite(pv.StationStart, 6), ["station_end"] = Hz.Finite(pv.StationEnd, 6) };
            });
    }
}
