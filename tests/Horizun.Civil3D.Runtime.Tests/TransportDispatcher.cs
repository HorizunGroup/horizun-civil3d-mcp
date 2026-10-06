using System.Text.Json.Nodes;
using Horizun.Civil3D.Core;

namespace Horizun.Civil3D.Plugin;

// The production pipe is compiled unchanged. Only Autodesk main-thread dispatch is replaced.
internal sealed class Dispatcher
{
    internal RequestGate Gate { get; } = new();
    internal int Invocations { get; private set; }
    internal JsonObject Status() => new() { ["transport_test"] = true };
    internal JsonObject Invoke(string? id, string command, JsonObject args, int timeout)
    {
        Invocations++;
        return Wire.Reply(id, CommandResult.Ok(new JsonObject { ["command"] = command }));
    }
}
