// -----------------------------------------------------------------------------
// horizun_c3d_document - info, list_open, object_census, save.
//
// save is the first write of the bridge and follows the full contract:
// dry run by default -> plan + single-use token -> apply with the token ->
// verified from the FILE ON DISK (written after the request, non-empty), not
// from "SaveAs did not throw".
// -----------------------------------------------------------------------------
using System.Text.Json.Nodes;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Horizun.Civil3D.Core;
using Horizun.Civil3D.Plugin.Civil;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using CivilEntity = Autodesk.Civil.DatabaseServices.Entity;

namespace Horizun.Civil3D.Plugin.Commands;

internal sealed class DocumentCommand : ICommand
{
    public string Name => "document";

    public CommandResult Execute(CommandContext ctx) =>
        ctx.RequireAction("info", "list_open", "object_census", "save", "undo_last") switch
        {
            "info" => Info(ctx),
            "list_open" => ListOpen(),
            "object_census" => Census(ctx),
            "undo_last" => UndoLast(ctx),
            _ => Save(ctx),
        };

    /// <summary>
    /// Undo the LAST Horizun write as one Civil 3D UNDO step - only while nothing has changed since that write
    /// (no user edit, no other write), so the UNDO can never revert someone else's work. The bridge sends no other
    /// command, ever; this one runs inside the bridge's own command context, only on an explicit, confirmed request.
    /// </summary>
    private static CommandResult UndoLast(CommandContext ctx)
    {
        var doc = ctx.Document(forWrite: true);
        if (doc != AcApp.DocumentManager.MdiActiveDocument)
            throw new HzRefusal(ErrorCodes.DocumentMismatch, "Only the active drawing can be undone. Nothing was changed.");
        var last = DrawingRevision.GetLastWrite(doc)
                   ?? throw new HzRefusal(ErrorCodes.NotFound, "There is no Horizun write to undo in this drawing during this Civil 3D session. Nothing was changed.");
        var now = DrawingRevision.Capture(doc);
        if (now != last.Revision)
            throw new HzRefusal(ErrorCodes.InvalidInput,
                "The drawing changed after Horizun's last write (" + last.Tool + " " + last.Action + "): a user edit, another write, a save or " +
                "Civil 3D updating dependent objects. A UNDO now could revert that newer change, so it is refused. Use U in Civil 3D. Nothing was changed.");
        var plan = new JsonObject
        {
            ["op"] = "undo_last",
            ["drawing_revision"] = now,
            ["undo"] = new JsonObject
            {
                ["tool"] = last.Tool, ["action"] = last.Action, ["undo_label"] = last.UndoLabel, ["written_utc"] = last.Utc.ToString("o"),
                ["created_objects"] = last.Created.Count, ["modified_objects"] = last.Modified,
            },
            ["how"] = "One Civil 3D UNDO step (the write was committed as exactly one step).",
        };
        var data = new JsonObject
        {
            ["tool"] = ctx.Tool.Name,
            ["action"] = "undo_last",
            ["document"] = new JsonObject { ["name"] = Path.GetFileName(doc.Name) },
        };
        if (ctx.DryRun) return CommandResult.Ok(ctx.Rehearse(doc, data, plan));

        ctx.RequireConfirmation(doc, plan);
        doc.Editor.Command("_.UNDO", "1");
        DrawingRevision.ClearLastWrite(doc); // one level only: the previous write's state is no longer known
        DrawingRevision.Bump(doc);

        var v = new VerificationSet();
        v.Flag("Civil 3D UNDO ran (one step)", true, true);
        if (last.Created.Count > 0)
        {
            var back = last.Created.Count(id => id.IsErased || !id.IsValid);
            v.Check("objects created by that write are gone", last.Created.Count, back, back == last.Created.Count);
        }
        data["dry_run"] = false;
        data["committed"] = true;
        data["plan"] = plan;
        data["verified"] = v.ToJson("Created objects re-checked after the UNDO; modified objects are reverted by Civil 3D's UNDO itself - re-read them with the typed tools if you need the values.");
        data["undo"] = new JsonObject { ["instruction"] = "Civil 3D REDO brings the write back." };
        return v.AllVerified ? CommandResult.Ok(data)
            : CommandResult.Fail(ErrorCodes.VerificationFailed, "The UNDO ran but objects created by that write are still present. Inspect the drawing.", data);
    }

