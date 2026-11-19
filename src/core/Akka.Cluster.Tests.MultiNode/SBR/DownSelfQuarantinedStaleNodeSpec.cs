//-----------------------------------------------------------------------
// <copyright file="DownSelfQuarantinedStaleNodeSpec.cs" company="Akka.NET Project">
//     Copyright (C) 2009-2022 Lightbend Inc. <http://www.lightbend.com>
//     Copyright (C) 2013-2025 .NET Foundation <https://github.com/akkadotnet/akka.net>
// </copyright>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Akka.Cluster.TestKit;
using Akka.Configuration;
using Akka.MultiNode.TestAdapter;
using Akka.Remote.TestKit;
using Akka.Remote.Transport;
using FluentAssertions;

namespace Akka.Cluster.Tests.MultiNode.SBR
{
    public class DownSelfQuarantinedStaleNodeSpecConfig : MultiNodeConfig
    {
        public RoleName First { get; }
        public RoleName Second { get; }
        public RoleName Third { get; }
        public RoleName Fourth { get; }

        public DownSelfQuarantinedStaleNodeSpecConfig()
        {
            First = Role("first");
            Second = Role("second");
            Third = Role("third");
            Fourth = Role("fourth");

            CommonConfig = ConfigurationFactory.ParseString(@"
                akka {
                    loglevel = INFO
                    cluster {
                        downing-provider-class = ""Akka.Cluster.SBR.SplitBrainResolverProvider""
                        split-brain-resolver.active-strategy = keep-majority
                        split-brain-resolver.stable-after = 5s
                    }
                    remote.retry-gate-closed-for = 1s

                    # The blackhole also swallows the peers closing their connections to the stale node, so
                    # without this the stale node keeps its old associations for the default 120 s pause and
                    # never makes the new handshake that gets refused. A real paused process still has its
                    # sockets closed by the OS, so it reconnects as soon as it wakes.
                    remote.transport-failure-detector.heartbeat-interval = 1s
                    remote.transport-failure-detector.acceptable-heartbeat-pause = 3s

                    actor.provider = cluster
                    test.filter-leeway = 10s
                }")
                .WithFallback(MultiNodeLoggingConfig.LoggingConfig)
                .WithFallback(DebugConfig(false))
                .WithFallback(MultiNodeClusterSpec.ClusterConfig());

            // fourth stands in for a frozen node: its own SBR must not reach a decision while it is cut off,
            // so the only thing that can take it down after it reconnects is being quarantined by the others
            NodeConfig(new List<RoleName> { Fourth }, new List<Config>
            {
                ConfigurationFactory.ParseString("akka.cluster.split-brain-resolver.stable-after = 120s")
            });

            TestTransport = true;
        }
    }

    /// <summary>
    /// A node that the rest of the cluster downed and removed while it was cut off must down itself once it
    /// reconnects and learns that the others have quarantined it, instead of running on as a cluster of its own.
    /// See #9002.
    /// </summary>
    public class DownSelfQuarantinedStaleNodeSpec : MultiNodeClusterSpec
    {
        private readonly DownSelfQuarantinedStaleNodeSpecConfig _config;

        public DownSelfQuarantinedStaleNodeSpec()
            : this(new DownSelfQuarantinedStaleNodeSpecConfig())
        {
        }

        protected DownSelfQuarantinedStaleNodeSpec(DownSelfQuarantinedStaleNodeSpecConfig config)
            : base(config, typeof(DownSelfQuarantinedStaleNodeSpec))
        {
            _config = config;
        }

        [MultiNodeFact]
        public async Task Should_DownSelf_When_RemovedByClusterWhileCutOff()
        {
            var majority = new[] { _config.First, _config.Second, _config.Third };

            await AwaitClusterUpAsync(default, _config.First, _config.Second, _config.Third, _config.Fourth);
            await EnterBarrierAsync("cluster-up");

            await RunOnAsync(async () =>
            {
                foreach (var role in majority)
                    await TestConductor.BlackholeAsync(_config.Fourth, role, ThrottleTransportAdapter.Direction.Both);
            }, _config.First);
            await EnterBarrierAsync("blackholed");

            // the majority downs and removes fourth
            await RunOnAsync(async () =>
            {
                await WithinAsync(TimeSpan.FromSeconds(30), async () =>
                {
                    await AwaitAssertAsync(() =>
                    {
                        Cluster.State.Members.Select(m => m.Address)
                            .Should().BeEquivalentTo(majority.Select(GetAddress));
                    });
                });
            }, majority);
            await EnterBarrierAsync("fourth-removed");

            // fourth must still be up here: its own SBR is waiting out its long stable-after
            await RunOnAsync(() =>
            {
                Cluster.IsTerminated.Should().BeFalse();
                return Task.CompletedTask;
            }, _config.Fourth);
            await EnterBarrierAsync("fourth-still-up");

            await RunOnAsync(async () =>
            {
                foreach (var role in majority)
                    await TestConductor.PassThroughAsync(_config.Fourth, role, ThrottleTransportAdapter.Direction.Both);
            }, _config.First);
            await EnterBarrierAsync("reconnected");

            await RunOnAsync(async () =>
            {
                // well within fourth's own stable-after: only the quarantine can explain this
                await WithinAsync(TimeSpan.FromSeconds(30), async () =>
                {
                    await AwaitAssertAsync(() => Cluster.IsTerminated.Should().BeTrue(),
                        interval: TimeSpan.FromMilliseconds(250));
                });
            }, _config.Fourth);

            await RunOnAsync(() =>
            {
                Cluster.IsTerminated.Should().BeFalse();
                Cluster.State.Members.Select(m => m.Address).Should().BeEquivalentTo(majority.Select(GetAddress));
                return Task.CompletedTask;
            }, majority);

            await EnterBarrierAsync("done");
        }
    }
}
