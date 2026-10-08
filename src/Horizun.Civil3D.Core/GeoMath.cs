// -----------------------------------------------------------------------------
// Horizun Civil 3D MCP - plane angle helpers for horizun_c3d_document action=geo.
//
// One convention everywhere, stated in every reply: a direction in the drawing
// plane is reported as the angle from the drawing +Y axis, POSITIVE
// COUNTER-CLOCKWISE (the AutoCAD sense), in (-180, 180]. Its clockwise twin, the
// azimuth used by surveyors, is (360 - angle) mod 360.
// -----------------------------------------------------------------------------
using System.Text.Json.Nodes;

namespace Horizun.Civil3D.Core;

public static class GeoMath
{
    public const string Convention =
        "Directions are angles from the drawing +Y axis, positive counter-clockwise, in (-180, 180] degrees; " +
        "azimuth_deg is the clockwise surveyor form (0-360).";

    public static double Deg(double rad) => rad * 180.0 / Math.PI;

    /// <summary>Wrap an angle in degrees into (-180, 180].</summary>
    public static double Wrap180(double deg)
    {
        var a = deg % 360.0;
        if (a <= -180.0) a += 360.0;
        if (a > 180.0) a -= 360.0;
        return a;
    }

    /// <summary>Angle of (x, y) from +Y, counter-clockwise, degrees in (-180, 180]; null for a zero or non-finite vector.</summary>
    public static double? FromYCcwDeg(double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || Math.Sqrt(x * x + y * y) < 1e-12) return null;
        return Wrap180(Deg(Math.Atan2(-x, y)));
    }

    /// <summary>Clockwise azimuth from +Y in [0, 360).</summary>
    public static double AzimuthDeg(double fromYCcwDeg)
    {
        var a = (360.0 - fromYCcwDeg) % 360.0;
        return a < 0 ? a + 360.0 : a;
    }

    /// <summary>Signed angle (degrees, counter-clockwise positive) that turns direction a onto direction b.</summary>
    public static double? TurnDeg(double ax, double ay, double bx, double by)
    {
        if (FromYCcwDeg(ax, ay) is not { } a || FromYCcwDeg(bx, by) is not { } b) return null;
        return Wrap180(b - a);
    }

    /// <summary>A direction as JSON: vector, angle from +Y (ccw) and azimuth; null members when the vector is degenerate.</summary>
    public static JsonObject Direction(double x, double y, int round = 9)
    {
        var o = new JsonObject { ["x"] = Hz.Finite(x, 12), ["y"] = Hz.Finite(y, 12) };
        if (FromYCcwDeg(x, y) is { } a)
        {
            o["from_y_ccw_deg"] = Hz.Finite(a, round);
            o["azimuth_deg"] = Hz.Finite(AzimuthDeg(a), round);
        }
        else
        {
            o["from_y_ccw_deg"] = null;
            o["azimuth_deg"] = null;
            o["reason"] = "zero-length or non-finite vector";
        }
        return o;
    }

    /// <summary>An angle given in radians, reported as radians and degrees.</summary>
    public static JsonObject Angle(double rad, int round = 9) =>
        new() { ["rad"] = Hz.Finite(rad, 12), ["deg"] = Hz.Finite(Deg(rad), round) };
}
