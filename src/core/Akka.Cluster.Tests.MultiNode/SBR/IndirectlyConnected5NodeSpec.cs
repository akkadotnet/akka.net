//-----------------------------------------------------------------------
// <copyright file="IndirectlyConnected5NodeSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.Remote.TestKit;
using Akka.Remote.Transport;
using Akka.TestKit;
using FluentAssertions;
using MultiNodeFactAttribute = Akka.MultiNode.TestAdapter.MultiNodeFactAttribute; 

namespace Akka.Cluster.Tests.MultiNode.SBR
{
    public class IndirectlyConnected5NodeSpecConfig : MultiNodeConfig
    {
        public RoleName Node1 { get; }
        public RoleName Node2 { get; }
        public RoleName Node3 { get; }
        public RoleName Node4 { get; }
        public RoleName Node5 { get; }


        public IndirectlyConnected5NodeSpecConfig()
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

    public class IndirectlyConnected5NodeSpec : MultiNodeClusterSpec
    {
        private readonly IndirectlyConnected5NodeSpecConfig _config;

        public IndirectlyConnected5NodeSpec()
            : this(new IndirectlyConnected5NodeSpecConfig())
        {
        }

        protected IndirectlyConnected5NodeSpec(IndirectlyConnected5NodeSpecConfig config)
            : base(config, typeof(IndirectlyConnected5NodeSpec))
        {
            _config = config;
        }

        [MultiNodeFact]
        public void IndirectlyConnected5NodeSpecTests()
        {
            A_5_node_cluster_should_avoid_a_split_brain_when_indirectly_connected_combined_with_clean_partition();
        }

        public void A_5_node_cluster_should_avoid_a_split_brain_when_indirectly_connected_combined_with_clean_partition()
        {
            var cluster = Cluster.Get(Sys);

            RunOn(() =>
            {
                cluster.Join(cluster.SelfAddress);
            }, _config.Node1);
            EnterBarrier("node1 joined");
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
            EnterBarrier("Cluster formed");

            RunOn(() =>
            {
                foreach (var x in new[] { _config.Node1, _config.Node2, _config.Node3 })
                {
                    foreach (var y in new[] { _config.Node4, _config.Node5 })
                    {
                        TestConductor.Blackhole(x, y, ThrottleTransportAdapter.Direction.Both).Wait();
                    }
                }

            }, _config.Node1);
            EnterBarrier("blackholed-clean-partition");

            RunOn(() =>
            {
                TestConductor.Blackhole(_config.Node2, _config.Node3, ThrottleTransportAdapter.Direction.Both).Wait();
            }, _config.Node1);
            EnterBarrier("blackholed-indirectly-connected");

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
            EnterBarrier("unreachable");

            RunOn(() =>
            {
                Within(TimeSpan.FromSeconds(15), () =>
                {
                    AwaitAssert(() =>
                    {
                        cluster.State.Members.Select(i => i.Address).Should().BeEquivalentTo(Node(_config.Node1).Address);
                        foreach (var m in cluster.State.Members)
                        {
                            m.Status.Should().Be(MemberStatus.Up);
                        }
                    });
                });
            }, _config.Node1);

            RunOn(() =>
            {
                // downed
                AwaitCondition(() => cluster.IsTerminated, max: TimeSpan.FromSeconds(15));
            }, _config.Node2, _config.Node3, _config.Node4, _config.Node5);

            EnterBarrier("done");
        }
    }
}

