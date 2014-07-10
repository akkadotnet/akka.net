using System;
using Akka.Actor;
using Akka.TestKit;
using Xunit;

namespace Akka.Tests.Actor
{
    
    public class FSMTransitionSpec : AkkaSpec, ImplicitSender
    {
        public ActorRef Self { get { return testActor; } }

        
            
        [Fact]
        public void FSMTransitionNotifier_must_notify_listeners()
        {
            //arrange
            var fsm = sys.ActorOf(Props.Create(() => new MyFSM(testActor)));

            //act
            Within(TimeSpan.FromSeconds(1), () =>
            {
                fsm.Tell(new FSMBase.SubscribeTransitionCallBack(testActor));
                expectMsg(new FSMBase.CurrentState<int>(fsm, 0), FSMSpecHelpers.CurrentStateExpector<int>());
                fsm.Tell("tick");
                expectMsg(new FSMBase.Transition<int>(fsm, 0, 1), FSMSpecHelpers.TransitionStateExpector<int>());
                fsm.Tell("tick");
                expectMsg(new FSMBase.Transition<int>(fsm, 1, 0), FSMSpecHelpers.TransitionStateExpector<int>());
                return true;
            });

            //assert
        }

        [Fact]
        public void FSMTransitionNotifier_must_not_fail_when_listener_goes_away()
        {
            //arrange
            var forward = sys.ActorOf(Props.Create(() => new Forwarder(testActor)));
            var fsm = sys.ActorOf(Props.Create(() => new MyFSM(testActor)));

            //act
            Within(TimeSpan.FromSeconds(1), async () =>
            {
                fsm.Tell(new FSMBase.SubscribeTransitionCallBack(forward));
                expectMsg(new FSMBase.CurrentState<int>(fsm, 0), FSMSpecHelpers.CurrentStateExpector<int>());
                await forward.GracefulStop(TimeSpan.FromSeconds(5));
                fsm.Tell("tick");
                expectNoMsg(TimeSpan.FromMilliseconds(300));
                return true;
            });

            //assert
        }

        [Fact]
        public void FSM_must_make_previous_and_next_state_data_available_in_OnTransition()
        {
            //arrange
            var fsm = sys.ActorOf(Props.Create(() => new OtherFSM(testActor)));

            //act
            Within(TimeSpan.FromSeconds(1), () =>
            {
                fsm.Tell("tick");
                expectMsg(new Tuple<int, int>(0, 1));
                return true;
            });

            //assert
        }

        [Fact]
        public void FSM_must_not_leak_memory_in_nextState()
        {
            //arrange
            var fsmref = sys.ActorOf<LeakyFSM>();

            //act
            fsmref.Tell("switch", Self);
            expectMsg(Tuple.Create(0, 1));
            fsmref.Tell("test", Self);
            expectMsg("ok");

            //assert
        }

        #region Test actors

        public class MyFSM : FSM<int, object>
        {
            public MyFSM(ActorRef target)
            {
                Target = target;
                StartWith(0, new object());
                When(0, @event =>
                {
                    if (@event.FsmEvent.Equals("tick")) return GoTo(1);
                    return null;
                });

                When(1, @event =>
                {
                    if (@event.FsmEvent.Equals("tick")) return GoTo(0);
                    return null;
                });

                WhenUnhandled(@event =>
                {
                    if (@event.FsmEvent.Equals("reply")) return Stay().Replying("reply");
                    return null;
                });

                Initialize();
            }

            public ActorRef Target { get; private set; }

            protected override void PreRestart(Exception reason, object message)
            {
                Target.Tell("restarted");
            }
        }

        public class OtherFSM : FSM<int, int>
        {
            public OtherFSM(ActorRef target)
            {
                Target = target;
                StartWith(0, 0);
                When(0, @event =>
                {
                    if (@event.FsmEvent.Equals("tick"))
                    {
                        return GoTo(1).Using(1);
                    }
                    return null;
                });

                When(1, @event => Stay());

                OnTransition((state, i) =>
                {
                    if (state == 0 && i == 1) target.Tell(Tuple.Create(StateData, NextStateData));
                });
            }

            public ActorRef Target { get; private set; }
        }

        public class LeakyFSM : FSM<int, ActorRef>
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

                OnTransition((state, i) =>
                {
                    NextStateData.Tell(Tuple.Create(state, i));
                });

                When(1, @event =>
                {
                    if (@event.FsmEvent.Equals("test"))
                    {
                        try
                        {
                            Sender.Tell(string.Format("failed: {0}", NextStateData));
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
            public Forwarder(ActorRef target)
            {
                Target = target;
            }

            public ActorRef Target { get; private set; }

            protected override void OnReceive(object message)
            {
                Target.Tell(message);
            }
        }

        #endregion
    }
}
