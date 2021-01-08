//-----------------------------------------------------------------------
// <copyright file="FSMTransitionSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.TestKit;
using FluentAssertions;
using Xunit;
using static Akka.Actor.FSMBase;

namespace Akka.Tests.Actor
{
    public class FSMTransitionSpec : AkkaSpec
    {
        [Fact]
        public void FSMTransitionNotifier_must_not_trigger_onTransition_for_stay()
        {
            var fsm = Sys.ActorOf(Props.Create(() => new SendAnyTransitionFSM(TestActor)));
            ExpectMsg((0, 0)); // caused by initialize(), OK.
            fsm.Tell("stay"); // no transition event
            ExpectNoMsg(500.Milliseconds());
            fsm.Tell("goto"); // goto(current state)
            ExpectMsg((0, 0));
        }

        [Fact]
        public void FSMTransitionNotifier_must_notify_listeners()
        {
            var fsm = Sys.ActorOf(Props.Create(() => new MyFSM(TestActor)));

            Within(1.Seconds(), () =>
            {
                fsm.Tell(new SubscribeTransitionCallBack(TestActor));
                ExpectMsg(new CurrentState<int>(fsm, 0));
                fsm.Tell("tick");
                ExpectMsg(new Transition<int>(fsm, 0, 1));
                fsm.Tell("tick");
                ExpectMsg(new Transition<int>(fsm, 1, 0));
            });
        }

        [Fact]
        public void FSMTransitionNotifier_must_not_fail_when_listener_goes_away()
        {
            var forward = Sys.ActorOf(Props.Create(() => new Forwarder(TestActor)));
            var fsm = Sys.ActorOf(Props.Create(() => new MyFSM(TestActor)));

            Within(1.Seconds(), () =>
            {
                fsm.Tell(new SubscribeTransitionCallBack(forward));
                ExpectMsg(new CurrentState<int>(fsm, 0));
                forward.GracefulStop(5.Seconds()).Wait();
                fsm.Tell("tick");
                ExpectNoMsg(200.Milliseconds());
            });
        }

        [Fact]
        public void FSM_must_make_previous_and_next_state_data_available_in_OnTransition()
        {
            var fsm = Sys.ActorOf(Props.Create(() => new OtherFSM(TestActor)));

            Within(1.Seconds(), () =>
            {
                fsm.Tell("tick");
                ExpectMsg((0, 1));
            });
        }

        [Fact]
        public void FSM_must_trigger_transition_event_when_goto_the_same_state()
        {
            var forward = Sys.ActorOf(Props.Create(() => new Forwarder(TestActor)));
            var fsm = Sys.ActorOf(Props.Create(() => new OtherFSM(TestActor)));

            Within(1.Seconds(), () =>
            {
                fsm.Tell(new SubscribeTransitionCallBack(forward));
                ExpectMsg(new CurrentState<int>(fsm, 0));
                fsm.Tell("tick");
                ExpectMsg((0, 1));
                ExpectMsg(new Transition<int>(fsm, 0, 1));
                fsm.Tell("tick");
                ExpectMsg((1, 1));
                ExpectMsg(new Transition<int>(fsm, 1, 1));
            });
        }

        [Fact]
        public void FSM_must_not_trigger_transition_event_on_stay()
        {
            var forward = Sys.ActorOf(Props.Create(() => new Forwarder(TestActor)));
            var fsm = Sys.ActorOf(Props.Create(() => new OtherFSM(TestActor)));

            Within(1.Seconds(), () =>
            {
                fsm.Tell(new SubscribeTransitionCallBack(forward));
                ExpectMsg(new CurrentState<int>(fsm, 0));
                fsm.Tell("stay");
                ExpectNoMsg(500.Milliseconds());
            });
        }

        [Fact]
        public void FSM_must_not_leak_memory_in_nextState()
        {
            var fsmref = Sys.ActorOf<LeakyFSM>();

            fsmref.Tell("switch");
            ExpectMsg((0, 1));
            fsmref.Tell("test");
            ExpectMsg("ok");
        }

        #region Test actors

        public class SendAnyTransitionFSM : FSM<int, int>
        {
            public SendAnyTransitionFSM(IActorRef target)
            {
                Target = target;

                StartWith(0, 0);

                When(0, @event =>
                {
                    if (@event.FsmEvent.Equals("stay"))
                        return Stay();
                    else
                        return GoTo(0);
                });

                OnTransition((state1, state2) =>
                {
                    Target.Tell((state1, state2));
                });

                Initialize();
            }

            public IActorRef Target { get; }
        }

        public class MyFSM : FSM<int, object>
        {
            public MyFSM(IActorRef target)
            {
                Target = target;

                StartWith(0, new object());

                When(0, @event =>
                {
                    if (@event.FsmEvent.Equals("tick"))
                        return GoTo(1);
                    return null;
                });

                When(1, @event =>
                {
                    if (@event.FsmEvent.Equals("tick"))
                        return GoTo(0);
                    return null;
                });

                WhenUnhandled(@event =>
                {
                    if (@event.FsmEvent.Equals("reply"))
                        return Stay().Replying("reply");
                    return null;
                });

                Initialize();
            }

            public IActorRef Target { get; }

            protected override void PreRestart(Exception reason, object message)
            {
                Target.Tell("restarted");
            }
        }

        public class OtherFSM : FSM<int, int>
        {
            public OtherFSM(IActorRef target)
            {
                Target = target;

                StartWith(0, 0);

                When(0, @event =>
                {
                    if (@event.FsmEvent.Equals("tick"))
                    {
                        return GoTo(1).Using(1);
                    }
                    else if (@event.FsmEvent.Equals("stay"))
                    {
                        return Stay();
                    }
                    return null;
                });

                When(1, @event => GoTo(1));

                OnTransition((state1, state2) =>
                {
                    if (state1 == 0 && state2 == 1)
                        target.Tell((StateData, NextStateData));

                    if (state1 == 1 && state2 == 1)
                        target.Tell((StateData, NextStateData));
                });
            }

            public IActorRef Target { get; private set; }
        }

        public class LeakyFSM : FSM<int, IActorRef>
        {
            public LeakyFSM()
            {
                StartWith(0, null);

                When(0, @event =>
                {
                    if (@event.FsmEvent.Equals("switch"))
                    {
                        return GoTo(1).Using(Sender);
                    }

                    return null;
                });

                OnTransition((state1, state2) =>
                {
                    NextStateData.Tell((state1, state2));
                });

                When(1, @event =>
                {
                    if (@event.FsmEvent.Equals("test"))
                    {
                        try
                        {
                            Sender.Tell($"failed: {NextStateData}");
                        }
                        catch (InvalidOperationException)
                        {
                            Sender.Tell("ok");
                        }

                        return Stay();
                    }
                    return null;
                });
            }
        }

        public class Forwarder : UntypedActor
        {
            public Forwarder(IActorRef target)
            {
                Target = target;
            }

            public IActorRef Target { get; }

            protected override void OnReceive(object message)
            {
                Target.Tell(message);
            }
        }

        #endregion
    }
}

