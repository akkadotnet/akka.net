//-----------------------------------------------------------------------
// <copyright file="DownAllIndirectlyConnected5NodeSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.MultiNode.TestAdapter;
using Akka.Remote.TestKit;
using Akka.Remote.Transport;
using Akka.TestKit;
using FluentAssertions;

namespace Akka.Cluster.Tests.MultiNode.SBR
{
    public class DownAllIndirectlyConnected5NodeSpecConfig : MultiNodeConfig
    {
        public RoleName Node1 { get; }
        public RoleName Node2 { get; }
        public RoleName Node3 { get; }
        public RoleName Node4 { get; }
        public RoleName Node5 { get; }


        public DownAllIndirectlyConnected5NodeSpecConfig()
        {
            Node1 = Role("node1");
            Node2 = Role("node2");
            Node3 = Role("node3");
            Node4 = Role("node4");
            Node5 = Role("node5");

            CommonConfig = ConfigurationFactory.ParseString(@"

                akka {
                    loglevel = INFO
                    cluster {
                    downing-provider-class = ""Akka.Cluster.SBR.SplitBrainResolverProvider""
                        split-brain-resolver.active-strategy = keep-majority
                        split-brain-resolver.stable-after = 6s

                        run-coordinated-shutdown-when-down = off
                    }

                    actor.provider = cluster

                    test.filter-leeway = 10s
                }")
                .WithFallback(MultiNodeLoggingConfig.LoggingConfig)
                .WithFallback(DebugConfig(true))
                .WithFallback(MultiNodeClusterSpec.ClusterConfig());

            TestTransport = true;
        }
    }

    public class DownAllIndirectlyConnected5NodeSpec : MultiNodeClusterSpec
    {
        private readonly DownAllIndirectlyConnected5NodeSpecConfig _config;

        public DownAllIndirectlyConnected5NodeSpec()
            : this(new DownAllIndirectlyConnected5NodeSpecConfig())
        {
        }

        protected DownAllIndirectlyConnected5NodeSpec(DownAllIndirectlyConnected5NodeSpecConfig config)
            : base(config, typeof(DownAllIndirectlyConnected5NodeSpec))
        {
            _config = config;
        }

        [MultiNodeFact]
        public async Task DownAllIndirectlyConnected5NodeSpecTests()
        {
            await A_5_node_cluster_with_keep_one_indirectly_connected_off_should_down_all_when_indirectly_connected_combined_with_clean_partition();
        }

        public async Task A_5_node_cluster_with_keep_one_indirectly_connected_off_should_down_all_when_indirectly_connected_combined_with_clean_partition()
        {
            var cluster = Cluster.Get(Sys);

            RunOn(() =>
            {
                cluster.Join(cluster.SelfAddress);
            }, _config.Node1);
            await EnterBarrierAsync("node1 joined");
            RunOn(() =>
            {
                cluster.Join(Node(_config.Node1).Address);
            }, _config.Node2, _config.Node3, _config.Node4, _config.Node5);
            Within(TimeSpan.FromSeconds(10), () =>
            {
                AwaitAssert(() =>
                {
                    cluster.State.Members.Count.Should().Be(5);
                    foreach (var m in cluster.State.Members)
                    {
                        m.Status.Should().Be(MemberStatus.Up);
                    }
                });
            });
            await EnterBarrierAsync("Cluster formed");

            await RunOnAsync(async () =>
            {

                foreach (var x in new[] { _config.Node1, _config.Node2, _config.Node3 })
                {
                    foreach (var y in new[] { _config.Node4, _config.Node5 })
                    {
                        await TestConductor.BlackholeAsync(x, y, ThrottleTransportAdapter.Direction.Both);
                    }
                }
            }, _config.Node1);
            await EnterBarrierAsync("blackholed-clean-partition");

            await RunOnAsync(async () =>
            {
                await TestConductor.BlackholeAsync(_config.Node2, _config.Node3, ThrottleTransportAdapter.Direction.Both);
            }, _config.Node1);
            await EnterBarrierAsync("blackholed-indirectly-connected");

            Within(TimeSpan.FromSeconds(10), () =>
            {
                AwaitAssert(() =>
                {
                    RunOn(() =>
                    {
                        cluster.State.Unreachable.Select(i => i.Address).Should().BeEquivalentTo(new[] { _config.Node2, _config.Node3, _config.Node4, _config.Node5 }.Select(i => Node(i).Address));
                    }, _config.Node1);
                    RunOn(() =>
                    {
                        cluster.State.Unreachable.Select(i => i.Address).Should().BeEquivalentTo(new[] { _config.Node3, _config.Node4, _config.Node5 }.Select(i => Node(i).Address));
                    }, _config.Node2);
                    RunOn(() =>
                    {
                        cluster.State.Unreachable.Select(i => i.Address).Should().BeEquivalentTo(new[] { _config.Node2, _config.Node4, _config.Node5 }.Select(i => Node(i).Address));
                    }, _config.Node3);
                    RunOn(() =>
                    {
                        cluster.State.Unreachable.Select(i => i.Address).Should().BeEquivalentTo(new[] { _config.Node1, _config.Node2, _config.Node3 }.Select(i => Node(i).Address));
                    }, _config.Node4, _config.Node5);
                });
            });
            await EnterBarrierAsync("unreachable");

            await RunOnAsync(async () =>
            {
                await WithinAsync(TimeSpan.FromSeconds(15), async () =>
                {
                    await AwaitAssertAsync(() =>
                    {
                        cluster.State.Members.Select(i => i.Address).Should().BeEquivalentTo(Node(_config.Node1).Address);
                        foreach (var m in cluster.State.Members)
                        {
                            m.Status.Should().Be(MemberStatus.Up);
                        }
                    });
                });
            }, _config.Node1);

            await RunOnAsync(async () =>
            {
                // downed
                await AwaitConditionAsync(() => Task.FromResult(cluster.IsTerminated), max: TimeSpan.FromSeconds(15));
            }, _config.Node2, _config.Node3, _config.Node4, _config.Node5);

            await EnterBarrierAsync("done");
        }
    }
}
