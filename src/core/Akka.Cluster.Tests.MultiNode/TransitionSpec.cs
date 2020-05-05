//-----------------------------------------------------------------------
// <copyright file="TransitionSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.Remote.TestKit;
using FluentAssertions;

namespace Akka.Cluster.Tests.MultiNode
{
    public class TransitionSpecConfig : MultiNodeConfig
    {
        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }

        public TransitionSpecConfig()
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");

            CommonConfig = DebugConfig(false)
                .WithFallback(ConfigurationFactory.ParseString(@"
                  akka.cluster.periodic-tasks-initial-delay = 300s
                  akka.cluster.publish-stats-interval = 0s
                "))
                .WithFallback(MultiNodeClusterSpec.ClusterConfigWithFailureDetectorPuppet());
        }
    }

    public class TransitionSpec : MultiNodeClusterSpec
    {
        private readonly TransitionSpecConfig _config;

        public TransitionSpec() : this(new TransitionSpecConfig())
        {
        }

        protected TransitionSpec(TransitionSpecConfig config) : base(config, typeof(TransitionSpec))
        {
            _config = config;
        }

        private RoleName Leader(params RoleName[] roles)
        {
            // sorts the addresses and provides the address of the node with the lowest port number
            // as that node will be the leader
            return roles.Select(x => (x, GetAddress(x).Port)).OrderBy(x => x.Item2).First().Item1;
        }

        private RoleName[] NonLeader(params RoleName[] roles)
        {
            return roles.Skip(1).ToArray();
        }

        private MemberStatus MemberStatus(Address address)
        {
            var status = ClusterView.Members.Union(ClusterView.UnreachableMembers)
                .Where(m => m.Address == address)
                .Select(m => m.Status)
                .ToList();

            if (status.Any())
            {
                return status.First();
            }
            else
            {
                return Akka.Cluster.MemberStatus.Removed;
            }
        }

        private ImmutableHashSet<Address> MemberAddresses()
        {
            return ClusterView.Members.Select(c => c.Address).ToImmutableHashSet();
        }

        private ImmutableHashSet<RoleName> Members()
        {
            return MemberAddresses().Select(RoleName).ToImmutableHashSet();
        }

        private ImmutableHashSet<RoleName> SeenLatestGossip()
        {
            return ClusterView.SeenBy.Select(RoleName).ToImmutableHashSet();
        }

        private void AwaitSeen(params Address[] addresses)
        {
            AwaitAssert(() =>
            {
                SeenLatestGossip().Select(GetAddress).Should().BeEquivalentTo(addresses.ToImmutableHashSet());
            });
        }

        private void AwaitMembers(params Address[] addresses)
        {
            AwaitAssert(() =>
            {
                ClusterView.RefreshCurrentState();
                MemberAddresses().Should().BeEquivalentTo(addresses.ToImmutableHashSet());
            });
        }

        private void AwaitMemberStatus(Address address, MemberStatus status)
        {
            AwaitAssert(() =>
            {
                ClusterView.RefreshCurrentState();
                MemberStatus(address).Should().Be(status);
            });
        }

        private void LeaderActions()
        {
            Cluster.ClusterCore.Tell(InternalClusterAction.LeaderActionsTick.Instance);
        }

        private void ReapUnreachable()
        {
            Cluster.ClusterCore.Tell(InternalClusterAction.ReapUnreachableTick.Instance);
        }

        private int _gossipBarrierCounter = 0;
        private void GossipTo(RoleName fromRole, RoleName toRole)
        {
            _gossipBarrierCounter++;

            RunOn(() =>
            {
                var oldCount = ClusterView.LatestStats.GossipStats.ReceivedGossipCount;
                EnterBarrier("before-gossip-" + _gossipBarrierCounter);
                AwaitCondition(() => ClusterView.LatestStats.GossipStats.ReceivedGossipCount != oldCount); // received gossip
                AwaitCondition(() => ImmutableHashSet.Create(fromRole, toRole).Except(SeenLatestGossip()).IsEmpty);
                EnterBarrier("after-gossip-" + _gossipBarrierCounter);
            }, toRole);

            RunOn(() =>
            {
                EnterBarrier("before-gossip-" + _gossipBarrierCounter);
                // send gossip
                Cluster.ClusterCore.Tell(new InternalClusterAction.SendGossipTo(GetAddress(toRole)));
                // gossip chat will synchronize the views
                AwaitCondition(() => ImmutableHashSet.Create(fromRole, toRole).Except(SeenLatestGossip()).IsEmpty);
                EnterBarrier("after-gossip-" + _gossipBarrierCounter);
            }, fromRole);

            RunOn(() =>
            {
                EnterBarrier("before-gossip-" + _gossipBarrierCounter);
                EnterBarrier("after-gossip-" + _gossipBarrierCounter);
            }, Roles.Where(r => r != fromRole && r != toRole).ToArray());
        }

