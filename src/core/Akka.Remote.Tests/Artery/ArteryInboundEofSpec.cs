// -----------------------------------------------------------------------
// <copyright file="ArteryInboundEofSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
// -----------------------------------------------------------------------

#nullable enable

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Remote.Artery;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.TestKit;
using Akka.TestKit;
using Xunit;

namespace Akka.Remote.Tests.Artery;

public sealed class ArteryInboundEofSpec : AkkaSpec
{
    public ArteryInboundEofSpec(ITestOutputHelper output) : base(output) { }

    public static IEnumerable<object[]> TruncatedInputs()
    {
        foreach (var lanes in new[] { 1, 4 })
        foreach (var fragmented in new[] { false, true })
        foreach (var tail in new[] { "preamble", "length", "body" })
            yield return new object[] { lanes, fragmented, tail };
    }

    private static byte[] Preamble() => new byte[] { (byte)'A', (byte)'K', (byte)'K', (byte)'A', (byte)ArteryStreamId.Ordinary };

    private ArteryInboundProcessingStage CreateStage(int lanes, Action<object> deliver)
    {
        var context = new AssociationRegistryInboundContext(new AssociationRegistry(),
            new UniqueAddress(new Address("akka", "local", "127.0.0.1", 1234), 1), (_, _) => { });
        context.CompleteHandshake(new UniqueAddress(new Address("akka", "peer", "127.0.0.1", 4321), 42));
        return new ArteryInboundProcessingStage(65536, 65536, Sys.Serialization, Sys.Scheduler,
            inboundLanes: lanes, inboundLaneBufferSize: 4, inboundContext: context,
            dispatchOrdinary: (payload, _, _) => deliver(payload));
    }

    private static Source<ReadOnlySequence<byte>, NotUsed> Input(byte[] bytes, bool fragmented) => fragmented
        ? Source.From(bytes.Select(value => new ReadOnlySequence<byte>(new[] { value })))
        : Source.Single(new ReadOnlySequence<byte>(bytes));

    [Theory(DisplayName = "Natural Artery EOF rejects incomplete preambles, frame lengths and frame bodies")]
    [MemberData(nameof(TruncatedInputs))]
    public async Task Should_FailIncompleteInput_When_UpstreamEnds(int lanes, bool fragmented, string tail)
    {
        var bytes = tail switch
        {
            "preamble" => Preamble()[..4],
            "length" => Preamble().Concat(new byte[] { 8, 0 }).ToArray(),
            _ => Preamble().Concat(new byte[] { 8, 0, 0, 0, 1, 2 }).ToArray()
        };
        var materializer = ActorMaterializer.Create(Sys);
        try
        {
            var completion = Input(bytes, fragmented).Via(CreateStage(lanes, _ => throw new InvalidOperationException("No full frame.")))
                .RunWith(Sink.Ignore<IInboundEnvelope>(), materializer);
            await Assert.ThrowsAsync<ArteryFramingException>(() => completion.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            materializer.Shutdown();
        }
    }

    [Theory(DisplayName = "Empty connections and complete Artery input keep clean EOF and accepted messages")]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(4, false)]
    [InlineData(4, true)]
    public async Task Should_CompleteAndDeliverAcceptedFrames_When_InputIsWhole(int lanes, bool fragmented)
    {
        using var first = ArteryEnvelopeCodec.Encode(Sys.Serialization, 42, null, "akka://local/user/target", "first");
        using var second = ArteryEnvelopeCodec.Encode(Sys.Serialization, 42, null, "akka://local/user/target", "second");
        var frames = first.WrittenSpan.ToArray().Concat(second.WrittenSpan.ToArray()).ToArray();
        var materializer = ActorMaterializer.Create(Sys);
        try
        {
            foreach (var bytes in new[] { Array.Empty<byte>(), Preamble(), Preamble().Concat(frames).ToArray() })
            {
                var delivered = new ConcurrentQueue<object>();
                await Input(bytes, fragmented).Via(CreateStage(lanes, delivered.Enqueue))
                    .RunWith(Sink.ForEach<IInboundEnvelope>(envelope => delivered.Enqueue(envelope.Message)), materializer)
                    .WaitAsync(TimeSpan.FromSeconds(5));
                await AwaitAssertAsync(() => Assert.Equal(bytes.Length > Preamble().Length
                    ? new object[] { "first", "second" } : Array.Empty<object>(), delivered.ToArray()));
            }
        }
        finally
        {
            materializer.Shutdown();
        }
    }

