// -----------------------------------------------------------------------------
// Horizun Civil 3D MCP - plug-in entry point (loaded at Civil 3D startup by the
// Horizun.Civil3D.bundle in ApplicationPlugins).
//
// Startup order (every step logged; a failure leaves NO discovery file, so the
// server can never connect to a half-started bridge):
//   1. data folders + log
//   2. refuse to run outside Civil 3D (plain AutoCAD has no AeccDbMgd)
//   3. register commands and verify them against the shared Contract
//   4. UI pump on the main thread
//   5. pipe listener, then the discovery file (last: published = connectable)
//   6. ribbon (never fatal)
// -----------------------------------------------------------------------------
using System.Reflection;
using Autodesk.AutoCAD.Runtime;
using Horizun.Civil3D.Core;
using Horizun.Civil3D.Plugin.Commands;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

[assembly: ExtensionApplication(typeof(Horizun.Civil3D.Plugin.App))]
[assembly: CommandClass(typeof(Horizun.Civil3D.Plugin.AcadCommands))]

namespace Horizun.Civil3D.Plugin;

public sealed class App : IExtensionApplication
{
    internal static Dispatcher? Bridge { get; private set; }
    internal static PipeServer? Pipe { get; private set; }
    internal static DiscoveryRecord? Published { get; private set; }
    internal static string? StartupError { get; private set; }

    public static string Edition =>
#if HZ_DEVTOOLS
        "desarrollo";
#else
        "uso";
#endif

    public static int Year { get; } = ReadYear();
    public static string PluginVersion { get; } =
        typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0]
        ?? typeof(App).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    private static int ReadYear()
    {
        var v = typeof(App).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "Civil3DYear")?.Value;
        return int.TryParse(v, out var y) ? y : 0;
    }

    public void Initialize()
    {
        try
        {
            HorizunPaths.EnsureDirectories();
            Log.Start(Year);
            try { if (CSharpChannel.EndSession(HorizunPaths.SettingsFile())) Log.Info("C# channel left on by the previous session: turned off at startup"); }
            catch (System.Exception e) { Log.Warn("C# channel end of session: " + e.Message); }
            Log.Info($"version {PluginVersion}, built for Civil 3D {Year}, contract {Contract.Hash}");

            if (!IsCivil3D(out var why))
            {
                StartupError = why;
                Log.Warn("not starting: " + why);
                return;
            }

            var d = CreateDispatcher();

            var expected = Contract.PluginCommands.OrderBy(x => x).ToList();
            var actual = d.CommandNames.OrderBy(x => x).ToList();
            if (!expected.SequenceEqual(actual))
            {
                StartupError = "Plug-in commands [" + string.Join(",", actual) + "] do not match the contract [" +
                               string.Join(",", expected) + "]. Bridge NOT started.";
                Log.Error(StartupError);
                return;
            }

            UiPump.Start(d);
            Bridge = d;

            var pid = Environment.ProcessId;
            var token = Hz.NewToken(32);
            var pipeName = Wire.PipeName(pid);
            Pipe = new PipeServer(pipeName, token, d);
            Pipe.Start();

            string acadver = "";
            try { acadver = Convert.ToString(AcApp.GetSystemVariable("ACADVER")) ?? ""; } catch { }
            Published = new DiscoveryRecord
            {
                Year = Year,
                Pid = pid,
                PipeName = pipeName,
                AuthToken = token,
                InstanceId = Guid.NewGuid().ToString("N"),
                StartedUtc = DateTime.UtcNow,
                ProtocolVersion = Contract.ProtocolVersion,
                ContractHash = Contract.Hash,
                PluginVersion = PluginVersion,
                AcadVersion = acadver,
                Commands = actual,
            };
            var path = Discovery.Write(Published);
            Log.Info("bridge listening on " + pipeName + ", discovery " + path);
        }
        catch (System.Exception e)
        {
            StartupError = "Startup failed: " + e.GetType().Name + ": " + e.Message;
            Log.Error("startup failed", e);
            try { Pipe?.Stop(); } catch { }
            Published = null;
        }

        try { Ribbon.Install(); } catch (System.Exception e) { Log.Warn("ribbon not installed: " + e.Message); }
    }

    public void Terminate()
    {
        try
        {
            if (Published != null) Discovery.Delete(Published.Year, Published.Pid);
            Pipe?.Stop();
            Bridge?.Gate.FailQueued("Civil 3D is closing; the request never ran.");
            UiPump.Stop();
            Log.Info("bridge stopped");
        }
        catch (System.Exception e) { Log.Error("shutdown", e); }
    }

    // Separate, non-inlined method: Civil 3D types are only JIT-resolved AFTER the
    // IsCivil3D check, so a plain AutoCAD logs a refusal instead of a load error.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static Dispatcher CreateDispatcher()
    {
        var d = new Dispatcher(Year);
        d.Register(new HealthCommand());
        d.Register(new DocumentCommand());
        d.Register(new QueryCommand());
        d.Register(new StylesCommand());
        d.Register(new SurfaceCommand());
        d.Register(new ProbeCommand());
        d.Register(new GradingCommand());
        d.Register(new FeatureLineCommand());
        d.Register(new ExecuteCSharpCommand());
        d.Register(new AlignmentCommand());
        d.Register(new ProfileCommand());
        d.Register(new SectionsCommand());
        d.Register(new CorridorCommand());
        d.Register(new LabelsCommand());
        d.Register(new LayersCommand());
        d.Register(new EntitiesCommand());
        d.Register(new DimensionsCommand());
        d.Register(new CadStylesCommand());
        d.Register(new BlocksCommand());
        d.Register(new TablesCommand());
        d.Register(new LayoutsCommand());
        d.Register(new CleanupCommand());
        d.Register(new PipesCommand());
        d.Register(new PointsCommand());
        d.Register(new ExchangeCommand());
        return d;
    }

    private static bool IsCivil3D(out string why)
    {
        try
        {
            var c3d = AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "AeccDbMgd")
                      || File.Exists(Path.Combine(AppContext.BaseDirectory, "C3D", "AeccDbMgd.dll"));
            if (!c3d)
            {
                // AutoCAD keeps the exe in the product root; probe there too.
                var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? "";
                c3d = File.Exists(Path.Combine(exeDir, "C3D", "AeccDbMgd.dll"));
            }
            why = c3d ? "" : "This AutoCAD is not Civil 3D (no C3D\\AeccDbMgd.dll). The Horizun Civil 3D bridge does not start.";
            return c3d;
        }
        catch (System.Exception e)
        {
            why = "Could not determine whether this is Civil 3D: " + e.Message;
            return false;
        }
    }
}
