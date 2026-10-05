using Horizun.Civil3D.Core;

namespace Horizun.Civil3D.Core.Tests;

public class PermissionRegressionTests
{
    [Theory]
    [InlineData("{\"paused\":\"true\"}")]
    [InlineData("{\"paused\":null}")]
    [InlineData("{\"paused\":1}")]
    [InlineData("{\"enable_execute_csharp\":\"true\"}")]
    [InlineData("{\"enable_execute_csharp\":null}")]
    [InlineData("{\"allowed_tools\":\"horizun_c3d_health\"}")]
    [InlineData("{\"allowed_tools\":null}")]
    [InlineData("{\"allowed_tools\":[\"horizun_c3d_health\",123]}")]
    [InlineData("{\"denied_tools\":[123]}")]
    [InlineData("{\"denied_tools\":[null]}")]
    [InlineData("{\"denied_tools\":[\"  \"]}")]
    [InlineData("{\"permission_profile\":null}")]
    [InlineData("{\"permission_profile\":true}")]
    [InlineData("{\"pause\":true}")]
    [InlineData("{\"paused\":true,\"paused\":false}")]
    [InlineData("{\"permission_profile\":\"unsafe_code\",\"enable_execute_csharp\":true,\"denied_tools\":{}}")]
    public void Invalid_control_fields_never_allow_writes(string json)
    {
        var settings = Settings.Parse(json);
        Assert.True(settings.FailedClosed);
        Assert.Equal(PermissionProfile.ReadOnly, settings.Profile);
        Assert.NotNull(settings.Refusal("horizun_c3d_document", ToolEffect.FullWrite));
        Assert.NotNull(settings.Refusal("horizun_c3d_surface", ToolEffect.SafeWrite));
        Assert.NotNull(settings.Refusal("horizun_c3d_execute_csharp", ToolEffect.UnsafeCode));
    }

    [Fact]
    public void Invalid_file_path_fails_closed_instead_of_treating_it_as_missing()
    {
        var settings = Settings.Load(Path.GetTempPath()); // A directory cannot be read as a settings file.
        Assert.True(settings.FailedClosed);
        Assert.Equal(PermissionProfile.ReadOnly, settings.Profile);
    }

    [Fact]
    public void Valid_controls_keep_pause_deny_and_allowlist_enforced()
    {
        var settings = Settings.Parse("""
            {"permission_profile":"full_write","paused":true,"enable_execute_csharp":false,
             "allowed_tools":[" horizun_c3d_document "],"denied_tools":["horizun_c3d_document"]}
            """);
        Assert.False(settings.FailedClosed);
        Assert.True(settings.Paused);
        Assert.Contains("horizun_c3d_document", settings.AllowedTools);
        Assert.NotNull(settings.Refusal("horizun_c3d_document", ToolEffect.FullWrite));
        Assert.Null(settings.Refusal("horizun_c3d_health", ToolEffect.Read));
    }
}
