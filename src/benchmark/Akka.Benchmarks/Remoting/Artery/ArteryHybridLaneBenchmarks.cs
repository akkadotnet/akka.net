//-----------------------------------------------------------------------
// <copyright file="ArteryHybridLaneBenchmarks.cs" company="Akka.NET Project">
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
    /// <b>Task 0 follow-up config: hybrid stream-island + actor lanes.</b> Motivated by the
    /// smoke-run finding that the stream <c>.Async()</c> boundary costs ~500ns/msg on this
    /// hardware (config 3's lane fan-out point saturates below the single fused island's
    /// ceiling, and lane count cannot recover it). This config keeps the fused serial island
    /// for framing + header decode — where streams earn their keep — but fans out to N lane
    /// ACTORS via a plain mailbox <c>Tell</c> from the island's sink, replacing the stream
    /// async boundary with the cheaper actor hop that config 1 showed running ~3-5× faster.
    /// If this recovers lane scaling, Artery's inbound lanes should be actor-based (or
    /// custom-boundary-based) rather than <c>Partition + .Async()</c>-based.
    /// </summary>
    [Config(typeof(ArterySubstrateConfig))]
    public class ArteryHybridLaneBenchmarks
    {
        [Params(1, 2, 4, 8, 16)]
        public int Lanes { get; set; }

        [Params(32, 128, 1024)]
        public int HashBytes { get; set; }

        private ActorSystem _system = null!;
        private ActorMaterializer _materializer = null!;
        private ArteryFrameCorpus _corpus = null!;

        private IActorRef[]? _recipients;
        private IActorRef[] _lanes = null!;
        private Task _allDone = null!;
        private Channel<ReadOnlySequence<byte>> _channel = null!;
        private int _iteration;

        [GlobalSetup]
        public void GlobalSetup()
        {
            _system = ActorSystem.Create("artery-hybrid-lanes", ArterySubstrateFixture.SystemConfig);
            _materializer = _system.Materializer();
            _corpus = ArterySubstrateFixture.CreateCorpus();
        }

        [GlobalCleanup]
        public void GlobalCleanup()
        {
            _materializer.Dispose();
            _system.Dispose();
        }

        [IterationSetup]
        public void IterationSetup()
        {
            var iteration = ++_iteration;
            _recipients = ArterySubstrateFixture.SpawnRecipients(_system, out _allDone);

            _lanes = new IActorRef[Lanes];
            for (var i = 0; i < Lanes; i++)
            {
                _lanes[i] = _system.ActorOf(
                    Props.Create(() => new DeserializeLaneActor(_recipients, HashBytes)),
                    $"hybrid-lane-{iteration}-{i}");
            }

            _channel = Channel.CreateBounded<ReadOnlySequence<byte>>(
                new BoundedChannelOptions(ArterySubstrateFixture.ChunkChannelCapacity)
                {
                    SingleReader = true,
                    SingleWriter = true
                });

            var lanes = _lanes;
            var laneCount = Lanes;

            ChannelSource.FromReader(_channel.Reader)
                .Via(Framing.LengthField(
                    ArteryEnvelopeCodec.FrameLengthFieldLength,
                    ArterySubstrateFixture.MaxFrameLength,
                    0, ByteOrder.LittleEndian))
                .Select(ArteryEnvelopeCodec.Decode)
                .RunWith(
                    Sink.ForEach<InboundFrame>(envelope =>
                        lanes[envelope.RecipientId % laneCount].Tell(envelope)),
                    _materializer);
        }

        [IterationCleanup]
        public void IterationCleanup()
        {
            foreach (var lane in _lanes)
                _system.Stop(lane);
            ArterySubstrateFixture.StopRecipients(_system, _recipients);
        }

        [Benchmark(OperationsPerInvoke = ArterySubstrateFixture.TotalMessages)]
        public Task HybridLanes()
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

        /// <summary>
        /// Actor lane: deserialize knob + dispatch to the recipient. Identical work to
        /// config 3's stream lane, but reached via a mailbox hop instead of a stream
        /// async boundary.
        /// </summary>
        private sealed class DeserializeLaneActor : UntypedActor
        {
            private readonly IActorRef[] _recipients;
            private readonly int _hashBytes;

            public DeserializeLaneActor(IActorRef[] recipients, int hashBytes)
            {
                _recipients = recipients;
                _hashBytes = hashBytes;
            }

            protected override void OnReceive(object message)
            {
                if (message is not InboundFrame envelope)
                    return;

                envelope.Checksum = DeserializeKnob.Run(envelope.Payload, _hashBytes);
                _recipients[envelope.RecipientId].Tell(envelope);
            }
        }
    }
}
