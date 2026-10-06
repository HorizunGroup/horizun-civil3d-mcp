// API: docs/api-probes/2025/AeccPressurePipesMgd.audit-network.txt.
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Horizun.Civil3D.Core;
using Horizun.Civil3D.Plugin.Civil;

namespace Horizun.Civil3D.Plugin.Commands;

internal static class PressureNetworks
{
    public static CommandResult Execute(CommandContext ctx)
    {
        // Load explicitly before JIT-compiling methods that name pressure types,
        // as is already done for the optional data-shortcut assembly.
        if (!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "AeccPressurePipesMgd"))
        {
            var path = Path.Combine(Path.GetDirectoryName(typeof(CivilDocument).Assembly.Location)!, "AeccPressurePipesMgd.dll");
            if (!File.Exists(path)) throw new HzRefusal(ErrorCodes.Unsupported, "The installed Civil 3D pressure-network API assembly is unavailable. Nothing ran.");
            System.Reflection.Assembly.LoadFrom(path);
        }
        return PressureApi.Execute(ctx);
    }
}

internal static class PressureApi
{
    private static ObjectIdCollection Ids(Document doc) =>
        CivilDocumentPressurePipesExtension.GetPressurePipeNetworkIds(CommandContext.Civil(doc));

    private static ObjectId Named(Document doc, Transaction tr, string name, bool mustExist)
    {
        var matches = new List<ObjectId>();
        var names = new List<string>();
        foreach (ObjectId id in Ids(doc))
        {
            var net = (PressurePipeNetwork)tr.GetObject(id, OpenMode.ForRead);
            names.Add(net.Name);
            if (string.Equals(net.Name, name, StringComparison.OrdinalIgnoreCase)) matches.Add(id);
        }
        if (!mustExist)
        {
            if (matches.Count > 0) throw new HzRefusal(ErrorCodes.InvalidInput, "A pressure network named '" + name + "' already exists. Nothing changed.");
            return ObjectId.Null;
        }
        if (matches.Count == 1) return matches[0];
        throw new HzRefusal(matches.Count == 0 ? ErrorCodes.NotFound : ErrorCodes.Ambiguous,
            "Pressure network '" + name + "' is missing or ambiguous. Nothing ran.", new JsonObject { ["candidates"] = Hz.Strings(names) });
    }

    private static JsonObject NetworkJson(PressurePipeNetwork n)
    {
        var o = new JsonObject { ["handle"] = n.Handle.ToString() };
        var u = new JsonObject();
        Safe.Str(o, u, "name", () => n.Name);
        Safe.Str(o, u, "parts_list", () => n.PartsListName);
        Safe.Str(o, u, "reference_surface", () => n.ReferenceSurfaceName);
        Safe.Int(o, u, "pipes", () => n.GetPipeIds().Count);
        Safe.Int(o, u, "fittings", () => n.GetFittingIds().Count);
        Safe.Int(o, u, "appurtenances", () => n.GetAppurtenanceIds().Count);
        Safe.Flag(o, u, "is_reference", () => n.IsReferenceObject);
        if (Hz.Bool(o, "is_reference") == true)
        {
            Safe.Flag(o, u, "reference_valid", () => n.IsReferenceValid);
            Safe.Flag(o, u, "reference_stale", () => n.IsReferenceStale);
        }
        if (u.Count > 0) o["unreadable"] = u;
        return o;
    }

    private static JsonObject PartJson(PressurePart p)
    {
        var o = new JsonObject { ["handle"] = p.Handle.ToString(), ["class"] = p.GetType().Name };
        var u = new JsonObject();
        Safe.Str(o, u, "name", () => p.Name);
        Safe.Str(o, u, "family", () => p.PartFamilyName);
        Safe.Str(o, u, "description", () => p.PartDescription);
        Safe.Int(o, u, "connection_count", () => p.ConnectionCount);
        try { o["position"] = Resolve.Json(p.Position); }
        catch (Exception e) { o["position"] = null; u["position"] = e.Message; }
        if (p is PressurePipe pipe)
        {
            try { o["start"] = Resolve.Json(pipe.StartPoint); o["end"] = Resolve.Json(pipe.EndPoint); }
            catch (Exception e) { o["start"] = null; o["end"] = null; u["endpoints"] = e.Message; }
            Safe.Num(o, u, "length_2d", () => pipe.Length2DCenterToCenter);
            Safe.Num(o, u, "length_3d", () => pipe.Length3DCenterToCenter);
            if (pipe.ReferenceSurfaceId.IsNull)
            {
                o["minimum_cover"] = null; o["maximum_cover"] = null;
                u["cover"] = "No reference surface is assigned to this pressure pipe.";
            }
            else
            {
                Safe.Num(o, u, "minimum_cover", () => pipe.MinimumCover);
                Safe.Num(o, u, "maximum_cover", () => pipe.MaximumCover);
            }
            Safe.Num(o, u, "nominal_diameter_api_raw", () => pipe.NominalDiameter);
            Safe.Num(o, u, "inner_diameter_api_raw", () => pipe.InnerDiameter);
            o["diameter_units"] = "API-native values; unit interpretation for the selected parts catalog has not been established.";
        }
        var ports = new JsonArray();
        try
        {
            for (var i = 0; i < p.ConnectionCount; i++)
            {
                var port = new JsonObject { ["index"] = i };
                try
                {
                    using var connection = p.GetConnectionAt(i);
                    port["open"] = connection.Open;
                    port["connected_handle"] = connection.ConnectedId.IsNull ? null : connection.ConnectedId.Handle.ToString();
                    port["connected_port"] = connection.ConnectedId.IsNull ? null : connection.ConnectedIndex;
                }
                catch (Exception e) { port["unreadable"] = e.GetType().Name + ": " + e.Message; }
                ports.Add(port);
            }
        }
        catch (Exception e) { u["connections"] = e.Message; }
        o["connections"] = ports;
        if (u.Count > 0) o["unreadable"] = u;
        return o;
    }

