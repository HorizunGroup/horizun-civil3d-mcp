using System.Text.Json.Nodes;

namespace Horizun.Civil3D.Core;

/// <summary>Explicitly refreshed alignment-linked sheet cameras, not native view frames.</summary>
public static class SheetInputs
{
    public static string? ValidateCreate(JsonObject args) => V.First(
        new[] { "center", "width", "height", "scale", "station" }.Any(k => args[k] == null) ? "center, width, height, scale and station are required." : null,
        args["center"] is JsonObject p && (p.Count != 2 || p["x"] == null || p["y"] == null) ? "center must contain exactly paper-space x and y." : null,
        V.Point(args, "center"), V.Pos(args, "width"), V.Pos(args, "height"),
        V.Pos(args, "scale"), V.Fin(args, "station"), V.Fin(args, "offset"),
        new[] { "layout", "alignment" }.Any(k => string.IsNullOrWhiteSpace(Hz.Str(args,k))) ? "layout and alignment must be nonempty strings." : null);

    public static double Twist(double x1, double y1, double x2, double y2)
    {
        var dx=x2-x1; var dy=y2-y1;
        if(!Hz.IsFinite(dx)||!Hz.IsFinite(dy)||(dx==0&&dy==0)) throw new ArgumentException("Alignment tangent cannot be resolved.");
        // WCS -> DCS rotates by -TwistAngle. Positive world tangent angle therefore
        // yields a horizontal DCS tangent. See Autodesk's paperspace zoom example.
        return Math.Atan2(dy,dx);
    }
}
