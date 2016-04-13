using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.Streams.Dsl;
using Akka.Streams.Stage;
using Akka.Streams.TestKit.Tests;
using Akka.TestKit.Internal;
using Akka.TestKit.TestEvent;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Akka.Streams.Tests.Dsl
{
    public class StageActorRefSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public StageActorRefSpec(ITestOutputHelper helper) : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        private static GraphStageWithMaterializedValue<SinkShape<int>, Task<int>> SumStage(IActorRef probe)
            => new SumTestStage(probe);

        [Fact]
        public void A_Graph_stage_ActorRef_must_receive_messages()
        {
            var t = Source.Maybe<int>().ToMaterialized(SumStage(TestActor), Keep.Both).Run(Materializer);
            var res = t.Item2;

            var stageRef = ExpectMsg<IActorRef>();
            stageRef.Tell(new Add(1));
            stageRef.Tell(new Add(2));
            stageRef.Tell(new Add(3));

            stageRef.Tell(StopNow.Instance);
            res.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            res.Result.Should().Be(6);
        }

        [Fact]
        public void A_Graph_stage_ActorRef_must_be_able_to_be_replied_to()
        {
            var t = Source.Maybe<int>().ToMaterialized(SumStage(TestActor), Keep.Both).Run(Materializer);
            var res = t.Item2;

            var stageRef = ExpectMsg<IActorRef>();
            stageRef.Tell(new AddAndTell(1));
            ExpectMsg(1);
            stageRef.Should().Be(LastSender);
            LastSender.Tell(new AddAndTell(9));
            ExpectMsg(10);

            stageRef.Tell(StopNow.Instance);
            res.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            res.Result.Should().Be(10);
        }

        [Fact]
        public void A_Graph_stage_ActorRef_must_yield_the_same_self_ref_each_time()
        {
            var t = Source.Maybe<int>().ToMaterialized(SumStage(TestActor), Keep.Both).Run(Materializer);
            var res = t.Item2;

            var stageRef = ExpectMsg<IActorRef>();
            stageRef.Tell(CallInitStageActorRef.Instance);
            var explicitlyObtained = ExpectMsg<IActorRef>();
            stageRef.Should().Be(explicitlyObtained);
            explicitlyObtained.Tell(new AddAndTell(1));
            ExpectMsg(1);
            LastSender.Tell(new AddAndTell(2));
            ExpectMsg(3);
            stageRef.Tell(new AddAndTell(3));
            ExpectMsg(6);

            stageRef.Tell(StopNow.Instance);
            res.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            res.Result.Should().Be(6);
        }

        [Fact]
        public void A_Graph_stage_ActorRef_must_be_watchable()
        {
            var t = Source.Maybe<int>().ToMaterialized(SumStage(TestActor), Keep.Both).Run(Materializer);
            var source = t.Item1;
            var res = t.Item2;

            var stageRef = ExpectMsg<IActorRef>();
            Watch(stageRef);

            stageRef.Tell(new Add(1));
            source.SetResult(0);

            res.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            res.Result.Should().Be(1);
            ExpectTerminated(stageRef);
        }

        [Fact]
        public void A_Graph_stage_ActorRef_must_be_able_to_become()
        {
            var t = Source.Maybe<int>().ToMaterialized(SumStage(TestActor), Keep.Both).Run(Materializer);
            var source = t.Item1;
            var res = t.Item2;

            var stageRef = ExpectMsg<IActorRef>();
            Watch(stageRef);

            stageRef.Tell(new Add(1));
            stageRef.Tell(BecomeStringEcho.Instance);
            stageRef.Tell(42);
            ExpectMsg("42");

            source.SetResult(0);
            res.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            res.Result.Should().Be(1);
        }

        [Fact]
        public void A_Graph_stage_ActorRef_must_reply_Terminated_when_terminated_stage_is_watched()
        {
            var t = Source.Maybe<int>().ToMaterialized(SumStage(TestActor), Keep.Both).Run(Materializer);
            var source = t.Item1;
            var res = t.Item2;

            var stageRef = ExpectMsg<IActorRef>();
            Watch(stageRef);

            stageRef.Tell(new Add(1));
            source.SetResult(0);
            res.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            res.Result.Should().Be(1);
            ExpectTerminated(stageRef);

            var p = CreateTestProbe();
            p.Watch(stageRef);
            p.ExpectTerminated(stageRef);
        }

        [Fact]
        public void A_Graph_stage_ActorRef_must_be_unwatchable()
        {
            var t = Source.Maybe<int>().ToMaterialized(SumStage(TestActor), Keep.Both).Run(Materializer);
            var source = t.Item1;
            var res = t.Item2;

            var stageRef = ExpectMsg<IActorRef>();
            Watch(stageRef);
            Unwatch(stageRef);

            stageRef.Tell(new Add(1));
            source.SetResult(0);
            res.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            res.Result.Should().Be(1);

            ExpectNoMsg(100);
        }

        [Fact]
        public void A_Graph_stage_ActorRef_must_ignore_and_log_warnings_for_PoisonPill_and_Kill_messages()
        {
            var t = Source.Maybe<int>().ToMaterialized(SumStage(TestActor), Keep.Both).Run(Materializer);
            var source = t.Item1;
            var res = t.Item2;

            var stageRef = ExpectMsg<IActorRef>();
            stageRef.Tell(new Add(40));
            
            Sys.EventStream.Publish(new Mute(new CustomEventFilter(e => e is Warning)));
            Sys.EventStream.Subscribe(TestActor, typeof (Warning));

            stageRef.Tell(PoisonPill.Instance);
            var warn = ExpectMsg<Warning>(TimeSpan.FromSeconds(1));
            warn.Message.ToString()
                .Should()
                .MatchRegex(
                    "<PoisonPill> message sent to StageActorRef\\(akka\\://test/user/StreamSupervisor-[0-9]+/StageActorRef-[0-9]\\) will be ignored, since it is not a real Actor. Use a custom message type to communicate with it instead.");

            stageRef.Tell(Kill.Instance);
            warn = ExpectMsg<Warning>(TimeSpan.FromSeconds(1));
            warn.Message.ToString()
                .Should()
                .MatchRegex(
                    "<Kill> message sent to StageActorRef\\(akka\\://test/user/StreamSupervisor-[0-9]+/StageActorRef-[0-9]\\) will be ignored, since it is not a real Actor. Use a custom message type to communicate with it instead.");
            source.SetResult(2);
            res.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
            res.Result.Should().Be(42);
        }


        private sealed class Add
        {
            public readonly int N;

            public Add(int n)
            {
                N = n;
            }
        }
        private sealed class AddAndTell
        {
            public readonly int N;

            public AddAndTell(int n)
            {
                N = n;
            }
        }
        private sealed class CallInitStageActorRef
        {
            public static readonly CallInitStageActorRef Instance = new CallInitStageActorRef();
            private CallInitStageActorRef() { }
        }
        private sealed class BecomeStringEcho
        {
            public static readonly BecomeStringEcho Instance = new BecomeStringEcho();
            private BecomeStringEcho() { }
        }
        private sealed class PullNow
        {
            public static readonly PullNow Instance = new PullNow();
            private PullNow() { }
        }
        private sealed class StopNow
        {
            public static readonly StopNow Instance = new StopNow();
            private StopNow() { }
        }


        private class SumTestStage : GraphStageWithMaterializedValue<SinkShape<int>, Task<int>>
        {
            private readonly IActorRef _probe;

            #region internal classes

            private class Logic : GraphStageLogic
            {
                private readonly SumTestStage _stage;
                private readonly TaskCompletionSource<int> _promise;
                private int _sum;
                private StageActorRef _self;

                public Logic(SumTestStage stage, TaskCompletionSource<int> promise) : base(stage.Shape)
                {
                    _stage = stage;
                    _promise = promise;

                    SetHandler(stage._inlet, onPush: () =>
                    {
                        _sum += Grab(stage._inlet);
                        promise.TrySetResult(_sum);
                        CompleteStage();
                    }, onUpstreamFinish: () =>
                    {
                        promise.TrySetResult(_sum);
                        CompleteStage();
                    }, onUpstreamFailure: ex =>
                    {
                        promise.TrySetException(ex);
                        FailStage(ex);
                    });
                }

                public override void PreStart()
                {
                    Pull(_stage._inlet);
                    _self = GetStageActorRef(Behaviour);
                    _stage._probe.Tell(_self);
                }

                private void Behaviour(Tuple<IActorRef, object> args)
                {
                    var msg = args.Item2;
                    var sender = args.Item1;

                    msg.Match()
                        .With<Add>(a => _sum += a.N)
                        .With<PullNow>(() => Pull(_stage._inlet))
                        .With<CallInitStageActorRef>(() => sender.Tell(GetStageActorRef(Behaviour), _self))
                        .With<BecomeStringEcho>(() => GetStageActorRef(tuple => tuple.Item1.Tell(tuple.Item2.ToString())))
                        .With<StopNow>(() =>
                        {
                            _promise.TrySetResult(_sum);
                            CompleteStage();
                        }).With<AddAndTell>(a =>
                        {
                            _sum += a.N;
                            sender.Tell(_sum, _self);
                        });
                }
            }
#endregion

            private readonly Inlet<int> _inlet = new Inlet<int>("IntSum.in");
            
            public SumTestStage(IActorRef probe)
            {
                _probe = probe;
                Shape = new SinkShape<int>(_inlet);
            }

            public override SinkShape<int> Shape { get; }

            public override GraphStageLogic CreateLogicAndMaterializedValue(Attributes inheritedAttributes, out Task<int> materialized)
            {
                var promise = new TaskCompletionSource<int>();
                materialized = promise.Task;
                return new Logic(this, promise);
            }
        }
    }
}
