using System.Text.Json.Nodes;
using Horizun.Civil3D.Core;

namespace Horizun.Civil3D.Core.Tests;

/// <summary>Which calls may name an open drawing that is not the active window (reads only, never writes).</summary>
public class DocumentScopeTests
{
    private static IEnumerable<string> Actions(ToolContract c) =>
        ((c.InputSchema["properties"] as JsonObject)?["action"]?["enum"] as JsonArray)?.Select(n => n!.GetValue<string>()) ?? Enumerable.Empty<string>();

    [Fact]
    public void Every_listed_pair_is_a_real_read_action()
    {
        var all = DocumentScope.All();
        Assert.NotEmpty(all);
        foreach (var (tool, actions) in all)
        {
            var c = Contract.Find(tool);
            Assert.NotNull(c);
            Assert.NotEmpty(actions); // a listed tool whose actions all fail the read gate is a list mistake
            foreach (var a in actions)
            {
                Assert.Contains(a, Actions(c!));
                Assert.Equal(ToolEffect.Read, c!.EffectFor(new JsonObject { ["action"] = a }));
            }
        }
    }

    [Fact]
    public void No_write_action_of_any_tool_may_target_a_non_active_drawing()
    {
        foreach (var c in Contract.All)
            foreach (var a in Actions(c))
                if (c.EffectFor(new JsonObject { ["action"] = a }) != ToolEffect.Read)
                    Assert.False(DocumentScope.AllowsNonActive(c.Name, a), c.Name + " " + a);
    }

    [Theory]
    [InlineData("horizun_c3d_entities", "query", true)]
    [InlineData("horizun_c3d_surface", "get", true)]
    [InlineData("horizun_c3d_document", "geo", true)]
    [InlineData("horizun_c3d_layouts", "list", true)]
    [InlineData("horizun_c3d_surface", "volumes_report", false)] // aborted write transaction
    [InlineData("horizun_c3d_sections", "get_section", false)]   // pending sections must never be read read-only
    [InlineData("horizun_c3d_exchange", "shortcuts_status", false)]
    [InlineData("horizun_c3d_document", "save", false)]
    [InlineData("horizun_c3d_document", "list_open", false)]      // not drawing-bound
    [InlineData("horizun_c3d_execute_csharp", null, false)]
    [InlineData("horizun_c3d_entities", "erase", false)]
    [InlineData("horizun_c3d_entities", null, false)]
    [InlineData("horizun_c3d_nope", "list", false)]
    public void Policy(string tool, string? action, bool allowed) => Assert.Equal(allowed, DocumentScope.AllowsNonActive(tool, action));

    [Fact]
    public void Target_document_descriptions_point_to_the_published_list()
    {
        foreach (var c in Contract.All)
            if ((c.InputSchema["properties"] as JsonObject)?["target_document"] is JsonObject td && DocumentScope.NonActiveActions(c.Name).Count > 0)
                Assert.Contains("non_active_reads", Hz.Str(td, "description"));
    }
}
