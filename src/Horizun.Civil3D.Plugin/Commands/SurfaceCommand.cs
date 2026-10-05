// Surface actions, confirmed against 2025 DLLs in AeccDbMgd.phase1-*.txt.
// All batch targets resolve first. Transient volume reads always ABORT.
using System.Text.Json.Nodes;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.DatabaseServices.Styles;
using Horizun.Civil3D.Core;
using Horizun.Civil3D.Plugin.Civil;
using Surface=Autodesk.Civil.DatabaseServices.Surface;

namespace Horizun.Civil3D.Plugin.Commands;

internal sealed partial class SurfaceCommand : ICommand
{
    public string Name=>"surface";
    public CommandResult Execute(CommandContext ctx)
    {
        if(SurfaceInputs.Validate(ctx.Args) is { } why) throw new HzRefusal(ErrorCodes.InvalidInput,why+" Nothing ran.");
        return SurfaceInputs.WriteActions.Contains(ctx.Action!) ? Write(ctx) : Read(ctx);
    }
    private static Surface Open(Transaction tr,ObjectId id,OpenMode mode=OpenMode.ForRead) => tr.GetObject(id,mode) as Surface
        ?? throw new HzRefusal(ErrorCodes.InvalidInput,"The selected object is not a Civil 3D surface.");
    private static List<ObjectId> Resolve(CommandContext ctx,Document doc,Transaction tr)
    {
        var civil=CommandContext.Civil(doc);
        if(Hz.Str(ctx.Args,"handle") is { } h) { var id=Catalog.FromHandle(doc.Database,h); Open(tr,id); return new(){id}; }
        var names=ctx.Args["names"] as JsonArray;
        var selected=names?.Select(n=>n!.GetValue<string>()).ToList() ?? new(){Hz.Str(ctx.Args,"name")!};
        return selected.Select(n=>Catalog.ByName("surface",n,doc.Database,civil,tr)).ToList();
    }
    private static JsonObject Data(CommandContext ctx,Document doc)=>new() {
        ["tool"]=ctx.Tool.Name,["action"]=ctx.Action,["document"]=doc.Name,
        ["units"]=DrawingInfo.Units(CommandContext.Civil(doc),doc.Database)
    };
    private static JsonObject Describe(Surface surface,Transaction tr,bool full)
    {
        var data=Catalog.Describe(surface,tr,new Catalog.Lookup(tr),full);
        var reasons=data["not_editable_because"] as JsonArray ?? new JsonArray();
        try
        {
            data["surface_locked"]=surface.Lock;
            if(surface.Lock)reasons.Add(JsonValue.Create("Surface is locked."));
            if(surface.IsReadOnlyReferenceObject || surface.IsCWSReferenceObject || surface.IsReferenceSubObject)reasons.Add(JsonValue.Create("Read-only, cloud-worksharing or subobject reference."));
        }
        catch(Exception e) { reasons.Add(JsonValue.Create("Editability could not be verified: "+e.GetType().Name+": "+e.Message)); }
        data["editable"]=Hz.Bool(data,"editable")==true && reasons.Count==0;
        if(reasons.Count>0 && reasons.Parent==null)data["not_editable_because"]=reasons;
        return data;
    }
    private static int SamplingBudget(CommandContext ctx,int selected,bool include)
    {
        var total=Hz.Int(ctx.Args,"max_samples")??10000;
        if(include && selected>total)throw new HzRefusal(ErrorCodes.InvalidInput,"max_samples must be at least the number of selected surfaces; the budget is shared by the entire call.");
        return Math.Max(1,total/Math.Max(1,selected));
    }

