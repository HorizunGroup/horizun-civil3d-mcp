using System.Text.Json.Nodes;
using Horizun.Civil3D.Core;
using Horizun.Civil3D.Server;

namespace Horizun.Civil3D.Core.Tests;

public class HostInspectionTests
{
    [Theory]
    [InlineData(2024, "24.3", ".NETFramework,Version=v4.7", ".NETFramework,Version=v4.7", "net48")]
    [InlineData(2026, "25.1", ".NETCoreApp,Version=v8.0", ".NETCoreApp,Version=v8.0", "net8")]
    [InlineData(2026, "25.1", ".NETCoreApp,Version=v10.0", ".NETCoreApp,Version=v8.0", "net10")]
    public void Sdk_framework_and_host_update_select_correct_plugin(int year, string version, string host, string civil, string expected)
    {
        var result = HostInspection.InspectAssemblies(new("AcMgd", new Version(version), host), new("AeccDbMgd", new Version("13.6"), civil), year);
        Assert.Equal(expected, result["runtime"]!.GetValue<string>());
        Assert.Equal(civil, result["civil_target_framework"]!.GetValue<string>());
    }

    [Fact]
    public void Net8_host_rejects_net10_civil_library()
    {
        Assert.Throws<InvalidDataException>(() => HostInspection.InspectAssemblies(
            new("AcMgd", new Version("25.1"), ".NETCoreApp,Version=v8.0"),
            new("AeccDbMgd", new Version("13.8"), ".NETCoreApp,Version=v10.0"), 2026));
    }
    static JsonObject Build(int year, string runtime, string acad, string civil, string framework = ".NETCoreApp,Version=v8.0") => new()
    {
        ["year"] = year, ["runtime"] = runtime, ["target_framework"] = framework, ["civil_target_framework"] = framework,
        ["autocad_assembly_version"] = acad, ["civil_assembly_version"] = civil,
    };

    // 0.9.6 field failure: the package recorded 2025 civil 13.7.0.1402 and required exact equality,
    // so every other 2025 update reported "no build matching any installed Civil 3D".
    [Theory]
    [InlineData("13.7.0.1402", "13.7.0.1500")]
    [InlineData("13.7.0.1402", "13.7.0.1402")]
    [InlineData("13.7.0.145", "13.7.0.1402")]
    [InlineData("13.7.0.145", "13.7.0.765")]
    public void Civil_2025_same_or_newer_update_matches(string built, string installed)
    {
        var (ok, reason) = HostInspection.Match(Build(2025, "net8", "25.0.0.0", built), Build(2025, "net8", "25.0.0.0", installed));
        Assert.True(ok, reason);
    }

    [Fact]
    public void Older_update_than_the_build_is_refused_with_the_required_version()
    {
        var (ok, reason) = HostInspection.Match(Build(2025, "net8", "25.0.0.0", "13.7.0.1402"), Build(2025, "net8", "25.0.0.0", "13.7.0.1175"));
        Assert.False(ok);
        Assert.Contains("13.7.0.1402", reason);
        Assert.Contains("13.7.0.1175", reason);
    }

    [Theory]
    [InlineData(2024, "net48", "24.3.0.0", "13.6.0.1781", "13.6.0.1900", ".NETFramework,Version=v4.7")]
    [InlineData(2026, "net8", "25.1.0.0", "13.8.0.1516", "13.8.0.1600", ".NETCoreApp,Version=v8.0")]
    public void Other_years_accept_newer_updates(int year, string runtime, string acad, string built, string installed, string framework)
    {
        var (ok, reason) = HostInspection.Match(Build(year, runtime, acad, built, framework), Build(year, runtime, acad, installed, framework));
        Assert.True(ok, reason);
    }

    [Fact]
    public void Different_release_runtime_year_or_unreadable_versions_are_refused()
    {
        var built = Build(2025, "net8", "25.0.0.0", "13.7.0.1402");
        Assert.False(HostInspection.Match(built, Build(2025, "net8", "25.0.0.0", "13.8.0.1516")).Compatible);
        Assert.False(HostInspection.Match(Build(2026, "net8", "25.1.0.0", "13.8.0.1516"), Build(2026, "net10", "25.1.0.0", "13.8.0.1516", ".NETCoreApp,Version=v10.0")).Compatible);
        Assert.False(HostInspection.Match(built, Build(2026, "net8", "25.0.0.0", "13.7.0.1402")).Compatible);
        Assert.False(HostInspection.Match(built, Build(2025, "net8", "25.0.0.0", "garbage")).Compatible);
        var missing = Build(2025, "net8", "25.0.0.0", "13.7.0.1402");
        missing.Remove("civil_assembly_version");
        Assert.False(HostInspection.Match(built, missing).Compatible);
    }

    [Fact]
    public void Reads_framework_and_identity_without_loading_target_code()
    {
        var info = HostInspection.ReadAssembly(typeof(Contract).Assembly.Location);
        Assert.Equal("Horizun.Civil3D.Core", info.Name);
        Assert.Equal(".NETCoreApp,Version=v8.0", info.Framework);
    }

    [Fact]
    public void Renamed_non_Autodesk_dlls_cannot_pass_host_detection()
    {
        var root = TestDirectories.CreateTempSubdirectory("hz-host-metadata-");
        try
        {
            Directory.CreateDirectory(Path.Combine(root.FullName, "C3D"));
            File.Copy(typeof(Contract).Assembly.Location, Path.Combine(root.FullName, "acmgd.dll"));
            File.Copy(typeof(Contract).Assembly.Location, Path.Combine(root.FullName, "C3D", "AeccDbMgd.dll"));
            Assert.Throws<InvalidDataException>(() => HostInspection.Inspect(root.FullName, 2024));
            Assert.Throws<InvalidDataException>(() => HostInspection.Inspect(root.FullName, 2026));
        }
        finally { root.Delete(true); }
    }
}
