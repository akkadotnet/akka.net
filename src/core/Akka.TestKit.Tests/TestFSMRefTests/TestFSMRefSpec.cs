//-----------------------------------------------------------------------
// <copyright file="TestFSMRefSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Diagnostics;
using System.Threading.Tasks;
using Akka.Actor;
using Xunit;
using FluentAssertions;
using FluentAssertions.Extensions;

namespace Akka.TestKit.Tests.TestFSMRefTests
{
    public class TestFSMRefSpec : AkkaSpec
    {
        [Fact]
        public async Task A_TestFSMRef_must_allow_access_to_internal_state()
        {
            var fsm = ActorOfAsTestFSMRef<StateTestFsm, int, string>("test-fsm-ref-1");

            fsm.StateName.Should().Be(1);
            fsm.StateData.Should().Be("");

            fsm.Tell("go");
            fsm.StateName.Should().Be(2);
            fsm.StateData.Should().Be("go");

            fsm.SetState(1);
            fsm.StateName.Should().Be(1);
            fsm.StateData.Should().Be("go");

            fsm.SetStateData("buh");
            fsm.StateName.Should().Be(1);
            fsm.StateData.Should().Be("buh");

            // Timed from before SetStateTimeout, so a stall before the wait cannot make the timeout look early.
            // The upper bound is generous on purpose: the timeout travels through the scheduler and the
            // dispatcher, and a starved CI agent can take seconds.
            var elapsed = Stopwatch.StartNew();
            fsm.SetStateTimeout(100.Milliseconds());
            await AwaitConditionAsync(() => Task.FromResult(fsm.StateName == 2 && fsm.StateData == "timeout"),
                Dilated(5.Seconds()));
            elapsed.Elapsed.Should().BeGreaterOrEqualTo(80.Milliseconds());
        }

        [Fact]
        public void A_TestFSMRef_must_allow_access_to_timers()
        {
            var fsm = ActorOfAsTestFSMRef<TimerTestFsm, int, object>("test-fsm-ref-2");
            fsm.IsTimerActive("test").Should().Be(false);
            fsm.SetTimer("test", 12, 10.Milliseconds(), true);
            fsm.IsTimerActive("test").Should().Be(true);
            fsm.CancelTimer("test");
            fsm.IsTimerActive("test").Should().Be(false);
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
                        return GoTo(2).Using("go");
                    if(fsmEvent is StateTimeout)
                        return GoTo(2).Using("timeout");
                    return null;
                });
                
                When(2, e =>
                {
                    var fsmEvent = e.FsmEvent;
                    if(Equals(fsmEvent, "back"))
                        return GoTo(1).Using("back");
                    return null;
                });
            }
        }
        private class TimerTestFsm : FSM<int, object>
        {
            public TimerTestFsm()
            {
                StartWith(1, null);
                When(1, _ => Stay());
            }
        }
    }
}

