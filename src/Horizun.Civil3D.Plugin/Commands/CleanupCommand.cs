// -----------------------------------------------------------------------------
// horizun_c3d_cleanup - purge, drawing report, xrefs, standards check (block C).
// -----------------------------------------------------------------------------
using System.Text.Json.Nodes;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Horizun.Civil3D.Core;
using Horizun.Civil3D.Plugin.Civil;
using Entity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace Horizun.Civil3D.Plugin.Commands;

internal sealed class CleanupCommand : ICommand
{
    public string Name => "cleanup";

    public CommandResult Execute(CommandContext ctx)
    {
        if (ToolRules.Validate(ctx.Tool.Name, ctx.Args) is { } why) throw new HzRefusal(ErrorCodes.InvalidInput, why + " Nothing ran.");
        return ctx.Action switch
        {
            "purge_preview" => WriteFlow.Read(ctx, (doc, tr, data) =>
            {
                var p = Purgeable(doc.Database, tr, Kinds(ctx), null);
                data["purgeable"] = ToJson(p);
                data["total"] = p.Values.Sum(l => l.Count);
                data["note"] = "One pass. Purging can make more items purgeable (nested blocks, styles they used); purge repeats passes.";
            }),
            "purge" => Purge(ctx),
            "drawing_report" => WriteFlow.Read(ctx, (doc, tr, data) => Report(ctx, doc, tr, data)),
            "xrefs" => WriteFlow.Read(ctx, (doc, tr, data) => data["xrefs"] = Xrefs(doc.Database, tr)),
            "xref_reload" => XrefReload(ctx),
            _ => WriteFlow.Read(ctx, (doc, tr, data) => Standards(ctx, doc, tr, data)),
        };
    }

    private static List<string> Kinds(CommandContext ctx) =>
        ctx.Args["kinds"] != null ? Resolve.Strings(ctx.Args["kinds"]) : CadInputs.PurgeKinds.ToList();

    // ---- purge -------------------------------------------------------------------

    private static IEnumerable<(ObjectId Id, string Name)> Candidates(Database db, Transaction tr, string kind)
    {
        IEnumerable<(ObjectId, string)> Table(ObjectId tableId, Func<SymbolTableRecord, bool> skip)
        {
            foreach (ObjectId id in (SymbolTable)tr.GetObject(tableId, OpenMode.ForRead))
            {
                var r = (SymbolTableRecord)tr.GetObject(id, OpenMode.ForRead);
                if (!r.IsDependent && !skip(r)) yield return (id, r.Name);
            }
        }
        IEnumerable<(ObjectId, string)> Dict(ObjectId dictId, ObjectId current)
        {
            foreach (DBDictionaryEntry e in (DBDictionary)tr.GetObject(dictId, OpenMode.ForRead))
                if (e.Value != current && !e.Key.Equals("Standard", StringComparison.OrdinalIgnoreCase)) yield return (e.Value, e.Key);
        }
        return kind switch
        {
            "layers" => Table(db.LayerTableId, r => r.Name == "0" || r.Name.Equals("Defpoints", StringComparison.OrdinalIgnoreCase) || r.ObjectId == db.Clayer),
            "linetypes" => Table(db.LinetypeTableId, r => r.Name is "Continuous" or "ByLayer" or "ByBlock" || r.ObjectId == db.Celtype),
            "text_styles" => Table(db.TextStyleTableId, r => r.Name == "Standard" || r.ObjectId == db.Textstyle || ((TextStyleTableRecord)r).IsShapeFile),
            "dim_styles" => Table(db.DimStyleTableId, r => r.Name == "Standard" || r.ObjectId == db.Dimstyle),
            "blocks" => Table(db.BlockTableId, r => r is BlockTableRecord b && (b.IsLayout || b.IsFromExternalReference || b.IsAnonymous)),
            "mleader_styles" => Dict(db.MLeaderStyleDictionaryId, db.MLeaderstyle),
            "table_styles" => Dict(db.TableStyleDictionaryId, db.Tablestyle),
            _ => Table(db.RegAppTableId, r => r.Name.Equals("ACAD", StringComparison.OrdinalIgnoreCase)),
        };
    }

