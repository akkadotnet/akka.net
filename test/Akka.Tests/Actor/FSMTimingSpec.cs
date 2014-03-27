using System;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Akka.Tests.Actor
{
    [TestClass]
    public class FSMTimingSpec : AkkaSpec, ImplicitSender
    {
        public ActorRef Self { get { return testActor; } }

        public ActorRef _fsm;

        public ActorRef fsm
        {
            get { return _fsm ?? (_fsm = sys.ActorOf(Props.Create(() => new StateMachine(Self)))); }
        }

        [TestInitialize]
        public override void Setup()
        {
            base.Setup();
            //initializes the Finite State Machine, so it doesn't affect any of the time-sensitive tests below
            fsm.Tell(new FSMBase.SubscribeTransitionCallBack(Self));
            expectMsg(new FSMBase.CurrentState<State>(fsm, State.Initial), FSMSpecHelpers.CurrentStateExpector<State>(), TimeSpan.FromSeconds(1));
        }

        [TestMethod]
        public void FSM_must_receive_StateTimeout()
        {
            //arrange

            //act
            Within(TimeSpan.FromSeconds(1), () =>
            {
                Within(TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1), () =>
                {
                    fsm.Tell(State.TestStateTimeout, Self);
                    expectMsg(new FSMBase.Transition<State>(fsm, State.Initial, State.TestStateTimeout), FSMSpecHelpers.TransitionStateExpector<State>());
                    expectMsg(new FSMBase.Transition<State>(fsm, State.TestStateTimeout, State.Initial), FSMSpecHelpers.TransitionStateExpector<State>());
                    return true;
                });
                expectNoMsg(TimeSpan.FromMilliseconds(50));
                return true;
            });

            //assert
        }

        [TestMethod]
        public void FSM_must_cancel_a_StateTimeout()
        {
            //arrange

            //act
            Within(TimeSpan.FromSeconds(1), () =>
            {
                fsm.Tell(State.TestStateTimeout, Self);
                fsm.Tell(new Cancel(), Self);
                expectMsg(new FSMBase.Transition<State>(fsm, State.Initial, State.TestStateTimeout), FSMSpecHelpers.TransitionStateExpector<State>());
                expectMsgType<Cancel>();
                expectMsg(new FSMBase.Transition<State>(fsm, State.TestStateTimeout, State.Initial), FSMSpecHelpers.TransitionStateExpector<State>());
                expectNoMsg(TimeSpan.FromMilliseconds(50));
                return true;
            });

            //assert
        }

        [TestMethod]
        public void FSM_must_allow_StateTimeout_override()
        {
            //arrange

            //act
            //the timeout in state TestStateTieout is 800ms, then it will change back to Initial
            Within(TimeSpan.FromMilliseconds(400), () =>
            {
                fsm.Tell(State.TestStateTimeoutOverride, Self);
                expectMsg(new FSMBase.Transition<State>(fsm, State.Initial, State.TestStateTimeout), FSMSpecHelpers.TransitionStateExpector<State>());
                expectNoMsg(TimeSpan.FromMilliseconds(300));
                return true;
            });

            Within(TimeSpan.FromSeconds(1), () =>
            {
                fsm.Tell(new Cancel(), Self);
                expectMsgType<Cancel>();
                expectMsg(new FSMBase.Transition<State>(fsm, State.TestStateTimeout, State.Initial),
                    FSMSpecHelpers.TransitionStateExpector<State>());
                return true;
            });

            //assert
        }

        [TestMethod]
        public void FSM_must_receive_single_shot_timer()
        {
            //arrange

            //act
            Within(TimeSpan.FromSeconds(2), () =>
            {
                Within(TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1), () =>
                {
                    fsm.Tell(State.TestSingleTimer, Self);
                    expectMsg(new FSMBase.Transition<State>(fsm, State.Initial, State.TestSingleTimer), FSMSpecHelpers.TransitionStateExpector<State>());
                    expectMsgType<Tick>();
                    expectMsg(new FSMBase.Transition<State>(fsm, State.TestSingleTimer, State.Initial),
                        FSMSpecHelpers.TransitionStateExpector<State>());
                    return true;
                });
                expectNoMsg(TimeSpan.FromMilliseconds(500));
                return true;
            });

            //assert
        }

        [TestMethod]
        public void FSM_must_resubmit_single_shot_timer()
        {
            Within(TimeSpan.FromSeconds(2.5), () =>
            {
                Within(TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1), () =>
                {
                    fsm.Tell(State.TestSingleTimerResubmit, Self);
                    expectMsg(new FSMBase.Transition<State>(fsm, State.Initial, State.TestSingleTimerResubmit), FSMSpecHelpers.TransitionStateExpector<State>());
                    expectMsgType<Tick>();
                    return true;
                });

                Within(TimeSpan.FromSeconds(1), () =>
                {
                    expectMsgType<Tock>();
                    expectMsg(new FSMBase.Transition<State>(fsm, State.TestSingleTimerResubmit, State.Initial),
                        FSMSpecHelpers.TransitionStateExpector<State>());
                    return true;
                });
                expectNoMsg(TimeSpan.FromMilliseconds(500));
                return true;
            });
        }

        [TestMethod]
        public void FSM_must_correctly_cancel_a_named_timer()
        {
            fsm.Tell(State.TestCancelTimer, Self);
            expectMsg(new FSMBase.Transition<State>(fsm, State.Initial, State.TestCancelTimer), FSMSpecHelpers.TransitionStateExpector<State>());
            Within(TimeSpan.FromMilliseconds(500), () =>
            {
                fsm.Tell(new Tick(), Self);
                expectMsgType<Tick>();
                return true;
            });

            Within(TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(1), () =>
            {
                expectMsgType<Tock>();
                return true;
            });
            fsm.Tell(new Cancel(), Self);
            expectMsg(new FSMBase.Transition<State>(fsm, State.TestCancelTimer, State.Initial),
                FSMSpecHelpers.TransitionStateExpector<State>(), TimeSpan.FromSeconds(1));
        }

        [TestMethod]
        public void FSM_must_not_get_confused_between_named_and_state_timers()
        {
            fsm.Tell(State.TestCancelStateTimerInNamedTimerMessage, Self);
            fsm.Tell(new Tick(), Self);
            expectMsg(new FSMBase.Transition<State>(fsm, State.Initial, State.TestCancelStateTimerInNamedTimerMessage), FSMSpecHelpers.TransitionStateExpector<State>());
            expectMsgType<Tick>(TimeSpan.FromMilliseconds(500));
            Task.Delay(TimeSpan.FromMilliseconds(200));
            Resume(fsm);
            expectMsg(new FSMBase.Transition<State>(fsm, State.TestCancelStateTimerInNamedTimerMessage, State.TestCancelStateTimerInNamedTimerMessage2),
                FSMSpecHelpers.TransitionStateExpector<State>(), TimeSpan.FromMilliseconds(500));
            fsm.Tell(new Cancel(), Self);
            Within(TimeSpan.FromMilliseconds(500), () =>
            {
                expectMsgType<Cancel>();
                expectMsg(
                    new FSMBase.Transition<State>(fsm, State.TestCancelStateTimerInNamedTimerMessage2, State.Initial),
                    FSMSpecHelpers.TransitionStateExpector<State>());
                return true;
            });
        }

        [TestMethod]
        public void FSM_must_receive_and_cancel_a_repeated_timer()
        {
            fsm.Tell(State.TestRepeatedTimer, Self);
            expectMsg(new FSMBase.Transition<State>(fsm, State.Initial, State.TestRepeatedTimer),
                FSMSpecHelpers.TransitionStateExpector<State>());
            var seq = receiveWhile(TimeSpan.FromSeconds(300), o =>
            {
                if (o is Tick) return o;
                return null;
            });

            Assert.AreEqual(5, seq.Count);
            Within(TimeSpan.FromMilliseconds(500), () =>
            {
                expectMsg(new FSMBase.Transition<State>(fsm, State.TestRepeatedTimer, State.Initial),
                    FSMSpecHelpers.TransitionStateExpector<State>());
                return true;
            });
        }

        #region Actors

        static void Suspend(ActorRef actorRef)
        {
            actorRef.Match()
                .With<ActorRefWithCell>(l => l.Suspend());
        }

        static void Resume(ActorRef actorRef)
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
            TestUnandled,
            TestCancelTimer,
            TestCancelStateTimerInNamedTimerMessage,
            TestCancelStateTimerInNamedTimerMessage2
        }

        public class Tick { }
        public class Tock { }
        public class Cancel { }
        public class SetHandler { }

        public class Unhandled
        {
            public Unhandled(object msg)
            {
                Msg = msg;
            }

            public object Msg { get; private set; }
        }

        public class StateMachine : FSM<State, int>
        {
            public StateMachine(ActorRef tester)
            {
                Tester = tester;
                StartWith(State.Initial, 0);
                When(State.Initial, @event =>
                {
                    State<State, int> nextState = null;
                    @event.FsmEvent.Match()
                        .With<State>(s =>
                        {
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
                        });
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
                    @event.FsmEvent.Match()
                        .With<Tick>(t =>
                        {
                            Tester.Tell(new Tick());
                            nextState = GoTo(State.Initial);
                        });
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
                            SetTimer("hallo", new Tock(), TimeSpan.FromMilliseconds(1));
                            var contextLocal = Context;
                            AwaitCond(() => contextLocal.AsInstanceOf<ActorCell>().Mailbox.HasUnscheduledMessages, TimeSpan.FromSeconds(1)).Wait();
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

                    @event.FsmEvent.Match()
                        .With<Tick>(tick =>
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
                        });

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
                            AwaitCond(() => contextLocal.AsInstanceOf<ActorCell>().Mailbox.HasUnscheduledMessages,
                                    TimeSpan.FromSeconds(1)).Wait();
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

                When(State.TestUnandled, @event =>
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



            public ActorRef Tester { get; private set; }
        }

        #endregion
    }
}
