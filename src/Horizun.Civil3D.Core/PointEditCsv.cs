using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Horizun.Civil3D.Core;

/// <summary>Bounded spreadsheet roundtrip. Point number, source fingerprint and
/// unit are locked. Every original row is required; creation/deletion is refused.</summary>
public static class PointEditCsv
{
    public const int MaxRows = 10_000;
    public const int MaxBytes = 8 * 1024 * 1024;
    public const string Header = "source_sha256,linear_unit,number,easting,northing,elevation,description";
    public sealed record Snapshot(string Fingerprint, string LinearUnit, string SourceIdentity, IReadOnlyList<PointFile.Row> Rows);
    public sealed record Edit(PointFile.Row Before, PointFile.Row After);
    private static string F(double n) => n.ToString("R", CultureInfo.InvariantCulture);

    public static Snapshot Capture(IEnumerable<PointFile.Row> rows, string linearUnit, string sourceIdentity)
    {
        if (linearUnit is not ("meter" or "foot" or "USSurveyFoot")) throw new ArgumentException("Explicit Civil linear units are required.");
        if (string.IsNullOrWhiteSpace(sourceIdentity)) throw new ArgumentException("A drawing identity/revision is required.");
        var sorted = rows.OrderBy(p => p.Number).ToArray();
        if (sorted.Length is < 1 or > MaxRows) throw new ArgumentException("Editable CSV needs 1..10000 points.");
        var numbers = new HashSet<uint>();
        foreach (var row in sorted)
        {
            if (row.Number is not { } n || n == 0 || !numbers.Add(n)) throw new ArgumentException("Unique positive point numbers are required.");
            ValidateRow(row);
        }
        var payload = new JsonObject { ["source"] = sourceIdentity, ["linear_unit"] = linearUnit,
            ["points"] = new JsonArray(sorted.Select(p => (JsonNode)new JsonObject { ["number"] = (long)p.Number!.Value,
                ["x"] = p.X, ["y"] = p.Y, ["z"] = p.Z, ["description"] = p.Description }).ToArray()) };
        var hash = RevitTerrainPackage.Hash(Encoding.UTF8.GetBytes(payload.ToJsonString(Hz.Compact)));
        return new(hash, linearUnit, sourceIdentity, sorted);
    }

    public static string Write(Snapshot snapshot)
    {
        var result = new StringBuilder(Header + "\n");
        foreach (var row in snapshot.Rows)
            result.Append(snapshot.Fingerprint).Append(',').Append(snapshot.LinearUnit).Append(',')
                .Append(row.Number!.Value.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(F(row.X)).Append(',').Append(F(row.Y)).Append(',').Append(F(row.Z)).Append(',')
                .Append(Quote(row.Description)).Append('\n');
        if (Encoding.UTF8.GetByteCount(result.ToString()) > MaxBytes) throw new ArgumentException("Editable CSV exceeds 8 MiB.");
        return result.ToString();
    }

    public static IReadOnlyList<Edit> ReadEdits(string csv, Snapshot current)
    {
        if (Encoding.UTF8.GetByteCount(csv) > MaxBytes) throw new ArgumentException("Editable CSV exceeds 8 MiB.");
        var lines = csv.Replace("\r\n", "\n").Split('\n');
        if (lines.Length == 0 || lines[0].TrimStart('\uFEFF') != Header) throw new ArgumentException("Editable CSV header is invalid; use comma delimiters and invariant decimals.");
        var baseline = current.Rows.ToDictionary(p => p.Number!.Value);
        var seen = new HashSet<uint>(); var edits = new List<Edit>();
        foreach (var line in lines.Skip(1))
        {
            if (line.Length == 0) continue;
            var cells = Cells(line);
            if (cells.Length != 7) throw new ArgumentException("Each editable CSV row must have seven fields.");
            if (cells[0] != current.Fingerprint) throw new ArgumentException("The source drawing/points changed since export. Export again before editing.");
            if (cells[1] != current.LinearUnit) throw new ArgumentException("Linear units changed; no automatic conversion is allowed.");
            if (!uint.TryParse(cells[2], NumberStyles.None, CultureInfo.InvariantCulture, out var n) || !seen.Add(n) || !baseline.TryGetValue(n, out var before))
                throw new ArgumentException("Point numbers cannot be changed, created or duplicated.");
            double Number(string cell) => double.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && Hz.IsFinite(v)
                ? v : throw new ArgumentException("Point coordinates must be finite invariant decimals, not spreadsheet formulas.");
            var after = new PointFile.Row(n, Number(cells[3]), Number(cells[4]), Number(cells[5]), cells[6]);
            ValidateRow(after);
            if (before != after) edits.Add(new(before, after));
        }
        if (seen.Count != baseline.Count) throw new ArgumentException("All exported rows must remain; this exchange cannot delete points.");
        return edits;
    }

    private static void ValidateRow(PointFile.Row row)
    {
        if (!Hz.IsFinite(row.X) || !Hz.IsFinite(row.Y) || !Hz.IsFinite(row.Z)) throw new ArgumentException("Point coordinates must be finite.");
        if (row.Description == null || row.Description.Length > 1024 || row.Description.Any(c => char.IsControl(c))) throw new ArgumentException("Descriptions must contain at most 1024 characters without control characters.");
        var leading = row.Description.TrimStart();
        if (leading.Length > 0 && "=+-@".IndexOf(leading[0]) >= 0) throw new ArgumentException("Formula-like descriptions are refused for spreadsheet safety; use plain text.");
    }

    private static string Quote(string text) => "\"" + text.Replace("\"", "\"\"") + "\"";
    private static string[] Cells(string line)
    {
        var result = new List<string>(); var text = new StringBuilder(); var quoted = false; var closed = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"') { text.Append('"'); i++; }
                else if (c == '"') { quoted = false; closed = true; }
                else text.Append(c);
            }
            else if (c == ',') { result.Add(text.ToString()); text.Clear(); closed = false; }
            else if (c == '"' && text.Length == 0 && !closed) quoted = true;
            else if (closed || c == '"') throw new ArgumentException("Malformed CSV quoting.");
            else text.Append(c);
        }
        if (quoted) throw new ArgumentException("Unterminated CSV quote.");
        result.Add(text.ToString()); return result.ToArray();
    }
}