        [MultiNodeFact]
        public void TransitionSpecs()
        {
            A_Cluster_must_start_nodes_as_singleton_clusters();
            A_Cluster_must_perform_correct_transitions_when_second_joining_first();
            A_Cluster_must_perform_correct_transitions_when_third_joins_second();
            A_Cluster_must_perform_correct_transitions_when_second_becomes_unavailable();
        }

        private void A_Cluster_must_start_nodes_as_singleton_clusters()
        {
            RunOn(() =>
            {
                Cluster.Join(GetAddress(Myself));

                // first joining itself will immediately be moved to Up
                AwaitMemberStatus(GetAddress(Myself), Akka.Cluster.MemberStatus.Up);
                AwaitCondition(() => ClusterView.IsSingletonCluster);
            }, _config.First);

            EnterBarrier("after-1");
        }

        private void A_Cluster_must_perform_correct_transitions_when_second_joining_first()
        {
            RunOn(() =>
            {
                Cluster.Join(GetAddress(_config.First));
            }, _config.Second);

            RunOn(() =>
            {
                // gossip chat from the join will synchronize the views
                AwaitMembers(GetAddress(_config.First), GetAddress(_config.Second));
                AwaitMemberStatus(GetAddress(_config.First), Akka.Cluster.MemberStatus.Up);
                AwaitMemberStatus(GetAddress(_config.Second), Akka.Cluster.MemberStatus.Joining);
                AwaitAssert(() => SeenLatestGossip().Should().BeEquivalentTo(ImmutableHashSet.Create(_config.First, _config.Second)));
            }, _config.First, _config.Second);

            EnterBarrier("convergence-joining-2");

            RunOn(() =>
            {
                LeaderActions();
                AwaitMemberStatus(GetAddress(_config.First), Akka.Cluster.MemberStatus.Up);
                AwaitMemberStatus(GetAddress(_config.Second), Akka.Cluster.MemberStatus.Up);
            }, _config.First);
            EnterBarrier("leader-actions-2");

            GossipTo(_config.First, _config.Second);
            RunOn(() =>
            {
                // gossip chat will synchronize the views
                AwaitMemberStatus(GetAddress(_config.Second), Akka.Cluster.MemberStatus.Up);
                AwaitAssert(() => SeenLatestGossip().Should().BeEquivalentTo(ImmutableHashSet.Create(_config.First, _config.Second)));
                AwaitMemberStatus(GetAddress(_config.First), Akka.Cluster.MemberStatus.Up);
            }, _config.First, _config.Second);

            EnterBarrier("after-2");
        }

