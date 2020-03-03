//-----------------------------------------------------------------------
// <copyright file="TestProbeSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Akka.Actor;
using Akka.TestKit.TestActors;
using Akka.Util.Internal;
using Xunit;

namespace Akka.TestKit.Tests.TestActorRefTests
{
    public class TestProbeSpec : AkkaSpec
    {
        [Fact]
        public void TestProbe_should_equal_underlying_Ref()
        {
            var p = CreateTestProbe();
            p.Equals(p.Ref).ShouldBeTrue();
            p.Ref.Equals(p).ShouldBeTrue();
            var hs = new HashSet<IActorRef> {p, p.Ref};
            hs.Count.ShouldBe(1);
        }

        /// <summary>
        /// Should be able to receive a <see cref="Terminated"/> message from a <see cref="TestProbe"/>
        /// if we're deathwatching it and it terminates.
        /// </summary>
        [Fact]
        public void TestProbe_should_send_Terminated_when_killed()
        {
            var p = CreateTestProbe();
            Watch(p);
            Sys.Stop(p);
            ExpectTerminated(p);
        }

        /// <summary>
        /// If we deathwatch the underlying actor ref or TestProbe itself, it shouldn't matter.
        /// 
        /// They should be equivalent either way.
        /// </summary>
        [Fact]
        public void TestProbe_underlying_Ref_should_be_equivalent_to_TestProbe()
        {
            var p = CreateTestProbe();
            Watch(p.Ref);
            Sys.Stop(p);
            ExpectTerminated(p);
        }

        /// <summary>
        /// Should be able to receive a <see cref="Terminated"/> message from a <see cref="TestProbe.Ref"/>
        /// if we're deathwatching it and it terminates.
        /// </summary>
        [Fact]
        public void TestProbe_underlying_Ref_should_send_Terminated_when_killed()
        {
            var p = CreateTestProbe();
            Watch(p.Ref);
            Sys.Stop(p.Ref);
            ExpectTerminated(p.Ref);
        }

        [Fact]
        public void TestProbe_should_create_a_child_when_invoking_ChildActorOf()
        {
            var probe = CreateTestProbe();
            var child = probe.ChildActorOf(Props.Create<EchoActor>());
            child.Path.Parent.ShouldBe(probe.Ref.Path);
            var namedChild = probe.ChildActorOf<EchoActor>("actorName");
            namedChild.Path.Name.ShouldBe("actorName");
        }

        [Fact]
        public void TestProbe_restart_a_failing_child_if_the_given_supervisor_says_so()
        {
            var restarts = new AtomicCounter(0);
            var probe = CreateTestProbe();
            var child = probe.ChildActorOf(Props.Create(() => new FailingActor(restarts)), SupervisorStrategy.DefaultStrategy);
            AwaitAssert(() =>
            {
                child.Tell("hello");
                restarts.Current.ShouldBeGreaterThan(1);
            });
        }
        
        class FailingActor : ActorBase
        {
            private AtomicCounter Restarts { get; }
            
            public FailingActor(AtomicCounter restarts)
            {
                Restarts = restarts;
            }
            
            protected override bool Receive(object message)
            {
                throw new Exception("Simulated failure");
            }

            protected override void PostRestart(Exception reason)
            {
                Restarts.IncrementAndGet();
            }
        }
    }
}
