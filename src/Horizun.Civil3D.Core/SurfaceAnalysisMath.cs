// -----------------------------------------------------------------------------
// Horizun Civil 3D MCP - analysis ranges, colours and per-band statistics.
//
// Pure logic for apply_elevation_analysis / apply_slope_analysis, tested
// without Civil 3D. Organisation-neutral: legends (ranges + colours) arrive as
// parameters; the built-in schemes are generic ramps, not any company standard.
//
//   equal    N ranges of equal width between the surface minimum and maximum
//   step     ranges of a fixed interval, aligned so one boundary falls exactly
//            on break_at (default 0: cut and fill never share a band)
//   ranges   explicit [{min,max,color}]
//   recolor  keep the surface's current ranges, change only the colours
// -----------------------------------------------------------------------------
using System.Globalization;
using System.Text.Json.Nodes;

namespace Horizun.Civil3D.Core;

/// <summary>A colour as Civil 3D stores it: an ACI index or a true RGB colour.</summary>
public readonly record struct HzColor(bool IsAci, short Aci, byte R, byte G, byte B)
{
    public static HzColor FromAci(short aci) => new(true, aci, 0, 0, 0);
    public static HzColor FromRgb(byte r, byte g, byte b) => new(false, 0, r, g, b);

    public override string ToString() => IsAci ? "ACI " + Aci : $"#{R:X2}{G:X2}{B:X2}";

    public JsonNode ToJson() => IsAci ? JsonValue.Create((int)Aci) : JsonValue.Create(ToString());

    /// <summary>Accepts an integer ACI 1-255, "#RRGGBB", or a decimal string of an ACI. Null when invalid.</summary>
    public static HzColor? Parse(JsonNode? node)
    {
        if (node is not JsonValue v) return null;
        if (Hz.AsDouble(v) is { } d)
            return d is >= 1 and <= 255 && Math.Abs(d - Math.Round(d)) < 1e-9 ? FromAci((short)Math.Round(d)) : null;
        if (!v.TryGetValue<string>(out var s)) return null;
        s = s.Trim();
        if (s.Length == 7 && s[0] == '#' && s.Skip(1).All(Uri.IsHexDigit))
            return FromRgb(byte.Parse(s.Substring(1, 2), NumberStyles.HexNumber), byte.Parse(s.Substring(3, 2), NumberStyles.HexNumber),
                byte.Parse(s.Substring(5, 2), NumberStyles.HexNumber));
        if (short.TryParse(s, out var aci) && aci is >= 1 and <= 255) return FromAci(aci);
        return null;
    }
}

public readonly record struct AnalysisRange(double Min, double Max, HzColor Color);

public static class SurfaceAnalysisMath
{
    public const int MaxRanges = 64;
    public static readonly string[] Modes = { "equal", "step", "ranges", "recolor" };
    public static readonly string[] Schemes = { "rainbow", "cutfill", "reds", "blues", "greens", "grays", "land" };

    public static List<(double Min, double Max)> Equal(double min, double max, int n)
    {
        if (!double.IsFinite(min) || !double.IsFinite(max) || max <= min)
            throw new HzRefusal(ErrorCodes.InvalidInput, "The surface has no finite value range to split (min " + min + ", max " + max + ").");
        if (n < 1 || n > MaxRanges) throw new HzRefusal(ErrorCodes.InvalidInput, "number_of_ranges must be 1-" + MaxRanges + ".");
        var w = (max - min) / n;
        return Enumerable.Range(0, n).Select(i => (min + i * w, i == n - 1 ? max : min + (i + 1) * w)).ToList();
    }

    /// <summary>Fixed-interval bands covering [min,max], with a boundary exactly on breakAt.</summary>
    public static List<(double Min, double Max)> Step(double min, double max, double interval, double breakAt)
    {
        if (!double.IsFinite(min) || !double.IsFinite(max) || max <= min)
            throw new HzRefusal(ErrorCodes.InvalidInput, "The surface has no finite value range to split.");
        if (!double.IsFinite(interval) || interval <= 0) throw new HzRefusal(ErrorCodes.InvalidInput, "interval must be finite and > 0.");
        var first = breakAt + Math.Floor((min - breakAt) / interval) * interval;
        var last = breakAt + Math.Ceiling((max - breakAt) / interval) * interval;
        var n = (int)Math.Round((last - first) / interval);
        if (n < 1) n = 1;
        if (n > MaxRanges)
            throw new HzRefusal(ErrorCodes.InvalidInput, "interval " + interval + " gives " + n + " bands over [" + min + ", " + max + "]; the maximum is " + MaxRanges + ". Use a larger interval.");
        return Enumerable.Range(0, n).Select(i => (first + i * interval, first + (i + 1) * interval)).ToList();
    }

