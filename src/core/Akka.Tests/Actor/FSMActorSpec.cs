//-----------------------------------------------------------------------
// <copyright file="FSMActorSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.Actor.Internal;
using Akka.Event;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using static Akka.Actor.FSMBase;

namespace Akka.Tests.Actor
{
    public class FSMActorSpec : AkkaSpec
    {
        #region Actors
        public class Latches
        {
            public Latches(ActorSystem system)
            {
                UnlockedLatch = new TestLatch();
                LockedLatch = new TestLatch();
                UnhandledLatch = new TestLatch();
                TerminatedLatch = new TestLatch();
                TransitionLatch = new TestLatch();
                InitialStateLatch = new TestLatch();
                TransitionCallBackLatch = new TestLatch();
            }

            public TestLatch UnlockedLatch { get; }

            public TestLatch LockedLatch { get; }

            public TestLatch UnhandledLatch { get; }

            public TestLatch TerminatedLatch { get; }

            public TestLatch TransitionLatch { get; }

            public TestLatch InitialStateLatch { get; }

            public TestLatch TransitionCallBackLatch { get; }
        }
        public enum LockState
        {
            Locked,
            Open
        }

        public sealed class CodeState
        {
            public CodeState(string soFar, string code)
            {
                SoFar = soFar;
                Code = code;
            }

            public string SoFar { get; }

            public string Code { get; }
        }

        public class Hello
        {
            public static Hello Instance { get; } = new Hello();

            private Hello() { }
        }

        public class Bye
        {
            public static Bye Instance { get; } = new Bye();

            private Bye() { }
        }

        public class Lock : FSM<LockState, CodeState>
        {
            private readonly Latches _latches;
            private readonly ILoggingAdapter Log = Context.GetLogger();

            public Lock(string code, TimeSpan timeout, Latches latches)
            {
                var code1 = code;
                _latches = latches;
                StartWith(LockState.Locked, new CodeState("", code1));

                When(LockState.Locked, evt =>
                {
                    if (evt.FsmEvent is char)
                    {
                        var codeState = evt.StateData;
                        if (codeState.Code == code1)
                        {
                            DoUnlock();
                            return GoTo(LockState.Open).Using(new CodeState("", codeState.Code)).ForMax(timeout);
                        }
                    }
                    else if (evt.FsmEvent.Equals("hello"))
                    {
                        return Stay().Replying("world");
                    }
                    else if (evt.FsmEvent.Equals("bey"))
                    {
                        return Stop(Shutdown.Instance);
                    }

                    return null;
                });

                When(LockState.Open, evt =>
                {
                    if (evt.FsmEvent is StateTimeout)
                    {
                        DoLock();
                        return GoTo(LockState.Locked);
                    }

                    return null;
                });

                WhenUnhandled(evt =>
                {
                    var msg = evt.FsmEvent;
                    Log.Warning($"unhandled event {msg} in state {StateName} with data {StateData}");
                    latches.UnhandledLatch.Open();
                    return Stay();
                });

                OnTransition((state, nextState) =>
                {
                    if (state == LockState.Locked && nextState == LockState.Open)
                    {
                        _latches.TransitionLatch.Open();
                    }
                });

                OnTermination(evt =>
                {
                    if (evt.Reason == Shutdown.Instance && evt.TerminatedState == LockState.Locked)
                    {
                        // stop is called from lockstate with shutdown as reason...
                        latches.TerminatedLatch.Open();
                    }
                });

                Initialize();
            }

            private void DoLock()
            {
                _latches.LockedLatch.Open();
            }

            private void DoUnlock()
            {
                _latches.UnlockedLatch.Open();
            }
        }

        public class TransitionTester : UntypedActor
        {
            private readonly Latches _latches;

            public TransitionTester(Latches latches)
            {
                _latches = latches;
            }

            protected override void OnReceive(object message)
            {
                if (message is Transition<object>)
                {
                    _latches.TransitionCallBackLatch.Open();
                }
                else if (message is CurrentState<LockState>)
                {
                    _latches.InitialStateLatch.Open();
                }
            }
        }

        public class AnswerTester : UntypedActor
        {
            private readonly TestLatch _answerLatch;
            private readonly IActorRef _lockFsm;

            public AnswerTester(TestLatch answerLatch, IActorRef lockFsm)
            {
                _answerLatch = answerLatch;
                _lockFsm = lockFsm;
            }

            protected override void OnReceive(object message)
            {
                if (message is Hello)
                {
                    _lockFsm.Tell("hello");
                }
                else if (message.Equals("world"))
                {
                    _answerLatch.Open();
                }
                else if (message is Bye)
                {
                    _lockFsm.Tell("bye");
                }
            }
        }

        public class ActorLogTermination : FSM<int, object>
        {
            public ActorLogTermination()
            {
                StartWith(1, null);

                When(1, evt =>
                {
                    if (evt.FsmEvent.Equals("go"))
                    {
                        return GoTo(2);
                    }
                    return null;
                });
            }
        }

