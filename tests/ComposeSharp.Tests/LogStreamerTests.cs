using System.Text;
using ComposeSharp.Engine.Internal;
using Docker.DotNet;

namespace ComposeSharp.Tests;

public class LogStreamerTests
{
    [Fact]
    public async Task InterleavedFramesKeepStreamIdentityAndLineOrder()
    {
        using var bytes = new MemoryStream();
        WriteFrame(bytes, 1, "out-");
        WriteFrame(bytes, 2, "err-one\nerr-");
        WriteFrame(bytes, 1, "one\nout-two\n");
        WriteFrame(bytes, 2, "two\n");
        bytes.Position = 0;
        using var stream = new MultiplexedStream(bytes, true);
        var lines = new List<(string Line, bool IsStdErr)>();

        await foreach (var line in LogStreamer.ReadLinesAsync(stream, CancellationToken.None))
            lines.Add(line);

        Assert.Equal(new[] { ("err-one", true), ("out-one", false), ("out-two", false), ("err-two", true) }, lines);
    }

    [Fact]
    public async Task SplitUtf8AndUnterminatedLinesRemainSeparate()
    {
        using var bytes = new MemoryStream();
        var character = "界"u8.ToArray();
        WriteFrame(bytes, 1, character[..1]);
        WriteFrame(bytes, 2, "error"u8.ToArray());
        WriteFrame(bytes, 1, character[1..]);
        bytes.Position = 0;
        using var stream = new MultiplexedStream(bytes, true);
        var lines = new List<(string Line, bool IsStdErr)>();
        await foreach (var line in LogStreamer.ReadLinesAsync(stream, CancellationToken.None))
            lines.Add(line);
        Assert.Equal(new[] { ("界", false), ("error", true) }, lines);
    }

    private static void WriteFrame(Stream stream, byte target, string text)
        => WriteFrame(stream, target, Encoding.UTF8.GetBytes(text));

    private static void WriteFrame(Stream stream, byte target, byte[] payload)
    {
        var header = new byte[8];
        header[0] = target;
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), payload.Length);
        stream.Write(header);
        stream.Write(payload);
    }
}
