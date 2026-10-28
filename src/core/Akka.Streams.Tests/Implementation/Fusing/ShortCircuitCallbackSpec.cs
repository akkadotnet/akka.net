//-----------------------------------------------------------------------
// <copyright file="ShortCircuitCallbackSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2026 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Stage;
using Akka.Streams.TestKit;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Streams.Tests.Implementation.Fusing;

public sealed class ShortCircuitCallbackSpec : AkkaSpec
{
    public ShortCircuitCallbackSpec(ITestOutputHelper output) : base(output) { }

    [Fact(DisplayName = "Should_UseSuppliedCallback_When_PublicShellInitAdapterQueuesLocalInput")]
    public void Should_use_supplied_callback_when_public_shell_init_adapter_queues_local_input()
    {
        using var materializer = ActorMaterializer.Create(Sys);
        var extendedMaterializer = (ExtendedActorMaterializer)materializer;
        var stage = new AdapterStage();
        var assembly = new GraphAssembly(
            new IGraphStageWithMaterializedValue<Shape, object>[] { stage }, new[] { Attributes.None },
            Array.Empty<Inlet>(), Array.Empty<int>(), Array.Empty<Outlet>(), Array.Empty<int>());
        var (connections, logics) = assembly.Materialize(Attributes.None,
            assembly.Stages.Select(value => value.Module).ToArray(), new Dictionary<IModule, object>(), _ => { });
        var shell = new GraphInterpreterShell(assembly, connections, logics, stage.Shape,
            ActorMaterializerSettings.Create(Sys), extendedMaterializer);
        var probe = CreateTestProbe();
        var queued = new Queue<object>();
        var delivered = false;
        try
        {
            shell.Init(probe.Ref, new SubFusingActorMaterializerImpl(extendedMaterializer, _ => probe.Ref), queued.Enqueue, 1000);
            shell.Receive(new ActorGraphInterpreter.AsyncInput(shell, logics[0], 0, GraphStageLogic.NoPromise,
                _ => shell.Interpreter.OnAsyncInput(logics[0], 42, GraphStageLogic.NoPromise,
                    value => delivered = (int)value == 42)), 1000);

            delivered.Should().BeFalse("a local callback must be queued rather than invoked recursively");
            queued.Should().ContainSingle();
            var input = queued.Dequeue().Should().BeOfType<ActorGraphInterpreter.AsyncInput>().Subject;
            input.Shell.Should().BeSameAs(shell);
            input.Event.Should().Be(42);
            shell.Receive(input, 1000);
            delivered.Should().BeTrue();
        }
        finally
        {
            shell.Interpreter.Finish();
        }
    }

    [Fact(DisplayName = "Should_UseSuppliedCallback_When_PublicShellInitAdapterQueuesStageActorInput")]
    public void Should_use_supplied_callback_when_public_shell_init_adapter_queues_stage_actor_input()
    {
        using var materializer = ActorMaterializer.Create(Sys);
        var extendedMaterializer = (ExtendedActorMaterializer)materializer;
        var stage = new AdapterStage();
        var assembly = new GraphAssembly(
            new IGraphStageWithMaterializedValue<Shape, object>[] { stage }, new[] { Attributes.None },
            Array.Empty<Inlet>(), Array.Empty<int>(), Array.Empty<Outlet>(), Array.Empty<int>());
        var (connections, logics) = assembly.Materialize(Attributes.None,
            assembly.Stages.Select(value => value.Module).ToArray(), new Dictionary<IModule, object>(), _ => { });
        var shell = new GraphInterpreterShell(assembly, connections, logics, stage.Shape,
            ActorMaterializerSettings.Create(Sys), extendedMaterializer);
        var probe = CreateTestProbe();
        var queued = new Queue<object>();
        var payload = new object();
        object? received = null;
        IActorRef? sender = null;
        var previousActivity = Activity.Current;
        try
        {
            Activity.Current = null;
            shell.Init(probe.Ref, new SubFusingActorMaterializerImpl(extendedMaterializer, _ => probe.Ref), queued.Enqueue, 1000);
            shell.Receive(new ActorGraphInterpreter.AsyncInput(shell, logics[0], 0, GraphStageLogic.NoPromise,
                _ => ((AdapterStage.Logic)logics[0]).CreateActor(pair =>
                {
                    sender = pair.Item1;
                    received = pair.Item2;
                }).Tell(payload, TestActor)), 1000);

            received.Should().BeNull();
            queued.Should().ContainSingle();
            var input = queued.Dequeue().Should().BeOfType<ActorGraphInterpreter.AsyncInput>().Subject;
            shell.Receive(input, 1000);
            received.Should().BeSameAs(payload);
            sender.Should().BeSameAs(TestActor);
        }
        finally
        {
            Activity.Current = previousActivity;
            shell.Interpreter.Finish();
        }
    }

