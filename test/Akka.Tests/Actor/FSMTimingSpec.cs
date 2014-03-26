using System;
using Akka.Actor;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Akka.Tests.Actor
{
    [TestClass]
    public class FSMTimingSpec : AkkaSpec, ImplicitSender
    {
        public ActorRef Self { get { return testActor; } }

        #region Actors

        private void Suspend(ActorRef actorRef)
        {
            actorRef.Match()
                .With<ActorRefWithCell>(l => l.Suspend());
        }

        private void Resume(ActorRef actorRef)
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
            TestCancelStateTimer,
            TestCancelStateTimer2
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
                        .With<State>(s =>
                        {
                            if (s == State.TestStateTimeout) nextState = GoTo(State.Initial);
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
                            GoTo(State.Initial);
                        });

                    return nextState;
                });

                When(State.TestCancelTimer, @event =>
                {
                    State<State, int> nextState = null;

                    @event.FsmEvent.Match()
                        .With<Tick>(async tick =>
                        {
                            SetTimer("hallo", new Tock(), TimeSpan.FromMilliseconds(1));
                            await AwaitCond(() => Context.AsInstanceOf<ActorCell>().Mailbox.HasUnscheduledMessages, TimeSpan.FromSeconds(1));
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
            }



            public ActorRef Tester { get; private set; }
        }

        #endregion
    }
}
