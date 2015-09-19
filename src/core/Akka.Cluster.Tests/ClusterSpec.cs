//-----------------------------------------------------------------------
// <copyright file="ClusterSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2015 Typesafe Inc. <http://www.typesafe.com>
//     Copyright (C) 2013-2015 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.TestKit;
using Akka.Util.Internal;
using Xunit;

namespace Akka.Cluster.Tests
{
    public class ClusterSpec : AkkaSpec
    {
        const string Config = @"    
        akka.cluster {
            auto-down-unreachable-after = 0s
            periodic-tasks-initial-delay = 120 s // turn off scheduled tasks
            publish-stats-interval = 0 s # always, when it happens
        }
        akka.actor.provider = ""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
        akka.remote.helios.tcp.port = 0";

        public IActorRef Self { get { return TestActor; } }

        readonly Address _selfAddress;
        readonly Cluster _cluster;
        
        public ClusterReadView ClusterView { get { return _cluster.ReadView; } }

        public ClusterSpec()
            : base(Config)
        {
            _selfAddress = Sys.AsInstanceOf<ExtendedActorSystem>().Provider.DefaultAddress;
            _cluster = Cluster.Get(Sys);
        }

        public void LeaderActions()
        {
            _cluster.ClusterCore.Tell(InternalClusterAction.LeaderActionsTick.Instance);
        }

        [Fact]
        public void AClusterMustUseTheAddressOfTheRemoteTransport()
        {
            Assert.Equal(_selfAddress, _cluster.SelfAddress);
        }

        [Fact]
        public void AClusterMustInitiallyBecomeSingletonClusterWhenJoiningItselfAndReachConvergence()
        {
            Assert.Equal(0, ClusterView.Members.Count);
            _cluster.Join(_selfAddress);
            LeaderActions(); // Joining -> Up
            AwaitCondition(() => ClusterView.IsSingletonCluster);
            Assert.Equal(_selfAddress, ClusterView.Self.Address);
            Assert.Equal(ImmutableHashSet.Create(_selfAddress), ClusterView.Members.Select(m => m.Address).ToImmutableHashSet());
            AwaitAssert(() => Assert.Equal(MemberStatus.Up, ClusterView.Status));
        }

        [Fact]
        public void AClusterMustPublishInitialStateAsSnapshotToSubscribers()
        {
            try
            {
                _cluster.Subscribe(TestActor, ClusterEvent.InitialStateAsSnapshot, new []{typeof(ClusterEvent.IMemberEvent)});
                ExpectMsg<ClusterEvent.CurrentClusterState>();
            }
            finally
            {
                _cluster.Unsubscribe(TestActor);
            }
        }

        [Fact]
        public void AClusterMustPublishInitialStateAsEventsToSubscribers()
        {
            try
            {
                _cluster.Join(_selfAddress);
                LeaderActions(); // Joining -> Up
                _cluster.Subscribe(TestActor, ClusterEvent.InitialStateAsEvents, new[] { typeof(ClusterEvent.IMemberEvent) });
                ExpectMsg<ClusterEvent.MemberUp>();
            }
            finally
            {
                _cluster.Unsubscribe(TestActor);
            }
        }

        [Fact]
        public void AClusterMustSendCurrentClusterStateToOneReceiverWhenRequested()
        {
            _cluster.SendCurrentClusterState(TestActor);
            ExpectMsg<ClusterEvent.CurrentClusterState>();
        }

        // this should be the last test step, since the cluster is shutdown
        [Fact]
        public void AClusterMustPublishMemberRemovedWhenShutdown()
        {
            _cluster.Join(_selfAddress);
            LeaderActions(); // Joining -> Up
            _cluster.Subscribe(TestActor, new []{typeof(ClusterEvent.MemberRemoved)});
            // first, is in response to the subscription
            ExpectMsg<ClusterEvent.CurrentClusterState>();

            _cluster.Shutdown();
            var memberRemoved = ExpectMsg<ClusterEvent.MemberRemoved>();
            Assert.Equal(_selfAddress, memberRemoved.Member.Address);
        }
    }
}

