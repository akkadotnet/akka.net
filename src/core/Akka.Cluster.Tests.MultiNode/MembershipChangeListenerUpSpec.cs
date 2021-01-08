//-----------------------------------------------------------------------
// <copyright file="MembershipChangeListenerUpSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Remote.TestKit;
using Akka.TestKit;

namespace Akka.Cluster.Tests.MultiNode
{
    public class MembershipChangeListenerUpConfig : MultiNodeConfig
    {
        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }

        public MembershipChangeListenerUpConfig()
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");

            CommonConfig = DebugConfig(false)
                .WithFallback(MultiNodeClusterSpec.ClusterConfigWithFailureDetectorPuppet());
        }
    }

    public class MembershipChangeListenerUpSpec : MultiNodeClusterSpec
    {
        private class Listener : ReceiveActor
        {
            private readonly TestLatch _latch;
            private readonly ImmutableList<Address> _expectedAddresses;
            private ImmutableSortedSet<Member> _members = ImmutableSortedSet<Member>.Empty;

            public Listener(TestLatch latch, ImmutableList<Address> expectedAddresses)
            {
                _latch = latch;
                _expectedAddresses = expectedAddresses;

                Receive<ClusterEvent.CurrentClusterState>(state =>
                {
                    _members = state.Members;
                });

                Receive<ClusterEvent.MemberUp>(m =>
                {
                    _members = _members.Remove(m.Member).Add(m.Member);

                    if (!_members.Select(c => c.Address).Except(_expectedAddresses).Any())
                        _latch.CountDown();
                });
            }
        }

        private readonly MembershipChangeListenerUpConfig _config;

        public MembershipChangeListenerUpSpec() : this(new MembershipChangeListenerUpConfig())
        {
        }

        protected MembershipChangeListenerUpSpec(MembershipChangeListenerUpConfig config) : base(config, typeof(MembershipChangeListenerUpSpec))
        {
            _config = config;
        }

        [MultiNodeFact]
        public void MembershipChangeListenerUpSpecs()
        {
            Set_of_connected_cluster_systems_must_when_two_nodes_after_cluster_convergence_updates_membership_table_then_all_MembershipChangeListeners_should_be_triggered();
            Set_of_connected_cluster_systems_must_when_three_nodes_after_cluster_convergence_updates_membership_table_then_all_MembershipChangeListeners_should_be_triggered();
        }

        public void Set_of_connected_cluster_systems_must_when_two_nodes_after_cluster_convergence_updates_membership_table_then_all_MembershipChangeListeners_should_be_triggered()
        {
            AwaitClusterUp(_config.First);

            RunOn(() =>
            {
                var latch = new TestLatch();
                var expectedAddresses = ImmutableList.Create(GetAddress(_config.First), GetAddress(_config.Second));
                var listener = Sys.ActorOf(Props.Create(() => new Listener(latch, expectedAddresses)).WithDeploy(Deploy.Local));
                Cluster.Subscribe(listener, new[] { typeof(ClusterEvent.IMemberEvent) });
                EnterBarrier("listener-1-registered");
                Cluster.Join(GetAddress(_config.First));
                latch.Ready();
            }, _config.First, _config.Second);

            RunOn(() =>
            {
                EnterBarrier("listener-1-registered");
            }, _config.Third);

            EnterBarrier("after-1");
        }

        public void Set_of_connected_cluster_systems_must_when_three_nodes_after_cluster_convergence_updates_membership_table_then_all_MembershipChangeListeners_should_be_triggered()
        {
            var latch = new TestLatch();
            var expectedAddresses = ImmutableList.Create(GetAddress(_config.First), GetAddress(_config.Second), GetAddress(_config.Third));
            var listener = Sys.ActorOf(Props.Create(() => new Listener(latch, expectedAddresses)).WithDeploy(Deploy.Local));
            Cluster.Subscribe(listener, new[] { typeof(ClusterEvent.IMemberEvent) });
            EnterBarrier("listener-2-registered");

            RunOn(() =>
            {
                Cluster.Join(GetAddress(_config.First));
            }, _config.Third);

            latch.Ready();

            EnterBarrier("after-2");
        }
    }
}
