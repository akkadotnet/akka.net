//-----------------------------------------------------------------------
// <copyright file="StreamsRegressionSpec.cs" company="Akka.NET Project">
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
    /// Regression tests for cardinality and correctness guarantees that are not
    /// specific to any one fan-in/fan-out stage, but rather to the end-to-end rule
    /// that drives the whole design:
    ///
    ///   "Only trace elements that arrive with an existing parent trace context."
    ///
    /// In particular, a long-lived background stream (e.g. a Source.Tick feeding
    /// SelectAsync → Sink.Ignore) must emit ZERO "Akka.Streams" activities, because
    /// the tick timer fires on the interpreter thread with no Activity.Current to
    /// anchor any span to. A mixed graph where one leg is traced and another is not
    /// should show spans only for the traced leg's elements.
    /// </summary>
    public class StreamsRegressionSpec : AkkaSpec
    {
        private readonly ActorMaterializer _materializer;

        public StreamsRegressionSpec(ITestOutputHelper output) : base(output)
        {
            _materializer = Sys.Materializer();
        }

        [Fact]
        public async Task Background_Source_Tick_stream_with_no_producer_context_should_emit_zero_spans()
        {
            // This is the cardinality guard for background streams. A stream like this — a tick
            // timer feeding a Select and a SelectAsync with no external traced producer — could
            // otherwise produce one stage span per element per hour indefinitely, which would be
            // catastrophic for a long-lived process. The fix: only propagate when the producer
            // actually carried an Activity context. Source.Tick fires on the interpreter's own
            // scheduler with no producer scope, so every tick-driven element is invisible to
            // the "Akka.Streams" ActivitySource.
            using var collector = new StreamsActivityCollector();

            var cancel = Source.Tick(TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(20), "tick")
                .Select(x => x.ToUpperInvariant())
                .SelectAsync(1, async x => { await Task.Yield(); return x; })
                .ToMaterialized(Sink.Ignore<string>(), Keep.Left)
                .Run(_materializer);

            // Let the stream run for long enough to emit many elements if it were going to.
            await Task.Delay(500);

            cancel.Cancel();
            await Task.Delay(100);

            // The invariant: the entire stream ran with no producer trace context, so no
            // framework-owned "Akka.Streams" Activity should have been created.
            var emitted = collector.StoppedActivities.ToArray();
            emitted.Should().BeEmpty(
                "a background tick-driven stream with no producer trace context must produce " +
                "zero Akka.Streams spans — otherwise long-lived background streams would accumulate " +
                "unbounded spans against a random root trace");
        }

        [Fact]
        public async Task Mixed_traced_and_untraced_Merge_legs_should_only_emit_spans_for_traced_elements()
        {
            // Build: (traced queue, untraced tick source) → Merge → Select → Sink
            // Offer one traced element; the tick leg should produce zero spans.
            using var collector = new StreamsActivityCollector();
            using var producers = new ProducerActivityScope("ProducerTest");

            var materialized = Source.Queue<string>(8, OverflowStrategy.DropNew)
                .MergeMaterialized(
                    Source.Tick(TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(20), "tick"),
                    Keep.Both)
                .Select(s => s.ToUpperInvariant())
                .ToMaterialized(Sink.Ignore<string>(), Keep.Left)
                .Run(_materializer);
            var queue = materialized.Item1;
            var tickCancel = materialized.Item2;

            ActivityTraceId producerTraceId;
            using (var p = producers.Start("producer.offer"))
            {
                producerTraceId = p.TraceId;
                (await queue.OfferAsync("traced")).Should().Be(QueueOfferResult.Enqueued.Instance);
            }

            // Give the stream time to process both the traced element and several untraced ticks.
            await Task.Delay(200);
            queue.Complete();
            tickCancel.Cancel();

            // Every emitted span should belong to the traced producer's trace — NONE should
            // come from a tick element (those would be root spans with random trace ids).
            var stopped = collector.StoppedActivities.ToArray();
            foreach (var a in stopped)
                Output.WriteLine($"[stream] {a.OperationName} trace={a.TraceId}");

            stopped.Should().NotBeEmpty("the traced offer should have produced at least one stream span");
            stopped.Should().OnlyContain(
                a => a.TraceId == producerTraceId,
                "only the traced offer's elements should produce spans; tick-driven elements are invisible");
        }

        [Fact]
        public async Task GraphDSL_composed_subgraph_should_preserve_trace_context_end_to_end()
        {
            // A graph built via GraphDsl.Create with multiple internal stages should still
            // propagate the producer's trace through every stage in the composed sub-graph.
            using var collector = new StreamsActivityCollector();
            using var producers = new ProducerActivityScope("ProducerTest");

            var (queue, _) = Source.Queue<int>(8, OverflowStrategy.DropNew)
                .ToMaterialized(
                    Sink.FromGraph(GraphDsl.Create(
                        Sink.Ignore<int>(),
                        (builder, innerSink) =>
                        {
                            var normalize = builder.Add(Flow.Create<int>().Select(i => i + 1));
                            var multiply = builder.Add(Flow.Create<int>().Select(i => i * 2));
                            builder.From(normalize.Outlet).To(multiply.Inlet);
                            builder.From(multiply.Outlet).To(innerSink.Inlet);
                            return new SinkShape<int>(normalize.Inlet);
                        })),
                    Keep.Both)
                .Run(_materializer);

            ActivityTraceId producerTraceId;
            using (var p = producers.Start("producer.offer"))
            {
                producerTraceId = p.TraceId;
                (await queue.OfferAsync(1)).Should().Be(QueueOfferResult.Enqueued.Instance);
            }
            queue.Complete();

            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                if (collector.StoppedActivities.Count(a => a.OperationName == "akka.stream.stage Select") >= 2)
                    break;
                await Task.Delay(50);
            }

            var stopped = collector.StoppedActivities.ToArray();
            var selectSpans = stopped
                .Where(a => a.OperationName == "akka.stream.stage Select")
                .ToList();
            selectSpans.Should().HaveCount(2,
                "both Select stages inside the composed sub-graph should emit spans");
            selectSpans.Should().OnlyContain(s => s.TraceId == producerTraceId,
                "every stage in the composed sub-graph should share the producer's trace id");
        }
    }
}
