//-----------------------------------------------------------------------
// <copyright file="ArteryIngressSourceBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams;
using Akka.Streams.Dsl;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Remoting.Artery
{
    /// <summary>
    /// <b>Task 0.4 — per-message ingress-hop penalty.</b> The outbound path's shape: one
    /// stream element per MESSAGE (not per chunk), pushed from outside the stream — exactly
    /// where Artery's <c>SendQueue</c> lives. Compares stock <c>ChannelSource.FromReader</c>
    /// (drain-many hot path: sync <c>TryRead</c> per pull, one coalesced async wakeup on
    /// empty) against stock <c>Source.Queue</c> (verified in design.md: one mailbox hop per
    /// offer, plus a <c>Task</c> allocation per offer from its async API). Bare source →
    /// <c>Sink.Ignore</c> so the ingress cost dominates. The chunk-granularity comparison
    /// (where the hop is amortized ~1,700×) lives in <see cref="ArterySingleIslandBenchmarks"/>.
    ///
    /// <para>
    /// A custom drain-many channel-source prototype was measured head-to-head against
    /// <c>ChannelSource.FromReader</c> and removed after landing identically (67–74ns/msg,
    /// 1B/msg both; N=3 on 2026-07-03) — the stock core infrastructure already does the job,
    /// so the prototype was redundant.
    /// </para>
    /// </summary>
    [Config(typeof(ArterySubstrateConfig))]
    public class ArteryIngressSourceBenchmarks
    {
        private ActorSystem _system = null!;
        private ActorMaterializer _materializer = null!;
        private ArteryFrameCorpus _corpus = null!;

        private Channel<InboundFrame> _channel = null!;
        private ISourceQueueWithComplete<InboundFrame> _queue = null!;
        private Task _streamDone = null!;

        [GlobalSetup]
        public void GlobalSetup()
        {
            _system = ActorSystem.Create("artery-ingress-bench", ArterySubstrateFixture.SystemConfig);
            _materializer = _system.Materializer();
            _corpus = ArterySubstrateFixture.CreateCorpus();
        }

        [GlobalCleanup]
        public void GlobalCleanup()
        {
            _materializer.Dispose();
            _system.Dispose();
        }

        [IterationSetup(Target = nameof(SourceQueue_PerMessage))]
        public void SetupSourceQueue()
        {
            var (queue, done) = Source.Queue<InboundFrame>(
                    ArterySubstrateFixture.MessageChannelCapacity, OverflowStrategy.Backpressure)
                .ToMaterialized(Sink.Ignore<InboundFrame>(), Keep.Both)
                .Run(_materializer);
            _queue = queue;
            _streamDone = done;
        }

        [Benchmark(OperationsPerInvoke = ArterySubstrateFixture.TotalMessages)]
        public async Task SourceQueue_PerMessage()
        {
            var frames = _corpus.DecodedFrames;
            for (var r = 0; r < ArterySubstrateFixture.Repeat; r++)
            {
                for (var i = 0; i < frames.Length; i++)
                    await _queue.OfferAsync(frames[i]);
            }

            _queue.Complete();
            await _streamDone;
        }

        [IterationSetup(Target = nameof(ChannelSourceFromReader_PerMessage))]
        public void SetupChannelSourceFromReader()
        {
            _channel = Channel.CreateBounded<InboundFrame>(
                new BoundedChannelOptions(ArterySubstrateFixture.MessageChannelCapacity)
                {
                    SingleReader = true,
                    SingleWriter = false
                });

            _streamDone = ChannelSource.FromReader(_channel.Reader)
                .RunWith(Sink.Ignore<InboundFrame>(), _materializer);
        }

        /// <summary>
        /// Head-to-head against <see cref="SourceQueue_PerMessage"/>: this benchmark drives the
        /// EXISTING core <c>ChannelSource.FromReader</c> (backed by <c>ChannelSourceLogic</c>)
        /// through the per-message ingress workload. Artery can just use the stock
        /// <c>ChannelSource</c> for its drain-many ingress.
        /// </summary>
        [Benchmark(OperationsPerInvoke = ArterySubstrateFixture.TotalMessages)]
        public Task ChannelSourceFromReader_PerMessage()
        {
            var writer = _channel.Writer;
            var frames = _corpus.DecodedFrames;
            for (var r = 0; r < ArterySubstrateFixture.Repeat; r++)
            {
                for (var i = 0; i < frames.Length; i++)
                {
                    while (!writer.TryWrite(frames[i]))
                        Thread.SpinWait(16);
                }
            }

            writer.Complete();
            return _streamDone;
        }
    }

    /// <summary>
    /// Calibrates the deserialize knob: the raw CPU cost of <see cref="DeserializeKnob.Run"/>
    /// per knob value, with no actors or streams involved. These per-message ns costs are the
    /// denominators for the CPU-ns/msg analysis in task 0.5.
    /// </summary>
    [Config(typeof(Configurations.MicroBenchmarkConfig))]
    public class DeserializeKnobCalibrationBenchmarks
    {
        [Params(32, 128, 1024)]
        public int HashBytes { get; set; }

        private InboundFrame _frame = null!;

        [GlobalSetup]
        public void GlobalSetup()
        {
            var corpus = new ArteryFrameCorpus(
                messageCount: 64, recipientCount: 64,
                ArterySubstrateFixture.PayloadSize, ArterySubstrateFixture.ChunkSize);
            _frame = corpus.DecodedFrames[0];
        }

        [Benchmark]
        public ulong Knob() => DeserializeKnob.Run(_frame.Payload, HashBytes);
    }
}