    [Theory(DisplayName = "An upstream failure retains its cause even with a partially read Artery preamble")]
    [InlineData(1)]
    [InlineData(4)]
    public async Task Should_PreserveFailure_When_UpstreamFails(int lanes)
    {
        var materializer = ActorMaterializer.Create(Sys);
        var failure = new InvalidOperationException("upstream failed");
        try
        {
            var (queue, completion) = Source.Queue<ReadOnlySequence<byte>>(1, OverflowStrategy.Backpressure)
                .Via(CreateStage(lanes, _ => { }))
                .ToMaterialized(Sink.Ignore<IInboundEnvelope>(), Keep.Both).Run(materializer);
            await queue.OfferAsync(new ReadOnlySequence<byte>(Preamble()[..2]));
            queue.Fail(failure);
            var observed = await Record.ExceptionAsync(() => completion.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Same(failure, observed);
        }
        finally
        {
            materializer.Shutdown();
        }
    }
    [Theory(DisplayName = "Artery EOF checks framing after pending output has drained")]
    [InlineData(1)]
    [InlineData(4)]
    public async Task Should_RejectTruncatedTail_When_PendingOutputDrainsAfterEof(int lanes)
    {
        using var encoded = ArteryEnvelopeCodec.Encode(Sys.Serialization, 42, null, null, new ArteryHeartbeat());
        var bytes = Preamble().Concat(encoded.WrittenSpan.ToArray()).Concat(encoded.WrittenSpan.ToArray())
            .Concat(new byte[] { 8, 0 }).ToArray();
        var materializer = ActorMaterializer.Create(Sys);
        try
        {
            var (sourceFinished, subscriber) = Source.Single(new ReadOnlySequence<byte>(bytes))
                .WatchTermination((_, finished) => finished)
                .Via(CreateStage(lanes, _ => throw new InvalidOperationException("Only control frames expected.")))
                .ToMaterialized(this.SinkProbe<IInboundEnvelope>(), Keep.Both).Run(materializer);
            await subscriber.RequestAsync(1);
            Assert.IsType<ArteryHeartbeat>((await subscriber.ExpectNextAsync()).Message);
            await sourceFinished.WaitAsync(TimeSpan.FromSeconds(5));
            await subscriber.RequestAsync(1);
            Assert.IsType<ArteryHeartbeat>((await subscriber.ExpectNextAsync()).Message);
            await subscriber.RequestAsync(1);
            Assert.IsType<ArteryFramingException>(await subscriber.ExpectErrorAsync());
        }
        finally
        {
            materializer.Shutdown();
        }
    }

    [Fact(DisplayName = "Artery EOF checks framing after a full lane resumes and drains accepted work")]
    public async Task Should_RejectTruncatedTail_When_LaneBackpressureClearsAfterEof()
    {
        using var encoded = ArteryEnvelopeCodec.Encode(Sys.Serialization, 42, null, "akka://local/user/target", "frame");
        var frame = encoded.WrittenSpan.ToArray();
        var bytes = Preamble().Concat(Enumerable.Range(0, 12).SelectMany(_ => frame)).Concat(new byte[] { 8, 0 }).ToArray();
        var entered = new TaskCompletionSource<Done>(TaskCreationOptions.RunContinuationsAsynchronously);
        var delivered = new ConcurrentQueue<object>();
        var gate = new object();
        var open = false;
        void Release()
        {
            lock (gate)
            {
                open = true;
                Monitor.PulseAll(gate);
            }
        }
        var stage = CreateStage(4, payload =>
        {
            lock (gate)
            {
                entered.TrySetResult(Done.Instance);
                while (!open)
                    Monitor.Wait(gate);
            }
            delivered.Enqueue(payload);
        });
        var materializer = ActorMaterializer.Create(Sys);
        try
        {
            var (sourceFinished, completion) = Source.Single(new ReadOnlySequence<byte>(bytes))
                .WatchTermination((_, finished) => finished).Via(stage)
                .ToMaterialized(Sink.Ignore<IInboundEnvelope>(), Keep.Both).Run(materializer);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await sourceFinished.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(completion.IsCompleted, "the full lane must hold completion until accepted work can progress");
            Release();
            await Assert.ThrowsAsync<ArteryFramingException>(() => completion.WaitAsync(TimeSpan.FromSeconds(5)));
            await AwaitAssertAsync(() => Assert.Equal(12, delivered.Count));
        }
        finally
        {
            Release();
            materializer.Shutdown();
        }
    }

    [Theory(DisplayName = "Downstream cancellation of partial Artery input remains successful cancellation")]
    [InlineData(1)]
    [InlineData(4)]
    public async Task Should_AllowCancellation_When_InputIsPartial(int lanes)
    {
        var materializer = ActorMaterializer.Create(Sys);
        try
        {
            var ((queue, killSwitch), completion) = Source.Queue<ReadOnlySequence<byte>>(1, OverflowStrategy.Backpressure)
                .Via(CreateStage(lanes, _ => { }))
                .ViaMaterialized(KillSwitches.Single<IInboundEnvelope>(), Keep.Both)
                .ToMaterialized(Sink.Ignore<IInboundEnvelope>(), Keep.Both).Run(materializer);
            await queue.OfferAsync(new ReadOnlySequence<byte>(Preamble()[..2]));
            killSwitch.Shutdown();
            await completion.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            materializer.Shutdown();
        }
    }

}
