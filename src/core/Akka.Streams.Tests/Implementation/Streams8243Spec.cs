//-----------------------------------------------------------------------
// <copyright file="Streams8243Spec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

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
    /// Tests for https://github.com/akkadotnet/akka.net/issues/8243 — propagating per-element
    /// trace context across the publisher/subscriber actor boundary introduced by <c>.Async()</c>.
    ///
    /// Before this change, the <c>OnNext</c> boundary event carried no <see cref="ActivityContext"/>,
    /// so stages downstream of an <c>.Async()</c> boundary started a fresh (disconnected) trace.
    /// The fix captures the producer context at <c>ActorOutputBoundary</c>, carries it on the
    /// <c>OnNext</c> message, and re-arms the downstream connection's SlotContext at
    /// <c>BatchingActorInputBoundary</c> — so the trace stays contiguous across the boundary.
    /// </summary>
    public class Streams8243Spec : AkkaSpec
    {
        private readonly ActorMaterializer _materializer;

        public Streams8243Spec(ITestOutputHelper output) : base(output)
        {
            _materializer = Sys.Materializer();
        }

        [Fact]
        public async Task Trace_context_should_cross_an_Async_boundary_to_downstream_stages()
        {
            using var collector = new StreamsActivityCollector();
            using var producers = new ProducerActivityScope("ProducerTest");

            // TracedConsumeSource -> [.Async() actor boundary] -> Select -> Sink.Seq.
            // The downstream Select lives in a different shell than the source; before #8243 it
            // produced no span (context lost at the boundary), now it must share the producer trace.
            var (done, source) = MaterializeAsyncBoundary(producers, 41);

            var producerTraceId = await source.TraceId;
            producerTraceId.Should().NotBeNullOrEmpty();

            await collector.WaitForSpansAsync(
                atLeast: 1,
                timeoutSeconds: 10,
                predicate: snap => snap.Any(a =>
                    a.OperationName.Contains("SeqStage") && a.TraceId.ToString() == producerTraceId));

            var spans = collector.StoppedActivities.ToArray();
            foreach (var a in spans)
                Output.WriteLine($"[span] op='{a.OperationName}' trace={a.TraceId} parent={a.ParentSpanId}");

            // The downstream (post-boundary) Select and the terminal SeqStage must both be on the
            // producer trace — proving continuity across the .Async() actor boundary.
            spans.Should().Contain(a => a.OperationName.Contains("Select") && a.TraceId.ToString() == producerTraceId,
                "the post-boundary Select stage must share the producer trace id");
            spans.Should().Contain(a => a.OperationName.Contains("SeqStage") && a.TraceId.ToString() == producerTraceId,
                "the terminal sink stage must share the producer trace id — continuity across .Async()");

            (await done).Should().Equal(42);
        }

        [Fact]
        public async Task Trace_context_should_cross_an_Async_boundary_with_stages_on_both_sides()
        {
            using var collector = new StreamsActivityCollector();
            using var producers = new ProducerActivityScope("ProducerTest");

            // TracedConsumeSource -> Select -> [.Async()] -> Select -> Sink.Seq. A STAGE (the pre-boundary
            // Select) pushes to the boundary while its stage span is current; the fix carries that
            // context across so the post-boundary Select + sink stay on the producer trace.
            var source = new TracedConsumeSource(producers.Source, 10);
            var done = Source.FromGraph(source)
                .Select(i => i + 1)
                .Async()
                .Select(i => i * 2)
                .RunWith(Sink.Seq<int>(), _materializer);

            var producerTraceId = await source.TraceId;
            producerTraceId.Should().NotBeNullOrEmpty("the producer span must be live for the assertion to be meaningful");

            await collector.WaitForSpansAsync(
                atLeast: 1,
                timeoutSeconds: 10,
                predicate: snap => snap.Any(a =>
                    a.OperationName.Contains("SeqStage") && a.TraceId.ToString() == producerTraceId));

            var spans = collector.StoppedActivities.ToArray();
            foreach (var a in spans)
                Output.WriteLine($"[span] op='{a.OperationName}' trace={a.TraceId} parent={a.ParentSpanId}");

            // Both the pre- and post-boundary Select stages must be on the producer trace.
            spans.Count(a => a.OperationName.Contains("Select") && a.TraceId.ToString() == producerTraceId)
                .Should().BeGreaterOrEqualTo(2, "both the pre- and post-boundary Select stages must share the producer trace");
            spans.Should().Contain(a => a.OperationName.Contains("SeqStage") && a.TraceId.ToString() == producerTraceId,
                "the terminal sink (downstream of .Async()) must share the producer trace id");

            // Continuity is more than a shared TraceId: verify the actual parent CHAIN crosses the
            // boundary. SeqStage -> post-boundary Select -> pre-boundary Select, each parent link
            // resolving to a captured stage span on the producer trace. The pre-boundary Select's own
            // parent is the producer "consume" span (on the ProducerTest source, not captured here).
            var byId = spans.ToDictionary(a => a.SpanId);
            var seqStage = spans.Single(a => a.OperationName.Contains("SeqStage") && a.TraceId.ToString() == producerTraceId);

            byId.Should().ContainKey(seqStage.ParentSpanId,
                "the sink span's parent must be a captured stage span, not a disconnected root");
            var postBoundarySelect = byId[seqStage.ParentSpanId];
            postBoundarySelect.OperationName.Should().Contain("Select");

            byId.Should().ContainKey(postBoundarySelect.ParentSpanId,
                "the post-boundary Select's parent (the pre-boundary Select) must be captured — proving the chain crosses .Async() rather than just sharing a TraceId");
            var preBoundarySelect = byId[postBoundarySelect.ParentSpanId];
            preBoundarySelect.OperationName.Should().Contain("Select");
            preBoundarySelect.TraceId.ToString().Should().Be(producerTraceId);

            (await done).Should().Equal(22);
        }

        private (Task<System.Collections.Immutable.IImmutableList<int>> done, TracedConsumeSource source) MaterializeAsyncBoundary(
            ProducerActivityScope producers, int value)
        {
            var source = new TracedConsumeSource(producers.Source, value);
            var done = Source.FromGraph(source)
                .Async()
                .Select(x => x + 1)
                .RunWith(Sink.Seq<int>(), _materializer);
            return (done, source);
        }
    }
}
