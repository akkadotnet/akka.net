//-----------------------------------------------------------------------
// <copyright file="ClusterSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Configuration;
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
        public void A_cluster_must_use_the_address_of_the_remote_transport()
        {
            Assert.Equal(_selfAddress, _cluster.SelfAddress);
        }

        [Fact]
        public void A_cluster_must_initially_become_singleton_cluster_when_joining_itself_and_reach_convergence()
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
        public void A_cluster_must_publish_initial_state_as_snapshot_to_subscribers()
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
        public void A_cluster_must_publish_initial_state_as_events_to_subscribers()
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
        public void A_cluster_must_send_current_cluster_state_to_one_receiver_when_requested()
        {
            _cluster.SendCurrentClusterState(TestActor);
            ExpectMsg<ClusterEvent.CurrentClusterState>();
        }

        // this should be the last test step, since the cluster is shutdown
        [Fact]
        public void A_cluster_must_publish_member_removed_when_shutdown()
        {
            _cluster.Join(_selfAddress);
            LeaderActions(); // Joining -> Up

            var callbackProbe = CreateTestProbe();
            _cluster.RegisterOnMemberRemoved(() =>
            {
                callbackProbe.Tell("OnMemberRemoved");
            });

            _cluster.Subscribe(TestActor, new []{typeof(ClusterEvent.MemberRemoved)});
            // first, is in response to the subscription
            ExpectMsg<ClusterEvent.CurrentClusterState>();

            _cluster.Shutdown();
            var memberRemoved = ExpectMsg<ClusterEvent.MemberRemoved>();
            Assert.Equal(_selfAddress, memberRemoved.Member.Address);

            callbackProbe.ExpectMsg("OnMemberRemoved");
        }

        // TODO: https://github.com/akkadotnet/akka.net/issues/1983
        [Fact(Skip = "fails for now - will need to implement https://github.com/akkadotnet/akka.net/issues/1983")]
        public void A_cluster_must_be_allowed_to_join_and_leave_with_local_address()
        {
            var sys2 = ActorSystem.Create("ClusterSpec2", ConfigurationFactory.ParseString(@"akka.actor.provider = ""Akka.Cluster.ClusterActorRefProvider, Akka.Cluster""
        akka.remote.helios.tcp.port = 0"));

            try
            {
                var ref2 = sys2.ActorOf(Props.Empty);
                Cluster.Get(sys2).Join(ref2.Path.Address); // address doesn't contain full address information
                Within(TimeSpan.FromSeconds(5), () =>
                {
                    AwaitAssert(() =>
                    {
                        Cluster.Get(sys2).State.Members.Count.ShouldBe(1);
                        Cluster.Get(sys2).State.Members.First().Status.ShouldBe(MemberStatus.Up);
                    });
                });

                Cluster.Get(sys2).Leave(ref2.Path.Address);

                Within(TimeSpan.FromSeconds(5), () =>
                {
                    AwaitAssert(() =>
                    {
                        Cluster.Get(sys2).IsTerminated.ShouldBe(true);
                    });
                });
            }
            finally
            {
                Shutdown(sys2);
            }
        }
    }
}

