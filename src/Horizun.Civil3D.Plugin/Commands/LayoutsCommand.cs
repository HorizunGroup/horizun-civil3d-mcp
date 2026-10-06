// -----------------------------------------------------------------------------
// horizun_c3d_layouts - layouts, viewports, page setups and PDF (block C).
//
// plot_pdf plots in the foreground (BACKGROUNDPLOT forced to 0 and restored)
// through the publish engine, then verifies the file on disk: it exists, starts
// with %PDF and holds one page per requested layout.
// -----------------------------------------------------------------------------
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.PlottingServices;
using Horizun.Civil3D.Core;
using Horizun.Civil3D.Plugin.Civil;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace Horizun.Civil3D.Plugin.Commands;

internal sealed partial class LayoutsCommand : ICommand
{
    public string Name => "layouts";
    private const string PdfDevice = "DWG To PDF.pc3";

    public CommandResult Execute(CommandContext ctx)
    {
        if (ToolRules.Validate(ctx.Tool.Name, ctx.Args) is { } why) throw new HzRefusal(ErrorCodes.InvalidInput, why + " Nothing ran.");
        return ctx.Action switch
        {
            "list" => WriteFlow.Read(ctx, List),
            "devices" => WriteFlow.Read(ctx, (doc, tr, data) => Devices(ctx, data)),
            "create" => Create(ctx),
            "rename" => Rename(ctx),
            "delete" => Delete(ctx),
            "viewport" => ViewportCreate(ctx),
            "alignment_viewport" => AlignmentViewport(ctx, false),
            "refresh_alignment_viewport" => AlignmentViewport(ctx, true),
            "page_setup" => PageSetup(ctx),
            _ => PlotPdf(ctx),
        };
    }

    // ---- helpers -----------------------------------------------------------------

    private static LayoutManager Lm(Document doc)
    {
        if (HostApplicationServices.WorkingDatabase != doc.Database)
            throw new HzRefusal(ErrorCodes.DocumentMismatch, "The target drawing is not the working database. Activate it in Civil 3D first. Nothing changed.");
        return LayoutManager.Current;
    }

    private static ObjectId LayoutId(Document doc, Transaction tr, string name, bool allowModel = false)
    {
        var dict = (DBDictionary)tr.GetObject(doc.Database.LayoutDictionaryId, OpenMode.ForRead);
        foreach (DBDictionaryEntry e in dict)
            if (e.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                if (!allowModel && ((Layout)tr.GetObject(e.Value, OpenMode.ForRead)).ModelType)
                    throw new HzRefusal(ErrorCodes.InvalidInput, "'" + name + "' is model space, not a paper layout. Nothing changed.");
                return e.Value;
            }
        throw new HzRefusal(ErrorCodes.NotFound, "No layout '" + name + "'. Nothing changed.", new JsonObject { ["candidates"] = Hz.Strings(Cad.Entries(dict).Select(x => x.Key)) });
    }

    private static JsonObject VpJson(Viewport v) => new()
    {
        ["handle"] = v.Handle.ToString(), ["number"] = v.Number, ["center"] = Resolve.Json(v.CenterPoint, false), ["width"] = Hz.Finite(v.Width, 6),
        ["height"] = Hz.Finite(v.Height, 6), ["view_center"] = Resolve.Json(v.ViewCenter), ["scale"] = Hz.Finite(v.CustomScale, 12),
        ["scale_text"] = v.CustomScale > 0 ? "1:" + Math.Round(1 / v.CustomScale, 4) : null, ["locked"] = v.Locked, ["on"] = v.On, ["layer"] = v.Layer,
    };

    private static JsonObject LayoutJson(Layout l, Transaction tr)
    {
        var vps = new JsonArray();
        var btr = (BlockTableRecord)tr.GetObject(l.BlockTableRecordId, OpenMode.ForRead);
        foreach (ObjectId id in btr)
            if (id.ObjectClass.DxfName == "VIEWPORT" && tr.GetObject(id, OpenMode.ForRead) is Viewport v && v.Number != 1)
                vps.Add(VpJson(v));
        return new JsonObject
        {
            ["name"] = l.LayoutName, ["tab_order"] = l.TabOrder, ["model"] = l.ModelType, ["device"] = l.PlotConfigurationName,
            ["media"] = l.CanonicalMediaName, ["plot_style"] = l.CurrentStyleSheet, ["plot_type"] = l.PlotType.ToString(),
            ["paper_size_mm"] = new JsonObject { ["width"] = Hz.Finite(l.PlotPaperSize.X, 3), ["height"] = Hz.Finite(l.PlotPaperSize.Y, 3) },
            ["viewports"] = vps,
        };
    }

