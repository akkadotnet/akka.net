//-----------------------------------------------------------------------
// <copyright file="ClusterMetricsExtensionSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2020 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2020 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading.Tasks;
using Akka.Cluster.Metrics.Tests.Helpers;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.Remote.TestKit;
using FluentAssertions;
using Akka.Configuration;
using ConfigurationFactory = Akka.Configuration.ConfigurationFactory;

namespace Akka.Cluster.Metrics.Tests.MultiNode
{
    public class ClusterMetricsCommonConfig : MultiNodeConfig
    {
        public readonly RoleName Node1;
        public readonly RoleName Node2;
        public readonly RoleName Node3;
        public readonly RoleName Node4;
        public readonly RoleName Node5;

        public Config EnableMetricsExtension => ConfigurationFactory.ParseString(@"
            akka.extensions=[""Akka.Cluster.Metrics.ClusterMetricsExtensionProvider, Akka.Cluster.Metrics""]
            akka.cluster.metrics.collector.enabled = on
        ");
        
        public Config DisableMetricsExtension => ConfigurationFactory.ParseString(@"
            akka.extensions=[""Akka.Cluster.Metrics.ClusterMetricsExtensionProvider, Akka.Cluster.Metrics""]
            akka.cluster.metrics.collector.enabled = off
        ");

        public ClusterMetricsCommonConfig()
        {
            Node1 = Role("node-1");
            Node2 = Role("node-2");
            Node3 = Role("node-3");
            Node4 = Role("node-4");
            Node5 = Role("node-5");
        }
    }

    public class ClusterMetricsDisabledConfig : ClusterMetricsCommonConfig
    {
        public ClusterMetricsDisabledConfig()
        {
            CommonConfig = DisableMetricsExtension
                .WithFallback(DebugConfig(on: false))
                .WithFallback(MultiNodeClusterSpec.ClusterConfigWithFailureDetectorPuppet());
        }
    }

    public class ClusterMetricsEnabledConfig : ClusterMetricsCommonConfig
    {
        public ClusterMetricsEnabledConfig()
        {
            CommonConfig = EnableMetricsExtension
                .WithFallback(DebugConfig(on: false))
                .WithFallback(MultiNodeClusterSpec.ClusterConfigWithFailureDetectorPuppet());
        }
    }

    public class ClusterMetricsEnabledSpec : MultiNodeClusterSpec
    {
        private readonly ClusterMetricsCommonConfig _config;
        public ClusterMetricsView MetricsView { get; }

        private ClusterMetricsEnabledSpec(ClusterMetricsCommonConfig config) : base(config, typeof(ClusterMetricsEnabledSpec))
        {
            _config = config;
            MetricsView = new ClusterMetricsView(Cluster.System);
        }
        
        public ClusterMetricsEnabledSpec() : this(new ClusterMetricsEnabledConfig())
        {
        }

        [MultiNodeFact]
        public async Task ClusterMetrics_Should_work_when_enabled()
        {
            await Should_collect_and_publish_metrics_and_gossip_them_around_the_node_ring();
            await Should_reflect_the_correct_number_or_node_metrics_in_cluster_view();
        }

        private async Task Should_collect_and_publish_metrics_and_gossip_them_around_the_node_ring()
        {
            await AwaitAssertAsync(() =>
            {
                AwaitClusterUp(Roles.ToArray());
            }, TimeSpan.FromSeconds(30));
                
            EnterBarrier("cluster_started");
            await AwaitAssertAsync(() =>
            {
                Cluster.State.Members.Count(m => m.Status == MemberStatus.Up).Should().Be(Roles.Count);
            }, TimeSpan.FromSeconds(30));
                
            await AwaitAssertAsync(() =>
            {
                MetricsView.ClusterMetrics.Count.Should().Be(Roles.Count);
            }, TimeSpan.FromSeconds(30));
            
            await WithinAsync(10.Seconds(), async () =>
            {
                var collector = new MetricsCollectorBuilder().Build(Cluster.System);
                collector.Sample().Metrics.Count.Should().BeGreaterThan(3);
                EnterBarrier("after");
            });
        }

        private async Task Should_reflect_the_correct_number_or_node_metrics_in_cluster_view()
        {
            await WithinAsync(30.Seconds(), async () =>
            {
                RunOn(() =>
                {
                    Cluster.Leave(Node(_config.Node1).Address);
                }, _config.Node2);
                
                EnterBarrier("first-left");
                
                await RunOnAsync(async () =>
                {
                    MarkNodeAsUnavailable(Node(_config.Node1).Address);
                    await AwaitAssertAsync(() => MetricsView.ClusterMetrics.Count.Should().Be(Roles.Count - 1));
                }, _config.Node2, _config.Node3, _config.Node4, _config.Node5);
                
                EnterBarrier("finished");
            });
        }
    }

    public class ClusterMetricsDisabledSpec : MultiNodeClusterSpec
    {
        public ClusterMetricsView MetricsView { get; }
        
        private ClusterMetricsDisabledSpec(ClusterMetricsDisabledConfig config) : base(config, typeof(ClusterMetricsDisabledSpec))
        {
            MetricsView = new ClusterMetricsView(Cluster.System);
        }

        public ClusterMetricsDisabledSpec() : this(new ClusterMetricsDisabledConfig())
        {
        }

        [MultiNodeFact]
        public void ClusterMetrics_Should_not_collect_publish_and_gossip_metrics_when_disabled()
        {
            AwaitClusterUp(Roles.ToArray());
            MetricsView.ClusterMetrics.Count.Should().Be(0);
            ClusterMetrics.Get(Sys).Subscribe(TestActor);
            ExpectNoMsg();
            MetricsView.ClusterMetrics.Count.Should().Be(0);
            EnterBarrier("after");
        }
    }
}
