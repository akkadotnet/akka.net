//-----------------------------------------------------------------------
// <copyright file="StreamsFanOutSpec.cs" company="Akka.NET Project">
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
    /// Validates trace-context propagation through Akka.Streams fan-out stages.
    ///
    /// Fan-out stages (Broadcast, Balance) do not need their own fan-in linking
    /// logic — each output element from a fan-out corresponds to exactly one input
    /// element, and the existing per-element SlotContext carry through ProcessPush
    /// should propagate the upstream trace context to every downstream branch.
    /// These tests are validation-only: if the per-element context-carry mechanism
    /// is correct, they pass without any stage-specific changes.
    /// </summary>
    public class StreamsFanOutSpec : AkkaSpec
    {
        private readonly ActorMaterializer _materializer;

        public StreamsFanOutSpec(ITestOutputHelper output) : base(output)
        {
            _materializer = Sys.Materializer();
        }

        [Fact]
        public async Task Broadcast_should_propagate_upstream_trace_to_every_downstream_branch()
        {
            // Build: Source.Queue → Broadcast(2) → { Select "left", Select "right" } → two Sinks.
            // One traced offer should produce stage spans on BOTH branches, all sharing the
            // same TraceId as the producer.
            using var collector = new StreamsActivityCollector();
            using var producers = new ProducerActivityScope("ProducerTest");

            var (queue, _) = Source.Queue<int>(8, OverflowStrategy.DropNew)
                .ToMaterialized(
                    Sink.FromGraph(GraphDsl.Create(
                        Sink.Ignore<int>(),
                        Sink.Ignore<int>(),
                        Keep.Both,
                        (builder, leftSink, rightSink) =>
                        {
                            var broadcast = builder.Add(new Broadcast<int>(2));
                            builder.From(broadcast.Out(0)).Via(Flow.Create<int>().Select(i => i + 10)).To(leftSink.Inlet);
                            builder.From(broadcast.Out(1)).Via(Flow.Create<int>().Select(i => i + 20)).To(rightSink.Inlet);
                            return new SinkShape<int>(broadcast.In);
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
                var selectCount = collector.StoppedActivities
                    .Count(a => a.OperationName == "akka.stream.stage Select");
                if (selectCount >= 2)
                    break;
                await Task.Delay(50);
            }

            var stopped = new List<Activity>(collector.StoppedActivities);
            foreach (var a in stopped)
                Output.WriteLine($"[stream] {a.OperationName} trace={a.TraceId}");

            var selectSpans = stopped
                .Where(a => a.OperationName == "akka.stream.stage Select")
                .ToList();
            selectSpans.Should().HaveCountGreaterOrEqualTo(2,
                "both Broadcast branches should run their Select stage spans");
            selectSpans.Should().OnlyContain(s => s.TraceId == producerTraceId,
                "every downstream branch should share the producer's trace id");
        }

        [Fact]
        public async Task Balance_should_propagate_upstream_trace_to_whichever_branch_consumes_each_element()
        {
            // Balance distributes elements round-robin (or to whichever branch is ready).
            // Each consumed element should arrive at its chosen branch's downstream stage
            // span with the producer's trace id intact.
            using var collector = new StreamsActivityCollector();
            using var producers = new ProducerActivityScope("ProducerTest");

            var (queue, _) = Source.Queue<int>(8, OverflowStrategy.DropNew)
                .ToMaterialized(
                    Sink.FromGraph(GraphDsl.Create(
                        Sink.Ignore<int>(),
                        Sink.Ignore<int>(),
                        Keep.Both,
                        (builder, leftSink, rightSink) =>
                        {
                            var balance = builder.Add(new Balance<int>(2));
                            builder.From(balance.Out(0)).Via(Flow.Create<int>().Select(i => i + 10)).To(leftSink.Inlet);
                            builder.From(balance.Out(1)).Via(Flow.Create<int>().Select(i => i + 20)).To(rightSink.Inlet);
                            return new SinkShape<int>(balance.In);
                        })),
                    Keep.Both)
                .Run(_materializer);

            // Offer two elements from the same traced scope, so both branches see something.
            ActivityTraceId producerTraceId;
            using (var p = producers.Start("producer.offer"))
            {
                producerTraceId = p.TraceId;
                (await queue.OfferAsync(1)).Should().Be(QueueOfferResult.Enqueued.Instance);
                (await queue.OfferAsync(2)).Should().Be(QueueOfferResult.Enqueued.Instance);
            }
            queue.Complete();

            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                var selectCount = collector.StoppedActivities
                    .Count(a => a.OperationName == "akka.stream.stage Select");
                if (selectCount >= 2)
                    break;
                await Task.Delay(50);
            }

            var stopped = new List<Activity>(collector.StoppedActivities);
            var selectSpans = stopped
                .Where(a => a.OperationName == "akka.stream.stage Select")
                .ToList();
            selectSpans.Should().HaveCountGreaterOrEqualTo(2,
                "both elements should have reached a downstream Select stage");
            selectSpans.Should().OnlyContain(s => s.TraceId == producerTraceId,
                "every branch should share the producer's trace id regardless of which one got the element");
        }
    }
}
