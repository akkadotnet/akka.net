#nullable enable
using System;
using System.Buffers;
using System.IO.Pipelines;
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
        var expected = new byte[TcpConnection.PipeReadToActor.ReadCopyChunkSize + 1];
        new Random(17).NextBytes(expected);
        await pipe.Writer.WriteAsync(expected);
        var probe = CreateTestProbe();
        var reader = new TcpConnection.PipeReadToActor(pipe.Reader, CancellationToken.None, probe.Ref);
        reader.Start();
        var delivered = (await probe.ExpectMsgAsync<TcpConnection.PipeReadCompleted>()).Data;
        Assert.Equal(expected.Length, delivered.Length);
        var tail = delivered.Slice(TcpConnection.PipeReadToActor.ReadCopyChunkSize);
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
}
