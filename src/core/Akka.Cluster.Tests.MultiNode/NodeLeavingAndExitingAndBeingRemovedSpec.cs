//-----------------------------------------------------------------------
// <copyright file="NodeLeavingAndExitingAndBeingRemovedSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.Remote.TestKit;
using FluentAssertions;

namespace Akka.Cluster.Tests.MultiNode
{
    public class NodeLeavingAndExitingAndBeingRemovedSpecConfig : MultiNodeConfig
    {
        public RoleName First { get; set; }
        public RoleName Second { get; set; }
        public RoleName Third { get; set; }

        public NodeLeavingAndExitingAndBeingRemovedSpecConfig()
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");

            CommonConfig = DebugConfig(false)
                .WithFallback(ConfigurationFactory.ParseString("akka.cluster.auto-down-unreachable-after = 0s"))
                .WithFallback(MultiNodeClusterSpec.ClusterConfigWithFailureDetectorPuppet());
        }
    }

    public class NodeLeavingAndExitingAndBeingRemovedNode1 : NodeLeavingAndExitingAndBeingRemovedSpec { }
    public class NodeLeavingAndExitingAndBeingRemovedNode2 : NodeLeavingAndExitingAndBeingRemovedSpec { }
    public class NodeLeavingAndExitingAndBeingRemovedNode3 : NodeLeavingAndExitingAndBeingRemovedSpec { }

    public abstract class NodeLeavingAndExitingAndBeingRemovedSpec : MultiNodeClusterSpec
    {
        private readonly NodeLeavingAndExitingAndBeingRemovedSpecConfig _config;

        protected NodeLeavingAndExitingAndBeingRemovedSpec() : this(new NodeLeavingAndExitingAndBeingRemovedSpecConfig())
        {
        }

        protected NodeLeavingAndExitingAndBeingRemovedSpec(NodeLeavingAndExitingAndBeingRemovedSpecConfig config) : base(config)
        {
            _config = config;
        }

        [MultiNodeFact]
        public void NodeLeavingAndExitingAndBeingRemovedSpecs()
        {
            Node_that_is_leaving_non_singleton_cluster_eventually_set_to_removed_and_removed_from_membership_ring_and_seen_table();
        }

        public void Node_that_is_leaving_non_singleton_cluster_eventually_set_to_removed_and_removed_from_membership_ring_and_seen_table()
        {
            AwaitClusterUp(_config.First, _config.Second, _config.Third);

            Within(TimeSpan.FromSeconds(30), () =>
            {
                RunOn(() =>
                {
                    Cluster.Leave(GetAddress(_config.Second));
                }, _config.First);
                EnterBarrier("second-left");

                RunOn(() =>
                {
                    EnterBarrier("second-shutdown");
                    MarkNodeAsUnavailable(GetAddress(_config.Second));

                    // verify that the 'second' node is no longer part of the 'members'/'unreachable' set
                    AwaitAssert(() =>
                    {
                        ClusterView.Members.Select(c => c.Address).Should().NotContain(GetAddress(_config.Second));
                    });
                    AwaitAssert(() =>
                    {
                        ClusterView.UnreachableMembers.Select(c => c.Address).Should().NotContain(GetAddress(_config.Second));
                    });
                }, _config.First, _config.Third);

                RunOn(() =>
                {
                    // verify that the second node is shut down
                    AwaitCondition(() => Cluster.IsTerminated);
                    EnterBarrier("second-shutdown");
                }, _config.Second);

                EnterBarrier("finished");
            });

        }
    }
}