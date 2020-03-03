//-----------------------------------------------------------------------
// <copyright file="LeaderElectionSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using Akka.Cluster.TestKit;
using Akka.Remote.TestKit;
using Akka.TestKit;

namespace Akka.Cluster.Tests.MultiNode
{
    public class LeaderElectionSpecConfig : MultiNodeConfig
    {
        public RoleName Controller { get; private set; }
        public RoleName First { get; private set; }
        public RoleName Second { get; private set; }
        public RoleName Third { get; private set; }
        public RoleName Forth { get; private set; }

        public LeaderElectionSpecConfig(bool failureDetectorPuppet)
        {
            Controller = Role("controller");
            First = Role("first");
            Second = Role("second");
            Third = Role("third");
            Forth = Role("forth");

            CommonConfig = DebugConfig(false)
                .WithFallback(MultiNodeClusterSpec.ClusterConfig(failureDetectorPuppet));
        }
    }

    public class LeaderElectionWithFailureDetectorPuppetMultiJvmNode : LeaderElectionSpec
    {
        public LeaderElectionWithFailureDetectorPuppetMultiJvmNode()
            : base(true, typeof(LeaderElectionWithFailureDetectorPuppetMultiJvmNode))
        {
        }
    }

    public class LeaderElectionWithAccrualFailureDetectorMultiJvmNode : LeaderElectionSpec
    {
        public LeaderElectionWithAccrualFailureDetectorMultiJvmNode()
            : base(false, typeof(LeaderElectionWithAccrualFailureDetectorMultiJvmNode))
        {
        }
    }

    public abstract class LeaderElectionSpec : MultiNodeClusterSpec
    {
        private readonly LeaderElectionSpecConfig _config;

        private readonly ImmutableList<RoleName> _sortedRoles;

        protected LeaderElectionSpec(bool failureDetectorPuppet, Type type)
            : this(new LeaderElectionSpecConfig(failureDetectorPuppet), type)
        {

        }

        protected LeaderElectionSpec(LeaderElectionSpecConfig config, Type type)
            : base(config, type)
        {
            _config = config;
            _sortedRoles = ImmutableList.Create(
                _config.First,
                _config.Second,
                _config.Third,
                _config.Forth)
                .Sort(new RoleNameComparer(this));
        }

        [MultiNodeFact]
        public void LeaderElectionSpecs()
        {
            Cluster_of_four_nodes_must_be_able_to_elect_single_leader();
            Cluster_of_four_nodes_must_be_able_to_reelect_single_leader_after_leader_has_left();
            Cluster_of_four_nodes_must_be_able_to_reelect_single_leader_after_leader_has_left_again();
        }

        public void Cluster_of_four_nodes_must_be_able_to_elect_single_leader()
        {
            AwaitClusterUp(_config.First, _config.Second, _config.Third, _config.Forth);

            if (Myself != _config.Controller)
            {
                ClusterView.IsLeader.ShouldBe(Myself == _sortedRoles.First());
                AssertLeaderIn(_sortedRoles);
            }

            EnterBarrier("after-1");
        }

        public void ShutdownLeaderAndVerifyNewLeader(int alreadyShutdown)
        {
            var currentRoles = _sortedRoles.Skip(alreadyShutdown).ToList();
            currentRoles.Count.ShouldBeGreaterOrEqual(2);
            var leader = currentRoles.First();
            var aUser = currentRoles.Last();
            var remainingRoles = currentRoles.Skip(1).ToImmutableList();
            var n = "-" + (alreadyShutdown + 1);

            if (Myself == _config.Controller)
            {
                EnterBarrier("before-shutdown" + n);
                TestConductor.Exit(leader, 0).Wait();
                EnterBarrier("after-shutdown" + n, "after-unavailable" + n, "after-down" + n, "completed" + n);
            }
            else if (Myself == leader)
            {
                EnterBarrier("before-shutdown" + n, "after-shutdown" + n);
                // this node will be shutdown by the controller and doesn't participate in more barriers
            }
            else if (Myself == aUser)
            {
                var leaderAddress = GetAddress(leader);
                EnterBarrier("before-shutdown" + n, "after-shutdown" + n);

                // detect failure
                MarkNodeAsUnavailable(leaderAddress);
                AwaitAssert(() => ClusterView.UnreachableMembers.Select(x => x.Address).Contains(leaderAddress).ShouldBeTrue());
                EnterBarrier("after-unavailable" + n);

                // user marks the shutdown leader as DOWN
                Cluster.Down(leaderAddress);

                // removed
                AwaitAssert((() => ClusterView.UnreachableMembers.Select(x => x.Address).Contains(leaderAddress).ShouldBeFalse()));
                EnterBarrier("after-down" + n, "completed" + n);
            }
            else if (remainingRoles.Contains(Myself))
            {
                // remaining cluster nodes, not shutdown
                var leaderAddress = GetAddress(leader);
                EnterBarrier("before-shutdown" + n, "after-shutdown" + n);

                AwaitAssert(() => ClusterView.UnreachableMembers.Select(x => x.Address).Contains(leaderAddress).ShouldBeTrue());
                EnterBarrier("after-unavailable" + n);

                EnterBarrier("after-down" + n);
                AwaitMembersUp(currentRoles.Count - 1);
                var nextExpectedLeader = remainingRoles.First();
                ClusterView.IsLeader.ShouldBe(Myself == nextExpectedLeader);
                AssertLeaderIn(remainingRoles);

                EnterBarrier("completed" + n);
            }
        }

        public void Cluster_of_four_nodes_must_be_able_to_reelect_single_leader_after_leader_has_left()
        {
            Within(TimeSpan.FromSeconds(30), () =>
            {
                ShutdownLeaderAndVerifyNewLeader(0);
                EnterBarrier("after-2");
            });
        }

        public void Cluster_of_four_nodes_must_be_able_to_reelect_single_leader_after_leader_has_left_again()
        {
            Within(TimeSpan.FromSeconds(30), () =>
            {
                ShutdownLeaderAndVerifyNewLeader(1);
                EnterBarrier("after-3");
            });
        }
    }
}
