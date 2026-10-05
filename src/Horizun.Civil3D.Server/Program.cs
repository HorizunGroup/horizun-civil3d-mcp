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
var stdin = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
var stdout = Console.OpenStandardOutput();
var writeLock = new object();
var pending = new List<Task>();

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
while ((line = stdin.ReadLine()) != null)
{
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
        pending.RemoveAll(t => t.IsCompleted);
        pending.Add(Task.Run(() =>
        {
            var reply = server.Handle(msg);
            if (reply != null) Send(reply);
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
