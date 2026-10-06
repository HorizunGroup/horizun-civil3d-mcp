// -----------------------------------------------------------------------------
// Horizun Civil 3D MCP - geometric grading engine (pure geometry, no Autodesk).
//
// Native Civil 3D gradings have NO public API (no managed GradingGroup in
// 2025). This engine reproduces grading results with 3D polylines that later
// become TIN breaklines: it was validated against a native grading on a real
// project (median dz 0.000 m, volume -0.09 %). Output is labelled GEOMETRIC,
// never presented as native gradings.
//
// Steps, applied in order from the source footprint:
//   outer: offset {dist, dz?}            parallel line dist outward, z + dz
//          grade_to_surface {slope | cut_slope, fill_slope}  H:V slopes; rays
//                outward (fans at convex corners, like native gradings) until
//                they meet the target surface -> daylight line
//   inner: offset {dist, dz?}            parallel line dist inward
//          grade_to_depth {depth, slope}  inner line dropped by depth, set in by depth*slope
//          grade_to_elevation {elevation, slope}  inner line at a fixed elevation
//
// The target surface enters as a sampler: (x, y) -> z, or null outside it.
// -----------------------------------------------------------------------------
using System.Text.Json.Nodes;

namespace Horizun.Civil3D.Core;

public readonly record struct P3(double X, double Y, double Z)
{
    public double Dist2D(P3 o) => Math.Sqrt((X - o.X) * (X - o.X) + (Y - o.Y) * (Y - o.Y));
}

public sealed record GradingLine(string Tag, string Side, List<P3> Points);

public sealed class GradingResult
{
    public List<GradingLine> Lines { get; } = new();
    public List<string> Log { get; } = new();
    public int FailedRays { get; set; }
    public int TotalRays { get; set; }
    public GradingLine BoundaryLine => Lines.Last(l => l.Side != "inner");

    public JsonArray Summary() => Hz.Arr(Lines.Select(l => (JsonNode?)new JsonObject
    {
        ["tag"] = l.Tag,
        ["side"] = l.Side,
        ["points"] = l.Points.Count,
        ["min_z"] = Hz.Finite(l.Points.Min(p => p.Z), 4),
        ["max_z"] = Hz.Finite(l.Points.Max(p => p.Z), 4),
        ["area"] = Hz.Finite(Math.Abs(GradingEngine.Area(l.Points)), 3),
    }));
}

public static class GradingEngine
{
    public static readonly string[] OuterSteps = { "offset", "grade_to_surface" };
    public static readonly string[] InnerSteps = { "offset", "grade_to_depth", "grade_to_elevation" };

    public static double Area(IReadOnlyList<P3> l)
    {
        double a = 0;
        for (var i = 0; i < l.Count; i++) { var p = l[i]; var q = l[(i + 1) % l.Count]; a += p.X * q.Y - q.X * p.Y; }
        return a / 2;
    }

    /// <summary>Drop the closing duplicate and consecutive duplicates, orient counter-clockwise.</summary>
    public static List<P3> Normalize(IEnumerable<P3> input)
    {
        var p = input.ToList();
        if (p.Count > 2 && p[0].Dist2D(p[^1]) < 1e-6) p.RemoveAt(p.Count - 1);
        p = p.Where((q, i) => i == 0 || q.Dist2D(p[i - 1]) > 1e-4).ToList();
        if (p.Count < 3) throw new HzRefusal(ErrorCodes.InvalidInput, "The source footprint needs at least 3 distinct vertices.");
        if (Math.Abs(Area(p)) < 1e-6) throw new HzRefusal(ErrorCodes.InvalidInput, "The source footprint has zero area.");
        if (Area(p) < 0) p.Reverse();
        return p;
    }

    private static (double X, double Y) OutNormal(P3 a, P3 b)
    {
        var dx = b.X - a.X; var dy = b.Y - a.Y; var len = Math.Sqrt(dx * dx + dy * dy);
        return (dy / len, -dx / len); // exterior normal for a CCW polygon
    }

    private static (double X, double Y) Unit(double x, double y)
    {
        var l = Math.Sqrt(x * x + y * y);
        return l < 1e-12 ? (0, 0) : (x / l, y / l);
    }

    public static List<P3> Offset(IReadOnlyList<P3> l, double d, double dz)
    {
        var r = new List<P3>(); var n = l.Count;
        for (var i = 0; i < n; i++)
        {
            var n1 = OutNormal(l[(i - 1 + n) % n], l[i]); var n2 = OutNormal(l[i], l[(i + 1) % n]);
            var bis = Unit(n1.X + n2.X, n1.Y + n2.Y);
            if (bis == (0, 0)) bis = n2;
            var m = d / Math.Max(0.2, bis.X * n2.X + bis.Y * n2.Y);
            r.Add(new P3(l[i].X + bis.X * m, l[i].Y + bis.Y * m, l[i].Z + dz));
        }
        return r;
    }