    private static CommandResult Info(CommandContext ctx)
    {
        var doc = ctx.Document(forWrite: false);
        var d = DrawingInfo.File(doc, doc == AcApp.DocumentManager.MdiActiveDocument);
        ctx.Read(doc, tr =>
        {
            var civil = CommandContext.Civil(doc);
            d["units"] = DrawingInfo.Units(civil, doc.Database);
            d["coordinate_system"] = DrawingInfo.CoordinateSystem(civil);
            d["xrefs"] = DrawingInfo.Xrefs(doc.Database, tr);
            return 0;
        });
        return CommandResult.Ok(new JsonObject { ["document"] = d });
    }

    private static CommandResult ListOpen()
    {
        var dm = AcApp.DocumentManager;
        var active = dm.MdiActiveDocument;
        var a = new JsonArray();
        foreach (Document d in dm) a.Add(DrawingInfo.File(d, d == active));
        return CommandResult.Ok(new JsonObject { ["count"] = a.Count, ["documents"] = a });
    }

    private static CommandResult Census(CommandContext ctx)
    {
        var doc = ctx.Document(forWrite: false);
        var data = new JsonObject { ["document"] = doc.Name };
        ctx.Read(doc, tr =>
        {
            var db = doc.Database;
            var civil = CommandContext.Civil(doc);
            data["units"] = DrawingInfo.Units(civil, db);
            var counts = new JsonObject();
            var refs = new JsonObject();
            var unreadable = new JsonObject();
            foreach (var type in Catalog.Types)
            {
                try
                {
                    if (type == "cogo_point")
                    {
                        counts[type] = (long)civil.CogoPoints.Count; // references not scanned: can be 100k+ points
                        continue;
                    }
                    var ids = Catalog.Ids(type, db, civil, tr);
                    counts[type] = ids.Count;
                    int isRef = 0, stale = 0;
                    var kinds = type == "surface" ? new Dictionary<string, int>() : null;
                    foreach (var id in ids)
                    {
                        var obj = tr.GetObject(id, OpenMode.ForRead);
                        if (obj is CivilEntity ce && ce.IsReferenceObject)
                        {
                            isRef++;
                            if (ce.IsReferenceStale) stale++;
                        }
                        if (kinds != null)
                        {
                            var k = obj switch
                            {
                                Autodesk.Civil.DatabaseServices.TinVolumeSurface => "tin_volume",
                                Autodesk.Civil.DatabaseServices.TinSurface => "tin",
                                Autodesk.Civil.DatabaseServices.GridVolumeSurface => "grid_volume",
                                Autodesk.Civil.DatabaseServices.GridSurface => "grid",
                                _ => obj.GetType().Name,
                            };
                            kinds[k] = kinds.TryGetValue(k, out var n) ? n + 1 : 1;
                        }
                    }
                    if (isRef > 0) refs[type] = new JsonObject { ["references"] = isRef, ["stale"] = stale };
                    if (kinds != null)
                    {
                        var ko = new JsonObject();
                        foreach (var kv in kinds.OrderBy(k => k.Key)) ko[kv.Key] = kv.Value;
                        data["surfaces_by_kind"] = ko;
                    }
                }
                catch (Exception e)
                {
                    counts[type] = null;
                    unreadable[type] = e.GetType().Name + ": " + e.Message;
                }
            }
            data["counts"] = counts;
            data["data_shortcut_references"] = refs;
            data["data_shortcut_note"] = "References are read-only here: edit them in their source drawing.";

            // Raw model-space histogram by runtime class, for anything the catalogue does not name.
            var hist = new Dictionary<string, int>();
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                if (id.IsErased) continue;
                var cls = id.ObjectClass.Name;
                hist[cls] = hist.TryGetValue(cls, out var n) ? n + 1 : 1;
            }
            var h = new JsonObject();
            foreach (var kv in hist.OrderByDescending(k => k.Value).ThenBy(k => k.Key)) h[kv.Key] = kv.Value;
            data["model_space_by_class"] = h;
            data["xrefs"] = DrawingInfo.Xrefs(db, tr);
            if (unreadable.Count > 0) data["unreadable"] = unreadable;
            return 0;
        });
        return CommandResult.Ok(data);
    }

    private static CommandResult Save(CommandContext ctx)
    {
        var doc = ctx.Document(forWrite: true);
        if (doc != AcApp.DocumentManager.MdiActiveDocument)
            throw new HzRefusal(ErrorCodes.DocumentMismatch, "Only the active drawing can be saved. Nothing was changed.");
        if (doc.IsReadOnly)
            throw new HzRefusal(ErrorCodes.ReadOnlyDocument, "'" + Path.GetFileName(doc.Name) + "' is open read-only. Nothing was changed.");
        var titled = Convert.ToInt32(AcApp.GetSystemVariable("DWGTITLED")) != 0;
        if (!titled || !Path.IsPathRooted(doc.Name))
            throw new HzRefusal(ErrorCodes.Unsupported,
                "This drawing has never been saved, so it has no path of its own. Save it once from Civil 3D (Save As). " +
                "The bridge does not choose file names for you. Nothing was changed.");

        // Keep the lock from plan capture through confirmation and SaveAs. The
        // observer detects changes even when the file and DBMOD flags did not.
        using var saveLock = doc.LockDocument(DocumentLockMode.Write, "HZ_SAVE", "HZ_SAVE", false);
        var path = doc.Name;
        var before = new FileInfo(path);
        var dbmod = Convert.ToInt32(AcApp.GetSystemVariable("DBMOD"));
        var plan = new JsonObject
        {
            ["op"] = "save",
            ["drawing_revision"] = DrawingRevision.Capture(doc),
            ["path"] = path,
            ["file_exists"] = before.Exists,
            ["file_last_write_utc"] = before.Exists ? before.LastWriteTimeUtc.ToString("o") : null,
            ["has_unsaved_changes"] = dbmod != 0,
            ["unsaved_changes"] = DrawingInfo.DbmodBits(dbmod),
            ["backup"] = "Civil 3D writes the previous file as .bak (same as a normal save).",
        };
        var data = new JsonObject
        {
            ["tool"] = ctx.Tool.Name,
            ["action"] = "save",
            ["document"] = new JsonObject { ["name"] = Path.GetFileName(path), ["path"] = path },
            ["units"] = DrawingInfo.Units(CommandContext.Civil(doc), doc.Database),
        };
        if (ctx.DryRun) return CommandResult.Ok(ctx.Rehearse(doc, data, plan));

        ctx.RequireConfirmation(doc, plan);
        var requestedUtc = DateTime.UtcNow;
        var db = doc.Database;
        db.SaveAs(path, true, DwgVersion.Current, db.SecurityParameters);
        DrawingRevision.Bump(doc);

        var after = new FileInfo(path);
        after.Refresh();
        var v = new VerificationSet();
        v.Flag("file exists after save", true, after.Exists);
        v.Check("file written by this request", requestedUtc.AddSeconds(-2).ToString("o"),
            after.Exists ? after.LastWriteTimeUtc.ToString("o") : null,
            after.Exists && after.LastWriteTimeUtc >= requestedUtc.AddSeconds(-2),
            "The file's last-write time is older than this request: the save did not reach the disk.");
        v.Check("file is not empty", "> 0 bytes", after.Exists ? after.Length : null, after.Exists && after.Length > 0);

        bool? stillModified = null;
        try { stillModified = Convert.ToInt32(AcApp.GetSystemVariable("DBMOD")) != 0; } catch { }

        data["dry_run"] = false;
        data["committed"] = true;
        data["plan"] = plan;
        data["verified"] = v.ToJson("Re-read from the FILE ON DISK after the save (existence, write time, size).");
        data["file"] = new JsonObject { ["bytes"] = after.Exists ? after.Length : null, ["last_write_utc"] = after.Exists ? after.LastWriteTimeUtc.ToString("o") : null };
        data["dbmod_after"] = stillModified;
        if (stillModified == true)
            data["dbmod_note"] = "The file on disk was verified. Civil 3D may still flag the drawing as modified after a " +
                                 "programmatic save; that flag does not mean the file is stale.";
        if (!v.AllVerified)
            return CommandResult.Fail(ErrorCodes.VerificationFailed,
                "Save was requested but the file on disk does not prove it (" + v.Status + "). Do not assume the drawing is saved.",
                data);
        return CommandResult.Ok(data);
    }
}
