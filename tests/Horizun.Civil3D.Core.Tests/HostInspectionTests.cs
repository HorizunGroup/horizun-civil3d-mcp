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
