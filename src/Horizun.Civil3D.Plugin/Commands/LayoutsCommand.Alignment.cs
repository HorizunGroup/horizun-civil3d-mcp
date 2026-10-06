using System.Text.Json.Nodes;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.DatabaseServices;
using Horizun.Civil3D.Core;
using Horizun.Civil3D.Plugin.Civil;

namespace Horizun.Civil3D.Plugin.Commands;

internal sealed partial class LayoutsCommand
{
    private const string AlignmentLinkKey="Horizun.AlignmentSheet.v1";

    private static JsonObject ReadSheetLink(Viewport vp, Transaction tr)
    {
        if(vp.ExtensionDictionary.IsNull) throw new HzRefusal(ErrorCodes.InvalidInput,"Viewport has no Horizun alignment link.");
        var d=(DBDictionary)tr.GetObject(vp.ExtensionDictionary,OpenMode.ForRead);
        if(!d.Contains(AlignmentLinkKey)) throw new HzRefusal(ErrorCodes.InvalidInput,"Viewport has no Horizun alignment link.");
        var record=(Xrecord)tr.GetObject(d.GetAt(AlignmentLinkKey),OpenMode.ForRead);
        using var buffer=record.Data;
        var values=buffer?.AsArray();
        if(values==null||values.Length!=1||values[0].Value is not string text||text.Length>2048)
            throw new HzRefusal(ErrorCodes.InvalidInput,"Invalid alignment link record.");
        var link=JsonNode.Parse(text) as JsonObject ?? throw new HzRefusal(ErrorCodes.InvalidInput,"Invalid alignment link record.");
        if(link.Count!=4||!V.IsHex(Hz.Str(link,"alignment_handle"))||Hz.Num(link,"station") is not {} s||!Hz.IsFinite(s)||
           Hz.Num(link,"offset") is not {} o||!Hz.IsFinite(o)||Hz.Num(link,"scale") is not {} scale||!Hz.IsFinite(scale)||scale<=0)
            throw new HzRefusal(ErrorCodes.InvalidInput,"Invalid alignment link fields.");
        return link;
    }

