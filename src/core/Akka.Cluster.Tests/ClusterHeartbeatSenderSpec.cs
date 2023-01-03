//-----------------------------------------------------------------------
// <copyright file="ClusterHeartbeatSenderSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2022 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Immutable;
using System.Threading.Tasks;
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
    public class ClusterHeartbeatSenderSpec : ClusterHeartbeatSenderBase
    {
        public ClusterHeartbeatSenderSpec(ITestOutputHelper output) : base(output, false)
        {
        }
    }
    
    public class ClusterHeartbeatSenderLegacySpec : ClusterHeartbeatSenderBase
    {
        public ClusterHeartbeatSenderLegacySpec(ITestOutputHelper output) : base(output, true)
        {
        }
    }
    
    public abstract class ClusterHeartbeatSenderBase : AkkaSpec
    {
        class TestClusterHeartbeatSender : ClusterHeartbeatSender
        {
            private readonly TestProbe _probe;

            public TestClusterHeartbeatSender(TestProbe probe) : base(Cluster.Get(Context.System))
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

        private static Config Config(bool useLegacyHeartbeat) => $@"
            akka.loglevel = DEBUG
            akka.actor.provider = cluster
            akka.cluster.failure-detector.heartbeat-interval = 0.2s
            akka.cluster.use-legacy-heartbeat-message = {(useLegacyHeartbeat ? "true" : "false")}
        ";

        protected ClusterHeartbeatSenderBase(ITestOutputHelper output, bool useLegacyMessage)
            : base(Config(useLegacyMessage), output){ }

        [Fact]
        public async Task ClusterHeartBeatSender_must_increment_heartbeat_SeqNo()
        {
            var probe = CreateTestProbe();
            var underTest = Sys.ActorOf(Props.Create(() => new TestClusterHeartbeatSender(probe)));

            underTest.Tell(new ClusterEvent.CurrentClusterState());
            underTest.Tell(new ClusterEvent.MemberUp(new Member(
                new UniqueAddress(new Address("akka", Sys.Name), 1), 1, 
                MemberStatus.Up, ImmutableHashSet<string>.Empty, AppVersion.Zero)));

            (await probe.ExpectMsgAsync<Heartbeat>()).SequenceNr.Should().Be(1L);
            (await probe.ExpectMsgAsync<Heartbeat>()).SequenceNr.Should().Be(2L);
        }
    }
}