    private static Dictionary<string, List<(ObjectId Id, string Name)>> Purgeable(Database db, Transaction tr, List<string> kinds, HashSet<string>? names)
    {
        var result = new Dictionary<string, List<(ObjectId, string)>>();
        foreach (var k in kinds)
        {
            var cands = Candidates(db, tr, k).Where(c => !c.Id.IsErased && (names == null || names.Contains(c.Name))).ToList();
            var ids = Cad.Ids(cands.Select(c => c.Id));
            if (ids.Count > 0) db.Purge(ids);
            var ok = ids.Cast<ObjectId>().ToHashSet();
            result[k] = cands.Where(c => ok.Contains(c.Id)).ToList();
        }
        return result;
    }

    private static JsonObject ToJson(Dictionary<string, List<(ObjectId Id, string Name)>> p)
    {
        var o = new JsonObject();
        foreach (var (k, l) in p) o[k] = Hz.Strings(l.Select(x => x.Name));
        return o;
    }

    private static CommandResult Purge(CommandContext ctx)
    {
        var kinds = Kinds(ctx);
        var names = ctx.Args["names"] != null ? Resolve.Strings(ctx.Args["names"]).ToHashSet(StringComparer.OrdinalIgnoreCase) : null;
        var purged = new List<(string Kind, string Name)>();
        return WriteFlow.Run(ctx, "HZ_CLEANUP",
            (doc, tr, plan) =>
            {
                var p = Purgeable(doc.Database, tr, kinds, names);
                var total = p.Values.Sum(l => l.Count);
                if (names != null)
                {
                    var found = p.Values.SelectMany(l => l.Select(x => x.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var notPurgeable = names.Where(n => !found.Contains(n)).ToList();
                    if (notPurgeable.Count > 0)
                        throw new HzRefusal(ErrorCodes.InvalidInput, "Not purgeable now (in use, protected or missing): " + string.Join(", ", notPurgeable) + ". Nothing changed.");
                }
                if (total == 0) throw new HzRefusal(ErrorCodes.InvalidInput, "Nothing is purgeable for kinds " + string.Join(", ", kinds) + ". Nothing changed.");
                plan["first_pass"] = ToJson(p); plan["first_pass_total"] = total;
                plan["note"] = "Further passes may purge items that become unused (nested blocks); each purged name is re-checked.";
            },
            (doc, tr) =>
            {
                for (var pass = 0; pass < 10; pass++)
                {
                    var p = Purgeable(doc.Database, tr, kinds, names);
                    var n = 0;
                    foreach (var (k, l) in p)
                        foreach (var (id, name) in l)
                        {
                            tr.GetObject(id, OpenMode.ForWrite).Erase();
                            purged.Add((k, name));
                            n++;
                        }
                    if (n == 0 || names != null) break;
                }
            },
            (doc, tr, v, after) =>
            {
                var db = doc.Database;
                var remaining = new Dictionary<string, HashSet<string>>();
                foreach (var k in kinds) remaining[k] = Candidates(db, tr, k).Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var (k, n) in purged) if (remaining[k].Contains(n)) v.Flag(k + " '" + n + "' gone", true, false);
                v.Check("items purged and gone", purged.Count, purged.Count(x => !remaining[x.Kind].Contains(x.Name)), purged.All(x => !remaining[x.Kind].Contains(x.Name)) && purged.Count > 0);
                var o = new JsonObject();
                foreach (var g in purged.GroupBy(x => x.Kind)) o[g.Key] = Hz.Strings(g.Select(x => x.Name));
                after["purged"] = o;
                after["still_purgeable"] = Purgeable(db, tr, kinds, null).Values.Sum(l => l.Count);
            });
    }

    // ---- drawing report -----------------------------------------------------------

    private static void Report(CommandContext ctx, Document doc, Transaction tr, JsonObject data)
    {
        var db = doc.Database;
        var limit = (int)(Hz.Num(ctx.Args, "limit") ?? 100);
        var byType = new SortedDictionary<string, int>();
        var byLayer = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var zero = new JsonArray(); var empty = new JsonArray();
        int onZero = 0, onDefpoints = 0, proxies = 0, total = 0;
        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        foreach (ObjectId bid in bt)
        {
            var btr = (BlockTableRecord)tr.GetObject(bid, OpenMode.ForRead);
            if (!btr.IsLayout) continue;
            foreach (ObjectId id in btr)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not Entity e) continue;
                total++;
                var t = id.ObjectClass.DxfName;
                byType[t] = byType.TryGetValue(t, out var n) ? n + 1 : 1;
                byLayer[e.Layer] = byLayer.TryGetValue(e.Layer, out var m) ? m + 1 : 1;
                if (e.Layer == "0") onZero++;
                if (e.Layer.Equals("Defpoints", StringComparison.OrdinalIgnoreCase) && e is not Viewport) onDefpoints++;
                if (e is ProxyEntity) proxies++;
                if (e is Curve && e is not Xline && e is not Ray && Cad.Length(e) is { } len && len < 1e-9 && zero.Count < limit) zero.Add(new JsonObject { ["handle"] = e.Handle.ToString(), ["type"] = t, ["layer"] = e.Layer });
                if ((e is DBText dt && string.IsNullOrWhiteSpace(dt.TextString) || e is MText mt && string.IsNullOrWhiteSpace(mt.Text)) && empty.Count < limit)
                    empty.Add(new JsonObject { ["handle"] = e.Handle.ToString(), ["type"] = t, ["layer"] = e.Layer });
            }
        }
        var hidden = new JsonArray();
        foreach (ObjectId lid in (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead))
        {
            var l = (LayerTableRecord)tr.GetObject(lid, OpenMode.ForRead);
            if ((l.IsFrozen || l.IsOff || l.IsLocked) && byLayer.TryGetValue(l.Name, out var c) && c > 0)
                hidden.Add(new JsonObject { ["layer"] = l.Name, ["objects"] = c, ["frozen"] = l.IsFrozen, ["off"] = l.IsOff, ["locked"] = l.IsLocked });
        }
        var t2 = new JsonObject(); foreach (var kv in byType) t2[kv.Key] = kv.Value;
        var l2 = new JsonObject(); foreach (var kv in byLayer.OrderByDescending(x => x.Value).Take(200)) l2[kv.Key] = kv.Value;
        data["entities"] = total; data["by_type"] = t2; data["by_layer_top200"] = l2;
        data["on_layer_0"] = onZero; data["on_defpoints"] = onDefpoints; data["proxies"] = proxies;
        data["zero_length_curves"] = zero; data["empty_texts"] = empty; data["layers_hidden_or_locked_with_objects"] = hidden;
        data["xrefs"] = Xrefs(db, tr);
        data["purgeable_total"] = Purgeable(db, tr, CadInputs.PurgeKinds.ToList(), null).Values.Sum(x => x.Count);
        data["scope_note"] = "Model and paper space entities; contents of block definitions are not counted.";
    }

    // ---- xrefs ----------------------------------------------------------------------

    private static JsonArray Xrefs(Database db, Transaction tr)
    {
        var rows = new JsonArray();
        var g = db.GetHostDwgXrefGraph(true);
        for (var i = 1; i < g.NumNodes; i++)
        {
            var n = (XrefGraphNode)g.GetXrefNode(i);
            var btr = n.BlockTableRecordId.IsNull ? null : tr.GetObject(n.BlockTableRecordId, OpenMode.ForRead) as BlockTableRecord;
            rows.Add(new JsonObject
            {
                ["name"] = n.Name, ["status"] = n.XrefStatus.ToString(), ["nested"] = n.IsNested,
                ["path"] = btr?.PathName, ["overlay"] = btr?.IsFromOverlayReference,
                ["found_path"] = btr == null ? null : HostApplicationServices.Current.FindFile(btr.PathName, db, FindFileHint.XRefDrawing) is { Length: > 0 } f ? f : null,
                ["references"] = btr?.GetBlockReferenceIds(true, false).Count,
            });
        }
        return rows;
    }

    private static CommandResult XrefReload(CommandContext ctx)
    {
        var names = Resolve.Strings(ctx.Args["names"]);
        var ids = new ObjectIdCollection();
        return WriteFlow.Run(ctx, "HZ_CLEANUP",
            (doc, tr, plan) =>
            {
                var bt = (BlockTable)tr.GetObject(doc.Database.BlockTableId, OpenMode.ForRead);
                foreach (var n in names)
                {
                    if (!bt.Has(n) || !((BlockTableRecord)tr.GetObject(bt[n], OpenMode.ForRead)).IsFromExternalReference)
                        throw new HzRefusal(ErrorCodes.NotFound, "No external reference '" + n + "'. Nothing changed.");
                    ids.Add(bt[n]);
                }
                plan["reload"] = Hz.Strings(names);
            },
            (doc, tr) => doc.Database.ReloadXrefs(ids),
            (doc, tr, v, after) =>
            {
                var rows = Xrefs(doc.Database, tr);
                foreach (var n in names)
                {
                    var row = rows.OfType<JsonObject>().FirstOrDefault(r => string.Equals(Hz.Str(r, "name"), n, StringComparison.OrdinalIgnoreCase));
                    v.Text(n + " status", "Resolved", Hz.Str(row, "status"));
                }
                after["xrefs"] = rows;
            });
    }

    // ---- standards ------------------------------------------------------------------

    private static void Standards(CommandContext ctx, Document doc, Transaction tr, JsonObject data)
    {
        var db = doc.Database;
        JsonObject std;
        if (ctx.Args["standard"] is JsonObject s) std = s;
        else
        {
            var path = Hz.Str(ctx.Args, "standard_path")!;
            if (!RuntimeCompat.IsPathFullyQualified(path) || !File.Exists(path)) throw new HzRefusal(ErrorCodes.NotFound, "standard_path must be an existing absolute .json path.");
            std = JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? throw new HzRefusal(ErrorCodes.InvalidInput, "The standard file is not a JSON object.");
        }
        var issues = new JsonArray();
        var checks = 0;
        void Issue(string kind, string item, string detail) => issues.Add(new JsonObject { ["kind"] = kind, ["item"] = item, ["detail"] = detail });
        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        foreach (var n in std["layers"] as JsonArray ?? new JsonArray())
        {
            if (n is not JsonObject spec || Hz.Str(spec, "name") is not { } name) continue;
            checks++;
            if (!lt.Has(name)) { if (Hz.Bool(spec, "required") != false) Issue("layer_missing", name, "required layer is not in the drawing"); continue; }
            var l = (LayerTableRecord)tr.GetObject(lt[name], OpenMode.ForRead);
            if (spec["color"] is { } c && !Cad.SameColor(Cad.Color(c), l.Color)) Issue("layer_color", name, "expected " + c.ToJsonString() + ", found " + Cad.ColorJson(l.Color).ToJsonString());
            if (Hz.Str(spec, "linetype") is { } ltn)
            {
                var actual = ((LinetypeTableRecord)tr.GetObject(l.LinetypeObjectId, OpenMode.ForRead)).Name;
                if (!actual.Equals(ltn, StringComparison.OrdinalIgnoreCase)) Issue("layer_linetype", name, "expected " + ltn + ", found " + actual);
            }
            if (spec["lineweight"] is { } lw && Cad.Lineweight(lw) != l.LineWeight) Issue("layer_lineweight", name, "expected " + lw.ToJsonString() + ", found " + Cad.LineweightJson(l.LineWeight).ToJsonString());
            if (Hz.Bool(spec, "plot") is { } pl && pl != l.IsPlottable) Issue("layer_plot", name, "expected plot=" + pl);
        }
        foreach (var (key, tableId, what) in new[] { ("text_styles", db.TextStyleTableId, "text style"), ("dim_styles", db.DimStyleTableId, "dimension style") })
        {
            var t = (SymbolTable)tr.GetObject(tableId, OpenMode.ForRead);
            foreach (var n in Resolve.Strings(std[key])) { checks++; if (!t.Has(n)) Issue(key.TrimEnd('s') + "_missing", n, what + " is not in the drawing"); }
        }
        var forbidden = Resolve.Strings(std["forbidden_layers"]).Select(LayersCommand.Wildcard).ToList();
        if (forbidden.Count > 0)
            foreach (ObjectId id in lt)
            {
                checks++;
                var l = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
                if (!l.IsDependent && forbidden.Any(r => r.IsMatch(l.Name))) Issue("layer_forbidden", l.Name, "matches a forbidden pattern");
            }
        if (Hz.Str(std, "layer_pattern") is { } pattern)
        {
            var rx = new System.Text.RegularExpressions.Regex(pattern);
            foreach (ObjectId id in lt)
            {
                var l = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
                if (l.IsDependent || l.Name == "0" || l.Name.Equals("Defpoints", StringComparison.OrdinalIgnoreCase)) continue;
                checks++;
                if (!rx.IsMatch(l.Name)) Issue("layer_name", l.Name, "does not match layer_pattern " + pattern);
            }
        }
        data["checks"] = checks;
        data["issues"] = issues;
        data["passed"] = issues.Count == 0;
        data["fix_hint"] = "Fix layers with horizun_c3d_layers (create/set) and styles with horizun_c3d_cad_styles; this check never edits.";
    }
}
