using Horizun.Civil3D.Core;

namespace Horizun.Civil3D.Core.Tests;

public class PointEditCsvTests
{
    private static PointFile.Row[] Points() => new[] { new PointFile.Row(1, 1000000.123456789, 2000000, 100, "Bench, \"north\""), new PointFile.Row(2, 1000010, 2000010, 101, "Control") };
    private static PointEditCsv.Snapshot Snapshot(string unit = "meter", string revision = "fixture.dwg:revision-1") => PointEditCsv.Capture(Points(), unit, revision);

    [Theory]
    [InlineData("meter")]
    [InlineData("foot")]
    [InlineData("USSurveyFoot")]
    public void Roundtrip_preserves_survey_precision_quotes_and_explicit_units(string unit)
    {
        var snapshot = Snapshot(unit); var csv = PointEditCsv.Write(snapshot);
        Assert.Empty(PointEditCsv.ReadEdits(csv, snapshot));
        Assert.Equal(Points()[0].X, double.Parse(csv.Split('\n')[1].Split(',')[3], System.Globalization.CultureInfo.InvariantCulture));
        var changed = csv.Replace(",101,\"Control\"", ",101.25,\"Control edited\"");
        var edit = Assert.Single(PointEditCsv.ReadEdits(changed, snapshot));
        Assert.Equal((uint)2, edit.Before.Number); Assert.Equal(101.25, edit.After.Z);
        Assert.Equal("Control edited", edit.After.Description);
    }

    [Theory]
    [InlineData("revision")]
    [InlineData("geometry")]
    [InlineData("units")]
    public void Refuses_stale_source_or_changed_unit_without_any_partial_edit(string change)
    {
        var snapshot = Snapshot(); var csv = PointEditCsv.Write(snapshot);
        var points = Points(); points[0] = points[0] with { Z = 99 };
        var now = change switch { "revision" => Snapshot(revision: "fixture.dwg:revision-2"), "geometry" => PointEditCsv.Capture(points, "meter", snapshot.SourceIdentity), _ => Snapshot("foot") };
        Assert.Throws<ArgumentException>(() => PointEditCsv.ReadEdits(csv, now));
    }

    [Theory]
    [InlineData("deleted")]
    [InlineData("duplicate")]
    [InlineData("renumbered")]
    [InlineData("formula")]
    [InlineData("nan")]
    [InlineData("quoting")]
    public void Refuses_structural_or_numeric_spreadsheet_damage(string issue)
    {
        var snapshot = Snapshot(); var csv = PointEditCsv.Write(snapshot);
        csv = issue switch {
            "deleted" => string.Join("\n", csv.Split('\n').Take(2)),
            "duplicate" => csv + csv.Split('\n')[1] + "\n",
            "renumbered" => csv.Replace(",meter,2,", ",meter,3,"),
            "formula" => csv.Replace("\"Control\"", "\"=WEBSERVICE(1)\""),
            "nan" => csv.Replace(",101,", ",NaN,"),
            _ => csv.Replace("\"Control\"", "\"Control\"junk") };
        Assert.Throws<ArgumentException>(() => PointEditCsv.ReadEdits(csv, snapshot));
    }

    [Fact]
    public void Capture_refuses_duplicates_nonfinite_values_and_formula_descriptions()
    {
        var rows = Points(); rows[1] = rows[1] with { Number = 1 };
        Assert.Throws<ArgumentException>(() => PointEditCsv.Capture(rows, "meter", "fixture"));
        rows = Points(); rows[1] = rows[1] with { Description = " @SUM(1)" };
        Assert.Throws<ArgumentException>(() => PointEditCsv.Capture(rows, "meter", "fixture"));
    }
}
