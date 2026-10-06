// -----------------------------------------------------------------------------
// horizun_c3d_exchange - data shortcuts and our own LandXML 1.2 export (block D).
// API: docs/api-probes/2025/AeccDbMgd.phase3-datashortcuts.txt.
//
// AeccDataShortcutMgd.dll is not always loaded when the plug-in starts, so every
// use goes through Shortcuts (NoInlining) after EnsureLoaded(); the dispatcher
// never touches those types at startup.
// -----------------------------------------------------------------------------
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.DataShortcuts;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.Settings;
using Horizun.Civil3D.Core;
using Horizun.Civil3D.Plugin.Civil;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace Horizun.Civil3D.Plugin.Commands;

internal sealed class ExchangeCommand : ICommand
{
    public string Name => "exchange";

    public CommandResult Execute(CommandContext ctx)
    {
        if (ToolRules.Validate(ctx.Tool.Name, ctx.Args) is { } why) throw new HzRefusal(ErrorCodes.InvalidInput, why + " Nothing ran.");
        if (ctx.Action?.StartsWith("shortcuts_") == true) Shortcuts.EnsureLoaded();
        return ctx.Action switch
        {
            "shortcuts_status" => WriteFlow.Read(ctx, (doc, tr, data) => Shortcuts.Status(doc, data)),
            "shortcuts_publish" => Shortcuts.Publish(ctx),
            "shortcuts_project" => Shortcuts.Project(ctx),
            "shortcuts_reference" => Shortcuts.Reference(ctx),
            "export_dwg" => DwgExport.Run(ctx),
            "export_revit" => RevitTerrainExport.Run(ctx),
            _ => ExportLandXml(ctx),
        };
    }

    // ---- LandXML -------------------------------------------------------------------

    private static LxSurface SurfaceModel(TinSurface s)
    {
        var index = new Dictionary<(long, long), int>();
        var pts = new List<(double, double, double)>();
        static (long, long) Key(double x, double y) => ((long)Math.Round(x * 1e6), (long)Math.Round(y * 1e6));
        foreach (TinSurfaceVertex v in s.Vertices)
        {
            var k = Key(v.Location.X, v.Location.Y);
            if (index.ContainsKey(k)) continue;
            index[k] = pts.Count;
            pts.Add((v.Location.X, v.Location.Y, v.Location.Z));
        }
        var faces = new List<(int, int, int)>();
        foreach (TinSurfaceTriangle t in s.GetTriangles(false))
        {
            if (!t.IsVisible) continue;
            int I(TinSurfaceVertex v) => index.TryGetValue(Key(v.Location.X, v.Location.Y), out var i) ? i
                : throw new HzRefusal(ErrorCodes.Internal, "A triangle vertex of '" + s.Name + "' is not in the vertex list. Nothing written.");
            faces.Add((I(t.Vertex1), I(t.Vertex2), I(t.Vertex3)));
        }
        return new LxSurface(s.Name, s.Description ?? "", pts, faces);
    }