    private static void List(Document doc, Transaction tr, JsonObject data)
    {
        var rows = new JsonArray();
        var dict = (DBDictionary)tr.GetObject(doc.Database.LayoutDictionaryId, OpenMode.ForRead);
        foreach (var l in Cad.Entries(dict).Select(e => (Layout)tr.GetObject(e.Value, OpenMode.ForRead)).OrderBy(l => l.TabOrder))
            rows.Add(LayoutJson(l, tr));
        data["layouts"] = rows;
        data["current"] = HostApplicationServices.WorkingDatabase == doc.Database ? LayoutManager.Current.CurrentLayout : null;
    }

    private static List<string> MediaFor(string device)
    {
        var psv = PlotSettingsValidator.Current;
        using var ps = new PlotSettings(false);
        psv.SetPlotConfigurationName(ps, device, null);
        psv.RefreshLists(ps);
        return psv.GetCanonicalMediaNameList(ps).Cast<string>().ToList();
    }

    private static void Devices(CommandContext ctx, JsonObject data)
    {
        var psv = PlotSettingsValidator.Current;
        var devices = psv.GetPlotDeviceList().Cast<string>().ToList();
        data["devices"] = Hz.Strings(devices);
        data["plot_styles"] = Hz.Strings(psv.GetPlotStyleSheetList().Cast<string>());
        if (Hz.Str(ctx.Args, "device") is { } d)
        {
            if (!devices.Contains(d, StringComparer.OrdinalIgnoreCase)) throw new HzRefusal(ErrorCodes.NotFound, "No plot device '" + d + "'.");
            using var ps = new PlotSettings(false);
            psv.SetPlotConfigurationName(ps, d, null);
            psv.RefreshLists(ps);
            data["media"] = new JsonArray(psv.GetCanonicalMediaNameList(ps).Cast<string>().Select(m => (JsonNode)new JsonObject
            {
                ["canonical"] = m, ["name"] = psv.GetLocaleMediaName(ps, m),
            }).ToArray());
        }
    }

    private static string DeviceName(string device)
    {
        var list = PlotSettingsValidator.Current.GetPlotDeviceList().Cast<string>().ToList();
        return list.FirstOrDefault(x => x.Equals(device, StringComparison.OrdinalIgnoreCase))
               ?? throw new HzRefusal(ErrorCodes.NotFound, "No plot device '" + device + "'. Nothing changed.", new JsonObject { ["devices"] = Hz.Strings(list) });
    }

    private static string MediaName(string device, string media)
    {
        var psv = PlotSettingsValidator.Current;
        using var ps = new PlotSettings(false);
        psv.SetPlotConfigurationName(ps, device, null);
        psv.RefreshLists(ps);
        foreach (var m in psv.GetCanonicalMediaNameList(ps).Cast<string?>().OfType<string>())
            if (m.Equals(media, StringComparison.OrdinalIgnoreCase) || string.Equals(psv.GetLocaleMediaName(ps, m), media, StringComparison.OrdinalIgnoreCase)) return m;
        throw new HzRefusal(ErrorCodes.NotFound, "Device '" + device + "' has no media '" + media + "'. Use devices with device to list them. Nothing changed.");
    }

    // ---- create / rename / delete ---------------------------------------------------

