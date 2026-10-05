// -----------------------------------------------------------------------------
// horizun_c3d_cad_styles - text, dimension and multileader styles, annotation
// scales and linetypes (block C).
// -----------------------------------------------------------------------------
using System.Text.Json.Nodes;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Horizun.Civil3D.Core;
using Horizun.Civil3D.Plugin.Civil;
using AcColor = Autodesk.AutoCAD.Colors.Color;
using ColorMethod = Autodesk.AutoCAD.Colors.ColorMethod;

namespace Horizun.Civil3D.Plugin.Commands;

internal sealed class CadStylesCommand : ICommand
{
    public string Name => "cad_styles";
    private const string Scales = "ACDB_ANNOTATIONSCALES";

    public CommandResult Execute(CommandContext ctx)
    {
        if (ToolRules.Validate(ctx.Tool.Name, ctx.Args) is { } why) throw new HzRefusal(ErrorCodes.InvalidInput, why + " Nothing ran.");
        return ctx.Action switch
        {
            "list" => WriteFlow.Read(ctx, (doc, tr, data) => List(Hz.Str(ctx.Args, "kind")!, doc, tr, data)),
            "text_create" or "text_set" => TextStyle(ctx),
            "dim_create" or "dim_set" => DimStyleWrite(ctx),
            "mleader_create" => MLeaderStyleCreate(ctx),
            "set_current" => SetCurrent(ctx),
            "scale_add" => ScaleAdd(ctx),
            _ => LinetypeLoad(ctx),
        };
    }

    // ---- reads -----------------------------------------------------------------

    private static JsonObject TextJson(TextStyleTableRecord t, Database db) => new()
    {
        ["name"] = t.Name, ["font"] = t.FileName, ["big_font"] = t.BigFontFileName, ["typeface"] = t.Font.TypeFace,
        ["height"] = Hz.Finite(t.TextSize, 6), ["width_factor"] = Hz.Finite(t.XScale, 6), ["oblique_deg"] = Hz.Finite(t.ObliquingAngle * 180 / Math.PI, 6),
        ["annotative"] = t.Annotative == AnnotativeStates.True, ["current"] = db.Textstyle == t.ObjectId,
    };

    private static JsonObject DimJson(DimStyleTableRecord d, Transaction tr, Database db)
    {
        var o = new JsonObject { ["name"] = d.Name, ["annotative"] = d.Annotative == AnnotativeStates.True, ["current"] = db.Dimstyle == d.ObjectId };
        foreach (var k in CadInputs.DimVars.Keys) o[k] = DimGet(d, k, tr);
        return o;
    }