        public class ActorStopTermination : FSM<int, object>
        {
            private readonly TestLatch _testLatch;

            public ActorStopTermination(TestLatch testLatch, IActorRef testActor)
            {
                _testLatch = testLatch;

                StartWith(1, null);

                When(1, evt => null);

                OnTermination(x =>
                {
                    testActor.Tell(x);
                });
            }

            protected override void PreStart()
            {
                _testLatch.CountDown();
            }
        }

        public class ActorStopReason : FSM<int, object>
        {
            public ActorStopReason(string expected, IActorRef testActor)
            {
                StartWith(1, null);

                When(1, evt =>
                {
                    if (evt.FsmEvent.Equals(2))
                    {
                        return Stop(Normal.Instance, expected);
                    }

                    return null;
                });

                OnTermination(x =>
                {
                    if (x.Reason is Normal 
                        && x.TerminatedState == 1 
                        && x.StateData.Equals(expected))
                    {
                        testActor.Tell("green");
                    }
                });
            }
        }

        public class StopTimersFSM : FSM<string, object>
        {
            public StopTimersFSM(IActorRef testActor, List<string> timerNames)
            {
                StartWith("not-started", null);

                When("not-started", evt =>
                {
                    if (evt.FsmEvent.Equals("start"))
                    {
                        return GoTo("started").Replying("starting");
                    }

                    return null;
                });

                When("started", evt =>
                {
                    if (evt.FsmEvent.Equals("stop"))
                    {
                        return Stop();
                    }

                    return null;
                }, 10.Seconds());

                OnTransition((state1, state2) =>
                {
                    if (state1.Equals("not-started") && state2.Equals("started"))
                    {
                        foreach (var timerName in timerNames)
                        {
                            SetTimer(timerName, new object(), 10.Seconds(), false);
                        }
                    }
                });

                OnTermination(x =>
                {
                    foreach (var timerName in timerNames)
                    {
                        IsTimerActive(timerName).Should().BeFalse();
                        var intern = (IInternalSupportsTestFSMRef<string, object>)this;
                        intern.IsStateTimerActive.Should().BeFalse();
                    }
                    testActor.Tell("stopped");
                });
            }
        }

        public class RollingEventLogFsm : FSM<int, int>, ILoggingFSM
        {
            public RollingEventLogFsm()
            {
                StartWith(1, 0);

                When(1, evt =>
                {
                    if (evt.FsmEvent.Equals("count"))
                    {
                        return Stay().Using(evt.StateData + 1);
                    }
                    else if (evt.FsmEvent.Equals("log"))
                    {
                        return Stay().Replying("getlog");
                    }

                    return null;
                });
            }
        }

        public class TransformingStateFsm : FSM<int, int>
        {
            public TransformingStateFsm()
            {
                StartWith(0, 0);

                When(0, evt =>
                {
                    return Transform(evt2 =>
                    {
                        if (evt2.FsmEvent.Equals("go"))
                            return Stay();

                        return null;
                    }).Using(state =>
                    {
                        return GoTo(1);
                    })(evt);
                });

                When(1, evt =>
                {
                    return Stay();
                });
            }
        }

        public const string OverrideInitState = "init";
        public const string OverrideTimeoutToInf = "override-timeout-to-inf";
        public class CancelStateTimeoutFsm : FSM<string, string>
        {
            public CancelStateTimeoutFsm(TestProbe p)
            {
                StartWith(OverrideInitState, "");

                When(OverrideInitState, evt =>
                {
                    if (evt.FsmEvent is StateTimeout)
                    {
                        p.Ref.Tell(StateTimeout.Instance);
                        return Stay();
                    }

                    if (evt.FsmEvent.Equals(OverrideTimeoutToInf))
                    {
                        p.Ref.Tell(OverrideTimeoutToInf);
                        return Stay().ForMax(TimeSpan.MaxValue);
                    }

                    return null;
                }, 1.Seconds());

                Initialize();
            }
        }

        #endregion

        [Fact(Skip = "Not implemented yet")]
        public void FSMActor_must_unlock_the_lock()
        {
            var latches = new Latches(Sys);
            var timeout = 2.Seconds();
            var lockFsm = Sys.ActorOf(Props.Create(() => new Lock("33221", 1.Seconds(), latches)));
            var transitionTester = Sys.ActorOf(Props.Create(() => new TransitionTester(latches)));
            lockFsm.Tell(new SubscribeTransitionCallBack(transitionTester));
            latches.InitialStateLatch.Ready(timeout);

            lockFsm.Tell('3');
            lockFsm.Tell('3');
            lockFsm.Tell('2');
            lockFsm.Tell('2');
            lockFsm.Tell('1');

            latches.UnlockedLatch.Ready(timeout);
            latches.TransitionLatch.Ready(timeout);
            latches.TransitionCallBackLatch.Ready(timeout);
            latches.LockedLatch.Ready(timeout);

            EventFilter.Warning("unhandled event").ExpectOne(() =>
            {
                lockFsm.Tell("not_handled");
                latches.UnhandledLatch.Ready(timeout);
            });

            var answerLatch = new TestLatch();
            var tester = Sys.ActorOf(Props.Create(() => new AnswerTester(answerLatch, lockFsm)));
            tester.Tell(Hello.Instance);
            answerLatch.Ready(timeout);

            tester.Tell(Bye.Instance);
            latches.TerminatedLatch.Ready(timeout);
        }

