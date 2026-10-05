// -----------------------------------------------------------------------------
// Horizun Civil 3D MCP - the wire between the MCP server and the plug-in.
//
// Local named pipe "Horizun.Civil3D-<pid>", ACL'd to the current user. One
// request per connection, one line of UTF-8 JSON each way:
//
//   request  {"id","command","params","token"}
//   reply    {"id","success","data","error","code","detail","host_messages","bridge_queue"}
//
// Two control verbs bypass the Civil 3D queue and never touch the drawing:
//   __status          queue and UI-thread state
//   __cancel_queued   remove a request that has NOT started ({"wire_id"})
//
// Reads are bounded: an oversized or endless line is refused instead of
// buffered without limit.
// -----------------------------------------------------------------------------
using System.Text;
using System.Text.Json.Nodes;

namespace Horizun.Civil3D.Core;

public static class Wire
{
    public const string StatusVerb = "__status";
    public const string CancelVerb = "__cancel_queued";

    public static string PipeName(int pid) => "Horizun.Civil3D-" + pid;

    public static JsonObject Request(string id, string command, JsonObject? parameters, string token) => new()
    {
        ["id"] = id,
        ["command"] = command,
        ["params"] = parameters?.DeepClone() ?? new JsonObject(),
        ["token"] = token,
    };

    public static JsonObject Reply(string? id, CommandResult result, JsonArray? hostMessages = null, JsonObject? queue = null)
    {
        var o = new JsonObject
        {
            ["id"] = id,
            ["success"] = result.Success,
            ["data"] = result.Data,
            ["error"] = result.Error,
            ["code"] = result.Code,
        };
        if (result.Detail != null) o["detail"] = result.Detail;
        if (hostMessages != null && hostMessages.Count > 0) o["host_messages"] = hostMessages;
        if (queue != null) o["bridge_queue"] = queue;
        return o;
    }

    /// <summary>Read one newline-terminated UTF-8 line, refusing anything above maxBytes.</summary>
    public static string? ReadLine(Stream s, int maxBytes)
    {
        var buf = new MemoryStream();
        var chunk = new byte[64 * 1024];
        // Pipes in message-less byte mode: read until newline. We cannot over-read
        // past the newline because each connection carries exactly one message.
        while (true)
        {
            var n = s.Read(chunk, 0, chunk.Length);
            if (n == 0) break;
            var nl = Array.IndexOf(chunk, (byte)'\n', 0, n);
            if (nl >= 0)
            {
                buf.Write(chunk, 0, nl);
                break;
            }
            buf.Write(chunk, 0, n);
            if (buf.Length > maxBytes)
                throw new InvalidDataException($"Message exceeds the {maxBytes}-byte limit; refused instead of buffered.");
        }
        if (buf.Length == 0) return null;
        return Encoding.UTF8.GetString(buf.ToArray()).TrimEnd('\r');
    }

    public static void WriteLine(Stream s, JsonNode message)
    {
        var bytes = Encoding.UTF8.GetBytes(message.ToJsonString(Hz.Compact) + "\n");
        s.Write(bytes, 0, bytes.Length);
        s.Flush();
    }
}
