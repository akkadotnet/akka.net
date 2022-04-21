//-----------------------------------------------------------------------
// <copyright file="DownAllUnstable5NodeSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2021 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2021 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.Remote.TestKit;
using Akka.Remote.Transport;
using Akka.TestKit;
using FluentAssertions;
using MultiNodeFactAttribute = Akka.MultiNode.TestAdapter.MultiNodeFactAttribute; 

namespace Akka.Cluster.Tests.MultiNode.SBR
{
    public class DownAllUnstable5NodeSpecConfig : MultiNodeConfig
    {
        public RoleName Node1 { get; }
        public RoleName Node2 { get; }
        public RoleName Node3 { get; }
        public RoleName Node4 { get; }
        public RoleName Node5 { get; }


        public DownAllUnstable5NodeSpecConfig()
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
                        failure-detector.acceptable-heartbeat-pause = 3s
                        split-brain-resolver.active-strategy = keep-majority
                        split-brain-resolver.stable-after = 10s
                        split-brain-resolver.down-all-when-unstable = 7s

                        run-coordinated-shutdown-when-down = off
                    }

                    # quicker reconnect
                    remote.retry-gate-closed-for = 1s
                    remote.netty.tcp.connection-timeout = 3 s

                    actor.provider = cluster

                    test.filter-leeway = 10s
                }")
                .WithFallback(MultiNodeLoggingConfig.LoggingConfig)
                .WithFallback(DebugConfig(true))
                .WithFallback(MultiNodeClusterSpec.ClusterConfig());

            TestTransport = true;
        }
    }

    public class DownAllUnstable5NodeSpec : MultiNodeClusterSpec
    {
        private readonly DownAllUnstable5NodeSpecConfig _config;

        public DownAllUnstable5NodeSpec()
            : this(new DownAllUnstable5NodeSpecConfig())
        {
        }

        protected DownAllUnstable5NodeSpec(DownAllUnstable5NodeSpecConfig config)
            : base(config, typeof(DownAllUnstable5NodeSpec))
        {
            _config = config;
        }

        [MultiNodeFact]
        public void DownAllUnstable5NodeSpecTests()
        {
            A_5_node_cluster_with_down_all_when_unstable_should_down_all_when_instability_continues();
        }

        public void A_5_node_cluster_with_down_all_when_unstable_should_down_all_when_instability_continues()
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

            // acceptable-heartbeat-pause = 3s
            // stable-after = 10s
            // down-all-when-unstable = 7s

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

            Within(TimeSpan.FromSeconds(10), () =>
            {
                AwaitAssert(() =>
                {
                    RunOn(() =>
                    {
                        cluster.State.Unreachable.Select(i => i.Address).Should().BeEquivalentTo(new[] { _config.Node4, _config.Node5 }.Select(i => Node(i).Address));
                    }, _config.Node1, _config.Node2, _config.Node3);
                    RunOn(() =>
                    {
                        cluster.State.Unreachable.Select(i => i.Address).Should().BeEquivalentTo(new[] { _config.Node1, _config.Node2, _config.Node3 }.Select(i => Node(i).Address));
                    }, _config.Node4, _config.Node5);
                });
            });
            EnterBarrier("unreachable-clean-partition");

            // no decision yet
            Thread.Sleep(2000);
            cluster.State.Members.Count.Should().Be(5);
            foreach (var m in cluster.State.Members)
            {
                m.Status.Should().Be(MemberStatus.Up);
            }

            RunOn(() =>
            {
                TestConductor.Blackhole(_config.Node2, _config.Node3, ThrottleTransportAdapter.Direction.Both).Wait();
            }, _config.Node1);
            EnterBarrier("blackhole-2");
            // then it takes about 5 seconds for failure detector to observe that
            Thread.Sleep(7000);

            RunOn(() =>
            {
                TestConductor.PassThrough(_config.Node2, _config.Node3, ThrottleTransportAdapter.Direction.Both).Wait();
            }, _config.Node1);
            EnterBarrier("passThrough-2");

            // now it should have been unstable for more than 17 seconds

            // all downed
            AwaitCondition(() => cluster.IsTerminated, max: TimeSpan.FromSeconds(15));

            EnterBarrier("done");
        }
    }
}

