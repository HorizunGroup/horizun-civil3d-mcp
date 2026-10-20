// Confirmed signatures: AeccDbMgd.phase3-points.txt CogoPoint coordinate and
// RawDescription setters; export-units.txt foot conversion; acdbmgd.revision.txt
// FingerprintGuid. No guessed importer, transform or merge semantics.
using System.Text;
using System.Text.Json.Nodes;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.Settings;
using Horizun.Civil3D.Core;
using Horizun.Civil3D.Plugin.Civil;

namespace Horizun.Civil3D.Plugin.Commands;

internal sealed partial class PointsCommand
{
    private static PointEditCsv.Snapshot CsvSnapshot(CommandContext ctx, Document doc, Transaction tr)
    {
        var zone = Civ(doc).Settings.DrawingSettings.UnitZoneSettings;
        var unit = zone.DrawingUnits switch {
            DrawingUnitType.Meters => "meter",
            DrawingUnitType.Feet => zone.ImperialToMetricConversion switch {
                ImperialToMetricConversionType.InternationalFoot => "foot",
                ImperialToMetricConversionType.UsSurveyFoot => "USSurveyFoot",
                _ => throw new HzRefusal(ErrorCodes.Unsupported, "Unknown Civil foot conversion. Nothing changed.") },
            _ => throw new HzRefusal(ErrorCodes.Unsupported, "Unknown Civil drawing units. Nothing changed.") };
        var fingerprint = doc.Database.FingerprintGuid;
        if (string.IsNullOrWhiteSpace(fingerprint)) throw new HzRefusal(ErrorCodes.Unsupported, "Drawing has no persistent identity; save it before editable CSV export. Nothing changed.");
        try {
            return PointEditCsv.Capture(Select(ctx, doc, tr).Select(p => new PointFile.Row(p.PointNumber, p.Easting, p.Northing, p.Elevation, p.RawDescription ?? "")),
                unit, doc.Name + "|" + fingerprint);
        } catch (ArgumentException e) { throw new HzRefusal(ErrorCodes.InvalidInput, e.Message + " Nothing changed."); }
    }

