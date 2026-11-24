#nullable enable
using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.IO;
using Akka.Streams.Implementation.IO;
using Akka.Streams.Dsl;
using Akka.TestKit;
using Xunit;

namespace Akka.Streams.Tests.IO;

public class TcpRetainedReadSpec : AkkaSpec
{
    public TcpRetainedReadSpec(ITestOutputHelper output) : base(output) { }

    [Fact(DisplayName = "A retained TCP delivery must survive writing its last segment through Streams")]
    public async Task Should_preserve_retained_delivery_when_tail_is_written()
    {
        var pipe = new Pipe(new PipeOptions(pauseWriterThreshold: 0));
        var expected = new byte[65537];
        new Random(17).NextBytes(expected);
        await pipe.Writer.WriteAsync(expected);
        var probe = CreateTestProbe();
        var reader = new TcpConnection.PipeReadToActor(pipe.Reader, CancellationToken.None, probe.Ref);
        reader.Start();
        var delivered = (await probe.ExpectMsgAsync<TcpConnection.PipeReadCompleted>()).Data;
        Assert.Equal(expected.Length, delivered.Length);
        var tail = delivered.Slice(65536);
        var completion = Source.Single(tail)
            .Via(new IncomingConnectionStage(probe.Ref, new IPEndPoint(IPAddress.Loopback, 1), true))
            .RunWith(Sink.Ignore<ReadOnlySequence<byte>>(), Sys.Materializer());
        await probe.FishForMessageAsync(message => message is Akka.IO.Tcp.Write, TimeSpan.FromSeconds(10));
        probe.LastSender.Tell(Akka.IO.Tcp.Closed.Instance);
        await completion.WaitAsync(TimeSpan.FromSeconds(10));
        await pipe.Writer.CompleteAsync();
        await pipe.Reader.CompleteAsync();
        Assert.Equal(expected.Length, delivered.Length);
        Assert.Equal(expected, delivered.ToArray());
    }

    [Theory(DisplayName = "Streams TCP writes must preserve caller-built sequence segments")]
    [InlineData(0, 12)]
    [InlineData(1, 10)]
    [InlineData(3, 6)]
    [InlineData(3, 9)]
    [InlineData(9, 3)]
    public async Task Should_preserve_user_segments_when_written(int offset, int length)
    {
        var expected = Enumerable.Range(0, 12).Select(i => (byte)i).ToArray();
        var segments = new UserSegment[4];
        for (var i = 0; i < segments.Length; i++)
            segments[i] = new UserSegment(expected.AsMemory(i * 3, 3), i == 0 ? null : segments[i - 1]);
        var original = new ReadOnlySequence<byte>(segments[0], 0, segments[^1], 3);
        var snapshots = segments.Select(s => (s.Memory, s.Next, s.RunningIndex)).ToArray();
        var positions = Enumerable.Range(0, 13).Select(i => original.GetPosition(i)).ToArray();
        var probe = CreateTestProbe();
        var completion = Source.Single(original.Slice(offset, length))
            .Via(new IncomingConnectionStage(probe.Ref, new IPEndPoint(IPAddress.Loopback, 1), true))
            .RunWith(Sink.Ignore<ReadOnlySequence<byte>>(), Sys.Materializer());
        var write = Assert.IsType<Akka.IO.Tcp.Write>(
            await probe.FishForMessageAsync(message => message is Akka.IO.Tcp.Write, TimeSpan.FromSeconds(10)));
        Assert.Equal(expected.AsSpan(offset, length).ToArray(), write.Data.ToArray());
        probe.LastSender.Tell(Akka.IO.Tcp.Closed.Instance);
        await completion.WaitAsync(TimeSpan.FromSeconds(10));

        for (var i = 0; i < segments.Length; i++)
        {
            Assert.Equal(snapshots[i].Memory, segments[i].Memory);
            Assert.Same(snapshots[i].Next, segments[i].Next);
            Assert.Equal(snapshots[i].RunningIndex, segments[i].RunningIndex);
        }
        Assert.Equal(expected.Length, original.Length);
        Assert.Equal(expected, original.ToArray());
        for (var i = 0; i < positions.Length; i++)
        {
            Assert.Equal(positions[i], original.GetPosition(i));
            Assert.Equal(expected.AsSpan(i).ToArray(), original.Slice(positions[i]).ToArray());
        }
    }

    private sealed class UserSegment : ReadOnlySequenceSegment<byte>
    {
        public UserSegment(ReadOnlyMemory<byte> memory, UserSegment? previous)
        {
            Memory = memory;
            if (previous is not null)
            {
                RunningIndex = previous.RunningIndex + previous.Memory.Length;
                previous.Next = this;
            }
        }
    }
}
