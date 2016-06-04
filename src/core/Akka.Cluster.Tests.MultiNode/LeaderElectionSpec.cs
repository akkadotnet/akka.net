//-----------------------------------------------------------------------
// <copyright file="JoinSeedNodeSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------
using System;
using System.Collections.Immutable;
using System.Linq;
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

        public LeaderElectionSpecConfig(bool failureDectectorPuppet)
        {
            Controller = Role("controller");
            First = Role("first");
            Second = Role("second");
            Third = Role("third");
            Forth = Role("forth");

            CommonConfig = DebugConfig(false)
                .WithFallback(MultiNodeClusterSpec.ClusterConfig(failureDectectorPuppet));
        }
    }

    public class LeaderElectionWithFailureDetectorPuppetMultiJvmNode1 : LeaderElectionSpec
    {
        public LeaderElectionWithFailureDetectorPuppetMultiJvmNode1()
            : base(true)
        {
        }
    }

    public class LeaderElectionWithFailureDetectorPuppetMultiJvmNode2 : LeaderElectionSpec
    {
        public LeaderElectionWithFailureDetectorPuppetMultiJvmNode2()
            : base(true)
        {
        }
    }

    public class LeaderElectionWithFailureDetectorPuppetMultiJvmNode3 : LeaderElectionSpec
    {
        public LeaderElectionWithFailureDetectorPuppetMultiJvmNode3()
            : base(true)
        {
        }
    }

    public class LeaderElectionWithFailureDetectorPuppetMultiJvmNode4 : LeaderElectionSpec
    {
        public LeaderElectionWithFailureDetectorPuppetMultiJvmNode4()
            : base(true)
        {
        }
    }

    public class LeaderElectionWithFailureDetectorPuppetMultiJvmNode5 : LeaderElectionSpec
    {
        public LeaderElectionWithFailureDetectorPuppetMultiJvmNode5()
            : base(true)
        {
        }
    }

    public class LeaderElectionWithAccrualFailureDetectorMultiJvmNode1 : LeaderElectionSpec
    {
        public LeaderElectionWithAccrualFailureDetectorMultiJvmNode1()
            : base(false)
        {
        }
    }

    public class LeaderElectionWithAccrualFailureDetectorMultiJvmNode2 : LeaderElectionSpec
    {
        public LeaderElectionWithAccrualFailureDetectorMultiJvmNode2()
            : base(false)
        {
        }
    }

    public class LeaderElectionWithAccrualFailureDetectorMultiJvmNode3 : LeaderElectionSpec
    {
        public LeaderElectionWithAccrualFailureDetectorMultiJvmNode3()
            : base(false)
        {
        }
    }

    public class LeaderElectionWithAccrualFailureDetectorMultiJvmNode4 : LeaderElectionSpec
    {
        public LeaderElectionWithAccrualFailureDetectorMultiJvmNode4()
            : base(false)
        {
        }
    }

    public class LeaderElectionWithAccrualFailureDetectorMultiJvmNode5 : LeaderElectionSpec
    {
        public LeaderElectionWithAccrualFailureDetectorMultiJvmNode5()
            : base(false)
        {
        }
    }

    public abstract class LeaderElectionSpec : MultiNodeClusterSpec
    {
        private readonly LeaderElectionSpecConfig _config;

        private readonly ImmutableList<RoleName> _sortedRoles;

        protected LeaderElectionSpec(bool failureDetectorPuppet)
            : this(new LeaderElectionSpecConfig(failureDetectorPuppet))
        {

        }

        protected LeaderElectionSpec(LeaderElectionSpecConfig config)
            : base(config)
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
            Cluster_of_four_nodes_must_be_able_to_reelect_sinle_leader_after_leader_has_left();
            Cluster_of_four_nodes_must_be_able_to_reelect_sinle_leader_after_leader_has_left_again();
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

        public void Cluster_of_four_nodes_must_be_able_to_reelect_sinle_leader_after_leader_has_left()
        {
            Within(TimeSpan.FromSeconds(30), () =>
            {
                ShutdownLeaderAndVerifyNewLeader(0);
                EnterBarrier("after-2");
            });
        }

        public void Cluster_of_four_nodes_must_be_able_to_reelect_sinle_leader_after_leader_has_left_again()
        {
            Within(TimeSpan.FromSeconds(30), () =>
            {
                ShutdownLeaderAndVerifyNewLeader(1);
                EnterBarrier("after-3");
            });
        }
    }
}