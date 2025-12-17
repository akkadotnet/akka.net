//-----------------------------------------------------------------------
// <copyright file="SingletonClusterSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.MultiNode.TestAdapter;
using Akka.Remote.TestKit;
using Akka.TestKit;

namespace Akka.Cluster.Tests.MultiNode
{
    public class SingletonClusterConfig : MultiNodeConfig
    {
        public RoleName First { get; }
        public RoleName Second { get; }

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

    public class SingletonClusterWithFailureDetectorPuppetMultiNode : SingletonClusterSpec
    {
        public SingletonClusterWithFailureDetectorPuppetMultiNode() : base(true, typeof(SingletonClusterWithFailureDetectorPuppetMultiNode))
        {
        }
    }

    public class SingletonClusterWithAccrualFailureDetectorMultiNode : SingletonClusterSpec
    {
        public SingletonClusterWithAccrualFailureDetectorMultiNode() : base(false, typeof(SingletonClusterWithAccrualFailureDetectorMultiNode))
        {
        }
    }

    public abstract class SingletonClusterSpec : MultiNodeClusterSpec
    {
        private readonly SingletonClusterConfig _config;

        protected SingletonClusterSpec(bool failureDetectorPuppet, Type type) : this(new SingletonClusterConfig(failureDetectorPuppet), type)
        {
        }

        protected SingletonClusterSpec(SingletonClusterConfig config, Type type) : base(config, type)
        {
            _config = config;
        }

        [MultiNodeFact]
        public async Task SingletonClusterSpecs()
        {
            await Cluster_of_2_nodes_must_become_singleton_cluster_when_started_with_seednodes();
            await Cluster_of_2_nodes_must_not_be_singleton_cluster_when_joined_with_other_node();
            await Cluster_of_2_nodes_must_become_singleton_cluster_when_one_node_is_shutdown();
            await Cluster_of_2_nodes_must_leave_and_shutdown_itself_when_singleton_cluster();
        }

        public async Task Cluster_of_2_nodes_must_become_singleton_cluster_when_started_with_seednodes()
        {
            await RunOnAsync(async () =>
            {
                var nodes = ImmutableList.Create(GetAddress(_config.First));
                Cluster.JoinSeedNodes(nodes);
                await AwaitMembersUpAsync(1);
                ClusterView.IsSingletonCluster.ShouldBeTrue();
            }, _config.First);

            await EnterBarrierAsync("after-1");
        }

        public async Task Cluster_of_2_nodes_must_not_be_singleton_cluster_when_joined_with_other_node()
        {
            await AwaitClusterUpAsync(CancellationToken.None, _config.First, _config.Second);
            ClusterView.IsSingletonCluster.ShouldBeFalse();
            AssertLeader(_config.First, _config.Second);

            await EnterBarrierAsync("after-2");
        }

        public async Task Cluster_of_2_nodes_must_become_singleton_cluster_when_one_node_is_shutdown()
        {
            await RunOnAsync(async () =>
            {
                var secondAddress = GetAddress(_config.Second);
                await TestConductor.ExitAsync(_config.Second, 0);

                MarkNodeAsUnavailable(secondAddress);

                await AwaitMembersUpAsync(1, ImmutableHashSet.Create(secondAddress), TimeSpan.FromSeconds(30));
                ClusterView.IsSingletonCluster.ShouldBeTrue();
                await AwaitConditionAsync(() => ClusterView.IsLeader);
            }, _config.First);

            await EnterBarrierAsync("after-3");
        }

        public async Task Cluster_of_2_nodes_must_leave_and_shutdown_itself_when_singleton_cluster()
        {
            await RunOnAsync(async () =>
            {
                Cluster.Leave(GetAddress(_config.First));
                await AwaitConditionAsync(() => Task.FromResult(Cluster.IsTerminated), TimeSpan.FromSeconds(5));
            }, _config.First);

            await EnterBarrierAsync("after-4");
        }
    }
}
