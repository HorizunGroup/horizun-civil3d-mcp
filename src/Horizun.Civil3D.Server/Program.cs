// -----------------------------------------------------------------------------
// Horizun Civil 3D MCP server - entry point.
//
// stdout carries ONLY protocol messages (one JSON object per line). Everything
// else goes to stderr and logs\server.log. Requests are handled concurrently
// (the plug-in's queue serialises Civil 3D work); writes to stdout are locked.
//
//   horizun-civil3d-mcp            run the MCP server on stdio
//   horizun-civil3d-mcp --version  print version and contract hash
// -----------------------------------------------------------------------------
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using Horizun.Civil3D.Core;
using Horizun.Civil3D.Server;

var version = typeof(McpServer).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "0.0.0";

if (args.Length > 0 && args[0] == "--inspect-host")
{
    try
    {
        if (args.Length != 3 || !int.TryParse(args[2], out var year))
            throw new ArgumentException("Usage: --inspect-host <AutoCAD installation root> <Civil 3D year>");
        Console.WriteLine(HostInspection.Inspect(args[1], year).ToJsonString(Hz.Compact));
        return 0;
    }
    catch (Exception e) { Console.Error.WriteLine(e.Message); return 2; }
}

if (args.Contains("--version"))
{
    Console.WriteLine("horizun-civil3d-mcp " + version + " contract " + Contract.Hash + " protocol " + Contract.ProtocolVersion);
    return 0;
}

Log.Start();
Log.Info("server " + version + " starting, contract " + Contract.Hash);
try { var swept = Discovery.SweepStale(); if (swept > 0) Log.Info("swept " + swept + " stale discovery file(s)"); }
catch (Exception e) { Log.Error("sweep", e); }

var server = new McpServer(version);
var stdin = new BoundedStdioReader(Console.OpenStandardInput(), Contract.MaxRequestBytes);
var stdout = Console.OpenStandardOutput();
var writeLock = new object();
var pending = new List<Task>();
const int MaxConcurrentCalls = 32;
using var callSlots = new SemaphoreSlim(MaxConcurrentCalls);

void Send(JsonObject message)
{
    var bytes = Encoding.UTF8.GetBytes(message.ToJsonString(Hz.Compact) + "\n");
    lock (writeLock)
    {
        stdout.Write(bytes, 0, bytes.Length);
        stdout.Flush();
    }
}

string? line;
while ((line = stdin.ReadLine(out var tooLong)) != null)
{
    if (tooLong)
    {
        Send(McpServer.Error(null, -32600, "Request exceeds the " + Contract.MaxRequestBytes + "-byte limit. Nothing ran."));
        continue;
    }
    if (string.IsNullOrWhiteSpace(line)) continue;
    JsonNode? node;
    try { node = JsonNode.Parse(line); }
    catch (Exception)
    {
        Send(McpServer.Error(null, -32700, "Parse error"));
        continue;
    }
    if (node is not JsonObject msg)
    {
        Send(McpServer.Error(null, -32600, "Batch requests are not supported."));
        continue;
    }
    if (msg.ContainsKey("result") || msg.ContainsKey("error")) continue; // responses to server requests: none issued

    var method = Hz.Str(msg, "method");
    if (method == "tools/call")
    {
        if (!callSlots.Wait(0))
        {
            if (msg.TryGetPropertyValue("id", out var refusedId) && refusedId != null)
                Send(new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = refusedId.DeepClone(),
                    ["result"] = McpServer.ToolError(ErrorCodes.QueueFull,
                        "The server is handling " + MaxConcurrentCalls + " calls. Nothing was queued or ran; retry after one finishes."),
                });
            continue;
        }
        pending.RemoveAll(t => t.IsCompleted);
        pending.Add(Task.Run(() =>
        {
            try
            {
                var reply = server.Handle(msg);
                if (reply != null) Send(reply);
            }
            finally { callSlots.Release(); }
        }));
    }
    else
    {
        var reply = server.Handle(msg);
        if (reply != null) Send(reply);
    }
}

Task.WaitAll(pending.ToArray(), TimeSpan.FromSeconds(30));
Log.Info("stdin closed; server exiting");
return 0;
