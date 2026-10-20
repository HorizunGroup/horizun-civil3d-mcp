using System.Text;
using Horizun.Civil3D.Core;

namespace Horizun.Civil3D.Core.Tests;

public class WireBoundaryTests
{
    [Fact]
    public void Pipe_line_rejects_over_limit_even_when_newline_is_in_the_same_chunk()
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("123456789\n"));
        Assert.Throws<InvalidDataException>(() => Wire.ReadLine(input, 8));
    }

    [Fact]
    public void Pipe_line_accepts_exact_byte_limit()
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("12345678\n"));
        Assert.Equal("12345678", Wire.ReadLine(input, 8));
    }

    [Fact]
    public void Stdio_reader_drains_oversized_request_and_reads_next_request()
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("123456789\n{}\n"));
        var reader = new BoundedStdioReader(input, 8);

        Assert.Equal(string.Empty, reader.ReadLine(out var oversized));
        Assert.True(oversized);
        Assert.Equal("{}", reader.ReadLine(out var nextOversized));
        Assert.False(nextOversized);
        Assert.Null(reader.ReadLine(out _));
    }

    [Fact]
    public void Stdio_reader_counts_utf8_bytes()
    {
        using var input = new MemoryStream(Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes("éé\n{}\n")).ToArray());
        var reader = new BoundedStdioReader(input, 4);

        Assert.Equal(string.Empty, reader.ReadLine(out var oversized));
        Assert.True(oversized); // BOM (3 bytes) plus two UTF-8 characters (4 bytes)
        Assert.Equal("{}", reader.ReadLine(out var nextOversized));
        Assert.False(nextOversized);
    }

    [Fact]
    public void Stdio_reader_accepts_initial_utf8_bom()
    {
        using var input = new MemoryStream(Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes("{}\n")).ToArray());
        var reader = new BoundedStdioReader(input, 5);

        Assert.Equal("{}", reader.ReadLine(out var oversized));
        Assert.False(oversized);
    }
}
