//-----------------------------------------------------------------------
// <copyright file="IndirectlyConnected3NodeSpec.cs" company="Akka.NET Project">
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
    public class IndirectlyConnected3NodeSpecConfig : MultiNodeConfig
    {
        public RoleName Node1 { get; }
        public RoleName Node2 { get; }
        public RoleName Node3 { get; }

        public IndirectlyConnected3NodeSpecConfig()
        {
            Node1 = Role("node1");
            Node2 = Role("node2");
            Node3 = Role("node3");

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

    public class IndirectlyConnected3NodeSpec : MultiNodeClusterSpec
    {
        private readonly IndirectlyConnected3NodeSpecConfig _config;

        public IndirectlyConnected3NodeSpec()
            : this(new IndirectlyConnected3NodeSpecConfig())
        {
        }

        protected IndirectlyConnected3NodeSpec(IndirectlyConnected3NodeSpecConfig config)
            : base(config, typeof(IndirectlyConnected3NodeSpec))
        {
            _config = config;
        }

        [MultiNodeFact]
        public void IndirectlyConnected3NodeSpecTests()
        {
            A_3_node_cluster_should_avoid_a_split_brain_when_two_unreachable_but_can_talk_via_third();
        }

        public void A_3_node_cluster_should_avoid_a_split_brain_when_two_unreachable_but_can_talk_via_third()
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
            }, _config.Node2, _config.Node3);
            Within(TimeSpan.FromSeconds(10), () =>
            {
                AwaitAssert(() =>
                {
                    cluster.State.Members.Count.Should().Be(3);
                    foreach (var m in cluster.State.Members)
                    {
                        m.Status.Should().Be(MemberStatus.Up);
                    }
                });
            });
            EnterBarrier("Cluster formed");

            RunOn(() =>
            {
                TestConductor.Blackhole(_config.Node2, _config.Node3, ThrottleTransportAdapter.Direction.Both).Wait();
            }, _config.Node1);
            EnterBarrier("Blackholed");

            Within(TimeSpan.FromSeconds(10), () =>
            {
                AwaitAssert(() =>
                {
                    RunOn(() =>
                    {
                        cluster.State.Unreachable.Select(i => i.Address).Should().BeEquivalentTo(Node(_config.Node2).Address);
                    }, _config.Node3);
                    RunOn(() =>
                    {
                        cluster.State.Unreachable.Select(i => i.Address).Should().BeEquivalentTo(Node(_config.Node3).Address);
                    }, _config.Node2);
                    RunOn(() =>
                    {
                        cluster.State.Unreachable.Select(i => i.Address).Should().BeEquivalentTo(new[] { _config.Node3, _config.Node2 }.Select(i => Node(i).Address));
                    }, _config.Node1);
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
            }, _config.Node2, _config.Node3);

            EnterBarrier("done");
        }
    }
}