    private static void List(string kind, Document doc, Transaction tr, JsonObject data)
    {
        var db = doc.Database;
        var rows = new JsonArray();
        switch (kind)
        {
            case "text":
                foreach (ObjectId id in (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead))
                {
                    var t = (TextStyleTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    if (!t.IsShapeFile) rows.Add(TextJson(t, db));
                }
                break;
            case "dim":
                foreach (ObjectId id in (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead))
                    rows.Add(DimJson((DimStyleTableRecord)tr.GetObject(id, OpenMode.ForRead), tr, db));
                break;
            case "mleader":
                foreach (DBDictionaryEntry e in (DBDictionary)tr.GetObject(db.MLeaderStyleDictionaryId, OpenMode.ForRead))
                {
                    var s = (MLeaderStyle)tr.GetObject(e.Value, OpenMode.ForRead);
                    rows.Add(new JsonObject
                    {
                        ["name"] = e.Key, ["text_height"] = Hz.Finite(s.TextHeight, 6), ["arrow_size"] = Hz.Finite(s.ArrowSize, 6), ["landing_gap"] = Hz.Finite(s.LandingGap, 6),
                        ["text_style"] = s.TextStyleId.IsNull ? null : ((TextStyleTableRecord)tr.GetObject(s.TextStyleId, OpenMode.ForRead)).Name,
                        ["annotative"] = s.Annotative == AnnotativeStates.True, ["current"] = db.MLeaderstyle == e.Value,
                    });
                }
                break;
            case "linetype":
                foreach (ObjectId id in (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead))
                {
                    var l = (LinetypeTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    rows.Add(new JsonObject { ["name"] = l.Name, ["description"] = l.AsciiDescription, ["pattern_length"] = Hz.Finite(l.PatternLength, 6), ["dashes"] = l.NumDashes });
                }
                break;
            case "scale":
                foreach (ObjectContext c in db.ObjectContextManager.GetContextCollection(Scales))
                    if (c is AnnotationScale s)
                        rows.Add(new JsonObject { ["name"] = s.Name, ["paper_units"] = s.PaperUnits, ["drawing_units"] = s.DrawingUnits, ["current"] = db.Cannoscale?.Name == s.Name });
                break;
            default:
                foreach (DBDictionaryEntry e in (DBDictionary)tr.GetObject(db.TableStyleDictionaryId, OpenMode.ForRead))
                    rows.Add(new JsonObject { ["name"] = e.Key, ["current"] = db.Tablestyle == e.Value });
                break;
        }
        data["kind"] = kind;
        data["styles"] = rows;
    }

    // ---- text styles --------------------------------------------------------------

    private static string FontCheck(Database db, string font)
    {
        if (font.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || font.EndsWith(".otf", StringComparison.OrdinalIgnoreCase) || font.EndsWith(".ttc", StringComparison.OrdinalIgnoreCase))
        {
            var sys = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), Path.GetFileName(font));
            if (File.Exists(sys) || File.Exists(font)) return "TrueType";
            var user = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts", Path.GetFileName(font));
            if (File.Exists(user)) return "TrueType (user font)";
        }
        else
        {
            var name = font.EndsWith(".shx", StringComparison.OrdinalIgnoreCase) ? font : font + ".shx";
            var p = HostApplicationServices.Current.FindFile(name, db, FindFileHint.CompiledShapeFile);
            if (!string.IsNullOrEmpty(p)) return "SHX";
        }
        throw new HzRefusal(ErrorCodes.NotFound, "Font '" + font + "' was not found (Windows Fonts or the AutoCAD support path). Nothing changed.");
    }

