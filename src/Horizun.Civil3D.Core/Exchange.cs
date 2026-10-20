// -----------------------------------------------------------------------------
// Horizun Civil 3D MCP - our own exchange formats (no Civil 3D needed, so they
// are unit tested): LandXML 1.2 writer + reader summary, point files.
//
// Civil 3D 2025 has no public .NET LandXML export; the plug-in reads the
// drawing into LandXmlModel and this writer produces the file, which is then
// re-read with LandXmlSummary to verify counts and lengths.
// LandXML coordinate order is "northing easting [elevation]" (Y X Z).
// -----------------------------------------------------------------------------
using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Horizun.Civil3D.Core;

public sealed class LandXmlModel
{
    public bool Metric = true;
    public string ImperialLinearUnit = "foot";
    public string AppVersion = "";
    public List<LxSurface> Surfaces = new();
    public List<LxAlignment> Alignments = new();
}

public sealed record LxSurface(string Name, string Description, List<(double X, double Y, double Z)> Points, List<(int A, int B, int C)> Faces);

public sealed record LxElement(string Kind, double StaStart, double Length, (double X, double Y) Start, (double X, double Y) End,
                               (double X, double Y)? Center = null, double Radius = 0, bool Clockwise = false);

public sealed record LxPvi(double Station, double Elevation, double CurveLength);

public sealed record LxProfile(string Name, List<LxPvi> Pvis);

public sealed record LxAlignment(string Name, string Description, double StaStart, double Length, List<LxElement> Elements, List<LxProfile> Profiles);

public static class LandXmlWriter
{
    public const string Ns = "http://www.landxml.org/schema/LandXML-1.2";
    private static string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);
    private static string NE(double x, double y) => F(y) + " " + F(x);

    public static string Write(LandXmlModel m, DateTime now)
    {
        if (!m.Metric && m.ImperialLinearUnit is not ("foot" or "USSurveyFoot"))
            throw new ArgumentException("Imperial linear units must identify foot or USSurveyFoot.", nameof(m));
        XNamespace ns = Ns;
        var root = new XElement(ns + "LandXML",
            new XAttribute("version", "1.2"), new XAttribute("date", now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            new XAttribute("time", now.ToString("HH:mm:ss", CultureInfo.InvariantCulture)),
            new XElement(ns + "Units", m.Metric
                ? new XElement(ns + "Metric", new XAttribute("areaUnit", "squareMeter"), new XAttribute("linearUnit", "meter"), new XAttribute("volumeUnit", "cubicMeter"),
                    new XAttribute("temperatureUnit", "celsius"), new XAttribute("pressureUnit", "milliBars"), new XAttribute("angularUnit", "decimal degrees"), new XAttribute("directionUnit", "decimal degrees"))
                : new XElement(ns + "Imperial", new XAttribute("areaUnit", "squareFoot"), new XAttribute("linearUnit", m.ImperialLinearUnit), new XAttribute("volumeUnit", "cubicYard"),
                    new XAttribute("temperatureUnit", "fahrenheit"), new XAttribute("pressureUnit", "inHG"), new XAttribute("angularUnit", "decimal degrees"), new XAttribute("directionUnit", "decimal degrees"))),
            new XElement(ns + "Application", new XAttribute("name", "Horizun Civil 3D MCP"), new XAttribute("manufacturer", "Horizun Group"), new XAttribute("version", m.AppVersion)));
        if (m.Surfaces.Count > 0)
        {
            var ss = new XElement(ns + "Surfaces");
            foreach (var s in m.Surfaces)
            {
                var pnts = new XElement(ns + "Pnts");
                for (var i = 0; i < s.Points.Count; i++) pnts.Add(new XElement(ns + "P", new XAttribute("id", i + 1), NE(s.Points[i].X, s.Points[i].Y) + " " + F(s.Points[i].Z)));
                var faces = new XElement(ns + "Faces");
                foreach (var f in s.Faces) faces.Add(new XElement(ns + "F", (f.A + 1) + " " + (f.B + 1) + " " + (f.C + 1)));
                ss.Add(new XElement(ns + "Surface", new XAttribute("name", s.Name), new XAttribute("desc", s.Description),
                    new XElement(ns + "Definition", new XAttribute("surfType", "TIN"), pnts, faces)));
            }
            root.Add(ss);
        }
        if (m.Alignments.Count > 0)
        {
            var als = new XElement(ns + "Alignments");
            foreach (var a in m.Alignments)
            {
                var cg = new XElement(ns + "CoordGeom");
                foreach (var e in a.Elements)
                {
                    if (e.Kind == "Line")
                        cg.Add(new XElement(ns + "Line", new XAttribute("staStart", F(e.StaStart)), new XAttribute("length", F(e.Length)),
                            new XElement(ns + "Start", NE(e.Start.X, e.Start.Y)), new XElement(ns + "End", NE(e.End.X, e.End.Y))));
                    else
                        cg.Add(new XElement(ns + "Curve", new XAttribute("rot", e.Clockwise ? "cw" : "ccw"), new XAttribute("staStart", F(e.StaStart)),
                            new XAttribute("length", F(e.Length)), new XAttribute("radius", F(e.Radius)),
                            new XElement(ns + "Start", NE(e.Start.X, e.Start.Y)), new XElement(ns + "Center", NE(e.Center!.Value.X, e.Center.Value.Y)),
                            new XElement(ns + "End", NE(e.End.X, e.End.Y))));
                }
                var al = new XElement(ns + "Alignment", new XAttribute("name", a.Name), new XAttribute("desc", a.Description),
                    new XAttribute("staStart", F(a.StaStart)), new XAttribute("length", F(a.Length)), cg);
                foreach (var p in a.Profiles)
                {
                    var pa = new XElement(ns + "ProfAlign", new XAttribute("name", p.Name));
                    foreach (var v in p.Pvis)
                        pa.Add(v.CurveLength > 0
                            ? new XElement(ns + "ParaCurve", new XAttribute("length", F(v.CurveLength)), F(v.Station) + " " + F(v.Elevation))
                            : new XElement(ns + "PVI", F(v.Station) + " " + F(v.Elevation)));
                    al.Add(new XElement(ns + "Profile", new XAttribute("name", p.Name), pa));
                }
                als.Add(al);
            }
            root.Add(als);
        }
        var sb = new StringBuilder();
        using (var w = XmlWriter.Create(sb, new XmlWriterSettings { Indent = true, Encoding = Encoding.UTF8, OmitXmlDeclaration = true }))
            new XDocument(root).Save(w);
        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" + sb;
    }
}

