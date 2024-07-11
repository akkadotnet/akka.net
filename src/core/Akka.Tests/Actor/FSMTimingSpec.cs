//-----------------------------------------------------------------------
// <copyright file="FSMTimingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2023 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2023 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Event;
using Akka.TestKit;
using Akka.Util.Internal;
using FluentAssertions;
using FluentAssertions.Extensions;
using Xunit;
using static Akka.Actor.FSMBase;

namespace Akka.Tests.Actor
{
    public class FSMTimingSpec : AkkaSpec
    {
        public IActorRef FSM { get; }

        public FSMTimingSpec()
        {
            FSM = Sys.ActorOf(Props.Create(() => new StateMachine(TestActor)), "fsm");
            FSM.Tell(new SubscribeTransitionCallBack(TestActor));
            ExpectMsg(new CurrentState<FsmState>(FSM, FsmState.Initial));
        }

        [Fact]
        public async Task FSM_must_receive_StateTimeout()
        {
            FSM.Tell(FsmState.TestStateTimeout);
            await ExpectMsgAsync(new Transition<FsmState>(FSM, FsmState.Initial, FsmState.TestStateTimeout));
            await ExpectMsgAsync(new Transition<FsmState>(FSM, FsmState.TestStateTimeout, FsmState.Initial));
            await ExpectNoMsgAsync(50.Milliseconds());

        }

        [Fact]
        public async Task FSM_must_cancel_a_StateTimeout()
        {
            FSM.Tell(FsmState.TestStateTimeout);
            FSM.Tell(Cancel.Instance);
            await ExpectMsgAsync(new Transition<FsmState>(FSM, FsmState.Initial, FsmState.TestStateTimeout));
            await ExpectMsgAsync<Cancel>();
            await ExpectMsgAsync(new Transition<FsmState>(FSM, FsmState.TestStateTimeout, FsmState.Initial));
            await ExpectNoMsgAsync(50.Milliseconds());
        }

        [Fact]
        public async Task FSM_must_cancel_a_StateTimeout_when_actor_is_stopped()
        {
            var stoppingActor = Sys.ActorOf(Props.Create<StoppingActor>());
            Sys.EventStream.Subscribe(TestActor, typeof(DeadLetter));
            stoppingActor.Tell(FsmState.TestStoppingActorStateTimeout);

            await ExpectNoMsgAsync(300.Milliseconds());

        }

        [Fact]
        public async Task FSM_must_allow_StateTimeout_override()
        {
            //the timeout in state TestStateTimeout is 800ms, then it will change back to Initial
            await WithinAsync(400.Milliseconds(), async() =>
            {
                FSM.Tell(FsmState.TestStateTimeoutOverride);
                await ExpectMsgAsync(new Transition<FsmState>(FSM, FsmState.Initial, FsmState.TestStateTimeout));
                await ExpectNoMsgAsync(300.Milliseconds());
            });

            await WithinAsync(1.Seconds(), async () =>
            {
                FSM.Tell(Cancel.Instance);
                await ExpectMsgAsync<Cancel>();
                await ExpectMsgAsync(new Transition<FsmState>(FSM, FsmState.TestStateTimeout, FsmState.Initial));
            });
        }

        [Fact]
        public async Task FSM_must_receive_single_shot_timer()
        {
            await WithinAsync(2.Seconds(), async() =>
            {
                await WithinAsync(500.Milliseconds(), 1.Seconds(), async() =>
                {
                    FSM.Tell(FsmState.TestSingleTimer);
                    await ExpectMsgAsync(new Transition<FsmState>(FSM, FsmState.Initial, FsmState.TestSingleTimer));
                    await ExpectMsgAsync<Tick>();
                    await ExpectMsgAsync(new Transition<FsmState>(FSM, FsmState.TestSingleTimer, FsmState.Initial));
                });
                await ExpectNoMsgAsync(500.Milliseconds());
            });
        }