    private static CommandResult Create(CommandContext ctx)
    {
        var name = Hz.Str(ctx.Args, "new_name")!;
        var copy = Hz.Str(ctx.Args, "copy_from");
        var tdwg = Hz.Str(ctx.Args, "template_dwg");
        var tlay = Hz.Str(ctx.Args, "template_layout");
        var id = ObjectId.Null;
        var expectedEntities = -1;
        return WriteFlow.Run(ctx, "HZ_LAYOUTS",
            (doc, tr, plan) =>
            {
                var lm = Lm(doc);
                if (lm.LayoutExists(name)) throw new HzRefusal(ErrorCodes.InvalidInput, "Layout '" + name + "' exists. Nothing changed.");
                SymbolUtilityServices.ValidateSymbolName(name, false);
                if (copy != null) LayoutId(doc, tr, copy);
                if (tdwg != null)
                {
                    if (!Path.IsPathRooted(tdwg) || !File.Exists(tdwg)) throw new HzRefusal(ErrorCodes.NotFound, "template_dwg must be an existing absolute path. Nothing changed.");
                    using var src = new Database(false, true);
                    src.ReadDwgFile(tdwg, FileOpenMode.OpenForReadAndAllShare, true, null);
                    using var str = src.TransactionManager.StartTransaction();
                    var d = (DBDictionary)str.GetObject(src.LayoutDictionaryId, OpenMode.ForRead);
                    if (!d.Contains(tlay!)) throw new HzRefusal(ErrorCodes.NotFound, Path.GetFileName(tdwg) + " has no layout '" + tlay + "'. Nothing changed.", new JsonObject { ["candidates"] = Hz.Strings(Cad.Entries(d).Select(x => x.Key)) });
                    var sl = (Layout)str.GetObject(d.GetAt(tlay!), OpenMode.ForRead);
                    expectedEntities = ((BlockTableRecord)str.GetObject(sl.BlockTableRecordId, OpenMode.ForRead)).Cast<ObjectId>().Count();
                    str.Commit();
                }
                plan["new_name"] = name; plan["source"] = copy != null ? "copy of " + copy : tdwg != null ? tlay + " from " + Path.GetFileName(tdwg) : "empty layout";
            },
            (doc, tr) =>
            {
                var lm = Lm(doc);
                if (copy != null) { lm.CopyLayout(copy, name); id = lm.GetLayoutId(name); return; }
                id = lm.CreateLayout(name);
                if (tdwg == null) return;
                using var src = new Database(false, true);
                src.ReadDwgFile(tdwg, FileOpenMode.OpenForReadAndAllShare, true, null);
                var ids = new ObjectIdCollection();
                Layout? srcLayout;
                using (var str = src.TransactionManager.StartTransaction())
                {
                    var d = (DBDictionary)str.GetObject(src.LayoutDictionaryId, OpenMode.ForRead);
                    srcLayout = (Layout)str.GetObject(d.GetAt(tlay!), OpenMode.ForRead);
                    foreach (ObjectId eid in (BlockTableRecord)str.GetObject(srcLayout.BlockTableRecordId, OpenMode.ForRead)) ids.Add(eid);
                    var nl = (Layout)tr.GetObject(id, OpenMode.ForWrite);
                    nl.CopyFrom(srcLayout);
                    nl.LayoutName = name;
                    str.Commit();
                }
                var target = ((Layout)tr.GetObject(id, OpenMode.ForRead)).BlockTableRecordId;
                if (ids.Count > 0) src.WblockCloneObjects(ids, target, new IdMapping(), DuplicateRecordCloning.Ignore, false);
            },
            (doc, tr, v, after) =>
            {
                var l = (Layout)tr.GetObject(id, OpenMode.ForRead);
                v.Text("layout name", name, l.LayoutName);
                if (expectedEntities >= 0)
                {
                    var n = ((BlockTableRecord)tr.GetObject(l.BlockTableRecordId, OpenMode.ForRead)).Cast<ObjectId>().Count();
                    v.Check("template entities copied", expectedEntities, n, n >= expectedEntities);
                }
                after["layout"] = LayoutJson(l, tr);
            });
    }

