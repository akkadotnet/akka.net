//-----------------------------------------------------------------------
// <copyright file="IndirectlyConnected3NodeSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.MultiNode.TestAdapter;
using Akka.Remote.TestKit;
using Akka.Remote.Transport;
using Akka.TestKit;
using FluentAssertions;

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
        public async Task IndirectlyConnected3NodeSpecTests()
        {
            await A_3_node_cluster_should_avoid_a_split_brain_when_two_unreachable_but_can_talk_via_third();
        }

        public async Task A_3_node_cluster_should_avoid_a_split_brain_when_two_unreachable_but_can_talk_via_third()
        {
            var cluster = Cluster.Get(Sys);

            // Set up termination signal using event-driven callback instead of polling
            // This must be set up BEFORE the cluster is partitioned
            var terminatedTcs = new TaskCompletionSource<Done>(TaskCreationOptions.RunContinuationsAsynchronously);
            cluster.RegisterOnMemberRemoved(() => terminatedTcs.TrySetResult(Done.Instance));

            RunOn(() =>
            {
                cluster.Join(cluster.SelfAddress);
            }, _config.Node1);
            await EnterBarrierAsync("node1 joined");
            RunOn(() =>
            {
                cluster.Join(Node(_config.Node1).Address);
            }, _config.Node2, _config.Node3);
            await WithinAsync(TimeSpan.FromSeconds(10), async () =>
            {
                await AwaitAssertAsync(() =>
                {
                    cluster.State.Members.Count.Should().Be(3);
                    foreach (var m in cluster.State.Members)
                    {
                        m.Status.Should().Be(MemberStatus.Up);
                    }
                });
            });
            await EnterBarrierAsync("Cluster formed");

            await RunOnAsync(async () =>
            {
                await TestConductor.BlackholeAsync(_config.Node2, _config.Node3, ThrottleTransportAdapter.Direction.Both);
            }, _config.Node1);
            await EnterBarrierAsync("Blackholed");

            await WithinAsync(TimeSpan.FromSeconds(10), async () =>
            {
                await AwaitAssertAsync(() =>
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
            await EnterBarrierAsync("unreachable");

            // Node1 waits for SBR to complete and verify it's the only surviving member
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

            // Nodes 2,3 wait for termination using the event-driven callback
            await RunOnAsync(async () =>
            {
                // Use event-driven notification via RegisterOnMemberRemoved
                // This is more reliable than polling cluster.IsTerminated because:
                // 1. The callback fires as soon as the member is removed/shutdown starts
                // 2. The callback also fires in PostStop if the cluster daemon is stopping
                // 3. No race between polling interval and actual state change
                var completed = await Task.WhenAny(
                    terminatedTcs.Task,
                    Task.Delay(TimeSpan.FromSeconds(20)));

                if (completed != terminatedTcs.Task)
                {
                    // Fallback check - the cluster should definitely be terminated by now
                    cluster.IsTerminated.Should().BeTrue(
                        "Cluster should be terminated - either via MemberRemoved callback or shutdown. " +
                        $"Current self member status: {cluster.SelfMember.Status}");
                }
            }, _config.Node2, _config.Node3);

            await EnterBarrierAsync("done");
        }
    }
}
