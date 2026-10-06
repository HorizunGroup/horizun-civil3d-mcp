#if NET48
using System.Runtime.CompilerServices;

internal static class LegacyTestStartup
{
    // Framework test hosts do not consume runtimeconfig.json. Set the switch before STJ loads.
    [ModuleInitializer]
    internal static void Initialize() =>
        AppContext.SetSwitch("System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault", false);
}
#endif
