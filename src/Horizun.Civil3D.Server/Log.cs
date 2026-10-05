// -----------------------------------------------------------------------------
// Horizun Civil 3D MCP - server log (logs\server.log + stderr). Never stdout:
// stdout belongs to the MCP protocol.
// -----------------------------------------------------------------------------
using Horizun.Civil3D.Core;

namespace Horizun.Civil3D.Server;

internal static class Log
{
    private static readonly object Sync = new();
    private static string? _path;

    public static void Start()
    {
        try
        {
            Directory.CreateDirectory(HorizunPaths.LogsDir());
            _path = Path.Combine(HorizunPaths.LogsDir(), "server.log");
            var fi = new FileInfo(_path);
            if (fi.Exists && fi.Length > 5 * 1024 * 1024) File.Move(_path, _path + ".1", overwrite: true);
        }
        catch { _path = null; }
    }

    public static void Info(string m) => Write("INFO ", m);
    public static void Error(string m, Exception? e = null) => Write("ERROR", e == null ? m : m + " :: " + e);

    private static void Write(string level, string m)
    {
        var line = DateTime.UtcNow.ToString("o") + " " + level + " " + m;
        try { Console.Error.WriteLine(line); } catch { }
        if (_path == null) return;
        try { lock (Sync) File.AppendAllText(_path, line + Environment.NewLine); } catch { }
    }
}
