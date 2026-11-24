#nullable enable
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Akka.IO;
using Akka.Streams.Dsl;
using Akka.TestKit;
using Xunit;

namespace Akka.Streams.Tests.IO;

public sealed class TcpChunkFramingSpec : AkkaSpec
{
    public TcpChunkFramingSpec(ITestOutputHelper output) : base(output) { }

    [Fact(DisplayName = "Length framing preserves a TCP delivery with its second prefix split at 64 KiB")]
    public async Task Should_frame_split_length_and_payload()
    {
        var first = new byte[65530];
        var second = new byte[65537];
        new Random(17).NextBytes(first);
        new Random(91).NextBytes(second);
        var wire = new byte[4 + first.Length + 4 + second.Length];
        BinaryPrimitives.WriteInt32BigEndian(wire, first.Length);
        first.CopyTo(wire, 4);
        BinaryPrimitives.WriteInt32BigEndian(wire.AsSpan(4 + first.Length), second.Length);
        second.CopyTo(wire, 8 + first.Length);
        var data = await CopyThroughTcp(wire);
        var frames = await Source.Single(data).Via(Framing.SimpleFramingProtocolDecoder(131072))
            .RunWith(Sink.Seq<ReadOnlySequence<byte>>(), Sys.Materializer())
            .WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(2, frames.Count);
        Assert.Equal(first, frames[0].ToArray());
        Assert.Equal(second, frames[1].ToArray());
        Assert.Equal(wire, data.ToArray());
    }

    [Fact(DisplayName = "Delimiter framing matches a delimiter across fresh TCP segments")]
    public async Task Should_frame_split_delimiter()
    {
        var first = Encoding.ASCII.GetBytes(new string('a', 65535));
        var second = Encoding.ASCII.GetBytes(new string('b', 65537));
        var separator = Encoding.ASCII.GetBytes("\r\n");
        var wire = first.Concat(separator).Concat(second).Concat(separator).ToArray();
        var data = await CopyThroughTcp(wire);
        var frames = await Source.Single(data).Via(Framing.Delimiter(separator.AsMemory(), 131072))
            .RunWith(Sink.Seq<ReadOnlySequence<byte>>(), Sys.Materializer())
            .WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(2, frames.Count);
        Assert.Equal(first, frames[0].ToArray());
        Assert.Equal(second, frames[1].ToArray());
        Assert.Equal(wire, data.ToArray());
    }

    [Fact(DisplayName = "JSON framing scans objects spanning fresh TCP segments")]
    public async Task Should_frame_json_across_tcp_segments()
    {
        var first = Encoding.UTF8.GetBytes("{\"value\":\"" + new string('a', 65537) + "\"}");
        var second = Encoding.UTF8.GetBytes("{\"next\":true}");
        var wire = first.Concat(second).ToArray();
        var data = await CopyThroughTcp(wire);
        var frames = await Source.Single(data).Via(JsonFraming.ObjectScanner(131072))
            .RunWith(Sink.Seq<ReadOnlySequence<byte>>(), Sys.Materializer())
            .WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(2, frames.Count);
        Assert.Equal(first, frames[0].ToArray());
        Assert.Equal(second, frames[1].ToArray());
        Assert.Equal(wire, data.ToArray());
    }

    private async Task<ReadOnlySequence<byte>> CopyThroughTcp(byte[] wire)
    {
        var pipe = new Pipe(new PipeOptions(pauseWriterThreshold: 0));
        var receiver = CreateTestProbe();
        await pipe.Writer.WriteAsync(wire);
        new TcpConnection.PipeReadToActor(pipe.Reader, CancellationToken.None, receiver.Ref).Start();
        var data = (await receiver.ExpectMsgAsync<TcpConnection.PipeReadCompleted>()).Data;
        Assert.False(data.IsSingleSegment);
        await pipe.Writer.CompleteAsync();
        await pipe.Reader.CompleteAsync();
        return data;
    }
}