        [Fact]
        public async Task FSM_must_resubmit_single_shot_timer()
        {
            await WithinAsync(TimeSpan.FromSeconds(2.5), async () =>
            {
                await WithinAsync(500.Milliseconds(), 1.Seconds(), async() =>
                {
                    FSM.Tell(FsmState.TestSingleTimerResubmit);
                    await ExpectMsgAsync(new Transition<FsmState>(FSM, FsmState.Initial, FsmState.TestSingleTimerResubmit));
                    await ExpectMsgAsync<Tick>();
                });

                await WithinAsync(1.Seconds(), async() =>
                {
                    await ExpectMsgAsync<Tock>();
                    await ExpectMsgAsync(new Transition<FsmState>(FSM, FsmState.TestSingleTimerResubmit, FsmState.Initial));
                });
                await ExpectNoMsgAsync(500.Milliseconds());
            });
        }

        [Fact]
        public async Task FSM_must_correctly_cancel_a_named_timer()
        {
            FSM.Tell(FsmState.TestCancelTimer);
            await ExpectMsgAsync(new Transition<FsmState>(FSM, FsmState.Initial, FsmState.TestCancelTimer));
            await WithinAsync(500.Milliseconds(), async() =>
            {
                FSM.Tell(Tick.Instance);
                await ExpectMsgAsync<Tick>();
            });

            await WithinAsync(300.Milliseconds(), 1.Seconds(), async() =>
            {
                await ExpectMsgAsync<Tock>();
            });
            FSM.Tell(Cancel.Instance);
            await ExpectMsgAsync(new Transition<FsmState>(FSM, FsmState.TestCancelTimer, FsmState.Initial), 1.Seconds());
        }

        [Fact]
        public async Task FSM_must_not_get_confused_between_named_and_state_timers()
        {
            FSM.Tell(FsmState.TestCancelStateTimerInNamedTimerMessage);
            FSM.Tell(Tick.Instance);
            await ExpectMsgAsync(new Transition<FsmState>(FSM, FsmState.Initial, FsmState.TestCancelStateTimerInNamedTimerMessage));
            await ExpectMsgAsync<Tick>(500.Milliseconds());
            await Task.Delay(200.Milliseconds());
            Resume(FSM);
            await ExpectMsgAsync(new Transition<FsmState>(FSM, FsmState.TestCancelStateTimerInNamedTimerMessage, FsmState.TestCancelStateTimerInNamedTimerMessage2), 500.Milliseconds());
            FSM.Tell(Cancel.Instance);
            await WithinAsync(500.Milliseconds(), async() =>
            {
                await ExpectMsgAsync<Cancel>();
                await ExpectMsgAsync(new Transition<FsmState>(FSM, FsmState.TestCancelStateTimerInNamedTimerMessage2, FsmState.Initial));
            });
        }

        [Fact]
        public async Task FSM_must_receive_and_cancel_a_repeated_timer()
        {
            FSM.Tell(FsmState.TestRepeatedTimer);
            await ExpectMsgAsync(new Transition<FsmState>(FSM, FsmState.Initial, FsmState.TestRepeatedTimer));
            var seq = await ReceiveWhileAsync(2.Seconds(), o =>
            {
                if (o is Tick)
                    return o;
                return null;
            }).ToListAsync();
            seq.Should().HaveCount(5);
            await WithinAsync(500.Milliseconds(), async() =>
            {
                await ExpectMsgAsync(new Transition<FsmState>(FSM, FsmState.TestRepeatedTimer, FsmState.Initial));
            });
        }

        [Fact]
        public async Task FSM_must_notify_unhandled_messages()
        {
            // EventFilter
            //    .Warning("unhandled event Akka.Tests.Actor.FSMTimingSpec+Tick in state TestUnhandled", source: fsm.Path.ToString())
            //    .And
            //    .Warning("unhandled event Akka.Tests.Actor.FSMTimingSpec+Unhandled in state TestUnhandled", source: fsm.Path.ToString())
            //    .ExpectOne(
            //    () =>
            //    {
            FSM.Tell(FsmState.TestUnhandled);
            await ExpectMsgAsync(new Transition<FsmState>(FSM, FsmState.Initial, FsmState.TestUnhandled));
            await WithinAsync(3.Seconds(), async() =>
            {
                FSM.Tell(Tick.Instance);
                FSM.Tell(SetHandler.Instance);
                FSM.Tell(Tick.Instance);
                (await ExpectMsgAsync<Unhandled>()).Msg.Should().BeOfType<Tick>();
                FSM.Tell(new Unhandled("test"));
                FSM.Tell(Cancel.Instance);
                var transition = await ExpectMsgAsync<Transition<FsmState>>();
                transition.FsmRef.Should().Be(FSM);
                transition.From.Should().Be(FsmState.TestUnhandled);
                transition.To.Should().Be(FsmState.Initial);
            });
            //    });
        }

