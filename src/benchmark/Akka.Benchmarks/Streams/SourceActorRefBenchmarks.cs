//-----------------------------------------------------------------------
// <copyright file="SourceActorRefBenchmarks.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Diagnostics;
using System.Runtime;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Benchmarks.Configurations;
using Akka.Streams;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
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
        private const string ProducerSourceName = "Akka.Benchmarks.QueueProducer";
        private const string ReferencePayload = "hit";

        private ActorSystem _system = null!;
        private ActorMaterializer _materializer = null!;
        private ActivitySource _producerSource = null!;

        [GlobalSetup]
        public void Setup()
        {
            Console.WriteLine("Queue benchmark GC: server={0}, runtime={1}, processors={2}",
                GCSettings.IsServerGC, Environment.Version, Environment.ProcessorCount);
            foreach (var setting in GC.GetConfigurationVariables())
                Console.WriteLine("Queue benchmark GC config: {0}={1}", setting.Key, setting.Value);
            _system = ActorSystem.Create("source-actorref-bench");
            _materializer = _system.Materializer();
            _producerSource = new ActivitySource(ProducerSourceName);
            _ = StreamsDiagnostics.ActivitySource;
        }

        [GlobalCleanup]
        public async Task Cleanup()
        {
            _producerSource.Dispose();
            _materializer.Dispose();
            await _system.Terminate();
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
        public Task Source_Queue_Ingress_Throughput()
            => RunUntracedQueue(1, static value => value);

        [Benchmark(OperationsPerInvoke = ElementCount)]
        public Task Source_Queue_Reference_Ingress_Throughput()
            => RunUntracedQueue(ReferencePayload, static value => value.Length);

        [Benchmark(OperationsPerInvoke = ElementCount)]
        public Task Source_Queue_Traced_Ingress_Throughput()
            => RunTracedQueue(1, static value => value);

        [Benchmark(OperationsPerInvoke = ElementCount)]
        public Task Source_Queue_Traced_Reference_Ingress_Throughput()
            => RunTracedQueue(ReferencePayload, static value => value.Length);

        // Queue's async callback receives a reference-typed Offer command for both payload types.
        // These cases compare stream payloads; direct callback<T> boxing belongs in separate tests.
        private Task RunUntracedQueue<T>(T payload, Func<T, long> value)
        {
            if (Activity.Current is not null || StreamsDiagnostics.ActivitySource.HasListeners())
                throw new InvalidOperationException("Untraced queue control requires no activity or Streams listener.");
            return OfferAndDrain(payload, value);
        }

        private async Task RunTracedQueue<T>(T payload, Func<T, long> value)
        {
            var expectedParent = default(ActivityContext);
            var ingressCount = 0;
            var wrongParentCount = 0;
            using var listener = new ActivityListener
            {
                ShouldListenTo = static source => source.Name is ProducerSourceName or "Akka.Streams",
                Sample = static (ref ActivityCreationOptions<ActivityContext> options) =>
                    options.Source.Name == ProducerSourceName || options.Name == StreamsDiagnostics.OperationIngressQueued
                        || options.Name.StartsWith(StreamsDiagnostics.OperationIngress + " ", StringComparison.Ordinal)
                        ? ActivitySamplingResult.AllDataAndRecorded
                        : ActivitySamplingResult.None,
                ActivityStarted = activity =>
                {
                    if (activity.Source.Name != "Akka.Streams"
                        || !activity.OperationName.StartsWith(StreamsDiagnostics.OperationIngress + " ", StringComparison.Ordinal))
                        return;
                    Interlocked.Increment(ref ingressCount);
                    // A pre-initialization offer may have a queued-ingress span between it and
                    // the producer; it must still preserve the producer's trace and a real parent.
                    if (activity.TraceId != expectedParent.TraceId || activity.ParentSpanId == default)
                        Interlocked.Increment(ref wrongParentCount);
                }
            };
            ActivitySource.AddActivityListener(listener);
            using var producer = _producerSource.StartActivity("queue.offer.batch")
                ?? throw new InvalidOperationException("Traced queue control did not create a producer activity.");
            expectedParent = producer.Context;

            await OfferAndDrain(payload, value, () =>
            {
                if (Volatile.Read(ref ingressCount) != ElementCount || Volatile.Read(ref wrongParentCount) != 0)
                    throw new InvalidOperationException("Queue ingress did not preserve the producer trace for every offer.");
            });
        }

        private async Task OfferAndDrain<T>(T payload, Func<T, long> value, Action? beforeComplete = null)
        {
            var (queue, completion) = Source.Queue<T>(1024, OverflowStrategy.Backpressure)
                .ToMaterialized(Sink.Aggregate<T, (int Count, long Sum)>(default,
                    (totals, element) => (totals.Count + 1, totals.Sum + value(element))), Keep.Both)
                .Run(_materializer);

            for (var i = 0; i < ElementCount; i++)
            {
                if (await queue.OfferAsync(payload) is not QueueOfferResult.Enqueued)
                    throw new InvalidOperationException("Queue did not accept a benchmark element.");
            }

            beforeComplete?.Invoke();
            queue.Complete();
            var totals = await completion;
            if (totals.Count != ElementCount || totals.Sum != ElementCount * value(payload))
                throw new InvalidOperationException($"Incomplete queue delivery: {totals.Count} elements, sum {totals.Sum}.");
        }
    }
}