    public static List<HzColor> Palette(string scheme, IReadOnlyList<(double Min, double Max)> ranges, double breakAt)
    {
        var n = ranges.Count;
        switch (scheme)
        {
            case "cutfill":
            {
                var cut = ranges.Count(r => r.Max <= breakAt + 1e-9);
                var fill = n - cut;
                var res = new List<HzColor>();
                for (var i = 0; i < cut; i++) res.Add(Lerp((128, 0, 0), (255, 230, 120), cut == 1 ? 1 : (double)i / (cut - 1)));
                for (var i = 0; i < fill; i++) res.Add(Lerp((170, 230, 170), (0, 40, 140), fill == 1 ? 0 : (double)i / (fill - 1)));
                return res;
            }
            case "reds": return Grad(n, (255, 220, 200), (140, 0, 0));
            case "blues": return Grad(n, (200, 225, 255), (0, 30, 130));
            case "greens": return Grad(n, (210, 245, 200), (0, 90, 30));
            case "grays": return Grad(n, (230, 230, 230), (60, 60, 60));
            case "land":
                return Enumerable.Range(0, n).Select(i => Multi(n == 1 ? 0 : (double)i / (n - 1),
                    (40, 120, 40), (150, 190, 80), (230, 210, 120), (170, 110, 60), (240, 240, 240))).ToList();
            default:
                return Enumerable.Range(0, n).Select(i => Hsv(240.0 * (1 - (n == 1 ? 0 : (double)i / (n - 1))))).ToList();
        }
    }

    /// <summary>Area (and value*area) per band from weighted samples. Values outside every band are counted separately.</summary>
    public static JsonObject Bands(IReadOnlyList<AnalysisRange> ranges, IEnumerable<(double Value, double Area)> samples,
                                   bool withVolume, string valueUnit, double displayFactor = 1.0)
    {
        var area = new double[ranges.Count];
        var volume = new double[ranges.Count];
        double total = 0, outside = 0;
        var count = 0;
        foreach (var (value, a) in samples)
        {
            count++;
            total += a;
            var idx = -1;
            for (var i = 0; i < ranges.Count; i++)
            {
                var r = ranges[i];
                var lastBand = i == ranges.Count - 1;
                if (value >= r.Min && (value < r.Max || (lastBand && value <= r.Max))) { idx = i; break; }
            }
            if (idx < 0) { outside += a; continue; }
            area[idx] += a;
            volume[idx] += value * a;
        }
        var rows = new JsonArray();
        for (var i = 0; i < ranges.Count; i++)
        {
            var row = new JsonObject
            {
                ["min"] = Hz.Finite(ranges[i].Min * displayFactor, 9),
                ["max"] = Hz.Finite(ranges[i].Max * displayFactor, 9),
                ["color"] = ranges[i].Color.ToJson(),
                ["area_estimated"] = count > 0 ? Hz.Finite(area[i], 6) : null,
                ["area_pct"] = total > 0 ? Hz.Finite(area[i] / total * 100, 4) : null,
            };
            if (withVolume) row["volume_estimated"] = count > 0 ? Hz.Finite(volume[i], 6) : null;
            rows.Add(row);
        }
        var o = new JsonObject
        {
            ["method"] = "midpoint_grid_estimate",
            ["value_unit"] = valueUnit,
            ["samples"] = count,
            ["area_sampled"] = count > 0 ? Hz.Finite(total, 6) : null,
            ["area_outside_all_bands"] = count > 0 ? Hz.Finite(outside, 6) : null,
            ["bands"] = rows,
            ["note"] = "Per-band areas/volumes are grid estimates. Volume = sum(value x cell area); for a volume surface the value is the cut/fill depth.",
        };
        if (count == 0) o["unreadable_reason"] = "No valid samples: per-band areas are unknown, not zero.";
        return o;
    }

    private static List<HzColor> Grad(int n, (int, int, int) a, (int, int, int) b) =>
        Enumerable.Range(0, n).Select(i => Lerp(a, b, n == 1 ? 1 : (double)i / (n - 1))).ToList();

    private static HzColor Lerp((int R, int G, int B) a, (int R, int G, int B) b, double t) =>
        HzColor.FromRgb((byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));

    private static HzColor Multi(double t, params (int, int, int)[] stops)
    {
        var seg = Math.Min(stops.Length - 2, (int)(t * (stops.Length - 1)));
        var lt = t * (stops.Length - 1) - seg;
        return Lerp(stops[seg], stops[seg + 1], lt);
    }

    private static HzColor Hsv(double hue)
    {
        var x = 1 - Math.Abs(hue / 60 % 2 - 1);
        var (r, g, b) = hue switch
        {
            < 60 => (1.0, x, 0.0), < 120 => (x, 1.0, 0.0), < 180 => (0.0, 1.0, x),
            < 240 => (0.0, x, 1.0), < 300 => (x, 0.0, 1.0), _ => (1.0, 0.0, x),
        };
        return HzColor.FromRgb((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
    }
}
