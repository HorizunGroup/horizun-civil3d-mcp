using System.IO.Pipes;
using System.Reflection;
using System.Text.Json.Nodes;
using Horizun.Civil3D.Core;
using Horizun.Civil3D.Plugin;

namespace Horizun.Civil3D.Runtime.Tests;

public class RuntimeProtocolTests
{
    [Fact]
    public void Current_contract_is_identical_on_all_host_runtimes() =>
        Assert.Equal("e36ee390efbb5d34ee0fe86c", Contract.Hash);

    [Theory]
    [InlineData("C:terrain.zip", false)]
    [InlineData("\\terrain.zip", false)]
    [InlineData("C:\\terrain.zip", true)]
    [InlineData("\\\\server\\share\\terrain.zip", true)]
    public void Windows_file_exports_refuse_implicit_drive_paths(string path, bool expected) =>
        Assert.Equal(expected, RuntimeCompat.IsPathFullyQualified(path));

    [Fact]
    public void Exact_read_refuses_truncated_plot_header()
    {
        using var stream = new MemoryStream(new byte[] { 1, 2 });
        Assert.Throws<EndOfStreamException>(() => RuntimeCompat.ReadExactly(stream, new byte[5], 0, 5));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Actual_plugin_pipe_authenticates_before_dispatch_on_each_runtime(bool authorized)
    {
        var name = "hz-runtime-test-" + Guid.NewGuid().ToString("N");
        var dispatcher = new Dispatcher();
        var pipe = new PipeServer(name, "test-secret", dispatcher);
        pipe.Start();
        try
        {
            using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut);
            client.Connect(5000);
            Wire.WriteLine(client, Wire.Request("test", "test_command", new JsonObject(), authorized ? "test-secret" : "wrong"));
            var reply = (JsonObject)JsonNode.Parse(Wire.ReadLine(client, Contract.MaxReplyBytes)!)!;
            Assert.Equal(authorized, reply["success"]!.GetValue<bool>());
            Assert.Equal(authorized ? 1 : 0, dispatcher.Invocations);
            if (!authorized) Assert.Equal(ErrorCodes.PermissionDenied, reply["code"]!.GetValue<string>());
        }
        finally
        {
            pipe.Stop();
            var thread = (Thread)typeof(PipeServer).GetField("_thread", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(pipe)!;
            Assert.True(thread.Join(5000));
        }
    }
}
