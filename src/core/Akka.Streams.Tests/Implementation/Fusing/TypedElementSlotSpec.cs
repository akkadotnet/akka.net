//-----------------------------------------------------------------------
// <copyright file="TypedElementSlotSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Stage;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Streams.Tests.Implementation.Fusing
{
    public sealed class TypedElementSlotSpec : AkkaSpec
    {
        public TypedElementSlotSpec(ITestOutputHelper output) : base(output)
        {
        }

        [Theory(DisplayName = "Should_preserve_pending_values_after_terminal_callbacks_When_using_typed_connections")]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void Should_preserve_pending_values_after_terminal_callbacks_When_using_typed_connections(bool chasing, bool fail)
        {
            AssertPending(0, chasing, fail);
            AssertPending(default(ReadOnlySequence<byte>), chasing, fail);
            AssertPending(MultiSegmentSequence(), chasing, fail);
            AssertPending(new ReferenceValue(new object()), chasing, fail);
            AssertPending<int?>(42, chasing, fail);
        }

        [Theory(DisplayName = "Should_clear_payload_and_cached_box_When_a_pending_push_is_cancelled")]
        [InlineData(false)]
        [InlineData(true)]
        public void Should_clear_payload_and_cached_box_When_a_pending_push_is_cancelled(bool chasing)
        {
            using var graph = new ManualGraph<ReferenceValue, ReferenceValue>(Sys, chasing);
            graph.Request();
            graph.Source.Logic.Send(new ReferenceValue(new object()));
            graph.Run();
            _ = graph.Connection.Slot; // Exercise cleanup of the compatibility box as well.

            var cause = new InvalidOperationException("cancel");
            graph.Sink.Logic.CancelInput(cause);

            graph.Sink.Logic.Available.Should().BeFalse();
            graph.Sink.Logic.Invoking(logic => logic.Take()).Should().Throw<ArgumentException>();
            AssertCleared<ReferenceValue>(graph.Connection);
            graph.Run();
            graph.Source.Logic.Cancellation.Should().BeSameAs(cause);
            graph.Connection.Slot.Should().BeSameAs(GraphInterpreter.Empty.Instance);
        }

        [Fact(DisplayName = "Should_reuse_the_box_and_observe_mutations_When_the_public_slot_is_read")]
        public void Should_reuse_the_box_and_observe_mutations_When_the_public_slot_is_read()
        {
            using var graph = new ManualGraph<MutableValue, MutableValue>(Sys);
            graph.Request();
            graph.Source.Logic.Send(new MutableValue { Number = 1 });
            graph.Run();

            var boxed = graph.Connection.Slot;
            graph.Connection.Slot.Should().BeSameAs(boxed);
            typeof(MutableValue).GetField(nameof(MutableValue.Number))!.SetValue(boxed, 7);

            graph.Sink.Logic.Take().Number.Should().Be(7);
            AssertCleared<MutableValue>(graph.Connection);

            graph.Request();
            graph.Source.Logic.Send(new MutableValue { Number = 9 });
            graph.Run();
            graph.Connection.Slot.Should().NotBeSameAs(boxed);
            graph.Sink.Logic.Take().Number.Should().Be(9);
        }

        [Theory(DisplayName = "Should_preserve_setter_identity_and_deferred_cast_errors_When_the_public_slot_is_assigned")]
        [InlineData(false)]
        [InlineData(true)]
        public void Should_preserve_setter_identity_and_deferred_cast_errors_When_the_public_slot_is_assigned(bool completed)
        {
            AssertSetter(12, completed);
            AssertSetter("assigned reference", completed);
        }

        private void AssertSetter<T>(T value, bool completed)
        {
            using var graph = new ManualGraph<T, T>(Sys);
            graph.Request();
            graph.Source.Logic.Send(value);
            graph.Run();
            if (completed)
            {
                graph.Source.Logic.CompleteOutput();
                graph.Run();
            }

            object assigned = value!;
            graph.Connection.Slot = assigned;
            graph.Connection.Slot.Should().BeSameAs(assigned);
            if (graph.Connection.Storage is { } storage)
                storage.HasValue.Should().BeFalse();
            graph.Sink.Logic.Take().Should().Be(value);

            // Restore the arrived port state to isolate the existing public setter contract.
            graph.Connection.PortState = completed
                ? GraphInterpreter.InReady | GraphInterpreter.InClosed | GraphInterpreter.OutClosed
                : GraphInterpreter.InReady;
            var invalid = new object();
            graph.Connection.Slot = invalid;
            graph.Connection.Slot.Should().BeSameAs(invalid);
            graph.Sink.Logic.Invoking(logic => logic.Take()).Should().Throw<InvalidCastException>();
            if (completed)
                graph.Connection.Slot.Should().BeSameAs(invalid); // Slow path casts before clearing.
            else
                graph.Connection.Slot.Should().BeSameAs(GraphInterpreter.Empty.Instance);
        }

        [Fact(DisplayName = "Should_keep_failure_marker_and_exception_identity_When_grabbing_a_pending_failed_value")]
        public void Should_keep_failure_marker_and_exception_identity_When_grabbing_a_pending_failed_value()
        {
            using var graph = new ManualGraph<int, int>(Sys);
            graph.Request();
            graph.Source.Logic.Send(1);
            var error = new InvalidOperationException("failure");
            graph.Source.Logic.FailOutput(error);
            graph.Run();

            var marker = graph.Connection.Slot.Should().BeOfType<GraphInterpreter.Failed>().Subject;
            marker.Reason.Should().BeSameAs(error);
            marker.PreviousElement.Should().Be(1);
            graph.Connection.Slot.Should().BeSameAs(marker);
            graph.Sink.Logic.Take().Should().Be(1);
            var consumed = graph.Connection.Slot.Should().BeOfType<GraphInterpreter.Failed>().Subject;
            consumed.Reason.Should().BeSameAs(error);
            consumed.PreviousElement.Should().BeSameAs(GraphInterpreter.Empty.Instance);
            graph.Sink.Logic.Available.Should().BeFalse();
        }

        [Fact(DisplayName = "Should_preserve_raw_slot_lifetime_When_an_ungrabbed_value_is_dropped_by_pull")]
        public void Should_preserve_raw_slot_lifetime_When_an_ungrabbed_value_is_dropped_by_pull()
        {
            using var graph = new ManualGraph<ReferenceValue, ReferenceValue>(Sys);
            graph.Request();
            graph.Source.Logic.Send(new ReferenceValue(new object()));
            graph.Run();
            var dropped = graph.Connection.Slot;

            graph.Request();
            graph.Sink.Logic.Invoking(logic => logic.Take()).Should().Throw<ArgumentException>();
            // The original object Slot also retains the dropped value until replacement/cancel.
            graph.Connection.Slot.Should().BeSameAs(dropped);

            var next = new ReferenceValue(new object());
            graph.Source.Logic.Send(next);
            graph.Run();
            graph.Connection.Slot.Should().NotBeSameAs(dropped);
            graph.Sink.Logic.Take().Should().Be(next);
            AssertCleared<ReferenceValue>(graph.Connection);
        }

        [Theory(DisplayName = "Should_preserve_pending_grab_When_cancellation_follows_terminal_delivery")]
        [InlineData(false)]
        [InlineData(true)]
        public void Should_preserve_pending_grab_When_cancellation_follows_terminal_delivery(bool fail)
        {
            using var graph = new ManualGraph<ReadOnlySequence<byte>, ReadOnlySequence<byte>>(Sys);
            graph.Request();
            var value = MultiSegmentSequence();
            graph.Source.Logic.Send(value);
            if (fail)
                graph.Source.Logic.FailOutput(new InvalidOperationException("terminal"));
            else
                graph.Source.Logic.CompleteOutput();
            graph.Run();

            graph.Sink.Logic.CancelInput(new InvalidOperationException("already closed"));
            graph.Run();
            graph.Sink.Logic.Take().Should().Be(value);
            AssertCleared<ReadOnlySequence<byte>>(graph.Connection);
        }

        [Fact(DisplayName = "Should_preserve_marker_setter_behavior_When_public_slot_control_views_are_assigned")]
        public void Should_preserve_marker_setter_behavior_When_public_slot_control_views_are_assigned()
        {
            using var graph = new ManualGraph<int, int>(Sys);
            graph.Request();
            graph.Source.Logic.Send(1);
            graph.Run();
            graph.Connection.Slot = GraphInterpreter.Empty.Instance;
            graph.Sink.Logic.Available.Should().BeFalse();
            AssertCleared<int>(graph.Connection);

            var error = new InvalidOperationException("assigned failure");
            var invalid = new object();
            var failed = new GraphInterpreter.Failed(error, invalid);
            graph.Connection.PortState = GraphInterpreter.InReady | GraphInterpreter.InFailed | GraphInterpreter.OutClosed;
            graph.Connection.Slot = failed;
            graph.Connection.Slot.Should().BeSameAs(failed);
            graph.Sink.Logic.Invoking(logic => logic.Take()).Should().Throw<InvalidCastException>();
            var consumed = graph.Connection.Slot.Should().BeOfType<GraphInterpreter.Failed>().Subject;
            consumed.Reason.Should().BeSameAs(error);
            consumed.PreviousElement.Should().BeSameAs(GraphInterpreter.Empty.Instance);

            var cancelled = new GraphInterpreter.Cancelled(error);
            graph.Connection.PortState = GraphInterpreter.InReady | GraphInterpreter.InClosed;
            graph.Connection.Slot = cancelled;
            graph.Connection.Slot.Should().BeSameAs(cancelled);
            graph.Sink.Logic.Available.Should().BeFalse();
            graph.Sink.Logic.Invoking(logic => logic.Take()).Should().Throw<ArgumentException>();
        }

        [Fact(DisplayName = "Should_select_storage_from_actual_stage_ports_When_assembly_ports_are_recast")]
        public void Should_select_storage_from_actual_stage_ports_When_assembly_ports_are_recast()
        {
            using var graph = new ManualGraph<int, int>(Sys, recast: true);
            graph.Connection.Storage.Should().BeOfType<ElementSlot<int>>();
            graph.Request();
            graph.Source.Logic.Send(42);
            graph.Run();
            graph.Sink.Logic.Take().Should().Be(42);
        }

        [Theory(DisplayName = "Should_preserve_pending_trace_context_When_typed_delivery_completes_or_fails")]
        [InlineData(false)]
        [InlineData(true)]
        public void Should_preserve_pending_trace_context_When_typed_delivery_completes_or_fails(bool fail)
        {
            using var collector = new StreamsActivityCollector();
            using var producer = new ProducerActivityScope("typed-slot-producer");
            using var graph = new ManualGraph<ReadOnlySequence<byte>, ReadOnlySequence<byte>>(Sys);
            graph.Request();
            System.Diagnostics.ActivityContext context;
            using (var activity = producer.Start("push"))
            {
                activity.Should().NotBeNull();
                context = activity.Context;
                graph.Source.Logic.Send(MultiSegmentSequence());
            }
            if (fail)
                graph.Source.Logic.FailOutput(new InvalidOperationException("traced failure"));
            else
                graph.Source.Logic.CompleteOutput();
            graph.Run();

            graph.Connection.SlotContext.Should().Be(context);
            collector.StoppedActivities.Should().Contain(activity =>
                activity.TraceId == context.TraceId && activity.ParentSpanId == context.SpanId);
            graph.Sink.Logic.Take().Length.Should().Be(5);
            if (fail)
                graph.Connection.SlotContext.Should().Be(context); // Existing failure retry contract.
            else
                graph.Connection.SlotContext.Should().BeNull();
        }

        [Fact(DisplayName = "Should_keep_object_storage_When_ports_are_erased_reference_typed_or_custom")]
        public void Should_keep_object_storage_When_ports_are_erased_reference_typed_or_custom()
        {
            using var erased = new ManualGraph<int, object>(Sys);
            erased.Connection.Storage.Should().BeNull();
            erased.Request();
            erased.Source.Logic.Send(17);
            erased.Run();
            erased.Sink.Logic.Take().Should().Be(17);

            var sentinel = new object();
            using var references = new ManualGraph<object, object>(Sys);
            references.Connection.Storage.Should().BeNull();
            references.Request();
            references.Source.Logic.Send(sentinel);
            references.Run();
            references.Sink.Logic.Take().Should().BeSameAs(sentinel);

            new ForeignInlet().CreateElementSlot(new Outlet<int>("out")).Should().BeNull();
            new Inlet<int>("in").CreateElementSlot(new Outlet<long>("out")).Should().BeNull();
            new Inlet<int>("in").CreateElementSlot(new ForeignOutlet()).Should().BeNull();
        }

        [Fact(DisplayName = "Should_reject_null_values_and_allow_nullable_values_When_using_typed_connections")]
        public void Should_reject_null_values_and_allow_nullable_values_When_using_typed_connections()
        {
            using var nullable = new ManualGraph<int?, int?>(Sys);
            nullable.Request();
            nullable.Source.Logic.Invoking(logic => logic.Send(null)).Should().Throw<ArgumentNullException>()
                .Which.Should().BeSameAs(ReactiveStreamsCompliance.ElementMustNotBeNullException);
            nullable.Connection.HasTypedElement.Should().BeFalse();
            nullable.Source.Logic.Send(3);
            nullable.Run();
            nullable.Sink.Logic.Take().Should().Be(3);

            using var references = new ManualGraph<object?, object?>(Sys);
            references.Request();
            references.Source.Logic.Invoking(logic => logic.Send(null)).Should().Throw<ArgumentNullException>()
                .Which.Should().BeSameAs(ReactiveStreamsCompliance.ElementMustNotBeNullException);
            references.Sink.Logic.Available.Should().BeFalse();
        }

        [Fact(DisplayName = "Should_isolate_payloads_and_failure_When_materializing_one_graph_concurrently")]
        public async Task Should_isolate_payloads_and_failure_When_materializing_one_graph_concurrently()
        {
            var materializer = Sys.Materializer();
            var graph = Source.Queue<ReadOnlySequence<byte>>(4, OverflowStrategy.Backpressure)
                .Via(new PassThrough<ReadOnlySequence<byte>>())
                .ToMaterialized(Sink.Seq<ReadOnlySequence<byte>>(), Keep.Both);
            var (cancelledQueue, cancelledResult) = graph.Run(materializer);
            var (completedQueue, completedResult) = graph.Run(materializer);
            var first = new ReadOnlySequence<byte>(new byte[] { 1 });
            var second = MultiSegmentSequence();

            (await cancelledQueue.OfferAsync(first)).Should().Be(QueueOfferResult.Enqueued.Instance);
            (await completedQueue.OfferAsync(second)).Should().Be(QueueOfferResult.Enqueued.Instance);
            var error = new InvalidOperationException("only first materialization");
            cancelledQueue.Fail(error);
            completedQueue.Complete();

            Func<Task> cancelled = async () => await cancelledResult.WaitAsync(TimeSpan.FromSeconds(5));
            (await cancelled.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(error);
            var values = await completedResult.WaitAsync(TimeSpan.FromSeconds(5));
            values.Should().HaveCount(1);
            values.Single().ToArray().Should().Equal(second.ToArray());
            values.Single().Start.GetObject().Should().BeSameAs(second.Start.GetObject());
        }

        [Fact(DisplayName = "Should_isolate_downstream_cancellation_When_materializing_one_graph_concurrently")]
        public async Task Should_isolate_downstream_cancellation_When_materializing_one_graph_concurrently()
        {
            var materializer = Sys.Materializer();
            var graph = Source.Queue<ReadOnlySequence<byte>>(4, OverflowStrategy.Backpressure)
                .Via(new PassThrough<ReadOnlySequence<byte>>())
                .ViaMaterialized(KillSwitches.Single<ReadOnlySequence<byte>>(), Keep.Both)
                .ToMaterialized(Sink.Seq<ReadOnlySequence<byte>>(), Keep.Both);
            var ((firstQueue, firstSwitch), firstResult) = graph.Run(materializer);
            var ((secondQueue, _), secondResult) = graph.Run(materializer);
            var first = new ReadOnlySequence<byte>(new byte[] { 1 });
            var second = MultiSegmentSequence();

            (await firstQueue.OfferAsync(first)).Should().Be(QueueOfferResult.Enqueued.Instance);
            (await secondQueue.OfferAsync(second)).Should().Be(QueueOfferResult.Enqueued.Instance);
            firstSwitch.Shutdown(); // Cancels this materialization's upstream source.
            await firstQueue.WatchCompletionAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await firstResult.WaitAsync(TimeSpan.FromSeconds(5));
            (await secondQueue.OfferAsync(first)).Should().Be(QueueOfferResult.Enqueued.Instance);
            secondQueue.Complete();

            var values = await secondResult.WaitAsync(TimeSpan.FromSeconds(5));
            values.Select(value => value.Length).Should().Equal(5, 1);
            values.First().Start.GetObject().Should().BeSameAs(second.Start.GetObject());
        }

        [Fact(DisplayName = "Should_allow_widened_grab_When_pass_along_reads_a_typed_input_as_object")]
        public async Task Should_allow_widened_grab_When_pass_along_reads_a_typed_input_as_object()
        {
            var values = await Source.From(new[] { 1, 2, 3 })
                .Via(new WideningPassAlong())
                .RunWith(Sink.Seq<object>(), Sys.Materializer())
                .WaitAsync(TimeSpan.FromSeconds(5));
            values.Cast<int>().Should().Equal(1, 2, 3);
        }

        private void AssertPending<T>(T value, bool chasing, bool fail)
        {
            using var graph = new ManualGraph<T, T>(Sys, chasing);
            graph.Connection.Storage.Should().BeOfType<ElementSlot<T>>();
            var error = new InvalidOperationException("terminal");
            graph.Source.Logic.OnNextDemand(() =>
            {
                // Push and close inside interpreter execution to exercise chased events too.
                graph.Source.Logic.Send(value);
                if (fail)
                    graph.Source.Logic.FailOutput(error);
                else
                    graph.Source.Logic.CompleteOutput();
            });
            graph.Request();

            graph.Sink.Logic.Events.Should().Equal("next", fail ? "failure" : "complete");
            if (fail)
                graph.Sink.Logic.Failure.Should().BeSameAs(error);
            graph.Sink.Logic.Available.Should().BeTrue();
            graph.Sink.Logic.Take().Should().Be(value);
            graph.Sink.Logic.Invoking(logic => logic.Take()).Should().Throw<ArgumentException>();
            AssertCleared<T>(graph.Connection);
        }

        private static void AssertCleared<T>(GraphInterpreter.Connection connection)
        {
            var storage = connection.Storage.Should().BeOfType<ElementSlot<T>>().Subject;
            storage.HasValue.Should().BeFalse();
            storage.Value.Should().Be(default(T));
            storage.BoxedValue.Should().BeSameAs(GraphInterpreter.Empty.Instance);
        }

        private static ReadOnlySequence<byte> MultiSegmentSequence()
        {
            var first = new Segment(new byte[] { 1, 2 });
            var last = first.Append(new byte[] { 3, 4, 5 });
            return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
        }

        private readonly record struct ReferenceValue(object Reference);
        private struct MutableValue
        {
            public int Number;
        }

        private sealed class Segment : ReadOnlySequenceSegment<byte>
        {
            public Segment(byte[] bytes) => Memory = bytes;
            public Segment Append(byte[] bytes)
            {
                var next = new Segment(bytes) { RunningIndex = RunningIndex + Memory.Length };
                Next = next;
                return next;
            }
        }

        private sealed class ForeignInlet : Inlet
        {
            public ForeignInlet() : base("foreign.in")
            {
            }
            public override Inlet CarbonCopy() => new ForeignInlet();
        }

        private sealed class ForeignOutlet : Outlet
        {
            public ForeignOutlet() : base("foreign.out")
            {
            }
            public override Outlet CarbonCopy() => new ForeignOutlet();
        }

        private sealed class ManualGraph<TIn, TOut> : IDisposable
        {
            public ManualGraph(ActorSystem system, bool chasing = true, bool recast = false)
            {
                Source = new ManualSource<TIn>();
                Sink = new ManualSink<TOut>();
                var stages = new IGraphStageWithMaterializedValue<Shape, object>[] { Source, Sink };
                var inlet = recast ? new Inlet<object>("recast.in") { Id = 0 } : (Inlet)Sink.In;
                var outlet = recast ? new Outlet<object>("recast.out") { Id = 0 } : (Outlet)Source.Out;
                var assembly = new GraphAssembly(stages, new[] { Attributes.None, Attributes.None },
                    new[] { inlet }, new[] { 1 }, new[] { outlet }, new[] { 0 });
                var (connections, logics) = assembly.Materialize(Attributes.None,
                    stages.Select(stage => stage.Module).ToArray(), new Dictionary<IModule, object>(), _ => { });
                Interpreter = new GraphInterpreter(assembly, NoMaterializer.Instance,
                    Logging.GetLogger(system, "typed-element-slot"), logics, connections,
                    (_, _, _, _) => { }, !chasing, null!);
                Interpreter.Init(null);
            }

            public ManualSource<TIn> Source { get; }
            public ManualSink<TOut> Sink { get; }
            public GraphInterpreter Interpreter { get; }
            public GraphInterpreter.Connection Connection => Interpreter.Connections[0];
            public void Run() => Interpreter.Execute(int.MaxValue);
            public void Request()
            {
                Sink.Logic.Request();
                Run();
            }
            public void Dispose() => Interpreter.Finish();
        }

        private sealed class ManualSource<T> : GraphStage<SourceShape<T>>
        {
            public ManualSource() => Shape = new SourceShape<T>(Out);
            public Outlet<T> Out { get; } = new("manual.out");
            public override SourceShape<T> Shape { get; }
            public SourceLogic Logic { get; private set; } = null!;
            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => Logic = new SourceLogic(this);

            public sealed class SourceLogic : OutGraphStageLogic
            {
                private readonly ManualSource<T> _stage;
                private Action? _onNextDemand;

                public SourceLogic(ManualSource<T> stage) : base(stage.Shape)
                {
                    _stage = stage;
                    SetHandler(stage.Out, this);
                }
                public Exception? Cancellation { get; private set; }
                public override void OnPull()
                {
                    var action = _onNextDemand;
                    _onNextDemand = null;
                    action?.Invoke();
                }
                public void OnNextDemand(Action action) => _onNextDemand = action;
                public override void OnDownstreamFinish(Exception cause) => Cancellation = cause;
                public void Send(T value) => Push(_stage.Out, value);
                public void CompleteOutput() => Complete(_stage.Out);
                public void FailOutput(Exception error) => Fail(_stage.Out, error);
            }
        }

        private sealed class ManualSink<T> : GraphStage<SinkShape<T>>
        {
            public ManualSink() => Shape = new SinkShape<T>(In);
            public Inlet<T> In { get; } = new("manual.in");
            public override SinkShape<T> Shape { get; }
            public SinkLogic Logic { get; private set; } = null!;
            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => Logic = new SinkLogic(this);

            public sealed class SinkLogic : InGraphStageLogic
            {
                private readonly ManualSink<T> _stage;
                public SinkLogic(ManualSink<T> stage) : base(stage.Shape)
                {
                    _stage = stage;
                    SetHandler(stage.In, this);
                }
                public List<string> Events { get; } = new();
                public Exception? Failure { get; private set; }
                public bool Available => IsAvailable(_stage.In);
                public override void OnPush() => Events.Add("next");
                public override void OnUpstreamFinish() => Events.Add("complete");
                public override void OnUpstreamFailure(Exception error)
                {
                    Failure = error;
                    Events.Add("failure");
                }
                public void Request() => Pull(_stage.In);
                public T Take() => Grab(_stage.In);
                public void CancelInput(Exception cause) => Cancel(_stage.In, cause);
            }
        }

        private sealed class WideningPassAlong : GraphStage<FlowShape<int, object>>
        {
            private readonly Inlet<int> _in = new("widen.in");
            private readonly Outlet<object> _out = new("widen.out");

            public WideningPassAlong() => Shape = new FlowShape<int, object>(_in, _out);

            public override FlowShape<int, object> Shape { get; }

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

            private sealed class Logic : GraphStageLogic
            {
                public Logic(WideningPassAlong stage) : base(stage.Shape)
                {
                    SetHandler(stage._out, onPull: () =>
                    {
                        // PassAlong also pulls after emission; do not issue a second demand.
                        if (!HasBeenPulled(stage._in))
                            TryPull(stage._in);
                    });
                    PassAlong(stage._in, stage._out, doPull: false);
                }
            }
        }

        private sealed class PassThrough<T> : GraphStage<FlowShape<T, T>>
        {
            private readonly Inlet<T> _in = new("isolation.in");
            private readonly Outlet<T> _out = new("isolation.out");
            public PassThrough() => Shape = new FlowShape<T, T>(_in, _out);
            public override FlowShape<T, T> Shape { get; }
            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
            private sealed class Logic : InAndOutGraphStageLogic
            {
                private readonly PassThrough<T> _stage;
                public Logic(PassThrough<T> stage) : base(stage.Shape)
                {
                    _stage = stage;
                    SetHandlers(stage._in, stage._out, this);
                }
                public override void OnPush() => Push(_stage._out, Grab(_stage._in));
                public override void OnPull() => Pull(_stage._in);
            }
        }
    }
}
