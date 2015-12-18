//-----------------------------------------------------------------------
// <copyright file="FSMTimingSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit;
using Akka.Util.Internal;
using Xunit;

namespace Akka.Tests.Actor
{

    public class FSMTimingSpec : AkkaSpec
    {
        public IActorRef Self { get { return TestActor; } }

        public IActorRef _fsm;

        public IActorRef fsm
        {
            get { return _fsm ?? (_fsm = Sys.ActorOf(Props.Create(() => new StateMachine(Self)), "fsm")); }
        }

        public FSMTimingSpec()
            //: base("akka.test.test-actor.dispatcher.type=Dispatcher" + FullDebugConfig)
            //: base("akka.test.test-actor.dispatcher.type=Dispatcher" + FullDebugConfig)
            //: base(FullDebugConfig)
        {
            //initializes the Finite State Machine, so it doesn't affect any of the time-sensitive tests below
            fsm.Tell(new FSMBase.SubscribeTransitionCallBack(Self));
            ExpectMsg(new FSMBase.CurrentState<State>(fsm, State.Initial), FSMSpecHelpers.CurrentStateExpector<State>(), TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void FSM_must_receive_StateTimeout()
        {
            //arrange

            //act
            Within(TimeSpan.FromSeconds(1), () =>
            {
                Within(TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1), () =>
                {
                    fsm.Tell(State.TestStateTimeout, Self);
                    ExpectMsg(new FSMBase.Transition<State>(fsm, State.Initial, State.TestStateTimeout), FSMSpecHelpers.TransitionStateExpector<State>());
                    ExpectMsg(new FSMBase.Transition<State>(fsm, State.TestStateTimeout, State.Initial), FSMSpecHelpers.TransitionStateExpector<State>());
                    return true;
                });
                ExpectNoMsg(TimeSpan.FromMilliseconds(50));
                return true;
            });

            //assert
        }

        [Fact]
        public void FSM_must_cancel_a_StateTimeout()
        {
            //arrange

            //act
            Within(TimeSpan.FromSeconds(1), () =>
            {
                fsm.Tell(State.TestStateTimeout, Self);
                fsm.Tell(new Cancel(), Self);
                ExpectMsg(new FSMBase.Transition<State>(fsm, State.Initial, State.TestStateTimeout), FSMSpecHelpers.TransitionStateExpector<State>());
                ExpectMsg<Cancel>();
                ExpectMsg(new FSMBase.Transition<State>(fsm, State.TestStateTimeout, State.Initial), FSMSpecHelpers.TransitionStateExpector<State>());
                ExpectNoMsg(TimeSpan.FromMilliseconds(50));
                return true;
            });

            //assert
        }

        [Fact]
        public void FSM_must_allow_StateTimeout_override()
        {
            //arrange

            //act
            //the timeout in state TestStateTimeout is 800ms, then it will change back to Initial
            Within(TimeSpan.FromMilliseconds(400), () =>
            {
                fsm.Tell(State.TestStateTimeoutOverride, Self);
                ExpectMsg(new FSMBase.Transition<State>(fsm, State.Initial, State.TestStateTimeout), FSMSpecHelpers.TransitionStateExpector<State>());
                ExpectNoMsg(TimeSpan.FromMilliseconds(300));
                return true;
            });

            Within(TimeSpan.FromSeconds(1), () =>
            {
                fsm.Tell(new Cancel(), Self);
                ExpectMsg<Cancel>();
                ExpectMsg(new FSMBase.Transition<State>(fsm, State.TestStateTimeout, State.Initial), FSMSpecHelpers.TransitionStateExpector<State>());
                return true;
            });

            //assert
        }

        [Fact]
        public void FSM_must_receive_single_shot_timer()
        {
            //arrange

            //act
            Within(TimeSpan.FromSeconds(2), () =>
            {
                Within(TimeSpan.FromMilliseconds(450), TimeSpan.FromSeconds(1), () =>
                {
                    fsm.Tell(State.TestSingleTimer, Self);
                    ExpectMsg(new FSMBase.Transition<State>(fsm, State.Initial, State.TestSingleTimer), FSMSpecHelpers.TransitionStateExpector<State>());
                    ExpectMsg<Tick>();
                    ExpectMsg(new FSMBase.Transition<State>(fsm, State.TestSingleTimer, State.Initial), FSMSpecHelpers.TransitionStateExpector<State>());
                    return true;
                });
                ExpectNoMsg(TimeSpan.FromMilliseconds(500));
                return true;
            });

            //assert
        }

        [Fact]
        public void FSM_must_resubmit_single_shot_timer()
        {
            Within(TimeSpan.FromSeconds(2.5), () =>
            {
                Within(TimeSpan.FromMilliseconds(450), TimeSpan.FromSeconds(1), () =>
                {
                    fsm.Tell(State.TestSingleTimerResubmit, Self);
                    ExpectMsg(new FSMBase.Transition<State>(fsm, State.Initial, State.TestSingleTimerResubmit), FSMSpecHelpers.TransitionStateExpector<State>());
                    ExpectMsg<Tick>();
                    return true;
                });

                Within(TimeSpan.FromSeconds(1), () =>
                {
                    ExpectMsg<Tock>();
                    ExpectMsg(new FSMBase.Transition<State>(fsm, State.TestSingleTimerResubmit, State.Initial), FSMSpecHelpers.TransitionStateExpector<State>());
                    return true;
                });
                ExpectNoMsg(TimeSpan.FromMilliseconds(500));
                return true;
            });
        }

        [Fact]
        public void FSM_must_correctly_cancel_a_named_timer()
        {
            fsm.Tell(State.TestCancelTimer, Self);
            ExpectMsg(new FSMBase.Transition<State>(fsm, State.Initial, State.TestCancelTimer), FSMSpecHelpers.TransitionStateExpector<State>());
            Within(TimeSpan.FromMilliseconds(1500), () =>
            {
                fsm.Tell(new Tick(), Self);
                ExpectMsg<Tick>();
                return true;
            });

            Within(TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(1), () =>
            {
                ExpectMsg<Tock>();
                return true;
            });
            fsm.Tell(new Cancel(), Self);
            ExpectMsg(new FSMBase.Transition<State>(fsm, State.TestCancelTimer, State.Initial), FSMSpecHelpers.TransitionStateExpector<State>(), TimeSpan.FromSeconds(1));
        }

        [Fact]
        public void FSM_must_not_get_confused_between_named_and_state_timers()
        {
            fsm.Tell(State.TestCancelStateTimerInNamedTimerMessage, Self);
            fsm.Tell(new Tick(), Self);
            ExpectMsg(new FSMBase.Transition<State>(fsm, State.Initial, State.TestCancelStateTimerInNamedTimerMessage), FSMSpecHelpers.TransitionStateExpector<State>());
            ExpectMsg<Tick>(TimeSpan.FromMilliseconds(500));
            Task.Delay(TimeSpan.FromMilliseconds(200));
            Resume(fsm);
            ExpectMsg(new FSMBase.Transition<State>(fsm, State.TestCancelStateTimerInNamedTimerMessage, State.TestCancelStateTimerInNamedTimerMessage2), FSMSpecHelpers.TransitionStateExpector<State>(), TimeSpan.FromMilliseconds(500));
            fsm.Tell(new Cancel(), Self);
            Within(TimeSpan.FromMilliseconds(500), () =>
            {
                ExpectMsg<Cancel>();
                ExpectMsg(new FSMBase.Transition<State>(fsm, State.TestCancelStateTimerInNamedTimerMessage2, State.Initial), FSMSpecHelpers.TransitionStateExpector<State>());
                return true;
            });
        }

        /// <summary>
        /// receiveWhile is currently broken
        /// </summary>
        [Fact]
        public void FSM_must_receive_and_cancel_a_repeated_timer()
        {
            fsm.Tell(State.TestRepeatedTimer, Self);
            ExpectMsg(new FSMBase.Transition<State>(fsm, State.Initial, State.TestRepeatedTimer), FSMSpecHelpers.TransitionStateExpector<State>());
            var seq = ReceiveWhile(TimeSpan.FromSeconds(2), o =>
            {
                if(o is Tick) return o;
                return null;
            });

            Assert.Equal(5, seq.Count);
            Within(TimeSpan.FromMilliseconds(500), () =>
            {
                ExpectMsg(new FSMBase.Transition<State>(fsm, State.TestRepeatedTimer, State.Initial), FSMSpecHelpers.TransitionStateExpector<State>());
                return true;
            });
        }

        #region Actors

        static void Suspend(IActorRef actorRef)
        {
            actorRef.Match()
                .With<ActorRefWithCell>(l => l.Suspend());
        }

        static void Resume(IActorRef actorRef)
        {
            actorRef.Match()
                .With<ActorRefWithCell>(l => l.Resume());
        }

        public enum State
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
            TestCancelStateTimerInNamedTimerMessage2
        }

