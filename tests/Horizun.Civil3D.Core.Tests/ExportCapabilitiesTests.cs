using System.Text.Json.Nodes;
using System.Xml.Linq;
using Horizun.Civil3D.Core;

namespace Horizun.Civil3D.Core.Tests;

public class ExportCapabilitiesTests
{
    [Theory]
    [InlineData("C:\\exports\\copy.dwg", true)]
    [InlineData("C:\\exports\\copy.DWG", true)]
    [InlineData("copy.dwg", false)]
    [InlineData("C:copy.dwg", false)]
    [InlineData("\\exports\\copy.dwg", false)]
    [InlineData("C:\\exports\\copy.pdf", false)]
    public void Dwg_export_requires_a_fully_qualified_destination(string output, bool valid)
    {
        // Contract path validation follows host semantics. These examples exercise
        // the Windows product rather than pretending Windows paths are Linux paths.
        if (!OperatingSystem.IsWindows()) return;
        var args = new JsonObject { ["action"] = "export_dwg", ["target_document"] = "fixture.dwg", ["output"] = output };
        Assert.Equal(valid, ToolRules.Validate("horizun_c3d_exchange", args) == null);
    }

    [Fact]
    public void File_exports_are_denied_under_safe_write()
    {
        var settings = new Settings { Profile = PermissionProfile.SafeWrite };
        foreach (var pair in new[] { ("horizun_c3d_exchange", "export_dwg"), ("horizun_c3d_exchange", "export_landxml"), ("horizun_c3d_exchange", "export_revit"), ("horizun_c3d_points", "export_csv") })
        {
            var tool = Contract.Find(pair.Item1)!;
            Assert.NotNull(settings.Refusal(tool.Name, tool.EffectFor(new JsonObject { ["action"] = pair.Item2 })));
        }
    }

    [Theory]
    [InlineData("foot")]
    [InlineData("USSurveyFoot")]
    public void Landxml_preserves_the_selected_foot_definition_without_rescaling(string unit)
    {
        var model = new LandXmlModel { Metric = false, ImperialLinearUnit = unit };
        model.Surfaces.Add(new LxSurface("fixture", "", new() { (1000000, 2000000, 10), (1000001, 2000000, 11), (1000000, 2000001, 12) }, new() { (0, 1, 2) }));
        var xml = XDocument.Parse(LandXmlWriter.Write(model, DateTime.UtcNow));
        XNamespace ns = LandXmlWriter.Ns;
        Assert.Equal(unit, xml.Root!.Element(ns + "Units")!.Element(ns + "Imperial")!.Attribute("linearUnit")!.Value);
        Assert.Equal("2000000 1000000 10", xml.Descendants(ns + "P").First().Value);
    }

    [Fact]
    public void Landxml_refuses_an_ambiguous_foot_definition()
    {
        Assert.Throws<ArgumentException>(() => LandXmlWriter.Write(new LandXmlModel { Metric = false, ImperialLinearUnit = "feet" }, DateTime.UtcNow));
    }

    [Theory]
    [InlineData("pressure_list", false, null, null, null, true)]
    [InlineData("pressure_get", false, "fixture", null, null, true)]
    [InlineData("pressure_get", false, null, null, null, false)]
    [InlineData("pressure_list", false, null, 0.0, null, false)]
    [InlineData("pressure_list", false, null, 501.0, null, false)]
    [InlineData("pressure_list", false, null, 1.5, null, false)]
    [InlineData("pressure_list", false, null, 100.0, -1.0, false)]
    [InlineData("pressure_create_network", true, null, null, null, true)]
    [InlineData("pressure_create_network", false, null, null, null, false)]
    [InlineData("pressure_rename", true, "fixture", null, null, true)]
    public void Pressure_rules_protect_writes_and_bound_pagination(string action, bool targeted, string? network, double? limit, double? offset, bool valid)
    {
        var args = new JsonObject { ["action"] = action };
        if (targeted) args["target_document"] = "fixture.dwg";
        if (network != null) args["network"] = network;
        if (limit.HasValue) args["limit"] = limit.Value;
        if (offset.HasValue) args["offset"] = offset.Value;
        if (action is "pressure_create_network" or "pressure_rename") args["new_name"] = "fixture_new";
        Assert.Equal(valid, ToolRules.Validate("horizun_c3d_pipes", args) == null);
    }
}
