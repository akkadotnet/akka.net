//-----------------------------------------------------------------------
// <copyright file="ArteryEncodeStageBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.IO;
using Akka.Remote.Artery;
using Akka.Streams;
using Akka.Streams.Dsl;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;

namespace Akka.Benchmarks.Remoting.Artery
{
    /// <summary>
    /// Stage-level micro for <c>Akka.Remote.Artery.ArteryEncodeStage</c>'s <c>OnPush</c> path --
    /// this is DELIBERATELY NOT the same thing <see cref="ArteryEnvelopeEncodeBenchmarks"/> covers.
    /// That benchmark isolates <see cref="Akka.Remote.Artery.ArteryEnvelopeCodec"/>'s own
    /// copy-vs-detach choice (unchanged by the PR2 refactor); THIS benchmark drives the REAL
    /// production <c>ArteryEncodeStage</c> graph stage through a minimal single-island
    /// materialized graph (<c>Source(channel) -&gt; ArteryEncodeStage -&gt; Sink.ForEach</c>, no
    /// <c>.Async()</c> boundary), so the only thing that differs between the two A/B commits under
    /// test is the stage's OWN OnPush/OnPull bookkeeping:
    /// <list type="bullet">
    /// <item><description>
    /// BASELINE (293c5835d) keeps two outstanding <c>IMemoryOwner&lt;byte&gt;</c> fields on the
    /// stage itself (the "2-generation lag") and pushes a MEMORY-backed
    /// <c>new ReadOnlySequence&lt;byte&gt;(owner.Memory)</c> -- disposal is entirely internal to the
    /// stage, triggered by its own <c>OnPull</c>; nothing downstream needs to (or can -- there is no
    /// segment to walk) dispose anything.
    /// </description></item>
    /// <item><description>
    /// TREATMENT (b297bc572) pushes an owner-carrying, SEGMENT-backed sequence via
    /// <c>OwnedSequenceSegment.Create(owner)</c> and disposes nothing itself -- ownership moves
    /// downstream on every single push, so this benchmark's sink calls
    /// <see cref="OwnedSequenceSegmentExtensions.DisposeOwnedSegments"/> to play that downstream
    /// role (matching what write-coalescing does in production).
    /// </description></item>
    /// </list>
    /// <para>
    /// <b>What this harness does NOT isolate.</b> The measured region includes real
    /// <c>ArteryEnvelopeCodec.Encode(Serialization, ...)</c> serializer-lookup/encode cost AND the
    /// graph-interpreter's own per-push dispatch overhead -- both are IDENTICAL production code
    /// paths on both commits (unrelated to the PR2 diff), so they contribute a SHARED constant to
    /// both variants' alloc/op and time/op rather than distorting the delta between them. This is a
    /// genuine, purpose-built addition to the benchmark suite (no pre-existing bench drove
    /// <c>ArteryEncodeStage</c> itself) -- kept as permanent infrastructure.
    /// </para>
    /// </summary>
    [Config(typeof(ArteryEncodeStageConfig))]
    public class ArteryEncodeStageBenchmarks
    {
        private const long OriginUid = 0x0102_0304_0506_0708L;
        private const string SenderPath = "akka://Sys@127.0.0.1:9001/user/sender-actor";
        private const string RecipientPath = "akka://Sys@127.0.0.1:9001/user/recipient-actor";

        /// <summary>
        /// Messages pushed through the stage per benchmark invocation (= OperationsPerInvoke).
        /// Sized so one invocation clears BenchmarkDotNet's 100ms iteration-time floor at the
        /// ~230-275ns/op this stage actually runs at once JIT/pool/GC are warmed to steady state
        /// (1_000_000 * ~270ns ~= 270ms) -- see <see cref="ArteryEncodeStageConfig"/> for why
        /// that floor was being missed before, and why 300_000 (sized off the OLD, warm-up-
        /// polluted ~800ns/op Monitoring figure) still wasn't enough.
        /// </summary>
        private const int MessageCount = 1_000_000;

        /// <summary>Payload sizes spanning small (control-message-scale), mid, and multi-KB messages -- matches <see cref="ArteryEnvelopeEncodeBenchmarks"/>.</summary>
        [Params(32, 256, 4096)]
        public int PayloadSize { get; set; }

        private ActorSystem _system = null!;
        private ActorMaterializer _materializer = null!;
        private IOutboundEnvelope[] _envelopes = null!;
        private Flow<IOutboundEnvelope, ReadOnlySequence<byte>, NotUsed> _flow = null!;

        private Channel<IOutboundEnvelope> _channel = null!;
        private Task _allDone = null!;

        [GlobalSetup]
        public void GlobalSetup()
        {
            _system = ActorSystem.Create("artery-encode-stage", ArterySubstrateFixture.SystemConfig);
            _materializer = _system.Materializer();

            var payload = new byte[PayloadSize];
            new Random(42).NextBytes(payload);

            _envelopes = new IOutboundEnvelope[MessageCount];
            for (var i = 0; i < MessageCount; i++)
                // Same byte[] instance reused across the corpus (matches how the codec-level
                // benchmark reuses one payload array) -- the encode call reads it fresh every time,
                // never mutates it.
                _envelopes[i] = new OutboundEnvelope(payload, SenderPath, RecipientPath);

            // The real production stage -- constructed once, materialized fresh every iteration
            // below so each iteration starts from a clean CreateLogic() (fresh _pendingDispose /
            // _pendingDisposeOlder fields at baseline; nothing to reset at treatment).
            _flow = Flow.FromGraph(new ArteryEncodeStage(_system.Serialization, OriginUid));
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
            var tcs = new TaskCompletionSource<Done>(TaskCreationOptions.RunContinuationsAsynchronously);
            _allDone = tcs.Task;
            var remaining = MessageCount;

            _channel = Channel.CreateBounded<IOutboundEnvelope>(
                new BoundedChannelOptions(ArterySubstrateFixture.ChunkChannelCapacity)
                {
                    SingleReader = true,
                    SingleWriter = true
                });

            ChannelSource.FromReader(_channel.Reader)
                .Via(_flow)
                .RunWith(Sink.ForEach<ReadOnlySequence<byte>>(seq =>
                {
                    // No-op at baseline (memory-backed sequence, nothing to walk); the REAL
                    // owner-return at treatment (segment-backed) -- this benchmark stands in for
                    // write-coalescing/TcpConnection's downstream disposal responsibility so pooled
                    // buffers don't pile up across thousands of BenchmarkDotNet invocations.
                    seq.DisposeOwnedSegments();
                    if (Interlocked.Decrement(ref remaining) == 0)
                        tcs.TrySetResult(Done.Instance);
                }), _materializer);
        }

