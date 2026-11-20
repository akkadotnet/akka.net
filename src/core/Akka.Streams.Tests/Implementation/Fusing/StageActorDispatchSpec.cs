//-----------------------------------------------------------------------
// <copyright file="StageActorDispatchSpec.cs" company="Akka.NET Project">
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
using Akka.Streams.Stage;
using Akka.Streams.TestKit;
using Akka.TestKit;
using FluentAssertions;
using Xunit;

namespace Akka.Streams.Tests.Implementation.Fusing;

public sealed class StageActorDispatchSpec : AkkaSpec
{
    public StageActorDispatchSpec(ITestOutputHelper output) : base(output) { }

    [Theory(DisplayName = "Should_QueueStageActorMessagesWithoutReentry_When_SentOnOwnInterpreter")]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(1000)]
    public async Task Should_queue_stage_actor_messages_without_reentry_when_sent_on_own_interpreter(int limit)
    {
        using var materializer = ActorMaterializer.Create(Sys,
            ActorMaterializerSettings.Create(Sys).WithSyncProcessingLimit(limit).WithFuzzingMode(false));
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            var payloads = Enumerable.Range(0, 64).Select(index => new Payload(0, index)).ToArray();
            var (control, result) = Source.Single(1)
                .ViaMaterialized(new DispatchStage(payloads, TestActor), Keep.Right)
                .ToMaterialized(Sink.First<int>(), Keep.Both).Run(materializer);
            (await result.WaitAsync(TimeSpan.FromSeconds(3))).Should().Be(payloads.Length);
            control.Events.Should().Equal(new[] { "push-enter", "push-exit" }
                .Concat(payloads.Select(payload => $"message-{payload.Index}")));
            for (var index = 0; index < payloads.Length; index++)
                control.Received[index].Message.Should().BeSameAs(payloads[index]);
            foreach (var pair in control.Received)
                pair.Sender.Should().BeSameAs(TestActor);
            await control.Stopped.Task.WaitAsync(TimeSpan.FromSeconds(3));
        }, materializer);
    }

    [Fact(DisplayName = "Should_PreserveOneFifo_When_StageActorMessagesAndAsyncCallbacksAreInterleaved")]
    public async Task Should_preserve_one_fifo_when_stage_actor_messages_and_async_callbacks_are_interleaved()
    {
        using var materializer = ActorMaterializer.Create(Sys,
            ActorMaterializerSettings.Create(Sys).WithSyncProcessingLimit(1).WithFuzzingMode(false));
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            var payloads = Enumerable.Range(0, 64).Select(index => new Payload(0, index)).ToArray();
            var (control, result) = Source.Single(1)
                .ViaMaterialized(new DispatchStage(payloads, TestActor, mixed: true), Keep.Right)
                .ToMaterialized(Sink.First<int>(), Keep.Both).Run(materializer);
            (await result.WaitAsync(TimeSpan.FromSeconds(3))).Should().Be(payloads.Length);
            control.Events.Should().Equal(new[] { "push-enter", "push-exit" }.Concat(
                payloads.SelectMany(payload => new[] { $"callback-{payload.Index}", $"message-{payload.Index}" })));
        }, materializer);
    }

    [Fact(DisplayName = "Should_PreserveEachProducerOrderAndReferences_When_StageActorHasConcurrentExternalSenders")]
    public async Task Should_preserve_each_producer_order_and_references_when_stage_actor_has_concurrent_external_senders()
    {
        using var materializer = ActorMaterializer.Create(Sys);
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            var payloads = Enumerable.Range(0, 4).Select(producer => Enumerable.Range(0, 32)
                .Select(index => new Payload(producer, index)).ToArray()).ToArray();
            var senders = Enumerable.Range(0, 4).Select(_ => CreateTestProbe().Ref).ToArray();
            var (control, result) = Source.Maybe<int>()
                .ViaMaterialized(new DispatchStage(payloads.SelectMany(batch => batch).ToArray(), TestActor, external: true), Keep.Right)
                .ToMaterialized(Sink.First<int>(), Keep.Both).Run(materializer);
            var actor = await control.Ready.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await Task.WhenAll(payloads.Select((batch, producer) => Task.Run(() =>
            {
                foreach (var payload in batch)
                    actor.Tell(payload, senders[producer]);
            })));
            (await result.WaitAsync(TimeSpan.FromSeconds(3))).Should().Be(128);
            for (var producer = 0; producer < payloads.Length; producer++)
            {
                var received = control.Received.Where(pair => pair.Message.Producer == producer).ToArray();
                for (var index = 0; index < payloads[producer].Length; index++)
                    received[index].Message.Should().BeSameAs(payloads[producer][index]);
                foreach (var pair in received)
                    pair.Sender.Should().BeSameAs(senders[producer]);
            }
        }, materializer);
    }

    [Fact(DisplayName = "Should_RestoreProducerContext_When_StageActorMessageIsTraced")]
    public async Task Should_restore_producer_context_when_stage_actor_message_is_traced()
    {
        using var collector = new StreamsActivityCollector();
        using var producer = new ProducerActivityScope("StageActorDispatchProducer");
        using var materializer = ActorMaterializer.Create(Sys);
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            var payloads = new[] { new Payload(0, 0), new Payload(0, 1) };
            var (control, result) = Source.Maybe<int>()
                .ViaMaterialized(new DispatchStage(payloads, TestActor, external: true), Keep.Right)
                .ToMaterialized(Sink.First<int>(), Keep.Both).Run(materializer);
            var actor = await control.Ready.Task.WaitAsync(TimeSpan.FromSeconds(3));
            ActivityContext expected;
            using (var activity = producer.Start("stage-actor-send"))
            {
                expected = activity.Context;
                actor.Tell(payloads[0], TestActor);
            }
            actor.Tell(payloads[1], TestActor);
            (await result.WaitAsync(TimeSpan.FromSeconds(3))).Should().Be(2);
            control.Contexts[0].Should().NotBeNull();
            control.Contexts[0]!.Value.TraceId.Should().Be(expected.TraceId);
            control.Contexts[1].Should().BeNull();
            collector.StoppedActivities.Should().Contain(activity =>
                activity.OperationName.StartsWith("akka.stream.ingress", StringComparison.Ordinal)
                && activity.ParentSpanId == expected.SpanId);
        }, materializer);
    }

    [Fact(DisplayName = "Should_StopAndFailWithOriginalCause_When_QueuedStageActorHandlerThrows")]
    public async Task Should_stop_and_fail_with_original_cause_when_queued_stage_actor_handler_throws()
    {
        using var materializer = ActorMaterializer.Create(Sys,
            ActorMaterializerSettings.Create(Sys).WithSyncProcessingLimit(1).WithFuzzingMode(false));
        await this.AssertAllStagesStoppedAsync(async () =>
        {
            var failure = new InvalidOperationException("stage actor failure");
            var payloads = Enumerable.Range(0, 64).Select(index => new Payload(0, index)).ToArray();
            var (control, result) = Source.Single(1)
                .ViaMaterialized(new DispatchStage(payloads, TestActor, failure: failure), Keep.Right)
                .ToMaterialized(Sink.First<int>(), Keep.Both).Run(materializer);
            Func<Task> failing = async () => await result.WaitAsync(TimeSpan.FromSeconds(3));
            (await failing.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(failure);
            await control.Stopped.Task.WaitAsync(TimeSpan.FromSeconds(3));
            control.Received.Select(pair => pair.Message).Should().Equal(payloads.Take(18));
        }, materializer);
    }

    [Fact(DisplayName = "Should_PreservePublicCallbackFactoryTupleAndBecome_When_StageActorIsConstructedDirectly")]
    public async Task Should_preserve_public_callback_factory_tuple_and_become_when_stage_actor_is_constructed_directly()
    {
        using var materializer = ActorMaterializer.Create(Sys);
        var probe = CreateTestProbe();
        var factoryCalls = 0;
        var actor = new StageActor(materializer, receive =>
        {
            factoryCalls++;
            return pair => receive(pair);
        }, pair => probe.Ref.Tell(pair, TestActor));
        try
        {
            var payload = new object();
            actor.Ref.Tell(payload, TestActor);
            var received = await probe.ExpectMsgAsync<(IActorRef, object)>();
            received.Item1.Should().BeSameAs(TestActor);
            received.Item2.Should().BeSameAs(payload);
            actor.Become(pair => probe.Ref.Tell("changed", pair.Item1));
            actor.Ref.Tell(payload, TestActor);
            await probe.ExpectMsgAsync("changed");
            factoryCalls.Should().Be(1);
        }
        finally
        {
            actor.Stop();
        }
    }

    private sealed record Payload(int Producer, int Index);

    private sealed class Control
    {
        public TaskCompletionSource<IActorRef> Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<Done> Stopped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> Events { get; } = new();
        public List<(IActorRef Sender, Payload Message)> Received { get; } = new();
        public List<ActivityContext?> Contexts { get; } = new();
    }

    private sealed class DispatchStage : GraphStageWithMaterializedValue<FlowShape<int, int>, Control>
    {
        private readonly Payload[] _payloads;
        private readonly IActorRef _sender;
        private readonly bool _external;
        private readonly bool _mixed;
        private readonly Exception? _failure;
        private readonly Inlet<int> _in = new("stage-actor-dispatch.in");
        private readonly Outlet<int> _out = new("stage-actor-dispatch.out");

        public DispatchStage(Payload[] payloads, IActorRef sender, bool external = false, Exception? failure = null, bool mixed = false)
        {
            _payloads = payloads;
            _sender = sender;
            _external = external;
            _mixed = mixed;
            _failure = failure;
            Shape = new FlowShape<int, int>(_in, _out);
        }

        public override FlowShape<int, int> Shape { get; }
        public override ILogicAndMaterializedValue<Control> CreateLogicAndMaterializedValue(Attributes inheritedAttributes)
        {
            var control = new Control();
            return new LogicAndMaterializedValue<Control>(new Logic(this, control), control);
        }

        private sealed class Logic : InAndOutGraphStageLogic
        {
            private readonly DispatchStage _stage;
            private readonly Control _control;
            private IActorRef _actor = null!;
            private readonly Action<int> _callback;

            public Logic(DispatchStage stage, Control control) : base(stage.Shape)
            {
                _stage = stage;
                _control = control;
                _callback = GetAsyncCallback<int>(index => control.Events.Add($"callback-{index}"));
                SetHandlers(stage._in, stage._out, this);
            }

            public override void PreStart()
            {
                _actor = GetStageActor(pair =>
                {
                    var payload = (Payload)pair.Item2;
                    _control.Events.Add($"message-{payload.Index}");
                    _control.Received.Add((pair.Item1, payload));
                    _control.Contexts.Add(Activity.Current?.Context);
                    if (payload.Index == 17 && _stage._failure is { } failure)
                        throw failure;
                    if (_control.Received.Count == _stage._payloads.Length)
                    {
                        Emit(_stage._out, _control.Received.Count, CompleteStage);
                    }
                }).Ref;
                _control.Ready.TrySetResult(_actor);
            }

            public override void OnPull() => Pull(_stage._in);
            public override void OnPush()
            {
                Grab(_stage._in);
                _control.Events.Add("push-enter");
                if (!_stage._external)
                    foreach (var payload in _stage._payloads)
                    {
                        if (_stage._mixed)
                            _callback(payload.Index);
                        _actor.Tell(payload, _stage._sender);
                    }
                _control.Events.Add("push-exit");
            }

            // The last actor message completes the stage after the source has finished.
            public override void OnUpstreamFinish() { }
            public override void PostStop() => _control.Stopped.TrySetResult(Done.Instance);
        }
    }
}
