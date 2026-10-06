namespace Horizun.Civil3D.Core.Tests;

internal static class TestDirectories
{
    public static DirectoryInfo CreateTempSubdirectory(string prefix) =>
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N")));
}
