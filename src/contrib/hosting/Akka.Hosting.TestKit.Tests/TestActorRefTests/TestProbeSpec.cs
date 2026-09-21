//-----------------------------------------------------------------------
// <copyright file="TestProbeSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.TestKit;
using Akka.TestKit.TestActors;
using Akka.Util.Internal;
using FluentAssertions;
using Xunit;

namespace Akka.Hosting.TestKit.Tests.TestActorRefTests
{
    public class TestProbeSpec : TestKit
    {
        [Fact]
        public void TestProbe_should_equal_underlying_Ref()
        {
            var p = CreateTestProbe();
            p.Equals(p.Ref).Should().BeTrue();
            p.Ref.Equals(p).Should().BeTrue();
            var hs = new HashSet<IActorRef> {p, p.Ref};
            hs.Count.Should().Be(1);
        }

        protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider)
        {
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
            child.Path.Parent.Should().Be(probe.Ref.Path);
            var namedChild = probe.ChildActorOf<EchoActor>("actorName");
            namedChild.Path.Name.Should().Be("actorName");
        }

        [Fact]
        public async Task TestProbe_restart_a_failing_child_if_the_given_supervisor_says_so()
        {
            var probe = CreateTestProbe();
            var restartWatcher = CreateTestProbe();
            var child = probe.ChildActorOf(Props.Create(() => new FailingActor(restartWatcher)), SupervisorStrategy.DefaultStrategy);

            // Send two messages that will cause failures and restarts
            child.Tell("hello");
            child.Tell("hello");

            // Wait for exactly 2 restart notifications
            await restartWatcher.ExpectMsgAsync("restarted");
            await restartWatcher.ExpectMsgAsync("restarted");
        }

        class FailingActor : ActorBase
        {
            private readonly IActorRef _restartWatcher;

            public FailingActor(IActorRef restartWatcher)
            {
                _restartWatcher = restartWatcher;
            }

            protected override bool Receive(object message)
            {
                throw new Exception("Simulated failure");
            }

            protected override void PostRestart(Exception reason)
            {
                _restartWatcher.Tell("restarted");
                base.PostRestart(reason);
            }
        }
    }
}
