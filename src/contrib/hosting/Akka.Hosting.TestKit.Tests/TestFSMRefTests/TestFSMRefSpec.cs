//-----------------------------------------------------------------------
// <copyright file="TestFSMRefSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
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
    public void A_TestFSMRef_must_allow_access_to_internal_state()
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

        fsm.SetStateTimeout(TimeSpan.FromMilliseconds(100));
        Within(TimeSpan.FromMilliseconds(80), TimeSpan.FromMilliseconds(500), () =>
            AwaitCondition(() => fsm is { StateName: 2, StateData: "timeout" })
        );
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