    private static CommandResult TextStyle(CommandContext ctx)
    {
        var create = ctx.Action == "text_create";
        var name = Hz.Str(ctx.Args, create ? "new_name" : "name")!;
        var font = Hz.Str(ctx.Args, "font");
        var height = Hz.Num(ctx.Args, "height");
        var wf = Hz.Num(ctx.Args, "width_factor");
        var obl = Hz.Num(ctx.Args, "oblique");
        var ann = Hz.Bool(ctx.Args, "annotative");
        var id = ObjectId.Null;
        return WriteFlow.Run(ctx, "HZ_CAD_STYLES",
            (doc, tr, plan) =>
            {
                var db = doc.Database;
                if (create) Cad.NewSymbol(tr, db.TextStyleTableId, name, "text style");
                else
                {
                    id = Cad.Symbol(tr, db.TextStyleTableId, name, "text style");
                    plan["before"] = TextJson((TextStyleTableRecord)tr.GetObject(id, OpenMode.ForRead), db);
                    if (font == null && height == null && wf == null && obl == null && ann == null) throw new HzRefusal(ErrorCodes.InvalidInput, "Give at least one property to change. Nothing changed.");
                }
                if (font != null) plan["font_kind"] = FontCheck(db, font);
                plan["name"] = name; plan["changes"] = Hz.Without(ctx.Args, "action", "target_document", "dry_run", "confirmation_token", "name", "new_name");
            },
            (doc, tr) =>
            {
                var db = doc.Database;
                TextStyleTableRecord t;
                if (create)
                {
                    t = new TextStyleTableRecord { Name = name };
                    var table = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForWrite);
                    id = table.Add(t);
                    tr.AddNewlyCreatedDBObject(t, true);
                }
                else t = (TextStyleTableRecord)tr.GetObject(id, OpenMode.ForWrite);
                if (font != null) t.FileName = font;
                if (height is { } h) t.TextSize = h;
                if (wf is { } w) t.XScale = w;
                if (obl is { } o) t.ObliquingAngle = Cad.Rad(o);
                if (ann is { } a) t.Annotative = a ? AnnotativeStates.True : AnnotativeStates.False;
            },
            (doc, tr, v, after) =>
            {
                var t = (TextStyleTableRecord)tr.GetObject(id, OpenMode.ForRead);
                if (font != null) v.Text("font", font, t.FileName);
                if (height is { } h) v.Number("height", h, t.TextSize, 1e-9);
                if (wf is { } w) v.Number("width factor", w, t.XScale, 1e-9);
                if (obl is { } o) v.Number("oblique", Cad.Rad(o), t.ObliquingAngle, 1e-9);
                if (ann is { } a) v.Flag("annotative", a, t.Annotative == AnnotativeStates.True);
                if (create) v.Text("name", name, t.Name);
                after["style"] = TextJson(t, doc.Database);
            });
    }

    // ---- dimension styles ------------------------------------------------------------

    internal static JsonNode? DimGet(DimStyleTableRecord d, string k, Transaction tr) => k switch
    {
        "dimtxt" => d.Dimtxt, "dimasz" => d.Dimasz, "dimexe" => d.Dimexe, "dimexo" => d.Dimexo, "dimgap" => d.Dimgap, "dimdec" => d.Dimdec,
        "dimlfac" => d.Dimlfac, "dimscale" => d.Dimscale, "dimrnd" => d.Dimrnd, "dimtad" => d.Dimtad, "dimtih" => d.Dimtih, "dimtoh" => d.Dimtoh,
        "dimclrd" => (int)d.Dimclrd.ColorIndex, "dimclre" => (int)d.Dimclre.ColorIndex, "dimclrt" => (int)d.Dimclrt.ColorIndex,
        "dimtxsty" => d.Dimtxsty.IsNull ? null : ((TextStyleTableRecord)tr.GetObject(d.Dimtxsty, OpenMode.ForRead)).Name,
        "dimblk" => d.Dimblk.IsNull ? "" : ((BlockTableRecord)tr.GetObject(d.Dimblk, OpenMode.ForRead)).Name,
        "dimpost" => d.Dimpost, "dimdsep" => d.Dimdsep.ToString(), "dimzin" => d.Dimzin, "dimadec" => d.Dimadec, "dimcen" => d.Dimcen,
        _ => null,
    };

    private static void DimSet(DimStyleTableRecord d, string k, JsonNode v, Database db, Transaction tr)
    {
        double N() => Hz.AsDouble(v)!.Value;
        int I() => (int)Math.Round(N());
        AcColor C() => AcColor.FromColorIndex(ColorMethod.ByAci, (short)I());
        switch (k)
        {
            case "dimtxt": d.Dimtxt = N(); break; case "dimasz": d.Dimasz = N(); break; case "dimexe": d.Dimexe = N(); break;
            case "dimexo": d.Dimexo = N(); break; case "dimgap": d.Dimgap = N(); break; case "dimdec": d.Dimdec = I(); break;
            case "dimlfac": d.Dimlfac = N(); break; case "dimscale": d.Dimscale = N(); break; case "dimrnd": d.Dimrnd = N(); break;
            case "dimtad": d.Dimtad = I(); break; case "dimtih": d.Dimtih = v.GetValue<bool>(); break; case "dimtoh": d.Dimtoh = v.GetValue<bool>(); break;
            case "dimclrd": d.Dimclrd = C(); break; case "dimclre": d.Dimclre = C(); break; case "dimclrt": d.Dimclrt = C(); break;
            case "dimtxsty": d.Dimtxsty = Cad.Symbol(tr, db.TextStyleTableId, v.GetValue<string>(), "text style"); break;
            case "dimblk": { var n = v.GetValue<string>(); d.Dimblk = n.Length == 0 ? ObjectId.Null : Cad.Symbol(tr, db.BlockTableId, n, "arrow block"); break; }
            case "dimpost": d.Dimpost = v.GetValue<string>(); break;
            case "dimdsep": d.Dimdsep = v.GetValue<string>().FirstOrDefault('.'); break;
            case "dimzin": d.Dimzin = I(); break; case "dimadec": d.Dimadec = I(); break; case "dimcen": d.Dimcen = N(); break;
        }
    }

    private static bool DimEqual(JsonNode? want, JsonNode? got)
    {
        if (Hz.AsDouble(want) is { } a && Hz.AsDouble(got) is { } b) return Math.Abs(a - b) <= 1e-9 * Math.Max(1, Math.Abs(a));
        if (want is JsonValue wv && wv.TryGetValue<bool>(out var wb) && got is JsonValue gv && gv.TryGetValue<bool>(out var gb)) return wb == gb;
        return string.Equals(want?.ToString(), got?.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static CommandResult DimStyleWrite(CommandContext ctx)
    {
        var create = ctx.Action == "dim_create";
        var name = Hz.Str(ctx.Args, create ? "new_name" : "name")!;
        var props = (ctx.Args["properties"] as JsonObject)?.ToDictionary(kv => kv.Key.ToLowerInvariant(), kv => kv.Value!.DeepClone()) ?? new();
        var ann = Hz.Bool(ctx.Args, "annotative");
        ObjectId id = ObjectId.Null, from = ObjectId.Null;
        return WriteFlow.Run(ctx, "HZ_CAD_STYLES",
            (doc, tr, plan) =>
            {
                var db = doc.Database;
                if (create)
                {
                    Cad.NewSymbol(tr, db.DimStyleTableId, name, "dimension style");
                    from = Hz.Str(ctx.Args, "from") is { } f ? Cad.Symbol(tr, db.DimStyleTableId, f, "dimension style") : db.Dimstyle;
                    plan["copied_from"] = ((DimStyleTableRecord)tr.GetObject(from, OpenMode.ForRead)).Name;
                }
                else
                {
                    id = Cad.Symbol(tr, db.DimStyleTableId, name, "dimension style");
                    plan["before"] = DimJson((DimStyleTableRecord)tr.GetObject(id, OpenMode.ForRead), tr, db);
                }
                if (props.TryGetValue("dimtxsty", out var ts)) Cad.Symbol(tr, db.TextStyleTableId, ts.GetValue<string>(), "text style");
                if (props.TryGetValue("dimblk", out var bk) && bk.GetValue<string>().Length > 0) Cad.Symbol(tr, db.BlockTableId, bk.GetValue<string>(), "arrow block");
                plan["name"] = name; plan["properties"] = ctx.Args["properties"]?.DeepClone();
                if (ann != null) plan["annotative"] = ann;
            },
            (doc, tr) =>
            {
                var db = doc.Database;
                DimStyleTableRecord d;
                if (create)
                {
                    d = new DimStyleTableRecord();
                    d.CopyFrom((DimStyleTableRecord)tr.GetObject(from, OpenMode.ForRead));
                    d.Name = name;
                    var table = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForWrite);
                    id = table.Add(d);
                    tr.AddNewlyCreatedDBObject(d, true);
                }
                else d = (DimStyleTableRecord)tr.GetObject(id, OpenMode.ForWrite);
                foreach (var (k, v) in props) DimSet(d, k, v, db, tr);
                if (ann is { } a) d.Annotative = a ? AnnotativeStates.True : AnnotativeStates.False;
                if (!create && db.Dimstyle == id) db.SetDimstyleData(d);
            },
            (doc, tr, v, after) =>
            {
                var d = (DimStyleTableRecord)tr.GetObject(id, OpenMode.ForRead);
                if (create) v.Text("name", name, d.Name);
                foreach (var (k, want) in props)
                {
                    var got = DimGet(d, k, tr);
                    v.Check(k, want.DeepClone(), got, DimEqual(want, got));
                }
                if (ann is { } a) v.Flag("annotative", a, d.Annotative == AnnotativeStates.True);
                if (create && props.Count == 0 && ann == null) v.Flag("style created", true, !id.IsNull);
                after["style"] = DimJson(d, tr, doc.Database);
            });
    }

    // ---- multileader styles -------------------------------------------------------------

    private static CommandResult MLeaderStyleCreate(CommandContext ctx)
    {
        var name = Hz.Str(ctx.Args, "new_name")!;
        var th = Hz.Num(ctx.Args, "text_height");
        var asz = Hz.Num(ctx.Args, "arrow_size");
        var gap = Hz.Num(ctx.Args, "landing_gap");
        var ann = Hz.Bool(ctx.Args, "annotative");
        ObjectId from = ObjectId.Null, ts = ObjectId.Null, id = ObjectId.Null;
        return WriteFlow.Run(ctx, "HZ_CAD_STYLES",
            (doc, tr, plan) =>
            {
                var db = doc.Database;
                var dict = (DBDictionary)tr.GetObject(db.MLeaderStyleDictionaryId, OpenMode.ForRead);
                if (dict.Contains(name)) throw new HzRefusal(ErrorCodes.InvalidInput, "Multileader style '" + name + "' exists. Nothing changed.");
                if (Hz.Str(ctx.Args, "from") is { } f)
                {
                    if (!dict.Contains(f)) throw new HzRefusal(ErrorCodes.NotFound, "No multileader style '" + f + "'. Nothing changed.");
                    from = dict.GetAt(f);
                }
                else from = db.MLeaderstyle;
                if (Hz.Str(ctx.Args, "text_style") is { } t) ts = Cad.Symbol(tr, db.TextStyleTableId, t, "text style");
                plan["new_name"] = name; plan["copied_from"] = ((MLeaderStyle)tr.GetObject(from, OpenMode.ForRead)).Name;
                plan["changes"] = Hz.Without(ctx.Args, "action", "target_document", "dry_run", "confirmation_token", "new_name", "from");
            },
            (doc, tr) =>
            {
                var s = new MLeaderStyle((MLeaderStyle)tr.GetObject(from, OpenMode.ForRead));
                if (!ts.IsNull) s.TextStyleId = ts;
                if (th is { } h) s.TextHeight = h;
                if (asz is { } a) s.ArrowSize = a;
                if (gap is { } g) s.LandingGap = g;
                if (ann is { } an) s.Annotative = an ? AnnotativeStates.True : AnnotativeStates.False;
                id = s.PostMLeaderStyleToDb(doc.Database, name);
                tr.AddNewlyCreatedDBObject(s, true);
            },
            (doc, tr, v, after) =>
            {
                var dict = (DBDictionary)tr.GetObject(doc.Database.MLeaderStyleDictionaryId, OpenMode.ForRead);
                v.Flag("style in dictionary", true, dict.Contains(name));
                var s = (MLeaderStyle)tr.GetObject(id, OpenMode.ForRead);
                if (!ts.IsNull) v.Flag("text style", true, s.TextStyleId == ts);
                if (th is { } h) v.Number("text height", h, s.TextHeight, 1e-9);
                if (asz is { } a) v.Number("arrow size", a, s.ArrowSize, 1e-9);
                if (gap is { } g) v.Number("landing gap", g, s.LandingGap, 1e-9);
                if (ann is { } an) v.Flag("annotative", an, s.Annotative == AnnotativeStates.True);
                after["style"] = new JsonObject { ["name"] = s.Name, ["handle"] = s.Handle.ToString() };
            });
    }

    // ---- current / scales / linetypes ---------------------------------------------------

    private static CommandResult SetCurrent(CommandContext ctx)
    {
        var kind = Hz.Str(ctx.Args, "kind")!;
        var name = Hz.Str(ctx.Args, "name")!;
        var id = ObjectId.Null;
        return WriteFlow.Run(ctx, "HZ_CAD_STYLES",
            (doc, tr, plan) =>
            {
                var db = doc.Database;
                switch (kind)
                {
                    case "text": id = Cad.Symbol(tr, db.TextStyleTableId, name, "text style"); break;
                    case "dim": id = Cad.Symbol(tr, db.DimStyleTableId, name, "dimension style"); break;
                    case "mleader":
                        var dict = (DBDictionary)tr.GetObject(db.MLeaderStyleDictionaryId, OpenMode.ForRead);
                        if (!dict.Contains(name)) throw new HzRefusal(ErrorCodes.NotFound, "No multileader style '" + name + "'. Nothing changed.");
                        id = dict.GetAt(name); break;
                    default:
                        if (!db.ObjectContextManager.GetContextCollection(Scales).HasContext(name))
                            throw new HzRefusal(ErrorCodes.NotFound, "No annotation scale '" + name + "'. Add it with scale_add. Nothing changed.");
                        break;
                }
                plan["kind"] = kind; plan["name"] = name;
            },
            (doc, tr) =>
            {
                var db = doc.Database;
                switch (kind)
                {
                    case "text": db.Textstyle = id; break;
                    case "dim": db.Dimstyle = id; db.SetDimstyleData((DimStyleTableRecord)tr.GetObject(id, OpenMode.ForRead)); break;
                    case "mleader": db.MLeaderstyle = id; break;
                    default: db.Cannoscale = (AnnotationScale)db.ObjectContextManager.GetContextCollection(Scales).GetContext(name); break;
                }
            },
            (doc, tr, v, after) =>
            {
                var db = doc.Database;
                switch (kind)
                {
                    case "text": v.Flag("current text style", true, db.Textstyle == id); break;
                    case "dim": v.Flag("current dimension style", true, db.Dimstyle == id); break;
                    case "mleader": v.Flag("current multileader style", true, db.MLeaderstyle == id); break;
                    default: v.Text("current annotation scale", name, db.Cannoscale?.Name); break;
                }
                after["current"] = name;
            });
    }

    private static CommandResult ScaleAdd(CommandContext ctx)
    {
        var name = Hz.Str(ctx.Args, "new_name")!;
        var paper = Hz.Num(ctx.Args, "paper_units")!.Value;
        var drawing = Hz.Num(ctx.Args, "drawing_units")!.Value;
        return WriteFlow.Run(ctx, "HZ_CAD_STYLES",
            (doc, tr, plan) =>
            {
                if (doc.Database.ObjectContextManager.GetContextCollection(Scales).HasContext(name))
                    throw new HzRefusal(ErrorCodes.InvalidInput, "Annotation scale '" + name + "' exists. Nothing changed.");
                plan["name"] = name; plan["paper_units"] = paper; plan["drawing_units"] = drawing; plan["scale"] = paper / drawing;
            },
            (doc, tr) => doc.Database.ObjectContextManager.GetContextCollection(Scales).AddContext(new AnnotationScale { Name = name, PaperUnits = paper, DrawingUnits = drawing }),
            (doc, tr, v, after) =>
            {
                var c = doc.Database.ObjectContextManager.GetContextCollection(Scales);
                v.Flag("scale exists", true, c.HasContext(name));
                if (c.GetContext(name) is AnnotationScale s)
                {
                    v.Number("paper units", paper, s.PaperUnits, 1e-12);
                    v.Number("drawing units", drawing, s.DrawingUnits, 1e-12);
                }
                after["scale"] = name;
            });
    }

    private static CommandResult LinetypeLoad(CommandContext ctx)
    {
        var names = Resolve.Strings(ctx.Args["names"]);
        var file = Hz.Str(ctx.Args, "file") ?? "acadiso.lin";
        var toLoad = new List<string>();
        return WriteFlow.Run(ctx, "HZ_CAD_STYLES",
            (doc, tr, plan) =>
            {
                var db = doc.Database;
                var path = Path.IsPathRooted(file) ? file : HostApplicationServices.Current.FindFile(file, db, FindFileHint.Default);
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) throw new HzRefusal(ErrorCodes.NotFound, "Linetype file '" + file + "' was not found. Nothing changed.");
                var defined = File.ReadLines(path).Where(l => l.StartsWith("*")).Select(l => l.Substring(1).Split(',')[0].Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var lt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
                var already = new List<string>();
                foreach (var n in names)
                {
                    if (lt.Has(n)) { already.Add(n); continue; }
                    if (!defined.Contains(n)) throw new HzRefusal(ErrorCodes.NotFound, "Linetype '" + n + "' is not defined in " + Path.GetFileName(path) + ". Nothing changed.");
                    toLoad.Add(n);
                }
                if (toLoad.Count == 0) throw new HzRefusal(ErrorCodes.InvalidInput, "Every requested linetype is already loaded: " + string.Join(", ", already) + ". Nothing to do.");
                plan["file"] = path; plan["load"] = Hz.Strings(toLoad); plan["already_loaded"] = Hz.Strings(already);
            },
            (doc, tr) => { foreach (var n in toLoad) doc.Database.LoadLineTypeFile(n, file); },
            (doc, tr, v, after) =>
            {
                var lt = (LinetypeTable)tr.GetObject(doc.Database.LinetypeTableId, OpenMode.ForRead);
                foreach (var n in toLoad) v.Flag(n + " loaded", true, lt.Has(n));
                after["loaded"] = Hz.Strings(toLoad);
            });
    }
}
