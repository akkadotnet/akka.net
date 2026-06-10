//-----------------------------------------------------------------------
// <copyright file="Streams8241Spec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2025 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Streams.Tests.Implementation
{
    /// <summary>
    /// Regression tests for https://github.com/akkadotnet/akka.net/issues/8241
    ///
    /// When stream tracing is enabled (a listener is attached to the "Akka.Streams"
    /// ActivitySource), a traced element pushed across an actor/async boundary triggered a
    /// <see cref="NullReferenceException"/> inside <c>GraphInterpreter.ProcessPush</c>.
    ///
    /// The downstream boundary connection has a <c>null</c> <c>InOwner</c> (boundary
    /// connections are constructed with a null owner in <c>GraphAssembly</c> and
    /// <c>AttachDownstreamBoundary</c> only sets the <c>InHandler</c>). If the element that
    /// reaches it carries an armed SlotContext, ProcessPush called
    /// <c>StreamsDiagnostics.GetStageOperationName(connection.InOwner)</c> with a null stage,
    /// which dereferences <c>stage.GetType()</c> and threw — and because <c>ActiveStage</c>
    /// was also null, <c>ReportStageError</c>'s <c>throw e;</c> erased the origin stack.
    ///
    /// The author's single-shell synthetic graphs never reproduced this because none crossed
    /// an actor boundary — the only place a null-InOwner connection exists. The trigger
    /// requires the LAST in-shell stage to push to the boundary while an Activity is current,
    /// which is exactly what an actor-callback source (Akka.Streams.Kafka) does.
    /// </summary>
    public class Streams8241Spec : AkkaSpec
    {
        private readonly ActorMaterializer _materializer;

        public Streams8241Spec(ITestOutputHelper output) : base(output)
        {
            _materializer = Sys.Materializer();
        }

        /// <summary>
        /// A source that pushes its element from inside an active producer span, mimicking
        /// Akka.Streams.Kafka pushing from a consumer callback while a consume span is current.
        /// Pushing while the span is current arms the outgoing connection's SlotContext.
        /// </summary>
        private sealed class TracedSource : GraphStage<SourceShape<int>>
        {
            private readonly ActivitySource _producer;
            private readonly int _value;
            private readonly TaskCompletionSource<string> _traceId = new();

            public TracedSource(ActivitySource producer, int value)
            {
                _producer = producer;
                _value = value;
                Shape = new SourceShape<int>(Out);
            }

            public Outlet<int> Out { get; } = new("TracedSource.out");
            public override SourceShape<int> Shape { get; }

            /// <summary>The W3C trace id of the "consume" span the element was pushed under.</summary>
            public Task<string> TraceId => _traceId.Task;

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes)
                => new Logic(this);

            private sealed class Logic : OutGraphStageLogic
            {
                private readonly TracedSource _stage;
                private bool _pushed;

                public Logic(TracedSource stage) : base(stage.Shape)
                {
                    _stage = stage;
                    SetHandler(stage.Out, this);
                }

                public override void OnPull()
                {
                    if (_pushed)
                    {
                        CompleteStage();
                        return;
                    }

                    _pushed = true;
                    using var span = _stage._producer.StartActivity("consume", ActivityKind.Consumer);
                    _stage._traceId.TrySetResult(span?.TraceId.ToString());
                    Push(_stage.Out, _stage._value);
                }
            }
        }

        [Fact]
        public async Task Kafka_style_traced_source_pushing_across_async_boundary_should_not_throw_NRE()
        {
            using var collector = new StreamsActivityCollector();
            using var producers = new ProducerActivityScope("ProducerTest");

            // TracedSource pushes directly into the actor-output-boundary connection
            // (InOwner == null) while the "consume" span is current → SlotContext armed.
            var done = Source.FromGraph(new TracedSource(producers.Source, 41))
                .Async()
                .RunWith(Sink.Seq<int>(), _materializer);

            var finished = await Task.WhenAny(done, Task.Delay(TimeSpan.FromSeconds(5)));
            finished.Should().BeSameAs(done, "the stream should complete rather than hang or fault");
            (await done).Should().Equal(new List<int> { 41 });
        }

        [Fact]
        public async Task Traced_element_across_Async_boundary_with_stages_on_both_sides_should_not_throw_NRE()
        {
            // The idiomatic shape: a stage on the upstream side pushes to the actor-output
            // boundary while its own stage Activity is current. Before the fix this faulted
            // with the same NRE; the element must still flow through to the sink.
            //
            // NOTE: this asserts no-crash + element delivery only. Trace context does not yet
            // cross the actor boundary, so the downstream Select/Sink spans are not linked to
            // the producer trace — tracked as a follow-up in #8243.
            using var collector = new StreamsActivityCollector();
            using var producers = new ProducerActivityScope("ProducerTest");

            var done = Source.FromGraph(new TracedSource(producers.Source, 41))
                .Select(x => x + 1)   // upstream-shell stage; pushes to the boundary in its span
                .Async()              // actor boundary (null-InOwner connection)
                .Select(x => x * 2)   // downstream-shell stage
                .RunWith(Sink.Seq<int>(), _materializer);

            var finished = await Task.WhenAny(done, Task.Delay(TimeSpan.FromSeconds(5)));
            finished.Should().BeSameAs(done, "the stream should complete rather than hang or fault");
            (await done).Should().Equal(new List<int> { 84 });
        }

        [Fact]
        public async Task MergeHub_dynamic_producer_traced_element_should_flow_through_without_NRE()
        {
            // Dynamic materialization: a producer is materialized separately and attached to a
            // MergeHub consumer at runtime, with tracing enabled. This exercises the actor-callback
            // ingress path across the hub boundary — the traced element must flow end-to-end to the
            // sink without the #8241 NRE.
            //
            // We deliberately do NOT assert span/trace continuity through the hub: whether the
            // per-element trace context survives the hub's async wakeup is timing-dependent (the
            // hub drains its queue on whichever wakeup fires first, so the element can be delivered
            // under a null context and produce no downstream spans). That best-effort cross-boundary
            // behaviour is tracked with the broader continuity work in #8243; asserting it here is
            // inherently racy.
            using var collector = new StreamsActivityCollector();
            using var producers = new ProducerActivityScope("ProducerTest");

            var delivered = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            var sink = MergeHub.Source<int>(perProducerBufferSize: 4)
                .To(Sink.ForEach<int>(x => delivered.TrySetResult(x)))
                .Run(_materializer);

            var source = new TracedSource(producers.Source, 7);
            Source.FromGraph(source).RunWith(sink, _materializer);

            (await source.TraceId).Should().NotBeNullOrEmpty("the producer span must be live");

            var finished = await Task.WhenAny(delivered.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            finished.Should().BeSameAs(delivered.Task,
                "the traced element must flow through the dynamically materialized hub to the sink");
            (await delivered.Task).Should().Be(7);
        }
    }
}