    private static CommandResult ExportEditableCsv(CommandContext ctx)
    {
        var output = Hz.Str(ctx.Args, "output")!; byte[]? bytes = null; PointEditCsv.Snapshot? snapshot = null;
        bool Matches(string path) => bytes != null && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes);
        return WriteFlow.Run(ctx, "HZ_POINTS_EDITABLE_CSV",
            (doc, tr, plan) => {
                if (File.Exists(output) || Directory.Exists(output)) throw new HzRefusal(ErrorCodes.InvalidInput, "Destination already exists. Nothing written.");
                if (!Directory.Exists(Path.GetDirectoryName(output))) throw new HzRefusal(ErrorCodes.NotFound, "Destination folder does not exist. Nothing written.");
                snapshot = CsvSnapshot(ctx, doc, tr); bytes = Encoding.UTF8.GetBytes(PointEditCsv.Write(snapshot));
                plan["output"] = output; plan["points"] = snapshot.Rows.Count; plan["linear_unit"] = snapshot.LinearUnit;
                plan["source_sha256"] = snapshot.Fingerprint; plan["csv_sha256"] = RevitTerrainPackage.Hash(bytes);
                plan["edit_rules"] = "Keep all rows, source_sha256, linear_unit and point numbers. Edit easting/northing/elevation/plain-text description; apply_csv uses the same group/numbers selection. No coordinate transform or point creation/deletion.";
            },
            (doc, tr) => AtomicOutput.Write(output, false, stage => File.WriteAllBytes(stage, bytes!), Matches),
            (doc, tr, checks, after) => {
                checks.Flag("editable CSV exists", true, File.Exists(output));
                if (File.Exists(output)) checks.Flag("CSV every byte reread equals exported snapshot", true, Matches(output));
                after["file"] = new JsonObject { ["path"] = output, ["sha256"] = RevitTerrainPackage.Hash(bytes!), ["points"] = snapshot!.Rows.Count };
            });
    }

    private static CommandResult ApplyCsv(CommandContext ctx)
    {
        var file = Hz.Str(ctx.Args, "file")!;
        var resolved = new List<(ObjectId Id, PointEditCsv.Edit Edit)>();
        return WriteFlow.Run(ctx, "HZ_POINTS_CSV_EDIT",
            (doc, tr, plan) => {
                if (!File.Exists(file)) throw new HzRefusal(ErrorCodes.NotFound, "Editable CSV file does not exist. Nothing changed.");
                if (new FileInfo(file).Length > PointEditCsv.MaxBytes) throw new HzRefusal(ErrorCodes.InvalidInput, "Editable CSV exceeds 8 MiB. Nothing changed.");
                var input = File.ReadAllBytes(file); var snapshot = CsvSnapshot(ctx, doc, tr);
                IReadOnlyList<PointEditCsv.Edit> edits;
                try { edits = PointEditCsv.ReadEdits(new UTF8Encoding(false, true).GetString(input), snapshot); }
                catch (Exception e) when (e is ArgumentException || e is DecoderFallbackException) { throw new HzRefusal(ErrorCodes.InvalidInput, e.Message + " Nothing changed."); }
                if (edits.Count == 0) throw new HzRefusal(ErrorCodes.InvalidInput, "CSV contains no point edits. Nothing changed.");
                resolved.Clear();
                foreach (var edit in edits) {
                    var id = Civ(doc).CogoPoints.GetPointByPointNumber(edit.Before.Number!.Value);
                    var point = (CogoPoint)tr.GetObject(id, OpenMode.ForRead);
                    Resolve.Editable(point, tr);
                    if (point.IsLocked) throw new HzRefusal(ErrorCodes.NotEditable, "Locked COGO points cannot be edited by CSV. Nothing changed.");
                    if (!point.IsMovable || point.IsSurveyPoint || point.IsProjectPoint && !point.IsCheckedOut)
                        throw new HzRefusal(ErrorCodes.NotEditable, "Survey, unmovable or unchecked-out project points cannot be edited by CSV. Nothing changed.");
                    resolved.Add((id, edit));
                }
                plan["file"] = file; plan["csv_sha256"] = RevitTerrainPackage.Hash(input); plan["source_sha256"] = snapshot.Fingerprint;
                plan["linear_unit"] = snapshot.LinearUnit; plan["updated_points"] = resolved.Count;
                plan["edits"] = new JsonArray(resolved.Select(r => (JsonNode)new JsonObject { ["number"] = (long)r.Edit.Before.Number!.Value,
                    ["before"] = CsvRow(r.Edit.Before), ["after"] = CsvRow(r.Edit.After) }).ToArray());
            },
            (doc, tr) => {
                foreach (var (id, edit) in resolved) {
                    var point = (CogoPoint)tr.GetObject(id, OpenMode.ForWrite);
                    point.Easting = edit.After.X; point.Northing = edit.After.Y;
                    point.Elevation = edit.After.Z; point.RawDescription = edit.After.Description;
                }
            },
            (doc, tr, checks, after) => {
                foreach (var (id, edit) in resolved) {
                    var point = (CogoPoint)tr.GetObject(id, OpenMode.ForRead); var label = "point " + edit.After.Number!.Value;
                    checks.Number(label + " number", edit.After.Number.Value, point.PointNumber, 0);
                    checks.Number(label + " easting", edit.After.X, point.Easting, 1e-8);
                    checks.Number(label + " northing", edit.After.Y, point.Northing, 1e-8);
                    checks.Number(label + " elevation", edit.After.Z, point.Elevation, 1e-8);
                    checks.Text(label + " raw description", edit.After.Description, point.RawDescription);
                }
                after["updated_points"] = resolved.Count; after["source_saved"] = false;
            });
    }

    private static JsonObject CsvRow(PointFile.Row row) => new() { ["x"] = row.X, ["y"] = row.Y, ["z"] = row.Z, ["description"] = row.Description };
}
