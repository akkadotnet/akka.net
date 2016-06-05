//-----------------------------------------------------------------------
// <copyright file="SingletonClusterSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2016 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2016 Akka.NET project <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using Akka.Actor;
using Akka.Configuration;
using Akka.Remote.TestKit;
using Akka.TestKit;

namespace Akka.Cluster.Tests.MultiNode
{
    public class SingletonClusterConfig : MultiNodeConfig
    {
        public RoleName First { get; set; }
        public RoleName Second { get; set; }

        public SingletonClusterConfig(bool failureDetectorPuppet)
        {
            First = Role("first");
            Second = Role("second");

            CommonConfig = DebugConfig(false)
                .WithFallback(ConfigurationFactory.ParseString(@"
                    akka.cluster.auto-down-unreachable-after = 0s
                    akka.cluster.failure-detector.threshold = 4
                "))
                .WithFallback(MultiNodeClusterSpec.ClusterConfig(failureDetectorPuppet));
        }
    }

    public class SingletonClusterWithFailureDetectorPuppetMultiNode1 : SingletonClusterSpec
    {
        public SingletonClusterWithFailureDetectorPuppetMultiNode1() : base(true)
        {
        }
    }

    public class SingletonClusterWithFailureDetectorPuppetMultiNode2 : SingletonClusterSpec
    {
        public SingletonClusterWithFailureDetectorPuppetMultiNode2() : base(true)
        {
        }
    }

    public class SingletonClusterWithAccrualFailureDetectorMultiNode1 : SingletonClusterSpec
    {
        public SingletonClusterWithAccrualFailureDetectorMultiNode1() : base(false)
        {
        }
    }

    public class SingletonClusterWithAccrualFailureDetectorMultiNode2 : SingletonClusterSpec
    {
        public SingletonClusterWithAccrualFailureDetectorMultiNode2() : base(false)
        {
        }
    }

    public abstract class SingletonClusterSpec : MultiNodeClusterSpec
    {
        private readonly SingletonClusterConfig _config;

        protected SingletonClusterSpec(bool failureDetectorPuppet) : this(new SingletonClusterConfig(failureDetectorPuppet))
        {
        }

        protected SingletonClusterSpec(SingletonClusterConfig config) : base(config)
        {
            _config = config;
        }

        [MultiNodeFact]
        public void SingletonClusterSpecs()
        {
            Cluster_of_2_nodes_must_become_singleton_cluster_when_started_with_seednodes();
            Cluster_of_2_nodes_must_not_be_singleton_cluster_when_joined_with_other_node();
            Cluster_of_2_nodes_must_become_singleton_cluster_when_one_node_is_shutdown();
            Cluster_of_2_nodes_must_leave_and_shutdown_itself_when_singleton_cluster();
        }

        public void Cluster_of_2_nodes_must_become_singleton_cluster_when_started_with_seednodes()
        {
            RunOn(() =>
            {
                var nodes = ImmutableList.Create<Address>(GetAddress(_config.First));
                Cluster.JoinSeedNodes(nodes);
                AwaitMembersUp(1);
                ClusterView.IsSingletonCluster.ShouldBeTrue();
            }, _config.First);

            EnterBarrier("after-1");
        }

        public void Cluster_of_2_nodes_must_not_be_singleton_cluster_when_joined_with_other_node()
        {
            AwaitClusterUp(_config.First, _config.Second);
            ClusterView.IsSingletonCluster.ShouldBeFalse();
            AssertLeader(_config.First, _config.Second);

            EnterBarrier("after-2");
        }

        public void Cluster_of_2_nodes_must_become_singleton_cluster_when_one_node_is_shutdown()
        {
            RunOn(() =>
            {
                var secondAddress = GetAddress(_config.Second);
                TestConductor.Exit(_config.Second, 0).Wait();

                MarkNodeAsUnavailable(secondAddress);

                AwaitMembersUp(1, ImmutableHashSet.Create<Address>(secondAddress), TimeSpan.FromSeconds(30));
                ClusterView.IsSingletonCluster.ShouldBeTrue();
                AwaitCondition(() => ClusterView.IsLeader);
            }, _config.First);

            EnterBarrier("after-3");
        }

        public void Cluster_of_2_nodes_must_leave_and_shutdown_itself_when_singleton_cluster()
        {
            RunOn(() =>
            {
                Cluster.Leave(GetAddress(_config.First));
                AwaitCondition(() => Cluster.IsTerminated, TimeSpan.FromSeconds(5));
            }, _config.First);

            EnterBarrier("after-4");
        }
    }
}