        #region Actors

        static void Suspend(IActorRef actorRef)
        {
            var l = actorRef as ActorRefWithCell;
            l?.Suspend();
        }

        static void Resume(IActorRef actorRef)
        {
            var l = actorRef as ActorRefWithCell;
            l?.Resume();
        }

        public enum FsmState
        {
            Initial,
            TestStateTimeout,
            TestStateTimeoutOverride,
            TestSingleTimer,
            TestSingleTimerResubmit,
            TestRepeatedTimer,
            TestUnhandled,
            TestCancelTimer,
            TestCancelStateTimerInNamedTimerMessage,
            TestCancelStateTimerInNamedTimerMessage2,
            TestStoppingActorStateTimeout
        }

        public class Tick
        {
            private Tick() { }
            public static Tick Instance { get; } = new();
        }

        public class Tock
        {
            private Tock() { }
            public static Tock Instance { get; } = new();
        }

        public class Cancel
        {
            private Cancel() { }
            public static Cancel Instance { get; } = new();
        }

        public class SetHandler
        {
            private SetHandler() { }
            public static SetHandler Instance { get; } = new();
        }

        public class Unhandled
        {
            public Unhandled(object msg)
            {
                Msg = msg;
            }

            public object Msg { get; }
        }

        public static void StaticAwaitCond(Func<bool> evaluator, TimeSpan max, TimeSpan? interval)
        {
            InternalAwaitCondition(evaluator, max, interval, (format, args) => XAssert.Fail(string.Format(format, args)));
        }