    private static void Read(CommandContext ctx, Document doc, Transaction tr, JsonObject data)
    {
        var limit = (int)(Hz.Num(ctx.Args, "limit") ?? 100);
        var offset = (int)(Hz.Num(ctx.Args, "offset") ?? 0);
        if (ctx.Action == "pressure_list")
        {
            var all = Ids(doc).Cast<ObjectId>().ToList();
            data["total"] = all.Count;
            data["networks"] = new JsonArray(all.Skip(offset).Take(limit).Select(id =>
                (JsonNode)NetworkJson((PressurePipeNetwork)tr.GetObject(id, OpenMode.ForRead))).ToArray());
            data["has_more"] = offset + limit < all.Count;
        }
        else
        {
            var net = (PressurePipeNetwork)tr.GetObject(Named(doc, tr, Hz.Str(ctx.Args, "network")!, true), OpenMode.ForRead);
            data["network"] = NetworkJson(net);
            foreach (var group in new[] { ("pipes", net.GetPipeIds()), ("fittings", net.GetFittingIds()), ("appurtenances", net.GetAppurtenanceIds()) })
            {
                var items = new JsonArray();
                foreach (ObjectId id in group.Item2.Cast<ObjectId>().Skip(offset).Take(limit))
                {
                    try { items.Add(PartJson((PressurePart)tr.GetObject(id, OpenMode.ForRead))); }
                    catch (Exception e) { items.Add(new JsonObject { ["handle"] = id.Handle.ToString(), ["unreadable"] = e.GetType().Name + ": " + e.Message }); }
                }
                data[group.Item1] = new JsonObject { ["total"] = group.Item2.Count, ["returned"] = items.Count,
                    ["has_more"] = offset + items.Count < group.Item2.Count, ["items"] = items };
            }
        }
        data["offset"] = offset;
        data["limit"] = limit;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static CommandResult Execute(CommandContext ctx)
    {
        if (ctx.Action is "pressure_list" or "pressure_get")
            return WriteFlow.Read(ctx, (doc, tr, data) => Read(ctx, doc, tr, data));
        var name = Hz.Str(ctx.Args, "new_name")!;
        var rename = ctx.Action == "pressure_rename";
        ObjectId id = ObjectId.Null, surface = ObjectId.Null, layer = ObjectId.Null;
        return WriteFlow.Run(ctx, "HZ_PRESSURE",
            (doc, tr, plan) =>
            {
                Named(doc, tr, name, false);
                if (rename)
                {
                    id = Named(doc, tr, Hz.Str(ctx.Args, "network")!, true);
                    var existing = (PressurePipeNetwork)tr.GetObject(id, OpenMode.ForRead);
                    if (existing.IsReferenceObject)
                        throw new HzRefusal(ErrorCodes.NotEditable, "The pressure network is a data reference; rename it in its source drawing. Nothing changed.");
                    Resolve.Editable(existing, tr);
                    plan["network_handle"] = id.Handle.ToString();
                }
                else
                {
                    if (Hz.Str(ctx.Args, "surface") is { } s) surface = Resolve.Named(doc, tr, "surface", s);
                    if (Hz.Str(ctx.Args, "layer") is { } l) layer = Resolve.Layer(doc.Database, tr, l);
                    plan["reference_surface_handle"] = surface.IsNull ? null : surface.Handle.ToString();
                    plan["layer_handle"] = layer.IsNull ? null : layer.Handle.ToString();
                    plan["parts"] = 0;
                }
                plan["new_name"] = name;
            },
            (doc, tr) =>
            {
                if (!rename) id = PressurePipeNetwork.Create(doc.Database, name);
                var net = (PressurePipeNetwork)tr.GetObject(id, OpenMode.ForWrite);
                if (rename) net.Name = name;
                if (!surface.IsNull) net.ReferenceSurfaceId = surface;
                if (!layer.IsNull) net.LayerId = layer;
            },
            (doc, tr, checks, after) =>
            {
                var net = (PressurePipeNetwork)tr.GetObject(id, OpenMode.ForRead);
                checks.Text("pressure network name", name, net.Name);
                checks.Flag("network appears in Civil document collection", true, Ids(doc).Cast<ObjectId>().Contains(id));
                if (!rename)
                {
                    checks.Check("empty pressure network", 0, net.GetPipeIds().Count + net.GetFittingIds().Count + net.GetAppurtenanceIds().Count,
                        net.GetPipeIds().Count == 0 && net.GetFittingIds().Count == 0 && net.GetAppurtenanceIds().Count == 0);
                    if (!surface.IsNull) checks.Flag("reference surface", true, net.ReferenceSurfaceId == surface);
                    if (!layer.IsNull) checks.Flag("layer", true, net.LayerId == layer);
                }
                after["network"] = NetworkJson(net);
            });
    }
}
