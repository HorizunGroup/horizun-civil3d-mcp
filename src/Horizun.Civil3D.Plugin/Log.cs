// -----------------------------------------------------------------------------
// Horizun Civil 3D MCP - plug-in log (logs\plugin-<year>-<pid>.log).
// Never throws: logging must not be the reason a command fails.
// -----------------------------------------------------------------------------
using Horizun.Civil3D.Core;

namespace Horizun.Civil3D.Plugin;

internal static class Log
{
    private static readonly object Sync = new();
    private static string? _path;

    public static string? Path => _path;

    public static void Start(int year)
    {
        try
        {
            Directory.CreateDirectory(HorizunPaths.LogsDir());
            _path = System.IO.Path.Combine(HorizunPaths.LogsDir(), $"plugin-{year}-{Environment.ProcessId}.log");
            Info("Horizun Civil 3D plug-in starting");
        }
        catch { _path = null; }
    }

    public static void Info(string message) => Write("INFO ", message);
    public static void Warn(string message) => Write("WARN ", message);
    public static void Error(string message, Exception? e = null) =>
        Write("ERROR", e == null ? message : message + " :: " + e.GetType().Name + ": " + e.Message + Environment.NewLine + e.StackTrace);

    private static void Write(string level, string message)
    {
        if (_path == null) return;
        try
        {
            lock (Sync)
                File.AppendAllText(_path, DateTime.UtcNow.ToString("o") + " " + level + " " + message + Environment.NewLine);
        }
        catch { }
    }
}
