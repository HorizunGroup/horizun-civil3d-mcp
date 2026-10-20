using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Horizun.Civil3D.Core;

/// <summary>A bounded, lossless TIN handoff. Importing its points as a Revit
/// Toposolid does NOT preserve connectivity or establish shared coordinates.</summary>
public static class RevitTerrainPackage
{
    public const string Format = "horizun.civil3d.revit-terrain/v1";
    public const int MaxPoints = 20_000; // Current Horizun Revit reader guard, not an Autodesk limit.
    public const int MaxFaces = 40_000;
    public const int MaxPayloadBytes = 64 * 1024 * 1024;
    public sealed record Prepared(byte[] LandXml, byte[] Manifest, JsonObject Summary)
    {
        public byte[]? MeshObj { get; init; }
    }

    public static Prepared Prepare(LxSurface surface, string linearUnit, JsonObject source,
        JsonObject coordinateSystem, string version)
    {
        if (string.IsNullOrWhiteSpace(surface.Name) || surface.Points.Count is < 3 or > MaxPoints ||
            surface.Faces.Count is < 1 or > MaxFaces)
            throw new ArgumentException("One nonempty TIN with 3..20000 visible vertices and 1..40000 faces is required. No automatic thinning is performed.");
        var metresPerUnit = linearUnit switch
        {
            "meter" => 1d, "foot" => 0.3048, "USSurveyFoot" => 1200d / 3937d,
            _ => throw new ArgumentException("Explicit meter, foot or USSurveyFoot units are required."),
        };
        var xy = new HashSet<(double, double)>();
        foreach (var p in surface.Points)
            if (!Hz.IsFinite(p.X) || !Hz.IsFinite(p.Y) || !Hz.IsFinite(p.Z) || !xy.Add((p.X, p.Y)))
                throw new ArgumentException("TIN vertices must be finite and unique in XY; conflicting elevations are not merged.");
        var used = new HashSet<int>();
        var edges = new Dictionary<(int, int), int>();
        var triangles = new HashSet<(int, int, int)>();
        double area = 0;
        foreach (var (a, b, c) in surface.Faces)
        {
            if (new[] { a, b, c }.Any(i => i < 0 || i >= surface.Points.Count) || a == b || b == c || a == c)
                throw new ArgumentException("TIN face references are invalid.");
            var ids = new[] { a, b, c }; Array.Sort(ids);
            if (!triangles.Add((ids[0], ids[1], ids[2]))) throw new ArgumentException("Duplicate TIN face.");
            var pa = surface.Points[a]; var pb = surface.Points[b]; var pc = surface.Points[c];
            var twice = (pb.X - pa.X) * (pc.Y - pa.Y) - (pc.X - pa.X) * (pb.Y - pa.Y);
            if (!Hz.IsFinite(twice) || twice == 0) throw new ArgumentException("Degenerate TIN face.");
            area += Math.Abs(twice) / 2;
            foreach (var i in ids) used.Add(i);
            foreach (var (u, v) in new[] { (a, b), (b, c), (c, a) })
            {
                var edge = (Math.Min(u, v), Math.Max(u, v));
                edges[edge] = edges.GetValueOrDefault(edge) + 1;
                if (edges[edge] > 2) throw new ArgumentException("Non-manifold TIN edge.");
            }
        }
        if (used.Count != surface.Points.Count) throw new ArgumentException("Package must contain only vertices used by visible faces.");
        if (!Hz.IsFinite(area) || area <= 0) throw new ArgumentException("Invalid TIN plan area.");
        var hullArea = HullArea(surface.Points);
        var convexCoverage = Math.Abs(area - hullArea) <= Math.Max(1e-10, hullArea * 1e-9);
        var model = new LandXmlModel { Metric = linearUnit == "meter", ImperialLinearUnit = linearUnit == "meter" ? "foot" : linearUnit, AppVersion = version };
        model.Surfaces.Add(surface);
        var xml = Encoding.UTF8.GetBytes(LandXmlWriter.Write(model, RuntimeCompat.UnixEpoch));
        var obj = TerrainMeshObj.Write(surface, metresPerUnit);
        if (xml.Length > MaxPayloadBytes) throw new ArgumentException("Terrain XML exceeds the receiver's 64 MiB guard.");
        var ps = surface.Points;
        static JsonArray Point((double X, double Y, double Z) p, double factor) =>
            new(JsonValue.Create(p.X * factor), JsonValue.Create(p.Y * factor), JsonValue.Create(p.Z * factor));
        var controls = new JsonArray();
        // The first valid triangle gives three non-collinear plan controls. These
        // are exported positions, not independently surveyed placement evidence.
        var first = surface.Faces[0];
        foreach (var i in new[] { first.A, first.B, first.C })
            controls.Add(new JsonObject { ["point_id"] = i + 1, ["xyz_m"] = Point(ps[i], metresPerUnit) });
        var summary = new JsonObject
        {
            ["format"] = Format, ["landxml_entry"] = "terrain.xml", ["landxml_sha256"] = Hash(xml),
            ["exact_mesh"] = new JsonObject { ["entry"] = "terrain.obj", ["sha256"] = Hash(obj),
                ["linear_unit"] = "meter", ["axis_order"] = "east north elevation",
                ["vertices"] = surface.Points.Count, ["faces"] = surface.Faces.Count,
                ["connectivity_preserved"] = true, ["native_revit_receiver_available"] = false,
                ["placement_verified"] = false,
                ["note"] = "OBJ preserves the visible triangle indices and missing faces. The current Revit MCP has no typed mesh/DirectShape importer; this asset is not an editable Toposolid and is not a placement claim." },
            ["surface"] = surface.Name, ["source"] = source.DeepClone(),
            ["coordinate_system"] = coordinateSystem.DeepClone(), ["linear_unit"] = linearUnit,
            ["coordinates"] = "Civil drawing coordinates; LandXML is northing/easting/elevation. No translation, rotation or CRS reprojection applied.",
            ["revit_coordinate_requirement"] = "The receiver treats LandXML coordinates as Revit shared coordinates. Establish and independently check that relationship before applying.",
            ["shared_coordinates_verified"] = false, ["revit_import_verified"] = false,
            ["vertices"] = ps.Count, ["visible_faces"] = surface.Faces.Count,
            ["boundary_edges"] = edges.Count(e => e.Value == 1),
            ["plan_area_m2"] = area * metresPerUnit * metresPerUnit,
            ["convex_hull_area_m2"] = hullArea * metresPerUnit * metresPerUnit,
            ["convex_coverage"] = convexCoverage,
            ["elevation_m"] = new JsonObject { ["min"] = ps.Min(p => p.Z) * metresPerUnit,
                ["max"] = ps.Max(p => p.Z) * metresPerUnit, ["mean_of_vertices"] = ps.Average(p => p.Z) * metresPerUnit },
            ["bounds_m"] = new JsonObject { ["min"] = Point((ps.Min(p => p.X), ps.Min(p => p.Y), ps.Min(p => p.Z)), metresPerUnit),
                ["max"] = Point((ps.Max(p => p.X), ps.Max(p => p.Y), ps.Max(p => p.Z)), metresPerUnit) },
            ["placement_controls"] = controls,
            ["receiver"] = new JsonObject { ["tool"] = "horizun_create_elements", ["kind"] = "toposolid",
                ["minimum_revit_year"] = 2024, ["max_points"] = MaxPoints,
                ["requires"] = Hz.Strings(new[] { "target_document", "type_id", "level_id", "shared-coordinate check" }),
                ["triangulation"] = "Revit retriangulates points. Original triangle edges, breaklines, holes and concave boundaries are not guaranteed by the current point-only importer.",
                ["nonconvex_native_import_requires_review"] = !convexCoverage },
        };
        return new(xml, Encoding.UTF8.GetBytes(summary.ToJsonString(Hz.Indented)), summary) { MeshObj = obj };
    }

