using System.Text.Json.Nodes;
using Horizun.Civil3D.Core;

namespace Horizun.Civil3D.Core.Tests;

/// <summary>The "Canal C#" switch: on/off, restore of the previous profile, end of session, never rewrites an invalid file.</summary>
public class CSharpChannelTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "hz-csharp-" + Guid.NewGuid().ToString("N"));
    private string PathFile => Path.Combine(_dir, "settings.json");

    public CSharpChannelTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void Enable_from_no_file_then_disable_restores_safe_write()
    {
        var on = CSharpChannel.Enable(PathFile);
        Assert.True(CSharpChannel.IsOn(on));
        Assert.Null(on.Refusal("horizun_c3d_execute_csharp", ToolEffect.UnsafeCode));
        Assert.Equal("safe_write", Hz.Str(JsonNode.Parse(File.ReadAllText(PathFile))!.AsObject(), CSharpChannel.RestoreKey));

        var off = CSharpChannel.Disable(PathFile);
        Assert.False(CSharpChannel.IsOn(off));
        Assert.Equal(PermissionProfile.SafeWrite, off.Profile);
        Assert.False(off.EnableExecuteCSharp);
        Assert.Null(JsonNode.Parse(File.ReadAllText(PathFile))![CSharpChannel.RestoreKey]);
        Assert.NotNull(off.Refusal("horizun_c3d_execute_csharp", ToolEffect.UnsafeCode));
    }

    [Fact]
    public void Previous_profile_and_other_keys_survive_a_round_trip()
    {
        File.WriteAllText(PathFile, "{\"permission_profile\":\"full_write\",\"denied_tools\":[\"horizun_c3d_cleanup\"]}");
        CSharpChannel.Enable(PathFile);
        CSharpChannel.Enable(PathFile); // idempotent: the saved profile stays full_write
        var off = CSharpChannel.Disable(PathFile);
        Assert.Equal(PermissionProfile.FullWrite, off.Profile);
        Assert.Contains("horizun_c3d_cleanup", off.DeniedTools);
        Assert.False(off.FailedClosed);
    }

    [Fact]
    public void End_session_turns_off_only_a_channel_opened_by_the_switch()
    {
        File.WriteAllText(PathFile, "{\"permission_profile\":\"unsafe_code\",\"enable_execute_csharp\":true}");
        Assert.False(CSharpChannel.EndSession(PathFile)); // set by hand: the owner's choice is kept
        Assert.True(CSharpChannel.IsOn(Settings.Load(PathFile)));

        File.Delete(PathFile);
        CSharpChannel.Enable(PathFile);
        Assert.True(CSharpChannel.EndSession(PathFile));
        Assert.False(CSharpChannel.IsOn(Settings.Load(PathFile)));
        Assert.False(CSharpChannel.EndSession(PathFile));
    }

    [Fact]
    public void An_invalid_settings_file_is_never_rewritten()
    {
        File.WriteAllText(PathFile, "{\"permission_profile\":\"god_mode\"}");
        Assert.Throws<InvalidOperationException>(() => CSharpChannel.Enable(PathFile));
        Assert.Equal("{\"permission_profile\":\"god_mode\"}", File.ReadAllText(PathFile));
        Assert.False(CSharpChannel.EndSession(PathFile));
    }

    [Fact]
    public void Full_write_switch_raises_then_restores_and_ends_with_the_session()
    {
        var on = CSharpChannel.EnableFullWrite(PathFile);
        Assert.True(CSharpChannel.IsFullWriteOn(on));
        Assert.Null(on.Refusal("horizun_c3d_entities", ToolEffect.FullWrite));
        Assert.NotNull(on.Refusal("horizun_c3d_execute_csharp", ToolEffect.UnsafeCode)); // full_write is not the C# channel

        var off = CSharpChannel.DisableFullWrite(PathFile);
        Assert.Equal(PermissionProfile.SafeWrite, off.Profile);
        Assert.NotNull(off.Refusal("horizun_c3d_entities", ToolEffect.FullWrite));

        CSharpChannel.EnableFullWrite(PathFile);
        Assert.True(CSharpChannel.EndSession(PathFile));
        Assert.Equal(PermissionProfile.SafeWrite, Settings.Load(PathFile).Profile);
    }

    [Fact]
    public void Full_write_switch_keeps_an_unsafe_code_profile_and_its_off_also_closes_the_csharp_channel()
    {
        CSharpChannel.Enable(PathFile);                       // C# on (unsafe_code) remembers safe_write
        var still = CSharpChannel.EnableFullWrite(PathFile);  // never lowers unsafe_code
        Assert.Equal(PermissionProfile.UnsafeCode, still.Profile);
        var off = CSharpChannel.DisableFullWrite(PathFile);
        Assert.Equal(PermissionProfile.SafeWrite, off.Profile);
        Assert.False(CSharpChannel.IsOn(off));

        File.WriteAllText(PathFile, "{\"permission_profile\":\"read_only\"}");
        CSharpChannel.EnableFullWrite(PathFile);
        Assert.Equal(PermissionProfile.ReadOnly, CSharpChannel.DisableFullWrite(PathFile).Profile); // back to the owner's read_only
    }

    [Theory]
    [InlineData("{\"csharp_session_restore_profile\":\"full_write\"}", false)]
    [InlineData("{\"csharp_session_restore_profile\":\"root\"}", true)]
    [InlineData("{\"csharp_session_restore_profile\":1}", true)]
    public void Restore_key_is_validated(string json, bool failsClosed) => Assert.Equal(failsClosed, Settings.Parse(json).FailedClosed);
}
