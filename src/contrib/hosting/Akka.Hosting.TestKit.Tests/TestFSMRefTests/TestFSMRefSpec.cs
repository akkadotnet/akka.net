//-----------------------------------------------------------------------
// <copyright file="TestFSMRefSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Akka.Actor;
using FluentAssertions;
using Xunit;

namespace Akka.Hosting.TestKit.Tests.TestFSMRefTests;

public class TestFSMRefSpec : TestKit
{
    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
    {
        
    }
    
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
        fsm.SetStateTimeout(TimeSpan.FromMilliseconds(100));
        await AwaitConditionAsync(() => Task.FromResult(fsm is { StateName: 2, StateData: "timeout" }),
            Dilated(TimeSpan.FromSeconds(5)));
        elapsed.Elapsed.Should().BeGreaterOrEqualTo(TimeSpan.FromMilliseconds(80));
    }

    [Fact]
    public void A_TestFSMRef_must_allow_access_to_timers()
    {
        var fsm = ActorOfAsTestFSMRef<TimerTestFsm, int, object>("test-fsm-ref-2");
        fsm.IsTimerActive("test").Should().Be(false);
        fsm.SetTimer("test", 12, TimeSpan.FromMilliseconds(10), true);
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
            StartWith(1, "");
            When(1, e => Stay());
        }
    }
}