    public static string Hash(byte[] bytes) => RuntimeCompat.Hex(RuntimeCompat.Sha256(bytes));

    public static void Write(string path, Prepared prepared)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var (name, bytes) in Payloads(prepared))
        {
            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using var data = entry.Open(); data.Write(bytes);
        }
    }

    /// <summary>Reopen and compare every payload byte, not just geometry counts.</summary>
    public static bool Matches(string path, Prepared prepared)
    {
        using var zip = ZipFile.OpenRead(path);
        var payloads = Payloads(prepared).ToArray();
        if (zip.Entries.Count != payloads.Length) return false;
        foreach (var (name, expected) in payloads)
        {
            var entries = zip.Entries.Where(e => e.FullName == name).ToArray();
            if (entries.Length != 1 || entries[0].Length != expected.Length) return false;
            using var input = entries[0].Open();
            using var bytes = new MemoryStream(); input.CopyTo(bytes);
            if (!bytes.ToArray().AsSpan().SequenceEqual(expected)) return false;
        }
        var summary = LandXmlSummary.Read(Encoding.UTF8.GetString(prepared.LandXml));
        return summary.Surfaces.Count == 1 && summary.Alignments.Count == 0 &&
            summary.Surfaces.TryGetValue(Hz.Str(prepared.Summary, "surface")!, out var s) &&
            s.Points == Hz.Num(prepared.Summary, "vertices") && s.Faces == Hz.Num(prepared.Summary, "visible_faces");
    }

    private static IEnumerable<(string Name, byte[] Bytes)> Payloads(Prepared prepared)
    {
        yield return ("terrain.xml", prepared.LandXml);
        yield return ("manifest.json", prepared.Manifest);
        if (prepared.MeshObj != null) yield return ("terrain.obj", prepared.MeshObj);
    }

    private static double HullArea(List<(double X, double Y, double Z)> points)
    {
        var sorted = points.Select(p => (p.X, p.Y)).OrderBy(p => p.X).ThenBy(p => p.Y).ToArray();
        var hull = new List<(double X, double Y)>();
        static double Cross((double X, double Y) o, (double X, double Y) a, (double X, double Y) b) =>
            (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        foreach (var p in sorted)
        {
            while (hull.Count >= 2 && Cross(hull[^2], hull[^1], p) <= 0) hull.RemoveAt(hull.Count - 1);
            hull.Add(p);
        }
        var lower = hull.Count;
        foreach (var p in sorted.AsEnumerable().Reverse().Skip(1))
        {
            while (hull.Count > lower && Cross(hull[^2], hull[^1], p) <= 0) hull.RemoveAt(hull.Count - 1);
            hull.Add(p);
        }
        if (hull.Count < 4) throw new ArgumentException("TIN points are collinear.");
        // Relative origin prevents loss of area precision at large survey coordinates.
        double twice = 0;
        for (var i = 1; i < hull.Count - 1; i++) twice += Cross(hull[0], hull[i], hull[i + 1]);
        return Math.Abs(twice) / 2;
    }
}
