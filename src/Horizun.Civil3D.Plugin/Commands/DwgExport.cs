// API signatures: docs/api-probes/2025/AcDbMgd.dwg-export.txt.
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using Autodesk.AutoCAD.DatabaseServices;
using Horizun.Civil3D.Core;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace Horizun.Civil3D.Plugin.Commands;

internal static class DwgExport
{
    private static AtomicOutput.DestinationState CaptureSource(string path)
    {
        // AutoCAD retains a write handle to its open drawing. Share that handle
        // for reading; the document lock spans both hashes and the copy operation.
        if (!File.Exists(path)) return new(false, null);
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var hash = SHA256.Create();
        return new(true, BitConverter.ToString(hash.ComputeHash(input)).Replace("-", ""));
    }
    // Structural evidence only: equal counts cannot establish equality of every
    // Civil design value, dependency or external reference target.
    private static JsonObject Summary(Database db, Transaction tr)
    {
        var blocks = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);
        var table = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        foreach (ObjectId id in table)
        {
            var block = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
            if (block.IsFromExternalReference || block.IsFromOverlayReference) continue;
            var classes = new SortedDictionary<string, long>(StringComparer.Ordinal);
            foreach (ObjectId entityId in block)
            {
                if (entityId.IsErased) continue;
                var name = entityId.ObjectClass.Name;
                classes[name] = classes.GetValueOrDefault(name) + 1;
            }
            var counts = new JsonObject();
            foreach (var row in classes) counts[row.Key] = row.Value;
            blocks[block.Name] = new JsonObject { ["is_layout"] = block.IsLayout, ["entity_classes"] = counts };
        }
        var result = new JsonObject();
        foreach (var block in blocks) result[block.Key] = block.Value;
        return new JsonObject { ["insertion_units"] = db.Insunits.ToString(), ["local_blocks"] = result };
    }

    private static JsonObject ReadFile(string path)
    {
        using var loaded = new Database(false, true);
        loaded.ReadDwgFile(path, FileOpenMode.OpenForReadAndReadShare, false, "");
        if (loaded.NeedsRecovery) throw new IOException("The exported DWG requires recovery.");
        JsonObject summary;
        using (var tr = loaded.TransactionManager.StartTransaction())
        {
            summary = Summary(loaded, tr);
            tr.Abort();
        }
        loaded.CloseInput(true);
        return summary;
    }

    private static void RequireSameSummary(string phase, JsonObject expected, JsonObject actual)
    {
        if (JsonNode.DeepEquals(expected, actual)) return;
        // Retain exact block names and counts: silently ignoring anonymous or
        // unreferenced blocks could hide a real loss of drawing content.
        var differences = new JsonArray();
        var before = (JsonObject)expected["local_blocks"]!;
        var after = (JsonObject)actual["local_blocks"]!;
        foreach (var name in before.Select(x => x.Key).Concat(after.Select(x => x.Key))
                     .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal))
        {
            if (JsonNode.DeepEquals(before[name], after[name])) continue;
            differences.Add(new JsonObject
            {
                ["block"] = name,
                ["expected"] = before[name]?.DeepClone(),
                ["actual"] = after[name]?.DeepClone(),
            });
        }
        var evidence = new JsonObject
        {
            ["phase"] = phase,
            ["expected_units"] = expected["insertion_units"]?.DeepClone(),
            ["actual_units"] = actual["insertion_units"]?.DeepClone(),
            ["block_differences"] = differences,
        };
        Log.Error("DWG export structural mismatch " + evidence.ToJsonString(Hz.Compact));
        throw new IOException("DWG export failed structural verification at " + phase +
                              "; destination was not published. " + evidence.ToJsonString(Hz.Compact));
    }

    public static CommandResult Run(CommandContext ctx)
    {
        var output = Path.GetFullPath(Hz.Str(ctx.Args, "output")!);
        JsonObject expected = new();
        var sourceName = "";
        var databaseFilename = "";
        var fingerprint = "";
        var versionGuid = "";
        var dbmod = 0;
        AtomicOutput.DestinationState? sourceFile = null;
        Autodesk.AutoCAD.ApplicationServices.Document? pushedDocument = null;
        try
        {
        return WriteFlow.Run(ctx, "HZ_EXPORT_DWG",
            (doc, tr, plan) =>
            {
                if (doc != AcApp.DocumentManager.MdiActiveDocument)
                    throw new HzRefusal(ErrorCodes.DocumentMismatch, "DWG export requires the active drawing to verify its modified state.");
                if (string.Equals(output, Path.GetFullPath(doc.Name), StringComparison.OrdinalIgnoreCase))
                    throw new HzRefusal(ErrorCodes.InvalidInput, "The destination is the source drawing. Nothing written.");
                if (File.Exists(output) || Directory.Exists(output))
                    throw new HzRefusal(ErrorCodes.InvalidInput, "The destination exists; export_dwg never overwrites. Nothing written.");
                if (!Directory.Exists(Path.GetDirectoryName(output)))
                    throw new HzRefusal(ErrorCodes.NotFound, "The destination folder does not exist. Nothing written.");
                expected = Summary(doc.Database, tr);
                sourceName = doc.Name;
                databaseFilename = doc.Database.Filename;
                fingerprint = doc.Database.FingerprintGuid;
                versionGuid = doc.Database.VersionGuid;
                dbmod = Convert.ToInt32(AcApp.GetSystemVariable("DBMOD"));
                sourceFile = File.Exists(sourceName) ? CaptureSource(sourceName) : null;
                plan["output"] = output;
                plan["dwg_version"] = "Current";
                plan["source"] = sourceName;
                plan["source_structural_summary"] = expected.DeepClone();
                plan["verification_scope"] = "DWG reopen, insertion units and local block/entity class counts; Civil design values are not individually verified.";
                plan["external_references"] = "Xrefs and data shortcuts remain linked to their sources. This action does not package dependent files or relocate their paths.";
                plan["source_saved"] = false;
                plan["copy_method"] = "Full DWG SaveAs with bBakAndRename=false; source filename and modified state preserved.";
                plan["source_dbmod"] = dbmod;
                plan["source_database_filename"] = databaseFilename;
                plan["source_fingerprint"] = fingerprint;
                plan["source_version_guid"] = versionGuid;
                plan["source_file_sha256"] = sourceFile?.Sha256;
            },
            (doc, tr) => AtomicOutput.Write(output, false,
                stage =>
                {
                    // Wblock performs cloning and can drop unreferenced records.
                    // The full-save overload preserves the document filename;
                    // Push/Pop also preserves the dirty flag of the unsaved source.
                    // Keep the stack entry through WriteFlow's transaction commit:
                    // that commit can itself set DBMOD even without object edits.
                    doc.PushDbmod();
                    pushedDocument = doc;
                    doc.Database.SaveAs(stage, false, DwgVersion.Current, doc.Database.SecurityParameters);
                    if (doc.Name != sourceName || doc.Database.Filename != databaseFilename ||
                        doc.Database.FingerprintGuid != fingerprint || doc.Database.VersionGuid != versionGuid ||
                        (sourceFile != null && CaptureSource(sourceName) != sourceFile))
                        throw new IOException("DWG copy changed source identity, modified state or original file; destination was not published.");
                },
                stage =>
                {
                    RequireSameSummary("saved_dwg_reopen", expected, ReadFile(stage));
                    return true;
                }),
            (doc, tr, checks, after) =>
            {
                pushedDocument!.PopDbmod();
                pushedDocument = null;
                checks.Flag("DWG exists", true, File.Exists(output));
                if (!File.Exists(output)) return;
                var actual = ReadFile(output);
                checks.Flag("exported structural summary equals source", true, JsonNode.DeepEquals(expected, actual));
                checks.Text("source document identity unchanged", sourceName, doc.Name);
                checks.Text("source database filename unchanged", databaseFilename, doc.Database.Filename);
                checks.Text("source fingerprint unchanged", fingerprint, doc.Database.FingerprintGuid);
                checks.Text("source version GUID unchanged", versionGuid, doc.Database.VersionGuid);
                checks.Flag("source modified state unchanged", true, Convert.ToInt32(AcApp.GetSystemVariable("DBMOD")) == dbmod);
                if (sourceFile != null)
                    checks.Flag("original source DWG bytes unchanged", true, CaptureSource(sourceName) == sourceFile);
                after["structural_summary"] = actual;
                after["file"] = new JsonObject
                {
                    ["path"] = output, ["bytes"] = new FileInfo(output).Length,
                    ["sha256"] = AtomicOutput.Capture(output).Sha256,
                };
                after["verification_scope"] = "File reopened and structural counts checked; external dependencies and individual design values require further inspection.";
            });
        }
        finally
        {
            // Balance the stack even when staging, committing or verifying fails.
            if (pushedDocument != null) pushedDocument.PopDbmod();
        }
    }
}
