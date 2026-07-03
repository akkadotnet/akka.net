//-----------------------------------------------------------------------
// <copyright file="ArteryLaneStreamBenchmarks.cs" company="Akka.NET Project">
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
    /// <b>Task 0.3 — Config 3: lane streams.</b> <c>ChannelSource.FromReader</c> (drain-many
    /// source) → LengthField framing → header decode → <c>Partition(N)</c> by recipient hash → per-lane <c>.Async()</c>
    /// deserialize island → per-recipient dispatch. The serial decode/partition island stays
    /// light (framing + fixed-offset header reads + a recipient hash); the deserialize knob —
    /// the expensive step — runs on the lanes. Sweeping N ∈ {1,2,4,8,16} answers gate G0's
    /// second question: do lanes scale deserialize across cores linearly enough to recover
    /// the interpreter tax within the core budget?
    /// </summary>
    [Config(typeof(ArterySubstrateConfig))]
    public class ArteryLaneStreamBenchmarks
    {
        [Params(1, 2, 4, 8, 16)]
        public int Lanes { get; set; }

        [Params(32, 128, 1024)]
        public int HashBytes { get; set; }

        /// <summary>
        /// Input-buffer size at the lane's <c>.Async()</c> boundary. The materializer default
        /// (16) bounds how many elements cross per interpreter wakeup; smoke runs showed the
        /// boundary dominating lane throughput, so the sweep quantifies how much of that cost
        /// is recoverable by deeper boundary buffers (more wakeup coalescing) before concluding
        /// anything about lane viability.
        /// </summary>
        [Params(16, 512)]
        public int BoundaryBuffer { get; set; }

        private ActorSystem _system = null!;
        private ActorMaterializer _materializer = null!;
        private ArteryFrameCorpus _corpus = null!;

        private IActorRef[]? _recipients;
        private Task _allDone = null!;
        private Channel<ReadOnlySequence<byte>> _channel = null!;

        [GlobalSetup]
        public void GlobalSetup()
        {
            _system = ActorSystem.Create("artery-lane-streams", ArterySubstrateFixture.SystemConfig);
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
            _recipients = ArterySubstrateFixture.SpawnRecipients(_system, out _allDone);
            _channel = Channel.CreateBounded<ReadOnlySequence<byte>>(
                new BoundedChannelOptions(ArterySubstrateFixture.ChunkChannelCapacity)
                {
                    SingleReader = true,
                    SingleWriter = true
                });

            var lanes = Lanes;
            var hashBytes = HashBytes;
            var recipients = _recipients;

            RunnableGraph.FromGraph(GraphDsl.Create(b =>
            {
                var source = b.Add(ChannelSource.FromReader(_channel.Reader));
                var framing = b.Add(Framing.LengthField(
                    ArteryEnvelopeCodec.FrameLengthFieldLength,
                    ArterySubstrateFixture.MaxFrameLength,
                    0, ByteOrder.LittleEndian));
                var decode = b.Add(Flow.Create<ReadOnlySequence<byte>>().Select(ArteryEnvelopeCodec.Decode));
                // The serial decode/partition island ends here; everything below the
                // partition runs on its own lane island across the .Async() boundary.
                var partition = b.Add(new Partition<InboundFrame>(lanes, e => e.RecipientId % lanes));

                b.From(source).Via(framing).Via(decode).To(partition.In);

                var boundaryBuffer = BoundaryBuffer;
                for (var i = 0; i < lanes; i++)
                {
                    var lane = b.Add(Flow.Create<InboundFrame>()
                        .Select(envelope =>
                        {
                            envelope.Checksum = DeserializeKnob.Run(envelope.Payload, hashBytes);
                            return envelope;
                        })
                        .Async()
                        .AddAttributes(Attributes.CreateInputBuffer(boundaryBuffer, boundaryBuffer)));
                    var dispatch = b.Add(Sink.ForEach<InboundFrame>(
                        envelope => recipients[envelope.RecipientId].Tell(envelope)));

                    b.From(partition.Out(i)).Via(lane).To(dispatch);
                }

                return ClosedShape.Instance;
            })).Run(_materializer);
        }

        [IterationCleanup]
        public void IterationCleanup() => ArterySubstrateFixture.StopRecipients(_system, _recipients);

        [Benchmark(OperationsPerInvoke = ArterySubstrateFixture.TotalMessages)]
        public Task LaneStreams()
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
    }
}