        public abstract class DebugName
        {
            public override string ToString()
            {
                return GetType().Name;
            }
        }
        public class Tick : DebugName { }
        public class Tock : DebugName { }
        public class Cancel : DebugName { }
        public class SetHandler : DebugName { }

        public class Unhandled : DebugName
        {
            public Unhandled(object msg)
            {
                Msg = msg;
            }

            public object Msg { get; private set; }
        }


        public static void StaticAwaitCond(Func<bool> evaluator, TimeSpan max, TimeSpan? interval)
        {
            InternalAwaitCondition(evaluator, max, interval,(format,args)=> XAssert.Fail(string.Format(format,args)));
        }


        public class StateMachine : FSM<State, int>, ILoggingFSM
        {
            public StateMachine(IActorRef tester)
            {
                Tester = tester;
                StartWith(State.Initial, 0);
                When(State.Initial, @event =>
                {
                    State<State, int> nextState = null;
                    if (@event.FsmEvent is State)
                    {
                        var s = (State) @event.FsmEvent;
                        switch (s)
                        {
                            case State.TestSingleTimer:
                                SetTimer("tester", new Tick(), TimeSpan.FromMilliseconds(500), false);
                                nextState = GoTo(State.TestSingleTimer);
                                break;
                            case State.TestRepeatedTimer:
                                SetTimer("tester", new Tick(), TimeSpan.FromMilliseconds(100), true);
                                nextState = GoTo(State.TestRepeatedTimer).Using(4);
                                break;
                            case State.TestStateTimeoutOverride:
                                nextState = GoTo(State.TestStateTimeout).ForMax(TimeSpan.MaxValue);
                                break;
                            default:
                                nextState = GoTo(s);
                                break;
                        }
                    }
                    return nextState;
                });

                When(State.TestStateTimeout, @event =>
                {
                    State<State, int> nextState = null;
                    @event.FsmEvent.Match()
                        .With<StateTimeout>(s =>
                        {
                            nextState = GoTo(State.Initial);
                        })
                        .With<Cancel>(c =>
                        {
                            nextState = GoTo(State.Initial).Replying(new Cancel());
                        });
                    return nextState;

                }, TimeSpan.FromMilliseconds(800));

                When(State.TestSingleTimer, @event =>
                {
                    State<State, int> nextState = null;
                    if (@event.FsmEvent is Tick)
                    {
                        Tester.Tell(new Tick());
                        nextState = GoTo(State.Initial);
                    }
                    return nextState;
                });

                OnTransition((state, state1) =>
                {
                    if(state == State.Initial && state1 == State.TestSingleTimerResubmit)
                        SetTimer("blah", new Tick(), TimeSpan.FromMilliseconds(500));
                });

                When(State.TestSingleTimerResubmit, @event =>
                {
                    State<State, int> nextState = null;
                    @event.FsmEvent.Match()
                        .With<Tick>(tick =>
                        {
                            Tester.Tell(new Tick());
                            SetTimer("blah", new Tock(), TimeSpan.FromMilliseconds(500));
                            nextState = Stay();
                        })
                        .With<Tock>(tock =>
                        {
                            Tester.Tell(new Tock());
                            nextState = GoTo(State.Initial);
                        });

                    return nextState;
                });

                When(State.TestCancelTimer, @event =>
                {
                    State<State, int> nextState = null;

                    @event.FsmEvent.Match()
                        .With<Tick>(tick =>
                        {
                            var contextLocal = Context;
                            var numberOfMessages = ((ActorCell) contextLocal).Mailbox.NumberOfMessages;
                            SetTimer("hallo", new Tock(), TimeSpan.FromMilliseconds(1));
                            StaticAwaitCond(() => contextLocal.AsInstanceOf<ActorCell>().Mailbox.HasUnscheduledMessages, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(50));
                            CancelTimer("hallo");
                            Sender.Tell(new Tick());
                            SetTimer("hallo", new Tock(), TimeSpan.FromMilliseconds(500));
                            nextState = Stay();
                        })
                        .With<Tock>(tock =>
                        {
                            Tester.Tell(new Tock());
                            nextState = Stay();
                        })
                        .With<Cancel>(c =>
                        {
                            CancelTimer("hallo");
                            nextState = GoTo(State.Initial);
                        });

                    return nextState;
                });

                When(State.TestRepeatedTimer, @event =>
                {
                    State<State, int> nextState = null;

                    if (@event.FsmEvent is Tick)
                    {
                        var remaining = @event.StateData;
                        Tester.Tell(new Tick());
                        if (remaining == 0)
                        {
                            CancelTimer("tester");
                            nextState = GoTo(State.Initial);
                        }
                        else
                        {
                            nextState = Stay().Using(remaining - 1);
                        }
                    }

                    return nextState;
                });

                When(State.TestCancelStateTimerInNamedTimerMessage, @event =>
                {
                    //FSM is suspended after processing this message and resumed 500s later
                    State<State, int> nextState = null;

                    @event.FsmEvent.Match()
                        .With<Tick>(tick =>
                        {
                            Suspend(Self);
                            SetTimer("named", new Tock(), TimeSpan.FromMilliseconds(1));
                            var contextLocal = Context;
                            StaticAwaitCond(() =>((ActorCell) contextLocal).Mailbox.HasUnscheduledMessages,
                                    TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(50));
                            nextState = Stay().ForMax(TimeSpan.FromMilliseconds(1)).Replying(new Tick());
                        })
                        .With<Tock>(tock =>
                        {
                            nextState = GoTo(State.TestCancelStateTimerInNamedTimerMessage2);
                        });

                    return nextState;
                });

                When(State.TestCancelStateTimerInNamedTimerMessage2, @event =>
                {
                    State<State, int> nextState = null;

                    @event.FsmEvent.Match()
                        .With<StateTimeout>(s =>
                        {
                            nextState = GoTo(State.Initial);
                        })
                        .With<Cancel>(c =>
                        {
                            nextState = GoTo(State.Initial).Replying(new Cancel());
                        });

                    return nextState;
                });

                When(State.TestUnhandled, @event =>
                {
                    State<State, int> nextState = null;

                    @event.FsmEvent.Match()
                        .With<SetHandler>(s =>
                        {
                            WhenUnhandled(@event1 =>
                            {
                                Tester.Tell(new Unhandled(new Tick()));
                                return Stay();
                            });
                            nextState = Stay();
                        })
                        .With<Cancel>(c =>
                        {
                            WhenUnhandled(@event1 => null);
                            nextState = GoTo(State.Initial);
                        });

                    return nextState;
                });
            }



            public IActorRef Tester { get; private set; }
        }

        #endregion
    }
}

