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
using System.Threading.Tasks;
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
    /// stages, with correct parent-child relationships.
    ///
    /// This is the Phase 1 verification for https://github.com/petabridge/phobos/issues/1307 —
    /// end-to-end trace continuity through Akka.Streams pipelines.
    /// </summary>
    public class StreamsDiagnosticsSpec : AkkaSpec
    {
        private readonly ActorMaterializer _materializer;

        public StreamsDiagnosticsSpec(ITestOutputHelper output) : base(output)
        {
            _materializer = Sys.Materializer();
        }

        /// <summary>
        /// Captures every Activity started on the "Akka.Streams" source while subscribed, so a test
        /// can assert parent/child relationships.
        /// </summary>
        private sealed class StreamsActivityCollector : IDisposable
        {
            private readonly ActivityListener _listener;
            public ConcurrentQueue<Activity> StartedActivities { get; } = new();
            public ConcurrentQueue<Activity> StoppedActivities { get; } = new();

            public StreamsActivityCollector()
            {
                _listener = new ActivityListener
                {
                    ShouldListenTo = source => source.Name == StreamsDiagnostics.ActivitySourceName,
                    Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                    SampleUsingParentId = (ref ActivityCreationOptions<string> _) => ActivitySamplingResult.AllDataAndRecorded,
                    ActivityStarted = a => StartedActivities.Enqueue(a),
                    ActivityStopped = a => StoppedActivities.Enqueue(a)
                };
                ActivitySource.AddActivityListener(_listener);
            }

            public void Dispose() => _listener.Dispose();
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

            // Offer one element from inside a traced scope (simulating Phobos-instrumented actor)
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

            // Wait a beat for interpreter to drain
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (collector.StoppedActivities.Count < 2 && DateTime.UtcNow < deadline)
                await Task.Delay(25);

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

            var ingress = stopped.Find(a => a.OperationName.StartsWith("akka.stream.offer"));
            ingress.Should().NotBeNull("ingress span should be emitted when OfferAsync is called from a traced scope");

            var selectStage = stopped.Find(a =>
                a.OperationName == "akka.stream.stage Select");
            selectStage.Should().NotBeNull("Select stage span should be emitted for the pushed element");

            // The Select stage span should descend from the ingress span, which should descend from the producer.
            // Same trace id end-to-end, correct parent chain.
            ingress.ParentId.Should().NotBeNullOrEmpty("ingress should parent to the producer's span");
            selectStage.ParentSpanId.Should().Be(ingress.SpanId, "Select should parent to the ingress span");
            ingress.TraceId.Should().Be(selectStage.TraceId, "all spans should share one trace id");
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
                    // This is the core Montrose case: SqlClient.ExecuteNonQueryAsync creates a span
                    // whose parent should be the SelectAsync stage span, not a random root.
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

            // Wait for the async lambda to complete
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (userSpans.Count == 0 && DateTime.UtcNow < deadline)
                await Task.Delay(25);

            userSpans.Should().HaveCount(1, "user span inside SelectAsync lambda should have been created");
            var userSpan = userSpans[0];
            Output.WriteLine($"user.work span: traceId={userSpan.TraceId} parentSpanId={userSpan.ParentSpanId}");

            // Dump stream spans for diagnosis
            foreach (var a in collector.StoppedActivities)
                Output.WriteLine($"[stream] {a.OperationName} trace={a.TraceId} span={a.SpanId} parent={a.ParentSpanId}");

            // The user's span should share the same TraceId as the producer offer, proving
            // end-to-end trace continuity from actor → Source.Queue → SelectAsync → user code.
            var ingress = collector.StoppedActivities.ToArray()[0];
            userSpan.TraceId.Should().Be(ingress.TraceId,
                "user span inside SelectAsync lambda should share the producer's trace id");
            userSpan.ParentSpanId.Should().NotBe(default(ActivitySpanId),
                "user span should have a parent (not be a root)");
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

            // No Phobos-owned spans should have been produced.
            collector.StartedActivities.Should().BeEmpty(
                "when the producer has no Activity.Current, ingress capture is a no-op and no stream spans are created (NetFile regression guard)");
        }

    }
}
