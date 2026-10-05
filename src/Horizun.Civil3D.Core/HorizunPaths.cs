// -----------------------------------------------------------------------------
// Horizun Civil 3D MCP - where the bridge keeps its local state.
//
// Everything lives under ONE per-user root so the plug-in and the server agree
// without configuration:
//
//   %USERPROFILE%\.horizun\civil3d\
//       settings.json      permission profile (read by BOTH halves)
//       discovery\         civil3d-<year>-<pid>.json, one per running Civil 3D
//       logs\              plug-in and server logs
//       probes\<year>\     API signature dumps written by the probe
//
// The Revit product uses %USERPROFILE%\.horizun\ directly; the civil3d sub-root
// keeps the two products from ever reading each other's discovery or settings.
// HORIZUN_C3D_DATA_ROOT overrides the root (tests, sandboxes).
// -----------------------------------------------------------------------------
namespace Horizun.Civil3D.Core;

public static class HorizunPaths
{
    public const string DataRootEnv = "HORIZUN_C3D_DATA_ROOT";

    public static string DataRoot()
    {
        var overridden = Environment.GetEnvironmentVariable(DataRootEnv);
        if (!string.IsNullOrWhiteSpace(overridden)) return Path.GetFullPath(overridden);
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".horizun", "civil3d");
    }

    public static string SettingsFile() => Path.Combine(DataRoot(), "settings.json");
    public static string DiscoveryDir() => Path.Combine(DataRoot(), "discovery");
    public static string LogsDir() => Path.Combine(DataRoot(), "logs");
    public static string FixturesDir() => Path.Combine(DataRoot(), "fixtures");
    public static string ProbesDir(int year) => Path.Combine(DataRoot(), "probes", year.ToString());

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(DataRoot());
        Directory.CreateDirectory(DiscoveryDir());
        Directory.CreateDirectory(LogsDir());
    }
}
