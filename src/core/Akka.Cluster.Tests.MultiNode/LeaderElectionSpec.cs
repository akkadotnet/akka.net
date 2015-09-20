using System.Collections.Immutable;
using System.Linq;
using Akka.Remote.TestKit;
using Akka.TestKit;

namespace Akka.Cluster.Tests.MultiNode
{
    public class LeaderElectionSpecConfig : MultiNodeConfig
    {
        private readonly RoleName _controller;
        public RoleName Controller
        {
            get { return _controller; }
        }

        private readonly RoleName _first;
        public RoleName First
        {
            get { return _first; }
        }

        private readonly RoleName _second;
        public RoleName Second
        {
            get { return _second; }
        }

        private readonly RoleName _third;
        public RoleName Third
        {
            get { return _third; }
        }

        private readonly RoleName _forth;
        public RoleName Forth
        {
            get { return _forth; }
        }

        public LeaderElectionSpecConfig(bool failureDectectorPuppet)
        {
            _controller = Role("controller");
            _first = Role("first");
            _second = Role("second");
            _third = Role("third");
            _forth = Role("forth");

            if (failureDectectorPuppet)
            {
                CommonConfig = MultiNodeLoggingConfig.LoggingConfig
                    .WithFallback(DebugConfig(true))
                    .WithFallback(@"akka.cluster.auto-down-unreachable-after = 0s
akka.cluster.publish-stats-interval = 25 s")
                    .WithFallback(MultiNodeClusterSpec.ClusterConfigWithFailureDetectorPuppet());
            }
            else
            {
                CommonConfig = MultiNodeLoggingConfig.LoggingConfig
                    .WithFallback(DebugConfig(true))
                    .WithFallback(@"akka.cluster.auto-down-unreachable-after = 0s
akka.cluster.publish-stats-interval = 25 s")
                    .WithFallback(MultiNodeClusterSpec.ClusterConfig());
            }

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

        public void ShutdownLeaderAndVerifyNewLeader(int alreadyShutdown)
        {
            var currentRoles = _sortedRoles.RemoveAt(alreadyShutdown);
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
            }
            else if (Myself == aUser)
            {
                var leaderAddress = GetAddress(leader);
                EnterBarrier("before-shutdown" + n, "after-shutdown" + n);
                MarkNodeAsUnavailable(leaderAddress);
                AwaitAssert(() => ClusterView.UnreachableMembers.Select(x => x.Address).Contains(leaderAddress).ShouldBeTrue());
                EnterBarrier("after-unavailable" + n);
                Cluster.Down(leaderAddress);
                AwaitAssert((() => ClusterView.UnreachableMembers.Select(x => x.Address).Contains(leaderAddress).ShouldBeFalse()));
                EnterBarrier("after-down" + n, "completed" + n);
            }
            else if (remainingRoles.Contains(Myself))
            {
                var leaderAddress = GetAddress(leader);
                EnterBarrier("before-shutdown" + n, "after-shutdown" + n);
                AwaitAssert(() => ClusterView.UnreachableMembers.Select(x => x.Address).Contains(leaderAddress).ShouldBeTrue());
                EnterBarrier("after-unavailable" + n);
                EnterBarrier("after-down" + n);
                AwaitMembersUp(currentRoles.Count - 1);
                var nextExpectedLeader = remainingRoles.First();
                (ClusterView.IsLeader && Myself == nextExpectedLeader).ShouldBeTrue();
                AssertLeaderIn(remainingRoles);
                EnterBarrier("completed" + n);
            }
        }

        [MultiNodeFact]
        public void AClusterOfFourNodesMustBeAbleToElectASingleLeader()
        {
            AwaitClusterUp(_config.First, _config.Second, _config.Third, _config.Forth);

            if (Myself != _config.Controller)
            {
                (ClusterView.IsLeader && Myself == _sortedRoles.First()).ShouldBeTrue();
                AssertLeaderIn(_sortedRoles);
            }

            EnterBarrier("after-1");
            ShutdownLeaderAndVerifyNewLeader(1);
            EnterBarrier("after-3");
        }

        [MultiNodeFact]
        public void BeAbleToReElectASingleLeaderAfterLeaderHasLeft()
        {
            ShutdownLeaderAndVerifyNewLeader(0);
            EnterBarrier("after-2");
        }

        [MultiNodeFact]
        public void BeAbleToReElectASingleLeaderAfterLeaderHasLeftAgain()
        {
            ShutdownLeaderAndVerifyNewLeader(1);
            EnterBarrier("after-3");
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

}