    /// <summary>Remove self-intersection loops (concave corners with large offsets), keeping the larger loop.</summary>
    public static List<P3> RemoveLoops(List<P3> l)
    {
        var changed = true; var guard = 0;
        while (changed && guard++ < 200)
        {
            changed = false; var n = l.Count;
            for (var i = 0; i < n && !changed; i++)
                for (var j = i + 2; j < n && !changed; j++)
                {
                    if (i == 0 && j == n - 1) continue;
                    var a = l[i]; var b = l[(i + 1) % n]; var c = l[j]; var d = l[(j + 1) % n];
                    var den = (b.X - a.X) * (d.Y - c.Y) - (b.Y - a.Y) * (d.X - c.X);
                    if (Math.Abs(den) < 1e-12) continue;
                    var t = ((c.X - a.X) * (d.Y - c.Y) - (c.Y - a.Y) * (d.X - c.X)) / den;
                    var u = ((c.X - a.X) * (b.Y - a.Y) - (c.Y - a.Y) * (b.X - a.X)) / den;
                    if (t <= 1e-9 || t >= 1 - 1e-9 || u <= 1e-9 || u >= 1 - 1e-9) continue;
                    var x = new P3(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);
                    var inner = l.Skip(i + 1).Take(j - i).ToList();
                    var outer = l.Skip(j + 1).Concat(l.Take(i + 1)).ToList();
                    var keepOuter = Math.Abs(Area(outer.Append(x).ToList())) >= Math.Abs(Area(inner.Prepend(x).ToList()));
                    l = keepOuter ? l.Take(i + 1).Append(x).Concat(l.Skip(j + 1)).ToList() : inner.Prepend(x).ToList();
                    changed = true;
                }
        }
        return l;
    }

    private sealed record DensePoint(P3 P, (double X, double Y) N, bool Convex, (double X, double Y) N1, (double X, double Y) N2);

    private static List<DensePoint> Densify(IReadOnlyList<P3> l, double step)
    {
        var r = new List<DensePoint>(); var n = l.Count;
        for (var i = 0; i < n; i++)
        {
            var a = l[i]; var b = l[(i + 1) % n]; var prev = l[(i - 1 + n) % n];
            var n1 = OutNormal(prev, a); var n2 = OutNormal(a, b);
            var bis = Unit(n1.X + n2.X, n1.Y + n2.Y);
            if (bis == (0, 0)) bis = n2;
            var cross = n1.X * n2.Y - n1.Y * n2.X; // < 0: right turn on a CCW polygon = convex corner
            r.Add(new DensePoint(a, bis, cross < -1e-9, n1, n2));
            var len = a.Dist2D(b);
            var k = (int)Math.Floor(len / step);
            for (var j = 1; j <= k; j++)
            {
                var t = j * step / len;
                if (t >= 1 - 1e-6) break;
                r.Add(new DensePoint(new P3(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t), n2, false, n2, n2));
            }
        }
        return r;
    }

    /// <summary>March from p along dir at an H:V slope (up in cut, down in fill) until it meets the surface.</summary>
    public static P3? Daylight(P3 p, (double X, double Y) dir, double cutHV, double fillHV, Func<double, double, double?> surface, double maxDistance = 1000)
    {
        var z0 = surface(p.X, p.Y);
        if (z0 == null) return null;
        var cut = z0.Value > p.Z;
        var g = cut ? 1.0 / cutHV : -1.0 / fillHV;
        double F(double t)
        {
            var z = surface(p.X + dir.X * t, p.Y + dir.Y * t);
            return z == null ? double.NaN : p.Z + g * t - z.Value;
        }
        var f0 = F(0);
        if (Math.Abs(f0) < 1e-6) return p;
        var step = Math.Min(0.25, Math.Max(0.02, Math.Abs(f0) * Math.Min(cutHV, fillHV) / 20));
        double t0 = 0, t1 = step, fa = f0;
        for (var k = 0; t1 <= maxDistance; k++, t1 += step)
        {
            var fb = F(t1);
            if (double.IsNaN(fb)) return null;
            if (Math.Sign(fb) != Math.Sign(fa) || fb == 0)
            {
                for (var it = 0; it < 60; it++)
                {
                    var tm = (t0 + t1) / 2; var fm = F(tm);
                    if (double.IsNaN(fm)) return null;
                    if (Math.Sign(fm) == Math.Sign(fa)) { t0 = tm; fa = fm; } else t1 = tm;
                }
                var tt = (t0 + t1) / 2;
                return new P3(p.X + dir.X * tt, p.Y + dir.Y * tt, p.Z + g * tt);
            }
            t0 = t1; fa = fb;
        }
        return null;
    }

