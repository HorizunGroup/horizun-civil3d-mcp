// -----------------------------------------------------------------------------
// Horizun Civil 3D MCP - the plug-in end of the local named pipe.
//
// Listens on "Horizun.Civil3D-<pid>" on a BACKGROUND thread. This thread never
// touches the drawing: it authenticates, answers the two control verbs
// directly, and hands every real command to the Dispatcher, which marshals it
// to Civil 3D's main thread.
//
// Security: pipe ACL = current user only; every request carries the 256-bit
// token from the discovery file, compared in constant time. No network
// listener exists anywhere in the bridge.
// -----------------------------------------------------------------------------
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json.Nodes;
using Horizun.Civil3D.Core;

namespace Horizun.Civil3D.Plugin;

internal sealed class PipeServer
{
    private const int MaxConcurrentConnections = RequestGate.DefaultCapacity + 4;
    private const int DefaultCommandTimeoutMs = 600_000;

    private readonly string _pipeName;
    private readonly string _token;
    private readonly Dispatcher _dispatcher;
    private readonly SemaphoreSlim _slots = new(MaxConcurrentConnections);
    private volatile bool _stopping;
    private Thread? _thread;

    public PipeServer(string pipeName, string token, Dispatcher dispatcher)
    {
        _pipeName = pipeName;
        _token = token;
        _dispatcher = dispatcher;
    }

    public void Start()
    {
        _thread = new Thread(AcceptLoop) { IsBackground = true, Name = "Horizun.Civil3D pipe" };
        _thread.Start();
    }

    public void Stop()
    {
        _stopping = true;
        // Unblock WaitForConnection with a throwaway client.
        try
        {
            using var c = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut);
            c.Connect(200);
        }
        catch { }
    }

    private NamedPipeServerStream Create()
    {
        var rules = new PipeSecurity();
        var me = WindowsIdentity.GetCurrent().User!;
        rules.AddAccessRule(new PipeAccessRule(me, PipeAccessRights.FullControl, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(_pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 64 * 1024, 64 * 1024, rules);
    }

    private void AcceptLoop()
    {
        while (!_stopping)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = Create();
                server.WaitForConnection();
                if (_stopping) { server.Dispose(); break; }
                if (!_slots.Wait(0))
                {
                    Refuse(server, "Too many simultaneous connections to the Civil 3D bridge; nothing was queued.");
                    continue;
                }
                var conn = server;
                server = null;
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { Handle(conn); }
                    finally { _slots.Release(); }
                });
            }
            catch (Exception e)
            {
                server?.Dispose();
                if (_stopping) break;
                Log.Error("pipe accept failed", e);
                Thread.Sleep(250);
            }
        }
    }

    private static void Refuse(NamedPipeServerStream s, string why)
    {
        try { Wire.WriteLine(s, Wire.Reply(null, CommandResult.Fail(ErrorCodes.QueueFull, why))); } catch { }
        try { s.Dispose(); } catch { }
    }

    private void Handle(NamedPipeServerStream s)
    {
        string? id = null;
        try
        {
            using (s)
            {
                var readTask = Task.Run(() => Wire.ReadLine(s, Contract.MaxRequestBytes));
                if (!readTask.Wait(30_000))
                {
                    Log.Warn("pipe client sent nothing within 30 s; closed");
                    return;
                }
                var line = readTask.Result;
                if (line == null) return;

                if (JsonNode.Parse(line) is not JsonObject req)
                {
                    Wire.WriteLine(s, Wire.Reply(null, CommandResult.Fail(ErrorCodes.InvalidInput, "Request must be a JSON object.")));
                    return;
                }
                id = Hz.Str(req, "id");
                if (!Hz.SecretEquals(Hz.Str(req, "token"), _token))
                {
                    Wire.WriteLine(s, Wire.Reply(id, CommandResult.Fail(ErrorCodes.PermissionDenied, "Bad or missing bridge token. Nothing ran.")));
                    return;
                }

                var command = Hz.Str(req, "command") ?? "";
                var parameters = req["params"] as JsonObject ?? new JsonObject();
                JsonObject reply;
                if (command == Wire.StatusVerb)
                {
                    reply = Wire.Reply(id, CommandResult.Ok(_dispatcher.Status()));
                }
                else if (command == Wire.CancelVerb)
                {
                    var ok = _dispatcher.Gate.CancelQueued(Hz.Str(parameters, "wire_id"), out var detail);
                    reply = Wire.Reply(id, CommandResult.Ok(new JsonObject { ["cancelled"] = ok, ["state"] = detail }));
                }
                else
                {
                    var timeout = Hz.Int(req, "timeout_ms") ?? DefaultCommandTimeoutMs;
                    reply = _dispatcher.Invoke(id, command, parameters, timeout);
                }

                var text = reply.ToJsonString(Hz.Compact);
                if (text.Length > Contract.MaxReplyBytes)
                    reply = Wire.Reply(id, CommandResult.Fail(ErrorCodes.Internal,
                        "THE COMMAND RAN but its reply exceeds " + Contract.MaxReplyBytes + " bytes and was not sent. " +
                        "Re-run with a narrower filter or a smaller limit; check the drawing before repeating a write."));
                Wire.WriteLine(s, reply);
            }
        }
        catch (Exception e)
        {
            Log.Error("pipe request " + id + " failed", e);
        }
    }
}
