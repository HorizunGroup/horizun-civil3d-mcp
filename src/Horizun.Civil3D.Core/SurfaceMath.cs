using System.Text.Json.Nodes;

namespace Horizun.Civil3D.Core;

public readonly record struct SurfaceXY(double X, double Y);
public readonly record struct SurfaceCell(double X, double Y, double Area);

/// <summary>Bounded deterministic sampling. The grid is midpoint quadrature, explicitly estimated.</summary>
public static class SurfaceMath
{
    public static List<SurfaceXY> AlongLine(JsonObject line, double step)
    {
        var a=(JsonObject)line["start"]!; var b=(JsonObject)line["end"]!;
        var ax=Hz.Num(a,"x")!.Value; var ay=Hz.Num(a,"y")!.Value;
        var bx=Hz.Num(b,"x")!.Value; var by=Hz.Num(b,"y")!.Value;
        var dx=bx-ax; var dy=by-ay; var length=Math.Sqrt(dx*dx+dy*dy);
        if (!double.IsFinite(length) || !double.IsFinite(step) || step <= 0 || Math.Ceiling(length/step)+1 > SurfaceInputs.MaxPoints)
            throw new HzRefusal(ErrorCodes.InvalidInput,"Line sampling exceeds 10000 points or has invalid coordinates/step.");
        var result=new List<SurfaceXY>();
        if (length == 0) { result.Add(new(ax,ay)); return result; }
        var full=(int)Math.Floor(length/step);
        for(var i=0;i<=full;i++) { var t=i*step/length; result.Add(new(ax+dx*t,ay+dy*t)); }
        // A non-integral final segment still includes the exact end, once.
        if (full*step < length) result.Add(new(bx,by));
        else result[^1]=new(bx,by);
        return result;
    }

    public static (List<SurfaceCell> Cells,double Spacing) Grid(double xmin,double ymin,double xmax,double ymax,double? spacing,int maxSamples)
    {
        var w=xmax-xmin; var h=ymax-ymin;
        if (!new[]{xmin,ymin,xmax,ymax,w,h,w*h}.All(double.IsFinite) || w<=0 || h<=0 || maxSamples<1 || maxSamples>SurfaceInputs.MaxGridSamples)
            throw new HzRefusal(ErrorCodes.InvalidInput,"Surface XY bounds or sampling budget are invalid.");
        var s=spacing ?? Math.Max(Math.Sqrt(w*h/maxSamples),Math.Max(w,h)/maxSamples);
        if (!double.IsFinite(s) || s<=0) throw new HzRefusal(ErrorCodes.InvalidInput,"grid_spacing must be finite and > 0.");
        double Count(double v) => Math.Max(1,Math.Ceiling(v/s));
        if (spacing == null) while(Count(w)*Count(h)>maxSamples) s*=1.01;
        if (Count(w)*Count(h)>maxSamples) throw new HzRefusal(ErrorCodes.InvalidInput,"grid_spacing requires more than max_samples; increase spacing or the budget. Nothing sampled.");
        var nx=(int)Count(w); var ny=(int)Count(h); var cells=new List<SurfaceCell>(nx*ny);
        for(var i=0;i<nx;i++) for(var j=0;j<ny;j++)
        {
            var x0=i*s; var y0=j*s; var cw=Math.Min(s,w-x0); var ch=Math.Min(s,h-y0);
            cells.Add(new(xmin+x0+cw/2,ymin+y0+ch/2,cw*ch));
        }
        return (cells,s);
    }
}

public sealed class SurfaceSamples
{
    private readonly List<(double Z,double Area)> _values=new();
    public void Add(double z,double area)
    {
        if (!double.IsFinite(z) || !double.IsFinite(area) || area<=0 || !double.IsFinite(z*area))
            throw new HzRefusal(ErrorCodes.InvalidInput,"A sampled elevation/area is non-finite or invalid.");
        _values.Add((z,area));
    }
    public JsonObject ToJson()
    {
        var area=_values.Sum(v=>v.Area); var cut=_values.Where(v=>v.Z<0).Select(v=>(Z:-v.Z,v.Area)).ToList(); var fill=_values.Where(v=>v.Z>0).ToList();
        var ca=cut.Sum(v=>v.Area); var fa=fill.Sum(v=>v.Area);
        var cv=cut.Sum(v=>v.Z*v.Area); var fv=fill.Sum(v=>v.Z*v.Area);
        if (!new[] { area,ca,fa,cv,fv,fv-cv }.All(double.IsFinite))
            throw new HzRefusal(ErrorCodes.InvalidInput,"Accumulated sampled area or volume overflows; statistics are unavailable.");
        return new JsonObject {
            ["method"]="midpoint_grid_estimate", ["samples"]=_values.Count, ["area_2d_estimated"]=_values.Count>0?Hz.Finite(area):null,
            ["cut_area_estimated"]=_values.Count>0?Hz.Finite(ca):null, ["fill_area_estimated"]=_values.Count>0?Hz.Finite(fa):null, ["zero_area_estimated"]=_values.Count>0?Hz.Finite(area-ca-fa):null,
            ["cut_volume_estimated"]=_values.Count>0?Hz.Finite(cv):null, ["fill_volume_estimated"]=_values.Count>0?Hz.Finite(fv):null, ["net_volume_estimated"]=_values.Count>0?Hz.Finite(fv-cv):null,
            ["cut"]=Describe(cut,ca,cv), ["fill"]=Describe(fill,fa,fv),
            ["area_weighted_signed_mean"]=area>0 ? Hz.Finite((fv-cv)/area):null,
            ["unreadable_reason"]=_values.Count==0?"No valid elevations were sampled; area and volume are unknown, not zero.":null,
            ["note"]="Area, volumes, side minima and percentiles are grid estimates, not exact Civil 3D measurements. Boundary cells use their actual rectangular area."
        };
    }
    private static JsonObject Describe(List<(double Z,double Area)> values,double area,double volume)
    {
        if (values.Count==0) return new JsonObject { ["min"]=null,["max"]=null,["mean"]=null,["median"]=null,["p90"]=null,["unreadable_reason"]="No samples on this side of zero." };
        values.Sort((a,b)=>a.Z.CompareTo(b.Z));
        double Percentile(double p) { var a=0d; foreach(var v in values) { a+=v.Area; if(a>=p*area) return v.Z; } return values[^1].Z; }
        return new JsonObject { ["min"]=Hz.Finite(values[0].Z),["max"]=Hz.Finite(values[^1].Z),["mean"]=Hz.Finite(volume/area),["median"]=Hz.Finite(Percentile(.5)),["p90"]=Hz.Finite(Percentile(.9)) };
    }
}