    private static CommandResult Rename(CommandContext ctx)
    {
        var name = Hz.Str(ctx.Args, "name")!;
        var newName = Hz.Str(ctx.Args, "new_name")!;
        var id = ObjectId.Null;
        return WriteFlow.Run(ctx, "HZ_LAYOUTS",
            (doc, tr, plan) =>
            {
                id = LayoutId(doc, tr, name);
                if (Lm(doc).LayoutExists(newName) && !newName.Equals(name, StringComparison.OrdinalIgnoreCase)) throw new HzRefusal(ErrorCodes.InvalidInput, "Layout '" + newName + "' exists. Nothing changed.");
                plan["name"] = name; plan["new_name"] = newName;
            },
            (doc, tr) => Lm(doc).RenameLayout(((Layout)tr.GetObject(id, OpenMode.ForRead)).LayoutName, newName),
            (doc, tr, v, after) => { v.Text("layout name", newName, ((Layout)tr.GetObject(id, OpenMode.ForRead)).LayoutName); after["name"] = newName; });
    }

    private static CommandResult Delete(CommandContext ctx)
    {
        var name = Hz.Str(ctx.Args, "name")!;
        var real = name;
        return WriteFlow.Run(ctx, "HZ_LAYOUTS",
            (doc, tr, plan) =>
            {
                var id = LayoutId(doc, tr, name);
                var l = (Layout)tr.GetObject(id, OpenMode.ForRead);
                real = l.LayoutName;
                plan["delete"] = LayoutJson(l, tr);
                plan["entities_lost"] = ((BlockTableRecord)tr.GetObject(l.BlockTableRecordId, OpenMode.ForRead)).Cast<ObjectId>().Count();
            },
            (doc, tr) => Lm(doc).DeleteLayout(real),
            (doc, tr, v, after) => { v.Flag("layout removed", false, Lm(doc).LayoutExists(real)); after["deleted"] = real; });
    }

    // ---- viewport ---------------------------------------------------------------------

    private static CommandResult ViewportCreate(CommandContext ctx)
    {
        var c = Resolve.P(ctx.Args["center"]);
        var w = Hz.Num(ctx.Args, "width")!.Value;
        var h = Hz.Num(ctx.Args, "height")!.Value;
        var vcP = Resolve.P(ctx.Args["view_center"]);
        var vc = new Point2d(vcP.X, vcP.Y);
        var scale = Hz.Num(ctx.Args, "scale")!.Value;
        var locked = Hz.Bool(ctx.Args, "locked") ?? true;
        var frozen = new List<ObjectId>();
        ObjectId layoutId = ObjectId.Null, layerId = ObjectId.Null, id = ObjectId.Null;
        string? onNote = null;
        return WriteFlow.Run(ctx, "HZ_LAYOUTS",
            (doc, tr, plan) =>
            {
                layoutId = LayoutId(doc, tr, Hz.Str(ctx.Args, "layout")!);
                foreach (var n in Resolve.Strings(ctx.Args["frozen_layers"])) frozen.Add(Cad.Symbol(tr, doc.Database.LayerTableId, n, "layer"));
                layerId = Resolve.Layer(doc.Database, tr, Hz.Str(ctx.Args, "layer"));
                var l = (Layout)tr.GetObject(layoutId, OpenMode.ForRead);
                plan["layout"] = l.LayoutName; plan["center"] = Resolve.Json(c, false); plan["width"] = w; plan["height"] = h;
                plan["view_center"] = Resolve.Json(vc); plan["scale"] = scale; plan["scale_text"] = "1:" + Math.Round(1 / scale, 4);
                plan["model_window"] = new JsonObject { ["width"] = Hz.Finite(w / scale, 4), ["height"] = Hz.Finite(h / scale, 4) };
                plan["locked"] = locked; plan["frozen_layers"] = frozen.Count;
            },
            (doc, tr) =>
            {
                var l = (Layout)tr.GetObject(layoutId, OpenMode.ForWrite);
                if (l.GetViewports().Count == 0) l.Initialize();
                var btr = (BlockTableRecord)tr.GetObject(l.BlockTableRecordId, OpenMode.ForWrite);
                var vp = new Viewport();
                vp.SetDatabaseDefaults(doc.Database);
                vp.CenterPoint = new Point3d(c.X, c.Y, 0);
                vp.Width = w; vp.Height = h;
                vp.LayerId = layerId;
                id = btr.AppendEntity(vp);
                tr.AddNewlyCreatedDBObject(vp, true);
                vp.ViewDirection = Vector3d.ZAxis;
                vp.ViewTarget = Point3d.Origin;
                vp.ViewCenter = vc;
                vp.CustomScale = scale;
                try { vp.On = true; } catch (Autodesk.AutoCAD.Runtime.Exception e) { onNote = "On could not be set now (" + e.Message + "); it turns on when the layout is activated."; }
                if (frozen.Count > 0) vp.FreezeLayersInViewport(frozen.GetEnumerator());
                vp.Locked = locked;
            },
            (doc, tr, v, after) =>
            {
                var vp = (Viewport)tr.GetObject(id, OpenMode.ForRead);
                v.Check("center", Resolve.Json(c, false), Resolve.Json(vp.CenterPoint, false), Math.Abs(vp.CenterPoint.X - c.X) <= 1e-9 && Math.Abs(vp.CenterPoint.Y - c.Y) <= 1e-9);
                v.Number("width", w, vp.Width, 1e-9);
                v.Number("height", h, vp.Height, 1e-9);
                v.Check("view center", Resolve.Json(vc), Resolve.Json(vp.ViewCenter), vp.ViewCenter.GetDistanceTo(vc) <= 1e-6);
                v.Number("scale", scale, vp.CustomScale, 1e-12);
                v.Flag("locked", locked, vp.Locked);
                foreach (var f in frozen) v.Flag("layer frozen in viewport", true, vp.IsLayerFrozenInViewport(f));
                after["viewport"] = VpJson(vp);
                if (onNote != null) after["note"] = onNote;
            });
    }