    private sealed class AdapterStage : GraphStage<ClosedShape>
    {
        public override ClosedShape Shape => ClosedShape.Instance;
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic();

        internal sealed class Logic : GraphStageLogic
        {
            public Logic() : base(ClosedShape.Instance) { }
            public override void PreStart() => SetKeepGoing(true);
            internal IActorRef CreateActor(Action<(IActorRef, object)> handler) => GetStageActor(handler.Invoke).Ref;
        }
    }

    [Theory(DisplayName = "Should_ResumeAndDeliverEveryElement_When_ShellOrActorQuotaIsReached")]
    [InlineData(1)]
    [InlineData(1000)]
    public async Task Should_resume_and_deliver_every_element_when_shell_or_actor_quota_is_reached(int limit)
    {
        using var materializer = ActorMaterializer.Create(Sys,
            ActorMaterializerSettings.Create(Sys).WithInputBuffer(1, 1)
                .WithSyncProcessingLimit(limit).WithFuzzingMode(false));
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            var expected = Enumerable.Range(1, 4096).ToArray();
            var result = await Source.From(Enumerable.Range(0, expected.Length))
                .Select(static value => value + 1)
                .RunWith(Sink.Seq<int>(), materializer).WaitAsync(TimeSpan.FromSeconds(3));

            result.Should().Equal(expected);
        }, materializer);
    }

    [Theory(DisplayName = "Should_QueueCallbacksInOrderAndCompleteFeedback_When_ActorEventLimitIsReached")]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(1000)]
    public async Task Should_queue_callbacks_in_order_and_complete_feedback_when_actor_event_limit_is_reached(int limit)
    {
        using var materializer = ActorMaterializer.Create(Sys,
            ActorMaterializerSettings.Create(Sys).WithSyncProcessingLimit(limit).WithFuzzingMode(false));
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            const int count = 64;
            var (control, result) = Source.Single(count)
                .ViaMaterialized(new CallbackBurstStage(), Keep.Right)
                .ToMaterialized(Sink.First<int>(), Keep.Both).Run(materializer);

            (await result.WaitAsync(TimeSpan.FromSeconds(3))).Should().Be(count);
            await Task.WhenAll(control.Feedback).WaitAsync(TimeSpan.FromSeconds(3));
            control.Events.Should().Equal(new[] { "push-enter", "push-exit" }
                .Concat(Enumerable.Range(0, count).Select(value => $"callback-{value}")));
            control.Feedback.Should().HaveCount(count);
            foreach (var feedback in control.Feedback)
                (await feedback).Should().Be(Done.Instance);
            await control.Stopped.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Func<Task> detached = async () => await control.Callback.InvokeWithFeedback(99);
            await detached.Should().ThrowAsync<StreamDetachedException>();
        }, materializer);
    }

    [Fact(DisplayName = "Should_FailQueuedFeedbackAndStop_When_ShortCircuitHandlerThrows")]
    public async Task Should_fail_queued_feedback_and_stop_when_short_circuit_handler_throws()
    {
        using var materializer = ActorMaterializer.Create(Sys,
            ActorMaterializerSettings.Create(Sys).WithSyncProcessingLimit(1).WithFuzzingMode(false));
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            var failure = new InvalidOperationException("short-circuit callback failure");
            var (control, result) = Source.Single(64)
                .ViaMaterialized(new CallbackBurstStage(failure), Keep.Right)
                .ToMaterialized(Sink.First<int>(), Keep.Both).Run(materializer);

            Func<Task> stream = async () => await result.WaitAsync(TimeSpan.FromSeconds(3));
            (await stream.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(failure);
            await control.Stopped.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Func<Task> failed = async () => await control.Feedback[17];
            (await failed.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(failure);
            foreach (var pending in control.Feedback.Skip(18))
            {
                Func<Task> detached = async () => await pending.WaitAsync(TimeSpan.FromSeconds(3));
                await detached.Should().ThrowAsync<StreamDetachedException>();
            }
            control.Events.Should().Equal(new[] { "push-enter", "push-exit" }
                .Concat(Enumerable.Range(0, 18).Select(value => $"callback-{value}")));
        }, materializer);
    }

    [Fact(DisplayName = "Should_RestoreProducerContext_When_TracedCallbackIsQueuedOnItsOwnInterpreter")]
    public async Task Should_restore_producer_context_when_traced_callback_is_queued_on_its_own_interpreter()
    {
        using var collector = new StreamsActivityCollector();
        using var producer = new ProducerActivityScope("ShortCircuitCallbackProducer");
        using var materializer = ActorMaterializer.Create(Sys,
            ActorMaterializerSettings.Create(Sys).WithSyncProcessingLimit(1).WithFuzzingMode(false));
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            var (control, result) = Source.Single(4)
                .ViaMaterialized(new CallbackBurstStage(producer: producer), Keep.Right)
                .ToMaterialized(Sink.First<int>(), Keep.Both).Run(materializer);

            (await result.WaitAsync(TimeSpan.FromSeconds(3))).Should().Be(4);
            await Task.WhenAll(control.Feedback).WaitAsync(TimeSpan.FromSeconds(3));
            control.ProducerContext.Should().NotBeNull();
            control.Contexts.Should().HaveCount(4);
            foreach (var context in control.Contexts)
            {
                context.Should().NotBeNull();
                context!.Value.TraceId.Should().Be(control.ProducerContext!.Value.TraceId);
            }
            collector.StoppedActivities.Should().Contain(activity =>
                activity.OperationName.StartsWith("akka.stream.ingress", StringComparison.Ordinal)
                && activity.ParentSpanId == control.ProducerContext!.Value.SpanId);
        }, materializer);
    }

    [Fact(DisplayName = "Should_ScheduleOnTargetActor_When_CallbackIsInvokedFromAnotherInterpreter")]
    public async Task Should_schedule_on_target_actor_when_callback_is_invoked_from_another_interpreter()
    {
        using var materializer = ActorMaterializer.Create(Sys);
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            var probe = CreateTestProbe();
            var ((input, callbacks), completion) = Source.Maybe<int>()
                .ViaMaterialized(new AsyncCallbackSpec.AsyncCallbackGraphStage(probe.Ref), Keep.Both)
                .ToMaterialized(Sink.Ignore<int>(), Keep.Both).Run(materializer);
            await probe.ExpectMsgAsync<AsyncCallbackSpec.Started>();

            var feedback = await Source.Single(42)
                .Select(value => callbacks.CallbackAsync.InvokeWithFeedback(value))
                .RunWith(Sink.First<Task<Done>>(), materializer).WaitAsync(TimeSpan.FromSeconds(3));

            (await feedback.WaitAsync(TimeSpan.FromSeconds(3))).Should().Be(Done.Instance);
            await probe.ExpectMsgAsync(42);
            input.TrySetResult(0);
            await completion.WaitAsync(TimeSpan.FromSeconds(3));
        }, materializer);
    }

    [Theory(DisplayName = "Should_PreserveBufferedCallbackBeforePreStartSelfSend_When_StageActorStarts")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Should_preserve_buffered_callback_before_prestart_self_send_when_stage_actor_starts(bool subfused)
    {
        using var materializer = ActorMaterializer.Create(Sys,
            ActorMaterializerSettings.Create(Sys).WithSyncProcessingLimit(1).WithFuzzingMode(false));
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            var control = new PreStartControl();
            Task<int> StartInner(IMaterializer innerMaterializer) => Source.Maybe<int>()
                .Via(new PreStartSelfSendStage(control)).RunWith(Sink.First<int>(), innerMaterializer);
            var result = subfused
                ? Source.Single(0).Select(_ =>
                {
                    control.OuterActor = GraphInterpreter.Current.Context;
                    return StartInner(GraphInterpreter.Current.SubFusingMaterializer);
                }).SelectAsync(1, static task => task).RunWith(Sink.First<int>(), materializer)
                : StartInner(materializer);
            (await result.WaitAsync(TimeSpan.FromSeconds(3))).Should().Be(2);
            control.Events.Should().Equal("prestart-enter", "prestart-exit", "buffered-callback", "self-message");
            control.HandlerActors.Should().HaveCount(2);
            if (subfused)
            {
                control.OuterActor.Should().NotBeNull();
                foreach (var actor in control.HandlerActors)
                    actor.Should().BeSameAs(control.OuterActor);
            }
        }, materializer);
    }

    private sealed class PreStartControl
    {
        public List<string> Events { get; } = new();
        public List<object> HandlerActors { get; } = new();
        public object? OuterActor { get; set; }
    }

    private sealed class PreStartSelfSendStage : GraphStage<FlowShape<int, int>>
    {
        private readonly PreStartControl _control;
        private readonly Inlet<int> _in = new("prestart-self-send.in");
        private readonly Outlet<int> _out = new("prestart-self-send.out");
        public PreStartSelfSendStage(PreStartControl control)
        {
            _control = control;
            Shape = new FlowShape<int, int>(_in, _out);
        }
        public override FlowShape<int, int> Shape { get; }
        protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);

        private sealed class Logic : InAndOutGraphStageLogic
        {
            private readonly PreStartSelfSendStage _stage;
            private int _delivered;
            public Logic(PreStartSelfSendStage stage) : base(stage.Shape)
            {
                _stage = stage;
                SetHandlers(stage._in, stage._out, this);
                // This callback is still Pending because Interpreter has not been assigned.
                GetAsyncCallback<int>(_ => Delivered("buffered-callback"))(0);
            }
            public override void PreStart()
            {
                _stage._control.Events.Add("prestart-enter");
                GetStageActor(_ => Delivered("self-message")).Ref.Tell("self-message");
                _stage._control.Events.Add("prestart-exit");
            }
            private void Delivered(string name)
            {
                _stage._control.Events.Add(name);
                _stage._control.HandlerActors.Add(GraphInterpreter.Current.Context);
                if (++_delivered == 2)
                    Emit(_stage._out, _delivered, CompleteStage);
            }
            public override void OnPull() => Pull(_stage._in);
            public override void OnPush() => Grab(_stage._in);
        }
    }

    private sealed class CallbackControl
    {
        public IAsyncCallback<int> Callback { get; set; } = null!;
        public List<Task<Done>> Feedback { get; } = new();
        public List<string> Events { get; } = new();
        public List<ActivityContext?> Contexts { get; } = new();
        public ActivityContext? ProducerContext { get; set; }
        public TaskCompletionSource<Done> Stopped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class CallbackBurstStage : GraphStageWithMaterializedValue<FlowShape<int, int>, CallbackControl>
    {
        private readonly Exception? _failure;
        private readonly ProducerActivityScope? _producer;
        private readonly Inlet<int> _in = new("callback-burst.in");
        private readonly Outlet<int> _out = new("callback-burst.out");

        public CallbackBurstStage(Exception? failure = null, ProducerActivityScope? producer = null)
        {
            _failure = failure;
            _producer = producer;
            Shape = new FlowShape<int, int>(_in, _out);
        }

        public override FlowShape<int, int> Shape { get; }

        public override ILogicAndMaterializedValue<CallbackControl> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var control = new CallbackControl();
            return new LogicAndMaterializedValue<CallbackControl>(new Logic(this, control), control);
        }

        private sealed class Logic : InAndOutGraphStageLogic
        {
            private readonly CallbackBurstStage _stage;
            private readonly CallbackControl _control;
            private int _remaining;
            private int _count;

            public Logic(CallbackBurstStage stage, CallbackControl control) : base(stage.Shape)
            {
                _stage = stage;
                _control = control;
                control.Callback = GetTypedAsyncCallback<int>(value =>
                {
                    control.Events.Add($"callback-{value}");
                    control.Contexts.Add(Activity.Current?.Context);
                    if (value == 17 && stage._failure is { } failure)
                        throw failure;
                    if (--_remaining == 0)
                    {
                        Push(stage._out, _count);
                        CompleteStage();
                    }
                });
                SetHandlers(stage._in, stage._out, this);
            }

            public override void OnPull() => Pull(_stage._in);

            public override void OnPush()
            {
                _count = _remaining = Grab(_stage._in);
                _control.Events.Add("push-enter");
                using (var producer = _stage._producer?.Start("enqueue-own-callbacks"))
                {
                    _control.ProducerContext = producer?.Context;
                    for (var value = 0; value < _count; value++)
                        _control.Feedback.Add(_control.Callback.InvokeWithFeedback(value));
                }
                _control.Events.Add("push-exit");
            }

            public override void OnUpstreamFinish()
            {
                if (_remaining == 0)
                    CompleteStage();
            }

            public override void PostStop() => _control.Stopped.TrySetResult(Done.Instance);
        }
    }
}
