//-----------------------------------------------------------------------
// <copyright file="ClusterHeartbeatSenderSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Immutable;
using Akka.Actor;
using Akka.Configuration;
using Akka.TestKit;
using Akka.Util;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using static Akka.Cluster.ClusterHeartbeatSender;

namespace Akka.Cluster.Tests
{
    public class ClusterHeartbeatSenderSpec : AkkaSpec
    {
        class TestClusterHeartbeatSender : ClusterHeartbeatSender
        {
            private readonly TestProbe _probe;

            public TestClusterHeartbeatSender(TestProbe probe)
            {
                _probe = probe;
            }

            protected override void PreStart()
            {
                // don't register for cluster events
            }

            protected override ActorSelection HeartbeatReceiver(Address address)
            {
                return Context.ActorSelection(_probe.Ref.Path);
            }
        }

        public static readonly Config Config = @"
            akka.loglevel = DEBUG
            akka.actor.provider = cluster
            akka.cluster.failure-detector.heartbeat-interval = 0.2s
        ";

        public ClusterHeartbeatSenderSpec(ITestOutputHelper output)
            : base(Config, output){ }

        [Fact]
        public void ClusterHeartBeatSender_must_increment_heartbeat_SeqNo()
        {
            var probe = CreateTestProbe();
            var underTest = Sys.ActorOf(Props.Create(() => new TestClusterHeartbeatSender(probe)));

            underTest.Tell(new ClusterEvent.CurrentClusterState());
            underTest.Tell(new ClusterEvent.MemberUp(new Member(
                new UniqueAddress(new Address("akka", Sys.Name), 1), 1, 
                MemberStatus.Up, ImmutableHashSet<string>.Empty, AppVersion.Zero)));

            probe.ExpectMsg<Heartbeat>().SequenceNr.Should().Be(1L);
            probe.ExpectMsg<Heartbeat>().SequenceNr.Should().Be(2L);
        }
    }
}
