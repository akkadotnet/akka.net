//-----------------------------------------------------------------------
// <copyright file="GraphStageTimersSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Streams.Dsl;
using Akka.Streams.Implementation.Fusing;
using Akka.Streams.Stage;
using Akka.Streams.TestKit;
using Akka.TestKit;
using Akka.TestKit.Xunit2.Attributes;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

// ReSharper disable InvokeAsExtensionMethod

namespace Akka.Streams.Tests.Dsl
{
    public class GraphStageTimersSpec : AkkaSpec
    {
        private ActorMaterializer Materializer { get; }

        public GraphStageTimersSpec(ITestOutputHelper helper) : base(helper)
        {
            var settings = ActorMaterializerSettings.Create(Sys);
            Materializer = ActorMaterializer.Create(Sys, settings);
        }

        private SideChannel SetupIsolatedStage()
        {
            var channel = new SideChannel();
            var stopPromise =
                Source.Maybe<int>()
                    .Via(new TestStage(TestActor, channel, this))
                    .To(Sink.Ignore<int>())
                    .Run(Materializer);
            channel.StopPromise = stopPromise;
            AwaitCondition(()=>channel.IsReady);
            return channel;
        }

        [LocalFact(SkipLocal = "Racy on Azure DevOps")]
        public async Task GraphStage_timer_support_must_receive_single_shot_timer()
        {
            var driver = SetupIsolatedStage();
            await AwaitAssertAsync(async() =>
            {
                driver.Tell(TestSingleTimer.Instance);
                await ExpectMsgAsync(new Tick(1), TimeSpan.FromSeconds(10));
                await ExpectNoMsgAsync(TimeSpan.FromSeconds(1));
            });
            driver.StopStage();
        }

        [LocalFact(SkipLocal = "Racy on Azure DevOps")]
        public async Task GraphStage_timer_support_must_resubmit_single_shot_timer()
        {
            var driver = SetupIsolatedStage();
            await WithinAsync(TimeSpan.FromSeconds(2.5), async() =>
            {
                await WithinAsync(TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1), async() =>
                {
                    driver.Tell(TestSingleTimerResubmit.Instance);
                    await ExpectMsgAsync(new Tick(1));
                });
                await WithinAsync(TimeSpan.FromSeconds(1), async() => await ExpectMsgAsync(new Tick(2)));

                await ExpectNoMsgAsync(TimeSpan.FromSeconds(1));
            });
            driver.StopStage();
        }

        [Fact]
        public async Task GraphStage_timer_support_must_correctly_cancel_a_named_timer()
        {
            var driver = SetupIsolatedStage();
            driver.Tell(TestCancelTimer.Instance);
            await WithinAsync(TimeSpan.FromMilliseconds(5000), async() => await ExpectMsgAsync<TestCancelTimerAck>());
            await WithinAsync(TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(3000), async() => await ExpectMsgAsync(new Tick(1)));
            await ExpectNoMsgAsync(TimeSpan.FromSeconds(1));
            driver.StopStage();
        }

        [Fact]
        public async Task GraphStage_timer_support_must_receive_and_cancel_a_repeated_timer()
        {
            var driver = SetupIsolatedStage();
            driver.Tell(TestRepeatedTimer.Instance);
            var seq = ReceiveWhile(TimeSpan.FromSeconds(30), o => (Tick)o, msgs: 5);
            seq.Should().HaveCount(5);
            await ExpectNoMsgAsync(TimeSpan.FromSeconds(1));
            driver.StopStage();
        }

        [Fact]
        public async Task GraphStage_timer_support_must_produce_scheduled_ticks_as_expected()
        {
            await this.AssertAllStagesStoppedAsync(async() => {
                var upstream = this.CreatePublisherProbe<int>();
                var downstream = this.CreateSubscriberProbe<int>();

                Source.FromPublisher(upstream)
                    .Via(new TestStage2())
                    .RunWith(Sink.FromSubscriber(downstream), Materializer);

                downstream.Request(5);
                downstream.ExpectNext(1, 2, 3);

                await downstream.ExpectNoMsgAsync(TimeSpan.FromSeconds(1));

                await upstream.SendCompleteAsync();
                await downstream.ExpectCompleteAsync();
            }, Materializer);
        }

        [Fact]
        public async Task GraphStage_timer_support_must_propagate_error_if_OnTimer_throws_an_Exception()
        {
            await this.AssertAllStagesStoppedAsync(() => {
                var exception = new TestException("Expected exception to the rule");
                var upstream = this.CreatePublisherProbe<int>();
                var downstream = this.CreateSubscriberProbe<int>();

                Source.FromPublisher(upstream)
                    .Via(new ThrowStage(exception))
                    .RunWith(Sink.FromSubscriber(downstream), Materializer);

                downstream.Request(1);
                downstream.ExpectError().Should().Be(exception);
                return Task.CompletedTask;
            }, Materializer);
        }

        #region Test classes

        private sealed class TestSingleTimer
        {
            public static readonly TestSingleTimer Instance = new TestSingleTimer();

            private TestSingleTimer()
            {

            }
        }

        private sealed class TestSingleTimerResubmit
        {
            public static readonly TestSingleTimerResubmit Instance = new TestSingleTimerResubmit();

            private TestSingleTimerResubmit()
            {

            }
        }

        private sealed class TestCancelTimer
        {
            public static readonly TestCancelTimer Instance = new TestCancelTimer();

            private TestCancelTimer()
            {

            }
        }

        private sealed class TestCancelTimerAck
        {
            public static readonly TestCancelTimerAck Instance = new TestCancelTimerAck();

