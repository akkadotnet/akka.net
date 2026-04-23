//-----------------------------------------------------------------------
// <copyright file="StreamsDiagnosticsSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams.Stage;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
using Akka.Streams.TestKit;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Streams.Tests.Implementation
{
    /// <summary>
    /// Validates that per-element trace context captured at Source.Queue ingress flows through
    /// the GraphInterpreter and causes stage-scoped Activity spans to be emitted for downstream
    /// stages, with correct parent-child relationships — i.e. end-to-end OpenTelemetry trace
    /// continuity through Akka.Streams pipelines.
    /// </summary>
    public class StreamsDiagnosticsSpec : AkkaSpec
    {
        private readonly ActorMaterializer _materializer;

        public StreamsDiagnosticsSpec(ITestOutputHelper output) : base(output)
        {
            _materializer = Sys.Materializer();
        }

        [Fact]
        public async Task ProducerActivityContext_should_propagate_to_downstream_stage_spans()
        {
            using var collector = new StreamsActivityCollector();

            // Materialize: Source.Queue -> Select -> Sink.Seq
            var queue = Source.Queue<int>(16, OverflowStrategy.DropNew)
                .Select(i => i * 2)
                .ToMaterialized(Sink.Seq<int>(), Keep.Left)
                .Run(_materializer);

            // Offer one element from inside a traced scope (simulating an instrumented producer).
            // Use a DIFFERENT source so our parent isn't swallowed by our own listener.
            using var producerSource = new ActivitySource("ProducerTest");
            using var producerListener = new ActivityListener
            {
                ShouldListenTo = src => src.Name == "ProducerTest",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStarted = _ => { },
                ActivityStopped = _ => { }
            };
            ActivitySource.AddActivityListener(producerListener);

            using (var parent = producerSource.StartActivity("producer.offer", ActivityKind.Internal))
            {
                parent.Should().NotBeNull("producer trace must be live for this test to mean anything");
                var result = await queue.OfferAsync(42);
                result.Should().Be(QueueOfferResult.Enqueued.Instance);
            }

            queue.Complete();

            await collector.WaitForSpansAsync(atLeast: 4, timeoutSeconds: 3);

            // Diagnostic dump of every span we saw:
            var stopped = new List<Activity>(collector.StoppedActivities);
            foreach (var a in stopped)
            {
                var stageType = a.GetTagItem("stream.stage.type") as string ?? "<none>";
                Output.WriteLine($"[span] name='{a.OperationName}' traceId={a.TraceId} spanId={a.SpanId} parentId={a.ParentSpanId} stream.stage.type={stageType}");
            }

            // We expect at least:
            //   - 1 akka.stream.offer ingress span (from Source.Queue's Callback handler)
            //   - 1 akka.stream.stage span (from Select, created in ProcessPush)
            stopped.Should().HaveCountGreaterOrEqualTo(2,
                "Source.Queue should emit an ingress span and downstream stages should each emit a stage span");

            stopped.Should().Contain(a => a.OperationName.StartsWith("akka.stream.ingress"),
                "at least one ingress span should be emitted when OfferAsync is called from a traced scope");

            var selectStage = stopped.Find(a =>
                a.OperationName == "akka.stream.stage Select");
            selectStage.Should().NotBeNull("Select stage span should be emitted for the pushed element");

            // End-to-end continuity: all stream spans share a single trace id.
            var distinctTraceIds = stopped.Select(a => a.TraceId.ToString()).Distinct().ToList();
            distinctTraceIds.Should().HaveCount(1, "all stream spans should share one trace id");

            // The Select stage span must have a parent (it should not be a root).
            selectStage.ParentSpanId.Should().NotBe(default(ActivitySpanId),
                "Select stage span must parent to an upstream span");
        }

        [Fact]
        public async Task User_span_inside_SelectAsync_lambda_should_parent_to_stage_span()
        {
            using var collector = new StreamsActivityCollector();

            // A separate user-owned ActivitySource represents "user code inside the SelectAsync lambda"
            // (e.g. OpenTelemetry.Instrumentation.SqlClient creating a span when it sees Activity.Current).
            using var userSource = new ActivitySource("UserWork");
            var userSpans = new List<Activity>();
            using var userListener = new ActivityListener
            {
                ShouldListenTo = src => src.Name == "UserWork" || src.Name == "ProducerTest",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStarted = _ => { },
                ActivityStopped = a =>
                {
                    if (a.Source.Name == "UserWork") userSpans.Add(a);
                }
            };
            ActivitySource.AddActivityListener(userListener);

            var sink = Source.Queue<int>(16, OverflowStrategy.DropNew)
                .SelectAsync(1, async i =>
                {
                    // Simulate user code (or OpenTelemetry auto-instrumentation) inside the async lambda.
                    // Represents the canonical case where user work inside SelectAsync (e.g. a
                    // SqlClient / HttpClient / gRPC call) creates a span whose parent should be the
                    // SelectAsync stage span, not a random root.
                    using var userSpan = userSource.StartActivity("user.work", ActivityKind.Internal);
                    await Task.Yield();
                    return i * 2;
                })
                .ToMaterialized(Sink.Seq<int>(), Keep.Left)
                .Run(_materializer);

            using var producerSource = new ActivitySource("ProducerTest");
            using (var parent = producerSource.StartActivity("producer.offer", ActivityKind.Internal))
            {
                parent.Should().NotBeNull();
                (await sink.OfferAsync(42)).Should().Be(QueueOfferResult.Enqueued.Instance);
            }

            await collector.WaitForSpansAsync(atLeast: 3, timeoutSeconds: 3,
                predicate: _ => userSpans.Count > 0);

            userSpans.Should().HaveCount(1, "user span inside SelectAsync lambda should have been created");
            var userSpan = userSpans[0];
            Output.WriteLine($"user.work span: traceId={userSpan.TraceId} parentSpanId={userSpan.ParentSpanId}");

            // Dump stream spans for diagnosis
            foreach (var a in collector.StoppedActivities)
                Output.WriteLine($"[stream] {a.OperationName} trace={a.TraceId} span={a.SpanId} parent={a.ParentSpanId}");

            // The user's span should share the same TraceId as the stream spans, proving
            // end-to-end trace continuity from actor → Source.Queue → SelectAsync → user code.
            var streamSpansArr = collector.StoppedActivities.ToArray();
            streamSpansArr.Should().NotBeEmpty("stream spans should have been emitted");
            userSpan.TraceId.Should().Be(streamSpansArr[0].TraceId,
                "user span inside SelectAsync lambda should share the producer's trace id");
            userSpan.ParentSpanId.Should().NotBe(default(ActivitySpanId),
                "user span should have a parent (not be a root)");
        }

        [Fact]
        public async Task Multiple_offers_from_different_traced_scopes_should_preserve_distinct_traces()
        {
            // Validates that the generalized capture at InvokeCallbacks correctly attributes each
            // element to its OWN producer trace when offers happen from different traced scopes.
            // This is the multi-producer interleaving case that was the critical disqualifying
            // scenario for the earlier mailbox-level hypothesis.
            using var collector = new StreamsActivityCollector();

            var queue = Source.Queue<int>(16, OverflowStrategy.DropNew)
                .Select(i => i * 10)
                .ToMaterialized(Sink.Seq<int>(), Keep.Left)
                .Run(_materializer);

            using var producerSource = new ActivitySource("ProducerTest");
            using var producerListener = new ActivityListener
            {
                ShouldListenTo = src => src.Name == "ProducerTest",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStarted = _ => { },
                ActivityStopped = _ => { }
            };
            ActivitySource.AddActivityListener(producerListener);

            // First offer under traceA
            string traceAId;
            using (var parent = producerSource.StartActivity("producer.offerA", ActivityKind.Internal))
            {
                parent.Should().NotBeNull();
                traceAId = parent!.TraceId.ToString();
                (await queue.OfferAsync(1)).Should().Be(QueueOfferResult.Enqueued.Instance);
            }

            await collector.WaitForSpansAsync(atLeast: 2, timeoutSeconds: 3);

            var aSpans = collector.StoppedActivities.ToArray();
            foreach (var a in aSpans)
                a.TraceId.ToString().Should().Be(traceAId, "first offer's spans must all belong to traceA");

            // Second offer under traceB (separate trace)
            string traceBId;
            using (var parent = producerSource.StartActivity("producer.offerB", ActivityKind.Internal))
            {
                parent.Should().NotBeNull();
                traceBId = parent!.TraceId.ToString();
                (await queue.OfferAsync(2)).Should().Be(QueueOfferResult.Enqueued.Instance);
            }

            queue.Complete();

            var expectedTotal = aSpans.Length + 2;  // at least ingress + stage for offer B
            await collector.WaitForSpansAsync(atLeast: expectedTotal, timeoutSeconds: 3);

            traceAId.Should().NotBe(traceBId, "the two offers must have different trace ids");

            // Every captured stream span should belong to EITHER traceA OR traceB — never mixed.
            var allSpans = collector.StoppedActivities.ToArray();
            foreach (var a in allSpans)
            {
                var tid = a.TraceId.ToString();
                (tid == traceAId || tid == traceBId).Should().BeTrue(
                    $"span {a.OperationName} traceId {tid} should match either traceA or traceB");
            }

            // And each trace id should have at least one stream span.
            allSpans.Should().Contain(a => a.TraceId.ToString() == traceAId, "traceA should have stream spans");
            allSpans.Should().Contain(a => a.TraceId.ToString() == traceBId, "traceB should have stream spans");
        }

        [Fact]
        public async Task No_producer_context_should_produce_no_stream_spans()
        {
            using var collector = new StreamsActivityCollector();

            var sink = Source.Queue<int>(16, OverflowStrategy.DropNew)
                .Select(i => i + 1)
                .ToMaterialized(Sink.Seq<int>(), Keep.Left)
                .Run(_materializer);

            // Offer without any Activity.Current set — simulates a background / timer-driven caller.
            Activity.Current.Should().BeNull("precondition: no ambient trace");
            var result = await sink.OfferAsync(7);
            result.Should().Be(QueueOfferResult.Enqueued.Instance);

            sink.Complete();

            // Wait a beat to be sure nothing shows up asynchronously
            await Task.Delay(200);

            // No framework-owned spans should have been produced.
            collector.StartedActivities.Should().BeEmpty(
                "when the producer has no Activity.Current, ingress capture is a no-op and no " +
                "stream spans are created — this is the cardinality guard that keeps background " +
                "tick-driven streams invisible to tracing");
        }

    }
}