    private static CommandResult Read(CommandContext ctx)
    {
        var doc=ctx.Document(false);
        var data=Data(ctx,doc);
        return ctx.Read(doc,tr=>
        {
            var civil=CommandContext.Civil(doc);
            if(ctx.Action=="list")
            {
                var ids=Catalog.Ids("surface",doc.Database,civil,tr);
                var offset=Hz.Int(ctx.Args,"offset")??0; var limit=Hz.Int(ctx.Args,"limit")??100;
                var matched=new List<ObjectId>();
                foreach(var id in ids)
                {
                    var s=Open(tr,id);
                    if(Hz.Like(s.Name,Hz.Str(ctx.Args,"name")) && Hz.Like(s.StyleName,Hz.Str(ctx.Args,"style")) && Hz.Like(s.Layer,Hz.Str(ctx.Args,"layer"))) matched.Add(id);
                }
                var items=new JsonArray();
                var selected=matched.Skip(offset).Take(limit).ToList();
                var include=Hz.Bool(ctx.Args,"include_isopaca_statistics")??false;
                var budget=SamplingBudget(ctx,selected.Count,include);
                foreach(var id in selected) items.Add(Info(ctx,Open(tr,id),tr,include,budget));
                data["surfaces"]=items; data["matched"]=matched.Count;data["offset"]=offset;data["returned"]=items.Count;data["has_more"]=offset+items.Count<matched.Count;
            }
            else if(ctx.Action=="volumes_report" && Hz.Str(ctx.Args,"base") is { } baseName)
            {
                var baseId=Catalog.ByName("surface",baseName,doc.Database,civil,tr);
                var compId=Catalog.ByName("surface",Hz.Str(ctx.Args,"comparison")!,doc.Database,civil,tr);
                if(baseId==compId) throw new HzRefusal(ErrorCodes.InvalidInput,"base and comparison resolve to the same surface.");
                var id=TinVolumeSurface.Create("HZ_TRANSIENT_"+Guid.NewGuid().ToString("N"),baseId,compId);
                var surface=Open(tr,id,OpenMode.ForWrite);
                var volume=Volume(surface,tr,ctx.Args);volume.Remove("name");volume.Remove("handle");
                data["volume"]=volume; data["temporary_surface"]=true;
            }
            else
            {
                var ids=Resolve(ctx,doc,tr); // Resolve the entire batch before doing any work.
                var budget=SamplingBudget(ctx,ids.Count,ctx.Action=="get" && (Hz.Bool(ctx.Args,"include_isopaca_statistics")??true));
                var items=new JsonArray();
                foreach(var id in ids)
                {
                    var s=Open(tr,id);
                    if(ctx.Action=="get") items.Add(Info(ctx,s,tr,Hz.Bool(ctx.Args,"include_isopaca_statistics")??true,budget));
                    else if(ctx.Action=="volumes_report") items.Add(Volume(s,tr,ctx.Args));
                    else items.Add(Sample(ctx,s));
                }
                data["surfaces"]=items;
            }
            data["transaction"]=new JsonObject { ["committed"]=false,["disposition"]="always_abort",["note"]="GetVolumeProperties requires ForWrite access; read transactions and temporary surfaces are aborted, never committed." };
            return CommandResult.Ok(data);
        });
    }
    private static JsonObject Info(CommandContext ctx,Surface s,Transaction tr,bool isopaca,int budget)
    {
        var info=Describe(s,tr,true);
        if(s is TinVolumeSurface or GridVolumeSurface)
        {
            try
            {
                info["volume"]=Volume(s,tr,new JsonObject());
            }
            catch(Exception e) { info["volume"]=null; info["volume_unreadable_reason"]=e.GetType().Name+": "+e.Message; }
            if(isopaca && info["volume"] is JsonObject native)
            {
                try { info["isopaca_statistics"]=Statistics(ctx,s,native,budget); }
                catch(HzRefusal) when(ctx.Args["grid_spacing"]!=null) { throw; }
                catch(Exception e) { info["isopaca_statistics"]=null;info["isopaca_unreadable_reason"]=e.GetType().Name+": "+e.Message; }
            }
        }
        else if(isopaca) info["isopaca_note"]="Cut/fill depths are defined only for volume surfaces; this is an elevation surface.";
        return info;
    }
    internal static JsonObject Volume(Surface s,Transaction tr,JsonObject args)
    {
        if(s is not TinVolumeSurface && s is not GridVolumeSurface)
            throw new HzRefusal(ErrorCodes.InvalidInput,"'"+s.Name+"' is not a volume surface. Supply base and comparison to compute a transient volume.");
        if(!s.IsWriteEnabled) s.UpgradeOpen();
        var v=s is TinVolumeSurface tv ? tv.GetVolumeProperties() : ((GridVolumeSurface)s).GetVolumeProperties();
        var data=new JsonObject {
            ["name"]=s.Name,["handle"]=s.Handle.ToString(),["is_out_of_date"]=s.IsOutOfDate,
            ["base"]=Catalog.NameOf(v.BaseSurface,tr),["comparison"]=Catalog.NameOf(v.ComparisonSurface,tr),
            ["base_handle"]=v.BaseSurface.Handle.ToString(),["comparison_handle"]=v.ComparisonSurface.Handle.ToString(),
            ["unadjusted_cut"]=Hz.Finite(v.UnadjustedCutVolume),["unadjusted_fill"]=Hz.Finite(v.UnadjustedFillVolume),["unadjusted_net"]=Hz.Finite(v.UnadjustedNetVolume),
            ["native_adjusted_cut"]=Hz.Finite(v.AdjustedCutVolume),["native_adjusted_fill"]=Hz.Finite(v.AdjustedFillVolume),["native_adjusted_net"]=Hz.Finite(v.AdjustedNetVolume),
            ["native_cut_factor"]=Hz.Finite(v.CutFactor),["native_fill_factor"]=Hz.Finite(v.FillFactor),["source"]="Civil3D.GetVolumeProperties",
            ["area_2d"]=null,["area_unreadable_reason"]="The probed volume-surface API does not expose an exact 2D area; get with include_isopaca_statistics returns grid-estimated cut/fill areas.",
            ["native_net_convention"]="returned_by_Civil3D",["note"]="Volumes describe the current surface; an out-of-date surface is not silently rebuilt."
        };
        var cf=Hz.Num(args,"cut_factor")??v.CutFactor;var ff=Hz.Num(args,"fill_factor")??v.FillFactor;
        data["requested_factors"]=new JsonObject { ["cut_factor"]=Hz.Finite(cf),["fill_factor"]=Hz.Finite(ff),["cut"]=Hz.Finite(v.UnadjustedCutVolume*cf),["fill"]=Hz.Finite(v.UnadjustedFillVolume*ff),["net"]=Hz.Finite(v.UnadjustedFillVolume*ff-v.UnadjustedCutVolume*cf),["net_convention"]="fill_minus_cut",["applied_to_surface"]=false };
        if(new[]{v.UnadjustedCutVolume,v.UnadjustedFillVolume,v.UnadjustedNetVolume,v.AdjustedCutVolume,v.AdjustedFillVolume,v.AdjustedNetVolume,cf,ff,v.UnadjustedCutVolume*cf,v.UnadjustedFillVolume*ff}.Any(x=>!double.IsFinite(x)))
            data["unreadable_reason"]="One or more native values/factors are non-finite; those fields are null, not zero.";
        return data;
    }
    private static JsonObject Statistics(CommandContext ctx,Surface s,JsonObject native,int budget)
    {
        var gp=s.GetGeneralProperties();
        var (cells,spacing)=SurfaceMath.Grid(gp.MinimumCoordinateX,gp.MinimumCoordinateY,gp.MaximumCoordinateX,gp.MaximumCoordinateY,Hz.Num(ctx.Args,"grid_spacing"),budget);
        var samples=new SurfaceSamples();var outside=0;var unreadable=0;var reasons=new HashSet<string>();
        foreach(var cell in cells)
        {
            try { samples.Add(s.FindElevationAtXY(cell.X,cell.Y),cell.Area); }
            catch(PointNotOnEntityException) { outside++; }
            catch(Exception e) { unreadable++;if(reasons.Count<5) reasons.Add(e.GetType().Name+": "+e.Message); }
        }
        var stats=samples.ToJson();stats["grid_spacing"]=spacing;stats["attempted_samples"]=cells.Count;stats["outside_samples"]=outside;stats["unreadable_samples"]=unreadable;
        stats["unreadable_reasons"]=Hz.Strings(reasons);
        stats["max_cut_depth_native"]=Hz.Finite(Math.Max(0,-gp.MinimumElevation));stats["max_fill_height_native"]=Hz.Finite(Math.Max(0,gp.MaximumElevation));
        stats["extrema_source"]="Civil3D.GetGeneralProperties (volume elevation = comparison - base)";
        if(!double.IsFinite(gp.MinimumElevation) || !double.IsFinite(gp.MaximumElevation)) stats["native_extrema_unreadable_reason"]="Civil 3D returned non-finite minimum/maximum elevations.";
        foreach(var side in new[]{"cut","fill"})
        {
            var area=Hz.Num(stats,side+"_area_estimated");var volume=Hz.Num(native,"unadjusted_"+side);
            stats[side+"_native_volume_over_estimated_area"]=(area>0 && volume.HasValue)?Hz.Finite(volume.Value/area.Value):null;
            stats[side+"_native_volume_over_estimated_area_note"]=(area>0 && volume.HasValue)?"Native Civil 3D volume divided by grid-estimated area; this mean remains estimated.":"No positive sampled area or readable native volume on this side.";
        }
        var checks=new JsonArray();
        foreach(var side in new[]{"cut","fill"})
            if(Hz.Num(native,"unadjusted_"+side) is { } a && Hz.Num(stats,side+"_volume_estimated") is { } b)
                checks.Add(Reconcile.Compare(side+" volume",a,"civil3d",b,"grid_estimate",.5));
        stats["reconciliation"]=checks;
        return stats;
    }
    private static JsonObject Sample(CommandContext ctx,Surface s)
    {
        var points=ctx.Args["points"] is JsonArray p ? p.Cast<JsonObject>().Select(v=>new SurfaceXY(Hz.Num(v,"x")!.Value,Hz.Num(v,"y")!.Value)).ToList()
            : SurfaceMath.AlongLine((JsonObject)ctx.Args["line"]!,Hz.Num(ctx.Args,"step")!.Value);
        var rows=new JsonArray();var outside=0;var unreadable=0;
        foreach(var point in points)
        {
            var row=new JsonObject {["x"]=point.X,["y"]=point.Y};
            try { var z=s.FindElevationAtXY(point.X,point.Y); row["elevation"]=Hz.Finite(z); if(!double.IsFinite(z)){ row["status"]="unreadable";row["reason"]="Civil 3D returned a non-finite elevation.";unreadable++; }else row["status"]="measured"; }
            catch(PointNotOnEntityException){row["elevation"]=null;row["status"]="outside_surface";row["reason"]="Point lies outside the surface or inside a hole.";outside++;}
            catch(Exception e){row["elevation"]=null;row["status"]="unreadable";row["reason"]=e.GetType().Name+": "+e.Message;unreadable++;}
            rows.Add(row);
        }
        return new JsonObject {["name"]=s.Name,["handle"]=s.Handle.ToString(),["coordinate_system"]="drawing WCS, no coordinate conversion",["points"]=rows,["outside_count"]=outside,["unreadable_count"]=unreadable,["is_out_of_date"]=s.IsOutOfDate};
    }