        [Fact]
        public void FSMActor_must_log_termination()
        {
            var actorRef = Sys.ActorOf(Props.Create(() => new ActorLogTermination()));
            var name = actorRef.Path.ToString();
            EventFilter.Error("Next state 2 does not exist").ExpectOne(() =>
            {
                Sys.EventStream.Subscribe(TestActor, typeof(Error));
                actorRef.Tell("go");
                var error = ExpectMsg<Error>(1.Seconds());
                error.LogSource.Should().Contain(name);
                error.Message.Should().Be("Next state 2 does not exist");
                Sys.EventStream.Unsubscribe(TestActor);
            });
        }

        [Fact]
        public void FSMActor_must_run_onTermination_upon_ActorRef_Stop()
        {
            var started = new TestLatch(1);
            var actorRef = Sys.ActorOf(Props.Create(() => new ActorStopTermination(started, TestActor)));
            started.Ready();
            Sys.Stop(actorRef);
            var stopEvent = ExpectMsg<StopEvent<int, object>>(1.Seconds());
            stopEvent.Reason.Should().BeOfType<Shutdown>();
            stopEvent.TerminatedState.Should().Be(1);
        }

        [Fact]
        public void FSMActor_must_run_onTermination_with_updated_state_upon_stop()
        {
            var expected = "pigdog";
            var actorRef = Sys.ActorOf(Props.Create(() => new ActorStopReason(expected, TestActor)));
            actorRef.Tell(2);
            ExpectMsg("green");
        }

        [Fact]
        public void FSMActor_must_cancel_all_timers_when_terminated()
        {
            var timerNames = new List<string> {"timer-1", "timer-2", "timer-3"};

            var fsmRef = new TestFSMRef<StopTimersFSM, string, object>(Sys, Props.Create(
                () => new StopTimersFSM(TestActor, timerNames)));

            Action<bool> checkTimersActive = active =>
            {
                foreach (var timer in timerNames)
                {
                    fsmRef.IsTimerActive(timer).Should().Be(active);
                    fsmRef.IsStateTimerActive().Should().Be(active);
                }
            };

            checkTimersActive(false);

            fsmRef.Tell("start");
            ExpectMsg("starting", 1.Seconds());
            checkTimersActive(true);

            fsmRef.Tell("stop");
            ExpectMsg("stopped", 1.Seconds());
        }

        [Fact(Skip = "Not implemented yet")]
        public void FSMActor_must_log_events_and_transitions_if_asked_to_do_so()
        {
        }

        [Fact(Skip = "Does not pass due to LoggingFsm limitations")]
        public void FSMActor_must_fill_rolling_event_log_and_hand_it_out()
        {
            var fsmRef = new TestActorRef<RollingEventLogFsm>(Sys, Props.Create<RollingEventLogFsm>());
            fsmRef.Tell("log");
            ExpectMsg<object>(1.Seconds());
            fsmRef.Tell("count");
            fsmRef.Tell("log");
            ExpectMsg<object>(1.Seconds());
            fsmRef.Tell("count");
            fsmRef.Tell("log");
            ExpectMsg<object>(1.Seconds());
        }

        [Fact]
        public void FSMActor_must_allow_transforming_of_state_results()
        {
            var fsmRef = Sys.ActorOf(Props.Create<TransformingStateFsm>());
            fsmRef.Tell(new SubscribeTransitionCallBack(TestActor));
            fsmRef.Tell("go");
            ExpectMsg(new CurrentState<int>(fsmRef, 0));
            ExpectMsg(new Transition<int>(fsmRef, 0, 1));
        }

        [Fact(Skip = "Not implemented yet")]
        public void FSMActor_must_allow_cancelling_stateTimeout_by_issuing_forMax()
        {
            var sys = ActorSystem.Create("fsmEvent", Sys.Settings.Config);
            var p = CreateTestProbe(sys);

            var fsmRef = sys.ActorOf(Props.Create(() => new CancelStateTimeoutFsm(p)));

            try
            {
                p.ExpectMsg<StateTimeout>();
                fsmRef.Tell(OverrideTimeoutToInf);
                p.ExpectMsg(OverrideTimeoutToInf);
                p.ExpectNoMsg(3.Seconds());
            }
            finally
            {
                sys.WhenTerminated.Wait();
            }
        }
    }
}