    private static CommandResult AlignmentViewport(CommandContext ctx,bool refresh)
    {
        ObjectId alignmentId=ObjectId.Null,viewportId=ObjectId.Null,layoutId=ObjectId.Null,layerId=ObjectId.Null;
        JsonObject link=new(); Point3d viewTarget=Point3d.Origin; double twist=0;
        string originalLayout=""; bool originalTileMode=false;
        var center=refresh?Point3d.Origin:Resolve.P(ctx.Args["center"]);
        var width=Hz.Num(ctx.Args,"width")??0; var height=Hz.Num(ctx.Args,"height")??0;
        return WriteFlow.Run(ctx,"HZ_ALIGNMENT_SHEET",
            (doc,tr,plan)=>
            {
                if(Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument!=doc)
                    throw new HzRefusal(ErrorCodes.InvalidInput,"Alignment viewports require the active target drawing for native paper-space activation.");
                originalLayout=LayoutManager.Current.CurrentLayout; originalTileMode=doc.Database.TileMode;
                plan["native_layout_before"]=originalLayout;plan["native_tile_mode_before"]=originalTileMode;
                plan["native_activation"]="temporarily activate target paper layout through LayoutManager; restore original layout in finally";
                if(refresh)
                {
                    viewportId=Catalog.FromHandle(doc.Database,Hz.Str(ctx.Args,"handle")!);
                    if(tr.GetObject(viewportId,OpenMode.ForRead) is not Viewport vp||vp.Number==1)
                        throw new HzRefusal(ErrorCodes.InvalidInput,"Select a paper-space viewport created by alignment_viewport.");
                    var owner=(BlockTableRecord)tr.GetObject(vp.OwnerId,OpenMode.ForRead);
                    if(!owner.IsLayout||((Layout)tr.GetObject(owner.LayoutId,OpenMode.ForRead)).ModelType)
                        throw new HzRefusal(ErrorCodes.InvalidInput,"Alignment sheets require a paper-space layout viewport.");
                    layoutId=owner.LayoutId;
                    link=ReadSheetLink(vp,tr);
                    alignmentId=Catalog.FromHandle(doc.Database,Hz.Str(link,"alignment_handle")!);
                    if(((LayerTableRecord)tr.GetObject(vp.LayerId,OpenMode.ForRead)).IsLocked)
                        throw new HzRefusal(ErrorCodes.InvalidInput,"Viewport layer is locked.");
                }
                else
                {
                    layoutId=LayoutId(doc,tr,Hz.Str(ctx.Args,"layout")!);
                    layerId=Resolve.Layer(doc.Database,tr,Hz.Str(ctx.Args,"layer"));
                    if(((LayerTableRecord)tr.GetObject(layerId,OpenMode.ForRead)).IsLocked)
                        throw new HzRefusal(ErrorCodes.InvalidInput,"Requested viewport layer is locked.");
                    alignmentId=Resolve.Named(doc,tr,"alignment",Hz.Str(ctx.Args,"alignment")!);
                    link=new JsonObject { ["alignment_handle"]=alignmentId.Handle.ToString(),["station"]=Hz.Num(ctx.Args,"station")!.Value,
                        ["offset"]=Hz.Num(ctx.Args,"offset")??0,["scale"]=Hz.Num(ctx.Args,"scale")!.Value };
                }
                var a=Resolve.Open<Alignment>(tr,alignmentId);
                if(a.IsReferenceObject&&(!a.IsReferenceValid||a.IsReferenceStale))
                    throw new HzRefusal(ErrorCodes.InvalidInput,"Alignment reference is invalid or stale; refresh the reference explicitly first.");
                var station=Hz.Num(link,"station")!.Value;
                if(station<a.StartingStation||station>a.EndingStation||a.EndingStation<=a.StartingStation)
                    throw new HzRefusal(ErrorCodes.InvalidInput,"Sheet station is outside the current alignment range.");
                double x=0,y=0,x1=0,y1=0,x2=0,y2=0;
                a.PointLocation(station,Hz.Num(link,"offset")!.Value,ref x,ref y);
                var delta=Math.Min(0.01,(a.EndingStation-a.StartingStation)/1000);
                a.PointLocation(Math.Max(a.StartingStation,station-delta),0,ref x1,ref y1);
                a.PointLocation(Math.Min(a.EndingStation,station+delta),0,ref x2,ref y2);
                if(!Hz.IsFinite(x)||!Hz.IsFinite(y)) throw new HzRefusal(ErrorCodes.InvalidInput,"Alignment returned nonfinite coordinates.");
                viewTarget=new Point3d(x,y,0); twist=SheetInputs.Twist(x1,y1,x2,y2);
                plan["link"]=link.DeepClone(); plan["view_target_wcs"]=Resolve.Json(viewTarget); plan["view_center_dcs"]=Resolve.Json(Point2d.Origin); plan["twist_radians"]=twist;
                plan["orientation_method"]="finite station chord, estimated local tangent";
                plan["orientation_station_delta"]=delta;
                plan["refresh_mode"]="explicit command after alignment edits; no automatic event reactor";
                plan["viewport_on"]=true;
                plan["perspective_on"]=false;
                plan["scale_units"]="native paper-space units divided by model drawing units; caller must account for unit conversion";
                if(!refresh){plan["center"]=Resolve.Json(center,false);plan["width"]=width;plan["height"]=height;}
            },
            (doc,tr)=>
            {
                var manager=LayoutManager.Current;
                var paperName=((Layout)tr.GetObject(layoutId,OpenMode.ForRead)).LayoutName;
                try
                {
                manager.CurrentLayout=paperName;
                Viewport vp;
                if(refresh) vp=(Viewport)tr.GetObject(viewportId,OpenMode.ForWrite);
                else
                {
                    var l=(Layout)tr.GetObject(layoutId,OpenMode.ForWrite);
                    if(l.GetViewports().Count==0) l.Initialize();
                    var btr=(BlockTableRecord)tr.GetObject(l.BlockTableRecordId,OpenMode.ForWrite);
                    vp=new Viewport();vp.SetDatabaseDefaults(doc.Database);vp.LayerId=layerId;
                    vp.CenterPoint=new Point3d(center.X,center.Y,0);vp.Width=width;vp.Height=height;
                    viewportId=btr.AppendEntity(vp);tr.AddNewlyCreatedDBObject(vp,true);
                    vp.CreateExtensionDictionary();
                    var d=(DBDictionary)tr.GetObject(vp.ExtensionDictionary,OpenMode.ForWrite);
                    var r=new Xrecord();using var data=new ResultBuffer(new TypedValue(1,link.ToJsonString(Hz.Compact)));r.Data=data;
                    d.SetAt(AlignmentLinkKey,r);tr.AddNewlyCreatedDBObject(r,true);
                }
                vp.Locked=false;vp.PerspectiveOn=false;vp.ViewDirection=Vector3d.ZAxis;vp.ViewTarget=viewTarget;
                vp.TwistAngle=twist;vp.ViewCenter=Point2d.Origin;vp.CustomScale=Hz.Num(link,"scale")!.Value;
                vp.On=true;
                vp.Locked=true;
                }
                finally { manager.CurrentLayout=originalLayout; }
            },
            (doc,tr,v,after)=>
            {
                var vp=(Viewport)tr.GetObject(viewportId,OpenMode.ForRead);
                v.Text("original layout restored",originalLayout,LayoutManager.Current.CurrentLayout);
                v.Flag("original tile mode restored",originalTileMode,doc.Database.TileMode);
                v.Check("alignment link",link,ReadSheetLink(vp,tr),link.ToJsonString(Hz.Compact)==ReadSheetLink(vp,tr).ToJsonString(Hz.Compact));
                v.Number("view center DCS x",0,vp.ViewCenter.X,1e-6);v.Number("view center DCS y",0,vp.ViewCenter.Y,1e-6);
                v.Number("view target WCS x",viewTarget.X,vp.ViewTarget.X,1e-6);v.Number("view target WCS y",viewTarget.Y,vp.ViewTarget.Y,1e-6);v.Number("view target WCS z",0,vp.ViewTarget.Z,1e-6);
                v.Number("view direction x",0,vp.ViewDirection.X,1e-9);v.Number("view direction y",0,vp.ViewDirection.Y,1e-9);v.Number("view direction z",1,vp.ViewDirection.Z,1e-9);
                v.Number("twist",twist,vp.TwistAngle,1e-9);v.Number("scale",Hz.Num(link,"scale")!.Value,vp.CustomScale,1e-12);
                v.Flag("locked",true,vp.Locked);
                v.Flag("viewport on",true,vp.On);
                v.Flag("perspective off",false,vp.PerspectiveOn);
                if(!refresh){v.Number("paper width",width,vp.Width,1e-9);v.Number("paper height",height,vp.Height,1e-9);v.Number("paper center x",center.X,vp.CenterPoint.X,1e-9);v.Number("paper center y",center.Y,vp.CenterPoint.Y,1e-9);v.Text("layer",layerId.Handle.ToString(),vp.LayerId.Handle.ToString());}
                after["viewport"]=VpJson(vp);after["refresh_mode"]="explicit";after["view_target_wcs"]=Resolve.Json(vp.ViewTarget);
            });
    }
}
