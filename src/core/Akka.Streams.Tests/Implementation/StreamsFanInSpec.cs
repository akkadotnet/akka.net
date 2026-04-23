//-----------------------------------------------------------------------
// <copyright file="StreamsFanInSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Streams.Tests.Implementation
{
    /// <summary>
    /// Validates trace-context propagation through Akka.Streams fan-in stages. The
    /// general pattern: N traced producers push elements that either accumulate into
    /// one merged output (Batch, BatchWeighted, GroupedWeightedWithin) or flow through
    /// as 1-to-1 pass-throughs that still need their upstream trace context preserved
    /// on the outlet (Merge, MergePreferred, Concat).
    ///
    /// Under "first-wins" fan-in semantics:
    ///   - The merged/passed-through output element's downstream stage span has the
    ///     FIRST contributing input element's <see cref="ActivityContext"/> as its
    ///     primary parent.
    ///   - Any additional inputs that contributed to the same output are attached
    ///     as <see cref="ActivityLink"/>s so trace consumers can jump between the
    ///     contributing traces.
    /// </summary>
    public class StreamsFanInSpec : AkkaSpec
    {
        private readonly ActorMaterializer _materializer;

        public StreamsFanInSpec(ITestOutputHelper output) : base(output)
        {
            _materializer = Sys.Materializer();
        }

        /// <summary>
        /// Drives a slow-stage pipeline that pins Batch's downstream pull so N subsequent
        /// offers pile into a single aggregate. Returns the producer trace ids in order.
        /// </summary>
        private async Task<(List<ActivityTraceId> producerTraceIds, StreamsActivityCollector collector)>
            RunBatchWeightedWithNTracedProducers(int n)
        {
            var collector = new StreamsActivityCollector();

            var startedProcessing = new TaskCompletionSource<bool>();
            var releaseProcessing = new TaskCompletionSource<bool>();
            var queue = Source.Queue<int>(64, OverflowStrategy.DropNew)
                .BatchWeighted(
                    max: 1000L,
                    costFunction: (int _) => 1L,
                    seed: (int i) => new List<int> { i },
                    aggregate: (List<int> acc, int i) => { acc.Add(i); return acc; })
                .SelectAsync(1, async batch =>
                {
                    startedProcessing.TrySetResult(true);
                    await releaseProcessing.Task;
                    return batch;
                })
                .ToMaterialized(Sink.Ignore<List<int>>(), Keep.Left)
                .Run(_materializer);

            using var producers = new ProducerActivityScope("ProducerTest");

            // Prime the pump — first element flushes immediately and pins SelectAsync busy.
            using (var primer = producers.Start("producer.offer.primer"))
            {
                (await queue.OfferAsync(0)).Should().Be(QueueOfferResult.Enqueued.Instance);
            }
            await startedProcessing.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var producerTraceIds = new List<ActivityTraceId>();
            for (int i = 1; i <= n; i++)
            {
                using var parent = producers.Start($"producer.offer.{i}");
                parent.Should().NotBeNull();
                producerTraceIds.Add(parent.TraceId);
                (await queue.OfferAsync(i)).Should().Be(QueueOfferResult.Enqueued.Instance);
            }

            await Task.Delay(300);
            releaseProcessing.SetResult(true);
            queue.Complete();

            await collector.WaitForLinkedSpanAsync();

            return (producerTraceIds, collector);
        }

        [Fact]
        public async Task BatchWeighted_should_link_all_input_traces_via_ActivityLinks_on_flushed_stage_span()
        {
            var (producerTraceIds, collector) = await RunBatchWeightedWithNTracedProducers(3);
            using var _ = collector;

            var stopped = new List<Activity>(collector.StoppedActivities);
            foreach (var a in stopped)
                Output.WriteLine($"[stream] {a.OperationName} trace={a.TraceId} parent={a.ParentSpanId} links={a.Links?.Count() ?? 0}");

            var flushed = stopped.FirstOrDefault(a => (a.Links?.Count() ?? 0) > 0);
            flushed.Should().NotBeNull("the flushed downstream span should carry fan-in ActivityLinks");
            flushed.TraceId.Should().Be(producerTraceIds[0],
                "primary parent of the flushed batch should be the FIRST input element's trace");

            var linkedTraceIds = flushed.Links.Select(l => l.Context.TraceId).ToList();
            linkedTraceIds.Should().Contain(producerTraceIds[1]);
            linkedTraceIds.Should().Contain(producerTraceIds[2]);
        }

        [Fact]
        public async Task BatchWeighted_fan_in_link_count_should_equal_N_minus_1_for_N_inputs()
        {
            var (producerTraceIds, collector) = await RunBatchWeightedWithNTracedProducers(7);
            using var _ = collector;

            var flushed = collector.StoppedActivities.FirstOrDefault(a => (a.Links?.Count() ?? 0) > 0);
            flushed.Should().NotBeNull();
            (flushed.Links?.Count() ?? 0).Should().Be(producerTraceIds.Count - 1,
                "under first-wins semantics the primary parent is producer #0 and the remaining 6 appear as links");
        }

        [Fact]
        public async Task GroupedWithin_should_link_all_input_traces_via_ActivityLinks_on_emitted_group()
        {
            // GroupedWithin flushes when either the element count reaches maxNumber OR the
            // interval elapses. Using a tight window + count=4 so that feeding exactly 4
            // traced elements triggers the count-driven flush deterministically.
            using var collector = new StreamsActivityCollector();

            var startedProcessing = new TaskCompletionSource<bool>();
            var releaseProcessing = new TaskCompletionSource<bool>();
            var queue = Source.Queue<int>(64, OverflowStrategy.DropNew)
                .GroupedWithin(4, TimeSpan.FromSeconds(30))
                .SelectAsync(1, async group =>
                {
                    startedProcessing.TrySetResult(true);
                    await releaseProcessing.Task;
                    return group;
                })
                .ToMaterialized(Sink.Ignore<IEnumerable<int>>(), Keep.Left)
                .Run(_materializer);

            using var producers = new ProducerActivityScope("ProducerTest");

            var producerTraceIds = new List<ActivityTraceId>();
            for (int i = 0; i < 4; i++)
            {
                using var parent = producers.Start($"producer.offer.{i}");
                producerTraceIds.Add(parent.TraceId);
                (await queue.OfferAsync(i)).Should().Be(QueueOfferResult.Enqueued.Instance);
            }

            await startedProcessing.Task.WaitAsync(TimeSpan.FromSeconds(5));
            releaseProcessing.SetResult(true);
            queue.Complete();

            await collector.WaitForLinkedSpanAsync();

            var stopped = new List<Activity>(collector.StoppedActivities);
            var flushed = stopped.FirstOrDefault(a => (a.Links?.Count() ?? 0) > 0);
            flushed.Should().NotBeNull(
                "GroupedWithin's flushed downstream stage span should carry fan-in ActivityLinks");
            flushed.TraceId.Should().Be(producerTraceIds[0]);
            (flushed.Links?.Count() ?? 0).Should().Be(3, "first-wins + 3 additional links = 4 inputs");
        }

        [Fact]
        public async Task Merge_should_preserve_upstream_trace_context_on_each_passed_through_element()
        {
            // Merge is 1-to-1 pass-through: each output element is exactly one input element.
            // The downstream stage span for an output should inherit the trace identity of
            // the originating input's upstream producer.
            using var collector = new StreamsActivityCollector();
            using var producers = new ProducerActivityScope("ProducerTest");

            // Build: (queueA, queueB) → Merge → Select → Sink.Ignore
            var runnable = Source.Queue<int>(8, OverflowStrategy.DropNew)
                .MergeMaterialized(
                    Source.Queue<int>(8, OverflowStrategy.DropNew),
                    Keep.Both)
                .Select(i => i * 2)
                .ToMaterialized(Sink.Ignore<int>(), Keep.Left)
                .Run(_materializer);

            var (queueA, queueB) = runnable;

            ActivityTraceId traceA;
            ActivityTraceId traceB;
            using (var pa = producers.Start("producer.A"))
            {
                traceA = pa.TraceId;
                (await queueA.OfferAsync(1)).Should().Be(QueueOfferResult.Enqueued.Instance);
            }
            using (var pb = producers.Start("producer.B"))
            {
                traceB = pb.TraceId;
                (await queueB.OfferAsync(2)).Should().Be(QueueOfferResult.Enqueued.Instance);
            }

            queueA.Complete();
            queueB.Complete();

            await collector.WaitForSpansAsync(atLeast: 2, timeoutSeconds: 5,
                predicate: snap => snap.Count(a => a.OperationName == "akka.stream.stage Select") >= 2);

            var stopped = new List<Activity>(collector.StoppedActivities);
            foreach (var a in stopped)
                Output.WriteLine($"[stream] {a.OperationName} trace={a.TraceId}");

            var selectSpans = stopped.Where(a => a.OperationName == "akka.stream.stage Select").ToList();
            selectSpans.Should().HaveCount(2, "one Select span per element that flowed through Merge");

            var traceIds = selectSpans.Select(s => s.TraceId).ToHashSet();
            traceIds.Should().Contain(traceA, "producer A's trace should reach the downstream Select stage");
            traceIds.Should().Contain(traceB, "producer B's trace should reach the downstream Select stage");
        }

        [Fact]
        public async Task Concat_should_preserve_upstream_trace_context_for_each_source_in_sequence()
        {
            // Concat drains source 0 completely, then source 1, etc. Each element should
            // carry its own source's trace context through to the downstream stage.
            using var collector = new StreamsActivityCollector();
            using var producers = new ProducerActivityScope("ProducerTest");

            var first = Source.Queue<int>(8, OverflowStrategy.DropNew);
            var second = Source.Queue<int>(8, OverflowStrategy.DropNew);

            var runnable = first
                .ConcatMaterialized(second, Keep.Both)
                .Select(i => i + 100)
                .ToMaterialized(Sink.Ignore<int>(), Keep.Left)
                .Run(_materializer);

            var (q1, q2) = runnable;

            ActivityTraceId trace1;
            ActivityTraceId trace2;
            using (var p1 = producers.Start("producer.first"))
            {
                trace1 = p1.TraceId;
                (await q1.OfferAsync(1)).Should().Be(QueueOfferResult.Enqueued.Instance);
            }
            q1.Complete();
            await Task.Delay(200); // let first source drain

            using (var p2 = producers.Start("producer.second"))
            {
                trace2 = p2.TraceId;
                (await q2.OfferAsync(2)).Should().Be(QueueOfferResult.Enqueued.Instance);
            }
            q2.Complete();

            await collector.WaitForSpansAsync(atLeast: 2, timeoutSeconds: 5,
                predicate: snap => snap.Count(a => a.OperationName == "akka.stream.stage Select") >= 2);

            var selectSpans = collector.StoppedActivities
                .Where(a => a.OperationName == "akka.stream.stage Select")
                .ToList();
            selectSpans.Should().HaveCount(2);

            var traceIds = selectSpans.Select(s => s.TraceId).ToHashSet();
            traceIds.Should().Contain(trace1, "first source's trace should appear on one Select span");
            traceIds.Should().Contain(trace2, "second source's trace should appear on the other Select span");
        }
    }
}