    internal static ObjectId Style(Document doc,Transaction tr,string name)
    {
        var ids=new List<ObjectId>();var names=new List<string>();
        foreach(ObjectId id in CommandContext.Civil(doc).Styles.SurfaceStyles) { var s=(SurfaceStyle)tr.GetObject(id,OpenMode.ForRead);names.Add(s.Name);if(string.Equals(s.Name,name,StringComparison.OrdinalIgnoreCase))ids.Add(id); }
        if(ids.Count!=1) throw new HzRefusal(ids.Count==0?ErrorCodes.NotFound:ErrorCodes.Ambiguous,"Surface style '"+name+"' must resolve uniquely. Nothing changed.",new JsonObject{["candidates"]=Hz.Strings(names)});
        return ids[0];
    }
    internal static void UniqueSurface(Document doc,Transaction tr,string name)
    {
        foreach(ObjectId id in CommandContext.Civil(doc).GetSurfaceIds()) if(string.Equals(Open(tr,id).Name,name,StringComparison.OrdinalIgnoreCase))
            throw new HzRefusal(ErrorCodes.InvalidInput,"A surface named '"+name+"' already exists. Existing/base surfaces are never overwritten.");
    }
    private static void Editable(Surface s,Transaction tr)
    {
        var layer=(LayerTableRecord)tr.GetObject(s.LayerId,OpenMode.ForRead);
        if(s.IsReferenceObject || s.IsReadOnlyReferenceObject || s.IsCWSReferenceObject || s.IsReferenceSubObject || s.Lock || layer.IsLocked)
            throw new HzRefusal(ErrorCodes.NotEditable,"'"+s.Name+"' is a reference, locked surface or on a locked layer. Nothing changed.");
    }
    private static JsonObject DisplaySnapshot(SurfaceStyle style)
    {
        var settings=new JsonObject();
        foreach(var component in Enum.GetValues<SurfaceDisplayStyleType>())
        {
            var views=new JsonObject();
            foreach(var view in new[]{"plan","model"})
            {
                var d=view=="plan"?style.GetDisplayStylePlan(component):style.GetDisplayStyleModel(component);
                var color=d.Color;var method=color.ColorMethod.ToString();
                var c=new JsonObject {["method"]=method,["index"]=color.ColorIndex};
                if(method=="ByColor") { c["red"]=color.Red;c["green"]=color.Green;c["blue"]=color.Blue; }
                if(!double.IsFinite(d.LinetypeScale))throw new HzRefusal(ErrorCodes.NotEditable,"Source style has an unreadable linetype scale; no duplicate was created.");
                views[view]=new JsonObject{["visible"]=d.Visible,["color"]=c,["layer"]=d.Layer,["linetype"]=d.Linetype,["linetype_scale"]=d.LinetypeScale,["lineweight"]=d.Lineweight.ToString(),["plot_style"]=d.PlotStyle};
            }
            settings[component.ToString()]=views;
        }
        return settings;
    }
    private static CommandResult Write(CommandContext ctx)
    {
        if(ctx.Action is "add_data" or "paste") return Geometry(ctx);
        if(ctx.Action is "apply_elevation_analysis" or "apply_slope_analysis" or "style_display") return Analysis(ctx);
        var doc=ctx.Document(true);
        if(doc.IsReadOnly) throw new HzRefusal(ErrorCodes.ReadOnlyDocument,"The drawing is read-only. Nothing changed.");
        // One lock spans plan capture, confirmation, commit and verification.
        using var operationLock=doc.LockDocument(DocumentLockMode.Write,"HZ_SURFACE","HZ_SURFACE",false);
        var data=Data(ctx,doc);var plan=new JsonObject {["action"]=ctx.Action,["drawing_revision"]=DrawingRevision.Capture(doc)};
        var ids=new List<ObjectId>();ObjectId style=ObjectId.Null,baseId=ObjectId.Null,compId=ObjectId.Null,layerId=ObjectId.Null;
        var newName=Hz.Str(ctx.Args,"new_name");
        ctx.Read(doc,tr=>
        {
            if(ctx.Action is "rename" or "set_style" or "rebuild")
            {
                ids=Resolve(ctx,doc,tr);var before=new JsonArray();
                foreach(var id in ids) { var s=Open(tr,id);Editable(s,tr);before.Add(new JsonObject{["handle"]=s.Handle.ToString(),["name"]=s.Name,["style_handle"]=s.StyleId.Handle.ToString(),["fingerprint"]=s.ComputeFingerPrint().ToString(),["is_out_of_date"]=s.IsOutOfDate}); }
                plan["surfaces_before"]=before;
            }
            if(ctx.Action is "rename" or "create_tin" or "create_volume") { UniqueSurface(doc,tr,newName!);plan["new_name"]=newName; }
            if(ctx.Action is "set_style" or "duplicate_style" or "create_tin" or "create_volume")
            {
                style=Style(doc,tr,Hz.Str(ctx.Args,"style")!);var source=(SurfaceStyle)tr.GetObject(style,OpenMode.ForRead);
                plan["style"]=new JsonObject {["name"]=source.Name,["handle"]=source.Handle.ToString()};
                var usage=StylesCommand.Usage("surface",doc.Database,CommandContext.Civil(doc),tr,out _)!;
                var users=usage.TryGetValue(style,out var list)?list:new List<JsonObject>();
                plan["style_used_by"]=Hz.Arr(users.Select(n=>(JsonNode?)n.DeepClone()));
                plan["style_note"]="Assigning this style does not edit its definition. Duplicate a shared style before changing its display or analysis settings.";
            }
            if(ctx.Action=="duplicate_style")
            {
                foreach(ObjectId id in CommandContext.Civil(doc).Styles.SurfaceStyles) if(string.Equals(((SurfaceStyle)tr.GetObject(id,OpenMode.ForRead)).Name,newName,StringComparison.OrdinalIgnoreCase)) throw new HzRefusal(ErrorCodes.InvalidInput,"The new style name already exists. Nothing changed.");
                plan["new_name"]=newName;
                plan["source_display_settings"]=DisplaySnapshot((SurfaceStyle)tr.GetObject(style,OpenMode.ForRead));
            }
            if(ctx.Action=="create_volume")
            {
                var civil=CommandContext.Civil(doc);
                baseId=Catalog.ByName("surface",Hz.Str(ctx.Args,"base")!,doc.Database,civil,tr);compId=Catalog.ByName("surface",Hz.Str(ctx.Args,"comparison")!,doc.Database,civil,tr);
                if(baseId==compId)throw new HzRefusal(ErrorCodes.InvalidInput,"Base and comparison resolve to the same surface.");
                plan["base"]=Describe(Open(tr,baseId),tr,false);plan["comparison"]=Describe(Open(tr,compId),tr,false);
            }
            if(Hz.Str(ctx.Args,"layer") is { } layer)
            {
                var table=(LayerTable)tr.GetObject(doc.Database.LayerTableId,OpenMode.ForRead);
                if(!table.Has(layer))throw new HzRefusal(ErrorCodes.NotFound,"Layer '"+layer+"' does not exist. Nothing changed.");
                layerId=table[layer];var l=(LayerTableRecord)tr.GetObject(layerId,OpenMode.ForRead);
                if(l.IsLocked)throw new HzRefusal(ErrorCodes.NotEditable,"Destination layer is locked. Nothing changed.");
                plan["layer"]=l.Name;plan["layer_handle"]=l.Handle.ToString();
            }
            if(ctx.Action is "create_tin" or "create_volume") plan["description"]=Hz.Str(ctx.Args,"description")??"";
            return 0;
        });
        if(ctx.DryRun)return CommandResult.Ok(ctx.Rehearse(doc,data,plan));
        ctx.RequireConfirmation(doc,plan);
        ObjectId created=ObjectId.Null;
        ctx.Write(doc,"HZ_SURFACE",tr=>
        {
            if(ctx.Action=="duplicate_style")created=((SurfaceStyle)tr.GetObject(style,OpenMode.ForWrite)).CopyAsSibling(newName!);
            else if(ctx.Action is "create_tin" or "create_volume")
            {
                created=ctx.Action=="create_tin"?TinSurface.Create(newName!,style):TinVolumeSurface.Create(newName!,baseId,compId,style);
                var s=Open(tr,created,OpenMode.ForWrite);s.Description=Hz.Str(ctx.Args,"description")??"";if(!layerId.IsNull)s.LayerId=layerId;
            }
            else foreach(var id in ids) { var s=Open(tr,id,OpenMode.ForWrite);if(ctx.Action=="rename")s.Name=newName!;else if(ctx.Action=="set_style")s.StyleId=style;else s.Rebuild(); }
            return 0;
        });
        var checks=new VerificationSet();var actual=new JsonArray();
        try
        {
            ctx.Verify(doc,tr=>
            {
                if(ctx.Action=="duplicate_style")
                {
                    var found=Style(doc,tr,newName!);var s=(SurfaceStyle)tr.GetObject(found,OpenMode.ForRead);
                    checks.Text("new style name",newName,s.Name,false);checks.Text("created style handle",created.Handle.ToString(),found.Handle.ToString(),false);
                    var snapshot=DisplaySnapshot(s);
                    checks.Check("copied plan/model display settings",plan["source_display_settings"],snapshot,Hz.Canonical(plan["source_display_settings"])==Hz.Canonical(snapshot));
                    actual.Add(new JsonObject{["name"]=s.Name,["handle"]=s.Handle.ToString(),["display_settings"]=snapshot});
                }
                else foreach(var id in created.IsNull?ids:new List<ObjectId>{created})
                {
                    var s=Open(tr,id);var row=Describe(s,tr,false);
                    if(ctx.Action is "rename" or "create_tin" or "create_volume")checks.Text(id.Handle+" name",newName,s.Name,false);
                    if(ctx.Action is "set_style" or "create_tin" or "create_volume")checks.Text(id.Handle+" style handle",style.Handle.ToString(),s.StyleId.Handle.ToString(),false);
                    if(ctx.Action is "create_tin" or "create_volume")
                    {
                        checks.Flag("created surface kind",true,ctx.Action=="create_tin"?s is TinSurface:s is TinVolumeSurface);
                        checks.Text("description",Hz.Str(ctx.Args,"description")??"",s.Description,false);
                        if(!layerId.IsNull)checks.Text("layer handle",layerId.Handle.ToString(),s.LayerId.Handle.ToString(),false);
                        if(s is TinVolumeSurface)
                        {
                            var volumes=Volume(s,tr,new JsonObject());row["volume"]=volumes;
                            checks.Text("base surface handle",baseId.Handle.ToString(),Hz.Str(volumes,"base_handle"),false);
                            checks.Text("comparison surface handle",compId.Handle.ToString(),Hz.Str(volumes,"comparison_handle"),false);
                            checks.Flag("volumes readable",true,new[]{"unadjusted_cut","unadjusted_fill","unadjusted_net"}.All(k=>Hz.Num(volumes,k) is { } v && double.IsFinite(v)));
                        }
                    }
                    if(ctx.Action=="rebuild")checks.Flag(id.Handle+" is_out_of_date",false,s.IsOutOfDate);
                    actual.Add(row);
                }
                return 0;
            });
        }
        catch(Exception e) { checks.Check("post-commit re-read",true,null,false,e.GetType().Name+": "+e.Message); }
        data["dry_run"]=false;data["committed"]=true;data["plan"]=plan;data["actual"]=actual;data["verified"]=checks.ToJson();
        data["undo"]=new JsonObject {["label"]="HZ_SURFACE",["instruction"]="Use one UNDO step in Civil 3D to undo this committed batch; no drawing was saved."};
        return checks.AllVerified?CommandResult.Ok(data):CommandResult.Fail(ErrorCodes.VerificationFailed,"The transaction committed but the re-read did not verify every requested change. Inspect actual/verified before retrying.",data);
    }
}