        public class StateMachine : FSM<FsmState, int>, ILoggingFSM
        {
            public StateMachine(IActorRef tester)
            {
                Tester = tester;
                StartWith(FsmState.Initial, 0);

                When(FsmState.Initial, @event =>
                {
                    if (@event.FsmEvent is FsmState name)
                    {
                        switch (name)
                        {
                            case FsmState.TestSingleTimer:
                                SetTimer("tester", Tick.Instance, 500.Milliseconds(), false);
                                return GoTo(FsmState.TestSingleTimer);
                            case FsmState.TestRepeatedTimer:
                                SetTimer("tester", Tick.Instance, 100.Milliseconds(), true);
                                return GoTo(FsmState.TestRepeatedTimer).Using(4);
                            case FsmState.TestStateTimeoutOverride:
                                return GoTo(FsmState.TestStateTimeout).ForMax(TimeSpan.MaxValue);
                            default:
                                return GoTo(name);
                        }
                    }
                    return null;
                });

                When(FsmState.TestStateTimeout, @event =>
                {
                    if (@event.FsmEvent is StateTimeout)
                    {
                        return GoTo(FsmState.Initial);
                    }
                    else if (@event.FsmEvent is Cancel)
                    {
                        return GoTo(FsmState.Initial).Replying(Cancel.Instance);
                    }
                    return null;
                }, 800.Milliseconds());

                When(FsmState.TestSingleTimer, @event =>
                {
                    if (@event.FsmEvent is Tick)
                    {
                        Tester.Tell(Tick.Instance);
                        return GoTo(FsmState.Initial);
                    }
                    return null;
                });

                OnTransition((state1, state2) =>
                {
                    if (state1 == FsmState.Initial && state2 == FsmState.TestSingleTimerResubmit)
                        SetTimer("blah", Tick.Instance, 500.Milliseconds());
                });

                When(FsmState.TestSingleTimerResubmit, @event =>
                {
                    if (@event.FsmEvent is Tick)
                    {
                        Tester.Tell(Tick.Instance);
                        SetTimer("blah", Tock.Instance, 500.Milliseconds());
                        return Stay();
                    }
                    else if (@event.FsmEvent is Tock)
                    {
                        Tester.Tell(Tock.Instance);
                        return GoTo(FsmState.Initial);
                    }
                    return null;
                });

                When(FsmState.TestCancelTimer, @event =>
                {
                    if (@event.FsmEvent is Tick)
                    {
                        var contextLocal = Context.AsInstanceOf<ActorCell>();
                        SetTimer("hallo", Tock.Instance, 1.Milliseconds());
                        StaticAwaitCond(() => contextLocal.Mailbox.HasMessages, 1.Seconds(), 50.Milliseconds());
                        CancelTimer("hallo");
                        Sender.Tell(Tick.Instance);
                        SetTimer("hallo", Tock.Instance, 500.Milliseconds());
                        return Stay();
                    }
                    else if (@event.FsmEvent is Tock)
                    {
                        Tester.Tell(Tock.Instance);
                        return Stay();
                    }
                    else if (@event.FsmEvent is Cancel)
                    {
                        CancelTimer("hallo");
                        return GoTo(FsmState.Initial);
                    }
                    return null;
                });

                When(FsmState.TestRepeatedTimer, @event =>
                {
                    if (@event.FsmEvent is Tick)
                    {
                        var remaining = @event.StateData;
                        Tester.Tell(Tick.Instance);
                        if (remaining == 0)
                        {
                            CancelTimer("tester");
                            return GoTo(FsmState.Initial);
                        }
                        else
                        {
                            return Stay().Using(remaining - 1);
                        }
                    }
                    return null;
                });

                When(FsmState.TestCancelStateTimerInNamedTimerMessage, @event =>
                {
                    // FSM is suspended after processing this message and resumed 500s later
                    if (@event.FsmEvent is Tick)
                    {
                        Suspend(Self);
                        SetTimer("named", Tock.Instance, 1.Milliseconds());
                        var contextLocal = Context.AsInstanceOf<ActorCell>();
                        StaticAwaitCond(() => contextLocal.Mailbox.HasMessages, 1.Seconds(), 50.Milliseconds());
                        return Stay().ForMax(1.Milliseconds()).Replying(Tick.Instance);
                    }
                    else if (@event.FsmEvent is Tock)
                    {
                        return GoTo(FsmState.TestCancelStateTimerInNamedTimerMessage2);
                    }
                    return null;
                });

                When(FsmState.TestCancelStateTimerInNamedTimerMessage2, @event =>
                {
                    if (@event.FsmEvent is StateTimeout)
                    {
                        return GoTo(FsmState.Initial);
                    }
                    else if (@event.FsmEvent is Cancel)
                    {
                        return GoTo(FsmState.Initial).Replying(Cancel.Instance);
                    }
                    return null;
                });

                When(FsmState.TestUnhandled, @event =>
                {
                    if (@event.FsmEvent is SetHandler)
                    {
                        WhenUnhandled(evt =>
                        {
                            if (evt.FsmEvent is Tick)
                            {
                                Tester.Tell(new Unhandled(Tick.Instance));
                                return Stay();
                            }

                            return null;
                        });
                        return Stay();
                    }
                    else if (@event.FsmEvent is Cancel)
                    {
                        // whenUnhandled(NullFunction)
                        return GoTo(FsmState.Initial);
                    }
                    return null;
                });
            }

            public IActorRef Tester { get; }
        }

        public class StoppingActor : FSM<FsmState, int>
        {
            public StoppingActor()
            {
                StartWith(FsmState.Initial, 0);

                When(FsmState.Initial, evt =>
                {
                    if (evt.FsmEvent is FsmState state)
                    {
                        if (state == FsmState.TestStoppingActorStateTimeout)
                        {
                            Context.Stop(Self);
                            return Stay();
                        }
                    }
                    return null;
                }, 200.Milliseconds());
            }
        }

        #endregion
    }
}
