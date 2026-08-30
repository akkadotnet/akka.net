//-----------------------------------------------------------------------
// <copyright file="ArterySingleIslandBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System.Buffers;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Util;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Remoting.Artery
{
    /// <summary>
    /// <b>Task 0.2 — Config 2: single-island streams.</b> <c>ChannelSource.FromReader</c> (drain-many
    /// source) → LengthField framing → header decode → deserialize → per-recipient dispatch, all fused into ONE
    /// interpreter island (no <c>.Async()</c> anywhere). Read against config 1 this yields the
    /// raw interpreter tax; on its own it is the <b>serial-island ceiling — the single most
    /// important number of gate G0</b>: it bounds what one connection's inbound decode island
    /// can ever do, regardless of lane count, and must clear ~680K msgs/sec with margin.
    ///
    /// <para>
    /// The <c>SourceQueue</c> variant (task 0.4) swaps the ingress for stock
    /// <c>Source.Queue</c> at the same chunk granularity — the per-offer mailbox hop is
    /// amortized over ~900 messages per chunk here, so any large delta indicates trouble
    /// beyond the known per-offer hop. The per-message hop cost is measured separately in
    /// <see cref="ArteryIngressSourceBenchmarks"/>.
    /// </para>
    /// </summary>
    [Config(typeof(ArterySubstrateConfig))]
    public class ArterySingleIslandBenchmarks
    {
        [Params(32, 128, 1024)]
        public int HashBytes { get; set; }

        private ActorSystem _system = null!;
        private ActorMaterializer _materializer = null!;
        private ArteryFrameCorpus _corpus = null!;

        private IActorRef[]? _recipients;
        private Task _allDone = null!;
        private Channel<ReadOnlySequence<byte>> _channel = null!;
        private ISourceQueueWithComplete<ReadOnlySequence<byte>> _queue = null!;

        [GlobalSetup]
        public void GlobalSetup()
        {
            _system = ActorSystem.Create("artery-single-island", ArterySubstrateFixture.SystemConfig);
            _materializer = _system.Materializer();
            _corpus = ArterySubstrateFixture.CreateCorpus();
        }

        [GlobalCleanup]
        public void GlobalCleanup()
        {
            _materializer.Dispose();
            _system.Dispose();
        }

        private Flow<ReadOnlySequence<byte>, InboundFrame, NotUsed> BuildIslandFlow()
        {
            var hashBytes = HashBytes; // local copy: avoid per-message field loads in the closure
            return Flow.Create<ReadOnlySequence<byte>>()
                .Via(Framing.LengthField(
                    ArteryEnvelopeCodec.FrameLengthFieldLength,
                    ArterySubstrateFixture.MaxFrameLength,
                    0, ByteOrder.LittleEndian))
                .Select(ArteryEnvelopeCodec.Decode)
                .Select(envelope =>
                {
                    envelope.Checksum = DeserializeKnob.Run(envelope.Payload, hashBytes);
                    return envelope;
                });
        }

        private Sink<InboundFrame, Task<Done>> BuildDispatchSink()
        {
            var recipients = _recipients!;
            return Sink.ForEach<InboundFrame>(envelope => recipients[envelope.RecipientId].Tell(envelope));
        }

        [IterationSetup(Target = nameof(SingleIsland_ChannelDrain))]
        public void SetupChannelDrain()
        {
            _recipients = ArterySubstrateFixture.SpawnRecipients(_system, out _allDone);
            _channel = Channel.CreateBounded<ReadOnlySequence<byte>>(
                new BoundedChannelOptions(ArterySubstrateFixture.ChunkChannelCapacity)
                {
                    SingleReader = true,
                    SingleWriter = true
                });

            ChannelSource.FromReader(_channel.Reader)
                .Via(BuildIslandFlow())
                .RunWith(BuildDispatchSink(), _materializer);
        }

        [IterationCleanup(Target = nameof(SingleIsland_ChannelDrain))]
        public void CleanupChannelDrain() => ArterySubstrateFixture.StopRecipients(_system, _recipients);

        [Benchmark(OperationsPerInvoke = ArterySubstrateFixture.TotalMessages)]
        public Task SingleIsland_ChannelDrain()
        {
            var writer = _channel.Writer;
            var chunks = _corpus.Chunks;
            for (var r = 0; r < ArterySubstrateFixture.Repeat; r++)
            {
                for (var c = 0; c < chunks.Length; c++)
                {
                    while (!writer.TryWrite(chunks[c]))
                        Thread.SpinWait(16);
                }
            }

            writer.Complete();
            return _allDone;
        }

        [IterationSetup(Target = nameof(SingleIsland_SourceQueue))]
        public void SetupSourceQueue()
        {
            _recipients = ArterySubstrateFixture.SpawnRecipients(_system, out _allDone);
            _queue = Source.Queue<ReadOnlySequence<byte>>(
                    ArterySubstrateFixture.ChunkChannelCapacity, OverflowStrategy.Backpressure)
                .Via(BuildIslandFlow())
                .ToMaterialized(BuildDispatchSink(), Keep.Left)
                .Run(_materializer);
        }

        [IterationCleanup(Target = nameof(SingleIsland_SourceQueue))]
        public void CleanupSourceQueue() => ArterySubstrateFixture.StopRecipients(_system, _recipients);

        [Benchmark(OperationsPerInvoke = ArterySubstrateFixture.TotalMessages)]
        public async Task SingleIsland_SourceQueue()
        {
            var chunks = _corpus.Chunks;
            for (var r = 0; r < ArterySubstrateFixture.Repeat; r++)
            {
                for (var c = 0; c < chunks.Length; c++)
                    await _queue.OfferAsync(chunks[c]);
            }

            _queue.Complete();
            await _allDone;
        }
    }
}
