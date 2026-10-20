// -----------------------------------------------------------------------------
// horizun_c3d_query - list / get Civil 3D objects of one type.
// -----------------------------------------------------------------------------
using System.Text.Json.Nodes;
using Autodesk.AutoCAD.DatabaseServices;
using Horizun.Civil3D.Core;
using Horizun.Civil3D.Plugin.Civil;

namespace Horizun.Civil3D.Plugin.Commands;

internal sealed class QueryCommand : ICommand
{
    public string Name => "query";

    public CommandResult Execute(CommandContext ctx) =>
        ctx.RequireAction("list", "get") == "list" ? List(ctx) : Get(ctx);

    private static string RequireType(CommandContext ctx)
    {
        var type = Hz.Str(ctx.Args, "type");
        if (type == null || !Catalog.Types.Contains(type))
            throw new HzRefusal(ErrorCodes.InvalidInput, "type is required: one of " + string.Join(", ", Catalog.Types) + ".");
        return type;
    }

    private static CommandResult List(CommandContext ctx)
    {
        var type = RequireType(ctx);
        var doc = ctx.Document(forWrite: false);
        var nameF = Hz.Str(ctx.Args, "name");
        var layerF = Hz.Str(ctx.Args, "layer");
        var styleF = Hz.Str(ctx.Args, "style");
        var offset = Math.Max(0, Hz.Int(ctx.Args, "offset") ?? 0);
        var limit = RuntimeCompat.Clamp(Hz.Int(ctx.Args, "limit") ?? 100, 1, 1000);
        var fields = (ctx.Args["fields"] as JsonArray)?.Select(n => n?.GetValue<string>()).Where(s => s != null).Select(s => s!).ToHashSet();

        var data = new JsonObject { ["type"] = type, ["document"] = doc.Name };
        ctx.Read(doc, tr =>
        {
            var civil = CommandContext.Civil(doc);
            data["units"] = DrawingInfo.Units(civil, doc.Database);
            var lk = new Catalog.Lookup(tr);
            var ids = Catalog.Ids(type, doc.Database, civil, tr);
            var matched = 0;
            var items = new JsonArray();
            foreach (var id in ids)
            {
                var obj = tr.GetObject(id, OpenMode.ForRead);
                var d = Catalog.Describe(obj, tr, lk, full: false);
                if (!Hz.Like(Hz.Str(d, "name"), nameF) || !Hz.Like(Hz.Str(d, "layer"), layerF) || !Hz.Like(Hz.Str(d, "style"), styleF))
                    continue;
                matched++;
                if (matched <= offset || items.Count >= limit) continue;
                items.Add(fields == null ? d : Project(d, fields));
            }
            data["total_of_type"] = ids.Count;
            data["matched"] = matched;
            data["offset"] = offset;
            data["returned"] = items.Count;
            data["has_more"] = offset + items.Count < matched;
            data["items"] = items;
            return 0;
        });
        return CommandResult.Ok(data);
    }

    private static JsonObject Project(JsonObject d, HashSet<string> fields)
    {
        var o = new JsonObject { ["handle"] = d["handle"]?.DeepClone(), ["name"] = d["name"]?.DeepClone() };
        foreach (var f in fields)
            if (d.TryGetPropertyValue(f, out var v)) o[f] = v?.DeepClone();
        return o;
    }

    private static CommandResult Get(CommandContext ctx)
    {
        var doc = ctx.Document(forWrite: false);
        var handle = Hz.Str(ctx.Args, "handle");
        var name = Hz.Str(ctx.Args, "name");
        var type = Hz.Str(ctx.Args, "type");
        if (string.IsNullOrWhiteSpace(handle) && (string.IsNullOrWhiteSpace(name) || type == null))
            throw new HzRefusal(ErrorCodes.InvalidInput, "get needs a handle, or a type plus an exact name.");

        var data = new JsonObject { ["document"] = doc.Name };
        ctx.Read(doc, tr =>
        {
            var civil = CommandContext.Civil(doc);
            data["units"] = DrawingInfo.Units(civil, doc.Database);
            ObjectId id;
            if (!string.IsNullOrWhiteSpace(handle))
            {
                id = Catalog.FromHandle(doc.Database, handle);
            }
            else
            {
                if (!Catalog.Types.Contains(type!))
                    throw new HzRefusal(ErrorCodes.InvalidInput, "type must be one of " + string.Join(", ", Catalog.Types) + ".");
                id = Catalog.ByName(type!, name!, doc.Database, civil, tr);
            }
            var obj = tr.GetObject(id, OpenMode.ForRead);
            var actualType = Catalog.TypeOf(obj);
            if (type != null && !string.IsNullOrWhiteSpace(handle) && actualType != type)
                throw new HzRefusal(ErrorCodes.InvalidInput,
                    "Handle " + handle!.ToUpperInvariant() + " is a " + actualType + " (" + obj.GetType().Name + "), not a " + type + ".");
            data["object"] = Catalog.Describe(obj, tr, new Catalog.Lookup(tr), full: true);
            return 0;
        });
        return CommandResult.Ok(data);
    }
}
