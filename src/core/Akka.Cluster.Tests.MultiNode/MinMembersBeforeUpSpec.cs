//-----------------------------------------------------------------------
// <copyright file="MinMembersBeforeUpSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.Remote.TestKit;
using Akka.TestKit;

namespace Akka.Cluster.Tests.MultiNode
{
    #region Member.Up

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

    public class MinMembersBeforeUpSpec : MinMembersBeforeUpBase
    {
        public MinMembersBeforeUpSpec() : this(new MinMembersBeforeUpSpecConfig())
        {
        }

        protected MinMembersBeforeUpSpec(MinMembersBeforeUpSpecConfig config) : base(config, typeof(MinMembersBeforeUpSpec))
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

    #endregion

    #region Member.WeaklyUp

    public class MinMembersBeforeUpWithWeaklyUpSpecConfig : MultiNodeConfig
    {
        public readonly RoleName First;
        public readonly RoleName Second;
        public readonly RoleName Third;

        public MinMembersBeforeUpWithWeaklyUpSpecConfig()
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");

            CommonConfig = ConfigurationFactory.ParseString(@"
                akka.cluster.min-nr-of-members = 3
                akka.cluster.allow-weakly-up-members = on
            ").WithFallback(MultiNodeClusterSpec.ClusterConfigWithFailureDetectorPuppet());
        }
    }
    public class MinMembersBeforeUpWithWeaklyUpNode1 : MinMembersBeforeUpWithWeaklyUpSpec { }
    public class MinMembersBeforeUpWithWeaklyUpNode2 : MinMembersBeforeUpWithWeaklyUpSpec { }
    public class MinMembersBeforeUpWithWeaklyUpNode3 : MinMembersBeforeUpWithWeaklyUpSpec { }

    public abstract class MinMembersBeforeUpWithWeaklyUpSpec : MinMembersBeforeUpBase
    {
        protected MinMembersBeforeUpWithWeaklyUpSpec() : this(new MinMembersBeforeUpWithWeaklyUpSpecConfig())
        {
        }

        protected MinMembersBeforeUpWithWeaklyUpSpec(MinMembersBeforeUpWithWeaklyUpSpecConfig config) 
            : base(config, typeof(MinMembersBeforeUpWithWeaklyUpSpec))
        {
            First = config.First;
            Second = config.Second;
            Third = config.Third;
        }

        [MultiNodeFact]
        public void Cluster_leader_must_wait_with_moving_members_to_up_until_minimum_number_of_members_have_joined_with_WeaklyUp_enabled()
        {
            TestWaitMovingMembersToUp();
        }
    }

    #endregion

    public class MinMembersOfRoleBeforeUpSpec : MinMembersBeforeUpBase
    {
        public MinMembersOfRoleBeforeUpSpec() : this(new MinMembersOfRoleBeforeUpSpecConfig())
        {
        }

        protected MinMembersOfRoleBeforeUpSpec(MinMembersOfRoleBeforeUpSpecConfig config) : base(config, typeof(MinMembersOfRoleBeforeUpSpec))
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

        protected MinMembersBeforeUpBase(MultiNodeConfig config, Type type) : base(config, type)
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