        private void A_Cluster_must_perform_correct_transitions_when_third_joins_second()
        {
            RunOn(() =>
            {
                Cluster.Join(GetAddress(_config.Second));
            }, _config.Third);

            RunOn(() =>
            {
                // gossip chat from the join will synchronize the views
                AwaitAssert(() => SeenLatestGossip().Should().BeEquivalentTo(ImmutableHashSet.Create(_config.Second, _config.Third)));
            }, _config.Second, _config.Third);
            EnterBarrier("third-joined-second");

            GossipTo(_config.Second, _config.First);
            RunOn(() =>
            {
                // gossip chat will synchronize the views
                AwaitMembers(GetAddress(_config.First), GetAddress(_config.Second), GetAddress(_config.Third));
                AwaitMemberStatus(GetAddress(_config.Third), Akka.Cluster.MemberStatus.Joining);
                AwaitMemberStatus(GetAddress(_config.Second), Akka.Cluster.MemberStatus.Up);
                AwaitAssert(() => SeenLatestGossip().Should().BeEquivalentTo(ImmutableHashSet.Create(_config.First, _config.Second, _config.Third)));
            }, _config.First, _config.Second);

            GossipTo(_config.First, _config.Third);
            RunOn(() =>
            {
                AwaitMembers(GetAddress(_config.First), GetAddress(_config.Second), GetAddress(_config.Third));
                AwaitMemberStatus(GetAddress(_config.First), Akka.Cluster.MemberStatus.Up);
                AwaitMemberStatus(GetAddress(_config.Second), Akka.Cluster.MemberStatus.Up);
                AwaitMemberStatus(GetAddress(_config.Third), Akka.Cluster.MemberStatus.Joining);
                AwaitAssert(() => SeenLatestGossip().Should().BeEquivalentTo(ImmutableHashSet.Create(_config.First, _config.Second, _config.Third)));
            }, _config.First, _config.Second, _config.Third);

            EnterBarrier("convergence-joining-3");

            var leader12 = Leader(_config.First, _config.Second);
            Log.Debug("Leader: {0}", leader12);
            var tmp = Roles.Where(x => x != leader12).ToList();
            var other1 = tmp.First();
            var other2 = tmp.Skip(1).First();

            RunOn(() =>
            {
                LeaderActions();
                AwaitMemberStatus(GetAddress(_config.First), Akka.Cluster.MemberStatus.Up);
                AwaitMemberStatus(GetAddress(_config.Second), Akka.Cluster.MemberStatus.Up);
                AwaitMemberStatus(GetAddress(_config.Third), Akka.Cluster.MemberStatus.Up);
            }, leader12);
            EnterBarrier("leader-actions-3");

            // leader gossipTo first non-leader
            GossipTo(leader12, other1);
            RunOn(() =>
            {
                AwaitMemberStatus(GetAddress(_config.Third), Akka.Cluster.MemberStatus.Up);
                AwaitAssert(() => SeenLatestGossip().Should().BeEquivalentTo(ImmutableHashSet.Create(leader12, Myself)));
            }, other1);

            // first non-leader gossipTo the other non-leader
            GossipTo(other1, other2);
            RunOn(() =>
            {
                // send gossip
                Cluster.ClusterCore.Tell(new InternalClusterAction.SendGossipTo(GetAddress(other2)));
            }, other1);

            RunOn(() =>
            {
                AwaitMemberStatus(GetAddress(_config.Third), Akka.Cluster.MemberStatus.Up);
                AwaitAssert(() => SeenLatestGossip().Should().BeEquivalentTo(ImmutableHashSet.Create(_config.First, _config.Second, _config.Third)));
            }, other2);

            // first non-leader gossipTo the leader
            GossipTo(other1, leader12);
            RunOn(() =>
            {
                AwaitMemberStatus(GetAddress(_config.First), Akka.Cluster.MemberStatus.Up);
                AwaitMemberStatus(GetAddress(_config.Second), Akka.Cluster.MemberStatus.Up);
                AwaitMemberStatus(GetAddress(_config.Third), Akka.Cluster.MemberStatus.Up);
                AwaitAssert(() => SeenLatestGossip().Should().BeEquivalentTo(ImmutableHashSet.Create(_config.First, _config.Second, _config.Third)));
            }, _config.First, _config.Second, _config.Third);

            EnterBarrier("after-3");
        }

        private void A_Cluster_must_perform_correct_transitions_when_second_becomes_unavailable()
        {
            RunOn(() =>
            {
                MarkNodeAsUnavailable(GetAddress(_config.Second));
                ReapUnreachable();
                AwaitAssert(() => ClusterView.UnreachableMembers.Select(c => c.Address).Should().Contain(GetAddress(_config.Second)));
                AwaitAssert(() => SeenLatestGossip().Should().BeEquivalentTo(ImmutableHashSet.Create(_config.Third)));
            }, _config.Third);

            EnterBarrier("after-second-unavailable");

            GossipTo(_config.Third, _config.First);
            RunOn(() =>
            {
                AwaitAssert(() => ClusterView.UnreachableMembers.Select(c => c.Address).Should().Contain(GetAddress(_config.Second)));
            }, _config.First, _config.Third);

            RunOn(() =>
            {
                Cluster.Down(GetAddress(_config.Second));
            }, _config.First);

            EnterBarrier("after-second-down");
            GossipTo(_config.First, _config.Third);
            RunOn(() =>
            {
                AwaitAssert(() => ClusterView.UnreachableMembers.Select(c => c.Address).Should().Contain(GetAddress(_config.Second)));
                AwaitMemberStatus(GetAddress(_config.Second), Akka.Cluster.MemberStatus.Down);
                AwaitAssert(() => SeenLatestGossip().Should().BeEquivalentTo(ImmutableHashSet.Create(_config.First, _config.Third)));
            }, _config.First, _config.Third);

            EnterBarrier("after-4");
        }
    }
}