    private static LxAlignment AlignmentModel(Alignment al, Transaction tr, bool profiles, JsonArray skipped)
    {
        var elems = new List<LxElement>();
        for (var i = 0; i < al.Entities.Count; i++)
        {
            switch (al.Entities.GetEntityByOrder(i))
            {
                case AlignmentLine l:
                    elems.Add(new LxElement("Line", l.StartStation, l.Length, (l.StartPoint.X, l.StartPoint.Y), (l.EndPoint.X, l.EndPoint.Y)));
                    break;
                case AlignmentArc a:
                    elems.Add(new LxElement("Curve", a.StartStation, a.Length, (a.StartPoint.X, a.StartPoint.Y), (a.EndPoint.X, a.EndPoint.Y),
                        (a.CenterPoint.X, a.CenterPoint.Y), a.Radius, a.Clockwise));
                    break;
                case var e:
                    throw new HzRefusal(ErrorCodes.Unsupported, "Alignment '" + al.Name + "' has a " + e.EntityType + " entity; this export writes tangents and arcs only (spirals not yet). Nothing written.");
            }
        }
        var profs = new List<LxProfile>();
        if (profiles)
            foreach (ObjectId pid in al.GetProfileIds())
            {
                var p = (Profile)tr.GetObject(pid, OpenMode.ForRead);
                if (p.ProfileType != ProfileType.FG) { skipped.Add(new JsonObject { ["profile"] = p.Name, ["alignment"] = al.Name, ["reason"] = p.ProfileType + " profile (only layout profiles are exported)" }); continue; }
                var pvis = new List<LxPvi>();
                for (var i = 0; i < p.PVIs.Count; i++) { var v = p.PVIs[i]; pvis.Add(new LxPvi(v.RawStation, v.Elevation, v.VerticalCurve?.Length ?? 0)); }
                profs.Add(new LxProfile(p.Name, pvis));
            }
        return new LxAlignment(al.Name, al.Description ?? "", al.StartingStation, al.Length, elems, profs);
    }

