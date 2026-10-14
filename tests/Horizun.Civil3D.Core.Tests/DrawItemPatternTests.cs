using System.Text.Json.Nodes;
using Horizun.Civil3D.Core;

namespace Horizun.Civil3D.Core.Tests;

/// <summary>
/// Found live (v0.8.1): entities draw could not create MTEXT or TEXT. The items[] schema declared
/// pattern default "SOLID" (meant for hatch), the client filled it into every item and the mtext was refused
/// "items[0]: field 'pattern' is not used by mtext. Nothing ran." ToolRules.WithoutDefaults only strips
/// top-level fields, so item fields must not declare schema defaults; the hatch default lives in
/// CadInputs.HatchPattern.
/// </summary>
public class DrawItemPatternTests
{
    private static string? Draw(string items) =>
        ToolRules.Validate("horizun_c3d_entities", JsonNode.Parse("{\"action\":\"draw\",\"target_document\":\"d.dwg\",\"items\":[" + items + "]}")!.AsObject());

    private const string Mtext = "{\"type\":\"mtext\",\"position\":{\"x\":10,\"y\":20},\"text\":\"EJE 6\",\"height\":4.83052,\"layer\":\"EJE VIA\"}";
    private const string Text = "{\"type\":\"text\",\"position\":{\"x\":10,\"y\":30},\"text\":\"EJE 6\",\"height\":4.83052}";
    private const string Hatch = "{\"type\":\"hatch\",\"boundaries\":[\"2A\"]}";

    [Fact]
    public void Mtext_text_and_hatch_without_pattern_pass() => Assert.Null(Draw(Mtext + "," + Text + "," + Hatch));

    [Fact]
    public void Hatch_without_pattern_is_solid()
    {
        Assert.Equal("SOLID", CadInputs.HatchPattern(JsonNode.Parse(Hatch)!.AsObject()));
        Assert.Equal("ANSI31", CadInputs.HatchPattern(JsonNode.Parse("{\"type\":\"hatch\",\"boundaries\":[\"2A\"],\"pattern\":\"ANSI31\"}")!.AsObject()));
    }

    [Fact]
    public void Item_schema_declares_no_pattern_default()
    {
        var pattern = (JsonObject)Contract.Find("horizun_c3d_entities")!.InputSchema["properties"]!["items"]!["items"]!["properties"]!["pattern"]!;
        Assert.False(pattern.ContainsKey("default"));
    }

    [Fact]
    public void A_null_pattern_on_mtext_is_ignored() =>
        Assert.Null(Draw(Mtext.TrimEnd('}') + ",\"pattern\":null}"));

    [Fact]
    public void A_pattern_on_mtext_is_still_refused_by_name() =>
        Assert.Contains("'pattern' is not used by mtext", Draw(Mtext.TrimEnd('}') + ",\"pattern\":\"SOLID\"}"));

    /// <summary>Nested defaults that predate this guard. Same risk (a filled breaklines.max_distance=0 with
    /// kind=proximity is refused "applies to kind=standard only"); remove each entry when its default is moved out.</summary>
    private static readonly string[] KnownNestedDefaults =
    {
        "horizun_c3d_surface/breaklines/properties/kind",
        "horizun_c3d_surface/breaklines/properties/mid_ordinate",
        "horizun_c3d_surface/breaklines/properties/max_distance",
        "horizun_c3d_surface/breaklines/properties/weeding_distance",
        "horizun_c3d_surface/breaklines/properties/weeding_angle",
        "horizun_c3d_surface/boundaries/properties/non_destructive",
        "horizun_c3d_surface/boundaries/properties/mid_ordinate",
    };

    /// <summary>The class of bug: a "default" below the top-level properties is filled by clients where it does not
    /// apply, and WithoutDefaults cannot strip it. No NEW nested default may appear.</summary>
    [Fact]
    public void No_tool_declares_a_new_schema_default_below_the_top_level()
    {
        var found = new List<string>();
        void Walk(JsonNode? n, string path, bool propertyMap = false)
        {
            switch (n)
            {
                case JsonObject o:
                    // A "properties" map may hold a FIELD named "default" (block attributes); only schemas count.
                    if (!propertyMap && o.ContainsKey("default")) found.Add(path);
                    foreach (var (k, v) in o) Walk(v, path + "/" + k, !propertyMap && k == "properties");
                    break;
                case JsonArray a:
                    for (var i = 0; i < a.Count; i++) Walk(a[i], path + "/" + i);
                    break;
            }
        }
        foreach (var c in Contract.All)
            if (c.InputSchema["properties"] is JsonObject props)
                foreach (var (k, p) in props)
                    if (p is JsonObject po)
                        foreach (var (k2, v) in po)
                            if (k2 != "default") Walk(v, c.Name + "/" + k + "/" + k2, k2 == "properties");
        Assert.Empty(found.Except(KnownNestedDefaults));
    }
}