/// <summary>What a LandXML file actually holds (used to verify our own export by re-reading it).</summary>
public sealed record LandXmlSummary(Dictionary<string, (int Points, int Faces, double MinZ, double MaxZ)> Surfaces,
                                    Dictionary<string, (double Length, double ElementLength, int Elements, Dictionary<string, int> ProfilePvis)> Alignments)
{
    private static double D(string s) => double.Parse(s, CultureInfo.InvariantCulture);

    public static LandXmlSummary Read(string xml)
    {
        var doc = XDocument.Parse(xml);
        XNamespace ns = LandXmlWriter.Ns;
        var surfaces = new Dictionary<string, (int, int, double, double)>();
        foreach (var s in doc.Descendants(ns + "Surface"))
        {
            var pts = s.Descendants(ns + "P").Select(p => p.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToList();
            var zs = pts.Where(p => p.Length >= 3).Select(p => D(p[2])).ToList();
            var faces = s.Descendants(ns + "F").ToList();
            var maxId = pts.Count;
            if (faces.Any(f => f.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(id => int.Parse(id, CultureInfo.InvariantCulture) is var n && (n < 1 || n > maxId))))
                throw new FormatException("Surface '" + (string?)s.Attribute("name") + "' has a face that references a missing point.");
            surfaces[(string?)s.Attribute("name") ?? ""] = (pts.Count, faces.Count, zs.Count > 0 ? zs.Min() : double.NaN, zs.Count > 0 ? zs.Max() : double.NaN);
        }
        var als = new Dictionary<string, (double, double, int, Dictionary<string, int>)>();
        foreach (var a in doc.Descendants(ns + "Alignment"))
        {
            var cg = a.Element(ns + "CoordGeom");
            var elems = cg?.Elements().ToList() ?? new List<XElement>();
            var profs = a.Elements(ns + "Profile").ToDictionary(p => (string?)p.Attribute("name") ?? "", p => p.Descendants().Count(x => x.Name == ns + "PVI" || x.Name == ns + "ParaCurve"));
            als[(string?)a.Attribute("name") ?? ""] = (D((string?)a.Attribute("length") ?? "0"), elems.Sum(e => D((string?)e.Attribute("length") ?? "0")), elems.Count, profs);
        }
        return new LandXmlSummary(surfaces, als);
    }
}

/// <summary>Point files (CSV/TXT, comma, semicolon, tab or space separated) in the usual Civil 3D formats.</summary>
public static class PointFile
{
    public sealed record Row(uint? Number, double X, double Y, double Z, string Description);

    public static List<Row> Parse(string text, string format, bool skipHeader, out List<string> errors)
    {
        errors = new List<string>();
        var rows = new List<Row>();
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var start = skipHeader ? 1 : 0;
        for (var i = start; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var sep = line.Contains(';') ? ';' : line.Contains(',') ? ',' : line.Contains('\t') ? '\t' : ' ';
            var f = sep == ' ' ? line.Split(' ', StringSplitOptions.RemoveEmptyEntries) : Fields(line, sep);
            var need = format.Length - (format.EndsWith('D') ? 1 : 0);
            if (f.Length < need) { if (errors.Count < 20) errors.Add("line " + (i + 1) + ": expected " + format + ", got " + f.Length + " fields"); continue; }
            uint? num = null; double x = 0, y = 0, z = 0; var desc = "";
            var ok = true;
            for (var k = 0; k < format.Length && ok; k++)
            {
                var v = k < f.Length ? f[k] : "";
                switch (format[k])
                {
                    case 'P': ok = uint.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0; num = n; break;
                    case 'N': ok = double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out y); break;
                    case 'E': ok = double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out x); break;
                    case 'Z': ok = double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out z); break;
                    case 'D': desc = string.Join(sep == ' ' ? " " : sep.ToString(), f.Skip(k)); k = format.Length; break;
                }
            }
            if (!ok || !Hz.IsFinite(x) || !Hz.IsFinite(y) || !Hz.IsFinite(z)) { if (errors.Count < 20) errors.Add("line " + (i + 1) + ": not " + format + ": " + line); continue; }
            rows.Add(new Row(num, x, y, z, desc));
        }
        return rows;
    }

    /// <summary>Quote-aware split: "a, b" stays one field (quotes removed, inner text kept verbatim; "" = one quote).</summary>
    private static string[] Fields(string line, char sep)
    {
        var fields = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        var wasQuoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"') { cell.Append('"'); i++; }
                else if (ch == '"') quoted = false;
                else cell.Append(ch);
            }
            else if (ch == '"' && cell.ToString().Trim().Length == 0) { quoted = true; wasQuoted = true; cell.Clear(); }
            else if (ch == sep) { fields.Add(wasQuoted ? cell.ToString() : cell.ToString().Trim()); cell.Clear(); wasQuoted = false; }
            else if (!(wasQuoted && !quoted)) cell.Append(ch);
        }
        fields.Add(wasQuoted ? cell.ToString() : cell.ToString().Trim());
        return fields.ToArray();
    }

    public static string Write(IEnumerable<Row> rows, string format)
    {
        var sb = new StringBuilder();
        foreach (var r in rows)
        {
            var parts = format.Select(c => c switch
            {
                'P' => r.Number?.ToString(CultureInfo.InvariantCulture) ?? "",
                'N' => r.Y.ToString("0.####", CultureInfo.InvariantCulture),
                'E' => r.X.ToString("0.####", CultureInfo.InvariantCulture),
                'Z' => r.Z.ToString("0.####", CultureInfo.InvariantCulture),
                _ => r.Description.Contains(',') ? "\"" + r.Description.Replace("\"", "\"\"") + "\"" : r.Description,
            });
            sb.Append(string.Join(",", parts)).Append('\n');
        }
        return sb.ToString();
    }
}
