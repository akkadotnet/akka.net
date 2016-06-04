using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.TestKit;
using Akka.TestKit;

namespace Akka.Cluster.Tests.MultiNode
{
    public class MinMembersBeforeUpSpecConfig : MultiNodeConfig
    {
        public readonly RoleName First;
        public readonly RoleName Second;
        public readonly RoleName Third;

        public MinMembersBeforeUpSpecConfig()
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");

            CommonConfig = ConfigurationFactory.ParseString(@"
                akka.cluster.min-nr-of-members = 3
            ").WithFallback(MultiNodeClusterSpec.ClusterConfigWithFailureDetectorPuppet());
        }
    }

    public class MinMembersOfRoleBeforeUpSpecConfig : MultiNodeConfig
    {
        public readonly RoleName First;
        public readonly RoleName Second;
        public readonly RoleName Third;

        public MinMembersOfRoleBeforeUpSpecConfig()
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");

            CommonConfig = ConfigurationFactory.ParseString(@"
                akka.cluster.role.backend.min-nr-of-members = 2
            ").WithFallback(MultiNodeClusterSpec.ClusterConfigWithFailureDetectorPuppet());

            NodeConfig(new List<RoleName> { First }, new List<Config>
            {
                ConfigurationFactory.ParseString("akka.cluster.roles =[frontend]")
            });

            NodeConfig(new List<RoleName> { Second, Third }, new List<Config>
            {
                ConfigurationFactory.ParseString("akka.cluster.roles =[backend]")
            });
        }
    }

    public class MinMembersBeforeUpNode1 : MinMembersBeforeUpSpec { }
    public class MinMembersBeforeUpNode2 : MinMembersBeforeUpSpec { }
    public class MinMembersBeforeUpNode3 : MinMembersBeforeUpSpec { }

    public class MinMembersOfRoleBeforeUpNode1 : MinMembersOfRoleBeforeUpSpec { }
    public class MinMembersOfRoleBeforeUpNode2 : MinMembersOfRoleBeforeUpSpec { }
    public class MinMembersOfRoleBeforeUpNode3 : MinMembersOfRoleBeforeUpSpec { }

    public abstract class MinMembersBeforeUpSpec : MinMembersBeforeUpBase
    {
        protected MinMembersBeforeUpSpec() : this(new MinMembersBeforeUpSpecConfig())
        {
        }

        protected MinMembersBeforeUpSpec(MinMembersBeforeUpSpecConfig config) : base(config)
        {
            First = config.First;
            Second = config.Second;
            Third = config.Third;
        }

        [MultiNodeFact]
        public void Cluster_leader_must_wait_with_moving_members_to_up_until_minimum_number_of_members_have_joined()
        {
            TestWaitMovingMembersToUp();
        }
    }

    public abstract class MinMembersOfRoleBeforeUpSpec : MinMembersBeforeUpBase
    {
        protected MinMembersOfRoleBeforeUpSpec() : this(new MinMembersOfRoleBeforeUpSpecConfig())
        {
        }

        protected MinMembersOfRoleBeforeUpSpec(MinMembersOfRoleBeforeUpSpecConfig config) : base(config)
        {
            First = config.First;
            Second = config.Second;
            Third = config.Third;
        }

        [MultiNodeFact]
        public void Cluster_leader_must_wait_with_moving_members_to_up_until_minimum_number_of_members_with_specific_role_have_joined()
        {
            TestWaitMovingMembersToUp();
        }
    }

    public abstract class MinMembersBeforeUpBase : MultiNodeClusterSpec
    {
        protected RoleName First;
        protected RoleName Second;
        protected RoleName Third;

        protected MinMembersBeforeUpBase(MultiNodeConfig config) : base(config)
        {
        }

        protected void TestWaitMovingMembersToUp()
        {
            var onUpLatch = new TestLatch(1);
            Cluster.RegisterOnMemberUp(() =>
            {
                onUpLatch.CountDown();
            });

            RunOn(() =>
            {
                Cluster.Join(GetAddress(Myself));
                AwaitAssert(() =>
                {
                    ClusterView.RefreshCurrentState();
                    ClusterView.Status.ShouldBe(MemberStatus.Joining);
                });
            }, First);
            EnterBarrier("first-started");

            onUpLatch.IsOpen.ShouldBeFalse();

            RunOn(() =>
            {
                Cluster.Join(GetAddress(First));
            }, Second);

            RunOn(() =>
            {
                var expectedAddresses = new List<Address> { GetAddress(First), GetAddress(Second) };
                AwaitAssert(() =>
                {
                    ClusterView.RefreshCurrentState();
                    ClusterView.Members.Select(c => c.Address).Except(expectedAddresses).Count().ShouldBe(0);
                });
                ClusterView.Members.All(c => c.Status == MemberStatus.Joining).ShouldBeTrue();
                // and it should not change
                foreach (var _ in Enumerable.Range(1, 5))
                {
                    Thread.Sleep(1000);
                    ClusterView.Members.Select(c => c.Address).Except(expectedAddresses).Count().ShouldBe(0);
                    ClusterView.Members.All(c => c.Status == MemberStatus.Joining).ShouldBeTrue();
                }
            }, First, Second);
            EnterBarrier("second-joined");

            RunOn(() =>
            {
                Cluster.Join(GetAddress(First));
            }, Third);
            AwaitClusterUp(First, Second, Third);

            onUpLatch.Ready(TestKitSettings.DefaultTimeout);
            EnterBarrier("after-1");
        }
    }
}