        [Benchmark(OperationsPerInvoke = MessageCount)]
        public Task EncodeStage_OnPush()
        {
            var writer = _channel.Writer;
            var envelopes = _envelopes;
            for (var i = 0; i < envelopes.Length; i++)
            {
                while (!writer.TryWrite(envelopes[i]))
                    Thread.SpinWait(16);
            }

            writer.Complete();
            return _allDone;
        }
    }

    /// <summary>
    /// BenchmarkDotNet configuration for <see cref="ArteryEncodeStageBenchmarks"/> specifically
    /// -- deliberately NOT <c>ArterySubstrateConfig</c> (used by the macro substrate benchmarks
    /// in <see cref="ArterySubstrateFixture"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this benchmark can't share <c>ArterySubstrateConfig</c>.</b> That config pins
    /// <c>RunStrategy.Monitoring</c> with <c>RunOncePerIteration()</c>: no pilot stage, no
    /// overhead subtraction, exactly <c>WarmupCount</c> + <c>IterationCount</c> fixed calls.
    /// It's the right tool for the macro substrate benchmarks, whose single invocation already
    /// spans hundreds of ms driving ~1M messages. Reusing it here (at the original
    /// <c>MessageCount = 5_000</c>) produced ~750-900ns/op iterations of only ~3.7-4ms each --
    /// two orders of magnitude under BenchmarkDotNet's recommended 100ms floor -- which tripped
    /// a <c>MinIterationTime</c> warning and left the reported ns/op trustworthy only to an
    /// order of magnitude, exactly the number this investigation needs precision on (deciding
    /// between a ~550ns/msg and a ~320ns/msg hypothesis).
    /// </para>
    /// <para>
    /// <b>The fix.</b> <see cref="ArteryEncodeStageBenchmarks.MessageCount"/> was raised to
    /// 1_000_000 so a single invocation clears the 100ms floor with comfortable margin even
    /// under host load. (An initial pass raised it only to 300_000, sized off the OLD
    /// Monitoring-strategy figure of ~750-900ns/op -- but that figure turned out to be inflated
    /// by warm-up costs [JIT tiering / buffer-pool growth] that a 5_000-message, 2-warmup-
    /// iteration batch never fully paid off; once warmed, this stage actually runs at
    /// ~230-275ns/op, so 300_000 ops/invocation landed at only 65-87ms -- still short of the
    /// floor.) That alone doesn't let this config drop down to the fully-automatic
    /// <c>ThroughputBenchmarkConfig</c> pattern used elsewhere in this project (e.g.
    /// <c>StreamThroughputBenchmarks</c>): those benchmarks build a brand-new, stateless
    /// <c>Source.From(...)</c> pipeline on every call, so BenchmarkDotNet's pilot stage is free
    /// to unroll several calls into one measured iteration. This benchmark instead drains a
    /// single-use, <c>[IterationSetup]</c>-scoped <see cref="System.Threading.Channels.Channel{T}"/>
    /// -- real backpressure-producing plumbing, deliberately preserved rather than swapped for a
    /// synchronous <c>Source.From</c> (that would change WHAT the benchmark measures, not just
    /// HOW it's measured). A second call against the same channel after
    /// <c>writer.Complete()</c> would spin forever on <c>TryWrite</c>, so
    /// <c>InvocationCount</c>/<c>UnrollFactor</c> are explicitly pinned to 1 -- the engine must
    /// call <see cref="ArteryEncodeStageBenchmarks.EncodeStage_OnPush"/> exactly once per
    /// iteration, matching the existing <c>[IterationSetup]</c> contract.
    /// </para>
    /// <para>
    /// The net effect: BenchmarkDotNet's ordinary <c>RunStrategy.Throughput</c> engine (normal
    /// overhead-subtraction and statistics, NOT Monitoring), one real batch-of-1M call per
    /// iteration, <c>WarmupCount</c>/<c>IterationCount</c> left at the same 2/10 shape as before
    /// so total run time stays predictable.
    /// </para>
    /// </remarks>
    public class ArteryEncodeStageConfig : ManualConfig
    {
        public ArteryEncodeStageConfig()
        {
            AddDiagnoser(MemoryDiagnoser.Default);
            AddExporter(MarkdownExporter.GitHub);
            AddLogger(ConsoleLogger.Default);
            AddColumn(new RequestsPerSecondColumn());
            AddJob(Job.Default
                .WithGcMode(new GcMode { Server = true, Concurrent = true })
                .WithStrategy(RunStrategy.Throughput)
                .WithWarmupCount(2)
                .WithIterationCount(10)
                .WithInvocationCount(1)
                .WithUnrollFactor(1));
        }
    }
}
