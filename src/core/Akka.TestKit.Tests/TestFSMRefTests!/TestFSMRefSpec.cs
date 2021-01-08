//-----------------------------------------------------------------------
// <copyright file="TestFSMRefSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using Akka.Actor;
using Akka.TestKit;
using Xunit;

namespace Akka.Testkit.Tests.TestFSMRefTests
{
    public class TestFSMRefSpec : AkkaSpec
    {
        [Fact]
        public void A_TestFSMRef_must_allow_access_to_internal_state()
        {
            var fsm = ActorOfAsTestFSMRef<StateTestFsm, int, string>("test-fsm-ref-1");

            fsm.StateName.ShouldBe(1);
            fsm.StateData.ShouldBe("");

            fsm.Tell("go");
            fsm.StateName.ShouldBe(2);
            fsm.StateData.ShouldBe("go");

            fsm.SetState(1);
            fsm.StateName.ShouldBe(1);
            fsm.StateData.ShouldBe("go");

            fsm.SetStateData("buh");
            fsm.StateName.ShouldBe(1);
            fsm.StateData.ShouldBe("buh");

            fsm.SetStateTimeout(TimeSpan.FromMilliseconds(100));
            Within(TimeSpan.FromMilliseconds(80), TimeSpan.FromMilliseconds(500), () =>
                AwaitCondition(() => fsm.StateName == 2 && fsm.StateData == "timeout")
                );
        }

        [Fact]
        public void A_TestFSMRef_must_allow_access_to_timers()
        {
            var fsm = ActorOfAsTestFSMRef<TimerTestFsm, int, object>("test-fsm-ref-2");
            fsm.IsTimerActive("test").ShouldBe(false);
            fsm.SetTimer("test", 12, TimeSpan.FromMilliseconds(10), true);
            fsm.IsTimerActive("test").ShouldBe(true);
            fsm.CancelTimer("test");
            fsm.IsTimerActive("test").ShouldBe(false);
        }

        private class StateTestFsm : FSM<int, string>
        {
            public StateTestFsm()
            {
                StartWith(1, "");
                When(1, e =>
                {
                    var fsmEvent = e.FsmEvent;
                    if(Equals(fsmEvent, "go"))
                        return GoTo(2, "go");
                    if(fsmEvent is StateTimeout)
                        return GoTo(2, "timeout");
                    return null;
                });
                When(2, e =>
                {
                    var fsmEvent = e.FsmEvent;
                    if(Equals(fsmEvent, "back"))
                        return GoTo(1, "back");
                    return null;
                });
            }
        }
        private class TimerTestFsm : FSM<int, object>
        {
            public TimerTestFsm()
            {
                StartWith(1, null);
                When(1, e => Stay());
            }
        }
    }
}