    public static GradingResult Run(IEnumerable<P3> source, IReadOnlyList<JsonObject> outer, IReadOnlyList<JsonObject> inner,
                                    double densify, Func<double, double, double?>? surface)
    {
        var res = new GradingResult();
        var p = Normalize(source);
        res.Lines.Add(new GradingLine("base", "base", p));
        res.Log.Add($"source: {p.Count} vertices, area {Math.Abs(Area(p)):F3}, z {p.Min(q => q.Z):F3}..{p.Max(q => q.Z):F3}");
        var cur = p;
        var idx = 0;
        foreach (var s in outer)
        {
            idx++;
            var type = Hz.Str(s, "type");
            if (type == "offset")
            {
                var d = Hz.Num(s, "dist")!.Value;
                cur = RemoveLoops(Offset(cur, d, Hz.Num(s, "dz") ?? 0));
                res.Lines.Add(new GradingLine($"outer{idx}_offset_{d}", "outer", cur));
            }
            else if (type == "grade_to_surface")
            {
                if (surface == null) throw new HzRefusal(ErrorCodes.InvalidInput, "grade_to_surface needs a target surface.");
                var sl = Hz.Num(s, "slope") ?? 1.0;
                var cs = Hz.Num(s, "cut_slope") ?? sl;
                var fs = Hz.Num(s, "fill_slope") ?? sl;
                var day = new List<P3>();
                foreach (var q in Densify(cur, densify))
                {
                    var dirs = new List<(double X, double Y)> { q.N };
                    if (q.Convex)
                    {
                        var a1 = Math.Atan2(q.N1.Y, q.N1.X); var a2 = Math.Atan2(q.N2.Y, q.N2.X);
                        var da = a2 - a1;
                        while (da > Math.PI) da -= 2 * Math.PI;
                        while (da < -Math.PI) da += 2 * Math.PI;
                        var k = Math.Max(2, (int)Math.Ceiling(Math.Abs(da) / (Math.PI / 12)));
                        dirs = Enumerable.Range(0, k + 1).Select(j => (Math.Cos(a1 + da * j / k), Math.Sin(a1 + da * j / k))).ToList();
                    }
                    foreach (var dir in dirs)
                    {
                        res.TotalRays++;
                        var dl = Daylight(q.P, dir, cs, fs, surface);
                        if (dl != null) day.Add(dl.Value); else res.FailedRays++;
                    }
                }
                day = day.Where((q, i) => i == 0 || q.Dist2D(day[i - 1]) > 0.01).ToList();
                if (day.Count < 3) throw new HzRefusal(ErrorCodes.InvalidInput, "The daylight line could not be built: the rays did not meet the target surface (" + res.FailedRays + " of " + res.TotalRays + " failed). Nothing changed.");
                cur = RemoveLoops(day);
                res.Lines.Add(new GradingLine($"outer{idx}_daylight_c{cs}_f{fs}", "outer", cur));
                res.Log.Add($"daylight: {cur.Count} points, {res.FailedRays} of {res.TotalRays} rays did not meet the surface");
            }
        }
        var inn = p;
        idx = 0;
        foreach (var s in inner)
        {
            idx++;
            var before = Area(inn);
            var prevLine = inn;
            var prevZ = inn.Average(q => q.Z);
            var type = Hz.Str(s, "type");
            if (type == "offset")
            {
                var d = Hz.Num(s, "dist")!.Value;
                inn = RemoveLoops(Offset(inn, -d, Hz.Num(s, "dz") ?? 0));
                res.Lines.Add(new GradingLine($"inner{idx}_offset_{d}", "inner", inn));
            }
            else if (type == "grade_to_depth")
            {
                var dp = Hz.Num(s, "depth")!.Value; var sl = Hz.Num(s, "slope") ?? 0.001;
                inn = RemoveLoops(Offset(inn, -dp * sl, -dp));
                res.Lines.Add(new GradingLine($"inner{idx}_depth_{dp}", "inner", inn));
            }
            else if (type == "grade_to_elevation")
            {
                var el = Hz.Num(s, "elevation")!.Value; var sl = Hz.Num(s, "slope") ?? 1.0;
                var zAvg = inn.Average(q => q.Z);
                inn = RemoveLoops(Offset(inn, -Math.Abs(zAvg - el) * sl, 0)).Select(q => q with { Z = el }).ToList();
                res.Lines.Add(new GradingLine($"inner{idx}_elevation_{el}", "inner", inn));
            }
            // An inward step must keep a positive (CCW) area, shrink the footprint, and keep every vertex
            // at least the step's inset distance from the previous line. An offset larger than the shape
            // turns it inside out - often still CCW with a smaller area - but its vertices end up closer
            // than the inset distance, which is what catches it.
            var after = Area(inn);
            var inset = type switch
            {
                "offset" => Hz.Num(s, "dist")!.Value,
                "grade_to_depth" => Hz.Num(s, "depth")!.Value * (Hz.Num(s, "slope") ?? 0.001),
                _ => Math.Abs(prevZ - Hz.Num(s, "elevation")!.Value) * (Hz.Num(s, "slope") ?? 1.0),
            };
            var tooClose = inn.Any(q => DistanceToBoundary(q, prevLine) < inset * (1 - 1e-6) - 1e-9);
            if (inn.Count < 3 || after <= 1e-6 || after > before + 1e-6 || tooClose)
                throw new HzRefusal(ErrorCodes.InvalidInput, "Inner step " + idx + " collapses the footprint (the inward offset is larger than the shape). Nothing changed.");
        }
        return res;
    }

