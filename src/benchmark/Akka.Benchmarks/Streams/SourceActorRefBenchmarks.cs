//-----------------------------------------------------------------------
// <copyright file="SourceActorRefBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Streams;
using Akka.Streams.Dsl;
using BenchmarkDotNet.Attributes;

namespace Akka.Benchmarks.Streams
{
    /// <summary>
    /// Baseline throughput benchmarks for actor-driven stream ingress sources.
    ///
    /// <para>
    /// <see cref="Source.ActorRef{T}(int,OverflowStrategy)"/> is currently implemented on top of the legacy
    /// <c>ActorPublisher&lt;T&gt;</c> machinery: every element crosses an actor mailbox and then flows through
    /// <c>ActorPublisher</c> subscription/demand bookkeeping before reaching the stream boundary. It is a
    /// candidate for migration to a stream-native <c>GraphStageWithMaterializedValue</c>/<c>StageActorRef</c>
    /// implementation. These benchmarks capture the <em>current</em> numbers so that migration work can be
    /// compared against a known baseline and guarded against regressions.
    /// </para>
    ///
    /// <para>
    /// <see cref="Source.Queue{T}(int,OverflowStrategy)"/> is included as a reference point: it is the other
    /// common "push from outside the stream" ingress source, but it is backpressure-aware and stream-native,
    /// so it frames roughly where a modernized <c>Source.ActorRef</c> could land.
    /// </para>
    /// </summary>
    [Config(typeof(ThroughputBenchmarkConfig))]
    public class SourceActorRefBenchmarks
    {
        private const int ElementCount = 100_000;

        private ActorSystem _system;
        private ActorMaterializer _materializer;

        [GlobalSetup]
        public void Setup()
        {
            _system = ActorSystem.Create("source-actorref-bench");
            _materializer = _system.Materializer();
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            _materializer.Dispose();
            _system.Dispose();
        }

        /// <summary>
        /// Pushes <see cref="ElementCount"/> elements through the current <c>ActorPublisher</c>-backed
        /// <see cref="Source.ActorRef{T}(int,OverflowStrategy)"/> ingress path and waits for the stream to drain.
        /// </summary>
        /// <remarks>
        /// The buffer is sized to hold every element and uses <see cref="OverflowStrategy.Fail"/> so that a
        /// dropped element surfaces as a stream failure rather than silently skewing the throughput number
        /// (<c>Source.ActorRef</c> does not support backpressure). Completion is requested with
        /// <see cref="Status.Success"/>, which drains the buffered elements before signaling completion.
        /// </remarks>
        [Benchmark(OperationsPerInvoke = ElementCount)]
        public async Task Source_ActorRef_Ingress_Throughput()
        {
            var (actorRef, completion) = Source.ActorRef<int>(ElementCount, OverflowStrategy.Fail)
                .ToMaterialized(Sink.Ignore<int>(), Keep.Both)
                .Run(_materializer);

            for (var i = 0; i < ElementCount; i++)
                actorRef.Tell(i);

            actorRef.Tell(new Status.Success(Done.Instance));
            await completion;
        }

        /// <summary>
        /// Reference baseline: pushes <see cref="ElementCount"/> elements through the stream-native,
        /// backpressure-aware <see cref="Source.Queue{T}(int,OverflowStrategy)"/> ingress path.
        /// </summary>
        [Benchmark(OperationsPerInvoke = ElementCount)]
        public async Task Source_Queue_Ingress_Throughput()
        {
            var (queue, completion) = Source.Queue<int>(1024, OverflowStrategy.Backpressure)
                .ToMaterialized(Sink.Ignore<int>(), Keep.Both)
                .Run(_materializer);

            for (var i = 0; i < ElementCount; i++)
                await queue.OfferAsync(i);

            queue.Complete();
            await completion;
        }
    }
}
