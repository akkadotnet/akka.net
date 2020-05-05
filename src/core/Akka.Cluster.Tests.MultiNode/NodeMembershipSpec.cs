//-----------------------------------------------------------------------
// <copyright file="NodeMembershipSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System.Linq;
using Akka.Cluster.TestKit;
using Akka.Remote.TestKit;
using Akka.TestKit;

namespace Akka.Cluster.Tests.MultiNode
{
    public class NodeMembershipSpecConfig : MultiNodeConfig
    {
        public readonly RoleName First;
        public readonly RoleName Second;
        public readonly RoleName Third;

        public NodeMembershipSpecConfig()
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");

            CommonConfig = MultiNodeClusterSpec.ClusterConfigWithFailureDetectorPuppet();
        }
    }

    public class NodeMembershipSpec : MultiNodeClusterSpec
    {
        private readonly NodeMembershipSpecConfig _config;

        public NodeMembershipSpec() : this(new NodeMembershipSpecConfig())
        {
        }

        protected NodeMembershipSpec(NodeMembershipSpecConfig config) : base(config, typeof(NodeMembershipSpec))
        {
            _config = config;
        }

        [MultiNodeFact]
        public void NodeMembershipSpecs()
        {
            Set_of_connected_cluster_systems_must_with_two_nodes_start_gossiping_to_each_other_so_that_both_nodes_gets_same_gossip_info();
            Set_of_connected_cluster_systems_must_with_three_nodes_start_gossiping_to_each_other_so_that_all_nodes_gets_same_gossip_info();
            Set_of_connected_cluster_systems_must_correct_member_age();
        }

        public void Set_of_connected_cluster_systems_must_with_two_nodes_start_gossiping_to_each_other_so_that_both_nodes_gets_same_gossip_info()
        {
            RunOn(() =>
            {
                StartClusterNode();
            }, _config.First);
            EnterBarrier("first-started");

            RunOn(() =>
            {
                Cluster.Join(GetAddress(_config.First));
                AwaitAssert(() => ClusterView.Members.Count.ShouldBe(2));
                AssertMembers(ClusterView.Members, GetAddress(_config.First), GetAddress(_config.Second));
                AwaitAssert(() => ClusterView.Members.All(c => c.Status == MemberStatus.Up).ShouldBeTrue());
            }, _config.First, _config.Second);

            EnterBarrier("after-1");
        }

        public void Set_of_connected_cluster_systems_must_with_three_nodes_start_gossiping_to_each_other_so_that_all_nodes_gets_same_gossip_info()
        {
            RunOn(() =>
            {
                Cluster.Join(GetAddress(_config.First));
            }, _config.Third);

            AwaitAssert(() => ClusterView.Members.Count.ShouldBe(3));
            AssertMembers(ClusterView.Members, GetAddress(_config.First), GetAddress(_config.Second), GetAddress(_config.Third));
            AwaitAssert(() => ClusterView.Members.All(c => c.Status == MemberStatus.Up).ShouldBeTrue());

            EnterBarrier("after-2");
        }

        public void Set_of_connected_cluster_systems_must_correct_member_age()
        {
            var firstMember = ClusterView.Members.First(c => c.Address == GetAddress(_config.First));
            var secondMember = ClusterView.Members.First(c => c.Address == GetAddress(_config.Second));
            var thirdMember = ClusterView.Members.First(c => c.Address == GetAddress(_config.Third));

            firstMember.IsOlderThan(thirdMember).ShouldBeTrue();
            thirdMember.IsOlderThan(firstMember).ShouldBeFalse();
            secondMember.IsOlderThan(thirdMember).ShouldBeTrue();
            thirdMember.IsOlderThan(secondMember).ShouldBeFalse();

            EnterBarrier("after-3");
        }
    }
}