    private static CommandResult ExportLandXml(CommandContext ctx)
    {
        var output = Hz.Str(ctx.Args, "output")!;
        var withProfiles = Hz.Bool(ctx.Args, "include_profiles") ?? true;
        var model = new LandXmlModel { AppVersion = App.PluginVersion };
        var skipped = new JsonArray();
        bool ValidFile(string path)
        {
            var sum = LandXmlSummary.Read(File.ReadAllText(path));
            return model.Surfaces.All(s => sum.Surfaces.TryGetValue(s.Name, out var got) &&
                       got.Points == s.Points.Count && got.Faces == s.Faces.Count)
                   && model.Alignments.All(a => sum.Alignments.TryGetValue(a.Name, out var got) &&
                       Math.Abs(got.Length - a.Length) <= 1e-6 &&
                       Math.Abs(got.ElementLength - a.Length) <= 1e-4 &&
                       a.Profiles.All(p => got.ProfilePvis != null &&
                           got.ProfilePvis.TryGetValue(p.Name, out var count) && count == p.Pvis.Count));
        }
        return WriteFlow.Run(ctx, "HZ_EXCHANGE",
            (doc, tr, plan) =>
            {
                if (File.Exists(output)) throw new HzRefusal(ErrorCodes.InvalidInput, "The output file exists; this action never overwrites. Nothing written.");
                if (Path.GetDirectoryName(output) is not { } dir || !Directory.Exists(dir)) throw new HzRefusal(ErrorCodes.NotFound, "The output folder does not exist. Nothing written.");
                // Civil design coordinates use the drawing settings. INSUNITS can
                // deliberately differ when MatchAutoCADVariables is disabled.
                var zone = CommandContext.Civil(doc).Settings.DrawingSettings.UnitZoneSettings;
                model.Metric = zone.DrawingUnits switch
                {
                    DrawingUnitType.Meters => true,
                    DrawingUnitType.Feet => false,
                    _ => throw new HzRefusal(ErrorCodes.Unsupported, "Civil drawing units could not be identified. Nothing written."),
                };
                model.ImperialLinearUnit = model.Metric ? "foot" : zone.ImperialToMetricConversion switch
                {
                    ImperialToMetricConversionType.InternationalFoot => "foot",
                    ImperialToMetricConversionType.UsSurveyFoot => "USSurveyFoot",
                    _ => throw new HzRefusal(ErrorCodes.Unsupported, "The Civil foot definition could not be identified. Nothing written."),
                };
                foreach (var n in Resolve.Strings(ctx.Args["surfaces"]))
                {
                    var o = tr.GetObject(Resolve.Named(doc, tr, "surface", n), OpenMode.ForRead);
                    if (o is not TinSurface ts || o is TinVolumeSurface) throw new HzRefusal(ErrorCodes.Unsupported, "Surface '" + n + "' is not a TIN surface. Nothing written.");
                    if (ts.GetGeneralProperties().NumberOfPoints > 3_000_000) throw new HzRefusal(ErrorCodes.InvalidInput, "Surface '" + n + "' has more than 3 million points. Nothing written.");
                    model.Surfaces.Add(SurfaceModel(ts));
                }
                foreach (var n in Resolve.Strings(ctx.Args["alignments"]))
                    model.Alignments.Add(AlignmentModel((Alignment)tr.GetObject(Resolve.Named(doc, tr, "alignment", n), OpenMode.ForRead), tr, withProfiles, skipped));
                plan["output"] = output; plan["units"] = model.Metric ? "meter" : model.ImperialLinearUnit;
                plan["units_source"] = "Civil drawing settings";
                plan["surfaces"] = new JsonArray(model.Surfaces.Select(s => (JsonNode)new JsonObject { ["name"] = s.Name, ["points"] = s.Points.Count, ["faces"] = s.Faces.Count }).ToArray());
                plan["alignments"] = new JsonArray(model.Alignments.Select(a => (JsonNode)new JsonObject { ["name"] = a.Name, ["length"] = Hz.Finite(a.Length, 6), ["elements"] = a.Elements.Count, ["profiles"] = a.Profiles.Count }).ToArray());
                if (skipped.Count > 0) plan["skipped"] = skipped.DeepClone();
            },
            (doc, tr) => AtomicOutput.Write(output, false,
                stage => File.WriteAllText(stage, LandXmlWriter.Write(model, DateTime.Now), new UTF8Encoding(false)), ValidFile),
            (doc, tr, v, after) =>
            {
                v.Flag("file written", true, File.Exists(output));
                if (!File.Exists(output)) return;
                LandXmlSummary sum;
                try { sum = LandXmlSummary.Read(File.ReadAllText(output)); }
                catch (System.Exception e) { v.Check("file parses as LandXML", true, e.Message, false); return; }
                foreach (var s in model.Surfaces)
                {
                    var got = sum.Surfaces.TryGetValue(s.Name, out var g) ? g : default;
                    v.Check(s.Name + " points", s.Points.Count, got.Points, got.Points == s.Points.Count);
                    v.Check(s.Name + " faces", s.Faces.Count, got.Faces, got.Faces == s.Faces.Count);
                }
                foreach (var a in model.Alignments)
                {
                    var got = sum.Alignments.TryGetValue(a.Name, out var g) ? g : default;
                    v.Number(a.Name + " length", a.Length, got.Length, 1e-6);
                    v.Number(a.Name + " sum of element lengths", a.Length, got.ElementLength, 1e-4);
                    foreach (var p in a.Profiles)
                        v.Check(a.Name + "/" + p.Name + " PVIs", p.Pvis.Count, got.ProfilePvis?.GetValueOrDefault(p.Name) ?? 0, (got.ProfilePvis?.GetValueOrDefault(p.Name) ?? 0) == p.Pvis.Count);
                }
                after["file"] = new JsonObject { ["path"] = output, ["bytes"] = new FileInfo(output).Length };
                if (skipped.Count > 0) after["skipped"] = skipped;
            });
    }
}

