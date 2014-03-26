using System;
using Akka.Actor;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Akka.Tests.Actor
{
    public static class FSMSpecHelpers
    {
        public static Func<object, object, bool> CurrentStateExpector = (expected, actual) =>
        {
            var expectedFsmState = expected.AsInstanceOf<FSMStates.CurrentState<int>>();
            var actualFsmState = actual.AsInstanceOf<FSMStates.CurrentState<int>>();
            return expectedFsmState.FsmRef.Equals(actualFsmState.FsmRef) &&
                   expectedFsmState.State == actualFsmState.State;
        };

        public static Func<object, object, bool> TransitionStateExpector = (expected, actual) =>
        {
            var expectedFsmState = expected.AsInstanceOf<FSMStates.Transition<int>>();
            var actualFsmState = actual.AsInstanceOf<FSMStates.Transition<int>>();
            return expectedFsmState.FsmRef.Equals(actualFsmState.FsmRef) &&
                   expectedFsmState.To == actualFsmState.To &&
                   expectedFsmState.From == actualFsmState.From;
        };
    }

    [TestClass]
    public class FSMTransitionSpec : AkkaSpec, ImplicitSender
    {
        public ActorRef Self { get { return testActor; } }

        
            
        [TestMethod]
        public void FSMTransitionNotifier_must_notify_listeners()
        {
            //arrange
            var fsm = sys.ActorOf(Props.Create(() => new MyFSM(testActor)));

            //act
            Within(TimeSpan.FromSeconds(1), () =>
            {
                fsm.Tell(new FSMStates.SubscribeTransitionCallBack(testActor));
                expectMsg(new FSMStates.CurrentState<int>(fsm, 0), FSMSpecHelpers.CurrentStateExpector);
                fsm.Tell("tick");
                expectMsg(new FSMStates.Transition<int>(fsm, 0, 1), FSMSpecHelpers.TransitionStateExpector);
                fsm.Tell("tick");
                expectMsg(new FSMStates.Transition<int>(fsm, 1, 0), FSMSpecHelpers.TransitionStateExpector);
                return true;
            });

            //assert
        }

        [TestMethod]
        public void FSMTransitionNotifier_must_not_fail_when_listener_goes_away()
        {
            //arrange
            var forward = sys.ActorOf(Props.Create(() => new Forwarder(testActor)));
            var fsm = sys.ActorOf(Props.Create(() => new MyFSM(testActor)));

            //act
            Within(TimeSpan.FromSeconds(1), () =>
            {
                fsm.Tell(new FSMStates.SubscribeTransitionCallBack(forward));
                expectMsg(new FSMStates.CurrentState<int>(fsm, 0), FSMSpecHelpers.CurrentStateExpector);
                forward.Stop();
                fsm.Tell("tick");
                expectNoMsg(TimeSpan.FromMilliseconds(30));
                return true;
            });

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