    // ---- page setup ----------------------------------------------------------------

    private static CommandResult PageSetup(CommandContext ctx)
    {
        var area = Hz.Str(ctx.Args, "area") ?? "layout";
        var fit = Hz.Bool(ctx.Args, "fit");
        var scale = Hz.Num(ctx.Args, "scale");
        var centered = Hz.Bool(ctx.Args, "centered");
        var rot = Hz.Str(ctx.Args, "rotation");
        string device = "", media = "", style = "";
        var id = ObjectId.Null;
        return WriteFlow.Run(ctx, "HZ_LAYOUTS",
            (doc, tr, plan) =>
            {
                id = LayoutId(doc, tr, Hz.Str(ctx.Args, "layout")!);
                device = DeviceName(Hz.Str(ctx.Args, "device")!);
                if (Hz.Str(ctx.Args, "media") is { } m) media = MediaName(device, m);
                if (Hz.Str(ctx.Args, "plot_style") is { } s)
                {
                    var list = PlotSettingsValidator.Current.GetPlotStyleSheetList().Cast<string>().ToList();
                    style = list.FirstOrDefault(x => x.Equals(s, StringComparison.OrdinalIgnoreCase)) ?? throw new HzRefusal(ErrorCodes.NotFound, "No plot style table '" + s + "'. Nothing changed.", new JsonObject { ["plot_styles"] = Hz.Strings(list) });
                }
                if (area == "display" && fit != true && scale == null) plan["note"] = "Display area plots what the layout last showed.";
                plan["layout"] = ((Layout)tr.GetObject(id, OpenMode.ForRead)).LayoutName; plan["device"] = device; plan["media"] = media.Length > 0 ? media : "(device default)";
                plan["plot_style"] = style.Length > 0 ? style : null; plan["area"] = area; plan["fit"] = fit; plan["scale"] = scale; plan["rotation"] = rot;
            },
            (doc, tr) =>
            {
                var l = (Layout)tr.GetObject(id, OpenMode.ForWrite);
                var psv = PlotSettingsValidator.Current;
                using var ps = new PlotSettings(l.ModelType);
                ps.CopyFrom(l);
                psv.SetPlotConfigurationName(ps, device, media.Length > 0 ? media : null);
                psv.RefreshLists(ps);
                psv.SetPlotType(ps, area switch { "extents" => Autodesk.AutoCAD.DatabaseServices.PlotType.Extents, "display" => Autodesk.AutoCAD.DatabaseServices.PlotType.Display, _ => Autodesk.AutoCAD.DatabaseServices.PlotType.Layout });
                if (fit == true) { psv.SetUseStandardScale(ps, true); psv.SetStdScaleType(ps, StdScaleType.ScaleToFit); }
                else if (scale is { } sc) { psv.SetUseStandardScale(ps, false); psv.SetCustomPrintScale(ps, new CustomScale(sc, 1)); }
                if (centered is { } ce) psv.SetPlotCentered(ps, ce);
                if (rot != null) psv.SetPlotRotation(ps, rot switch { "90" => PlotRotation.Degrees090, "180" => PlotRotation.Degrees180, "270" => PlotRotation.Degrees270, _ => PlotRotation.Degrees000 });
                if (style.Length > 0) psv.SetCurrentStyleSheet(ps, style);
                l.CopyFrom(ps);
            },
            (doc, tr, v, after) =>
            {
                var l = (Layout)tr.GetObject(id, OpenMode.ForRead);
                v.Text("device", device, l.PlotConfigurationName);
                if (media.Length > 0) v.Text("media", media, l.CanonicalMediaName);
                if (style.Length > 0) v.Text("plot style", style, l.CurrentStyleSheet);
                v.Text("plot area", area, l.PlotType.ToString());
                if (fit == true) v.Flag("scale to fit", true, l.UseStandardScale && l.StdScaleType == StdScaleType.ScaleToFit);
                if (scale is { } sc) v.Number("custom scale", sc, l.CustomPrintScale.Numerator / l.CustomPrintScale.Denominator, 1e-9);
                if (centered is { } ce) v.Flag("centered", ce, l.PlotCentered);
                if (rot != null) v.Text("rotation", rot, l.PlotRotation switch { PlotRotation.Degrees090 => "90", PlotRotation.Degrees180 => "180", PlotRotation.Degrees270 => "270", _ => "0" });
                after["layout"] = LayoutJson(l, tr);
            });
    }