            private TestCancelTimerAck()
            {

            }
        }

        private sealed class TestRepeatedTimer
        {
            public static readonly TestRepeatedTimer Instance = new TestRepeatedTimer();

            private TestRepeatedTimer()
            {

            }
        }

        private sealed class Tick
        {
            public int N { get; }

            public Tick(int n)
            {
                N = n;
            }

            public override bool Equals(object obj)
            {
                var t = obj as Tick;
                return t != null && Equals(t);
            }

            private bool Equals(Tick other) => N == other.N;

            public override int GetHashCode() => N;
        }

        private sealed class SideChannel
        {
            public volatile Action<object> AsyncCallback;
            public volatile TaskCompletionSource<int> StopPromise;

            public bool IsReady => AsyncCallback != null;
            public void Tell(object message) => AsyncCallback(message);
            public void StopStage() => StopPromise.TrySetResult(-1);
        }

        private sealed class TestStage : SimpleLinearGraphStage<int>
        {
            private sealed class Logic : TimerGraphStageLogic
            {
                private const string TestSingleTimerKey = "TestSingleTimer";
                private const string TestSingleTimerResubmitKey = "TestSingleTimerResubmit";
                private const string TestCancelTimerKey = "TestCancelTimer";
                private const string TestRepeatedTimerKey = "TestRepeatedTimer";
                private readonly TestStage _stage;
                private int _tickCount = 1;

                public Logic(TestStage stage) : base(stage.Shape)
                {
                    _stage = stage;

                    SetHandler(stage.Inlet, onPush: () => Push(stage.Outlet, Grab(stage.Inlet)));

                    SetHandler(stage.Outlet, onPull: () => Pull(stage.Inlet));
                }

                public override void PreStart()
                    => _stage._sideChannel.AsyncCallback = GetAsyncCallback<object>(OnTestEvent);

                private void OnTestEvent(object message)
                {
                    switch (message)
                    {
                        case TestSingleTimer _:
                            ScheduleOnce(TestSingleTimerKey, Dilated(500));
                            break;
                        case TestSingleTimerResubmit _:
                            ScheduleOnce(TestSingleTimerResubmitKey, Dilated(500));
                            break;
                        case TestCancelTimer _:
                            ScheduleOnce(TestCancelTimerKey, Dilated(1));
                            // Likely in mailbox but we cannot guarantee
                            CancelTimer(TestCancelTimerKey);
                            _stage._probe.Tell(TestCancelTimerAck.Instance);
                            ScheduleOnce(TestCancelTimerKey, Dilated(500));
                            break;
                        case TestRepeatedTimer _:
                            ScheduleRepeatedly(TestRepeatedTimerKey, Dilated(100));
                            break;
                    }
                }

                private TimeSpan Dilated(int milliseconds)
                    => _stage._testKit.Dilated(TimeSpan.FromMilliseconds(milliseconds));

                protected internal override void OnTimer(object timerKey)
                {
                    var tick = new Tick(_tickCount++);
                    _stage._probe.Tell(tick);

                    if (timerKey.Equals(TestSingleTimerResubmitKey) && tick.N == 1)
                        ScheduleOnce(TestSingleTimerResubmitKey, Dilated(500));
                    else if (timerKey.Equals(TestRepeatedTimerKey) && tick.N == 5)
                        CancelTimer(TestRepeatedTimerKey);
                }
            }

            private readonly IActorRef _probe;
            private readonly SideChannel _sideChannel;
            private readonly TestKitBase _testKit;

            public TestStage(IActorRef probe, SideChannel sideChannel, TestKitBase testKit)
            {
                _probe = probe;
                _sideChannel = sideChannel;
                _testKit = testKit;
            }

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
        }
        
        private sealed class TestStage2 : SimpleLinearGraphStage<int>
        {
            private sealed class Logic : TimerGraphStageLogic
            {
                private const string TimerKey = "tick";
                private readonly TestStage2 _stage;
                private int _tickCount;

                public Logic(TestStage2 stage) : base(stage.Shape)
                {
                    _stage = stage;

                    SetHandler(stage.Inlet, onPush: DoNothing, 
                        onUpstreamFinish: CompleteStage,
                        onUpstreamFailure: FailStage);


                    SetHandler(stage.Outlet, onPull: DoNothing, onDownstreamFinish: _ => CompleteStage());
                }

                public override void PreStart() => ScheduleRepeatedly(TimerKey, TimeSpan.FromMilliseconds(100));

                protected internal override void OnTimer(object timerKey)
                {
                    _tickCount++;
                    if(IsAvailable(_stage.Outlet))
                        Push(_stage.Outlet, _tickCount);
                    if(_tickCount == 3)
                        CancelTimer(TimerKey);
                }
            }

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
        }

        private sealed class ThrowStage : SimpleLinearGraphStage<int>
        {
            private sealed class Logic : TimerGraphStageLogic
            {
                private readonly ThrowStage _stage;

                public Logic(ThrowStage stage) : base(stage.Shape)
                {
                    _stage = stage;

                    SetHandler(stage.Outlet, onPull: () => Pull(stage.Inlet));
                    SetHandler(stage.Inlet, onPush: DoNothing);
                }

                public override void PreStart() => ScheduleOnce("tick", TimeSpan.FromMilliseconds(100));

                protected internal override void OnTimer(object timerKey)
                {
                    throw _stage._exception;
                }
            }

            private readonly Exception _exception;

            public ThrowStage(Exception exception)
            {
                _exception = exception;
            }

            protected override GraphStageLogic CreateLogic(Attributes inheritedAttributes) => new Logic(this);
        }
        #endregion
    }
}
