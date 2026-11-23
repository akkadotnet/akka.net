// -----------------------------------------------------------------------
// <copyright file="ArteryInboundLaneConsumerSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Remote.Artery;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.TestKit;
using Xunit;

namespace Akka.Remote.Tests.Artery;

public sealed class ArteryInboundLaneConsumerSpec : AkkaSpec
{
    public ArteryInboundLaneConsumerSpec(ITestOutputHelper output) : base(output) { }

    private static byte[] Preamble() => new byte[] { (byte)'A', (byte)'K', (byte)'K', (byte)'A', (byte)ArteryStreamId.Ordinary };

    private ArteryInboundProcessingStage CreateStage(Action<object> deliver)
    {
        var context = new AssociationRegistryInboundContext(new AssociationRegistry(),
            new UniqueAddress(new Address("akka", "local", "127.0.0.1", 1234), 1), (_, _) => { });
        context.CompleteHandshake(new UniqueAddress(new Address("akka", "peer", "127.0.0.1", 4321), 42));
        return new ArteryInboundProcessingStage(65536, 65536, Sys.Serialization, Sys.Scheduler,
            inboundLanes: 2, inboundLaneBufferSize: 4, inboundContext: context,
            dispatchOrdinary: (message, _, _) => deliver(message));
    }

    private Task Run(ArteryInboundProcessingStage stage, bool keepOpen)
    {
        using var encoded = ArteryEnvelopeCodec.Encode(Sys.Serialization, 42, null, "akka://local/user/target", "frame");
        var bytes = Preamble().Concat(encoded.WrittenSpan.ToArray()).ToArray();
        var source = Source.Single(new ReadOnlySequence<byte>(bytes));
        if (keepOpen)
            source = source.Concat(Source.Never<ReadOnlySequence<byte>>());
        return source.Via(stage).RunWith(Sink.Ignore<IInboundEnvelope>(), Sys.Materializer());
    }

    [Fact(DisplayName = "Should_FailStage_When_LaneConsumerEndsAbnormally")]
    public async Task Should_FailStage_When_LaneConsumerEndsAbnormally()
    {
        // A fatal exception is not swallowed by the consumer loop, so the consumer task faults.
        var completion = Run(CreateStage(_ => throw new OutOfMemoryException("simulated")), keepOpen: true);
        await Assert.ThrowsAsync<OutOfMemoryException>(() => completion.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact(DisplayName = "Should_NotFailStage_When_LaneConsumerCompletesNormally")]
    public async Task Should_NotFailStage_When_LaneConsumerCompletesNormally()
    {
        var delivered = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = Run(CreateStage(message => delivered.TrySetResult(message)), keepOpen: false);
        await completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("frame", await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }
}