    // ---- PDF ---------------------------------------------------------------------------

    internal static int PdfPages(string path)
    {
        var text = Encoding.GetEncoding(28591).GetString(File.ReadAllBytes(path));
        return Regex.Matches(text, @"/Type\s*/Page(?![a-zA-Z])").Count;
    }

    private static bool PdfLooksComplete(string path, int expectedPages)
    {
        using var fs = File.OpenRead(path);
        if (fs.Length < 5) return false;
        var header = new byte[5];
        RuntimeCompat.ReadExactly(fs, header, 0, header.Length);
        return Encoding.ASCII.GetString(header) == "%PDF-" && PdfPages(path) == expectedPages;
    }

    private static CommandResult PlotPdf(CommandContext ctx)
    {
        var names = Resolve.Strings(ctx.Args["layouts"]);
        var output = Hz.Str(ctx.Args, "output")!;
        var overwrite = Hz.Bool(ctx.Args, "overwrite") ?? false;
        var device = "";
        var ids = new List<ObjectId>();
        var notes = new JsonArray();
        AtomicOutput.Result? promoted = null;
        AtomicOutput.DestinationState? previousOutput = null;
        return WriteFlow.Run(ctx, "HZ_LAYOUTS",
            (doc, tr, plan) =>
            {
                Lm(doc);
                if (PlotFactory.ProcessPlotState != ProcessPlotState.NotPlotting) throw new HzRefusal(ErrorCodes.Busy, "Civil 3D is already plotting. Nothing ran.");
                device = DeviceName(Hz.Str(ctx.Args, "device") ?? PdfDevice);
                if (!device.EndsWith(".pc3", StringComparison.OrdinalIgnoreCase) || !device.Contains("PDF", StringComparison.OrdinalIgnoreCase))
                    throw new HzRefusal(ErrorCodes.InvalidInput, "plot_pdf needs a PDF device (e.g. DWG To PDF.pc3); got '" + device + "'. Nothing ran.");
                var dir = Path.GetDirectoryName(output);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) throw new HzRefusal(ErrorCodes.NotFound, "The output folder does not exist: " + dir + ". Nothing ran.");
                if (File.Exists(output) && !overwrite) throw new HzRefusal(ErrorCodes.InvalidInput, "The output file exists; pass overwrite=true to replace it. Nothing ran.");
                previousOutput = AtomicOutput.Capture(output);
                foreach (var n in names) ids.Add(LayoutId(doc, tr, n));
                plan["layouts"] = Hz.Strings(names); plan["device"] = device; plan["output"] = output; plan["overwrite"] = overwrite && previousOutput.Exists;
                plan["previous_output_sha256"] = previousOutput.Sha256;
                plan["note"] = "Each layout keeps its page setup (area, scale, plot style); only the device is switched to the PDF device, with the closest media. A verified PDF replaces an existing output atomically, preserving the old file as a named backup.";
            },
            (doc, tr) =>
            {
                var lm = Lm(doc);
                var original = lm.CurrentLayout;
                var bg = AcApp.GetSystemVariable("BACKGROUNDPLOT");
                AcApp.SetSystemVariable("BACKGROUNDPLOT", (short)0);
                try
                {
                    promoted = AtomicOutput.Write(output, overwrite, stage =>
                    {
                    var psv = PlotSettingsValidator.Current;
                    using var pe = PlotFactory.CreatePublishEngine();
                    using var progress = new PlotProgressDialog(false, ids.Count, true);
                    progress.OnBeginPlot();
                    progress.IsVisible = false;
                    pe.BeginPlot(progress, null);
                    for (var i = 0; i < ids.Count; i++)
                    {
                        var l = (Layout)tr.GetObject(ids[i], OpenMode.ForRead);
                        lm.CurrentLayout = l.LayoutName;
                        var ps = new PlotSettings(l.ModelType);
                        ps.CopyFrom(l);
                        var keepMedia = MediaFor(device).Contains(l.CanonicalMediaName);
                        psv.SetPlotConfigurationName(ps, device, keepMedia ? l.CanonicalMediaName : null);
                        psv.RefreshLists(ps);
                        if (!keepMedia)
                        {
                            psv.SetClosestMediaName(ps, l.PlotPaperSize.X, l.PlotPaperSize.Y, PlotPaperUnit.Millimeters, true);
                            notes.Add(new JsonObject { ["layout"] = l.LayoutName, ["media"] = ps.CanonicalMediaName, ["note"] = "closest PDF media to the layout paper size" });
                        }
                        var pi = new PlotInfo { Layout = ids[i], OverrideSettings = ps };
                        new PlotInfoValidator { MediaMatchingPolicy = MatchingPolicy.MatchEnabled }.Validate(pi);
                        if (i == 0) pe.BeginDocument(pi, doc.Name, null, 1, true, stage);
                        progress.OnBeginSheet();
                        pe.BeginPage(new PlotPageInfo(), pi, i == ids.Count - 1, null);
                        pe.BeginGenerateGraphics(null);
                        pe.EndGenerateGraphics(null);
                        pe.EndPage(null);
                        progress.OnEndSheet();
                    }
                    pe.EndDocument(null);
                    pe.EndPlot(null);
                    progress.OnEndPlot();
                    }, stage => PdfLooksComplete(stage, ids.Count), previousOutput);
                }
                finally
                {
                    AcApp.SetSystemVariable("BACKGROUNDPLOT", bg);
                    try { lm.CurrentLayout = original; } catch (Autodesk.AutoCAD.Runtime.Exception) { }
                }
            },
            (doc, tr, v, after) =>
            {
                after["previous_file_backup"] = promoted?.BackupPath;
                var exists = File.Exists(output);
                v.Flag("PDF written", true, exists);
                if (!exists) return;
                var fi = new FileInfo(output);
                var head = new byte[5];
                using (var fs = File.OpenRead(output)) RuntimeCompat.ReadExactly(fs, head, 0, Math.Min(5, (int)fs.Length));
                v.Text("PDF header", "%PDF-", Encoding.ASCII.GetString(head), false);
                var pages = PdfPages(output);
                v.Check("pages", ids.Count, pages, pages == ids.Count, pages == 0 ? "No /Type /Page found (compressed object streams?); open the file to check." : null);
                after["file"] = new JsonObject { ["path"] = output, ["bytes"] = fi.Length, ["pages"] = pages };
                if (notes.Count > 0) after["media_notes"] = notes;
            });
    }
}