/// <summary>Everything that touches AeccDataShortcutMgd types (loaded on demand).</summary>
internal static class Shortcuts
{
    public static void EnsureLoaded()
    {
        if (AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "AeccDataShortcutMgd")) return;
        var dir = Path.GetDirectoryName(typeof(Alignment).Assembly.Location)!;
        var path = Path.Combine(dir, "AeccDataShortcutMgd.dll");
        if (!File.Exists(path)) throw new HzRefusal(ErrorCodes.Unsupported, "AeccDataShortcutMgd.dll was not found next to AeccDbMgd.dll. Nothing ran.");
        System.Reflection.Assembly.LoadFrom(path);
    }

    private static DataShortcuts.DataShortcutManager Manager()
    {
        var ok = false;
        var m = DataShortcuts.CreateDataShortcutManager(ref ok);
        if (!ok || m == null) throw new HzRefusal(ErrorCodes.Unsupported, "Civil 3D could not open the data shortcut manager (no working folder / project set?). Nothing ran.");
        return m;
    }

    /// <summary>Current project folder (Civil returns the project NAME; the folder is working folder + name).</summary>
    private static string ProjectPath()
    {
        var p = DataShortcuts.GetCurrentProjectFolder();
        if (string.IsNullOrEmpty(p)) throw new HzRefusal(ErrorCodes.NotFound, "No data shortcut project is current. Use shortcuts_project first. Nothing changed.");
        return Path.IsPathRooted(p) ? p : Path.Combine(DataShortcuts.GetWorkingFolder(), p);
    }

    private static string AssociatedProject(Document doc)
    {
        try { return DataShortcuts.GetAssociateShortcutProjectIdFromDrawing(doc.Database) ?? ""; } catch (System.Exception) { return ""; }
    }

    private static List<DataShortcuts.DataShortcutManager.PublishedItem> Published(DataShortcuts.DataShortcutManager m)
    {
        try { return Enumerable.Range(0, m.GetPublishedItemsCount()).Select(m.GetPublishedItemAt).ToList(); }
        catch (InvalidOperationException) { return new(); } // no shortcut published yet in this project
    }

    private static string Path_(DataShortcuts.DataShortcutManager.PublishedItem p) => Path.Combine(p.SourceLocation ?? "", p.SourceFileName ?? "");

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Status(Document doc, JsonObject data)
    {
        data["working_folder"] = DataShortcuts.GetWorkingFolder();
        data["current_project"] = DataShortcuts.GetCurrentProjectFolder();
        data["other_projects"] = Hz.Strings(DataShortcuts.GetOtherProjectFolders() ?? new List<string>());
        try { data["drawing_project_id"] = DataShortcuts.GetAssociateShortcutProjectIdFromDrawing(doc.Database); } catch (System.Exception) { data["drawing_project_id"] = null; }
        DataShortcuts.DataShortcutManager m;
        try { m = Manager(); }
        catch (System.Exception e) when (e is InvalidOperationException or HzRefusal)
        {
            // Live finding: with no active shortcut project Civil 3D throws "Can't get data shortcuts from active project".
            data["published"] = null;
            data["publishable_from_this_drawing"] = null;
            data["unavailable_reason"] = e.Message + " Set a working folder and a data shortcut project in Civil 3D (Prospector > Data Shortcuts).";
            return;
        }
        using var _ = m;
        try { StatusLists(m, data); }
        catch (InvalidOperationException e)
        {
            // Live finding: GetPublishedItemsCount throws "Can't get data shortcuts from active project" with no project.
            data["published"] = null;
            data["publishable_from_this_drawing"] = null;
            data["unavailable_reason"] = e.Message + " Set a working folder and a data shortcut project in Civil 3D (Prospector > Data Shortcuts).";
        }
    }

    private static void StatusLists(DataShortcuts.DataShortcutManager m, JsonObject data)
    {
        // Live finding: GetPublishedItemsCount throws "Can't get data shortcuts from active project" while the
        // project has no shortcut yet; the publishable list is independent and must still be reported.
        JsonArray? pub = new JsonArray();
        try
        {
            for (var i = 0; i < m.GetPublishedItemsCount(); i++)
            {
                var p = m.GetPublishedItemAt(i);
                pub.Add(new JsonObject { ["name"] = p.Name, ["type"] = p.DSEntityType.ToString(), ["source"] = Path_(p), ["broken"] = p.IsBroken, ["description"] = p.Description });
            }
        }
        catch (InvalidOperationException e)
        {
            pub = null;
            data["published_unavailable_reason"] = e.Message + ": either the project has no shortcuts yet, or THIS drawing is not associated with the project " +
                "(shortcuts_reference associates it; a drawing saved inside the project folder can publish without it).";
        }
        var exp = new JsonArray();
        for (var i = 0; i < m.GetExportableItemsCount(); i++)
        {
            var e = m.GetExportableItemAt(i);
            exp.Add(new JsonObject { ["index"] = e.Index, ["name"] = e.Name, ["type"] = e.DSEntityType.ToString(), ["published"] = e.IsExported });
        }
        data["published"] = pub;
        data["publishable_from_this_drawing"] = exp;
    }

    private static string Norm(string? p) => string.IsNullOrEmpty(p) ? "" : Path.GetFullPath(p).TrimEnd('\\', '/');

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static CommandResult Project(CommandContext ctx)
    {
        var folder = Norm(Hz.Str(ctx.Args, "working_folder"));
        var name = Hz.Str(ctx.Args, "name"); // null = set the working folder only (used to restore a previous setting)
        var exists = false;
        return WriteFlow.Run(ctx, "HZ_EXCHANGE",
            (doc, tr, plan) =>
            {
                string? prevFolder = null, prevProject = null;
                try { prevFolder = DataShortcuts.GetWorkingFolder(); } catch (System.Exception) { }
                try { prevProject = DataShortcuts.GetCurrentProjectFolder(); } catch (System.Exception) { }
                exists = name != null && Directory.Exists(Path.Combine(folder, name));
                plan["working_folder"] = folder; plan["project"] = name; plan["project_exists"] = exists;
                plan["folder_will_be_created"] = !Directory.Exists(folder);
                plan["previous"] = new JsonObject { ["working_folder"] = prevFolder, ["current_project"] = prevProject };
                plan["note"] = "This changes Civil 3D's data-shortcut working folder for this user. To go back, run shortcuts_project with the previous values.";
            },
            (doc, tr) =>
            {
                Directory.CreateDirectory(folder);
                DataShortcuts.SetWorkingFolder(folder);
                if (name == null) return;
                if (exists) DataShortcuts.SetCurrentProjectFolder(name);
                else DataShortcuts.CreateProjectFolder(name, Hz.Str(ctx.Args, "description") ?? "Horizun", true);
            },
            (doc, tr, v, after) =>
            {
                var wf = Norm(DataShortcuts.GetWorkingFolder());
                var cur = Norm(DataShortcuts.GetCurrentProjectFolder());
                v.Check("working folder", folder, wf, string.Equals(wf, folder, StringComparison.OrdinalIgnoreCase));
                if (name != null)
                {
                    v.Check("current project", name, cur, cur.EndsWith(name, StringComparison.OrdinalIgnoreCase));
                    v.Flag("project folder on disk", true, Directory.Exists(Path.Combine(folder, name)));
                }
                after["working_folder"] = wf; after["current_project"] = cur;
            });
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static CommandResult Publish(CommandContext ctx)
    {
        var names = Resolve.Strings(ctx.Args["names"]);
        var already = new List<string>();
        string projectId = "", project = "";
        var associate = false;
        return WriteFlow.Run(ctx, "HZ_EXCHANGE",
            (doc, tr, plan) =>
            {
                if (string.IsNullOrEmpty(doc.Database.Filename) || !File.Exists(doc.Database.Filename)) throw new HzRefusal(ErrorCodes.InvalidInput, "Save the drawing inside the shortcut project first. Nothing published.");
                if (Convert.ToInt32(AcApp.GetSystemVariable("DBMOD")) != 0) throw new HzRefusal(ErrorCodes.InvalidInput, "The drawing has unsaved changes; shortcuts point at the SAVED file. Save first. Nothing published.");
                project = ProjectPath();
                if (!Path.GetFullPath(doc.Database.Filename).StartsWith(Path.GetFullPath(project), StringComparison.OrdinalIgnoreCase))
                    throw new HzRefusal(ErrorCodes.InvalidInput, "The drawing is not inside the current shortcut project (" + project + "). Nothing published.");
                // Live finding (v0.7.3): after a Civil 3D restart, a drawing that is NOT associated with the project
                // cannot read the published list (and reports every item as unpublished), so the publish could not be
                // verified. Associate it in the apply, as the Civil 3D UI does when creating shortcuts.
                projectId = DataShortcuts.GetDSProjectId(project) ?? "";
                var assoc = AssociatedProject(doc);
                if (!string.IsNullOrEmpty(assoc) && !string.Equals(assoc, projectId, StringComparison.OrdinalIgnoreCase))
                    throw new HzRefusal(ErrorCodes.InvalidInput, "This drawing is associated with another shortcut project (" + assoc + "). Nothing published.");
                associate = string.IsNullOrEmpty(assoc);
                using var m = Manager();
                var all = Enumerable.Range(0, m.GetExportableItemsCount()).Select(m.GetExportableItemAt).ToList();
                foreach (var n in names)
                    if (!all.Any(x => x.Name.Equals(n, StringComparison.OrdinalIgnoreCase)))
                        throw new HzRefusal(ErrorCodes.NotFound, "'" + n + "' cannot be published from this drawing. Nothing published.", new JsonObject { ["publishable"] = Hz.Strings(all.Select(x => x.Name)) });
                plan["project"] = project; plan["publish"] = Hz.Strings(names);
                plan["associate_drawing_with_project"] = associate ? projectId : null;
            },
            (doc, tr) =>
            {
                if (associate) DataShortcuts.AssociateDSProject(projectId, doc.Database, false);
                var m = Manager();
                var selected = 0;
                for (var i = 0; i < m.GetExportableItemsCount(); i++)
                {
                    var e = m.GetExportableItemAt(i);
                    if (!names.Contains(e.Name, StringComparer.OrdinalIgnoreCase)) continue;
                    if (e.IsExported) { already.Add(e.Name); continue; }
                    m.SetSelectItemAtIndex(e.Index, true);
                    selected++;
                }
                if (selected > 0 && !DataShortcuts.SaveDataShortcutManager(ref m)) throw new HzRefusal(ErrorCodes.Internal, "Civil 3D refused to save the data shortcuts. Nothing published.");
                m.Dispose();
            },
            (doc, tr, v, after) =>
            {
                using var m = Manager();
                var pub = Published(m).Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var n in names)
                {
                    // Fallback: the shortcut XML Civil 3D writes in <project>\_Shortcuts\<type>\<name>_<guid>.xml
                    var onDisk = Directory.Exists(Path.Combine(project, "_Shortcuts")) &&
                                 Directory.EnumerateFiles(Path.Combine(project, "_Shortcuts"), n + "_*.xml", SearchOption.AllDirectories).Any();
                    v.Check(n + " published", true, pub.Contains(n) ? "in the published list" : onDisk ? "shortcut file on disk only" : "missing", pub.Contains(n));
                }
                after["published"] = Hz.Strings(names);
                if (already.Count > 0) after["already_published"] = Hz.Strings(already);
                if (associate) after["note"] = "The drawing was associated with the shortcut project; save it to keep the association.";
            });
    }

    private static readonly Dictionary<string, string> CatalogType = new()
    {
        ["Surface"] = "surface", ["Alignment"] = "alignment", ["PipeNetwork"] = "pipe_network", ["Corridor"] = "corridor",
    };

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static CommandResult Reference(CommandContext ctx)
    {
        var name = Hz.Str(ctx.Args, "name")!;
        var type = (DataShortcutEntityType)Enum.Parse(typeof(DataShortcutEntityType), Hz.Str(ctx.Args, "type")!);
        var source = Hz.Str(ctx.Args, "source_dwg");
        var created = new List<ObjectId>();
        string projectId = "";
        var associate = false;
        return WriteFlow.Run(ctx, "HZ_EXCHANGE",
            (doc, tr, plan) =>
            {
                // Live finding (v0.7.2): CreateReference and the published list both fail ("Can't get data shortcuts
                // from active project") until the HOST drawing is associated with the current project; the Civil 3D
                // UI associates it implicitly. Associate it in the apply, and say so in the plan.
                projectId = DataShortcuts.GetDSProjectId(ProjectPath()) ?? "";
                var assoc = AssociatedProject(doc);
                if (!string.IsNullOrEmpty(assoc) && !string.Equals(assoc, projectId, StringComparison.OrdinalIgnoreCase))
                    throw new HzRefusal(ErrorCodes.InvalidInput, "This drawing is associated with another shortcut project (" + assoc + "); it is not re-associated automatically. Nothing changed.");
                associate = string.IsNullOrEmpty(assoc);
                plan["associate_drawing_with_project"] = associate ? projectId : null;
                if (source == null && associate)
                    plan["source_dwg_resolution"] = "resolved from the project's published shortcuts right after associating the drawing";
                else if (source == null)
                {
                    using var m = Manager();
                    var items = Published(m);
                    var it = items.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && p.DSEntityType == type)
                             ?? throw new HzRefusal(ErrorCodes.NotFound, "No published " + type + " shortcut '" + name + "'. Nothing changed.", new JsonObject { ["published"] = Hz.Strings(items.Select(p => p.DSEntityType + ":" + p.Name)) });
                    if (it.IsBroken) throw new HzRefusal(ErrorCodes.InvalidInput, "Shortcut '" + name + "' is broken (source missing). Nothing changed.");
                    source = Path.Combine(it.SourceLocation ?? "", it.SourceFileName ?? "");
                }
                if (source != null)
                {
                    if (!File.Exists(source)) throw new HzRefusal(ErrorCodes.NotFound, "Source drawing not found: " + source + ". Nothing changed.");
                    if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(doc.Database.Filename ?? ""), StringComparison.OrdinalIgnoreCase))
                        throw new HzRefusal(ErrorCodes.InvalidInput, "The source is this drawing. Nothing changed.");
                }
                if (CatalogType.TryGetValue(type.ToString(), out var ct)) Resolve.Unique(doc, tr, ct, name);
                plan["name"] = name; plan["type"] = type.ToString(); plan["source_dwg"] = source;
            },
            (doc, tr) =>
            {
                if (associate) DataShortcuts.AssociateDSProject(projectId, doc.Database, false);
                if (source == null)
                {
                    using var m = Manager();
                    var it = Published(m).FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && p.DSEntityType == type)
                             ?? throw new HzRefusal(ErrorCodes.NotFound, "No published " + type + " shortcut '" + name + "' in the current project. Nothing changed.");
                    if (it.IsBroken) throw new HzRefusal(ErrorCodes.InvalidInput, "Shortcut '" + name + "' is broken (source missing). Nothing changed.");
                    source = Path.Combine(it.SourceLocation ?? "", it.SourceFileName ?? "");
                }
                var ids = DataShortcuts.CreateReference(doc.Database, source!, name, type);
                if (ids == null || ids.Count == 0) throw new HzRefusal(ErrorCodes.Internal, "Civil 3D created no reference object. Nothing changed.");
                created.AddRange(ids.Cast<ObjectId>());
            },
            (doc, tr, v, after) =>
            {
                v.Check("reference objects", "> 0", created.Count, created.Count > 0);
                var rows = new JsonArray();
                foreach (var id in created.Take(20))
                {
                    var d = Catalog.Describe(tr.GetObject(id, OpenMode.ForRead), tr, new Catalog.Lookup(tr), false);
                    rows.Add(d);
                }
                if (created.Count > 0)
                {
                    var first = (JsonObject)rows[0]!;
                    v.Text("reference name", name, Hz.Str(first, "name"));
                    v.Flag("is a data reference", true, Hz.Bool(first, "is_reference") == true);
                }
                if (associate) v.Text("drawing associated with the project", projectId, AssociatedProject(doc));
                after["references"] = rows;
                after["source_dwg"] = source;
                if (associate) after["note"] = "The drawing was associated with the shortcut project; save it to keep the association and the reference.";
            });
    }
}