    public static double DistanceToBoundary(P3 p, IReadOnlyList<P3> poly)
    {
        var best = double.MaxValue;
        for (var i = 0; i < poly.Count; i++)
        {
            var a = poly[i]; var b = poly[(i + 1) % poly.Count];
            var dx = b.X - a.X; var dy = b.Y - a.Y; var len2 = dx * dx + dy * dy;
            var t = len2 < 1e-24 ? 0 : RuntimeCompat.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0, 1);
            var cx = a.X + t * dx - p.X; var cy = a.Y + t * dy - p.Y;
            best = Math.Min(best, Math.Sqrt(cx * cx + cy * cy));
        }
        return best;
    }

    /// <summary>Validate a step list; null when valid.</summary>
    public static string? ValidateSteps(JsonNode? node, bool outer)
    {
        if (node == null) return null;
        if (node is not JsonArray a || a.Count > 20) return (outer ? "outer" : "inner") + " must be an array of at most 20 steps.";
        var allowedTypes = outer ? OuterSteps : InnerSteps;
        for (var i = 0; i < a.Count; i++)
        {
            var w = (outer ? "outer" : "inner") + "[" + i + "]";
            if (a[i] is not JsonObject s) return w + " must be an object.";
            var type = Hz.Str(s, "type");
            if (type == null || !allowedTypes.Contains(type)) return w + ".type must be one of " + string.Join(", ", allowedTypes) + ".";
            string[] keys = type switch
            {
                "offset" => new[] { "type", "dist", "dz" },
                "grade_to_surface" => new[] { "type", "slope", "cut_slope", "fill_slope" },
                "grade_to_depth" => new[] { "type", "depth", "slope" },
                _ => new[] { "type", "elevation", "slope" },
            };
            if (s.FirstOrDefault(kv => !keys.Contains(kv.Key)) is { Key: { } extra }) return w + "." + extra + " does not apply to " + type + ".";
            bool Pos(string k, bool required) => Hz.Num(s, k) is { } v ? Hz.IsFinite(v) && v > 0 : !required && s[k] == null;
            bool Fin(string k, bool required) => Hz.Num(s, k) is { } v ? Hz.IsFinite(v) : !required && s[k] == null;
            switch (type)
            {
                case "offset":
                    if (!Pos("dist", true)) return w + ".dist must be > 0.";
                    if (!Fin("dz", false)) return w + ".dz must be finite.";
                    break;
                case "grade_to_surface":
                    if (!Pos("slope", false) || !Pos("cut_slope", false) || !Pos("fill_slope", false)) return w + ": slopes are H:V and must be > 0.";
                    break;
                case "grade_to_depth":
                    if (!Pos("depth", true)) return w + ".depth must be > 0.";
                    if (Hz.Num(s, "slope") is { } sl && (!Hz.IsFinite(sl) || sl < 0)) return w + ".slope must be >= 0.";
                    break;
                default:
                    if (!Fin("elevation", true)) return w + ".elevation must be a finite number.";
                    if (!Pos("slope", false)) return w + ".slope must be > 0.";
                    break;
            }
        }
        return null;
    }
}
