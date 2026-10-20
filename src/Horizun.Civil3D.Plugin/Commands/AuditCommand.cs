using System.Text.Json.Nodes;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.DatabaseServices;
using Horizun.Civil3D.Core;
using Horizun.Civil3D.Plugin.Civil;
using CivilEntity = Autodesk.Civil.DatabaseServices.Entity;
using DbObject = Autodesk.AutoCAD.DatabaseServices.DBObject;
using CivilSurface = Autodesk.Civil.DatabaseServices.Surface;

namespace Horizun.Civil3D.Plugin.Commands;

internal sealed class AuditCommand : ICommand
{
    public string Name => "audit";

    private static readonly string[] Types =
    {
        "surface", "alignment", "profile", "corridor", "feature_line", "site", "parcel",
        "pipe_network", "pipe", "structure",
    };

    private static bool? Flag(Func<bool> read, List<string> errors, string label)
    {
        try { return read(); }
        catch (Exception e)
        {
            errors.Add(label + ": " + e.GetType().Name + ": " + e.Message);
            return null;
        }
    }

    private static JsonObject Totals(AuditCounts c) => new()
    {
        ["enumerated"] = c.Enumerated,
        ["inspected"] = c.Inspected,
        ["unreadable_objects"] = c.Unreadable,
        ["references"] = c.References,
        ["reference_status_unknown"] = c.ReferenceUnknown,
        ["stale_references"] = c.StaleReferences,
        ["stale_status_unknown"] = c.StaleUnknown,
        ["invalid_references"] = c.InvalidReferences,
        ["validity_unknown"] = c.ValidityUnknown,
        ["out_of_date"] = c.OutOfDate,
        ["current"] = c.Fresh,
        ["currency_unknown"] = c.CurrencyUnknown,
    };

    public CommandResult Execute(CommandContext ctx)
    {
        var rawLimit = Hz.Num(ctx.Args, "sample_limit") ?? 50;
        if (rawLimit < 0 || rawLimit > 200 || rawLimit != Math.Truncate(rawLimit))
            throw new HzRefusal(ErrorCodes.InvalidInput, "sample_limit must be an integer from 0 to 200.");
        var limit = (int)rawLimit;
        var doc = ctx.Document(forWrite: false);
        var data = new JsonObject
        {
            ["tool"] = ctx.Tool.Name,
            ["document"] = DrawingInfo.File(doc, true),
            ["read_only"] = true,
            ["scope"] = "Current active drawing. Full count for listed Civil 3D types; detailed examples are limited. " +
                        "Feature lines are enumerated in model space, including grading-owned feature lines. " +
                        "The audit does not establish design-code compliance or inspect geometry quality.",
        };
        ctx.Read(doc, tr =>
        {
            var civil = CommandContext.Civil(doc);
            var db = doc.Database;
            var unavailable = new JsonObject();
            try { data["units"] = DrawingInfo.Units(civil, db); }
            catch (Exception e) { data["units"] = null; unavailable["units"] = e.GetType().Name + ": " + e.Message; }
            try { data["coordinate_system"] = DrawingInfo.CoordinateSystem(civil); }
            catch (Exception e) { data["coordinate_system"] = null; unavailable["coordinate_system"] = e.GetType().Name + ": " + e.Message; }

            try
            {
                var xrefs = DrawingInfo.Xrefs(db, tr);
                data["xrefs"] = xrefs;
                data["xref_count"] = xrefs.Count;
                data["xrefs_not_resolved"] = xrefs.OfType<JsonObject>().Count(x =>
                    !string.Equals(Hz.Str(x, "status"), "Resolved", StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception e)
            {
                data["xrefs"] = null;
                data["xref_count"] = null;
                data["xrefs_not_resolved"] = null;
                unavailable["xrefs"] = e.GetType().Name + ": " + e.Message;
            }

            var types = new JsonObject();
            var examples = new JsonArray();
            var omitted = 0;
            var partial = unavailable.Count > 0 || data["units"]?["unreadable"] is JsonObject { Count: > 0 }
                          || data["coordinate_system"]?["unreadable"] is JsonObject { Count: > 0 };
            foreach (var type in Types)
            {
                var counts = new AuditCounts();
                List<ObjectId> ids;
                try { ids = Catalog.Ids(type, db, civil, tr); counts.SetEnumerated(ids.Count); }
                catch (Exception e)
                {
                    partial = true;
                    var row = Totals(counts);
                    row["unavailable_reason"] = e.GetType().Name + ": " + e.Message;
                    types[type] = row;
                    continue;
                }
                foreach (var id in ids)
                {
                    if (id.IsNull || id.IsErased) { counts.MarkUnreadable(); partial = true; continue; }
                    DbObject obj;
                    try { obj = tr.GetObject(id, OpenMode.ForRead); }
                    catch (Exception e)
                    {
                        counts.MarkUnreadable(); partial = true;
                        if (examples.Count < limit) examples.Add(new JsonObject { ["type"] = type, ["handle"] = id.Handle.ToString(),
                            ["unreadable"] = e.GetType().Name + ": " + e.Message });
                        else omitted++;
                        continue;
                    }

                    var errors = new List<string>();
                    var ce = obj as CivilEntity;
                    var reference = ce == null ? null : Flag(() => ce.IsReferenceObject, errors, "reference");
                    var stale = reference == true ? Flag(() => ce!.IsReferenceStale, errors, "stale") : null;
                    var valid = reference == true ? Flag(() => ce!.IsReferenceValid, errors, "valid") : null;
                    var currency = obj switch
                    {
                        CivilSurface s => Flag(() => s.IsOutOfDate, errors, "surface currency"),
                        Corridor c => Flag(() => c.IsOutOfDate, errors, "corridor currency"),
                        _ => null,
                    };
                    counts.Record(reference, stale, valid, currency, obj is CivilSurface or Corridor);
                    if (errors.Count > 0) partial = true;
                    if (stale != true && valid != false && currency != true && errors.Count == 0) continue;
                    if (examples.Count >= limit) { omitted++; continue; }
                    string? name;
                    try { name = Catalog.NameOf(obj); }
                    catch (Exception e) { name = null; errors.Add("name: " + e.GetType().Name + ": " + e.Message); partial = true; }
                    examples.Add(new JsonObject
                    {
                        ["type"] = type,
                        ["handle"] = obj.Handle.ToString(),
                        ["name"] = name,
                        ["reference_stale"] = stale,
                        ["reference_valid"] = valid,
                        ["out_of_date"] = currency,
                        ["unreadable"] = errors.Count == 0 ? null : Hz.Strings(errors),
                    });
                }
                if (counts.Unreadable > 0 || counts.ReferenceUnknown > 0 || counts.StaleUnknown > 0 ||
                    counts.ValidityUnknown > 0 || counts.CurrencyUnknown > 0) partial = true;
                types[type] = Totals(counts);
            }
            data["types"] = types;
            data["examples"] = examples;
            data["examples_omitted"] = omitted;
            data["partial"] = partial;
            if (unavailable.Count > 0) data["unavailable"] = unavailable;
            return 0;
        });
        return CommandResult.Ok(data);
    }
}
